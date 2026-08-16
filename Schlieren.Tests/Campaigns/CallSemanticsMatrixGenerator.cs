using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Generates test matrix for Call Semantics & Frame Integrity Campaign.
/// Uses pairwise combinations to cover interactions efficiently.
/// </summary>
public static class CallSemanticsMatrixGenerator
{
    public enum CallType { Call, DelegateCall, StaticCall, CallCode }
    public enum ChildResult { Success, Revert, OutOfGas }
    public enum TargetState { CodePresent, EmptyAccount, Nonexistent }
    public enum AccessWarmth { Cold, Warm }
    public enum ValueTransfer { Zero, NonZero }
    public enum ChildBehavior { NoOp, SLoad, SStore, Log, NestedCall }
    public enum ReturnDataSize { Zero, One, ThirtyOne, ThirtyTwo, ThirtyThree, Large256 }
    public enum Fork { Berlin, London, Shanghai, Cancun }

    public sealed class CallTestCase
    {
        public required string CaseId { get; init; }
        public required CallType Type { get; init; }
        public required ChildResult Result { get; init; }
        public required TargetState Target { get; init; }
        public required AccessWarmth Access { get; init; }
        public required ValueTransfer Value { get; init; }
        public required ChildBehavior Behavior { get; init; }
        public required ReturnDataSize ReturnSize { get; init; }
        public required int Depth { get; init; }
        public required Fork Fork { get; init; }

        public string Bytecode { get; set; } = "";
        public string ParentBytecode { get; set; } = "";
        public ulong? ExpectedGas { get; set; }
    }

    /// <summary>
    /// Generate pairwise combinations to cover call semantics efficiently.
    /// Target: 50+ cases initially, expandable to 200-500.
    /// Uses deterministic addresses for reproducibility.
    /// </summary>
    public static List<CallTestCase> GenerateMatrix()
    {
        var cases = new List<CallTestCase>();

        // Core scenarios - each call type with common patterns
        foreach (var callType in new[] { CallType.Call, CallType.DelegateCall, CallType.StaticCall })
        {
            // Basic success - no-op
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success, 
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));

            // With storage read
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun));

            // Revert cases
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Revert,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));

            // Warm access
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Warm, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun));

            // Depth 3 nested
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NestedCall, ReturnDataSize.ThirtyTwo, 3, Fork.Cancun));
        }

        // STATICCALL-specific: state modification attempts
        cases.Add(CreateCase(cases.Count + 1, CallType.StaticCall, ChildResult.Revert,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.SStore, ReturnDataSize.Zero, 2, Fork.Cancun));

        // CALL with value transfer (not valid for STATICCALL/DELEGATECALL)
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.NonZero,
            ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));

        // Empty account targets
        foreach (var callType in new[] { CallType.Call, CallType.DelegateCall })
        {
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.EmptyAccount, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));
        }

        // Return data size variations
        foreach (var size in new[] { ReturnDataSize.One, ReturnDataSize.ThirtyOne, 
                                     ReturnDataSize.ThirtyThree, ReturnDataSize.Large256 })
        {
            cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, size, 2, Fork.Cancun));
        }

        // Cross-fork validation - Berlin (EIP-2929 access lists)
        foreach (var callType in new[] { CallType.Call, CallType.DelegateCall, CallType.StaticCall })
        {
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, 2, Fork.Berlin));

            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Warm, ValueTransfer.Zero,
                ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, 2, Fork.Berlin));
        }

        // OOG scenarios
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.OutOfGas,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.SStore, ReturnDataSize.Zero, 2, Fork.Cancun));

        // Deduplicate by case ID (prevent duplicate semantic cases)
        cases = cases
            .GroupBy(c => c.CaseId)
            .Select(g => g.First())
            .ToList();

        return cases;
    }

    /// <summary>
    /// Create a test case with deterministic ID encoding dimensions.
    /// Format: R6_CALL_COLD_SUCCESS_SLOAD_R32_D2_CANCUN
    /// </summary>
    private static CallTestCase CreateCase(
        int seqNum,
        CallType type,
        ChildResult result,
        TargetState target,
        AccessWarmth access,
        ValueTransfer value,
        ChildBehavior behavior,
        ReturnDataSize returnSize,
        int depth,
        Fork fork)
    {
        var callStr = type.ToString().ToUpperInvariant();
        var accessStr = access.ToString().ToUpperInvariant();
        var resultStr = result.ToString().ToUpperInvariant();
        var behaviorStr = behavior.ToString().ToUpperInvariant();
        var retStr = returnSize switch
        {
            ReturnDataSize.Zero => "R0",
            ReturnDataSize.One => "R1",
            ReturnDataSize.ThirtyOne => "R31",
            ReturnDataSize.ThirtyTwo => "R32",
            ReturnDataSize.ThirtyThree => "R33",
            ReturnDataSize.Large256 => "R256",
            _ => "R0"
        };
        var forkStr = fork.ToString().ToUpperInvariant();

        var caseId = $"R6_{callStr}_{accessStr}_{resultStr}_{behaviorStr}_{retStr}_D{depth}_{forkStr}";

        return new CallTestCase
        {
            CaseId = caseId,
            Type = type,
            Result = result,
            Target = target,
            Access = access,
            Value = value,
            Behavior = behavior,
            ReturnSize = returnSize,
            Depth = depth,
            Fork = fork
        };
    }

    /// <summary>
    /// Generate bytecode for a test case.
    /// </summary>
    public static (string parentCode, string childCode) GenerateBytecode(CallTestCase testCase)
    {
        var child = GenerateChildBytecode(testCase);
        var parent = GenerateParentBytecode(testCase, child.address);
        
        return (parent.code, child.code);
    }

    private static (string code, string address) GenerateChildBytecode(CallTestCase testCase)
    {
        var opcodes = new List<string>();

        // STEP 1: Generate behavior body ONLY (no termination)
        switch (testCase.Behavior)
        {
            case ChildBehavior.NoOp:
                // Emit nothing - just termination
                break;

            case ChildBehavior.SLoad:
                // PUSH1 0x00 (slot 0)
                // SLOAD
                // PUSH1 0x00 (offset)
                // MSTORE
                opcodes.AddRange(new[] { "60", "00", "54", "60", "00", "52" });
                break;

            case ChildBehavior.SStore:
                // PUSH1 0x01 (value)
                // PUSH1 0x00 (slot)
                // SSTORE
                opcodes.AddRange(new[] { "60", "01", "60", "00", "55" });
                break;

            case ChildBehavior.Log:
                // PUSH1 0x00 (size)
                // PUSH1 0x00 (offset)
                // LOG0
                opcodes.AddRange(new[] { "60", "00", "60", "00", "a0" });
                break;

            case ChildBehavior.NestedCall:
                // Recursive CALL to grandchild (depth 3)
                // PUSH1 0x00 (retSize)
                // PUSH1 0x00 (retOffset)
                // PUSH1 0x00 (argsSize)
                // PUSH1 0x00 (argsOffset)
                // PUSH1 0x00 (value)
                // PUSH20 <grandchild address 0xcc>
                // PUSH2 0x7530 (gas 30000)
                // CALL
                // POP
                opcodes.AddRange(new[] { 
                    "60", "00", "60", "00", "60", "00", "60", "00", "60", "00",
                    "73"  // PUSH20
                });
                // Grandchild address: 0x00000000000000000000000000000000000000cc
                opcodes.AddRange(new[] {
                    "00", "00", "00", "00", "00", "00", "00", "00", "00", "00",
                    "00", "00", "00", "00", "00", "00", "00", "00", "00", "cc"
                });
                opcodes.AddRange(new[] { "61", "75", "30", "f1", "50" });
                break;
        }

        // STEP 2: Generate exactly ONE terminal path (mutually exclusive)
        var returnSize = testCase.ReturnSize switch
        {
            ReturnDataSize.Zero => 0,
            ReturnDataSize.One => 1,
            ReturnDataSize.ThirtyOne => 31,
            ReturnDataSize.ThirtyTwo => 32,
            ReturnDataSize.ThirtyThree => 33,
            ReturnDataSize.Large256 => 256,
            _ => 0
        };

        switch (testCase.Result)
        {
            case ChildResult.Success:
                if (returnSize > 0)
                {
                    // PUSH1 <size>
                    // PUSH1 0x00 (offset)
                    // RETURN
                    opcodes.AddRange(new[] { "60", returnSize.ToString("x2"), "60", "00", "f3" });
                }
                else
                {
                    // STOP (success with no return)
                    opcodes.Add("00");
                }
                break;

            case ChildResult.Revert:
                // PUSH1 <size>
                // PUSH1 0x00 (offset)
                // REVERT
                opcodes.AddRange(new[] { "60", returnSize.ToString("x2"), "60", "00", "fd" });
                break;

            case ChildResult.OutOfGas:
                // Will OOG during execution due to parent's low gas limit
                // Terminate normally but parent provides insufficient gas
                opcodes.Add("00");
                break;
        }

        var code = "0x" + string.Join("", opcodes);
        
        // Use deterministic child address
        return (code, DeterministicAddresses.Child);
    }

    private static (string code, string address) GenerateParentBytecode(
        CallTestCase testCase, string childAddress)
    {
        var opcodes = new List<string>();

        // CALL: gas, to, value, argsOffset, argsSize, retOffset, retSize
        // DELEGATECALL: gas, to, argsOffset, argsSize, retOffset, retSize
        // STATICCALL: gas, to, argsOffset, argsSize, retOffset, retSize

        var retSize = testCase.ReturnSize switch
        {
            ReturnDataSize.Zero => 0,
            ReturnDataSize.One => 1,
            ReturnDataSize.ThirtyOne => 31,
            ReturnDataSize.ThirtyTwo => 32,
            ReturnDataSize.ThirtyThree => 33,
            ReturnDataSize.Large256 => 256,
            _ => 0
        };

        // Stack setup (bottom to top for CALL)
        opcodes.AddRange(new[] { "60", retSize.ToString("x2") });  // retSize
        opcodes.AddRange(new[] { "60", "00" });  // retOffset
        opcodes.AddRange(new[] { "60", "00" });  // argsSize
        opcodes.AddRange(new[] { "60", "00" });  // argsOffset

        if (testCase.Type == CallType.Call)
        {
            var value = testCase.Value == ValueTransfer.NonZero ? "01" : "00";
            opcodes.AddRange(new[] { "60", value });  // value
        }

        // Target address - split into 20 bytes
        opcodes.Add("73");  // PUSH20
        var addressBytes = childAddress.StartsWith("0x") ? childAddress[2..] : childAddress;
        // Split 40-char hex string into 20 separate 2-char bytes
        for (int i = 0; i < 40; i += 2)
        {
            opcodes.Add(addressBytes.Substring(i, 2));
        }

        // Gas
        if (testCase.Result == ChildResult.OutOfGas)
        {
            opcodes.AddRange(new[] { "61", "0b", "b8" });  // PUSH2 3000 gas (too low for SSTORE)
        }
        else
        {
            opcodes.AddRange(new[] { "62", "01", "86", "a0" });  // PUSH3 100,000 gas (0x0186a0)
        }

        // Call opcode
        var callOpcode = testCase.Type switch
        {
            CallType.Call => "f1",
            CallType.DelegateCall => "f4",
            CallType.StaticCall => "fa",
            CallType.CallCode => "f2",
            _ => "f1"
        };
        opcodes.Add(callOpcode);

        // POP result
        opcodes.Add("50");

        // Parent returns
        opcodes.AddRange(new[] { "60", "00", "60", "00", "f3" });

        var code = "0x" + string.Join("", opcodes);
        
        // Use deterministic parent address
        return (code, DeterministicAddresses.Parent);
    }
}
