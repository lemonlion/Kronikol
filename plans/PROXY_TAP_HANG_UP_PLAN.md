# A caller that hangs up on a tap (#172)

**Date:** 2026-10-13 · **Repo version:** 4.18.1 (`origin/main` at `34145c44`, the commit the issue's line numbers are for;
every line number below is for it) · **Status:** 🟡 green-lit in full by the owner on 2026-10-13 ("implement the plan in
full", every §9 question as recommended); **R1 released as 4.19.2**, R2 next (§11). Drafted from #172
(filed 2026-10-13) at the owner's ask of 2026-10-13 to analyse the issue critically and plan a fix for the underlying
problem. Written alongside the plans for #171 (`PROXY_TAP_FAILED_FORWARD_PLAN.md`, session kronikol-70) and #170
(session kronikol-ef), which change the same method; §7 is the order (#170's releases and #171's first went before R1).
Checked again the same day against the source, the wiki, Kronikol4J and six more probes (§11); the cited files are
unchanged through 4.18.2 (`9cf89794`).

Evidence labels: **RUN** (measured on 2026-10-13 with the published 4.18.1 packages from nuget.org and with the prototype
in [`PROXY_TAP_HANG_UP_PLAN.prototype.patch`](PROXY_TAP_HANG_UP_PLAN.prototype.patch): Windows 11 26200 with .NET 10.0.11,
where `HttpListener` is http.sys, and Ubuntu 24.04.5 in `mcr.microsoft.com/dotnet/sdk:10.0` with .NET 10.0.12, where it is
the managed implementation; scripts and output in [`PROXY_TAP_HANG_UP_PLAN.harness/`](PROXY_TAP_HANG_UP_PLAN.harness/README.md)),
**READ** (Kronikol's source at `34145c44`), **SOURCE** (.NET's `System.Net.HttpListener` on `release/10.0` at `591ede81`;
`M/` and `W/` are `src/libraries/System.Net.HttpListener/src/System/Net/Managed/` and `.../Windows/`), **ISSUE** (the
issue's own macOS measurement, not re-run: there is no macOS machine here).

## 0. Summary

- **The issue is right about the leak and its cause.** On Linux, 10 callers that hang up leave 10 connections held by the
  tap on each of four paths (the issue names three), and only `DisposeAsync` lets them go (RUN). The managed `HttpListener`
  stops watching a connection once it has handed the request out, and keeps it until the response is closed or aborted
  (SOURCE); `ProxyTap` does neither on any path that gives up on a response (READ).
- **Windows does not leak.** http.sys drops a request whose connection died by itself: no connection survives and its
  queue shows 0 active requests on every path (RUN). The defect belongs to the managed listener, so to Linux and macOS.
- **The miscount is real on Linux too, and which exception arrives depends on how the caller goes.** A caller whose
  socket is reset mid-body (an RST alone) makes the body read throw `IOException` on Linux (5 of 5), and through 4.18.1
  that counts 10 of 10 in `ForwardFailures` with ten `proxy-tap error` lines (RUN). A close that sends a FIN before its
  RST, as `TcpClient.Dispose()` with a zero linger does (the issue's repro and this plan's first probes), reads as an end
  of stream on Linux: `HttpListenerException` 400, not counted (5 of 5, RUN). macOS mostly reports that same close as
  an `IOException` (ISSUE). Windows throws `HttpListenerException` 995 either way (RUN). Only the `IOException` lands
  in the catch that counts `ForwardFailures`.
- **A chunked request cut short by a FIN is forwarded as if whole on Linux, and no catch can see it.** The managed
  listener's read ends without an error, so the tap forwards the bytes that arrived with a `Content-Length` of their
  own, records the exchange and answers the caller `200` (RUN). On Windows http.sys answers `400` itself and nothing is
  forwarded (RUN). It is a .NET defect with no issue filed; R1 does not change it (Q6).
- **The underlying problem is two design gaps, not three missing calls.** No one owns the caller's response: each of the
  exits from `GuardedHandleAsync` decides alone whether to close it, and four of them do not. And failures are sorted by
  exception type in one catch around the whole exchange, when the type for the same event differs by platform and by
  how the caller closed;
  only the step that failed (reading the request, forwarding, writing the answer) says whose failure it was.
- **Three corrections to the suggested fix.** (1) The managed `Abort()` is not an abort when nothing has been sent: it
  writes a whole empty response first, `200 OK` by default, so adding it to the hang-up catch would tell a caller still
  listening that its request succeeded (RUN; dotnet/runtime#88196). (2) On Windows, `Abort()` after `HttpListener.Close()`
  throws `ObjectDisposedException` (RUN); the issue checked macOS only. (3) "Treat an `IOException` as a hang-up" must
  be decided by the step, not the type: the type depends on the platform and on how the caller closed.
- **R1 (patch, the ask):** `GuardedHandleAsync` owns the response through a small `CallerAnswer`; the body read gets its
  own catch that ends the exchange as the caller's; one `finally` ends every response the tap did not close, with a
  failure status set first and every throw guarded. The prototype releases every connection on all four paths on Linux,
  answers a caller that half-closes mid-body `400` (what http.sys answers on Windows), still counts real upstream
  failures and leaves keep-alive alone (RUN).
- **R2 (patch, found on the way):** `OtlpTap` does not leak (it owns its sockets) but misreads hang-ups the same way.
  With forwarding on, an exporter that hangs up before its answer is counted as a forward failure while the collector
  did receive the export (Linux, RUN); and a chunked export cut short, or with a size line that is not hex, is answered
  `200 OK` and counted as received (both platforms, RUN). The same reader puts a keep-alive connection out of step when
  an export carries trailers, and a chunk size past `int` ends in an `OverflowException` (READ); R2 fixes both.
- **Questions for the owner (§9):** Q1 whether a request cut short should be drawn, Q2 caller-side time limits for both
  taps, Q3 whether R2 rides with R1, Q4 a macOS lane for the tap tests, Q5 to Q7 smaller ones.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | The managed `HttpListener` keeps a connection until its response is closed or aborted | **Confirmed** | SOURCE: the read timer starts in `BeginReadRequest` (M/HttpConnection.cs:195) and stops after the first read (:257); a dispatched context stays in `_listenerContexts` (M/HttpListener.Managed.cs:192) and its connection in `_connections` (HttpConnection.cs:312) until `Close`/`Abort` or listener close; .NET's own issue for a client's FIN going unnoticed is dotnet/runtime#39183 (open since 2020). RUN: path A–D below |
| 2 | `RespondAsync`'s write throws once the caller has reset; its catch (L555-558) does nothing else; the `Close()` at L553 is never reached | **Confirmed** | READ; RUN path A, Linux: 10 of 10 held |
| 3 | 10 callers that read one byte of an 8 MB answer and reset leave 10 connections open | **Confirmed on Linux** (macOS per the issue) | RUN `probe/probe-4.18.1-linux.txt` |
| 4 | Reading a reset caller's body (`CopyToAsync`, L184) mostly throws `IOException` | **Line corrected; the type depends on how the caller closes.** The call is L185 (L184 is the `MemoryStream`). An RST alone: `IOException` on Linux (5/5). A FIN before the RST, as the issue's `TcpClient.Dispose()` sends: `HttpListenerException` 400 on Linux (5/5), mostly `IOException` on macOS (ISSUE). Windows: `HttpListenerException` 995 both ways | RUN `reset/reset-4.18.1-{linux,windows}.txt`, `socket/socket-{linux,windows}.txt`, and `listener/listener-{linux,windows}.txt` section 1 (a FIN-then-RST close); SOURCE: on the async path `CopyToAsync` takes, an RST passes the socket's `IOException` through (M/HttpRequestStream.Managed.cs:194-198), and an end of stream with bytes still owed throws `HttpListenerException(400)` (:203-205); a **chunked** body turns every read error into `HttpListenerException(400)` on every platform (M/ChunkedInputStream.cs:145-149, :170-171), so macOS's `IOException` is a Content-Length body's |
| 5 | The hang-up catch at L156 takes only `HttpListenerException` or `ObjectDisposedException` | **Confirmed, at L157** | READ |
| 6 | So it lands in the forward-failure catch: `ForwardFailures` goes up, `proxy-tap error` is logged, `TryRespondBadGateway`'s write fails into a catch (L573-576) that does not abort | **Confirmed, on Linux too** | RUN `reset/`: an RST alone, 10 of 10 counted with ten `proxy-tap error` lines on Linux and the upstream never dialled; a FIN first goes to the hang-up catch (path B: `ForwardFailures` 0); Windows 0 either way |
| 7 | The odd read that fails with `HttpListenerException` goes to the hang-up catch, which does not abort | **Confirmed**: on Linux it is every read after a FIN, on Windows every read | RUN path B, C |
| 8 | `Diagnostics()` then reports a failing upstream that was fine | **Confirmed** (L90-93), on macOS and Linux | READ; RUN `reset/` produces the count it reports |
| 9 | `Abort()` does not throw on a response already closed or aborted, or once the listener has closed | **True on the managed listener, false on Windows** after `HttpListener.Close()`: `ObjectDisposedException` (`HttpRequestQueueV2Handle`) | RUN `listener/*.txt` sections 4, 5; SOURCE W/HttpListenerSession.Windows.cs:69 disposes the queue handle the P/Invoke takes |
| 10 | On a bare `HttpListener`, 20 of 20 such connections stayed open without `Abort()` and none with it | Consistent with RUN; not re-run on a bare listener | |
| 11 | Treat an `IOException` from reading the caller's request as the caller hanging up | **Right intent, wrong key**: decide by the step (§3.2, §3.3) | |
| 12 | A client that resets before the accept crashes the process on macOS (.NET's accept callback) | Not reproducible here (Linux, Windows); a .NET defect, out of scope. The facts in §5 reset only once the tap holds the request | |

## 2. Where it stands today

### 2.1 The code (READ)

Every exchange runs in `GuardedHandleAsync` (L150-171), fire-and-forget from the accept loop (L146). Its exits, and what
each does with the caller's response:

| Exit | Where | Closes | Aborts | Counts |
|---|---|---|---|---|
| Answer written | `RespondAsync` L552-553 | ✅ `Close()` | | `RequestsHandled` (L308), `RequestsCaptured` (L317) |
| Caller gone while the answer is written | `RespondAsync` catch L555-558 | ❌ | ❌ | handled and recorded ("the exchange is still recorded") |
| Body read fails with `HttpListenerException`/`ObjectDisposedException` (Linux after a FIN, any chunked body's error on the managed listener, Windows always) | hang-up catch L157-160 | ❌ | ❌ | nothing |
| Body read fails with anything else (`IOException`: an RST alone on Linux, most closes on macOS) | catch-all L161-166 | ❌ (the 502 write fails) | ❌ | `ForwardFailures` + `proxy-tap error` line |
| Forward fails, caller still there | catch-all → `TryRespondBadGateway` L561-577 | ✅ L571 | | `ForwardFailures` (correct) |
| Forward fails, caller gone | catch-all → `TryRespondBadGateway`'s catch L573-576 | ❌ | ❌ | `ForwardFailures` (correct) |
| Tap disposed | `DisposeAsync` L601 `_listener.Close()` | the listener force-closes everything (SOURCE M/HttpListener.Managed.cs:217-243) | | |

The forward's deadline is linked to the tap's lifetime (L257-259), so `ForwardTimeout` bounds only the upstream. Nothing
bounds the body read (L185) or the answer write (L552).

### 2.2 The measurements (RUN)

`probe/probe.cs`, 10 callers per path, 3 s after the hang-up. Its resets are `TcpClient.Dispose()` with a zero linger,
which shuts the socket down (a FIN) before closing it (an RST); a reader on Linux sees the FIN as an end of stream and
never the RST (RUN `socket/`). Row B′ is an RST alone. "Held" is the managed listener's own tables
(`_listenerContexts`, `_connections`, read by reflection) and, on Linux, socket descriptors the process still holds that
no TCP table lists (a reset socket leaves `/proc/net/tcp` but not the descriptor table). On Windows, http.sys's queue
(`netsh http show servicestate`).

| Path | Linux 4.18.1 | Linux prototype | Windows 4.18.1 and prototype | macOS 4.18.1 (ISSUE) |
|---|---|---|---|---|
| A. reset while the tap writes an 8 MB answer | **10 held**; recorded, `ForwardFailures` 0 | 0 held; recorded | 0 held, 0 active requests | 10 open |
| B. reset mid-body: `TcpClient.Dispose()` with a zero linger, a FIN then an RST (as in the issue's repro) | **10 held**; `ForwardFailures` 0 | 0 held | 0 held; `ForwardFailures` 0 | 10 open; `ForwardFailures` 9 or 10 |
| B′. an RST alone mid-body (`reset/reset-probe.cs`) | `ForwardFailures` **10**, ten `proxy-tap error` lines, the upstream never dialled (held not measured: the exit is the catch-all's, as on macOS) | not measured (the body read's catch takes every type) | `ForwardFailures` 0 | |
| C. half-close (FIN) mid-body, caller still reading | **10 held** in CLOSE_WAIT; the caller hears nothing for 5 s | 0 held; the caller gets `400 Bad Request` | 0 held; http.sys answers `400 Bad Request` itself | not measured |
| D. upstream fails after the caller reset | **10 held**; `ForwardFailures` 10 (correct) | 0 held; `ForwardFailures` 10 | 0 held; `ForwardFailures` 10 | not measured |
| E. control: 20 keep-alive requests | 20 answered on 1 connection | same | same | |

Every held connection on Linux was released by `DisposeAsync` (all tables 0 afterwards).

### 2.3 What the listener does (SOURCE, RUN)

`listener/listener-probe.cs` on a bare `HttpListener`, the same file on both platforms, with `reset/`, `chunked/` and
`abort/` where a row names them:

| | Managed (Linux, macOS) | http.sys (Windows) |
|---|---|---|
| A dispatched connection is watched | No: no timer, no pending read (SOURCE, item 1 of §1) | http.sys drops the request when the connection dies (RUN: 0 active requests) |
| Body read, caller resets with an RST alone | `IOException`, "Connection reset by peer" (Linux, 5/5, `reset/`) | `HttpListenerException` 995 (5/5) |
| Body read, caller resets with a FIN first (`TcpClient.Dispose()`, zero linger) | `HttpListenerException` 400 (Linux, 5/5); `IOException` mostly (macOS, ISSUE) | `HttpListenerException` 995 (5/5) |
| Body read, caller half-closes, Content-Length body | `HttpListenerException` 400; nothing is sent; the connection waits | `HttpListenerException` 995; http.sys has sent `400 Bad Request` |
| Body read, caller half-closes, chunked body | **The read ends with no error and the bytes that arrived**, a whole chunk or half of one (Linux, `chunked/`) | `HttpListenerException` 995 with nothing read; http.sys has sent `400 Bad Request` |
| Answer write, caller reset | `HttpListenerException` ("Broken pipe", COR_E_IO) | `HttpListenerException` 995 |
| `Abort()` with no head sent, caller listening | **Writes a whole empty response with the status set, `200 OK` by default (`Transfer-Encoding: chunked` and its last chunk), then FIN** (RUN `listener/` with a half-closed caller, `abort/` with one still sending; SOURCE `HttpConnection.Close` always closes the response stream, M/HttpConnection.cs:508, which sends an unsent head, M/HttpResponseStream.Managed.cs:56-90; dotnet/runtime#88196, open) | Nothing is sent; a caller still sending gets a reset (RUN `abort/`) |
| `StatusCode = 400`, then `Abort()` | `400 Bad Request` with `Connection: close`, empty, then FIN | The same reset; the status changes nothing |
| `Abort()` after `Close()`, twice, or on a keep-alive response already closed | No effect, next request unharmed | No effect, next request unharmed |
| `Abort()` after `HttpListener.Close()` | No effect | **`ObjectDisposedException`** |
| `Close()` once the connection is gone | No exception (it closes the socket itself) | **`HttpListenerException` 1229** |

More facts from the source shape the design:

- On the managed listener `Close()` marks the response disposed before it does anything, so an `Abort()` after a
  `Close()` that failed does nothing (M/HttpListenerResponse.Managed.cs:98-110). On Windows `Close()` closes the stream
  before it marks the response closed (W/HttpListenerResponse.Windows.cs:160-161), so a `Close()` that threw 1229 leaves
  the response to a later `Abort()`.
- Setting `StatusCode` once the head has gone does not throw on either platform: the setter throws only on a closed
  response (`ObjectDisposedException`) or a value outside 100-999 (M/HttpListenerResponse.Managed.cs:74-86,
  W/HttpListenerResponse.Windows.cs:50-62). The late value is simply never sent.
- The managed `Abort()` cannot throw on a reset socket: the unsent head's write turns the socket's `IOException` into an
  `HttpListenerException` (M/HttpResponseStream.Managed.cs:134-155), which the close swallows (:56-90).
- A cancellation token never interrupts body I/O already running, only `Abort()` does: neither listener's streams
  override `ReadAsync`/`WriteAsync`, and the base `Stream` checks the token once and then runs the old Begin/End pair
  without it (CoreLib Stream.cs:303-306, 450-464; writes :723-728, 754-771). This matters for Q2.

### 2.4 The same defect elsewhere (READ, RUN)

- **`OtlpTap`** (plain sockets, not `HttpListener`) keeps each connection in `using (client)` (OtlpTap.cs:282), so every
  exit disposes it: no leak. It misreads hang-ups in two places, both measured with `otlp/otlp-probe.cs`:
  - **O1.** `ForwardAsync` writes the collector's answer to the exporter inside the forward's `try` (L411), whose catch
    takes `IOException` and `SocketException` (L414) and counts a forward failure (L416-417). An exporter that hangs up
    before its answer is therefore a "forward failure" although the collector received the export: Linux
    `ForwardFailures` 1 and `otlp-tap forward failed: Unable to write data to the transport connection: Broken pipe.`
    (Windows: the write did not fail, 0). `Diagnostics()` then says the export "could not be forwarded … and were
    answered 502 Bad Gateway … the real collector never saw them" (L148-151), false on both counts. The same catch takes
    `TaskCanceledException`, so a forward cut short by the tap's own disposal is counted too: `ForwardFailures` 1 and
    `otlp-tap forward failed: The operation was canceled.` on both platforms (RUN `otlp/otlp-dispose-probe.cs`). The 502
    it then writes uses the cancelled token (L418), so the exporter reads nothing. `DisposeAsync` does not wait for the
    handler, so its own `otlp-tap stopped (…)` line can report the count before it moves (0 on Windows, 3 runs of 3).
  - **O2.** `HttpRequestReader.ReadChunkedAsync` (HttpRequestReader.cs:165-200) returns the bytes read so far as a
    complete body when the stream ends before the last chunk (L172-173) or a size line is not hex (L176-177), while a
    Content-Length body cut short returns `null` (L101, `// truncated`). A chunked export whose exporter half-closes
    mid-body, or one with a broken size line, is answered `HTTP/1.1 200 OK` and counted in `RequestsReceived` with no
    failure anywhere (both platforms, RUN). A size line over 1,024 bytes takes the same path (`ReadLineAsync` returns
    `null`, L226-227). Chunk extensions are fine (`1a;name=value` is cut at the `;`, L174-175).
  - **O3, the same method (READ).** After the zero-size chunk the reader takes exactly one more line (L180) as the end of
    the trailers. An export with one trailer field leaves the blank line behind, so the next request on that keep-alive
    connection reads an empty request line and is closed unanswered (L51-52); with two or more, a trailer line is read as
    a request of its own and answered `404` or `401`, one answer ahead of the exporter. And a size of `80000000` or more
    parses as a negative `int`, so `new byte[count]` (L150) throws `OverflowException` into the catch-all (L302-305): an
    `otlp-tap connection error` line, nothing counted.
- **`TcpTap`**: a failed read or write half-closes the other side and both sockets are disposed in a `finally`
  (TcpTap.cs:241-261, 283-318): no leak. The `finally` runs once both pumps end, so an upstream that neither closes
  after a half-close nor sends anything holds the pair until disposal (READ; Q2).
- **No other server** in `src/`: a search for `HttpListener`, `TcpListener`, socket accept and listen calls, Kestrel and
  named pipes finds only the three taps.
- **Kronikol4J** has no tap at all (its `TrackingProxy` is a `java.lang.reflect.Proxy`, not a network tap). Its ledger
  (`docs/REMAINING_PARITY.md` at `4ff5c07`) says so for the proxy tap (L1922, "Java has no ProxyTap equivalent") and the
  OTLP tap (L2439, "the port has no OTLP tap"), and never mentions `TcpTap`.
- **Test stubs** never abort either: `StubUpstream` (ProxyTapTests.cs:17-85; its per-request catch at L62 takes only
  `HttpListenerException`/`ObjectDisposedException`, so on macOS an `IOException` would end its accept loop),
  `StubCollector` (OtlpTapTests.cs:47-118), `SequencedCollector` (OtlpExporterTests.cs:9-69), `EchoServer`
  (PlaywrightAdapterTests.cs:121-163, no per-request catch at all) and a one-shot listener in
  `tests/Kronikol.Tests/Tool/ExportCommandTests.cs:136-149`. Test-only; §4.4.

## 3. Findings the issue does not state

1. **Windows does not leak** (§2.2). CI runs the tap tests on Ubuntu only (`ci.yml`: ProxyTap in "Extension Tests",
   L81-86; Otlp in "Remaining Unit Tests (auto-discovered)", L287-302; the Windows and macOS lanes run template facts
   only, L139-146), which is where the facts in §5 go red; this Windows box cannot show them red, so the red step runs
   in the Linux container (`PROXY_TAP_HANG_UP_PLAN.harness/README.md`).
2. **The exception type depends on the platform and on how the caller closed** (§2.3). An RST alone is an `IOException`
   on Linux. A FIN first is an `HttpListenerException` 400 on Linux and mostly an `IOException` on macOS. Windows throws
   995 either way, and a chunked body's error is an `HttpListenerException` 400 on the managed listener whatever caused
   it. A rule keyed on type is right only by luck.
3. **A type rule in `GuardedHandleAsync` would also misfile upstream failures.** `HttpIOException` derives from
   `IOException`. Today `SendAsync` with `ResponseContentRead` wraps an upstream body cut short in
   `HttpRequestException` (RUN `forward/`, both platforms: a Content-Length body cut by a FIN or an RST, or a chunked
   body with no last chunk, arrives as `HttpRequestException` around an `HttpIOException` or an `IOException`), so an
   `IOException` filter would hold by accident, and stop holding the day the forward streams its body.
4. **The managed `Abort()` writes a whole empty `200 OK` response** when nothing was sent (§2.3). Added bare to the
   hang-up catch, it would answer a half-closed caller, or one whose chunked body was broken, `200` for a request the
   upstream never saw.
5. **On Windows, `Abort()` after the listener closes throws `ObjectDisposedException`, and `Close()` on a connection
   http.sys dropped throws `HttpListenerException` 1229** (§2.3). A handler that finishes after `DisposeAsync` closed
   the listener is exactly that case, so every end of a response needs a guard.
6. **A fourth leaking path:** a failed forward whose caller had already hung up (`TryRespondBadGateway`'s catch). The
   issue names the catch; its repro does not drive it. RUN path D: 10 of 10 held on Linux.
7. **A half-closed caller waits for ever on Linux and macOS:** nothing is sent and the connection sits in CLOSE_WAIT until
   the tap is disposed (RUN path C; the listener's side of it is dotnet/runtime#39183). http.sys answers such a caller
   `400` itself.
8. **`OtlpTap` misreads hang-ups too** (§2.4 O1, O2), without the leak, and its chunked reader has two more defects
   (O3).
9. **Nothing bounds the caller's side.** A caller that stops sending, or stops reading, without hanging up holds its
   handler and connection until disposal on the managed listener (READ; http.sys has its own entity-body and send-rate
   timers). Not the issue's case: Q2. A caller that stalls inside its request **head** never reaches the tap at all: the
   managed listener re-reads a partial head without re-arming its timer (SOURCE M/HttpConnection.cs:320), so only
   disposal releases it, and no tap-side limit can.
10. **A chunked request cut short by a FIN is forwarded as if whole** on Linux, and so on macOS, which runs the same
    code (RUN `chunked/`). The read ends with no error after a whole chunk or half of one. The tap forwards what arrived
    with a `Content-Length` of its own (10 bytes, or 5), records the exchange and answers the caller `200`; on Windows
    http.sys answers `400` and nothing is forwarded. With no exception, R1's catch cannot see it, and the listener
    exposes nothing that tells such a body from a whole one. A caller killed mid-upload closes with a FIN whenever it has
    nothing unread, so for chunked uploads this is the issue's own scenario. Q6.

## 4. The design

### 4.1 R1: `ProxyTap` ends every response it does not answer (the ask; a patch)

The prototype is [`PROXY_TAP_HANG_UP_PLAN.prototype.patch`](PROXY_TAP_HANG_UP_PLAN.prototype.patch) (one file, 88 lines added, 23 removed).

1. **One owner of the response.** A private `CallerAnswer` wraps the `HttpListenerResponse`. `Close()` sets `Closed`
   only after `Response.Close()` returns. `EndUnclosed()` does nothing when `Closed`; otherwise it sets
   `StatusCode = StatusIfUnclosed` (502 by default), then calls `Abort()`. Setting the status once the head has gone is
   harmless (the setter does not throw, and the value is never sent, §2.3); a closed response throws
   `ObjectDisposedException`, and on Windows `Abort()` throws it once the listener has closed. **`EndUnclosed()` never
   throws:** in the release each step swallows every exception, where the prototype names three types. It runs in a
   `finally` on a fire-and-forget task, and anything escaping it would skip the `_inflight` decrement (so
   `DisposeAsync` waits its full 2 s) and surface as an unobserved task exception; a failed end leaves the response
   exactly as today. It becomes `internal` in the release so the facts in §5 can drive it directly.
2. **`GuardedHandleAsync`** creates the `CallerAnswer`, passes it down, and calls `EndUnclosed()` in its `finally`,
   before `_inflight` is decremented. Every exit is covered, including those #171 adds (§7).
3. **The body read decides the caller's failures.** L182-187 get their own `try`/`catch (Exception)`: any failure there
   means the request never arrived whole (the caller hung up or half-closed, the chunked body was broken, or the tap is
   stopping). It sets `StatusIfUnclosed = 400` and returns: no forward, no `ForwardFailures`, no `proxy-tap error` line,
   no record (Q1). The catch is untyped on purpose (§3.2): the step decides, not the platform's choice of exception.
   For R1-5 the read goes through an internal seam, a `Func<Stream, Stream>?` that wraps `request.InputStream` and is
   `null` in production, so a fact can throw any type on any platform. 4.18.1's `OtlpTap` seams
   (`AfterResponseWritten`, `BeforeQueued`) are the precedent.
4. **The hang-up catch at L157-160** keeps its types, but after R1 the listener no longer reaches it: the body read and
   the answer write both have their own catches. What still lands there is an `ObjectDisposedException` from another
   step, such as the upstream client disposed by `DisposeAsync` after its 2 s drain, or a disposed sink. Its comment
   ("The caller hung up") changes to say so. #171's plan then routes both, the client disposed after the drain as the
   tap stopping and the disposed sink as a capture fault, and the #171 release that builds on R1 removes this catch,
   so nothing else unexpected passes for a hang-up (§4.3).
5. **`RespondAsync` and `TryRespondBadGateway`** take the `CallerAnswer` and call its `Close()`. Their catches stay as they
   are; their comments say the `finally` aborts. `TryRespondBadGateway` keeps setting 502 first (L565) and
   `StatusIfUnclosed` is 502 by default, so whatever stops a 502 before it is written, a managed-listener caller still
   there hears a 502 head, never a 200.
6. **Doc comment** of `ProxyTap.ForwardFailures` (L72): add "A caller that hangs up is not counted."

Why not the issue's three `Abort()` calls: a fourth or fifth exit (#171 adds one) has to remember a fifth call; the bare
call answers a listening caller `200` (§3.4); and it throws on Windows after the listener closes (§3.5).

Why a flag and not an unconditional `Abort()` in the `finally`: `Abort()` after `Close()` does nothing on both platforms
today (§2.3), but that is the BCL's behaviour on one version (the issue itself says "checked on .NET 10.0.12"), and on the
managed listener a closed keep-alive response's connection already serves the next request. The flag never touches a
closed response, and a `Close()` that threw (Windows 1229) leaves it unset, so that response is still aborted.

Behaviour changes for the changelog: on Linux and macOS a caller that hangs up no longer holds a connection until the
tap is disposed; a caller that half-closes in the middle of a Content-Length body is answered `400 Bad Request` at once
(http.sys's answer on Windows) where it heard nothing; on Linux and macOS a request whose caller hangs up mid-body is no
longer counted in `ForwardFailures`, logged as `proxy-tap error` or answered with a 502 attempt. A chunked body cut
short by a FIN is still forwarded as if whole (§3.10). Cost on the success path: one small object per exchange.

### 4.2 R2: `OtlpTap` reads a hang-up as a hang-up (a patch; found on the way)

1. **`ForwardAsync`** keeps only `SendAsync` and `ReadAsByteArrayAsync` in the `try` that counts `ForwardFailures` and
   answers 502; the answer write moves after it, where a failure is the exporter's and falls to `ServeAsync`'s existing
   hang-up catch (L298-301), silent and uncounted.
2. A `TaskCanceledException` while the tap's own token is cancelled is the tap stopping, not a forward failure: it ends
   the connection without a count or a 502 (the same split #171 makes for `ProxyTap`, §7).
3. **`ReadChunkedAsync`** returns "incomplete" when the stream ends before the zero-size chunk, or a size line is not hex,
   and `ReadAsync` returns `null` for it, as the Content-Length path already does (L101). The export is not counted, not
   queued and not answered; the connection closes (Q5 asks whether a broken size line should get a 400 instead).
4. **The rest of `ReadChunkedAsync` (O3, found while checking the plan).** It reads trailer lines up to the blank line
   that ends them (RFC 9112 §7.1.2), so the next request on the connection starts where it should. A size that does not
   fit an `int`, which today parses as negative, is too large: `413`, as a body over `MaxRequestBytes` is, not an
   `OverflowException`.
5. **Doc comment** of `OtlpTap.ForwardFailures` (L93) and the diagnostic's wording (L151) stay true once 1 and 2 hold;
   no change.

### 4.3 How R1 meets the sibling plans

- **#171** (`PROXY_TAP_FAILED_FORWARD_PLAN.md`) puts a catch around the forward (L282-296) that records a failed forward,
  and takes everything thrown on the capture side (an `IdentityResolver`, a sink or `TryDecodeBody` that throws, all
  counted as forward failures today and measured in its harness), the forward cut short by the tap's own disposal (L257:
  the deadline is linked to the lifetime token, so a disposal mid-forward counts today), and the 502 body. Its 502 goes
  through `TryRespondBadGateway`, so it sets `Closed` like any other answer, and R1's `finally` aborts whatever its new
  exits leave. It builds on R1.
- **#170** shipped first (4.18.3 and 4.19.0, the owner's call, §7). It changes `HandleAsync` L189-254, `ProxyTapOptions`,
  `ProxyTapServiceCollectionExtensions.Copy` (the DI defect in §8) and core's `InternalFlowSegmentBuilder`, and appends
  facts to ProxyTapTests.cs. None of those lines is R1's, so R1 rebased over it, and over 4.19.1, #171's first release;
  Appendix A's numbers are at `34145c44`, and both moved them (`GuardedHandleAsync` starts at L158 in 4.19.1). R1's
  facts are in a class of their own.

### 4.4 Not in this plan

- Time limits on the caller's side, for both taps (§3.9, Q2).
- The macOS accept crash (§1, item 12), a .NET defect.
- The managed listener reads a **chunked** request whose caller half-closes mid-body as complete (SOURCE
  M/ChunkedInputStream.cs:122-127), and `ProxyTap` forwards it cut short (RUN, §3.10). No catch sees it and the
  listener's public surface cannot tell it apart, so the fix belongs to .NET, which has no issue for it yet
  (dotnet/runtime#39183 is the FIN unnoticed after the request is read): Q6.
- The test stubs' own `Abort()` hygiene (§2.4), except where R1's facts use them.

## 5. Tests, red first

`tests/Kronikol.Tests.ProxyTap` (internals visible) for R1, `tests/Kronikol.Tests.Otlp` for R2. "Managed" facts need
the managed listener and read its tables by reflection (`_listenerContexts`, `_connections`, the names `probe.cs`
lists); they fail with a named message if a .NET version renames them. On Windows they skip with the reason the
owner's rule asks for: the condition cannot occur there (http.sys owns the connection and drops it itself, §2.2), the
code under test is the same, and Linux CI runs them. Both projects are xUnit v3, so the skip is
`Assert.SkipWhen(OperatingSystem.IsWindows(), "…")`, as `TcpTap`'s `ContainerEndToEndTests.cs:53` skips without Docker.
A caller resets only once the tap holds its request (`_inflight`), never before the accept (§1, item 12). "Red before
the fix" means on `34145c44` with only the test seam added (R1-5); R1-6 tests the new type, so it is red against a
`CallerAnswer` whose `EndUnclosed()` is the issue's bare `Abort()`.

| Fact | Platforms | Red before the fix | Proves |
|---|---|---|---|
| R1-1 `A_caller_that_resets_while_the_answer_is_written_leaves_no_connection_held` | managed | Linux | path A released; the exchange is still recorded and counted handled |
| R1-2 `A_caller_that_resets_mid_body_leaves_no_connection_held_and_is_no_forward_failure` (theory: an RST alone; a FIN then an RST) | managed | Linux: both hold the connection (B′ by reading, §2.2), and the RST alone counts 10 and logs `proxy-tap error` | paths B and B′ released; `ForwardFailures` 0, no `proxy-tap error`, the upstream saw nothing |
| R1-3 `A_caller_that_half_closes_mid_body_is_answered_400_and_let_go` | all | Linux (it hears nothing); Windows passes before and after (http.sys) | path C; never `200` |
| R1-4 `An_upstream_that_fails_after_its_caller_hung_up_is_a_forward_failure_and_leaves_no_connection_held` | managed | Linux | path D; the count stays |
| R1-5 `A_body_read_that_throws_is_the_callers_failure_whatever_the_exception` | all | all | theory over `IOException`, `HttpListenerException`, `ObjectDisposedException`, `InvalidOperationException`, thrown by the request stream through a test seam on the read (an internal `Func<Stream, Stream>?` wrapping `request.InputStream`, null in production). It pins the catch as untyped, which R1-2's real closes cannot, since they throw only `IOException` and `HttpListenerException` |
| R1-6 `Ending_an_unclosed_response_never_throws` | all | Windows (after `HttpListener.Close()`), against the issue's bare `Abort()` | `CallerAnswer.EndUnclosed()` after `Close()`, after `Abort()`, twice, after the listener closed, after a failed write |
| R1-7 `An_upstream_body_cut_short_stays_a_forward_failure` | all | none (a guard) | an upstream that sends half its Content-Length and resets: `ForwardFailures` 1 and a 502. It kills a fix that takes every failure, or an `IOException` anywhere in the exception's chain, as a hang-up; a filter on the outer type alone passes it, since `SendAsync` wraps the cut in `HttpRequestException` (§3.3) |
| R1-8 `Keep_alive_requests_are_answered_on_one_connection` | all | none (a guard) | 20 requests on one connection after the change |
| R2-1 `An_exporter_that_hangs_up_before_its_answer_is_no_forward_failure` | all | Linux | O1: the collector got it, `ForwardFailures` 0, no `forward failed` line |
| R2-2 `A_forward_cut_short_by_disposal_is_no_forward_failure` | all | all (RUN `otlp/otlp-dispose-*`: `ForwardFailures` 1, `otlp-tap forward failed: The operation was canceled.`, and the exporter gets no 502) | a collector held open, the tap disposed mid-forward. The fact waits for the handler: `DisposeAsync` returns before it ends (on Windows the count reached 1 only after it returned) |
| R2-3 `A_chunked_export_cut_short_is_not_received` (theory: no zero-size chunk; a size line that is not hex) | all | all | O2: no `200`, `RequestsReceived` 0, nothing queued |
| R2-4 `A_complete_chunked_export_is_received_and_answered` | all | none (a guard) | the reader still takes a whole chunked body, with a chunk extension |
| R2-5 `A_chunked_export_with_trailers_keeps_the_connection_in_step` (theory: one trailer field, two) | all | all | O3: a second export on the same connection is answered `200` and received |
| R2-6 `A_chunk_size_past_int_is_too_large` (theory: `80000000`, `ffffffff`) | all | all | O3: `413`, `PayloadsRejected` 1, no `connection error` line |

Proofs, as the recent plans give them: each fact that is not a guard red on `34145c44` (Linux container for the managed
ones, recipe in the harness README) and green on the fix; and these mutations killed: drop `EndUnclosed()` from the `finally` (R1-1, -2,
-3, -4); drop the status before `Abort()` (R1-3: the caller gets `200`); replace the body read's catch with a type filter
in `GuardedHandleAsync` (R1-5); drop the guard around `Abort()` (R1-6 on Windows); move the answer write back into
`ForwardAsync`'s `try` (R2-1); return the partial chunked body again (R2-3); read one trailer line again (R2-5); drop
the negative-size check (R2-6). The one change no fact can tell apart,
`Abort()` without the flag, is harmless on .NET 10 (§4.1) and is named as such.

Then the full ProxyTap and Otlp projects on Windows and in the Linux container, the core suite after the version bump,
and `release.slnf` in Release for every target.

## 6. Releases and records

### 6.1 Releases

R1 is 4.19.2, the next free patch when it was ready (4.18.3 and 4.19.0 went to #170, 4.19.1 to #171's first release;
each number claimed with "taking X now" at push, §7). R2 is the patch after it (Q3). Both are patches: bug fixes,
nothing new for a consumer to call (`CallerAnswer` is internal); the behaviour changes are listed in the changelog, not
in the bump.

### 6.2 Changelog draft (R1)

> **Fixed.** `ProxyTap` no longer holds the connection of a caller that hangs up (#172). On Linux and macOS the
> `HttpListener` keeps a connection until its response is closed or aborted, and the tap did neither when it gave up on
> a response: a caller that hung up while its answer was written, mid-body, or after a forward failed left its connection
> open until the tap was disposed (10 of 10 on each path, measured). Every response the tap does not answer is now
> aborted, with a failure status set first, since the managed listener's `Abort()` writes a whole empty response when
> none was sent and would otherwise answer `200 OK`. A caller that half-closes in the middle of a Content-Length body is
> answered `400 Bad Request` at once, as http.sys does on Windows, where it heard nothing before. A request whose caller
> hangs up before its body arrives is no longer counted in `ForwardFailures` or logged as `proxy-tap error` (on Linux
> and macOS it was whenever the read threw an `IOException`, as a connection reset makes it do); whether the read
> failed decides, not the exception type, which differs by platform and by how the caller closed. Windows held nothing
> and counted nothing. A patch: a bug fix, nothing new for a consumer to call.

### 6.3 Wiki (`../Kronikol.wiki` at `origin/master` `e5c5a5b`; the lines below are the same as at `85abe19`; edit in a worktree of its own after publication)

- `Integration-ProxyTap-Extension.md`: a short paragraph after L80 (disposal) or in Limitations (L174-178) on what the tap
  does when a caller hangs up. Today the page never mentions it, `ForwardFailures`, the 502 or `ProxyTap.Diagnostics()`.
  If the owner agrees (Q6), a Limitations bullet too: on Linux and macOS a chunked request cut short by the caller's FIN
  reaches the service as if whole (§3.10).
- `Diagnostics-and-Debugging.md` L158 (the prose naming `ProxyTap.Diagnostics()`; L154 is inside a code sample): the
  forward-failure line counts requests the tap could not forward, and a caller that hangs up is not one. No wiki page
  quotes the line itself.
- R2: `Integration-Otlp-Extension.md` L143 (the tap's counters): an exporter that hangs up is not counted, and a chunked
  export cut short is not received. The same line leaves out `PayloadsRejected` and `PayloadsFailed` (OtlpTap.cs:100,
  :103); name them while there.

### 6.4 Plans, roadmap, Kronikol4J

- `PLANS_STATUS.md`: this plan's row, updated per release.
- No `ROADMAP.md` row unless the owner wants one (Q7).
- Kronikol4J's divergence ledger (`docs/REMAINING_PARITY.md`, the section headed at L1877 at `4ff5c07`, newest entry
  last): one line per release in the format of its last entries, "- **What changed (.NET 4.x.y, date,
  `PROXY_TAP_HANG_UP_PLAN.md` R1, #172).** Nothing to port: …". For R1, the port has no proxy tap (L1922); for R2, it has
  no OTLP tap (L2439).

## 7. Order against the sibling plans

Agreed by message on 2026-10-13 between this session (kronikol-a5), kronikol-70 (#171) and kronikol-ef (#170), then
changed by the owner the same day: #170's two releases (a patch, then a minor with a new option) go as soon as they
pass, without waiting for the others. After them, a release from #171's plan may go first if the owner takes it; then
R1 (#172); then #171, which builds on R1's `CallerAnswer`. Version numbers are claimed with "taking X now" just before
each push. R2 touches only `Kronikol.Extensions.Otlp` and can go at any point after R1.

As it went, the same day: #170 shipped as 4.18.3 and 4.19.0. The owner then asked for #171 and for this plan in full,
and #171's first release went out as 4.19.1. R1 followed as 4.19.2, and #171's next release builds on its
`CallerAnswer`, taking the two follow-ups §4.1 item 4 names (the typed hang-up catch removed, and the catch-all counting
a 502 that was the answer as a forward failure and anything after a closed answer as a capture fault). R2 rebases over
4.19.1, which changed the start of `OtlpTap.ForwardAsync`.

## 8. Found on the way, not in this plan

- **Handed to #171:** the capture-side throws counted as forward failures, the disposal mid-forward counted as one, the
  forward's spans never stopped on a failure, the 502 body that is not JSON when the message holds a line break.
- **Handed to #170:** `ProxyTapServiceCollectionExtensions.Copy` (L42-75) copies every option but `InFlightRegistry`
  (`ProxyTapOptions` L125), so a tap registered through the list overload never publishes in-flight identities; that
  overload has no fact. #170's first release fixes it, with a fact that every option is copied.
- **`TcpTap`:** an upstream connect timeout lands in the "shutting down" catch (no log, no count) and a refused connect is
  only logged, never in `Diagnostics()` (TcpTap.cs:247-253, READ). Q7.
- **`OtlpTap`:** a stalled exporter holds its handler, a 16 KiB buffer (HttpRequestReader.cs:38) and a body array sized to
  its Content-Length up front (L150), up to `MaxRequestBytes` (32 MiB), until disposal. Part of Q2.

## 9. Questions, with recommendations

**Answered 2026-10-13:** the owner green-lit the plan in full, so each question took its recommendation. Q2 is #173, Q6
is dotnet/runtime#135680 with the wiki's Limitations bullet, and Q7 is #174; Q1, Q3, Q4 and Q5 shaped the releases as
written.

- **Q1.** Should a request whose caller hung up before its body arrived be drawn? *Recommendation: no.* Nothing reached
  the service; #171 draws failed forwards, which did. As planned such a request leaves no trace at all, which is what
  Windows does today, and Linux after a FIN. If the owner wants a trace, the alternative is a new counter (say `CallerHangUps`) and a
  `Diagnostics()` line, which is new public surface and so a minor of its own, not part of R1.
- **Q2.** Time limits on the caller's side (the body read, the answer write) for both taps? *Recommendation: a separate
  issue.* It is a behaviour change with its own defaults to choose; on the managed listener it has to be `Abort()` on a
  timer, since a token does not interrupt body I/O already running (§2.3). It cannot cover a caller stalled inside its
  request head, which never reaches the tap (§3.9), nor a `TcpTap` pair whose upstream goes quiet after a half-close
  (§2.4).
- **Q3.** R2 in R1's release or its own? *Recommendation: its own patch right after R1*: another package and wiki page,
  and #171 waits on R1 alone.
- **Q4.** A macOS lane for the tap tests? *Recommendation: no.* Linux CI drives the `IOException` for real (R1-2's RST
  alone) and R1-5 pins the untyped catch on every platform; a lane would add the accept-crash hazard (§1, item 12) for
  no new coverage.
- **Q5.** `OtlpTap`: answer `400` to a size line that is not hex, rather than closing? *Recommendation: close, as the
  Content-Length path does today*, and revisit with Q2.
- **Q6.** The cut-short chunked request is now measured (§3.10): report it to dotnet/runtime with
  `chunked/chunked-probe.cs` as the repro, and add the wiki's Limitations bullet (§6.3)? *Recommendation: yes to both,
  and no Kronikol-side check.* The only check possible reads the listener's private chunk decoder by reflection from a
  shipped package. If .NET makes the read fail, R1's catch takes it like any other failed read.
- **Q7.** File `TcpTap`'s uncounted connect failures as an issue, and place this plan in `ROADMAP.md`? *Recommendation:
  file the issue; no roadmap row* (two patches).

## 10. Assumption ledger

| Assumption | How it holds |
|---|---|
| macOS behaves as the issue measured | Not re-run (no macOS here); the managed source is shared with Linux, Linux throws macOS's `IOException` for an RST alone (`reset/`), and R1 does not depend on which type is thrown |
| The BCL's private table names hold across .NET versions | The managed facts name the field they could not find and fail, never pass silently |
| Windows' result is not luck | 0 active requests on every path, run twice on 4.18.1 and once on the prototype; SOURCE: a failed write already cancels the request (W/HttpResponseStream.Windows.cs:139, 200, 216, 256, 358; `WriteAsync` completes through :256) |
| R1's prototype is the design | It is a sketch for measurement; the release follows §5 red first and may name things differently |

## 11. Log

- 2026-10-13: plan written (session kronikol-a5), with the harness and the prototype patch; the order in §7 agreed with
  kronikol-70 and kronikol-ef. Nothing implemented.
- 2026-10-13, later: checked again at the owner's ask, against .NET's source, every line cited, the wiki
  (`e5c5a5b`), Kronikol4J's ledger (`4ff5c07`) and six more probes (harness `reset/`, `socket/`, `chunked/`,
  `forward/`, `abort/`, `otlp/otlp-dispose-probe.cs`). What changed:
  - The first probes' "reset" was `TcpClient.Dispose()` with a zero linger, which sends a FIN before its RST. An RST
    alone throws `IOException` on Linux and 4.18.1 counts it, so the miscount is not macOS-only (§0, §1, §2.2 B′, §2.3).
  - On Linux a chunked request cut short by a FIN is forwarded as if whole, and no catch can see it (§3.10, Q6).
  - The managed `Abort()` writes a whole empty response, and on Windows a caller still sending gets a reset (§2.3).
  - The `StatusCode` setter does not throw once the head has gone. `EndUnclosed()` now swallows every exception, so
    the `_inflight` decrement always runs. The test seam is named, and so is what the L157 catch still takes (§4.1).
  - R2 also takes the reader's trailer and size-overflow defects (O3, R2-5, R2-6). R2-2 is measured, and R1-7 says
    what it can and cannot kill (§5).
  - Corrected references: OtlpTap L93, the async read lines, the Windows abort lines, the CI lanes, Kronikol4J's ledger
    lines, the wiki's prose line (L158) and dotnet/runtime#39183, which is the FIN going unnoticed, not the chunked
    case.
  - The owner put #170 first (§7).
- 2026-10-13, R1 released as 4.19.2 (session kronikol-a5, in a worktree of its own), the owner having asked for the
  plan in full. It is §4.1 as written: `CallerAnswer` is internal and nested in `ProxyTap`, `EndUnclosed()` swallows
  every exception, the body read has its own untyped catch (status 400, return), the typed hang-up catch stays with its
  new comment (#171's next release removes it, §7), `RespondAsync` and `TryRespondBadGateway` take the answer, and the
  facts get an internal `InFlight` and the `WrapRequestBody` seam.
  - Red (harness `r1/red-4.19.0.txt`): on 4.19.0's code with only the seam, `InFlight` and a `CallerAnswer` whose
    `EndUnclosed()` is the bare `Abort()`, 9 runs fail on Linux and 5 on Windows, each on its own assertion. On Linux
    the leak facts find 1 context and 1 connection still held, the half-closed caller hears nothing, and R1-5 gets a
    502 or nothing. On Windows R1-5 fails for all four types and R1-6 after the listener closed
    (`ObjectDisposedException`). R1-6's other cases and the two guards pass, as §5 says they would.
  - Mutations (harness `r1/mutations.txt`, tools in `mutation/`): of 12, 10 are caught by the facts §5 names, on the
    platform it names. The 2 that survive are §4.1's harmless pair: `Abort()` without the flag, and the 502 closing
    the response without setting it. Without the status, the half-closed caller hears `HTTP/1.1 200 OK` (M02), the trap
    §3 finding 4 describes.
  - Found on the way: `StubUpstream`'s per-request catch took only `HttpListenerException` and
    `ObjectDisposedException`, so an `IOException` (macOS) ended its loop, and it never ended a failed response. It now
    aborts the response whatever was thrown (§4.4 left the stubs alone except where R1's facts use them, and they use
    this one).
  - Checks (harness `r1/checks.txt`): after the bump, the ProxyTap project passes 82 of 82 on Linux in Debug and in
    Release and 78 with 4 skipped on Windows, the core suite 7,458 with 2 skipped, and `release.slnf` builds in Release
    for every target.
  - Outward, per §9: #173 (Q2), #174 (Q7) and dotnet/runtime#135680 (Q6), whose repro is `chunked/runtime-repro.cs`
    with its output on both platforms.

## Appendix A. Edit sites at `34145c44`

| File | Lines | R | Change |
|---|---|---|---|
| `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs` | 150-171 | R1 | `CallerAnswer` created, passed down, `EndUnclosed()` in the `finally`; the hang-up catch's comment (157-160, §4.1 item 4) |
| same | 173, 181-187 | R1 | `HandleAsync` takes the `CallerAnswer`; the body read's own catch (status 400, return), reading through the test seam |
| same | new, beside the counters | R1 | the internal seam (`Func<Stream, Stream>?`, `null` in production) for R1-5 |
| same | 307 | R1 | `RespondAsync(answer, …)` |
| same | 526-559, 561-577 | R1 | both take the `CallerAnswer` and call its `Close()` |
| same | new, after 577 | R1 | `internal sealed class CallerAnswer` |
| same | 72 | R1 | `ForwardFailures` doc comment |
| `src/Kronikol.Extensions.Otlp/OtlpTap.cs` | 389-421 | R2 | the answer write out of the forward's `try`; the tap's own cancellation not counted |
| `src/Kronikol.Extensions.Otlp/HttpRequestReader.cs` | 86, 165-200 | R2 | a chunked body cut short or with a broken size line is incomplete (`null`); trailer lines read up to the blank line (L180); a size past `int` is too large (L176) |
| `src/Kronikol.Extensions.Otlp/OtlpTap.cs` | 289-290, 310-314 | R2 | the `null` request closes the connection (no change); the too-large answer takes the new case |
| `tests/Kronikol.Tests.ProxyTap/` | new class | R1 | §5 R1-1 to R1-8 |
| `tests/Kronikol.Tests.Otlp/` | new facts | R2 | §5 R2-1 to R2-4 |
