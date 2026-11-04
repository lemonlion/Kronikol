"""Static census of a folder of .feature files: python -I corpus-backgrounds.py <dir>.

For each feature: whether it writes a Background, how many scenarios it runs, and what BackgroundStepsDetector would do to its
scenarios had they all passed, by the rule in src/Kronikol/Reports/BackgroundStepsDetector.cs at 27cc0e46, on keyword + text as written.
Outline rows are expanded by placeholder filling of the step text only, as a table or doc string is not compared.
"""
import pathlib, re, sys, collections
root = pathlib.Path(sys.argv[1])
KW = ("Given ", "When ", "Then ", "And ", "But ", "* ")
def parse(path):
    feat = {"file": str(path.relative_to(root)), "name": None, "background": [], "rules": [], "scenarios": []}
    cur, rule, mode, in_doc, table_for, rows, hdr = None, None, None, False, None, [], None
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line.startswith('"""') or line.startswith("```"):
            in_doc = not in_doc; continue
        if in_doc or not line or line.startswith("#") or line.startswith("@"):
            continue
        if line.startswith("Feature:"): feat["name"] = line[8:].strip(); mode = "feature"; continue
        if line.startswith("Rule:"): rule = line[5:].strip(); feat["rules"].append({"name": rule, "background": []}); mode = "rule"; continue
        if line.startswith("Background:"): mode = "rule-bg" if rule else "bg"; continue
        m = re.match(r"(Scenario Outline|Scenario Template|Scenario|Example):\s*(.*)", line)
        if m:
            cur = {"name": m.group(2), "rule": rule, "steps": [], "outline": m.group(1) in ("Scenario Outline", "Scenario Template"), "rows": [], "hdr": None}
            feat["scenarios"].append(cur); mode = "scenario"; continue
        if re.match(r"(Examples|Scenarios):", line): mode = "examples"; cur["hdr"] = None; continue
        if line.startswith("|"):
            if mode == "examples":
                cells = [c.strip() for c in line.strip("|").split("|")]
                if cur["hdr"] is None: cur["hdr"] = cells
                else: cur["rows"].append(dict(zip(cur["hdr"], cells)))
            continue
        if line.startswith(KW):
            kw, text = line.split(" ", 1)
            step = ("And" if kw == "*" else kw, text)  # Reqnroll reports * as And
            if mode == "bg": feat["background"].append(step)
            elif mode == "rule-bg": feat["rules"][-1]["background"].append(step)
            elif mode == "scenario": cur["steps"].append(step)
    return feat
def expand(feat):
    out = []
    for s in feat["scenarios"]:
        rb = next((r["background"] for r in feat["rules"] if r["name"] == s["rule"]), [])
        bg = feat["background"] + rb
        if s["outline"] and s["rows"]:
            for row in s["rows"]:
                steps = []
                for kw, t in s["steps"]:
                    for k, v in row.items(): t = t.replace(f"<{k}>", v)
                    steps.append((kw, t))
                out.append({"rule": s["rule"], "written": len(bg), "steps": bg + steps, "name": s["name"], "outline": True})
        else:
            out.append({"rule": s["rule"], "written": len(bg), "steps": bg + s["steps"], "name": s["name"], "outline": False})
    return out
def detector(members):
    if len(members) < 2: return 0
    if any(m["steps"] and m["steps"][0][0] in ("And", "When") for m in members): return 0
    n = 0
    for i in range(min(len(m["steps"]) for m in members)):
        if all(m["steps"][i] == members[0]["steps"][i] for m in members): n += 1
        else: break
    if n == 0: return 0
    if any(len(m["steps"]) > n and m["steps"][n][0] in ("Given", "When") for m in members): return 0
    return n
totals = collections.Counter(); after = collections.Counter(); kinds = collections.Counter(); examples = {}
# Two files with one title are one feature, as ToFeatures groups them; the detector sees them together.
by_title = collections.defaultdict(list)
for path in sorted(root.rglob("*.feature")):
    feat = parse(path)
    by_title[feat["name"]].append(feat)
for title, feats in by_title.items():
    feat = feats[0]
    runs = [r for f in feats for r in expand(f)]
    feat = dict(feat, file=" + ".join(f["file"] for f in feats))
    totals["features"] += 1; totals["scenarios (rows counted)"] += len(runs)
    if any(f["background"] or any(r["background"] for r in f["rules"]) for f in feats):
        totals["features writing a Background"] += 1
    for f in feats:
        for s in f["scenarios"]:
            first = s["steps"][0][0] if s["steps"] else "(none)"
            if f["background"]: after[first] += 1
    groups = collections.defaultdict(list)
    for r in runs: groups[r["rule"]].append(r)
    for rule, members in groups.items():
        members = [m for m in members if m["steps"]]
        marked = detector(members)
        for m in members:
            w = m["written"]
            verdict = "right: none written, none marked" if w == 0 and marked == 0 else "right: as written" if w == marked else "MISSED: written, not marked" if marked < w and marked == 0 else "INVENTED: marked, none written" if w == 0 else "WRONG COUNT"
            kinds[verdict] += 1
            if marked and verdict != "right: as written": examples.setdefault(verdict, []).append((feat["file"], m["name"], w, marked))
            if marked: totals["scenarios whose steps a report replaces with the first's"] += 1 if m is not members[0] else 0
for k, v in totals.items(): print(f"{v:5}  {k}")
print("\nFirst step a scenario writes after a feature-level Background:")
for k, v in after.most_common(): print(f"{v:5}  {k}")
print("\nThe detector against what is written, every scenario passing:")
for k, v in kinds.most_common(): print(f"{v:5}  {k}")
for verdict, rows in examples.items():
    print(f"\n{verdict}, first 12:")
    for file, name, w, marked in rows[:12]: print(f"  {file}: {name} (written {w}, marked {marked})")
