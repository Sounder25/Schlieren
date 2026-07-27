# EELS — Ethereum Execution Layer Specifications (pure form)

This folder contains **only** the executable Ethereum execution-layer specification:
the Python package `ethereum/` from the official repository
https://github.com/ethereum/execution-specs

That is the complete, consensus-critical reference implementation of the EL rules
(per hard fork). Contributor tooling (Claude configs, CI YAML, tests, docs site,
fixture generators) has been stripped out so you only have the true EELS source.

## Layout

- `ethereum/forks/<fork>/` — full protocol rules for that hard fork (VM, gas, txs, blocks, precompiles)
- `ethereum/crypto/` — cryptographic primitives used by the spec
- `ethereum/state.py`, `merkle_patricia_trie.py`, etc. — shared state / trie / genesis helpers
- `ethereum/assets/` — genesis and sample chain data used by the reference code

Each hard fork under `forks/` is intentionally a complete copy of its predecessor
(WET, not DRY). Read `forks/prague/` or `forks/cancun/` for modern mainnet-relevant rules.

Upstream monorepo (full clone with tests/tooling) was moved aside if you need it later.
