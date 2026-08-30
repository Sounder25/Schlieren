namespace Schlieren.Harvest.Campaigns;

/// <summary>
/// One mandatory quota within a campaign family. A case belongs to the stratum
/// when its path or case ID contains every required keyword and none of the
/// excluded keywords.
/// </summary>
public sealed record CampaignSelectionStratum(
    string Name,
    int Count,
    IReadOnlyList<string> RequiredKeywords,
    IReadOnlyList<string> ExcludedKeywords);

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
    IReadOnlyList<string> ScoreDimensions,
    string SelectionPolicyVersion = CampaignManifest.CurrentSelectionPolicyVersion,
    /// <summary>
    /// Optional exact quotas for focused campaigns. Families without strata
    /// retain the original greedy set-cover behavior.
    /// </summary>
    IReadOnlyList<CampaignSelectionStratum>? SelectionStrata = null)
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

    // ── Wave 2: focused precompile campaigns ─────────────────────────────

    /// <summary>Campaign 8: EIP-2537 BLS12-381 G1ADD validation.</summary>
    public static readonly CampaignFamilyPolicy PrecompilesBls12G1Add = new(
        "precompiles-bls12-g1add", "1",
        "EIP-2537 BLS12-381 G1ADD valid inputs, invalid encodings, call types, gas, and activation",
        PathFilters: ["bls12_g1add", "bls12_precompiles_before_fork"],
        ScoreDimensions: [],
        SelectionPolicyVersion: "stratified-v1",
        SelectionStrata:
        [
            new("valid-prague", 7,
                ["test_bls12_g1add.py::test_valid[", "fork_Prague"], []),
            new("valid-osaka", 8,
                ["test_bls12_g1add.py::test_valid[", "fork_Osaka"], []),
            new("invalid-prague", 9,
                ["test_bls12_g1add.py::test_invalid[", "fork_Prague"], []),
            new("invalid-osaka", 9,
                ["test_bls12_g1add.py::test_invalid[", "fork_Osaka"], []),
            new("call-types-prague", 6,
                ["test_bls12_g1add.py::test_call_types[", "fork_Prague"], []),
            new("call-types-osaka", 6,
                ["test_bls12_g1add.py::test_call_types[", "fork_Osaka"], []),
            new("gas-prague", 2,
                ["test_bls12_g1add.py::test_gas[", "fork_Prague"], []),
            new("gas-osaka", 2,
                ["test_bls12_g1add.py::test_gas[", "fork_Osaka"], []),
            new("before-fork-g1add", 1,
                ["test_bls12_precompiles_before_fork.py::test_precompile_before_fork[", "fork_Cancun", "G1ADD"], [])
        ]);

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
            ["precompiles-bls12-g1add"] = PrecompilesBls12G1Add,
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

        if (SelectionStrata is { Count: > 0 })
            return TrySelectStratified(eligible, count);

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

    private SelectionResult TrySelectStratified(
        IReadOnlyList<Fixtures.FixtureCaseMetadata> eligible,
        int requestedCount)
    {
        var strata = SelectionStrata
            ?? throw new InvalidOperationException("Stratified selection requires configured strata.");
        var quotaTotal = strata.Sum(stratum => stratum.Count);
        if (quotaTotal != requestedCount)
        {
            return Insufficient(
                requestedCount,
                quotaTotal,
                $"Campaign '{FamilyName}' strata require {quotaTotal} cases, not the requested {requestedCount}.");
        }

        var selected = new List<Fixtures.FixtureCaseMetadata>(requestedCount);
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stratum in strata)
        {
            var candidates = eligible
                .Where(candidate =>
                    stratum.RequiredKeywords.All(keyword => CaseContains(candidate, keyword)) &&
                    stratum.ExcludedKeywords.All(keyword => !CaseContains(candidate, keyword)) &&
                    !selectedIds.Contains(candidate.CaseId))
                .OrderBy(candidate => candidate.CaseId, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count < stratum.Count)
            {
                return Insufficient(
                    stratum.Count,
                    candidates.Count,
                    $"Stratum '{stratum.Name}' has {candidates.Count} eligible cases; " +
                    $"its fixed quota is {stratum.Count}. Do not borrow from another stratum.");
            }

            foreach (var candidate in SelectEvenly(candidates, stratum.Count))
            {
                if (!selectedIds.Add(candidate.CaseId))
                {
                    return Insufficient(
                        stratum.Count,
                        candidates.Count,
                        $"Stratum '{stratum.Name}' overlaps a previously selected case: {candidate.CaseId}.");
                }

                selected.Add(candidate);
            }
        }

        return new SelectionResult(IsSuccess: true, Cases: selected, InsufficientReport: null);
    }

    private static IEnumerable<Fixtures.FixtureCaseMetadata> SelectEvenly(
        IReadOnlyList<Fixtures.FixtureCaseMetadata> candidates,
        int count)
    {
        if (count == 0)
            yield break;

        if (count == 1)
        {
            yield return candidates[0];
            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            var index = i * (candidates.Count - 1) / (count - 1);
            yield return candidates[index];
        }
    }

    private static SelectionResult Insufficient(int requested, int available, string reason) =>
        new(
            IsSuccess: false,
            Cases: null,
            InsufficientReport: new InsufficientCoverageReport(
                RequestedCount: requested,
                AvailableCount: available,
                Reason: reason));

    private int ScoreCase(Fixtures.FixtureCaseMetadata c, HashSet<string> covered) =>
        ScoreDimensions.Count(d => !covered.Contains(d) && CaseContains(c, d));

    private static bool CaseContains(Fixtures.FixtureCaseMetadata c, string keyword) =>
        c.RelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        c.CaseId.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
