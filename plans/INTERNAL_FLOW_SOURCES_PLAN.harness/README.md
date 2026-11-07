# Harness for `INTERNAL_FLOW_SOURCES_PLAN.md` (#138)

`probe/` is a console app built for net8.0, net9.0 and net10.0. It registers an `ActivityListener` set up like
Kronikol's `InternalFlowActivityListener` (sampling `AllData`, capturing on `ActivityStopped`), with one difference
chosen by the first argument:

| mode | `ShouldListenTo` | `Sample` |
|---|---|---|
| `none` | no listener | |
| `exclude` | every source except `System.Net.Http` (Kronikol today) | `AllData` |
| `include` | every source | `AllData` |
| `include-parented` | every source | `None` for `System.Net.Http` when the span has no parent, else `AllData` (the plan's R1) |

The probe then makes three `HttpClient` GETs to a loopback HTTP/1.1 server, which records the `traceparent` each request
carries. With `grpc` it also makes one unary gRPC call through `Grpc.Net.Client` 2.76.0, to a closed port, or to the live
server when `PROBE_GRPC_LIVE=1`. With `ai` it also runs Application Insights' `DependencyTrackingTelemetryModule`
(`Microsoft.ApplicationInsights.DependencyCollector` 2.23.0) into a capturing channel. It prints what the listener
captured (source, operation, display name, kind, parent), what the server saw on the wire, and what Application Insights
recorded.

```
dotnet build -c Release
dotnet bin/Release/<tfm>/Probe.dll <none|exclude|include|include-parented> <ai|noai> <parent|noparent> [ai-first|listener-first] [grpc]
```

The output files were measured on 2026-10-26 on Windows 11 26200 with .NET 8.0.27, 9.0.16 and 10.0.11:

- `results-ai.txt`: every mode on every runtime with Application Insights on, with and without a parent span.
- `results-noai.txt`: no Application Insights, with a parent. Also includes the `listener-first` start order.
- `results-noai-noparent.txt`: no Application Insights, no parent. This is the case that shows plain `include` changes
  the wire.
- `results-grpc-live.txt`: a gRPC call to a live server, with `exclude` and `include`. Each `GrpcOut` gets an HTTP child.
