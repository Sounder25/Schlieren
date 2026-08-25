using System.Text.Json;
using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Worker;

// Schlieren.Harvest.Worker
//
// Receives one WorkerRequest from stdin (JSON), executes the requested operation,
// writes one WorkerResponse to stdout (JSON), then exits.
//
// Supported operations:
//   "execute"           — execute one manifest case via SchlierenCaseExecutor
//   "calibration-crash" — deliberately terminates the worker process (proves the
//                         parent can detect and persist Aborted evidence)
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
        if (string.IsNullOrEmpty(req.Payload))
        {
            response = WorkerResponse.ProtocolError("execute request missing payload");
        }
        else
        {
            // Deserialize the execute request payload
            ExecuteRequest? execReq;
            try
            {
                execReq = JsonSerializer.Deserialize<ExecuteRequest>(req.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                response = WorkerResponse.ProtocolError($"execute payload is not valid JSON: {ex.Message}");
                goto WriteAndExit;
            }

            if (execReq is null ||
                string.IsNullOrEmpty(execReq.FixturePath) ||
                string.IsNullOrEmpty(execReq.Fork) ||
                string.IsNullOrEmpty(execReq.ManifestHash) ||
                string.IsNullOrEmpty(execReq.CaseId))
            {
                response = WorkerResponse.ProtocolError(
                    "execute payload missing required fields: fixturePath, fork, manifestHash, caseId");
                goto WriteAndExit;
            }

            // Validate fixture path is an absolute path to an existing file
            if (!Path.IsPathRooted(execReq.FixturePath) || !File.Exists(execReq.FixturePath))
            {
                response = WorkerResponse.ProtocolError(
                    $"fixturePath is not an absolute path to an existing file: {execReq.FixturePath}");
                goto WriteAndExit;
            }

            // Execute the case via the canonical Schlieren path
            try
            {
                var executor = new SchlierenCaseExecutor();
                var snapshot = await executor.ExecuteFromPathAsync(
                    execReq.FixturePath,
                    execReq.Fork,
                    journalEnabled: execReq.JournalEnabled);

                // Serialize the ExecutionSnapshot as the result
                var resultJson = JsonSerializer.Serialize(snapshot,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                response = WorkerResponse.Completed(resultJson);
            }
            catch (Exception ex)
            {
                response = WorkerResponse.ProtocolError($"Case execution failed: {ex.Message}");
            }
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

WriteAndExit:
Console.WriteLine(WorkerProtocol.Serialize(response));
return 0;
