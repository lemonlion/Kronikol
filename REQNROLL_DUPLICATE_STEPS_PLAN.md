# Plan: Fix Duplicated Steps in ReqNRoll Reports (Incomplete Idempotency Guard)

## Status: IMPLEMENTED in full, shipped in 3.0.64 (issue #71)

Deviations from the plan as written, all forced by the repo moving on since it was drafted:
the template package pins were already bumped from `2.29.17-beta` to 3.0.63 by the 3.0.64 CI
overhaul, so Part 2's pin-bump co-requisite was already satisfied; the TUnit example's
`reqnroll.json` listed only the framework assembly and had `Kronikol.ReqNRoll.Core` **added**
to honour Part 3's keep-both intent; no example feature had a Background, so a new
`Cake Quality` feature (Background + two scenarios diverging only at `Then`) was added to the
xUnit3 example to genuinely exercise `BackgroundStepsDetector` extraction; the secondary unit
test was skipped per the plan's own gate (Reqnroll 3.3.4's `ScenarioContext` has only an
internal constructor). Bonus find while wiring the regression tests into CI: all five
"Integration (…)" CI jobs' `FullyQualifiedName~` filters matched zero tests under xunit.v3
(theory args live in `DisplayName` only) — fixed in the same release.

## Problem Statement

When a consumer's `reqnroll.json` lists **both** the framework binding assembly and
`Kronikol.ReqNRoll.Core` in `bindingAssemblies` (which is exactly what Kronikol's own
templates and example projects currently ship):

```json
{
  "bindingAssemblies": [
    { "assembly": "Kronikol.ReqNRoll.xUnit3" },
    { "assembly": "Kronikol.ReqNRoll.Core" }
  ]
}
```

every step appears **twice** in the generated Kronikol report, with identical durations,
the duplicate rendered with an `And` keyword:

```
Given a pancake batch has been created (15ms)
And a pancake batch has been created (15ms)
And an order has been created for the batch (11ms)
And an order has been created for the batch (11ms)
When the audit logs are retrieved (14ms)
And the audit logs are retrieved (14ms)
...
```

Observed in the wild in the BreakfastProvider repo
(`tests/BreakfastProvider.Tests.Component.ReqNRoll`, Kronikol 3.0.60), whose
`reqnroll.json` was copied from our template.

## Root Cause

ReqNRoll scans every assembly in `bindingAssemblies` for `[Binding]` classes. With both
assemblies listed it discovers **two** hook classes:

- `Kronikol.ReqNRoll.ReqNRollTrackingHooks` (base, in `Kronikol.ReqNRoll.Core`)
- `Kronikol.ReqNRoll.ReqNRollTrackingHooksXUnit3` (subclass, in `Kronikol.ReqNRoll.xUnit3`;
  same pattern in `.xUnit2` and `.TUnit` — see `src/Kronikol.ReqNRoll.*/BindingHooks.cs`)

ReqNRoll instantiates **one instance of each class per scenario** and runs every hook
method on both instances → all hooks execute twice per scenario/step.

Issue #59 (v3.0.18, commit `82f294f`) anticipated this. The changelog says
"Added idempotency guards to ReqNRoll hooks to prevent double-execution if both Core and
framework assemblies are scanned" — **but the guard was only added to `BeforeScenario`**
(`src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs`, ~line 36):

```csharp
[BeforeScenario(Order = int.MinValue)]
public void BeforeScenario()
{
    // Idempotency guard: if hooks have already run for this scenario (e.g. both base
    // and derived [Binding] classes discovered), skip duplicate execution.
    if (_scenarioContext.ContainsKey(ReqNRollConstants.ScenarioRuntimeIdKey))
        return;
    ...
}
```

`BeforeStep`, `AfterStep`, and `AfterScenario` have **no guard**. Consequences per
double-discovered scenario:

1. **`AfterStep` runs twice** → `steps.Add(...)` runs twice on the *shared*
   `List<ReqNRollStepInfo>` stored in `ScenarioContext` under
   `ReqNRollConstants.StepsCollectionKey` → every step recorded twice.
   Durations are identical because both `AfterStep` executions read the same stopwatch
   under `ReqNRollConstants.StepStopwatchKey` (the second `BeforeStep` overwrote the
   first instance's stopwatch; both `AfterStep`s read the surviving one).
2. **`BeforeStep` runs twice** → `StepCollector.StartStep(...)` called twice, so
   assertion-tracking sub-steps and attachments can attach to a phantom step entry.
   `AfterStep` likewise calls `StepCollector.CompleteStep(...)` twice.
3. **`AfterScenario` runs twice** → `ReqNRollScenarioCollector.Collect(...)` enqueues the
   scenario twice. This is currently *masked* in the report by
   `DistinctBy(x => x.ScenarioId)` in
   `src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs` (~line 30) —
   which is why users see doubled steps but not doubled scenarios. The non-owner
   instance also has a null `_stopwatch` (its `BeforeScenario` returned early), so the
   second enqueued copy has `Duration = null`.
4. The duplicate step renders as `And` because the HTML renderer collapses a repeated
   keyword into `And`.

`ReqNRollTestRunHooks` (same file folder) is already effectively guarded:
`BeforeTestRun` checks `StartRunTime != default`, and `AfterTestRun` is a harmless
last-write-wins.

Note: the wiki integration guides (`../Kronikol.wiki/Integration-ReqNRoll-*.md`) already
show `bindingAssemblies` with **only** the framework assembly — the templates and
examples in this repo are out of date and still list both. The hook fix is required
regardless, because existing consumers (e.g. BreakfastProvider) already have both listed.

## Fix Design

### Part 1 — Owner-instance guard in `ReqNRollTrackingHooks` (the real fix)

Track which hook *instance* owns the scenario, and make every per-scenario/per-step hook
a no-op on non-owner instances.

1. Add a constant to `src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs`:

   ```csharp
   public const string OwnerHooksKey = "Kronikol.OwnerHooks";
   ```

2. In `BeforeScenario`, when the existing guard passes (i.e. this instance is doing the
   initialization), also record ownership:

   ```csharp
   _scenarioContext[ReqNRollConstants.OwnerHooksKey] = this;
   ```

   Keep the existing `ScenarioRuntimeIdKey` guard as-is (it is what makes the first
   instance the owner).

3. Add a private helper and guard the three unguarded hooks:

   ```csharp
   private bool IsOwner =>
       _scenarioContext.TryGetValue(ReqNRollConstants.OwnerHooksKey, out object? owner)
       && ReferenceEquals(owner, this);
   ```

   At the top of `BeforeStep()`, `AfterStep()`, and `AfterScenario()`:

   ```csharp
   if (!IsOwner)
       return;
   ```

Why owner-instance rather than per-step "already ran" flags: `BeforeStep`/`AfterStep`
carry no step identity we can key on cheaply (the same step text can legitimately appear
twice in one scenario), and hook execution order between the two binding classes is
unspecified (both registrations use the same `Order` value). `ReferenceEquals` against
the instance stored in the per-scenario `ScenarioContext` is order-independent, allocation
free, and correct even when a step is repeated.

Correctness notes:

- Normal single-assembly configs: exactly one instance exists, it becomes owner,
  behavior unchanged.
- `ScenarioContext` is per-scenario, so ownership cannot leak across scenarios or
  parallel test threads.
- The non-owner's `AfterScenario` no longer calls `Collect`, so the `Duration = null`
  duplicate enqueue also disappears. Leave the `DistinctBy(ScenarioId)` in
  `ScenarioInfoEnumerableExtensions` as defense-in-depth.
- No changes needed in `Kronikol.ReqNRoll.xUnit2/xUnit3/TUnit` — their `BindingHooks.cs`
  subclasses inherit the fixed base. `ReqNRollTestRunHooks` needs no change.

### Part 2 — Stop shipping the double-scan config in templates

Update the three project templates to list **only** the framework assembly (matching the
wiki), since #59 made `Kronikol.ReqNRoll.Core` redundant there:

- `templates/kronikol-reqnroll-xunit3/reqnroll.json`
- `templates/kronikol-reqnroll-xunit2/reqnroll.json`
- `templates/kronikol-reqnroll-tunit/reqnroll.json`

Remove the `{ "assembly": "Kronikol.ReqNRoll.Core" }` entry from each.

**⚠️ Version-pin dependency**: the template `.csproj` files currently pin
`Kronikol.ReqNRoll.{xUnit2,xUnit3,TUnit}` at **`2.29.17-beta`** (e.g.
`templates/kronikol-reqnroll-xunit3/Kronikol.ReqNRoll.xUnit3.csproj` line ~24) — a
version that **predates the #59 framework-assembly `[Binding]` subclasses**. Removing
Core from `bindingAssemblies` while keeping that pin would leave template-generated
projects with **no hooks discovered at all** (empty reports). The Core removal and a
package-pin bump to the version released by this fix must land **together** in all
three templates. While there, sanity-check the other template families
(`kronikol-lightbdd-*`, `kronikol-xunit*`, etc.) for the same stale `2.29.17-beta` pin
and bump them too. The legacy `ttd-reqnroll-*` templates have no `reqnroll.json` and
reference the frozen pre-rebrand packages — out of scope, leave untouched.

### Part 3 — Keep the double-scan config in the examples, on purpose

**Deliberately keep both assemblies listed** in:

- `examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit3/reqnroll.json`
- `examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit2/reqnroll.json`
- `examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.TUnit/reqnroll.json`

These example projects are run by CI ("Example.Api ReqNRoll Tests" and
"Integration (ReqNRoll)" matrix entries in `.github/workflows/ci.yml`), so keeping the
both-listed config permanently exercises the double-discovery path that existing
consumers have. Document this intent in the regression test (JSON has no comments).

**Known coverage gap (accept, but state it)**: no TUnit project appears anywhere in
`ci.yml` — the `Example.Api.Tests.Component.ReqNRoll.TUnit` example is never run in CI,
so the fix is CI-verified only via the xUnit2/xUnit3 paths. That is acceptable because
the fix lives entirely in the shared base class `ReqNRollTrackingHooks`; run the TUnit
example once locally as part of step 4 of the Implementation Order for completeness.

## Test Plan (TDD — red first, per CLAUDE.md)

### Primary regression test (integration — goes RED before the fix)

The `Example.Api.Tests.Integration` project already runs the component test projects via
`Helpers/TestProjectRunner.RunAsync(projectName, ...)` and parses the generated reports
via `Helpers/ReportParser` (see existing patterns in `Tests/ReportGenerationTests.cs`).
Because the example ReqNRoll projects list both binding assemblies, they currently
reproduce the bug — so this test fails on main before the fix and passes after.

Add `Tests/ReqNRollDuplicateStepsTests.cs` (new file, xUnit, follow the style of
`ReportGenerationTests.cs`):

1. Run `TestProjects.ReqNRollXUnit3` (and `TestProjects.ReqNRollXUnit2`) via
   `TestProjectRunner`.
2. Parse the generated report from `result.ReportsFolderPath`:
   - Easiest robust source: the specifications **YAML** data report
     (`ReportParser.GetReportFiles(...).SpecificationsYaml` →
     `ReportParser.ReadYamlAsync`), which contains each scenario's step list. If the
     existing `ReportParser` lacks a step extractor, add a small helper
     (`ExtractScenarioStepsAsync` or YAML parse) rather than regexing HTML.
3. Assert, for every scenario in the report:
   - **No adjacent duplicate steps**: no two consecutive steps with identical text.
     Caveat: this generic rule would false-positive on a feature that legitimately
     repeats the same step twice in a row — verify no example feature does that before
     relying on it, and treat the exact-match assertions below as the authoritative
     check.
   - For known scenarios in the example suite, assert the **exact ordered step texts**
     match the `.feature` file (pick stable scenarios from
     `examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit3/Features/`
     and hard-code the expected texts). Cover at least: one plain scenario, one
     scenario **with a Background** (doubled steps would interact with
     `BackgroundStepsDetector.DetectAndExtract`), and one **Scenario Outline** example
     (doubled steps feed `ExampleValueGrouper.BuildStructured`, so outline grouping is
     a second place the duplication can distort output).
4. Add an XML-doc/comment on the test class stating that the example projects
   intentionally list both `Kronikol.ReqNRoll.<framework>` and `Kronikol.ReqNRoll.Core`
   in `bindingAssemblies` to exercise double binding discovery, and must stay that way.

Filter note: CI's "Integration (ReqNRoll)" matrix entry filters on
`Component.ReqNRoll.xUnit2|Component.ReqNRoll.xUnit3` — name the test methods so they
match that filter (follow the naming used by existing integration tests for ReqNRoll
projects, e.g. include `Component_ReqNRoll_xUnit3` in the test name as the existing
tests do — verify against how the filter actually matches before relying on it).

### Secondary unit test (only if cheap)

`tests/Kronikol.Tests/ReqNRollBindingDiscoveryTests.cs` already covers discovery shape.
Attempt a direct unit test of the owner guard **only if** Reqnroll's `ScenarioContext`
can be constructed in-process (check whether its constructor is public in the referenced
Reqnroll version; no existing test in `tests/Kronikol.Tests/ReqNRoll/` constructs one).
If constructible: create two `ReqNRollTrackingHooks` instances sharing one
`ScenarioContext`, call `BeforeScenario` on both, and assert the second instance's
`IsOwner`-guarded paths are inert (e.g. expose the guard via an
`internal` method + `InternalsVisibleTo` — `Kronikol.Tests` visibility already exists
for other internals, verify in the csproj/AssemblyInfo). If `ScenarioContext` is not
constructible without the Reqnroll runtime, **skip this** — the integration test is the
authoritative red/green signal; do not build elaborate fakes for it.

### Manual verification (optional but recommended)

BreakfastProvider (`c:\Code\BreakfastProvider`, branch with
`tests/BreakfastProvider.Tests.Component.ReqNRoll`) reproduces the bug with released
3.0.60. Pack locally and point BreakfastProvider at the local feed
(`c:\Code\local-nuget` is the existing local NuGet folder), run the ReqNRoll component
suite, and confirm the audit-log scenario report shows 6 steps, not 12.

## Implementation Order

1. **RED**: Write the integration regression test (Part "Test Plan" step 1). Run it
   locally against the current code; confirm it fails with doubled steps.
   ```bash
   dotnet test examples/Example.Api/tests/Example.Api.Tests.Integration \
     --filter "FullyQualifiedName~ReqNRollDuplicateSteps"
   ```
   (Restore `examples/Example.Api/Example.Api.sln` first, as CI does.)
2. **GREEN**: Apply the owner-instance guard (Fix Design Part 1:
   `ReqNRollConstants.cs` + `ReqNRollTrackingHooks.cs`). Re-run the test; confirm pass.
3. Update the three templates (Part 2). Grep to confirm no template/doc still tells
   users to add Core:
   ```bash
   grep -rn "Kronikol.ReqNRoll.Core" templates/ ../Kronikol.wiki/
   ```
4. Run the full local suite:
   ```bash
   dotnet test tests/Kronikol.Tests
   dotnet test examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit2
   dotnet test examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit3
   dotnet test examples/Example.Api/tests/Example.Api.Tests.Integration \
     --filter "FullyQualifiedName~Component.ReqNRoll"
   ```
5. Per CLAUDE.md Bug Fixing rules: while in the area, double-check the sibling adapters
   for the same class of bug (LightBDD/BDDfy/NUnit adapters don't use ReqNRoll binding
   discovery, so they should be unaffected — verify, don't assume).

## Documentation & Release (per CLAUDE.md)

0. **GitHub issue** — repo convention is to reference an issue number in changelog
   entries (see #59, #61–#64). File an issue describing the duplicated-steps bug (or
   link the existing one if a consumer already filed it) and reference it from the
   changelog entry and commit message.
1. **CHANGELOG.md** — new `### Fixed` entry, e.g.:
   > **ReqNRoll steps no longer duplicated when both `Kronikol.ReqNRoll.Core` and the
   > framework assembly are listed in `bindingAssemblies`** — The v3.0.18 idempotency
   > guard only covered `BeforeScenario`; `BeforeStep`/`AfterStep`/`AfterScenario` still
   > executed twice, recording every step twice (rendered as a duplicate `And` line with
   > identical duration). Hooks now no-op on non-owner binding instances. Templates
   > updated to list only the framework assembly.
2. **Wiki** (`../Kronikol.wiki/Integration-ReqNRoll-*.md`) — the setup sections already
   show the correct single-assembly config; add a **Troubleshooting** entry to each:
   "Steps appear twice in the report (duplicate `And` lines with identical durations)" →
   cause: both assemblies in `bindingAssemblies` on Kronikol < fix version; remedy:
   upgrade, and/or remove `Kronikol.ReqNRoll.Core` from `bindingAssemblies`.
3. **Version bump** — same version across **all** packages. `Directory.Build.props`
   currently has `<Version>3.0.61</Version>` with the changelog topping out at 3.0.60:
   if 3.0.61 is still unreleased, ship as 3.0.61; otherwise bump the patch. Check
   released tags (`git tag | sort -V | tail`) before deciding.
4. Commit, tag `v{version}`, push commit + tag to origin (release workflow is
   `.github/workflows/release.yml`).

## Downstream Follow-up (outside this repo)

- **BreakfastProvider**: after the release, bump `Kronikol.ReqNRoll.xUnit3` /
  `Kronikol.AssertionTracking` package versions in
  `tests/BreakfastProvider.Tests.Component.ReqNRoll/*.csproj`. Optionally also remove
  `Kronikol.ReqNRoll.Core` from its `reqnroll.json` `bindingAssemblies` (either change
  alone fixes the symptom; the upgrade is the durable one).

## Key Files Reference

| File | Role |
|---|---|
| `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs` | The bug: guard only on `BeforeScenario` |
| `src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs` | Add `OwnerHooksKey` |
| `src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs` | `DistinctBy(ScenarioId)` masking (leave as-is) |
| `src/Kronikol.ReqNRoll.{xUnit2,xUnit3,TUnit}/BindingHooks.cs` | Derived `[Binding]` subclasses (no change) |
| `templates/kronikol-reqnroll-{xunit2,xunit3,tunit}/reqnroll.json` | Remove Core entry |
| `templates/kronikol-reqnroll-*/Kronikol.ReqNRoll.*.csproj` | Bump stale `2.29.17-beta` package pins (must land with Core removal) |
| `examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.*/reqnroll.json` | Keep both entries (regression harness) |
| `examples/Example.Api/tests/Example.Api.Tests.Integration/` | Home of the new regression test (`TestProjectRunner`, `ReportParser`) |
| `tests/Kronikol.Tests/ReqNRollBindingDiscoveryTests.cs` | Existing discovery-shape tests |
| `.github/workflows/ci.yml` | Matrix entries "Example.Api ReqNRoll Tests" / "Integration (ReqNRoll)" |
| CHANGELOG 3.0.18 entry + commit `82f294f` | History of the incomplete guard (#59) |
