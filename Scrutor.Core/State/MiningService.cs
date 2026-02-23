using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using System.Threading;

namespace Scrutor.Core.State;

public sealed class MiningService : BackgroundService, IMiningService
{
    private readonly ITxMempool _mempool;
    private readonly IGlobalState _globalState;
    private readonly IChainState _chainState;
    private readonly IStateTransition _stateTransition;
    private readonly ILogger<MiningService> _logger;

    public MiningService(
        ITxMempool mempool, 
        IGlobalState globalState, 
        IChainState chainState, 
        IStateTransition stateTransition,
        ILogger<MiningService> logger)
    {
        _mempool = mempool;
        _globalState = globalState;
        _chainState = chainState;
        _stateTransition = stateTransition;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mining service started (Automine mode)");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_mempool.Count > 0)
            {
                await MineAsync(stoppingToken);
            }

            await Task.Delay(100, stoppingToken);
        }
    }

    public async Task MineAsync(CancellationToken ct = default)
    {
        var parent = _chainState.CurrentBlock;
        
        ulong nextTimestamp;
        if (_chainState.NextBlockTimestamp.HasValue)
        {
            nextTimestamp = _chainState.NextBlockTimestamp.Value;
            _chainState.NextBlockTimestamp = null; // Reset after use
        }
        else
        {
            nextTimestamp = (ulong)((long)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _chainState.TimeOffset);
            // Ensure monotonicity
            if (nextTimestamp <= parent.Timestamp) nextTimestamp = parent.Timestamp + 1;
        }

        var block = new Block
        {
            Number = parent.Number + 1,
            ParentHash = parent.Hash,
            Timestamp = nextTimestamp,
            GasLimit = parent.GasLimit,
            BaseFeePerGas = parent.BaseFeePerGas, 
            Miner = "0x" + Convert.ToHexString(new byte[20]).ToLowerInvariant()
        };

        _logger.LogDebug("Producing block {Number}", block.Number);

        ulong cumulativeGasUsed = 0;
        uint txIndex = 0;
        uint logIndex = 0;
        
        var txsToProcess = _mempool.Count;
        while (txsToProcess-- > 0)
        {
            var tx = _mempool.PopBest();
            if (tx == null) break;

            var blockContext = new BlockContext
            {
                ChainId = _chainState.ChainId,
                Number = block.Number,
                Timestamp = block.Timestamp,
                GasLimit = block.GasLimit,
                BaseFeePerGas = block.BaseFeePerGas,
                Coinbase = Address.FromHex(block.Miner)
            };

            var result = await _stateTransition.ApplyTransactionAsync(tx, _globalState, blockContext, ct: ct);
            
            if (result.IsSuccess || result.Error == EvmError.Revert)
            {
                block.Transactions.Add(tx);
                cumulativeGasUsed += result.GasUsed;

                var receipt = new TransactionReceipt
                {
                    TransactionHash = "0x" + Convert.ToHexString(tx.Hash).ToLowerInvariant(),
                    TransactionIndex = txIndex++,
                    BlockHash = block.Hash,
                    BlockNumber = block.Number,
                    From = tx.From.ToString(),
                    To = tx.To?.ToString(),
                    ContractAddress = tx.To == null ? CryptoUtils.DeriveContractAddress(tx.From, tx.Nonce).ToString() : null,
                    GasUsed = result.GasUsed,
                    CumulativeGasUsed = cumulativeGasUsed,
                    Status = result.IsSuccess ? 1UL : 0UL,
                    EffectiveGasPrice = tx.GasPrice,
                    Logs = result.Logs
                };

                for (int i = 0; i < receipt.Logs.Count; i++)
                {
                    var log = receipt.Logs[i];
                    log.BlockNumber = block.Number;
                    log.BlockHash = block.Hash;
                    log.TransactionHash = receipt.TransactionHash;
                    log.TransactionIndex = receipt.TransactionIndex;
                    log.LogIndex = logIndex++;
                }

                _chainState.BlockStore.AddReceipt(receipt);
            }
            else
            {
                if (result.Error == EvmError.NonceTooHigh)
                {
                    _mempool.Add(tx);
                }
                
                _logger.LogWarning("Transaction {Hash} failed with error {Error}", 
                    Convert.ToHexString(tx.Hash), result.Error);
            }
        }

        block.GasUsed = cumulativeGasUsed;
        block.Hash = "0x" + Convert.ToHexString(CryptoUtils.Keccak256(BitConverter.GetBytes(block.Number))).ToLowerInvariant();

        _chainState.UpdateHead(block);
        _logger.LogInformation("Mined block {Number} with {TxCount} transactions", block.Number, block.Transactions.Count);
    }
}