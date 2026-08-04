//!/usr/bin/env dotnet-script
#r "nuget: Newtonsoft.Json, 13.0.3"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Run from project root
var projectRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var fixturesRoot = Path.Combine(projectRoot, "state_tests", "static", "state_tests");
var maxPerFork = args.Length > 1 ? int.Parse(args[1]) : 100;

Console.WriteLine($"=== SCRUTOR FULL SYSTEM SWEEP ===");
Console.WriteLine($"Project root: {projectRoot}");
Console.WriteLine($"Fixtures root: {fixturesRoot}");
Console.WriteLine($"Max cases per fork: {maxPerFork}");
Console.WriteLine();

if (!Directory.Exists(fixturesRoot))
{
    Console.WriteLine($"ERROR: Fixtures not found at {fixturesRoot}");
    Environment.Exit(1);
}

// Count fixtures per fork
var forkCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var forks = new[] { "Cancun", "Shanghai", "Paris", "London", "Berlin", "Istanbul", 
                   "Constantinople", "Byzantium", "Homestead", "Frontier", "Prague", "Osaka" };

foreach (var file in Directory.EnumerateFiles(fixturesRoot, "*.json", SearchOption.AllDirectories))
{
    try
    {
        var json = File.ReadAllText(file);
        foreach (var fork in forks)
        {
            if (json.Contains($"\"{fork}\"") || json.Contains($"\"{fork.ToLower()}\""))
            {
                if (!forkCounts.ContainsKey(fork)) forkCounts[fork] = 0;
                forkCounts[fork]++;
            }
        }
    }
    catch { /* skip unparseable */ }
}

Console.WriteLine("=== FIXTURE AVAILABILITY ===");
foreach (var fork in forks)
{
    var count = forkCounts.TryGetValue(fork, out var c) ? c : 0;
    Console.WriteLine($"{fork,-15} {count,6} fixtures");
}

// Total count
var total = Directory.EnumerateFiles(fixturesRoot, "*.json", SearchOption.AllDirectories).Count();
Console.WriteLine($"{"TOTAL",-15} {total,6} fixtures");
Console.WriteLine();

// Engine status
Console.WriteLine("=== ENGINE STATUS ===");
Console.WriteLine("Target fork: Cancun (hardwired)");
Console.WriteLine("Expected: 100% pass rate on Cancun fixtures");
Console.WriteLine("Expected: Lower pass rate on pre-Cancun (gas schedule delta)");
Console.WriteLine("Expected: 0% on Prague/Osaka (not implemented)");
Console.WriteLine();

// Recommendations
Console.WriteLine("=== NEXT STEPS ===");
Console.WriteLine("1. Run full Cancun harness (should be 100%)");
Console.WriteLine("2. Analyze failure taxonomy on pre-Cancun forks");
Console.WriteLine("3. Implement Prague EIPs (7702, 2537 BLS) if targeting mainnet");
Console.WriteLine();
Console.WriteLine("Sweep complete. Run specific harness tests for detailed KPIs.");
