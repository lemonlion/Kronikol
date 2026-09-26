"""Why a call nested in a run's own report is top level once the run is ingested (plan F17, Q4).

    python s2_ingest_causes.py <reference TestRunReport.json> <ingested TestRunReport.json>

For every request R4 nests in the reference and not in the ingested report (matched by requestResponseId,
scenarios by id), counts which clause nested it, (a) made by the parent's service or (b) on the parent's
trace, and whether its request carried a timestamp. Counts only; no body or header is printed.
"""
import collections, json, pathlib, sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from flow_prototype import parents, real_id  # noqa: E402


def scenarios(path):
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    for feature in data.get("features") or []:
        for sc in feature.get("scenarios") or []:
            yield sc


def parent_map(sc):
    recs = sc.get("httpInteractions") or []
    par = parents(recs)
    out = {}
    for i, rec in enumerate(recs):
        if rec.get("type") == "Request" and real_id(rec.get("requestResponseId")):
            p = par.get(i)
            out[rec["requestResponseId"]] = (recs[p] if p is not None else None, rec)
    return out


def main():
    ref = {sc.get("id"): parent_map(sc) for sc in scenarios(sys.argv[1])}
    causes = collections.Counter()
    for sc in scenarios(sys.argv[2]):
        got = parent_map(sc)
        for rid, (want_parent, rec) in ref.get(sc.get("id"), {}).items():
            if want_parent is None or rid not in got or got[rid][0] is not None:
                continue
            clause = "(a) the parent's service made it" if want_parent.get("serviceName") == rec.get("callerName") else "(b) on the parent's trace"
            stamped = "has a timestamp" if rec.get("timestamp") else "no timestamp"
            causes[f"{clause}, {stamped}"] += 1
    for kind, n in sorted(causes.items()):
        print(f"{kind}: {n}")


if __name__ == "__main__":
    main()
