using System.Diagnostics;
using System.Security.Cryptography;

namespace Schlieren.Harvest.Execution;

/// <summary>Options for pinning and invoking the EELS executable.</summary>
public sealed record EelsOracleOptions(
    string ExecutablePath,
    string ExpectedVersion,
    string WorkingDirectory,
    TimeSpan Timeout);

/// <summary>
/// Pins and probes the EELS executable, then runs it against fixture files.
///
/// Contracts:
///   - Rejects a version mismatch before any case execution.
///   - Invokes: ethereum-spec-evm statetest --json &lt;fixturePath&gt;
///   - UseShellExecute=false, both streams redirected.
///   - Kills the process tree on timeout; returns TimedOut=true.
///   - Records executable SHA-256 and reported version.
///   - Never throws on process failure — callers classify the result.
/// </summary>
public sealed class EelsProcessOracle : IReferenceOracle
{
    private readonly EelsOracleOptions _options;

    public string ExecutableSha256 { get; }
    public string ReportedVersion  { get; }
    public bool   VersionMatches   { get; }

    public EelsProcessOracle(EelsOracleOptions options)
    {
        _options = options;

        // Record executable SHA-256
        try
        {
            var bytes = File.ReadAllBytes(options.ExecutablePath);
            ExecutableSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch
        {
            ExecutableSha256 = "";
        }

        // Probe reported version
        ReportedVersion = ProbeVersion(options.ExecutablePath);
        VersionMatches  = string.IsNullOrEmpty(options.ExpectedVersion) ||
                          ReportedVersion.Contains(options.ExpectedVersion, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OracleRunResult> RunAsync(string fixturePath, CancellationToken ct = default)
    {
        if (!VersionMatches)
        {
            return new OracleRunResult(
                Stdout:   "",
                Stderr:   $"Version mismatch: expected '{_options.ExpectedVersion}', got '{ReportedVersion}'",
                ExitCode: -1,
                TimedOut: false);
        }

        var psi = new ProcessStartInfo
        {
            FileName               = _options.ExecutablePath,
            Arguments              = $"statetest --json \"{fixturePath}\"",
            WorkingDirectory       = _options.WorkingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.Timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Kill process tree on timeout or cancellation
                TryKillTree(process);
                return new OracleRunResult(
                    Stdout:   stdoutBuilder.ToString(),
                    Stderr:   stderrBuilder.ToString(),
                    ExitCode: -1,
                    TimedOut: !ct.IsCancellationRequested);
            }

            return new OracleRunResult(
                Stdout:   stdoutBuilder.ToString(),
                Stderr:   stderrBuilder.ToString(),
                ExitCode: process.ExitCode,
                TimedOut: false);
        }
        catch (Exception ex)
        {
            return new OracleRunResult(
                Stdout:   "",
                Stderr:   $"Process launch failed: {ex.Message}",
                ExitCode: -1,
                TimedOut: false);
        }
    }

    private static string ProbeVersion(string executablePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = executablePath,
                Arguments              = "--version",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            var combined = (stdout + " " + stderr).Trim();
            return combined;
        }
        catch { return ""; }
    }

    private static void TryKillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }
}
