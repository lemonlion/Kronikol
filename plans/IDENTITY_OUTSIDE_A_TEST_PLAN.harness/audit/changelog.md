## [{VERSION}] - 2026-10-12

**Patch - outside a test, `TrackingDiagramOverride.StartSetup()` and `StartAction()` set the phase again on xUnit v2 and
NUnit, as they did before 4.13.1, and now on MSTest and TUnit too.** Found by an audit of #133's release
(`plans/IDENTITY_OUTSIDE_A_TEST_PLAN.md`, section 7). A bug fix with nothing new for a consumer to call, so the patch part
moves ({PREVIOUS} to {VERSION}). The history action's `VERSION` installs `Kronikol.Tool` {VERSION}, and the templates pin
{PREVIOUS}.

### Fixed

- **From 4.13.1 to {PREVIOUS}, `StartSetup()` and `StartAction()` set no phase outside a test.** The phase
  (`TestPhaseContext.Current`) belongs to the flow, not to a test, and every tracker stamps it on the calls it logs. xUnit
  v2 runs a test on the flow its constructor and `IAsyncLifetime.InitializeAsync` ran on, so a test class or a base class
  that called `TrackingDiagramOverride.StartSetup()` or `StartAction()` there set the phase that every call of the test
  carried. 4.13.1 (#133) made the overrides do nothing when no test is running, which dropped the phase along with the
  marker it meant to drop, so such a test's calls carried no phase (`Unknown`). Measured: the new facts pass on 4.12.0,
  before #133, and fail on 4.14.6. Outside a test both now set the phase on their flow, and only `StartAction()`'s
  diagram marker still needs a test: it goes to the test a `TestIdentityScope` or the global fallback names, and is
  dropped when neither names one. The same holds on NUnit (a set-up fixture, a fixture's constructor, its one-time set-up
  and tear-down, a thread the test did not reach), and on MSTest and TUnit, whose overrides threw
  `NullReferenceException` there before 4.13.1 and did nothing from 4.13.1. xUnit v3's override is unchanged: it has set
  no phase outside a test since 2.31.6, and xUnit v3 runs a test class's constructor and `InitializeAsync` inside the
  test.
- The four overrides' doc comments said they do nothing outside a test when no scope or fallback names one; they now say
  that the marker is dropped and the phase is set either way.

### Tests

- Kronikol.Tests.xUnit2: a test class whose constructor calls `StartAction()` runs in the Action phase, a call it makes
  carries that phase, and the constructor's `StartAction()` logs no marker; a class whose `InitializeAsync` calls
  `StartSetup()` after its constructor's `StartAction()` runs in the Setup phase. Kronikol.Tests.NUnit4,
  Kronikol.Tests.MSTest and Kronikol.Tests.TUnit: `StartAction()` and `StartSetup()` set the phase on a thread the test
  did not reach (on TUnit, with no TUnit test). With the facts copied onto v4.14.6 the six phase facts fail
  (`Unknown`) and the marker fact passes; on v4.12.0 the three xUnit v2 phase facts pass and the marker fact fails, on the
  #133 defect itself.
