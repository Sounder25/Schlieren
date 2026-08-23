"""
Schlieren Harvester — with Etherscan enrichment
Scans Ethereum mainnet, scores transactions, enriches with contract info.
Run: python harvester.py [--blocks N] [--reset] [--loop]
"""

import os, json, time, requests, argparse
from datetime import datetime, timezone
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

RPC_URL       = "https://ethereum.publicnode.com"
ETHERSCAN_KEY = "5X5M5H4SZUDQP34ETUGNN4RGW3JIP2HP1Y"
ETHERSCAN_URL = "https://api.etherscan.io/api"
CORPUS_DIR    = Path(r"C:\projects\Schlieren\muscle\corpus")
STATE_FILE    = CORPUS_DIR / "_state.json"

# 4-byte selector → function name cache
SELECTOR_CACHE: dict[str, str] = {}

KNOWN_PROTOCOLS = {
    "0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2": "WETH9",
    "0x7a250d5630b4cf539739df2c5dacb4c659f2488d": "UniswapV2Router",
    "0x68b3465833fb72a70ecdf485e0e4c7bd8665fc45": "UniswapV3Router2",
    "0xe592427a0aece92de3edee1f18e0157c05861564": "UniswapV3SwapRouter",
    "0x87870bca3f3fd6335c3f4ce8392d69350b4fa4e2": "AaveV3Pool",
    "0xba12222222228d8ba445958a75a0704d566bf2c8": "BalancerVault",
    "0xbebc44782c7db0a1a60cb6fe97d0b483032ff1c7": "Curve3Pool",
    "0x1111111254eeb25477b68fb85ed929f73a960582": "1inchV5Router",
    "0xdef1c0ded9bec7f1a1670819833240f027b25eff": "0xExchangeProxy",
    "0x000000000022d473030f116ddee9f6b43ac78ba3": "Permit2",
    "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48": "USDC",
    "0xdac17f958d2ee523a2206206994597c13d831ec7": "USDT",
    "0x6b175474e89094c44da98b954eedeac495271d0f": "DAI",
    "0x2260fac5e5542a773aa44fbcfedf7c193bc2c599": "WBTC",
}

MIN_GAS      = 100_000   # was 50k — cuts simple transfers and ERC-20 approvals
MIN_CALLDATA = 68        # was 36 — needs selector + at least 2 full params (64 bytes + 0x = 68 chars... wait, bytes)
MAX_PER_RUN  = 30        # was 50 — fewer but richer results

# Minimum calldata bytes (not chars) — selector(4) + 2 params(64) = 68 bytes
MIN_CALLDATA_BYTES = 68

# Selectors to SKIP — boring but high-volume (transfer, approve, transferFrom)
SKIP_SELECTORS = {
    "0xa9059cbb",  # transfer(address,uint256)
    "0x095ea7b3",  # approve(address,uint256)
    "0x23b872dd",  # transferFrom(address,address,uint256)
    "0x70a08231",  # balanceOf(address)
    "0xdd62ed3e",  # allowance(address,address)
}


# ─── RPC ──────────────────────────────────────────────────────────────────────

def rpc(method, params, timeout=20):
    r = requests.post(RPC_URL, json={"jsonrpc":"2.0","method":method,"params":params,"id":1}, timeout=timeout)
    r.raise_for_status()
    return r.json().get("result")


# ─── Etherscan helpers ────────────────────────────────────────────────────────

def etherscan(module, action, **kwargs):
    params = {"module": module, "action": action, "apikey": ETHERSCAN_KEY, **kwargs}
    try:
        r = requests.get(ETHERSCAN_URL, params=params, timeout=10)
        d = r.json()
        if d.get("status") == "1":
            return d.get("result")
    except:
        pass
    return None


def get_contract_info(address: str) -> dict:
    """Returns name, isVerified, deployer, deployedAt for a contract address."""
    info = {"contractName": None, "isVerified": False, "deployer": None, "deployedAt": None, "deployedBlock": None}

    # Source code / name
    src = etherscan("contract", "getsourcecode", address=address)
    if src and isinstance(src, list) and src[0].get("ContractName"):
        info["contractName"] = src[0]["ContractName"]
        info["isVerified"]   = bool(src[0].get("SourceCode"))

    # Creator / deploy tx
    creator = etherscan("contract", "getcontractcreation", contractaddresses=address)
    if creator and isinstance(creator, list):
        info["deployer"]      = creator[0].get("contractCreator")
        deploy_tx             = creator[0].get("txHash")
        if deploy_tx:
            # Get deploy tx timestamp
            receipt = rpc("eth_getTransactionByHash", [deploy_tx])
            if receipt and receipt.get("blockNumber"):
                block_num = int(receipt["blockNumber"], 16)
                block     = rpc("eth_getBlockByNumber", [hex(block_num), False])
                if block:
                    ts = int(block.get("timestamp", "0x0"), 16)
                    info["deployedAt"]    = datetime.fromtimestamp(ts, tz=timezone.utc).isoformat()
                    info["deployedBlock"] = block_num

    return info


def decode_selector(calldata: str) -> str:
    """Looks up 4-byte selector — tries local cache then 4byte.directory."""
    if not calldata or len(calldata) < 10:
        return ""
    selector = calldata[:10].lower()
    if selector in SELECTOR_CACHE:
        return SELECTOR_CACHE[selector]

    # Bulk fetch: try to get all sigs for this selector in one shot
    try:
        r = requests.get(
            f"https://www.4byte.directory/api/v1/signatures/?hex_signature={selector}",
            timeout=4
        )
        results = r.json().get("results", [])
        if results:
            # Pick the most-called one (lowest ID = oldest = most canonical)
            best = sorted(results, key=lambda x: x.get("id", 9999))[0]
            name = best.get("text_signature", "")
            # Shorten to just the function name without params
            short = name.split("(")[0] if "(" in name else name
            SELECTOR_CACHE[selector] = short
            return short
    except:
        pass

    SELECTOR_CACHE[selector] = ""
    return ""


# Pre-load common selectors so we don't need to call 4byte for the most common ones
SELECTOR_CACHE.update({
    "0xa9059cbb": "transfer",
    "0x095ea7b3": "approve",
    "0x23b872dd": "transferFrom",
    "0x38ed1739": "swapExactTokensForTokens",
    "0x8803dbee": "swapTokensForExactTokens",
    "0x7ff36ab5": "swapExactETHForTokens",
    "0x18cbafe5": "swapExactTokensForETH",
    "0xe8e33700": "addLiquidity",
    "0xbaa2abde": "removeLiquidity",
    "0x414bf389": "exactInputSingle",
    "0xc04b8d59": "exactInput",
    "0xdb3e2198": "exactOutputSingle",
    "0x09b81346": "exactOutput",
    "0xac9650d8": "multicall",
    "0x5ae401dc": "multicall",
    "0x1a98b2e0": "multicall",
    "0x87517c45": "supply",
    "0x69328dec": "withdraw",
    "0xe0e669eb": "borrow",
    "0x5ceae9c4": "repay",
    "0x573ade81": "liquidationCall",
    "0x69328dec": "withdraw",
    "0x2e1a7d4d": "withdraw",
    "0xd0e30db0": "deposit",
    "0xa0712d68": "mint",
    "0x42966c68": "burn",
    "0x6a627842": "mint",
    "0x0d4d1513": "swap",
    "0x022c0d9f": "swap",
    "0x128acb08": "swap",
    "0x6d9a640a": "execute",
    "0x3593564c": "execute",
    "0x1cff79cd": "execute",
    "0xb61d27f6": "execute",
    "0x9a8a0592": "execute",
    "0x47e7ef24": "depositAndInvest",
    "0x4515cef3": "deposit",
    "0xf5298aca": "withdrawAndUnwrap",
    "0x2c4e722e": "rewardRate",
    "0x70a08231": "balanceOf",
    "0xdd62ed3e": "allowance",
    "0x18160ddd": "totalSupply",
})


def enrich_candidate(c: dict) -> dict:
    """Add Etherscan + 4byte info to a scored candidate."""
    to = c.get("toAddress", "")

    # Only enrich unknown contracts (known protocols already labeled)
    if c.get("candidateType", "").startswith("KNOWN_PROTOCOL"):
        # Still decode the function
        fn = decode_selector(c.get("inputData", ""))
        c["functionName"] = fn
        c["contractName"] = KNOWN_PROTOCOLS.get(to)
        c["isVerified"]   = True
        return c

    if to and c.get("candidateType") != "CONTRACT_CREATION":
        info = get_contract_info(to)
        c.update(info)
    elif not to:
        # Contract creation — get deployer from tx
        c["contractName"] = "NEW CONTRACT"

    fn = decode_selector(c.get("inputData", ""))
    c["functionName"] = fn
    return c


# ─── Scoring ──────────────────────────────────────────────────────────────────

def detect_fork(n):
    if n >= 22_000_000: return "Osaka"
    if n >= 20_000_000: return "Prague"
    if n >= 19_426_587: return "Cancun"
    if n >= 17_034_870: return "Shanghai"
    if n >= 15_537_394: return "Paris"
    return "Berlin"


def score_tx(tx):
    to       = (tx.get("to") or "").lower()
    calldata = tx.get("input", "0x")
    value    = tx.get("value", "0x0")
    gas      = int(tx.get("gas", "0x0"), 16)
    has_val  = value not in ("0x0", "0x", "0")
    cd_bytes = (len(calldata) - 2) // 2
    selector = calldata[:10].lower() if len(calldata) >= 10 else ""

    # Skip boring selectors regardless of contract
    if selector in SKIP_SELECTORS:
        return None, 0

    # Tier 1 — contract creation
    if not tx.get("to"):
        return "CONTRACT_CREATION", 90

    # Tier 2 — known high-value protocol with real calldata
    if to in KNOWN_PROTOCOLS:
        if cd_bytes < 4:
            return None, 0  # plain ETH send to known protocol, skip
        return f"KNOWN_PROTOCOL:{KNOWN_PROTOCOLS[to]}", 100

    # Tier 3 — unknown contract, strict admission
    if cd_bytes >= MIN_CALLDATA_BYTES and gas >= MIN_GAS:
        score = 40
        if gas >= 200_000:  score += 20
        if gas >= 500_000:  score += 20
        if has_val:         score += 15
        if cd_bytes >= 500: score += 10  # complex calldata
        if gas >= 1_000_000: score += 10  # very expensive
        return "CONTRACT_CALL", score

    return None, 0


# ─── State ────────────────────────────────────────────────────────────────────

def load_state():
    if STATE_FILE.exists():
        try: return json.loads(STATE_FILE.read_text())
        except: pass
    return {"lastScannedBlock": 0}


def save_state(state):
    STATE_FILE.write_text(json.dumps(state, indent=2))


# ─── Main scan ────────────────────────────────────────────────────────────────

def scan(blocks=25, enrich=True):
    CORPUS_DIR.mkdir(parents=True, exist_ok=True)
    state = load_state()

    print("Getting latest block...")
    latest = rpc("eth_getBlockByNumber", ["latest", False])
    head   = int(latest["number"], 16) - 5

    last  = state.get("lastScannedBlock", 0)
    start = (head - 20) if last == 0 else last + 1

    if start > head:
        print(f"Already up to date at block {head:,}")
        return []

    end = min(start + blocks - 1, head)
    print(f"Scanning blocks {start:,} → {end:,}  (head={head:,})")

    candidates = []

    for block_num in range(start, end + 1):
        try:
            block = rpc("eth_getBlockByNumber", [hex(block_num), True])
        except Exception as e:
            print(f"  Block {block_num}: {e}")
            continue

        if not block or not block.get("transactions"):
            continue

        ts    = int(block.get("timestamp", "0x0"), 16)
        dt    = datetime.fromtimestamp(ts, tz=timezone.utc)
        txs   = block["transactions"]
        found = 0

        for tx in txs:
            ctype, score = score_tx(tx)
            if not ctype:
                continue

            candidates.append({
                "txHash":        tx["hash"],
                "blockNumber":   block_num,
                "blockHex":      hex(block_num),
                "blockTimestamp": ts,
                "blockDate":     dt.strftime("%Y-%m-%d %H:%M UTC"),
                "fork":          detect_fork(block_num),
                "fromAddress":   (tx.get("from") or "").lower(),
                "toAddress":     (tx.get("to") or "").lower(),
                "gasLimit":      int(tx.get("gas", "0x0"), 16),
                "value":         tx.get("value", "0x0"),
                "valueEth":      round(int(tx.get("value","0x0"), 16) / 1e18, 6),
                "inputData":     tx.get("input", "0x"),
                "candidateType": ctype,
                "priorityScore": score,
                "discoveredAt":  datetime.now(timezone.utc).isoformat(),
                # enrichment placeholders
                "contractName":  KNOWN_PROTOCOLS.get((tx.get("to") or "").lower()),
                "isVerified":    None,
                "deployer":      None,
                "deployedAt":    None,
                "deployedBlock": None,
                "functionName":  None,
            })
            found += 1

        print(f"  Block {block_num:,} ({dt.strftime('%H:%M')}): {len(txs)} txs → {found} candidates")

        if len(candidates) >= MAX_PER_RUN:
            break

    # Sort, cap
    candidates.sort(key=lambda c: -c["priorityScore"])
    candidates = candidates[:MAX_PER_RUN]

    # Enrich top candidates with Etherscan + 4byte
    if enrich and candidates:
        print(f"\nEnriching {len(candidates)} candidates...")
        enriched = []
        with ThreadPoolExecutor(max_workers=3) as ex:
            futures = {ex.submit(enrich_candidate, c): c for c in candidates}
            for i, future in enumerate(as_completed(futures), 1):
                try:
                    result = future.result()
                    enriched.append(result)
                    name = result.get("contractName") or result.get("functionName") or "?"
                    print(f"  [{i}/{len(candidates)}] {result['txHash'][:20]}… → {name}")
                except Exception as e:
                    enriched.append(futures[future])
                    print(f"  [{i}/{len(candidates)}] enrich error: {e}")
        candidates = sorted(enriched, key=lambda c: -c["priorityScore"])

    # Write index
    index = {
        "scannedAt":   datetime.now(timezone.utc).isoformat(),
        "startBlock":  start,
        "endBlock":    end,
        "totalScored": len(candidates),
        "candidates":  candidates,
    }
    index_file = CORPUS_DIR / "harvest_index.json"
    index_file.write_text(json.dumps(index, indent=2))

    state["lastScannedBlock"] = end
    state["lastScanTime"]     = datetime.now(timezone.utc).isoformat()
    save_state(state)

    print(f"\n✓ {len(candidates)} enriched candidates → {index_file}")
    return candidates


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--blocks",    type=int,  default=25)
    parser.add_argument("--no-enrich", action="store_true", help="Skip Etherscan enrichment (faster)")
    parser.add_argument("--loop",      action="store_true")
    parser.add_argument("--reset",     action="store_true")
    args = parser.parse_args()

    if args.reset and STATE_FILE.exists():
        STATE_FILE.unlink()
        print("Checkpoint reset.")

    if args.loop:
        while True:
            try:
                scan(args.blocks, enrich=not args.no_enrich)
            except Exception as e:
                print(f"Error: {e}")
            print("Sleeping 2 min...\n")
            time.sleep(120)
    else:
        scan(args.blocks, enrich=not args.no_enrich)


if __name__ == "__main__":
    main()
