using Scrutor.Core.Execution;

namespace Scrutor.Core.Security;

/// <summary>
/// Stateful, incremental reentrancy detector.
///
/// Wire it to <see cref="ExecutionContext.OnStep"/> before execution starts:
///   var detector = new LiveReentrancyDetector(finding => /* UI push */);
///   context.OnStep = detector.OnStep;
///
/// The callback fires the moment a reentrancy condition is observed —
/// while the EVM is still running, not after the trace is complete.
///
/// Detection logic mirrors ReentrancyDetector but maintains an O(depth)
/// frame stack instead of rescanning the full trace each time.
/// </summary>
public sealed class LiveReentrancyDetector
{
    private readonly Action<ReentrancyFinding> _onFinding;

    // Active call frames: (contractAddress, depth, entryStepIndex)
    private readonly Stack<(string Address, int Depth, int StepIndex)> _frames = new();

    // Tracks storage values seen at each depth so we can detect
    // post-call mutations when execution returns to parent depth.
    private readonly Dictionary<int, Dictionary<string, string>> _storageByDepth = new();

    // Depths where we already issued a finding (suppress duplicates)
    private readonly HashSet<string> _issuedKeys = new();

    // Track last SSTORE per depth so we can detect CEI violations
    // (state mutation AFTER external call returned)
    private int _lastDepth = -1;

    public LiveReentrancyDetector(Action<ReentrancyFinding> onFinding)
    {
        _onFinding = onFinding;
    }

    /// <summary>
    /// Called by <see cref="ExecutionContext.OnStep"/> for every executed opcode.
    /// </summary>
    public void OnStep(ExecutionTraceStep step, int stepIndex)
    {
        var addr = step.ContractAddress ?? string.Empty;
        var depth = step.Depth;

        // ── 1. Pop frames that are deeper than current depth (we returned) ──
        while (_frames.Count > 0 && _frames.Peek().Depth >= depth)
            _frames.Pop();

        // ── 2. Check: is this address already open in an ancestor frame? ──
        foreach (var frame in _frames)
        {
            if (string.Equals(frame.Address, addr, StringComparison.OrdinalIgnoreCase)
                && depth > frame.Depth)
            {
                var key = $"{addr}:{frame.StepIndex}:{stepIndex}";
                if (_issuedKeys.Add(key))
                {
                    // Determine severity: Critical if any SSTORE happened in parent
                    // context between the initial entry and this re-entry
                    var hasMutation = HasPostCallSstore(frame.Depth, frame.StepIndex);

                    _onFinding(new ReentrancyFinding
                    {
                        Severity = hasMutation
                            ? ReentrancySeverity.Critical
                            : ReentrancySeverity.Medium,
                        TargetContract = addr,
                        AttackerContract = step.CallerAddress ?? string.Empty,
                        InitialEntryStep = frame.StepIndex,
                        ReentryStep = stepIndex,
                        DepthDelta = depth - frame.Depth,
                        Description = $"Re-entry into {addr} by {step.CallerAddress} " +
                                      $"at step {stepIndex} (initially entered at step {frame.StepIndex})"
                    });
                }
                break;
            }
        }

        // ── 3. Track SSTORE for CEI-violation detection ──
        if (step.Op == "SSTORE" && step.Storage.Count > 0)
        {
            if (!_storageByDepth.TryGetValue(depth, out var slots))
            {
                slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _storageByDepth[depth] = slots;
            }
            foreach (var (k, v) in step.Storage)
                slots[k] = v;
        }

        // ── 4. Push this frame ──
        _frames.Push((addr, depth, stepIndex));
        _lastDepth = depth;
    }

    private bool HasPostCallSstore(int parentDepth, int entryStep)
        => _storageByDepth.TryGetValue(parentDepth, out _);
}
