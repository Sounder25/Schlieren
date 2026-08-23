#!/usr/bin/env python3
"""
Real-transaction ingestion tool for Schlieren.

Pulls an already-settled Ethereum mainnet transaction and reconstructs everything
needed to replay it: sender/target, calldata, value, gas, block context, runtime
bytecode, and (best-effort) the touched prestate. The transaction's real receipt
(status, gasUsed) is the ground truth to diff Schlieren's replay against — this is
stronger evidence than comparing against another candidate EVM implementation on a
synthetic case, because it's what Ethereum itself already adjudicated.

No debug_traceTransaction / prestateTracer dependency: free public RPC endpoints
don't expose the debug namespace. Prestate is reconstructed from plain eth_* calls
(eth_getBalance, eth_getTransactionCount, eth_getCode, eth_getStorageAt) at the
tx's parent block. Storage reconstruction is necessarily incomplete for arbitrary
contracts without a trace — pass --storage-slots to supply known slots (e.g. via
solc's storage layout output, or hand-derived mapping slots) for anything beyond
the sender/target's basic balance/nonce/code.

Usage:
    python import_real_tx.py <tx_hash> [--rpc URL] [--out FILE]
        [--storage-slots ADDR:SLOT_HEX,SLOT_HEX,... [ADDR:SLOT_HEX,...] ...]

Output: a JSON fixture in EELS-state-test-like shape (env / pre / transaction /
post-from-real-receipt) written to --out (default: muscle/real_tx/<hash>.json).
"""
import argparse
import json
import sys
import urllib.request
from pathlib import Path

DEFAULT_RPC = "https://ethereum.publicnode.com"


def rpc_call(rpc_url, method, params):
    body = json.dumps({"jsonrpc": "2.0", "method": method, "params": params, "id": 1}).encode()
    # Some public RPC endpoints 403 the default Python urllib User-Agent string.
    headers = {"Content-Type": "application/json", "User-Agent": "curl/8.0"}
    req = urllib.request.Request(rpc_url, data=body, headers=headers)
    with urllib.request.urlopen(req, timeout=20) as resp:
        payload = json.loads(resp.read())
    if "error" in payload:
        raise RuntimeError(f"{method} failed: {payload['error']}")
    return payload["result"]


def hex_to_int(h):
    return int(h, 16) if h else 0


def fetch_transaction(rpc_url, tx_hash):
    tx = rpc_call(rpc_url, "eth_getTransactionByHash", [tx_hash])
    if tx is None:
        raise RuntimeError(f"transaction {tx_hash} not found (node may be non-archive / tx pruned)")
    receipt = rpc_call(rpc_url, "eth_getTransactionReceipt", [tx_hash])
    return tx, receipt


def fetch_block_context(rpc_url, block_number_hex):
    block = rpc_call(rpc_url, "eth_getBlockByNumber", [block_number_hex, False])
    return {
        "currentCoinbase": block["miner"],
        "currentGasLimit": block["gasLimit"],
        "currentNumber": block["number"],
        "currentTimestamp": block["timestamp"],
        "currentBaseFee": block.get("baseFeePerGas", "0x0"),
        "currentDifficulty": block.get("difficulty", "0x0"),
        "currentRandom": block.get("mixHash"),
        "currentExcessBlobGas": block.get("excessBlobGas", "0x0"),
    }


def parent_block_hex(block_number_hex):
    return hex(hex_to_int(block_number_hex) - 1)


def fetch_account_state(rpc_url, address, at_block_hex, extra_slots):
    balance = rpc_call(rpc_url, "eth_getBalance", [address, at_block_hex])
    nonce = rpc_call(rpc_url, "eth_getTransactionCount", [address, at_block_hex])
    code = rpc_call(rpc_url, "eth_getCode", [address, at_block_hex])
    storage = {}
    for slot in extra_slots:
        value = rpc_call(rpc_url, "eth_getStorageAt", [address, slot, at_block_hex])
        # Skip all-zero slots — EELS-style pre-state omits untouched/empty slots.
        if int(value, 16) != 0:
            storage[slot] = value
    return {
        "nonce": nonce,
        "balance": balance,
        "code": code if code and code != "0x" else "0x",
        "storage": storage,
    }


def build_fixture(rpc_url, tx_hash, storage_slots_by_address):
    tx, receipt = fetch_transaction(rpc_url, tx_hash)
    block_number_hex = tx["blockNumber"]
    if block_number_hex is None:
        raise RuntimeError("transaction is still pending (no blockNumber) — cannot replay")

    env = fetch_block_context(rpc_url, block_number_hex)
    at_parent = parent_block_hex(block_number_hex)

    touched = {tx["from"]}
    if tx.get("to"):
        touched.add(tx["to"])
    if receipt:
        for log in receipt.get("logs", []):
            touched.add(log["address"])

    pre_state = {}
    for addr in touched:
        slots = storage_slots_by_address.get(addr.lower(), [])
        pre_state[addr] = fetch_account_state(rpc_url, addr, at_parent, slots)

    fixture = {
        f"realtx::{tx_hash}": {
            "_source": {
                "network": "mainnet",
                "txHash": tx_hash,
                "blockNumber": block_number_hex,
                "note": (
                    "Prestate is best-effort (sender/target/log-emitters' balance+nonce+code, "
                    "plus any --storage-slots supplied). No debug_traceTransaction available on "
                    "free public RPCs, so storage for addresses/slots not explicitly listed is "
                    "reconstructed as empty (0) even if the real account has other nonzero slots. "
                    "Treat state-mismatch results with that caveat — gas/success/logs vs the real "
                    "receipt below are the reliable ground truth, full state-root equivalence is not."
                ),
            },
            "env": env,
            "pre": pre_state,
            "transaction": {
                "nonce": tx["nonce"],
                "gasPrice": tx.get("gasPrice", "0x0"),
                "maxFeePerGas": tx.get("maxFeePerGas"),
                "maxPriorityFeePerGas": tx.get("maxPriorityFeePerGas"),
                "gasLimit": [tx["gas"]],
                "to": tx.get("to"),
                "value": [tx["value"]],
                "data": [tx["input"]],
                "sender": tx["from"],
                "type": tx.get("type", "0x0"),
            },
            # Ground truth: what Ethereum mainnet actually decided. Not a full EELS "post"
            # block (we don't have prestateTracer's post-state diff) — status/gasUsed/logCount
            # are what a replay should be diffed against.
            "realReceipt": {
                "status": receipt.get("status") if receipt else None,
                "gasUsed": receipt.get("gasUsed") if receipt else None,
                "cumulativeGasUsed": receipt.get("cumulativeGasUsed") if receipt else None,
                "logCount": len(receipt.get("logs", [])) if receipt else None,
                "logsBloom": receipt.get("logsBloom") if receipt else None,
                "contractAddress": receipt.get("contractAddress") if receipt else None,
            },
        }
    }
    return fixture


def parse_storage_slots_arg(raw_list):
    result = {}
    for entry in raw_list or []:
        addr, _, slot_list = entry.partition(":")
        result[addr.lower()] = [s for s in slot_list.split(",") if s]
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("tx_hash")
    p.add_argument("--rpc", default=DEFAULT_RPC)
    p.add_argument("--out", default=None)
    p.add_argument("--storage-slots", nargs="*", default=[],
                    help="ADDR:0xslot1,0xslot2 — known storage slots to fetch for an address")
    args = p.parse_args()

    slots = parse_storage_slots_arg(args.storage_slots)
    fixture = build_fixture(args.rpc, args.tx_hash, slots)

    out_path = Path(args.out) if args.out else Path(__file__).parent.parent / "muscle" / "real_tx" / f"{args.tx_hash}.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(fixture, indent=2))
    print(f"wrote {out_path}")

    root = next(iter(fixture.values()))
    print(f"  block:  {root['env']['currentNumber']}")
    print(f"  to:     {root['transaction']['to']}")
    print(f"  status: {root['realReceipt']['status']}  gasUsed: {root['realReceipt']['gasUsed']}  logs: {root['realReceipt']['logCount']}")


if __name__ == "__main__":
    sys.exit(main())
