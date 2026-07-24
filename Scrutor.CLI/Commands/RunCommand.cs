using Scrutor.CLI.Scripting;
using Scrutor.CLI.Workspace;
using Scrutor.Core.Configuration;
using Scrutor.RPC.Server;

namespace Scrutor.CLI.Commands;

/// <summary>
/// scrutor run &lt;script.csx&gt; [--rpc http://127.0.0.1:8545]
///
/// Runs a C# script against a Scrutor node. If --rpc is omitted the command
/// starts an embedded node using the workspace configuration (scrutor.config.json),
/// runs the script, then shuts the node down.
/// </summary>
public static class RunCommand
{
    public static async Task<int> RunAsync(string scriptPath, string? rpcUrl, string? workdir)
    {
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"✗  Script not found: {scriptPath}");
            return 1;
        }

        var root = workdir ?? Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? Directory.GetCurrentDirectory();
        var ws   = WorkspaceConfig.Discover(root);

        string resolvedRpc;
        NodeBootstrap? ownedServer = null;

        if (!string.IsNullOrEmpty(rpcUrl))
        {
            // Connect to externally-running node
            resolvedRpc = rpcUrl;
            Console.WriteLine($"→  Connecting to {rpcUrl}");
        }
        else
        {
            // Start embedded node
            var cfg = ws != null ? WorkspaceToNodeConfig(ws) : new NodeConfiguration();
            resolvedRpc = $"http://127.0.0.1:{cfg.Port}";
            Console.WriteLine($"→  Starting embedded node on {resolvedRpc}");

            ownedServer = NodeBootstrap.Start(cfg);
            // Brief settle — IOCP listener binds instantly but give OS a tick
            await Task.Delay(50);
        }

        await using var node = new ScrutorNode(
            resolvedRpc,
            ws != null ? Path.Combine(ws.Root, ws.Paths.Artifacts) : Path.Combine(root, "artifacts"));

        // Verify connectivity
        try
        {
            var chainId = await node.ChainIdAsync();
            Console.WriteLine($"   chain_id={chainId}  block={await node.BlockNumberAsync()}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  Cannot reach node at {resolvedRpc}: {ex.Message}");
            ownedServer?.Dispose();
            return 1;
        }

        var accounts = await node.GetAccountsAsync();
        Console.WriteLine($"   accounts={accounts.Length}");
        Console.WriteLine();

        try
        {
            Console.WriteLine($"▶  {Path.GetFileName(scriptPath)}");
            Console.WriteLine();
            var exitCode = await ScriptHost.RunScriptAsync(scriptPath, node, accounts);
            Console.WriteLine();
            return exitCode;
        }
        finally
        {
            ownedServer?.Dispose();
        }
    }

    internal static NodeConfiguration WorkspaceToNodeConfig(WorkspaceConfig ws) => new()
    {
        ChainId  = ws.Network.ChainId,
        Port     = ws.Network.Port,
        Host     = ws.Network.Host,
        GasLimit = ws.Network.GasLimit,
        BaseFee  = ws.Network.BaseFee,
        Accounts = ws.Accounts.Count,
        Balance  = decimal.Parse(ws.Accounts.Balance),
        Mnemonic = ws.Accounts.Mnemonic,
        Hardfork = ws.Network.Hardfork,
    };
}
