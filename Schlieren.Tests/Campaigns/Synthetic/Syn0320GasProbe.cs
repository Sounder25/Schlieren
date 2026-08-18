using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Targeted probe for SYN-0320: CALL + SStore + MultiSlot pre-populated + value=0.
/// Schlieren=28628, REVM=25828, delta=+2800.
///
/// Instruments the full chain:
///   SSTORE entry: address, slot, originalValue, currentValue, newValue, isWarm, cost, refund
///   Opcode gas sequence: every op with gasBefore/cost
///   TX finalization: executionGasUsed, refundCounter, appliedRefund, finalGasUsed
/// </summary>
public sealed class Syn0320GasProbe
{
    private readonly ITestOutputHelper _out;
    public Syn0320GasProbe(ITestOutputHelper output) => _out = output;

    // SYN-0320 exactly:
    // CALL, ExistingCode, SStore, High gas, Zero value, MultiSlot pre-populated
    // MultiSlot: pre-state slot 0=0xAA  slot 1=0xBB  slot 2=0xCC
    // StorageWrite(MultiSlot) = (0xAA, slot 0)  → same-value write

    private const string ParentCode =
        "0x" +
        "6000" +         // PUSH1 0   retSize
        "6000" +         // PUSH1 0   retOffset
        "6000" +         // PUSH1 0   argsSize
        "6000" +         // PUSH1 0   argsOffset
        "6000" +         // PUSH1 0   value
        "73" + "00000000000000000000000000000000000000bb" +  // PUSH20 child
        "5a" +           // GAS
        "f1" +           // CALL
        "50" +           // POP
        "00";            // STOP

    // Child: PUSH1 0xAA, PUSH1 0x00, SSTORE, STOP
    private const string ChildCode = "0x60aa60005500";

    private static Core.Execution.EvmMachine BuildMachine() =>
        new(typeof(Core.Execution.IOpcode).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)Activator.CreateInstance(t)!)
            .ToList());

    [Fact]
    public async Task SYN0320_Gas_Chain_Probe()
    {
        var machine  = BuildMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness  = new SchlierenExecutionHarness(pipeline);

        var request = new CampaignExecutionRequest
        {
            Fork     = "Cancun",
            Caller   = DeterministicAddresses.Caller,
            Target   = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value    = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Parent,
                    Code    = ParentCode,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce   = 0,
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code    = ChildCode,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce   = 0,
                    // MultiSlot pre-state — slot 0=0xAA, 1=0xBB, 2=0xCC
                    Storage = new Dictionary<string, string>
                    {
                        ["0x0"] = "0xAA",
                        ["0x1"] = "0xBB",
                        ["0x2"] = "0xCC",
                    }
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Caller,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce   = 0,
                },
            }
        };

        var result = await harness.ExecuteAsync(request);

        _out.WriteLine("=== SYN-0320 Gas Chain Probe ===");
        _out.WriteLine($"Expected (REVM): 25828");
        _out.WriteLine($"Schlieren:       {result.GasUsed}");
        _out.WriteLine($"Delta:           {(long)result.GasUsed - 25828:+#;-#;0}");
        _out.WriteLine($"Success:         {result.Success}");
        _out.WriteLine($"StateDiff:       {string.Join(", ", result.Fingerprint.StateDiff.Select(kv => $"{kv.Key}={kv.Value}"))}");
        _out.WriteLine($"GasRefund:       {result.RawTrace.GasRefundCounter}");
        _out.WriteLine("");

        // Opcode sequence with gas
        _out.WriteLine("=== Opcode gas sequence (depth=2 child frame) ===");
        var steps = result.RawTrace.TraceSteps ?? new();
        var childSteps = steps.Where(s => s.Depth == 2).ToList();
        foreach (var step in childSteps)
        {
            _out.WriteLine($"  pc={step.Pc:D3} {step.Op,-20} gasBefore={ParseHex(step.Gas),10}  cost={ParseHex(step.GasCost),6}");
        }

        _out.WriteLine("");
        _out.WriteLine("=== Root frame gas steps ===");
        var rootSteps = steps.Where(s => s.Depth == 1).ToList();
        foreach (var step in rootSteps)
        {
            _out.WriteLine($"  pc={step.Pc:D3} {step.Op,-20} gasBefore={ParseHex(step.Gas),10}  cost={ParseHex(step.GasCost),6}");
        }

        // Frame tree
        _out.WriteLine("");
        _out.WriteLine("=== Frame tree ===");
        foreach (var f in result.Fingerprint.FrameTree)
            _out.WriteLine($"  depth={f.Depth} type={f.CallType} success={f.Success} gasProvided={f.GasProvided} gasConsumed={f.GasConsumed}");
    }

    private static long ParseHex(string h)
    {
        if (string.IsNullOrEmpty(h) || h == "0x" || h == "0x0") return 0;
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        return long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
