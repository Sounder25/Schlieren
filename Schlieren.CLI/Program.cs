using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schlieren.CLI.Commands;
using Schlieren.Core.Configuration;
using Schlieren.Core.DependencyInjection;
using Schlieren.Core.Forking;
using Schlieren.RPC;
using Schlieren.RPC.DependencyInjection;

namespace Schlieren.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var root = BuildRootCommand();
        return await root.InvokeAsync(args);
    }

    // ── Root ─────────────────────────────────────────────────────────────────

    static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Schlieren — Windows-native Ethereum development platform");

        root.AddCommand(BuildNodeCommand());
        root.AddCommand(BuildInitCommand());
        root.AddCommand(BuildCompileCommand());
        root.AddCommand(BuildRunCommand());
        root.AddCommand(BuildTestCommand());
        root.AddCommand(BuildTraceCommand());
        root.AddCommand(HarvestCommand.Build());
        root.AddCommand(GuardCommand.Build());

        // Default: no subcommand → show help
        root.SetHandler(() =>
        {
            Console.WriteLine("Usage: schlieren <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  node      Start the local Ethereum node");
            Console.WriteLine("  init      Create a new Schlieren workspace");
            Console.WriteLine("  compile   Compile Solidity contracts in the workspace");
            Console.WriteLine("  run       Run a .csx script against the node");
            Console.WriteLine("  test      Run *.test.csx test files");
            Console.WriteLine("  trace     Gas causality tree for a transaction");
            Console.WriteLine("  harvest   Harvest certification pipeline");
            Console.WriteLine();
            Console.WriteLine("Run 'schlieren <command> --help' for details.");
        });

        return root;
    }

    // ── 'schlieren node' ───────────────────────────────────────────────────────

    static Command BuildNodeCommand()
    {
        var cmd = CommandLineParser.BuildNodeCommand();

        cmd.SetHandler(async (context) =>
        {
            var (config, exitCode) = await CommandLineParser.ParseNodeContextAsync(context);
            if (config == null || exitCode != 0)
            {
                context.ExitCode = exitCode;
                return;
            }

            try { config.Validate(); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Configuration Error: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            if (!config.Silent) PrintBanner(config);

            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    if (!config.Silent)
                        Console.WriteLine("📦 Registering services...");

                    services.AddSingleton(config);
                    services.AddSchlierenCore();

                    if (!string.IsNullOrEmpty(config.ForkUrl))
                    {
                        services.AddSingleton<IBlockCache, BlockCache>();
                        services.AddHttpClient<IForkProvider, ForkProvider>(client =>
                        {
                            client.BaseAddress = new Uri(config.ForkUrl);
                            client.Timeout = TimeSpan.FromMilliseconds(config.ForkRequestTimeout);
                        });
                    }

                    services.AddSchlierenRpc();
                })
                .Build();

            if (!config.Silent) DisplayServiceInfo(host, config);

            if (!config.Silent)
            {
                Console.WriteLine();
                Console.WriteLine("🚀 Starting Schlieren Node...");
                Console.WriteLine($"   Chain ID:  {config.ChainId}");
                Console.WriteLine($"   Hardfork:  {config.Hardfork}");
                Console.WriteLine($"   HTTP RPC:  http://{config.Host}:{config.Port}");

                if (!string.IsNullOrEmpty(config.ForkUrl))
                {
                    Console.WriteLine($"   Fork URL:  {config.ForkUrl}");
                    if (config.ForkBlockNumber.HasValue)
                        Console.WriteLine($"   Fork Block: {config.ForkBlockNumber.Value}");
                }

                Console.WriteLine();
                Console.WriteLine("Press Ctrl+C to stop");
            }

            await host.RunAsync();
        });

        return cmd;
    }

    // ── 'schlieren compile' ────────────────────────────────────────────────────

    static Command BuildCompileCommand()
    {
        var workdirOpt = new Option<string?>(
            aliases: new[] { "--workdir" },
            description: "Workspace directory (default: current directory)");

        var cmd = new Command("compile", "Compile Solidity contracts in the workspace")
        {
            workdirOpt
        };

        cmd.SetHandler(async (workdir) =>
        {
            var exitCode = await CompileCommand.RunAsync(workdir);
            Environment.ExitCode = exitCode;
        }, workdirOpt);

        return cmd;
    }

    // ── 'schlieren init' ───────────────────────────────────────────────────────

    static Command BuildInitCommand()
    {
        var dirArg = new Argument<string?>(
            name: "directory",
            description: "Directory to initialize (default: current directory)",
            getDefaultValue: () => null);

        var cmd = new Command("init", "Create a new Schlieren workspace")
        {
            dirArg
        };

        cmd.SetHandler(async (dir) =>
        {
            var exitCode = await InitCommand.RunAsync(dir);
            Environment.ExitCode = exitCode;
        }, dirArg);

        return cmd;
    }

    // ── 'schlieren run' ────────────────────────────────────────────────────────

    static Command BuildRunCommand()
    {
        var scriptArg = new Argument<string>(
            name: "script",
            description: "Path to the .csx script to run");

        var rpcOpt = new Option<string?>(
            aliases: new[] { "--rpc" },
            description: "RPC URL of an already-running node (starts embedded node if omitted)");

        var workdirOpt = new Option<string?>(
            aliases: new[] { "--workdir" },
            description: "Workspace root directory (default: script directory)");

        var cmd = new Command("run", "Run a C# script (.csx) against the Schlieren node")
        {
            scriptArg, rpcOpt, workdirOpt
        };

        cmd.SetHandler(async (script, rpc, workdir) =>
        {
            var exitCode = await RunCommand.RunAsync(script, rpc, workdir);
            Environment.ExitCode = exitCode;
        }, scriptArg, rpcOpt, workdirOpt);

        return cmd;
    }

    // ── 'schlieren test' ───────────────────────────────────────────────────────

    static Command BuildTestCommand()
    {
        var patternOpt = new Option<string?>(
            aliases: new[] { "--pattern" },
            description: "Glob pattern for test files (default: *.test.csx)");

        var rpcOpt = new Option<string?>(
            aliases: new[] { "--rpc" },
            description: "RPC URL of an already-running node (starts embedded node if omitted)");

        var workdirOpt = new Option<string?>(
            aliases: new[] { "--workdir" },
            description: "Workspace root directory (default: current directory)");

        var cmd = new Command("test", "Run *.test.csx test files against the Schlieren node")
        {
            patternOpt, rpcOpt, workdirOpt
        };

        cmd.SetHandler(async (pattern, rpc, workdir) =>
        {
            var exitCode = await TestCommand.RunAsync(pattern, rpc, workdir);
            Environment.ExitCode = exitCode;
        }, patternOpt, rpcOpt, workdirOpt);

        return cmd;
    }

    // ── 'schlieren trace' ──────────────────────────────────────────────────────

    static Command BuildTraceCommand()
    {
        var txHashArg = new Argument<string>(
            name: "txhash",
            description: "Transaction hash to trace (0x-prefixed)");

        var rpcOpt = new Option<string?>(
            aliases: new[] { "--rpc" },
            description: "RPC URL of the node (default: http://127.0.0.1:8545)");

        var cmd = new Command("trace", "Print the gas causality tree for a transaction")
        {
            txHashArg, rpcOpt
        };

        cmd.SetHandler(async (txHash, rpc) =>
        {
            var exitCode = await TraceCommand.RunAsync(txHash, rpc, null);
            Environment.ExitCode = exitCode;
        }, txHashArg, rpcOpt);

        return cmd;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static void PrintBanner(NodeConfiguration config)
    {
        Console.WriteLine("🔥 Schlieren — Windows-Native Ethereum Development Platform");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine();

        if (!string.IsNullOrEmpty(config.ConfigSource))
        {
            Console.WriteLine($"📄 Config: {config.ConfigSource}");
            Console.WriteLine();
        }
    }

    static void DisplayServiceInfo(IHost host, NodeConfiguration config)
    {
        var router = host.Services.GetService<IJsonRpcRouter>();
        if (router == null) return;

        var methods = router.GetRegisteredMethods();
        Console.WriteLine($"🔌 {methods.Count} RPC methods registered");

        var grouped = methods
            .GroupBy(m => m.Split('_')[0])
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            Console.WriteLine($"   [{group.Key}]");
            foreach (var method in group.OrderBy(m => m))
                Console.WriteLine($"      • {method}");
        }
    }
}
