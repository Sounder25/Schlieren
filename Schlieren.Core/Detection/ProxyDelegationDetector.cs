using Schlieren.Core.Execution;

namespace Schlieren.Core.Detection;

/// <summary>
/// Detects EIP-1967 proxy delegation to address(0) due to missing implementation state.
/// Pattern: SLOAD from canonical implementation slot returns 0 → DELEGATECALL(0) → no child frame.
///
/// This is an execution diagnostic, NOT a security finding. The proxy pattern is
/// correct; the execution context simply doesn't contain the deployed storage state.
/// </summary>
public static class ProxyDelegationDetector
{
    // EIP-1967 implementation slot: bytes32(uint256(keccak256('eip1967.proxy.implementation')) - 1)
    public const string Eip1967ImplementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";

    // EIP-1967 beacon slot: bytes32(uint256(keccak256('eip1967.proxy.beacon')) - 1)
    public const string Eip1967BeaconSlot = "0xa3f0ad74e5423aebfd80d3ef4346578335a9a72aeaee59ff6cb3582b35133d50";

    // EIP-1967 admin slot: bytes32(uint256(keccak256('eip1967.proxy.admin')) - 1)
    public const string Eip1967AdminSlot = "0xb53127684a568b3173ae13b9f8a6016e243e63b6e8ee1178d6a717850b5d6103";

    /// <summary>
    /// Result of proxy delegation analysis. Null if the pattern was not detected.
    /// </summary>
    public sealed record ProxyDelegationResult(
        int DelegatecallStep,
        string SlotUsed,
        string SlotName,
        string Explanation);

    /// <summary>
    /// Analyzes an execution trace for EIP-1967 proxy delegation to a zero implementation.
    /// Returns null if the pattern was not detected.
    /// </summary>
    public static ProxyDelegationResult? Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        if (trace.Count < 10) return null;

        for (int i = 0; i < trace.Count; i++)
        {
            var step = trace[i];
            if (step.Op != "DELEGATECALL") continue;

            // DELEGATECALL stack layout (pre-execution):
            // [gas, target, argsOffset, argsSize, retOffset, retSize]
            if (step.Stack == null || step.Stack.Count < 6) continue;

            var target = step.Stack[1];
            if (!IsZeroAddress(target)) continue;

            // Verify no depth increase (no child frame created)
            var currentDepth = step.Depth;
            var nextStep = i + 1 < trace.Count ? trace[i + 1] : null;
            if (nextStep != null && nextStep.Depth > currentDepth)
                continue;

            // Look backward for SLOAD from EIP-1967 slots
            var implementationSlotRead = FindRecentSlotRead(trace, i, Eip1967ImplementationSlot);
            var beaconSlotRead = FindRecentSlotRead(trace, i, Eip1967BeaconSlot);

            if (implementationSlotRead || beaconSlotRead)
            {
                var slotUsed = implementationSlotRead ? Eip1967ImplementationSlot : Eip1967BeaconSlot;
                var slotName = implementationSlotRead ? "implementation" : "beacon";

                return new ProxyDelegationResult(
                    DelegatecallStep: i,
                    SlotUsed: slotUsed,
                    SlotName: slotName,
                    Explanation: $"EIP-1967 {slotName} storage resolved to address(0). " +
                                 "No implementation bytecode executed. " +
                                 "Execution context does not contain the proxy's deployed storage state.");
            }
        }

        return null;
    }

    /// <summary>
    /// Detects whether the trace shows a successful proxy delegation (non-zero implementation).
    /// Returns the implementation address if found.
    /// </summary>
    public static string? DetectResolvedImplementation(IReadOnlyList<ExecutionTraceStep> trace)
    {
        for (int i = 0; i < trace.Count; i++)
        {
            var step = trace[i];
            if (step.Op != "DELEGATECALL") continue;
            if (step.Stack == null || step.Stack.Count < 6) continue;

            var target = step.Stack[1];
            if (IsZeroAddress(target)) continue;

            // Check if preceded by EIP-1967 slot read
            if (FindRecentSlotRead(trace, i, Eip1967ImplementationSlot) ||
                FindRecentSlotRead(trace, i, Eip1967BeaconSlot))
            {
                return NormalizeHex(target);
            }
        }

        return null;
    }

    private static bool IsZeroAddress(string stackValue)
    {
        if (string.IsNullOrEmpty(stackValue)) return false;
        var hex = stackValue.StartsWith("0x") ? stackValue[2..] : stackValue;
        return hex.All(c => c == '0');
    }

    private static bool FindRecentSlotRead(IReadOnlyList<ExecutionTraceStep> trace, int beforeStep, string targetSlot)
    {
        var start = Math.Max(0, beforeStep - 50);

        for (int i = beforeStep - 1; i >= start; i--)
        {
            var step = trace[i];
            if (step.Op != "SLOAD") continue;
            if (step.Stack == null || step.Stack.Count < 1) continue;

            var key = step.Stack[0];
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
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return clean.ToLowerInvariant().PadLeft(64, '0');
    }
}
