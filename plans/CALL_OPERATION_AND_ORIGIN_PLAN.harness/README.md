# Harness for CALL_OPERATION_AND_ORIGIN_PLAN.md (#192)

Written 2026-10-26 at 5.11.0 (`44bcc62a`); `ledger/` and `route/` added 2026-10-27 at 5.12.1 (`79e8d644`). Everything
here measures the design; nothing here is product code.

| Path | What it is | How to run it |
|---|---|---|
| `probes/graphql-detector.cs` | `GraphQlOperationDetector.TryExtractLabel` over the bodies real clients send, beside the ones its tests cover (F4) | `dotnet run --file graphql-detector.cs` from `probes/` |
| `probes/service-case.cs` | `services` and `interactions --group-by service` on two calls whose service names differ only in case (F7) | `dotnet run --file service-case.cs -- <empty folder>` from `probes/` |
| `measure/measure.cs` | bucket counts per report: today's `path`, the history templater, the issue's rule, what each folds, the GraphQL census, callers and attribution (section 3.6) | see the header of the file |
| `stand-ins/` | `jest-ingest-TestRunReport.krkl`, a Jest ingest made from `plans/INGEST_FIDELITY_PLAN.harness/otel-jest` through the wiki's recipe and `kronikol ingest` (5.11.0); `report-calls.js`, `capture-shape.js`, `json-shape.js`, the counting helpers used to check it | `node report-calls.js <TestRunReport.json>` |
| `ledger/` | 2026-10-27, for section 4.8: `census.cs` (the ledger's lines, versions, call lists, and every stored set fingerprint rebuilt from its call lines), `move46.cs` and `translate46.cs` (the real 4 to 6 move, and how far a translation of stored lines brings it back), `masschange.cs` (the 2026-10-25 run that changed 946 positions), `resetcost.cs` (what a version move costs the verdicts, replayed), `loadtime.cs` (reading with and without call lists) | `dotnet run --file <script>.cs -- <history.jsonl> [args]` from `ledger/`; `resetcost.cs` takes how many runs back to place the move (10 was run), `loadtime.cs` a work folder. The ledger is BreakfastProvider's `kronikol-history` branch, `e61cd2d7` (2026-10-27), not committed here: `git clone --single-branch --branch kronikol-history --depth 1 https://github.com/lemonlion/BreakfastProvider` |
| `route/` | 2026-10-27, the operation key of 4.1 and 4.2 prototyped (`Key.cs`, `Gql.cs`: the root-field scanner and the strict reader) and measured (`Measure.cs`), and every body of `GraphQlOperationDetectorTests.cs` read through it (`Corpus.cs`) | `dotnet build -c Release route.csproj`, then `dotnet bin/Release/net10.0/Kronikol.Tests.PlantUml.Ikvm.dll corpus <out>` or `... measure <out> <label>=<file>[;<krkl>] ...`. The assembly takes a name `Kronikol.csproj` grants `InternalsVisibleTo`, so it can check its copies of `PathOf` and `StatementHead` against the originals; it is not a test project, and CI's discovery reads `tests/` only |
| `q18/` | 2026-10-28, for Q18: what the fragment's `rawCalls` block adds to `History.run.json`, per report (a fragment written with and without it, raw and gzipped), over a non-mergeable `.krkl` read through `ReportScanner`'s index | `dotnet run -c Release -- <label>=<report file> ...` from `q18/`; prints counts and byte sizes only, one row per report, into `results/q18-raw-calls.txt` when redirected there |
| `kept-rules/` | 2026-10-28, for 4.8's guard: `ShapeGuardTests`' corpus read by rules 6 and 7, once by 5.16.4 (`f33ed3e9`) from each capture as its trackers recorded it (a `RecordedBefore8` mark's method and address, a call marked not recorded left out) and once by 5.17.0 from the raw call, without patterns and with nine of the kinds a consumer writes | `python make-probe.py Program.cs` then `dotnet build kept-rules.csproj -p:KronikolProject=<5.16.4 checkout>/src/Kronikol/Kronikol.csproj` and `dotnet bin/Debug/net10.0/Kronikol.Tests.PlantUml.Ikvm.dll <out>`; for 5.17.0, `python make-probe.py Program.cs new` and build without the property. The two outputs are byte-identical |
| `results/` | what each script printed; `results/measure/` for BreakfastProvider's first five reports and the example, `results/measure-stand-ins/` for the Jest ingest and four more BreakfastProvider lanes (nunit, tunit, lightbdd, bddfy in-memory, from `gh run download 40290999167 --repo lemonlion/BreakfastProvider -n <lane>-report`), `results/ledger/` for `ledger/` and `results/route/` for `route/` | |

The scripts reference `src/Kronikol/Kronikol.csproj` by a relative path, so they build the library of whichever
checkout holds them. `#:property PublishAot=false` because a file-based app turns reflection-based JSON off by default.

The measurement prints counts, shapes and path segments, never a whole body: at most a 200-character excerpt of a
GraphQL body the detector misses (none, on the reports measured). Reports from a company's own suites can still
hold service names in their paths: share their output only as counts.

Two sibling plans measured the same BreakfastProvider CI run (40290999167, Kronikol 5.4.1) for origin, and section 3.5
cites their scripts rather than repeating them:

- kronikol-ed, `C:/Code/Kronikol-191/plans/SERVICE_CARD_PLAN.harness/probes/card-prototype.cs` (the flow nesting
  ported, with `InteractionShape` v6 for the targets);
- kronikol-67, `C:/Code/Kronikol-190/plans/PARTY_IDENTITY_PLAN.harness/bp-reports/` (`parties.py`, `infer.py`,
  `hosts.py`, `cases.py` and their `results-*.txt`: attribution, phase and callers per edge; the party inference),
  over JSON decoded with `kronikol decode` from that run's artifacts.
