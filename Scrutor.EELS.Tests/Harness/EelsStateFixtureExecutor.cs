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

        var result = await _stateTransition.ApplyTransactionAsync(testCase.Transaction, globalState, testCase.BlockContext, commit: true, ct: ct);

        var mismatches = new List<string>();
        var stateMatches = CompareExpectedState(testCase, globalState, mismatches);
        var receiptStatusMatches = CompareReceiptStatus(testCase.ExpectedReceiptStatus, result.IsSuccess, mismatches);

        return new EelsCaseExecutionReport(
            testCase.CaseId,
            result.IsSuccess,
            result.GasUsed,
            stateMatches,
            receiptStatusMatches,
            mismatches);
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
