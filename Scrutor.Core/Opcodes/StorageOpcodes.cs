using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public sealed class OpcodeSload : IOpcode
{
    public byte Code => 0x54;
    public string Name => "SLOAD";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var key))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var rules   = context.Block.Rules;
        var slotKey = (context.StorageAddress, key);

        // EIP-2929 (Berlin+): warm/cold slot tracking.  Pre-Berlin: always "warm" path,
        // cost comes entirely from fork rules.
        bool isWarm = rules.HasEip2929WarmCold
            ? context.Access.TouchSlot(context.StorageAddress, key)
            : true; // pre-Berlin: no warm/cold concept, flat cost via SloadCost

        var gasCost = rules.SloadCost(isWarm);

        var value = await context.Storage.LoadAsync(key);

        // EIP-2200 (Istanbul+): capture original value for SSTORE tri-state metering.
        if (rules.HasEip2200Reentrancy && !context.OriginalStorageValues.ContainsKey(slotKey))
            context.OriginalStorageValues[slotKey] = value;

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

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.IsStatic)
            return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

        var rules = context.Block.Rules;

        // EIP-2200 (Istanbul+) reentrancy guard: SSTORE prohibited when gas_left ≤ CALL_STIPEND.
        if (rules.HasEip2200Reentrancy)
        {
            var gasRemaining = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;
            if (gasRemaining <= 2300UL)
                return (ExecutionResult.Failure(EvmError.OutOfGas, gasRemaining), context.ProgramCounter + 1);
        }

        if (!context.Stack.TryPop(out var key) || !context.Stack.TryPop(out var value))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var slotKey      = (context.StorageAddress, key);
        var currentValue = await context.Storage.LoadAsync(key);

        // Capture original value (Istanbul+ EIP-2200 tri-state metering).
        if (rules.HasEip2200Reentrancy && !context.OriginalStorageValues.ContainsKey(slotKey))
            context.OriginalStorageValues[slotKey] = currentValue;

        var originalValue = rules.HasEip2200Reentrancy
            ? context.OriginalStorageValues[slotKey]
            : currentValue; // pre-Istanbul: no tri-state, original = current

        // EIP-2929 (Berlin+): cold surcharge on top of EIP-2200 base cost.
        bool isWarm     = !rules.HasEip2929WarmCold || context.Access.TouchSlot(context.StorageAddress, key);
        ulong coldCost  = (rules.HasEip2929WarmCold && !isWarm) ? 2_100UL : 0UL;

        var (baseCost, refundDelta) = rules.SstoreBaseCost(originalValue, currentValue, value);
        ulong totalCost = coldCost + baseCost;

        context.GasRefundCounter += refundDelta;
        context.Storage.Store(key, value);
        context.TraceStorageWrite(key, value);

        return (ExecutionResult.Success(totalCost), context.ProgramCounter + 1);
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
