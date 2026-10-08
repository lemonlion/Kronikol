# Adapter capture gaps: the tests xUnit v3, MSTest, NUnit 4 and TUnit leave out of their reports

**Written:** 2026-10-08, at 4.12.0, by kronikol-1c, from a probe of the four adapters run that day at 4.11.0 (`a81d5e03`).
**Status: R1 released as 4.13.3 (with a follow-up in 4.14.2), and R2 and R3 together as 4.14.4**, both at the owner's "fix any other problems you found" (the instruction that executed
`XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md`, whose section 8 inferred the first of these gaps). **Q1 to Q5 await the owner**:
each needs new public surface or a design choice. Evidence labels: **RUN** (measured by the probe), **READ** (in the source,
`file:line` at `a81d5e03`), **INFERRED**. The probe, its outputs and its feasibility dumps are in the session scratchpad
(`adapters-probe/`, summarised in `adapters-probe/FINDINGS.md`); R1 copies what its facts need into the repo.

## 1. What the probe measured

Each adapter's own template, as shipped (the service name and the template's TODO applied), ran a suite of tests that
pass, fail, skip statically and dynamically, run as data rows, and fail in their constructor and set-up hooks, each making
one tracked call. The runner's TRX was compared with the report, twice per adapter (RUN).

| Adapter | Runner | Report | Missing from the report | Wrong in the report |
|---|---|---|---|---|
| xUnit v3 | 13 tests, 6 failed | 7 scenarios | `[Fact(Skip)]`; constructor, `InitializeAsync` and class fixture failures; **every test of a class that implements `IAsyncLifetime`, passing ones included** | |
| MSTest | 10, 5 failed | 5 | `[Ignore]`; **DataRow rows 2 and 3, row 2 failing** (collapsed into row 1); constructor and `[ClassInitialize]` failures | rows 2 and 3's calls listed as background calls |
| NUnit 4 | 13, 5 failed | 10 | `[Ignore]`; constructor and `[OneTimeSetUp]` failures | `errorMessage` `""` on passing scenarios; skipped scenarios (`Assert.Ignore`, `Assert.Inconclusive`) and a passing one (`Assert.Pass("…")`) carry a message and a stack trace; `Assert.Warn` is reported Passed |
| TUnit | 9, 4 failed | 7 | `[Skip]`; constructor failure | a skipped scenario (`Skip.Test`) carries a message; a `[Before(Test)]` failure has a negative duration |

The causes (READ):
- **xUnit v3** captures in `Dispose()` only (`Kronikol.xUnit3/DiagrammedComponentTest.cs:13,30-36`). xunit.v3 3.2.2 disposes a
  class through `DisposeAsync` when it is `IAsyncDisposable` and through `Dispose` otherwise, never both, and
  `IAsyncLifetime` is `IAsyncDisposable`; a class whose construction or `InitializeAsync` failed is never disposed.
- **MSTest** keys a scenario by `{class}.{TestName}` (`Kronikol.MSTest/DiagrammedComponentTest.cs:48`), the same for every
  data row; `TestContextEnumerableExtensions.cs:22` keeps the first, and the call-attribution id (`CurrentTestInfo.cs:17`,
  resolvers at `DiagrammedComponentTest.cs:23-27` and `DiagrammedTestRun.cs:20-24`) is the same string, so the other rows'
  calls lose their scenario. Capture is `[TestCleanup]`, which a static ignore, a constructor failure and a class
  initialisation failure never reach.
- **NUnit 4** maps `Result.Message` and `Result.StackTrace` whatever the outcome (`Kronikol.NUnit4/TestContextEnumerableExtensions.cs:45-46`)
  and without `FailureText.OrNull`, and maps `Warning` to Passed (`TestResultExtensions.cs:16`). Capture is `[TearDown]`.
- **TUnit** maps the message whatever the outcome and the duration unchecked (`Kronikol.TUnit/TestContextEnumerableExtensions.cs:47-49`).
  Capture is `[After(Test)]`.

The repo's rule, stated at `Kronikol.MSTest/DiagrammedComponentTest.cs:50-59`: only a failed scenario carries an
`errorMessage` and `errorStackTrace`; a skipped one carrying one is a defect, and a passing one has neither.

Also found (RUN): a run whose only failures are kinds an adapter misses reports "# No failures", `failed: 0` and a full
`Specifications.html` while the runner exits non-zero (all four adapters); a run that captures no scenario writes nothing
and leaves the previous run's report in place; and the xUnit v3, MSTest and NUnit templates' sample test fails as shipped
(the test SDK's generated `Main` becomes the entry point, CS8892, so the template's host is never built), which CI misses
because it builds the templates and never runs them.

## 2. Releases

| Release | Contents | Bump |
|---|---|---|
| **R1** | NUnit 4 and TUnit carry a message and stack trace on failed scenarios only, through `FailureText.OrNull`; TUnit's negative duration is no duration; MSTest data rows are scenarios of their own, keyed by display name at the five id sites; xUnit v3 captures a test whose class implements `IAsyncLifetime`; CI runs the example integration facts no entry named (section 5) | patch |
| **R2** | NUnit 4 reports the tests its `[TearDown]` never sees (a static ignore, a constructor or `[OneTimeSetUp]` failure) from the run's result tree in the set-up fixture's `[OneTimeTearDown]`; TUnit reports its static skips and constructor failures from `AssemblyHookContext.AllTests` in `[After(Assembly)]` | patch |
| **R3** | The templates' sample test passes as shipped, and CI runs it (section 4) | patch |

Every fix starts with a fact that fails on 4.12.x for its own reason. R1's MSTest change moves the scenario id of a data
row, which keys its history: the changelog says so. R2 reads `NUnit.Framework.Internal`'s result tree, which NUnit does not
promise to keep; the fact pins the NUnit version it was measured on, and a missing tree degrades to today's behaviour.

## 3. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | xUnit v3's static skips and constructor, `InitializeAsync` and class fixture failures reach only xUnit's message stream. A framework like Kronikol.xUnit2's (`[assembly: TestFramework]`, or a wrapper) sees them, at the cost of new public surface, a template change, and the composition question #132 answered for v2 | Measure first: xunit.v3 may offer a message sink a package can register without taking the framework slot |
| Q2 | MSTest's `[Ignore]` tests and constructor and `[ClassInitialize]` failures reach no hook a test class can use. Static ignores can be found by reflection; failures need a results source (Microsoft.Testing.Platform's `IDataConsumer`, or reading the TRX afterwards) | Reflection for static ignores in R2's shape; the rest waits for the owner |
| Q3 | Silent green: when every failure is one the adapter missed, the report says there were none. Even after R1, R2, Q1 and Q2, a report cannot vouch for a run it did not see whole | A line in `Failures.md` and a diagnostic when the adapter knows it saw fewer tests than ran, where it can know |
| Q4 | A run that captures no scenario writes nothing, so the previous run's report is read as this one's | Write the pointer and `Failures.md` saying no scenario was captured, and rotate the previous run out |
| Q5 | NUnit's `Warning` (`Assert.Warn`) is reported Passed (`TestStatusExtensions`, public), where the VSTest adapter's TRX says NotExecuted, and its text rides in `errorMessage`, the only place the report has for it. R1 keeps both as they were | A warning shown as such (a label, or a note beside the verdict) is new surface; until then, keep the text |

## 4. The templates (R3)

The xUnit v3 template's sample test fails because the test assembly's entry point is xunit.v3's generated
`XunitAutoGeneratedEntryPoint.Main`, not the template's placeholder `Program.Main` (CS8892, RUN), and
`WebApplicationFactory<Program>` runs the entry point to find the host. `<GenerateProgramFile>false</GenerateProgramFile>`
does not help there (RUN): it switches off only Microsoft.NET.Test.Sdk's generated program, which wins in the MSTest and
NUnit templates. Switching off xunit.v3's would make the test executable start the web application. The likely fix is in
the templates' factories: while `Program.cs` is the placeholder, override `CreateHostBuilder` to build the placeholder app,
so no entry point is involved; the TODO that removes `Program.cs` removes the override too. TUnit's template fails only
outside a solution (its factory resolves a solution-relative content root). CI builds the templates and never runs them
(`.github/workflows/ci.yml:353-371`); R3 runs each template's sample test in CI.

## 5. The example integration facts CI never ran (R1)

Found while verifying R1 (RUN, 2026-10-08): `Example.Api.Tests.Integration` runs each component project of
`TestProjects.All` as a child process and reads its report. CI runs that project five times, filtered by project name
(`.github/workflows/ci.yml`, the Integration entries), and no filter names `Component.TUnit`, the row TUnit joined
`TestProjects.All` with in 2.22.27 (`c7a6a7c0`). So the 30 TUnit rows of `ConfigurationOverrideTests`, `FieldFocusTests`
and `ReportGenerationTests`, and the 8 facts of `TUnitParameterizedRenderingTests`, have never run in CI. R1 adds an
Integration remainder entry, as 3.0.64 did for the end-to-end classes: a negative filter over the five entries' names, so a
fact no entry names runs there. The negative filter excludes on both names, as the positive filter matches on both,
since a theory's arguments are only in its display name.

Run locally, 12 of the 38 failed, each on the runner's 120 s timeout and none on an assertion. The runner built the
component project before every run of it, and a build with nothing to do took 76 to 84 s on this machine (other builds
running) against 2 s for the TUnit example's run; CI's Linux builds are fast enough that its Integration entries take
about 5 minutes. R1 builds each project once per test process and starts each run with `--no-build`, which also keeps a
run from compiling source that changed while the suite ran.

## 6. Log

- **2026-10-08.** Written at 4.12.0 from the probe kronikol-1c ran with a background agent at 4.11.0 (worktree
  `Kronikol-adapters-probe`, read-only).
- **2026-10-08.** R1 released as 4.13.3 (kronikol-1c): TUnit, MSTest and xUnit v3 as planned, and NUnit 4 once
  kronikol-28's 4.13.1 had added `tests/Kronikol.Tests.NUnit4`. Decided while executing: NUnit's `Assert.Warn` keeps its
  text, since gating it would drop the only trace of the warning (Q5); the templates moved to R3 (section 4); the
  integration facts CI never ran and the runner's build before every run (section 5) joined R1. The red proofs and each
  adapter's probe before and after are in `ADAPTER_CAPTURE_GAPS_PLAN.harness/r1/`. Checks: on 4.13.1, before the rebase onto 4.13.2, every example component suite and all 254 integration facts passed, the TUnit rows among them, in 30 minutes; on 4.13.2 the TUnit, MSTest, xUnit v3, NUnit 4 and xUnit v2 adapter projects and both LightBDD ones pass (29, 60, 17, 19, 91, 25 and 26), the core suite 7,059 with 2 skipped, the full Playwright suite 997 with 28 skipped when run alone (a run under load timed out one fact waiting for diagrams to render, ArrowLinkOpensPopupTests' merged report, which passes alone), and release.slnf builds in Release for every target.
- **2026-10-08.** 4.13.3 published: Release run 37808467272 passed, and so did CI 37808458586, whose new Integration
  (Remainder) job ran the 38 TUnit facts on Linux while E2E (Remainder) still selected 451; nuget.org listed all 62
  ids at 17:46, the wiki has the adapter pages' edits (`d0feec3`) and Kronikol4J's ledger its line (`93f30da`). The
  same day kronikol-94 found that a row with a display name of its own lost its method in its id, so rows of two
  methods with the same display name shared one. Fixed in 4.14.2 with a class that overloads a test method, which
  failed every overload in clean-up (`GetMethod(name)`); a row MSTest names after its method keeps 4.13.3's id. At
  4.14.2 the MSTest project passes 66 of 66, the core suite 7,098 with 2 skipped, the full Playwright suite 1,003 with
  28 skipped when run alone, and release.slnf builds in Release for every target.
- **2026-10-08.** 4.14.2 published: Release run 37831687433 and CI 37831683901 passed, nuget.org listed all 62 ids at
  20:56, the wiki has the MSTest page's edit (`518d77d`) and Kronikol4J's ledger its line (`619fc10`).
- **2026-10-08.** R2 and R3 released together as 4.14.4 (kronikol-1c), to spare a release cycle while three sessions
  released in turn. R2: NUnit 4 adds the tests its tear-down never sees from the run's result tree in the set-up
  fixture's `[OneTimeTearDown]`, and TUnit from `AssemblyHookContext.AllTests` in the `[After(Assembly)]` hook
  (reached through `AssemblyHookContext.Current`, so no template or signature changes); a test of a class that does
  not derive from `DiagrammedComponentTest` stays out, as does a TUnit test with no result, which a filtered run
  showed covers the tests a filter leaves out (`r2/tunit-filtered.txt`). That closes Q3's silent green for these two
  adapters; xUnit v3 and MSTest wait on Q1 and Q2. A fact pins the NUnit version the tree reading was measured on
  (4.6.0), as section 2 said. R3: measured first with every template as published, 21 of 24 runs failed
  (`r3/before/`), for three causes: the entry point section 4 names, no content root outside a solution, and two xUnit
  v3 templates (BDDfy, ReqNRoll) with no `Microsoft.NET.Test.Sdk`, whose test host never started. Section 4's
  `CreateHostBuilder` override was not enough on its own: `WebApplicationFactory` looks for the content root after the
  builder is made, whatever root it sets, and in .NET 10 reads `MvcTestingAppManifest.json` (always written to the
  output) rather than the content-root attribute, so the placeholder factory names its root through the
  `TEST_CONTENTROOT_<assembly>` setting, read from the environment on the generic host. `WebHostBuilder`, which the
  TUnit templates used, is obsolete in .NET 10 (ASPDEPR004) and no template uses it now. CI runs each scaffold's
  sample test, and `TemplateTestHostTests` holds the test host and the builder. Checks, on the branch rebased onto
  4.14.3: the NUnit 4 and TUnit adapter projects pass 25 and 35, the full Playwright suite 1,003 with 28 skipped when
  run alone, release.slnf builds in Release for every target, and the core suite passes 7,110 with 2 skipped before
  the version bump and after it. On 4.14.2, the LightBDD TUnit project passed 25, the example NUnit 4 and TUnit
  component suites 2 and 12, and the integration facts that run them 30 and 38.
- **2026-10-08.** 4.14.4 published: Release run 37842750716 and CI 37842747718 passed, and CI's template job ran the
  twelve scaffolds' sample tests for the first time, all passing; nuget.org listed all 62 ids at 22:15, the wiki
  has the NUnit, TUnit and project-template pages' edits (`83e5802`) and Kronikol4J's ledger its two lines (`0e1a3d6`).
  The template package's README still said to delete only `Program.cs`, which now leaves `PlaceholderApiFactory`
  without its `Program`; it is fixed under the changelog's `[Unreleased]`, for the next release to carry.
