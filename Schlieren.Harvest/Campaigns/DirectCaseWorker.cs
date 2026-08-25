using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Fixtures;

namespace Schlieren.Harvest.Campaigns;

/// <summary>
/// In-process ICaseWorker that executes Schlieren directly and compares
/// against the fixture post-state oracle. No subprocess boundary — suitable
/// for the first baseline inspection where crash isolation is not yet required.
///
/// For each case:
///   1. Parse the fixture to get the expected ExecutionSnapshot (FixtureSnapshotBuilder).
///   2. Execute Schlieren (SchlierenCaseExecutor) to get the actual ExecutionSnapshot.
///   3. Compare via ConformanceComparator.
/// </summary>
public sealed class DirectCaseWorker : ICaseWorker
{
    private readonly int _timeoutMs;

    public DirectCaseWorker(int timeoutSeconds = 120)
    {
        _timeoutMs = timeoutSeconds * 1000;
    }

    public async Task<ComparisonResult> ExecuteCaseAsync(
        ManifestCase manifestCase,
        string catalogRoot,
        string manifestHash,
        CancellationToken ct = default)
    {
        try
        {
            // Resolve the fixture path
            var fixturePath = Path.IsPathRooted(manifestCase.RelativePath)
                ? manifestCase.RelativePath
                : Path.Combine(catalogRoot, manifestCase.RelativePath);

            if (!File.Exists(fixturePath))
                return ConformanceComparator.Aborted($"Fixture file not found: {fixturePath}");

            // Build expected snapshot from fixture post-state
            var (expectedSnapshot, parseError) = FixtureSnapshotBuilder.Build(
                fixturePath, manifestCase.Fork);

            if (expectedSnapshot is null)
                return new ComparisonResult(CaseStatus.FixtureInvalid, Array.Empty<FieldDelta>(),
                    $"Cannot build expected snapshot: {parseError}");

            // Build metadata for executor
            var meta = new FixtureCaseMetadata(
                manifestCase.CaseId,
                manifestCase.RelativePath,
                manifestCase.SourceSha256,
                manifestCase.Fork,
                new HashSet<StorageDimension>(manifestCase.Dimensions),
                AdmissionReasonCode.Admitted,
                null);

            // Execute Schlieren with timeout
            var executor = new SchlierenCaseExecutor();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeoutMs);

            ExecutionSnapshot actualSnapshot;
            try
            {
                actualSnapshot = await Task.Run(
                    () => executor.ExecuteAsync(meta, journalEnabled: false, catalogRoot),
                    cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return ConformanceComparator.Aborted($"Case timed out after {_timeoutMs / 1000}s");
            }

            // Compare
            return ConformanceComparator.Compare(expectedSnapshot, actualSnapshot);
        }
        catch (OperationCanceledException)
        {
            throw; // propagate parent cancellation
        }
        catch (Exception ex)
        {
            return ConformanceComparator.Aborted($"Execution error: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
