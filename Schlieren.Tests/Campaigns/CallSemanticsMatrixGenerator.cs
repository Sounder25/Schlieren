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
    public enum TargetState { CodePresent, EmptyAccount, Nonexistent, Precompile }
    public enum AccessWarmth { Cold, Warm }
    public enum ValueTransfer { Zero, OneWei, BoundaryLow, BoundaryHigh, OneEther }
    public enum ChildBehavior { NoOp, SLoad, SStore, Log, NestedCall, MultipleWrites, SelfDestruct, Create }
    public enum ReturnDataSize { Zero, One, ThirtyOne, ThirtyTwo, ThirtyThree, Large256 }
    public enum Fork { Berlin, London, Shanghai, Cancun }
    public enum PrecompileAddress { 
        Ecrecover = 1, 
        Sha256 = 2, 
        Ripemd160 = 3, 
        Identity = 4, 
        ModExp = 5, 
        EcAdd = 6, 
        EcMul = 7, 
        EcPairing = 8, 
        Blake2f = 9 
    }

    public sealed class CallTestCase
    {
        public required string CaseId { get; init; set; }
        public required CallType Type { get; init; }
        public required ChildResult Result { get; init; }
        public required TargetState Target { get; init; }
        public required AccessWarmth Access { get; init; }
        public required ValueTransfer Value { get; init; }
        public required ChildBehavior Behavior { get; init; }
        public required ReturnDataSize ReturnSize { get; init; }
        public required int Depth { get; init; }
        public required Fork Fork { get; init; }
        
        public PrecompileAddress? PrecompileTarget { get; set; }
        public ulong? GasLimit { get; set; }
        public string Bytecode { get; set; } = "";
        public string ParentBytecode { get; set; } = "";
        public ulong? ExpectedGas { get; set; }
    }

    /// <summary>
    /// Generate pairwise combinations to cover call semantics efficiently.
    /// Expanded matrix: ~200 deterministic cases covering:
    /// - Value transfer variations (1 wei, boundaries, 1 ether)
    /// - Precompile targets (ecrecover through blake2f)
    /// - Depth variations (2, 3, 4, 5)
    /// - Storage patterns (multiple writes, cold→warm transitions)
    /// - Gas boundary conditions
    /// - Cross-fork validation (Berlin, London, Shanghai, Cancun)
    /// Uses deterministic addresses for reproducibility.
    /// </summary>
    public static List<CallTestCase> GenerateMatrix()
    {
        var cases = new List<CallTestCase>();

        // === CORE CALL SEMANTICS (27 baseline cases) ===
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
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.OneWei,
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

        // === VALUE TRANSFER MATRIX (~30 cases) ===
        foreach (var value in new[] { ValueTransfer.OneWei, ValueTransfer.BoundaryLow, ValueTransfer.BoundaryHigh, ValueTransfer.OneEther })
        {
            // CALL with value + success
            cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, value,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));

            // CALL with value + revert (value returned to caller)
            cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Revert,
                TargetState.CodePresent, AccessWarmth.Cold, value,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));

            // Value transfer to empty account (creates account)
            cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                TargetState.EmptyAccount, AccessWarmth.Cold, value,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));

            // Value + storage operation
            cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, value,
                ChildBehavior.SStore, ReturnDataSize.Zero, 2, Fork.Cancun));

            // Value transfer cold vs warm
            cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Warm, value,
                ChildBehavior.NoOp, ReturnDataSize.Zero, 2, Fork.Cancun));
        }

        // === PRECOMPILE MATRIX (~36 cases) ===
        var precompiles = new[] { 
            PrecompileAddress.Ecrecover,
            PrecompileAddress.Sha256,
            PrecompileAddress.Ripemd160,
            PrecompileAddress.Identity,
            PrecompileAddress.ModExp,
            PrecompileAddress.EcAdd,
            PrecompileAddress.EcMul,
            PrecompileAddress.EcPairing,
            PrecompileAddress.Blake2f
        };

        foreach (var precompile in precompiles)
        {
            // CALL to precompile with zero value
            var preCase = CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                TargetState.Precompile, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun);
            preCase.PrecompileTarget = precompile;
            UpdateCaseId(preCase);
            cases.Add(preCase);

            // DELEGATECALL to precompile (exotic but valid)
            preCase = CreateCase(cases.Count + 1, CallType.DelegateCall, ChildResult.Success,
                TargetState.Precompile, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun);
            preCase.PrecompileTarget = precompile;
            UpdateCaseId(preCase);
            cases.Add(preCase);

            // STATICCALL to precompile
            preCase = CreateCase(cases.Count + 1, CallType.StaticCall, ChildResult.Success,
                TargetState.Precompile, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.NoOp, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun);
            preCase.PrecompileTarget = precompile;
            UpdateCaseId(preCase);
            cases.Add(preCase);

            // Precompile with value transfer (only Identity/ModExp accept value)
            if (precompile == PrecompileAddress.Identity || precompile == PrecompileAddress.ModExp)
            {
                preCase = CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
                    TargetState.Precompile, AccessWarmth.Cold, ValueTransfer.OneEther,
                    ChildBehavior.NoOp, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun);
                preCase.PrecompileTarget = precompile;
                UpdateCaseId(preCase);
                cases.Add(preCase);
            }
        }

        // === DEPTH MATRIX (~30 cases) ===
        foreach (var depth in new[] { 3, 4, 5 })
        {
            foreach (var callType in new[] { CallType.Call, CallType.DelegateCall, CallType.StaticCall })
            {
                // Success at depth N
                cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                    TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                    ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, depth, Fork.Cancun));

                // Revert at depth N
                cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Revert,
                    TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                    ChildBehavior.NoOp, ReturnDataSize.Zero, depth, Fork.Cancun));

                // With value at depth N (CALL only)
                if (callType == CallType.Call)
                {
                    cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                        TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.OneEther,
                        ChildBehavior.NoOp, ReturnDataSize.Zero, depth, Fork.Cancun));
                }
            }
        }

        // === STORAGE PATTERN MATRIX (~20 cases) ===
        foreach (var callType in new[] { CallType.Call, CallType.DelegateCall })
        {
            // Multiple SSTOREs (cold → warm transitions)
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.MultipleWrites, ReturnDataSize.Zero, 2, Fork.Cancun));

            // Multiple SSTOREs at depth 3
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.MultipleWrites, ReturnDataSize.Zero, 3, Fork.Cancun));

            // Multiple writes with value transfer
            if (callType == CallType.Call)
            {
                cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                    TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.OneWei,
                    ChildBehavior.MultipleWrites, ReturnDataSize.Zero, 2, Fork.Cancun));
            }

            // Multiple writes then revert (gas refund behavior)
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Revert,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.MultipleWrites, ReturnDataSize.Zero, 2, Fork.Cancun));

            // Cross-fork storage (Berlin vs Cancun gas deltas)
            cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.MultipleWrites, ReturnDataSize.Zero, 2, Fork.Berlin));
        }

        // === GAS BOUNDARY MATRIX (~15 cases) ===
        foreach (var callType in new[] { CallType.Call, CallType.DelegateCall, CallType.StaticCall })
        {
            // Exact gas for operation (boundary condition)
            var gasCase = CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun);
            gasCase.GasLimit = 25000; // Just enough for SLOAD + overhead
            UpdateCaseId(gasCase);
            cases.Add(gasCase);

            // Insufficient gas (OOG during execution)
            gasCase = CreateCase(cases.Count + 1, callType, ChildResult.OutOfGas,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.SStore, ReturnDataSize.Zero, 2, Fork.Cancun);
            gasCase.GasLimit = 3000; // Not enough for SSTORE
            UpdateCaseId(gasCase);
            cases.Add(gasCase);

            // High gas limit (no OOG)
            gasCase = CreateCase(cases.Count + 1, callType, ChildResult.Success,
                TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                ChildBehavior.MultipleWrites, ReturnDataSize.Zero, 2, Fork.Cancun);
            gasCase.GasLimit = 1_000_000; // Plenty
            UpdateCaseId(gasCase);
            cases.Add(gasCase);
        }

        // === CROSS-FORK VALIDATION (~20 cases) ===
        var forks = new[] { Fork.London, Fork.Shanghai };
        foreach (var fork in forks)
        {
            foreach (var callType in new[] { CallType.Call, CallType.DelegateCall, CallType.StaticCall })
            {
                // Cold access at fork F
                cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                    TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
                    ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, 2, fork));

                // Warm access at fork F
                cases.Add(CreateCase(cases.Count + 1, callType, ChildResult.Success,
                    TargetState.CodePresent, AccessWarmth.Warm, ValueTransfer.Zero,
                    ChildBehavior.SLoad, ReturnDataSize.ThirtyTwo, 2, fork));
            }
        }

        // === EXOTIC BEHAVIORS (~10 cases) ===
        // SELFDESTRUCT within child call
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.SelfDestruct, ReturnDataSize.Zero, 2, Fork.Cancun));

        // CREATE within child call
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.Create, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun));

        // CREATE with value
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.OneEther,
            ChildBehavior.Create, ReturnDataSize.ThirtyTwo, 2, Fork.Cancun));

        // SELFDESTRUCT at depth 3
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.SelfDestruct, ReturnDataSize.Zero, 3, Fork.Cancun));

        // LOG emission
        cases.Add(CreateCase(cases.Count + 1, CallType.Call, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.Log, ReturnDataSize.Zero, 2, Fork.Cancun));

        // LOG in STATICCALL (should revert)
        cases.Add(CreateCase(cases.Count + 1, CallType.StaticCall, ChildResult.Revert,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.Log, ReturnDataSize.Zero, 2, Fork.Cancun));

        // DELEGATECALL with SELFDESTRUCT (destroys parent)
        cases.Add(CreateCase(cases.Count + 1, CallType.DelegateCall, ChildResult.Success,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.SelfDestruct, ReturnDataSize.Zero, 2, Fork.Cancun));

        // STATICCALL with SELFDESTRUCT attempt (should revert)
        cases.Add(CreateCase(cases.Count + 1, CallType.StaticCall, ChildResult.Revert,
            TargetState.CodePresent, AccessWarmth.Cold, ValueTransfer.Zero,
            ChildBehavior.SelfDestruct, ReturnDataSize.Zero, 2, Fork.Cancun));

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
        var valueStr = value switch
        {
            ValueTransfer.Zero => "V0",
            ValueTransfer.OneWei => "V1",
            ValueTransfer.BoundaryLow => "V255",
            ValueTransfer.BoundaryHigh => "V256",
            ValueTransfer.OneEther => "V1E18",
            _ => "V0"
        };
        var targetStr = target switch
        {
            TargetState.CodePresent => "CODE",
            TargetState.EmptyAccount => "EMPTY",
            TargetState.Nonexistent => "NONEX",
            TargetState.Precompile => "PRE",
            _ => "CODE"
        };

        var caseId = $"R6_{callStr}_{accessStr}_{resultStr}_{behaviorStr}_{retStr}_{valueStr}_{targetStr}_D{depth}_{forkStr}";

        var testCase = new CallTestCase
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
        
        return testCase;
    }
    
    /// <summary>
    /// Update case ID after setting optional fields (precompile target, gas limit).
    /// Call this after setting PrecompileTarget or GasLimit.
    /// </summary>
    private static void UpdateCaseId(CallTestCase testCase)
    {
        var suffix = "";
        if (testCase.PrecompileTarget.HasValue)
        {
            suffix += $"_PRE{(int)testCase.PrecompileTarget.Value}";
        }
        if (testCase.GasLimit.HasValue)
        {
            suffix += $"_GAS{testCase.GasLimit.Value}";
        }
        
        if (!string.IsNullOrEmpty(suffix))
        {
            testCase.CaseId += suffix;
        }
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

            case ChildBehavior.MultipleWrites:
                // Write to slots 0, 1, 2 (test cold→warm transition)
                // Slot 0: PUSH1 0xAA, PUSH1 0x00, SSTORE
                opcodes.AddRange(new[] { "60", "aa", "60", "00", "55" });
                // Slot 1: PUSH1 0xBB, PUSH1 0x01, SSTORE
                opcodes.AddRange(new[] { "60", "bb", "60", "01", "55" });
                // Slot 2: PUSH1 0xCC, PUSH1 0x02, SSTORE
                opcodes.AddRange(new[] { "60", "cc", "60", "02", "55" });
                break;

            case ChildBehavior.SelfDestruct:
                // SELFDESTRUCT(caller)
                // PUSH20 <caller address 0x01>
                // SELFDESTRUCT
                opcodes.Add("73");  // PUSH20
                opcodes.AddRange(new[] {
                    "00", "00", "00", "00", "00", "00", "00", "00", "00", "00",
                    "00", "00", "00", "00", "00", "00", "00", "00", "00", "01"
                });
                opcodes.Add("ff");  // SELFDESTRUCT
                break;

            case ChildBehavior.Create:
                // CREATE(value=0, offset=0, size=minimal)
                // Deploy minimal contract: PUSH1 0x60, PUSH1 0x00, MSTORE, PUSH1 0x01, PUSH1 0x00, RETURN
                // Init code: 0x60600060005260016000f3 (11 bytes)
                // PUSH1 0x0B (init code size)
                // PUSH1 0x00 (offset - we'll write init code to memory first)
                // PUSH1 0x00 (value)
                // First write init code to memory:
                // PUSH11 0x60600060005260016000f3
                // PUSH1 0x00
                // MSTORE
                opcodes.AddRange(new[] { "6a", "60", "60", "00", "60", "00", "52", "60", "01", "60", "00", "f3" });
                opcodes.AddRange(new[] { "60", "00", "52" });
                // Now CREATE: PUSH1 0x0B (size), PUSH1 0x00 (offset), PUSH1 0x00 (value), CREATE
                opcodes.AddRange(new[] { "60", "0b", "60", "00", "60", "00", "f0" });
                // POP result address
                opcodes.Add("50");
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
                    // PUSH<n> <size>
                    // PUSH1 0x00 (offset)
                    // RETURN
                    var sizeBytes = BytecodeEncoder.EncodePushHex((ulong)returnSize);
                    opcodes.Add(sizeBytes);
                    opcodes.AddRange(new[] { "60", "00", "f3" });
                }
                else
                {
                    // STOP (success with no return)
                    opcodes.Add("00");
                }
                break;

            case ChildResult.Revert:
                // PUSH<n> <size>
                // PUSH1 0x00 (offset)
                // REVERT
                var revertSizeBytes = BytecodeEncoder.EncodePushHex((ulong)returnSize);
                opcodes.Add(revertSizeBytes);
                opcodes.AddRange(new[] { "60", "00", "fd" });
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
        // Use proper PUSH encoding for all sizes
        var retSizeBytes = BytecodeEncoder.EncodePushHex((ulong)retSize);
        opcodes.Add(retSizeBytes);  // retSize with correct PUSHn
        opcodes.AddRange(new[] { "60", "00" });  // retOffset
        opcodes.AddRange(new[] { "60", "00" });  // argsSize
        opcodes.AddRange(new[] { "60", "00" });  // argsOffset

        if (testCase.Type == CallType.Call)
        {
            var valueHex = testCase.Value switch
            {
                ValueTransfer.Zero => "00",
                ValueTransfer.OneWei => "01",
                ValueTransfer.BoundaryLow => BytecodeEncoder.EncodePushHex(255), // Max 1-byte value
                ValueTransfer.BoundaryHigh => BytecodeEncoder.EncodePushHex(256), // Min 2-byte value
                ValueTransfer.OneEther => BytecodeEncoder.EncodePushHex(1000000000000000000), // 1 ETH = 10^18 wei
                _ => "00"
            };
            
            if (testCase.Value == ValueTransfer.Zero || testCase.Value == ValueTransfer.OneWei)
            {
                opcodes.AddRange(new[] { "60", valueHex });  // PUSH1
            }
            else
            {
                opcodes.Add(valueHex);  // Already encoded with correct PUSHn
            }
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
