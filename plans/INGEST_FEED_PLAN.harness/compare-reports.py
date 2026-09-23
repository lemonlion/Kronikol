"""Compare two TestRunReport.json files scenario by scenario without printing their content.

Usage: python compare-reports.py <in-process TestRunReport.json> <ingested TestRunReport.json> [--ignore error,stepPath]

Prints, per scenario matched by id: interaction counts, which interaction members differ (with one
example each), whether annotations and the diagram source are identical, and the first differing
diagram line. Never dumps a report: the files can be megabytes.
"""
import json
import sys


def scenarios(path):
    with open(path, encoding="utf-8") as f:
        report = json.load(f)
    out = {}
    for feature in report.get("features", []):
        for s in feature.get("scenarios", []):
            out[s["id"]] = s
    return out


def main(argv):
    a_path, b_path = argv[1], argv[2]
    ignore = set()
    if "--ignore" in argv:
        ignore = set(argv[argv.index("--ignore") + 1].split(","))
    a, b = scenarios(a_path), scenarios(b_path)
    print(f"scenarios: in-process {len(a)}, ingested {len(b)}, common {len(set(a) & set(b))}")
    for sid in a:
        if sid not in b:
            print(f"  only in-process: {sid} ({a[sid].get('name')})")
    for sid in b:
        if sid not in a:
            print(f"  only ingested: {sid} ({b[sid].get('name')})")

    identical_diagrams = 0
    for sid in sorted(set(a) & set(b)):
        sa, sb = a[sid], b[sid]
        ia, ib = sa.get("httpInteractions", []), sb.get("httpInteractions", [])
        member_diffs = {}
        for i in range(min(len(ia), len(ib))):
            names = set(ia[i]) | set(ib[i])
            for n in sorted(names - ignore):
                va, vb = ia[i].get(n, "<absent>"), ib[i].get(n, "<absent>")
                if va != vb and n not in member_diffs:
                    member_diffs[n] = (i, str(va)[:60], str(vb)[:60])
        anns = json.dumps(sa.get("annotations"), sort_keys=True) == json.dumps(sb.get("annotations"), sort_keys=True)
        da = (sa.get("diagrams") or [""])[0]
        db = (sb.get("diagrams") or [""])[0]
        same = da == db
        identical_diagrams += same
        steps_a, steps_b = len(sa.get("steps", [])), len(sb.get("steps", []))
        print(f"- {sid[:12]} {str(sa.get('name'))[:50]!r}: interactions {len(ia)}/{len(ib)}; annotations {'same' if anns else 'DIFFER'}; "
              f"diagram {'same' if same else 'DIFFERS'} ({len(da)}/{len(db)} chars); steps {steps_a}/{steps_b}; result {sa.get('result')}/{sb.get('result')}")
        for n, (i, va, vb) in member_diffs.items():
            print(f"    member {n} differs, first at #{i}: {va!r} vs {vb!r}")
        if not same:
            la, lb = da.split("\n"), db.split("\n")
            shown = 0
            for i in range(max(len(la), len(lb))):
                x = la[i] if i < len(la) else "<eof>"
                y = lb[i] if i < len(lb) else "<eof>"
                if x != y:
                    print(f"    diagram line {i}: {x[:90]!r} | {y[:90]!r}")
                    shown += 1
                    if shown >= 4:
                        break
    print(f"diagrams identical: {identical_diagrams} of {len(set(a) & set(b))}")


if __name__ == "__main__":
    main(sys.argv)
