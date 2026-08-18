using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Interaction-focused test cases targeting execution semantics at intersections.
/// Bugs live in interactions, not individual dimensions.
/// 
/// Focus areas:
/// - State modification attempts in read-only contexts (STATICCALL → SSTORE/LOG/SELFDESTRUCT)
/// - Value transfer edge cases (insufficient balance, nonexistent accounts, precompiles)
/// - Nested call failures (OOG propagation, revert bubbling, returndata handling)
/// - Creation lifecycle (CREATE/CREATE2 → REVERT, address collision)
/// - Exceptional halts and rollback semantics
/// </summary>
public static class InteractionMatrixGenerator
{
    public enum InteractionPattern
    {
        // State modification in read-only context
        StaticCallSStore,
        StaticCallLog,
        StaticCallSelfDestruct,
        StaticCallCreate,
        StaticCallValueTransfer,
        
        // Value transfer edge cases
        CallValueInsufficientBalance,
        CallValueNonexistentTarget,
        CallValueEmptyTarget,
        CallValuePrecompile,
        DelegateCallWithValue,  // Should fail
        
        // Nested call failures
        CallNestedOOG,
        CallNestedRevert,
        CallNestedRevertWithReturndata,
        DelegateCallNestedRevert,
        
        // Creation lifecycle
        CallCreateRevert,
        CallCreate2Revert,
        CallCreate2Collision,
        CreateInsufficientBalance,
        CreateDepthLimit,
        
        // Storage transitions during revert
        SStoreRevert,
        MultipleSStoreRevert,
        SStoreNestedRevert,
        
        // Returndata edge cases
        ReturndataZero,
        ReturndataExact31,
        ReturndataExact32,
        ReturndataExact33,
        Returndata255,
        Returndata256,
        Returndata257,
        ReturndataRevert,
        
        // Gas forwarding rules
        Gas63_64thsForwarding,
        Gas2300Stipend,
        GasColdAccountAccess,
        GasWarmAccountAccess,
        GasColdStorageAccess,
        GasWarmStorageAccess,
        GasMemoryExpansion,
        GasValueTransferSurcharge,
        GasNewAccountSurcharge,
        
        // Precompile edge cases
        PrecompileInvalidInput,
        PrecompileMalformedInput,
        PrecompileInsufficientGas,
        PrecompileWithValue,
        
        // DELEGATECALL semantics
        DelegateCallSStore,        // Writes to caller's storage
        DelegateCallSelfDestruct,  // Destroys caller
        DelegateCallBalance,       // Reads caller's balance
        DelegateCallCallValue,     // Gets caller's msg.value
        
        // CALLCODE semantics (deprecated but must work)
        CallCodeValue,
        CallCodeSStore,
        CallCodeBalance
    }
    
    public sealed class InteractionTestCase
    {
        public required string CaseId { get; set; }
        public required InteractionPattern Pattern { get; init; }
        public required string Description { get; init; }
        public required CallSemanticsMatrixGenerator.Fork Fork { get; init; }
        public required int Depth { get; init; }
        
        public string ParentBytecode { get; set; } = "";
        public string ChildBytecode { get; set; } = "";
        public string? GrandchildBytecode { get; set; }
        
        public bool ExpectedSuccess { get; set; }
        public ulong? ExpectedGas { get; set; }
        public string? ExpectedReturndata { get; set; }
        
        public string GetCanonicalFingerprint()
        {
            return $"{Fork}|{Pattern}|{Depth}";
        }
    }
    
    /// <summary>
    /// Generate interaction-focused test matrix.
    /// Target: ~100 high-value interaction cases.
    /// </summary>
    public static List<InteractionTestCase> GenerateMatrix()
    {
        var cases = new List<InteractionTestCase>();
        
        // === STATICCALL STATE MODIFICATION ATTEMPTS ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.StaticCallSStore,
            "STATICCALL attempts SSTORE (should revert)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.StaticCallLog,
            "STATICCALL attempts LOG0 (should revert)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.StaticCallSelfDestruct,
            "STATICCALL attempts SELFDESTRUCT (should revert)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.StaticCallCreate,
            "STATICCALL attempts CREATE (should revert)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.StaticCallValueTransfer,
            "STATICCALL with non-zero value (should fail at call site)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        // === VALUE TRANSFER EDGE CASES ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallValueInsufficientBalance,
            "CALL with value > caller balance (should fail, no state change)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallValueNonexistentTarget,
            "CALL with value to nonexistent account (creates account, transfers value)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallValueEmptyTarget,
            "CALL with value to empty account (resurrects account)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallValuePrecompile,
            "CALL with value to precompile (Identity/ModExp accept, others reject)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        // === NESTED CALL FAILURES ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallNestedOOG,
            "CALL → child OOG (parent continues, returndata empty, success=0)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallNestedRevert,
            "CALL → child REVERT (parent continues, child state rolled back)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallNestedRevertWithReturndata,
            "CALL → child REVERT(32 bytes) (parent gets returndata, child state rolled back)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.DelegateCallNestedRevert,
            "DELEGATECALL → child REVERT (parent storage unchanged, returndata available)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        // === CREATION LIFECYCLE ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallCreateRevert,
            "CALL → CREATE → REVERT in init code (no contract deployed, gas consumed)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallCreate2Revert,
            "CALL → CREATE2 → REVERT in init code (no contract, address not reserved)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallCreate2Collision,
            "CALL → CREATE2 with collision (returns zero address, no deployment)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        // === STORAGE + REVERT INTERACTION ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.SStoreRevert,
            "SSTORE → REVERT (storage change rolled back, gas for SSTORE consumed)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.MultipleSStoreRevert,
            "3× SSTORE → REVERT (all storage rolled back, cold→warm gas charged)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.SStoreNestedRevert,
            "Parent SSTORE → CALL → child SSTORE → child REVERT (child rolled back, parent kept)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        // === RETURNDATA SIZE BOUNDARIES ===
        foreach (var size in new[] { 0, 31, 32, 33, 255, 256, 257 })
        {
            cases.Add(CreateCase(cases.Count + 1, 
                size == 0 ? InteractionPattern.ReturndataZero :
                size == 31 ? InteractionPattern.ReturndataExact31 :
                size == 32 ? InteractionPattern.ReturndataExact32 :
                size == 33 ? InteractionPattern.ReturndataExact33 :
                size == 255 ? InteractionPattern.Returndata255 :
                size == 256 ? InteractionPattern.Returndata256 :
                InteractionPattern.Returndata257,
                $"CALL with {size}-byte returndata (boundary test)",
                CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        }
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.ReturndataRevert,
            "CALL → REVERT(32 bytes) → parent RETURNDATACOPY (gets revert reason)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 3));
        
        // === DELEGATECALL SEMANTICS ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.DelegateCallSStore,
            "DELEGATECALL → child SSTORE (writes to PARENT storage context)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.DelegateCallSelfDestruct,
            "DELEGATECALL → child SELFDESTRUCT (destroys PARENT, not child)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.DelegateCallBalance,
            "DELEGATECALL → child reads BALANCE (gets PARENT balance, not child)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.DelegateCallCallValue,
            "DELEGATECALL → child reads CALLVALUE (gets PARENT msg.value)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        // === CALLCODE SEMANTICS (deprecated but must work) ===
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallCodeValue,
            "CALLCODE with value (deprecated opcode, must still work correctly)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        cases.Add(CreateCase(cases.Count + 1, InteractionPattern.CallCodeSStore,
            "CALLCODE → child SSTORE (writes to CALLER storage, like DELEGATECALL)",
            CallSemanticsMatrixGenerator.Fork.Cancun, 2));
        
        // Deduplicate by canonical fingerprint
        cases = cases
            .GroupBy(c => c.GetCanonicalFingerprint())
            .Select(g => g.First())
            .ToList();
        
        return cases;
    }
    
    private static InteractionTestCase CreateCase(
        int index,
        InteractionPattern pattern,
        string description,
        CallSemanticsMatrixGenerator.Fork fork,
        int depth)
    {
        var caseId = $"INT_{index:D3}_{pattern}_{fork}";
        
        return new InteractionTestCase
        {
            CaseId = caseId,
            Pattern = pattern,
            Description = description,
            Fork = fork,
            Depth = depth
        };
    }
}
