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
/// Production ICaseWorker that executes each case through real subprocess boundaries:
///
///   1. Spawns the EELS oracle process against the fixture to independently confirm
///      the fixture's expected pass/fail and stateRoot (runtime oracle authority).
///   2. Builds expected ExecutionSnapshot from the fixture post-state
///      (field-level ground truth: gas, status, accounts, storage).
///   3. Spawns <c>Schlieren.Harvest.Worker</c> as a child process to execute Schlieren
///      through the canonical EVM path (fresh state, no shared mutable state).
///   4. Cross-validates: if EELS disagrees with fixture's declared outcome → HarnessError.
///   5. Compares Schlieren's actual output against the expected via ConformanceComparator.
///
/// Worker crash, timeout, or protocol error → Aborted (never Pass).
/// EELS crash or disagreement with fixture → HarnessError (apparatus defect).
/// </summary>
public sealed class SubprocessCaseWorker : ICaseWorker
{
    private readonly string _workerExePath;
    private readonly IReferenceOracle _oracle;
    private readonly int _timeoutMs;

    /// <param name="workerExePath">Absolute path to the Schlieren.Harvest.Worker executable.</param>
    /// <param name="oracle">EELS process oracle for independent runtime confirmation.</param>
    /// <param name="timeoutSeconds">Per-case timeout in seconds.</param>
    public SubprocessCaseWorker(string workerExePath, IReferenceOracle oracle, int timeoutSeconds = 120)
    {
        if (!File.Exists(workerExePath))
            throw new FileNotFoundException(
                $"Worker executable not found: {workerExePath}", workerExePath);
        _workerExePath = workerExePath;
        _oracle = oracle ?? throw new ArgumentNullException(nameof(oracle));
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

        // 1. Run EELS oracle independently against the fixture
        OracleRunResult oracleResult;
        try
        {
            oracleResult = await _oracle.RunAsync(fixturePath, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ComparisonResult(CaseStatus.HarnessError, Array.Empty<FieldDelta>(),
                $"EELS oracle invocation failed: {ex.Message}");
        }

        // EELS nonzero exit or timeout → HarnessError (apparatus cannot confirm fixture)
        if (oracleResult.ExitCode != 0 || oracleResult.TimedOut)
        {
            return new ComparisonResult(CaseStatus.HarnessError, Array.Empty<FieldDelta>(),
                $"EELS oracle returned exit code {oracleResult.ExitCode}" +
                (oracleResult.TimedOut ? " (timed out)" : "") +
                $". Stderr: {oracleResult.Stderr?.Substring(0, Math.Min(200, oracleResult.Stderr?.Length ?? 0))}");
        }

        // Parse EELS result to check pass/fail agreement with fixture
        var eelsResult = EelsOutputParser.Parse(oracleResult.Stdout, oracleResult.ExitCode, oracleResult.Stderr);
        if (!eelsResult.IsSuccess)
        {
            return new ComparisonResult(CaseStatus.HarnessError, Array.Empty<FieldDelta>(),
                $"EELS output parse failed: {eelsResult.ParseError}");
        }

        // Find the entry matching our case (or first entry)
        var matchingEntry = eelsResult.Cases
            .FirstOrDefault(e => e.Name == manifestCase.CaseId)
            ?? eelsResult.Cases.FirstOrDefault();

        if (matchingEntry is null)
        {
            return new ComparisonResult(CaseStatus.HarnessError, Array.Empty<FieldDelta>(),
                "EELS produced no entries for this fixture");
        }

        // 2. Build expected snapshot from fixture post-state (detailed field-level authority)
        var (expectedSnapshot, parseError) = FixtureSnapshotBuilder.Build(
            fixturePath, manifestCase.Fork, manifestCase.CaseId);

        if (expectedSnapshot is null)
            return new ComparisonResult(CaseStatus.FixtureInvalid, Array.Empty<FieldDelta>(),
                $"Cannot build expected snapshot: {parseError}");

        // Cross-validate: EELS pass/fail must agree with fixture's declared status
        if (matchingEntry.Pass != expectedSnapshot.IsSuccess)
        {
            return new ComparisonResult(CaseStatus.HarnessError, Array.Empty<FieldDelta>(),
                $"EELS oracle disagrees with fixture: EELS says pass={matchingEntry.Pass}, " +
                $"fixture post-state says isSuccess={expectedSnapshot.IsSuccess}. " +
                "This indicates a fixture or oracle defect.");
        }

        // 3. Spawn worker subprocess to execute Schlieren
        ExecutionSnapshot? actualSnapshot;
        try
        {
            actualSnapshot = await SpawnWorkerAsync(
                manifestCase, fixturePath, manifestHash, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ConformanceComparator.Aborted($"Worker spawn failed: {ex.Message}");
        }

        if (actualSnapshot is null)
            return ConformanceComparator.Aborted("Worker returned null snapshot (protocol error or crash)");

        // 4. Compare Schlieren output against expected (fixture post-state confirmed by EELS)
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

        await process.StandardInput.WriteLineAsync(workerRequest);
        process.StandardInput.Close();

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
            try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }

        var termination = WorkerExitClassifier.Classify(
            process.ExitCode, !string.IsNullOrWhiteSpace(stdout), timedOut: false, cancelled: false);

        if (termination != Execution.WorkerTerminationKind.Completed)
            return null;

        var response = JsonSerializer.Deserialize<WorkerResponse>(stdout.Trim(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (response is null || !response.Success || string.IsNullOrEmpty(response.Result))
            return null;

        return JsonSerializer.Deserialize<ExecutionSnapshot>(response.Result,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
