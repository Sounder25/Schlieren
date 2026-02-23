using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace Scrutor.Core.Configuration;

/// <summary>
/// Handles loading and saving node configuration from TOML and JSON files.
/// Supports full state serialization as required for L3_CLI_READY flag.
/// </summary>
public sealed class ConfigurationLoader
{
    private readonly JsonSerializerOptions _jsonOptions;
    
    public ConfigurationLoader()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }
    
    // SECTION: Load Operations
    
    /// <summary>
    /// Loads configuration from a file, auto-detecting format by extension.
    /// Supported formats: .toml, .json
    /// </summary>
    /// <param name="filePath">Path to the configuration file</param>
    /// <returns>Loaded and validated NodeConfiguration</returns>
    /// <exception cref="FileNotFoundException">If file does not exist</exception>
    /// <exception cref="InvalidOperationException">If file format is unsupported</exception>
    public NodeConfiguration LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        NodeConfiguration config = extension switch
        {
            ".toml" => LoadFromToml(filePath),
            ".json" => LoadFromJson(filePath),
            _ => throw new InvalidOperationException(
                $"Unsupported configuration format: {extension}. Use .toml or .json")
        };
        
        config.ConfigSource = Path.GetFullPath(filePath);
        config.Validate();
        
        return config;
    }
    
    /// <summary>
    /// Loads configuration from a TOML file
    /// </summary>
    private NodeConfiguration LoadFromToml(string filePath)
    {
        var tomlContent = File.ReadAllText(filePath);
        var tomlModel = Toml.ToModel(tomlContent);
        
        return MapTomlToConfig(tomlModel);
    }
    
    /// <summary>
    /// Loads configuration from a JSON file
    /// </summary>
    private NodeConfiguration LoadFromJson(string filePath)
    {
        var jsonContent = File.ReadAllText(filePath);
        var config = JsonSerializer.Deserialize<NodeConfiguration>(jsonContent, _jsonOptions);
        
        if (config == null)
            throw new InvalidOperationException($"Failed to deserialize configuration from {filePath}");
        
        return config;
    }
    
    // SECTION: Save Operations
    
    /// <summary>
    /// Saves configuration to a file, using format based on extension.
    /// This enables full state serialization for L3_CLI_READY requirement.
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="filePath">Destination file path</param>
    public void SaveToFile(NodeConfiguration config, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        switch (extension)
        {
            case ".toml":
                SaveToToml(config, filePath);
                break;
            case ".json":
                SaveToJson(config, filePath);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported configuration format: {extension}. Use .toml or .json");
        }
    }
    
    /// <summary>
    /// Saves configuration to a TOML file
    /// </summary>
    private void SaveToToml(NodeConfiguration config, string filePath)
    {
        var tomlModel = MapConfigToToml(config);
        var tomlString = Toml.FromModel(tomlModel);
        File.WriteAllText(filePath, tomlString);
    }
    
    /// <summary>
    /// Saves configuration to a JSON file
    /// </summary>
    private void SaveToJson(NodeConfiguration config, string filePath)
    {
        var jsonString = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(filePath, jsonString);
    }
    
    // SECTION: TOML Mapping (Complex due to TOML structure)
    
    /// <summary>
    /// Maps TOML model to NodeConfiguration
    /// </summary>
    private NodeConfiguration MapTomlToConfig(TomlTable tomlModel)
    {
        var config = new NodeConfiguration();
        
        // Network & Server
        if (tomlModel.TryGetValue("host", out var hostValue))
        {
            string? hostString = hostValue?.ToString();
            if (!string.IsNullOrEmpty(hostString))
            {
                config.Host = hostString;
            }
        }
        if (tomlModel.TryGetValue("port", out var port))
            config.Port = Convert.ToInt32(port);
        if (tomlModel.TryGetValue("chain_id", out var chainId))
            config.ChainId = Convert.ToUInt64(chainId);
        if (tomlModel.TryGetValue("hardfork", out var hardforkValue))
        {
            string? hardforkString = hardforkValue?.ToString();
            if (!string.IsNullOrEmpty(hardforkString))
            {
                config.Hardfork = hardforkString;
            }
        }
        
        // Accounts
        if (tomlModel.TryGetValue("accounts", out var accounts))
            config.Accounts = Convert.ToInt32(accounts);
        if (tomlModel.TryGetValue("balance", out var balance))
            config.Balance = Convert.ToDecimal(balance);
        if (tomlModel.TryGetValue("mnemonic", out var mnemonicValue))
        {
            string? mnemonicString = mnemonicValue?.ToString();
            if (!string.IsNullOrEmpty(mnemonicString))
            {
                config.Mnemonic = mnemonicString;
            }
        }
        if (tomlModel.TryGetValue("derivation_path", out var derivationPathValue))
        {
            string? derivationPathString = derivationPathValue?.ToString();
            if (!string.IsNullOrEmpty(derivationPathString))
            {
                config.DerivationPath = derivationPathString;
            }
        }
        if (tomlModel.TryGetValue("auto_impersonate", out var autoImpersonate))
            config.AutoImpersonate = Convert.ToBoolean(autoImpersonate);
        
        // Mining
        if (tomlModel.TryGetValue("block_time", out var blockTime))
            config.BlockTime = blockTime != null ? Convert.ToInt32(blockTime) : null;
        if (tomlModel.TryGetValue("automine", out var automine))
            config.Automine = Convert.ToBoolean(automine);
        if (tomlModel.TryGetValue("gas_limit", out var gasLimit))
            config.GasLimit = Convert.ToUInt64(gasLimit);
        if (tomlModel.TryGetValue("base_fee", out var baseFee))
            config.BaseFee = Convert.ToUInt64(baseFee);
        if (tomlModel.TryGetValue("gas_price", out var gasPrice))
            config.GasPrice = Convert.ToUInt64(gasPrice);
        
        // Forking
        if (tomlModel.TryGetValue("fork_url", out var forkUrlValue))
        {
            string? forkUrlString = forkUrlValue?.ToString();
            if (!string.IsNullOrEmpty(forkUrlString))
            {
                config.ForkUrl = forkUrlString;
            }
        }
        if (tomlModel.TryGetValue("fork_block_number", out var forkBlockNumber))
            config.ForkBlockNumber = forkBlockNumber != null ? Convert.ToUInt64(forkBlockNumber) : null;
        if (tomlModel.TryGetValue("fork_chain_id", out var forkChainId))
            config.ForkChainId = forkChainId != null ? Convert.ToUInt64(forkChainId) : null;
        if (tomlModel.TryGetValue("fork_retry_backoff", out var forkRetryBackoff))
            config.ForkRetryBackoff = Convert.ToInt32(forkRetryBackoff);
        if (tomlModel.TryGetValue("fork_request_timeout", out var forkRequestTimeout))
            config.ForkRequestTimeout = Convert.ToInt32(forkRequestTimeout);
        if (tomlModel.TryGetValue("no_storage_caching", out var noStorageCaching))
            config.NoStorageCaching = Convert.ToBoolean(noStorageCaching);
        
        // State Management
        if (tomlModel.TryGetValue("init_state", out var initStateValue))
        {
            string? initStateString = initStateValue?.ToString();
            if (!string.IsNullOrEmpty(initStateString))
            {
                config.InitState = initStateString;
            }
        }
        if (tomlModel.TryGetValue("dump_state", out var dumpStateValue))
        {
            string? dumpStateString = dumpStateValue?.ToString();
            if (!string.IsNullOrEmpty(dumpStateString))
            {
                config.DumpState = dumpStateString;
            }
        }
        if (tomlModel.TryGetValue("prune_history", out var pruneHistory))
            config.PruneHistory = Convert.ToBoolean(pruneHistory);
        if (tomlModel.TryGetValue("state_interval", out var stateInterval))
            config.StateInterval = Convert.ToInt32(stateInterval);
        if (tomlModel.TryGetValue("max_blocks", out var maxBlocks))
            config.MaxBlocks = maxBlocks != null ? Convert.ToInt32(maxBlocks) : null;
        
        // Logging & Debugging
        if (tomlModel.TryGetValue("silent", out var silent))
            config.Silent = Convert.ToBoolean(silent);
        if (tomlModel.TryGetValue("transaction_order", out var transactionOrder))
            config.TransactionOrder = Convert.ToBoolean(transactionOrder);
        if (tomlModel.TryGetValue("step_tracing", out var stepTracing))
            config.StepTracing = Convert.ToBoolean(stepTracing);
        if (tomlModel.TryGetValue("log_file", out var logFileValue))
        {
            string? logFileString = logFileValue?.ToString();
            if (!string.IsNullOrEmpty(logFileString))
            {
                config.LogFile = logFileString;
            }
        }
        
        // CORS
        if (tomlModel.TryGetValue("cors_origins", out var corsOrigins) && corsOrigins is TomlArray corsArray)
            config.CorsOrigins = corsArray.Select(x => x?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (tomlModel.TryGetValue("allow_origin", out var allowOrigin))
            config.AllowOrigin = Convert.ToBoolean(allowOrigin);
        
        // Advanced
        if (tomlModel.TryGetValue("code_size_limit", out var codeSizeLimit))
            config.CodeSizeLimit = Convert.ToBoolean(codeSizeLimit);
        if (tomlModel.TryGetValue("disable_block_gas_limit", out var disableBlockGasLimit))
            config.DisableBlockGasLimit = Convert.ToBoolean(disableBlockGasLimit);
        if (tomlModel.TryGetValue("optimism", out var optimism))
            config.Optimism = Convert.ToBoolean(optimism);
        if (tomlModel.TryGetValue("genesis_timestamp", out var genesisTimestamp))
            config.GenesisTimestamp = genesisTimestamp != null ? Convert.ToUInt64(genesisTimestamp) : null;
        if (tomlModel.TryGetValue("strict_mode", out var strictMode))
            config.StrictMode = Convert.ToBoolean(strictMode);
        
        return config;
    }
    
    /// <summary>
    /// Maps NodeConfiguration to TOML model
    /// </summary>
    private TomlTable MapConfigToToml(NodeConfiguration config)
    {
        var table = new TomlTable();
        
        // Network & Server
        table["host"] = config.Host!;
        table["port"] = config.Port;
        table["chain_id"] = config.ChainId;
        table["hardfork"] = config.Hardfork!;
        
        // Accounts
        table["accounts"] = config.Accounts;
        table["balance"] = (double)config.Balance;
        if (config.Mnemonic != null)
            table["mnemonic"] = config.Mnemonic;
        table["derivation_path"] = config.DerivationPath!;
        table["auto_impersonate"] = config.AutoImpersonate;
        
        // Mining
        if (config.BlockTime.HasValue)
            table["block_time"] = config.BlockTime.Value;
        table["automine"] = config.Automine;
        table["gas_limit"] = config.GasLimit;
        table["base_fee"] = config.BaseFee;
        table["gas_price"] = config.GasPrice;
        
        // Forking
        if (config.ForkUrl != null)
            table["fork_url"] = config.ForkUrl;
        if (config.ForkBlockNumber.HasValue)
            table["fork_block_number"] = config.ForkBlockNumber.Value;
        if (config.ForkChainId.HasValue)
            table["fork_chain_id"] = config.ForkChainId.Value;
        table["fork_retry_backoff"] = config.ForkRetryBackoff;
        table["fork_request_timeout"] = config.ForkRequestTimeout;
        table["no_storage_caching"] = config.NoStorageCaching;
        
        // State Management
        if (config.InitState != null)
            table["init_state"] = config.InitState;
        if (config.DumpState != null)
            table["dump_state"] = config.DumpState;
        table["prune_history"] = config.PruneHistory;
        table["state_interval"] = config.StateInterval;
        if (config.MaxBlocks.HasValue)
            table["max_blocks"] = config.MaxBlocks.Value;
        
        // Logging & Debugging
        table["silent"] = config.Silent;
        table["transaction_order"] = config.TransactionOrder;
        table["step_tracing"] = config.StepTracing;
        if (config.LogFile != null)
            table["log_file"] = config.LogFile;
        
        // CORS
        if (config.CorsOrigins != null && config.CorsOrigins.Length > 0)
        {
            var corsArray = new TomlArray();
            foreach (var origin in config.CorsOrigins)
                corsArray.Add(origin);
            table["cors_origins"] = corsArray;
        }
        table["allow_origin"] = config.AllowOrigin;
        
        // Advanced
        table["code_size_limit"] = config.CodeSizeLimit;
        table["disable_block_gas_limit"] = config.DisableBlockGasLimit;
        table["optimism"] = config.Optimism;
        if (config.GenesisTimestamp.HasValue)
            table["genesis_timestamp"] = config.GenesisTimestamp.Value;
        table["strict_mode"] = config.StrictMode;
        
        return table;
    }
}
