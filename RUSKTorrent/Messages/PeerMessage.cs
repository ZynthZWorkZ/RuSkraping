using System;
using System.Text;

namespace RuSkraping.RUSKTorrent.Messages;

/// <summary>
/// Base class for all peer wire protocol messages.
/// </summary>
public abstract class PeerMessage
{
    public abstract MessageType Type { get; }
    public abstract byte[] Encode();
    
    public static PeerMessage? Decode(byte[] data)
    {
        if (data.Length == 0)
            return new KeepAliveMessage();

        var messageType = (MessageType)data[0];

        return messageType switch
        {
            MessageType.Choke => new ChokeMessage(),
            MessageType.Unchoke => new UnchokeMessage(),
            MessageType.Interested => new InterestedMessage(),
            MessageType.NotInterested => new NotInterestedMessage(),
            MessageType.Have => HaveMessage.Decode(data),
            MessageType.Bitfield => BitfieldMessage.Decode(data),
            MessageType.Request => RequestMessage.Decode(data),
            MessageType.Piece => PieceMessage.Decode(data),
            MessageType.Cancel => CancelMessage.Decode(data),
            _ => null
        };
    }
}

public enum MessageType : byte
{
    Choke = 0,
    Unchoke = 1,
    Interested = 2,
    NotInterested = 3,
    Have = 4,
    Bitfield = 5,
    Request = 6,
    Piece = 7,
    Cancel = 8
}

/// <summary>
/// Handshake message (special format, not a regular message).
/// </summary>
public class HandshakeMessage
{
    public byte[] InfoHash { get; set; } = Array.Empty<byte>();
    public string PeerId { get; set; } = string.Empty;

    public byte[] Encode()
    {
        byte[] message = new byte[68];
        message[0] = 19; // Protocol name length
        
        byte[] protocolName = Encoding.ASCII.GetBytes("BitTorrent protocol");
        Array.Copy(protocolName, 0, message, 1, 19);
        
        // 8 reserved bytes (index 20-27) are left as zeros
        
        // Info hash (20 bytes)
        Array.Copy(InfoHash, 0, message, 28, 20);
        
        // Peer ID (20 bytes)
        byte[] peerIdBytes = Encoding.ASCII.GetBytes(PeerId);
        Array.Copy(peerIdBytes, 0, message, 48, Math.Min(20, peerIdBytes.Length));
        
        return message;
    }

    public static HandshakeMessage? Decode(byte[] data)
    {
        if (data.Length < 68)
            return null;

        if (data[0] != 19)
            return null;

        byte[] protocolName = new byte[19];
        Array.Copy(data, 1, protocolName, 0, 19);
        if (Encoding.ASCII.GetString(protocolName) != "BitTorrent protocol")
            return null;

        var handshake = new HandshakeMessage();
        
        // Extract info hash
        handshake.InfoHash = new byte[20];
        Array.Copy(data, 28, handshake.InfoHash, 0, 20);
        
        // Extract peer ID
        byte[] peerIdBytes = new byte[20];
        Array.Copy(data, 48, peerIdBytes, 0, 20);
        handshake.PeerId = Encoding.ASCII.GetString(peerIdBytes);
        
        return handshake;
    }
}

/// <summary>
/// Keep-alive message (length = 0, no payload).
/// </summary>
public class KeepAliveMessage : PeerMessage
{
    public override MessageType Type => throw new InvalidOperationException("Keep-alive has no type");
    
    public override byte[] Encode()
    {
        return Array.Empty<byte>();
    }
}

public class ChokeMessage : PeerMessage
{
    public override MessageType Type => MessageType.Choke;
    public override byte[] Encode() => new byte[] { (byte)Type };
}

public class UnchokeMessage : PeerMessage
{
    public override MessageType Type => MessageType.Unchoke;
    public override byte[] Encode() => new byte[] { (byte)Type };
}

public class InterestedMessage : PeerMessage
{
    public override MessageType Type => MessageType.Interested;
    public override byte[] Encode() => new byte[] { (byte)Type };
}

public class NotInterestedMessage : PeerMessage
{
    public override MessageType Type => MessageType.NotInterested;
    public override byte[] Encode() => new byte[] { (byte)Type };
}

public class HaveMessage : PeerMessage
{
    public override MessageType Type => MessageType.Have;
    public int PieceIndex { get; set; }

    public override byte[] Encode()
    {
        byte[] message = new byte[5];
        message[0] = (byte)Type;
        message[1] = (byte)(PieceIndex >> 24);
        message[2] = (byte)(PieceIndex >> 16);
        message[3] = (byte)(PieceIndex >> 8);
        message[4] = (byte)PieceIndex;
        return message;
    }

    public static HaveMessage Decode(byte[] data)
    {
        if (data.Length < 5)
            throw new ArgumentException("Invalid Have message");

        return new HaveMessage
        {
            PieceIndex = (data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4]
        };
    }
}

public class BitfieldMessage : PeerMessage
{
    public override MessageType Type => MessageType.Bitfield;
    public byte[] Bitfield { get; set; } = Array.Empty<byte>();

    public override byte[] Encode()
    {
        byte[] message = new byte[1 + Bitfield.Length];
        message[0] = (byte)Type;
        Array.Copy(Bitfield, 0, message, 1, Bitfield.Length);
        return message;
    }

    public static BitfieldMessage Decode(byte[] data)
    {
        byte[] bitfield = new byte[data.Length - 1];
        Array.Copy(data, 1, bitfield, 0, bitfield.Length);
        
        return new BitfieldMessage { Bitfield = bitfield };
    }
}

public class RequestMessage : PeerMessage
{
    public override MessageType Type => MessageType.Request;
    public int Index { get; set; }
    public int Begin { get; set; }
    public int Length { get; set; }

    public override byte[] Encode()
    {
        byte[] message = new byte[13];
        message[0] = (byte)Type;
        
        // Index (4 bytes)
        message[1] = (byte)(Index >> 24);
        message[2] = (byte)(Index >> 16);
        message[3] = (byte)(Index >> 8);
        message[4] = (byte)Index;
        
        // Begin (4 bytes)
        message[5] = (byte)(Begin >> 24);
        message[6] = (byte)(Begin >> 16);
        message[7] = (byte)(Begin >> 8);
        message[8] = (byte)Begin;
        
        // Length (4 bytes)
        message[9] = (byte)(Length >> 24);
        message[10] = (byte)(Length >> 16);
        message[11] = (byte)(Length >> 8);
        message[12] = (byte)Length;
        
        return message;
    }

    public static RequestMessage Decode(byte[] data)
    {
        if (data.Length < 13)
            throw new ArgumentException("Invalid Request message");

        return new RequestMessage
        {
            Index = (data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4],
            Begin = (data[5] << 24) | (data[6] << 16) | (data[7] << 8) | data[8],
            Length = (data[9] << 24) | (data[10] << 16) | (data[11] << 8) | data[12]
        };
    }
}

public class PieceMessage : PeerMessage
{
    public override MessageType Type => MessageType.Piece;
    public int Index { get; set; }
    public int Begin { get; set; }
    public byte[] Block { get; set; } = Array.Empty<byte>();

    public override byte[] Encode()
    {
        byte[] message = new byte[9 + Block.Length];
        message[0] = (byte)Type;
        
        // Index (4 bytes)
        message[1] = (byte)(Index >> 24);
        message[2] = (byte)(Index >> 16);
        message[3] = (byte)(Index >> 8);
        message[4] = (byte)Index;
        
        // Begin (4 bytes)
        message[5] = (byte)(Begin >> 24);
        message[6] = (byte)(Begin >> 16);
        message[7] = (byte)(Begin >> 8);
        message[8] = (byte)Begin;
        
        // Block data
        Array.Copy(Block, 0, message, 9, Block.Length);
        
        return message;
    }

    public static PieceMessage Decode(byte[] data)
    {
        if (data.Length < 9)
            throw new ArgumentException("Invalid Piece message");

        byte[] block = new byte[data.Length - 9];
        Array.Copy(data, 9, block, 0, block.Length);

        return new PieceMessage
        {
            Index = (data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4],
            Begin = (data[5] << 24) | (data[6] << 16) | (data[7] << 8) | data[8],
            Block = block
        };
    }
}

public class CancelMessage : PeerMessage
{
    public override MessageType Type => MessageType.Cancel;
    public int Index { get; set; }
    public int Begin { get; set; }
    public int Length { get; set; }

    public override byte[] Encode()
    {
        byte[] message = new byte[13];
        message[0] = (byte)Type;
        
        message[1] = (byte)(Index >> 24);
        message[2] = (byte)(Index >> 16);
        message[3] = (byte)(Index >> 8);
        message[4] = (byte)Index;
        
        message[5] = (byte)(Begin >> 24);
        message[6] = (byte)(Begin >> 16);
        message[7] = (byte)(Begin >> 8);
        message[8] = (byte)Begin;
        
        message[9] = (byte)(Length >> 24);
        message[10] = (byte)(Length >> 16);
        message[11] = (byte)(Length >> 8);
        message[12] = (byte)Length;
        
        return message;
    }

    public static CancelMessage Decode(byte[] data)
    {
        if (data.Length < 13)
            throw new ArgumentException("Invalid Cancel message");

        return new CancelMessage
        {
            Index = (data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4],
            Begin = (data[5] << 24) | (data[6] << 16) | (data[7] << 8) | data[8],
            Length = (data[9] << 24) | (data[10] << 16) | (data[11] << 8) | data[12]
        };
    }
}
