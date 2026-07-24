using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scrutor.CLI.Workspace;

/// <summary>
/// The scrutor.config.json workspace configuration — replaces hardhat.config.js.
/// Lives in the project root created by 'scrutor init'.
/// </summary>
public sealed class WorkspaceConfig
{
    public static readonly string FileName = "scrutor.config.json";

    [JsonPropertyName("network")]
    public NetworkConfig Network { get; set; } = new();

    [JsonPropertyName("accounts")]
    public AccountsConfig Accounts { get; set; } = new();

    [JsonPropertyName("paths")]
    public PathsConfig Paths { get; set; } = new();

    [JsonPropertyName("compiler")]
    public CompilerConfig Compiler { get; set; } = new();

    // ── Serialization ────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, _writeOptions);

    public static WorkspaceConfig? FromJson(string json) =>
        JsonSerializer.Deserialize<WorkspaceConfig>(json);

    // ── Load helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walk up from <paramref name="startDir"/> until scrutor.config.json is found.
    /// Returns null if none found at or above the directory.
    /// </summary>
    public static WorkspaceConfig? Discover(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
            {
                var json = File.ReadAllText(candidate);
                var cfg  = FromJson(json);
                if (cfg != null) cfg.Root = dir.FullName;
                return cfg;
            }
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Absolute path of the workspace root (set after load/discover).</summary>
    [JsonIgnore]
    public string Root { get; set; } = string.Empty;
}

public sealed class NetworkConfig
{
    [JsonPropertyName("chainId")]
    public ulong ChainId { get; set; } = 31337;

    [JsonPropertyName("hardfork")]
    public string Hardfork { get; set; } = "cancun";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8545;

    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("gasLimit")]
    public ulong GasLimit { get; set; } = 30_000_000;

    [JsonPropertyName("baseFee")]
    public ulong BaseFee { get; set; } = 1_000_000_000;
}

public sealed class AccountsConfig
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 10;

    [JsonPropertyName("balance")]
    public string Balance { get; set; } = "10000";

    [JsonPropertyName("mnemonic")]
    public string? Mnemonic { get; set; }
}

public sealed class PathsConfig
{
    [JsonPropertyName("contracts")]
    public string Contracts { get; set; } = "contracts";

    [JsonPropertyName("scripts")]
    public string Scripts { get; set; } = "scripts";

    [JsonPropertyName("tests")]
    public string Tests { get; set; } = "tests";

    [JsonPropertyName("artifacts")]
    public string Artifacts { get; set; } = "artifacts";
}

public sealed class CompilerConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.8.24";

    [JsonPropertyName("optimizer")]
    public bool Optimizer { get; set; } = true;

    [JsonPropertyName("runs")]
    public int Runs { get; set; } = 200;
}
