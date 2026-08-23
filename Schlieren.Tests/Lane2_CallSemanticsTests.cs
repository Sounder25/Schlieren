using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Opcodes;

/// <summary>
/// Lane 2: CALL-family semantics and gas forwarding edge cases.
/// Covers EIP-150 (63/64 cap), EIP-161 new-account gas, call stipend (2300),
/// EIP-3860 init-code cap, EIP-3529 SELFDESTRUCT refund removal,
/// EIP-211 RETURNDATACOPY safety, and caller balance pre-check.
/// </summary>
public class CallGasForwardingTests
{
    private static EvmExecutionContext MakeContext(ulong gasLimit = 100_000, ulong gasUsed = 0, bool isStatic = false)
    {
        var ctx = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex("0x1000000000000000000000000000000000000001"),
            Caller           = Address.FromHex("0x2000000000000000000000000000000000000002"),
            GlobalState      = new GlobalState(),
            GasLimit         = gasLimit,
            GasUsed          = gasUsed,
            IsStatic         = isStatic
        };
        return ctx;
    }

    // ── EIP-150: 63/64 gas cap ──────────────────────────────────────────────

    [Fact]
    public async Task StaticCall_63_64GasCap_AlwaysLeavesAtLeast1_64_InParent()
    {
        ulong gasLimit = 64_000;
        var ctx = MakeContext(gasLimit: gasLimit, gasUsed: 0);

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(0));
        };

        // Request ulong.MaxValue to ensure cap always triggers.
        var addr = new BigInteger(0x1234);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(addr);
        ctx.Stack.Push(new BigInteger(ulong.MaxValue));

        var (result, _) = await new OpcodeStaticCall().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        // EIP-150 invariant: parent must always retain at least 1/64 of remaining gas.
        // remaining is whatever GasLimit-GasUsed is at the moment of the cap; we just know
        // that receivedGas < gasLimit (some gas must stay).
        Assert.True(receivedGas < gasLimit,
            $"Child received {receivedGas} ≥ gasLimit {gasLimit}; EIP-150 cap not applied");
        // Upper bound: no more than 63/64 of the full limit can flow to child.
        Assert.True(receivedGas <= gasLimit - gasLimit / 64,
            $"Child received {receivedGas} > {gasLimit - gasLimit / 64}; EIP-150 cap violated");
    }

    [Fact]
    public async Task StaticCall_SmallRequest_NotFurtherReducedByEip150()
    {
        // When the requested gas is already < maxForward, the child should receive exactly the request.
        ulong gasLimit = 100_000;
        var ctx = MakeContext(gasLimit: gasLimit, gasUsed: 0);

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(0));
        };

        var addr = new BigInteger(0xFF);
        // Request 100 gas — well below the 63/64 cap regardless of how many state reads occur
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(addr);
        ctx.Stack.Push(100);

        await new OpcodeStaticCall().ExecuteAsync(ctx);

        // The 63/64 cap only limits the *maximum* forwarded amount, not a small request.
        Assert.Equal(100UL, receivedGas);
    }

    [Fact]
    public async Task DelegateCall_63_64GasCap_Preserved()
    {
        ulong gasLimit = 100_000;
        var ctx = MakeContext(gasLimit: gasLimit, gasUsed: 0);

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(0));
        };

        var addr = new BigInteger(0x5678);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(addr);
        ctx.Stack.Push(new BigInteger(ulong.MaxValue)); // request >> remaining

        await new OpcodeDelegateCall().ExecuteAsync(ctx);

        // EIP-150: received < gasLimit and received ≤ gasLimit - gasLimit/64
        Assert.True(receivedGas < gasLimit);
        Assert.True(receivedGas <= gasLimit - gasLimit / 64);
    }

    [Fact]
    public async Task Call_63_64GasCap_NoValueTransfer()
    {
        ulong gasLimit = 100_000;
        var ctx = MakeContext(gasLimit: gasLimit, gasUsed: 0);

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(0));
        };

        var addr = new BigInteger(0xDEAD);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.Zero); // value = 0
        ctx.Stack.Push(addr);
        ctx.Stack.Push(new BigInteger(ulong.MaxValue)); // request huge amount

        await new OpcodeCall().ExecuteAsync(ctx);

        // EIP-150: no stipend since value=0; child must still be capped at 63/64 of remaining.
        Assert.True(receivedGas < gasLimit);
        Assert.True(receivedGas <= gasLimit - gasLimit / 64);
    }

    // ── Call stipend (2300) ─────────────────────────────────────────────────

    [Fact]
    public async Task Call_Stipend_AddedWhenValueNonZero()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        var callee = Address.FromHex("0x2000000000000000000000000000000000000002");
        // Pre-seed callee with non-empty code so new-account gas (25000) is not charged.
        state.SetCode(callee, new byte[] { 0x00 });
        state.SetBalance(caller, 1_000_000);

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000,
            GasUsed         = 0,
        };

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(tx.GasLimit));
        };

        // Push: gas=50000, addr=callee, value=1, argsO=0, argsL=0, retO=0, retL=0
        var addrBig = new BigInteger(callee.Bytes, isUnsigned: true, isBigEndian: true);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.One); // value = 1 wei
        ctx.Stack.Push(addrBig);
        ctx.Stack.Push(50_000);

        await new OpcodeCall().ExecuteAsync(ctx);

        // Child should receive forwarded (50000 within 63/64 cap) + 2300 stipend = 52300.
        Assert.Equal(52_300UL, receivedGas);
    }

    [Fact]
    public async Task Call_Stipend_NotAddedWhenValueIsZero()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        state.SetBalance(caller, 1_000_000);

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000,
            GasUsed         = 0,
        };

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(tx.GasLimit));
        };

        var addr = new BigInteger(0xBEEF);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.Zero); // value = 0
        ctx.Stack.Push(addr);
        ctx.Stack.Push(50_000);

        await new OpcodeCall().ExecuteAsync(ctx);

        // No stipend: receivedGas == 50000 (within 63/64 cap of 100000).
        Assert.Equal(50_000UL, receivedGas);
    }

    // ── Caller balance pre-check ────────────────────────────────────────────

    [Fact]
    public async Task Call_InsufficientBalance_Pushes0_NoSubCall()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        state.SetBalance(caller, 5); // only 5 wei

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000
        };

        bool subCallInvoked = false;
        ctx.SubCall = (_, _, _, _, _) =>
        {
            subCallInvoked = true;
            return Task.FromResult(ExecutionResult.Success(0));
        };

        var addr = new BigInteger(0xAAAA);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(new BigInteger(10)); // value = 10 > balance 5
        ctx.Stack.Push(addr);
        ctx.Stack.Push(50_000);

        var (result, _) = await new OpcodeCall().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);   // Outer call returns success with 0 on stack
        Assert.False(subCallInvoked);    // Sub-call never issued
        Assert.Equal(BigInteger.Zero, ctx.Stack.Pop()); // 0 pushed for failure
    }

    [Fact]
    public async Task CallCode_InsufficientBalance_Pushes0_NoSubCall()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        state.SetBalance(caller, 3);

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000
        };

        bool subCallInvoked = false;
        ctx.SubCall = (_, _, _, _, _) =>
        {
            subCallInvoked = true;
            return Task.FromResult(ExecutionResult.Success(0));
        };

        var addr = new BigInteger(0xBBBB);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(new BigInteger(100)); // value > balance
        ctx.Stack.Push(addr);
        ctx.Stack.Push(50_000);

        var (result, _) = await new OpcodeCallCode().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.False(subCallInvoked);
        Assert.Equal(BigInteger.Zero, ctx.Stack.Pop());
    }

    [Fact]
    public async Task CallCode_WithValue_InStaticContext_CanCallPrecompile()
    {
        var ctx = MakeContext(isStatic: true);
        ctx.GlobalState.SetBalance(ctx.ContractAddress, 1);

        // CALLCODE(gas=50_000, codeAddress=ECRECOVER, value=1,
        //          argsOffset=0, argsLength=0, retOffset=0, retLength=0)
        // CALLCODE does not transfer value to another account, so unlike CALL,
        // a non-zero value is permitted while the current frame is static.
        ctx.Stack.Push(0);
        ctx.Stack.Push(0);
        ctx.Stack.Push(0);
        ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.One);
        ctx.Stack.Push(BigInteger.One);
        ctx.Stack.Push(50_000);

        var (result, _) = await new OpcodeCallCode().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    // ── LastReturnData cleared on call entry ────────────────────────────────

    [Fact]
    public async Task Call_LastReturnData_ClearedBeforeSubCall()
    {
        var ctx = MakeContext();
        // Pre-seed stale return data
        ctx.LastReturnData = new byte[] { 0xDE, 0xAD };

        // Sub-call fails and returns empty data
        ctx.SubCall = (_, _, _, _, _) =>
            Task.FromResult(ExecutionResult.Failure(EvmError.OutOfGas, 0, Array.Empty<byte>()));

        var addr = new BigInteger(0);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.Zero);
        ctx.Stack.Push(addr);
        ctx.Stack.Push(10_000);

        await new OpcodeCall().ExecuteAsync(ctx);

        // LastReturnData should be the sub-call result (empty), not the stale value.
        Assert.Empty(ctx.LastReturnData);
    }

    [Fact]
    public async Task StaticCall_LastReturnData_UpdatedAfterSubCall()
    {
        var ctx = MakeContext();
        ctx.LastReturnData = new byte[] { 0xFF };

        ctx.SubCall = (_, _, _, _, _) =>
            Task.FromResult(ExecutionResult.Success(0, new byte[] { 0xAB }));

        var addr = new BigInteger(0);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(addr);
        ctx.Stack.Push(10_000);

        await new OpcodeStaticCall().ExecuteAsync(ctx);

        Assert.Equal(new byte[] { 0xAB }, ctx.LastReturnData);
    }

    [Fact]
    public async Task DelegateCall_LastReturnData_ClearedOnFailedSubCall()
    {
        var ctx = MakeContext();
        ctx.LastReturnData = new byte[] { 0xCC, 0xDD };

        ctx.SubCall = (_, _, _, _, _) =>
            Task.FromResult(ExecutionResult.Failure(EvmError.Revert, 0, Array.Empty<byte>()));

        var addr = new BigInteger(0);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(addr);
        ctx.Stack.Push(10_000);

        await new OpcodeDelegateCall().ExecuteAsync(ctx);

        Assert.Empty(ctx.LastReturnData);
    }

    // ── EIP-3860: CREATE / CREATE2 init-code size cap ──────────────────────

    [Fact]
    public async Task Create_OversizedInitCode_ReturnsOutOfGas()
    {
        var ctx = MakeContext(gasLimit: 10_000_000);

        // Stack order for CREATE: value, offset, length (pushed reverse for LIFO)
        ctx.Stack.Push(49_153); // length > 2*24576
        ctx.Stack.Push(0);      // offset
        ctx.Stack.Push(0);      // value

        var (result, _) = await new OpcodeCreate().ExecuteAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    [Fact]
    public async Task Create_MaxAllowedInitCode_SizeClearsPast_SizeCheck()
    {
        var ctx = MakeContext(gasLimit: 10_000_000);

        bool subCallInvoked = false;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            subCallInvoked = true;
            return Task.FromResult(ExecutionResult.Success(tx.GasLimit, new byte[] { 0x60, 0x00 }));
        };

        // Boundary: exactly 49152 bytes (2 × 24576) — should NOT fail size check.
        var initCode = new byte[49_152];
        ctx.Memory.Store(0, initCode);
        ctx.Stack.Push(49_152); // length
        ctx.Stack.Push(0);      // offset
        ctx.Stack.Push(0);      // value

        await new OpcodeCreate().ExecuteAsync(ctx);

        Assert.True(subCallInvoked, "SubCall should have been reached (size check passes at exactly 49152)");
    }

    [Fact]
    public async Task Create2_OversizedInitCode_ReturnsOutOfGas()
    {
        var ctx = MakeContext(gasLimit: 10_000_000);

        // Stack order for CREATE2: value, offset, length, salt (pushed reverse)
        ctx.Stack.Push(0);       // salt
        ctx.Stack.Push(49_153);  // length > 49152
        ctx.Stack.Push(0);       // offset
        ctx.Stack.Push(0);       // value

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    [Fact]
    public async Task Create2_InitCodeWordGas_IsCharged()
    {
        var ctx = MakeContext(gasLimit: 10_000_000);

        ulong? gasPassedToChild = null;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            gasPassedToChild = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(tx.GasLimit, new byte[] { 0x00 }));
        };

        // 64 bytes of init code → 2 words
        // initCodeWordGas = 2 × 2 = 4; hashWordGas = 6 × 2 = 12; base = 32000 → total = 32016
        var initCode = new byte[64];
        ctx.Memory.Store(0, initCode);
        var memoryExpansionGas = ctx.Memory.CalculateGasCost(initCode.Length);

        ctx.Stack.Push(0);  // salt
        ctx.Stack.Push(64); // length
        ctx.Stack.Push(0);  // offset
        ctx.Stack.Push(0);  // value

        await new OpcodeCreate2().ExecuteAsync(ctx);

        Assert.NotNull(gasPassedToChild);
        const ulong createBaseGas = 32_000;
        const ulong initCodeWordGas = 2 * 2;
        const ulong hashWordGas = 6 * 2;
        var gasBeforeEip150 =
            ctx.GasLimit - createBaseGas - memoryExpansionGas - initCodeWordGas - hashWordGas;
        var expectedForwarded = gasBeforeEip150 - gasBeforeEip150 / 64UL;

        Assert.Equal(0UL, memoryExpansionGas);
        Assert.Equal(9_967_984UL, gasBeforeEip150);
        Assert.Equal(9_812_235UL, expectedForwarded);
        Assert.Equal(expectedForwarded, gasPassedToChild!.Value);
    }

    // ── EIP-3529: SELFDESTRUCT no gas refund ───────────────────────────────

    [Fact]
    public async Task SelfDestruct_ChargesBaseAndColdBeneficiaryAccess()
    {
        var state = new GlobalState();
        var contract    = Address.FromHex("0x1000000000000000000000000000000000000001");
        var beneficiary = Address.FromHex("0x2000000000000000000000000000000000000002");
        state.SetBalance(contract, 500);

        var ctx = new EvmExecutionContext
        {
            ContractAddress = contract,
            GlobalState     = state,
            GasLimit        = 50_000,
            Code            = new byte[] { 0xFF }
        };

        var addrBig = new BigInteger(beneficiary.Bytes, isUnsigned: true, isBigEndian: true);
        ctx.Stack.Push(addrBig);

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        // EIP-3529 removes the old refund, not SELFDESTRUCT's 5000 static cost.
        // A cold, previously nonexistent beneficiary adds both the EIP-2929
        // 2600 access surcharge and the 25000 new-account surcharge.
        Assert.Equal(32600UL, result.GasUsed);
        // Balance must flow to beneficiary.
        var benBal = await state.GetBalanceAsync(beneficiary, default);
        Assert.Equal(new BigInteger(500), benBal);
    }

    [Fact]
    public async Task SelfDestruct_InStaticContext_Fails()
    {
        var ctx = MakeContext(isStatic: true);
        ctx.Stack.Push(BigInteger.Zero);

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.StaticModeViolation, result.Error);
    }

    // ── RETURNDATACOPY overflow safety ──────────────────────────────────────

    [Fact]
    public async Task ReturnDataCopy_OobOffset_ReturnsOutOfGas()
    {
        var ctx = MakeContext();
        ctx.LastReturnData = new byte[] { 0x01, 0x02, 0x03 };

        // offset=2, length=5 → 2+5=7 > 3 → out of bounds
        ctx.Stack.Push(5);  // length
        ctx.Stack.Push(2);  // offset
        ctx.Stack.Push(0);  // dest

        var (result, _) = await new OpcodeReturnDataCopy().ExecuteAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    [Fact]
    public async Task ReturnDataCopy_BigIntegerOverflowOffset_ReturnsOutOfGas()
    {
        var ctx = MakeContext();
        ctx.LastReturnData = new byte[] { 0xFF };

        // Push a value larger than int.MaxValue
        var hugOffset = new BigInteger(int.MaxValue) + 1;
        ctx.Stack.Push(0);         // length
        ctx.Stack.Push(hugOffset); // offset > int.MaxValue
        ctx.Stack.Push(0);         // dest

        var (result, _) = await new OpcodeReturnDataCopy().ExecuteAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    [Fact]
    public async Task ReturnDataCopy_ZeroLength_AlwaysSucceeds()
    {
        var ctx = MakeContext();
        ctx.LastReturnData = new byte[] { 0xAB };

        ctx.Stack.Push(0); // length = 0
        ctx.Stack.Push(0); // offset
        ctx.Stack.Push(0); // dest

        var (result, _) = await new OpcodeReturnDataCopy().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ReturnDataCopy_ValidCopy_CopiesCorrectBytes()
    {
        var ctx = MakeContext();
        ctx.LastReturnData = new byte[] { 0x10, 0x20, 0x30, 0x40 };

        // offset=1, length=2 → should copy [0x20, 0x30] to dest=0
        ctx.Stack.Push(2); // length
        ctx.Stack.Push(1); // offset
        ctx.Stack.Push(0); // dest

        var (result, _) = await new OpcodeReturnDataCopy().ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 0x20, 0x30 }, ctx.Memory.Load(0, 2));
    }

    // ── CallCode stipend ────────────────────────────────────────────────────

    [Fact]
    public async Task CallCode_Stipend_AddedWhenValueNonZero()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        state.SetBalance(caller, 1_000_000);

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000
        };

        ulong receivedGas = 0;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            receivedGas = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(tx.GasLimit));
        };

        var addr = new BigInteger(0xCCCC);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.One); // value = 1
        ctx.Stack.Push(addr);
        ctx.Stack.Push(50_000);

        await new OpcodeCallCode().ExecuteAsync(ctx);

        // childGasLimit = 50000 + 2300 = 52300
        Assert.Equal(52_300UL, receivedGas);
    }

    // ── Gas refund accounting ───────────────────────────────────────────────

    [Fact]
    public async Task Call_UnusedGas_RefundIncludesUnchargedStipend()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        var callee = Address.FromHex("0x4000000000000000000000000000000000000004");
        // Pre-seed callee with code so it's NOT empty (avoids new-account gas).
        state.SetCode(callee, new byte[] { 0x00 });
        state.SetBalance(caller, 1_000_000);

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000,
            GasUsed         = 0
        };

        const ulong childGasUsed = 0;
        ulong? childGasLimit = null;
        ctx.SubCall = (tx, _, _, _, _) =>
        {
            childGasLimit = tx.GasLimit;
            return Task.FromResult(ExecutionResult.Success(childGasUsed));
        };

        var addrBig = new BigInteger(callee.Bytes, isUnsigned: true, isBigEndian: true);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.One); // value = 1 → stipend applies
        ctx.Stack.Push(addrBig);
        ctx.Stack.Push(10_000); // request 10000

        var parentGasBeforeCall = ctx.GasLimit - ctx.GasUsed;
        await new OpcodeCall().ExecuteAsync(ctx);

        const ulong accessCost = 2_600;
        const ulong valueTransferCost = 9_000;
        const ulong requestedGas = 10_000;
        var gasAvailableForForwarding =
            parentGasBeforeCall - accessCost - valueTransferCost;
        var eip150Cap =
            gasAvailableForForwarding - gasAvailableForForwarding / 64UL;
        var forwardedGas = Math.Min(requestedGas, eip150Cap);
        var parentDebit = accessCost + valueTransferCost + forwardedGas;
        const ulong stipend = 2_300;
        var expectedChildGasLimit = forwardedGas + stipend;
        var childGasRemaining = expectedChildGasLimit - childGasUsed;
        var parentRefund = childGasRemaining;
        var parentFinalGas = parentGasBeforeCall - parentDebit + parentRefund;

        Assert.Equal(100_000UL, parentGasBeforeCall);
        Assert.Equal(87_019UL, eip150Cap);
        Assert.Equal(10_000UL, forwardedGas);
        Assert.Equal(21_600UL, parentDebit);
        Assert.Equal(12_300UL, expectedChildGasLimit);
        Assert.Equal(expectedChildGasLimit, childGasLimit);
        Assert.Equal(12_300UL, childGasRemaining);
        Assert.Equal(12_300UL, parentRefund);
        Assert.Equal(90_700UL, parentFinalGas);
        Assert.Equal(parentGasBeforeCall - parentFinalGas, ctx.GasUsed);
    }

    [Fact]
    public async Task Call_FullGasUsedByChild_NoRefundToParent()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        var callee = Address.FromHex("0x3000000000000000000000000000000000000003");
        state.SetBalance(caller, 1_000_000);
        // Pre-seed callee with code so it is NOT empty (avoids new-account gas).
        state.SetCode(callee, new byte[] { 0x00 });

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000,
            GasUsed         = 0
        };

        ctx.SubCall = (tx, _, _, _, _) =>
            Task.FromResult(ExecutionResult.Success(tx.GasLimit)); // child burns all its gas

        var addrBig = new BigInteger(callee.Bytes, isUnsigned: true, isBigEndian: true);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.Zero); // value = 0 → no stipend, no new-account gas
        ctx.Stack.Push(addrBig);
        ctx.Stack.Push(10_000);

        await new OpcodeCall().ExecuteAsync(ctx);

        // gasLimit=10000; child used all 10000 → refund=0.
        // EIP-2929 cold touch of callee (+2600) is included in GasUsed.
        // Actual: GasUsed = 10000 (forwarded, all burned) + 2600 (cold callee EIP-2929) = 12600.
        Assert.Equal(12_600UL, ctx.GasUsed);
    }

    [Fact]
    public async Task Call_PartialGasUsedByChild_PartialRefund()
    {
        var state = new GlobalState();
        var caller = Address.FromHex("0x1000000000000000000000000000000000000001");
        var callee = Address.FromHex("0x5000000000000000000000000000000000000005");
        state.SetBalance(caller, 1_000_000);
        state.SetCode(callee, new byte[] { 0x00 }); // non-empty

        var ctx = new EvmExecutionContext
        {
            ContractAddress = caller,
            GlobalState     = state,
            GasLimit        = 100_000,
            GasUsed         = 0
        };

        // Child uses exactly 3000 out of 10000 forwarded (no stipend since value=0).
        ctx.SubCall = (tx, _, _, _, _) =>
            Task.FromResult(ExecutionResult.Success(3_000));

        var addrBig = new BigInteger(callee.Bytes, isUnsigned: true, isBigEndian: true);
        ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0); ctx.Stack.Push(0);
        ctx.Stack.Push(BigInteger.Zero); // value = 0
        ctx.Stack.Push(addrBig);
        ctx.Stack.Push(10_000);

        await new OpcodeCall().ExecuteAsync(ctx);

        // gasLimit=10000; child used 3000 → refund=7000; GasUsed=10000-7000=3000.
        // Plus cold EIP-2929 touch of callee at 2600 → actual = 3000+2600=5600.
        Assert.Equal(5_600UL, ctx.GasUsed);
    }
}
