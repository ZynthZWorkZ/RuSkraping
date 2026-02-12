using System;
using System.ComponentModel;
using System.Net;

namespace RuSkraping.Models;

/// <summary>
/// Represents a peer connection in a torrent swarm
/// </summary>
public class Peer : INotifyPropertyChanged
{
    private string _ipAddress = string.Empty;
    private int _port;
    private string _peerId = string.Empty;
    private PeerState _state = PeerState.Disconnected;
    private int _downloadSpeed;
    private int _uploadSpeed;
    private double _progress;
    private bool _isSeeder;
    private bool _isChoking = true;
    private bool _isInterested;
    private bool _amChoking = true;
    private bool _amInterested;
    private string _client = string.Empty;

    public string IpAddress
    {
        get => _ipAddress;
        set { _ipAddress = value; OnPropertyChanged(nameof(IpAddress)); }
    }

    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(nameof(Port)); }
    }

    public string PeerId
    {
        get => _peerId;
        set { _peerId = value; OnPropertyChanged(nameof(PeerId)); }
    }

    public PeerState State
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

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(ProgressFormatted)); }
    }

    public bool IsSeeder
    {
        get => _isSeeder;
        set { _isSeeder = value; OnPropertyChanged(nameof(IsSeeder)); }
    }

    public bool IsChoking
    {
        get => _isChoking;
        set { _isChoking = value; OnPropertyChanged(nameof(IsChoking)); }
    }

    public bool IsInterested
    {
        get => _isInterested;
        set { _isInterested = value; OnPropertyChanged(nameof(IsInterested)); }
    }

    public bool AmChoking
    {
        get => _amChoking;
        set { _amChoking = value; OnPropertyChanged(nameof(AmChoking)); }
    }

    public bool AmInterested
    {
        get => _amInterested;
        set { _amInterested = value; OnPropertyChanged(nameof(AmInterested)); }
    }

    public string Client
    {
        get => _client;
        set { _client = value; OnPropertyChanged(nameof(Client)); }
    }

    // Formatted properties
    public string StateText => State.ToString();
    public string DownloadSpeedFormatted => FormatSpeed(DownloadSpeed);
    public string UploadSpeedFormatted => FormatSpeed(UploadSpeed);
    public string ProgressFormatted => $"{Progress:F1}%";

    private static string FormatSpeed(int bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond} B/s";
        if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024.0:F1} KB/s";
        return $"{bytesPerSecond / (1024.0 * 1024.0):F1} MB/s";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum PeerState
{
    Disconnected,
    Connecting,
    Handshaking,
    Connected,
    Downloading,
    Uploading,
    Choked,
    Error
}
