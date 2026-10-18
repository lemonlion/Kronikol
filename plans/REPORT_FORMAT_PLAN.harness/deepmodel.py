# Synthetic: how one LightBDD composite step costs in TestRunReport.json at each nesting depth (sizes only).
import json, gzip
from compression import zstd
def step(i, subs):
    return {"keyword": "And", "text": f"the order {i} is validated against the price list", "status": "Passed",
            "durationSeconds": 0.0123, "bypassReason": None, "docString": None, "docStringMediaType": None,
            "failureMessage": None, "sourceFile": None, "sourceLine": None, "comments": [], "subSteps": subs,
            "attachments": [], "parameters": [],
            "textSegments": [{"text": "the order ", "parameterName": None, "parameter": None, "tableReference": None, "tableReferenceFormattedValue": None},
                             {"text": None, "parameterName": "id", "parameter": {"value": str(i), "expectation": None, "status": "NotApplicable"}, "tableReference": None, "tableReferenceFormattedValue": None},
                             {"text": " is validated against the price list", "parameterName": None, "parameter": None, "tableReference": None, "tableReferenceFormattedValue": None}]}
def chain(depth):  # one chain of composite steps, depth levels deep (21 steps for depth 20)
    node = step(depth, [])
    for d in range(depth - 1, -1, -1):
        node = step(d, [node])
    return node
def wrap(steps):  # the real nesting above a step: root, features, feature, scenarios, scenario, steps
    return {"formatVersion": 2, "features": [{"name": "f", "scenarios": [{"id": "x", "steps": steps}]}]}
def nn(o):
    if isinstance(o, dict): return {k: nn(v) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list): return [nn(x) for x in o]
    return o
ind = lambda o: len(json.dumps(o, ensure_ascii=False, indent=2).replace("\n", "\r\n").encode())
cmp = lambda o: len(json.dumps(o, ensure_ascii=False, separators=(",", ":")).encode())
base_i, base_c = ind(wrap([])), cmp(wrap([]))
prev = None
print("depth  bytes-this-step(indented CRLF)  compact  compact-no-nulls")
rows = []
for D in range(0, 21):
    t = wrap([chain(D)])
    rows.append((ind(t), cmp(t), cmp(nn(t))))
for D in (0, 1, 5, 10, 15, 20):
    a = rows[D]; b = rows[D - 1] if D else (base_i, base_c, base_c)
    print(f"{D:>5}  {a[0]-b[0]:>10,}  {a[1]-b[1]:>8,}  {a[2]-b[2]:>8,}")
t = wrap([chain(20) for _ in range(10)])  # 10 chains x 21 steps = 210 steps
bi = json.dumps(t, ensure_ascii=False, indent=2).replace("\n", "\r\n").encode()
bc = json.dumps(nn(t), ensure_ascii=False, separators=(",", ":")).encode()
print(f"210 steps in 10 chains 20 deep: indented {len(bi):,}  compact-no-nulls {len(bc):,}  ({len(bc)/len(bi):.1%})  "
      f"gz6(indented) {len(gzip.compress(bi,6)):,}  gz6(compact-no-nulls) {len(gzip.compress(bc,6)):,}  zstd19 {len(zstd.compress(bc,19)):,}")
