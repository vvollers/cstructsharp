#!/usr/bin/env python3
"""Extract every cstruct definition string from the dissect ecosystem into one JSON corpus file.

Usage: extract-dissect-corpus.py <ecosystem-dir> <output.json>

The corpus is consumed by tests/CStructSharpTests/Dissect/DissectCorpusSweepTests.cs (opt-in through
CSTRUCTSHARP_DISSECT_CORPUS). Entries are {id, repository, path, line, source}. Definitions are recognized the same
way the parity inventory recognizes them: a Python string literal that starts a line with
typedef/struct/union/enum/flag/#define. Docstrings that merely mention a struct and fragments assembled from
several literals (unbalanced braces, or text before the first declaration) are not definitions and are skipped.
"""
import ast, json, pathlib, re, sys

root = pathlib.Path(sys.argv[1]).resolve()
out = pathlib.Path(sys.argv[2])
skip = ("dissect.cstruct/", "dissect.cstruct_legacy", "dissect_legacy", "/tests/", "/build/", "dissect-docs", "-templates", "splunk")


def is_definition(s):
    """A whole definition: balanced braces and nothing but comments/blank lines before the first declaration."""
    if s.count("{") != s.count("}"):
        return False
    body = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
    body = re.sub(r"//[^\n]*", "", body)
    first = next((line.strip() for line in body.splitlines() if line.strip()), "")
    return re.match(r"(typedef|struct|union|enum|flag|#)\b", first) is not None


entries = []
for path in sorted(root.rglob("*.py")):
    p = str(path)
    if any(s in p for s in skip):
        continue
    try:
        tree = ast.parse(path.read_text(errors="replace"))
    except Exception:
        continue
    rel = p.replace(str(root) + "/", "")
    repo = rel.split("/")[0]
    for node in ast.walk(tree):
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            s = node.value
            if len(s) > 40 and re.search(r"^\s*(typedef|struct|union|enum|flag|#define)\b", s, re.M) and is_definition(s):
                entries.append({"id": f"{rel}:{node.lineno}", "repository": repo, "path": rel, "line": node.lineno, "source": s})
out.write_text(json.dumps(entries, indent=1))
print(f"{len(entries)} definitions -> {out}")
