"""Compare nesting rules for `flow` on real reports.

Walks each scenario's records in capture order. A request is OPEN from its record until the response
record with its requestResponseId. A request that is never answered, or has no requestResponseId, is never
a parent (nobody knows when it ended). Rules for the parent of a request X, among the open requests:

  R1  the innermost open request, whatever it is
  R2  the innermost open P whose service is X's caller ("made by the party handling P")
  R3  the innermost open P where P.service == X.caller, or else (X.caller != P.caller and X shares
      P's trace: Kronikol traceId, or activityTraceId)
  R4  as R3, the trace test on Kronikol's traceId only (the plan's rule, section 3.1)

For each rule: requests nested, depth histogram, and how often indentation alone would point at the wrong
parent when printed in capture order (the nearest earlier printed line one level shallower is not the
parent), split by cause: an interleaved sibling branch, or a step header between parent and child.
Prints counts and a few addresses only.
"""
import json, sys, collections

RULES = ["R1", "R2", "R3", "R4"]

def short(p):
    parts = p.replace("\\", "/").split("/")
    for marker in ("tests", "build"):
        if marker in parts:
            i = parts.index(marker)
            return "/".join(parts[max(0, i - 1): i + 2])
    return "/".join(parts[-4:])

def same_trace(x, p, activity=True):
    if x.get("traceId") and x.get("traceId") == p.get("traceId"):
        return True
    return activity and bool(x.get("activityTraceId")) and x.get("activityTraceId") == p.get("activityTraceId")

def parent_of(rule, x, open_reqs, recs):
    for j in reversed(open_reqs):
        p = recs[j]
        if rule == "R1":
            return j
        if p.get("serviceName") == x.get("callerName"):
            return j
        if rule in ("R3", "R4") and x.get("callerName") != p.get("callerName") \
                and same_trace(x, p, activity=(rule == "R3")):
            return j
    return None

stats = {r: collections.Counter() for r in RULES}
hist = {r: collections.Counter() for r in RULES}
examples = collections.defaultdict(list)
nscen = nreq = 0

for path in sys.argv[1:]:
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    ordinal = -1
    for feature in data.get("features") or []:
        for s in feature.get("scenarios") or []:
            ordinal += 1
            nscen += 1
            recs = s.get("httpInteractions") or []
            answered = {c.get("requestResponseId") for c in recs if c.get("type") == "Response"}
            req_index = {c["requestResponseId"]: i for i, c in enumerate(recs)
                         if c.get("type") == "Request" and c.get("requestResponseId")}
            nreq += sum(1 for c in recs if c.get("type") == "Request")
            for rule in RULES:
                open_reqs, depth, parent = [], {}, {}
                printed = []            # (index, depth, step) in print order, as flow prints requests
                for i, c in enumerate(recs):
                    rr = c.get("requestResponseId")
                    if c.get("type") == "Request":
                        par = parent_of(rule, c, open_reqs, recs)
                        parent[i] = par
                        depth[i] = 0 if par is None else depth[par] + 1
                        hist[rule][depth[i]] += 1
                        if par is not None:
                            stats[rule]["nested"] += 1
                            # the parent indentation implies: nearest earlier printed line at depth-1
                            implied = next((pi for pi, pd, _ in reversed(printed) if pd == depth[i] - 1), None)
                            # flow prints a step header whenever a record's stepPath changes, responses included
                            step_break = any(recs[k].get("stepPath") != recs[par].get("stepPath")
                                             for k in range(par + 1, i + 1))
                            if implied != par:
                                stats[rule]["wrong parent: interleaved branch"] += 1
                                if len(examples[(rule, "interleaved")]) < 3:
                                    examples[(rule, "interleaved")].append(f"{short(path)} s{ordinal}/i{i} parent i{par}, indentation says i{implied}")
                            elif step_break:
                                stats[rule]["wrong parent: step header between"] += 1
                                if len(examples[(rule, "step")]) < 3:
                                    examples[(rule, "step")].append(f"{short(path)} s{ordinal}/i{i} parent i{par} step {recs[par].get('stepPath')} -> {c.get('stepPath')}")
                        printed.append((i, depth[i], c.get("stepPath")))
                        if rr and rr in answered:
                            open_reqs.append(i)
                    elif c.get("type") == "Response" and rr in req_index and req_index[rr] in open_reqs:
                        open_reqs.remove(req_index[rr])

print(f"{nscen} scenarios, {nreq} requests\n")
print(f"{'rule':5} {'nested':>7} {'interleaved':>12} {'step-split':>11}  depth histogram")
for r in RULES:
    st = stats[r]
    print(f"{r:5} {st['nested']:>7} {st['wrong parent: interleaved branch']:>12} {st['wrong parent: step header between']:>11}  {dict(sorted(hist[r].items()))}")
print()
for (r, kind), ex in sorted(examples.items()):
    print(f"{r} {kind}:")
    for e in ex:
        print(f"    {e}")
