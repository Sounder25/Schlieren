using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Core.Opcodes;

/// <summary>
/// POP (0x50): Remove the top item from the stack.
/// Gas: 2
/// </summary>
public sealed class OpcodePop : IOpcode
{
    public byte Code => 0x50;
    public string Name => "POP";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out _))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// PUSH0 (0x5F): Push zero onto the stack. EIP-3855 (Shanghai).
/// Gas: 2
/// </summary>
public sealed class OpcodePush0 : IOpcode
{
    public byte Code => 0x5F;
    public string Name => "PUSH0";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(BigInteger.Zero))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

public abstract class PushOpcodeBase : IOpcode
{
    public abstract byte Code { get; }
    public abstract string Name { get; }
    public abstract int Size { get; }

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        // A PUSH_N at PC requires (Size) immediate bytes starting at PC+1.
        // Per EELS / Yellow Paper: bytes past end-of-code are treated as 0x00 (zero-padded).
        // Only fail if the opcode byte itself is at or past end-of-code (handled by the
        // EvmMachine loop — this opcode would never be dispatched in that case).
        var start = context.ProgramCounter + 1;
        var available = Math.Max(0, context.Code.Length - start);

        BigInteger value;
        if (available >= Size)
        {
            // All bytes present — fast path.
            var dataSpan = context.Code.AsSpan(start, Size);
            value = new BigInteger(dataSpan, isUnsigned: true, isBigEndian: true);
        }
        else
        {
            // Partial or zero bytes available — zero-pad to Size bytes.
            var padded = new byte[Size];
            if (available > 0)
                Array.Copy(context.Code, start, padded, 0, available);
            value = new BigInteger(padded, isUnsigned: true, isBigEndian: true);
        }

        if (!context.Stack.TryPush(value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + Size + 1));
    }
}

// [AI-EDIT 2026-01-10] Full PUSH1-PUSH32 coverage (0x60–0x7F)
public sealed class OpcodePush1 : PushOpcodeBase { public override byte Code => 0x60; public override string Name => "PUSH1"; public override int Size => 1; }
public sealed class OpcodePush2 : PushOpcodeBase { public override byte Code => 0x61; public override string Name => "PUSH2"; public override int Size => 2; }
public sealed class OpcodePush3 : PushOpcodeBase { public override byte Code => 0x62; public override string Name => "PUSH3"; public override int Size => 3; }
public sealed class OpcodePush4 : PushOpcodeBase { public override byte Code => 0x63; public override string Name => "PUSH4"; public override int Size => 4; }
public sealed class OpcodePush5 : PushOpcodeBase { public override byte Code => 0x64; public override string Name => "PUSH5"; public override int Size => 5; }
public sealed class OpcodePush6 : PushOpcodeBase { public override byte Code => 0x65; public override string Name => "PUSH6"; public override int Size => 6; }
public sealed class OpcodePush7 : PushOpcodeBase { public override byte Code => 0x66; public override string Name => "PUSH7"; public override int Size => 7; }
public sealed class OpcodePush8 : PushOpcodeBase { public override byte Code => 0x67; public override string Name => "PUSH8"; public override int Size => 8; }
public sealed class OpcodePush9 : PushOpcodeBase { public override byte Code => 0x68; public override string Name => "PUSH9"; public override int Size => 9; }
public sealed class OpcodePush10 : PushOpcodeBase { public override byte Code => 0x69; public override string Name => "PUSH10"; public override int Size => 10; }
public sealed class OpcodePush11 : PushOpcodeBase { public override byte Code => 0x6A; public override string Name => "PUSH11"; public override int Size => 11; }
public sealed class OpcodePush12 : PushOpcodeBase { public override byte Code => 0x6B; public override string Name => "PUSH12"; public override int Size => 12; }
public sealed class OpcodePush13 : PushOpcodeBase { public override byte Code => 0x6C; public override string Name => "PUSH13"; public override int Size => 13; }
public sealed class OpcodePush14 : PushOpcodeBase { public override byte Code => 0x6D; public override string Name => "PUSH14"; public override int Size => 14; }
public sealed class OpcodePush15 : PushOpcodeBase { public override byte Code => 0x6E; public override string Name => "PUSH15"; public override int Size => 15; }
public sealed class OpcodePush16 : PushOpcodeBase { public override byte Code => 0x6F; public override string Name => "PUSH16"; public override int Size => 16; }
public sealed class OpcodePush17 : PushOpcodeBase { public override byte Code => 0x70; public override string Name => "PUSH17"; public override int Size => 17; }
public sealed class OpcodePush18 : PushOpcodeBase { public override byte Code => 0x71; public override string Name => "PUSH18"; public override int Size => 18; }
public sealed class OpcodePush19 : PushOpcodeBase { public override byte Code => 0x72; public override string Name => "PUSH19"; public override int Size => 19; }
public sealed class OpcodePush20 : PushOpcodeBase { public override byte Code => 0x73; public override string Name => "PUSH20"; public override int Size => 20; }
public sealed class OpcodePush21 : PushOpcodeBase { public override byte Code => 0x74; public override string Name => "PUSH21"; public override int Size => 21; }
public sealed class OpcodePush22 : PushOpcodeBase { public override byte Code => 0x75; public override string Name => "PUSH22"; public override int Size => 22; }
public sealed class OpcodePush23 : PushOpcodeBase { public override byte Code => 0x76; public override string Name => "PUSH23"; public override int Size => 23; }
public sealed class OpcodePush24 : PushOpcodeBase { public override byte Code => 0x77; public override string Name => "PUSH24"; public override int Size => 24; }
public sealed class OpcodePush25 : PushOpcodeBase { public override byte Code => 0x78; public override string Name => "PUSH25"; public override int Size => 25; }
public sealed class OpcodePush26 : PushOpcodeBase { public override byte Code => 0x79; public override string Name => "PUSH26"; public override int Size => 26; }
public sealed class OpcodePush27 : PushOpcodeBase { public override byte Code => 0x7A; public override string Name => "PUSH27"; public override int Size => 27; }
public sealed class OpcodePush28 : PushOpcodeBase { public override byte Code => 0x7B; public override string Name => "PUSH28"; public override int Size => 28; }
public sealed class OpcodePush29 : PushOpcodeBase { public override byte Code => 0x7C; public override string Name => "PUSH29"; public override int Size => 29; }
public sealed class OpcodePush30 : PushOpcodeBase { public override byte Code => 0x7D; public override string Name => "PUSH30"; public override int Size => 30; }
public sealed class OpcodePush31 : PushOpcodeBase { public override byte Code => 0x7E; public override string Name => "PUSH31"; public override int Size => 31; }
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

// [AI-EDIT 2026-01-10] Full DUP1-DUP16 coverage (0x80–0x8F)
public sealed class OpcodeDup1 : DupOpcodeBase { public override byte Code => 0x80; public override string Name => "DUP1"; public override int Depth => 1; }
public sealed class OpcodeDup2 : DupOpcodeBase { public override byte Code => 0x81; public override string Name => "DUP2"; public override int Depth => 2; }
public sealed class OpcodeDup3 : DupOpcodeBase { public override byte Code => 0x82; public override string Name => "DUP3"; public override int Depth => 3; }
public sealed class OpcodeDup4 : DupOpcodeBase { public override byte Code => 0x83; public override string Name => "DUP4"; public override int Depth => 4; }
public sealed class OpcodeDup5 : DupOpcodeBase { public override byte Code => 0x84; public override string Name => "DUP5"; public override int Depth => 5; }
public sealed class OpcodeDup6 : DupOpcodeBase { public override byte Code => 0x85; public override string Name => "DUP6"; public override int Depth => 6; }
public sealed class OpcodeDup7 : DupOpcodeBase { public override byte Code => 0x86; public override string Name => "DUP7"; public override int Depth => 7; }
public sealed class OpcodeDup8 : DupOpcodeBase { public override byte Code => 0x87; public override string Name => "DUP8"; public override int Depth => 8; }
public sealed class OpcodeDup9 : DupOpcodeBase { public override byte Code => 0x88; public override string Name => "DUP9"; public override int Depth => 9; }
public sealed class OpcodeDup10 : DupOpcodeBase { public override byte Code => 0x89; public override string Name => "DUP10"; public override int Depth => 10; }
public sealed class OpcodeDup11 : DupOpcodeBase { public override byte Code => 0x8A; public override string Name => "DUP11"; public override int Depth => 11; }
public sealed class OpcodeDup12 : DupOpcodeBase { public override byte Code => 0x8B; public override string Name => "DUP12"; public override int Depth => 12; }
public sealed class OpcodeDup13 : DupOpcodeBase { public override byte Code => 0x8C; public override string Name => "DUP13"; public override int Depth => 13; }
public sealed class OpcodeDup14 : DupOpcodeBase { public override byte Code => 0x8D; public override string Name => "DUP14"; public override int Depth => 14; }
public sealed class OpcodeDup15 : DupOpcodeBase { public override byte Code => 0x8E; public override string Name => "DUP15"; public override int Depth => 15; }
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

// [AI-EDIT 2026-01-10] Full SWAP1-SWAP16 coverage (0x90–0x9F)
public sealed class OpcodeSwap1 : SwapOpcodeBase { public override byte Code => 0x90; public override string Name => "SWAP1"; public override int Depth => 1; }
public sealed class OpcodeSwap2 : SwapOpcodeBase { public override byte Code => 0x91; public override string Name => "SWAP2"; public override int Depth => 2; }
public sealed class OpcodeSwap3 : SwapOpcodeBase { public override byte Code => 0x92; public override string Name => "SWAP3"; public override int Depth => 3; }
public sealed class OpcodeSwap4 : SwapOpcodeBase { public override byte Code => 0x93; public override string Name => "SWAP4"; public override int Depth => 4; }
public sealed class OpcodeSwap5 : SwapOpcodeBase { public override byte Code => 0x94; public override string Name => "SWAP5"; public override int Depth => 5; }
public sealed class OpcodeSwap6 : SwapOpcodeBase { public override byte Code => 0x95; public override string Name => "SWAP6"; public override int Depth => 6; }
public sealed class OpcodeSwap7 : SwapOpcodeBase { public override byte Code => 0x96; public override string Name => "SWAP7"; public override int Depth => 7; }
public sealed class OpcodeSwap8 : SwapOpcodeBase { public override byte Code => 0x97; public override string Name => "SWAP8"; public override int Depth => 8; }
public sealed class OpcodeSwap9 : SwapOpcodeBase { public override byte Code => 0x98; public override string Name => "SWAP9"; public override int Depth => 9; }
public sealed class OpcodeSwap10 : SwapOpcodeBase { public override byte Code => 0x99; public override string Name => "SWAP10"; public override int Depth => 10; }
public sealed class OpcodeSwap11 : SwapOpcodeBase { public override byte Code => 0x9A; public override string Name => "SWAP11"; public override int Depth => 11; }
public sealed class OpcodeSwap12 : SwapOpcodeBase { public override byte Code => 0x9B; public override string Name => "SWAP12"; public override int Depth => 12; }
public sealed class OpcodeSwap13 : SwapOpcodeBase { public override byte Code => 0x9C; public override string Name => "SWAP13"; public override int Depth => 13; }
public sealed class OpcodeSwap14 : SwapOpcodeBase { public override byte Code => 0x9D; public override string Name => "SWAP14"; public override int Depth => 14; }
public sealed class OpcodeSwap15 : SwapOpcodeBase { public override byte Code => 0x9E; public override string Name => "SWAP15"; public override int Depth => 15; }
public sealed class OpcodeSwap16 : SwapOpcodeBase { public override byte Code => 0x9F; public override string Name => "SWAP16"; public override int Depth => 16; }
