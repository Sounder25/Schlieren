using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Schlieren.RPC.Models;
using Schlieren.Core.Models;
using Schlieren.Core.State;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using Schlieren.Core.Configuration;
using Schlieren.RPC;

namespace Schlieren.RPC.Handlers;

/// <summary>
/// Handles all eth_* namespace RPC methods
/// </summary>
public sealed class EthHandlers
{
    private readonly IGlobalState _globalState;
    private readonly ITxMempool _mempool;
    private readonly IChainState _chainState;
    private readonly IStateTransition _stateTransition;
    private readonly IMiningService _miningService;
    private readonly IImpersonationService _impersonation;
    private readonly IAccountManager _accountManager;
    private readonly NodeConfiguration _config;
    private readonly IStateManager _stateManager;
    private readonly Dictionary<string, StateDumpDto> _snapshots = new();
    private int _snapshotIdCounter = 0;

    public EthHandlers(
        IGlobalState globalState, 
        ITxMempool mempool, 
        IChainState chainState, 
        IStateTransition stateTransition, 
        IMiningService miningService, 
        IImpersonationService impersonation,
        IAccountManager accountManager,
        NodeConfiguration config,
        IStateManager stateManager)
    {
        _globalState = globalState;
        _mempool = mempool;
        _chainState = chainState;
        _stateTransition = stateTransition;
        _miningService = miningService;
        _impersonation = impersonation;
        _accountManager = accountManager;
        _config = config;
        _stateManager = stateManager;
    }

    public async Task<string> HandleEthCall(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction call object");

        var callObj = parameters[0] as JsonElement?;
        if (!callObj.HasValue || callObj.Value.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid call object");

        if (callObj.Value.TryGetProperty("from", out var fromProp) &&
            fromProp.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(fromProp.GetString()) &&
            !EthereumTypes.IsValidAddress(fromProp.GetString()!))
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid 'from' address");
        }

        var defaultGasLimit = _chainState.CurrentBlock.GasLimit;
        var tx = BuildCallTransaction(callObj.Value, defaultGasLimit);
        var blockContext = ResolveEthCallBlockContext(parameters);

        // eth_call is not a real tx: use current account nonce (or explicit) so NonceTooLow
        // does not wipe return data after prior deploys/sends from the same from.
        if (callObj.Value.TryGetProperty("nonce", out var callNonceProp))
            tx.Nonce = (ulong)ParseHexQuantityElement(callNonceProp, "nonce");
        else
            tx.Nonce = await _globalState.GetNonceAsync(tx.From, ct);

        var result = await _stateTransition.ApplyTransactionAsync(tx, _globalState, blockContext, commit: false, ct: ct);

        if (!result.IsSuccess && result.Error != EvmError.Revert)
            throw new RpcException(JsonRpcErrorCodes.ExecutionError, $"eth_call failed: {result.Error}");

        return EthereumTypes.ToEthHex(result.ReturnData);
    }

    public async Task<string> HandleEstimateGas(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction call object");

        var callObj = parameters[0] as JsonElement?;
        if (!callObj.HasValue || callObj.Value.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid call object");

        var blockContext = ResolveEstimateGasBlockContext(parameters);
        var tx = BuildCallTransaction(callObj.Value, blockContext.GasLimit);

        // Use sender's current nonce unless explicitly provided.
        tx.Nonce = callObj.Value.TryGetProperty("nonce", out var nonceProp)
            ? (ulong)ParseHexQuantityElement(nonceProp, "nonce")
            : await _globalState.GetNonceAsync(tx.From, ct);

        var intrinsicGas = ComputeIntrinsicGas(tx);
        var upper = tx.GasLimit == 0 ? blockContext.GasLimit : Math.Min(tx.GasLimit, blockContext.GasLimit);
        if (upper < intrinsicGas)
            throw new RpcException(JsonRpcErrorCodes.ExecutionError, $"Unable to estimate gas: provided gas cap {upper} below intrinsic gas {intrinsicGas}");

        // [AI-EDIT 2026-01-10] Tight binary-search bounds: intrinsic floor to capped upper limit.
        ulong lower = intrinsicGas;

        // Must execute successfully at upper bound, otherwise estimation fails.
        var upperTx = CloneForGas(tx, upper);
        var upperResult = await _stateTransition.ApplyTransactionAsync(upperTx, _globalState, blockContext, commit: false, ct: ct);
        if (!upperResult.IsSuccess)
            throw new RpcException(JsonRpcErrorCodes.ExecutionError, $"Unable to estimate gas: execution failed ({upperResult.Error})");

        while (lower < upper)
        {
            var mid = lower + ((upper - lower) / 2);
            var probeTx = CloneForGas(tx, mid);
            var probeResult = await _stateTransition.ApplyTransactionAsync(probeTx, _globalState, blockContext, commit: false, ct: ct);
            if (probeResult.IsSuccess) upper = mid;
            else lower = mid + 1;
        }

        return EthereumTypes.ToEthHex(lower);
    }

    public async Task<string> HandleSendRawTransaction(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing raw transaction data");

        try
        {
            var rawHex = parameters[0]?.ToString() ?? string.Empty;
            var clean = rawHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? rawHex[2..] : rawHex;
            if (string.IsNullOrWhiteSpace(clean) || clean.Length % 2 != 0)
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid raw transaction hex");

            var rawBytes = Convert.FromHexString(clean);
            var tx = Transaction.FromRaw(rawBytes);
            if (tx.R.Length == 0 || tx.S.Length == 0)
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid transaction signature");

            try
            {
                // Typed txs sign keccak(type||rlp(unsigned)); legacy signs EIP-155 digest.
                // Do not recover against tx.Hash (keccak of full signed raw).
                tx.From = CryptoUtils.RecoverAddress(tx.GetRecoveryHash(), tx.V, tx.R, tx.S);
            }
            catch
            {
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid transaction signature");
            }
            _mempool.Add(tx);
            await MaybeAutomineAsync(ct);
            return EthereumTypes.ToEthHex(tx.Hash);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (UnsupportedTransactionTypeException ex)
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, ex.Message);
        }
        catch (Exception ex)
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidRequest, $"Invalid transaction: {ex.Message}");
        }
    }

    public async Task<string> HandleGetTransactionCount(object[] parameters, CancellationToken ct = default)
    {
        ValidateParams(parameters);
        var addressParam = parameters[0]?.ToString() ?? string.Empty;
        var address = Address.FromHex(addressParam);
        
        var nonce = await _globalState.GetNonceAsync(address, ct);

        // If 'pending' tag is used, factor in transactions in mempool
        if (parameters.Length > 1 && parameters[1]?.ToString() == "pending")
        {
            nonce = _mempool.GetNextNonce(address, nonce);
        }

        return EthereumTypes.ToEthHex(nonce);
    }

    public string HandleChainId() => EthereumTypes.ToEthHex(_chainState.ChainId);

    public string HandleNetVersion() => _chainState.ChainId.ToString();

    public bool HandleNetListening() => true;

    public string HandleNetPeerCount() => "0x0";

    public string HandleGasPrice()
    {
        // Prefer configured gas price for deterministic dev-node behavior,
        // but never return below current base fee if it is set.
        var floor = _chainState.CurrentBlock.BaseFeePerGas;
        var configured = new BigInteger(_config.GasPrice);
        var effective = configured < floor ? new BigInteger(floor) : configured;
        return EthereumTypes.ToEthHex(effective);
    }

    /// <summary>
    /// eth_maxPriorityFeePerGas — EIP-1559 priority fee tip suggestion.
    /// Returns the configured gas price minus the current base fee (floored at 1 gwei).
    /// </summary>
    public string HandleMaxPriorityFeePerGas()
    {
        const ulong minTip = 1_000_000_000; // 1 gwei floor
        var baseFee = new BigInteger(_chainState.CurrentBlock.BaseFeePerGas);
        var gasPrice = new BigInteger(_config.GasPrice);
        var tip = gasPrice - baseFee;
        if (tip < BigInteger.Zero) tip = BigInteger.Zero;
        if (tip < minTip) tip = minTip;
        return EthereumTypes.ToEthHex(tip);
    }

    public object HandleFeeHistory(object[] parameters)
    {
        if (parameters == null || parameters.Length < 2)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing required parameters: blockCount and newestBlock");

        var blockCount = ParseQuantityParam(parameters[0], "blockCount");
        if (blockCount == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "blockCount must be greater than zero");
        if (blockCount > 1024)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "blockCount too large (max 1024)");

        var newest = ParseFeeHistoryNewestBlock(parameters[1]);
        var percentiles = ParseRewardPercentiles(parameters.Length > 2 ? parameters[2] : null);

        var oldest = newest >= (blockCount - 1) ? newest - (blockCount - 1) : 0UL;
        var effectiveCount = newest - oldest + 1;

        var baseFees = new List<string>((int)effectiveCount + 1);
        var gasRatios = new List<double>((int)effectiveCount);
        var rewards = percentiles.Count > 0 ? new List<List<string>>((int)effectiveCount) : null;

        Block? lastBlock = null;
        for (ulong n = oldest; n <= newest; n++)
        {
            var block = _chainState.BlockStore.GetBlockByNumber(n)
                ?? new Block { Number = n, GasLimit = _chainState.CurrentBlock.GasLimit, BaseFeePerGas = 0, GasUsed = 0 };

            baseFees.Add(EthereumTypes.ToEthHex(block.BaseFeePerGas));
            gasRatios.Add(block.GasLimit == 0 ? 0d : (double)block.GasUsed / block.GasLimit);

            if (rewards != null)
            {
                var blockReceipts = _chainState.BlockStore.GetReceiptsByBlockNumber(n).ToList();
                var blockRewards = new List<string>(percentiles.Count);
                foreach (var p in percentiles)
                {
                    var tip = ComputeWeightedTipPercentile(blockReceipts, p, block.BaseFeePerGas);
                    blockRewards.Add(EthereumTypes.ToEthHex(tip));
                }
                rewards.Add(blockRewards);
            }

            lastBlock = block;
        }

        // Per eth_feeHistory spec this array has blockCount + 1 entries.
        var nextBaseFee = lastBlock == null ? 0UL : ComputeNextBaseFee(lastBlock);
        baseFees.Add(EthereumTypes.ToEthHex(nextBaseFee));

        // Per spec, 'reward' field must be absent (not null) when no percentiles were requested.
        if (rewards == null)
        {
            return new
            {
                oldestBlock = EthereumTypes.ToEthHex(oldest),
                baseFeePerGas = baseFees,
                gasUsedRatio = gasRatios
            };
        }

        return new
        {
            oldestBlock = EthereumTypes.ToEthHex(oldest),
            baseFeePerGas = baseFees,
            gasUsedRatio = gasRatios,
            reward = rewards
        };
    }

    public string HandleWeb3ClientVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return $"Schlieren/{version}";
    }

    public string HandleBlockNumber()
    {
        return EthereumTypes.ToEthHex(_chainState.CurrentBlock.Number);
    }

    public async Task<string> HandleGetBalance(object[] parameters, CancellationToken ct = default)
    {
        ValidateParams(parameters);
        var addressParam = parameters[0]?.ToString() ?? string.Empty;
        
        if (!EthereumTypes.IsValidAddress(addressParam))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid address");

        var address = Address.FromHex(addressParam);
        var balance = await _globalState.GetBalanceAsync(address, ct);
        
        return EthereumTypes.ToEthHex(balance);
    }

    public async Task<string> HandleGetCode(object[] parameters, CancellationToken ct = default)
    {
        ValidateParams(parameters);
        var addressParam = parameters[0]?.ToString() ?? string.Empty;
        var address = Address.FromHex(addressParam);
        var code = await _globalState.GetCodeAsync(address, ct);
        return EthereumTypes.ToEthHex(code);
    }

    public async Task<string> HandleGetStorageAt(object[] parameters, CancellationToken ct = default)
    {
        if (parameters.Length < 2) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing parameters");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        var key = EthereumTypes.FromEthHexBigInteger(parameters[1]?.ToString() ?? "0x0");
        var value = await _globalState.GetStorageAtAsync(address, key, ct);
        return EthereumTypes.ToEthHex(value);
    }

    public object? HandleGetBlockByNumber(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing block number");

        var tag = parameters[0] switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => parameters[0]?.ToString()
        };

        var includeFullTransactions = false;
        if (parameters.Length > 1)
        {
            includeFullTransactions = parameters[1] switch
            {
                bool b => b,
                JsonElement je when je.ValueKind is JsonValueKind.True or JsonValueKind.False => je.GetBoolean(),
                _ => false
            };
        }

        Block? block = null;
        if (tag == "latest") block = _chainState.CurrentBlock;
        else if (tag == "earliest") block = _chainState.BlockStore.GetBlockByNumber(0);
        else if (tag == "pending") block = _chainState.CurrentBlock;
        else if (tag != null && tag.StartsWith("0x"))
        {
            var number = EthereumTypes.FromEthHex(tag);
            block = _chainState.BlockStore.GetBlockByNumber(number);
        }
        else
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid block number");
        }

        if (block == null)
            return null;

        return BuildBlockByNumberResponse(block, includeFullTransactions);
    }

    private object BuildBlockByNumberResponse(Block block, bool includeFullTransactions)
    {
        var transactions = includeFullTransactions
            ? block.Transactions.Select((tx, index) => new
            {
                hash = EthereumTypes.ToEthHex(tx.Hash),
                nonce = EthereumTypes.ToEthHex(tx.Nonce),
                from = tx.From.ToString(),
                to = tx.To?.ToString(),
                value = EthereumTypes.ToEthHex(tx.Value),
                gas = EthereumTypes.ToEthHex(tx.GasLimit),
                gasPrice = EthereumTypes.ToEthHex(tx.GasPrice),
                input = EthereumTypes.ToEthHex(tx.Data),
                blockHash = block.Hash,
                blockNumber = EthereumTypes.ToEthHex(block.Number),
                transactionIndex = EthereumTypes.ToEthHex((ulong)index)
            }).Cast<object>().ToList()
            : block.Transactions
                .Select(tx => (object)EthereumTypes.ToEthHex(tx.Hash))
                .ToList();

        return new
        {
            number = EthereumTypes.ToEthHex(block.Number),
            hash = block.Hash,
            parentHash = block.ParentHash,
            nonce = "0x0000000000000000",
            sha3Uncles = "0x" + new string('0', 64),
            logsBloom = "0x" + new string('0', 512),
            transactionsRoot = string.IsNullOrWhiteSpace(block.TransactionsRoot) ? "0x" + new string('0', 64) : block.TransactionsRoot,
            stateRoot = string.IsNullOrWhiteSpace(block.StateRoot) ? "0x" + new string('0', 64) : block.StateRoot,
            receiptsRoot = string.IsNullOrWhiteSpace(block.ReceiptsRoot) ? "0x" + new string('0', 64) : block.ReceiptsRoot,
            miner = string.IsNullOrWhiteSpace(block.Miner) ? Address.Zero.ToString() : block.Miner,
            difficulty = EthereumTypes.ToEthHex(block.Difficulty),
            totalDifficulty = EthereumTypes.ToEthHex(block.Difficulty),
            extraData = "0x",
            size = EthereumTypes.ToEthHex((ulong)0),
            gasLimit = EthereumTypes.ToEthHex(block.GasLimit),
            gasUsed = EthereumTypes.ToEthHex(block.GasUsed),
            timestamp = EthereumTypes.ToEthHex(block.Timestamp),
            transactions,
            uncles = Array.Empty<string>(),
            baseFeePerGas = EthereumTypes.ToEthHex(block.BaseFeePerGas)
        };
    }

    public object? HandleGetTransactionByHash(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction hash");

        var hashHex = parameters[0]?.ToString();
        if (hashHex == null) return null;
        hashHex = hashHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hashHex.ToLowerInvariant() : "0x" + hashHex.ToLowerInvariant();

        for (ulong i = _chainState.CurrentBlock.Number; ; i--)
        {
            var block = _chainState.BlockStore.GetBlockByNumber(i);
            if (block == null)
            {
                if (i == 0) break;
                continue;
            }

            for (int index = 0; index < block.Transactions.Count; index++)
            {
                var tx = block.Transactions[index];
                if (!string.Equals(EthereumTypes.ToEthHex(tx.Hash), hashHex, StringComparison.OrdinalIgnoreCase))
                    continue;

                return BuildTransactionJson(tx, block, (ulong)index);
            }

            if (i == 0) break;
        }

        // Mempool (pending) lookup — hash only, no block fields
        foreach (var tx in _mempool.GetPending())
        {
            if (string.Equals(EthereumTypes.ToEthHex(tx.Hash), hashHex, StringComparison.OrdinalIgnoreCase))
                return BuildTransactionJson(tx, block: null, index: null);
        }

        return null;
    }

    public object? HandleGetTransactionReceipt(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction hash");

        var hashHex = parameters[0]?.ToString();
        if (hashHex == null) return null;
        hashHex = hashHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hashHex.ToLowerInvariant() : "0x" + hashHex.ToLowerInvariant();

        var receipt = _chainState.BlockStore.GetReceiptByHash(hashHex);
        if (receipt == null) return null;

        return new
        {
            transactionHash = receipt.TransactionHash,
            transactionIndex = EthereumTypes.ToEthHex(receipt.TransactionIndex),
            blockHash = string.IsNullOrEmpty(receipt.BlockHash) ? "0x" + new string('0', 64) : receipt.BlockHash,
            blockNumber = EthereumTypes.ToEthHex(receipt.BlockNumber),
            from = receipt.From,
            to = receipt.To,
            cumulativeGasUsed = EthereumTypes.ToEthHex(receipt.CumulativeGasUsed),
            gasUsed = EthereumTypes.ToEthHex(receipt.GasUsed),
            contractAddress = receipt.ContractAddress,
            logs = receipt.Logs.Select(ToEthLogJson).ToList(),
            logsBloom = receipt.LogsBloom,
            status = EthereumTypes.ToEthHex(receipt.Status),
            effectiveGasPrice = EthereumTypes.ToEthHex(receipt.EffectiveGasPrice),
            type = EthereumTypes.ToEthHex((ulong)receipt.TxType)
        };
    }

    internal static object ToEthLogJson(TransactionLog log) => new
    {
        address = log.Address,
        topics = log.Topics,
        data = log.Data,
        blockNumber = EthereumTypes.ToEthHex(log.BlockNumber),
        transactionHash = log.TransactionHash,
        transactionIndex = EthereumTypes.ToEthHex(log.TransactionIndex),
        blockHash = log.BlockHash,
        logIndex = EthereumTypes.ToEthHex(log.LogIndex),
        removed = log.Removed
    };

    private static object BuildTransactionJson(Transaction tx, Block? block, ulong? index)
    {
        return new
        {
            hash = EthereumTypes.ToEthHex(tx.Hash),
            nonce = EthereumTypes.ToEthHex(tx.Nonce),
            blockHash = block == null
                ? null
                : (string.IsNullOrWhiteSpace(block.Hash) ? "0x" + new string('0', 64) : block.Hash),
            blockNumber = block == null ? null : EthereumTypes.ToEthHex(block.Number),
            transactionIndex = index == null ? null : EthereumTypes.ToEthHex(index.Value),
            from = tx.From.ToString(),
            to = tx.To?.ToString(),
            value = EthereumTypes.ToEthHex(tx.Value),
            gas = EthereumTypes.ToEthHex(tx.GasLimit),
            gasPrice = EthereumTypes.ToEthHex(tx.GasPrice),
            input = EthereumTypes.ToEthHex(tx.Data),
            type = EthereumTypes.ToEthHex((ulong)tx.TxType),
            v = EthereumTypes.ToEthHex((ulong)tx.V),
            r = EthereumTypes.ToEthHex(tx.R),
            s = EthereumTypes.ToEthHex(tx.S)
        };
    }

    public List<TransactionLog> HandleGetLogs(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing filter object");

        var filter = parameters[0] as JsonElement?;
        if (!filter.HasValue || filter.Value.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid filter object");

        ulong fromBlock;
        ulong toBlock;
        if (filter.Value.TryGetProperty("blockHash", out var blockHashProp))
        {
            if (filter.Value.TryGetProperty("fromBlock", out _) || filter.Value.TryGetProperty("toBlock", out _))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Cannot specify blockHash with fromBlock/toBlock");

            var blockHash = blockHashProp.GetString();
            if (string.IsNullOrWhiteSpace(blockHash) || !IsValidBlockHash(blockHash))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid blockHash");

            var block = _chainState.BlockStore.GetBlockByHash(blockHash!);
            if (block == null) return new List<TransactionLog>();
            fromBlock = block.Number;
            toBlock = block.Number;
        }
        else
        {
            fromBlock = ParseBlockTag(filter.Value, "fromBlock", 0);
            toBlock = ParseBlockTag(filter.Value, "toBlock", _chainState.CurrentBlock.Number);
        }

        if (toBlock < fromBlock)
            return new List<TransactionLog>();

        if ((toBlock - fromBlock) > (ulong)_config.MaxBlocksScanned)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Block range too large (max {_config.MaxBlocksScanned})");

        var addressSet = ParseAddressFilter(filter.Value);

        List<HashSet<string>?>? topicFilters = null;
        if (filter.Value.TryGetProperty("topics", out var topicsProp) && topicsProp.ValueKind == JsonValueKind.Array)
        {
            topicFilters = new List<HashSet<string>?>();
            foreach (var pos in topicsProp.EnumerateArray())
            {
                if (pos.ValueKind == JsonValueKind.Null)
                {
                    topicFilters.Add(null); // wildcard
                }
                else if (pos.ValueKind == JsonValueKind.String)
                {
                    var t = pos.GetString()!;
                    ValidateTopic(t);
                    topicFilters.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t });
                }
                else if (pos.ValueKind == JsonValueKind.Array)
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var t in pos.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String)
                        {
                            var s = t.GetString()!;
                            ValidateTopic(s);
                            set.Add(s);
                        }
                        else if (t.ValueKind != JsonValueKind.Null) 
                            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid topic value");
                    }
                    if (set.Count == 0) return new List<TransactionLog>(); // Empty array means match nothing
                    topicFilters.Add(set);
                }
                else throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid topics format");
            }
            if (topicFilters.Count > 4) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Too many topic filters (max 4)");
        }

        var matched = new List<TransactionLog>();
        for (ulong i = fromBlock; i <= toBlock; i++)
        {
            foreach (var receipt in _chainState.BlockStore.GetReceiptsByBlockNumber(i))
            {
                foreach (var log in receipt.Logs)
                {
                    if (addressSet != null && !addressSet.Contains(log.Address)) continue;
                    if (topicFilters != null && !TopicsMatch(topicFilters, log.Topics)) continue;

                    matched.Add(log);
                    if (matched.Count >= _config.MaxLogsReturned) break;
                }
                if (matched.Count >= _config.MaxLogsReturned) break;
            }
            if (matched.Count >= _config.MaxLogsReturned) break;
        }

        return matched
            .OrderBy(l => l.BlockNumber)
            .ThenBy(l => l.LogIndex)
            .ToList();
    }

    private void ValidateTopic(string topic)
    {
        if (topic.Length != 66 || !topic.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid topic: {topic} (must be 32-byte hex string)");
    }

    private HashSet<string>? ParseAddressFilter(JsonElement filter)
    {
        if (!filter.TryGetProperty("address", out var addrProp))
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (addrProp.ValueKind == JsonValueKind.String)
        {
            var address = addrProp.GetString();
            if (string.IsNullOrWhiteSpace(address) || !EthereumTypes.IsValidAddress(address))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid address filter");
            set.Add(address);
            return set;
        }

        if (addrProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in addrProp.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.String)
                    throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid address filter");
                var address = a.GetString();
                if (string.IsNullOrWhiteSpace(address) || !EthereumTypes.IsValidAddress(address))
                    throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid address filter");
                set.Add(address);
            }
            if (set.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return set;
        }

        throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid address filter");
    }

    private static bool IsValidBlockHash(string hash)
    {
        if (!hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || hash.Length != 66)
            return false;
        return hash[2..].All(c => Uri.IsHexDigit(c));
    }

    private ulong ParseBlockTag(JsonElement filter, string propertyName, ulong defaultValue)
    {
        if (filter.TryGetProperty(propertyName, out var prop))
        {
            var tag = prop.GetString();
            if (tag == "latest" || tag == "pending") return _chainState.CurrentBlock.Number;
            if (tag == "earliest") return 0;
            if (tag != null && tag.StartsWith("0x")) return EthereumTypes.FromEthHex(tag);
        }
        return defaultValue;
    }

    private static bool TopicsMatch(List<HashSet<string>?> filters, List<string> logTopics)
    {
        for (int i = 0; i < filters.Count; i++)
        {
            var f = filters[i];
            if (f == null) continue; // wildcard
            if (i >= logTopics.Count) return false;
            if (!f.Contains(logTopics[i])) return false;
        }
        return true;
    }

    public string[] HandleAccounts() => _accountManager.GetAddresses().Select(a => a.ToString()).ToArray();

    public void IncrementBlockNumber() { }

    public bool HandleAnvilSetBalance(object[] parameters)
    {
        if (parameters.Length < 2) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing parameters");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        // Wei amounts must parse as BigInteger — UInt64 overflows on typical balances.
        var balance = EthereumTypes.FromEthHexBigInteger(parameters[1]?.ToString() ?? "0x0");
        _globalState.SetBalance(address, balance);
        return true;
    }

    public bool HandleAnvilSetNonce(object[] parameters)
    {
        if (parameters.Length < 2) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing parameters");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        var nonce = EthereumTypes.FromEthHex(parameters[1]?.ToString() ?? "0x0");
        _globalState.SetNonce(address, nonce);
        return true;
    }

    public bool HandleAnvilSetCode(object[] parameters)
    {
        if (parameters.Length < 2) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing parameters");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        var codeHex = parameters[1]?.ToString() ?? "0x";
        var code = Convert.FromHexString(codeHex.StartsWith("0x") ? codeHex[2..] : codeHex);
        _globalState.SetCode(address, code);
        return true;
    }

    public bool HandleAnvilSetStorageAt(object[] parameters)
    {
        if (parameters.Length < 3) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing parameters");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        var key = EthereumTypes.FromEthHexBigInteger(parameters[1]?.ToString() ?? "0x0");
        var value = EthereumTypes.FromEthHexBigInteger(parameters[2]?.ToString() ?? "0x0");
        _globalState.SetStorageAt(address, key, value);
        return true;
    }

    public async Task<string> HandleSendTransaction(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction object");

        var txObj = parameters[0] as JsonElement?;
        if (!txObj.HasValue || txObj.Value.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid transaction object");

        if (!txObj.Value.TryGetProperty("from", out var fromProp) || string.IsNullOrWhiteSpace(fromProp.GetString()))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Sender 'from' address is required for eth_sendTransaction");

        var fromHex = fromProp.GetString()!;
        if (!EthereumTypes.IsValidAddress(fromHex))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid 'from' address");
        var fromAddress = Address.FromHex(fromHex);
        
        var isImpersonated = _impersonation.IsImpersonated(fromAddress);
        var isManagedAccount = _accountManager.GetPrivateKey(fromAddress) != null;
        if (!isImpersonated && !isManagedAccount)
        {
            throw new RpcException(-32000, $"Sender account {fromAddress} is not impersonated or unlocked. " +
                "Use anvil_impersonateAccount to unlock it for unsigned transactions.");
        }

        if (txObj.Value.TryGetProperty("gasPrice", out _) &&
            (txObj.Value.TryGetProperty("maxFeePerGas", out _) || txObj.Value.TryGetProperty("maxPriorityFeePerGas", out _)))
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Cannot specify both gasPrice and EIP-1559 fee fields");
        }

        var toAddress = ParseOptionalAddress(txObj.Value, "to");
        var value = ParseOptionalQuantity(txObj.Value, "value", BigInteger.Zero);
        var data = ParseOptionalData(txObj.Value, "data");
        var gasLimit = (ulong)ParseOptionalQuantity(txObj.Value, "gas", _chainState.CurrentBlock.GasLimit);
        var gasPrice = ResolveSendTransactionGasPrice(txObj.Value);

        // Build the transaction
        var tx = new Transaction
        {
            From = fromAddress,
            To = toAddress,
            Value = value,
            Data = data,
            GasLimit = gasLimit,
            GasPrice = gasPrice,
            Authorization = TransactionAuthorization.Impersonated
        };

        // Set nonce (if not provided)
        if (txObj.Value.TryGetProperty("nonce", out var nonceProp))
        {
            tx.Nonce = (ulong)ParseHexQuantityElement(nonceProp, "nonce");
        }
        else
        {
            var baseNonce = await _globalState.GetNonceAsync(fromAddress, ct);
            tx.Nonce = await _mempool.ReserveNonceAsync(fromAddress, baseNonce);
        }

        tx.Hash = ComputeUnsignedTransactionHash(tx);

        _mempool.Add(tx);
        await MaybeAutomineAsync(ct);
        return EthereumTypes.ToEthHex(tx.Hash);
    }

    /// <summary>
    /// Instant automine (Anvil-compatible): mine before send* returns so receipt is available
    /// for clients that only call eth_getTransactionReceipt once (e.g. hardhat-chai-matchers emit).
    /// </summary>
    private async Task MaybeAutomineAsync(CancellationToken ct)
    {
        if (!_chainState.Automine)
            return;
        // Interval mining still uses the background MiningService cadence.
        if (_chainState.BlockTimeSeconds is > 0)
            return;
        await _miningService.MineAsync(ct);
    }

    public bool HandleAnvilImpersonateAccount(object[] parameters)
    {
        if (parameters.Length < 1) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing address");
        
        string addressStr;
        if (parameters[0] is JsonElement je && je.ValueKind == JsonValueKind.String)
        {
            addressStr = je.GetString()!;
        }
        else
        {
            addressStr = parameters[0]?.ToString() ?? string.Empty;
        }

        var address = Address.FromHex(addressStr);
        _impersonation.Impersonate(address);
        return true;
    }

    public bool HandleAnvilStopImpersonatingAccount(object[] parameters)
    {
        if (parameters.Length < 1) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing address");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        _impersonation.StopImpersonating(address);
        return true;
    }

    public async Task<string> HandleAnvilMine(object[] parameters)
    {
        await _miningService.MineAsync();
        return "0x1"; 
    }

    public long HandleEvmIncreaseTime(object[] parameters)
    {
        if (parameters.Length < 1) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing seconds parameter");
        var seconds = (long)EthereumTypes.FromEthHex(parameters[0]?.ToString() ?? "0x0");
        _chainState.TimeOffset += seconds;
        return _chainState.TimeOffset;
    }

    public bool HandleAnvilSetAutomine(object[] parameters)
    {
        if (parameters.Length < 1)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing automine boolean parameter");

        bool automine;
        var p = parameters[0];
        if (p is bool b)
        {
            automine = b;
        }
        else if (p is JsonElement je && (je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False))
        {
            automine = je.GetBoolean();
        }
        else if (bool.TryParse(p?.ToString(), out var parsed))
        {
            automine = parsed;
        }
        else
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid automine value (expected true/false)");
        }

        _chainState.Automine = automine;
        return true;
    }

    public bool HandleAnvilGetAutomine() => _chainState.Automine;

    public ulong HandleAnvilSetNextBlockTimestamp(object[] parameters)
    {
        if (parameters.Length < 1) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing timestamp parameter");
        var timestamp = EthereumTypes.FromEthHex(parameters[0]?.ToString() ?? "0x0");
        _chainState.NextBlockTimestamp = timestamp;
        return timestamp;
    }

    public void SetBalance(string addressHex, BigInteger balance)
    {
         var address = Address.FromHex(addressHex);
         _globalState.SetBalance(address, balance);
    }

    public string HandleAnvilShowPrivateKey(object[] parameters)
    {
        if (parameters.Length < 1) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing address");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        var key = _accountManager.GetPrivateKey(address);
        if (key == null) throw new RpcException(-32000, "Address not managed by this node");
        return key.StartsWith("0x") ? key : "0x" + key;
    }

    public string? HandleAnvilShowMnemonic() => _accountManager.Mnemonic;

    public string HandleEvmSnapshot(object[] parameters)
    {
        var id = "0x" + (++_snapshotIdCounter).ToString("x");
        var state = _stateManager.CaptureState();
        _snapshots[id] = state;
        return id;
    }

    public bool HandleEvmRevert(object[] parameters)
    {
        if (parameters.Length < 1) return false;
        var id = parameters[0]?.ToString();
        if (id == null || !_snapshots.ContainsKey(id)) return false;

        var state = _snapshots[id];
        _stateManager.RestoreState(state);
        
        // Reset mempool nonce reservations for all accounts involved in the revert
        foreach (var addrStr in state.Accounts.Keys)
        {
            _mempool.ResetReservation(Address.FromHex(addrStr));
        }
        
        return true;
    }

    public async Task<object> HandleDebugTraceTransaction(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length < 1)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction hash");

        var hash = parameters[0]?.ToString();
        if (string.IsNullOrWhiteSpace(hash))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid transaction hash");

        var normalizedHash = hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hash.ToLowerInvariant()
            : "0x" + hash.ToLowerInvariant();

        // Parse options once
        var options = ParseTraceOptions(parameters.Length > 1 ? parameters[1] : null);
        
        // First try to get the stored trace
        var storedTrace = _chainState.BlockStore.GetTraceByHash(normalizedHash);
        var receipt = _chainState.BlockStore.GetReceiptByHash(normalizedHash);

        if (storedTrace != null && receipt != null)
        {
            // Return stored trace with receipt data
            return BuildTraceResponseFromStored(receipt, storedTrace, options);
        }

        // Fallback: try to find transaction and replay (for un-traced transactions)
        Transaction? tx = null;
        foreach (var block in _chainState.BlockStore.GetAllBlocks())
        {
            tx = block.Transactions.FirstOrDefault(t => EthereumTypes.ToEthHex(t.Hash) == normalizedHash);
            if (tx != null) break;
        }

        if (tx == null)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Transaction not found");

        var replay = new Transaction
        {
            Hash = tx.Hash,
            From = tx.From,
            To = tx.To,
            Value = tx.Value,
            Nonce = tx.Nonce,
            GasPrice = tx.GasPrice,
            GasLimit = tx.GasLimit,
            Data = tx.Data,
            Authorization = TransactionAuthorization.Internal,  // skip nonce/balance: replay only
            EnableTracing = true
        };

        var result = await _stateTransition.ApplyTransactionAsync(
            replay,
            _globalState,
            BuildCurrentBlockContext(),
            commit: false,
            ct: ct);

        return BuildTraceResponse(result, options);
    }

    public async Task<object> HandleDebugTraceCall(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction call object");

        var callObj = parameters[0] as JsonElement?;
        if (!callObj.HasValue || callObj.Value.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid call object");

        object? blockSelector = null;
        object? optionsParam = null;
        if (parameters.Length > 1)
        {
            if (parameters[1] is JsonElement p1 && p1.ValueKind == JsonValueKind.Object)
                optionsParam = parameters[1];
            else
                blockSelector = parameters[1];
        }
        if (parameters.Length > 2)
            optionsParam = parameters[2];

        var blockContext = blockSelector == null
            ? BuildCurrentBlockContext()
            : ResolveEthCallBlockContext(new[] { parameters[0], blockSelector });

        var tx = BuildCallTransaction(callObj.Value, blockContext.GasLimit);
        tx.Nonce = await _globalState.GetNonceAsync(tx.From, ct);
        tx.EnableTracing = true;

        var result = await _stateTransition.ApplyTransactionAsync(
            tx,
            _globalState,
            blockContext,
            commit: false,
            ct: ct);

        var options = ParseTraceOptions(optionsParam);
        return BuildTraceResponse(result, options);
    }

    public async Task<object> HandleDebugTraceBlockByNumber(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing block number");

        var blockNumber = ParseBlockTagParam(parameters[0]);
        var block = _chainState.BlockStore.GetBlockByNumber(blockNumber);
        if (block == null)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Block not found");

        var options = ParseTraceOptions(parameters.Length > 1 ? parameters[1] : null);
        return await TraceBlockTransactions(block, options, ct);
    }

    public async Task<object> HandleDebugTraceBlockByHash(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing block hash");

        var blockHash = parameters[0]?.ToString();
        if (string.IsNullOrWhiteSpace(blockHash))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid block hash");

        var block = _chainState.BlockStore.GetBlockByHash(blockHash!);
        if (block == null)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Block not found");

        var options = ParseTraceOptions(parameters.Length > 1 ? parameters[1] : null);
        return await TraceBlockTransactions(block, options, ct);
    }

    public async Task<object> HandleDebugWhyNot(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction call object or transaction hash");

        Transaction tx;
        BlockContext blockContext;
        BigInteger senderBalance;
        ulong senderNonce;
        ulong intrinsicGas;
        string source;

        if (parameters[0] is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var blockSelector = parameters.Length > 1 ? parameters[1] : null;
            blockContext = blockSelector == null
                ? BuildCurrentBlockContext()
                : ResolveEthCallBlockContext(new[] { parameters[0], blockSelector });

            tx = BuildCallTransaction(je, blockContext.GasLimit);
            tx.Nonce = je.TryGetProperty("nonce", out var nonceProp)
                ? (ulong)ParseHexQuantityElement(nonceProp, "nonce")
                : await _globalState.GetNonceAsync(tx.From, ct);
            tx.EnableTracing = true;
            source = "callObject";
        }
        else
        {
            var hash = parameters[0] switch
            {
                JsonElement p when p.ValueKind == JsonValueKind.String => p.GetString(),
                _ => parameters[0]?.ToString()
            };
            if (string.IsNullOrWhiteSpace(hash))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid transaction hash");

            var normalizedHash = hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? hash.ToLowerInvariant()
                : "0x" + hash.ToLowerInvariant();

            Transaction? found = null;
            Block? foundBlock = null;
            foreach (var b in _chainState.BlockStore.GetAllBlocks())
            {
                found = b.Transactions.FirstOrDefault(t => EthereumTypes.ToEthHex(t.Hash) == normalizedHash);
                if (found != null)
                {
                    foundBlock = b;
                    break;
                }
            }

            if (found == null || foundBlock == null)
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Transaction not found");

            tx = new Transaction
            {
                Hash = found.Hash,
                From = found.From,
                To = found.To,
                Value = found.Value,
                Nonce = found.Nonce,
                GasPrice = found.GasPrice,
                GasLimit = found.GasLimit,
                Data = found.Data,
                Authorization = TransactionAuthorization.Impersonated,
                EnableTracing = true
            };
            blockContext = BuildBlockContextForTrace(foundBlock);
            source = "transactionHash";
        }

        senderBalance = await _globalState.GetBalanceAsync(tx.From, ct);
        senderNonce = await _globalState.GetNonceAsync(tx.From, ct);
        intrinsicGas = ComputeIntrinsicGas(tx);

        var result = await _stateTransition.ApplyTransactionAsync(
            tx,
            _globalState,
            blockContext,
            commit: false,
            ct: ct);

        var reasons = ClassifyWhyNotReasons(tx, result, senderBalance, senderNonce, intrinsicGas);

        return new
        {
            source,
            success = result.IsSuccess,
            error = result.Error.ToString(),
            gasUsed = EthereumTypes.ToEthHex(result.GasUsed),
            intrinsicGas = EthereumTypes.ToEthHex(intrinsicGas),
            reasons
        };
    }

    private BlockContext BuildCurrentBlockContext()
    {
        var block = _chainState.CurrentBlock;
        return new BlockContext
        {
            ChainId = _chainState.ChainId,
            Number = block.Number,
            Timestamp = block.Timestamp,
            GasLimit = block.GasLimit,
            Difficulty = block.Difficulty,
            BaseFeePerGas = block.BaseFeePerGas,
            Coinbase = string.IsNullOrEmpty(block.Miner) ? Address.Zero : Address.FromHex(block.Miner)
        };
    }

    private BlockContext BuildBlockContextForTrace(Block block)
    {
        return new BlockContext
        {
            ChainId = _chainState.ChainId,
            Number = block.Number,
            Timestamp = block.Timestamp,
            GasLimit = block.GasLimit,
            Difficulty = block.Difficulty,
            BaseFeePerGas = block.BaseFeePerGas,
            Coinbase = string.IsNullOrEmpty(block.Miner) ? Address.Zero : Address.FromHex(block.Miner)
        };
    }

    private BlockContext ResolveEthCallBlockContext(object[] parameters)
    {
        // Supports latest/pending/earliest and explicit hex quantity up to current head.
        if (parameters.Length < 2)
            return BuildCurrentBlockContext();

        var tagOrNumber = parameters[1] switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => parameters[1]?.ToString()
        };

        if (string.IsNullOrWhiteSpace(tagOrNumber) ||
            string.Equals(tagOrNumber, "latest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagOrNumber, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCurrentBlockContext();
        }

        if (string.Equals(tagOrNumber, "earliest", StringComparison.OrdinalIgnoreCase))
        {
            var genesis = _chainState.BlockStore.GetBlockByNumber(0) ?? _chainState.CurrentBlock;
            return new BlockContext
            {
                ChainId = _chainState.ChainId,
                Number = genesis.Number,
                Timestamp = genesis.Timestamp,
                GasLimit = genesis.GasLimit,
                Difficulty = genesis.Difficulty,
                BaseFeePerGas = genesis.BaseFeePerGas,
                Coinbase = string.IsNullOrEmpty(genesis.Miner) ? Address.Zero : Address.FromHex(genesis.Miner)
            };
        }

        if (!tagOrNumber.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid block tag for eth_call");

        var requestedNumber = EthereumTypes.FromEthHex(tagOrNumber);
        var head = _chainState.CurrentBlock.Number;
        if (requestedNumber > head)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "eth_call block number is greater than current head");

        // Historical state snapshots are not yet persisted; execution uses current state with validated block context.
        return BuildCurrentBlockContext();
    }

    private BlockContext ResolveEstimateGasBlockContext(object[] parameters)
    {
        // Supports optional block tag in second positional parameter.
        if (parameters.Length > 1)
        {
            var p = parameters[1];
            var isStringTag = p is string || (p is JsonElement je && je.ValueKind == JsonValueKind.String);
            if (isStringTag)
            {
                return ResolveEthCallBlockContext(new object[] { new object(), p! });
            }
        }

        return BuildCurrentBlockContext();
    }

    private static Transaction BuildCallTransaction(JsonElement callObj, ulong defaultGasLimit)
    {
        byte[] data = Array.Empty<byte>();
        if (callObj.TryGetProperty("data", out var dataProp))
        {
            var dataHex = dataProp.GetString() ?? "0x";
            var clean = dataHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? dataHex[2..] : dataHex;
            data = clean.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(clean);
        }

        var tx = new Transaction
        {
            From = callObj.TryGetProperty("from", out var fromProp) && !string.IsNullOrWhiteSpace(fromProp.GetString())
                ? Address.FromHex(fromProp.GetString()!)
                : Address.Zero,
            To = callObj.TryGetProperty("to", out var toProp) && !string.IsNullOrWhiteSpace(toProp.GetString())
                ? Address.FromHex(toProp.GetString()!)
                : null,
            Data = data,
            Value = callObj.TryGetProperty("value", out var valueProp)
                ? EthereumTypes.FromEthHexBigInteger(valueProp.GetString() ?? "0x0")
                : BigInteger.Zero,
            // Use call-style execution assumptions by default for estimation.
            GasPrice = callObj.TryGetProperty("gasPrice", out var gasPriceProp)
                ? EthereumTypes.FromEthHexBigInteger(gasPriceProp.GetString() ?? "0x0")
                : BigInteger.Zero,
            GasLimit = callObj.TryGetProperty("gas", out var gasProp)
                ? EthereumTypes.FromEthHex(gasProp.GetString() ?? "0x0")
                : defaultGasLimit,
            Authorization = TransactionAuthorization.Simulation
        };

        return tx;
    }

    private static Transaction CloneForGas(Transaction source, ulong gasLimit)
    {
        return new Transaction
        {
            Hash = source.Hash,
            From = source.From,
            To = source.To,
            Value = source.Value,
            Nonce = source.Nonce,
            GasPrice = source.GasPrice,
            GasLimit = gasLimit,
            Data = source.Data,
            Authorization = source.Authorization,
            EnableTracing = source.EnableTracing
        };
    }

    private static ulong ComputeIntrinsicGas(Transaction tx)
    {
        const ulong txBase = 21_000;
        const ulong zeroByteCost = 4;
        const ulong nonZeroByteCost = 16;
        const ulong createCost = 32_000;

        ulong gas = txBase;
        if (tx.To == null)
        {
            checked { gas += createCost; }
        }

        foreach (var b in tx.Data)
        {
            checked { gas += b == 0 ? zeroByteCost : nonZeroByteCost; }
        }

        return gas;
    }

    private static string ParseHexGasCostAsDecimal(string gasCostHex)
    {
        if (string.IsNullOrWhiteSpace(gasCostHex))
            return "0";
        
        try
        {
            var value = EthereumTypes.FromEthHex(gasCostHex);
            return value.ToString();
        }
        catch
        {
            return "0";
        }
    }

    private static object BuildTraceResponse(ExecutionResult result, TraceOptions options)
    {
        var steps = result.TraceSteps.AsEnumerable();
        if (options.Limit.HasValue)
        {
            steps = steps.Take(options.Limit.Value);
        }

        return new
        {
            gas = EthereumTypes.ToEthHex(result.GasUsed),
            failed = !result.IsSuccess,
            returnValue = EthereumTypes.ToEthHex(result.ReturnData),
            structLogs = steps.Select(s => new
            {
                pc = s.Pc,
                op = s.Op,
                gas = s.Gas,
                gasCost = s.GasCost,
                gasCostDec = ParseHexGasCostAsDecimal(s.GasCost),
                depth = s.Depth,
                stack = options.DisableStack ? new List<string>() : s.Stack,
                memory = options.DisableMemory ? new List<string>() : s.Memory,
                storage = options.DisableStorage ? new Dictionary<string, string>() : s.Storage,
                contract = s.ContractAddress,
                caller = s.CallerAddress,
                callType = s.CallType?.ToString(),
                output = s.OutputData != null ? EthereumTypes.ToEthHex(s.OutputData) : null
            }).ToList()
        };
    }

    private static object BuildTraceResponseFromStored(TransactionReceipt receipt, List<ExecutionTraceStep> traceSteps, TraceOptions options)
    {
        var steps = traceSteps.AsEnumerable();
        if (options.Limit.HasValue)
        {
            steps = steps.Take(options.Limit.Value);
        }

        return new
        {
            gas = EthereumTypes.ToEthHex(receipt.GasUsed),
            failed = receipt.Status == 0,
            returnValue = "0x",  // We don't store return value separately
            structLogs = steps.Select(s => new
            {
                pc = s.Pc,
                op = s.Op,
                gas = s.Gas,
                gasCost = s.GasCost,
                gasCostDec = ParseHexGasCostAsDecimal(s.GasCost),
                depth = s.Depth,
                stack = options.DisableStack ? new List<string>() : s.Stack,
                memory = options.DisableMemory ? new List<string>() : s.Memory,
                storage = options.DisableStorage ? new Dictionary<string, string>() : s.Storage,
                contract = s.ContractAddress,
                caller = s.CallerAddress,
                callType = s.CallType?.ToString(),
                output = s.OutputData != null ? EthereumTypes.ToEthHex(s.OutputData) : null
            }).ToList()
        };
    }

    private async Task<List<object>> TraceBlockTransactions(Block block, TraceOptions options, CancellationToken ct)
    {
        var traces = new List<object>(block.Transactions.Count);
        foreach (var tx in block.Transactions)
        {
            var replay = new Transaction
            {
                Hash = tx.Hash,
                From = tx.From,
                To = tx.To,
                Value = tx.Value,
                Nonce = tx.Nonce,
                GasPrice = tx.GasPrice,
                GasLimit = tx.GasLimit,
                Data = tx.Data,
                Authorization = TransactionAuthorization.Impersonated,
                EnableTracing = true
            };

            var result = await _stateTransition.ApplyTransactionAsync(
                replay,
                _globalState,
                BuildBlockContextForTrace(block),
                commit: false,
                ct: ct);

            traces.Add(new
            {
                txHash = EthereumTypes.ToEthHex(tx.Hash),
                result = BuildTraceResponse(result, options)
            });
        }
        return traces;
    }

    private static TraceOptions ParseTraceOptions(object? parameter)
    {
        if (parameter == null) return TraceOptions.Default;
        if (parameter is not JsonElement je) return TraceOptions.Default;
        if (je.ValueKind == JsonValueKind.Null) return TraceOptions.Default;
        if (je.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Trace options must be an object");

        var disableStack = GetBooleanOption(je, "disableStack");
        var disableMemory = GetBooleanOption(je, "disableMemory");
        var disableStorage = GetBooleanOption(je, "disableStorage");
        var limit = GetIntegerOption(je, "limit");

        return new TraceOptions(disableStack, disableMemory, disableStorage, limit);
    }

    private static List<object> ClassifyWhyNotReasons(
        Transaction tx,
        ExecutionResult result,
        BigInteger senderBalance,
        ulong senderNonce,
        ulong intrinsicGas)
    {
        var reasons = new List<object>();
        var upfrontCost = (new BigInteger(tx.GasLimit) * tx.GasPrice) + tx.Value;

        if (tx.Nonce < senderNonce)
        {
            reasons.Add(new
            {
                code = "nonce_too_low",
                message = "Transaction nonce is below sender account nonce.",
                evidence = new
                {
                    txNonce = EthereumTypes.ToEthHex(tx.Nonce),
                    stateNonce = EthereumTypes.ToEthHex(senderNonce)
                },
                recommendation = "Increase transaction nonce to at least current state nonce."
            });
        }
        else if (tx.Nonce > senderNonce)
        {
            reasons.Add(new
            {
                code = "nonce_too_high",
                message = "Transaction nonce is above sender account nonce.",
                evidence = new
                {
                    txNonce = EthereumTypes.ToEthHex(tx.Nonce),
                    stateNonce = EthereumTypes.ToEthHex(senderNonce)
                },
                recommendation = "Use the next pending nonce or submit intermediate nonces first."
            });
        }

        if (senderBalance < upfrontCost)
        {
            reasons.Add(new
            {
                code = "insufficient_funds",
                message = "Sender balance cannot cover value plus maximum gas cost.",
                evidence = new
                {
                    senderBalance = EthereumTypes.ToEthHex(senderBalance),
                    requiredUpfront = EthereumTypes.ToEthHex(upfrontCost),
                    gasLimit = EthereumTypes.ToEthHex(tx.GasLimit),
                    gasPrice = EthereumTypes.ToEthHex(tx.GasPrice),
                    value = EthereumTypes.ToEthHex(tx.Value)
                },
                recommendation = "Fund sender account or reduce value/gas settings."
            });
        }

        if (tx.GasLimit < intrinsicGas)
        {
            reasons.Add(new
            {
                code = "intrinsic_gas_too_low",
                message = "Gas limit is below intrinsic transaction gas requirement.",
                evidence = new
                {
                    gasLimit = EthereumTypes.ToEthHex(tx.GasLimit),
                    intrinsicGas = EthereumTypes.ToEthHex(intrinsicGas)
                },
                recommendation = "Increase gas limit above intrinsic minimum."
            });
        }

        if (!result.IsSuccess)
        {
            switch (result.Error)
            {
                case EvmError.OutOfGas:
                    reasons.Add(new
                    {
                        code = "out_of_gas_execution",
                        message = "Execution ran out of gas during opcode processing.",
                        evidence = new { gasUsed = EthereumTypes.ToEthHex(result.GasUsed) },
                        recommendation = "Increase gas limit or simplify execution path."
                    });
                    break;
                case EvmError.Revert:
                    reasons.Add(new
                    {
                        code = "execution_reverted",
                        message = "Contract reverted execution.",
                        evidence = new { returnData = EthereumTypes.ToEthHex(result.ReturnData) },
                        recommendation = "Inspect contract preconditions and calldata."
                    });
                    break;
                case EvmError.StaticModeViolation:
                    reasons.Add(new
                    {
                        code = "static_mode_violation",
                        message = "State-changing operation attempted in static context.",
                        evidence = new { error = result.Error.ToString() },
                        recommendation = "Remove state writes or avoid static call context."
                    });
                    break;
                case EvmError.NonceTooLow:
                case EvmError.NonceTooHigh:
                case EvmError.InsufficientFunds:
                    break;
                default:
                    reasons.Add(new
                    {
                        code = "execution_error",
                        message = "Transaction failed during execution.",
                        evidence = new { error = result.Error.ToString(), gasUsed = EthereumTypes.ToEthHex(result.GasUsed) },
                        recommendation = "Inspect trace and state to identify failing branch."
                    });
                    break;
            }
        }

        if (result.IsSuccess && reasons.Count == 0)
        {
            reasons.Add(new
            {
                code = "no_blocker_detected",
                message = "Simulation succeeded with current parameters.",
                evidence = new { gasUsed = EthereumTypes.ToEthHex(result.GasUsed) },
                recommendation = "No corrective action required."
            });
        }

        return reasons;
    }

    private static bool GetBooleanOption(JsonElement options, string name)
    {
        if (!options.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            return value.GetBoolean();
        throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Trace option '{name}' must be boolean");
    }

    private static int? GetIntegerOption(JsonElement options, string name)
    {
        if (!options.TryGetProperty(name, out var value)) return null;

        int parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsed))
        {
            if (parsed < 0)
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Trace option '{name}' must be >= 0");
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var s = value.GetString();
            if (string.IsNullOrWhiteSpace(s))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Trace option '{name}' must be a non-empty quantity");
            parsed = (int)EthereumTypes.FromEthHex(s);
            return parsed < 0
                ? throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Trace option '{name}' must be >= 0")
                : parsed;
        }

        throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Trace option '{name}' must be number or hex quantity");
    }

    private readonly record struct TraceOptions(bool DisableStack, bool DisableMemory, bool DisableStorage, int? Limit)
    {
        public static readonly TraceOptions Default = new(false, false, false, null);
    }

    private static ulong ParseQuantityParam(object? parameter, string parameterName)
    {
        var hex = parameter switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je when je.ValueKind == JsonValueKind.Number => "0x" + je.GetUInt64().ToString("x"),
            _ => parameter?.ToString()
        };

        if (string.IsNullOrWhiteSpace(hex))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid {parameterName}");

        return EthereumTypes.FromEthHex(hex);
    }

    private static BigInteger ParseHexQuantityElement(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var hex = element.GetString();
            if (string.IsNullOrWhiteSpace(hex))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}'");
            return EthereumTypes.FromEthHex(hex);
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out var n))
        {
            return new BigInteger(n);
        }

        throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}'");
    }

    private static BigInteger ParseOptionalQuantity(JsonElement txObj, string fieldName, BigInteger defaultValue)
    {
        if (!txObj.TryGetProperty(fieldName, out var field))
            return defaultValue;

        return ParseHexQuantityElement(field, fieldName);
    }

    private static Address? ParseOptionalAddress(JsonElement txObj, string fieldName)
    {
        if (!txObj.TryGetProperty(fieldName, out var field))
            return null;
        if (field.ValueKind != JsonValueKind.String)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}' address");

        var value = field.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!EthereumTypes.IsValidAddress(value))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}' address");

        return Address.FromHex(value);
    }

    private static byte[] ParseOptionalData(JsonElement txObj, string fieldName)
    {
        if (!txObj.TryGetProperty(fieldName, out var field))
            return Array.Empty<byte>();
        if (field.ValueKind != JsonValueKind.String)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}' field");

        var dataHex = field.GetString() ?? "0x";
        var clean = dataHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? dataHex[2..] : dataHex;
        if (clean.Length == 0) return Array.Empty<byte>();
        if (clean.Length % 2 != 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}' hex payload");

        try
        {
            return Convert.FromHexString(clean);
        }
        catch (FormatException)
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid '{fieldName}' hex payload");
        }
    }

    private BigInteger ResolveSendTransactionGasPrice(JsonElement txObj)
    {
        var hasGasPrice = txObj.TryGetProperty("gasPrice", out var gasPrice);
        var hasMaxFee = txObj.TryGetProperty("maxFeePerGas", out var maxFee);
        var hasMaxPriority = txObj.TryGetProperty("maxPriorityFeePerGas", out var maxPriority);

        if (hasGasPrice && (hasMaxFee || hasMaxPriority))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Cannot specify both gasPrice and EIP-1559 fee fields");

        if (hasMaxPriority && !hasMaxFee)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "maxPriorityFeePerGas requires maxFeePerGas");

        if (hasMaxFee)
        {
            var maxFeeValue = ParseHexQuantityElement(maxFee, "maxFeePerGas");
            if (hasMaxPriority)
            {
                var maxPriorityValue = ParseHexQuantityElement(maxPriority, "maxPriorityFeePerGas");
                if (maxPriorityValue > maxFeeValue)
                    throw new RpcException(JsonRpcErrorCodes.InvalidParams, "maxPriorityFeePerGas cannot exceed maxFeePerGas");
            }
            return maxFeeValue;
        }

        if (hasGasPrice)
            return ParseHexQuantityElement(gasPrice, "gasPrice");

        var configured = new BigInteger(_config.GasPrice);
        var baseFee = new BigInteger(_chainState.CurrentBlock.BaseFeePerGas);
        return configured < baseFee ? baseFee : configured;
    }

    private static byte[] ComputeUnsignedTransactionHash(Transaction tx)
    {
        var bytes = new List<byte>(256);
        bytes.AddRange(tx.From.Bytes);
        bytes.AddRange(tx.To?.Bytes ?? Array.Empty<byte>());
        bytes.AddRange(tx.Value.ToByteArray(isUnsigned: true, isBigEndian: true));
        bytes.AddRange(BitConverter.GetBytes(tx.Nonce));
        bytes.AddRange(BitConverter.GetBytes(tx.GasLimit));
        bytes.AddRange(tx.GasPrice.ToByteArray(isUnsigned: true, isBigEndian: true));
        bytes.AddRange(tx.Data);
        return CryptoUtils.Keccak256(bytes.ToArray());
    }

    private ulong ParseBlockTagParam(object? parameter)
    {
        var tag = parameter switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => parameter?.ToString()
        };

        if (string.IsNullOrWhiteSpace(tag))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid newestBlock");

        return tag switch
        {
            "latest" => _chainState.CurrentBlock.Number,
            "pending" => _chainState.CurrentBlock.Number,
            "earliest" => 0UL,
            _ when tag.StartsWith("0x", StringComparison.OrdinalIgnoreCase) => EthereumTypes.FromEthHex(tag),
            _ => throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid newestBlock")
        };
    }

    private ulong ParseFeeHistoryNewestBlock(object? parameter)
    {
        var newest = ParseBlockTagParam(parameter);
        var head = _chainState.CurrentBlock.Number;
        if (newest > head)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "newestBlock is greater than current head");
        return newest;
    }

    private static List<double> ParseRewardPercentiles(object? parameter)
    {
        var values = new List<double>();
        if (parameter == null) return values;

        if (parameter is not JsonElement je || je.ValueKind != JsonValueKind.Array)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "rewardPercentiles must be an array");

        double last = double.MinValue;
        foreach (var item in je.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var p))
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "rewardPercentiles must contain numbers");
            if (p < 0 || p > 100)
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "rewardPercentiles must be in range [0, 100]");
            if (p < last)
                throw new RpcException(JsonRpcErrorCodes.InvalidParams, "rewardPercentiles must be sorted ascending");
            last = p;
            values.Add(p);
        }

        return values;
    }

    private static BigInteger ComputeWeightedTipPercentile(
        IReadOnlyCollection<TransactionReceipt> receipts,
        double percentile,
        ulong baseFeePerGas)
    {
        if (receipts.Count == 0) return BigInteger.Zero;

        var rows = receipts
            .Select(r =>
            {
                var weight = r.GasUsed == 0 ? 1UL : r.GasUsed;
                var tip = r.EffectiveGasPrice - baseFeePerGas;
                if (tip < BigInteger.Zero) tip = BigInteger.Zero;
                return (tip, weight);
            })
            .OrderBy(x => x.tip)
            .ToList();

        var totalWeight = rows.Aggregate(0UL, (acc, x) => acc + x.weight);
        if (totalWeight == 0) return BigInteger.Zero;

        var threshold = (ulong)Math.Ceiling((percentile / 100d) * totalWeight);
        if (threshold == 0) threshold = 1;

        ulong cumulative = 0;
        foreach (var row in rows)
        {
            cumulative += row.weight;
            if (cumulative >= threshold) return row.tip;
        }

        return rows[^1].tip;
    }

    private static ulong ComputeNextBaseFee(Block parent)
    {
        // EIP-1559 next base fee approximation for dev-node reporting.
        if (parent.GasLimit == 0) return parent.BaseFeePerGas;
        var targetGas = parent.GasLimit / 2;
        if (targetGas == 0) return parent.BaseFeePerGas;
        if (parent.GasUsed == targetGas) return parent.BaseFeePerGas;

        var parentBase = new BigInteger(parent.BaseFeePerGas);
        BigInteger delta;
        if (parent.GasUsed > targetGas)
        {
            var gasDelta = new BigInteger(parent.GasUsed - targetGas);
            delta = (parentBase * gasDelta) / targetGas / 8;
            if (delta < BigInteger.One) delta = BigInteger.One;
            return (ulong)(parentBase + delta);
        }

        var gasDeficit = new BigInteger(targetGas - parent.GasUsed);
        delta = (parentBase * gasDeficit) / targetGas / 8;
        var next = parentBase - delta;
        if (next < BigInteger.Zero) next = BigInteger.Zero;
        return (ulong)next;
    }

    private void ValidateParams(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing address");
    }
}
