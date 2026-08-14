using Microsoft.Extensions.Logging;

namespace Schlieren.RPC.Logging;

[ProviderAlias("Observable")]
public class ObservableLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new ObservableLogger(categoryName);
    }

    public void Dispose() { }
}
