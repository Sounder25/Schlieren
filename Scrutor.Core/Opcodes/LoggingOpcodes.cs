using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public abstract class LogOpcodeBase : IOpcode
{
    public abstract byte Code { get; }
    public abstract string Name { get; }
    public abstract int TopicCount { get; }

    public ValueTask<(ExecutionResult Result, int NextPc)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var topics = new List<string>(TopicCount);
        for (int i = 0; i < TopicCount; i++)
        {
            if (!context.Stack.TryPop(out var topic))
                return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
            
            topics.Add("0x" + topic.ToString("x64"));
        }

        var offsetInt = (int)offset;
        var lengthInt = (int)length;

        var expansionGas = context.Memory.CalculateGasCost(offsetInt + lengthInt);
        var data = context.Memory.Load(offsetInt, lengthInt);

        var log = new TransactionLog
        {
            Address = context.ContractAddress.ToString(),
            Topics = topics,
            Data = "0x" + Convert.ToHexString(data).ToLowerInvariant()
        };

        context.Logs.Add(log);

        ulong gas = 375 + (ulong)(375 * TopicCount) + (ulong)(8 * lengthInt) + expansionGas;

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(gas), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeLog0 : LogOpcodeBase { public override byte Code => 0xA0; public override string Name => "LOG0"; public override int TopicCount => 0; }
public sealed class OpcodeLog1 : LogOpcodeBase { public override byte Code => 0xA1; public override string Name => "LOG1"; public override int TopicCount => 1; }
public sealed class OpcodeLog2 : LogOpcodeBase { public override byte Code => 0xA2; public override string Name => "LOG2"; public override int TopicCount => 2; }
public sealed class OpcodeLog3 : LogOpcodeBase { public override byte Code => 0xA3; public override string Name => "LOG3"; public override int TopicCount => 3; }
public sealed class OpcodeLog4 : LogOpcodeBase { public override byte Code => 0xA4; public override string Name => "LOG4"; public override int TopicCount => 4; }
