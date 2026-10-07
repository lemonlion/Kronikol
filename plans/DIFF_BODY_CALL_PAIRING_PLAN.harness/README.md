# Harness for `DIFF_BODY_CALL_PAIRING_PLAN.md`

Read on 2026-10-06 at Kronikol 4.6.0 (`main` at `37e93813`). Every line number in the plan is for that commit.

| Path | What it is |
|---|---|
| `research/pairing.py` | The measurement and the executable spec of the rule. `python pairing.py OLD.json NEW.json [--examples N]` matches scenarios as `CrossRunBodyDiff` does, then, for every interaction in the old scenario that carries a body (every address `--body` accepts), says what 4.6.0's ordinal lookup pairs it with, what issue #115's literal key pairs it with, and what the plan's recommended rule (section 4) pairs it with. It reads the report JSON directly and prints counts, addresses and URIs, never a payload. `InteractionShape.Template` and `TemplateStatement` are ported from `src/Kronikol/History/InteractionShape.cs` (rule version 4) |
| `research/results-4.0.2-two-runs.txt` | Its output for two runs of BreakfastProvider's xUnit lane on Kronikol 4.0.2, three minutes apart, same commit, same machine: the same-version pair |
| `research/results-3.20.0-vs-3.27.0.txt` | Its output for two retained runs of the same lane on 3.20.0 and 3.27.0 (2026-09-19 and 2026-09-22): a pair across an upgrade, where 3.27.0 records more calls |
| `research/repro-4.6.0.txt` | The 4.6.0 tool's own answers on those reports, command by command with exit codes: the wrong pairs (§1, §2), the identical labels under `--baseline` (§3), the missing provenance note (§4), the run diff that ignores `--count` (§5), `compare`'s hint (§6), and from the second
pass the flags and positionals `diff` drops (§7, §10), the run diff's ignored `--offset` and `--limit` (§8) and `--body`'s
value reaching `http` (§9). `noids.json` is `unenriched.json` with its `stableId` removed. The `--baseline` layout is the same-version pair copied to `TestRunReport.json` and `baseline/TestRunReport.json`; `unenriched.json` is the 3.0.44-shaped report `QueryCommandTests.UnenrichedReport` writes, with a second call |

Added when the plan was executed (2026-10-07, 4.6.1):

| Path | What it is |
|---|---|
| `r1/red-4.6.0.txt` | Every fact of section 5 run against the `v4.6.0` tag before the code: what failed, and why |
| `mutations/mutate.py`, `mutations/results.txt` | 32 mutations of the built code, each with the facts it turned red: 32 of 32 killed |
| `s6/` | Acceptance: `acceptance.cs` runs the built engine (or the 4.6.0 control) on every address of both pairs of reports and compares its partner with `pairing.py --design`; `README.md` has the result |
| `s7/` | Producers: `producers.py` (two runs through `kronikol ingest` and `kronikol merge`) and `Kronikol4JRuns.java` (two through Kronikol4J's serializer), checked the same way; `README.md` has the result |

`research/pairing.py` gained, in execution, the shape tier's rule as built (`unpaired`, `exact=`, F19 in the plan) and
`--design FILE`, which writes the rule's answer for every address.

## The reports

The reports are not committed: they are 4 to 6 MB each and BreakfastProvider's. To make them again:

```bash
git clone https://github.com/lemonlion/BreakfastProvider bp && cd bp    # measured at 106702b, pins Kronikol 4.0.2
P=./tests/BreakfastProvider.Tests.Component.xUnit/BreakfastProvider.Tests.Component.xUnit.csproj
R=tests/BreakfastProvider.Tests.Component.xUnit/bin/Debug/net10.0/Reports
dotnet test "$P" && cp "$R/TestRunReport.json" ../xunit-4.0.2-a.json        # 212 passed, about 3 minutes
dotnet test "$P" --no-build && cp "$R/TestRunReport.json" ../xunit-4.0.2-b.json
PYTHONUTF8=1 python research/pairing.py ../xunit-4.0.2-a.json ../xunit-4.0.2-b.json
```

The two older reports were retained runs (`Reports/runs/local_20260919T040511Z_14e947b5` and
`local_20260922T080715Z_14e947b5`) of a local BreakfastProvider checkout. Any two runs of one suite will do; the
numbers in the plan are for these four files.

## Reading the output

Each `ordinal:` line is what 4.6.0 does: `same key` is the right call, `a different call` is the defect,
`same shape, different URI` is the right call with a regenerated id, `out of range` is a call the new scenario
does not reach. Each `key:` line is the literal rule (half, service, method and the full URI); `path key:` is
the same with the URI as `interactions` lists it (path and query). `design:` is the plan's recommended rule:
the exact key through the request, then the key with the URI templated, else a refusal. `tie (...)` lines count
the addresses whose key several calls in one scenario share, which the n-th rule decides by order; `statement`
means a statement-shaped dependency, where the statement, not the URI, says which call it is.
