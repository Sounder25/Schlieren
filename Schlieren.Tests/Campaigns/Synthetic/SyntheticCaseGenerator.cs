using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Generates synthetic cases via pairwise (covering-array) coverage.
/// Every critical pair of dimensions appears at least once.
/// No Cartesian explosion — ~500 cases for the first CALL-state batch.
/// </summary>
public static class SyntheticCaseGenerator
{
    public static List<SyntheticCase> GenerateCallStateInteractions(string fork = "Cancun")
    {
        var cases  = new List<SyntheticCase>();
        var seen   = new HashSet<string>();
        int serial = 0;

        void Add(SyntheticCase c)
        {
            if (seen.Add(c.CanonicalFingerprint())) cases.Add(c);
        }

        SyntheticCase Make(
            CallKind call, TargetKind target, ChildBehavior behavior,
            GasClass gas        = GasClass.High,
            ValueClass value    = ValueClass.Zero,
            ReturnSize ret      = ReturnSize.Zero,
            int depth           = 2,
            StoragePattern stor = StoragePattern.None,
            RevertMode revert   = RevertMode.None,
            bool warmTarget     = false,
            bool warmStorage    = false) =>
            new SyntheticCase
            {
                CaseId        = $"SYN-{++serial:D4}",
                Fork          = fork,
                CallKind      = call,
                TargetKind    = target,
                ChildBehavior = behavior,
                GasClass      = gas,
                ValueClass    = value,
                ReturnSize    = ret,
                Depth         = depth,
                StoragePattern = stor,
                RevertMode    = revert,
                WarmTarget    = warmTarget,
                WarmStorage   = warmStorage,
                Seed          = serial,
            };

        // 1. CallKind × ChildBehavior — all 4×11 = 44 pairs
        foreach (var call in All<CallKind>())
        foreach (var beh  in All<ChildBehavior>())
            Add(Make(call, TargetKind.ExistingCode, beh));

        // 2. ChildBehavior × GasClass — all 11×9 = 99 pairs
        foreach (var beh in All<ChildBehavior>())
        foreach (var gas in All<GasClass>())
            Add(Make(CallKind.Call, TargetKind.ExistingCode, beh, gas: gas));

        // 3. ChildBehavior × ReturnSize — all 11×8 = 88 pairs
        foreach (var beh in All<ChildBehavior>())
        foreach (var ret in All<ReturnSize>())
            Add(Make(CallKind.Call, TargetKind.ExistingCode, beh, ret: ret));

        // 4. CallKind × ValueClass — all 4×6 = 24 pairs
        foreach (var call  in All<CallKind>())
        foreach (var value in All<ValueClass>())
            Add(Make(call, TargetKind.ExistingCode, ChildBehavior.Return, value: value));

        // 5. Depth × key ChildBehaviors
        var keyBehaviors = new[] {
            ChildBehavior.Stop, ChildBehavior.SStore, ChildBehavior.SStoreRevert,
            ChildBehavior.Return, ChildBehavior.Revert, ChildBehavior.OutOfGas,
        };
        foreach (var depth in new[] { 1, 2, 3, 4, 16, 64, 1023, 1024 })
        foreach (var beh   in keyBehaviors)
            Add(Make(CallKind.Call, TargetKind.ExistingCode, beh, depth: depth));

        // 6. StoragePattern × RevertMode — all 6×4 = 24 pairs
        foreach (var stor   in All<StoragePattern>())
        foreach (var revert in All<RevertMode>())
            Add(Make(CallKind.Call, TargetKind.ExistingCode, ChildBehavior.SStore,
                     stor: stor, revert: revert));

        // 7. TargetKind × CallKind — all 5×4 = 20 pairs (minus invalid)
        foreach (var target in All<TargetKind>())
        foreach (var call   in All<CallKind>())
        {
            // StaticCall cannot transfer value → skip value-bearing combos
            if (call == CallKind.StaticCall && target == TargetKind.Nonexistent)
                continue;
            Add(Make(call, target, ChildBehavior.Stop));
        }

        // 8. Gas boundary stress: N-1 / N / N+1 × CallKind × SStore
        foreach (var call in All<CallKind>())
        foreach (var gas  in new[] { GasClass.ExactMinus1, GasClass.Exact, GasClass.ExactPlus1 })
            Add(Make(call, TargetKind.ExistingCode, ChildBehavior.SStore,
                     gas: gas, stor: StoragePattern.ZeroToX));

        // 9. Stipend edge cases × Value
        foreach (var gas   in new[] { GasClass.BelowStipend, GasClass.Stipend, GasClass.AboveStipend })
        foreach (var value in new[] { ValueClass.Zero, ValueClass.One })
            Add(Make(CallKind.Call, TargetKind.ExistingCode, ChildBehavior.SStore,
                     gas: gas, value: value, stor: StoragePattern.ZeroToX));

        // 10. Value transfer × warm/cold × TargetKind
        foreach (var value  in All<ValueClass>())
        foreach (var warm   in new[] { false, true })
        foreach (var target in new[] { TargetKind.ExistingCode, TargetKind.Nonexistent, TargetKind.EmptyAccount })
            Add(Make(CallKind.Call, target, ChildBehavior.Return,
                     value: value, warmTarget: warm));

        return cases;
    }

    private static IEnumerable<T> All<T>() where T : struct, Enum =>
        (T[])Enum.GetValues(typeof(T));
}
