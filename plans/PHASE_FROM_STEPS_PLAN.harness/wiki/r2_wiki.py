"""R2's wiki edits (plan section 6), applied at release, after r1_wiki.py, to a checkout of Kronikol.wiki.

Each edit is an exact replacement that must match once; line endings are kept as the file has them.

    python r2_wiki.py <wiki-checkout> <version>
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
VERSION = sys.argv[2]

EDITS = {
    "Ingesting-External-Captures.md": [
        ("Attribute interactions that carry no `testId` to the test that was running at their timestamp; the optional value",
         "Attribute interactions that carry no `testId` to the test that was running at their timestamp, and print how many "
         "it attributed; the optional value"),
        ("Deterministic, and exactly right for a suite running one worker at a time.",
         "Deterministic, and exactly right for a suite running one worker at a time.\n"
         "\n"
         "`kronikol ingest` prints how many records the pass attributed, as a line after `Replayed …`, zero included:\n"
         "`--attribute-by-window: 4 interaction record(s) attributed to a test by time window.` What the window and\n"
         "claims passes attribute is not a diagnostic. Until " + VERSION + " each was one, of kind `UnattributedInteractions`,\n"
         "and since the query header shows the first message of each kind, a run that also left records unattributed\n"
         "read as its success line \"(×2)\". What no pass could attribute is still recorded:\n"
         "`N interaction record(s) could not be attributed to a test.` Under `ExclusiveOnly` the records two windows held\n"
         "are recorded when there are any (until " + VERSION + " the line was recorded at zero too)."),
        ("What this cannot bring back is a record with **no twin**. Redis has no OpenTelemetry span in most stacks, so a "
         "contested Redis wire record is still dropped.",
         "What this cannot bring back is a record with **no twin**. Redis has no OpenTelemetry span in most stacks, so a "
         "contested Redis wire record is still dropped.\n"
         "\n"
         "The merged call takes its twin's phase too when the wire record has none: `--phase-from-steps` runs before the\n"
         "merge, and a wire record no pass could place had no test to be phased by. Until " + VERSION + " such a call kept no\n"
         "phase."),
        ("| `UnattributedInteractions` | How many records window attribution claimed, and how many are still unattributed. |",
         "| `UnattributedInteractions` | How many records could not be attributed to a test. What a pass did attribute is not a "
         "diagnostic (`kronikol ingest` prints the window pass's count). |"),
    ],
    "Cross-Run-History.md": [
        ("   repository-level `.git` uses its own.\n",
         "   repository-level `.git` uses its own. An ingest (`kronikol ingest`, `IngestPipeline.Run`) has no test output:\n"
         "   it looks above the directory it is run in, then above the reports directory. Until " + VERSION + " it looked\n"
         "   above the tool's own folder first, so a tool built inside a checkout read and appended to that checkout's\n"
         "   ledger, whatever it was replaying.\n"),
    ],
    "Report-Configuration.md": [
        ("for the nearest `.kronikol` or `.git` directory above the test output or the reports directory.",
         "for the nearest `.kronikol` or `.git` directory above the test output or the reports directory (for an ingest, "
         "above the directory it is run in, then the reports directory)."),
    ],
    "Diagnostics-and-Debugging.md": [
        ("| `UnattributedInteractions` | How many records window attribution claimed, and how many remain unattributed. |",
         "| `UnattributedInteractions` | How many records could not be attributed to a test. |"),
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
