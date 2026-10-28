"""Key-path inventory of a JSON file: every path (array indices collapsed to []) with how often it occurs and the
value types seen there. Prints no values. usage: python -I shape.py <file.json> [max_depth]"""
import json
import sys
from collections import Counter, defaultdict

counts = Counter()
types = defaultdict(Counter)


def walk(node, path, depth, max_depth):
    if depth > max_depth:
        return
    if isinstance(node, dict):
        for key, value in node.items():
            child = f"{path}.{key}"
            counts[child] += 1
            types[child][type(value).__name__] += 1
            walk(value, child, depth + 1, max_depth)
    elif isinstance(node, list):
        for item in node:
            walk(item, path + "[]", depth + 1, max_depth)


with open(sys.argv[1], encoding="utf-8-sig") as handle:
    document = json.load(handle)
walk(document, "$", 0, int(sys.argv[2]) if len(sys.argv) > 2 else 12)
for path in sorted(counts):
    print(f"{counts[path]:6}  {path}  {dict(types[path])}")
