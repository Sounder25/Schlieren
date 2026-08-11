using System.Numerics;

namespace Scrutor.Core.Execution;

/// <summary>
/// Layer 1 — Deterministic Delta Reasoning.
/// Converts observed state deltas into protocol-level hypotheses.
/// No AI/model involved. Fast, explainable, impossible to hallucinate.
/// </summary>
public static class DivergenceDiagnostics
{
    /// <summary>Confidence level for a diagnosis.</summary>
    public enum Confidence { Certain, High, Medium, Low }

    /// <summary>A single diagnostic hypothesis derived from observed evidence.</summary>
    public sealed record Diagnosis(
        string Category,           // "gas_undercharge", "precompile_invalid_success", "missing_fork_gate", etc.
        string Summary,            // Human-readable one-liner
        string ProtocolRule,       // EIP or spec reference
        string CodeBoundary,       // Where to look in source
        Confidence Confidence,
        string Evidence);          // The arithmetic/pattern that produced this

    // ══════════════════════════════════════════════════════════════════════════
    //  Known gas constants — EVM protocol constants that appear as balance deltas
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly (long gasUnits, string name, string eip)[] KnownGasConstants =
    {
        // Access costs
        (2600, "COLD_ACCOUNT_ACCESS", "EIP-2929"),
        (2100, "COLD_SLOAD", "EIP-2929"),
        (100, "WARM_ACCESS", "EIP-2929"),

        // Precompile base costs
        (3000, "ECRECOVER", "EIP-1"),
        (6900, "P256VERIFY", "EIP-7951"),
        (150, "BN_ADD (Istanbul+)", "EIP-1108"),
        (500, "BN_ADD (Byzantium)", "EIP-196"),
        (6000, "BN_MUL (Istanbul+)", "EIP-1108"),
        (40000, "BN_MUL (Byzantium)", "EIP-196"),
        (45000, "BN_PAIRING_BASE (Istanbul+)", "EIP-1108"),
        (34000, "BN_PAIRING_PER_POINT (Istanbul+)", "EIP-1108"),
        (100000, "BN_PAIRING_BASE (Byzantium)", "EIP-197"),
        (80000, "BN_PAIRING_PER_POINT (Byzantium)", "EIP-197"),
        (200, "MODEXP_FLOOR (EIP-2565)", "EIP-2565"),
        (500, "MODEXP_FLOOR (EIP-7883)", "EIP-7883"),

        // Opcode costs
        (5, "CLZ", "EIP-7939"),
        (3, "BASIC_OPCODE (ADD/MUL/etc.)", "Yellow Paper"),
        (20000, "SSTORE_SET", "EIP-2200"),
        (5000, "SSTORE_RESET", "EIP-2200"),
        (2300, "CALL_STIPEND", "EIP-2200"),
        (25000, "SELFDESTRUCT_NEW_ACCOUNT", "EIP-150"),
        (5000, "SELFDESTRUCT_BASE (Tangerine+)", "EIP-150"),
        (32000, "CREATE_BASE", "Yellow Paper"),
        (21000, "TX_BASE", "Yellow Paper"),

        // Calldata costs
        (4, "CALLDATA_ZERO_BYTE", "Yellow Paper"),
        (16, "CALLDATA_NONZERO_BYTE (Istanbul+)", "EIP-2028"),
        (68, "CALLDATA_NONZERO_BYTE (pre-Istanbul)", "Yellow Paper"),
    };

    // ══════════════════════════════════════════════════════════════════════════
    //  Structural patterns — combinations of mismatch types that indicate
    //  specific failure classes
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Given a raw balance delta (actual - expected) in wei and the gasPrice,
    /// produce zero or more protocol-level hypotheses.
    /// </summary>
    public static List<Diagnosis> DiagnoseBalanceDelta(BigInteger deltaWei, BigInteger gasPrice)
    {
        var results = new List<Diagnosis>();
        if (gasPrice <= 0) return results;

        // Convert to gas units
        var deltaGas = (long)(deltaWei / gasPrice);
        var absDeltaGas = Math.Abs(deltaGas);

        // Check exact match against known constants
        foreach (var (gasUnits, name, eip) in KnownGasConstants)
        {
            if (absDeltaGas == gasUnits)
            {
                string direction = deltaGas > 0 ? "overcharged" : "undercharged";
                results.Add(new Diagnosis(
                    Category: "gas_constant_match",
                    Summary: $"Exactly {direction} by {name} ({gasUnits} gas) — {eip}",
                    ProtocolRule: eip,
                    CodeBoundary: GuessBoundary(name),
                    Confidence: Confidence.High,
                    Evidence: $"delta={deltaWei} wei / gasPrice={gasPrice} = {deltaGas} gas = {name}"));
                break;
            }

            // Check multiples (per-item accounting)
            if (absDeltaGas > gasUnits && absDeltaGas % gasUnits == 0)
            {
                int k = (int)(absDeltaGas / gasUnits);
                if (k <= 100) // reasonable multiplier
                {
                    string direction = deltaGas > 0 ? "overcharged" : "undercharged";
                    results.Add(new Diagnosis(
                        Category: "gas_multiple_match",
                        Summary: $"{direction} by {k}×{name} ({k}×{gasUnits}={absDeltaGas} gas) — {eip}",
                        ProtocolRule: eip,
                        CodeBoundary: GuessBoundary(name),
                        Confidence: Confidence.Medium,
                        Evidence: $"delta={deltaGas} gas = {k} × {gasUnits} ({name})"));
                }
            }
        }

        // Check for /3 pattern (EIP-7883 removes the /3 divisor)
        if (absDeltaGas > 200 && results.Count == 0)
        {
            // If actual = expected * 3, the /3 divisor is still being applied
            // The delta would be -2/3 of expected
        }

        return results;
    }

    /// <summary>
    /// Structural pattern: precompile returning success on invalid input.
    /// Detected when: storage written (actual != 0) where expected = 0,
    /// AND balance undercharged.
    /// </summary>
    public static Diagnosis? DiagnosePrecompileInvalidSuccess(
        bool hasStorageWriteWhenExpectedEmpty,
        bool hasBalanceUndercharge,
        string fixtureEipFolder)
    {
        if (!hasStorageWriteWhenExpectedEmpty || !hasBalanceUndercharge)
            return null;

        string precompile = fixtureEipFolder switch
        {
            var f when f.Contains("ecadd") => "BnAdd (0x06)",
            var f when f.Contains("ecmul") => "BnMul (0x07)",
            var f when f.Contains("pairing") => "BnPairing (0x08)",
            var f when f.Contains("p256") => "P256Verify (0x0100)",
            var f when f.Contains("modexp") => "ModExp (0x05)",
            _ => "Unknown precompile"
        };

        return new Diagnosis(
            Category: "precompile_invalid_success",
            Summary: $"{precompile} returning success on invalid input — should consume all gas and return empty",
            ProtocolRule: "EIP-196/197: invalid point → revert",
            CodeBoundary: "Precompiles.cs → catch block should return (null, gasLimit)",
            Confidence: Confidence.Certain,
            Evidence: $"Storage slot written (precompile output stored) when fixture expects empty (call should fail)");
    }

    /// <summary>
    /// Structural pattern: missing fork gate.
    /// Detected when: receipt_status says expected=False but actual=True,
    /// AND the fixture is from a newer fork folder.
    /// </summary>
    public static Diagnosis? DiagnoseMissingForkGate(
        bool receiptExpectedFail,
        bool receiptActualSuccess,
        string forkName,
        string fixturePath)
    {
        if (!receiptExpectedFail || !receiptActualSuccess) return null;

        // Transaction should have been rejected but was accepted
        return new Diagnosis(
            Category: "missing_fork_gate",
            Summary: $"Transaction accepted on {forkName} when spec says it should be rejected",
            ProtocolRule: $"Fork validation rules for {forkName}",
            CodeBoundary: "StateTransition.cs → transaction type validation or intrinsic gas check",
            Confidence: Confidence.Medium,
            Evidence: $"receipt.status: expected=False, actual=True in {fixturePath}");
    }

    /// <summary>
    /// Structural pattern: CREATE lifecycle failure.
    /// Detected when: missing_account + nonce mismatch + code mismatch appear together.
    /// </summary>
    public static Diagnosis? DiagnoseCreateLifecycleFailure(
        bool hasMissingAccount,
        bool hasNonceMismatch,
        bool hasCodeMismatch)
    {
        if (!hasMissingAccount && !hasNonceMismatch && !hasCodeMismatch) return null;
        int signals = (hasMissingAccount ? 1 : 0) + (hasNonceMismatch ? 1 : 0) + (hasCodeMismatch ? 1 : 0);
        if (signals < 2) return null;

        return new Diagnosis(
            Category: "create_lifecycle",
            Summary: "CREATE/CREATE2 lifecycle divergence — account not created or nonce/code mismatch",
            ProtocolRule: "EIP-3541/7610: CREATE validation + deployment rules",
            CodeBoundary: "StateTransition.cs → CREATE path, or SystemOpcodes.cs → OpcodeCreate",
            Confidence: Confidence.High,
            Evidence: $"Signals: missing_account={hasMissingAccount}, nonce={hasNonceMismatch}, code={hasCodeMismatch}");
    }

    /// <summary>
    /// Nonce delta reasoning.
    /// +1 nonce on sender when expected 0 = tx applied when it should have been rejected.
    /// 0 nonce on contract when expected 1 = CREATE failed.
    /// </summary>
    public static Diagnosis? DiagnoseNonceDelta(long expected, long actual, bool isSender)
    {
        long delta = actual - expected;
        if (delta == 0) return null;

        if (isSender && delta == 1)
        {
            return new Diagnosis(
                Category: "tx_applied_when_should_reject",
                Summary: "Sender nonce incremented — transaction was applied when it should have been rejected pre-execution",
                ProtocolRule: "Transaction validation (nonce, balance, type, gas)",
                CodeBoundary: "StateTransition.cs → ValidateTransaction or intrinsic gas",
                Confidence: Confidence.High,
                Evidence: $"sender nonce: expected={expected}, actual={actual}");
        }

        if (!isSender && expected == 1 && actual == 0)
        {
            return new Diagnosis(
                Category: "create_not_executed",
                Summary: "Contract nonce=0 when expected=1 — CREATE did not execute or was reverted",
                ProtocolRule: "CREATE lifecycle: nonce bump + code deployment",
                CodeBoundary: "StateTransition.cs → CREATE path or SystemOpcodes.cs → OpcodeCreate",
                Confidence: Confidence.High,
                Evidence: $"contract nonce: expected=1, actual=0");
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private static string GuessBoundary(string constantName) => constantName switch
    {
        var n when n.Contains("BN_") => "Precompiles.cs or Bn254Pairing.cs",
        var n when n.Contains("P256") => "Precompiles.cs → P256Verify()",
        var n when n.Contains("MODEXP") => "Precompiles.cs → ModExpGas()",
        var n when n.Contains("ECRECOVER") => "Precompiles.cs → EcRecover()",
        var n when n.Contains("CLZ") => "BitwiseOpcodes.cs → OpcodeClz",
        var n when n.Contains("COLD") || n.Contains("WARM") => "ExecutionContext.cs → Access tracker",
        var n when n.Contains("SSTORE") => "StorageOpcodes.cs → OpcodeSStore",
        var n when n.Contains("SELFDESTRUCT") => "SystemOpcodes.cs → OpcodeSelfDestruct",
        var n when n.Contains("CREATE") => "SystemOpcodes.cs → OpcodeCreate/Create2",
        var n when n.Contains("TX_BASE") => "IntrinsicGas.cs",
        var n when n.Contains("CALLDATA") => "IntrinsicGas.cs",
        var n when n.Contains("CALL_STIPEND") => "StorageOpcodes.cs → EIP-2200 guard",
        _ => "Unknown — run single-case trace"
    };
}
