# ProxyTap trace from identity plan: #170

**Written:** 2026-10-13, at 4.18.1 (`34145c44`), the day #170 was filed, in the worktree `C:/Code/Kronikol-170`
(branch `plan/170-proxytap-trace-from-identity`), and revised the same day after a review against the real
OpenTelemetry SDK (section 11). **Status: executed and published on 2026-10-13, R0 as 4.18.3 and R1 as 4.19.0; #170
closed.** Green-lit in full by the owner the same day ("implement the plan in full"), every question in section 9 as
recommended, and its releases went out as soon as they passed rather than after #172 and #171 (section 6.2). Evidence labels: **RUN** (measured here; the
probe, its source and what it printed are in the harness), **READ** (in the source at `34145c44`, `file:line`, or in
the wiki at `origin/master` of `../Kronikol.wiki` as fetched on 2026-10-13), **INFERRED** (reasoned from facts,
stated by none), **ISSUE** (taken from #170, not re-measured), **DOC** (a third party's documentation). The harness is
[`PROXYTAP_TRACE_FROM_IDENTITY_PLAN.harness/`](PROXYTAP_TRACE_FROM_IDENTITY_PLAN.harness/README.md).

Two other plans touch the same file at the same time: kronikol-70's `PROXY_TAP_FAILED_FORWARD_PLAN.md` (#171, a
forward that throws records nothing) and kronikol-a5's `PROXY_TAP_HANG_UP_PLAN.md` (#172, a caller that hangs up keeps
its connection). Section 6.2 records the order agreed with both sessions on 2026-10-13.

## 0. Summary

**The problem.** A `ProxyTap` in front of a caller that cannot send a `traceparent` (a mobile app) knows whose call it
is, because the host's `IdentityResolver` says so, and its own records land in the right scenario. But when the
request brings no trace, the tap forwards it under a new random trace id (`ProxyTap.cs:243-247` with nothing
listening, a root server span at `:212-214` and `client.Id` at `:233` with a listener). The service behind the tap
carries that trace on, and every capturer behind it that attributes by trace id (another tap's
`IdentityFromTraceparent`, the OTLP tap's `AttributeByTraceId`) files those calls under a test id no test has. In the
issue's run that was 56 service calls, 70 database calls and 2 calls to a third service, all in "Traffic outside any
test" (ISSUE). The repro gives the same on 4.18.1 (RUN, `repro-170.cs`).

**The root cause.** Kronikol's out-of-process attribution rests on one invariant: the W3C trace id is the test id.
A browser test mints it that way (`TestTrackingIdentity.Create`, `src/Kronikol.Playwright/TestTrackingIdentity.cs:51-61`).
The tap is the only place that knows the identity for a caller that cannot mint, and it breaks the invariant by
minting a random trace. The `test-tracking-*` headers it re-stamps (`ReinjectCorrelation`) do not help past a service
that passes on only W3C trace context, as OpenTelemetry does by default, and a database span never carries them.
The trace id is the only key every hop and every span keeps.

**Verdict on the issue.** The diagnosis is right and the suggestion is the right fix: an opt-in option that starts
the forwarded trace with the test id when the request brings none. Four parts of the suggestion need correcting
before it is built (section 3):

1. "A GUID's digits count" would mint a trace whose downstream records carry the GUID's 32 digits, while the test's
   id keeps its hyphens. Ingest and the report compare test ids ordinally, so those records would still land outside
   the test. The trace backend would find them, but the report, which is what the issue measured, would not.
   Upper-case hex has the same problem, and `ActivityTraceId.CreateFromString` throws on it (RUN). R1 accepts an id
   only when it already is a W3C trace id: 32 lower-case hex digits, not all zero (Q2).
2. With a listener, the issue parents the server span on a random span id that no one exports. A default span id
   makes the server span a real root of the test's trace (RUN, `probe-identity-root.cs`). R1 does that (Q4).
3. The rule must look at where the identity came from, not at its shape. The fallback identity is minted as
   `Guid.NewGuid().ToString("N")` (`ProxyTap.cs:193`), which is a valid trace id, and the issue (rightly) wants
   unattributed traffic to keep random traces. An ignored request (`test-tracking-ignore`) keeps a random trace too,
   as `Reinject` already skips it (Q9).
4. No test id Kronikol's own adapters produce by default is a trace id as written: ReqNRoll and xUnit 2 mint a
   hyphenated GUID, xUnit v3 uses its `UniqueID` and NUnit its `Test.ID` (READ, section 3.4). The option serves a host
   that mints the `N` form, as the issue's suite and a Playwright test's default identity do, and silently does
   nothing for the others. R1 counts those requests and reports them as an `OptionNotApplied` diagnostic; Q2 sets out
   what serving the GUID spellings would take.

**Two things the option needs that the issue does not mention** (sections 3.10 and 3.11), both measured:

- **F3. Kronikol's own listener clears the sampled flag.** `InternalFlowActivityListener` samples every source but
  `System.Net.Http` with `AllData`, and `TestTrackingMessageHandler` starts it on first use, so a tap in a test process
  takes its listener branch with unrecorded spans. It forwards a caller's `-01` as `-00` and a trace it starts as
  `-00` (RUN, `probe-internal-flow-listener.cs`). A service behind it with OpenTelemetry's default sampler then records
  nothing, so R1 as first written would put the right trace id on calls whose database spans are never exported. R0
  forwards the caller's sampled flag, and marks a trace the tap starts as sampled, as the branch without a listener
  already does.
- **F4. One trace per test pools overlapping calls' internal flow.** The internal-flow builder tells calls apart by
  span tree only when several tests share a trace (#87's rule). Two calls of one test that overlap in time and share
  a trace each take both calls' spans, even when the span tree gives each span to one call (RUN,
  `probe-shared-trace-flow.cs`). Today that happens to a browser test behind a tap with a listener. R1 would do it to
  every overlapping call through the tap, a mobile app's parallel requests included. R0 removes from a call's popup
  the spans the tree gives to another call of the same test.

**Found on the way** (sections 3.7 and 8): F1, `AddProxyTapTestTracking(IEnumerable<ProxyTapOptions>)` drops
`InFlightRegistry` (READ). F2, a hand-written listener that returns `None` for a remote parent that is not sampled
makes the tap forward a caller's `-00` trace under a new random, sampled trace (RUN, `probe-unsampled-inbound.cs`). The
first draft said OpenTelemetry's default sampler does this. It does not: the SDK answers a dropped remote parent with
`PropagationData`, and the tap forwards the caller's header unchanged (RUN, `probe-otel-sdk.cs`). F2 is still a defect
for such listeners, and it is on the lines R1 changes.

**Releases.** R0, a patch: F1 to F4, F4 in Kronikol's core and ported to Kronikol4J. R1, a minor: the option, its
diagnostic and its docs. The owner moved both ahead of #172 and #171 (section 6.2).

## 1. How far each claim was checked

| Claim (#170) | Label | Notes |
|---|---|---|
| With no inbound `traceparent` and nothing listening, the forwarded trace id is `ActivityTraceId.CreateRandom()` | READ, RUN | `ProxyTap.cs:243-247`; three calls forwarded three random ids, none the test id. |
| With a listener, the server span is a root and the forwarded trace is `client.Id` | READ, RUN | `ProxyTap.cs:212-214`, `:225`, `:233`; two calls, two random ids. |
| A request that brings a `traceparent` keeps its trace | READ, RUN | `:237-242` without a listener, `:212` with one. Its trace id, yes, with the real OpenTelemetry SDK whatever its sampler (`probe-otel-sdk.cs`). A hand-written listener that returns `None` for an unsampled caller replaces it (F2), and Kronikol's own listener keeps the id but clears the sampled flag (F3). |
| Each record keeps the resolver's `testId` and takes the forwarded trace as `activityTraceId` | RUN | `Record`, `:369-407`. |
| 139 calls, 130 in a scenario, 139 distinct trace ids, and the 56, 70 and 2 calls behind them outside any test | ISSUE | The suite is not available here. |
| The API carries the forwarded trace on; its `traceid` response header matched on all 139 | ISSUE | |
| `ReinjectCorrelation`'s headers get no further than an API that passes on only W3C trace context | READ, DOC | `Reinject`, `:355-365`, stamps four `test-tracking-*` headers. OpenTelemetry .NET's default propagator is trace context plus baggage, so custom headers are not passed on. |
| `IdentityFromTraceparent` describes the browser model, trace id == test id | READ | `ProxyTapOptions.cs:82-87`; the OTLP tap's `AttributeByTraceId` makes the same assumption (`SpanToInteractionMapper.cs:370-375`, `OtlpTapOptions.cs:102-115`). |
| Alternative: a second proxy that adds the header works | INFERRED | It also needs the identity, so it duplicates the resolver. |
| Alternative: setting `Activity.Current` in `IdentityResolver` works only with a listener and leaves an activity open | READ, INFERRED | The resolver runs at `:190`, before the server span starts at `:212-214` with `Activity.Current` as its implicit parent. Nothing ends the activity. |
| Alternative: joining at ingest fixes the report but not the trace backend | READ, INFERRED | Feasible, because the tap's record carries both the test id and the forwarded trace id (RUN). But only `kronikol ingest` would see the join, not an in-process report and not Jaeger or Tempo. Q6 keeps it as a possible complement. |

## 2. Where it stands today

**Identity** (`ProxyTap.cs:189-193`). `ResolveIdentity` (`:322-353`) asks the host's `IdentityResolver` first, then
the `test-tracking-current-test-*` headers and their configured fallbacks, then, with `IdentityFromTraceparent` on
(the default), the inbound trace id. With none of those and `CaptureUnattributedRequests` on, the identity becomes
`(FallbackTestName, FallbackTestId ?? Guid.NewGuid().ToString("N"))`.

**Trace** (`:207-254`). With `EmitActivities` on (the default) and a listener sampling `Kronikol.ProxyTap`, a server
span starts under the inbound `traceparent` or as a root, and a client span `forward` starts under
`Activity.Current`, which is the server span. The forwarded `traceparent` is the client span's id, flags included.
Without a client span, an inbound `traceparent` is copied verbatim; with none, `SynthesizeTraceparent` (default on)
forwards `00-{random}-{random}-01`, and with it off nothing is sent, though the records still get random ids. The
real OpenTelemetry SDK leaves no client span when it drops a trace (its local child of an unsampled span is `None`),
so a dropped trace takes the branches without one (RUN, `probe-otel-sdk.cs`).

**Which branch a test process takes.** A tap that runs in the test process usually has a listener without anyone
adding one: `InternalFlowActivityListener` (`src/Kronikol/InternalFlow/InternalFlowActivityListener.cs:46-52`) listens
to every source but `System.Net.Http` with `AllData`, and `TestTrackingMessageHandler` starts it on its first request
(`TestTrackingMessageHandler.cs:139-143`), as do the gRPC interceptor and the Kafka tracker. `AllData` makes spans that
are not recorded, so the forwarded header carries flags `00` (F3).

**Records** (`:369-407`). Both halves carry the resolved test id and the trace and span chosen above as
`ActivityTraceId` and `ActivitySpanId`.

**Readers behind the tap.** A downstream `ProxyTap` with no id header reads the trace id as the test id
(`:346-347`). The OTLP tap files each span under its trace id (`SpanToInteractionMapper.ResolveTestId`). Ingest leaves
any record with a test id alone; window attribution only touches records with no id or the fallback id
(`IngestAttribution.cs:88-190`), and records whose id is not a test are folded into "Traffic outside any test" when the
host asks for that (`IngestPipeline.cs:874-883`, ordinal comparison).

**The browser model** (`TestTrackingIdentity.cs`). `Create(name)` with no id makes the test id the trace's `N` form,
"the recommended default for proxy-tap topologies" (`:47-50`). `Create(name, id)` and `FromCurrentScope()` keep a
separate trace Guid, so a test whose id comes from its framework has the same gap behind a W3C-only hop as #170
(section 8).

## 3. Findings the issue does not state

### 3.1 Only an id that already is a trace id joins (Q2)

Every reader in section 2 turns a trace id into a test id by taking its text: lower case, 32 digits, no hyphens
(`TraceParent.TryParse` lower-cases, `ProxyTapOptions.cs:194`; an OTLP span's trace id is lower-case hex). Ingest
matches a record to a test by ordinal comparison (`IngestPipeline.cs:874-883`), and so does the report's grouping
(`PlantUmlCreator.cs:113`). So:

- a test id `4bf92f35-77b3-4da6-a3ce-929d0e0e4736` would forward trace `4bf92f3577b34da6a3ce929d0e0e4736`, and every
  record behind the tap would be filed under the second string, a different scenario from the test's;
- a test id `4BF92F35…` would do the same after lower-casing, and `ActivityTraceId.CreateFromString` throws
  `ArgumentOutOfRangeException` on it as given (RUN, `probe-identity-root.cs`).

`InteractionRecord.ToGuid` (`InteractionRecord.cs:619-626`) already treats every GUID spelling as one value, and
`OtlpSpanMapper.TraceIdFor` (`:280-291`) uses it to export a test id as a trace id. But no reader uses it to match a
record to a test, so it does not rescue the join. Making every reader canonicalise ids is a change to ingest, the
report and the query verbs, and it is not what #170 asks for (Q2).

### 3.2 The fallback identity looks like a trace id

`Guid.NewGuid().ToString("N")` (`ProxyTap.cs:193`) is 32 lower-case hex digits. A rule that looked only at the id's
shape would mint one trace per unattributed request, and a `FallbackTestId` such as `0af76519…` would join all
unattributed traffic into one trace, which the issue says it does not want. The rule has to remember that the
identity was resolved (before `:193`), not fallen back to.

### 3.3 An ignored request

`test-tracking-ignore` keeps a call out of the diagram, and `Reinject` already skips it (`:273`). If the tap minted the
test's trace for it, the database spans behind it, which never see the ignore header, would be drawn under the test
while the call that caused them is hidden. R1 keeps a random trace for an ignored request (Q9).

### 3.4 No adapter's default id is a trace id as written

- ReqNRoll mints `Guid.NewGuid().ToString()` per scenario (`src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs:51`),
  and so does xUnit 2 for each test (`src/Kronikol.xUnit2/KronikolResultsSink.cs:120`, which hands the id over, else
  `TestTrackingAttribute.cs:49`). That is
  the hyphenated `D` form: 32 hex digits, but not a trace id as written (READ).
- xUnit v3's `TestContext.Current.Test.UniqueID` (used by `Track.TestIdResolver`, e.g.
  `src/Kronikol.BDDfy.xUnit3/BDDfyDiagramsConfigurator.cs:19`) and NUnit's `TestContext.CurrentContext.Test.ID`
  (`src/Kronikol.NUnit4/TestContextEnumerableExtensions.cs:41`) are not GUIDs at all (DOC: a 64-digit hash and
  `0-1001`-style ids).

So the option applies only where something mints the `N` form: the issue's suite, as its repro's comment says (ISSUE),
and a Playwright test's `TestTrackingIdentity.Create(name)`. A host that hands any other id to the resolver would get
no change and no sign of why. R1 counts those requests in `Diagnostics()` as `DiagnosticKind.OptionNotApplied`, the
kind that already exists for an option that could not take effect (`src/Kronikol/Reports/DiagnosticEntry.cs:119`), and
logs the first one through `Log` (Q5). A tap's `Diagnostics()` reaches a report only through
`IngestRequest.HostDiagnostics` (`IngestPipeline.cs:263`, `:381`), as its `CaptureDegraded` entries do today; nothing
in `src/` calls it, so for a report written in the test process the `Log` line is the only signal (READ).

### 3.5 A root, not an orphan (Q4)

The issue starts the server span under `new ActivityContext(testTrace, ActivitySpanId.CreateRandom(), Recorded,
isRemote: true)`. That span's `ParentSpanId` names a span no one exports, which Jaeger and Tempo show as a missing
parent. With `default` for the span id, .NET starts the span in the test's trace with `ParentSpanId`
`0000000000000000` and no `ParentId`, while `HasRemoteParent` is true and the sampler is shown a sampled remote
parent in that trace (RUN). An exporter that writes a parent span id only when it is not the default then sends the
span as a root (INFERRED; not run against an exporter, so R1's acceptance checks it with the OTLP tap, which keeps
`parentSpanId`). Without a listener,
the forwarded `traceparent` still has to name some parent span, and it names a random one, as the browser model's
does (`TestTrackingIdentity.cs:95`).

The real SDK agrees (RUN, `probe-otel-sdk.cs`): under its default sampler both spans are recorded in the test's trace,
the server span with `ParentSpanId` `0000000000000000`. Under `ParentBased(TraceIdRatioBased(0))` they are recorded
too, because the test's trace reaches the sampler as a sampled remote parent. So a parent-based sampler keeps every
test trace the tap starts, whatever its ratio, as it keeps the `-01` trace a browser test sends. That is intended for
test traffic, and the option's doc comment says so. Under `AlwaysOff` the server span exists unrecorded and the client
span is null, so R1's branch without a client span forwards the test's trace.

### 3.6 `SynthesizeTraceparent` off (Q3)

With `SynthesizeTraceparent` off and nothing listening, today's tap sends no `traceparent` and records random ids
(`:249-254`). With the new option on, the sensible reading is that the option chooses the trace and
`SynthesizeTraceparent` still decides whether one is sent without a listener: nothing is sent, and the records carry
the test's trace, since they belong to it. The doc comment says so; `Validate` does not throw.

### 3.7 F2: a listener that returns `None` replaces an unsampled trace

With a hand-written listener that returns `None` for a remote parent that is not sampled and samples a root, an
inbound `00-0af76519…319c-…-00` is forwarded as `00-38f9dd232b37726e82bfe826cfa6c29a-…-01` (RUN,
`probe-unsampled-inbound.cs`). The server span is dropped, so `client` starts at `:225` with no `Activity.Current`, as
a root of a new trace, and the sampler keeps it. The caller's trace is replaced and its sampling decision reversed,
and every reader behind the tap attributes the call to a trace id no test has: #170's symptom, for a caller that does
send a trace. The same happens with a listener that drops server spans and keeps client spans.

The first draft called this listener the shape of OpenTelemetry's default sampler. The SDK does not behave this way:
it answers a dropped root or remote parent with `PropagationData`, so the server span exists, unrecorded, and its
client child is `None`. With the default sampler, a ratio of 0 and `AlwaysOff`, the tap forwards a `-00` caller's
header unchanged (RUN, `probe-otel-sdk.cs`). F2 therefore needs a listener someone wrote by hand. It is still a
defect, and its fix is the parent R1 needs anyway: the client span starts under `server?.Context` or, when the server
span was dropped, under the inbound context, so both go through one parent expression (section 4.3).

### 3.8 One trace per test is already the browser model's shape

After R1 every call a test makes through the tap shares one trace, as every request a browser test sends already
does. `kronikol query trace <test id>` then lists the test's calls through the tap, which helps
(`QueryCommand.Trace.cs:99`). Two consequences:

- **Internal flow** pools the spans of a test's calls that overlap in time: F4, section 3.11. The first draft said
  the join by trace id, span id and time window (`IngestPipeline.cs:220-241`) keeps two calls' spans apart. For calls
  of one test it does not, because the span id is used only when several tests share the trace.
- **Trace backends.** With a listener each call's server span is a root of the test's trace, so a test is one trace
  with several roots. Without one, each service's span names a parent no one exports, as a browser test's requests
  do. That a backend shows several roots as one trace is INFERRED, not run against Jaeger or Tempo. The report is
  unaffected either way.

### 3.9 #171's failed forward

kronikol-70 found that on a failed forward the server and client spans are never stopped (they are disposed only at
`:303-304`, on success), so a listener exports nothing for that call. #171 ends them with an error status, and its new
record takes the `activityTraceId` and `activitySpanId` locals that R1 sets, so a failed forward joins the test's
trace once both are in, with no extra work in either plan.

### 3.10 F3: Kronikol's own listener clears the sampled flag

With `InternalFlowActivityListener` attached, as it is in any test process that has sent a request through
`TestTrackingMessageHandler` (section 2), the tap forwards (RUN, `probe-internal-flow-listener.cs`):

| Inbound | Forwarded with the listener | Forwarded without it |
|---|---|---|
| `00-0af76519…319c-b7ad6b71…-01` | `00-0af76519…319c-04f117d5…-00` | the inbound header |
| `…-00` | `…-{new span}-00` | the inbound header |
| none | `00-{random}-{random}-00` | `00-{random}-{random}-01` |

`AllData` makes activities that are not recorded, so `client.Id` (`:233`) carries flags `00`. The listener's own doc
chose `AllData` so that "existing OTel exporters that check `Recorded` will skip these activities"
(`InternalFlowActivityListener.cs:21-23`). In the tap that choice reaches the next process: a service behind the tap
whose sampler is parent-based (OpenTelemetry's default) drops the trace and exports nothing. The tap's trace id still
reaches it, so a second tap attributes by it, but the OTLP tap receives no spans. That is the half of #170 the issue
counts most (the 70 database calls). R1 as first written would have forwarded `00-{test id}-{span}-00` here: the right
id on spans that are never exported (RUN, the same probe's R1 simulation).

The tap is meant to be transparent ("copied verbatim … transparent when nobody listens", `:239`), and its branch
without a listener marks a trace it starts as sampled (`TraceParent.Format`'s default flags, `ProxyTapOptions.cs:198`).
R0 keeps both promises with a listener too: the forwarded flags are the client span's when it is recorded, else the
inbound header's, else sampled (section 4.3). The real SDK's decisions are unchanged by this, since it never leaves an
unrecorded client span behind (section 2).

### 3.11 F4: calls of one test that share a trace pool their internal flow

`InternalFlowSegmentBuilder` (`src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs:152-162`) narrows a call's
candidate spans to its span id and that span's descendants only when calls of more than one test claim the trace.
That was #87's deliberate scope: "a trace used by one test keeps today's selection, so no popup of an unaffected
report changes" (`plans/SPAN_ATTRIBUTION_PLAN.md` §2, rule 2). Inside one test, calls are told apart by their time
windows alone. Two calls of one test that overlap in time and share a trace therefore each show both calls' spans,
even when every span's parent is the span one of the calls forwarded (RUN, `probe-shared-trace-flow.cs`, through the
public `BuildSegments`). With a random trace per call, today's shape for the tap, each shows its own.

This already happens to a browser test behind a tap with a listener: the browser sends one trace, and each of the
tap's records carries its own client span id. R1 would spread it to every overlapping call of a resolved identity
through the tap, and a mobile app sends parallel requests as a matter of course.

The fix (R0, in core): when calls of one test claim a trace under more than one span id, a span that descends (by
`ParentSpanId` within the trace) from another call's span id, and not from this call's, leaves this call's
candidates. Nothing else changes:

- **A nested call** keeps showing inside its outer call. As first written, the rule relied on the nested call's
  spans descending from both span ids. A real capture breaks that: Fastify records the test's call under its request
  hook's span, and the calls the resolver makes under the resolver's span, a sibling of the hook's (RUN, the core
  suite's `IngestSpanTests` on the first build, 15 spans down to 7). As built, a span another call's span id claims
  stays in this call's popup when that call ran inside this one, by the rule `kronikol query flow` nests calls by
  (`CallNesting`, `plans/FLOW_NESTING_PLAN.md` R4): made while this call was still waiting for its answer, by the service
  handling it, or by another party on its Kronikol trace. Two calls from one party never nest, so a mobile app's
  parallel calls are still told apart.
- **Spans that descend from no call's span id** stay in every window that holds them, as today. That covers the
  in-process case where another listener creates `System.Net.Http`'s span, which Kronikol's listener skips
  (`InternalFlowActivityListener.cs:33-36`), so the store holds no link between a test's span and the service's
  (INFERRED; a fact pins it, section 5.1).
- **No count is added** to `SpansLeftOut`: the removed spans are not ambiguous, they are shown in the other call's
  popup.

Kronikol4J has the same builder (`../Kronikol4J/kronikol4j-report/src/main/java/io/kronikol/report/flow/InternalFlowSegmentBuilder.java`),
so the rule is ported there in the same release (section 6.5).

## 4. The design

### 4.1 The option (R1)

```csharp
/// <summary>
/// When a request brings no <c>traceparent</c> and the tap resolves its identity (the <see cref="IdentityResolver"/>,
/// the <c>test-tracking-current-test-id</c> header or one of <see cref="TestIdHeaderFallbacks"/>), forward it under a
/// trace whose id is the test id, so the services behind the tap, and every capturer that attributes by trace id,
/// file what they do under the test. This mints for a caller that cannot send a trace (a mobile app, say) what a
/// browser test mints for itself. It applies only to a test id that is a W3C trace id as written: 32 lower-case hex
/// digits, not all zero, such as <c>Guid.ToString("N")</c>. Any other id keeps a random trace, and
/// <see cref="ProxyTap.Diagnostics"/> counts those requests. A request that brings its own <c>traceparent</c> keeps it,
/// and an unattributed or ignored request keeps a random trace. With a listener on
/// <see cref="ProxyTap.ActivitySource"/> the tap's server span is a root of the test's trace; without one the
/// forwarded <c>traceparent</c> is sent when <see cref="SynthesizeTraceparent"/> is on. The trace goes out sampled, and
/// a parent-based sampler on the tap records it whatever its ratio, as it records a browser test's. Default false.
/// </summary>
public bool TraceIdFromIdentity { get; set; }
```

A `bool`, named as the issue named it, beside `IdentityFromTraceparent` and `SynthesizeTraceparent` (Q1). Replacing
a caller's own trace, the issue's possible later mode, would be a separate option that applies only when a trace
arrives, so it composes with this one and needs no enum now.

### 4.2 When it applies

| Inbound `traceparent` | Identity from | Ignored | Test id | Forwarded trace |
|---|---|---|---|---|
| present | anything | either | anything | the inbound trace, as today |
| none | resolver, id header or id fallback header | no | W3C trace id | **the test id** |
| none | resolver, id header or id fallback header | no | anything else | random, counted as `OptionNotApplied` |
| none | resolver, id header or id fallback header | yes | anything | random |
| none | fallback (`CaptureUnattributedRequests`) | either | anything | random |
| none | none (not captured) | either | n/a | random, as today |

### 4.3 The code (R0 and R1)

All in `ProxyTap.HandleAsync`; edit sites are in Appendix A.

1. Before the fallback at `:193`, keep the resolved identity: `var resolved = identity;`.
2. Compute the trace the identity names:
   `var identityTrace = _options.TraceIdFromIdentity && inboundTraceparent is null && !ignore && resolved is { } r ? (IsTraceId(r.Id) ? r.Id : NotApplied()) : null;`
   where `IsTraceId` is an internal static (`Length == 32`, every char `0-9` or `a-f`, not all `0`) and
   `NotApplied()` increments an internal counter, logs the first time, and returns null.
3. One parent for both spans:
   `var parent = inboundContext ?? (identityTrace is { } t ? new ActivityContext(ActivityTraceId.CreateFromString(t), default, ActivityTraceFlags.Recorded, isRemote: true) : default);`
   then `server = ActivitySource.StartActivity(name, ActivityKind.Server, parent)` (a `default` context means
   `Activity.Current` is the parent, as `:214` does today). The client span starts as today when the server span
   exists (implicitly under it, so `Activity.Parent` stays set), and under `parent` when the server span was dropped.
   That second case is F2's fix: R0 ships it for the inbound context, and R1 adds the identity context.
   With a client span, the forwarded header is built from its ids rather than taken from `client.Id`:
   `TraceParent.Format(client.TraceId.ToString(), client.SpanId.ToString(), (byte)((inboundTraceparent?.Flags ?? 1) | (client.Recorded ? 1 : 0)))`.
   That is F3's fix (R0): a recorded span still says sampled, an unrecorded one keeps the caller's flags, and a trace
   the tap starts is sampled, as without a listener. The other flag bits a caller sent pass through.
4. Without a client span, the synthesised branch takes `activityTraceId = identityTrace ?? ActivityTraceId.CreateRandom().ToString()`,
   and so does the branch that sends nothing (section 3.6).
5. `Diagnostics()` adds one entry when the counter is above zero:
   `"{name}: TraceIdFromIdentity did not apply to {n:N0} request(s), because their test id is not a W3C trace id (32 lower-case hex digits); they were forwarded under a random trace, so the services behind this tap cannot be matched to the test"`.

Nothing else changes: the server span keeps its tags, `Reinject` and `Record` are untouched, and the in-flight
registry still sees the resolved identity.

### 4.4 F1: the list overload drops `InFlightRegistry` (R0)

`ProxyTapServiceCollectionExtensions.Copy` (`:42-74`) copies every option except `InFlightRegistry`, which was added
on 2026-08-22 (`f46ebdb6`) without a line there. A tap registered through
`AddProxyTapTestTracking(IEnumerable<ProxyTapOptions>)` therefore never publishes its in-flight identity, and a
database tee that relies on it attributes nothing. The list overload has no fact at all: the only DI fact uses the
`Action` overload (`ProxyTapTests.cs:555`). kronikol-a5 found the same gap independently while writing the #172 plan
and handed it to this plan. R0 adds the line. A reflection fact over every public property
then makes sure `Copy` cannot fall behind again: R1's `TraceIdFromIdentity` must appear there too, or the fact fails.

### 4.5 F4: the builder tells one test's calls apart by span tree (R0, core)

In `InternalFlowSegmentBuilder.Attribute`, beside `TestsByTrace`, keep for each trace the span ids its calls record
and which calls record each (as built: request records only, since a call is its request), and which of a test's
calls ran inside which, by `CallNesting`'s rule over the test's records in time order (section 3.11). A span stays in
a call's candidates when no claimed id is on its chain, or when a call that claims one is this call or ran inside it.
When one test's calls claim a trace under two or more span ids, a call's candidates are the trace's spans less those
that descend from another of those span ids and not from its own. The ancestry walk goes up `ParentSpanId` within the
trace and checks each parent id, held or not, against the claimed set, since the span a tap forwarded is often not in
the store. Traces of several tests keep #87's rule unchanged, and so does every trace claimed under one span id, so a
serial suite's popups, an in-process test's calls under one ambient span, and every existing builder fact are
untouched. The whole-test flow is unchanged. Kronikol4J's `InternalFlowSegmentBuilder.java` takes the same rule and
the same facts.

### 4.6 Docs inside the package

`GenerateDocumentationFile` is on, so these ship (each a sentence, patch or minor with its release):
`ProxyTap`'s class summary (`ProxyTap.cs:12-18`, "the browser/test fixture stamps …") gains the resolver case;
`IdentityFromTraceparent` (`ProxyTapOptions.cs:82-87`) and `IdentityResolver` (`:95-99`) point at
`TraceIdFromIdentity`; `SynthesizeTraceparent` (`:147`) says the trace it mints is random unless
`TraceIdFromIdentity` applies; `EmitActivities` (`:139-145`) says the client span is parented on the inbound or
identity context even when a sampler drops the server span, and that the forwarded header keeps the caller's sampled
flag when the span is not recorded (R0). `InternalFlowSegmentBuilder.BuildSegments`'s remarks (`:17-22`) gain the
one-test rule of section 4.5 (R0).

## 5. Tests, red first

The tap's facts go in `tests/Kronikol.Tests.ProxyTap/ProxyTapTests.cs` with its `StubUpstream`, `ListSink`, `Options` and
`Request` helpers, plus one reflection fact; F4's go in `tests/Kronikol.Tests/InternalFlow/InternalFlowSegmentBuilderTests.cs`
and the same facts in Kronikol4J's builder tests. Listener facts filter stopped activities by their `kronikol.service` tag,
unique per fact, because an `ActivityListener` is process-wide and other facts run taps in parallel. Their
`ShouldListenTo` compares against the constant `ProxyTap.ActivitySourceName`, never `ActivitySource.Name` (harness
README).

### 5.1 R0

- `An_unsampled_inbound_trace_is_forwarded_unchanged_when_the_listener_drops_the_server_span` (F2): a listener that
  returns `None` for an unsampled remote parent and samples a root, an inbound trace with flags `00`; the forwarded
  header names the inbound trace with flags `00`, and the record's `ActivityTraceId` is that trace. Red on 4.18.1 (RUN
  shows the replacement).
- `A_sampler_that_keeps_only_the_client_span_parents_it_on_the_inbound_trace` (F2): `None` for server spans,
  `AllDataAndRecorded` for client spans; the client span's trace is the inbound one.
- `A_listener_that_only_propagates_leaves_the_callers_header_unchanged`: `PropagationData` for a root or remote parent
  and `None` for a local one, which is what the OpenTelemetry SDK does when it drops a trace; inbound `01`, `00` and
  none are forwarded as without a listener. Passes on 4.18.1; it guards the branch the SDK takes.
- `An_unrecorded_forward_span_keeps_the_callers_sampled_flag` (F3): an `AllData` listener, as Kronikol's
  internal-flow listener is; inbound `01` is forwarded `01` and `00` stays `00`, both in the inbound trace, and a
  request with none is forwarded `01`. Red on 4.18.1 for `01` and none (RUN).
- `A_recorded_forward_span_is_forwarded_sampled` (F3): an `AllDataAndRecorded` listener and an inbound `00`; the
  forwarded header says `01`, as on 4.18.1.
- `Kronikols_internal_flow_listener_does_not_clear_the_sampled_flag` (F3): the same as the `AllData` fact through
  `InternalFlowActivityListener` itself, so a change to that listener cannot bring F3 back unseen.
- `Overlapping_calls_of_one_test_in_one_trace_keep_their_own_spans` (F4, builder): the harness probe as a fact, two
  calls of one test overlapping in time, one trace, each with a child span under its own span id; each segment holds
  its own span only. Red on 4.18.1 (RUN).
- As built, four more F4 facts: `A_call_keeps_the_spans_of_the_calls_made_inside_it_whatever_span_they_recorded`
  (the Fastify capture's shape), `A_call_keeps_the_spans_of_a_delivery_on_its_kronikol_trace`,
  `A_call_of_the_test_that_records_no_span_id_keeps_every_span_of_the_trace_in_its_time` and
  `A_trace_of_several_tests_keeps_the_rule_of_87_for_a_call_with_nothing_under_its_span`, each for a mutation the
  planned facts let through. The F4 facts live in `SpanAttributionTests`, #87's file, whose helpers build span trees.
- `A_nested_call_of_one_test_still_shows_inside_its_outer_call` (F4): an outer call whose span is the ancestor of a
  nested call's span; the outer segment holds both calls' descendants, the nested one only its own. Passes on 4.18.1.
- `Spans_that_descend_from_no_call_stay_in_every_overlapping_window` (F4): one test, one trace, two span ids, and a
  span whose parent is neither; both segments hold it, as on 4.18.1. This pins the in-process case of section 3.11.
- `One_tests_calls_under_one_span_id_keep_todays_selection` (F4): a serial and a concurrent pair under one ambient
  span; segments as on 4.18.1.
- `Registering_taps_from_a_list_copies_every_option`: sets every public property of `ProxyTapOptions` (and fills every
  get-only collection) to a non-default value, registers through the list overload, resolves the `ProxyTap` and
  compares property by property. Red on 4.18.1 for `InFlightRegistry`.
- `A_tap_registered_from_a_list_publishes_its_in_flight_identity`: the behaviour behind the last fact, through
  `InFlightIdentityRegistry.MostRecentFor` during a slow upstream.

### 5.2 R1

- `With_TraceIdFromIdentity_a_resolved_test_id_becomes_the_forwarded_trace`: no listener, a resolver naming a 32-hex
  id; the forwarded header is `00-{test id}-{16 hex}-01`, the span differs between two calls, and both records carry
  the test id as `ActivityTraceId` and the forwarded span as `ActivitySpanId`.
- `With_a_listener_the_server_span_is_a_root_of_the_test_trace`: server `TraceId` is the test id, `ParentSpanId` is
  default, `HasRemoteParent` is true; the client span is its child; the forwarded header names the client's trace
  and span with flags `01`; the records carry the client's ids.
- `With_an_AllData_listener_the_test_trace_is_forwarded_sampled`: Kronikol's internal-flow listener attached; the
  forwarded header is `00-{test id}-{client span}-01` (RUN shows `-00` for the design without F3's rule).
- `The_test_trace_survives_a_sampler_that_drops_the_server_span`.
- `Under_a_listener_that_only_propagates_the_test_trace_is_forwarded`: the SDK's `PropagationData` shape, as for
  `AlwaysOff`: the server span exists unrecorded, there is no client span, and the forwarded header is
  `00-{test id}-{16 hex}-01`.
- `An_inbound_traceparent_keeps_its_trace_with_TraceIdFromIdentity_on`.
- `The_test_id_header_and_its_fallback_headers_mint_the_trace_too`: no resolver, the id from
  `test-tracking-current-test-id`, then from a `TestIdHeaderFallbacks` header.
- `Unattributed_requests_keep_a_random_trace`: `CaptureUnattributedRequests` with a trace-shaped `FallbackTestId`, and
  with the fresh per-request id; neither forwarded trace equals the fallback id, and two calls get two traces.
- `An_ignored_request_keeps_a_random_trace`.
- `Ids_that_are_not_trace_ids_keep_a_random_trace_and_are_counted`: theory over upper case, a hyphenated GUID,
  `0-1001`, 32 zeros, 31 and 33 digits and a non-hex letter; no exception, a random trace, one `OptionNotApplied`
  entry whose count is the number of requests, and one log line however many requests.
- `TraceIdFromIdentity_is_off_by_default_and_changes_nothing_when_off`: the default is false, and with it off a
  resolved trace-shaped id is forwarded under a random trace, as on 4.18.1.
- `With_SynthesizeTraceparent_off_nothing_is_sent_and_the_records_carry_the_test_trace` (Q3 as recommended).
- `A_second_tap_behind_a_w3c_only_service_files_the_call_under_the_test`: tap A with a resolver and
  `ReinjectCorrelation` off, a stub service that forwards only `traceparent` to tap B, and tap B with default options;
  B's records carry the test id. This is the issue's chain in one process.

### 5.3 Acceptance in the harness

The issue's repro, run against the built package, prints `== test id` on every line once the option is on, without
a listener, with `InternalFlowActivityListener` attached and with the OpenTelemetry SDK attached, and the forwarded
flags are `01` in all three. One ingest run joins the three sources: a tests file naming the test, the tap's records,
and an OTLP span file whose database span has the forwarded trace id. The report draws all of them in the test's
scenario and none in "Traffic outside any test", where 4.18.1 folds the span. The same run with two calls that
overlap in time shows each call's database span in its own popup only (F4).

### 5.4 Red runs and mutations

The new facts are copied onto `v4.18.1`, with `TraceIdFromIdentity` stubbed as a property that does nothing, and each
must fail on its own assertion (the off-by-default and unattributed facts, and the R0 guards marked "passes on
4.18.1", pass there by design, and the log says so). Kronikol4J's F4 facts are run red against its builder before
the port.
Each of these mutations, run once, must be caught by the fact named:

| Mutation | Caught by |
|---|---|
| Forward `client.Id` again (R0) | `An_unrecorded_forward_span_keeps_the_callers_sampled_flag`, `With_an_AllData_listener_…` |
| Flags from the inbound header alone, without `client.Recorded` (R0) | `A_recorded_forward_span_is_forwarded_sampled` |
| Builder: walk only spans the store holds, not the parent ids they name (R0) | `Overlapping_calls_of_one_test_in_one_trace_…` |
| Builder: drop a span that descends from this call too (R0) | `A_nested_call_of_one_test_still_shows_inside_its_outer_call` |
| Builder: drop a span that descends from no call (R0) | `Spans_that_descend_from_no_call_stay_in_every_overlapping_window` |
| Mint from `identity` after `:193` instead of `resolved` | `Unattributed_requests_keep_a_random_trace` |
| Drop `!ignore` | `An_ignored_request_keeps_a_random_trace` |
| Drop `inboundTraceparent is null` | `An_inbound_traceparent_keeps_its_trace_with_TraceIdFromIdentity_on` |
| Lower-case the id before checking it | `Ids_that_are_not_trace_ids_…` (upper case) |
| Strip hyphens before checking it | `Ids_that_are_not_trace_ids_…` (GUID) |
| Random parent span id instead of `default` | `With_a_listener_the_server_span_is_a_root_of_the_test_trace` |
| `client` back on `Activity.Current` | `The_test_trace_survives_a_sampler_…`, `An_unsampled_inbound_trace_…` |
| No counter increment | `Ids_that_are_not_trace_ids_…` |
| `Copy` without `TraceIdFromIdentity` | `Registering_taps_from_a_list_copies_every_option` |
| The synthesised branch ignores `identityTrace` | `With_TraceIdFromIdentity_a_resolved_test_id_becomes_the_forwarded_trace` |

## 6. Slices, releases and records

### 6.1 Slices

- **R0, patch:** F1 (section 4.4), F2 (section 3.7), F3 (section 3.10) in the tap, F4 (sections 3.11 and 4.5) in
  core and in Kronikol4J, their facts and doc sentences. Bug fixes, nothing new to call. Behaviour changes for the
  changelog: a tap with an unrecorded forward span (Kronikol's internal-flow listener) forwards the caller's sampled
  flag, and a trace it starts as sampled; a listener that drops the server span no longer replaces the caller's
  trace; and a popup of a call that overlapped another call of its test in one trace no longer shows the spans the
  span tree gives to the other call.
- **R1, minor:** `TraceIdFromIdentity`, its rule, the `OptionNotApplied` diagnostic, its facts, its doc sentences
  and the wiki. New public option, so a minor even though its default keeps today's behaviour.

### 6.2 Order and numbers

Agreed by message with kronikol-70 (#171) and kronikol-a5 (#172) on 2026-10-13: #172's patch first, then #171's,
then this plan's. When the owner green-lit this plan the same day neither other plan had been built, and the owner
chose to release this plan's R0 and R1 as soon as they pass; both sessions agreed to rebase onto them. Both other
patches change `GuardedHandleAsync` and `TryRespondBadGateway`, and this plan does not touch them. #171 also edits the
forward's `try` at `:282-304`, next to this plan's `:207-254`; whichever lands second rebases. Numbers are claimed only
with "taking X now" at push time; with 4.18.2 the newest release, R0 would be 4.18.3 and R1 4.19.0 if nothing else
lands first. R0 is its own patch, released before R1 (Q8).

### 6.3 Changelog drafts

**R0.** "ProxyTap forwards the caller's sampled flag when the tap's spans are not recorded, keeps the caller's trace
when a listener drops the tap's server span, and a tap registered from a list publishes its in-flight identity; a call's
internal flow no longer shows another call of its test that ran at the same time in the same trace. A patch: bug
fixes, nothing new to call. In a test process Kronikol's internal-flow listener (started by `TestTrackingMessageHandler`)
records the tap's spans without sampling them, and the tap forwarded that: a browser test's `-01` arrived at the next
service as `-00`, and a trace the tap started went out unsampled, so a service with a parent-based sampler (OpenTelemetry's
default) exported nothing for the OTLP tap to attribute. A hand-written listener that returned `None` for an
unsampled caller made the tap forward a new random, sampled trace. `AddProxyTapTestTracking(IEnumerable<ProxyTapOptions>)`
copied every option but `InFlightRegistry`. The internal-flow builder told one test's calls apart by time alone, so two
overlapping calls in one trace (a browser test behind a tap with a listener) each showed both calls' spans; a span the
span tree gives to one of them now shows in that call's popup only. Kronikol4J's builder takes the same rule."

**R1.** "`ProxyTapOptions.TraceIdFromIdentity` (#170): a tap whose `IdentityResolver` or test-id header names the
test forwards a request that brings no `traceparent` under a trace whose id is the test id, so the services, database
calls and taps behind it are filed under the test. A minor: a new option, off by default. It applies to a test id that
is a W3C trace id as written (32 lower-case hex digits, not all zero); other ids keep a random trace, and
`ProxyTap.Diagnostics()` counts them as `OptionNotApplied`."

### 6.4 Wiki

`Integration-ProxyTap-Extension.md` at `origin/master`: a row for `TraceIdFromIdentity` in the options table beside
`IdentityFromTraceparent` (`:102`), `IdentityResolver` (`:104`) and `EmitActivities` / `SynthesizeTraceparent`
(`:110`), and a short section, "A caller that cannot send a trace", with the resolver and the option together, the
id rule, and the diagnostic. R0's F2 and F3 get a sentence under the trace options, and F4 a
sentence in `Internal-Flow-Tracking.md`, in "How it works" step 3 (`:17`) and the "Span correlation by trace, tree and
time" bullet (`:337`, both at `origin/master` `85abe19`). The page has no listener recipe today;
if one is added, it uses `ProxyTap.ActivitySourceName`. The edits are kept as a script in this plan's harness and
applied at release time, committed at once (the wiki checkout is shared).

### 6.5 Kronikol4J

Kronikol4J has no proxy tap, so F1 to F3 and R1 have nothing to port. It does have the internal-flow builder
(`kronikol4j-report/src/main/java/io/kronikol/report/flow/InternalFlowSegmentBuilder.java`), so F4's rule and its facts
are ported in R0's release, and its ledger records the port. R1's ledger line says there is nothing to port.

### 6.6 Before declaring done

The audit checklist's items that apply here: every doc sentence in section 4.6, checked against the diff; the red
runs and mutations of section 5.4 recorded; the full core suite and the ProxyTap project after the version bump; the
templates' pins and the history action's `VERSION`; for R0, Kronikol4J's builder port with its facts green and its
ledger line, and the full Playwright suite, since F4 changes what a popup shows; `PLANS_STATUS.md`; #170 closed with a
comment naming R0's and R1's versions.

## 7. Where it sits in the roadmap

No row is taken: the owner green-lit the plan directly on 2026-10-13, as #133's plan was.

## 8. Found on the way, not in this plan

- **Callers inside the test process have the same gap.** A test's own `HttpClient` through
  `TestTrackingMessageHandler` uses the trace of `Activity.Current` when there is one, and writes the `traceparent`
  itself only on a transport that does not propagate it. With no current activity it mints a random trace per request
  and sends `00-{random}-{random}-00` (`TestTrackingMessageHandler.cs:165-186`, READ). The identity rides in the
  `test-tracking-*` headers, so a service that passes on only W3C context loses the test the same way. The `-00` is F3's
  shape too: a service with a parent-based sampler would record none of those calls' spans (INFERRED from the SDK's
  handling of `-00` in `probe-otel-sdk.cs`). Owner's call whether that deserves its own issue.
- **The internal-flow listener and an in-process host.** The `AllData` listener that causes F3 also makes an
  in-process host's own spans unrecorded, so a call that host makes onward would carry `-00` as well (INFERRED, not
  measured). F3's fix covers the tap only.
- **`TestTrackingIdentity.Create(name, id)` and `FromCurrentScope()`** mint a trace that is not the test id
  (section 2), so a browser test with a framework id has #170's gap behind a W3C-only hop. Its doc recommends omitting
  the id; nothing in this plan changes it.
- **A listener trap, not a defect.** A `ShouldListenTo` that reads `ProxyTap.ActivitySource.Name` before the type is
  initialised throws `TypeInitializationException` when any `ActivitySource` is created (harness README). Docs and
  tests use the constant.
- **#171's unstopped spans** are kronikol-70's (section 3.9).

## 9. Questions, with recommendations

The owner took every recommendation below on 2026-10-13 ("implement the plan in full").

- **Q1. A `bool` or an enum?** Recommend the `bool` `TraceIdFromIdentity`, as the issue asks. Replacing a caller's
  trace, if ever wanted, is its own option and composes with this one.
- **Q2. Which ids?** Three ways, by whom they serve (section 3.4):
  - (a) Only an id that is a W3C trace id as written (32 lower-case hex, not all zero). Serves the issue's suite and
    a Playwright test's default identity, and no adapter's default id.
  - (b) Also every GUID spelling, forwarded as its `N` form, plus one normalisation wherever records are filed under
    tests: a record whose test id is the same GUID as a test's, spelt differently, is filed under the test's spelling
    (ingest's known-test check, `IngestPipeline.cs:874-883` and `:1089`, and the report's grouping). Serves ReqNRoll
    and xUnit 2 as well.
  - (c) Any id, joined through the tap's own records (Q6).

  Recommend (a) for R1, since it is what every reader already matches. (b) is the first follow-up if a ReqNRoll or
  xUnit 2 host asks: the issue's own suggestion, "a GUID's digits count", only works with (b)'s reader change, and
  every place that compares test ids would go through it, so it needs its own plan.
- **Q3. `SynthesizeTraceparent` off?** Recommend: nothing is sent and the records carry the test trace; documented,
  not rejected.
- **Q4. Root or orphan?** Recommend the root (a default parent span id), measured.
- **Q5. A diagnostic for ids the option cannot use?** Recommend yes, `OptionNotApplied`, with one log line.
- **Q6. Non-hex ids (xUnit v3, NUnit), and an ingest join?** Recommend leaving both out of this plan. A trace hashed
  from the id would reach the backend but not the report without the Q2 reader change; an ingest join by the tap
  record's `activityTraceId` would fix the report but not the backend. Either can follow if a host asks. With a
  listener, the tap's server span already carries `kronikol.test.id` (`ProxyTap.cs:222`), so a backend can find a
  test's calls by that tag whatever their trace id.
- **Q7. Default on in v5?** Recommend listing it among v5's candidate default changes (`ROADMAP.md` D30), since
  turning it on in 4.x would be a major change.
- **Q8. R0 on its own or inside R1?** Recommend its own patch, released before R1. F3 affects every tap that runs in
  a test process with Kronikol's internal-flow listener, today and under R1. F4 already affects a browser test behind
  a tap with a listener. (The first draft gave F2 as the reason; F2 needs a hand-written listener, section 3.7.) R0
  can split in two, the tap's F1 to F3 and the builder's F4 with its Kronikol4J port, if the owner prefers smaller
  releases.
- **Q9. Ignored requests?** Recommend a random trace, as `Reinject` skips them.
- **Q10. F4's scope?** Recommend the rule in section 4.5: a call's popup loses only the spans the span tree gives to
  another call of its test. The simpler rule, a call's own subtree whenever it is not empty, would drop an in-process
  service's spans whose chain runs through a span the listener does not keep (section 3.11).
- **Q11. F3's fix: in the tap or in the listener?** Recommend the tap: its forwarded flags follow the caller, or say
  sampled for a trace it starts, whichever listener is attached, Kronikol's or another `AllData` one. The alternative,
  leaving `Kronikol.ProxyTap` out of `InternalFlowActivityListener` as it leaves out `System.Net.Http`, fixes only
  Kronikol's own listener and takes the tap's spans out of in-process internal flow.

## 10. Assumption ledger

| Assumption | Basis | If wrong |
|---|---|---|
| The issue's suite mints trace-shaped test ids | ISSUE (the repro's comment) | The option does nothing for it; the diagnostic says so, and Q2 or Q6 becomes the fix. |
| OpenTelemetry's default propagators pass `traceparent` and not custom headers | DOC | If a service passed the `test-tracking-*` headers on, it would be attributed already. |
| Trace backends accept several root spans in one trace | INFERRED. The browser model produces orphans (a parent span no one exports), not roots, so it is no evidence (section 3.8) | A backend would show one test as several traces; the report is unaffected. |
| A service behind the tap continues the W3C `traceparent` | ISSUE: the API's `traceid` header matched the forwarded trace on all 139 calls | One of the issue's apps also sends Sentry's `sentry-trace` and `baggage`, which the tap forwards untouched. A service whose propagator prefers `sentry-trace` would continue the app's Sentry trace instead (INFERRED), and R1's trace would stop there. |
| The issue's tap forwarded `-01` | INFERRED: the database spans behind it were exported and counted (ISSUE), so no `AllData` listener was attached to it, or the API's sampler ignores the flag | Elsewhere, under F3's conditions, R1 alone would forward the test's trace as `-00` and the database spans would stay missing, which is why R0 goes first (Q8). |
| #171 records a failed forward with the same `activityTraceId` / `activitySpanId` locals | kronikol-70's message, 2026-10-13 | R1's tests would need one more fact for the failed forward. |

## 11. Log

- **2026-10-13.** Written at 4.18.1 (`34145c44`). Reproduced the issue on the published 4.18.1 (`repro-170.cs`),
  measured the root-span parent and `CreateFromString`'s input rules (`probe-identity-root.cs`), and found F2
  (`probe-unsampled-inbound.cs`) and F1 (by reading). Order agreed with kronikol-70 (#171) and kronikol-a5 (#172).
- **2026-10-13, review (owner's request: check the plan is accurate and complete).** Re-read every cited line at
  `34145c44`; `origin/main` had moved only by 4.18.2 (`9cf89794`) and the #172 plan (`11912425`), neither in the tap.
  Corrected:
  - F2 does not happen under the OpenTelemetry SDK (1.19.1, three samplers, `probe-otel-sdk.cs`), only under a
    listener that returns `None`. Q8's reason changed with it.
  - The claim that a shared trace keeps calls' internal flow apart was wrong for one test's overlapping calls
    (`probe-shared-trace-flow.cs`).
  - The "several roots" assumption leaned on the browser model, which produces orphans.
  - Section 8 misdescribed the `traceparent` that `TestTrackingMessageHandler` sends.

  Added:
  - F3: Kronikol's internal-flow listener clears the sampled flag (`probe-internal-flow-listener.cs`). R1 as first
    written would have forwarded the test's trace unsampled.
  - F4: the builder's one-test pooling, with Kronikol4J's builder to port.
  - The adapter id survey: no default adapter id is a trace id as written; ReqNRoll and xUnit 2 mint hyphenated GUIDs.
  - Where `Diagnostics()` reaches a report.
  - The issue's Sentry-header app.
  - Q10 and Q11.

- **2026-10-13, green-lit.** The owner said "implement the plan in full", taking every question in section 9 as
  recommended, and chose to release R0 and R1 as soon as they pass instead of after #172 and #171, whose plans had
  not been built; kronikol-a5 and kronikol-70 agreed to rebase onto them (section 6.2). `origin/main` was `2b7508d5`
  (4.18.2 and the #172 and #168 plans), with no change to the tap, the builder or their tests since `34145c44`.

- **2026-10-13, R0 built (4.18.3).** F1 to F4 as designed, with these differences, each with its fact:
  - F4's rule as first written failed the core suite: `IngestSpanTests` replays a Jest and OpenTelemetry capture whose
    nested calls' span is a sibling of the outer call's, and the GraphQL call's popup fell from 15 spans to 7. A span
    another call claims now stays in a call's popup when that call ran inside it, by `CallNesting`'s rule (section
    3.11).
  - The nested-call fact could not pass on 4.18.2 as section 5.1 said: the nested call's window also holds the outer
    call's own work, which 4.18.2 shows in both popups.
  - The tap's facts that attach a listener run in a collection of their own, apart from every other (a listener is
    process-wide), in `ProxyTapTraceContextTests`; the registration facts are in `ProxyTapRegistrationTests`.
  - Mutations (harness `r0/mutations.txt`): the planned R0 rows were all caught. Three more survived first and each
    got a fact: the one-test rule applied to traces of several tests too (a fact pins #87's rule there), the rule
    applied to a traced call with no span id, and a call kept open after its answer. Of the rule as built, eleven of
    twelve are caught and one is equivalent.
  - Kronikol4J's port found that a request record handed in twice could become its own outer call, and the walk up
    the outer calls would never end. A call now never runs inside itself, and the walk is bounded; a fact holds it.
  - Kronikol4J ported the rule and its facts (it has no #87 rule, so a trace of several tests keeps its older
    behaviour there; the ledger says so).

- **2026-10-13, 4.18.3 published.** Release run 38410055097, CI 38410050086, CodeQL 38410050189 and CI Summary Preview
  38410050233 passed on `c560c00a`, and nuget.org listed all 62 ids. The wiki has R0's edits (`0932972`), and
  Kronikol4J the port of F4 with its facts (`1e10c71` to `efcee94`) and its ledger line (`08631cd`).
- **2026-10-13, R1 built (4.19.0).** The option as designed (sections 4.1 to 4.3), with these differences, each with
  its fact:
  - Found while building it: a tap started under an activity hands that activity to every request it handles, as
    `Activity.Current` (its accept loop is started with `Task.Run` from `StartAsync`), and a parent context with no
    span id adopts it, so the server span kept the test's trace id but hung off whatever was current at start-up (RUN:
    `ParentSpanId` was the ambient span's). The tap clears `Activity.Current` before its spans start under a parent
    with no span id, inside `HandleAsync`, whose change does not reach its caller.
  - A request that brings its own `traceparent` was counted as not applied when its test id is not a trace id (mutation
    R1-3 survived the first run); it is not counted, and the inbound fact checks it.
  - The facts read `Diagnostics()` after the tap is disposed, since its counters settle only after the response.
  - Mutations (harness `r1/mutations.txt`): 15 of 15 caught, the 10 planned rows among them.

- **2026-10-13, 4.19.0 published; the plan is done.** Release run 38414250063, CI 38414231513 and 38414907388, CodeQL
  38414231752 and 38414907876 and CI Summary Preview 38414232331 and 38414907474 passed on `baa411bb`, and nuget.org
  listed all 62 ids. The wiki has R1's section, "A caller that cannot send a trace" (`8a85456`), and Kronikol4J's ledger
  a line saying there is nothing to port (`ab7ba50`). #170 is closed with a comment naming both releases.

## Appendix A. Edit sites at `34145c44` (4.18.1)

| File | Lines | Change | Release |
|---|---|---|---|
| `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs` | 12-18 | Class summary: the resolver case | R1 |
| same | 72-102 | `Diagnostics()`: the `OptionNotApplied` entry; the counter beside `_forwardFailures` (`:52`) | R1 |
| same | 189-193 | Keep `resolved` before the fallback | R1 |
| same | 207-226 | One parent context; the client span under `server?.Context ?? parent` | R0 (inbound), R1 (identity) |
| same | 231-236 | The forwarded header from the client span's ids with F3's flags | R0 |
| same | 243-254 | `identityTrace ?? random` in both branches without a client span | R1 |
| same | new | `IsTraceId`, `NotApplied` | R1 |
| `src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs` | 82-99, 139-148 | Doc sentences; the new option after `SynthesizeTraceparent` | R0 (`EmitActivities`), R1 |
| `src/Kronikol.Extensions.ProxyTap/ProxyTapServiceCollectionExtensions.cs` | 42-74 | Copy `InFlightRegistry` (R0) and `TraceIdFromIdentity` (R1) | R0, R1 |
| `src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs` | 17-22, 122-273 | F4's rule in `Attribute`; the remarks | R0 |
| `../Kronikol4J/kronikol4j-report/src/main/java/io/kronikol/report/flow/InternalFlowSegmentBuilder.java` | | F4's rule, ported | R0 |
| `tests/Kronikol.Tests.ProxyTap/ProxyTapTests.cs` | new facts | Section 5 | R0, R1 |
| `tests/Kronikol.Tests/InternalFlow/InternalFlowSegmentBuilderTests.cs`, Kronikol4J's builder tests | new facts | Section 5.1 (F4) | R0 |
| wiki `Internal-Flow-Tracking.md` | 17, 337 | F4's sentence | R0 |
| `CHANGELOG.md`, `Directory.Build.props` and the version pins | | Per release | R0, R1 |
| wiki `Integration-ProxyTap-Extension.md` | 102-110 and a new section | Section 6.4 | R0, R1 |
