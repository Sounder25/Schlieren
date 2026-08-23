using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Opcodes;

namespace Schlieren.Tests.Execution;

public sealed class JournalInstructionCorrelationTests
{
    [Fact]
    public async Task EveryExecutedOpcode_HasDistinctMonotonicInstructionIdentity()
    {
        var journal = new ExecutionJournal();
        var context = new Schlieren.Core.Execution.ExecutionContext
        {
            Code = [0x60, 0x01, 0x50, 0x00], // PUSH1 1; POP; STOP
            GasLimit = 100_000,
            Journal = journal,
            JournalFrameId = 1
        };
        var machine = new EvmMachine(
        [
            new OpcodePush1(),
            new OpcodePop(),
            new OpcodeStop()
        ]);

        var result = await machine.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        var instructions = journal.Events
            .OfType<OpcodeGasEvent>()
            .Select(entry => entry.InstructionId)
            .ToArray();
        Assert.Equal(3, instructions.Length);
        Assert.All(instructions, id => Assert.NotNull(id));
        Assert.Equal(instructions.Order().ToArray(), instructions);
        Assert.Equal(instructions.Length, instructions.Distinct().Count());
    }
}
