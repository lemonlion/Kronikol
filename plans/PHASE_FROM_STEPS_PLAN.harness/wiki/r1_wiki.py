"""R1's wiki edits (plan section 6), applied at release to a checkout of Kronikol.wiki.

Each edit is an exact replacement that must match once; line endings are kept as the file has them.

    python r1_wiki.py <wiki-checkout> <version>
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
VERSION = sys.argv[2]

EDITS = {
    "Ingesting-External-Captures.md": [
        # The flag's row.
        ("| `--phase-from-steps` | off | Give interactions the phase of the `Given`/`When`/`Then` step they happened during: "
         "each call's `phase` in the data files and for `kronikol query`. It draws nothing;",
         "| `--phase-from-steps` | off | Give interactions the phase of the `Given`/`When`/`Then` step their call started in "
         "(a response takes its request's phase): each call's `phase` in the data files and for `kronikol query`, and a "
         "line saying how many records took one. It draws nothing;"),
        # Phases from steps: the pair rule, the capturer rule, F3 and the console line.
        ("`--phase-from-steps` (`IngestRequest.PhaseFromSteps`) gives an interaction the phase of the top-level step\n"
         "whose window contains it: `Given`/`Context` → `Setup`, `When`/`Then` (`Action`/`Outcome`) → `Action`,\n"
         "`And`/`But`/`Conjunction` inherit the previous step's phase. A step's window is its `timestamp` plus its\n"
         "`durationMs`. Only records whose own `phase` is absent or `Unknown` are touched, so a capturer that knows\n"
         "better still wins.",
         "`--phase-from-steps` (`IngestRequest.PhaseFromSteps`) gives an interaction the phase of the top-level step\n"
         "its call started in: `Given`/`Context` → `Setup`, `When`/`Then` (`Action`/`Outcome`) → `Action`,\n"
         "`And`/`But`/`Conjunction` inherit the previous step's phase. A step's window is its `timestamp` plus its\n"
         "`durationMs`. A request and its response (the records sharing a `requestResponseId`) take one phase, judged\n"
         "on the pair's earliest record as `--run-window` judges it, so a response answered after its step ended takes\n"
         f"its request's phase (until {VERSION} it took none, or the next step's). A record with no `requestResponseId` is\n"
         "judged on its own `timestamp`. Only records whose own `phase` is absent or names no phase are touched (`Unknown`,\n"
         f"or a value such as `Teardown`, which until {VERSION} was kept and then read as `Unknown`), so a capturer that\n"
         "knows better still wins, half by half, and a phase it wrote on either half is the pair's (the request's, when\n"
         "both halves carry one)."),
        ("a hand-written `kind: marker`, `markerKind: Phase` line. The partition is `--separate-setup`'s, below.",
         "a hand-written `kind: marker`, `markerKind: Phase` line. The partition is `--separate-setup`'s, below.\n"
         "\n"
         "`kronikol ingest` prints how many records took a phase, as a line after `Replayed …`, zero included:\n"
         "`--phase-from-steps: 4 interaction record(s) took the phase of the step their call started in.` Zero is the\n"
         "value worth a look: steps written without `durationMs` are instants, so no call falls inside one. The count is\n"
         f"not a diagnostic; until {VERSION} it was one, of kind `Other`, so every `kronikol query` answer carried it."),
        # The Diagnostics table: Other's example, and the kinds an ingest can record that the table lacked.
        ("| `Other` | Anything without a dedicated kind yet — e.g. how many records took their phase from a step. |",
         "| `StepAttributionMismatch` | A step's delimiter did not line up with the step it should have opened, so the calls after it carry no `stepPath`. |\n"
         "| `ResultDefaulted` | Scenarios that started and never reported an end took `ResultWhenUnknown` (by default `Passed`) instead of a verdict, and how many. |\n"
         "| `BackgroundCalls` | Calls made under a scenario's identity after it ended, moved to no scenario and listed in the report's background section; one entry per scenario. |\n"
         "| `HistoryUnavailable` | Cross-run history was not read or not written: no ledger found, a newer format, a lock held past the retries, or a companion file that could not be read. The message says what to do. |\n"
         "| `HistoryPartialRun` | The run lacked more than `HistoryPartialThreshold` of the previous run's scenarios and was recorded as partial. |\n"
         "| `HistoryLedgerDamaged` | Lines of the history ledger could not be parsed and were skipped; `kronikol history verify` says which. |\n"
         "| `HistoryShapeTemplate` | A `HistoryShapeTemplates` rule did not compile, or timed out, and was skipped. |\n"
         "| `ReportRotationFailed` | The previous run's files could not be moved to `runs/<run>/`, or a run past `KeepRuns` could not be removed. The message names the file. |\n"
         "| `OptionNotApplied` | An option was set that the run's configuration ignores, such as a PlantUML theme under browser or Node rendering. The message names it. |\n"
         "| `Other` | Anything without a dedicated kind yet, e.g. that no run window could be derived, so `--run-window` dropped nothing. |"),
    ],
    "Phase-Aware-Tracking.md": [
        ("an ingested run, `kronikol ingest --phase-from-steps` gives each call the phase of its step, and",
         "an ingested run, `kronikol ingest --phase-from-steps` gives each call the phase of the step it started in, and"),
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
