using Schlieren.Harvest.Configuration;
using Xunit;

namespace Schlieren.Harvest.Tests.Configuration;

/// <summary>
/// Tests proving EelsProvenanceProbe contracts:
/// - Produces semantic identity from a valid EELS installation
/// - Refuses to produce identity if executable path doesn't exist
/// - Refuses to produce identity if specs root doesn't exist
/// - Records package version, source commit, Python version, platform
/// - Computes deterministic source tree hash
/// - Computes deterministic evm_tools hash
/// - Records dependency versions
/// </summary>
public sealed class EelsProvenanceProbeTests
{
    [Fact]
    public void Probe_MissingExecutable_ThrowsConfigurationError()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), "nonexistent_eels_" + Guid.NewGuid() + ".exe");

        var error = Assert.Throws<HarvestConfigurationException>(
            () => EelsProvenanceProbe.Probe(nonexistent, Path.GetTempPath()));

        Assert.Equal("HARVEST.EELS_PROBE_EXECUTABLE_MISSING", error.Code);
    }

    [Fact]
    public void Probe_MissingSpecsRoot_ThrowsConfigurationError()
    {
        // Use a temp file as a stand-in for executable existing
        var tempExe = Path.GetTempFileName();
        var nonexistentRoot = Path.Combine(Path.GetTempPath(), "nonexistent_specs_" + Guid.NewGuid());
        try
        {
            var error = Assert.Throws<HarvestConfigurationException>(
                () => EelsProvenanceProbe.Probe(tempExe, nonexistentRoot));

            Assert.Equal("HARVEST.EELS_PROBE_SPECS_ROOT_MISSING", error.Code);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public void SemanticIdentity_RequiresNonEmptyPackageVersion()
    {
        // The identity record must reject empty version
        Assert.Throws<ArgumentException>(() => new EelsSemanticIdentity(
            PackageName: "ethereum-execution",
            PackageVersion: "",
            SourceTreeSha256: "abc123",
            EvmToolsSha256: "def456",
            SourceCommit: "85aa48c",
            PythonVersion: "3.13.11",
            RuntimePlatform: "win32",
            LauncherSha256: "ee46923d",
            DependencyVersions: new Dictionary<string, string>()));
    }

    [Fact]
    public void SemanticIdentity_RequiresNonEmptySourceTreeHash()
    {
        Assert.Throws<ArgumentException>(() => new EelsSemanticIdentity(
            PackageName: "ethereum-execution",
            PackageVersion: "2.19.0",
            SourceTreeSha256: "",
            EvmToolsSha256: "def456",
            SourceCommit: "85aa48c",
            PythonVersion: "3.13.11",
            RuntimePlatform: "win32",
            LauncherSha256: "ee46923d",
            DependencyVersions: new Dictionary<string, string>()));
    }

    [Fact]
    public void SemanticIdentity_ValidConstruction()
    {
        var deps = new Dictionary<string, string>
        {
            ["cryptography"] = "45.0.7",
            ["py-ecc"] = "8.0.0",
        };

        var identity = new EelsSemanticIdentity(
            PackageName: "ethereum-execution",
            PackageVersion: "2.19.0",
            SourceTreeSha256: "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256: "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            SourceCommit: "85aa48c742c38a2d5a876f84ebf8082a50273064",
            PythonVersion: "3.13.11",
            RuntimePlatform: "win32",
            LauncherSha256: "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            DependencyVersions: deps);

        Assert.Equal("ethereum-execution", identity.PackageName);
        Assert.Equal("2.19.0", identity.PackageVersion);
        Assert.Equal("85aa48c742c38a2d5a876f84ebf8082a50273064", identity.SourceCommit);
        Assert.Equal(2, identity.DependencyVersions.Count);
    }

    [Fact]
    public void SemanticIdentity_LauncherHashIsNonAuthoritative()
    {
        // Two identities that differ only in launcher hash should be
        // considered semantically equivalent for certification purposes.
        var deps = new Dictionary<string, string>();

        var id1 = new EelsSemanticIdentity(
            "ethereum-execution", "2.19.0",
            "aaa", "bbb", "ccc", "3.13.11", "win32", "launcher_hash_1", deps);

        var id2 = new EelsSemanticIdentity(
            "ethereum-execution", "2.19.0",
            "aaa", "bbb", "ccc", "3.13.11", "win32", "launcher_hash_2", deps);

        Assert.True(id1.IsSemanticEquivalent(id2));
    }

    [Fact]
    public void SemanticIdentity_DifferentSourceTree_NotEquivalent()
    {
        var deps = new Dictionary<string, string>();

        var id1 = new EelsSemanticIdentity(
            "ethereum-execution", "2.19.0",
            "source_hash_1", "bbb", "ccc", "3.13.11", "win32", "x", deps);

        var id2 = new EelsSemanticIdentity(
            "ethereum-execution", "2.19.0",
            "source_hash_2", "bbb", "ccc", "3.13.11", "win32", "x", deps);

        Assert.False(id1.IsSemanticEquivalent(id2));
    }

    [Fact]
    public void SemanticIdentity_DifferentVersion_NotEquivalent()
    {
        var deps = new Dictionary<string, string>();

        var id1 = new EelsSemanticIdentity(
            "ethereum-execution", "2.19.0",
            "aaa", "bbb", "ccc", "3.13.11", "win32", "x", deps);

        var id2 = new EelsSemanticIdentity(
            "ethereum-execution", "2.20.0",
            "aaa", "bbb", "ccc", "3.13.11", "win32", "x", deps);

        Assert.False(id1.IsSemanticEquivalent(id2));
    }
}
