# WARM_UP_PLAN harness

The executable spec and the measurements behind [`WARM_UP_PLAN.md`](../WARM_UP_PLAN.md) (#113). Written 2026-10-06 at
Kronikol 4.6.0 (`37e93813`). Python 3.10 or later, standard library only; the culture probe needs the .NET 10 SDK.

## Files

| Path | What it is |
|---|---|
| `warmup.py` | The rule of the plan's section 4.1 (variant `P`), the variants it was chosen from (`V0` is #113's rule as written; `V3`, `PU`, `P5`, `P20`, and `PS` and `PL` for the plan's Q10), and one subcommand per measurement. Its path templating is `InteractionShape.Template` (Version 4) ported regex for regex. Run `python warmup.py -h` |
| `corpus/ci-2026-10-05/` | BreakfastProvider's 18 published reports of CI run `gh:37292325012:1` (2026-10-05, Kronikol 4.0.2, BreakfastProvider `106702b`), reduced by `warmup.py extract` to the fields the rule reads: per scenario its name, feature, result, `durationSeconds` and `endedAt`; per call its caller, service, method, URI, metaType, category, start, duration and status. Bodies, headers and diagrams are dropped. The site is overwritten by every nightly run, so this copy is the only one |
| `corpus/local-windows/` | The same suite run on this machine (Windows 11, .NET SDK 10.0.300, Kronikol 4.0.2 packages): ReqNRoll three times, xUnit twice, and xUnit once with each warm-up of the plan's section 4.8 |
| `corpus/example-api/` | The eleven `examples/Example.Api` component suites' reports as they sat in the repository's build output (written 2026-09-04 to 2026-09-23, Kronikol 3.0.77 to 3.28.0): a second app, a second team's code paths |
| `results/` | What each subcommand printed, named for it (`variants.txt`, `truth-*.txt`, `table-*.txt`, `stability.txt`, `slowest.txt`, `misses.txt`, `totals.txt`, `marks-*.txt`, `ledger-*.txt`, `culture.txt`, `corpus.txt`) |
| `breakfastprovider/runlanes.sh` | Builds a BreakfastProvider clone once and runs lanes several times, copying each `TestRunReport.json` out. `KRONIKOL_HISTORY=off`, so nothing is recorded |
| `breakfastprovider/warmup-query.patch` | The section 4.8 change to BreakfastProvider's `StartupExtensions.cs`. The `InitializeOnStartup()` run is the same line without the `warmup:` argument |
| `culture/culture.cs` | F12: what four duration formats write on a comma-decimal machine. `dotnet run --file culture.cs` |

## Re-running

```bash
python warmup.py variants corpus/ci-2026-10-05                     # results/variants.txt
python warmup.py truth corpus/ci-2026-10-05 --list P               # in-memory lanes, results/truth-in-memory.txt
python warmup.py truth corpus/ci-2026-10-05 --lanes docker_xunit,docker_nunit,docker_tunit,docker_reqnroll,docker_lightbdd --list P
python warmup.py table corpus/ci-2026-10-05 --variant P            # the issue's six scenarios
python warmup.py stability corpus/local-windows/ReqNRoll-1.calls.json.gz corpus/local-windows/ReqNRoll-2.calls.json.gz corpus/local-windows/ReqNRoll-3.calls.json.gz
python warmup.py marks corpus/local-windows                        # what each run marks, the warm-up runs included
git -C <BreakfastProvider clone> show c2e7db61:history.jsonl > history.jsonl
python warmup.py ledger history.jsonl 2026-09-14 2026-09-30T23:59:59Z
```

`marks`, `table`, `slowest`, `misses` and `totals` take `--variant`. Every subcommand also reads full
`TestRunReport.json` files, so a newer run of the consumer can be measured the same way: point it at a folder of reports.

## The acceptance it serves

Execution's S7 (plan sections 6.1 and 6.6) runs the built Kronikol on the consumer and compares what its
`TestRunReport.json` marks with `warmup.py marks` over the same file, call for call. The two must agree; a difference is
classified before it is called a bug. The control is the same comparison on a report the previous release wrote, which
carries no marks: it must fail and list every mark the script finds, or the comparison cannot catch anything.

## Traps met

- A Bash heredoc on this machine eats a backslash, so the scripts were written with the Write tool.
- BreakfastProvider's live reports are replaced every night and its CI deletes each lane's report artifact once Pages is
  deployed: call-level data for an older run cannot be fetched again. Reduce what you measure into the corpus the same day.
- A local component run rewrites `docs/Specifications.yml`; `runlanes.sh` restores `docs/` after each run.
- Run lanes one at a time: the in-memory fakes bind fixed ports.
