using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution.Causal;

public static class FailureEvidenceFactory
{
    private static readonly Regex HexAddr = new(
        @"0x[0-9a-fA-F]{40}", RegexOptions.Compiled);
    private static readonly Regex ExpectedActual = new(
        @"expected=(0x[0-9a-fA-F]+|\d+|True|False),\s*actual=(0x[0-9a-fA-F]+|\d+|True|False)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FailureEvidence From(
        string caseId,
        string forkName,
        string fixturePath,
        Transaction tx,
        Address sender,
        Address coinbase,
        IReadOnlyList<string> mismatches,
        ulong gasUsed,
        long refundCounter,
        bool executionSucceeded,
        EvmError error = EvmError.None,
        string? lastOpcode = null,
        int lastPc = 0,
        string? expectException = null,
        bool? expectedReceiptSuccess = null)
    {
        var rules = ForkRulesFactory.For(forkName);
        var price = EffectivePrice(tx, coinbase, rules);
        long? senderResidual = null;
        BigInteger? senderWei = null, coinbaseWei = null;
        bool missing = false, unexpected = false, storage = false, code = false, nonce = false, balance = false, receipt = false;
        bool recExpOkActFail = false, recExpFailActOk = false;
        var senderText = sender.ToString();
        var coinText = coinbase.ToString();

        foreach (var line in mismatches)
        {
            if (line.StartsWith("missing account", StringComparison.Ordinal)) missing = true;
            else if (line.StartsWith("unexpected account", StringComparison.Ordinal)) unexpected = true;
            else if (line.StartsWith("storage mismatch", StringComparison.Ordinal)) storage = true;
            else if (line.StartsWith("code mismatch", StringComparison.Ordinal)) code = true;
            else if (line.StartsWith("nonce mismatch", StringComparison.Ordinal)) nonce = true;
            else if (line.StartsWith("balance mismatch", StringComparison.Ordinal))
            {
                balance = true;
                var addr = HexAddr.Match(line);
                if (!addr.Success || !TryParseExpectedActual(line, out var exp, out var act))
                    continue;
                var deltaWei = act - exp;
                if (addr.Value.Equals(senderText, StringComparison.OrdinalIgnoreCase))
                {
                    senderWei = deltaWei;
                    if (price > 0 && deltaWei % price == 0)
                    {
                        try { senderResidual = (long)(deltaWei / price); }
                        catch { /* overflow */ }
                    }
                }
                else if (!coinbase.Equals(default(Address)) &&
                         addr.Value.Equals(coinText, StringComparison.OrdinalIgnoreCase))
                {
                    coinbaseWei = deltaWei;
                }
            }
            else if (line.StartsWith("receipt.status mismatch", StringComparison.Ordinal))
            {
                receipt = true;
                var expM = Regex.Match(line, @"expected=(True|False)", RegexOptions.IgnoreCase);
                var actM = Regex.Match(line, @"actual=(True|False)", RegexOptions.IgnoreCase);
                if (expM.Success && actM.Success)
                {
                    var expOk = expM.Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    var actOk = actM.Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    recExpOkActFail = expOk && !actOk;
                    recExpFailActOk = !expOk && actOk;
                }
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
            Mismatches = mismatches,
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

    private static bool TryParseExpectedActual(string line, out BigInteger expected, out BigInteger actual)
    {
        expected = actual = 0;
        var m = ExpectedActual.Match(line);
        if (!m.Success) return false;
        return TryQty(m.Groups[1].Value, out expected) && TryQty(m.Groups[2].Value, out actual);
    }

    private static bool TryQty(string raw, out BigInteger value)
    {
        value = 0;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = raw[2..];
            if (hex.Length % 2 == 1) hex = "0" + hex;
            if (hex.Length == 0) return true;
            try
            {
                value = new BigInteger(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
                return true;
            }
            catch { return false; }
        }
        return BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
