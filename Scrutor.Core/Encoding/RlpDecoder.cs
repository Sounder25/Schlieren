namespace Scrutor.Core.Encoding;

public static class RlpDecoder
{
    private const int MAX_DEPTH = 1024;

    public static RlpItem Decode(ReadOnlyMemory<byte> input)
    {
        var (item, _) = DecodeItem(input, 0);
        return item;
    }

    private static (RlpItem, int) DecodeItem(ReadOnlyMemory<byte> data, int depth)
    {
        if (depth > MAX_DEPTH) throw new FormatException($"RLP depth exceeds maximum allowed limit of {MAX_DEPTH}");
        if (data.Length == 0) throw new FormatException("RLP data cannot be empty");

        var prefix = data.Span[0];

        if (prefix < 0x80) // Single byte
        {
            return (new RlpItem { Data = data.Slice(0, 1) }, 1);
        }

        if (prefix <= 0xb7) // Short string
        {
            var len = prefix - 0x80;
            // Ensure there's enough data for the string content
            if (data.Length < 1 + len) throw new FormatException("Invalid RLP: insufficient data for short string content");
            return (new RlpItem { Data = data.Slice(1, len) }, 1 + len);
        }

        if (prefix <= 0xbf) // Long string
        {
            var lenOfLen = prefix - 0xb7;
            // Ensure there's enough data to read the length of the length
            if (data.Length < 1 + lenOfLen) throw new FormatException("Invalid RLP: insufficient data for long string length prefix");
            var len = ToInt(data.Slice(1, lenOfLen));
            // Ensure there's enough data for the string content
            if (data.Length < 1 + lenOfLen + len) throw new FormatException("Invalid RLP: insufficient data for long string content");
            return (new RlpItem { Data = data.Slice(1 + lenOfLen, len) }, 1 + lenOfLen + len);
        }

        if (prefix <= 0xf7) // Short list
        {
            var len = prefix - 0xc0;
            // Ensure there's enough data for the list content
            if (data.Length < 1 + len) throw new FormatException("Invalid RLP: insufficient data for short list content");
            return DecodeList(data.Slice(1, len), 1 + len, depth + 1);
        }

        // Long list
        var listLenOfLen = prefix - 0xf7;
        // Ensure there's enough data to read the length of the list's length
        if (data.Length < 1 + listLenOfLen) throw new FormatException("Invalid RLP: insufficient data for long list length prefix");
        var listLen = ToInt(data.Slice(1, listLenOfLen));
        // Ensure there's enough data for the list content
        if (data.Length < 1 + listLenOfLen + listLen) throw new FormatException("Invalid RLP: insufficient data for long list content");
        return DecodeList(data.Slice(1 + listLenOfLen, listLen), 1 + listLenOfLen + listLen, depth + 1);
    }

    private static (RlpItem, int) DecodeList(ReadOnlyMemory<byte> content, int totalConsumed, int depth)
    {
        if (depth > MAX_DEPTH) throw new FormatException($"RLP depth exceeds maximum allowed limit of {MAX_DEPTH}");
        var items = new List<RlpItem>();
        var offset = 0;
        
        while (offset < content.Length)
        {
            var (item, consumed) = DecodeItem(content.Slice(offset), depth);
            items.Add(item);
            offset += consumed;
        }

        return (new RlpItem { IsList = true, Items = items }, totalConsumed);
    }

    private static int ToInt(ReadOnlyMemory<byte> data)
    {
        if (data.Length > 4) throw new OverflowException("RLP length too large");
        int result = 0;
        foreach (var b in data.Span) result = (result << 8) | b;
        return result;
    }
}
