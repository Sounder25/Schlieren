namespace Schlieren.Harvest.Configuration;

/// <summary>
/// Resolves the EELS executable path from configuration.
///
/// Contracts:
/// - EELS_EXE environment variable must be set (no fallback)
/// - Path must be absolute
/// - File must exist on disk
/// </summary>
public static class EelsExecutableResolver
{
    /// <summary>
    /// Resolves the EELS executable path.
    /// </summary>
    /// <param name="getEnvironmentVariable">
    /// Function to query environment variables by name.
    /// </param>
    /// <returns>Resolved absolute path to the EELS executable.</returns>
    /// <exception cref="HarvestConfigurationException">
    /// Thrown when EELS_EXE is unset, relative, or points to a missing file.
    /// </exception>
    public static string Resolve(Func<string, string?> getEnvironmentVariable)
    {
        var path = getEnvironmentVariable("EELS_EXE");

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new HarvestConfigurationException(
                "HARVEST.EELS_EXE_REQUIRED",
                "EELS_EXE environment variable must be set. No hardcoded fallback is permitted.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new HarvestConfigurationException(
                "HARVEST.EELS_EXE_ABSOLUTE_PATH_REQUIRED",
                $"EELS_EXE must be an absolute path. Got: {path}");
        }

        if (!File.Exists(path))
        {
            throw new HarvestConfigurationException(
                "HARVEST.EELS_EXE_NOT_FOUND",
                $"EELS executable not found at {path}. Install ethereum-spec-evm and set EELS_EXE.");
        }

        return path;
    }
}
