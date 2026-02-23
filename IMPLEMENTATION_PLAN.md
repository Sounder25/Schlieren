# Scrutor - Windows-Native Ethereum Development Node

## Implementation Plan

> **Vision**: A superior Windows-native local Ethereum node that fully replicates Anvil's capabilities while delivering 2-5x performance improvements and advanced testing features for vulnerability hunting.

---

## 🎯 Project Overview

**Name**: Scrutor  
**Primary Goal**: Replace Foundry Anvil with a Windows-optimized, high-performance EVM simulator  
**Target Users**: Security researchers, fuzzing engineers, dApp developers on Windows  
**Key Differentiator**: Native Windows execution + Python integration + Advanced testing features

---

## 📋 Phase 1: Foundation & Core EVM (Weeks 1-4) - COMPLETED

### Milestone 1.1: Project Setup & Architecture

- [x] Choose tech stack (C#/.NET 8+ recommended for Windows native)
- [x] Set up solution structure with dependency injection
- [x] Design modular architecture: `Scrutor.Core`, `Scrutor.RPC`, `Scrutor.CLI`
- [x] Decide on EVM implementation (Custom Implementation `Scrutor.Core`)

### Milestone 1.2: Basic EVM Execution

- [x] Implement EVM opcodes (Arithmetic, Comparison, Bitwise, Control Flow completed)
- [x] Transaction execution pipeline (Foundation in place)
- [x] Gas calculation and metering (All Phase 1 Opcodes)
- [x] Account state management (Foundation in place)
- [x] Memory pool implementation (Stubbed)

**Deliverable**: Core Arithmetic Execution capable of passing unit tests.

---

## 📋 Phase 2: Anvil Parity - Core Features (Weeks 5-10)

### Milestone 2.1: Account & Chain Management

- [ ] CLI Options Implementation:
  - `--accounts`, `--balance`, `--mnemonic`
  - `--chain-id`, `--hardfork`
  - `--port`, `--host`
  - `--block-time` (mining modes)
  - `--auto-impersonate`
  - `--prune-history`
- [ ] Genesis configuration (custom genesis.json support)
- [ ] Pre-funded account generation

### Milestone 2.2: Forking Subsystem

- [x] Remote RPC client with caching
- [x] `--fork-url`, `--fork-block-number` support
- [ ] Offline mode with state caching
- [x] Retry logic and rate limiting
- [ ] Transaction-level forking
- [ ] Handle reorgs from upstream

**Deliverable**: Can fork mainnet and execute transactions against forked state

### Milestone 2.3: Standard RPC Methods

Implement all standard Ethereum JSON-RPC methods:

- [x] `eth_*` namespace (partial: accounts, blocks, transactions)
- [ ] `net_*` namespace
- [ ] `web3_*` namespace
- [ ] `debug_*` namespace (basic tracing)
- [ ] `txpool_*` namespace
- [ ] Transport layers: HTTP, WebSocket, IPC

**Deliverable**: Compatible with web3.py, ethers.js, and other standard clients

---

## 📋 Phase 3: Anvil Parity - Cheatcodes (Weeks 11-14)

### Milestone 3.1: Anvil-Specific RPC Methods

Implement all `anvil_*` cheatcodes:

- [ ] `anvil_impersonateAccount` / `anvil_stopImpersonatingAccount`
- [ ] `anvil_setBalance`
- [ ] `anvil_setCode`
- [ ] `anvil_setNonce`
- [ ] `anvil_setStorageAt`
- [ ] `anvil_mine` (manual block mining)
- [ ] `anvil_reset` (reset to genesis or fork)
- [ ] `anvil_setChainId`
- [ ] `anvil_snapshot` / `anvil_revert`
- [ ] `anvil_setBlockTimestampInterval`
- [ ] `anvil_removeBlockTimestampInterval`
- [ ] `anvil_dumpState` / `anvil_loadState`
- [ ] `anvil_setLoggingEnabled`

### Milestone 3.2: Ganache Compatibility

- [ ] `evm_increaseTime`
- [ ] `evm_setNextBlockTimestamp`
- [ ] `evm_setAutomine`
- [ ] `evm_setIntervalMining`
- [ ] `evm_mine`
- [ ] `evm_snapshot` / `evm_revert`

**Deliverable**: Drop-in replacement for Anvil in existing Foundry workflows

---

## 📋 Phase 4: Performance Optimizations (Weeks 15-18)

### Milestone 4.1: Memory & Speed

- [ ] Optimize state trie operations
- [ ] Implement efficient caching (LRU for remote calls)
- [ ] Parallel transaction execution (where safe)
- [ ] JIT compilation for hot paths
- [ ] Memory pooling and recycling
- [ ] Benchmark against Anvil (target: 2-5x faster)

### Milestone 4.2: State Management

- [ ] `--memory-limit` for resource capping
- [ ] `--auto-save-interval` for periodic state dumps
- [ ] Automatic pruning with configurable retention
- [ ] Historical block caching
- [ ] Incremental state snapshots

**Deliverable**: Demonstrable performance improvements in benchmarks

---

## 📋 Phase 5: Advanced Testing Features (Weeks 19-24)

### Milestone 5.1: Built-in Fuzzing

- [ ] `anvil_chaosMode` - Random tx failures and reorgs
- [ ] `anvil_setFailureRate <PERCENT>` - Simulate network issues
- [ ] `evm_simulateReorg <DEPTH>` - Chain reorganization simulation
- [ ] Integration hooks for external fuzzers

### Milestone 5.2: MEV Simulation

- [ ] `eth_sendBundle` (Flashbots-style)
- [ ] `txpool_simulateFrontRun`
- [ ] Priority fee simulation
- [ ] Builder API support

### Milestone 5.3: Enhanced Tracing & Debugging

- [ ] JavaScript tracer support (Geth-compatible)
- [ ] `trace_*` namespace (Parity/OpenEthereum)
- [ ] `ots_*` namespace (Otterscan compatibility)
- [ ] State diff tracing
- [ ] Call frame trees with gas breakdowns
- [ ] `anvil_recoverSignature` - Debugging helper

**Deliverable**: Advanced testing suite for vulnerability hunters

---

## 📋 Phase 6: Windows Optimization & Python Integration (Weeks 25-28)

### Milestone 6.1: Native Windows Features

- [ ] .NET native AOT compilation
- [ ] Windows service installation
- [ ] Performance counter integration
- [ ] Windows Event Log support
- [ ] Zero WSL dependencies

### Milestone 6.2: Python Bindings

- [ ] Native Python module (via pythonnet or C API)
- [ ] `pip install scrutor-py`
- [ ] Programmatic API:

  ```python
  from scrutor import Scrutor
  node = Scrutor(fork_url="https://eth.llamarpc.com")
  node.start()
  node.set_balance("0x123...", 1000 * 10**18)
  ```

- [ ] Async/await support
- [ ] Integration examples with chimera_fuzz pipeline

### Milestone 6.3: GUI (Optional)

- [ ] WPF or Avalonia-based desktop app
- [ ] Real-time mempool viewer
- [ ] State inspector
- [ ] Log viewer
- [ ] Block explorer

**Deliverable**: Seamless Windows + Python experience

---

## 📋 Phase 7: Extended Features (Weeks 29-32)

### Milestone 7.1: Multi-Chain Support

- [ ] Layer 2 support (Optimism, Arbitrum, Base)
- [ ] Custom precompiles
- [ ] Multi-fork mode (parallel chain simulation)
- [ ] `--multi-fork` CLI option
- [ ] Chain-switching API

### Milestone 7.2: Token Simulation

- [ ] `anvil_setTokenBalance` - ERC-20 balance manipulation
- [ ] Automatic ERC-20/721/1155 detection
- [ ] Pre-deployed test tokens

### Milestone 7.3: Configuration & Usability

- [ ] TOML configuration files
- [ ] `--config <PATH>` support
- [ ] `--strict-mode` for production-like behavior
- [ ] Better error messages and logging
- [ ] Auto-update mechanism

**Deliverable**: Feature-complete, extensible platform

---

## 📋 Phase 8: Polish & Release (Weeks 33-36)

### Milestone 8.1: Documentation

- [ ] Complete API reference
- [ ] Migration guide from Anvil
- [ ] Tutorials and examples
- [ ] Integration guides (Hardhat, Foundry, Brownie)
- [ ] Video demos

### Milestone 8.2: Testing & Validation

- [ ] Comprehensive test suite (unit, integration, e2e)
- [ ] Anvil compatibility test suite
- [ ] Performance benchmarks
- [ ] Security audit (if handling real funds in future)
- [ ] Community beta testing

### Milestone 8.3: Release

- [ ] Package for Chocolatey, Scoop, winget
- [ ] Docker images (ironically, for non-Windows users)
- [ ] GitHub releases with binaries
- [ ] Marketing and outreach

**Deliverable**: Production-ready 1.0 release

---

## 🛠️ Technology Stack (Recommended)

### Core Implementation

- **Language**: C# with .NET 8+ (native AOT for performance)
- **EVM**: Custom implementation or leverage Nethermind components
- **JSON-RPC**: ASP.NET Core Minimal APIs or custom server
- **State Storage**: RocksDB or FASTER (Microsoft Research)
- **Networking**: Built-in HttpClient with Polly for resilience

### Python Integration

- **Bindings**: pythonnet or ctypes
- **Distribution**: PyPI package with native wheels

### GUI (Optional)

- **Framework**: Avalonia (cross-platform XAML) or WPF (Windows-only)

### Testing

- **Unit Tests**: xUnit
- **Integration**: SpecFlow for BDD
- **Benchmarks**: BenchmarkDotNet

---

## 📊 Success Metrics

1. **Performance**: 2-5x faster than Anvil in standard benchmarks
2. **Compatibility**: 100% Anvil CLI/RPC compatibility
3. **Reliability**: <0.1% failure rate in extensive fuzzing
4. **Usability**: Zero WSL dependencies, <5 min setup time
5. **Adoption**: Integration with at least one major security firm

---

## 🚧 Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| EVM implementation complexity | Start with proven libraries (Nethermind), iterate |
| Performance targets too ambitious | Set realistic MVP, optimize iteratively |
| Python binding overhead | Benchmark early, use IPC if needed |
| Scope creep | Strict phase gating, MVP-first approach |
| Windows-only limits adoption | Ensure .NET cross-platform works, provide Docker |

---

## 🎯 MVP Definition (First 3 Months)

**Minimum Viable Product** to validate the concept:

1. ✅ Basic EVM execution (empty chain)
2. ✅ Forking from mainnet
3. ✅ Standard `eth_*` RPC methods
4. ✅ Core `anvil_*` cheatcodes (impersonate, setBalance, mine)
5. ✅ CLI with essential flags
6. ✅ HTTP transport
7. ✅ Basic Python bindings
8. ✅ 1.5-2x performance vs Anvil

**Success Criteria**: Can run chimera_fuzz pipeline with 50%+ speed improvement

---

## 📝 Next Steps

1. **Project Scaffolding**: Create solution structure
2. **EVM Decision**: Evaluate Nethermind vs custom implementation
3. **Prototype**: Build simple transaction executor
4. **Benchmark**: Establish baseline performance
5. **Iterate**: Follow phased approach

---

## 💡 Open Questions

- [x] Project name: **Scrutor**
- [ ] Open source license? (MIT, Apache 2.0)
- [ ] Hosting? (GitHub, GitLab)
- [ ] Community engagement strategy?
- [ ] Funding/sponsorship needs?

---

**Last Updated**: 2026-01-07  
**Status**: Planning Phase  
**Owner**: [Your Name/Team]
