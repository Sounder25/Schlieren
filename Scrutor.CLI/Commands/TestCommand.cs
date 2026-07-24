using Scrutor.CLI.Scripting;
using Scrutor.CLI.Workspace;
namespace Scrutor.CLI.Commands;

/// <summary>
/// scrutor test [--pattern *.test.csx] [--rpc http://...] [--workdir path]
///
/// Discovers all *.test.csx files in the workspace tests/ directory, runs each
/// against an embedded (or external) Scrutor node, and prints green/red output.
/// Each test file gets a fresh state snapshot so tests are fully isolated.
/// Exit code: 0 = all green, 1 = any failure.
/// </summary>
public static class TestCommand
{
    public static async Task<int> RunAsync(string? pattern, string? rpcUrl, string? workdir)
    {
        var root = workdir ?? Directory.GetCurrentDirectory();
        var ws   = WorkspaceConfig.Discover(root);

        if (ws == null)
        {
            Console.Error.WriteLine("✗  No scrutor.config.json found (run 'scrutor init' first)");
            return 1;
        }

        var testsDir  = Path.Combine(ws.Root, ws.Paths.Tests);
        var glob      = pattern ?? "*.test.csx";
        var testFiles = Directory.GetFiles(testsDir, glob, SearchOption.AllDirectories)
                                 .OrderBy(f => f)
                                 .ToArray();

        if (testFiles.Length == 0)
        {
            Console.WriteLine($"No {glob} files found in {ws.Paths.Tests}/. Nothing to test.");
            return 0;
        }

        // Start embedded node (or connect externally)
        string resolvedRpc;
        NodeBootstrap? ownedServer = null;

        if (!string.IsNullOrEmpty(rpcUrl))
        {
            resolvedRpc = rpcUrl;
            Console.WriteLine($"→  Connecting to {rpcUrl}");
        }
        else
        {
            var cfg = RunCommand.WorkspaceToNodeConfig(ws);
            resolvedRpc = $"http://127.0.0.1:{cfg.Port}";
            Console.WriteLine($"→  Starting embedded node on {resolvedRpc}");
            ownedServer = NodeBootstrap.Start(cfg);
            await Task.Delay(50);
        }

        await using var node = new ScrutorNode(
            resolvedRpc,
            Path.Combine(ws.Root, ws.Paths.Artifacts));

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

        int totalPassed = 0, totalFailed = 0;

        try
        {
            foreach (var file in testFiles)
            {
                var rel = Path.GetRelativePath(ws.Root, file);
                Console.WriteLine($"  {rel}");

                // Snapshot state so each test file starts clean
                var snap = await node.SnapshotAsync();

                var (p, f) = await ScriptHost.RunTestScriptAsync(file, node, accounts);
                totalPassed += p;
                totalFailed += f;

                // Revert to pre-test state for the next file
                await node.RevertAsync(snap);

                Console.WriteLine();
            }
        }
        finally
        {
            ownedServer?.Dispose();
        }

        // Summary
        var total = totalPassed + totalFailed;
        if (totalFailed == 0)
            Console.WriteLine($"✓  {totalPassed}/{total} tests passed");
        else
            Console.WriteLine($"✗  {totalFailed}/{total} tests failed  ({totalPassed} passed)");

        return totalFailed == 0 ? 0 : 1;
    }
}
