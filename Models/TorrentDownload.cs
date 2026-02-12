using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RuSkraping.Models;

/// <summary>
/// Represents a torrent download with all its metadata and state
/// </summary>
public class TorrentDownload : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _infoHash = string.Empty;
    private long _totalSize;
    private long _downloaded;
    private long _uploaded;
    private double _progress;
    private TorrentState _state = TorrentState.Stopped;
    private int _downloadSpeed;
    private int _uploadSpeed;
    private int _peersConnected;
    private int _peersTotal;
    private int _seedsConnected;
    private string _savePath = string.Empty;
    private DateTime _addedDate = DateTime.Now;
    private TimeSpan _eta = TimeSpan.Zero;
    private double _ratio;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public string InfoHash
    {
        get => _infoHash;
        set { _infoHash = value; OnPropertyChanged(nameof(InfoHash)); }
    }

    public long TotalSize
    {
        get => _totalSize;
        set { _totalSize = value; OnPropertyChanged(nameof(TotalSize)); OnPropertyChanged(nameof(TotalSizeFormatted)); }
    }

    public long Downloaded
    {
        get => _downloaded;
        set 
        { 
            _downloaded = value; 
            OnPropertyChanged(nameof(Downloaded));
            OnPropertyChanged(nameof(DownloadedFormatted));
            UpdateProgress();
            UpdateRatio();
        }
    }

    public long Uploaded
    {
        get => _uploaded;
        set 
        { 
            _uploaded = value; 
            OnPropertyChanged(nameof(Uploaded));
            OnPropertyChanged(nameof(UploadedFormatted));
            UpdateRatio();
        }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(ProgressFormatted)); }
    }

    public TorrentState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(StateText)); }
    }

    public int DownloadSpeed
    {
        get => _downloadSpeed;
        set { _downloadSpeed = value; OnPropertyChanged(nameof(DownloadSpeed)); OnPropertyChanged(nameof(DownloadSpeedFormatted)); }
    }

    public int UploadSpeed
    {
        get => _uploadSpeed;
        set { _uploadSpeed = value; OnPropertyChanged(nameof(UploadSpeed)); OnPropertyChanged(nameof(UploadSpeedFormatted)); }
    }

    public int PeersConnected
    {
        get => _peersConnected;
        set { _peersConnected = value; OnPropertyChanged(nameof(PeersConnected)); OnPropertyChanged(nameof(PeersText)); }
    }

    public int PeersTotal
    {
        get => _peersTotal;
        set { _peersTotal = value; OnPropertyChanged(nameof(PeersTotal)); OnPropertyChanged(nameof(PeersText)); }
    }

    public int SeedsConnected
    {
        get => _seedsConnected;
        set { _seedsConnected = value; OnPropertyChanged(nameof(SeedsConnected)); OnPropertyChanged(nameof(SeedsText)); }
    }

    public string SavePath
    {
        get => _savePath;
        set { _savePath = value; OnPropertyChanged(nameof(SavePath)); }
    }

    public DateTime AddedDate
    {
        get => _addedDate;
        set { _addedDate = value; OnPropertyChanged(nameof(AddedDate)); }
    }

    public TimeSpan ETA
    {
        get => _eta;
        set { _eta = value; OnPropertyChanged(nameof(ETA)); OnPropertyChanged(nameof(ETAFormatted)); }
    }

    public double Ratio
    {
        get => _ratio;
        set { _ratio = value; OnPropertyChanged(nameof(Ratio)); OnPropertyChanged(nameof(RatioFormatted)); }
    }

    // Formatted properties for UI
    public string TotalSizeFormatted => FormatBytes(TotalSize);
    public string DownloadedFormatted => FormatBytes(Downloaded);
    public string UploadedFormatted => FormatBytes(Uploaded);
    public string ProgressFormatted => $"{Progress:F2}%";
    public string StateText => State.ToString();
    public string DownloadSpeedFormatted => $"{FormatBytes(DownloadSpeed)}/s";
    public string UploadSpeedFormatted => $"{FormatBytes(UploadSpeed)}/s";
    public string PeersText => $"{PeersConnected}/{PeersTotal}";
    public string SeedsText => $"{SeedsConnected}";
    public string ETAFormatted => ETA.TotalSeconds > 0 ? $"{ETA:hh\\:mm\\:ss}" : "∞";
    public string RatioFormatted => $"{Ratio:F2}";

    public ObservableCollection<Peer> Peers { get; set; } = new();
    public ObservableCollection<TorrentFile> Files { get; set; } = new();

    private void UpdateProgress()
    {
        if (TotalSize > 0)
        {
            Progress = (double)Downloaded / TotalSize * 100;
        }
    }

    private void UpdateRatio()
    {
        if (Downloaded > 0)
        {
            Ratio = (double)Uploaded / Downloaded;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum TorrentState
{
    Stopped,
    Checking,
    Downloading,
    Seeding,
    Paused,
    Error,
    QueuedForChecking,
    QueuedForDownload
}
