"""The audit of 2026-09-27 (plan section 7.7): `kronikol query flow` on generated scenarios, checked against
the plan's own rules.

    python audit_properties.py <kronikol.exe> <out-dir> [--scenarios N] [--seed S] [--workers W]

Generates scenarios the corpus never holds (nesting two and three deep, a party's calls answered in any
order, deliveries on an open call's trace, requests never answered or answered before they were recorded,
requests with no pairing id, user actions, calls made after the next step began, annotations anywhere),
runs the real tool on each with no filter, --service, --step and --errors-only, and checks every view:

  order     call lines in capture order, exactly the requests the filters select
  header    S1: a step header directly above a shown call of that step, and only where the step changes
  annotation S1: every annotation, in index order, above the first shown call recorded after it, else last
  depth     two spaces per shown ancestor under rule R4 (parents from flow_prototype.parents)
  tree      F15: a line names its parent unless the nearest line above it with less indentation, of any
            kind (step header, annotation, call), is that parent; an `inside` always names the R4 parent
  status    `no response` exactly on a request with a pairing id, no response anywhere, not a user action
  legend    the footer's legend exactly when a line is indented; the count matches the call lines
  trailing  no line ends in whitespace
  proto     the output equals flow_prototype.render() for the same view

Prints one line per failed check with the scenario, view and line (eight per check at most), then a count
per check. The generated report is written to <out-dir>, outside the repository.
"""
import argparse, json, os, random, re, subprocess, sys
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from flow_prototype import parents as r4_parents, real_id, render  # noqa: E402

SERVICES = ["api", "svc1", "svc2", "db", "cache"]
STATUSES = [(200, "OK"), (201, "Created"), (500, "InternalServerError"), (502, "BadGateway"),
            (503, "ServiceUnavailable"), (None, None)]


def generate(rng, number):
    steps = rng.randint(1, 3)
    step_paths = [str(k) for k in range(steps)]
    records, annotations = [], []
    current = None if rng.random() < 0.2 else "0"
    next_step = 0 if current is None else 1
    open_calls, serial = [], 0

    def new_id():
        nonlocal serial
        serial += 1
        return f"{number}-{serial}"

    def record(kind, call, status=None):
        r = {"type": kind, "method": call["method"], "uri": call["uri"], "serviceName": call["service"],
             "callerName": call["caller"], "traceId": call["trace"], "stepPath": current, "headers": []}
        if call["id"] is not None:
            r["requestResponseId"] = call["id"]
        if kind == "Request" and call.get("body"):
            r["content"] = call["body"]
        if kind == "Response" and status is not None:
            code, text = status
            if code is not None:
                r["statusCode"], r["statusText"] = code, text
            if rng.random() < 0.6:
                r["durationMs"] = rng.choice([0.4, 3, 45, 999, 1500, 12000])
        if call.get("user"):
            r["isUserAction"] = True
        records.append(r)

    for _ in range(rng.randint(2, 14)):
        roll = rng.random()
        if roll < 0.40 or not open_calls:
            handling = [c for c in open_calls if not c.get("user")]
            parent = rng.choice(handling) if handling and rng.random() < 0.75 else None
            caller = parent["service"] if parent else "test"
            service = rng.choice([s for s in SERVICES if s != caller])
            trace = parent["trace"] if parent and rng.random() < 0.3 else f"t{new_id()}"
            call = {"id": None if rng.random() < 0.05 else new_id(), "caller": caller, "service": service,
                    "method": rng.choice(["GET", "POST", "QUERY"]), "uri": f"http://{service}/r{serial}",
                    "trace": trace, "body": "{\"n\":%d}" % serial if rng.random() < 0.3 else None,
                    "never": rng.random() < 0.06}
            record("Request", call)
            open_calls.append(call)
        elif roll < 0.48:
            host = rng.choice(open_calls)
            call = {"id": new_id(), "caller": "broker", "service": rng.choice(SERVICES), "method": "CONSUME",
                    "uri": f"http://broker/topic{serial}", "trace": host["trace"], "body": None, "never": False}
            record("Request", call)
            open_calls.append(call)
        elif roll < 0.78:
            call = rng.choice(open_calls)
            open_calls.remove(call)
            if not call["never"] and not call.get("user"):
                record("Response", call, rng.choice(STATUSES))
        elif roll < 0.84 and next_step < steps:
            current = step_paths[next_step]
            next_step += 1
        elif roll < 0.90:
            annotations.append({"index": len(records), "kind": "Custom", "text": f"note {serial}"})
            serial += 1
        elif roll < 0.94:
            call = {"id": new_id(), "caller": "User", "service": "web", "method": "Click", "uri": "http://web/",
                    "trace": f"t{new_id()}", "body": None, "never": True, "user": True}
            record("Request", call)
        else:
            call = {"id": new_id(), "caller": "test", "service": rng.choice(SERVICES), "method": "GET",
                    "uri": f"http://early/r{serial}", "trace": f"t{new_id()}", "body": None, "never": False}
            record("Response", call, (200, "OK"))
            record("Request", call)

    rng.shuffle(open_calls)
    for call in open_calls:
        if not call["never"] and not call.get("user"):
            record("Response", call, rng.choice(STATUSES))
    if rng.random() < 0.15:
        annotations.append({"index": len(records), "kind": "Custom", "text": "after the last call"})

    return {"id": f"g{number}", "stableId": f"{number:016x}", "name": f"Generated {number}", "result": "Passed",
            "durationSeconds": 1.0, "labels": [], "categories": [], "backgroundSteps": [], "attachments": [],
            "steps": [{"keyword": "When", "text": f"step {k}", "status": "Passed", "durationSeconds": 0.1,
                       "subSteps": [], "attachments": []} for k in range(steps)],
            "annotations": annotations, "httpInteractions": records}


def views(rng, scenario):
    out = [[]]
    requests = [r for r in scenario["httpInteractions"] if r["type"] == "Request"]
    out.append(["--service", rng.choice(sorted({r["serviceName"] for r in requests}))])
    out.append(["--step", rng.choice([s["text"][-1] for s in scenario["steps"]])])
    out.append(["--errors-only"])
    return out


CALL = re.compile(r"^( *)(s\d+)/i(\d+) ")


def check(number, scenario, view, output):
    problems = []
    recs = scenario["httpInteractions"]
    parent = r4_parents(recs)
    lines = output.split("\n")
    body = lines[2:]
    while body and body[-1] == "":
        body.pop()
    footer = body.pop() if body else ""

    def flag(name, text):
        problems.append((name, f"s{number} {' '.join(view) or '(unfiltered)'}: {text}"))

    for line in lines:
        if line != line.rstrip():
            flag("trailing", repr(line))

    step = view[1] if view[:1] == ["--step"] else None
    service = view[1] if view[:1] == ["--service"] else None
    errors = view == ["--errors-only"]
    first_response = {}
    for i, r in enumerate(recs):
        if r["type"] == "Response" and real_id(r.get("requestResponseId")):
            first_response.setdefault(r["requestResponseId"], r)

    def find_response(i, r):
        # The tool's FindResponse: the first response by id, else a response to the same service within
        # the next four records.
        rid = real_id(r.get("requestResponseId"))
        if rid:
            return first_response.get(rid)
        for j in range(i + 1, min(len(recs), i + 5)):
            if recs[j]["type"] == "Response" and recs[j]["serviceName"] == r["serviceName"]:
                return recs[j]
        return None

    def selected(i, r):
        if r["type"] != "Request":
            return False
        if step is not None and r.get("stepPath") != step:
            return False
        if service is not None and service.lower() not in r["serviceName"].lower():
            return False
        if errors:
            code = (find_response(i, r) or {}).get("statusCode")
            return code is not None and code >= 400
        return True

    expected = [i for i, r in enumerate(recs) if selected(i, r)]
    shown = set(expected)
    calls, printed = [], []
    for k, line in enumerate(body):
        m = CALL.match(line)
        kind = "call" if m else ("header" if line.startswith("── ") else "annotation" if line.startswith("  ── ") else "other")
        indent = len(m.group(1)) if m else len(line) - len(line.lstrip(" "))
        printed.append((kind, indent, int(m.group(3)) if m else None))
        if m:
            calls.append((k, int(m.group(3)), len(m.group(1)), line))

    if [c[1] for c in calls] != expected:
        flag("order", f"printed {[c[1] for c in calls]}, selected {expected}")

    indented = False
    for k, ordinal, indent, line in calls:
        chain, p = [], parent.get(ordinal)
        while p is not None:
            chain.append(p)
            p = parent.get(p)
        depth = sum(1 for p in chain if p in shown)
        indented |= depth > 0
        if indent != 2 + 2 * depth:
            flag("depth", f"i{ordinal} indent {indent}, {depth} shown ancestors: {line}")

        # F15: the parent a reader takes from indentation is the nearest line above with less of it.
        above = next(((kind, o) for kind, ind, o in reversed(printed[:k]) if ind < indent), None)
        implied = above[1] if above and above[0] == "call" else None
        named = re.search(r"  inside (s\d+)/i(\d+)$", line)
        mine = parent.get(ordinal)
        if named and int(named.group(2)) != mine:
            flag("tree", f"i{ordinal} names i{named.group(2)}, R4 parent i{mine}: {line}")
        if mine is not None and implied != mine and not named:
            flag("tree", f"i{ordinal} reads as inside {above}, R4 parent i{mine}, not named: {line}")
        if named and implied == mine:
            flag("tree", f"i{ordinal} names the parent its indentation already shows: {line}")

        r = recs[ordinal]
        says = re.search(r"  no response(  |$)", line) is not None
        should = real_id(r.get("requestResponseId")) is not None and real_id(r.get("requestResponseId")) not in first_response \
            and not r.get("isUserAction")
        if says != should:
            flag("status", f"i{ordinal} says no response: {says}, should: {should}: {line}")

    # S1: a step header stands directly above a shown call of that step, and only where the step changes.
    previous_step = None
    for k, (kind, indent, ordinal) in enumerate(printed):
        if kind == "header":
            path = body[k][3:].split("  ")[0]
            following = printed[k + 1] if k + 1 < len(printed) else None
            if not following or following[0] != "call" or recs[following[2]].get("stepPath") != path:
                flag("header", f"header {path!r} not above a call of its step: {body[k + 1] if k + 1 < len(body) else None!r}")
        elif kind == "call":
            path = recs[ordinal].get("stepPath")
            if path != previous_step and path is not None and (k == 0 or printed[k - 1][0] != "header"):
                flag("header", f"i{ordinal} starts step {path!r} with no header above it")
            previous_step = path

    # S1: every annotation, in index order, above the first shown call recorded after it, else at the end.
    notes = sorted(scenario.get("annotations") or [], key=lambda a: a["index"])
    if calls:
        expected_notes = []
        for a in notes:
            before = next((c for c in calls if c[1] >= a["index"]), None)
            expected_notes.append((a["text"], before[1] if before else None))
        actual_notes = []
        for k, (kind, indent, ordinal) in enumerate(printed):
            if kind == "annotation":
                after = next((printed[j][2] for j in range(k + 1, len(printed)) if printed[j][0] == "call"), None)
                actual_notes.append((body[k][5:], after))
        if actual_notes != expected_notes:
            flag("annotation", f"printed {actual_notes}, expected {expected_notes}")
    elif any(kind == "annotation" for kind, _, _ in printed):
        flag("annotation", "an annotation in a view that shows no call")

    legend = footer.endswith(" · indented calls ran inside the call above them")
    if legend != indented:
        flag("legend", f"legend {legend}, indented {indented}: {footer}")
    if not footer.startswith(f"{len(calls)} calls shown"):
        flag("legend", f"count: {footer} over {len(calls)} call lines")

    return problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tool")
    ap.add_argument("out")
    ap.add_argument("--scenarios", type=int, default=600)
    ap.add_argument("--seed", type=int, default=20260927)
    ap.add_argument("--workers", type=int, default=8)
    args = ap.parse_args()

    rng = random.Random(args.seed)
    scenarios = [generate(rng, n) for n in range(args.scenarios)]
    report = {"kronikolVersion": "3.31.0", "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
              "features": [{"name": "Generated", "labels": [], "scenarios": scenarios}]}
    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, "TestRunReport.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(report, f)

    jobs = [(n, v) for n, s in enumerate(scenarios) for v in views(rng, s)]

    def run(job):
        n, view = job
        result = subprocess.run([args.tool, "query", "flow", path, f"s{n}", "--max-bytes", "0", *view],
                                capture_output=True, text=True, encoding="utf-8")
        return n, view, result.returncode, result.stdout, result.stderr

    with ThreadPoolExecutor(args.workers) as pool:
        results = list(pool.map(run, jobs))

    failures, differ = Counter(), 0
    for n, view, code, out, err in results:
        if code != 0:
            failures["exit"] += 1
            print(f"exit {code} s{n} {view}: {err.strip()[:200]}")
            continue
        for name, text in check(n, scenarios[n], view, out):
            failures[name] += 1
            if name == "tree" and "reads as inside ('annotation'" in text:
                failures["tree, below an annotation"] += 1
            if failures[name] <= 8:
                print(f"{name:<9} {text}")
        step = view[1] if view[:1] == ["--step"] else None
        service = view[1] if view[:1] == ["--service"] else None
        proto = render(report, n, step=step, service=service, errors_only=view == ["--errors-only"])
        if proto.rstrip("\n") != out.rstrip("\n"):
            differ += 1
            failures["proto"] += 1
            # A build before the audit differs from the prototype only by the `inside` it did not print.
            strip = lambda text: re.sub(r"  inside s\d+/i\d+$", "", text.rstrip("\n"), flags=re.M)
            if strip(proto) == strip(out):
                failures["proto, by inside references alone"] += 1
            if differ <= 4:
                print(f"proto     s{n} {' '.join(view) or '(unfiltered)'} differs:\n--- tool\n{out}--- prototype\n{proto}\n")

    depth = Counter()
    for s in scenarios:
        par = r4_parents(s["httpInteractions"])
        for i in par:
            d, p = 0, par[i]
            while p is not None:
                d, p = d + 1, par.get(p)
            depth[d] += 1
    print(f"\n{len(scenarios)} scenarios, {len(jobs)} views; requests by depth: {dict(sorted(depth.items()))}")
    for name in ["exit", "order", "header", "annotation", "depth", "tree", "tree, below an annotation", "status", "legend",
                 "trailing", "proto", "proto, by inside references alone"]:
        print(f"{failures[name]:>6}  {name}")


if __name__ == "__main__":
    main()
