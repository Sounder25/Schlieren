using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution.Inspect;

public sealed record InspectRequest
{
    public required Transaction Tx { get; init; }
    public required BlockContext Block { get; init; }
    public IReadOnlyList<string> Mismatches { get; init; } = Array.Empty<string>();
    public string? ExpectException { get; init; }
    public bool? ExpectedReceiptSuccess { get; init; }
    public bool DisableStack { get; init; }
    public bool DisableMemory { get; init; }
    public bool DisableStorage { get; init; }
}
