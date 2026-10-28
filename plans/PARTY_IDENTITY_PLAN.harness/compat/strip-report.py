"""The inverse of make-report.py, for a report a release wrote (real-report.cs): writes <out>/new/TestRunReport.json
(the decoded report, unchanged) and <out>/base/TestRunReport.json (the same with serviceId, parties and every call's
testDouble taken out, nothing else touched), so run.sh compares what an older tool reads of the real keys with what it
reads without them (plan section 7.5).

usage: python -I strip-report.py <decoded.json> <out>
"""
import json
import os
import shutil
import sys

source, out = sys.argv[1], sys.argv[2]
os.makedirs(f"{out}/base", exist_ok=True)
os.makedirs(f"{out}/new", exist_ok=True)
shutil.copy(source, f"{out}/new/TestRunReport.json")
report = json.load(open(source, encoding="utf-8"))
found = {"serviceId": "serviceId" in report, "parties": "parties" in report}
report.pop("serviceId", None)
report.pop("parties", None)
marks = 0


def strip(node):
    global marks
    if isinstance(node, dict):
        if "testDouble" in node:
            del node["testDouble"]
            marks += 1
        for value in node.values():
            strip(value)
    elif isinstance(node, list):
        for value in node:
            strip(value)


strip(report)
with open(f"{out}/base/TestRunReport.json", "w", encoding="utf-8", newline="\n") as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
print(f"serviceId {found['serviceId']}, parties {found['parties']}, testDouble marks {marks}")
