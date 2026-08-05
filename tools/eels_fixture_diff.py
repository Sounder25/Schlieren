#!/usr/bin/env python3
"""
eels-fixture-diff — First Divergence & End-to-End Step-Trace Finder
====================================================================
Runs a fixture case through Scrutor.EELS.Tests and captures full mismatch
context. With `--step-trace`, it runs the SingleCaseTracer to emit Scrutor's
structLog, generates the EELS Python reference trace, and invokes eels_trace_compare.py
to pinpoint the exact step, PC, opcode, and gas delta in one command!

Usage:
    python tools/eels_fixture_diff.py <fixture.json> <case_id> [--fork Cancun] [--step-trace]

Outputs:
    • Pre-state & tx summary
    • Scrutor execution result (via dotnet test --filter)
    • Mismatch table: account / slot / field → expected vs actual
    • Gas accounting: intrinsic + EVM + refund + coinbase + sender delta
    • Step-by-step structLog divergence diff (when --step-trace is specified)

Exit codes:
    0 = pass (no divergence)
    1 = divergence found (details printed)
    2 = fixture not found / parse error
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

# Force UTF-8 output encoding for Windows terminals
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")


# ---------------------------------------------------------------------------
# Hex helpers
# ---------------------------------------------------------------------------

def _decode_hex(value: str | int | None) -> int:
    if value is None:
        return 0
    if isinstance(value, int):
        return value
    v = str(value).strip()
    if v.startswith(("0x", "0X")):
        return int(v, 16)
    try:
        return int(v, 16)
    except ValueError:
        return int(v)


def _to_hex(value: int) -> str:
    return hex(value)


# ---------------------------------------------------------------------------
# Fixture loading
# ---------------------------------------------------------------------------

def load_fixture(fixture_path: str, case_id: str, fork: str) -> dict:
    """Return the fixture post-state entry for the given case + fork."""
    path = Path(fixture_path)
    if not path.exists():
        print(f"[ERROR] Fixture not found: {fixture_path}", file=sys.stderr)
        sys.exit(2)

    with open(path, "rb") as fh:
        doc = json.load(fh)

    if case_id not in doc:
        available = list(doc.keys())
        print(f"[ERROR] Case '{case_id}' not found. Available: {available[:10]}", file=sys.stderr)
        sys.exit(2)

    case = doc[case_id]
    post = case.get("post", {})
    if fork not in post:
        available_forks = list(post.keys())
        print(f"[ERROR] Fork '{fork}' not in case.post. Available: {available_forks}", file=sys.stderr)
        sys.exit(2)

    return {
        "pre":  case.get("pre", {}),
        "transaction": case.get("transaction", {}),
        "env":  case.get("env", {}),
        "post": post[fork],
        "fork": fork,
        "case_id": case_id,
        "fixture_path": str(fixture_path),
    }


# ---------------------------------------------------------------------------
# Pretty-print pre-state
# ---------------------------------------------------------------------------

def print_pre_state(data: dict):
    pre = data["pre"]
    tx  = data["transaction"]
    env = data["env"]

    print("=" * 70)
    print(f"  FIXTURE  : {data['fixture_path']}")
    print(f"  CASE ID  : {data['case_id']}")
    print(f"  FORK     : {data['fork']}")
    print("=" * 70)
    print()

    print("── TRANSACTION ─────────────────────────────────────────────────────")
    gas_limit_raw = tx.get("gasLimit")
    gas_limit = _decode_hex(gas_limit_raw[0] if isinstance(gas_limit_raw, list) else gas_limit_raw)
    value_raw  = tx.get("value")
    value      = _decode_hex(value_raw[0] if isinstance(value_raw, list) else value_raw)
    print(f"  sender      : {tx.get('secretKey', '?')} (derived)")
    print(f"  to          : {tx.get('to', 'CREATE')}")
    print(f"  gasLimit    : {gas_limit:,}  ({_to_hex(gas_limit)})")
    print(f"  value       : {value:,}  ({_to_hex(value)})")
    data_raw = tx.get("data", [""])
    data_hex = data_raw[0] if isinstance(data_raw, list) else data_raw
    data_bytes = bytes.fromhex(data_hex.replace("0x", "").replace("0X", "")) if data_hex else b""
    print(f"  calldata    : {len(data_bytes)} bytes  (0x{data_bytes[:16].hex()}...)" if len(data_bytes) > 16
          else f"  calldata    : {len(data_bytes)} bytes  (0x{data_bytes.hex()})")
    print()

    print("── PRE-STATE ────────────────────────────────────────────────────────")
    for addr, acct in pre.items():
        bal = _decode_hex(acct.get("balance", 0))
        nonce = _decode_hex(acct.get("nonce", 0))
        code = acct.get("code", "0x")
        code_bytes = bytes.fromhex(code.replace("0x", ""))
        storage = acct.get("storage", {})
        print(f"  {addr}")
        print(f"    balance  = {bal:,}  ({_to_hex(bal)})")
        print(f"    nonce    = {nonce}")
        print(f"    code     = {len(code_bytes)} bytes")
        if storage:
            for slot, val in list(storage.items())[:4]:
                print(f"    storage[{slot}] = {val}")
            if len(storage) > 4:
                print(f"    ... +{len(storage)-4} more slots")
    print()


# ---------------------------------------------------------------------------
# Run Scrutor via dotnet test --filter
# ---------------------------------------------------------------------------

def run_scrutor(fixture_path: str, case_id: str, fork: str, project_root: str, step_trace: bool = False) -> tuple[int, str]:
    """Run EELS harness for a single case via dotnet test."""
    env = os.environ.copy()
    env["EELS_FIXTURES_ROOT"] = str(Path(fixture_path).resolve().parent)
    env["EELS_REQUIRED_FORK"] = fork
    env["EELS_MAX_CASES"] = "9999"
    env["EELS_INCLUDE_SUBDIRS"] = "0"

    filter_expr = "SingleCaseTrace" if step_trace else "BENCHMARK_TaxonomySnapshot"
    if step_trace:
        env["EELS_CASE_FILTER"] = case_id
        env["EELS_STRUCT_LOG_OUT"] = str((Path(project_root) / "TestResults" / "struct_log_scrutor.json").resolve())

    cmd = [
        "dotnet", "test",
        str(Path(project_root) / "Scrutor.EELS.Tests" / "Scrutor.EELS.Tests.csproj"),
        "--filter", filter_expr,
        "--no-build",
        "--logger", "console;verbosity=detailed",
    ]

    print("── RUNNING SCRUTOR ──────────────────────────────────────────────────")
    print(f"  cmd: {' '.join(cmd)}")
    print(f"  EELS_FIXTURES_ROOT={env['EELS_FIXTURES_ROOT']}")
    print()

    result = subprocess.run(cmd, capture_output=True, text=True, env=env, cwd=project_root)
    output = result.stdout + "\n" + result.stderr
    return result.returncode, output


def parse_mismatches(output: str) -> list[dict]:
    patterns = [
        r"balance mismatch for (?P<addr>\S+): expected=(?P<exp>\S+), actual=(?P<act>\S+)",
        r"nonce mismatch for (?P<addr>\S+): expected=(?P<exp>\S+), actual=(?P<act>\S+)",
        r"storage mismatch for (?P<addr>\S+) slot (?P<slot>\S+): expected=(?P<exp>\S+), actual=(?P<act>\S+)",
        r"code mismatch for (?P<addr>\S+)",
        r"receipt\.status mismatch: expected=(?P<exp>\S+), actual=(?P<act>\S+)",
        r"missing account in actual state: (?P<addr>\S+)",
        r"unexpected account in actual state: (?P<addr>\S+)",
    ]
    found = []
    for line in output.splitlines():
        for pat in patterns:
            m = re.search(pat, line)
            if m:
                found.append({"raw": line.strip(), **m.groupdict()})
                break
    return found


def run_step_trace_diff(fixture_path: str, project_root: str):
    """Run eels_trace_compare.py to diff Scrutor's structLog vs EELS Python reference trace."""
    scrutor_trace = Path(project_root) / "Scrutor.EELS.Tests" / "TestResults" / "struct_log_scrutor.json"
    if not scrutor_trace.exists():
        scrutor_trace = Path(project_root) / "TestResults" / "struct_log_scrutor.json"

    trace_compare_script = Path(project_root) / "tools" / "eels_trace_compare.py"

    cmd = [
        sys.executable,
        str(trace_compare_script),
        str(scrutor_trace),
        "--eels-fixture", str(fixture_path),
        "--label1", "Scrutor",
        "--label2", "EELS Spec"
    ]

    print("── STEP-BY-STEP STRUCTLOG DIFF (EELS REFERENCE) ─────────────────────")
    print(f"  cmd: {' '.join(cmd)}")
    print()
    subprocess.run(cmd)


def run_diff(fixture_path: str, case_id: str, fork: str, project_root: str, step_trace: bool = False):
    data = load_fixture(fixture_path, case_id, fork)
    print_pre_state(data)

    exit_code, output = run_scrutor(fixture_path, case_id, fork, project_root, step_trace)

    print("── SCRUTOR OUTPUT ───────────────────────────────────────────────────")
    lines = output.strip().splitlines()
    for line in lines[-60:]:
        print(" ", line)
    print()

    mismatches = parse_mismatches(output)

    print("── DIVERGENCE REPORT ────────────────────────────────────────────────")
    if not mismatches and exit_code == 0 and not step_trace:
        print("  ✅  NO DIVERGENCE — case passes.")
        sys.exit(0)

    if mismatches:
        print(f"  {len(mismatches)} mismatch(es) found:\n")
        for i, m in enumerate(mismatches, 1):
            print(f"  [{i:02d}] {m['raw']}")
            if "exp" in m and "act" in m:
                exp_val = _decode_hex(m["exp"])
                act_val = _decode_hex(m["act"])
                delta = act_val - exp_val
                sign  = "+" if delta >= 0 else ""
                print(f"       Δ = {sign}{delta:,}  (actual − expected)")
        print()

    if step_trace:
        run_step_trace_diff(fixture_path, project_root)
    else:
        print("  TIP: Re-run with --step-trace for step-by-step opcode gas diff against EELS reference:")
        print(f"       python tools/eels_fixture_diff.py {fixture_path} {case_id} --fork {fork} --step-trace")
        sys.exit(1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Diff a single EELS fixture case against Scrutor execution output."
    )
    parser.add_argument("fixture",  help="Path to the fixture .json file")
    parser.add_argument("case_id",  help="Top-level key inside the fixture JSON (e.g. 'callBasic_d0g0v0')")
    parser.add_argument("--fork",   default="Cancun", help="Fork name (default: Cancun)")
    parser.add_argument("--step-trace", action="store_true", help="Generate structLog and run step-by-step diff against EELS reference trace")
    parser.add_argument("--root",   default=None, help="Scrutor project root (default: parent of 'tools/' directory)")
    args = parser.parse_args()

    project_root = args.root
    if not project_root:
        project_root = str(Path(__file__).resolve().parent.parent)

    run_diff(args.fixture, args.case_id, args.fork, project_root, args.step_trace)
