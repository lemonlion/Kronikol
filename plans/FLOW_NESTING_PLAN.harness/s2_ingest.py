"""R4 on an ingested run (plan §7.5 and Q4): does `kronikol ingest` keep the parents a run's own report gives?

    python s2_ingest.py project <TestRunReport.json> <out.ndjson>
    python s2_ingest.py compare <reference TestRunReport.json> <ingested TestRunReport.json>

`project` writes every scenario's httpInteractions as ingest input: each record as the report holds it, plus
the scenario's testId and testName, which is the input shape `kronikol ingest --help` documents. Markers
are not in httpInteractions, so the ingested scenarios have no steps; R4 does not read them.

`compare` computes R4's parents (flow_prototype.parents) on both reports, per scenario matched by id, and
compares each request's parent by requestResponseId: the same parent, both top level, or not. Prints
counts and, for each kind of disagreement, a few addresses with caller and service names. Reports are read
with json.load; no body or header is printed.
"""
import collections, json, sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from flow_prototype import parents, real_id  # noqa: E402


def scenarios(path):
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    ordinal = -1
    for feature in data.get("features") or []:
        for sc in feature.get("scenarios") or []:
            ordinal += 1
            yield ordinal, sc


def project(report, out):
    lines = 0
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        for _, sc in scenarios(report):
            for rec in sc.get("httpInteractions") or []:
                f.write(json.dumps({**rec, "testId": sc.get("id"), "testName": sc.get("name")}, ensure_ascii=False) + "\n")
                lines += 1
    print(f"{lines} records written")


def parent_ids(sc):
    recs = sc.get("httpInteractions") or []
    par = parents(recs)
    out = {}
    for i, rec in enumerate(recs):
        if rec.get("type") == "Request" and real_id(rec.get("requestResponseId")):
            p = par.get(i)
            out[rec["requestResponseId"]] = (real_id(recs[p].get("requestResponseId")) if p is not None else None, i, rec)
    return out


def compare(reference, ingested):
    ref = {sc.get("id"): (o, sc) for o, sc in scenarios(reference)}
    counts = collections.Counter()
    examples = collections.defaultdict(list)
    for ordinal, sc in scenarios(ingested):
        if sc.get("id") not in ref:
            counts["ingested scenario not in the reference"] += 1
            continue
        ref_ordinal, ref_sc = ref[sc.get("id")]
        want, got = parent_ids(ref_sc), parent_ids(sc)
        for rid, (parent, i, rec) in want.items():
            if rid not in got:
                counts["request missing from the ingested report"] += 1
                continue
            other, j, _ = got[rid]
            kind = ("same parent" if parent is not None else "both top level") if parent == other \
                else "nested in the reference, top level ingested" if other is None \
                else "top level in the reference, nested ingested" if parent is None \
                else "a different parent"
            counts[kind] += 1
            if kind not in ("same parent", "both top level") and len(examples[kind]) < 6:
                examples[kind].append(f"reference s{ref_ordinal}/i{i} ingested s{ordinal}/i{j}: "
                                      f"{rec.get('callerName')} -> {rec.get('serviceName')} {rec.get('method')}")
    for kind, n in sorted(counts.items()):
        print(f"{kind}: {n:,}")
    for kind, lines in examples.items():
        print(f"{kind}, e.g.:")
        for line in lines:
            print(f"    {line}")


if __name__ == "__main__":
    if sys.argv[1] == "project":
        project(sys.argv[2], sys.argv[3])
    else:
        compare(sys.argv[2], sys.argv[3])
