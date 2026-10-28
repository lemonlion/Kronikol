# Counts only: how many calls a decoded data file holds in scenarios' httpInteractions and in the root
# background.interactions block, which ReportScanner does not read. Prints no bodies, no names beyond
# party names and attribution sources.
# Usage: python -I -X utf8 background_block.py <decoded.json> [...]
import json
import sys
from collections import Counter


def main(paths):
    for path in paths:
        with open(path, encoding="utf-8") as f:
            root = json.load(f)
        scen_req = 0
        scen_all = 0
        for feature in root.get("features") or []:
            for scenario in feature.get("scenarios") or []:
                for i in scenario.get("httpInteractions") or []:
                    scen_all += 1
                    if i.get("type") == "Request":
                        scen_req += 1
        bg = root.get("background")
        print(f"== {path.split('/')[-1]}")
        print(f"   root keys: {sorted(root.keys())}")
        print(f"   kronikolVersion={root.get('kronikolVersion')} formatVersion={root.get('formatVersion')}")
        print(f"   scenario interactions: {scen_all} records, {scen_req} requests")
        if bg is None:
            print("   background: absent")
            continue
        inter = bg.get("interactions") or []
        reqs = [i for i in inter if i.get("type") == "Request"]
        print(f"   background.calls={bg.get('calls')} interactions={len(inter)} requests={len(reqs)} afterScenarioEnd rows={len(bg.get('afterScenarioEnd') or [])}")
        edges = Counter((i.get("callerName"), i.get("serviceName")) for i in reqs)
        for (c, s), n in edges.most_common(15):
            print(f"     {n:5d}  {c} > {s}")
        src = Counter(i.get("attributionSource") for i in reqs)
        print(f"   background attributionSource: {dict(src)}")
        phases = Counter(i.get("phase") for i in reqs)
        print(f"   background phase: {dict(phases)}")


if __name__ == "__main__":
    main(sys.argv[1:])
