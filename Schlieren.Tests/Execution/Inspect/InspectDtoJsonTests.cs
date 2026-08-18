using System.Text.Json;
using Schlieren.Core.Execution.Inspect;
using Xunit;

namespace Schlieren.Tests.Execution.Inspect;

public sealed class InspectDtoJsonTests
{
    [Fact]
    public void RoundTrip_MatchesSchemaKeys()
    {
        var dto = new InspectResult
        {
            Ok = true,
            Fork = "Frontier",
            Execution = new InspectExecution
            {
                Success = true,
                Error = "None",
                GasUsed = "0xcf08",
                GasLimit = "0x186a0",
                RefundCounter = "0x0",
                ReturnValue = "0x"
            },
            Trace = new InspectTrace
            {
                StructLogs =
                [
                    new InspectStructLog
                    {
                        Pc = 0,
                        Op = "PUSH1",
                        Gas = "0x1869d",
                        GasCost = "0x3",
                        GasCostDec = 3,
                        Depth = 1
                    }
                ]
            },
            GasTree = new InspectGasNode { Label = "root", Gas = 0, TotalGas = 53000 },
            Diagnosis = new InspectDiagnosis
            {
                Fingerprint = "INTRINSIC / TX.CREATE_SURCHARGE / Frontier",
                FirstPhase = "INTRINSIC",
                Root = new InspectDiagnosisHit
                {
                    RuleId = "TX.CREATE_SURCHARGE",
                    Title = "Frontier CREATE surcharge",
                    Grade = "PROVEN",
                    Score = 92
                }
            }
        };

        var json = JsonSerializer.Serialize(dto, InspectJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Frontier", root.GetProperty("fork").GetString());
        Assert.True(root.TryGetProperty("execution", out _));
        Assert.True(root.TryGetProperty("trace", out var trace));
        Assert.True(trace.TryGetProperty("structLogs", out var logs));
        Assert.Equal(3, logs[0].GetProperty("gasCostDec").GetInt32());
        Assert.True(root.TryGetProperty("gasTree", out _));
        Assert.True(root.TryGetProperty("diagnosis", out var dx));
        Assert.Equal("TX.CREATE_SURCHARGE", dx.GetProperty("root").GetProperty("ruleId").GetString());
        Assert.Equal("PROVEN", dx.GetProperty("root").GetProperty("grade").GetString());
    }
}
