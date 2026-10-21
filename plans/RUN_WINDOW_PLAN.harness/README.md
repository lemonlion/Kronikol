# RUN_WINDOW_PLAN harness

Probes for [`RUN_WINDOW_PLAN.md`](../RUN_WINDOW_PLAN.md) (#187), run on 2026-10-21 with the .NET 10.0.300 SDK on Windows
11. Each is a .NET 10 file-based app. Run them from a folder **outside the repository** (copy the `.cs` file there
first), so that the repository's `Directory.Build.props` is not read into the app.

| Probe | Runs on | What it shows | Answer |
|---|---|---|---|
| [`p1-two-runs.cs`](p1-two-runs.cs) | Kronikol 5.2.3 (nuget.org) | The issue's shape: a stack's start-up, a browser run, two real losses and a device run in one capture, one tests file per run; each run ingested as the issue's host ingests it, then the two workarounds (C: `RunStartedAt` at the stack's start; D: also `DropUnattributed` on the database) | [`results/p1.txt`](results/p1.txt) |
| [`p2-edges.cs`](p2-edges.cs) | Kronikol 5.2.3 | a. a run with no end marker; b. an explicit window that ends before it starts; c. the same suite twice with stable test ids, with and without the window; d. a Phase boundary after a scenario's last call with `SeparateSetup` | [`results/p2.txt`](results/p2.txt), [`results/p2d-fold.puml`](results/p2d-fold.puml) |
| [`p3-prototype.cs`](p3-prototype.cs) | the prototype (below) | P1's capture with each run's windows declared: A' and B' (start-up kept, the other run left), B'' (the start-up declared as the browser run's), E (`DropUnattributed` not asked about the start-up), F (`SeparateSetup`: the Setup partition), G (an open end closed where the device run began), H (a setup window over everything before the device run, the browser run declared inside it) | [`results/p3.txt`](results/p3.txt), [`results/p3f-setup.puml`](results/p3f-setup.puml), [`results/p3-report-grep.txt`](results/p3-report-grep.txt) |

| [`p4-merge.cs`](p4-merge.cs) + [`p4-merge.sh`](p4-merge.sh) | Kronikol 5.2.3 and Kronikol.Tool 5.2.3 (`dotnet tool execute`) | P1's browser run ingested with a fold and `GenerateMergeableData`, its own ledger written; then `kronikol merge --history` against a copy of that ledger | [`results/p4.txt`](results/p4.txt) |
| [`p5-strip-setup.js`](p5-strip-setup.js) | node, the page's `stripSetup` at `9d89b793` | The page's Setup filter on the start-up's diagram (P3 F's `.puml`) | [`results/p5.txt`](results/p5.txt) |

```bash
mkdir -p <scratch>/probes && cp plans/RUN_WINDOW_PLAN.harness/p1-two-runs.cs plans/RUN_WINDOW_PLAN.harness/p2-edges.cs <scratch>/probes/
cd <scratch>/probes
dotnet run --file p1-two-runs.cs -- <scratch>/p1-out     # writes capture.ndjson and the two tests files there, then the reports
dotnet run --file p2-edges.cs -- <scratch>/p2-out
```

The `.puml` answers were read with the `query.cs` each report writes beside itself (the tool need not be installed):
`dotnet run --file <report dir>/query.cs -- diagram <report dir> s0/d0 --out <file>`, the scenario's address taken from
`query.cs -- summary <report dir>`. The report files were never opened; `p3-report-grep.txt` holds the run band's,
the timeline's and the features' labels, matched by pattern.

## The prototype

R2's core of the plan (section 3.3 to 3.6 and the setup scenario's feature, time and boundary), on `src/Kronikol` at
`9d89b793` (5.2.3). It is a measuring device, not the implementation: it has no CLI flags, no validation (section 3.9),
none of R1, and its message is section 3.6's figures in today's order.

```bash
mkdir -p <scratch>/proto && cd <scratch>/proto
git -C <repo> archive --format=tar 9d89b793 src/Kronikol Directory.Build.props global.json THIRD-PARTY-NOTICES.md | tar -x --force-local -f -
git apply <repo>/plans/RUN_WINDOW_PLAN.harness/prototype.diff      # or patch -p1
dotnet build src/Kronikol/Kronikol.csproj -c Debug -f net10.0       # about 15 s; only the core project
mkdir -p <scratch>/probes3 && cp <repo>/plans/RUN_WINDOW_PLAN.harness/p3-prototype.cs <scratch>/probes3/
cd <scratch>/probes3 && dotnet run --file p3-prototype.cs -- <scratch>/p1-out <scratch>/p3-out
```

`p3-prototype.cs` finds the project at `../proto/src/Kronikol/Kronikol.csproj`, so `probes3/` and `proto/` sit side by
side, and it reads P1's capture and tests files from P1's output folder.

The existing test suite was not run against the prototype (plan section 4.5).
