using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Targeted probe for GAS-001: CALL value=1, child STOP.
/// Schlieren=30322, REVM=30328, delta=-6.
/// Print the full opcode gas sequence to find where the 6 gas disappears.
/// </summary>
public sealed class Gas001ValueCallProbe
{
    private readonly ITestOutputHelper _out;
    public Gas001ValueCallProbe(ITestOutputHelper output) => _out = output;

    // Parent: PUSH1 0 x5, PUSH20 child, PUSH1 1 (value), GAS, CALL, POP, STOP
    // Stack for CALL: gas target value argsOffset argsLength retOffset retLength
    private const string ParentCode =
        "0x" +
        "6000" +   // PUSH1 0  retSize
        "6000" +   // PUSH1 0  retOffset
        "6000" +   // PUSH1 0  argsSize
        "6000" +   // PUSH1 0  argsOffset
        "6001" +   // PUSH1 1  value = 1 wei
        "73" + "00000000000000000000000000000000000000bb" +
        "5a" +     // GAS
        "f1" +     // CALL
        "50" +     // POP
        "00";      // STOP

    // Child: PUSH1 0, PUSH1 0, RETURN(0,0) — exact SYN-0233 child
    private const string ChildCode = "0x60006000f3";

    private static Core.Execution.EvmMachine BuildMachine() =>
        new(typeof(Core.Execution.IOpcode).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)Activator.CreateInstance(t)!)
            .ToList());

    [Fact]
    public async Task GAS001_Value1_CALL_Gas_Chain()
    {
        var machine  = BuildMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness  = new SchlierenExecutionHarness(pipeline);

        var result = await harness.ExecuteAsync(new CampaignExecutionRequest
        {
            Fork     = "Cancun",
            Caller   = DeterministicAddresses.Caller,
            Target   = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value    = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount { Address = DeterministicAddresses.Parent, Code = ParentCode,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount { Address = DeterministicAddresses.Child,  Code = ChildCode,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount { Address = DeterministicAddresses.Caller,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            }
        });

        _out.WriteLine("=== GAS-001 Probe: CALL value=1, child STOP ===");
        _out.WriteLine($"Expected (REVM) : 30328");
        _out.WriteLine($"Schlieren       : {result.GasUsed}");
        _out.WriteLine($"Delta           : {(long)result.GasUsed - 30328:+#;-#;0}");
        _out.WriteLine($"Success         : {result.Success}");
        _out.WriteLine("");
        _out.WriteLine("=== Root frame opcodes (depth=1) ===");
        _out.WriteLine($"{"pc",-5} {"op",-20} {"gasBefore",12} {"cost",8}");
        var steps = result.RawTrace.TraceSteps ?? new();
        foreach (var s in steps.Where(s => s.Depth == 1))
            _out.WriteLine($"{s.Pc,-5} {s.Op,-20} {Hex(s.Gas),12} {Hex(s.GasCost),8}");

        _out.WriteLine("");
        _out.WriteLine("=== Frame tree ===");
        foreach (var f in result.Fingerprint.FrameTree)
            _out.WriteLine($"  depth={f.Depth} type={f.CallType} success={f.Success} " +
                           $"gasProvided={f.GasProvided} gasConsumed={f.GasConsumed}");

        _out.WriteLine("");
        _out.WriteLine("=== Arithmetic check ===");
        var intrinsic = 21_000UL;
        var root = result.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 1);
        var child = result.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2);
        _out.WriteLine($"  intrinsic           = {intrinsic}");
        _out.WriteLine($"  root.gasConsumed     = {root?.GasConsumed}");
        _out.WriteLine($"  child.gasConsumed    = {child?.GasConsumed}");
        _out.WriteLine($"  intrinsic + root     = {intrinsic + (root?.GasConsumed ?? 0)}");
        _out.WriteLine($"  total reported       = {result.GasUsed}");
    }

    private static long Hex(string h)
    {
        if (string.IsNullOrEmpty(h) || h is "0x" or "0x0") return 0;
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        return long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
