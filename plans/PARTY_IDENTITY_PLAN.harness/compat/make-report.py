"""Writes <out>/base/TestRunReport.json (a decoded report, unchanged) and <out>/new/TestRunReport.json (the same with
PARTY_IDENTITY_PLAN's block after `environment`, and `testDouble` on every Cow Service call), so readers can be run on
both (plan §5.5, §5.10; run.sh).

usage: python -I make-report.py <decoded.json> <out>
"""
import json
import os
import shutil
import sys

source, out = sys.argv[1], sys.argv[2]
os.makedirs(f"{out}/base", exist_ok=True)
os.makedirs(f"{out}/new", exist_ok=True)
shutil.copy(source, f"{out}/base/TestRunReport.json")
report = json.load(open(source, encoding="utf-8"))
changed = {}
for key, value in report.items():
    changed[key] = value
    if key == "environment":
        changed["serviceId"] = "breakfast-provider"
        changed["parties"] = [
            {"name": "Breakfast Provider", "id": "breakfast-provider", "kind": "underTest", "kindSource": "declared", "note": "BreakfastProvider.Api"},
            {"name": "Caller", "kind": "test", "kindSource": "observed"},
            {"name": "Cow Service", "id": "cow-service", "kind": "double", "kindSource": "declared", "note": "Dependencies.Fakes.CowService"},
            {"name": "Kafka Broker", "kind": "unknown"}]
marked = 0
for feature in changed["features"]:
    for scenario in feature["scenarios"]:
        for call in scenario.get("httpInteractions") or []:
            if call.get("serviceName") == "Cow Service":
                call["testDouble"] = "WireMock"
                marked += 1
with open(f"{out}/new/TestRunReport.json", "w", encoding="utf-8") as handle:
    json.dump(changed, handle, ensure_ascii=False, separators=(",", ":"))
print(f"block added; testDouble on {marked} records")
