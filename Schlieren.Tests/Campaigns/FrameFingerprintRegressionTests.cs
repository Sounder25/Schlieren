using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Regression tests for the frame fingerprint telemetry bugs found during the
/// August 2026 hardening campaign:
///
///   Bug 1 — GasProvided was always 0 (now reads firstStep.Gas from trace)
///   Bug 2 — ReturnData was always 0x (callerStepIdx pointed at GAS not CALL)
///   Bug 3 — StateDiff was empty dict (placeholder never populated)
///
/// These tests assert both the execution semantics AND the telemetry accuracy.
/// A passing execution with wrong telemetry is still a bug.
/// </summary>
public class FrameFingerprintRegressionTests
{
    private readonly ITestOutputHelper _out;
    public FrameFingerprintRegressionTests(ITestOutputHelper output) => _out = output;

    private static Core.Execution.EvmMachine BuildMachine() =>
        new(typeof(Core.Execution.IOpcode).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)System.Activator.CreateInstance(t)!)
            .ToList());

    private async Task<CampaignExecutionResult> Execute(
        string parentCode, string childCode,
        Dictionary<string, string>? childStorage = null,
        ulong gasLimit = 10_000_000)
    {
        var machine  = BuildMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness  = new SchlierenExecutionHarness(pipeline);

        return await harness.ExecuteAsync(new CampaignExecutionRequest
        {
            Fork     = "Cancun",
            Caller   = DeterministicAddresses.Caller,
            Target   = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value    = 0,
            GasLimit = gasLimit,
            Prestate = new[]
            {
                new CampaignAccount { Address = DeterministicAddresses.Parent, Code = parentCode, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount { Address = DeterministicAddresses.Child,  Code = childCode,  Balance = "0xDE0B6B3A7640000", Nonce = 0,
                                      Storage = childStorage ?? new Dictionary<string, string>() },
                new CampaignAccount { Address = DeterministicAddresses.Caller, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            }
        });
    }

    // ── Shared bytecode ──────────────────────────────────────────────────────

    // Parent: PUSH1 0 ×5, PUSH20 child, GAS, CALL, STOP
    private const string ParentCallChildAllGas =
        "0x6000600060006000600073" +
        "00000000000000000000000000000000000000bb" +
        "5af15000";

    // Parent: PUSH1 0 ×5, PUSH20 child, PUSH1 100 gas, CALL, STOP
    private const string ParentCallChild100Gas =
        "0x6000600060006000600073" +
        "00000000000000000000000000000000000000bb" +
        "6064" +   // PUSH1 100
        "f15000";  // CALL STOP

    // Parent: call child with all gas, then RETURNDATASIZE → slot 0
    private const string ParentCallChildStoreRDS =
        "0x6000600060006000600073" +
        "00000000000000000000000000000000000000bb" +
        "5af1" +    // GAS CALL
        "3d600055" + // RETURNDATASIZE PUSH1 0 SSTORE
        "00";

    // Child variants
    private const string ChildStop         = "0x00";
    private const string ChildSStore       = "0x60aa60005500";          // SSTORE slot 0 = 0xAA, STOP
    private const string ChildSStoreRevert = "0x60aa60005560006000fd";  // SSTORE slot 0 = 0xAA, REVERT
    private const string ChildReturn32     =
        "0x7fdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef" +
        "60005260206000f3";   // MSTORE, RETURN(0, 32)
    // Child: REVERT with 4 bytes 0xdeadbeef
    // PUSH4 deadbeef, PUSH1 0, MSTORE → mem[0..31] = 0x00..00deadbeef (right-aligned)
    // REVERT(28, 4) → returns bytes 28-31 = 0xdeadbeef
    private const string ChildRevertWith4  =
        "0x63deadbeef" +  // PUSH4 0xdeadbeef
        "6000" +          // PUSH1 0
        "52" +            // MSTORE (stores right-aligned at mem[0..31])
        "6004" +          // PUSH1 4  (length)
        "601c" +          // PUSH1 28 (offset — deadbeef sits at bytes 28-31)
        "fd";             // REVERT

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SSTORE_then_REVERT_StateDiff_is_empty()
    {
        var r = await Execute(ParentCallChildAllGas, ChildSStoreRevert);

        _out.WriteLine($"Parent success={r.Success}  GasUsed={r.GasUsed}");
        _out.WriteLine($"depth=2 success={r.Fingerprint.FrameTree[0].Success} gasProvided={r.Fingerprint.FrameTree[0].GasProvided}");

        Assert.True(r.Success, "parent should succeed");
        Assert.False(r.Fingerprint.FrameTree[0].Success, "child should fail (REVERT)");
        Assert.Empty(r.Fingerprint.StateDiff); // SSTORE must be rolled back
    }

    [Fact]
    public async Task SSTORE_then_STOP_StateDiff_shows_write()
    {
        var r = await Execute(ParentCallChildAllGas, ChildSStore);

        _out.WriteLine($"Parent success={r.Success}  GasUsed={r.GasUsed}");
        _out.WriteLine($"StateDiff: {string.Join(", ", r.Fingerprint.StateDiff.Select(kv => $"{kv.Key}={kv.Value}"))}");

        Assert.True(r.Success);
        Assert.True(r.Fingerprint.FrameTree[0].Success, "child should succeed");
        Assert.Contains(r.Fingerprint.StateDiff, kv => kv.Value.Contains("0x0 → 0xAA"));
    }

    [Fact]
    public async Task Child_OOG_100gas_parent_survives_no_StateDiff()
    {
        var r = await Execute(ParentCallChild100Gas, ChildSStore);

        _out.WriteLine($"Parent success={r.Success}  GasUsed={r.GasUsed}");
        _out.WriteLine($"depth=2 gasProvided={r.Fingerprint.FrameTree[0].GasProvided} gasConsumed={r.Fingerprint.FrameTree[0].GasConsumed} success={r.Fingerprint.FrameTree[0].Success}");

        Assert.True(r.Success, "parent must survive child OOG");
        Assert.False(r.Fingerprint.FrameTree[0].Success, "child OOGs");
        Assert.Equal(100UL, r.Fingerprint.FrameTree[0].GasProvided);  // exactly 100 forwarded
        Assert.Equal(100UL, r.Fingerprint.FrameTree[0].GasConsumed); // all consumed on OOG
        Assert.Empty(r.Fingerprint.StateDiff); // SSTORE never committed
    }

    [Fact]
    public async Task GasProvided_root_equals_tx_minus_intrinsic()
    {
        var r = await Execute(ParentCallChildAllGas, ChildStop);

        var root = r.Fingerprint.FrameTree.First(f => f.Depth == 1);
        _out.WriteLine($"Root GasProvided={root.GasProvided}  (expected ~9979000)");

        // tx.GasLimit=10_000_000, intrinsic=21000 → 9_979_000
        Assert.Equal(9_979_000UL, root.GasProvided);
    }

    [Fact]
    public async Task GasProvided_child_reflects_63_64_forwarding()
    {
        var r = await Execute(ParentCallChildAllGas, ChildStop);

        var child = r.Fingerprint.FrameTree.First(f => f.Depth == 2);
        _out.WriteLine($"Child GasProvided={child.GasProvided}");

        // 63/64 of ~9 979 000 minus CALL base cost (~700) ≈ 9 820 500 ± a few hundred
        Assert.InRange(child.GasProvided, 9_800_000UL, 9_900_000UL);
    }

    [Fact]
    public async Task Child_STOP_consumes_zero_gas()
    {
        var r = await Execute(ParentCallChildAllGas, ChildStop);

        var child = r.Fingerprint.FrameTree.First(f => f.Depth == 2);
        _out.WriteLine($"Child GasConsumed={child.GasConsumed}  (expected 0 for bare STOP)");

        Assert.True(child.Success);
        Assert.Equal(0UL, child.GasConsumed);
    }

    [Fact]
    public async Task ReturnData_32bytes_captured_in_child_frame()
    {
        var r = await Execute(ParentCallChildStoreRDS, ChildReturn32);

        var child = r.Fingerprint.FrameTree.First(f => f.Depth == 2);
        _out.WriteLine($"Child returnData length={child.ReturnData.Length}  value={child.ReturnData}");
        _out.WriteLine($"Parent stored RETURNDATASIZE in slot 0: {r.Fingerprint.StateDiff.FirstOrDefault(kv => kv.Key.EndsWith(":0x0")).Value}");

        Assert.True(child.Success);
        Assert.Equal(2 + 64, child.ReturnData.Length); // "0x" + 64 hex chars = 32 bytes
        Assert.Contains("deadbeef", child.ReturnData);

        // Parent stored RETURNDATASIZE (32 = 0x20) in slot 0 — confirms EVM saw the right size
        Assert.Contains(r.Fingerprint.StateDiff, kv => kv.Value.Contains("→ 0x20"));
    }

    [Fact]
    public async Task ReturnData_REVERT_payload_captured_in_child_frame()
    {
        var r = await Execute(ParentCallChildStoreRDS, ChildRevertWith4);

        var child = r.Fingerprint.FrameTree.First(f => f.Depth == 2);
        _out.WriteLine($"Child returnData={child.ReturnData}  success={child.Success}");
        _out.WriteLine($"Parent stored RETURNDATASIZE in slot 0: {r.Fingerprint.StateDiff.FirstOrDefault(kv => kv.Key.EndsWith(":0x0")).Value}");

        Assert.False(child.Success, "child REVERTs");
        Assert.Equal(2 + 8, child.ReturnData.Length); // "0x" + 8 hex chars = 4 bytes
        Assert.Contains("deadbeef", child.ReturnData);

        // Parent still sees RETURNDATASIZE=4 even though child reverted
        Assert.Contains(r.Fingerprint.StateDiff, kv => kv.Value.Contains("→ 0x4"));
    }

    [Fact]
    public async Task RootFrame_HasNoParentCall_ReturnDataIsEmpty()
    {
        // Root frame reconstruction has no parent CALL step to source OutputData from.
        // This is expected and correct — do not "fix" root ReturnData by guessing.
        var r = await Execute(ParentCallChildAllGas, ChildReturn32);

        var root = r.Fingerprint.FrameTree.First(f => f.Depth == 1);
        _out.WriteLine($"Root returnData='{root.ReturnData}' (expected '0x' — no parent CALL step)");

        Assert.Equal("0x", root.ReturnData);
    }
}
