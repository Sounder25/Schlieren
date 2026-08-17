using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution;

/// <summary>
/// Applies block-level system operations that run before any transaction.
/// These calls do not count against the block gas limit and do not appear
/// in transaction receipts.
///
/// EIP-4788 (Cancun+): write parentBeaconBlockRoot into 0x000F3DF6...
/// EIP-2935 (Prague+): write parentHash into 0x0000F908...
///
/// Per EELS: process_system_transaction() is called once per block, before
/// any user transactions, using a synthetic tx from SYSTEM_ADDRESS.
/// </summary>
public static class BlockPrelude
{
    // EIP-4788 beacon roots contract (Cancun+)
    private static readonly Address BeaconRootsContract =
        Address.FromHex("0x000F3DF6D732807Ef1319fB7B8Bb8522d0Beac02");

    // EIP-2935 block-hash history contract (Prague+)
    private static readonly Address BlockHashHistoryContract =
        Address.FromHex("0x0000F90827F1C53A10cb7A02335B175320002935");

    // Synthetic system caller (2^160 - 1 ... the EELS SYSTEM_ADDRESS)
    private static readonly Address SystemAddress =
        Address.FromHex("0xfffffffffffffffffffffffffffffffffffffffe");

    /// <summary>
    /// Apply all block-level system operations required for the given block context.
    /// Call this once, before executing any transactions in the block.
    /// </summary>
    public static async Task ApplyAsync(
        BlockContext block,
        GlobalState state,
        IStateTransition pipeline,
        CancellationToken ct = default)
    {
        // EIP-4788: store parentBeaconBlockRoot at system contract
        if (block.Rules.HasEip4788BeaconRoot)
            await ApplyEip4788Async(block, state, pipeline, ct);

        // EIP-2935: store parentHash at block-hash history contract
        if (block.Rules.HasEip2935BlockHashHistory)
            await ApplyEip2935Async(block, state, pipeline, ct);
    }

    // ── EIP-4788 ──────────────────────────────────────────────────────────────
    // Calldata = abi.encode(parentBeaconBlockRoot) = the 32-byte root
    // System call: from=SYSTEM_ADDRESS, to=0x000F3DF6..., gas=30_000_000, value=0

    private static async Task ApplyEip4788Async(
        BlockContext block,
        GlobalState state,
        IStateTransition pipeline,
        CancellationToken ct)
    {
        // parentBeaconBlockRoot lives on the block context; if not populated (legacy test), skip.
        if (block.ParentBeaconBlockRoot == null || block.ParentBeaconBlockRoot.Length == 0)
            return;

        var calldata = new byte[32];
        var root = block.ParentBeaconBlockRoot;
        Buffer.BlockCopy(root, 0, calldata, 32 - root.Length, root.Length);

        var gasPrice = (ulong)block.BaseFeePerGas; // must meet fee floor; system addr has no real cost
        var tx = new Transaction
        {
            From                  = SystemAddress,
            To                    = BeaconRootsContract,
            Value                 = BigInteger.Zero,
            Data                  = calldata,
            GasLimit              = 30_000_000,
            GasPrice              = gasPrice,
            MaxFeePerGas          = gasPrice,
            MaxPriorityFeePerGas  = 0,
            TxType                = 0,
            Nonce                 = 0,
            Authorization         = TransactionAuthorization.System,
            AccessList            = Array.Empty<AccessListEntry>(),
            AuthorizationList     = Array.Empty<Eip7702Authorization>(),
            EnableTracing         = false,
        };

        await pipeline.ApplyTransactionAsync(tx, state, block, commit: true, ct: ct);
    }

    // ── EIP-2935 ──────────────────────────────────────────────────────────────
    // Calldata = abi.encode(parentHash) = the 32-byte parent block hash
    // System call: from=SYSTEM_ADDRESS, to=0x0000F908..., gas=30_000_000, value=0

    private static async Task ApplyEip2935Async(
        BlockContext block,
        GlobalState state,
        IStateTransition pipeline,
        CancellationToken ct)
    {
        // parentHash lives on BlockContext.Hash (which holds the current block's parent hash)
        if (block.Hash == null || block.Hash.Length == 0)
            return;

        var calldata = new byte[32];
        var hash = block.Hash;
        Buffer.BlockCopy(hash, 0, calldata, 32 - Math.Min(hash.Length, 32), Math.Min(hash.Length, 32));

        var gasPrice = (ulong)block.BaseFeePerGas;
        var tx = new Transaction
        {
            From                  = SystemAddress,
            To                    = BlockHashHistoryContract,
            Value                 = BigInteger.Zero,
            Data                  = calldata,
            GasLimit              = 30_000_000,
            GasPrice              = gasPrice,
            MaxFeePerGas          = gasPrice,
            MaxPriorityFeePerGas  = 0,
            TxType                = 0,
            Nonce                 = 0,
            Authorization         = TransactionAuthorization.System,
            AccessList            = Array.Empty<AccessListEntry>(),
            AuthorizationList     = Array.Empty<Eip7702Authorization>(),
            EnableTracing         = false,
        };

        await pipeline.ApplyTransactionAsync(tx, state, block, commit: true, ct: ct);
    }
}
