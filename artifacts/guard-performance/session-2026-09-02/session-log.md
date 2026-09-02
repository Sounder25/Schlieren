# Guard Performance Campaign Session Log

**Session:** 2026-09-02
**Hermes profile:** time
**Campaign initiated by:** Erick Turner

---

## Pre-Campaign State

### AWS Node Status (i-000aa2178570bbfab)

- **Region/AZ:** us-east-1a
- **Reth sync:** Block 0x18b0904 (≈26M), healthy
- **Guard CLI:** Published at `/opt/schlieren-guard/` (last rebuild Sep 2 03:39 UTC)
- **Evidence directory:** `/opt/guard-out/` — 18 evidence files

### Recent Guard Results (latest 3)

| Token | Kind | Effective Loss | Notes |
|-------|------|----------------|-------|
| USDC (A0b86991...) | SellSuccessful | 0.67% | Normal stablecoin |
| 6877fb50... | SellSuccessful | 0.06% | Low-slip token |
| f69EBe435... | SellBlocked | N/A | Sell failed, causalFrame=2 |

### Repository State

- **Worktree:** `C:\projects\Schlieren\.worktrees\schlieren-guard`
- **Branch:** `feature/schlieren-guard`
- **Unpushed commits:** 11
- **Working tree:** Clean

---

## Campaign Plan Reference

Full specification in: `.hermes/desktop-attachments/Guard_Performance_Characterization_and_Autonomy_Plan_2026-09-02.md`

### Key Questions to Answer

1. Fastest stable production transport (UI → Headless)?
2. Latency breakdown: P50/P95/P99 for each layer
3. Cold vs warm latency for standard Guard check
4. Which Guard features consume runtime
5. Full diagnostic run latency
6. Maximum stable concurrency
7. Cost per 1,000 scans
8. Autonomous operation proof (no SSH/SSM required)

### 6 Phases

| Phase | Purpose | Gate |
|-------|---------|------|
| 0 | Freeze environment, build | Reproducible manifest |
| 1 | Instrument latency boundaries | Timing reconciliation |
| 2 | Qualify transport candidates | Select ≤3 stable paths |
| 3 | Measure feature cost | P0–P7 ablation |
| 4 | Full scans, concurrency | P50/P95/P99, capacity |
| 5 | Autonomous operation | Tunnel-free, reboot, soak |
| 6 | Commercial verdict | Go/no-go recommendation |

---

## Session Actions

### 2026-09-02 ~10:19 UTC

- Connected to AWS node via SSM
- Verified Guard CLI published at `/opt/schlieren-guard/`
- Verified Reth healthy and synced
- Pulled latest evidence file summaries
- Identified UI location: `schlieren-ui/` (Vite frontend)
- Identified alternate demo UI: `demo/guard-ui/`

---

## Architecture Mapped

### Request Path (Local Dev)

```
Browser (localhost:3000)
  |  fetch(endpoint, { method: 'POST', body: 'schlieren_guard' })
  |  endpoint defaults to Vite dev server URL
  ▼
Vite Dev Server (port 3000)
  |  vite.config.ts: /rpc → http://localhost:8545
  |  (NOTE: hard-coded to 8545, should be 18545 for local Schlieren node)
  ▼
Schlieren.CLI Node (port 18545) [via --port flag]
  |  JSON-RPC: schlieren_guard({ token, rpc, block })
  |  GuardHandlers.HandleGuard()
  |    → HttpClientFactory.CreateClient()
  |    → ForkProvider (caching RPC wrapper)
  |    → TokenRiskChecker.EvaluateUniswapV2Async()
  ▼
RPC Target (http://localhost:8545 default, or caller-supplied URL)
  |  eth_call, eth_getStorageAt, eth_getCode, eth_getBlockByNumber
  |  ~8 RPC calls per Guard run (per 883ms observation)
  ▼
Reth on EC2 (loopback 8545 only) OR fork-url target
```

### Components

| Component | Location | Port | Role |
|-----------|----------|------|------|
| Browser UI | Local dev machine | — | User input, result render |
| Vite Dev Server | Local dev machine | 3000 | Proxy `/rpc` → Schlieren node |
| Schlieren.CLI Node | Local dev machine OR EC2 | 18545 (local) / 8545 (EC2) | Guard RPC handler |
| ForkProvider | Inside Schlieren process | — | Caching RPC wrapper |
| Reth | EC2 i-000aa2178570bbfab | 8545 (loopback only) | State provider |

### Key Files

- `schlieren-ui/src/views/Guard/Guard.tsx` — UI component
- `schlieren-ui/src/engine/guard-rpc.ts` — RPC call wrapper
- `schlieren-ui/vite.config.ts` — Proxy config (line 10: `target: 'http://localhost:8545'`)
- `Schlieren.RPC/Handlers/GuardHandlers.cs` — RPC handler
- `Schlieren.CLI/Commands/GuardCommand.cs` — CLI command
- `Schlieren.Guard/TokenRiskChecker.cs` — Core evaluation logic

### Latency Layers to Instrument

Per plan §7.2:

| Layer | Boundary | Current Instrumentation |
|-------|----------|-------------------------|
| UI → Transport | Button click to request sent | None |
| Transport | DNS/TCP/TLS + wire time | None |
| Headless Queue | Request received to processing start | None |
| Guard Resolve | Token/pool/metadata lookup | None |
| Guard State | Contract/code/storage fetch | Partial (BlockCache) |
| Guard Execution | BUY + SELL simulation | None |
| Guard Classification | Outcome determination | None |
| Guard Trace/Report | Evidence bundle construction | None |
| Serialization | Result → JSON bytes | None |
| Transport Return | Response wire time | None |
| UI Parse/Render | Response to visible result | None |

### Issues Found

1. **Vite proxy targets wrong port** — Line 10 of `vite.config.ts` points to `localhost:8545` (Reth loopback) instead of `localhost:18545` (Schlieren node). Local dev startup skill says this should be 18545.

---

## Next Steps

1. Fix Vite proxy target for local dev (8545 → 18545)
2. Create campaign branch `perf/guard-latency-characterization`
3. Record full environment manifest (git, build, AWS config)
4. Add correlation IDs and structured timing to GuardHandlers
5. Build .NET test harness for controlled measurement
6. Create frozen token manifest (12 cases)

---

## Stop Conditions

Per plan §16, stop if:
- Core outcomes change between profiles
- Reth loses sync
- Error rate > 5%
- Credentials logged
- SSH/Reth exposed publicly
- Campaign cannot reconcile timing

---

## Definition of Done (§17)

- [ ] 883 ms observation reproduced or disproved
- [ ] UI/transport/Guard/Reth/trace/serial time separately measured
- [ ] LOCAL, ALB-H2, SSH, SSM measured or explicitly skipped
- [ ] Standard and diagnostic distributions reported
- [ ] Concurrency ceiling and cost model reported
- [ ] Production path runs tunnel-free
- [ ] Reboot/recovery/soak tests passed or failed with proof
- [ ] REPRODUCE.md permits independent reproduction
- [ ] VERDICT.md gives commercial go/no-go
