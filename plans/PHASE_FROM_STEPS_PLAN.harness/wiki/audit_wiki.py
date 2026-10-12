"""The audit's wiki edits (2026-10-12), applied at release to a checkout of Kronikol.wiki.

Each edit is an exact replacement that must match once; line endings are kept as the file has them.

    python audit_wiki.py <wiki-checkout> <version>
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
VERSION = sys.argv[2]

EDITS = {
    "Ingesting-External-Captures.md": [
        ("| `requestResponseId` | | Pairs a request with its response (one arrow). Any string; non-UUIDs are hashed to a stable GUID. |",
         "| `requestResponseId` | | Pairs a request with its response (one arrow). Any non-empty string; non-UUIDs are hashed to a "
         "stable GUID. An empty one pairs nothing, as a missing one does (until " + VERSION + " every record that carried an "
         "empty one was a half of the same call, so each call read as answered by the first response). |"),
        ("- a **response follows its request** (matched by `requestResponseId`), never its own timestamp — a slow\n"
         "  query answered after the test's `end` record must not be orphaned or, worse, given to the next test;",
         "- a **response follows its request** (matched by `requestResponseId`), never its own timestamp: a slow\n"
         "  query answered after the test's `end` record must not be orphaned or, worse, given to the next test. It\n"
         "  follows it wherever the two sit in the files, and a response whose request no window holds (or two do,\n"
         "  under `ExclusiveOnly`) stays unattributed with it. Until " + VERSION + " a response read before its request, or\n"
         "  whose request no window held, was judged on its own timestamp. Only a response whose request is not in\n"
         "  the capture is judged alone;"),
        ("which leaves a record inside two test windows unattributed instead of giving it to whichever test started last.",
         "which leaves a record inside two test windows unattributed instead of giving it to whichever test started last. "
         "A response takes its request's claim wherever the two sit in the files (until " + VERSION + " a response read "
         "first was judged alone); one whose request matched no claim is judged on its own content, since a claim names "
         "its owner where a timestamp does not."),
        ("A record with no `requestResponseId` is\njudged on its own `timestamp`.",
         "A record with no `requestResponseId` (or an empty one) is\njudged on its own `timestamp`."),
    ],
}

for page, edits in EDITS.items():
    path = WIKI / page
    raw = path.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    for old, new in edits:
        count = text.count(old)
        assert count == 1, f"{page}: {old[:70]!r} matched {count} times"
        text = text.replace(old, new)
    path.write_bytes((text.replace("\n", "\r\n") if crlf else text).encode("utf-8"))
    print(f"{page}: {len(edits)} edit(s)")
