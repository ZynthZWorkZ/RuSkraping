using System.Windows;
using System.Windows.Input;
using RuSkraping.Models;

namespace RuSkraping;

/// <summary>
/// Interaction logic for TorrentDetailsWindow.xaml
/// </summary>
public partial class TorrentDetailsWindow : Window
{
    private TorrentDownload _torrent;

    public TorrentDetailsWindow(TorrentDownload torrent)
    {
        InitializeComponent();
        _torrent = torrent;
        LoadDetails();
    }

    private void LoadDetails()
    {
        TitleText.Text = $"Torrent Details - {_torrent.Name}";
        InfoHashText.Text = _torrent.InfoHash;
        NameText.Text = _torrent.Name;
        TotalSizeText.Text = _torrent.TotalSizeFormatted;
        FileCountText.Text = $"{_torrent.Files.Count} file(s)";
        SavePathText.Text = _torrent.SavePath;
        AddedDateText.Text = _torrent.AddedDate.ToString("yyyy-MM-dd HH:mm:ss");

        // Load files list
        FilesListView.ItemsSource = _torrent.Files;
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyHashButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_torrent.InfoHash);
            MessageBox.Show($"Info hash copied to clipboard:\n\n{_torrent.InfoHash}", 
                "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard:\n{ex.Message}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
