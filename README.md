# Schlieren

Windows-native Ethereum development platform. Guard is the primary product — it runs a simulated buy → approve → sell against any ERC-20 token on a forked mainnet and measures the real round-trip trading cost.

---

## How it runs

```
Browser  (localhost:3000)
    │  /rpc proxy
    ▼
Local Schlieren.CLI  (localhost:18545)
    │  fork-url
    ▼
https://schlieren.soundersolution.com
    │
    ▼
AWS ALB → EC2 Reth node  (:8545, loopback only)
```

**Only Reth lives on EC2.** The Guard engine runs on your machine. The cloud node is a read-only chain-data source — the browser never talks to it directly.

---

## Quick start

### 1. Start the local Guard RPC server

Open a terminal and leave it running:

```bash
cd C:\projects\Schlieren

dotnet run --project Schlieren.CLI/Schlieren.CLI.csproj -c Release -- ^
  node --host 127.0.0.1 --port 18545 ^
  --fork-url https://schlieren.soundersolution.com
```

Wait for the line: `[IOCP Server] Listening on port 18545`

### 2. Start the UI

Open a second terminal:

```bash
cd C:\projects\Schlieren\schlieren-ui
npm install       # first time only
npm run dev -- --port 3000
```

Open **http://localhost:3000** in your browser. The connection indicator in the top-right should go green.

### 3. Run a Guard check

1. Click **Guard** in the nav
2. Paste any ERC-20 token address
3. Hit **Run** — Guard simulates a buy and sell against the live forked state and reports the result

---

## Verify the stack is working

```bash
# Is the local node up?
curl -s -X POST http://localhost:18545 ^
  -H "Content-Type: application/json" ^
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"web3_clientVersion\",\"params\":[]}"
# Good: {"result":"Schlieren/1.0.0.0",...}

# Is the EC2 Reth node reachable?
curl -s -X POST https://schlieren.soundersolution.com ^
  -H "Content-Type: application/json" ^
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_blockNumber\",\"params\":[]}"
# Good: {"result":"0x..."}  (a recent block number)
```

---

## Port reference

| Port | Service | Where |
|------|---------|-------|
| 3000 | Vite dev server | Your machine |
| 18545 | Schlieren Guard RPC | Your machine |
| 8545 | Reth (loopback) | EC2 — never public |
| 443 | HTTPS ALB | AWS — fork source only |

---

## Troubleshooting

**"Method not found: schlieren_guard"**
The UI is hitting the AWS node instead of your local Guard server. Check `schlieren-ui/vite.config.ts` — the proxy target must be `http://127.0.0.1:18545`, not the ALB URL.

**Port 3000 already in use**
```bash
netstat -ano | findstr ":3000"
# find the PID in the last column, then:
taskkill /PID <pid> /F
```

**Local node crashes on startup**
Build it first to surface any errors:
```bash
dotnet build Schlieren.CLI/Schlieren.CLI.csproj -c Release
```

**Connection indicator stays red**
Both services need to be running. Check the terminal running `dotnet run` — if it stopped, restart it.

---

## Project structure

```
Schlieren.CLI/          Entry point — CLI commands (node, guard, trace, harvest…)
Schlieren.RPC/          JSON-RPC server + method handlers
Schlieren.Guard/        Guard engine — simulated token trade logic
Schlieren.Core/         EVM execution engine
Schlieren.Harvest/      Harvest certification pipeline
schlieren-ui/           React/Vite front-end
demo/                   Final demo video + planning docs
tools/                  Utility scripts
infra/                  AWS infrastructure scripts
```

---

## AWS infrastructure

- **EC2 instance:** `i-000aa2178570bbfab` (us-east-1a)
- **ALB:** `schlieren-alb` → target group `schlieren-node-tg` → EC2:18545
- **Domain:** `schlieren.soundersolution.com` (CNAME → ALB)
- **TLS cert:** ACM-managed, auto-renews
- **Reth** and **Lighthouse** run as systemd services on the instance

No SSH or SSM tunnels needed for normal operation.

---

## Guard contact

Guard@soundersolution.com
