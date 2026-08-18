namespace Scrutor.Core.Execution;

/// <summary>
/// Layer 2 — Structural pattern rules from hard-won EELS divergence knowledge.
/// Combines multi-field signals (not single gas constants) into protocol hypotheses.
/// Pure / deterministic — no model, no I/O.
/// </summary>
public static class StructuralPatternRules
{
    /// <summary>
    /// Evaluate all Layer 2 rules. Returns zero or more diagnoses (ordered by confidence then category).
    /// </summary>
    public static IReadOnlyList<DivergenceDiagnostics.Diagnosis> Evaluate(MismatchContext ctx)
    {
        var hits = new List<DivergenceDiagnostics.Diagnosis?>
        {
            RuleEip2200SstoreStipend(ctx),
            RuleEip3541EfPrefixCreate(ctx),
            RuleEip7610CreateCollision(ctx),
            RuleEip7702AuthWarmOrder(ctx),
            RuleEip7702NonceOverflow(ctx),
            RuleEip7825TxGasCap(ctx),
            RuleEip7883ModExpFloorOrDiv3(ctx),
            RuleEip3529RefundCap(ctx),
            RuleEip2929ColdAccess(ctx),
            RuleCoinbasePriorityFee(ctx),
            RuleEip161EmptyAccount(ctx),
            RuleExceptionalHaltGas(ctx),
            RuleEip7623CalldataFloor(ctx),
            RulePrecompileGasSchedule(ctx),
            RuleSelfDestructNewAccount(ctx),
            RuleCreateInitcodeOrDeployment(ctx),
            RuleUnexpectedAccount(ctx),
            RuleReceiptFailOog(ctx),
            RuleOsakaNewOpcodeGate(ctx),
            RuleBalanceOnlyGasResidual(ctx),
        };

        return hits
            .Where(d => d is not null)
            .Select(d => d!)
            .OrderByDescending(d => d.Confidence)
            .ThenBy(d => d.Category, StringComparer.Ordinal)
            .ToList();
    }

    // ── Rules ───────────────────────────────────────────────────────────────

    /// <summary>EIP-2200: SSTORE OOG when gas_left ≤ 2300 before storage read.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip2200SstoreStipend(MismatchContext ctx)
    {
        if (!ctx.HasStorageMismatch && !ctx.HasBalanceMismatch) return null;
        if (ctx.PrimaryBalanceDeltaGas is not long g) return null;
        if (Math.Abs(g) != 2300 && Math.Abs(g) % 2300 != 0) return null;
        if (Math.Abs(g) > 2300 * 20) return null;

        return Dx(
            "struct_eip2200_stipend",
            "EIP-2200 CALL_STIPEND (2300) gas residual — SSTORE reentrancy guard or stipend double-count",
            "EIP-2200: gas_left ≤ 2300 → OutOfGas before SSTORE netting",
            "StorageOpcodes.cs → OpcodeSStore entry guard",
            DivergenceDiagnostics.Confidence.High,
            $"|deltaGas|={Math.Abs(g)}; storage={ctx.HasStorageMismatch} balance={ctx.HasBalanceMismatch}");
    }

    /// <summary>EIP-3541: EF-prefixed runtime code → ExceptionalHalt (all execution gas).</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip3541EfPrefixCreate(MismatchContext ctx)
    {
        // Require explicit EIP-3541 / EF-prefix signal — bare "create" floods false hits.
        bool folderOk = FolderHas(ctx, "eip3541") || FolderHas(ctx, "ef_prefix") || FolderHas(ctx, "0xef");
        if (!folderOk) return null;
        if (!(ctx.HasCodeMismatch || ctx.HasMissingAccount || ctx.ContractNonceZeroWhenExpectedOne
              || ctx.ReceiptExpectedSuccessActualFail))
            return null;

        return Dx(
            "struct_eip3541_ef_prefix",
            "CREATE EF-prefix / EIP-3541 path — exceptional halt or code deploy divergence",
            "EIP-3541: runtime code starting 0xEF is invalid; top-level CREATE ExceptionalHalt consumes all gas",
            "SystemOpcodes.cs → OpcodeCreate / StateTransition CREATE path",
            DivergenceDiagnostics.Confidence.High,
            $"code={ctx.HasCodeMismatch} missing={ctx.HasMissingAccount} receiptFail={ctx.ReceiptExpectedSuccessActualFail}");
    }

    /// <summary>EIP-7610: CREATE collision on non-empty storage.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip7610CreateCollision(MismatchContext ctx)
    {
        bool folderOk = FolderHas(ctx, "7610") || FolderHas(ctx, "collision");
        if (folderOk)
        {
            if (!(ctx.HasMissingAccount || ctx.HasCodeMismatch || ctx.ContractNonceZeroWhenExpectedOne
                  || ctx.HasNonceMismatch))
                return null;
        }
        else
        {
            // Without folder: require full CREATE lifecycle triad (missing + nonce + code).
            if (!(ctx.HasMissingAccount && ctx.HasNonceMismatch && ctx.HasCodeMismatch))
                return null;
        }

        return Dx(
            "struct_eip7610_collision",
            "CREATE/CREATE2 collision — possible EIP-7610 non-empty storage reject",
            "EIP-7610: account with storage is a collision even if nonce=0 and code empty",
            "SystemOpcodes.cs → OpcodeCreate collision check",
            folderOk ? DivergenceDiagnostics.Confidence.High : DivergenceDiagnostics.Confidence.Medium,
            $"folder={ctx.EipFolder}; missing={ctx.HasMissingAccount} nonce={ctx.HasNonceMismatch} code={ctx.HasCodeMismatch}");
    }

    /// <summary>EIP-7702: warm only after successful recover_authority (order matters).</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip7702AuthWarmOrder(MismatchContext ctx)
    {
        if (!FolderHas(ctx, "7702") && !FolderHas(ctx, "set_code")) return null;
        if (!ctx.HasBalanceMismatch && !ctx.HasStorageMismatch && Math.Abs(ctx.PrimaryBalanceDeltaGas ?? 0) != 2600)
            return null;

        bool coldDelta = ctx.PrimaryBalanceDeltaGas is long d && Math.Abs(d) == 2600;
        if (!coldDelta && !ctx.HasBalanceMismatch) return null;

        return Dx(
            "struct_eip7702_auth_warm",
            "EIP-7702 authorization warm-order divergence (cold access 2600 or auth loop)",
            "EIP-7702: accessed_addresses.add(authority) only after recover; chainId/nonce-overflow/bad-sig skip warm differently",
            "StateTransition.cs → EIP-7702 auth validation loop",
            DivergenceDiagnostics.Confidence.High,
            $"folder={ctx.EipFolder}; deltaGas={ctx.PrimaryBalanceDeltaGas}");
    }

    /// <summary>EIP-7702: U64.MAX_VALUE nonce → no warm, no write.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip7702NonceOverflow(MismatchContext ctx)
    {
        if (!FolderHas(ctx, "7702") && !FolderHas(ctx, "set_code")) return null;
        if (!ctx.HasNonceMismatch && !ctx.HasBalanceMismatch) return null;

        return Dx(
            "struct_eip7702_nonce_overflow",
            "EIP-7702 nonce overflow / auth invalid path — warm or nonce write may diverge",
            "EIP-7702: auth.Nonce >= U64.MAX_VALUE → return None before warm",
            "StateTransition.cs → validate_authorization nonce check",
            DivergenceDiagnostics.Confidence.Medium,
            $"folder={ctx.EipFolder}; nonce={ctx.HasNonceMismatch}");
    }

    /// <summary>EIP-7825: Osaka transaction gas limit cap.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip7825TxGasCap(MismatchContext ctx)
    {
        if (!ctx.IsOsakaOrLater && !FolderHas(ctx, "7825") && !FolderHas(ctx, "tx_gas"))
            return null;
        // Fixture expects reject (receipt false) but we applied, or reverse
        if (!ctx.ReceiptExpectedFailActualSuccess && !ctx.SenderNoncePlusOne && !FolderHas(ctx, "7825"))
            return null;

        if (FolderHas(ctx, "7825") || ctx.ReceiptExpectedFailActualSuccess || ctx.SenderNoncePlusOne)
        {
            return Dx(
                "struct_eip7825_tx_gas_cap",
                "Possible EIP-7825 transaction gas limit cap missing — tx accepted/rejected vs Osaka rules",
                "EIP-7825: transaction gas limit cap (Osaka)",
                "StateTransition.cs → ValidateTransaction / intrinsic gas / gas limit cap",
                FolderHas(ctx, "7825") ? DivergenceDiagnostics.Confidence.Certain : DivergenceDiagnostics.Confidence.Medium,
                $"fork={ctx.ForkName}; receiptAcceptWhenShouldFail={ctx.ReceiptExpectedFailActualSuccess}; senderNonce+1={ctx.SenderNoncePlusOne}");
        }

        return null;
    }

    /// <summary>EIP-7883: ModExp floor 500 and removal of /3.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip7883ModExpFloorOrDiv3(MismatchContext ctx)
    {
        // Only true ModExp suites — not every path containing "198" as a substring elsewhere.
        if (!FolderHas(ctx, "7883") && !FolderHas(ctx, "modexp") && !FolderHas(ctx, "eip198_modexp"))
            return null;
        if (!ctx.HasBalanceMismatch || ctx.PrimaryBalanceDeltaGas is null) return null;

        long abs = Math.Abs(ctx.PrimaryBalanceDeltaGas.Value);
        // Require a gas residual that plausibly maps to ModExp formula change.
        if (abs < 100) return null;

        string detail;
        DivergenceDiagnostics.Confidence conf;
        if (abs is 500 or 200 or 300 or 1500 or 2500)
        {
            detail = $"deltaGas={abs} matches MODEXP floor/multiplier edge (200→500, /3 removal)";
            conf = DivergenceDiagnostics.Confidence.High;
        }
        else if (abs % 3 == 0 && abs >= 300)
        {
            detail = $"deltaGas={abs} divisible by 3 — possible lingering /3 divisor (EIP-7883 removes it)";
            conf = DivergenceDiagnostics.Confidence.Medium;
        }
        else
        {
            detail = $"modexp folder balance mismatch deltaGas={ctx.PrimaryBalanceDeltaGas}";
            conf = DivergenceDiagnostics.Confidence.Low;
        }

        return Dx(
            "struct_eip7883_modexp",
            "EIP-7883 ModExp gas formula divergence (floor 500 / no /3)",
            "EIP-7883: ModExp gas increase — floor 500, multiplier 16, remove /3",
            "Precompiles.cs → ModExpGas()",
            conf,
            detail);
    }

    /// <summary>EIP-3529: refund cap gasUsed/5.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip3529RefundCap(MismatchContext ctx)
    {
        if (!ctx.HasBalanceMismatch || ctx.GasUsed == 0) return null;
        if (ctx.GasRefundCounter <= 0) return null;

        var cap = (long)(ctx.GasUsed / 5);
        var effective = Math.Min(ctx.GasRefundCounter, cap);
        if (ctx.PrimaryBalanceDeltaGas is not long g) return null;
        // Residual near uncapped refund or cap difference
        var uncappedDiff = Math.Abs(ctx.GasRefundCounter - effective);
        if (Math.Abs(g) != effective && Math.Abs(g) != uncappedDiff && Math.Abs(g) != cap)
            return null;

        return Dx(
            "struct_eip3529_refund_cap",
            "EIP-3529 refund cap mismatch — effective refund = min(counter, gasUsed/5)",
            "EIP-3529: refund capped at gasUsed/5 (London+)",
            "StateTransition.cs → refund application / GasRefundCounter",
            DivergenceDiagnostics.Confidence.High,
            $"gasUsed={ctx.GasUsed} refundCounter={ctx.GasRefundCounter} cap={cap} deltaGas={g}");
    }

    /// <summary>EIP-2929 cold account/slot access.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip2929ColdAccess(MismatchContext ctx)
    {
        if (ctx.PrimaryBalanceDeltaGas is not long g) return null;
        long abs = Math.Abs(g);
        if (abs != 2600 && abs != 2100 && abs != 100) return null;

        string name = abs switch
        {
            2600 => "COLD_ACCOUNT_ACCESS",
            2100 => "COLD_SLOAD",
            _ => "WARM_ACCESS"
        };

        return Dx(
            "struct_eip2929_access",
            $"EIP-2929 access-list metering residual ({name} = {abs})",
            "EIP-2929: cold/warm account and storage access costs",
            "ExecutionContext.cs → access tracker / warm on first touch",
            DivergenceDiagnostics.Confidence.High,
            $"deltaGas={g}; folder={ctx.EipFolder}");
    }

    /// <summary>Coinbase balance: priority fee routing (EIP-1559).</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleCoinbasePriorityFee(MismatchContext ctx)
    {
        // Almost every gas bug also moves coinbase tip. Only surface when coinbase is
        // involved and we lack stronger structural signals (CREATE/storage/code/nonce).
        if (!ctx.TouchesCoinbaseBalance || !ctx.HasBalanceMismatch) return null;
        if (ctx.HasStorageMismatch || ctx.HasCodeMismatch || ctx.HasMissingAccount
            || ctx.HasNonceMismatch || ctx.HasUnexpectedAccount)
            return null;
        // Precompile/ModExp suites: root cause is precompile gas, not tip routing.
        if (DivergenceDiagnostics.IsPrecompileFixtureFolder(ctx.EipFolder)
            || DivergenceDiagnostics.IsPrecompileFixtureFolder(ctx.FixturePath))
            return null;

        return Dx(
            "struct_coinbase_priority_fee",
            "Coinbase balance divergence — priority fee / tip routing (EIP-1559) or gas residual side-effect",
            "EIP-1559: coinbase receives priority fee portion of gas payment",
            "StateTransition.cs → gas payment / coinbase credit",
            DivergenceDiagnostics.Confidence.Medium,
            $"coinbase balance mismatched; deltaGas={ctx.PrimaryBalanceDeltaGas}");
    }

    /// <summary>EIP-161 empty account deletion / touch rules.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip161EmptyAccount(MismatchContext ctx)
    {
        if (!ctx.HasUnexpectedAccount && !ctx.HasMissingAccount) return null;
        if (ctx.HasCodeMismatch || ctx.HasStorageMismatch) return null; // prefer create rules

        return Dx(
            "struct_eip161_empty_account",
            "Empty-account touch/delete divergence (EIP-161 / pre-Spurious Frontier touch)",
            "EIP-161: empty accounts deleted; pre-EIP-161 CALL may create empty accounts",
            "StateTransition.cs / SystemOpcodes.cs → account touch + empty cleanup",
            DivergenceDiagnostics.Confidence.Medium,
            $"unexpected={ctx.HasUnexpectedAccount} missing={ctx.HasMissingAccount} fork={ctx.ForkName}");
    }

    /// <summary>ExceptionalHalt should burn all execution gas.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleExceptionalHaltGas(MismatchContext ctx)
    {
        if (!ctx.ReceiptExpectedSuccessActualFail && !ctx.ReceiptExpectedFailActualSuccess)
            return null;
        if (!ctx.HasBalanceMismatch) return null;

        return Dx(
            "struct_exceptional_halt_gas",
            "Receipt status vs balance gas residual — possible ExceptionalHalt all-gas consume bug",
            "EELS ExceptionalHalt: consume remaining execution gas (not just gas used so far)",
            "EvmMachine / StateTransition → halt gas accounting",
            DivergenceDiagnostics.Confidence.Medium,
            $"receiptFailExpectSuccess={ctx.ReceiptExpectedSuccessActualFail} deltaGas={ctx.PrimaryBalanceDeltaGas}");
    }

    /// <summary>EIP-7623 calldata floor.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleEip7623CalldataFloor(MismatchContext ctx)
    {
        if (!FolderHas(ctx, "7623") && !FolderHas(ctx, "calldata")) return null;
        if (!ctx.HasBalanceMismatch && !ctx.HasReceiptMismatch) return null;

        return Dx(
            "struct_eip7623_calldata_floor",
            "EIP-7623 calldata cost floor divergence",
            "EIP-7623: increase calldata cost / floor",
            "IntrinsicGas.cs / StateTransition.cs → calldata floor",
            DivergenceDiagnostics.Confidence.High,
            $"folder={ctx.EipFolder}; deltaGas={ctx.PrimaryBalanceDeltaGas}");
    }

    /// <summary>Precompile gas schedule (BN254 EIP-1108, P256, etc.).</summary>
    private static DivergenceDiagnostics.Diagnosis? RulePrecompileGasSchedule(MismatchContext ctx)
    {
        if (!DivergenceDiagnostics.IsPrecompileFixtureFolder(ctx.EipFolder)
            && !DivergenceDiagnostics.IsPrecompileFixtureFolder(ctx.FixturePath))
            return null;
        if (!ctx.HasBalanceMismatch || ctx.PrimaryBalanceDeltaGas is null) return null;
        // Skip tiny noise
        if (Math.Abs(ctx.PrimaryBalanceDeltaGas.Value) < 50) return null;

        return Dx(
            "struct_precompile_gas",
            "Precompile gas schedule divergence in fixture folder",
            "Precompile base/per-point gas (EIP-1108/2537/7951/…)",
            "Precompiles.cs → gas schedule for active fork",
            DivergenceDiagnostics.Confidence.Medium,
            $"folder={ctx.EipFolder}; deltaGas={ctx.PrimaryBalanceDeltaGas}");
    }

    /// <summary>SELFDESTRUCT new-account gas 25000.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleSelfDestructNewAccount(MismatchContext ctx)
    {
        if (!FolderHas(ctx, "selfdestruct") && !FolderHas(ctx, "6780") && !FolderHas(ctx, "destruct"))
        {
            if (ctx.PrimaryBalanceDeltaGas is not long g || Math.Abs(g) != 25000) return null;
        }

        if (ctx.PrimaryBalanceDeltaGas is long d && Math.Abs(d) == 25000)
        {
            return Dx(
                "struct_selfdestruct_new_account",
                "SELFDESTRUCT new-account gas (25000) residual",
                "EIP-150/6780: SELFDESTRUCT gas + new account surcharge",
                "SystemOpcodes.cs → OpcodeSelfDestruct",
                DivergenceDiagnostics.Confidence.High,
                $"deltaGas={d}; folder={ctx.EipFolder}");
        }

        if (FolderHas(ctx, "selfdestruct") || FolderHas(ctx, "6780"))
        {
            return Dx(
                "struct_selfdestruct_lifecycle",
                "SELFDESTRUCT lifecycle / EIP-6780 same-tx restriction divergence",
                "EIP-6780: SELFDESTRUCT only full-delete in same transaction as create",
                "SystemOpcodes.cs → OpcodeSelfDestruct + fork gate",
                DivergenceDiagnostics.Confidence.Medium,
                $"folder={ctx.EipFolder}");
        }

        return null;
    }

    /// <summary>CREATE initcode size / deployment gas without full lifecycle cluster.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleCreateInitcodeOrDeployment(MismatchContext ctx)
    {
        // "create" alone matches tstorage_create_contexts, eip2929 create gas, etc. — too broad.
        bool folderOk = FolderHas(ctx, "3860") || FolderHas(ctx, "initcode")
            || FolderHas(ctx, "eip3541") || FolderHas(ctx, "create2_collision");
        if (!folderOk)
        {
            // Allow CREATE opcode suites only with code/missing account signal.
            if (!(FolderHas(ctx, "create") && (ctx.HasCodeMismatch || ctx.HasMissingAccount
                    || ctx.ContractNonceZeroWhenExpectedOne)))
                return null;
        }

        if (ctx.HasMissingAccount && ctx.HasCodeMismatch && ctx.HasNonceMismatch)
            return null; // covered by 7610 / create_lifecycle

        return Dx(
            "struct_create_initcode",
            "CREATE/initcode path divergence (EIP-3860 size limit or deploy gas)",
            "EIP-3860: initcode size limit; CREATE base 32000 + code deposit",
            "SystemOpcodes.cs → OpcodeCreate / IntrinsicGas",
            folderOk ? DivergenceDiagnostics.Confidence.Medium : DivergenceDiagnostics.Confidence.Low,
            $"folder={ctx.EipFolder}; code={ctx.HasCodeMismatch} nonce={ctx.HasNonceMismatch}");
    }

    /// <summary>Unexpected account in post-state.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleUnexpectedAccount(MismatchContext ctx)
    {
        if (!ctx.HasUnexpectedAccount) return null;

        return Dx(
            "struct_unexpected_account",
            "Unexpected account in actual state — spurious create or missing empty-delete",
            "Account creation / EIP-161 empty deletion",
            "StateTransition.cs → post-tx empty account cleanup",
            DivergenceDiagnostics.Confidence.Medium,
            $"fork={ctx.ForkName}; folder={ctx.EipFolder}");
    }

    /// <summary>Expected success, actual fail — often OOG mid-execution.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleReceiptFailOog(MismatchContext ctx)
    {
        if (!ctx.ReceiptExpectedSuccessActualFail) return null;

        return Dx(
            "struct_receipt_oog_or_halt",
            "Execution failed when fixture expected success — OOG, halt, or depth",
            "Receipt status = EVM success flag after gas + halt rules",
            "EvmMachine / StateTransition → gas check and ExceptionalHalt",
            DivergenceDiagnostics.Confidence.Medium,
            $"gasUsed={ctx.GasUsed}; folder={ctx.EipFolder}");
    }

    /// <summary>Osaka-only opcode/precompile gates (CLZ, P256, …).</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleOsakaNewOpcodeGate(MismatchContext ctx)
    {
        if (!ctx.IsOsakaOrLater && !FolderHas(ctx, "osaka") && !FolderHas(ctx, "7939")
            && !FolderHas(ctx, "7951") && !FolderHas(ctx, "clz") && !FolderHas(ctx, "p256"))
            return null;

        if (!FolderHas(ctx, "7939") && !FolderHas(ctx, "7951") && !FolderHas(ctx, "clz")
            && !FolderHas(ctx, "p256") && !FolderHas(ctx, "7883") && !FolderHas(ctx, "7825"))
            return null;

        if (!ctx.HasReceiptMismatch && !ctx.HasBalanceMismatch && !ctx.HasStorageMismatch)
            return null;

        return Dx(
            "struct_osaka_feature_gate",
            "Osaka feature-folder divergence — opcode/precompile/gas gate may be incomplete",
            "Osaka EIPs: 7939 CLZ, 7951 P256Verify, 7883 ModExp, 7825 gas cap",
            "ForkRules.cs OsakaRules + opcode/precompile registration",
            DivergenceDiagnostics.Confidence.Medium,
            $"folder={ctx.EipFolder}; fork={ctx.ForkName}");
    }

    /// <summary>Balance-only residual without other fields — generic gas ledger.</summary>
    private static DivergenceDiagnostics.Diagnosis? RuleBalanceOnlyGasResidual(MismatchContext ctx)
    {
        if (!ctx.HasBalanceMismatch) return null;
        if (ctx.HasStorageMismatch || ctx.HasCodeMismatch || ctx.HasMissingAccount || ctx.HasNonceMismatch
            || ctx.HasUnexpectedAccount || ctx.HasReceiptMismatch)
            return null;
        if (ctx.PrimaryBalanceDeltaGas is null) return null;
        // Prefer specific suite rules (ModExp / precompiles) over catch-all residual.
        if (DivergenceDiagnostics.IsPrecompileFixtureFolder(ctx.EipFolder)
            || DivergenceDiagnostics.IsPrecompileFixtureFolder(ctx.FixturePath)
            || FolderHas(ctx, "7883") || FolderHas(ctx, "modexp"))
            return null;
        // Avoid duplicating more specific gas-constant rules when |delta| is a known constant
        long abs = Math.Abs(ctx.PrimaryBalanceDeltaGas.Value);
        if (abs is 2100 or 2600 or 2300 or 3000 or 5000 or 25000 or 21000 or 100 or 500 or 200 or 6900)
            return null;
        if (abs < 50) return null;

        return Dx(
            "struct_balance_gas_residual",
            "Balance-only gas residual — full gas ledger (intrinsic + EVM + refund + tip)",
            "Sender balance = pre − value − gasUsed×price + refund; coinbase gets tip",
            "StateTransition.cs → gas purchase / refund / value transfer",
            DivergenceDiagnostics.Confidence.Low,
            $"deltaGas={ctx.PrimaryBalanceDeltaGas}; refundCounter={ctx.GasRefundCounter}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool FolderHas(MismatchContext ctx, string token) =>
        ctx.EipFolder.Contains(token, StringComparison.OrdinalIgnoreCase)
        || ctx.FixturePath.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static DivergenceDiagnostics.Diagnosis Dx(
        string category,
        string summary,
        string protocol,
        string boundary,
        DivergenceDiagnostics.Confidence confidence,
        string evidence) =>
        new(
            Category: category,
            Summary: summary,
            ProtocolRule: protocol,
            CodeBoundary: boundary,
            Confidence: confidence,
            Evidence: evidence);
}
