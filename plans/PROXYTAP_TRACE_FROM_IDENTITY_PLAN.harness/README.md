# Harness: PROXYTAP_TRACE_FROM_IDENTITY_PLAN (#170)

Each probe is a file-based app. Run it with `dotnet run --file <name>.cs` (.NET 10 SDK; this machine has 10.0.300,
runtime 10.0.11). The ones that name `Kronikol.Extensions.ProxyTap@4.18.1` run against the published package, so
they show today's behaviour without building the repo.

| Probe | What it shows | Run on 2026-10-13 |
|---|---|---|
| `repro-170.cs` | The issue's repro, verbatim. A tap whose `IdentityResolver` names test `4bf92f35…4736` forwards a call that brings no `traceparent` under a new random trace, with and without a listener; a call that brings one keeps it. | Reproduced: three calls without a listener forwarded `61915d9e…`, `c6ea2d82…`, `15b38915…`; two with a listener `80ebdc22…`, `c7e1aaca…`; each record kept `testId` `4bf92f35…4736` and took the forwarded trace as `activityTraceId`. The call with `traceparent` `00-4bf92f35…4736-…-01` was forwarded under `4bf92f35…4736`. |
| `probe-identity-root.cs` | What `ActivitySource.StartActivity` does with a parent `ActivityContext` whose trace id is the test id: with a default (all-zero) span id, and with a random one. Also what `ActivityTraceId.CreateFromString` does with upper case and all-zero input. | Default span id, `isRemote: true`: the activity is in trace `4bf92f35…4736` with `ParentSpanId` `0000000000000000` and `ParentId` null (a root of that trace), `HasRemoteParent` true; its child parents on it. Random span id: `ParentSpanId` is that random id, which names no exported span. The sampler is shown trace `4bf92f35…4736`, span `0000000000000000`, `Recorded`, remote. `CreateFromString` throws `ArgumentOutOfRangeException` for `4BF92F35…` and for 32 zeros. |
| `probe-unsampled-inbound.cs` | A tap with a hand-written listener that returns `None` for a remote parent that is not sampled and samples a root, sent a `traceparent` with flags `01` and then `00`. (Written first as "shaped like OpenTelemetry's default"; `probe-otel-sdk.cs` shows the real SDK does not do this.) | Flags `01`: forwarded `00-0af76519…319c-3dc5efc1c554e4ed-01`, same trace. Flags `00`: forwarded `00-38f9dd232b37726e82bfe826cfa6c29a-66a777a6e1dbabf5-01`, a new trace, now sampled. Finding F2. |
| `probe-otel-sdk.cs` | The same three requests (flags `01`, flags `00`, none) through a tap whose listener is the real OpenTelemetry SDK 1.19.1 (`AddSource` plus the in-memory exporter), with its default sampler and, by argument, `ParentBased(TraceIdRatioBased(0))` (`ratio0`) and `AlwaysOff` (`alwaysoff`). Then R1's spans simulated on the same provider: a server span under (test trace, default span id, `Recorded`, remote) and a client span under it. | Default sampler: `01` kept with a new span, `00` forwarded verbatim (nothing exported), none got a random root, so F2 does not happen with the SDK (it answers a dropped root or remote parent with `PropagationData`). Ratio 0: the same, nothing exported for the root. AlwaysOff: all three forwarded as without a listener. R1 simulated: both spans recorded in trace `4bf92f35…4736` under the default sampler and under ratio 0, the server span with `ParentSpanId` `0000000000000000` and `HasRemoteParent` true; under AlwaysOff the server span exists unrecorded and the client span is null. |
| `probe-internal-flow-listener.cs` | The same three requests with Kronikol's own `InternalFlowActivityListener` attached (`AllData` on every source but `System.Net.Http`, as `TestTrackingMessageHandler` starts it in a test process), and with nothing attached (`none`). Then R1's spans simulated under it. | With the listener: `01` forwarded as `00-0af76519…319c-04f117d55a82b330-00`, `00` as `…-00`, none as `00-839aea1b…-…-00`: the sampled flag cleared on all three. Without it: `01` and `00` verbatim, none `…-01`. R1 simulated: both spans unrecorded, so the forwarded header would carry `-00`. Finding F3. |
| `probe-shared-trace-flow.cs` | `InternalFlowSegmentBuilder.BuildSegments` (public) over two calls of one test that overlap in time, each with a downstream span whose parent is the span id that call forwarded. Shape `r1`: both calls in the test's trace. Shape `today`: a random trace each. | `r1`: each call's popup holds both calls' spans. `today`: each holds its own. Finding F4. |

Trap met while writing the last probe: a listener whose `ShouldListenTo` reads `ProxyTap.ActivitySource.Name`
before anything else has touched the `ProxyTap` type fails with `TypeInitializationException` (inner
`NullReferenceException`) as soon as any `ActivitySource` is created. Here that was the `System.Net.NameResolution`
source, created inside `HttpListener.AddPrefix`. Creating `ProxyTap.ActivitySource` calls `ShouldListenTo`
synchronously while the field is still null. Compare with the constant `ProxyTap.ActivitySourceName`, as
`ProxyTapTests` does. The issue's repro reads `ActivitySource.Name` and works only because it builds the tap first.

## audit/ (2026-10-13)

The audit of both releases (plan section 12). `probe-http-instrumentation.cs` shows what reaches the service behind a
tap whose process instruments HttpClient or that was started under an activity (`-local` runs this tree); `acceptance.cs`
is section 5.3's acceptance (`acceptance-4.19.0.cs` runs the published 4.19.0); `bench-f4.cs` times the builder on one
test whose calls share one trace; `mutate.py` runs the mutations. Each has its output beside it, and `red-4.19.0.txt`
holds the red proofs. `plan_edits.py`, `roadmap_edits.py` and `../wiki/audit_wiki.py` hold the record's edits.
