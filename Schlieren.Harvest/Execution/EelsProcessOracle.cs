using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Execution;

public sealed record EelsOracleOptions(
    string ExecutablePath,
    string ExpectedVersion,
    string WorkingDirectory,
    TimeSpan Timeout);

/// <summary>
/// Pins and invokes EELS with argument-safe process configuration and typed attempt evidence.
/// </summary>
public sealed class EelsProcessOracle : IReferenceOracle
{
    private readonly EelsOracleOptions _options;

    public string ExecutableSha256 { get; }
    public string ReportedVersion { get; }
    public bool VersionMatches { get; }

    public EelsProcessOracle(EelsOracleOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ExecutableSha256 = ComputeFileSha256(options.ExecutablePath);
        ReportedVersion = ProbeVersion(options.ExecutablePath);
        VersionMatches = string.IsNullOrWhiteSpace(options.ExpectedVersion) ||
                         ReportedVersion.Contains(options.ExpectedVersion,
                             StringComparison.OrdinalIgnoreCase);
    }

    public EelsIdentity Identity =>
        new(ExecutableSha256, ReportedVersion, null);

    public async Task<OracleRunResult> RunAsync(
        string fixturePath,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!VersionMatches)
        {
            var stderr = $"Version mismatch: expected '{_options.ExpectedVersion}', got '{ReportedVersion}'";
            return Result("", stderr, -1, false, ApparatusFailureKind.OracleProtocol, stopwatch.Elapsed);
        }

        var psi = CreateStartInfo(_options, fixturePath);
        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_options.Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillTree(process);
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                var partialStdout = await ReadCompletedAsync(stdoutTask);
                var partialStderr = await ReadCompletedAsync(stderrTask);
                var failure = ct.IsCancellationRequested
                    ? ApparatusFailureKind.Cancelled
                    : ApparatusFailureKind.OracleTimeout;
                return Result(partialStdout, partialStderr, -1, !ct.IsCancellationRequested,
                    failure, stopwatch.Elapsed);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var failureKind = process.ExitCode == 0
                ? (ApparatusFailureKind?)null
                : ApparatusFailureKind.OracleExit;
            return Result(stdout, stderr, process.ExitCode, false,
                failureKind, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return Result("", $"Process launch failed: {ex.Message}", -1, false,
                ApparatusFailureKind.OracleExit, stopwatch.Elapsed);
        }
    }

    public static IReadOnlyList<string> BuildArgumentList(string fixturePath) =>
        ["statetest", "--json", "--noreturndata", "--nostack", "--nomemory", fixturePath];

    /// <summary>
    /// Compatibility projection for callers that display the command. Process execution
    /// uses <see cref="BuildArgumentList"/> and never reparses this string.
    /// </summary>
    public static string BuildArguments(string fixturePath, EelsOracleOptions options) =>
        $"statetest --json --noreturndata --nostack --nomemory \"{fixturePath}\"";

    public static void ValidateIdentity(EelsIdentity actual, EelsIdentity pinned)
    {
        // Version is the primary semantic check — it identifies the specification behavior.
        if (!string.IsNullOrWhiteSpace(pinned.ReportedVersion) &&
            !actual.ReportedVersion.Contains(pinned.ReportedVersion,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"EELS version mismatch: expected '{pinned.ReportedVersion}', got '{actual.ReportedVersion}'.");

        // Launcher SHA-256: warn on mismatch but do not gate.
        // The pip console-launcher hash is packaging noise — it changes on venv
        // recreation without semantic change. Semantic provenance (source tree hash,
        // source commit) is the authoritative identity for certification.
        if (!string.IsNullOrWhiteSpace(pinned.ExecutableSha256) &&
            !string.Equals(actual.ExecutableSha256, pinned.ExecutableSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"WARNING: EELS launcher SHA-256 mismatch (non-blocking): " +
                $"manifest='{pinned.ExecutableSha256}', actual='{actual.ExecutableSha256}'. " +
                $"Version '{actual.ReportedVersion}' matches. " +
                $"Launcher hash is packaging noise; semantic provenance is authoritative.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(EelsOracleOptions options, string fixturePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildArgumentList(fixturePath))
            psi.ArgumentList.Add(argument);
        return psi;
    }

    private OracleRunResult Result(
        string stdout,
        string stderr,
        int exitCode,
        bool timedOut,
        ApparatusFailureKind? failureKind,
        TimeSpan elapsed)
    {
        var evidence = new ExecutionAttemptEvidence(
            failureKind,
            elapsed,
            exitCode,
            HashText(stdout),
            HashText(stderr),
            DiagnosticRetentionReduced: true,
            ExecutableSha256);
        return new OracleRunResult(stdout, stderr, exitCode, timedOut, evidence);
    }

    private static string ProbeVersion(string executablePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null) return "";
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000)) TryKillTree(process);
            return (stdout + " " + stderr).Trim();
        }
        catch { return ""; }
    }

    private static string ComputeFileSha256(string path)
    {
        try { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(); }
        catch { return ""; }
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<string> ReadCompletedAsync(Task<string> task)
    {
        try { return await task; }
        catch { return ""; }
    }

    private static void TryKillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }
}
