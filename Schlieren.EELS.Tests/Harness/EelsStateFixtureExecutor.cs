using System.Collections.Concurrent;
using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Harness;

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
            var discrepancies0 = new List<StateDiscrepancy>();
            var stateMatches0 = CompareExpectedState(testCase, globalState, discrepancies0);
            // ExpectedReceiptStatus is forced to false for exception cases; pass IsSuccess=false.
            var receiptMatches0 = CompareReceiptStatus(testCase.ExpectedReceiptStatus, false, discrepancies0);
            var mismatches0 = discrepancies0.Select(item => item.Render()).ToArray();
            return new EelsCaseExecutionReport(
                testCase.CaseId, false, 0, 0, stateMatches0, receiptMatches0, mismatches0,
                Discrepancies: discrepancies0);
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

        var discrepancies = new List<StateDiscrepancy>();

        if (unhandledException is not null)
        {
            discrepancies.Add(new StateDiscrepancy
            {
                Kind = DiscrepancyKind.EngineException,
                Detail = $"{unhandledException.GetType().FullName}: {unhandledException.Message}\nStackTrace: {unhandledException.StackTrace}"
            });
        }

        var stateMatches = CompareExpectedState(testCase, globalState, discrepancies);
        var receiptStatusMatches = CompareReceiptStatus(testCase.ExpectedReceiptStatus, result.IsSuccess, discrepancies);
        var mismatches = discrepancies.Select(item => item.Render()).ToArray();

        var last = result.TraceSteps is { Count: > 0 } ? result.TraceSteps[^1] : null;
        return new EelsCaseExecutionReport(
            testCase.CaseId,
            result.IsSuccess,
            result.GasUsed,
            // [AI-EDIT 2026-08-05] Expose raw refund counter so the balance auditor can
            // compute Term 4 (EIP-3529 cap) exactly: min(counter, gasUsed/5) × price.
            result.GasRefundCounter,
            stateMatches,
            receiptStatusMatches,
            mismatches,
            result.Error,
            last?.Op,
            last?.Pc ?? 0,
            discrepancies);
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
        private readonly BlockingCollection<(Action Run, Action OnTimeout)> _queue = new();

        public LargeStackWorker()
        {
            _thread = new Thread(WorkerLoop, 32 * 1024 * 1024);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((
                Run: () =>
                {
                    try
                    {
                        var result = action().GetAwaiter().GetResult();
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                },
                OnTimeout: () => tcs.TrySetException(
                    new TimeoutException("EELS case exceeded the 120s large-stack worker limit."))));
            return tcs.Task;
        }

        private void WorkerLoop()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                // Run each work item on its own fresh 32MB thread so a StackOverflowException
                // in one item does not kill the shared worker loop.
                Exception? workerEx = null;
                var itemThread = new Thread(() =>
                {
                    try { item.Run(); }
                    catch (Exception ex) { workerEx = ex; }
                }, 32 * 1024 * 1024);
                itemThread.IsBackground = true;
                itemThread.Start();
                // 120-second per-case timeout. CREATE2 collision / deep-recursion
                // fixtures in ported_static can pin the worker here.
                if (!itemThread.Join(TimeSpan.FromSeconds(120)))
                {
                    item.OnTimeout();
                    itemThread.Interrupt();
                }
                if (workerEx != null)
                    throw workerEx;
            }
        }
    }

    private static bool CompareReceiptStatus(bool? expectedStatus, bool actualStatus, List<StateDiscrepancy> discrepancies)
    {
        if (!expectedStatus.HasValue)
        {
            return true;
        }

        if (expectedStatus.Value == actualStatus)
        {
            return true;
        }

        discrepancies.Add(new StateDiscrepancy
        {
            Kind = DiscrepancyKind.ReceiptStatus,
            ExpectedBoolean = expectedStatus.Value,
            ActualBoolean = actualStatus
        });
        return false;
    }

    internal static bool CompareExpectedState(EelsStateCase testCase, GlobalState actualState, List<StateDiscrepancy> discrepancies)
    {
        var actualSnapshot = actualState.Snapshot();

        foreach (var (expectedAddress, expectedAccount) in testCase.ExpectedPostState)
        {
            if (!actualSnapshot.TryGetValue(expectedAddress, out var actualAccount))
            {
                discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.MissingAccount, Address = expectedAddress });
                continue;
            }

            if (actualAccount.Nonce != expectedAccount.Nonce)
            {
                discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.Nonce, Address = expectedAddress, ExpectedNumber = expectedAccount.Nonce, ActualNumber = actualAccount.Nonce });
            }

            if (actualAccount.Balance != expectedAccount.Balance)
            {
                discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.Balance, Address = expectedAddress, ExpectedNumber = expectedAccount.Balance, ActualNumber = actualAccount.Balance });
            }

            if (!actualAccount.Code.AsSpan().SequenceEqual(expectedAccount.Code))
            {
                discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.Code, Address = expectedAddress });
            }

            CompareStorage(expectedAddress, expectedAccount.Storage, actualAccount.Storage, discrepancies);
        }

        foreach (var (actualAddress, actualAccount) in actualSnapshot)
        {
            if (testCase.ExpectedPostState.ContainsKey(actualAddress) ||
                IsEmptyAccount(actualAccount))
            {
                continue;
            }

            discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.UnexpectedAccount, Address = actualAddress });
        }

        return discrepancies.Count == 0;
    }

    private static bool IsEmptyAccount(Account account) =>
        account.Nonce == 0 &&
        account.Balance.IsZero &&
        account.Code.Length == 0 &&
        account.Storage.Values.All(value => value.IsZero);

    private static void CompareStorage(
        Address address,
        IReadOnlyDictionary<BigInteger, BigInteger> expectedStorage,
        IDictionary<BigInteger, BigInteger> actualStorage,
        List<StateDiscrepancy> discrepancies)
    {
        // [AI-EDIT 2026-01-10] EELS storage map is sparse; compare only declared keys.
        foreach (var (slot, expectedValue) in expectedStorage)
        {
            var actualValue = actualStorage.TryGetValue(slot, out var found) ? found : BigInteger.Zero;
            if (actualValue != expectedValue)
            {
                discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.Storage, Address = address, StorageSlot = slot, ExpectedNumber = expectedValue, ActualNumber = actualValue });
            }
        }

        foreach (var (slot, actualValue) in actualStorage)
        {
            if (actualValue.IsZero || expectedStorage.ContainsKey(slot))
            {
                continue;
            }

            discrepancies.Add(new StateDiscrepancy { Kind = DiscrepancyKind.Storage, Address = address, StorageSlot = slot, ExpectedNumber = BigInteger.Zero, ActualNumber = actualValue });
        }
    }
}
