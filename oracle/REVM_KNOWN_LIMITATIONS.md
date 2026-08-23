# REVM Oracle — Known Limitations

**Status:** No known limitations as of 2026-08-18.

---

## REVM-BUG-001: RETRACTED (Harness Defect)

**Previous status:** Reported as "Berlin SSTORE Clear Refund Not Applied"  
**Actual cause:** Schlieren's `revm-harness` set `ctx.cfg.spec = SpecId::BERLIN` without
calling `ctx.cfg.set_spec_and_mainnet_gas_params(SpecId::BERLIN)`. This left the gas
parameter table at its default (Osaka/London+ values), so REVM used
`SSTORE_CLEARS_SCHEDULE = 4800` (London) instead of `REFUND_STORAGE_CLEAR = 15000` (Berlin).

**Fix (2026-08-18):** Changed harness line 201 from:
```rust
ctx.cfg.spec = spec;
```
to:
```rust
ctx.cfg.set_spec_and_mainnet_gas_params(spec);
```

After the fix, REVM 42.x correctly returns `gas_used=14314, refund=14314` for the
Berlin XToZero test case — matching EELS and Schlieren exactly.

**Lesson:** When using REVM 42.x, setting the spec ID alone is insufficient.
`set_spec_and_mainnet_gas_params()` must be called to rebuild the fork-dependent
gas constant table. `Context::mainnet()` defaults to `SpecId::OSAKA`.

---

## Suppression in Campaign

No suppressions active. All REVM divergences from Schlieren/EELS are now treated as
real findings requiring investigation.
