using System.Collections.Generic;
using System.Linq;
using Schlieren.Core.Execution;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Services;

/// <summary>
/// Detects Solidity library runtime protection patterns (CALL context guard).
/// Libraries embed their deployed address and reject non-DELEGATECALL execution.
/// </summary>
public static class LibraryGuardDetector
{
    public static DiagnosticFinding? Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        if (trace.Count < 10) return null; // Too short for library guard pattern
        
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
        
        // Early termination (< 50 steps) and no meaningful work
        var earlyTermination = trace.Count < 50;
        
        // If we have the pattern, it's likely a library guard
        if (hasPush32Early && hasEarlyComparison && hasEarlyBranch && 
            !hasStorageAccess && !hasExternalCalls && earlyTermination)
        {
            var evidence = new List<string>
            {
                $"Early 32-byte constant at step {FindPush32Step(trace)}" +
                    (embeddedConstant != null ? $" ({embeddedConstant})" : ""),
                $"Comparison at step {FindComparisonStep(trace)}",
                $"Branch at step {branchStep}",
                $"Execution terminated after {trace.Count} steps",
                "No storage access (SLOAD/SSTORE)",
                "No external calls",
                "No function dispatch reached"
            };
            
            return new DiagnosticFinding(
                Category: "Compiler Guard",
                Severity: DiagnosticSeverity.Info,
                Title: "Library runtime context guard triggered",
                Summary: "Solidity library protection detected. Runtime rejected CALL execution context.",
                Detail: string.Join(" · ", evidence),
                LikelyCause: "Bytecode represents a Solidity library executed via CALL instead of expected DELEGATECALL context.",
                IsExpectedBehavior: true,
                Confidence: DiagnosticConfidence.High,
                StepIndex: branchStep);
        }
        
        return null;
    }
    
    private static int FindPush32Step(IReadOnlyList<ExecutionTraceStep> trace)
    {
        for (int i = 0; i < Math.Min(5, trace.Count); i++)
        {
            if (trace[i].Op == "PUSH32") return i;
        }
        return -1;
    }
    
    private static int FindComparisonStep(IReadOnlyList<ExecutionTraceStep> trace)
    {
        for (int i = 0; i < Math.Min(10, trace.Count); i++)
        {
            if (trace[i].Op == "EQ" || trace[i].Op == "XOR") return i;
        }
        return -1;
    }
}
