using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Schlieren.Harvest.Configuration;

/// <summary>
/// Semantic identity of an EELS oracle installation.
///
/// For v2 certification, ALL fields are required (non-null, non-empty).
/// <see cref="ValidateForCertification"/> reports incomplete fields and dirty state
/// without throwing, so the caller can collect all problems before refusing.
///
/// <see cref="CanonicalHash"/> is a SHA-256 of the deterministic canonical JSON
/// representation of every field except CanonicalHash itself, with dependency keys
/// sorted by <see cref="StringComparer.Ordinal"/>. It is computed once at construction
/// and stored in the v2 certificate to bind the issued certificate to this exact
/// identity snapshot.
///
/// The launcher SHA-256 is retained as metadata — it changes on venv recreation
/// without semantic change — but IS included in the canonical hash because v2
/// certification requires binding the exact observed runtime.
/// </summary>
public sealed class EelsSemanticIdentity
{
    public string PackageName { get; }
    public string PackageVersion { get; }
    public string SourceRepository { get; }
    public string SourceCommit { get; }
    public string SourceTreeSha256 { get; }
    public string EvmToolsSha256 { get; }
    public string UvLockSha256 { get; }
    public string PyprojectTomlSha256 { get; }
    public string PythonImplementation { get; }
    public string PythonVersion { get; }
    public string RuntimePlatform { get; }
    public string InstallMode { get; }
    public string DistributionArtifactSha256 { get; }
    public string LauncherSha256 { get; }
    public IReadOnlyDictionary<string, string> DependencyVersions { get; }
    public bool IsCleanCheckout { get; }

    /// <summary>
    /// SHA-256 of the canonical JSON representation of this identity.
    /// Computed once at construction; stored in the certificate to bind
    /// the issued certificate to this exact identity snapshot.
    /// Does not hash itself.
    /// </summary>
    public string CanonicalHash { get; }

    /// <summary>
    /// Constructs a semantic identity. No parameter has a default value.
    /// The caller must supply every field explicitly.
    /// </summary>
    public EelsSemanticIdentity(
        string PackageName,
        string PackageVersion,
        string SourceRepository,
        string SourceCommit,
        string SourceTreeSha256,
        string EvmToolsSha256,
        string UvLockSha256,
        string PyprojectTomlSha256,
        string PythonImplementation,
        string PythonVersion,
        string RuntimePlatform,
        string InstallMode,
        string DistributionArtifactSha256,
        string LauncherSha256,
        bool IsCleanCheckout,
        IReadOnlyDictionary<string, string> DependencyVersions)
    {
        this.PackageName = PackageName;
        this.PackageVersion = PackageVersion;
        this.SourceRepository = SourceRepository;
        this.SourceCommit = SourceCommit;
        this.SourceTreeSha256 = SourceTreeSha256;
        this.EvmToolsSha256 = EvmToolsSha256;
        this.UvLockSha256 = UvLockSha256;
        this.PyprojectTomlSha256 = PyprojectTomlSha256;
        this.PythonImplementation = PythonImplementation;
        this.PythonVersion = PythonVersion;
        this.RuntimePlatform = RuntimePlatform;
        this.InstallMode = InstallMode;
        this.DistributionArtifactSha256 = DistributionArtifactSha256;
        this.LauncherSha256 = LauncherSha256;
        this.IsCleanCheckout = IsCleanCheckout;
        this.DependencyVersions = DependencyVersions is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(DependencyVersions, StringComparer.Ordinal);

        this.CanonicalHash = ComputeCanonicalHash();
    }

    /// <summary>
    /// Validates that this identity has all fields required for v2 certification.
    /// Reports incomplete fields and dirty checkout without throwing.
    /// Returns a list of problem field names, or empty if certification-ready.
    /// </summary>
    public IReadOnlyList<string> ValidateForCertification()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(PackageName)) problems.Add(nameof(PackageName));
        if (string.IsNullOrWhiteSpace(PackageVersion)) problems.Add(nameof(PackageVersion));
        if (string.IsNullOrWhiteSpace(SourceRepository)) problems.Add(nameof(SourceRepository));
        if (string.IsNullOrWhiteSpace(SourceCommit)) problems.Add(nameof(SourceCommit));
        if (string.IsNullOrWhiteSpace(SourceTreeSha256)) problems.Add(nameof(SourceTreeSha256));
        if (string.IsNullOrWhiteSpace(EvmToolsSha256)) problems.Add(nameof(EvmToolsSha256));
        if (string.IsNullOrWhiteSpace(UvLockSha256)) problems.Add(nameof(UvLockSha256));
        if (string.IsNullOrWhiteSpace(PyprojectTomlSha256)) problems.Add(nameof(PyprojectTomlSha256));
        if (string.IsNullOrWhiteSpace(PythonImplementation)) problems.Add(nameof(PythonImplementation));
        if (string.IsNullOrWhiteSpace(PythonVersion)) problems.Add(nameof(PythonVersion));
        if (string.IsNullOrWhiteSpace(RuntimePlatform)) problems.Add(nameof(RuntimePlatform));
        if (string.IsNullOrWhiteSpace(InstallMode)) problems.Add(nameof(InstallMode));
        if (string.IsNullOrWhiteSpace(DistributionArtifactSha256)) problems.Add(nameof(DistributionArtifactSha256));
        if (string.IsNullOrWhiteSpace(LauncherSha256)) problems.Add(nameof(LauncherSha256));
        if (!IsCleanCheckout) problems.Add(nameof(IsCleanCheckout));

        // Dependency collection: null or empty is incomplete provenance
        if (DependencyVersions is null || DependencyVersions.Count == 0)
        {
            problems.Add(nameof(DependencyVersions));
        }
        else
        {
            // Every dependency must have a non-blank name and version
            foreach (var kv in DependencyVersions)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    problems.Add("DependencyVersions[blank-key]");
                if (string.IsNullOrWhiteSpace(kv.Value))
                    problems.Add($"DependencyVersions[{kv.Key}]");
            }
        }

        return problems;
    }

    /// <summary>
    /// Two identities are semantically equivalent if their source content,
    /// version, and tools match — regardless of launcher hash, Python minor
    /// version, or platform (pure-python package).
    /// Retained as a diagnostic helper. Certification must use exact
    /// <see cref="CanonicalHash"/> equality.
    /// </summary>
    public bool IsSemanticEquivalent(EelsSemanticIdentity other)
    {
        if (other is null) return false;
        return string.Equals(PackageVersion, other.PackageVersion, StringComparison.Ordinal) &&
               string.Equals(SourceTreeSha256, other.SourceTreeSha256, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(EvmToolsSha256, other.EvmToolsSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether this identity binds to the given <see cref="Campaigns.EelsIdentity"/> (the thin v1 record).
    /// The thin identity's version, launcher SHA, and commit must match this semantic identity's.
    /// </summary>
    public bool BindsTo(Campaigns.EelsIdentity thin)
    {
        if (thin is null) return false;
        return string.Equals(PackageVersion, thin.ReportedVersion, StringComparison.Ordinal) &&
               string.Equals(LauncherSha256, thin.ExecutableSha256, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(SourceCommit, thin.CommitSha, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deterministic canonical hash. Sorted keys, no whitespace, lowercased hex.
    /// Includes every field except CanonicalHash itself.
    /// Dependency keys sorted by <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    private string ComputeCanonicalHash()
    {
        // Sort dependency versions deterministically
        var sortedDeps = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (DependencyVersions is not null)
        {
            foreach (var kv in DependencyVersions)
                sortedDeps[kv.Key] = kv.Value;
        }

        var canonical = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["dependencyVersions"] = sortedDeps,
            ["distributionArtifactSha256"] = DistributionArtifactSha256 ?? "",
            ["evmToolsSha256"] = EvmToolsSha256 ?? "",
            ["installMode"] = InstallMode ?? "",
            ["isCleanCheckout"] = IsCleanCheckout,
            ["launcherSha256"] = LauncherSha256 ?? "",
            ["packageName"] = PackageName ?? "",
            ["packageVersion"] = PackageVersion ?? "",
            ["pyprojectTomlSha256"] = PyprojectTomlSha256 ?? "",
            ["pythonImplementation"] = PythonImplementation ?? "",
            ["pythonVersion"] = PythonVersion ?? "",
            ["runtimePlatform"] = RuntimePlatform ?? "",
            ["sourceCommit"] = SourceCommit ?? "",
            ["sourceRepository"] = SourceRepository ?? "",
            ["sourceTreeSha256"] = SourceTreeSha256 ?? "",
            ["uvLockSha256"] = UvLockSha256 ?? "",
        };

        var json = JsonSerializer.Serialize(canonical, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
