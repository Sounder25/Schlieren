using System.Diagnostics;
using System.Security.Cryptography;

namespace Schlieren.Harvest.Configuration;

/// <summary>
/// Probes an EELS installation for semantic provenance data.
/// Shells out to the venv Python and git to collect identity facts.
/// </summary>
public static class EelsProvenanceProbe
{
    /// <summary>Default timeout for subprocess invocations.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Probes the given EELS installation for semantic identity.
    /// </summary>
    /// <param name="executablePath">Path to the EELS venv launcher (ethereum-spec-evm.exe).</param>
    /// <param name="specsRoot">Path to the execution-specs repository root.</param>
    /// <param name="timeout">Optional timeout override for subprocess calls.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static EelsSemanticIdentity Probe(
        string executablePath,
        string specsRoot,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

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

        // Compute uv.lock and pyproject.toml SHA-256
        var uvLockPath = Path.Combine(specsRoot, "uv.lock");
        var uvLockSha256 = File.Exists(uvLockPath) ? ComputeFileSha256(uvLockPath) : "";
        var pyprojectPath = Path.Combine(specsRoot, "pyproject.toml");
        var pyprojectSha256 = File.Exists(pyprojectPath) ? ComputeFileSha256(pyprojectPath) : "";

        // Get source commit — failure means unprovable
        var (commitOk, sourceCommit) = RunGit(specsRoot, new[] { "rev-parse", "HEAD" },
            effectiveTimeout, cancellationToken);

        // Check for dirty working tree — only an empty successful result is clean
        var (statusOk, gitStatus) = RunGit(specsRoot, new[] { "status", "--porcelain" },
            effectiveTimeout, cancellationToken);
        var isClean = statusOk && string.IsNullOrWhiteSpace(gitStatus);

        // Get Python version and probe package metadata
        var probeScript = "import sys,hashlib,pathlib,importlib.metadata,json; "
            + "dist=importlib.metadata.distribution('ethereum-execution'); "
            + "s=pathlib.Path(sys.argv[1])/'src'/'ethereum'; "
            + "t=pathlib.Path(sys.argv[1])/'src'/'ethereum_spec_tools'/'evm_tools'; "
            + "h=lambda d:(lambda g:g.hexdigest())(hashlib.sha256(b''.join(f.read_bytes() for f in sorted(d.rglob('*.py'))))); "
            + "deps={}; "
            + "[deps.__setitem__(r.split('>')[0].split('<')[0].split('=')[0].split('[')[0].split(';')[0].strip(),importlib.metadata.version(r.split('>')[0].split('<')[0].split('=')[0].split('[')[0].split(';')[0].strip())) for r in (dist.requires or []) if r.split('>')[0].split('<')[0].split('=')[0].split('[')[0].split(';')[0].strip()]; "
            + "print(json.dumps({'packageName':dist.metadata['Name'],'packageVersion':dist.metadata['Version'],"
            + "'sourceTreeSha256':h(s) if s.exists() else '','evmToolsSha256':h(t) if t.exists() else '',"
            + "'pythonVersion':f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}',"
            + "'runtimePlatform':sys.platform,'dependencyVersions':deps}))";

        var probeOutput = RunPython(pythonExe, probeScript, specsRoot, effectiveTimeout, cancellationToken);
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
            SourceCommit: commitOk ? sourceCommit : "unknown",
            PythonVersion: probeData.GetProperty("pythonVersion").GetString() ?? "",
            RuntimePlatform: probeData.GetProperty("runtimePlatform").GetString() ?? "",
            LauncherSha256: launcherSha256,
            DependencyVersions: depVersions,
            IsCleanCheckout: isClean,
            UvLockSha256: uvLockSha256,
            PyprojectTomlSha256: pyprojectSha256);
    }

    internal static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Runs a git command and returns (success, stdout). On failure, timeout, or
    /// cancellation returns (false, ""). Never throws.
    /// </summary>
    private static (bool Success, string Output) RunGit(
        string workdir, string[] args, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            return RunProcessCore(psi, timeout, ct);
        }
        catch (OperationCanceledException) { return (false, ""); }
        catch { return (false, ""); }
    }

    /// <summary>
    /// Runs a Python script via <c>python -c &lt;script&gt; &lt;specsRoot&gt;</c> using
    /// <see cref="ProcessStartInfo.ArgumentList"/> for safe argument passing on Windows.
    /// No shell escaping is needed — the OS passes each argument as a discrete argv element.
    /// </summary>
    internal static string RunPython(
        string pythonExe,
        string script,
        string specsRoot,
        TimeSpan timeout,
        CancellationToken ct)
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

        var (success, stdout) = RunProcessCore(psi, timeout, ct);
        if (!success)
        {
            // RunProcessCore already killed the tree; rethrow with context.
            // If we get here, it wasn't cancellation (that throws OCE from
            // RunProcessCore), so it was a non-zero exit or timeout.
            throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_PYTHON_FAILED",
                $"Python probe failed: {stdout}");
        }
        return stdout;
    }

    /// <summary>
    /// Core subprocess runner with proper async wait, cancellation, tree kill,
    /// and stream draining.
    ///
    /// Sequence:
    ///   1. Start process.
    ///   2. Begin async reads of stdout and stderr (prevents pipe-buffer deadlock).
    ///   3. Await <see cref="Process.WaitForExitAsync"/> with a linked token that
    ///      fires on either caller cancellation or timeout.
    ///   4. On cancellation/timeout: kill the entire process tree, await termination,
    ///      drain remaining buffered output, then throw/return.
    ///   5. On normal exit: await stream drain, check exit code.
    /// </summary>
    private static (bool Success, string Stdout) RunProcessCore(
        ProcessStartInfo psi, TimeSpan timeout, CancellationToken ct)
    {
        using var p = Process.Start(psi)
            ?? throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_PROCESS_START_FAILED",
                $"Failed to start {psi.FileName}");

        // Begin async drain of both streams immediately to prevent deadlock
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        // Link caller cancellation with timeout into a single token
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        bool exited;
        try
        {
            // WaitForExitAsync honors cancellation properly
            p.WaitForExitAsync(linkedCts.Token).GetAwaiter().GetResult();
            exited = true;
        }
        catch (OperationCanceledException)
        {
            exited = false;
        }

        if (!exited)
        {
            // Kill entire process tree, then wait for it to actually terminate
            KillProcessTree(p);
            try { p.WaitForExit(5000); } catch { }

            // Drain any remaining buffered output
            try { stdoutTask.Wait(2000); } catch { }
            try { stderrTask.Wait(2000); } catch { }

            // Distinguish caller cancellation from timeout
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            throw new HarvestConfigurationException(
                "HARVEST.EELS_PROBE_TIMEOUT",
                $"Process '{psi.FileName}' timed out after {timeout.TotalSeconds}s");
        }

        // Process exited normally — finish draining streams
        stdoutTask.Wait(5000);
        stderrTask.Wait(5000);

        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result.Trim() : "";
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result.Trim() : "";

        if (p.ExitCode != 0)
            return (false, $"exit {p.ExitCode}: {stderr}");

        return (true, stdout);
    }

    /// <summary>Kill the process and its entire tree (child processes).</summary>
    private static void KillProcessTree(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
    }
}
