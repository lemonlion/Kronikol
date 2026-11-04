#!/usr/bin/env python
# A capture for a checked-in Cucumber Messages run: one call per test step of each test case's last attempt, 1 ms into the step,
# under the scenario id the synthesiser mints, or the kronikol-test-id attachment's value where the run has one.
# usage: calls.py <messages.ndjson> <out capture file>
import base64, json, sys, datetime as dt

picks, cases, attempts, steps, attach = {}, {}, {}, {}, {}
for line in open(sys.argv[1], encoding="utf-8"):
    o = json.loads(line)
    if "pickle" in o:
        picks[o["pickle"]["id"]] = o["pickle"]
    elif "testCase" in o:
        cases[o["testCase"]["id"]] = o["testCase"]
    elif "testCaseStarted" in o:
        a = o["testCaseStarted"]
        attempts.setdefault(a["testCaseId"], []).append(a)
    elif "testStepStarted" in o:
        s = o["testStepStarted"]
        steps.setdefault(s["testCaseStartedId"], []).append(s)
    elif "attachment" in o:
        a = o["attachment"]
        if a.get("fileName", "").lower() == "kronikol-test-id" and a.get("testCaseStartedId"):
            body = a.get("body", "")
            if a.get("contentEncoding") == "BASE64":
                body = base64.b64decode(body).decode("utf-8")
            attach[a["testCaseStartedId"]] = body.strip()

out = []
for case_id, tries in attempts.items():
    last = max(tries, key=lambda a: a.get("attempt", 0))
    case = cases[case_id]
    test_id = attach.get(last["id"]) or f'{case["pickleId"]}#{last.get("attempt", 0)}'
    for n, s in enumerate(steps.get(last["id"], [])):
        ts = dt.datetime.fromtimestamp(s["timestamp"]["seconds"] + s["timestamp"]["nanos"] / 1e9 + 0.0005, dt.timezone.utc)
        stamp = ts.isoformat().replace("+00:00", "Z")
        end = (ts + dt.timedelta(microseconds=100)).isoformat().replace("+00:00", "Z")
        kind = "hook" if any(t["id"] == s["testStepId"] and t.get("hookId") for t in case["testSteps"]) else "step"
        rid = f"{test_id}-{n}"
        common = dict(testId=test_id, method="POST", uri=f"http://shop.test/{kind}-{n}", serviceName="Shop", callerName="Tests", content="{}", requestResponseId=rid)
        out.append(dict(common, type="Request", timestamp=stamp))
        out.append(dict(common, type="Response", statusCode="200", timestamp=end))

with open(sys.argv[2], "w", encoding="utf-8") as f:
    for r in out:
        f.write(json.dumps(r) + "\n")
print(len(out) // 2, "calls")
