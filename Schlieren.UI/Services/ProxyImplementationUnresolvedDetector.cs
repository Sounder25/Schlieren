using System.Collections.Generic;
using System.Linq;
using Schlieren.Core.Execution;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Services;

/// <summary>
/// Detects EIP-1967 proxy delegation to address(0) due to missing implementation state.
/// Pattern: SLOAD from canonical implementation slot returns 0 → DELEGATECALL(0) → no child frame.
/// </summary>
public static class ProxyImplementationUnresolvedDetector
{
    // EIP-1967 implementation slot: bytes32(uint256(keccak256('eip1967.proxy.implementation')) - 1)
    private const string Eip1967ImplementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
    
    // EIP-1967 beacon slot: bytes32(uint256(keccak256('eip1967.proxy.beacon')) - 1)
    private const string Eip1967BeaconSlot = "0xa3f0ad74e5423aebfd80d3ef4346578335a9a72aeaee59ff6cb3582b35133d50";

    public static DiagnosticFinding? Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        if (trace.Count < 10) return null; // Too short for proxy pattern

        // Find DELEGATECALL operations
        for (int i = 0; i < trace.Count; i++)
        {
            var step = trace[i];
            if (step.Op != "DELEGATECALL") continue;

            // DELEGATECALL stack layout (pre-execution):
            // [gas, target, argsOffset, argsSize, retOffset, retSize]
            // Target is at stack[1]
            if (step.Stack == null || step.Stack.Count < 6) continue;

            var target = step.Stack[1];
            
            // Check if target is zero
            if (!IsZeroAddress(target)) continue;

            // Verify no depth increase (no child frame created)
            var currentDepth = step.Depth;
            var nextStep = i + 1 < trace.Count ? trace[i + 1] : null;
            if (nextStep != null && nextStep.Depth > currentDepth)
                continue; // Child frame was created, not our pattern

            // Look backward for SLOAD from EIP-1967 slots
            var implementationSlotRead = FindRecentSlotRead(trace, i, Eip1967ImplementationSlot);
            var beaconSlotRead = FindRecentSlotRead(trace, i, Eip1967BeaconSlot);

            if (implementationSlotRead || beaconSlotRead)
            {
                var slotUsed = implementationSlotRead ? Eip1967ImplementationSlot : Eip1967BeaconSlot;
                var slotName = implementationSlotRead ? "implementation" : "beacon";

                var detail = $"EIP-1967 {slotName} slot: {slotUsed}\n" +
                            $"Value: 0x0000000000000000000000000000000000000000\n\n" +
                            $"DELEGATECALL targeted address(0) at step {i}.\n" +
                            "No child execution frame created.\n" +
                            "Proxy returned with empty execution.";

                return new DiagnosticFinding(
                    Category: "Proxy Delegation",
                    Severity: DiagnosticSeverity.Info,
                    Title: "Implementation unresolved",
                    Summary: $"EIP-1967 {slotName} storage resolved to address(0). No implementation bytecode executed.",
                    Detail: detail,
                    LikelyCause: "Execution context does not contain the proxy's deployed storage state.",
                    IsExpectedBehavior: true,
                    Confidence: DiagnosticConfidence.High,
                    StepIndex: i);
            }
        }

        return null;
    }

    private static bool IsZeroAddress(string stackValue)
    {
        // Stack values are hex strings like "0x0" or "0x0000..."
        if (string.IsNullOrEmpty(stackValue)) return false;
        
        var hex = stackValue.StartsWith("0x") ? stackValue.Substring(2) : stackValue;
        return hex.All(c => c == '0');
    }

    private static bool FindRecentSlotRead(IReadOnlyList<ExecutionTraceStep> trace, int beforeStep, string targetSlot)
    {
        // Look back up to 50 steps for SLOAD from the target slot
        var start = Math.Max(0, beforeStep - 50);
        
        for (int i = beforeStep - 1; i >= start; i--)
        {
            var step = trace[i];
            if (step.Op != "SLOAD") continue;

            // SLOAD stack layout (pre-execution): [key]
            if (step.Stack == null || step.Stack.Count < 1) continue;

            var key = step.Stack[0];
            
            // Normalize both for comparison
            var normalizedKey = NormalizeHex(key);
            var normalizedTarget = NormalizeHex(targetSlot);
            
            if (normalizedKey.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "";
        
        // Remove 0x prefix, convert to lowercase, pad to 64 chars
        var clean = hex.StartsWith("0x") ? hex.Substring(2) : hex;
        return clean.ToLowerInvariant().PadLeft(64, '0');
    }
}
