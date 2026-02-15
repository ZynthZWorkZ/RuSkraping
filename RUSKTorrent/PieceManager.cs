using System;
using System.Collections.Generic;
using System.Linq;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Manages all pieces for a torrent download.
/// Tracks progress, verifies hashes, and coordinates piece requests.
/// </summary>
public class PieceManager
{
    public List<Piece> Pieces { get; }
    public long TotalSize { get; }
    public int PieceLength { get; }
    public bool IsComplete => Pieces.All(p => p.IsVerified);
    
    private readonly object _lock = new object();
    private readonly BitField _havePieces;

    public PieceManager(TorrentMetadata metadata)
    {
        PieceLength = metadata.PieceLength;
        TotalSize = metadata.TotalSize;
        Pieces = new List<Piece>();
        var pieceHashes = metadata.GetPieceHashes();
        _havePieces = new BitField(pieceHashes.Count);

        // Create piece objects
        for (int i = 0; i < pieceHashes.Count; i++)
        {
            long pieceLength = CalculatePieceLength(i, metadata, pieceHashes.Count);
            Pieces.Add(new Piece(i, pieceLength, pieceHashes[i]));
        }
    }

    private long CalculatePieceLength(int pieceIndex, TorrentMetadata metadata, int totalPieces)
    {
        // Last piece might be smaller
        if (pieceIndex == totalPieces - 1)
        {
            long remainder = metadata.TotalSize % metadata.PieceLength;
            return remainder == 0 ? metadata.PieceLength : remainder;
        }
        return metadata.PieceLength;
    }

    /// <summary>
    /// Gets the next piece to download from available pieces.
    /// </summary>
    public Piece? GetNextPieceToDownload(BitField peerBitfield)
    {
        lock (_lock)
        {
            // Find pieces the peer has that we don't have
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (peerBitfield.HasPiece(i) && !_havePieces.HasPiece(i) && !Pieces[i].IsComplete)
                {
                    return Pieces[i];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Gets a specific piece by index.
    /// </summary>
    public Piece? GetPiece(int index)
    {
        lock (_lock)
        {
            return index >= 0 && index < Pieces.Count ? Pieces[index] : null;
        }
    }

    /// <summary>
    /// Marks a piece as verified and complete.
    /// </summary>
    public void MarkPieceComplete(int pieceIndex)
    {
        lock (_lock)
        {
            if (pieceIndex >= 0 && pieceIndex < Pieces.Count)
            {
                _havePieces.SetPiece(pieceIndex);
            }
        }
    }

    /// <summary>
    /// Gets the overall download progress percentage.
    /// </summary>
    public double GetProgress()
    {
        lock (_lock)
        {
            if (Pieces.Count == 0) return 0;
            int completePieces = Pieces.Count(p => p.IsVerified);
            return (double)completePieces / Pieces.Count * 100.0;
        }
    }

    /// <summary>
    /// Gets the total number of bytes downloaded.
    /// </summary>
    public long GetDownloadedBytes()
    {
        lock (_lock)
        {
            return Pieces.Where(p => p.IsVerified).Sum(p => p.Length);
        }
    }

    /// <summary>
    /// Gets our bitfield representing pieces we have.
    /// </summary>
    public BitField GetBitField()
    {
        lock (_lock)
        {
            return _havePieces.Clone();
        }
    }
}

/// <summary>
/// Represents a bitfield indicating which pieces are available.
/// Used in the BitTorrent protocol to communicate piece availability.
/// </summary>
public class BitField
{
    private readonly byte[] _data;
    public int Length { get; }

    public BitField(int pieceCount)
    {
        Length = pieceCount;
        _data = new byte[(pieceCount + 7) / 8]; // Round up to nearest byte
    }

    public BitField(byte[] data, int pieceCount)
    {
        _data = (byte[])data.Clone();
        Length = pieceCount;
    }

    public bool HasPiece(int index)
    {
        if (index < 0 || index >= Length)
            return false;

        int byteIndex = index / 8;
        int bitIndex = 7 - (index % 8); // MSB first
        return (_data[byteIndex] & (1 << bitIndex)) != 0;
    }

    public void SetPiece(int index)
    {
        if (index < 0 || index >= Length)
            return;

        int byteIndex = index / 8;
        int bitIndex = 7 - (index % 8);
        _data[byteIndex] |= (byte)(1 << bitIndex);
    }

    public void ClearPiece(int index)
    {
        if (index < 0 || index >= Length)
            return;

        int byteIndex = index / 8;
        int bitIndex = 7 - (index % 8);
        _data[byteIndex] &= (byte)~(1 << bitIndex);
    }

    public byte[] ToBytes()
    {
        return (byte[])_data.Clone();
    }

    public BitField Clone()
    {
        return new BitField(_data, Length);
    }

    public int CountSetBits()
    {
        int count = 0;
        for (int i = 0; i < Length; i++)
        {
            if (HasPiece(i))
                count++;
        }
        return count;
    }
}
