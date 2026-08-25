using System.Diagnostics;
using System.Text.Json;
using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Fixtures;
using Schlieren.Harvest.Worker;

namespace Schlieren.Harvest.Campaigns;

/// <summary>
/// Production ICaseWorker that executes each case through the real subprocess boundary:
///
///   1. Spawns <c>Schlieren.Harvest.Worker</c> as a child process with the execute request.
///      The worker builds fresh EVM state and runs ApplyTransactionAsync independently.
///   2. Builds expected ExecutionSnapshot from the fixture post-state oracle
///      (the independent ground truth per the approved design).
///   3. Compares Schlieren's actual output against the expected via ConformanceComparator.
///
/// The EELS process oracle is invoked separately to confirm the fixture's own correctness
/// (pass/fail agreement) but the primary comparison authority is the fixture post-state,
/// which contains exact gas, status, and post-state fields independently grounded.
///
/// Worker crash, timeout, or protocol error → Aborted (never Pass).
/// </summary>
public sealed class SubprocessCaseWorker : ICaseWorker
{
    private readonly string _workerExePath;
    private readonly int _timeoutMs;

    /// <param name="workerExePath">Absolute path to the Schlieren.Harvest.Worker executable.</param>
    /// <param name="timeoutSeconds">Per-case timeout in seconds.</param>
    public SubprocessCaseWorker(string workerExePath, int timeoutSeconds = 120)
    {
        if (!File.Exists(workerExePath))
            throw new FileNotFoundException(
                $"Worker executable not found: {workerExePath}", workerExePath);
        _workerExePath = workerExePath;
        _timeoutMs = timeoutSeconds * 1000;
    }

    public async Task<ComparisonResult> ExecuteCaseAsync(
        ManifestCase manifestCase,
        string catalogRoot,
        string manifestHash,
        CancellationToken ct = default)
    {
        // Resolve fixture path
        var fixturePath = Path.IsPathRooted(manifestCase.RelativePath)
            ? manifestCase.RelativePath
            : Path.GetFullPath(Path.Combine(catalogRoot, manifestCase.RelativePath));

        if (!File.Exists(fixturePath))
            return ConformanceComparator.Aborted($"Fixture file not found: {fixturePath}");

        // 1. Build expected snapshot from fixture post-state (independent ground truth)
        var (expectedSnapshot, parseError) = FixtureSnapshotBuilder.Build(
            fixturePath, manifestCase.Fork, manifestCase.CaseId);

        if (expectedSnapshot is null)
            return new ComparisonResult(CaseStatus.FixtureInvalid, Array.Empty<FieldDelta>(),
                $"Cannot build expected snapshot: {parseError}");

        // 2. Spawn worker subprocess to execute Schlieren
        ExecutionSnapshot? actualSnapshot;
        try
        {
            actualSnapshot = await SpawnWorkerAsync(
                manifestCase, fixturePath, manifestHash, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // propagate parent cancellation
        }
        catch (Exception ex)
        {
            return ConformanceComparator.Aborted($"Worker spawn failed: {ex.Message}");
        }

        if (actualSnapshot is null)
            return ConformanceComparator.Aborted("Worker returned null snapshot (protocol error or crash)");

        // 3. Compare
        return ConformanceComparator.Compare(expectedSnapshot, actualSnapshot);
    }

    private async Task<ExecutionSnapshot?> SpawnWorkerAsync(
        ManifestCase manifestCase,
        string fixturePath,
        string manifestHash,
        CancellationToken ct)
    {
        var execReq = new ExecuteRequest(
            ManifestHash: manifestHash,
            CaseId:       manifestCase.CaseId,
            FixturePath:  fixturePath,
            SourceSha256: manifestCase.SourceSha256,
            Fork:         manifestCase.Fork,
            JournalEnabled: false);

        var requestPayload = JsonSerializer.Serialize(execReq,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var workerRequest = JsonSerializer.Serialize(
            new WorkerRequest("execute", requestPayload),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var psi = new ProcessStartInfo
        {
            FileName               = _workerExePath,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start worker process");

        // Send request
        await process.StandardInput.WriteLineAsync(workerRequest);
        process.StandardInput.Close();

        // Wait with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        string stdout;
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — kill the process
            try { process.Kill(entireProcessTree: true); } catch { }
            return null; // caller maps to Aborted
        }

        // Classify termination
        var exitCode = process.ExitCode;
        var hasValidResponse = !string.IsNullOrWhiteSpace(stdout);

        var termination = WorkerExitClassifier.Classify(
            exitCode, hasValidResponse, timedOut: false, cancelled: false);

        if (termination != Execution.WorkerTerminationKind.Completed)
            return null; // caller maps to Aborted

        // Parse worker response
        var response = JsonSerializer.Deserialize<WorkerResponse>(stdout.Trim(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (response is null || !response.Success || string.IsNullOrEmpty(response.Result))
            return null;

        // Deserialize the ExecutionSnapshot from the worker's result
        var snapshot = JsonSerializer.Deserialize<ExecutionSnapshot>(response.Result,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return snapshot;
    }
}
