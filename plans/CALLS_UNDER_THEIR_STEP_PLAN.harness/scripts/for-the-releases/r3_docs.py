"""R3's changelog entry, wiki lines and ledger line, applied at release time with the number and date taken then.

Usage: r3_docs.py changelog <worktree> <version> <previous> <date>
       r3_docs.py wiki <wiki worktree> <version>
       r3_docs.py ledger <Kronikol4J worktree> <version> <date>
"""
import pathlib
import sys

what, root, version = sys.argv[1], pathlib.Path(sys.argv[2]), sys.argv[3]


def edit(path, reps):
    p = root / path
    s = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in s
    s = s.replace("\r\n", "\n")
    for old, new in reps:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


if what == "changelog":
    previous, date = sys.argv[4], sys.argv[5]
    edit("CHANGELOG.md", [(f"""## [{previous}] - """, f"""## [{version}] - {date}

The minor part moves ({previous} to {version}): a call names its sub-step, which a reader could not get before (`plans/CALLS_UNDER_THEIR_STEP_PLAN.md`
R3, closes #225). Report output changes, so Kronikol4J's ledger has a line.

### Added

- **A call reads the sub-step it was made in (#225).** A call's `stepPath` can now read `0.1`, the deepest step its flow was in, in the
  data file, in XML and YAML, and through `kronikol merge`. `kronikol query flow s4/0.1` and `interactions s4 --step 0.1` return that
  sub-step's calls, and the diagram still draws a bar for a top-level step alone.

### Changed

- **`steps`** gives a row the calls of its step and of everything under it, so a top-level row keeps its count and a sub-step's row shows
  its own, counted in one pass over the calls.
- **`failures`** and its `--json` list each call under the deepest failing step that covers it, so a failing parent with passed sub-steps
  keeps every call, no call is listed twice in a failing chain, and a failing sub-step under a passing parent leaves out its passed
  siblings' calls. **`Failures.md`** reads the same way.
- **`--group-by step`** splits by sub-step, as it groups by the raw `stepPath`.
- The data file's schema says a `stepPath` can be dotted, and the query skill's files say what a dotted path means.

### Fixed

- **A step tree 28 levels deep stopped the report.** A step holds its sub-steps inside it in the data file, two JSON levels each, and the
  writer, `kronikol query` and `kronikol merge` stopped at the JSON reader's default of 64, with a `JsonException` when the report was
  written, and `kronikol decode` refused the file. They now go to 1,024.
- **A step tree about 415 levels deep took the test process with it,** as a stack overflow, and no report was written. A report now lists
  200 levels of steps, and the last row it lists says that the steps below it are not listed.

### Notes for a reader

- A tool older than this release reading a newer report shows a top-level row with no calls where they all sit under a sub-step.
- A merge reads a shard from before this release beside one from after, and each scenario's calls read as their own shard wrote them.
- `kronikol ingest` stays at the top level: a capture draws a bar for level 0 alone and its records have no flow to read (filed as its own issue).

## [{previous}] - """)])

elif what == "wiki":
    edit("Generated-Reports.md", [(
        """(`b0`, `0`, `1`). Where a framework's adapter collected the steps, a call made with a step open names that step, or its top-level step, whatever else ran.""",
        """(`b0`, `0`, `1`), dotted for a sub-step (`0.1`, since """ + version + """). Where a framework's adapter collected the steps, a call made in a step names that step, whatever else ran.""")])
    edit("Querying-Reports.md", [(
        """compose. At run scope, `step` buckets collide across scenarios (the same `stepPath` is a different step
in each test) and the header says so; scope with `s3` for one scenario's steps.""",
        """compose. `step` groups by the raw `stepPath`, so a parent and its sub-step are two buckets (since """ + version + """). At run scope,
`step` buckets collide across scenarios (the same `stepPath` is a different step in each test) and the header says so; scope with
`s3` for one scenario's steps.""")])
    edit("Step-Tracking.md", [
        ("""- **Calls** — a call made in a sub-step belongs to its Gherkin step: the diagram draws a bar for each Gherkin step and none for a sub-step, and the call's `stepPath` is the Gherkin step's (#225).""",
         f"""- **Calls**: a call made in a sub-step reads that sub-step: its `stepPath` is `0.1`, and `kronikol query flow s4/0.1` returns it (since {version}). The diagram still draws a bar for each Gherkin step and none for a sub-step. A call from `kronikol ingest` reads the top-level step, as a capture has no flow to read."""),
        ("""Delimiters are only injected for top-level steps; nested sub-steps do not produce additional delimiters.""",
         """Delimiters are only injected for top-level steps; nested sub-steps do not produce additional delimiters. A call made in a sub-step still reads that sub-step's `stepPath`."""),
    ])

elif what == "ledger":
    date = sys.argv[4]
    p = root / "docs/REMAINING_PARITY.md"
    s = p.read_bytes().decode("utf-8")
    line = f"""- **A call reads its sub-step (.NET {version}, {date}, `CALLS_UNDER_THEIR_STEP_PLAN.md` R3, closes #225).** .NET's `stepPath` can read `0.1`, the deepest step a call's flow was in, and `steps`, `failures`, `Failures.md` and `--group-by step` read it. The port's calls stay at the top level (Q7): its stack has no flow to read, so a dotted path needs the port's own answer to what a flow is (#3 there). Report output moves for any call made in a sub-step.
"""
    eol = "\r\n" if "\r\n" in s else "\n"
    lines = s.split(eol)
    last = max(i for i, l in enumerate(lines) if l.startswith("- **") and "(.NET 5." in l)
    lines.insert(last + 1, line.rstrip("\n"))
    p.write_bytes(eol.join(lines).encode("utf-8"))

print("applied", what)
