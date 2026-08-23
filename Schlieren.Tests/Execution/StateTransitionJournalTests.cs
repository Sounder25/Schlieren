using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class StateTransitionJournalTests
{
    [Fact]
    public async Task ValidationFailure_AttachesTransactionOnlyJournal()
    {
        var result = await new StateTransition(new EvmMachine([])).ApplyTransactionAsync(
            new Transaction
            {
                TxType = 4,
                GasLimit = 100_000,
                Authorization = TransactionAuthorization.Impersonated,
                EnableJournal = true
            },
            new GlobalState(),
            new BlockContext { Rules = ForkRulesFactory.For("Frontier") },
            commit: false);

        Assert.Equal(EvmError.InvalidTransaction, result.Error);
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        Assert.IsType<TransactionStartedEvent>(Assert.Single(journal.Events));
    }

    [Fact]
    public async Task NestedCall_RecordsExplicitParentAndChildFrames()
    {
        var state = new GlobalState();
        var callee = Address.FromHex("0x4000000000000000000000000000000000000004");
        var caller = Address.FromHex("0x5000000000000000000000000000000000000005");
        var sender = Address.FromHex("0x2000000000000000000000000000000000000002");

        state.SetCode(callee, [0x60, 0x01, 0x60, 0x00, 0x55, 0x00]);
        var callerCode = new List<byte>
        {
            0x60, 0x00,
            0x60, 0x00,
            0x60, 0x00,
            0x60, 0x00,
            0x60, 0x00,
            0x73
        };
        callerCode.AddRange(callee.Bytes);
        callerCode.AddRange([0x61, 0x27, 0x10, 0xf1, 0x00]);
        state.SetCode(caller, callerCode.ToArray());

        var transition = new StateTransition(new EvmMachine(
        [
            new OpcodeStop(),
            new OpcodePush1(),
            new OpcodePush2(),
            new OpcodePush20(),
            new OpcodeSstore(),
            new OpcodeCall()
        ]));
        var result = await transition.ApplyTransactionAsync(
            new Transaction
            {
                From = sender,
                To = caller,
                GasLimit = 200_000,
                GasPrice = 1,
                Authorization = TransactionAuthorization.Internal,
                EnableJournal = true
            },
            state,
            new BlockContext
            {
                BaseFeePerGas = 1,
                Rules = ForkRulesFactory.For("Osaka")
            },
            commit: false);

        Assert.True(result.IsSuccess);
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        var frames = journal.Events.OfType<FrameEnteredEvent>().ToArray();
        Assert.Equal(2, frames.Length);

        var root = Assert.Single(frames, frame => frame.Depth == 0);
        var child = Assert.Single(frames, frame => frame.Depth == 1);
        Assert.Null(root.ParentFrameId);
        Assert.Equal(CallType.Root, root.CallType);
        Assert.Equal(root.FrameId, child.ParentFrameId);
        Assert.Equal(CallType.Call, child.CallType);

        Assert.Contains(
            journal.Events.OfType<OpcodeGasEvent>(),
            entry => entry.FrameId == child.FrameId && entry.Name == "SSTORE");
        Assert.Contains(
            journal.Events.OfType<FrameExitedEvent>(),
            entry => entry.FrameId == root.FrameId);
        Assert.Contains(
            journal.Events.OfType<FrameExitedEvent>(),
            entry => entry.FrameId == child.FrameId);
    }
}
