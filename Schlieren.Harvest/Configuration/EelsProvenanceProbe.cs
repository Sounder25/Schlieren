using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Schlieren.Harvest.Configuration;

/// <summary>
/// Probes an EELS installation for semantic provenance data.
/// Shells out to the venv Python and git to collect identity facts.
/// </summary>
public static class EelsProvenanceProbe
{
    /// <summary>
    /// Probes the given EELS installation for semantic identity.
    /// </summary>
    public static EelsSemanticIdentity Probe(string executablePath, string specsRoot)
    {
        if (!File.Exists(executablePath))
            throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_EXECUTABLE_MISSING",
                $"EELS executable not found: {executablePath}");

        if (!Directory.Exists(specsRoot))
            throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_SPECS_ROOT_MISSING",
                $"Execution-specs root not found: {specsRoot}");

        // Locate the venv Python
        var venvScripts = Path.GetDirectoryName(executablePath)!;
        var pythonExe = Path.Combine(venvScripts, "python.exe");
        if (!File.Exists(pythonExe))
            pythonExe = Path.Combine(venvScripts, "python");
        if (!File.Exists(pythonExe))
            throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_PYTHON_MISSING",
                $"Cannot find Python in EELS venv scripts directory: {venvScripts}");

        // Compute launcher SHA-256
        var launcherSha256 = ComputeFileSha256(executablePath);

        // Get source commit
        var sourceCommit = RunGit(specsRoot, "rev-parse HEAD");

        // Get Python version and probe package
        var probeScript = @"
import sys, hashlib, pathlib, importlib.metadata, json

dist = importlib.metadata.distribution('ethereum-execution')
src_dir = pathlib.Path(sys.argv[1]) / 'src' / 'ethereum'
tools_dir = pathlib.Path(sys.argv[1]) / 'src' / 'ethereum_spec_tools' / 'evm_tools'

def tree_hash(d):
    h = hashlib.sha256()
    for f in sorted(d.rglob('*.py')):
        h.update(f.read_bytes())
    return h.hexdigest()

deps = {}
for r in (dist.requires or []):
    pkg = r.split('>')[0].split('<')[0].split('=')[0].split('[')[0].split(';')[0].strip()
    try: deps[pkg] = importlib.metadata.version(pkg)
    except: pass

result = {
    'packageName': dist.metadata['Name'],
    'packageVersion': dist.metadata['Version'],
    'sourceTreeSha256': tree_hash(src_dir) if src_dir.exists() else '',
    'evmToolsSha256': tree_hash(tools_dir) if tools_dir.exists() else '',
    'pythonVersion': f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}',
    'runtimePlatform': sys.platform,
    'dependencyVersions': deps
}
print(json.dumps(result))
";
        var probeOutput = RunPython(pythonExe, probeScript, specsRoot);
        var probeData = System.Text.Json.JsonDocument.Parse(probeOutput).RootElement;

        var depVersions = new Dictionary<string, string>();
        if (probeData.TryGetProperty("dependencyVersions", out var depsEl))
        {
            foreach (var prop in depsEl.EnumerateObject())
                depVersions[prop.Name] = prop.Value.GetString() ?? "";
        }

        return new EelsSemanticIdentity(
            PackageName: probeData.GetProperty("packageName").GetString() ?? "ethereum-execution",
            PackageVersion: probeData.GetProperty("packageVersion").GetString() ?? "",
            SourceTreeSha256: probeData.GetProperty("sourceTreeSha256").GetString() ?? "",
            EvmToolsSha256: probeData.GetProperty("evmToolsSha256").GetString() ?? "",
            SourceCommit: sourceCommit,
            PythonVersion: probeData.GetProperty("pythonVersion").GetString() ?? "",
            RuntimePlatform: probeData.GetProperty("runtimePlatform").GetString() ?? "",
            LauncherSha256: launcherSha256,
            DependencyVersions: depVersions);
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RunGit(string workdir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return string.IsNullOrEmpty(output) ? "unknown" : output;
        }
        catch { return "unknown"; }
    }

    private static string RunPython(string pythonExe, string script, string specsRoot)
    {
        var psi = new ProcessStartInfo(pythonExe, $"-c \"{EscapeForCommandLine(script)}\" \"{specsRoot}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)
            ?? throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_PYTHON_FAILED", "Failed to start Python");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        if (p.ExitCode != 0)
            throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_PYTHON_FAILED",
                $"Python probe failed (exit {p.ExitCode}): {stderr}");
        return stdout.Trim();
    }

    private static string EscapeForCommandLine(string script) =>
        script.Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n");
}
