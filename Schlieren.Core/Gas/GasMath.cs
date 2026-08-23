using System.Numerics;

namespace Schlieren.Core.Gas;

/// <summary>Checked arithmetic shared by executable gas formulas.</summary>
public static class GasMath
{
    public static ulong AddChecked(ulong left, ulong right) =>
        checked(left + right);

    public static ulong MultiplyChecked(ulong left, ulong right) =>
        checked(left * right);

    public static BigInteger WordCount(BigInteger byteLength)
    {
        if (byteLength < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        return byteLength.IsZero
            ? BigInteger.Zero
            : (byteLength + 31) / 32;
    }

    public static bool TryGetHostMemoryEnd(
        BigInteger offset,
        BigInteger length,
        int hostLimit,
        out int end)
    {
        if (offset < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (hostLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(hostLimit));

        if (length.IsZero)
        {
            end = 0;
            return true;
        }

        var mathematicalEnd = offset + length;
        if (mathematicalEnd > hostLimit || mathematicalEnd > int.MaxValue)
        {
            end = 0;
            return false;
        }

        end = (int)mathematicalEnd;
        return true;
    }
}