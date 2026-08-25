namespace Schlieren.UI.Services;

/// <summary>
/// External configuration for HarvestService. Loaded once at the application
/// composition root (App.OnFrameworkInitializationCompleted) from environment
/// variables. No credential or machine-specific path may have a compiled default.
/// </summary>
public sealed record HarvestServiceOptions(
    Uri N8nBaseUri,
    string? N8nApiKey,
    string? McpToken,
    string? CorpusDirectory)
{
    /// <summary>
    /// Reads the four Harvest configuration keys from the supplied environment reader.
    /// <list type="bullet">
    ///   <item><term>SCHLIEREN_N8N_BASE_URL</term><description>Absolute HTTP/HTTPS base URI (defaults to http://localhost:5678 when absent or invalid).</description></item>
    ///   <item><term>SCHLIEREN_N8N_API_KEY</term><description>Optional n8n API key. Blank or whitespace-only trims to null.</description></item>
    ///   <item><term>SCHLIEREN_MCP_TOKEN</term><description>Optional MCP bearer token. Blank or whitespace-only trims to null.</description></item>
    ///   <item><term>SCHLIEREN_HARVEST_CORPUS</term><description>Optional corpus directory path. Blank trims to null; nonblank is canonicalized with Path.GetFullPath.</description></item>
    /// </list>
    /// </summary>
    public static HarvestServiceOptions FromEnvironment(Func<string, string?> read)
    {
        const string localhostDefault = "http://localhost:5678";

        Uri baseUri;
        var rawBase = read("SCHLIEREN_N8N_BASE_URL");
        if (!string.IsNullOrWhiteSpace(rawBase) &&
            Uri.TryCreate(rawBase.Trim(), UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            baseUri = parsed;
        }
        else
        {
            baseUri = new Uri(localhostDefault);
        }

        var apiKey = NullIfBlank(read("SCHLIEREN_N8N_API_KEY"));
        var mcpToken = NullIfBlank(read("SCHLIEREN_MCP_TOKEN"));

        string? corpusDir = null;
        var rawCorpus = read("SCHLIEREN_HARVEST_CORPUS");
        if (!string.IsNullOrWhiteSpace(rawCorpus))
            corpusDir = Path.GetFullPath(rawCorpus.Trim());

        return new HarvestServiceOptions(baseUri, apiKey, mcpToken, corpusDir);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
