using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.UI.Services;

/// <summary>
/// Runs real bytecode (hex string) through the live Scrutor.Core EVM engine
/// and returns a fully-populated ExecutionResult with a real trace.
/// No synthetic data — every step comes from the actual interpreter loop.
/// </summary>
public static class BytecodeExecutionService
{
    // Reuse a shared machine instance — all opcodes registered once
    private static readonly EvmMachine _machine = BuildMachine();

    private static EvmMachine BuildMachine()
    {
        // Reflect-register every concrete IOpcode in Scrutor.Core (same approach as DI container)
        var opcodeInstances = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!)
            .ToList();

        return new EvmMachine(opcodeInstances);
    }

    /// <summary>
    /// Parse hex string (with or without 0x prefix, spaces/newlines ignored) and execute.
    /// Returns null if the hex is malformed.
    /// </summary>
    public static async Task<ExecutionResult?> RunAsync(
        string hexBytecode,
        ulong gasLimit = 10_000_000,
        CancellationToken ct = default)
    {
        // --- parse ---
        var cleaned = hexBytecode
            .Replace("0x", "")
            .Replace("0X", "")
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();

        if (cleaned.Length == 0)
            return null;

        if (cleaned.Length % 2 != 0)
            return null;

        byte[] code;
        try
        {
            code = Convert.FromHexString(cleaned);
        }
        catch
        {
            return null;
        }

        // --- build minimal execution context ---
        var state = new GlobalState();
        var callerAddr = new Address(new byte[20]);
        var contractAddr = new Address(Enumerable.Repeat((byte)0xAA, 20).ToArray());

        // Give the contract some code so CODESIZE etc. work
        state.SetCode(contractAddr, code);
        state.SetBalance(callerAddr, BigInteger.Pow(10, 24)); // 1M ETH — so value transfers don't OOG

        var context = new Scrutor.Core.Execution.ExecutionContext
        {
            Code = code,
            GasLimit = gasLimit,
            Caller = callerAddr,
            Origin = callerAddr,
            ContractAddress = contractAddr,
            StorageAddress = contractAddr,
            State = state,
            CaptureTrace = true,
            CallValue = BigInteger.Zero,
            CallData = Array.Empty<byte>(),
        };

        context.Access.WarmAddress(callerAddr);
        context.Access.WarmAddress(contractAddr);

        try
        {
            return await _machine.ExecuteAsync(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Return a failure result that still carries whatever trace we built
            return ExecutionResult.Failure(EvmError.InternalError, context.GasUsed) with
            {
                TraceSteps = context.TraceSteps,
                ReturnData = System.Text.Encoding.UTF8.GetBytes(ex.Message)
            };
        }
    }

    /// <summary>
    /// Best-effort human label for a raw opcode name — used in the instructions panel.
    /// </summary>
    public static string DescribeOpcode(string opName) => opName switch
    {
        "STOP"      => "Halt execution",
        "ADD"       => "a + b",
        "MUL"       => "a × b",
        "SUB"       => "a − b",
        "DIV"       => "a ÷ b (uint)",
        "MOD"       => "a % b",
        "EXP"       => "a ^ b",
        "LT"        => "a < b",
        "GT"        => "a > b",
        "EQ"        => "a == b",
        "ISZERO"    => "a == 0",
        "AND"       => "a & b",
        "OR"        => "a | b",
        "XOR"       => "a ^ b",
        "NOT"       => "~a",
        "SHL"       => "shift left",
        "SHR"       => "shift right",
        "MLOAD"     => "memory read 32B",
        "MSTORE"    => "memory write 32B",
        "MSTORE8"   => "memory write 1B",
        "SLOAD"     => "storage read",
        "SSTORE"    => "storage write",
        "JUMP"      => "unconditional jump",
        "JUMPI"     => "conditional jump",
        "JUMPDEST"  => "jump target",
        "CALL"      => "external call",
        "DELEGATECALL" => "delegatecall",
        "STATICCALL"   => "read-only call",
        "RETURN"    => "return data",
        "REVERT"    => "revert + data",
        "SHA3"      => "keccak256",
        "CALLDATALOAD" => "read calldata",
        "CALLDATASIZE" => "calldata length",
        "CODESIZE"  => "bytecode length",
        "GAS"       => "remaining gas",
        "CALLER"    => "msg.sender",
        "ADDRESS"   => "this address",
        "CALLVALUE" => "msg.value",
        _ when opName.StartsWith("PUSH") => $"push {opName[4..]}B literal",
        _ when opName.StartsWith("DUP")  => $"dup stack[{opName[3..]}]",
        _ when opName.StartsWith("SWAP") => $"swap stack[{opName[4..]}]",
        _ when opName.StartsWith("LOG")  => $"emit log{opName[3..]} topics",
        _ => ""
    };
}
