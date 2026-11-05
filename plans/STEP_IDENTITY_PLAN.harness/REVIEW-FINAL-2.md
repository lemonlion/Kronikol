# Second final review of R1 (on c0359e8f against 83d13095, 5.28.6)

An independent reviewer, read and run 2026-11-05. Probes: this folder's `probes/` (Probe.BDDfy/Probe.cs, Probe.Reqnroll/, held/Program.cs), rows `out/{base,new}-<fixture>.rows.txt`, `run.sh`, `cmp.py`.
None of them stops a report or changes a verdict. Verdicts matched on every lane except T15 and Q43, from #222's R4.

## Regressions, most severe first

1. **BDDfy async step: sub-steps become rows, an assertion disappears, a file moves.** Shape: `async Task WhenX() => await d.Facade();` with `[Step] async Task Facade()` making a call that suspends, then running another `[Step]`, a `Track.That`, or an attachment.
   AN1: 5.28.6 `1 When…{1.0 Facade, 1.1 Inner one, 1.2 Inner two}, 2 Then`, calls `1 /facade, 1 /inner-1, 1 /inner-2, 2 /orders/1`; here `1 When…{1.0 Facade}, 2 Inner one, 3 Inner two, 4 Then`, calls `1 /facade, 2 /inner-1, 3 /inner-2, 4 /orders/1`.
   AN2: `1.1 Assert.Equal(2, 1 + 1)` is gone (Track.cs:416 finds no active step). AN9: the digest named `1.1 Assert.Equal(3, 1 + 1) (Probe.cs:94)`, now only `1.0`. AN3: file `1:facade-file` becomes `scenario:facade-file`. AN4: `1.1 Inner that fails [Failed]`, call `1 /inner-fails`, becomes `3 Inner that fails [Failed]`, listed after `2 Then [SkippedAfterFailure]`, call `3 /inner-fails`. AN7, AN8 also lift inner steps to rows. AN10: a hidden async TearDown's inner steps become rows 2 and 3. Controls AN0, AN6 read the same. AN5 (two [Step]s in turn in the step itself) wrong on 5.28.6, right here.
   Fault: BDDfyStepTrackingExecutor.cs:84: inner.Execute returns the Task at the first suspension and CompleteFrame empties the stack. On 5.28.6 that finally popped the open [Step] and left the frame open until the [Step] finished, so it was right by accident. Plan 5.1 calls the double pop harmless, sec 9 states no limit, sec 11 says R1 "meets but does not fix" #122. The closing half of #122 (the 5.8 note on #224's R3) would end it.

2. **Reqnroll: a consumer's rebuilt row in a scenario with a nested Gherkin step scrambles it.** T21 plus `info.Steps[last] = new ReqNRollStepInfo(...)` before the report.
   Q3: 5.28.6 `0 Given a step that runs another step{0.0 Given a registered customer…}, 1 When{1.0 An order is submitted}, 2 Then ***{2.0 The order is read back}`, calls `0 /customers, 0 /outer, 1 /orders, - /orders/1`; here `0 Given a registered customer{…}, 1 Given a step that runs another step{1.0 An order is submitted}, 2 When{2.0 The order is read back}, 3 Then ***`, every call `-`.
   Q3c: with the nested step in a written Background, b0, b1 are lost and every call reads `-`. Q3b: the nested step's table moves to its own top-level row.
   Fault: ScenarioInfoEnumerableExtensions.cs:111-112: by-position takes the whole list; #222's RanByAStep filter went in the merge.

3. **InOrderTheyRan lists a row twice.** BDDfy `.When(W1).Then(T1).When(W2).Then(T2 fails).When(W3)`. OR2: `When three [Skipped]` at rows 2 and 5, 8 rows where 5.28.6 has 7. Otherwise run-order rows read better (OR1).
   Fault: StepPairing.cs:60: `next = native + 1` moves backwards, as BDDfy's list is sorted (DiagramCapturingProcessor.cs:111) and the run is not. `Math.Max(next, native + 1)` should fix it.

4. **A held call rewinds every call after it, on all lanes.** A DocumentFlow call held under step 0, confirmed after step 1 closed, then a call between steps. 5.28.6: `1 held, 1 confirming-operation, 1 between-steps`; here `0 held, 0 confirming-operation, 0 between-steps`. `held` reading 0 is the intended change, the other two are not.
   Fault: ReportGenerator.cs:5434 assigns current from a stale stamp. Plan 5.9 marks the held call "not probed". Probe held/Program.cs.

5. **BDDfy lists a file twice.** HS1: a hidden Setup attaches then throws: `0:setup-file, 0.0:seed-file` becomes those two plus `scenario:setup-file, scenario:seed-file`. HS2: a rebuilt row holding a file gains `scenario:rebuilt-row-file`.
   Fault: ScenarioInfoExtensions.cs:111: FilesNotListed runs even when MapSteps paired by position (line 119).

6. **A .BDDfy() inside a step loses every row.** HS3: 5.28.6 listed a muddled two-row tree, this lists none, only `calls: - /customers, - /orders/1`.
   Fault: StepPairing.cs:38,47,62: the inner natives count as "listed inside", but the outer frame that holds them has no native and no row. Rare, but the rows are gone.

## Claims the code contradicts (not regressions)

- 5.5 "NotRunAfter ... exact": Q4, StepPairing.cs:67 lists the no-frame When before a block hook's [Step] that ran ahead of it: `Block, Given, When [Failed], Block`. Reads better than 5.28.6 still.
- 5.4 As executed "no row carrying OutsideFrameworkStep ... reaches" the guess: Q51, Q52 rebuild the ReqNRollScenarioInfo with `new`, so WrittenBackground is null and `b0 Reference data is seeded` comes back.
- 5.4 As executed "RanByAStep ... go, as frames and StepPairing answer what they asked", and sec 9 "as published": finding 2, plus HS2 for BDDfy.
- 5.3 As executed and DiagnosticEntry.cs:64 "a step a framework's adapter collected cannot record it": HS1 records StepAttributionMismatch on a real adapter with no hand-built row.
- 5.1 and 5.9, plain lanes "as published by construction": xUnit v3, a [Step] calling Assert.Skip goes from [Failed] to [Skipped] (P20). An improvement, a plain-lane change the plan's list of three omits.
- 5.2 a failed outside row "seeds priorFailure": no such code at this commit (ScenarioInfoEnumerableExtensions.cs:119 reads natives only). No visible effect at R1, R2 needs it.
- Build warning: StepPairing.cs:10 has a paramref on the class comment (CS1734).

## Looked at, no fault

Plain lanes: xUnit v2, v3, NUnit, MSTest, TUnit on 13 shapes (P0 to P13, without P4); rows and steps matched except P20; P13 a timing race. Official fixtures base against new: Engine.xUnit3 identical; Engine.Reqnroll.xUnit3 (42 of 68 same), Engine.BDDfy, Engine.LightBDD every difference read was a fix; Engine.Reqnroll.Files part read. One timing difference on Engine.LightBDD: /orders/1 in L2 falls out of the scenario, plan F14. Reqnroll: hook steps caught or left running, no scenario steps, with a Background; outlines, nested files, pending and undefined nested steps; a nested AfterStep that throws (Q18), CloseLeftOpen, SplitBackground in every shape above. LightBDD: retry decorators (no file lost; the row gained is the stated kind); left running over a composite, bypass, ignore, assertions and files in composites, nested composites.

Not run: the RealEngines host itself, the Floor lane and Reqnroll TUnit and xUnit2 on the probes, Playwright, examples.
