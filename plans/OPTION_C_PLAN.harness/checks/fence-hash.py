# The SHA-256 of both variants of a fenced report script, without and with the $verdict lines, as VerdictFencesTests
# pins them (VerdictFences.Variant reproduced; line endings normalized, a BOM dropped). S5 re-pinned the feature map
# helper and the search function with it.
# usage: python fence-hash.py <script.js> ...
import hashlib, sys

OPEN, ELSE, CLOSE, PREFIX = "// kron:verdicts", "// kron:else", "// kron:/verdicts", "//|"

def variant(source, verdicts):
    out, state, first = [], "outside", True
    for line in source.split("\n"):
        bare = line.rstrip("\r")
        if bare == OPEN:
            assert state == "outside"; state = "verdicts"; continue
        if bare == ELSE:
            assert state == "verdicts"; state = "without"; continue
        if bare == CLOSE:
            assert state != "outside"; state = "outside"; continue
        if state == "outside":
            kept = line
        elif state == "verdicts":
            kept = line if verdicts else None
        else:
            assert bare.startswith(PREFIX)
            kept = None if verdicts else line[len(PREFIX):]
        if kept is None:
            continue
        out.append(kept)
    assert state == "outside"
    return "\n".join(out)

def sha(text):
    text = text.replace("\r\n", "\n").lstrip("﻿")
    return hashlib.sha256(text.encode("utf-8")).hexdigest()

for path in sys.argv[1:]:
    with open(path, encoding="utf-8", newline="") as f:
        src = f.read()
    print(path.split("/")[-1], sha(variant(src, False)), sha(variant(src, True)))
