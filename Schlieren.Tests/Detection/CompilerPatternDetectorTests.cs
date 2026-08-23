using Schlieren.Core.Detection;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Detection;

public class CompilerPatternDetectorTests
{
    // ABI Panic(uint256): 4-byte selector at 0, 32-byte code starting at 4 (36 bytes total).
    // Word 1 therefore begins with the last 4 bytes of the uint256, not the whole word.
    private static readonly string PanicWord0 =
        "0x4e487b7100000000000000000000000000000000000000000000000000000000";
    private static readonly string PanicOverflowWord1 =
        "0x0000001100000000000000000000000000000000000000000000000000000000";
    private static readonly string PanicDivZeroWord1 =
        "0x0000001200000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void Analyze_RevertPanicInMemoryWithoutOutputData_DetectsCheckedArithmetic()
    {
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "PUSH1", Stack = new() },
            new()
            {
                Pc = 10,
                Op = "REVERT",
                Stack = new() { "0x0", "0x24" },
                Memory = new() { PanicWord0, PanicOverflowWord1 },
                OutputData = null
            }
        };

        var patterns = CompilerPatternDetector.Analyze(trace);

        var panic = Assert.Single(patterns, p => p.PatternId == "SOLIDITY_PANIC_REVERT");
        Assert.Equal(1, panic.FirstStep);
        Assert.True(panic.IsExpectedBehavior);
        Assert.Contains("overflow", panic.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RevertPanicDivisionByZeroInMemory_DetectsPanic12()
    {
        var trace = new List<ExecutionTraceStep>
        {
            new()
            {
                Pc = 4,
                Op = "REVERT",
                Stack = new() { "0x0", "0x24" },
                Memory = new() { PanicWord0, PanicDivZeroWord1 }
            }
        };

        var patterns = CompilerPatternDetector.Analyze(trace);

        var panic = Assert.Single(patterns, p => p.PatternId == "SOLIDITY_PANIC_REVERT");
        Assert.Contains("division by zero", panic.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RevertWithoutPanicSelector_DoesNotDetect()
    {
        var trace = new List<ExecutionTraceStep>
        {
            new()
            {
                Pc = 0,
                Op = "REVERT",
                Stack = new() { "0x0", "0x4" },
                Memory = new() { "0x08c379a000000000000000000000000000000000000000000000000000000000" }
            }
        };

        var patterns = CompilerPatternDetector.Analyze(trace);

        Assert.DoesNotContain(patterns, p => p.PatternId == "SOLIDITY_PANIC_REVERT");
    }

    [Fact]
    public async Task Analyze_LiveRevertTrace_DetectsPanicFromMemoryAndStack()
    {
        // Solidity Panic(0x11) in memory, then REVERT(offset=0, size=36).
        var code = new List<byte>();
        code.Add(0x7f); // PUSH32
        code.AddRange(Convert.FromHexString("4e487b7100000000000000000000000000000000000000000000000000000000"));
        code.AddRange(new byte[] { 0x60, 0x00, 0x52 }); // PUSH1 0 / MSTORE
        code.AddRange(new byte[] { 0x60, 0x11, 0x60, 0x04, 0x52 }); // PUSH1 0x11 / PUSH1 4 / MSTORE (ABI overlap)
        code.AddRange(new byte[] { 0x60, 0x24, 0x60, 0x00, 0xfd }); // PUSH1 36 / PUSH1 0 / REVERT

        var context = new EvmExecutionContext
        {
            Code = code.ToArray(),
            CaptureTrace = true,
            GasLimit = 100_000
        };
        var machine = new EvmMachine(
        [
            new OpcodePush32(),
            new OpcodePush1(),
            new OpcodeMstore(),
            new OpcodeRevert()
        ]);

        var result = await machine.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.Revert, result.Error);

        Assert.Contains(context.TraceSteps, s => s.Op == "REVERT");

        var patterns = CompilerPatternDetector.Analyze(context.TraceSteps);
        Assert.Contains(patterns, p => p.PatternId == "SOLIDITY_PANIC_REVERT");
    }
}
