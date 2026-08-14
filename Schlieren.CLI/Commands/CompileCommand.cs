using System.Text.Json;
using Schlieren.CLI.Workspace;

namespace Schlieren.CLI.Commands;

/// <summary>
/// schlieren compile
/// Downloads solc if needed, compiles every *.sol under contracts/, writes ABI+bytecode
/// JSON files to artifacts/.
/// </summary>
public static class CompileCommand
{
    // solc release download base URL — we pull a specific version per workspace config.
    private const string SolcBaseUrl = "https://github.com/ethereum/solc-bin/raw/gh-pages/windows-amd64/";
    private const string SolcListUrl = "https://binaries.soliditylang.org/windows-amd64/list.json";

    public static async Task<int> RunAsync(string? workdir)
    {
        var root = workdir ?? Directory.GetCurrentDirectory();
        var ws   = WorkspaceConfig.Discover(root);
        if (ws == null)
        {
            Console.Error.WriteLine("✗  No schlieren.config.json found (run 'schlieren init' first)");
            return 1;
        }

        var contractsDir = Path.Combine(ws.Root, ws.Paths.Contracts);
        var artifactsDir = Path.Combine(ws.Root, ws.Paths.Artifacts);
        Directory.CreateDirectory(artifactsDir);

        var solFiles = Directory.GetFiles(contractsDir, "*.sol", SearchOption.AllDirectories);
        if (solFiles.Length == 0)
        {
            Console.WriteLine("No .sol files found in contracts/. Nothing to compile.");
            return 0;
        }

        var solcPath = await EnsureSolcAsync(ws.Compiler.Version, ws.Root);
        if (solcPath == null) return 1;

        Console.WriteLine($"🔨 Compiling {solFiles.Length} Solidity file(s) with solc {ws.Compiler.Version}…");

        int compiled = 0, failed = 0;
        foreach (var sol in solFiles)
        {
            var rel = Path.GetRelativePath(ws.Root, sol);
            var ok  = await CompileOneAsync(solcPath, sol, artifactsDir, ws.Compiler, rel);
            if (ok) compiled++; else failed++;
        }

        Console.WriteLine();
        if (failed == 0)
            Console.WriteLine($"✓  {compiled} contract(s) compiled → {Path.GetRelativePath(ws.Root, artifactsDir)}/");
        else
            Console.Error.WriteLine($"✗  {failed} compilation(s) failed, {compiled} succeeded");

        return failed == 0 ? 0 : 1;
    }

    // ── Solc resolver ────────────────────────────────────────────────────────

    private static async Task<string?> EnsureSolcAsync(string version, string wsRoot)
    {
        var cacheDir = Path.Combine(wsRoot, ".schlieren-cache", "solc");
        Directory.CreateDirectory(cacheDir);

        var exeName = $"solc-v{version}.exe";
        var exePath = Path.Combine(cacheDir, exeName);
        if (File.Exists(exePath))
        {
            Console.WriteLine($"   solc {version} found in cache");
            return exePath;
        }

        Console.WriteLine($"   Downloading solc {version}…");
        try
        {
            // Resolve the exact filename from the list
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Add("User-Agent", "Schlieren/1.0");

            var listJson = await http.GetStringAsync(SolcListUrl);
            using var doc = JsonDocument.Parse(listJson);

            // List has a "releases" object: { "0.8.24": "solc-windows-amd64-v0.8.24+commit.e11b9ed9.exe", ... }
            string? fileName = null;
            if (doc.RootElement.TryGetProperty("releases", out var releases))
            {
                foreach (var rel in releases.EnumerateObject())
                {
                    if (rel.Name == version)
                    {
                        fileName = rel.Value.GetString();
                        break;
                    }
                }
            }

            if (fileName == null)
            {
                Console.Error.WriteLine($"✗  solc version {version} not found in release list");
                return null;
            }

            var downloadUrl = $"https://binaries.soliditylang.org/windows-amd64/{fileName}";
            Console.WriteLine($"   → {downloadUrl}");

            using var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            await using var fs = File.Create(exePath);
            await resp.Content.CopyToAsync(fs);

            Console.WriteLine($"   ✓ solc downloaded to {exePath}");
            return exePath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  Failed to download solc: {ex.Message}");
            return null;
        }
    }

    // ── Compile single file ──────────────────────────────────────────────────

    private static async Task<bool> CompileOneAsync(
        string solcPath, string solFile, string artifactsDir,
        CompilerConfig compiler, string displayName)
    {
        var optimizerArgs = compiler.Optimizer
            ? $"--optimize --optimize-runs {compiler.Runs}"
            : "";

        var args = $"--combined-json abi,bin,bin-runtime,hashes {optimizerArgs} \"{solFile}\"";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = solcPath,
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine($"  ✗  {displayName}");
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Console.Error.WriteLine($"      {line.TrimEnd()}");
            return false;
        }

        // Parse combined-json output and write one artifact per contract.
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (!doc.RootElement.TryGetProperty("contracts", out var contracts))
            {
                Console.Error.WriteLine($"  ✗  {displayName}: no 'contracts' key in solc output");
                return false;
            }

            int count = 0;
            foreach (var entry in contracts.EnumerateObject())
            {
                // entry.Name is "path/File.sol:ContractName"
                var contractName = entry.Name.Contains(':')
                    ? entry.Name.Split(':')[^1]
                    : entry.Name;

                var obj = entry.Value;

                // Build normalized artifact
                var artifact = new
                {
                    contractName,
                    sourcePath = solFile,
                    abi = JsonSerializer.Deserialize<JsonElement>(
                        obj.TryGetProperty("abi", out var abiEl) ? abiEl.GetRawText() : "[]"),
                    bytecode = "0x" + (obj.TryGetProperty("bin", out var binEl) ? (binEl.GetString() ?? "") : ""),
                    deployedBytecode = "0x" + (obj.TryGetProperty("bin-runtime", out var rtEl) ? (rtEl.GetString() ?? "") : ""),
                    compiler = new
                    {
                        version = compiler.Version,
                        optimizer = compiler.Optimizer,
                        runs = compiler.Runs
                    }
                };

                var artifactPath = Path.Combine(artifactsDir, $"{contractName}.json");
                await File.WriteAllTextAsync(artifactPath,
                    JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
                count++;
            }

            Console.WriteLine($"  ✓  {displayName} → {count} artifact(s)");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ✗  {displayName}: parse error — {ex.Message}");
            return false;
        }
    }
}
