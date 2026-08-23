using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;
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

        var discrepancies = new List<StateDiscrepancy>();
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
                            discrepancies.Add(new StateDiscrepancy
                            {
                                Kind = DiscrepancyKind.ReceiptStatus,
                                ExpectedBoolean = rec.Status.Value,
                                ActualBoolean = result.IsSuccess,
                                Detail = $"tx[{i}]"
                            });
                        }

                        if (rec.CumulativeGasUsed.HasValue && rec.CumulativeGasUsed.Value != result.GasUsed &&
                            block.Transactions.Count == 1)
                        {
                            discrepancies.Add(new StateDiscrepancy
                            {
                                Kind = DiscrepancyKind.ReceiptGasUsed,
                                ExpectedNumber = rec.CumulativeGasUsed.Value,
                                ActualNumber = result.GasUsed,
                                Detail = $"tx[{i}]"
                            });
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
            discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.EngineException, Detail = $"{ex.GetType().Name}: {ex.Message}" });
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

        _ = EelsStateFixtureExecutor.CompareExpectedState(dummy, state, discrepancies);
        // EELS process_system_transaction does not persist SYSTEM_ADDRESS in postState.
        var systemAddress = Address.FromHex("0xfffffffffffffffffffffffffffffffffffffffe");
        discrepancies.RemoveAll(item => item.Address == systemAddress);
        var stateMatches = !discrepancies.Any(item => item.Kind is not DiscrepancyKind.ReceiptStatus and not DiscrepancyKind.ReceiptGasUsed);
        var receiptMatches = !discrepancies.Any(item => item.Kind is DiscrepancyKind.ReceiptStatus or DiscrepancyKind.ReceiptGasUsed);
        var mismatches = discrepancies.Select(item => item.Render()).ToArray();

        return new EelsCaseExecutionReport(
            testCase.CaseId,
            lastSuccess && boom is null,
            lastGas,
            lastRefund,
            stateMatches,
            receiptMatches,
            mismatches,
            Discrepancies: discrepancies);
    }
}
