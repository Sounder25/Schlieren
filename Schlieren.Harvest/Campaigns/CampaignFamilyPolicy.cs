namespace Schlieren.Harvest.Campaigns;

/// <summary>
/// Describes the selection policy for a named campaign family.
///
/// Each family defines:
///   - Path keywords: fixture relative paths must contain at least one to be eligible
///   - Dimension keywords: scoring dimensions extracted from the fixture path/CaseId
///     for the greedy set-cover algorithm
///
/// The selector scores each admitted case by counting how many dimension keywords
/// appear in its relative path or CaseId that haven't been covered yet, then
/// uses ordinal CaseId as a tie-breaker for determinism.
///
/// This is path-keyword based rather than opcode-byte based, which is appropriate
/// for campaigns targeting specific EIP subdirectories where the relevant
/// opcodes/semantics are well-represented by the fixture path structure.
/// </summary>
public sealed record CampaignFamilyPolicy(
    string   FamilyName,
    string   FamilyVersion,
    string   Description,
    /// <summary>
    /// Fixture paths must contain at least one of these substrings (case-insensitive)
    /// to be eligible for this campaign. Empty = all admitted fixtures are eligible.
    /// </summary>
    IReadOnlyList<string> PathFilters,
    /// <summary>
    /// Scoring dimensions: each is a keyword that may appear in the fixture path/CaseId.
    /// The greedy set-cover maximises the breadth of distinct keywords covered.
    /// </summary>
    IReadOnlyList<string> ScoreDimensions)
{
    // ── Known campaign families ────────────────────────────────────────────

    /// <summary>Campaign 1: Storage lifecycle (SSTORE/SLOAD, EIP-2929 warm/cold).</summary>
    public static readonly CampaignFamilyPolicy StorageLifecycle = new(
        "storage-lifecycle", "1",
        "SSTORE/SLOAD lifecycle, EIP-2929 warm/cold access, value transitions, call geometry",
        PathFilters: new[] { "sstore", "sload", "storage", "eip2929", "eip1153_tstore" },
        ScoreDimensions: new[] {
            "sstore", "sload", "warm", "cold", "zero", "nonzero", "revert", "rollback",
            "delegatecall", "staticcall", "callcode", "nested", "refund", "berlin", "london"
        });

    /// <summary>Campaign 2: Return data (RETURNDATASIZE, RETURNDATACOPY, REVERT semantics).</summary>
    public static readonly CampaignFamilyPolicy ReturnData = new(
        "return-data", "1",
        "RETURNDATASIZE/RETURNDATACOPY, REVERT with data, return-data propagation across frames",
        PathFilters: new[] { "eip211", "return_data", "returndata", "revert", "stReturnDataTest" },
        ScoreDimensions: new[] {
            "returndatasize", "returndatacopy", "revert", "return_data", "create",
            "staticcall", "delegatecall", "call", "nested", "empty", "overflow"
        });

    /// <summary>Campaign 3: Call semantics (CALL/STATICCALL/DELEGATECALL frame isolation).</summary>
    public static readonly CampaignFamilyPolicy CallSemantics = new(
        "call-semantics", "1",
        "CALL/STATICCALL/DELEGATECALL/CALLCODE frame isolation, gas forwarding, value transfer",
        PathFilters: new[] {
            "stCallCodes", "stCallDelegate", "eip214", "staticcall",
            "stSystemOperations", "stCallCreateCallCode", "stDelegatecall"
        },
        ScoreDimensions: new[] {
            "staticcall", "delegatecall", "callcode", "call", "value", "gas",
            "revert", "oog", "nested", "precompile", "eoa", "depth"
        });

    /// <summary>Campaign 4: CREATE/CREATE2 address derivation, nonce, collision.</summary>
    public static readonly CampaignFamilyPolicy CreateSemantics = new(
        "create-semantics", "1",
        "CREATE/CREATE2 address derivation, nonce semantics, collision, initcode gas",
        PathFilters: new[] { "stCreate2", "eip1014", "create2", "create", "stCreateTest" },
        ScoreDimensions: new[] {
            "create2", "create", "collision", "nonce", "initcode", "revert",
            "value", "codesize", "oog", "salt", "address"
        });

    /// <summary>Campaign 5: SELFDESTRUCT EIP-6780 Cancun semantics.</summary>
    public static readonly CampaignFamilyPolicy SelfDestruct = new(
        "selfdestruct", "1",
        "SELFDESTRUCT EIP-6780: same-transaction vs different-transaction, Cancun semantics",
        PathFilters: new[] { "eip6780", "selfdestruct", "stSelfDestructTest" },
        ScoreDimensions: new[] {
            "selfdestruct", "same_tx", "different_tx", "cancun", "balance",
            "zero", "nested", "revert", "create", "multiple"
        });

    /// <summary>Campaign 6: Transient storage TSTORE/TLOAD (EIP-1153).</summary>
    public static readonly CampaignFamilyPolicy TransientStorage = new(
        "transient-storage", "1",
        "TSTORE/TLOAD EIP-1153: per-transaction lifetime, revert behavior, frame isolation",
        PathFilters: new[] { "eip1153", "tstore", "tload", "tstorage" },
        ScoreDimensions: new[] {
            "tstore", "tload", "revert", "reentry", "delegatecall", "staticcall",
            "create", "cleared", "nested", "reentrancy", "context"
        });

    /// <summary>Campaign 7: Access lists + EIP-1559 fee market.</summary>
    public static readonly CampaignFamilyPolicy AccessListFeeMarket = new(
        "access-list-fee-market", "1",
        "EIP-2930 access lists, EIP-1559 fee market, warm/cold slot effects",
        PathFilters: new[] { "eip2930", "eip1559", "access_list", "fee_market" },
        ScoreDimensions: new[] {
            "access_list", "slot", "warmth", "address", "refund",
            "basefee", "priority", "maxfee", "intrinsic", "type_1", "type_2"
        });

    // ── Wave 2: Precompile + Gas campaigns ────────────────────────────────

    /// <summary>Campaign 8: BLS12-381 precompiles (EIP-2537, Prague).</summary>
    public static readonly CampaignFamilyPolicy PrecompilesBls12 = new(
        "precompiles-bls12", "1",
        "EIP-2537 BLS12-381 precompiles: G1/G2 add, mul, MSM, pairing, map-to-curve",
        PathFilters: new[] { "eip2537", "bls12" },
        ScoreDimensions: new[] {
            "g1add", "g1mul", "g1msm", "g2add", "g2mul", "g2msm",
            "pairing", "map_fp_to_g1", "map_fp2_to_g2",
            "valid", "invalid", "gas", "call_types", "isogeny",
            "variable_length", "before_fork", "zero_length", "multi_inf"
        });

    // ── Lookup ─────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, CampaignFamilyPolicy> _all =
        new Dictionary<string, CampaignFamilyPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["storage-lifecycle"]        = StorageLifecycle,
            ["return-data"]              = ReturnData,
            ["call-semantics"]           = CallSemantics,
            ["create-semantics"]         = CreateSemantics,
            ["selfdestruct"]             = SelfDestruct,
            ["transient-storage"]        = TransientStorage,
            ["access-list-fee-market"]   = AccessListFeeMarket,
            ["precompiles-bls12"]        = PrecompilesBls12,
        };

    /// <summary>
    /// Returns the policy for a given family name, or null if not found.
    /// </summary>
    public static CampaignFamilyPolicy? TryGet(string familyName) =>
        _all.TryGetValue(familyName, out var policy) ? policy : null;

    /// <summary>Returns all registered campaign family names.</summary>
    public static IReadOnlyCollection<string> AllFamilyNames => _all.Keys.ToList();

    // ── Selection helper ────────────────────────────────────────────────────

    /// <summary>
    /// Filters an admitted case list to those eligible for this campaign,
    /// then scores each case by how many uncovered ScoreDimensions it would add.
    /// Returns the top <paramref name="count"/> cases via greedy set-cover.
    /// </summary>
    public SelectionResult TrySelect(
        IReadOnlyList<Fixtures.FixtureCaseMetadata> admittedCases,
        int count)
    {
        // Filter to eligible cases (path must contain at least one PathFilter keyword)
        var eligible = PathFilters.Count == 0
            ? admittedCases.Where(c => c.Admission == Fixtures.AdmissionReasonCode.Admitted).ToList()
            : admittedCases.Where(c =>
                c.Admission == Fixtures.AdmissionReasonCode.Admitted &&
                PathFilters.Any(f =>
                    c.RelativePath.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    c.CaseId.Contains(f, StringComparison.OrdinalIgnoreCase)))
              .ToList();

        if (eligible.Count < count)
            return new SelectionResult(
                IsSuccess: false, Cases: null,
                InsufficientReport: new InsufficientCoverageReport(
                    RequestedCount: count,
                    AvailableCount: eligible.Count,
                    Reason: $"Only {eligible.Count} cases match the '{FamilyName}' path filter; " +
                            $"need {count}. Do not weaken the filter."));

        // Greedy set-cover over ScoreDimensions (keyword presence in path/CaseId)
        var selected = new List<Fixtures.FixtureCaseMetadata>(count);
        var covered  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = new List<Fixtures.FixtureCaseMetadata>(eligible);

        while (selected.Count < count && remaining.Count > 0)
        {
            var best = remaining
                .OrderByDescending(c => ScoreCase(c, covered))
                .ThenBy(c => c.CaseId, StringComparer.Ordinal)
                .First();

            selected.Add(best);
            foreach (var dim in ScoreDimensions)
            {
                if (CaseContains(best, dim))
                    covered.Add(dim);
            }
            remaining.Remove(best);
        }

        return new SelectionResult(IsSuccess: true, Cases: selected, InsufficientReport: null);
    }

    private int ScoreCase(Fixtures.FixtureCaseMetadata c, HashSet<string> covered) =>
        ScoreDimensions.Count(d => !covered.Contains(d) && CaseContains(c, d));

    private static bool CaseContains(Fixtures.FixtureCaseMetadata c, string keyword) =>
        c.RelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        c.CaseId.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
