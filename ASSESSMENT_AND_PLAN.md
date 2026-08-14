# Schlieren EVM Project Assessment
**Senior Developer Review — 2026-08-03**
**Assessment by:** Hermes Agent

---

## Executive Summary

Schlieren is a .NET 8 Ethereum execution client implementing the EVM with JSON-RPC, CLI, and WPF UI frontends. The project has achieved **Cancun fork conformance** against the EELS state-test fixture suite with strong test coverage (265 unit tests, 1,127 fixture cases passing). However, several structural and organizational issues impede production readiness.

### Key Metrics

| Metric | Value |
|--------|-------|
| **Total Source Lines** | ~14,616 (excluding tests) |
| **Test Lines** | ~8,761 |
| **Unit Tests** | 265 (all passing) |
| **EELS Fixture Cases** | 1,127 (0 failures in Cancun sweep) |
| **Solution Projects** | 6 in solution, 2 orphaned |
| **Build Warnings** | 0 |
| **Code Quality** | Good — consistent style, proper null handling |

---

## Project Structure Analysis

### Active Projects (In Solution)

| Project | Lines | Purpose | Status |
|---------|-------|---------|--------|
| `Schlieren.Core` | 9,699 | EVM engine, opcodes, state transitions, precompiles | ✅ Core — production quality |
| `Schlieren.CLI` | 2,440 | Command-line host, scripting | ✅ Functional |
| `Schlieren.RPC` | 2,798 | JSON-RPC server (eth_*, debug_*) | ✅ Functional |
| `Schlieren.Tests` | 6,336 | Unit + integration tests | ✅ Comprehensive |
| `Schlieren.EELS.Tests` | 2,425 | EELS fixture harness | ✅ Active development |
| `Schlieren.UI` | 680 | WPF desktop app | 🟡 Minimal implementation |

### Orphaned Projects (NOT in Solution)

| Project | Lines | Purpose | Recommendation |
|---------|-------|---------|----------------|
| `Schlieren.Burst` | 160 | Burst load testing tool | 🗑️ **Delete** — dev scratch, unused |
| `Schlieren.BurstClient` | 198 | Duplicate of Burst | 🗑️ **Delete** — exact duplicate |

---

## Code Quality Assessment

### Strengths

1. **EVM Implementation** — Comprehensive opcode coverage with proper gas accounting
   - All Cancun opcodes implemented (BLOBHASH, TLOAD/TSTORE, MCOPY)
   - EIP-150 63/64 gas forwarding correctly applied
   - EIP-2929 warm/cold access tracking working
   - EIP-6780 SELFDESTRUCT semantics correct

2. **Test Coverage** — Excellent breadth:
   - Core EVM tests (arithmetic, stack, memory, control flow)
   - Call semantics tests (CALL, DELEGATECALL, STATICCALL)
   - RPC integration tests with mock HTTP
   - Deep recursion tests using 32MB stack LargeStackWorker

3. **Precompile Implementation** — Full suite:
   - ecRecover (0x01), SHA-256 (0x02), RIPEMD-160 (0x03)
   - ModExp (0x05), BN254 ecAdd/Mul/Pairing (0x06–0x08)
   - BLAKE2F (0x09), KZG Point Eval (0x0A)

4. **Code Style** — Consistent:
   - CRLF line endings (editorconfig enforced)
   - Nullable reference types enabled
   - Latest C# language version
   - No build warnings

### Weaknesses

1. **Orphaned Projects** — Burst/BurstClient are duplicate scratch tools not in the solution
2. **External Dependencies in Repo** — `_eels_full_repo_backup/` contains a full EELS repository (should be external reference)
3. **Node Modules Committed** — `muscle/` contains hardhat artifacts + node_modules (shouldn't be in repo)
4. **Fixture Data** — `fixtures/` (585MB) and `state_tests/` (505MB) are gitignored but present on disk
5. **Minimal .editorconfig** — Only one diagnostic rule; should include style rules
6. **No CI/CD Configuration** — Missing GitHub Actions, versioning strategy, release automation
7. **UI Project** — Minimal implementation, unclear if production-intended

---

## Known Technical Defects (from HANDOFF.md)

| ID | Issue | Status | Risk |
|----|-------|--------|------|
| D1 | CREATE OOG EIP-150 reserve discrepancy (7,453 gas) | ⏸️ Deferred | Low |
| D2 | TLOAD/TSTORE gas discrepancies in older fixtures | 🔴 Open | Medium |
| D3 | CALLCODE + TLOAD 2-gas under-charge | 🟡 Minor | Low |
| D4 | Missing fixture subdirs for 3 harness tests | 🟡 Infra | Low |

---

## Primary Plan: Production Readiness

### Phase 1: Structural Cleanup (Immediate)

**Goal:** Clean repository to production-grade structure

1. **Delete Orphaned Projects**
   ```bash
   rm -rf Schlieren.Burst Schlieren.BurstClient
   ```

2. **Externalize EELS Backup**
   - Move `_eels_full_repo_backup/` to external storage or delete
   - Document EELS fixture URL in README instead

3. **Clean `muscle/` Directory**
   - This appears to be a hardhat project for contract testing
   - Options:
     - If actively used: Add to `.gitignore`, keep locally
     - If unused: Delete entirely

4. **Organize Documentation**
   - Consolidate `AUDIT_FINDINGS.md`, `CONFORMANCE_STATUS.md`, `GAS_LEDGER.md` into `docs/`
   - Move `EELs-NotebookLM/` to `.gitignore` (reference material)

5. **Update .editorconfig**
   - Add comprehensive style rules (indentation, naming, etc.)

### Phase 2: CI/CD Pipeline (Week 1)

**Goal:** Automated quality gates

1. **GitHub Actions Workflow**
   ```yaml
   # .github/workflows/ci.yml
   - Build (dotnet build)
   - Test (Schlieren.Tests)
   - EELS conformance sweep (optional, on schedule)
   - Code coverage report
   ```

2. **Versioning Strategy**
   - Add GitVersion or similar
   - Tag releases with semantic versioning
   - NuGet package publishing (if applicable)

3. **Branch Protection**
   - Require CI pass for PR merge
   - Require code review

### Phase 3: Code Organization (Week 2)

**Goal:** Improve maintainability

1. **Opcode Organization**
   - Current: 10 opcode files (Arithmetic, Bitwise, Storage, etc.) ✅ Good
   - Consider grouping by EIP if complexity grows

2. **Test Organization**
   - Current structure is reasonable
   - Add `TestCategory` attributes for filtering

3. **API Documentation**
   - Add XML doc comments to public APIs
   - Generate API reference docs

### Phase 4: EELS Harness Hardening (Week 3)

**Goal:** Expand conformance coverage

1. **Fix Missing Fixtures**
   - Download `eip6780_selfdestruct/selfdestruct_revert`
   - Download `eip1153_tstore/basic_tload`

2. **Expand Fixture Sweep**
   - Run against broader `state_tests/static/state_tests/`
   - Track failure categories in automated report

3. **Performance Benchmark**
   - Add benchmarking for hot paths
   - BN254 pairing optimization (if throughput becomes important)

---

## Secondary Plan: Cleanup & Removal

### Target: Dead Code and Scratch Files

| Target | Location | Reason | Action |
|--------|----------|--------|--------|
| Burst projects | `./Schlieren.Burst/`, `./Schlieren.BurstClient/` | Duplicate, orphaned | Delete |
| EELS backup | `./_eels_full_repo_backup/` | External reference, large | Remove |
| Node modules | `./muscle/node_modules/` | Should be gitignored | Add to .gitignore, remove from disk |
| Diagnostics | `./Schlieren.EELS.Tests/Diagnostics/` | Already deleted in staged changes | Confirm deletion |
| Scratch experiments | `.gitignore mentions: scratch/, CkzgTest/, DebugBalance.cs` | Dev artifacts | Delete if exist |

### Documentation Consolidation

| File | Contents | Action |
|------|----------|--------|
| `AUDIT_FINDINGS.md` | Historical gas audit | Move to `docs/archive/` |
| `CONFORMANCE_STATUS.md` | Current conformance | Keep in root, update actively |
| `GAS_LEDGER.md` | Gas discrepancy log | Move to `docs/` |
| `HANDOFF.md` | Session context | Keep in root |
| `WORKLOG-2026-07-27.md` | Session log | Move to `docs/transcripts/` |
| `transcripts/` | Session transcripts | Keep |

### Directory Structure Target

```
C:\projects\Schlieren\
├── .github/
│   └── workflows/
│       └── ci.yml
├── .hermes/                    # Hermes session data
├── .meta/                      # Fixture metadata
├── docs/
│   ├── archive/               # Historical docs
│   │   ├── AUDIT_FINDINGS.md
│   │   └── GAS_LEDGER.md
│   └── transcripts/
│       └── WORKLOG-2026-07-27.md
├── Schlieren.Core/
├── Schlieren.CLI/
├── Schlieren.RPC/
├── Schlieren.Tests/
├── Schlieren.EELS.Tests/
├── Schlieren.UI/                # Evaluate: keep or remove
├── tools/                     # Build/dev tools
├── .editorconfig              # Enhanced rules
├── .gitignore                 # Cleaned
├── Directory.Build.props
├── Directory.Packages.props
├── Schlieren.sln
├── HANDOFF.md                 # Active session doc
├── CONFORMANCE_STATUS.md      # Live status
├── README.md                  # Project README
└── ASSESSMENT_AND_PLAN.md     # This document
```

---

## Resource Cleanup Commands

### Immediate Safe Deletions

```bash
# Delete orphaned Burst projects
rm -rf Schlieren.Burst Schlieren.BurstClient

# Remove EELS backup (external reference preferred)
rm -rf _eels_full_repo_backup

# Remove muscle node_modules (regenerable)
rm -rf muscle/node_modules

# Update .gitignore
cat >> .gitignore << 'EOF'

# Development scratch directories
muscle/node_modules/
tools/specdata/
EOF
```

### Git Operations

```bash
# Stage deletions
git rm -r Schlieren.Burst Schlieren.BurstClient _eels_full_repo_backup

# Commit cleanup
git commit -m "chore: remove orphaned burst projects and EELS backup

- Delete Schlieren.Burst and Schlieren.BurstClient (orphaned dev tools)
- Remove _eels_full_repo_backup (external reference preferred)
- Clean up repository structure for production readiness"
```

---

## Risk Assessment

| Risk | Level | Mitigation |
|------|-------|------------|
| EELS fixture coverage incomplete | Medium | Expand sweep, track failures |
| BN254 pairing performance | Low | Optimize when needed |
| Missing fixture subdirs | Low | Download specific directories |
| UI project unfinished | Low | Clarify intent, document or remove |
| No CI/CD | Medium | Implement GitHub Actions |
| Large on-disk fixtures | Low | Already gitignored |

---

## Recommendations Summary

### Must Do (P0)

1. ✅ Delete orphaned Burst/BurstClient projects
2. ✅ Remove EELS backup directory
3. ✅ Clean `.gitignore` for muscle/node_modules
4. ✅ Add GitHub Actions CI workflow
5. ✅ Enhance `.editorconfig`

### Should Do (P1)

1. Consolidate documentation into `docs/`
2. Add API XML documentation
3. Implement semantic versioning
4. Download missing EELS fixture subdirs

### Nice to Have (P2)

1. BN254 pairing optimization
2. Benchmark suite
3. Prague fork fixture preparation
4. UI project evaluation

---

## Conclusion

Schlieren is a well-architected EVM implementation with excellent test coverage and successful Cancun conformance. The core engine is production-quality. The primary blockers for production readiness are organizational: orphaned projects, missing CI/CD, and scattered documentation. 

The cleanup plan above will transform Schlieren into a clean, production-grade repository within 2-3 weeks of focused effort. The secondary cleanup (dead code removal) can be executed immediately with minimal risk.

**Priority:** Execute secondary cleanup first (safe deletions), then proceed with CI/CD setup and documentation consolidation.
