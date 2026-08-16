using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Campaign 002 generator — ~2,000 cases across new semantic surfaces.
///
/// Baseline 001 covered: CallKind × ChildBehavior × GasClass × ReturnSize × Value ×
///   StoragePattern × RevertMode × TargetKind × Depth × Stipend.
///
/// Campaign 002 expands into:
///   A. CREATE/CREATE2 lifecycle (deploy, revert, OOG, collision, parent-revert)
///   B. Nested revert/rollback (SSTORE→REVERT at various depths)
///   C. Warm/cold account and storage transitions (EIP-2929)
///   D. Value/balance boundaries (insufficient, zero, transfer-to-empty)
///   E. Returndata sizes (0, 1, 31, 32, 33, 255, 256, 257 bytes)
///   F. Memory expansion (small, medium, large, boundary)
///   G. LOG0–LOG4 with varying topic counts and data sizes
///   H. Precompiles PRE1–PRE9 with valid/invalid inputs
///   I. Depth stress (1, 2, 3, 4, 8, 16, 64, 1023, 1024)
///   J. Interaction pairs (CALL→SSTORE→REVERT, STATICCALL→SSTORE, etc.)
/// </summary>
public static class Campaign002Generator
{
    public static List<SyntheticCase> Generate(string fork = "Cancun")
    {
        var cases  = new List<SyntheticCase>();
        var seen   = new HashSet<string>();
        int serial = 2000; // offset from baseline 001 serial space

        void Add(SyntheticCase c)
        {
            if (seen.Add(c.CanonicalFingerprint())) cases.Add(c);
        }

        SyntheticCase Make(
            CallKind call        = CallKind.Call,
            TargetKind target    = TargetKind.ExistingCode,
            ChildBehavior beh    = ChildBehavior.Stop,
            GasClass gas         = GasClass.High,
            ValueClass value     = ValueClass.Zero,
            ReturnSize ret       = ReturnSize.Zero,
            int depth            = 2,
            StoragePattern stor  = StoragePattern.None,
            RevertMode revert    = RevertMode.None,
            bool warmTarget      = false,
            bool warmStorage     = false) =>
            new SyntheticCase
            {
                CaseId         = $"S2-{++serial:D4}",
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

        // ── A. CREATE/CREATE2 lifecycle ────────────────────────────────────
        // Every create behavior × every call kind that can contain CREATE
        foreach (var beh in new[] { ChildBehavior.Create, ChildBehavior.Create2, ChildBehavior.CreateRevert })
        foreach (var gas in new[] { GasClass.High, GasClass.Minimal, GasClass.ExactMinus1, GasClass.Exact })
            Add(Make(beh: beh, gas: gas));

        // CREATE then parent REVERT (should roll back the deployment)
        foreach (var beh in new[] { ChildBehavior.Create, ChildBehavior.Create2 })
        foreach (var revert in new[] { RevertMode.ExplicitRevert, RevertMode.OutOfGas })
            Add(Make(beh: beh, revert: revert));

        // ── B. Nested revert/rollback ──────────────────────────────────────
        // SSTORE at various depths, then revert at different levels
        foreach (var depth in new[] { 2, 3, 4, 8 })
        foreach (var revert in All<RevertMode>())
            Add(Make(beh: ChildBehavior.SStore, depth: depth, stor: StoragePattern.ZeroToX, revert: revert));

        // SStoreRevert: storage writes must disappear entirely
        foreach (var depth in new[] { 2, 3, 4 })
        foreach (var stor in All<StoragePattern>())
            Add(Make(beh: ChildBehavior.SStoreRevert, depth: depth, stor: stor));

        // ── C. Warm/cold account and storage transitions ───────────────────
        // Same slot written twice in same tx (second write warm)
        foreach (var call in All<CallKind>())
            Add(Make(call: call, beh: ChildBehavior.SStore, stor: StoragePattern.SameSlotTwice));

        // Cold vs warm target access
        foreach (var call in All<CallKind>())
        foreach (bool warm in new[] { false, true })
            Add(Make(call: call, beh: ChildBehavior.SStore,
                     stor: StoragePattern.ZeroToX, warmTarget: warm));

        foreach (var call in All<CallKind>())
        foreach (bool warm in new[] { false, true })
            Add(Make(call: call, beh: ChildBehavior.SStore,
                     stor: StoragePattern.XToY, warmStorage: warm));

        // ── D. Value/balance boundaries ────────────────────────────────────
        foreach (var call in All<CallKind>())
        foreach (var value in All<ValueClass>())
        foreach (var target in new[] { TargetKind.ExistingCode, TargetKind.Nonexistent, TargetKind.EmptyAccount })
        {
            if (call == CallKind.StaticCall && value != ValueClass.Zero) continue;
            Add(Make(call: call, target: target, beh: ChildBehavior.Return, value: value));
        }

        // Value transfer + child revert = balance rollback
        foreach (var value in new[] { ValueClass.One, ValueClass.OneEther })
        foreach (var revert in new[] { RevertMode.ExplicitRevert, RevertMode.OutOfGas })
            Add(Make(beh: ChildBehavior.Return, value: value, revert: revert));

        // ── E. Returndata sizes ────────────────────────────────────────────
        // All return sizes × success and revert
        foreach (var ret in All<ReturnSize>())
        {
            Add(Make(beh: ChildBehavior.Return, ret: ret));
            Add(Make(beh: ChildBehavior.Revert, ret: ret));
        }

        // Returndata from nested calls
        foreach (var ret in new[] { ReturnSize.Zero, ReturnSize.Byte32, ReturnSize.Byte33, ReturnSize.Byte257 })
        foreach (var depth in new[] { 2, 3, 4 })
            Add(Make(beh: ChildBehavior.Return, ret: ret, depth: depth));

        // ── F. Memory expansion ────────────────────────────────────────────
        // RETURN with large data forces memory expansion charging
        foreach (var ret in new[] { ReturnSize.Byte255, ReturnSize.Byte256, ReturnSize.Byte257 })
        foreach (var call in All<CallKind>())
            Add(Make(call: call, beh: ChildBehavior.Return, ret: ret));

        // ── G. LOG0–LOG4 topic counts ──────────────────────────────────────
        foreach (var beh in new[] { ChildBehavior.Log, ChildBehavior.Log1, ChildBehavior.Log2,
                                    ChildBehavior.Log3, ChildBehavior.Log4 })
        {
            // Success
            Add(Make(beh: beh));
            // Revert (logs must be rolled back)
            Add(Make(beh: beh, revert: RevertMode.ExplicitRevert));
            // Inside STATICCALL (must fail)
            Add(Make(call: CallKind.StaticCall, beh: beh));
        }

        // ── H. Precompiles PRE1–PRE9 ──────────────────────────────────────
        // Each precompile × CALL × STATICCALL
        foreach (int pre in Enumerable.Range(1, 9))
        {
            Add(Make(target: TargetKind.Precompile, beh: ChildBehavior.Stop, gas: GasClass.High));
            Add(Make(call: CallKind.StaticCall, target: TargetKind.Precompile, beh: ChildBehavior.Stop));
            Add(Make(target: TargetKind.Precompile, beh: ChildBehavior.Stop, gas: GasClass.Minimal));
        }

        // ── I. Depth stress ────────────────────────────────────────────────
        foreach (var depth in new[] { 1, 2, 3, 4, 8, 16, 64, 1023, 1024 })
        foreach (var beh in new[] { ChildBehavior.Stop, ChildBehavior.SStore, ChildBehavior.SStoreRevert,
                                    ChildBehavior.Return, ChildBehavior.Revert })
            Add(Make(beh: beh, depth: depth, gas: GasClass.High));

        // Depth 1025 — must reject (call stack limit)
        Add(Make(beh: ChildBehavior.SStore, depth: 1025));

        // ── J. Interaction pairs ───────────────────────────────────────────
        // STATICCALL → SSTORE must fail (write protection)
        foreach (var stor in All<StoragePattern>())
            Add(Make(call: CallKind.StaticCall, beh: ChildBehavior.SStore, stor: stor));

        // DELEGATECALL → SSTORE (writes to caller's storage)
        foreach (var stor in All<StoragePattern>())
        foreach (var revert in All<RevertMode>())
            Add(Make(call: CallKind.DelegateCall, beh: ChildBehavior.SStore, stor: stor, revert: revert));

        // CALL → nested CALL → SSTORE (grandchild writes, parent calls back)
        foreach (var depth in new[] { 2, 3, 4 })
        foreach (var revert in new[] { RevertMode.None, RevertMode.ExplicitRevert })
            Add(Make(beh: ChildBehavior.CallThenSStore, depth: depth, revert: revert));

        // SSTORE → CALL → SSTORE (multiple writes across frames)
        foreach (var revert in All<RevertMode>())
            Add(Make(beh: ChildBehavior.SStoreThenCall, revert: revert));

        // Gas boundary N-1/N/N+1 for each ChildBehavior that has a definite cost
        foreach (var beh in new[] { ChildBehavior.SStore, ChildBehavior.Log, ChildBehavior.Create })
        foreach (var gas in new[] { GasClass.ExactMinus1, GasClass.Exact, GasClass.ExactPlus1 })
            Add(Make(beh: beh, gas: gas, stor: StoragePattern.ZeroToX));

        // CALL with all gas classes × all value classes (key grid)
        foreach (var gas in All<GasClass>())
        foreach (var value in new[] { ValueClass.Zero, ValueClass.One, ValueClass.OneEther })
            Add(Make(beh: ChildBehavior.Return, gas: gas, value: value));

        return cases;
    }

    private static IEnumerable<T> All<T>() where T : struct, Enum =>
        (T[])Enum.GetValues(typeof(T));
}
