using System.Globalization;
using System.Numerics;

namespace Schlieren.UI.Services;

/// <summary>Decimal or 0x-hex quantity (EELS fixtures use hex; the workbench also accepts decimal).</summary>
public static class WorkbenchQuantity
{
    public static bool TryBigInteger(string? raw, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        var s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = s[2..];
            if (hex.Length == 0) return true;
            if (hex.Length % 2 == 1) hex = "0" + hex;
            try
            {
                value = new BigInteger(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryUlong(string? raw, out ulong value)
    {
        value = 0;
        if (!TryBigInteger(raw, out var big) || big < 0 || big > ulong.MaxValue)
            return string.IsNullOrWhiteSpace(raw);
        value = (ulong)big;
        return true;
    }

    public static string ToDecimalString(BigInteger value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
