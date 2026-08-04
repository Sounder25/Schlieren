using Scrutor.Core.Execution;

namespace Scrutor.Core.Security;

/// <summary>
/// Type of storage collision detected during DELEGATECALL execution.
/// </summary>
public enum StorageCollisionType
{
    /// <summary>
    /// Slot 0x00 overwritten during DELEGATECALL (legacy proxy pattern).
    /// Common in older proxy implementations where slot 0 holds owner.
    /// </summary>
    LegacySlotZero,
    
    /// <summary>
    /// EIP-1967 implementation slot corruption.
    /// Slot: 0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc
    /// </summary>
    Erc1967Implementation,
    
    /// <summary>
    /// EIP-1967 admin slot corruption.
    /// Slot: 0xb535470464514b7b90209420923d607555bbe57d57f7e2f322fce670654068d3
    /// </summary>
    Erc1967Admin,
    
    /// <summary>
    /// General proxy storage layout overlap.
    /// Implementation contract storage overlaps with proxy reserved slots.
    /// </summary>
    ProxyLayoutOverlap
}

/// <summary>
/// Represents a detected storage collision during DELEGATECALL.
/// </summary>
public sealed class StorageCollisionFinding
{
    public StorageCollisionType CollisionType { get; init; }
    public string ProxyContract { get; init; } = string.Empty;
    public string ImplementationContract { get; init; } = string.Empty;
    public string CollidingSlot { get; init; } = string.Empty;
    public string WrittenValue { get; init; } = string.Empty;
    public int StepIndex { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Detects storage collisions during DELEGATECALL execution.
/// 
/// Storage collisions occur when an implementation contract writes to
/// storage slots that are reserved for the proxy's own state (e.g., owner,
/// implementation address, admin). This can corrupt proxy state and lead
/// to loss of control or unexpected behavior.
/// 
/// Key patterns detected:
/// - Slot 0x00 writes (legacy proxy owner slot)
/// - EIP-1967 implementation slot writes
/// - EIP-1967 admin slot writes
/// - Custom proxy layout overlap
/// </summary>
public static class StorageCollisionDetector
{
    // EIP-1967 Standard Storage Slots
    public static readonly string Erc1967ImplementationSlot = 
        "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
    
    public static readonly string Erc1967AdminSlot = 
        "0xb535470464514b7b90209420923d607555bbe57d57f7e2f322fce670654068d3";

    public static readonly string LegacySlotZero = 
        "0x0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Analyzes an execution trace for storage collision patterns.
    /// </summary>
    /// <param name="trace">The execution trace to analyze</param>
    /// <returns>List of storage collision findings, empty if none detected</returns>
    public static List<StorageCollisionFinding> Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        var findings = new List<StorageCollisionFinding>();

        // Track DELEGATECALL frames and their proxy/impl addresses
        var delegateCallFrames = new Stack<(int startStep, string proxyAddr, string implAddr)>();
        
        // Track storage slots written per frame depth
        var slotWritesByDepth = new Dictionary<int, HashSet<string>>();

        for (int i = 0; i < trace.Count; i++)
        {
            var step = trace[i];
            
            // Track DELEGATECALL frame entry
            if (step.CallType == CallType.DelegateCall && !string.IsNullOrEmpty(step.ContractAddress))
            {
                // The proxy is the caller's contract address (where storage lives)
                // The implementation is what we're delegating to
                var proxyAddr = GetProxyAddress(trace, i);
                var implAddr = step.ContractAddress;
                
                delegateCallFrames.Push((i, proxyAddr ?? step.ContractAddress, implAddr));
            }
            
            // Exit frame when depth decreases (we've returned from a DELEGATECALL)
            while (delegateCallFrames.Count > 0)
            {
                var frameStartStep = delegateCallFrames.Peek().startStep;
                var frameDepth = GetFrameDepth(trace, frameStartStep);
                
                // If current depth is less than frame depth, we've exited that frame
                if (step.Depth < frameDepth)
                {
                    delegateCallFrames.Pop();
                }
                else
                {
                    break;
                }
            }
            
            // Check for SSTORE operations within DELEGATECALL frames
            if (string.Equals(step.Op, "SSTORE", StringComparison.OrdinalIgnoreCase) && 
                step.Storage.Count > 0 &&
                delegateCallFrames.Count > 0)
            {
                var frame = delegateCallFrames.Peek();
                
                foreach (var (slot, value) in step.Storage)
                {
                    if (IsReservedProxySlot(slot, out var collisionType))
                    {
                        findings.Add(new StorageCollisionFinding
                        {
                            CollisionType = collisionType,
                            ProxyContract = frame.proxyAddr,
                            ImplementationContract = frame.implAddr,
                            CollidingSlot = slot,
                            WrittenValue = value,
                            StepIndex = i,
                            Description = $"DELEGATECALL to {frame.implAddr} mutated proxy reserved storage slot {slot} at step {i}."
                        });
                    }
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Checks if a storage slot is a reserved proxy slot.
    /// </summary>
    private static bool IsReservedProxySlot(string slot, out StorageCollisionType collisionType)
    {
        collisionType = StorageCollisionType.ProxyLayoutOverlap;
        
        if (string.IsNullOrEmpty(slot))
            return false;
        
        // Normalize slot to lowercase for comparison
        var normalizedSlot = slot.ToLowerInvariant();
        
        // Check legacy slot 0x00
        if (normalizedSlot == LegacySlotZero.ToLowerInvariant())
        {
            collisionType = StorageCollisionType.LegacySlotZero;
            return true;
        }
        
        // Check EIP-1967 implementation slot
        if (normalizedSlot == Erc1967ImplementationSlot.ToLowerInvariant())
        {
            collisionType = StorageCollisionType.Erc1967Implementation;
            return true;
        }
        
        // Check EIP-1967 admin slot
        if (normalizedSlot == Erc1967AdminSlot.ToLowerInvariant())
        {
            collisionType = StorageCollisionType.Erc1967Admin;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets the proxy address (caller's contract) for a DELEGATECALL.
    /// </summary>
    private static string? GetProxyAddress(IReadOnlyList<ExecutionTraceStep> trace, int delegateCallStep)
    {
        // Walk backwards to find the step before DELEGATECALL to get the proxy address
        for (int i = delegateCallStep - 1; i >= 0; i--)
        {
            var step = trace[i];
            if (step.Depth < trace[delegateCallStep].Depth)
            {
                return step.ContractAddress;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Gets the frame depth at a given step index.
    /// </summary>
    private static int GetFrameDepth(IReadOnlyList<ExecutionTraceStep> trace, int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < trace.Count)
            return trace[stepIndex].Depth;
        return 0;
    }
}
