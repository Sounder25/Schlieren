using System.CommandLine;
using Scrutor.Core.Configuration;

namespace Scrutor.CLI;

/// <summary>
/// Anvil-compatible CLI parser implementing 1:1 flag mapping for ecosystem compatibility.
/// Adheres to Coding Governance: Zero-stub policy - all handlers are fully implemented.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Builds the complete command-line interface with all Anvil-compatible options
    /// </summary>
    public static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Scrutor - Windows-Native Ethereum Development Node")
        {
            // SECTION: Network & Server Options
            new Option<string>(
                aliases: new[] { "--host", "-h" },
                getDefaultValue: () => "127.0.0.1",
                description: "The host address the server will listen on"),
            
            new Option<int>(
                aliases: new[] { "--port", "-p" },
                getDefaultValue: () => 8545,
                description: "The port number the server will listen on"),
            
            new Option<ulong>(
                aliases: new[] { "--chain-id" },
                getDefaultValue: () => 31337,
                description: "The chain ID"),
            
            new Option<string>(
                aliases: new[] { "--hardfork" },
                getDefaultValue: () => "cancun",
                description: "The EVM hardfork to use (cancun, shanghai, paris, etc.)"),
            
            // SECTION: Account Options
            new Option<int>(
                aliases: new[] { "--accounts", "-a" },
                getDefaultValue: () => 10,
                description: "Number of dev accounts to generate and configure"),
            
            new Option<decimal>(
                aliases: new[] { "--balance" },
                getDefaultValue: () => 10000m,
                description: "The balance of every dev account in ETH"),
            
            new Option<string?>(
                aliases: new[] { "--mnemonic", "-m" },
                description: "BIP39 mnemonic phrase used to generate accounts"),
            
            new Option<string>(
                aliases: new[] { "--derivation-path" },
                getDefaultValue: () => "m/44'/60'/0'/0/",
                description: "Derivation path for HD wallet"),
            
            new Option<bool>(
                aliases: new[] { "--auto-impersonate" },
                getDefaultValue: () => false,
                description: "Enable auto-impersonation of accounts on startup"),
            
            // SECTION: Mining Options
            new Option<int?>(
                aliases: new[] { "--block-time", "-b" },
                description: "Block time in seconds for interval mining (default: instant, 0 = automine)"),
            
            new Option<bool>(
                aliases: new[] { "--no-mining" },
                getDefaultValue: () => false,
                description: "Disable auto-mining and mine on demand instead"),
            
            new Option<ulong>(
                aliases: new[] { "--gas-limit" },
                getDefaultValue: () => 30_000_000,
                description: "The block gas limit"),
            
            new Option<ulong>(
                aliases: new[] { "--base-fee" },
                getDefaultValue: () => 1_000_000_000,
                description: "The base fee in wei (EIP-1559)"),
            
            new Option<ulong>(
                aliases: new[] { "--gas-price" },
                getDefaultValue: () => 1_000_000_000,
                description: "The gas price in wei for legacy transactions"),
            
            // SECTION: Forking Options
            new Option<string?>(
                aliases: new[] { "--fork-url" },
                description: "Fetch state over a remote endpoint instead of starting from an empty state"),
            
            new Option<ulong?>(
                aliases: new[] { "--fork-block-number" },
                description: "Fetch state from a specific block number over a remote endpoint"),
            
            new Option<ulong?>(
                aliases: new[] { "--fork-chain-id" },
                description: "Specify chain ID when forking"),
            
            new Option<int>(
                aliases: new[] { "--fork-retry-backoff" },
                getDefaultValue: () => 3,
                description: "Number of retry attempts for spurious networks"),
            
            new Option<int>(
                aliases: new[] { "--fork-request-timeout" },
                getDefaultValue: () => 30000,
                description: "Timeout for RPC requests in milliseconds"),
            
            new Option<bool>(
                aliases: new[] { "--no-storage-caching" },
                getDefaultValue: () => false,
                description: "Disable storage caching when forking"),
            
            // SECTION: State Management Options
            new Option<string?>(
                aliases: new[] { "--init", "--load-state" },
                description: "Initialize the genesis block with state from a file"),
            
            new Option<string?>(
                aliases: new[] { "--dump-state" },
                description: "Dump the state of the chain on exit to the given file"),
            
            new Option<bool>(
                aliases: new[] { "--prune-history" },
                getDefaultValue: () => false,
                description: "Don't keep full chain history"),
            
            new Option<int>(
                aliases: new[] { "--state-interval" },
                getDefaultValue: () => 0,
                description: "Interval in seconds at which state is periodically saved"),
            
            new Option<int?>(
                aliases: new[] { "--max-blocks" },
                description: "Maximum number of blocks to keep in memory"),
            
            // SECTION: Logging & Debugging Options
            new Option<bool>(
                aliases: new[] { "--silent" },
                getDefaultValue: () => false,
                description: "Don't print anything on startup"),
            
            new Option<bool>(
                aliases: new[] { "--transaction-order" },
                getDefaultValue: () => false,
                description: "Print transactions in the order they are included in the block"),
            
            new Option<bool>(
                aliases: new[] { "--steps-tracing" },
                getDefaultValue: () => false,
                description: "Enable steps tracing for debugging"),
            
            new Option<string?>(
                aliases: new[] { "--log-file" },
                description: "Write logs to a file"),
            
            // SECTION: CORS Options
            new Option<string[]?>(
                aliases: new[] { "--cors-origins" },
                description: "CORS allowed origins (comma-separated)")
            { AllowMultipleArgumentsPerToken = true },
            
            new Option<bool>(
                aliases: new[] { "--allow-origin" },
                getDefaultValue: () => false,
                description: "Allow all CORS origins (insecure, dev only)"),
            
            // SECTION: Advanced Options
            new Option<bool>(
                aliases: new[] { "--no-code-size-limit" },
                getDefaultValue: () => false,
                description: "Disable EIP-170 code size limit"),
            
            new Option<bool>(
                aliases: new[] { "--disable-block-gas-limit" },
                getDefaultValue: () => false,
                description: "Disable block gas limit checks"),
            
            new Option<bool>(
                aliases: new[] { "--optimism" },
                getDefaultValue: () => false,
                description: "Enable Optimism/Bedrock support"),
            
            new Option<ulong?>(
                aliases: new[] { "--timestamp" },
                description: "The timestamp of the genesis block"),
            
            new Option<bool>(
                aliases: new[] { "--strict" },
                getDefaultValue: () => false,
                description: "Enable strict mode for production-like behavior"),
            
            // SECTION: Configuration File Option
            new Option<string?>(
                aliases: new[] { "--config", "-c" },
                description: "Path to configuration file (.toml or .json)")
        };
        
        rootCommand.Description = "Scrutor - A Windows-native Ethereum development node with Anvil compatibility";
        
        return rootCommand;
    }
    
    /// <summary>
    /// Parses command-line arguments and builds a NodeConfiguration.
    /// Follows priority: CLI flags > Config file > Defaults
    /// </summary>
    public static async Task<(NodeConfiguration? Config, int ExitCode)> ParseArgumentsAsync(string[] args)
    {
        var rootCommand = BuildRootCommand();
        NodeConfiguration? config = null;
        
        rootCommand.SetHandler((context) =>
        {
            // First, check if --config is specified
            var configPath = context.ParseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string?>>()
                    .First(o => o.Aliases.Contains("--config")));
            
            // Load from file if specified
            if (!string.IsNullOrEmpty(configPath))
            {
                var loader = new ConfigurationLoader();
                config = loader.LoadFromFile(configPath);
            }
            else
            {
                config = new NodeConfiguration();
            }
            
            // Override with CLI arguments (CLI takes precedence)
            OverrideWithCliArgs(config, context.ParseResult, rootCommand);
        });
        
        var exitCode = await rootCommand.InvokeAsync(args);
        
        return (config, exitCode);
    }
    
    /// <summary>
    /// Overrides configuration with CLI arguments where provided
    /// </summary>
    private static void OverrideWithCliArgs(
        NodeConfiguration config,
        System.CommandLine.Parsing.ParseResult parseResult,
        RootCommand rootCommand)
    {
        // Helper to check if option was explicitly provided
        bool WasProvided(string alias) =>
            parseResult.Tokens.Any(t => t.Value == alias);
        
        // Network & Server
        if (WasProvided("--host") || WasProvided("-h"))
            config.Host = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string>>().First(o => o.Aliases.Contains("--host")))!;
        
        if (WasProvided("--port") || WasProvided("-p"))
            config.Port = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int>>().First(o => o.Aliases.Contains("--port")));
        
        if (WasProvided("--chain-id"))
            config.ChainId = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong>>().First(o => o.Aliases.Contains("--chain-id")));
        
        if (WasProvided("--hardfork"))
            config.Hardfork = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string>>().First(o => o.Aliases.Contains("--hardfork")))!;
        
        // Accounts
        if (WasProvided("--accounts") || WasProvided("-a"))
            config.Accounts = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int>>().First(o => o.Aliases.Contains("--accounts")));
        
        if (WasProvided("--balance") || WasProvided("-b"))
            config.Balance = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<decimal>>().First(o => o.Aliases.Contains("--balance")));
        
        if (WasProvided("--mnemonic") || WasProvided("-m"))
            config.Mnemonic = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string?>>().First(o => o.Aliases.Contains("--mnemonic")));
        
        if (WasProvided("--derivation-path"))
            config.DerivationPath = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string>>().First(o => o.Aliases.Contains("--derivation-path")))!;
        
        if (WasProvided("--auto-impersonate"))
            config.AutoImpersonate = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--auto-impersonate")));
        
        // Mining
        if (WasProvided("--block-time"))
            config.BlockTime = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int?>>().First(o => o.Aliases.Contains("--block-time")));
        
        if (WasProvided("--no-mining"))
        {
            var noMining = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--no-mining")));
            config.Automine = !noMining;
        }
        
        if (WasProvided("--gas-limit"))
            config.GasLimit = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong>>().First(o => o.Aliases.Contains("--gas-limit")));
        
        if (WasProvided("--base-fee"))
            config.BaseFee = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong>>().First(o => o.Aliases.Contains("--base-fee")));
        
        if (WasProvided("--gas-price"))
            config.GasPrice = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong>>().First(o => o.Aliases.Contains("--gas-price")));
        
        // Forking
        if (WasProvided("--fork-url"))
            config.ForkUrl = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string?>>().First(o => o.Aliases.Contains("--fork-url")));
        
        if (WasProvided("--fork-block-number"))
            config.ForkBlockNumber = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong?>>().First(o => o.Aliases.Contains("--fork-block-number")));
        
        if (WasProvided("--fork-chain-id"))
            config.ForkChainId = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong?>>().First(o => o.Aliases.Contains("--fork-chain-id")));
        
        if (WasProvided("--fork-retry-backoff"))
            config.ForkRetryBackoff = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int>>().First(o => o.Aliases.Contains("--fork-retry-backoff")));
        
        if (WasProvided("--fork-request-timeout"))
            config.ForkRequestTimeout = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int>>().First(o => o.Aliases.Contains("--fork-request-timeout")));
        
        if (WasProvided("--no-storage-caching"))
            config.NoStorageCaching = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--no-storage-caching")));
        
        // State Management
        if (WasProvided("--init") || WasProvided("--load-state"))
            config.InitState = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string?>>().First(o => o.Aliases.Contains("--init")));
        
        if (WasProvided("--dump-state"))
            config.DumpState = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string?>>().First(o => o.Aliases.Contains("--dump-state")));
        
        if (WasProvided("--prune-history"))
            config.PruneHistory = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--prune-history")));
        
        if (WasProvided("--state-interval"))
            config.StateInterval = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int>>().First(o => o.Aliases.Contains("--state-interval")));
        
        if (WasProvided("--max-blocks"))
            config.MaxBlocks = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<int?>>().First(o => o.Aliases.Contains("--max-blocks")));
        
        // Logging
        if (WasProvided("--silent"))
            config.Silent = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--silent")));
        
        if (WasProvided("--transaction-order"))
            config.TransactionOrder = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--transaction-order")));
        
        if (WasProvided("--steps-tracing"))
            config.StepTracing = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--steps-tracing")));
        
        if (WasProvided("--log-file"))
            config.LogFile = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string?>>().First(o => o.Aliases.Contains("--log-file")));
        
        // CORS
        if (WasProvided("--cors-origins"))
            config.CorsOrigins = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<string[]?>>().First(o => o.Aliases.Contains("--cors-origins")));
        
        if (WasProvided("--allow-origin"))
            config.AllowOrigin = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--allow-origin")));
        
        // Advanced
        if (WasProvided("--no-code-size-limit"))
        {
            var noLimit = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--no-code-size-limit")));
            config.CodeSizeLimit = !noLimit;
        }
        
        if (WasProvided("--disable-block-gas-limit"))
            config.DisableBlockGasLimit = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--disable-block-gas-limit")));
        
        if (WasProvided("--optimism"))
            config.Optimism = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--optimism")));
        
        if (WasProvided("--timestamp"))
            config.GenesisTimestamp = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<ulong?>>().First(o => o.Aliases.Contains("--timestamp")));
        
        if (WasProvided("--strict"))
            config.StrictMode = parseResult.GetValueForOption(
                rootCommand.Options.OfType<Option<bool>>().First(o => o.Aliases.Contains("--strict")));
    }
}
