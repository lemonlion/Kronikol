"""Counts only: per decoded data file, the parties that received a call and those with no host (an empty URI authority)
in any call they received, over the scenarios' calls and the root background block. Prints party names, never a body.
Usage: python -I -X utf8 verify-party_hosts.py <label>=<decoded.json> [...]
"""
import json
import sys
from collections import defaultdict


def authority(uri):
    if "://" not in uri:
        return ""
    return uri.split("://", 1)[1].split("/", 1)[0].split("?", 1)[0]


for arg in sys.argv[1:]:
    label, path = arg.split("=", 1)
    with open(path, encoding="utf-8") as f:
        root = json.load(f)
    calls = []
    for feature in root.get("features") or []:
        for scenario in feature.get("scenarios") or []:
            calls.extend(scenario.get("httpInteractions") or [])
    background = root.get("background")
    if isinstance(background, dict):
        calls.extend(background.get("interactions") or [])
    hosts = defaultdict(set)
    for call in calls:
        if call.get("type") != "Request" or not call.get("serviceName"):
            continue
        hosts[call["serviceName"]].add(authority(call.get("uri") or ""))
    none = sorted(p for p, h in hosts.items() if h <= {""})
    sql_hosts = sorted({h for p, hs in hosts.items() if "SQL" in p for h in hs})
    print(f"{label} (kronikolVersion {str(root.get('kronikolVersion'))[:6]}): called parties {len(hosts)}, "
          f"no host {len(none)}: {', '.join(none)}; SQL Server parties' hosts {sql_hosts}")
