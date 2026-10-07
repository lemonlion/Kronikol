# PHASE_FROM_STEPS_PLAN harness

`s0/` holds the S0 measurement of issue #130 on 4.6.0 (`main` at 37e93813), taken on 2026-10-06.

- `tests.ndjson` and `calls/calls.ndjson`: the issue's repro. One test with one 10 ms `When` step, and two
  calls. Call `a` is answered inside the step. Call `b` is a database read answered at 10:00:02.000, after
  the step and the test have ended.
- `run_one.sh <label> <out> <ingest flags…>`: runs one ingest from this folder, then prints the files
  written, `Run.json`, each interaction's phase (through `inspect.py`) and four `kronikol query` answers.
  `TOOL` must name a `Kronikol.Tool.dll` copied **outside any git checkout**. Run from a repository's
  `bin/`, the tool resolves the history ledger from its own folder first (`HistoryPathResolver.cs:69`) and
  appends this run to that repository's `.kronikol/history.jsonl`. That happened once while measuring
  (plan §8, item 1); the three lines were removed.
- `outputs/`: the saved query answers. A is `--phase-from-steps --diagnostics-section`, B is
  `--phase-from-steps`, C has no phase flag, D is `--phase-from-steps --separate-setup`, and Bgit is B run
  in a folder after `git init`.

```bash
dotnet build src/Kronikol.Tool -c Debug
cp -r src/Kronikol.Tool/bin/Debug/net10.0 "$TEMP/k-tool"     # outside the checkout
TOOL="$TEMP/k-tool/Kronikol.Tool.dll" plans/PHASE_FROM_STEPS_PLAN.harness/s0/run_one.sh B outB calls/calls.ndjson --tests tests.ndjson --phase-from-steps
```

`inspect.py` reads one field set out of `TestRunReport.json` and prints only that. Never open the report
itself.
