# Harness for `GRPC_IDENTITY_PROPAGATION_PLAN.md` (#134)

Every RUN line in the plan comes from `probe/`, `escape/` or a `kronikol query` run, with its output in `results/`.

## The probe

`probe/` is a console app that starts three in-process hosts on `TestServer`, as the issue's suite runs them
(`WebApplicationFactory` hosts; `TestServer` does not flow the test's `AsyncLocal`):

```
test --HTTP (TestTrackingMessageHandler)--> host A --gRPC (GrpcTrackingInterceptor)--> host B --HTTP--> stub C
```

Host A and host B call `AddTestTrackingContextPropagation()`. Host B's gRPC service calls stub C through Kronikol's
HTTP handler with its own `IHttpContextAccessor`, and reports which identity headers and `traceparent` values arrived
and what its own server span looks like. `RequestResponseLogger.CaptureBackground` is on, so a call no scenario claims
is listed as `test=unknown` instead of being dropped. The `nonascii` probe also starts one Kestrel host (HTTP/1.1 and
h2c endpoints).

It references the **published** packages, so it measures what users run:

```bash
cd probe
dotnet build                                   # Kronikol.Extensions.Grpc 4.9.0, Grpc 2.84.0
dotnet run --no-build -- attribution control   # or any probe below
# The version #134 was filed against, from nuget.org (the user cache can hold a local pack of the same number):
dotnet build -p:KronikolVersion=4.6.0 -p:RestorePackagesPath=<empty dir> --source https://api.nuget.org/v3/index.json
```

`Kronikol.Extensions.Grpc` floats `Grpc.Net.Client` (`2.*`, #97): both 4.6.0 and 4.9.0 as published require 2.84.0 or
later, so the probe cannot pin the 2.76.0 the issue's suite lists. `-p:GrpcVersion=2.76.0` shows the restore failure
(`results/nu1605-*.txt`).

| Probe | Plan | What it shows |
|---|---|---|
| `attribution control` | P1 | A's gRPC client built with A's accessor: B's call to C is `unknown` |
| `attribution noaccessor` | P6 | The interceptor with no accessor and no fetcher, inside A: A's call still resolves (A's middleware scope); B's call to C is `unknown` |
| `attribution workaround` | P1 | The issue's second interceptor: B sees the identity, B's call to C is `id-1` |
| `attribution direct` | P1b | No host A: the test calls B over gRPC, the `CreateTestTrackingGrpcClient` shape. B's call to C is `unknown` |
| `attribution httpchain` | P8 | HTTP, not gRPC, between A and B, A's handler with A's accessor: B sees no identity and no `traceparent`; B's call to C is `unknown` |
| `attribution httpchain-forward` | P8b | As `httpchain`, with `HeadersToForward` naming the two identity headers: B's call to C is `id-1` |
| `attribution httpchain-noaccessor` | P8c | As `httpchain`, A's handler built without an accessor: it stamps the identity from A's middleware scope, and B's call to C is `id-1` |
| `metadata` | P2, P3 | One `Metadata` reused for three calls: what it holds, what B receives (one joined `traceparent`), B's server span (out of the trace from the second call), what each call logs; `Metadata.Empty` throws |
| `activity [recording\|parentbased]` | P4, P5 | `Activity.Current` after an awaited unary call is the interceptor's span, and the next call is its child; the wire flags are `00` even when the span is recorded, and under a parent-based sampler B's server span is then not recorded |
| `accessor` | P7 | `HasHttpContextAccessor` reads `false` for an interceptor built with an accessor |
| `streaming` | P9 | A server stream that sends one reply and then fails `NotFound` is recorded as `OK` with no content |
| `nonascii` | P10 | Five test names over real sockets and in memory: through the HTTP handler as it ships, as raw identity metadata (the issue's workaround), and encoded by the plan's section 4.1 rule (`Section41` in `Program.cs`, the same rule as `escape/esc.cs`) and decoded where they arrive |

## The encoding rule

`escape/esc.cs` is the plan's section 4.1 rule as a file-based app (`dotnet run esc.cs`, .NET 10 SDK). It encodes and
decodes the values that matter (the P10 names, a tab, surrounding spaces, a literal `UTF-8''`, the empty string, an
MSTest id, a GUID, a lone surrogate, 70,000 non-ASCII characters and a malformed escape) and says whether each comes back
exact. It was run on .NET 8, 9 and 10 by putting `#:property TargetFramework=net8.0` (or `net9.0`) as the first line of
a copy: `results/escape-net{8.0,9.0,10.0}.txt`.

## A real consumer

`results/p11-breakfastprovider-xunit-4.0.2.txt` is BreakfastProvider's xUnit lane as its CI wrote it on 2026-10-05
(run #307, `106702b`, Kronikol 4.0.2; the gRPC package, the middleware and the resolver are unchanged from v4.0.2 to
v4.9.0), read with `kronikol query` only. It holds every gRPC call into the API (`interactions --grep
breakfast.BreakfastGrpc`), the flow of each of those scenarios, the five scenarios whose names mention gRPC, and the run's
CosmosDB calls grouped by method. The report itself is kept in another session's scratchpad and is not copied here.

## Results

`results/p<n>-<probe>[-<variant>]-<version>.txt`, one file per run (the P-number is the plan's).

- Run on 4.9.0 (`417c8e58`) and on 4.6.0 (`a074cf14`, restored from nuget.org), with the same answers on both:
  - `attribution control`, `workaround`, `noaccessor` and `direct`;
  - `metadata`;
  - `activity control`;
  - `accessor`.
- Run on 4.9.0 only:
  - `activity recording` and `parentbased`;
  - `attribution httpchain`, `httpchain-forward` and `httpchain-noaccessor`;
  - `streaming`;
  - `nonascii`.

## Execution

Each release's proofs, added as it was made:

- `r0/red.sh <worktree> <out>` runs R0's new and changed facts on the previous release's source: a worktree at that tag
  with the facts copied in and `TrackingHeaderValue` stubbed to pass values through. The stub cannot make a fact
  vacuous, since the facts take their expected wire form from an independent RFC 8187 oracle, never from
  `TrackingHeaderValue`. Output: `r0/red-v4.10.0.txt` and `r0/red-v4.11.0.txt`.
- `r0/mutate.py <worktree> <out>` applies 16 mutations of R0's product code one at a time and runs the facts that should
  catch each. Output: `r0/mutations.txt`.
- `suite.sh <worktree> <out>` runs every test project in Release. Output: `r0/suite.txt`.
- `accept.sh <version> <package dir, or -> <results dir> <scratch>` runs every probe on one version, restoring into an
  empty package folder of its own. Output: `results/accept/accept-<version>-<probe>-<variant>.txt`, with 4.11.0 from
  nuget.org as the control and `4.11.2-local.r0` packed from R0's commit (released as 4.12.1, after 4.11.1 and 4.12.0 took the numbers before it). The `nonascii propagated` variant (added for
  R1) sends each name through a host that calls `AddTestTrackingContextPropagation()` and reads the next call from the
  log.
- `r1/red.sh <worktree at the previous release> <worktree at R1> <out>` and `r1/mutate.py`, the same for R1. The red
  proof stubs `GrpcTrackingOptions.PropagateTestIdentity` and the interceptor's `HasHttpContextAccessor` so the facts
  compile; neither stub does anything. Output: `r1/red-v4.12.1.txt` and `r1/mutations.txt`.
- `r2/red.sh` and `r2/mutate.py`, the same for R2, with nothing stubbed: R2's facts use only public members. Output:
  `r2/red-v4.13.0.txt` and `r2/mutations.txt`.
- `r3/red.sh` and `r3/mutate.py`, the same for R3, with nothing stubbed. Output: `r3/red-v4.13.2.txt` and
  `r3/mutations.txt`. `r3/listener-red.txt` and `r3/listener-green.txt`: the listener facts on R3's code before and after
  the `EnsureStarted` fix, with only the `IsStarted` probe added before it, which changes nothing.
- `r4/probe/`, an MSTest project on the published packages (`-p:KronikolVersion=`), with three `[DataRow]`s, the second
  failing, a test with a `DisplayName` and no data, two `[DynamicData]` rows, and two methods whose one row each has the
  same `DisplayName` of its own, one failing; `r4/probe-<version>.txt` holds what `dotnet test` printed and the report as
  its `query.cs` reads it.
- `r1/suite.txt`, `r2/suite.txt` and `r3/suite.txt`: `suite.sh` on each release's commit before its rebase, and
  `r1/rebased-check.txt`, `r2/rebased-check.txt` and `r3/rebased-check.txt` the Release build and the projects the
  rebase could touch, on the commit that was pushed.
- `results/accept/breakfastprovider-r0.txt`, `-r1.txt`, `-r2.txt` and `-r3.txt`: BreakfastProvider's xUnit and ReqNRoll
  lanes on each release's local packages, compared with the release before, and `breakfastprovider-grpc-flows-4.11.0.txt`
  the four gRPC scenarios before R1.
