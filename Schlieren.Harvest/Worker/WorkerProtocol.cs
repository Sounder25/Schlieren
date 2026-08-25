using System.Text.Json;

namespace Schlieren.Harvest.Worker;

/// <summary>
/// Minimal stdin/stdout JSON protocol between the parent CampaignRunner and the
/// worker child process.
/// </summary>
public sealed record WorkerRequest(string Operation, string? Payload);

/// <summary>
/// Typed payload for the "execute" operation.
/// All identity fields are required; the parent validates them before spawning the worker.
/// </summary>
public sealed record ExecuteRequest(
    string ManifestHash,
    string CaseId,
    string FixturePath,
    string SourceSha256,
    string Fork,
    bool   JournalEnabled = false);

public sealed record WorkerResponse(
    bool Success,
    string? Result,
    string? Error,
    WorkerTerminationKind Termination)
{
    public static WorkerResponse ProtocolError(string message) =>
        new(false, null, message, WorkerTerminationKind.ProtocolError);

    public static WorkerResponse Completed(string result) =>
        new(true, result, null, WorkerTerminationKind.Completed);
}

public enum WorkerTerminationKind
{
    Completed,
    TimedOut,
    Cancelled,
    Crashed,
    ProtocolError
}

public static class WorkerProtocol
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static WorkerRequest? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WorkerRequest>(json, _opts); }
        catch { return null; }
    }

    public static string Serialize(WorkerResponse response) =>
        JsonSerializer.Serialize(response, _opts);
}
