"""Per report: how many callee names have calls whose URI carries a real host, a placeholder host, or none.

Reads the scenarios' httpInteractions and the background list, which the data file names interactions.

usage: python -I hosts.py <decoded.json> [...]
"""
import json
import sys
from collections import defaultdict
from urllib.parse import urlsplit

PLACEHOLDER = {"topics", "unknown"}

for path in sys.argv[1:]:
    with open(path, encoding="utf-8") as handle:
        report = json.load(handle)
    calls = [i for f in report.get("features", []) for s in f.get("scenarios", []) for i in s.get("httpInteractions", []) or []]
    calls += (report.get("background") or {}).get("interactions", []) or []
    hosts = defaultdict(set)
    for interaction in calls:
        if interaction.get("type") != "Request":
            continue
        try:
            host = urlsplit(interaction.get("uri") or "").hostname or ""
        except ValueError:
            host = "?"
        hosts[interaction.get("serviceName")].add(host)
    none = sorted(n for n, hs in hosts.items() if hs == {""})
    placeholder = sorted(n for n, hs in hosts.items() if hs and hs <= PLACEHOLDER)
    real = sorted(n for n, hs in hosts.items() if hs - {""} - PLACEHOLDER)
    print(f"{path.replace(chr(92), '/').split('/')[-1]}: {len(hosts)} callee names; no host {len(none)} {none}; "
          f"placeholder host {len(placeholder)} {placeholder}; a real host {len(real)}")
