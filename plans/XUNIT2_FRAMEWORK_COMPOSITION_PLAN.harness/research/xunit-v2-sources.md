# xUnit v2 internals, the VS adapters and the frameworks that take the slot

Research for `plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md` (#132), 2026-10-07. Every claim cites a file and line from the
source in the table, with a short quote. **Probe** marks a claim also checked by running code (probe projects in the session
scratchpad `xunit-src/probe/` and `probe-af/`, net8.0, VS adapters 2.8.2 and 3.1.5). **Inference** marks reasoning from code
that was not run.

## Sources

| Key | Source | Ref |
|---|---|---|
| X | github.com/xunit/xunit | tag `v2-2.9.3` (9712244) |
| VS2 | github.com/xunit/visualstudio.xunit | tag `2.8.2` (699d445) |
| VS3 | github.com/xunit/visualstudio.xunit | tag `3.1.5` (1b188a7) |
| X3 | github.com/xunit/xunit, the v3 runner utility VS3 pins (`Versions.props` `xunit_v3_Version 3.1.0`) | tag `v3-3.1.0` (03a0716) |
| ABS | github.com/xunit/abstractions.xunit | tag `2.0.3` |
| AF | github.com/JDCain/Xunit.Extensions.AssemblyFixture (nuspec repository commit) | tag `2.6.0` (4ae170c) |
| DI | github.com/pengweiqhca/Xunit.DependencyInjection (9.9.2 nuspec commit) | branch `v2` = 48c49bb |
| MEZ | github.com/meziantou/Meziantou.Xunit.ParallelTestFramework (2.3.0 nuspec commit) | cd06c29 |
| RQ | github.com/reqnroll/Reqnroll | tag `v3.3.4` (33eeea2) |
| PKG | nupkgs from nuget.org; ilspycmd 10.0.1 where noted | |
| DOCS | github.com/xunit/xunit.net | main 09a4211 |
| RT / RS | dotnet/runtime `v8.0.0`; microsoft/referencesource `main` | |

VS2 ships xunit.runner.utility 2.9.0; `git diff v2-2.9.0 v2-2.9.3 -- src/xunit.runner.utility` is empty, so X runner-utility
citations apply to VS2.

## A1. Message order (X `src/xunit.execution/Sdk/Frameworks/Runners/TestRunner.cs`)

- L130 `QueueMessage(new TestStarting(Test))` always first. L136-142 the skip branch comes after it: a skipped test gets
  Starting, Skipped, Finished and no class instance (probe).
- L145-149 `if (!aggregator.HasExceptions) { var tuple = await aggregator.RunAsync(() => InvokeTestAsync(aggregator));` ->
  `XunitTestRunner.cs` L52-80, L87-88 `new XunitTestInvoker(...).RunAsync()` -> `TestInvoker.cs` L256 `CallTestMethod`.
- L157-166 build TestPassed/TestFailed; L168-170 queued only `if (!CancellationTokenSource.IsCancellationRequested)`: after
  a cancel a test can get Starting and Finished with no result.
- L173-178 TestCleanupFailure never fires in stock 2.9.3 (a Dispose error becomes ITestFailed). L180 TestFinished.
- Output: `TestOutputHelper.cs` L66 queues TestOutput inline; the text is also on the result (`XunitTestRunner.cs` L72-75).
- `ExecutionErrorTestCaseRunner.cs` L31-47 and `XunitTheoryTestCaseRunner.cs` L207-219 send Starting, TestFailed(time 0), Finished.

## A2. Lifecycle (X `TestInvoker.cs` L161-196, `XunitTestInvoker.cs`)

- L167 `CreateTestClass()` (outside the try), L173 `InitializeAsync`, L177 `BeforeTestMethodInvokedAsync`, L179-180 the method
  (only with no errors), L182 `After`, L186 `DisposeAsync`, L190 (finally) `Dispose`. The result is queued after all of it.
- Constructor throws: nothing else runs. InitializeAsync throws: Before, method, After, DisposeAsync skipped; Dispose runs.
- Before throws (`XunitTestInvoker.cs` L60-68 `Aggregator.Add(ex); break;`): only earlier attributes get After, in reverse
  (L85-97) (probe).
- Attribute sources in order: collection, class, method, assembly (`XunitTestCaseRunner.cs` L131-135).

## A3. Message bus

- Choice: `TestAssemblyRunner.cs` L138-144 (SynchronousMessageBus when `SynchronousMessageReportingOrDefault()`, else
  MessageBus), once per run (L197). `XunitTestAssemblyRunner` does not override it.
- Option name `"xunit.execution.SynchronousMessageReporting"` (`common/TestOptionsNames.cs` L22), default false
  (`TestFrameworkOptionsReadExtensions.cs` L218-230).
- Async `MessageBus.cs`: L71-73 enqueue and return `continueRunning`; L28 one reporter worker; L31-49 FIFO dispatch;
  L39-47 sink exceptions become an ErrorMessage; L52-60 Dispose joins the worker. Probe: every sink call on one thread, and
  a fact body ran BEFORE the sink saw its ITestStarting.
- `SynchronousMessageBus.cs` L32-38 `return messageSink.OnMessage(message) && continueRunning;`, no try/catch.
- No wrapper between the bus and the executor's sink in core (the bus passes unchanged through `XunitTestAssemblyRunner.cs`
  L346, `XunitTestCollectionRunner.cs` L185, `XunitTestClassRunner.cs` L206, `XunitTestMethodRunner.cs` L45,
  `XunitTestCaseRunner.cs` L140, `XunitTestRunner.cs` L88). `DelegatingMessageBus.cs` L11-38 is synchronous and unused by
  core; SkippableFact wraps the bus per test case. AF, DI, MEZ, RQ and Autofac do not override `CreateMessageBus`.
- AsyncLocal: `TestRunner.RunAsync` L130 -> sync `QueueMessage` -> sync `OnMessage`, no async-method boundary, so a value set
  in the sink stays in the execution context, flows through the L149 await into the whole invocation, is present at
  L169/L180, and is undone only when `TestRunner.RunAsync` returns (RT `AsyncMethodBuilderCore.cs` L30-53; RS
  `AsyncMethodBuilder.cs` L299-320): no leak to the next test or theory row (`XunitTheoryTestCaseRunner.cs` L193-194).
  Probe (sync): the constructor, Before, the body, after an await, inside Task.Run, After, Dispose and the sink at the
  result all saw the value. Probe (async): empty.

## A4. Who turns synchronous reporting on

- VS2 L537-542 and VS3 L605-610: only when `parallelizeTestCollections` is false in config (then also
  `DisableParallelization`). Probe: confirmed.
- VS3 also honours an undocumented `"synchronousReporting": true` in xunit.runner.json for v2 assemblies (X3
  `ConfigReader_Json.cs` L140-141/L274; X3 `TestOptionsNames.cs` L300-307 "Consumed by: v2, v3"). Probe: works under 3.1.5,
  ignored under 2.8.2.
- xunit.runner.console 2.9.3 never sets it (`ConsoleRunner.cs` L395-399). The programmatic `AssemblyRunner` always does
  (L170/L189), without disabling parallelism.
- v2 xunit.runner.json has no such key (`ConfigReader_Json.cs` L260-277); no published schema lists one (DOCS).

## A5. Completion and host exit

- `XunitTestFrameworkExecutor.cs` L93-97 `protected override async void RunTestCases`; `RunTests` returns at the first real
  await (probe: before any result with parallel collections). `ITestAssemblyFinished` is queued in a finally
  (`TestAssemblyRunner.cs` L220-222), never if `QueueMessage(TestAssemblyStarting)` returned false (L203).
- VS2 L555-556 and VS3 L629-630: `RunTests`, then `resultsSink.Finished.WaitOne()` with no timeout. `Finished` is set only
  after ITestAssemblyFinished has passed through the adapter's sink (`ExecutionSink.cs` L230, L449-455; X3 L346, L855-859).
  So writing reports inside `OnMessage(ITestAssemblyFinished)` before forwarding it is safe. Probe: 3 s of writing, forward,
  the adapter disposed the executor, the process exited; both buses, both adapters.
- Failure modes (probe): a decorator that never forwards ITestAssemblyFinished hangs the run (testhost alive at 25 s); a
  throw from OnMessage under the async bus gave "Catastrophic failure" and a hang; under the sync bus, "Test host process
  crashed".
- The executor is disposed twice (`TestFramework.cs` L42-52, L77-82; `Xunit2.cs` L84-89) and is created in discovery-only
  sessions such as `--list-tests` (`Xunit2.cs` L49; probe: CreateExecutor, two Dispose, no RunTests).

## A6. RunAll vs RunTests

- Both adapters only call `RunTests`, with or without `--filter` (VS2 L555 after discovery L438-454 and filter L464-467 or
  deserialization L481-534; VS3 via X3 `Xunit2.cs` L584-588). Console L451 and MSBuild L337 also call RunTests.
- One executor gets at most one call: a new `XunitFrontController` per assembly (VS2 L435, VS3 L517). One adapter call can
  loop over several assemblies (VS2 L386-399). Testhost reuse across Test Explorer runs: unknown. Probe: `dotnet test A.dll
  B.dll` used separate processes.

## A7. Options

- `SetValue<TValue>` is public (ABS `ITestFrameworkOptions.cs` L24). VS2 in-process passes the runner's own object (a
  mutation is visible to the runner); with AppDomains a serialized copy (`TestFrameworkOptions.cs` L13-15). VS3 passes a
  write-through wrapper (`Xunit2Options.cs` L21-28). Probe: `SetValue(true)` switched the bus under both adapters. Passing
  a wrapping options object of one's own avoids touching the runner's.

## A8. Theories

- Pre-enumerated into one test case per row when pre-enumeration is on (default), the theory is not skipped, the data
  source supports discovery enumeration, every row is serializable and nothing throws (`TheoryDiscoverer.cs` L128-307).
- Otherwise one `XunitTheoryTestCase`: each row still gets its own `ITest` and its own Starting/result/Finished, but all
  rows share one `ITestCase` and `UniqueID` (`XunitTheoryTestCaseRunner.cs` L164-167; probe). Key by the ITest reference.

## A9. Fixture and constructor failures

- Every affected test gets ITestStarting, ITestFailed (time 0, the exception) and ITestFinished, with no Before/After
  (`TestRunner.cs` L145-147). Class fixture constructor: `TestClassException` (`XunitTestClassRunner.cs` L100-107); fixture
  InitializeAsync and collection fixture: the raw exception (L111, L120, L126); unresolvable constructor argument:
  `TestClassException` "did not have matching fixture data" (`TestClassRunner.cs` L126-127). Probe: all confirmed.

## A10. ITest and names

- One `ITest` instance on every message of a test (`TestMessage.cs` L17-24; probe `sameRef=True`). `DisplayName` honours
  `[Fact(DisplayName = ...)]` and includes theory arguments (`XunitTestCase.cs` L124-127, L96-97). `UniqueID` excludes the
  display name (`TestMethodTestCase.cs` L180-190, L215-250). `ITestFailed` carries strings, not the Exception
  (`TestFailed.cs` L36-42).

## A11. ExecutionTime

- `TestInvoker.cs` L194 `Timer.Total`: constructor, Before, method, After, Dispose; NOT InitializeAsync or DisposeAsync.
  Probe 0.93 s for 0.9 s of work. 0 when the constructor or InitializeAsync throws, and for skipped tests.

## B. Xunit.Extensions.AssemblyFixture 2.6.0 (latest; JDCain/Xunit.Extensions.AssemblyFixture, authors kzu and J.D. Cain)

- `public class AssemblyFixtureFramework : XunitTestFramework` (AF `src/TestFramework.cs` L7), not sealed; `CreateExecutor`
  (L16-19) returns an internal executor (`TestFrameworkExecutor.cs` L8) whose `RunTestCases` is `async void` (L14).
- Fixtures are created after ITestAssemblyStarting (`TestAssemblyRunner.cs` L33-73) and disposed before
  ITestAssemblyFinished (L75-118). A fixture constructor taking `IMessageSink` gets the EXECUTION sink (L52-60): under the
  decorator, the decorator's own sink (probe).
- Probe: a subclass overriding `CreateExecutor` to return the decorator worked.

## C. Frameworks that take the slot (`TestFrameworkAttribute.cs` L10 `AllowMultiple = false`; `TestFrameworkProxy.cs` L81)

| Package | Downloads | Sealed? | Registered by | Where a decorator goes |
|---|---|---|---|---|
| Xunit.DependencyInjection 9.9.2 (last on xunit 2.x) | 22.3M (about 16.9M on v2-era versions) | Yes | MSBuild-generated attribute, off via `EnableXunitDependencyInjectionDefaultTestFrameworkAttribute=false` (targets L9-15) | Own framework wrapping `new DependencyInjectionTestFrameworkExecutor(...)` (public), or a composing `ITestFramework`. Its `RunTestCases` blocks (`.GetAwaiter().GetResult()`, executor L11-14). Not run |
| Meziantou.Xunit.ParallelTestFramework 2.3.0 | 4.2M | No | MSBuild-generated attribute, off via `IncludeMeziantouXunitParallelTestFramework=false` | Subclass, override `CreateExecutor` |
| Xunit.Frameworks.Autofac 0.5.4 | 0.13M | Abstract base | User writes the attribute for a subclass | Override `CreateExecutor` |
| Reqnroll.xUnit 3.3.4 | 7.2M | No | Emits the attribute from `build/xUnit.AssemblyHooks.cs` | `GenerateReqnrollAssemblyHooksFile=false` and a hand-written hooks file; `[AfterTestRun]` runs before ITestAssemblyFinished |
| Xunit.Extensions.Ordering 1.4.5 | 7.31M | No | User attribute | Subclass |
| SpecFlow.xUnit 3.9.74 | 25.1M | n/a | Emits the attribute | as Reqnroll |
| xunit.assemblyfixture 2.2.0 | 1.92M | n/a | User attribute | n/a |

## D. Observing results without the slot

- `IRunnerReporter`: runner-side, one active; VS2 loads `*reporters*.dll` from the test folder, VS3 built-ins only.
- A custom test-case type wrapping the bus (the SkippableFact pattern) sees its own tests only, never ITestAssemblyFinished.
- Wrapping from an assembly-level TestCaseOrderer: untested. `ITestOutputHelper`, `BeforeAfterTestAttribute`, fixture
  `IMessageSink`s and `TestEventSource` never see outcomes.

## E. What a decorator must do

1. Write reports, then forward ITestAssemblyFinished in a finally; never throw from OnMessage.
2. On .NET Framework with AppDomains derive from `LongLivedMarshalByRefObject` (Inference). Kronikol.xUnit2 targets
   net8.0/net9.0/net10.0 only, so this does not apply.
3. Dispose idempotent; never write from Dispose; discovery-only sessions create the executor too.
4. Thread-safe state: under the sync bus the sink is called concurrently from test threads. VS2 enables sync only together
   with DisableParallelization; `VsExecutionSink` 2.8.2 has an unsynchronized `ExecutionSummary.Errors++` (L92, L180-230)
   while `ExecutionSink` locks (L178-463), and `AssemblyRunner.cs` L189 forces sync without disabling parallelism. Probe:
   15/15 results right with parallel collections and forced sync.
5. ITestFinished is terminal (a cancelled test may have no result).
6. Key by the ITest reference.
7. RunTests returning is not completion: async void in xunit, AF, MEZ, RQ and Autofac; blocking in DI.
