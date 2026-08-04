using Scrutor.Core.Execution;

namespace Scrutor.Core.Security;

/// <summary>
/// Severity level for reentrancy findings.
/// </summary>
public enum ReentrancySeverity
{
    /// <summary>
    /// Read-only reentrancy (STATICCALL or no state modification).
    /// </summary>
    Info,
    
    /// <summary>
    /// Frame re-entered without state lock (potential vulnerability).
    /// </summary>
    Medium,
    
    /// <summary>
    /// State mutated AFTER sub-call returned (State-Check-Interaction violation).
    /// </summary>
    Critical
}

/// <summary>
/// Represents a detected reentrancy vulnerability.
/// </summary>
public sealed class ReentrancyFinding
{
    public ReentrancySeverity Severity { get; init; }
    public string TargetContract { get; init; } = string.Empty;
    public string AttackerContract { get; init; } = string.Empty;
    public int InitialEntryStep { get; init; }
    public int ReentryStep { get; init; }
    public int DepthDelta { get; init; }
    public List<string> MutatedStorageSlots { get; init; } = new();
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Analyzes execution traces for reentrancy vulnerabilities.
/// 
/// Reentrancy occurs when a contract calls external code that calls back
/// into the original contract before state is updated (State-Check-Interaction violation).
/// </summary>
public static class ReentrancyDetector
{
    /// <summary>
    /// Analyzes an execution trace for reentrancy patterns.
    /// </summary>
    /// <param name="trace">The execution trace to analyze</param>
    /// <returns>List of reentrancy findings, empty if none detected</returns>
    public static List<ReentrancyFinding> Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        var findings = new List<ReentrancyFinding>();
        var activeFrames = new Stack<(string address, int depth, int stepIndex)>();

        for (int i = 0; i < trace.Count; i++)
        {
            var step = trace[i];
            var currentAddr = step.ContractAddress;

            // Check if current contract address is already present in an upper active call frame
            foreach (var frame in activeFrames)
            {
                if (string.Equals(frame.address, currentAddr, StringComparison.OrdinalIgnoreCase) && 
                    step.Depth > frame.depth)
                {
                    // Check for post-call storage mutations (State-Check-Interaction rule)
                    var postMutations = FindPostCallStorageMutations(trace, i, frame.depth);
                    
                    findings.Add(new ReentrancyFinding
                    {
                        Severity = postMutations.Count > 0 ? ReentrancySeverity.Critical : ReentrancySeverity.Medium,
                        TargetContract = currentAddr ?? string.Empty,
                        AttackerContract = step.CallerAddress ?? string.Empty,
                        InitialEntryStep = frame.stepIndex,
                        ReentryStep = i,
                        DepthDelta = step.Depth - frame.depth,
                        MutatedStorageSlots = postMutations,
                        Description = $"Contract {currentAddr} re-entered by {step.CallerAddress} at step {i} (initial entry at step {frame.stepIndex})."
                    });
                    break;
                }
            }

            // Frame stack maintenance
            while (activeFrames.Count > 0 && activeFrames.Peek().depth >= step.Depth)
            {
                activeFrames.Pop();
            }
            activeFrames.Push((currentAddr ?? string.Empty, step.Depth, i));
        }

        return findings;
    }

    /// <summary>
    /// Finds storage slots that were mutated after a reentrant call returned.
    /// This indicates a State-Check-Interaction violation.
    /// </summary>
    private static List<string> FindPostCallStorageMutations(
        IReadOnlyList<ExecutionTraceStep> trace, 
        int reentryStep, 
        int parentDepth)
    {
        var mutatedSlots = new HashSet<string>();
        var slotsBeforeCall = new Dictionary<string, string>();
        
        // Find the slot values before the reentry
        for (int i = reentryStep - 1; i >= 0; i--)
        {
            var step = trace[i];
            if (step.Depth != parentDepth)
                continue;
                
            foreach (var (slot, value) in step.Storage)
            {
                slotsBeforeCall[slot] = value;
            }
            break;
        }
        
        // Scan for mutations after returning from reentry
        for (int i = reentryStep; i < trace.Count; i++)
        {
            var step = trace[i];
            
            // Left the parent frame depth
            if (step.Depth < parentDepth)
                break;
                
            // Still in sub-call
            if (step.Depth > parentDepth)
                continue;
                
            // Back at parent depth - check for SSTORE
            if (step.Op == "SSTORE" && step.Storage.Count > 0)
            {
                foreach (var (slot, value) in step.Storage)
                {
                    // Slot was modified after the call returned
                    if (!slotsBeforeCall.TryGetValue(slot, out var oldValue) || oldValue != value)
                    {
                        mutatedSlots.Add(slot);
                    }
                }
            }
        }
        
        return mutatedSlots.ToList();
    }
}
