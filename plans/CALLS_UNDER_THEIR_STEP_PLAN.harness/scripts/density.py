"""Counts steps against sub-steps in a report's data file, to size a sub-step bar and how many calls could name a sub-step."""
import json
import pathlib
import statistics
import sys


def walk(steps):
    for step in steps or []:
        yield step
        yield from walk(step.get("subSteps") or step.get("steps") or [])


for path in sys.argv[1:]:
    with open(path, encoding="utf-8") as f:
        report = json.load(f)
    scenarios = [s for feature in report.get("features", []) for s in feature.get("scenarios", [])]
    tops = subs = asserts = 0
    per_scenario = []
    calls_total = calls_under_composite = 0
    for scenario in scenarios:
        top_steps = (scenario.get("backgroundSteps") or []) + (scenario.get("steps") or [])
        top = len(top_steps)
        sub = asserted = 0
        composite = set()
        def key(i, s):
            return i
        for index, step in enumerate(scenario.get("backgroundSteps") or []):
            nested = list(walk(step.get("subSteps") or []))
            if any(not n.get("assertion") for n in nested):
                composite.add(f"b{index}")
            sub += sum(1 for n in nested if not n.get("assertion"))
            asserted += sum(1 for n in nested if n.get("assertion"))
        for index, step in enumerate(scenario.get("steps") or []):
            nested = list(walk(step.get("subSteps") or []))
            if any(not n.get("assertion") for n in nested):
                composite.add(f"{index}")
            sub += sum(1 for n in nested if not n.get("assertion"))
            asserted += sum(1 for n in nested if n.get("assertion"))
        tops += top
        subs += sub
        asserts += asserted
        per_scenario.append((top, sub))
        for call in scenario.get("httpInteractions") or []:
            if call.get("type") != "Request":
                continue
            calls_total += 1
            if (call.get("stepPath") or "") in composite:
                calls_under_composite += 1
    with_sub = [p for p in per_scenario if p[1] > 0]
    print(f"{pathlib.Path(path).parent.name}/{pathlib.Path(path).name}: {len(scenarios)} scenarios, {tops} top-level steps, "
          f"{subs} sub-steps, {asserts} assertions")
    print(f"  scenarios with a sub-step: {len(with_sub)}; bars per scenario now, median {statistics.median(p[0] for p in per_scenario) if per_scenario else 0}, max {max((p[0] for p in per_scenario), default=0)}; "
          f"with a bar per sub-step, median {statistics.median(p[0] + p[1] for p in per_scenario) if per_scenario else 0}, max {max((p[0] + p[1] for p in per_scenario), default=0)}")
    print(f"  calls {calls_total}, of which under a top-level step that has sub-steps {calls_under_composite}")
