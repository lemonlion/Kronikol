"""Counts only: per decoded data file, the Cosmos DB Replace requests, by where they sit (a scenario's calls or the root
background block), caller, collection and attributionSource. Prints no body and no document id.
Usage: python -I -X utf8 verify-replace_calls.py <label>=<decoded.json> [...]
"""
import json
import sys
from collections import Counter

for arg in sys.argv[1:]:
    label, path = arg.split("=", 1)
    with open(path, encoding="utf-8") as f:
        root = json.load(f)
    counts = Counter()

    def scan(calls, where):
        for i in calls or []:
            if i.get("type") != "Request" or i.get("method") != "Replace" or "Cosmos" not in (i.get("serviceName") or ""):
                continue
            uri = i.get("uri") or ""
            rest = uri.split("://", 1)[1] if "://" in uri else uri
            path_part = rest.split("/", 1)[1] if "/" in rest else ""
            counts[(where, i.get("callerName"), path_part.split("/docs/")[0], i.get("attributionSource"))] += 1

    for feature in root.get("features") or []:
        for scenario in feature.get("scenarios") or []:
            scan(scenario.get("httpInteractions"), "scenario")
    background = root.get("background")
    if isinstance(background, dict):
        scan(background.get("interactions"), "root block")
    rows = "; ".join(f"{n} in {w}, {c} > CosmosDB {coll}, {a}" for (w, c, coll, a), n in sorted(counts.items()))
    print(f"{label}: {rows or 'none'}")
