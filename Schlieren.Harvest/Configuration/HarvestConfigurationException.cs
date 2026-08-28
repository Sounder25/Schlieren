namespace Schlieren.Harvest.Configuration;

/// <summary>
/// Exception thrown when Harvest apparatus configuration is invalid.
/// </summary>
public sealed class HarvestConfigurationException : Exception
{
    public string Code { get; }

    public HarvestConfigurationException(string code, string message) : base(message)
    {
        Code = code;
    }

    public HarvestConfigurationException(string code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }
}
