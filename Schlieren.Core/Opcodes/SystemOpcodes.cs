using System.Numerics;
using System.Security.Cryptography;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Core.Opcodes;

public sealed class OpcodeCreate : IOpcode
{
    public byte Code => 0xF0;
    public string Name => "CREATE";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.IsStatic)
             return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

        if (!context.Stack.TryPop(out var value) || 
            !context.Stack.TryPop(out var offset) || 
            !context.Stack.TryPop(out var length))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        // EIP-3860 (Shanghai+) – init code must not exceed 2 × MAX_CODE_SIZE (49152 bytes).
        if (context.Block.Rules.HasEip3860InitcodeLimit && length > 2 * 24576)
            return (ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1);

        var offsetInt = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var lengthInt = length > int.MaxValue ? int.MaxValue : (int)length;

        // Memory expansion gas for reading initcode from memory.
        if (lengthInt > 0)
        {
            var memExpGas = context.Memory.CalculateGasCost(offsetInt + lengthInt);
            context.ConsumeGas(memExpGas, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);
        }

        // EIP-3860 (Shanghai+) word gas: 2 per 32-byte word of init code, charged before execution.
        var initCodeWordGas = context.Block.Rules.HasEip3860InitcodeLimit
            ? 2UL * ((ulong)(lengthInt + 31) / 32)
            : 0UL;
        // Base CREATE gas: 32000
        context.ConsumeGas(32000 + initCodeWordGas, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);

        var initCode = context.Memory.Load(offsetInt, lengthInt);

        // Derive address
        var nonce = await context.GlobalState.GetNonceAsync(context.ContractAddress, ct);
        var newAddress = CryptoUtils.DeriveContractAddress(context.ContractAddress, nonce);

        // EELS pre-checks (identical to the balance/nonce/depth guards in EELS create opcode):
        //   • sender.balance < endowment
        //   • sender.nonce == u64::MAX  (would overflow if bumped)
        //   • depth exceeded
        // On any of these: refund forwarded gas, push 0, no nonce bump.
        // IMPORTANT: DO NOT warm contractAddress here — EELS only adds to accessed_addresses
        // AFTER these checks pass (line 99 of generic_create). Warming before the check
        // poisons the access list, causing downstream BALANCE/EXTCODE to use warm cost (100)
        // instead of cold (2600), producing a 2500-gas delta.
        var senderBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
        var parentGasBeforeChild = context.GasLimit - context.GasUsed;
        // Frontier/Homestead: forward ALL remaining gas (no 63/64).
        // TangerineWhistle+ (HasPreEip150CallGas=false): 63/64 cap (EIP-150).
        var forwardedGas = context.Block.Rules.HasPreEip150CallGas
            ? parentGasBeforeChild
            : parentGasBeforeChild - (parentGasBeforeChild / 64UL);

        if (senderBalance < value || nonce == ulong.MaxValue || context.CallDepth >= 1024)
        {
            // EIP-211: Clear return data buffer even on early-exit pre-checks.
            context.LastReturnData = Array.Empty<byte>();
            context.Stack.TryPush(0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        // EIP-2929: warm the contract address AFTER pre-checks pass (EELS generic_create line 99)
        context.Access.WarmAddress(newAddress);

        // Increment nonce of creator (after guards pass)
        context.GlobalState.SetNonce(context.ContractAddress, nonce + 1);

        // Charge only the gas that enters the child frame.
        context.ConsumeGas(forwardedGas, GasSemantics.Allocation, GasComponents.CallForwarded);

        // EIP-211: Clear return data buffer before any call-like operation
        context.LastReturnData = Array.Empty<byte>();

        // EELS account_deployable() / EIP-7610: CREATE collides when destination has
        // nonzero nonce, existing code, or any storage. Message gas already reserved
        // (EIP-150) remains consumed; creator nonce already bumped.
        if (!await AccountDeployability.IsDeployableAsync(context.GlobalState, newAddress, ct))
        {
            context.Stack.TryPush(0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        // Construct internal tx for creation
        var tx = new Transaction
        {
            From = context.ContractAddress,
            To = null, // Contract creation
            Value = value,
            Data = initCode,
            GasLimit = forwardedGas, // EIP-150: 63/64 of parent's available gas
            GasPrice = context.GasPrice,
            Nonce = nonce,
            Authorization = TransactionAuthorization.Internal,
            EnableTracing = context.CaptureTrace
        };

        // SubCall with commit=false: initcode state changes go into a sub-overlay
        // that is NEVER committed. Only manual writes to context.GlobalState persist.
        var result = await context.SubCall(tx, false, newAddress, null);
        if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
        if (result.IsSuccess && result.Logs.Count > 0) context.Logs.AddRange(result.Logs);

        // EIP-211: Only capture return data on revert (failure), not on success
        // Successful CREATE/CREATE2 does NOT set return data - deployed code is separate
        if (!result.IsSuccess)
            context.LastReturnData = result.ReturnData;

        // Calculate unused gas from child
        var childRemaining = forwardedGas > result.GasUsed ? forwardedGas - result.GasUsed : 0UL;

        if (result.IsSuccess)
        {
            // Yellow Paper: charge 200 gas per byte of deployed runtime code BEFORE refunding.
            var runtimeCode = result.ReturnData;
            var codeDepositCost = checked((ulong)runtimeCode.Length * 200UL);

            // EIP-170: oversized code → ExceptionalHalt. Revert creation account only
            // (sub-call overlay already discarded initcode state changes).
            const int maxCodeSize = 24576;
            if (runtimeCode.Length > maxCodeSize)
            {
                await CreateRevertHelper.RevertCreationAccount(context, newAddress, ct);
                context.Stack.TryPush(0);
                // No RefundGas — child gas is consumed by exceptional halt.
            }
            else if (childRemaining < codeDepositCost)
            {
                // Frontier (pre-EIP-2): deposit OOG → deploy empty code and succeed (Yellow Paper §7).
                // Homestead+ (EIP-2): deposit OOG → ExceptionalHalt, revert account.
                if (!context.Block.Rules.HasCreateDepositOogHalt)
                {
                    context.GlobalState.SetCode(newAddress, Array.Empty<byte>());
                    context.CommitLastCreateTransient?.Invoke();
                    context.CommitLastCreateTransient   = null;
                    context.RollbackLastCreateTransient = null;
                    context.GasRefundCounter += result.GasRefundCounter;
                    context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn); // Frontier: child gas is NOT burned
                    context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
                }
                else
                {
                    await CreateRevertHelper.RevertCreationAccount(context, newAddress, ct);
                    context.Stack.TryPush(0);
                    // No RefundGas — child gas consumed by exceptional halt.
                }
            }
            else
            {
                // EIP-3541 (London+): EF-prefix → ExceptionalHalt. Revert creation account only.
                if (context.Block.Rules.HasEip3541EfPrefix && runtimeCode.Length > 0 && runtimeCode[0] == 0xEF)
                {
                    await CreateRevertHelper.RevertCreationAccount(context, newAddress, ct);
                    context.Stack.TryPush(0);
                    // Consume remaining child gas, do not refund.
                }
                else
                {
                    // Deduct code deposit cost from child's remaining gas.
                    childRemaining -= codeDepositCost;

                    // Install runtime code.
                    context.GlobalState.SetCode(newAddress, runtimeCode);

                    // EIP-1153: CREATE succeeded — commit staging transient overlay.
                    context.CommitLastCreateTransient?.Invoke();
                    context.CommitLastCreateTransient   = null;
                    context.RollbackLastCreateTransient = null;

                    context.GasRefundCounter += result.GasRefundCounter;

                    // Refund remaining gas to parent.
                    context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn);

                    // Push created address.
                    context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
                }
            }
        }
        else
        {
            // Init code failed (REVERT/OOG during execution): the sub-call overlay was
            // never committed so its writes are already gone. Just refund gas, push 0.
            // No snapshot restore needed — the overlay architecture handles revert.
            context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn);
            context.Stack.TryPush(0);
        }

        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
    }
}

/// <summary>
/// Reverts the creation account on CREATE/CREATE2 failure (EIP-170/EIP-3541/deposit OOG).
/// Undoes value transfer, nonce bump, and code that were manually written to GlobalState
/// after SubCall returned. Sub-call overlay changes are already discarded (commit=false).
/// Also rolls back any transient-storage writes from the failed CREATE frame (EIP-1153).
/// </summary>
internal static class CreateRevertHelper
{
    public static async ValueTask RevertCreationAccount(
        ExecutionContext context, Address newAddress, CancellationToken ct)
    {
        var bal = await context.GlobalState.GetBalanceAsync(newAddress, ct);
        if (bal > 0)
        {
            var callerBal = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
            context.GlobalState.SetBalance(newAddress, BigInteger.Zero);
            context.GlobalState.SetBalance(context.ContractAddress, callerBal + bal);
        }
        context.GlobalState.SetNonce(newAddress, 0);
        context.GlobalState.SetCode(newAddress, Array.Empty<byte>());
        context.GlobalState.DeleteAccount(newAddress);

        // EIP-1153: roll back transient-storage writes from this failed CREATE frame.
        context.RollbackLastCreateTransient?.Invoke();
        context.RollbackLastCreateTransient = null;
        context.CommitLastCreateTransient   = null;
    }
}

public sealed class OpcodeCall : IOpcode
{
    public byte Code => 0xF1;
    public string Name => "CALL";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var gas) || 
            !context.Stack.TryPop(out var addr) || 
            !context.Stack.TryPop(out var value) ||
            !context.Stack.TryPop(out var argsOffset) ||
            !context.Stack.TryPop(out var argsLength) ||
            !context.Stack.TryPop(out var retOffset) ||
            !context.Stack.TryPop(out var retLength))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        if (context.IsStatic && !value.IsZero)
             return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

        // Gap 8: Clear last return data at call entry (EIP-211 §3)
        context.LastReturnData = Array.Empty<byte>();

        // Extract address
        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var toAddress = new Address(padded);

        var argsOffsetInt = argsOffset > int.MaxValue ? int.MaxValue : (int)argsOffset;
        var argsLengthInt = argsLength > int.MaxValue ? int.MaxValue : (int)argsLength;
        var retOffsetInt = retOffset > int.MaxValue ? int.MaxValue : (int)retOffset;
        var retLengthInt = retLength > int.MaxValue ? int.MaxValue : (int)retLength;

        // Calculate memory expansion cost for both input and return data regions
        var maxInputEnd = argsLengthInt > 0 ? (long)argsOffsetInt + argsLengthInt : 0L;
        var maxReturnEnd = retLengthInt > 0 ? (long)retOffsetInt + retLengthInt : 0L;
        var maxMemoryAccess = (int)Math.Min(Math.Max(maxInputEnd, maxReturnEnd), int.MaxValue);
        var memoryCost = context.Memory.CalculateGasCost(maxMemoryAccess);


        context.ConsumeGas(memoryCost, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);
        context.Memory.Expand(maxMemoryAccess);

        // Load input data
        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);


        // EIP-2929 / pre-Berlin: CALL access cost = CallBaseCost + ExtAccountCost(isWarm)
        // Berlin: CallBaseCost=0, ExtAccountCost=warm/cold
        // TangerineWhistle–Istanbul: CallBaseCost=700, ExtAccountCost NOT added (700 is all-in)
        var rules = context.Block.Rules;
        var isWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(toAddress) : true;
        // Only add ExtAccountCost separately for Berlin+ (warm/cold world); pre-Berlin it's absorbed into CallBaseCost
        ulong accessCost = rules.HasEip2929WarmCold
            ? rules.CallBaseCost + rules.ExtAccountCost(isWarm)
            : rules.CallBaseCost;

        // EIP-7702: if the callee has a delegation designator (0xEF0100 || addr),
        // charge warm/cold access for the DELEGATE address (EELS access_delegation()).
        // This is part of extra_gas charged from the caller, not the callee's budget.
        if (context.Block?.Rules.HasEip7702SetCode == true)
        {
            var calleeCode = await context.GlobalState.GetCodeAsync(toAddress, ct);
            if (calleeCode.Length == 23 && calleeCode[0] == 0xEF && calleeCode[1] == 0x01 && calleeCode[2] == 0x00)
            {
                var delegateAddr = new Address(calleeCode[3..]);
                bool delegateIsWarm = context.Access.TouchAddress(delegateAddr);
                accessCost += rules.ExtAccountCost(delegateIsWarm);
            }
        }
        ulong valueTransferCost = value.IsZero ? 0UL : 9000UL;

        // Gap 3: EIP-161 new-account surcharge – 25000 if callee is empty and value > 0.
        ulong newAccountCost = 0;
        if (!value.IsZero)
        {
            var calleeCode = await context.GlobalState.GetCodeAsync(toAddress, ct);
            var calleeBalance = await context.GlobalState.GetBalanceAsync(toAddress, ct);
            var calleeNonce = await context.GlobalState.GetNonceAsync(toAddress, ct);
            bool isEmpty = calleeCode.Length == 0 && calleeBalance == 0 && calleeNonce == 0;
            if (isEmpty) newAccountCost = 25_000;
        }

        ulong extraCost = accessCost + valueTransferCost + newAccountCost;

        // ── Pre-EIP-150 (Frontier/Homestead) ────────────────────────────────
        // Parent pays CALL_BASE + gas_arg + new_account_if_nonexistent + 9000_if_value.
        // Child receives exactly gas_arg (+ stipend).  No 63/64 rule.
        // New account cost applies when account doesn't exist (regardless of value).
        if (rules.HasPreEip150CallGas)
        {
            // In Frontier the new-account surcharge is NOT gated on value > 0.
            // Re-compute: account non-existence alone triggers the 25000 cost.
            if (value.IsZero)
            {
                // Check existence regardless (Frontier yellow-paper §9.2)
                var calleeBalance2 = await context.GlobalState.GetBalanceAsync(toAddress, ct);
                var calleeCode2    = await context.GlobalState.GetCodeAsync(toAddress, ct);
                var calleeNonce2   = await context.GlobalState.GetNonceAsync(toAddress, ct);
                bool exists = calleeBalance2 > 0 || calleeCode2.Length > 0 || calleeNonce2 > 0;
                if (!exists) newAccountCost = 25_000;
            }

            var requestedGasFrontier = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
            // Parent pays: base + gas_arg + value_cost + new_account_cost (no ExtAccountCost)
            // In Frontier CallBaseCost=40 and ExtAccountCost is NOT part of CALL overhead.
            ulong frontierCost = rules.CallBaseCost + requestedGasFrontier + valueTransferCost + newAccountCost;
            context.ConsumeGas(frontierCost);
            context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallLocal,
                rules.CallBaseCost + valueTransferCost + newAccountCost, GasSemantics.ExclusiveCharge);
            context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallForwarded,
                requestedGasFrontier, GasSemantics.Allocation);
            var stipendFrontier = value.IsZero ? 0UL : 2300UL;
            var childGasLimitFrontier = requestedGasFrontier + stipendFrontier;

            // Balance check
            if (!value.IsZero)
            {
                var callerBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
                if (callerBalance < value)
                {
                    context.RefundGas(requestedGasFrontier + stipendFrontier,
                        GasSemantics.Return, GasComponents.CallUnusedReturn);
                    context.Stack.TryPush(0);
                    return (ExecutionResult.Success(0), context.ProgramCounter + 1);
                }
            }

            ExecutionResult frontierResult;
            if (Precompiles.IsPrecompile(toAddress, context.Block.Rules))
            {
                frontierResult = Precompiles.ExecuteAsResult(toAddress, input, childGasLimitFrontier, context.Block.Rules);
                // Frontier: touch the precompile address (creates empty account if non-existent)
                if (frontierResult.IsSuccess)
                    await context.GlobalState.TouchAccountAsync(toAddress, ct);
                if (frontierResult.IsSuccess && value > 0)
                {
                    var callerBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
                    if (callerBalance < value)
                    {
                        frontierResult = ExecutionResult.Failure(EvmError.InsufficientFunds);
                    }
                    else
                    {
                        var benBalance = await context.GlobalState.GetBalanceAsync(toAddress, ct);
                        context.GlobalState.SetBalance(context.ContractAddress, callerBalance - value);
                        context.GlobalState.SetBalance(toAddress, benBalance + value);
                    }
                }
            }
            else if (context.SubCall == null)
            {
                return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);
            }
            else
            {
                var tx = new Transaction
                {
                    From = context.ContractAddress,
                    To = toAddress,
                    Value = value,
                    Data = input,
                    GasLimit = childGasLimitFrontier,
                    GasPrice = context.GasPrice,
                    Authorization = TransactionAuthorization.Internal,
                    EnableTracing = context.CaptureTrace
                };
                frontierResult = await context.SubCall(tx, context.IsStatic, null, null);
                if (frontierResult.TraceSteps.Count > 0) context.TraceSteps.AddRange(frontierResult.TraceSteps);
                if (frontierResult.IsSuccess && frontierResult.Logs.Count > 0) context.Logs.AddRange(frontierResult.Logs);
            }

            var childUsedFrontier = frontierResult.GasUsed > childGasLimitFrontier ? childGasLimitFrontier : frontierResult.GasUsed;
            var childRemainingFrontier = childGasLimitFrontier > childUsedFrontier ? childGasLimitFrontier - childUsedFrontier : 0UL;
            context.RefundGas(childRemainingFrontier, GasSemantics.Return, GasComponents.CallUnusedReturn);
            if (frontierResult.IsSuccess) context.GasRefundCounter += frontierResult.GasRefundCounter;
            context.LastReturnData = frontierResult.ReturnData;
            var copyLenF = Math.Min(retLengthInt, frontierResult.ReturnData.Length);
            if (copyLenF > 0) context.Memory.Store(retOffsetInt, frontierResult.ReturnData[..copyLenF]);
            context.Stack.TryPush(frontierResult.IsSuccess ? 1 : 0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        // ── Post-EIP-150 (TangerineWhistle+): 63/64 gas forwarding ──────────

        // Gap 1: EIP-150 – forward at most 63/64 of remaining gas (after extra costs).
        // EELS: if gas_left < extra_gas + memory_cost → OOG path (charge_gas will throw).
        // Guard against ulong underflow: if remaining < extraCost there is nothing to forward.
        var remaining = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;
        var availableAfterExtras = remaining > extraCost ? remaining - extraCost : 0UL;
        var maxForward = availableAfterExtras - availableAfterExtras / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var forwardedGas = Math.Min(requestedGas, maxForward);


        // Parent pays forwarded gas + extra costs (but NOT the stipend).
        context.ConsumeGas(forwardedGas + extraCost);
        context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallLocal,
            extraCost, GasSemantics.ExclusiveCharge);
        context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallForwarded,
            forwardedGas, GasSemantics.Allocation);

        // Value-bearing calls receive a 2,300 gas stipend
        // On a pre-execution failure, EELS returns that full allocation even though
        // only the forwarded portion was charged to the parent.
        var stipend = value.IsZero ? 0UL : 2300UL;

        // Gap 4: Check caller balance BEFORE issuing sub-call.
        if (!value.IsZero)
        {
            var callerBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
            if (callerBalance < value)
            {
                // The CALL extras remain charged; return the unused child allocation.
                // EELS: evm.gas_left += message_call_gas.sub_call; evm.return_data = b""
                context.RefundGas(forwardedGas + stipend,
                    GasSemantics.Return, GasComponents.CallUnusedReturn);
                context.LastReturnData = Array.Empty<byte>();
                context.Stack.TryPush(0);
                return (ExecutionResult.Success(0), context.ProgramCounter + 1);
            }
        }

        // Gap 2: Add 2300 call stipend when value > 0 (EELS: stipend added to child, not charged to parent).
        var childGasLimit = forwardedGas + stipend;

        ExecutionResult result;
        if (Precompiles.IsPrecompile(toAddress, context.Block.Rules))
        {
            result = Precompiles.ExecuteAsResult(toAddress, input, childGasLimit, context.Block.Rules);
            if (result.IsSuccess && value > 0)
            {
                var callerBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
                if (callerBalance < value)
                {
                    result = ExecutionResult.Failure(EvmError.InsufficientFunds);
                }
                else
                {
                    var calleeBalance = await context.GlobalState.GetBalanceAsync(toAddress, ct);
                    context.GlobalState.SetBalance(context.ContractAddress, callerBalance - value);
                    context.GlobalState.SetBalance(toAddress, calleeBalance + value);
                }
            }
        }
        else
        {
            if (context.SubCall == null)
                return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

            var tx = new Transaction
            {
                From = context.ContractAddress,
                To = toAddress,
                Value = value,
                Data = input,
                GasLimit = childGasLimit,
                GasPrice = context.GasPrice,
                Authorization = TransactionAuthorization.Internal,
                EnableTracing = context.CaptureTrace
            };

            result = await context.SubCall(tx, context.IsStatic, null, null);
            if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
            if (result.IsSuccess && result.Logs.Count > 0) context.Logs.AddRange(result.Logs);
        }

        // EELS refund semantics: return ALL unused child gas to parent.
        // The stipend was added to childGasLimit but never charged to parent,
        // so EELS `evm.gas_left += child_evm.gas_left` naturally handles it.
        var childUsed = result.GasUsed > childGasLimit ? childGasLimit : result.GasUsed;
        var childRemaining = childGasLimit > childUsed ? childGasLimit - childUsed : 0UL;
        
        
        context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn);
        if (result.IsSuccess)
        {
            context.GasRefundCounter += result.GasRefundCounter;
        }

        // Handle return data
        context.LastReturnData = result.ReturnData;
        
        // Copy return data to memory
        var copyLen = Math.Min(retLengthInt, result.ReturnData.Length);
        if (copyLen > 0)
        {
            var dataToCopy = new byte[copyLen];
            Array.Copy(result.ReturnData, 0, dataToCopy, 0, copyLen);
            context.Memory.Store(retOffsetInt, dataToCopy);
        }

        context.Stack.TryPush(result.IsSuccess ? 1 : 0);
        
        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeCreate2 : IOpcode
{
    public byte Code => 0xF5;
    public string Name => "CREATE2";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasCreate2)
            return (ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit), context.ProgramCounter + 1);

        if (context.IsStatic)
             return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

        if (!context.Stack.TryPop(out var value) || 
            !context.Stack.TryPop(out var offset) || 
            !context.Stack.TryPop(out var length) ||
            !context.Stack.TryPop(out var salt))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        // EIP-3860 (Shanghai+) – init code must not exceed 2 × MAX_CODE_SIZE (49152 bytes).
        if (context.Block.Rules.HasEip3860InitcodeLimit && length > 2 * 24576)
            return (ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1);

        var offsetInt = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var lengthInt = length > int.MaxValue ? int.MaxValue : (int)length;

        // Memory expansion gas for reading initcode from memory.
        if (lengthInt > 0)
        {
            var memExpGas = context.Memory.CalculateGasCost(offsetInt + lengthInt);
            context.ConsumeGas(memExpGas, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);
        }

        // EIP-3860 (Shanghai+) word gas: 2 per 32-byte word of init code.
        var initCodeWordGas = context.Block.Rules.HasEip3860InitcodeLimit
            ? 2UL * ((ulong)(lengthInt + 31) / 32)
            : 0UL;
        // Base CREATE2 gas: 32000 + hash gas (6 per word)
        var hashWordGas = 6UL * ((ulong)(lengthInt + 31) / 32);
        context.ConsumeGas(32000 + initCodeWordGas + hashWordGas,
            GasSemantics.ExclusiveCharge, GasComponents.CallLocal);

        var initCode = context.Memory.Load(offsetInt, lengthInt);
        
        // Ensure salt is exactly 32 bytes (big endian)
        var saltBytes = salt.ToByteArray(isUnsigned: true, isBigEndian: true);
        var paddedSalt = new byte[32];
        if (saltBytes.Length > 32) Array.Copy(saltBytes, saltBytes.Length - 32, paddedSalt, 0, 32);
        else Array.Copy(saltBytes, 0, paddedSalt, 32 - saltBytes.Length, saltBytes.Length);

        // Derive address using salt
        var newAddress = CryptoUtils.DeriveContractAddress2(context.ContractAddress, paddedSalt, initCode);

        // EELS pre-checks before nonce bump (balance, nonce overflow, depth)
        // IMPORTANT: DO NOT warm contractAddress before these checks — same as CREATE.
        var nonce = await context.GlobalState.GetNonceAsync(context.ContractAddress, ct);
        var senderBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
        var parentGasBeforeChild = context.GasLimit - context.GasUsed;
        // Frontier/Homestead: forward ALL remaining gas (no 63/64).
        // TangerineWhistle+ (HasPreEip150CallGas=false): 63/64 cap (EIP-150).
        var forwardedGas = context.Block.Rules.HasPreEip150CallGas
            ? parentGasBeforeChild
            : parentGasBeforeChild - (parentGasBeforeChild / 64UL);

        if (senderBalance < value || nonce == ulong.MaxValue || context.CallDepth >= 1024)
        {
            // EIP-211: Clear return data buffer even on early-exit pre-checks.
            context.LastReturnData = Array.Empty<byte>();
            context.Stack.TryPush(0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        // EIP-2929: warm the contract address AFTER pre-checks pass (EELS generic_create line 99)
        context.Access.WarmAddress(newAddress);

        // Increment nonce of creator (after guards pass)
        context.GlobalState.SetNonce(context.ContractAddress, nonce + 1);

        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        // Charge only the gas that enters the child frame.
        context.ConsumeGas(forwardedGas, GasSemantics.Allocation, GasComponents.CallForwarded);

        // EIP-211: Clear return data buffer before any call-like operation
        context.LastReturnData = Array.Empty<byte>();

        // EELS account_deployable() / EIP-7610: CREATE2 collides when destination
        // has nonzero nonce, existing code, or any storage. Message gas reserved.
        if (!await AccountDeployability.IsDeployableAsync(context.GlobalState, newAddress, ct))
        {
            context.Stack.TryPush(0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        var tx = new Transaction
        {
            From = context.ContractAddress,
            To = null,
            Value = value,
            Data = initCode,
            GasLimit = forwardedGas, // EIP-150: 63/64 of parent's available gas
            GasPrice = context.GasPrice,
            Nonce = nonce,
            Authorization = TransactionAuthorization.Internal,
            EnableTracing = context.CaptureTrace
        };

        // SubCall with commit=false: initcode state changes are isolated.
        var result = await context.SubCall(tx, false, newAddress, null);
        if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
        if (result.IsSuccess && result.Logs.Count > 0) context.Logs.AddRange(result.Logs);

        // EIP-211: Only capture return data on revert (failure), not on success
        // Successful CREATE/CREATE2 does NOT set return data - deployed code is separate
        if (!result.IsSuccess)
            context.LastReturnData = result.ReturnData;

        // Calculate unused gas from child
        var childRemaining = forwardedGas > result.GasUsed ? forwardedGas - result.GasUsed : 0UL;

        if (result.IsSuccess)
        {
            // Yellow Paper: charge 200 gas per byte of deployed runtime code BEFORE refunding.
            var runtimeCode = result.ReturnData;
            var codeDepositCost = checked((ulong)runtimeCode.Length * 200UL);

            // EIP-170: oversized code → ExceptionalHalt. Revert creation account only
            // (sub-call overlay already discarded initcode state changes).
            const int maxCodeSize = 24576;
            if (runtimeCode.Length > maxCodeSize)
            {
                await CreateRevertHelper.RevertCreationAccount(context, newAddress, ct);
                context.Stack.TryPush(0);
                // No RefundGas — child gas is consumed by exceptional halt.
            }
            else if (childRemaining < codeDepositCost)
            {
                // Frontier (pre-EIP-2): deposit OOG → deploy empty code and succeed (Yellow Paper §7).
                // Homestead+ (EIP-2): deposit OOG → ExceptionalHalt, revert account.
                if (!context.Block.Rules.HasCreateDepositOogHalt)
                {
                    context.GlobalState.SetCode(newAddress, Array.Empty<byte>());
                    context.CommitLastCreateTransient?.Invoke();
                    context.CommitLastCreateTransient   = null;
                    context.RollbackLastCreateTransient = null;
                    context.GasRefundCounter += result.GasRefundCounter;
                    context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn); // Frontier: child gas is NOT burned
                    context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
                }
                else
                {
                    await CreateRevertHelper.RevertCreationAccount(context, newAddress, ct);
                    context.Stack.TryPush(0);
                    // No RefundGas — child gas consumed by exceptional halt.
                }
            }
            else
            {
                // EIP-3541 (London+): EF-prefix → ExceptionalHalt. Revert creation account only.
                if (context.Block.Rules.HasEip3541EfPrefix && runtimeCode.Length > 0 && runtimeCode[0] == 0xEF)
                {
                    await CreateRevertHelper.RevertCreationAccount(context, newAddress, ct);
                    context.Stack.TryPush(0);
                    // Consume remaining child gas, do not refund.
                }
                else
                {
                    // Deduct code deposit cost from child's remaining gas.
                    childRemaining -= codeDepositCost;

                    // Install runtime code.
                    context.GlobalState.SetCode(newAddress, runtimeCode);

                    // EIP-1153: CREATE succeeded — commit staging transient overlay.
                    context.CommitLastCreateTransient?.Invoke();
                    context.CommitLastCreateTransient   = null;
                    context.RollbackLastCreateTransient = null;

                    context.GasRefundCounter += result.GasRefundCounter;

                    // Refund remaining gas to parent.
                    context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn);

                    // Push created address.
                    context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
                }
            }
        }
        else
        {
            // Init code failed (REVERT/OOG): overlay handles revert, just refund gas.
            context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn);
            context.Stack.TryPush(0);
        }

        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeStaticCall : IOpcode
{
    public byte Code => 0xFA;
    public string Name => "STATICCALL";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasStaticCall)
            return (ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit), context.ProgramCounter + 1);

        if (!context.Stack.TryPop(out var gas) || 
            !context.Stack.TryPop(out var addr) || 
            !context.Stack.TryPop(out var argsOffset) ||
            !context.Stack.TryPop(out var argsLength) ||
            !context.Stack.TryPop(out var retOffset) ||
            !context.Stack.TryPop(out var retLength))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        // Gap 8: Clear last return data at call entry (EIP-211 §3)
        context.LastReturnData = Array.Empty<byte>();

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var toAddress = new Address(padded);

        var argsOffsetInt = argsOffset > int.MaxValue ? int.MaxValue : (int)argsOffset;
        var argsLengthInt = argsLength > int.MaxValue ? int.MaxValue : (int)argsLength;
        var retOffsetInt = retOffset > int.MaxValue ? int.MaxValue : (int)retOffset;
        var retLengthInt = retLength > int.MaxValue ? int.MaxValue : (int)retLength;

        // Calculate memory expansion cost for both input and return data regions
        var maxInputEnd = argsLengthInt > 0 ? (long)argsOffsetInt + argsLengthInt : 0L;
        var maxReturnEnd = retLengthInt > 0 ? (long)retOffsetInt + retLengthInt : 0L;
        var maxMemoryAccess = (int)Math.Min(Math.Max(maxInputEnd, maxReturnEnd), int.MaxValue);
        var memoryCost = context.Memory.CalculateGasCost(maxMemoryAccess);
        context.ConsumeGas(memoryCost, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);
        context.Memory.Expand(maxMemoryAccess);

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929 / pre-Berlin: STATICCALL access cost = CallBaseCost + ExtAccountCost(isWarm)
        // TangerineWhistle–Istanbul: CallBaseCost=700 is all-in; don't add ExtAccountCost
        var rules = context.Block.Rules;
        var isWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(toAddress) : true;
        ulong accessCost = rules.HasEip2929WarmCold
            ? rules.CallBaseCost + rules.ExtAccountCost(isWarm)
            : rules.CallBaseCost;

        // EIP-7702: if the target has a delegation designator (0xEF0100 || addr),
        // charge warm/cold access for the DELEGATE address (EELS access_delegation()).
        if (context.Block?.Rules.HasEip7702SetCode == true)
        {
            var calleeCode = await context.GlobalState.GetCodeAsync(toAddress, ct);
            if (calleeCode.Length == 23 && calleeCode[0] == 0xEF && calleeCode[1] == 0x01 && calleeCode[2] == 0x00)
            {
                var delegateAddr = new Address(calleeCode[3..]);
                bool delegateIsWarm = context.Access.TouchAddress(delegateAddr);
                accessCost += rules.ExtAccountCost(delegateIsWarm);
            }
        }

        context.ConsumeGas(accessCost, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);

        // Gap 1: EIP-150 – forward at most 63/64 of remaining gas.
        var remaining = context.GasLimit - context.GasUsed;
        var maxForward = remaining - remaining / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var gasLimit = Math.Min(requestedGas, maxForward);

        context.ConsumeGas(gasLimit, GasSemantics.Allocation, GasComponents.CallForwarded);

        ExecutionResult result;
        if (Precompiles.IsPrecompile(toAddress, context.Block.Rules))
        {
            result = Precompiles.ExecuteAsResult(toAddress, input, gasLimit, context.Block.Rules);
        }
        else
        {
            if (context.SubCall == null)
                return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

            var tx = new Transaction
            {
                From = context.ContractAddress,
                To = toAddress,
                Value = 0, // STATICCALL MUST NOT send value
                Data = input,
                GasLimit = gasLimit,
                GasPrice = context.GasPrice,
                Authorization = TransactionAuthorization.Internal,
                EnableTracing = context.CaptureTrace
            };

            result = await context.SubCall(tx, true, null, null);
            if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
            if (result.IsSuccess && result.Logs.Count > 0) context.Logs.AddRange(result.Logs);
        }

        var childUsed = result.GasUsed > gasLimit ? gasLimit : result.GasUsed;
        context.RefundGas(gasLimit > childUsed ? gasLimit - childUsed : 0UL,
            GasSemantics.Return, GasComponents.CallUnusedReturn);
        if (result.IsSuccess)
        {
            context.GasRefundCounter += result.GasRefundCounter;
        }
        context.LastReturnData = result.ReturnData;
        
        var copyLen = Math.Min(retLengthInt, result.ReturnData.Length);
        if (copyLen > 0)
        {
            var dataToCopy = new byte[copyLen];
            Array.Copy(result.ReturnData, 0, dataToCopy, 0, copyLen);
            context.Memory.Store(retOffsetInt, dataToCopy);
        }

        context.Stack.TryPush(result.IsSuccess ? 1 : 0);
        
        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeCallCode : IOpcode
{
    public byte Code => 0xF2;
    public string Name => "CALLCODE";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var gas) || 
            !context.Stack.TryPop(out var addr) || 
            !context.Stack.TryPop(out var value) ||
            !context.Stack.TryPop(out var argsOffset) ||
            !context.Stack.TryPop(out var argsLength) ||
            !context.Stack.TryPop(out var retOffset) ||
            !context.Stack.TryPop(out var retLength))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        // Gap 8: Clear last return data at call entry (EIP-211 §3)
        context.LastReturnData = Array.Empty<byte>();

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var codeAddress = new Address(padded);

        var argsOffsetInt = argsOffset > int.MaxValue ? int.MaxValue : (int)argsOffset;
        var argsLengthInt = argsLength > int.MaxValue ? int.MaxValue : (int)argsLength;
        var retOffsetInt = retOffset > int.MaxValue ? int.MaxValue : (int)retOffset;
        var retLengthInt = retLength > int.MaxValue ? int.MaxValue : (int)retLength;

        // Calculate memory expansion cost for both input and return data regions
        var maxInputEnd = argsLengthInt > 0 ? (long)argsOffsetInt + argsLengthInt : 0L;
        var maxReturnEnd = retLengthInt > 0 ? (long)retOffsetInt + retLengthInt : 0L;
        var maxMemoryAccess = (int)Math.Min(Math.Max(maxInputEnd, maxReturnEnd), int.MaxValue);
        var memoryCost = context.Memory.CalculateGasCost(maxMemoryAccess);
        context.ConsumeGas(memoryCost, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);
        context.Memory.Expand(maxMemoryAccess);

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929 / pre-Berlin: DELEGATECALL access cost = CallBaseCost + ExtAccountCost(isWarm)
        // TangerineWhistle–Istanbul: CallBaseCost=700 is all-in; don't add ExtAccountCost
        var rules = context.Block.Rules;
        var isCodeWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(codeAddress) : true;
        ulong accessCost = rules.HasEip2929WarmCold
            ? rules.CallBaseCost + rules.ExtAccountCost(isCodeWarm)
            : rules.CallBaseCost;
        // EIP-7702: if the code address has a delegation designator (0xEF0100 || addr),
        // charge warm/cold access for the DELEGATE address (EELS access_delegation()).
        // This is part of extra_gas charged from the caller, not the callee's budget.
        if (context.Block?.Rules.HasEip7702SetCode == true)
        {
            var calleeCode = await context.GlobalState.GetCodeAsync(codeAddress, ct);
            if (calleeCode.Length == 23 && calleeCode[0] == 0xEF && calleeCode[1] == 0x01 && calleeCode[2] == 0x00)
            {
                var delegateAddr = new Address(calleeCode[3..]);
                bool delegateIsWarm = context.Access.TouchAddress(delegateAddr);
                accessCost += rules.ExtAccountCost(delegateIsWarm);
            }
        }

        // Value-transfer cost: 9000 if value > 0 (EELS: transfer_gas_cost).
        ulong valueTransferCost = value.IsZero ? 0UL : 9000UL;

        ulong extraCost = accessCost + valueTransferCost;

        // ── Pre-EIP-150 (Frontier/Homestead) CALLCODE ────────────────────────
        // EELS Homestead callcode(): to = evm.message.current_target (self).
        // calculate_message_call_gas: cost = CALL_BASE + gas_arg + create_gas_cost(to) + transfer_gas_cost
        //   create_gas_cost(to=self) = 0 (self always exists)
        //   transfer_gas_cost = 9000 if value > 0 else 0
        //   sub_call = gas_arg + stipend (stipend = 2300 if value > 0 else 0)
        // Balance check happens AFTER gas charge; on fail, refund sub_call gas.
        if (rules.HasPreEip150CallGas)
        {
            var requestedGasPre = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
            var stipendPre = value.IsZero ? 0UL : 2300UL;
            // Cost: CALL_BASE + gas_arg + value_transfer_cost (create_gas_cost=0 since to=self)
            ulong frontierCallCodeCost = accessCost + requestedGasPre + valueTransferCost;
            context.ConsumeGas(frontierCallCodeCost); // OOGs if insufficient
            context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallLocal,
                accessCost + valueTransferCost, GasSemantics.ExclusiveCharge);
            context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallForwarded,
                requestedGasPre, GasSemantics.Allocation);

            // Balance check: CALLCODE sends value from self to self; reject if insufficient.
            if (!value.IsZero)
            {
                var callerBalancePre = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
                if (callerBalancePre < value)
                {
                    // Refund the sub_call allocation (gas_arg + stipend) to parent.
                    context.RefundGas(requestedGasPre + stipendPre,
                        GasSemantics.Return, GasComponents.CallUnusedReturn);
                    context.Stack.TryPush(0);
                    return (ExecutionResult.Success(0), context.ProgramCounter + 1);
                }
            }

            var childGasLimitPre = requestedGasPre + stipendPre;
            var tx = new Transaction
            {
                From = context.ContractAddress,
                To = context.ContractAddress,
                Value = value,
                Data = input,
                GasLimit = childGasLimitPre,
                GasPrice = context.GasPrice,
                Authorization = TransactionAuthorization.Internal,
                EnableTracing = context.CaptureTrace
            };
            if (context.SubCall == null)
                return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);
            var preResult = await context.SubCall(tx, context.IsStatic, null, codeAddress);
            if (preResult.TraceSteps.Count > 0) context.TraceSteps.AddRange(preResult.TraceSteps);
            if (preResult.IsSuccess && preResult.Logs.Count > 0) context.Logs.AddRange(preResult.Logs);
            var childUsedPre = preResult.GasUsed > childGasLimitPre ? childGasLimitPre : preResult.GasUsed;
            var childRemainingPre = childGasLimitPre > childUsedPre ? childGasLimitPre - childUsedPre : 0UL;
            context.RefundGas(childRemainingPre, GasSemantics.Return, GasComponents.CallUnusedReturn);
            if (preResult.IsSuccess) context.GasRefundCounter += preResult.GasRefundCounter;
            context.LastReturnData = preResult.ReturnData;
            var copyLenPre = Math.Min(retLengthInt, preResult.ReturnData.Length);
            if (copyLenPre > 0) { var buf = new byte[copyLenPre]; Array.Copy(preResult.ReturnData, 0, buf, 0, copyLenPre); context.Memory.Store(retOffsetInt, buf); }
            context.Stack.TryPush(preResult.IsSuccess ? 1 : 0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        // ── Post-EIP-150 (TangerineWhistle+): 63/64 gas forwarding ──────────
        var remaining = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;
        var availableAfterExtras = remaining > extraCost ? remaining - extraCost : 0UL;
        var maxForward = availableAfterExtras - availableAfterExtras / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var forwardedGas = Math.Min(requestedGas, maxForward);

        // Parent pays forwarded gas + extra costs (but NOT the stipend).
        context.ConsumeGas(forwardedGas + extraCost);
        context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallLocal,
            extraCost, GasSemantics.ExclusiveCharge);
        context.RecordGasComponent(GasComponentScope.Opcode, GasComponents.CallForwarded,
            forwardedGas, GasSemantics.Allocation);

        // Gap 4: Check caller balance BEFORE issuing sub-call.
        if (!value.IsZero)
        {
            var callerBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
            if (callerBalance < value)
            {
                // The CALLCODE extras (access + value-transfer cost) remain charged.
                // EELS: evm.gas_left += message_call_gas.sub_call  (sub_call = forwardedGas + stipend)
                var stipendEarly = 2300UL;
                context.RefundGas(forwardedGas + stipendEarly,
                    GasSemantics.Return, GasComponents.CallUnusedReturn);
                context.LastReturnData = Array.Empty<byte>();
                context.Stack.TryPush(0);
                return (ExecutionResult.Success(0), context.ProgramCounter + 1);
            }
        }

        // Gap 2: Add 2300 call stipend when value > 0 (EELS: stipend added to child, not charged to parent).
        var stipend = value.IsZero ? 0UL : 2300UL;
        var childGasLimit = forwardedGas + stipend;

        ExecutionResult result;
        if (Precompiles.IsPrecompile(codeAddress, context.Block.Rules))
        {
            result = Precompiles.ExecuteAsResult(codeAddress, input, childGasLimit, context.Block.Rules);
        }
        else
        {
            if (context.SubCall == null)
                 return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

            var tx = new Transaction
            {
                From = context.ContractAddress,
                To = context.ContractAddress,
                Value = value,
                Data = input,
                GasLimit = childGasLimit,
                GasPrice = context.GasPrice,
                Authorization = TransactionAuthorization.Internal,
                EnableTracing = context.CaptureTrace
            };

            // EELS: is_static = is_staticcall OR parent.is_static
            // CALLCODE is NOT a staticcall, but inherits parent's static flag.
            result = await context.SubCall(tx, context.IsStatic, null, codeAddress);
            if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
            if (result.IsSuccess && result.Logs.Count > 0) context.Logs.AddRange(result.Logs);
        }

        // EELS refund semantics: return ALL unused child gas to parent.
        var childUsed = result.GasUsed > childGasLimit ? childGasLimit : result.GasUsed;
        var childRemaining = childGasLimit > childUsed ? childGasLimit - childUsed : 0UL;
        context.RefundGas(childRemaining, GasSemantics.Return, GasComponents.CallUnusedReturn);
        if (result.IsSuccess)
        {
            context.GasRefundCounter += result.GasRefundCounter;
        }
        context.LastReturnData = result.ReturnData;
        
        var copyLen = Math.Min(retLengthInt, result.ReturnData.Length);
        if (copyLen > 0)
        {
            var dataToCopy = new byte[copyLen];
            Array.Copy(result.ReturnData, 0, dataToCopy, 0, copyLen);
            context.Memory.Store(retOffsetInt, dataToCopy);
        }

        context.Stack.TryPush(result.IsSuccess ? 1 : 0);
        
        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeDelegateCall : IOpcode
{
    public byte Code => 0xF4;
    public string Name => "DELEGATECALL";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        // DELEGATECALL added in Homestead (EIP-7). Frontier: INVALID — burn the frame.
        if (!context.Block.Rules.HasDelegateCall)
            return (ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit), context.ProgramCounter + 1);

        if (!context.Stack.TryPop(out var gas) || 
            !context.Stack.TryPop(out var addr) || 
            !context.Stack.TryPop(out var argsOffset) ||
            !context.Stack.TryPop(out var argsLength) ||
            !context.Stack.TryPop(out var retOffset) ||
            !context.Stack.TryPop(out var retLength))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        // Gap 8: Clear last return data at call entry (EIP-211 §3)
        context.LastReturnData = Array.Empty<byte>();

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var codeAddress = new Address(padded);

        var argsOffsetInt = argsOffset > int.MaxValue ? int.MaxValue : (int)argsOffset;
        var argsLengthInt = argsLength > int.MaxValue ? int.MaxValue : (int)argsLength;
        var retOffsetInt = retOffset > int.MaxValue ? int.MaxValue : (int)retOffset;
        var retLengthInt = retLength > int.MaxValue ? int.MaxValue : (int)retLength;

        // Calculate memory expansion cost for both input and return data regions
        var maxInputEnd = argsLengthInt > 0 ? (long)argsOffsetInt + argsLengthInt : 0L;
        var maxReturnEnd = retLengthInt > 0 ? (long)retOffsetInt + retLengthInt : 0L;
        var maxMemoryAccess = (int)Math.Min(Math.Max(maxInputEnd, maxReturnEnd), int.MaxValue);
        var memoryCost = context.Memory.CalculateGasCost(maxMemoryAccess);
        context.ConsumeGas(memoryCost, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);
        context.Memory.Expand(maxMemoryAccess);

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929 / pre-Berlin: CALLCODE access cost = CallBaseCost + ExtAccountCost(isWarm)
        // TangerineWhistle–Istanbul: CallBaseCost=700 is all-in; don't add ExtAccountCost
        var rules = context.Block.Rules;
        var isCodeWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(codeAddress) : true;
        ulong accessCost = rules.HasEip2929WarmCold
            ? rules.CallBaseCost + rules.ExtAccountCost(isCodeWarm)
            : rules.CallBaseCost;

        // EIP-7702: if the code address has a delegation designator (0xEF0100 || addr),
        // charge warm/cold access for the DELEGATE address (EELS access_delegation()).
        if (context.Block?.Rules.HasEip7702SetCode == true)
        {
            var calleeCode = await context.GlobalState.GetCodeAsync(codeAddress, ct);
            if (calleeCode.Length == 23 && calleeCode[0] == 0xEF && calleeCode[1] == 0x01 && calleeCode[2] == 0x00)
            {
                var delegateAddr = new Address(calleeCode[3..]);
                bool delegateIsWarm = context.Access.TouchAddress(delegateAddr);
                accessCost += rules.ExtAccountCost(delegateIsWarm);
            }
        }

        context.ConsumeGas(accessCost, GasSemantics.ExclusiveCharge, GasComponents.CallLocal);

        // Pre-EIP-150 (Frontier/Homestead): cost = CALL_BASE + gas_arg; child gets exactly gas_arg.
        // No 63/64 cap. OOG-burns if insufficient.
        if (rules.HasPreEip150CallGas)
        {
            var requestedGasPre150 = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
            context.ConsumeGas(requestedGasPre150, GasSemantics.Allocation,
                GasComponents.CallForwarded); // OOGs if gas_left < accessCost + requestedGas
            var gasLimit150 = requestedGasPre150;
            ExecutionResult result150;
            if (Precompiles.IsPrecompile(codeAddress, context.Block.Rules))
            {
                result150 = Precompiles.ExecuteAsResult(codeAddress, input, gasLimit150, context.Block.Rules);
            }
            else
            {
                if (context.SubCall == null)
                    return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);
                var tx150 = new Transaction { From = context.Caller, To = context.ContractAddress, Value = context.CallValue, Data = input, GasLimit = gasLimit150, GasPrice = context.GasPrice, Authorization = TransactionAuthorization.Internal, EnableTracing = context.CaptureTrace };
                result150 = await context.SubCall(tx150, context.IsStatic, null, codeAddress);
                if (result150.TraceSteps.Count > 0) context.TraceSteps.AddRange(result150.TraceSteps);
                if (result150.IsSuccess && result150.Logs.Count > 0) context.Logs.AddRange(result150.Logs);
            }
            var childUsed150 = result150.GasUsed > gasLimit150 ? gasLimit150 : result150.GasUsed;
            context.RefundGas(gasLimit150 > childUsed150 ? gasLimit150 - childUsed150 : 0UL,
                GasSemantics.Return, GasComponents.CallUnusedReturn);
            if (result150.IsSuccess) context.GasRefundCounter += result150.GasRefundCounter;
            context.LastReturnData = result150.ReturnData;
            var copyLen150 = Math.Min(retLengthInt, result150.ReturnData.Length);
            if (copyLen150 > 0) { var buf150 = new byte[copyLen150]; Array.Copy(result150.ReturnData, 0, buf150, 0, copyLen150); context.Memory.Store(retOffsetInt, buf150); }
            context.Stack.TryPush(result150.IsSuccess ? 1 : 0);
            return (ExecutionResult.Success(0), context.ProgramCounter + 1);
        }

        // Post-EIP-150 (TangerineWhistle+): 63/64 forwarding.
        var remaining = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;
        var maxForwardDC = remaining - remaining / 64;
        var requestedGasDC = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var gasLimit = Math.Min(requestedGasDC, maxForwardDC);

        context.ConsumeGas(gasLimit, GasSemantics.Allocation, GasComponents.CallForwarded);

        ExecutionResult result;
        if (Precompiles.IsPrecompile(codeAddress, context.Block.Rules))
        {
            result = Precompiles.ExecuteAsResult(codeAddress, input, gasLimit, context.Block.Rules);
        }
        else
        {
            if (context.SubCall == null)
                return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

            // DELEGATECALL: From=caller, To=ContractAddress, Value=context.CallValue
            // The Value field is set so CALLVALUE returns the inherited value inside
            // the child frame, but NO actual balance transfer occurs.
            // ExecuteInternalAsync skips transfer when codeAddress != null (DELEGATECALL).
            var tx = new Transaction
            {
                From = context.Caller,
                To = context.ContractAddress,
                Value = context.CallValue,
                Data = input,
                GasLimit = gasLimit,
                GasPrice = context.GasPrice,
                Authorization = TransactionAuthorization.Internal,
                EnableTracing = context.CaptureTrace
            };

            result = await context.SubCall(tx, context.IsStatic, null, codeAddress);
            if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
            if (result.IsSuccess && result.Logs.Count > 0) context.Logs.AddRange(result.Logs);
        }

        var childUsed = result.GasUsed > gasLimit ? gasLimit : result.GasUsed;
        context.RefundGas(gasLimit > childUsed ? gasLimit - childUsed : 0UL,
            GasSemantics.Return, GasComponents.CallUnusedReturn);
        if (result.IsSuccess)
        {
            context.GasRefundCounter += result.GasRefundCounter;
        }
        context.LastReturnData = result.ReturnData;
        
        var copyLen = Math.Min(retLengthInt, result.ReturnData.Length);
        if (copyLen > 0)
        {
            var dataToCopy = new byte[copyLen];
            Array.Copy(result.ReturnData, 0, dataToCopy, 0, copyLen);
            context.Memory.Store(retOffsetInt, dataToCopy);
        }

        context.Stack.TryPush(result.IsSuccess ? 1 : 0);
        
        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeSelfDestruct : IOpcode
{
    public byte Code => 0xFF;
    public string Name => "SELFDESTRUCT";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.IsStatic)
             return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

        if (!context.Stack.TryPop(out var addr))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var beneficiary = new Address(padded);

        var rules = context.Block.Rules;

        // SELFDESTRUCT gas: Frontier/Homestead = 0; TangerineWhistle+ = 5000 base.
        // EIP-2929 (Berlin+): additionally charge cold/warm access for beneficiary.
        var beneficiaryIsWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(beneficiary) : true;
        ulong gasCost = rules.SelfdestructBaseCost + (rules.HasEip2929WarmCold && !beneficiaryIsWarm ? 2_600UL : 0UL);

        var balance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);

        // TangerineWhistle+: transferring balance to a non-existent account costs an extra 25,000.
        // Frontier/Homestead: no new-account surcharge.
        if (rules.SelfdestructNewAccountCost > 0 && balance > 0)
        {
            var beneficiaryNonce = await context.GlobalState.GetNonceAsync(beneficiary, ct);
            var beneficiaryCode = await context.GlobalState.GetCodeAsync(beneficiary, ct);
            var beneficiaryBalance = await context.GlobalState.GetBalanceAsync(beneficiary, ct);
            var beneficiaryIsAlive =
                beneficiaryNonce != 0 ||
                beneficiaryCode.Length != 0 ||
                beneficiaryBalance != 0;
            if (!beneficiaryIsAlive)
                gasCost += rules.SelfdestructNewAccountCost;
        }

        var createdInTransaction =
            context.GlobalState.WasCreatedInTransaction(context.ContractAddress);

        // EELS balance transfer semantics:
        //
        // Different beneficiary: add originator balance to beneficiary, then zero originator.
        //
        // Self-send (beneficiary == originator):
        //   Pre-EIP-6780 (Shanghai and earlier): EELS does set_balance(ben, ben+orig) then
        //     set_balance(orig, 0) — the comment says zero must come AFTER the add in case of
        //     self-send. Net result: originator is always zeroed.
        //   EIP-6780 (Cancun+): EELS uses move_ether which reduces then increases the same
        //     account → net unchanged. Originator is only explicitly zeroed when it was created
        //     in the same transaction (and is also marked for deletion).
        if (!beneficiary.Equals(context.ContractAddress))
        {
            var benBalance = await context.GlobalState.GetBalanceAsync(beneficiary, ct);
            context.GlobalState.SetBalance(beneficiary, benBalance + balance);
            context.GlobalState.SetBalance(context.ContractAddress, 0);
        }
        else
        {
            // Self-send: zero unless EIP-6780 is active and this is a pre-existing contract.
            var shouldZeroOnSelfSend = !rules.HasEip6780SelfdestructRestriction || createdInTransaction;
            if (shouldZeroOnSelfSend)
                context.GlobalState.SetBalance(context.ContractAddress, 0);
        }

        // Frontier–Berlin (removed by EIP-3529 in London): first SELFDESTRUCT of an account
        // in the transaction adds 24000 to the refund counter.
        // Check BEFORE MarkForDeletion: IsMarkedForDeletion reflects prior same-tx selfdestructs only.
        // EELS: if originator not already in accounts_to_delete → refund_counter += 24000
        if (rules.HasSelfdestructRefund && !context.GlobalState.IsMarkedForDeletion(context.ContractAddress))
            context.GasRefundCounter += 24_000;

        // EIP-6780 (Shanghai+): account deletion only when created in same transaction.
        // Pre-Shanghai: SELFDESTRUCT always marks the contract for deletion.
        bool shouldDelete = rules.HasEip6780SelfdestructRestriction
            ? createdInTransaction
            : true;
        if (shouldDelete)
            context.GlobalState.MarkForDeletion(context.ContractAddress);

        return (ExecutionResult.Success(gasCost), context.Code.Length);
    }
}

internal static class PrecompileExecutor
{
    public static bool IsSupportedPrecompile(Address address)
    {
        var n = GetPrecompileNumber(address);
        return n is >= 1 and <= 5;
    }

    public static ExecutionResult Execute(Address address, byte[] input, ulong gasLimit)
    {
        var n = GetPrecompileNumber(address);
        if (n is < 1 or > 5)
        {
            return ExecutionResult.Success(0, Array.Empty<byte>());
        }

        return n switch
        {
            1 => ExecuteEcRecover(input, gasLimit),
            2 => ExecuteSha256(input, gasLimit),
            3 => ExecuteRipemd160(input, gasLimit),
            4 => ExecuteIdentity(input, gasLimit),
            5 => ExecuteModExp(input, gasLimit),
            _ => ExecutionResult.Success(0, Array.Empty<byte>())
        };
    }

    private static int GetPrecompileNumber(Address address)
    {
        var bytes = address.Bytes;
        for (var i = 0; i < 19; i++)
        {
            if (bytes[i] != 0)
            {
                return -1;
            }
        }

        return bytes[19];
    }

    private static ExecutionResult ExecuteEcRecover(byte[] input, ulong gasLimit)
    {
        const ulong gasCost = 3000;
        if (gasLimit < gasCost)
        {
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        }

        var padded = new byte[128];
        var copyLen = Math.Min(128, input.Length);
        Array.Copy(input, 0, padded, 0, copyLen);

        var hash = padded[0..32];
        var vWord = padded[32..64];
        var r = padded[64..96];
        var s = padded[96..128];

        var v = new BigInteger(vWord, isUnsigned: true, isBigEndian: true);
        var vInt = (int)v;
        if (vInt is 0 or 1)
        {
            vInt += 27;
        }

        try
        {
            var recovered = CryptoUtils.RecoverAddressForPrecompile(hash, vInt, r, s);
            if (recovered is null)
            {
                // Invalid signature inputs return empty output while still charging gas.
                return ExecutionResult.Success(gasCost, Array.Empty<byte>());
            }
            var output = new byte[32];
            Array.Copy(recovered.Value.Bytes, 0, output, 12, 20);
            return ExecutionResult.Success(gasCost, output);
        }
        catch
        {
            // Invalid signature inputs return empty output while still charging gas.
            return ExecutionResult.Success(gasCost, Array.Empty<byte>());
        }
    }

    private static ExecutionResult ExecuteSha256(byte[] input, ulong gasLimit)
    {
        var words = (ulong)(input.Length + 31) / 32;
        var gasCost = 60UL + (12UL * words);
        if (gasLimit < gasCost)
        {
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        }

        var hash = SHA256.HashData(input);
        return ExecutionResult.Success(gasCost, hash);
    }

    private static ExecutionResult ExecuteRipemd160(byte[] input, ulong gasLimit)
    {
        var words = (ulong)(input.Length + 31) / 32;
        var gasCost = 600UL + (120UL * words);
        if (gasLimit < gasCost)
        {
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        }

        var ripemd = new Org.BouncyCastle.Crypto.Digests.RipeMD160Digest();
        ripemd.BlockUpdate(input, 0, input.Length);
        var hash20 = new byte[20];
        ripemd.DoFinal(hash20, 0);
        var output = new byte[32];
        Array.Copy(hash20, 0, output, 12, 20);
        return ExecutionResult.Success(gasCost, output);
    }

    private static ExecutionResult ExecuteIdentity(byte[] input, ulong gasLimit)
    {
        var words = (ulong)(input.Length + 31) / 32;
        var gasCost = 15UL + (3UL * words);
        if (gasLimit < gasCost)
        {
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        }

        var output = new byte[input.Length];
        Array.Copy(input, output, input.Length);
        return ExecutionResult.Success(gasCost, output);
    }

    private static ExecutionResult ExecuteModExp(byte[] input, ulong gasLimit)
    {
        var baseLen = ReadLengthWord(input, 0);
        var expLen = ReadLengthWord(input, 32);
        var modLen = ReadLengthWord(input, 64);

        var gasCost = ComputeModExpGas(baseLen, expLen, modLen, input);

        if (!OperandValidation.TryGasCost(gasCost, gasLimit, out var gasUsedCost))
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);

        if (modLen.IsZero)
            return ExecutionResult.Success(gasUsedCost, Array.Empty<byte>());

        if (baseLen > int.MaxValue || expLen > int.MaxValue || modLen > int.MaxValue ||
            baseLen + expLen + modLen > int.MaxValue - 96)
        {
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        }

        var offset = new BigInteger(96);
        var baseBytes = ReadSegment(input, offset, baseLen);
        offset += baseLen;
        var expBytes = ReadSegment(input, offset, expLen);
        offset += expLen;
        var modBytes = ReadSegment(input, offset, modLen);

        var modulus = new BigInteger(modBytes, isUnsigned: true, isBigEndian: true);
        byte[] output;
        if (modulus.IsZero)
        {
            output = new byte[(int)modLen];
        }
        else
        {
            var @base = new BigInteger(baseBytes, isUnsigned: true, isBigEndian: true);
            var exponent = new BigInteger(expBytes, isUnsigned: true, isBigEndian: true);
            var result = BigInteger.ModPow(@base, exponent, modulus);
            output = ToFixedLengthWord(result, (int)modLen);
        }

        return ExecutionResult.Success(gasUsedCost, output);
    }

    private static BigInteger ReadLengthWord(byte[] input, int start)
    {
        if (start >= input.Length)
        {
            return BigInteger.Zero;
        }

        var len = Math.Min(32, input.Length - start);
        var bytes = new byte[32];
        Array.Copy(input, start, bytes, 0, len);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    private static byte[] ReadSegment(byte[] input, BigInteger start, BigInteger len)
    {
        if (len.IsZero)
        {
            return Array.Empty<byte>();
        }

        var output = new byte[(int)len];
        if (start >= input.Length)
        {
            return output;
        }

        var startInt = (int)start;
        var copyLen = Math.Min(input.Length - startInt, output.Length);
        Array.Copy(input, startInt, output, 0, copyLen);
        return output;
    }

    private static BigInteger ComputeModExpGas(
        BigInteger baseLen,
        BigInteger expLen,
        BigInteger modLen,
        byte[] input)
    {
        var maxLen = BigInteger.Max(baseLen, modLen);
        var words = (maxLen + 7) / 8;
        var multiplicationComplexity = words * words;

        var headLen = (int)BigInteger.Min(expLen, 32);
        var expHead = ReadSegmentForGas(input, 96 + baseLen, headLen);
        var expHeadValue = new BigInteger(expHead, isUnsigned: true, isBigEndian: true);
        var expHeadBits = expHeadValue.IsZero ? 0 : GetBitLength(expHeadValue);

        BigInteger iterationCount;
        if (expLen <= 32)
        {
            iterationCount = BigInteger.Max(0, expHeadBits - 1);
        }
        else
        {
            iterationCount = (new BigInteger(8) * (expLen - 32)) + BigInteger.Max(0, expHeadBits - 1);
        }

        if (iterationCount.IsZero)
        {
            iterationCount = BigInteger.One;
        }

        var gas = (multiplicationComplexity * iterationCount) / 3;
        if (gas < 200)
        {
            gas = 200;
        }

        return gas;
    }

    private static byte[] ReadSegmentForGas(byte[] input, BigInteger start, int length)
    {
        var output = new byte[length];
        if (length == 0 || start < 0 || start >= input.Length)
        {
            return output;
        }

        var startInt = (int)start;
        var copyLen = Math.Min(length, input.Length - startInt);
        Array.Copy(input, startInt, output, 0, copyLen);
        return output;
    }

    private static int GetBitLength(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 0)
        {
            return 0;
        }

        var msb = bytes[0];
        var bitsInMsb = 8;
        while ((msb & 0x80) == 0)
        {
            bitsInMsb--;
            msb <<= 1;
        }

        return ((bytes.Length - 1) * 8) + bitsInMsb;
    }

    private static byte[] ToFixedLengthWord(BigInteger value, int size)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == size)
        {
            return bytes;
        }

        var output = new byte[size];
        if (bytes.Length > size)
        {
            Array.Copy(bytes, bytes.Length - size, output, 0, size);
        }
        else
        {
            Array.Copy(bytes, 0, output, size - bytes.Length, bytes.Length);
        }

        return output;
    }
}

