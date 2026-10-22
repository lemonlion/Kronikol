#!/usr/bin/env python3
"""Fallback for `kronikol query` when the dotnet tool is not installed.

Implements six of the tool's commands — summary, failures, steps, services, grep and http — against the
same addressing, so a machine without the tool degrades to a smaller set of answers rather than to
reading a 10 MB file.

It is a fallback in one important respect: it parses the report with `json.load`, so it holds the document
in the *process*. A report whose payloads were written compressed (`formatVersion` 2) is inflated as it is
loaded, so every command reads one form. That is fine — the process is not the context window, and the point of this script is
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

import base64
import gzip
import hashlib
import json
import os
import re
import sys

BUDGET = 6000


# The most a report may decode to, as the tool's: a small file from a fork's CI that decodes to more is refused.
CAP = 4 << 30
FORMATS = ("a report is JSON (starting with {, after a byte order mark or none), or that JSON compressed with zstd "
           "(Kronikol's TestRunReport.krkl, or a bare zstd frame) or gzip")


def load(path):
    path = find(path)
    data = read_report(path)
    try:
        return path, inflate(json.loads(data.decode("utf-8")))
    except (UnicodeDecodeError, ValueError) as error:
        die(f"{path} is not valid JSON: {error}")


def find(path):
    """The report a path names: a file, or the one report in a folder, as the tool finds it. TestRunReport.krkl or
    TestRunReport.json at the top, and of both the one of_both takes, else the one report below; runs/ holds
    earlier runs of the report above it, and baseline/ the copy --baseline compares against, so neither is a second
    report."""
    if not os.path.exists(path):
        die(f"No such file or directory: {path}")
    if not os.path.isdir(path):
        return path
    top = [os.path.join(path, name) for name in ("TestRunReport.krkl", "TestRunReport.json")
           if os.path.exists(os.path.join(path, name))]
    if top:
        return of_both(top)
    reports = {}
    for root, folders, names in os.walk(path):
        folders[:] = [f for f in folders if f.lower() not in ("runs", "baseline")]
        for name in names:
            if name.lower().endswith(".krkl") or name.endswith("TestRunReport.json"):
                stem = os.path.join(root, os.path.splitext(name)[0]).lower()
                reports.setdefault(stem, []).append(os.path.join(root, name))
    matches = [of_both(pair) for pair in reports.values()]
    if len(matches) != 1:
        die(f"{len(matches)} reports under {path} — name the one you mean")
    return matches[0]


def of_both(files):
    """Of a report written both ways, as the tool takes it: the one the folder's Run.json lists when it lists one and
    not the other, since a copy or a download of the artifacts scrambles the times; else the newer."""
    if len(files) == 2:
        listed = set()
        try:
            with open(os.path.join(os.path.dirname(files[0]), "Run.json"), encoding="utf-8") as handle:
                manifest = json.load(handle)
            names = manifest.get("files") if isinstance(manifest, dict) and manifest.get("run") and manifest.get("at") else None
            if isinstance(names, list) and all(isinstance(name, str) for name in names):
                listed = {name.lower() for name in names}
        except (OSError, ValueError):
            pass
        named = [file for file in files if os.path.basename(file).lower() in listed]
        if len(named) == 1:
            return named[0]
    return max(files, key=os.path.getmtime)


def read_report(path):
    """The JSON a report file holds, told apart by its first bytes as the tool's reader does."""
    with open(path, "rb") as handle:
        data = handle.read()
    head = data[:24]
    if len(head) >= 12 and head[:4] == b"\x50\x2a\x4d\x18" and head[8:12] == b"KRKL":
        # Kronikol's marker: a skippable frame naming the file, its container version and the decoded length.
        if len(head) < 24 or int.from_bytes(head[4:8], "little") != 16:
            die(f"{path}: it is damaged or truncated: Kronikol's marker at its start is not the 24 bytes it should be")
        if head[12] > 1:
            die(f"{path}: it was written by a newer Kronikol (container version {head[12]}; this script reads "
                "version 1). Upgrade Kronikol.Tool.")
        declared = int.from_bytes(head[16:24], "little", signed=True)
        if declared < 0:
            die(f"{path}: it is damaged or truncated: Kronikol's marker gives a negative length")
        if declared > CAP:
            die(f"{path}: it says it decodes to {declared:,} bytes, more than {CAP:,} bytes (4 GiB), the most a "
                "report may decode to")
        return zstd_report(path, data[24:], declared)
    magic = int.from_bytes(head[:4], "little") if len(head) >= 4 else None
    if magic is not None and (magic & 0xFFFFFFF0 == 0x184D2A50 or magic == 0xFD2FB528):
        return zstd_report(path, data, None)
    if head[:2] == b"\x1f\x8b":
        return gzip_report(path, data)
    json_text = data[3:] if data[:3] == b"\xef\xbb\xbf" else data
    start = json_text.lstrip(b" \t\r\n")
    if not start or start[:1] == b"{":
        return json_text
    die(f"{path}: it is not a report Kronikol reads: {FORMATS}")


def zstd_report(path, data, declared):
    try:
        from compression import zstd
    except ImportError:
        die(f"{path} is compressed with zstd, which Python reads from 3.14 on (this is {sys.version.split()[0]}). "
            "Use the tool: kronikol query, or dotnet run --file query.cs beside the report.")
    out = bytearray()
    try:
        # Frame after frame, as a zstd decoder reads them, skippable ones stepped over; never past the cap.
        while data:
            decoder = zstd.ZstdDecompressor()
            out += decoder.decompress(data, max_length=CAP + 1 - len(out))
            while not decoder.eof and len(out) <= CAP:
                if decoder.needs_input:
                    die(f"{path}: it is damaged or truncated: its zstd frame ends early")
                out += decoder.decompress(b"", max_length=CAP + 1 - len(out))
            if len(out) > CAP:
                die(f"{path}: it decodes to more than {CAP:,} bytes (4 GiB), the most a report may decode to")
            data = decoder.unused_data
    except zstd.ZstdError as error:
        die(f"{path}: it is damaged or truncated: its zstd frame does not decode ({error})")
    if declared is not None and len(out) != declared:
        die(f"{path}: it is damaged or truncated: it decodes to {len(out):,} bytes, where Kronikol's marker says "
            f"{declared:,}")
    return bytes(out)


def gzip_report(path, data):
    import zlib
    if len(data) < 18:
        die(f"{path}: it is damaged or truncated: it is too short to be a gzip stream")
    out = bytearray()
    try:
        decoder = zlib.decompressobj(wbits=31)
        out += decoder.decompress(data, CAP + 1 - len(out))
        while not decoder.eof and len(out) <= CAP:
            if not decoder.unconsumed_tail:
                die(f"{path}: it is damaged or truncated: its gzip stream ends before its trailer")
            out += decoder.decompress(decoder.unconsumed_tail, CAP + 1 - len(out))
        if len(out) > CAP:
            die(f"{path}: it decodes to more than {CAP:,} bytes (4 GiB), the most a report may decode to")
    except zlib.error as error:
        die(f"{path}: it is damaged or truncated: its gzip stream does not decode ({error})")
    return bytes(out)


def inflate(report):
    """Payloads written compressed (CompressTestRunReportPayloads, formatVersion 2): {"$h", "$n", "$z"}, where
    $z is the base64 of the gzip of the text's UTF-8, back to the text."""

    def text(value):
        if isinstance(value, dict) and isinstance(value.get("$z"), str):
            return gzip.decompress(base64.b64decode(value["$z"])).decode("utf-8")
        return value

    def interactions(holder):
        for interaction in holder.get("httpInteractions") or holder.get("interactions") or []:
            if isinstance(interaction, dict) and "content" in interaction:
                interaction["content"] = text(interaction["content"])

    for feature in report.get("features") or []:
        for scenario in feature.get("scenarios") or []:
            if isinstance(scenario.get("diagrams"), list):
                scenario["diagrams"] = [text(d) for d in scenario["diagrams"]]
            interactions(scenario)
    if isinstance(report.get("background"), dict):
        interactions(report["background"])
    return report


def scenarios(report):
    """Flat scenario list in the order the tool numbers them: s0, s1, ..."""
    found = []
    for feature in report.get("features", []):
        for scenario in feature.get("scenarios", []):
            scenario["_feature"] = feature.get("name", "")
            scenario["_ordinal"] = len(found)
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
    """The scenario an address names, and the step path it narrows to.

    The script advertises "the same addressing" as the tool, and for a while it was not: `s0/99` was
    parsed for its scenario ordinal and its step path was thrown away, so a nonsense path answered with
    the whole scenario — the same defect the tool carried, reimplemented independently. `sabc` was worse:
    `int("abc")` raised an uncaught ValueError and the user got a Python traceback where the tool says
    `Not an address: sabc` and exits 2.
    """
    found = scenarios(report)

    if address.startswith("sid:"):
        wanted = address[4:].strip().lower()
        matches = [s for s in found if (s.get("stableId") or "").lower() == wanted]
        if not matches:
            die(f"No scenario with stableId {wanted} in this report")
        if len(matches) > 1:
            names = ", ".join(f"s{s['_ordinal']}" for s in matches)
            die(f"{len(matches)} scenarios share stableId {wanted} (a repeated row, or a retry) — "
                f"name the one you mean: {names}")
        return matches[0], None

    parts = address.split("/")
    if not parts[0].startswith("s") or not parts[0][1:].isdigit():
        die(f"Not an address: {address}")

    index = int(parts[0][1:])
    if index >= len(found):
        die(f"No scenario s{index} — the report has {len(found)}")
    scenario = found[index]

    step_path = None
    if len(parts) > 1 and not parts[1].startswith("i") and not parts[1].startswith("d"):
        step_path = parts[1]
        if not any(covered_by(path, step_path) for path, _, _ in walk_steps(scenario)):
            die(f"No step {step_path} in s{index} — `steps <report> s{index}` lists its paths")

    return scenario, step_path


def covered_by(path, scope):
    """A step path in an address covers that step and everything under it — the tool's rule, segment-wise
    so `1` does not swallow `10`."""
    return path == scope or path.startswith(scope + ".")


# ─── Commands ──────────────────────────────────────────────────


def cmd_summary(path, report, _args):
    found = scenarios(report)
    # A scenario that is not a test (an ingest's fold, a stack's start-up) is counted as none, as the tool counts it;
    # its calls are the stack's traffic, and stay in the count of interactions.
    tests = [s for s in found if not s.get("notATest")]
    not_a_test = len(found) - len(tests)
    failed = [s for s in tests if s.get("result") == "Failed"]
    calls = sum(len(s.get("httpInteractions") or []) for s in found)

    lines = [
        f"{os.path.basename(path)}  {os.path.getsize(path) // 1024} KB",
        f"{report.get('startTime')} → {report.get('endTime')}",
        f"{len(tests)} scenarios" + (f" · {not_a_test} not a test" if not_a_test else "") + f" · {len(failed)} failed · {calls} interactions",
        "",
    ]

    for feature in report.get("features", []):
        rows = feature.get("scenarios", [])
        feature_tests = [s for s in rows if not s.get("notATest")]
        others = len(rows) - len(feature_tests)
        broken = sum(1 for s in feature_tests if s.get("result") == "Failed")
        if not feature_tests and others:
            # a feature of no test (a stack's start-up) says only that, as the tool says it
            lines.append(f"{feature.get('name')}  {others} not a test")
        else:
            lines.append(f"{feature.get('name')}  {len(feature_tests) - broken} passed" + (f", {broken} FAILED" if broken else "")
                         + (f", {others} not a test" if others else ""))

    if failed:
        lines += ["", "Failed:"]
        for scenario in failed[:10]:
            lines.append(f"  s{found.index(scenario)}  {one_line(scenario.get('name'), 70)}")

    emit(lines, "next: failures" if failed else "next: steps sN")


def cmd_failures(_path, report, _args):
    found = scenarios(report)
    failed = [(i, s) for i, s in enumerate(found) if s.get("result") == "Failed" and not s.get("notATest")]

    if not failed:
        tests = sum(1 for s in found if not s.get("notATest"))
        not_a_test = len(found) - tests
        emit(["nothing failed"], f"{tests} scenarios, all passed" + (f" · {not_a_test} not a test" if not_a_test else ""))
        return

    lines = []
    for index, scenario in failed:
        lines.append(f"s{index}  {scenario['_feature']} › {scenario.get('name')}")
        if scenario.get("errorMessage"):
            lines.append("  " + one_line(scenario["errorMessage"], 200))
        # A category, never the message. The framework's own name for what went wrong (3.1.0) - shown
        # after the message so it cannot be read as the message, and never grouped on.
        if scenario.get("failureCause"):
            lines.append("  cause: " + one_line(scenario["failureCause"], 120))
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
    scenario, step_scope = resolve_scenario(report, args[0])
    address = "s" + str(scenario["_ordinal"])

    by_step = {}
    for i, interaction in enumerate(scenario.get("httpInteractions") or []):
        if interaction.get("type") != "Request":
            continue
        by_step.setdefault(interaction.get("stepPath"), []).append(i)

    lines = [f"{address}  {scenario['_feature']} › {scenario.get('name')}  [{scenario.get('result')}]",
             f"stableId {scenario.get('stableId')}"]
    if scenario.get("stableId"):
        lines.append(f"open: TestRunReport.html#sid-{scenario['stableId']}")
    lines.append("")

    rows = [r for r in walk_steps(scenario) if step_scope is None or covered_by(r[0], step_scope)]
    base = min((r[1] for r in rows), default=0) if step_scope else 0
    if step_scope:
        lines.append(f"scoped to step {step_scope} and its sub-steps — `steps <report> {address}` for the whole scenario")
        lines.append("")

    for path, raw_depth, step in rows:
        depth = raw_depth - base
        mark = "✗" if step.get("status") == "Failed" else " "
        keyword = (step.get("keyword") or "").strip()
        calls = by_step.get(path, [])
        span = f"  [i{calls[0]}-i{calls[-1]}] {len(calls)} calls" if len(calls) > 1 else (f"  [i{calls[0]}]" if calls else "")
        lines.append(f"{'  ' * depth}{mark} {path:<5} {one_line((keyword + ' ' + step.get('text', '')).strip(), 90)}{span}")
        if step.get("failureMessage"):
            lines.append(f"{'  ' * depth}      {one_line(step['failureMessage'], 180)}")

    emit(lines, f"{sum(len(v) for v in by_step.values())} calls")


def cmd_services(_path, report, args):
    scope = [resolve_scenario(report, args[0])[0]] if args else scenarios(report)

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


# The line a capture writes after a complete document it capped: "... (N more documents not shown)", "... (N more rows
# not shown)" or "... (N more)". The report and the .NET tool read the document before it; so does this script.
FOOTNOTE = re.compile(r"\r?\n\.\.\. \(\d+ more(?: (?:rows|documents) not shown)?\)\s*$")
NOT_JSON = object()


def body_json(content):
    """The body as JSON and the capture footnote that ends it (None when none does); NOT_JSON when it is not JSON."""
    try:
        return json.loads(content), None
    except json.JSONDecodeError:
        pass
    match = FOOTNOTE.search(content)
    if match and content[:match.start()].lstrip()[:1] in ("{", "["):
        try:
            return json.loads(content[:match.start()]), match.group(0).strip()
        except json.JSONDecodeError:
            pass
    return NOT_JSON, None


def paths_containing(content, needle):
    document, _ = body_json(content)
    if document is NOT_JSON:
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

    scenario, _ = resolve_scenario(report, address)
    ordinal = address.split("/i")[1]
    if not ordinal.isdigit():
        die(f"Not an interaction address: {address}")
    index = int(ordinal)
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
    # Why the call failed, in the service's own words, when its capturer read them (a MongoDB errmsg).
    if interaction.get("error"):
        lines.append(f"error {one_line(interaction['error'], 200)}")

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
    document, footnote = body_json(content)
    if document is NOT_JSON:
        return content
    text = json.dumps(document, indent=2)
    return f"{text}\n{footnote}" if footnote else text


def keys(content, max_depth=3):
    document, _ = body_json(content)
    if document is NOT_JSON:
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
    node, _ = body_json(content)
    if node is NOT_JSON:
        return f"(not JSON — {size(content)} of text)"
    try:
        for part in path.lstrip("$.").split("."):
            while "[" in part:
                name, rest = part.split("[", 1)
                if name:
                    node = node[name]
                index, part = rest.split("]", 1)
                node = node[int(index)]
            if part:
                node = node[part]
    except (KeyError, IndexError, TypeError, ValueError):
        return f"(nothing at {path})"
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
    # Piped on Windows, Python writes the console's code page, which has no arrows: every `http` line crashed.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass

    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)

    path, report = load(sys.argv[2])
    COMMANDS[sys.argv[1]](path, report, sys.argv[3:])


if __name__ == "__main__":
    main()
