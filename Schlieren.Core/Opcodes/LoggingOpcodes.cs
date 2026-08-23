using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Primitives;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Core.Opcodes;

public abstract class LogOpcodeBase : IOpcode
{
    public abstract byte Code { get; }
    public abstract string Name { get; }
    public abstract int TopicCount { get; }

    public ValueTask<(ExecutionResult Result, int NextPc)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        // LOG is a state-modifying operation — forbidden in static context.
        if (context.IsStatic)
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StaticModeViolation), context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var topics = new List<string>(TopicCount);
        var topicValues = new List<BigInteger>(TopicCount);
        for (int i = 0; i < TopicCount; i++)
        {
            if (!context.Stack.TryPop(out var topic))
                return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
            
            // Topics are always 32-byte hex (pad left; trim if oversized).
            var hex = topic.ToString("x");
            if (hex.Length > 64) hex = hex[^64..];
            topics.Add("0x" + hex.PadLeft(64, '0'));
            topicValues.Add(topic);
        }

        var offsetInt = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var lengthInt = length > int.MaxValue ? int.MaxValue : (int)length;

        // EELS: if size == 0, no memory extension is needed regardless of offset.
        ulong expansionGas = lengthInt > 0
            ? context.Memory.CalculateGasCost(offsetInt + lengthInt)
            : 0;
        var data = lengthInt > 0
            ? context.Memory.Load(offsetInt, lengthInt)
            : Array.Empty<byte>();

        var log = new TransactionLog
        {
            Address = context.ContractAddress.ToString(),
            Topics = topics,
            Data = "0x" + Convert.ToHexString(data).ToLowerInvariant()
        };

        context.Logs.Add(log);
        context.RecordLog(topicValues, data);

        ulong gas = 375 + (ulong)(375 * TopicCount) + (ulong)(8 * lengthInt) + expansionGas;

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(gas), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeLog0 : LogOpcodeBase { public override byte Code => 0xA0; public override string Name => "LOG0"; public override int TopicCount => 0; }
public sealed class OpcodeLog1 : LogOpcodeBase { public override byte Code => 0xA1; public override string Name => "LOG1"; public override int TopicCount => 1; }
public sealed class OpcodeLog2 : LogOpcodeBase { public override byte Code => 0xA2; public override string Name => "LOG2"; public override int TopicCount => 2; }
public sealed class OpcodeLog3 : LogOpcodeBase { public override byte Code => 0xA3; public override string Name => "LOG3"; public override int TopicCount => 3; }
public sealed class OpcodeLog4 : LogOpcodeBase { public override byte Code => 0xA4; public override string Name => "LOG4"; public override int TopicCount => 4; }
