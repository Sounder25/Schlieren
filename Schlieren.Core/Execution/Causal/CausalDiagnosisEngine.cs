using System.Numerics;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Execution.Causal;

/// <summary>
/// First-divergence diagnosis against inventory rule IDs.
/// Earlier execution phases outrank downstream final-state symptoms.
/// </summary>
public static class CausalDiagnosisEngine
{
    public sealed class Report
    {
        public required ScoredDiagnosis Root { get; init; }
        public required IReadOnlyList<ScoredDiagnosis> Ranked { get; init; }
        public required string Fingerprint { get; init; }
        public required ExecutionPhase FirstPhase { get; init; }
    }

    public static Report Analyze(FailureEvidence ev)
    {
        var firstPhase = DetectFirstPhase(ev);
        var candidates = new List<ScoredDiagnosis>();
        TryCreateInitcodeWord(ev, firstPhase, candidates);
        TryCreateSurchargeFrontier(ev, firstPhase, candidates);
        TrySelfDestructRefund(ev, firstPhase, candidates);
        TryOpcodeActivation(ev, firstPhase, candidates);
        TryTxValidation(ev, firstPhase, candidates);
        TryIntrinsic(ev, firstPhase, candidates);
        TryRefundCap(ev, firstPhase, candidates);
        TryConstantResidual(ev, firstPhase, candidates);
        TryFinalStateFallback(ev, firstPhase, candidates);

        var ranked = candidates
            .OrderByDescending(d => d.Score)
            .ThenByDescending(d => d.Grade)
            .ThenBy(d => d.Phase)
            .ToList();

        if (ranked.Count == 0)
        {
            ranked.Add(Fallback(ev, firstPhase));
        }

        var root = ranked[0];
        var fp = CausalFingerprint.Build(ev.ForkName, root.Phase, root.RuleId);
        ranked[0] = CloneWithFingerprint(root, fp);
        for (var i = 1; i < ranked.Count; i++)
            ranked[i] = CloneWithFingerprint(ranked[i],
                CausalFingerprint.Build(ev.ForkName, ranked[i].Phase, ranked[i].RuleId));

        return new Report
        {
            Root = ranked[0],
            Ranked = ranked,
            Fingerprint = fp,
            FirstPhase = firstPhase
        };
    }

    public static ExecutionPhase DetectFirstPhase(FailureEvidence ev)
    {
        if (!string.IsNullOrEmpty(ev.ExpectException) && ev.ExecutionSucceeded)
            return ExecutionPhase.TransactionValidation;
        if (ev.Error is EvmError.NonceTooLow or EvmError.NonceTooHigh
            or EvmError.InsufficientFunds or EvmError.InvalidTransaction)
            return ExecutionPhase.TransactionValidation;
        if (ev.Error == EvmError.OutOfGas && ev.GasUsed == ev.TxGasLimit && ev.LastOpcode is null)
            return ExecutionPhase.IntrinsicGas;
        if (ev.Error == EvmError.InvalidOpcode)
            return ExecutionPhase.OpcodeActivation;
        if (LooksLikeFrontierCreateSurcharge(ev))
            return ExecutionPhase.IntrinsicGas;
        if (ev.FeePairGas == 24_000 && ev.Fork < Fork.London)
            return ExecutionPhase.Refund;
        if (CreateMeteringLikely(ev) || SenderOverchargeMatchesCreateWord(ev))
            return ExecutionPhase.GasCharge;
        if (ev.Error is EvmError.OutOfGas or EvmError.Revert or EvmError.StaticModeViolation
            && !string.IsNullOrEmpty(ev.LastOpcode))
            return ExecutionPhase.OpcodeResult;
        if (ev.IsCreateTx && ev.HasMissingAccount)
            return ExecutionPhase.FrameAllocation;
        if (ev.HasStorageMismatch || ev.HasCodeMismatch)
            return ExecutionPhase.StateMutation;
        if (ev.HasReceiptMismatch && (ev.HasUnexpectedAccount || ev.HasMissingAccount))
            return ExecutionPhase.CommitRevert;
        if (ev.RefundCounter != 0 && ev.HasBalanceMismatch && !ev.HasStorageMismatch)
            return ExecutionPhase.Refund;
        if (ev.HasBalanceMismatch && !ev.HasMissingAccount && !ev.HasStorageMismatch)
            return ExecutionPhase.Settlement;
        return ExecutionPhase.FinalState;
    }

    private static void TryCreateInitcodeWord(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (!ev.IsCreateTx && ev.LastOpcode is not ("CREATE" or "CREATE2"))
            return;

        var words = ev.InitcodeLength <= 0 ? 0 : (ev.InitcodeLength + 31) / 32;
        if (words == 0) return;

        var extra = 2L * words;
        var over = SenderOvercharge(ev);
        var formulaHit = over == extra;
        var preShanghai = ev.Fork < Fork.Shanghai;
        var active = ev.Rules.HasEip3860InitcodeLimit;

        var score = 0;
        if (first == ExecutionPhase.GasCharge) score += 40;
        if (formulaHit) score += 25;
        if (preShanghai) score += 20;
        if (ev.IsCreateTx || ev.LastOpcode is "CREATE" or "CREATE2") score += 15;
        if (formulaHit) score += 10;
        if (ev.HasMissingAccount || ev.ReceiptExpectedSuccessActualFail) score += 10;
        if (!preShanghai || active) score -= 50;
        if (first < ExecutionPhase.GasCharge) score -= 20;
        if (words == 0) score -= 30;

        if (score < 20) return;

        var grade = formulaHit && preShanghai && ev.HasMissingAccount
            ? DiagnosisGrade.Proven
            : formulaHit && preShanghai
                ? DiagnosisGrade.Strong
                : DiagnosisGrade.Possible;

        into.Add(new ScoredDiagnosis
        {
            RuleId = "CREATE.INITCODE_WORD",
            Title = "CREATE initcode metering active before Shanghai",
            Phase = ExecutionPhase.GasCharge,
            Grade = grade,
            Score = score,
            ProtocolRule = "EIP-3860: 2 gas per 32-byte initcode word from Shanghai only",
            Why = "EIP-3860 initcode-word charging activates at Shanghai, not " + ev.ForkName + ".",
            Proof = formulaHit
                ? $"Schlieren charged 2 gas per initcode word. {words} words × 2 = {extra} gas. Sender residual {DescribeResidual(ev)} matches."
                : $"CREATE/CREATE2 on {ev.ForkName} with {words} initcode words ({ev.InitcodeLength} B). Residual {DescribeResidual(ev)}.",
            Consequences = "DIRECT EFFECT: CREATE child received fewer gas.\n" +
                           "DOWNSTREAM: Code deposit can fail.\n" +
                           "FINAL SYMPTOM: Expected created account absent; sender balance differs.",
            LikelyFix = "Gate EIP-3860 CREATE/CREATE2 word gas on HasEip3860InitcodeLimit.",
            CodeBoundary = "Schlieren.Core/Opcodes/SystemOpcodes.cs (CREATE/CREATE2 base charge)",
            GasDelta = extra
        });
    }

    private static void TryCreateSurchargeFrontier(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (ev.Fork != Fork.Frontier) return;
        if (!LooksLikeFrontierCreateSurcharge(ev) && !ev.IsCreateTx) return;

        var over = SenderOvercharge(ev);
        var pair = ev.FeePairGas;
        var hit = over == 32_000 || pair == 32_000
                  || over == 32_000 + 2L * Math.Max(0, (ev.InitcodeLength + 31) / 32);
        if (!hit) return;

        var score = 0;
        if (first == ExecutionPhase.IntrinsicGas) score += 40;
        score += 25; // formula
        score += 20; // Frontier
        if (ev.IsCreateTx) score += 15;
        score += 10; // exact 32000
        if (pair == 32_000) score += 10; // sender/coinbase cancel
        if (first > ExecutionPhase.IntrinsicGas && first != ExecutionPhase.GasCharge) score -= 20;

        var proven = pair == 32_000 || over == 32_000;
        into.Add(new ScoredDiagnosis
        {
            RuleId = "TX.CREATE_SURCHARGE",
            Title = "Frontier CREATE tx surcharge 32000 (should be 0)",
            Phase = ExecutionPhase.IntrinsicGas,
            Grade = proven ? DiagnosisGrade.Proven : DiagnosisGrade.Strong,
            Score = score,
            ProtocolRule = "EIP-2: 32000 creation surcharge from Homestead; Frontier is 0",
            Why = "Schlieren adds TxCreate=32000 whenever to is null. That charge starts at Homestead, not Frontier.",
            Proof = pair == 32_000
                ? "Sender and coinbase residuals cancel (pure fee shift) at exactly 32000 gas. That is the CREATE surcharge moving from sender to miner."
                : $"Sender overcharged by {over} gas. Frontier must not add TxCreate=32000.",
            Consequences =
                "DIRECT EFFECT: intrinsic gas is 32000 too high; child/execution budget and miner fee both shift by 32000.\n" +
                "DOWNSTREAM: sender balance lower; coinbase balance higher by the same wei.\n" +
                "FINAL SYMPTOM: paired ±N wei on sender and coinbase — not an account-cleanup bug.",
            LikelyFix = "In IntrinsicGas.Compute, add TxCreate only when fork >= Homestead.",
            CodeBoundary = "Schlieren.Core/Execution/IntrinsicGas.cs:61-64",
            GasDelta = 32_000
        });
    }

    private static void TrySelfDestructRefund(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (ev.Fork >= Fork.London) return;
        var pair = ev.FeePairGas;
        var over = SenderOvercharge(ev);
        if (pair != 24_000 && over != 24_000) return;

        var score = first == ExecutionPhase.Refund ? 40 : 20;
        score += 25 + 20 + 10;
        into.Add(new ScoredDiagnosis
        {
            RuleId = "SELFDESTRUCT.REFUND",
            Title = "Missing pre-London SELFDESTRUCT 24000 refund",
            Phase = ExecutionPhase.Refund,
            Grade = pair == 24_000 ? DiagnosisGrade.Proven : DiagnosisGrade.Strong,
            Score = score,
            ProtocolRule = "Frontier–Berlin: +24000 once per account; London EIP-3529 removes it",
            Why = "Schlieren never credits the pre-London SELFDESTRUCT refund.",
            Proof = pair == 24_000
                ? "Sender/coinbase fee pair is exactly 24000 gas."
                : $"Sender residual implies 24000 gas. Fork {ev.ForkName} still pays this refund.",
            Consequences = "DIRECT EFFECT: refund counter short 24000.\nFINAL SYMPTOM: sender/coinbase balances.",
            LikelyFix = "Credit 24000 on first SELFDESTRUCT of an account when fork < London.",
            CodeBoundary = "Schlieren.Core/Opcodes/SystemOpcodes.cs OpcodeSelfDestruct",
            GasDelta = 24_000
        });
    }

    private static void TryOpcodeActivation(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (ev.Error != EvmError.InvalidOpcode && first != ExecutionPhase.OpcodeActivation)
            return;
        if (string.IsNullOrEmpty(ev.LastOpcode) && ev.Error != EvmError.InvalidOpcode)
            return;

        var score = first == ExecutionPhase.OpcodeActivation ? 40 : 15;
        score += 20; // fork context present
        if (!string.IsNullOrEmpty(ev.LastOpcode)) score += 15;

        into.Add(new ScoredDiagnosis
        {
            RuleId = "HALT.OPCODE_ACTIVATION",
            Title = $"Opcode activation / invalid opcode ({ev.LastOpcode ?? "unknown"})",
            Phase = ExecutionPhase.OpcodeActivation,
            Grade = ev.Error == EvmError.InvalidOpcode ? DiagnosisGrade.Strong : DiagnosisGrade.Possible,
            Score = score,
            ProtocolRule = "Inactive opcode byte must exceptionally halt the frame",
            Why = "Interpreter treated a fork-gated opcode differently than the fixture expected.",
            Proof = $"Error={ev.Error}; last opcode={ev.LastOpcode ?? "—"} PC={ev.LastPc}.",
            Consequences = "DIRECT EFFECT: frame halt.\nDOWNSTREAM: receipt / created account / balances.",
            LikelyFix = "Consult IForkRules activation flags in the opcode (see HALT.OPCODE_ACTIVATION).",
            CodeBoundary = "Schlieren.Core/Execution/EvmMachine.cs + opcode local gates"
        });
    }

    private static void TryTxValidation(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (first != ExecutionPhase.TransactionValidation) return;
        into.Add(new ScoredDiagnosis
        {
            RuleId = "TX.VALIDATION",
            Title = "Transaction rejected at validation",
            Phase = ExecutionPhase.TransactionValidation,
            Grade = DiagnosisGrade.Strong,
            Score = 70,
            ProtocolRule = ev.ExpectException ?? ev.Error.ToString(),
            Why = "Expected and actual disagreed on whether the transaction is valid.",
            Proof = $"expectException={ev.ExpectException ?? "—"}; error={ev.Error}; success={ev.ExecutionSucceeded}.",
            Consequences = "DIRECT EFFECT: no EVM body, or body ran when it should not.\nFINAL SYMPTOM: nonce/balance/receipt.",
            LikelyFix = "StateTransition validation vs fixture expectException.",
            CodeBoundary = "Schlieren.Core/Execution/StateTransition.cs (tx entry)"
        });
    }

    private static void TryIntrinsic(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (first != ExecutionPhase.IntrinsicGas) return;
        into.Add(new ScoredDiagnosis
        {
            RuleId = "TX.INTRINSIC",
            Title = "Intrinsic gas / pre-execution OOG",
            Phase = ExecutionPhase.IntrinsicGas,
            Grade = DiagnosisGrade.Strong,
            Score = 65,
            ProtocolRule = "IntrinsicGas.Compute(tx, rules)",
            Why = "Transaction died before the first opcode; intrinsic or floor likely diverged.",
            Proof = $"gasUsed={ev.GasUsed} gasLimit={ev.TxGasLimit} lastOpcode={ev.LastOpcode ?? "none"}.",
            Consequences = "DIRECT EFFECT: no execution.\nFINAL SYMPTOM: receipt fail / unchanged state.",
            LikelyFix = "IntrinsicGas.cs + EIP-7623 floor gate.",
            CodeBoundary = "Schlieren.Core/Execution/IntrinsicGas.cs"
        });
    }

    private static void TryRefundCap(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (!ev.HasBalanceMismatch || ev.HasStorageMismatch) return;
        if (ev.RefundCounter <= 0) return;
        var score = first == ExecutionPhase.Refund ? 40 : 10;
        score += 15;
        if (ev.Fork >= Fork.London) score += 20; else score -= 20;
        if (score < 25) return;
        into.Add(new ScoredDiagnosis
        {
            RuleId = "SETTLE.REFUND_CAP",
            Title = "Refund-cap / EIP-3529 settlement",
            Phase = ExecutionPhase.Refund,
            Grade = DiagnosisGrade.Possible,
            Score = score,
            ProtocolRule = "cappedRefund = min(counter, floor(used / RefundQuotient))",
            Why = "Sender residual with a nonzero refund counter and no storage mismatch.",
            Proof = $"refundCounter={ev.RefundCounter}; residual={DescribeResidual(ev)}; fork={ev.ForkName}.",
            Consequences = "DIRECT EFFECT: settlement refund.\nFINAL SYMPTOM: sender balance.",
            LikelyFix = "StateTransition refund quotient (2 pre-London, 5 London+).",
            CodeBoundary = "Schlieren.Core/Execution/StateTransition.cs (SETTLE.REFUND_CAP)"
        });
    }

    private static void TryConstantResidual(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (ev.SenderGasResidual is not long raw) return;
        var abs = Math.Abs(raw);
        if (abs == 0) return;

        // Fork-gated constants only. Do not offer Osaka-only prices on Frontier.
        var table = new (long gas, string id, string title, Fork min, Fork? max)[]
        {
            (2600, "ACCESS.COLD_ACCOUNT", "Cold account access (2600)", Fork.Berlin, null),
            (2100, "ACCESS.SLOAD", "Cold SLOAD (2100)", Fork.Berlin, null),
            (3000, "PRECOMPILE.ECRECOVER", "ecRecover 3000", Fork.Frontier, null),
            (6900, "PRECOMPILE.P256VERIFY", "P256Verify 6900", Fork.Osaka, null),
            (2300, "SSTORE.REENTRANCY_GUARD", "SSTORE stipend 2300", Fork.Istanbul, null),
            (20000, "SSTORE.FORMULA", "SSTORE set 20000", Fork.Frontier, null),
        };

        foreach (var (gas, id, title, min, max) in table)
        {
            if (abs != gas && (gas < 500 || abs % gas != 0 || abs / gas > 8))
                continue;
            var score = 10; // exact-ish delta
            if (abs == gas) score += 10;
            if (ev.Fork >= min && (max is null || ev.Fork <= max.Value)) score += 20;
            else score -= 50;
            if (first == ExecutionPhase.GasCharge) score += 15;
            else score -= 20;
            if (score < 15) continue;

            into.Add(new ScoredDiagnosis
            {
                RuleId = id,
                Title = title,
                Phase = ExecutionPhase.GasCharge,
                Grade = DiagnosisGrade.Possible,
                Score = score,
                ProtocolRule = id,
                Why = "Sender gas residual equals a protocol constant that is active on this fork.",
                Proof = $"residual={raw} gas; constant={gas}; fork={ev.ForkName} (active from {min}).",
                Consequences = "FINAL SYMPTOM: sender balance. Not proven as first divergence.",
                LikelyFix = "See docs/gas/GAS_FORMULAS.md → " + id,
                CodeBoundary = "docs/gas/GAS_RULE_INVENTORY.md",
                GasDelta = gas
            });
        }
    }

    private static void TryFinalStateFallback(FailureEvidence ev, ExecutionPhase first, List<ScoredDiagnosis> into)
    {
        if (into.Count > 0) return;
        into.Add(Fallback(ev, first));
    }

    private static ScoredDiagnosis Fallback(FailureEvidence ev, ExecutionPhase first)
    {
        var rule = ev.HasMissingAccount ? "STATE.MISSING_ACCOUNT"
            : ev.HasStorageMismatch ? "STATE.STORAGE"
            : ev.HasReceiptMismatch ? "HALT.RECEIPT"
            : "STATE.FINAL";
        return new ScoredDiagnosis
        {
            RuleId = rule,
            Title = first == ExecutionPhase.FinalState
                ? "Final-state mismatch (no isolated rule)"
                : $"{first.ToLabel()} divergence (rule not isolated)",
            Phase = first,
            Grade = DiagnosisGrade.Possible,
            Score = 10,
            ProtocolRule = "Final post-state comparison",
            Why = "Evidence does not uniquely identify an inventory rule.",
            Proof = string.Join("; ", ev.Mismatches.Take(3)),
            Consequences = "Downstream symptoms only. Run a reference structLog diff on this case.",
            LikelyFix = "Use OPEN IN WORKBENCH + eels-fixture-diff for this CaseId.",
            CodeBoundary = "(unknown)"
        };
    }

    private static bool LooksLikeFrontierCreateSurcharge(FailureEvidence ev)
        => ev.Fork == Fork.Frontier && FeeShiftMatches(ev, 32_000);

    private static bool FeeShiftMatches(FailureEvidence ev, long gasUnits)
    {
        if (ev.FeePairGas == gasUnits || SenderOvercharge(ev) == gasUnits)
            return true;
        if (ev.SenderWeiDelta is { } s && ev.CoinbaseWeiDelta is { } c && s + c == BigInteger.Zero && s != 0)
            return BigInteger.Abs(s) % gasUnits == 0;
        return false;
    }

    private static bool CreateMeteringLikely(FailureEvidence ev)
        => ev.IsCreateTx && ev.Fork < Fork.Shanghai && ev.InitcodeLength > 32
           && (ev.HasMissingAccount || ev.ReceiptExpectedSuccessActualFail);

    private static bool SenderOverchargeMatchesCreateWord(FailureEvidence ev)
    {
        if (!ev.IsCreateTx || ev.Fork >= Fork.Shanghai) return false;
        var words = ev.InitcodeLength <= 0 ? 0 : (ev.InitcodeLength + 31) / 32;
        return words > 0 && SenderOvercharge(ev) == 2L * words;
    }

    /// <summary>Gas Schlieren charged extra vs expected (positive = overcharge).</summary>
    private static long? SenderOvercharge(FailureEvidence ev)
        => ev.SenderGasResidual is long r ? -r : null;

    private static string DescribeResidual(FailureEvidence ev)
        => ev.SenderGasResidual is long r
            ? $"{r} gas (actual−expected); overcharge={-r}"
            : "not isolated";

    private static ScoredDiagnosis CloneWithFingerprint(ScoredDiagnosis d, string fp) => new()
    {
        RuleId = d.RuleId,
        Title = d.Title,
        Phase = d.Phase,
        Grade = d.Grade,
        Score = d.Score,
        Why = d.Why,
        Proof = d.Proof,
        Consequences = d.Consequences,
        LikelyFix = d.LikelyFix,
        CodeBoundary = d.CodeBoundary,
        ProtocolRule = d.ProtocolRule,
        GasDelta = d.GasDelta,
        Fingerprint = fp
    };
}
