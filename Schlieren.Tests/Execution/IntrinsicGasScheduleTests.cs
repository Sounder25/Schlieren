using System.Numerics;
using System.Reflection;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

/// <summary>
/// Pins transaction intrinsic gas (IntrinsicGas.cs). Guards the legacy gaps
/// documented in docs/FORK_GAS_AUDIT.md: EIP-2028 (16/byte, Istanbul+) and
/// EIP-3860 (2/word initcode, Shanghai+) are currently unconditional.
/// </summary>
public sealed class IntrinsicGasScheduleTests
{
    private static readonly Address To = Address.FromHex("0x0000000000000000000000000000000000001000");

    private static Transaction Tx(byte[]? data = null, bool create = false)
    {
        return new Transaction
        {
            To = create ? null : To,
            Data = data ?? Array.Empty<byte>(),
            GasLimit = 1_000_000
        };
    }

    [Fact]
    public void PublicApi_RequiresExplicitForkRules()
    {
        var methods = typeof(IntrinsicGas).GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.DoesNotContain(methods, method => method.Name == nameof(IntrinsicGas.Compute) &&
            method.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual([typeof(Transaction)]));
        Assert.DoesNotContain(methods, method => method.Name == nameof(IntrinsicGas.TryCompute) &&
            method.GetParameters().All(parameter => parameter.ParameterType != typeof(IForkRules)));
    }

    [Fact]
    public void PlainCall_Charges21000()
    {
        Assert.Equal(21_000UL, IntrinsicGas.Compute(Tx(), ForkRulesFactory.Latest));
    }

    [Fact]
    public void SingleZeroCalldataByte_Charges21004()
    {
        Assert.Equal(21_004UL, IntrinsicGas.Compute(Tx(new byte[] { 0x00 }), ForkRulesFactory.Latest));
    }

    [Fact]
    public void SingleNonZeroCalldataByte_Charges21016()
    {
        Assert.Equal(21_016UL, IntrinsicGas.Compute(Tx(new byte[] { 0x01 }), ForkRulesFactory.Latest));
    }

    [Fact]
    public void ContractCreation_Charges53000()
    {
        Assert.Equal(53_000UL, IntrinsicGas.Compute(Tx(create: true), ForkRulesFactory.Latest));
    }

    [Fact]
    public void Creation_With32ZeroInitcodeBytes_ChargesEip3860WordGas()
    {
        // 53000 base + 2 (initcode word) + 4*32 (zero calldata bytes) = 53130
        Assert.Equal(53_130UL, IntrinsicGas.Compute(Tx(new byte[32], create: true), ForkRulesFactory.Latest));
    }

    [Fact]
    public void Creation_With32NonZeroInitcodeBytes_ChargesEip3860WordGas()
    {
        // 53000 base + 2 (initcode word) + 16*32 (non-zero calldata bytes) = 53514
        Assert.Equal(53_514UL, IntrinsicGas.Compute(Tx(Enumerable.Repeat((byte)0xFF, 32).ToArray(), create: true), ForkRulesFactory.Latest));
    }

    [Fact]
    public void AccessList_Charges2400PerAddress_And1900PerKey()
    {
        var tx = Tx();
        tx.AccessList = new[]
        {
            new AccessListEntry
            {
                Address = Address.FromHex("0x0000000000000000000000000000000000000001"),
                StorageKeys = new[] { BigInteger.One }
            }
        };

        Assert.Equal(21_000UL + 2_400UL + 1_900UL, IntrinsicGas.Compute(tx, ForkRulesFactory.Latest));
    }

    [Fact]
    public void TryCompute_FailsWhenGasLimitBelowIntrinsic()
    {
        var tx = Tx();
        tx.GasLimit = 20_000;

        Assert.False(IntrinsicGas.TryCompute(tx, ForkRulesFactory.Latest, out _));
    }
}
