using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RuSkraping.RUSKTorrent.Messages;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Listens for incoming peer connections on a TCP port.
/// When a peer connects to us (instead of us connecting to them),
/// it reads the handshake to identify which torrent they want,
/// then raises an event so TorrentEngine can handle it.
/// All background tasks are fully self-contained — exceptions never escape unobserved.
/// </summary>
public class PeerListener : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// The port we're actually listening on.
    /// </summary>
    public int ListenPort { get; private set; }

    /// <summary>
    /// Whether the listener is currently active.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    /// Fired when an incoming peer completes handshake.
    /// Args: (TcpClient client, byte[] infoHash, string remotePeerId)
    /// </summary>
    public event Action<TcpClient, byte[], string>? IncomingPeerHandshaked;

    /// <summary>
    /// Starts listening for incoming peer connections.
    /// Tries ports 6881-6999 to find an available one.
    /// </summary>
    public async Task<int> StartAsync(int preferredPort = 6881)
    {
        _cts = new CancellationTokenSource();

        // Try ports 6881-6999
        for (int port = preferredPort; port < preferredPort + 119; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                ListenPort = port;
                IsListening = true;
                ErrorLogger.LogMessage($"[PeerListener] Listening for incoming peers on port {port}", "INFO");

                // Accept connections in background — fully observed
                _ = AcceptLoopSafeAsync(_cts.Token);
                return port;
            }
            catch (SocketException)
            {
                // Port in use, try next
                try { _listener?.Stop(); } catch { }
            }
        }

        ErrorLogger.LogMessage("[PeerListener] Could not find an available port (tried 6881-6999)", "WARN");
        return 0; // Return 0 to indicate failure (non-fatal)
    }

    /// <summary>
    /// Safe wrapper — catches everything so the task never faults unobserved.
    /// </summary>
    private async Task AcceptLoopSafeAsync(CancellationToken ct)
    {
        try
        {
            await AcceptLoopAsync(ct);
        }
        catch (Exception)
        {
            // Swallow — expected during shutdown (OperationCanceledException,
            // ObjectDisposedException, SocketException with Interrupted, etc.)
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsListening)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                ErrorLogger.LogMessage($"[PeerListener] Incoming connection from {remoteEp}", "INFO");

                // Handle handshake in a separate task — fully observed
                _ = HandleIncomingSafeAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    ErrorLogger.LogMessage($"[PeerListener] Accept error: {ex.Message}", "WARN");
            }
        }
    }

    /// <summary>
    /// Safe wrapper — catches everything so the task never faults unobserved.
    /// </summary>
    private async Task HandleIncomingSafeAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            await HandleIncomingAsync(client, ct);
        }
        catch (Exception ex)
        {
            // Swallow — network errors during incoming handshake are expected
            if (ex is not OperationCanceledException)
            {
                var remoteEp = "unknown";
                try { remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown"; } catch { }
                ErrorLogger.LogMessage($"[PeerListener] Error handling incoming peer {remoteEp}: {ex.Message}", "WARN");
            }
            try { client.Close(); } catch { }
        }
    }

    private async Task HandleIncomingAsync(TcpClient client, CancellationToken ct)
    {
        var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        var stream = client.GetStream();

        // Read the 68-byte handshake with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        byte[] handshakeData = new byte[68];
        int bytesRead = 0;
        while (bytesRead < 68)
        {
            int read = await stream.ReadAsync(handshakeData, bytesRead, 68 - bytesRead, cts.Token);
            if (read == 0)
            {
                ErrorLogger.LogMessage($"[PeerListener] Peer {remoteEp} disconnected during handshake", "WARN");
                client.Close();
                return;
            }
            bytesRead += read;
        }

        // Decode the handshake
        var handshake = HandshakeMessage.Decode(handshakeData);
        if (handshake == null)
        {
            ErrorLogger.LogMessage($"[PeerListener] Invalid handshake from {remoteEp}", "WARN");
            client.Close();
            return;
        }

        string infoHashStr = BitConverter.ToString(handshake.InfoHash).Replace("-", "").ToUpperInvariant();
        ErrorLogger.LogMessage($"[PeerListener] Peer {remoteEp} wants torrent {infoHashStr.Substring(0, 8)}...", "INFO");

        // Raise event - TorrentEngine will check if this info hash matches an active torrent
        IncomingPeerHandshaked?.Invoke(client, handshake.InfoHash, handshake.PeerId);
    }

    public void Stop()
    {
        IsListening = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { }
        GC.SuppressFinalize(this);
    }
}
