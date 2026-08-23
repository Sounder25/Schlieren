using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Execution.Inspect;
using Xunit;

namespace Schlieren.Tests.Execution.Inspect;

public sealed class InspectMapperTests
{
    [Fact]
    public void ParseGasDec_HexAndDecimal()
    {
        Assert.Equal(3, InspectMapper.ParseGasDec("0x3"));
        Assert.Equal(3, InspectMapper.ParseGasDec("3"));
        Assert.Equal(0, InspectMapper.ParseGasDec(null));
    }

    [Fact]
    public void FromStep_CopiesContractAndDecimalCost()
    {
        var step = new ExecutionTraceStep
        {
            Pc = 4,
            Op = "CALL",
            Gas = "0x100",
            GasCost = "0x64",
            Depth = 1,
            ContractAddress = "0x00000000000000000000000000000000000000aa",
            CallerAddress = "0x0000000000000000000000000000000000000001",
            CallType = CallType.Call,
            OutputData = new byte[] { 0xab }
        };

        var dto = InspectMapper.FromStep(step);
        Assert.Equal(100, dto.GasCostDec);
        Assert.Equal("0x00000000000000000000000000000000000000aa", dto.Contract);
        Assert.Equal("0x0000000000000000000000000000000000000001", dto.Caller);
        Assert.Equal("Call", dto.CallType);
        Assert.Equal("0xab", dto.Output);
    }

    [Fact]
    public void FromHit_MapsProvenGrade()
    {
        var hit = InspectMapper.FromHit(new ScoredDiagnosis
        {
            RuleId = "TX.CREATE_SURCHARGE",
            Title = "t",
            Phase = ExecutionPhase.IntrinsicGas,
            Basis = new DiagnosisProofBasis(true, true, true, true),
            Score = 90,
            Why = "w",
            Proof = "p",
            Consequences = "c",
            LikelyFix = "f",
            CodeBoundary = "b"
        });
        Assert.Equal("PROVEN", hit.Grade);
        Assert.Equal("INTRINSIC", hit.Phase);
    }
}
