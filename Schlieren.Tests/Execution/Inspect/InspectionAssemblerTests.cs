using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Inspect;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;

namespace Schlieren.Tests.Execution.Inspect;

public sealed class InspectionAssemblerTests
{
    private static readonly Address Sender =
        Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
    private static readonly Address Coin =
        Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");

    [Fact]
    public void FrontierCreateMismatches_AreProvenSurcharge()
    {
        var req = FrontierRequest(
        [
            $"balance mismatch for {Sender}: expected=0xf4240, actual=0xa6040",
            $"balance mismatch for {Coin}: expected=0x0, actual=0x4e200"
        ]);
        var result = ExecutionResult.Success(53_000);

        var inspect = InspectionAssembler.FromCanonical(req, result);

        Assert.True(inspect.Ok);
        Assert.Equal("Frontier", inspect.Fork);
        Assert.NotNull(inspect.Diagnosis?.Root);
        Assert.Equal("TX.CREATE_SURCHARGE", inspect.Diagnosis!.Root!.RuleId);
        Assert.Equal("PROVEN", inspect.Diagnosis.Root.Grade);
        Assert.Contains("INTRINSIC", inspect.Diagnosis.Fingerprint, StringComparison.Ordinal);
        Assert.NotNull(inspect.GasTree);
        Assert.StartsWith("0x", inspect.Execution.GasUsed);
    }

    [Fact]
    public void NoMismatches_IsNotProven()
    {
        var req = FrontierRequest([]);
        var result = ExecutionResult.Success(21_000);
        var inspect = InspectionAssembler.FromCanonical(req, result);
        Assert.NotEqual("PROVEN", inspect.Diagnosis?.Root?.Grade);
    }

    [Fact]
    public void DisableStack_EmptiesStack()
    {
        var req = FrontierRequest([]) with { DisableStack = true };
        var result = ExecutionResult.Success(21_000, traceSteps: new List<ExecutionTraceStep>
        {
            new()
            {
                Pc = 0,
                Op = "PUSH1",
                Gas = "0x10",
                GasCost = "0x3",
                Stack = new List<string> { "0x1" }
            }
        });

        var inspect = InspectionAssembler.FromCanonical(req, result);
        Assert.Single(inspect.Trace.StructLogs);
        Assert.Empty(inspect.Trace.StructLogs[0].Stack);
        Assert.Equal(3, inspect.Trace.StructLogs[0].GasCostDec);
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
            Rules = ForkRulesFactory.For("Frontier"),
            GasLimit = 30_000_000
        };
        return new InspectRequest { Tx = tx, Block = block, Mismatches = mismatches };
    }
}
