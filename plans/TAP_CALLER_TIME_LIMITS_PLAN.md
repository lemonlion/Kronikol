# Caller-side time limits for the taps (#173)

**Date:** 2026-10-14 · **Repo version:** 4.20.2 (`origin/main` at `3240c983`; every line number below is for it unless
it says otherwise) · **Status:** ✅ executed in full, every question in §9 as the owner green-lit it on 2026-10-15: R0
published as 4.22.2 on 2026-10-15, R1 as 4.23.0, R2 as 4.25.0 and R3 as 4.26.0 on 2026-10-16, with 4.26.1, a fix to R1
that 4.26.0's CI run found, the same day; #173 closed (§11). Audited on 2026-10-16 at the owner's ask: what
the audit found is fixed in 4.26.3 (§11). Drafted from #173 (filed 2026-10-13 from
`PROXY_TAP_HANG_UP_PLAN.md` §3 finding 9, §8 and Q2) at the owner's ask of 2026-10-14 to analyse the issue critically
and plan a fix for the underlying problem. Written alongside the plan for #174 (`TCPTAP_FAILED_CONNECT_PLAN.md`, session
kronikol-71), which owns both taps' accept loops and `TcpTap`'s connect path; §7 is the split. #174 was green-lit on
2026-10-15 and released the same day as 4.20.3, 4.22.0 and 4.22.1, which moved the lines this plan cites in `TcpTap.cs`,
`OtlpTap.cs`, `TcpTapOptions.cs`, `CaptureDegradation.cs` and `DiagnosticEntry.cs`; they stay at `3240c983`, where the
code was read, and Appendix A gives each edit site at 4.22.1 too (§7). Swept for completeness and accuracy on 2026-10-15
(§11).

Evidence labels: **RUN** (measured on 2026-10-14 with the published 4.20.2 packages from nuget.org or a bare
`HttpListener`, and on 2026-10-15 with the sweep's socket, `HttpListener` and BCL probes, one of them on .NET 8 too:
Windows 11 26200, where `HttpListener` is http.sys, and Ubuntu in `mcr.microsoft.com/dotnet/sdk:10.0`, where it is the
managed implementation; scripts and output in
[`TAP_CALLER_TIME_LIMITS_PLAN.harness/`](TAP_CALLER_TIME_LIMITS_PLAN.harness/README.md)), **READ** (Kronikol's source at
`3240c983`, notes in the harness's `read/` and `sweep/`), **SOURCE** (.NET's `System.Net.HttpListener` on `release/10.0`
at `591ede8`, ASP.NET Core at `v10.0.13`; notes in `source/`; other projects' source, linked, in
`sweep/external-facts.md`), **DOC** (vendor documentation, linked in `source/prior-art.md`), **PEER** (a fact measured
by the #174 plan, cited by its harness path, not re-measured).

## 0. Summary

- **The issue is right about all three taps.** Measured on 4.20.2: on Linux `ProxyTap` holds a caller whose body stalls,
  and on both platforms one that never reads its answer, through 200 s and until disposal. `OtlpTap` holds a partial
  head, a stalled body and an idle connection until it is disposed, on both platforms, and ten exporters that each sent
  10 bytes of a declared 32 MiB pinned 321 MiB of heap. `TcpTap` holds the half-closed pair the issue names, its mirror,
  and a pair whose client stops reading (§2.2).
- **One claim needs a qualifier.** http.sys's entity-body timer resets a stalled body at about two minutes, and the tap
  then counts and logs nothing; its send-rate timer exists, but it scales with the pending write, which is the whole
  answer, so a caller that stops reading an 8 MB answer is allowed about 15.5 hours (inferred from a 64 KiB write reset
  at 445.7 s). The issue's two constraints hold: a token does not stop listener I/O once it is running, and a stalled
  head never reaches the tap (line 320 is right). They have a consequence the issue does not draw: after `Abort()` the
  managed listener can report the cut I/O as a success, so a limit has to decide by its own state (§1).
- **The underlying problem is that no tap watches progress.** Each trusts its peers to end every transfer, the only
  release is disposal, and nothing counts or logs a transfer that is held. The cost is more than resources: with the
  in-flight registry on, a stalled answer leaves its test's identity on the service, so a database tap attributes that
  service's idle-time queries to it; held descriptors ended two taps' accept loops for good until #174's 4.20.3, and
  still leave new callers waiting in the listen queue (PEER, #174); on Linux, stopping `ProxyTap` tells every stalled
  caller `200 OK`; and the same suite holds different things on Windows and on Linux CI (§3).
- **The design is one rule for all three taps** (§4.1): a transfer the tap is in the middle of is cut when its peer
  moves no bytes for 100 s, so a slow peer that keeps moving (16 KiB per limit, up to 32 KiB for the last piece of a
  `ProxyTap` answer or a `TcpTap` write) is never cut; an idle connection between exchanges is never a stall (pooled
  database connections), and no limit applies while a debugger is attached, as in Kestrel. One internal watch, shared by
  the three packages, runs it on one heartbeat per tap with a monotonic, injectable clock (§4.2). Each tap supplies the
  action: `ProxyTap` answers a stalled body `408` (a reset on Windows) and aborts a stalled answer (§4.3), `OtlpTap`
  adds a keep-alive limit and answers a stalled head or body `408` (§4.4), and `TcpTap` closes a half-closed pair whose
  open direction goes quiet, or one whose write is not read, never an idle one (§4.5). Each has a counter and a
  `Diagnostics()` line.
- **What it cannot fix:** a `ProxyTap` caller stalled inside its request head on Linux and macOS. The listener reads the
  head before the tap sees the request, its only timer is hard-coded and covers the first bytes alone, and
  `HttpListener` is in maintenance mode. Documented, and Q6 asks whether the tap should leave `HttpListener`.
- **Releases (§6):** R0, a patch for what the reading found broken (§4.6): an infinite `ForwardTimeout` cuts every
  forward at 10 s and blames `ConnectTimeout`; Linux disposal answers a stalled upload `200 OK`; `OtlpTap` authenticates
  after it has allocated and read the body, and reads a bad `Content-Length` as no body; no tap guards its `Log`
  callback; and six smaller ones (negative caps, a drain cut short, both framings at once, `OtlpTap`'s stop line, a
  handler that outlives disposal, `NoDelay`), with the timeouts' whole range in the first. Then R1 `ProxyTap`, R2
  `OtlpTap` and R3 `TcpTap`, a minor each.
- **Questions for the owner (§9):** Q1 on by default (recommended), Q2 the values, Q3 to Q5 the option shape, the
  releases and the debugger rule, Q6 and Q7 the head, Q8 to Q13 smaller ones. Q9 asks that this plan be committed only
  after R0 is published, since it describes how a caller without `OtlpTap`'s secret can pin its memory.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | `ProxyTap`'s `ForwardTimeout` bounds only the forward to the upstream | **Confirmed** | READ: the forward's deadline is made after the body read (L397-399) and never reaches the answer write; `HttpClient.Timeout` (L192) is the same bound plus 10 s |
| 2 | Nothing bounds reading the caller's body or writing its answer | **Confirmed** | READ: the body is copied with no token (L279-299), the answer written in one `WriteAsync` with none (L852); RUN cases B1 and W1 |
| 3 | On the managed listener a caller that stops sending, or stops reading, without hanging up holds its handler and its connection until the tap is disposed | **Confirmed on Linux** | RUN B1 and W1: in flight 3 of 3 and every connection held through 200 s; `DisposeAsync` releases them |
| 4 | http.sys has entity-body and send-rate timers of its own | **True, but only the first bounds the case.** The entity-body timer resets a stalled body at about two minutes; the send-rate timer scales with the pending write, so it holds a caller that stops reading an answer for hours | RUN B1 on Windows: a reset at 125.2 s, and the tap counts and logs nothing. RUN W1 on Windows: held through 200 s, in flight 3, like Linux. `netsh` shows 120 s timers and 150 B/s; at that rate an 8 MB answer is allowed hours |
| 5 | `OtlpTap`: a stalled exporter holds its handler, a 16 KiB read buffer and a body array sized up front to its `Content-Length` (up to `MaxRequestBytes`, 32 MiB) until disposal | **Confirmed, and wider** | READ: HttpRequestReader.cs L39, L154, OtlpTapOptions.cs L68, plus up to 64 KiB of head. RUN: ten exporters that each sent 10 bytes of a declared 32 MiB added 321 MiB of GC heap after a full collection (322 on Windows), held through 200 s on both platforms. The array is made before the shared secret is checked (§3 item 8). A partial head and an idle keep-alive connection are held the same way (RUN H1, K1) |
| 6 | `TcpTap`: a pair whose upstream neither closes after a half-close nor sends anything is held until disposal, since it is released only once both pumps end | **Confirmed, and it is one of three such states** | READ TcpTap.cs L241, L255-261. RUN T1: held through 90 s, `LiveConnections` 3. The mirror case (the upstream half-closes, the client stays silent, T2) and a client that stops reading a large reply (T3) are held the same way |
| 7 | On the managed listener a token does not interrupt body I/O already running, so a limit has to call `Abort()` on a timer | **Confirmed, with a consequence the issue does not draw** | SOURCE: no listener stream overrides `ReadAsync` or `WriteAsync`; the base `Stream` checks the token once (CoreLib Stream.cs:303-306, :723-728) and runs Begin/End without it (:460-463). After `Abort()` a pending Content-Length write completes with no exception (RUN `listener/` claim 2: 10 of 10 on Linux), so the tap has to decide by its own state, not by what the I/O returns (§4.3). A pending body read did throw, 20 of 20 (claim 3), and `Abort()` does stop it, as the issue says |
| 8 | A caller that stalls inside its request head never reaches the tap: the managed listener re-reads a partial head without re-arming its timer (M/HttpConnection.cs line 320 on `release/10.0`), so only disposal releases it | **Confirmed**: line 320 at `591ede8`, line 325 on `main` | SOURCE §1.4. RUN H1: held through 200 s on Linux, and at disposal it hears `HTTP/1.1 200 OK`. The only timer is hard-coded, 90 s for a new connection and 15 s for a kept-alive one (RUN H0: a reset at 90.0 s; K1: at 15.0 s), and no `TimeoutManager` setting reaches it (SOURCE §2) |
| 9 | Each tap needs defaults chosen and changes behaviour, so this is a release of its own | **Agreed**: a minor per tap, after a patch for what the reading found broken | §4, §6 |

## 2. Where it stands today

### 2.1 The code (READ)

| Tap | Phase | What bounds it today | Where |
|---|---|---|---|
| `ProxyTap` | request head | the listener: on Linux only the wait for the first bytes (90 s, or 15 s on a kept-alive connection); on Windows http.sys | not the tap's |
| `ProxyTap` | request body | nothing on Linux and macOS; http.sys's entity-body timer on Windows | L279-299 |
| `ProxyTap` | the forward | `ForwardTimeout`, 200 s | L397-399, L192 |
| `ProxyTap` | the answer | nothing on Linux and macOS; on Windows http.sys's send-rate timer, which allows the whole answer at 150 B/s (hours for megabytes) | L825-859, L887-902 |
| `ProxyTap` | an idle kept-alive connection | the listener (15 s on Linux, 2 min on Windows) | not the tap's |
| `OtlpTap` | head, body, answer, idle kept-alive connection | nothing | `FillAsync` HttpRequestReader.cs L285-290; `WriteResponseAsync` OtlpTap.cs L459-475 |
| `OtlpTap` | the forward | `HttpClient.Timeout` 30 s, no option | L172 |
| `TcpTap` | a half-closed pair | nothing | L241, L255-261 |
| `TcpTap` | a write the peer does not read | nothing | L299 |
| `TcpTap` | a decoded command left unanswered | the reaper, off by default | L323-371 |
| `TcpTap` | an idle pair | nothing, by design (3.0.49) | TcpTapOptions.cs L165-167 |

What a held exchange holds. `ProxyTap`: its handler, its `CallerAnswer`, the context and the connection, and, while the
answer is written, its in-flight identity in `InFlightIdentityRegistry` (L310-314). `OtlpTap`: a 16 KiB buffer, up to 64
KiB of head, the declared body's array, the socket and the handler. `TcpTap`: two sockets, two read buffers
(`ReadBufferBytes`, 32 KiB each), the decode task and its channel. Nothing in any tap counts or logs a held exchange.

### 2.2 The measurements (RUN)

`probe/`, against the published 4.20.2 packages: three callers per case (ten for `OtlpTap`'s B1), sampled every 15 s,
then the tap disposed. "Held" is still open and still held by the tap or its listener at the last sample. No `ProxyTap`
or `OtlpTap` caller heard a byte before disposal; every release before it was a reset.

| Case | Windows (http.sys) | Linux (managed listener) |
|---|---|---|
| `ProxyTap` H0, a connection that sends nothing | reset at 125.2 s | reset at 90.0 s |
| `ProxyTap` H1, a partial head | reset at 125.2 s | **held through 200 s**; at disposal `HTTP/1.1 200 OK` |
| `ProxyTap` B1, a body that stalls | reset at 125.2 s; the tap counts and logs nothing | **held through 200 s**, in flight 3; at disposal `HTTP/1.1 200 OK`, one a second, `DisposeAsync` 3,028 ms |
| `ProxyTap` W1, an 8 MB answer never read | **held through 200 s**, in flight 3 | **held through 200 s**, in flight 3 |
| `ProxyTap` K1, an idle kept-alive connection | reset at 125.1 s | reset at 15.0 s |
| `OtlpTap` H1, B1 (ten 32 MiB declarations, 10 bytes each), K1 | **all held through 200 s**; B1 +322 MiB of GC heap | **all held through 200 s**; B1 +321 MiB |
| `TcpTap` T1, the client half-closes, the upstream stays silent | **held through 90 s**, `LiveConnections` 3 | the same |
| `TcpTap` T2, the upstream half-closes, the client stays silent | **held through 90 s** | the same |
| `TcpTap` T3, the client never reads an 8 MB reply | **held through 90 s**; the upstream blocked after 393-786 KB | the same; it blocked after 4.8-5.2 MB |
| `TcpTap` T4, an idle pair (the control) | open through 90 s, as it should be | the same |
| `TcpTap` T3 with the reaper on, a pass-through decoder that reports nothing unanswered / `RedisTap` | held through 90 s / reaped at 20 s, the client hearing nothing; `DisposeAsync` 540 ms against 31 ms without the reaper | the same; 954 ms |

### 2.3 What the platforms do on their own (SOURCE, RUN)

| | Managed listener (Linux, macOS) | http.sys (Windows) |
|---|---|---|
| Timers | One, for the wait for a head's first bytes: 90 s, or 15 s on a kept-alive connection, hard-coded (M/HttpConnection.cs:63, :193-195), stopped at the first read and not re-armed while that head is read (:257, :320); armed again for each later request on a kept-alive connection | Entity body, drain, request queue, idle connection and header wait at 120 s each, send rate 150 B/s (DOC; `netsh`, RUN), which resets a stalled write at about (bytes yet to deliver / 150 B/s, rounded up to 5 s) plus 5 to 10 s, by where the stall falls between its checks about 5 s apart (RUN `listener/`, `sweep/send-rate-piece-probe`; the rule is inferred) |
| What a timer sends | A reset; no `408` (:182-186; RUN H0, K1) | A reset; no `408` (RUN H0, B1, K1) |
| `TimeoutManager` | `EntityBody`, `HeaderWait`, `MinSendBytesPerSecond` and `RequestQueue` throw `PlatformNotSupportedException` when set; `DrainEntityBody` and `IdleConnection` are stored and never read (SOURCE §2; RUN `listener/` claim 6) | Settable, on the URL group: a set `EntityBody` or `MinSendBytesPerSecond` governs a connection's first request, a set `HeaderWait` or `IdleConnection` only its later ones (RUN claim 6). Each timer fired up to about 8 s late, the send-rate timer up to 10 s (deadlines are rounded up to a multiple of 5 s and checked about every 5 s; RUN `listener/`, `sweep/`) |
| A body read | Returns what has arrived for a Content-Length body; waits to fill its buffer for a chunked one | Waits to fill its buffer for both (RUN `listener/`) |
| A token on body I/O | Checked once, then ignored | The same (both use the base `Stream`'s Begin/End path) |
| `Abort()` with no head sent | Writes the head with the status set (`200` by default), then closes (dotnet/runtime#88196): a `408` set first reaches the caller with `Connection: close` (RUN claim 3) | Writes nothing; resets |
| A pending body read when `Abort()` runs | Throws: `IOException` for a Content-Length body, `HttpListenerException` 400 for a chunked one (20 of 20, RUN claim 3) | Throws 995 |
| A pending write when `Abort()` runs | A Content-Length write completes with **no exception** within a millisecond, 10 of 10 (RUN claim 2); with a chunked answer `Abort()` itself blocks until the caller reads (5 of 5 over 10 s, claim 1) | Throws 995 or 1236; `Abort()` returns at once; the reset discards what the caller had not read |
| Stopping the listener | Writes `200 OK` to every connection without an answer: a dispatched one stalled in its body, one stalled in its head (a chunked `200` whose last chunk is sent twice) and an idle kept-alive one (RUN B1, H1; claim 4) | Resets at once, no bytes (claim 4) |

### 2.4 How other servers and proxies bound a stalled peer (DOC, SOURCE)

| Who | Head | Body or answer (reset by progress unless marked) | Idle kept-alive | Half-closed TCP | Under a debugger |
|---|---|---|---|---|---|
| Kestrel | 30 s, total | 240 B/s after a 5 s grace (a rate) | 130 s | n/a | none enforced (`TimeoutControl.cs` L62, L123, L157) |
| YARP | n/a | `ActivityTimeout` 100 s | n/a | n/a | always applies |
| nginx | 60 s | 60 s between reads, 60 s between writes | 75 s | stream: ends at the first EOF unless `proxy_half_close` | n/a |
| http.sys | 120 s | 120 s for a body; 150 B/s for an answer | 120 s | n/a | n/a |
| Envoy | off | stream idle 5 min | 1 h | only the 1 h idle | n/a |
| HAProxy | none by default | `timeout client` / `server`, no default | `http-keep-alive`, falling back to `http-request`, then `client` | `client-fin` / `server-fin`, no default; the manual's example 30 s | n/a |
| socat (Testcontainers) | n/a | n/a | n/a | after the first EOF, closes once nothing has moved for 0.5 s (its source re-arms the wait on every pass; the man page reads as a total) | n/a |
| OpenTelemetry Collector, OTLP/HTTP receiver | none by default | none by default | 60 s | n/a | n/a |

All from `source/prior-art.md`, which links each, and the sweep's `external-facts.md` (harness `sweep/`). The fixed
limits on a stalled body or answer there run from 30 s (Jetty, WireMock) to 10 min (mitmproxy, nginx's stream module),
with Kestrel's a rate and HAProxy's unset by default. Kestrel, YARP and Envoy answer a stalled head or body `408`
(Envoy: "terminated with a 408 Request Timeout error code if no upstream response header has been received, otherwise a
stream reset occurs"; its code answers `504` once the request is whole); HAProxy answers a stalled body `400` in its
default streaming mode, and nginx and http.sys send nothing. Envoy's documentation warns that turning its idle limit off
"is likely to yield connection leaks due to lost TCP FIN packets", which is #173's case. The OpenTelemetry Collector's
own OTLP/HTTP receiver closes an idle connection at 60 s and, by default, bounds no stalled head, body or answer (its
factory zeroes both timeouts "For backward compatibility").

## 3. Findings the issue does not state

1. **The platforms differ both ways.** Windows resets a stalled body at about two minutes and the tap counts and logs
   nothing; neither platform released a caller that stopped reading its answer within 200 s, and on Windows the
   send-rate timer gives a whole answer hours (RUN B1, W1; §1 row 4). The same suite holds different things on a
   developer's Windows machine and on Linux CI, and says nothing about either.
2. **Neither listener's timer sends an answer.** The managed listener's and http.sys's timers end in a reset (RUN). A
   caller cannot tell a time limit from a crash.
3. **A stalled answer is a correctness problem, not only a resource one.** The answer write runs inside the exchange's
   in-flight registration (L310-314). With `ProxyTapOptions.InFlightRegistry` on,
   `InFlightIdentityRegistry.MostRecentFor` returns the newest identity in flight, or none when the service is idle; a
   stalled answer keeps an old identity there, so a database tap that resolves its identity through the registry
   attributes that service's idle-time queries, a background job's say, to the stalled test until the tap is disposed
   (READ `InFlightIdentityRegistry.cs`).
4. **Stopping `ProxyTap` on Linux and macOS tells a stalled caller its request succeeded** (RUN B1 and H1, §2.3). The
   dispatched case is fixable in the tap; R0 item 2.
5. **After `Abort()` the listener can report success.** A pending Content-Length write completes with no exception on
   Linux (§1 row 7), so a limit that reads the outcome from the I/O misses its own cut. And with a chunked answer the
   managed `Abort()` blocks until the caller reads, so whatever cuts an answer there must keep it Content-Length (§2.3).
6. **A body read is not a byte.** On Windows, and on Linux for a chunked body, a read waits to fill its buffer (§2.3),
   so "no read completed" is not "no byte arrived"; the size of the reads sets the slowest caller a limit lets through
   (§4.1 item 1).
7. **`HttpListener.TimeoutManager` is not a portable mechanism** (§2.3): four of its six settings throw on the managed
   listener and the other two are never read.
8. **`OtlpTap` reads and allocates before it authenticates.** The body's array is made at its declared length and the
   body read before `ExpectedHeaders` is checked (L288 before L318). On a non-loopback bind, the case the docs ask the
   secret for, a caller without it can pin `MaxRequestBytes` per connection (RUN B1 measures the pinning; R0 item 3,
   Q9).
9. **`OtlpTap` holds a partial head and an idle kept-alive connection until it is disposed** (RUN H1, K1), and, by
   reading, a connection that never sends a byte, which waits at the same call (READ `read/otlptap.md` §0 item 6): it
   has no keep-alive limit, which every HTTP server in §2.4 has.
10. **`TcpTap` holds three kinds of pair, not one, and must keep a fourth.** T1, T2 and T3 are held; T4, the idle pair,
    must stay held, as 3.0.49 learned. Its opt-in reaper ages a command by the wall clock (the pump's stamp, L294,
    against `UtcNow`, L360) and makes `DisposeAsync` wait up to about a second, the pairs' reapers waiting at once (RUN
    T3N, three pairs: 540 ms and 954 ms, against 31 ms), since its tick takes no token (L340).
11. **Held connections end two taps' accept loops.** With descriptors exhausted, `TcpTap`'s and `OtlpTap`'s IPv4 accept
    loops end for good while `ProxyTap`'s listener keeps accepting (PEER, #174's `accept/accept-linux.txt`). #174 fixed
    the loops in 4.20.3 (they pause and try again, and callers wait in the listen queue); this plan stops the stalls
    that hold descriptors.
12. **The taps run inside test processes.** The one long stall a healthy test makes is a debugger pause, and every limit
    has to survive it (§4.1 item 4).
13. **`ProxyTap.ForwardTimeout` set to infinite cuts every forward at 10 s, and no value is checked** (RUN; R0 item 1).
14. **No tap guards its `Log` callback** (READ; R0 item 6), and in `TcpTap` a throwing callback can stop a pair from
    ever being released, which is the leak this issue is about by another route.
15. **The hang-up facts cannot see a caller stalled before its head is whole.** Such a connection sits in
    `HttpEndPointListener._unregisteredConnections`, not in the `_listenerContexts` and `_connections` tables
    `ProxyTapHangUpTests` reads (RUN H0, H1). R1's held-connection check reads all three tables (R1-1, R1-3).

## 4. The design

### 4.1 One rule for every tap: a stall, not a duration

1. **A limit applies to a transfer the tap is in the middle of, and it measures how long the peer has moved no bytes.**
   Every read or write that moves at least one byte starts the wait again. A slow caller that keeps moving is never cut,
   whatever its size or total time (a 1 GB upload at 100 KB/s passes), as long as it moves at least one piece per limit
   (below). That is what a tap can observe (each completed read or write), and it is how nginx (`client_body_timeout`,
   `send_timeout`), YARP (`ActivityTimeout`), Envoy (`stream_idle_timeout`) and http.sys (`EntityBody`) time a body
   (DOC, `source/prior-art.md`). Kestrel's minimum data rate needs per-second byte accounting and catches a peer that
   drips a byte at a time, which a test-time caller does not do (Q8). **A read is not a byte, though.** On Windows, for
   both kinds of body, and on Linux for a chunked one, a body read returns only once its buffer is full or the body
   ends: a 64 KiB read waited for 64 KiB (RUN `listener/`, a surprise of the run). So `ProxyTap` reads in pieces of at
   most 16 KiB and writes in pieces of 16 KiB (the last up to 32 KiB, §4.3 item 4), and its rule is in effect "16 KiB
   per limit", or 32 KiB for an answer's last piece: 164 to 328 bytes a second at the default, the order of http.sys's
   own send rate (150 B/s) and Kestrel's minimum (240 B/s). A caller slower than that over a whole limit is cut, which
   no test-time caller is. On raw sockets (`OtlpTap`, `TcpTap`) a read returns what has arrived, so there a read's rule
   is byte-level. A write counts whole on every platform, since it completes only once the kernel has taken all of it:
   `TcpTap`'s pumps write what they read, up to `ReadBufferBytes` (32 KiB), so a client reading a large reply has to
   take 32 KiB per limit (328 B/s at the default), and `OtlpTap` writes its answers in pieces of at most 16 KiB (§4.4
   item 6), so a reader of a large relayed answer has to take 16 KiB per limit.
2. **An idle connection between exchanges is not a stall.** Pooled database connections sit idle for minutes by design,
   and 3.0.49's first, silence-based reaper killed exactly those (`TcpTapOptions.cs:163-167`, CHANGELOG.md:8451, READ).
   Only `OtlpTap`, an HTTP server like any other, gets a keep-alive limit, as http.sys (2 min) and Kestrel (130 s) have;
   `ProxyTap`'s is its listener's own.
3. **One default for every stall limit: 100 s.** It is YARP's `ActivityTimeout`, and it sits below the timers a tap
   already sits on (http.sys resets a stalled body at about two minutes, RUN 125.2 s, §2.3, and, once R1 writes answers
   in pieces, a stalled 16 KiB piece at 115 to 120 s, §4.3 item 8), so on Windows the tap acts, counts and logs before
   http.sys does, and both platforms behave alike. It is long enough that no well-behaved peer meets it, and short
   enough that a stalled one is released within two minutes rather than at the end of the run (Q1, Q2).
4. **Not while a debugger is attached.** The taps run inside test processes, where a breakpoint pauses the caller (or
   the whole process) for as long as someone reads the screen. Kestrel suspends its connection timeouts (keep-alive,
   request headers, body drain and both minimum data rates) while `Debugger.IsAttached` (`TimeoutControl.cs` L62, L123,
   L157 at `v10.0.13`, SOURCE; documented as "Behavior with debugger attached", DOC), and the request-timeouts
   middleware and SignalR suspend theirs. A stall that ends while a debugger is attached is forgotten; one that outlives
   the detach is cut at the next check (Q5). The check sees only a debugger attached to the tap's own process: a service
   that runs in a process of its own (a container, or a host the run starts) and is paused in a debugger there in the
   middle of a transfer is cut after the limit. The options' doc comments and the wiki say so, with `null` as the
   setting for such a session (§10).

### 4.2 The watch: one mechanism, three taps

An internal type, say `StallWatch` (the release may name it differently), shared by the three packages as one source
file linked into each, the way #174 shares its `TapAcceptLoop` between `TcpTap` and `Otlp`: one `internal sealed` class
in `src/Kronikol.Extensions.TcpTap/` beside `TapAcceptLoop.cs`, its namespace switched per package by a compile symbol
(`KRONIKOL_OTLP_SHARED`, which #174 added in 4.20.3, and a new `KRONIKOL_PROXYTAP_SHARED`), linked as
`Shared\StallWatch.cs`, so no compilation can see two copies under one name. Facts reach each copy through its own
package's `InternalsVisibleTo`, never by linking the file into a test project (CS0436). The MongoDB.V2 links
(`Kronikol.Extensions.MongoDB.V2.csproj` L27-40) share one namespace, which is safe only because no project references
both MongoDB packages (READ, `sweep/notes-repo.md`). The watch:

1. **Entries.** Each transfer that can stall registers an entry: the time of its last progress, what it is waiting on
   (for messages and counters), and an expiry action. Progress is one `Volatile.Write` of a monotonic timestamp
   (`TimeProvider.GetTimestamp()`), so the hot path allocates nothing. An entry is removed when its transfer ends.
2. **One clock per tap.** A heartbeat (`TimeProvider.CreateTimer`, once a second) walks the entries and fires each entry
   whose last progress is older than its limit, once (item 3). A limit is compared as an elapsed `TimeSpan`
   (`TimeProvider.GetElapsedTime`), never added to a timestamp, so a limit as large as `TimeSpan.MaxValue` cannot
   overflow. While `Debugger.IsAttached` the walk fires no limit's entry (§4.1 item 4); the reaper's check, which keeps
   today's behaviour (§4.5 item 5), still runs. The timer is created under `ExecutionContext.SuppressFlow()`: a timer
   keeps the context it was created in, and `StartAsync` runs inside a test, so the heartbeat would otherwise act under
   that test's identity and `Activity` for the tap's life (`ProxyTap` already clears `Activity.Current` for its accept
   loop, L199-211, for the same reason). The heartbeat stops first thing in `DisposeAsync`, which does not wait for a
   callback already running; an action such a callback queues checks that the watch is not disposed before it acts.
3. **Actions never throw, and never hold the heartbeat.** The heartbeat only decides: it queues each fired action to the
   thread pool, so an action that blocks (the managed `Abort()` writes an unsent head synchronously; a host's `Log` or
   `OnCaptureDegraded` may block) holds one pool thread, not every limit of the tap. Each action swallows everything and
   reports through `TryLog` (the guarded log helper both plans name, §4.6, §7). Each entry has one state (active, ended,
   fired) moved only by `Interlocked.CompareExchange`: the transfer's own end moves active to ended before it looks at
   anything else, the heartbeat moves active to fired before it acts, and whoever moves first decides; the transfer's
   code reads a lost swap as the limit's. The walk runs over a concurrent collection and tolerates a second walk at once
   (timer callbacks may overlap), and it never reads the clock while it holds a lock (§5).
4. **Seams for facts** (internal, `InternalsVisibleTo` exists for each test project): the tap's `TimeProvider`
   (`TimeProvider.System` in production; facts use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`,
   a test-only package, and advance it past the limit) and a `Func<bool>` for "a debugger is attached", since a fact
   cannot attach one. There is no `TimeProvider` in `src/` at `3240c983` (READ, `read/tcptap.md` §0 item 8); #174 added
   `TapAcceptLoop.Clock`, an internal `TimeProvider` (4.20.3), so in `TcpTap` and `OtlpTap` one seam feeds both and each
   tap keeps one clock. #174's facts hand-write a `ManualClock` (wall clock only, in `Kronikol.Tests.TcpTap`); the watch
   needs timers and a timestamp, which `FakeTimeProvider` gives (a new test-only package in the three test projects,
   which list their packages inline: there is no central package list), and R3-6 needs the wall clock and the timestamp
   apart, which a hand-written clock gives, under a name of its own.
5. **Why not a token per transfer.** On `ProxyTap`'s listener a token stops nothing once the I/O runs (SOURCE,
   `source/dotnet-listener-timers.md` §4.1), so the action there must be `Abort()`. On `OtlpTap`'s and `TcpTap`'s
   sockets a token does cancel pending I/O (since .NET Core 3.0, corefx#36516; SOURCE, `source/prior-art.md` §8), and
   the socket stays fit to write an answer after a cancelled read (RUN `sweep/cancel-then-answer-probe-*`: .NET 10 on
   both platforms and .NET 8 on Windows; since .NET 9 the runtime keeps it usable on purpose, dotnet/runtime PR #99181),
   so their actions cancel the connection's own token; but the debugger rule and a fake clock need a central check
   either way, and one mechanism is one set of facts.

### 4.3 R1: `ProxyTap` cuts a caller that stalls in its body or its answer (a minor)

1. **Option `CallerStallTimeout`** (`TimeSpan?`, default 100 s; `null` or `Timeout.InfiniteTimeSpan` means none; zero or
   another negative value is refused at start). `Copy` (`ProxyTapServiceCollectionExtensions.cs` L42-77) copies it, and
   `Registering_taps_from_a_list_copies_every_option` (`ProxyTapRegistrationTests.cs` L34-74) gains a `TimeSpan?` case
   in its `NonDefault` table. Its doc comment says what it covers, what it does not (the head, item 7) and that it does
   not lengthen http.sys's own timers on Windows (item 8).
2. **Two phases are watched, nothing else:** the body read (from the first read to the end of the copy, ProxyTap.cs
   L279-299) and the answer write (from the first write to `Close()`, `RespondAsync` L825-859, and the 502's write in
   `TryRespondBadGateway` L887-902, which becomes an awaited `WriteAsync`: today it is a synchronous `Write` (L895), and
   on Windows `Abort()` cannot interrupt a synchronous write, SOURCE §3.4). The forward is `ForwardTimeout`'s, and the
   time between phases is the tap's own.
3. **The body read.** The copy at L285-287 becomes a loop of reads of at most 16 KiB (`CopyToAsync` reads 128 KiB at a
   time: it asks the pool for 81,920 bytes and is handed 131,072), each recording progress when it completes (§4.1 item
   1); the `WrapRequestBody` seam still wraps the stream it reads. On expiry the action calls `answer.TimeOut(408)`: it
   wins the answer's state, sets `StatusCode = 408` and `KeepAlive = false`, then calls `Abort()`. On the managed
   listener that writes `HTTP/1.1 408 Request Timeout` with `Connection: close` and closes, and the pending read throws
   (`IOException` for a Content-Length body, `HttpListenerException` 400 for a chunked one, 20 of 20; RUN `listener/`
   claim 3), the behaviour dotnet/runtime#88196 describes, used here on purpose. On http.sys `Abort()` resets the
   connection, writes nothing, and the read throws 995. When the copy returns, by an exception **or normally**, the
   handler ends the body's entry (active to ended, §4.2 item 3) before it looks at anything else; a lost swap means the
   limit fired first, and the exchange is the limit's whatever the read returned. The heartbeat calls `TimeOut(408)`
   only after winning its own swap (active to fired), so a read that completes between the heartbeat's sample and its
   action either ends the entry first and is forwarded, or loses and is not; never both (R1-11). The source allows a
   chunked read cut by `Abort()` to end as if the body were whole, when the shutdown's end of stream is seen before the
   close (SOURCE §4.3); the run never saw it, but a caller's FIN arriving with the cut does exactly that
   (dotnet/runtime#135680), and the swap settles that case too. The request is not forwarded and not recorded (as #172
   Q1 decided for a caller that hangs up), `ForwardFailures` does not move, `CallerTimeouts` goes up by one and one log
   line names the phase.
4. **The answer write.** `RespondAsync` writes the body in pieces of 16 KiB, the last taking the remainder (16 to 32
   KiB, so that no pending piece is short enough for http.sys to reset before the tap's limit, item 8), recording
   progress after each. Today the whole body goes in one `WriteAsync` (L852, READ), in which no progress is visible, so
   a stall limit would be a total deadline on a large answer to a slow but steady reader. The answer keeps its
   `Content-Length` (`ContentLength64` before the first piece), so the managed listener never sends it chunked: there a
   chunked answer's `Abort()` blocks until the caller reads (more than 10 s in 5 of 5, RUN `listener/` claim 1), which
   would hold the action's pool thread, and the caller's connection, until the caller reads. On expiry the action calls
   `answer.TimeOut()`, which aborts (the head is out, so no status is needed). After the last piece the handler ends the
   answer's entry the same way before `Close()`, and after any write it reads the entry's state, not the exception: on
   the managed listener a pending `WriteAsync` completes **without** an exception within a millisecond of `Abort()` (10
   of 10, and the listener's tables empty; on Windows it throws 995 or 1236; RUN `listener/` claim 2). The exchange
   stays handled and recorded, as one whose caller resets mid-answer is today (`ProxyTapHangUpTests` L46-50), plus
   `CallerTimeouts` and a log line. Its in-flight registration (`InFlightIdentityRegistry`, L310-314) is released when
   the handler returns, which is the point (§3).
5. **`CallerAnswer` becomes safe across threads** (L904-956; `Closed` is a plain property today, READ). One state field:
   open, closing, closed, ended, timed out. `Close()` moves open to closing before `Response.Close()` and closing to
   closed after it, or back to open if `Close()` threw (so a Windows `Close()` that threw 1229 is still aborted, as #172
   designed). `EndUnclosed()` moves open to ended, then sets `StatusIfUnclosed` and aborts. `TimeOut()` moves open to
   timed out, then sets its status and aborts. Each acts only when its move wins, and each swallows every exception.
   Once an answer is timed out or ended, any exception from its I/O is the limit's or the stop's: `RespondAsync`'s typed
   catch (L855) and the catch-all's `answer.Closed` test (L248) read the answer's state, so a write racing `Abort()`
   that throws a type nobody expected is not counted as a forward failure.
6. **Counter and diagnostics.** `ProxyTap.CallerTimeouts` (public, `long`), split in the `Diagnostics()` line by phase:
   `{name}: {n} caller(s) stalled for CallerStallTimeout ({t}) and were cut: {a} sending a request body (not forwarded),
   {b} reading an answer (recorded when captured)` (`DiagnosticKind.CaptureDegraded`, as the tap's capture lines;
   `Diagnostics()` is L108-137). Log lines: `proxy-tap caller stalled {t} sending its request body; not forwarded,
   answered 408 and closed` (on Windows, where http.sys writes nothing on `Abort()`,
   `...; not forwarded, connection reset`) and `proxy-tap caller stalled {t} reading its answer; closed`.
7. **What it cannot reach: a caller stalled inside its request head.** The listener reads the head before the tap sees
   the request. The managed listener times only the wait for the head's first bytes, 90 s on a new connection and 15 s
   on a kept-alive one, closes with a reset and sends no answer; a caller that sends one byte and stops is held until
   the listener closes (SOURCE `source/dotnet-listener-timers.md` §1.1-1.5; the issue's line 320 is right; RUN
   `listener/` claim 5). `HttpListener` is in maintenance mode (a maintainer on dotnet/runtime#123463), so no fix is
   coming. The option's doc comment and the wiki's Limitations section say so (Q6, Q7).
8. **http.sys keeps its own timers; today none of them bounds a caller that stops reading in time.**
   `TimeoutManager.EntityBody`, `HeaderWait`, `MinSendBytesPerSecond` and `RequestQueue` throw
   `PlatformNotSupportedException` when set on the managed listener, and nothing there reads the other two (SOURCE §2),
   so the tap cannot use them as its mechanism. On Windows they still apply: http.sys resets a stalled head, body or
   idle connection at about two minutes (RUN: 125.1-125.2 s), so a `CallerStallTimeout` longer than that does not keep
   such a caller longer there (Q10). Its send-rate timer is no bound on today's answer: a caller that never read an 8 MB
   answer was held through 200 s on Windows as on Linux (RUN, case W1), since the timer scales with what is still to be
   sent (a stalled 64 KiB write was reset at 445.7 s, RUN `listener/` claim 6, so one 8 MB write is allowed about 15.5
   hours; inferred). Once R1 writes in pieces, the timer does bound the answer, piece by piece: a lone stalled 16 KiB
   piece was reset at 120.2 s and a 4 KiB one at 40.0 s, 3 of 3 each, which fits (bytes yet to deliver / 150 B/s,
   rounded up to 5 s) + 10 s for a stall that begins just after one of http.sys's checks, about 5 s apart (a stall later
   in that cycle goes up to 5 s sooner), and a 1 KiB piece behind 96 others went at 396 to 406 s, so the bytes queued
   ahead count too (RUN `sweep/send-rate-piece-probe`; the rule is inferred). Hence item 4's last piece of 16 to 32 KiB:
   no pending piece is short enough for http.sys to reset before about 115 s, so the 100 s default acts, counts and logs
   first on Windows too, where a reset by http.sys would reach the tap only as a failed write (`HttpListenerException`
   1236), uncounted. A `CallerStallTimeout` past about 115 s is capped by http.sys there, uncounted, as the body's is
   past two minutes. The answer's limit is still needed on both platforms.

### 4.4 R2: `OtlpTap` bounds a stalled or idle exporter (a minor)

1. **Option `CallerStallTimeout`**, the same name and meaning as `ProxyTap`'s: a request head, a request body or an
   answer that moves no bytes for this long ends the connection (default 100 s). **Option `KeepAliveTimeout`**: a
   connection waiting for a request's first byte, the first request's or the next one's, is closed after this long
   (default 120 s, http.sys's `IdleConnection`; Kestrel uses 130 s). 120 s sits above every default client idle limit
   whose client would not recover from a close that races its next request (.NET closes an idle connection at 60 to 75
   s, Go at 90 s, JDK 20 and later at 30 s), and the clients that keep a connection longer (OkHttp 5 min; urllib3 and
   Node with no limit) check for the FIN before reuse and retry. At the Collector's 60 s a .NET exporter, which does not
   retry a POST, would meet that race (SOURCE, the sweep's `external-facts.md` §4). Both options take `null` or
   `Timeout.InfiniteTimeSpan` for none, are refused when zero or another negative value, are copied by `Copy`
   (`OtlpTapServiceCollectionExtensions.cs` L42-67), and are checked by a new every-option fact for this package, the
   twin of `ProxyTap`'s (READ, `read/otlptap.md` §0 item 9).
2. **The reader learns when a head starts.** Today an idle keep-alive connection and one inside a head wait at the same
   call (`ReadHeadAsync` L130, then `FillAsync` L288) and nothing marks the first byte (READ). After R2 the wait before
   the first byte is keep-alive and the wait after it is a stall. Unlike `ProxyTap`, this tap reads its own heads, so a
   head that stalls is bounded too. A connection registers with the watch inside `ServeAsync`'s `try`, where its life is
   already handled, never in #174's accept callback, whose throw would reach the accept loop.
3. **The action cancels the connection's own token**, linked to the tap's lifetime, which ends the pending read at once
   with `OperationCanceledException` (0.1 ms). `ServeAsync` then writes its answer on the same socket, which reached the
   caller whole, then a FIN, in every trial on .NET 10 (Windows and Linux) and on .NET 8 (Windows), where the runtime
   marks the socket disconnected after a cancel (dotnet/runtime#69397) and the write still went out (RUN
   `sweep/cancel-then-answer-probe-*`). Cancelling is the one way to end a pending read alike on both platforms:
   `Shutdown` ends it on Linux but not on Windows, where only the close does (variant c). The connection always closes
   afterwards, since the request's framing is lost at the cut, with §4.6 item 3's staged close, so a caller still
   sending does not lose the answer to a reset (variant e: bytes left unread turn the close into an RST). `ServeAsync`'s
   first catch (L298-301), which today swallows a cancellation exactly like a hang-up or the tap stopping, tells the
   three apart by the connection's state (§4.2 item 3), not by the exception.
4. **What each limit does.** Keep-alive: closes, writes nothing, counts nothing (as http.sys and Kestrel close an idle
   connection; it is hygiene, not a failure). A head or body stall: writes `408 Request Timeout` with
   `Connection: close`, under a 5-second bound of its own on the real clock, then closes; not received, `CallerTimeouts`
   up by one, one log line. An answer stall: closes; counted the same way (the export was already received and counted).
   A stall while the reader discards an over-cap body, or inside a chunk-size line, a chunk's line break or the
   trailers, is a body stall: `408` (its `413` has not been sent), `CallerTimeouts`, not `PayloadsRejected`.
5. **Diagnostics.** `OtlpTap.CallerTimeouts` and a `Diagnostics()` line split by phase: `{n} exporter connection(s)
   stalled for CallerStallTimeout ({t}) and were closed: {a} sending an export (not received), {b} reading an answer
   (the export already received)`. The stop line (L611-613) gains the counter.
6. **The answer is written in pieces, like `ProxyTap`'s,** but of at most 16 KiB (a raw socket has no http.sys timer, so
   a short last piece is harmless), with progress after each. Today `WriteResponseAsync` writes a relayed collector body
   in one `WriteAsync` (L471-474), which a stall limit would turn into a total deadline; a collector's answer is small
   as a rule, but nothing bounds a relayed one.

### 4.5 R3: `TcpTap` releases a pair that cannot finish (a minor)

1. **Option `HalfClosedTimeout`** (default 100 s): once one pump has ended (its source sent a FIN, which the tap
   forwards as a FIN, TcpTap.cs:318), the pair is closed when the other moves no bytes for this long, counted from the
   FIN or from its last byte, whichever is later. It is a stall limit like the others (§4.1 item 1), not a total: a
   server that keeps streaming an answer to a client that has half-closed is never cut. Several proxies give a
   half-closed connection a short life: socat, which Testcontainers runs, closes once the other direction has been quiet
   for 0.5 s after the first EOF; nginx's stream module ends the session at the first EOF unless `proxy_half_close` is
   on; HAProxy's manual pairs `timeout client-fin 30s` with `timeout tunnel 1h`. Others fall back to their idle limit:
   Envoy 1 h, mitmproxy 600 s, nginx with `proxy_half_close` 10 min (DOC, `source/prior-art.md` §4-7). Redis and MongoDB
   clients do not half-close and wait, so 100 s cuts nothing they do.
2. **Option `StalledWriteTimeout`** (default 100 s): a pump whose write to its destination has not completed for this
   long (that peer has stopped reading) closes the pair. Both directions: a client that stops reading a large reply, and
   a server that stops reading a client's commands. Today such a pump waits in `WriteAsync` (L299) until the tap is
   disposed (READ).
3. **Neither acts on an idle pair.** A pair with nothing pending and neither side finished is never touched, however
   long it sits (the 3.0.49 rule, §4.1 item 2). A partial command followed by silence (state (e) in `read/tcptap.md` §7)
   is left alone too: it is idle at the TCP level, and the opt-in reaper and the server's own timeouts are the tools for
   it.
4. **Mechanism.** Each pump records its last progress after each completed read and each completed write, marks "write
   pending since" before each `WriteAsync` and clears it after (a `Volatile.Write` of a timestamp each), and records
   when it ended. `ConnectionPumpState.LastActivityTicks` (L731), written after every write and read nowhere since
   3.0.49 (READ), becomes that field or goes. The action cancels the pair's own token and disposes both sockets; both
   pumps then end and the existing `finally` (L255-261) releases the pair. The pair's cancellation is caught where the
   pumps run, as each pump already takes a cancelled read or write for its end (L285, L301): since #174's 4.22.0 the
   connection has one catch, which logs whatever reaches it as an error, so a cancellation from the pair's token that
   reached it would be logged as a connection error.
5. **The reaper moves onto the watch.** `ReapStuckConnectionsAfter` keeps its rule and its default (off), but it is
   checked by the tap's heartbeat on the monotonic clock instead of a loop per pair on the wall clock: a clock jump can
   no longer age a command (finding F7 in `read/tcptap.md` §9), and a pair is no longer released up to a second late by
   an uncancellable `Task.Delay(1_000)` (L340, L245; F8). A command's time reaches the reaper as a wall-clock stamp: the
   pump stamps each segment with `DateTimeOffset.UtcNow` (L294), which is also the recorded interaction's time and stays
   wall clock, and the decoders hand the oldest unanswered one back through the public
   `IProtocolDecoder.OldestUnansweredSince` (a `DateTimeOffset?`, IProtocolDecoder.cs L55). So the reaper takes a
   command's wall-clock age once, when it first sees that oldest value, and from then on adds monotonic time: a jump of
   the wall clock after that sighting cannot age the command, the public interface does not change, and a value that
   changes on every read (the test decoder `StuckBlockingDecoder`, TcpTapTests.cs L768) still ages, so the fact that
   guards the reaper's stand-down after a drop keeps its meaning. The reaper keeps its behaviour under a debugger: the
   debugger rule (§4.1 item 4) is for the new limits, and `ReapStuckConnectionsAfter` reaps today whether or not one is
   attached (Q5 says so). Its action runs on a pool thread like the others (§4.2 item 3), no longer on a task of its
   own, which R3's doc comments say (Appendix A).
6. **Counters.** `TcpTap.HalfClosedPairsClosed` and `TcpTap.StalledWritesClosed`, each with its own `Diagnostics()` line
   in the style of `StuckConnectionsReaped`'s (L565-568), and the stop line (L715-718) names them. No new
   `CaptureDegradationKind`: a pair closed by a limit loses no capture of what it forwarded, and a command cut short is
   already `ConnectionClosedMidMessage`. The lines sit beside the reaped line, after the accept and connect-failure
   lines #174 added in 4.20.3 and 4.22.0 (§7).

### 4.6 R0: what the reading found broken (a patch, before R1)

Found while reading the caller paths, each confirmed by reading and, where marked, by a probe; each gets a fact but item
5 (hygiene: no failure reproduced on either platform measured, so no fact can be red) and item 8 (documentation).

1. **`ProxyTap`: an infinite `ForwardTimeout` cuts every forward at 10 seconds, and no value is checked.**
   `HttpClient.Timeout` is set to `ForwardTimeout + 10 s` (ProxyTap.cs:192). With `Timeout.InfiniteTimeSpan` (-1 ms)
   that is 9.999 s, while the forward's own deadline (L399, `CancelAfter`) treats the same value as "never". Measured
   with an upstream that answers after 20 s: every caller gets a 502 at 10.1 s whose body blames `ConnectTimeout`
   ("could not connect ... within ConnectTimeout (00:00:05)"), although the upstream had received all three requests;
   the 30 s control answers 200 at 20 s. `TimeSpan.Zero` fails every forward at once, -5 s answers 502 with "Specified
   argument was out of the range of valid values" (counted, not recorded), and -10 s makes `StartAsync` throw (an
   `HttpClient.Timeout` of zero) (RUN `probe/forward-timeout-infinite-probe-{windows,linux}.txt`, both platforms alike).
   Fix: a `ForwardTimeout` of `Timeout.InfiniteTimeSpan`, or of `int.MaxValue` milliseconds (24.8 days) or more, means
   no deadline: the client gets an infinite timeout and the forward no `CancelAfter`. Today `TimeSpan.MaxValue` makes
   `StartAsync` throw an `OverflowException` at L192, from 24.8 days less 10 s the `HttpClient.Timeout` setter throws
   there naming `value`, and past 49.7 days `CancelAfter` would throw on every request, outside the forward's `try` (RUN
   `sweep/timeout-limits-check`). `ProxyTapOptions.Validate` (ProxyTapOptions.cs L186-199) refuses zero and every other
   negative value, naming the option, as it refuses other bad options, and treats `ConnectTimeout` (ProxyTapOptions.cs
   L142) the same way: `SocketsHttpHandler` refuses zero, a negative value or more than `int.MaxValue` milliseconds from
   `StartAsync` (ProxyTap.cs L182), naming `value`.
2. **`ProxyTap` on Linux and macOS: stopping the tap answers `200 OK` to a caller still sending its body.** At
   `DisposeAsync` the managed listener closes each dispatched response, which writes its unsent head with the default
   status (dotnet/runtime#88196's mechanism), so each caller stalled mid-upload heard `HTTP/1.1 200 OK` for a request
   the upstream never saw, one a second, since the response's close first waits a second for the rest of the body
   (`FlushInput`, SOURCE §1.6): `DisposeAsync` took 3,028 ms for three callers, past its own 2-second drain (RUN
   `probe/proxytap-stall-probe-linux.txt`, case B1). A caller stalled inside its head hears the same `200 OK`, from a
   context the tap never saw. Fix: `DisposeAsync` settles each answer by its phase before the listener closes (the
   in-flight answers are tracked, where today only their count is), so no caller the tap has dispatched is told a
   request succeeded that the tap did not answer, and no close waits on a body:
   - an answer whose request body is still being read, or not yet begun, is ended at once with `503 Service Unavailable`
     (on Windows a reset, since `Abort()` writes nothing there);
   - a forward in flight keeps today's stopping path: the cancel cuts it, and its handler records the failure and writes
     its own `502` (L452-472), the status its record names;
   - an answer already in hand is left to finish within the 2-second drain, which therefore runs before
     `_listener.Close()` (today the close, L980, comes first and the drain, L999-1000, after it);
   - what is still open after the drain is aborted, with `503` where its head is unsent;
   - until the close, the accept loop keeps taking requests and answers each new one `503` with `Connection: close` at
     once, never forwarded, so a request that arrives while the tap stops is refused rather than left to the listener's
     close, which answered every connection it held `200 OK` in the run (`listener/` claim 4).

   That ends an answer from a second thread while its handler may be closing it, so `CallerAnswer`'s single state field
   (§4.3 item 5) lands here, in R0, with the two catches that read it (L248, L855), since a write racing R0's own
   `Abort()` must not count as a forward failure; R1 adds only `TimeOut()`. The head case stays the listener's (§4.3
   item 7).
3. **`OtlpTap`: the shared secret is checked after the body is read** (`IsAuthenticated` at L318, after `ReadAsync` at
   L288), **and the body's array is allocated at its declared length before any of it arrives** (HttpRequestReader.cs
   L154). On a non-loopback bind, where the docs ask for `ExpectedHeaders` (OtlpTap.cs:32-33), a caller without the
   secret can make the tap allocate up to `MaxRequestBytes` (32 MiB) per connection and hold it by stalling (READ,
   `read/otlptap.md` §0 item 7; RUN `probe/otlp-stall` measured the pinning with no secret configured). Fix: check
   `ExpectedHeaders` once the head is read, before the size and before the body (an over-cap request without the secret
   is then a `401`, counted in `UnauthenticatedRequests`, where today it is a `413`), and answer `401` with
   `Connection: close` and a staged close: the answer, `Shutdown(Send)`, then the body read and thrown away into the
   reader's 16 KiB buffer until EOF or its declared length, giving up after 5 s without a byte or 30 s in all (nginx's
   `lingering_timeout` and `lingering_time`), on the real clock as the 408's own bound is, and only then the close; a
   fact asserts the answer and the FIN, not the close, which a silent caller delays by 5 s. Closing at once loses the
   401 to a reset whenever body bytes are still unread: an `HttpClient` export of 8 MiB never heard it on either
   platform, one of 1 MiB mostly not, and one of 64 KiB sometimes not on Windows, while the staged close delivered it in
   120 of 120 trials on each platform (RUN `sweep/early-answer-probe-*`; the reader's own 413 drain exists for the same
   reason, HttpRequestReader.cs L98). Nothing is kept, so the memory fix holds. Grow the body's buffer as bytes arrive
   (up to `MaxRequestBytes`) instead of allocating the declared length up front. See Q9 for when this text may be
   public.
4. **`OtlpTap`: a `Content-Length` that is not a plain number in `int` range is read as "no body"**
   (HttpRequestReader.cs L92-94): `abc`, `10, 10`, `3000000000` and `2147483648` fail the parse, and `-5` parses and
   falls through on `length > 0` (L94). The export is received with an empty body (answered `200` without a
   `ForwardBaseUri`; with one, the empty body is forwarded and the collector's answer relayed, OtlpTap.cs L345-357), and
   on a kept-alive connection its body is then read as the next request's head, so the connection is out of step. The
   parse also takes a sign: `+5` is read as a 5-byte body, where RFC 9110 §8.6 allows digits only. Fix, per value: one
   that is not a run of digits (`abc`, `-5`, `+5`) is invalid framing, which RFC 9112 §6.3 answers `400` and a close; a
   list of identical values (`10, 10`) may be rejected or read as one value (RFC 9110 §8.6), and is rejected the same
   way, as the reader has no list parsing to trust; a run of digits past `int` (`3000000000`) is over `MaxRequestBytes`,
   so `413` and a close, as a chunked size past `int` already is. The parse takes digits only (`NumberStyles.None`), and
   `0` stays an empty body. Each refusal closes with item 3's staged close.
5. **`OtlpTap`: `NoDelay` is set before the `using`** (OtlpTap.cs:281, `using (client)` at L282), so if setting it
   threw, the catch at L298 would leave the `TcpClient` to its finalizer, holding a descriptor until a GC. Setting it on
   a socket the peer had reset did not throw, 3 of 3 on Windows and on Linux (RUN `sweep/line-refs-probe`); a
   BSD-derived stack such as macOS's may refuse `setsockopt` on a reset connection (not run). Hygiene, not a measured
   bug, so it rides along with R0: inside the `using`.
6. **Unguarded `Log` callbacks in all three taps.** A `Log` bound to xUnit's `ITestOutputHelper` throws
   `InvalidOperationException` once its test has ended (reported by kronikol-71 for #174, not re-measured). In `TcpTap`
   a throwing callback at L383 escapes `PumpAsync`: `Closed` is never set and the decode task waits for ever; at L257
   the sockets are not disposed and `_live` never falls; at L365 a counted reap disposes nothing; at L447 and L517 and
   in `Diagnostic` (L690, which decoders reach through `context.Log`) it turns into a decoder error and disables
   decoding; at L478, in that error's handling, a callback that always throws fails again in the outer catch (L431), so
   the decode task faults and stops draining, and `DecodingDisabledConnections` and `DecodeErrors` count twice. L495,
   `Degrade`'s line for an `OnCaptureDegraded` that threw, escapes `PumpAsync` from a pump's drop (L391) and skips the
   reap's disposal (L366); it was #174's, and 4.22.0 guarded it, since that release sends its own events through
   `Degrade` (READ, `read/tcptap.md` §9 F3, `sweep/notes-line-refs.md`). `TcpTap`'s `TryReset` catch (L466) and
   `ResolveIdentity` catch (L672), which decoders reach, are the same. `ProxyTap`'s calls (L168, 212, 247, 459, 468,
   608, 1004) and `OtlpTap`'s per-connection calls (L304, 313, 321, 341, 409 and 448; at each but the first a throw
   comes before the answer is written, so the exporter gets a closed connection and no answer) are unguarded the same
   way, and `OtlpTap`'s `MapPayload` catch (L526) is worse: a throw there ends the map loop for good (L481-498 catches
   only a cancellation), so no later export is mapped, and `DisposeAsync`, which awaits that loop catching only a
   cancellation (L601-606), then throws. Fix: `private void TryLog(string message)` in each tap,
   `try { _options.Log?.Invoke(message); } catch { }`, the name agreed with #174. #174 added it to `TcpTap` and
   `OtlpTap` with the lines it owns (the accept loops and the start and stop lines in 4.20.3, `TcpTap` L253 and L495 in
   4.22.0); R0 adds it to `ProxyTap` and moves every other site above onto it (§7).
7. **No tap refuses a negative cap.** `TcpTap`'s `BodyCapBytes` (`TcpTapOptions.cs` L96, `Validate` L188-214): for Redis
   the parser's constructor throws inside the decoder factory and every pair closes right after its upstream connect;
   for MongoDB decoding is disabled at the first record (READ, F1). `ProxyTap`'s `BodyCapBytes` (`ProxyTapOptions.cs`
   L54) and `OtlpTap`'s `ContentCapBytes` (`OtlpTapOptions.cs` L125) are applied as a range (`text[..cap]`, ProxyTap.cs
   L740-741; `value[..limit]`, SpanToInteractionMapper.cs L418-425), which throws for a negative cap: in `ProxyTap`
   every exchange with a body becomes a capture fault and is not recorded, and in `OtlpTap` every payload with a capped
   attribute fails (READ, `sweep/notes-line-refs.md`). Fix: each tap's `Validate` refuses a negative cap, naming the
   option.
8. **`TcpTap`: doc comments that are false** (F9): `CaptureDegradationKind`'s summary ("Forwarding is never affected by
   any of them", CaptureDegradation.cs:3) beside `StuckConnectionReaped`, which closes connections; and
   `OnCaptureDegraded`'s "Invoked on the decoder task" (TcpTapOptions.cs L136-142), when the reaper calls it on its own
   task (L366), with a list of events that leaves out the reap. **This was #174's:** 4.22.0 rewrote both, with
   `CaptureDegraded`'s summary (`DiagnosticEntry.cs` L41-45), so nothing is left for R0; R3 changes the thread
   `OnCaptureDegraded`'s doc names for a reap (Appendix A). The stop line (L715-718), a log string that leaves out the
   reaped and closed-mid-message counts, is R3's, which adds its counters after the failed accepts and connects #174
   added.
9. **`OtlpTap`: an over-cap body cut short is still refused as too large.** `DiscardAsync` returns quietly at the end of
   the stream (HttpRequestReader.cs L255-266), and L100 then returns a too-large request, so an exporter that hung up
   mid-drain moves `PayloadsRejected` and is written a `413` it will never read (READ, `read/otlptap.md` §11 item 4; the
   chunked drain already returns nothing since #172). Fix: a drain cut short ends the request as a truncated body does
   (L104-105): nothing counted, no answer.
10. **`OtlpTap`: the stop line leaves out `PayloadsRejected` and `PayloadsFailed`** (L611-613; `read/otlptap.md` §11
    item 6). Fix: name them. #174 added its failed accepts to the same line in 4.20.3.
11. **`OtlpTap`: a request with both `Transfer-Encoding: chunked` and `Content-Length` keeps its connection**
    (HttpRequestReader.cs L84-91), where RFC 9112 §6.1 says the server "MUST close the connection after responding to
    such a request" (`read/otlptap.md` §11 item 7). Fix: answer it, then close.
12. **`ProxyTap`: a handler that reaches the forward after `DisposeAsync` has disposed `_cts` (L1003) throws
    `ObjectDisposedException` at `_cts?.Token` (L397),** behind a slow `IdentityResolver` for instance, and the
    catch-all counts a forward failure and tries a `502`, which `ForwardFailures`' own doc rules out (L82-87: "Nor is a
    forward the tap's own disposal cut short"; `read/proxytap.md` §1). Fix: take the lifetime token once, while the tap
    runs, and treat a disposed tap as the stopping path.

### 4.7 Alternatives considered

| Alternative | Verdict |
|---|---|
| A total deadline per exchange | Cuts a large transfer to a slow but steady peer. Rejected (§4.1 item 1) |
| A minimum data rate (Kestrel's 240 B/s after a 5 s grace) | Needs byte accounting per second; it catches a drip-feeder, which a test-time peer is not. Q8 |
| `HttpListener.TimeoutManager` | Throws `PlatformNotSupportedException` on the managed listener for four of its six settings and is never read for the other two (SOURCE §2). Not a mechanism; Q10 asks whether to set it on Windows as well |
| A cancellation token on `ProxyTap`'s listener streams | Checked once, then ignored while the I/O runs (SOURCE §4.1). Rejected |
| TCP keep-alive on `OtlpTap`'s and `TcpTap`'s sockets | Finds a peer whose host is gone, never a stalled or paused process, whose kernel still answers. A complement, not a fix. Q11 |
| An idle timeout for `TcpTap` pairs | Kills pooled connections (3.0.49). Rejected |
| Moving `ProxyTap` off `HttpListener` (Kestrel, or the raw-socket reader `OtlpTap` already has) | The only way to bound a stalled head on Linux and macOS, and it would retire the managed listener's other defects (dotnet/runtime#39183, #88196, #135680, the head timer). A rewrite of the tap's HTTP layer, with a framework dependency in Kestrel's case. Q6 |

### 4.8 Not in this plan

- A `ProxyTap` caller stalled inside its request head (§4.3 item 7; Q6, Q7).
- Both taps' accept loops, `TcpTap`'s connect path, `ConnectTimeout` and the connect-failure counters: #174 (§7).
- The upstream side. `ProxyTap` has `ForwardTimeout`; `OtlpTap`'s forward has a fixed 30-second `HttpClient.Timeout`
  with no option (OtlpTap.cs:172), which is §8's.
- Caps on how many connections a tap holds (§8, Q13).
- A `TcpTap` pair held by its own capture: `await decodeTask` (L244) keeps the sockets while a blocked sink or
  `IdentityResolver` runs. That is the capture's wait, not a peer's.

## 5. Tests, red first

`tests/Kronikol.Tests.ProxyTap`, `tests/Kronikol.Tests.Otlp` and `tests/Kronikol.Tests.TcpTap`, each with internals
visible. Facts that drive the limit through the watch use `FakeTimeProvider` and move it in step with the I/O they
drive: a "steady" fact moves it a fixed step below the limit for each piece moved, a "stall" fact moves it past the
limit. They wait for no real timer. They do not race the tap either: each step is ordered through a seam on the watch,
which lists its entries by phase with their state and last-progress stamp. A "steady" fact waits (in real time, with a
patience) until the entry's stamp has reached the current fake time before it moves the clock again, and a "stall" fact
waits for the entry to exist and its stamp to stop moving before it moves the clock past the limit; without that, a fact
that runs ahead of the tap's reads sees a steady caller cut, and one that advances before the entry exists sees nothing.
`FakeTimeProvider` runs due timer callbacks on whichever thread next reads or moves it (the fact's `Advance`, or a tap
thread recording progress), so every assertion polls for its effect (a counter, a log line, a close) with a patience
rather than asserting right after `Advance`, and the watch never reads the clock while it holds a lock. Actions run on
the thread pool (§4.2 item 3), so an action that blocks (R1-5b's mutation, a chunked `Abort()`) cannot hang `Advance`:
the fact waits for a close that never comes and goes red within its patience. Every fact sets the debugger seam to "not
attached", so the suite also passes under a debugger. R1-1, R2-1 and R3-1 also run on the real clock with a 1-second
limit, on both platforms (the tap's limit acts long before http.sys's, so nothing needs a skip). Callers and peers are
raw sockets, as in `ProxyTapHangUpTests` and `OtlpTapHangUpTests`; listeners take ports from `LoopbackPort`, which #174
linked into `Kronikol.Tests.TcpTap` in 4.22.0 (it lacked it at `3240c983`, `read/tcptap.md` §8). CI runs the four tap
test projects (these three and `Kronikol.Tests.TcpTap.Bson3`) on Linux only (`ci.yml` L60, L79-86), so before each
release every fact also runs on Windows, as #174's do, and the run is recorded in the harness. "Red before the fix"
means on the previous release with only the seams added: the `TimeProvider`, read wherever the tap reads a clock today,
and the debugger flag.

| Fact | Release | Red before the fix | Proves |
|---|---|---|---|
| R0-1 `An_infinite_forward_timeout_never_cuts_a_slow_upstream` | R0 | all | an upstream that answers after 15 s with `ForwardTimeout = InfiniteTimeSpan` is answered 200, not 502 |
| R0-2 `An_unauthenticated_export_is_refused_before_its_body_is_read` | R0 | the first case (4.20.2 answers nothing until the body arrives); the second is a guard | a head declaring 32 MiB and 10 body bytes, no secret: `401` and a close while the rest of the body is unsent, `UnauthenticatedRequests` 1, nothing queued; and an `HttpClient` export of 8 MiB without the secret hears `401`, not a reset (a small body passes even with an immediate close, RUN `sweep/early-answer-probe-*`). Kills "close at once" |
| R0-3 `A_declared_length_is_not_allocated_before_it_arrives` | R0 | all | `HttpRequestReader` over a stream that gives a head declaring 32 MiB and 10 body bytes, then waits: the reader holds under 1 MiB for the body (read through an internal property; the process's allocation counters move with every fact running beside it, so they are not used); the rest then arrives and the body is read whole |
| R0-4 `A_content_length_that_is_not_digits_in_range_is_refused` (theory: `abc`, `-5`, `+5` and `10, 10` answered 400; `3000000000` and `2147483648` answered 413; `0` still an empty body) | R0 | all | the answer and a close, `RequestsReceived` 0, nothing queued, the connection not read out of step |
| R0-5 `A_throwing_log_callback_changes_nothing` (theory per tap and per call site that a fact can reach) | R0 | all | each path ends as it would with no callback: the pair released and `LiveConnections` 0, decoding not disabled, the reap disposed, the counters unchanged |
| R0-6 `A_negative_cap_is_refused` (theory: `TcpTap`'s and `ProxyTap`'s `BodyCapBytes`, `OtlpTap`'s `ContentCapBytes`) | R0 | all | `Validate` throws, naming the option |
| R0-7 `Forward_and_connect_timeouts_are_checked_at_start` (theory: zero, -5 s and -10 s refused, naming the option; `TimeSpan.MaxValue` and 30 days accepted as no deadline; the same for `ConnectTimeout`) | R0 | all | today zero and -5 s start and fail every forward, -10 s throws from `HttpClient`, `TimeSpan.MaxValue` throws `OverflowException` and 30 days throws from the `HttpClient.Timeout` setter |
| R0-8 `Stopping_the_tap_never_tells_a_caller_its_request_succeeded` (theory by phase) | R0 | Linux for the stalled body (`200 OK` and about a second each, RUN B1), and for the request during the drain and the answer in hand, which 4.20.2's close before its drain (L980, L999-1000) refuses or cuts; the forward's `502` guards its phase (it races that close today, not measured) | four callers stalled mid-body hear `503` (a reset on http.sys), never `200`, and `DisposeAsync` takes under 2.5 s; a forward cut by disposal hears the `502` its record names; an answer in hand is delivered whole, never replaced by `503`; a request that arrives during the drain hears `503` |
| R0-9 `An_over_cap_body_cut_short_is_not_counted` | R0 | all | an exporter declares 64 MiB, sends 1 MiB and hangs up: `PayloadsRejected` 0, nothing written |
| R0-10 `The_stop_line_names_every_counter` (`OtlpTap`) | R0 | all | the stop line names `PayloadsRejected` and `PayloadsFailed` |
| R0-11 `A_request_with_both_framings_closes_its_connection` | R0 | all | `Transfer-Encoding: chunked` with `Content-Length`: answered, then closed; a second request on the connection is not read |
| R0-12 `A_handler_that_outlives_disposal_is_not_a_forward_failure` | R0 | all | an `IdentityResolver` that blocks past `DisposeAsync`: `ForwardFailures` 0, no `502` tried |
| R1-1 `A_caller_that_stalls_in_its_body_is_cut_and_not_forwarded` (theory: Content-Length, chunked, a head with no body byte) | R1 | all (Linux holds it, http.sys for 2 min) | managed: `HTTP/1.1 408` then a close; http.sys: a reset. The upstream saw nothing, `CallerTimeouts` 1, `ForwardFailures` 0, `InFlight` 0, the log line. On Linux only the chunked and no-byte cases tell "armed at the first completed read" from "armed when the read is issued" |
| R1-2 `A_body_read_that_ends_after_the_limit_fired_is_not_forwarded` | R1 | none on R0's release, which has no limit; the mutation that forwards after a lost swap turns it red | the `WrapRequestBody` seam ends the body normally once the answer has timed out, the race §4.3 item 3 guards: nothing forwarded |
| R1-3 `A_caller_that_stops_reading_its_answer_is_cut_and_recorded` | R1 | all | 8 MB answer, caller reads nothing: released, recorded, `CallerTimeouts` 1, `InFlight` 0, `MostRecentFor` null afterwards |
| R1-4 `A_slow_steady_body_is_never_cut` (theory: Content-Length, chunked) | R1 | none (a guard) | the caller sends 16 KiB and the fake clock moves 0.6 s, forty times, against a 1 s limit: forwarded whole. Kills a total deadline; the chunked case also kills reads larger than 16 KiB on every platform, since a chunked read waits for its buffer (§2.3) |
| R1-5 `A_slow_steady_reader_is_never_cut` | R1 | none (a guard) | an 8 MB answer to a caller with a 16 KiB receive buffer, which reads 1 MiB and lets the fake clock move 0.6 s, against a 1 s limit: the whole answer. 8 MB is past what the socket buffers hold (about 2.4 MB on Linux, RUN W1), so it kills a single `WriteAsync` on Linux too; the caller finishes well inside the managed listener's 15 s keep-alive reset (§8) |
| R1-5b `An_answer_cut_by_the_limit_ends_at_once` | R1 | none (a guard) | the caller stops reading an 8 MB answer: the tap's side closes within the limit plus a tick. Kills an answer sent chunked on Linux, whose `Abort()` blocks until the caller reads (RUN `listener/` claim 1); on Windows that `Abort()` returns at once |
| R1-6 `A_slow_upstream_is_not_the_callers_stall` | R1 | none (a guard) | the upstream holds its answer while the fake clock moves 2 s, against a 1 s limit: answered 200 |
| R1-7 `The_limit_and_the_answer_racing_end_it_once` | R1 | by mutation (R0's `CallerAnswer` has no `TimeOut` to race) | `TimeOut`, `Close` and `EndUnclosed` from three threads, 1,000 rounds: one wins, nothing throws, no abort after a close that succeeded |
| R1-8 `No_limit_applies_while_a_debugger_is_attached` | R1 | none (new seam) | attached: nothing cut past the limit; detached: cut at the next tick |
| R1-9 `The_stall_timeout_option` (defaults, `null`, infinite, `TimeSpan.MaxValue`, refused values, `Copy`) | R1 | none (new option) | `TimeSpan.MaxValue` is accepted and never cuts (the watch compares elapsed time, §4.2 item 2) |
| R1-10 `Keep_alive_requests_leave_no_entry_behind` | R1 | none (a guard) | 20 requests on one connection with the fake clock moved past the limit between them, and the same after the `400` path and after a `502`: `CallerTimeouts` 0, and the watch holds no entry after each exchange (the existing `Keep_alive_requests_are_answered_on_one_connection`, `ProxyTapHangUpTests.cs` L264, cannot see an entry left armed). Kills "never remove the entry" |
| R1-11 `A_read_completing_as_the_limit_fires_is_forwarded_or_cut_never_both` (theory: the body, the answer) | R1 | by mutation (a check in place of the swap) | a seam holds the heartbeat between its sample of the entry and its swap, and the read (or the last piece) completes in that gap: forwarded, `200`, `CallerTimeouts` 0, the answer delivered whole |
| R1-12 `The_answer_is_cut_into_pieces_of_16_KiB_and_a_longer_last` (theory over lengths 0, 1, 16,383, 16,384, 16,385, 32,767, 32,768, 32,769 and 8 MB) | R1 | none (new code) | every piece is 16 KiB but the last, which is 16 to 32 KiB unless the whole answer is shorter, and the pieces add up to the answer. Kills a short last piece |
| W-1 the watch's own facts, on the watch alone with a `FakeTimeProvider` (in `Kronikol.Tests.ProxyTap`, since it lands with R1; the other packages compile the same file) | R1 | none (a new type) | an entry past its limit acts once and never again; progress restarts the wait; an entry removed before its limit never acts; an action that throws is swallowed and logged; while the debugger flag is set no limit's entry acts (a reaper's still does), and an entry still stalled after it clears acts at the next tick; after `DisposeAsync` returns no walk starts and no queued action acts; an entry added during a walk is seen at the next tick |
| R2-1 `A_stalled_head_is_answered_408_and_closed` | R2 | all | `CallerTimeouts` 1, the log line |
| R2-2 `A_stalled_body_is_answered_408_and_not_received` | R2 | all | `RequestsReceived` 0, nothing queued |
| R2-3 `An_idle_keep_alive_connection_is_closed_quietly` | R2 | all | still open past `CallerStallTimeout`, closed after `KeepAliveTimeout`, no bytes written, `CallerTimeouts` 0 |
| R2-4 `A_connection_that_never_sends_is_closed_after_the_keep_alive_limit` | R2 | all | the same for a connection that sends nothing at all |
| R2-5 `A_slow_steady_export_is_received` and `An_exporter_that_reuses_its_connection_keeps_it` | R2 | none (guards) | an export of 16 KiB per 0.6 s step of the fake clock, forty steps, is received whole; three exports on one connection, with the clock moved past `CallerStallTimeout` (not past `KeepAliveTimeout`) between them, share it. Kills a total deadline and a keep-alive wait timed by the stall limit |
| R2-6 `Disposing_the_tap_is_not_a_time_limit` | R2 | none (a guard) | a stall cut by `DisposeAsync`: `CallerTimeouts` 0 |
| R2-7 `Registering_taps_copies_every_option` for `OtlpTap` | R2 | none (new) | the twin of `ProxyTap`'s fact, which the package lacks |
| R2-8 `A_slow_collector_is_not_the_exporters_stall` | R2 | none (a guard) | in forward mode the collector holds its answer while the fake clock moves past the limit: the export is answered, `CallerTimeouts` 0. Kills "watch the forward" |
| R2-9 `A_stall_inside_a_body_is_a_body_stall` (theory: an over-cap body being discarded, a chunk-size line, a chunk's line break, the trailers) | R2 | all | `408`, `CallerTimeouts` 1, `PayloadsRejected` 0, nothing received |
| R2-10 `A_relayed_answer_is_written_in_pieces` | R2 | none (a guard) | a collector relays an 8 MB answer: an exporter that stops reading is cut and counted as an answer stall; one that reads 1 MiB per 0.6 s step of the fake clock is not. Kills a single `WriteAsync` |
| R3-1 `A_pair_whose_upstream_stays_silent_after_the_client_half_closes_is_released` | R3 | all | `HalfClosedPairsClosed` 1, `LiveConnections` 0, both sockets closed |
| R3-2 `A_pair_whose_client_stays_silent_after_the_upstream_half_closes_is_released` | R3 | all | the other direction |
| R3-3 `An_idle_pair_is_never_closed` | R3 | none (a guard; the 3.0.49 rule) | a pair that has carried a command and its reply, then sits idle, outlives three times both limits (traffic first, as `AnIdleConnectionWithNothingUnansweredIsNeverReaped` does, or a write-pending mark never cleared would go unseen) |
| R3-4 `A_client_that_stops_reading_a_large_reply_releases_the_pair` and its upstream twin | R3 | all | `StalledWritesClosed` 1 |
| R3-5 `A_slow_steady_reader_keeps_its_pair`, `A_half_close_answered_in_time_ends_normally` and `A_half_closed_pair_that_keeps_moving_is_kept` | R3 | none (guards) | the last: after the client's FIN the upstream sends 16 KiB per 0.6 s step of the fake clock for three times the limit, and the pair ends only at the upstream's own FIN |
| R3-6 `The_reaper_ignores_a_wall_clock_jump` | R3 | all, with the pump's stamp (L294) and the reaper's `UtcNow` (L360) read through the seam's `GetUtcNow` | a `TimeProvider` of the fact's own that delegates `GetTimestamp`, `TimestampFrequency` and `CreateTimer` to a `FakeTimeProvider` and offsets only `GetUtcNow` (a subclass cannot: `FakeTimeProvider.GetTimestamp` reads `GetUtcNow`); its wall clock jumps an hour after the reaper has seen the command: no reap, and the recorded interaction keeps its wall-clock time. On the old reaper the jump reaps a command sent a second before |
| R3-7 `CopyTo_copies_every_option` for `TcpTapOptions` | R3 | none (new) | every settable property differs from its default after `CopyTo` (TcpTapOptions.cs L217-252), found by reflection as in `ProxyTap`'s fact. `TcpTap` has no registration `Copy`: the Redis and Mongo list registrations copy through `CopyTo` (TcpTapServiceCollectionExtensions.cs L37, L71), the generic tap has no list registration, and the hand-picked `CopyToCarriesEveryProtocolOption` (TcpTapTests.cs L636-666) cannot see a new option left out |
| R3-8 `Disposing_with_the_reaper_on_does_not_wait_for_its_tick` | R3 | all (RUN T3N: 540 ms and 954 ms) | three reaper-on pairs, five rounds: the slowest `DisposeAsync` under 250 ms. On 4.20.2 the reaper's uncancellable one-second delay (L340, F8) makes each round wait up to a second, so all five come in under it about one time in a thousand |

Proofs, as the recent plans give them: every fact that is not a guard red on the previous release (Linux container for
the managed listener's, recipe in the harness README) and green on the fix, and these mutations killed: forward after
the body's entry lost its swap (R1-2); write the answer in one piece again (R1-5); send it chunked (R1-5b); move the
watch into the forward (R1-6); drop the `Interlocked` move in `TimeOut` (R1-7); drop the debugger check (R1-8); count a
keep-alive close (R2-3); arm the half-close limit on an idle pair (R3-3); read the wall clock in the reaper again
(R3-6); time the half-close from the FIN, not from the last byte (R3-5); give the reaper's tick a delay of its own again
(R3-8); close at once after the 401 (R0-2); never remove an entry (R1-10); a check in place of the entry's swap (R1-11);
watch the forward (R2-8); count a stalled drain as a 413 (R2-9); write the relayed answer in one piece (R2-10); a short
last piece (R1-12); arm the body's entry at the first completed read (R1-1); a total deadline on the body, and reads of
32 KiB (R1-4); never clear the write-pending mark (R3-3); drop the status before `Abort()` (R1-1: the caller hears
`200`). Then each project on Windows and in the Linux container, the core suite after the version bump, and
`release.slnf` in Release for every target.

## 6. Releases and records

### 6.1 Releases

- **R0**, the next free patch: §4.6, bug fixes, nothing new to call.
- **R1, R2, R3**, each the next free minor in turn: each adds options and a counter, which is new public surface. Each
  also changes what untouched code does, which CLAUDE.md makes a MAJOR trigger unless the cut is read as the fix of a
  defect (a fix that changes behaviour does not raise the bump, and the new options make the release a minor); the
  changelog says so plainly, and Q1 is the owner's call. The shared watch lands with R1.
- Numbers are claimed with "taking X now" just before each push. #174 and #165 have shipped, with the audits of #168
  and #169 between them (4.20.3 to 4.22.1, all on 2026-10-15); #175 and #78/#124 are planned.

### 6.2 Changelog draft (R1)

> **Added.** `ProxyTapOptions.CallerStallTimeout` (default 100 s) and `ProxyTap.CallerTimeouts` (#173). A caller that
> stops sending its request body, or stops reading its answer, for that long is cut: a body with `408 Request Timeout`
> (on Windows http.sys resets the connection instead), an answer by closing the connection. On Linux and macOS such a
> caller held its connection, and the tap's handler, until the tap was disposed; on Windows http.sys reset a stalled
> body after two minutes, with nothing counted, and gave a caller that stopped reading as long as 150 bytes a second
> allowed, hours for an answer of a few megabytes. A slow caller that keeps moving (16 KiB per limit, up to 32 KiB for
> an answer's last piece) is never cut, the wait for the upstream is still `ForwardTimeout`'s, and no limit applies
> while a debugger is attached. A body cut this way is not forwarded; an answer cut this way is still recorded when
> captured, and the caller's in-flight identity is released. A caller that stalls inside its request head is beyond the
> tap: the managed `HttpListener` reads the head first and holds such a caller until the tap stops (see the wiki's
> Limitations). Behaviour change: the limit is on by default, so such a caller is now cut where it was held;
> `CallerStallTimeout = null` keeps the old behaviour. A minor: a new option and a new counter, with the cut read as the
> fix of a defect rather than a changed default (Q1).

R2's and R3's follow the same form; R0's lists §4.6 item by item, item 8 aside (#174's).

### 6.3 Wiki (`../Kronikol.wiki`)

Line numbers are at `origin/master` `60daf39` and are taken again at publication, in a worktree of its own (the local
clone is 52 commits behind). Each page already says things that are false, and each release corrects the ones it touches
(READ, `git show 60daf39:<page>` in `../Kronikol.wiki`):

- `Integration-ProxyTap-Extension.md`. R0: L80's "Stopping a tap" says what each caller hears when the tap stops (R0
  item 2: `503` for a request still arriving, the `502` its record names for a forward cut short, an answer in hand
  finished), and its "≤ 2 s" holds again once no close waits on a body (3,028 ms measured); the `ForwardTimeout` /
  `ConnectTimeout` row (L125) says that an infinite or very large value means no deadline and which values are refused;
  a negative `BodyCapBytes` is refused. R1: the option in the options table (beside L125); a paragraph beside "A caller
  that hangs up" (L82-93) on a caller that stalls, which corrects L89-90 (a caller that stops sending mid-body is not
  answered `400`: it is held, and after R1 answered `408` and closed, or reset on Windows); L194's "`null` when the
  service is idle", which a caller stalled reading its answer makes false until the limit; the counter beside the
  `CaptureDegraded` line (L212-213); and Limitations bullets (L236-243) on the head and on a debugger attached to
  another process.
- `Integration-Otlp-Extension.md`. R0: L30's "Authenticates" becomes true, and an over-cap request without the secret is
  now a `401`, counted as unauthenticated, not a `413`; L129 gains the `400` and `413` for a `Content-Length` that is
  not digits in range, a request with both framings is answered and closed, and an over-cap export cut short mid-drain
  is no longer answered `413` or counted; a negative `ContentCapBytes` is refused. R2: both options in the table
  (L127-129), the counter beside the counters (L143), and the time limits in L160's "`MaxRequestBytes` and
  `QueueCapacity` bound the rest". The page documents no `Diagnostics()` line today; R2 adds the tap's, and a
  Limitations note on a debugger attached to another process.
- `Integration-TcpTap-Extension.md`, which documents the reaper since #174's 4.22.0 edit (`ReapStuckConnectionsAfter` in
  the options table, `StuckConnectionsReaped` among the counters; the page never mentioned it before). R0: a negative
  `BodyCapBytes` is refused. R3: both options and both counters (the table at L289-300); the idle-pair rule beside the
  reaper; L32's half-close promise, bounded now by `HalfClosedTimeout`; L37's "A tap is never the reason a request
  fails", false since the reaper, rewritten to say what closes a connection; the reaper's row, whose "the one
  intervention that ever touches forwarding" becomes one of three; and the sentence on the task `OnCaptureDegraded` runs
  on, which since 4.22.0 names the reaper's own for a reap, now a pool thread; and a Limitations note on a debugger
  attached to another process. The page's "stall" (L148, L300, L318) is the decoding heuristic, so the new text keeps
  the word for that (Q3). #174's edit already rewrote L285's "(never forwarding)" and L304's "called on the decoder
  task".
- `Diagnostics-and-Debugging.md`. R0: L110's "Forwarding was never affected", false since `ProxyTap`'s forward failures
  and the reaper used the kind, and since #174's failed connects and accepts too; #174's edits left the line as it was.
  R1 to R3: the tap pages carry each tap's `Diagnostics()` lines (this page lists none per tap), and this page points to
  them.

### 6.4 Plans, roadmap, Kronikol4J

- `PLANS_STATUS.md`: this plan's row, updated per release.
- `ROADMAP.md`: no row (Q12).
- Kronikol4J's divergence ledger (`docs/REMAINING_PARITY.md`): one line per release, "Nothing to port": the port has no
  proxy tap, no OTLP tap and no TCP tap (PROXY_TAP_HANG_UP_PLAN.md §2.4).

## 7. Order against the sibling plan (#174)

Agreed by message on 2026-10-14, and brought up to date on 2026-10-15, between this session (kronikol-51) and
kronikol-71 (#174, `TCPTAP_FAILED_CONNECT_PLAN.md`):

- **#174 owns** both taps' accept loops (`TcpTap.AcceptLoopAsync` L181-199, `OtlpTap.AcceptLoopAsync` L259-275), which
  it made one internal class, `TapAcceptLoop` (`src/Kronikol.Extensions.TcpTap/TapAcceptLoop.cs`, linked into `Otlp`);
  `TcpTap`'s upstream connect, its `ConnectTimeout` and the catches around it (its R2 removed the one at L247-250, which
  took a connect timeout for the tap stopping, and left one catch that logs whatever reaches it); the connect-failure
  counters and their `Diagnostics()` lines; F2 (a throwing `DecoderFactory`, its R2 item 7); `TcpTap` L253 and L495
  (`Degrade`'s line for an `OnCaptureDegraded` that threw, which its R2 reaches from the accept loop and puts through
  `TryLog`); and the start and stop log lines (`TcpTap` L162 and L715, `OtlpTap` L182, L210 and L611), which neither
  plan's list had: today a `Log` that throws at start makes `StartAsync` throw after the listener and its loop have
  started. It also took the false doc comments that were this plan's R0 item 8 (its R2 item 9), and it leaves a cap on
  connections to this plan (Q13).
- **#173 owns** everything after both sockets are open (pumps, half-close, the pair's release, the reaper), every time
  limit and its counters, F1, F3's lines other than L253 and L495, `TcpTap.Diagnostic` (L690), and every `Log` call in
  `ProxyTap`, in `OtlpTap`'s per-connection path and in its map loop (`MapPayload`, L526).
- **`TryLog`** is one helper per tap, named the same in both plans. #174 added it to `TcpTap` and `OtlpTap` in 4.20.3;
  R0 adds `ProxyTap`'s.
- #174's `Diagnostics()` lines came first (the failed accepts, then the failed connects), this plan's go after them, and
  neither plan takes a `ROADMAP.md` row.
- **State on 2026-10-15:** #174 is complete: its R1 shipped as 4.20.3, R2 as 4.22.0 and R3 as 4.22.1 (plan log
  `fefb99e5`). R2 moved `TcpTap`'s connect into `ConnectUpstreamAsync`, counts and reports a connect that fails, removed
  the catch at L247-250 and puts L253 and L495 through `TryLog`; R3 listens on both loopbacks for `localhost`
  (`_listeners`, a loop each) and connects IPv4 first. The pumps, the half-close, the pair's release and the reaper are
  as this plan read them. Every line number here is still at `3240c983`, where the code was read; Appendix A gives each
  edit site at 4.22.1 too, and each release re-reads them on the `main` it starts from.

## 8. Found on the way, not in this plan

- **`OtlpTap`'s forward has a fixed 30-second timeout with no option** (OtlpTap.cs:172). The upstream side; it would be
  a `ForwardTimeout` like `ProxyTap`'s.
- **`OtlpTap` drains a body over `MaxRequestBytes` in full before its 413** (HttpRequestReader.cs L96-100), on purpose:
  "Drain rather than buffer: the connection stays clean, so the 413 actually reaches the caller" (L98). A caller that
  declares up to `int.MaxValue` bytes, or sends a chunked body over the cap (drained with no total bound, L192-197),
  holds the handler for as long as it keeps sending; Kestrel answers 413 and closes. Bytes, not time, so not this
  plan's; with R0's secret check it is open only to a caller that has the secret.
- **No tap caps how many connections it holds.** With descriptors exhausted, `TcpTap`'s and `OtlpTap`'s accept loops end
  for good while `ProxyTap`'s listener keeps accepting (PEER:
  `TCPTAP_FAILED_CONNECT_PLAN.harness/accept/accept-linux.txt`); #174 fixed that in 4.20.3, and a cap at accept would go
  in its `TapAcceptLoop`. Connections held by a stalled peer are one way to get there, which this plan closes.
- **`ProxyTap` reads a request body whole into memory with no cap** (L279-299; `BodyCapBytes` caps only the captured
  copy). A proxy has to forward any size; noted, not proposed.
- **On Linux and macOS a slow reader can lose the tail of a large answer.** Once the tap closes a response, the managed
  listener arms its 15 s keep-alive timer even while the answer's tail is still in the socket's send buffer; a caller
  that takes longer than that to read it gets a reset and a truncated answer (dotnet/runtime#63236; SOURCE
  `source/dotnet-listener-timers.md` §1.5, not run here). It is the listener's, outside any limit of the tap's; a fact
  that reads a large answer slowly must finish inside it (R1-5).
- **`TcpTap` counts every reap twice** (as reaped and as closed mid-message, F6). Both are true; two lines for one
  event.
- **`SECURITY.md`'s supported-versions table lists only 2.0.x** (L5-8), stale at 4.20.2. A documentation fix with no
  bump, outside this plan's code.

## 9. Questions, with recommendations

- **Q1. On by default?** *Recommendation: yes, at 100 s,* in minors. CLAUDE.md defines a MAJOR change by its effect,
  "changing a default so that existing code behaves differently without being touched", and its rule that a new option
  is a minor covers a default that keeps today's behaviour; these defaults do not. A minor holds only by reading the cut
  as the fix of a defect (a fix that changes behaviour does not raise the bump, and the new options make the release a
  minor), and that reading is the owner's call. The case for: the behaviour today is a leak with no upside, Windows
  already resets a stalled body at two minutes, and a limit that is off by default fixes nothing for the people who
  never read the option. The case against: a run whose caller legitimately pauses for 100 s without a debugger attached
  to the test process (a service paused in a debugger of its own, §4.1 item 4) would see a 408 where it saw nothing. The
  alternative is the options off by default in 4.x, a minor beyond doubt, and on at v5 (`ROADMAP.md` D30), where a
  changed default belongs; it leaves the leak in place until then for everyone who does not read the changelog.
- **Q2. The values.** *Recommendation: 100 s for every stall limit, 120 s for `OtlpTap`'s keep-alive.* §4.1 item 3, §4.4
  item 1. `HalfClosedTimeout` is on for every `TcpTap`, generic ones too: a protocol that half-closes to end its request
  and then waits more than 100 s for the first byte of its reply would be cut. None of the clients Kronikol documents
  does (§4.5 item 1; the suite's only half-close is a synthetic fact, `TcpTapTests.cs` L191-220); the alternative is the
  default in `RedisTapOptions` and `MongoTapOptions` only, and `null` for a generic tap.
- **Q3. One option per tap, or one per phase** (nginx has `client_body_timeout` and `send_timeout`)? *Recommendation:
  one stall option per tap,* plus `OtlpTap`'s keep-alive, which means something else, and `TcpTap`'s half-close, a stall
  on the one direction still open, kept as an option of its own so it can be `null` where a protocol half-closes and
  then waits (Q2). The names are the release's to settle: `TcpTap`'s wiki page and `Diagnostics()` already say "stall"
  for its decoding heuristic (`DecodingStallBytes`), so `StalledWriteTimeout` may read better as `WriteTimeout`, the
  meaning `Stream.WriteTimeout` has.
- **Q4. Three minors or one?** *Recommendation: three,* each with its own package, wiki page and facts, in the order R1,
  R2, R3; R0 first.
- **Q5. Suspend the limits while a debugger is attached?** *Recommendation: yes,* as Kestrel does (§4.1 item 4). The
  existing reaper keeps reaping under a debugger, as it does today (§4.5 item 5): joining the new limits there would
  change it unasked.
- **Q6. Move `ProxyTap` off `HttpListener`?** *Recommendation: not in #173; a plan of its own if the owner wants the
  head bounded on Linux and macOS.* §4.7.
- **Q7. Report the head timer to dotnet/runtime?** *Recommendation: no.* #173 already says it publicly, the source is
  unambiguous, and `HttpListener` is in maintenance mode (#123463); the wiki's Limitations bullet is what users need.
- **Q8. A minimum data rate, or a total budget for a head, as well as the stall limit?** *Recommendation: no* (§4.7).
- **Q9. When may §4.6 item 3 be public?** It describes how a caller without `OtlpTap`'s secret can make the tap hold
  memory on a LAN bind. #173's own text already says the array is sized up front; the missing secret check is new.
  *Recommendation: commit this plan after R0 is published,* as #171's plan was committed after its R0. #171 opened no
  security advisory: its changelog entry said what was fixed and whom it affected ("upgrade any tap that listens where
  others can reach it"), and R0's would do the same.
- **Q10. Set http.sys's own timers from the option on Windows?** *Recommendation: no.* A shorter option already acts
  first. A longer one could lengthen the body's timer (a set `EntityBody` does govern a connection's first request, RUN
  `listener/` claim 6), and once R1 writes in pieces the answer's through `MinSendBytesPerSecond`, but it is a
  Windows-only setting on the URL group for a case, a caller stalled for over two minutes, that is not worth keeping on
  one platform only.
- **Q11. TCP keep-alive on `OtlpTap`'s and `TcpTap`'s sockets?** *Recommendation: no,* §4.7.
- **Q12. A `ROADMAP.md` row?** *Recommendation: none,* as #171, #172 and #174 took none (#174's Q7, green-lit as
  recommended). If the owner wants one, the next free stage-1 number is 1.31 (1.26 is held by PR #164, and #165's plan
  takes 1.30 with its first release), with an entry in `ROADMAP.md`'s §7 issue map and its Appendix B.1 register, which
  `CHANGED_SCENARIOS_PLAN.md` (#78, #124) and #165 are editing too; claimed with "taking row 1.31" before it is written.
- **Q13. A cap on how many connections a tap holds?** #174's plan first called it this plan's open question, which it
  was not (this plan had it only as found on the way, §8); its §4.5 now points to this question. *Recommendation: not
  now.* The limits here release what a stalled peer holds within two minutes, and #174's loop now outlives a run out of
  descriptors; a cap would refuse connections a suite opens on purpose, at a number nothing here measured. If a run is
  ever seen holding live connections by the hundred, the cap goes in `TapAcceptLoop`, as a plan of its own.

## 10. Assumption ledger

| Assumption | How it holds |
|---|---|
| macOS behaves as Linux | Not run (no macOS here); the managed `HttpListener` source is shared, and every action in §4 is chosen by the tap's own state, not by the exception a platform throws |
| No well-behaved peer stalls for 100 s | The fixed limits on a stalled body or answer in `source/prior-art.md` run from 30 s to 10 min and 100 s sits inside them; the debugger rule covers the one test-time pause that is long |
| `Debugger.IsAttached` reflects a debugger | It covers managed debuggers (Visual Studio, Rider, VS Code); a native-only debugger is not seen, which Kestrel accepts too. It sees only the tap's own process: a service in a process of its own, paused in a debugger there mid-transfer, is cut after the limit unless the option is `null` (§4.1 item 4) |
| The BCL behaviour R1 relies on (`Abort()` writes the status; a pending write completes without error) holds on later .NET versions | R1 decides by its own state after every read and write, never by the exception or its absence, so a change in either direction is still handled |

## 11. Log

- 2026-10-14: plan written (session kronikol-51) in worktree `C:/Code/Kronikol-173` (branch
  `plan/173-tap-caller-time-limits`), with the harness. Nothing implemented. The split with #174 agreed with kronikol-71
  (§7), including one guarded log helper, `TryLog`, for both plans.
  - Read: the three taps' caller paths (`read/`), .NET's listener source and ASP.NET Core's (`source/`).
  - Run: the three taps at 4.20.2 under stalled peers and `ProxyTap` with an infinite `ForwardTimeout` (`probe/`, both
    platforms), and a bare `HttpListener` for the six claims `source/dotnet-listener-timers.md` §8 had only inferred
    (`listener/`, both platforms).
  - What the runs changed. http.sys bounds a stalled body and not a reader that stops (W1 held on Windows too), so the
    answer's limit is needed on both platforms. Stopping the managed listener answers stalled callers `200 OK`, now R0
    item 2. An infinite `ForwardTimeout` cuts at 10 s and no value is checked, R0 item 1. A pending body read always
    threw after `Abort()` (20 of 20): the run never saw the clean end the source allows there, and the clean end it did
    see came from a caller's FIN (#135680), so the state check after the read stays, as a guard against that race. Body
    reads wait to fill their buffer on Windows and for chunked bodies, so the taps read and write in pieces of at most
    16 KiB (§4.1 item 1). A chunked answer's `Abort()` blocks on Linux, so R1 keeps answers Content-Length. A set
    `EntityBody` governs a connection's first request on Windows, as the source allowed (only `HeaderWait` and
    `IdleConnection` skip it) (Q10).
  - Reviewed against the harness the same day; 32 corrections, among them R1-5's answer size (2 MB fit the Linux socket
    buffers, so the fact could not fail), `CallerAnswer`'s state moved into R0 (the disposal fix needs it), the
    `Content-Length` cases sorted by RFC, three more unguarded `Log` sites, and the reaper's clock fact.
- 2026-10-15: swept for completeness and accuracy at the owner's ask, with deep dives (session kronikol-51); nothing
  implemented. The evidence is in the harness's `sweep/`: four sets of notes (the code's citations, the repository and
  the sibling plans, outside sources, an adversarial review of the design) and five runs.
  - Run: an answer written before the body and then a close (`early-answer-probe`, both platforms); a cancelled read
    followed by an answer (`cancel-then-answer-probe`, .NET 10 on both platforms and .NET 8 on Windows); http.sys's
    send-rate timer per piece (`send-rate-piece-probe`); where the BCL stops taking a duration (`timeout-limits-check`);
    and three facts about the code (`line-refs-probe`).
  - What the runs changed. An immediate close after an early 401 lost it to a reset in most large uploads, and a staged
    close delivered it in every trial, so R0's early answers close in stages (§4.6 item 3). A cancelled read left the
    socket fit for the 408 on every target run, so R2 keeps its mechanism and gains the staged close (§4.4 item 3). A
    stalled 16 KiB piece is reset by http.sys at 115 to 120 s and a 4 KiB one at about 40 s, so R1's last piece takes
    the remainder and the 100 s default still acts first on Windows (§4.3 items 4 and 8). `TimeSpan.MaxValue` and other
    large durations break `StartAsync` or every forward today (R0 item 1).
  - Other design changes. The half-close limit is a stall, not a total from the FIN: every limit it cited times silence,
    socat's included (§4.5 item 1). A transfer's end and its limit settle by one swap on the entry, not a check (§4.2
    item 3, R1-11). The heartbeat only decides and queues each action, runs with no test's context, and compares elapsed
    time (§4.2 items 2 and 3). R0's disposal settles each answer by its phase, where one `503` for all would have
    replaced the `502` of a forward cut short (§4.6 item 2). The reaper takes a command's wall-clock age once and then
    adds monotonic time, since its input is a wall-clock value from a public interface (§4.5 item 5). `OtlpTap` writes
    its answers in pieces and treats a stall inside a drained body as a body stall (§4.4).
  - Added: R0 items 9 to 12, negative caps refused in all three taps, both timeouts' whole range; facts R0-9 to R0-12,
    R1-11, R1-12, R2-8 to R2-10, R3-8 and the watch's own; Q13, the connection cap #174 left to this plan.
  - Corrected: `CopyToAsync` reads 128 KiB; `-5` parses as a length, and `+5` is read as a body; an export whose
    `Content-Length` fails the parse is answered `200` only without a forward; the `NoDelay` leak did not reproduce, so
    R0 item 5 is hygiene; `TcpTap` counts a write's progress whole (32 KiB per limit); the debugger rule cannot see
    another process; Q1 read CLAUDE.md's MAJOR rule by a changed option rather than by its effect; the fake-clock facts
    raced the tap; R0-2 and R0-3 measured process-wide allocations; HAProxy answers a stalled body `400`; the wiki's
    false statements are listed per page (§6.3); line ranges throughout.
  - From #174, green-lit and being implemented first: `TryLog` and the start, stop, L253 and L495 lines are its, as are
    R0 item 8's doc comments; its `TapAcceptLoop` is the pattern for the linked watch and its clock; Q12 now recommends
    no row, as #174's Q7 did.
  - Checked, no change: #173 and #174 have nothing new; dotnet/runtime #88196, #135680, #63236, #123463 and #39183 have
    no fix; .NET 10.0.13 ships the same `HttpListener` as 10.0.11 and 10.0.12, and `release/10.0` is still at `591ede8`;
    the 120 s keep-alive sits above every exporter's default idle limit that would not survive the race (§4.4 item 1).
  - Re-checked the same day against 4.22.1 (`fefb99e5`), once #174 had shipped as 4.20.3, 4.22.0 and 4.22.1. The design
    holds: the pumps, the half-close, the pair's release and the reaper are unchanged; the pumps already take a
    cancelled read or write for their end, and the one catch 4.22.0 left logs whatever reaches it, so R3's pair token
    stays inside the pumps (§4.5 item 4); a throw from `TapAcceptLoop`'s callback ends the loop, so R2 still registers
    inside `ServeAsync` (§4.4 item 2). `ProxyTap`, `HttpRequestReader` and the option files R0 edits other than
    `TcpTapOptions.cs` did not change. The text now says what #174 shipped, Appendix A gives each edit site in the files
    it changed at 4.22.1 too, §6.3 leaves out the wiki lines #174 corrected (L285, L304) and adds the reaper's new row,
    and `DiagnosticEntry.cs`'s `CaptureDegraded` summary gains a peer cut by a time limit in R1.
- 2026-10-15: green-lit in full by the owner ("implement the plan in full"), every question in §9 as recommended: Q1
  on by default, in minors; Q2 100 s and 120 s; Q3 one stall option per tap; Q4 R0, then R1, R2 and R3; Q5 no limit
  under a debugger, the reaper exempt; Q9 this plan committed once R0 is published; Q6 to Q8 and Q10 to Q13 not done.
  Implementation started the same evening (session kronikol-51), from `origin/main` `85f849f7` (4.22.1).
- 2026-10-15: R0 released as 4.22.2 (`9b7886d9`, worktree `C:/Code/Kronikol-173-r0`). Release 38811468774 and CI
  38811461417 passed, and nuget.org listed all 62 ids at 22:01 UTC; wiki `bb13d64`, Kronikol4J ledger `68a67b8`. This
  plan was committed after that, per Q9. The proofs are in the harness's `r0/` (its README): 60 of R0's 65 cases fail on
  4.22.1 with two seams stubbed on Windows, and 57 of the 61 run on Linux; of 44 mutations, 43 are killed, and the one
  left is equivalent (the README says why).
  - Found while implementing. http.sys resets the connections it keeps alive when the listener closes, so a caller lost
    an unread `502` written during the stop: an answer written while the tap stops says `Connection: close`. Closing the
    managed listener writes an empty `HTTP/1.1 200 OK` with `Connection: close` on each idle kept-alive connection,
    answering no request. `OtlpExportSink` and `OtlpExporter` called `Log` unguarded too (a throw from a refused batch's
    line ended the export worker), fixed in R0. The first mutation run left two mutants alive, and they showed that a
    request the stop reached before its forward began was still forwarded, with the cancelled token, and recorded as cut
    short: R0 refuses it with a `503` and records nothing (R0-12 strengthened, a fact of its own beside it).
  - Departures from §5. R0-8 is four facts, not a theory, and its bound on `DisposeAsync` is 1.5 s, not 2.5 s: with the
    2 s drain, the mutation that settles nothing before the close passed under 2.5 s. R0-5's theories throw only on
    their own case's line, so each case fails at its own site on the old release.
  - R1 started the same evening, in worktree `C:/Code/Kronikol-173-r1` from `9b7886d9`.
- 2026-10-16: R1 released as 4.23.0 (`c7eef4b3`, worktree `C:/Code/Kronikol-173-r1`, rebased onto 4.22.3 before its
  release). Release 38821776017, CI 38821772775, CodeQL 38821772767 and CI Summary Preview 38821772690 passed, and
  nuget.org listed all 62 ids at 23:31 UTC on 2026-10-15; wiki `01b1578`, Kronikol4J ledger `323c3a0`. The proofs are in
  the harness's `r1/` (its README): 27 of R1's 39 cases fail on 4.22.2 with R1's seams stubbed, the 12 that pass being
  guards, six answer lengths a single piece already serves, the race R1-7 (proven by mutation) and an older registration
  fact; of 20 mutations, 20 are killed, the one that writes no `408` on Linux only.
  - Found while implementing. `ProxyTap.DisposeAsync` threw `ArgumentException` ("The handle is invalid") on Windows for a
    tap disposed straight after it started, 4 to 61 of every 400 on 4.22.2: the accept loop's first wait began while the
    listener closed, a window R0 widened when the loop began to wait for the listener's close. Fixed in 4.23.0, with a
    fact of 1,000 disposals. The first mutation run left alive a body read that the limit raced, which `TryBeginForward`
    refused anyway, so R1-2 holds the limit's action through a seam on the watch (`BeforeAct`) until the read has ended.
  - Departures from §5. `-1` ms is `Timeout.InfiniteTimeSpan`, so the refused-values theory uses `-2` and `-1000` ms; the
    `400` path of R1-10 is a reset on Windows, where http.sys writes nothing on an abort.
  - R2 started the same night, in worktree `C:/Code/Kronikol-173-r2` from `c7eef4b3`.
- 2026-10-16: R2 released as 4.25.0 (`946e36e8`, worktree `C:/Code/Kronikol-173-r2`, rebased onto 4.24.0 before its
  release). Release 38827898674, CI 38827896211, CodeQL 38827896251 and CI Summary Preview 38827896247 passed, and
  nuget.org listed all 62 ids at 00:35 UTC; wiki `9ca835f`, Kronikol4J ledger `910a6bb`. The proofs are in the harness's
  `r2/` (its README): 38 of R2's 39 cases fail on 4.23.0 with R2's seams stubbed, the one that passes a guard, and of 23
  mutations, 23 are killed.
  - Found while implementing. Plan §5's W-1, the watch's own facts, were never written in R1; R3 adds them. The
    registration twin (R2-7) validates the options as registration does, so its value for `TracesPath` starts with a
    slash.
  - Departures from §5. R2-2 is a theory over three bodies, as R1-1 is; R2-10 is a theory, an exporter that stops reading
    and a steady reader; and R2 has the twins of R1-2 and R1-11 (a transfer completing as the limit fires, and one ending
    after it fired, each for the export and the answer) and of R1's option, debugger and no-entry facts.
  - R3 started the same night, in worktree `C:/Code/Kronikol-173-r3` from `769c55cd`, rebased onto 4.25.0.
- 2026-10-16: R3 released as 4.26.0 (`a3b20f99`, worktree `C:/Code/Kronikol-173-r3`). Release 38831708935, CodeQL
  38831706673 and CI Summary Preview 38831706682 passed, CI 38831706828 failing in two of R1's cases that 4.26.1 fixes
  (below), and nuget.org listed all 62 ids at 01:13 UTC; wiki `17d42b6`, Kronikol4J ledger `ca35e6c`. The proofs are in
  the harness's `r3/` (its README): 24 of R3's 26 cases fail on 4.25.0 with R3's seams stubbed, the two that pass
  guards, and of 20 mutations, 20 are killed.
  - Found while implementing. W-1, the watch's own facts, were never written in R1: `StallWatchTests` adds them (11
    facts in `Kronikol.Tests.ProxyTap`) with R3's additions to the watch, `WatchWhen` (an entry checked by a condition,
    the reaper's) and entries that act under a debugger. On Linux a half-closed pair whose remaining side read the tail
    of a reply from its socket buffers, sending nothing, was closed at the limit: the rule as §4.5 item 1 states it, and
    harmless, since the client got every byte; the steady-reader guard now keeps its upstream open, and the changelog
    says what counts as progress.
  - Departures from §5. R3-5's steady reader runs with the upstream's side open (above). R3 has twins of R1's option,
    debugger and no-entry facts, a fact that the reaper still acts under a debugger, and stop-line checks with non-zero
    counts.
- 2026-10-16: 4.26.1 (`be6384a9`), a patch to R1. 4.26.0's CI run failed two cases of R1-1 on a loaded runner at
  `Assert.Equal(1, tap.CallerTimeouts)`: `CutStalled` ended the caller's response, which lets the caller go, and only
  then counted the cut, so whoever read the counter as soon as the caller was gone could read 0. `CallerAnswer.TimeOut`
  now takes an action run once the cut has won and before the abort, and `CutStalled` counts in it;
  `A_cut_is_counted_before_the_caller_is_let_go` fails on the old order. The audit of 4.26.0 added
  `A_pair_a_limit_closes_raises_no_capture_degradation`, for a sentence of its changelog no fact checked (a mutant that
  raises the event fails it), and `DiagnosticEntry.CaptureDegraded`'s summary, which named only a caller. Release
  38834755455, CI 38834753160, CodeQL 38834753037 and CI Summary Preview 38834753158 passed, and nuget.org listed all 62
  ids at 01:48 UTC; Kronikol4J ledger `6f5ec06`; no wiki edit, R3's having named a pair cut by a time limit on the
  diagnostics page. #173 closed with a comment naming the five releases. The plan is complete.
- 2026-10-16: the plan's releases audited, and the fixes released as 4.26.3 (`46f21de0`, worktree
  `C:/Code/Kronikol-173-audit`, rebased onto 4.26.2, the audit of #174). At the owner's ask that nothing be missed from
  the plan and that its releases carry no bugs, five read-only reviews ran over `09369515`: the plan item by item, a bug
  hunt in each tap and the watch, and every changelog, wiki and doc-comment claim against the code; the session auditing
  #174 reported three more. Each finding was checked by reading the code or by a probe before anything was changed. The
  proofs are in the harness's `audit/` (its README), with the reviews as written.
  - Missed from the plan. §4.5 item 4 and §4.2 item 1: `TcpTap` registered a watch entry for every write, an allocation
    and two shared-dictionary operations per segment forwarded, where the plan has the pump set a timestamp before the
    write and clear it after; it now does, with one entry per pair. §4.2 item 3: the reaper read the clock under its
    lock. R0 item 8 gave the `TcpTap` stop line's reaped and closed-mid-message counts to R3, which left them out. §4.1
    item 4's advice for a peer paused in a debugger in another process was in no doc comment. §5's mutations for R1 left
    out two of the plan's (the body's entry armed at the first completed read, the watch moved into the forward) and
    proved R1-7 by another mutant; the audit runs all three, and all three are killed.
  - Faults in what the plan shipped. `TcpTap` (R3) and `OtlpTap` (R2) made the heartbeat before binding, so a start that
    threw left it running; 4.26.2's reworked start, agreed between the two audits, makes it after the bind, and 4.26.3
    adds the facts. The reaper closed a pair outside the pair's one close; a half-closed pair whose other side had
    stopped reading was counted by whichever limit came first in the beat, about half the time as the half-close; a
    condition check that blocked held up the whole heartbeat; `OtlpTap` received an export that arrived whole as
    `KeepAliveTimeout` fired, and counted a refusal whose head the stall limit had already cut; an empty line before a
    request line started the stall phase. In the tests, R1-1 and R1-3 asserted a log line the cut writes after letting
    the caller go without waiting for it, R1-1's Windows branch asserted only that the answer was not a success, and
    4.26.1's fact for the count order called `TimeOut` and not `CutStalled`.
  - Older faults on the same paths, fixed with them: `ProxyTap` could be started twice and after its dispose, linked the
    start token into its lifetime, stopped taking requests after one failed wait with nothing said, missed a request
    taken but not yet handled when it stopped (on Linux the caller heard `200 OK`), answered a `HEAD` with
    `Content-Length: 0` and named no failure count at stop; `OtlpTap`'s reader read a `Transfer-Encoding` other than
    `chunked` as no body and joined a repeated header in time that grew with the square of its count.
  - Did not hold: `StallWatch`'s `ExecutionContext.SuppressFlow()` throwing when the flow is already suppressed (two
    reviews): the throw is .NET Framework's; on .NET 8, 9 and 10 a nested suppression is allowed
    (`audit/probe-suppress/`).
  - Docs. 4.26.0's changelog, 4.26.1's, the TcpTap page and `CaptureDegradation.StuckConnectionReaped`'s doc said a pair
    a limit closes has lost none of its capture; one closed mid-message has, and the Redis and MongoDB decoders report
    it as `ConnectionClosedMidMessage`, which a fact now checks. The other corrections are in 4.26.3's changelog.
  - Left to #174's audit, which shipped them in 4.26.2: `ConnectTimeout` never validated, an `OtlpTap` on `::` whose
    `TracesEndpoint` threw after binding, the TcpTap page's sentence on when a tap closes a connection and
    `TcpTap.Diagnostics()`'s summary.
  - Found while releasing. The core suite's `CultureInvariantSourceTests` failed on the patch's own new log line, a
    count of failed waits in `ProxyTap`'s accept loop formatted with the machine's culture; it names the invariant
    culture before the release.
  - Proofs. With the patch's tests copied onto 4.26.1 and `ProxyTap`'s seams stubbed, every fix's fact fails, each on
    its own assertion, on Windows and, for the request taken as the tap stops, on Linux; the facts that pass there are
    guards. Of 33 mutations, 31 are killed, one on Linux alone; the survivors remove only the close's disposes, which
    its cancel makes a second path (removing the cancel too is killed), and undo the stopping refusal's order, which no
    fact can force. Release 38921191836, CI 38921187779, CodeQL 38921187278 and CI Summary Preview 38921187484 passed,
    and nuget.org listed all 62 ids at 15:22 UTC.
  - The wiki has the audit's edits (`eb04e6a`) and Kronikol4J's ledger its line (`e71b622`).

## Appendix A. Edit sites at `3240c983`

The releases of #174 moved the lines in `TcpTap.cs`, `OtlpTap.cs`, `TcpTapOptions.cs`, `CaptureDegradation.cs` and
`DiagnosticEntry.cs`, so those rows give them at 4.22.1 (`fefb99e5`) too (§7). Each release re-reads its sites on the
`main` it starts from.

| File | Lines | R | Change |
|---|---|---|---|
| `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs` | 172-192, 397-399 | R0 | a `ForwardTimeout` that is infinite, or of `int.MaxValue` ms or more, gives `HttpClient` an infinite timeout and the forward no `CancelAfter`; `ConnectTimeout` likewise |
| same | 52, 216-265, 963-1005 | R0 | the in-flight answers tracked; `DisposeAsync` settles each by its phase, the drain before `_listener.Close()`, the accept loop answering `503` until the close |
| same | 397, 1003 | R0 | the lifetime token taken once, while the tap runs |
| same | 82-89 | R1 | `ForwardFailures`' doc: a body cut by the limit is not a forward failure either |
| same | 248, 855 | R0 | the catch-all and `RespondAsync`'s catch read the answer's state (ended from R0, timed out from R1) |
| same | 168, 212, 247, 459, 468, 608, 1004 | R0 | `TryLog` |
| same | 234-265, 279-299 | R1 | the body read as a loop of reads of at most 16 KiB, its phase, and its entry ended by a swap after the copy |
| same | 825-859, 887-902; 255, 472 | R1 | the answer in pieces of 16 KiB, the last taking the remainder, still Content-Length, its phase, the entry's state read after each piece and its swap after the last; the 502 written with an awaited `WriteAsync`, and its phase, and both its call sites awaiting it |
| same | 904-956 | R0 | `CallerAnswer`'s single state field, which the disposal fix needs |
| same | 904-956 | R1 | `TimeOut()` |
| same | 108-137, new beside the counters | R1 | `CallerTimeouts` and its `Diagnostics()` line |
| same | 963-1005 | R1 | the heartbeat stops first |
| `src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs` | `Validate` 186-199 | R0 | `ForwardTimeout` and `ConnectTimeout` zero or negative (other than infinite) refused; a negative `BodyCapBytes` refused |
| same | new, beside 138-142; `Validate` 186-199 | R1 | `CallerStallTimeout` and its check |
| `src/Kronikol.Extensions.ProxyTap/ProxyTapServiceCollectionExtensions.cs` | 42-77 | R1 | `Copy` |
| `src/Kronikol.Extensions.TcpTap/StallWatch.cs` | new | R1 | the watch, beside #174's `TapAcceptLoop.cs`; `TcpTap` compiles it from R1, unused until R3 |
| `src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj`, `src/Kronikol.Extensions.Otlp/Kronikol.Extensions.Otlp.csproj` | new | R1, R2 | `KRONIKOL_PROXYTAP_SHARED` in `ProxyTap` (`Otlp` has #174's `KRONIKOL_OTLP_SHARED`), and a `<Compile Include=... Link="Shared\StallWatch.cs" />` item in each |
| `tests/Kronikol.Tests.{ProxyTap,Otlp,TcpTap}/*.csproj` | new | R1, R2, R3 | a `Microsoft.Extensions.TimeProvider.Testing` reference |
| `src/Kronikol.Extensions.Otlp/OtlpTap.cs` | 281-282 (4.22.1: 280-281) | R0 | `NoDelay` inside the `using` |
| same | 304, 313, 321, 341, 409, 448; 526 (`MapPayload`) (4.22.1: 303, 312, 320, 340, 408, 447; 525) | R0 | `TryLog` (#174 added the helper in 4.20.3 with its own lines, 182, 210 and 611) |
| same | 286-295, 308-367 (the check itself L318-324) (4.22.1: 285-294, 307-366; 317-323) | R0 | the secret checked after the head, before the body; a branch for a refused `Content-Length` |
| same | 277-306 (4.22.1: 276-305) | R2 | the connection's token, the catch that tells a limit from a hang-up and from stopping, the 408 |
| same | 459-475 (4.22.1: 458-474) | R2 | the answer's body in pieces of at most 16 KiB |
| same | 611-613 (4.22.1: 610-613) | R0 | the stop line names `PayloadsRejected` and `PayloadsFailed` |
| same | 611-613 (4.22.1: 610-613) | R2 | the stop line names `CallerTimeouts` |
| same | 573-614 (4.22.1: 572-614) | R2 | `DisposeAsync`: the heartbeat stops first |
| `src/Kronikol.Extensions.Otlp/HttpRequestReader.cs` | 44-109 (head 46-80, chunked body 84-91, Content-Length body 92-106), 154 | R0 | the head returned before the body is read, so the tap can refuse it first; `400` or `413` for a `Content-Length` that is not digits in range; the body grown as it arrives |
| same | 124-150, 285-290 | R2 | the head's first byte marked; progress recorded per fill |
| same | 84-91, 96-100, 255-266 | R0 | a request with both framings closes its connection; a drain cut short ends the request |
| `src/Kronikol.Extensions.Otlp/OtlpTapOptions.cs` | `Validate` 140-155 | R0 | a negative `ContentCapBytes` refused |
| `src/Kronikol.Extensions.Otlp/OtlpTapOptions.cs`, `OtlpTapServiceCollectionExtensions.cs` | new, `Validate` 140-155; 42-67 | R2 | both options and their checks; `Copy` |
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 257, 365, 383, 447, 466, 478, 517, 672, 690 (4.22.1: 296, 508, 526, 591, 610, 622, 661, 829, 847) | R0 | `TryLog` (#174 added the helper and its own lines: 162 and 715 in 4.20.3, 253 and 495 in 4.22.0) |
| same | 271-321, 731 (4.22.1: 414-464, 908) | R3 | write-pending and pump-ended marks; `LastActivityTicks` |
| same | 229-261, 323-371 (4.22.1: 267-300, 466-514) | R3 | the pair's token and release; the reaper on the watch, with its first-seen times |
| same | 529-580, 715-718 (4.22.1: 674-736, 891-895) | R3 | two counters, their lines, the stop line |
| same | 693-720 (4.22.1: 866-897) | R3 | `DisposeAsync`: the heartbeat stops first |
| `src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs` | 96, 188-214 (4.22.1: 113, 214-240) | R0 | a negative `BodyCapBytes` refused |
| same | new, beside 173; `Validate` 188-214; `CopyTo` 217-252 (4.22.1: beside 199; 214-240; 243-278) | R3 | both options, their checks and their copy (the Redis and Mongo list registrations copy through `CopyTo`) |
| `src/Kronikol.Extensions.TcpTap/CaptureDegradation.cs`, `TcpTapOptions.cs` | 3; 136-142 (4.22.1: 3-7; 160-168) | #174 | doc comments (formerly R0 item 8), rewritten in 4.22.0 |
| same | 22-24; 157-158; 136-142 (4.22.1: 26-28; 183-184; 160-168) | R3 | "the one intervention that ever touches forwarding" (now three); `OnCaptureDegraded`'s thread for a reap, the reaper's own since 4.22.0, now a pool thread |
| `src/Kronikol/Reports/DiagnosticEntry.cs` | 41-45 (4.22.1: 41-46) | #174, R1 | `CaptureDegraded`'s "forwarding was never affected", rewritten in 4.22.0 to name a forward, a connect or an accept that failed; R1 adds a caller or peer cut by a time limit, which R2 and R3 report under the same kind |
| `tests/Kronikol.Tests.{ProxyTap,Otlp,TcpTap}/` | new classes | R0-R3 | §5 |
