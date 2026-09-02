# Guard 3-Minute Demo — Final Script
## Target: Wallet/trading founders who need evidence
## Core Message: "Execution beats simulation. Period."
## Includes: Competitor comparison + Performance contract offer

---

## AUDIO DESIGN NOTES

**Sound layers:**
- Bed: Subtle electronic pulse (60 BPM, like a heartbeat)
- SFX: Terminal keystrokes, RPC call "blips", silence for impact
- Voice: Close-mic'd, intimate, confident

**Music:**
- Opening: Building tension, minor key
- Competitor section: False-major (sounds safe, is wrong)
- Guard execution: Mechanical, precise like a machine
- Evidence reveal: Silence, then single piano note
- CTA: Clean resolve

---

## SCENE 1: THE BLIND SPOT (0:00-0:30)

**Visual:**
```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│   TODAY'S TOKEN SCANNERS                                  │
│                                                             │
│   [Database] → [Simulation] → [Score]                       │
│                                                             │
│   Works great for tokens launched...                        │
│                                                             │
│              [yesterday] [last week] [known]                │
│                                                             │
│   But for tokens launched 15 minutes ago?                   │
│                                                             │
│   [EMPTY DATABASE ICON]                                     │
│                                                             │
│   You're flying blind.                                      │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Voiceover:**
"If you're a wallet, your users ask one thing: Is this token safe to buy?

Today you check GoPlus. Check Honeypot.is. Get a score.

That works fine for tokens launched yesterday. Last week. Three months ago.

But what about tokens launched fifteen minutes ago?

[pause]

You're flying blind."

**SFX:** Heartbeat stops. Silence (2 seconds).

---

## SCENE 2: THE EXECUTION (0:30-1:05)

**Visual:** Terminal fills screen

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  $ schlieren guard 0x5be4ea5d...                           │
│                                                             │
│  [PINNING] Block 25872450 @ 2026-08-31 02:54:35Z           │
│                                                             │
│  [EXECUTING]                                                │
│                                                             │
│    → BUY     0.05 ETH → 2,847,293 tokens                   │
│      ✓ Success  gas=151,264                                 │
│                                                             │
│    → APPROVE Router02 for full balance                    │
│      ✓ Success  gas=46,277                                  │
│                                                             │
│    → SELL    2,847,293 tokens → ETH                         │
│      ✓ Success  gas=125,157                                 │
│                                                             │
│  ╔═══════════════════════════════════════════════════════╗│
│  ║  SELL SUCCESSFUL                                      ║│
│  ║  Round-trip loss: 1.14%                               ║│
│  ║  Token age: 15 minutes                                ║│
│  ╚═══════════════════════════════════════════════════════╝│
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Voiceover (whispered during execution):**
"Pin the state. Fund a synthetic buyer. Execute the actual path. Buy. Approve. Sell. No simulation. Real EVM execution against real block 25872450.

Result: SELL SUCCESSFUL. One point one four percent loss."

**SFX:** Each checkmark = mechanical "click." Success = clean tone.

---

## SCENE 3: COMPETITOR OUTPUT (1:05-1:20)

**Visual:** Split screen — GoPlus response

```
┌─────────────────────────────┬───────────────────────────────┐
│                             │                               │
│  GOPLUS API RESPONSE        │  SCHLIEREN GUARD             │
│                             │                               │
│  {                          │  Token: 0x5be4ea...            │
│    "token": "0x5be4ea...",   │  Age: 15 minutes              │
│    "is_honeypot": null,    │                               │
│    "buy_tax": null,        │  BUY:   ✓ EXECUTED            │
│    "sell_tax": null,       │  APPROVE: ✓ EXECUTED          │
│    "risk_score": null      │  SELL:  ✓ EXECUTED            │
│  }                          │                               │
│                             │  Result: SELL SUCCESSFUL       │
│  [ERROR: Token not found]   │  Loss: 1.14%                   │
│                             │                               │
│  NULL DATA                  │  EXECUTION EVIDENCE            │
│                             │                               │
└─────────────────────────────┴───────────────────────────────┘
```

**Voiceover:**
"GoPlus at scan time: Token not found.

Guard: Full execution evidence."

**SFX:** GoPlus side = error buzz. Guard side = clean chime.

---

## SCENE 4: THE BLOCKED SELL (1:20-2:00)

**Visual:** Terminal, second token

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  $ schlieren guard 0x2d1643a5...                           │
│                                                             │
│  [PINNING] Block 25872405                                  │
│                                                             │
│    → BUY      ✓   270,394 gas                              │
│    → APPROVE  ✓    46,389 gas                              │
│    → SELL     ✗   135,309 gas [REVERT]                     │
│                                                             │
│  ╔═══════════════════════════════════════════════════════╗│
│  ║  SELL BLOCKED                                          ║│
│  ║                                                        ║│
│  ║  First causal frame: #4 at depth 3                    ║│
│  ║  Contract: 0x2d1643...                                 ║│
│  ║  Error: TRANSFER_FROM_FAILED                           ║│
│  ╚═══════════════════════════════════════════════════════╝│
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Voiceover:**
"Second token. Buy executes. Approves. Sell...

[pause]

Reverts. Frame four. Depth three. Inside the token's own transfer logic."

**SFX:** Revert sound = record scratch, then silence.

---

## SCENE 5: THE DIVERGENCE (2:00-2:30)

**Visual:** Side-by-side comparison

```
┌─────────────────────────────┬───────────────────────────────┐
│                             │                               │
│  HONEYPOT.IS                │  SCHLIEREN GUARD             │
│  Diamond Cat                │  Same Token                   │
│                             │                               │
│  ┌─────────────────────┐    │  ┌─────────────────────┐      │
│  │ ✓ NOT A HONEYPOT    │    │  │ ✓ BUY: EXECUTED     │      │
│  │ ✓ LOW RISK          │    │  │ ✗ SELL: BLOCKED     │      │
│  │ ✓ 3.19% SELL TAX    │    │  │                     │      │
│  └─────────────────────┘    │  │ Frame #4            │      │
│                             │  │ TRANSFER_FROM_FAILED│      │
│  [Safe. Green checkmarks.   │  └─────────────────────┘      │
│   Looks trustworthy.]       │  [Blocked. Red warning.       │
│                             │   The execution failed.]       │
│                             │                               │
│  SCORE: LOW RISK            │  EVIDENCE: BLOCKED            │
│                             │                               │
└─────────────────────────────┴───────────────────────────────┘
```

**Voiceover:**
"Same token. Diamond Cat.

Honeypot.is: 'Not a honeypot. Low risk. Three point one nine percent sell tax.' 

[pause]

Guard: SELL BLOCKED.

A score said safe. The execution proved otherwise.

The execution was right."

**SFX:** Honeypot side = cheerful jingle (ironic). Guard side = low piano note. Silence.

---

## SCENE 6: THE PERFORMANCE CONTRACT (2:30-2:55)

**Visual:** Contract terms appear cleanly

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  FOUNDING DESIGN PARTNER PROGRAM                            │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                                                     │   │
│  │  Performance Contract                               │   │
│  │                                                     │   │
│  │  • $1,250 one-time deposit                          │   │
│  │    → Credited toward first commercial contract        │   │
│  │                                                     │   │
│  │  • 60-day pilot scope                                 │   │
│  │    → Tailored to YOUR specific needs                │   │
│  │    → Pick the chain. Pick the venue.                │   │
│  │                                                     │   │
│  │  • Direct product input                               │   │
│  │    → Guard becomes what YOUR users need             │   │
│  │                                                     │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  Limited: 3 positions                                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Voiceover:**
"Founding design partner program. Three positions.

Here's the contract: You put down $1,250. We credit it toward your first commercial contract.

In those sixty days, we build what YOU need. Your chain. Your venue. Your integration.

We're not selling you a platform. We're building it with you.

But only three partners."

**SFX:** Clean, minimal bed music rises slightly.

---

## SCENE 7: CLOSE (2:55-3:00)

**Visual:** Simple text, Guard logo

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│            SCHLIEREN GUARD                                  │
│                                                             │
│            guard@schlieren.xyz                              │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Voiceover:**
"Guard. Execution evidence for tokens that don't have reputations yet.

Email guard at schlieren dot xyz."

**SFX:** Single piano note. Cut to black.

---

## VISUAL OVERLAY SPECIFICATIONS

### Token Info Overlays (appear during execution)

```
┌────────────────────────────────┐
│ TOKEN METADATA                 │
├────────────────────────────────┤
│ Contract: 0x5be4e...8955     │
│ Age: 15 minutes                │
│ Block: 25,872,450              │
│ Fork: Osaka                    │
│ Network: Ethereum Mainnet      │
└────────────────────────────────┘
```

### Competitor Data Overlays

**GoPlus:**
```
┌────────────────────────────────┐
│ GOPLUS API                     │
├────────────────────────────────┤
│ is_honeypot: null              │
│ buy_tax: null                  │
│ sell_tax: null                 │
│ owner_address: null            │
│ hold_count: null               │
├────────────────────────────────┤
│ Status: NOT IN DATABASE        │
└────────────────────────────────┘
```

**Honeypot.is:**
```
┌────────────────────────────────┐
│ HONEYPOT.IS API                │
├────────────────────────────────┤
│ isHoneypot: false              │
│ risk: "low"                    │
│ sellTax: "3.19"                │
│ buyTax: "5.00"                 │
│ flags: []                      │
├────────────────────────────────┤
│ Status: LOW RISK               │
└────────────────────────────────┘
```

---

## PRODUCTION CHECKLIST

| Element | Spec |
|---------|------|
| Resolution | 1920×1080 |
| Aspect Ratio | 16:9 |
| Frame Rate | 60 FPS (smooth terminal) |
| Color Space | Rec. 709 (sRGB safe) |
| Audio | 48kHz, stereo |
| Max Peak | -3dB (leave headroom) |
| Voice EQ | High-pass 80Hz, +2kHz presence |

---

## TRUTHFULNESS COMPLIANCE

**Must say:**
- "Fifteen minutes old AT SCAN TIME"
- "GoPlus has no data ON THIS TOKEN at scan time" (not universally)
- "Guard doesn't need a database" (true)

**Must NOT say:**
- "GoPlus is wrong" (they have no data, not wrong data)
- "Guard prevents honeypots" (it executes and reports)
- "This token is definitely a honeypot" (Guard says SELL BLOCKED—fact)

**Visual accuracy:**
- Show actual competitor captures from `competitor-captures/`
- Show actual Guard evidence JSON
- Block numbers must match evidence files
