using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.UI.Services;

namespace Schlieren.Tests.Execution;

public sealed class GasTraceInvariantTests
{
    [Fact]
    public async Task CanonicalGasTree_TotalGasEqualsChargedGas()
    {
        var run = await BytecodeExecutionService.RunAsync(
            "600560030160005260206000f3",
            new BytecodeRunOptions
            {
                ForkLabel = "Osaka",
                GasLimit = 100_000
            });

        Assert.NotNull(run);
        Assert.True(run.Result.IsSuccess);
        Assert.NotNull(run.GasTree);

        Assert.Equal(run.Result.GasUsed, run.GasTree.TotalGas);
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
            EnableTracing = true
        };
        var block = new BlockContext
        {
            BaseFeePerGas = 1,
            Rules = ForkRulesFactory.For("Osaka")
        };

        var result = await transition.ApplyTransactionAsync(tx, state, block, commit: false);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.TraceSteps, step => step.Depth == 2);

        var frames = GasTreeFromTrace.BuildFrames(result.TraceSteps, "root");
        var child = Assert.Single(frames.Children);

        Assert.Contains(child.OpcodeSteps, step => step.op == "SSTORE");
        Assert.DoesNotContain(frames.OpcodeSteps, step => step.op == "SSTORE");
    }
}
