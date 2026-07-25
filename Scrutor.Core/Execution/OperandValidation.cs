using System.Numerics;

namespace Scrutor.Core.Execution;

/// <summary>
/// Centralized validation for EVM stack operands that must be converted to host memory indexes.
/// EVM allows 256-bit operands, but the host execution layer uses 32-bit addressing.
///
/// Consensus Rule: An oversized operand (exceeding Int32.MaxValue) on a nonzero-length operation
/// results in OutOfGas, NOT InternalError. This is a fork-defined EVM exceptional halt.
///
/// Zero-length operations are always valid regardless of offset (they don't touch memory).
/// </summary>
internal static class OperandValidation
{
    /// <summary>
    /// Validates and converts a memory range (offset, length) to host-compatible integers.
    /// Returns true if the range is valid or zero-length; false if it would overflow.
    /// </summary>
    /// <param name="offset">The 256-bit memory offset from the EVM stack</param>
    /// <param name="length">The 256-bit length from the EVM stack</param>
    /// <param name="offsetInt">The converted 32-bit offset (valid only if returns true or length is zero)</param>
    /// <param name="lengthInt">The converted 32-bit length (valid only if returns true or length is zero)</param>
    /// <param name="endExclusive">The exclusive end offset for gas calculation</param>
    /// <returns>True if the range is processable; false if it would exceed host limits (yield OutOfGas)</returns>
    internal static bool TryResolveMemoryRange(
        BigInteger offset,
        BigInteger length,
        out int offsetInt,
        out int lengthInt,
        out ulong endExclusive)
    {
        offsetInt = 0;
        lengthInt = 0;
        endExclusive = 0;

        // [EVM CONSENSUS] Zero-length operations are always valid, regardless of offset.
        // "Copying 0 bytes from anywhere to anywhere" is a no-op.
        if (length.IsZero)
            return true;

        // [HOST LIMIT] Int32.MaxValue is the practical limit for .NET array indexes.
        // EVM specification would allow up to 2^64-1, but gas costs make this unreachable.
        // Any offset or length exceeding this threshold must result in OutOfGas.
        if (offset > int.MaxValue || length > int.MaxValue)
            return false;

        offsetInt = (int)offset;
        lengthInt = (int)length;

        // [RANGE OVERFLOW] Even if offset and length are individually valid,
        // their sum might overflow. Check that end fits in addressable range.
        endExclusive = (ulong)offsetInt + (ulong)lengthInt;

        // Final check: end must fit within our addressable range
        return endExclusive <= int.MaxValue;
    }
}
