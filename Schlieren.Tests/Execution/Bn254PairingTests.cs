using System.Numerics;
using Schlieren.Core.Execution;

namespace Schlieren.Tests.Execution;

/// <summary>
/// EIP-197 / EELS require a G2 r-order subgroup check. geth historically
/// only checks the twist equation; Schlieren follows EELS.
/// </summary>
public sealed class Bn254PairingTests
{
    [Fact]
    public void EmptyInput_ReturnsOne()
    {
        var output = Bn254Pairing.Run([]);
        Assert.NotNull(output);
        Assert.Equal(32, output!.Length);
        Assert.Equal(1, output[31]);
    }

    [Fact]
    public void InfinityPair_ReturnsOne()
    {
        var output = Bn254Pairing.Run(new byte[192]);
        Assert.NotNull(output);
        Assert.Equal(1, output![31]);
    }

    [Fact]
    public void GeneratorPair_DoesNotRevert()
    {
        var input = Concat(EncodeFp(1), EncodeFp(2), EncodeG2Generator());
        var output = Bn254Pairing.Run(input);
        Assert.NotNull(output);
        Assert.Equal(32, output!.Length);
    }

    [Fact]
    public void OnCurveOffSubgroupG2_Reverts()
    {
        // Twist point x=1, y found by solving y² = x³+b2. Almost surely not r-torsion.
        // EIP-197 / EELS: must revert rather than return a 0/1 pairing bit.
        var x0 = BigInteger.One;
        var x1 = BigInteger.Zero;
        var y0 = BigInteger.Parse("18278151005453108793778860132295291098363647455926340152056652516292830556603");
        var y1 = BigInteger.Parse("5912654199736721486680175016176231956195085055698687135131307249486702594212");

        var input = Concat(
            EncodeFp(1), EncodeFp(2),
            EncodeFp(x1), EncodeFp(x0), EncodeFp(y1), EncodeFp(y0));

        Assert.Null(Bn254Pairing.Run(input));
    }

    [Fact]
    public void G2NotOnCurve_Reverts()
    {
        var badG2 = Concat(EncodeFp(1), EncodeFp(1), EncodeFp(1), EncodeFp(1));
        var input = Concat(EncodeFp(1), EncodeFp(2), badG2);
        Assert.Null(Bn254Pairing.Run(input));
    }

    private static byte[] EncodeG2Generator()
    {
        // EIP-197 P2
        var xc1 = BigInteger.Parse("11559732032986387107991004021392285783925812861821192530917403151452391805634");
        var xc0 = BigInteger.Parse("10857046999023057135944570762232829481370756359578518086990519993285655852781");
        var yc1 = BigInteger.Parse("4082367875863433681332203403145435568316851327593401208105741076214120093531");
        var yc0 = BigInteger.Parse("8495653923123431417604973247489272438418190587263600148770280649306958101930");
        return Concat(EncodeFp(xc1), EncodeFp(xc0), EncodeFp(yc1), EncodeFp(yc0));
    }

    private static byte[] EncodeFp(BigInteger v)
    {
        var raw = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == 32) return raw;
        var buf = new byte[32];
        Array.Copy(raw, 0, buf, 32 - raw.Length, raw.Length);
        return buf;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var n = parts.Sum(p => p.Length);
        var buf = new byte[n];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, buf, o, p.Length);
            o += p.Length;
        }
        return buf;
    }
}
