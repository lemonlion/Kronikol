"""Summarises `kronikol query parties --json` envelopes: test parties, underTest parties, R1's subject row (the underTest
party receiving the most calls, then by ordinal name, as PartyResolver.cs:98-99), and the notes.
Usage: python -I -X utf8 parties_summary.py <envelope.json>..."""
import json, os, sys

for p in sys.argv[1:]:
    name = os.path.basename(p)[:-5]
    try:
        env = json.load(open(p, encoding="utf-8-sig"))
    except Exception as e:
        print(f"{name}: unreadable ({e})"); continue
    items = env.get("items") or []
    tests = sorted(i["party"] for i in items if i["kind"] == "test")
    under = [i for i in items if i["kind"] == "underTest"]
    subject = sorted(under, key=lambda i: (-i["callsReceived"], i["party"]))
    subj = subject[0]["party"] if subject else None
    kinds = {}
    for i in items:
        kinds[i["kind"]] = kinds.get(i["kind"], 0) + 1
    notes = env.get("notes") or []
    print(f"{name:28} parties {len(items):2} tests {tests} underTest {[ (u['party'], u['kindSource'], u['callsMade'], u['callsReceived']) for u in under]} subjectRow {subj!r} kinds {kinds} truncated {env.get('truncated')}")
    for n in notes:
        print(f"    note: {n[:300]}")
