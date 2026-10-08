"""Prints the key tree of a JSON file (keys, types, array lengths), never its values.

For finding the fields compare.py reads without opening TestRunReport.json. Usage: python tools/keys.py <file.json> [maxdepth]
"""
import json
import sys


def walk(node, path, depth, maxdepth, seen):
    if depth > maxdepth:
        return
    if isinstance(node, dict):
        for k, v in node.items():
            p = f"{path}.{k}"
            sig = (p, type(v).__name__)
            if sig not in seen:
                seen.add(sig)
                extra = f" [{len(v)}]" if isinstance(v, (list, dict)) else ""
                print(f"{'  ' * depth}{k}: {type(v).__name__}{extra}")
            walk(v, p, depth + 1, maxdepth, seen)
    elif isinstance(node, list):
        for item in node:
            walk(item, path + "[]", depth, maxdepth, seen)


doc = json.load(open(sys.argv[1], encoding="utf-8-sig"))
walk(doc, "$", 0, int(sys.argv[2]) if len(sys.argv) > 2 else 6, set())
