# gRPC identity propagation plan: #134

**Written:** 2026-10-07, at 4.9.0 (`417c8e58`). #134 was filed the same day against 4.6.0 (`a074cf14`) and links one
line, `GrpcTrackingInterceptor.cs` L353 (its `traceparent`), which is unchanged at `417c8e58`. 4.9.1 (`f6bf1d03`) was
tagged later the same day and touches none of the files this plan edits; at its tip only `CHANGELOG.md`'s line numbers
move (by 27). **Status: green-lit on 2026-10-08 with Q1 and Q4 as recommended, and executing: R0 is 4.12.1 and R1 4.13.0 (§11).**
Evidence labels: **RUN** (measured here),
**READ** (in the source, `file:line` at `417c8e58`), **INFERRED** (reasoned from facts, stated by none), **ISSUE** (taken
from #134, not re-measured). The probe behind every RUN line, and its output, are in
[`GRPC_IDENTITY_PROPAGATION_PLAN.harness/`](GRPC_IDENTITY_PROPAGATION_PLAN.harness/README.md).

#134 reports that `GrpcTrackingInterceptor` puts only `traceparent` into a call's metadata, while
`TestTrackingMessageHandler` puts `test-tracking-current-test-name` and `-id` on its requests. In a suite that runs ten
ASP.NET Core hosts in one process, a host called over gRPC therefore cannot attribute its own database, HTTP and Kafka
calls to the scenario: the call carries no test headers, and `TestServer` does not flow the test's `AsyncLocal`. The
issue asks for the identity headers in the metadata by default, with an option to turn it off, and for the interceptor
to implement `ITrackingComponent.HasHttpContextAccessor`, whose absence makes the diagnostic report show `⚠ null`.

The defect is real. It reproduces on 4.6.0 and on 4.9.0, and the issue's workaround fixes it (RUN). It is also wider
than the issue says. A single gRPC service called straight from the test, the shape `CreateTestTrackingGrpcClient`
builds, loses its downstream calls the same way, so every gRPC-fronted service under test is affected, not only
multi-host suites. The owner's BreakfastProvider shows it (RUN, P11):

- four of its scenarios read CosmosDB behind a gRPC call, and none of them records a call under that gRPC call, though
  the same run records 34 such reads in other scenarios;
- one of the four is a stream that fails `NotFound`, and it is recorded as `OK` (F11).

The ask is right and buildable, with three corrections the measurements force:

- **The values need an encoding first.** A test name with a non-ASCII character fails the call over a real socket,
  and one with a line feed fails it even in memory. That is true of the HTTP handler today: a test named `Café order`,
  or an xUnit v3 theory whose long string argument xUnit shortens with `···`, cannot call its service over Kestrel
  through Kronikol. Copying the handler into gRPC would copy the failure. With the encoding §4.1 proposes, all five
  names tried cross both kinds of socket and arrive exact (RUN).
- **The HTTP handler does not put the identity on every request.** A host's handler that has an accessor does not
  forward the identity it received on the host's own HTTP calls unless `HeadersToForward` names the headers. The DI
  filter gives every handler it builds an accessor, and a test pins this as deliberate. A handler built without one
  does forward it, through the middleware's scope (RUN, P8 and P8c). Two wiki pages describe only the second case, and
  Kronikol4J re-stamps on every hop. gRPC has to re-propagate on every hop, or a three-host chain loses the third host.
- **`HasHttpContextAccessor` alone does not fix the column.** The report reads one arbitrary instance of each group and
  prints a hard-coded `⚠ null`, and three more components that it lists lack the member.

This plan:

- **checks each claim** (§1) and **measures** the defect, the workaround and the edge cases on three in-process hosts
  and on Kestrel, and the defect in BreakfastProvider's own CI report (§2);
- **lists what the issue does not say** (§3): the single-service case (F1), the interceptor writing into the caller's
  `Metadata` (F2) and throwing on `Metadata.Empty` (F3), an `Activity` that leaks into the caller (F4), trace flags
  that make an OpenTelemetry host drop its spans (F5), header values that fail calls (F6), the accessor column (F7),
  HTTP's hop rule (F8), a `Guid.Parse` that throws on a foreign trace id (F9), a trace id gRPC never carries (F10),
  streaming calls recorded as `OK` before they run (F11) and stale docs (F12);
- **designs the fix** (§4): one lossless encoding for identity values in every writer and reader (R0), the four
  identity headers in every gRPC call's metadata behind `PropagateTestIdentity` (R1), and streaming outcomes (R2).

It ships as three releases (§6.2): **R0, a patch**, for the defects on the code the fix touches; **R1, a minor**, for
the propagation and its option; **R2, a patch**, for streaming outcomes.

## 0. Summary

| | |
|---|---|
| What ships | **R0, a patch:** identity header values are encoded losslessly (printable ASCII unchanged, anything else as an RFC 8187 `UTF-8''` value) by every writer and decoded by every reader, so no test name fails a call; the interceptor stops writing into the caller's `Metadata`, keeps a caller's `traceparent`, takes the trace flags from its span and gives `Activity.Current` back; a foreign trace id no longer throws; the diagnostic report's accessor column counts instances. **R1, a minor:** `GrpcTrackingOptions.PropagateTestIdentity` (default `true`) puts the four identity headers into every gRPC call's metadata, on every hop; `HasHttpContextAccessor` on the interceptor and on the three other components the report lists without it. **R2, a patch:** a streaming call's outcome is recorded when the stream ends |
| The rule (R1) | When an identity resolves and names a scenario (`TestIdentity.IsAttributed`), the call's metadata gains `test-tracking-current-test-name`, `-id`, `-trace-id` and `-caller-name`, each only when the caller's metadata lacks it. The identity is whatever the interceptor resolves, including one that arrived on the request the host is serving. It is sent whether or not the call itself is tracked in the current phase |
| What it measured | On 4.6.0 and 4.9.0, B's call to C is `unknown` behind a gRPC hop, from host A or straight from the test (P1, P1b); the issue's workaround makes it `id-1`. In BreakfastProvider's CI report, four gRPC scenarios miss the CosmosDB read behind their gRPC call (P11). A reused `Metadata` sends B one `traceparent` joining every earlier call's, and from the second call B's span leaves the trace (P2); `Metadata.Empty` throws (P3); non-ASCII and line-feed names fail calls, and with §4.1's encoding all five tried arrive exact over both sockets (P10) |
| Bumps | R0 patch (nothing new to call; one internal helper). R1 minor (a new option and four new public members; its default is on, as 2.34.0's messaging propagation was). R2 patch |
| Open | Ten questions (§9). Q1, whether HTTP should also re-stamp on every hop, is the one with the largest effect; the plan recommends it as its own release and does not do it |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | `TestTrackingMessageHandler` puts the name and id headers on every request | READ, RUN | **Partly.** `TestTrackingMessageHandler.cs:216-223` adds each header only when the request the host is serving did not carry it, and a test pins that (`TestTrackingMessageHandlerTests.cs:972-1008`). It sees that request only through an accessor. The DI filter always gives the handler one (`TrackingHttpMessageHandlerBuilderFilter.cs:13-23`), and then host A's call to host B carries neither header (P8). Built without one, the handler resolves the middleware's scope and adds all four (P8c) |
| C2 | `AddTestTrackingContextPropagation()` turns them into a `TestIdentityScope` in the host that receives them | READ | **True.** `TestTrackingContextMiddleware.cs:25-40` begins a scope when both headers are present; it reads nothing else and skips no path or content type, so `application/grpc` requests are wrapped like any other |
| C3 | `GrpcTrackingInterceptor` adds only `traceparent`, with trace flags `00` (L353) | READ, RUN | **True, unchanged at `417c8e58`,** and it adds it to the caller's own `Metadata` object (F2) |
| C4 | Service B can't attribute anything it does while serving the call | RUN | **True on 4.6.0 and 4.9.0,** from host A whether its client has an accessor or not (P1, P6), **and straight from the test** (P1b, F1), which is BreakfastProvider's shape (P11) |
| C5 | gRPC calls go through the ASP.NET Core pipeline, so the middleware handles them once the metadata carries the two headers | RUN | **True.** With the issue's second interceptor, B's call to C is `id-1` from `RequestHeader` (P1); no change is needed on the receiving side |
| C6 | The workaround copies the `Metadata`, since callers may share one between calls | RUN | **True, and the shipped interceptor does not:** three calls on one `Metadata` leave three `traceparent` entries in it, which B receives joined into one value (P2), and `Metadata.Empty` makes the interceptor throw (P3) |
| C7 | With the workaround, a chain test, API, B, C, stub shows every hop's calls, and `kronikol query trace` follows it | ISSUE | Not re-measured end to end (the owner's suite). Consistent with RUN: one gRPC hop is enough to attribute the next host's calls (P1), and every gRPC hop carries a `traceparent` for `trace` to follow. An HTTP hop from inside a `TestServer` host carries none (§8) |
| C8 | The interceptor lacks `HasHttpContextAccessor`, so the report shows `⚠ null` even with an accessor | READ, RUN | **True** (P7), **and the column is wrong in two more ways:** it reads `instances[0]` of an unordered `ConcurrentBag` (`DiagnosticReportGenerator.cs:207`), and `null` is a literal (`:210`) for a member that is a `bool`. Three more components that the report lists lack the member (F7) |
| C9 | Ask: propagate by default with an option to turn it off, and implement `HasHttpContextAccessor` | RUN, READ | **Buildable, with three corrections:** the values need an encoding (F6), every hop must re-propagate (F8), and the column needs its own fix (F7). The headers to send are the HTTP handler's four, not two. The trace id gives the called host `RequestHeader` attribution in `MessageTracker` (L364) and a trace id shared with the call, for `kronikol query flow` (F9, F10). No reader uses the caller name's value; it is sent for parity |

## 2. Where it stands today

### 2.1 The code

**The interceptor** (`src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs`, 432 lines) handles five call kinds:
async unary (L35), blocking unary (L82), server streaming (L130), client streaming (L170) and duplex (L208). Each one
does the same thing:

1. It returns the plain continuation when the phase is not tracked (L42).
2. It resolves an identity with `TestInfoResolver.ResolveWithSource(_httpContextAccessor, fetcher)` (L46) and returns
   the plain continuation when nothing resolves (L47).
3. It reads the caller's headers for the log (`GetCallHeaders`, L55).
4. It starts a span on `Kronikol.Grpc` (L61) and adds `traceparent` (`InjectTraceParent`, L343-357):
   `var headers = context.Options.Headers ?? new Metadata(); headers.Add("traceparent", $"00-{trace}-{span}-00");`.

The Kronikol trace id is always a new GUID (L57). For the two streaming kinds and duplex, the response is logged as
`OK` with no content straight after the call starts (L165, L203, L241), and the span is disposed at return. The async
unary span is started without `using` (L61) and completed when the response arrives (L269).

**The HTTP handler** (`src/Kronikol/Tracking/TestTrackingMessageHandler.cs`) is the model the issue points to:

- It injects `traceparent` only when no `Activity` is current (L156-170).
- Through its accessor, it reads the four headers from the request the host is serving (L187-193).
- It takes that request's identity as the call's own when both headers are there (L199-202), and otherwise resolves
  one (L205-208), forwarding untracked when nothing resolves. A handler without an accessor skips L187-193 and
  resolves the middleware's scope (`TestInfoResolver.cs:113-114`).
- It reuses the inbound trace id as the call's own, with `Guid.Parse` (L211).
- It adds the four headers only when the request being served did not carry them (L213-223). So with an accessor it
  forwards nothing, and without one it stamps all four (P8, P8c).
- It snapshots the request headers for the log before adding them (L173), so diagrams never show them. A test pins
  that (`TestTrackingMessageHandlerTests.cs:1137-1157`).
- It has no option that turns the headers off. `HeadersToForward` (default `[]`) copies named headers from the inbound
  request (L325-339).

**The receiving side.** `TestTrackingContextMiddleware.InvokeAsync` (L25-40) begins `TestIdentityScope.Begin(name, id)`
for the request when both headers are present, and `TestInfoResolver.TryResolveFromHttpContext` (L161-185) reads the
same two headers through an accessor. Five places in core read the identity headers, and none of them decodes
anything:

- `TestTrackingContextMiddleware.cs:27-28`;
- `TestInfoResolver.cs:171-172`;
- `TestTrackingMessageHandler.cs:190-191`;
- `MessageTracker.cs:360-361`, which uses the header path only when name, id **and** trace id are all present (L364),
  then calls `Guid.Parse` with no guard (L370). Without the trace id it resolves through the shared chain (L378), which
  finds the middleware's scope;
- the public `TestTrackingServerBridge.GetCurrentTestInfo` (`TestTrackingServerBridge.cs:23-24`).

ProxyTap (`ProxyTap.cs:331`, `:338`) reads them too.

**The writers.** Three places write these headers today:

- the HTTP handler, with the raw name and id (L217, L220);
- ProxyTap (`ProxyTap.cs:356-363`);
- Playwright's `TestTrackingIdentity.ToHeaders()` (`TestTrackingIdentity.cs:80-82`).

The last two pass the name, the id and the caller name through a `HeaderSafe` that keeps printable ASCII, turns
everything else into `?` and stops at 512 characters (`ProxyTap.cs:366-377`; a public copy at
`TestTrackingIdentity.cs:101-112`).

**What decides a scenario.** Logs are matched to scenarios by `TestId` everywhere: `PlantUmlCreator.cs:103`,
`InternalFlowSegmentBuilder.cs:69`, `ComponentFlowSegmentBuilder.cs:86` and `ReportGenerator.cs:2402`. The name is shown
only by the diagnostic page (`DiagnosticReportGenerator.cs:143`) and used by `kronikol ingest` when nothing else names a
scenario (`FeatureSynthesizer.cs:145`). So an id must arrive byte for byte; a name only needs to arrive readably.

**Test ids by framework** (READ):

- 64 hex characters: xUnit v3 (`Kronikol.xUnit3/CurrentTestInfo.cs:19`);
- a GUID: xUnit v2, TUnit, ReqNRoll and LightBDD;
- `0-1001`: NUnit;
- `{FullyQualifiedClassName}.{Method}`: MSTest (`Kronikol.MSTest/CurrentTestInfo.cs:17`), which can hold any Unicode
  letter a C# identifier can.

Names are freer (READ):

- xUnit v3's argument formatter (xunit.v3.common 3.2.2) shortens a string argument longer than 50 characters with
  three U+00B7 (`new string((char)0xB7, 3)`), so an all-ASCII theory gets a non-ASCII name.
- The same formatter writes an argument whose `ToString()` holds a line feed as it is.
- NUnit, TUnit and LightBDD names routinely hold quotes and any letter.

**The diagnostic column.** `DiagnosticReportGenerator.cs` groups components by `ComponentName` (L188-191). Its accessor
cell (L206-211) prints:

- `✓ configured` when `instances[0].HasHttpContextAccessor`;
- `⚠ null` when any instance was invoked;
- `—` otherwise.

The registry is a process-wide `ConcurrentBag` (`TrackingComponentRegistry.cs:10-12`). Nothing in `src/` clears it;
its public `Clear()` (L31-38) is for test setup. So which instance is `[0]` is not defined.

`ITrackingComponent.HasHttpContextAccessor` is a `bool` defaulting to `false`, and its doc says it "Returns `null` when
the concept is not applicable" (`ITrackingComponent.cs:25-30`). Six classes hold an accessor and do not report it:

- four register with the report: `GrpcTrackingInterceptor`, `S3TrackingMessageHandler`, `ServiceBusTracker` and
  `TrackingSqliteConnection`;
- two never register: `SpannerTrackingInterceptor` and `TrackingSpannerConnection`. The `SpannerTracker` they create
  does register (`SpannerTracker.cs:24`) and reports its accessor (`:30`).

`CHANGELOG.md:7527` says every component implements the member.

**Spans.** `InternalFlowActivityListener` samples every source except `System.Net.Http` with `AllData` and ignores the
parent's flags (`InternalFlowActivityListener.cs:48-51`). So `00` never changes what Kronikol captures, and a span it
alone samples is not `Recorded`. Across the repo the wire flags differ:

- the gRPC interceptor and the HTTP handler write `-00`;
- Playwright writes `-01` (`TestTrackingIdentity.cs:93`);
- ProxyTap writes `-01` only when it synthesizes a `traceparent` (`ProxyTapOptions.cs:198`, `ProxyTap.cs:243-248`), and
  otherwise forwards its span's id or the inbound value.

### 2.2 The measurements

The probe ([harness](GRPC_IDENTITY_PROPAGATION_PLAN.harness/README.md)) starts three in-process hosts on `TestServer`:

```
test --HTTP--> host A --gRPC--> host B --HTTP--> stub C
```

Host A and host B call `AddTestTrackingContextPropagation()`, and host B calls C through Kronikol's HTTP handler with
its own accessor. `CaptureBackground` is on, so a call no scenario claims is listed as `unknown` rather than dropped. For
P10 it also starts one Kestrel host.

It references the published packages: 4.9.0, and 4.6.0 restored from nuget.org into an empty folder. A local pack of
4.6.0 in the user cache was stamped `4.5.1+7de6aea2` and was set aside.

| # | Run | B saw | B's call to C | Result file |
|---|---|---|---|---|
| P1 | A's client built with A's accessor (the issue's setup), 4.6.0 and 4.9.0 | no identity; one `traceparent`, flags `00` | `unknown`, source `None` | `p1-attribution-control-*.txt` |
| P1 | The issue's workaround (a second interceptor adding the two headers), 4.6.0 and 4.9.0 | `id-1`, `Scenario one` | **`id-1`, `RequestHeader`** | `p1-attribution-workaround-*.txt` |
| P6 | A's interceptor with no accessor and no fetcher, 4.6.0 and 4.9.0 | no identity | `unknown`; A's own call still resolves, from A's middleware scope (`Scope`) | `p1-attribution-noaccessor-*.txt` |
| P1b | No host A: the test calls B over gRPC (`CreateTestTrackingGrpcClient`'s shape), 4.6.0 and 4.9.0 | no identity | `unknown` | `p1b-attribution-direct-*.txt` |
| P8 | HTTP, not gRPC, between A and B, A's handler with A's accessor | no identity **and no `traceparent`** | `unknown` | `p8-attribution-httpchain-4.9.0.txt` |
| P8b | As P8, with `HeadersToForward` naming the two identity headers | `id-1` | `id-1`, `RequestHeader` | `p8b-attribution-httpchain-forward-4.9.0.txt` |
| P8c | As P8, A's handler built without an accessor | `id-1`, no `traceparent` | `id-1`, `RequestHeader`; A's own call resolves from `Scope` | `p8c-attribution-httpchain-noaccessor-4.9.0.txt` |

The edge cases, on 4.9.0, and on 4.6.0 where stated:

| # | What | Measured | Result file |
|---|---|---|---|
| P2 | One `Metadata` (one entry) reused for three calls | After calls 1, 2 and 3 it holds 1, 2 and 3 `traceparent` entries. B receives one `traceparent` whose value joins all of them, oldest first. From the second call on, B's server span leaves the caller's trace: `Hierarchical` format, trace id and parent span all zeros. Each call logs the stale entries as its own request headers (0, 1, 2). 64 concurrent calls on one `Metadata` left 65 entries and none threw. 4.6.0 the same | `p-metadata-*.txt` |
| P3 | `Metadata.Empty` passed as the call's headers | `InvalidOperationException: Object is read only`, thrown by the interceptor before the call is sent. 4.6.0 the same | `p-metadata-*.txt` |
| P4 | `Activity.Current` around two awaited unary calls | `null` before; `/hop.Hop/Call` (the interceptor's span) after the first and after the second; the second call's `traceparent` carries the first call's trace id, so it was started as the first span's child. 4.6.0 the same | `p-activity-control-*.txt` |
| P5 | The span's own flags against the wire | Kronikol alone: `Recorded=False`, flags `None`, wire `00` (4.6.0 the same). A listener that records (as an OpenTelemetry SDK does): span `Recorded`, wire still `00`. Under OpenTelemetry's default sampler, `ParentBased(AlwaysOn)`: span `Recorded`, wire `00`, and **B's server span is not recorded** | `p-activity-{control,recording,parentbased}-*.txt` |
| P7 | `HasHttpContextAccessor` of an interceptor built with an accessor | `False`. 4.6.0 the same | `p-accessor-*.txt` |
| P9 | A server stream that sends one reply, then fails `NotFound` | The client gets one message, then `RpcException NotFound`; the interceptor recorded the response as `OK` with no content | `p9-streaming-4.9.0.txt` |
| P10 | Five test names as identity headers, over Kestrel (HTTP/1.1, and h2c for gRPC) and in memory | See the table below | `p10-nonascii-4.9.0.txt` |

P10 sends the five names five ways:

- through the HTTP handler as it ships;
- as identity metadata the way the issue's workaround writes it (raw values);
- encoded by §4.1's rule and decoded where they arrive.

| Test name | HTTP handler, socket | Workaround metadata, h2c | HTTP handler, in memory | Workaround metadata, in memory | §4.1 encoded, socket and h2c |
|---|---|---|---|---|---|
| `Place an order` | ok | ok | ok | ok | ok, exact |
| `Café order` | `HttpRequestException`: request headers must contain only ASCII characters | `RpcException Internal`, same cause | ok, exact name | ok, exact name | ok, exact |
| `Order a coffee ☕` | as above | as above | ok | ok | ok, exact |
| `Theory(text: "aaa…a"···)` (xUnit v3's shortening, all-ASCII argument) | as above | as above | ok | ok | ok, exact |
| `Theory(a` + line feed + `b)` | `FormatException`: new-line characters are not allowed in header values | `RpcException Unavailable` | **`FormatException`** | ok | ok, exact |

**P11, a real consumer** (`p11-breakfastprovider-xunit-4.0.2.txt`). BreakfastProvider's xUnit lane, as its CI wrote it on
2026-10-05 (run #307, `106702b`, Kronikol 4.0.2), was read with `kronikol query` only. The gRPC package, the middleware
and the resolver are unchanged from v4.0.2 to v4.9.0. The tests call the API over gRPC through
`CreateTestTrackingGrpcClient` (`BreakfastProvider.Tests.Component.Shared/Common/Grpc/GrpcBreakfastSteps.cs:22`). The run
holds seven gRPC calls into the API, and none of them has a call under it:

| Scenario | Call | Recorded | What the handler does (`BreakfastGrpcService.cs`) |
|---|---|---|---|
| s49, "Order status via grpc should return order details" | `GetOrderStatus` | `OK`, 7 ms | reads the order from CosmosDB (L42) |
| s50, "Order status for non existent order should return not found" | `GetOrderStatus` | `NotFound`, 126 ms | reads CosmosDB (L42), then `NotFound` (L54) |
| s51 to s53, recipe summaries | `GetRecipeSummary` | `OK` | returns hard-coded data and calls nothing (L10-35) |
| s54, "Streaming order updates should return the current status" | `StreamOrderUpdates` | `OK`, 0 ms | reads CosmosDB (L65), then streams |
| s55, "Streaming updates for non existent order should return not found" | `StreamOrderUpdates` | **`OK`**, 0 ms | reads CosmosDB (L65), then **`NotFound`** (L77) |

So four scenarios miss a CosmosDB read. The run records 34 CosmosDB `Read /orders` calls in other scenarios, so the
read is tracked and these four were dropped for want of an identity. s55 is P9 on a real suite: its test expects
`NotFound` and passes, and the report says `OK`. The five scenarios whose names mention gRPC but call no gRPC method of
the API (s170 to s174, contract and reflection endpoints) make no downstream call. Two of them, s172 and s173, are duplex
reflection streams recorded `OK` in 0 ms and 2 ms.

## 3. Findings the issue does not state

**F1. Every gRPC-fronted service, not only multi-host suites** (RUN, P1b and P11). The test calling one service over
gRPC is the most common shape the gRPC package serves, and that service's own downstream calls are unattributed. In the
owner's BreakfastProvider, four scenarios miss the CosmosDB read behind their gRPC call. The wiki documents the opposite
direction only (`Integration-Grpc-Extension.md:94`, "SUT → downstream gRPC").

**F2. The interceptor writes into the caller's `Metadata`** (RUN, P2; READ, `InjectTraceParent` L352-353). A caller
that keeps one `Metadata` for its calls, to carry an auth header for instance, gains one more `traceparent` entry on
each call.

- The receiving host gets them as one header whose value joins them all, oldest first.
- From the second call on, ASP.NET Core cannot parse it, so the receiving host's span starts outside the caller's
  trace, with an all-zero trace id. That breaks the internal-flow view and `kronikol query trace` for every call after
  the first.
- Each call also logs the stale entries as its own request headers.
- `Metadata` is an unsynchronised list, so concurrent calls on one object race (INFERRED; 64 concurrent calls lost no
  entry in P2).

Identity headers added the same way would pile up too.

**F3. `Metadata.Empty` throws** (RUN, P3). `Metadata.Empty` is frozen, so a call that works without Kronikol fails
with it.

**F4. The async unary call leaks its span into the caller** (RUN, P4). `AsyncUnaryCall` is synchronous and starts the
span without restoring `Activity.Current`, so the caller's flow keeps the span after the call.

- A later gRPC call is started as its child: two sequential calls share a trace and nest (RUN).
- A later HTTP call through Kronikol's handler finds a span current. So it records that span's ids as its own and
  adds no `traceparent` (L156-170), and on `TestServer` the request then carries none (INFERRED from the code and P8).

The blocking and streaming kinds dispose their span at return and do not leak.

**F5. The trace flags say `00` even when the span is recorded** (RUN, P5). Kronikol's own capture is unaffected, since
it samples everything. But a suite that runs an OpenTelemetry SDK with its default `ParentBased` sampler sees the
receiving host drop its server span for every call the interceptor makes. That takes in the
`Kronikol.Extensions.OpenTelemetry` exporter path (`OpenTelemetryTrackingExtensions.cs:22-24`).

**F6. Identity header values fail calls** (RUN, P10; READ). The HTTP handler writes the raw name and id.

- Over a real socket, any character above U+007F fails the request.
- A line feed fails it even in memory, since `Headers.Add` validates.
- xUnit v3 puts non-ASCII into an all-ASCII theory's name whenever it shortens an argument, and MSTest's id is a method
  name.
- A name with a leading or trailing space reaches a real server without it: RFC 9110 field values exclude surrounding
  whitespace (INFERRED).

In-process suites never meet the socket half, which is why it went unseen.

`?` replacement, ProxyTap's and Playwright's answer, is lossy:

- it breaks an MSTest id that holds a non-ASCII letter;
- it can merge two ids (`Café` and `Cafè` both become `Caf?`);
- it would cost in-memory suites the exact name they see today.

**F7. The accessor column is wrong three ways** (READ, RUN P7):

- it reads one undefined instance per group;
- it prints a literal `null`;
- four components that register with the report never report an accessor they hold.

A multi-host suite has test-side clients without an accessor and host-side ones with one, so the cell reads `✓` or
`⚠` depending on registration order. #137 section 2 reports the instance half.

**F8. HTTP does not re-stamp the identity on a hop when its handler has an accessor** (RUN, P8, P8b and P8c; READ). With
an accessor, which the DI filter always provides, the handler sees the identity on the request being served and adds
none to its own call. A test pins this as deliberate, and `HeadersToForward` is the opt-in that fixes it. Without an
accessor, the handler resolves the middleware's scope and stamps all four headers.

`Tracking-Dependencies.md:13` says the handler "Forwards tracking headers downstream so multi-hop calls are correlated",
and `Integration-Playwright.md:81` says "its outbound handlers re-stamp all four". Both are true only of a handler built
without an accessor. Kronikol4J's HTTP clients do re-stamp on every hop (`KronikolOkHttpInterceptor.java:76-91` and
three siblings). Inside a host on `TestServer` the hop carries no `traceparent` either way, because the handler leaves
that to a `DiagnosticsHandler` that `TestServer` does not have (L147-153; §8).

**F9. A foreign trace id throws** (READ). `Guid.Parse` on the inbound `test-tracking-trace-id` in the HTTP handler
(L211) and in `MessageTracker` (L370) fails the application's own call when another tool sends a non-GUID value;
ProxyTap uses `Guid.TryParse` (`ProxyTap.cs:204`). Also, `MessageTracker` takes the header path only when name, id and
trace id are all present (L364). Without the trace id it still attributes through the middleware's scope, with source
`Scope` and a new trace id.

**F10. gRPC never carries the Kronikol trace id** (READ). The interceptor makes a new GUID for every call (L57) and
reads no inbound one. The HTTP handler reuses the inbound id (L211), which is what lets `kronikol query flow` nest a
host's calls under the call into it when the two sides name each other differently (`CallNesting.cs:62-75`, the
second rule).

**F11. Streaming calls are recorded before they run** (RUN, P9 and P11; READ L165, L203, L241). Server streaming,
client streaming and duplex calls log their response as `OK`, with no content, as soon as the call starts, and their
span covers only the start. A stream that fails reads as a success in the report and in `kronikol query failures`.
BreakfastProvider's s55 is one.

**F12. Stale docs** (READ).

- `TestTrackingContextMiddleware.cs:12` tells users to register `app.UseTestTrackingContext()`, which does not exist.
- `ITrackingComponent.cs:28` promises `null` from a `bool`.
- `GrpcTrackingOptions.CurrentStepTypeFetcher` (L22) is read nowhere.
- `Integration-Grpc-Extension.md:374` says unresolved calls are "logged with "Unknown" test identity". The interceptor
  logs nothing unless `CaptureBackground` is on (L46-48).

## 4. The design

### 4.1 One encoding for identity values (R0)

A new internal static class in core, `TrackingHeaderValue` (`src/Kronikol/Tracking/`), with two methods:

- **`Encode(string value)`** returns the value unchanged when three things hold: every character is printable ASCII
  (U+0020 to U+007E), the value neither begins nor ends with a space, and it does not begin with `UTF-8''`. Otherwise
  it returns `UTF-8''` followed by `Uri.EscapeDataString(value)` (UTF-8, percent-encoded). That is RFC 8187's
  `ext-value` form, the one `Content-Disposition: filename*=` uses, and its output is always printable ASCII with no
  space.
- **`Decode(string value)`** returns `Uri.UnescapeDataString` of the rest when the value begins with `UTF-8''`, and the
  value itself when it does not.

The rule is prototyped in the harness (`escape/esc.cs`) and gives the same answers on .NET 8.0.27, 9.0.16 and 10.0.11
(RUN):

- every value tried round-trips exactly, from the P10 names to tabs, surrounding spaces, an MSTest id, a literal
  `UTF-8''`, the empty string and 70,000 non-ASCII characters;
- ASCII names, GUIDs and the empty string come out unchanged;
- a malformed escape is left as written, and nothing throws;
- the one lossy case is a lone surrogate, which is not valid UTF-16, written as U+FFFD. No framework's name or id
  holds one.

Over real sockets, P10's encoded legs carry all five names over HTTP/1.1 and h2c and decode them exactly (RUN).

The writers encode the name, the id and the caller name; the trace id is a GUID and needs nothing:

- the HTTP handler (L217, L220, L223);
- ProxyTap's re-stamp (`ProxyTap.cs:357-361`, in place of `HeaderSafe`);
- Playwright's `ToHeaders()` (`TestTrackingIdentity.cs:80-82`, in place of `HeaderSafe`; the public `HeaderSafe` stays);
- in R1, the gRPC interceptor.

Every reader decodes. That is the five readers in core (§2.1) and ProxyTap's. Core already lets
`Kronikol.Extensions.Grpc` see its internals (`Kronikol.csproj:19`); R0 adds the same line for
`Kronikol.Extensions.ProxyTap` and `Kronikol.Playwright`, so no public API is added.

What it changes on the wire:

- **Nothing for an ASCII value without surrounding spaces,** which is every id but MSTest's non-ASCII ones and most
  names. Every existing header pin holds.
- **Any other value** goes as `UTF-8''…`. Before, a non-ASCII value failed the call over a socket and a line feed
  failed it everywhere. In memory a non-ASCII value arrived raw; now every Kronikol reader in the same process decodes
  it, so in-memory suites see the exact name they see today.

Why not the alternatives:

- **`?` replacement (`HeaderSafe`)** is lossy (F6).
- **gRPC's `-bin` keys** would fix gRPC alone, and the middleware reads the text keys. One scheme for both transports is
  simpler.

**No length cap.** ProxyTap and Playwright cap every value at 512 characters, but a cap is lossy and an id must never
be cut. Kestrel limits a request's headers to 32 KB in all (`MaxRequestHeadersTotalSize`) and, over HTTP/2, one field
to 16 KB (`Http2Limits.MaxRequestHeaderFieldSize`). Encoding triples a non-ASCII byte, so a name would need a few
thousand non-ASCII characters to reach the field limit. xUnit v3 shortens each argument to 50 characters, and nothing
in the corpus comes near (INFERRED; Q10).

### 4.2 The metadata the interceptor sends (R0)

The interceptor never writes into the caller's `Metadata`. It builds a new `Metadata` per call, copies the caller's
entries into it, and adds its own (F2, F3).

**`traceparent`.** It is added only when the copy has none, as the HTTP handler does (L165). Its flags come from the
span: `01` when `Activity.Current.Recorded`, `00` otherwise, and `00` for the random-id fallback when there is no span
(F5). By default nothing changes, because Kronikol's listener samples `AllData` and its spans are not `Recorded` (P5).
The existing fact pinning `-00` (`GrpcTrackingInterceptorTests.cs:674-754`) stays true. The trace id and span id stay
exactly as today; `SPAN_ATTRIBUTION_PLAN.md` relies on them.

**The span is given back.** `AsyncUnaryCall` saves `Activity.Current` before starting the span, invokes the
continuation under the span (so the HTTP layer underneath is its child), and restores the saved value before it
returns. The span still ends when the response arrives (L269) (F4). The other four kinds already dispose at return.

### 4.3 Identity in the metadata (R1)

A new option, in the house pattern (`KafkaTrackingOptions.cs:35-41` and four siblings):

```csharp
/// <summary>
/// When <c>true</c>, the interceptor puts the test identity into each call's metadata, and a host that calls
/// <c>AddTestTrackingContextPropagation()</c> establishes a <see cref="TestIdentityScope"/> from it, so that the
/// called host's own tracking is attributed to the originating test. Defaults to <c>true</c>.
/// </summary>
public bool PropagateTestIdentity { get; set; } = true;
```

The rule, for all five call kinds:

1. **Resolve first, then decide.** The interceptor resolves the identity before the phase check, so a call that is not
   drawn in the current phase still carries its scenario to the next host. The HTTP handler stamps such a call too, and
   marks only the log as ignored (L233-234).
2. **Only an identity that names a scenario travels.** When the resolved identity `IsAttributed`, the four headers are
   added. The background identity (`None`, `Detached`) is not sent: the receiving host then resolves its own chain to
   the same background id, with the true source. The issue's workaround does the same. The HTTP handler and the
   messaging producers send `Unknown`/`unknown` instead (§8, Q3).
3. **Every hop re-propagates.** The identity is whatever the interceptor resolves, including one that arrived with the
   request the host is serving (`RequestHeader`) or that the host's middleware put in scope (`Scope`). Host B's
   interceptor forwards to host C what host A sent to host B. This is where gRPC deliberately differs from the HTTP
   handler with an accessor (F8, Q1).
4. **The four headers, each only when the caller's metadata lacks it,** so a workaround interceptor like the issue's
   keeps working and is not duplicated:

   | Key | Value |
   |---|---|
   | `test-tracking-current-test-name` | `TrackingHeaderValue.Encode(identity.Name)` |
   | `test-tracking-current-test-id` | `TrackingHeaderValue.Encode(identity.Id)` |
   | `test-tracking-trace-id` | the call's Kronikol trace id: the inbound one when the request being served carries a GUID there (F10, parity with L211), else a new GUID. It is also the `TraceId` the call is logged with |
   | `test-tracking-caller-name` | `TrackingHeaderValue.Encode(options.CallerName)`. No reader uses its value; it is sent so that a gRPC hop and an HTTP hop look the same to the next host |

5. **The log is unchanged.** The headers are read for the log before any are added (L55), as the HTTP handler does, so
   diagrams gain no header lines.

`false` restores today's metadata exactly (copied, per R0), with no identity keys.

**Nothing changes on the receiving side.** The middleware and `TestInfoResolver` already read these keys from an
`application/grpc` request (C2, C5); R0 has them decode.

### 4.4 `HasHttpContextAccessor` and the column

- **R1** adds `public bool HasHttpContextAccessor => _httpContextAccessor is not null;` to the four components that
  register without it: `GrpcTrackingInterceptor`, `S3TrackingMessageHandler`, `ServiceBusTracker` and
  `TrackingSqliteConnection`. The gRPC interceptor reports the accessor it resolves with: the constructor's, else
  `options.HttpContextAccessor` (L27). The two Spanner classes never appear in the report and are left as they are
  (§8). `CHANGELOG.md:7527` claimed every component implemented the member.
- **R0** makes the column count, as the Active column beside it does (`DiagnosticReportGenerator.cs:199-204`):
  - `✓ M of M` when every instance has one;
  - `⚠ 0 of M` when none has one and one was invoked;
  - `N of M`, plain, when some have one, since test-side clients without one are normal;
  - `—` when none has one and none was invoked.

  The literal `null` goes, the per-instance table of a group of several gains an accessor cell, and the
  `ITrackingComponent` doc says what `false` means. #137 section 2 is this half: its counting ships in R0, and its gRPC
  row reads right once R1 adds the member. Section 1 of #137 is not in this plan.

### 4.5 Streaming outcomes (R2)

The response of a streaming call is logged when the stream ends, not when it starts:

- **Server streaming and duplex:** the response stream is wrapped in a reader that logs `OK` when `MoveNext` returns
  `false`, and the mapped status with `"{code}: {message}"` when it throws `RpcException`, as the unary path does
  (L261-265).
- **Client streaming:** `ResponseAsync` is wrapped as the unary path wraps it.
- **A call disposed before its stream ends** logs `Cancelled`, so no request is left unpaired.
- The span ends with the outcome.

Content stays `null`. Recording the messages of a stream is a separate question (Q6).

### 4.6 What each output shows after the fix

- **Sequence diagrams and the report.** After R1, B's own calls appear in the scenario, under the gRPC arrow into B,
  where they were missing or in the background list. No header line is added to any note.
- **`kronikol query flow`.** It nests B's calls under the call into B even when A's `ServiceName` for B and B's
  `CallerName` differ (the shared trace id, F10).
- **The diagnostic page.** It reads `✓ 2 of 2` or `1 of 2` for the interceptor, not `⚠ null`.
- **Kestrel suites.** A non-ASCII test name no longer fails the call (R0).
- **Streaming calls.** A failing stream reads as failed (R2).

### 4.7 What stays as it is

- The header names (`ROADMAP.md` 12b.3: "no new names") and the `traceparent` trace and span ids.
- The HTTP handler's hop rule, unless Q1 says otherwise.
- The HTTP handler's and the messaging producers' stamping of the background identity (Q3).
- The receiving middleware.
- The logged headers.

## 5. Tests, red first

Unit facts go in `tests/Kronikol.Tests.Grpc` (xUnit v3), with the fake continuations already there
(`GrpcTrackingInterceptorTests.cs:36-70`), capturing the `ClientInterceptorContext` the continuation receives.

The two-host facts are new. The repo hosts no gRPC server anywhere today. `tests/Kronikol.Tests.Grpc` gains
`Grpc.AspNetCore`, pinned at or above what the package's `Grpc.Net.Client 2.*` resolves to (2.84.0 today, or restore
fails with NU1605, #97), and a `Protos/hop.proto` with `GrpcServices="Both"`. Hosts are `WebApplication` with
`UseTestServer`, like the probe.

Each fact is proved red on 4.9.0 in a worktree at `v4.9.0`, with compile stubs for new members, and its failure message
read. A fact that passes there is called a guard in the log.

**R0**

| # | Fact | Red on 4.9.0 because |
|---|---|---|
| T1 | A `Metadata` reused for three calls still holds its one entry afterwards, and call 3 sends exactly one `traceparent`, which B's server span parses into the caller's trace | P2 |
| T2 | `Metadata.Empty` as the headers: the call is sent and carries a `traceparent` | P3 |
| T3 | A caller's own `traceparent` is sent unchanged and no second one is added | it adds a second |
| T4 | After an awaited `AsyncUnaryCall`, `Activity.Current` is what it was before | P4 |
| T5 | Two sequential calls with no ambient span get two trace ids | P4 |
| T6 | Under a listener that records, the wire flags are `01`; under Kronikol's alone, `00` (the existing regex fact stays) | `00` always |
| T7 | `TrackingHeaderValue` round-trips every value in `escape/esc.cs` and leaves ASCII values without surrounding spaces byte-identical. It writes a lone surrogate as U+FFFD, and its output is always printable ASCII with no space | new type (stub) |
| T8 | HTTP handler: the name `Theory(a` + line feed + `b)` in memory, the call succeeds and host B's middleware scope holds the exact name | P10, `FormatException` |
| T9 | HTTP handler over Kestrel: `Café order`, the xUnit `···` name and ` Place an order ` succeed, and B resolves the exact name and id | P10 |
| T10 | Each reader decodes: middleware, `TestInfoResolver`, the handler's inbound path, `MessageTracker`, `TestTrackingServerBridge`, ProxyTap (one fact each) | they read raw |
| T11 | ProxyTap and Playwright `ToHeaders()` write encoded values (ASCII unchanged) | `?` |
| T12 | An inbound `test-tracking-trace-id` of `abc`: the HTTP handler's call succeeds with a new trace id; `MessageTracker` falls back without throwing | `FormatException` |
| T13 | The column: 3 instances, 2 with an accessor, invoked, reads `2 of 3`; all, `✓ 3 of 3`; none and invoked, `⚠ 0 of 3`; none and idle, `—`; the page never contains `⚠ null` | `instances[0]`, literal `null` |
| T14 | The same instances registered in reverse order give the same cell | order-dependent |
| T15 | Playwright: `DiagnosticReport.html` for a run with two `TestTrackingMessageHandler`s of one caller, one with an accessor and one without, both invoked, paints `1 of 2` in that row's HttpContextAccessor column | it paints `✓ configured` or `⚠ null` by registration order |

**R1**

| # | Fact | Red on R0 because |
|---|---|---|
| T20 | Theory over the five call kinds: with an attributed identity, the outgoing metadata carries the four keys with the encoded name and id, a GUID trace id and the caller name | no keys |
| T21 | The identity from the request the host is serving (accessor with the headers) is sent on: a hop re-propagates | no keys |
| T22 | The identity from a `TestIdentityScope` (no accessor, no fetcher) is sent | no keys |
| T23 | `PropagateTestIdentity = false`: none of the four keys, `traceparent` present | option missing (stub) |
| T24 | Guards, green on R0 too: the background identity (`CaptureBackground` on, nothing resolves) and a detached flow send no identity keys | guard |
| T25 | Guard: a caller's own `test-tracking-current-test-id` is kept, not duplicated | guard |
| T26 | The call's logged `TraceId` equals the inbound `test-tracking-trace-id`, and that value is the one sent | new GUID |
| T27 | Guard: the logged request headers hold none of the four keys | guard |
| T28 | `TrackDuringAction = false` in the action phase: nothing is logged, and the identity is still sent | early return |
| T29 | `HasHttpContextAccessor` is `true` with an accessor and `false` without, for the four components (theory; the gRPC interceptor with the accessor from its constructor and from its options) | interface default |
| T30 | Two hosts: the test calls B over gRPC through `CreateTestTrackingGrpcClient`'s shape; B's call to C is the scenario's, `RequestHeader` | P1b |
| T31 | Three hosts: test, A (HTTP), B (gRPC from A through `AddTrackedGrpcClient`), C: B's call to C is the scenario's | P1 |
| T32 | Four hosts, two gRPC hops (B to C over gRPC, C to a stub): C's call is the scenario's | P1 |
| T33 | Over Kestrel h2c with the name `Café order`: the call succeeds and B's call is attributed with the exact name | P10 |
| T34 | Guard: `PropagateTestIdentity = false` end to end leaves B's call `unknown` | guard |
| T35 | B's call to C carries the same Kronikol trace id as A's call to B, so `CallNesting.Parents` nests it when the names differ | different ids |
| T36 | Playwright: a run with a host-side `GrpcTrackingInterceptor` (accessor) and a test-side one (none), both invoked, paints `1 of 2` in the gRPC row | `⚠ 0 of 2` on R0 |

**R2**

| # | Fact | Red on R1 because |
|---|---|---|
| T40 | A server stream that sends one reply and fails `NotFound`: the response is logged once, with 404 and `NotFound: …` | `OK` at start |
| T41 | A completed server stream logs `OK` after its last message (the response timestamp is after the last read) | logged at start |
| T42 | A client stream and a duplex stream: outcome at the end, as T40 and T41 | logged at start |
| T43 | A call disposed mid-stream logs `Cancelled` once, and a second dispose logs nothing | `OK` at start |

**Mutations** (each must turn a named fact red):

- write into the caller's `Metadata` again (T1);
- drop the `Activity.Current` restore (T4);
- hard-code `00` (T6);
- `?` in place of the encoding (T7, T9);
- leave surrounding spaces unencoded (T7, T9);
- skip decoding in one reader at a time (T10);
- read `instances[0]` (T14);
- propagate only when the phase is tracked (T28);
- add the identity only when the inbound request lacks it, the HTTP rule (T21, T32);
- send the background identity (T24);
- a new GUID for the trace id (T26, T35);
- log a stream at start (T40).

## 6. Slices, releases and records

### 6.1 Slices

| Slice | Content | Release |
|---|---|---|
| S0 | This plan and its harness; a `PLANS_STATUS.md` row | none (no bump) |
| S1 | `TrackingHeaderValue`, the writers and readers in core, ProxyTap and Playwright, the two IVT lines (§4.1); T7 to T12 | R0 |
| S2 | The interceptor's metadata, flags and span (§4.2); T1 to T6 | R0 |
| S3 | The diagnostic column and the three doc-comment fixes (§4.4, F12); T13 to T15 | R0 |
| S4 | `PropagateTestIdentity`, the four keys, the inbound trace id, the phase rule (§4.3); T20 to T28 | R1 |
| S5 | `HasHttpContextAccessor` on four components (§4.4); T29 and T36 | R1 |
| S6 | The two-host test infrastructure and T30 to T35 | R1 |
| S7 | Streaming outcomes (§4.5); T40 to T43 | R2 |
| S8 | Acceptance: the harness and BreakfastProvider on local packages of each release, with the previous release as the control (§6.6) | each |

### 6.2 Releases

Version numbers are claimed with a "taking X now" message when each release is ready. Other plans written on
2026-10-07 also expect releases, and 4.9.1 has shipped since this plan's base.

- **R0, a patch.** Bug fixes with no new public member: one internal class and two `InternalsVisibleTo` lines. The wire
  form of a non-ASCII identity value changes. It used to fail the call over a socket, and every Kronikol reader decodes
  it, so the changelog calls it out rather than bumping for it.
- **R1, a minor.** A new option (`GrpcTrackingOptions.PropagateTestIdentity`) and four new public members. Its default
  is on, so a suite that upgrades sees its gRPC services' own calls appear in their scenarios. This is the precedent of
  2.34.0, a minor that turned identity propagation on for five messaging packages (`CHANGELOG.md:6290-6293`). No
  existing option's default changes, so it is not major (Q4).
- **R2, a patch.** The recorded outcome of a streaming call changes; nothing is added.
- **R3, a patch, added when the owner settled Q1 as recommended.** The HTTP handler puts the identity of the request it
  is serving on every request it sends, whether or not it holds an accessor, and adds each header only when the request
  lacks it. It also takes §8's first finding: it sends the current span's `traceparent` when its transport has no
  `DiagnosticsHandler` (TestServer's in-memory handler), so an HTTP hop between in-process hosts stays in the trace.
  Both change what a host sends downstream, and neither adds anything to call.

R0 and R1 can ship the same day; R1 needs R0's encoding.

### 6.3 Changelog drafts

**R0 (patch).** Test names and ids no longer fail the calls that carry them, and the gRPC interceptor leaves the
caller's metadata and trace alone (#134, plans/GRPC_IDENTITY_PROPAGATION_PLAN.md R0).

- **Identity values.** A test name with a non-ASCII character made every request through `TestTrackingMessageHandler`
  fail over a real socket ("Request headers must contain only ASCII characters"); a name with a line feed failed it in
  memory too. That covers xUnit v3 theories whose long string arguments are shortened with `···`, and MSTest ids naming
  a method with such a letter. Such values, and values with surrounding spaces, now travel as an RFC 8187 `UTF-8''`
  value, and every Kronikol reader decodes them; other values are sent exactly as before. ProxyTap and Playwright's
  `ToHeaders()` send the same form in place of `?`.
- **The gRPC interceptor and the caller's objects.**
  - It no longer adds `traceparent` to the caller's own `Metadata`. A reused `Metadata` no longer sends a joined value
    that put every call after the first outside its trace, and `Metadata.Empty` no longer throws.
  - It keeps a `traceparent` the caller set.
  - Its flags say `01` when the call's span is recorded, so a host sampling with OpenTelemetry's default keeps its span.
  - An awaited unary call no longer leaves its span as `Activity.Current`, so the next call no longer joins its trace.
- **Trace ids.** A `test-tracking-trace-id` that is not a GUID no longer fails the call that receives it.
- **The diagnostic page.** The HttpContextAccessor column counts the instances that have one (`1 of 2`), where it read
  one arbitrary instance and printed `⚠ null`.
- **Doc comments.** `ITrackingComponent.HasHttpContextAccessor`, `TestTrackingContextMiddleware` (it named a method that
  does not exist) and `GrpcTrackingOptions.CurrentStepTypeFetcher` (no effect) say what is true.

The part that moved is the patch: nothing new to call.

**R1 (minor).** A host called over gRPC attributes its own calls to the scenario (#134,
plans/GRPC_IDENTITY_PROPAGATION_PLAN.md R1).

- `GrpcTrackingInterceptor` puts the test's identity into each call's metadata, in the four headers the HTTP handler
  uses, so a host that calls `AddTestTrackingContextPropagation()` attributes its database, HTTP and messaging calls to
  the scenario. It forwards on every hop: a host that received the identity passes it on to the next.
- `GrpcTrackingOptions.PropagateTestIdentity` (default `true`) turns it off.
- The interceptor reuses an inbound trace id, so `kronikol query flow` nests the called host's calls under the gRPC
  call.
- `HasHttpContextAccessor` is implemented on `GrpcTrackingInterceptor`, `S3TrackingMessageHandler`, `ServiceBusTracker`
  and `TrackingSqliteConnection`.

The part that moved is the minor: a new option and new public members. Behaviour change: a suite that tests a service
over gRPC sees that service's own calls in its scenarios, where they were missing or listed as background.

**R2 (patch).** A streaming gRPC call's outcome is recorded when its stream ends: a stream that fails reads as failed,
and its span covers the stream. The part that moved is the patch.

### 6.4 Wiki

Pull `C:\Code\Kronikol.wiki` first; it is ten commits behind its origin. Pages, by release:

- **R0:**
  - `HTTP-Tracking-Setup.md`: header values are encoded; how to read them by hand with `TestTrackingServerBridge`.
  - `Diagnostics-and-Debugging.md`: the column reads `N of M`.
  - `Integration-Playwright.md`: `ToHeaders()` encodes.
  - `Integration-ProxyTap-Extension.md`: the re-stamp encodes.
- **R1:**
  - `Integration-Grpc-Extension.md` gains a section "A host called over gRPC". It covers the four headers, every-hop
    forwarding, `PropagateTestIdentity`, the receiving host's `AddTestTrackingContextPropagation()` and the
    `CreateTestTrackingGrpcClient` case. The page's options table and its `traceparent` bullet (`:448`) are updated,
    and `:374` is fixed.
  - `Background-Thread-Correlation.md`: gRPC carries the headers too.
  - `Multi-Host-Test-Architectures.md`: gRPC between hosts, and for HTTP hops, `HeadersToForward` or a handler without
    an accessor, unless Q1 changes it.
  - `API-Reference.md` (`:226-243`): the new option.
- **R1, whatever Q1 decides:** `Tracking-Dependencies.md:13` and `Integration-Playwright.md:81` say which handlers
  re-stamp.
- **R2:** `Integration-Grpc-Extension.md`: how streaming calls are recorded.

### 6.5 Doc comments

- `ITrackingComponent.HasHttpContextAccessor` (R0).
- `TestTrackingContextMiddleware` (R0).
- `TestTrackingServerBridge` (R0: values are decoded).
- `GrpcTrackingOptions.CurrentStepTypeFetcher` (R0: "has no effect"; its removal goes to `V5_PLAN.md`'s open
  questions, as the five `ComponentDiagramOptions` did in 4.5.1).
- `GrpcTrackingOptions.PropagateTestIdentity` (R1).
- Two summaries describe one direction of identity only. `GrpcTrackingChannel`'s says "the test-to-SUT (incoming)
  direction", and `GrpcServiceCollectionExtensions`'s says "test identity flows through without manual wiring". Each
  gains what the called host now receives (R1).

Grep the shipped XML docs for "traceparent", "Unknown" and "identity" in the gRPC package before each tag.

### 6.6 Kronikol4J, consumers and acceptance

**Kronikol4J.** One ledger entry per release in `C:\Code\Kronikol4J\docs\REMAINING_PARITY.md`, at the end of the
divergence ledger:

- **R1:** "Not mirrored yet, worth porting". `KronikolClientInterceptor.java:63-65` sends only `traceparent`, the same
  gap.
- **R0:** the `UTF-8''` form, which the port's `KronikolServletFilter` should decode. Its HTTP clients send raw values,
  which OkHttp also refuses outside ASCII.

**The harness,** on each release's local packages, with 4.9.0 as the control (the same check must fail there):

- **R0:** P2, P3, P4 and P5 flip, and so do P10's HTTP-handler cells. P10's metadata cells measure the issue's
  workaround interceptor, which writes raw values, so they do not move.
- **R1:** P1, P1b and P6 flip; P8 does not, unless Q1. R1's acceptance adds a `nonascii` variant without the workaround
  that reads B's call from the log, attribution and decoded name both, which fails on 4.9.0 by sending no identity at
  all.
- **R2:** P9 flips.

**BreakfastProvider**, in a scratch clone (never `C:/Code/BreakfastProvider`), on each release's local packages, with
the previous release as the control:

- **R0:** its xUnit and ReqNRoll lanes pass, and every scenario draws the same calls as on the control. Its names are
  ASCII, so this shows R0 left every ASCII header byte-identical.
- **R1:** s49, s50, s54 and s55 each gain the CosmosDB read under their gRPC call (P11), and the same check fails on
  the control. Any other scenario whose call set changes is listed and explained before the tag. The API's gRPC call
  to the Notification Service fake now carries the identity, and the fake makes no tracked call, so nothing is
  expected there.
- **R2:** s54's and s55's streams, and s172's and s173's reflection streams, are recorded when they end. s55 reads
  `NotFound`.

### 6.7 Before declaring done

Run `plan-execution-audit-checklist`. In particular:

- every writer and reader of the changed header format (§4.1, the list in Appendix A);
- `release.slnf` built in Release for every target;
- the red runs on the previous tag, with each failure message read;
- the mutation list;
- `gh issue close 134` after R1;
- a comment on #137 when R1 ships: its section 2's counting shipped in R0 and its gRPC row in R1, and its section 1
  is open;
- the `PLANS_STATUS.md` row, and `ROADMAP.md` Appendix C for leftovers.

## 7. Where it sits in the roadmap

Not placed. At `417c8e58`, `ROADMAP.md` lists neither #134 nor its siblings (#106 is missing from §7 too). Sessions
planning issues from the same batch are placing them as they go: #135's plan took row 1.21 and D37 on 2026-10-07,
uncommitted. So a row is claimed with "taking 1.N now" when the owner places this plan. The natural place is stage 1,
beside the other defects users hit in a first integration. No decision number is needed unless Q1 or Q4 is decided
against the recommendation.

## 8. Found on the way, not in this plan

- **The HTTP handler sends no `traceparent` from inside a host on `TestServer`** (RUN, P8 and P8c). It leaves the header
  to a framework `DiagnosticsHandler` whenever an `Activity` is current (L147-153), and `TestServer`'s client has none.
  So an HTTP hop between in-process hosts starts a new trace, and `kronikol query trace` and the internal-flow view
  break at it. The comment's concern, Application Insights correlation on real sockets, needs its own measurement. This
  wants its own issue.
- **The background identity is sent as if it were a scenario's** (READ). The HTTP handler (L205-220) and the messaging
  producers (`TrackingKafkaProducer.cs:108-113` and siblings) stamp `Unknown`/`unknown` when `CaptureBackground` is on.
  The receiving host then records background work with source `RequestHeader`. The id is right and the source is
  wrong. R1 does not copy it (Q3).
- **The two Spanner `ITrackingComponent`s never register** (READ). `SpannerTrackingInterceptor` and
  `TrackingSpannerConnection` implement the interface but never call `TrackingComponentRegistry.Register`. The
  `SpannerTracker` they create registers and reports its accessor, so the report is right. Their interface
  implementation is dead weight, but nothing reads it.
- **#133:** xUnit v2's fetcher answers a fresh GUID outside a test. Every identity the gRPC interceptor sends from such
  a flow would be that orphan, as the HTTP handler's already is. Only #133's fix removes it; this plan neither depends
  on it nor works around it.
- **Siblings planned elsewhere:**
  - #137 section 1, markers listed as unpaired requests;
  - #138, `InternalFlowActivitySources` and the listener's unused `additionalActivitySources`
    (`InternalFlowActivityListener.cs:44`);
  - #136, MongoDB's options take no accessor; its own session plans it and leaves the column to this plan;
  - #135, Kafka trace ids; its session formats `traceparent` the way §4.2 does.
- **#97.** The package floats `Grpc.Net.Client 2.*`, so 4.6.0 and 4.9.0 both require 2.84.0 or later, and the
  issue's 2.76.0 cannot restore beside them (RUN, `nu1605-*.txt`). T30 to T35's `Grpc.AspNetCore` pin depends on it.
- **MSTest gives every `[DataRow]` of a method the same id** (READ, `Kronikol.MSTest/CurrentTestInfo.cs:17`), so
  their calls share one scenario. That is outside this plan; it wants an issue.

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Should the HTTP handler re-stamp the identity on every hop, as gRPC will after R1, as Kronikol4J does, and as it already does without an accessor? With an accessor (the DI default) it is opt-in through `HeadersToForward`, pinned by a test (P8, P8b, P8c) | **Yes, as its own patch release after R1.** It flips a pinned fact and changes what every host sends downstream, so it is the owner's call, not this plan's. Until then the wiki says `HeadersToForward` is how |
| Q2 | Encode non-ASCII values (`UTF-8''`), or replace them with `?` as ProxyTap and Playwright do? | **Encode.** `?` breaks MSTest ids, can merge ids, and costs in-memory suites the exact name (F6) |
| Q3 | Send the background identity over gRPC, as HTTP and the messaging producers do? | **No.** The receiving host resolves the same id with the true source. Whether HTTP and the producers should stop is a separate question (§8) |
| Q4 | Default `PropagateTestIdentity` to `true` in a minor, or `false` until v5? | **`true`, in R1, a minor.** It is the fix the issue asks for, and 2.34.0 set the precedent for messaging |
| Q5 | R0 and R1 as two releases, or one minor? | **Two.** R0 fixes calls that fail today for every user, gRPC or not, and does not wait on R1's tests |
| Q6 | Should a streaming call record its messages, not just its outcome? | **Not in R2.** Decide the shape (count, first and last, all up to a cap) separately |
| Q7 | Propagate when the call itself is not tracked in the current phase? | **Yes**, as the HTTP handler does: whether a call is drawn says nothing about whose work the next host does |
| Q8 | `GrpcTrackingOptions.CurrentStepTypeFetcher` does nothing: document it, or give gRPC the HTTP handler's implicit action start? | **Document it now**, and put its removal in `V5_PLAN.md`'s open questions |
| Q9 | Should R2 ship in this plan at all? | **Yes, last.** It is a defect in the file this plan rewrites (CLAUDE.md: fix what you find), it misreports BreakfastProvider's s55, and nothing waits on it |
| Q10 | Cap an encoded name's length, as ProxyTap and Playwright cap every value at 512? | **No cap on an id, ever. None on a name either,** unless a run reaches Kestrel's 16 KB field limit; nothing in the corpus comes near (§4.1) |

## 10. Assumption ledger

| Assumption | Level | If wrong |
|---|---|---|
| The issue's legacy `WebHost.CreateDefaultBuilder` hosts run `IStartupFilter`s as `WebApplication` hosts do, so the probe's hosts stand for theirs | INFERRED | Re-run P1 with a `WebHost` host |
| No reader outside Kronikol depends on the raw value of a non-ASCII identity header | INFERRED | A user's own reader sees `UTF-8''…`; the changelog says so, and `TestTrackingServerBridge` decodes |
| No test name reaches Kestrel's limits once encoded: 16 KB per field over HTTP/2, 32 KB for all headers (percent-encoding triples a non-ASCII byte) | INFERRED | Q10: cap names only, never ids |
| A server strips a header value's surrounding whitespace (RFC 9110), so §4.1 encodes a value that begins or ends with a space | INFERRED, RFC 9110 | The rule encodes a few names it need not; nothing breaks |
| gRPC servers ignore metadata keys they do not know, so non-Kronikol servers are unaffected by R1 | INFERRED, gRPC spec | A server that rejects unknown metadata would need `PropagateTestIdentity = false` |
| A caller's own identity keys should win over the interceptor's | INFERRED, ProxyTap's rule | Overwrite instead; T25 changes |
| Restoring `Activity.Current` after `AsyncUnaryCall` does not move the response span's parent (the response continuation captured the span's context when the call started) | INFERRED | T4 and T5 would show it; then end the span in the wrapper as now and restore only the caller's slot |

## 11. Log

- **2026-10-07.** Written by session kronikol-94 in worktree `C:/Code/Kronikol-grpc134` (branch `plan/grpc-134` at
  `417c8e58`). Peers writing plans from the same batch: kronikol-cc (#135, Kafka), kronikol-f1 (#136, MongoDB, which
  leaves `ITrackingComponent` and the column to this plan), kronikol-50 (#141, Shouldly), kronikol-1c (#132, xUnit v2)
  and kronikol-28 (an assessment of #133). The probe was run on 4.9.0 and on 4.6.0 from nuget.org.
- **2026-10-07, the same evening.** An independent check of every READ and RUN line found 23 problems. Every one was
  corrected, and these were measured for it:
  - P2's view from host B;
  - P8c (HTTP without an accessor);
  - P1b on 4.6.0;
  - P10's encoded legs;
  - P11's full list of gRPC calls;
  - the NU1605 restores;
  - the encoding rule on .NET 8 and 9.

  The wrong claims it caught:
  - "six components", which is four;
  - HTTP's hop rule stated without its accessor condition;
  - P2's "sends n of them", which is one joined value;
  - a P11 limited to scenario names, which hid three of BreakfastProvider's four affected scenarios and its `NotFound`
    stream;
  - T15 depending on R1;
  - a 32 KB limit that is 16 KB per field over HTTP/2.
- **2026-10-08.** Green-lit by the owner ("Can you implement the plan in full, in a separate worktree so you don't
  interfere with the other sessions. Fix any other problems you found"), with Q1 and Q4 as recommended. Q1 became R3, a
  patch after R2, and R3 also takes §8's first finding (the HTTP handler sends no `traceparent` from inside a TestServer
  host). Executed by kronikol-94 in worktrees `C:/Code/Kronikol-grpc134-impl` (R0) and `C:/Code/Kronikol-grpc134-r1`
  (R1 to R3). Other plans released first, so the numbers moved: R0 was claimed as 4.11.2 and shipped as 4.12.1, after
  4.11.1 and 4.12.0 (`XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md`).
  - **R0 = 4.12.1.** Its proofs are in the harness (`README.md`, "Execution"):
    - `r0/red-v4.11.0.txt`: with the facts copied onto v4.11.0 and `TrackingHeaderValue` stubbed to pass values
      through, 71 facts failed (core 43 of 93, gRPC 20 of 25, ProxyTap 2 of 22, Playwright 5 of 9, and the browser
      fact), each for its own reason. The guards that pass there are the plain-ASCII facts, the flags-`00` facts and
      the idle `—` cell. The first red run, on v4.10.0 (`r0/red-v4.10.0.txt`), found reader facts and a writer theory
      that took their expected wire form from `TrackingHeaderValue` itself, so the stub made them pass; they now take
      it from an independent RFC 8187 oracle.
    - `r0/mutations.txt`: 16 of 16 killed.
    - `results/accept/`: on R0's local packages, against 4.11.0 from nuget.org, P2, P3, P4, P5 and P10's handler cells
      flip, and nothing else does. One effect the plan did not predict: an HTTP call made on TestServer after a gRPC
      call now carries the handler's `traceparent`. The leaked gRPC span had made the handler leave the header to a
      framework `DiagnosticsHandler`, which TestServer does not have.
    - `results/accept/breakfastprovider-r0.txt`: BreakfastProvider, in a scratch clone at `0ee43e8` with its pins moved
      to each version. The xUnit lane passes 212 of 212 on both, with the same 2,650 calls and 941 distinct bodies, and
      `kronikol query diff` finds no scenario whose result or calls changed. The ReqNRoll lane passes 214 of 214 on
      both, and R0's run drew one CosmosDB query fewer, in s128 ("An outbox message should transition to failed after
      exhausting retries"), whose background outbox processor is polled until the message fails. Two more runs on
      4.11.0 drew 4 and 3 such queries, so the count is the scenario's own timing.
    - `r0/suite.txt`: the full suite on R0's commit before its rebase. `r0/suite-rebased.txt`: the full suite on the
      rebased commit.
  - The disk filled at about 10:25 UTC while several sessions built at once. The runs it broke (one mutation's build,
    the red proof's browser leg) were run again, and each file says so.
  - **R0 published the same day.** Release run 37768391888 and CI 37768389028 passed on `e8b8141e`, nuget.org lists
    all 62 ids at 4.12.1, the wiki has R0's edits (`35286a1`) and Kronikol4J's ledger its line (`688a64b`).
  - **R1 = 4.13.0.** Its proofs:
    - `r1/red-v4.12.1.txt`: with the facts copied onto v4.12.1, and `PropagateTestIdentity` and the interceptor's
      `HasHttpContextAccessor` stubbed to do nothing, 56 of the 78 gRPC rows failed, the three `HasHttpContextAccessor`
      facts of S3, ServiceBus and Sqlite failed, and the browser fact for the gRPC row did not read `1 of 2`. The 22
      gRPC rows that pass there are six guards: no identity header with the option off (one fact on fakes, one on
      real hosts), none for the background identity or for a detached flow, a call's logged request headers are the
      caller's alone, and the option's default (stubbed). A first run found five rows failing with an
      `ArgumentNullException`, because such a call sent no metadata at all and the fact read the missing collection;
      they now read an empty `Metadata` then, and the file is the run after that change and T25's below.
    - `r1/mutations.txt`: the first run killed 8 of 9. The survivor, a second name header beside the caller's own,
      showed that T25 set only the caller's id key. T25 is now a theory over each call kind and each of the four
      keys, and the same mutation for each key is killed: 12 of 12.
    - `results/accept/accept-4.13.0-local.r1-*`: against R0's results, P1, P1b, P6 and P7 flip, and the
      `nonascii propagated` variant now finds each of the five names on the next host's call, exactly, from the request
      headers. P8 does not flip, as planned until R3, and nothing else moves.
    - `results/accept/breakfastprovider-r1.txt`: s49, s50, s54 and s55 each gain the CosmosDB read behind their gRPC
      call (four calls, two of them `NotFound`), in both lanes; on s49 and s50 it nests under the call, and on the two
      streams it follows it, since a stream's response is logged when it starts until R2. Nothing else changed but s28,
      whose `GET /menu` calls the Supplier Service and Pub/Sub only while the menu is not cached; a second run on
      4.11.0 drew the same single call as R1.
    - `r1/suite.txt`: the full suite on R1's commit, before its rebase onto 4.12.2 and 4.12.3. `r1/rebased-check.txt`:
      on the rebased commit, the Release build and the core, gRPC, S3, ServiceBus, Sqlite and diagnostic-page
      projects, since 4.12.2 changed how `RequestResponseLogger`'s store is reached.
  - **R1 published the same day.** Release run 37774651570 and CI 37774646774 passed on `134fb79e`, nuget.org lists
    all 62 ids at 4.13.0, the wiki has R1's edits (`6b78db6`, with a gRPC-only "Hosts That Call Each Other" section on
    `Multi-Host-Test-Architectures.md` that R3 widens), Kronikol4J's ledger its line (`c6e3e18`), #134 is closed with
    a comment, and #137 has one saying its section 2 is done.
  - **R2 = 4.13.2.** Its proofs:
    - `r2/red-v4.13.0.txt`: with the facts copied onto v4.13.0, 8 of the 10 `StreamingOutcomeTests` facts failed: a
      stream was logged `OK`, with no content, as it started. The two that pass there are guards (a call disposed once
      its status is known is logged with that status; a stream read to its end and then disposed is logged once), and
      so are the 39 `GrpcTrackingInterceptorTests`, among them the three that now read their stream to its end.
    - `r2/mutations.txt`: 4 of 4 killed.
    - `results/accept/accept-4.13.2-local.r2-*`: against R1's results only P9 flips. The stream that fails after one
      reply is logged `NotFound` with its message, where it read `OK`.
    - `results/accept/breakfastprovider-r2.txt`: s54's and s55's CosmosDB reads now nest under their streams, s55's
      stream reads `NotFound`, and the two reflection streams, which their client disposes after one reply, read
      `Cancelled` (408). Those are the three new errors in each lane; ReqNRoll's one CosmosDB query fewer is s128's
      polling again.
    - `r2/suite.txt`: the full suite on R2's commit before its rebase onto 4.13.1, all 50 projects. The core passes
      7,059 with 2 skipped and the gRPC project 189. The Playwright project failed 79 facts in that run while the disk
      was full, and passed 997 with 28 skipped when run again alone on the same commit. `r2/rebased-check.txt`:
      on the rebased commit, since 4.13.1 changed how an adapter answers outside a test, the Release build and the
      gRPC project (189) pass, and every core fact passed in one of two runs. Each run failed one other fact, which
      passes alone: `CultureInvariantPipelineTests` under ar-SA in the first, and in the second
      `HistoryLedgerTests`' read budget (2,372 ms against 1,500, with other sessions' suites running), which 4.13.1's
      core run also failed. Neither reads gRPC code. R3 takes both up.
    - Found while releasing: kronikol-28's 4.13.1 CI failed `AsyncUnaryCall_activity_spans_from_request_to_response`
      once ("Sequence contains no matching element"; it passed on the second attempt). Nothing in the gRPC test project
      clears the span store. The cause is `InternalFlowActivityListener.EnsureStarted`: since #70 (`4e8bebd3`) a
      caller that loses the race to start the listener returns at once, before the winner has registered it, so the
      span the loser starts next is not sampled, and its gRPC call logs a trace id no span carries. R2's and R1's test
      classes add parallel first callers. The fix is R3's.
  - **R2 published the same day.** Release run 37788719687 and CI 37788717005 passed on `494265b5`, nuget.org lists
    all 62 ids at 4.13.2, the wiki has R2's edit (`853df17`, the call-kinds table of `Integration-Grpc-Extension.md`),
    and Kronikol4J's ledger its line (`1c470e6`: the port has always recorded a call in `onClose` with its status, so
    the two now agree).
  - **R3 = 4.14.3.** It also fixes the three defects found while releasing R2, each of which failed a full run now and
    then:
    - The listener race: a caller of `InternalFlowActivityListener.EnsureStarted` that loses the race waits until the
      listener is registered, for at most a second and still without a lock (#70). `r3/listener-red.txt`: on R3's code
      before the fix, with only the `IsStarted` probe added, 7 of the 8 concurrent callers returned before the listener
      was registered in the first round, in three runs of three, and the new guard named
      `InternalFlowActivityListenerTests`, whose #70 fact reset the listener from a parallel collection.
      `r3/listener-green.txt`: the same facts, three runs, and the 199 internal-flow facts, after.
    - `CultureInvariantPipelineTests` compares two runs of the whole pipeline, and the component diagram draws every
      call the process logged: one call logged under another test's id between the two runs failed it under every
      culture (an experiment, not kept). It now runs alone, in `WholeRunComparisonCollection`, which the
      diagram-cache guard accepts.
    - `HistoryLedgerTests`' read budget, and `RelationshipStatsTests`' budget, which kronikol-50 saw fail at 3,601 ms
      against 2,000 under load, are stretched by a probe of the machine's load (`ContentionScale`, as the Playwright
      project's render budgets are), at most five times. A read eight seconds slower, or a stats run eleven seconds
      slower, still fails them.
    Its proofs:
    - `r3/red-v4.13.2.txt`: with R3's facts copied onto v4.13.2, the five handler facts that pin a hop's identity,
      trace id, `traceparent` and caller name, and the three-host chain, fail; the 115 that pass there include the
      precedence fact, which held already and now reads which scenario the call belongs to (its mutation is killed).
    - `r3/mutations.txt`: 11 of 11 killed, the new ones among them: a losing caller that returns at once, one that
      waits no time, and a ledger read and a stats run slower than their budgets stretched to the cap.
    - `results/accept/accept-4.13.3-local.r3-*`: against R2's results, P8 flips (host B sees the scenario's identity
      and a `traceparent`, and its call to C is `id-1`, through the request header, where it was `unknown`), P8b and P8c
      gain the `traceparent` they lacked, and nothing else moves.
    - `results/accept/breakfastprovider-r3.txt`: both lanes pass, with the same calls, errors and statuses per service
      as R2's but one CosmosDB polling query, and the same logged headers: the identity headers a hop now sends are not
      added to what it logs, and BreakfastProvider's fakes record nothing of their own.
    - `r3/suite.txt`: the full suite on R3 rebased onto 4.13.3 (`e3e46160`), all 50 projects: the core passes 7,069
      with 2 skipped and the Playwright project 997 with 28 skipped, alone. A first run, at `4fe00a78`, failed 36
      Playwright facts while another session's Playwright suite ran beside it, and every other project passed.
      `r3/rebased-check.txt`: on the commit pushed, rebased onto 4.14.0, 4.14.1 and 4.14.2, release.slnf builds in
      Release, the Playwright project passes 1,003 with 28 skipped alone (a run during which the disk fell to 488 MB
      failed 31), and the core (7,108 with 2 skipped), assertion-tracking, LightBDD, MSTest and gRPC projects, rebuilt
      there, pass.
  - **Section 8's MSTest finding, measured and handed on.** `r4/probe/` is an MSTest project on the published packages,
    and `r4/probe-4.13.2.txt` its run on 4.13.2, read with its `query.cs`. Every row of a data-driven test had its
    method's id, so with the second of three `[DataRow]`s failing, `dotnet test` failed while the report said 3 scenarios
    and 0 failed, `Failures.md` said "No failures", and the later rows' calls were background calls. This plan's fix was
    written and red-proved, then dropped unreleased: kronikol-1c's 4.13.3 (ADAPTER_CAPTURE_GAPS_PLAN R1) gave each row an
    id of its own the same day, in another format, and a row's id should change once. A collision that format left (two
    methods whose rows share a `DisplayName` of their own) and this plan's facts went to that session, which fixes and
    adopts them in 4.14.2, with an overload defect it found on the way. Run with the same probe on the published
    packages, 4.13.2 reports 5 scenarios and 1 failed, the second row's failure missing, and 4.14.2 reports 8 and 2
    failed, every row and both rows named "small" apart (`r4/probe-4.13.2.txt`, `r4/probe-4.14.2.txt`).
  - **R3 published the same day; the plan is complete.** Release run 37837642849, CI 37837639629 (the unit-test job
    that failed on the listener race on 4.13.1's and 4.14.1's CI passed on its first attempt) and CodeQL 37837639639
    passed on `f9c67188`, nuget.org lists all 62 ids at 4.14.3, the wiki has R3's edits (`0b8eb20`), and Kronikol4J's
    ledger its line (`aab0cb9`). What the plan left is in `ROADMAP.md` Appendix C.

## Appendix A. Edit sites at `417c8e58`

**R0**

- `src/Kronikol/Tracking/TrackingHeaderValue.cs`: new, internal.
- `src/Kronikol/Kronikol.csproj:14-22`: `InternalsVisibleTo` for `Kronikol.Extensions.ProxyTap` and `Kronikol.Playwright`.
- `src/Kronikol/Tracking/TestTrackingMessageHandler.cs`:
  - L189-192 and L201: decode;
  - L211: `Guid.TryParse`;
  - L217, L220 and L223: encode.
- `src/Kronikol/Tracking/TestTrackingContextMiddleware.cs`: L27-31 decode; L12 doc.
- `src/Kronikol/Tracking/TestInfoResolver.cs:171-175`: decode.
- `src/Kronikol/Tracking/MessageTracker.cs`: L360-372 decode, L370 `Guid.TryParse`.
- `src/Kronikol/Tracking/TestTrackingServerBridge.cs:23-31`: decode; doc.
- `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs`: L331 and L338 decode; L357-361 encode.
- `src/Kronikol.Playwright/TestTrackingIdentity.cs:80-82`: encode (the public `HeaderSafe` at L101-112 stays).
- `src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs`:
  - `InjectTraceParent`, L343-357: copy, keep the caller's `traceparent`, flags from the span;
  - `AsyncUnaryCall`, L35-80: save and restore `Activity.Current`.
- `src/Kronikol.Extensions.Grpc/GrpcTrackingOptions.cs:22`: doc.
- `src/Kronikol/Reports/DiagnosticReportGenerator.cs:206-221`: the column and the per-instance cell.
- `src/Kronikol/Tracking/ITrackingComponent.cs:25-30`: doc.
- Tests:
  - `tests/Kronikol.Tests.Grpc/GrpcTrackingInterceptorTests.cs`;
  - `tests/Kronikol.Tests/Tracking/` (handler, middleware, resolver, `MessageTracker`, bridge, the new encoding);
  - `tests/Kronikol.Tests.ProxyTap`;
  - the Playwright package's tests;
  - the diagnostic report tests;
  - one E2E fact (T15) in `tests/Kronikol.Tests.EndToEnd`.

**R1**

- `src/Kronikol.Extensions.Grpc/GrpcTrackingOptions.cs`: `PropagateTestIdentity`.
- `src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs`:
  - the five call kinds (L35, L82, L130, L170, L208): resolve before the phase check, stamp, inbound trace id;
  - `HasHttpContextAccessor`.
- `HasHttpContextAccessor` on the other three:
  - `src/Kronikol.Extensions.S3/S3TrackingMessageHandler.cs`;
  - `src/Kronikol.Extensions.ServiceBus/ServiceBusTracker.cs`;
  - `src/Kronikol.Extensions.Sqlite/TrackingSqliteConnection.cs`.
- Doc summaries in `GrpcTrackingChannel.cs` and `GrpcServiceCollectionExtensions.cs`.
- Tests:
  - `tests/Kronikol.Tests.Grpc/Kronikol.Tests.Grpc.csproj`: `Grpc.AspNetCore` and a `Protobuf` item;
  - `Protos/hop.proto`;
  - a new `TwoHostPropagationTests.cs`;
  - one E2E fact (T36) in `tests/Kronikol.Tests.EndToEnd`.

**R2**

- `src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs`: L130-244, the stream wrappers.
- `tests/Kronikol.Tests.Grpc`: the server's `Stream` method and the streaming facts.
