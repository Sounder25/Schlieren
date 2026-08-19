using Schlieren.Core.Execution;

namespace Schlieren.Core.Detection;

/// <summary>
/// Detects common Solidity compiler patterns in execution traces.
/// These patterns are expected behavior, not vulnerabilities.
///
/// Identified patterns:
/// - OpenZeppelin ReentrancyGuard (SLOAD→check→SSTORE mutex)
/// - ABI function selector dispatch (CALLDATALOAD→AND→EQ→JUMPI)
/// - Solidity receive()/fallback() routing
/// - Multicall batch pattern (sequential CALLs with loop)
/// - Checked arithmetic overflow (revert with Panic(0x11))
/// </summary>
public static class CompilerPatternDetector
{
    /// <summary>
    /// A detected compiler/library pattern in the execution trace.
    /// </summary>
    public sealed record CompilerPattern(
        string PatternId,
        string Title,
        string Explanation,
        int FirstStep,
        int? LastStep,
        bool IsExpectedBehavior);

    /// <summary>
    /// Analyze the trace for all known compiler patterns.
    /// Returns zero or more detected patterns.
    /// </summary>
    public static IReadOnlyList<CompilerPattern> Analyze(IReadOnlyList<ExecutionTraceStep> trace)
    {
        var results = new List<CompilerPattern>();

        var reentrancyGuard = DetectReentrancyGuard(trace);
        if (reentrancyGuard != null) results.Add(reentrancyGuard);

        var selectorDispatch = DetectSelectorDispatch(trace);
        if (selectorDispatch != null) results.Add(selectorDispatch);

        var receiveFallback = DetectReceiveFallback(trace);
        if (receiveFallback != null) results.Add(receiveFallback);

        var checkedArith = DetectCheckedArithmeticRevert(trace);
        if (checkedArith != null) results.Add(checkedArith);

        var multicall = DetectMulticallBatch(trace);
        if (multicall != null) results.Add(multicall);

        return results;
    }

    /// <summary>
    /// OpenZeppelin ReentrancyGuard pattern:
    /// SLOAD(slot) → check value != _ENTERED → SSTORE(slot, _ENTERED) → ... → SSTORE(slot, _NOT_ENTERED)
    /// The mutex slot is typically 0x0 and alternates between 1 (NOT_ENTERED) and 2 (ENTERED).
    /// </summary>
    private static CompilerPattern? DetectReentrancyGuard(IReadOnlyList<ExecutionTraceStep> trace)
    {
        // Look for SLOAD→{few ops}→SSTORE writing 2, then later SSTORE writing 1 to the same slot
        for (int i = 0; i < trace.Count - 10; i++)
        {
            if (trace[i].Op != "SLOAD") continue;

            // Find SSTORE within 15 steps writing a small value (2 = _ENTERED)
            for (int j = i + 1; j < Math.Min(i + 15, trace.Count); j++)
            {
                if (trace[j].Op != "SSTORE") continue;
                if (trace[j].Stack == null || trace[j].Stack.Count < 2) continue;

                var slot = trace[j].Stack[0];
                var value = trace[j].Stack[1];

                // Value should be 2 (_ENTERED) for the lock acquisition
                if (!IsSmallValue(value, 2)) continue;

                // Look for the unlock: SSTORE to same slot with value 1
                for (int k = j + 1; k < trace.Count; k++)
                {
                    if (trace[k].Op != "SSTORE") continue;
                    if (trace[k].Stack == null || trace[k].Stack.Count < 2) continue;

                    var unlockSlot = trace[k].Stack[0];
                    var unlockValue = trace[k].Stack[1];

                    if (NormalizeHex(slot) == NormalizeHex(unlockSlot) && IsSmallValue(unlockValue, 1))
                    {
                        return new CompilerPattern(
                            PatternId: "OZ_REENTRANCY_GUARD",
                            Title: "OpenZeppelin ReentrancyGuard mutex",
                            Explanation: $"Reentrancy lock acquired at step {j} (slot {slot} = 2), " +
                                         $"released at step {k} (slot {slot} = 1). " +
                                         "This is expected OpenZeppelin nonReentrant modifier behavior.",
                            FirstStep: i,
                            LastStep: k,
                            IsExpectedBehavior: true);
                    }
                }

                break; // Only check first SSTORE candidate per SLOAD
            }
        }

        return null;
    }

    /// <summary>
    /// ABI function selector dispatch:
    /// CALLDATALOAD(0) → SHR/AND(0xffffffff) → DUP→PUSH4→EQ→JUMPI, repeated for each function.
    /// </summary>
    private static CompilerPattern? DetectSelectorDispatch(IReadOnlyList<ExecutionTraceStep> trace)
    {
        if (trace.Count < 5) return null;

        int selectorChecks = 0;
        int firstCheck = -1;
        int lastCheck = -1;

        for (int i = 0; i < Math.Min(100, trace.Count) - 3; i++)
        {
            // Look for the EQ→JUMPI selector comparison pattern
            if (trace[i].Op == "EQ" && i + 1 < trace.Count && trace[i + 1].Op == "JUMPI")
            {
                // Verify preceded by PUSH4 (selector constant) within 3 steps
                var hasPush4 = false;
                for (int back = Math.Max(0, i - 3); back < i; back++)
                {
                    if (trace[back].Op == "PUSH4")
                    {
                        hasPush4 = true;
                        break;
                    }
                }

                if (hasPush4)
                {
                    selectorChecks++;
                    if (firstCheck == -1) firstCheck = i;
                    lastCheck = i;
                }
            }
        }

        if (selectorChecks >= 2)
        {
            return new CompilerPattern(
                PatternId: "SOLIDITY_SELECTOR_DISPATCH",
                Title: "Solidity ABI function selector dispatch",
                Explanation: $"Detected {selectorChecks} function selector comparisons " +
                             $"(steps {firstCheck}–{lastCheck}). Standard Solidity ABI routing.",
                FirstStep: firstCheck,
                LastStep: lastCheck,
                IsExpectedBehavior: true);
        }

        return null;
    }

    /// <summary>
    /// Solidity receive()/fallback() routing:
    /// CALLDATASIZE → ISZERO → JUMPI (if calldata is empty, jump to receive(); else fallback())
    /// </summary>
    private static CompilerPattern? DetectReceiveFallback(IReadOnlyList<ExecutionTraceStep> trace)
    {
        for (int i = 0; i < Math.Min(10, trace.Count) - 2; i++)
        {
            if (trace[i].Op == "CALLDATASIZE" &&
                i + 1 < trace.Count && trace[i + 1].Op == "ISZERO" &&
                i + 2 < trace.Count && trace[i + 2].Op == "JUMPI")
            {
                return new CompilerPattern(
                    PatternId: "SOLIDITY_RECEIVE_FALLBACK",
                    Title: "Solidity receive()/fallback() routing",
                    Explanation: "CALLDATASIZE→ISZERO→JUMPI: standard Solidity pattern for " +
                                 "routing empty calldata to receive() or non-matching selectors to fallback().",
                    FirstStep: i,
                    LastStep: i + 2,
                    IsExpectedBehavior: true);
            }
        }

        return null;
    }

    /// <summary>
    /// Checked arithmetic overflow revert:
    /// Solidity 0.8+ emits Panic(0x11) for overflow/underflow.
    /// Pattern: REVERT with returndata = 0x4e487b71{...00000011}
    ///
    /// REVERT never populates <see cref="ExecutionTraceStep.OutputData"/> (that field
    /// is only filled for call-like ops). The payload is reconstructed from the
    /// pre-execution stack (offset, size) and the memory snapshot.
    /// </summary>
    private static CompilerPattern? DetectCheckedArithmeticRevert(IReadOnlyList<ExecutionTraceStep> trace)
    {
        for (int i = 0; i < trace.Count; i++)
        {
            if (trace[i].Op != "REVERT") continue;

            var data = GetRevertPayload(trace[i]);
            if (data is not { Length: >= 36 }) continue;

            // Panic selector: 0x4e487b71
            if (data[0] != 0x4e || data[1] != 0x48 || data[2] != 0x7b || data[3] != 0x71)
                continue;

            var panicCode = data[35]; // Last byte of the uint256 argument
            string panicName = panicCode switch
            {
                0x01 => "assert failure",
                0x11 => "arithmetic overflow/underflow",
                0x12 => "division by zero",
                0x21 => "invalid enum value",
                0x22 => "invalid storage encoding",
                0x31 => "empty array pop",
                0x32 => "array out of bounds",
                0x41 => "too much memory",
                0x51 => "zero-initialized function pointer",
                _ => $"Panic(0x{panicCode:x2})"
            };

            return new CompilerPattern(
                PatternId: "SOLIDITY_PANIC_REVERT",
                Title: $"Solidity checked {panicName}",
                Explanation: $"REVERT with Panic(0x{panicCode:x2}): {panicName}. " +
                             "This is Solidity 0.8+ compiler-generated checked arithmetic, not a vulnerability.",
                FirstStep: i,
                LastStep: i,
                IsExpectedBehavior: true);
        }

        return null;
    }

    /// <summary>
    /// REVERT stack (pre-exec, top-first) is [offset, size]. Memory is 32-byte hex words.
    /// OutputData is accepted if a caller already populated it.
    /// </summary>
    private static byte[]? GetRevertPayload(ExecutionTraceStep step)
    {
        if (step.OutputData is { Length: > 0 })
            return step.OutputData;

        if (step.Stack is not { Count: >= 2 })
            return null;
        if (!TryParseHexInt(step.Stack[0], out var offset) ||
            !TryParseHexInt(step.Stack[1], out var size) ||
            offset < 0 || size <= 0)
            return null;

        return SliceMemoryWords(step.Memory, offset, size);
    }

    private static bool TryParseHexInt(string? hex, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(hex)) return false;
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (clean.Length == 0)
        {
            value = 0;
            return true;
        }
        return int.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private static byte[]? SliceMemoryWords(IReadOnlyList<string>? words, int offset, int size)
    {
        if (words is null || words.Count == 0) return null;

        var mem = new byte[words.Count * 32];
        for (var w = 0; w < words.Count; w++)
        {
            var hex = words[w];
            if (string.IsNullOrEmpty(hex)) continue;
            var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            if (clean.Length > 64) clean = clean[^64..];
            if ((clean.Length & 1) == 1) clean = "0" + clean;
            try
            {
                var bytes = Convert.FromHexString(clean);
                var dest = w * 32 + (32 - bytes.Length);
                if (dest < 0)
                {
                    Array.Copy(bytes, bytes.Length - 32, mem, w * 32, 32);
                }
                else
                {
                    Array.Copy(bytes, 0, mem, dest, bytes.Length);
                }
            }
            catch (FormatException)
            {
                return null;
            }
        }

        if (offset > mem.Length) return null;
        var available = mem.Length - offset;
        var take = Math.Min(size, available);
        if (take <= 0) return null;
        var slice = new byte[take];
        Array.Copy(mem, offset, slice, 0, take);
        return slice;
    }

    /// <summary>
    /// Multicall batch pattern: sequential external CALLs in a loop.
    /// Pattern: CALL→POP→CALL→POP (3+ calls with same context address).
    /// </summary>
    private static CompilerPattern? DetectMulticallBatch(IReadOnlyList<ExecutionTraceStep> trace)
    {
        int callCount = 0;
        int firstCall = -1;
        int lastCall = -1;
        string? firstCaller = null;

        for (int i = 0; i < trace.Count; i++)
        {
            if (trace[i].Op is "CALL" or "STATICCALL")
            {
                var caller = trace[i].ContractAddress;
                if (callCount == 0)
                {
                    firstCaller = caller;
                    firstCall = i;
                }

                // Same context = same batch
                if (caller == firstCaller || firstCaller == null)
                {
                    callCount++;
                    lastCall = i;
                }
            }
        }

        if (callCount >= 3)
        {
            return new CompilerPattern(
                PatternId: "MULTICALL_BATCH",
                Title: "Multicall batch execution",
                Explanation: $"Detected {callCount} sequential external calls from the same contract " +
                             $"(steps {firstCall}–{lastCall}). Likely a Multicall/batch aggregation pattern.",
                FirstStep: firstCall,
                LastStep: lastCall,
                IsExpectedBehavior: true);
        }

        return null;
    }

    private static bool IsSmallValue(string hex, int expected)
    {
        if (string.IsNullOrEmpty(hex)) return false;
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        clean = clean.TrimStart('0');
        return clean == expected.ToString("x") || clean == expected.ToString();
    }

    private static string NormalizeHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "";
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return clean.ToLowerInvariant().PadLeft(64, '0');
    }
}
