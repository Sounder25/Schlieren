# Guard Demo — Final Assembly

## THE TOKENS

### Token 1: Imperial Oracle Throne (IOT)
**Contract:** `0x3a91699d96D3b8c4A1Be05E86D9A7963b4e246E1`
**Age:** 6 minutes when pulled
**Liquidity:** $18K

| Tool | Result |
|------|--------|
| FindCoin.app | HIGH RISK (45/100) — rug-pull indicators, 100% supply in top 10 wallets |
| Honeypot.is | NOT A HONEYPOT, 0% buy tax, 0% sell tax |
| GoPlus | 0 risk items, 0 attention items, contract verified |
| Guard Run 1 | Block 25,876,526 → SELL SUCCESSFUL, 1.01% loss |
| Guard Run 2 | Block 25,876,582 → SELL SUCCESSFUL, 1.03% loss |

**Evidence:** Two execution screenshots, 56 blocks apart (11 minutes). Same result. Reproducible.

---

### Token 2: Synthara Inc (SNTI)
**Contract:** `0x0bD86f9844ECdda91D072dBb4C3ECe9fDe78D9fE`
**Age:** 7 minutes when pulled

| Tool | Result |
|------|--------|
| Honeypot.is | NOT A HONEYPOT, 0% tax, LOW RISK |
| GoPlus | 404 NOT FOUND |
| Guard | SELL SUCCESSFUL (video shows execution) |

**Evidence:** Fresh Token pull video (18.7s)

---

## THE NARRATION

### Positioning
Guard doesn't compete with Honeypot.is or GoPlus. They answer different questions.

| Tool | Question It Answers |
|------|---------------------|
| Honeypot.is | "Can a simulated swap succeed?" |
| GoPlus | "Does this contract have known red flags?" |
| FindCoin | "What's the risk score based on holder distribution?" |
| **Guard** | **"What happens to my trade right now?"** |

Guard executes the actual Buy → Approve → Sell path against the current Ethereum block and measures the loss.

---

### Demo Script (3:00)

#### 0:00-0:20 — The Problem
"Token launched 6 minutes ago. Your scanning tools — GoPlus, Honeypot, FindCoin — they're all looking at different things. One says HIGH RISK. One says CLEAN. One says NOT A HONEYPOT. Which one do you trust?"

#### 0:20-0:50 — The Competitors
[Show FindCoin.app HIGH RISK screenshot]
"FindCoin says: 45 out of 100, HIGH RISK. Rug-pull indicators. Top 10 wallets hold 100% of supply."

[Show GoPlus CLEAN screenshot]
"GoPlus says: Zero risk items, zero attention items. Contract verified. Looks safe."

[Show Honeypot.is result]
"Honeypot says: Not a honeypot. Zero percent tax. Low risk."

#### 0:50-1:20 — Guard Execution
"So what actually happens if you buy?"

[Show Guard execution — Run 1]
"Block 25,876,526. Guard executes the full path: Buy, Approve, Sell. No simulation. Real EVM execution against a pinned state."

"Result: SELL SUCCESSFUL. 1.01% round-trip loss."

#### 1:20-1:50 — The State Change (PROOF)
[Show Guard execution — Run 2]
"Run it again. 56 blocks later — about 11 minutes. New buyer address. New Ethereum state."

"Result: SELL SUCCESSFUL. 1.03% loss."

"This isn't a stored rating. It's a fresh execution against current blockchain state. Same token, different block, same result."

#### 1:50-2:20 — The Differentiator
"GoPlus returns 404 on tokens this new. Honeypot runs a simulation. FindCoin checks holder distribution."

"Guard buys and sells before you, so you know what to expect."

"Your users aren't asking 'is this a honeypot?' They're asking 'what happens to my money?' Guard answers that question directly."

#### 2:20-3:00 — CTA
"Founding Design Partner program. Three positions."

"$1,250 deposit credited toward your first commercial contract. 60-day pilot scope. We build what YOUR wallet needs — your chain, your venue, your integration."

"Email guard at schlieren dot xyz. Book a call. We'll run it live on your token."

---

## VISUAL ASSETS

| Asset | File | Use At |
|-------|------|--------|
| Guard Run 1 | Screenshot | 0:50-1:00 |
| Guard Run 2 | Screenshot | 1:20-1:30 |
| FindCoin HIGH RISK | Screenshot | 0:25-0:35 |
| GoPlus CLEAN | Screenshot | 0:35-0:45 |
| Honeypot.is result | Screenshot | 0:45-0:50 |
| Fresh Token pull video | 18.7s clip | B-roll |
| 2 comp video | Cut clips | Competitor section |

---

## EDIT CHECKLIST

1. [ ] Extract clean FindCoin screenshot
2. [ ] Extract clean GoPlus screenshot
3. [ ] Extract clean Guard Run 1 screenshot
4. [ ] Extract clean Guard Run 2 screenshot
5. [ ] Record voiceover (3:00 script)
6. [ ] Cut 2 comp video to remove dead time
7. [ ] Sequence: Competitor screenshots → Guard execution → CTA
8. [ ] Add text overlays for competitor results
9. [ ] Add Guard logo + tagline at close
10. [ ] Export final demo

---

## TAGLINE

**"Guard buys and sells before you, so you know what to expect."**

---

## CONTACT

guard@schlieren.xyz

---

## PRICING

- Founding Design Partner: $1,250 (3 positions)
- 60-day pilot: $2,500
- Messaging: "Buy in now, we build what you need."
