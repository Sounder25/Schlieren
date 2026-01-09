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
        // 0. Signature Recovery (bypass for impersonated or internal transactions)
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

        // Use a state overlay to ensure snapshot isolation
        var overlay = new StateOverlay(state);

        // 1. Basic validation (nonce, balance for gas)
        var senderNonce = await overlay.GetNonceAsync(tx.From, ct);
        if (tx.Nonce < senderNonce)
            return ExecutionResult.Failure(EvmError.NonceTooLow);
        if (tx.Nonce > senderNonce)
            return ExecutionResult.Failure(EvmError.NonceTooHigh);

        var senderBalance = await overlay.GetBalanceAsync(tx.From, ct);
        var upFrontCost = tx.GasLimit * tx.GasPrice + tx.Value;
        if (senderBalance < upFrontCost)
             return ExecutionResult.Failure(EvmError.InsufficientFunds);

        // 2. Deduct upfront gas cost
        overlay.SetBalance(tx.From, senderBalance - (tx.GasLimit * tx.GasPrice));
        overlay.SetNonce(tx.From, senderNonce + 1);

        // 3. Create execution context
        var context = new ExecutionContext
        {
            Code = tx.To.HasValue ? await overlay.GetCodeAsync(tx.To.Value, ct) : tx.Data, 
            ContractAddress = tx.To ?? Address.Zero,
            Caller = tx.From,
            Origin = tx.From, 
            GasPrice = tx.GasPrice,
            CallValue = tx.Value,
            CallData = tx.To.HasValue ? tx.Data : Array.Empty<byte>(), 
            GasLimit = tx.GasLimit,
            Block = block,
            GlobalState = overlay,
            Storage = new OverlayStorage(overlay, tx.To ?? Address.Zero, ct)
        };

        // 4. Execute
        var result = await _evm.ExecuteAsync(context, ct);

        // 5. Finalize
        if (result.IsSuccess)
        {
            // Value transfer
            if (tx.Value > 0 && tx.To.HasValue)
            {
                var recipientBalance = await overlay.GetBalanceAsync(tx.To.Value, ct);
                overlay.SetBalance(tx.To.Value, recipientBalance + tx.Value);
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