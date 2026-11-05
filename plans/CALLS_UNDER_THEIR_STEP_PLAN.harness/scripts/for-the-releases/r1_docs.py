"""R1's changelog entry, wiki lines and ledger line, applied at release time with the number and date taken then.

Usage: r1_docs.py changelog <worktree> <version> <previous> <date>
       r1_docs.py wiki <wiki worktree> <version>
       r1_docs.py ledger <Kronikol4J worktree> <version> <date>
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

The patch part moves ({previous} to {version}): bug fixes, with no new option and no new public member (`plans/CALLS_UNDER_THEIR_STEP_PLAN.md`
R1, part of #225). Report output moves for a test whose steps overlap, so Kronikol4J's ledger has a line.

### Fixed

- **Steps that overlap keep their own places (#225).** `StepCollector` kept one stack of open steps for each test, so two steps started
  together (`Task.WhenAll`) were listed one inside the other, a failure landed on the step opened last, a completion closed whichever
  step was on top, and a step left running took the next step as its child and kept its bar from being drawn. Each flow of execution now
  knows its own step. Two steps at once are siblings, in the order they began, each with its own result, duration, assertions and
  attachments, and each draws its bar if it is at the top level.
- **A call reads the step its flow was in.** A call used to take its step from what was open for the test, which the last step started
  decides, so with two top-level steps at once the first step's calls read the second's row. It now reads its own flow's step. A record
  from elsewhere, a service's own thread for one, reads what is open: the one chain when there is one, the step several share when they
  run at once, and no step when they share none. A service's onward call reads the step of the call it ran inside, known from the trace
  the call carries, whichever way the two parties are named. A `[Step]` in a service's own handler stands under the step that called it,
  by the same trace, and a `[Step]` on a thread with no context of the test's, a message consumer's for one, stands under the step running.
- **A flow comes back to its step after a stretch under another test's id,** where the steps after it were listed as top-level rows.

### Behaviour changes

- Rows move where top-level steps overlap: what was listed nested is now listed side by side, and its bars agree with that.
- A step still running when the report is drawn is listed with no result, where it was not listed at all or was listed as passed.
- A call whose step can no longer be known, as two steps with nothing shared were running, reads no step, where it read the wrong one.
- A spare `CompleteStep(testId, ...)` no longer closes a step left running, which closes by itself with its own result, and a step that
  bypasses itself by id no longer closes its parent.
- `kronikol query steps` marks a step with no result `?`, beside a step that has one. Where a scenario failed and no step is marked
  failed, `failures` and `Failures.md` name such a step as still running when the report was written (`**Still running when the report
  was written**` in the digest), and in `failures --json` and `Failures.jsonl` each entry of `failingSteps` has a `stillRunning` field
  that says which it is. The report's step counts leave such a step out of skipped. `flow` prints `── no step` above a call that reads
  no step, where it printed that call under the step above it.
- A failed scenario none of whose steps carries a result, as a capture need not say, reads `no step is marked failed: none of its steps
  carries a result`, where it said the failure was outside a tracked step.

### Known limits, as before

- `CompleteStep(testId, ...)` closes the step last opened by hand on that test, so steps opened by id on two branches at once pair last
  in, first out.
- A call beside a step that hung reads that step.
- A long-lived worker started before the first step, whose handler is a `[Step]`, shares the test's context, so its step is a row of its
  own with its own bar. Start it under `TestIdentityScope.Detach()`, or from inside a step, and it stands under the step running.
- New here, and rare: a step opened by hand with the `StartStep` that takes no id, in an async helper, is not found by the next step its
  caller starts. Open it by id, as a step in a setup or a hook already is.

## [{previous}] - """)])

elif what == "wiki":
    edit("Step-Tracking.md", [
        ("""- **Run sub-steps one after another.** `StepCollector` keeps one stack of open steps for each scenario, so two sub-steps started together (`Task.WhenAll`) are drawn one inside the other.""",
         f"""- **Sub-steps can run at once.** Each flow of execution knows its own step, so two sub-steps started together (`Task.WhenAll`) are listed side by side in the order they began, each with its own result, duration, assertions and attachments (since {version}). A step still running when the report is drawn is listed with no result. `CompleteStep(testId, ...)` closes the step last opened by hand on that test, so steps opened by id on two branches at once pair last in, first out."""),
        ("""Set `ShowStepDelimiters = false` to suppress delimiter injection while keeping step tracking active:""",
         """Set `ShowStepDelimiters = false` to suppress delimiter injection while keeping step tracking active. A call keeps its step with the delimiters off, in a step or between two, where it stays with the step above it:"""),
        ("""- **Assertions** \u2014 with [[Assertion Tracking]], an assertion is drawn under the innermost step open when it runs, so a failed one shows the sub-step it failed in, and the line.""",
         """- **Assertions** \u2014 with [[Assertion Tracking]], an assertion is drawn under the step it ran in, so a failed one shows the sub-step it failed in, and the line. One from a flow whose step has ended is drawn under the step open then."""),
    ])
    edit("Querying-Reports.md", [
        ("""`… N more attachment(s) (names): attachments s3/2`.

""",
         f"""`… N more attachment(s) (names): attachments s3/2`.

From {version} a step with no result, one still running when the report was written, is marked `?` beside a step of the
scenario that has a result. A capture need not say how its steps ended, so where none has a result, none is marked.

"""),
        ("""prints, with a total beside each capped list (`attachmentsTotal`, `stepAttachmentsTotal`, and on a failing step
`attachmentsTotal` and `callsTotal`).
""",
         f"""prints, with a total beside each capped list (`attachmentsTotal`, `stepAttachmentsTotal`, and on a failing step
`attachmentsTotal` and `callsTotal`).

From {version}, where a scenario failed and no step is marked failed, `failures` and `Failures.md` list a step with no
result in place of a failing step, `? s3/1  name (no result: still running when the report was written)`, and
`**Still running when the report was written**` in the digest. In `--json` and `Failures.jsonl` each entry of
`failingSteps` says which it is, in `stillRunning`. Where none of a failed scenario's steps has a result, as a capture
need not say, it reads `no step is marked failed: none of its steps carries a result`.
"""),
        ("""`failingSteps[]` (each with its `calls[]` and, from 4.24.0, the `attachments[]` that step made; from 4.28.0 `callsTotal` and `attachmentsTotal`), attachments,""",
         f"""`failingSteps[]` (each with its `calls[]` and, from 4.24.0, the `attachments[]` that step made; from 4.28.0 `callsTotal` and `attachmentsTotal`; from {version} `stillRunning`), attachments,"""),
        ("""The footer adds `indented calls ran inside the call above them` when a line is indented.""",
         f"""From {version} `── no step` stands above a call that reads no step, where that call was printed under the step above it.

The footer adds `indented calls ran inside the call above them` when a line is indented."""),
    ])
    edit("Generated-Reports.md", [
        ("""| `SkippedAfterFailure` | ! | Yellow (`#fbc800`) | Cascading non-execution after a prior step failure |

""",
         f"""| `SkippedAfterFailure` | ! | Yellow (`#fbc800`) | Cascading non-execution after a prior step failure |

A step still running when the report was drawn has no status, and is listed with no icon (from {version}).

"""),
    ])

elif what == "ledger":
    date = sys.argv[4]
    p = root / "docs/REMAINING_PARITY.md"
    s = p.read_bytes().decode("utf-8")
    line = f"""- **Steps that overlap (.NET {version}, {date}, `CALLS_UNDER_THEIR_STEP_PLAN.md` R1, #225).** .NET's `StepCollector` has replaced its stack of open steps with a flow of execution (`AsyncLocal`): each flow knows its own step, a step's place among its siblings is kept from its start, and a step still open when the report is drawn is listed with no result. A call's stamp reads its own flow's step, and a service's onward call reads the step of the call it ran inside, by trace. `StepCollector.java` holds the same stack. Report output moves where steps overlap: rows, bars and each call's `stepPath`. To mirror: a `ThreadLocal` is not the same, as Java's async work does not carry one, so the port needs its own answer for what a flow is.
"""
    eol = "\r\n" if "\r\n" in s else "\n"
    lines = s.split(eol)
    last = max(i for i, l in enumerate(lines) if l.startswith("- **") and "(.NET 5." in l)
    lines.insert(last + 1, line.rstrip("\n"))
    p.write_bytes(eol.join(lines).encode("utf-8"))

print("applied", what)
