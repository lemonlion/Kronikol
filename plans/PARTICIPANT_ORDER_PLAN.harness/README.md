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

## What was not done

- No C# prototype of R1. Its effect on real diagrams is modelled by `order_rules.py`'s `RU` (the subject moved right
  after the caller), which is the rule's whole effect on a diagram; the repro is the red test R1 starts from.
- The suite #228 was found in is not on this machine; the repro stands in for it.
