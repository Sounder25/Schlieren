using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public sealed class OpcodeSload : IOpcode
{
    public byte Code => 0x54;
    public string Name => "SLOAD";

    // [AI-EDIT 2026-01-10] EIP-2929: cold slot = 2100 gas, warm slot = 100 gas.
    private const ulong ColdSlotCost = 2100;
    private const ulong WarmSlotCost = 100;

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var key))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var slotKey = (context.StorageAddress, key);
        
        // EIP-2929: first access to a slot within the tx is cold (2100), subsequent warm (100).
        var isWarm = context.Access.TouchSlot(context.StorageAddress, key);
        var gasCost = isWarm ? WarmSlotCost : ColdSlotCost;

        var value = await context.Storage.LoadAsync(key);
        
        // EIP-2200: Capture original value on first access to this slot in the transaction
        if (!context.OriginalStorageValues.ContainsKey(slotKey))
        {
            context.OriginalStorageValues[slotKey] = value;
        }
        
        context.TraceStorageRead(key, value);
        
        if (!context.Stack.TryPush(value))
             return (ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1);

        return (ExecutionResult.Success(gasCost), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeSstore : IOpcode
{
    public byte Code => 0x55;
    public string Name => "SSTORE";

    // [AI-EDIT 2026-01-10] EIP-2929 + EIP-2200 SSTORE gas costs (Cancun rules, EIP-3529 caps refunds).
    // Cold slot access surcharge per EIP-2929.
    private const ulong ColdSlotSurcharge = 2100;
    // EIP-2200 base SSTORE costs (applied after warm/cold determination):
    private const ulong SstoreSetCost = 20000;   // 0 → non-zero
    private const ulong SstoreResetCost = 2900;  // non-zero → non-zero (different value)
    private const ulong SstoreWarmNoopCost = 100; // no-op (value unchanged)
    private const ulong SstoreCleanupCost = 2900; // non-zero → 0 (with refund)

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.IsStatic)
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1)).Result;

        // EIP-2200 reentrancy guard: SSTORE is prohibited when gas_left <= CALL_STIPEND (2300).
        // EELS sstore(): "if evm.gas_left <= GasCosts.CALL_STIPEND: raise OutOfGasError"
        var gasRemaining = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;
        if (gasRemaining <= 2300UL)
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.OutOfGas, gasRemaining), context.ProgramCounter + 1)).Result;

        if (!context.Stack.TryPop(out var key) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1)).Result;

        var slotKey = (context.StorageAddress, key);
        var currentValue = await context.Storage.LoadAsync(key);
        
        // EIP-2200: Capture original value on first access to this slot in the transaction
        if (!context.OriginalStorageValues.ContainsKey(slotKey))
        {
            context.OriginalStorageValues[slotKey] = currentValue;
        }
        var originalValue = context.OriginalStorageValues[slotKey];
        
        // EIP-2929: Determine cold vs warm, charge cold surcharge if cold
        var isWarm = context.Access.TouchSlot(context.StorageAddress, key);
        ulong coldCost = isWarm ? 0 : ColdSlotSurcharge;
        
        // EIP-2200 + EIP-3529: Full (original, current, new) tri-state metering
        ulong baseCost;
        long refundDelta = 0;
        
        if (currentValue == value)
        {
            // No-op: value unchanged
            baseCost = SstoreWarmNoopCost; // 100 gas
        }
        else if (originalValue == currentValue)
        {
            // First write to this slot in the transaction
            if (originalValue == BigInteger.Zero)
            {
                // 0 → non-zero: setting a new slot
                baseCost = SstoreSetCost; // 20,000 gas
            }
            else
            {
                // non-zero → different value
                baseCost = SstoreResetCost; // 2,900 gas
                
                // If clearing the slot, grant refund
                if (value == BigInteger.Zero)
                {
                    refundDelta = 4800; // EIP-3529 refund for clearing storage
                }
            }
        }
        else
        {
            // Subsequent write to a slot already modified in this transaction (dirty slot)
            baseCost = SstoreWarmNoopCost; // 100 gas
            
            // EIP-2200 dirty-slot refund adjustments
            if (originalValue != BigInteger.Zero)
            {
                // Original was non-zero: track zero-transitions for clear refund
                if (currentValue == BigInteger.Zero)
                {
                    // Was cleared, now writing non-zero: remove prior refund
                    refundDelta -= 4800;
                }
                if (value == BigInteger.Zero)
                {
                    // Now clearing: add refund
                    refundDelta += 4800;
                }
            }
            
            // Restore-to-original refund (independent of clear refund)
            if (value == originalValue)
            {
                if (originalValue == BigInteger.Zero)
                {
                    // Restoring to zero: reclaim the set cost
                    refundDelta += (long)(SstoreSetCost - SstoreWarmNoopCost); // +19,900
                }
                else
                {
                    // Restoring to original non-zero: reclaim the reset cost
                    refundDelta += (long)(SstoreResetCost - SstoreWarmNoopCost); // +2,800
                }
            }
        }
        
        ulong totalCost = coldCost + baseCost;
        context.GasRefundCounter += refundDelta;
        
        context.Storage.Store(key, value);
        context.TraceStorageWrite(key, value);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(totalCost), context.ProgramCounter + 1)).Result;
    }
}

public sealed class OpcodeTload : IOpcode
{
    public byte Code => 0x5C;
    public string Name => "TLOAD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var key))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var value = context.LoadTransientStorage(key);
        if (!context.Stack.TryPush(value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(100), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeTstore : IOpcode
{
    public byte Code => 0x5D;
    public string Name => "TSTORE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.IsStatic)
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var key) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        context.StoreTransientStorage(key, value);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(100), context.ProgramCounter + 1));
    }
}
