# Sixth review of #224: cut 10 against published 5.28.2

Probes C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad/review6/probes, dumps C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad/review6/out, fixes C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad/review6/apply_fix.py (net +26 lines). S A U = Setup Action Unknown; ! = IGNORED; |M| marker; [ ] partition. M measured, C read.

## 1. Verdict

- **R1 alone: not as cut.** 1 and 2 drop calls 5.28.2 recorded; 1 can stop a step's body running.
- **R1 + R2: not as cut.** Adds 3 (a green test goes red), 4, 5.
- **All three:** R3 adds nothing beyond review 5's B05.
- **Small fixes measured for 1 to 5 and 7.** The #224 fix holds at every stage.

## 2. Findings

1. **T7 takes a `SkipIf` getter out of its step (R1, M).** Wiki example: `!_gateway.IsHealthy`.
   `bool Down { get { noSetup.GetAsync(url).Wait(); return false; } } [WhenStep(SkipIf = nameof(Down))] Task Act()`
   After a Given (S02, S10): 5.28.2 A, tracked, under When's bar. Patch S!, above the bar; R2 inside [ ]. First step (S01, R52): S and S! become U. `Current != TestPhase.Action` on a When or Then (S04, S05): body runs becomes Bypassed, the act never runs. `Current == Setup` on a Given (S06), `!HasActiveStep(id)` (S07) flip too. A step the getter runs (S08, S09): sub-step becomes top-level with a bar.
   Fix F-T7: read after `StartStep`, in a try whose catch fails the step, #226's contract (C, `on-226.patch:824`). M: S01 to S11 read as 5.28.2, S08's When a label; H05, M01, M02 Given [Failed]; weave suite passes, Debug and Release.

2. **BDDfy's keyword-free first step reads what the flow held (R1, M).** T3 writes Unknown only without a test id.
   `await WovenGiven(l); new ByNameStory(this, l).BDDfy();` with a by-name `Setup()`; or `StartSetup()`, `StartAction()`, a hand write first (B18, B20 to B23). 5.28.2 U, tracked. Patch S, nosetup dropped; or A, noaction dropped.
   Fix F-T3: `if (testId is null || (keyword is null && !under))` write. M: as 5.28.2, R1 and R3.

3. **T4b fails a passing test (R2, M).** The guard catches `InvalidOperationException` only; a shared handler now asks each test.
   `static` client, `CurrentStepTypeFetcher = () => name.Value!.ToUpper()`; a later test calls outside a step (F2). 5.28.2 passes, U. R2 `NullReferenceException`, red, rows gone. A second client's first When call (F1): A becomes U.
   Fix: `catch (Exception)`; at `ActionStartDrawn`, write Action before returning. M: F2 passes with its boundary; F1 A; 214 facts pass.

4. **`EndTime` lets a later `StartAction()` swallow the act again (R2, M; F21).**
   R40 (two Reqnroll hosts), L40, B32: a When leaves a woven step running, a Then marks. The left step takes the When's completion, so `BoundaryStep` reads open. 5.28.2 [Given] When, act. Patch [Given, When, act]. R45 (`BypassStep` on its own step): same.
   Fix F-T2: `boundary.EndTime > 0 || !FlowHolds(boundary)`. M: as 5.28.2; override still works (R42, R48, B28).

5. **A woven Given and When before a framework's first step keep the boundary (R2, M).** T1 resets `ActionStepSeen`, not what was drawn.
   `[BeforeScenario] await woven.Seed(); await woven.LogIn();` R41, B24. 5.28.2 [hook, Given] When. Patch [seed] |M| log-in, Given, When.
   Fix F-T1: at that reset, `ActionStartDrawn = false` unless explicit or a client drew. M: as 5.28.2.

6. **An older weaver with this core pins on a throw (R1, M).** Weaver 5.28.2: M01, M02 (throwing getter), M10, M12 (Task stub throws, caught). 5.28.2 pair: act A. Mix: act, Then S, nosetup dropped. M01, M02 also weaver 5.4.1; core R2, no marker either. Consumers hold two version lines.

7. **T9 loses the chain in the other order (R1, M).** `StartStep(other, ...); [GivenStep]{ CompleteStep(other, true); await When(); }` I02, I03: phases as 5.28.2, above the rule; R2 marker mid-Given. Fix F-T9: unlink one node. M: label, marker at the real When.

8. **Low.** `[Obsolete]` LightBDD, shared handler (R2, M): S01, S02, S05, S06 U becomes S and A; §7.2 lists the partition, not nosetup and noaction now dropped. `ChainOf` is dead code (C). A skip beside a running sibling no longer closes it, later bars go, §9's shape (M). `Task<struct>` with `SkipIf`: `InvalidProgramException` at both (M).

## 3. Plan claims that do not hold

- §5.2 "a closed step still on a chain means" forked: C02. "does not lose this test's chain": 7. "what #226's wrapper does": not the getter's order.
- §5.5 "A step with no keyword reads `Unknown`": 2. §5.8 "step lists and step bars": 1.
- §5.6 "A call must not fail for want of one": 3. "does not count as an act": 5. "logs nothing": 4.
- §7.1 omits 1. §7.2 "on every adapter": B28 not at R2, yes at R3.
- §9 names the older weaver's bypass, not 6; F05 stale for I01. Q11 "reads the same": 1 (C).

## 4. What held

T6: 54 of 54 matrix cells (6 parents, 9 stubs, `Task<T>`), depth 3, WhenAll, forks, async void, on Reqnroll, LightBDD, BDDfy; Release as Debug. Hand bypass, both overloads: K01, K02, K08 to K12, R49, L43; self-bypass closes the parent, as 5.28.2. T8: C01, C03 to C05, R51. `PhaseOfAttribute`: A01 to A05; M13, M14, 116 calls unmoved. T2 retries: N01 to N04, B26, B27, R50. T4c: L44 to L51 draw as 5.28.2 or better. T4b across fixtures. This weaver on cores 5.28.2, 5.4.1. The fixes moved nothing else on review 5's suites but H05.

## 5. Not run

R40, R41 on TUnit; legacy parallel; full repo suites; readers, OTLP, merge on the fixes; item 3's fix beyond rev6-fetch, rev6-a, facts; BreakfastProvider; a browser.
