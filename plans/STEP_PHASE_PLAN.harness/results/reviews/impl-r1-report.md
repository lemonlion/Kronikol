# Review of R1 (cd7412d9): one rule for what moves a phase

(The reviewer's answer, written here by the session that asked: the harness refused the reviewer's own write of this file.)

The commit is not ready to ship as it stands: it has two regressions to fix, a crash, and a phase pin that needs a decision.

Builds compared: baseline `5.28.2` from nuget.org; commit `5.28.3-k60rv1`, unpatched; patched `5.28.3-k60rv3`, the commit plus four patches (`review-patches.diff`). Each run's restored Kronikol ids were checked from `obj/project.assets.json`. All measured unless marked (read) or (inferred).

| # | Finding | 5.28.2 records | The change records |
|---|---|---|---|
| 1 | A failing LightBDD or BDDfy step with a step still open above it | The step's sub-steps and attachment are in the report | Both are gone, and `query attachments` says none exist |
| 2 | A step left open (published weaver, hand-made) | The next keyword corrects the phase: When and Then read Action | Setup for the rest of the test, and `TrackDuringSetup=false` drops those calls |
| 3 | About 20,000 skipped steps under one open step | Passes in about 120 to 160 ms | Stack overflow kills the test process, so no report |
| 4 | The implicit boundary ignores the rule | Nested When reads Action on all its calls | First call Action, the rest Setup, and the partition disagrees with `query` |
| 5 | A phase set by hand inside a keyword step | An inner keyword ends it | The inner keyword no longer ends it |
| 6 | LightBDD phases on shared handlers and message trackers | Unknown, nothing dropped | Setup or Action, and the per-phase options start dropping |

## 1. Failing step loses its evidence (new regression)

LightBDD L18 (L16 is the same without the attachment): a When that makes a call, attaches a file, then `await WovenSlow(l + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20))`. `query steps`: 5.28.2 `x 1 WHEN ...`, `attachment: evidence of l18-when`, `x 1.0 Woven slow`; the change only `x 1 WHEN ...`. `query attachments` on the change says "s17 has no attachments, of its own or on its steps". `query failures` loses the `x s15/1.0 Woven slow` line.

Also: L15 a hand step left open at the throw; L17 the same inside a composite, loses `1.1.0`; L20 a non-async `[Step] Task` that throws before returning, on `Kronikol.StepTracking` 5.28.2 (holds on the prototype weaver). BDDfy, dumps only (D09, D10): the failing Then and the next And disappear from the step list, the And loses its bar, in D09 `d09-then-2` reads Unknown instead of Action. L19 held: a sub-step that ends before the run does closes its parent, the tree matches 5.28.2. So this needs a hung or abandoned sub-step, a hand step left open, or the published weaver's shape.

Cause (read): 5.28.2's catch and finally closed two steps. The commit's catch closes one, and `CompleteStep` pops the stack top, so the adapter's own step stays open, and `GetSteps` returns only completed steps. `MapSteps` joins collected steps to the framework's by index, so the data has nowhere to attach.

Fix, tried: record the stack depth before `StartStep`, and in the catch call a new `CompleteDownTo(testId, depth, passed: false)`. L15 to L18, L20, D09 and D10 all come back, and the step now reads Failed where 5.28.2 wrongly said Passed.

## 2. A step left open pins the phase

W02, on Kronikol 5.28.3 with `Kronikol.StepTracking` 5.28.2 or 5.14.0 (identical): `[GivenStep] public Task GivenGuarded(string l) { if (l.Length > 0) throw new ArgumentException(l); return Call(l); }`, `try { await GivenGuarded("g"); } catch (ArgumentException) { }`, then a When, a call and a Then. 5.28.2: w, after, t read Action, `-noaction` IGNORED. The change on the published weaver: Setup, `-nosetup` IGNORED. The change on the prototype weaver: Action, `-noaction` IGNORED. W03 and W04 (a throwing `SkipIf` getter) behave the same. This change is only safe shipped with the weaver rewrite in `255f79f3`, which is local only.

The same pin without any weaver mismatch, 5.28.2 / the change: H01 `using var` hand Given spanning the test: Action / Setup. H04 sync helper, Start then a throw, no finally: Action / Setup. H07 `CompleteStep("other-id", true)`: Action / Setup. B01 Given opened in the constructor, closed in Dispose: Action / Setup. B02 Given opened in a sync-completing `InitializeAsync`: Action / Setup. H03 `ClearSteps` with a Given open: Action / Setup. H08 sync `[WhenStep]` leaves a keyword-free hand step, then a Given: Setup / Action. R22 sync Reqnroll Given binding leaves a keyword-free hand step, `r22-then`: Action / Setup. R23 the same in a When binding, `r23-given`: Setup / Action. D02 BDDfy Given leaves a hand step, `d02-then`: Action / Unknown. In R22, `r22-when` reads Action only through the handler's injection. R21, H02, H05 and H06 held.

Fixes: `ClearSteps` closing the steps it finds open: tried, fixes H03 only. A build warning when StepTracking is older than Kronikol: not tried. Adapters removing their own chain node by reference whatever the stack popped: not tried, should cover R22, R23 and D02 (inferred). Hand shapes need a changelog call-out; no way to detect them at runtime (inferred).

## 3. Stack overflow kills the process

Shape C (shape A the same with a sync `[Step]` parent): `[Step(SkipIf = nameof(Always), SkipReason = "skipped")] public Task SkippedAsync() => Task.CompletedTask;`, a hand step with no keyword, `for (var i = 0; i < N; i++) await SkippedAsync();`, the hand step's completion.

A sync parent: N 16,000 70 ms on the change; N 20,000 119 ms on 5.28.2, exit -1073741571 on the change, 82 ms patched. C hand step: 16,000 102 ms; 20,000 161 ms / exit -1073741571 / 73 ms. E `[GivenStep(SkipIf)]` loop at the top, 20,000: 88 to 142 ms / 3,356 to 3,734 ms / 321 ms. A, C, E at 100,000 patched: 570, 182, 1,237 ms. An async parent (B) does not crash: 45 ms at 20,000. Likelihood low; the loss is the whole assembly's report.

Cause (read): `BypassStep` leaves its node on the chain for the flow's life, and `Without` recurses over every node above the closing one. Fix, tried: make `Without` iterative and take a bypassed node off the chain head.

## 4. The implicit boundary and Setup partition do not follow the rule

`InjectImplicitActionStartIfNeeded` still reads the keyword string.

R19, a Given binding running a nested Gherkin When with two calls and a message: 5.28.2 in-nested-1, the message and in-nested-2 all read Action; the change Action, Setup, Setup. `TrackDuringSetup=false` keeps the first call and drops the second, and `TrackDuringAction=false` does the reverse. The commit's fact `A_nested_Gherkin_step_is_a_label` has no HTTP handler, so it cannot see this. D08 on BDDfy splits the same way.

R01, the issue's own shape (a When with only a message, then And, then Then): the phase is fixed: `r01-and` moves from Setup to Action. The marker stays before `r01-then`, so the And's call draws inside the Setup partition while `query interactions --group-by step,phase,path` says Action. R05 and L13/L14 draw the same way. R02 and R20 have no marker at all, on both builds.

Fix, tried: read `TestPhaseContext.Current` first and fall back to the keyword only when Unknown. R19 and D08 read all Setup, with the marker at the When. R01, R02, R05, L13 and L14 get the marker at the first action call. Side effect: in R15 the handler's markers land at the explicit `StartAction` and the marker at `r15-when` goes. This moves report output, so it would need a Kronikol4J ledger line.

## 5. Hand-set phases and by-rule shifts

These follow the rule, but they are losses that need a changelog line. O01, O03 a When sets Setup by hand, then an inner When; inner and after: Action / Setup. O02 a Given calls `StartAction()`, then an inner Given; inner and after: Setup / Action. O05 a Given reuses a When to arrange; reused When and after: Action / Setup (`-nosetup` now dropped). R14, R16 hand Setup in a When, then And; the And: Setup / Action. D01 woven Given in a BDDfy When; d01-when-after-woven: Setup / Action. D04, D07 story in a When; story-given: Setup / Action; after-story: Unknown / Action. O04, the save-and-restore workaround, held.

## 6. LightBDD moves well past the issues' words

On shared handlers 5.28.2 reads Unknown once the boundary has fired, and message trackers always read Unknown. The change reads Setup or Action, so the per-phase options start dropping. `l01-given-nosetup` was kept and is now IGNORED, and bus `-nosetup` rows disappear. In L02 the second diagram disappears. Step trees improve: L08 and L10 read correctly, and L09's sibling no longer escapes to the top level. This contradicts "changes nothing else a suite relies on", so it needs a call-out. L12: a hand write before `RunScenarioAsync` leaves `l12-and-after-when` at Setup on both builds.

## Smaller notes

W08 comes from the base commit, not this one: `[WhenStep] async void` gets KRONIKOL004, no step, and its calls read Setup where 5.28.2 read Action. (read) `PhaseConfiguration.ResolvePhaseFromStepType("ButWhen")`, a public method, changes from Setup to Action. (read) The `But` arm of `PhaseOfStep` is unreachable from the three adapters. (read) The fix-up in `CompleteStep` sets `CurrentFlowStep.Value = node.Parent`, which drops nodes above it; no effect measured.

## What held

Public surface identical line for line for Kronikol, ReqNRoll.Core, LightBDD.Core, xUnit3 and BDDfy.xUnit3. #195: phases fixed across R01, R02, R08 to R12, R17 and R18; R06 hooks follow the rule; R07 and R15 identical. Parallel: 48 tests, 3 runs, no phase crossing between tests. Debug and Release identical. Weaver versions 5.14.0 and 5.28.2 identical on the new core. W01, W09, W10, W11 and W13 hold; W12, W14 and W15 change as the rule says, including `w15-between-1`. Step trees unchanged on plain and Reqnroll, except the shapes above.

## Patches and the commit's own facts

The patches are in the reviewer's copy only and marked `REVIEW PATCH`. The commit's facts ran on the patched source and all passed: `Kronikol.Tests`, filtered to the phase, handler and collector classes, 369 passed; `StepPhaseWeavingTests` 39 passed; `StepPhaseScenarioTests` 12 passed. Probe dumps on the patched build differ from the unpatched commit only where listed: H03; R01, R02, R04, R05, R15, R19, R20, R22, R23; D04, D07, D08, D09, D10; L13 to L18. The full suites and the golden pins were not run with the patches.

## Not reached

Real SQL, gRPC and Cosmos trackers. Per-phase verbosity (inferred, it reads the same phase value). A real in-process `WebApplicationFactory` host. NUnit, MSTest, TUnit and plain xUnit2 on its own probes. Reqnroll floor 3.0.1, net11 runtime async. Framework retries (retry loops only, W14 and W15). `query history`, BreakfastProvider, a real BDDfy report (dumps only).

## Files

`review-patches.diff`, `probes`, `out`.
