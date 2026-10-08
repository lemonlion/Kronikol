# Adapter capture gaps: the tests xUnit v3, MSTest, NUnit 4 and TUnit leave out of their reports

**Written:** 2026-10-08, at 4.12.0, by kronikol-1c, from a probe of the four adapters run that day at 4.11.0 (`a81d5e03`).
**Status: executing R1 and R2** at the owner's "fix any other problems you found" (the instruction that executed
`XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md`, whose section 8 inferred the first of these gaps). **Q1 to Q4 await the owner**:
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
| **R1** | NUnit 4 and TUnit carry a message and stack trace on failed scenarios only, through `FailureText.OrNull`; TUnit's negative or start-less duration is no duration; MSTest data rows are scenarios of their own, keyed by display name at the three id sites; xUnit v3 captures a test whose class implements `IAsyncLifetime`; the templates' sample test passes as shipped, and CI runs it | patch |
| **R2** | NUnit 4 reports the tests its `[TearDown]` never sees (a static ignore, a constructor or `[OneTimeSetUp]` failure) from the run's result tree in the set-up fixture's `[OneTimeTearDown]`; TUnit reports its static skips and constructor failures from `AssemblyHookContext.AllTests` in `[After(Assembly)]` | patch |

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

## 4. Log

- **2026-10-08.** Written at 4.12.0 from the probe kronikol-1c ran with a background agent at 4.11.0 (worktree
  `Kronikol-adapters-probe`, read-only).
