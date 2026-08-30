using System.Globalization;
using System.Numerics;
using System.Text;
using Schlieren.Core.Primitives;

namespace Schlieren.Guard;

public static class Abi
{
    public static byte[] Selector(string signature)
    {
        var hash = CryptoUtils.Keccak256(Encoding.UTF8.GetBytes(signature));
        return hash[..4];
    }

    public static byte[] EncodeCall(string signature, params byte[][] words)
    {
        var selector = Selector(signature);
        var payload = new byte[4 + words.Sum(w => w.Length)];
        selector.CopyTo(payload, 0);
        var offset = 4;
        foreach (var word in words)
        {
            word.CopyTo(payload, offset);
            offset += word.Length;
        }
        return payload;
    }

    public static byte[] Word(BigInteger value)
    {
        var raw = value.Sign < 0
            ? throw new ArgumentOutOfRangeException(nameof(value), "ABI uint256 cannot be negative.")
            : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > 32)
            throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds uint256.");
        var word = new byte[32];
        raw.CopyTo(word, 32 - raw.Length);
        return word;
    }

    public static byte[] Word(Address address)
    {
        var word = new byte[32];
        address.Bytes.CopyTo(word, 12);
        return word;
    }

    public static byte[] AddressArray(IReadOnlyList<Address> addresses)
    {
        // head: offset (32) + length (32) + n * 32
        var body = new byte[32 + 32 + (addresses.Count * 32)];
        Word(32).CopyTo(body, 0);
        Word(addresses.Count).CopyTo(body, 32);
        for (var i = 0; i < addresses.Count; i++)
            Word(addresses[i]).CopyTo(body, 64 + (i * 32));
        return body;
    }

    public static Address DecodeAddress(byte[] data)
    {
        if (data.Length < 32)
            throw new InvalidOperationException("ABI address return is shorter than 32 bytes.");
        return new Address(data[^20..]);
    }

    public static BigInteger DecodeUint256(byte[] data)
    {
        if (data.Length == 0)
            return BigInteger.Zero;
        var slice = data.Length >= 32 ? data[..32] : data;
        return new BigInteger(slice, isUnsigned: true, isBigEndian: true);
    }

    public static string ToHex(byte[] data) =>
        "0x" + Convert.ToHexString(data).ToLowerInvariant();

    public static byte[] FromHex(string hex)
    {
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (clean.Length == 0) return Array.Empty<byte>();
        if (clean.Length % 2 == 1) clean = "0" + clean;
        return Convert.FromHexString(clean);
    }

    public static string Qty(BigInteger value) => "0x" + value.ToString("x", CultureInfo.InvariantCulture);
}
