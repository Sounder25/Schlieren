using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public abstract class PushOpcodeBase : IOpcode
{
    public abstract byte Code { get; }
    public abstract string Name { get; }
    public abstract int Size { get; }

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.ProgramCounter + Size >= context.Code.Length)
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1));

        var dataSpan = context.Code.AsSpan(context.ProgramCounter + 1, Size);
        var value = new BigInteger(dataSpan, isUnsigned: true, isBigEndian: true);
        
        if (!context.Stack.TryPush(value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + Size + 1));
    }
}

public sealed class OpcodePush1 : PushOpcodeBase { public override byte Code => 0x60; public override string Name => "PUSH1"; public override int Size => 1; }
public sealed class OpcodePush2 : PushOpcodeBase { public override byte Code => 0x61; public override string Name => "PUSH2"; public override int Size => 2; }
public sealed class OpcodePush4 : PushOpcodeBase { public override byte Code => 0x63; public override string Name => "PUSH4"; public override int Size => 4; }
public sealed class OpcodePush8 : PushOpcodeBase { public override byte Code => 0x67; public override string Name => "PUSH8"; public override int Size => 8; }
public sealed class OpcodePush20 : PushOpcodeBase { public override byte Code => 0x73; public override string Name => "PUSH20"; public override int Size => 20; }
public sealed class OpcodePush32 : PushOpcodeBase { public override byte Code => 0x7F; public override string Name => "PUSH32"; public override int Size => 32; }

public abstract class DupOpcodeBase : IOpcode
{
    public abstract byte Code { get; }
    public abstract string Name { get; }
    public abstract int Depth { get; }

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        try 
        {
            context.Stack.Dup(Depth);
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
        }
        catch (EvmStackUnderflowException) { return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1)); }
        catch (EvmStackOverflowException) { return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1)); }
    }
}

public sealed class OpcodeDup1 : DupOpcodeBase { public override byte Code => 0x80; public override string Name => "DUP1"; public override int Depth => 1; }
public sealed class OpcodeDup2 : DupOpcodeBase { public override byte Code => 0x81; public override string Name => "DUP2"; public override int Depth => 2; }
public sealed class OpcodeDup3 : DupOpcodeBase { public override byte Code => 0x82; public override string Name => "DUP3"; public override int Depth => 3; }
public sealed class OpcodeDup4 : DupOpcodeBase { public override byte Code => 0x83; public override string Name => "DUP4"; public override int Depth => 4; }
public sealed class OpcodeDup16 : DupOpcodeBase { public override byte Code => 0x8F; public override string Name => "DUP16"; public override int Depth => 16; }

public abstract class SwapOpcodeBase : IOpcode
{
    public abstract byte Code { get; }
    public abstract string Name { get; }
    public abstract int Depth { get; }

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        try
        {
            context.Stack.Swap(Depth);
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
        }
        catch (EvmStackUnderflowException) { return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1)); }
    }
}

public sealed class OpcodeSwap1 : SwapOpcodeBase { public override byte Code => 0x90; public override string Name => "SWAP1"; public override int Depth => 1; }
public sealed class OpcodeSwap2 : SwapOpcodeBase { public override byte Code => 0x91; public override string Name => "SWAP2"; public override int Depth => 2; }
public sealed class OpcodeSwap3 : SwapOpcodeBase { public override byte Code => 0x92; public override string Name => "SWAP3"; public override int Depth => 3; }
public sealed class OpcodeSwap16 : SwapOpcodeBase { public override byte Code => 0x9F; public override string Name => "SWAP16"; public override int Depth => 16; }
