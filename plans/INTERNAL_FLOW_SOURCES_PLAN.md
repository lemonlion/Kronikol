# Where a span came from: internal flow's sources (#138)

**Date:** 2026-10-26 · **Repo version:** written against 5.10.1 (`origin/main` at `c37042e2`). Line numbers are for 5.31.2 (`55999241`). 5.12.0 to 5.31.2 moved lines in the two handler files, `ReportConfigurationOptions.cs`, `InternalFlowRenderer.cs` and `GrpcTrackingInterceptor.cs` and changed nothing this plan relies on;
5.31.2 (#219 R1) moved the handler's chain walk into `HandlerChain.cs`, with the same answers for every chain this plan
discusses. Every other file cited is unchanged from `1d03279b` through 5.31.2 or changed without moving the lines cited, and `CosmosTrackingMessageHandler.cs`, cited by name only, still records no trace id · **Status:** ✅ executed on 2026-11-07, when the owner asked to "implement the plan in
full" (each question takes its recommended answer): release A (R1 and R2) is 5.32.2, release B (R3 and R4) is 5.34.0, and #138 is closed; see the log.
Written at the owner's request of 2026-10-26: analyse #138 critically, then plan a fix for the underlying problem, which
may not be the fix the issue proposes.

Evidence labels:

- **RUN** means measured on 2026-10-26 with the probe in
  [`INTERNAL_FLOW_SOURCES_PLAN.harness/`](INTERNAL_FLOW_SOURCES_PLAN.harness/README.md), on Windows 11 26200 with .NET
  8.0.27, 9.0.16 and 10.0.11. The probe used Application Insights `Microsoft.ApplicationInsights.DependencyCollector`
  2.23.0 and `Grpc.Net.Client` 2.76.0.
- **READ** means Kronikol's source.
- **SOURCE** means upstream source:
  - .NET's `DiagnosticsHandler.cs` on `release/8.0` (`80c68f48`), `release/9.0` (`b0984b27`), `release/10.0` (`00a171f4`)
    and `release/11.0` (`86ee4f57`, read 2026-11-07);
  - Application Insights' `HttpCoreDiagnosticSourceListener.cs` on `2.x` (`98bc6c54`);
  - grpc-dotnet's `GrpcCall.cs` and `HttpHandlerFactory.cs` on `master` (`7db6c142`).
- **ISSUE** means the issue's own counts, which were not measured again: the six suites are not on this machine.

## 0. Summary

- **The symptom is real, but its cause is not where the issue looks.** The "Unknown" lane is not a gap in the renderer.
  Kronikol's own listener creates it.
  - The listener leaves out the `System.Net.Http` source to protect Application Insights.
  - `HttpClient` makes its span anyway, with or without a listener, whenever `Activity.Current` is set, which is every
    traced call. It does this with `new Activity("System.Net.Http.HttpRequestOut")` (SOURCE).
  - An activity made that way belongs to .NET's unnamed default source `""`, and the listener does hear that source.
  - So the exclusion keeps out no span. It only takes away the span's source name and its kind (`Client` becomes
    `Internal`) (RUN).
- **The reason for the exclusion does not hold.**
  - .NET 8, 9 and 10 write the `DiagnosticListener` events that Application Insights reads on both paths. No path
    excludes the other (SOURCE).
  - Measured with the real Application Insights 2.23 dependency collector: the same 3 of 3 HTTP dependencies, with the same ids
    and correlation, whether the listener hears `System.Net.Http` or not. This held on all three runtimes, with and
    without a parent span, and in either start order (RUN).
  - The Application Insights break fixed on 2026-04-22 was caused by the handler's own `traceparent`, which it put on
    requests ahead of `HttpClient` (`866412be`). Application Insights skips such a request on purpose (SOURCE, lines 288
    and 320).
  - The exclusion shipped the same day and has been given the credit for that fix ever since.
- **So the lane is fixed at capture, not in the renderer (R1).**
  - The listener hears `System.Net.Http`, but samples its spans only under a parent.
  - Measured: hearing it with no condition would make root spans and put a `traceparent` on every untraced call, which
    changes what goes on the wire.
  - Under a parent only, the wire stays exactly as it is today, and nothing new is captured outside a trace (RUN).
  - The span's kind changes from `Internal` to `Client`, and one rule that picks a call's popup reads the kind. R1 keeps
    `HttpClient` spans out of that rule, so no popup changes (F11).
- **The issue's fallback to the operation name is still worth having, as the second line (R2).** It covers activities
  that really have no source: other libraries still use `new Activity(name)`. The probe caught Application Insights' own
  `Microsoft.ApplicationInsights.Init` span this way.
  - The bare namespace is not the right rule, because ASP.NET Core's legacy span would land in a different lane from its
    source's.
  - The Azure SDK's legacy spans still land in a different lane from its source's (`BlobClient` against
    `Azure.Storage.Blobs.BlobClient`). The activity does not carry the namespace, so no rule can join them (F10, Q9).
  - "The DiagnosticListener's name" is not on the activity. It could be had only by subscribing to every listener, which
    changes what libraries do (claim 5).
- **The comment's "two steps per gRPC call" is a second problem, which R1 does not solve (R3).**
  - grpc-dotnet never names its span, so every gRPC step reads `Grpc.Net.Client.GrpcOut`. The method is only in the
    `grpc.method` tag (SOURCE, RUN).
  - The HTTP child under it is the same call again.
  - With Kronikol's own gRPC interceptor there are three spans for one call (READ).
  - R3 names the span from the tag and folds a transport span that is the only child of an RPC client span.
- **Part 2 of the issue is right, and the problem is wider than it says (R4).**
  - Four public inputs do nothing.
  - Five wiki pages and three XML docs send users to them.
  - The test written for one of them passes only because the listener hears every source.
- **Releases:** R1 and R2 are a patch (release A). R3 and R4 are a minor (release B): a deprecation is a minor bump under
  semver, and the fold is a change to the report. See Q1.

## 1. How far each claim was checked

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 1 | Activities made without an `ActivitySource` have an empty `Source.Name`, and the renderer names their lane "Unknown" (L106, L227, L362) | **True.** The lines are now `InternalFlowRenderer.cs:107`, `:260` and `:397`. The call tree also drops the `[source]` tag for them (`:147`). | READ |
| 2 | `HttpRequestOut` is created "whenever something subscribes to its `DiagnosticListener`" | **Half true.** The span is made whenever `Activity.Current` is set, subscriber or not. A subscriber is a second trigger: with Application Insights on and no parent, the span is still made. | SOURCE `release/8.0` L48-72, `release/10.0` L50-73; RUN (`none` mode: the server receives a `traceparent` for a span nobody captured) |
| 3 | 269 of 596 activity diagrams have an Unknown lane, and 85 start with one; the comment counts 1,403 under gRPC in one report | Not measured again. Plausible, since every traced outgoing call over `SocketsHttpHandler` gets such a span. | ISSUE |
| 4 | Fall back to the operation name's namespace | **Right for `HttpClient`, but only by coincidence.** ASP.NET Core's legacy span `Microsoft.AspNetCore.Hosting.HttpRequestIn` would land in a `Microsoft.AspNetCore.Hosting` lane, while its source is `Microsoft.AspNetCore`, so one component would get two lanes. A table must come first (R2). The Azure SDK splits the same way, and no table can mend that (R2, F10). | READ, SOURCE |
| 5 | Fall back to the `DiagnosticListener`'s name | **Not from the activity.** An `Activity` holds no link to any `DiagnosticListener`: the handler writes events about it to a listener the activity knows nothing of. The name can be had only by subscribing to every `DiagnosticListener` (`DiagnosticListener.AllListeners`) and pairing each `.Start` event with `Activity.Current`. That is rejected for R2: a subscriber changes what libraries do. `DiagnosticsHandler` and Azure.Core both test for any subscriber before deciding to make a span, so each library's response would have to be measured for changes on the wire, as plain `include` changed it (section 2.2). The activity's own name already gives a usable lane (Q9). | SOURCE |
| 6 | `TestTrackingMessageHandlerOptions.InternalFlowActivitySources` has no effect | **True, and the problem is wider.** The constructor's `additionalActivitySources` is never read (`InternalFlowActivityListener.cs:44-52`). Three more public inputs feed it (section 3, F6). | READ |
| 7 | `ReportConfigurationOptions.InternalFlowActivitySources` is the one that filters, with `Manual` | **True.** It is read only under `Manual` (`InternalFlowSpanCollector.cs:68-72`). Its XML doc ("`null` includes all sources", `ReportConfigurationOptions.cs:233`) does not say this; the wiki does (`Internal-Flow-Tracking.md:159`). | READ |
| 8 | Each `Grpc.Net.Client.GrpcOut` span has an Unknown span named `POST` under it | **True.** It is reproduced on .NET 9 and 10. On .NET 8 the child is named `System.Net.Http.HttpRequestOut`, because .NET 8 does not set the display name. | RUN (`results-grpc-live.txt`) |
| 9 | An `HttpClient` lane chosen by `OperationName`, or folding into the gRPC parent, would make each gRPC call one step | **Partly.** A lane named `HttpClient` would differ from the name the span carries whenever anything listens to its source (the SUT's OpenTelemetry, or Kronikol after R1). The same span would then land in two lanes depending on who else is listening. The lane has to be `System.Net.Http`. Folding is right, but needs the limits in R3, and alone it still leaves the step reading `Grpc.Net.Client.GrpcOut`. | RUN, READ |

## 2. What happens today

### 2.1 Capture (READ, SOURCE)

- `InternalFlowActivityListener` (`src/Kronikol/InternalFlow/InternalFlowActivityListener.cs:44-52`) is set up like this:
  - `ShouldListenTo` hears every source except the names in `AppInsightsConflictSources`, which is just `System.Net.Http`.
  - `Sample` and `SampleUsingParentId` return `AllData` for everything.
  - `ActivityStopped` adds the span to `InternalFlowSpanStore`.
  - It is started by:
    - `TestTrackingMessageHandler` on its first `SendAsync` (`TestTrackingMessageHandler.cs:149`);
    - `GrpcTrackingInterceptor` (`:594`);
    - `KafkaTracker` (`:275`);
    - `AddActivityListenerForInternalFlowTracking`.
- A second path writes to the same store: the OpenTelemetry SDK exporter `TestTrackingSpanExporter`, which
  `Kronikol.Extensions.OpenTelemetry`'s `AddTestTrackingExporter()` adds (`OpenTelemetryTrackingExtensions.cs:22`).
  - It gets only the spans the SUT's own OpenTelemetry records, from the sources the SUT added. So an `HttpClient` span
    that arrives this way already carries `System.Net.Http`.
  - The store keeps an `Activity` once, by reference (`InternalFlowSpanStore.cs:9-12`), so after R1 a span that both
    paths see is still stored once.
  - The method takes no list of sources, so it adds no fifth input to F6.
- How .NET's `DiagnosticsHandler` makes the span (SOURCE, `release/10.0` L50-73, the same logic on 8.0, 9.0 and 11.0):
  - It runs when `Activity.Current != null`, or when its source has listeners, or when its `DiagnosticListener` is
    enabled.
  - It makes the span through `ActivitySource("System.Net.Http")` when that source has listeners.
  - Otherwise it uses `new Activity("System.Net.Http.HttpRequestOut")`, provided `Activity.Current` is set or the
    `DiagnosticListener` asks for it.
  - On either path it then:
    - writes `HttpRequestOut.Start`;
    - injects `traceparent` (never overwriting one already on the request);
    - writes `HttpRequestOut.Stop`.
  - From .NET 9 the display name is the HTTP method (`L116`). Tags are written when `IsAllDataRequested` is true, which
    holds for both paths.
- An activity made with `new Activity(...)` belongs to `Activity`'s default source, whose name is `""`. Every listener
  whose `ShouldListenTo` accepts `""` hears it, and Kronikol's accepts every name but one.
- `FlowSpan.From` (`FlowSpan.cs:41-55`) makes the swimlane from `activity.Source.Name` and the label from
  `DisplayName ?? OperationName`. Nothing reads `OperationName` when the source is empty, or reads any tag.
- The renderer turns an empty source into "Unknown" in three views (`InternalFlowRenderer.cs:107`, `:260`, `:397`). The
  call tree leaves the tag out (`:147`).
- The empty name also breaks every source-name check. None of these ever sees the span as `System.Net.Http`:
  - the `AutoInstrumentation` trace anchor (`InternalFlowSpanCollector.cs:83-84`), which lists `System.Net.Http` at `:14`;
  - the `Manual` filter (`:100-101`);
  - `ActivitySourceDiscovery` (`:19-20`), which skips empty names, so the diagnostics report never shows how many spans
    were unattributed.

### 2.2 The measurements (RUN)

Three `HttpClient` GETs and, where stated, one gRPC call to a live server.

| Case (all three runtimes unless stated) | `exclude` (Kronikol today) | `include` | `include-parented` (R1) |
|---|---|---|---|
| Span under a parent: source, kind | `""`, `Internal` | `System.Net.Http`, `Client` | `System.Net.Http`, `Client` |
| Display name | .NET 8: `System.Net.Http.HttpRequestOut`; .NET 9/10: `GET` / `POST` | same | same |
| `traceparent` at the server, with a parent | the captured span's id | the captured span's id | the captured span's id |
| No parent, no Application Insights: spans captured / `traceparent` on the wire | 0 / **none** | 3 roots / **present** (a change on the wire) | 0 / **none** |
| No parent, Application Insights on | 3, source `""` (Application Insights' subscription makes the fallback span) | 3, `System.Net.Http` | 3, source `""` (the same as today) |
| Application Insights HTTP dependencies | 3 of 3 | 3 of 3 | 3 of 3 |
| Dependency id = captured span id; operation id / parent id = the parent's trace / span | yes | yes | yes |
| Start order `listener-first` (.NET 8 and 10, `include`) | | 3 of 3, same correlation | |
| gRPC call: `GrpcOut` source / display / `grpc.method` | `Grpc.Net.Client` / `Grpc.Net.Client.GrpcOut` / `/probe.Svc/Do` | same | same |
| gRPC call: its HTTP child | source `""`, display `POST` (.NET 8: `System.Net.Http.HttpRequestOut`) | source `System.Net.Http` | source `System.Net.Http` |

Also seen in the measurements:

- A gRPC call to a closed port fails before it sends any HTTP request. It makes no HTTP child, only `GrpcOut`.
- On .NET 9 and 10, a listener that hears every source also gets the runtime's `Experimental.System.Net.*` spans: socket
  connect, DNS lookup and connection setup (all roots), and `WaitForConnection`. `WaitForConnection` is a child of the
  request span once `System.Net.Http` is heard (F5).
- With the listener started first, it also captures a span with no source named `Microsoft.ApplicationInsights.Init`.

### 2.3 Why the exclusion is there

There were three commits on 2026-04-22, all for one report: "AppInsights DependencyTelemetry broken by TTD 2.x".

1. `9b841658` (2.21.1) left out every well-known source. Its message gives the reason: "caused `DiagnosticsHandler` to
   take the ActivitySource-based code path instead of the DiagnosticListener path. Application Insights SDK 2.x only
   creates DependencyTelemetry from the latter".
2. `866412be`:
   - put off starting the listener until the first `SendAsync`;
   - made the handler stop putting the parent's `traceparent` on requests whose transport has a `DiagnosticsHandler`.
3. `8ceef18b` narrowed the exclusion to `System.Net.Http`, because leaving out every well-known source emptied the
   activity diagrams.

Application Insights' listener does two things with these events (SOURCE `HttpCoreDiagnosticSourceListener.cs:272-400`):

- On `Start` it skips any request that already carries a `traceparent` (L288).
- On `Stop` it builds a dependency only if the request's `traceparent` is the current activity's id (L320).

The handler's pre-injected parent id fails both checks. That is the break `866412be` fixed. Application Insights does
not look at which path made the span, and .NET writes the events on both (section 2.1). No test in the repo has ever run
Application Insights, so the exclusion's premise was never measured. The tests check the mechanism instead:

- `InternalFlowActivityListenerTests.Does_not_listen_to_System_Net_Http`;
- `Does_not_cause_HasListeners_for_well_known_framework_sources`;
- `AddActivityListenerForInternalFlowTrackingTests.Does_not_capture_spans_from_well_known_source`;
- `AutoStartActivityListenerTests` L63-68.

Each of these starts a span from an `ActivitySource("System.Net.Http")` the test made itself. None calls a real
`HttpClient`, so none could see that the span arrives anyway (F7).

## 3. Findings the issue does not state

- **F1. The exclusion keeps out nothing (the root cause).** Every traced `HttpClient` call is captured either way. The
  exclusion only decides whether it is captured with or without its source name and kind (section 2.2).
- **F2. Three features miss `HttpClient` spans entirely.**
  - `AutoInstrumentation`, the default granularity, lists `System.Net.Http` as an anchor (`InternalFlowSpanCollector.cs:14`),
    but no captured span has ever carried that name, so a trace whose only well-known span is an `HttpClient` call is
    dropped.
  - `Manual` with `["System.Net.Http"]` matches nothing.
  - The diagnostics report's discovered sources (`DiagnosticReportGenerator.cs:168-178`) leave all of these spans out.
- **F3. Every gRPC step is labelled `Grpc.Net.Client.GrpcOut`.** grpc-dotnet starts the span without a display name, so
  the display name is the operation name. The method is only in the `grpc.method` tag (SOURCE `GrpcCall.cs`
  `InitializeCall`; RUN).
- **F4. A gRPC call made through Kronikol's interceptor is three spans:**
  - `Kronikol.Grpc`, named after the method (`GrpcTrackingInterceptor.cs:78`);
  - then `GrpcOut`;
  - then the HTTP request.

  This is READ only: the probe used no interceptor. R3's test measures it on `MultiHostPropagationTests`' hosts
  (`tests/Kronikol.Tests.Grpc/MultiHost/HopHosts.cs:59`).
- **F5. On .NET 9 and later, the listen-to-everything listener makes the runtime build connection-level spans for every
  connection.** These are socket connect, DNS lookup, connection setup and waiting for a connection.
  - The roots are never drawn: each has a trace of its own, and the segment builder only draws spans from a claimed trace
    (`InternalFlowSegmentBuilder.cs:201`, `:257`, `:641`).
  - The store still keeps every one of them for the whole run.
  - Once `System.Net.Http` is heard, `WaitForConnection` becomes a child of the request span and would be drawn. That is
    noise, so R1 leaves the `Experimental.System.Net.` prefix out.
- **F6. Four public inputs do nothing:**
  - `TestTrackingMessageHandlerOptions.InternalFlowActivitySources` (`TestTrackingMessageHandlerOptions.cs:57`);
  - the `params string[]` of the public `InternalFlowActivityListener` constructor (`:44`);
  - the `params string[]` of `AddActivityListenerForInternalFlowTracking` (`InternalFlowServiceCollectionExtensions.cs:31-33`);
  - `EnsureStarted`'s `string[]?`, which is internal.

  Their docs promise what none of them does:
  - "auto-starts a listener for well-known sources" (`InternalFlowServiceCollectionExtensions.cs:20-21`; it hears every
    source);
  - "in addition to the well-known auto-instrumentation sources" (`:28-29`);
  - "OpenTelemetry activity source names to capture" (`TestTrackingMessageHandlerOptions.cs:56`).

  The wiki pages that send users to them (lines at the wiki's `f73bdd6`, as in section 6.3):
  - `Internal-Flow-Tracking.md:33`, `:40`, `:219`, `:339`;
  - `HTTP-Tracking-Setup.md:49`;
  - `Report-Configuration.md:155`, which says "for capture-time filtering";
  - `Diagnostics-and-Debugging.md:196-197`, `:228`;
  - `Integration-OpenTelemetry-Extension.md:29`, `:93`, `:131`, `:379`.

  Two tests named `Captures_spans_from_additional_custom_source` pass, but only because the listener hears every source:
  `InternalFlowActivityListenerTests.cs:81` and `AddActivityListenerForInternalFlowTrackingTests.cs:45`.
- **F7. The tests check the mechanism, not the outcome (section 2.3).** That is how a capture that never happened went
  unnoticed for six months.
- **F8. Two more docs are wrong.**
  - `InternalFlowSpanGranularity.Manual` says "spans from manually created `ActivitySource`s"
    (`InternalFlowSpanGranularity.cs:11`). What it actually does is narrow to the sources listed, or keep every span when
    none is listed.
  - `ReportConfigurationOptions.InternalFlowActivitySources` does not say it applies only under `Manual`.
- **F9. Other code still makes spans with no source** (Application Insights' own `Init` span, RUN). R1 does not cover
  them, and R2 does.
- **F10. The Azure SDK names one span two ways** (SOURCE: azure-sdk-for-net `main` at `2ca9b094`,
  `sdk/core/Azure.Core/src/Shared/DiagnosticScopeFactory.cs` and `DiagnosticScope.cs`).
  - Its `DiagnosticListener` is named after the client's namespace, `Azure.Storage.Blobs` (`DiagnosticScopeFactory.cs:47-50`).
  - Its `ActivitySource` is used only when the client is marked stable or the `Azure.Experimental.EnableActivitySource`
    switch is on (`:88-96`, `DiagnosticScope.cs:467-472`). It is named after the namespace plus the activity's name up to
    its first `.`, so `BlobClient.Upload` comes from `Azure.Storage.Blobs.BlobClient` (`:98-101`).
  - Otherwise the span is a legacy `new Activity("BlobClient.Upload")`. It is made only when something subscribes to the
    listener, as Application Insights 2.x does.
  - The activity carries no trace of the client's namespace: its `az.namespace` tag is the resource provider,
    `Microsoft.Storage` (`:74-76`). So no rule can put the two forms in one lane (R2).
- **F11. A span's kind decides which popup shows it.** `InternalFlowSegmentBuilder.ClientSpanOwners` (`:420-490`, called
  at `:630`) gives a `Client` or `Producer` span that no call claims to a call with no trace id, when the span began up
  to 50 ms before that call (`LeadToleranceMs`, `:139`) and ran across it. It leaves out spans that a server or consumer
  span in view answered (`:432`), and spans under another client span (`:443`). It was built for a library that starts its
  span and then logs the call from a tracker inside it, as Cosmos DB's SDK does (4.26.5, 4.26.6).
  - Today an `HttpClient` span is `Internal`, so this never applies to it. R1 makes it `Client` (section 2.2).
  - The premise never holds for an `HttpClient` span:
    - every tracker in an `HttpClient` pipeline is a `DelegatingHandler` outside `DiagnosticsHandler`, so it logs the
      call before the span begins;
    - the wire taps that log a call after it, `ProxyTap` and `TcpTap`, record a trace id.
  - A tracked call claims its own `HttpClient` span. The handler records the span the transport started, read from the
    `traceparent` it put on the request (`TestTrackingMessageHandler.cs:360-373`). Since 5.31.2 it reads the copy
    that a forwarding handler sends, too (#219).
  - So after R1, only an untracked `HttpClient` call (a token fetch, or a client without Kronikol's handler) becomes a new
    candidate. It would be shown in the popup of an untraced call made beside it, such as a Blob Storage, Cosmos DB or
    S3 call, whose trackers record no trace id. R1 prevents that (section 4.1).

## 4. The design

### 4.1 R1: the listener hears `System.Net.Http`, under a parent only (patch)

**Change.** In `InternalFlowActivityListener`:

- `ShouldListenTo` hears every source except names that start with `Experimental.System.Net.` (F5, Q3).
  `AppInsightsConflictSources` becomes an internal `ExcludedSourcePrefixes`. The class docs say why each prefix is
  excluded, and quote section 2.2 on Application Insights so the old reason does not come back.
- `Sample` returns `None` for a span from `System.Net.Http` whose `options.Parent` is `default`. Every other span stays
  `AllData`.
- `SampleUsingParentId` returns `None` for a span from `System.Net.Http` whose parent id is null or empty.

**Why "under a parent".** It is the one setting that changes nothing on the wire (section 2.2). With no parent and no
other subscriber, `HttpClient` makes no span and sends no `traceparent`, as it does today. A root `HttpClient` span could
not be drawn anyway (F5), so nothing is lost.

**Other listeners in the SUT.**

- `ActivitySource` combines every listener's sampling decision and keeps the highest. So when the SUT's OpenTelemetry also
  hears `System.Net.Http`, Kronikol's `AllData` can never lower OpenTelemetry's decision or set `Recorded`.
- When OpenTelemetry samples a span out under a parent, today's code still makes the fallback span (`Activity.Current` is
  set). After R1 the span comes from the source instead. The `traceparent` is the same either way, except for the sampled
  flag, which is `00` in both cases unless something records the span.

**The span's kind.** An `HttpClient` span becomes `Client` instead of `Internal`. That kind is read in one place,
`InternalFlowSegmentBuilder.ClientSpanOwners` (F11). To keep every popup as it is, `ClientSpanOwners` leaves out spans
whose source (by R2's `SpanSource`) is `System.Net.Http`. An `HttpClient` span under another client span, such as
`GrpcOut`, still goes with that span, because the spans under a span go with it (`LeadOf`, `:496-507`).
`CallOwnSpanThroughTheHandlerTests` already runs the handler under a listener that hears `System.Net.Http` (`:28-31`,
`:130`), so the handler's own-span logic is already tested on the path R1 turns on.

**What a user sees.**

- `HttpClient` steps move from "Unknown" to a `System.Net.Http` lane, with the method as the label on .NET 9 and later.
- Under `AutoInstrumentation`, traces anchored only by an `HttpClient` call now appear (F2), so some tests gain a flow
  link. Before release, measure how many diagrams and links change on BreakfastProvider and on the six suites (Q8).
- The report changes, so it needs a Kronikol4J ledger entry (section 6).

**Not in R1.** The handler's `traceparent` logic (`TestTrackingMessageHandler.cs:176-195`) stays as it is.
`HandlerChain.TransportWritesTraceContext` (`HandlerChain.cs:49`, `TransportPropagatesTraceContext` before 5.31.2)
already leaves propagation to `DiagnosticsHandler`, which section 2.3 shows is right.

### 4.2 R2: a span with no source gets its name from its operation (patch)

**Change.** Add one internal function, `SpanSource.Of(Activity)`, used everywhere a span's source is read:

- `FlowSpan.From` uses it for `Source`;
- `InternalFlowSpanCollector.FilterByAutoInstrumentation` and `FilterByManualSources` use it;
- `ActivitySourceDiscovery.GetDiscoveredSources` uses it.

With one function, the lane, both filters and the diagnostics report always agree. The rules, in order:

1. If `Source.Name` is not empty, use it.
2. Otherwise, if the operation name is in this table, use the table's source:

   | Operation name | Source |
   |---|---|
   | `System.Net.Http.HttpRequestOut` | `System.Net.Http` |
   | `Microsoft.AspNetCore.Hosting.HttpRequestIn` | `Microsoft.AspNetCore` |
   | `Grpc.Net.Client.GrpcOut` | `Grpc.Net.Client` |

   These are the .NET and gRPC spans whose source is known, so they land in the same lane by either path. The second and
   third rows are a safeguard: Kronikol's listener hears `Microsoft.AspNetCore` and `Grpc.Net.Client`, so their legacy
   forms appear only in a process where it has not started yet.
3. Otherwise, if the operation name contains a `.`, use the part before the last `.`.
4. Otherwise, use the operation name itself (Q2).

A name R2 makes is drawn like any source name. It goes through the renderer's `SwimlaneTitle` (5.20.1,
`InternalFlowRenderer.cs:205-237`), which writes a zero-width space before a title whose first word holds more than eight
digits, because the diagram engine slows sharply on such a title. So an operation name made of a hash or an id is drawn
as fast as any source name.

`FlowSpan` is public, and its `Source` doc says "the `ActivitySource`'s name, an OTLP span's instrumentation scope"
(`FlowSpan.cs:23`). R2 adds "or, for an activity made without a source, the name taken from its operation".

**What R2 cannot do.** An Azure SDK span made the legacy way lands in `BlobClient`, while the same call made through its
source lands in `Azure.Storage.Blobs.BlobClient` (F10). Which form a client uses is decided once per process (the switch,
the client's stable flag, a subscriber to its listener), so one run shows one form or the other. Q9 asks whether that is
good enough.

**Not changed.** Supplied spans (OTLP ingest) keep `ScopeName ?? ServiceName ?? ""` (`OtlpSpan.cs:127-128`), because an
OTLP span carries no operation name. The renderer's "Unknown" stays only as the last resort for those.

**Why R2 is still needed after R1.** Legacy activities from other code are still common. The probe caught Application
Insights' own `Init` span this way, and the Azure SDK does the same unless the `Azure.Experimental.EnableActivitySource`
switch is on. R1 fixes `HttpClient`; R2 fixes the rule.

### 4.3 R3: one step per gRPC call (minor: a change to the report)

**(a) The label.** In `FlowSpan.From`, when a span's `DisplayName` equals its `OperationName` (nobody named it), the
label comes from the first of these tags that is present:

- `grpc.method` (grpc-dotnet);
- `rpc.service` + `/` + `rpc.method` (OpenTelemetry's RPC conventions).

If neither tag is there, the label stays as it is. A name someone set is never replaced. So `GrpcOut` reads
`/google.spanner.v1.Spanner/ExecuteStreamingSql`, not `Grpc.Net.Client.GrpcOut`.

**(b) The fold.** A span is folded into its parent, and not drawn, when all of these hold:

- it is the parent's only child;
- it has no children of its own;
- (parent source, child source) is one of:
  - (`Grpc.Net.Client`, `System.Net.Http`): the HTTP request that carries the call;
  - (`Kronikol.Grpc`, `Grpc.Net.Client`): the client's span under Kronikol's interceptor span (F4).

The fold runs again on what is left, so a chain of three becomes one step. A call that retried or hedged has several HTTP
children, so it is not folded: each attempt is information. The fold goes in `InternalFlowRenderer.BuildSpanTree`, which
every renderer view calls (`:17`, `:41`, `:134`, `:247`, `:373`, `:436`, `:473`, `:536`), so the activity diagram, call
tree and flame chart always agree. Stage S0 of R3 checks that the component flows and the whole-test flow draw through it
too (they reuse the segments, `ComponentFlowSegmentBuilder.cs:59-71`).

The table grows only when a measured case is added, not by guesswork (Q5). The Confluent schema-registry call in the
comment is not folded: its parent is not a wrapper around the same call, so it keeps its own step, now in the
`System.Net.Http` lane after R1.

### 4.4 R4: the inputs that do nothing (minor: a deprecation)

**Change.**

- Mark `TestTrackingMessageHandlerOptions.InternalFlowActivitySources` `[Obsolete]`. The message points to
  `ReportConfigurationOptions.InternalFlowActivitySources` with `InternalFlowSpanGranularity.Manual`.
- Add a parameterless `InternalFlowActivityListener()` constructor and an
  `AddActivityListenerForInternalFlowTracking(this IServiceCollection)` overload. Mark the `params` versions
  `[Obsolete]`.
  - Overload resolution picks the overload without `params` for a call with no arguments, so only callers that pass
    names get the warning.
- Drop `EnsureStarted`'s parameter. It is internal.
- Fix the docs: the three XML docs in F6, the two in F8, and the wiki pages in section 6.3.
- Remove the obsolete members in v6 (`plans/V6_PLAN.md` gets a line).

**Alternatives rejected.**

- Making the inputs an allow-list. The listener is process-wide and the first caller wins, so whichever handler sent
  first would silently narrow what every test captures. `Manual` already filters, at report time, without that race.
- Giving them the meaning "also hear these excluded sources". After R1 there is nothing left to opt into.

## 5. Tests, red first

Each fact below is a test name, then what it shows. "Red today" means it fails on 5.31.2 (`55999241`).

**R1** (`tests/Kronikol.Tests/InternalFlow/`, using a real `HttpClient` against a loopback server like the probe's):

- `An_HttpClient_call_under_a_span_is_captured_with_its_source`: source `System.Net.Http`, kind `Client`. Red today: the
  source is `""`.
- `An_HttpClient_call_outside_any_span_makes_no_span_and_sends_no_traceparent`: green today. It is the guard against
  plain `include`, which turns it red (section 2.2).
- `Application_Insights_records_the_same_http_dependencies_with_the_listener`: the real `DependencyTrackingTelemetryModule`
  with a capturing channel, in both start orders. It checks 3 of 3 dependencies, that each dependency's id is the
  captured span's id, and that the operation id and parent id are the parent's. It is green today and after R1: it pins
  section 2.3 so the exclusion never comes back unmeasured (Q7).
- `The_runtime_connection_spans_are_not_heard`: no `Experimental.System.Net.*` span in the store after a call.
- `SpanAttributionTests`, for F11, with the file's `Span(...)` helper:
  - `An_untracked_HttpClient_span_stays_out_of_the_untraced_call_beside_it`: a `Client` span from `System.Net.Http`
    begins 10 ms before a call with no trace id, as the Blob Storage tracker logs one, and runs across it. The span is
    not in that call's popup. Red today: `ClientSpanOwners` reads only the kind.
  - The facts for a library's own client span, which `ClientSpanOwners` was built for, stay green unchanged: `:674`,
    `:693` (whose `GET` child is already a `Client` span under the library's), `:716` and `:736`.
- Rewrite, with the premise reversed:
  - `InternalFlowActivityListenerTests.Does_not_listen_to_System_Net_Http` (`:22`);
  - `InternalFlowActivityListenerTests.Does_not_cause_HasListeners_for_well_known_framework_sources` (`:60`);
  - `AddActivityListenerForInternalFlowTrackingTests.Does_not_capture_spans_from_well_known_source` (`:31`);
  - `AutoStartActivityListenerTests.First_SendAsync_does_not_listen_to_System_Net_Http` (`:57`).
- `tests/Kronikol.Tests.Grpc`, on `MultiHostPropagationTests`' hosts: a call between hosts leaves no span with an empty
  source in its segment.

**R2:**

- `SpanSourceTests`: a theory over the table's rows, the namespace rule, a name with no `.`, and a span that has a
  source, which wins over everything.
- `InternalFlowSpanCollectorStoreIntegrationTests` (the file that already drives the collector's filters; there is no
  `InternalFlowSpanCollectorTests`):
  - `Manual` with `["System.Net.Http"]` keeps a legacy `HttpRequestOut`;
  - `AutoInstrumentation` anchors a trace on one.

  Both are red today.
- `ActivitySourceDiscoveryTests`: a legacy span is counted under its derived name.
- `InternalFlowRendererTests`: a legacy span gets no "Unknown" lane in the activity diagram, the call tree's tag or the
  flame data.

**R3:**

- The label comes from `grpc.method` when nobody named the span, and does not when someone did.
- The fold:
  - an only HTTP child is folded;
  - two HTTP children (a retry) are kept;
  - a child with children of its own is kept;
  - `Kronikol.Grpc` to `GrpcOut` to HTTP folds to one step;
  - the activity diagram, call tree and flame data all have the same number of spans.
- `tests/Kronikol.Tests.Grpc`: a call through the interceptor is one step, named after the method.
- Playwright, as `CLAUDE.md` requires for a change to the report (`tests/Kronikol.Tests.EndToEnd/`): the flow popup of
  a gRPC call shows one action and no "Unknown" swimlane.

**R4:**

- A reflection test checks that `[Obsolete]` is present, with its message, on the three members.
- A compile check shows that calls with no arguments to the new overloads raise no CS0618. The repo does not treat
  warnings as errors (`Directory.Build.props` only adds `NoWarn` 1591), so the check needs a compile of its own: a small
  project with `TreatWarningsAsErrors`, or a compile inside the test.
- The two `Captures_spans_from_additional_custom_source` facts (F6) are rewritten to pass no names and to say what they
  show: a custom source is heard with no setup. A test that still sets an obsolete member says so with
  `#pragma warning disable CS0618`.
- Kronikol's own build has no CS0618: `TestTrackingMessageHandler.cs:54` stops reading the option, as `EnsureStarted`
  loses its parameter.

## 6. Releases and records

### 6.1 Releases

| Release | Contents | Bump and why |
|---|---|---|
| A: the next patch | R1 + R2 | **Patch.** Bug fixes: spans keep the source they had, and the filters see it. The report's swimlane names change, which `CLAUDE.md` says is not a major on its own. |
| B: the next minor after A | R3 + R4 | **Minor.** Semver makes a deprecation a minor, and the fold is new report behaviour. No new option (Q4). |

The numbers are not fixed here. The repo is at 5.31.2 at this revision, and other plans release often, so A and B take
whatever numbers come next when each ships. Close #138 with B.

### 6.2 Changelog drafts

- A: "`HttpClient` calls in internal flow are drawn in a `System.Net.Http` lane, not Unknown. The listener now hears
  `System.Net.Http` for calls made under a span; calls outside any span still make no span and carry no `traceparent`.
  `AutoInstrumentation` now keeps traces whose only well-known span is an `HttpClient` call, so some tests gain a flow
  link. Spans from code that makes activities without a source are named from their operation. The listener no longer
  hears .NET's `Experimental.System.Net.*` connection spans. Application Insights 2.x dependency tracking is unchanged,
  measured with its SDK (the reason the source was left out since 2.21.1 did not hold)."
- B: "A gRPC call reads as one step, named after its method. The HTTP request that carries it, and the client span
  under Kronikol's interceptor, fold into it when they are its only child. `TestTrackingMessageHandlerOptions.InternalFlowActivitySources`
  and the `params` of `InternalFlowActivityListener` and `AddActivityListenerForInternalFlowTracking` are obsolete: they
  never changed what was captured. Filter with `ReportConfigurationOptions.InternalFlowActivitySources` and `Manual`."

### 6.3 Wiki (`../Kronikol.wiki`)

Line numbers are for the wiki's `origin/master` at `7f72c19` (5.31.2), whose 5.31.2 edit changed only
`Internal-Flow-Tracking.md:15`, which is not cited. From `aed3a7b` (5.16.0) to `f73bdd6` (5.31.1), four lines
added near the top of `Integration-OpenTelemetry-Extension.md` moved its lines by four, and `Report-Configuration.md`'s row
moved from `:153` to `:155`; no other line cited here moved.

- `Internal-Flow-Tracking.md`:
  - `:31`: `HttpClient` is captured under a span, in its own lane.
  - `:40`, `:219`: the samples set the handler's option. Point them to the report option with `Manual`.
  - `:111`, `:120`: the `AutoInstrumentation` row and the anchor list name `HttpClient`, which matches from A on.
  - `:213`, `:339`: the listener hears every source but the runtime's connection spans, not "well-known sources".
  - A short section on spans without a source and how they are named. A short section on the gRPC fold.
- `HTTP-Tracking-Setup.md:49`: the option is obsolete.
- `Report-Configuration.md:155`: remove "Also configurable on `TestTrackingMessageHandlerOptions` for capture-time
  filtering".
- `Diagnostics-and-Debugging.md:38`, `:196-197`, `:228`: legacy spans are counted under the name R2 gives them.
- `Integration-OpenTelemetry-Extension.md`:
  - `:20`, `:124`, `:292`: the listener starts on the first request, not in the handler's constructor (`866412be`);
  - `:20`, `:50`, `:63`, `:92`, `:296`: it hears every source but one prefix, not "well-known sources";
  - `:29`, `:131`: the samples set the handler's option;
  - `:43`, `:144`, `:205`: the samples pass names to `AddActivityListenerForInternalFlowTracking`; pass none;
  - `:93`, `:379`: the rows that send custom sources to the handler's option;
  - `:288`, `:296`: the API table gains the overloads with no arguments and marks the `params` ones obsolete.

### 6.4 Plans, Kronikol4J

- `PLANS_STATUS.md`: update this plan's row at each release.
- `V6_PLAN.md`: remove the obsolete members.
- Sibling plans:
  - #190 (`PARTY_IDENTITY_PLAN.md:1112`) names this plan, because both edit `TestTrackingMessageHandler`. Its R0 to R3
    shipped as 5.13.0 to 5.16.0, which completes that plan. They changed none of the code this plan edits; R0, R1 and
    R3 moved its lines, and the numbers here follow them. R3 split `TransportPropagatesTraceContext` around a new
    `EndOfChain`, which gives the same answer; 5.31.2 moved both into `HandlerChain.cs`.
  - #191 (`SERVICE_CARD_PLAN.md`) and #192 (`CALL_OPERATION_AND_ORIGIN_PLAN.md`) do not mention internal flow, its
    swimlanes or #138 (read 2026-10-27).
  - #219 (`WHAT_ANSWERS_PLAN.md`): its R1 shipped as 5.31.2 on 2026-11-07. It moved the handler's chain walk into
    `HandlerChain.cs`, which decides the `traceparent` and the double and now follows a call past a handler that hands
    it on. `TransportSpan` now also reads the copy such a handler sends. That is the code F11 and the end of section 4.1
    rely on. Read at 5.31.2, F11's premise holds: a tracked call still claims its own `HttpClient` span. This plan edits
    only the handler's `:54` and `:149`, both in R4, which 5.31.2 left where they were. #219's R2 is still to come: check
    these lines again when it ships. The #219 plan had rejected watching `System.Net.Http` partly on the Application
    Insights premise this plan disproves. Its author was told on 2026-11-07, and the rejection now rests on the two
    reasons that still hold.
  - `GRPC_IDENTITY_PROPAGATION_PLAN.md:794-795` and `ROADMAP.md` Appendix C list #138 as in no plan. Update Appendix C
    when this plan is committed.
- `C:\Code\Kronikol4J\docs\REMAINING_PARITY.md`: one entry each for A and B. The port's spans come from
  OpenTelemetry with scopes, so R1 and R2 probably have nothing to port. The R3 fold and label apply if it draws gRPC
  client spans. Read the port before writing the entries.

## 7. Questions, with recommendations

1. **Two releases (a patch, then a minor), or one minor?** *Recommended:* two. The lane fix stands alone and is a patch.
2. **What lane does a span with no source get when its operation name has no `.`?** The name itself, or "Unknown"?
   *Recommended:* the name. "Unknown" tells the reader nothing the operation name does not.
3. **Leave out `Experimental.System.Net.*`?** *Recommended:* yes. The roots are never drawn. The one child,
   `WaitForConnection`, is pool noise under the first request, and the runtime builds these spans only because Kronikol
   listens.
4. **Fold always, or behind an option?** *Recommended:* always, with no option. The folded span repeats its parent, and an
   option is public surface nobody has asked for. Under `Full` the folded span is still in the store and the call tree's
   data, just not drawn as a separate step.
5. **Fold only the two measured pairs, or any client span with an only HTTP child (Azure SDK, AWS)?** *Recommended:* the
   two pairs, plus the table, which grows only when a case is measured.
6. **Deprecate now and remove in v6, or remove now?** *Recommended:* deprecate now. Removing is a major bump.
7. **Add Application Insights 2.23 as a test-only package to `Kronikol.Tests`?** *Recommended:* yes, pinned. It is the
   only way to keep section 2.3 measured. It is test-only and ships in no package.
8. **Measure again on the six suites and on BreakfastProvider before A (the 1,403, the 269 of 596, and how many
   diagrams and per-call popups change)?** *Recommended:* yes. Record the counts before and after in this plan's log.
   A popup that gains or loses a span is a finding to explain before release (F11).
9. **For an Azure SDK span made the legacy way, is the lane `BlobClient` good enough, or should Kronikol learn the
   client's namespace by subscribing to every `DiagnosticListener` (F10, claim 5)?** *Recommended:* `BlobClient` for
   now. Subscribing changes what libraries do, so it needs a measured study of its own, and the legacy form shows only
   where something such as Application Insights 2.x already subscribes.

## 8. Assumption ledger

| Assumption | Basis | Risk if wrong |
|---|---|---|
| Application Insights 3.x does not conflict | 3.x is built on OpenTelemetry and hears `System.Net.Http` itself, so R1 only adds a listener next to it. Not run. | Low. R1's Application Insights test can add 3.x. |
| The ASP.NET Core host wiring of Application Insights 2.x behaves like the module alone | `Microsoft.ApplicationInsights.AspNetCore` uses the same `DependencyTrackingTelemetryModule`. The probe ran the module, not a host. | Low. R1's test could use a `WebApplicationFactory` host. |
| Linux behaves the same | `DiagnosticsHandler`'s activity path has no OS-specific code. Not run. | Low. CI's Linux lanes run R1's tests. |
| .NET 8 and 9 do not matter to the fix, and .NET 11 behaves as 10 | Kronikol targets `net10.0` only (`Directory.Build.props`, `MultiTargetFrameworks`, still so at 5.31.1). 8 and 9 were measured only to show the 2.21.1 premise was already wrong then. A test project can run on .NET 11, whose `DiagnosticsHandler` (`release/11.0`, `86ee4f57`) has the same paths, display name and source names, `Experimental.` prefix included. Read, not run. | Low. CI runs R1's tests on whatever runtime the lanes use. |
| The issue's counts are about `HttpClient` spans under traced calls | The comment names the span (`POST`, under `GrpcOut`), and RUN reproduces it exactly. | Low. Q8 measures it. |
| Kind is read only by `ClientSpanOwners` | A search of `src/` at 5.14.0 for `.Kind` and `ActivityKind`, made again at 5.31.1 with property patterns (`Kind: ActivityKind`) included, and of 5.31.2's new `HandlerChain.cs` (whose two `.Kind` reads are of its own chain ends), finds `InternalFlowSegmentBuilder.cs:432` and `:443` and nothing else on a captured span; the OTLP encoder and mapper read the kind of captures, not of flow spans. | Medium: a new reader added before A ships. Search again when A is built. |
| The wire taps record a trace id for an `HttpClient` call | `ProxyTap.cs` and `TcpTap.cs` set `ActivityTraceId`, and every `HttpClient` call under a span carries a `traceparent`. Not run for this plan. | Low. R1's `SpanAttributionTests` fact covers the untraced case either way. |

## 9. Log

- **2026-10-26:**
  - Plan written. Checked the source of .NET 8, 9 and 10, Application Insights 2.x and grpc-dotnet.
  - Measured 38 runs of the probe, recorded in the harness.
  - Nothing implemented.
- **2026-10-27:** reviewed against 5.14.0 (`8d7d1e1d`) at the owner's request, to make it complete and accurate.
  - Line numbers moved to 5.14.0; the wiki's moved to its `183dbb0`.
  - Found F11: R1 turns `HttpClient` spans from `Internal` to `Client`, and `ClientSpanOwners` reads the kind. R1 now
    keeps `System.Net.Http` spans out of it, with a red-first fact.
  - Found F10: the Azure SDK names one span two ways, which no rule can join. R2 says so, and Q9 asks.
  - Claim 5 was too strong: a listener's name can be had by subscribing to every `DiagnosticListener`, at the cost of
    changing what libraries do. Rejected for now (Q9).
  - Added the OpenTelemetry exporter as the store's second path; it has no input to deprecate.
  - Corrected the test list: a second `Captures_spans_from_additional_custom_source`, the facts' real names and files,
    and the CS0618 handling R4 needs.
  - Checked and unchanged: Kronikol targets `net10.0` only; the store never evicts (F5); the popup script draws no span
    data, so R3's fold in `BuildSpanTree` reaches every view; `WaitForConnection` is captured only when
    `System.Net.Http` is heard (`results-noai.txt:38`, `:48`).
  - Issue #138 has had no new comment since 2026-10-26.
- **2026-10-28:** checked each release since 5.14.0 against every file this plan cites.
  - 5.15.0 (`6bbc2ce1`, #190 R2) changed none of them.
  - 5.15.1 (`5ac5022f`) rewrote part of `CosmosTrackingMessageHandler.cs`, which still logs its calls with no trace id,
    so F11 holds.
  - 5.16.0 (`15a95d4c`, #190 R3) moved `TestTrackingMessageHandler.cs:358-365` to `:362-369` and split
    `TransportPropagatesTraceContext` around `EndOfChain`, with the same answer. Line numbers now follow 5.16.0.
  - The wiki's cited lines are unchanged at `aed3a7b`.
- **2026-11-07:** checked again at the owner's request, against 5.31.1 (`896ddad3`, 87 commits past 5.16.0) and the
  wiki's `f73bdd6`.
  - No finding or design choice changed. Issue #138 has had no new comment since 2026-10-26.
  - Lines moved:
    - the handler (5.30.2 removed three fields, so every line cited is three higher);
    - its options' `InternalFlowActivitySources` (5.31.1);
    - `ReportConfigurationOptions.cs:234`;
    - the renderer (5.20.1's `SwimlaneTitle`, which R2 now names);
    - the gRPC interceptor (5.16.1);
    - two wiki pages.

    Every citation now follows 5.31.1.
  - Fixed two errors in the plan: `Report-Configuration.md`'s row was cited as `:149-150`, and R2's tests named
    `InternalFlowSpanCollectorTests`, a file that does not exist.
  - .NET 11 (`release/11.0`) makes the `HttpClient` span as .NET 10 does, and keeps the `Experimental.` prefix on its
    connection sources, so R1 is unchanged.
  - The kind is still read only by `ClientSpanOwners`, and the Cosmos DB tracker still records no trace id, so F11
    holds. The taps still record one.
  - The #219 plan's author was told of the Application Insights measurement.
  - 5.31.2 (`55999241`, #219 R1) shipped the same afternoon. It moved the handler's chain walk into `HandlerChain.cs`
    and lets `TransportSpan` read a forwarder's copy, so F11's premise holds (section 6.4). It moved the lines cited
    for the `traceparent` logic, `TransportSpan` and `CallOwnSpanThroughTheHandlerTests` by one to three, and
    citations now follow 5.31.2.
- **2026-11-07, release A (5.32.2):** green-lit the same afternoon ("implement the plan in full"); every question takes
  its recommended answer. Built in its own worktree on 5.31.2, beside other sessions' releases. What building it found:
  - **A span another listener samples reaches every listener of its source.** .NET reports a stopped span to each
    listener that hears its source, whatever each sampled, so an app's OpenTelemetry that samples every span would have
    put the root span of each call made outside a trace in the store. The listener's `ActivityStopped` now skips an
    HttpClient span with no parent too, so nothing new is captured outside a trace (fact
    `A_root_HttpClient_span_another_listener_samples_is_not_kept`).
  - **F11's case is an untraced call made inside a request.** Outside one, the lead-span rule already gives a span that
    began up to 50 ms before an untraced call to it, whatever its kind, as it did before #138. Inside one, that rule
    leaves such a span to the request, and only `ClientSpanOwners` hands a client span to the call, so that is where
    the kind matters. `SpanAttributionTests`' fact puts the call inside a request.
  - **A bug of F2's kind in the same filter, fixed in A.** `AutoInstrumentation`'s list names `Azure.Cosmos` and
    `Azure.Storage`, but the Cosmos DB SDK's source is `Azure.Cosmos.Operation` (azure-cosmos-dotnet-v3,
    `OpenTelemetryRecorderFactory.cs`) and the Storage clients' are named after the client (F10), so they matched no
    source. A listed name now covers the sources in its namespace (`InternalFlowSpanCollector.IsWellKnown`), in the
    filter and in the diagnostics report's well-known status.
  - **The wire facts run alone.** Six test classes hear every source with a listener that samples every span, which
    makes every HttpClient call in the process start a span and send a `traceparent`. `HttpClientSpanCaptureTests`
    runs in a collection with `DisableParallelization`.
  - Q8: the six suites are not on this machine; the in-repo example suite `Example.Api.Tests.Component.xUnit3`, whose
    cow service fake listens on a real socket, stood in. Run on 5.32.0 and then on this release and compared by counts
    alone (`harness/q8/compare.cs` on the decoded pages, `results-example-xunit3.txt`): the same 11 popups and 16 spans,
    the same span count in every popup, and the one popup that drew an "Unknown" lane, for 4 steps, draws them in a
    `System.Net.Http` lane. No "Unknown" lane is left.
  - Checks: the core suite on 5.32.1 with this release (12,550 passed, 2 skipped); the end-to-end suite on 5.32.0 with
    it (2,151 passed, 28 skipped); the gRPC, OpenTelemetry, Otlp, ProxyTap, Kafka and TcpTap test projects on 5.31.2
    with it (262; 4; 447; 203, 4 skipped; 400, 3 skipped; 506, 7 skipped). release.slnf was not built in Release here,
    for disk space; CI builds it.
- **2026-11-07, release B (5.34.0):** R3 and R4 as planned, but for two corrections to the fold, each measured:
  - **The fold moves the folded span's children up.** The plan folded a transport span only when it had no children of
    its own. A host in the test's process hangs its spans under the request that carried the call, so a call to such a
    host would never have folded. The folded span's children now move up to the span it folds into.
  - **The fold takes the only transport span of its source, not the only child.** Measured on a real call through the
    interceptor over h2c (`GrpcCallSpansTests`): the interceptor sends its own span as the call's parent, so the host's
    request span hangs under Kronikol's span, beside grpc-dotnet's `GrpcOut`, and the plan's only-child rule never
    fired; the call was still three steps. Two transport spans of one source under a call, a retry or a hedge, still
    keep both. The host's request span carries `grpc.method` too, so its step reads as the method it served.
  - The popup's title still counts the spans captured (`InternalFlowHtmlGenerator.Title`), which the plan left as it was.
  - **On Grpc.Net.Client 2.45.0**, the floor CI's FloorRun pass builds the gRPC tests on, grpc-dotnet makes its span
    with `new Activity(...)`, with no source. R2's naming gives it `Grpc.Net.Client`, so the fold reads it as on the newest
    2.x; measured on both. 5.32.2's own fact asked for the raw source and failed there on main's CI; `4cf31b4b` (tests,
    no bump) reads the source as the report does. It was not a race: 400 calls, 16 at once, never returned before the
    span was stored.
  - S0: the component diagram's relationship popups gather `FlowSpans` and draw through the same renderer, so the fold
    reaches them.
  - R4: three public inputs are obsolete, each with an overload that takes no names; the fourth, `EnsureStarted`'s
    parameter, was internal and is gone. `plans/V6_PLAN.md` item 11 removes the three.
  - Checks: the core suite on 5.33.0 with this release (12,592 passed, 2 skipped); the end-to-end suite (2,154 passed,
    28 skipped); the gRPC tests on the newest Grpc.Net.Client and on 2.45.0 in Release (263 each); the OpenTelemetry
    tests (4). release.slnf was not built in Release here, for disk space; CI builds it.

## Appendix A. Edit sites at 5.31.2 (`55999241`)

| File | Lines | Release |
|---|---|---|
| `src/Kronikol/InternalFlow/InternalFlowActivityListener.cs` | 5-55 (docs, set, `ShouldListenTo`, `Sample`), 66-75 (`EnsureStarted`) | R1, R4 |
| `src/Kronikol/InternalFlow/FlowSpan.cs` | 23 (`Source` doc), 41-55 | R2, R3 |
| `src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs` | 420-490 (`ClientSpanOwners`, the filter at 443) | R1 |
| `src/Kronikol/InternalFlow/InternalFlowSpanCollector.cs` | 74-103 | R2 |
| `src/Kronikol/InternalFlow/ActivitySourceDiscovery.cs` | 19-20 | R2 |
| `src/Kronikol/InternalFlow/InternalFlowRenderer.cs` | 161-185 (`BuildSpanTree`) | R3 |
| `src/Kronikol/InternalFlow/InternalFlowServiceCollectionExtensions.cs` | 10-43 | R4 |
| `src/Kronikol/InternalFlow/InternalFlowSpanGranularity.cs` | 11 | R4 (doc) |
| `src/Kronikol/Tracking/TestTrackingMessageHandlerOptions.cs` | 56-57 | R4 |
| `src/Kronikol/Tracking/TestTrackingMessageHandler.cs` | 54, 149 | R4 |
| `src/Kronikol/ReportConfigurationOptions.cs` | 234 | R4 (doc) |
| `src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs` | 594 | R4 (call site) |
| `src/Kronikol.Extensions.Kafka/KafkaTracker.cs` | 275 | R4 (call site) |
