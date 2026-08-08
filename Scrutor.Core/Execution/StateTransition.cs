using System.Numerics;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.Core.Execution;

public interface IStateTransition
{
    Task<ExecutionResult> ApplyTransactionAsync(Transaction tx, IGlobalState state, BlockContext block, bool commit = true, CancellationToken ct = default);
}

public sealed class StateTransition : IStateTransition
{
    private readonly EvmMachine _evm;

    public StateTransition(EvmMachine evm)
    {
        _evm = evm;
    }

    public async Task<ExecutionResult> ApplyTransactionAsync(Transaction tx, IGlobalState state, BlockContext block, bool commit = true, CancellationToken ct = default)
    {
        var txOverlay = new StateOverlay(state);

        // 0. Signature Recovery — must use signing hash (typed-tx unsigned digest), not tx hash.
        if (tx.Authorization == TransactionAuthorization.Signed)
        {
            try
            {
                tx.From = CryptoUtils.RecoverAddress(tx.GetRecoveryHash(), tx.V, tx.R, tx.S);
            }
            catch
            {
                throw new Exception("Internal transaction error");
            }
        }

        // [AI-EDIT 2026-08-07] Tx type gating: reject tx types not supported by this fork.
        // type-1 (EIP-2930 access lists): Berlin+
        // type-2 (EIP-1559): London+
        // type-3 (EIP-4844 blobs): Cancun+
        // type-4 (EIP-7702 set-code): Prague+
        if (tx.Authorization != TransactionAuthorization.Internal &&
            tx.Authorization != TransactionAuthorization.Simulation)
        {
            if (tx.TxType == 1 && !block.Rules.HasEip2930AccessLists)
                return ExecutionResult.Failure(EvmError.InvalidTransaction);
            if (tx.TxType == 2 && !block.Rules.HasEip1559BaseFee)
                return ExecutionResult.Failure(EvmError.InvalidTransaction);
            if (tx.TxType == 3 && !block.Rules.HasEip4844BlobTx)
                return ExecutionResult.Failure(EvmError.InvalidTransaction);
            if (tx.TxType == 4 && !block.Rules.HasEip7702SetCode)
                return ExecutionResult.Failure(EvmError.InvalidTransaction);
        }

        BigInteger maxGasCost = BigInteger.Zero;
        BigInteger blobFee = BigInteger.Zero;
        ulong intrinsicGas = 0;

        // [AI-EDIT 2026-01-10] EIP-1559 effective gas price:
        //   type-2/3: min(maxFeePerGas, baseFeePerGas + maxPriorityFeePerGas)
        //   type-0/1: gasPrice (legacy, single field)
        // The effective price is what the sender actually pays per gas unit.
        var baseFeePerGas = new BigInteger(block.BaseFeePerGas);
        BigInteger effectiveGasPrice;
        if (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero)
        {
            effectiveGasPrice = BigInteger.Min(tx.MaxFeePerGas, baseFeePerGas + tx.MaxPriorityFeePerGas);
        }
        else
        {
            effectiveGasPrice = tx.GasPrice;
        }

        if (tx.Authorization != TransactionAuthorization.Internal)
        {
            // EIP-3860 (Shanghai+): reject contract-creating transactions whose initcode exceeds
            // 2 × MAX_CODE_SIZE (49152 bytes). Pre-Shanghai: no limit.
            if (!tx.To.HasValue && block.Rules.HasEip3860InitcodeLimit && tx.Data.Length > 2 * 24576)
                return ExecutionResult.Failure(EvmError.InternalError, 0);

            // [AI-EDIT 2026-01-10] Intrinsic gas must be charged before EVM execution.
            // Yellow Paper §6.2 / EIP-2930: base 21000 + calldata bytes + access-list entries.
            intrinsicGas = IntrinsicGas.Compute(tx, block.Rules);
            
            if (tx.GasLimit < intrinsicGas)
                return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);

            // EIP-7623 (Prague+): transaction validity — gasLimit must cover the calldata token floor.
            if (block.Rules.HasEip7623CalldataFloor)
            {
                var tokenFloor = IntrinsicGas.ComputeFloor(tx);
                if (tx.GasLimit < tokenFloor)
                    return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);
            }
        }

        // Split into two gates:
        //
        //  VALIDATION gate — Signed only.
        //    Nonce ordering, EIP-1559 fee floor, blob hash well-formedness, and
        //    the upfront balance sufficiency check. Impersonated txs have no real
        //    key so we cannot enforce these; Simulation and Internal bypass for the
        //    same reason.
        //
        //  DEDUCTION gate — everything except Internal and Simulation.
        //    The sender must be charged gas + blob fee + value and have their nonce
        //    bumped for every externally-submitted tx, signed OR impersonated.
        //    Skipping this for Impersonated was the bug: the sender was never debited,
        //    so post-execution gas refund math operated on an unmodified balance,
        //    and the net effect was a free transaction.

        var isSigned = tx.Authorization == TransactionAuthorization.Signed;
        var isImpersonated = tx.Authorization == TransactionAuthorization.Impersonated;
        var isInternal = tx.Authorization == TransactionAuthorization.Internal;
        var isSimulation = tx.Authorization == TransactionAuthorization.Simulation;

        // ── Validation (Signed only) ────────────────────────────────────────────
        ulong senderNonceForDeduction = 0;
        BigInteger senderBalanceForDeduction = BigInteger.Zero;

        if (isSigned)
        {
            senderNonceForDeduction = await state.GetNonceAsync(tx.From, ct);

            // Nonce ordering
            if (tx.Nonce < senderNonceForDeduction) return ExecutionResult.Failure(EvmError.NonceTooLow);
            if (tx.Nonce > senderNonceForDeduction) return ExecutionResult.Failure(EvmError.NonceTooHigh);

            // EIP-1559 fee floor
            if (tx.TxType >= 2)
            {
                if (tx.MaxFeePerGas < baseFeePerGas) return ExecutionResult.Failure(EvmError.InsufficientFunds);
            }
            else
            {
                if (tx.GasPrice < baseFeePerGas) return ExecutionResult.Failure(EvmError.InsufficientFunds);
            }

            // EIP-4844 blob hash well-formedness
            if (tx.TxType >= 3)
            {
                if (tx.BlobVersionedHashes == null || tx.BlobVersionedHashes.Count == 0 || tx.BlobVersionedHashes.Count > 6)
                    return ExecutionResult.Failure(EvmError.InternalError);

                foreach (var hash in tx.BlobVersionedHashes)
                {
                    if (hash == null || hash.Length != 32 || hash[0] != 0x01)
                        return ExecutionResult.Failure(EvmError.InternalError);
                }

                if (tx.MaxFeePerBlobGas < block.GetBlobBaseFee())
                    return ExecutionResult.Failure(EvmError.InsufficientFunds);
            }

            // EIP-7702: type-4 (SetCode) structural validity — EELS apply_transaction
            // TransactionTypeContractCreationError: type-4 must have a target (not CREATE).
            // EmptyAuthorizationListError: type-4 must have at least one authorization.
            if (tx.TxType == 4)
            {
                if (!tx.To.HasValue)
                    return ExecutionResult.Failure(EvmError.InvalidTransaction);
                if (tx.AuthorizationList.Count == 0)
                    return ExecutionResult.Failure(EvmError.InvalidTransaction);
            }

            // Upfront balance sufficiency
            senderBalanceForDeduction = await state.GetBalanceAsync(tx.From, ct);
            maxGasCost = new BigInteger(tx.GasLimit) * (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice);
            var maxBlobCost = CalculateMaxBlobCost(tx);
            var upfrontCost = maxGasCost + maxBlobCost + tx.Value;
            if (senderBalanceForDeduction < upfrontCost)
                return ExecutionResult.Failure(EvmError.InsufficientFunds);
        }

        // ── Deduction (all externally-submitted txs: Signed + Impersonated) ────
        // Internal and Simulation never deduct — they are read-only probes or
        // recursive sub-calls that get their gas budget from the parent frame.
        if (!isInternal && !isSimulation)
        {
            // EIP-7702: type-4 structural validity for Impersonated txs (Signed path checked above).
            // EELS: TransactionTypeContractCreationError — type-4 must have a target.
            // EELS: EmptyAuthorizationListError — type-4 must have at least one authorization
            //       (counts all entries including invalid-signature ones per the wire format).
            if (isImpersonated && tx.TxType == 4)
            {
                if (!tx.To.HasValue)
                    return ExecutionResult.Failure(EvmError.InvalidTransaction);
                if (tx.AuthorizationList.Count == 0)
                    return ExecutionResult.Failure(EvmError.InvalidTransaction);
            }

            // EIP-1559 fee validations for Impersonated txs (Signed path checked above).
            // EELS: InsufficientMaxFeePerGasError, PriorityFeeGreaterThanMaxFeeError.
            if (isImpersonated && tx.TxType >= 2)
            {
                if (tx.MaxFeePerGas < baseFeePerGas)
                    return ExecutionResult.Failure(EvmError.InsufficientFunds);
                if (tx.MaxPriorityFeePerGas > tx.MaxFeePerGas)
                    return ExecutionResult.Failure(EvmError.InvalidTransaction);
            }

            // EELS: InvalidSenderError — sender must be an EOA (empty code or delegation designator).
            // Applies to ALL externally-submitted txs.
            {
                var senderCode = await state.GetCodeAsync(tx.From, ct);
                bool isDelegation = senderCode.Length == 23 &&
                                    senderCode[0] == 0xEF && senderCode[1] == 0x01 && senderCode[2] == 0x00;
                if (senderCode.Length > 0 && !isDelegation)
                    return ExecutionResult.Failure(EvmError.InvalidTransaction);
            }

            // For Impersonated txs we did not pre-fetch nonce/balance above,
            // so fetch them now (only one extra await when impersonated).
            if (isImpersonated)
            {
                senderNonceForDeduction = await state.GetNonceAsync(tx.From, ct);
                senderBalanceForDeduction = await state.GetBalanceAsync(tx.From, ct);
                maxGasCost = new BigInteger(tx.GasLimit) * (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice);
            }

            blobFee = CalculateBlobFee(tx, block);
            var actualGasCost = new BigInteger(tx.GasLimit) * effectiveGasPrice;

            if (commit)
            {
                // Deduct gas + blob fee + value upfront; refund of unspent gas happens post-execution.
                txOverlay.SetBalance(tx.From, senderBalanceForDeduction - actualGasCost - blobFee - tx.Value);
                txOverlay.SetNonce(tx.From, senderNonceForDeduction + 1);
            }
        }

        // Execute the transaction body with only the gas remaining after intrinsic deduction.
        // Internal sub-calls bypass this and receive their gas directly from the parent context.
        ulong executionGasLimit = tx.Authorization == TransactionAuthorization.Internal
            ? tx.GasLimit
            : (tx.GasLimit - intrinsicGas);

        // [AI-EDIT 2026-01-10] EIP-2929: build a fresh access tracker for this transaction.
        // tx.From and tx.To are pre-warmed at no extra cost (per spec). Access list addresses
        // and storage keys are pre-warmed and their gas cost is already included in intrinsicGas.
        var accessTracker = new AccessTracker();

        // Top-level contract creation (to == null): derive address from sender+tx.nonce and
        // pass as creationAddress so initcode runs and runtime code can be installed.
        Address? topLevelCreation = null;
        if (tx.Authorization != TransactionAuthorization.Internal && !tx.To.HasValue)
        {
            topLevelCreation = CryptoUtils.DeriveContractAddress(tx.From, tx.Nonce);
        }

        if (tx.Authorization != TransactionAuthorization.Internal)
        {
            accessTracker.WarmAddress(tx.From);
            if (tx.To.HasValue) accessTracker.WarmAddress(tx.To.Value);
            if (topLevelCreation.HasValue) accessTracker.WarmAddress(topLevelCreation.Value);
            // EIP-2929: pre-warm all active precompile addresses + EIP-3651 coinbase
            for (int i = 1; i <= block.Rules.PrecompileCount; i++)
            {
                var precompileBytes = new byte[20];
                precompileBytes[19] = (byte)i;
                accessTracker.WarmAddress(new Address(precompileBytes));
            }
            // EIP-3651: pre-warm coinbase address so COINBASE access is always warm.
            if (!block.Coinbase.Equals(Address.Zero))
                accessTracker.WarmAddress(block.Coinbase);
            foreach (var entry in tx.AccessList)
            {
                accessTracker.WarmAddress(entry.Address);
                foreach (var slot in entry.StorageKeys)
                    accessTracker.WarmSlot(entry.Address, slot);
            }
        }

        // EIP-7702 authorization processing
        ulong authRefund = 0;
        if (block.Rules.HasEip7702SetCode && tx.TxType == 4 && tx.AuthorizationList.Count > 0)
        {
            foreach (var auth in tx.AuthorizationList)
            {
                if (auth.ChainId != 0 && auth.ChainId != block.ChainId)
                    continue;

                // EELS validate_authorization: nonce >= U64.MAX_VALUE → return None (before warm).
                // When the fixture auth.nonce was ulong.MaxValue at load time, skip without warming.
                if (auth.Nonce == ulong.MaxValue)
                    continue;

                // EELS recover_authority: invalid signature (bad r/s/v) → return None (before warm).
                if (!auth.IsValid)
                    continue;

                // EELS: message.accessed_addresses.add(authority) — warm AFTER all early-exit checks.
                // This makes subsequent CALL to the signer charge WARM_ACCESS (100) not cold (2600).
                accessTracker?.WarmAddress(auth.Signer);

                var signerNonce = await txOverlay.GetNonceAsync(auth.Signer, ct);
                if (signerNonce != auth.Nonce || signerNonce == ulong.MaxValue)
                    continue;

                // EELS validate_authorization line 4506:
                // if authority has code AND it's not a valid delegation designator → skip
                var signerCode = await txOverlay.GetCodeAsync(auth.Signer, ct);
                bool isDelegation = signerCode.Length == 23 &&
                                    signerCode[0] == 0xEF && signerCode[1] == 0x01 && signerCode[2] == 0x00;
                if (signerCode.Length > 0 && !isDelegation)
                    continue;

                // If the authority already exists in state, grant the partial refund
                // EELS: refund_counter += AUTH_PER_EMPTY_ACCOUNT - REFUND_AUTH_PER_EXISTING_ACCOUNT
                //                       = 25000 - 12500 = 12500
                var signerBalance = await txOverlay.GetBalanceAsync(auth.Signer, ct);
                var accountExists = signerNonce > 0 || !signerBalance.IsZero || signerCode.Length > 0;
                if (accountExists)
                    authRefund += 12500;

                // EIP-7702: NULL_ADDRESS (0x000...000) means clear delegation → set code to empty.
                // Any other address → write delegation designator 0xEF0100 || address.
                byte[] designation;
                if (auth.DelegateAddress.Equals(Address.Zero))
                {
                    designation = Array.Empty<byte>();
                }
                else
                {
                    designation = new byte[23];
                    designation[0] = 0xEF;
                    designation[1] = 0x01;
                    designation[2] = 0x00;
                    auth.DelegateAddress.Bytes.CopyTo(designation, 3);
                }

                txOverlay.SetCode(auth.Signer, designation);
                txOverlay.SetNonce(auth.Signer, signerNonce + 1);
            }
        }

        var result = await ExecuteInternalAsync(
            tx,
            txOverlay,
            block,
            tx.From,
            topLevelCreation,
            null,
            false,
            commit,
            ct,
            0,
            executionGasLimit,
            accessTracker: accessTracker);

        // Merge EIP-7702 authorization refund into result
        if (authRefund > 0)
            result = result with { GasRefundCounter = result.GasRefundCounter + (long)authRefund };

        // Install runtime bytecode after successful top-level CREATE
        // Always charge code-deposit gas (even commit=false / estimateGas) so estimates cover it.
        if (topLevelCreation.HasValue && result.IsSuccess)
        {
            const int maxCodeSize = 24576; // EIP-170
            if (result.ReturnData.Length > maxCodeSize)
            {
                result = ExecutionResult.Failure(EvmError.OutOfGas, result.GasUsed);
            }
            else
            {
                // EIP-3541 (London+): reject code whose first byte is 0xEF.
                // InvalidContractPrefix is an ExceptionalHalt — ALL remaining gas consumed.
                if (block.Rules.HasEip3541EfPrefix && result.ReturnData.Length > 0 && result.ReturnData[0] == 0xEF)
                {
                    result = ExecutionResult.Failure(EvmError.InvalidOpcode, executionGasLimit);
                }
                else
                {
                    // Code deposit: 200 gas per byte (Yellow Paper).
                    var depositGas = 200UL * (ulong)result.ReturnData.Length;
                    var remaining = executionGasLimit > result.GasUsed
                        ? executionGasLimit - result.GasUsed
                        : 0UL;
                    if (depositGas > remaining)
                    {
                        result = ExecutionResult.Failure(EvmError.OutOfGas, executionGasLimit);
                    }
                    else
                    {
                        if (commit)
                            txOverlay.SetCode(topLevelCreation.Value, result.ReturnData);

                        result = ExecutionResult.Success(
                            result.GasUsed + depositGas,
                            result.ReturnData,
                            result.Logs,
                            result.TraceSteps) with { GasRefundCounter = result.GasRefundCounter };
                    }
                }
            }
        }

        // EELS: process_create_message calls restore_tx_state(snapshot) on failure,
        // which undoes all mutations to the creation address (nonce=1, storage, etc).
        // Our overlay architecture may have leaked writes to the creation address into
        // txOverlay during execution (e.g. via SSTORE, value transfer, or sub-calls).
        // Clean it up so txOverlay.Commit() doesn't persist the ghost account.
        if (topLevelCreation.HasValue && !result.IsSuccess)
        {
            txOverlay.Reset(topLevelCreation.Value);
        }

        // [AI-EDIT 2026-01-10] Post-execution accounting on the base state.
        // Total gas used = intrinsic + EVM execution gas.
        // 1. Refund unspent gas back to sender (always, success or failure)
        // 2. Credit effective total gas fee to coinbase/miner
        // Only when committing (not dry-run probes like estimateGas).
        if (tx.Authorization != TransactionAuthorization.Internal &&
            tx.Authorization != TransactionAuthorization.Simulation &&
            commit)
        {
            // [AI-EDIT 2026-01-10] Cap EVM-reported gas at executionGasLimit.
            // On OOG, ConsumeGas overshoots (e.g. adds 20000 gas then throws), leaving
            // context.GasUsed > executionGasLimit. Per spec, a frame that OOGs consumes
            // ALL its allocated gas — never more. Capping here ensures accounting is correct.
            var evmGasUsed = result.GasUsed > executionGasLimit ? executionGasLimit : result.GasUsed;
            var totalGasUsed = intrinsicGas + evmGasUsed;

            // EIP-3529 (London+): apply capped gas refund. Pre-London: max refund = gasUsed/2.
            if (result.GasRefundCounter > 0)
            {
                var maxRefund = (long)(totalGasUsed / block.Rules.RefundQuotient);
                var cappedRefund = Math.Min(result.GasRefundCounter, maxRefund);
                totalGasUsed -= (ulong)cappedRefund;
            }

            // EIP-7623 (Prague+): enforce calldata token floor.
            // If actual gas consumed is below floor = TX_BASE + tokens×10, charge the floor instead.
            // Floor applies after EIP-3529 refund to prevent double-benefit.
            if (block.Rules.HasEip7623CalldataFloor && tx.Authorization != TransactionAuthorization.Internal)
            {
                var tokenFloor = IntrinsicGas.ComputeFloor(tx);
                if (totalGasUsed < tokenFloor)
                    totalGasUsed = tokenFloor;
            }

            // Sender balance recovery after execution:
            // 1. Refund for UNUSED gas at the effective gas price.
            // 2. For EIP-1559 (type-2/3): also refund the "price-cap" difference.
            //    The sender paid gasLimit × maxFeePerGas upfront, but should only pay
            //    gasLimit × effectiveGasPrice. The excess (per-gas × total-limit) is returned.
            //    Combined: refund = gasRefund × effectiveGasPrice + gasLimit × (maxFee - effectiveGasPrice)
            // 3. If execution failed (REVERT / OOG / exceptional halt), restore tx.Value to sender.
            //    The recipient credit is inside the execution overlay and was never committed,
            //    so the value was never transferred; the sender should not lose it.
            //    Gas and blob fees are non-refundable regardless of execution outcome.
            {
                var gasRefund = tx.GasLimit > totalGasUsed ? tx.GasLimit - totalGasUsed : 0UL;
                var currentBalance = await txOverlay.GetBalanceAsync(tx.From, ct);
                var gasRefundAmount = new BigInteger(gasRefund) * effectiveGasPrice;
                
                // Value restoration on failed execution: tx.Value was debited upfront but the
                // recipient credit in the overlay was never committed, so return it to sender.
                var valueRestoration = result.IsSuccess ? BigInteger.Zero : tx.Value;
                txOverlay.SetBalance(tx.From, currentBalance + gasRefundAmount + valueRestoration);
            }

            // [AI-EDIT 2026-01-10] EIP-1559 coinbase credit = (effectiveGasPrice - baseFee) × gasUsed.
            // The base fee portion is burned (no credit). Priority fee portion goes to miner.
            // For pre-London blocks baseFeePerGas = 0 so this degrades to full effectiveGasPrice.
            if (!block.Coinbase.Equals(Address.Zero))
            {
                var effectivePriorityFee = effectiveGasPrice > baseFeePerGas
                    ? effectiveGasPrice - baseFeePerGas
                    : BigInteger.Zero;
                var minerFee = new BigInteger(totalGasUsed) * effectivePriorityFee;
                if (minerFee > 0)
                {
                    var coinbaseBalance = await txOverlay.GetBalanceAsync(block.Coinbase, ct);
                    txOverlay.SetBalance(block.Coinbase, coinbaseBalance + minerFee);
                }
            }

            // Return a result that reflects the true total gas used to callers (e.g. eth_getReceipt).
            result = result with { GasUsed = totalGasUsed };
        }

        if (commit)
        {
            txOverlay.Commit();
            // Only the top-level transaction should finalize account deletions.
            // Internal sub-calls propagate deletion marks via Commit() → parent.MarkForDeletion()
            // and must NOT call DeleteAccount here — doing so would immediately remove the account
            // from GlobalState while the parent CREATE opcode still needs to reference or push it.
            if (tx.Authorization != TransactionAuthorization.Internal)
            {
                foreach (var addr in txOverlay.GetAccountsMarkedForDeletion())
                    state.DeleteAccount(addr);
            }
        }

        return result;
    }

    /// <summary>
    /// Same as <see cref="ApplyTransactionAsync"/> but also returns a populated
    /// <see cref="GasFrameNode"/> tree so callers can build a gas causality tree.
    /// </summary>
    public async Task<(ExecutionResult result, GasFrameNode rootFrame, ulong intrinsicGas, ulong calldataGas)>
        ApplyTransactionWithGasTreeAsync(
            Transaction tx,
            IGlobalState state,
            BlockContext block,
            bool commit = true,
            CancellationToken ct = default)
    {
        // Compute calldata gas separately so the tree can split it from the base 21000.
        ulong calldataGas = 0;
        foreach (var b in tx.Data)
            calldataGas += b == 0 ? 4UL : 16UL;

        var intrinsicGas = IntrinsicGas.Compute(tx);

        // Root frame — the top-level call.
        var rootFrame = new GasFrameNode
        {
            Label = tx.To.HasValue
                ? $"Contract {tx.To.Value} execution"
                : $"CREATE {CryptoUtils.DeriveContractAddress(tx.From, tx.Nonce)}"
        };

        // Re-execute ApplyTransactionAsync with the root frame injected so every
        // sub-call appends its own child node to rootFrame.Children.
        // We do this by running a modified path: replicate the top-level setup and
        // pass rootFrame as the parentGasFrame to ExecuteInternalAsync.
        // Simplest correct approach: just run ApplyTransactionAsync normally to get
        // the committed result, but wire the gas frame into our own internal call.

        var result = await ApplyTransactionWithFrameAsync(tx, state, block, commit, ct, rootFrame);
        return (result, rootFrame, intrinsicGas, calldataGas);
    }

    private async Task<ExecutionResult> ApplyTransactionWithFrameAsync(
        Transaction tx,
        IGlobalState state,
        BlockContext block,
        bool commit,
        CancellationToken ct,
        GasFrameNode rootFrame)
    {
        // Mirror the setup from ApplyTransactionAsync, passing rootFrame to ExecuteInternalAsync.
        if (tx.Authorization == TransactionAuthorization.Signed)
        {
            try { tx.From = CryptoUtils.RecoverAddress(tx.GetRecoveryHash(), tx.V, tx.R, tx.S); }
            catch { throw new Exception("Internal transaction error"); }
        }

        BigInteger maxGasCost = BigInteger.Zero;
        BigInteger blobFee = BigInteger.Zero;
        ulong intrinsicGas = 0;
        var baseFeePerGas = new BigInteger(block.BaseFeePerGas);
        BigInteger effectiveGasPrice;
        if (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero)
            effectiveGasPrice = BigInteger.Min(tx.MaxFeePerGas, baseFeePerGas + tx.MaxPriorityFeePerGas);
        else
            effectiveGasPrice = tx.GasPrice;

        if (tx.Authorization != TransactionAuthorization.Internal)
        {
            intrinsicGas = IntrinsicGas.Compute(tx, block.Rules);
            if (tx.GasLimit < intrinsicGas) return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);
            if (block.Rules.HasEip7623CalldataFloor)
            {
                var tokenFloor = IntrinsicGas.ComputeFloor(tx);
                if (tx.GasLimit < tokenFloor) return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);
            }
        }

        if (tx.Authorization != TransactionAuthorization.Internal &&
            tx.Authorization != TransactionAuthorization.Simulation)
        {
            var senderNonce = await state.GetNonceAsync(tx.From, ct);
            if (tx.Nonce < senderNonce) return ExecutionResult.Failure(EvmError.NonceTooLow);
            if (tx.Nonce > senderNonce) return ExecutionResult.Failure(EvmError.NonceTooHigh);
            var senderBalance = await state.GetBalanceAsync(tx.From, ct);
            var priceForUpfront = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice;
            maxGasCost = new BigInteger(tx.GasLimit) * priceForUpfront;
            blobFee = CalculateBlobFee(tx, block);
            var maxBlobCost = CalculateMaxBlobCost(tx);
            if (senderBalance < maxGasCost + maxBlobCost + tx.Value) return ExecutionResult.Failure(EvmError.InsufficientFunds);
            if (commit) { state.SetBalance(tx.From, senderBalance - maxGasCost - blobFee - tx.Value); state.SetNonce(tx.From, senderNonce + 1); }
        }

        ulong executionGasLimit = tx.Authorization == TransactionAuthorization.Internal
            ? tx.GasLimit : (tx.GasLimit - intrinsicGas);

        var accessTracker = new AccessTracker();
        Address? topLevelCreation = null;
        if (tx.Authorization != TransactionAuthorization.Internal && !tx.To.HasValue)
            topLevelCreation = CryptoUtils.DeriveContractAddress(tx.From, tx.Nonce);

        if (tx.Authorization != TransactionAuthorization.Internal)
        {
            accessTracker.WarmAddress(tx.From);
            if (tx.To.HasValue) accessTracker.WarmAddress(tx.To.Value);
            if (topLevelCreation.HasValue) accessTracker.WarmAddress(topLevelCreation.Value);
            for (int i = 1; i <= 9; i++) { var b = new byte[20]; b[19] = (byte)i; accessTracker.WarmAddress(new Address(b)); }
            if (!block.Coinbase.Equals(Address.Zero)) accessTracker.WarmAddress(block.Coinbase);
            foreach (var entry in tx.AccessList) { accessTracker.WarmAddress(entry.Address); foreach (var slot in entry.StorageKeys) accessTracker.WarmSlot(entry.Address, slot); }
        }

        var result = await ExecuteInternalAsync(
            tx, state, block, tx.From, topLevelCreation, null, false, commit, ct, 0,
            executionGasLimit, accessTracker: accessTracker, parentGasFrame: rootFrame);

        if (topLevelCreation.HasValue && result.IsSuccess)
        {
            const int maxCodeSize = 24576;
            if (result.ReturnData.Length > maxCodeSize)
                result = ExecutionResult.Failure(EvmError.OutOfGas, result.GasUsed);
            else
            {
                var depositGas = 200UL * (ulong)result.ReturnData.Length;
                var remaining = executionGasLimit > result.GasUsed ? executionGasLimit - result.GasUsed : 0UL;
                if (depositGas > remaining)
                    result = ExecutionResult.Failure(EvmError.OutOfGas, executionGasLimit);
                else
                {
                    if (commit) state.SetCode(topLevelCreation.Value, result.ReturnData);
                    result = ExecutionResult.Success(result.GasUsed + depositGas, result.ReturnData, result.Logs, result.TraceSteps) with { GasRefundCounter = result.GasRefundCounter };
                }
            }
        }

        if (tx.Authorization != TransactionAuthorization.Internal &&
            tx.Authorization != TransactionAuthorization.Simulation &&
            commit)
        {
            var evmGasUsed = result.GasUsed > executionGasLimit ? executionGasLimit : result.GasUsed;
            var totalGasUsed = intrinsicGas + evmGasUsed;
            if (result.GasRefundCounter > 0)
            {
                var maxRefund = (long)(totalGasUsed / 5);
                totalGasUsed -= (ulong)Math.Min(result.GasRefundCounter, maxRefund);
            }
            // EIP-7623 (Prague+): enforce calldata token floor.
            if (block.Rules.HasEip7623CalldataFloor && tx.Authorization != TransactionAuthorization.Internal)
            {
                var tokenFloor = IntrinsicGas.ComputeFloor(tx);
                if (totalGasUsed < tokenFloor)
                    totalGasUsed = tokenFloor;
            }
            var gasRefund = tx.GasLimit > totalGasUsed ? tx.GasLimit - totalGasUsed : 0UL;
            var currentBalance = await state.GetBalanceAsync(tx.From, ct);
            var gasRefundAmount = new BigInteger(gasRefund) * effectiveGasPrice;
            BigInteger priceDiffRefund = BigInteger.Zero;
            if (tx.TxType >= 2 && tx.MaxFeePerGas > effectiveGasPrice)
                priceDiffRefund = new BigInteger(tx.GasLimit) * (tx.MaxFeePerGas - effectiveGasPrice);
            var valueRestoration = result.IsSuccess ? BigInteger.Zero : tx.Value;
            state.SetBalance(tx.From, currentBalance + gasRefundAmount + priceDiffRefund + valueRestoration);
            if (!block.Coinbase.Equals(Address.Zero))
            {
                var priorityFee = effectiveGasPrice > baseFeePerGas ? effectiveGasPrice - baseFeePerGas : BigInteger.Zero;
                var minerFee = new BigInteger(totalGasUsed) * priorityFee;
                if (minerFee > 0)
                {
                    var cb = await state.GetBalanceAsync(block.Coinbase, ct);
                    state.SetBalance(block.Coinbase, cb + minerFee);
                }
            }
            result = result with { GasUsed = totalGasUsed };
        }

        return result;
    }

    private static BigInteger CalculateBlobFee(Transaction tx, BlockContext block)
    {
        if (tx.TxType != 3 || tx.BlobVersionedHashes.Count == 0)
            return BigInteger.Zero;

        const ulong gasPerBlob = 1UL << 17;
        var blobGas = new BigInteger(gasPerBlob) * tx.BlobVersionedHashes.Count;
        return blobGas * FakeExponential(
            factor: BigInteger.One,
            numerator: new BigInteger(block.ExcessBlobGas),
            denominator: new BigInteger(3_338_477));
    }

    private static BigInteger CalculateMaxBlobCost(Transaction tx)
    {
        if (tx.TxType != 3 || tx.BlobVersionedHashes.Count == 0)
            return BigInteger.Zero;

        const ulong gasPerBlob = 1UL << 17;
        return new BigInteger(gasPerBlob) *
            tx.BlobVersionedHashes.Count *
            tx.MaxFeePerBlobGas;
    }

    private static BigInteger FakeExponential(
        BigInteger factor,
        BigInteger numerator,
        BigInteger denominator)
    {
        var i = BigInteger.One;
        var output = BigInteger.Zero;
        var numeratorAccumulator = factor * denominator;
        while (numeratorAccumulator > BigInteger.Zero)
        {
            output += numeratorAccumulator;
            numeratorAccumulator =
                numeratorAccumulator * numerator / (denominator * i);
            i++;
        }

        return output / denominator;
    }

    private async Task<ExecutionResult> ExecuteInternalAsync(
        Transaction tx,
        IGlobalState state,
        BlockContext block,
        Address origin,
        Address? creationAddress,
        Address? codeAddress,
        bool isStatic,
        bool commit,
        CancellationToken ct,
        int depth = 0,
        ulong? executionGasLimit = null,
        ITransientStorageFrame? transientStorage = null,
        AccessTracker? accessTracker = null,
        Dictionary<(Address, BigInteger), BigInteger>? originalStorageSnapshot = null,
        GasFrameNode? parentGasFrame = null)
    {
        if (depth > 1024)
        {
            // [DIAGNOSTIC] Log call stack depth limit
            Console.WriteLine($"[DEPTH_LIMIT_EXCEEDED] depth={depth} tx={tx} from={tx.From} to={tx.To}");
            return ExecutionResult.Failure(EvmError.InternalError, 0, null); // Call stack depth limit reached
        }

        // Use a state overlay to ensure snapshot isolation for this execution frame
        var overlay = new StateOverlay(state);
        transientStorage ??= new TransientStorageRoot();
        var transientFrame = new TransientStorageOverlay(transientStorage);
        // [AI-EDIT 2026-01-10] EIP-2929: reuse the top-level access tracker for the whole tx tree.
        accessTracker ??= new AccessTracker();
        var accessCheckpoint = accessTracker.CreateCheckpoint();
        // [AI-EDIT 2026-07-24] EIP-2200: reuse the top-level original storage snapshot for the whole tx tree.
        originalStorageSnapshot ??= new Dictionary<(Address, BigInteger), BigInteger>();

        // [AI-EDIT 2026-07-24] Value transfer: debit caller (if internal call) and credit recipient upfront.
        // Top-level sender debit was already applied in ApplyTransactionAsync.
        // DELEGATECALL/CALLCODE (codeAddress != null): NO value transfer — Value field is only
        // for CALLVALUE opcode return, not actual balance movement. EELS: should_transfer_value=False.
        var recipient = creationAddress ?? tx.To;
        if (tx.Value > 0 && recipient.HasValue && codeAddress == null)
        {
            if (tx.Authorization == TransactionAuthorization.Internal)
            {
                var senderBalance = await overlay.GetBalanceAsync(tx.From, ct);
                if (senderBalance < tx.Value)
                    return ExecutionResult.Failure(EvmError.InsufficientFunds);
                
                overlay.SetBalance(tx.From, senderBalance - tx.Value);
            }
            
            var recipientBalance = await overlay.GetBalanceAsync(recipient.Value, ct);
            overlay.SetBalance(recipient.Value, recipientBalance + tx.Value);
        }

        // [AI-EDIT 2026-01-10] Precompile dispatch: addresses 0x01–0x09 are handled here,
        // not by the EVM bytecode interpreter. Only CALL-type frames qualify (not CREATE).
        if (!creationAddress.HasValue && !codeAddress.HasValue && tx.To.HasValue && Precompiles.IsPrecompile(tx.To.Value, block.Rules.PrecompileCount))
        {
            var (preOutput, preGas) = Precompiles.Execute(tx.To.Value, tx.Data, executionGasLimit ?? 0UL, block.Rules);
            if (preOutput == null)
            {
                // OOG in precompile — all gas consumed, no state change
                return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);
            }

            // EELS touch_account: Frontier/Homestead precompile calls still create the account.
            if (!block.Rules.HasEip161EmptyAccountDeletion)
                await overlay.TouchAccountAsync(tx.To.Value, ct);

            // Precompile succeeded — state commit handled here
            if (commit)
            {
                overlay.Commit();
            }

            return ExecutionResult.Success(preGas, preOutput);
        }

        // Determine Code and Contract Address
        byte[] code;
        Address contractAddress;

        if (creationAddress.HasValue)
        {
            // CREATE: Use data as init code, address is the new address
            code = tx.Data;
            contractAddress = creationAddress.Value;

            // EIP-161 (Spurious Dragon+): a newly created contract starts at nonce 1.
            // Pre-Spurious Dragon (Frontier/Homestead/Tangerine): new contracts start at nonce 0.
            if (block.Rules.HasEip161ContractNonce)
                overlay.SetNonce(contractAddress, 1);
            
            // EIP-6780: Mark the account as created in this transaction
            overlay.MarkCreated(contractAddress);
        }
        else if (codeAddress.HasValue)
        {
            // CALLCODE/DELEGATECALL: execute external code in current contract context.
            code = await overlay.GetCodeAsync(codeAddress.Value, ct);
            contractAddress = tx.To ?? Address.Zero;
            // EIP-7702: if the code address has a delegation designator (0xEF0100 || addr),
            // resolve the actual code from the delegate address.
            // The CALLCODE/DELEGATECALL opcode already charged the access cost for the delegate
            // via access_delegation() equivalent in SystemOpcodes.cs.
            if (block.Rules.HasEip7702SetCode && code.Length == 23 &&
                code[0] == 0xEF && code[1] == 0x01 && code[2] == 0x00)
            {
                var delegateAddr = new Address(code[3..]);
                accessTracker ??= new AccessTracker();
                // Delegate access was already charged (warm/cold) by the opcode; just mark warm.
                accessTracker.WarmAddress(delegateAddr);
                code = await overlay.GetCodeAsync(delegateAddr, ct);
                // contractAddress stays as tx.To (CALLCODE storage context = caller's address,
                // DELEGATECALL storage context = current contract address — both set by opcode).
            }
        }
        else
        {
            // CALL: Use code at To address
            code = tx.To.HasValue ? await overlay.GetCodeAsync(tx.To.Value, ct) : Array.Empty<byte>();
            contractAddress = tx.To ?? Address.Zero;
            // EIP-7702: if the callee has a delegation designator (0xEF0100 || addr),
            // resolve the actual code from the delegate address while keeping storage context.
            // Per EELS process_message_call(): top-level delegation resolution is free —
            // just warm the delegate address and redirect code. Sub-call delegation is
            // handled by the CALL opcode's own warm/cold account gas charge.
            if (block.Rules.HasEip7702SetCode && code.Length == 23 &&
                code[0] == 0xEF && code[1] == 0x01 && code[2] == 0x00)
            {
                var delegateAddr = new Address(code[3..]);
                accessTracker ??= new AccessTracker();
                // Warm the delegate address. The CALL opcode already charged the access cost
                // via access_delegation() equivalent in SystemOpcodes.cs.
                accessTracker.WarmAddress(delegateAddr);
                code = await overlay.GetCodeAsync(delegateAddr, ct);
                // contractAddress stays as tx.To (storage context of the EOA)
            }
        }

        // [AI-EDIT 2026-01-10] Use the pre-computed execution gas limit (post-intrinsic deduction)
        // when provided; otherwise fall back to tx.GasLimit (e.g. for internal sub-calls).
        ulong gasForExecution = executionGasLimit ?? tx.GasLimit;

        // EELS touch_account(): in Frontier/Homestead (pre-EIP-161), every CALL touches the
        // callee address, creating an empty account if it didn't previously exist.
        // SpuriousDragon+ deletes empty accounts at tx end — do NOT touch there.
        if (!creationAddress.HasValue && !codeAddress.HasValue && tx.To.HasValue
            && !block.Rules.HasEip161EmptyAccountDeletion)
        {
            await overlay.TouchAccountAsync(tx.To.Value, ct);
        }

        // Build a gas frame node for this call, appending to the parent's child list.
        GasFrameNode? thisFrame = null;
        if (parentGasFrame != null)
        {
            var toLabel = creationAddress.HasValue
                ? $"CREATE {creationAddress.Value}"
                : tx.To.HasValue
                    ? $"Contract {tx.To.Value} execution"
                    : "root";
            thisFrame = new GasFrameNode { Label = toLabel };
            parentGasFrame.Children.Add(thisFrame);
        }

        // 3. Create execution context
        var context = new ExecutionContext
        {
            Code = code,
            ContractAddress = contractAddress,
            StorageAddress = contractAddress, // Storage owner (same for CALL, caller's address for DELEGATECALL/CALLCODE)
            Caller = tx.From,
            Origin = origin,
            // [AI-EDIT 2026-01-10] For type-2/3 transactions, GASPRICE should return the
            // effective gas price (min(maxFeePerGas, baseFee + maxPriorityFeePerGas)), not maxFeePerGas.
            // For type-0/1, effective gas price == gasPrice so this is always correct.
            GasPrice = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero
                ? BigInteger.Min(tx.MaxFeePerGas, new BigInteger(block.BaseFeePerGas) + tx.MaxPriorityFeePerGas)
                : tx.GasPrice,
            CallValue = tx.Value,
            CallData = (creationAddress.HasValue || !tx.To.HasValue) ? Array.Empty<byte>() : tx.Data, 
            BlobVersionedHashes = tx.BlobVersionedHashes,
            GasLimit = gasForExecution,
            IsStatic = isStatic,
            CaptureTrace = tx.EnableTracing,
            GasFrame = thisFrame,
            CallDepth = depth + 1,
            Block = block,
            GlobalState = overlay,
            Storage = new OverlayStorage(overlay, contractAddress, ct),
            TransientLoad = transientFrame.Load,
            TransientStore = transientFrame.Store,
            Access = accessTracker,
            OriginalStorageValues = originalStorageSnapshot
        };
        
        // [AI-EDIT 2026-08-03] Set call context for security analysis
        var callType = DetermineCallType(creationAddress, codeAddress, isStatic);
        context.SetCallContext(callType, caller: tx.From, codeAddress: codeAddress);

        // Wire up recursion — sub-calls receive their own gas stipend from the calling opcode,
        // so no executionGasLimit override is needed (depth > 0 path).
        // [AI-EDIT 2026-01-10] Share the same AccessTracker across all sub-calls: EIP-2929
        // warm/cold state accumulates across the entire transaction's call tree.
        // [AI-EDIT 2026-07-24] Share the same OriginalStorageValues snapshot across all sub-calls: EIP-2200
        // original values are captured at transaction start and shared across all frames.
        // [AI-EDIT 2026-08-03] Deep CALL recursion (up to 1024 per EVM spec) can exceed
        // .NET's default 1MB thread stack. Callers that execute deep fixture contracts
        // must run on a thread with sufficient stack (see EelsStateFixtureExecutor).
        // The recursion itself is architecturally correct per EELS process_message().

        context.SubCall = (subTx, subIsStatic, subCreateAddr, subCodeAddr) =>
        {
            subTx.BlobVersionedHashes = context.BlobVersionedHashes;
            return ExecuteInternalAsync(
                subTx,
                overlay,
                block,
                origin,
                subCreateAddr,
                subCodeAddr,
                subIsStatic,
                true,
                ct,
                depth + 1,
                executionGasLimit: null,
                transientStorage: transientFrame,
                accessTracker: accessTracker,
                originalStorageSnapshot: originalStorageSnapshot,
                parentGasFrame: thisFrame);
        };

        // 4. Execute
        var result = await _evm.ExecuteAsync(context, ct);

        // 5. Finalize: commit state only on success.
        if (result.IsSuccess)
        {
            if (commit)
            {
                overlay.Commit();
                transientFrame.Commit();
            }
        }
        else
        {
            accessTracker.Restore(accessCheckpoint);
        }
        
        return result;
    }

    private class OverlayStorage : IEvmStorage
    {
        private readonly IGlobalState _state;
        private readonly Address _address;
        private readonly CancellationToken _ct;

        public OverlayStorage(IGlobalState state, Address address, CancellationToken ct)
        {
            _state = state;
            _address = address;
            _ct = ct;
        }

        public ValueTask<BigInteger> LoadAsync(BigInteger key) => _state.GetStorageAtAsync(_address, key, _ct);
        public void Store(BigInteger key, BigInteger value) => _state.SetStorageAt(_address, key, value);
    }

    private interface ITransientStorageFrame
    {
        BigInteger Load(Address address, BigInteger key);
        void Store(Address address, BigInteger key, BigInteger value);
    }

    /// <summary>
    /// Determines the call type based on execution context for security analysis.
    /// </summary>
    private static CallType DetermineCallType(Address? creationAddress, Address? codeAddress, bool isStatic)
    {
        if (creationAddress.HasValue)
        {
            // CREATE vs CREATE2 differentiation would require additional context
            // For now, we mark all contract creations as Create
            return CallType.Create;
        }
        
        if (codeAddress.HasValue)
        {
            // DELEGATECALL vs CALLCODE would need to be passed from the opcode
            // The opcode handler should set this, but for now we assume DELEGATECALL
            // as it's the more common proxy pattern case
            return CallType.DelegateCall;
        }
        
        if (isStatic)
        {
            return CallType.StaticCall;
        }
        
        // Root transaction (depth 0) or regular CALL
        return CallType.Call;
    }

    private sealed class TransientStorageRoot : ITransientStorageFrame
    {
        private readonly Dictionary<Address, Dictionary<BigInteger, BigInteger>> _data = new();

        public BigInteger Load(Address address, BigInteger key)
        {
            if (_data.TryGetValue(address, out var byKey) && byKey.TryGetValue(key, out var value))
            {
                return value;
            }

            return BigInteger.Zero;
        }

        public void Store(Address address, BigInteger key, BigInteger value)
        {
            if (!_data.TryGetValue(address, out var byKey))
            {
                byKey = new Dictionary<BigInteger, BigInteger>();
                _data[address] = byKey;
            }

            byKey[key] = value;
        }
    }

    private sealed class TransientStorageOverlay : ITransientStorageFrame
    {
        private readonly ITransientStorageFrame _parent;
        private readonly Dictionary<Address, Dictionary<BigInteger, BigInteger>> _writes = new();

        public TransientStorageOverlay(ITransientStorageFrame parent)
        {
            _parent = parent;
        }

        public BigInteger Load(Address address, BigInteger key)
        {
            if (_writes.TryGetValue(address, out var byKey) && byKey.TryGetValue(key, out var value))
            {
                return value;
            }

            return _parent.Load(address, key);
        }

        public void Store(Address address, BigInteger key, BigInteger value)
        {
            if (!_writes.TryGetValue(address, out var byKey))
            {
                byKey = new Dictionary<BigInteger, BigInteger>();
                _writes[address] = byKey;
            }

            byKey[key] = value;
        }

        public void Commit()
        {
            foreach (var (address, byKey) in _writes)
            {
                foreach (var (key, value) in byKey)
                {
                    _parent.Store(address, key, value);
                }
            }
        }
    }
}
