# Scrutor EELS Harness

This project runs published EELS `state_test` fixtures against Scrutor.

## Scope

- Discovers fixture JSON files from a configured root.
- Parses `state_test` cases for a target fork (default: `Cancun`).
- Executes each case through `StateTransition` + `EvmMachine`.
- Produces mismatch reports for account nonce/balance/code/storage and receipt status.

## Environment Variables

- `EELS_FIXTURES_ROOT`: absolute or relative path to fixture JSON root.
- `EELS_REQUIRED_FORK`: fork label in fixture `post` map (default: `Cancun`).
- `EELS_MAX_CASES`: max number of cases to load (default: `25`).
- `EELS_INCLUDE_SUBDIRS`: `1`/`true` to recurse fixture folders.

## Fixture setup (fresh clone)

Fixtures are large (~400 MB compressed) and are **not** in git. Download and extract with:

```powershell
pwsh ./tools/fetch-fixtures.ps1
```

Default source: `ethereum/execution-specs` release `tests@v20.0.1` → `./fixtures`.
Use `-Force` to re-download/re-extract. The archive stays local and gitignored.

## Running

```powershell
dotnet test .\Scrutor.EELS.Tests\Scrutor.EELS.Tests.csproj --nologo
```

Example with explicit fixture root:

```powershell
$env:EELS_FIXTURES_ROOT="C:\fixtures\state_tests"
$env:EELS_INCLUDE_SUBDIRS="1"
dotnet test .\Scrutor.EELS.Tests\Scrutor.EELS.Tests.csproj --nologo
```
