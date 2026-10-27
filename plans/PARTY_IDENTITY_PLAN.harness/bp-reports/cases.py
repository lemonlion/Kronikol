"""infer.py's rule on report shapes BreakfastProvider does not have (PARTY_IDENTITY_PLAN.md §5.4).

Each case is a list of (caller, service, calls, callerCategory, serviceCategory, attributionSource) edges, turned into
a decoded-report dict and passed to infer.infer. Prints what the rule names and what it should.

usage: python -I cases.py   (from this folder)
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from infer import infer  # noqa: E402


def report(edges):
    interactions = []
    for caller, service, count, caller_category, service_category, source in edges:
        for _ in range(count):
            interactions.append({"type": "Request", "callerName": caller, "serviceName": service,
                                 "callerDependencyCategory": caller_category, "dependencyCategory": service_category,
                                 "attributionSource": source})
    return {"features": [{"scenarios": [{"httpInteractions": interactions}]}]}


CASES = [
    ("Jest recipe: Fastify inbound, MSW outbound", "Test", ["Service"], [
        ("Test", "Service", 40, None, None, None),
        ("Service", "api.psp.example", 25, None, None, None),
        ("Service", "risk.example", 12, None, None, None)]),
    ("in-process subject: the test calls the code, which calls out", "(none: Test is the subject)", [], [
        ("Test", "Pricing Engine", 30, None, None, None),
        ("Test", "Postgres", 50, None, "PostgreSQL", None)]),
    ("an ingested UI test: a user acts on the web app, which calls the API", "User", ["web"], [
        ("User", "web", 20, "User", None, None),
        ("web", "Orders API", 35, None, None, None),
        ("Orders API", "Postgres", 60, None, "PostgreSQL", None)]),
    ("two services under test, both driven by the test", "Caller", ["Gateway", "Orders API"], [
        ("Caller", "Gateway", 60, None, None, "TestContext"),
        ("Caller", "Orders API", 40, None, None, "TestContext"),
        ("Gateway", "Orders API", 30, None, None, "RequestHeader"),
        ("Orders API", "Postgres", 80, None, "PostgreSQL", "RequestHeader")]),
    ("the service's trackers left at the default caller name", "Caller", ["Breakfast Provider"], [
        ("Caller", "Breakfast Provider", 100, None, None, "TestContext"),
        ("Caller", "CosmosDB", 80, None, "CosmosDB", "RequestHeader"),
        ("Caller", "Cow Service", 30, None, None, "RequestHeader")]),
    ("the test seeds a store through a client of its own", "Caller", ["Orders API"], [
        ("Caller", "Orders API", 50, None, None, "TestContext"),
        ("Caller", "Postgres", 200, None, "PostgreSQL", "TestContext"),
        ("Orders API", "Postgres", 90, None, "PostgreSQL", "RequestHeader")]),
    ("the test calls only stores", "Caller", [], [
        ("Caller", "Postgres", 20, None, "PostgreSQL", "TestContext")]),
    ("a consumer test: only a broker calls the service", "(none)", [], [
        ("Kafka Broker", "Orders Worker", 15, "MessageQueue", None, "Scope"),
        ("Orders Worker", "Postgres", 15, None, "PostgreSQL", "Scope")]),
    ("two test clients with different caller names", "Admin Client, Caller", ["Orders API"], [
        ("Caller", "Orders API", 70, None, None, "TestContext"),
        ("Admin Client", "Orders API", 20, None, None, "TestContext"),
        ("Orders API", "Postgres", 90, None, "PostgreSQL", "RequestHeader")]),
    ("a scheduler, not the test, also calls the service", "Caller", ["Orders API"], [
        ("Caller", "Orders API", 70, None, None, "TestContext"),
        ("Scheduler", "Orders API", 30, None, None, "Scope"),
        ("Orders API", "Postgres", 90, None, "PostgreSQL", "RequestHeader")]),
]

for label, want_test, want_subjects, edges in CASES:
    tests, subjects, _ = infer(report(edges))
    got = ", ".join(tests) or "(none)"
    ok = got == want_test.split(":")[0].strip() and subjects == want_subjects
    print(f"{'ok  ' if ok else 'MISS'} {label}: test={got!r} subject={subjects!r}; wanted test={want_test!r} subject={want_subjects!r}")
