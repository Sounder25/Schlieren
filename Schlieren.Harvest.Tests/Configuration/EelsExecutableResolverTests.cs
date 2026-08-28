using Schlieren.Harvest.Configuration;
using Xunit;

namespace Schlieren.Harvest.Tests.Configuration;

/// <summary>
/// Tests proving EelsExecutableResolver configuration contracts:
/// - EELS_EXE must be set (no fallback)
/// - Must be an absolute path
/// - Must exist on disk
/// - Returns the configured path when all checks pass
/// </summary>
public sealed class EelsExecutableResolverTests
{
    [Fact]
    public void ResolveEelsExecutable_Unset_ThrowsConfigurationError()
    {
        var error = Assert.Throws<HarvestConfigurationException>(
            () => EelsExecutableResolver.Resolve(_ => null));

        Assert.Equal("HARVEST.EELS_EXE_REQUIRED", error.Code);
        Assert.Contains("EELS_EXE", error.Message);
    }

    [Fact]
    public void ResolveEelsExecutable_EmptyString_ThrowsConfigurationError()
    {
        var error = Assert.Throws<HarvestConfigurationException>(
            () => EelsExecutableResolver.Resolve(_ => ""));

        Assert.Equal("HARVEST.EELS_EXE_REQUIRED", error.Code);
    }

    [Fact]
    public void ResolveEelsExecutable_WhitespaceOnly_ThrowsConfigurationError()
    {
        var error = Assert.Throws<HarvestConfigurationException>(
            () => EelsExecutableResolver.Resolve(_ => "   "));

        Assert.Equal("HARVEST.EELS_EXE_REQUIRED", error.Code);
    }

    [Fact]
    public void ResolveEelsExecutable_RelativePath_ThrowsConfigurationError()
    {
        var error = Assert.Throws<HarvestConfigurationException>(
            () => EelsExecutableResolver.Resolve(_ => "ethereum-spec-evm.exe"));

        Assert.Equal("HARVEST.EELS_EXE_ABSOLUTE_PATH_REQUIRED", error.Code);
        Assert.Contains("absolute", error.Message);
    }

    [Fact]
    public void ResolveEelsExecutable_ConfiguredFileMissing_ThrowsConfigurationError()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), "nonexistent_eels_" + Guid.NewGuid() + ".exe");

        var error = Assert.Throws<HarvestConfigurationException>(
            () => EelsExecutableResolver.Resolve(_ => nonexistent));

        Assert.Equal("HARVEST.EELS_EXE_NOT_FOUND", error.Code);
        Assert.Contains(nonexistent, error.Message);
    }

    [Fact]
    public void ResolveEelsExecutable_UsesConfiguredAbsolutePath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = EelsExecutableResolver.Resolve(key =>
                key == "EELS_EXE" ? tempFile : null);

            Assert.Equal(tempFile, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveEelsExecutable_QueriesCorrectKey()
    {
        var tempFile = Path.GetTempFileName();
        string? queriedKey = null;
        try
        {
            EelsExecutableResolver.Resolve(key =>
            {
                queriedKey = key;
                return tempFile;
            });

            Assert.Equal("EELS_EXE", queriedKey);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
