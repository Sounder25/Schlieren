# Schlieren Guard — Demo Package

## What this is

A pre-sales demonstrator for **Schlieren Guard**, a token execution analysis product built on Schlieren's deterministic EVM engine.

The demonstrator is used to validate commercial interest and sell Founding Design Partner positions ($1,250 each) before building production infrastructure.

**This is NOT a production product. It is an evidence-driven demo shell backed by real execution results.**

---

## How to run the demo UI

1. Open `demo/guard-ui/index.html` in any modern browser (Chrome recommended)
2. Click a case pill to load a prepared result, or type a token address manually

No server required. No build step. Single HTML file.

For fullscreen presentation: F11 in Chrome.

---

## Token cases and evidence

### Case A — PEPE (established, normal)
- **Token:** `0x6982508145454ce325ddbe47a25d4ec3d2311933`
- **Verdict:** SELL SUCCESSFUL
- **Round-trip loss:** 1.08%
- **Block:** 25,872,402
- **Evidence:** `demo/evidence/case-a-normal/guard-6982...json`
- **RPC used:** https://ethereum.publicnode.com

### Case B — DCAT / Diamond Cat (competitive divergence)
- **Token:** `0x2d1643a5d14fb221521767f2405efbbe4d4603fd`
- **Verdict:** SELL BLOCKED (causal frame #4, TransferHelper: TRANSFER_FROM_FAILED)
- **Honeypot.is says:** isHoneypot=false, risk=low, sellTax=3.19%
- **Guard says:** SELL BLOCKED
- **Block:** 25,872,405
- **Evidence:** `demo/evidence/case-b-abnormal/guard-2d1643...json`

### Case C — CopperInu (hostile, classic honeypot)
- **Token:** `0xf69ebe4353d00912763b9e7be3aee4f00509c2cc`
- **Verdict:** SELL BLOCKED (causal frame #2, TransferHelper: TRANSFER_FROM_FAILED)
- **Block:** 25,872,399
- **Evidence:** `demo/evidence/case-c-hostile/guard-f69ebe...json`

### Case D — Phoenix (brand-new token)
- **Token:** `0x5be4ea5d45ec78f4ca34c69a2c714145c4538955`
- **Pool created:** 2026-08-31T02:54:35Z (~9 minutes before Guard scan)
- **Verdict:** SELL SUCCESSFUL
- **Round-trip loss:** 1.14%
- **Block:** 25,872,450
- **GoPlus at scan time:** Not in database
- **Evidence:** `demo/evidence/case-d-new-token/guard-5be4ea...json`

---

## How results were generated

All execution results are produced by the Schlieren Guard CLI:

```bash
# Syntax
demo/cli-publish/Schlieren.CLI.exe guard <token_address> \
  --fork-url https://ethereum.publicnode.com \
  --hardfork Osaka \
  --out demo/evidence/<case>/

# Example — PEPE
demo/cli-publish/Schlieren.CLI.exe guard \
  0x6982508145454ce325ddbe47a25d4ec3d2311933 \
  --fork-url https://ethereum.publicnode.com \
  --hardfork Osaka \
  --out demo/evidence/case-a-normal/
```

The CLI:
1. Pins Ethereum state at the latest finalized block via the RPC endpoint
2. Creates a disposable synthetic buyer address in a local overlay
3. Executes BUY → APPROVE → SELL through Uniswap V2 Router02
4. No transaction is broadcast on-chain. No capital is at risk.
5. Outputs a JSON evidence bundle to the specified directory

**RPC endpoint used:** `https://ethereum.publicnode.com` (free, no key required)

**Engine:** Schlieren Core — 1,350/1,350 EELS conformance, Frontier through Osaka

---

## How to reproduce each case

All cases can be re-run at any time. Results will differ by block (state changes), but the execution mechanism is identical.

To reproduce Case C (CopperInu honeypot) — expected to remain SELL BLOCKED:
```bash
demo/cli-publish/Schlieren.CLI.exe guard \
  0xf69ebe4353d00912763b9e7be3aee4f00509c2cc \
  --fork-url https://ethereum.publicnode.com \
  --hardfork Osaka \
  --out ./repro-copper/
```

To reproduce Case B (DCAT divergence) — verify Honeypot.is still disagrees:
```bash
demo/cli-publish/Schlieren.CLI.exe guard \
  0x2d1643a5d14fb221521767f2405efbbe4d4603fd \
  --fork-url https://ethereum.publicnode.com \
  --hardfork Osaka \
  --out ./repro-dcat/
# Then check: curl https://api.honeypot.is/v2/IsHoneypot?address=0x2d1643...
```

---

## Competitor captures

`demo/competitor-captures/honeypot_is_captures.json` — Honeypot.is API responses for all three pre-generated tokens, captured 2026-08-30.

`demo/competitor-captures/goplus_captures.json` — GoPlus API responses. CopperInu and DCAT return null (unknown tokens). Phoenix also not in database at scan time.

---

## Known limitations

- **Uniswap V2 only.** Many established tokens have migrated to V3. This is a deliberate scope constraint for the prototype.
- **2.0 ETH fixed trade size.** The CLI default is 0.05 ETH (50,000 wei×1e15), but the deployed evidence uses the default. Actual values confirmed from evidence JSON.
- **No live execution in the browser.** The UI presents pre-generated evidence artifacts. RUN GUARD plays an animation and shows frozen results.
- **No authentication, billing, accounts, or production API.** This is a demonstration shell only.
- **Phoenix (Case D) is time-specific.** The 9-minute age is accurate to the scan time (2026-08-31 ~03:03 UTC). On future runs the token will no longer be "brand new."

---

## What is NOT implemented (by design)

Per the mission scope, the following were deliberately not built:
- User accounts / authentication
- Payments / billing
- Production API or database
- Cloud deployment
- Wallet integration
- Multi-chain support beyond Ethereum V2
- Enterprise dashboard

These are **design partner questions** — see `demo/DEMO_SCRIPT.md`.

---

## Folder structure

```
demo/
├── guard-ui/
│   └── index.html              ← Single-file demo UI (open in browser)
├── evidence/
│   ├── case-a-normal/          ← PEPE evidence JSON
│   ├── case-b-abnormal/        ← DCAT evidence JSON
│   ├── case-c-hostile/         ← CopperInu evidence JSON
│   └── case-d-new-token/       ← Phoenix evidence JSON
├── competitor-captures/
│   ├── honeypot_is_captures.json
│   └── goplus_captures.json
├── cli-publish/                ← Published Schlieren.CLI binary
├── DEMO_SCRIPT.md              ← Exact 2:30 narration + timing
├── VALIDATION_REPORT.md        ← Build and evidence verification
└── README.md                   ← This file
```

---

## Commercial position

**Schlieren Guard** is an early product demonstrator backed by Schlieren's real deterministic EVM execution capability.

The Guard engine (BUY→SELL scenario execution) is complete and tested. The commercial product — API design, pricing, integration model, delivery mechanism — is being designed with founding design partners.

**Founding Design Partner Program: 3 positions at $1,250 each.**

Contact: guard@schlieren.xyz
