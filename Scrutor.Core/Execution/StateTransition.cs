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

    public Task<ExecutionResult> ApplyTransactionAsync(Transaction tx, IGlobalState state, BlockContext block, bool commit = true, CancellationToken ct = default)
    {
        // 0. Signature Recovery
        if (tx.Authorization == TransactionAuthorization.Signed)
        {
            try
            {
                tx.From = CryptoUtils.RecoverAddress(tx.Hash, tx.V, tx.R, tx.S);
            }
            catch
            {
                throw new Exception("Internal transaction error");
            }
        }

        // Delegate to internal execution with origin = tx.From (root)
        return ExecuteInternalAsync(tx, state, block, tx.From, null, commit, ct, 0);
    }

    private async Task<ExecutionResult> ExecuteInternalAsync(Transaction tx, IGlobalState state, BlockContext block, Address origin, Address? creationAddress, bool commit, CancellationToken ct, int depth = 0)
    {
        if (depth > 1024)
             return ExecutionResult.Failure(EvmError.InternalError, 0, null); // Call stack depth limit reached

        // Use a state overlay to ensure snapshot isolation
        var overlay = new StateOverlay(state);

        // 1. Basic validation (nonce, balance for gas)
        var senderNonce = await overlay.GetNonceAsync(tx.From, ct);
        if (tx.Authorization != TransactionAuthorization.Internal) // Skip nonce check for internal calls? Or enforce?
        {
            if (tx.Nonce < senderNonce) return ExecutionResult.Failure(EvmError.NonceTooLow);
            if (tx.Nonce > senderNonce) return ExecutionResult.Failure(EvmError.NonceTooHigh);
        }

        var senderBalance = await overlay.GetBalanceAsync(tx.From, ct);
        var upFrontCost = tx.GasLimit * tx.GasPrice + tx.Value;
        
        // For internal calls, we might skip balance check if we trust the opcode?
        // But strictly, we should check.
        if (senderBalance < upFrontCost)
             return ExecutionResult.Failure(EvmError.InsufficientFunds);

        // 2. Deduct upfront gas cost
        overlay.SetBalance(tx.From, senderBalance - (tx.GasLimit * tx.GasPrice));
        if (tx.Authorization != TransactionAuthorization.Internal)
        {
            overlay.SetNonce(tx.From, senderNonce + 1);
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
        else
        {
            // CALL: Use code at To address
            code = tx.To.HasValue ? await overlay.GetCodeAsync(tx.To.Value, ct) : Array.Empty<byte>();
            contractAddress = tx.To ?? Address.Zero;
        }

        // 3. Create execution context
        var context = new ExecutionContext
        {
            Code = code,
            ContractAddress = contractAddress,
            Caller = tx.From,
            Origin = origin, 
            GasPrice = tx.GasPrice,
            CallValue = tx.Value,
            CallData = (creationAddress.HasValue || !tx.To.HasValue) ? Array.Empty<byte>() : tx.Data, 
            GasLimit = tx.GasLimit,
            Block = block,
            GlobalState = overlay,
            Storage = new OverlayStorage(overlay, contractAddress, ct)
        };

        // Wire up recursion
        context.SubCall = (subTx, isStatic, subCreateAddr) => 
            ExecuteInternalAsync(subTx, overlay, block, origin, subCreateAddr, true, ct, depth + 1);

        // 4. Execute
        var result = await _evm.ExecuteAsync(context, ct);

        // 5. Finalize
        if (result.IsSuccess)
        {
            // Value transfer
            var recipient = creationAddress ?? tx.To;
            if (tx.Value > 0 && recipient.HasValue)
            {
                var recipientBalance = await overlay.GetBalanceAsync(recipient.Value, ct);
                overlay.SetBalance(recipient.Value, recipientBalance + tx.Value);
            }

            // Gas refund
            var gasRefund = (tx.GasLimit - result.GasUsed) * tx.GasPrice;
            if (gasRefund > 0)
            {
                var currentBalance = await overlay.GetBalanceAsync(tx.From, ct);
                overlay.SetBalance(tx.From, currentBalance + gasRefund);
            }

            if (commit)
            {
                overlay.Commit();
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
}