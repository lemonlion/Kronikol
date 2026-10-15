# Prior art: caller-side time limits for in-process taps (#173)

Research for `plans/TAP_CALLER_TIME_LIMITS_PLAN.md`, read on 2026-10-14. Every fact carries a link: source code as a
file path with line numbers at a named tag, documentation by URL and anchor. Labels: **SOURCE** (code read at the tag
named), **DOC** (the project's own documentation), **ISSUE** (a GitHub issue or pull request), **INFERENCE** (my
reading, not stated by the source).

## What was read

| Project | Ref read | Notes |
|---|---|---|
| dotnet/aspnetcore | tag `v10.0.13` (commit `0ef4bbfa`, 2026-09-21, newest release/10.0 servicing tag) | Kestrel core, socket transport, request-timeouts middleware, SignalR. Compared with `main` at `f4777b2d`: `Heartbeat.cs` identical; `TimeoutControl.cs` differs only in two comment typos, so its line numbers hold on main. |
| dotnet/runtime | tag `v10.0.13` (`6aa72daa`); tag `v8.0.32` for .NET 8 | System.Net.Sockets, CoreLib `Stream.cs` and `Debugger.cs` |
| dotnet/yarp | tag `v2.3.0` (`4154b6d1`, newest release) | `main` at `2aa3d835` has the same code a few lines lower |
| haproxy/haproxy | tag `v3.4.0`, `doc/configuration.txt` | rendered at docs.haproxy.org/3.4 (3.4.6-69), same text |
| nginx/nginx | tag `release-1.31.6`; nginx.org docs | |
| envoyproxy/envoy | tag `v1.40.0` (API protos, `source/common/tcp_proxy`) | |
| Shopify/toxiproxy | tag `v2.12.0` (`3ccd6a79`) | |
| mitmproxy/mitmproxy | tag `v12.2.3` | |
| wiremock/wiremock | tag `3.13.2`; jetty/jetty.project branch `jetty-11.0.x` at `5c8ce313` | |
| wiremock/WireMock.Net | tag `2.19.0` | |
| testcontainers-java `2.0.5`, testcontainers-dotnet `4.16.0`; socat man page | | |

## 0. Summary for the plan

**Debugger.** Kestrel suppresses every timeout its `TimeoutControl` enforces while a managed debugger is attached: keep-alive,
request headers, body drain, `IConnectionTimeoutFeature` and both minimum data rates (`TimeoutControl.cs` L62, L123,
L157, section 1.1). The check runs when a deadline is evaluated on each 1-second tick; it does not move the deadline,
so one that passed while the debugger was attached fires on the first tick after it detaches. Separately, with or
without a debugger, the two rate checks do not count a tick gap longer than one heartbeat interval ("overly long tick
intervals are the result of server resource starvation"); the fixed deadlines (keep-alive, headers) get no such
allowance. ASP.NET Core's request-timeouts middleware and SignalR's handshake and client timeouts follow the same
debugger rule; YARP's `ActivityTimeout` and Kestrel's HTTP/2 ping timeout do not. mitmproxy disarms its idle watchdog
while a hook (including a flow a user has intercepted) is pending and restarts the idle clock afterwards.

**Shape of the limits.** Every product separates a *total* budget for the request head (Kestrel 30 s, nginx 60 s
counted from accept for a connection's first request, HAProxy `timeout http-request` with no default, Envoy `request_headers_timeout` off by default) from
*inactivity* limits for bodies and tunnels that any progress resets (nginx 60 s per read or per write, YARP 100 s, Envoy
stream idle 5 min, Jetty 30 s, mitmproxy 600 s, nginx stream 10 min, Envoy TCP 1 h). Kestrel uses minimum rates instead
(240 B/s after a 5 s grace) and times only while it is waiting on the peer. Nobody puts a total deadline on a body by
default (Envoy's `request_timeout` exists but is off "as it is not compatible with streaming requests").

**Half-closed TCP.** HAProxy has dedicated `timeout client-fin` / `server-fin`, unset by default (falling back to the
client, server or tunnel timeout); its manual's example pairs `client-fin 30s` with `tunnel 1h`. socat, which
Testcontainers runs, waits 0.5 s after the first EOF and then closes both sides. nginx stream ends the session on the
first EOF by default; with `proxy_half_close on` the pair is held until both sides close or 10 min (`proxy_timeout`)
pass with no data. Envoy's TCP proxy propagates the half-close and relies on its 1 h idle timeout. Toxiproxy turns an
EOF into a full close of the other socket and has no timers at all.

**What is sent.** A head that does not complete gets 408 (Kestrel, HAProxy; nginx logs 408 but sends nothing). A
stalled request body gets 408 (Kestrel if the response has not started, Envoy stream idle before upstream headers, YARP
when reading the client's body timed out). An idle keep-alive connection is just closed (Kestrel, HAProxy, nginx). A
client that stops reading is aborted (Kestrel's transport sends RST by default; nginx closes). A slow upstream gets 504
(YARP, HAProxy).

**.NET cancellation.** Since .NET Core 3.0 (corefx#36516) the token passed to `Socket.ReceiveAsync/SendAsync` and
`NetworkStream.ReadAsync/WriteAsync` cancels a *pending* operation (CancelIoEx on Windows, dequeue on Unix) and
surfaces as `OperationCanceledException`; the code is the same at v8.0.32 and v10.0.13. Neither the .NET docs nor
CancelIoEx promise a usable socket afterwards (a send can have gone out in part; on Windows a cancelled TCP receive
cancels every receive pending on the socket), so a timed-out connection should be closed. On Unix, disposing a socket
with queued async operations makes .NET close it abortively (RST). `ReadTimeout`/`ReceiveTimeout` do not apply to async
calls, and the base `Stream` async overloads check the token once and never again.

## 1. Kestrel and the debugger

### 1.1 The checks (SOURCE, aspnetcore v10.0.13)

`TimeoutControl` reads the debugger through a seam:
[`TimeoutControl.cs` L44](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L44)
`internal IDebugger Debugger { get; set; } = DebuggerWrapper.Singleton;`, with
[`DebuggerWrapper.cs` L8-16](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/DebuggerWrapper.cs#L8-L16)
`public bool IsAttached => Debugger.IsAttached;` (System.Diagnostics) and
[`IDebugger.cs` L6-9](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/IDebugger.cs#L6-L9).

The three checks
([L60-73](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L60-L73),
[L123](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L123),
[L157](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L157)):

```csharp
// TimeoutControl.cs L60-73
private void CheckForTimeout(long timestamp)
{
    if (!Debugger.IsAttached)
    {
        if (timestamp > Interlocked.Read(ref _timeoutTimestamp))
        {
            var timeoutReason = TimerReason;

            CancelTimeout();

            _timeoutHandler.OnTimeout(timeoutReason);
        }
    }
}

// L123, request body rate
timeout = rate < _minReadRate.BytesPerSecond && !Debugger.IsAttached;

// L157, response rate
timeout = _concurrentAwaitingWrites > 0 && timestamp > _writeTimingTimeoutTimestamp && !Debugger.IsAttached;
```

`CheckForTimeout` serves the single-deadline reasons and the other two the rates;
[`TimeoutReason.cs` L6-15](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutReason.cs#L6-L15)
lists all six (KeepAlive, RequestHeaders, ReadDataRate, WriteDataRate, RequestBodyDrain, TimeoutFeature), so all six are
suppressed. A fourth check,
[`Heartbeat.cs` L54-62](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Heartbeat.cs#L54-L62)
`if (!_debugger.IsAttached)`, only gates the "heartbeat slow" warning.

A grep of `src/Servers/Kestrel/Core/src` at v10.0.13 finds `IsAttached` only in those three files, so the HTTP/2 ping
timeout
([`Http2KeepAlive.cs` L77-82](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http2/Http2KeepAlive.cs#L77-L82))
is not suppressed. It is off by default: `KeepAlivePingDelay` defaults to `TimeSpan.MaxValue`
([DOC](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0#http2-keep-alive-ping-configuration)).

Kestrel's tests drive the seam with a mock `IDebugger`:
[`TimeoutControlTests.cs` L25 `DoesNotTimeOutWhenDebuggerIsAttached`, L40 `DoesNotTimeOutWhenRequestBodyDoesNotSatisfyMinimumDataRateButDebuggerIsAttached`](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/test/TimeoutControlTests.cs#L25-L50),
[`HeartbeatTests.cs` L145 `HeartbeatTakingLongerThanIntervalIsNotLoggedIfDebuggerAttached`](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/test/HeartbeatTests.cs#L145).

**DOC.** The Kestrel options page, section
[Behavior with debugger attached](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0#behavior-with-debugger-attached):
"The following timeout and rate limit options aren't enforced when a debugger is attached to a Kestrel process:"
`KeepAliveTimeout`, `RequestHeadersTimeout`, `MinRequestBodyDataRate`, `MinResponseDataRate`,
`IConnectionTimeoutFeature`, `IHttpMinRequestBodyDataRateFeature`, `IHttpMinResponseDataRateFeature`. The
[Keep-alive timeout](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0#keep-alive-timeout)
and
[Request headers timeout](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0#request-headers-timeout)
sections each say "This timeout is not enforced when a debugger is attached to the Kestrel process."

**ISSUE.** Added in 2017:
[aspnet/KestrelHttpServer#1884](https://github.com/aspnet/KestrelHttpServer/issues/1884) "Ensure timeouts are disabled
when debugger is attached" (opened 2017-06-02), fixed by
[PR #1920](https://github.com/aspnet/KestrelHttpServer/pull/1920) "Do not enforce timeouts when the debugger is
attached" (merged 2017-06-28, commit `9b4be69e`), which added `IDebugger` and `DebuggerWrapper`.

**What `Debugger.IsAttached` means.** CoreCLR
[`Debugger.cs` L61-66 at runtime v10.0.13](https://github.com/dotnet/runtime/blob/v10.0.13/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs#L61-L66):
"Returns whether or not a managed debugger is attached to the process.", implemented as a QCall marked
`SuppressGCTransition`, cheap enough for every tick. INFERENCE: it is per process, so a debugger attached to a
different process (a system under test running out of process, say) is not seen, nor is a native-only debugger.

### 1.2 The same rule elsewhere, and where it is absent

- ASP.NET Core request-timeouts middleware:
  [`RequestTimeoutsMiddleware.cs` L29-34](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Http/Http/src/Timeouts/RequestTimeoutsMiddleware.cs#L29-L34)
  returns `_next(context)` untouched when `Debugger.IsAttached` (SOURCE).
  [DOC](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-10.0): "When an app is
  running in debug mode, the timeout middleware doesn't trigger. This behavior is the same as for Kestrel timeouts. To
  test timeouts, run the app without the debugger attached." Its default status on timeout is 504 (same page).
- SignalR:
  [`HubConnectionContext.cs` L537-543](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/SignalR/server/Core/src/HubConnectionContext.cs#L537-L543)
  arms the handshake timeout (`cts.CancelAfter(timeout)`) only when no debugger is attached;
  [L741-746](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/SignalR/server/Core/src/HubConnectionContext.cs#L741-L746)
  `CheckClientTimeout` returns at once when `Debugger.IsAttached` (SOURCE).
- YARP, the opposite choice ([DOC](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0#debugging-considerations)):
  "Route-level Timeout does not apply when a debugger is attached to the process" but "ActivityTimeout always applies,
  even when a debugger is attached." The page advises raising it "To avoid ActivityTimeout interfering with debugging
  or legitimate long-running requests".
- mitmproxy, the same need met without a debugger check (SOURCE): `TimeoutWatchdog.disarm()`
  ([`server.py` L78-88](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/server.py#L78-L88)) stops
  the inactivity timer, and when the last blocker leaves calls `register_activity()` before re-arming, so the idle
  clock restarts from zero. `ProxyConnectionHandler.handle_hook`
  ([`mode_servers.py` L69-75](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/mode_servers.py#L69-L75))
  wraps every hook in `disarm()`, including `await data.wait_for_resume()` for a flow a user has intercepted.

### 1.3 The heartbeat and how each kind is measured (SOURCE)

- [`Heartbeat.cs` L11](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Heartbeat.cs#L11)
  `Interval = TimeSpan.FromSeconds(1)`;
  [L30-34](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Heartbeat.cs#L30-L34)
  a dedicated background thread named "Kestrel Timer" (not the thread pool);
  [L70-78](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Heartbeat.cs#L70-L78)
  `while (!_stopEvent.Wait(_interval)) OnHeartbeat();`;
  [L43-68](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Heartbeat.cs#L43-L68)
  runs every callback, then logs `HeartbeatSlow` if the pass took longer than the interval.
- Fan-out:
  [`ConnectionManager.cs` L36-44](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/ConnectionManager.cs#L36-L44)
  walks the connections,
  [`KestrelConnection.cs` L44-58](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/KestrelConnection.cs#L44-L58)
  calls each handler,
  [`HttpConnection.cs` L111](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/HttpConnection.cs#L111)
  registers and
  [L260-272](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/HttpConnection.cs#L260-L272)
  reads `_timeProvider.GetTimestamp()` and calls
  [`TimeoutControl.Tick` L51-58](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L51-L58):
  the three checks, then `_lastTimestamp = timestamp`.
- Clock: `TimeProvider.GetTimestamp()`;
  [DOC](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider.gettimestamp?view=net-10.0) "The default
  implementation returns Stopwatch.GetTimestamp()". INFERENCE: a monotonic counter, so wall-clock changes do not
  matter, while a process pause does (the counter keeps running while the process is stopped).

**Single deadlines** (keep-alive, request headers, body drain, timeout feature).
[`AssignTimeout` L186-193](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L186-L193)
sets the deadline to last tick + timeout + one interval ("Add Heartbeat.Interval since this can be called right before
the next heartbeat"); `CheckForTimeout` fires on the first tick past it. INFERENCE: the effective limit is between the
timeout and the timeout plus 2 s.

**Request body rate** (`MinRequestBodyDataRate`).
- Timed only while the application awaits a body read that is not already satisfied:
  [`MessageBody.cs` L203-225](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/MessageBody.cs#L203-L225)
  starts timing only when `!readAwaitable.IsCompleted`, and
  [L250-259](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/MessageBody.cs#L250-L259)
  stops it. Time the app spends between reads does not count.
- Each tick adds `Math.Min(timestamp - _lastTimestamp, _heartbeatIntervalTicks)`
  ([L112-114](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L112-L114));
  after the grace period
  ([L118-124](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L118-L124))
  the rate is total bytes over total timed seconds: an average over the time spent waiting, not a window (test
  [L92 `RequestBodyDataRateIsAveragedOverTimeSpentReadingRequestBody`](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/test/TimeoutControlTests.cs#L92)).
- Not enforced while a single deadline is set (the body drain,
  [L77-83](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L77-L83))
  or while HTTP/2 connection flow control is starved
  ([L85-101](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L85-L101)).
- [`MinDataRate.cs` L26-29](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/MinDataRate.cs#L26-L29)
  rejects a grace period not longer than the heartbeat interval.
  [DOC](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0#minimum-request-body-data-rate):
  "Kestrel checks every second if data is arriving at the specified rate in bytes/second. If the rate drops below the
  minimum, the connection is timed out. The grace period is the amount of time Kestrel allows the client to increase
  its send rate up to the minimum. The rate isn't checked during that time. The grace period helps avoid dropping
  connections that are initially sending data at a slow rate because of TCP slow-start."

**Response rate** (`MinResponseDataRate`).
- Per write, not averaged
  ([`KestrelServerLimits.cs` L371-381](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs#L371-L381)):
  "Whenever the server writes a chunk of data, a timer is set to the maximum of the grace period set in this property
  or the length of the write in bytes divided by the data rate ... The connection is aborted if the write has not
  completed by the time that timer expires." Implementation:
  [`BytesWrittenToBuffer` L284-310](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L284-L310).
- Timed only while a flush is actually blocked:
  [`TimingPipeFlusher.cs` L51-82](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/PipeWriterHelpers/TimingPipeFlusher.cs#L51-L82)
  calls `StartTimingWrite` only when `FlushAsync` did not complete synchronously.
- `main` adds
  ([`KestrelServerLimits.cs` L382-383 at f4777b2d](https://github.com/dotnet/aspnetcore/blob/f4777b2dc73cefe741c2cd8d36341b62caf2cfd8/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs#L382-L383)):
  "This rate may not accurately measure how much data a client is reading. Data is immediately copied into operating
  system buffers until those are full, and the rate only applies to the application buffers."

### 1.4 Pauses and large gaps

- Rates (SOURCE): a long gap between ticks is not counted. Read side
  [L112-114](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L112-L114):
  "Assume overly long tick intervals are the result of server resource starvation. Don't count extra time between
  ticks against the rate limit." Write side
  [L148-155](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L148-L155)
  pushes the write deadline forward by the extra time. Tests
  [L332 `ReadTimingOnlyCountsUpToOneHeartbeatIntervalPerTick`](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/test/TimeoutControlTests.cs#L332)
  and
  [L477 `WriteTimingOnlyCountsUpToOneHeartbeatIntervalPerTick`](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/test/TimeoutControlTests.cs#L477).
- Single deadlines (SOURCE): no such allowance;
  [L60-73](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/TimeoutControl.cs#L60-L73)
  compares the tick's timestamp with the deadline.
- INFERENCE, what follows for a paused test process:
  - Stopped at a breakpoint with a debugger attached: the Kestrel Timer thread stops with the rest of the process, so
    the next tick sees the whole pause as one gap. The rates discount it; passed deadlines do not fire because the
    debugger is still attached.
  - After the debugger detaches: nothing moved the deadlines, so any that passed fire on the next tick.
  - Paused with no debugger (SIGSTOP, a suspended VM): the rates still discount the gap, but keep-alive and header
    deadlines that passed fire on the first tick after resume.

## 2. Kestrel defaults and what each sends

| Limit | Default | Kind | What the caller sees when it fires |
|---|---|---|---|
| `KeepAliveTimeout` | 130 s | from the start of each request loop to the first byte of a request | close with no response |
| `RequestHeadersTimeout` | 30 s | total, first byte of the request to the end of the headers | `408 Request Timeout`, `Connection: close`, then close |
| `MinRequestBodyDataRate` | 240 B/s, 5 s grace | average over the time spent waiting for body bytes | the app's body read throws `BadHttpRequestException` (408); a 408 with `Connection: close` if the response has not started; on HTTP/2 the connection is aborted |
| `MinResponseDataRate` | 240 B/s, 5 s grace | deadline per blocked write | connection aborted (RST by default) |
| body drain (internal) | 5 s | total, draining an unread body after the app finished | connection aborted |

Evidence (SOURCE unless marked):
- Defaults:
  [`KestrelServerLimits.cs` L37, L39](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs#L37-L39)
  (`TimeSpan.FromSeconds(130)`, `TimeSpan.FromSeconds(30)`);
  [L358-360](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs#L358-L360)
  and
  [L382-384](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs#L382-L384)
  `new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5))`, commented "Matches the default IIS
  minBytesPerSecond"; the body drain is
  [`Constants.cs` L32](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Constants.cs#L32)
  `RequestBodyDrainTimeout = TimeSpan.FromSeconds(5)`, armed at
  [`Http1MessageBody.cs` L88](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1MessageBody.cs#L88).
  DOC: [API page](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.keepalivetimeout?view=aspnetcore-10.0)
  "Defaults to 130 seconds."; the [options page](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-5.0)
  for the 5.0 moniker still says "Defaults to 2 minutes", so cite the source or the API page, not that page. Rates:
  "The default minimum rate is 240 bytes/second with a 5-second grace period." (same page, 5.0 moniker; the 10.0 text
  gives no number, the source does).
- Arming: keep-alive at the start of each request loop,
  [`Http1Connection.cs` L707-713](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L707-L713);
  replaced by the header timeout at the first byte of a request,
  [L171-184](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L171-L184);
  cancelled when the headers are parsed,
  [L252-256](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L252-L256).
- Dispatch:
  [`HttpConnection.OnTimeout` L274-301](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/HttpConnection.cs#L274-L301):
  KeepAlive calls `StopProcessingNextRequest`, RequestHeaders `HandleRequestHeadersTimeout`, ReadDataRate
  `HandleReadDataRateTimeout`, WriteDataRate and RequestBodyDrain/TimeoutFeature `Abort(...)`.
- Keep-alive close:
  [`Http1Connection.cs` L134-138](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L134-L138)
  clears `_keepAlive` and cancels the pending read, then
  [L766-771](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L766-L771)
  ends the connection when no request is in progress.
- Header 408:
  [L146-156](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L146-L156)
  sets `_requestTimedOut`;
  [L773-778](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L773-L778)
  throws `RequestHeadersTimeout`;
  [`KestrelBadHttpRequestException.cs` L70-75](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/KestrelBadHttpRequestException.cs#L70-L75)
  maps both `RequestHeadersTimeout` and `RequestBodyTimeout` to `StatusCodes.Status408RequestTimeout`;
  [`HttpProtocol.cs` L1410-1427](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L1410-L1427)
  writes the error response only `if (!HasResponseStarted)` and disables keep-alive, so
  [L1240-1245](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L1240-L1245)
  adds `Connection: close`. On `main` the fast path spells it out:
  [`Http1Connection.cs` L32-33 at f4777b2d](https://github.com/dotnet/aspnetcore/blob/f4777b2dc73cefe741c2cd8d36341b62caf2cfd8/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L32-L33)
  `"HTTP/1.1 408 Request Timeout\r\nContent-Length: 0\r\nConnection: close"`.
- Body rate:
  [`Http1Connection.cs` L158-165](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L158-L165)
  logs and sets `_requestTimedOut`; the body reader then throws `RequestBodyTimeout`
  ([`Http1ContentLengthMessageBody.cs` L44-47, L82-86](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1ContentLengthMessageBody.cs#L44-L86)).
  HTTP/2 aborts the connection with `INTERNAL_ERROR`
  ([`Http2Connection.cs` L261-272](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http2/Http2Connection.cs#L261-L272)).
- Abort on the wire:
  [`SocketConnection.cs` L366-407](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Transport.Sockets/src/Internal/SocketConnection.cs#L366-L407):
  an abort with a reason does `_socket.Close(timeout: 0)` ("This forces an abortive close with linger time 0") unless
  `FinOnError`, an AppContext switch `Microsoft.AspNetCore.Server.Kestrel.FinOnError`
  ([`SocketTransportOptions.cs` L16-25](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Transport.Sockets/src/SocketTransportOptions.cs#L16-L25));
  a graceful end does `Shutdown(SocketShutdown.Both)` then `Dispose()` (FIN).

WireMock.Net hosts on Kestrel
([`AspNetCoreSelfHost.cs` L106-108](https://github.com/wiremock/WireMock.Net/blob/2.19.0/src/WireMock.Net.Minimal/Owin/AspNetCoreSelfHost.cs#L106-L108))
and changes only size limits
([`AspNetCoreSelfHost.NETStandard.cs` L14-19](https://github.com/wiremock/WireMock.Net/blob/2.19.0/src/WireMock.Net.Minimal/Owin/AspNetCoreSelfHost.NETStandard.cs#L14-L19)),
so its callers get exactly the limits above, debugger rule included.

## 3. YARP `ActivityTimeout` (SOURCE v2.3.0, DOC)

- Meaning and default
  ([`ForwarderRequestConfig.cs` L21-27](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/ForwarderRequestConfig.cs#L21-L27);
  same words in the
  [HTTP client config DOC](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/http-client-config?view=aspnetcore-10.0#httprequest)):
  "How long a request is allowed to remain idle between any operation completing, after which it will be canceled.
  The default is 100 seconds. The timeout will reset when response headers are received or after successfully reading
  or writing any request, response, or streaming data like gRPC or WebSockets. TCP keep-alive packets and HTTP/2
  protocol pings will not reset the timeout, but WebSocket pings will."
  [`HttpForwarder.cs` L30](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L30)
  `DefaultTimeout = TimeSpan.FromSeconds(100)`, used at
  [L129](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L129).
- Resets: after response headers
  ([L215-217](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L215-L217)) and
  after every successful read and write in the body copier
  ([`StreamCopier.cs` L84-85, L112-113](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/StreamCopier.cs#L84-L113)).
  The timer is a `CancellationTokenSource.CancelAfter`, re-armed only if more than 20 ms passed since the last re-arm
  ([`ActivityCancellationTokenSource.cs` L13-16, L44-60](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Utilities/ActivityCancellationTokenSource.cs#L13-L60)).
- What is answered (all in `HttpForwarder.cs`): timed out before the upstream answered: **504**
  (`ForwarderError.RequestTimedOut`,
  [L684-688](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L684-L688), message
  "The request timed out before receiving a response.",
  [L1088](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L1088)); timed out
  while reading the *client's* request body: **408** (`RequestBodyClient`), and while writing the body to the upstream:
  **504** (`RequestBodyDestination`),
  [L598-621](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L598-L621); after
  the response started, the response is reset or aborted ("we must forcefully terminate it so the client doesn't get
  the mistaken impression that the truncated response is complete",
  [L938-948](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L938-L948)). An
  idle WebSocket ends as `UpgradeActivityTimeout`
  ([L837-841](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L837-L841)).
- No debugger exemption (section 1.2). The [timeouts DOC](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0#defaults)
  says requests have no other timeout by default "other than the ActivityTimeout used to clean up idle requests".
- For comparison, `HttpClient.Timeout` defaults to 100 s for the whole request
  ([DOC](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0#remarks)), and
  YARP's recommended handler sets `ConnectTimeout = TimeSpan.FromSeconds(15)`
  ([DOC](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/http-client-config?view=aspnetcore-10.0#custom-iforwarderhttpclientfactory)).

## 4. HAProxy (DOC, configuration manual 3.4)

Links go to [docs.haproxy.org/3.4](https://docs.haproxy.org/3.4/configuration.html); line numbers are in
[`doc/configuration.txt` at v3.4.0](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt).

| Setting | Meaning (quoted) | Default |
|---|---|---|
| [`timeout client`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20client) ([L14739-14776](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L14739-L14776)) | "Set the maximum inactivity time on the client side." "The inactivity timeout applies when the client is expected to acknowledge or send data." | none: "An unspecified timeout results in an infinite timeout, which is not recommended. Such a usage is accepted and works but reports a warning during startup" |
| [`timeout server`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20server) ([L15013-15053](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L15013-L15053)) | "Set the maximum inactivity time on the server side." | none, same warning |
| [`timeout client-fin`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20client-fin) ([L14779-14807](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L14779-L14807)) | "Set the inactivity timeout on the client side for half-closed connections." "The inactivity timeout applies when the client is expected to acknowledge or send data while one direction is already shut down. ... This is particularly useful to avoid keeping connections in FIN_WAIT state for too long when clients do not disconnect cleanly. This problem is particularly common long connections such as RDP or WebSocket. Note that this timeout can override "timeout tunnel" when a connection shuts down in one direction." | "By default it is not set, so half-closed connections will use the other timeouts (timeout.client or timeout.tunnel)." |
| [`timeout server-fin`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20server-fin) ([L15056-15083](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L15056-L15083)) | the server-side twin; "This setting was provided for completeness, but in most situations, it should not be needed." | not set; falls back to timeout.server or timeout.tunnel |
| [`timeout tunnel`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20tunnel) ([L15112-15160](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L15112-L15160)) | "The tunnel timeout applies when a bidirectional connection is established between a client and a server, and the connection remains inactive in both directions. This timeout supersedes both the client and server timeouts once the connection becomes a tunnel." Advises also setting `timeout client-fin` for a client that "sends a shutdown and does not acknowledge pending data anymore". | no default stated; the manual's example: `timeout client 30s`, `timeout client-fin 30s`, `timeout server 30s`, `timeout tunnel 1h` |
| [`timeout http-request`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20http-request) ([L14936-14983](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L14936-L14983)) | total time for the request head: "The client timeout cannot offer a good protection against this abuse because it is an inactivity timeout ... With the HTTP request timeout, no matter what speed the client types, the request will be aborted if it does not complete in time. When the timeout expires, an HTTP 408 response is sent to the client to inform it about the problem, and the connection is closed." Header part only, unless `option http-buffer-request`. | none: "If this parameter is not set, the client timeout still applies between each chunk of the incoming request." |
| [`timeout http-keep-alive`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20http-keep-alive) ([L14858-14933](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L14858-L14933)) | wait for the next request; "when a connection expires during timeout http-keep-alive, no error is returned, the connection just closes." | "If this parameter is not set, the "http-request" timeout applies, and if both are not set, "timeout client" still applies at the lower level." |

Status codes ([L550, L563](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L550-L563)): "408 when
the request timeout strikes before the request is complete", "504 when the response timeout strikes before the server
responds". Termination codes in logs
([L30119-30150, L30215-30221](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L30119-L30221)):
`cD` "The client did not send nor acknowledge any data for as long as the "timeout client" delay", `cR` for
`timeout http-request` (408), `sH` "The "timeout server" stroke before the server could return its response headers".
Advice repeated for client, server, connect and tunnel: use values "slightly above multiples of 3 seconds (e.g. 4 or
5 seconds)" to cover TCP retransmits.

## 5. nginx

**Stream (TCP) proxy** ([DOC](https://nginx.org/en/docs/stream/ngx_stream_proxy_module.html)):
- [`proxy_timeout`](https://nginx.org/en/docs/stream/ngx_stream_proxy_module.html#proxy_timeout), default `10m`:
  "Sets the timeout between two successive read or write operations on client or proxied server connections. If no
  data is transmitted within this time, the connection is closed."
- [`proxy_half_close`](https://nginx.org/en/docs/stream/ngx_stream_proxy_module.html#proxy_half_close), default `off`,
  since 1.21.4: "Enables or disables closing each direction of a TCP connection independently ("TCP half-close"). If
  enabled, proxying over TCP will be kept until both sides close the connection."
- SOURCE (release-1.31.6,
  [`src/stream/ngx_stream_proxy_module.c`](https://github.com/nginx/nginx/blob/release-1.31.6/src/stream/ngx_stream_proxy_module.c)):
  one timer for the whole session, re-armed after every pass that moved data
  ([L2134-2139](https://github.com/nginx/nginx/blob/release-1.31.6/src/stream/ngx_stream_proxy_module.c#L2134-L2139),
  `ngx_add_timer(c->write, pscf->timeout)`); on expiry it logs "connection timed out" and finalizes the session
  ([L1822-1824](https://github.com/nginx/nginx/blob/release-1.31.6/src/stream/ngx_stream_proxy_module.c#L1822-L1824)).
  With half-close off the session is finalized once either side has hit EOF and the other direction is flushed
  ([L2197-2205](https://github.com/nginx/nginx/blob/release-1.31.6/src/stream/ngx_stream_proxy_module.c#L2197-L2205));
  with it on, it waits for EOF on both ("avoid closing live connections until both read ends get EOF",
  [L2207-2212](https://github.com/nginx/nginx/blob/release-1.31.6/src/stream/ngx_stream_proxy_module.c#L2207-L2212))
  after passing the first EOF on with `shutdown(SHUT_WR)`
  ([L2112-2127](https://github.com/nginx/nginx/blob/release-1.31.6/src/stream/ngx_stream_proxy_module.c#L2112-L2127)).
  INFERENCE: so a half-closed pair is bounded only by `proxy_timeout`.

**HTTP** ([core module](https://nginx.org/en/docs/http/ngx_http_core_module.html),
[proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html)):

| Directive | Default | Kind (quoted) |
|---|---|---|
| [`client_header_timeout`](https://nginx.org/en/docs/http/ngx_http_core_module.html#client_header_timeout) | 60s | "If a client does not transmit the entire header within this time, the request is terminated with the 408 (Request Time-out) error." |
| [`client_body_timeout`](https://nginx.org/en/docs/http/ngx_http_core_module.html#client_body_timeout) | 60s | "The timeout is set only for a period between two successive read operations, not for the transmission of the whole request body. If a client does not transmit anything within this time, the request is terminated with the 408 (Request Time-out) error." |
| [`send_timeout`](https://nginx.org/en/docs/http/ngx_http_core_module.html#send_timeout) | 60s | "The timeout is set only between two successive write operations, not for the transmission of the whole response. If the client does not receive anything within this time, the connection is closed." |
| [`keepalive_timeout`](https://nginx.org/en/docs/http/ngx_http_core_module.html#keepalive_timeout) | 75s | "a timeout during which a keep-alive client connection will stay open on the server side" |
| [`lingering_time`](https://nginx.org/en/docs/http/ngx_http_core_module.html#lingering_time) / [`lingering_timeout`](https://nginx.org/en/docs/http/ngx_http_core_module.html#lingering_timeout) | 30s / 5s | after the response, read and discard further client data, waiting at most 5 s for each piece and 30 s in all |
| [`reset_timedout_connection`](https://nginx.org/en/docs/http/ngx_http_core_module.html#reset_timedout_connection) | off | when on, a timed-out connection is closed with `SO_LINGER` 0 (RST) "to avoid keeping an already closed socket with filled buffers in a FIN_WAIT1 state for a long time" |
| [`proxy_read_timeout`](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_read_timeout) / [`proxy_send_timeout`](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_send_timeout) | 60s / 60s | upstream side, "only between two successive read [write] operations" |

So yes: nginx's body, send, proxy and stream timeouts are inactivity timeouts; the header timeout is a total. SOURCE
(release-1.31.6, `src/http/ngx_http_request.c`): `client_header_timeout` is armed when the connection is set up
([L366](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L366)) and re-armed only when no
timer is set ([L440-443](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L440-L443),
[L1624-1627](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L1624-L1627)), so it bounds
the first request from accept to the end of its head. The body timer is re-armed on every wait
([`ngx_http_request_body.c` L435-438](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request_body.c#L435-L438)).
The "408" is a logged status, not a sent response: a timeout finalizes with `NGX_HTTP_REQUEST_TIME_OUT`
([`ngx_http_request.c` L1130-1134](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L1130-L1134),
[`ngx_http_request_body.c` L280-283](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request_body.c#L280-L283)),
which goes to `ngx_http_terminate_request`
([L2775-2785](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L2775-L2785)); that
records the status for the log and closes without writing a response
([L2931-2983](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L2931-L2983)).

## 6. Envoy (DOC and SOURCE, v1.40.0)

| Setting | Default | Meaning (quoted) |
|---|---|---|
| TCP proxy [`idle_timeout`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/extensions/filters/network/tcp_proxy/v3/tcp_proxy.proto#envoy-v3-api-field-extensions-filters-network-tcp-proxy-v3-tcpproxy-idle-timeout) ([proto L276-284](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/tcp_proxy/v3/tcp_proxy.proto#L276-L284)) | 1 h | "the period in which there are no bytes sent or received on either the upstream or downstream connection. If not set, the default idle timeout is 1 hour. If set to 0s, the timeout is disabled." Warning: "Disabling this timeout is likely to yield connection leaks due to lost TCP FIN packets, etc." |
| TCP proxy [`max_downstream_connection_duration`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/extensions/filters/network/tcp_proxy/v3/tcp_proxy.proto#envoy-v3-api-field-extensions-filters-network-tcp-proxy-v3-tcpproxy-max-downstream-connection-duration) ([L318-322](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/tcp_proxy/v3/tcp_proxy.proto#L318-L322)) | none | "If not set, there is no maximum duration." |
| HCM [`stream_idle_timeout`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#envoy-v3-api-field-extensions-filters-network-http-connection-manager-v3-httpconnectionmanager-stream-idle-timeout) ([proto L590-624](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#L590-L624)) | 5 min | "Each time an encode/decode event for headers or data is processed for the stream, the timer will be reset. If the timeout fires, the stream is terminated with a 408 Request Timeout error code if no upstream response header has been received, otherwise a stream reset occurs." The default was chosen for "robustness to TCP connections that terminate without a FIN." |
| HCM [`request_timeout`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#envoy-v3-api-field-extensions-filters-network-http-connection-manager-v3-httpconnectionmanager-request-timeout) ([L643-648](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#L643-L648)) | off | "The amount of time that Envoy will wait for the entire request to be received." The [timeouts FAQ](https://www.envoyproxy.io/docs/envoy/v1.40.0/faq/configuration/timeouts): "not enforced by default as it is not compatible with streaming requests (requests that never end)." |
| HCM [`request_headers_timeout`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#envoy-v3-api-field-extensions-filters-network-http-connection-manager-v3-httpconnectionmanager-request-headers-timeout) ([L650-656](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#L650-L656)) | off | "activated when the first byte of the headers is received, and is disarmed when the last byte of the headers has been received." |
| HTTP connection [`idle_timeout`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/config/core/v3/protocol.proto#envoy-v3-api-field-config-core-v3-httpprotocoloptions-idle-timeout) ([protocol.proto L312-332](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/config/core/v3/protocol.proto#L312-L332)) | 1 h | "the period in which there are no active requests"; same leak warning as the TCP proxy. |
| HCM [`delayed_close_timeout`](https://www.envoyproxy.io/docs/envoy/v1.40.0/api-v3/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#envoy-v3-api-field-extensions-filters-network-http-connection-manager-v3-httpconnectionmanager-delayed-close-timeout) ([L682-715](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#L682-L715)) | 1000 ms | after Envoy decides to close, how long it waits for the peer's FIN or RST before closing; "any progress made writing data to the socket will restart the timer". |

Half-close in the TCP proxy (SOURCE,
[`source/common/tcp_proxy/tcp_proxy.cc`](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc)):
the downstream connection is put in half-close mode
([L457](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc#L457)
`enableHalfClose(true)`) and `end_stream` is passed on in both directions
([L1130-1133](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc#L1130-L1133),
[L1325-1333](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc#L1325-L1333)). There
is no separate half-close timer: the one idle timer (default
[L145-152](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc#L145-L152),
`std::chrono::hours(1)`) is reset by data in either direction and on expiry closes the downstream, "which results in
also closing the upstream connection"
([L1495-1502](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc#L1495-L1502)).
After the downstream fully closes, the upstream is handed to a drainer that keeps the same idle timer and closes the
upstream at once if it sends data ("Try to behave similar to what the kernel would do",
[L1677-1690](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/tcp_proxy/tcp_proxy.cc#L1677-L1690)).

## 7. Test-time proxies

**Toxiproxy** (SOURCE v2.12.0). No idle or half-close timer anywhere: a grep of the non-test Go files for `SetDeadline`,
`SetReadDeadline`, `SetWriteDeadline`, `CloseWrite` and `KeepAlive` finds nothing. Each direction copies until its
source ends, then *fully* closes the destination and removes the connection
([`link.go` L137-168](https://github.com/Shopify/toxiproxy/blob/v2.12.0/link.go#L137-L168): `io.Copy(dest,
link.output)` then `dest.Close()`), so a client's FIN closes the upstream socket outright (no half-close) and the other
direction then fails and closes the client. Upstream is dialled with plain `net.Dial`, no timeout
([`proxy.go` L206](https://github.com/Shopify/toxiproxy/blob/v2.12.0/proxy.go#L206)). Timing behaviour only comes from
toxics: `timeout` "Stops all data from getting through, and closes the connection after `timeout`. If `timeout` is 0,
the connection won't close" ([README](https://github.com/Shopify/toxiproxy/blob/v2.12.0/README.md#timeout),
[`toxics/timeout.go` L5-45](https://github.com/Shopify/toxiproxy/blob/v2.12.0/toxics/timeout.go#L5-L45)), plus
`reset_peer`, `slow_close` and `limit_data`.

**mitmproxy** (SOURCE v12.2.3, DOC). [`tcp_timeout`](https://docs.mitmproxy.org/stable/concepts/options/#tcp_timeout),
default 600: "Timeout in seconds for inactive TCP connections. Connections will be closed after this period of
inactivity." ([`options.py` L240-247](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/options.py#L240-L247)).
One watchdog per client connection handler
([`server.py` L116-120](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/server.py#L116-L120);
UDP uses a fixed 20 s, L47), fed by every event on any of its connections
([L387](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/server.py#L387)); on expiry it cancels the
client handler, "Closing connection due to inactivity"
([L344-354](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/server.py#L344-L354)). Half-close is
supported: an EOF only clears `CAN_READ` and the writer stays usable
([L302-317](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/server.py#L302-L317)), and a half-close
command calls `write_eof()`
([L439-455](https://github.com/mitmproxy/mitmproxy/blob/v12.2.3/mitmproxy/proxy/server.py#L439-L455)); a half-closed
pair is bounded by the same 600 s. The watchdog is disarmed during hooks and intercepts (section 1.2).

**WireMock (Java)** (SOURCE 3.13.2). Upstream side: `proxyTimeout` defaults to `DEFAULT_TIMEOUT = 300_000` ms
([`Options.java` L47](https://github.com/wiremock/wiremock/blob/3.13.2/src/main/java/com/github/tomakehurst/wiremock/core/Options.java#L47),
[`WireMockConfiguration.java` L141](https://github.com/wiremock/wiremock/blob/3.13.2/src/main/java/com/github/tomakehurst/wiremock/core/WireMockConfiguration.java#L141),
[`CommandLineOptions.java` L998-1002](https://github.com/wiremock/wiremock/blob/3.13.2/src/main/java/com/github/tomakehurst/wiremock/standalone/CommandLineOptions.java#L998-L1002)).
Caller side: `--jetty-idle-timeout` is unset by default
([`WireMockConfiguration.java` L114](https://github.com/wiremock/wiremock/blob/3.13.2/src/main/java/com/github/tomakehurst/wiremock/core/WireMockConfiguration.java#L114))
and applied only if present
([`Jetty11Utils.java` L54](https://github.com/wiremock/wiremock/blob/3.13.2/src/main/java/com/github/tomakehurst/wiremock/jetty11/Jetty11Utils.java#L54)),
so Jetty's own default holds: `_idleTimeout = 30000`
([`AbstractConnector.java` L154, jetty-11.0.x at 5c8ce313](https://github.com/jetty/jetty.project/blob/5c8ce3131d5742486e715f043c247a08ec1eaf3c/jetty-server/src/main/java/org/eclipse/jetty/server/AbstractConnector.java#L154)),
an inactivity limit: "This value is interpreted as the maximum time between some progress being made on the
connection. So if a single byte is read or written, then the timeout is reset."
([L284-297](https://github.com/jetty/jetty.project/blob/5c8ce3131d5742486e715f043c247a08ec1eaf3c/jetty-server/src/main/java/org/eclipse/jetty/server/AbstractConnector.java#L284-L297)).
The [standalone docs](https://wiremock.org/docs/standalone/java-jar/) list both options without defaults.

**WireMock.Net**: Kestrel's limits (section 2).

**MockServer** (DOC, [configuration properties](https://www.mock-server.com/mock_server/configuration_properties.html)):
upstream side only. `maxSocketTimeout` 20,000 ms, "Maximum time to wait for the first response byte when
forwarding/proxying"; `socketConnectionTimeout` 20,000 ms; `webSocketProxyIdleTimeoutSeconds` 0, "disables idle
reaping".

**Testcontainers' socat** (SOURCE). Java
([`SocatContainer.java` L19, L40-46 at 2.0.5](https://github.com/testcontainers/testcontainers-java/blob/2.0.5/core/src/main/java/org/testcontainers/containers/SocatContainer.java#L19-L46))
and .NET
([`SocatBuilder.cs` L18, L104-107 at 4.16.0](https://github.com/testcontainers/testcontainers-dotnet/blob/4.16.0/src/Testcontainers/Containers/SocatBuilder.cs#L18-L107))
run `socat TCP-LISTEN:<port>,fork,reuseaddr TCP:<target>` on `alpine/socat:1.7.4.x` with no `-t` or `-T`, so socat's
defaults apply ([man page](http://www.dest-unreach.org/socat/doc/socat.html)):
[`-t`](http://www.dest-unreach.org/socat/doc/socat.html#option_t) "When one channel has reached EOF ..., the write part
of the other channel is shut down. Then, socat waits <timeout> [timeval] seconds before terminating. ... Default is 0.5
seconds"; [`-T`](http://www.dest-unreach.org/socat/doc/socat.html#option_T) "Total inactivity timeout ... default is
-1" (none). The closing phase: "For a defined time socat continues to transfer data in the other direction, but then
closes all remaining channels and terminates."

## 8. .NET sockets: cancelling a pending operation

**It works, since .NET Core 3.0.** ISSUE:
[dotnet/corefx#36516](https://github.com/dotnet/corefx/pull/36516) "Plumb CancellationToken through
Socket.Receive/SendAsync" (stephentoub, merged 2019-04-07): "In .NET Core 2.1 ... those tokens were only checked at
the beginning of the call ... This PR plumbs the token through so that a cancellation request at any point in the
operation will cancel that operation. On Windows we register to use CancelIoEx to request cancellation of the specific
overlapped operation on the specific socket. On Unix we use the existing cancellation infrastructure". Caveat stated
there: "On Windows, canceling a TCP receive will end up canceling all TCP receives pending on that socket, even when we
request cancellation of a specific overlapped operation; this is just how cancellation works at the OS level". (Its
second caveat, that only the first concurrent receive or send was cancellable, looks obsolete: at v10.0.13 a busy
cached event-args object is replaced by a new one,
[`Socket.Tasks.cs` L293-295](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L293-L295),
INFERENCE.)

**The path at v10.0.13** (SOURCE, `src/libraries/System.Net.Sockets/src/System/Net/Sockets/`):
- `NetworkStream.ReadAsync(Memory<byte>, CancellationToken)` and the array overload call
  `_streamSocket.ReceiveAsync(..., fromNetworkStream: true, cancellationToken)`
  ([`NetworkStream.cs` L563-607](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/NetworkStream.cs#L563-L607));
  both `WriteAsync` overloads call `SendAsyncForNetworkStream(..., cancellationToken)`
  ([L624-666](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/NetworkStream.cs#L624-L666)).
- `Socket.ReceiveAsync(Memory<byte>, [SocketFlags,] CancellationToken)`
  ([`Socket.Tasks.cs` L273-302](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L273-L302))
  and `SendAsync` / `SendAsyncForNetworkStream`
  ([L532-566](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L532-L566))
  check the token up front, then pass it into the operation
  ([L1039-1055](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L1039-L1055),
  [L1135-1150](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L1135-L1150)).
- Windows: when the operation is pending, a registration calls `Interop.Kernel32.CancelIoEx(handle,
  _pendingOverlappedForCancellation)` ("cancellation is opportunistic"):
  [`SocketAsyncEventArgs.Windows.cs` L188-219](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncEventArgs.Windows.cs#L188-L219).
- Unix: the queued operation registers `TryCancel`
  ([`SocketAsyncContext.Unix.cs` L885-891](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncContext.Unix.cs#L885-L891));
  a waiting operation becomes `Canceled` with `SocketError.OperationAborted` and is left in the queue to be skipped
  ([L204-260](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncContext.Unix.cs#L204-L260));
  one already running when the cancel arrives completes normally if its I/O succeeds
  ([L152-198](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncContext.Unix.cs#L152-L198)).
- Result: `OperationAborted` or `ConnectionAborted` with the token cancelled becomes `OperationCanceledException`
  ([`Socket.Tasks.cs` L1351-1362](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L1351-L1362)).
  DOC ([`Socket.ReceiveAsync`](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receiveasync?view=net-10.0)):
  "OperationCanceledException: The cancellation token was canceled. This exception is stored into the returned task."
- .NET 8: the same code at tag v8.0.32
  ([`NetworkStream.cs` L551, L574-575](https://github.com/dotnet/runtime/blob/v8.0.32/src/libraries/System.Net.Sockets/src/System/Net/Sockets/NetworkStream.cs#L551-L575),
  [`SocketAsyncEventArgs.Windows.cs` L200](https://github.com/dotnet/runtime/blob/v8.0.32/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncEventArgs.Windows.cs#L200),
  [`SocketAsyncContext.Unix.cs` L869](https://github.com/dotnet/runtime/blob/v8.0.32/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncContext.Unix.cs#L869)).

**State after a cancelled operation.** No .NET document says. What the platform documents say:
- [CancelIoEx](https://learn.microsoft.com/en-us/windows/win32/fileio/cancelioex-func#remarks): "CancelIoEx cancels
  only outstanding I/O on the handle, it does not change the state of the handle; this means that you cannot rely on
  the state of the handle because you cannot know whether the operation was completed successfully or canceled." and
  "The operation completed normally. This can occur even if the operation was canceled".
- Winsock, for the synchronous timeouts
  ([SO_RCVTIMEO / SO_SNDTIMEO](https://learn.microsoft.com/en-us/windows/win32/winsock/sol-socket-socket-options)): "If
  a blocking receive call times out, the connection is in an indeterminate state and should be closed." (same for
  send).
- Unix send (SOURCE): a send can make partial progress, adding to `bytesSent` and then waiting for writability
  ([`SocketPal.Unix.cs` L967-1021](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketPal.Unix.cs#L967-L1021)),
  so a send cancelled at that point has put part of the buffer on the wire. INFERENCE: a cancelled receive on Unix
  consumes nothing and leaves the socket usable, but a cancelled send leaves the byte stream mid-message on either
  platform; for a tap that is dropping the caller anyway, cancel and close.

**Closing with operations pending** (SOURCE): on Unix, closing a socket whose queue still holds operations aborts them
(`StopAndAbort`,
[`SocketAsyncContext.Unix.cs` L1212-1245](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketAsyncContext.Unix.cs#L1212-L1245);
[`SafeSocketHandle.Unix.cs` L173-177](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SafeSocketHandle.Unix.cs#L173-L177))
and "In case we cancel operations, switch to an abortive close. Unless the user requested a normal close using
Socket.Shutdown."
([`SafeSocketHandle.cs` L143-150](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SafeSocketHandle.cs#L143-L150)).
INFERENCE: a pump blocked in `ReceiveAsync` whose socket is disposed sends RST on Linux unless `Shutdown(Send)` came
first. On Windows `OnHandleClose` returns false and cancellation goes through `CancelIoEx(handle, null)` in
`TryUnblockSocket`
([`SafeSocketHandle.Windows.cs` L105-133](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SafeSocketHandle.Windows.cs#L105-L133)).

**What does not help.**
- [`NetworkStream.ReadTimeout`](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.readtimeout?view=net-10.0#remarks):
  "This property affects only synchronous reads performed by calling the Read method. This property does not affect
  asynchronous reads performed by calling the BeginRead or ReadAsync method."
  [`Socket.ReceiveTimeout`](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivetimeout?view=net-10.0#remarks):
  "This option applies to synchronous Receive calls only."
- Base `Stream`: the array overloads check the token once and then run Begin/End
  ([`Stream.cs` L303-306, L723-728 at runtime v10.0.13](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Private.CoreLib/src/System/IO/Stream.cs#L303-L306)),
  and the `Memory` overloads forward to them for array-backed memory (L308-313, L730-733). INFERENCE: for a stream
  that overrides neither (the managed HttpListener streams, per #173), a token cannot interrupt a pending read or
  write; only aborting the connection can.

## 9. Defaults side by side (caller side unless marked)

| Phase | Kestrel | nginx | HAProxy | Envoy | Others |
|---|---|---|---|---|---|
| Request head | 30 s total from first byte | 60 s total from accept (first request) | `http-request`: none (falls back to `client` inactivity) | `request_headers_timeout`: off | |
| Request body | 240 B/s average after 5 s grace, only while waiting | 60 s between reads | `client`: none (startup warning); manual example 30 s | stream idle 5 min (any event) | YARP 100 s idle; Jetty 30 s idle |
| Response to a non-reader | per-write deadline max(5 s, bytes / 240 B/s) | 60 s between writes | `client` | stream idle 5 min | YARP 100 s idle; Jetty 30 s idle |
| Idle keep-alive | 130 s | 75 s | `http-keep-alive`: falls back to `http-request`, then `client` | 1 h (`idle_timeout`) | |
| Raw TCP, both directions | n/a | stream `proxy_timeout` 10 min | `tunnel`: none (manual example 1 h) | TCP `idle_timeout` 1 h | mitmproxy 600 s; socat and Toxiproxy none |
| Half-closed TCP | n/a | ends at first EOF; with `proxy_half_close on`, `proxy_timeout` | `client-fin` / `server-fin`: unset, fall back (manual example 30 s) | propagated, idle 1 h | socat 0.5 s then close; mitmproxy 600 s; Toxiproxy closes the other side at once |
| Upstream (not caller) | n/a | 60 s read / send | `server`: none | per route | YARP 100 s; HttpClient 100 s; MockServer 20 s to first byte; WireMock 300 s |
| Debugger | all of the above suppressed | n/a | n/a | n/a | ASP.NET request timeouts and SignalR suppressed; YARP not; mitmproxy disarms during intercepts |

## 10. Not established

- No product found suppresses timeouts for a process paused without a debugger; Kestrel's tick-gap allowance covers
  only the rates (section 1.4).
- Whether the monotonic counter behind `Stopwatch` advances across machine sleep on each OS was not researched; it
  matters only for a laptop put to sleep mid-test.
- `Debugger.IsAttached` cannot see a debugger attached to a different process (INFERENCE from its comment, section 1.1).
