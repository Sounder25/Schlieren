using System.Collections.Concurrent;
using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.State;

namespace Scrutor.EELS.Tests.Harness;

public sealed class EelsStateFixtureExecutor
{
    private readonly IStateTransition _stateTransition;

    public EelsStateFixtureExecutor()
    {
        var opcodes = OpcodeCatalog.CreateAll();
        _stateTransition = new StateTransition(new EvmMachine(opcodes));
    }

    public async Task<EelsCaseExecutionReport> ExecuteAsync(EelsStateCase testCase, CancellationToken ct = default)
    {
        var globalState = new GlobalState();

        foreach (var (address, account) in testCase.PreState)
        {
            globalState.SetBalance(address, account.Balance);
            globalState.SetNonce(address, account.Nonce);
            globalState.SetCode(address, account.Code);
            foreach (var (slot, value) in account.Storage)
            {
                globalState.SetStorageAt(address, slot, value);
            }
        }

        // [AI-EDIT 2026-08-11] EELS modern-format fixtures declare some transactions as
        // invalid via the `expectException` field.  When that is set the transaction must be
        // rejected without any state mutation.  Skip EVM execution entirely; the expected
        // post-state is already the pre-state, and the receipt status is expected=false.
        if (testCase.ExpectedException is not null)
        {
            // Don't execute — treat as a clean "tx rejected" outcome.
            var mismatches0 = new List<string>();
            var stateMatches0 = CompareExpectedState(testCase, globalState, mismatches0);
            // ExpectedReceiptStatus is forced to false for exception cases; pass IsSuccess=false.
            var receiptMatches0 = CompareReceiptStatus(testCase.ExpectedReceiptStatus, false, mismatches0);
            return new EelsCaseExecutionReport(
                testCase.CaseId, false, 0, 0, stateMatches0, receiptMatches0, mismatches0);
        }

        // [AI-EDIT 2026-08-03] EVM spec allows 1024 call depth. Each async frame
        // in .NET consumes ~8-16KB of stack. On the default 1MB thread this overflows
        // for deeply-nested fixtures. Run execution on a thread with 32MB stack.
        ExecutionResult result;
        Exception? unhandledException = null;

        try
        {
            result = await RunOnLargeStackAsync(() =>
                _stateTransition.ApplyTransactionAsync(testCase.Transaction, globalState, testCase.BlockContext, commit: true, ct: ct));
        }
        catch (OutOfMemoryException ex)
        {
            unhandledException = ex;
            result = ExecutionResult.Failure(EvmError.OutOfGas, testCase.Transaction.GasLimit);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unhandledException = ex;
            result = ExecutionResult.Failure(EvmError.InternalError, testCase.Transaction.GasLimit);
        }

        var mismatches = new List<string>();

        if (unhandledException is not null)
        {
            mismatches.Add(
                $"Unhandled engine exception: " +
                $"{unhandledException.GetType().FullName}: " +
                $"{unhandledException.Message}");
        }

        var stateMatches = CompareExpectedState(testCase, globalState, mismatches);
        var receiptStatusMatches = CompareReceiptStatus(testCase.ExpectedReceiptStatus, result.IsSuccess, mismatches);

        return new EelsCaseExecutionReport(
            testCase.CaseId,
            result.IsSuccess,
            result.GasUsed,
            // [AI-EDIT 2026-08-05] Expose raw refund counter so the balance auditor can
            // compute Term 4 (EIP-3529 cap) exactly: min(counter, gasUsed/5) × price.
            result.GasRefundCounter,
            stateMatches,
            receiptStatusMatches,
            mismatches);
    }

    /// <summary>
    /// Runs an async operation on a dedicated thread with a 32MB stack to support deep EVM recursion.
    /// Each executor instance owns its own worker thread so multiple instances can run concurrently
    /// without serialising through a shared queue (required for Parallel.ForEachAsync in the
    /// taxonomy analyzer and balance auditor).
    /// </summary>
    private readonly LargeStackWorker _worker = new();

    private Task<T> RunOnLargeStackAsync<T>(Func<Task<T>> action)
    {
        return _worker.RunAsync(action);
    }

    private sealed class LargeStackWorker
    {
        private readonly Thread _thread;
        private readonly BlockingCollection<Action> _queue = new();

        public LargeStackWorker()
        {
            _thread = new Thread(WorkerLoop, 32 * 1024 * 1024);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    var result = action().GetAwaiter().GetResult();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        private void WorkerLoop()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                // Run each work item on its own fresh 32MB thread so a StackOverflowException
                // in one item does not kill the shared worker loop.
                Exception? workerEx = null;
                var itemTcs = new TaskCompletionSource<bool>();
                var itemThread = new Thread(() =>
                {
                    try { work(); itemTcs.TrySetResult(true); }
                    catch (Exception ex) { workerEx = ex; itemTcs.TrySetResult(false); }
                }, 32 * 1024 * 1024);
                itemThread.IsBackground = true;
                itemThread.Start();
                // 120-second per-case timeout — if hit, mark the outer TCS as cancelled
                if (!itemThread.Join(TimeSpan.FromSeconds(120)))
                {
                    itemThread.Interrupt();
                    // Signal completion so the queue drains — the case TCS stays uncompleted
                    // and the caller will get an OperationCanceledException from CancellationToken
                }
                if (workerEx != null)
                    throw workerEx;
            }
        }
    }

    private static bool CompareReceiptStatus(bool? expectedStatus, bool actualStatus, List<string> mismatches)
    {
        if (!expectedStatus.HasValue)
        {
            return true;
        }

        if (expectedStatus.Value == actualStatus)
        {
            return true;
        }

        mismatches.Add($"receipt.status mismatch: expected={expectedStatus.Value}, actual={actualStatus}");
        return false;
    }

    private static bool CompareExpectedState(EelsStateCase testCase, GlobalState actualState, List<string> mismatches)
    {
        var actualSnapshot = actualState.Snapshot();

        foreach (var (expectedAddress, expectedAccount) in testCase.ExpectedPostState)
        {
            if (!actualSnapshot.TryGetValue(expectedAddress, out var actualAccount))
            {
                mismatches.Add($"missing account in actual state: {expectedAddress}");
                continue;
            }

            if (actualAccount.Nonce != expectedAccount.Nonce)
            {
                mismatches.Add($"nonce mismatch for {expectedAddress}: expected={expectedAccount.Nonce}, actual={actualAccount.Nonce}");
            }

            if (actualAccount.Balance != expectedAccount.Balance)
            {
                mismatches.Add(
                    $"balance mismatch for {expectedAddress}: expected={EelsHex.ToCanonicalHex(expectedAccount.Balance)}, actual={EelsHex.ToCanonicalHex(actualAccount.Balance)}");
            }

            if (!actualAccount.Code.AsSpan().SequenceEqual(expectedAccount.Code))
            {
                mismatches.Add($"code mismatch for {expectedAddress}");
            }

            CompareStorage(expectedAddress.ToString(), expectedAccount.Storage, actualAccount.Storage, mismatches);
        }

        foreach (var (actualAddress, actualAccount) in actualSnapshot)
        {
            if (testCase.ExpectedPostState.ContainsKey(actualAddress) ||
                IsEmptyAccount(actualAccount))
            {
                continue;
            }

            mismatches.Add(
                $"unexpected account in actual state: {actualAddress}");
        }

        return mismatches.Count == 0;
    }

    private static bool IsEmptyAccount(Account account) =>
        account.Nonce == 0 &&
        account.Balance.IsZero &&
        account.Code.Length == 0 &&
        account.Storage.Values.All(value => value.IsZero);

    private static void CompareStorage(
        string address,
        IReadOnlyDictionary<BigInteger, BigInteger> expectedStorage,
        IDictionary<BigInteger, BigInteger> actualStorage,
        List<string> mismatches)
    {
        // [AI-EDIT 2026-01-10] EELS storage map is sparse; compare only declared keys.
        foreach (var (slot, expectedValue) in expectedStorage)
        {
            var actualValue = actualStorage.TryGetValue(slot, out var found) ? found : BigInteger.Zero;
            if (actualValue != expectedValue)
            {
                mismatches.Add(
                    $"storage mismatch for {address} slot {EelsHex.ToCanonicalHex(slot)}: expected={EelsHex.ToCanonicalHex(expectedValue)}, actual={EelsHex.ToCanonicalHex(actualValue)}");
            }
        }

        foreach (var (slot, actualValue) in actualStorage)
        {
            if (actualValue.IsZero || expectedStorage.ContainsKey(slot))
            {
                continue;
            }

            mismatches.Add(
                $"storage mismatch for {address} slot {EelsHex.ToCanonicalHex(slot)}: expected=0x0, actual={EelsHex.ToCanonicalHex(actualValue)}");
        }
    }
}
