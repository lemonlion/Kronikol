# Review of the Setup/Action boundary change (88f854fc, 1ac5e1d8, 2c2072a9)

(The reviewer's answer, written here by the session that asked: a subagent cannot write a report file here.)

**Result:** the issue's four symptoms are fixed and the shared-handler shapes hold, but not to publish as it stands. Faults with small fixes tried: F1 draws a Setup box over an act, F2 redraws old captures with the act as setup, F5 is a race, F7 throws in a consumer's call. Owner decisions: F3 is lost control of the boundary, F4 is a skipped step deciding the boundary.

**Measured:** packages `5.28.3-k60r2v1` from a `git archive 2c2072a9` copy; baseline 5.28.2 from nuget.org; `d1dcd4ae` built as v0 to tell this change from its base; restored ids in `kronikol-ids.txt` beside each dump. About 250 tests on xUnit v3, Reqnroll on xUnit v3, LightBDD on xUnit v3 with its decorator and with the `[Obsolete]` overload that has none, NUnit 4, BDDfy on xUnit v3, and a real `WebApplicationFactory` host on xUnit v3 with no port. Notation: `{S a b S}` a and b inside the Setup partition; `|M|` a boundary marker; `[When]` a step bar. Pictures use `SeparateSetup = true` unless stated. Everything measured unless marked inferred. Every finding moved between v0 and this change, except where a finding says otherwise; NUnit not built on v0, so N02 is attributed by reading the rule.

## F1. A Setup box over an act that is not a When step

`await GivenCall("g"); await client.GetAsync("act"); await ThenCall("t");` with `[GivenStep]`, a plain call or one inside a keyword-free `[Step]`, `[ThenStep]`.

5.28.2 (A01): no box, `[Given] g act [Then] t`. This change: `{S [Given] g act S} [Then] t`, the marker logged at the Then. Same on: the act in a `[Step]` (A02), a Then with no call (A04), `TrackDuringSetup=false` (A05, the act dropped on both, only the box new). NUnit (N02): Given in `[SetUp]`, a plain body, `[ThenStep]` in `[TearDown]`: 5.28.2 no box; this change `{S setup body S} teardown`, so the whole test body is Setup. Only with the base's weaver: `[WhenStep]` on async void (W09): 5.28.2 has the step, its bar and `Action`; now no step (KRONIKOL004), the call reads Setup, `{S [Given] g w S} [Then] t`, the step tree loses the When. Iterators (W06-W08): 5.28.2 throws InvalidProgramException; now the act sits in the box.

The change's own comment says a Setup partition over an act "would hide an act". On the report's filter (shipped `stripSetup` in Node over written sources): with Setup hidden, 5.28.2 keeps `a01-the-act`, `w09-w` and `n02-loose`, this change removes all three. Inferred: `ReportToggleDefaults.SetupShown=false` makes that the default view; an Unknown-phase call in such an act takes `SetupVariant`.

Fix tried: a woven `Then` draws no boundary, `&& (frameworkStep || keyword != "Then")`. A01, A02, A04, A05 and W06-W09 match 5.28.2's no-box picture, Reqnroll and LightBDD do not move. NUnit not re-run on the trial. Trade-off: a woven Given+Then suite whose act is in the Then gets no box.

## F2. "Last marker wins" re-reads existing data

Old captures: a store written by 5.28.2's `NdjsonInteractionWriter`, then `kronikol ingest --separate-setup`, the 5.28.2 tool against this change's. T05 two clients that each drew a marker: `{S ga gb S} wa wa2 wb wb2` / `{S ga gb wa wa2 S} wb wb2`. E02 explicit twice: `{S setup S} a b` / `{S setup a S} b`. E03: `{S s1 S} a1 s2 a2` / `{S s1 a1 s2 S} a2`. T08, T09, E08 move the same way. `--fold-unknown Traffic --tests <one> --separate-setup --no-collapse` puts 4 of 323 calls in Setup with the 5.28.2 tool, 261 of 323 with this one. Without `--separate-setup` no difference. `dnx Kronikol.Tool` runs the newest tool, so an unchanged capture redraws its act as setup.

In-process: the public `StepCollector.ClearSteps(id)` in a consumer's retry loop wipes the boundary state. E07 woven Given/When twice: 5.28.2 no box; this change two markers and `{S a1-g [When] a1-w [Given] a2-g S} [When] a2-w`, the filter now removes `a1-w`. E08 explicit twice: 5.28.2 `{S a1-setup S} a1-act a2-setup a2-act`; this change `{S a1-setup a1-act a2-setup S} a2-act`.

Fix tried (v2, then v3): a marker that replaces an earlier one carries a flag in its existing `PlantUml` field, and the renderer takes the last flagged marker, else the first. An old store then has 0 diff lines from the 5.28.2 tool. E08 returns to 5.28.2's picture, E07 becomes `{S a1-g S} a1-w a2-g a2-w`. E05 still overrides, the Reqnroll probe, R03 and R04 included, shows no difference from the change. No stray line in the written source.

## F3. Control of the boundary is lost on framework suites, with no opt-out

Part claimed by design, so a decision rather than a logic bug. Reqnroll unless noted, 5.28.2 / this change:

- R27 `Given call; When message; And { StartAction(); call }; Then call` (also R28, LB08, LB20, BDDfy F06): `{S g [When] prep S} [And] act [Then] t` / `{S g S} [When] prep [And] act [Then] t`, the explicit call logs nothing.
- R14 `CurrentStepTypeFetcher = null`, no explicit call (the de facto opt-out): no box / box.
- R13 null fetcher, explicit in a later step: honoured / ignored.
- R36 custom fetcher saying the act is later: honoured / never asked once framework steps run.
- R05 `When { call; StartAction(); }` (also R08 from `[AfterStep]`, LB07): `{S g S} [When] w [Then] t` / `{S g [When] w S} [Then] t`.
- R24 `StartSetup` then `StartAction` inside a When: arrange call reads Action / reads Setup.

Filter: on R05 it removes `r05-w` now, kept it at 5.28.2; on R27 it keeps `prep` now, removed it at 5.28.2. Inferred: `TrackDuringSetup=false` now drops R24's arrange call. The same family improves too: prep, `StartAction`, act inside one When (R04, LB06, LB09, F05) honoured now, ignored at 5.28.2. The no-decorator LightBDD path still ignores it, as at 5.28.2. Three behaviours now exist: woven steps honour a later explicit call (E01, H04, K05), framework steps ignore it, the no-decorator path ignores a mid-step one. Opt-out: `WhenTriggersAction=false` works on woven steps only (measured); from the code, framework steps ignore it. No fix tried. Suggested: a changelog migration note, call `StartAction()` inside your first When. A real opt-out would be new public surface and a minor bump.

## F4. A bypassed When moves the boundary (LightBDD regression)

LB10 `Given g1; When { Bypass }; Given g2; When w`: 5.28.2 `{S g1 [WHEN] [GIVEN] g2 S} [WHEN] w`; this change `{S g1 S} [WHEN] [GIVEN] g2 [WHEN] w`; the filter removed `g2` at 5.28.2, keeps it now. LB11 `When { Bypass }; Given g; When w`: 5.28.2 `{S [WHEN] [GIVEN] g S} [WHEN] w`; this change no marker and no box. Woven SkipIf: K01 `{S g1 S} [When skipped] [Given] g2 [When] w`, K02 no box; 5.28.2 no box on either. The repo's fact `A_bypassed_When_draws_the_boundary` pins the woven case, so a step that never ran decides the boundary on purpose. The no-decorator path keeps 5.28.2's picture. Fix idea, not tried: `BypassStep` undoes `ActionStepSeen`, and LightBDD's decorator catches the bypass separately. A marker already drawn cannot be unlogged.

## F5. Race on the fetcher path

S01: a custom fetcher, four clients, `Task.WhenAll(clients.Select(c => Task.Run(() => c.GetAsync("/when"))))` at the first act; 2000 tests, 64 at once. Tests with a When request logged before the marker: 5.28.2 per-test handlers 0, 0; this change per-test 24, 18; shared 23, 19, 20; no `Task.Run`, shared 0. All 8000 When calls still read Action, so only the picture is wrong: about 1% of such tests draw one act request inside the box. Cause (read): `NoteCallInStepType` draws the marker after releasing the lock. Fix tried: log under the lock, 0, 0, 0 of 2000.

## F6. `ClearSteps` loses a box 5.28.2 drew

R25: Reqnroll, `[AfterStep]` calling `ClearSteps` after a Given. 5.28.2 `{S g S} [When] w [Then] t`; this change no marker and no box. Fix idea, not tried: `ClearSteps` keeps the boundary fields; the 47 test usages of `ClearSteps` would need checking.

## F7. Null ids now throw inside a consumer's call

N04 `CurrentTestInfoFetcher = () => (null, null)` plus a step-type fetcher: at 5.28.2 the call succeeds and the report dies later; now the application's HTTP call throws ArgumentNullException from `NeedsStepTypeFromHandler`, against the code's own comment "A call must not fail for want of a step type". N01, N02 `StartAction(null)` and `StartAction(() => null)`: at 5.28.2 no throw, the report dies at run end; now throws at the call. N03, N05, N06 do not throw on either. Fix tried: guards. Separate bug, on both versions: a null-id log kills the report at run end in `BackgroundAttribution.Expire`. Not fixed.

## F8. The diagnostic report counts markers (`DiagnosticMode=true`)

Unpaired Requests, bars off 27 / 75; bars on 497 / 605. Scenarios with No Log Entries, bars off 10 / 7. Orphaned ids 3 / 4. Inferred: the Unpaired table is capped at 50, so markers can hide a real unpaired call. "Entries per Test" shows the hash as the name when a marker comes first, a test with no calls is no longer warned about. Fix tried for Unpaired and names: filter out markers. The Unpaired heading goes.

## F9. Ingest makes scenarios out of markers

`kronikol ingest` without `--tests` on a store with bars off turns marker-only tests into 0-call scenarios named by hash. Scenarios 95 to 99. One of the four is T07, whose fetcher no longer throws. Not fixed.

## F10. Default options: per-phase variants move

With `SeparateSetup=false`: D07 `SetupVerbosity=Summarised`, `ActionVerbosity=Detailed`, an Unknown-phase call before the steps: drawn Detailed at 5.28.2, Summarised now, the fix's intent, but output changes with no option touched. D08 bars off, collapse on, one variant call in the test: a loop of 6 becomes loops of 3 and 3. Worth a changelog line.

## F11. A hand-written phase can land in the box

H07 `TestPhaseContext.Current = Action` after a Given, a call, then `[WhenStep]`: 5.28.2 no box; this change `{S g hand-action S} [When] w`, so a call recorded as Action is drawn inside Setup.

## F12. `TrackDuringSetup=false` now drops more

LB34, LB35: later tests on a shared handler, on the no-decorator path. 5.28.2 records the Given call as Unknown and draws it; this change records it as `Setup:IGNORED` and drops it. The decorator path moved in the base. The option is finally honoured, but consumers lose calls they were seeing: a changelog line.

## Same as 5.28.2, not regressions

T03 a client per step: no boundary. T04 Given on client A, When on B, Then on A: `{S g b-w S} a-t`, a test-level check the likely fix, not tried. T06 and no-decorator LB17 `When, Given, When`-style by fetcher: `{S first-act g S} w`; "first act before any Setup step has no partition" holds only on the step path (C02, decorated LB17). LB40: a woven step under a keyword-free LightBDD step leaves HTTP phases Unknown.

## New pictures, or compared on this change only

D02 `InsertTestDelimiter` first: a marker is logged but no box is drawn. D01 a custom note in a Given: `{S g1 S} [And] g2 [When] w`, `g2` outside the box. H10 a raw-thread flow with only a scope: no boundary. LB14 a custom "setup" word: a boundary with the decorator, none without.

## What held (measured)

C01, C03, C04, C05 and the issue's four symptoms fixed. Shared handlers P11-P33, R15/R16, LightBDD and BDDfy parallel sets, T01/T02, and 2000 tests in S01 each log one marker in the right place. Explicit calls: R03, R09, R30-R35 (in the step, nested, throwing, unawaited, `WhenAll`), E02. T07: a throwing fetcher no longer fails the call; 5.28.2 threw InvalidOperationException. Real host HS01-HS41: server-side calls follow the marker by position, on default and preserved flows, with the shared client fixed. HS04: a server-side message in a Given is Summarised as configured; 5.28.2 drew it Detailed. HS21: a server call on TestServer's default flow reads Unknown and is not dropped by `TrackDuringSetup=false`, on both. Splits (D03, no empty diagram), folding (D04, D05), a marker with no calls (D06, D10). `MaxArrowsPerDiagram=1`: the marker does not count as an arrow and the partition still closes. Redaction and `MaxContentLength` leave the boundary alone. `TrackDuringSetup` and `TrackDuringAction` on R20, R21, B02, F02 match 5.28.2. `kronikol merge`: merged diagrams hash the same as their shards on both tools and on mixed shards, 119 and 118 diagrams. Query verbs show only timing, phase and T07 differences and never count a marker. `Failures.md` unchanged. `History.run.json` differs only on T07 and polls. An old store without `--separate-setup` unchanged. A code read found the rest of the marker readers filter markers out, apart from F8 and F9. Retries: NUnit Retry and Repeat, xRetry (same id across attempts, one marker); attempt 2's Given drawn after the box, as an explicit suite always drew it. Pollers (B01-B03), forks (F01-F08), lifecycle shapes (L01-L21, N01, N10, N11). Older `Kronikol.StepTracking` 5.28.2, 5.12.1 and 4.0.0 with the new core. `WhenTriggersAction=false`: no woven markers. The step tree unchanged in Reqnroll, LightBDD, NUnit and BDDfy; in xUnit it differs only on weaver forms and the fetcher's T07. Memory: `ClientsInSetup`'s strong reference to the handler adds about 427 bytes per test; per-test hosts stay alive on both versions, 40 of 40 at about 318 KB each. Public API: from `git diff d1dcd4ae 2c2072a9 -- src`, `public` added only inside the private `TestStepState`, nothing removed; no API-compat tool run. One cosmetic race seen once: a poll record landing between bar and marker put the When bar in the box (B03).

## Not reached

MSTest, TUnit, xUnit2, Reqnroll on NUnit, xUnit2 and TUnit, LightBDD on xUnit2 and TUnit. Reqnroll or LightBDD on a real host. The Setup toggle in a browser, client-side splitting (`stripSetup` alone in Node). Real SQL, EF and other trackers' variants. `query history` against a ledger. The repo's own suite, so no trial fix has been run against the repo's facts. Trial fixes not run on NUnit, BDDfy or the host; LightBDD on v2 only. Kronikol4J parity, the wiki, the docs.

## Files

`trial-fixes.patch` (169 lines), `probes/` with dumps under `probes/*/out/<label>/`, `scripts/stripsetup.js`, `scripts/hscmp.py`, `wt/` the patched source, `feed/` the packages.
