// Diagnostics/BalanceMismatchSampler.cs
// Run with:
//   EELS_FIXTURES_ROOT=C:/projects/Scrutor/fixtures/state_tests/cancun
//   EELS_INCLUDE_SUBDIRS=1
// Purpose: dump first N balance-mismatch cases with all relevant values so we
// can identify the exact accounting delta.

using System.Numerics;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Diagnostics;

public sealed class BalanceMismatchSampler
{
    [Fact]
    public async Task Dump_First5_BalanceMismatches()
    {
        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true,
            MaxCases = int.MaxValue
        };

        var loader   = new EelsStateFixtureLoader();
        var cases    = loader.LoadCases(options);
        if (cases.Count == 0)
        {
            Assert.Fail("No cases loaded — set EELS_FIXTURES_ROOT.");
            return;
        }

        var executor = new EelsStateFixtureExecutor();
        var balanceMismatches = new List<string>();
        var dumped = 0;

        foreach (var tc in cases)
        {
            if (dumped >= 5) break;

            var report = await executor.ExecuteAsync(tc);
            var balHits = report.Mismatches
                .Where(m => m.StartsWith("balance mismatch", StringComparison.Ordinal))
                .ToList();

            if (balHits.Count == 0) continue;

            dumped++;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== CASE {tc.CaseId} ===");
            sb.AppendLine($"  Fork:         {tc.ForkName}");
            sb.AppendLine($"  TxType:       {tc.Transaction.TxType}");
            sb.AppendLine($"  GasLimit:     {tc.Transaction.GasLimit}");
            sb.AppendLine($"  GasPrice:     {tc.Transaction.GasPrice}");
            sb.AppendLine($"  MaxFeePerGas: {tc.Transaction.MaxFeePerGas}");
            sb.AppendLine($"  MaxPriority:  {tc.Transaction.MaxPriorityFeePerGas}");
            sb.AppendLine($"  Value:        {tc.Transaction.Value}");
            sb.AppendLine($"  BaseFee:      {tc.BlockContext.BaseFeePerGas}");
            sb.AppendLine($"  Coinbase:     {tc.BlockContext.Coinbase}");
            sb.AppendLine($"  From:         {tc.Transaction.From}");
            sb.AppendLine($"  To:           {tc.Transaction.To}");
            sb.AppendLine($"  Execution:    {(report.ExecutionSucceeded ? "SUCCESS" : "FAILED")}");
            sb.AppendLine($"  Pre-balances (relevant accounts):");

            // Print pre-state balances for every address in the expected post state
            foreach (var (addr, expected) in tc.ExpectedPostState)
            {
                tc.PreState.TryGetValue(addr, out var pre);
                var preBal = pre?.Balance ?? BigInteger.Zero;
                sb.AppendLine($"    {addr}  pre={preBal}  expected={expected.Balance}  delta={expected.Balance - preBal}");
            }

            sb.AppendLine($"  Mismatches:");
            foreach (var m in balHits)
                sb.AppendLine($"    {m}");

            balanceMismatches.Add(sb.ToString());
        }

        Assert.Fail(string.Join("\n", balanceMismatches));
    }
}
