using System.Text.Json;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Execution;

public sealed class JournalTraceAssemblerTests
{
    [Fact]
    public void Assembler_ProjectsFramesStepsEventsTreeAndSnapshots()
    {
        var result = BuildResult();

        var dto = JournalTraceAssembler.FromCanonical("Osaka", result);

        Assert.True(dto.Ok);
        Assert.Equal("Osaka", dto.Fork);
        Assert.Equal(2, dto.Frames.Count);
        Assert.Equal(dto.Frames[0].Id, dto.Frames[1].ParentId);
        var step = Assert.Single(dto.Steps);
        Assert.Equal(dto.Frames[1].Id, step.FrameId);
        Assert.NotNull(step.Stack);
        Assert.NotNull(step.Memory);
        Assert.NotNull(step.Storage);
        Assert.Contains(dto.Events, entry => entry.Kind == "opcodeGas");
        Assert.True(dto.Conservation.IsConserved);
        Assert.Equal(dto.Conservation.DerivedGas, dto.GasTree.TotalGas);
    }

    [Fact]
    public void SnapshotDisableFlags_OmitOnlyRequestedFields()
    {
        var dto = JournalTraceAssembler.FromCanonical(
            "Osaka",
            BuildResult(),
            new JournalTraceOptions(DisableStack: true, DisableStorage: true));

        var step = Assert.Single(dto.Steps);
        Assert.Null(step.Stack);
        Assert.NotNull(step.Memory);
        Assert.Null(step.Storage);
        var json = JsonSerializer.Serialize(step);
        Assert.DoesNotContain("Stack", json);
        Assert.Contains("Memory", json);
        Assert.DoesNotContain("Storage", json);
    }

    private static ExecutionResult BuildResult()
    {
        var journal = new ExecutionJournal();
        var root = journal.OpenFrame(null);
        var child = journal.OpenFrame(root);
        journal.Record(new FrameEnteredEvent
        {
            FrameId = root,
            Depth = 0,
            CallType = CallType.Root,
            ContractAddress = Address.Zero,
            GasLimit = 100
        });
        journal.Record(new FrameEnteredEvent
        {
            FrameId = child,
            ParentFrameId = root,
            Depth = 1,
            CallType = CallType.Call,
            ContractAddress = Address.Zero,
            GasLimit = 10
        });
        journal.Record(new OpcodeGasEvent
        {
            FrameId = child,
            ParentFrameId = root,
            Pc = 0,
            Opcode = 0x60,
            Name = "PUSH1",
            GasBefore = 10,
            GasAfter = 7,
            Amount = 3,
            Semantics = GasSemantics.ExclusiveCharge,
            Depth = 1,
            Stack = ["0x01"],
            Memory = ["00"],
            Storage = new Dictionary<string, string> { ["0x00"] = "0x01" }
        });
        journal.Record(new FrameExitedEvent
        {
            FrameId = child,
            ParentFrameId = root,
            Depth = 1,
            Success = true,
            Error = EvmError.None,
            GasUsed = 3,
            GasRemaining = 7
        });
        journal.Record(new FrameExitedEvent
        {
            FrameId = root,
            Depth = 0,
            Success = true,
            Error = EvmError.None,
            GasUsed = 3,
            GasRemaining = 97
        });
        return ExecutionResult.Success(3, [0xaa]) with { Journal = journal };
    }
}
