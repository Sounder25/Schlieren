using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Primitives;
using System.Threading;
using System.Diagnostics;

namespace Schlieren.Core.State;

public sealed class MiningService : BackgroundService, IMiningService
{
    private readonly ITxMempool _mempool;
    private readonly IGlobalState _globalState;
    private readonly IChainState _chainState;
    private readonly IStateTransition _stateTransition;
    private readonly ILogger<MiningService> _logger;
    private readonly bool _enableGlobalTracing;

    public MiningService(
        ITxMempool mempool, 
        IGlobalState globalState, 
        IChainState chainState, 
        IStateTransition stateTransition,
        ILogger<MiningService> logger,
        bool enableGlobalTracing = false)
    {
        _mempool = mempool;
        _globalState = globalState;
        _chainState = chainState;
        _stateTransition = stateTransition;
        _logger = logger;
        _enableGlobalTracing = enableGlobalTracing;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mining service started (Automine mode)");
        var miningClock = Stopwatch.StartNew();
        long? nextIntervalMineAtMs = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_chainState.Automine)
            {
                nextIntervalMineAtMs = null;
                await Task.Delay(100, stoppingToken);
                continue;
            }

            if (_mempool.Count == 0)
            {
                // [AI-EDIT 2026-01-10] No pending transactions: reset interval schedule.
                nextIntervalMineAtMs = null;
                await Task.Delay(100, stoppingToken);
                continue;
            }

            var intervalSeconds = _chainState.BlockTimeSeconds;
            if (intervalSeconds.HasValue && intervalSeconds.Value > 0)
            {
                // [AI-EDIT 2026-01-10] Interval mining mode: enforce cadence before producing next block.
                var intervalMs = intervalSeconds.Value * 1000L;
                var nowMs = miningClock.ElapsedMilliseconds;
                nextIntervalMineAtMs ??= nowMs + intervalMs;

                if (nowMs < nextIntervalMineAtMs.Value)
                {
                    var waitMs = (int)Math.Min(100L, nextIntervalMineAtMs.Value - nowMs);
                    await Task.Delay(waitMs, stoppingToken);
                    continue;
                }

                await MineAsync(stoppingToken);
                nextIntervalMineAtMs = miningClock.ElapsedMilliseconds + intervalMs;
                continue;
            }

            // [AI-EDIT 2026-01-10] Instant automine mode: mine immediately when mempool has txs.
            nextIntervalMineAtMs = null;
            await MineAsync(stoppingToken);
            await Task.Delay(25, stoppingToken);
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

        // Assign block hash before receipts so receipt.blockHash is non-zero for clients.
        block.Hash = "0x" + Convert.ToHexString(CryptoUtils.Keccak256(BitConverter.GetBytes(block.Number))).ToLowerInvariant();

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

            // Enable tracing if global tracing is on
            if (_enableGlobalTracing)
            {
                tx.EnableTracing = true;
            }

            var result = await _stateTransition.ApplyTransactionAsync(tx, _globalState, blockContext, ct: ct);

            // Pre-execution validation failures: do not include in block (no receipt → clients hang if we drop after accept).
            if (result.Error is EvmError.NonceTooHigh)
            {
                _mempool.Add(tx);
                _logger.LogDebug("Re-queued tx {Hash}: nonce too high", Convert.ToHexString(tx.Hash));
                continue;
            }

            if (result.Error is EvmError.NonceTooLow or EvmError.InsufficientFunds)
            {
                _logger.LogWarning("Dropped tx {Hash}: {Error}", Convert.ToHexString(tx.Hash), result.Error);
                continue;
            }

            // Success, revert, OOG, invalid opcode, etc. — include so eth_getTransactionReceipt resolves.
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
                TxType = tx.TxType,
                EffectiveGasPrice = tx.GasPrice,
                Logs = result.IsSuccess ? result.Logs : new List<TransactionLog>()
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

            // Store execution trace if available
            if (result.TraceSteps.Count > 0)
            {
                _chainState.BlockStore.AddTrace(receipt.TransactionHash, result.TraceSteps);
            }

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Transaction {Hash} included with status=0 error={Error}",
                    Convert.ToHexString(tx.Hash), result.Error);
            }
        }

        block.GasUsed = cumulativeGasUsed;

        _chainState.UpdateHead(block);
        _logger.LogInformation("Mined block {Number} with {TxCount} transactions", block.Number, block.Transactions.Count);
    }
}