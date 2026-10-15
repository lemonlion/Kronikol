"""Prints what each side of a playwright-bdd run says about every attachment.

Usage: python -I summarise_attachments.py <messages.ndjson> <tests.jsonl>
Messages: per attempt (testCaseStarted), each attachment's fileName, mediaType, encoding, body bytes, url, and the
step it is pinned to (the pickle step's text, or the hook's name). Tests: each attachment record's name, mediaType,
attempt, and the size of the file its path names.
"""
import base64
import json
import os
import sys

messages, tests = sys.argv[1], sys.argv[2]

pickles, steps_text, hooks, cases, starts, attachments = {}, {}, {}, {}, [], []
with open(messages, encoding="utf-8") as f:
    for line in f:
        if not line.strip():
            continue
        env = json.loads(line)
        if "pickle" in env:
            p = env["pickle"]
            pickles[p["id"]] = p["name"]
            for s in p["steps"]:
                steps_text[s["id"]] = s["text"]
        elif "hook" in env:
            h = env["hook"]
            hooks[h["id"]] = h.get("name") or h.get("type") or "hook"
        elif "testCase" in env:
            tc = env["testCase"]
            cases[tc["id"]] = (tc["pickleId"], {ts["id"]: ts for ts in tc["testSteps"]})
        elif "testCaseStarted" in env:
            starts.append(env["testCaseStarted"])
        elif "attachment" in env:
            attachments.append(env["attachment"])

start_by_id = {s["id"]: s for s in starts}
print("== messages")
for s in starts:
    pickle_id, test_steps = cases[s["testCaseId"]]
    print(f"-- {pickles[pickle_id]!r} attempt {s.get('attempt')} ({s['id']})")
    for a in attachments:
        if a.get("testCaseStartedId") != s["id"]:
            continue
        ts = test_steps.get(a.get("testStepId"))
        if ts is None:
            where = "no step"
        elif ts.get("pickleStepId"):
            where = "step " + repr(steps_text.get(ts["pickleStepId"]))
        else:
            where = "hook " + repr(hooks.get(ts.get("hookId"), ts.get("hookId")))
        body = a.get("body") or ""
        size = len(base64.b64decode(body)) if a.get("contentEncoding") == "BASE64" else len(body.encode("utf-8"))
        print(f"   {a.get('fileName')!r:28} {a.get('mediaType'):22} {a.get('contentEncoding'):8} {size:>9} B"
              f"  url={a.get('url')!r}  on {where}")
orphans = [a for a in attachments if a.get("testCaseStartedId") not in start_by_id]
if orphans:
    print(f"   ({len(orphans)} attachment(s) with no testCaseStartedId: {[a.get('fileName') for a in orphans]})")

print("== tests")
with open(tests, encoding="utf-8") as f:
    for line in f:
        r = json.loads(line)
        if r["event"] != "attachment":
            continue
        path = r.get("path") or ""
        size = os.path.getsize(path) if path and os.path.exists(path) else None
        print(f"   {r['testId'][:8]} attempt {r.get('attempt')}  {r.get('name')!r:24} {r.get('mediaType')!s:22}"
              f" {size!s:>9} B  {os.path.basename(path)}")
