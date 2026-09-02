# Schlieren Guard — Validation Report

**Date:** 2026-08-31
**Build commit (pre-demo additions):** 193d5bde95619a0fb19ed51121f7a8a3600bb6de

---

## Build Status

| Component | Status |
|---|---|
| Schlieren.Guard project (main branch) | ✅ Built and published |
| Schlieren.CLI — guard command registered | ✅ |
| Full solution build (dotnet build Schlieren.sln) | ✅ 0 errors, 10 warnings (pre-existing) |
| CLI publish (demo/cli-publish/) | ✅ |
| Guard UI (demo/guard-ui/index.html) | ✅ Static HTML, no build step |

---

## Evidence Status

### Case A — PEPE (Normal)
| Check | Result |
|---|---|
| Token address correct | ✅ 0x6982508145454ce325ddbe47a25d4ec3d2311933 (PEPE) |
| Pool/router correct | ✅ Uniswap V2 Router02 |
| Block recorded | ✅ 25,872,402 |
| BUY from Schlieren | ✅ gas 128,772 |
| SELL from Schlieren | ✅ gas 110,975 |
| Round-trip loss calc | ✅ 1.08% (from effectiveLossPercent in evidence JSON) |
| Raw evidence preserved | ✅ demo/evidence/case-a-normal/ |
| UI matches evidence | ✅ |

### Case B — DCAT / Diamond Cat (Competitive Divergence)
| Check | Result |
|---|---|
| Token address correct | ✅ 0x2d1643a5d14fb221521767f2405efbbe4d4603fd (DIAMOND CAT) |
| Block recorded | ✅ 25,872,405 |
| BUY from Schlieren | ✅ gas 270,394, success=true |
| SELL from Schlieren | ✅ gas ~68,000, success=false |
| Causal frame identified | ✅ Frame #4, depth 3, TransferHelper: TRANSFER_FROM_FAILED |
| Competitor comparison | ✅ Honeypot.is: isHoneypot=false, risk=low, sellTax=3.19% |
| Guard vs competitor divergence | ✅ CONFIRMED — different verdicts |
| Raw evidence preserved | ✅ demo/evidence/case-b-abnormal/ |
| Competitor capture preserved | ✅ demo/competitor-captures/honeypot_is_captures.json |

### Case C — CopperInu (Hostile)
| Check | Result |
|---|---|
| Token address correct | ✅ 0xf69eBe4353d00912763B9e7BE3aEE4f00509c2CC |
| Block recorded | ✅ 25,872,399 |
| BUY from Schlieren | ✅ gas 121,296, success=true |
| SELL from Schlieren | ✅ success=false |
| Causal frame identified | ✅ Frame #2, depth 1, TransferHelper: TRANSFER_FROM_FAILED |
| Raw evidence preserved | ✅ demo/evidence/case-c-hostile/ |

### Case D — Phoenix (Brand-New Token)
| Check | Result |
|---|---|
| Token address correct | ✅ 0x5be4ea5d45ec78f4ca34c69a2c714145c4538955 |
| Token age at scan | ✅ ~9 minutes (pool created 2026-08-31T02:54:35Z per GeckoTerminal) |
| Pool source documented | ✅ GeckoTerminal /api/v2/networks/eth/new_pools uniswap_v2 |
| Block recorded | ✅ 25,872,450 |
| BUY from Schlieren | ✅ gas 151,264, success=true |
| SELL from Schlieren | ✅ gas 125,157, success=true |
| Round-trip loss | ✅ 1.14% |
| GoPlus status at scan time | ✅ Not in database (confirmed via API) |
| Raw evidence preserved | ✅ demo/evidence/case-d-new-token/ |

---

## Competitor Captures

| Competitor | Method | Tokens captured | Status |
|---|---|---|---|
| Honeypot.is | REST API v2/IsHoneypot | PEPE, DCAT, CopperInu | ✅ Saved |
| GoPlus | REST API v1/token_security/1 | PEPE, DCAT, CopperInu, Phoenix | ✅ Saved |
| Quick Intel | Not available (no free API) | — | ⚠️ Not captured — browser access required |
| TokenSniffer | Not available (no free API) | — | ⚠️ Not captured — browser access required |

Quick Intel and TokenSniffer screenshots should be captured manually via browser on recording day.

---

## Video

| Item | Status |
|---|---|
| Demo UI functional | ✅ |
| All 4 case pills load correctly | ✅ |
| Evidence drawer opens/closes | ✅ |
| DCAT competitor row visible in evidence | ✅ |
| Phoenix age badge displays | ✅ |
| Screen recording | ⏳ To be recorded by Erick |
| Target resolution | 1920×1080 |
| Target length | 2:30 |
| Narration script | ✅ demo/DEMO_SCRIPT.md |

---

## Known Defects

| Item | Severity | Notes |
|---|---|---|
| "Recovered ETH" in UI uses approximated value | Low | Values derived from hex parsing; display rounded to 4dp |
| DCAT shows as "DCAT" — actual token name is "DIAMOND CAT" | Low | Update tokenName field if desired |
| Case B framing as "abnormal" | Note | Both B and C are SELL BLOCKED; B is differentiated by competitor divergence story |
| Phoenix pool liquidity ($10K) is thin | Note | Acceptable for demo purposes; stated in evidence |

---

## Features Not Implemented (by design)

- Live browser-to-engine execution (UI uses pre-generated evidence)
- User accounts / auth
- Billing / subscriptions
- Production API or database
- Cloud deployment
- Wallet integration
- Uniswap V3 support
- Multi-chain (Ethereum V2 only)
- Enterprise dashboard

---

## Design Partner Questions (collected during build)

These were deliberately NOT answered during the build:

1. Should Guard return PASS/WARN/FAIL labels, or raw measurements only?
2. What trade size should partners specify? Is 2 ETH right?
3. Should Guard support Uniswap V3 paths? (most tokens have moved there)
4. Should Guard test multiple exit sizes (0.1 ETH, 1 ETH, 10 ETH)?
5. Should Guard test multiple time/block conditions (same block, +1 block)?
6. Is API latency more important than evidence depth for wallet use cases?
7. Do exchanges want batch screening against a token list?
8. Do RPC providers want `schlieren_guard` as an RPC method?
9. Is deployment preference SaaS, private cloud, local node, or embedded?
10. What retention policy is acceptable for evidence artifacts?
