using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Fixtures;
using Schlieren.Harvest.Worker;

namespace Schlieren.Harvest.Campaigns;

/// <summary>Executes the independent EELS oracle and canonical Schlieren worker.</summary>
public sealed class SubprocessCaseWorker : ICaseWorker
{
    private readonly string _workerExePath;
    private readonly string _workerSha256;
    private readonly IReferenceOracle _oracle;
    private readonly int _timeoutMs;

    public SubprocessCaseWorker(string workerExePath, IReferenceOracle oracle, int timeoutSeconds = 120)
    {
        if (!File.Exists(workerExePath))
            throw new FileNotFoundException($"Worker executable not found: {workerExePath}", workerExePath);
        _workerExePath = workerExePath;
        _workerSha256 = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(workerExePath))).ToLowerInvariant();
        _oracle = oracle ?? throw new ArgumentNullException(nameof(oracle));
        _timeoutMs = checked(timeoutSeconds * 1000);
    }

    public static CaseStatus StatusForApparatusFailure(ApparatusFailureKind failure) => failure switch
    {
        ApparatusFailureKind.OracleTimeout or
        ApparatusFailureKind.OracleExit or
        ApparatusFailureKind.OracleProtocol => CaseStatus.HarnessError,
        _ => CaseStatus.Aborted
    };

    public async Task<ComparisonResult> ExecuteCaseAsync(
        ManifestCase manifestCase,
        string catalogRoot,
        string manifestHash,
        CancellationToken ct = default)
    {
        var fixturePath = Path.IsPathRooted(manifestCase.RelativePath)
            ? manifestCase.RelativePath
            : Path.GetFullPath(Path.Combine(catalogRoot, manifestCase.RelativePath));

        if (!File.Exists(fixturePath))
            return ConformanceComparator.Aborted($"Fixture file not found: {fixturePath}");

        OracleRunResult oracleResult;
        try
        {
            oracleResult = await _oracle.RunAsync(fixturePath, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var evidence = Evidence(ApparatusFailureKind.OracleExit, TimeSpan.Zero, null, "", ex.Message, "");
            return ApparatusFailure(evidence, $"EELS oracle invocation failed: {ex.Message}");
        }

        if (oracleResult.ExitCode != 0 || oracleResult.TimedOut)
        {
            var failure = oracleResult.AttemptEvidence?.FailureKind ??
                (oracleResult.TimedOut
                    ? ApparatusFailureKind.OracleTimeout
                    : ApparatusFailureKind.OracleExit);
            var evidence = EnsureOracleEvidence(oracleResult, failure);
            return ApparatusFailure(evidence,
                $"EELS oracle returned exit code {oracleResult.ExitCode}" +
                (oracleResult.TimedOut ? " (timed out)" : "") +
                $". Stderr: {Truncate(oracleResult.Stderr, 200)}");
        }

        var eelsResult = EelsOutputParser.Parse(
            oracleResult.Stdout, oracleResult.ExitCode, oracleResult.Stderr);
        if (!eelsResult.IsSuccess)
        {
            var evidence = EnsureOracleEvidence(oracleResult, ApparatusFailureKind.OracleProtocol);
            return ApparatusFailure(evidence, $"EELS output parse failed: {eelsResult.ParseError}");
        }

        var matchingEntry = eelsResult.Cases
            .FirstOrDefault(e => string.Equals(e.Name, manifestCase.CaseId, StringComparison.Ordinal));
        if (matchingEntry is null)
        {
            var evidence = EnsureOracleEvidence(oracleResult, ApparatusFailureKind.OracleProtocol);
            return ApparatusFailure(evidence,
                $"EELS produced no exact entry for case '{manifestCase.CaseId}'");
        }

        var (expectedSnapshot, parseError) = FixtureSnapshotBuilder.Build(
            fixturePath, manifestCase.Fork, manifestCase.CaseId);
        if (expectedSnapshot is null)
            return new ComparisonResult(CaseStatus.FixtureInvalid, Array.Empty<FieldDelta>(),
                $"Cannot build expected snapshot: {parseError}");

        if (!matchingEntry.Pass)
        {
            var evidence = EnsureOracleEvidence(oracleResult, ApparatusFailureKind.OracleProtocol);
            return ApparatusFailure(evidence,
                $"EELS oracle could not confirm fixture post-state: pass={matchingEntry.Pass}, " +
                $"stateRoot={matchingEntry.StateRoot}.");
        }

        WorkerInvocation worker;
        try
        {
            worker = await SpawnWorkerAsync(manifestCase, fixturePath, manifestHash, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var evidence = Evidence(ApparatusFailureKind.WorkerCrash, TimeSpan.Zero, null, "", ex.Message, _workerSha256);
            return ApparatusFailure(evidence, $"Worker spawn failed: {ex.Message}");
        }

        if (worker.Snapshot is null)
            return ApparatusFailure(worker.Evidence, "Worker did not return a valid execution snapshot");

        return ConformanceComparator.Compare(expectedSnapshot, worker.Snapshot);
    }

    private ComparisonResult ApparatusFailure(ExecutionAttemptEvidence evidence, string detail) =>
        new(StatusForApparatusFailure(evidence.FailureKind
                ?? throw new InvalidOperationException("Failure evidence requires a failure kind.")),
            Array.Empty<FieldDelta>(), detail, evidence);

    private async Task<WorkerInvocation> SpawnWorkerAsync(
        ManifestCase manifestCase,
        string fixturePath,
        string manifestHash,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var execReq = new ExecuteRequest(
            manifestHash, manifestCase.CaseId, fixturePath,
            manifestCase.SourceSha256, manifestCase.Fork, JournalEnabled: false);
        var requestPayload = JsonSerializer.Serialize(execReq,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var workerRequest = JsonSerializer.Serialize(new WorkerRequest("execute", requestPayload),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var psi = new ProcessStartInfo
        {
            FileName = _workerExePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start worker process");
        await process.StandardInput.WriteLineAsync(workerRequest);
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeoutMs);
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
                : ApparatusFailureKind.WorkerTimeout;
            return new WorkerInvocation(null,
                Evidence(failure, stopwatch.Elapsed, null, partialStdout, partialStderr, _workerSha256));
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var hasOutput = !string.IsNullOrWhiteSpace(stdout);
        var termination = WorkerExitClassifier.Classify(
            process.ExitCode, hasOutput, timedOut: false, cancelled: false);
        if (termination != Schlieren.Harvest.Execution.WorkerTerminationKind.Completed)
            return new WorkerInvocation(null,
                Evidence(WorkerExitClassifier.ToApparatusFailure(termination),
                    stopwatch.Elapsed, process.ExitCode, stdout, stderr, _workerSha256));

        WorkerResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<WorkerResponse>(stdout.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            response = null;
        }

        if (response is null || !response.Success || string.IsNullOrEmpty(response.Result))
            return new WorkerInvocation(null,
                Evidence(ApparatusFailureKind.WorkerProtocol, stopwatch.Elapsed,
                    process.ExitCode, stdout, stderr, _workerSha256));

        ExecutionSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ExecutionSnapshot>(response.Result,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            snapshot = null;
        }

        var evidence = Evidence(snapshot is null ? ApparatusFailureKind.WorkerProtocol : null,
            stopwatch.Elapsed, process.ExitCode, stdout, stderr, _workerSha256);
        return new WorkerInvocation(snapshot, evidence);
    }

    private static ExecutionAttemptEvidence EnsureOracleEvidence(
        OracleRunResult result,
        ApparatusFailureKind failure) =>
        result.AttemptEvidence is null
            ? Evidence(failure, TimeSpan.Zero, result.ExitCode,
                result.Stdout, result.Stderr, "")
            : result.AttemptEvidence with { FailureKind = failure };

    private static ExecutionAttemptEvidence Evidence(
        ApparatusFailureKind? failure,
        TimeSpan elapsed,
        int? exitCode,
        string stdout,
        string stderr,
        string executableSha256) =>
        new(failure, elapsed, exitCode, Hash(stdout), Hash(stderr),
            DiagnosticRetentionReduced: true, executableSha256);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static async Task<string> ReadCompletedAsync(Task<string> task)
    {
        try { return await task; }
        catch { return ""; }
    }

    private static void TryKillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private sealed record WorkerInvocation(
        ExecutionSnapshot? Snapshot,
        ExecutionAttemptEvidence Evidence);
}
