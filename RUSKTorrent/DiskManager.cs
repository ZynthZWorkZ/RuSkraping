using System;
using System.IO;
using System.Threading.Tasks;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Handles all disk I/O operations for torrent downloads.
/// Writes pieces to disk and reads them back as needed.
/// </summary>
public class DiskManager : IDisposable
{
    private readonly string _downloadPath;
    private readonly TorrentMetadata _metadata;
    private readonly object _lock = new object();

    public DiskManager(string downloadPath, TorrentMetadata metadata)
    {
        _downloadPath = downloadPath;
        _metadata = metadata;
        
        // Create download directory if it doesn't exist
        Directory.CreateDirectory(_downloadPath);
    }

    /// <summary>
    /// Writes a complete piece to disk.
    /// </summary>
    public async Task WritePieceAsync(int pieceIndex, byte[] pieceData)
    {
        lock (_lock)
        {
            long pieceOffset = (long)pieceIndex * _metadata.PieceLength;
            WritePieceToFiles(pieceOffset, pieceData);
        }
    }

    /// <summary>
    /// Writes piece data across potentially multiple files.
    /// </summary>
    private void WritePieceToFiles(long pieceOffset, byte[] pieceData)
    {
        int dataOffset = 0;
        long bytesRemaining = pieceData.Length;

        foreach (var file in _metadata.Files)
        {
            // Check if this file contains part of this piece
            if (pieceOffset < file.Offset + file.Length && pieceOffset + pieceData.Length > file.Offset)
            {
                // Calculate where in the file to write
                long fileWriteOffset = Math.Max(0, pieceOffset - file.Offset);
                long pieceReadOffset = Math.Max(0, file.Offset - pieceOffset);
                long bytesToWrite = Math.Min(
                    file.Length - fileWriteOffset,
                    pieceData.Length - pieceReadOffset
                );

                if (bytesToWrite > 0)
                {
                    string filePath = Path.Combine(_downloadPath, file.Path);
                    
                    // Create directory structure for the file
                    string? directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Open or create file and write data
                    using var fileStream = new FileStream(
                        filePath,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.None
                    );

                    fileStream.Seek(fileWriteOffset, SeekOrigin.Begin);
                    fileStream.Write(pieceData, (int)pieceReadOffset, (int)bytesToWrite);
                    fileStream.Flush();
                }
            }
        }
    }

    /// <summary>
    /// Reads a piece from disk (for verification or seeding).
    /// </summary>
    public async Task<byte[]> ReadPieceAsync(int pieceIndex)
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                long pieceLength = CalculatePieceLength(pieceIndex);
                byte[] pieceData = new byte[pieceLength];
                long pieceOffset = (long)pieceIndex * _metadata.PieceLength;

                ReadPieceFromFiles(pieceOffset, pieceData);
                return pieceData;
            }
        });
    }

    private void ReadPieceFromFiles(long pieceOffset, byte[] buffer)
    {
        int bufferOffset = 0;

        foreach (var file in _metadata.Files)
        {
            if (pieceOffset < file.Offset + file.Length && pieceOffset + buffer.Length > file.Offset)
            {
                long fileReadOffset = Math.Max(0, pieceOffset - file.Offset);
                long pieceWriteOffset = Math.Max(0, file.Offset - pieceOffset);
                long bytesToRead = Math.Min(
                    file.Length - fileReadOffset,
                    buffer.Length - pieceWriteOffset
                );

                if (bytesToRead > 0)
                {
                    string filePath = Path.Combine(_downloadPath, file.Path);
                    
                    if (File.Exists(filePath))
                    {
                        using var fileStream = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read
                        );

                        fileStream.Seek(fileReadOffset, SeekOrigin.Begin);
                        fileStream.Read(buffer, (int)pieceWriteOffset, (int)bytesToRead);
                    }
                }
            }
        }
    }

    private long CalculatePieceLength(int pieceIndex)
    {
        if (pieceIndex == _metadata.PieceCount - 1)
        {
            long remainder = _metadata.TotalSize % _metadata.PieceLength;
            return remainder == 0 ? _metadata.PieceLength : remainder;
        }
        return _metadata.PieceLength;
    }

    /// <summary>
    /// Checks if all files exist and have the correct size.
    /// </summary>
    public bool VerifyFiles()
    {
        foreach (var file in _metadata.Files)
        {
            string filePath = Path.Combine(_downloadPath, file.Path);
            if (!File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length != file.Length)
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        // Cleanup if needed
        GC.SuppressFinalize(this);
    }
}
