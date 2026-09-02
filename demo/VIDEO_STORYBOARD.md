# Guard 3-Minute Demo — Visual Storyboard

## Scene Breakdown

### SCENE 1: Problem Setup (0:00-0:25)
**Visual:** Three lines of text, appearing one at a time

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  TOKEN SAFETY TODAY                                         │
│                                                             │
│  GoPlus      →  Database lookup                             │
│                 "What did we see yesterday?"                  │
│                                                             │
│  Honeypot.is →  Simulation                                  │
│                 "What do we think will happen?"             │
│                                                             │
│  Guard       →  EXECUTION                                   │
│                 "What ACTUALLY happens at this block?"      │
│                                                             │
│                                    [fade to black]          │
└─────────────────────────────────────────────────────────────┘
```
**Audio:** "If you're a wallet or trading product, your users ask: Is this token safe? Today you check databases or run simulations. Both work fine for tokens launched yesterday. But what about tokens launched FIFTEEN MINUTES AGO?"

---

### SCENE 2: Token #1 — 15 Minutes Old (0:25-1:00)
**Visual:** Terminal screen, typing the command

```
┌─────────────────────────────────────────────────────────────┐
│ $ schlieren guard 0x7a2b... --fork-url http://localhost:8545│
│                                                             │
│ [PINNING block 25872450...]                                 │
│ [FUNDING synthetic buyer...]                                │
│ [BUY]  ✓ Executed  gas=151,264                              │
│ [APPROVE] ✓ Executed  gas=46,277                            │
│ [SELL] ✓ Executed  gas=125,157                              │
│                                                             │
│ ╔═══════════════════════════════════════════════════════╗ │
│ ║  SELL SUCCESSFUL                                      ║ │
│ ║  Measured round-trip loss: 1.08%                      ║ │
│ ║  Token age at scan: ~15 minutes                         ║ │
│ ╚═══════════════════════════════════════════════════════╝ │
│                                                             │
│ Evidence: /out/guard-5be4ea5d...json                       │
└─────────────────────────────────────────────────────────────┘
```

**Cut to:** Browser showing GoPlus API response

```json
{
  "token": "0x5be4ea5d...",
  "is_honeypot": null,
  "buy_tax": null,
  "sell_tax": null
  // Token not found
}
```

**Audio:** "Fifteen minutes old. GoPlus: null. Guard: full execution evidence."

---

### SCENE 3: Token #2 — Real Block, Real Failure (1:00-1:35)
**Visual:** Terminal, second token

```
┌─────────────────────────────────────────────────────────────┐
│ $ schlieren guard 0x9f4c...                                │
│                                                             │
│ [PINNING block 25872405...]                                 │
│ [BUY]  ✓ Executed  gas=270,394                              │
│ [APPROVE] ✓ Executed  gas=46,389                           │
│ [SELL] ✗ REVERTED  gas=135,309                              │
│                                                             │
│ ╔═══════════════════════════════════════════════════════╗ │
│ ║  SELL BLOCKED                                          ║ │
│ ║                                                        ║ │
│ ║  First causal frame: Frame #4 at depth 3              ║ │
│ ║  Contract: TransferHelper                             ║ │
│ ║  Error: TRANSFER_FROM_FAILED                          ║ │
│ ╚═══════════════════════════════════════════════════════╝ │
└─────────────────────────────────────────────────────────────┘
```

**Audio:** "Twenty-two minutes old. Buy went through. Sell stopped at frame four, inside the token's own transfer logic."

---

### SCENE 4: The Divergence — Why This Matters (1:35-2:15)
**Visual:** Split screen

```
┌──────────────────────────┬──────────────────────────────────┐
│   HONEYPOT.IS            │   SCHLIEREN GUARD                │
│                          │                                  │
│   Diamond Cat (DCAT)     │   Same Token                     │
│   ─────────────────      │   ─────────────────              │
│   ✓ isHoneypot: FALSE    │   ✓ BUY: Executed                │
│   ✓ Risk: LOW            │   ✗ SELL: BLOCKED                │
│   ✓ Sell Tax: 3.19%        │                                  │
│                          │   Frame #4:                      │
│                          │   TRANSFER_FROM_FAILED           │
│                          │                                  │
│   [green checkmarks]     │   [red warning]                  │
└──────────────────────────┴──────────────────────────────────┘
```

**Audio:** "Same token. Honeypot.is: 'Not a honeypot, low risk, three point one nine percent tax.' Guard: SELL BLOCKED. The execution was right."

---

### SCENE 5: The Evidence (2:15-2:45)
**Visual:** JSON evidence bundle (scrolling slow)

```
┌─────────────────────────────────────────────────────────────┐
│ {                                                          │
│   "kind": "schlieren-guard-evidence",                       │
│   "version": "1",                                          │
│   "pin": {                                                 │
│     "chainId": 1,                                          │
│     "blockNumber": 25872405,                               │
│     "blockHash": "0xabc...",                               │
│     "fork": "Osaka"                                        │
│   },                                                       │
│   "verdict": {                                             │
│     "kind": "SellBlocked",                                 │
│     "causalFrameId": 4,                                    │
│     "causalDepth": 3,                                      │
│     "causalContract": "0x2d1643..."                        │
│   },                                                       │
│   "steps": [                                               │
│     {"name": "buy", "success": true,  "gas": 270394},      │
│     {"name": "approve", "success": true, "gas": 46389},    │
│     {"name": "sell", "success": false, "gas": 135309}      │
│   ]                                                        │
│ }                                                          │
└─────────────────────────────────────────────────────────────┘
```

**Audio:** "Every run produces replayable evidence. Pinned block, exact failure frame, complete trace."

---

### SCENE 6: Call to Action (2:45-3:00)
**Visual:** Clean closing card

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│              SCHLIEREN GUARD                                │
│                                                             │
│   Founding Design Partner Program                         │
│   3 positions · $1,250                                    │
│                                                             │
│   • Priority API access                                   │
│   • Input into product design                             │
│   • Private technical sessions                            │
│                                                             │
│   guard@schlieren.xyz                                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Audio:** "Three positions. Email guard at schlieren dot xyz."

---

## Key Visual Principles

1. **No UI chrome.** Full-screen terminal, full-screen JSON. Clean.
2. **Large text.** Terminal font minimum 16pt. Values matter more than density.
3. **Color coding:**
   - Green ✓ = success
   - Red ✗ = blocked/failure
   - Yellow = warnings/cautions
   - Cyan = Guard brand accent

4. **Motion:** Fast where unimportant (terminal scrolling), slow where critical (result reveal)

5. **No music under narration.** Silence between segments lets points land.

## Recording Checklist

- [ ] Terminal set to 1920×1080, font size 16+, dark theme
- [ ] Commands pre-typed for speed (or edit out long waits)
- [ ] Result screens mocked for clarity if actual CLI output is messy
- [ ] GoPlus null response is actual API screenshot
- [ ] Voiceover recorded clean, no echo
- [ ] Total runtime checked: 2:50-3:05
