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
    public async Task Precompile_RecordsExclusiveExecutionComponent()
    {
        var sender = Address.FromHex("0x8100000000000000000000000000000000000001");
        var identity = Address.FromHex("0x0000000000000000000000000000000000000004");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        var result = await new StateTransition(new EvmMachine([])).ApplyTransactionAsync(
            new Transaction
            {
                From = sender,
                To = identity,
                Data = [0x01, 0x02, 0x03],
                GasLimit = 100_000,
                GasPrice = 1,
                Authorization = TransactionAuthorization.Impersonated,
                EnableJournal = true
            },
            state,
            new BlockContext { BaseFeePerGas = 1, Rules = ForkRulesFactory.For("Osaka") });

        Assert.True(result.IsSuccess);
        var component = Assert.Single(result.Journal!.Events.OfType<GasComponentEvent>(),
            entry => entry.Component == GasComponents.PrecompileExecution);
        Assert.Equal(GasComponentScope.Frame, component.Scope);
        Assert.Equal(GasSemantics.ExclusiveCharge, component.Semantics);
        Assert.True(component.Amount > 0);
    }

    [Fact]
    public async Task TopLevelCreate_RecordsCodeDepositComponent()
    {
        var sender = Address.FromHex("0x8200000000000000000000000000000000000002");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        var result = await new StateTransition(new EvmMachine(
            [new OpcodePush1(), new OpcodeMstore8(), new OpcodeReturn()]))
            .ApplyTransactionAsync(
                new Transaction
                {
                    From = sender,
                    To = null,
                    Data = Convert.FromHexString("600060005360016000F3"),
                    GasLimit = 100_000,
                    GasPrice = 1,
                    Authorization = TransactionAuthorization.Impersonated,
                    EnableJournal = true
                },
                state,
                new BlockContext { BaseFeePerGas = 1, Rules = ForkRulesFactory.For("Osaka") });

        Assert.True(result.IsSuccess);
        var component = Assert.Single(result.Journal!.Events.OfType<GasComponentEvent>(),
            entry => entry.Component == GasComponents.CreateCodeDeposit);
        Assert.Equal(200UL, component.Amount);
        Assert.Equal(GasSemantics.ExclusiveCharge, component.Semantics);
    }

    [Fact]
    public async Task OsakaCalldataFloor_RecordsOnlyIncrementalCharge()
    {
        var sender = Address.FromHex("0x8300000000000000000000000000000000000003");
        var contract = Address.FromHex("0x8400000000000000000000000000000000000004");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        state.SetCode(contract, [0x00]);
        var tx = new Transaction
        {
            From = sender,
            To = contract,
            Data = new byte[10],
            GasLimit = 100_000,
            GasPrice = 1,
            Authorization = TransactionAuthorization.Impersonated,
            EnableJournal = true
        };
        var rules = ForkRulesFactory.For("Osaka");

        var result = await new StateTransition(new EvmMachine([new OpcodeStop()]))
            .ApplyTransactionAsync(tx, state,
                new BlockContext { BaseFeePerGas = 1, Rules = rules });

        Assert.True(result.IsSuccess);
        var component = Assert.Single(result.Journal!.Events.OfType<GasComponentEvent>(),
            entry => entry.Component == GasComponents.TransactionCalldataFloor);
        Assert.Equal(IntrinsicGas.ComputeFloor(tx) - IntrinsicGas.Compute(tx, rules), component.Amount);
        Assert.Equal(GasComponentScope.Transaction, component.Scope);
        Assert.Equal(GasSemantics.ExclusiveCharge, component.Semantics);
    }

    [Fact]
    public async Task ExternalTransaction_RecordsIntrinsicGasAndSettlement()
    {
        var sender = Address.FromHex("0x1000000000000000000000000000000000000001");
        var contract = Address.FromHex("0x2000000000000000000000000000000000000002");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        state.SetCode(contract, [0x00]);

        var result = await new StateTransition(new EvmMachine([new OpcodeStop()]))
            .ApplyTransactionAsync(
                new Transaction
                {
                    From = sender,
                    To = contract,
                    GasLimit = 100_000,
                    GasPrice = 1,
                    Authorization = TransactionAuthorization.Impersonated,
                    EnableJournal = true
                },
                state,
                new BlockContext
                {
                    BaseFeePerGas = 1,
                    Rules = ForkRulesFactory.For("Osaka")
                });

        Assert.True(result.IsSuccess);
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        var intrinsic = Assert.Single(journal.Events.OfType<IntrinsicGasChargedEvent>());
        Assert.Equal(21_000UL, intrinsic.Amount);
        Assert.Equal(GasSemantics.ExclusiveCharge, intrinsic.Semantics);

        var settlement = Assert.Single(journal.Events.OfType<TransactionSettledEvent>());
        Assert.Equal(result.GasUsed, settlement.ChargedGas);
        Assert.Equal(100_000UL - result.GasUsed, settlement.UnusedGasReturned);
        Assert.IsType<TransactionStartedEvent>(journal.Events[0]);
        Assert.Same(settlement, journal.Events[^1]);
        Assert.True(intrinsic.Sequence < settlement.Sequence);
    }

    [Fact]
    public async Task StorageClear_RecordsRefundCounterAndEffectiveCredit()
    {
        var sender = Address.FromHex("0x3000000000000000000000000000000000000003");
        var contract = Address.FromHex("0x4000000000000000000000000000000000000004");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        state.SetCode(contract, [0x60, 0x00, 0x60, 0x00, 0x55, 0x00]);
        state.SetStorageAt(contract, 0, 1);

        var result = await new StateTransition(new EvmMachine(
            [new OpcodeStop(), new OpcodePush1(), new OpcodeSstore()]))
            .ApplyTransactionAsync(
                new Transaction
                {
                    From = sender,
                    To = contract,
                    GasLimit = 100_000,
                    GasPrice = 1,
                    Authorization = TransactionAuthorization.Impersonated,
                    EnableJournal = true
                },
                state,
                new BlockContext
                {
                    BaseFeePerGas = 1,
                    Rules = ForkRulesFactory.For("Osaka")
                });

        Assert.True(result.IsSuccess);
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        var counter = Assert.Single(journal.Events.OfType<RefundCounterChangedEvent>());
        Assert.Equal(4_800, counter.Delta);
        Assert.Equal(GasSemantics.RefundCounter, counter.Semantics);

        var credit = Assert.Single(journal.Events.OfType<EffectiveGasRefundedEvent>());
        Assert.Equal(4_800UL, credit.Amount);
        Assert.Equal(credit.GrossGasUsed / 5, credit.RefundCap);
        Assert.Equal(GasSemantics.Credit, credit.Semantics);
        Assert.Equal(credit.GrossGasUsed - credit.Amount, result.GasUsed);
    }

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
        callerCode.AddRange([0x61, 0xc3, 0x50, 0xf1, 0x00]);
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

        var childStore = Assert.Single(
            journal.Events.OfType<OpcodeGasEvent>(),
            entry => entry.FrameId == child.FrameId && entry.Name == "SSTORE");
        var childExit = Assert.Single(
            journal.Events.OfType<FrameExitedEvent>(),
            entry => entry.FrameId == child.FrameId);
        var rootCall = Assert.Single(
            journal.Events.OfType<OpcodeGasEvent>(),
            entry => entry.FrameId == root.FrameId && entry.Name == "CALL");
        var rootExit = Assert.Single(
            journal.Events.OfType<FrameExitedEvent>(),
            entry => entry.FrameId == root.FrameId);
        Assert.True(root.Sequence < child.Sequence);
        Assert.True(child.Sequence < childStore.Sequence);
        Assert.True(childStore.Sequence < childExit.Sequence);
        Assert.True(childExit.Sequence < rootCall.Sequence);
        Assert.True(rootCall.Sequence < rootExit.Sequence);

        Assert.Contains(journal.Events.OfType<GasComponentEvent>(), component =>
            component.FrameId == root.FrameId &&
            component.OpcodeName == "CALL" &&
            component.Component == GasComponents.CallLocal &&
            component.Semantics == GasSemantics.ExclusiveCharge);
        Assert.Contains(journal.Events.OfType<GasComponentEvent>(), component =>
            component.FrameId == root.FrameId &&
            component.OpcodeName == "CALL" &&
            component.Component == GasComponents.CallForwarded &&
            component.Semantics == GasSemantics.Allocation);
        Assert.Contains(journal.Events.OfType<GasComponentEvent>(), component =>
            component.FrameId == root.FrameId &&
            component.OpcodeName == "CALL" &&
            component.Component == GasComponents.CallUnusedReturn &&
            component.Semantics == GasSemantics.Return);
    }

    [Fact]
    public async Task JournalEnabledAndDisabled_PreserveExecutionAndPostState()
    {
        var withoutJournal = await RunParityFixture(enableJournal: false);
        var withJournal = await RunParityFixture(enableJournal: true);

        Assert.Null(withoutJournal.Result.Journal);
        Assert.NotNull(withJournal.Result.Journal);
        Assert.Equal(withoutJournal.Result.IsSuccess, withJournal.Result.IsSuccess);
        Assert.Equal(withoutJournal.Result.Error, withJournal.Result.Error);
        Assert.Equal(withoutJournal.Result.GasUsed, withJournal.Result.GasUsed);
        Assert.Equal(withoutJournal.Result.GasRefundCounter, withJournal.Result.GasRefundCounter);
        Assert.Equal(withoutJournal.Result.ReturnData, withJournal.Result.ReturnData);
        Assert.Equal(withoutJournal.Result.Logs.Count, withJournal.Result.Logs.Count);
        Assert.Equal(
            withoutJournal.Result.TraceSteps.Select(TraceProjection),
            withJournal.Result.TraceSteps.Select(TraceProjection));
        Assert.Equal(withoutJournal.SenderBalance, withJournal.SenderBalance);
        Assert.Equal(withoutJournal.SenderNonce, withJournal.SenderNonce);
        Assert.Equal(withoutJournal.StorageValue, withJournal.StorageValue);
    }

    private static async Task<(
        ExecutionResult Result,
        System.Numerics.BigInteger SenderBalance,
        ulong SenderNonce,
        System.Numerics.BigInteger StorageValue)> RunParityFixture(bool enableJournal)
    {
        var sender = Address.FromHex("0x6000000000000000000000000000000000000006");
        var contract = Address.FromHex("0x7000000000000000000000000000000000000007");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        state.SetCode(contract, [0x60, 0x01, 0x60, 0x00, 0x55, 0x00]);
        var transition = new StateTransition(new EvmMachine(
            [new OpcodeStop(), new OpcodePush1(), new OpcodeSstore()]));
        var result = await transition.ApplyTransactionAsync(
            new Transaction
            {
                From = sender,
                To = contract,
                GasLimit = 100_000,
                GasPrice = 1,
                Authorization = TransactionAuthorization.Impersonated,
                EnableTracing = true,
                EnableJournal = enableJournal
            },
            state,
            new BlockContext
            {
                BaseFeePerGas = 1,
                Rules = ForkRulesFactory.For("Osaka")
            });

        return (
            result,
            await state.GetBalanceAsync(sender),
            await state.GetNonceAsync(sender),
            await state.GetStorageAtAsync(contract, 0));
    }

    private static (int Pc, string Op, string Gas, string GasCost, int Depth, string Stack)
        TraceProjection(ExecutionTraceStep step) =>
        (step.Pc, step.Op, step.Gas, step.GasCost, step.Depth, string.Join(",", step.Stack));
}
