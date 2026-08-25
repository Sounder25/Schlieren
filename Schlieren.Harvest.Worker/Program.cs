using Schlieren.Harvest.Worker;

// Schlieren.Harvest.Worker
// Receives a single WorkerRequest from stdin (JSON), executes the requested
// operation against the canonical Schlieren EVM, writes one WorkerResponse to
// stdout (JSON), then exits.
//
// Task 4 stub: all execution operations are rejected with ProtocolError until
// Task 6 wires in SchlierenCaseExecutor.

var raw = Console.In.ReadToEnd().Trim();

WorkerResponse response;
try
{
    var req = WorkerProtocol.Deserialize(raw);
    response = req is null
        ? WorkerResponse.ProtocolError("Could not deserialize request")
        : WorkerResponse.ProtocolError($"Operation '{req.Operation}' is not yet implemented (Task 4 stub)");
}
catch (Exception ex)
{
    response = WorkerResponse.ProtocolError($"Unhandled exception: {ex.Message}");
}

Console.WriteLine(WorkerProtocol.Serialize(response));
return 0;
