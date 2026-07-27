# Handoff: Scrutor EVM Conformance & Next Work

## Current Repository State
- Nonce fix implemented & accepted for CREATE/CREATE2.
- Storage-presence implementation and EIP-6780 deletion marker logic preserved.
- All current unit tests and test suites preserved.
- `Create2SelfDestructTracer.cs` included for detailed EVM frame gas tracing.

## Next Work: CREATE/CREATE2 Collision Gas Accounting

Status: PLANNED — DO NOT IMPLEMENT UNTIL TRACE PROOF IS COMPLETE

### Objective
Align Scrutor's `CREATE` and `CREATE2` gas consumption ordering on collision with EELS specification (`fork-cancun.md`).

### Context & Citations
- **EELS Reference Folder:** `EELs-NoteBookLM/`
- **Controlling Specification File:** `EELs-NoteBookLM/fork-cancun.md`
- **Citations in `generic_create` (lines 7351-7376):**
  - EIP-150 message-gas (`max_message_call_gas(evm.gas_left)`) is calculated and reserved BEFORE balance, depth, and deployability collision checks.
  - On balance or call-depth limit early exits (lines 7360-7367), reserved gas IS refunded to the caller.
  - On deployability collision (lines 7371-7374), reserved gas stays consumed (NOT refunded) and the creator nonce increment remains in place.

### Unresolved 163,302-Gas Discrepancy
- The target fixture `test_dynamic_create2_selfdestruct_collision` exhibits an unresolved 163,302 gas discrepancy between Scrutor and EELS.
- Trace proof indicates this is tied to whether forwarded creation gas is burned on collision inside inner frames (`0x601`) vs parent frame (`0x600`) gas accounting and EIP-6780 `SELFDESTRUCT` refund mechanics.

### Commands to Resume on Laptop

```powershell
# 1. Restore & Build
dotnet restore
dotnet build --no-restore

# 2. Configure EELS Fixture Environment
$env:EELS_FIXTURES_ROOT="C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS="1"

# 3. Run Gas & Collision Tracer
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj `
    --filter "Create2SelfDestructTracer" `
    --logger "console;verbosity=detailed"

# 4. Run Unit Test Suite
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --logger "console;verbosity=detailed"
```
