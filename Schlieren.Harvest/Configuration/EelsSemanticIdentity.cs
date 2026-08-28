namespace Schlieren.Harvest.Configuration;

/// <summary>
/// Semantic identity of an EELS oracle installation.
/// The launcher SHA-256 is retained as metadata but is NOT authoritative
/// for certification—it changes on venv recreation without semantic change.
/// </summary>
public sealed class EelsSemanticIdentity
{
    public string PackageName { get; }
    public string PackageVersion { get; }
    public string SourceTreeSha256 { get; }
    public string EvmToolsSha256 { get; }
    public string SourceCommit { get; }
    public string PythonVersion { get; }
    public string RuntimePlatform { get; }
    public string LauncherSha256 { get; }
    public IReadOnlyDictionary<string, string> DependencyVersions { get; }

    public EelsSemanticIdentity(
        string PackageName,
        string PackageVersion,
        string SourceTreeSha256,
        string EvmToolsSha256,
        string SourceCommit,
        string PythonVersion,
        string RuntimePlatform,
        string LauncherSha256,
        IReadOnlyDictionary<string, string> DependencyVersions)
    {
        if (string.IsNullOrWhiteSpace(PackageVersion))
            throw new ArgumentException("PackageVersion must not be empty.", nameof(PackageVersion));
        if (string.IsNullOrWhiteSpace(SourceTreeSha256))
            throw new ArgumentException("SourceTreeSha256 must not be empty.", nameof(SourceTreeSha256));

        this.PackageName = PackageName;
        this.PackageVersion = PackageVersion;
        this.SourceTreeSha256 = SourceTreeSha256;
        this.EvmToolsSha256 = EvmToolsSha256;
        this.SourceCommit = SourceCommit;
        this.PythonVersion = PythonVersion;
        this.RuntimePlatform = RuntimePlatform;
        this.LauncherSha256 = LauncherSha256;
        this.DependencyVersions = DependencyVersions;
    }

    /// <summary>
    /// Two identities are semantically equivalent if their source content,
    /// version, and tools match — regardless of launcher hash, Python minor
    /// version, or platform (pure-python package).
    /// </summary>
    public bool IsSemanticEquivalent(EelsSemanticIdentity other)
    {
        if (other is null) return false;
        return string.Equals(PackageVersion, other.PackageVersion, StringComparison.Ordinal) &&
               string.Equals(SourceTreeSha256, other.SourceTreeSha256, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(EvmToolsSha256, other.EvmToolsSha256, StringComparison.OrdinalIgnoreCase);
    }
}
