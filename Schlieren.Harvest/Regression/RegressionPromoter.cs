namespace Schlieren.Harvest.Regression;

/// <summary>
/// A provenance record for a promoted regression fixture.
/// Records the chain of evidence from divergence to permanent test.
/// </summary>
public sealed record RegressionProvenance(
    string SourceRunId,
    string SourceCaseId,
    string FamilyId,
    string RepairOrderId,
    string RepairCommitSha,
    string PromotedFixturePath,
    string PromotedTestName,
    DateTime PromotedUtc);

/// <summary>
/// Promotes a fixed representative into a permanent regression fixture with full provenance.
///
/// Contracts:
///   - Promotion occurs only AFTER a human-approved repair order identifies its representative.
///   - Records source run, case, family, repair, and commit.
///   - Never changes expected values or approves a fix automatically.
///   - Only copies minimized data — does not re-execute.
/// </summary>
public static class RegressionPromoter
{
    /// <summary>
    /// Validates and records the provenance for a regression promotion.
    ///
    /// Does NOT copy the fixture file itself — that's the CLI's job after validation.
    /// This method enforces provenance requirements only.
    ///
    /// Throws <see cref="InvalidOperationException"/> if:
    ///   - RepairOrderId is missing or empty.
    ///   - RepairCommitSha is missing or empty.
    ///   - SourceRunId is missing or empty.
    /// </summary>
    public static RegressionProvenance Promote(
        string sourceRunId,
        string sourceCaseId,
        string familyId,
        string repairOrderId,
        string repairCommitSha,
        string promotedFixturePath,
        string promotedTestName)
    {
        if (string.IsNullOrWhiteSpace(sourceRunId))
            throw new InvalidOperationException("Source run ID is required for promotion.");
        if (string.IsNullOrWhiteSpace(sourceCaseId))
            throw new InvalidOperationException("Source case ID is required for promotion.");
        if (string.IsNullOrWhiteSpace(repairOrderId))
            throw new InvalidOperationException("Repair order ID is required for promotion.");
        if (string.IsNullOrWhiteSpace(repairCommitSha))
            throw new InvalidOperationException("Repair commit SHA is required for promotion.");
        if (string.IsNullOrWhiteSpace(promotedFixturePath))
            throw new InvalidOperationException("Promoted fixture path is required.");
        if (string.IsNullOrWhiteSpace(promotedTestName))
            throw new InvalidOperationException("Promoted test name is required.");

        return new RegressionProvenance(
            SourceRunId:         sourceRunId,
            SourceCaseId:        sourceCaseId,
            FamilyId:            familyId,
            RepairOrderId:       repairOrderId,
            RepairCommitSha:     repairCommitSha,
            PromotedFixturePath: promotedFixturePath,
            PromotedTestName:    promotedTestName,
            PromotedUtc:         DateTime.UtcNow);
    }
}
