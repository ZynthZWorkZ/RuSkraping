using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RuSkraping.Models;

namespace RuSkraping.Services;

/// <summary>
/// Service for searching RuTracker using HTTP requests (no Selenium/ChromeDriver needed)
/// Based on rusearch implementation
/// </summary>
public class RuTrackerSearchService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private const string BaseUrl = "https://rutracker.org";
    private bool _disposed = false;

    public RuTrackerSearchService()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Referer", $"{BaseUrl}/forum/index.php");
        _httpClient.DefaultRequestHeaders.Add("Origin", BaseUrl);

        // Register Windows-1251 encoding (required for RuTracker)
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Sets authentication cookies for the search requests
    /// </summary>
    public void SetCookies(List<CookieData> cookies)
    {
        var rutrackerUri = new Uri(BaseUrl);
        
        foreach (var cookie in cookies)
        {
            try
            {
                var netCookie = new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
                {
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly
                };

                if (cookie.Expires.HasValue)
                {
                    netCookie.Expires = cookie.Expires.Value;
                }

                _cookieContainer.Add(rutrackerUri, netCookie);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not add cookie {cookie.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Searches RuTracker and returns all results across pages
    /// </summary>
    /// <param name="searchQuery">Search query string</param>
    /// <param name="maxPages">Maximum number of pages to fetch (-1 for all pages)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="progressCallback">Callback for progress updates (currentPage, totalPages, results)</param>
    /// <returns>List of search results</returns>
    public async Task<List<TorrentSearchResult>> SearchAsync(
        string searchQuery, 
        int maxPages = -1, 
        CancellationToken cancellationToken = default,
        Action<int, int, List<TorrentSearchResult>>? progressCallback = null)
    {
        var allResults = new List<TorrentSearchResult>();

        // Prepare initial POST request
        string encodedQuery = Uri.EscapeDataString(searchQuery);
        string initialUrl = $"{BaseUrl}/forum/tracker.php?nm={encodedQuery}";
        var content = new StringContent(
            $"max=1&nm={Uri.EscapeDataString(searchQuery)}", 
            Encoding.UTF8, 
            "application/x-www-form-urlencoded");

        // Send initial request
        System.Diagnostics.Debug.WriteLine($"Sending search request to: {initialUrl}");
        var response = await _httpClient.PostAsync(initialUrl, content, cancellationToken);
        
        System.Diagnostics.Debug.WriteLine($"Response status: {response.StatusCode}");
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Search request failed: {response.StatusCode}");
        }

        // Get and decode content
        string decodedContent = await DecodeResponseAsync(response);
        System.Diagnostics.Debug.WriteLine($"Response length: {decodedContent.Length} characters");

        // Extract search_id from pagination links
        string? searchId = ExtractSearchId(decodedContent);
        System.Diagnostics.Debug.WriteLine($"Extracted search_id: {searchId ?? "null"}");

        // Parse first page results
        ParseResults(decodedContent, allResults, 1);
        System.Diagnostics.Debug.WriteLine($"First page results: {allResults.Count}");
        
        // Callback for first page
        progressCallback?.Invoke(1, 1, allResults);

        // Fetch additional pages if search_id was found
        if (!string.IsNullOrEmpty(searchId) && allResults.Count > 0)
        {
            int currentPage = 2;
            int startOffset = 50;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if we've reached max pages limit
                if (maxPages > 0 && currentPage > maxPages)
                {
                    break;
                }

                // Fetch next page
                string paginationUrl = $"{BaseUrl}/forum/tracker.php?search_id={searchId}&start={startOffset}&nm={Uri.EscapeDataString(searchQuery)}";

                var request = new HttpRequestMessage(HttpMethod.Get, paginationUrl);
                request.Headers.Add("Accept", "text/html");
                request.Headers.Add("Accept-Encoding", "identity");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                request.Headers.Add("Referer", $"{BaseUrl}/forum/tracker.php?nm={Uri.EscapeDataString(searchQuery)}");
                request.Headers.Add("Upgrade-Insecure-Requests", "1");

                var pageResponse = await _httpClient.SendAsync(request, cancellationToken);

                if (!pageResponse.IsSuccessStatusCode)
                {
                    break;
                }

                string pageContent = await DecodeResponseAsync(pageResponse);

                // Parse results from this page
                int resultsBefore = allResults.Count;
                ParseResults(pageContent, allResults, currentPage);
                int resultsAfter = allResults.Count;

                // Callback for progress
                progressCallback?.Invoke(currentPage, currentPage, allResults);

                // If no new results were found, we've reached the end
                if (resultsAfter == resultsBefore)
                {
                    break;
                }

                currentPage++;
                startOffset += 50;
                
                // Small delay to avoid overwhelming the server
                await Task.Delay(500, cancellationToken);
            }
        }

        return allResults;
    }

    /// <summary>
    /// Decodes HTTP response using Windows-1251 encoding
    /// </summary>
    private async Task<string> DecodeResponseAsync(HttpResponseMessage response)
    {
        byte[] rawBytes = await response.Content.ReadAsByteArrayAsync();

        try
        {
            var encoding1251 = Encoding.GetEncoding(1251);
            return encoding1251.GetString(rawBytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(rawBytes);
        }
    }

    /// <summary>
    /// Extracts search_id from HTML content for pagination
    /// </summary>
    private string? ExtractSearchId(string htmlContent)
    {
        // Pattern: tracker.php?search_id=XXXXX&start=50
        var searchIdPattern = new Regex(@"tracker\.php\?search_id=([^&""\s]+)", RegexOptions.IgnoreCase);
        var match = searchIdPattern.Match(htmlContent);

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Parses search results from HTML content
    /// </summary>
    private void ParseResults(string htmlContent, List<TorrentSearchResult> results, int pageNumber)
    {
        // Extract search-results div
        var searchResultsPattern = new Regex(@"<div id=""search-results""[^>]*>(.*?)</div>\s*</div>", RegexOptions.Singleline);
        var searchMatch = searchResultsPattern.Match(htmlContent);

        if (!searchMatch.Success)
        {
            return;
        }

        string resultsHtml = searchMatch.Groups[1].Value;

        // Extract table rows with data-topic_id
        var rowPattern = new Regex(@"<tr[^>]*data-topic_id=""(\d+)""[^>]*>(.*?)</tr>", RegexOptions.Singleline);
        var rows = rowPattern.Matches(resultsHtml);

        foreach (Match row in rows)
        {
            string topicId = row.Groups[1].Value;
            string rowHtml = row.Groups[2].Value;

            var result = new TorrentSearchResult
            {
                TopicId = topicId,
                Link = $"{BaseUrl}/forum/viewtopic.php?t={topicId}",
                Page = pageNumber
            };

            // Extract title
            var titlePattern = new Regex(@"<a[^>]*class=""[^""]*tLink[^""]*""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            var titleMatch = titlePattern.Match(rowHtml);
            result.Title = titleMatch.Success
                ? Regex.Replace(titleMatch.Groups[1].Value, @"<[^>]+>", "").Trim()
                : "N/A";
            result.Title = Regex.Replace(result.Title, @"\s+", " ");

            // Extract size and download URL
            var sizePattern = new Regex(@"<a[^>]*class=""[^""]*tr-dl[^""]*""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var sizeMatch = sizePattern.Match(rowHtml);

            if (!sizeMatch.Success)
            {
                sizePattern = new Regex(@"<a[^>]*href=""([^""]*dl\.php[^""]*)""[^>]*class=""[^""]*tr-dl[^""]*""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                sizeMatch = sizePattern.Match(rowHtml);
            }

            if (sizeMatch.Success)
            {
                string downloadHref = sizeMatch.Groups[1].Value.Trim();
                result.Size = Regex.Replace(sizeMatch.Groups[2].Value, @"<[^>]+>", "").Trim();
                result.Size = Regex.Replace(result.Size, @"&nbsp;", " ");
                result.Size = Regex.Replace(result.Size, @"\s+", " ");

                // Construct full download URL
                if (downloadHref.StartsWith("dl.php"))
                {
                    result.DownloadUrl = $"{BaseUrl}/forum/{downloadHref}";
                }
                else if (downloadHref.StartsWith("/forum/dl.php"))
                {
                    result.DownloadUrl = $"{BaseUrl}{downloadHref}";
                }
                else if (downloadHref.StartsWith("http"))
                {
                    result.DownloadUrl = downloadHref;
                }
                else
                {
                    result.DownloadUrl = $"{BaseUrl}/forum/{downloadHref}";
                }
            }
            else
            {
                result.Size = "N/A";
                result.DownloadUrl = string.Empty;
            }

            // Extract seeds
            var seedsPattern = new Regex(@"<b[^>]*class=""[^""]*seedmed[^""]*""[^>]*>(\d+)</b>");
            var seedsMatch = seedsPattern.Match(rowHtml);
            result.Seeds = seedsMatch.Success ? seedsMatch.Groups[1].Value : "0";

            // Extract leeches
            var leechesPattern = new Regex(@"<td[^>]*class=""[^""]*leechmed[^""]*""[^>]*>(\d+)</td>");
            var leechesMatch = leechesPattern.Match(rowHtml);
            result.Leeches = leechesMatch.Success ? leechesMatch.Groups[1].Value : "0";

            // Extract author
            var authorPattern = new Regex(@"<a[^>]*href=""tracker\.php\?pid=\d+""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            var authorMatch = authorPattern.Match(rowHtml);
            result.Author = authorMatch.Success ? authorMatch.Groups[1].Value.Trim() : "N/A";

            // Extract date
            var datePattern = new Regex(@"<p>(.*?)</p>", RegexOptions.Singleline);
            var dateMatch = datePattern.Match(rowHtml);
            result.Date = dateMatch.Success ? dateMatch.Groups[1].Value.Trim() : "N/A";

            results.Add(result);
        }
    }

    /// <summary>
    /// Fetches detailed torrent information from the viewtopic page
    /// </summary>
    /// <param name="topicId">The topic ID of the torrent</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated TorrentSearchResult with magnet URL, image, and description</returns>
    public async Task<(string magnetUrl, string imageUrl, string description)> FetchTorrentDetailsAsync(
        string topicId, 
        CancellationToken cancellationToken = default)
    {
        string magnetUrl = string.Empty;
        string imageUrl = string.Empty;
        string description = string.Empty;

        try
        {
            // Build the viewtopic URL
            string viewTopicUrl = $"{BaseUrl}/forum/viewtopic.php?t={topicId}";

            // Create request
            var request = new HttpRequestMessage(HttpMethod.Get, viewTopicUrl);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Encoding", "identity");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            request.Headers.Add("Referer", $"{BaseUrl}/forum/tracker.php");
            request.Headers.Add("Cache-Control", "max-age=0");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");

            // Send request
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch details for topic {topicId}: {response.StatusCode}");
                return (magnetUrl, imageUrl, description);
            }

            // Decode response
            string htmlContent = await DecodeResponseAsync(response);

            // Extract magnet URL
            magnetUrl = ExtractMagnetUrl(htmlContent);

            // Extract image URL
            imageUrl = ExtractImageUrl(htmlContent);

            // Extract description
            description = ExtractDescription(htmlContent);

            System.Diagnostics.Debug.WriteLine($"Fetched details for topic {topicId}:");
            System.Diagnostics.Debug.WriteLine($"  Magnet: {(string.IsNullOrEmpty(magnetUrl) ? "Not found" : "Found")}");
            System.Diagnostics.Debug.WriteLine($"  Image: {(string.IsNullOrEmpty(imageUrl) ? "Not found" : "Found")}");
            System.Diagnostics.Debug.WriteLine($"  Description: {description.Length} chars");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching details for topic {topicId}: {ex.Message}");
        }

        return (magnetUrl, imageUrl, description);
    }

    /// <summary>
    /// Extracts magnet URL from HTML content
    /// </summary>
    private string ExtractMagnetUrl(string htmlContent)
    {
        try
        {
            // Pattern: <a href="magnet:?xt=urn:btih:..." class="med magnet-link"
            var magnetPattern = new Regex(@"<a[^>]*class=""[^""]*magnet-link[^""]*""[^>]*href=""(magnet:[^""]+)""", RegexOptions.Singleline);
            var match = magnetPattern.Match(htmlContent);

            if (match.Success)
            {
                string magnetUrl = match.Groups[1].Value;
                // Decode HTML entities
                magnetUrl = System.Net.WebUtility.HtmlDecode(magnetUrl);
                return magnetUrl;
            }

            // Alternative pattern: href first, then class
            var alternatePattern = new Regex(@"<a[^>]*href=""(magnet:[^""]+)""[^>]*class=""[^""]*magnet-link[^""]*""", RegexOptions.Singleline);
            match = alternatePattern.Match(htmlContent);

            if (match.Success)
            {
                string magnetUrl = match.Groups[1].Value;
                magnetUrl = System.Net.WebUtility.HtmlDecode(magnetUrl);
                return magnetUrl;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting magnet URL: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts image URL from HTML content
    /// </summary>
    private string ExtractImageUrl(string htmlContent)
    {
        try
        {
            // Pattern 1: class contains "postImg" (anywhere in class value), then src
            // Matches: class="postImg", class="postImg postImgAligned img-right", etc.
            var imagePattern = new Regex(@"<img[^>]*class=""[^""]*postImg[^""]*""[^>]*src=""([^""]+)""", RegexOptions.Singleline);
            var match = imagePattern.Match(htmlContent);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Pattern 2: src first, then class contains "postImg"
            var alternatePattern = new Regex(@"<img[^>]*src=""([^""]+)""[^>]*class=""[^""]*postImg[^""]*""", RegexOptions.Singleline);
            match = alternatePattern.Match(htmlContent);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Pattern 3: Just look for any img tag with postImg in the class attribute
            var simplePattern = new Regex(@"<img[^>]*\bclass=""[^""]*postImg[^""]*""[^>]*>", RegexOptions.Singleline);
            match = simplePattern.Match(htmlContent);

            if (match.Success)
            {
                // Extract src from the matched img tag
                var srcPattern = new Regex(@"src=""([^""]+)""");
                var srcMatch = srcPattern.Match(match.Value);
                if (srcMatch.Success)
                {
                    return srcMatch.Groups[1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting image URL: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts description/post content from HTML and converts to clean text
    /// </summary>
    private string ExtractDescription(string htmlContent)
    {
        try
        {
            // Pattern: Find the post body content
            var postBodyPattern = new Regex(@"<div class=""post_body""[^>]*>(.*?)</div>\s*</td>", RegexOptions.Singleline);
            var match = postBodyPattern.Match(htmlContent);

            if (match.Success)
            {
                string rawDescription = match.Groups[1].Value;
                
                // Remove script tags
                rawDescription = Regex.Replace(rawDescription, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                // Remove style tags
                rawDescription = Regex.Replace(rawDescription, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                // Remove image tags (we extract images separately)
                rawDescription = Regex.Replace(rawDescription, @"<img[^>]*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                // Convert <br> tags to newlines
                rawDescription = Regex.Replace(rawDescription, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                
                // Convert </p> and </div> to double newlines for paragraphs
                rawDescription = Regex.Replace(rawDescription, @"</p>", "\n\n", RegexOptions.IgnoreCase);
                rawDescription = Regex.Replace(rawDescription, @"</div>", "\n", RegexOptions.IgnoreCase);
                
                // Convert <li> to bullet points
                rawDescription = Regex.Replace(rawDescription, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
                rawDescription = Regex.Replace(rawDescription, @"</li>", "\n", RegexOptions.IgnoreCase);
                
                // Remove all remaining HTML tags
                rawDescription = Regex.Replace(rawDescription, @"<[^>]+>", "", RegexOptions.Singleline);
                
                // Decode HTML entities
                rawDescription = System.Net.WebUtility.HtmlDecode(rawDescription);
                
                // Clean up whitespace
                rawDescription = Regex.Replace(rawDescription, @"[ \t]+", " "); // Multiple spaces to single
                rawDescription = Regex.Replace(rawDescription, @"\n[ \t]+", "\n"); // Remove leading spaces on lines
                rawDescription = Regex.Replace(rawDescription, @"[ \t]+\n", "\n"); // Remove trailing spaces on lines
                rawDescription = Regex.Replace(rawDescription, @"\n{3,}", "\n\n"); // Max 2 consecutive newlines
                
                rawDescription = rawDescription.Trim();
                
                // Limit length for display
                if (rawDescription.Length > 5000)
                {
                    rawDescription = rawDescription.Substring(0, 5000) + "...";
                }
                
                return rawDescription;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting description: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// Downloads a torrent file
    /// </summary>
    public async Task<byte[]> DownloadTorrentAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Add("Referer", $"{BaseUrl}/forum/tracker.php");
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("Accept-Encoding", "identity");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }
}

