using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RuSkraping.Models;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Handles communication with HTTP and UDP trackers to discover peers.
/// Implements parallel tracker announces for fast peer discovery.
/// Supports cookie-based authentication for private trackers (RuTracker).
/// </summary>
public class TrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _authHttpClient; // For private trackers needing cookies
    private readonly TorrentMetadata _metadata;
    private readonly string _peerId;
    private readonly int _port;
    private readonly bool _hasAuthCookies;
    
    // Connection timeout for individual tracker requests
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan UdpTimeout = TimeSpan.FromSeconds(5);
    
    // Maximum concurrent tracker requests
    private const int MaxConcurrentTrackers = 30;
    
    // Stop querying once we have this many peers
    private const int TargetPeerCount = 200;

    public TrackerClient(TorrentMetadata metadata, string peerId, int port = 6881)
    {
        _metadata = metadata;
        _peerId = peerId;
        _port = port;
        _httpClient = new HttpClient();
        _httpClient.Timeout = HttpTimeout;
        
        // Create authenticated HTTP client for private trackers (like RuTracker)
        var cookieContainer = new CookieContainer();
        _hasAuthCookies = false;
        
        try
        {
            var savedCookies = CookieStorage.LoadCookies();
            if (savedCookies != null && savedCookies.Count > 0)
            {
                foreach (var cookie in savedCookies)
                {
                    try
                    {
                        string domain = cookie.Domain;
                        if (string.IsNullOrEmpty(domain)) domain = ".rutracker.org";
                        if (!domain.StartsWith(".")) domain = "." + domain;
                        
                        cookieContainer.Add(new System.Net.Cookie(
                            cookie.Name, 
                            cookie.Value, 
                            cookie.Path ?? "/", 
                            domain
                        ));
                    }
                    catch { /* Skip invalid cookies */ }
                }
                _hasAuthCookies = true;
                ErrorLogger.LogMessage($"[TrackerClient] Loaded {savedCookies.Count} auth cookies for private tracker support", "INFO");
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogMessage($"[TrackerClient] Could not load auth cookies: {ex.Message}", "WARN");
        }
        
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _authHttpClient = new HttpClient(handler);
        _authHttpClient.Timeout = HttpTimeout;
        _authHttpClient.DefaultRequestHeaders.Add("User-Agent", "RUSKTorrent/1.0");
    }

    /// <summary>
    /// Announces to ALL trackers IN PARALLEL and combines peer lists from all successful responses.
    /// </summary>
    public async Task<TrackerResponse> AnnounceAsync(long uploaded, long downloaded, long left, TrackerEvent eventType = TrackerEvent.Started)
    {
        if (_metadata.Trackers.Count == 0)
        {
            throw new InvalidOperationException("No tracker URLs available");
        }

        ErrorLogger.LogMessage($"[TrackerClient] Starting parallel announce to {_metadata.Trackers.Count} trackers", "INFO");

        var allPeers = new List<PeerInfo>();
        int successfulTrackers = 0;
        int failedTrackers = 0;
        int skippedTrackers = 0;
        var lockObj = new object();

        // Use SemaphoreSlim to limit concurrency
        using var semaphore = new SemaphoreSlim(MaxConcurrentTrackers);
        using var globalCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // Global timeout for all trackers

        var tasks = _metadata.Trackers.Select(async trackerUrl =>
        {
            await semaphore.WaitAsync(globalCts.Token);
            try
            {
                // If we already have enough peers, skip remaining trackers
                lock (lockObj)
                {
                    if (allPeers.Count >= TargetPeerCount)
                    {
                        skippedTrackers++;
                        return;
                    }
                }

                List<PeerInfo> peers;

                if (trackerUrl.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                {
                    peers = await AnnounceUdpAsync(trackerUrl, uploaded, downloaded, left, eventType, globalCts.Token);
                }
                else if (trackerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         trackerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    peers = await AnnounceHttpAsync(trackerUrl, uploaded, downloaded, left, eventType, globalCts.Token);
                }
                else
                {
                    return; // Unknown protocol
                }

                lock (lockObj)
                {
                    successfulTrackers++;
                    foreach (var peer in peers)
                    {
                        if (!allPeers.Any(p => p.IP == peer.IP && p.Port == peer.Port))
                        {
                            allPeers.Add(peer);
                        }
                    }
                    if (peers.Count > 0)
                    {
                        ErrorLogger.LogMessage($"[TrackerClient] {trackerUrl} → {peers.Count} peers (total: {allPeers.Count})", "INFO");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (lockObj) { skippedTrackers++; }
            }
            catch (Exception ex)
            {
                lock (lockObj) { failedTrackers++; }
                // Only log the first few failures to avoid log spam
                if (failedTrackers <= 5)
                {
                    ErrorLogger.LogMessage($"[TrackerClient] FAIL {trackerUrl}: {ex.Message}", "WARN");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Global timeout reached, that's fine if we have peers
        }

        ErrorLogger.LogMessage($"[TrackerClient] Summary: {successfulTrackers} ok, {failedTrackers} failed, {skippedTrackers} skipped, {allPeers.Count} unique peers", "INFO");

        if (allPeers.Count > 0)
        {
            return new TrackerResponse
            {
                Interval = 1800,
                Peers = allPeers
            };
        }

        if (successfulTrackers == 0)
        {
            throw new Exception($"All {failedTrackers} trackers failed.");
        }

        throw new Exception($"No peers available. {successfulTrackers} trackers responded successfully but returned 0 peers. This torrent may be dead or from a private tracker.");
    }

    // ─────────────── HTTP TRACKER ───────────────

    /// <summary>
    /// Determines if a tracker URL belongs to a private tracker that needs cookie authentication.
    /// </summary>
    private bool IsPrivateTracker(string trackerUrl)
    {
        return trackerUrl.Contains("t-ru.org", StringComparison.OrdinalIgnoreCase) ||
               trackerUrl.Contains("rutracker", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<PeerInfo>> AnnounceHttpAsync(string trackerUrl, long uploaded, long downloaded, long left, TrackerEvent eventType, CancellationToken ct)
    {
        var infoHash = _metadata.GetInfoHashBytes();
        string requestUrl = BuildTrackerUrl(trackerUrl, infoHash, uploaded, downloaded, left, eventType);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(HttpTimeout);

        // Use authenticated client for private trackers (RuTracker)
        var client = (IsPrivateTracker(trackerUrl) && _hasAuthCookies) ? _authHttpClient : _httpClient;
        
        var response = await client.GetAsync(requestUrl, cts.Token);
        response.EnsureSuccessStatusCode();

        byte[] responseData = await response.Content.ReadAsByteArrayAsync(cts.Token);
        var trackerResponse = ParseHttpTrackerResponse(responseData);

        if (!trackerResponse.IsSuccess)
        {
            throw new Exception($"Tracker failure: {trackerResponse.FailureReason}");
        }

        return trackerResponse.Peers;
    }

    private string BuildTrackerUrl(string baseUrl, byte[] infoHash, long uploaded, long downloaded, long left, TrackerEvent eventType)
    {
        var sb = new StringBuilder(baseUrl);
        sb.Append(baseUrl.Contains('?') ? '&' : '?');
        sb.Append("info_hash=").Append(UrlEncodeBytes(infoHash));
        sb.Append("&peer_id=").Append(Uri.EscapeDataString(_peerId));
        sb.Append("&port=").Append(_port);
        sb.Append("&uploaded=").Append(uploaded);
        sb.Append("&downloaded=").Append(downloaded);
        sb.Append("&left=").Append(left);
        sb.Append("&compact=1");
        sb.Append("&numwant=200");
        sb.Append("&event=").Append(eventType.ToString().ToLowerInvariant());
        return sb.ToString();
    }

    private string UrlEncodeBytes(byte[] bytes)
    {
        var result = new StringBuilder();
        foreach (byte b in bytes)
        {
            if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') ||
                b == '-' || b == '_' || b == '.' || b == '~')
            {
                result.Append((char)b);
            }
            else
            {
                result.Append('%').Append(b.ToString("X2"));
            }
        }
        return result.ToString();
    }

    private TrackerResponse ParseHttpTrackerResponse(byte[] data)
    {
        var decoded = BencodeParser.Parse(data);
        if (decoded is not Dictionary<string, object> dict)
        {
            throw new Exception("Invalid tracker response format");
        }

        var response = new TrackerResponse();

        if (dict.ContainsKey("failure reason"))
        {
            response.FailureReason = Encoding.UTF8.GetString((byte[])dict["failure reason"]);
            return response;
        }

        if (dict.ContainsKey("interval"))
        {
            response.Interval = Convert.ToInt32(dict["interval"]);
        }

        if (dict.ContainsKey("peers"))
        {
            if (dict["peers"] is byte[] compactPeers)
            {
                response.Peers = ParseCompactPeers(compactPeers);
            }
            else if (dict["peers"] is List<object> peerList)
            {
                response.Peers = ParseDictionaryPeers(peerList);
            }
        }

        return response;
    }

    // ─────────────── UDP TRACKER (BEP-15) ───────────────

    private async Task<List<PeerInfo>> AnnounceUdpAsync(string trackerUrl, long uploaded, long downloaded, long left, TrackerEvent eventType, CancellationToken ct)
    {
        // Parse UDP tracker URL: udp://host:port/announce
        var uri = new Uri(trackerUrl);
        string host = uri.Host;
        int port = uri.Port > 0 ? uri.Port : 80;

        // Resolve host
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0)
                throw new Exception($"Could not resolve {host}");
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            throw new Exception($"DNS resolution failed for {host}");
        }

        // Prefer IPv4, but support IPv6 too
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        var selectedAddr = ipv4 ?? addresses[0];

        // Create UDP client matching the address family
        using var udpClient = new UdpClient(selectedAddr.AddressFamily);
        udpClient.Client.ReceiveTimeout = (int)UdpTimeout.TotalMilliseconds;
        udpClient.Client.SendTimeout = (int)UdpTimeout.TotalMilliseconds;

        var endpoint = new IPEndPoint(selectedAddr, port);

        // Step 1: CONNECT
        long connectionId = await UdpConnectAsync(udpClient, endpoint, ct);

        // Step 2: ANNOUNCE
        var peers = await UdpAnnounceAsync(udpClient, endpoint, connectionId, uploaded, downloaded, left, eventType, ct);

        return peers;
    }

    private async Task<long> UdpConnectAsync(UdpClient client, IPEndPoint endpoint, CancellationToken ct)
    {
        // Connection request:
        // Offset  Size    Name            Value
        // 0       64-bit  protocol_id     0x41727101980
        // 8       32-bit  action          0 (connect)
        // 12      32-bit  transaction_id  random
        
        int transactionId = Random.Shared.Next();
        byte[] request = new byte[16];
        
        // protocol_id = 0x41727101980 (magic constant)
        long protocolId = 0x41727101980;
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(protocolId)), 0, request, 0, 8);
        
        // action = 0 (connect)
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(0)), 0, request, 8, 4);
        
        // transaction_id
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(transactionId)), 0, request, 12, 4);

        await client.SendAsync(request, request.Length, endpoint);

        // Receive response with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(UdpTimeout);

        var receiveTask = client.ReceiveAsync(cts.Token);
        var result = await receiveTask;
        byte[] response = result.Buffer;

        if (response.Length < 16)
            throw new Exception("UDP connect response too short");

        // Parse response
        int actionResp = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response, 0));
        int transactionResp = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response, 4));

        if (actionResp != 0)
            throw new Exception($"UDP connect failed: action={actionResp}");
        if (transactionResp != transactionId)
            throw new Exception("UDP connect: transaction ID mismatch");

        long connectionId = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(response, 8));
        return connectionId;
    }

    private async Task<List<PeerInfo>> UdpAnnounceAsync(UdpClient client, IPEndPoint endpoint, long connectionId,
        long uploaded, long downloaded, long left, TrackerEvent eventType, CancellationToken ct)
    {
        // Announce request:
        // Offset  Size    Name            Value
        // 0       64-bit  connection_id   from connect
        // 8       32-bit  action          1 (announce)
        // 12      32-bit  transaction_id  random
        // 16      20-byte info_hash
        // 36      20-byte peer_id
        // 56      64-bit  downloaded
        // 64      64-bit  left
        // 72      64-bit  uploaded
        // 80      32-bit  event           0=none, 1=completed, 2=started, 3=stopped
        // 84      32-bit  IP address      0 (default)
        // 88      32-bit  key             random
        // 92      32-bit  num_want        -1 (default)
        // 96      16-bit  port

        int transactionId = Random.Shared.Next();
        byte[] request = new byte[98];

        // connection_id
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(connectionId)), 0, request, 0, 8);
        
        // action = 1 (announce)
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(1)), 0, request, 8, 4);
        
        // transaction_id
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(transactionId)), 0, request, 12, 4);
        
        // info_hash (20 bytes)
        Array.Copy(_metadata.GetInfoHashBytes(), 0, request, 16, 20);
        
        // peer_id (20 bytes)
        byte[] peerIdBytes = Encoding.ASCII.GetBytes(_peerId.PadRight(20, '\0').Substring(0, 20));
        Array.Copy(peerIdBytes, 0, request, 36, 20);
        
        // downloaded
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(downloaded)), 0, request, 56, 8);
        
        // left
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(left)), 0, request, 64, 8);
        
        // uploaded
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(uploaded)), 0, request, 72, 8);
        
        // event: 0=none, 1=completed, 2=started, 3=stopped
        int eventValue = eventType switch
        {
            TrackerEvent.Completed => 1,
            TrackerEvent.Started => 2,
            TrackerEvent.Stopped => 3,
            _ => 0
        };
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(eventValue)), 0, request, 80, 4);
        
        // IP address = 0 (default)
        Array.Copy(BitConverter.GetBytes(0), 0, request, 84, 4);
        
        // key (random)
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(Random.Shared.Next())), 0, request, 88, 4);
        
        // num_want = -1 (as many as possible)
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(-1)), 0, request, 92, 4);
        
        // port
        request[96] = (byte)((_port >> 8) & 0xFF);
        request[97] = (byte)(_port & 0xFF);

        await client.SendAsync(request, request.Length, endpoint);

        // Receive response with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(UdpTimeout);

        var receiveTask = client.ReceiveAsync(cts.Token);
        var result = await receiveTask;
        byte[] response = result.Buffer;

        if (response.Length < 20)
            throw new Exception("UDP announce response too short");

        // Parse response header
        int actionResp = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response, 0));
        int transactionResp = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response, 4));

        if (actionResp == 3) // Error
        {
            string errorMsg = response.Length > 8 ? Encoding.UTF8.GetString(response, 8, response.Length - 8) : "Unknown error";
            throw new Exception($"UDP tracker error: {errorMsg}");
        }

        if (actionResp != 1)
            throw new Exception($"UDP announce failed: action={actionResp}");
        if (transactionResp != transactionId)
            throw new Exception("UDP announce: transaction ID mismatch");

        // Parse peers from response (starting at offset 20, 6 bytes each)
        var peers = new List<PeerInfo>();
        for (int i = 20; i + 5 < response.Length; i += 6)
        {
            var ipAddress = new IPAddress(new byte[] { response[i], response[i + 1], response[i + 2], response[i + 3] });
            int peerPort = (response[i + 4] << 8) | response[i + 5];

            if (peerPort > 0 && !ipAddress.Equals(IPAddress.Any))
            {
                peers.Add(new PeerInfo
                {
                    IP = ipAddress.ToString(),
                    Port = peerPort
                });
            }
        }

        return peers;
    }

    // ─────────────── SHARED PARSERS ───────────────

    private List<PeerInfo> ParseCompactPeers(byte[] data)
    {
        var peers = new List<PeerInfo>();
        for (int i = 0; i + 5 < data.Length; i += 6)
        {
            var ipAddress = new IPAddress(new byte[] { data[i], data[i + 1], data[i + 2], data[i + 3] });
            int port = (data[i + 4] << 8) | data[i + 5];
            if (port > 0)
            {
                peers.Add(new PeerInfo { IP = ipAddress.ToString(), Port = port });
            }
        }
        return peers;
    }

    private List<PeerInfo> ParseDictionaryPeers(List<object> peerList)
    {
        var peers = new List<PeerInfo>();
        foreach (var item in peerList)
        {
            if (item is Dictionary<string, object> peerDict)
            {
                string? ip = peerDict.ContainsKey("ip") ? Encoding.UTF8.GetString((byte[])peerDict["ip"]) : null;
                int port = peerDict.ContainsKey("port") ? Convert.ToInt32(peerDict["port"]) : 0;
                string? peerId = peerDict.ContainsKey("peer id") ? Encoding.UTF8.GetString((byte[])peerDict["peer id"]) : null;
                if (!string.IsNullOrEmpty(ip) && port > 0)
                {
                    peers.Add(new PeerInfo { IP = ip, Port = port, PeerId = peerId });
                }
            }
        }
        return peers;
    }
}

public class TrackerResponse
{
    public string? FailureReason { get; set; }
    public int Interval { get; set; } = 1800;
    public List<PeerInfo> Peers { get; set; } = new();
    public bool IsSuccess => string.IsNullOrEmpty(FailureReason);
}

public class PeerInfo
{
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? PeerId { get; set; }
}

public enum TrackerEvent
{
    Started,
    Completed,
    Stopped
}
