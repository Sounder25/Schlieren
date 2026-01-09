using System.Numerics;
using System.Text.Json;
using Scrutor.RPC.Models;
using Scrutor.Core.Models;
using Scrutor.Core.State;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using Scrutor.Core.Configuration;
using Scrutor.RPC;

namespace Scrutor.RPC.Handlers;

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
        if (!callObj.HasValue)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid call object");

        var tx = new Transaction
        {
            From = callObj.Value.TryGetProperty("from", out var fromProp) ? Address.FromHex(fromProp.GetString()!) : Address.Zero,
            To = callObj.Value.TryGetProperty("to", out var toProp) ? Address.FromHex(toProp.GetString()!) : null,
            Data = callObj.Value.TryGetProperty("data", out var dataProp) ? Convert.FromHexString(dataProp.GetString()![2..]) : Array.Empty<byte>(),
            GasLimit = 30_000_000,
            GasPrice = 0
        };

        var block = _chainState.CurrentBlock;
        var blockContext = new BlockContext
        {
            Number = block.Number,
            Timestamp = block.Timestamp,
            GasLimit = block.GasLimit,
            Difficulty = block.Difficulty,
            Coinbase = string.IsNullOrEmpty(block.Miner) ? Address.Zero : Address.FromHex(block.Miner)
        };

        var result = await _stateTransition.ApplyTransactionAsync(tx, _globalState, blockContext, commit: false, ct: ct);
        
        return EthereumTypes.ToEthHex(result.ReturnData);
    }

    public string HandleSendRawTransaction(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing raw transaction data");

        var rawHex = parameters[0]?.ToString() ?? string.Empty;
        var rawBytes = Convert.FromHexString(rawHex.StartsWith("0x") ? rawHex[2..] : rawHex);

        try
        {
            var tx = Transaction.FromRaw(rawBytes);
            _mempool.Add(tx);
            return EthereumTypes.ToEthHex(tx.Hash);
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
        var key = EthereumTypes.FromEthHex(parameters[1]?.ToString() ?? "0x0");
        var value = await _globalState.GetStorageAtAsync(address, key, ct);
        return EthereumTypes.ToEthHex(value);
    }

    public Block? HandleGetBlockByNumber(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing block number");

        var tag = parameters[0]?.ToString();
        if (tag == "latest") return _chainState.CurrentBlock;
        if (tag == "earliest") return _chainState.BlockStore.GetBlockByNumber(0);
        if (tag == "pending") return _chainState.CurrentBlock;

        if (tag != null && tag.StartsWith("0x"))
        {
            var number = EthereumTypes.FromEthHex(tag);
            return _chainState.BlockStore.GetBlockByNumber(number);
        }

        return null;
    }

    public Transaction? HandleGetTransactionByHash(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction hash");

        var hashHex = parameters[0]?.ToString();
        if (hashHex == null) return null;

        for (ulong i = _chainState.CurrentBlock.Number; i >= 0; i--)
        {
            var block = _chainState.BlockStore.GetBlockByNumber(i);
            if (block == null) break;

            var tx = block.Transactions.FirstOrDefault(t => EthereumTypes.ToEthHex(t.Hash) == hashHex);
            if (tx != null) return tx;
            if (i == 0) break;
        }

        return null;
    }

    public TransactionReceipt? HandleGetTransactionReceipt(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction hash");

        var hashHex = parameters[0]?.ToString();
        if (hashHex == null) return null;

        return _chainState.BlockStore.GetReceiptByHash(hashHex);
    }

    public List<TransactionLog> HandleGetLogs(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing filter object");

        var filter = parameters[0] as JsonElement?;
        if (!filter.HasValue)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid filter object");

        // Parse filter fields
        ulong fromBlock = ParseBlockTag(filter.Value, "fromBlock", 0);
        ulong toBlock = ParseBlockTag(filter.Value, "toBlock", _chainState.CurrentBlock.Number);

        if (toBlock < fromBlock)
            return new List<TransactionLog>();

        if ((toBlock - fromBlock) > (ulong)_config.MaxBlocksScanned)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Block range too large (max {_config.MaxBlocksScanned})");

        HashSet<string>? addressSet = null;
        if (filter.Value.TryGetProperty("address", out var addrProp))
        {
            addressSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (addrProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in addrProp.EnumerateArray())
                {
                    if (a.ValueKind == JsonValueKind.String && a.GetString() is string s) addressSet.Add(s);
                }
            }
            else if (addrProp.ValueKind == JsonValueKind.String && addrProp.GetString() is string s)
            {
                addressSet.Add(s);
            }
        }

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

        var results = new List<TransactionLog>();
        for (ulong i = fromBlock; i <= toBlock; i++)
        {
            foreach (var receipt in _chainState.BlockStore.GetReceiptsByBlockNumber(i))
            {
                foreach (var log in receipt.Logs)
                {
                    if (addressSet != null && !addressSet.Contains(log.Address)) continue;
                    if (topicFilters != null && !TopicsMatch(topicFilters, log.Topics)) continue;

                    results.Add(log);
                    if (results.Count >= _config.MaxLogsReturned) return results;
                }
            }
        }

        return results.OrderBy(l => l.BlockNumber).ThenBy(l => l.LogIndex).ToList();
    }

    private void ValidateTopic(string topic)
    {
        if (topic.Length != 66 || !topic.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid topic: {topic} (must be 32-byte hex string)");
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
        var balance = EthereumTypes.FromEthHex(parameters[1]?.ToString() ?? "0x0");
        _globalState.SetBalance(address, balance);
        return true;
    }

    public bool HandleAnvilSetNonce(object[] parameters)
    {
        if (parameters.Length < 2) throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing parameters");
        var address = Address.FromHex(parameters[0]?.ToString() ?? string.Empty);
        var nonce = (ulong)EthereumTypes.FromEthHex(parameters[1]?.ToString() ?? "0x0");
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
        var key = EthereumTypes.FromEthHex(parameters[1]?.ToString() ?? "0x0");
        var value = EthereumTypes.FromEthHex(parameters[2]?.ToString() ?? "0x0");
        _globalState.SetStorageAt(address, key, value);
        return true;
    }

    public async Task<string> HandleSendTransaction(object[] parameters, CancellationToken ct = default)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing transaction object");

        var txObj = parameters[0] as JsonElement?;
        if (!txObj.HasValue)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Invalid transaction object");

        if (!txObj.Value.TryGetProperty("from", out var fromProp) || string.IsNullOrEmpty(fromProp.GetString()))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Sender 'from' address is required for eth_sendTransaction");

        var fromAddress = Address.FromHex(fromProp.GetString()!);
        
        // 1. Check if the sender is impersonated
        if (!_impersonation.IsImpersonated(fromAddress))
        {
            throw new RpcException(-32000, $"Sender account {fromAddress} is not impersonated or unlocked. " +
                "Use anvil_impersonateAccount to unlock it for unsigned transactions.");
        }

        // 2. Build the transaction
        var tx = new Transaction
        {
            From = fromAddress,
            To = txObj.Value.TryGetProperty("to", out var toProp) ? Address.FromHex(toProp.GetString()!) : null,
            Value = txObj.Value.TryGetProperty("value", out var valProp) ? EthereumTypes.FromEthHex(valProp.GetString()!) : 0,
            Data = txObj.Value.TryGetProperty("data", out var dataProp) ? Convert.FromHexString(dataProp.GetString()![2..]) : Array.Empty<byte>(),
            GasLimit = txObj.Value.TryGetProperty("gas", out var gasProp) ? (ulong)EthereumTypes.FromEthHex(gasProp.GetString()!) : 30_000_000,
            GasPrice = txObj.Value.TryGetProperty("gasPrice", out var priceProp) ? EthereumTypes.FromEthHex(priceProp.GetString()!) : _config.GasPrice,
            Authorization = TransactionAuthorization.Impersonated
        };

        // 3. Set Nonce (if not provided)
        if (txObj.Value.TryGetProperty("nonce", out var nonceProp))
        {
            tx.Nonce = (ulong)EthereumTypes.FromEthHex(nonceProp.GetString()!);
        }
        else
        {
            var baseNonce = await _globalState.GetNonceAsync(fromAddress, ct);
            tx.Nonce = await _mempool.ReserveNonceAsync(fromAddress, baseNonce);
        }

        // 4. Hash (even though unsigned, we need a unique identifier)
        // We'll use a local hash variant for unsigned txs
        tx.Hash = tx.Nonce % 2 == 0 ? CryptoUtils.Keccak256(tx.Data) : CryptoUtils.Keccak256(BitConverter.GetBytes(tx.Nonce)); 
        // Actually, let's just use Keccak256 of the fields for a consistent hash
        var hashSource = new List<byte>();
        hashSource.AddRange(tx.From.Bytes);
        hashSource.AddRange(BitConverter.GetBytes(tx.Nonce));
        hashSource.AddRange(tx.Data);
        tx.Hash = CryptoUtils.Keccak256(hashSource.ToArray());

        // 5. Add to mempool
        _mempool.Add(tx);

        return EthereumTypes.ToEthHex(tx.Hash);
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

    private void ValidateParams(object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing address");
    }
}
