using Schlieren.Core.Execution;

namespace Schlieren.Core.Security;

/// <summary>
/// Stateful, incremental storage-collision detector.
///
/// Wire it to <see cref="ExecutionContext.OnStep"/> before execution starts:
///   var detector = new LiveStorageCollisionDetector(finding => /* UI push */);
///   context.OnStep += detector.OnStep;   // chain with LiveReentrancyDetector
///
/// Fires the instant a SSTORE inside a DELEGATECALL frame touches a
/// reserved proxy slot — not after the trace is complete.
/// </summary>
public sealed class LiveStorageCollisionDetector
{
    private readonly Action<StorageCollisionFinding> _onFinding;

    // Stack of active DELEGATECALL frames: (proxyAddr, implAddr, entryDepth)
    private readonly Stack<(string ProxyAddr, string ImplAddr, int EntryDepth)> _delegateFrames = new();

    // Suppress duplicate findings for the same (slot, stepIndex)
    private readonly HashSet<string> _issuedKeys = new();

    // EIP-1967 slots (lowercase)
    private static readonly string _implSlot =
        "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
    private static readonly string _adminSlot =
        "0xb535470464514b7b90209420923d607555bbe57d57f7e2f322fce670654068d3";
    private static readonly string _slotZero =
        "0x0000000000000000000000000000000000000000000000000000000000000000";

    public LiveStorageCollisionDetector(Action<StorageCollisionFinding> onFinding)
    {
        _onFinding = onFinding;
    }

    public void OnStep(ExecutionTraceStep step, int stepIndex)
    {
        var depth = step.Depth;

        // ── 1. Pop delegate frames that have been exited ──
        while (_delegateFrames.Count > 0 && _delegateFrames.Peek().EntryDepth >= depth)
            _delegateFrames.Pop();

        // ── 2. Track new DELEGATECALL frame entries ──
        if (step.CallType == CallType.DelegateCall && !string.IsNullOrEmpty(step.ContractAddress))
        {
            // proxy = who initiated the delegatecall (CallerAddress)
            // impl  = the code being delegated to (ContractAddress)
            var proxy = step.CallerAddress ?? step.ContractAddress;
            var impl  = step.ContractAddress;
            _delegateFrames.Push((proxy, impl, depth));
        }

        // ── 3. Detect SSTORE inside a DELEGATECALL touching reserved slots ──
        if (step.Op == "SSTORE" && step.Storage.Count > 0 && _delegateFrames.Count > 0)
        {
            var frame = _delegateFrames.Peek();
            foreach (var (slot, value) in step.Storage)
            {
                if (!TryClassify(slot, out var collisionType))
                    continue;

                var key = $"{slot}:{stepIndex}";
                if (!_issuedKeys.Add(key))
                    continue;

                _onFinding(new StorageCollisionFinding
                {
                    CollisionType = collisionType,
                    ProxyContract = frame.ProxyAddr,
                    ImplementationContract = frame.ImplAddr,
                    CollidingSlot = slot,
                    WrittenValue = value,
                    StepIndex = stepIndex,
                    Description = $"DELEGATECALL from {frame.ProxyAddr} to {frame.ImplAddr} " +
                                  $"wrote reserved proxy slot {slot} at step {stepIndex}"
                });
            }
        }
    }

    private static bool TryClassify(string slot, out StorageCollisionType type)
    {
        var norm = slot.ToLowerInvariant();
        if (norm == _slotZero)   { type = StorageCollisionType.LegacySlotZero;       return true; }
        if (norm == _implSlot)   { type = StorageCollisionType.Erc1967Implementation; return true; }
        if (norm == _adminSlot)  { type = StorageCollisionType.Erc1967Admin;          return true; }
        type = StorageCollisionType.ProxyLayoutOverlap;
        return false;
    }
}
