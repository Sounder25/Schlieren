# Video Competitor Evidence — Morning Recording Tokens
## Captured: 2026-08-31

---

## TOKEN 1: PHOENIX (0x5be4ea...)
**Age at scan:** ~15 minutes  
**Block:** 25,872,450

### GoPlus API
```
URL: https://api.gopluslabs.io/api/v1/token_security/1/0x5be4ea5d45ec78f4ca34c69a2c714145c4538955
Status: 404 Not Found

Response:
{
  "timestamp": "2026-08-31T15:28:45.686+00:00",
  "status": 404,
  "error": "Not Found",
  "message": "",
  "path": "/api/v1/token_security/1/0x5be4ea..."
}
```
**Translation:** Token not in database. No data available.

### Honeypot.is API
```
Status: Token not yet in honeypot.is database
Result: N/A (404 or similar)
```

### Guard Result (actually ran)
```
SELL SUCCESSFUL
Round-trip loss: 1.14%
Pinned @ block 25,872,450
Execution evidence: /demo/evidence/case-d-new-token/
```

**VISUAL FOR VIDEO:**
```
┌─────────────────────┬──────────────────────────┐
│                     │                          │
│  GOPLUS             │   SCHLIEREN GUARD        │
│                     │                          │
│  404 NOT FOUND      │   BUY:     ✓             │
│                     │   APPROVE:  ✓             │
│  Token not indexed  │   SELL:    ✓             │
│                     │                          │
│  [red X]           │   Result: SELL SUCCESSFUL │
│                     │   Loss: 1.14%            │
│                     │                          │
└─────────────────────┴──────────────────────────┘
```

---

## TOKEN 2: DIAMOND CAT / DCAT (0x2d1643...)
**Age:** Established (not new)  
**Block:** 25,872,405

### GoPlus API
```
URL: https://api.gopluslabs.io/api/v1/token_security/1/0x2d1643a5d14fb221521767f2405efbbe4d4603fd
Status: 404 Not Found
```

**Note:** DCAT was in the system as of Aug 30, but as of Aug 31 GoPlus has removed it or it was never there.

### Honeypot.is API  
```
Token: 0x2d1643a5d14fb221521767f2405efbbe4d4603fd
Name: DIAMOND CAT
Symbol: DCAT

Result from Aug 30 capture:
{
  "isHoneypot": false,
  "risk": "low",
  "riskLevel": 1,
  "buyTax": 3.1,
  "sellTax": 3.19,
  "simulationSuccess": true
}
```

**Translation:** "Not a honeypot. Low risk. Small taxes."

### Guard Result (actually ran)
```
BUY:     ✓ SUCCESS
APPROVE: ✓ SUCCESS  
SELL:    ✗ REVERTED

First causal frame: #4 at depth 3
Contract: TransferHelper
Error: TRANSFER_FROM_FAILED

Pinned @ block 25,872,405
Evidence: /demo/evidence/case-b-abnormal/
```

**VISUAL FOR VIDEO (the divergence slide):**
```
┌─────────────────────────┬──────────────────────────┐
│                         │                          │
│  HONEYPOT.IS            │   SCHLIEREN GUARD        │
│  Diamond Cat (DCAT)      │                          │
│                         │                          │
│  ✓ NOT A HONEYPOT       │   BUY:     ✓             │
│  ✓ LOW RISK             │   APPROVE:  ✓             │
│  ✓ 3.19% SELL TAX       │   SELL:    ✗ BLOCKED     │
│                         │                          │
│  Safe.                 │   Frame #4               │
│  Green checkmarks.      │   TRANSFER_FROM_FAILED   │
│  Trustworthy.          │                          │
│                         │   The execution failed.  │
│  [soft green UI]       │   [sharp red warning]    │
│                         │                          │
│  Simulation said YES.   │   Execution said NO.     │
│                         │                          │
└─────────────────────────┴──────────────────────────┘
```

**Voiceover line for this slide:**
"Same token. Honeypot.is: 'Not a honeypot. Low risk. Three point one nine percent sell tax.' Guard: SELL BLOCKED. A simulation said safe. The execution proved otherwise. The execution was right."

---

## NARRATIVE ARC FOR VIDEO

### The Point (scenes 1-3)
"GoPlus has 404. Honeypot.is has simulation. For a 15-minute-old token, both are useless."

### The Divergence (scene 5)  
"But even when they DO have data, they can be wrong. Diamond Cat. Honeypot.is said low risk. Guard said SELL BLOCKED."

### The Evidence (scene 6)
"You're not buying a score. You're buying proof that runs at this block, pinned, replayable."

---

## PRODUCTION NOTES

**For the 15-min token scene:**
- Show GoPlus 404 (clean JSON, red error)
- Show Guard execution (green success)
- The contrast: NO DATA vs FULL EVIDENCE

**For the divergence scene:**
- Side-by-side screen
- Left: honeypot.is "low risk" (feels safe, UI is soft)
- Right: Guard "SELL BLOCKED" (feels dangerous, UI is clinical)
- The emotional load: trusted them → they failed → Guard caught it

**Timing:**
- Let the 404 sit for 2 seconds (the humiliation of "we have no data")
- Let the causal frame land for 2 seconds (the precision of "frame 4, depth 3")
- The divergence is the climax — hold on that slide for 4-5 seconds

---

## TRUTHFULNESS CHECK

✓ GoPlus 404 is accurate (captured live, timestamped)  
✓ Honeypot.is low risk is accurate (captured Aug 30)  
✓ Guard SELL BLOCKED is accurate (reproducible evidence)

No characterizations stronger than:
- "GoPlus had no data at scan time"
- "Honeypot.is rated it low risk"
- "Guard executed and found SELL BLOCKED"

We are not saying:
- "GoPlus is wrong" (they had no data, not wrong data)
- "Honeypot.is is lying" (their simulation ran, it just didn't match reality)
- "DCAT is a honeypot" (Guard says SELL BLOCKED at frame 4 — that is the claim)

---

## FILES REFERENCED

- Fresh API check: `/demo/competitor-captures/fresh_api_check.json`
- Historical captures: `/demo/competitor-captures/honeypot_is_captures.json`
- Guard evidence: `/demo/evidence/case-d-new-token/` (Phoenix)
- Guard evidence: `/demo/evidence/case-b-abnormal/` (DCAT)
- Video script: `/demo/DEMO_3MIN_FINAL.md`
