using System.Diagnostics;
using System.Runtime.InteropServices;
using Schlieren.Harvest.Configuration;
using Xunit;

namespace Schlieren.Harvest.Tests.Configuration;

// ═══════════════════════════════════════════════════════════════════════════
// Deterministic unit tests — no Python required.
// These validate ArgumentList construction produces correct argv elements.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class RunPythonArgumentListTests
{
    /// <summary>
    /// Constructs a ProcessStartInfo via the same pattern RunPython uses
    /// and asserts the ArgumentList contains the expected discrete elements.
    /// </summary>
    [Fact]
    public void ArgumentList_SimpleScript_ContainsThreeElements()
    {
        var psi = BuildPsi("python.exe", "print('hello')", "/some/path");

        Assert.Equal(3, psi.ArgumentList.Count);
        Assert.Equal("-c", psi.ArgumentList[0]);
        Assert.Equal("print('hello')", psi.ArgumentList[1]);
        Assert.Equal("/some/path", psi.ArgumentList[2]);
    }

    [Fact]
    public void ArgumentList_ScriptWithNewlines_PreservedVerbatim()
    {
        var script = "import sys\nx = 42\nprint(x)";
        var psi = BuildPsi("python.exe", script, ".");

        Assert.Equal(script, psi.ArgumentList[1]);
        Assert.Contains("\n", psi.ArgumentList[1]);
    }

    [Fact]
    public void ArgumentList_ScriptWithQuotes_PreservedVerbatim()
    {
        var script = "print(\"hello\" + ' world')";
        var psi = BuildPsi("python.exe", script, ".");

        Assert.Equal(script, psi.ArgumentList[1]);
        Assert.Contains("\"", psi.ArgumentList[1]);
        Assert.Contains("'", psi.ArgumentList[1]);
    }

    [Fact]
    public void ArgumentList_PathWithSpaces_PreservedVerbatim()
    {
        var path = @"C:\Program Files\execution specs\root";
        var psi = BuildPsi("python.exe", "print(1)", path);

        Assert.Equal(path, psi.ArgumentList[2]);
        Assert.Contains(" ", psi.ArgumentList[2]);
    }

    [Fact]
    public void ArgumentList_PathWithBackslashes_PreservedVerbatim()
    {
        var path = @"C:\projects\execution-specs\src\ethereum";
        var psi = BuildPsi("python.exe", "print(1)", path);

        Assert.Equal(path, psi.ArgumentList[2]);
        Assert.Contains(@"\", psi.ArgumentList[2]);
    }

    [Fact]
    public void ArgumentList_CombinedSpecialChars_AllPreserved()
    {
        var script = "import json\nprint(json.dumps({\"key\": \"val with \\\"quotes\\\"\"}))";
        var path = @"C:\my path\with spaces\and""quotes";
        var psi = BuildPsi("python.exe", script, path);

        Assert.Equal("-c", psi.ArgumentList[0]);
        Assert.Equal(script, psi.ArgumentList[1]);
        Assert.Equal(path, psi.ArgumentList[2]);
    }

    [Fact]
    public void ArgumentList_UseShellExecute_IsFalse()
    {
        var psi = BuildPsi("python.exe", "print(1)", ".");

        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.CreateNoWindow);
    }

    /// <summary>
    /// Mirrors the exact ProcessStartInfo construction from EelsProvenanceProbe.RunPython.
    /// </summary>
    private static ProcessStartInfo BuildPsi(string pythonExe, string script, string specsRoot)
    {
        var psi = new ProcessStartInfo(pythonExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(specsRoot);
        return psi;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Integration tests — require Python. Skipped when Python is unavailable.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class RunPythonIntegrationTests
{
    private static readonly string? PythonPath = FindPython();

    private static string? FindPython()
    {
        // Try EELS venv
        var specsRoot = Environment.GetEnvironmentVariable("EELS_SPECS_ROOT");
        if (!string.IsNullOrEmpty(specsRoot))
        {
            var candidate = Path.Combine(specsRoot, ".venv", "Scripts", "python.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(specsRoot, ".venv", "bin", "python");
            if (File.Exists(candidate)) return candidate;
        }

        // Try EELS executable sibling
        var eelsExe = Environment.GetEnvironmentVariable("EELS_EXE");
        if (!string.IsNullOrEmpty(eelsExe))
        {
            var dir = Path.GetDirectoryName(eelsExe)!;
            var candidate = Path.Combine(dir,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python.exe" : "python");
            if (File.Exists(candidate)) return candidate;
        }

        // Try system Python via PATH
        try
        {
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
            var psi = new ProcessStartInfo(exeName, "python")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                var first = output.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(first) && File.Exists(first))
                    return first;
            }
        }
        catch { }

        return null;
    }

    private static string RequirePython()
    {
        Skip.If(PythonPath is null, "Python not available — skipping integration test");
        return PythonPath!;
    }

    [SkippableFact]
    public void RunPython_SimpleEcho_ReturnsOutput()
    {
        var python = RequirePython();
        var result = EelsProvenanceProbe.RunPython(
            python, "print('hello')", ".", TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal("hello", result);
    }

    [SkippableFact]
    public void RunPython_SpacesInSpecsRoot_SurvivesArgvPassing()
    {
        var python = RequirePython();
        var dir = Path.Combine(Path.GetTempPath(), "eels probe test " + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var result = EelsProvenanceProbe.RunPython(
                python, "import sys; print(sys.argv[1])", dir,
                TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(dir, result);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RunPython_QuotesInScript_SurvivesArgvPassing()
    {
        var python = RequirePython();
        var result = EelsProvenanceProbe.RunPython(
            python, "print(\"hello\" + ' world')", ".",
            TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal("hello world", result);
    }

    [SkippableFact]
    public void RunPython_BackslashesInSpecsRoot_SurvivesArgvPassing()
    {
        var python = RequirePython();
        var dir = Path.Combine(Path.GetTempPath(), "eels\\probe\\test_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var result = EelsProvenanceProbe.RunPython(
                python, "import sys; print(sys.argv[1])", dir,
                TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(dir, result);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RunPython_NewlinesInScript_SurvivesArgvPassing()
    {
        var python = RequirePython();
        var result = EelsProvenanceProbe.RunPython(
            python, "import sys\nx = 42\nprint(f'value={x}')", ".",
            TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal("value=42", result);
    }

    [SkippableFact]
    public void RunPython_CombinedSpecialChars_EndToEnd()
    {
        var python = RequirePython();
        var dir = Path.Combine(Path.GetTempPath(), "eels probe\\special_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var script = "import sys, json\n"
                + "path = sys.argv[1]\n"
                + "msg = \"hello \\\"world\\\"\"\n"
                + "print(json.dumps({'path': path, 'msg': msg}))";
            var result = EelsProvenanceProbe.RunPython(
                python, script, dir, TimeSpan.FromSeconds(10), CancellationToken.None);
            var parsed = System.Text.Json.JsonDocument.Parse(result).RootElement;
            Assert.Equal(dir, parsed.GetProperty("path").GetString());
            Assert.Equal("hello \"world\"", parsed.GetProperty("msg").GetString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RunPython_Timeout_ThrowsProbeTimeout()
    {
        var python = RequirePython();
        var ex = Assert.Throws<HarvestConfigurationException>(() =>
            EelsProvenanceProbe.RunPython(
                python, "import time; time.sleep(300)", ".",
                TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Equal("HARVEST.EELS_PROBE_TIMEOUT", ex.Code);
    }

    [SkippableFact]
    public void RunPython_NonZeroExit_ThrowsProbeFailed()
    {
        var python = RequirePython();
        var ex = Assert.Throws<HarvestConfigurationException>(() =>
            EelsProvenanceProbe.RunPython(
                python, "import sys; sys.exit(1)", ".",
                TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal("HARVEST.EELS_PROBE_PYTHON_FAILED", ex.Code);
    }

    [SkippableFact]
    public void RunPython_LargeStdout_DoesNotDeadlock()
    {
        var python = RequirePython();
        var result = EelsProvenanceProbe.RunPython(
            python, "print('x' * 100000)", ".",
            TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(100000, result.Length);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Task 2 — Required integration tests
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void RunPython_MultilineScriptAndSpacedPath_RoundTrips()
    {
        var python = RequirePython();
        // Multi-line script with a path that has spaces — both must survive argv
        var script = "import sys\nimport os\nprint(sys.argv[1])";
        var spacedPath = Path.Combine(Path.GetTempPath(), "eels probe path with spaces");
        Directory.CreateDirectory(spacedPath);
        try
        {
            var result = EelsProvenanceProbe.RunPython(
                python, script, spacedPath,
                TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(spacedPath, result);
        }
        finally { Directory.Delete(spacedPath); }
    }

    [SkippableFact]
    public void RunPython_Timeout_IsTyped()
    {
        var python = RequirePython();
        // A script that sleeps forever must produce a typed timeout exception
        var ex = Assert.Throws<HarvestConfigurationException>(() =>
            EelsProvenanceProbe.RunPython(
                python, "import time; time.sleep(300)", ".",
                TimeSpan.FromMilliseconds(500), CancellationToken.None));
        Assert.Equal("HARVEST.EELS_PROBE_TIMEOUT", ex.Code);
    }

    [SkippableFact]
    public void RunPython_Cancellation_IsDistinctFromTimeout()
    {
        var python = RequirePython();
        // Cancellation before timeout must produce OperationCanceledException, NOT
        // a HarvestConfigurationException with TIMEOUT code.
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately
        Assert.Throws<OperationCanceledException>(() =>
            EelsProvenanceProbe.RunPython(
                python, "import time; time.sleep(300)", ".",
                TimeSpan.FromSeconds(30), cts.Token));
    }

    [SkippableFact]
    public void RunPython_LargeStdoutAndStderr_DoNotDeadlock()
    {
        var python = RequirePython();
        // Write 100KB to both stdout and stderr simultaneously — must not deadlock.
        var script = "import sys; sys.stdout.write('O' * 100000); sys.stderr.write('E' * 100000); sys.stdout.flush(); sys.stderr.flush()";
        var result = EelsProvenanceProbe.RunPython(
            python, script, ".",
            TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.Equal(100000, result.Length);
    }
}
