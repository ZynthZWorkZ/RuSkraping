using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using RuSkraping.RUSKTorrent;
using RuSkraping.Models;

namespace RuSkraping;

/// <summary>
/// Interaction logic for DownloadManager.xaml
/// </summary>
public partial class DownloadManager : Window
{
    private ObservableCollection<TorrentDownload> _torrents;
    private TorrentEngine _torrentEngine;

    public DownloadManager()
    {
        try
        {
            ErrorLogger.LogMessage("DownloadManager: Initialization started", "INFO");
            
            InitializeComponent();
            
            ErrorLogger.LogMessage("DownloadManager: XAML initialized", "INFO");
            
            _torrents = new ObservableCollection<TorrentDownload>();
            TorrentsListView.ItemsSource = _torrents;
            
            ErrorLogger.LogMessage("DownloadManager: Creating TorrentEngine", "INFO");
            
            // Initialize BitTorrent engine
            _torrentEngine = new TorrentEngine();
            
            ErrorLogger.LogMessage("DownloadManager: TorrentEngine created successfully", "INFO");
            
            // Subscribe to engine events
            _torrentEngine.TorrentAdded += OnTorrentAdded;
            _torrentEngine.TorrentRemoved += OnTorrentRemoved;
            _torrentEngine.TorrentUpdated += OnTorrentUpdated;
            
            StatusText.Text = "RUSKTorrent Engine Ready - Full Download Capability!";
            
            ErrorLogger.LogMessage("DownloadManager: Initialization completed successfully", "INFO");
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, "DownloadManager Constructor");
            MessageBox.Show(
                $"Failed to initialize Download Manager:\n\n{ex.Message}\n\n" +
                $"Check log file: {ErrorLogger.GetLogFilePath()}",
                "Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OnTorrentAdded(object? sender, TorrentDownload torrent)
    {
        Dispatcher.Invoke(() =>
        {
            _torrents.Add(torrent);
            StatusText.Text = $"Added: {torrent.Name}";
        });
    }

    private void OnTorrentRemoved(object? sender, TorrentDownload torrent)
    {
        Dispatcher.Invoke(() =>
        {
            _torrents.Remove(torrent);
            StatusText.Text = $"Removed: {torrent.Name}";
        });
    }

    private void OnTorrentUpdated(object? sender, TorrentDownload torrent)
    {
        Dispatcher.Invoke(() =>
        {
            // Find the torrent in the collection and update it
            var existingTorrent = _torrents.FirstOrDefault(t => t.InfoHash == torrent.InfoHash);
            if (existingTorrent != null)
            {
                // The observable properties should automatically update the UI
                StatusText.Text = $"Updated: {torrent.Name} - {torrent.StateText} - {torrent.ProgressFormatted}";
            }
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        
        // Dispose engine
        _torrentEngine?.Dispose();
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void AddTorrentButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Torrent files (*.torrent)|*.torrent|All files (*.*)|*.*",
            Title = "Select Torrent File"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                AddTorrentButton.IsEnabled = false;
                StatusText.Text = "Parsing torrent file...";

                // Add torrent to engine
                ErrorLogger.LogMessage($"[DownloadManager] Loading torrent file: {openFileDialog.FileName}", "INFO");
                var torrent = await _torrentEngine.AddTorrentFromFileAsync(openFileDialog.FileName);
                ErrorLogger.LogMessage($"[DownloadManager] Torrent loaded successfully: {torrent.Name}", "INFO");

                // Show success message with metadata
                string message = $"✅ Torrent Loaded Successfully!\n\n" +
                                $"Name: {torrent.Name}\n" +
                                $"Size: {torrent.TotalSizeFormatted}\n" +
                                $"Files: {torrent.Files.Count}\n" +
                                $"Info Hash: {torrent.InfoHash}\n\n" +
                                $"Ready to download! Select the torrent and click 'Start' to begin.";

                MessageBox.Show(message, "Torrent Added", MessageBoxButton.OK, MessageBoxImage.Information);
                
                StatusText.Text = $"Loaded: {torrent.Name}";
            }
            catch (Exception ex)
            {
                ErrorLogger.LogException(ex, "[DownloadManager] AddTorrentButton_Click");
                MessageBox.Show($"Failed to add torrent:\n\n{ex.Message}\n\nCheck errors.log for details.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to load torrent";
            }
            finally
            {
                AddTorrentButton.IsEnabled = true;
            }
        }
    }

    private async void AddMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        var inputDialog = new MagnetInputDialog();
        inputDialog.Owner = this;
        
        if (inputDialog.ShowDialog() == true)
        {
            string magnetLink = inputDialog.MagnetLink;
            
            try
            {
                AddMagnetButton.IsEnabled = false;
                StatusText.Text = "Parsing magnet link...";

                // Add magnet to engine
                var torrent = await _torrentEngine.AddTorrentFromMagnetAsync(magnetLink);

                // Show success message with magnet metadata
                string message = $"🧲 Magnet Link Loaded Successfully!\n\n" +
                                $"Name: {torrent.Name}\n" +
                                $"Info Hash: {torrent.InfoHash}\n";
                
                if (torrent.TotalSize > 0)
                {
                    message += $"Size: {torrent.TotalSizeFormatted}\n";
                }

                message += $"\nThe magnet link has been parsed and loaded.\n" +
                          $"Note: Magnet downloads require DHT (not yet implemented).\n" +
                          $"For now, use .torrent files to test downloading.";

                MessageBox.Show(message, "Magnet Added", MessageBoxButton.OK, MessageBoxImage.Information);
                
                StatusText.Text = $"Loaded magnet: {torrent.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add magnet link:\n\n{ex.Message}\n\nMake sure the magnet link is valid.\nFormat: magnet:?xt=urn:btih:...", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to load magnet link";
            }
            finally
            {
                AddMagnetButton.IsEnabled = true;
            }
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (TorrentsListView.SelectedItem is TorrentDownload torrent)
        {
            try
            {
                StartButton.IsEnabled = false;
                
                ErrorLogger.LogMessage($"[DownloadManager] User clicked Start for torrent: {torrent.Name}", "INFO");
                
                // Determine initial path for dialog
                string initialPath;
                try
                {
                    // Try to use user's Downloads folder
                    initialPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Downloads"
                    );
                    
                    // If Downloads doesn't exist, use Documents
                    if (!System.IO.Directory.Exists(initialPath))
                    {
                        initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }
                    
                    ErrorLogger.LogMessage($"[DownloadManager] Initial dialog path: {initialPath}", "INFO");
                }
                catch
                {
                    // Fallback to user profile
                    initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                
                // Ask user where to save the torrent
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = $"Choose where to save: {torrent.Name}",
                    ShowNewFolderButton = true,
                    SelectedPath = initialPath
                };

                ErrorLogger.LogMessage($"[DownloadManager] Showing folder browser dialog...", "INFO");
                var dialogResult = folderDialog.ShowDialog();
                
                if (dialogResult != System.Windows.Forms.DialogResult.OK)
                {
                    ErrorLogger.LogMessage($"[DownloadManager] User cancelled save location dialog", "INFO");
                    StatusText.Text = "Download cancelled - no save location selected";
                    return;
                }

                // Update save path
                string selectedPath = folderDialog.SelectedPath;
                ErrorLogger.LogMessage($"[DownloadManager] User selected save path: {selectedPath}", "INFO");
                
                // Create subdirectory for the torrent if it doesn't exist
                string savePath = System.IO.Path.Combine(selectedPath, SanitizeFolderName(torrent.Name));
                torrent.SavePath = savePath;
                
                ErrorLogger.LogMessage($"[DownloadManager] Final save path: {savePath}", "INFO");
                
                StatusText.Text = $"Starting download: {torrent.Name}...";
                await _torrentEngine.StartTorrentAsync(torrent);
                StatusText.Text = $"Downloading: {torrent.Name}";
            }
            catch (Exception ex)
            {
                ErrorLogger.LogException(ex, "[DownloadManager] StartButton_Click");
                MessageBox.Show($"Failed to start torrent:\n\n{ex.Message}\n\nCheck errors.log for details.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Error starting download";
            }
            finally
            {
                StartButton.IsEnabled = true;
            }
        }
        else
        {
            MessageBox.Show("Please select a torrent first.", "No Selection", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string SanitizeFolderName(string name)
    {
        // Remove invalid characters from folder name
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (TorrentsListView.SelectedItem is TorrentDownload torrent)
        {
            try
            {
                await _torrentEngine.PauseTorrentAsync(torrent);
                StatusText.Text = $"Paused: {torrent.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to pause torrent:\n{ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (TorrentsListView.SelectedItem is TorrentDownload torrent)
        {
            try
            {
                await _torrentEngine.StopTorrentAsync(torrent);
                StatusText.Text = $"Stopped: {torrent.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop torrent:\n{ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (TorrentsListView.SelectedItem is TorrentDownload torrent)
        {
            var result = MessageBox.Show(
                $"Remove '{torrent.Name}'?\n\nDo you want to delete the downloaded data as well?\n(Currently no data to delete - Phase 0)",
                "Remove Torrent",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes || result == MessageBoxResult.No)
            {
                bool deleteData = result == MessageBoxResult.Yes;
                
                try
                {
                    await _torrentEngine.RemoveTorrentAsync(torrent, deleteData);
                    StatusText.Text = deleteData 
                        ? $"Removed {torrent.Name} (and would delete data if any existed)" 
                        : $"Removed {torrent.Name}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to remove torrent:\n{ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void TorrentsListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        bool hasSelection = TorrentsListView.SelectedItem != null;
        
        StartButton.IsEnabled = hasSelection;
        PauseButton.IsEnabled = hasSelection;
        StopButton.IsEnabled = hasSelection;
        RemoveButton.IsEnabled = hasSelection;
        ViewDetailsButton.IsEnabled = hasSelection;
    }

    private void TorrentsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TorrentsListView.SelectedItem is TorrentDownload torrent)
        {
            ShowTorrentDetails(torrent);
        }
    }

    private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (TorrentsListView.SelectedItem is TorrentDownload torrent)
        {
            ShowTorrentDetails(torrent);
        }
    }

    private void ShowTorrentDetails(TorrentDownload torrent)
    {
        var detailsWindow = new TorrentDetailsWindow(torrent);
        detailsWindow.Owner = this;
        detailsWindow.ShowDialog();
    }

    /// <summary>
    /// Public method to add magnet from external sources (like MainWindow)
    /// </summary>
    public async void AddMagnet(string magnetUri)
    {
        try
        {
            StatusText.Text = "Loading magnet link...";
            var torrent = await _torrentEngine.AddTorrentFromMagnetAsync(magnetUri);
            StatusText.Text = $"Loaded magnet: {torrent.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load magnet link:\n\n{ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to load magnet";
        }
    }

    /// <summary>
    /// Public method to add torrent from URL
    /// </summary>
    public async void AddTorrentFromUrl(string downloadUrl)
    {
        try
        {
            StatusText.Text = "Downloading torrent file...";
            
            // Download the .torrent file
            using var httpClient = new System.Net.Http.HttpClient();
            var torrentData = await httpClient.GetByteArrayAsync(downloadUrl);
            
            StatusText.Text = "Parsing torrent file...";
            var torrent = await _torrentEngine.AddTorrentFromBytesAsync(torrentData, downloadUrl);
            
            StatusText.Text = $"Loaded torrent: {torrent.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load torrent from URL:\n\n{ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to load torrent";
        }
    }
}
