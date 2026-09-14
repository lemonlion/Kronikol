# BACKGROUND_ATTRIBUTION_PLAN.md — dependency calls a scenario did not cause

**Status:** written 2026-09-14 and revised the same evening after tracing the mechanism against the
source and two consumer reports; **NOT green-lit**, nothing implemented. The cross-run history dogfood
on BreakfastProvider surfaced it (`CROSS_RUN_HISTORY_PLAN.md` §12.1, 3.14.0 and 3.15.0 entries). Two
defects found while tracing are logged in §7 and are fixed as bugs regardless of this plan.

## 1. What the consumer showed

BreakfastProvider runs 34896168088 and 34897006191, lane `xunit-in-docker`, 203 scenarios each, no code
change between them. After 3.15.0 made call counts harmless, the scenarios still reading
`behaviour-changed` were the ones whose *set* of Cosmos DB calls on the `orders` container differed
between the runs. Read by script from the two `TestRunReport.json` files:

- Thirteen xUnit test classes build their own web host inside the test (`delayAppCreation: true`, then
  `CreateAppAndClient(configOverrides)` or `CreateAppAndClientWithSharedFactory`, in
  `tests/BreakfastProvider.Tests.Component.xUnit/Infrastructure/BaseFixture.cs`): the two health-check
  classes, header propagation, telemetry, goat-milk feature flag, menu availability, menu caching, menu
  downstream failure, orders cross-field validation, outbox retry exhaustion, orders pagination, orders
  rate limiting, toppings feature flag. The other five suites have their counterparts.
- **Every scenario in those classes holds four `Create /orders` and four `Query /orders`** beyond
  anything its own request does (`GET /toppings`, `GET /health`, `GET /menu`, `GET /goat-milk`), in
  both runs. Every class on the shared static host holds only calls its own requests explain.
- **Every run-to-run difference on that container sits in one of those classes.** Toppings: six
  `Replace` in the first run, none in the second. Pagination: four against two. Cross-field validation:
  fourteen `Create` and ten `Query` against twelve and eight. Telemetry: seven `Create` against eight.
- The captured content says what the calls are. Each `Query` is
  `SELECT VALUE root FROM root WHERE (root["status"] = "Pending")` — the outbox processor's poll
  (`src/BreakfastProvider.Api/Events/Outbox/OutboxProcessor.cs`, `ProcessPendingMessagesAsync`). Each
  `Create` that precedes it returns a `partitionedQueryExecutionInfoVersion` body: it is the Cosmos SDK
  fetching the **query plan** for that poll, not a document create (§7.2). The six `Replace` are the
  same processor claiming and then updating three pending `OrderCreatedEvent` messages that audit-log
  scenarios had left in the outbox at that moment (`TryClaimMessage`, then the status update); in the
  second run nothing was pending when the poll ran, so no `Replace`.
- The earlier note that the calls carried "trace ids no scenario owns" was not evidence and is
  withdrawn: every non-HTTP capture gets a fresh `traceId` per call
  (`src/Kronikol.Extensions.CosmosDB/CosmosTrackingMessageHandler.cs:53`), so a trace id never ties a
  dependency call to a request either way.
- The in-memory lanes show none of this: those hosts have no `CosmosClient` registered, and the
  in-memory Kafka and Pub/Sub fakes run the handler synchronously inside the producing request.

So the noise is not "a consumer draining earlier scenarios' messages" in general. It is one precise
thing: **a host started inside a test inherits that test's identity, and everything the host's
background services do is attributed to the test for as long as the host lives.** The flips are the
host's outbox processor doing other scenarios' outbox work whenever any is pending.

## 2. How attribution works today

Verified against the source on 2026-09-14.

**One record, one key.** `RequestResponseLog(TestName, TestId, …)`
(`src/Kronikol/Tracking/RequestResponseLog.cs:10-26`) carries the scenario as two strings decided at
capture time. Nothing records how they were decided. The report attributes by
`trackedLogs.ToLookup(l => l.TestId)` (`src/Kronikol/Reports/ReportGenerator.cs:3776`, `:4161`) and
`scenario["httpInteractions"] = logLookup[s.Id]` (`:4044`, `:4454`).

**One resolution chain, first hit wins.** `TestInfoResolver.Resolve`
(`src/Kronikol/Tracking/TestInfoResolver.cs:24-43`):

1. `test-tracking-current-test-name` / `-id` request headers off `IHttpContextAccessor.HttpContext`
   — the scenario's own request, stamped by `TestTrackingMessageHandler` on the way out;
2. the framework fetcher — for xUnit v3, `TestContext.Current.Test`
   (`src/Kronikol.xUnit3/CurrentTestInfo.cs:14-20`), an `AsyncLocal`, throwing when absent so the
   chain falls through;
3. `TestIdentityScope.Current` (`AsyncLocal`, `src/Kronikol/Tracking/TestIdentityScope.cs:56`);
4. `TestIdentityScope.GlobalFallback` (process-wide static, `:72-75`).

A `null` result means **the call is not logged at all** (`CosmosTrackingMessageHandler.cs:48-50` and
every other capturer). Copies of the chain that must stay in step: `MessageTracker.cs:345-394`,
`RequestResponseLogger.LogPair` overloads (`:117-133`, no `GlobalFallback`),
`TestTrackingMessageHandler.cs:195-220`, `Kronikol.xUnit3/TrackingDiagramOverride.cs:64`,
`Kronikol.Playwright/TestTrackingIdentity.cs:66`. Thirty-three files call `TestInfoResolver.Resolve`.

**Why a test-started host inherits the test.** Level 2 is an `AsyncLocal`. A `WebApplicationFactory`
built inside a test (its class constructor and `CreateAppAndClient` run under the test's execution
context) starts every `IHostedService` there: the outbox processor and six message consumers in
BreakfastProvider (`src/BreakfastProvider.Api/Program.cs:211`, `StartupExtensions.cs:176-180,328`). Their
loops carry the test's `TestContext` for the life of the host: a per-test host until the class
disposes it (`BaseFixture.cs:510-515`), a `SharedFactoryCache` host for the rest of the run, under the
identity of whichever test built it first. The static host is started from the assembly fixture
(`GlobalTestSetup.cs:43` → `BaseFixture.EnsureHostInitialized`, `:91`), where the fetcher throws, so
**its** background work is dropped. The docker-lane report is therefore wrong in both directions: the
main host's outbox and consumer work is invisible, and each test-started host's work is the test's.

**Level 1 against level 2.** In-process, a scenario's own request resolves at level 1 inside the SUT,
while test-side store calls (the outbox scenario seeding a message through the SUT's
`ICosmosRepository<OutboxMessage>`, `BaseFixture.cs:173`) and inherited background loops both resolve
at level 2. The level alone cannot separate a scenario's own arrange step from a hosted loop that
inherited its context.

**A second leak, latent here.** `TestIdentityScope.SetFromMessage` (`TestIdentityScope.cs:119-122`)
sets the ambient identity from a message header and never restores it. Five consumer wrappers call it
(`Kronikol.Extensions.Kafka/TrackingKafkaConsumer.cs:97`, `PubSub/TrackingSubscriberClient.cs:67`,
`EventHubs/TrackingEventHubConsumerClient.cs:139`, `ServiceBus/TrackingServiceBusReceiver.cs:220`,
`MassTransit/TrackingConsumeObserver.cs:19`), so once a consumer context has handled one correlated
message, every later uncorrelated call on it keeps that scenario. BreakfastProvider does not hit this:
its consumers are raw `ConsumerBuilder` loops (`Reporting/KafkaOrderServedConsumerService.cs:54`,
`KafkaRecipeCostConsumerService.cs:54`, `ReportingKafkaConsumerService.cs:52`) that never read the
`kronikol-test-*` headers its tracked producer stamps, and its Pub/Sub publisher stamps nothing
(`tests/…/Fakes/PubSub/TrackedPubSubEventPublisher.cs:19-27`). It also never registers
`AddTestTrackingContextPropagation()`, never sets `GlobalFallback`, and opens three explicit scopes with
synthetic ids that match no scenario (`PostCustomerFeedbackSteps.cs:29`, `PostRecipeCostSteps.cs:30`,
`PublishOrderServedEventSteps.cs:33`). Docker and external-SUT lanes run two tests at once
(`.github/workflows/_tests.yml:244`).

**Time is not available.** Only the HTTP handler and `RequestResponseLogger.LogPair` stamp
`Timestamp`; `RequestResponseLogger.Log` (`:30-44`) does not. In the first report 549 of 835 SUT
dependency requests have no timestamp — every Cosmos, Kafka, SQL Server, MongoDB, Spanner, ClickHouse
and BigQuery capture (§7.1).

**What exists already.** `TrackingIgnore` drops a call, it does not say why it was attributed.
`TestCorrelationStore` / `CorrelatedProcessingScope` / `ProcessingCorrelation.Wrap` are the parallel-safe,
opt-in path keyed by work-item id; `TestCorrelationStore.OnResolveMiss` is the existing "no scenario"
hook. `DiagnosticReportGenerator.cs:77-97` counts "Unknown entries … typically from background threads"
keyed on `TestId == "unknown"`, which never fires once any level supplies a real id — the blind spot.
`TcpTapOptions.FallbackTestName/Id` already model "this capture has no identity of its own, give it a
bucket". The NDJSON ingest path has a whole unattributed vocabulary (`IngestPipeline.cs`
`WindowAttributionFallbackId`, `DropUnattributed`, `DiagnosticKind.UnattributedInteractions`) that
in-process capture lacks.

## 3. What right looks like

A scenario owns a dependency call when the call is in the scenario's flow: its own request, what the
SUT does inside that request, and what a consumer does while handling a message that carries the
scenario's correlation — even after the scenario ended, until the run ends. A host's background loops
are nobody's flow. Their calls are **background**: reported, in no scenario, in no fingerprint.

## 4. Options

| | Option | What it fixes | What it leaves | Cost |
|---|---|---|---|---|
| A | **Attribute by correlation, not by currency** (the original recommendation). A call is the scenario's only when its ambient context, or the message being handled, names that scenario. | Consumer work for the right scenario, after it ended too. | **Nothing in the measured case**: a host started inside a test *does* carry the test's ambient context. A is necessary for consumers, not sufficient. | Messaging wrappers propagate a scoped identity; consumers must use them. |
| B | **Provenance mark.** Record which level answered (`RequestHeader`, `FrameworkContext`, `AmbientScope`, `GlobalFallback`) on every log. | Diagnostics see how each call got its scenario; a fingerprint can exclude levels 3 and 4. | Level 2 covers both a scenario's own store calls and an inherited loop. | One enum, one nullable field, ~30 call sites, `MapLogJson`, a diagnostics count. |
| C | **Time-fence** to the scenario's first and last own request. | — | Loses consumer work after the last request; impossible until §7.1 lands. | Cheap after §7.1. |
| D | **Identity expires when the scenario ends.** The framework adapters observe completion; a call resolved at level 2 or 4 to a finished scenario is background. | Every run-long leak: `SharedFactoryCache` hosts attributing the whole run's outbox work to one health-check test, per-test hosts between test end and dispose, fire-and-forget tasks. | Calls a leaked context makes **while the test still runs** — the test-started host's first poll runs during the test. | A finished-scenario registry in core, one check in the resolver, one call per adapter at completion. |
| E | **Detached host start.** Start hosts and other long-lived infrastructure with execution-context flow suppressed, so their loops have no identity: `using (TestIdentityScope.SuppressFlow()) { _ = factory.Services; }`. Kronikol supplies the helper and the guidance; the consumer makes the call. | The measured case at its root, first poll included; the thirteen classes' diagrams. | Nothing for consumers that *should* be attributed (needs A). | A tiny API; a BreakfastProvider change in `CreateAppAndClient`, the shared-factory cache, and the other five fixtures. |
| F | **`background` bucket.** Identity-less calls are recorded under a reserved id instead of dropped, listed in a report section by service and operation, excluded from fingerprints and scenario diagrams by default. | The main host's outbox and consumer work becomes visible; diagnostics can count leaks. | — | A report section; history excludes the bucket; an option to keep today's drop. |
| G | **Hosted services detach themselves.** Wherever Kronikol is registered in the SUT's container (`AddTestTrackingContextPropagation()` today; one registration for all of it), a decorator starts every `IHostedService` under a detached marker — a Kronikol `AsyncLocal` the resolver checks before the framework fetcher — so a host's loops carry no scenario however the host was started. | The measured case at its root, first poll included, with nothing for the consumer to call per host. | Infrastructure Kronikol cannot see: a consumer's own `Task.Run` loop, a Change Feed Processor, a timer. | A decorator over `IHostedService`, one marker, one resolver check. |

**Principle (the user's, 2026-09-14):** people end up using Kronikol in various ways. A test-started
host is an ordinary pattern — a `WebApplicationFactory` per test class is in every ASP.NET Core testing
guide — so a rule that depends on the consumer calling something per host is the last resort, not the
first. Every mechanism below is automatic or degrades to today's behaviour; E is the escape hatch for
what Kronikol cannot see.

**Recommendation (revised):** §7 as a patch, then D, then measure, then G with E, then F, A, B — each a
minor release:

1. **§7 now, patch.** Timestamps on every capture; query-plan requests no longer read as `Create`.
2. **D.** A finished-scenario registry; a level-2 or level-4 identity naming a finished scenario is
   background, with a per-scenario diagnostic ("N calls arrived after this scenario ended"). An explicit
   scope (level 3, `Begin` or a per-message scope) is the caller saying whose work it is and is honoured
   after the end, but marked. Automatic: nothing to wire.
3. **Measure.** With §7.1 in the consumer's report, compare every Cosmos call's timestamp with the
   scenario's own request timestamps. A scenario lasts tens of milliseconds and the outbox polls seconds
   apart, so most of the four polls must land after the scenario ended — D covers those — and the first
   may not. How many land inside decides how much G and E still carry.
4. **G**, automatic for every consumer that registers Kronikol in the SUT's container, and **E**
   (`TestIdentityScope.SuppressFlow()`) for infrastructure it cannot see, documented as the pattern for
   test-started hosts. Acceptance: no `Query` or `Replace` on `orders` in the thirteen classes' read-only
   scenarios; two consecutive docker-lane runs read `nothing changed`.
5. **F.** The bucket and its section; the `unknown` diagnostics fold into it.
6. **A.** `SetFromMessage` becomes a scope restored after the handler in all five wrappers (a fix);
   Pub/Sub attributes stamped on publish; BreakfastProvider's raw consumers go through the wrappers or
   copy their three lines, and register `AddTestTrackingContextPropagation()`.
7. **B.** `AttributionSource` on the log, in `MapLogJson`, counted in the diagnostics report.

## 5. Tests that would decide it

- **G.** An `IHostedService` started by a host built inside a test makes calls that resolve to no
  scenario, with nothing called but the registration (integration: an Example.Api hosted service started
  inside a test).
- **E.** A host started inside `SuppressFlow` makes calls that resolve to no identity (unit: resolver
  under a suppressed context).
- **D.** A call resolved to a finished test's id is background and counted; a call inside an explicit
  scope naming a finished test is still attributed and marked; nothing changes for a running test.
- **F.** Background calls appear in the report section, in no scenario, and never in
  `History.run.json`; with the drop option they disappear as today.
- **A.** `TrackingKafkaConsumer` restores the previous identity after the handler; two messages from
  scenarios A and B on one consumer leave no identity between them; a consumer handling A's message
  while B is current attributes to A.
- **§7.1.** Every enqueued log has a `Timestamp`; a Cosmos capture reads a non-null `timestamp` in the
  report.
- **§7.2.** A Cosmos query-plan request is not a `Create` in any verbosity; in `Summarised` it is not
  captured at all.
- **BreakfastProvider acceptance.** Two consecutive runs with no code change read `nothing changed` on
  every docker and external-SUT lane; the thirteen classes' read-only scenarios hold no Cosmos calls.

## 6. Open questions

1. Should D expire explicit scopes (level 3) as well? Proposed: no, mark only — the caller asked.
2. Should `background` calls still count towards the component diagram's dependency edges?
   Probably yes: the SUT does talk to that store.
3. One pagination scenario (*Listing orders when none exist*) shows no test-side request at all in the
   report while holding the host's calls — check whether a per-test host's client is tracked.
4. Kronikol4J parity: the Java port's attribution is manual; every field here is additive.

## 7. Defects found while tracing (fixed as bugs, independent of this plan)

1. **Non-HTTP captures carry no timestamp.** `RequestResponseLogger.Log` enqueues whatever it is given;
   only `LogPair` and the HTTP handler set `Timestamp`. Two thirds of a docker-lane report's dependency
   calls cannot be placed in time. Fix: `Log` stamps `DateTimeOffset.UtcNow` when the capturer did not.
2. **A Cosmos query-plan request reads as `Create`.** The SDK fetches a query plan with a `POST` to the
   container's documents resource before running a query; the classifier reads that `POST` as a document
   create, so every `Query` in the report is preceded by a `Create` that never happened, and a scenario
   that only queried is shown creating documents. Fix (3.15.1): the plan request is classified from its
   header as internal (`Other`): skipped in `Summarised`, shown as `Other` in `Detailed`, the raw
   `POST` in `Raw`. Cross-run history reads the phantom `Create` leaving a scenario's set of calls as
   `behaviour-changed` once, on the first run after upgrading; the evidence names it.
