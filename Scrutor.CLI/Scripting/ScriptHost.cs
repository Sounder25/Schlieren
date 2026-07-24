using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Scrutor.CLI.Workspace;

namespace Scrutor.CLI.Scripting;

/// <summary>
/// Globals injected into every .csx script.
/// Accessible as top-level identifiers: node, accounts, Assert, Test.
/// </summary>
public sealed class ScriptGlobals
{
    /// <summary>Live RPC connection to the running Scrutor node.</summary>
    public ScrutorNode node { get; init; } = null!;

    /// <summary>Array of funded dev accounts (HD-derived, matches startup mnemonic).</summary>
    public string[] accounts { get; init; } = Array.Empty<string>();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Test runner helpers (used by *.test.csx)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Simple assertion set for .csx test scripts.
/// Throws AssertionException on failure so the test runner can catch it.
/// </summary>
public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Throw(message ?? $"Expected {expected}, got {actual}");
    }
    public static void True(bool condition, string? message = null)
    {
        if (!condition) Throw(message ?? "Expected true, got false");
    }
    public static void False(bool condition, string? message = null)
    {
        if (condition) Throw(message ?? "Expected false, got true");
    }
    public static void NotNull(object? obj, string? message = null)
    {
        if (obj == null) Throw(message ?? "Expected non-null");
    }
    public static void Null(object? obj, string? message = null)
    {
        if (obj != null) Throw(message ?? $"Expected null, got {obj}");
    }
    public static void Contains(string substring, string haystack, string? message = null)
    {
        if (!haystack.Contains(substring, StringComparison.OrdinalIgnoreCase))
            Throw(message ?? $"Expected '{haystack}' to contain '{substring}'");
    }
    private static void Throw(string msg) => throw new AssertionException(msg);
}

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}

/// <summary>
/// Test registration delegate used in *.test.csx.
/// Each Test() call is collected and run sequentially with pass/fail output.
/// </summary>
public sealed class TestRegistry
{
    private readonly List<(string Name, Func<Task> Body)> _tests = new();

    public void Register(string name, Func<Task> body) => _tests.Add((name, body));
    public void Register(string name, Action body) => _tests.Add((name, () => { body(); return Task.CompletedTask; }));

    public async Task<(int Passed, int Failed)> RunAsync()
    {
        int passed = 0, failed = 0;
        foreach (var (name, body) in _tests)
        {
            try
            {
                await body();
                Console.WriteLine($"  ✓  {name}");
                passed++;
            }
            catch (AssertionException ex)
            {
                Console.WriteLine($"  ✗  {name}");
                Console.WriteLine($"       {ex.Message}");
                failed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗  {name}");
                Console.WriteLine($"       {ex.GetType().Name}: {ex.Message}");
                failed++;
            }
        }
        return (passed, failed);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  ScriptHost
// ─────────────────────────────────────────────────────────────────────────────

public static class ScriptHost
{
    // Assemblies available to every script.
    private static readonly Assembly[] ScriptAssemblies =
    {
        typeof(ScrutorNode).Assembly,                          // Scrutor.CLI
        typeof(Scrutor.Core.Primitives.CryptoUtils).Assembly, // Scrutor.Core
        typeof(System.Numerics.BigInteger).Assembly,
        typeof(System.Net.Http.HttpClient).Assembly,
        typeof(System.Text.Json.JsonDocument).Assembly,
    };

    private static readonly string[] ScriptImports =
    {
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.Numerics",
        "System.Threading.Tasks",
        "System.Text.Json",
        "Scrutor.CLI.Scripting",
    };

    /// <summary>
    /// Execute a .csx script file against a running node.
    /// Injects `node` and `accounts` as top-level globals.
    /// Returns exit code (0 = ok, 1 = error).
    /// </summary>
    public static async Task<int> RunScriptAsync(
        string scriptPath,
        ScrutorNode node,
        string[] accounts)
    {
        var source = await File.ReadAllTextAsync(scriptPath);

        var opts = ScriptOptions.Default
            .AddReferences(ScriptAssemblies)
            .AddImports(ScriptImports)
            .WithFilePath(scriptPath)
            .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Debug);

        var globals = new ScriptGlobals { node = node, accounts = accounts };

        try
        {
            await CSharpScript.RunAsync(source, opts, globals, typeof(ScriptGlobals));
            return 0;
        }
        catch (CompilationErrorException cex)
        {
            Console.Error.WriteLine($"✗  Compilation error in {Path.GetFileName(scriptPath)}:");
            foreach (var d in cex.Diagnostics)
                Console.Error.WriteLine($"   {d}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  Runtime error in {Path.GetFileName(scriptPath)}:");
            Console.Error.WriteLine($"   {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Execute a *.test.csx file.
    /// In test mode a `Test(name, body)` global is available that collects and
    /// runs assertions. Returns (passed, failed) counts.
    /// </summary>
    public static async Task<(int Passed, int Failed)> RunTestScriptAsync(
        string scriptPath,
        ScrutorNode node,
        string[] accounts)
    {
        // Wrap the script source: expose Test() as a registration call.
        var source = await File.ReadAllTextAsync(scriptPath);

        // Prepend the Test() helper shim so script authors write Test("name", async () => { ... })
        // without any extra imports.
        var wrapped = $"""
// --- scrutor test runner preamble ---
var __testReg = new TestRegistry();
void Test(string __name, Func<Task> __body) => __testReg.Register(__name, __body);
void Test(string __name, Action __body)     => __testReg.Register(__name, __body);
// --- user script ---
{source}
// --- run collected tests ---
var __result = await __testReg.RunAsync();
return __result;
""";

        var opts = ScriptOptions.Default
            .AddReferences(ScriptAssemblies)
            .AddImports(ScriptImports)
            .WithFilePath(scriptPath)
            .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Debug);

        var globals = new ScriptGlobals { node = node, accounts = accounts };

        try
        {
            var state = await CSharpScript.RunAsync<(int, int)>(wrapped, opts, globals, typeof(ScriptGlobals));
            return state.ReturnValue;
        }
        catch (CompilationErrorException cex)
        {
            Console.Error.WriteLine($"✗  Compilation error in {Path.GetFileName(scriptPath)}:");
            foreach (var d in cex.Diagnostics)
                Console.Error.WriteLine($"   {d}");
            return (0, 1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  Runtime error in {Path.GetFileName(scriptPath)}:");
            Console.Error.WriteLine($"   {ex.Message}");
            return (0, 1);
        }
    }
}
