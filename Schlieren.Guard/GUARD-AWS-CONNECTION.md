# Schlieren Guard — AWS Connection & Usage

## Quick Start

```bash
cd C:\projects\Schlieren
python tools/guard-run.py 0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48
```

Drop a token address in. Get a verdict back. That's the whole interface.

---

## What Guard Does

Guard is a token risk checker built on Schlieren's EVM execution engine. Given an ERC-20 token address, it:

1. **Buys** the token via Uniswap V2 with a synthetic wallet
2. **Approves** the Router to spend the tokens
3. **Sells** the tokens back to WETH
4. **Reports** what happened — including the exact revert reason, gas costs, and round-trip loss

All of this runs against a **real, fully-synced Ethereum node** — not a simulator, not a third-party API. Guard executes the actual EVM bytecode at the actual chain state.

---

## How It's Connected

```
┌─────────────────────┐          AWS Systems Manager          ┌──────────────────────────┐
│  Your Windows PC    │  ─── SSM SendCommand (encrypted) ───> │  EC2 Instance            │
│                     │                                        │  (i-000aa2178570bbfab)   │
│  tools/guard-run.py │                                        │                          │
│                     │                                        │  ┌──────────────────────┐│
│                     │                                        │  │ Guard CLI (.NET 8)   ││
│                     │                                        │  │    ↓ eth_call         ││
│                     │                                        │  │ Reth (127.0.0.1:8545)││
│                     │                                        │  │    ↓ Lighthouse CL   ││
│                     │                                        │  │ Ethereum P2P Network ││
│                     │                                        │  └──────────────────────┘│
│                     │  <── SSM GetCommandInvocation ──────── │  Evidence JSON output     │
└─────────────────────┘                                        └──────────────────────────┘
```

**No SSH. No tunnels. No open ports.** 

The entire communication happens through AWS Systems Manager (SSM), which is:
- Encrypted end-to-end
- Authenticated via IAM (no SSH keys to manage)
- Logged in CloudTrail
- The Reth RPC never touches the internet — it listens on `127.0.0.1` only

---

## Commands

### Check Node Health
```bash
python tools/guard-run.py --health
```
Output:
```
✓ Reth RPC alive — block 25,885,898 (0x18afcca)
✓ Not syncing (fully synced)
Services: active
Disk usage: 38%
```

### Run Guard on a Token
```bash
python tools/guard-run.py 0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48
```
Output:
```
SELL SUCCESSFUL
Buy and sell both committed. Measured round-trip loss 0.62%.

  [PASS] buy            gas=132751
  [PASS] approve        gas=55570
  [PASS] sell           gas=113885
```

### Pin to a Specific Block
```bash
python tools/guard-run.py 0xABCD... --block 25861963
```

### Republish Guard After Code Changes
```bash
python tools/guard-run.py --publish
python tools/guard-run.py 0xABCD... --publish   # publish then run
```

### Get Raw Evidence JSON
```bash
python tools/guard-run.py 0xABCD... --raw
```

---

## Prerequisites

| Requirement | How to Check |
|---|---|
| AWS CLI v2 | `"C:\Users\Erick\AppData\Local\Programs\Amazon\AWSCLIV2\aws.exe" --version` |
| AWS credentials | `aws sts get-caller-identity` — should show `hermes-agent-bedrock` |
| SSM permissions | The IAM user needs `ssm:SendCommand` and `ssm:GetCommandInvocation` |
| Python 3.11+ | `python --version` |

**You do NOT need:**
- SSH keys
- Port forwarding
- Any open ports on the EC2 instance
- VPN or direct network access to the node

---

## Infrastructure Details

| Component | Value |
|---|---|
| EC2 Instance | `i-000aa2178570bbfab` |
| Region | `us-east-1a` |
| Reth HTTP RPC | `http://127.0.0.1:8545` (localhost only) |
| Reth WebSocket | `ws://127.0.0.1:8546` (localhost only) |
| Lighthouse Beacon | `http://127.0.0.1:5052` (localhost only) |
| Guard CLI path | `/opt/schlieren-guard/Schlieren.CLI` |
| Guard evidence | `/opt/guard-out/guard-<token>.json` |
| Source code | `/opt/schlieren` (branch: `feature/schlieren-guard`) |
| Runner script | `C:\projects\Schlieren\tools\guard-run.py` |

---

## Guard Verdicts

| Verdict | Meaning |
|---|---|
| `SellSuccessful` | Token bought and sold normally. Reports round-trip loss %. |
| `SellBlocked` | Buy worked, sell reverted. Reports revert reason and causal frame. |
| `SellDelayed` | Immediate sell failed, but sell after +1 block/+12s succeeded. Cooldown token. |
| `BuyFailed` | Couldn't buy — no pool, dead contract, or buy restriction. |
| `Inconclusive` | Something unexpected — approve failed, no pair found, etc. |

---

## Troubleshooting

| Problem | Solution |
|---|---|
| "Reth RPC not responding" | Run `--health`. If services inactive, restart instance from AWS Console. |
| "Still syncing" | Wait for Reth to catch up. Guard needs fully synced state. |
| Command times out | Use `--timeout 300`. Normal runs take 1-2s but complex contracts can take longer. |
| Guard exit 2 | Internal RPC error. Check `--health` first. |
| Guard exit 3 | Inconclusive — token has no Uniswap V2 pool or buy failed. |
| "Publish failed" | Git conflicts on the instance. Fix manually via SSM. |
| Evidence truncated | SSM has 24KB stdout limit. Use `--raw` or read evidence directly on instance. |

---

## For Hermes Agent

When a user drops a token address in chat, run:

```python
import subprocess
result = subprocess.run(
    ["python", "tools/guard-run.py", token_address],
    capture_output=True, text=True, timeout=180,
    cwd=r"C:\projects\Schlieren"
)
print(result.stdout)
if result.returncode != 0:
    print(result.stderr)
```

For batch runs, call the script once per token. Each run is independent.

---

## Security Notes

- **RPC is loopback-only.** Reth listens on 127.0.0.1:8545. It has never been and should never be exposed to the internet.
- **SSM, not SSH.** All remote commands go through AWS Systems Manager. No SSH daemon is required (it's disabled by default on the instance).
- **No secrets in code.** The script uses the locally configured AWS CLI identity. No API keys, no passwords, no PEM files.
- **IAM-scoped.** The `hermes-agent-bedrock` user has only the permissions it needs.
