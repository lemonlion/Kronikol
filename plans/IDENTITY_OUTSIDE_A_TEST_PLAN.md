# Identity outside a test (#133)

**Status: EXECUTED and PUBLISHED as 4.13.1 (2026-10-08).** The owner asked for #133 to be assessed critically, "what I don't want is
for this to be 'fixed' and then another consumer breaks", then to "implement the plan in full, in a separate worktree"
and "fix any other problems you found". The assessment was given in a session on 2026-10-07 (kronikol-28) and is
recorded here with what the execution measured. Harness: `IDENTITY_OUTSIDE_A_TEST_PLAN.harness/`.

## TL;DR

- Outside a test, Kronikol.xUnit2's fetcher answered `("Unknown Test", <new GUID>)` where every other adapter throws. The
  resolver took the random id for the test, so a call made in a test class's constructor, `InitializeAsync`,
  `DisposeAsync`, `Dispose`, a fixture or a thread the test did not start never reached a `TestIdentityScope`, the global
  fallback or the background, and vanished from the report. Not deliberate: the GUID predates the resolver chain, every
  later change assumed `unknown`, and 2.28.22's "all fetchers throw" never reached xUnit v2 (its `unknown` check is dead).
- Kronikol.NUnit4 had the same defect (found while executing, measured): NUnit's context names the fixture in a
  set-up fixture, a fixture's constructor and one-time set-up and tear-down, and an ad hoc test with a new id on a
  thread that did not flow a test's context.
- Fixed by option B: `GetCurrentTestInfo()` (public, documented) keeps its answer; Kronikol's own readers (the fetcher,
  `Track.TestIdResolver`, `TrackingDiagramOverride`) read the identity directly and answer no test. MSTest's and TUnit's
  `TrackingDiagramOverride`, which threw `NullReferenceException` outside a test, now go to the scope or the global
  fallback, or do nothing, as xUnit v3's has since 2.31.6 (#49).
- A patch: bug fixes, nothing new for a consumer to call. Who can see a change, and what they see: section 3.

## 1. Findings

| # | Finding | Level | Where it went |
|---|---|---|---|
| F1 | Kronikol.xUnit2: outside a test the fetcher, `Track.TestIdResolver` and `TrackingDiagramOverride` answer a new random id each call (`XUnit2TestTrackingContext.GetCurrentTestInfo()`); the fetcher's `unknown` check never matches (dead since 931d887a) | RUN (probe/before/xunit2.txt) | Fixed |
| F2 | xUnit v2 runs a test class's constructor and `InitializeAsync` before `TestTrackingAttribute.Before`, `DisposeAsync` and `Dispose` after `After`: outside the identity window | RUN | Documented (wiki); widening the window is Q1 |
| F3 | The random id travels: `test-tracking-current-test-id` on HTTP calls (and so the service's own calls), Kafka headers, a document's owner (`DocumentOwnership` excludes only `unknown`) | RUN (HTTP), READ (Kafka, Mongo) | Fixed with F1 |
| F4 | `DeferredLogFlushHandler` flushed held calls under the random id when its first call came outside a test | READ, then a fact | Fixed with F1 (held calls now wait for a test, as designed) |
| F5 | `Track.That()` notes and steps outside a test went under the random id (`TestIdResolver` answered it) | READ, then a fact | Fixed |
| F6 | Kronikol.NUnit4: the same, with the fixture's id (`[SetUpFixture]`, fixture constructor, `[OneTimeSetUp]`, `[OneTimeTearDown]`) and NUnit's ad hoc id per flow on a thread the test did not reach | RUN (probe/before/nunit.txt) | Fixed (`RunningTest`) |
| F7 | MSTest's and TUnit's `TrackingDiagramOverride` throw `NullReferenceException` outside a test, inside a scope too | RUN | Fixed |
| F8 | The wiki said every fetcher throws (2.28.22), that xUnit v2's `InitializeAsync`/`DisposeAsync` are safe places to call the fetcher, that fetchers are "null-safe" and return `("Unknown", "unknown")` (five pages, 2.27.10's state), and listed only three integrations as setting `Track.TestIdResolver`; its custom-tracker recipe read `GetCurrentTestInfo()` directly | READ | Fixed (`wiki/r1_wiki.py`, 23 edits on 10 pages) |
| F9 | A call whose id names no scenario leaves only a console count line, which `dotnet test` does not print at its default verbosity (kronikol-1c measured 0 of 74 lines) | RUN (peer) | kronikol-1c takes it as a separate patch (its plan's F7) |

## 2. Option B, and why not A

A: make `GetCurrentTestInfo()` return `TestIdentityScope.UnknownIdentity` outside a test. It fixes custom code that
calls it too, but it is public and documented as returning `("Unknown Test", <new GUID>)` (Tracking-Custom-Dependencies),
so code that used its id as a key or as a test id for its own markers would change bucket (to the shared `unknown`) and
name (`Unknown` for `Unknown Test`) without being touched.

B (taken): keep `GetCurrentTestInfo()`, give the adapter an internal `Current` accessor, and have Kronikol's three
readers use it. Custom fetchers built on `GetCurrentTestInfo()` keep exactly what they had; the wiki's recipe now
resolves through `TestInfoResolver.Resolve(null, CurrentTestInfo.Fetcher)` (and a fact runs it).

NUnit takes the same shape: `RunningTest.Current` is null when NUnit's context is the ad hoc one or its current test is
a suite, and the fetcher, both `TestIdResolver`s and the override read it.

## 3. Who sees a change

- **xUnit v2 and NUnit, calls outside a test:** they went under an id no scenario owns, so no report showed them. They
  now go to a `TestIdentityScope` or the global fallback when one names a test (a fallback an earlier test left set
  included), and otherwise nowhere, or to Background calls under `CaptureBackground`. An HTTP call's test headers
  follow: the scope's or fallback's test, the background identity (`unknown`) under `CaptureBackground`, or none. No report loses a call it drew: the calls that move were drawn nowhere.
- **`DeferredLogFlushHandler` on xUnit v2:** held calls flushed outside a test went under the random id; they now appear in
  the first test that makes a call through the handler, the handler's design and the other frameworks' behaviour.
- **`TrackingDiagramOverride.StartAction()` outside a test on xUnit v2:** it set the phase (inherited by the test that
  followed, since the constructor's flow is the test's) and logged an orphan marker; it now does nothing.
- **MSTest, TUnit overrides outside a test:** no exception.
- **Inside a test nothing changes:** the probes' in-test lines and the xUnit v3 probe are identical before and after.
- Consumers checked: section 4.

## 4. Proofs

- Red: the new tests copied onto v4.12.0: every behaviour fact failed for its own reason (xUnit v2 14, NUnit 10,
  MSTest 4, TUnit 3); the passes are the controls inside a test and the guard on `GetCurrentTestInfo()` (`red/`).
- Mutations: 18, each undoing one part of the fix; 17 turned a named fact red. M18 (TUnit's override ignoring the running
  test) survives: Kronikol.Tests.TUnit runs under xUnit v3, where TUnit has no current test, and Example.Api's TUnit
  suite, which CI runs under TUnit and which calls the override inside its tests, checks no marker.
  `probe/after/tunit.txt` shows the path on a real TUnit run (`mutations/`).
- Suites on this tree: Kronikol.Tests.xUnit2 91, Kronikol.Tests.NUnit4 11 (new), Kronikol.Tests.MSTest 56 and
  Kronikol.Tests.TUnit 21 (also in Release, as CI runs them, on 4.12.0); the core suite 7,058 with 2 skipped and one
  failure, a HistoryLedgerTests read budget (1,795 ms against 1,500 while other sessions' suites ran; it passes alone,
  and the whole suite passed with this change on 4.12.3); release.slnf builds and packs in Release for every target.
- Probes: one per framework, on the published 4.9.1 packages and on this checkout (`probe/`, `run_probes.sh`). xUnit v3
  is the control: identical before and after.
- Consumers: Example.Api's xUnit2, NUnit4, ReqNRoll.xUnit2 and LightBDD.xUnit2 suites, each run on 8f653f63 (4.11.1) and on this branch with a fresh history ledger, record the same scenarios, results and calls (`consumers/examples.txt`). BreakfastProvider's NUnit lane (0ee43e8, in memory) passes 212 of 212 twice on 4.11.1 and twice with Kronikol and Kronikol.NUnit4 packed from this branch; every scenario's calls match, except one outbox poll in the retry exhaustion scenario, which differs as much between two runs of the same build (`consumers/breakfastprovider-nunit.txt`). No run prints the orphan warning. BreakfastProvider has no xUnit v2 or MSTest lane, and its TUnit lane cannot reach the override change, which is on a path that threw before.

## 5. Open questions (not in this release)

| # | Question | Measured | Recommendation |
|---|---|---|---|
| Q1 | Widen xUnit v2's identity window to the test class's constructor, `InitializeAsync`, `DisposeAsync` and `Dispose` | xUnit v3 runs all four inside the test (`probe/before/xunit3.txt`, `Stage=TestExecution`); since 4.11.1 the scenario exists at `ITestStarting`, before the constructor | Owner's call: it changes what existing xUnit v2 reports draw (constructor calls join the diagram). #132's plan (Q6) left it here |
| Q2 | TUnit 1.53's `[Before(Assembly)]` and `[Before(Class)]` run in the first test's `TestContext`, `[After(Class)]` and `[After(Assembly)]` in the last test's, so their calls are drawn in a test that did not make them | RUN (`probe/before/tunit.txt`) | Needs TUnit research (which hook is running); a separate issue |
| Q3 | NUnit's fetcher names a call by `TestAdapter.DisplayName`, the fixture class's display name, not the test's | RUN | Cosmetic in reports (they key by id); it reaches the header and `kronikol ingest`'s names. A separate issue |
| Q4 | `StepCollector.ResolveTestId` skips the global fallback where `Track` does not; the overrides consult the global fallback inside a detached flow where the resolver does not | READ | Small; left as they are |
| Q5 | ReqNRoll's and LightBDD's overrides throw `InvalidOperationException` outside a scenario, where the others now do nothing | READ | Deliberate and clear; left |

## 6. Log

- **2026-10-07.** Assessed read-only on 4.9.0 (kronikol-28): F1 to F5 and F8, the repro (`probe/src/xunit2`), options A
  and B. kronikol-1c's #132 plan took the window question as its Q6 and this issue's assessment as the reason.
- **2026-10-08.** Executed in the worktree `Kronikol-xunit2-133`, on 4.10.0, then 4.11.1 and 4.12.0: #123's xUnit v2
  results sink and #132's executor wrapper merged with this without a conflict in behaviour, and Kronikol.Tests.xUnit2
  passes 91 on the result. Four releases landed while it waited its turn (4.12.1, 4.12.2, 4.12.3 and 4.13.0); it was
  rebased onto each, and the suites in section 4 ran on the last. The cross-framework probe found F6 and F7 and Q2 to Q3. Peers: kronikol-1c took F9;
  kronikol-50 (#141) and kronikol-f1 (#136) shared no lines; kronikol-94 (#134) rebases its MSTest data-row work on
  this release's override.
- **2026-10-08, published.** 4.13.1's Release run 37779632646 passed on 78f62e12, with CodeQL 37779628620 and CI Summary
  Preview 37779628625. CI 37779628618 failed its first attempt on one gRPC test outside this change
  (GrpcTrackingInterceptorTests.AsyncUnaryCall_activity_spans_from_request_to_response: no span carried the call's trace
  id, because InternalFlowActivityListener.EnsureStarted lets a concurrent first caller return before the listener is
  registered, a product race kronikol-94 found and fixes in its R3) and passed on the second, whose auto-discovered lane
  ran Kronikol.Tests.NUnit4 on Linux, 11 of 11 (the first attempt stopped before it). nuget.org lists all 62 ids at
  4.13.1. The wiki has the 23 edits (ec03999; the link check reads the same before and after), and #133 is closed with a
  comment naming the release. kronikol-1c's next patch adds its NUnit facts and the adapter's InternalsVisibleTo to the
  new project.
