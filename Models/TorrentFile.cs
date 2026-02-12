using System.ComponentModel;

namespace RuSkraping.Models;

/// <summary>
/// Represents a file within a torrent
/// </summary>
public class TorrentFile : INotifyPropertyChanged
{
    private string _path = string.Empty;
    private long _size;
    private long _downloaded;
    private double _progress;
    private FilePriority _priority = FilePriority.Normal;

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(FileName)); }
    }

    public long Size
    {
        get => _size;
        set { _size = value; OnPropertyChanged(nameof(Size)); OnPropertyChanged(nameof(SizeFormatted)); UpdateProgress(); }
    }

    public long Downloaded
    {
        get => _downloaded;
        set { _downloaded = value; OnPropertyChanged(nameof(Downloaded)); UpdateProgress(); }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(ProgressFormatted)); }
    }

    public FilePriority Priority
    {
        get => _priority;
        set { _priority = value; OnPropertyChanged(nameof(Priority)); OnPropertyChanged(nameof(PriorityText)); }
    }

    // Formatted properties
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SizeFormatted => FormatBytes(Size);
    public string ProgressFormatted => $"{Progress:F1}%";
    public string PriorityText => Priority.ToString();

    private void UpdateProgress()
    {
        if (Size > 0)
        {
            Progress = (double)Downloaded / Size * 100;
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

public enum FilePriority
{
    DoNotDownload = 0,
    Low = 1,
    Normal = 4,
    High = 7
}
