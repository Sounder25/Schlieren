using System.Text.Json;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;

namespace Schlieren.Harvest.Comparison;

/// <summary>Typed result of a single case comparison.</summary>
public sealed record ComparisonResult(
    CaseStatus             Status,
    IReadOnlyList<FieldDelta> Deltas,
    string?                Detail = null);

/// <summary>
/// Compares an expected (oracle) ExecutionSnapshot against Schlieren's actual
/// ExecutionSnapshot and produces a typed ComparisonResult.
///
/// Contracts:
///   - Accumulates ALL deltas in stable order — never stops at the first mismatch.
///   - Delta stable order: status → gas → refund → returnData →
///     logs (count → address → topics → data per index) →
///     accounts (by address, sorted) → nonce/balance/code/storage (by slot, sorted).
///   - Missing oracle snapshot (null) → HarnessError. Never Pass.
///   - fixtureIsValid=false → FixtureInvalid. Never Pass.
///   - Journal evidence in JournalEvidence is supporting only; it cannot satisfy
///     an absent expected value.
///   - Aborted and Quarantined are only reachable via dedicated factory methods.
/// </summary>
public static class ConformanceComparator
{
    /// <summary>
    /// Compare two snapshots directly (both expected and actual are known).
    /// Used when the oracle snapshot is already parsed from fixture post-state.
    /// </summary>
    public static ComparisonResult Compare(
        ExecutionSnapshot expected,
        ExecutionSnapshot actual)
    {
        var deltas = new List<FieldDelta>();

        // 1. Status (Validity layer)
        if (expected.IsSuccess != actual.IsSuccess)
        {
            deltas.Add(Delta(DiscrepancyLayer.Validity, DiscrepancyKind.Status,
                expected.IsSuccess, actual.IsSuccess));
        }

        // 2. Gas (Gas layer)
        if (expected.GasUsed != actual.GasUsed)
        {
            deltas.Add(Delta(DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
                expected.GasUsed, actual.GasUsed));
        }

        // 3. Refund (Gas layer)
        if (expected.GasRefundCounter.HasValue &&
            expected.GasRefundCounter != actual.GasRefundCounter)
        {
            deltas.Add(Delta(DiscrepancyLayer.Gas, DiscrepancyKind.RefundCounter,
                expected.GasRefundCounter, actual.GasRefundCounter));
        }

        // 4. Return data (ReturnData layer)
        if (!string.Equals(expected.ReturnData, actual.ReturnData, StringComparison.OrdinalIgnoreCase))
        {
            deltas.Add(Delta(DiscrepancyLayer.ReturnData, DiscrepancyKind.ReturnData,
                expected.ReturnData, actual.ReturnData));
        }

        // 5. Logs (Logs layer) — count first, then per-index fields
        if (expected.Logs.Count != actual.Logs.Count)
        {
            deltas.Add(Delta(DiscrepancyLayer.Logs, DiscrepancyKind.LogCount,
                expected.Logs.Count, actual.Logs.Count));
        }
        else
        {
            for (var i = 0; i < expected.Logs.Count; i++)
            {
                var el = expected.Logs[i];
                var al = actual.Logs[i];

                if (!string.Equals(el.Address, al.Address, StringComparison.OrdinalIgnoreCase))
                    deltas.Add(Delta(DiscrepancyLayer.Logs, DiscrepancyKind.LogAddress,
                        el.Address, al.Address));

                var expTopics = string.Join(",", el.Topics);
                var actTopics = string.Join(",", al.Topics);
                if (!string.Equals(expTopics, actTopics, StringComparison.OrdinalIgnoreCase))
                    deltas.Add(Delta(DiscrepancyLayer.Logs, DiscrepancyKind.LogTopics,
                        expTopics, actTopics));

                if (!string.Equals(el.Data, al.Data, StringComparison.OrdinalIgnoreCase))
                    deltas.Add(Delta(DiscrepancyLayer.Logs, DiscrepancyKind.LogData,
                        el.Data, al.Data));
            }
        }

        // 6. Accounts (Account + Storage layers) — sorted by address
        var expAccounts = expected.PostState
            .ToDictionary(a => a.Address, StringComparer.OrdinalIgnoreCase);
        var actAccounts = actual.PostState
            .ToDictionary(a => a.Address, StringComparer.OrdinalIgnoreCase);

        var allAddresses = expAccounts.Keys.Union(actAccounts.Keys, StringComparer.OrdinalIgnoreCase)
                                      .OrderBy(a => a, StringComparer.Ordinal)
                                      .ToList();

        foreach (var address in allAddresses)
        {
            var hasExp = expAccounts.TryGetValue(address, out var ea);
            var hasAct = actAccounts.TryGetValue(address, out var aa);

            if (!hasExp)
            {
                // Account present in actual but not in expected
                deltas.Add(Delta(DiscrepancyLayer.Account, DiscrepancyKind.AccountExistence,
                    false, true));
                continue;
            }

            if (!hasAct)
            {
                // Account expected but absent in actual
                deltas.Add(Delta(DiscrepancyLayer.Account, DiscrepancyKind.AccountExistence,
                    true, false));
                continue;
            }

            // Nonce
            if (ea!.Nonce != aa!.Nonce)
                deltas.Add(Delta(DiscrepancyLayer.Account, DiscrepancyKind.Nonce,
                    ea.Nonce, aa.Nonce));

            // Balance
            if (!HexQuantityEquals(ea.Balance, aa.Balance))
                deltas.Add(Delta(DiscrepancyLayer.Account, DiscrepancyKind.Balance,
                    ea.Balance, aa.Balance));

            // Code
            if (!string.Equals(ea.Code, aa.Code, StringComparison.OrdinalIgnoreCase))
                deltas.Add(Delta(DiscrepancyLayer.Account, DiscrepancyKind.Code,
                    ea.Code, aa.Code));

            // Storage — sorted by slot
            var expStorage = NormalizeStorageKeys(ea.Storage);
            var actStorage = NormalizeStorageKeys(aa.Storage);
            var allSlots = expStorage.Keys.Union(actStorage.Keys, StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(s => s, StringComparer.Ordinal);

            foreach (var slot in allSlots)
            {
                expStorage.TryGetValue(slot, out var expVal);
                actStorage.TryGetValue(slot, out var actVal);
                expVal ??= "0x0";
                actVal ??= "0x0";

                if (!HexQuantityEquals(expVal, actVal))
                    deltas.Add(Delta(DiscrepancyLayer.Storage, DiscrepancyKind.StorageValue,
                        expVal, actVal));
            }
        }

        var status = deltas.Count == 0 ? CaseStatus.Pass : CaseStatus.Divergence;
        return new ComparisonResult(status, deltas);
    }

    /// <summary>
    /// Compare with explicit oracle and fixture-validity flags.
    ///
    /// Missing oracle → HarnessError (the apparatus could not produce expected output).
    /// Invalid fixture → FixtureInvalid (the fixture itself is defective).
    /// Journal evidence in schlierenSnapshot.JournalEvidence is never used to fill
    /// an absent oracle expected value.
    /// </summary>
    public static ComparisonResult CompareWithOracle(
        ExecutionSnapshot? oracleSnapshot,
        ExecutionSnapshot  schlierenSnapshot,
        bool               fixtureIsValid)
    {
        if (!fixtureIsValid)
            return new ComparisonResult(CaseStatus.FixtureInvalid, Array.Empty<FieldDelta>(),
                "Fixture failed admission or runtime validation");

        if (oracleSnapshot is null)
            return new ComparisonResult(CaseStatus.HarnessError, Array.Empty<FieldDelta>(),
                "Oracle snapshot is absent — cannot produce a Pass without independent expected evidence");

        return Compare(oracleSnapshot, schlierenSnapshot);
    }

    /// <summary>
    /// Factory for Aborted result (timeout / crash / cancellation / host termination).
    /// Only this method may produce Aborted — never the comparison path.
    /// </summary>
    public static ComparisonResult Aborted(string reason) =>
        new(CaseStatus.Aborted, Array.Empty<FieldDelta>(), reason);

    /// <summary>
    /// Factory for Quarantined result. Requires an explicit signed-off evidence string.
    /// Only this method may produce Quarantined — never the comparison path.
    /// </summary>
    public static ComparisonResult Quarantined(string independentEvidence) =>
        new(CaseStatus.Quarantined, Array.Empty<FieldDelta>(), independentEvidence);

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool HexQuantityEquals(string expected, string actual)
    {
        var normalizedExpected = NormalizeHexQuantity(expected);
        var normalizedActual = NormalizeHexQuantity(actual);

        return normalizedExpected is not null && normalizedActual is not null
            ? string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal)
            : string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> NormalizeStorageKeys(
        IReadOnlyDictionary<string, string> storage)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in storage)
        {
            var normalizedKey = NormalizeHexQuantity(key) ?? key;
            if (!normalized.TryAdd(normalizedKey, value))
                throw new InvalidDataException($"Duplicate storage slot after normalization: {key}");
        }

        return normalized;
    }

    private static string? NormalizeHexQuantity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;

        if (digits.Length == 0)
            return "0";
        if (digits.Any(c => !Uri.IsHexDigit(c)))
            return null;

        var significantDigits = digits.TrimStart('0');
        return significantDigits.Length == 0
            ? "0"
            : significantDigits.ToLowerInvariant();
    }

    private static FieldDelta Delta<TExp, TAct>(
        DiscrepancyLayer layer,
        DiscrepancyKind  kind,
        TExp             expected,
        TAct             actual)
        => new(
            Layer:    layer,
            Kind:     kind,
            Expected: JsonSerializer.SerializeToElement(expected),
            Actual:   JsonSerializer.SerializeToElement(actual));
}
