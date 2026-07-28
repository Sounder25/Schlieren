# Scrutor

Scrutor is a .NET 8 Ethereum execution client and EVM implementation. The
solution includes the core execution engine, JSON-RPC services, a command-line
host, a Windows desktop UI, unit tests, and an EELS state-test conformance
harness.

## Projects

- `Scrutor.Core` — EVM execution, state transitions, opcodes, transaction
  handling, precompiles, and chain state.
- `Scrutor.RPC` — Ethereum JSON-RPC server.
- `Scrutor.CLI` — command-line host.
- `Scrutor.UI` — WPF desktop application.
- `Scrutor.Tests` — unit and integration tests.
- `Scrutor.EELS.Tests` — adapter for published Ethereum Execution Layer
  Specification state-test fixtures.

## Build and test

```powershell
dotnet restore
dotnet build --no-restore
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --no-build
```

The EELS harness requires a local fixture checkout. Fixture data and local
reference copies are intentionally excluded from Git.

```powershell
$env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"

dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --no-build
```

See [Scrutor.EELS.Tests/README.md](Scrutor.EELS.Tests/README.md) for harness
configuration details.

## Cancun conformance status

The current work targets Cancun semantics using the EELS Cancun implementation
as the behavioral authority. Areas under active implementation and validation
include:

- EIP-150 message-call and contract-creation gas forwarding.
- EIP-2929 warm/cold address and storage access tracking.
- EIP-3529 refund behavior.
- EIP-6780 same-transaction `SELFDESTRUCT` lifecycle handling.
- EIP-7610 storage-aware contract-creation collision checks.
- Cancun `BLOBHASH` and blob precompile routing.

The `SELFDESTRUCT` new-account surcharge is covered by a regression test:
transferring a nonzero balance to a beneficiary that is not alive costs an
additional 25,000 gas. Restoring the EELS `CREATE2` collision predicate and
EIP-6780 lifecycle behavior resolves the tracked dynamic
CREATE2/SELFDESTRUCT fixture completely: Scrutor and EELS both use 368,516 gas,
and the expected balances, nonces, code, and storage match.

Conformance work remains ongoing; diagnostic tests must not be treated as proof
that the full Cancun suite passes.

### Current verification baseline

As of 2026-07-27:

- `dotnet build Scrutor.sln --no-restore`: succeeds with 0 warnings and 0
  errors.
- `Scrutor.Tests`: 233 passed, 0 failed. RPC call simulations bypass mined
  transaction fee, nonce, and sender-funding validation while retaining normal
  EVM execution and intrinsic-gas behavior.
- `Scrutor.EELS.Tests`: 13 passed, 0 failed. The Cancun taxonomy sweep reports
  0 failing cases out of 1,127 published state-test cases.

These numbers are a development baseline, not a conformance claim.

## Reference material

Local EELS source, NotebookLM exports, fixture archives, generated traces, and
scratch experiments are intentionally ignored. They should be downloaded or
generated locally rather than committed to this repository.
