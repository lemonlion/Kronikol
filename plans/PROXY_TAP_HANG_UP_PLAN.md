# A caller that hangs up on a tap (#172)

**Date:** 2026-10-13 · **Repo version:** 4.18.1 (`origin/main` at `34145c44`, the commit the issue's line numbers are for;
every line number below is for it) · **Status:** ❌ plan written, nothing implemented, **NOT green-lit**. Drafted from #172
(filed 2026-10-13) at the owner's ask of 2026-10-13 to analyse the issue critically and plan a fix for the underlying
problem. Written alongside the plans for #171 (`PROXY_TAP_FAILED_FORWARD_PLAN.md`, session kronikol-70) and #170
(session kronikol-ef), which change the same method; §7 is the order the three sessions agreed.

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
- **The miscount is real but macOS-only in practice.** The same hang-up mid-body reads as an `IOException` on macOS
  (ISSUE), as `HttpListenerException` 400 on Linux (5 of 5, RUN) and as `HttpListenerException` 995 on Windows (5 of 5,
  RUN). Only the first lands in the catch that counts `ForwardFailures`.
- **The underlying problem is two design gaps, not three missing calls.** No one owns the caller's response: each of the
  exits from `GuardedHandleAsync` decides alone whether to close it, and four of them do not. And failures are sorted by
  exception type in one catch around the whole exchange, when the type for the same event differs on every platform;
  only the step that failed (reading the request, forwarding, writing the answer) says whose failure it was.
- **Three corrections to the suggested fix.** (1) The managed `Abort()` is not an abort when nothing has been sent: it
  writes the response head first, `200 OK` by default, so adding it to the hang-up catch would tell a caller still
  listening that its request succeeded (RUN; dotnet/runtime#88196). (2) On Windows, `Abort()` after `HttpListener.Close()`
  throws `ObjectDisposedException` (RUN); the issue checked macOS only. (3) "Treat an `IOException` as a hang-up" must
  be decided by the step, not the type, or the fix is right on one platform and wrong on two.
- **R1 (patch, the ask):** `GuardedHandleAsync` owns the response through a small `CallerAnswer`; the body read gets its
  own catch that ends the exchange as the caller's; one `finally` ends every response the tap did not close, with a
  failure status set first and every throw guarded. The prototype releases every connection on all four paths on Linux,
  answers a half-closed caller `400` (what http.sys answers on Windows), still counts real upstream failures and leaves
  keep-alive alone (RUN).
- **R2 (patch, found on the way):** `OtlpTap` does not leak (it owns its sockets) but misreads hang-ups the same way.
  With forwarding on, an exporter that hangs up before its answer is counted as a forward failure while the collector
  did receive the export (Linux, RUN); and a chunked export cut short, or with a size line that is not hex, is answered
  `200 OK` and counted as received (both platforms, RUN).
- **Questions for the owner (§9):** Q1 whether a request cut short should be drawn, Q2 caller-side time limits for both
  taps, Q3 whether R2 rides with R1, Q4 a macOS lane for the tap tests, Q5 to Q7 smaller ones.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | The managed `HttpListener` keeps a connection until its response is closed or aborted | **Confirmed** | SOURCE: the read timer starts in `BeginReadRequest` (M/HttpConnection.cs:195) and stops after the first read (:257); a dispatched context stays in `_listenerContexts` (M/HttpListener.Managed.cs:192) and its connection in `_connections` (HttpConnection.cs:312) until `Close`/`Abort` or listener close. RUN: path A–D below |
| 2 | `RespondAsync`'s write throws once the caller has reset; its catch (L555-558) does nothing else; the `Close()` at L553 is never reached | **Confirmed** | READ; RUN path A, Linux: 10 of 10 held |
| 3 | 10 callers that read one byte of an 8 MB answer and reset leave 10 connections open | **Confirmed on Linux** (macOS per the issue) | RUN `probe/probe-4.18.1-linux.txt` |
| 4 | Reading a reset caller's body (`CopyToAsync`, L184) mostly throws `IOException` | **Line and platform corrected.** The call is L185 (L184 is the `MemoryStream`). `IOException` is macOS's answer; Linux and Windows throw `HttpListenerException` | RUN `listener/listener-{linux,windows}.txt` section 1; SOURCE: an RST passes the socket's `IOException` through (M/HttpRequestStream.Managed.cs:113, :196-198), an end of stream with bytes still owed throws `HttpListenerException(400)` (:117-119, :203-205); Linux's socket reports the reset as an end of stream here |
| 5 | The hang-up catch at L156 takes only `HttpListenerException` or `ObjectDisposedException` | **Confirmed, at L157** | READ |
| 6 | So it lands in the forward-failure catch: `ForwardFailures` goes up, `proxy-tap error` is logged, `TryRespondBadGateway`'s write fails into a catch (L573-576) that does not abort | **Confirmed by reading; the count is macOS-only** | READ; on Linux the same read goes to the hang-up catch (RUN path B: `ForwardFailures` 0) |
| 7 | The odd read that fails with `HttpListenerException` goes to the hang-up catch, which does not abort | **Confirmed, and on Linux and Windows it is every read** | RUN path B, C |
| 8 | `Diagnostics()` then reports a failing upstream that was fine | **Confirmed by reading** (L90-93), on macOS | READ |
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
| Body read fails with `HttpListenerException`/`ObjectDisposedException` (Linux, Windows) | hang-up catch L157-160 | ❌ | ❌ | nothing |
| Body read fails with anything else (`IOException` on macOS) | catch-all L161-166 | ❌ (the 502 write fails) | ❌ | `ForwardFailures` + `proxy-tap error` line |
| Forward fails, caller still there | catch-all → `TryRespondBadGateway` L561-577 | ✅ L571 | | `ForwardFailures` (correct) |
| Forward fails, caller gone | catch-all → `TryRespondBadGateway`'s catch L573-576 | ❌ | ❌ | `ForwardFailures` (correct) |
| Tap disposed | `DisposeAsync` L601 `_listener.Close()` | the listener force-closes everything (SOURCE M/HttpListener.Managed.cs:217-243) | | |

The forward's deadline is linked to the tap's lifetime (L257-259), so `ForwardTimeout` bounds only the upstream. Nothing
bounds the body read (L185) or the answer write (L552).

### 2.2 The measurements (RUN)

`probe/probe.cs`, 10 callers per path, 3 s after the hang-up. "Held" is the managed listener's own tables
(`_listenerContexts`, `_connections`, read by reflection) and, on Linux, socket descriptors the process still holds that
no TCP table lists (a reset socket leaves `/proc/net/tcp` but not the descriptor table). On Windows, http.sys's queue
(`netsh http show servicestate`).

| Path | Linux 4.18.1 | Linux prototype | Windows 4.18.1 and prototype | macOS 4.18.1 (ISSUE) |
|---|---|---|---|---|
| A. reset while the tap writes an 8 MB answer | **10 held**; recorded, `ForwardFailures` 0 | 0 held; recorded | 0 held, 0 active requests | 10 open |
| B. reset mid-body | **10 held**; `ForwardFailures` 0 | 0 held | 0 held; `ForwardFailures` 0 | 10 open; `ForwardFailures` 9 or 10 |
| C. half-close (FIN) mid-body, caller still reading | **10 held** in CLOSE_WAIT; the caller hears nothing for 5 s | 0 held; the caller gets `400 Bad Request` | 0 held; http.sys answers `400 Bad Request` itself | not measured |
| D. upstream fails after the caller reset | **10 held**; `ForwardFailures` 10 (correct) | 0 held; `ForwardFailures` 10 | 0 held; `ForwardFailures` 10 | not measured |
| E. control: 20 keep-alive requests | 20 answered on 1 connection | same | same | |

Every held connection on Linux was released by `DisposeAsync` (all tables 0 afterwards).

### 2.3 What the listener does (SOURCE, RUN)

`listener/listener-probe.cs` on a bare `HttpListener`, the same file on both platforms:

| | Managed (Linux, macOS) | http.sys (Windows) |
|---|---|---|
| A dispatched connection is watched | No: no timer, no pending read (SOURCE, item 1 of §1) | http.sys drops the request when the connection dies (RUN: 0 active requests) |
| Body read, caller resets | `HttpListenerException` 400 (Linux, 5/5); `IOException` mostly (macOS, ISSUE) | `HttpListenerException` 995 (5/5) |
| Body read, caller half-closes | `HttpListenerException` 400; nothing is sent; the connection waits | `HttpListenerException` 995; http.sys has sent `400 Bad Request` |
| Answer write, caller reset | `HttpListenerException` ("Broken pipe", COR_E_IO) | `HttpListenerException` 995 |
| `Abort()` with no head sent, caller listening | **Writes the head with the status set, `200 OK` by default, then FIN** (RUN; SOURCE `HttpConnection.Close` always closes the response stream, M/HttpConnection.cs:508, which sends an unsent head, M/HttpResponseStream.Managed.cs:56-90; dotnet/runtime#88196, open) | Nothing more |
| `StatusCode = 400`, then `Abort()` | `400 Bad Request`, then FIN | Nothing more |
| `Abort()` after `Close()`, twice, or on a keep-alive response already closed | No effect, next request unharmed | No effect, next request unharmed |
| `Abort()` after `HttpListener.Close()` | No effect | **`ObjectDisposedException`** |
| `Close()` once the connection is gone | No exception (it closes the socket itself) | **`HttpListenerException` 1229** |

Two more facts from the source shape the design: on the managed listener `Close()` marks the response disposed before it
does anything, so an `Abort()` after a `Close()` that failed does nothing (M/HttpListenerResponse.Managed.cs:98-110);
and a cancellation token never interrupts body I/O already running, only `Abort()` does (CoreLib Stream.cs:303-306,
450-462), which matters for Q2.

### 2.4 The same defect elsewhere (READ, RUN)

- **`OtlpTap`** (plain sockets, not `HttpListener`) keeps each connection in `using (client)` (OtlpTap.cs:282), so every
  exit disposes it: no leak. It misreads hang-ups in two places, both measured with `otlp/otlp-probe.cs`:
  - **O1.** `ForwardAsync` writes the collector's answer to the exporter inside the forward's `try` (L411), whose catch
    takes `IOException` and `SocketException` (L414) and counts a forward failure (L416-417). An exporter that hangs up
    before its answer is therefore a "forward failure" although the collector received the export: Linux
    `ForwardFailures` 1 and `otlp-tap forward failed: Unable to write data to the transport connection: Broken pipe.`
    (Windows: the write did not fail, 0). `Diagnostics()` then says the export "could not be forwarded … and were
    answered 502 Bad Gateway … the real collector never saw them" (L148-151), false on both counts. The same catch takes
    `TaskCanceledException`, so a forward cut short by the tap's own disposal is counted too (READ).
  - **O2.** `HttpRequestReader.ReadChunkedAsync` (HttpRequestReader.cs:165) returns the bytes read so far as a complete
    body when the stream ends before the last chunk (L172) or a size line is not hex (L176), while a Content-Length body
    cut short returns `null` (L101, `// truncated`). A chunked export whose exporter half-closes mid-body, or one with a
    broken size line, is answered `HTTP/1.1 200 OK` and counted in `RequestsReceived` with no failure anywhere (both
    platforms, RUN).
- **`TcpTap`**: a failed read or write half-closes the other side and both sockets are disposed in a `finally`
  (TcpTap.cs:241-261, 283-318): no leak.
- **No other server** in `src/`: a search for `HttpListener`, `TcpListener`, socket accept and listen calls, Kestrel and
  named pipes finds only the three taps.
- **Kronikol4J** has no tap at all; its ledger already says so (`docs/REMAINING_PARITY.md`:1917-1924).
- **Test stubs** (`StubUpstream` in ProxyTapTests.cs:43-65, OtlpTapTests.cs:93, PlaywrightAdapterTests.cs:151) never
  abort either, and `StubUpstream`'s per-request catch takes only `HttpListenerException`/`ObjectDisposedException`, so
  on macOS an `IOException` would end its accept loop. Test-only; §4.4.

## 3. Findings the issue does not state

1. **Windows does not leak** (§2.2). CI runs the tap tests on Ubuntu only (`ci.yml`, "Extension Tests"), which is where
   the facts in §5 go red; this Windows box cannot show them red, so the red step runs in the Linux container
   (`PROXY_TAP_HANG_UP_PLAN.harness/README.md`).
2. **The exception type is per platform** (§2.3): `IOException` on macOS, `HttpListenerException` 400 on Linux, 995 on
   Windows, for the same reset. A rule keyed on type is right on one platform at most.
3. **A type rule in `GuardedHandleAsync` would also misfile upstream failures.** `HttpIOException` derives from
   `IOException`. Today `SendAsync` with `ResponseContentRead` wraps an upstream body cut short in
   `HttpRequestException` (measured in #171's harness), so an `IOException` filter would hold by accident, and stop
   holding the day the forward streams its body.
4. **The managed `Abort()` writes a `200 OK` head** when nothing was sent (§2.3). Added bare to the hang-up catch, it
   would answer a half-closed caller, or one whose chunked body was broken, `200` with an empty body for a request the
   upstream never saw.
5. **On Windows, `Abort()` after the listener closes throws `ObjectDisposedException`, and `Close()` on a connection
   http.sys dropped throws `HttpListenerException` 1229** (§2.3). A handler that finishes after `DisposeAsync` closed
   the listener is exactly that case, so every end of a response needs a guard.
6. **A fourth leaking path:** a failed forward whose caller had already hung up (`TryRespondBadGateway`'s catch). The
   issue names the catch; its repro does not drive it. RUN path D: 10 of 10 held on Linux.
7. **A half-closed caller waits for ever on Linux and macOS:** nothing is sent and the connection sits in CLOSE_WAIT until
   the tap is disposed (RUN path C). http.sys answers such a caller `400` itself.
8. **`OtlpTap` misreads hang-ups too** (§2.4 O1, O2), without the leak.
9. **Nothing bounds the caller's side.** A caller that stops sending, or stops reading, without hanging up holds its
   handler and connection until disposal on the managed listener (READ; http.sys has its own entity-body and send-rate
   timers). Not the issue's case: Q2.

## 4. The design

### 4.1 R1: `ProxyTap` ends every response it does not answer (the ask; a patch)

The prototype is [`PROXY_TAP_HANG_UP_PLAN.prototype.patch`](PROXY_TAP_HANG_UP_PLAN.prototype.patch) (one file, 88 lines added, 23 removed).

1. **One owner of the response.** A private `CallerAnswer` wraps the `HttpListenerResponse`. `Close()` sets `Closed`
   only after `Response.Close()` returns. `EndUnclosed()` does nothing when `Closed`; otherwise it sets
   `StatusCode = StatusIfUnclosed` (502 by default), swallowing the `InvalidOperationException`/`ObjectDisposedException`
   a response whose head has gone or which is closed throws, then calls `Abort()`, swallowing `ObjectDisposedException`,
   `HttpListenerException` and `InvalidOperationException` (Windows after the listener closed). It becomes `internal`
   in the release so the facts in §5 can drive it directly.
2. **`GuardedHandleAsync`** creates the `CallerAnswer`, passes it down, and calls `EndUnclosed()` in its `finally`,
   before `_inflight` is decremented. Every exit is covered, including those #171 adds (§7).
3. **The body read decides the caller's failures.** L182-187 get their own `try`/`catch (Exception)`: any failure there
   means the request never arrived whole (the caller hung up or half-closed, the chunked body was broken, or the tap is
   stopping). It sets `StatusIfUnclosed = 400` and returns: no forward, no `ForwardFailures`, no `proxy-tap error` line,
   no record (Q1). The catch is untyped on purpose (§3.2): the step decides, not the platform's choice of exception.
4. **`RespondAsync` and `TryRespondBadGateway`** take the `CallerAnswer` and call its `Close()`. Their catches stay as they
   are; their comments say the `finally` aborts. `TryRespondBadGateway` keeps setting 502 first (L565), so a 502 whose
   body write fails still reaches a managed-listener caller as a 502 head.
5. **Doc comment** of `ProxyTap.ForwardFailures` (L72): add "A caller that hangs up is not counted."

Why not the issue's three `Abort()` calls: a fourth or fifth exit (#171 adds one) has to remember a fifth call; the bare
call answers a listening caller `200` (§3.4); and it throws on Windows after the listener closes (§3.5).

Why a flag and not an unconditional `Abort()` in the `finally`: `Abort()` after `Close()` does nothing on both platforms
today (§2.3), but that is the BCL's behaviour on one version (the issue itself says "checked on .NET 10.0.12"), and on the
managed listener a closed keep-alive response's connection already serves the next request. The flag never touches a
closed response, and a `Close()` that threw (Windows 1229) leaves it unset, so that response is still aborted.

Behaviour changes for the changelog: on Linux and macOS a caller that hangs up no longer holds a connection until the
tap is disposed; a half-closed caller is answered `400 Bad Request` at once (http.sys's answer on Windows) where it heard
nothing; on macOS a request whose caller hangs up mid-body is no longer counted in `ForwardFailures`, logged as
`proxy-tap error` or answered with a 502 attempt. Cost on the success path: one small object per exchange.

### 4.2 R2: `OtlpTap` reads a hang-up as a hang-up (a patch; found on the way)

1. **`ForwardAsync`** keeps only `SendAsync` and `ReadAsByteArrayAsync` in the `try` that counts `ForwardFailures` and
   answers 502; the answer write moves after it, where a failure is the exporter's and falls to `ServeAsync`'s existing
   hang-up catch (L298-301), silent and uncounted.
2. A `TaskCanceledException` while the tap's own token is cancelled is the tap stopping, not a forward failure: it ends
   the connection without a count or a 502 (the same split #171 makes for `ProxyTap`, §7).
3. **`ReadChunkedAsync`** returns "incomplete" when the stream ends before the zero-size chunk, or a size line is not hex,
   and `ReadAsync` returns `null` for it, as the Content-Length path already does (L101). The export is not counted, not
   queued and not answered; the connection closes (Q5 asks whether a broken size line should get a 400 instead).
4. **Doc comment** of `OtlpTap.ForwardFailures` (L94) and the diagnostic's wording (L151) stay true once 1 and 2 hold;
   no change.

### 4.3 How R1 meets the sibling plans

- **#171** (`PROXY_TAP_FAILED_FORWARD_PLAN.md`) puts a catch around the forward (L282-296) that records a failed forward,
  and takes everything thrown on the capture side (an `IdentityResolver`, a sink or `TryDecodeBody` that throws, all
  counted as forward failures today and measured in its harness), the forward cut short by the tap's own disposal (L257:
  the deadline is linked to the lifetime token, so a disposal mid-forward counts today), and the 502 body. Its 502 goes
  through `TryRespondBadGateway`, so it sets `Closed` like any other answer, and R1's `finally` aborts whatever its new
  exits leave. It builds on R1.
- **#170** changes L207-254 and `ProxyTapOptions`; no line in common with R1. It also takes the DI defect in §8.

### 4.4 Not in this plan

- Time limits on the caller's side, for both taps (§3.9, Q2).
- The macOS accept crash (§1, item 12), a .NET defect.
- The managed listener reads a **chunked** request whose caller half-closes mid-body as complete (SOURCE
  M/ChunkedInputStream.cs:122-127, the family of dotnet/runtime#39183), so `ProxyTap` would forward it cut short. From
  the source only, not measured: Q6.
- The test stubs' own `Abort()` hygiene (§2.4), except where R1's facts use them.

## 5. Tests, red first

`tests/Kronikol.Tests.ProxyTap` (internals visible) for R1, `tests/Kronikol.Tests.Otlp` for R2. "Managed" facts need
the managed listener and read its tables by reflection (`_listenerContexts`, `_connections`, the names `probe.cs`
lists); they fail with a named message if a .NET version renames them. On Windows they skip with the reason the
owner's rule asks for: the condition cannot occur there (http.sys owns the connection and drops it itself, §2.2), the
code under test is the same, and Linux CI runs them. A caller resets only once the tap holds its request (`_inflight`),
never before the accept (§1, item 12).

| Fact | Platforms | Red before the fix | Proves |
|---|---|---|---|
| R1-1 `A_caller_that_resets_while_the_answer_is_written_leaves_no_connection_held` | managed | Linux | path A released; the exchange is still recorded and counted handled |
| R1-2 `A_caller_that_resets_mid_body_leaves_no_connection_held_and_is_no_forward_failure` | managed | Linux | path B released; `ForwardFailures` 0, no `proxy-tap error`, the upstream saw nothing |
| R1-3 `A_caller_that_half_closes_mid_body_is_answered_400_and_let_go` | all | Linux (it hears nothing); Windows passes before and after (http.sys) | path C; never `200` |
| R1-4 `An_upstream_that_fails_after_its_caller_hung_up_is_a_forward_failure_and_leaves_no_connection_held` | managed | Linux | path D; the count stays |
| R1-5 `A_body_read_that_throws_is_the_callers_failure_whatever_the_exception` | all | all | theory over `IOException` (macOS), `HttpListenerException`, `ObjectDisposedException`, `InvalidOperationException`, thrown by the request stream through a test seam on the read (an internal `Func<Stream, Stream>?` wrapping `request.InputStream`, null in production); the only fact that covers macOS's type off macOS |
| R1-6 `Ending_an_unclosed_response_never_throws` | all | Windows (after `HttpListener.Close()`) | `CallerAnswer.EndUnclosed()` after `Close()`, after `Abort()`, twice, after the listener closed, after a failed write |
| R1-7 `An_upstream_body_cut_short_stays_a_forward_failure` | all | none (a guard) | an upstream that sends half its Content-Length and resets: `ForwardFailures` 1 and a 502; kills a type-keyed fix |
| R1-8 `Keep_alive_requests_are_answered_on_one_connection` | all | none (a guard) | 20 requests on one connection after the change |
| R2-1 `An_exporter_that_hangs_up_before_its_answer_is_no_forward_failure` | all | Linux | O1: the collector got it, `ForwardFailures` 0, no `forward failed` line |
| R2-2 `A_forward_cut_short_by_disposal_is_no_forward_failure` | all | all | a collector held open, the tap disposed mid-forward |
| R2-3 `A_chunked_export_cut_short_is_not_received` (theory: no zero-size chunk; a size line that is not hex) | all | all | O2: no `200`, `RequestsReceived` 0, nothing queued |
| R2-4 `A_complete_chunked_export_is_received_and_answered` | all | none (a guard) | the reader still takes a whole chunked body |

Proofs, as the recent plans give them: each fact red on `34145c44` (Linux container for the managed ones, recipe in the
harness README) and green on the fix; and these mutations killed: drop `EndUnclosed()` from the `finally` (R1-1, -2,
-3, -4); drop the status before `Abort()` (R1-3: the caller gets `200`); replace the body read's catch with a type filter
in `GuardedHandleAsync` (R1-5); drop the guard around `Abort()` (R1-6 on Windows); move the answer write back into
`ForwardAsync`'s `try` (R2-1); return the partial chunked body again (R2-3). The one change no fact can tell apart,
`Abort()` without the flag, is harmless on .NET 10 (§4.1) and is named as such.

Then the full ProxyTap and Otlp projects on Windows and in the Linux container, the core suite after the version bump,
and `release.slnf` in Release for every target.

## 6. Releases and records

### 6.1 Releases

R1 is the next free patch when it is ready (4.18.2 went to the #136 audit on 2026-10-13; the number is claimed with
"taking X now" at push, §7). R2 is the patch after it, or rides with it (Q3). Both are patches: bug fixes, nothing new
for a consumer to call (`CallerAnswer` is internal); the behaviour changes are listed in the changelog, not in the bump.

### 6.2 Changelog draft (R1)

> **Fixed.** `ProxyTap` no longer holds the connection of a caller that hangs up (#172). On Linux and macOS the
> `HttpListener` keeps a connection until its response is closed or aborted, and the tap did neither when it gave up on
> a response: a caller that hung up while its answer was written, mid-body, or after a forward failed left its connection
> open until the tap was disposed (10 of 10 on each path, measured). Every response the tap does not answer is now
> aborted, with a failure status set first, since the managed listener's `Abort()` writes an unsent head and would
> otherwise answer `200 OK`. A caller that half-closes mid-body is answered `400 Bad Request` at once, as http.sys does
> on Windows, where it heard nothing before. A request whose caller hangs up before its body arrives is no longer
> counted in `ForwardFailures` or logged as `proxy-tap error` (on macOS it was, since the read throws an `IOException`
> there); whether the read failed decides, not the exception type, which differs on each platform. Windows was not
> affected. A patch: a bug fix, nothing new for a consumer to call.

### 6.3 Wiki (`../Kronikol.wiki` at `origin/master` `85abe19`; edit in a worktree of its own after publication)

- `Integration-ProxyTap-Extension.md`: a short paragraph after L80 (disposal) or in Limitations (L174-178) on what the tap
  does when a caller hangs up; today the page says nothing about it, or about `ForwardFailures`.
- `Diagnostics-and-Debugging.md` L154, L158 (`ProxyTap.Diagnostics()`): the forward-failure line counts requests the tap
  could not forward; a caller that hangs up is not one.
- R2: `Integration-Otlp-Extension.md` L143 (`ForwardFailures`): an exporter that hangs up is not counted, and a chunked
  export cut short is not received.

### 6.4 Plans, roadmap, Kronikol4J

- `PLANS_STATUS.md`: this plan's row, updated per release.
- No `ROADMAP.md` row unless the owner wants one (Q7).
- Kronikol4J's divergence ledger (`docs/REMAINING_PARITY.md`, "divergence ledger" section): one line per release,
  "Nothing to port: the port has no tap", in the format of its last entries.

## 7. Order against the sibling plans

Agreed by message on 2026-10-13 between this session (kronikol-a5), kronikol-70 (#171) and kronikol-ef (#170): a release
from #171's plan may go first if the owner takes it; then R1 (#172); then #171, which builds on R1's `CallerAnswer`; then
#170 (a minor, a new option). Version numbers are claimed with "taking X now" just before each push. R2 touches only
`Kronikol.Extensions.Otlp` and can go at any point after R1.

## 8. Found on the way, not in this plan

- **Handed to #171:** the capture-side throws counted as forward failures, the disposal mid-forward counted as one, the
  forward's spans never stopped on a failure, the 502 body that is not JSON when the message holds a line break.
- **Handed to #170:** `ProxyTapServiceCollectionExtensions.Copy` (L42-75) copies every option but `InFlightRegistry`
  (`ProxyTapOptions` L125), so a tap registered through the list overload never publishes in-flight identities; that
  overload has no fact. #170's plan fixes it with a fact that every option is copied.
- **`TcpTap`:** an upstream connect timeout lands in the "shutting down" catch (no log, no count) and a refused connect is
  only logged, never in `Diagnostics()` (TcpTap.cs:247-253, READ). Q7.
- **`OtlpTap`:** a stalled exporter holds its handler, a 16 KiB buffer (HttpRequestReader.cs:38) and a body array sized to
  its Content-Length up front (L150), up to `MaxRequestBytes` (32 MiB), until disposal. Part of Q2.

## 9. Questions, with recommendations

- **Q1.** Should a request whose caller hung up before its body arrived be drawn? *Recommendation: no.* Nothing reached
  the service; #171 draws failed forwards, which did.
- **Q2.** Time limits on the caller's side (the body read, the answer write) for both taps? *Recommendation: a separate
  issue.* It is a behaviour change with its own defaults to choose; on the managed listener it has to be `Abort()` on a
  timer, since a token does not interrupt body I/O already running (§2.3).
- **Q3.** R2 in R1's release or its own? *Recommendation: its own patch right after R1*: another package and wiki page,
  and #171 waits on R1 alone.
- **Q4.** A macOS lane for the tap tests, the only platform where the read throws `IOException`? *Recommendation: no.*
  R1-5 covers the type on every platform; a lane would add the accept-crash hazard (§1, item 12) for no new coverage.
- **Q5.** `OtlpTap`: answer `400` to a size line that is not hex, rather than closing? *Recommendation: close, as the
  Content-Length path does today*, and revisit with Q2.
- **Q6.** Measure the managed listener's cut-short chunked request (§4.4) and report it to dotnet/runtime if `ProxyTap`
  forwards it truncated? *Recommendation: yes, during R1's work*: a probe on the harness pattern.
- **Q7.** File `TcpTap`'s uncounted connect failures as an issue, and place this plan in `ROADMAP.md`? *Recommendation:
  file the issue; no roadmap row* (two patches).

## 10. Assumption ledger

| Assumption | How it holds |
|---|---|
| macOS behaves as the issue measured | Not re-run (no macOS here); the managed source is shared with Linux, and R1 does not depend on which type is thrown |
| The BCL's private table names hold across .NET versions | The managed facts name the field they could not find and fail, never pass silently |
| Windows' result is not luck | 0 active requests on every path, run twice on 4.18.1 and once on the prototype; SOURCE: a failed write already cancels the request (W/HttpResponseStream.Windows.cs:136, 213, 356) |
| R1's prototype is the design | It is a sketch for measurement; the release follows §5 red first and may name things differently |

## 11. Log

- 2026-10-13: plan written (session kronikol-a5), with the harness and the prototype patch; the order in §7 agreed with
  kronikol-70 and kronikol-ef. Nothing implemented.

## Appendix A. Edit sites at `34145c44`

| File | Lines | R | Change |
|---|---|---|---|
| `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs` | 150-171 | R1 | `CallerAnswer` created, passed down, `EndUnclosed()` in the `finally` |
| same | 173, 181-187 | R1 | `HandleAsync` takes the `CallerAnswer`; the body read's own catch (status 400, return) |
| same | 307 | R1 | `RespondAsync(answer, …)` |
| same | 526-559, 561-577 | R1 | both take the `CallerAnswer` and call its `Close()` |
| same | new, after 577 | R1 | `internal sealed class CallerAnswer` |
| same | 72 | R1 | `ForwardFailures` doc comment |
| `src/Kronikol.Extensions.Otlp/OtlpTap.cs` | 389-421 | R2 | the answer write out of the forward's `try`; the tap's own cancellation not counted |
| `src/Kronikol.Extensions.Otlp/HttpRequestReader.cs` | 86, 165-177 | R2 | a chunked body cut short or with a broken size line is incomplete (`null`) |
| `tests/Kronikol.Tests.ProxyTap/` | new class | R1 | §5 R1-1 to R1-8 |
| `tests/Kronikol.Tests.Otlp/` | new facts | R2 | §5 R2-1 to R2-4 |
