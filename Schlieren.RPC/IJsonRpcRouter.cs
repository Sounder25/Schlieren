using System.Collections.Generic;

namespace Schlieren.RPC;

/// <summary>
/// Defines the interface for a JSON-RPC router, providing methods for discovery.
/// </summary>
public interface IJsonRpcRouter
{
    Task<string> ProcessRequest(string requestBody, CancellationToken ct = default);
    IReadOnlyList<string> GetRegisteredMethods();
}
