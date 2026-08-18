#!/usr/bin/env python3
"""Extract the normative gas schedule from EELs-NotebookLM per-fork docs.

Reads every fork-*.md export (raw EELS Python source), parses the GasCosts
class from vm/gas.py, resolves constant references, and emits a C# static
data module (ForkGasData.g.cs) with per-constant source citations.

Usage: python tools/specdata/generate.py
"""

import ast
import glob
import os
import re
import sys
from dataclasses import dataclass

DOC_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "EELs-NotebookLM")
OUT_PATH = os.path.join(
    os.path.dirname(__file__), "..", "..",
    "Schlieren.EELS.Tests", "SpecData", "ForkGasData.g.cs")

MEMBER_RE = re.compile(
    r"^\s+(?P<name>[A-Z][A-Z0-9_]*)\s*:\s*Final\[(?P<kind>Uint|U256|U64|int|bool)\]\s*=\s*(?P<expr>.+?)\s*$")


@dataclass
class Constant:
    name: str
    expr: str            # raw RHS expression as written (e.g. "VERY_LOW", "Uint(3)")
    value: int           # resolved value
    source_line: int     # 1-based line in the .md file
    kind: str


def _strip_comment(expr: str) -> str:
    return re.split(r"\s+#", expr, maxsplit=1)[0].strip()


def _eval_expr(expr: str, by_name: dict) -> int:
    """Evaluate a Python constant expression that may reference previously
    parsed constants, e.g. 'VERY_LOW', 'Uint(3)', 'int((A + B) * 4800 // 5000)',
    'Uint(2**17)', 'PER_BLOB * BLOB_SCHEDULE_TARGET'. Uses Python's own AST so
    operator precedence and grouping are exact."""
    expr = _strip_comment(expr).strip()
    node = ast.parse(expr, mode="eval").body
    return _eval_ast(node, by_name)


def _eval_ast(node, by_name: dict) -> int:
    if isinstance(node, ast.Constant):
        return int(node.value)
    if isinstance(node, ast.Name):
        if node.id in by_name:
            return by_name[node.id].value
        raise ValueError(f"unknown name: {node.id}")
    if isinstance(node, ast.Call):
        # Uint(...) / U256(...) / U64(...) / int(...) wrappers
        return _eval_ast(node.args[0], by_name)
    if isinstance(node, ast.BinOp):
        left = _eval_ast(node.left, by_name)
        right = _eval_ast(node.right, by_name)
        if isinstance(node.op, ast.Add):
            return left + right
        if isinstance(node.op, ast.Sub):
            return left - right
        if isinstance(node.op, ast.Mult):
            return left * right
        if isinstance(node.op, ast.Div):
            return left // right
        if isinstance(node.op, ast.FloorDiv):
            return left // right
        if isinstance(node.op, ast.Pow):
            return left ** right
        raise ValueError(f"unsupported op: {type(node.op).__name__}")
    raise ValueError(f"unsupported expression node: {type(node).__name__}")


def _is_fully_wrapped(expr: str) -> bool:
    """True if expr is surrounded by one matching pair of parentheses that
    fully encloses it, e.g. '(a + b)' but not '(a) * (b)'."""
    if not (expr.startswith("(") and expr.endswith(")")):
        return False
    depth = 0
    for idx, ch in enumerate(expr):
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return idx == len(expr) - 1
    return False


def parse_gas_costs(lines: list[str], start: int) -> list[Constant]:
    """Parse the GasCosts class body from `lines`, starting at `start` (index of
    the `class GasCosts:` line). Returns ordered constants; resolves references
    to earlier constants in the class."""
    constants: list[Constant] = []
    by_name: dict[str, Constant] = {}

    i = start + 1
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        if not stripped:
            i += 1
            continue
        if not line.startswith("    "):
            # class body ended (next top-level statement)
            break
        if stripped.startswith(('"""', "'''")) or stripped in ("@final",):
            i += 1
            continue
        m = MEMBER_RE.match(line)
        if not m:
            i += 1
            continue
        name, kind, expr = m.group("name"), m.group("kind"), m.group("expr").strip()

        # Join continuation lines when parens are unbalanced (e.g. int(\n ... \n)).
        while expr.count("(") > expr.count(")") and i + 1 < len(lines):
            i += 1
            nxt = lines[i].strip()
            if not nxt:
                break
            expr += " " + nxt

        expr = _strip_comment(expr)
        const = Constant(name=name, expr=expr, value=0, source_line=i + 1, kind=kind)
        constants.append(const)
        by_name[name] = const
        i += 1

    # Resolve all expressions (allows forward references) with fixed-point iteration.
    resolved: set[str] = set()
    for _ in range(8):
        changed = False
        for const in constants:
            if const.name in resolved:
                continue
            try:
                if const.kind == "bool":
                    const.value = 1 if const.expr == "True" else 0
                elif const.expr in by_name:
                    const.value = by_name[const.expr].value
                else:
                    const.value = _eval_expr(const.expr, by_name)
                resolved.add(const.name)
                changed = True
            except ValueError:
                pass
        if not changed:
            break

    for const in constants:
        if const.name not in resolved:
            print(f"  !! unresolved: {const.name} = {const.expr} ({const.source_line})")

    return constants


def parse_fork_file(path: str) -> list[Constant]:
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    lines = text.splitlines()

    # Locate the FILE: section for vm/gas.py
    gas_start = None
    for idx, line in enumerate(lines):
        if line.startswith("FILE:") and "/vm/gas.py" in line:
            gas_start = idx
            break
    if gas_start is None:
        print(f"  !! no vm/gas.py section in {path}")
        return []

    # Find the python code block start and the GasCosts class within it.
    class_start = None
    for idx in range(gas_start, min(gas_start + 4000, len(lines))):
        if re.match(r"^\s*class GasCosts\s*:", lines[idx]):
            class_start = idx
            break
    if class_start is None:
        print(f"  !! no GasCosts class in {path}")
        return []

    return parse_gas_costs(lines, class_start)


def fork_name(path: str) -> str:
    base = os.path.basename(path)
    return base.replace("fork-", "").replace(".md", "")


def cs_ident(fork: str) -> str:
    parts = fork.replace("-", "_").split("_")
    return "".join(p.capitalize() for p in parts)


def cs_str(s: str) -> str:
    """Escape a string for a C# double-quoted literal."""
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def emit_cs(forks: list[tuple[str, str, list[Constant]]]) -> str:
    out = []
    out.append("// <auto-generated> by tools/specdata/generate.py")
    out.append("// DO NOT EDIT MANUALLY. Re-run: python tools/specdata/generate.py")
    out.append("//")
    out.append("// Normative EVM gas schedule extracted from the Ethereum Execution")
    out.append("// Layer Specs (EELS) NotebookLM exports (EELs-NotebookLM/fork-*.md).")
    out.append("// Each constant carries its source file and line so the data can be")
    out.append("// audited against the spec it was derived from.")
    out.append("#nullable enable")
    out.append("")
    out.append("using System.Collections.Generic;")
    out.append("")
    out.append("namespace Schlieren.EELS.Tests.SpecData;")
    out.append("")
    out.append("/// <summary>")
    out.append("/// A single normative gas constant from the EELS GasCosts class.")
    out.append("/// </summary>")
    out.append("public sealed record GasConstant(")
    out.append("    string Name,")
    out.append("    ulong Value,")
    out.append("    string SourceFile,")
    out.append("    int SourceLine,")
    out.append("    string Raw);")
    out.append("")
    out.append("/// <summary>")
    out.append("/// The resolved gas schedule for one hard fork, keyed by GasCosts member name.")
    out.append("/// </summary>")
    out.append("public sealed record ForkGasSchedule(")
    out.append("    string Fork,")
    out.append("    string SourceFile,")
    out.append("    IReadOnlyDictionary<string, GasConstant> Constants);")
    out.append("")
    out.append("public static partial class ForkGasData")
    out.append("{")
    out.append("    public static readonly IReadOnlyList<ForkGasSchedule> AllForks = new List<ForkGasSchedule>")
    out.append("    {")
    for fork, source_file, constants in forks:
        ident = cs_ident(fork)
        out.append(f"        // {fork} ({source_file})")
        out.append(f"        new ForkGasSchedule(")
        out.append(f'            Fork: "{fork}",')
        out.append(f'            SourceFile: "{source_file}",')
        out.append(f"            Constants: new Dictionary<string, GasConstant>")
        out.append("            {")
        for c in constants:
            out.append(f'                ["{c.name}"] = new GasConstant("{c.name}", {c.value}UL, "{source_file}", {c.source_line}, {cs_str(c.expr)}),')
        out.append("            }),")
    out.append("    };")
    out.append("")
    out.append("    public static ForkGasSchedule? GetFork(string fork) =>")
    out.append("        AllForks.FirstOrDefault(f => string.Equals(f.Fork, fork, System.StringComparison.OrdinalIgnoreCase));")
    out.append("")
    out.append("    public static ulong? Get(string fork, string name)")
    out.append("    {")
    out.append("        var schedule = GetFork(fork);")
    out.append("        if (schedule is null) return null;")
    out.append("        return schedule.Constants.TryGetValue(name, out var c) ? c.Value : null;")
    out.append("    }")
    out.append("}")
    return "\n".join(out) + "\n"


def main() -> int:
    patterns = sorted(glob.glob(os.path.join(DOC_DIR, "fork-*.md")))
    if not patterns:
        print(f"No fork-*.md found in {DOC_DIR}")
        return 1

    forks = []
    for path in patterns:
        name = fork_name(path)
        print(f"Parsing {name} ...")
        consts = parse_fork_file(path)
        if not consts:
            print(f"  !! extracted 0 constants from {name}")
            return 1
        forks.append((name, os.path.basename(path), consts))
        print(f"  {len(consts)} constants")

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(emit_cs(forks))
    print(f"Wrote {OUT_PATH} ({len(forks)} forks)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
