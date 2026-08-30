using System.CommandLine;
using System.Net.Http;
using Schlieren.Core.Forking;
using Schlieren.Core.Primitives;
using Schlieren.Guard;

namespace Schlieren.CLI.Commands;

public static class GuardCommand
{
    public static Command Build()
    {
        var tokenArg = new Argument<string>("token", "Ethereum ERC-20 address");
        var forkOption = new Option<string>(
            "--fork-url",
            () => "http://127.0.0.1:8545",
            "Pinned-state JSON-RPC (loopback own node preferred)");
        var blockOption = new Option<ulong?>(
            "--block",
            "Block number to pin. Default: latest from the fork URL");
        var outOption = new Option<string?>(
            "--out",
            "Directory to write the Workbench evidence bundle");
        var forkNameOption = new Option<string>(
            "--hardfork",
            () => "Prague",
            "EVM fork rules used for the scenario");

        var cmd = new Command("guard", "Schlieren Guard: pinned Router02 buy → approve → sell risk check")
        {
            tokenArg,
            forkOption,
            blockOption,
            outOption,
            forkNameOption
        };

        cmd.SetHandler(async (string token, string forkUrl, ulong? block, string? outDir, string hardfork) =>
        {
            Environment.ExitCode = await RunAsync(token, forkUrl, block, outDir, hardfork);
        }, tokenArg, forkOption, blockOption, outOption, forkNameOption);

        return cmd;
    }

    public static async Task<int> RunAsync(
        string tokenHex,
        string forkUrl,
        ulong? block,
        string? outDir,
        string hardfork)
    {
        Address token;
        try
        {
            token = Address.FromHex(tokenHex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Invalid token address: {ex.Message}");
            return 1;
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri(forkUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var fork = new ForkProvider(http, new BlockCache());
        var checker = new TokenRiskChecker(fork, hardfork);

        GuardReport report;
        try
        {
            report = await checker.EvaluateUniswapV2Async(token, block);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Guard scenario failed: {ex.Message}");
            Console.Error.WriteLine("Point --fork-url at a synced local Reth/Lighthouse RPC (default http://127.0.0.1:8545).");
            return 2;
        }

        Console.WriteLine(report.Verdict.Headline);
        Console.WriteLine(report.ToPlainLanguage());
        Console.WriteLine();
        Console.WriteLine($"Pinned {report.Pin.ForkName} @ block {report.Pin.BlockNumber} {report.Pin.BlockHash}");
        Console.WriteLine($"Buyer  {report.Buyer}");
        Console.WriteLine($"Token  {report.Token}");
        foreach (var step in report.Steps)
        {
            var mark = step.Succeeded ? "PASS" : "FAIL";
            Console.WriteLine($"  [{mark}] {step.Name,-14} gas={step.GasUsedHint()} error={step.Result.Error}");
        }

        var bundle = WorkbenchEvidence.WriteBundle(report);
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
            var stem = tokenHex.Trim();
            if (stem.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                stem = stem[2..];
            var path = Path.Combine(outDir, $"guard-{stem}.json");
            await File.WriteAllTextAsync(path, bundle);
            Console.WriteLine();
            Console.WriteLine($"Evidence: {path}");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(bundle);
        }

        return report.Verdict.Kind is GuardOutcomeKind.Inconclusive or GuardOutcomeKind.BuyFailed ? 3 : 0;
    }

    private static ulong GasUsedHint(this ScenarioStep step) => step.Result.GasUsed;
}
