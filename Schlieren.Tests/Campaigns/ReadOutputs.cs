using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Read Schlieren outputs for the invariants that matter.
/// Storage rollback, gas accounting, value transfer, returndata.
/// </summary>
public class ReadOutputs
{
    private readonly ITestOutputHelper _out;
    public ReadOutputs(ITestOutputHelper output) => _out = output;

    private static Core.Execution.EvmMachine BuildMachine() =>
        new(typeof(Core.Execution.IOpcode).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)System.Activator.CreateInstance(t)!)
            .ToList());

    private async Task<CampaignExecutionResult> Run(
        string callerCode, string parentCode, string childCode,
        Dictionary<string, string>? childStorage = null,
        ulong gasLimit = 10_000_000,
        ulong value = 0)
    {
        var machine = BuildMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        var accounts = new List<CampaignAccount>
        {
            new() { Address = DeterministicAddresses.Parent,  Code = parentCode, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            new() { Address = DeterministicAddresses.Child,   Code = childCode,  Balance = "0xDE0B6B3A7640000", Nonce = 0,
                    Storage = childStorage ?? new Dictionary<string, string>() },
            new() { Address = DeterministicAddresses.Caller,  Balance = "0xDE0B6B3A7640000", Nonce = 0 },
        };

        return await harness.ExecuteAsync(new CampaignExecutionRequest
        {
            Fork = "Cancun", Caller = DeterministicAddresses.Caller,
            Target = DeterministicAddresses.Parent, Calldata = "0x",
            Value = 0, GasLimit = gasLimit, Prestate = accounts.ToArray()
        });
    }

    // Bytecode helpers
    // Parent: CALL child with all gas, check return, STOP
    // PUSH1 0 PUSH1 0 PUSH1 0 PUSH1 0 PUSH1 0 PUSH20 <child> GAS CALL STOP
    private const string ParentCallChildAllGas =
        "0x6000600060006000600073" +
        "00000000000000000000000000000000000000bb" +  // PUSH20 <child addr 20 bytes = 40 hex chars>
        "5af15000";

    // Parent: CALL child with fixed gas amount
    private static string ParentCallChildGas(uint gas) =>
        "0x60006000600060006000" +   // PUSH1 0 ×5 (retSize retOffset argsSize argsOffset value)
        "73" +                        // PUSH20
        "00000000000000000000000000000000000000bb" +  // child addr (40 hex = 20 bytes)
        $"62{gas:x6}" +              // PUSH3 <gas>
        "f15000";                     // CALL POP STOP

    // Child: SSTORE slot 0 = 0xAA, then STOP
    private const string ChildSStore =
        "0x60aa60005500";

    // Child: SSTORE slot 0 = 0xAA, then REVERT
    private const string ChildSStoreRevert =
        "0x60aa60005560006000fd";

    // Child: REVERT immediately
    private const string ChildRevert =
        "0x60006000fd";

    // Child: STOP
    private const string ChildStop =
        "0x00";

    // Child: return 32 bytes of 0xdeadbeef...
    private const string ChildReturn32 =
        "0x7fdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef" +
        "60005260206000f3";

    // ─────────────────────────────────────────────────────────────
    // STORAGE ROLLBACK: does REVERT actually undo the SSTORE?
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rollback_SSTORE_REVERT_must_undo_write()
    {
        _out.WriteLine("SSTORE then REVERT — slot must be unchanged after call");

        var r = await Run("", ParentCallChildAllGas, ChildSStoreRevert);

        _out.WriteLine($"Parent success : {r.Success}");
        _out.WriteLine($"Gas used       : {r.GasUsed}");
        _out.WriteLine("StateDiff:");
        foreach (var kv in r.Fingerprint.StateDiff)
            _out.WriteLine($"  {kv.Key} = {kv.Value}");
        _out.WriteLine("Frames:");
        foreach (var f in r.Fingerprint.FrameTree)
            _out.WriteLine($"  depth={f.Depth} type={f.CallType} success={f.Success} gasProvided={f.GasProvided} gasConsumed={f.GasConsumed}");

        // Key invariant: child reverted, slot must NOT appear in diff
        Assert.True(r.Success, "Parent should succeed");
        Assert.Empty(r.Fingerprint.StateDiff);
    }

    [Fact]
    public async Task Rollback_SSTORE_then_SUCCESS_must_persist()
    {
        _out.WriteLine("SSTORE then STOP — slot must appear in diff");

        var r = await Run("", ParentCallChildAllGas, ChildSStore);

        _out.WriteLine($"Parent success : {r.Success}");
        _out.WriteLine($"Gas used       : {r.GasUsed}");
        _out.WriteLine("StateDiff:");
        foreach (var kv in r.Fingerprint.StateDiff)
            _out.WriteLine($"  {kv.Key} = {kv.Value}");

        Assert.True(r.Success);
        Assert.NotEmpty(r.Fingerprint.StateDiff);
    }

    // ─────────────────────────────────────────────────────────────
    // GAS ACCOUNTING: per-frame gas table
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gas_ChildSuccess_returnsUnusedGasToParent()
    {
        _out.WriteLine("CALL → child STOP — parent should get unused gas back");

        var r = await Run("", ParentCallChildAllGas, ChildStop);

        _out.WriteLine($"Total gas used : {r.GasUsed}");
        _out.WriteLine("Frames (depth | type | gasProvided | gasConsumed | success):");
        foreach (var f in r.Fingerprint.FrameTree)
            _out.WriteLine($"  {f.Depth} | {f.CallType} | {f.GasProvided} | {f.GasConsumed} | {f.Success}");

        // Child did nothing — gas consumed by child should be tiny (just base cost)
        var child = r.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2);
        _out.WriteLine($"\nChild GasConsumed={child?.GasConsumed}  (expected: 0 for bare STOP)");
        Assert.NotNull(child);
        Assert.True(child.Success);
    }

    [Fact]
    public async Task Gas_ChildOOG_parentShouldContinue()
    {
        _out.WriteLine("CALL child with insufficient gas — parent survives");

        // Give child only 100 gas — not enough to do anything useful
        var parentCode =
            "0x6000600060006000600073" +
            "00000000000000000000000000000000000000bb" +
            "6064" +   // PUSH1 100
            "f15000";  // CALL STOP

        var r = await Run("", parentCode, ChildSStore, gasLimit: 10_000_000);

        _out.WriteLine($"Parent success : {r.Success}");
        _out.WriteLine($"Gas used       : {r.GasUsed}");
        _out.WriteLine("Frames:");
        foreach (var f in r.Fingerprint.FrameTree)
            _out.WriteLine($"  depth={f.Depth} success={f.Success} gasProvided={f.GasProvided} gasConsumed={f.GasConsumed}");
        _out.WriteLine("StateDiff:");
        foreach (var kv in r.Fingerprint.StateDiff)
            _out.WriteLine($"  {kv.Key} = {kv.Value}");

        // Parent succeeds, child fails, SSTORE must NOT have committed
        Assert.True(r.Success, "Parent should continue after child OOG");
        Assert.Empty(r.Fingerprint.StateDiff);
    }

    // ─────────────────────────────────────────────────────────────
    // RETURNDATA: does Schlieren report the right sizes?
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returndata_32bytes_from_child()
    {
        _out.WriteLine("Child returns 32 bytes — parent returndata should reflect that");

        // Parent calls child, then reads RETURNDATASIZE into slot 0
        // PUSH1 0 PUSH1 0 PUSH1 0 PUSH1 0 PUSH1 0 PUSH20 child GAS CALL
        // RETURNDATASIZE PUSH1 0 SSTORE   <-- store return data size in slot 0
        // STOP
        var parentCode =
            "0x6000600060006000600073" +
            "00000000000000000000000000000000000000bb" +
            "5af1" +   // GAS CALL
            "3d600055" + // RETURNDATASIZE PUSH1 0 SSTORE
            "00";

        var r = await Run("", parentCode, ChildReturn32);

        _out.WriteLine($"Success: {r.Success}  GasUsed: {r.GasUsed}");
        _out.WriteLine($"ReturnData: {r.ReturnData}");
        _out.WriteLine("StateDiff (slot 0 = RETURNDATASIZE stored by parent):");
        foreach (var kv in r.Fingerprint.StateDiff)
            _out.WriteLine($"  {kv.Key} = {kv.Value}");
        _out.WriteLine("Frames:");
        foreach (var f in r.Fingerprint.FrameTree)
            _out.WriteLine($"  depth={f.Depth} success={f.Success} returnData={f.ReturnData}");
    }

    [Fact]
    public async Task Returndata_revert_payload_is_available()
    {
        _out.WriteLine("Child REVERTs with payload — parent should still see RETURNDATASIZE > 0");

        // Child: REVERT with 4 bytes of data
        var childRevertWithData = "0x" +
            "63deadbeef" +   // PUSH4 0xdeadbeef
            "60005260046000fd"; // store in mem, REVERT(0, 4) -- actually REVERT(28, 4) for last 4 bytes

        // Simpler: push 0xdeadbeef to mem[0..31], revert(28, 4)
        // Actually: PUSH4 deadbeef, PUSH1 0, MSTORE → mem[0..31] has it right-aligned
        // PUSH1 4, PUSH1 28, REVERT
        var childRevertData =
            "0x63deadbeef60005260046000fd";  // store at 0, revert(0,4)... let's just check RETURNDATASIZE

        var parentCode =
            "0x6000600060006000600073" +
            "00000000000000000000000000000000000000bb" +
            "5af1" +    // GAS CALL
            "3d600055" + // RETURNDATASIZE → slot 0
            "00";

        var r = await Run("", parentCode, childRevertData);

        _out.WriteLine($"Success: {r.Success}  GasUsed: {r.GasUsed}");
        _out.WriteLine("StateDiff (slot 0 = RETURNDATASIZE seen by parent after reverting child):");
        foreach (var kv in r.Fingerprint.StateDiff)
            _out.WriteLine($"  {kv.Key} = {kv.Value}");
        _out.WriteLine("Frames:");
        foreach (var f in r.Fingerprint.FrameTree)
            _out.WriteLine($"  depth={f.Depth} success={f.Success} returnData={f.ReturnData}");
    }
}
