# Fourth final review: R2 and R3

A narrow independent review of `git diff 2d9c88c3 r3-wip -- src`, the code of R2 and R3 before they were released, measured against 5.29.3. Its findings, as reported, with what was done about each at the end. Probes and rows are in `review-probes/final-4/`.

REGRESSIONS (R2):
1 A skip blamed on an unrelated failure (ScenarioInfoEnumerableExtensions.cs:115; the seed ignores order and catches; E_Hooks.feature, J_RoundTwo.feature). E05: hook catches a failed [Step], then a step skips itself. E06: step skips, then an [AfterScenario] [Step] fails. E09: same, caught. 5.29.3 reads `When a step skips itself [Skipped]`; r3-wip `[SkippedAfterFailure]`, on Skipped verdicts in E05, E09. Should read Skipped. New rows in J07, J09 err the same way.
2 "Steps before it" lists rows that never ran (FailuresDigestGenerator.cs:282; B_Backgrounds.feature). B5, hook [Step] failing under a written Background: 5.29.3 none, total 0; r3-wip lists s13/b0, s13/b1, total 2. E06, J04 too; E06 pushes out a row that ran. Should list only rows that ran.
3 An ambiguous step after a failure reads failed (ReqNRollTrackingHooks.cs:200; F05, F_Steps.feature). 5.29.3 unlisted, one failing step. r3-wip [Failed] + a second digest failing entry, though it never ran. Should read SkippedAfterFailure.
4 Inner row order flips (ReqNRollTrackingHooks.cs:162, StepPairing.cs:110; J01). A tie-order [BeforeStep] throws on an inner step, then the outer runs a [Step]. 5.29.3 `0.0 inner [Failed]`, `0.1 Late`. r3-wip reverses and drops the inner's duration. Should read as 5.29.3.
5 A sibling gains a foreign row (ReqNRollTrackingHooks.cs:82; J10/J11; floor lane only, order-dependent). J10 leaves a Gherkin step running; J11 fails and waits in its hook. 5.29.3 no stray; r3-wip `2 Given the stray step [SkippedAfterFailure]`. "No sibling scenario moves" false here.
FALSE/MISSING CLAIMS:
- changelog-r3 lines 7-8 (FeatureResultExtensions.cs:96 clocks only steps): a call after scenarioInvocation() leaves the scenario as background: 12/12 /decorator-after, D3's /clean. Row `3 Clean` stays with no call. On a handler running on both builds 4/5 on 5.29.3, 5/5 on r3-wip: not a regression. Holds for a call inside a step: N1 10/10 background -> 0/10.
- changelog-r3 omission: a consumer's throwing fetcher (F1) fails the test on 5.29.3; on r3-wip passes, phase Unknown, no Act (TestTrackingMessageHandler.cs:458). Call out.
- changelog-r2 line 7: F03, F04, F05 read Failed, not SkippedAfterFailure or Skipped. Rows after a plain hook's failure read Skipped (E01), flipping to SkippedAfterFailure if an after-hook [Step] also fails (J04).
- line 8: "after the failed row" false under a written Background (B5).
- line 10: `-` also marks a step that ran and skipped itself.
- line 14 omits two moves: addresses after not-run rows (E11 `2 Clean` -> `3 Clean`, `2 /clean` -> `3 /clean`); a failed outline row's stableId (C2 0284 -> 9179, its passing id, an improvement).
- D6 unstated: under a suite's time limit EndedAt moves +63 ms to +326 ms (LB_WAIT=1.2). The abandoned step's call stays, better. Any inherited-identity call in that window would stay too (inferred).
NO FAULT: 18 passing scenarios + every verdict on xUnit v3, TUnit, floor, both binding assemblies, stopAtFirstError. Rules with Backgrounds A1-A8, Background failing/undefined/pending, outline grouping C1-C7. @ignore, [BeforeScenario] throw, Pending + skip, [BeforeFeature] throw, [BeforeFeature] running a Gherkin step. Depth G01-G13, G21, file placement K1-K5, no bar for a not-run row (E11). Stated limits hold. Failures.md counts + query summary match; D5 matches; LastEnded entries do not cross scenarios.
NOT RUN: a real retry plugin, a throwing handler forced from outside, history ledger on, HTML painting, xUnit v2 at 3.3.4, long-run clock drift.

## What was done

- **1:** the seed went. A row that did not run says whether a failure Reqnroll recorded stopped it, noted when its finished event fires, so a caught `[Step]` and a skip stop nothing. N1, N2 on xUnit v3; N3 on every lane, as a plain hook's failure now reads the same as a step method's.
- **2:** "Steps before it" and its count hold only steps that ran. A unit fact in `FailuresDigestGeneratorTests`.
- **3:** a row that did not run reads `SkippedAfterFailure` after a failure, whatever error Reqnroll recorded on it. N4. An ambiguous step that is the first failure reads `Failed`, N5, and the changelog says so.
- **4:** what did not run stands among what ran there by the id of the step opened before it. N6 and a unit fact. Its duration stays gone: a row with no frame has no time, and the one it had was the instant Kronikol opened it after the fact.
- **5:** not fixed, stated. The stray step starts during the later scenario, so Reqnroll itself reports it there, and nothing tells them apart without a flow of the adapter's own. It needs a Gherkin step left running through `ITestRunner`.
- **The claims:** corrected in the changelogs and the wiki, each as reported above.

Each fact was run red first (`results/execution/red-fourth-review-engines.txt`), and M99 to M106 are the mutants for these rules.
