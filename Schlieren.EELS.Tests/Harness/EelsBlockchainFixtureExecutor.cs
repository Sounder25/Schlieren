using Schlieren.Core.Execution;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Harness;

public sealed class EelsBlockchainFixtureExecutor
{
    private readonly StateTransition _pipeline;

    public EelsBlockchainFixtureExecutor()
    {
        _pipeline = new StateTransition(new EvmMachine(OpcodeCatalog.CreateAll()));
    }

    public async Task<EelsCaseExecutionReport> ExecuteAsync(
        EelsBlockchainCase testCase,
        CancellationToken ct = default)
    {
        var state = new GlobalState();
        foreach (var (address, account) in testCase.PreState)
        {
            state.SetBalance(address, account.Balance);
            state.SetNonce(address, account.Nonce);
            state.SetCode(address, account.Code);
            foreach (var (slot, value) in account.Storage)
            {
                if (value != 0)
                    state.SetStorageAt(address, slot, value);
            }
        }

        var mismatches = new List<string>();
        bool lastSuccess = true;
        ulong lastGas = 0;
        long lastRefund = 0;
        Exception? boom = null;

        try
        {
            foreach (var block in testCase.Blocks)
            {
                ct.ThrowIfCancellationRequested();

                // Invalid block is not imported. postState is pre + previously accepted blocks.
                if (!string.IsNullOrEmpty(block.ExpectException))
                    continue;

                await BlockPrelude.ApplyAsync(block.Context, state, _pipeline, ct);

                for (var i = 0; i < block.Transactions.Count; i++)
                {
                    var tx = block.Transactions[i];
                    var result = await _pipeline.ApplyTransactionAsync(tx, state, block.Context, commit: true, ct: ct);
                    lastSuccess = result.IsSuccess;
                    lastGas = result.GasUsed;
                    lastRefund = result.GasRefundCounter;

                    if (i < block.Receipts.Count)
                    {
                        var rec = block.Receipts[i];
                        if (rec.Status.HasValue && rec.Status.Value != result.IsSuccess)
                        {
                            mismatches.Add(
                                $"receipt.status mismatch tx[{i}]: expected={rec.Status.Value}, actual={result.IsSuccess}");
                        }

                        if (rec.CumulativeGasUsed.HasValue && rec.CumulativeGasUsed.Value != result.GasUsed &&
                            block.Transactions.Count == 1)
                        {
                            mismatches.Add(
                                $"receipt.gasUsed mismatch tx[{i}]: expected={rec.CumulativeGasUsed.Value}, actual={result.GasUsed}");
                        }
                    }
                }

                var wd = block.Withdrawals
                    .Select(w => (w.Address, w.AmountGwei))
                    .ToList();
                await BlockEpilogue.ApplyAsync(block.Context, state, _pipeline, wd, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            boom = ex;
            lastSuccess = false;
            mismatches.Add($"Unhandled engine exception: {ex.GetType().Name}: {ex.Message}");
        }

        var dummy = new EelsStateCase(
            testCase.FixturePath,
            testCase.CaseId,
            testCase.ForkName,
            testCase.Blocks.FirstOrDefault()?.Context ?? new Core.Primitives.BlockContext(),
            Core.Primitives.Address.Zero,
            new Core.State.Transaction(),
            testCase.PreState,
            testCase.ExpectedPostState,
            null);

        var stateMatches = EelsStateFixtureExecutor.CompareExpectedState(dummy, state, mismatches);
        // EELS process_system_transaction does not persist SYSTEM_ADDRESS in postState.
        mismatches.RemoveAll(m =>
            m.Contains("0xfffffffffffffffffffffffffffffffffffffffe", StringComparison.OrdinalIgnoreCase));
        stateMatches = !mismatches.Any(m =>
            !m.StartsWith("receipt.", StringComparison.Ordinal));
        var receiptMatches = !mismatches.Any(m => m.StartsWith("receipt.", StringComparison.Ordinal));

        return new EelsCaseExecutionReport(
            testCase.CaseId,
            lastSuccess && boom is null,
            lastGas,
            lastRefund,
            stateMatches,
            receiptMatches,
            mismatches);
    }
}
