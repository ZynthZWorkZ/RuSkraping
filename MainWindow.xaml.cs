using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ookii.Dialogs.Wpf;
using RuSkraping.Models;
using RuSkraping.Services;

namespace RuSkraping;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ObservableCollection<TorrentSearchResult> searchResults;
    private RuTrackerSearchService? searchService;
    private string currentMagnetLink = string.Empty;
    private CancellationTokenSource? searchCancellation;
    private bool isPaused;
    private ManualResetEvent pauseEvent;

    public MainWindow()
    {
        InitializeComponent();
        searchResults = new ObservableCollection<TorrentSearchResult>();
        ResultsListView.ItemsSource = searchResults;
        pauseEvent = new ManualResetEvent(true);
        
        // Initialize search service (no ChromeDriver needed!)
        try
        {
            searchService = new RuTrackerSearchService();
            
            // Load cookies and set them in the service
            var cookies = CookieStorage.LoadCookies();
            if (cookies != null && cookies.Count > 0)
            {
                searchService.SetCookies(cookies);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize search service: {ex.Message}", 
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    // ChromeDriver initialization removed - no longer needed for search!
    // Only LoginWindow uses ChromeDriver now for authentication

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        // Access UI elements on the UI thread
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            MessageBox.Show("Please enter a search query.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Access UI elements on the UI thread
            SearchButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            searchCancellation = new CancellationTokenSource();
            isPaused = false;
            pauseEvent.Set();

        // Create and show progress window on UI thread
        var progressWindow = new Progress();
        progressWindow.Owner = this;
        progressWindow.Show();
        progressWindow.StartFakeAnimation();

        // Run the search in a background thread
        await Task.Run(async () =>
        {
            try
            {
                await PerformSearch(searchCancellation.Token, progressWindow);
        }
        catch (OperationCanceledException)
            {
                // Update status text on UI thread
                await Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = "Search stopped.";
                });
        }
        catch (Exception ex)
            {
                // Show error message on UI thread
                await Dispatcher.InvokeAsync(() =>
        {
            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
        }
        finally
            {
                // Update UI elements and close window on UI thread
                await Dispatcher.InvokeAsync(() =>
        {
            SearchButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
                    progressWindow.Close();
                });
            }
        });
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!isPaused)
        {
            isPaused = true;
            pauseEvent.Reset();
            PauseButton.Content = "Resume";
            StatusText.Text = "Search paused...";
        }
        else
        {
            isPaused = false;
            pauseEvent.Set();
            PauseButton.Content = "Pause";
            StatusText.Text = "Resuming search...";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        searchCancellation?.Cancel();
        pauseEvent.Set(); // Ensure we're not stuck in a pause
        StatusText.Text = "Stopping search...";
    }

    private async Task PerformSearch(CancellationToken cancellationToken, Progress progressWindow)
    {
        // Clear search results and update status on UI thread
        await Dispatcher.InvokeAsync(() =>
        {
            searchResults.Clear();
            StatusText.Text = "Searching...";
            GetMagnetButton.IsEnabled = false;
            OpenMagnetButton.IsEnabled = false;
        });

        try
        {
            // Check for valid cookies first
            List<Models.CookieData>? savedCookies = CookieStorage.LoadCookies();
            if (savedCookies == null || savedCookies.Count == 0)
            {
                // No valid cookies, need to login
                bool loginSuccess = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    var loginWindow = new LoginWindow();
                    if (loginWindow.ShowDialog() == true && loginWindow.LoginSuccessful)
                    {
                        loginSuccess = true;
                    }
                });
                
                if (!loginSuccess)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = "Login required to perform search.";
                        MessageBox.Show("Login is required to perform searches. Please login and try again.", 
                            "Login Required", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }
                
                // Reload cookies after successful login
                savedCookies = CookieStorage.LoadCookies();
                if (savedCookies == null || savedCookies.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = "Failed to load cookies after login.";
                        MessageBox.Show("Failed to load cookies after login. Please try again.", 
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return;
                }
                
                // Reinitialize search service with new cookies
                if (searchService != null)
                {
                    searchService.SetCookies(savedCookies);
                }
            }

            // Get search text from UI
            string searchText = await Dispatcher.InvokeAsync(() => SearchTextBox.Text);

            // Update status
            await Dispatcher.InvokeAsync(() => StatusText.Text = "Fetching search results...");

            // Perform HTTP-based search (no ChromeDriver needed!)
            var results = await searchService!.SearchAsync(
                searchText, 
                maxPages: -1, // Get all pages
                cancellationToken: cancellationToken,
                progressCallback: (currentPage, totalPage, allResults) =>
                {
                    // Check for pause
                    pauseEvent.WaitOne();
                    
                    // Update UI with results from this page
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"Processing page {currentPage}... Found {allResults.Count} results";
                        
                        // Add new results that aren't already in the collection
                        var existingIds = searchResults.Select(r => r.TopicId).ToHashSet();
                        foreach (var result in allResults.Where(r => !existingIds.Contains(r.TopicId)))
                        {
                            searchResults.Add(result);
                            existingIds.Add(result.TopicId);
                        }
                        
                        // Update progress bar
                        double progress = Math.Min((double)allResults.Count / Math.Max(allResults.Count, 50) * 100, 99);
                        progressWindow.UpdateProgress(progress, $"Page {currentPage}: {allResults.Count} results");
                    });
                });

            // Final progress update
            progressWindow.UpdateProgress(100, $"Complete! Found {results.Count} results");

            // Update status text on UI thread
            await Dispatcher.InvokeAsync(() => 
            {
                StatusText.Text = $"Search complete. Found {searchResults.Count} results.";
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() => 
            {
                StatusText.Text = "Search cancelled.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = $"Search failed: {ex.Message}";
                MessageBox.Show($"Search error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            // Update UI elements and close window on UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                GetMagnetButton.IsEnabled = true;
                OpenMagnetButton.IsEnabled = true;
                progressWindow.Close();
            });
        }
    }

    // Old Selenium-based methods removed - no longer needed with HTTP-based search!

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        searchCancellation?.Cancel();
        pauseEvent?.Dispose();
        
        // Dispose of search service
        searchService?.Dispose();
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

    // Add Magnet Link related methods here if needed, ensuring thread safety.
    // For example, if GetMagnetButton_Click and OpenMagnetButton_Click interact with
    // searchResults or driver, they should use Dispatcher.Invoke/InvokeAsync or Task.Run.
    // Looking at the existing code, GetMagnetButton_Click and OpenMagnetButton_Click
    // already seem to be interacting with driver and currentMagnetLink (which isn't UI).
    // We should ensure GetMagnetButton_Click's driver interaction is backgrounded and
    // MessageBox.Show is UI-threaded.

    private async void GetMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        // Access UI elements on UI thread
        if (ResultsListView.SelectedItem == null)
        {
            MessageBox.Show("Please select a result first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedResult = (TorrentSearchResult)ResultsListView.SelectedItem;
        
        // Check if magnet URL is already cached
        if (!string.IsNullOrEmpty(selectedResult.MagnetUrl))
        {
            currentMagnetLink = selectedResult.MagnetUrl;
            Clipboard.SetText(currentMagnetLink);
            StatusText.Text = "Magnet link copied to clipboard!";
            OpenMagnetButton.IsEnabled = true;
            return;
        }

        // Disable button while fetching
        GetMagnetButton.IsEnabled = false;
        StatusText.Text = $"Fetching magnet link for: {selectedResult.Title.Substring(0, Math.Min(50, selectedResult.Title.Length))}...";

        try
        {
            // Fetch torrent details including magnet URL
            var (magnetUrl, imageUrl, description) = await searchService!.FetchTorrentDetailsAsync(selectedResult.TopicId);
            
            if (!string.IsNullOrEmpty(magnetUrl))
            {
                // Cache the details in the result object
                selectedResult.MagnetUrl = magnetUrl;
                selectedResult.ImageUrl = imageUrl;
                selectedResult.Description = description;
                
                currentMagnetLink = magnetUrl;
                Clipboard.SetText(currentMagnetLink);
                StatusText.Text = "Magnet link copied to clipboard!";
            OpenMagnetButton.IsEnabled = true;
        }
        else
        {
                MessageBox.Show("Could not find magnet link for this torrent.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "No magnet link found.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error fetching magnet link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to fetch magnet link.";
        }
        finally
        {
            GetMagnetButton.IsEnabled = true;
        }
    }

    private void OpenMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        // Access currentMagnetLink (not a UI element, so no dispatcher needed)
        if (string.IsNullOrEmpty(currentMagnetLink))
        {
            MessageBox.Show("No magnet link available. Please click 'Get Magnet Link' first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Open magnet link with default torrent client
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = currentMagnetLink,
                UseShellExecute = true
            };
            
            System.Diagnostics.Process.Start(psi);
            
            // Update status on UI thread
            StatusText.Text = "Opened magnet link in default torrent client.";
        }
        catch (Exception ex)
        {
            // Show error message on UI thread
            MessageBox.Show($"Could not open magnet link: {ex.Message}\n\nMake sure you have a torrent client installed (e.g., qBittorrent, uTorrent, etc.)", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to open magnet link.";
        }
    }

    /// <summary>
    /// Checks if cookies are valid and prompts for login if needed
    /// </summary>
    private async Task<bool> EnsureValidCookiesAsync()
    {
        if (!CookieStorage.HasValidCookies())
        {
            // Show login window
            var loginWindow = new LoginWindow();
            var result = await Dispatcher.InvokeAsync(() => loginWindow.ShowDialog());
            
            if (result == true && loginWindow.LoginSuccessful)
            {
                return true;
            }
            return false;
        }
        return true;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();
        if (loginWindow.ShowDialog() == true && loginWindow.LoginSuccessful)
        {
            StatusText.Text = "Login successful! Cookies have been saved.";
            MessageBox.Show("Login successful! Your cookies have been saved.", 
                "Login Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusText.Text = "Login cancelled or failed.";
        }
    }

    private void CheckAuthButton_Click(object sender, RoutedEventArgs e)
    {
        var cookies = CookieStorage.LoadCookies();
        if (cookies != null && cookies.Count > 0)
        {
            var validCookies = cookies.Where(c => 
                c.Session || 
                (c.Expires.HasValue && c.Expires.Value > DateTime.UtcNow)
            ).ToList();
            
            if (validCookies.Count > 0)
            {
                StatusText.Text = $"Authentication valid. {validCookies.Count} active cookies.";
                MessageBox.Show($"Authentication is valid!\n\nActive cookies: {validCookies.Count}\nTotal cookies: {cookies.Count}", 
                    "Authentication Status", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = "All cookies have expired. Please login again.";
                MessageBox.Show("All cookies have expired. Please login again.", 
                    "Authentication Expired", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            StatusText.Text = "No cookies found. Please login.";
            MessageBox.Show("No cookies found. Please login to authenticate.", 
                "Not Authenticated", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportCookiesButton_Click(object sender, RoutedEventArgs e)
    {
        var cookies = CookieStorage.LoadCookies();
        if (cookies == null || cookies.Count == 0)
        {
            MessageBox.Show("No cookies to export. Please login first.", 
                "No Cookies", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "rutracker_cookies.json",
                DefaultExt = "json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = System.Text.Json.JsonSerializer.Serialize(cookies, options);
                File.WriteAllText(saveDialog.FileName, json);
                
                StatusText.Text = $"Cookies exported to {Path.GetFileName(saveDialog.FileName)}";
                MessageBox.Show($"Cookies exported successfully to:\n{saveDialog.FileName}", 
                    "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error exporting cookies: {ex.Message}", 
                "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Old ExtractTitles method removed - now using HTTP-based RuTrackerSearchService

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var selectedResult = button?.DataContext as TorrentSearchResult;

        if (selectedResult == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(selectedResult.DownloadUrl))
        {
            MessageBox.Show("No download URL available for this torrent.", 
                "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Show save file dialog
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Torrent files (*.torrent)|*.torrent|All files (*.*)|*.*",
            FileName = $"{selectedResult.TopicId}_{SanitizeFileName(selectedResult.Title)}.torrent",
            DefaultExt = "torrent",
            Title = "Save Torrent File"
        };

        if (saveDialog.ShowDialog() != true)
        {
            return; // User cancelled
        }

        // Disable button during download
        button.IsEnabled = false;
        var originalStatus = StatusText.Text;
        StatusText.Text = $"Downloading torrent: {selectedResult.Title.Substring(0, Math.Min(50, selectedResult.Title.Length))}...";

        try
        {
            // Download the torrent file
            byte[] torrentData = await searchService!.DownloadTorrentAsync(selectedResult.DownloadUrl);
            
            // Save to file
            await File.WriteAllBytesAsync(saveDialog.FileName, torrentData);
            
            StatusText.Text = $"Downloaded: {Path.GetFileName(saveDialog.FileName)}";
            MessageBox.Show($"Torrent file downloaded successfully!\n\nSaved to:\n{saveDialog.FileName}", 
                "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download failed: {ex.Message}";
            MessageBox.Show($"Failed to download torrent:\n{ex.Message}", 
                "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
            if (StatusText.Text.StartsWith("Downloaded:") || StatusText.Text.StartsWith("Download failed:"))
            {
                await Task.Delay(3000);
                StatusText.Text = originalStatus;
            }
        }
    }

    private string SanitizeFileName(string fileName)
    {
        // Remove invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        
        // Limit length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }
        
        return sanitized;
    }

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        // Access UI elements on UI thread
        var button = sender as Button;
        var selectedResult = button?.DataContext as TorrentSearchResult;

        if (selectedResult == null)
        {
            return; // Should not happen if button is in ListView item template
        }

        // Disable button while fetching
        button.IsEnabled = false;
        var originalStatus = StatusText.Text;
        StatusText.Text = "Fetching torrent details...";

        try
        {
            // Fetch details if not already cached
            if (string.IsNullOrEmpty(selectedResult.MagnetUrl) || string.IsNullOrEmpty(selectedResult.Description))
            {
                var (magnetUrl, imageUrl, description) = await searchService!.FetchTorrentDetailsAsync(selectedResult.TopicId);
                
                // Cache the details
                if (!string.IsNullOrEmpty(magnetUrl))
                    selectedResult.MagnetUrl = magnetUrl;
                if (!string.IsNullOrEmpty(imageUrl))
                    selectedResult.ImageUrl = imageUrl;
                if (!string.IsNullOrEmpty(description))
                    selectedResult.Description = description;
            }

            // Build details text with all available information
            string detailsText = $@"═══════════════════════════════════════════════════════
 TORRENT INFORMATION
═══════════════════════════════════════════════════════

Title: {selectedResult.Title}

Topic ID: {selectedResult.TopicId}
Size: {selectedResult.Size}
Seeds: {selectedResult.Seeds} | Leeches: {selectedResult.Leeches}
Author: {selectedResult.Author}
Date: {selectedResult.Date}

═══════════════════════════════════════════════════════
 LINKS
═══════════════════════════════════════════════════════

Page: {selectedResult.Link}

Download: {(string.IsNullOrEmpty(selectedResult.DownloadUrl) ? "Not available" : selectedResult.DownloadUrl)}

Magnet: {(string.IsNullOrEmpty(selectedResult.MagnetUrl) ? "Not available" : selectedResult.MagnetUrl)}

═══════════════════════════════════════════════════════
 DESCRIPTION
═══════════════════════════════════════════════════════

{(string.IsNullOrEmpty(selectedResult.Description) ? "No description available." : selectedResult.Description)}";

        // Show details in a new window on UI thread
        var detailsWindow = new Details();
        detailsWindow.Owner = this;
            detailsWindow.SetDetailsWithImage(detailsText, selectedResult.ImageUrl);
        detailsWindow.ShowDialog();
            
            StatusText.Text = originalStatus;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error fetching details: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to fetch details.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if there are results
        if (searchResults == null || searchResults.Count == 0)
        {
            MessageBox.Show("No search results to download. Please perform a search first.", 
                "No Results", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Filter results that have download URLs
        var downloadableResults = searchResults.Where(r => !string.IsNullOrEmpty(r.DownloadUrl)).ToList();
        if (downloadableResults.Count == 0)
        {
            MessageBox.Show("No results with download links available.", 
                "No Downloadable Results", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Show folder browser dialog
        var folderDialog = new VistaFolderBrowserDialog
        {
            Description = "Select a folder to save all torrent files",
            UseDescriptionForTitle = true
        };

        if (folderDialog.ShowDialog() != true)
        {
            return; // User cancelled
        }

        string downloadFolder = folderDialog.SelectedPath;
        if (string.IsNullOrEmpty(downloadFolder) || !Directory.Exists(downloadFolder))
        {
            MessageBox.Show("Invalid folder selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Disable button during download
        DownloadAllButton.IsEnabled = false;
        var originalStatus = StatusText.Text;

        // Create and show progress window
        var progressWindow = new Progress();
        progressWindow.Owner = this;
        progressWindow.Show();
        progressWindow.StartFakeAnimation();

        // Create cancellation token source
        var cancellationTokenSource = new CancellationTokenSource();

        // Run downloads in background thread
        await Task.Run(async () =>
        {
            try
            {
                await PerformDownloadAll(downloadableResults, downloadFolder, cancellationTokenSource.Token, progressWindow);
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = "Download stopped.";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    DownloadAllButton.IsEnabled = true;
                    progressWindow.Close();
                });
            }
        });
    }

    private async Task PerformDownloadAll(List<TorrentSearchResult> results, string downloadFolder, CancellationToken cancellationToken, Progress progressWindow)
    {
        int totalCount = results.Count;
        int successCount = 0;
        int failedCount = 0;
        var failedItems = new List<string>();

        // Update status on UI thread
        await Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = $"Starting download of {totalCount} files...";
        });

        for (int i = 0; i < results.Count; i++)
        {
            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            var result = results[i];
            int currentIndex = i + 1;

            try
            {
                // Update progress status
                await Dispatcher.InvokeAsync(() =>
                {
                    string shortTitle = result.Title.Length > 50 
                        ? result.Title.Substring(0, 50) + "..." 
                        : result.Title;
                    progressWindow.UpdateProgress(
                        (double)(i * 100) / totalCount,
                        $"Downloading {currentIndex} of {totalCount}: {shortTitle}");
                    StatusText.Text = $"Downloading {currentIndex} of {totalCount}: {shortTitle}";
                });

                // Validate download URL
                if (string.IsNullOrEmpty(result.DownloadUrl))
                {
                    failedCount++;
                    failedItems.Add($"{result.Title} (No download URL)");
                    continue;
                }

                // Download the torrent file
                byte[] torrentData = await searchService!.DownloadTorrentAsync(result.DownloadUrl, cancellationToken);

                // Generate filename
                string sanitizedTitle = SanitizeFileName(result.Title);
                string fileName = $"{result.TopicId}_{sanitizedTitle}.torrent";
                string filePath = GetUniqueFilePath(downloadFolder, fileName);

                // Save to file
                await File.WriteAllBytesAsync(filePath, torrentData, cancellationToken);

                successCount++;
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw to stop the loop
            }
            catch (Exception ex)
            {
                failedCount++;
                string errorMessage = ex.Message;
                failedItems.Add($"{result.Title} ({errorMessage})");
            }
        }

        // Final progress update
        progressWindow.UpdateProgress(100, $"Complete! Downloaded {successCount} of {totalCount} files");

        // Show completion summary on UI thread
        await Dispatcher.InvokeAsync(() =>
        {
            string summaryMessage = $"Download complete!\n\n" +
                                   $"Successfully downloaded: {successCount} files\n" +
                                   $"Failed: {failedCount} files\n" +
                                   $"Total: {totalCount} files";

            if (failedCount > 0 && failedItems.Count > 0)
            {
                // Show first 10 failed items
                int itemsToShow = Math.Min(10, failedItems.Count);
                summaryMessage += $"\n\nFailed items:\n";
                for (int i = 0; i < itemsToShow; i++)
                {
                    summaryMessage += $"- {failedItems[i]}\n";
                }
                if (failedItems.Count > 10)
                {
                    summaryMessage += $"... and {failedItems.Count - 10} more";
                }
            }

            MessageBox.Show(summaryMessage, "Download Complete", MessageBoxButton.OK, 
                failedCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            
            StatusText.Text = $"Downloaded {successCount} of {totalCount} files to {Path.GetFileName(downloadFolder)}";
        });
    }

    private string GetUniqueFilePath(string folder, string fileName)
    {
        string filePath = Path.Combine(folder, fileName);
        
        // If file doesn't exist, return the original path
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        // File exists, generate unique name
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int counter = 1;

        do
        {
            string newFileName = $"{fileNameWithoutExt} ({counter}){extension}";
            filePath = Path.Combine(folder, newFileName);
            counter++;
        }
        while (File.Exists(filePath));

        return filePath;
    }

    private void OpenDownloadManagerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorLogger.LogMessage("Opening Download Manager...", "INFO");
            
            // Check if Download Manager is already open
            var existingManager = Application.Current.Windows.OfType<DownloadManager>().FirstOrDefault();
            
            if (existingManager != null)
            {
                ErrorLogger.LogMessage("Download Manager already open, activating...", "INFO");
                // Activate existing window
                existingManager.Activate();
                existingManager.WindowState = WindowState.Normal;
            }
            else
            {
                ErrorLogger.LogMessage("Creating new Download Manager instance...", "INFO");
                // Create new Download Manager window
                var downloadManager = new DownloadManager();
                
                ErrorLogger.LogMessage("Showing Download Manager window...", "INFO");
                downloadManager.Show();
                
                ErrorLogger.LogMessage("Download Manager opened successfully", "INFO");
            }
            
            StatusText.Text = "Opened RUSKTorrent Manager";
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, "Opening Download Manager");
            var result = MessageBox.Show(
                $"Failed to open Download Manager:\n\n{ex.Message}\n\n" +
                $"Full error details logged to:\n{ErrorLogger.GetLogFilePath()}\n\n" +
                $"Would you like to open the log file?",
                "Error Opening Download Manager",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);
            
            if (result == MessageBoxResult.Yes)
            {
                ErrorLogger.OpenLogFile();
            }
            
            StatusText.Text = "Failed to open Download Manager - check error log";
        }
    }
}

// SearchResult class removed - now using TorrentSearchResult from Models