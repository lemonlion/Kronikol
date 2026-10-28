"""Shape counts under candidate C, and the cost of each variant (rules.cs `variants` output).

usage: python -I analyze.py variants.tsv
Method names in the corpus are all ASCII, so [A-Z]/[a-z] stand for \\p{Lu}/\\p{Ll} here.
"""
import collections
import re
import sys

rows = []
with open(sys.argv[1], encoding="utf-8") as f:
    header = next(f).rstrip("\n").split("\t")
    for line in f:
        rows.append(dict(zip(header, line.rstrip("\n").split("\t"))))
variants = header[4:]
corpora = ["breakfastprovider", "kronikol-tests", "kronikol-examples"]
totals = collections.Counter(r["corpus"] for r in rows)

# word-list cases: product and brand names the identifier cannot carry (a fixed list, so a floor)
PRODUCTS = ["XUnit", "xUnit", "TUnit", "NUnit", "MSTest", "ReqNRoll", "LightBDD", "BDDfy", "GraphQL", "gRPC", "PlantUML",
            "MongoDB", "CosmosDB", "SQLite", "PostgreSQL", "MySQL", "MediatR", "CSharp", "OAuth", "IPv4", "IPv6",
            "JavaScript", "TypeScript", "GitHub", "NuGet", "ClickHouse", "DuckDB", "OpenApi", "OpenAPI", "AsyncApi",
            "WebSocket", "DynamoDB", "RabbitMQ", "ActiveMQ", "EventHub", "ServiceBus", "BigQuery", "OpenTelemetry",
            "AwesomeAssertions", "FluentAssertions", "StackExchange", "WireMock", "TestContainers", "Testcontainers"]
NUMBER_WORDS = {"than", "most", "least", "within", "with", "returns", "after", "before", "every", "under", "over",
                "first", "last", "top", "has", "take", "takes", "of", "to", "at", "by", "in", "for", "retries", "times"}


def in_c_output_kept_I(r):
    return re.search(r"(?:^| )I(?= )", r["C"]) is not None


SHAPES = [
    ("plural acronym split before its s (URLs, IDs)",
     lambda r: re.search(r"(?<![A-Z])[A-Z]{2,}s(?![a-z])", r["method"]) and not re.search(r"(?<![A-Z])[A-Z]+[IA]s(?![a-z])", r["method"])),
    ("plural acronym ending Is/As (APIs, SLAs) left for the list",
     lambda r: re.search(r"(?<![A-Z])[A-Z]+[IA]s(?=[A-Z]|_|$|[0-9])", r["method"]) and re.search(r"(?<![A-Z])[A-Z]{1,}[IA]s", r["method"]) and False),
    ("name entirely in capitals, kept in capitals",
     lambda r: not re.search(r"[a-z]", r["method"]) and len(re.findall(r"[A-Z]+", r["method"])) > 1),
    ("interface name: its I kept as a word (I tracking component)",
     lambda r: re.search(r"(?:^|_|[a-z0-9])I[A-Z][a-z]", r["method"]) and not re.search(r"IPv[46]", r["method"]) and in_c_output_kept_I(r)),
    ("IPv4/IPv6 read as I pv4 / I pv6",
     lambda r: re.search(r"IPv[46]", r["method"])),
    ("camelCase identifier split and lower-cased (stable id, g RPC, x unit)",
     lambda r: re.search(r"(?:^|(?<=_))[a-z][A-Za-z0-9]*[A-Z]", r["method"])),
    ("lone capital before a capitalised word, not A or I (X unit, T unit, Req n roll)",
     lambda r: re.search(r"(?<![A-Z])[B-HJ-Z](?=[A-Z][a-z])", r["method"])),
    ("a lone capital variable lowered (first n scenarios)",
     lambda r: re.search(r"(?:^|(?<=_))[B-HJ-Z](?=_|$)", r["method"]) or re.search(r"(?<=[a-z])[B-HJ-Z](?=[A-Z][a-z]|_|$)", r["method"]) and False),
    ("product name from a fixed list split or lowered (word-list case)",
     lambda r: any(p in r["method"] for p in PRODUCTS)),
    ("acronym fragment kept in capitals (BD dfy, SQ lite)",
     lambda r: re.search(r"(?:^| )(BD dfy|SQ lite|UR ls|GUI ds|UUI dv|AP is)\b", r["C"], re.I) is not None),
    ("digit between capitals split (F6 F6 F6, E2 e)",
     lambda r: re.search(r"[A-Z][0-9]+[A-Z](?![a-z])", r["method"])),
    ("number glued to the word before it (more than10, with100)",
     lambda r: any(re.fullmatch(r"([A-Za-z]+?)([0-9]+)", w) and re.fullmatch(r"([A-Za-z]+?)([0-9]+)", w).group(1).lower() in NUMBER_WORDS
                   for w in r["C"].split(" "))),
    ("article A glued to a number (is A500 with)",
     lambda r: re.search(r"(?:(?<=[a-z])|(?<=_)|^)A[0-9]", r["method"])),
    ("possessive written _s_ (the scenario s tags)",
     lambda r: re.search(r"(?<=[A-Za-z])_s(?=_|$)", r["method"])),
]

print("## Shapes under candidate C: rows per corpus (tests), and real examples")
for name, test in SHAPES:
    hits = [r for r in rows if test(r)]
    if name.startswith("plural acronym ending") or name.startswith("a lone capital variable"):
        # recomputed below with simpler detectors
        pass
    counts = collections.Counter(r["corpus"] for r in hits)
    print(f"\n### {name}: " + ", ".join(f"{c} {counts[c]}" for c in corpora))
    seen = set()
    for r in hits:
        if r["method"] in seen:
            continue
        seen.add(r["method"])
        print(f"   {r['method']}  =>  {r['C']}")
        if len(seen) == 10:
            break

# simpler detectors for two shapes whose lambdas above are placeholders
print("\n### plural acronym ending Is/As (APIs, SLAs, left for the word list):",
      collections.Counter(r["corpus"] for r in rows if re.search(r"(?<![A-Z])[A-Z]+[IA]s(?=[A-Z]|_|$|[0-9])", r["method"])
                          and not re.search(r"(?<![A-Z])[A-Z][IA]s", r["method"])))
lone_var = [r for r in rows if re.search(r"(?:^|(?<=_))[B-HJ-Z](?=_|$)", r["method"])]
print("### a lone capital variable lowered (first n scenarios):", collections.Counter(r["corpus"] for r in lone_var),
      [f"{r['method']} => {r['C']}" for r in lone_var][:10])

print("\n## Variants: rows that differ from candidate C (cost beyond C) and from 5.12.0, per corpus")
c_changed = [r for r in rows if r["C"] != r["today"]]
for v in variants:
    beyond_c = collections.Counter(r["corpus"] for r in rows if r[v] != r["C"])
    vs_today = collections.Counter(r["corpus"] for r in rows if r[v] != r["today"])
    undone = [r for r in c_changed if r[v] != r["C"]]
    print(f"{v:10} beyond C: " + ", ".join(f"{c} {beyond_c[c]}" for c in corpora) +
          " | vs 5.12.0: " + ", ".join(f"{c} {vs_today[c]}" for c in corpora) +
          f" | of C's {len(c_changed)} changed rows, {len(undone)} read differently")

for v in variants:
    if v in ("NONE",):
        continue
    diff = [r for r in rows if r[v] != r["C"]]
    print(f"\n### {v}: up to 12 distinct rows where it differs from C")
    seen = set()
    for r in diff:
        if r["method"] in seen:
            continue
        seen.add(r["method"])
        print(f"   {r['method']}\n      C   {r['C']}\n      {v:3} {r[v]}")
        if len(seen) == 12:
            break

# possessive: which words take the s
before = collections.Counter()
for r in rows:
    for m in re.finditer(r"([A-Za-z]+)_s(?=_|$)", r["method"]):
        before[m.group(1).lower()] += 1
print("\n## words before _s_:", before.most_common(40))
print("totals", dict(totals))
