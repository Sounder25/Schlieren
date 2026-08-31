# Schlieren Guard — Demo Script
**Target runtime: 2:30 | Maximum: 3:00**
**Video file: Schlieren_Guard_Demo_v1.mp4**
**Narration: caption track (silent or voiceover)**

---

## PRE-DEMO SETUP

1. Open `demo/guard-ui/index.html` in Chrome, fullscreen (F11), 1920×1080
2. Start screen recording (OBS / Windows Game Bar Win+G / Playwright)
3. Confirm all 4 case pills are visible on landing screen

---

## 0:00–0:15 — HOOK

**Screen:** Landing page — SCHLIEREN / GUARD headline visible

**Caption/narration:**

> Guard buys and sells before you do, so you know what to expect.

> Before risking capital on a token, Guard evaluates the complete trade path:
> buy it, sell it, measure the outcome, and show the execution evidence.

*Hold 3 seconds on the landing screen with the slogan visible.*

---

## 0:15–0:40 — CASE A: ESTABLISHED TOKEN (PEPE)

**Action:** Click "PEPE — Normal round trip" pill

**Watch:** Animation runs through PIN → BUY → APPROVE → SELL → ADJUDICATE

**Result screen shows:**
- BUY: EXECUTED
- SELL: EXECUTED
- Start: 2.0 ETH
- Recovered: 1.9784 ETH
- Round-trip loss: 1.08%
- Block: 25,872,402

**Caption/narration:**

> This is PEPE — a real, established token evaluated against real Ethereum state
> at block 25,872,402.

> Guard executed the complete buy and sell path.
> Both sides committed. The measured round-trip loss was 1.08%.

*Pause 3 seconds on result before moving on.*

---

## 0:40–1:15 — CASE B: THE DIVERGENCE (DCAT)

**Action:** Click "← CHECK ANOTHER TOKEN", then click "DCAT — Honeypot.is: low risk. Guard: SELL BLOCKED." pill

**Watch:** Animation — BUY completes, SELL shows REVERT

**Result screen shows:**
- BUY: EXECUTED
- SELL: REVERTED
- GUARD RESULT: SELL BLOCKED

**Action:** Click "VIEW EXECUTION EVIDENCE"

**Evidence drawer opens — highlight the competitor comparison rows:**
- `Honeypot.is result: isHoneypot=false / risk=low / sellTax=3.19%`
- `Guard result: SELL BLOCKED — sell reverted at causal frame #4`

**Caption/narration:**

> Diamond Cat. Honeypot.is rates this token: not a honeypot,
> low risk, 3.19% sell tax.

> Guard executed the actual sell.

> It reverted.

> The causal frame is here — frame 4, the token's own transferFrom call.
> That's not a score. That's what happened when the trade ran.

*Hold 5 seconds on the open evidence drawer showing both rows.*

---

## 1:15–1:50 — CASE D: BRAND-NEW TOKEN (PHOENIX)

**Action:** Go back to landing, click "Phoenix — New token" pill

**Watch:** Animation runs normally → SELL SUCCESSFUL

**Result screen — age badge visible:**
- TOKEN AGE: ~9 minutes
- BUY: EXECUTED
- SELL: EXECUTED  
- Round-trip loss: 1.14%
- Block: 25,872,450

**Caption/narration:**

> This token launched approximately 9 minutes before this Guard scan.

> There's no reputation score. No historical victims.
> GoPlus has no data on it at all.

> Guard doesn't need a reputation score.
> It evaluates what matters: what actually happens when you try to buy and sell.

> At this state and trade size, the round trip executed.
> Observed loss: 1.14%.

*Hold 4 seconds on result.*

---

## 1:50–2:10 — COMPETITIVE CONTEXT

**Screen:** Stay on Phoenix result. Optionally cut to a browser tab showing Honeypot.is
with a DCAT search showing "Not a honeypot / Low Risk"

**Caption/narration:**

> Honeypot.is, GoPlus, Quick Intel, and TokenSniffer already perform valuable work —
> contract analysis, honeypot checks, buy/sell simulation, risk scoring.

> Guard isn't pretending those tools don't exist.

> Guard's question is narrower:
> exactly what happens across the complete round trip,
> and what execution evidence explains it?

> The DCAT result you just saw is the difference.
> A score said low risk. The execution said the sell reverts.

---

## 2:10–2:30 — PRODUCT POSITION + CTA

**Screen:** Return to Guard landing or show a clean title card

**Caption/narration:**

> The execution engine underneath this is real.
> Schlieren is 100% EELS-certified across every Ethereum fork from Frontier through Osaka.
> 1,350 conformance cases. Zero divergences.

> The commercial Guard product is intentionally still early.
> We haven't decided whether it becomes a wallet integration, an API,
> an exchange listing control, or something else.

> That's exactly what we're asking our first partners to help determine.

**Show CTA card:**

```
SCHLIEREN GUARD

Founding Design Partner Program
3 positions · $1,250

Partners receive:
· Direct input into Guard's product design
· Priority API access
· $1,250 credited toward first commercial contract
· Private technical sessions

guard@schlieren.xyz
```

**Final frame:**

> Guard buys and sells before you do, so you know what to expect.

> SCHLIEREN

---

## POST-RECORDING VERIFICATION CHECKLIST

Before sharing the video, confirm:

- [ ] All token addresses visible on screen are correct
- [ ] All block numbers match evidence files
- [ ] DCAT competitor comparison rows are readable in the recording
- [ ] Phoenix age badge (~9 minutes) is visible
- [ ] Evidence drawer opens and closes cleanly
- [ ] No narration claims the execution is "live" — it's presented as evidence from block N
- [ ] CTA email address is visible and readable
- [ ] Video is under 3:00
- [ ] Resolution is 1920×1080

---

## TRUTHFULNESS NOTES (per mission order §18)

The UI presents **pre-generated evidence artifacts** from real Schlieren executions.
Clicking RUN GUARD plays an animation and then displays the frozen evidence.
It does not execute the scan live in the browser.

Acceptable narration:
> "Guard evaluated this token at block 25,872,450."
> "Here is the Guard result from Phoenix at block N."

Do NOT say:
> "Guard is buying this token right now."
> "This is executing live."

The execution was real. The presentation is evidence-driven.
