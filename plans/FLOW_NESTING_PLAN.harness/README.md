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
| `s1_acceptance.py` | S1's acceptance (plan §7.1): the built `flow` on every scenario of the five lanes, unfiltered, with `--service CosmosDB` and `--step 1`, against `--mode nested` with the nesting taken out | `results-s1-acceptance.txt` (3.30.3), `results-s1-acceptance-3.30.2.txt` (the control) |
| `s1_unfiltered.py` | That S1 leaves an unfiltered `flow` as it was: every scenario, 3.30.2 against 3.30.3, byte for byte | `results-s1-unfiltered.txt` |
| `s2_acceptance.py` | S2's acceptance (plan §7.5): the built `flow` on every scenario of the five lanes, unfiltered, with `--service CosmosDB` and `--step 1`, against `--mode nested` line for line, and what the real output holds (indented lines, depth, `inside`, `no response`) | `results-s2-acceptance.txt` (S2), `results-s2-acceptance-3.30.4.txt` (the control) |
| `s2_bytes.py` | `flow_bytes.py`'s numbers taken again from the real tool: every scenario unfiltered, the release before S2 against S2 | `results-s2-bytes.txt` |
| `s2_mutations.py` | Each clause of the rule and of the verb broken in turn, and the facts that failed (plan §6.7): how a guard fact that passed on the release before proves itself | `results-s2-mutations.txt` |
| `s2_ingest.py`, `s2_ingest_causes.py` | R4 on an ingested run (plan §7.5, F17, Q4): a lane projected into ingest input, ingested in both orders, each call's parent compared with the lane's own report, and why a lost one was lost | `results-s2-ingest.txt` |
| `kronikol4j/NestedCallsTest.java` | R4 on a Java report (plan §7.5, F16): a Kronikol4J test whose service makes a call while the test's call waits | `results-s2-kronikol4j.txt` |

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

S2 ran the same corpus through the real `flow` (plan §7.5, below).

S1, 3.30.3 (K = the built tool, OLD = one built from the tag `v3.30.2`):

```bash
PYTHONUTF8=1 python s1_acceptance.py $K   $B.{ReqNRoll,BDDfy,LightBDD,NUnit,TUnit}/bin/Debug/net10.0/Reports/TestRunReport.json > results-s1-acceptance.txt
PYTHONUTF8=1 python s1_acceptance.py $OLD $B.{ReqNRoll,BDDfy}/bin/Debug/net10.0/Reports/TestRunReport.json > results-s1-acceptance-3.30.2.txt
EX=$(find "$PWD/../../examples" -name TestRunReport.json -not -path "*/runs/*" -not -path "*/baseline/*")
PYTHONUTF8=1 python s1_unfiltered.py $OLD $K $B.{ReqNRoll,BDDfy,LightBDD,NUnit,TUnit}/bin/Debug/net10.0/Reports/TestRunReport.json $EX > results-s1-unfiltered.txt
```

The control run is what makes the acceptance mean something: on 3.30.2, 569 of 1,228 views differ, every one
a step header with nothing under it.

S2, 3.31.0 (K = the built tool, PREV = Kronikol.Tool 3.30.4 installed from NuGet with `--tool-path`). The prototype's
`--mode nested` was brought in line with what S2 shipped (its docstring says how), so `results-examples.txt` and
`results-bytes.txt`, taken before, show the prototype as it was:

```bash
PYTHONUTF8=1 python s2_acceptance.py $K    $B.{ReqNRoll,BDDfy,LightBDD,NUnit,TUnit}/bin/Debug/net10.0/Reports/TestRunReport.json > results-s2-acceptance.txt
PYTHONUTF8=1 python s2_acceptance.py $PREV $B.{ReqNRoll,BDDfy,LightBDD,NUnit,TUnit}/bin/Debug/net10.0/Reports/TestRunReport.json > results-s2-acceptance-3.30.4.txt
PYTHONUTF8=1 python s2_bytes.py $PREV $K $B.{ReqNRoll,BDDfy,LightBDD,NUnit,TUnit}/bin/Debug/net10.0/Reports/TestRunReport.json > results-s2-bytes.txt
PYTHONUTF8=1 python s2_mutations.py > results-s2-mutations.txt      # rebuilds tests/Kronikol.Tests once per breakage

# An ingested run (write the ndjson and the reports outside the repository):
REF=$B.ReqNRoll/bin/Debug/net10.0/Reports/TestRunReport.json
PYTHONUTF8=1 python s2_ingest.py project $REF $OUT/reqnroll.ndjson
$K ingest $OUT/reqnroll.ndjson -o $OUT/calltree --no-component-diagram
$K ingest $OUT/reqnroll.ndjson -o $OUT/chrono --chronological --no-component-diagram
PYTHONUTF8=1 python s2_ingest.py compare $REF $OUT/calltree/TestRunReport.json
PYTHONUTF8=1 python s2_ingest_causes.py $REF $OUT/calltree/TestRunReport.json
```

The Java report: `kronikol4j/NestedCallsTest.java` copied into `kronikol4j-http/src/test/java/check/` of a
throwaway Kronikol4J worktree, run with `./gradlew :kronikol4j-http:test --tests check.NestedCallsTest`, which
prints the capture order and writes `kronikol4j-http/build/kronikol-report/TestRunReport.json`; then
`dump_records.py`, `nesting_rules.py` and `kronikol query flow` on that report.

The S2 control differs on 1,210 of the 2,986 views, and `s2_acceptance.py` classes every one as a change S2 made.
