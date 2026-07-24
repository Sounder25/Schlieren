using Scrutor.CLI.Workspace;

namespace Scrutor.CLI.Commands;

/// <summary>
/// scrutor init [directory]
/// Creates a new Scrutor workspace with scaffolding and scrutor.config.json.
/// </summary>
public static class InitCommand
{
    public static async Task<int> RunAsync(string? directory)
    {
        var target = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(directory);

        // Guard: already a workspace?
        var existing = Path.Combine(target, WorkspaceConfig.FileName);
        if (File.Exists(existing))
        {
            Console.Error.WriteLine($"✗  Already a Scrutor workspace: {existing}");
            return 1;
        }

        Directory.CreateDirectory(target);

        var cfg = new WorkspaceConfig();
        cfg.Root = target;

        // Write config
        await File.WriteAllTextAsync(existing, cfg.ToJson());

        // Scaffold directories
        var dirs = new[]
        {
            cfg.Paths.Contracts,
            cfg.Paths.Scripts,
            cfg.Paths.Tests,
            cfg.Paths.Artifacts,
        };

        foreach (var rel in dirs)
        {
            var abs = Path.Combine(target, rel);
            Directory.CreateDirectory(abs);
            // Keep empty dirs in git
            await File.WriteAllTextAsync(Path.Combine(abs, ".gitkeep"), "");
        }

        // Starter contract
        var counterPath = Path.Combine(target, cfg.Paths.Contracts, "Counter.sol");
        await File.WriteAllTextAsync(counterPath, StarterContracts.Counter);

        // Starter deploy script
        var deployPath = Path.Combine(target, cfg.Paths.Scripts, "deploy.csx");
        await File.WriteAllTextAsync(deployPath, StarterScripts.Deploy);

        // Starter test
        var testPath = Path.Combine(target, cfg.Paths.Tests, "Counter.test.csx");
        await File.WriteAllTextAsync(testPath, StarterScripts.CounterTest);

        // .gitignore
        await File.WriteAllTextAsync(Path.Combine(target, ".gitignore"), GitIgnore.Content);

        Console.WriteLine($"✓  Workspace created at {target}");
        Console.WriteLine();
        Console.WriteLine("  scrutor.config.json   — network, compiler, account settings");
        Console.WriteLine($"  {cfg.Paths.Contracts}/Counter.sol  — starter contract");
        Console.WriteLine($"  {cfg.Paths.Scripts}/deploy.csx     — starter deploy script");
        Console.WriteLine($"  {cfg.Paths.Tests}/Counter.test.csx — starter test");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  scrutor node          — start the local node");
        Console.WriteLine("  scrutor compile       — compile contracts");
        Console.WriteLine("  scrutor run scripts/deploy.csx");
        Console.WriteLine("  scrutor test");

        return 0;
    }
}

internal static class StarterContracts
{
    public const string Counter = """
// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

contract Counter {
    uint256 public count;

    event Incremented(address indexed by, uint256 newCount);

    function increment() external {
        count += 1;
        emit Incremented(msg.sender, count);
    }

    function reset() external {
        count = 0;
    }
}
""";
}

internal static class StarterScripts
{
    public const string Deploy = """
// scrutor deploy script — runs with: scrutor run scripts/deploy.csx
// 'node' and 'accounts' are injected by the script host.

var contract = await node.Deploy("Counter");
Console.WriteLine($"Counter deployed at {contract.Address}");
""";

    public const string CounterTest = """
// scrutor test file — runs with: scrutor test
// 'node', 'accounts', and test helpers are injected by the test host.

Test("Counter starts at zero", async () =>
{
    var counter = await node.Deploy("Counter");
    var count = await counter.Call<ulong>("count");
    Assert.Equal(0UL, count);
});

Test("Increment increases count", async () =>
{
    var counter = await node.Deploy("Counter");
    await counter.Send("increment", from: accounts[0]);
    var count = await counter.Call<ulong>("count");
    Assert.Equal(1UL, count);
});
""";
}

internal static class GitIgnore
{
    public const string Content = """
artifacts/
.scrutor-cache/
*.user
""";
}
