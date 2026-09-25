"""Nesting census: how `flow` would indent calls, measured on real reports.

For every scenario the records are walked in capture order (the order of httpInteractions, which is the
order query addresses them by). A request is "open" from its record to the response record carrying the
same requestResponseId. Depth of a request = how many requests are open when it is recorded (rule R1,
"made while the call above was waiting for its answer").

Reads each report with json.load and prints only counts and a few addresses, never content.
Usage: python nesting_census.py <report.json>...
"""
import json, sys, collections

def short(p):
    parts = p.replace("\\", "/").split("/")
    for marker in ("tests", "build"):
        if marker in parts:
            i = parts.index(marker)
            return "/".join(parts[max(0, i - 1): i + 2])
    return "/".join(parts[-4:])

totals = collections.Counter()
examples = collections.defaultdict(list)
depth_hist = collections.Counter()
types, metas = collections.Counter(), collections.Counter()

def note(kind, text):
    totals[kind] += 1
    if len(examples[kind]) < 4:
        examples[kind].append(text)

print(f"{'report':58} {'scen':>4} {'req':>5} {'nest':>5} {'scn+':>4} {'maxd':>4} {'lifo!':>5} {'open':>4} {'orph':>4} {'norr':>4} {'swal':>4}")
for path in sys.argv[1:]:
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    row = collections.Counter()
    ordinal = -1
    for feature in data.get("features") or []:
        for s in feature.get("scenarios") or []:
            ordinal += 1
            row["scen"] += 1
            recs = s.get("httpInteractions") or []
            by_id = {}
            for i, c in enumerate(recs):
                types[c.get("type")] += 1
                metas[c.get("metaType")] += 1
                if c.get("type") == "Request" and c.get("requestResponseId"):
                    by_id[c["requestResponseId"]] = i
            answered = {c.get("requestResponseId") for c in recs if c.get("type") == "Response"}
            stack = []          # indices of open requests
            unclosed = set()    # requests that never get a response
            scenario_nested = False
            for i, c in enumerate(recs):
                addr = f"s{ordinal}/i{i}"
                where = f"{short(path)} {addr}"
                rr = c.get("requestResponseId")
                if c.get("type") == "Request":
                    row["req"] += 1
                    depth = len(stack)
                    depth_hist[depth] += 1
                    row["maxd"] = max(row["maxd"], depth)
                    if depth:
                        row["nest"] += 1
                        scenario_nested = True
                        parent = recs[stack[-1]]
                        pcaller, pservice = parent.get("callerName"), parent.get("serviceName")
                        caller = c.get("callerName")
                        same_trace = (c.get("traceId") and c.get("traceId") == parent.get("traceId")) or \
                                     (c.get("activityTraceId") and c.get("activityTraceId") == parent.get("activityTraceId"))
                        # Classified in the order rule R4 reasons (plan section 4.1).
                        under_unanswered = any(o in unclosed for o in stack)
                        if caller == pservice:
                            note("nested: caller is the parent's service", where)
                        elif caller == pcaller:
                            kind = "under a request never answered" if under_unanswered else "concurrent work of one party"
                            note(f"nested: same caller as parent ({kind})", f"{where} {caller} -> {c.get('serviceName')} under i{stack[-1]}")
                        elif same_trace:
                            note("nested: other caller, same trace as parent", f"{where} {caller} -> {c.get('serviceName')}")
                        else:
                            note("nested: unrelated caller and trace", f"{where} {caller} -> {c.get('serviceName')} under i{stack[-1]} {pcaller} -> {pservice}")
                        if depth >= 2:
                            note("depth 2 or more: " + ("a request never answered in the chain" if under_unanswered else "out-of-order siblings only"), where)
                        if c.get("stepPath") != parent.get("stepPath"):
                            note("nested: step differs from parent's", f"{where} step {c.get('stepPath')} under i{stack[-1]} step {parent.get('stepPath')}")
                        if stack[-1] in unclosed or any(o in unclosed for o in stack):
                            row["swal"] += 1
                            note("nested under a request that is never answered", where)
                    if not rr:
                        row["norr"] += 1
                        note("request without requestResponseId", f"{where} ua={c.get('isUserAction')} meta={c.get('metaType')}")
                        continue            # cannot be closed, so it is never pushed
                    if rr not in answered:
                        row["open"] += 1
                        unclosed.add(i)
                        note("request never answered", f"{where} {c.get('callerName')} -> {c.get('serviceName')} {c.get('method')} meta={c.get('metaType')}")
                    stack.append(i)
                elif c.get("type") == "Response":
                    if rr in by_id and by_id[rr] in stack:
                        if stack[-1] != by_id[rr]:
                            row["lifo!"] += 1
                            note("response closes a request that is not innermost", f"{where} closes i{by_id[rr]} while i{stack[-1]} open")
                        stack.remove(by_id[rr])
                    else:
                        row["orph"] += 1
                        note("response with no open request", where)
            if scenario_nested:
                row["scn+"] += 1
    totals.update({k: v for k, v in row.items() if k != "maxd"})
    print(f"{short(path)[:58]:58} {row['scen']:>4} {row['req']:>5} {row['nest']:>5} {row['scn+']:>4} {row['maxd']:>4} {row['lifo!']:>5} {row['open']:>4} {row['orph']:>4} {row['norr']:>4} {row['swal']:>4}")

print()
print("columns: nest = requests at depth >= 1; scn+ = scenarios with any nesting; lifo! = responses closing a")
print("non-innermost request; open = requests never answered; orph = responses with no open request;")
print("norr = requests with no requestResponseId; swal = requests nested under a never-answered request")
print()
print("depth histogram (requests):", dict(sorted(depth_hist.items())))
print("record types:", dict(types), " metaTypes:", dict(metas))
print()
for kind in sorted(examples):
    print(f"{kind}: {totals[kind]}")
    for e in examples[kind]:
        print(f"    {e}")
