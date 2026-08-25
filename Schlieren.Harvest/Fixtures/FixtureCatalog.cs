namespace Schlieren.Harvest.Fixtures;

/// <summary>
/// Indexes and validates EELS fixture files within a declared root directory.
///
/// Path containment contract: every admitted file must resolve to a path inside
/// <see cref="Root"/> when canonicalized with <see cref="Path.GetFullPath"/>.
/// A path that resolves outside the root — whether by traversal or absolute
/// specification — is rejected with <see cref="AdmissionReasonCode.OutsideRoot"/>.
///
/// Duplicate detection is per <see cref="Admit"/> call — a new call resets
/// the seen-ID set so multiple independent catalogs can be constructed.
/// </summary>
public sealed class FixtureCatalog
{
    public string Root { get; }

    private readonly string _canonicalRoot;

    public FixtureCatalog(string root)
    {
        Root           = root;
        _canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Admits each supplied path, returning one <see cref="FixtureCaseMetadata"/>
    /// per (file, caseId) pair found.  Results are returned in input-file ordinal
    /// order, with cases within each file in JSON document order.
    /// </summary>
    public IReadOnlyList<FixtureCaseMetadata> Admit(IEnumerable<string> absolutePaths)
    {
        var results   = new List<FixtureCaseMetadata>();
        var seenCaseIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawPath in absolutePaths)
        {
            // 1. Root must exist
            if (!Directory.Exists(_canonicalRoot))
            {
                results.Add(new FixtureCaseMetadata(
                    CaseId:      "",
                    RelativePath: SafeRelative(rawPath),
                    SourceSha256: "",
                    Fork:         "",
                    Dimensions:   new HashSet<StorageDimension>(),
                    Admission:    AdmissionReasonCode.MissingRoot,
                    Detail:       $"Root directory does not exist: {_canonicalRoot}"));
                continue;
            }

            // 2. Path must be inside root
            string canonical;
            try { canonical = Path.GetFullPath(rawPath); }
            catch (Exception ex)
            {
                results.Add(Stub(rawPath, AdmissionReasonCode.OutsideRoot, ex.Message));
                continue;
            }

            if (!IsInsideRoot(canonical))
            {
                results.Add(Stub(rawPath, AdmissionReasonCode.OutsideRoot,
                    $"Resolved path {canonical} is outside root {_canonicalRoot}"));
                continue;
            }

            // 3. File must exist
            if (!File.Exists(canonical))
            {
                results.Add(Stub(rawPath, AdmissionReasonCode.MalformedJson, "File not found"));
                continue;
            }

            // 4. Delegate to reader for JSON parsing + per-case validation
            var cases = EelsFixtureReader.ReadFile(canonical, _canonicalRoot, seenCaseIds);
            results.AddRange(cases);
        }

        return results;
    }

    private bool IsInsideRoot(string canonicalPath)
    {
        // Normalize separators for comparison
        var normPath = canonicalPath.Replace('\\', '/');
        var normRoot = _canonicalRoot.Replace('\\', '/');
        return normPath.StartsWith(normRoot + "/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normPath, normRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string SafeRelative(string rawPath)
    {
        try
        {
            var canon = Path.GetFullPath(rawPath).Replace('\\', '/');
            var root  = _canonicalRoot.Replace('\\', '/') + "/";
            return canon.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? canon[root.Length..] : canon;
        }
        catch { return rawPath; }
    }

    private static FixtureCaseMetadata Stub(string rawPath, AdmissionReasonCode code, string? detail)
        => new("", rawPath.Replace('\\', '/'), "", "", new HashSet<StorageDimension>(), code, detail);
}
