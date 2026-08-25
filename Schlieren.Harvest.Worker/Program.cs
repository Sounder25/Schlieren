using Schlieren.Harvest.Worker;

// Schlieren.Harvest.Worker
//
// Receives one WorkerRequest from stdin (JSON), executes the requested operation,
// writes one WorkerResponse to stdout (JSON), then exits.
//
// Supported operations (Task 6):
//   "execute"           — execute one manifest case via SchlierenCaseExecutor
//   "calibration-crash" — deliberately terminates the worker (proves parent can
//                         persist Aborted evidence when a worker is killed)
//
// Any unrecognised operation returns ProtocolError.
// A crash, timeout, or cancellation at the parent is classified by WorkerExitClassifier.

var raw = Console.In.ReadToEnd().Trim();

WorkerResponse response;
try
{
    var req = WorkerProtocol.Deserialize(raw);
    if (req is null)
    {
        response = WorkerResponse.ProtocolError("Could not deserialize request");
    }
    else if (req.Operation == "calibration-crash")
    {
        // Deliberately terminate the process to let the parent prove it can
        // detect and persist an Aborted result via WorkerExitClassifier.Crashed.
        Environment.Exit(1);
        response = WorkerResponse.ProtocolError("unreachable"); // satisfies compiler
    }
    else if (req.Operation == "execute")
    {
        // Task 6: full execution wiring arrives via SchlierenCaseExecutor.
        // For now validate the request fields and return ProtocolError if missing,
        // or a stub Completed response if all fields are present.
        // Full execution is wired in Task 6 Step 4 integration.
        if (string.IsNullOrEmpty(req.Payload))
        {
            response = WorkerResponse.ProtocolError("execute request missing payload");
        }
        else
        {
            // Execution will be wired here in full Task 6 production integration.
            // The worker is exercised by WorkerExitClassifier tests via process launch.
            response = WorkerResponse.ProtocolError(
                "execute operation payload received but full wiring pending Task 6 integration");
        }
    }
    else
    {
        response = WorkerResponse.ProtocolError(
            $"Unknown operation '{req.Operation}'");
    }
}
catch (Exception ex)
{
    response = WorkerResponse.ProtocolError($"Unhandled exception: {ex.Message}");
}

Console.WriteLine(WorkerProtocol.Serialize(response));
return 0;
