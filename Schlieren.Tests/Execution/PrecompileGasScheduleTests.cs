using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Execution;

/// <summary>
/// Pins the Cancun precompile gas schedule (0x01-0x09) so changes made while
/// fixing legacy/precompile pricing cannot silently drift the values Schlieren
/// currently conforms to. See docs/FORK_GAS_AUDIT.md for fork origins.
/// </summary>
public sealed class PrecompileGasScheduleTests
{
    private static ExecutionResult Run(int precompileId, byte[] input, ulong gasLimit) =>
        Precompiles.ExecuteAsResult(
            Address.FromHex("0x00000000000000000000000000000000000000" + precompileId.ToString("x2")),
            input,
            gasLimit);

    [Fact]
    public void Ecrecover_Charges3000_AndReturnsEmptyForBadV()
    {
        var input = new byte[128];
        input[63] = 0xFF; // v = 255, not 27/28

        var result = Run(0x01, input, 3_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(3_000UL, result.GasUsed);
        Assert.Empty(result.ReturnData);
    }

    [Fact]
    public void Ecrecover_RecoversPublishedStaticCallVector()
    {
        var input = Convert.FromHexString(
            "18c547e4f7b0f325ad1e56f57e26c745b09a3e503d86e00e5255ff7f715d3d1c" +
            "000000000000000000000000000000000000000000000000000000000000001c" +
            "73b1693892219d736caba55bdb67216e485557ea6b6af75f37096c9aa6a5a75f" +
            "eeb940b1d03b21e36b0e47e79769f095fe2ab855bd91e3a38756b7d75a9c4549");

        var result = Run(0x01, input, 3_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(3_000UL, result.GasUsed);
        Assert.Equal(
            "000000000000000000000000a94f5374fce5edbc8e2a8697c15331677e6ebf0b",
            Convert.ToHexString(result.ReturnData).ToLowerInvariant());
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 72)]   // one word
    [InlineData(32, 72)]  // exactly one word
    [InlineData(33, 84)]  // two words
    public void Sha256_WordGasIs12(int inputLength, ulong expectedGas)
    {
        var result = Run(0x02, new byte[inputLength], 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedGas, result.GasUsed);
        Assert.Equal(32, result.ReturnData.Length);
    }

    [Fact]
    public void Ripemd160_Charges600_AndPadsOutputTo32()
    {
        var result = Run(0x03, Array.Empty<byte>(), 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(600UL, result.GasUsed);
        Assert.Equal(32, result.ReturnData.Length);
        Assert.True(result.ReturnData.Take(12).All(b => b == 0));
    }

    [Fact]
    public void Identity_Charges15_ForEmptyInput()
    {
        var result = Run(0x04, Array.Empty<byte>(), 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(15UL, result.GasUsed);
        Assert.Empty(result.ReturnData);
    }

    [Fact]
    public void ModExp_EmptyInput_ChargesMinimum200()
    {
        // EIP-2565 floor: max(200, multComp * iterCount / 3) with all sizes 0.
        var result = Run(0x05, new byte[96], 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(200UL, result.GasUsed);
        Assert.Empty(result.ReturnData);
    }

    [Fact]
    public void BnAdd_Charges150_ForPointAtInfinity()
    {
        var result = Run(0x06, new byte[128], 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(150UL, result.GasUsed);
        Assert.Equal(64, result.ReturnData.Length);
    }

    [Fact]
    public void BnMul_Charges6000_ForScalarZero()
    {
        var result = Run(0x07, new byte[96], 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(6_000UL, result.GasUsed);
        Assert.Equal(64, result.ReturnData.Length);
    }

    [Fact]
    public void BnPairing_EmptyInput_Charges45000_AndReturnsOne()
    {
        var result = Run(0x08, Array.Empty<byte>(), 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(45_000UL, result.GasUsed);
        Assert.Equal(32, result.ReturnData.Length);
        Assert.Equal(1, result.ReturnData[31]); // empty product of pairings == 1
    }

    [Fact]
    public void BnPairing_SinglePair_Charges79000_AndReturns32ByteWord()
    {
        var result = Run(0x08, new byte[192], 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(45_000UL + 34_000UL, result.GasUsed);
        Assert.Equal(32, result.ReturnData.Length);
    }

    [Fact]
    public void Blake2F_GasEqualsRounds()
    {
        var input = new byte[213];
        input[3] = 1; // rounds = 1 (big-endian)

        var result = Run(0x09, input, 100_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(1UL, result.GasUsed);
        Assert.Equal(64, result.ReturnData.Length);
    }

    private static byte[] Blake2FRoundsInput(uint rounds)
    {
        var input = new byte[213];
        input[0] = (byte)(rounds >> 24);
        input[1] = (byte)(rounds >> 16);
        input[2] = (byte)(rounds >> 8);
        input[3] = (byte)rounds;
        return input;
    }

    [Theory]
    [InlineData(0x01, 3_000)]
    [InlineData(0x02, 60)]
    [InlineData(0x03, 600)]
    [InlineData(0x04, 15)]
    [InlineData(0x05, 200)]
    [InlineData(0x06, 150)]
    [InlineData(0x07, 6_000)]
    [InlineData(0x08, 45_000)]
    [InlineData(0x09, 1)]
    public void Precompile_ChargesGas_WhenGivenExactlyItsCost(byte id, ulong exactCost)
    {
        var input = id switch
        {
            0x08 => Array.Empty<byte>(),
            0x09 => Blake2FRoundsInput(1),
            _ => Array.Empty<byte>()
        };

        var result = Run(id, input, exactCost);

        Assert.True(result.IsSuccess);
        Assert.Equal(exactCost, result.GasUsed);
    }
}
