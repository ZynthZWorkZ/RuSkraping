using System;
using System.Collections.Generic;
using System.Linq;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Represents a single piece in a torrent.
/// A piece is divided into blocks (typically 16KB each).
/// </summary>
public class Piece
{
    public int Index { get; }
    public long Length { get; }
    public byte[] Hash { get; }
    public List<Block> Blocks { get; }
    public bool IsComplete => Blocks.All(b => b.IsReceived);
    public bool IsVerified { get; private set; }
    
    private readonly object _lock = new object();

    public Piece(int index, long length, byte[] hash, int blockSize = 16384)
    {
        Index = index;
        Length = length;
        Hash = hash;
        Blocks = new List<Block>();
        
        // Divide piece into blocks
        long offset = 0;
        while (offset < length)
        {
            int currentBlockSize = (int)Math.Min(blockSize, length - offset);
            Blocks.Add(new Block(offset, currentBlockSize));
            offset += currentBlockSize;
        }
    }

    /// <summary>
    /// Gets the next block that needs to be requested.
    /// </summary>
    public Block? GetNextBlockToRequest()
    {
        lock (_lock)
        {
            return Blocks.FirstOrDefault(b => !b.IsReceived && !b.IsRequested);
        }
    }

    /// <summary>
    /// Marks a block as requested.
    /// </summary>
    public void MarkBlockRequested(int blockOffset)
    {
        lock (_lock)
        {
            var block = Blocks.FirstOrDefault(b => b.Offset == blockOffset);
            if (block != null)
            {
                block.IsRequested = true;
                block.RequestedAt = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Stores received block data.
    /// </summary>
    public bool ReceiveBlock(int blockOffset, byte[] data)
    {
        lock (_lock)
        {
            var block = Blocks.FirstOrDefault(b => b.Offset == blockOffset);
            if (block == null || block.Length != data.Length)
                return false;

            block.Data = data;
            block.IsReceived = true;
            block.ReceivedAt = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Gets all piece data by concatenating all blocks.
    /// </summary>
    public byte[] GetPieceData()
    {
        lock (_lock)
        {
            if (!IsComplete)
                throw new InvalidOperationException("Piece is not complete");

            byte[] pieceData = new byte[Length];
            foreach (var block in Blocks)
            {
                Array.Copy(block.Data!, 0, pieceData, block.Offset, block.Length);
            }
            return pieceData;
        }
    }

    /// <summary>
    /// Verifies the piece hash using SHA-1.
    /// </summary>
    public bool Verify()
    {
        lock (_lock)
        {
            if (!IsComplete)
                return false;

            byte[] pieceData = GetPieceData();
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            byte[] computedHash = sha1.ComputeHash(pieceData);

            IsVerified = computedHash.SequenceEqual(Hash);
            return IsVerified;
        }
    }

    /// <summary>
    /// Resets all blocks to unreceived state (for retry).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var block in Blocks)
            {
                block.Reset();
            }
            IsVerified = false;
        }
    }

    public int GetReceivedBlockCount()
    {
        lock (_lock)
        {
            return Blocks.Count(b => b.IsReceived);
        }
    }

    public double GetProgress()
    {
        lock (_lock)
        {
            if (Blocks.Count == 0) return 0;
            return (double)GetReceivedBlockCount() / Blocks.Count * 100.0;
        }
    }
}

/// <summary>
/// Represents a single block within a piece.
/// Standard block size is 16KB (16384 bytes).
/// </summary>
public class Block
{
    public long Offset { get; }
    public int Length { get; }
    public byte[]? Data { get; set; }
    public bool IsReceived { get; set; }
    public bool IsRequested { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public Block(long offset, int length)
    {
        Offset = offset;
        Length = length;
    }

    public void Reset()
    {
        Data = null;
        IsReceived = false;
        IsRequested = false;
        RequestedAt = null;
        ReceivedAt = null;
    }

    public bool IsTimedOut(TimeSpan timeout)
    {
        return IsRequested && !IsReceived && 
               RequestedAt.HasValue && 
               (DateTime.UtcNow - RequestedAt.Value) > timeout;
    }
}
