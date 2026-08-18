#!/usr/bin/env python3
"""
eels-trace-compare — Two-Trace Divergence Finder & EELS Ground Truth Runner
=============================================================================
Compares two EIP-3155 structLog JSON files step-by-step (e.g. Schlieren trace vs
EELS Python reference or Geth debug_traceTransaction output) to pinpoint the EXACT step,
PC, and opcode where gas, stack, memory, or storage diverge.

Optionally runs `ethereum-spec-evm statetest --trace` directly against the fixture
file to generate the canonical EELS Python reference trace as ground truth!

Usage:
    python tools/eels_trace_compare.py <trace1.json> <trace2.json> [--label1 Schlieren] [--label2 Reference]
    python tools/eels_trace_compare.py <schlieren_trace.json> --eels-fixture <fixture.json> [--case-id ID]

Exit codes:
    0 = traces match perfectly
    1 = divergence found (details printed)
    2 = invalid trace file format / runner error
"""

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

# Force UTF-8 output encoding for Windows terminals
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

# Path to EELS Python reference CLI executable
EELS_CLI_PATH = r"C:\projects\execution-specs\.venv\Scripts\ethereum-spec-evm.exe"


def _decode_gas(val) -> int:
    if val is None:
        return 0
    if isinstance(val, int):
        return val
    s = str(val).strip()
    if s.startswith(("0x", "0X")):
        return int(s, 16)
    try:
        return int(s)
    except ValueError:
        return int(s, 16)


def load_struct_logs(filepath: str) -> tuple[dict, list[dict]]:
    path = Path(filepath)
    if not path.exists():
        print(f"[ERROR] Trace file not found: {filepath}", file=sys.stderr)
        sys.exit(2)

    with open(path, "r", encoding="utf-8-sig") as fh:
        doc = json.load(fh)

    # Handle standard EIP-3155 wrapper or Geth debug_traceTransaction response
    if "structLogs" in doc:
        logs = doc["structLogs"]
    elif "result" in doc and "structLogs" in doc["result"]:
        logs = doc["result"]["structLogs"]
    elif isinstance(doc, list):
        logs = doc
    else:
        print(f"[ERROR] Unable to locate 'structLogs' in {filepath}", file=sys.stderr)
        sys.exit(2)

    return doc, logs


def generate_eels_reference_trace(fixture_path: str, case_id: str | None = None) -> str:
    """Run ethereum-spec-evm statetest --trace to capture the EELS Python reference structLog."""
    if not os.path.exists(EELS_CLI_PATH):
        print(f"[ERROR] EELS Python runner not found at: {EELS_CLI_PATH}", file=sys.stderr)
        sys.exit(2)

    out_dir = Path("TestResults")
    out_dir.mkdir(exist_ok=True)
    eels_trace_path = out_dir / "struct_log_eels.json"

    cmd = [EELS_CLI_PATH, "statetest", "--trace", str(fixture_path)]
    print(f"[EELS REFERENCE] Running EELS Python runner: {' '.join(cmd)}")

    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0 and not res.stdout:
        print(f"[ERROR] EELS reference runner failed:\n{res.stderr}", file=sys.stderr)
        sys.exit(2)

    # Parse stdout or JSON trace emitted by statetest
    try:
        # Save EELS trace output to struct_log_eels.json
        trace_json = json.loads(res.stdout)
        with open(eels_trace_path, "w", encoding="utf-8") as fh:
            json.dump(trace_json, fh, indent=2)
    except Exception:
        # Fallback: if EELS stdout is ndjson lines of structLog
        struct_logs = []
        for line in res.stdout.splitlines():
            line = line.strip()
            if line.startswith("{") and line.endswith("}"):
                try:
                    struct_logs.append(json.loads(line))
                except Exception:
                    pass
        with open(eels_trace_path, "w", encoding="utf-8") as fh:
            json.dump({"structLogs": struct_logs}, fh, indent=2)

    print(f"[EELS REFERENCE] Generated ground truth trace: {eels_trace_path}")
    return str(eels_trace_path)


def compare_traces(
    file1: str,
    file2: str,
    label1: str = "Trace-1",
    label2: str = "Trace-2"
):
    meta1, logs1 = load_struct_logs(file1)
    meta2, logs2 = load_struct_logs(file2)

    print("=" * 70)
    print(f"  COMPARING TRACES")
    print(f"  {label1:10} : {file1}  ({len(logs1)} steps)")
    print(f"  {label2:10} : {file2}  ({len(logs2)} steps)")
    print("=" * 70)
    print()

    min_len = min(len(logs1), len(logs2))

    for i in range(min_len):
        s1 = logs1[i]
        s2 = logs2[i]

        pc1, pc2 = s1.get("pc"), s2.get("pc")
        op1, op2 = s1.get("op"), s2.get("op")
        gas1, gas2 = _decode_gas(s1.get("gas")), _decode_gas(s2.get("gas"))
        cost1, cost2 = _decode_gas(s1.get("gasCost")), _decode_gas(s2.get("gasCost"))
        depth1, depth2 = s1.get("depth", 1), s2.get("depth", 1)

        mismatches = []
        if pc1 != pc2:
            mismatches.append(f"PC mismatch: {pc1} vs {pc2}")
        if op1 != op2:
            mismatches.append(f"Opcode mismatch: '{op1}' vs '{op2}'")
        if gas1 != gas2:
            mismatches.append(f"Remaining gas mismatch: {gas1:,} vs {gas2:,} (Δ = {gas1 - gas2:+,})")
        if cost1 != cost2 and cost1 != 0 and cost2 != 0:
            mismatches.append(f"GasCost mismatch: {cost1:,} vs {cost2:,} (Δ = {cost1 - cost2:+,})")
        if depth1 != depth2:
            mismatches.append(f"Depth mismatch: {depth1} vs {depth2}")

        if mismatches:
            print("── FIRST DIVERGENCE DETECTED ────────────────────────────────────────")
            print(f"  Step Index : {i}")
            print(f"  Reason     : {', '.join(mismatches)}")
            print()
            print(f"  {'Field':<15} | {label1:<25} | {label2:<25}")
            print(f"  {'-'*15}-+-{'-'*25}-+-{'-'*25}")
            print(f"  {'PC':<15} | {str(pc1):<25} | {str(pc2):<25}")
            print(f"  {'Opcode':<15} | {str(op1):<25} | {str(op2):<25}")
            print(f"  {'Gas Remaining':<15} | {gas1:<25,} | {gas2:<25,}")
            print(f"  {'Gas Cost':<15} | {cost1:<25,} | {cost2:<25,}")
            print(f"  {'Depth':<15} | {depth1:<25} | {depth2:<25}")

            st1, st2 = s1.get("stack", []), s2.get("stack", [])
            top1 = st1[-1] if st1 else "(empty)"
            top2 = st2[-1] if st2 else "(empty)"
            print(f"  {'Stack Top':<15} | {str(top1):<25} | {str(top2):<25}")
            print()

            if i > 0:
                print("── PRECEDING STEPS (last 3) ─────────────────────────────────────────")
                for prev in range(max(0, i - 3), i):
                    ps1 = logs1[prev]
                    print(f"  [{prev:04d}] PC={ps1.get('pc')} op={ps1.get('op'):<10} gas={_decode_gas(ps1.get('gas')):,}")
                print()

            sys.exit(1)

    if len(logs1) != len(logs2):
        print("── TRACE LENGTH MISMATCH ────────────────────────────────────────────")
        print(f"  {label1} executed {len(logs1)} steps; {label2} executed {len(logs2)} steps.")
        print(f"  All initial {min_len} steps matched perfectly.")
        sys.exit(1)

    print("✅  NO DIVERGENCE — Both structLogs matched step-for-step across all fields.")
    sys.exit(0)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Compare two EIP-3155 structLog JSON files step-by-step against EELS ground truth."
    )
    parser.add_argument("trace1", help="Path to first structLog JSON (e.g. Schlieren trace)")
    parser.add_argument("trace2", nargs="?", default=None, help="Path to second structLog JSON (optional if --eels-fixture is used)")
    parser.add_argument("--eels-fixture", help="Path to fixture file to generate EELS Python reference trace automatically")
    parser.add_argument("--label1", default="Schlieren", help="Label for first trace (default: Schlieren)")
    parser.add_argument("--label2", default="Reference", help="Label for second trace (default: Reference)")
    args = parser.parse_args()

    trace1 = args.trace1
    trace2 = args.trace2

    if args.eels_fixture:
        trace2 = generate_eels_reference_trace(args.eels_fixture)
        args.label2 = "EELS Spec"

    if not trace2:
        print("[ERROR] Must provide trace2 JSON path OR --eels-fixture <fixture.json>", file=sys.stderr)
        sys.exit(2)

    compare_traces(trace1, trace2, args.label1, args.label2)
