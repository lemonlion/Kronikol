# Repository facts for TAP_CALLER_TIME_LIMITS_PLAN.md (#173)

Swept 2026-10-15, read-only. Repo paths are relative to `C:/Code/Kronikol-173` (commit `3240c983`, version 4.20.2, `src/`
unmodified) unless they say otherwise. Wiki line numbers are at the wiki's `origin/master` `60daf39` (read them with
`git -C ../Kronikol.wiki show 60daf39:<page>.md`). Kronikol4J line numbers are at its `origin/main` `818b010` (read
them with `git -C ../Kronikol4J show 818b010:docs/REMAINING_PARITY.md`).

Later the same day #174 shipped (4.20.3, 4.22.0, 4.22.1) and its worktree `C:/Code/Kronikol-174` was removed; its plan
and harness, cited below by that worktree's paths, are on `main` under `plans/` (`fefb99e5`).

**Headline: #174 changed under the plan.** `C:/Code/Kronikol-174/plans/TCPTAP_FAILED_CONNECT_PLAN.md` L4-5: "**Green-lit
in full by the owner on 2026-10-15** ("implement the plan in full"), with every question in §9 answered as recommended."
Its R1 is half-written in that worktree (uncommitted: `OtlpTap.cs`, `TcpTap.cs`, the Otlp csproj, a new
`TapAcceptLoop.cs` and two new test classes). #173 will land second and rebase. See Q2, Q10 and Q11.

---

## 1. Frameworks and packages

**TFMs.** `Directory.Build.props` L5 `<TargetFramework>net10.0</TargetFramework>`, L6
`<MultiTargetFrameworks>net8.0;net9.0;net10.0</MultiTargetFrameworks>`. Each of the three packages clears the single TFM
and takes the list:
- `src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj` L4-5
- `src/Kronikol.Extensions.Otlp/Kronikol.Extensions.Otlp.csproj` L4-5
- `src/Kronikol.Extensions.TcpTap/Kronikol.Extensions.TcpTap.csproj` L4-5

Every tap test project inherits `net10.0` and sets no TFM: Kronikol.Tests.ProxyTap, .Otlp, .TcpTap and .TcpTap.Bson3.
`Kronikol.Tool` is `net10.0` only (`src/Kronikol.Tool/Kronikol.Tool.csproj` L5-6). `System.TimeProvider`, `ITimer` and
`Debugger.IsAttached` are in the BCL on all three TFMs, so `src/` needs no package for them.

**Does any other project compile the taps' sources?** Not at `3240c983`. No `<Compile Include>` anywhere points into the
three tap folders. It runs the other way: TcpTap compiles Redis's and MongoDB's sources (`Kronikol.Extensions.TcpTap.csproj`
L30-45, under `DefineConstants KRONIKOL_TCPTAP_SHARED` at L13).

#174's R1 changes that, uncommitted in `C:/Code/Kronikol-174`. `Kronikol.Extensions.Otlp.csproj` gains
`<DefineConstants>$(DefineConstants);KRONIKOL_OTLP_SHARED</DefineConstants>` and
`<Compile Include="..\Kronikol.Extensions.TcpTap\TapAcceptLoop.cs" Link="Shared\TapAcceptLoop.cs" />`.

Other projects reference a tap's assembly but do not compile its sources:
- `src/Kronikol.Tool/Kronikol.Tool.csproj` L44 references Otlp. It uses `OtlpExporter` and `OtlpTraceReader`, not
  `OtlpTap`.
- `tests/Kronikol.Tests.EndToEnd/...csproj` L41 references Otlp.
- `tests/Kronikol.Tests.Playwright/...csproj` L24 references ProxyTap.
- `tests/Kronikol.Tests/...csproj` L34 references Kronikol.Tool, so it gets Otlp transitively.

**Central package management: none.** There is no `Directory.Packages.props` and no `ManagePackageVersionsCentrally`
anywhere. Versions are inline in each csproj. ROADMAP row 1.11 (`plans/ROADMAP.md` L244) says the packages float "with
no central package list".

Test projects float `Microsoft.Extensions.*` at `10.*`, for example `tests/Kronikol.Tests.TcpTap/...csproj` L20
`Microsoft.Extensions.DependencyInjection Version="10.*"`. The tap test projects pin `xunit.v3` 3.2.2,
`xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.5.1 and `coverlet.collector` 10.0.0.

**TimeProvider packages.** Neither `Microsoft.Extensions.TimeProvider.Testing` nor `Microsoft.Bcl.TimeProvider`
appears in any csproj, props or json file. `FakeTimeProvider` appears only in the #173 plan.

**`TimeProvider` in code.** None in `src/` or `tests/` at `3240c983`. The only hit is an unrelated `DateTimeProvider`
inside a test's source string, at `tests/Kronikol.Tests.AssertionTracking/AssertionWeaverTests.cs` L2214-2233.

That changes with #174 R1:
- `TapAcceptLoop.cs` L40 adds `internal TimeProvider Clock { get; set; } = TimeProvider.System;`, the clock behind the
  once-a-minute log rule.
- Its tests use a hand-written `internal sealed class ManualClock(DateTimeOffset start) : TimeProvider`
  (`tests/Kronikol.Tests.TcpTap/TcpTapAcceptTests.cs` L362-370, namespace `Kronikol.Tests.TcpTap`) and set
  `tap.Accepting.Clock = clock` (L205-206). They add no package.

So the #173 plan's §4.2 item 4 ("There is no `TimeProvider` anywhere in `src/` today") goes stale once #174 R1 lands.
Each tap will already have one clock seam, `Accepting.Clock`.

Today's clock reads in the taps:
- ProxyTap.cs: `DateTimeOffset.UtcNow` at L277, 454, 480; `Stopwatch` at L418; `Task.Delay(25)` drain at L1000.
- InFlightIdentityRegistry.cs L58: `UtcNow`.
- TcpTap.cs: `UtcNow` at L294, 360, 475, 653; `Environment.TickCount64` at L311, 386, 731; `Task.Delay(1_000)` at L340;
  `Task.Delay(25)` at L712.

`Debugger.IsAttached` is used nowhere in `src/` or `tests/`.

## 2. Linked-file type conflicts

**Project references in `src/` that involve the taps:**

| From | To |
|---|---|
| ProxyTap | Kronikol only (csproj L14) |
| Otlp | Kronikol only (csproj L14); after #174 it also compiles `TapAcceptLoop.cs` |
| TcpTap | Kronikol (L26), `MongoDB.Bson 2.*` (L24), and the 12 linked Redis/MongoDB files (L33-44) |
| Kronikol.Tool | Kronikol and Otlp (L43-44) |

ProxyTap references neither Otlp nor TcpTap, and no `src/` package references two of the three.

**InternalsVisibleTo, all in csproj items.** No `[assembly: InternalsVisibleTo]` exists in any `.cs` file under `src/`.
- ProxyTap → `Kronikol.Tests.ProxyTap` (csproj L18).
- Otlp → `Kronikol.Tests.Otlp` (L18). #174's diff keeps exactly that.
- TcpTap → `Kronikol.Tests.TcpTap` (L48) and `Kronikol.Tests.TcpTap.Bson3` (L49).
- Inbound: core `src/Kronikol/Kronikol.csproj` L21 grants IVT to `Kronikol.Extensions.ProxyTap`, for TrackingHeaderValue
  per L20. Core grants none to Otlp or TcpTap.

**Test projects that reference the taps:**

| Test project | References |
|---|---|
| Kronikol.Tests.ProxyTap | ProxyTap and Kronikol (L27-28) |
| Kronikol.Tests.Otlp | Otlp and Kronikol (L23-24) |
| Kronikol.Tests.TcpTap | Kronikol, TcpTap, Redis and MongoDB.V2 (L41-46) |
| Kronikol.Tests.TcpTap.Bson3 | Kronikol and TcpTap (L27-28) |
| Kronikol.Tests.Playwright | ProxyTap (L24), with no IVT from it |
| Kronikol.Tests.EndToEnd | Otlp (L41), with no IVT from it |
| Kronikol.Tests | Otlp, transitively through Kronikol.Tool (L34), with no IVT from it |

**Can one compilation see two copies of an internal type? No, today, whatever the namespace.**
- Each tap grants IVT only to its own test project(s).
- No test project references two taps.
- No tap package has IVT from another tap.
- Core, the one assembly with IVT into a tap (ProxyTap), would not compile the shared file.

A project that references a tap without IVT sees its internals as inaccessible. Roslyn prefers the one accessible
candidate and reports no ambiguity.

**Warnings as errors: off.** No `TreatWarningsAsErrors`, `WarningsAsErrors` or `-warnaserror` appears in any props,
targets, csproj, rsp, `.editorconfig` or workflow. `CHANGELOG.md` L8503 says so outright: "NU1605 is in the SDK's
default `WarningsAsErrors` list ... with no `TreatWarningsAsErrors` anywhere in the repo." So CS0436 would only warn.
CS0433 (two accessible copies, once a name is used) is always an error.

**The MongoDB.V2 precedent** (`src/Kronikol.Extensions.MongoDB.V2/...csproj` L8 `RootNamespace
Kronikol.Extensions.MongoDB`, links at L27-40, mostly public types):
- It compiles the shared files into the **same** namespace as `Kronikol.Extensions.MongoDB`.
- It is safe only because the two packages are mutually exclusive: no project references both. `Kronikol.Tests.MongoDB`
  references MongoDB (L26) and `Kronikol.Tests.MongoDB.V2` references V2 (L23).
- Each package's IVT goes to its own test project only (MongoDB csproj L11, V2 L13).
- It is not a precedent for three packages that a host references together.

**The precedent that fits is TcpTap's.**
- `KRONIKOL_TCPTAP_SHARED` (TcpTap csproj L10-13) recompiles the same Redis/Mongo files into
  `Kronikol.Extensions.TcpTap.Protocols`, "so a host may reference this package and Kronikol.Extensions.Redis / .MongoDB
  at the same time". The switch is at the top of each shared file, for example
  `src/Kronikol.Extensions.Redis/RedisOperation.cs` L1-8 and `src/Kronikol.Extensions.MongoDB/MongoJson.cs` L5-12.
- `Kronikol.Tests.TcpTap` references TcpTap and MongoDB.V2 together. That works only because the namespaces differ.

**#174's `TapAcceptLoop` follows the TcpTap pattern.** In `C:/Code/Kronikol-174/src/Kronikol.Extensions.TcpTap/TapAcceptLoop.cs`:
- L1: "Shared source: also compiled into Kronikol.Extensions.Otlp as a linked file, in that package's own namespace."
- L5-9: `#if KRONIKOL_OTLP_SHARED namespace Kronikol.Extensions.Otlp; #else namespace Kronikol.Extensions.TcpTap; #endif`.
- L20: `internal sealed class TapAcceptLoop`.

`Kronikol.Tests.Otlp` sees only `Kronikol.Extensions.Otlp.TapAcceptLoop`. `Kronikol.Tests.TcpTap` and Bson3 see only the
TcpTap copy, and no other project has IVT from either package. No conflict now, and none even if a future test project
got IVT from both taps.

**Recommendation for `StallWatch`.** Copy #174 exactly:
1. Put one file in one tap's folder, as an `internal sealed` class.
2. Switch its namespace per package:
   ```
   #if KRONIKOL_OTLP_SHARED
   namespace Kronikol.Extensions.Otlp;
   #elif KRONIKOL_PROXYTAP_SHARED
   namespace Kronikol.Extensions.ProxyTap;
   #else
   namespace <home namespace>;
   #endif
   ```
   `KRONIKOL_OTLP_SHARED` already exists after #174. Add `KRONIKOL_PROXYTAP_SHARED` to the ProxyTap csproj, which has no
   `DefineConstants` today. If the home is TcpTap's folder, do not key the TcpTap branch on a namespace ending
   `.Protocols`, which is where `KRONIKOL_TCPTAP_SHARED` sends the Redis/Mongo files.
3. Link it with `Link="Shared\StallWatch.cs"`, as #174 does.
4. Test it through each package's IVT, never by linking the file into a test project, which would raise CS0436.
5. Seed the watch's `TimeProvider` from the same seam as `Accepting.Clock`, so a tap has one clock, not two.

Moving the watch into core as `internal` would need new IVT from core to Otlp and TcpTap, which exposes all of core's
internals to them. Core already grants it to ProxyTap.

## 3. Where tap options are built or copied

Nothing in `src/` outside the three packages, nor in `templates/`, `examples/` or `tools/`, constructs, copies or wraps
`ProxyTapOptions`, `OtlpTapOptions`, `TcpTapOptions`, `RedisTapOptions` or `MongoTapOptions`. The only mentions outside
the packages are in comments. There is no `samples/` directory.

**ProxyTap**
- Constructed at `ProxyTapServiceCollectionExtensions.cs` L19.
- Validated by `internal Validate()` (`ProxyTapOptions.cs` L186-199), called both at registration (L21) and at
  `StartAsync` (`ProxyTap.cs` L174). R0's "refused at start" fires at registration too.
- Exposed as `ProxyTap.Options` (L71). `ForwardFailure.Reason(..., ProxyTapOptions)` reads it (`ForwardFailure.cs` L50).
- **`Copy` (L42-77) copies 32 of 32 public members.** That is 28 settable properties plus the 4 get-only collections:
  - `HeaderWhitelist`: `UnionWith`, L54.
  - `SecretDenylist`: `Clear` then `UnionWith`, L55-56.
  - The two header fallback lists: `AddRange`, L61-62.
- The fact `Registering_taps_from_a_list_copies_every_option` (`tests/Kronikol.Tests.ProxyTap/ProxyTapRegistrationTests.cs`
  L34-74) walks the properties by reflection. Its `NonDefault` table (L16-32) knows `int?` (L19) and `TimeSpan` (L24),
  but not `TimeSpan?`, so a `TimeSpan?` option makes it throw "No test value ..." (L30-31), as the plan expects.

**OtlpTap**
- Constructed at `OtlpTapServiceCollectionExtensions.cs` L19 and validated at L21 and `OtlpTap.cs` L159.
- Exposed as `OtlpTap.Options` (L76).
- Also the input of the public static `SpanToInteractionMapper.Map(OtlpSpan, OtlpTapOptions)` and `MapAll(...)`
  (`SpanToInteractionMapper.cs` L35, L72). New listener options would sit on a type that is also used for offline mapping.
- **`Copy` (L42-67) copies 20 of 20 members:** 17 settable plus `ExpectedHeaders` (key by key, L47-48),
  `ServiceNameMap` (L54-55) and `CaptureKinds` (`Clear` then `UnionWith`, L56-57).
- No copy-every-option fact exists for this package, so R2-7 would be new. `OtlpTapTests.cs` L415 uses only the single
  registration.

**TcpTap has no `Copy` method.** Copying goes through **public virtual** `TcpTapOptions.CopyTo(TcpTapOptions target)`
(`TcpTapOptions.cs` L216-252). It copies 32 of 32 settable properties. `DisplayName` at L185 is computed and public.

The overrides call `base.CopyTo` first:
- `RedisTapOptions.CopyTo` (`RedisTapOptions.cs` L79-91) adds 5 more.
- `MongoTapOptions.CopyTo` (`MongoTapOptions.cs` L68-81) adds 7 more.

Callers of `CopyTo` (`TcpTapServiceCollectionExtensions.cs`):
- `AddRedisTapTestTracking(IEnumerable<RedisTapOptions>)`, L31-41, calls `captured.CopyTo(target)` at L37.
- `AddMongoTapTestTracking(IEnumerable<...>)`, L65-75, calls it at L71.
- `AddTcpTapTestTracking` (L82-95) has only the `Action<TcpTapOptions>` overload, with **no list overload**.

`TcpTapOptions` is a `public class` (L29), `Validate` is `public virtual` (L188), and `TcpTap` is a `public class`, not
sealed (L35).

What this means for the plan:
- R3-7 "Registering_taps_copies_every_option for TcpTap" has no `Copy` to test. It has to go through `CopyTo` and the
  Redis/Mongo list registrations.
- Appendix A does not list `TcpTapOptions.CopyTo` L216-252 as an R3 edit site. The plan's own harness flagged it:
  `read/tcptap.md` §10 says "It also needs a line in `CopyTo` (Options.cs:217-252), and no fact would notice the line
  missing".
- The existing `CopyToCarriesEveryProtocolOption` (`tests/Kronikol.Tests.TcpTap/TcpTapTests.cs` L636-666) checks
  hand-picked values. It does not use reflection.

**Do RedisTap, MongoTap or the MongoDB, Redis or Kafka packages mirror TcpTap's options?**
- `RedisTapOptions` and `MongoTapOptions` derive from `TcpTapOptions` (`RedisTapOptions.cs` L10, `MongoTapOptions.cs`
  L10). A new `TcpTapOptions` property and its `CopyTo` line reach both automatically, with nothing to add there.
  `RedisTap` and `MongoTap` (`RedisTap.cs` L16, `MongoTap.cs` L18) only re-expose the typed options.
- `TcpTapConnectionContext.Options` (`TcpTap.cs` L768) is public and is how decoders read options.
- Nothing in Kronikol.Extensions.Redis, .MongoDB(.V2) or .Kafka builds tap options or mirrors
  `ReapStuckConnectionsAfter`. A grep for `ReapStuck|StuckConnection|TcpTap*Options` outside the TcpTap package finds
  only tests in `Kronikol.Tests.TcpTap`.

## 4. What runs through each tap

**ProxyTap buffers everything and answers with a Content-Length. There is no streaming, WebSocket or CONNECT handling.**
- **Hop-by-hop headers.** `HopByHop` holds `Connection`, `Upgrade`, `TE` and `Trailer` (`ProxyTap.cs` L41-45). They are
  dropped from the forwarded request (`CopyRequestHeaders` L808-823, the skip at L812) and from the answer (L834).
  - The tap never calls `AcceptWebSocketAsync`, so a WebSocket handshake reaches the upstream as a plain GET.
  - There is no tunnel code: every request goes to the base's authority plus the request's `PathAndQuery` (L274), with
    the request's method (L401).
- **The request is buffered whole.** `CopyToAsync` into a `MemoryStream` (L284-287), then `ByteArrayContent` (L403).
- **The answer is buffered whole.** `SendAsync(message, HttpCompletionOption.ResponseContentRead, deadline.Token)` (L426),
  then `ReadAsByteArrayAsync` (L427). An endless upstream stream, such as SSE, runs into `ForwardTimeout` (200 s) and
  becomes a 502.
- **Content-Length is set on both answer paths.**
  - `RespondAsync`: `response.ContentLength64 = body.Length` (L851), then a single `WriteAsync(body)` (L852).
  - `TryRespondBadGateway`: `ContentLength64 = payload.Length` (L894), then a **synchronous** `Write` (L895).
  - The one path without a length is `CallerAnswer.EndUnclosed` (L930-955): it sets the status and calls `Abort()`,
    with no body.

**OtlpTap: HTTP/1.1 only.**
- It is a hand-rolled reader over `TcpListener` (`OtlpTap.cs` L27-33; `HttpRequestReader.cs` L28-33: "A minimal HTTP/1.1
  request reader"). HTTP/1.0 is handled for keep-alive (L73-80). There is no HTTP/2, h2c or gRPC path.
- Wiki Otlp page L204: "**HTTP/1.1 only.** OTLP/gRPC exporters need to be pointed at an OTLP/HTTP endpoint
  (`OTEL_EXPORTER_OTLP_TRACES_PROTOCOL=http/protobuf`)...".
- Per-connection serve tasks are fire-and-forget: `_ = Task.Run(() => ServeAsync(client, ct))` (L273), which #174 keeps.
  `DisposeAsync` awaits only the accept and map loops (L595-607).
- The tap counts no live connections; ProxyTap has `InFlight` and TcpTap has `LiveConnections`.

**TcpTap protocols:**
- RESP2/RESP3 through `RedisTap` (`RedisProtocolDecoder`).
- MongoDB OP_MSG, with the OP_QUERY handshake stepped over and OP_COMPRESSED passed through undecoded (wiki TcpTap L214-221).
- Any other protocol through `DecoderFactory` and `AddTcpTapTestTracking` (`TcpTapOptions.cs` L181-182;
  `TcpTapServiceCollectionExtensions.cs` L77-95). Bytes always pass, and TLS is not terminated (wiki L325).

Kafka (#135) does not use TcpTap; no Kafka source mentions it. It is captured in-process (`KAFKA_INTERCEPTION_AND_TRACE_PLAN.md`).

**Does a supported client half-close and then wait?** The repo has no evidence that one does.
- The only half-close in tests is synthetic: `AHalfCloseIsPropagatedUpstreamWhileTheReplyStillFlowsBack` (`TcpTapTests.cs`
  L191-220) half-closes a raw client after it sends, and a "late reply" must still arrive.
- The wiki promises the same: TcpTap L32 says "Half-close is propagated, so a client that shuts down its send side still
  receives the reply."
- StackExchange.Redis 2.6.48 ends with `QUIT, QUIT` (wiki L245-246), which is a command, not a half-close.
- The harness marks server and client half-close behaviour as INFERRED (`read/tcptap.md` L374-377, L387-392). In case (b),
  a pooled MongoDB.Driver connection with no read pending is held until its next checkout or the driver's
  `MaxConnectionIdleTime` (10 min, INFERRED).
- The generic `DecoderFactory` path admits any protocol.

**An inconsistency inside the plan.**
- R3 item 1 (plan L335-341) defines `HalfClosedTimeout` as an **absolute** deadline from the half-close ("the pair is
  closed if the other has not ended within this long").
- §4.1 item 1 (L193-196) says every limit is reset by progress.
- The harness's own read recommended "a 'quiet after half-close' rule rather than an absolute deadline", citing the L191-220
  fact (`read/tcptap.md` §10, L572-575).
- Under the absolute reading, a reply that streams for more than 100 s after a half-close is cut.

## 5. Tests

**Allocation facts.**
- No fact anywhere uses `GC.GetTotalAllocatedBytes`. The existing ones use `GC.GetAllocatedBytesForCurrentThread()`:
  `tests/Kronikol.Tests/History/HistoryAnalyzerTests.cs` L984-986, and `tests/Kronikol.Tests/Tool/QueryStreamingTests.cs`
  L65-67, L134-136 and L161-163.
- The reason is spelled out at `QueryStreamingTests.cs` L20-22: "Measured with GetAllocatedBytesForCurrentThread rather
  than working set: these tests share a parallel xunit process with four thousand others, so process-wide memory says more
  about the neighbours than about the scan."
- Plan facts R0-2 and R0-3 use process-wide `GC.GetTotalAllocatedBytes`. The tap allocates on thread-pool threads
  (`Task.Run(() => ServeAsync...)`), so a per-thread counter cannot see it.
- Process-wide measures need the fact isolated. The precedent is
  `[CollectionDefinition(nameof(ProxyTapTraceContextTests), DisableParallelization = true)]`
  (`tests/Kronikol.Tests.ProxyTap/ProxyTapTraceContextTests.cs` L19-23, "run in a collection of their own, after and apart
  from every other collection"; also used by `ProxyTapFailureSpanTests.cs` L12).

**Parallelization.**
- No tap test project has `xunit.runner.json`, so xUnit v3 defaults apply: classes run in parallel, up to one thread per
  processor. The only `xunit.runner.json` in `tests/` is in Kronikol.Tests.EndToEnd.
- ProxyTap has the one serial collection above.
- Otlp, TcpTap and Bson3 have no `[Collection]` or `CollectionDefinition` attributes.
- ProxyTap's `test.runsettings` sets only environment variables (`KRONIKOL_HISTORY=off`, `KRONIKOL_KEEP_RUNS=off`).

**Framework.** xunit.v3 3.2.2, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.5.1 and coverlet.collector
10.0.0 in all four tap test projects.

**Reflection in the hang-up tests: yes.** `tests/Kronikol.Tests.ProxyTap/ProxyTapHangUpTests.cs`:
- `Held(Tap tap)` (L413-417) reads the tap's private `_listener`, then
  `(Count(listener, "_listenerContexts"), Count(listener, "_connections"))` (L416).
- `Count` (L419-424) locks `SyncRoot`.
- `Field` (L426-430) uses `BindingFlags.NonPublic | BindingFlags.Instance` and throws a message naming
  `PROXY_TAP_HANG_UP_PLAN.harness/probe/probe.cs`.
- Those facts skip on Windows: `Assert.SkipWhen(OperatingSystem.IsWindows(), HttpSysDropsIt)` at L32, L58 and L107.
- `_unregisteredConnections` lives on `HttpEndPointListener`, not `HttpListener`, so reaching it means a different
  reflection path.
- `OtlpTapHangUpTests` uses no reflection. Its `HeldCollector` (L321) is a stub collector.

**LoopbackPort.**
- Linked into ProxyTap's test project (`Kronikol.Tests.ProxyTap.csproj` L35-38) and Otlp's (`Kronikol.Tests.Otlp.csproj`
  L37-40).
- Not linked into `Kronikol.Tests.TcpTap.csproj`, which links only `CultureRun.cs` at L36-38, nor into Bson3.
- TcpTap facts bind port 0 and keep it: taps use `ListenPort = 0` (`TestSupport.cs` L115, L122), and stubs hold
  `new TcpListener(IPAddress.Loopback, 0)` open (L300-302). That is safe; LoopbackPort guards against binding 0 and
  then releasing it.
- #174 plans to link LoopbackPort into `Kronikol.Tests.TcpTap.csproj` in R1 (plan L430-432; Appendix A L683). It is not
  done yet in its worktree, and Bson3 is not mentioned. The #173 plan's "R3 links it" (L452-453) is then redundant.

**CI operating system: Linux only for every tap test project.**
- `runs-on: ${{ matrix.os || 'ubuntu-latest' }}` (`.github/workflows/ci.yml` L60).
- ProxyTap runs in the "Extension Tests" entry (L81-86).
- Otlp, TcpTap and TcpTap.Bson3 run in "Remaining Unit Tests (auto-discovered)" (L79-80, L287-302; the skip list at
  L294 does not name them).
- Windows and macOS run only the template actions (L139-146).
- `release.yml` L37 runs only `tests/Kronikol.Tests`. `ci-summary-preview.yml` and `history-action.yml` run only the
  Example.Api CiPreview projects.
- R1's Windows branch (http.sys resets, no 408) is therefore never run by CI, and the Linux-only hang-up facts skip on
  Windows.

## 6. The wiki

`C:/Code/Kronikol.wiki` exists, but its checked-out `master` is at `e164dbc` (4.5.0) and is **52 commits behind**
`origin/master`. `origin/master` is at `60daf39`, which `git ls-remote` confirms is the remote HEAD. **Read the pages
with `git show origin/master:<file>`, never the working tree.** All four pages exist at `60daf39`:
Integration-ProxyTap-Extension.md (243 lines), Integration-Otlp-Extension.md (215), Integration-TcpTap-Extension.md (338)
and Diagnostics-and-Debugging.md (569).

### Integration-ProxyTap-Extension.md
- L19: "No ASP.NET Core host is required — the tap is built on `HttpListener` and binds `localhost` without URL ACLs."
  The page has no text on LAN binds beyond the `ListenHost` row at L107.
- L80, Stopping a tap: "`await tap.DisposeAsync()` stops listening, gives the port back, drains in-flight exchanges
  briefly (≤ 2 s) so their captures land, and releases the upstream client."
- L82-93, "A caller that hangs up":
  - L84-87: "Since **4.19.2** a caller that goes away is let go at once. On Linux and macOS the `HttpListener` behind the
    tap holds a connection until its response is closed or aborted, ... Windows (http.sys) drops such a request itself."
  - L89-90: "A request that never arrives whole, because its caller hung up or stopped sending mid-body, is not
    forwarded, not recorded and not counted. A caller still listening is answered `400 Bad Request`, as http.sys answers
    it on Windows."
  - L91: "A caller that hangs up while its answer is being written: the exchange is still recorded and counted as handled."
  - L92-93: "A forward that fails is answered `502 Bad Gateway` and counted in `ForwardFailures`, ... and
    `ProxyTap.Diagnostics()` reports the count".
- L125: "| `ForwardTimeout` / `ConnectTimeout` | 200 s / 5 s | Upstream deadlines. From 4.19.5 a forward that misses one
  is recorded as a failed call naming it".
- L157: TraceIdFromIdentity "counts those requests in `Diagnostics()` as `OptionNotApplied` and logs the first."
- L193: "The tap does this in a `using` covering every exit, so a failed forward cannot leave a stale identity behind."
- L194: "`MostRecentFor(serviceName)` | The identity of the **most recently started** request still in flight, or `null`
  when the service is idle."
- L209: "... for a timeout `proxy-tap: no answer from {host} within ForwardTimeout ({t})` or `proxy-tap: could not connect
  to {host} within ConnectTimeout ({t})`, then the chain."
- L210: "A forward cut short by the tap's own disposal is recorded as `proxy-tap: the tap stopped before {ServiceName}
  answered`."
- L212: "`ForwardFailures` counts the upstream's failures, not the tap stopping or a caller hanging up. `Diagnostics()`
  reports them on one `CaptureDegraded` line".
- L213: capture faults "counted on a `CaptureDegraded` line of its own".
- L236-243, Limitations:
  - L238: "HTTP/1.1 only (the `HttpListener` front end); HTTP/2 or gRPC-over-h2c hops need a different tee."
  - L240-242: on Linux and macOS a chunked request cut short reaches the service as if whole (dotnet/runtime#135680).
  - Nothing on stalls, the head, keep-alive, or slow callers.

### Integration-Otlp-Extension.md
- L15: "the receiver speaks HTTP/1.1 over a plain socket".
- L30: "1. **Authenticates** it against `ExpectedHeaders` (a per-start shared secret). Anything missing or different is
  answered `401` and counted — never forwarded, never mapped."
- L63: `o.ExpectedHeaders["x-kronikol-tap"] = sharedSecret;   // required off loopback`.
- L127: "| `ExpectedHeaders` | empty | Header name → exact value every request must carry; otherwise `401`. |"
- L129: "| `MaxRequestBytes` | 32 MiB | Bigger exports are answered `413` (and drained, not buffered). A chunk size past
  `int` is answered `413` too, and the connection closed, since a chunk that large is not drained (4.19.3). |"
- L143: the counters list, then: "A chunked export cut short or malformed is not received: like a `Content-Length` export
  cut short, it gets no answer and is neither counted nor mapped (4.19.3)."
- L149-160, "Listening interface and exposure":
  - L153: "**A non-loopback bind is an open port. Always pair it with a shared secret:**"
  - L160: "`MaxRequestBytes` and `QueueCapacity` bound the rest."
- L201-206, Limitations: traces only; HTTP/1.1 only (L204); fidelity. Nothing on time limits.
- The page documents no `Diagnostics()` line at all; L141's "Diagnostics callback" refers to `Log`. It says nothing of the
  30 s forward timeout (`OtlpTap.cs` L172).

### Integration-TcpTap-Extension.md
- L32: "... Capture can degrade; forwarding cannot stall. ... Half-close is propagated, so a client that shuts down its
  send side still receives the reply."
- L37: "... each is a `Log` line, an `OnCaptureDegraded` event and a `Diagnostics()` entry. A tap is never the reason a
  request fails."
- L148: "| `DecodingStallBytes` | `1 MiB` | Heuristic stall detector for `Diagnostics()` ...". The page's existing word
  "stall" means this decoding detector (also L300, L318), which collides with the plan's new "stall" names.
- L151: "| `ConnectTimeout` / `DrainTimeout` | 5 s / 2 s | Upstream connect; how long `DisposeAsync` waits for live
  connections so their captures land. |"
- L285: "**Capture loss is never silent.** A tap can lose *capture* (never forwarding) in a handful of ways".
- L289-300: the counters table. It lists no `StuckConnectionsReaped`.
- L304: "`TcpTapOptions.OnCaptureDegraded` ... is called on the decoder task as each event happens, with ... `Kind` one of
  `OversizePayloadSkipped`, `DecoderReset`, `DecodingDisabled`, `SegmentsDropped` (...), `ConnectionClosedMidMessage`."
- L306-318: example `Diagnostics()` lines (decoding disabled, oversize, decoder reset, segments dropped, closed
  mid-message, and the stall heuristic). No reaped line.
- L322-329, Limitations: OP_COMPRESSED, TLS, replica sets, exhaust cursors, resync, latency. Nothing on idle,
  half-closed or stalled pairs.
- **The reaper is not documented anywhere in the wiki.** `git grep` at `origin/master` for
  `ReapStuckConnectionsAfter|StuckConnectionsReaped|StuckConnectionReaped` finds nothing, though the option has shipped
  since 3.0.49 (`CHANGELOG.md` L8445, L8451). So the plan's §6.3 "the idle-pair rule stated next to the reaper" has
  nothing to sit next to. #174's §6.3 (L584-587) adds the reaper's option row, counter, kind and `Diagnostics()` example
  to this page when its R2 is published.

### Diagnostics-and-Debugging.md
- L10: "did a tap drop data".
- L110: "| `CaptureDegraded` | A **capture component** — a tap, a sink — lost or skipped data: a decoder that gave up on a
  connection, an oversize payload streamed past as a preview, export payloads dropped or refused, a forward that failed.
  Forwarding was never affected. ..."
- L145: "... a `ProxyTap` that answered `502` because the upstream was down."
- L154: the `HostDiagnostics = [.. redisTap.Diagnostics(), .. mongoTap.Diagnostics(), .. otlpTap.Diagnostics(), ...]`
  example.
- L158: "Every capture component exposes the same surface ... `ProxyTap`'s entry for requests it could not forward leaves
  out a caller that hangs up before its request has arrived whole, since nothing was forwarded (4.19.2)."
- L169: an example `--diagnostic "CaptureDegraded:tap-di-redis: ..."`.
- **The page has no list of each tap's `Diagnostics()` lines.** They live on the tap pages (TcpTap L306-318, ProxyTap
  L212-213), or nowhere (Otlp). The plan's §6.3 "the new `Diagnostics()` lines" here would start such a list.

### Statements that are false today, or that the plan makes false

**Already false:**
- ProxyTap L89-90, "stopped sending mid-body ... answered `400`". That holds only for a hang-up. A caller that stalls is
  held until disposal on Linux and then hears `200 OK` (plan RUN B1), and http.sys resets it at about 125 s.
- ProxyTap L194, "`null` when the service is idle". While a caller stalls reading its answer, the registration stays
  (`ProxyTap.cs` L310-314 covers `RespondAsync` at L489).
- ProxyTap L80, "≤ 2 s". The drain loop is bounded at 2 s, but the plan measured `DisposeAsync` at 3,028 ms for three
  stalled callers on Linux.
- ProxyTap L209, the `ConnectTimeout` wording. It is wrong for an infinite `ForwardTimeout` (R0 item 1).
- Otlp L30, "1. **Authenticates**". The code reads and allocates the body first: `ReadAsync` at L288 comes before
  `IsAuthenticated` at L318, and the array is allocated at `HttpRequestReader.cs` L154.
- Otlp L160, "`MaxRequestBytes` and `QueueCapacity` bound the rest". There is no connection cap and no time bound.
- TcpTap L37 ("never the reason a request fails") and L285 ("never forwarding"). The reaper closes connections.
- TcpTap L304. "Called on the decoder task" is false for the reaper (`TcpTap.cs` L366) and for dropped segments (L391).
  The kinds list omits `StuckConnectionReaped`.
- Diagnostics L110, "Forwarding was never affected". The forward-failure and reaper lines already use that kind.

**Made false or incomplete by the plan:**
- R0 item 2: ProxyTap L80, the disposal behaviour.
- R1: ProxyTap L89-91, L194 and Limitations L236-243; Diagnostics L110 and L158.
- R0 items 3-4 and R2: Otlp L30, L129, L143 and L160.
- R3: TcpTap L32, the half-close promise, which `HalfClosedTimeout` bounds; L37 and L285; L151; and the counters, kinds
  and `Diagnostics()` lists.

**Not on either plan's wiki list:** #174's §6.3 (L579-592) covers only the TcpTap and Otlp pages. Neither plan lists
Diagnostics L110.

## 7. Kronikol4J

`C:/Code/Kronikol4J` (local `main` is 21 commits behind `origin/main` `818b010`) has **no proxy tap, no OTLP receiver or
tap, and no TCP tap**. `git grep` at `origin/main` for `ProxyTap|OtlpTap|TcpTap|ServerSocket` hits only
`docs/REMAINING_PARITY.md`. `kronikol4j-proxy` is `TrackingProxy`, a `java.lang.reflect.Proxy` method tracker described
as "the idiomatic Java equivalent of the .NET DispatchProxy pattern" (`TrackingProxy.java` L25-37).

An **untracked** `docs/OTLP_TAP_PLAN.md` dated 2026-08-26 (not on `origin/main`) plans a `kronikol4j-otlp` port that would
hand-port `HttpRequestReader`. The ledger refers to it at L2034.

`docs/REMAINING_PARITY.md` exists (2,486 lines); its divergence ledger begins at L1877. Typical "nothing to port" lines:
- L2443: "**A proxy tap lets go of a caller that hangs up, and does not count one as a failed forward (.NET 4.19.2,
  2026-10-13, `PROXY_TAP_HANG_UP_PLAN.md` R1, #172).** Nothing to port: the port has no proxy tap. For whoever builds one:
  end every response the tap does not answer in one place that every exit passes through, ..."
- L2444: "**The OTLP tap reads an exporter that hangs up as a hang-up, and a chunked export cut short as no export (.NET
  4.19.3, ...).** Nothing to port: the port has no OTLP tap. For whoever builds one: ..."
- L2442 (4.19.1, the #171 SSRF fix): "Nothing to port: the port has neither a proxy tap nor an OTLP tap. For whoever
  builds one: forward to the base's scheme and authority ..."

The ledger never mentions `TcpTap`. #174 plans the wording "Nothing to port: the port has no network tap" (plan L598-599).

## 8. ROADMAP (`plans/ROADMAP.md` at `3240c983`)

- There is no mention of #173, #174 or caller-side time limits.
  - #171 and #172 appear only in row 1.28 (L260): "... released as each slice passed, ahead of #172 and #171".
  - Row 1.15 (L248) lists "`TcpTap` swallows an upstream connect timeout without a log line or a diagnostic" from
    `INGEST_FIDELITY_PLAN.md` §8, which is #174's subject.
  - Row 12b.3 (L500) has "the tap options for devices (`OtlpTapOptions.TestIdAttribute`, the LAN bind, TLS)".
- **Numbering.** Stage tables use `| # | Item | ... |` with `stage.N` (stage 1 "The patch train", L225-261, rows 1.1 to
  1.29). Other stages use `1b.N`, `1c.N`, `1d.N`, `2.N` to `14.N` and `12b.N`; decisions are `D1` to `D38`.
- **Row 1.26 is absent.** `ASSERTION_VALUE_SUBSTITUTION_PLAN.md` L682 says "1.26 is the row PR #164, a cloud session's
  4.15.0 in review, took on 2026-10-11". The next free stage-1 number is **1.30**.
- A row also needs an entry in §7's issue map (L645-647, e.g. `#170 → 1.28 · #168 → 1.29`) and the Appendix B.1
  register (header L785, rows L789-792).
- **Recent row**, L261: "| 1.29 | **Done: R1 4.19.6, R2 4.20.0, R3 4.20.1 and the driver floor 4.20.2, 2026-10-13.**
  **#168** MongoDB's notes (`MONGODB_DETAILED_PAYLOADS_PLAN.md`): at `Detailed` a write was drawn with no payload ... R1 (a
  patch) writes every reply as relaxed extended JSON ... R2 (a minor) records what each command sent ... R3 (a patch)
  makes the query tool read what the report reads, and caps a phase variant's content | #168. RUN: the plan's harness
  (published 4.18.1, a real server on both drivers) and its `r1/` | Green-lit in full by the owner on 2026-10-13, every
  question in §9 as recommended; no decision number (Q15) |"
- **Consistency with the siblings.** #171 and #172 took no row. #174's Q7, green-lit as recommended, takes none: "Q7. A
  `ROADMAP.md` row? **Recommended: none,** as for #171 and #172" (L637). #173's Q12 recommends a row, and its
  PLANS_STATUS row says "No `ROADMAP.md` row (Q12)".

## 9. Disclosure

**`SECURITY.md` is at the repo root**; there is none in `.github/`.
- L14: "**Do not open a public GitHub issue for security vulnerabilities.**"
- L16: email the owner or use GitHub's private vulnerability reporting
  (`https://github.com/lemonlion/Kronikol/security/advisories/new`).
- L25: "coordinate a fix before any public disclosure."
- Its Supported Versions table (L5-8) still lists only `2.0.x` as supported and `1.x` as not, which is stale at 4.20.2.
- L29 scopes Kronikol as "a **test-time library** ... not intended for production runtime use."

**How #171 handled its SSRF** (`plans/PROXY_TAP_FAILED_FORWARD_PLAN.md`):
- L7-10: "**Disclosure note.** Section 3.5 describes a security defect found while writing this plan: a request whose
  target started with `//` (or, through `ProxyTap`'s listener, `/\`) was forwarded to whatever host it named, by
  `ProxyTap` and by `OtlpTap` in forward mode (which also took an absolute-form target). 4.19.1 fixes it, and this file
  was committed once that release was on nuget.org (Q8)."
- L217-222: "**Security (SSRF).** Anyone who can reach the tap's port can make it fetch any host the tap's machine can
  reach. `ListenHost` defaults to `localhost`, but a suite that drives devices through a tap (#170's reporter does) binds
  a LAN address, and on a CI VM the reachable hosts include the cloud metadata endpoint. ..."
- L486: "| **R0** | ... | patch (a security fix, nothing new to call) | nothing; ship first (Q8) |"
- L667 (Q8): "**Ship R0 first**, ahead of the agreed order, with a GitHub security advisory if you want one. Binding to
  `localhost` does not close it (a browser page can send a `//` path, 3.5), which argues for the advisory. Keep this file
  uncommitted until R0 is published, or commit it without section 3.5, 4.7, R0's facts and this row".
- L676: "Both peers keep R0's details out of anything public, and their plans say only that a peer release may go first."

**What actually happened.**
- **No advisory was created.** `gh api repos/lemonlion/Kronikol/security-advisories` returns `[]`. The query ran as
  `lemonlion`, with admin rights, so drafts would have shown.
- The plan was committed at `c28e8948` (2026-10-13 17:09) after the release commit `4de3ee16` (16:45). The commit message
  says "committed once its R0, 4.19.1, was on nuget.org".
- Public disclosure was the changelog. `CHANGELOG.md` L606-612 (4.19.1): "It is a security fix: anyone who could reach a
  tap's port could make it fetch another host and read the answer, so upgrade any tap that listens where others can
  reach it."

## 10. Other worktrees (`git status --short`; `git diff --stat 3240c983`)

Every worktree is at `3240c983`.

| Worktree | Branch | Changes |
|---|---|---|
| Kronikol-165 | plan/165-weaver-deterministic-paths | M `plans/PLANS_STATUS.md` (+1 row), AssertionTracking `src/` and tests, three core report tests; ?? the weaver plan, `SourceDocuments.cs` and a new test. No tap, ROADMAP or tap-test change. |
| Kronikol-168-audit | audit/168 | clean |
| Kronikol-169-audit | audit/169 | clean |
| **Kronikol-174** | plan/174-tcptap-failed-connect | M `plans/PLANS_STATUS.md` (+1), `src/Kronikol.Extensions.Otlp/Kronikol.Extensions.Otlp.csproj` (+7), `src/Kronikol.Extensions.Otlp/OtlpTap.cs` (55 lines), `src/Kronikol.Extensions.TcpTap/TcpTap.cs` (58 lines); ?? `TapAcceptLoop.cs`, `tests/Kronikol.Tests.Otlp/OtlpTapAcceptTests.cs`, `tests/Kronikol.Tests.TcpTap/TcpTapAcceptTests.cs`, and the plan with its harness. **The only worktree touching the taps or their tests.** No ROADMAP change. |
| Kronikol-175 | plan/175-attachment-merge | M `plans/PLANS_STATUS.md` (+1); ?? the attachment-merge plan and harness |
| **Kronikol-78** | plan/78-124-changed-scenarios | M `plans/PLANS_STATUS.md` (+1) and **`plans/ROADMAP.md` (15 lines)**; ?? the changed-scenarios plan and harness |

Kronikol-78's ROADMAP edits:
- the header at L3 ("**Amended 2026-10-14:** `CHANGED_SCENARIOS_PLAN.md` written as 7.2 ... no new row and no decision
  number");
- D8 (L152);
- stage 7 (L412-420);
- the §7 issue map, `#78, #124 → 7.4 (plan 7.2)` at L645;
- a new Appendix B.1 register row right after the MONGODB_DETAILED_PAYLOADS row (L792).

It takes no stage-1 number. A #173 roadmap row would touch the same §7 line and the same B.1 spot.

All five plan worktrees, Kronikol-173 included, insert their PLANS_STATUS row at the same place, right under the table
header (`@@ -19,0 +20` or `-20,0 +21`), so each merge after the first conflicts there trivially.

## 11. The #174 plan (`C:/Code/Kronikol-174/plans/TCPTAP_FAILED_CONNECT_PLAN.md`, 684 lines, uncommitted)

**Status.** L4-5: green-lit in full on 2026-10-15. The §11 log (L662-667) records: "kronikol-51 confirmed that #173 is
not being implemented, so this plan adds `TryLog` to both taps. Neither plan's F3 list had the start and stop lines
(`TcpTap.cs:162`, 715; `OtlpTap.cs:182`, 210, 611), so this plan takes them". It adds no option (L55).

### R1, a patch, being implemented now

Plan text §4.1 (L234-269); diff in the worktree.

**`TcpTap.cs`:**
- The constructor creates `Accepting = new TapAcceptLoop(line => TryLog($"[{_options.DisplayName}] tcp-tap {line}"))`.
- New `internal TapAcceptLoop Accepting { get; }`.
- The start line (L162) goes through `TryLog`.
- `AcceptLoopAsync` (L181-199) becomes `Accepting.RunAsync(listener, client => { Interlocked.Increment(ref
  _connections); _ = Task.Run(() => HandleConnectionAsync(client, ct), ...); }, ct)`.
- `Diagnostics()` gains the accept line **first** in the list.
- New `private void TryLog(string message)`, placed right after `Diagnostic` (L690).
- The stop line (L715-718) goes through `TryLog` and ends `..., {DecodingDisabledConnections} connections with decoding
  disabled, {Accepting.Failures} failed accepts)`.
- `Diagnostic` (L690) and L253 are untouched so far.

**`OtlpTap.cs`:**
- The same `Accepting` member and the accept line first in `Diagnostics()`.
- The start line (L182) and the "could not also bind" line (L210) go through `TryLog`.
- `AcceptLoopAsync` (L259-275) becomes `Accepting.RunAsync(listener, client => _ = Task.Run(() => ServeAsync(client, ct),
  CancellationToken.None), ct)`.
- The stop line (L611-613) ends `..., {ForwardFailures} forward failures, {Accepting.Failures} failed accepts)`.
- `TryLog` is added after `DisposeAsync`.
- `ServeAsync` (L277-306), `NoDelay` (L281-282), the catch at L298-301, `HandleAsync` and `MapPayload` are **untouched**.

**`TapAcceptLoop`** (internal) has the seams `Accept`, `Pause` (10 ms doubling to 1 s, waiting on the tap's token) and
`Clock` (a `TimeProvider`), plus `Failures`, `DiagnosticLine` and `RunAsync`.

**The `Diagnostics()` line** (L253-255): "`{name}: {n:N0} accept(s) failed, the first at HH:mm:ssZ ({exception type}
{SocketError}: {message}); the tap kept listening and tried again, so callers that arrived meanwhile waited in the listen
queue, or found it full`".

**Log lines:** "accept on X failed (...); still listening, and trying again with pauses of up to 1 s", then at most one
"still failing" line a minute, then "accepting again ... after N failed accept(s) since HH:mm:ssZ", and "stopped
accepting on X: its listening socket was closed ...".

### R2, a minor (§4.2, L271-362)

- **Edit sites:** `TcpTap.cs` L201-254 (up to the pumps), L529-580 and L715-718, plus `CaptureDegradation.cs` L3-30,
  `TcpTapOptions.cs` L58, L104-105 and L136-143, and `DiagnosticEntry.cs` L41-46 (Appendix A L673-682).
- **New public members:** `TcpTap.UpstreamConnectFailures`, `TcpTap.AcceptFailures`, `OtlpTap.AcceptFailures`, and
  `CaptureDegradationKind.UpstreamConnectFailed` and `.AcceptFailed`, "appended after `StuckConnectionReaped`" (L312-320).
- **New internals:** `_upstreamConnectFailures`, `_upstreamConnectTimeouts` and `CountConnectFailure`.
- **Log line:** "`[{name}] tcp-tap connection {id} from {caller}: could not connect to {ForwardHost}:{ForwardPort} within
  ConnectTimeout ({t}); closed it without forwarding anything`", or with `({type} {SocketError}: {message})` in place of
  the timeout clause.
- **`Diagnostics()` line:** "`svc→redis: 12 connection(s) could not be forwarded, because the tap could not connect to
  localhost:6379 (9 failed, the last with SocketException ConnectionRefused: Connection refused; 3 got no connection
  within ConnectTimeout 00:00:05), the first at 14:03:35Z; the tap closed each one, so svc's commands on them never
  reached redis`".
- **Stop line:** adds the failed connects.
- **Outer catch** (item 6, L341-344): "`catch (Exception) when (ct.IsCancellationRequested)` replaces `TcpTap.cs:247`'s
  filter ... (This line is shared with #173; see §4.4.)"
- **A throwing factory** (item 7, L346-350): handled by `DisableDecoding(...)`; the tap "starts the pumps with no decoder
  ... and starts no reaper".
- **L253** goes through `TryLog` (item 8).
- **Doc fixes** (item 9, L356-358): "`CaptureDegradationKind`'s summary, `CaptureDegraded`'s, the "invoked on" sentence
  of `OnCaptureDegraded`, `ConnectTimeout`'s doc ..., the `Log` option's doc, and the class remarks".

### R3, a patch (§4.3, L364-379; taken with approach (a) at L664)

- `ListenHost` "localhost" also binds `::1`, with one R1 loop per listener and both stopped at disposal (`TcpTap.cs`
  L149-199 and L693-720).
- The upstream connect tries IPv4 addresses first.

### The split with #173: §4.4 (L381-413), quoted

- "**#174 owns** the accept loops of both taps; `HandleConnectionAsync` up to the pumps (the connect, the factory and the
  outer catches); and the accept and connect lines in `Diagnostics()`."
- "**#173 owns** everything after both sockets are open: the pumps (`TcpTap.cs:271-321`), half-close, the pair's dispose
  and the reaper, plus `OtlpTap`'s `ServeAsync` and `HttpRequestReader`."
- "**Shared:** `TcpTap.cs:247-254` (R2 items 6 and 8), the `Diagnostics()` list and the stop line. Each plan adds its own
  lines, and whichever lands second rebases."
- "**F1, a negative `BodyCapBytes`** ... **#173 takes it** ... **F2** ... **#174 takes it** ... **F3, unguarded `Log`
  calls.** **#173 takes** `TcpTap.cs:257`, 365, 383, 447, 466, 478, 495, 517 and 672 ..., `OtlpTap`'s outside its accept
  loop (among them `MapPayload`'s catch at L526 ...), and `ProxyTap`'s ... **#174 takes** L253 and every line it adds."
- "Whichever plan lands first adds it [`TryLog`] to `TcpTap`, and the other uses it rather than adding a second." The
  same rule for OtlpTap: "#173 takes the per-connection ones". `TcpTap.Diagnostic` (L690) "is on #173's side of the split
  (decoding)."

§4.5, L423-424: "**Owned by #173:** caller-side time limits, a pair held after a half-close, and **a cap on connections at
the accept loop (#173's open question)**."

§7, L602-607: "**#173** (kronikol-51) shares the lines in §4.4. Either can land first, and the second rebases."

### Interactions with #173

**With R0:**
- `TryLog` already exists in TcpTap and OtlpTap after #174 R1, so #173 R0 item 6 adds it to **ProxyTap only**. The
  sites still left to #173: TcpTap L257, 365, 383, 447, 466, 478, 495, 517, 672 and 690; OtlpTap L304, 313, 321, 341, 409,
  448 and 526; and all of ProxyTap's.
- OtlpTap `NoDelay` (L281-282): not touched by #174. Lines shift by roughly 8.
- OtlpTap `DisposeAsync`: #174 changes only the stop line at its end and adds `TryLog` after it. #173's top-of-method
  heartbeat stop and its map-loop fix (L595-607) do not overlap.
- **Duplicate:** #173 R0 item 8 (`CaptureDegradation.cs` L3; `OnCaptureDegraded`'s "never on a byte pump",
  `TcpTapOptions.cs` L139-141) is the same work as #174 R2 item 9, which also fixes `DiagnosticEntry.cs` L41-46.

**With R2:**
- `ServeAsync`, per-connection handling and the catch at L298-301: not touched by #174.
- #174's rewritten `AcceptLoopAsync` lambda calls `ServeAsync(client, ct)`, so a signature change in R2 edits #174's line.
- The `accepted` callback runs **outside** `TapAcceptLoop`'s try (`TapAcceptLoop.cs` L134). Anything R2 registers there
  and that throws ends the accept loop for good, so per-connection registration belongs inside `ServeAsync`.
- #174 adds no connection tracking and no cap, and its §4.5 assigns the cap to #173. **#173 lists caps as not in its
  plan (§4.8 L443, §8 L562-565) and asks no question about them, so neither plan owns the cap.**

**With R3:**
- The pair's release (#173 R3 at L241-261) overlaps #174 R2's L201-254: the outer catch at L247, `TryLog` at L253, and
  the reaper start at L237-239, which R2 skips when the factory threw.
- After R2 the outer catch matches only `ct.IsCancellationRequested`. An `OperationCanceledException` from a **pair**
  token that ever reached it would be logged by the catch-all as a connection error.
- The reaper: #173 moves it onto the watch; #174 R2 must start none when there is no decoder.
- `Diagnostics()`: #174's accept line comes first (done in the worktree) and its connect line follows (R2). #173's two
  lines come after, and the second plan to land rebases.
- The stop line: #174 appends failed accepts (R1) and failed connects (R2). #173 R3 appends its counters.
- `CaptureDegradationKind`: #174 appends two members; #173 adds none.
- The wiki: #174 §6.3 (L579-592) adds the reaper's row, counter, kind and `Diagnostics()` example to the TcpTap page. That
  is the anchor #173's "next to the reaper" needs, so #173's wiki edit follows #174 R2's publication.
- Tests: #174 adds `ManualClock : TimeProvider` to the `Kronikol.Tests.TcpTap` namespace, so #173 should reuse it or
  avoid that name. #174's §5 (L434-436) notes that CI runs these projects only on Linux: "Every fact is therefore also
  run on Windows before each release."
