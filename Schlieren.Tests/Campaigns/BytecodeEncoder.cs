using System;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// EVM bytecode encoding utilities.
/// </summary>
public static class BytecodeEncoder
{
    /// <summary>
    /// Encode a value as PUSH1..PUSH32 based on actual byte width needed.
    /// </summary>
    public static byte[] EncodePush(ulong value)
    {
        if (value == 0)
            return new byte[] { 0x60, 0x00 };  // PUSH1 0

        var hex = value.ToString("x");

        // Ensure even length for FromHexString
        if ((hex.Length & 1) != 0)
            hex = "0" + hex;

        var data = Convert.FromHexString(hex);

        if (data.Length is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(value), 
                "Value requires more than 32 bytes");

        var result = new byte[data.Length + 1];
        result[0] = (byte)(0x5f + data.Length);  // PUSH1=0x60, PUSH2=0x61, ..., PUSH32=0x7f
        data.CopyTo(result, 1);

        return result;
    }

    /// <summary>
    /// Encode PUSH as hex string (for string-based bytecode builders).
    /// </summary>
    public static string EncodePushHex(ulong value)
    {
        var bytes = EncodePush(value);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
