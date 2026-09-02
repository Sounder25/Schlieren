# Schlieren Guard — Execution-Proof Token & Contract Risk Checks

**Date:** 2026-08-30 (Rev. 6 — market validation, sharpened qualification gate, free/API monetization split)
**Author:** Erick (drafted with Claude)
**Status:** Brainstorm + proposal — not started, no code changed

**Revision note (Rev. 5):** Rev. 4 proposed a generic "explain any transaction" flagship
and, separately, floated a honeypot checker as one surface of it. That was underspecified
twice over. First, "paste a token, get a honeypot verdict" is a saturated market —
Honeypot.is, TokenSniffer (59M+ tokens tracked), and GoPlus (30+ checks, 1.37M+ tokens
analyzed) already do the scored version well. Second, calling the difference "framing"
was wrong: a real pinned-block fork-state provider is a genuine new engineering
component. This revision replaces the generic flagship with a specific, narrower, more
defensible consumer product — **Schlieren Guard** — built around one idea competitors
don't have: don't score it, execute it and prove it.

---

## 0. The actual differentiator

Every existing scanner outputs something like:

```
HONEYPOT: NO
SELL TAX: 5%
MINTABLE: YES
PROXY: YES
RISK SCORE: 62/100
```

A score, computed from static/heuristic checks, that itself may be stale — Honeypot.is
says as much: passing today doesn't mean the contract can't change later.

Guard answers a narrower, harder question empirically, by actually running the trade:

```
Pinned chain state (block N)
       ↓
Fund a disposable buyer
       ↓
BUY through the real pool/router
       ↓
Measure tokens actually received
       ↓
APPROVE the router
       ↓
SELL those exact tokens
       ↓
Measure ETH/USDC actually returned
       ↓
Inspect every CALL / SSTORE / REVERT along the way
       ↓
Verdict + evidence
```

The pitch is not "more checkboxes than GoPlus." It's: **don't trust our score — we
actually executed the trade. When we say the owner can trap you, click here and watch
exactly how.** That's a claim none of the incumbents can make, because none of them run
a real canonical EVM against real pinned state and hand you the frame-level proof.

---

## 1. Two verdicts, never conflated

**Execution Risk — "What happens if I trade it right now?"** Empirical, from the actual
buy/sell run above:

```
✓ BUY SUCCESSFUL          ✓ SELL SUCCESSFUL
Bought:       100 USDC     Sold:      8,421,552 TOKEN
Received: 8,421,552 TOKEN  Received:     92.37 USDC
Effective buy loss:  2.1%  Effective sell loss: 5.6%
Transfer: PASS   Approve: PASS   transferFrom: PASS
```

Or, on a real honeypot:

```
🚨 CANNOT SELL
Your buy succeeds. The same wallet's sell reverts.
Reason: transfer to the Uniswap pair reaches frame 7 → blacklist condition → REVERT
[Show execution]  ← opens Workbench at the exact frame
```

**Control Risk — "Can somebody change the rules later?"** A different question, reported
separately, never merged into one number:

```
OWNER CONTROL — CRITICAL
⚠ Owner can change transfer fee     ⚠ Owner can blacklist wallets
⚠ Owner can pause trading           ⚠ Owner can mint additional supply
⚠ Contract is upgradeable           ⚠ Proxy admin remains active
```

The distinguishing move here is running the privileged action as a counterfactual, not
just detecting that the function exists:

```
CURRENT STATE:        sell → succeeds
SIMULATED OWNER ACTION: setTax(100%)
THEN:                  sell → succeeds, but returns ~0

Result: the owner currently has the capability to make this token
        effectively unsellable.
```

That's a materially stronger claim than a static `modifiable_fee: true` flag — it shows
what the authority can actually cause, executed, not inferred.

Two more verdicts complete the report, and both require data the token contract itself
does not fully provide — flagged honestly rather than promised for free:

**Tax** — what you actually lose during buy and sell (read directly off the executed
scenario above, so this one's cheap).

**Liquidity** — can whoever controls the pool remove it? This needs a second data domain
beyond the token contract: which LP position/tokens exist, whether LP is burned or
timelocked, concentrated-liquidity NFT ownership, locker-contract identity and unlock
date, liquidity-manager permissions. This is real, separate integration work (indexing
known locker contracts, LP NFT ownership lookups) and should not be promised in the first
prototype.

| Verdict | Question |
|---|---|
| **Trade** | Can I actually buy, transfer, and sell it right now? |
| **Tax** | What do I actually lose doing that? |
| **Control** | Can privileged actors change trading, supply, or the implementation? |
| **Liquidity** | Can someone controlling liquidity remove it? (needs a separate data source — later) |

---

## 2. What this actually requires to build (the part that isn't "framing")

```
Chain RPC
   ↓
Pin block N
   ↓
ForkStateProvider
   ├── account/code
   ├── balances
   ├── storage
   ├── pool state
   └── block environment
            ↓
        Schlieren EVM
```

`Schlieren.Core/State/ForkingGlobalState.cs` is the substrate this builds on, but a
production `ForkStateProvider` needs: a pinned-block snapshot so every scenario in a
report (buy, sell, the owner counterfactual) runs against the *same* reproducible state
rather than drifting between calls; real RPC reliability at consumer-product uptime, not
internal-tool uptime; and a caching layer so repeated checks against popular
tokens/pools don't refetch the same accounts. That's the genuine new engineering line
item — not paint on an existing capability.

---

## 3. Where this sits relative to Hunter and Workbench

Guard is the consumer-facing product. Hunter's multi-client differential machinery
(Rev. 3–4) is not the same thing and is not renamed into this — it stays underlying
technology, one generator family among several feeding the same scenario-execution core:

```
                SCHLIEREN ENGINE
                       │
               Forked Live State
                       │
             Scenario Execution
                       │
       ┌───────────────┴──────────────┐
       │                              │
   Guard (consumer)              Workbench (expert)
       │                              │
 Buy / Sell / Rug check       Full forensic trace
 Plain-language verdict
```

```
Generator Families (feeding Scenario Execution)
├── EVM semantic boundaries        (Hunter)
├── Differential client testing    (Hunter)
├── Transaction replay             (Hunter)
└── Token Risk Scenarios           (Guard)
    ├── buy → sell
    ├── transfer / approve → transferFrom
    ├── owner-privilege counterfactual
    ├── proxy upgrade
    └── liquidity control (needs §1's second data domain)
```

"Show execution" in Guard's report opens the same Workbench that Hunter's divergence
cards use — same underlying diagnostic bundle, different entry point.

---

## 4. Market validation — this is a real, proven behavior, not a hypothesis

Token-risk checking is a demonstrated user behavior, not a guess: TokenSniffer had an
estimated 66,430 visits in July 2026 (7.4 pages/visit, 10+ minutes/visit, 59% direct
traffic — people who already know the tool and go there deliberately, not accidental
searchers). RugCheck (the Solana-side equivalent) had ~271,000 visits in June 2026, 81%
direct. Honeypot.is was smaller, ~26,000 monthly visits. On the embedded/API side, GoPlus
reports ~10M daily calls today; a 2025 CoinDesk Research report put its Token Security
API at ~717M calls/month plus ~350M monthly blockchain-level requests (including
transaction simulation), a 125,000-DAU browser extension, and ~$4.7M 2025 revenue across
products. A 2025 peer-reviewed study of newly created Uniswap V2 tokens found ~88%
exhibited honeypot-style blocked/locked-sale behavior at some point; Honeypot.is caught
91.6% of that set, GoPlus 81.6% — meaning even the incumbents have a real, measurable
miss rate. EVM DEX volume (Ethereum, BSC, Base) remains substantial per DefiLlama. The
need is real and people already act on it.

**But generic search demand is small and this is not an SEO market.** U.S. search volume
for "honeypot checker" (~260/mo) and "honeypot detector" (~110/mo) is tiny next to
branded searches like "token sniffer" (~1,900/mo). People learn "paste the CA into
TokenSniffer" as a specific verb/tool, not a category search. This is a trust/
distribution market: Guard has to get placed where the decision happens — Telegram/
Discord token communities, X, wallets, DEX frontends, trading bots, browser extensions,
APIs consumed by wallets/trading apps — not win on SEO.

**Basic scanning is expected to be free.** GoPlus's own model is free-tier scanning with
paid high-volume API access starting around $199/month. A "$5 to scan a token" paywall on
the consumer product is a weak model and shouldn't be built:

```
FREE (distribution)                    PREMIUM / API (revenue)
Paste address                          Privileged-action counterfactuals
→ Can I buy? Can I sell?                Continuous monitoring
→ Actual loss/tax                       Implementation/owner-capability change alerts
→ Why (causal explanation)              Wallet/DEX integration
                                         High-volume simulation, evidence API
```

The consumer scan is the distribution mechanism, not the business. The API/integration
layer is where revenue plausibly lives, same as GoPlus's own shape.

---

## 5. Competitive reality, stated plainly

TokenSniffer tracks roughly 59M tokens across 15 chains; GoPlus has analyzed 1.37M+
tokens with sub-3-second responses; Honeypot.is covers Ethereum, BSC, and Base. Guard
will not out-checkbox any of them, and won't match their chain coverage or response
latency for a long time. The only defensible wedge is the one in §0: execution proof
instead of a score, and a "click to watch exactly how" evidence trail none of them offer.
Competition is strong and Schlieren does not currently have a demonstrated superior
product — that's exactly what the gate below is designed to establish before anything
bigger gets built.

---

## 6. The actual go/no-go gate

Build exactly one thing. No liquidity analysis, no dashboard, no multichain, no wallet
extension, no user accounts, no payment, no 0–100 score, no Hunter integration.

**Ethereum ERC-20 address → pinned-block fork → real Uniswap buy → approve → real
Uniswap sell → plain-English result → "Show execution" opens Workbench at the exact
frame.**

Then run this qualification matrix through Guard, Honeypot.is, TokenSniffer, and GoPlus:

| Case | Guard must prove |
|---|---|
| Normal token | Sell succeeds |
| Honeypot | Sell fails, **and why** (exact frame/condition) |
| High-tax token | Sell succeeds; actual loss measured precisely |
| Cooldown / anti-bot delay | Immediate sell fails, but Guard does **not** falsely call it a honeypot |
| Privileged control present | Current sell passes, but an owner action can change that — shown as an executed counterfactual, not a static flag |

**The bar for continuing is not "does it work."** It's: can Guard produce information the
incumbents don't. If the result across this matrix is "Honeypot.is says HONEYPOT, Guard
says HONEYPOT with a prettier trace" — stop, the added complexity isn't justified. If
instead Guard correctly distinguishes a cooldown false-positive from a real honeypot with
the exact condition shown, or shows an executed owner-counterfactual where a competitor
only reports a static "owner present" flag — that's a real signal worth building on.

**Decision as it stands:** market existence — yes, proven. Users actively checking
tokens before trading — yes. Enough activity for a real product — yes. People likely to
pay directly for an ordinary honeypot check — probably no (see §4's free/premium split).
Competition — very strong. A demonstrated Schlieren advantage over incumbents — not yet.
Worth spending the compute to build the narrow prototype and find out — yes. Worth
committing to full Guard product development today — no. Note also that Guard has a
meaningfully better shot at real user adoption than commercial Hunter does — Hunter
serves a handful of client teams; Guard addresses a question hundreds of thousands of
crypto users already demonstrably ask.

---

## 7. Everything from Rev. 3/4 that still stands, unchanged

Hunter's adjudication pipeline (agreement is evidence, not correctness — candidate
divergence → apparatus cleared → semantics matched → implementation divergence → spec
adjudicated → reportable defect), the REVM-harness correction (the Berlin SSTORE case was
Schlieren's own harness bug, never sent to the Ethereum Foundation, but
`oracle/revm-harness` still needs the fix), and the `Schlieren.Differential` /
`Schlieren.Harvest` / `Schlieren.Hunter` architecture split all carry forward unchanged —
Hunter is still real, still a module, just further from the front door than Guard now is.
