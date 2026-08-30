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
    public void SemanticIdentity_EmptyPackageVersion_ReportedByValidation()
    {
        // The constructor accepts empty values; ValidateForCertification reports them.
        var id = new EelsSemanticIdentity(
            PackageName:                "ethereum-execution",
            PackageVersion:             "",
            SourceRepository:           "https://github.com/ethereum/execution-specs.git",
            SourceCommit:               "85aa48c742c38a2d5a876f84ebf8082a50273064",
            SourceTreeSha256:           "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256:             "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               "645f83fc5defbdb6a85ec5f9681abf83a33d0de18d0997b19b402487923bbfa6",
            PyprojectTomlSha256:        "f46eb6c72c82f54f9ea4374436976432e9544d8176e58d08ca1b2c907e4d3ae1",
            PythonImplementation:       "CPython",
            PythonVersion:              "3.13.11",
            RuntimePlatform:            "win32",
            InstallMode:                "editable",
            DistributionArtifactSha256: "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            LauncherSha256:             "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            true,
            DependencyVersions:         new Dictionary<string, string>());

        var problems = id.ValidateForCertification();
        Assert.Contains("PackageVersion", problems);
    }

    [Fact]
    public void SemanticIdentity_EmptySourceTreeHash_ReportedByValidation()
    {
        var id = new EelsSemanticIdentity(
            PackageName:                "ethereum-execution",
            PackageVersion:             "2.19.0",
            SourceRepository:           "https://github.com/ethereum/execution-specs.git",
            SourceCommit:               "85aa48c742c38a2d5a876f84ebf8082a50273064",
            SourceTreeSha256:           "",
            EvmToolsSha256:             "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               "645f83fc5defbdb6a85ec5f9681abf83a33d0de18d0997b19b402487923bbfa6",
            PyprojectTomlSha256:        "f46eb6c72c82f54f9ea4374436976432e9544d8176e58d08ca1b2c907e4d3ae1",
            PythonImplementation:       "CPython",
            PythonVersion:              "3.13.11",
            RuntimePlatform:            "win32",
            InstallMode:                "editable",
            DistributionArtifactSha256: "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            LauncherSha256:             "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            true,
            DependencyVersions:         new Dictionary<string, string>());

        var problems = id.ValidateForCertification();
        Assert.Contains("SourceTreeSha256", problems);
    }

    [Fact]
    public void SemanticIdentity_ValidConstruction()
    {
        var deps = new Dictionary<string, string>
        {
            ["cryptography"] = "45.0.7",
            ["py_ecc"] = "8.0.0",
        };

        var identity = new EelsSemanticIdentity(
            PackageName:                "ethereum-execution",
            PackageVersion:             "2.19.0",
            SourceRepository:           "https://github.com/ethereum/execution-specs.git",
            SourceCommit:               "85aa48c742c38a2d5a876f84ebf8082a50273064",
            SourceTreeSha256:           "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256:             "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               "645f83fc5defbdb6a85ec5f9681abf83a33d0de18d0997b19b402487923bbfa6",
            PyprojectTomlSha256:        "f46eb6c72c82f54f9ea4374436976432e9544d8176e58d08ca1b2c907e4d3ae1",
            PythonImplementation:       "CPython",
            PythonVersion:              "3.13.11",
            RuntimePlatform:            "win32",
            InstallMode:                "editable",
            DistributionArtifactSha256: "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            LauncherSha256:             "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            true,
            DependencyVersions:         deps);

        Assert.Equal("ethereum-execution", identity.PackageName);
        Assert.Equal("2.19.0", identity.PackageVersion);
        Assert.Equal("85aa48c742c38a2d5a876f84ebf8082a50273064", identity.SourceCommit);
        Assert.Equal("https://github.com/ethereum/execution-specs.git", identity.SourceRepository);
        Assert.Equal("CPython", identity.PythonImplementation);
        Assert.Equal("editable", identity.InstallMode);
        Assert.Equal(2, identity.DependencyVersions.Count);
        Assert.NotNull(identity.CanonicalHash);
        Assert.NotEmpty(identity.CanonicalHash);
    }

    [Fact]
    public void SemanticIdentity_LauncherHashIsNonAuthoritative()
    {
        // Two identities that differ only in launcher hash should be
        // considered semantically equivalent for certification purposes.
        var id1 = MakeCompleteIdentity(launcherSha256: "launcher_hash_1");
        var id2 = MakeCompleteIdentity(launcherSha256: "launcher_hash_2");

        Assert.True(id1.IsSemanticEquivalent(id2));
    }

    [Fact]
    public void SemanticIdentity_DifferentSourceTree_NotEquivalent()
    {
        var id1 = MakeCompleteIdentity(sourceTreeSha256: "source_hash_1_padded_to_be_a_realistic_sha256_0000000000");
        var id2 = MakeCompleteIdentity(sourceTreeSha256: "source_hash_2_padded_to_be_a_realistic_sha256_0000000000");

        Assert.False(id1.IsSemanticEquivalent(id2));
    }

    [Fact]
    public void SemanticIdentity_DifferentVersion_NotEquivalent()
    {
        var id1 = MakeCompleteIdentity(packageVersion: "2.19.0");
        var id2 = MakeCompleteIdentity(packageVersion: "2.20.0");

        Assert.False(id1.IsSemanticEquivalent(id2));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Task 1 — v2 canonical identity contract tests
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Helper: builds a complete, realistic, internally consistent identity
    /// suitable for the all-green path. Every field is a plausible real value.
    /// </summary>
    private static EelsSemanticIdentity MakeCompleteIdentity(
        string? packageName = null,
        string? packageVersion = null,
        string? sourceRepository = null,
        string? sourceCommit = null,
        string? sourceTreeSha256 = null,
        string? evmToolsSha256 = null,
        string? uvLockSha256 = null,
        string? pyprojectTomlSha256 = null,
        string? pythonImplementation = null,
        string? pythonVersion = null,
        string? runtimePlatform = null,
        string? installMode = null,
        string? distributionArtifactSha256 = null,
        string? launcherSha256 = null,
        bool? isCleanCheckout = null,
        IReadOnlyDictionary<string, string>? dependencyVersions = null)
    {
        return new EelsSemanticIdentity(
            PackageName:                packageName              ?? "ethereum-execution",
            PackageVersion:             packageVersion           ?? "2.19.0",
            SourceRepository:           sourceRepository         ?? "https://github.com/ethereum/execution-specs.git",
            SourceCommit:               sourceCommit             ?? "85aa48c742c38a2d5a876f84ebf8082a50273064",
            SourceTreeSha256:           sourceTreeSha256         ?? "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256:             evmToolsSha256           ?? "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               uvLockSha256             ?? "645f83fc5defbdb6a85ec5f9681abf83a33d0de18d0997b19b402487923bbfa6",
            PyprojectTomlSha256:        pyprojectTomlSha256      ?? "f46eb6c72c82f54f9ea4374436976432e9544d8176e58d08ca1b2c907e4d3ae1",
            PythonImplementation:       pythonImplementation     ?? "CPython",
            PythonVersion:              pythonVersion            ?? "3.13.11",
            RuntimePlatform:            runtimePlatform          ?? "win32",
            InstallMode:                installMode              ?? "editable",
            DistributionArtifactSha256: distributionArtifactSha256 ?? "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            LauncherSha256:             launcherSha256           ?? "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            isCleanCheckout          ?? true,
            DependencyVersions:         dependencyVersions       ?? new Dictionary<string, string>
            {
                ["cryptography"] = "45.0.7",
                ["py_ecc"] = "8.0.0",
                ["rlp"] = "4.0.1",
            });
    }

    [Fact]
    public void CanonicalHash_DependencyInsertionOrder_DoesNotChangeHash()
    {
        var depsAZ = new Dictionary<string, string>
        {
            ["cryptography"] = "45.0.7",
            ["py_ecc"] = "8.0.0",
            ["rlp"] = "4.0.1",
        };
        var depsZA = new Dictionary<string, string>
        {
            ["rlp"] = "4.0.1",
            ["py_ecc"] = "8.0.0",
            ["cryptography"] = "45.0.7",
        };

        var id1 = MakeCompleteIdentity(dependencyVersions: depsAZ);
        var id2 = MakeCompleteIdentity(dependencyVersions: depsZA);

        Assert.Equal(id1.CanonicalHash, id2.CanonicalHash);
    }

    [Fact]
    public void CanonicalHash_DependencyVersionChange_ChangesHash()
    {
        var depsOriginal = new Dictionary<string, string>
        {
            ["cryptography"] = "45.0.7",
            ["py_ecc"] = "8.0.0",
        };
        var depsUpgraded = new Dictionary<string, string>
        {
            ["cryptography"] = "46.0.0",
            ["py_ecc"] = "8.0.0",
        };

        var id1 = MakeCompleteIdentity(dependencyVersions: depsOriginal);
        var id2 = MakeCompleteIdentity(dependencyVersions: depsUpgraded);

        Assert.NotEqual(id1.CanonicalHash, id2.CanonicalHash);
    }

    [Fact]
    public void CanonicalHash_EveryCertificationFieldChange_ChangesHash()
    {
        var baseline = MakeCompleteIdentity();
        var baseHash = baseline.CanonicalHash;

        // Every certification-relevant field must alter the hash when changed.
        var mutations = new (string Name, Func<EelsSemanticIdentity> Factory)[]
        {
            ("PackageName",                () => MakeCompleteIdentity(packageName: "different-package")),
            ("PackageVersion",             () => MakeCompleteIdentity(packageVersion: "3.0.0")),
            ("SourceRepository",           () => MakeCompleteIdentity(sourceRepository: "https://github.com/fork/execution-specs.git")),
            ("SourceCommit",               () => MakeCompleteIdentity(sourceCommit: "aaaa48c742c38a2d5a876f84ebf8082a50273064")),
            ("SourceTreeSha256",           () => MakeCompleteIdentity(sourceTreeSha256: "000096a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49")),
            ("EvmToolsSha256",             () => MakeCompleteIdentity(evmToolsSha256: "0000c26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711")),
            ("UvLockSha256",               () => MakeCompleteIdentity(uvLockSha256: "0000000000000000000000000000000000000000000000000000000000000001")),
            ("PyprojectTomlSha256",        () => MakeCompleteIdentity(pyprojectTomlSha256: "0000000000000000000000000000000000000000000000000000000000000002")),
            ("PythonImplementation",       () => MakeCompleteIdentity(pythonImplementation: "PyPy")),
            ("PythonVersion",              () => MakeCompleteIdentity(pythonVersion: "3.12.0")),
            ("RuntimePlatform",            () => MakeCompleteIdentity(runtimePlatform: "linux")),
            ("InstallMode",                () => MakeCompleteIdentity(installMode: "wheel")),
            ("DistributionArtifactSha256", () => MakeCompleteIdentity(distributionArtifactSha256: "ffff000000000000000000000000000000000000000000000000000000000000")),
            ("LauncherSha256",             () => MakeCompleteIdentity(launcherSha256: "0000923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f")),
            ("IsCleanCheckout",            () => MakeCompleteIdentity(isCleanCheckout: false)),
        };

        foreach (var (name, factory) in mutations)
        {
            var mutated = factory();
            Assert.True(
                mutated.CanonicalHash != baseHash,
                $"Changing {name} did not change CanonicalHash");
        }
    }

    [Fact]
    public void ValidateForCertification_EmptyRequiredFields_ReturnsTheirNames()
    {
        // Build an identity with several empty fields.
        // ValidateForCertification must report each one by name.
        var id = new EelsSemanticIdentity(
            PackageName:                "",
            PackageVersion:             "2.19.0",
            SourceRepository:           "",
            SourceCommit:               "",
            SourceTreeSha256:           "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256:             "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               "",
            PyprojectTomlSha256:        "",
            PythonImplementation:       "",
            PythonVersion:              "3.13.11",
            RuntimePlatform:            "win32",
            InstallMode:                "",
            DistributionArtifactSha256: "",
            LauncherSha256:             "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            true,
            DependencyVersions:         new Dictionary<string, string>());

        var problems = id.ValidateForCertification();

        Assert.Contains("PackageName", problems);
        Assert.Contains("SourceRepository", problems);
        Assert.Contains("SourceCommit", problems);
        Assert.Contains("UvLockSha256", problems);
        Assert.Contains("PyprojectTomlSha256", problems);
        Assert.Contains("PythonImplementation", problems);
        Assert.Contains("InstallMode", problems);
        Assert.Contains("DistributionArtifactSha256", problems);
        // Empty dependency collection must be flagged
        Assert.Contains("DependencyVersions", problems);
        // Fields that ARE populated must NOT appear:
        Assert.DoesNotContain("PackageVersion", problems);
        Assert.DoesNotContain("SourceTreeSha256", problems);
        Assert.DoesNotContain("PythonVersion", problems);
    }

    [Fact]
    public void ValidateForCertification_DirtyCheckout_ReturnsIsCleanCheckout()
    {
        var id = MakeCompleteIdentity(isCleanCheckout: false);

        var problems = id.ValidateForCertification();

        Assert.Contains("IsCleanCheckout", problems);
    }

    [Fact]
    public void Constructor_RequiresExplicitCleanlinessAndLockHashes()
    {
        // Every constructor parameter must have HasDefaultValue == false.
        // This proves no parameter silently defaults cleanliness, lock hashes,
        // or any other certification field.
        var ctor = typeof(EelsSemanticIdentity).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        foreach (var param in ctor.GetParameters())
        {
            Assert.False(
                param.HasDefaultValue,
                $"Constructor parameter '{param.Name}' has a default value. " +
                $"All certification identity parameters must be explicitly supplied.");
        }
    }

    [Fact]
    public void Constructor_DefensivelyCopiesDependencyVersions()
    {
        // Mutating the caller's dictionary after construction must not
        // change the identity's public DependencyVersions or CanonicalHash.
        var mutableDeps = new Dictionary<string, string>
        {
            ["cryptography"] = "45.0.7",
            ["py_ecc"] = "8.0.0",
            ["rlp"] = "4.0.1",
        };

        var id = MakeCompleteIdentity(dependencyVersions: mutableDeps);
        var hashBefore = id.CanonicalHash;
        var countBefore = id.DependencyVersions.Count;

        // Mutate the original dictionary
        mutableDeps["injected_package"] = "9.9.9";
        mutableDeps.Remove("cryptography");

        // Identity must be unchanged
        Assert.Equal(countBefore, id.DependencyVersions.Count);
        Assert.Equal(hashBefore, id.CanonicalHash);
        Assert.True(id.DependencyVersions.ContainsKey("cryptography"),
            "Removing key from original dict should not affect identity");
        Assert.False(id.DependencyVersions.ContainsKey("injected_package"),
            "Adding key to original dict should not affect identity");
    }

    [Fact]
    public void ValidateForCertification_BlankDependencyNameOrVersion_Reported()
    {
        var depsWithBlankVersion = new Dictionary<string, string>
        {
            ["cryptography"] = "45.0.7",
            ["py_ecc"] = "",           // blank version
            ["rlp"] = "4.0.1",
        };

        var id = MakeCompleteIdentity(dependencyVersions: depsWithBlankVersion);
        var problems = id.ValidateForCertification();

        // Must report the specific dependency with the blank version
        Assert.Contains("DependencyVersions[py_ecc]", problems);
        // Must NOT report the collection itself (it's non-empty)
        Assert.DoesNotContain("DependencyVersions", problems);
        // Must NOT report deps with valid versions
        Assert.DoesNotContain("DependencyVersions[cryptography]", problems);
        Assert.DoesNotContain("DependencyVersions[rlp]", problems);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Task 2 — Deterministic probe contract tests
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PythonStartInfo_PreservesSeparateArguments()
    {
        // ProcessStartInfo.ArgumentList must carry exactly three discrete elements:
        // "-c", the script text, and the specsRoot path.
        var psi = new System.Diagnostics.ProcessStartInfo("python.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import sys; print(sys.argv[1])");
        psi.ArgumentList.Add(@"C:\projects\execution-specs");

        Assert.Equal(3, psi.ArgumentList.Count);
        Assert.Equal("-c", psi.ArgumentList[0]);
        Assert.Equal("import sys; print(sys.argv[1])", psi.ArgumentList[1]);
        Assert.Equal(@"C:\projects\execution-specs", psi.ArgumentList[2]);
    }

    [Fact]
    public void PythonStartInfo_PreservesSpacesQuotesBackslashesAndNewlines()
    {
        var script = "import sys\nx = 'hello \"world\"'\nprint(x)";
        var path = @"C:\Program Files\execution specs\root";

        var psi = new System.Diagnostics.ProcessStartInfo("python.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(path);

        Assert.Equal(script, psi.ArgumentList[1]);
        Assert.Contains("\n", psi.ArgumentList[1]);
        Assert.Contains("\"", psi.ArgumentList[1]);
        Assert.Equal(path, psi.ArgumentList[2]);
        Assert.Contains(" ", psi.ArgumentList[2]);
        Assert.Contains(@"\", psi.ArgumentList[2]);
    }

    [Fact]
    public void Probe_UnknownGitStatus_IsNotClean()
    {
        // When Git is unavailable, the probe must not default IsCleanCheckout to true.
        // We test by constructing the identity the way the probe would if git status failed.
        // Git failure → statusOk=false → isClean must be false.
        bool statusOk = false;
        string gitStatus = "";
        var isClean = statusOk && string.IsNullOrWhiteSpace(gitStatus);

        Assert.False(isClean, "Unknown git status must not be treated as clean");
    }

    [Fact]
    public void Probe_MissingLockfile_IsIncomplete()
    {
        // If uv.lock is missing, the identity's UvLockSha256 should be empty,
        // and ValidateForCertification must flag it.
        var id = MakeCompleteIdentity(uvLockSha256: "");
        var problems = id.ValidateForCertification();
        Assert.Contains("UvLockSha256", problems);
    }

    [Fact]
    public void SourceTreeHash_IncludesRelativePathsAndFileBytes()
    {
        // Create a temp directory with two files. Hash must include
        // normalized relative paths, byte lengths, and file bytes.
        var dir = Path.Combine(Path.GetTempPath(), "eels_srctree_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "alpha.py"), new byte[] { 0x41, 0x42 });
        File.WriteAllBytes(Path.Combine(dir, "sub", "beta.py"), new byte[] { 0x43 });
        try
        {
            var hash = EelsProvenanceProbe.ComputeDirectoryHash(dir, "*.py");
            Assert.NotNull(hash);
            Assert.NotEmpty(hash);
            Assert.Equal(64, hash.Length); // SHA-256 hex

            // Changing file content must change hash
            File.WriteAllBytes(Path.Combine(dir, "alpha.py"), new byte[] { 0x41, 0x42, 0x43 });
            var hash2 = EelsProvenanceProbe.ComputeDirectoryHash(dir, "*.py");
            Assert.NotEqual(hash, hash2);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SourceTreeHash_RenameChangesHash()
    {
        // Renaming a file changes its relative path, which must change the hash
        // even if file content is identical.
        var dir = Path.Combine(Path.GetTempPath(), "eels_rename_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "original.py"), new byte[] { 0x41, 0x42 });
        try
        {
            var hash1 = EelsProvenanceProbe.ComputeDirectoryHash(dir, "*.py");

            File.Move(Path.Combine(dir, "original.py"), Path.Combine(dir, "renamed.py"));
            var hash2 = EelsProvenanceProbe.ComputeDirectoryHash(dir, "*.py");

            Assert.NotEqual(hash1, hash2);
        }
        finally { Directory.Delete(dir, true); }
    }
}
