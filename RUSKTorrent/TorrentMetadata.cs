using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Represents parsed .torrent file metadata
/// </summary>
public class TorrentMetadata
{
    public string Name { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public int PieceLength { get; set; }
    public byte[] Pieces { get; set; } = Array.Empty<byte>();
    public List<TorrentFileInfo> Files { get; set; } = new();
    public List<string> Trackers { get; set; } = new();
    public string Comment { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? CreationDate { get; set; }
    public byte[] InfoHash { get; set; } = Array.Empty<byte>();
    public bool IsPrivate { get; set; }

    public int PieceCount => Pieces.Length / 20; // SHA-1 hashes are 20 bytes

    public static TorrentMetadata Parse(byte[] torrentData)
    {
        var decoded = BencodeParser.Parse(torrentData) as Dictionary<string, object?>;
        if (decoded == null)
            throw new FormatException("Invalid torrent file format");

        var metadata = new TorrentMetadata();

        // Parse announce (tracker)
        if (decoded.ContainsKey("announce"))
        {
            var announceBytes = decoded["announce"] as byte[];
            if (announceBytes != null)
                metadata.Trackers.Add(Encoding.UTF8.GetString(announceBytes));
        }

        // Parse announce-list (multiple trackers)
        if (decoded.ContainsKey("announce-list"))
        {
            var announceList = decoded["announce-list"] as List<object?>;
            if (announceList != null)
            {
                foreach (var tier in announceList)
                {
                    if (tier is List<object?> tierList)
                    {
                        foreach (var tracker in tierList)
                        {
                            if (tracker is byte[] trackerBytes)
                            {
                                string trackerUrl = Encoding.UTF8.GetString(trackerBytes);
                                if (!metadata.Trackers.Contains(trackerUrl))
                                    metadata.Trackers.Add(trackerUrl);
                            }
                        }
                    }
                }
            }
        }

        // Parse comment
        if (decoded.ContainsKey("comment"))
        {
            var commentBytes = decoded["comment"] as byte[];
            if (commentBytes != null)
                metadata.Comment = Encoding.UTF8.GetString(commentBytes);
        }

        // Parse created by
        if (decoded.ContainsKey("created by"))
        {
            var createdByBytes = decoded["created by"] as byte[];
            if (createdByBytes != null)
                metadata.CreatedBy = Encoding.UTF8.GetString(createdByBytes);
        }

        // Parse creation date
        if (decoded.ContainsKey("creation date"))
        {
            if (decoded["creation date"] is long timestamp)
                metadata.CreationDate = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
        }

        // Parse info dictionary
        if (!decoded.ContainsKey("info"))
            throw new FormatException("Torrent file missing 'info' dictionary");

        var info = decoded["info"] as Dictionary<string, object?>;
        if (info == null)
            throw new FormatException("Invalid 'info' dictionary");

        // Calculate info hash (SHA-1 of bencoded info dictionary)
        metadata.InfoHash = CalculateInfoHash(torrentData);

        // Parse name
        if (info.ContainsKey("name"))
        {
            var nameBytes = info["name"] as byte[];
            if (nameBytes != null)
                metadata.Name = Encoding.UTF8.GetString(nameBytes);
        }

        // Parse piece length
        if (info.ContainsKey("piece length") && info["piece length"] is long pieceLength)
        {
            metadata.PieceLength = (int)pieceLength;
        }

        // Parse pieces (SHA-1 hashes)
        if (info.ContainsKey("pieces"))
        {
            metadata.Pieces = info["pieces"] as byte[] ?? Array.Empty<byte>();
        }

        // Parse private flag
        if (info.ContainsKey("private") && info["private"] is long privateFlag)
        {
            metadata.IsPrivate = privateFlag == 1;
        }

        // Parse files (single-file vs multi-file mode)
        if (info.ContainsKey("files"))
        {
            // Multi-file mode
            var files = info["files"] as List<object?>;
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file is Dictionary<string, object?> fileDict)
                    {
                        var fileInfo = new TorrentFileInfo();

                        if (fileDict.ContainsKey("length") && fileDict["length"] is long length)
                            fileInfo.Length = length;

                        if (fileDict.ContainsKey("path") && fileDict["path"] is List<object?> pathParts)
                        {
                            var pathComponents = new List<string>();
                            foreach (var part in pathParts)
                            {
                                if (part is byte[] partBytes)
                                    pathComponents.Add(Encoding.UTF8.GetString(partBytes));
                            }
                            fileInfo.Path = string.Join(Path.DirectorySeparatorChar.ToString(), pathComponents);
                        }

                        metadata.Files.Add(fileInfo);
                        metadata.TotalSize += fileInfo.Length;
                    }
                }
            }
        }
        else if (info.ContainsKey("length"))
        {
            // Single-file mode
            var fileInfo = new TorrentFileInfo
            {
                Path = metadata.Name,
                Length = info["length"] is long length ? length : 0
            };
            metadata.Files.Add(fileInfo);
            metadata.TotalSize = fileInfo.Length;
        }

        return metadata;
    }

    private static byte[] CalculateInfoHash(byte[] torrentData)
    {
        // Find the info dictionary in the bencoded data
        int infoStart = FindInfoDictionaryStart(torrentData);
        if (infoStart < 0)
            throw new FormatException("Could not find info dictionary");

        // Parse to find the end of the info dictionary
        int position = infoStart;
        BencodeParser.Parse(torrentData); // This validates the structure

        // Extract just the info dictionary bytes
        int infoEnd = FindInfoDictionaryEnd(torrentData, infoStart);
        byte[] infoBytes = new byte[infoEnd - infoStart];
        Array.Copy(torrentData, infoStart, infoBytes, 0, infoBytes.Length);

        // Calculate SHA-1 hash
        using var sha1 = SHA1.Create();
        return sha1.ComputeHash(infoBytes);
    }

    private static int FindInfoDictionaryStart(byte[] data)
    {
        // Look for "4:info" pattern (the key "info" in bencode)
        byte[] pattern = Encoding.UTF8.GetBytes("4:info");
        
        for (int i = 0; i < data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i + pattern.Length; // Return position after "4:info"
        }

        return -1;
    }

    private static int FindInfoDictionaryEnd(byte[] data, int start)
    {
        int depth = 0;
        int position = start;

        while (position < data.Length)
        {
            byte current = data[position];

            if (current == 'd' || current == 'l')
            {
                depth++;
            }
            else if (current == 'e')
            {
                if (depth == 0)
                    return position + 1;
                depth--;
            }

            position++;
        }

        throw new FormatException("Could not find end of info dictionary");
    }

    public string GetInfoHashString()
    {
        return BitConverter.ToString(InfoHash).Replace("-", "").ToUpperInvariant();
    }
}

public class TorrentFileInfo
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
}
