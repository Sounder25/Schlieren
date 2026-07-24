# Scrutor Muscle (Hardhat)

Hardhat project that treats **Scrutor** as the local Anvil-compatible node.

RPC: `http://127.0.0.1:18545` · chainId `31337` · Anvil test mnemonic (account #0 funded).

## Prerequisites

- Node 18+
- Scrutor built (`dotnet build` in repo root)

## 1. Start Scrutor (must use this mnemonic)

From `C:\projects\Scrutor`:

```powershell
.\Scrutor.CLI\bin\Debug\net8.0\Scrutor.CLI.exe `
  --host 127.0.0.1 --port 18545 --accounts 3 --balance 10000 `
  --mnemonic "test test test test test test test test test test test junk"
```

Hardhat’s default account #0 is `0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266`. Without the mnemonic above, that key has zero balance and deploys fail.

## 2. Install & compile

```powershell
cd muscle
npm install
npm run compile
```

## 3. Muscle smoke / deploy / tests

```powershell
npm run smoke          # deploy Counter, increment, assert
npm run deploy         # same path, slightly more logging
npm test               # mocha suite on --network scrutor
```

Override RPC if needed:

```powershell
$env:SCRUTOR_RPC = "http://127.0.0.1:18545"
npm run smoke
```

## Layout

| Path | Role |
|------|------|
| `contracts/Counter.sol` | Trivial stateful contract |
| `scripts/smoke.js` | Gate: deploy + call on Scrutor |
| `scripts/deploy.js` | Deploy helper |
| `test/Counter.scrutor.js` | Integration tests against live node |
| `hardhat.config.js` | `scrutor` network → port 18545 |

## Relation to other gates

| Gate | What it proves |
|------|----------------|
| `scripts/anvil-smoke.ps1` | Raw JSON-RPC muscle-lite (no Solidity) |
| **This package** | Real toolchain deploy/call (Hardhat) |
| EELS fixtures | Spec math/gas (later, not day-one) |

## Known limits (Scrutor)

- Pairing precompile fail-closed for k>0
- Snapshot/revert may not fully rewind block number
- Prefer `eth_sendRawTransaction` path (Hardhat signs locally)
