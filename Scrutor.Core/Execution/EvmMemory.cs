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
        var additionalWords = newWords - currentWords;
        
        // Gas cost: 3 per word + quadratic memory expansion
        return 3 * additionalWords + (newWords * newWords) / 512;
    }

    private void EnsureCapacity(int requiredSize)
    {
        if (_data.Length >= requiredSize) return;
        
        var newSize = ((requiredSize + 31) / 32) * 32; // Round up to 32-byte boundary
        Array.Resize(ref _data, newSize);
    }
}
