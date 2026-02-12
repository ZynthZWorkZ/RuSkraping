using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RuSkraping.Models;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Main RUSKTorrent engine - coordinates all torrent operations
/// Phase 0: Metadata loading and parsing only
/// </summary>
public class TorrentEngine : IDisposable
{
    private readonly List<TorrentDownload> _activeTorrents;
    private readonly string _downloadDirectory;
    private bool _disposed;

    public event EventHandler<TorrentDownload>? TorrentAdded;
    public event EventHandler<TorrentDownload>? TorrentRemoved;
    public event EventHandler<TorrentDownload>? TorrentUpdated;

    public TorrentEngine(string downloadDirectory = "Downloads")
    {
        _activeTorrents = new List<TorrentDownload>();
        _downloadDirectory = downloadDirectory;

        // Ensure download directory exists
        if (!Directory.Exists(_downloadDirectory))
        {
            Directory.CreateDirectory(_downloadDirectory);
        }
    }

    /// <summary>
    /// Add a torrent from a .torrent file
    /// </summary>
    public async Task<TorrentDownload> AddTorrentFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Torrent file not found", filePath);

        // Read torrent file
        byte[] torrentData = await File.ReadAllBytesAsync(filePath);

        // Parse metadata
        var metadata = TorrentMetadata.Parse(torrentData);

        // Create TorrentDownload model
        var torrent = CreateTorrentFromMetadata(metadata, filePath);

        // Add to active torrents
        _activeTorrents.Add(torrent);

        // Raise event
        TorrentAdded?.Invoke(this, torrent);

        return torrent;
    }

    /// <summary>
    /// Add a torrent from magnet link
    /// </summary>
    public async Task<TorrentDownload> AddTorrentFromMagnetAsync(string magnetUri)
    {
        // Parse magnet link
        var magnet = MagnetLink.Parse(magnetUri);

        // Create TorrentDownload model from magnet
        var torrent = CreateTorrentFromMagnet(magnet);

        // Add to active torrents
        _activeTorrents.Add(torrent);

        // Raise event
        TorrentAdded?.Invoke(this, torrent);

        // Note: In a full implementation, we would:
        // 1. Contact DHT to find peers
        // 2. Request metadata from peers (BEP-0009)
        // 3. Once we have metadata, update the torrent info

        await Task.CompletedTask;
        return torrent;
    }

    /// <summary>
    /// Add a torrent from raw .torrent data
    /// </summary>
    public async Task<TorrentDownload> AddTorrentFromBytesAsync(byte[] torrentData, string sourceName = "Unknown")
    {
        // Parse metadata
        var metadata = TorrentMetadata.Parse(torrentData);

        // Create TorrentDownload model
        var torrent = CreateTorrentFromMetadata(metadata, sourceName);

        // Add to active torrents
        _activeTorrents.Add(torrent);

        // Raise event
        TorrentAdded?.Invoke(this, torrent);

        await Task.CompletedTask;
        return torrent;
    }

    /// <summary>
    /// Remove a torrent
    /// </summary>
    public async Task RemoveTorrentAsync(TorrentDownload torrent, bool deleteData = false)
    {
        if (!_activeTorrents.Contains(torrent))
            return;

        // Stop torrent if running
        await StopTorrentAsync(torrent);

        // Remove from list
        _activeTorrents.Remove(torrent);

        // Delete data if requested
        if (deleteData && !string.IsNullOrEmpty(torrent.SavePath))
        {
            try
            {
                if (Directory.Exists(torrent.SavePath))
                {
                    Directory.Delete(torrent.SavePath, true);
                }
                else if (File.Exists(torrent.SavePath))
                {
                    File.Delete(torrent.SavePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete data: {ex.Message}");
            }
        }

        // Raise event
        TorrentRemoved?.Invoke(this, torrent);
    }

    /// <summary>
    /// Start downloading a torrent (Phase 0: Just changes state)
    /// </summary>
    public Task StartTorrentAsync(TorrentDownload torrent)
    {
        if (!_activeTorrents.Contains(torrent))
            throw new InvalidOperationException("Torrent not found in engine");

        // Phase 0: Just update state
        torrent.State = TorrentState.QueuedForDownload;

        // Note: In full implementation, this would:
        // 1. Contact trackers for peers
        // 2. Connect to DHT
        // 3. Start peer connections
        // 4. Begin piece downloads

        TorrentUpdated?.Invoke(this, torrent);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pause a torrent
    /// </summary>
    public Task PauseTorrentAsync(TorrentDownload torrent)
    {
        if (!_activeTorrents.Contains(torrent))
            throw new InvalidOperationException("Torrent not found in engine");

        torrent.State = TorrentState.Paused;
        TorrentUpdated?.Invoke(this, torrent);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop a torrent
    /// </summary>
    public Task StopTorrentAsync(TorrentDownload torrent)
    {
        if (!_activeTorrents.Contains(torrent))
            throw new InvalidOperationException("Torrent not found in engine");

        torrent.State = TorrentState.Stopped;
        TorrentUpdated?.Invoke(this, torrent);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get all active torrents
    /// </summary>
    public IReadOnlyList<TorrentDownload> GetAllTorrents()
    {
        return _activeTorrents.AsReadOnly();
    }

    /// <summary>
    /// Get detailed information about a torrent's metadata
    /// </summary>
    public TorrentInfo? GetTorrentInfo(TorrentDownload torrent)
    {
        // Return detailed info stored in the torrent
        // This would be populated from metadata
        return null; // Phase 0: Metadata display in UI
    }

    private TorrentDownload CreateTorrentFromMetadata(TorrentMetadata metadata, string source)
    {
        var torrent = new TorrentDownload
        {
            Name = metadata.Name,
            InfoHash = metadata.GetInfoHashString(),
            TotalSize = metadata.TotalSize,
            Downloaded = 0,
            Uploaded = 0,
            State = TorrentState.Stopped,
            SavePath = Path.Combine(_downloadDirectory, SanitizePath(metadata.Name)),
            AddedDate = DateTime.Now
        };

        // Add files
        foreach (var file in metadata.Files)
        {
            torrent.Files.Add(new TorrentFile
            {
                Path = file.Path,
                Size = file.Length,
                Downloaded = 0,
                Priority = FilePriority.Normal
            });
        }

        // Store metadata for later use (we'll add a property for this)
        // torrent.Metadata = metadata;

        return torrent;
    }

    private TorrentDownload CreateTorrentFromMagnet(MagnetLink magnet)
    {
        var torrent = new TorrentDownload
        {
            Name = magnet.DisplayName ?? $"Magnet_{magnet.InfoHash.Substring(0, 8)}",
            InfoHash = magnet.InfoHash,
            TotalSize = magnet.ExactLength ?? 0,
            Downloaded = 0,
            Uploaded = 0,
            State = TorrentState.Stopped,
            SavePath = Path.Combine(_downloadDirectory, SanitizePath(magnet.DisplayName ?? "Unknown")),
            AddedDate = DateTime.Now
        };

        // Note: For magnet links, we don't know the files until we get metadata from peers
        // In Phase 0, we just show what we have from the magnet link

        return torrent;
    }

    private string SanitizePath(string path)
    {
        // Remove invalid characters from path
        var invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            path = path.Replace(c, '_');
        }
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Stop all torrents
        foreach (var torrent in _activeTorrents.ToList())
        {
            StopTorrentAsync(torrent).Wait();
        }

        _activeTorrents.Clear();
        _disposed = true;
    }
}

/// <summary>
/// Detailed torrent information (for display purposes)
/// </summary>
public class TorrentInfo
{
    public string Name { get; set; } = string.Empty;
    public string InfoHash { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public int PieceLength { get; set; }
    public int PieceCount { get; set; }
    public List<string> Trackers { get; set; } = new();
    public List<TorrentFileInfo> Files { get; set; } = new();
    public string Comment { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? CreationDate { get; set; }
    public bool IsPrivate { get; set; }
}
