using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Campaigns;

public class Proof_R256_Returndata
{
    private readonly ITestOutputHelper _output;

    public Proof_R256_Returndata(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Inspect_R256_Depth_Anomaly()
    {
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.CaseId == "R6_CALL_COLD_SUCCESS_NOOP_R256_V0_CODE_D2_CANCUN");

        var result = await ExecuteCase(testCase);

        _output.WriteLine("=== CALL + 256-byte return ===");
        _output.WriteLine($"TX Success: {result.Success}");
        _output.WriteLine($"TX Gas: {result.GasUsed}");
        _output.WriteLine($"Expected depth: 2");
        _output.WriteLine($"Actual depth: {(result.Fingerprint.FrameTree.Any() ? result.Fingerprint.FrameTree.Max(f => f.Depth) : 0)}");
        _output.WriteLine("");

        _output.WriteLine("Frame tree:");
        foreach (var frame in result.Fingerprint.FrameTree.OrderBy(f => f.Depth))
        {
            _output.WriteLine($"  Depth {frame.Depth}: {frame.CallType} @ {frame.CodeAddress}");
            _output.WriteLine($"    Success: {frame.Success}");
            _output.WriteLine($"    ReturnData length: {frame.ReturnData.Length}");
        }
        _output.WriteLine("");

        _output.WriteLine("Return data:");
        _output.WriteLine($"  Length: {result.ReturnData.Length} chars (including 0x prefix)");
        _output.WriteLine($"  Bytes: {(result.ReturnData.Length - 2) / 2}");
        _output.WriteLine($"  First 100 chars: {result.ReturnData.Substring(0, Math.Min(100, result.ReturnData.Length))}");
        _output.WriteLine("");

        // Check if it's actually a depth issue or frame-building issue
        _output.WriteLine("DIAGNOSIS:");
        var parentFrame = result.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 1);
        var childFrame = result.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2);
        
        if (parentFrame == null)
        {
            _output.WriteLine("✗ Parent frame (depth 1) MISSING from tree");
        }
        
        if (childFrame == null)
        {
            _output.WriteLine("✗ Child frame (depth 2) MISSING from tree");
        }

        if (parentFrame != null && childFrame == null)
        {
            _output.WriteLine("LIKELY CAUSE: FrameTree builder skips child when returndata is large");
            _output.WriteLine("CLASSIFICATION: Tracing/frame-building bug");
        }
        else if (parentFrame != null && childFrame != null)
        {
            _output.WriteLine("✓ Both frames present");
            _output.WriteLine("Frame depth is correct, test expectation may be wrong");
        }

        // Check trace steps to see if child actually executed
        _output.WriteLine("");
        _output.WriteLine("Trace validation:");
        if (result.RawTrace.TraceSteps != null && result.RawTrace.TraceSteps.Count > 0)
        {
            var maxDepth = result.RawTrace.TraceSteps.Max(s => s.Depth);
            _output.WriteLine($"  Max trace depth: {maxDepth}");
            
            var callStep = result.RawTrace.TraceSteps.FirstOrDefault(s => s.Op == "CALL");
            if (callStep != null)
            {
                _output.WriteLine($"  CALL opcode found at trace index {result.RawTrace.TraceSteps.IndexOf(callStep)}");
            }
            
            var depth2Steps = result.RawTrace.TraceSteps.Where(s => s.Depth == 2).ToList();
            _output.WriteLine($"  Depth-2 trace steps: {depth2Steps.Count}");
            
            if (depth2Steps.Any())
            {
                _output.WriteLine("  ✓ Child actually executed at depth 2");
                if (childFrame == null)
                {
                    _output.WriteLine("  ✗ But FrameTree doesn't have depth-2 frame");
                    _output.WriteLine("  VERDICT: Frame-building bug (trace has it, fingerprint drops it)");
                }
            }
        }
    }

    private async Task<CampaignExecutionResult> ExecuteCase(CallSemanticsMatrixGenerator.CallTestCase testCase)
    {
        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

        _output.WriteLine($"Test: {testCase.CaseId}");
        _output.WriteLine($"Parent: {parentCode}");
        _output.WriteLine($"Child: {childCode}");
        _output.WriteLine("");

        var request = new CampaignExecutionRequest
        {
            Fork = testCase.Fork.ToString(),
            Caller = DeterministicAddresses.Caller,
            Target = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Parent,
                    Code = parentCode,
                    Balance = "0x0",
                    Nonce = 0
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code = childCode,
                    Balance = "0x0",
                    Nonce = 0
                }
            }
        };

        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        return await harness.ExecuteAsync(request);
    }

    private static Core.Execution.EvmMachine BuildEvmMachine()
    {
        var opcodeInstances = typeof(Core.Execution.IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && 
                       typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)Activator.CreateInstance(t)!)
            .ToList();

        return new Core.Execution.EvmMachine(opcodeInstances);
    }
}
