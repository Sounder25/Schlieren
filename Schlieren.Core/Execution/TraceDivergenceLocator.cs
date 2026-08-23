namespace Schlieren.Core.Execution;

/// <summary>
/// Step-level EIP-3155 structLog comparison.
/// Given two traces (Schlieren vs reference), finds the exact step index
/// where execution diverges and classifies the divergence.
///
/// This is the C# equivalent of tools/eels_trace_compare.py.
/// </summary>
public static class TraceDivergenceLocator
{
    /// <summary>
    /// The result of comparing two execution traces step by step.
    /// <see cref="GasDelta"/> is extra-cost polarity (actualCost − expectedCost, or
    /// expectedRemaining − actualRemaining): positive means Schlieren overcharged.
    /// </summary>
    public sealed record TraceDivergence(
        bool IsMatch,
        int? DivergentStepIndex,
        string? DivergentOpcode,
        string? ExpectedOpcode,
        int? DivergentPc,
        int? DivergentDepth,
        long? GasDelta,
        string Category,
        string LikelySubsystem,
        string Detail);

    public static readonly TraceDivergence Match = new(
        IsMatch: true,
        DivergentStepIndex: null,
        DivergentOpcode: null,
        ExpectedOpcode: null,
        DivergentPc: null,
        DivergentDepth: null,
        GasDelta: null,
        Category: "Match",
        LikelySubsystem: "",
        Detail: "Traces are identical.");

    /// <summary>
    /// Compare two traces step-by-step. The first trace is treated as
    /// the "actual" (Schlieren) trace; the second is the "expected" (reference).
    /// </summary>
    public static TraceDivergence Compare(
        IReadOnlyList<ExecutionTraceStep> actual,
        IReadOnlyList<ExecutionTraceStep> expected)
    {
        int minLen = Math.Min(actual.Count, expected.Count);

        for (int i = 0; i < minLen; i++)
        {
            var a = actual[i];
            var e = expected[i];

            // 1. PC divergence — fundamental control flow difference
            if (a.Pc != e.Pc)
            {
                return BuildDivergence(i, a, e, "PcMismatch",
                    $"PC diverged at step {i}: actual={a.Pc} expected={e.Pc}",
                    ClassifyPcDivergence(a, e));
            }

            // 2. Opcode divergence — different instruction at same PC
            if (!string.Equals(a.Op, e.Op, StringComparison.OrdinalIgnoreCase))
            {
                return BuildDivergence(i, a, e, "OpcodeMismatch",
                    $"Opcode diverged at step {i}: actual={a.Op} expected={e.Op} (PC={a.Pc})",
                    ClassifyOpcodeDivergence(a.Op, e.Op));
            }

            // 3. Depth divergence — call tree structure differs
            if (a.Depth != e.Depth)
            {
                return BuildDivergence(i, a, e, "DepthMismatch",
                    $"Depth diverged at step {i}: actual={a.Depth} expected={e.Depth} (opcode={a.Op}, PC={a.Pc})",
                    "Call frame management (CALL/DELEGATECALL/RETURN depth tracking)");
            }

            // 4. Gas remaining — EIP-3155 `gas` is remaining gas before the opcode.
            // Store extra-cost polarity so GasDelta matches GasCostMismatch:
            // extraCost = expectedRemaining − actualRemaining.
            var actualGas = ParseGas(a.Gas);
            var expectedGas = ParseGas(e.Gas);
            if (actualGas.HasValue && expectedGas.HasValue && actualGas.Value != expectedGas.Value)
            {
                long extraCost = expectedGas.Value - actualGas.Value;
                return new TraceDivergence(
                    IsMatch: false,
                    DivergentStepIndex: i,
                    DivergentOpcode: a.Op,
                    ExpectedOpcode: e.Op,
                    DivergentPc: a.Pc,
                    DivergentDepth: a.Depth,
                    GasDelta: extraCost,
                    Category: "GasMismatch",
                    LikelySubsystem: ClassifyGasDivergence(a.Op, extraCost),
                    Detail: $"Gas diverged at step {i}: remaining actual={actualGas.Value} expected={expectedGas.Value} " +
                            $"extraCost={extraCost:+#;-#;0} (opcode={a.Op}, PC={a.Pc}, depth={a.Depth})");
            }

            // 5. GasCost divergence — the opcode was priced differently
            var actualCost = ParseGas(a.GasCost);
            var expectedCost = ParseGas(e.GasCost);
            if (actualCost.HasValue && expectedCost.HasValue && actualCost.Value != expectedCost.Value)
            {
                long costDelta = actualCost.Value - expectedCost.Value;
                return new TraceDivergence(
                    IsMatch: false,
                    DivergentStepIndex: i,
                    DivergentOpcode: a.Op,
                    ExpectedOpcode: e.Op,
                    DivergentPc: a.Pc,
                    DivergentDepth: a.Depth,
                    GasDelta: costDelta,
                    Category: "GasCostMismatch",
                    LikelySubsystem: ClassifyGasDivergence(a.Op, costDelta),
                    Detail: $"GasCost diverged at step {i}: actual={actualCost.Value} expected={expectedCost.Value} " +
                            $"delta={costDelta:+#;-#;0} (opcode={a.Op}, PC={a.Pc}, depth={a.Depth})");
            }
        }

        // Length divergence — one trace is longer
        if (actual.Count != expected.Count)
        {
            var shorter = actual.Count < expected.Count ? "actual" : "expected";
            int divergeAt = minLen;
            var longerTrace = actual.Count > expected.Count ? actual : expected;
            var nextOp = divergeAt < longerTrace.Count ? longerTrace[divergeAt].Op : "?";

            return new TraceDivergence(
                IsMatch: false,
                DivergentStepIndex: divergeAt,
                DivergentOpcode: divergeAt < actual.Count ? actual[divergeAt].Op : null,
                ExpectedOpcode: divergeAt < expected.Count ? expected[divergeAt].Op : null,
                DivergentPc: divergeAt < longerTrace.Count ? longerTrace[divergeAt].Pc : null,
                DivergentDepth: divergeAt < longerTrace.Count ? longerTrace[divergeAt].Depth : null,
                GasDelta: null,
                Category: "TraceLengthMismatch",
                LikelySubsystem: $"Execution terminated differently ({shorter} trace ended at step {minLen}, next={nextOp})",
                Detail: $"Traces diverge at step {divergeAt}: actual has {actual.Count} steps, expected has {expected.Count}");
        }

        return Match;
    }

    private static TraceDivergence BuildDivergence(
        int step, ExecutionTraceStep actual, ExecutionTraceStep expected,
        string category, string detail, string subsystem)
    {
        var actualGas = ParseGas(actual.Gas);
        var expectedGas = ParseGas(expected.Gas);
        long? delta = actualGas.HasValue && expectedGas.HasValue
            ? expectedGas.Value - actualGas.Value
            : null;

        return new TraceDivergence(
            IsMatch: false,
            DivergentStepIndex: step,
            DivergentOpcode: actual.Op,
            ExpectedOpcode: expected.Op,
            DivergentPc: actual.Pc,
            DivergentDepth: actual.Depth,
            GasDelta: delta,
            Category: category,
            LikelySubsystem: subsystem,
            Detail: detail);
    }

    private static string ClassifyPcDivergence(ExecutionTraceStep actual, ExecutionTraceStep expected)
    {
        // If one is a JUMP/JUMPI and the other isn't, it's a branch decision difference
        bool aIsJump = actual.Op is "JUMP" or "JUMPI" or "JUMPDEST";
        bool eIsJump = expected.Op is "JUMP" or "JUMPI" or "JUMPDEST";
        if (aIsJump || eIsJump)
            return "Branch decision (JUMPI condition evaluated differently)";

        return "Control flow divergence (different code path reached)";
    }

    private static string ClassifyOpcodeDivergence(string actualOp, string expectedOp)
    {
        // STOP vs REVERT = execution outcome divergence
        if ((actualOp == "STOP" && expectedOp == "REVERT") ||
            (actualOp == "REVERT" && expectedOp == "STOP"))
            return "Execution outcome (success vs revert)";

        // INVALID → opcode activation
        if (actualOp == "INVALID" || expectedOp == "INVALID")
            return "Opcode activation / invalid opcode";

        return $"Different opcode at same PC (actual={actualOp}, expected={expectedOp})";
    }

    private static string ClassifyGasDivergence(string opcode, long delta)
    {
        var absDelta = Math.Abs(delta);

        return absDelta switch
        {
            2600 => "EIP-2929 cold account access (COLD_ACCOUNT_ACCESS_COST = 2600)",
            2100 => "EIP-2929 cold storage read (COLD_SLOAD_COST = 2100)",
            100 => "EIP-2929 warm access (WARM_STORAGE_READ_COST = 100)",
            2500 => "EIP-2929 cold-warm difference (2600 - 100 = 2500)",
            2000 => "EIP-2929 cold SLOAD difference (2100 - 100 = 2000)",
            9000 => "Pre-EIP-150 call value transfer charge",
            2300 => "EIP-2200 SSTORE stipend guard",
            25000 => "SELFDESTRUCT new account cost",
            32000 => "CREATE base cost / tx create surcharge",
            24000 => "Pre-London SELFDESTRUCT refund",
            21000 => "Transaction base cost",
            _ => ClassifyGasByOpcode(opcode, absDelta)
        };
    }

    private static string ClassifyGasByOpcode(string opcode, long absDelta)
    {
        return opcode switch
        {
            "SLOAD" => "Storage read pricing (cold/warm SLOAD)",
            "SSTORE" => "Storage write pricing (EIP-2200/EIP-3529)",
            "CALL" or "STATICCALL" or "DELEGATECALL" or "CALLCODE" =>
                "Call opcode gas (access cost + value transfer + stipend)",
            "BALANCE" or "EXTCODESIZE" or "EXTCODECOPY" or "EXTCODEHASH" =>
                "Account access opcode pricing (EIP-2929)",
            "SELFDESTRUCT" => "SELFDESTRUCT gas schedule",
            "CREATE" or "CREATE2" => "CREATE gas schedule (base + initcode + deployment)",
            "EXP" => "EXP byte cost (fork-dependent: 10 vs 50)",
            "LOG0" or "LOG1" or "LOG2" or "LOG3" or "LOG4" => "LOG opcode gas",
            _ => $"Gas pricing for {opcode} (delta={absDelta})"
        };
    }

    private static long? ParseGas(string gasHex)
    {
        if (string.IsNullOrEmpty(gasHex)) return null;
        var clean = gasHex.StartsWith("0x") ? gasHex[2..] : gasHex;
        return long.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var val)
            ? val
            : null;
    }
}
