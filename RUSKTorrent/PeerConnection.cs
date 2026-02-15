using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RuSkraping.RUSKTorrent.Messages;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Manages a TCP connection to a single peer.
/// Handles handshake, message sending/receiving, and state management.
/// </summary>
public class PeerConnection : IDisposable
{
    public string PeerIp { get; }
    public int PeerPort { get; }
    public bool IsConnected { get; private set; }
    public bool AmChoking { get; set; } = true;
    public bool AmInterested { get; set; } = false;
    public bool PeerChoking { get; set; } = true;
    public bool PeerInterested { get; set; } = false;
    public BitField? PeerBitfield { get; private set; }

    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly byte[] _infoHash;
    private readonly string _peerId;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _cts;

    public event EventHandler<PeerMessage>? MessageReceived;
    public event EventHandler? Disconnected;

    public PeerConnection(string peerIp, int peerPort, byte[] infoHash, string peerId)
    {
        PeerIp = peerIp;
        PeerPort = peerPort;
        _infoHash = infoHash;
        _peerId = peerId;
    }

    /// <summary>
    /// Creates a PeerConnection from an already-connected TcpClient (incoming connection).
    /// The incoming handshake has already been read by PeerListener; we just need to send ours back.
    /// </summary>
    public static async Task<PeerConnection?> CreateFromIncomingAsync(
        TcpClient client, byte[] infoHash, string ourPeerId)
    {
        var remoteEp = client.Client.RemoteEndPoint;
        string peerIp = "unknown";
        int peerPort = 0;
        
        if (remoteEp is IPEndPoint ipEndPoint)
        {
            peerIp = ipEndPoint.Address.ToString();
            peerPort = ipEndPoint.Port;
        }

        var peer = new PeerConnection(peerIp, peerPort, infoHash, ourPeerId);
        
        try
        {
            peer._client = client;
            peer._stream = client.GetStream();
            peer._cts = new CancellationTokenSource();
            peer.IsConnected = true;

            // Send our handshake response
            var handshake = new Messages.HandshakeMessage
            {
                InfoHash = infoHash,
                PeerId = ourPeerId
            };
            byte[] handshakeData = handshake.Encode();
            await peer._stream.WriteAsync(handshakeData, 0, handshakeData.Length);
            await peer._stream.FlushAsync();

            ErrorLogger.LogMessage($"[PeerConnection] Incoming peer connected: {peerIp}:{peerPort}", "INFO");

            // Start receiving messages
            _ = Task.Run(() => peer.ReceiveMessagesAsync(peer._cts.Token));

            return peer;
        }
        catch (Exception ex)
        {
            ErrorLogger.LogMessage($"[PeerConnection] Failed to set up incoming peer {peerIp}:{peerPort}: {ex.Message}", "WARN");
            peer.Disconnect();
            return null;
        }
    }

    /// <summary>
    /// Connects to the peer and performs handshake (outgoing connection).
    /// </summary>
    public async Task<bool> ConnectAsync(int timeoutMs = 10000)
    {
        try
        {
            ErrorLogger.LogMessage($"[PeerConnection] Connecting to {PeerIp}:{PeerPort}", "INFO");
            _client = new TcpClient();
            _cts = new CancellationTokenSource();

            // Connect with timeout
            var connectTask = _client.ConnectAsync(PeerIp, PeerPort);
            var timeoutTask = Task.Delay(timeoutMs);
            
            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                ErrorLogger.LogMessage($"[PeerConnection] Connection timeout to {PeerIp}:{PeerPort}", "WARN");
                _client.Close();
                return false;
            }

            // Await the connect task to ensure it succeeded (might have thrown exception)
            await connectTask;

            // Verify the client is actually connected
            if (!_client.Connected)
            {
                ErrorLogger.LogMessage($"[PeerConnection] Connection failed to {PeerIp}:{PeerPort} (not connected)", "WARN");
                _client.Close();
                return false;
            }

            _stream = _client.GetStream();
            IsConnected = true;
            ErrorLogger.LogMessage($"[PeerConnection] TCP connected to {PeerIp}:{PeerPort}", "INFO");

            // Perform handshake
            bool handshakeSuccess = await PerformHandshakeAsync();
            if (!handshakeSuccess)
            {
                ErrorLogger.LogMessage($"[PeerConnection] Handshake failed with {PeerIp}:{PeerPort}", "WARN");
                Disconnect();
                return false;
            }

            ErrorLogger.LogMessage($"[PeerConnection] Handshake successful with {PeerIp}:{PeerPort}", "INFO");

            // Start receiving messages
            _ = Task.Run(() => ReceiveMessagesAsync(_cts.Token));

            return true;
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, $"[PeerConnection] Connection error to {PeerIp}:{PeerPort}");
            Disconnect();
            return false;
        }
    }

    private async Task<bool> PerformHandshakeAsync()
    {
        try
        {
            // Send handshake
            var handshake = new HandshakeMessage
            {
                InfoHash = _infoHash,
                PeerId = _peerId
            };

            byte[] handshakeData = handshake.Encode();
            await _stream!.WriteAsync(handshakeData, 0, handshakeData.Length);

            // Receive handshake response
            byte[] responseData = new byte[68];
            int bytesRead = 0;
            while (bytesRead < 68)
            {
                int read = await _stream.ReadAsync(responseData, bytesRead, 68 - bytesRead);
                if (read == 0)
                    return false;
                bytesRead += read;
            }

            var responseHandshake = HandshakeMessage.Decode(responseData);
            if (responseHandshake == null)
                return false;

            // Verify info hash
            if (!responseHandshake.InfoHash.AsSpan().SequenceEqual(_infoHash))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                // Read message length (4 bytes, big-endian)
                byte[] lengthBytes = new byte[4];
                int bytesRead = await _stream!.ReadAsync(lengthBytes, 0, 4, ct);
                if (bytesRead == 0)
                {
                    Disconnect();
                    return;
                }

                int messageLength = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) | (lengthBytes[2] << 8) | lengthBytes[3];

                // Keep-alive message
                if (messageLength == 0)
                {
                    continue;
                }

                // Read message payload
                byte[] messageData = new byte[messageLength];
                bytesRead = 0;
                while (bytesRead < messageLength)
                {
                    int read = await _stream.ReadAsync(messageData, bytesRead, messageLength - bytesRead, ct);
                    if (read == 0)
                    {
                        Disconnect();
                        return;
                    }
                    bytesRead += read;
                }

                // Parse and handle message
                var message = PeerMessage.Decode(messageData);
                if (message != null)
                {
                    await HandleMessageAsync(message);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disconnect();
        }
    }

    private async Task HandleMessageAsync(PeerMessage message)
    {
        switch (message.Type)
        {
            case MessageType.Choke:
                PeerChoking = true;
                break;
                
            case MessageType.Unchoke:
                PeerChoking = false;
                break;
                
            case MessageType.Interested:
                PeerInterested = true;
                break;
                
            case MessageType.NotInterested:
                PeerInterested = false;
                break;
                
            case MessageType.Bitfield:
                var bitfieldMsg = (BitfieldMessage)message;
                PeerBitfield = new BitField(bitfieldMsg.Bitfield, bitfieldMsg.Bitfield.Length * 8);
                break;
        }

        // Notify listeners
        MessageReceived?.Invoke(this, message);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Sends a message to the peer.
    /// </summary>
    public async Task SendMessageAsync(PeerMessage message)
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("Not connected");

        await _sendLock.WaitAsync();
        try
        {
            byte[] messageData = message.Encode();
            
            // Write message length (4 bytes, big-endian)
            byte[] lengthBytes = new byte[4];
            lengthBytes[0] = (byte)(messageData.Length >> 24);
            lengthBytes[1] = (byte)(messageData.Length >> 16);
            lengthBytes[2] = (byte)(messageData.Length >> 8);
            lengthBytes[3] = (byte)messageData.Length;

            await _stream.WriteAsync(lengthBytes, 0, 4);
            
            // Write message payload
            if (messageData.Length > 0)
            {
                await _stream.WriteAsync(messageData, 0, messageData.Length);
            }

            await _stream.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends an interested message to the peer.
    /// </summary>
    public async Task SendInterestedAsync()
    {
        await SendMessageAsync(new InterestedMessage());
        AmInterested = true;
    }

    /// <summary>
    /// Requests a block from the peer.
    /// </summary>
    public async Task RequestBlockAsync(int pieceIndex, int blockOffset, int blockLength)
    {
        var request = new RequestMessage
        {
            Index = pieceIndex,
            Begin = blockOffset,
            Length = blockLength
        };
        await SendMessageAsync(request);
    }

    public void Disconnect()
    {
        if (!IsConnected)
            return;

        IsConnected = false;
        _cts?.Cancel();
        _stream?.Close();
        _client?.Close();
        
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Disconnect();
        _sendLock?.Dispose();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
