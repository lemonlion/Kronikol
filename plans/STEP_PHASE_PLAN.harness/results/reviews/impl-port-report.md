# Review 4 of the #224/#195 port: R1 `9b9a9664`, R2 `e0df65f6`, facts `3ef0258a`, on base `397e7ecd`

(The reviewer's answer, written here by the session that asked: the harness refuses a subagent's write of this file.)

## Result

**Do not publish as it stands.** F1 is a regression that neither 5.29.2 nor the base has, and the report's own Setup filter removes the act because of it.

- #224 and #195 are fixed on every suite I ran, and parallel tests and shared handlers improve a lot.
- F1 has a small fix, tried on packages and against the change's facts I could run.
- F4 has a very small fix, not tried.
- F2, F3 and F5 are owner decisions.

## How it was measured

Builds were packages restored from a local feed, with the ids checked in `kronikol-ids.txt` beside each dump.

| Label | What it is |
|---|---|
| 5.29.2 | nuget.org, the baseline |
| `5.29.4-k60rv4` | the change |
| `5.29.4-k60rv4base` | `397e7ecd`, to separate the base's work |
| `5.29.4-k60rv5` | the change plus trial patches P1 to P4 |
| 5.29.3 | appeared on nuget.org during the review, used for F4 and to attribute review 1's LightBDD shapes |

Suites: Reqnroll on xUnit v3 (62); LightBDD on xUnit v3 (47, plus the no-decorator overload); BDDfy on xUnit v3 (28); plain xUnit v3 with woven steps (139); NUnit 4 (12); xUnit v2 (30); an in-process host with no port; review 1's parallel suite (48), skipped-step loops, and LightBDD on xUnit v2 (20); review 3's shapes (103), executors and parallel (48).

Notation: `{S a b S}` means a and b are inside the Setup partition, and `[When]` is a step bar. `SeparateSetup = true` unless stated. **Everything is measured unless marked (read) or (inferred).**

## Findings, ranked by what a consumer loses

### F1. BDDfy: an async step before the boundary loses the boundary or moves it to the Then

Belongs to R1's `OutlivedStepSaysPhase` combined with 5.29.3's close. It is not the base alone: `397e7ecd` still draws the boundary at the When.

```csharp
public async Task Given_an_async_call() => await C.Make("g");
public void When_a_sync_call() => C.Make("w").GetAwaiter().GetResult();
public void Then_a_sync_call() => C.Make("t").GetAwaiter().GetResult();
[Fact] public void A01() => this.Given(x => x.Given_an_async_call())
    .When(x => x.When_a_sync_call()).Then(x => x.Then_a_sync_call()).BDDfy();
```

| Shape | 5.29.2 and the base | This change |
|---|---|---|
| A01, A05 (async Given, sync When) | `{S [Given] g S} [When] w [Then] t` | `{S [Given] g [When] w S} [Then] t` |
| A02, A06, A10, A11, F02 (async throughout or mixed) | `{S [Given] g S} [When] w [Then] t` | no marker, no box |

Scale: in review 3's suite, 40 BDDfy scenarios lost the marker, 11 gained it and 8 moved it; E1, E2 and E4 in its executors suite lost it; in the real report through `kronikol query diagram`, 17 of 24 diagrams have a box, against 20 of 24 on 5.29.2.

Why a consumer cares: I ran the shipped `stripSetup` in Node over the real sources. It removes `a01-w` on this change and keeps it on 5.29.2, so the act vanishes from the view with Setup hidden. `query` still says Action, so picture and query disagree. Call phases and step trees are right, and per-phase detail does not move (`a14-w` stays Detailed inside the box).

Cause (read, and confirmed by the facts below): `CloseWhenDone` closes the frame on its own flow, so the flow that runs the next step still holds the closed saying frame. `OutlivedStepSaysPhase` reads that as outlived and skips the decide block. `CloseWaiting`'s `CompleteFrame` returns early because the frame has already closed. `StepsSetPhase` is already true, so the handler cannot rescue it. A later sync step moves the flow on, so the Then decides from stale Setup.

Why the facts miss it: no fact in `tests/Kronikol.Tests/BDDfy` reads the marker, and neither `StepBoundaryTests` nor the E2E `StepBoundaryPaintTests` mention BDDfy. `A_step_left_running_in_its_own_flow...` leaves its gate open, so `CloseWaiting` closes on that flow.

Fix, tried on packages (P1): `if (named && (isFrame || (!OutlivedStepSaysPhase(state) && !state.StepsSetPhase)))`. On `5.29.4-k60rv5` A01 and the rest are restored. Of the 24 shapes compared, 17 match 5.29.2 and the other 7 differ only where intended.

Fix, tried on facts only (P1b): `CompleteFrame` also leaves a frame already closed, found on this flow's chain. This keeps "outlived" exact, so I prefer it, but it has not run on packages.

Facts to port: five in `C:/Code/k224-review4/wt/tests/MiniFacts/Review4Facts.cs`. Four of them cover F1, F5 and F7 below; the fifth is the two-async-steps variant of F1. All five fail on the change.

### F2. A step left open pins its flow, and `TrackDuringSetup = false` silently drops the rest of the test

R1, by rule. The fact `A_step_never_completed_pins_its_flow` pins it. Review 1 raised the same class, and it still holds.

Shape H11: a hand `StartStep(id, "Given", ...)` never closed, then a hand When and Then, with a client that has `TrackDuringSetup = false`.

| Call | 5.29.2 | This change |
|---|---|---|
| `h11-w`, `h11-t` | Action | Setup |
| `h11-w-off`, `h11-t-off` | Action, recorded | Setup, IGNORED |

There is no boundary either; `query scenarios` shows 5 calls becoming 3. H17 (a woven When under a left-open hand Given) drops `h17-w-off` and `h17-off-after-when`. In H18 (a hand Given wrapped over the whole test) the When and Then read Setup.

Why a consumer cares: the act disappears from the report with no message, and `query` reports it as Setup.

Fix, not tried: if kept by rule, it needs a changelog line and migration (close the step, or give the wrapper no keyword). Exempting `ByHand` steps in `OpenStepSaysPhase` would restore 5.29.2 for hand steps, but R1's facts use that same public overload to stand in for step attributes, so they would have to move to the woven one (read).

### F3. A hand keyword step opened in `[BeforeScenario]` drops the act, and only the first step

R1's walker against `ParentOfNewFrame`.

Shape R40: a hand Given opened in `[BeforeScenario]`, closed in `[AfterScenario]`, with a scenario of When then Then under `TrackDuringSetup = false`. 5.29.2: `r40-w` and `r40-t` read Action and are recorded. This change: `r40-w` reads Setup and is IGNORED, then a marker at Then and `r40-t` reads Action.

Why a consumer cares: the first frame is a label because the flow holds the hook's step, even though that frame has no parent. After it closes the flow is out of that step, so the next frame decides. The act is dropped while the assertion's call is kept.

Fix, not tried: skipping `Left` steps in `OpenStepSaysPhase` conflicts with `A_keyword_under_a_woven_step_is_a_label...`, because a story under a woven step looks identical from the collector (read). This is a design call. P1 to P4 do not change it. The bar and tree differences in R40 belong to the base.

### F4. Bumping `Kronikol` alone takes a framework suite to 100% red, now that 5.29.3 is published

R1 added an optional parameter to the internal `StartFrame`. Adapter 5.29.3 with core at this change: BDDfy 25 of 26 failed, Reqnroll 62 of 62, LightBDD 47 of 47, plain xUnit v3 unaffected. All fail with `MissingMethodException` on `StepCollector.StartFrame(String, String, String, String[][], String)`.

Why a consumer cares: the nuspec dependency is `>=`, so nothing stops this at restore. The likely route is a suite that names `Kronikol` next to its adapter and takes one update per package (inferred). The failure is loud, and bumping the adapter clears it.

Fix, not tried: keep a 5-argument overload (trivial). Separately, an exact-version dependency from adapters to core would make any such mix fail at restore.

Same class from the base: `Kronikol.ReqNRoll.xUnit3` 5.29.2 on the new core fails 62 of 62 on `StartStep(..., String[][], String, Object)`. Held: LightBDD and BDDfy 5.29.2 adapters, and `Kronikol.StepTracking` 5.29.2, all work on the new core.

### F5. An explicit `StartAction()` or a custom fetcher in a later framework step is silently ignored

R2. This is part design, so it is an owner decision.

| Shape | 5.29.2 | This change |
|---|---|---|
| R13, R27 (R28, LB08, LB20, BDDfy F06 alike): Given, When prep, And `StartAction()` + act, Then | `{S g [When] prep S} [And] act [Then] t` | `{S g S} [When] prep [And] act [Then] t` |
| R36: a custom `CurrentStepTypeFetcher` | followed | never asked once steps run, so a public option does nothing on adapters |
| R14: null fetcher, no explicit call | no box | box |
| R05, R08: `StartAction()` after the When's call, or in `[AfterStep]` | `{S g S} [When] w [Then] t` | `{S g [When] w S} [Then] t` |

Why a consumer cares: the filter removes `r05-w` on this change and keeps `r27-prep`, so hiding Setup now hides what the suite called its act and shows its preparation. The same family (R04, R30, R31, R34, R35, LB06, LB07, F05) changes under the same rule, and woven steps stay overridable at any time, which leaves two behaviours.

Fix, not tried: a changelog migration note ("call it inside your first When"). A real opt-out would be new public surface.

### F6. `ClearSteps` mid-test loses the box on all three adapters

R2. Shape: R25 (`[AfterStep]` on a Given), LB62 and A12 (inside the Given). 5.29.2: `{S g S} [When] w`. This change: no marker, no box.

Fix: P4 restores Reqnroll only; BDDfy and LightBDD still have no marker on `5.29.4-k60rv5` (BDDfy measured, LightBDD by the flow value it holds). Keeping `Phase` across `ClearSteps` should cover all three, and flags reset as now, so a retry loop still redraws (inferred, not tried).

### F7. A LightBDD `AND` trusts a stale flow value over the test's own steps

R1. LB51 (`TestPhaseContext.Current = Action` before the runner, then Given, And, When): on 5.29.2 the And reads Setup, marker at the When; on this change the And reads Action, marker at the And. A rare regression. LB50 (Setup written before the runner, then Given, When, And): reads Setup and IGNORED, same as 5.29.2, so #195's shape survives there. `TrackingDiagramOverride.StartSetup()` outside a test reaches this through the public API (read, not measured).

Fix, tried on packages (P3): both shapes come out right.

### F8. A flow forked in a Given draws the boundary from its second tick on

R1 and R2. Shape B05: a `[GivenStep]` forks a loop of `[WhenStep] Tick`. 5.29.2: no box. This change: the first tick keeps to itself. `FlowAfter` then drops the closed Given from the chain, so a later tick decides for the test while main is still in Setup. `b05-g3` (Setup) is drawn after the box. Scope: plain suites only, since frames block this through `StepsSetPhase`.

Fix, not tried: a sticky per-flow note. My `CameFrom` version grew a chain, so I dropped it unpacked.

### F9. A bypassed LightBDD When decides the boundary

R2. The fact `A_bypassed_When_draws_the_boundary` pins the woven form.

| Shape | 5.29.2 | This change |
|---|---|---|
| LB10: Given, bypassed When, Given, When | `{S g1 [WHEN] [GIVEN] g2 S} [WHEN] w` | `{S g1 S} [WHEN] [GIVEN] g2 [WHEN] w` |
| LB11: bypassed When first | a box | none |

No fix tried.

### F10. LB40: a keyword-free LightBDD step with a woven Given and When reads Unknown

R1's adapter line. Not a regression in phases, but it breaks "a call reads what its flow holds", ignores `TrackDuringSetup = false`, and now draws a box over calls `query` calls Unknown. Cause (read): the fetcher writes Unknown into the handler's flow. Fix, tried on packages (P2): `lb40-wg` reads Setup and `lb40-ww` reads Action. The same patch also moves LB52.

### F11. Behaviour moves past the issues' words, which need changelog lines

R1 and R2. The commits are WIP and have no changelog, bump or Kronikol4J ledger line yet.

- LightBDD `SETUP` word (LB14, LB53): Unknown becomes Setup with a boundary, and `TrackDuringSetup = false` now drops the call.
- A suite's own word `WHENEVER` (LB59): the 5.29.2 prefix match gave Action and a box; now Unknown and no box.
- Shared handlers (LB33, LB35, review 1's L01, L02): the option is finally honoured, so a call 5.29.2 recorded is now dropped. Across the LightBDD suite, interactions go from 288 to 282.
- Phases: Unknown goes from 60 to 9, Action from 44 to 84, Setup from 40 to 48. This is a gain.
- Public method (read): `PhaseConfiguration.ResolvePhaseFromStepType("BUTWHEN")` goes from Setup to Action.

### F12. Cost: two contrived shapes are quadratic, and normal long tests are flat

`OpenStepSaysPhase` and `OutlivedStepSaysPhase` walk the whole chain with no early exit (read).

| Shape | 5.29.2 | This change |
|---|---|---|
| Z03: 5k keyword steps under 20k open keyword-free | 1 ms | 2614 ms |
| Z06: 20k tests each leaving a keyword step on one flow | 139 ms | 6397 ms |
| Z01: 200k Given/When closed | 2052 ms, 452 MB | 1662 ms, 636 MB |
| Z02: 200k under one open When | 194 ms, 55 MB | 197 ms, 80 MB |
| Z04: 50k left open | 25 ms | 73 ms |
| Z05: 20k fork-next | 219 ms | 202 ms |

Fix idea, not tried: per-state counters to skip the walks. I did not establish where Z01's extra allocation comes from.

### F13. Small items

- H07: a call hand-set to Action after a Given is drawn inside the box.
- N04: an Unknown call before the first step sits in the box, while the Given bar sits outside it.
- H12, H13, H15: a hand step closed on another flow means no boundary. This matches 5.29.2, but R2's gain is missed.

## Trial patches

`scripts/trial.patch` and `scripts/trial-p1b.patch`: P1 the line in F1 (fixes F1); P2 `stepType is not null &&` in the LightBDD options (F10); P3 `AND` reads `!state.StepsSetPhase && flowPhase != Unknown ? flowPhase : state.Phase` (F7); P4 `startsAction` also allows `state.Phase == Unknown && flowPhase == Setup` (F6 on Reqnroll).

Facts: the change as it stands 258 of 263 in my small project, the 5 failures being my new facts; P1 to P4 263 of 263; P1b + P3 + P4 263 of 263; P1 to P4 `StepPhaseScenarioTests` 13 of 13, `StepPhaseNoDecoratorTests` 2 of 2, `StepPhaseWeavingTests` 39 of 39.

Packages, `5.29.4-k60rv5` against the change: NUnit identical; Reqnroll only R25; LightBDD LB40, LB50, LB51 (no-decorator path: LB40, LB52); xUnit v3: H06 gains a box, the rest is timing noise; BDDfy: as F1, plus A13 gaining a box.

## What held (measured)

- #224: R46, R47, R48, R49, Y02 record what 5.29.2 dropped. #195: R44 (asterisk), R45 and the parallel features.
- Step trees: identical base to change on Reqnroll 62/62, LightBDD 43/43, BDDfy 24/24, NUnit 12/12. Plain xUnit v3 differs only in forked and concurrent shapes, whose order is timing.
- Parallel: review 1's self-checking suite passes 48/48 on the change and fails 48/48 on 5.29.2 and on the base. Review 3's passes 48/48 on all. Fan-out of 2000 tests: every test has one marker, none has a When call before it, and all 8000 read Action, on shared and per-test clients. On shared handlers 5.29.2 had 1998 tests without exactly one marker and 7995 calls not reading Action.
- Shared handlers and clients: T01, G02, R15, R16, LB31 to LB35, F11. A worker shared across tests: T20, T21 follow the rule.
- Lifecycle and retries: hooks R42, R43, R54 to R56; NUnit SetUp, TearDown, Retry, Repeat; xRetry one marker; `SkipIf`; bypass from a raw thread; BDDfy tear down F08.
- Skipped-step loops: no crash at 20k or 100k, and review 1's failing-step shapes keep their sub-steps. At 100k, `[GivenStep(SkipIf)]` takes 592 ms against 427 ms.
- Host: partition only, plus HS04, HS22, D07 and D08, where Unknown-phase messages before the boundary now take the setup variant, as configured.
- Options: caps, bars off, collapse, `WhenTriggersAction` off. Tests with no calls: a marker and no diagram; query counts unaffected; no empty partition in any dump. Ingest and merge: `ingest --separate-setup` from a store matches in-process; merge shows 0 differences against the run.
- Bugs fixed: null ids no longer stop the report, and a throwing fetcher no longer fails the call.
- Partition place in real reports: `{S [Given] call S} [When] ...` on Reqnroll, LightBDD, BDDfy (sync), NUnit and xUnit v3.
- "No public member" claim: public lines in the diff are only inside private nested classes (read, not run through the API tool).

## Base's own, not under review

A step never closed is now listed with a blank status (H04, L15 to L18); 5.29.2 omits H04's and 5.29.3 reads `[Passed]`. F03 to F06 step trees: concurrent steps become siblings.

## What I did not get to

MSTest, `Kronikol.TUnit`, Reqnroll on TUnit and xUnit v2, LightBDD on TUnit. `Kronikol.Tests.EndToEnd` and `examples/` (forbidden), so `StepBoundaryPaintTests` and `StepPhaseAcceptanceTests` did not run. The full core suite and golden pins on the patched copy. P1b on packages. Real engine rendering of new partitions: source only, plus `stripSetup` in Node. Real SQL, gRPC or broker trackers, Kronikol4J parity, the wiki, Linux, net11, `query history`. The host suite on the base build.

## State left behind

Disk: real free space fell to 9234 MiB, mostly from others' work; I stopped building there. Cleared: my `bin`, `obj` and cached versions. `C:/Code/Kronikol-224rv4` is clean. Kept: the 5.29.3 packages I restored stay in the shared cache. `C:/Code/k224-review4/base` is still a registered worktree. Dumps: `C:/Code/k224-review4/out/<probe>/<label>/`. Probes: `probes`, `probes1`, `probes3`. Patched copy: `wt`.
