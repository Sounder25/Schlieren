using System;

namespace Scrutor.Core.Primitives;

/// <summary>
/// Ethereum address (20 bytes)
/// </summary>
public readonly record struct Address : IEquatable<Address>
{
    private readonly byte[] _bytes;

    public Address(byte[] bytes)
    {
        if (bytes.Length != 20)
            throw new ArgumentException("Address must be 20 bytes", nameof(bytes));
        _bytes = bytes;
    }

    public static Address Zero => new(new byte[20]);
    public byte[] Bytes => _bytes ?? new byte[20];
    
    public override string ToString() => "0x" + Convert.ToHexString(Bytes).ToLowerInvariant();
    
    public static Address FromHex(string hex)
    {
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return new Address(Convert.FromHexString(clean.PadLeft(40, '0')));
    }

    public bool Equals(Address other)
    {
        // Use ReadOnlySpan for efficient comparison without allocation
        return Bytes.AsSpan().SequenceEqual(other.Bytes.AsSpan());
    }

    public override int GetHashCode()
    {
        // Stable hash from bytes
        var hc = new HashCode();
        foreach (var b in Bytes) hc.Add(b);
        return hc.ToHashCode();
    }
}
