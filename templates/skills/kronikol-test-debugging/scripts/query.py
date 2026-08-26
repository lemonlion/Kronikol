#!/usr/bin/env python3
"""Fallback for `kronikol query` when the dotnet tool is not installed.

Implements the four commands that matter most — summary, failures, steps, grep, http — against the same
addressing, so a machine without the tool degrades to a smaller set of answers rather than to reading a
10 MB file.

It is a fallback in one important respect: it parses the report with `json.load`, so it holds the document
in the *process*. That is fine — the process is not the context window, and the point of this script is
that the report never reaches the model. What it cannot do is a report larger than available RAM; for
those, install the tool:

    dotnet tool install -g Kronikol.Tool

Usage:
    python query.py summary   <report>
    python query.py failures  <report>
    python query.py steps     <report> s3
    python query.py services  <report> [s3]
    python query.py grep      <report> "4173" [--values]
    python query.py http      <report> s3/i47 [--keys] [--path $.a.b] [--out FILE]

Only in the real tool (not missing from the report — missing from this fallback):
    values (aggregation), interactions, flow, assertions, annotations, body, note,
    diagram, compare, diff, and the extended --path grammar ([*], ['a.b'], .length()).
"""

import hashlib
import json
import os
import sys

BUDGET = 6000


def load(path):
    if os.path.isdir(path):
        candidate = os.path.join(path, "TestRunReport.json")
        if not os.path.exists(candidate):
            matches = [
                os.path.join(root, name)
                for root, _, names in os.walk(path)
                for name in names
                if name.endswith("TestRunReport.json")
            ]
            if len(matches) != 1:
                die(f"{len(matches)} reports under {path} — name the one you mean")
            candidate = matches[0]
        path = candidate

    with open(path, encoding="utf-8") as handle:
        return path, json.load(handle)


def scenarios(report):
    """Flat scenario list in the order the tool numbers them: s0, s1, ..."""
    found = []
    for feature in report.get("features", []):
        for scenario in feature.get("scenarios", []):
            scenario["_feature"] = feature.get("name", "")
            found.append(scenario)
    return found


def body_hash(content):
    return "b:" + hashlib.sha1(content.encode("utf-8")).hexdigest()[:8]


def size(text):
    count = len(text.encode("utf-8"))
    return f"{count} B" if count < 1024 else f"{count / 1024:.1f} KB"


def one_line(text, limit=120):
    if not text:
        return ""
    flat = " ".join(str(text).split())
    return flat if len(flat) <= limit else flat[:limit] + "…"


def walk_steps(scenario):
    """Every step and sub-step with its stepPath address, background steps first."""

    def walk(step, path, depth):
        yield path, depth, step
        for i, sub in enumerate(step.get("subSteps") or []):
            yield from walk(sub, f"{path}.{i}", depth + 1)

    for i, step in enumerate(scenario.get("backgroundSteps") or []):
        yield from walk(step, f"b{i}", 0)
    for i, step in enumerate(scenario.get("steps") or []):
        yield from walk(step, str(i), 0)


def emit(lines, footer=""):
    written = 0
    for line in lines:
        cost = len(line.encode("utf-8")) + 1
        if written + cost > BUDGET:
            print(f"… truncated at {BUDGET} bytes")
            break
        print(line)
        written += cost
    if footer:
        print(footer)


def die(message):
    print(message, file=sys.stderr)
    sys.exit(2)


def resolve_scenario(report, address):
    if not address.startswith("s"):
        die(f"Not a scenario address: {address}")
    found = scenarios(report)
    index = int(address[1:].split("/")[0])
    if index >= len(found):
        die(f"No scenario s{index} — the report has {len(found)}")
    return found[index]


# ─── Commands ──────────────────────────────────────────────────


def cmd_summary(path, report, _args):
    found = scenarios(report)
    failed = [s for s in found if s.get("result") == "Failed"]
    calls = sum(len(s.get("httpInteractions") or []) for s in found)

    lines = [
        f"{os.path.basename(path)}  {os.path.getsize(path) // 1024} KB",
        f"{report.get('startTime')} → {report.get('endTime')}",
        f"{len(found)} scenarios · {len(failed)} failed · {calls} interactions",
        "",
    ]

    for feature in report.get("features", []):
        rows = feature.get("scenarios", [])
        broken = sum(1 for s in rows if s.get("result") == "Failed")
        lines.append(f"{feature.get('name')}  {len(rows) - broken} passed" + (f", {broken} FAILED" if broken else ""))

    if failed:
        lines += ["", "Failed:"]
        for scenario in failed[:10]:
            lines.append(f"  s{found.index(scenario)}  {one_line(scenario.get('name'), 70)}")

    emit(lines, "next: failures" if failed else "next: steps sN")


def cmd_failures(_path, report, _args):
    found = scenarios(report)
    failed = [(i, s) for i, s in enumerate(found) if s.get("result") == "Failed"]

    if not failed:
        emit(["nothing failed"], f"{len(found)} scenarios, all passed")
        return

    lines = []
    for index, scenario in failed:
        lines.append(f"s{index}  {scenario['_feature']} › {scenario.get('name')}")
        if scenario.get("errorMessage"):
            lines.append("  " + one_line(scenario["errorMessage"], 200))
        for path, depth, step in walk_steps(scenario):
            if step.get("status") != "Failed":
                continue
            keyword = (step.get("keyword") or "").strip()
            lines.append(f"  {'  ' * depth}✗ s{index}/{path}  {one_line((keyword + ' ' + step.get('text', '')).strip(), 90)}")
            if step.get("failureMessage"):
                lines.append(f"  {'  ' * depth}  {one_line(step['failureMessage'], 180)}")
            if step.get("sourceFile"):
                lines.append(f"  {'  ' * depth}  at {step['sourceFile']}:{step.get('sourceLine')}")
        lines.append("")

    emit(lines, f"{len(failed)} failed")


def cmd_steps(_path, report, args):
    if not args:
        die("Which scenario? steps <report> s3")
    scenario = resolve_scenario(report, args[0])

    by_step = {}
    for i, interaction in enumerate(scenario.get("httpInteractions") or []):
        if interaction.get("type") != "Request":
            continue
        by_step.setdefault(interaction.get("stepPath"), []).append(i)

    lines = [f"{scenario['_feature']} › {scenario.get('name')}  [{scenario.get('result')}]",
             f"stableId {scenario.get('stableId')}", ""]

    for path, depth, step in walk_steps(scenario):
        mark = "✗" if step.get("status") == "Failed" else " "
        keyword = (step.get("keyword") or "").strip()
        calls = by_step.get(path, [])
        span = f"  [i{calls[0]}-i{calls[-1]}] {len(calls)} calls" if len(calls) > 1 else (f"  [i{calls[0]}]" if calls else "")
        lines.append(f"{'  ' * depth}{mark} {path:<5} {one_line((keyword + ' ' + step.get('text', '')).strip(), 90)}{span}")
        if step.get("failureMessage"):
            lines.append(f"{'  ' * depth}      {one_line(step['failureMessage'], 180)}")

    emit(lines, f"{sum(len(v) for v in by_step.values())} calls")


def cmd_services(_path, report, args):
    scope = [resolve_scenario(report, args[0])] if args else scenarios(report)

    stats = {}
    for scenario in scope:
        for interaction in scenario.get("httpInteractions") or []:
            entry = stats.setdefault(interaction.get("serviceName", ""), {"calls": 0, "errors": 0, "bytes": 0})
            if interaction.get("type") == "Request":
                entry["calls"] += 1
            entry["bytes"] += len(interaction.get("content") or "")
            status = str(interaction.get("statusCode") or "")
            if status.isdigit() and int(status) >= 400:
                entry["errors"] += 1

    if not stats:
        emit(["no services were called"], "absence is the answer — nothing was captured for this scope")
        return

    lines = [f"{'service':<24} {'calls':>5} {'errors':>6} {'bytes':>9}"]
    for name, entry in sorted(stats.items(), key=lambda kv: -kv[1]["calls"]):
        lines.append(f"{name[:24]:<24} {entry['calls']:>5} {entry['errors']:>6} {entry['bytes'] // 1024:>7} KB")

    emit(lines, f"{len(stats)} services · a service missing here was never called")


def cmd_grep(_path, report, args):
    if not args:
        die('What are you looking for? grep <report> "4173"')
    needle, values = args[0], "--values" in args
    lines, seen = [], set()

    for index, scenario in enumerate(scenarios(report)):
        for path, _, step in walk_steps(scenario):
            haystack = (step.get("text") or "") + " " + (step.get("failureMessage") or "")
            if needle.lower() in haystack.lower():
                lines.append(f"s{index}/{path:<6} step       {one_line(haystack, 110)}")

        for i, interaction in enumerate(scenario.get("httpInteractions") or []):
            if needle.lower() in (interaction.get("uri") or "").lower():
                lines.append(f"s{index}/i{i:<5} uri        {one_line(interaction.get('uri'), 110)}")

            content = interaction.get("content")
            if not content or needle.lower() not in content.lower():
                continue

            digest = body_hash(content)
            if digest in seen:
                continue
            seen.add(digest)

            if values:
                for found in paths_containing(content, needle)[:4]:
                    lines.append(f"s{index}/i{i:<5} body       {found}")
            else:
                at = content.lower().index(needle.lower())
                lines.append(f"s{index}/i{i:<5} body       {digest} {size(content)}  …{one_line(content[max(0, at - 30):at + 60], 80)}…")

    if not lines:
        emit([f'"{needle}" was not found'], "this fallback searches steps, uris and bodies")
        return

    emit(lines, f"{len(lines)} hits")


def paths_containing(content, needle):
    try:
        document = json.loads(content)
    except json.JSONDecodeError:
        return []

    found = []

    def walk(node, path):
        if isinstance(node, dict):
            for key, value in node.items():
                walk(value, f"{path}.{key}")
        elif isinstance(node, list):
            for i, value in enumerate(node):
                walk(value, f"{path}[{i}]")
        elif needle.lower() in str(node).lower():
            found.append(f"{path} = {one_line(node, 60)}")

    walk(document, "$")
    return found


def cmd_http(_path, report, args):
    if not args:
        die("Which interaction? http <report> s3/i47")

    address = args[0]
    if "/i" not in address:
        die(f"Not an interaction address: {address}")

    scenario = resolve_scenario(report, address)
    index = int(address.split("/i")[1])
    interactions = scenario.get("httpInteractions") or []
    if index >= len(interactions):
        die(f"No interaction i{index} — the scenario has {len(interactions)}")

    interaction = interactions[index]
    lines = [
        f"{address}  {interaction.get('type')}  {interaction.get('callerName')} → {interaction.get('serviceName')}",
        f"{interaction.get('method')} {interaction.get('uri')}",
    ]
    for label, key in (("status", "statusCode"), ("in step", "stepPath"), ("trace", "activityTraceId")):
        if interaction.get(key):
            lines.append(f"{label} {interaction[key]}")

    content = interaction.get("content")
    if not content:
        emit(lines + ["body: none"])
        return

    if "--out" in args:
        target = args[args.index("--out") + 1]
        with open(target, "w", encoding="utf-8") as handle:
            handle.write(pretty(content))
        emit(lines + [f"wrote {size(content)} → {os.path.abspath(target)}"], "grep the file")
        return

    if "--keys" in args:
        emit(lines + [""] + keys(content), "--path $.<one of these> for a value")
        return

    if "--path" in args:
        emit(lines + [""] + [str(pick(content, args[args.index("--path") + 1]))])
        return

    text = pretty(content)
    if len(text.encode("utf-8")) > BUDGET:
        emit(lines + [f"body: {size(content)} · {body_hash(content)}",
                      "too big to print — use --keys, --path $.a.b, or --out FILE"])
        return

    emit(lines + ["", text])


def pretty(content):
    try:
        return json.dumps(json.loads(content), indent=2)
    except json.JSONDecodeError:
        return content


def keys(content, max_depth=3):
    try:
        document = json.loads(content)
    except json.JSONDecodeError:
        return [f"(not JSON — {size(content)} of text)"]

    found = []

    def walk(node, path, depth):
        if isinstance(node, dict):
            if depth >= max_depth:
                found.append(f"{path}  object ({len(node)} keys)")
                return
            for key, value in node.items():
                walk(value, f"{path}.{key}", depth + 1)
        elif isinstance(node, list):
            found.append(f"{path}[]  array ({len(node)})")
            if node and depth < max_depth:
                walk(node[0], f"{path}[0]", depth + 1)
        else:
            found.append(f"{path}  {type(node).__name__} = {one_line(node, 60)}")

    walk(document, "$", 0)
    return found


def pick(content, path):
    node = json.loads(content)
    for part in path.lstrip("$.").split("."):
        while "[" in part:
            name, rest = part.split("[", 1)
            if name:
                node = node[name]
            index, part = rest.split("]", 1)
            node = node[int(index)]
        if part:
            node = node[part]
    return json.dumps(node, indent=2) if isinstance(node, (dict, list)) else node


COMMANDS = {
    "summary": cmd_summary,
    "failures": cmd_failures,
    "steps": cmd_steps,
    "services": cmd_services,
    "grep": cmd_grep,
    "http": cmd_http,
}


def main():
    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)

    path, report = load(sys.argv[2])
    COMMANDS[sys.argv[1]](path, report, sys.argv[3:])


if __name__ == "__main__":
    main()
