using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution;

/// <summary>
/// Block-end operations after user transactions (EELS apply_body).
/// EIP-4895 withdrawals, then EIP-7002 / EIP-7251 request system calls.
/// </summary>
public static class BlockEpilogue
{
    public static readonly Address WithdrawalRequestContract =
        Address.FromHex("0x00000961Ef480Eb55e80D19ad83579A64c007002");

    public static readonly Address ConsolidationRequestContract =
        Address.FromHex("0x0000BBdDc7CE488642fb579F8B00f3A590007251");

    private static readonly Address SystemAddress =
        Address.FromHex("0xfffffffffffffffffffffffffffffffffffffffe");

    public const ulong GweiToWei = 1_000_000_000UL;

    public static async Task ApplyAsync(
        BlockContext block,
        GlobalState state,
        IStateTransition pipeline,
        IReadOnlyList<(Address Address, ulong AmountGwei)>? withdrawals = null,
        CancellationToken ct = default)
    {
        if (block.Rules.HasEip4895Withdrawals && withdrawals is { Count: > 0 })
            ApplyWithdrawals(state, withdrawals);

        if (block.Rules.HasEip7685Requests)
        {
            await ApplyRequestSystemCallAsync(block, state, pipeline, WithdrawalRequestContract, ct);
            await ApplyRequestSystemCallAsync(block, state, pipeline, ConsolidationRequestContract, ct);
        }
    }

    public static void ApplyWithdrawals(
        GlobalState state,
        IReadOnlyList<(Address Address, ulong AmountGwei)> withdrawals)
    {
        foreach (var (address, amountGwei) in withdrawals)
        {
            var current = state.GetBalanceAsync(address).GetAwaiter().GetResult();
            state.SetBalance(address, current + new BigInteger(amountGwei) * GweiToWei);
        }
    }

    private static async Task ApplyRequestSystemCallAsync(
        BlockContext block,
        GlobalState state,
        IStateTransition pipeline,
        Address target,
        CancellationToken ct)
    {
        var code = await state.GetCodeAsync(target, ct);
        if (code.Length == 0)
            return;

        var gasPrice = (ulong)block.BaseFeePerGas;
        var tx = new Transaction
        {
            From = SystemAddress,
            To = target,
            Value = BigInteger.Zero,
            Data = Array.Empty<byte>(),
            GasLimit = 30_000_000,
            GasPrice = gasPrice,
            MaxFeePerGas = gasPrice,
            MaxPriorityFeePerGas = 0,
            TxType = 0,
            Nonce = 0,
            Authorization = TransactionAuthorization.System,
            AccessList = Array.Empty<AccessListEntry>(),
            AuthorizationList = Array.Empty<Eip7702Authorization>(),
            EnableTracing = false,
        };

        await pipeline.ApplyTransactionAsync(tx, state, block, commit: true, ct: ct);
    }
}
