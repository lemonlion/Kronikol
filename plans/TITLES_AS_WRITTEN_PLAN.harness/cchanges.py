"""Classify candidate C's changed rows by what the change does (variants.tsv from rules.cs)."""
import collections, re, sys
rows = []
with open(sys.argv[1], encoding="utf-8") as f:
    header = next(f).rstrip("\n").split("\t")
    for line in f:
        rows.append(dict(zip(header, line.rstrip("\n").split("\t"))))
FRAGMENTS = {"QL": "GraphQL", "BDD": "LightBDD", "UML": "PlantUML", "MS": "MSTest", "RPC": "gRPC", "BD": "BDDfy",
             "SQ": "SQLite", "DB": "MongoDB/CosmosDB", "HM": "Redis HMGet", "F6": "hex F6F6F6"}
cat = collections.Counter(); ex = collections.defaultdict(list)
for r in rows:
    if r["C"] == r["today"]:
        continue
    t, c = r["today"].split(" "), r["C"].split(" ")
    kinds = set()
    if len(t) != len(c):
        kinds.add("split changed (digit then capital)")
    changed = [w for w in c if w not in t] if len(t) != len(c) else [cw for tw, cw in zip(t, c) if tw != cw]
    for w in changed:
        if w == "I":
            kinds.add("interface or IPv I kept as a word")
        elif w in FRAGMENTS:
            kinds.add("part of a product name now in capitals (" + FRAGMENTS[w] + ")")
    if not kinds or kinds == {"split changed (digit then capital)"}:
        kinds.add("plain correction")
    key = " + ".join(sorted(kinds))
    cat[(r["corpus"], key)] += 1
    if len(ex[key]) < 3:
        ex[key].append(f'{r["method"]} => {r["C"]}')
for (corpus, key), n in sorted(cat.items(), key=lambda kv: (kv[0][0], -kv[1])):
    print(f"{corpus:18} {n:4}  {key}")
print()
for key, e in ex.items():
    print(key); [print("   ", x) for x in e]
# acronym + Is/As as a verb or preposition (why the plural rule leaves Is/As alone)
pat = re.compile(r"(?<![A-Z])[A-Z]{2,}(Is|As)(?=[A-Z]|_|$)")
hits = [r["method"] for r in rows if pat.search(r["method"])]
print("\nacronym + Is/As glued in the corpus:", len(hits), hits[:10])
pat2 = re.compile(r"(?<![A-Z])[A-Z](Is|As)(?=[A-Z]|_|$)")
hits2 = [r["method"] for r in rows if pat2.search(r["method"])]
print("single capital + Is/As glued:", len(hits2), hits2[:10])
