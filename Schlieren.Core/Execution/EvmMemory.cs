namespace Schlieren.Core.Execution;

/// <summary>
/// EVM memory (expandable byte array, grows in 32-byte chunks)
/// </summary>
public sealed class EvmMemory
{
    // Max memory size: prevents DOS via OOM. 16 MB = ~540M gas,
    // far beyond realistic block gas limits. Adjust if running on constrained hosts.
    private const int MaxMemorySize = 16 * 1024 * 1024;

    private byte[] _data = Array.Empty<byte>();

    public int Size => _data.Length;

    public void Store(int offset, byte[] data)
    {
        if (offset < 0 || data.Length == 0) return;
        
        var requiredSize = offset + data.Length;
        EnsureCapacity(requiredSize);
        Array.Copy(data, 0, _data, offset, data.Length);
    }

    public byte[] Load(int offset, int length)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length == 0)
            return Array.Empty<byte>();

        // EVM operands may produce offset/length combinations whose sum
        // exceeds Int32.MaxValue. Calculate in 64-bit space first.
        var required = (long)offset + length;

        if (required > int.MaxValue)
        {
            throw new EvmOutOfGasException(
                $"Memory expansion too large: {required} bytes");
        }

        EnsureCapacity((int)required);

        var result = new byte[length];
        Buffer.BlockCopy(_data, offset, result, 0, length);
        return result;
    }

    public ulong CalculateGasCost(int newSize)
    {
        if (newSize <= _data.Length) return 0;
        
        var currentWords = (ulong)(_data.Length + 31) / 32;
        var newWords = (ulong)(newSize + 31) / 32;
        
        // EVM memory expansion cost: ΔC = memory_cost(new) - memory_cost(old)
        // where memory_cost(w) = 3w + ⌊w²/512⌋
        var oldCost = 3 * currentWords + (currentWords * currentWords) / 512;
        var newCost = 3 * newWords + (newWords * newWords) / 512;
        
        return newCost - oldCost;
    }

    public void Expand(int newSize)
    {
        if (newSize > 0)
        {
            EnsureCapacity(newSize);
        }
    }

    private void EnsureCapacity(int requiredSize)
    {
        if (requiredSize < 0 || requiredSize > MaxMemorySize)
        {
            throw new EvmOutOfGasException(
                $"Memory expansion exceeds the {MaxMemorySize}-byte limit.");
        }

        if (requiredSize <= _data.Length)
            return;

        var newSize = ((requiredSize + 31) / 32) * 32; // Round up to 32-byte boundary
        Array.Resize(ref _data, newSize);
    }

    public List<string> SnapshotWordsHex(int maxWords = 64)
    {
        if (_data.Length == 0) return new List<string>();

        var words = Math.Min((_data.Length + 31) / 32, maxWords);
        var result = new List<string>(words);
        for (var i = 0; i < words; i++)
        {
            var buf = new byte[32];
            var offset = i * 32;
            var len = Math.Min(32, _data.Length - offset);
            if (len > 0) Array.Copy(_data, offset, buf, 0, len);
            result.Add("0x" + Convert.ToHexString(buf).ToLowerInvariant());
        }
        return result;
    }
}
