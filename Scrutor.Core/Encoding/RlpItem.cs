namespace Scrutor.Core.Encoding;

public sealed class RlpItem
{
    public bool IsList { get; init; }
    public ReadOnlyMemory<byte> Data { get; init; }
    public List<RlpItem> Items { get; init; } = new();

    public byte[] ToBytes() => Data.ToArray();
    
    public System.Numerics.BigInteger ToBigInteger() 
    {
        if (Data.Length == 0) return System.Numerics.BigInteger.Zero;
        // BigInteger expects little-endian, RLP is big-endian
        var bytes = Data.ToArray();
        Array.Reverse(bytes);
        return new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }
}
