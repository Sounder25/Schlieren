using Schlieren.Core.Execution;

namespace Schlieren.Tests.Execution;

/// <summary>
/// Pins EIP-7883 ModExp gas formula against EIP text vectors / EELS Osaka.
/// </summary>
public sealed class Eip7883ModExpGasTests
{
    private static ulong Gas7883(int baseLen, int modLen, int expLen, byte[] expBytes) =>
        Precompiles.ModExpGas(baseLen, expLen, modLen, expBytes, eip2565: true, eip7883: true);

    private static ulong Gas2565(int baseLen, int modLen, int expLen, byte[] expBytes) =>
        Precompiles.ModExpGas(baseLen, expLen, modLen, expBytes, eip2565: true, eip7883: false);

    [Fact]
    public void SmallBaseMod_FloorIs500()
    {
        // maxLen ≤ 32 → complexity 16; exp=0 → iterations 1 → cost max(500, 16) = 500
        Assert.Equal(500UL, Gas7883(1, 1, 0, Array.Empty<byte>()));
        Assert.Equal(500UL, Gas7883(32, 32, 0, Array.Empty<byte>()));
    }

    [Fact]
    public void MaxLenExactly32_UsesComplexity16Not2WordsSq()
    {
        // words = 4; if wrongly 2*16=32 with iter=1 → 32, still floor 500
        // with exp that yields iter > 1: exp = 3 → bit_length-1 = 1
        // cost = 16 * 1 = 16 → floor 500
        Assert.Equal(500UL, Gas7883(32, 32, 1, new byte[] { 0x03 }));
    }

    [Fact]
    public void MaxLenAbove32_UsesDoubleWordsSquared()
    {
        // maxLen=64 → words=8 → complexity = 2*64 = 128
        // exp=0 → iter=1 → cost = max(500, 128) = 500 still
        Assert.Equal(500UL, Gas7883(64, 64, 0, Array.Empty<byte>()));

        // exp with bit_length-1 = 16 (e.g. 2^16 = 0x10000) → need 3-byte exp
        // exp = 0x010000 → bit_length = 17, iter = 16
        // cost = 128 * 16 = 2048
        var exp = new byte[] { 0x01, 0x00, 0x00 };
        Assert.Equal(2048UL, Gas7883(64, 64, 3, exp));
    }

    [Fact]
    public void LargeExp_UsesMultiplier16Not8()
    {
        // expLen=33, head all zeros → bitLen=0, iter = max(16*(33-32)+0, 1) = 16
        // maxLen=32 → complexity 16 → cost = 16*16 = 256 → floor 500
        var head = new byte[33]; // all zero
        Assert.Equal(500UL, Gas7883(32, 32, 33, head));

        // maxLen=64 → complexity 128 → cost = 128*16 = 2048
        Assert.Equal(2048UL, Gas7883(64, 64, 33, head));
    }

    [Fact]
    public void Eip2565_StillDividesBy3AndFloor200()
    {
        // maxLen=32 → words=4, mult=16; exp=0 → iter=1 → 16/3=5 → floor 200
        Assert.Equal(200UL, Gas2565(32, 32, 0, Array.Empty<byte>()));
    }

    [Fact]
    public void ComparedTo2565_TriplesWhenAboveFloor()
    {
        // maxLen=64 → 2565: words²=64, exp iter=16 → 64*16/3=341
        // 7883: 2*words²=128, iter=16 → 128*16=2048  (≈6× because *2 and no /3)
        var exp = new byte[] { 0x01, 0x00, 0x01 }; // 65537 → bit_length-1 = 16
        Assert.Equal(341UL, Gas2565(64, 64, 3, exp));
        Assert.Equal(2048UL, Gas7883(64, 64, 3, exp));
    }
}
