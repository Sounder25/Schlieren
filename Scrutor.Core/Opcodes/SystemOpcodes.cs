using System.Numerics;
using ELR.Core.Execution;
using ELR.Core.Primitives;
using ELR.Core.State;
using ExecutionContext = ELR.Core.Execution.ExecutionContext;

namespace ELR.Core.Opcodes;

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

        // Gas: 32000 + memory expansion + init code execution
        if (context.GasUsed + 32000 > context.GasLimit)
             return (ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1);
        context.ConsumeGas(32000);

        var offsetInt = (int)offset;
        var lengthInt = (int)length;
        var initCode = context.Memory.Load(offsetInt, lengthInt);

        // Derive address
        var nonce = await context.GlobalState.GetNonceAsync(context.ContractAddress, ct);
        var newAddress = CryptoUtils.DeriveContractAddress(context.ContractAddress, nonce);
        
        // Increment nonce of creator
        context.GlobalState.SetNonce(context.ContractAddress, nonce + 1);

        // Sub-call for initialization
        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        var gasAvailable = context.GasLimit - context.GasUsed;
        context.ConsumeGas(gasAvailable);

        // Construct internal tx for creation
        var tx = new Transaction
        {
            From = context.ContractAddress,
            To = null, // Contract creation
            Value = value,
            Data = initCode,
            GasLimit = gasAvailable, // Pass all
            GasPrice = context.GasPrice,
            Nonce = nonce,
            Authorization = TransactionAuthorization.Internal
        };

        var result = await context.SubCall(tx, false, newAddress, null); // isStatic=false, creationAddress, codeAddress=null

        // Refund unused
        var unused = gasAvailable - result.GasUsed;
        context.RefundGas(unused);

        if (result.IsSuccess)
        {
            // Set code
            context.GlobalState.SetCode(newAddress, result.ReturnData);
            context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
        }
        else
        {
            context.Stack.TryPush(0); // Failure returns 0 address
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

        // Extract address
        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var toAddress = new Address(padded);

        var argsOffsetInt = (int)argsOffset;
        var argsLengthInt = (int)argsLength;
        var retOffsetInt = (int)retOffset;
        var retLengthInt = (int)retLength;

        // Load input data
        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        var gasLimit = (ulong)gas;
        var remaining = context.GasLimit - context.GasUsed;
        if (gasLimit > remaining) gasLimit = remaining;
        
        context.ConsumeGas(gasLimit);

        // Sub-call
        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        var tx = new Transaction
        {
            From = context.ContractAddress,
            To = toAddress,
            Value = value,
            Data = input,
            GasLimit = gasLimit,
            GasPrice = context.GasPrice,
            Authorization = TransactionAuthorization.Internal
        };

        var result = await context.SubCall(tx, context.IsStatic, null, null);

        // Refund unused gas
        var unused = gasLimit - result.GasUsed;
        context.RefundGas(unused);

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

        context.ConsumeGas(32000);

        var offsetInt = (int)offset;
        var lengthInt = (int)length;
        var initCode = context.Memory.Load(offsetInt, lengthInt);
        
        // Ensure salt is exactly 32 bytes (big endian)
        var saltBytes = salt.ToByteArray(isUnsigned: true, isBigEndian: true);
        var paddedSalt = new byte[32];
        if (saltBytes.Length > 32) Array.Copy(saltBytes, saltBytes.Length - 32, paddedSalt, 0, 32);
        else Array.Copy(saltBytes, 0, paddedSalt, 32 - saltBytes.Length, saltBytes.Length);

        // Derive address using salt
        var newAddress = CryptoUtils.DeriveContractAddress2(context.ContractAddress, paddedSalt, initCode);
        
        // Increment nonce of creator
        var nonce = await context.GlobalState.GetNonceAsync(context.ContractAddress, ct);
        context.GlobalState.SetNonce(context.ContractAddress, nonce + 1);

        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        var gasAvailable = context.GasLimit - context.GasUsed;
        context.ConsumeGas(gasAvailable);

        var tx = new Transaction
        {
            From = context.ContractAddress,
            To = null,
            Value = value,
            Data = initCode,
            GasLimit = gasAvailable,
            GasPrice = context.GasPrice,
            Nonce = nonce,
            Authorization = TransactionAuthorization.Internal
        };

        var result = await context.SubCall(tx, false, newAddress, null);

        var unused = gasAvailable - result.GasUsed;
        context.RefundGas(unused);

        if (result.IsSuccess)
        {
            context.GlobalState.SetCode(newAddress, result.ReturnData);
            context.Stack.TryPush(new BigInteger(newAddress.Bytes, isUnsigned: true, isBigEndian: true));
        }
        else
        {
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

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var toAddress = new Address(padded);

        var argsOffsetInt = (int)argsOffset;
        var argsLengthInt = (int)argsLength;
        var retOffsetInt = (int)retOffset;
        var retLengthInt = (int)retLength;

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        var gasLimit = (ulong)gas;
        var remaining = context.GasLimit - context.GasUsed;
        if (gasLimit > remaining) gasLimit = remaining;
        
        context.ConsumeGas(gasLimit);

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
            Authorization = TransactionAuthorization.Internal
        };

        var result = await context.SubCall(tx, true, null, null);

        var unused = gasLimit - result.GasUsed;
        context.RefundGas(unused);
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

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var codeAddress = new Address(padded);

        var argsOffsetInt = (int)argsOffset;
        var argsLengthInt = (int)argsLength;
        var retOffsetInt = (int)retOffset;
        var retLengthInt = (int)retLength;

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        var gasLimit = (ulong)gas;
        var remaining = context.GasLimit - context.GasUsed;
        if (gasLimit > remaining) gasLimit = remaining;
        
        context.ConsumeGas(gasLimit);

        if (context.SubCall == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        var tx = new Transaction
        {
            From = context.ContractAddress,
            To = context.ContractAddress,
            Value = value,
            Data = input,
            GasLimit = gasLimit,
            GasPrice = context.GasPrice,
            Authorization = TransactionAuthorization.Internal
        };

        var result = await context.SubCall(tx, context.IsStatic, null, codeAddress);

        var unused = gasLimit - result.GasUsed;
        context.RefundGas(unused);
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

        var addressBytes = addr.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[20];
        if (addressBytes.Length > 20) Array.Copy(addressBytes, addressBytes.Length - 20, padded, 0, 20);
        else Array.Copy(addressBytes, 0, padded, 20 - addressBytes.Length, addressBytes.Length);
        var codeAddress = new Address(padded);

        var argsOffsetInt = (int)argsOffset;
        var argsLengthInt = (int)argsLength;
        var retOffsetInt = (int)retOffset;
        var retLengthInt = (int)retLength;

        var input = context.Memory.Load(argsOffsetInt, argsLengthInt);

        var gasLimit = (ulong)gas;
        var remaining = context.GasLimit - context.GasUsed;
        if (gasLimit > remaining) gasLimit = remaining;
        
        context.ConsumeGas(gasLimit);

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
            Authorization = TransactionAuthorization.Internal
        };

        var result = await context.SubCall(tx, context.IsStatic, null, codeAddress);

        var unused = gasLimit - result.GasUsed;
        context.RefundGas(unused);
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

        var balance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
        
        if (balance > 0)
        {
            var benBalance = await context.GlobalState.GetBalanceAsync(beneficiary, ct);
            context.GlobalState.SetBalance(beneficiary, benBalance + balance);
            context.GlobalState.SetBalance(context.ContractAddress, 0);
        }
        
        return (ExecutionResult.Success(5000), context.Code.Length);
    }
}

