"""Writes variants of out/tests.jsonl, changing only attachment records.

gone       every attachment path points at a folder that does not exist (CI kept only the two NDJSON files)
noattempt  attachment records carry no attempt, as the wiki's reporter recipe writes them
"""
import json
import posixpath

rows = [json.loads(line) for line in open("out/tests.jsonl", encoding="utf-8")]


def write(name, change):
    with open(f"out/tests-{name}.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for r in rows:
            r = dict(r)
            if r["event"] == "attachment":
                change(r)
            f.write(json.dumps(r) + "\n")


def gone(r):
    r["path"] = "C:/gone/" + posixpath.basename(r["path"].replace(chr(92), "/"))


def noattempt(r):
    r.pop("attempt", None)


write("gone", gone)
write("noattempt", noattempt)


def noname(r):
    r.pop("name", None)


write("noname", noname)
