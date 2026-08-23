using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class ExplicitCallTypeJournalTests
{
    [Fact]
    public async Task NestedCallCode_RecordsCallCodeInsteadOfDelegateCall()
    {
        var state = new GlobalState();
        var callee = Address.FromHex("0x4100000000000000000000000000000000000004");
        var caller = Address.FromHex("0x5100000000000000000000000000000000000005");
        var sender = Address.FromHex("0x2100000000000000000000000000000000000002");

        state.SetCode(callee, [0x00]);
        var callerCode = new List<byte>
        {
            0x60, 0x00, // return size
            0x60, 0x00, // return offset
            0x60, 0x00, // input size
            0x60, 0x00, // input offset
            0x60, 0x00, // value
            0x73
        };
        callerCode.AddRange(callee.Bytes);
        callerCode.AddRange([0x61, 0xc3, 0x50, 0xf2, 0x00]); // gas, CALLCODE, STOP
        state.SetCode(caller, callerCode.ToArray());

        var result = await new StateTransition(new EvmMachine(
        [
            new OpcodeStop(),
            new OpcodePush1(),
            new OpcodePush2(),
            new OpcodePush20(),
            new OpcodeCallCode()
        ])).ApplyTransactionAsync(
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
        var child = Assert.Single(
            journal.Events.OfType<FrameEnteredEvent>(),
            frame => frame.Depth == 1);

        Assert.Equal(CallType.CallCode, child.CallType);
    }

    [Fact]
    public async Task NestedCreate2_RecordsCreate2InsteadOfCreate()
    {
        var state = new GlobalState();
        var creator = Address.FromHex("0x5200000000000000000000000000000000000005");
        var sender = Address.FromHex("0x2200000000000000000000000000000000000002");

        // Empty initcode: salt, length, offset, value, CREATE2, STOP.
        state.SetCode(creator, Convert.FromHexString("6000600060006000F500"));

        var result = await new StateTransition(new EvmMachine(
        [
            new OpcodeStop(),
            new OpcodePush1(),
            new OpcodeCreate2()
        ])).ApplyTransactionAsync(
            new Transaction
            {
                From = sender,
                To = creator,
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
        var child = Assert.Single(
            journal.Events.OfType<FrameEnteredEvent>(),
            frame => frame.Depth == 1);

        Assert.Equal(CallType.Create2, child.CallType);
        Assert.Equal(
            FrameStateResolution.Commit,
            JournalAnalysis.Build(journal).Frames[child.FrameId!.Value].Resolution);
    }
}
