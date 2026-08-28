# Self-Destruct Account Existence Bug — Root Cause Analysis

**Date:** 2026-08-28
**Commit:** `834c2e7`
**Family:** fam-001 (15 cases)
**Classification:** `Unknown/Account/AccountExistence`
**Delta:** `expected: false, actual: true` — Schlieren keeps accounts alive that should be deleted.

## Test Scenario

All 15 cases are `test_reentrant_selfdestructing_call` across Cancun, Prague, and Osaka forks with variants:
- `tload_after_selfdestruct_new_contract`
- `tload_after_selfdestruct_pre_existing_contract`
- `tload_after_inner_selfdestruct_new_contract`
- `tstore_after_selfdestruct_new_contract`
- `tstore_after_selfdestruct_pre_existing_contract`

## Execution Flow

### Parent contract (`0xfb43...`):
1. `CALLDATACOPY` — copies init code from tx data into memory
2. `CREATE` — deploys child contract, stores child address at slot 0
3. `CALL` child with calldata `0x0001...` (selector = 1) — first call
4. Store result at slot 1
5. `CALL` child with calldata `0x0002...` (selector = 2) — second call
6. Store result at slot 2
7. Load and store memory[0] at slot 3

### Child contract runtime (deployed by CREATE):
- **Selector 1:** `TSTORE(0xff, 0x0100)` then `SELFDESTRUCT(0x00)` — stores to transient, then selfdestructs
- **Selector 2:** `TLOAD(0xff)` then `RETURN` — reads transient storage and returns it

### What Ethereum says should happen (EIP-6780, post-Cancun):

The child was created in the same transaction. After selfdestruct:
- **Balance is transferred** to the beneficiary (0x00)
- **Account is marked for deletion** at end of transaction because it was created in the same tx
- Per EIP-6780: selfdestruct only deletes if created in same tx (post-Cancun)
- The second CALL to the child should see the account as **not existing** in post-state

The expected post-state shows the child account does NOT exist (`accountExistence: false`).

### What Schlieren does:

Schlieren keeps the child account alive in post-state (`accountExistence: true`). The account persists after the transaction completes even though:
- It was created in the same transaction
- SELFDESTRUCT was executed on it
- EIP-6780 explicitly permits deletion in this case

## Root Cause Hypothesis

Schlieren's `StateTransition.cs` or `StateOverlay.cs` is not tracking which accounts were created in the current transaction. When SELFDESTRUCT fires:
- Schlieren transfers the balance (this part likely works)
- But it does NOT mark the account for deletion at finalization
- Or it marks it, but the finalization step doesn't remove same-tx-created accounts

The EIP-6780 rule requires:
1. Track `created_in_current_tx` set at transaction scope
2. On SELFDESTRUCT: always transfer balance, but only schedule deletion if `target ∈ created_in_current_tx`
3. At transaction finalization: remove all scheduled-for-deletion accounts from state

## Root Cause (Confirmed 2026-08-28)

The bug is NOT in account deletion logic. Schlieren's `MarkForDeletion` and `DeleteAccount` chain is correct and the child contract IS deleted. The bug is that **SELFDESTRUCT creates the beneficiary account as an empty account**, even when the transfer amount is zero.

When `SELFDESTRUCT(0x00)` fires with a zero-balance contract:
1. Schlieren transfers balance (0) to address 0x0000...
2. The balance transfer code calls `context.GlobalState.SetBalance(beneficiary, benBalance + balance)` which is `SetBalance(0x00, 0 + 0)`
3. This call creates an empty account for the beneficiary in the state overlay
4. Per EIP-161 (Spurious Dragon+): empty accounts (nonce=0, balance=0, code=empty) that are only "touched" but receive nothing should be cleaned up at transaction end
5. Schlieren's post-state snapshot includes this empty account as existing

The expected behavior: address 0x0000... should NOT appear in post-state because:
- It didn't exist before
- It received 0 balance
- It was never meaningfully created

This manifests as `accountExistence: expected=false, actual=true` because the comparison finds an account that shouldn't exist.

The actual selfdestruct-created contract (the child at `0x7ce7...`) IS correctly deleted — the deletion machinery works. The 15 cases all trigger because every variant uses `SELFDESTRUCT(0x00)` with a zero-balance contract, creating the zero address as an artifact.

## Fix Required

In `StateTransition.cs` transaction finalization: after account deletions, perform EIP-161 empty account cleanup. Any account that is empty (nonce=0, balance=0, code=empty) and was touched during the transaction should be removed from state if it would not otherwise exist.
