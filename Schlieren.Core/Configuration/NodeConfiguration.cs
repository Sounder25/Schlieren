using System.Net;

namespace Schlieren.Core.Configuration;

/// <summary>
/// Complete node configuration with full state serialization capability.
/// Maps 1:1 with Anvil CLI flags for ecosystem compatibility.
/// </summary>
public sealed class NodeConfiguration
{
    // SECTION: Network & Server Configuration
    
    /// <summary>Host address to bind the RPC server to (default: "127.0.0.1")</summary>
    public string Host { get; set; } = "127.0.0.1";
    
    /// <summary>Port to bind the RPC server to (default: 8545)</summary>
    public int Port { get; set; } = 8545;
    
    /// <summary>Chain ID for the network (default: 31337 - Anvil default)</summary>
    public ulong ChainId { get; set; } = 31337;
    
    /// <summary>Hardfork to use (cancun, shanghai, etc.)</summary>
    public string Hardfork { get; set; } = "cancun";
    
    // SECTION: Account Configuration
    
    /// <summary>Number of dev accounts to generate (default: 10)</summary>
    public int Accounts { get; set; } = 10;
    
    /// <summary>Default balance for generated accounts in ETH (default: 10000)</summary>
    public decimal Balance { get; set; } = 10000m;
    
    /// <summary>BIP39 mnemonic phrase for deterministic account generation</summary>
    public string? Mnemonic { get; set; }
    
    /// <summary>Derivation path for HD wallet (default: "m/44'/60'/0'/0/")</summary>
    public string DerivationPath { get; set; } = "m/44'/60'/0'/0/";
    
    /// <summary>Enable automatic account impersonation</summary>
    public bool AutoImpersonate { get; set; } = false;
    
    // SECTION: Mining Configuration
    
    /// <summary>Block time in seconds (0 = instant mining, null = manual mining)</summary>
    public int? BlockTime { get; set; } = 0;
    
    /// <summary>Enable auto-mining for every transaction</summary>
    public bool Automine { get; set; } = true;
    
    /// <summary>Gas limit per block (default: 30,000,000)</summary>
    public ulong GasLimit { get; set; } = 30_000_000;
    
    /// <summary>Base fee per gas (EIP-1559)</summary>
    public ulong BaseFee { get; set; } = 1_000_000_000; // 1 gwei
    
    /// <summary>Gas price for legacy transactions</summary>
    public ulong GasPrice { get; set; } = 1_000_000_000; // 1 gwei
    
    // SECTION: Forking Configuration
    
    /// <summary>RPC URL to fork from (e.g., "https://eth.llamarpc.com")</summary>
    public string? ForkUrl { get; set; }
    
    /// <summary>Block number to fork from (null = latest)</summary>
    public ulong? ForkBlockNumber { get; set; }
    
    /// <summary>Chain ID of the forked network (inherits from fork if not set)</summary>
    public ulong? ForkChainId { get; set; }
    
    /// <summary>Number of retry attempts for fork RPC calls</summary>
    public int ForkRetryBackoff { get; set; } = 3;
    
    /// <summary>Timeout for RPC requests in milliseconds</summary>
    public int ForkRequestTimeout { get; set; } = 30000;

    /// <summary>Maximum number of blocks to scan in eth_getLogs (default: 10,000)</summary>
    public int MaxBlocksScanned { get; set; } = 10000;

    /// <summary>Maximum number of logs to return in eth_getLogs (default: 10,000)</summary>
    public int MaxLogsReturned { get; set; } = 10000;
    
    /// <summary>Enable offline mode with cached state</summary>
    public bool NoStorageCaching { get; set; } = false;
    
    // SECTION: State Management
    
    /// <summary>Path to load initial state from</summary>
    public string? InitState { get; set; }
    
    /// <summary>Path to dump state on shutdown</summary>
    public string? DumpState { get; set; }
    
    /// <summary>Enable state pruning to limit memory usage</summary>
    public bool PruneHistory { get; set; } = false;
    
    /// <summary>Number of blocks to retain in history (0 = unlimited)</summary>
    public int StateInterval { get; set; } = 0;
    
    /// <summary>Maximum number of blocks in memory</summary>
    public int? MaxBlocks { get; set; }
    
    // SECTION: Logging & Debugging
    
    /// <summary>Disable logging output</summary>
    public bool Silent { get; set; } = false;
    
    /// <summary>Enable transaction order logging</summary>
    public bool TransactionOrder { get; set; } = false;
    
    /// <summary>Enable detailed step tracing</summary>
    public bool StepTracing { get; set; } = false;
    
    /// <summary>Logging output file path</summary>
    public string? LogFile { get; set; }
    
    // SECTION: CORS & Security
    
    /// <summary>Enable CORS with allowed origins</summary>
    public string[]? CorsOrigins { get; set; }
    
    /// <summary>Allow all CORS origins (insecure, development only)</summary>
    public bool AllowOrigin { get; set; } = false;
    
    // SECTION: Advanced Features
    
    /// <summary>Enable code size validation (EIP-170)</summary>
    public bool CodeSizeLimit { get; set; } = true;
    
    /// <summary>Disable block gas limit enforcement</summary>
    public bool DisableBlockGasLimit { get; set; } = false;
    
    /// <summary>Enable optimism/bedrock features</summary>
    public bool Optimism { get; set; } = false;
    
    /// <summary>Timestamp for the genesis block</summary>
    public ulong? GenesisTimestamp { get; set; }
    
    /// <summary>Enable strict mode for production-like behavior</summary>
    public bool StrictMode { get; set; } = false;
    
    /// <summary>Configuration source file path (for diagnostics)</summary>
    public string? ConfigSource { get; set; }
    
    // SECTION: Validation
    
    /// <summary>
    /// Validates the configuration for consistency and correctness.
    /// Throws ArgumentException if validation fails.
    /// </summary>
    public void Validate()
    {
        if (Port < 1 || Port > 65535)
            throw new ArgumentException($"Port must be between 1 and 65535, got {Port}");
        
        if (Accounts < 0 || Accounts > 1000)
            throw new ArgumentException($"Accounts must be between 0 and 1000, got {Accounts}");
        
        if (Balance < 0)
            throw new ArgumentException($"Balance cannot be negative, got {Balance}");
        
        if (BlockTime.HasValue && BlockTime.Value < 0)
            throw new ArgumentException($"BlockTime cannot be negative, got {BlockTime}");
        
        if (GasLimit == 0)
            throw new ArgumentException("GasLimit cannot be zero");
        
        if (!string.IsNullOrEmpty(ForkUrl))
        {
            if (!Uri.TryCreate(ForkUrl, UriKind.Absolute, out var uri) || 
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new ArgumentException($"ForkUrl must be a valid HTTP/HTTPS URL, got '{ForkUrl}'");
            }
            // Allow loopback/localhost for local fork scenarios (e.g. fork another local node).
            // Only block RFC-1918 private IPs on non-loopback hosts to prevent accidental SSRF
            // against internal networks from production contexts.
            if (!uri.IsLoopback &&
                (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6) &&
                IsPrivateIpAddress(uri.Host))
            {
                throw new ArgumentException($"ForkUrl cannot point to a private network IP address: '{ForkUrl}'");
            }
        }
        
        if (!IPAddress.TryParse(Host, out _) && Host != "localhost")
        {
            throw new ArgumentException($"Host must be a valid IP address or 'localhost', got '{Host}'");
        }
    }
    
    /// <summary>
    /// Checks if an IP address falls within private network ranges.
    /// </summary>
    private static bool IsPrivateIpAddress(string ipAddress)
    {
        var ip = IPAddress.Parse(ipAddress);
        var octets = ip.GetAddressBytes();

        // IPv4 only for now
        if (octets.Length == 4)
        {
            // 10.0.0.0/8
            if (octets[0] == 10) return true;
            // 172.16.0.0/12
            if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) return true;
            // 192.168.0.0/16
            if (octets[0] == 192 && octets[1] == 168) return true;
            // 127.0.0.0/8 (loopback) - already handled by Uri.IsLoopback but good for completeness
            if (octets[0] == 127) return true;
        }
        // TODO: Handle IPv6 private ranges if necessary

        return false;
    }
    
    /// <summary>
    /// Creates a deep clone of this configuration
    /// </summary>
    public NodeConfiguration Clone()
    {
        return new NodeConfiguration
        {
            Host = Host,
            Port = Port,
            ChainId = ChainId,
            Hardfork = Hardfork,
            Accounts = Accounts,
            Balance = Balance,
            Mnemonic = Mnemonic,
            DerivationPath = DerivationPath,
            AutoImpersonate = AutoImpersonate,
            BlockTime = BlockTime,
            Automine = Automine,
            GasLimit = GasLimit,
            BaseFee = BaseFee,
            GasPrice = GasPrice,
            ForkUrl = ForkUrl,
            ForkBlockNumber = ForkBlockNumber,
            ForkChainId = ForkChainId,
            ForkRetryBackoff = ForkRetryBackoff,
            ForkRequestTimeout = ForkRequestTimeout,
            NoStorageCaching = NoStorageCaching,
            InitState = InitState,
            DumpState = DumpState,
            PruneHistory = PruneHistory,
            StateInterval = StateInterval,
            MaxBlocks = MaxBlocks,
            Silent = Silent,
            TransactionOrder = TransactionOrder,
            StepTracing = StepTracing,
            LogFile = LogFile,
            CorsOrigins = CorsOrigins?.ToArray(),
            AllowOrigin = AllowOrigin,
            CodeSizeLimit = CodeSizeLimit,
            DisableBlockGasLimit = DisableBlockGasLimit,
            Optimism = Optimism,
            GenesisTimestamp = GenesisTimestamp,
            StrictMode = StrictMode,
            ConfigSource = ConfigSource
        };
    }
}
