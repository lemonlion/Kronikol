# PARTICIPANT_ORDER_PLAN harness (#228)

Measured on 2026-11-07 against `896ddad3` (5.31.1). Everything here reruns from a clean checkout.

## The repro

`repro/issue_shape.cs` draws the issue's scenario through the real `PlantUmlCreator`, once with the calls' phases
unknown and once with Given as `Setup` and When as `Action`. It references `src/Kronikol/Kronikol.csproj` of the
checkout it sits in, so the same file shows the defect on `896ddad3` and the fix on R1's branch.

```
dotnet run --file plans/PARTICIPANT_ORDER_PLAN.harness/repro/issue_shape.cs
```

Output on `896ddad3`: `results/issue-shape-896ddad3.txt`.

## The rules over real diagrams

`order_rules.py` reads every `*.puml` under the directories given, rebuilds today's order from the arrows (and says
whether that agrees with the declared order, the check that the parser reads what the code wrote), and counts what
each rule of the plan's §4 would change. A BDD step bar's keyword stands in for a call's phase, which the source does
not carry.

To fetch the corpus again (the seven artifacts are about 14 MB zipped and are not committed):

```
gh run download 42359580430 -R lemonlion/BreakfastProvider -n reqnroll-in-memory-report -D corpus/dl-reqnroll-in-memory-report
# likewise lightbdd-, bddfy-, nunit-, tunit-in-memory-report, reqnroll- and xunit-external-sut-report
dotnet tool exec Kronikol.Tool --yes -- query diagram corpus/dl-reqnroll-in-memory-report --out corpus/puml-reqnroll-in-memory
python -I order_rules.py --under-test breakfastProvider [--list] corpus/puml-*
```

GitHub keeps a run's artifacts for a limited time; a later CI run of BreakfastProvider gives the same shapes as long
as its tests do. Output of 2026-11-07: `results/breakfastprovider-5.14.0.txt`.

## R1's rule in full

`order_rules.py`'s `RU` moves the subject alone. `r1_rule.py` applies §5.1 as written: the pure caller, the parties
resolved `Test`, those resolved `UnderTest`, then the rest, each group in today's order. The roles are aliases, read
off `kronikol query parties` (BreakfastProvider declares `Caller` the test and `Breakfast Provider` under test):

```
python -I r1_rule.py --test caller --under-test breakfastProvider [--list] corpus/puml-*
```

Output of 2026-11-07: `results/r1-rule-breakfastprovider-5.14.0.txt` (no diagram reordered in any lane).

The same script measured Q1 (b) on Kronikol's own example reports. Copy `TestRunReport.krkl` from
`examples/Example.Api/tests/Example.Api.Tests.Component.{LightBDD.xUnit3,ReqNRoll.xUnit3}/bin/Debug/net10.0/Reports/`
(left there by any run of the example suites) into a directory of its own. Read the inferred roles with
`query parties` (`Caller` the test, `Dessert Provider` under test), write the diagrams out with `query diagram`, then
run `r1_rule.py --test caller --under-test dessertProvider`. On 2026-11-07 it reordered none of their 7 diagrams with
arrows.

## What was not done

- No C# prototype of R1. Its effect on real diagrams is modelled by `r1_rule.py` (§5.1 in full; `order_rules.py`'s
  `RU` moves the subject alone); the repro is the red test R1 starts from.
- The repro ran on `896ddad3` (5.31.1). The plan was checked again on `55999241` (5.31.2), which changed only the
  tracking handler; the repro logs its calls directly and does not use the handler, so it was not run again.
- F7 (more than one `actor` when a test has no pure caller) was read from the code, not reproduced: no diagram
  measured lacks a pure caller. T12 is its red fact.
- The suite #228 was found in is not on this machine; the repro stands in for it.
