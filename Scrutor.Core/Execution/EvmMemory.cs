namespace Scrutor.Core.Execution;

/// <summary>
/// EVM memory (expandable byte array, grows in 32-byte chunks)
/// </summary>
public sealed class EvmMemory
{
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
        if (offset < 0 || length <= 0) return Array.Empty<byte>();
        
        EnsureCapacity(offset + length);
        var result = new byte[length];
        Array.Copy(_data, offset, result, 0, length);
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
        if (requiredSize <= 0 || _data.Length >= requiredSize) return;
        
        // Guard against unreasonable allocation attempts. The EVM gas cost formula
        // makes anything beyond ~1MB astronomically expensive (>30M gas), so if we
        // reach here with a large size, gas accounting will reject it. Cap at 16MB
        // as a safety net against OOM — legitimate EVM execution never reaches this.
        if (requiredSize > 16 * 1024 * 1024)
            throw new EvmOutOfGasException($"Memory expansion too large: {requiredSize} bytes");

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
