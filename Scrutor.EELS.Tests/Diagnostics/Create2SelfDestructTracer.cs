// Diagnostics/Create2SelfDestructTracer.cs
// Targeted gas + balance trace for the failing fixture:
//   test_dynamic_create2_selfdestruct_collision[...-call_create2_contract_at_the_end_True-create2_dest_already_in_state_True]
//
// Run with:
//   $env:EELS_FIXTURES_ROOT="C:/projects/Scrutor/fixtures/state_tests/cancun"
//   $env:EELS_INCLUDE_SUBDIRS="1"
//   dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "Create2SelfDestructTracer"

using System.Numerics;
using System.Text;
using Scrutor.Core.Execution;
using Scrutor.Core.State;
using Scrutor.EELS.Tests.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Scrutor.EELS.Tests.Diagnostics;

public sealed class Create2SelfDestructTracer
{
    private readonly ITestOutputHelper _output;

    public Create2SelfDestructTracer(ITestOutputHelper output)
    {
        _output = output;
    }

    // -----------------------------------------------------------------
    // Test 1: Gas + balance trace for the primary collision case
    // -----------------------------------------------------------------
    [Fact]
    public async Task Trace_DynamicCreate2SelfDestructCollision_AtEnd_AlreadyInState()
    {
        var casePattern = Environment.GetEnvironmentVariable("EELS_CASE_PATTERN");
        if (string.IsNullOrWhiteSpace(casePattern))
        {
            casePattern = "call_create2_contract_at_the_end_True-create2_dest_already_in_state_True";
        }

        var tc = LoadSingleCase(casePattern);
        if (tc is null)
        {
            _output.WriteLine($"Case matching '{casePattern}' not found — set EELS_FIXTURES_ROOT.");
            return;
        }

        _output.WriteLine($"=== CASE ID ===");
        _output.WriteLine(tc.CaseId);
        _output.WriteLine("");

        // --- Pre-state balances ---
        _output.WriteLine("=== PRE-STATE BALANCES ===");
        foreach (var (addr, acct) in tc.PreState.OrderBy(kv => kv.Key.ToString()))
        {
            _output.WriteLine($"  {addr}  nonce={acct.Nonce}  balance={FormatWei(acct.Balance)}  code={acct.Code.Length}B");
            foreach (var (slot, val) in acct.Storage)
                _output.WriteLine($"    slot[{slot}]={val}");
        }
        _output.WriteLine("");

        // --- Fixture block env ---
        _output.WriteLine("=== BLOCK CONTEXT ===");
        _output.WriteLine($"  Coinbase    : {tc.BlockContext.Coinbase}");
        _output.WriteLine($"  BaseFee     : {tc.BlockContext.BaseFeePerGas} (0x{tc.BlockContext.BaseFeePerGas:x})");
        _output.WriteLine($"  BlockNumber : {tc.BlockContext.Number}");
        _output.WriteLine("");

        // --- Transaction ---
        _output.WriteLine("=== TRANSACTION ===");
        _output.WriteLine($"  From        : {tc.Transaction.From}");
        _output.WriteLine($"  To          : {tc.Transaction.To}");
        _output.WriteLine($"  GasLimit    : {tc.Transaction.GasLimit}");
        _output.WriteLine($"  GasPrice    : {tc.Transaction.GasPrice} (0x{tc.Transaction.GasPrice:x})");
        _output.WriteLine($"  Value       : {tc.Transaction.Value}");
        _output.WriteLine($"  Nonce       : {tc.Transaction.Nonce}");
        _output.WriteLine($"  Data        : 0x{Convert.ToHexString(tc.Transaction.Data ?? Array.Empty<byte>())}");
        _output.WriteLine("");

        // --- Execute with tracing ---
        var globalState = BuildGlobalState(tc);
        tc.Transaction.EnableTracing = true;

        var opcodes = OpcodeCatalog.CreateAll();
        var stateTransition = new StateTransition(new EvmMachine(opcodes));
        var result = await stateTransition.ApplyTransactionAsync(
            tc.Transaction, globalState, tc.BlockContext, commit: true);

        _output.WriteLine("=== EXECUTION RESULT ===");
        _output.WriteLine($"  IsSuccess   : {result.IsSuccess}");
        _output.WriteLine($"  GasUsed     : {result.GasUsed}");
        _output.WriteLine($"  GasLimit    : {tc.Transaction.GasLimit}");
        _output.WriteLine($"  GasRefundCounter: {result.GasRefundCounter}");
        _output.WriteLine("");

        // --- Gas trace ---
        _output.WriteLine("=== GAS TRACE (pc | opcode | step_cost | gas_before | gas_after | depth) ===");
        ulong running = 0;
        foreach (var step in result.TraceSteps)
        {
            var cost = ParseHex(step.GasCost);
            running += cost;
            _output.WriteLine($"  {step.Pc,5} | {step.Op,-14} | cost={cost,6} | depth={step.Depth}");
        }
        _output.WriteLine($"  --- Total traced opcode cost: {running}");
        _output.WriteLine($"  --- GasUsed from result:      {result.GasUsed}");
        _output.WriteLine("");

        // --- Post-state balances ---
        _output.WriteLine("=== SCRUTOR POST-STATE BALANCES ===");
        var snapshot = globalState.Snapshot();
        var allAddresses = snapshot.Keys
            .Concat(tc.ExpectedPostState.Keys)
            .Distinct()
            .OrderBy(a => a.ToString());

        foreach (var addr in allAddresses)
        {
            snapshot.TryGetValue(addr, out var actual);
            tc.ExpectedPostState.TryGetValue(addr, out var expected);

            var actualBal  = actual?.Balance ?? BigInteger.Zero;
            var expectedBal = expected?.Balance ?? BigInteger.Zero;
            var balTag = actualBal == expectedBal ? "OK " : "BAD";

            var actualNonce  = actual?.Nonce ?? 0;
            var expectedNonce = expected?.Nonce ?? 0;
            var nonceTag = actualNonce == expectedNonce ? "OK " : "BAD";

            _output.WriteLine($"  [{balTag}] [{nonceTag}] {addr}");
            _output.WriteLine($"         balance: actual={FormatWei(actualBal)}  expected={FormatWei(expectedBal)}  delta={actualBal - expectedBal}");
            _output.WriteLine($"         nonce:   actual={actualNonce}  expected={expectedNonce}");
        }
        _output.WriteLine("");

        // --- Mismatches summary ---
        var executor = new EelsStateFixtureExecutor();
        var report = await executor.ExecuteAsync(tc);
        _output.WriteLine("=== MISMATCHES ===");
        if (report.Mismatches.Count == 0)
        {
            _output.WriteLine("  (none - test passes)");
        }
        else
        {
            foreach (var m in report.Mismatches)
                _output.WriteLine($"  {m}");
        }

        // Do NOT assert pass/fail here — this is a diagnostic test only.
        // We always "pass" the xUnit test so the output is emitted.
    }

    // -----------------------------------------------------------------
    // Test 2: Targeted balance ledger — sender and coinbase only
    // -----------------------------------------------------------------
    [Fact]
    public async Task Trace_BalanceLedger_SenderAndCoinbase()
    {
        const string CasePattern = "call_create2_contract_at_the_end_True-create2_dest_already_in_state_True";
        var tc = LoadSingleCase(CasePattern);
        if (tc is null)
        {
            _output.WriteLine("Case not found.");
            return;
        }

        var globalState = BuildGlobalState(tc);
        var opcodes     = OpcodeCatalog.CreateAll();
        var stateTransition = new StateTransition(new EvmMachine(opcodes));
        var result = await stateTransition.ApplyTransactionAsync(
            tc.Transaction, globalState, tc.BlockContext, commit: true);

        _output.WriteLine("=== BALANCE LEDGER RECONCILIATION ===");

        var snapshot = globalState.Snapshot();

        // Key participants
        var sender    = tc.Sender;
        var coinbase  = tc.BlockContext.Coinbase;

        tc.PreState.TryGetValue(sender, out var preSender);
        tc.PreState.TryGetValue(coinbase, out var preCoinbase);

        snapshot.TryGetValue(sender, out var postSender);
        snapshot.TryGetValue(coinbase, out var postCoinbase);

        tc.ExpectedPostState.TryGetValue(sender, out var expSender);
        tc.ExpectedPostState.TryGetValue(coinbase, out var expCoinbase);

        var gasPricePaid   = (BigInteger)tc.Transaction.GasPrice;
        var gasLimit       = (BigInteger)tc.Transaction.GasLimit;
        var gasUsed        = (BigInteger)result.GasUsed;
        var gasRefundCounter = result.GasRefundCounter;
        var baseFee        = (BigInteger)tc.BlockContext.BaseFeePerGas;

        _output.WriteLine($"GasPrice     : {gasPricePaid}");
        _output.WriteLine($"GasLimit     : {gasLimit}");
        _output.WriteLine($"GasUsed      : {gasUsed}");
        _output.WriteLine($"GasRefundCounter: {gasRefundCounter}");
        _output.WriteLine($"BaseFee      : {baseFee}");
        _output.WriteLine($"EffectiveTip : {gasPricePaid - baseFee}");
        _output.WriteLine("");

        // Sender ledger
        var senderPreBal  = preSender?.Balance  ?? BigInteger.Zero;
        var senderPostBal = postSender?.Balance ?? BigInteger.Zero;
        var senderExpBal  = expSender?.Balance  ?? BigInteger.Zero;

        var maxGasCost    = gasLimit * gasPricePaid;
        var unusedGasCost = (gasLimit - gasUsed) * gasPricePaid;
        // EIP-3529: capped refund = min(GasRefundCounter, totalGasUsed/5)
        var cappedRefund  = BigInteger.Min((BigInteger)gasRefundCounter, gasUsed / 5);
        var refundPaid    = cappedRefund * gasPricePaid;

        _output.WriteLine("--- SENDER ---");
        _output.WriteLine($"  PreBalance          : {FormatWei(senderPreBal)}");
        _output.WriteLine($"  GasRefundCounter    : {gasRefundCounter}");
        _output.WriteLine($"  CappedRefund        : {cappedRefund}");
        _output.WriteLine($"  - max_gas_cost      : {FormatWei(maxGasCost)}  ({gasLimit} * {gasPricePaid})");
        _output.WriteLine($"  + unused_gas_refund : {FormatWei(unusedGasCost)}  ({gasLimit - gasUsed} * {gasPricePaid})");
        _output.WriteLine($"  + gas_refund_paid   : {FormatWei(refundPaid)}");
        _output.WriteLine($"  Expected (formula)  : {FormatWei(senderPreBal - gasUsed * gasPricePaid + refundPaid)}");
        _output.WriteLine($"  Expected (fixture)  : {FormatWei(senderExpBal)}");
        _output.WriteLine($"  Actual (Scrutor)    : {FormatWei(senderPostBal)}");
        _output.WriteLine($"  Delta (act-exp)     : {senderPostBal - senderExpBal}");
        _output.WriteLine("");

        // Coinbase ledger
        var coinbasePreBal  = preCoinbase?.Balance  ?? BigInteger.Zero;
        var coinbasePostBal = postCoinbase?.Balance ?? BigInteger.Zero;
        var coinbaseExpBal  = expCoinbase?.Balance  ?? BigInteger.Zero;

        var tip = gasPricePaid - baseFee;
        var expectedCoinbaseIncrease = gasUsed * tip;

        _output.WriteLine("--- COINBASE ---");
        _output.WriteLine($"  PreBalance          : {FormatWei(coinbasePreBal)}");
        _output.WriteLine($"  + expected_tip      : {FormatWei(expectedCoinbaseIncrease)}  ({gasUsed} * {tip})");
        _output.WriteLine($"  Expected (formula)  : {FormatWei(coinbasePreBal + expectedCoinbaseIncrease)}");
        _output.WriteLine($"  Expected (fixture)  : {FormatWei(coinbaseExpBal)}");
        _output.WriteLine($"  Actual (Scrutor)    : {FormatWei(coinbasePostBal)}");
        _output.WriteLine($"  Delta (act-exp)     : {coinbasePostBal - coinbaseExpBal}");

        // Also dump EELS-expected gas from fixture coinbase delta
        var eelsGasUsedFromCoinbase = (coinbaseExpBal - coinbasePreBal) / tip;
        _output.WriteLine($"  EELS GasUsed (from coinbase delta / tip): {eelsGasUsedFromCoinbase}");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------
    private EelsStateCase? LoadSingleCase(string pattern)
    {
        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true,
            MaxCases = int.MaxValue
        };

        var loader = new EelsStateFixtureLoader();
        try
        {
            var cases = loader.LoadCases(options);
            return cases.FirstOrDefault(c => c.CaseId.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static GlobalState BuildGlobalState(EelsStateCase tc)
    {
        var gs = new GlobalState();
        foreach (var (address, account) in tc.PreState)
        {
            gs.SetBalance(address, account.Balance);
            gs.SetNonce(address, account.Nonce);
            gs.SetCode(address, account.Code);
            foreach (var (slot, value) in account.Storage)
                gs.SetStorageAt(address, slot, value);
        }
        return gs;
    }

    private static string FormatWei(BigInteger wei) => $"{wei} (0x{wei:x})";

    private static ulong ParseHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return 0;
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return string.IsNullOrEmpty(s) ? 0 : Convert.ToUInt64(s, 16);
    }
}
