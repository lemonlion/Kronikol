# PARTY_IDENTITY_PLAN harness

Measurements behind `plans/PARTY_IDENTITY_PLAN.md` (#190), taken on 2026-10-26 and 2026-10-27 on Windows 11 26200
with .NET SDK 10.0.300, against Kronikol at `44bcc62a` (5.11.0).

## probe-names/

`probe.cs` is a file-based app that references `src/Kronikol/Kronikol.csproj`. It sends eight requests through the real
`TestTrackingMessageHandler` to a stub inner handler (no network), and prints the `ServiceName` and `CallerName` each
request record carries.

```
cd plans/PARTY_IDENTITY_PLAN.harness/probe-names
dotnet run --file probe.cs
```

`results.txt` is the output (plan §2.1).

## bp-reports/

These scripts read four real reports of BreakfastProvider. They came from CI run `40290999167` on 2026-10-26, written
by Kronikol 5.4.1. The reports are not committed. To fetch and decode them again, use your own scratch directory, never
this folder:

```
for lane in xunit-in-memory xunit-in-docker xunit-external-sut reqnroll-in-memory; do
  gh run download 40290999167 -R lemonlion/BreakfastProvider -n "$lane-report" -D "<scratch>/$lane-report"
  kronikol decode "<scratch>/$lane-report" -o "<scratch>/decoded/$lane-report.json"
done
```

A run's artifacts expire after the repository's retention period. Any later run of the same lanes serves as well; the
counts will differ a little.

| Script | What it prints | Results |
|---|---|---|
| `parties.py` | every `caller > service` edge with calls, categories, attribution and phase; the endpoints behind each name | `results-parties.txt` (plan §2.6, Appendix A) |
| `infer.py` | the read-time inference of plan §5.4: the test party and the subject | `results-infer.txt` |
| `hosts.py` | how many callee names have a real host, a placeholder host or none | `results-hosts.txt` |
| `cases.py` | `infer.py`'s rule on ten synthetic shapes BreakfastProvider does not have | `results-cases.txt` |

Run them with `python -I <script> <decoded.json>...`. `cases.py` takes no arguments.

## compat/

`make-report.py` takes a decoded report and writes two copies. `base/` is the report unchanged. `new/` is the same
report with the plan's `serviceId` and `parties` block after `environment`, and a `testDouble` on every Cow Service
record. `run.sh` runs a Kronikol tool's readers on both copies and says whether each output differs in anything but the
report's path and size.

```
python -I make-report.py <scratch>/decoded/xunit-in-memory-report.json <scratch>/compat
bash run.sh <path to kronikol> <scratch>/compat 5.11.0
```

`results.txt` covers the tool built from `44bcc62a` and Kronikol.Tool 5.4.1. 5.4.1 was installed into a scratch
folder with `dotnet tool install Kronikol.Tool --version 5.4.1 --tool-path <scratch>/tool541`, never globally (plan
§5.5, A1).

`results-5.16.0.txt` runs the same check on a report a release wrote (plan section 7.5). `real-report.cs` writes it
with the released Kronikol 5.16.0, its parties declared and four calls marked, and `strip-report.py` makes the base
by taking `serviceId`, `parties` and `testDouble` out of the decoded file. The four tools (5.4.1, 5.12.1, 5.13.0 and
5.14.0) were installed with `--tool-path` into a scratch folder.

```
dotnet run --file real-report.cs -- <scratch>/reports
kronikol decode <scratch>/reports/TestRunReport.krkl -o <scratch>/decoded.json
python -I strip-report.py <scratch>/decoded.json <scratch>/pair
bash run.sh <path to kronikol> <scratch>/pair 5.4.1
```

## sweeps/

`trackers.md` records how every capturer in `src/` names its two parties (plan Appendix B). A read-only sweep made it,
and the lines the plan relies on were checked by hand.
