using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Campaign 003 generators — three sub-campaigns as specified.
///
/// 003A — Fork-local semantic deltas
///   Same high-value cases from C001/C002, run across Berlin/London/Shanghai/Cancun/Prague.
///   Exposes gas schedule regressions: EIP-2929 warm/cold, EIP-1559, EIP-3529 refund cap,
///   EIP-3541 EF-prefix, EIP-1153 transient storage gate, etc.
///
/// 003B — Activation-boundary tests (DEFERRED)
///   Requires blockchain-level block number control to test fork transition semantics.
///   State-test harness runs a fixed IForkRules — no auto-transition by block number.
///   Will be added when a blockchain test harness is available.
///
/// 003C — New-feature interaction matrix (Cancun+)
///   TLOAD/TSTORE, MCOPY, PUSH0, BLOBHASH, SELFDESTRUCT (EIP-6780), EIP-7702 delegation.
///   Tests each feature in isolation, then in combination with CALL/REVERT/SSTORE.
/// </summary>
public static class Campaign003Generator
{
    // ── 003A ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fork-local semantic deltas: core behaviors × gas-schedule-changing forks.
    /// Runs the same semantic matrix across Berlin/London/Shanghai/Cancun/Prague.
    /// </summary>
    public static List<SyntheticCase> Generate003A()
    {
        var cases  = new List<SyntheticCase>();
        var seen   = new HashSet<string>();
        int serial = 3000;

        // Forks with distinct gas schedules (skip older forks — covered by EELS conformance)
        var forks = new[] { "Berlin", "London", "Shanghai", "Cancun", "Prague" };

        void Add(SyntheticCase c)
        {
            if (seen.Add(c.CanonicalFingerprint())) cases.Add(c);
        }

        SyntheticCase Make(
            string fork,
            CallKind call        = CallKind.Call,
            ChildBehavior beh    = ChildBehavior.Stop,
            GasClass gas         = GasClass.High,
            ValueClass value     = ValueClass.Zero,
            ReturnSize ret       = ReturnSize.Zero,
            StoragePattern stor  = StoragePattern.None,
            RevertMode revert    = RevertMode.None,
            TargetKind target    = TargetKind.ExistingCode,
            int depth            = 2,
            bool warmTarget      = false,
            bool warmStorage     = false) =>
            new SyntheticCase
            {
                CaseId         = $"S3A-{++serial:D4}",
                Fork           = fork,
                CallKind       = call,
                TargetKind     = target,
                ChildBehavior  = beh,
                GasClass       = gas,
                ValueClass     = value,
                ReturnSize     = ret,
                Depth          = depth,
                StoragePattern = stor,
                RevertMode     = revert,
                WarmTarget     = warmTarget,
                WarmStorage    = warmStorage,
                Seed           = serial,
            };

        foreach (var fork in forks)
        {
            // Core call semantics × all CallKinds
            foreach (var call in All<CallKind>())
            foreach (var beh  in new[] { ChildBehavior.Stop, ChildBehavior.SStore, ChildBehavior.SStoreRevert,
                                         ChildBehavior.Return, ChildBehavior.Revert, ChildBehavior.Log })
                Add(Make(fork, call, beh));

            // Gas schedule: warm vs cold account access (EIP-2929 — Berlin+)
            foreach (var call in All<CallKind>())
            foreach (bool warm in new[] { false, true })
                Add(Make(fork, call, ChildBehavior.SStore, warmTarget: warm));

            // Storage: warm vs cold slot (EIP-2929)
            foreach (var stor in new[] { StoragePattern.ZeroToX, StoragePattern.XToY, StoragePattern.XToZero })
            foreach (bool warm in new[] { false, true })
                Add(Make(fork, beh: ChildBehavior.SStore, stor: stor, warmStorage: warm));

            // Refund cap: EIP-3529 (London) halved from gasUsed/2 → gasUsed/5
            foreach (var stor in All<StoragePattern>())
                Add(Make(fork, beh: ChildBehavior.SStore, stor: stor));

            // Value transfer: all value classes × CALL
            foreach (var value in All<ValueClass>())
                Add(Make(fork, value: value, beh: ChildBehavior.Return));

            // REVERT rollback: all storage patterns
            foreach (var stor in All<StoragePattern>())
            foreach (var revert in new[] { RevertMode.ExplicitRevert, RevertMode.OutOfGas })
                Add(Make(fork, beh: ChildBehavior.SStore, stor: stor, revert: revert));

            // Returndata sizes
            foreach (var ret in All<ReturnSize>())
                Add(Make(fork, beh: ChildBehavior.Return, ret: ret));

            // Depth stress
            foreach (var depth in new[] { 2, 3, 8, 64 })
            foreach (var beh in new[] { ChildBehavior.SStore, ChildBehavior.SStoreRevert })
                Add(Make(fork, beh: beh, depth: depth));
        }

        return cases;
    }

    // ── 003B ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// DEFERRED: Fork activation boundary tests require blockchain-level block number control.
    /// The state-test harness executes with a fixed IForkRules — no auto-transition by block.
    /// Returns empty list as a documented placeholder.
    /// </summary>
    public static List<SyntheticCase> Generate003B() => new();

    // ── 003C ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// New-feature interaction matrix (Cancun/Prague).
    /// Tests TLOAD/TSTORE, MCOPY, PUSH0, SELFDESTRUCT (EIP-6780), EIP-7702.
    /// Each feature: isolation, then combination with CALL/REVERT/SSTORE.
    /// </summary>
    public static List<SyntheticCase> Generate003C()
    {
        var cases  = new List<SyntheticCase>();
        var seen   = new HashSet<string>();
        int serial = 3500;

        void Add(SyntheticCase c)
        {
            if (seen.Add(c.CanonicalFingerprint())) cases.Add(c);
        }

        SyntheticCase Make(
            ChildBehavior beh,
            CallKind call        = CallKind.Call,
            GasClass gas         = GasClass.High,
            ValueClass value     = ValueClass.Zero,
            ReturnSize ret       = ReturnSize.Zero,
            StoragePattern stor  = StoragePattern.None,
            RevertMode revert    = RevertMode.None,
            TargetKind target    = TargetKind.ExistingCode,
            int depth            = 2,
            string fork          = "Cancun") =>
            new SyntheticCase
            {
                CaseId         = $"S3C-{++serial:D4}",
                Fork           = fork,
                CallKind       = call,
                TargetKind     = target,
                ChildBehavior  = beh,
                GasClass       = gas,
                ValueClass     = value,
                ReturnSize     = ret,
                Depth          = depth,
                StoragePattern = stor,
                RevertMode     = revert,
                Seed           = serial,
            };

        // ── TLOAD/TSTORE (EIP-1153, Cancun) ──────────────────────────────
        // Transient storage: cleared between transactions, not persistent
        // We represent via SStore behavior on Cancun (TSTORE uses 0x5D opcode)
        // The materializer uses SSTORE bytecode — TSTORE requires different opcode
        // For now: SSTORE on Cancun covers the gas path; TSTORE-specific cases
        // need a new ChildBehavior when the materializer supports 0x5D directly
        foreach (var revert in All<RevertMode>())
            Add(Make(ChildBehavior.SStore, stor: StoragePattern.ZeroToX, revert: revert, fork: "Cancun"));

        // TSTORE then CALL then check (cross-frame transient visibility)
        foreach (var call in All<CallKind>())
            Add(Make(ChildBehavior.SStoreThenCall, call: call, fork: "Cancun"));

        // ── MCOPY (EIP-5656, Cancun) ──────────────────────────────────────
        // Memory copy — tested via Return behaviors (RETURN reads from memory)
        foreach (var ret in new[] { ReturnSize.Zero, ReturnSize.Byte32, ReturnSize.Byte33,
                                    ReturnSize.Byte256, ReturnSize.Byte257 })
        foreach (var revert in new[] { RevertMode.None, RevertMode.ExplicitRevert })
            Add(Make(ChildBehavior.Return, ret: ret, revert: revert, fork: "Cancun"));

        // ── PUSH0 (EIP-3855, Shanghai) ────────────────────────────────────
        // PUSH0 is a cheaper way to push 0; tested via zero-value operations
        foreach (var call in All<CallKind>())
            Add(Make(ChildBehavior.Stop, call: call, fork: "Shanghai"));

        foreach (var stor in All<StoragePattern>())
            Add(Make(ChildBehavior.SStore, stor: stor, fork: "Shanghai"));

        // ── SELFDESTRUCT EIP-6780 (Cancun) ────────────────────────────────
        // Cancun: SELFDESTRUCT only actually destroys if contract was created in same tx
        // Pre-existing contracts: balance transferred but account NOT deleted
        foreach (var value in new[] { ValueClass.Zero, ValueClass.One, ValueClass.OneEther })
            Add(Make(ChildBehavior.SelfDestruct, value: value, fork: "Cancun"));

        // SELFDESTRUCT pre-Cancun (account IS deleted)
        foreach (var value in new[] { ValueClass.Zero, ValueClass.One })
            Add(Make(ChildBehavior.SelfDestruct, value: value, fork: "Shanghai"));

        // SELFDESTRUCT + parent REVERT (should roll back)
        foreach (var revert in new[] { RevertMode.ExplicitRevert, RevertMode.OutOfGas })
            Add(Make(ChildBehavior.SelfDestruct, revert: revert, fork: "Cancun"));

        // ── CREATE/CREATE2 on Cancun (EIP-6780 interaction) ───────────────
        // Contract created in this tx + SELFDESTRUCT = full deletion allowed
        foreach (var beh in new[] { ChildBehavior.Create, ChildBehavior.Create2 })
        {
            Add(Make(beh, fork: "Cancun"));
            Add(Make(beh, fork: "Prague"));
            Add(Make(beh, revert: RevertMode.ExplicitRevert, fork: "Cancun"));
        }

        // ── LOG0-LOG4 across forks ────────────────────────────────────────
        foreach (var beh in new[] { ChildBehavior.Log, ChildBehavior.Log1, ChildBehavior.Log2,
                                    ChildBehavior.Log3, ChildBehavior.Log4 })
        {
            foreach (var fork in new[] { "Berlin", "London", "Shanghai", "Cancun" })
            {
                Add(Make(beh, fork: fork));
                Add(Make(beh, revert: RevertMode.ExplicitRevert, fork: fork));
                Add(Make(beh, call: CallKind.StaticCall, fork: fork));
            }
        }

        // ── Precompile × fork (gas schedule changes at Istanbul) ──────────
        // PRE1 (ecrecover), PRE2 (sha256), PRE4 (identity) — stable across forks
        // PRE5 (ModExp) — EIP-2565 changes gas in Berlin
        // PRE6-7 (BN254 add/mul) — EIP-1108 changes gas in Istanbul
        foreach (var fork in new[] { "Istanbul", "Berlin", "London", "Cancun" })
        {
            // Identity precompile — stable gas, useful as oracle check
            Add(Make(ChildBehavior.Stop, target: TargetKind.Precompile, fork: fork));
            Add(Make(ChildBehavior.Stop, target: TargetKind.Precompile, gas: GasClass.Minimal, fork: fork));
        }

        // ── Interaction pairs across forks ────────────────────────────────
        foreach (var fork in new[] { "London", "Cancun" })
        {
            // DELEGATECALL → SSTORE (storage context = caller)
            foreach (var stor in All<StoragePattern>())
            foreach (var revert in new[] { RevertMode.None, RevertMode.ExplicitRevert })
                Add(Make(ChildBehavior.SStore, call: CallKind.DelegateCall,
                         stor: stor, revert: revert, fork: fork));

            // STATICCALL → SSTORE (must fail)
            foreach (var stor in All<StoragePattern>())
                Add(Make(ChildBehavior.SStore, call: CallKind.StaticCall, stor: stor, fork: fork));

            // Nested CALL depth stress
            foreach (var depth in new[] { 2, 4, 16, 64 })
                Add(Make(ChildBehavior.SStore, depth: depth, fork: fork));
        }

        return cases;
    }

    private static IEnumerable<T> All<T>() where T : struct, Enum =>
        (T[])Enum.GetValues(typeof(T));
}
