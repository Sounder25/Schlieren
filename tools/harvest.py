#!/usr/bin/env python3
"""
Schlieren Mainnet Harvester
===========================
Scans finalized Ethereum blocks, selects interesting transactions, captures
exact pre-state via debug_traceTransaction/prestateTracer, and writes
ExecutionFixture JSON files into muscle/corpus/.

Schlieren is the ONLY execution engine. The on-chain receipt is ground truth.

Usage:
    python harvest.py [--rpc URL] [--debug-rpc URL] [--limit N] [--from-block N]
                      [--corpus-dir DIR] [--no-execute]

    --rpc         : Standard eth_* RPC endpoint (default: publicnode)
    --debug-rpc   : RPC with debug_* support (Alchemy/QuickNode/local Geth archive)
                    Falls back to best-effort eth_* prestate if not set.
    --limit       : Max fixtures to harvest per run (default: 20)
    --from-block  : Start block (default: resume from checkpoint, else finalized-5)
    --corpus-dir  : Output directory (default: muscle/corpus)
    --no-execute  : Skip Schlieren replay (harvest only)

Requirements:
    Python 3.10+  — no third-party packages.

Prestate note:
    With debug_traceTransaction/prestateTracer (Alchemy free tier, QuickNode,
    local Geth archive) you get EXACT pre-state including all touched storage.
    Without debug support, falls back to eth_getBalance/Code/Nonce for
    sender+target only (accurate enough for gas/status, not full state root).
"""
import argparse
import json
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path
from typing import Optional

# ─────────────────────────────────────────────────────────────────────────────
# RPC
# ─────────────────────────────────────────────────────────────────────────────

PUBLIC_RPC     = "https://ethereum.publicnode.com"
SCRIPT_DIR     = Path(__file__).parent
REPO_ROOT      = SCRIPT_DIR.parent
DEFAULT_CORPUS = REPO_ROOT / "muscle" / "corpus"
CHECKPOINT     = REPO_ROOT / "muscle" / "corpus" / ".checkpoint.json"


def rpc(url: str, method: str, params: list, retry: int = 3) -> object:
    body = json.dumps({"jsonrpc": "2.0", "method": method, "params": params, "id": 1}).encode()
    headers = {"Content-Type": "application/json", "User-Agent": "Schlieren-Harvester/1.0"}
    for attempt in range(retry):
        try:
            req = urllib.request.Request(url, data=body, headers=headers)
            with urllib.request.urlopen(req, timeout=30) as r:
                payload = json.loads(r.read())
            if "error" in payload:
                raise RuntimeError(f"{method}: {payload['error']}")
            return payload["result"]
        except Exception as e:
            if attempt == retry - 1:
                raise
            time.sleep(1.5 ** attempt)


def hex_int(h: Optional[str]) -> int:
    if not h:
        return 0
    return int(h, 16)


# ─────────────────────────────────────────────────────────────────────────────
# TRANSACTION SELECTOR
# ─────────────────────────────────────────────────────────────────────────────

# Addresses of known high-value protocols (battle-tested corpus seeds)
KNOWN_PROTOCOLS: set[str] = {
    "0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2",  # WETH9
    "0x7a250d5630b4cf539739df2c5dacb4c659f2488d",  # Uniswap V2 Router
    "0x68b3465833fb72a70ecdf485e0e4c7bd8665fc45",  # Uniswap V3 Router2
    "0xe592427a0aece92de3edee1f18e0157c05861564",  # Uniswap V3 SwapRouter
    "0x1111111254eeb25477b68fb85ed929f73a960582",  # 1inch v5
    "0x87870bca3f3fd6335c3f4ce8392d69350b4fa4e2",  # Aave V3 Pool
    "0x7fc66500c84a76ad7e9c93437bfc5ac33e2ddae9",  # AAVE token
    "0xd9e1ce17f2641f24ae83637ab66a2cca9c378b9f",  # Sushiswap Router
    "0xdef1c0ded9bec7f1a1670819833240f027b25eff",  # 0x V4 Proxy
    "0x00000000219ab540356cbb839cbe05303d7705fa",  # ETH2 Deposit
    "0xba12222222228d8ba445958a75a0704d566bf2c8",  # Balancer Vault
    "0xbebc44782c7db0a1a60cb6fe97d0b483032ff1c7",  # Curve 3pool
    "0xa2327a938febf5fec13bacfb16ae10ecbc4cbdcf",  # Curve USDC/WBTC/ETH
    "0x3416cf6c708da44db2624d63ea0aaef7113527c6",  # Uniswap V2 USDC/WETH
    "0xb4e16d0168e52d35cacd2c6185b44281ec28c9dc",  # Uniswap V2 USDC/WETH (old)
}


def is_interesting(tx: dict, receipt: dict) -> tuple[bool, str]:
    """Return (keep, reason). Filters for executions worth replaying."""
    to      = (tx.get("to") or "").lower()
    gas_used = hex_int(receipt.get("gasUsed", "0x0"))
    value   = hex_int(tx.get("value", "0x0"))
    data    = tx.get("input", "0x")

    # Skip: pure ETH transfers (no code, no data)
    if to and data in ("0x", "") and not receipt.get("logs"):
        return False, "plain_eth_transfer"

    # Skip: trivially low gas (likely simple reads or no-ops)
    if gas_used < 25_000:
        return False, "low_gas"

    # Keep: contract creation
    if not tx.get("to"):
        return True, "contract_creation"

    # Keep: known high-value protocol
    if to in KNOWN_PROTOCOLS:
        return True, f"known_protocol:{to[:10]}"

    # Keep: reverted transactions (interesting failure modes)
    if receipt.get("status") == "0x0":
        return True, "revert"

    # Keep: high gas usage (complex execution)
    if gas_used > 150_000:
        return True, f"high_gas:{gas_used}"

    # Keep: multiple logs emitted (multi-contract interaction)
    if len(receipt.get("logs", [])) >= 3:
        return True, f"multi_log:{len(receipt['logs'])}"

    # Keep: calldata suggests ABI call (not raw bytes)
    if len(data) >= 10:  # at least 4-byte selector
        return True, "abi_call"

    return False, "uninteresting"


# ─────────────────────────────────────────────────────────────────────────────
# PRESTATE CAPTURE
# ─────────────────────────────────────────────────────────────────────────────

def get_prestate_debug(debug_rpc: str, tx_hash: str) -> Optional[dict]:
    """
    Use debug_traceTransaction with prestateTracer.
    Returns the full pre-state dict keyed by address, or None if unavailable.
    Requires Geth archive node or Alchemy/QuickNode with debug namespace.
    """
    try:
        result = rpc(debug_rpc, "debug_traceTransaction", [
            tx_hash,
            {"tracer": "prestateTracer"}
        ])
        # prestateTracer returns {address: {balance, nonce, code, storage}}
        # Normalize all keys to lowercase
        return {addr.lower(): acct for addr, acct in result.items()}
    except Exception as e:
        print(f"    [debug_prestate] unavailable ({e}) — falling back to eth_*")
        return None


def get_prestate_fallback(eth_rpc: str, tx: dict, receipt: dict, at_block: str) -> dict:
    """
    Best-effort pre-state: sender + target + log emitters.
    Balance/nonce/code only — no storage (except what we can derive).
    Accurate enough for gas/status/log-count assertions.
    """
    touched = set()
    frm = (tx.get("from") or "").lower()
    to  = (tx.get("to")   or "").lower()
    if frm: touched.add(frm)
    if to:  touched.add(to)
    for log in receipt.get("logs", []):
        touched.add(log["address"].lower())

    pre = {}
    for addr in touched:
        balance = rpc(eth_rpc, "eth_getBalance",          [addr, at_block])
        nonce   = rpc(eth_rpc, "eth_getTransactionCount", [addr, at_block])
        code    = rpc(eth_rpc, "eth_getCode",             [addr, at_block])
        pre[addr] = {
            "balance": balance,
            "nonce":   nonce,
            "code":    code if code and code != "0x" else "0x",
            "storage": {}
        }
    return pre


def normalize_prestate(raw: dict) -> dict:
    """Normalize debug_traceTransaction prestate to fixture format."""
    out = {}
    for addr, acct in raw.items():
        storage = {}
        for slot, val in (acct.get("storage") or {}).items():
            # Some tracers return slots without 0x prefix; normalize to 32-byte padded hex
            k = slot if slot.startswith("0x") else "0x" + slot
            v = val  if val.startswith("0x")  else "0x" + val
            if int(v, 16) != 0:  # omit zero slots (EELS style)
                storage[k] = v
        out[addr.lower()] = {
            "balance": acct.get("balance", "0x0"),
            "nonce":   acct.get("nonce",   "0x0"),
            "code":    acct.get("code",    "0x"),
            "storage": storage,
        }
    return out


# ─────────────────────────────────────────────────────────────────────────────
# FIXTURE BUILDER
# ─────────────────────────────────────────────────────────────────────────────

def detect_fork(block_number: int) -> str:
    """Map block number to Ethereum hard fork name."""
    if block_number >= 22_000_000: return "Osaka"     # approx — update when confirmed
    if block_number >= 20_000_000: return "Prague"
    if block_number >= 19_426_587: return "Cancun"
    if block_number >= 17_034_870: return "Shanghai"
    if block_number >= 15_537_394: return "Paris"
    if block_number >= 12_965_000: return "London"
    if block_number >= 12_244_000: return "Berlin"
    if block_number >= 9_069_000:  return "Istanbul"
    return "Constantinople"


def build_fixture(tx: dict, receipt: dict, block: dict, prestate: dict,
                  prestate_method: str) -> dict:
    block_num = hex_int(block["number"])
    fork      = detect_fork(block_num)
    tx_hash   = tx["hash"]

    fixture_key = f"mainnet::{tx_hash}"
    return {
        fixture_key: {
            "_provenance": {
                "chain":          "mainnet",
                "chainId":        "0x1",
                "txHash":         tx_hash,
                "blockNumber":    block["number"],
                "blockNumberDec": block_num,
                "fork":           fork,
                "prestateMethod": prestate_method,  # "debug_prestateTracer" or "eth_fallback"
                "harvestedAt":    time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            },
            "env": {
                "currentCoinbase":    block.get("miner",         "0x0000000000000000000000000000000000000000"),
                "currentGasLimit":    block.get("gasLimit",      "0x0"),
                "currentNumber":      block.get("number",        "0x0"),
                "currentTimestamp":   block.get("timestamp",     "0x0"),
                "currentBaseFee":     block.get("baseFeePerGas", "0x0"),
                "currentDifficulty":  block.get("difficulty",    "0x0"),
                "currentRandom":      block.get("mixHash",       "0x" + "0" * 64),
                "currentExcessBlobGas": block.get("excessBlobGas", "0x0"),
            },
            "pre": prestate,
            "transaction": {
                "nonce":              tx.get("nonce",                "0x0"),
                "gasPrice":           tx.get("gasPrice",             "0x0"),
                "maxFeePerGas":       tx.get("maxFeePerGas"),
                "maxPriorityFeePerGas": tx.get("maxPriorityFeePerGas"),
                "gasLimit":           [tx.get("gas", "0x0")],
                "to":                 tx.get("to"),
                "value":              [tx.get("value", "0x0")],
                "data":               [tx.get("input", "0x")],
                "sender":             tx.get("from"),
                "type":               tx.get("type", "0x0"),
                "accessList":         tx.get("accessList", []),
            },
            # Ground truth — what Ethereum mainnet already adjudicated.
            # Schlieren is diffed against this, not against another EVM.
            "realReceipt": {
                "status":           receipt.get("status"),
                "gasUsed":          receipt.get("gasUsed"),
                "cumulativeGasUsed": receipt.get("cumulativeGasUsed"),
                "logCount":         len(receipt.get("logs", [])),
                "contractAddress":  receipt.get("contractAddress"),
            },
        }
    }


# ─────────────────────────────────────────────────────────────────────────────
# SCHLIEREN EXECUTOR
# ─────────────────────────────────────────────────────────────────────────────

REAL_TX_RUNNER = SCRIPT_DIR / "RealTxRunner" / "RealTxRunner.csproj"


def execute_with_schlieren(fixture_path: Path, fork: str) -> dict:
    """Run fixture through Schlieren via RealTxRunner. Returns result dict."""
    try:
        result = subprocess.run(
            ["dotnet", "run", "--project", str(REAL_TX_RUNNER),
             "--no-build", "--", str(fixture_path), "--fork", fork],
            capture_output=True, text=True, timeout=60,
            cwd=str(REPO_ROOT)
        )
        return {
            "exit_code": result.returncode,
            "stdout": result.stdout.strip(),
            "passed": result.returncode == 0,
        }
    except subprocess.TimeoutExpired:
        return {"exit_code": -1, "stdout": "TIMEOUT", "passed": False}
    except Exception as e:
        return {"exit_code": -1, "stdout": str(e), "passed": False}


# ─────────────────────────────────────────────────────────────────────────────
# CHECKPOINT
# ─────────────────────────────────────────────────────────────────────────────

def load_checkpoint() -> int:
    if CHECKPOINT.exists():
        data = json.loads(CHECKPOINT.read_text())
        return data.get("lastProcessedBlock", 0)
    return 0


def save_checkpoint(block_num: int):
    CHECKPOINT.parent.mkdir(parents=True, exist_ok=True)
    CHECKPOINT.write_text(json.dumps({
        "lastProcessedBlock": block_num,
        "savedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }, indent=2))


# ─────────────────────────────────────────────────────────────────────────────
# MAIN HARVEST LOOP
# ─────────────────────────────────────────────────────────────────────────────

def harvest(eth_rpc: str, debug_rpc: Optional[str], corpus_dir: Path,
            limit: int, from_block: Optional[int], execute: bool):

    corpus_dir.mkdir(parents=True, exist_ok=True)

    # ── Determine start block ──
    if from_block:
        start_block = from_block
    else:
        resumed = load_checkpoint()
        if resumed:
            start_block = resumed + 1
            print(f"Resuming from checkpoint: block {start_block}")
        else:
            # Start 10 blocks behind finalized to avoid chain tip issues
            finalized = rpc(eth_rpc, "eth_getBlockByNumber", ["finalized", False])
            start_block = hex_int(finalized["number"]) - 10
            print(f"Starting from finalized - 10: block {start_block}")

    # ── Stats ──
    harvested = 0
    scanned   = 0
    skipped   = 0
    passed    = 0
    failed    = 0
    results_log = []

    print(f"\n{'═'*60}")
    print(f"  SCHLIEREN MAINNET HARVESTER")
    print(f"  RPC:       {eth_rpc[:50]}")
    print(f"  Debug RPC: {(debug_rpc or 'none (eth_* fallback)')[:50]}")
    print(f"  Corpus:    {corpus_dir}")
    print(f"  Limit:     {limit} fixtures")
    print(f"  Execute:   {execute}")
    print(f"{'═'*60}\n")

    block_num = start_block
    while harvested < limit:
        # ── Fetch block ──
        block_hex = hex(block_num)
        try:
            block = rpc(eth_rpc, "eth_getBlockByNumber", [block_hex, True])
        except Exception as e:
            print(f"Block {block_num}: fetch failed ({e}), skipping")
            block_num += 1
            continue

        if not block:
            print(f"Block {block_num}: not found, stopping")
            break

        txs = block.get("transactions", [])
        print(f"Block {hex_int(block['number']):,}  ({len(txs)} txs)")

        for tx in txs:
            if harvested >= limit:
                break

            tx_hash = tx["hash"]
            scanned += 1

            # ── Receipt ──
            try:
                receipt = rpc(eth_rpc, "eth_getTransactionReceipt", [tx_hash])
                if not receipt:
                    continue
            except Exception:
                continue

            # ── Filter ──
            keep, reason = is_interesting(tx, receipt)
            if not keep:
                skipped += 1
                continue

            print(f"  ✓ {tx_hash[:18]}...  [{reason}]  gas={hex_int(receipt.get('gasUsed','0')):,}")

            # ── Pre-state ──
            prestate_method = "eth_fallback"
            prestate = None

            if debug_rpc:
                raw = get_prestate_debug(debug_rpc, tx_hash)
                if raw:
                    prestate = normalize_prestate(raw)
                    prestate_method = "debug_prestateTracer"

            if prestate is None:
                parent_block_hex = hex(hex_int(block["number"]) - 1)
                prestate = get_prestate_fallback(eth_rpc, tx, receipt, parent_block_hex)

            # ── Build fixture ──
            fixture = build_fixture(tx, receipt, block, prestate, prestate_method)
            fixture_key = list(fixture.keys())[0]
            fork = fixture[fixture_key]["_provenance"]["fork"]

            # ── Save ──
            safe_hash  = tx_hash[:18].replace("0x", "")
            date_stamp = time.strftime("%Y-%m-%d")
            out_path   = corpus_dir / f"{date_stamp}-{block_num}-{safe_hash}.json"
            out_path.write_text(json.dumps(fixture, indent=2))
            harvested += 1

            # ── Execute ──
            exec_result = {"passed": None}
            if execute:
                print(f"    → Schlieren replay...", end="", flush=True)
                exec_result = execute_with_schlieren(out_path, fork)
                status = "✓ PASS" if exec_result["passed"] else "✗ FAIL"
                print(f" {status}")
                if exec_result["passed"]:
                    passed += 1
                else:
                    failed += 1
                    # Print first failure line for diagnostics
                    for line in exec_result["stdout"].splitlines():
                        if "MISMATCH" in line or "Error" in line or "FAIL" in line:
                            print(f"    {line.strip()}")
                            break

            results_log.append({
                "txHash":   tx_hash,
                "block":    block_num,
                "reason":   reason,
                "fork":     fork,
                "prestate": prestate_method,
                "fixture":  str(out_path.name),
                "passed":   exec_result["passed"],
            })

        save_checkpoint(block_num)
        block_num += 1

    # ── Summary ──
    print(f"\n{'═'*60}")
    print(f"  SCHLIEREN HARVEST COMPLETE")
    print(f"  Blocks scanned:    {block_num - start_block}")
    print(f"  Transactions seen: {scanned}")
    print(f"  Skipped (filter):  {skipped}")
    print(f"  Fixtures written:  {harvested}")
    if execute:
        print(f"  Passed:            {passed}")
        print(f"  Failed:            {failed}")
    print(f"  Corpus:            {corpus_dir}")
    print(f"{'═'*60}\n")

    # Write run manifest
    manifest = corpus_dir / f"harvest-{time.strftime('%Y%m%d-%H%M%S')}.manifest.json"
    manifest.write_text(json.dumps({
        "harvested": harvested,
        "scanned":   scanned,
        "passed":    passed,
        "failed":    failed,
        "results":   results_log,
    }, indent=2))
    print(f"Manifest: {manifest.name}")


# ─────────────────────────────────────────────────────────────────────────────
# ENTRY POINT
# ─────────────────────────────────────────────────────────────────────────────

def main():
    p = argparse.ArgumentParser(
        description="Schlieren Mainnet Harvester — captures real Ethereum executions as test fixtures",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Harvest 10 fixtures from finalized chain, no execution (public RPC, no debug):
  python harvest.py --limit 10 --no-execute

  # With Alchemy (has debug namespace) — full prestate:
  python harvest.py --rpc https://eth-mainnet.g.alchemy.com/v2/YOUR_KEY \\
                    --debug-rpc https://eth-mainnet.g.alchemy.com/v2/YOUR_KEY \\
                    --limit 50

  # Start from a specific block:
  python harvest.py --from-block 21000000 --limit 5 --no-execute
        """)
    p.add_argument("--rpc",       default=PUBLIC_RPC,
                   help="Standard eth_* RPC URL")
    p.add_argument("--debug-rpc", default=None,
                   help="RPC URL with debug_traceTransaction support (Alchemy/QuickNode/local Geth)")
    p.add_argument("--limit",     type=int, default=10,
                   help="Max fixtures to harvest (default: 10)")
    p.add_argument("--from-block", type=int, default=None,
                   help="Start block number (default: resume checkpoint or finalized-10)")
    p.add_argument("--corpus-dir", default=str(DEFAULT_CORPUS),
                   help=f"Output directory (default: {DEFAULT_CORPUS})")
    p.add_argument("--no-execute", action="store_true",
                   help="Skip Schlieren replay (harvest only)")
    args = p.parse_args()

    # Build RealTxRunner first if we're going to execute
    if not args.no_execute and REAL_TX_RUNNER.exists():
        print("Building RealTxRunner...", end="", flush=True)
        r = subprocess.run(
            ["dotnet", "build", str(REAL_TX_RUNNER), "--nologo", "-c", "Debug"],
            capture_output=True, text=True, cwd=str(REPO_ROOT)
        )
        if r.returncode != 0:
            print(f"\nBuild failed — running with --no-execute\n{r.stderr[-500:]}")
            args.no_execute = True
        else:
            print(" OK")

    harvest(
        eth_rpc    = args.rpc,
        debug_rpc  = args.debug_rpc if not args.no_execute or args.debug_rpc else None,
        corpus_dir = Path(args.corpus_dir),
        limit      = args.limit,
        from_block = args.from_block,
        execute    = not args.no_execute,
    )


if __name__ == "__main__":
    sys.exit(main())
