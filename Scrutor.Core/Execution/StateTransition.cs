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

        // [AI-EDIT 2026-01-10] Top-level tx accounting must happen unconditionally
        // on the base state (not an overlay) so that nonce/balance always apply
        // regardless of whether execution succeeds or fails (EIP-161 / Yellow Paper §6).
        // When commit=false (e.g. eth_estimateGas probes), we validate only — no writes.
        BigInteger maxGasCost = BigInteger.Zero;
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
            // [AI-EDIT 2026-01-10] Intrinsic gas must be charged before EVM execution.
            // Yellow Paper §6.2 / EIP-2930: base 21000 + calldata bytes + access-list entries.
            intrinsicGas = IntrinsicGas.Compute(tx);
            if (tx.GasLimit < intrinsicGas)
                return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);

            var senderNonce = await state.GetNonceAsync(tx.From, ct);

            // Validate nonce before deducting anything
            if (tx.Nonce < senderNonce) return ExecutionResult.Failure(EvmError.NonceTooLow);
            if (tx.Nonce > senderNonce) return ExecutionResult.Failure(EvmError.NonceTooHigh);

            // Validate upfront balance covers max possible cost (use maxFeePerGas for type-2, gasPrice otherwise)
            var senderBalance = await state.GetBalanceAsync(tx.From, ct);
            var priceForUpfront = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice;
            maxGasCost = new BigInteger(tx.GasLimit) * priceForUpfront;
            var upfrontCost = maxGasCost + tx.Value;
            if (senderBalance < upfrontCost)
                return ExecutionResult.Failure(EvmError.InsufficientFunds);

            if (commit)
            {
                // Deduct max gas and increment nonce unconditionally (spec §6.2)
                state.SetBalance(tx.From, senderBalance - maxGasCost);
                state.SetNonce(tx.From, senderNonce + 1);
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
            // EIP-2929: precompile addresses 0x01–0x09 are pre-warmed.
            for (int i = 1; i <= 9; i++)
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

        var result = await ExecuteInternalAsync(
            tx, state, block, tx.From, topLevelCreation, null, false, commit, ct, 0,
            executionGasLimit, accessTracker: accessTracker);

        // Install runtime bytecode after successful top-level CREATE (CREATE opcode does this
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
                        state.SetCode(topLevelCreation.Value, result.ReturnData);

                    result = ExecutionResult.Success(
                        result.GasUsed + depositGas,
                        result.ReturnData,
                        result.Logs,
                        result.TraceSteps) with { GasRefundCounter = result.GasRefundCounter };
                }
            }
        }

        // [AI-EDIT 2026-01-10] Post-execution accounting on the base state.
        // Total gas used = intrinsic + EVM execution gas.
        // 1. Refund unspent gas back to sender (always, success or failure)
        // 2. Credit effective total gas fee to coinbase/miner
        // Only when committing (not dry-run probes like estimateGas).
        if (tx.Authorization != TransactionAuthorization.Internal && commit)
        {
            // [AI-EDIT 2026-01-10] Cap EVM-reported gas at executionGasLimit.
            // On OOG, ConsumeGas overshoots (e.g. adds 20000 gas then throws), leaving
            // context.GasUsed > executionGasLimit. Per spec, a frame that OOGs consumes
            // ALL its allocated gas — never more. Capping here ensures accounting is correct.
            var evmGasUsed = result.GasUsed > executionGasLimit ? executionGasLimit : result.GasUsed;
            var totalGasUsed = intrinsicGas + evmGasUsed;

            // EIP-3529: apply capped gas refund. Max refund = totalGasUsed / 5.
            if (result.GasRefundCounter > 0)
            {
                var maxRefund = (long)(totalGasUsed / 5);
                var cappedRefund = Math.Min(result.GasRefundCounter, maxRefund);
                totalGasUsed -= (ulong)cappedRefund;
            }

            var gasRefund = tx.GasLimit > totalGasUsed ? tx.GasLimit - totalGasUsed : 0UL;

            // [AI-EDIT 2026-01-10] Sender balance recovery after execution:
            // 1. Refund for UNUSED gas at the effective gas price.
            // 2. For EIP-1559 (type-2/3): also refund the "price-cap" difference.
            //    The sender paid gasLimit × maxFeePerGas upfront, but should only pay
            //    gasLimit × effectiveGasPrice. The excess (per-gas × total-limit) is returned.
            //    Combined: refund = gasRefund × effectiveGasPrice + gasLimit × (maxFee - effectiveGasPrice)
            {
                var currentBalance = await state.GetBalanceAsync(tx.From, ct);
                var gasRefundAmount = new BigInteger(gasRefund) * effectiveGasPrice;
                BigInteger priceDiffRefund = BigInteger.Zero;
                if (tx.TxType >= 2 && tx.MaxFeePerGas > effectiveGasPrice)
                    priceDiffRefund = new BigInteger(tx.GasLimit) * (tx.MaxFeePerGas - effectiveGasPrice);
                state.SetBalance(tx.From, currentBalance + gasRefundAmount + priceDiffRefund);
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
                    var coinbaseBalance = await state.GetBalanceAsync(block.Coinbase, ct);
                    state.SetBalance(block.Coinbase, coinbaseBalance + minerFee);
                }
            }

            // Return a result that reflects the true total gas used to callers (e.g. eth_getReceipt).
            result = result with { GasUsed = totalGasUsed };
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
        ulong intrinsicGas = 0;
        var baseFeePerGas = new BigInteger(block.BaseFeePerGas);
        BigInteger effectiveGasPrice;
        if (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero)
            effectiveGasPrice = BigInteger.Min(tx.MaxFeePerGas, baseFeePerGas + tx.MaxPriorityFeePerGas);
        else
            effectiveGasPrice = tx.GasPrice;

        if (tx.Authorization != TransactionAuthorization.Internal)
        {
            intrinsicGas = IntrinsicGas.Compute(tx);
            if (tx.GasLimit < intrinsicGas) return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);
            var senderNonce = await state.GetNonceAsync(tx.From, ct);
            if (tx.Nonce < senderNonce) return ExecutionResult.Failure(EvmError.NonceTooLow);
            if (tx.Nonce > senderNonce) return ExecutionResult.Failure(EvmError.NonceTooHigh);
            var senderBalance = await state.GetBalanceAsync(tx.From, ct);
            var priceForUpfront = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice;
            maxGasCost = new BigInteger(tx.GasLimit) * priceForUpfront;
            if (senderBalance < maxGasCost + tx.Value) return ExecutionResult.Failure(EvmError.InsufficientFunds);
            if (commit) { state.SetBalance(tx.From, senderBalance - maxGasCost); state.SetNonce(tx.From, senderNonce + 1); }
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

        if (tx.Authorization != TransactionAuthorization.Internal && commit)
        {
            var evmGasUsed = result.GasUsed > executionGasLimit ? executionGasLimit : result.GasUsed;
            var totalGasUsed = intrinsicGas + evmGasUsed;
            if (result.GasRefundCounter > 0)
            {
                var maxRefund = (long)(totalGasUsed / 5);
                totalGasUsed -= (ulong)Math.Min(result.GasRefundCounter, maxRefund);
            }
            var gasRefund = tx.GasLimit > totalGasUsed ? tx.GasLimit - totalGasUsed : 0UL;
            var currentBalance = await state.GetBalanceAsync(tx.From, ct);
            var gasRefundAmount = new BigInteger(gasRefund) * effectiveGasPrice;
            BigInteger priceDiffRefund = BigInteger.Zero;
            if (tx.TxType >= 2 && tx.MaxFeePerGas > effectiveGasPrice)
                priceDiffRefund = new BigInteger(tx.GasLimit) * (tx.MaxFeePerGas - effectiveGasPrice);
            state.SetBalance(tx.From, currentBalance + gasRefundAmount + priceDiffRefund);
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
             return ExecutionResult.Failure(EvmError.InternalError, 0, null); // Call stack depth limit reached

        // Use a state overlay to ensure snapshot isolation for this execution frame
        var overlay = new StateOverlay(state);
        transientStorage ??= new TransientStorageRoot();
        var transientFrame = new TransientStorageOverlay(transientStorage);
        // [AI-EDIT 2026-01-10] EIP-2929: reuse the top-level access tracker for the whole tx tree.
        accessTracker ??= new AccessTracker();
        // [AI-EDIT 2026-07-24] EIP-2200: reuse the top-level original storage snapshot for the whole tx tree.
        originalStorageSnapshot ??= new Dictionary<(Address, BigInteger), BigInteger>();

        // [AI-EDIT 2026-01-10] For internal sub-calls (CALL/CREATE/etc.), validate
        // that the caller has sufficient balance for the value transfer only.
        // Top-level gas deduction and nonce increment are handled by ApplyTransactionAsync.
        if (tx.Authorization == TransactionAuthorization.Internal)
        {
            var senderBalance = await overlay.GetBalanceAsync(tx.From, ct);
            if (senderBalance < tx.Value)
                return ExecutionResult.Failure(EvmError.InsufficientFunds);
        }

        // [AI-EDIT 2026-01-10] Precompile dispatch: addresses 0x01–0x09 are handled here,
        // not by the EVM bytecode interpreter. Only CALL-type frames qualify (not CREATE).
        if (!creationAddress.HasValue && !codeAddress.HasValue && tx.To.HasValue && Precompiles.IsPrecompile(tx.To.Value))
        {
            var (preOutput, preGas) = Precompiles.Execute(tx.To.Value, tx.Data, tx.GasLimit);
            if (preOutput == null)
            {
                // OOG in precompile — all gas consumed, no state change
                return ExecutionResult.Failure(EvmError.OutOfGas, tx.GasLimit);
            }

            // Precompile succeeded — value transfer and state commit handled here
            if (tx.Value > 0)
            {
                var preOverlay = new StateOverlay(state);
                var senderBal = await preOverlay.GetBalanceAsync(tx.From, ct);
                preOverlay.SetBalance(tx.From, senderBal - tx.Value);
                var recipientBal = await preOverlay.GetBalanceAsync(tx.To.Value, ct);
                preOverlay.SetBalance(tx.To.Value, recipientBal + tx.Value);
                if (commit) preOverlay.Commit();
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
        }
        else if (codeAddress.HasValue)
        {
            // CALLCODE/DELEGATECALL: execute external code in current contract context.
            code = await overlay.GetCodeAsync(codeAddress.Value, ct);
            contractAddress = tx.To ?? Address.Zero;
        }
        else
        {
            // CALL: Use code at To address
            code = tx.To.HasValue ? await overlay.GetCodeAsync(tx.To.Value, ct) : Array.Empty<byte>();
            contractAddress = tx.To ?? Address.Zero;
        }

        // [AI-EDIT 2026-01-10] Use the pre-computed execution gas limit (post-intrinsic deduction)
        // when provided; otherwise fall back to tx.GasLimit (e.g. for internal sub-calls).
        ulong gasForExecution = executionGasLimit ?? tx.GasLimit;

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

        // Wire up recursion — sub-calls receive their own gas stipend from the calling opcode,
        // so no executionGasLimit override is needed (depth > 0 path).
        // [AI-EDIT 2026-01-10] Share the same AccessTracker across all sub-calls: EIP-2929
        // warm/cold state accumulates across the entire transaction's call tree.
        // [AI-EDIT 2026-07-24] Share the same OriginalStorageValues snapshot across all sub-calls: EIP-2200
        // original values are captured at transaction start and shared across all frames.
        context.SubCall = (subTx, subIsStatic, subCreateAddr, subCodeAddr) =>
            ExecuteInternalAsync(
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

        // 4. Execute
        var result = await _evm.ExecuteAsync(context, ct);

        // 5. Finalize: commit state only on success
        if (result.IsSuccess)
        {
            // [AI-EDIT 2026-01-10] Value transfer: atomically debit sender AND credit recipient.
            // Both sides must be updated so balance is conserved. Previously only the credit
            // was applied, leaving the sender over-funded by tx.Value on every successful call.
            var recipient = creationAddress ?? tx.To;
            if (tx.Value > 0 && recipient.HasValue)
            {
                var senderBalance = await overlay.GetBalanceAsync(tx.From, ct);
                overlay.SetBalance(tx.From, senderBalance - tx.Value);

                var recipientBalance = await overlay.GetBalanceAsync(recipient.Value, ct);
                overlay.SetBalance(recipient.Value, recipientBalance + tx.Value);
            }

            if (commit)
            {
                overlay.Commit();
                transientFrame.Commit();
            }
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
