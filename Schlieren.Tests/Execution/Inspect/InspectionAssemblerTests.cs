using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Execution.Inspect;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.Tests.Inspect;
using Xunit;

namespace Schlieren.Tests.Execution.Inspect;

public sealed class InspectionAssemblerTests
{
    private static readonly Address Sender = Address.FromHex(InspectGoldenCase.SenderHex);
    private static readonly Address Coin = Address.FromHex(InspectGoldenCase.CoinbaseHex);

    [Fact]
    public void MissingJournal_IsRejectedInsteadOfReconstructedFromTrace()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InspectionAssembler.FromCanonical(
                FrontierRequest([]),
                ExecutionResult.Success(21_000)));

        Assert.Contains("journal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyMismatchStrings_AreDisplayOnlyAndCannotProveSurcharge()
    {
        var req = FrontierRequest(InspectGoldenCase.Mismatches);
        var result = WithJournal(ExecutionResult.Success(53_000));

        var inspect = InspectionAssembler.FromCanonical(req, result);

        Assert.True(inspect.Ok);
        Assert.Equal("Frontier", inspect.Fork);
        Assert.NotNull(inspect.Diagnosis?.Root);
        Assert.Equal("POSSIBLE", inspect.Diagnosis!.Root!.Grade);
        Assert.NotNull(inspect.GasTree);
        Assert.StartsWith("0x", inspect.Execution.GasUsed);
    }

    [Fact]
    public void TypedFeePair_ProvesFrontierCreateSurcharge()
    {
        var request = FrontierRequest(InspectGoldenCase.Mismatches) with
        {
            Discrepancies =
            [
                Balance(Sender, 1_000_000, 680_000),
                Balance(Coin, 0, 320_000)
            ]
        };

        var inspect = InspectionAssembler.FromCanonical(request, WithJournal(ExecutionResult.Success(53_000)));

        Assert.Equal("TX.CREATE_SURCHARGE", inspect.Diagnosis!.Root!.RuleId);
        Assert.Equal("PROVEN", inspect.Diagnosis.Root.Grade);
    }

    [Fact]
    public void NoMismatches_IsNotProven()
    {
        var req = FrontierRequest([]);
        var result = WithJournal(ExecutionResult.Success(21_000));
        var inspect = InspectionAssembler.FromCanonical(req, result);
        Assert.NotEqual("PROVEN", inspect.Diagnosis?.Root?.Grade);
    }

    [Fact]
    public void DisableStack_EmptiesStack()
    {
        var req = FrontierRequest([]) with { DisableStack = true };
        var result = WithJournal(ExecutionResult.Success(21_000, traceSteps: new List<ExecutionTraceStep>
        {
            new()
            {
                Pc = 0,
                Op = "PUSH1",
                Gas = "0x10",
                GasCost = "0x3",
                Stack = new List<string> { "0x1" }
            }
        }));

        var inspect = InspectionAssembler.FromCanonical(req, result);
        Assert.Single(inspect.Trace.StructLogs);
        Assert.Empty(inspect.Trace.StructLogs[0].Stack);
        Assert.Equal(3, inspect.Trace.StructLogs[0].GasCostDec);
    }

    [Fact]
    public async Task LiveFrontierCreate_FromCanonical_IsProvenSurcharge()
    {
        var sender = Address.FromHex(InspectGoldenCase.SenderHex);
        var coin = Address.FromHex(InspectGoldenCase.CoinbaseHex);
        var state = new GlobalState();
        state.SetBalance(sender, 10_000_000_000);

        var opcodes = new List<IOpcode> { new OpcodeStop(), new OpcodePush1() };
        var st = new StateTransition(new EvmMachine(opcodes));
        var tx = new Transaction
        {
            From = sender,
            To = null,
            GasPrice = 10,
            GasLimit = 100_000,
            Data = Convert.FromHexString(InspectGoldenCase.InitcodeHex[2..]),
            Authorization = TransactionAuthorization.Simulation,
            EnableTracing = true,
            EnableJournal = true
        };
        var block = new BlockContext
        {
            Coinbase = coin,
            Rules = ForkRulesFactory.For(InspectGoldenCase.Fork),
            GasLimit = 30_000_000
        };

        var result = await st.ApplyTransactionAsync(tx, state, block, commit: false);
        var inspect = InspectionAssembler.FromCanonical(
            new InspectRequest
            {
                Tx = tx,
                Block = block,
                Mismatches = InspectGoldenCase.Mismatches,
                Discrepancies =
                [
                    Balance(sender, 1_000_000, 680_000),
                    Balance(coin, 0, 320_000)
                ]
            },
            result);

        Assert.True(inspect.Ok);
        Assert.Equal("Frontier", inspect.Fork);
        Assert.NotNull(inspect.Diagnosis?.Root);
        Assert.Equal("TX.CREATE_SURCHARGE", inspect.Diagnosis!.Root!.RuleId);
        Assert.Equal("PROVEN", inspect.Diagnosis.Root.Grade);
        Assert.True(inspect.Trace.StructLogs.Count > 0);
        Assert.True(inspect.Trace.StructLogs[0].GasCostDec >= 0);
        Assert.NotNull(inspect.GasTree);
    }

    private static InspectRequest FrontierRequest(string[] mismatches)
    {
        var tx = new Transaction
        {
            From = Sender,
            To = null,
            GasPrice = 10,
            GasLimit = 100_000,
            Data = new byte[32]
        };
        var block = new BlockContext
        {
            Coinbase = Coin,
            Rules = ForkRulesFactory.For(InspectGoldenCase.Fork),
            GasLimit = 30_000_000
        };
        return new InspectRequest { Tx = tx, Block = block, Mismatches = mismatches };
    }

    private static StateDiscrepancy Balance(Address address, long expected, long actual) => new()
    {
        Kind = DiscrepancyKind.Balance,
        Address = address,
        ExpectedNumber = expected,
        ActualNumber = actual
    };

    private static ExecutionResult WithJournal(ExecutionResult result)
    {
        var journal = new ExecutionJournal();
        journal.Record(new IntrinsicGasChargedEvent { Amount = result.GasUsed });
        journal.Record(new TransactionSettledEvent
        {
            ChargedGas = result.GasUsed,
            UnusedGasReturned = 0
        });
        return result with { Journal = journal };
    }
}
