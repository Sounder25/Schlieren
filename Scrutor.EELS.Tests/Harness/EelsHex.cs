using System.Globalization;
using System.Numerics;

namespace Scrutor.EELS.Tests.Harness;

internal static class EelsHex
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0x0";
        }

        var clean = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (clean.Length == 0)
        {
            return "0x0";
        }

        clean = clean.TrimStart('0');
        return clean.Length == 0 ? "0x0" : "0x" + clean.ToLowerInvariant();
    }

    public static BigInteger ParseQuantity(string value)
    {
        var normalized = NormalizeRaw(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return BigInteger.Zero;
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var cleanHex = normalized[2..];
            if (cleanHex.Length == 0)
            {
                return BigInteger.Zero;
            }

            return BigInteger.Parse("0" + cleanHex, NumberStyles.AllowHexSpecifier);
        }

        return BigInteger.Parse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public static ulong ParseUlong(string value)
    {
        var quantity = ParseQuantity(value);
        if (quantity < ulong.MinValue || quantity > ulong.MaxValue)
        {
            throw new InvalidOperationException($"Quantity does not fit into UInt64: {value}");
        }

        return (ulong)quantity;
    }

    public static byte[] ParseBytes(string value)
    {
        var normalized = NormalizeRaw(value);
        var clean = normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? normalized[2..] : normalized;
        if (clean.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (clean.Length % 2 == 1)
        {
            clean = "0" + clean;
        }

        return Convert.FromHexString(clean);
    }

    private static string NormalizeRaw(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        const string rawPrefix = ":raw";
        if (trimmed.StartsWith(rawPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[rawPrefix.Length..].Trim();
        }

        return trimmed;
    }

    public static string ToCanonicalHex(BigInteger value)
    {
        if (value == BigInteger.Zero)
        {
            return "0x0";
        }

        return "0x" + value.ToString("x");
    }
}
