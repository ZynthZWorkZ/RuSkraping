using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RuSkraping.Models;
using RuSkraping.RUSKTorrent.Messages;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Main RUSKTorrent engine - coordinates all torrent operations.
/// Includes TCP listener for incoming peers, periodic re-announce, and full download capability.
/// </summary>
public class TorrentEngine : IDisposable
{
    private readonly List<TorrentDownload> _activeTorrents;
    private readonly Dictionary<string, TorrentContext> _torrentContexts;
    private readonly string _downloadDirectory;
    private readonly string _peerId;
    private PeerListener? _peerListener;
    private int _listenPort = 6881;
    private bool _disposed;

    public event EventHandler<TorrentDownload>? TorrentAdded;
    public event EventHandler<TorrentDownload>? TorrentRemoved;
    public event EventHandler<TorrentDownload>? TorrentUpdated;

    public TorrentEngine(string downloadDirectory = "Downloads")
    {
        _activeTorrents = new List<TorrentDownload>();
        _torrentContexts = new Dictionary<string, TorrentContext>();
        _downloadDirectory = downloadDirectory;
        _peerId = GeneratePeerId();

        // Ensure download directory exists
        if (!Directory.Exists(_downloadDirectory))
        {
            Directory.CreateDirectory(_downloadDirectory);
        }

        // Start the TCP listener for incoming peer connections — fully observed
        _ = StartListenerSafeAsync();
    }

    /// <summary>
    /// Safe wrapper — catches everything so the task never faults unobserved.
    /// </summary>
    private async Task StartListenerSafeAsync()
    {
        try
        {
            _peerListener = new PeerListener();
            _peerListener.IncomingPeerHandshaked += OnIncomingPeerHandshaked;
            _listenPort = await _peerListener.StartAsync(6881);
            if (_listenPort > 0)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Peer listener started on port {_listenPort}", "INFO");
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Could not start peer listener: {ex.Message}", "WARN");
            _listenPort = 6881; // Use default port for announce even if listener failed
        }
    }

    /// <summary>
    /// Handle incoming peer connections from the listener.
    /// Uses async void because it's an event handler, but wraps everything in try-catch
    /// to prevent any unhandled exception from crashing the app.
    /// </summary>
    private async void OnIncomingPeerHandshaked(TcpClient client, byte[] infoHash, string remotePeerId)
    {
        try
        {
            string infoHashStr = BitConverter.ToString(infoHash).Replace("-", "").ToUpperInvariant();

            // Find the matching torrent context
            TorrentContext? context = null;
            lock (_torrentContexts)
            {
                _torrentContexts.TryGetValue(infoHashStr, out context);
            }

            if (context == null)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Incoming peer for unknown torrent {infoHashStr.Substring(0, 8)}..., closing", "WARN");
                try { client.Close(); } catch { }
                return;
            }

            // Create a PeerConnection from the incoming TcpClient
            var peer = await PeerConnection.CreateFromIncomingAsync(client, infoHash, _peerId);
            if (peer != null)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] ✓ Incoming peer accepted for {context.Torrent.Name}: {peer.PeerIp}:{peer.PeerPort}", "INFO");
                
                lock (context.PeerLock)
                {
                    peer.MessageReceived += (s, msg) => OnPeerMessageReceived(context, peer, msg);
                    context.Peers.Add(peer);
                    context.IncomingPeerSignal.Release(); // Signal the download loop
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Failed to accept incoming peer: {ex.Message}", "WARN");
            try { client.Close(); } catch { }
        }
    }

    private string GeneratePeerId()
    {
        // Generate peer ID: "-RK0001-" + 12 random chars
        const string prefix = "-RK0001-";
        string random = Guid.NewGuid().ToString("N").Substring(0, 12);
        return prefix + random;
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
                    Directory.Delete(torrent.SavePath, true);
                else if (File.Exists(torrent.SavePath))
                    File.Delete(torrent.SavePath);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Failed to delete data: {ex.Message}", "WARN");
            }
        }

        // Raise event
        TorrentRemoved?.Invoke(this, torrent);
    }

    /// <summary>
    /// Start downloading a torrent
    /// </summary>
    public async Task StartTorrentAsync(TorrentDownload torrent)
    {
        if (!_activeTorrents.Contains(torrent))
            throw new InvalidOperationException("Torrent not found in engine");

        if (_torrentContexts.ContainsKey(torrent.InfoHash))
            return; // Already started

        torrent.State = TorrentState.Downloading;
        TorrentUpdated?.Invoke(this, torrent);

        var metadata = torrent.Metadata;
        if (metadata == null)
        {
            torrent.State = TorrentState.Error;
            TorrentUpdated?.Invoke(this, torrent);
            return;
        }

        // Create download context
        var context = new TorrentContext
        {
            Torrent = torrent,
            Metadata = metadata,
            PieceManager = new PieceManager(metadata),
            DiskManager = new DiskManager(torrent.SavePath, metadata),
            TrackerClient = new TrackerClient(metadata, _peerId, _listenPort),
            CancellationTokenSource = new CancellationTokenSource()
        };

        lock (_torrentContexts)
        {
            _torrentContexts[torrent.InfoHash] = context;
        }

        // Start download in background — fully observed
        _ = DownloadTorrentSafeAsync(context);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Safe wrapper — catches everything so the task never faults unobserved.
    /// </summary>
    private async Task DownloadTorrentSafeAsync(TorrentContext context)
    {
        try
        {
            await Task.Run(() => DownloadTorrentAsync(context));
        }
        catch (Exception ex)
        {
            // Already handled inside DownloadTorrentAsync, but just in case
            if (ex is not OperationCanceledException)
                ErrorLogger.LogMessage($"[TorrentEngine] Unhandled download error for {context.Torrent.Name}: {ex.Message}", "ERROR");
        }
    }

    /// <summary>
    /// Pause a torrent
    /// </summary>
    public Task PauseTorrentAsync(TorrentDownload torrent)
    {
        if (!_activeTorrents.Contains(torrent))
            throw new InvalidOperationException("Torrent not found in engine");

        if (_torrentContexts.TryGetValue(torrent.InfoHash, out var context))
        {
            context.CancellationTokenSource?.Cancel();
            lock (_torrentContexts)
            {
                _torrentContexts.Remove(torrent.InfoHash);
            }
        }

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

        if (_torrentContexts.TryGetValue(torrent.InfoHash, out var context))
        {
            try { context.CancellationTokenSource?.Cancel(); } catch { }
            try { context.DiskManager?.Dispose(); } catch { }
            
            lock (context.PeerLock)
            {
                foreach (var peer in context.Peers)
                {
                    try { peer.Dispose(); } catch { }
                }
                context.Peers.Clear();
            }
            
            lock (_torrentContexts)
            {
                _torrentContexts.Remove(torrent.InfoHash);
            }
        }

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
        return null; // Phase 0: Metadata display in UI
    }

    private TorrentDownload CreateTorrentFromMetadata(TorrentMetadata metadata, string source)
    {
        // Add public trackers as fallback if the torrent has limited trackers
        if (metadata.Trackers.Count < 5)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Torrent has only {metadata.Trackers.Count} trackers, adding public tracker fallbacks", "INFO");
            
            var publicTrackers = PublicTrackers.GetPublicTrackers();
            foreach (var tracker in publicTrackers)
            {
                if (!metadata.Trackers.Contains(tracker))
                    metadata.Trackers.Add(tracker);
            }
            
            ErrorLogger.LogMessage($"[TorrentEngine] Total trackers after adding fallbacks: {metadata.Trackers.Count}", "INFO");
        }
        
        var torrent = new TorrentDownload
        {
            Name = metadata.Name,
            InfoHash = metadata.GetInfoHashString(),
            TotalSize = metadata.TotalSize,
            Downloaded = 0,
            Uploaded = 0,
            State = TorrentState.Stopped,
            SavePath = Path.Combine(_downloadDirectory, SanitizePath(metadata.Name)),
            AddedDate = DateTime.Now,
            Metadata = metadata
        };

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

        return torrent;
    }

    private TorrentDownload CreateTorrentFromMagnet(MagnetLink magnet)
    {
        return new TorrentDownload
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
    }

    private string SanitizePath(string path)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            path = path.Replace(c, '_');
        return path;
    }

    // ═══════════════════════════════════════════════════════════
    //  MAIN DOWNLOAD LOOP
    // ═══════════════════════════════════════════════════════════

    private async Task DownloadTorrentAsync(TorrentContext context)
    {
        var ct = context.CancellationTokenSource!.Token;
        var torrent = context.Torrent;

        try
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Starting download for: {torrent.Name}", "INFO");
            ErrorLogger.LogMessage($"[TorrentEngine] Save path: {torrent.SavePath}", "INFO");
            ErrorLogger.LogMessage($"[TorrentEngine] Total size: {torrent.TotalSize} bytes", "INFO");
            ErrorLogger.LogMessage($"[TorrentEngine] Listening on port: {_listenPort}", "INFO");
            ErrorLogger.LogMessage($"[TorrentEngine] Info hash: {torrent.InfoHash}", "INFO");
            
            // Step 1: Piece map
            ErrorLogger.LogMessage($"[TorrentEngine] Piece map built: {context.PieceManager!.Pieces.Count} pieces", "INFO");

            // Step 2: Announce to trackers
            ErrorLogger.LogMessage($"[TorrentEngine] Announcing to all trackers...", "INFO");
            ErrorLogger.LogMessage($"[TorrentEngine] Trackers available: {string.Join(", ", context.Metadata!.Trackers)}", "INFO");
            
            var trackerResponse = await context.TrackerClient!.AnnounceAsync(
                torrent.Uploaded,
                torrent.Downloaded,
                torrent.TotalSize - torrent.Downloaded,
                TrackerEvent.Started
            );

            ErrorLogger.LogMessage($"[TorrentEngine] Found {trackerResponse.Peers.Count} unique peers from all trackers", "INFO");

            // Step 3: Try connecting to peers (outgoing) - connects to ALL reachable peers
            int connectedCount = await TryConnectToPeersAsync(context, trackerResponse.Peers, ct);

            // Step 4: If no outgoing connections worked, wait for incoming + re-announce
            if (connectedCount == 0)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] No outgoing connections succeeded. Waiting for incoming peers + re-announcing...", "INFO");
                bool foundPeer = await WaitForPeerWithReAnnounce(context, ct);
                if (!foundPeer || ct.IsCancellationRequested)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Could not find any connectable peers after all attempts.", "ERROR");
                    torrent.State = TorrentState.Error;
                    TorrentUpdated?.Invoke(this, torrent);
                    return;
                }
            }

            // Step 5: Start re-announce loop in background — fully observed
            _ = ReAnnounceLoopSafeAsync(context, ct);

            // Step 6: Download pieces!
            await DownloadPiecesAsync(context, ct);

            // Download complete!
            if (context.PieceManager.IsComplete)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] ✓ Download complete! {torrent.Name}", "INFO");
                torrent.State = TorrentState.Seeding;
                torrent.Progress = 100.0;
                TorrentUpdated?.Invoke(this, torrent);
                
                try
                {
                    await context.TrackerClient!.AnnounceAsync(
                        torrent.Uploaded, torrent.Downloaded, 0, TrackerEvent.Completed);
                }
                catch { /* Non-fatal */ }
            }
        }
        catch (OperationCanceledException)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Download cancelled: {torrent.Name}", "INFO");
            torrent.State = TorrentState.Stopped;
            TorrentUpdated?.Invoke(this, torrent);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, $"[TorrentEngine] Download error for {torrent.Name}");
            torrent.State = TorrentState.Error;
            TorrentUpdated?.Invoke(this, torrent);
        }
    }

    /// <summary>
    /// Try to connect to peers from the tracker response (outgoing connections).
    /// Connects to ALL reachable peers, not just the first one.
    /// Returns the number of newly connected peers.
    /// </summary>
    private async Task<int> TryConnectToPeersAsync(
        TorrentContext context, List<PeerInfo> peers, CancellationToken ct)
    {
        if (peers.Count == 0) return 0;

        // Filter out peers we already have
        var existingPeers = new HashSet<string>();
        lock (context.PeerLock)
        {
            foreach (var p in context.Peers)
                existingPeers.Add($"{p.PeerIp}:{p.PeerPort}");
        }

        var newPeers = peers.Where(p => !existingPeers.Contains($"{p.IP}:{p.Port}")).ToList();
        if (newPeers.Count == 0)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] All {peers.Count} peers already known", "INFO");
            return 0;
        }

        ErrorLogger.LogMessage($"[TorrentEngine] Attempting to connect to {newPeers.Count} new peers (parallel)...", "INFO");
        
        int successCount = 0;
        int failCount = 0;

        // Connect in parallel batches of 10
        const int batchSize = 10;
        for (int batchStart = 0; batchStart < newPeers.Count; batchStart += batchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = newPeers.Skip(batchStart).Take(batchSize).ToList();
            ErrorLogger.LogMessage($"[TorrentEngine] Trying peer batch {batchStart / batchSize + 1} ({batch.Count} peers)", "INFO");

            var peerTasks = batch.Select(async peerInfo =>
            {
                var peer = new PeerConnection(
                    peerInfo.IP, peerInfo.Port,
                    context.Metadata!.GetInfoHashBytes(), _peerId);

                try
                {
                    bool connected = await peer.ConnectAsync(8000);
                    if (connected)
                    {
                        ErrorLogger.LogMessage($"[TorrentEngine] ✓ Connected to peer: {peerInfo.IP}:{peerInfo.Port}", "INFO");
                        lock (context.PeerLock)
                        {
                            context.Peers.Add(peer);
                            peer.MessageReceived += (s, msg) => OnPeerMessageReceived(context, peer, msg);
                        }
                        Interlocked.Increment(ref successCount);
                        return;
                    }
                    peer.Dispose();
                    Interlocked.Increment(ref failCount);
                }
                catch
                {
                    peer.Dispose();
                    Interlocked.Increment(ref failCount);
                }
            });

            await Task.WhenAll(peerTasks);
        }

        ErrorLogger.LogMessage($"[TorrentEngine] Peer connection results: {successCount} connected, {failCount} failed", "INFO");
        
        int totalConnected;
        lock (context.PeerLock)
        {
            totalConnected = context.Peers.Count(p => p.IsConnected);
        }
        ErrorLogger.LogMessage($"[TorrentEngine] Total connected peers now: {totalConnected}", "INFO");

        return successCount;
    }

    /// <summary>
    /// Wait for incoming peers while periodically re-announcing to find new peers.
    /// Gives up after 3 re-announce cycles with no success.
    /// Returns true if at least one connected peer is available.
    /// </summary>
    private async Task<bool> WaitForPeerWithReAnnounce(TorrentContext context, CancellationToken ct)
    {
        const int maxRetries = 3;
        const int waitBetweenAnnouncesSeconds = 30;

        for (int attempt = 0; attempt < maxRetries && !ct.IsCancellationRequested; attempt++)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Waiting for incoming peers (attempt {attempt + 1}/{maxRetries})...", "INFO");
            ErrorLogger.LogMessage($"[TorrentEngine] Listening on port {_listenPort} - if any seeder sees our announce, they can connect to us", "INFO");

            // Wait for either an incoming peer or timeout
            try
            {
                bool gotPeer = await context.IncomingPeerSignal.WaitAsync(
                    TimeSpan.FromSeconds(waitBetweenAnnouncesSeconds), ct);

                if (gotPeer)
                {
                    lock (context.PeerLock)
                    {
                        if (context.Peers.Any(p => p.IsConnected))
                        {
                            ErrorLogger.LogMessage($"[TorrentEngine] ✓ Got incoming peer!", "INFO");
                            return true;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }

            // Re-announce to trackers to discover new peers
            ErrorLogger.LogMessage($"[TorrentEngine] Re-announcing to trackers...", "INFO");
            try
            {
                var response = await context.TrackerClient!.AnnounceAsync(
                    context.Torrent.Uploaded,
                    context.Torrent.Downloaded,
                    context.Torrent.TotalSize - context.Torrent.Downloaded,
                    TrackerEvent.Started
                );

                if (response.Peers.Count > 0)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Re-announce found {response.Peers.Count} peers, trying connections...", "INFO");
                    int connected = await TryConnectToPeersAsync(context, response.Peers, ct);
                    if (connected > 0) return true;
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Re-announce failed: {ex.Message}", "WARN");
            }
        }

        return false;
    }

    /// <summary>
    /// Safe wrapper — catches everything so the task never faults unobserved.
    /// </summary>
    private async Task ReAnnounceLoopSafeAsync(TorrentContext context, CancellationToken ct)
    {
        try
        {
            await ReAnnounceLoopAsync(context, ct);
        }
        catch (OperationCanceledException) { /* Expected during shutdown */ }
        catch (Exception ex)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] Re-announce loop crashed: {ex.Message}", "WARN");
        }
    }

    /// <summary>
    /// Periodic re-announce in background to discover new peers while downloading.
    /// </summary>
    private async Task ReAnnounceLoopAsync(TorrentContext context, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !context.PieceManager!.IsComplete)
        {
            try
            {
                // Wait 2 minutes between re-announces
                await Task.Delay(TimeSpan.FromMinutes(2), ct);

                ErrorLogger.LogMessage($"[TorrentEngine] Periodic re-announce...", "INFO");
                var response = await context.TrackerClient!.AnnounceAsync(
                    context.Torrent.Uploaded,
                    context.Torrent.Downloaded,
                    context.Torrent.TotalSize - context.Torrent.Downloaded,
                    TrackerEvent.Started
                );

                if (response.Peers.Count > 0)
                {
                    // Try connecting to any NEW peers we haven't tried
                    var existingIps = new HashSet<string>();
                    lock (context.PeerLock)
                    {
                        foreach (var p in context.Peers)
                            existingIps.Add($"{p.PeerIp}:{p.PeerPort}");
                    }

                    var newPeers = response.Peers
                        .Where(p => !existingIps.Contains($"{p.IP}:{p.Port}"))
                        .ToList();

                    if (newPeers.Count > 0)
                    {
                        ErrorLogger.LogMessage($"[TorrentEngine] Found {newPeers.Count} new peers, attempting connections...", "INFO");
                        int connected = await TryConnectToPeersAsync(context, newPeers, ct);
                        if (connected > 0)
                        {
                            await SendInterestedToAllPeers(context);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Re-announce error: {ex.Message}", "WARN");
            }
        }
    }

    /// <summary>
    /// Main piece download loop - downloads from connected peers.
    /// Resilient to peer disconnections - switches to another peer automatically.
    /// </summary>
    private async Task DownloadPiecesAsync(TorrentContext context, CancellationToken ct)
    {
        var torrent = context.Torrent;

        // Send interested to ALL connected peers so they can unchoke us
        await SendInterestedToAllPeers(context);

        int piecesDownloaded = 0;
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;

        while (!context.PieceManager!.IsComplete && !ct.IsCancellationRequested)
        {
            // Find an active, unchoked peer
            PeerConnection? activePeer = GetBestPeer(context);

            if (activePeer == null)
            {
                // No unchoked peers - try to get one
                activePeer = await WaitForUnchokedPeer(context, ct);
                if (activePeer == null)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] No usable peers after waiting. Giving up.", "ERROR");
                    torrent.State = TorrentState.Error;
                    TorrentUpdated?.Invoke(this, torrent);
                    return;
                }
            }

            // Choose piece
            var piece = context.PieceManager.GetNextPieceToDownload(
                activePeer.PeerBitfield ?? new BitField(0));
            
            if (piece == null)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] No pieces available from peer {activePeer.PeerIp}:{activePeer.PeerPort}", "WARN");
                await Task.Delay(2000, ct);
                continue;
            }

            ErrorLogger.LogMessage($"[TorrentEngine] Downloading piece {piece.Index}/{context.PieceManager.Pieces.Count} from {activePeer.PeerIp}:{activePeer.PeerPort}", "INFO");

            // Download all blocks in this piece
            bool pieceFailed = false;
            while (!piece.IsComplete && !ct.IsCancellationRequested && !pieceFailed)
            {
                // Check if peer is still alive before each block
                if (!activePeer.IsConnected)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Peer {activePeer.PeerIp}:{activePeer.PeerPort} disconnected mid-piece, resetting piece {piece.Index}", "WARN");
                    piece.Reset();
                    pieceFailed = true;
                    break;
                }

                var block = piece.GetNextBlockToRequest();
                if (block == null)
                {
                    // All blocks are either received or in-flight. Wait for them.
                    await Task.Delay(100, ct);
                    
                    // Check for timed-out blocks
                    foreach (var b in piece.Blocks)
                    {
                        if (b.IsTimedOut(TimeSpan.FromSeconds(30)))
                        {
                            ErrorLogger.LogMessage($"[TorrentEngine] Block {b.Offset} in piece {piece.Index} timed out, resetting", "WARN");
                            b.Reset();
                        }
                    }
                    continue;
                }

                try
                {
                    // Request block
                    piece.MarkBlockRequested((int)block.Offset);
                    await activePeer.RequestBlockAsync(piece.Index, (int)block.Offset, block.Length);

                    // Wait for block to be received (with timeout)
                    int waitMs = 0;
                    while (!block.IsReceived && waitMs < 15000 && !ct.IsCancellationRequested && activePeer.IsConnected)
                    {
                        await Task.Delay(100, ct);
                        waitMs += 100;
                    }

                    if (!block.IsReceived)
                    {
                        if (!activePeer.IsConnected)
                        {
                            ErrorLogger.LogMessage($"[TorrentEngine] Peer disconnected while waiting for block", "WARN");
                            piece.Reset();
                            pieceFailed = true;
                        }
                        else
                        {
                            ErrorLogger.LogMessage($"[TorrentEngine] Block {block.Offset} timed out, will retry", "WARN");
                            block.Reset();
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Peer disconnected mid-request - this is the exact bug we're fixing
                    ErrorLogger.LogMessage($"[TorrentEngine] Peer {activePeer.PeerIp}:{activePeer.PeerPort} died during request, switching peer", "WARN");
                    piece.Reset();
                    pieceFailed = true;
                }
                catch (IOException)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] IO error with peer {activePeer.PeerIp}:{activePeer.PeerPort}, switching peer", "WARN");
                    activePeer.Disconnect();
                    piece.Reset();
                    pieceFailed = true;
                }
                catch (SocketException)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Socket error with peer {activePeer.PeerIp}:{activePeer.PeerPort}, switching peer", "WARN");
                    activePeer.Disconnect();
                    piece.Reset();
                    pieceFailed = true;
                }
            }

            if (pieceFailed)
            {
                consecutiveFailures++;
                ErrorLogger.LogMessage($"[TorrentEngine] Piece {piece.Index} failed (consecutive failures: {consecutiveFailures}/{maxConsecutiveFailures})", "WARN");
                
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Too many consecutive failures. Re-announcing to find fresh peers...", "WARN");
                    try
                    {
                        var response = await context.TrackerClient!.AnnounceAsync(
                            torrent.Uploaded, torrent.Downloaded,
                            torrent.TotalSize - torrent.Downloaded, TrackerEvent.Started);
                        
                        if (response.Peers.Count > 0)
                        {
                            await TryConnectToPeersAsync(context, response.Peers, ct);
                            await SendInterestedToAllPeers(context);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger.LogMessage($"[TorrentEngine] Emergency re-announce failed: {ex.Message}", "WARN");
                    }
                    consecutiveFailures = 0;
                }
                
                // Small delay before retrying to avoid tight loop
                await Task.Delay(1000, ct);
                continue;
            }

            if (piece.IsComplete)
            {
                consecutiveFailures = 0; // Reset on success
                
                // Verify SHA-1
                ErrorLogger.LogMessage($"[TorrentEngine] Verifying piece {piece.Index}", "INFO");
                if (piece.Verify())
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Piece {piece.Index} verified ✓", "INFO");
                    
                    byte[] pieceData = piece.GetPieceData();
                    await context.DiskManager!.WritePieceAsync(piece.Index, pieceData);
                    
                    context.PieceManager.MarkPieceComplete(piece.Index);
                    piecesDownloaded++;
                    
                    torrent.Downloaded = context.PieceManager.GetDownloadedBytes();
                    torrent.Progress = context.PieceManager.GetProgress();
                    
                    ErrorLogger.LogMessage($"[TorrentEngine] Progress: {torrent.Progress:F2}% ({piecesDownloaded}/{context.PieceManager.Pieces.Count} pieces)", "INFO");
                    TorrentUpdated?.Invoke(this, torrent);
                }
                else
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Piece {piece.Index} hash verification FAILED, retrying", "ERROR");
                    piece.Reset();
                }
            }
        }
    }

    /// <summary>
    /// Send interested message to all connected peers so they may unchoke us.
    /// </summary>
    private async Task SendInterestedToAllPeers(TorrentContext context)
    {
        List<PeerConnection> connectedPeers;
        lock (context.PeerLock)
        {
            connectedPeers = context.Peers.Where(p => p.IsConnected).ToList();
        }

        ErrorLogger.LogMessage($"[TorrentEngine] Sending interested to {connectedPeers.Count} connected peers", "INFO");

        foreach (var peer in connectedPeers)
        {
            try
            {
                await peer.SendInterestedAsync();
            }
            catch (Exception ex)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Failed to send interested to {peer.PeerIp}:{peer.PeerPort}: {ex.Message}", "WARN");
            }
        }
    }

    /// <summary>
    /// Get the best available peer (connected, unchoked, has pieces we need).
    /// </summary>
    private PeerConnection? GetBestPeer(TorrentContext context)
    {
        lock (context.PeerLock)
        {
            // Prefer unchoked peers
            var unchoked = context.Peers
                .Where(p => p.IsConnected && !p.PeerChoking && p.PeerBitfield != null)
                .FirstOrDefault();
            
            return unchoked;
        }
    }

    /// <summary>
    /// Wait for an unchoked peer, sending interested messages and waiting for unchoke.
    /// </summary>
    private async Task<PeerConnection?> WaitForUnchokedPeer(TorrentContext context, CancellationToken ct)
    {
        // First check if we have any connected peers that are just choking us
        List<PeerConnection> chokingPeers;
        lock (context.PeerLock)
        {
            chokingPeers = context.Peers.Where(p => p.IsConnected && p.PeerChoking).ToList();
        }

        if (chokingPeers.Count > 0)
        {
            ErrorLogger.LogMessage($"[TorrentEngine] {chokingPeers.Count} peers are choking us, sending interested and waiting...", "INFO");
            
            // Send interested to all choking peers
            foreach (var peer in chokingPeers)
            {
                try { await peer.SendInterestedAsync(); } catch { }
            }

            // Wait up to 15 seconds for any peer to unchoke us
            for (int i = 0; i < 150 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(100, ct);
                var unchoked = GetBestPeer(context);
                if (unchoked != null)
                {
                    ErrorLogger.LogMessage($"[TorrentEngine] Peer {unchoked.PeerIp}:{unchoked.PeerPort} unchoked us!", "INFO");
                    return unchoked;
                }
            }
        }

        // No connected peers at all - wait for incoming or re-announce
        ErrorLogger.LogMessage($"[TorrentEngine] No usable peers. Waiting for incoming/re-announce...", "WARN");
        
        for (int attempt = 0; attempt < 3 && !ct.IsCancellationRequested; attempt++)
        {
            // Wait for incoming peer signal
            bool gotPeer = await context.IncomingPeerSignal.WaitAsync(TimeSpan.FromSeconds(30), ct);
            if (gotPeer)
            {
                await SendInterestedToAllPeers(context);
                await Task.Delay(3000, ct); // Give peers time to unchoke
                var unchoked = GetBestPeer(context);
                if (unchoked != null) return unchoked;
            }

            // Re-announce
            try
            {
                var response = await context.TrackerClient!.AnnounceAsync(
                    context.Torrent.Uploaded, context.Torrent.Downloaded,
                    context.Torrent.TotalSize - context.Torrent.Downloaded,
                    TrackerEvent.Started);

                if (response.Peers.Count > 0)
                {
                    int connected = await TryConnectToPeersAsync(context, response.Peers, ct);
                    if (connected > 0)
                    {
                        await SendInterestedToAllPeers(context);
                        await Task.Delay(3000, ct);
                        var unchoked = GetBestPeer(context);
                        if (unchoked != null) return unchoked;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogMessage($"[TorrentEngine] Re-announce failed: {ex.Message}", "WARN");
            }
        }

        return null;
    }

    private void OnPeerMessageReceived(TorrentContext context, PeerConnection peer, PeerMessage message)
    {
        if (message is PieceMessage pieceMsg)
        {
            var piece = context.PieceManager!.GetPiece(pieceMsg.Index);
            if (piece != null)
            {
                piece.ReceiveBlock(pieceMsg.Begin, pieceMsg.Block);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop listener
        try { _peerListener?.Dispose(); } catch { }

        // Stop all torrents — use try-catch on each to not lose others on failure
        foreach (var torrent in _activeTorrents.ToList())
        {
            try { StopTorrentAsync(torrent).Wait(TimeSpan.FromSeconds(5)); } catch { }
        }

        _activeTorrents.Clear();
    }
}

/// <summary>
/// Internal context for managing a torrent download
/// </summary>
internal class TorrentContext
{
    public TorrentDownload Torrent { get; set; } = null!;
    public TorrentMetadata? Metadata { get; set; }
    public PieceManager? PieceManager { get; set; }
    public DiskManager? DiskManager { get; set; }
    public TrackerClient? TrackerClient { get; set; }
    public List<PeerConnection> Peers { get; set; } = new();
    public object PeerLock { get; } = new object();
    public SemaphoreSlim IncomingPeerSignal { get; } = new SemaphoreSlim(0);
    public CancellationTokenSource? CancellationTokenSource { get; set; }
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
