using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class GasTraceInvariantTests
{
    [Fact]
    public async Task CanonicalGasTree_TotalGasEqualsChargedGas()
    {
        var state = new GlobalState();
        var target = Address.FromHex("0x9100000000000000000000000000000000000001");
        state.SetCode(target, Convert.FromHexString("600560030160005260206000F3"));
        var result = await new StateTransition(new EvmMachine(
        [
            new OpcodePush1(), new OpcodeAdd(), new OpcodeMstore(), new OpcodeReturn()
        ])).ApplyTransactionAsync(
            new Transaction
            {
                To = target,
                GasLimit = 100_000,
                Authorization = TransactionAuthorization.Internal,
                EnableJournal = true
            },
            state,
            new BlockContext { Rules = ForkRulesFactory.For("Osaka") },
            commit: false);

        Assert.True(result.IsSuccess);
        var tree = JournalGasTree.Build(result.Journal!, result);

        Assert.True(tree.Conservation.IsConserved, tree.Conservation.Delta);
        Assert.Equal(result.GasUsed, tree.Conservation.DerivedGas);
    }

    [Fact]
    public async Task NestedOpcodes_AreOwnedByChildFrame()
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
        callerCode.AddRange([0x61, 0x27, 0x10, 0xF1, 0x00]);
        state.SetCode(caller, callerCode.ToArray());

        var machine = new EvmMachine(
        [
            new OpcodeStop(),
            new OpcodePush1(),
            new OpcodePush2(),
            new OpcodePush20(),
            new OpcodeSstore(),
            new OpcodeCall()
        ]);
        var transition = new StateTransition(machine);
        var tx = new Transaction
        {
            From = sender,
            To = caller,
            GasLimit = 200_000,
            GasPrice = 1,
            Authorization = TransactionAuthorization.Internal,
            EnableTracing = true,
            EnableJournal = true
        };
        var block = new BlockContext
        {
            BaseFeePerGas = 1,
            Rules = ForkRulesFactory.For("Osaka")
        };

        var result = await transition.ApplyTransactionAsync(tx, state, block, commit: false);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.TraceSteps, step => step.Depth == 2);

        var tree = JournalGasTree.Build(result.Journal!, result);
        var rootFrame = Assert.Single(tree.Root.Children, node => node.Id.StartsWith("frame-"));
        var child = Assert.Single(rootFrame.Children, node => node.Id.StartsWith("frame-"));

        Assert.Contains(child.Children, node => node.Label.Contains("SSTORE"));
        Assert.DoesNotContain(rootFrame.Children, node => node.Label.Contains("SSTORE"));
    }
}
