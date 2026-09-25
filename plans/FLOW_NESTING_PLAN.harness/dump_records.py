"""Print one scenario's records in capture order, as metadata only (no bodies, no headers).

    python dump_records.py <report.json> <#ordinal | name substring>

Each line: index, type, the last four characters of requestResponseId, traceId, activityTraceId and
activitySpanId, the timestamp, stepPath, phase, metaType, isUserAction, caller -> service, method, path.
"""
import json, sys
from urllib.parse import urlsplit

report, needle = sys.argv[1], sys.argv[2]
with open(report, encoding="utf-8-sig") as f:
    data = json.load(f)

allsc = [s for feat in data["features"] for s in feat["scenarios"]]
scenario = allsc[int(needle[1:])] if needle.startswith("#") else next(s for s in allsc if needle in s["name"])
print(scenario["name"][:110], "|", scenario.get("result"))
for i, c in enumerate(scenario.get("httpInteractions") or []):
    uri = urlsplit(c.get("uri") or "")
    rr = (c.get("requestResponseId") or "")[-4:]
    tr = (c.get("traceId") or "")[-4:]
    at = (c.get("activityTraceId") or "")[-4:]
    sp = (c.get("activitySpanId") or "")[-4:]
    ts = (c.get("timestamp") or "")[11:23]
    print(f"i{i:<3} {c['type'][:4]:4} rr={rr:4} tr={tr:4} at={at:4} sp={sp:4} {ts:12} step={c.get('stepPath')!s:4} "
          f"{c.get('phase')!s:6} {c.get('metaType')!s:8} ua={int(bool(c.get('isUserAction')))} "
          f"{(c.get('callerName') or '')[:22]:22} -> {(c.get('serviceName') or '')[:24]:24} {c.get('method') or ''} {uri.path[:40]}")
