using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Fixtures;

namespace Schlieren.Harvest.Tests.Campaigns;

public sealed class SubprocessCaseWorkerTests
{
    [Theory]
    [InlineData(ApparatusFailureKind.OracleTimeout, CaseStatus.HarnessError)]
    [InlineData(ApparatusFailureKind.OracleExit, CaseStatus.HarnessError)]
    [InlineData(ApparatusFailureKind.OracleProtocol, CaseStatus.HarnessError)]
    [InlineData(ApparatusFailureKind.WorkerTimeout, CaseStatus.Aborted)]
    [InlineData(ApparatusFailureKind.WorkerCrash, CaseStatus.Aborted)]
    [InlineData(ApparatusFailureKind.WorkerProtocol, CaseStatus.Aborted)]
    [InlineData(ApparatusFailureKind.Cancelled, CaseStatus.Aborted)]
    public void StatusForApparatusFailure_UsesProductionClassification(
        ApparatusFailureKind failure,
        CaseStatus expected)
    {
        Assert.Equal(expected, SubprocessCaseWorker.StatusForApparatusFailure(failure));
    }

    [Theory]
    [InlineData(ApparatusFailureKind.OracleTimeout, true, -1, CaseStatus.HarnessError)]
    [InlineData(ApparatusFailureKind.OracleExit, false, 17, CaseStatus.HarnessError)]
    [InlineData(ApparatusFailureKind.Cancelled, false, -1, CaseStatus.Aborted)]
    public async Task ExecuteCaseAsync_PreservesTypedOracleFailureEvidence(
        ApparatusFailureKind failure,
        bool timedOut,
        int exitCode,
        CaseStatus expectedStatus)
    {
        var workerPath = Path.GetTempFileName();
        var fixturePath = Path.GetTempFileName();
        try
        {
            var evidence = new ExecutionAttemptEvidence(
                failure, TimeSpan.FromMilliseconds(250), exitCode,
                new string('a', 64), new string('b', 64), true, new string('c', 64));
            var oracle = new StubOracle(new OracleRunResult(
                "oracle stdout", "oracle stderr", exitCode, timedOut, evidence));
            var worker = new SubprocessCaseWorker(workerPath, oracle);
            var manifestCase = new ManifestCase(
                "case-id", fixturePath, "fixture-sha", "Berlin",
                Array.Empty<StorageDimension>());

            ComparisonResult result = await worker.ExecuteCaseAsync(
                manifestCase, Path.GetDirectoryName(fixturePath)!, "manifest-hash");

            Assert.Equal(expectedStatus, result.Status);
            Assert.Equal(evidence, result.AttemptEvidence);
        }
        finally
        {
            File.Delete(workerPath);
            File.Delete(fixturePath);
        }
    }

    private sealed class StubOracle(OracleRunResult result) : IReferenceOracle
    {
        public Task<OracleRunResult> RunAsync(
            string fixturePath,
            CancellationToken ct = default) => Task.FromResult(result);
    }
}
