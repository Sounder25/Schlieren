using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scrutor.Core.DependencyInjection;
using Scrutor.Core.Configuration;
using Scrutor.RPC.DependencyInjection;
using Scrutor.Core.Forking;
using Scrutor.RPC; // Added for IJsonRpcRouter

namespace Scrutor.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // SECTION: CLI Parsing & Configuration
        
        // Parse command-line arguments and load configuration
        var (config, exitCode) = await CommandLineParser.ParseArgumentsAsync(args);
        
        // If parsing failed or help/version was requested (config null), exit
        if (config == null || exitCode != 0)
        {
            return exitCode;
        }
        
        try 
        {
            config.Validate();
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Configuration Error: {ex.Message}");
            return 1;
        }
        
        // Respect --silent flag
        if (!config.Silent)
        {
            PrintBanner(config);
        }
        
        // SECTION: Dependency Injection Setup
        
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                if (!config.Silent)
                    Console.WriteLine("📦 Registering services via Scrutor...");
                
                // Register the configuration as a singleton
                services.AddSingleton(config);
                
                // Register Core services (EVM, opcodes, state management)
                services.AddScrutorCore(); 
                
                // Register Forking Services if URL provided
                if (!string.IsNullOrEmpty(config.ForkUrl))
                {
                    services.AddSingleton<IBlockCache, BlockCache>();
                    services.AddHttpClient<IForkProvider, ForkProvider>(client =>
                    {
                        client.BaseAddress = new Uri(config.ForkUrl);
                        client.Timeout = TimeSpan.FromMilliseconds(config.ForkRequestTimeout);
                    });
                } 
                
                // Register RPC services (handlers, router, transports)
                services.AddScrutorRpc();
                
                if (!config.Silent)
                    Console.WriteLine("✅ Service registration complete");
            })
            .Build();

        // SECTION: Service Validation
        
        if (!config.Silent)
        {
            DisplayServiceInfo(host, config);
        }
        
        // SECTION: State Management
        // Handled by BootstrapService via IHostedService lifecycle
        
        // SECTION: Server Startup
        
        if (!config.Silent)
        {
            Console.WriteLine();
            Console.WriteLine("🚀 Starting Scrutor Node...");
            Console.WriteLine($"   Chain ID: {config.ChainId}");
            Console.WriteLine($"   Hardfork: {config.Hardfork}");
            Console.WriteLine($"   HTTP RPC: http://{config.Host}:{config.Port}");
            Console.WriteLine($"   WebSocket: ws://{config.Host}:{config.Port}");
            
            if (!string.IsNullOrEmpty(config.ForkUrl))
            {
                Console.WriteLine($"   Fork URL: {config.ForkUrl}");
                if (config.ForkBlockNumber.HasValue)
                    Console.WriteLine($"   Fork Block: {config.ForkBlockNumber.Value}");
            }
            
            Console.WriteLine();
            Console.WriteLine("Press Ctrl+C to stop");
        }
        
        await host.RunAsync();
        return 0;
    }
    
    /// <summary>
    /// Prints the startup banner with configuration summary
    /// </summary>
    private static void PrintBanner(NodeConfiguration config)
    {
        Console.WriteLine("🔥 Scrutor - Windows-Native Ethereum Node");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine();
        
        if (!string.IsNullOrEmpty(config.ConfigSource))
        {
            Console.WriteLine($"📄 Configuration loaded from: {config.ConfigSource}");
            Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Displays registered services and RPC methods
    /// </summary>
    private static void DisplayServiceInfo(IHost host, NodeConfiguration config)
    {
        // Try to get router if it exists (Service check)
        var router = host.Services.GetService<IJsonRpcRouter>();
        if (router != null) // Removed explicit cast as IJsonRpcRouter is sufficient
        {
            var methods = router.GetRegisteredMethods();
            Console.WriteLine();
            Console.WriteLine($"🔌 Registered {methods.Count} RPC methods:");
            
            // Group methods by namespace for better readability
            var grouped = methods
                .GroupBy(m => m.Split('_')[0])
                .OrderBy(g => g.Key);
            
            foreach (var group in grouped)
            {
                Console.WriteLine($"\n   [{group.Key}]");
                foreach (var method in group.OrderBy(m => m))
                {
                    Console.WriteLine($"      • {method}");
                }
            }
        }
    }
}
