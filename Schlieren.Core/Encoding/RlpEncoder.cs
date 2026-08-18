namespace Schlieren.Core.Encoding;

/// <summary>
/// Minimal RLP encoder used to rebuild unsigned typed-tx payloads for ECDSA recovery.
/// </summary>
public static class RlpEncoder
{
    public static byte[] Encode(RlpItem item)
    {
        if (item.IsList)
            return EncodeList(item.Items);

        return EncodeBytes(item.Data.Span);
    }

    public static byte[] EncodeList(IReadOnlyList<RlpItem> items)
    {
        var parts = new List<byte[]>(items.Count);
        var total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var encoded = Encode(items[i]);
            parts.Add(encoded);
            total += encoded.Length;
        }

        var content = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, content, offset, part.Length);
            offset += part.Length;
        }

        return EncodeLength(content, offset: 0xc0);
    }

    public static byte[] EncodeBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length == 1 && data[0] < 0x80)
            return new[] { data[0] };

        return EncodeLength(data.ToArray(), offset: 0x80);
    }

    private static byte[] EncodeLength(byte[] content, int offset)
    {
        if (content.Length <= 55)
        {
            var result = new byte[1 + content.Length];
            result[0] = (byte)(offset + content.Length);
            Buffer.BlockCopy(content, 0, result, 1, content.Length);
            return result;
        }

        var lenBytes = EncodeInt(content.Length);
        var output = new byte[1 + lenBytes.Length + content.Length];
        output[0] = (byte)(offset + 55 + lenBytes.Length);
        Buffer.BlockCopy(lenBytes, 0, output, 1, lenBytes.Length);
        Buffer.BlockCopy(content, 0, output, 1 + lenBytes.Length, content.Length);
        return output;
    }

    private static byte[] EncodeInt(int value)
    {
        if (value == 0) return Array.Empty<byte>();
        // big-endian without leading zeros
        Span<byte> tmp = stackalloc byte[4];
        tmp[0] = (byte)(value >> 24);
        tmp[1] = (byte)(value >> 16);
        tmp[2] = (byte)(value >> 8);
        tmp[3] = (byte)value;
        int start = 0;
        while (start < 3 && tmp[start] == 0) start++;
        return tmp.Slice(start).ToArray();
    }
}
