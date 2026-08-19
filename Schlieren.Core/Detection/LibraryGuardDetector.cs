using Schlieren.Core.Execution;

namespace Schlieren.Core.Detection;

/// <summary>
/// Detects Solidity library runtime protection patterns (CALL context guard).
/// Libraries embed their deployed address and reject non-DELEGATECALL execution.
///
/// This is an execution diagnostic, NOT a security finding. The revert is
/// expected behavior for any library invoked via CALL rather than DELEGATECALL.
/// </summary>
public static class LibraryGuardDetector
{
    /// <summary>
    /// Result of a library guard analysis. Null if the pattern was not detected.
    /// </summary>
    public sealed record LibraryGuardResult(
        int BranchStep,
        int TotalSteps,
        string? EmbeddedConstant,
        string Explanation);

    /// <summary>
    /// Analyzes an execution trace for the Solidity library context guard pattern.
    /// Returns null if the trace does not match the pattern.
    /// </summary>
    public static LibraryGuardResult? Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        if (trace.Count < 10) return null;

        // Library guard fingerprint:
        // 1. Early 32-byte constant (embedded address) within first ~5 steps
        // 2. Comparison (EQ) shortly after
        // 3. Branch (JUMPI) before reaching function dispatcher
        // 4. No storage access
        // 5. No external calls
        // 6. Execution terminates early (< 50 steps typically)

        var hasPush32Early = false;
        string? embeddedConstant = null;
        var hasEarlyComparison = false;
        var hasEarlyBranch = false;
        int branchStep = -1;

        // Check first 15 steps for pattern
        for (int i = 0; i < Math.Min(15, trace.Count); i++)
        {
            var step = trace[i];

            if (step.Op == "PUSH32" && i < 5)
            {
                hasPush32Early = true;
                // Stack on this step is pre-execution. The PUSH32 result appears
                // as stack[0] on the next step.
                if (i + 1 < trace.Count && trace[i + 1].Stack is { Count: > 0 } nextStack)
                {
                    embeddedConstant = nextStack[0];
                }
            }

            if ((step.Op == "EQ" || step.Op == "XOR") && i < 10)
            {
                hasEarlyComparison = true;
            }

            if (step.Op == "JUMPI" && i < 15)
            {
                hasEarlyBranch = true;
                branchStep = i;
            }
        }

        // Check for absence of storage/external calls (library guard should fail before dispatch)
        var hasStorageAccess = trace.Any(s => s.Op == "SLOAD" || s.Op == "SSTORE");
        var hasExternalCalls = trace.Any(s =>
            s.Op == "CALL" ||
            s.Op == "DELEGATECALL" ||
            s.Op == "STATICCALL" ||
            s.Op == "CREATE" ||
            s.Op == "CREATE2");

        var earlyTermination = trace.Count < 50;

        if (hasPush32Early && hasEarlyComparison && hasEarlyBranch &&
            !hasStorageAccess && !hasExternalCalls && earlyTermination)
        {
            return new LibraryGuardResult(
                BranchStep: branchStep,
                TotalSteps: trace.Count,
                EmbeddedConstant: embeddedConstant,
                Explanation: "Solidity library protection detected. Runtime rejected CALL execution context. " +
                             "Bytecode represents a Solidity library executed via CALL instead of expected DELEGATECALL context.");
        }

        return null;
    }
}
