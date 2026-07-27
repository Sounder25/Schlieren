import sys

def patch_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    # Target 1: Add txOverlay
    old_target_1 = """    private async Task<ExecutionResult> ApplyTransactionWithFrameAsync(
        Transaction tx,
        IGlobalState state,
        BlockContext block,
        bool commit,
        CancellationToken ct,
        GasFrameNode rootFrame)
    {
        // Mirror the setup from ApplyTransactionAsync, passing rootFrame to ExecuteInternalAsync.
        if (tx.Authorization == TransactionAuthorization.Signed)"""
        
    new_target_1 = """    private async Task<ExecutionResult> ApplyTransactionWithFrameAsync(
        Transaction tx,
        IGlobalState state,
        BlockContext block,
        bool commit,
        CancellationToken ct,
        GasFrameNode rootFrame)
    {
        var txOverlay = new StateOverlay(state);

        // Mirror the setup from ApplyTransactionAsync, passing rootFrame to ExecuteInternalAsync.
        if (tx.Authorization == TransactionAuthorization.Signed)"""
    content = content.replace(old_target_1, new_target_1)

    # Target 2: Upfront Cost
    old_target_2 = """            var senderBalance = await state.GetBalanceAsync(tx.From, ct);
            var priceForUpfront = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice;
            maxGasCost = new BigInteger(tx.GasLimit) * priceForUpfront;
            blobFee = CalculateBlobFee(tx, block);
            var maxBlobCost = CalculateMaxBlobCost(tx);
            if (senderBalance < maxGasCost + maxBlobCost + tx.Value) return ExecutionResult.Failure(EvmError.InsufficientFunds);
            if (commit) { state.SetBalance(tx.From, senderBalance - maxGasCost - blobFee - tx.Value); state.SetNonce(tx.From, senderNonce + 1); }"""
            
    new_target_2 = """            var senderBalance = await txOverlay.GetBalanceAsync(tx.From, ct);
            var priceForUpfront = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice;
            maxGasCost = new BigInteger(tx.GasLimit) * priceForUpfront;
            blobFee = CalculateBlobFee(tx, block);
            var maxBlobCost = CalculateMaxBlobCost(tx);
            if (senderBalance < maxGasCost + maxBlobCost + tx.Value) return ExecutionResult.Failure(EvmError.InsufficientFunds);
            if (commit) { txOverlay.SetBalance(tx.From, senderBalance - maxGasCost - maxBlobCost - tx.Value); txOverlay.SetNonce(tx.From, senderNonce + 1); }"""
    content = content.replace(old_target_2, new_target_2)

    # Target 3: ExecuteInternalAsync
    old_target_3 = """        var result = await ExecuteInternalAsync(
            tx, state, block, tx.From, topLevelCreation, null, false, commit, ct, 0,
            executionGasLimit, accessTracker: accessTracker, parentGasFrame: rootFrame);"""
            
    new_target_3 = """        var result = await ExecuteInternalAsync(
            tx, txOverlay, block, tx.From, topLevelCreation, null, false, commit, ct, 0,
            executionGasLimit, accessTracker: accessTracker, parentGasFrame: rootFrame);"""
    content = content.replace(old_target_3, new_target_3)

    # Target 4: End Refund
    old_target_4 = """            {
                var currentBalance = await state.GetBalanceAsync(tx.From, ct);
                var gasRefundAmount = new BigInteger(gasRefund) * effectiveGasPrice;
                BigInteger priceDiffRefund = BigInteger.Zero;
                if (tx.TxType >= 2 && tx.MaxFeePerGas > effectiveGasPrice)
                    priceDiffRefund = new BigInteger(tx.GasLimit) * (tx.MaxFeePerGas - effectiveGasPrice);
                var valueRestoration = result.IsSuccess ? BigInteger.Zero : tx.Value;
                state.SetBalance(tx.From, currentBalance + gasRefundAmount + priceDiffRefund + valueRestoration);
            }
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

        return result;"""
        
    new_target_4 = """            {
                var currentBalance = await txOverlay.GetBalanceAsync(tx.From, ct);
                var priceForUpfront = tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero ? tx.MaxFeePerGas : tx.GasPrice;
                var unusedGasRefund = new BigInteger(gasRefund) * priceForUpfront;
                
                BigInteger executionFeeCapRefund = BigInteger.Zero;
                if (priceForUpfront > effectiveGasPrice)
                {
                    executionFeeCapRefund = new BigInteger(totalGasUsed) * (priceForUpfront - effectiveGasPrice);
                }
                
                BigInteger blobFeeCapRefund = BigInteger.Zero;
                if (tx.TxType == 3 && tx.BlobVersionedHashes.Count > 0)
                {
                    var actualBlobBaseFee = block.GetBlobBaseFee();
                    if (tx.MaxFeePerBlobGas > actualBlobBaseFee)
                    {
                        ulong totalBlobGas = 131072UL * (ulong)tx.BlobVersionedHashes.Count;
                        blobFeeCapRefund = new BigInteger(totalBlobGas) * (tx.MaxFeePerBlobGas - actualBlobBaseFee);
                    }
                }
                
                var valueRestoration = result.IsSuccess ? BigInteger.Zero : tx.Value;
                txOverlay.SetBalance(tx.From, currentBalance + unusedGasRefund + executionFeeCapRefund + blobFeeCapRefund + valueRestoration);
            }
            if (!block.Coinbase.Equals(Address.Zero))
            {
                var priorityFee = effectiveGasPrice > baseFeePerGas ? effectiveGasPrice - baseFeePerGas : BigInteger.Zero;
                var minerFee = new BigInteger(totalGasUsed) * priorityFee;
                if (minerFee > 0)
                {
                    var cb = await txOverlay.GetBalanceAsync(block.Coinbase, ct);
                    txOverlay.SetBalance(block.Coinbase, cb + minerFee);
                }
            }
            result = result with { GasUsed = totalGasUsed };
        }

        if (commit)
        {
            txOverlay.Commit();
            foreach (var addr in txOverlay.GetAccountsMarkedForDeletion())
            {
                state.DeleteAccount(addr);
            }
        }

        return result;"""
    content = content.replace(old_target_4, new_target_4)

    with open(filepath, 'w') as f:
        f.write(content)

patch_file("Scrutor.Core/Execution/StateTransition.cs")
print("Done")
