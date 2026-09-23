# S3 one-off run: the LightBDD xUnit3 example, projected and ingested (2026-09-22)

Run in a detached worktree at `25a0773a` (3.27.3) with `s2.prototype.patch` and
`s3.projection-hook.patch` applied. The worktree was removed afterwards; these files are the record.

## What ran

1. `examples/Example.Api/tests/Example.Api.Tests.Component.LightBDD.xUnit3` with
   `KRONIKOL_PROJECT_NDJSON=<scratch>/lightbdd.ndjson`. The hook (`s3.projection-hook.patch`) is a
   `RegisterGlobalTearDown` in `ConfiguredLightBddScope` that writes every entry of
   `RequestResponseLogger.RequestAndResponseLogs` through `NdjsonInteractionWriter`. 6 tests passed in
   583 ms. The suite's own report is the reference (`bin/Debug/net10.0/Reports/TestRunReport.json`,
   124,519 bytes; it was never opened, only processed by `compare-reports.py`).
2. The capture: 85 lines, 6 test ids; 49 marker halves (`Step` 40, `Custom` 6, `Phase` 3; no
   `Row` and no `Assertion` were emitted by this suite, so those two kinds are measured only by the
   synthetic fixture); 36 interaction lines (18 pairs); 0 `ui`, 0 `durationMs`.
3. Ingest A, like for like: `P1RealSuiteIngestTests.cs` runs `IngestPipeline.Run` with the suite's
   own options (`SpecificationsTitle`, `SeparateSetup = true`, everything else default) and
   `CallTreeOrdering = false`, no tests file. `Replayed=85 scenarios=6`; one diagnostic,
   `ResultDefaulted` (no end records); 49 marker logs in the store, `Step=40, Phase=3, Custom=6`.
4. Ingest B, the tool's defaults: `dotnet Kronikol.Tool.dll ingest lightbdd.ndjson -o <dir>
   --chronological`. Exit 0; `Replayed 85 interaction record(s) into 6 scenario(s).`; the same
   `ResultDefaulted` diagnostic; and `Warning: InternalFlowSpanStore has 0 spans — activity diagrams
   will be empty.`
5. `compare-reports.py <suite report> <ingested report> --ignore error,stepPath`, twice.

## Result

| Ingest | Diagrams byte-identical | `httpInteractions` | `annotations` |
|---|---|---|---|
| A, suite options | **6 of 6** (570, 4116, 2479, 2737, 579, 576 chars) | 14/14, 10/10, 12/12, 0/0, 0/0, 0/0; every member identical except `attributionSource` (`TestContext` in-process, `None` ingested: the pinned member) | identical |
| B, tool defaults | 3 of 6; the three scenarios with calls differ only where the in-process run's `SeparateSetup` drew `partition #F6F6F6 Setup` … `end` (the CLI has no flag for it) | as A | identical |

`steps` is 3 or 4 in-process and 0 ingested: no tests file was given, which §6 step 3 of the plan
states as the known limit until 14.1 writes tests NDJSON. `stepPath` was therefore excluded from
the comparison. The three "muffin" scenarios have no interactions; their diagrams (step bars and the
no-interactions marker) still compare identical.

Nothing in the run was unexplained by a row of §3.2 or an option difference; no new row was needed.

## Files

- `real-suite-compare-suite-options.txt`, `real-suite-compare-cli-defaults.txt`: the script's output.
- `compare-reports.py`: the comparison, prints counts and first differing lines only.
- `P1RealSuiteIngestTests.cs`: ingest A (inert unless `P1_NDJSON` and `P1_OUT` are set).
- `s3.projection-hook.patch`: the hook.

## The same run on the shipped code (2026-09-23, before 3.29.0)

Repeated on the working tree that became 3.29.0 (S2 as shipped, the projection hook now permanent
in `ConfiguredLightBddScope`, the tool built from the same tree), the same three steps. The suite: 6
passed, 580 ms; the capture 85 lines, 49 marker halves (`Step` 40, `Custom` 6, `Phase` 3), 36
interaction lines. Ingest A (the suite's options, `CallTreeOrdering = false`, no tests file):
`Replayed=85 scenarios=6`, one `ResultDefaulted` diagnostic, 49 marker logs in the store
(`Step=40, Phase=3, Custom=6`); **6 of 6 diagrams byte-identical** (553, 2714, 4081, 562, 2459, 559
chars), `annotations` identical, `httpInteractions` 12/12, 14/14, 10/10 and 0/0 three times, every
member identical except `attributionSource` (`TestContext` in-process, `None` ingested), `steps` 3 or
4 against 0 (no tests file). Ingest B (`kronikol ingest … --chronological`, the tool's defaults):
exit 0, `Replayed 85 interaction record(s) into 6 scenario(s).`, **no `InternalFlowSpanStore` warning
any more** (F8, 3.27.4), 3 of 6 diagrams identical, the three with calls differing only by the
`partition #F6F6F6 Setup` … `end` lines the command line cannot ask for (Q11). Nothing a row of §3.2
or an option difference does not explain.
