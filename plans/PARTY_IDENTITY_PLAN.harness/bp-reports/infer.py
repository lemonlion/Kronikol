"""The read-time inference PARTY_IDENTITY_PLAN.md §5.4 proposes, run on decoded reports.

Test parties: names that are never called, whose calls carry no caller category other than User (a broker's
delivery carries MessageQueue). Where the calls record how they got their scenario, a test party's calls are mostly
TestContext (a scheduler's are Scope or None); where nothing records it (an ingest), every such name qualifies.
Subject: among the test parties' callees, those that are not a store (database, cache, queue, storage or AI
category), preferring one that makes calls of its own, then the one with the most calls; a second subject only when
it, too, makes calls and takes at least a quarter of the test parties' calls.

usage: python -I infer.py <decoded.json> [...]
"""
import json
import sys
from collections import Counter, defaultdict

# The categories DependencyPalette.CategoryToType resolves to Database, Cache, MessageQueue, Storage or AI
# (DependencyPalette.cs:13-41), and DependencyCategories.IsStatementShaped's, which adds AtlasDataApi
# (DependencyCategories.cs:99-103). Both compare ignoring case. A category neither knows is not a store.
STORES = {name.lower() for name in (
    "CosmosDB", "SQL", "BigQuery", "MongoDB", "DynamoDB", "Elasticsearch", "Spanner", "Bigtable", "Database",
    "PostgreSQL", "SqlServer", "MySQL", "SQLite", "Oracle", "ClickHouse",
    "Redis",
    "ServiceBus", "MessageQueue",
    "BlobStorage", "S3", "CloudStorage",
    "AI",
    "AtlasDataApi")}


def requests(report):
    for feature in report.get("features", []):
        for scenario in feature.get("scenarios", []):
            for interaction in scenario.get("httpInteractions", []) or []:
                if interaction.get("type") == "Request":
                    yield interaction
    # Calls no scenario owns: the background list, named interactions (the schema's background.required).
    for interaction in (report.get("background") or {}).get("interactions", []) or []:
        if interaction.get("type") == "Request":
            yield interaction


def infer(report):
    """Returns (test parties, subjects, note). Several test parties when several pure callers qualify."""
    calls = list(requests(report))
    called = {c.get("serviceName") for c in calls}
    by_caller = defaultdict(list)
    for c in calls:
        by_caller[c.get("callerName")].append(c)

    # A test party: never called, and no caller category but User (a broker's delivery carries MessageQueue).
    candidates = []
    for caller, made in by_caller.items():
        if caller in called:
            continue
        if {c.get("callerDependencyCategory") for c in made} - {None, "", "User"}:
            continue
        attributed = sum(1 for c in made if c.get("attributionSource"))
        test_context = sum(1 for c in made if c.get("attributionSource") == "TestContext")
        candidates.append((test_context, len(made), caller, attributed))
    candidates.sort(reverse=True)
    if not candidates:
        return [], [], "no name is a pure caller without a caller category"
    # Where calls say how they got their scenario, a test party's are mostly TestContext; a scheduler or a
    # hosted service that calls the service is Scope, Detached or None. Where nothing says (ingest), all qualify.
    if any(c[3] for c in candidates):
        tests = [c[2] for c in candidates if c[3] and c[0] * 2 > c[3]] or [candidates[0][2]]
    else:
        tests = [c[2] for c in candidates]

    made_by_tests = [c for t in tests for c in by_caller[t]]
    callees = Counter(c.get("serviceName") for c in made_by_tests)
    callers = set(by_caller)
    eligible = []
    for name, count in callees.items():
        categories = {(c.get("dependencyCategory") or "").lower() for c in made_by_tests if c.get("serviceName") == name}
        if categories & STORES:
            continue
        eligible.append((name in callers, count, name))
    eligible.sort(reverse=True)
    if not eligible:
        return sorted(tests), [], "the test parties call only stores"
    subjects = [eligible[0][2]]
    total = sum(callees.values())
    for makes_calls, count, name in eligible[1:]:
        if makes_calls and count * 4 >= total:
            subjects.append(name)
    others = [n for n in sorted({c.get("callerName") for c in calls} | called) if n not in {*tests, *subjects}]
    note = f"{len(candidates)} pure caller(s): " + ", ".join(f"{c[2]} (TestContext {c[0]}/{c[1]})" for c in candidates)
    return sorted(tests), subjects, note + f"; unknown: {others}"


for path in sys.argv[1:]:
    with open(path, encoding="utf-8") as handle:
        report = json.load(handle)
    test, subjects, note = infer(report)
    print(f"{path.replace(chr(92), '/').split('/')[-1]}: test={test!r} subject={subjects!r}\n    {note}")
