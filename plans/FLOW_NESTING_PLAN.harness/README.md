# FLOW_NESTING_PLAN harness

The scripts behind every number in [`../FLOW_NESTING_PLAN.md`](../FLOW_NESTING_PLAN.md), with the output
they printed on 2026-09-26. Python 3, standard library only. Each script reads reports with `json.load`
and prints counts, addresses and `flow` lines; none prints a body or a header.

| Script | What it answers | Output |
|---|---|---|
| `nesting_census.py` | How many requests ran while another was open, under the plain "innermost open request" rule (R1), and every shape that breaks it: responses out of order, requests never answered, a call nested under an unrelated one | `results-census.txt` |
| `nesting_rules.py` | The four candidate rules side by side (plan §3.1): requests nested, depth histogram, and how often indentation alone would point at the wrong parent | `results-rules.txt` |
| `dump_records.py` | One scenario's records in capture order, ids and names only: how a response repeats its request's caller and service, which calls share a `traceId`, which carry no timestamp (plan §2.2) | printed |
| `flow_prototype.py` | `flow` today (`--mode flat`) and as proposed (`--mode nested`), for one scenario, with `--step`, `--service` and `--errors-only` | `results-examples.txt` |
| `flow_bytes.py` | What nesting costs over whole reports: bytes before and after, indented lines, deepest level, `inside` references in unfiltered views | `results-bytes.txt` |
| (shell loop below) | That `--mode flat` is today's `flow`: diffed against the real tool, trailing whitespace ignored | `results-fidelity.txt` |
| `trailing-annotation-probe.json` | A two-record report with an annotation after its last call (plan F7) | in `results-examples.txt` |

## The corpus

Every `TestRunReport.json` on the machine the plan was written on, `runs/` and `baseline/` copies
excluded: 1,134 scenarios, 7,183 requests.

- BreakfastProvider (`C:/Code/BreakfastProvider`), the external consumer: five full lanes (BDDfy,
  LightBDD, NUnit, ReqNRoll, TUnit; 178 to 205 scenarios each) and a one-scenario xUnit run, all
  written by Kronikol 3.0.83 on 2026-09-05.
- The in-repo examples (`examples/Example.Api/tests/*`), fifteen reports written by 3.0.77 to 3.28.0.
- `tests/Kronikol.Tests` (one scenario, no calls) and fifteen Kronikol4J module reports (66 scenarios, no
  calls: the Java port writes the fields, but none of these runs captured a call).

## Re-running

```bash
cd plans/FLOW_NESTING_PLAN.harness
REPORTS=$(cd /c/Code && find "$PWD/BreakfastProvider" "$PWD/Kronikol/examples" "$PWD/Kronikol/tests/Kronikol.Tests" "$PWD/Kronikol4J" \
  -name TestRunReport.json -not -path "*/runs/*" -not -path "*/node_modules/*" -not -path "*/baseline/*")
PYTHONUTF8=1 python nesting_census.py $REPORTS > results-census.txt
PYTHONUTF8=1 python nesting_rules.py  $REPORTS > results-rules.txt

B=/c/Code/BreakfastProvider/tests/BreakfastProvider.Tests.Component
PYTHONUTF8=1 python flow_bytes.py $B.{ReqNRoll,BDDfy,LightBDD,NUnit,TUnit}/bin/Debug/net10.0/Reports/TestRunReport.json > results-bytes.txt

# One scenario, today and proposed:
PYTHONUTF8=1 python flow_prototype.py $B.ReqNRoll/bin/Debug/net10.0/Reports/TestRunReport.json s26 --mode flat
PYTHONUTF8=1 python flow_prototype.py $B.ReqNRoll/bin/Debug/net10.0/Reports/TestRunReport.json s26 --service CosmosDB

# Fidelity of --mode flat against the real tool (K = a built Kronikol.Tool.exe):
for lane in ReqNRoll BDDfy; do D=$B.$lane/bin/Debug/net10.0/Reports; for s in s26 s57 s59 s103 s165; do
  $K query flow $D $s --max-bytes 0 > real.txt; PYTHONUTF8=1 python flow_prototype.py $D/TestRunReport.json $s --mode flat > proto.txt
  diff -q <(sed 's/[[:space:]]*$//' real.txt) <(sed 's/[[:space:]]*$//' proto.txt) > /dev/null && echo "$lane $s identical" || echo "$lane $s DIFFERS"
done; done
```

`results-fidelity.txt` and `results-examples.txt` were taken with `Kronikol.Tool` 3.29.6, built from
`6c689b5e`. Against 3.29.3 the fidelity check differs on s103, whose step text 3.29.3 read wrongly
(fixed in 3.29.6); that is the tool's old defect, not the prototype's.

Once S2 is built, run the same corpus through the real `flow` rather than the prototype (plan §7.5).
