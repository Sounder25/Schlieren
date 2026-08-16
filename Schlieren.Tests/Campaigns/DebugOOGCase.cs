using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

public class DebugOOGCase
{
    private readonly ITestOutputHelper _output;

    public DebugOOGCase(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectOOGBytecode()
    {
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.CaseId == "R6_CALL_COLD_OUTOFGAS_SSTORE_R0_D2_CANCUN");

        var (parent, child) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

        _output.WriteLine($"Parent: {parent}");
        _output.WriteLine($"Child: {child}");

        // Check for PUSH2 3000 (0x0bb8) = 61 0b b8
        Assert.Contains("610bb8", parent);
        
        // Child should be: PUSH1 1, PUSH1 0, SSTORE, STOP
        // 60 01 60 00 55 00
        Assert.Contains("6001600055", child);
    }
}
