using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Forks;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.Tests.Execution;

/// <summary>
/// EIP-7825 (Osaka): transactions with gas limit &gt; 16_777_216 are invalid
/// pre-execution — no nonce bump, no balance debit.
/// </summary>
public sealed class Eip7825TxGasLimitCapTests
{
    private static readonly Address Sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
    private static readonly Address To = Address.FromHex("0x0000000000000000000000000000000000001000");
    private const ulong TxMax = 16_777_216UL;

    private static StateTransition Transition()
    {
        var opcodes = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IOpcode).IsAssignableFrom(type))
            .Select(type => (IOpcode)Activator.CreateInstance(type)!);
        return new StateTransition(new EvmMachine(opcodes));
    }

    private static GlobalState FundedState()
    {
        var state = new GlobalState();
        state.SetBalance(Sender, BigInteger.Parse("1000000000000000000000")); // 1000 ETH
        state.SetNonce(Sender, 0);
        return state;
    }

    private static Transaction OverCapTx() => new()
    {
        From = Sender,
        To = To,
        Value = BigInteger.Zero,
        Nonce = 0,
        GasPrice = 1,
        GasLimit = TxMax + 1,
        Data = Array.Empty<byte>(),
        TxType = 0,
        Authorization = TransactionAuthorization.Impersonated,
    };

    private static Transaction AtCapTx() => new()
    {
        From = Sender,
        To = To,
        Value = BigInteger.Zero,
        Nonce = 0,
        GasPrice = 1,
        GasLimit = TxMax,
        Data = Array.Empty<byte>(),
        TxType = 0,
        Authorization = TransactionAuthorization.Impersonated,
    };

    private static BlockContext OsakaBlock() => new()
    {
        Number = 1,
        BaseFeePerGas = 0,
        GasLimit = 30_000_000,
        Coinbase = Address.Zero,
        Rules = OsakaRules.Instance,
    };

    private static BlockContext PragueBlock() => new()
    {
        Number = 1,
        BaseFeePerGas = 0,
        GasLimit = 30_000_000,
        Coinbase = Address.Zero,
        Rules = PragueRules.Instance,
    };

    [Fact]
    public async Task Osaka_RejectsGasLimitAbove2Pow24()
    {
        var state = FundedState();
        var result = await Transition().ApplyTransactionAsync(OverCapTx(), state, OsakaBlock());

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.InvalidTransaction, result.Error);
        Assert.Equal(0UL, await state.GetNonceAsync(Sender));
        Assert.Equal(BigInteger.Parse("1000000000000000000000"), await state.GetBalanceAsync(Sender));
    }

    [Fact]
    public async Task Osaka_AcceptsGasLimitExactly2Pow24()
    {
        var state = FundedState();
        var result = await Transition().ApplyTransactionAsync(AtCapTx(), state, OsakaBlock());

        // Cap is exclusive upper bound: gas == 2^24 is allowed (EELS: tx.gas > TX_MAX).
        Assert.True(result.IsSuccess);
        Assert.Equal(1UL, await state.GetNonceAsync(Sender));
    }

    [Fact]
    public async Task Prague_StillAllowsGasLimitAbove2Pow24()
    {
        var state = FundedState();
        var result = await Transition().ApplyTransactionAsync(OverCapTx(), state, PragueBlock());

        // Pre-Osaka: no EIP-7825 — over-cap txs remain valid if funded.
        Assert.True(result.IsSuccess);
        Assert.Equal(1UL, await state.GetNonceAsync(Sender));
    }

    [Fact]
    public void OsakaRules_ExposeCap()
    {
        Assert.True(OsakaRules.Instance.HasEip7825TxGasLimitCap);
        Assert.Equal(TxMax, OsakaRules.Instance.TxMaxGasLimit);
        Assert.False(PragueRules.Instance.HasEip7825TxGasLimitCap);
    }
}
