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

        // EIP-2929: first access to a slot within the tx is cold (2100), subsequent warm (100).
        var isWarm = context.Access.TouchSlot(context.ContractAddress, key);
        var gasCost = isWarm ? WarmSlotCost : ColdSlotCost;

        var value = await context.Storage.LoadAsync(key);
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

        if (!context.Stack.TryPop(out var key) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1)).Result;

        // EIP-2929: determine cold vs warm, charge cold surcharge if cold.
        var isWarm = context.Access.TouchSlot(context.ContractAddress, key);
        ulong gasCost = isWarm ? 0 : ColdSlotSurcharge;

        // EIP-2200: determine base cost based on current vs new value.
        var currentValue = await context.Storage.LoadAsync(key);
        if (currentValue == value)
        {
            gasCost += SstoreWarmNoopCost; // No change — warm no-op cost
        }
        else if (currentValue == BigInteger.Zero)
        {
            gasCost += SstoreSetCost; // Storage was empty, now setting a value
        }
        else if (value == BigInteger.Zero)
        {
            gasCost += SstoreCleanupCost; // Clearing storage (refunds handled externally)
        }
        else
        {
            gasCost += SstoreResetCost; // Changing to a different non-zero value
        }

        context.Storage.Store(key, value);
        context.TraceStorageWrite(key, value);

        // EIP-3529: grant refund when clearing storage (non-zero → zero).
        if (value == BigInteger.Zero && currentValue != BigInteger.Zero)
        {
            context.GasRefundCounter += 4800;
        }

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(gasCost), context.ProgramCounter + 1)).Result;
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
