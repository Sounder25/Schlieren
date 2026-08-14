#!/usr/bin/env python3
"""
Run test_pointer_contract_pointer_loop through EELS Prague and emit a JSONL gas trace.
Each line: {"op":"CALL","pc":40,"depth":1,"gas":929000}
"""
import sys, json, argparse
sys.path.insert(0, 'C:/projects/execution-specs/src')

# ── Patch evm_trace before any imports pull it ───────────────────────────────
_TRACE = []
import ethereum.forks.prague.vm.interpreter as _interp_mod
_orig_evm_trace = _interp_mod.evm_trace

def _patched_evm_trace(evm, event):
    from ethereum.forks.prague.vm.interpreter import OpStart
    if isinstance(event, OpStart):
        op_name = event.op.name if hasattr(event.op, 'name') else str(event.op)
        _TRACE.append({
            "op":    op_name,
            "pc":    int(evm.pc),
            "depth": int(evm.message.depth),
            "gas":   int(evm.gas_left),
        })
    _orig_evm_trace(evm, event)

_interp_mod.evm_trace = _patched_evm_trace

# ── Real imports ─────────────────────────────────────────────────────────────
from ethereum_types.numeric import U256, U64, Uint
from ethereum_types.bytes   import Bytes32, Bytes20
from ethereum.crypto.hash   import keccak256

from ethereum.forks.prague.fork_types import Account, Address
from ethereum.forks.prague.state_tracker import (
    BlockState, set_account, set_code, set_storage, EMPTY_CODE_HASH
)
from ethereum.forks.prague.transactions import (
    Authorization, SetCodeTransaction, AccessListTransaction,
)
from ethereum.forks.prague.vm import BlockEnvironment
from ethereum.forks.prague.fork import apply_body
from ethereum.rlp import encode as rlp_encode


# ── Helpers ───────────────────────────────────────────────────────────────────
def h2a(s: str) -> Address:
    return Address(bytes.fromhex(s.lstrip("0x").zfill(40)))

def h2u256(s) -> U256:
    if not s or s == "0x": return U256(0)
    return U256(int(s, 16))

def h2uint(s) -> Uint:
    if not s or s == "0x": return Uint(0)
    return Uint(int(s, 16))

def h2bytes(s: str) -> bytes:
    s = s.lstrip("0x")
    if not s: return b""
    if len(s) % 2: s = "0" + s
    return bytes.fromhex(s)

def pick(v):
    return v[0] if isinstance(v, list) else v


# ── Fixture loading ───────────────────────────────────────────────────────────
def load_case(path):
    with open(path) as f:
        d = json.load(f)
    key = next(k for k in d if "pointer_loop" in k)
    return d[key]


# ── State builder ─────────────────────────────────────────────────────────────
def build_state(pre: dict) -> BlockState:
    state = BlockState()
    for addr_hex, data in pre.items():
        addr    = h2a(addr_hex)
        balance = h2u256(data.get("balance", "0x0"))
        nonce   = Uint(int(data.get("nonce", "0x0"), 16))
        code    = h2bytes(data.get("code", "0x"))
        acct    = Account(nonce=nonce, balance=balance,
                          code_hash=keccak256(code) if code else EMPTY_CODE_HASH)
        set_account(state, addr, acct)
        if code:
            set_code(state, addr, code)
        for slot_hex, val_hex in data.get("storage", {}).items():
            val = U256(int(val_hex, 16))
            if val:
                set_storage(state, addr, U256(int(slot_hex, 16)), val)
    return state


# ── TX encoder ───────────────────────────────────────────────────────────────
def encode_tx(tx_data: dict, chain_id: int = 1) -> bytes:
    """Encode a type-4 SetCode transaction as raw bytes for apply_body."""
    auth_list = tx_data.get("authorizationList", [])
    auths_encoded = []
    for a in auth_list:
        cid  = int(a.get("chainId", "0x0"), 16)
        addr = h2bytes(a.get("address", "0x" + "00"*20))
        n    = int(a.get("nonce", "0x0"), 16)
        v    = int(a.get("yParity", a.get("v", "0x0")), 16)
        r    = int(a.get("r", "0x1"), 16)
        s    = int(a.get("s", "0x1"), 16)
        auths_encoded.append([cid, addr, n, v, r.to_bytes(32,'big'), s.to_bytes(32,'big')])

    gas_limit = int(pick(tx_data.get("gasLimit", "0x0")), 16)
    value     = int(pick(tx_data.get("value", "0x0")), 16)
    data      = h2bytes(pick(tx_data.get("data", "0x")))
    to_raw    = tx_data.get("to", "")
    to        = h2bytes(to_raw) if to_raw else b""
    nonce     = int(tx_data.get("nonce", "0x0"), 16)
    max_fee   = int(tx_data.get("maxFeePerGas", "0x0"), 16)
    max_pri   = int(tx_data.get("maxPriorityFeePerGas", "0x0"), 16)

    fields = [
        chain_id,
        nonce,
        max_pri,
        max_fee,
        gas_limit,
        to,
        value,
        data,
        [],           # access_list
        auths_encoded,
        0,            # y_parity (dummy — impersonated)
        1,            # r
        1,            # s
    ]
    return b'\x04' + rlp_encode(fields)


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fixture", default=(
        "C:/projects/Schlieren/fixtures/state_tests/prague/"
        "eip7702_set_code_tx/test_pointer_contract_pointer_loop.json"))
    ap.add_argument("--out", default=(
        "C:/projects/Schlieren/TestResults/eels_loop_trace.jsonl"))
    ap.add_argument("--chain-id", type=int, default=1)
    args = ap.parse_args()

    case  = load_case(args.fixture)
    state = build_state(case["pre"])
    env_d = case.get("env", {})

    block_env = BlockEnvironment(
        chain_id         = U64(args.chain_id),
        state            = state,
        block_gas_limit  = h2uint(env_d.get("currentGasLimit", "0x07270e00")),
        block_hashes     = [],
        coinbase         = h2a(env_d.get("currentCoinbase", "0x" + "00"*20)),
        number           = h2uint(env_d.get("currentNumber", "0x01")),
        base_fee_per_gas = h2uint(env_d.get("currentBaseFee", "0x07")),
        time             = h2u256(env_d.get("currentTimestamp", "0x03e8")),
        prev_randao      = Bytes32(h2bytes(env_d.get("currentRandom", "0x" + "00"*32)).rjust(32, b'\x00')),
        excess_blob_gas  = U64(int(env_d.get("currentExcessBlobGas", "0x00"), 16)),
        parent_beacon_block_root = Bytes32(b'\x00'*32),
    )

    tx_bytes = encode_tx(case["transaction"], chain_id=args.chain_id)

    _TRACE.clear()
    try:
        out = apply_body(
            block_env    = block_env,
            transactions = (tx_bytes,),
            withdrawals  = (),
        )
        print(f"[EELS] apply_body OK — block_gas_used={out.block_gas_used}", file=sys.stderr)
    except Exception as e:
        import traceback
        print(f"[EELS] exception: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)

    with open(args.out, "w") as f:
        for step in _TRACE:
            f.write(json.dumps(step) + "\n")
    print(f"[EELS] wrote {len(_TRACE)} trace steps → {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
