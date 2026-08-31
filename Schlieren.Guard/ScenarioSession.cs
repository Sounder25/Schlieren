using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forking;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Guard;

public sealed record ScenarioStep(
    string Name,
    Transaction Transaction,
    BlockContext Block,
    ExecutionResult Result,
    BigInteger BuyerEthBefore,
    BigInteger BuyerEthAfter,
    BigInteger TokenBalanceBefore,
    BigInteger TokenBalanceAfter,
    IReadOnlyList<GuardAccountSnapshot> PreState)
{
    public bool Succeeded => Result.IsSuccess;
    public ExecutionJournal? Journal => Result.Journal;
}

/// <summary>
/// One stateful scenario against immutable pinned chain state.
/// BUY → APPROVE → SELL must share this overlay; sells see buy mutations.
/// </summary>
public sealed class ScenarioSession
{
    private readonly IStateTransition _pipeline;
    private readonly Address? _token;

    private ScenarioSession(
        PinnedBase pinned,
        IGlobalState overlay,
        IStateTransition pipeline,
        Address buyer,
        Address? token)
    {
        Pinned = pinned;
        Overlay = overlay;
        _pipeline = pipeline;
        Buyer = buyer;
        _token = token;
        Steps = new List<ScenarioStep>();
    }

    public PinnedBase Pinned { get; }
    public IGlobalState Overlay { get; }
    public Address Buyer { get; }
    public IReadOnlyList<ScenarioStep> Steps { get; }

    public static async Task<ScenarioSession> OpenAsync(
        IForkProvider fork,
        ulong? blockNumber,
        Address buyer,
        Address? token = null,
        string forkName = "Prague",
        IStateTransition? pipeline = null,
        CancellationToken ct = default)
    {
        var chainId = await fork.GetChainIdAsync(ct);
        var number = blockNumber ?? await fork.GetLatestBlockNumberAsync(ct);
        var block = await fork.GetBlockByNumberAsync(number, ct)
            ?? throw new InvalidOperationException($"Fork provider returned no block {number}.");

        var overlay = new ForkingGlobalState(new GlobalState(), fork, number);
        return new ScenarioSession(
            PinnedBase.FromBlock(chainId, block, forkName),
            overlay,
            pipeline ?? GuardMachine.CreatePipeline(),
            buyer,
            token);
    }

    public static ScenarioSession OpenLocal(
        IGlobalState overlay,
        PinnedBase pinned,
        Address buyer,
        Address? token = null,
        IStateTransition? pipeline = null) =>
        new(pinned, overlay, pipeline ?? GuardMachine.CreatePipeline(), buyer, token);

    public void FundBuyer(BigInteger wei) => Overlay.SetBalance(Buyer, wei);

    public BlockContext CreateBlockContext(ulong extraBlocks = 0, ulong extraSeconds = 0) =>
        new()
        {
            ChainId = Pinned.ChainId,
            Number = Pinned.BlockNumber + extraBlocks,
            Timestamp = Pinned.Timestamp + extraSeconds,
            GasLimit = Pinned.GasLimit,
            BaseFeePerGas = Pinned.BaseFeePerGas,
            Coinbase = Pinned.Coinbase,
            Hash = ParseHash(Pinned.BlockHash),
            Rules = ForkRulesFactory.For(Pinned.ForkName)
        };

    public async Task<ScenarioStep> ExecuteAsync(
        string name,
        Address to,
        byte[] data,
        BigInteger value,
        ulong extraBlocks = 0,
        ulong extraSeconds = 0,
        ulong gasLimit = 2_000_000,
        CancellationToken ct = default)
    {
        var block = CreateBlockContext(extraBlocks, extraSeconds);
        var ethBefore = await Overlay.GetBalanceAsync(Buyer, ct);
        var tokenBefore = _token is { } token
            ? await ReadTokenBalanceAsync(token, ct)
            : BigInteger.Zero;

        var preState = GuardPreState.Capture(Overlay);
        var nonce = await Overlay.GetNonceAsync(Buyer, ct);
        var tx = new Transaction
        {
            From = Buyer,
            To = to,
            Data = data,
            Value = value,
            Nonce = nonce,
            GasLimit = gasLimit,
            GasPrice = Pinned.BaseFeePerGas,
            MaxFeePerGas = Pinned.BaseFeePerGas,
            MaxPriorityFeePerGas = BigInteger.Zero,
            TxType = 0,
            Authorization = TransactionAuthorization.Impersonated,
            EnableJournal = true
        };

        var result = await _pipeline.ApplyTransactionAsync(tx, Overlay, block, commit: true, ct);
        var ethAfter = await Overlay.GetBalanceAsync(Buyer, ct);
        var tokenAfter = _token is { } token2
            ? await ReadTokenBalanceAsync(token2, ct)
            : BigInteger.Zero;

        var step = new ScenarioStep(
            name, tx, block, result, ethBefore, ethAfter, tokenBefore, tokenAfter, preState);
        ((List<ScenarioStep>)Steps).Add(step);
        return step;
    }

    public async Task<BigInteger> ReadTokenBalanceAsync(Address token, CancellationToken ct = default)
    {
        var call = new Transaction
        {
            From = Buyer,
            To = token,
            Data = UniswapV2.BalanceOf(Buyer),
            GasLimit = 100_000,
            GasPrice = Pinned.BaseFeePerGas,
            Authorization = TransactionAuthorization.Impersonated,
            EnableJournal = false
        };
        var result = await _pipeline.ApplyTransactionAsync(
            call, Overlay, CreateBlockContext(), commit: false, ct);
        if (!result.IsSuccess)
            return BigInteger.Zero;
        return Abi.DecodeUint256(result.ReturnData);
    }

    public async Task<Address> CallAddressAsync(Address to, byte[] data, CancellationToken ct = default)
    {
        var call = new Transaction
        {
            From = Buyer,
            To = to,
            Data = data,
            GasLimit = 200_000,
            GasPrice = Pinned.BaseFeePerGas,
            Authorization = TransactionAuthorization.Impersonated
        };
        var result = await _pipeline.ApplyTransactionAsync(
            call, Overlay, CreateBlockContext(), commit: false, ct);
        if (!result.IsSuccess || result.ReturnData.Length < 32)
            return Address.Zero;
        return Abi.DecodeAddress(result.ReturnData);
    }

    private static byte[] ParseHash(string hash)
    {
        var bytes = new byte[32];
        if (string.IsNullOrWhiteSpace(hash))
            return bytes;
        var raw = Abi.FromHex(hash);
        var copy = Math.Min(32, raw.Length);
        raw.AsSpan(raw.Length - copy).CopyTo(bytes.AsSpan(32 - copy));
        return bytes;
    }
}
