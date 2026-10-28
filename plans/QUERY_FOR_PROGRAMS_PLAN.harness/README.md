# QUERY_FOR_PROGRAMS_PLAN harness

The evidence behind `plans/QUERY_FOR_PROGRAMS_PLAN.md` (#184). The probes are file-based apps run against the
published `Kronikol` package. 5.20.0 was not yet on nuget.org when the audit ran, so they pin 5.19.0. The code paths
they exercise are unchanged in 5.20.0: its changes to `QueryCommand.Overview.cs` are all inside `Parties`.

| File | What it is |
|---|---|
| `probe/gaps.cs` | The issue's own repro, plus a second test with the same name, a different `testId`, a different caller and a failure. `dotnet run --file probe/gaps.cs -- <empty folder>` |
| `probe/sid.cs` | Which verbs take `sid:`, run on the report `gaps.cs` writes (its two scenarios share one `stableId`) |
| `probe/scenario-forms.cs` | Every verb that takes a scenario, given a `sid:` that is unique, and what `compare` does with `sid:`, `sN/iK` and `b:` addresses |
| `results/gaps-on-5.19.0.txt` | `gaps.cs`'s output (local paths shown as `<probe>`) |
| `results/sid-on-5.19.0.txt` | `sid.cs`'s output |
| `results/scenario-forms-on-5.19.0.txt` | `scenario-forms.cs`'s output |
| `results/breakfastprovider-nunit-ids.txt` | Ids, `stableId`s and diagram counts of BreakfastProvider's NUnit lane (5.14.0), read with `kronikol decode` |
| `results/refusals-5.20.0.tsv` | Every message the engine writes to its error writer at 5.20.0 (190 in 17 files), with a first-guess kind from a keyword rule. R3 classifies each by hand. Made by a script that reads `src/Kronikol/Query/*.cs` |
| `results/related-issues.md` | The open issues that break the same four properties, with their dates and asks |
| `results/issue-184.json` | The issue as filed |
