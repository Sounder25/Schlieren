#!/usr/bin/env python3
"""
Schlieren Guard — One-Shot Runner

Drop a token address in, get Guard's verdict back.
Handles all the AWS SSM plumbing invisibly.

Usage from Hermes:
    python tools/guard-run.py 0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48

Usage from terminal:
    cd C:\projects\Schlieren
    python tools/guard-run.py <TOKEN_ADDRESS> [--block BLOCK_NUM] [--publish] [--health]

Flags:
    --block N       Pin to specific block number (default: latest)
    --publish       Republish Guard CLI from latest code before running
    --health        Just check node health, don't run Guard
    --raw           Print raw evidence JSON instead of summary
    --timeout N     SSM poll timeout in seconds (default: 180)
"""

import subprocess
import json
import sys
import time
import re
import os

# ── Config ────────────────────────────────────────────────────────────────────

AWS_CLI = r"C:\Users\Erick\AppData\Local\Programs\Amazon\AWSCLIV2\aws.exe"
INSTANCE_ID = "i-000aa2178570bbfab"
REGION = "us-east-1"
GUARD_CLI = "/opt/schlieren-guard/Schlieren.CLI"
GUARD_OUT = "/opt/guard-out"
GUARD_REPO = "/opt/schlieren"
GUARD_BRANCH = "feature/schlieren-guard"

# ── Helpers ───────────────────────────────────────────────────────────────────

def ssm_send(commands: list[str], timeout_sec: int = 600) -> str:
    """Send SSM command and return CommandId."""
    params = json.dumps({"commands": commands})
    result = subprocess.run([
        AWS_CLI, "ssm", "send-command",
        "--instance-ids", INSTANCE_ID,
        "--document-name", "AWS-RunShellScript",
        "--parameters", params,
        "--timeout-seconds", str(timeout_sec),
        "--region", REGION,
        "--query", "Command.CommandId",
        "--output", "text"
    ], capture_output=True, text=True, timeout=30)
    if result.returncode != 0:
        print(f"ERROR sending SSM command: {result.stderr}", file=sys.stderr)
        sys.exit(1)
    return result.stdout.strip()


def ssm_poll(cmd_id: str, timeout: int = 180, poll_interval: int = 5) -> dict:
    """Poll SSM command until complete or timeout. Returns {status, stdout, stderr}."""
    deadline = time.time() + timeout
    attempt = 0
    while time.time() < deadline:
        attempt += 1
        wait = min(poll_interval, max(2, poll_interval - 1 if attempt == 1 else poll_interval))
        if attempt == 1:
            time.sleep(3)  # first poll: short wait
        else:
            time.sleep(wait)

        result = subprocess.run([
            AWS_CLI, "ssm", "get-command-invocation",
            "--command-id", cmd_id,
            "--instance-id", INSTANCE_ID,
            "--region", REGION,
        ], capture_output=True, text=True, timeout=20)

        if result.returncode != 0:
            # Invocation might not be registered yet
            if "InvocationDoesNotExist" in result.stderr:
                continue
            print(f"Poll error: {result.stderr}", file=sys.stderr)
            continue

        data = json.loads(result.stdout)
        status = data.get("Status", "")

        if status in ("Success", "Failed", "TimedOut", "Cancelled"):
            return {
                "status": status,
                "stdout": data.get("StandardOutputContent", ""),
                "stderr": data.get("StandardErrorContent", ""),
            }

        # Still running — show progress dot
        print(".", end="", flush=True)

    return {"status": "LocalTimeout", "stdout": "", "stderr": f"Timed out after {timeout}s"}


def validate_token(addr: str) -> str:
    """Validate and normalize an Ethereum address."""
    addr = addr.strip()
    if not re.match(r'^0x[0-9a-fA-F]{40}$', addr):
        print(f"ERROR: Invalid Ethereum address: {addr}", file=sys.stderr)
        sys.exit(1)
    return addr


# ── Commands ──────────────────────────────────────────────────────────────────

def cmd_health():
    """Check node health."""
    print("Checking AWS Ethereum node health...")
    cmd_id = ssm_send([
        "systemctl is-active reth lighthouse || true",
        "echo ---BLOCK---",
        "curl -s -m 8 -X POST http://127.0.0.1:8545 -H 'content-type: application/json' "
        "-d '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_blockNumber\",\"params\":[]}' || echo RPC_FAIL",
        "echo",
        "echo ---SYNCING---",
        "curl -s -m 8 -X POST http://127.0.0.1:8545 -H 'content-type: application/json' "
        "-d '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_syncing\",\"params\":[]}' || echo RPC_FAIL",
        "echo",
        "echo ---LIGHTHOUSE---",
        "curl -s -m 8 http://127.0.0.1:5052/eth/v1/node/syncing || echo LH_FAIL",
        "echo",
        "echo ---DISK---",
        "df -h /",
    ])
    print(f"  Command: {cmd_id}")
    result = ssm_poll(cmd_id, timeout=30)
    print()  # newline after progress dots

    if result["status"] == "Success":
        out = result["stdout"]
        # Parse block number
        block_match = re.search(r'"result"\s*:\s*"(0x[0-9a-fA-F]+)"', out)
        if block_match:
            block_hex = block_match.group(1)
            block_dec = int(block_hex, 16)
            print(f"  ✓ Reth RPC alive — block {block_dec:,} ({block_hex})")
        elif "RPC_FAIL" in out:
            print("  ✗ Reth RPC not responding")
        
        # Sync status
        if '"result":false' in out or '"result": false' in out:
            print("  ✓ Not syncing (fully synced)")
        elif "currentBlock" in out:
            print("  ⚠ Still syncing...")
        
        # Services
        lines = out.strip().split("\n")
        if lines and ("active" in lines[0] or "inactive" in lines[0]):
            print(f"  Services: {lines[0].strip()}")
        
        # Disk
        disk_match = re.search(r'(\d+%)\s+/', out)
        if disk_match:
            print(f"  Disk usage: {disk_match.group(1)}")
    else:
        print(f"  ✗ Health check failed: {result['status']}")
        if result["stderr"]:
            print(f"    {result['stderr'][:300]}")


def cmd_publish():
    """Republish Guard CLI from latest code."""
    print("Publishing Guard CLI to AWS node...")
    cmd_id = ssm_send([
        "set -eu",
        "export HOME=/root DOTNET_CLI_HOME=/root DOTNET_CLI_TELEMETRY_OPTOUT=1",
        "export PATH=/usr/share/dotnet:/usr/local/bin:$PATH",
        f"cd {GUARD_REPO} && git fetch && git checkout {GUARD_BRANCH} && git pull",
        "git rev-parse --short HEAD",
        f"dotnet publish Schlieren.CLI/Schlieren.CLI.csproj -c Release -o /opt/schlieren-guard",
        "/opt/schlieren-guard/Schlieren.CLI --help | head -5",
    ])
    print(f"  Command: {cmd_id}")
    result = ssm_poll(cmd_id, timeout=120)
    print()

    if result["status"] == "Success":
        print(f"  ✓ Published successfully")
        # Find commit hash in output
        for line in result["stdout"].strip().split("\n"):
            line = line.strip()
            if re.match(r'^[0-9a-f]{7,12}$', line):
                print(f"  Commit: {line}")
                break
    else:
        print(f"  ✗ Publish failed: {result['status']}")
        print(result["stderr"][:500] if result["stderr"] else result["stdout"][:500])
        sys.exit(1)


def cmd_guard(token: str, block: int | None = None, raw: bool = False, timeout: int = 180):
    """Run Guard against a token and return verdict."""
    token = validate_token(token)
    token_stem = token[2:] if token.startswith("0x") else token

    block_flag = f" --block {block}" if block else ""
    
    print(f"Running Guard on {token}...")
    print(f"  Node: {INSTANCE_ID} ({REGION})")
    if block:
        print(f"  Pinned block: {block}")

    commands = [
        "set -eu",
        "export HOME=/root DOTNET_CLI_HOME=/root DOTNET_CLI_TELEMETRY_OPTOUT=1",
        "export PATH=/usr/share/dotnet:/usr/local/bin:$PATH",
        f"mkdir -p {GUARD_OUT}",
        # Preflight: is RPC alive?
        "echo PREFLIGHT",
        "curl -sf -m 10 -X POST http://127.0.0.1:8545 -H 'content-type: application/json' "
        "-d '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_blockNumber\",\"params\":[]}' "
        "| python3 -c \"import sys,json; r=json.load(sys.stdin).get('result','?'); "
        "print(f'BLOCK:{int(r,16)}')\" || { echo 'ERROR: Reth RPC not responding'; exit 1; }",
        # Run Guard
        "echo GUARD_START",
        "START=$(date +%s)",
        "set +e",
        f"{GUARD_CLI} guard {token} --fork-url http://127.0.0.1:8545{block_flag} --out {GUARD_OUT}",
        "EXIT=$?",
        "echo GUARD_EXIT:$EXIT",
        "echo WALL_SECONDS:$(($(date +%s)-START))",
        "set -e",
        # Read evidence — extract verdict summary (full JSON is too big for SSM's 24KB stdout)
        f"echo EVIDENCE_START",
        f"python3 -c \"\nimport json,sys\ntry:\n  d=json.load(open('{GUARD_OUT}/guard-{token_stem}.json'))\n  v=d.get('verdict',{{}})\n  print(json.dumps({{'verdict':v,'steps':[{{'name':s['name'],'success':s['success'],'gasUsed':s['gasUsed']}} for s in d.get('steps',[])],'token':d.get('token',''),'buyer':d.get('buyer',''),'pin':d.get('pin',{{}})}}))\\nexcept Exception as e: print('PARSE_ERROR:'+str(e))\n\" 2>/dev/null || echo NO_EVIDENCE",
        f"echo EVIDENCE_END",
    ]

    cmd_id = ssm_send(commands)
    print(f"  SSM Command: {cmd_id}")
    result = ssm_poll(cmd_id, timeout=timeout)
    print()  # newline after dots

    if result["status"] != "Success":
        print(f"  ✗ Command failed: {result['status']}")
        if result["stderr"]:
            print(f"  {result['stderr'][:500]}")
        if result["stdout"]:
            print(f"  {result['stdout'][:500]}")
        sys.exit(2)

    stdout = result["stdout"]

    # Extract wall time
    wall_match = re.search(r'WALL_SECONDS:(\d+)', stdout)
    wall = int(wall_match.group(1)) if wall_match else None

    # Extract exit code
    exit_match = re.search(r'GUARD_EXIT:(\d+)', stdout)
    guard_exit = int(exit_match.group(1)) if exit_match else None

    # Extract block
    block_match = re.search(r'BLOCK:(\d+)', stdout)
    node_block = int(block_match.group(1)) if block_match else None

    # Extract evidence JSON
    evidence_json = None
    ev_match = re.search(r'EVIDENCE_START\n(.*?)EVIDENCE_END', stdout, re.DOTALL)
    if ev_match:
        ev_text = ev_match.group(1).strip()
        if ev_text != "NO_EVIDENCE":
            try:
                evidence_json = json.loads(ev_text)
            except json.JSONDecodeError:
                # Might be truncated — SSM has a 24KB stdout limit
                print("  ⚠ Evidence JSON truncated (SSM output limit)")

    if raw and evidence_json:
        print(json.dumps(evidence_json, indent=2))
        return

    # Print Guard output (the text between GUARD_START and GUARD_EXIT)
    guard_text_match = re.search(r'GUARD_START\n(.*?)GUARD_EXIT:', stdout, re.DOTALL)
    if guard_text_match:
        guard_text = guard_text_match.group(1).strip()
        print("─" * 60)
        print(guard_text)
        print("─" * 60)

    # Summary
    print()
    if node_block:
        print(f"  Node block: {node_block:,}")
    if wall is not None:
        print(f"  Execution:  {wall}s")
    if guard_exit is not None:
        status_label = {0: "✓ Clean", 2: "✗ Scenario failed", 3: "⚠ Inconclusive/BuyFailed"}.get(guard_exit, f"? Exit {guard_exit}")
        print(f"  Guard exit: {status_label}")

    # Parse verdict from evidence
    if evidence_json:
        v = evidence_json.get("verdict", {})
        print()
        print(f"  Verdict:    {v.get('kind', '?')}")
        print(f"  Headline:   {v.get('headline', '?')}")
        loss = v.get('effectiveLossPercent')
        if loss is not None:
            print(f"  Loss:       {loss}%")
        hp = v.get('looksLikeHoneypot')
        if hp is not None:
            print(f"  Honeypot:   {hp}")
        causal = v.get('causalFrameId')
        if causal:
            print(f"  Causal:     frame={causal} contract={v.get('causalContract', '?')}")

        # Steps
        steps = evidence_json.get("steps", [])
        if steps:
            print()
            for s in steps:
                mark = "PASS" if s.get("success") else "FAIL"
                print(f"  [{mark}] {s['name']:<14} gas={s.get('gasUsed', '?')}")

        # Save evidence locally
        local_out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "guard-evidence")
        os.makedirs(local_out, exist_ok=True)
        local_path = os.path.join(local_out, f"guard-{token_stem}.json")
        with open(local_path, "w") as f:
            json.dump(evidence_json, f, indent=2)
        print(f"\n  Evidence saved: {local_path}")


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    args = sys.argv[1:]
    
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        sys.exit(0)

    # Parse flags
    publish = "--publish" in args
    health_only = "--health" in args
    raw = "--raw" in args
    
    timeout = 180
    if "--timeout" in args:
        idx = args.index("--timeout")
        timeout = int(args[idx + 1])
        args = args[:idx] + args[idx+2:]
    
    block = None
    if "--block" in args:
        idx = args.index("--block")
        block = int(args[idx + 1])
        args = args[:idx] + args[idx+2:]

    # Strip flags from args
    positional = [a for a in args if not a.startswith("--")]

    if health_only:
        cmd_health()
        return

    if publish:
        cmd_publish()
        if not positional:
            return  # publish-only

    if not positional:
        print("ERROR: No token address provided.", file=sys.stderr)
        print("Usage: python tools/guard-run.py <TOKEN_ADDRESS>", file=sys.stderr)
        sys.exit(1)

    cmd_guard(positional[0], block=block, raw=raw, timeout=timeout)


if __name__ == "__main__":
    main()
