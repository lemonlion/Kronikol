# xUnit v2 reports under another test framework: #132, with #123

**Written:** 2026-10-07, at 4.9.0 (`417c8e58`), the day #132 was filed. 4.9.1 was published while it was written and touches
none of the files below. **Status: green-lit 2026-10-08, executing** (the owner: "implement the plan in full", and "fix
any other problems you found"; roadmap row 1.24, decision D38, Q1 to Q11 as recommended). R1, a patch, is 4.11.1; R2, a
minor, follows. The log (section 11) records what was run. Evidence labels: **RUN** (measured here, `dotnet test` on net8.0 with
xunit 2.9.3, Kronikol.xUnit2 built from `417c8e58`), **READ** (in the source, `file:line`: Kronikol at `417c8e58`; xunit at
its `v2-2.9.3` tag; xunit.runner.visualstudio at `2.8.2` and `3.1.5`; Xunit.Extensions.AssemblyFixture at `2.6.0`),
**INFERRED** (reasoned from facts, stated by none), **DOC**, **ISSUE** (taken from #132 or #123, not re-measured). The probes
behind every RUN line are in [`XUNIT2_FRAMEWORK_COMPOSITION_PLAN.harness/`](XUNIT2_FRAMEWORK_COMPOSITION_PLAN.harness/README.md)
with their output. The xUnit source notes behind the READ lines about xUnit and the other frameworks are in its
`research/xunit-v2-sources.md`, cited below by section (A1 to E).

#132 asks for Kronikol.xUnit2's reports in a suite whose `[assembly: TestFramework]` slot is already taken, in the reporter's
case by Xunit.Extensions.AssemblyFixture. An assembly has one slot (`TestFrameworkAttribute` is `AllowMultiple = false`), and
many suites have filled it: Xunit.DependencyInjection, Reqnroll.xUnit and SpecFlow.xUnit, Xunit.Extensions.Ordering,
Meziantou.Xunit.ParallelTestFramework and AssemblyFixture all take it, the first three and Meziantou through an attribute
their package generates (READ, C). Kronikol cannot report from anywhere else: in xUnit v2 only the test framework's executor
sees every test's result message (READ, D).

The ask is right. Of the issue's three suggestions, the second, a decorator over the executor any framework returns, is the
one to build. It reaches every framework through xUnit's public `ITestFrameworkExecutor` interface, and in the consumer's
framework class it is a three-line override. The plan builds it on the technique the issue reports: key each scenario by the test's
`ITest` object instead of its name. That technique is also the fix for #123, a live defect in the framework Kronikol ships
today: a failing `[Fact(DisplayName = ...)]` is reported as passed, and theory rows swap verdicts, durations and calls. A
public decorator built on today's name matching would carry #123 into the new path, so the fix comes first:

- **R1, a patch.** Kronikol's own framework pairs each result with its scenario by the test's identity (#123). It reports the
  tracked tests it drops today: skipped ones, and ones that fail before their first line runs (constructor, `InitializeAsync`,
  a fixture). Section 3 lists the other defects R1 fixes.
- **R2, a minor.** One public extension, `WithKronikolReporting()` on `ITestFrameworkExecutor`, and the wiki section the
  issue's third suggestion asks for.

## 0. Summary

| | |
|---|---|
| What ships | **R1, a patch:** results matched by `ITest`, so DisplayName facts and theory rows get their own verdicts; skipped tests and tests that fail before running appear; reports written as `ITestAssemblyFinished` passes; the defects in section 3. **R2, a minor:** `WithKronikolReporting()`, which makes any xUnit v2 framework's executor write Kronikol's reports, and the wiki's "Already using another test framework" |
| What a consumer writes | Three lines in the framework class they already have (section 4.7). A sealed framework is wrapped by composition, in about twenty lines (RUN, probe F, Xunit.DependencyInjection) |
| What it needs first | Tests that run real xUnit v2 assemblies through `dotnet test`. Nothing in the repo runs the framework today, which is how #123 shipped (F9) |
| Bumps | R1 patch, R2 minor (section 6.2). No new option in either |
| Open | Q1 to Q11 (section 9). Q1 (fold #123 in) and Q3 (how messages reach the runner) shape the work |

## 1. How far each claim was checked

Harness paths below are relative to `XUNIT2_FRAMEWORK_COMPOSITION_PLAN.harness/`.

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | Kronikol.xUnit2 writes its reports only from its own `[assembly: TestFramework]` | READ, RUN | **True.** The only caller of `ReportLifecycle.GenerateReports` is `ReportingTestFrameworkExecutor.RunTestCases` (`ReportingTestFramework.cs:60`). Probe B, AssemblyFixtureFramework with `[assembly: TestTracking]`: 21 tests ran, and there was no Reports directory and no `kronikol-error.log` (`results/B.AssemblyFixtureOnly/1/reports-files.txt`) |
| C2 | An assembly has one test framework | READ, RUN | **True.** `TestFrameworkAttribute` is `AllowMultiple = false` (C). With both attributes the build fails with CS0579 (`results/B.AssemblyFixtureOnly/both-attributes-build.txt`) |
| C3 | Suites that share expensive infrastructure commonly use another framework already, most often AssemblyFixture | READ | **True of the slot, and broader than AssemblyFixture.** By nuget.org downloads, SpecFlow.xUnit (25.1M) and Xunit.DependencyInjection (22.3M) take the slot more often, and Reqnroll.xUnit, Xunit.Extensions.Ordering and Meziantou take it too (C). Reqnroll and SpecFlow suites are served by Kronikol.ReqNRoll.xUnit2 instead |
| C4 | `TestResultCapturingSink`, `ApplyResults()`, `ReportLifecycle.GenerateReports()` and `XUnit2TestTrackingContext.CollectedScenarios` / `GetAllScenarios()` are internal | READ | **True** (`ReportingTestFramework.cs:69`, `:121`, `:187`; `XUnit2TestTrackingContext.cs:16`, `:37`) |
| C5 | Only `XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(IEnumerable<ScenarioInfo>, …)` and `ScenarioInfo` are public | READ | **Nearly.** Its no-scenario overload, `ReportLifecycle.Options` and `ReportingTestFrameworkExecutor` are public too. None of them sees a result |
| C6 | The workaround: wrap the executor and its sink, record `ITestStarting` and each result per `ITest`, and write the reports when `ITestAssemblyFinished` arrives, before passing it on, since the runner lets the host exit once it has seen it | READ, RUN | **True.** The adapters set their completion event only after `ITestAssemblyFinished` has passed through their sink (A5). The harness's prototype does this, and wrote its report before the host exited in each of its 12 guarded runs (probes C and F, synchronous reporting on and off) |
| C7 | With `xunit.execution.SynchronousMessageReporting` set, `ITestStarting` reaches the sink on the test's own flow, just before `Before` | READ, RUN | **True, and the flow reaches further.** `Before` ran on the thread `ITestStarting` was delivered on 51 of 51 times, a median of about 0.25 ms later, and saw the scenario set there 51 of 51 times; the test body saw it 51 of 51 times (`results/C.AssemblyFixturePrototype-sync1/*/asynclocal.txt`). The constructor and `Dispose` see it too (A3). Without the option, `Before` saw it 0 of 51 times |
| C8 | Keyed by the `ITest`, the pairing is exact for theory rows and `[Fact(DisplayName = …)]` | RUN | **True.** Probe C, three runs: theory rows 18 of 18, and rows of a theory whose data cannot be serialized 9 of 9, under their own name, verdict, duration and call. 20 of 21 tests were right in every run; the one miss is the formatter (F6). Probe F, on Xunit.DependencyInjection: 19 of 20, the same miss |
| C9 | Skipped tests, and tests that fail in their constructor or `InitializeAsync`, have an `ITestStarting` and a result but no `Before`, so they can be reported | READ, RUN | **True, and today they are dropped.** Probe C reported all four (a skip, and a constructor, an `InitializeAsync` and a class fixture failure) with their results. On 4.9.0 all four are absent from the report, in 4 of 4 runs (F2). Fixture failures and unresolvable constructor arguments take the same path (A9) |
| C10 | Suggestion 1: a public composable sink and a public `GenerateReports()` | READ | **Buildable; not recommended** (Q2) |
| C11 | Suggestion 2: an executor decorator usable from any `XunitTestFramework` subclass | READ, RUN | **Buildable, and wider than stated:** any `ITestFramework`, a sealed one included, by composition. Probe F wrapped Xunit.DependencyInjection's sealed framework that way: constructor injection worked in 6 of 6 runs, and every run wrote a report |
| C12 | Suggestion 3: document the pattern on the wiki | | **In R2** (section 6.4) |
| C13 | The workaround is about 150 lines | RUN | **And it needs one thing the issue does not say.** A sink or executor written in a test project must derive from `LongLivedMarshalByRefObject`, or xunit.analyzers fails the build with xUnit3000 (F10) |

## 2. Where it stands today

**The shipped framework** (`src/Kronikol.xUnit2/ReportingTestFramework.cs:23-62`). `ReportingTestFrameworkExecutor` overrides
`RunTestCases`. It wraps the execution sink in `TestResultCapturingSink`, runs an `XunitTestAssemblyRunner` to completion,
then calls `ApplyResults()` and `ReportLifecycle.GenerateReports()`. The executor blocks, so the runner cannot finish before
the reports are written.

**Scenarios and results are made in two places and joined by name.**
- `TestTrackingAttribute.Before`, a `BeforeAfterTestAttribute`, files each test under a new GUID with a `MethodMatchKey` of
  `Namespace.Class.Method` (`TestTrackingAttribute.cs:31-60`), and opens the identity window that its calls are logged under
  (`:38`; `After` closes it, `:63-66`). `Before` gets only a `MethodInfo`, so every row of a theory gets the same key, and no
  display name is recorded.
- The sink records each `ITestPassed`, `ITestFailed` and `ITestSkipped` by display name in an unordered `ConcurrentBag`
  (`ReportingTestFramework.cs:73-114`).
- `ApplyResults` walks an unordered `ConcurrentDictionary` and gives each result to the first unmatched scenario whose key
  equals the result's display name, or is its prefix followed by `(` (`:121-156`). A scenario that gets no result keeps
  `Result = Passed`, the property's default (`ScenarioInfo.cs:16`), with no duration and no end time.

**Public surface today.** `ReportingTestFramework`, `ReportingTestFrameworkExecutor`, `ReportLifecycle` (its `Options`; its
`GenerateReports` is internal), `XUnit2ReportGenerator` (two overloads), `ScenarioInfo`, `XUnit2TestTrackingContext.GetCurrentTestInfo`,
`TestTrackingAttribute`, `DiagrammedTestRun`, `DiagrammedComponentTest`, `CurrentTestInfo`. `TestResultCapturingSink`,
`ApplyResults` and `XUnit2TestTrackingContext.CollectedScenarios` are internal, which is #132's complaint.

**The documented way round** is a collection fixture whose `Dispose` calls the public
`XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(start, end, options)`. The wiki says that with it "all will show as
Passed" and reports "may be truncated" (`Integration-xUnit2.md:365-387`). Measured on 4.9.0 (probe E, three runs), it is worse
than the wiki says (F4):
- all 15 scenarios were Passed with no duration, while the runner counted 6 failures;
- the four tests that never reach `Before` were absent;
- every theory row was named "Rows", with no arguments;
- `Specifications.html`, which Kronikol writes empty when it sees a failure, was written in full, as for a green run.

Source: `results/E.CollectionFixtureOnly/*/compare.txt`.

**The other xUnit v2 adapters are not affected.** Kronikol.LightBDD.xUnit2 runs under LightBDD's own framework (its scope
attribute is itself the test framework attribute) and Kronikol.ReqNRoll.xUnit2 under the one Reqnroll.xUnit generates. Each
writes its reports from its host library's end-of-run hook, and neither uses a Kronikol.xUnit2 type (READ:
`Kronikol.LightBDD.Core/ReportWritersConfigurationExtensions.cs:50-89`, `ReqNRollTrackingHooks.cs:159-222`;
`Kronikol.xUnit2.csproj:19` grants LightBDD.xUnit2 internals it does not use).

**Tests.** `Kronikol.Tests.xUnit2` has 12 facts. None runs the framework, the sink's `OnMessage`, `ReportLifecycle` or `After`:
`ApplyResultsTests` add results to the sink's bag by hand (`ApplyResultsTests.cs:44`). The only real xUnit v2 run in CI is the
example project's two passing facts, through `Example.Api.Tests.Integration` (`TestProjects.cs:25-41`). No theory, skipped
test, failing test or DisplayName has ever run through the framework in a test (F9).

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **#123 holds on 4.9.0, in every run, under both adapters.** The failing DisplayName fact was reported Passed, with no duration, under its method's name, in 4 of 4 runs. Theory rows were paired differently each run. In each run, 3 to 6 of 6 rows were under another row's name and up to 2 under another verdict, and 2 to 4 rows' own calls were moved to the background section as Expired, because their scenario carried another row's earlier end. Rows of a theory with non-serializable data: 2 of 3 wrong in every run (`results/A.OwnFramework*/*/compare.txt`) | RUN | R1 |
| F2 | **Tracked tests that never reach `Before` are missing from the report.** These are a skipped test, and tests whose constructor, `InitializeAsync` or class fixture fails. 19 tests ran and the report held 15 scenarios, in 4 of 4 runs. The shipped sink records skips (`ReportingTestFramework.cs:103-110`), but nothing creates a scenario for them, so the report's counts and `Failures.md` leave out failures the runner reported | RUN | R1 |
| F3 | **A suite that sets xUnit's `methodDisplay` to `method` gets no results at all.** Its display names lack the class, so none matches `Namespace.Class.Method`. All 15 scenarios were Passed with no duration while 6 tests failed, and `Specifications.html` was written in full (`results/A2.MethodDisplay/1/compare.txt`). Any option that changes the text of display names (`methodDisplayOptions`) is exposed the same way (INFERRED) | RUN | R1 |
| F4 | **The documented collection-fixture path reports every test as passed** (section 2): 15 of 15 Passed with no duration against 6 failures, the four early failures absent, the theory rows unnamed, and `Specifications.html` written as for a green run (`results/E.CollectionFixtureOnly/*/`). The wiki warns that "all will show as Passed" (`Integration-xUnit2.md:365-387`). Whether the report should also say so in its own data is Q7 | RUN | R1 (S3) |
| F5 | **The report's run start is its end.** `ReportLifecycle.StartTime` is a static field initialiser (`ReportingTestFramework.cs:177`), so it is set when the type is first touched, which in a suite that never sets `ReportLifecycle.Options` is `GenerateReports` itself. Start equalled end in 8 of 8 runs, and the field read 1.67 to 1.92 s after the first test started and 4 to 6 ms after the last one ended (`results/A3.StartTimeProbe/*/starttime-probe.txt`). The example and the template set `Options` in a collection fixture's constructor (`TestRun.cs:19`), which starts the clock at that collection's first test instead (READ) | RUN | R1 |
| F6 | **`FormatScenarioDisplayName` mishandles sentence-like names, and two inputs make it throw.** "Order API returns 404 for v1.2" gives "2"; "Does the thing." and "" throw `IndexOutOfRangeException`; "Ns.Class.M(x: Foo())" gives "M [x: Foo(]" (`results/G.FormatterProbe/output.txt`). xUnit v2 never passes it a DisplayName today (F1), but R1 would. An exception escaping a message sink crashed the test host in 3 of 3 runs, and the runner's TRX kept only 6 of 21 results (`results/C.AssemblyFixturePrototype-sync1-unguarded/*/console.txt`). Under the async bus, xUnit reports a catastrophic failure and that test is lost from the TRX and the report. The xUnit v3 adapter passes display names to the formatter today (`TestContextEnumerableExtensions.cs:29`) | RUN | R1 (S2), section 4.5 |
| F7 | **Calls filed under an id no scenario has are dropped from every report file.** With the hand-over off (the prototype, async bus), the calls of all 17 tests that made one were missing from `TestRunReport.json`. Each carried the id `Before` had made, and no scenario had that id (`results/C.AssemblyFixturePrototype-sync0/*/compare.txt`). Their only trace is a console line, "Warning: 17 orphaned test ID(s) in logs do not match any feature scenario." (`ReportDiagnostics.cs:67`, printed by `ReportGenerator.cs:635-641`), and `DiagnosticReport.html` under `DiagnosticMode`. `dotnet test` showed that line at normal verbosity and not at its default (0 of 74 lines, `results/C.AssemblyFixturePrototype-sync0-unguarded-defaultverbosity/1/console.txt`). That is why section 4.4's fallback pairs `Before`'s scenarios before writing. The drop itself is section 8 | RUN, READ | Section 4.4 |
| F8 | **Forcing synchronous reporting changes who calls the runner's sink.** Under it, test threads call the runner's sink directly and at the same time. The VS adapters do that only with collections serialized (VS2 L537-542, VS3 L605-610), and VS 2.8.2's sink updates a counter without a lock (E4). The switch itself costs nothing measurable (section 4.2) | READ, RUN | Section 4.2, Q3 |
| F9 | **No test runs the framework.** `Kronikol.Tests.xUnit2`'s 12 facts never run `ReportingTestFramework`, `OnMessage`, `ReportLifecycle` or `After`. The only real xUnit v2 run in CI is two passing facts (section 2), so #123 had no fact that could fail | READ | S0 |
| F10 | **A workaround written in a test project must derive its sink and executor from `LongLivedMarshalByRefObject`.** xunit.analyzers, which the `xunit` package brings in, otherwise fails the build with xUnit3000 (`probe/Prototype/Prototype.cs:31-33`). The issue's sketch does not mention it. Kronikol's types derive from it, and so does the wiki's composition recipe | RUN | Sections 4.7, 6.4 |
| F11 | **With the async bus nothing can be keyed by flow.** The scenario set at `ITestStarting` was found on another test's result message 35 of 63 times, and `ITestStarting` found another test's scenario already on its flow 60 times (`results/C.AssemblyFixturePrototype-sync0/*/asynclocal.txt`). The `ITest` reference is the only key that holds in both modes | RUN | Section 4.1 |

## 4. The design

### 4.1 The results sink (R1, internal)

One internal sink serves Kronikol's own executor (R1) and the decorator (R2). It looks at every message as it is sent and
passes every message on (section 4.2).

| Message | What the sink does |
|---|---|
| `ITestAssemblyStarting` | Takes the run's start, in UTC, when it sees the message |
| `ITestStarting` | If `TestTrackingAttribute` applies to the test, creates its `ScenarioInfo` under a new id, keyed by the `ITest` object by reference, and puts the scenario in an internal `AsyncLocal` for `Before` (below). "Applies" means what xUnit itself applies: the attribute on the method, on the class or a base class, or on the assembly (`XunitTestCaseRunner.cs` L131-135). An untracked test gets no scenario, as today |
| `ITestPassed`, `ITestFailed`, `ITestSkipped` | The scenario keyed by the message's `ITest` takes the result, the messages, the stack traces, the `ExecutionTime` as its duration, and the time the sink sees the message as its end |
| `ITestFinished` | A scenario with no result yet (a cancelled run sends none, A1) is marked as having none |
| `ITestAssemblyFinished` | Section 4.3 |
| Anything else | Passed on only |

The `ITest` key is exact where names are not:
- xUnit sends one `ITest` object on every message of a test (A10, RUN).
- Rows of a theory whose data cannot be serialized share one test case and one `UniqueID`, but each row still has its own
  `ITest` (A8), so the key is the object, not an id.
- A test that never reaches `Before` still has its `ITestStarting` and its result. That covers a skipped test, a constructor,
  `InitializeAsync` or fixture failure, and an unresolvable constructor argument (A1, A9).

**`Before` and the identity window.** `TestTrackingAttribute.Before` reads the `AsyncLocal`. When it holds a scenario for this
very method, `Before` opens the identity window under that scenario's id and creates nothing. When it holds nothing, because no
Kronikol sink is in the run or the bus did not deliver synchronously (section 4.4), `Before` does what it does today: a new
id, a scenario of its own, and its `MethodMatchKey`. `After` is unchanged. The window stays where it is, from `Before` to
`After`, which is how the reporter's own framework kept it (#123's comment of 2026-10-07). Opening it at `ITestStarting`
instead would cover the constructor, `InitializeAsync` and `Dispose`, whose calls today go under a fresh id each (#133's
assessment). That is Q6.

**The end of the run.** Before the reports are written:
1. Any scenario `Before` made on its own is paired with the sink's scenario for the same test by today's name rule, and the run
   records why (section 4.4). In a run where the hand-over worked there are none.
2. A tracked scenario with no result is reported `Passed` with `Scenario.ResultDefaulted` set, as `kronikol ingest` reports one
   (`FeatureSynthesizer.cs:184-185`), with the `ResultDefaulted` diagnostic. The report's diagnostics, `Failures.md` ("Some
   results are defaults, not verdicts") and `kronikol query`'s `!` line then say the verdict was not measured
   (`FailuresDigestGenerator.cs:561-562`, `QueryCommand.cs:388-401`). `ScenarioInfo` gains the flag as an internal property,
   so R1 adds no public member (Q7).

### 4.2 Synchronous reporting, and how messages reach the runner (R1)

The hand-over from `ITestStarting` to `Before` needs the sink to run on the test's own flow. xUnit v2 does that only through
its `SynchronousMessageBus`, which calls the sink inline from `TestRunner.RunAsync` (`SynchronousMessageBus.cs` L32-38). An
`AsyncLocal` set there reaches the constructor, `Before`, the test body, `After` and `Dispose`, and is undone when the test's
runner returns, so it never reaches the next test or row (A3, RUN). The default `MessageBus` delivers on a single reporter
thread, sometimes after the test body has already run (A3, RUN). So the executor turns on
`xunit.execution.SynchronousMessageReporting`, on its own copy of the options rather than the runner's object (VS 2.8.2
hands over its own, A7).

Synchronous reporting also changes what the runner's sink sees. With parallel collections it is called from many test
threads at once, which the VS adapters never do themselves: they turn synchronous reporting on only when collections run one
at a time (VS2 L537-542, VS3 L605-610), and VS 2.8.2's sink keeps a counter without a lock (E4). So the Kronikol sink passes
messages on through a queue of its own, as xUnit's default `MessageBus` does:
- one worker, first in first out;
- the bus gets the most recent continue-or-cancel answer from the runner's sink (`MessageBus.cs` L71-73);
- an exception from the runner's sink becomes an `ErrorMessage` (L39-47).

The runner therefore sees what it sees today, and only Kronikol's own bookkeeping runs inline: a dictionary update per test
message.

The switch itself costs nothing measurable. Over 2,000 trivial facts, xUnit's own assembly time was 0.841 s with synchronous
reporting and 0.827 s without (medians of five runs on a busy machine, VS 2.8.2's sink, `results/perf/perf.tsv`). So the queue
is there for the delivery the runner's sink was written against, not for speed (Q3).

### 4.3 When the reports are written (R1)

On `ITestAssemblyFinished` the sink finishes the scenarios (section 4.1), writes the reports, and passes the message on, in a
`finally`.
- **This is the one point that works under every framework.** The VS adapters let the host exit once `ITestAssemblyFinished`
  has passed through their sink (`ExecutionSink.cs` L230, L449-455; A5). The return of `RunTests` says nothing: it is
  `async void` in xunit, AssemblyFixture, Meziantou, Reqnroll and Autofac, and blocks only in DependencyInjection (E7).
- **A failure never reaches the bus.** It is caught and written to `kronikol-error.log`, as today
  (`ReportingTestFramework.cs:203-207`). A throw from `OnMessage` crashes the test host under the synchronous bus, and a
  message never passed on hangs the run (A5, RUN).
- **`Dispose` writes nothing.** The executor is disposed twice, and is also created by `--list-tests` sessions that run
  nothing (A5, RUN).
- **The run's start** is the time `ITestAssemblyStarting` is seen, not `ReportLifecycle`'s static field initialiser (F5).
- **Run state belongs to the run.** The decorator holds it per executor, which xUnit uses for one run (A6). The once-per-process
  guard in `ReportLifecycle.GenerateReports` stays for the shipped path.

### 4.4 When the hand-over cannot happen

A framework whose assembly runner overrides `CreateMessageBus` to keep the asynchronous bus would defeat the hand-over. None
of the five checked (AssemblyFixture, DependencyInjection, Meziantou, Reqnroll, Autofac) does (A3). In that case:
- `Before` makes its own scenarios, as today.
- Step 1 of section 4.1's end pairs them by name. That is exact for facts without a DisplayName, and as wrong as today for
  theory rows and DisplayName facts.
- The run records an `Other` diagnostic saying results were paired by name because messages were not delivered on the test's
  flow. Q10 weighs a kind of its own.

### 4.5 Scenario names

A scenario is named from `ITest.DisplayName` through `ScenarioTitleResolver.FormatScenarioDisplayName`. That is what
`ApplyResults` does today for every test it matches (`ReportingTestFramework.cs:149`), so default display names and theory
rows keep their names. DisplayName facts change: they were never matched, so each was named after its method, and now each is
named after its DisplayName.

That exposes the formatter to sentence-like names it mishandles (F6). It takes the text after the last `.`, so
`Order API returns 404 for v1.2` becomes `2`, and a name ending in `.` throws `IndexOutOfRangeException`. Thrown inside a sink
under synchronous reporting, that crashed the test host in 3 of 3 runs. R1 fixes it in the core:
- no input throws;
- a dotted prefix is removed only when it is an identifier path, that is, with no whitespace before the first `(`;
- casing is left as it is, since that is #139.

Kronikol.xUnit3 already passes display names to the formatter (`TestContextEnumerableExtensions.cs:29`), so the fix reaches
xUnit v3 suites too (Q8).

### 4.6 Kronikol's own framework (R1)

- **`ReportingTestFramework` and `ReportingTestFrameworkExecutor` stay public and keep their behaviour.** Removing or renaming
  either would be a major.
- **The executor uses the new pieces.** Its `RunTestCases` puts the sink and queue of 4.1 to 4.3 between the bus and the
  runner's sink, and runs `XunitTestAssemblyRunner` as now, with the copied options.
- **Reports move to `ITestAssemblyFinished`.** They are written there, before the runner sees it. Today they are written after
  the runner has finished. That is harmless while the executor blocks, but one code path for both entry points is the point.
- **The old matching code goes.** `TestResultCapturingSink`, `ApplyResults` and `TestOutcome` are internal and are replaced.
  `ReportLifecycle.GenerateReports` stays internal, and both paths read `ReportLifecycle.Options`.

### 4.7 The surface (R2)

| Member | What it does |
|---|---|
| `public static ITestFrameworkExecutor WithKronikolReporting(this ITestFrameworkExecutor executor)`, in a new `public static class TestFrameworkExecutorExtensions` | Returns an internal decorator. `RunTests` and `RunAll` copy the options with synchronous reporting on and put the sink and queue of 4.1 to 4.3 in front of the runner's sink. `Deserialize` passes through, and `Dispose` does too, once. Wrapping an executor that already reports (Kronikol's own, or one already wrapped) returns it unchanged. The decorator and the sink derive from `LongLivedMarshalByRefObject`, as xUnit's own sinks do (F10) |

The decorator reads `ReportLifecycle.Options` as Kronikol's own framework does, so a suite configures its reports in one
place whichever framework it runs. For the reporter's framework the consumer writes:

```csharp
[assembly: TestFramework("MyTests.ReportingAssemblyFixtureFramework", "MyTests")]

public sealed class ReportingAssemblyFixtureFramework(IMessageSink messageSink) : AssemblyFixtureFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        base.CreateExecutor(assemblyName).WithKronikolReporting();
}
```

**Frameworks that need more:**
- **A sealed framework** (Xunit.DependencyInjection's) is wrapped by composition: an `ITestFramework` of the consumer's own
  holds it, passes `SourceInformationProvider`, `GetDiscoverer` and `Dispose` through, and wraps what its `GetExecutor` returns.
  The class derives from `LongLivedMarshalByRefObject` (F10). RUN: probe F, Xunit.DependencyInjection 9.9.2
  (`probe/F.DependencyInjectionPrototype/KronikolDiFramework.cs`, 30 lines): constructor injection worked in 6 of 6 runs, and
  each run wrote a report.
- **A package that generates its own attribute** has a switch to stop it (E, C): `EnableXunitDependencyInjectionDefaultTestFrameworkAttribute`
  and `IncludeMeziantouXunitParallelTestFramework` set to `false`. A Reqnroll.xUnit suite uses Kronikol.ReqNRoll.xUnit2 and
  needs none of this.
- **The wiki gives each recipe.**

**Not public:** the sink, the decorator's type, and `ReportLifecycle.GenerateReports`. The issue's first suggestion would make
them public (Q2).

### 4.8 What each kind of test gets

| The test | xUnit reports | Kronikol 4.9.0 reports (probe A, 4 runs) | After R1 (the prototype, probe C, 3 runs) |
|---|---|---|---|
| A fact, passing or failing | its result | its result | the same |
| A failing `[Fact(DisplayName = …)]` | Failed | Passed, no duration, under its method's name | Failed, with its error and duration, under its DisplayName |
| A theory's rows | each row's result | names, verdicts, durations and calls swapped, differently each run; 2 to 4 rows' calls in the background section | each row its own |
| Rows of a theory with non-serializable data (one test case) | each row's result | 2 of 3 swapped | each row its own |
| `[Fact(Skip = …)]` | Skipped | absent | Skipped |
| Constructor, `InitializeAsync` or class fixture throws | Failed | absent | Failed, with the error and no calls |
| Any test, under `methodDisplay: method` | its result | Passed, no duration (probe A2) | its result (INFERRED: the key never reads the name) |
| A test with no result (a cancelled run, A1) | none | Passed | Passed, marked `ResultDefaulted` (INFERRED) |
| A test `TestTrackingAttribute` does not apply to | its result | absent | absent |
| Any test, on the collection-fixture path | its result | Passed, no duration (probe E) | Passed, marked `ResultDefaulted` (Q7) |

## 5. Tests, red first

**The lane (S0).** Each framework shape gets a fixture assembly under `tests/`, holding the harness's deliberately failing tests
(`probe/Shared/`) plus one class `TestTrackingAttribute` does not apply to. Facts in `Kronikol.Tests.xUnit2` run each fixture
with `dotnet test` in a child process, as `Example.Api.Tests.Integration` runs the examples (`TestProjectRunner.cs:34-49`), and
read its TRX and its report. They read the report through the report's own reader, never as text. A child process is not
optional: `ReportLifecycle`'s once-per-process guard, `CollectedScenarios` and the process-wide call log would carry one
fixture's run into the next.

The fixtures fail by design, so they go on CI's auto-discovery skip list (`ci.yml:283`). They also run with
`KRONIKOL_HISTORY=off`: the harness's first run wrote a history ledger at the checkout's root, where the ledger is looked for
above the test output. The facts that read the own-framework fixture are red on 4.9.x as written. They need no stub, since
they go through `dotnet test`.

| # | Fact | Fixture | Red on 4.9.x | Slice |
|---|---|---|---|---|
| T1 | A failing `[Fact(DisplayName = …)]` is reported Failed, with its error and duration, under its DisplayName | own | Passed, no duration, method name (F1) | S1 |
| T2 | Each row of a six-row theory whose rows call and sleep for different times (row 4 failing) carries its own name, verdict, duration and call, and no row's call is in the background section; the fixture runs three times | own | 3 to 6 rows wrong per run (F1) | S1 |
| T3 | Each row of a theory whose data cannot be serialized carries its own name and call | own | 2 of 3 wrong (F1) | S1 |
| T4 | A tracked `[Fact(Skip = …)]` is reported Skipped with its reason. An untracked skipped test and an untracked failing test are not in the report | own | absent (F2). The untracked half passes on 4.9.x: it guards against a scenario for every test | S1 |
| T5 | Tests whose constructor, `InitializeAsync`, class fixture or constructor argument fails are each reported Failed, with the exception's message and no calls | own | absent (F2) | S1 |
| T6 | Under `methodDisplay: method`, every test keeps its own result | own, with `xunit.runner.json` | all Passed (F3) | S1 |
| T7 | With `ReportLifecycle.Options` never set, the report's run start is no later than its first scenario's start, and its end no earlier than its last scenario's end | own | start equals end (F5) | S1 |
| T8 | The sink, driven with xUnit's message types in unit facts: an untracked test gets no scenario; `ITestStarting` then `ITestFinished` with no result gives a `ResultDefaulted` scenario and the diagnostic; the `AsyncLocal` is gone once the test's flow returns | unit | `ScenarioInfo` has no such flag | S1 |
| T9 | The queue, with 20 threads sending at once: the runner's sink is never called by two threads at the same time and gets every message in the order the bus sent it. A `false` from the runner's sink reaches the bus on a later message, and an exception from it becomes an `ErrorMessage`, after which `ITestAssemblyFinished` still arrives | unit | a new type (mutation: pass messages on inline) | S1 |
| T10 | The reports are complete when `dotnet test` returns, for a report that is slow to write (a fixture with many diagrams), under both adapters | own (S1), AssemblyFixture (S5) | mutation: pass `ITestAssemblyFinished` on before writing | S1, S5 |
| T11 | A report that cannot be written, because the reports folder path names a file, leaves the runner's results whole and the host's exit normal, and writes `kronikol-error.log` | own | passes on 4.9.x as a guard; mutation: let it throw | S1 |
| T12 | `dotnet test --list-tests` writes no report and no error log | own | guard | S1 |
| T13 | `FormatScenarioDisplayName` gives a name for "Order API returns 404 for v1.2", "Does the thing.", "" and "Ns.Class.M(x: Foo())", and none of them throws; `Ns.Class.Method(row: 1)` still gives `Method [row: 1]` | `Kronikol.Tests` (`ScenarioTitleResolverTests`) | "2", and two throws (F6) | S2 |
| T14 | On the collection-fixture path every scenario is `ResultDefaulted`, `Failures.md` says the results are defaults, and `Specifications.html` is not written as for a passing run | no framework | all Passed, `Specifications.html` in full (F4) | S3 |
| T15 | AssemblyFixture with `WithKronikolReporting()`: the report is written, T1 to T5 hold, and the assembly fixture is created once and injected | AssemblyFixture | no report (C1) | S5 |
| T16 | Xunit.DependencyInjection by composition: constructor injection works, the report is written, and T1 and T2 hold | DependencyInjection | no report | S5 |
| T17 | Wrapping twice, or wrapping Kronikol's own executor, writes one report and records each result once | unit, and own | mutation: wrap regardless | S5 |
| T18 | Under a framework that keeps the async bus (its runner overrides `CreateMessageBus`), facts without a DisplayName get their results and calls, the run records the `Other` diagnostic, and skipped tests are present | async bus | no report | S5 |
| T19 | The fixture facts run under xunit.runner.visualstudio 2.8.2 and 3.1.5 | the lane's parameter | | S0 |
| T20 | The options object the runner hands over is unchanged after the run, because synchronous reporting is set on a copy | unit | mutation: set it on the runner's object | S1 |
| T21 | A call made in a test class's constructor is not under that test's scenario (Q6 as recommended) | own | passes on 4.9.x as a guard | S1 |

Mutations to run once each, every one caught by the fact named:

| Mutation | Caught by |
|---|---|
| Pair results by display name again | T1, T2, T3, T6 |
| Key by `ITestCase.UniqueID` instead of the `ITest` reference | T3 |
| Create no scenario at `ITestStarting` | T4, T5 |
| Create a scenario for every test | T4's untracked half, T8 |
| Turn synchronous reporting off | T2: the calls are lost (F7) |
| Pass `ITestAssemblyFinished` on before writing | T10 |
| Let a write failure leave `OnMessage` | T11 |
| Write the reports from `Dispose` | T12 |
| Set the option on the runner's object | T20 |
| Pass messages on inline, without the queue | T9 |
| Drop the `ResultDefaulted` mark | T8, T14 |
| The formatter's old rule | T13 |
| Open the identity window at `ITestStarting` | T21 |
| Take the run's start from `ReportLifecycle`'s initialiser | T7 |
| Drop the fallback pairing of section 4.4 | T18 |

## 6. Slices, releases and records

### 6.1 Slices

| Slice | What | Bump |
|---|---|---|
| **S0** | The test lane: fixture assemblies under `tests/` that hold the deliberately failing tests of section 5, one per framework shape. S0 adds Kronikol's own framework and no framework (the collection-fixture path); S5 adds AssemblyFixture with the decorator, Xunit.DependencyInjection by composition, and a framework that keeps the asynchronous bus (section 4.4), since they need R2's extension to compile. Facts in `Kronikol.Tests.xUnit2` run each with `dotnet test` in a child process, under both VS adapters, and read the TRX and the report through the report's own reader. The fixtures go on CI's auto-discovery skip list, since they fail by design. The own-framework facts start red on 4.9.x | none alone |
| **S1** | The sink, the queue, the hand-over in `Before`, the end-of-run steps, the trigger at `ITestAssemblyFinished`, and `ReportingTestFrameworkExecutor` on them (sections 4.1 to 4.3, 4.6); T1 to T12, T20, T21 | patch |
| **S2** | The formatter fix in the core (section 4.5); T13 | patch |
| **S3** | `ResultDefaulted` on the collection-fixture path (Q7); T14 | patch |
| **S4** | R1's documents: section 6.4's R1 rows; the doc comments that describe the old behaviour (`ReportingTestFramework`'s and `ReportingTestFrameworkExecutor`'s summaries, `TestResultCapturingSink`'s goes with it, `DiagrammedTestRun`'s note, and the no-scenario `CreateStandardReportsWithDiagrams` overload, which should say results are not captured on that path); the changelog | with R1 |
| **S5** | `WithKronikolReporting()` and the decorator (section 4.7), and the fallback of section 4.4, which only a decorated framework can reach (Kronikol's own executor always sets the option); T10 again, T15 to T18 | minor |
| **S6** | R2's documents: the wiki section "Already using another test framework", section 6.4's R2 rows, the changelog | with R2 |

### 6.2 Releases

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| **R1** | S0 to S4 | patch | Bug fixes. Results reach their own scenarios, tests the report dropped appear, an unmeasured verdict says so, and the formatter cannot throw. Nothing is new for a consumer to call. The changelog calls out what readers will see change: DisplayName facts are named after their DisplayName, skipped tests and early failures appear, the run's start time moves, and the collection-fixture path's results read as defaults |
| **R2** | S5, S6 | minor | `WithKronikolReporting()` and its class are new public surface. `CLAUDE.md`: anything new for a consumer to call is a minor |

R1 can ship alone and first (rule 1). R1 waits for the answers to Q1, Q3 and Q5 to Q8; R2 for Q2, Q4, Q10 and Q11.

### 6.3 Changelog drafts

> **Patch - xUnit v2 results reach the right scenario, and the tests the report dropped appear (#123).**
> Kronikol.xUnit2 paired each test's result with its scenario by method name. A failing `[Fact(DisplayName = ...)]` was
> therefore reported as passed, with no error and no duration. The rows of a theory swapped verdicts, durations and calls, and
> a row's own call could land in the background section. Each result is now paired with its scenario by the test's identity,
> so every row and every DisplayName fact gets its own, and a DisplayName fact is named after its DisplayName. A tracked test
> that never reaches its first line is now reported: a skipped test as Skipped, and a test whose constructor,
> `InitializeAsync`, fixture or constructor arguments fail as Failed, with the error. A scenario that gets no verdict (a
> cancelled run, or the collection-fixture path, which captures no results) is reported with `ResultDefaulted`, as `kronikol
> ingest` reports one, where it read as a plain pass. The run's start is the time the run started, where it could be the time
> it ended. A display name ending in `.` no longer stops the reports from being written, and a sentence-like display name is
> no longer cut at its last `.` (xUnit v3 suites too). The patch part moved because nothing is new for a consumer to call.

> **Minor - Kronikol.xUnit2's reports under another test framework (#132).** A suite whose `[assembly: TestFramework]` is
> taken by another framework (Xunit.Extensions.AssemblyFixture, Xunit.DependencyInjection, Meziantou.Xunit.ParallelTestFramework
> and others) gets Kronikol's reports by wrapping that framework's executor: `base.CreateExecutor(assemblyName).WithKronikolReporting()`.
> The wrapper turns on xUnit's synchronous message reporting for the run, and passes messages to the runner from a single
> queue, as xUnit's own message bus does. The minor part moved because `WithKronikolReporting()` is new public surface.

### 6.4 Wiki

| Where | Change | Release |
|---|---|---|
| `Integration-xUnit2.md:76` | Results are paired with each test by its identity; DisplayName facts and theory rows each get their own; skipped tests and tests that fail before running appear | R1 |
| same, `:342-356` (the architecture drawing, and the table naming `TestResultCapturingSink`) | Describe the sink by what it does, not by an internal type's name | R1 |
| same, `:365-387` (the collection-fixture alternative) | Its results are reported as defaults (Q7); a suite on another framework should use R2's wrapper instead | R1, then R2 |
| same, `:49` | The sample `csproj` pins 2.31.0; pin the current version | R1 |
| `Generated-Reports.md:706-710` | Says reports are generated when the test run fixture is disposed, which is wrong for xUnit v2's framework | R1 |
| `Generated-Reports.md:531-535` (the status table's xUnit v2 column) | Skipped is now reported | R1 |
| `Integration-xUnit2.md`, new section "Already using another test framework" | The three-line subclass; composition for a sealed framework; the MSBuild switches that stop a generated attribute; what the wrapper turns on and why; pointers to Kronikol.ReqNRoll.xUnit2 and Kronikol.LightBDD.xUnit2 for those suites | R2 |
| `Framework-Integration-Guides.md:17-28` | One line: works under another xUnit v2 framework through `WithKronikolReporting()` | R2 |

Before each release, grep the wiki for `ReportingTestFramework`, `TestFramework(`, `ReportLifecycle` and `DiagrammedTestRun` for
pages this list missed.

### 6.5 Kronikol4J

- **R1** changes xUnit v2 output only, for which Kronikol4J has no counterpart. The exception is the formatter, which is in the
  core. Check whether Kronikol4J ports `FormatScenarioDisplayName`. If it does, either it follows the fix or the divergence
  ledger records the difference.
- **R2** adds .NET-only surface and changes no output for existing inputs, so it needs no ledger line.

### 6.6 Before declaring done

- **Tick section 5 fact by fact against the diff.** Every fact must have been red on the release before (the tests copied into
  a worktree at the last tag), and every mutation caught.
- **Re-run the harness on the release's packages.** Probes A (Kronikol's framework) and C (AssemblyFixture) must show every test
  with its own verdict, duration and calls, with no mismatch in `compare.py`.
- **The CI lane ran the fixture facts.** Read the job's log for their names, and check that the fixtures are on the skip list.
- **Build `release.slnf` in Release for every target, net8.0 included.**
- **The consumer.** BreakfastProvider has no xUnit v2 lane (READ, `C:/Code/BreakfastProvider`: xunit.v3 only), so the
  example project's run is the in-repo check. R2 is done when #132's reporter replaces their 150-line framework with the
  wrapper and their suite's report matches its runner's results.

## 7. Where it sits in the roadmap

**Taken 2026-10-08:** stage 1 row 1.24 and decision D38, announced to the other sessions first (1.21 went to #135, 1.22 to
#136 and 1.23 to #141). R1 is marked **patch** and R2 **minor**, placed by **rule 1** ("What is shipped and wrong comes
before what is new"): #123 is a shipped framework reporting failed tests as passed. #132's own part, R2, is new surface and
follows R1 directly, since it reuses R1's sink.

## 8. Found on the way, not in this plan

| What | Level | Where it goes |
|---|---|---|
| **#133**, filed with #132: outside a test, `XUnit2TestTrackingContext.GetCurrentTestInfo()` answers with a new random id instead of `unknown`, so nothing falls through to the scope, the global fallback or the background. Its likely fix edits `TestTrackingAttribute.cs` and `XUnit2TestTrackingContext.cs`, which R1 edits too (kronikol-28's assessment, 2026-10-07). It is not green-lit | READ | Its own decision. Whichever of the two is green-lit second rebases on the other. Not folded in: it changes attribution outside tests, which this plan does not touch |
| Calls made in a test class's constructor, `InitializeAsync`, `DisposeAsync` or `Dispose` run outside the identity window and go under a new id each (kronikol-28, RUN on 4.9.0) | RUN (peer) | Q6, with #133 |
| The xUnit v3, MSTest and NUnit 4 adapters capture a test in a per-test hook that a statically skipped test or a constructor failure never reaches, so those tests are missing from their reports too | INFERRED (`Kronikol.xUnit3/DiagrammedComponentTest.cs:30-36`; `Kronikol.MSTest/DiagrammedComponentTest.cs:32-68`; `Kronikol.NUnit4/DiagrammedComponentTest.cs:22-30`) | A new issue at the owner's word, once measured. Each adapter needs its own mechanism |
| **#139**: the formatter lower-cases every word after the first, so `API` reads `api` | ISSUE | #139. Section 4.5's fix leaves casing alone |
| VS 3.1.5 honours an undocumented `"synchronousReporting": true` in `xunit.runner.json` for v2 assemblies (A4). A suite that sets it already has its runner's sink called from every test thread | READ, RUN (research) | Nothing to do. Section 4.2's queue makes it harmless with the wrapper too |
| Xunit.Extensions.AssemblyFixture 2.6.0 calls a fixture's `DisposeAsync` twice on net48, and its assembly runner override bypasses xUnit's `parallelSemaphore` | INFERRED (research, B) | Upstream, if anyone wants it; Kronikol does not depend on either |
| Older plans cite `ReportingTestFramework.cs` by line (`CROSS_RUN_HISTORY_PLAN.md:965`, `CUCUMBER_BYPASS_PLAN.md:91`), and those lines move | READ | Nothing; they are records of their day |
| The report drops calls whose test id names no scenario from every report file (F7). The only trace is a console count line, which `dotnet test` does not show at its default verbosity, and `DiagnosticReport.html` under `DiagnosticMode`. No `DiagnosticKind` carries it outside `kronikol ingest` (`IngestPipeline.cs:785`, `:914`), so it reaches no labs page and no `kronikol query` answer (kronikol-28, READ). #133's random ids meet the same silence | RUN, READ | A structured diagnostic for such calls would make both visible. A separate issue, at the owner's word |
| `Specifications.html` is written in full for a run whose failures the report never saw (probes A2 and E). It follows the report's verdicts, so R1's fixes cover the xUnit v2 cases | RUN | Nothing beyond R1 |

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Fold #123 into this plan as R1, or leave it for a plan of its own | **Fold it in, as R1, first.** R2's decorator needs a sink that pairs results correctly. Built on the name matcher, it would carry #123's swaps into a new public path. #123 has been open since 2026-09-30 with no plan, and its suggested fix is the technique #132 reports, so one sink closes both |
| Q2 | The surface: the decorator alone, or also the issue's first suggestion, a public sink (`KronikolResultsSink(IMessageSink inner)`) and a public `ReportLifecycle.GenerateReports()` | **The decorator alone.** Every framework hands xUnit an `ITestFrameworkExecutor`, so the decorator reaches everything a public sink would. It also keeps the sink's message handling, the queue and the report trigger out of the contract. A public sink would invite composing it without synchronous reporting, where the hand-over fails quietly. If a consumer turns up whose executor cannot be wrapped, the sink can be made public then, as a minor |
| Q3 | How messages reach the runner's sink: through Kronikol's own ordered queue (section 4.2), or straight through on the test's thread, as the reporter's workaround does | **The queue.** Speed does not decide it: synchronous reporting cost nothing measurable (0.841 s against 0.827 s of xUnit's time over 2,000 facts, `results/perf/perf.tsv`), and the reporter's suite ran without a queue. The deciding fact is not the cost but who calls the runner's sink: forced synchronous reporting calls it from every test thread at once, which neither VS adapter does on its own, and VS 2.8.2's sink is not written for it (E4). Kronikol cannot test every runner that drives xUnit v2 (an IDE's own runner, for one), and the queue gives each the delivery it gets without Kronikol. The queue is a small class with facts of its own (T9). The alternative saves that class and leaves the runner's sink to cope with concurrent calls |
| Q4 | The name | **`WithKronikolReporting()`**, an extension on `ITestFrameworkExecutor`, after the `With...` names of Kronikol's other wrappers (`WithTestTracking`, `WithClickHouseDriverTestTracking`). The alternatives are `ReportingTestFramework.Wrap(executor)`, beside the type users already know, and the issue's `KronikolReporting.Wrap(executor)`. The owner picks |
| Q5 | Report skipped tests and tests that fail before their first line runs | **Yes, in R1.** xUnit counts them. The shipped sink already tried to record skips (`ReportingTestFramework.cs:103-110`) but had no scenario to put them on, and a test that failed in its constructor is a failure that the report and `Failures.md` should show. Only tests `TestTrackingAttribute` applies to are reported, so a suite's untracked tests stay out. The xUnit v3, MSTest and NUnit adapters omit statically skipped tests too (INFERRED); that is in section 8, not this plan |
| Q6 | Where the identity window opens: at `Before`, as now, or at `ITestStarting`, which would put calls made in the test's constructor, `InitializeAsync` and `Dispose` under its scenario | **At `Before`, as now.** The reporter's own framework kept it there (#123's comment). Moving it changes what existing reports draw: a call made in a constructor would join the diagram. With the sink in place, the wider window is a small change later, and it belongs with #133, whose assessment found that those calls each go under a new id today |
| Q7 | A scenario with no result: `Passed` with `ResultDefaulted`, or `Passed` as now. Also: the documented collection-fixture path, where no scenario ever gets a result | **`ResultDefaulted`, on both paths.** It is what `kronikol ingest` does, and it tells the reader that the verdict was not measured. On the fixture path that marks every scenario of every run, which is the truth (probe E: 15 of 15 Passed while 6 tests failed, F4). The wiki then sends those suites to R2's decorator, and the changelog calls the change out |
| Q8 | Fix `FormatScenarioDisplayName` for sentence-like names in the core, which reaches xUnit v3 suites too | **Yes, in R1.** R1 is what starts passing DisplayNames to it from xUnit v2, and a name ending in `.` makes it throw, which inside a sink crashed the test host in 3 of 3 runs (F6). The xUnit v3 adapter has passed such names to it all along. Casing stays as it is (#139) |
| Q9 | A zero-code alternative: Kronikol's framework composing another one, named by an assembly attribute of Kronikol's, so the consumer writes no class | **Not now.** Nobody asked for it, it would be a second public mechanism, and a package that generates its own attribute would still need its switch turned off. The decorator is three lines |
| Q10 | The diagnostic when the hand-over fails (section 4.4, in R2): kind `Other`, or a new `DiagnosticKind` member | **`Other`.** R2 is a minor either way, so the bump does not decide it. A kind of its own would let a reader filter on it, but only a consumer who meets the fallback would ever see one, and none of the five frameworks checked would. A member can be added later if one does |
| Q11 | Where the decorator takes its report options from: `ReportLifecycle.Options`, or an overload taking `ReportConfigurationOptions` | **`ReportLifecycle.Options`.** One place to configure, whichever framework runs the suite, and the example and the template already set it there (`TestRun.cs:19`, template `:13`). An overload can be added later as a minor |

## 10. Assumption ledger

| # | Assumption | Level | What would change |
|---|---|---|---|
| A1 | The reporter's framework is an `AssemblyFixtureFramework` subclass, as the issue's code shows | ISSUE | Nothing in R1. If it were a sealed or composed framework, the wiki's composition recipe applies |
| A2 | No framework a consumer uses overrides `CreateMessageBus` to keep the asynchronous bus | READ, for five (A3) | Section 4.4's fallback applies, and its diagnostic says so |
| A3 | A .NET test host runs one test assembly, and Kronikol.xUnit2 targets net8.0 to net10.0, so there are no AppDomains | RUN (`dotnet test A.dll B.dll`: two processes, A6), READ (`Directory.Build.props:6`) | If a host ran two assemblies, each executor still has run state of its own (section 4.3). Only the shipped path's once-per-process guard is shared |
| A4 | A Test Explorer re-run in a kept-alive host creates a new executor | INFERRED (a new front controller per assembly per call, A6) | If an executor were reused, the decorator's state would need resetting per `RunTests` call; T-facts cover one call only |
| A5 | `ITestAssemblyStarting` precedes every test message, and `ITestAssemblyFinished` follows the last one | READ (`TestAssemblyRunner.cs` L197-222) | If a run is cancelled before it starts, no `ITestAssemblyFinished` is sent (L203) and no report is written, as today |
| A6 | Rider and Visual Studio run xUnit v2 through the same VS adapter as `dotnet test` | INFERRED | A runner that never passes `ITestAssemblyFinished` on to a sink of its own would still get the reports, since they are written before the message is passed on |
| A7 | The runner sinks accept messages from one thread, in order, as xUnit's default bus delivers them | READ (A3, E4) | What the queue (section 4.2) preserves. Without it, Q3's measurement applies |

## 11. Log

- **2026-10-07.** Written at 4.9.0 (`417c8e58`) in the worktree `Kronikol-xunit2fw132`, the day #132 was filed together with
  #133 and a comment on #123 from the same integration.
  - **The harness.** It ran on net8.0 with xunit 2.9.3 and the 2.8.2 adapter (one run on 3.1.5), Xunit.Extensions.AssemblyFixture
    2.6.0 and Xunit.DependencyInjection 9.9.2, against Kronikol.xUnit2 built from `417c8e58`. It made 30 runs over twelve lanes,
    15 timed runs and a direct probe of the formatter (`results/SUMMARY.md`).
  - **The xUnit sources** were read at the tags in the header, with probe projects of their own for the claims about flow,
    completion and failure (`research/xunit-v2-sources.md`).
  - **Other sessions.** kronikol-28 assessed #133 read-only the same day and reported the constructor and `Dispose` finding
    (section 8). No session held #123, #132 or #133. Roadmap row 1.21 and D37 went to #135 the same day.
- **2026-10-08. Green-lit and executed.** The owner: "Can you implement the plan in full, in a separate worktree so you don't
  interfere with the other sessions. Fix any other problems you found", so Q1 to Q11 are taken as recommended. Executed by
  kronikol-1c in the worktree `Kronikol-xunit2impl` (branch `xunit2-132`, off `e59305da`, 4.9.1; rebased onto 4.11.0,
  `709b3afc`, before the release). Row 1.24 and D38 were taken by message. kronikol-28, releasing #133 as a patch of its
  own, changes only the `Track.TestIdResolver` lambda in `Before`, which R1 leaves alone, and kronikol-50 (#141) agreed the
  edit to `ReportGenerator.cs`, whose other parts it edits.
  - **Built.** R1: the internal `KronikolResultsSink`, `OrderedMessageForwarder` and `SynchronousReportingOptions`; the
    hand-over through `XUnit2TestTrackingContext` and `TestTrackingAttribute.Before`; `ReportingTestFrameworkExecutor` on
    them; `ResultDefaulted` on both paths (`XUnit2ReportGenerator`); the specifications rule and the formatter in the core.
    The lane is `tests/Kronikol.Tests.xUnit2.Fixtures/` (three fixture projects, no project at its root, on CI's skip
    list) and `tests/Kronikol.Tests.xUnit2/Lane/`.
  - **Where it departs from the plan.**
    - T4: a skipped test is reported Skipped without its reason. Kronikol's adapters give a skipped scenario no
      `errorMessage`, a rule the MSTest adapter states (`Kronikol.MSTest/DiagrammedComponentTest.cs:55-57`), so T4 asserts
      Skipped, no calls and no error.
    - Section 4.5's rule, refined while writing T13: a dotted prefix is removed only from a method path (no whitespace,
      and a last part that starts with a letter or `_`), parameters are read only from a name that ends in `)`, and an
      argument's own closing parenthesis is kept. A theory holds 22 inputs, the plan's among them, to not throwing.
    - The specifications rule is in the core (`ReportGenerator.CannotVouchForSpecifications`), so it also blanks
      `Specifications.html` for `kronikol ingest`'s defaulted results.
    - `TestRunReport.json` has no per-scenario `ResultDefaulted` field, so the flag is not in it. The `ResultDefaulted`
      diagnostic is, and `Failures.md` quotes it; T8 and T14 assert the diagnostic.
    - Section 5's mutation "write the reports from `Dispose`" was not run: a listing run holds no scenarios and an empty
      run writes nothing on either path, so T12 cannot tell. T12 stays a guard.
  - **Found and fixed on the way, in R1.** A passing xUnit v2 scenario carried an `errorMessage` and `errorStackTrace` of
    `""` where both should be absent (`ScenarioInfoCollectionExtensions`). `kronikol-error.log` was overwritten by each
    failure and is now appended. `ResolveScenarioTitle` threw for an empty method name, and `AppendTestParameters` cut an
    argument's closing parenthesis. `ReportLifecycle.WriteReports` no longer catches: the sink is the one place a write
    failure is caught and logged.
  - **R1, red first** (harness `r1/`). With the tests copied onto `v4.9.1`, 23 lane facts failed and 7 passed
    (`red491-lane-at-v4.9.1.txt`). Six of the seven are guards: T11, T12 and T21 under each adapter. The seventh, T3 under
    2.8.2, passed in that run by 4.9.1's random pairing, which got it wrong in the harness's runs. The 21 new formatter facts failed
    (`red491-formatter-at-v4.9.1.txt`). T8, T9 and T20 drive types 4.9.1 does not have.
  - **R1 mutations** (`r1/mutate_r1.py`, `r1/mutations-r1.txt`): 15 of 15 caught. Two survived their first run and were
    caught once the fact was fixed. Keying by `ITestCase.UniqueID` (M2) is caught by the sink's unit fact alone, since
    xUnit runs a test case's rows one after another and the lane cannot tell. Passing `ITestAssemblyFinished` on before
    writing (M6) is caught by the sink's unit fact, which now records the first arrival: Kronikol's own executor blocks
    until its run ends, so T10 is a guard there. Pairing by display name again (M1) is caught by T1 and T6 only, because
    rows that run in order pair right by name as their results arrive.
  - **Suites.** Verified at `89a2e879`, R1's code on 4.11.0, which the release commit keeps (the rebase onto `a81d5e03` brought
    only another plan's files): the core suite 6,937 passed with 2 skipped; Kronikol.Tests.xUnit2 58 of 58, the lane among
    them, and 58 of 58 on Linux, in a .NET 10 SDK container with the .NET 8 runtimes, as CI runs it; the example's xUnit v2 project 2 of 2 and its 30 integration facts; the Playwright suite
    994 of 995, the one failure a 60-second wait for a merged report's diagrams while another build loaded the machine,
    which passed alone (its class 4 of 4) and in the full run of R1's code before the rebase (995 of 995); `release.slnf`
    built in Release for every target. Every other `tests/*` project passed on R1's code before the rebase.
