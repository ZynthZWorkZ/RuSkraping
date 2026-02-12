using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Parser and container for magnet links
/// Format: magnet:?xt=urn:btih:<info-hash>&dn=<name>&tr=<tracker>&...
/// </summary>
public class MagnetLink
{
    public string InfoHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Trackers { get; set; } = new();
    public List<string> WebSeeds { get; set; } = new();
    public long? ExactLength { get; set; }

    public static MagnetLink Parse(string magnetUri)
    {
        if (!magnetUri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid magnet link format");

        var magnet = new MagnetLink();
        
        // Remove "magnet:?" prefix
        string queryString = magnetUri.Substring(8);
        
        // Parse query parameters
        var parameters = ParseQueryString(queryString);

        // Extract info hash from xt (exact topic)
        if (parameters.ContainsKey("xt"))
        {
            string xt = parameters["xt"].First();
            if (xt.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
            {
                magnet.InfoHash = xt.Substring(9).ToUpperInvariant();
                
                // Remove any & characters that might be in the hash
                magnet.InfoHash = magnet.InfoHash.Split('&')[0];
            }
        }

        // Extract display name (dn)
        if (parameters.ContainsKey("dn"))
        {
            magnet.DisplayName = Uri.UnescapeDataString(parameters["dn"].First());
        }

        // Extract trackers (tr)
        if (parameters.ContainsKey("tr"))
        {
            magnet.Trackers = parameters["tr"]
                .Select(Uri.UnescapeDataString)
                .ToList();
        }

        // Extract web seeds (ws)
        if (parameters.ContainsKey("ws"))
        {
            magnet.WebSeeds = parameters["ws"]
                .Select(Uri.UnescapeDataString)
                .ToList();
        }

        // Extract exact length (xl)
        if (parameters.ContainsKey("xl"))
        {
            if (long.TryParse(parameters["xl"].First(), out long length))
            {
                magnet.ExactLength = length;
            }
        }

        if (string.IsNullOrEmpty(magnet.InfoHash))
            throw new ArgumentException("Magnet link must contain info hash");

        return magnet;
    }

    private static Dictionary<string, List<string>> ParseQueryString(string query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string pair in query.Split('&'))
        {
            if (string.IsNullOrWhiteSpace(pair))
                continue;

            string[] parts = pair.Split('=', 2);
            string key = parts[0];
            string value = parts.Length > 1 ? parts[1] : "";

            if (!result.ContainsKey(key))
                result[key] = new List<string>();

            result[key].Add(value);
        }

        return result;
    }

    public string ToMagnetUri()
    {
        var parts = new List<string>();

        // Add info hash
        parts.Add($"xt=urn:btih:{InfoHash}");

        // Add display name
        if (!string.IsNullOrEmpty(DisplayName))
        {
            parts.Add($"dn={Uri.EscapeDataString(DisplayName)}");
        }

        // Add trackers
        foreach (var tracker in Trackers)
        {
            parts.Add($"tr={Uri.EscapeDataString(tracker)}");
        }

        // Add web seeds
        foreach (var webSeed in WebSeeds)
        {
            parts.Add($"ws={Uri.EscapeDataString(webSeed)}");
        }

        // Add exact length
        if (ExactLength.HasValue)
        {
            parts.Add($"xl={ExactLength.Value}");
        }

        return "magnet:?" + string.Join("&", parts);
    }

    public byte[] GetInfoHashBytes()
    {
        // Convert hex string to bytes
        if (InfoHash.Length == 40) // SHA-1 hash (20 bytes in hex)
        {
            byte[] bytes = new byte[20];
            for (int i = 0; i < 20; i++)
            {
                bytes[i] = Convert.ToByte(InfoHash.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        else if (InfoHash.Length == 32) // Base32 encoded
        {
            return DecodeBase32(InfoHash);
        }

        throw new FormatException("Invalid info hash format");
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.ToUpperInvariant().TrimEnd('=');
        
        int outputLength = input.Length * 5 / 8;
        byte[] output = new byte[outputLength];

        int buffer = 0;
        int bitsInBuffer = 0;
        int outputIndex = 0;

        foreach (char c in input)
        {
            int value = alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"Invalid character in base32 string: {c}");

            buffer = (buffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                output[outputIndex++] = (byte)(buffer >> (bitsInBuffer - 8));
                bitsInBuffer -= 8;
            }
        }

        return output;
    }
}
