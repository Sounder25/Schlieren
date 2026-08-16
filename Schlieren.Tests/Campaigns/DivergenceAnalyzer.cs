using System;
using System.Collections.Generic;
using System.Linq;
using Schlieren.Core.Execution;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Normalized execution result for differential comparison.
/// Enables hierarchical divergence analysis.
/// </summary>
public sealed class ExecutionFingerprint
{
    public required bool Success { get; init; }
    public required ulong GasUsed { get; init; }
    public required string ReturnData { get; init; }
    public required ulong Refund { get; init; }
    public required List<FrameFingerprint> FrameTree { get; init; }
    public required AccessFingerprint Accesses { get; init; }
    public required Dictionary<string, string> StateDiff { get; init; }
    public required List<LogFingerprint> Logs { get; init; }
}

public sealed class FrameFingerprint
{
    public required int Depth { get; init; }
    public required string CallType { get; init; }
    public required string CodeAddress { get; init; }
    public required string ContextAddress { get; init; }
    public required string Caller { get; init; }
    public required string Value { get; init; }
    public required ulong GasProvided { get; init; }
    public required ulong GasConsumed { get; init; }
    public required bool Success { get; init; }
    public required string ReturnData { get; init; }
}

public sealed class AccessFingerprint
{
    public required List<string> ColdAccounts { get; init; }
    public required List<string> WarmAccounts { get; init; }
    public required List<string> ColdSlots { get; init; }
    public required List<string> WarmSlots { get; init; }
}

public sealed class LogFingerprint
{
    public required string Address { get; init; }
    public required List<string> Topics { get; init; }
    public required string Data { get; init; }
}

/// <summary>
/// Compares two execution fingerprints and localizes first divergence.
/// </summary>
public static class DivergenceAnalyzer
{
    public enum DivergenceCategory
    {
        None,
        OutcomeMismatch,        // Success vs revert
        GasMismatch,            // Different gas total
        ReturnDataMismatch,     // Different return bytes
        FrameTreeMismatch,      // Different call structure
        AccessMismatch,         // Warm/cold accounting differs
        StateMismatch,          // Storage writes differ
        LogMismatch,            // Event logs differ
        RefundMismatch          // Refund accounting differs
    }

    public sealed class Divergence
    {
        public required DivergenceCategory Category { get; init; }
        public required string Message { get; init; }
        public required string? FirstMismatch { get; init; }
        public required long? Delta { get; init; }
        public required string? LikelySubsystem { get; init; }
        public required string? Recommendation { get; init; }
    }

    public static Divergence Compare(ExecutionFingerprint expected, ExecutionFingerprint actual)
    {
        // 1. Outcome check
        if (expected.Success != actual.Success)
        {
            return new Divergence
            {
                Category = DivergenceCategory.OutcomeMismatch,
                Message = $"Expected: {(expected.Success ? "SUCCESS" : "REVERT")}, " +
                         $"Actual: {(actual.Success ? "SUCCESS" : "REVERT")}",
                FirstMismatch = "Transaction outcome",
                Delta = null,
                LikelySubsystem = "Execution semantics / exceptional halt conditions",
                Recommendation = "Check EVM execution path and revert conditions"
            };
        }

        // 2. Gas check
        if (expected.GasUsed != actual.GasUsed)
        {
            var delta = (long)actual.GasUsed - (long)expected.GasUsed;
            var firstFrame = FindFirstGasDivergence(expected.FrameTree, actual.FrameTree);
            
            return new Divergence
            {
                Category = DivergenceCategory.GasMismatch,
                Message = $"Expected gas: {expected.GasUsed:N0}, Actual: {actual.GasUsed:N0}, Delta: {delta:+#,0;-#,0}",
                FirstMismatch = firstFrame,
                Delta = delta,
                LikelySubsystem = CategorizGasDivergence(delta, firstFrame),
                Recommendation = RecommendGasFix(delta, firstFrame)
            };
        }

        // 3. Return data check
        if (expected.ReturnData != actual.ReturnData)
        {
            return new Divergence
            {
                Category = DivergenceCategory.ReturnDataMismatch,
                Message = $"Return data mismatch",
                FirstMismatch = $"Expected: {expected.ReturnData[..Math.Min(66, expected.ReturnData.Length)]}, " +
                              $"Actual: {actual.ReturnData[..Math.Min(66, actual.ReturnData.Length)]}",
                Delta = actual.ReturnData.Length - expected.ReturnData.Length,
                LikelySubsystem = "RETURN / REVERT / RETURNDATACOPY handling",
                Recommendation = "Check returndata buffer and RETURNDATACOPY bounds"
            };
        }

        // 4. Frame tree check
        var frameDivergence = CompareFrameTree(expected.FrameTree, actual.FrameTree);
        if (frameDivergence != null)
            return frameDivergence;

        // 5. Access set check
        var accessDivergence = CompareAccesses(expected.Accesses, actual.Accesses);
        if (accessDivergence != null)
            return accessDivergence;

        // 6. State diff check
        if (!DictionariesEqual(expected.StateDiff, actual.StateDiff))
        {
            var firstDiff = expected.StateDiff.FirstOrDefault(kvp => 
                !actual.StateDiff.TryGetValue(kvp.Key, out var val) || val != kvp.Value);
            
            return new Divergence
            {
                Category = DivergenceCategory.StateMismatch,
                Message = "Storage state divergence",
                FirstMismatch = $"Slot {firstDiff.Key}: expected {firstDiff.Value}, actual {actual.StateDiff.GetValueOrDefault(firstDiff.Key, "missing")}",
                Delta = null,
                LikelySubsystem = "State journaling / SSTORE / revert handling",
                Recommendation = "Check storage write tracking and revert state rollback"
            };
        }

        // 7. Refund check
        if (expected.Refund != actual.Refund)
        {
            return new Divergence
            {
                Category = DivergenceCategory.RefundMismatch,
                Message = $"Refund mismatch: expected {expected.Refund}, actual {actual.Refund}",
                FirstMismatch = null,
                Delta = (long)actual.Refund - (long)expected.Refund,
                LikelySubsystem = "Refund accounting / SSTORE gas refund",
                Recommendation = "Check SSTORE refund rules and EIP-3529 (London) refund cap"
            };
        }

        // Perfect match
        return new Divergence
        {
            Category = DivergenceCategory.None,
            Message = "Perfect match",
            FirstMismatch = null,
            Delta = null,
            LikelySubsystem = null,
            Recommendation = null
        };
    }

    private static string? FindFirstGasDivergence(List<FrameFingerprint> expected, List<FrameFingerprint> actual)
    {
        for (int i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            if (expected[i].GasConsumed != actual[i].GasConsumed)
            {
                var frame = expected[i];
                return $"Depth {frame.Depth} / {frame.CallType} / Target {frame.CodeAddress}";
            }
        }
        
        return "Unknown frame";
    }

    private static string CategorizGasDivergence(long delta, string? firstFrame)
    {
        // Common gas deltas reveal likely subsystem
        return Math.Abs(delta) switch
        {
            2600 => "Access list (cold account charge)",
            2500 => "Access list (cold account charge, pre-Berlin)",
            2100 => "Cold storage slot (SLOAD/SSTORE)",
            100 => "Warm account access",
            _ when Math.Abs(delta) < 500 => "Opcode gas schedule / memory expansion",
            _ when Math.Abs(delta) > 10000 => "Frame gas attribution / nested gas double-counting",
            _ => "Gas accounting / metering"
        };
    }

    private static string RecommendGasFix(long delta, string? firstFrame)
    {
        return Math.Abs(delta) switch
        {
            2600 or 2500 => "Check AccessSet.IsWarm() for target account before CALL opcode",
            2100 => "Check warm/cold slot tracking in AccessSet",
            100 => "Verify warm access list propagation across frames",
            _ when Math.Abs(delta) > 10000 => "Check parent CALL gasCost attribution — likely includes child execution. Sum depth-1 gas only.",
            _ => "Review opcode gas schedule and memory expansion formula"
        };
    }

    private static Divergence? CompareFrameTree(List<FrameFingerprint> expected, List<FrameFingerprint> actual)
    {
        if (expected.Count != actual.Count)
        {
            return new Divergence
            {
                Category = DivergenceCategory.FrameTreeMismatch,
                Message = $"Frame count mismatch: expected {expected.Count}, actual {actual.Count}",
                FirstMismatch = "Frame tree structure",
                Delta = actual.Count - expected.Count,
                LikelySubsystem = "Call depth / frame stack integrity",
                Recommendation = "Check CALL-family opcode execution and frame creation"
            };
        }

        for (int i = 0; i < expected.Count; i++)
        {
            var exp = expected[i];
            var act = actual[i];

            if (exp.Depth != act.Depth)
            {
                return new Divergence
                {
                    Category = DivergenceCategory.FrameTreeMismatch,
                    Message = $"Frame {i} depth mismatch: expected {exp.Depth}, actual {act.Depth}",
                    FirstMismatch = $"Frame {i} depth",
                    Delta = act.Depth - exp.Depth,
                    LikelySubsystem = "Frame depth tracking",
                    Recommendation = "Check depth increment/decrement on CALL entry/exit"
                };
            }

            if (exp.CallType != act.CallType)
            {
                return new Divergence
                {
                    Category = DivergenceCategory.FrameTreeMismatch,
                    Message = $"Frame {i} type mismatch: expected {exp.CallType}, actual {act.CallType}",
                    FirstMismatch = $"Frame {i} call type",
                    Delta = null,
                    LikelySubsystem = "Call semantics classification",
                    Recommendation = "Check CallType assignment in frame creation"
                };
            }

            if (exp.CodeAddress != act.CodeAddress || exp.ContextAddress != act.ContextAddress)
            {
                return new Divergence
                {
                    Category = DivergenceCategory.FrameTreeMismatch,
                    Message = $"Frame {i} address mismatch",
                    FirstMismatch = $"Frame {i}: CodeAddress={act.CodeAddress}, ContextAddress={act.ContextAddress}",
                    Delta = null,
                    LikelySubsystem = "DELEGATECALL / CALLCODE context preservation",
                    Recommendation = "Check CodeAddress vs ContextAddress semantics for call type"
                };
            }
        }

        return null;
    }

    private static Divergence? CompareAccesses(AccessFingerprint expected, AccessFingerprint actual)
    {
        if (!ListsEqual(expected.ColdAccounts, actual.ColdAccounts) ||
            !ListsEqual(expected.WarmAccounts, actual.WarmAccounts))
        {
            return new Divergence
            {
                Category = DivergenceCategory.AccessMismatch,
                Message = "Account access warmness mismatch",
                FirstMismatch = "Account access set",
                Delta = null,
                LikelySubsystem = "AccessSet / EIP-2929 warm/cold tracking",
                Recommendation = "Check AccessSet.MarkWarm() and IsWarm() for accounts"
            };
        }

        if (!ListsEqual(expected.ColdSlots, actual.ColdSlots) ||
            !ListsEqual(expected.WarmSlots, actual.WarmSlots))
        {
            return new Divergence
            {
                Category = DivergenceCategory.AccessMismatch,
                Message = "Storage slot access warmness mismatch",
                FirstMismatch = "Storage slot access set",
                Delta = null,
                LikelySubsystem = "AccessSet / storage slot warm/cold tracking",
                Recommendation = "Check AccessSet storage slot tracking and propagation"
            };
        }

        return null;
    }

    private static bool ListsEqual<T>(List<T> a, List<T> b)
    {
        if (a.Count != b.Count) return false;
        var setA = new HashSet<T>(a);
        var setB = new HashSet<T>(b);
        return setA.SetEquals(setB);
    }

    private static bool DictionariesEqual<TKey, TValue>(
        Dictionary<TKey, TValue> a, Dictionary<TKey, TValue> b) where TKey : notnull
    {
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var value) || !Equals(value, kvp.Value))
                return false;
        }
        return true;
    }
}
