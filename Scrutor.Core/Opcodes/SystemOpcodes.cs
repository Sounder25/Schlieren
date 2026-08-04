using System.Numerics;
using System.Security.Cryptography;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

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

        // Gap 6: EIP-3860 – init code must not exceed 2 × MAX_CODE_SIZE (49152 bytes).
        if (length > 2 * 24576)
            return (ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1);

        var offsetInt = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var lengthInt = length > int.MaxValue ? int.MaxValue : (int)length;

        // EIP-3860 word gas: 2 per 32-byte word of init code, charged before execution.
        var initCodeWordGas = 2UL * ((ulong)(lengthInt + 31) / 32);
        // Base CREATE gas: 32000
        context.ConsumeGas(32000 + initCodeWordGas);

        var initCode = context.Memory.Load(offsetInt, lengthInt);

        // Derive address
        var nonce = await context.GlobalState.GetNonceAsync(context.ContractAddress, ct);
        var newAddress = CryptoUtils.DeriveContractAddress(context.ContractAddress, nonce);
        context.Access.WarmAddress(newAddress);
        
        // Increment nonce of creator
        context.GlobalState.SetNonce(context.ContractAddress, nonce + 1);

        // Sub-call for initialization
        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        // EIP-150: forward at most 63/64 of remaining gas to child.
        var parentGasBeforeChild = context.GasLimit - context.GasUsed;
        var forwardedGas = parentGasBeforeChild - (parentGasBeforeChild / 64UL);
        var parentReserve = parentGasBeforeChild - forwardedGas;

        // Charge only the gas that enters the child frame.
        context.ConsumeGas(forwardedGas);

        // EIP-211: Clear return data buffer before any call-like operation
        context.LastReturnData = Array.Empty<byte>();

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

        var result = await context.SubCall(tx, false, newAddress, null); // isStatic=false, creationAddress, codeAddress=null
        if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);

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

            // Debug instrumentation for code-deposit accounting
            if (context.CaptureTrace)
            {
                Console.WriteLine($"[CREATE_DEPOSIT] runtimeBytes={runtimeCode.Length} depositCost={codeDepositCost} childRemaining={childRemaining}");
            }

            if (childRemaining < codeDepositCost)
            {
                // Exceptional halt: out of gas during code deposit.
                // The child's remaining gas is consumed (not refunded to parent).
                // State reverted, address 0 returned.
                if (context.CaptureTrace)
                {
                    Console.WriteLine($"[CREATE_DEPOSIT_OOG] consumedChildGas={childRemaining} NOT_REFUNDED");
                }
                context.Stack.TryPush(0);
                // No RefundGas—child gas is consumed by exceptional halt.
            }
            else
            {
                // Deduct code deposit cost from child's remaining gas.
                childRemaining -= codeDepositCost;

                // Install runtime code.
                context.GlobalState.SetCode(newAddress, runtimeCode);

                context.GasRefundCounter += result.GasRefundCounter;

                // Refund remaining gas to parent.
                context.RefundGas(childRemaining);

                if (context.CaptureTrace)
                {
                    Console.WriteLine($"[CREATE_SUCCESS] depositPaid={codeDepositCost} refundedToParent={childRemaining}");
                }

                // Push created address.
                context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
            }
        }
        else
        {
            // Init code failed: refund all unused gas, return 0 address.
            context.RefundGas(childRemaining);
            context.Stack.TryPush(0);
        }

        return (ExecutionResult.Success(0), context.ProgramCounter + 1);
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
        context.ConsumeGas(memoryCost);
        context.Memory.Expand(maxMemoryAccess);

        // Load input data
        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929: charge cold address surcharge (part of extra gas).
        var isWarm = context.Access.TouchAddress(toAddress);
        ulong accessCost = isWarm ? 100UL : 2600UL;

        // Value-transfer cost: 9000 if value > 0 (EELS: transfer_gas_cost).
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

        // Gap 1: EIP-150 – forward at most 63/64 of remaining gas (after extra costs).
        var availableAfterExtras = context.GasLimit - context.GasUsed - extraCost;
        var maxForward = availableAfterExtras - availableAfterExtras / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var forwardedGas = Math.Min(requestedGas, maxForward);

        // Parent pays forwarded gas + extra costs (but NOT the stipend).
        context.ConsumeGas(forwardedGas + extraCost);

        // Value-bearing calls receive a 2,300 gas stipend in the child allocation.
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
                context.RefundGas(forwardedGas + stipend);
                context.Stack.TryPush(0);
                return (ExecutionResult.Success(0), context.ProgramCounter + 1);
            }
        }

        // Gap 2: Add 2300 call stipend when value > 0 (EELS: stipend added to child, not charged to parent).
        var childGasLimit = forwardedGas + stipend;

        ExecutionResult result;
        if (Precompiles.IsPrecompile(toAddress))
        {
            result = Precompiles.ExecuteAsResult(toAddress, input, childGasLimit);
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
        }

        // EELS refund semantics: return ALL unused child gas to parent.
        // The stipend was added to childGasLimit but never charged to parent,
        // so EELS `evm.gas_left += child_evm.gas_left` naturally handles it.
        var childUsed = result.GasUsed > childGasLimit ? childGasLimit : result.GasUsed;
        var childRemaining = childGasLimit > childUsed ? childGasLimit - childUsed : 0UL;
        context.RefundGas(childRemaining);
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
        if (context.IsStatic)
             return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

        if (!context.Stack.TryPop(out var value) || 
            !context.Stack.TryPop(out var offset) || 
            !context.Stack.TryPop(out var length) ||
            !context.Stack.TryPop(out var salt))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        // Gap 6: EIP-3860 – init code must not exceed 2 × MAX_CODE_SIZE (49152 bytes).
        if (length > 2 * 24576)
            return (ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1);

        var offsetInt = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var lengthInt = length > int.MaxValue ? int.MaxValue : (int)length;

        // EIP-3860 word gas: 2 per 32-byte word of init code.
        var initCodeWordGas = 2UL * ((ulong)(lengthInt + 31) / 32);
        // Base CREATE2 gas: 32000 + hash gas (6 per word)
        var hashWordGas = 6UL * ((ulong)(lengthInt + 31) / 32);
        context.ConsumeGas(32000 + initCodeWordGas + hashWordGas);

        var initCode = context.Memory.Load(offsetInt, lengthInt);
        
        // Ensure salt is exactly 32 bytes (big endian)
        var saltBytes = salt.ToByteArray(isUnsigned: true, isBigEndian: true);
        var paddedSalt = new byte[32];
        if (saltBytes.Length > 32) Array.Copy(saltBytes, saltBytes.Length - 32, paddedSalt, 0, 32);
        else Array.Copy(saltBytes, 0, paddedSalt, 32 - saltBytes.Length, saltBytes.Length);

        // Derive address using salt
        var newAddress = CryptoUtils.DeriveContractAddress2(context.ContractAddress, paddedSalt, initCode);
        context.Access.WarmAddress(newAddress);
        
        // Increment nonce of creator
        var nonce = await context.GlobalState.GetNonceAsync(context.ContractAddress, ct);
        context.GlobalState.SetNonce(context.ContractAddress, nonce + 1);

        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        // EIP-150: forward at most 63/64 of remaining gas to child.
        var parentGasBeforeChild = context.GasLimit - context.GasUsed;
        var forwardedGas = parentGasBeforeChild - (parentGasBeforeChild / 64UL);
        var parentReserve = parentGasBeforeChild - forwardedGas;

        // Charge only the gas that enters the child frame.
        context.ConsumeGas(forwardedGas);

        // EIP-211: Clear return data buffer before any call-like operation
        context.LastReturnData = Array.Empty<byte>();

        // EELS account_deployable(): CREATE2 collides when the destination has
        // a nonzero nonce, existing code, or any storage. The EIP-150 message
        // gas has already been reserved and remains consumed on collision.
        var destinationNonce = await context.GlobalState.GetNonceAsync(newAddress, ct);
        var destinationCode = await context.GlobalState.GetCodeAsync(newAddress, ct);
        var destinationStorage =
            await context.GlobalState.GetStoragePresenceAsync(newAddress, ct);
        if (destinationNonce != 0 ||
            destinationCode.Length != 0 ||
            destinationStorage != StoragePresence.Empty)
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

        var result = await context.SubCall(tx, false, newAddress, null);
        if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);

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

            if (childRemaining < codeDepositCost)
            {
                // Exceptional halt: out of gas during code deposit.
                // Consume all child gas, revert creation, return 0 address.
                context.Stack.TryPush(0);
            }
            else
            {
                // Deduct code deposit cost from child's remaining gas.
                childRemaining -= codeDepositCost;

                // Install runtime code.
                context.GlobalState.SetCode(newAddress, runtimeCode);

                context.GasRefundCounter += result.GasRefundCounter;

                // Refund remaining gas to parent.
                context.RefundGas(childRemaining);

                // Push created address.
                context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
            }
        }
        else
        {
            // Init code failed: refund all unused gas, return 0 address.
            context.RefundGas(childRemaining);
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
        context.ConsumeGas(memoryCost);
        context.Memory.Expand(maxMemoryAccess);

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929: charge warm access or cold address access before forwarding.
        var isWarm = context.Access.TouchAddress(toAddress);
        ulong accessCost = isWarm ? 100UL : 2600UL;
        context.ConsumeGas(accessCost);

        // Gap 1: EIP-150 – forward at most 63/64 of remaining gas.
        var remaining = context.GasLimit - context.GasUsed;
        var maxForward = remaining - remaining / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var gasLimit = Math.Min(requestedGas, maxForward);

        context.ConsumeGas(gasLimit);

        ExecutionResult result;
        if (Precompiles.IsPrecompile(toAddress))
        {
            result = Precompiles.ExecuteAsResult(toAddress, input, gasLimit);
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
        }

        var childUsed = result.GasUsed > gasLimit ? gasLimit : result.GasUsed;
        context.RefundGas(gasLimit > childUsed ? gasLimit - childUsed : 0UL);
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

        if (context.IsStatic && !value.IsZero)
             return (ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1);

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
        context.ConsumeGas(memoryCost);
        context.Memory.Expand(maxMemoryAccess);

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929: charge cold address surcharge (part of extra gas).
        var isCodeWarm = context.Access.TouchAddress(codeAddress);
        ulong accessCost = isCodeWarm ? 100UL : 2600UL;

        // Value-transfer cost: 9000 if value > 0 (EELS: transfer_gas_cost).
        ulong valueTransferCost = value.IsZero ? 0UL : 9000UL;

        ulong extraCost = accessCost + valueTransferCost;

        // Gap 1: EIP-150 – forward at most 63/64 of remaining gas (after extra costs).
        var availableAfterExtras = context.GasLimit - context.GasUsed - extraCost;
        var maxForward = availableAfterExtras - availableAfterExtras / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var forwardedGas = Math.Min(requestedGas, maxForward);

        // Parent pays forwarded gas + extra costs (but NOT the stipend).
        context.ConsumeGas(forwardedGas + extraCost);

        // Gap 4: Check caller balance BEFORE issuing sub-call.
        if (!value.IsZero)
        {
            var callerBalance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
            if (callerBalance < value)
            {
                // Refund all forwarded gas + extra costs on balance-check failure.
                context.RefundGas(forwardedGas + extraCost);
                context.Stack.TryPush(0);
                return (ExecutionResult.Success(0), context.ProgramCounter + 1);
            }
        }

        // Gap 2: Add 2300 call stipend when value > 0 (EELS: stipend added to child, not charged to parent).
        var stipend = value.IsZero ? 0UL : 2300UL;
        var childGasLimit = forwardedGas + stipend;

        ExecutionResult result;
        if (Precompiles.IsPrecompile(codeAddress))
        {
            result = Precompiles.ExecuteAsResult(codeAddress, input, childGasLimit);
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

            result = await context.SubCall(tx, false, null, codeAddress);
            if (result.TraceSteps.Count > 0) context.TraceSteps.AddRange(result.TraceSteps);
        }

        // EELS refund semantics: return ALL unused child gas to parent.
        var childUsed = result.GasUsed > childGasLimit ? childGasLimit : result.GasUsed;
        var childRemaining = childGasLimit > childUsed ? childGasLimit - childUsed : 0UL;
        context.RefundGas(childRemaining);
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
        context.ConsumeGas(memoryCost);
        context.Memory.Expand(maxMemoryAccess);

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        // EIP-2929: charge warm access or cold address access before forwarding.
        var isCodeWarm = context.Access.TouchAddress(codeAddress);
        ulong accessCost = isCodeWarm ? 100UL : 2600UL;
        context.ConsumeGas(accessCost);

        // Gap 1: EIP-150 – forward at most 63/64 of remaining gas.
        var remaining = context.GasLimit - context.GasUsed;
        var maxForward = remaining - remaining / 64;
        var requestedGas = gas > ulong.MaxValue ? ulong.MaxValue : (ulong)gas;
        var gasLimit = Math.Min(requestedGas, maxForward);

        context.ConsumeGas(gasLimit);

        ExecutionResult result;
        if (Precompiles.IsPrecompile(codeAddress))
        {
            result = Precompiles.ExecuteAsResult(codeAddress, input, gasLimit);
        }
        else
        {
            if (context.SubCall == null)
                 return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

            // DELEGATECALL: From=caller, To=ContractAddress, Value=context.CallValue
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
        }

        var childUsed = result.GasUsed > gasLimit ? gasLimit : result.GasUsed;
        context.RefundGas(gasLimit > childUsed ? gasLimit - childUsed : 0UL);
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

        // EIP-2929: charge cold address surcharge for the beneficiary.
        var isWarm = context.Access.TouchAddress(beneficiary);
        ulong gasCost = 5000UL + (isWarm ? 0UL : 2600UL);

        var balance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);

        // EELS Cancun selfdestruct(): transferring a nonzero balance to an account
        // that is not alive costs an additional 25,000 gas. An account is alive
        // only when at least one of nonce, code, or balance is nonzero.
        if (balance > 0)
        {
            var beneficiaryNonce = await context.GlobalState.GetNonceAsync(beneficiary, ct);
            var beneficiaryCode = await context.GlobalState.GetCodeAsync(beneficiary, ct);
            var beneficiaryBalance = await context.GlobalState.GetBalanceAsync(beneficiary, ct);
            var beneficiaryIsAlive =
                beneficiaryNonce != 0 ||
                beneficiaryCode.Length != 0 ||
                beneficiaryBalance != 0;
            if (!beneficiaryIsAlive)
                gasCost += 25_000UL;
        }

        var createdInTransaction =
            context.GlobalState.WasCreatedInTransaction(context.ContractAddress);

        if (balance > 0 && !beneficiary.Equals(context.ContractAddress))
        {
            var benBalance = await context.GlobalState.GetBalanceAsync(beneficiary, ct);
            context.GlobalState.SetBalance(beneficiary, benBalance + balance);
            context.GlobalState.SetBalance(context.ContractAddress, 0);
        }
        else if (balance > 0 && createdInTransaction)
        {
            // EIP-6780 burns the balance when a same-transaction creation
            // selfdestructs to itself.
            context.GlobalState.SetBalance(context.ContractAddress, 0);
        }

        // EIP-6780: account deletion is deferred to transaction finalization
        // and applies only to contracts created during this transaction.
        if (createdInTransaction)
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
            var recovered = CryptoUtils.RecoverAddress(hash, vInt, r, s);
            var output = new byte[32];
            Array.Copy(recovered.Bytes, 0, output, 12, 20);
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

        if (gasLimit < gasCost)
        {
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        }

        if (modLen.IsZero)
        {
            return ExecutionResult.Success((ulong)gasCost, Array.Empty<byte>());
        }

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

        return ExecutionResult.Success((ulong)gasCost, output);
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

