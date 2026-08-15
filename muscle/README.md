# muscle/

Pre-built prestate JSON files used by Workbench acceptance tests.

These are small synthetic prestate payloads (not full EELS fixtures) designed to test specific Workbench loading and execution paths.

| File | Description |
|---|---|
| `prestate-aa-calls-bb.json` | Contract A calls contract B (two-contract CALL topology) |
| `prestate-call-aa-bb.json` | Entry calls A then B (sequential CALL trace) |

Used by `Schlieren.Tests/WorkbenchAaBbAcceptanceTests.cs`.
