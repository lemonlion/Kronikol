"""Aggregate party facts from a decoded Kronikol report (kronikol decode output).

Prints, per report: every caller>service edge with its call count, the caller/service categories,
the URI endpoints (scheme://host:port) behind each service name, capturedBy and attributionSource
counts. Never prints bodies or headers.

usage: python -I parties.py <decoded.json> [...]
"""
import json
import sys
from collections import Counter, defaultdict
from urllib.parse import urlsplit


def interactions(report):
    for feature in report.get("features", []):
        for scenario in feature.get("scenarios", []):
            for interaction in scenario.get("httpInteractions", []) or []:
                yield scenario, interaction
    background = report.get("background") or {}
    # The background list is named interactions, unlike a scenario's (the schema's background.required).
    for interaction in background.get("interactions", []) or []:
        yield None, interaction


def endpoint(uri):
    try:
        parts = urlsplit(uri)
    except ValueError:
        return "?"
    port = parts.port
    host = parts.hostname or ""
    return f"{parts.scheme}://{host}" + (f":{port}" if port else "")


def main(paths):
    for path in paths:
        with open(path, encoding="utf-8") as handle:
            report = json.load(handle)
        edges = Counter()
        edge_categories = defaultdict(Counter)
        service_endpoints = defaultdict(Counter)
        caller_names = Counter()
        service_names = Counter()
        captured_by = Counter()
        attribution = defaultdict(Counter)
        phases = defaultdict(Counter)
        for _, interaction in interactions(report):
            if interaction.get("type") != "Request":
                continue
            caller = interaction.get("callerName")
            service = interaction.get("serviceName")
            edges[(caller, service)] += 1
            edge_categories[(caller, service)][(interaction.get("callerDependencyCategory"), interaction.get("dependencyCategory"))] += 1
            service_endpoints[service][endpoint(interaction.get("uri") or "")] += 1
            caller_names[caller] += 1
            service_names[service] += 1
            captured_by[interaction.get("capturedBy")] += 1
            attribution[(caller, service)][interaction.get("attributionSource")] += 1
            phases[(caller, service)][interaction.get("phase")] += 1

        print(f"=== {path.split('/')[-1].split(chr(92))[-1]}  suite={report.get('suite')}  calls={sum(edges.values())}")
        print(f"callers: {dict(caller_names)}")
        only_callers = sorted(set(caller_names) - set(service_names))
        print(f"names that only ever call (never called): {only_callers}")
        print("edges (caller > service : calls | callerCategory/serviceCategory | attributionSource | phase):")
        for (caller, service), count in sorted(edges.items(), key=lambda kv: (-kv[1], str(kv[0]))):
            categories = ", ".join(f"{a}/{b}" for (a, b), _ in edge_categories[(caller, service)].most_common())
            sources = ", ".join(f"{k}x{v}" for k, v in attribution[(caller, service)].most_common())
            phase = ", ".join(f"{k}x{v}" for k, v in phases[(caller, service)].most_common())
            print(f"  {caller} > {service} : {count} | {categories} | {sources} | {phase}")
        print("endpoints behind each service name:")
        for service, endpoints in sorted(service_endpoints.items(), key=lambda kv: str(kv[0])):
            listed = ", ".join(f"{k} x{v}" for k, v in endpoints.most_common(6))
            more = f" (+{len(endpoints) - 6} more)" if len(endpoints) > 6 else ""
            print(f"  {service}: {listed}{more}")
        print(f"capturedBy: {dict(captured_by)}")
        print()


if __name__ == "__main__":
    main(sys.argv[1:])
