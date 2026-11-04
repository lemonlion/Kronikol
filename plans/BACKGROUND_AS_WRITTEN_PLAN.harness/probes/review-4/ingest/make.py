import json, os, sys
here = sys.argv[1]
T = "2026-11-03T10:00:%02d+00:00"
def t(s): return T % s

BS = chr(92)
step_text = "the file C:" + BS + "temp" + BS + "new is read at 100%date() of <&star> stock"

caps = []
def marker(tid, text, s):
    caps.append({"type": "Request", "uri": "http://override.com/", "serviceName": "", "callerName": "", "testId": tid, "testName": tid,
                 "kind": "marker", "markerKind": "Step", "timestamp": t(s),
                 "plantUml": "hnote across <<stepDelimiter>> #black:<color:white>Given " + text})
    caps.append({"type": "Request", "uri": "http://override.com/", "serviceName": "", "callerName": "", "testId": tid, "testName": tid,
                 "kind": "marker", "markerKind": "Step", "markerEnd": True, "timestamp": t(s)})
def call(tid, s):
    rid = f"{tid}-{s}"
    caps.append({"type": "Request", "method": "GET", "uri": "http://api.local/x", "serviceName": "API", "callerName": "Tests", "testId": tid, "testName": tid, "timestamp": t(s), "requestResponseId": rid, "traceId": rid})
    caps.append({"type": "Response", "method": "GET", "uri": "http://api.local/x", "serviceName": "API", "callerName": "Tests", "testId": tid, "testName": tid, "timestamp": t(s + 1), "requestResponseId": rid, "traceId": rid, "statusCode": "OK"})

marker("t1", step_text, 1)
call("t1", 2)

tests = [
    {"event": "start", "testId": "t1", "testName": "raw marker with text the bar builder would escape", "feature": "F", "timestamp": t(0)},
    {"event": "step", "testId": "t1", "keyword": "Given", "text": step_text, "timestamp": t(1)},
    {"event": "end", "testId": "t1", "status": "passed", "timestamp": t(5)},
]
with open(os.path.join(here, "caps.ndjson"), "w", encoding="utf-8", newline="\n") as f:
    for c in caps:
        f.write(json.dumps(c) + "\n")
with open(os.path.join(here, "tests.ndjson"), "w", encoding="utf-8", newline="\n") as f:
    for c in tests:
        f.write(json.dumps(c) + "\n")
