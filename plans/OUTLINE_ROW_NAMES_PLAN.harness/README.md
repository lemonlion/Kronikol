# OUTLINE_ROW_NAMES_PLAN harness

What `plans/OUTLINE_ROW_NAMES_PLAN.md` measured and read, for #101. Measured on 2026-10-22 with the .NET 10 SDK on
Windows, against Kronikol 5.4.0 from nuget.org, Reqnroll 3.3.4 and xUnit 2.9.3. No report was opened: every name, id
and value was read back through the report's own `query.cs` or `kronikol query`.

## probe/

- `src/xunit2/`: a Reqnroll suite on `Kronikol.ReqNRoll.xUnit2`. `Features/Baskets.feature` is the issue's outline
  verbatim (its third row fails on purpose); `Features/Edges.feature` holds E01-E13, the cases a filled name has to get
  right. `reqnroll.json` turns on Reqnroll's `message` formatter, so the run also writes `cucumber-messages.ndjson`.
  `src/Directory.Build.targets` swaps the package for a checkout's project with `-p:KronikolSource=<checkout>`.
- `run.sh <work-dir> before [version]` runs the suite on the published package (default 5.4.0);
  `run.sh <work-dir> after <checkout>` on a checkout's adapter. The probe is copied into `<work-dir>` first, outside the
  repository, so the repository's `Directory.Build.props` does not apply to it. Answers land in `results/<label>/`.
- `tsv.py <labels...>` turns `results/<label>/query-scenarios-all.json` into `ids.tsv` (feature, values, name, id).
- `ids-check.cs` (`dotnet run --file ids-check.cs`, from `probe/`): which `Compute` inputs reproduce the ids 5.4.0
  wrote, whether Gherkin's rule gives the pickle's name, and each row's id if the filled name were hashed.
  Needs `#:property PublishAot=false`: file-based apps default to AOT, which turns off reflection-based System.Text.Json.

## probe/results/

- `before-5.4.0/`: the Reqnroll lane on 5.4.0 (`test-log.txt`, `Failures.md`, the `query` answers, `ids.tsv`, and the run's
  Cucumber messages).
- `cucumber-lane-5.4.0/`: the same run's Cucumber messages through `kronikol ingest --cucumber-messages` (5.4.0,
  installed with `dotnet tool install Kronikol.Tool --version 5.4.0 --tool-path <scratch>`).
- `ids-check.txt`: 30 of 30 ids reproduced by `Compute(suite, feature, title, outlineId: title, values)` under the
  suite `Probe101.xUnit2`; Gherkin's rule gives the pickle's name on 29 of 29 rows; 27 of 29 ids would move if the
  filled name were hashed.

## research/

Written by three read-only research agents on 2026-10-22, each told to cite file and line and to mark what it inferred.
The plan re-read by hand every line it cites from them.

- `reqnroll.md`: Reqnroll 3.3.4's generated code for xUnit v2, v3 and TUnit, the cases it refuses at generation, the
  embedded pickles and when they vanish, `ScenarioInfo.Title` and `Arguments`. Its scratch projects are outside the repo.
- `name-readers.md`: every reader of a scenario's name in `src/`, sorted into keys and display.
- `gherkin-cucumber.md`: Gherkin's `Interpolate` at 35.0.0, 42.0.1 and the commit the issue cites, quoted, with a probe
  of Gherkin's own compiler; the Cucumber lane; Kronikol4J, and a draft of its ledger line.
