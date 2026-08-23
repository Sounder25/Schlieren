using System.Numerics;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution.Causal;

public static class FailureEvidenceFactory
{
    public static FailureEvidence From(
        string caseId,
        string forkName,
        string fixturePath,
        Transaction tx,
        Address sender,
        Address coinbase,
        ulong gasUsed,
        long refundCounter,
        bool executionSucceeded,
        EvmError error = EvmError.None,
        string? lastOpcode = null,
        int lastPc = 0,
        string? expectException = null,
        bool? expectedReceiptSuccess = null,
        IReadOnlyList<StateDiscrepancy>? discrepancies = null)
    {
        var rules = ForkRulesFactory.For(forkName);
        var price = EffectivePrice(tx, coinbase, rules);
        long? senderResidual = null;
        BigInteger? senderWei = null, coinbaseWei = null;
        bool missing = false, unexpected = false, storage = false, code = false, nonce = false, balance = false, receipt = false;
        bool recExpOkActFail = false, recExpFailActOk = false;
        discrepancies ??= Array.Empty<StateDiscrepancy>();
        foreach (var discrepancy in discrepancies)
        {
            switch (discrepancy.Kind)
            {
                case DiscrepancyKind.MissingAccount: missing = true; break;
                case DiscrepancyKind.UnexpectedAccount: unexpected = true; break;
                case DiscrepancyKind.Storage: storage = true; break;
                case DiscrepancyKind.Code: code = true; break;
                case DiscrepancyKind.Nonce: nonce = true; break;
                case DiscrepancyKind.Balance:
                    balance = true;
                    if (discrepancy.Address is not { } address ||
                        discrepancy.ExpectedNumber is not { } expected ||
                        discrepancy.ActualNumber is not { } actual)
                        break;
                    var deltaWei = actual - expected;
                    if (address.Equals(sender))
                    {
                        senderWei = deltaWei;
                        if (price > 0 && deltaWei % price == 0)
                        {
                            try { senderResidual = (long)(deltaWei / price); }
                            catch { /* overflow */ }
                        }
                    }
                    else if (!coinbase.Equals(default(Address)) && address.Equals(coinbase))
                        coinbaseWei = deltaWei;
                    break;
                case DiscrepancyKind.ReceiptStatus:
                    receipt = true;
                    if (discrepancy.ExpectedBoolean is { } expOk && discrepancy.ActualBoolean is { } actOk)
                    {
                        recExpOkActFail = expOk && !actOk;
                        recExpFailActOk = !expOk && actOk;
                    }
                    break;
            }
        }

        var isCreate = !tx.To.HasValue;
        var initLen = isCreate ? tx.Data.Length : 0;
        var family = ExtractFamily(fixturePath);

        return new FailureEvidence
        {
            CaseId = caseId,
            ForkName = forkName,
            FixturePath = fixturePath,
            Fork = rules.Fork,
            TestFamily = family,
            ExecutionSucceeded = executionSucceeded,
            Error = error,
            GasUsed = gasUsed,
            RefundCounter = refundCounter,
            TxGasLimit = tx.GasLimit,
            IsCreateTx = isCreate,
            InitcodeLength = initLen,
            LastOpcode = lastOpcode,
            LastPc = lastPc,
            ExpectException = expectException,
            ExpectedReceiptSuccess = expectedReceiptSuccess,
            Sender = sender,
            Coinbase = coinbase,
            To = tx.To,
            EffectiveGasPrice = price,
            Discrepancies = discrepancies,
            HasMissingAccount = missing,
            HasUnexpectedAccount = unexpected,
            HasStorageMismatch = storage,
            HasCodeMismatch = code,
            HasNonceMismatch = nonce,
            HasBalanceMismatch = balance,
            HasReceiptMismatch = receipt,
            ReceiptExpectedSuccessActualFail = recExpOkActFail,
            ReceiptExpectedFailActualSuccess = recExpFailActOk,
            SenderGasResidual = senderResidual,
            SenderWeiDelta = senderWei,
            CoinbaseWeiDelta = coinbaseWei,
            FeePairGas = ComputeFeePairGas(senderWei, coinbaseWei, price),
            Rules = rules
        };
    }

    /// <summary>
    /// Sender and coinbase residuals that cancel are a pure gas-used error
    /// (fee paid vs fee received). Returns that error in gas units.
    /// </summary>
    public static long? ComputeFeePairGas(BigInteger? senderWei, BigInteger? coinbaseWei, BigInteger price)
    {
        if (senderWei is null || coinbaseWei is null) return null;
        if (senderWei.Value + coinbaseWei.Value != BigInteger.Zero) return null;
        var mag = BigInteger.Abs(senderWei.Value);
        if (price > 0 && mag % price == 0)
        {
            try { return (long)(mag / price); }
            catch { return null; }
        }
        // Price unknown or not matching: still recognize exact protocol lumps.
        if (mag % 32_000 == 0 && mag / 32_000 > 0 && mag / 32_000 < 1_000_000)
            return 32_000;
        if (mag % 24_000 == 0 && mag / 24_000 > 0 && mag / 24_000 < 1_000_000)
            return 24_000;
        return null;
    }

    public static string ExtractFamily(string? fixturePath)
    {
        if (string.IsNullOrWhiteSpace(fixturePath)) return "unknown";
        var parts = fixturePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("eip", StringComparison.OrdinalIgnoreCase))
                return part;
        }
        return parts.Length >= 2 ? parts[^2] : parts[^1];
    }

    private static BigInteger EffectivePrice(Transaction tx, Address coinbase, IForkRules rules)
    {
        if (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero)
        {
            // Evidence factory does not have the block; caller should prefer tx.GasPrice
            // which StateTransition already resolved for type-0/1. Type-2 tests usually
            // set GasPrice == MaxFeePerGas in the harness.
            var prio = tx.MaxPriorityFeePerGas;
            var max = tx.MaxFeePerGas;
            if (prio > 0 && prio < max) return prio; // conservative for coinbase; sender uses GasPrice below
        }
        return tx.GasPrice > 0 ? tx.GasPrice : BigInteger.One;
    }

}
