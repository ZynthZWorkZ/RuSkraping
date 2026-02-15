using System;
using System.Collections.Generic;
using System.Text;

namespace RuSkraping.RUSKTorrent;

/// <summary>
/// Parser for Bencode format used in .torrent files and tracker responses
/// Bencode supports: integers, strings, lists, and dictionaries
/// </summary>
public static class BencodeParser
{
    public static object? Parse(byte[] data)
    {
        int position = 0;
        return ParseValue(data, ref position);
    }

    public static object? Parse(string data)
    {
        return Parse(Encoding.UTF8.GetBytes(data));
    }

    private static object? ParseValue(byte[] data, ref int position)
    {
        if (position >= data.Length)
            throw new FormatException("Unexpected end of data");

        byte current = data[position];

        // Integer: i<number>e
        if (current == 'i')
            return ParseInteger(data, ref position);

        // List: l<values>e
        if (current == 'l')
            return ParseList(data, ref position);

        // Dictionary: d<key-value pairs>e
        if (current == 'd')
            return ParseDictionary(data, ref position);

        // String: <length>:<string>
        if (current >= '0' && current <= '9')
            return ParseString(data, ref position);

        throw new FormatException($"Invalid bencode at position {position}");
    }

    private static long ParseInteger(byte[] data, ref int position)
    {
        position++; // Skip 'i'

        int start = position;
        while (position < data.Length && data[position] != 'e')
            position++;

        if (position >= data.Length)
            throw new FormatException("Unterminated integer");

        string numberStr = Encoding.UTF8.GetString(data, start, position - start);
        position++; // Skip 'e'

        return long.Parse(numberStr);
    }

    private static byte[] ParseString(byte[] data, ref int position)
    {
        int start = position;
        while (position < data.Length && data[position] != ':')
            position++;

        if (position >= data.Length)
            throw new FormatException("Invalid string length");

        string lengthStr = Encoding.UTF8.GetString(data, start, position - start);
        int length = int.Parse(lengthStr);

        position++; // Skip ':'

        if (position + length > data.Length)
            throw new FormatException("String length exceeds data");

        byte[] result = new byte[length];
        Array.Copy(data, position, result, 0, length);
        position += length;

        return result;
    }

    private static List<object?> ParseList(byte[] data, ref int position)
    {
        position++; // Skip 'l'

        var list = new List<object?>();

        while (position < data.Length && data[position] != 'e')
        {
            list.Add(ParseValue(data, ref position));
        }

        if (position >= data.Length)
            throw new FormatException("Unterminated list");

        position++; // Skip 'e'
        return list;
    }

    private static Dictionary<string, object?> ParseDictionary(byte[] data, ref int position)
    {
        position++; // Skip 'd'

        var dict = new Dictionary<string, object?>();

        while (position < data.Length && data[position] != 'e')
        {
            // Keys must be strings
            byte[] keyBytes = ParseString(data, ref position);
            string key = Encoding.UTF8.GetString(keyBytes);

            object? value = ParseValue(data, ref position);
            dict[key] = value;
        }

        if (position >= data.Length)
            throw new FormatException("Unterminated dictionary");

        position++; // Skip 'e'
        return dict;
    }

    /// <summary>
    /// Advances past one complete bencode value starting at the given position.
    /// Returns the position immediately after the value.
    /// Unlike Parse(), this does NOT interpret the data — it just finds the exact byte boundaries.
    /// Essential for extracting raw bencode bytes (e.g., info dictionary for SHA-1 hashing).
    /// </summary>
    public static int SkipValue(byte[] data, int position)
    {
        if (position >= data.Length)
            throw new FormatException("Unexpected end of data at position " + position);

        byte current = data[position];

        // Integer: i<number>e
        if (current == 'i')
        {
            position++; // Skip 'i'
            while (position < data.Length && data[position] != 'e')
                position++;
            if (position >= data.Length)
                throw new FormatException("Unterminated integer");
            position++; // Skip 'e'
            return position;
        }

        // List: l<values>e
        if (current == 'l')
        {
            position++; // Skip 'l'
            while (position < data.Length && data[position] != 'e')
                position = SkipValue(data, position);
            if (position >= data.Length)
                throw new FormatException("Unterminated list");
            position++; // Skip 'e'
            return position;
        }

        // Dictionary: d<key-value pairs>e
        if (current == 'd')
        {
            position++; // Skip 'd'
            while (position < data.Length && data[position] != 'e')
            {
                position = SkipValue(data, position); // Skip key (always a string)
                position = SkipValue(data, position); // Skip value
            }
            if (position >= data.Length)
                throw new FormatException("Unterminated dictionary");
            position++; // Skip 'e'
            return position;
        }

        // String: <length>:<data>
        if (current >= '0' && current <= '9')
        {
            int start = position;
            while (position < data.Length && data[position] != ':')
                position++;
            if (position >= data.Length)
                throw new FormatException("Invalid string length prefix");
            string lengthStr = Encoding.UTF8.GetString(data, start, position - start);
            int length = int.Parse(lengthStr);
            position++; // Skip ':'
            position += length; // Skip the string data (correctly handles binary data!)
            return position;
        }

        throw new FormatException($"Invalid bencode at position {position} (byte: 0x{current:X2})");
    }

    // Encoding methods for creating bencode data
    public static byte[] Encode(object? obj)
    {
        var result = new List<byte>();
        EncodeValue(obj, result);
        return result.ToArray();
    }

    private static void EncodeValue(object? obj, List<byte> result)
    {
        switch (obj)
        {
            case long l:
            case int i:
                result.Add((byte)'i');
                result.AddRange(Encoding.UTF8.GetBytes(obj.ToString()!));
                result.Add((byte)'e');
                break;

            case string str:
                byte[] strBytes = Encoding.UTF8.GetBytes(str);
                result.AddRange(Encoding.UTF8.GetBytes(strBytes.Length.ToString()));
                result.Add((byte)':');
                result.AddRange(strBytes);
                break;

            case byte[] bytes:
                result.AddRange(Encoding.UTF8.GetBytes(bytes.Length.ToString()));
                result.Add((byte)':');
                result.AddRange(bytes);
                break;

            case List<object?> list:
                result.Add((byte)'l');
                foreach (var item in list)
                    EncodeValue(item, result);
                result.Add((byte)'e');
                break;

            case Dictionary<string, object?> dict:
                result.Add((byte)'d');
                foreach (var kvp in dict)
                {
                    // Encode key as string
                    byte[] keyBytes = Encoding.UTF8.GetBytes(kvp.Key);
                    result.AddRange(Encoding.UTF8.GetBytes(keyBytes.Length.ToString()));
                    result.Add((byte)':');
                    result.AddRange(keyBytes);

                    // Encode value
                    EncodeValue(kvp.Value, result);
                }
                result.Add((byte)'e');
                break;

            default:
                throw new ArgumentException($"Cannot bencode type {obj?.GetType().Name ?? "null"}");
        }
    }
}
