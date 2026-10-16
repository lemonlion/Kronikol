# OtlpTap caller time limits: audit C

Scope: `OtlpTap`'s time limits as released in 4.25.0 (`946e36e8`, diff `5607b776..946e36e8`) and the `OtlpTap` parts of
4.22.2 (`9b7886d9`, diff `2ffd3f23..9b7886d9`), against `plans/TAP_CALLER_TIME_LIMITS_PLAN.md` R0 (§4.6 items 3, 4, 9,
10, 11) and R2 (§4.4). Read in `C:/Code/Kronikol-173-audit`, detached at `09369515` (origin/main: 4.26.1 and its plan
log). `OtlpTap.cs`, `HttpRequestReader.cs`, `OtlpTapOptions.cs` and `OtlpTapServiceCollectionExtensions.cs` are unchanged
since `946e36e8`; `StallWatch.cs` gained `WatchWhen` and `EvenUnderDebugger` in 4.26.0 (`a3b20f99`), which `OtlpTap` does
not use, and was read at HEAD. Read-only: nothing was built or run, so every finding rests on the code, the BCL and
kernel behaviour it relies on, and the facts in `tests/Kronikol.Tests.Otlp/`. The wiki is outside the folder this audit
may read, so its text was not checked.

## Summary

| Id | Where | Defect | Severity | Confidence | Covered by a test |
|---|---|---|---|---|---|
| C1 | OtlpTap.cs:342, 846-853, 368 | `HeadStarted` drops `Next(Sending)`'s false; a request that arrives as `KeepAliveTimeout` fires is counted, queued and mapped, then closed unanswered | low | confirmed by reading | no |
| C2 | OtlpTap.cs:353-356, 439-460 | `Refusal()` counts the 401/413 before the head's swap is settled; one request is counted as refused and as a send timeout, and hears 408 | low | confirmed by reading | no |
| C3 | StallWatch.cs:51-52 | `ExecutionContext.SuppressFlow()` throws when flow is already suppressed, so `StartAsync` throws from such a caller (all three taps) | low | confirmed by reading (BCL contract) | no |
| C4 | OtlpTap.cs:222-223, 240 | the watch is made before `Bind()`; a start that then fails leaves a rooted 1 s heartbeat (and the tap) alive unless disposed, which DI registration never does | low | confirmed by reading | no |
| C5 | HttpRequestReader.cs:134 | repeated headers are joined quadratically before the secret is checked: one 64 KiB head allocates about 0.54 GB | medium (LAN bind), low (loopback) | confirmed by reading, cost computed | no |
| C6 | HttpRequestReader.cs:281-285, 114-117 | a CRLF before a request line starts the stall phase (408 and a count at 100 s, not a quiet close at 120 s), and the request after it is dropped | low | confirmed by reading | no |
| C7 | OtlpTap.cs:652-656; CHANGELOG.md:112; OtlpTapStallTests.cs:345-360 | the answer side does not honour "moves bytes once a limit"; the steady-reader guard reads 1 MiB per step, so it cannot check the 16 KiB per limit the plan states, and on Linux the real rate is likely much higher | low | 16 KiB part confirmed; Linux part plausible | no |
| C8 | OtlpTap.cs:167-175 | `Diagnostics()`'s summary lists every line but the new time-limit one | low (doc) | confirmed by reading | n/a |
| X1 | OtlpTap.cs:89, 246-247, 315-319 | outside the brief: `ListenHost = "::"` (or any bare IPv6 literal) makes `TracesEndpoint` throw, so `StartAsync` throws after binding and starting its loops | low | confirmed by reading (Uri rules) | no |
| X2 | HttpRequestReader.cs:149-161 | outside the brief: a `Transfer-Encoding` that is not chunked is ignored, so the body is read as none or by `Content-Length` (RFC 9112 §6.3 says 400 and close) | low | confirmed by reading | no |

## C1. A request that arrives as KeepAliveTimeout fires is received, then cut unanswered

Where: OtlpTap.cs:342 (`HeadStarted = () => limits.Next(CutPhase.Sending),`), `ConnectionLimits.End()` at 846-853,
the body's settle at 368; HttpRequestReader.cs:281-285 (`HeadStarted?.Invoke()`).

Defect: `Next()` returns false when the last phase's entry lost its swap, leaving `_entry` null and the connection
cancelled, but `HeadStarted` is an `Action`, so the false is dropped; the next `End()` sees a null entry and returns true
without looking at `_cut`, so the request is handled as if the limit had not fired.

Mechanism, step by step:

1. The kept-alive entry is due. The heartbeat's `Look` moves it to fired (StallWatch.cs:112) and queues the action.
2. The exporter's request, head and body in one segment of at most 16 KiB (any small OTLP export), is returned by the
   pending `FillAsync` before the action's `Cancel` reaches the read (or it completed just before the beat, its
   continuation still queued on the pool).
3. `FillAsync` calls `Progressed`, then `ReadHeadTextAsync` calls `HeadStarted`, which calls `Next(Sending)`. `Next`
   calls `End()`: the exchange takes the fired entry, `TryEnd` loses, `CutBy(KeptAlive)` sets `_cut` and cancels the
   connection, and `Next` returns false without arming a sending entry. The false is discarded.
4. The head and the body are parsed out of the 16 KiB buffer. `ReadExactAsync` calls `FillAsync` only once the buffer is
   empty, so the cancelled token is never observed.
5. L368 `if (!limits.End()) break;`: `_entry` is null, and `End()` returns true for a null entry.
6. `HandleAsync` increments `RequestsReceived`, queues the payload (it is mapped to the sink), and `WriteResponseAsync`
   arms an answer entry (its own `Next(Answering)` result is ignored too, L649) and writes on the cancelled token:
   `OperationCanceledException`. The inner catch takes it (`Cut` is `KeptAlive`), `CutAsync(KeptAlive)` writes nothing,
   and the `using` closes the connection.

Outcome: the export is counted and mapped while its exporter hears an end of stream (or a reset). Both the option's
promise ("writing nothing and counting nothing", OtlpTapOptions.cs:84-85) and the release's ("a transfer that completes
as the limit fires is received or cut, never both", CHANGELOG 4.25.0) fail. With `ForwardBaseUri` the forward runs on the
cancelled token: `SendAsync` throws `TaskCanceledException`, ForwardAsync's filter (`!ct.IsCancellationRequested`, `ct`
being the connection's token) lets it pass, so nothing counts it, and the collector never sees an export the tap has
already mapped. An exporter that retries a failed export (OkHttp's connection retry, or an OTel SDK's own retry) is
then mapped twice. An unauthenticated request in the same race is counted in `UnauthenticatedRequests` with no 401
written.

If the action's thread wins the compare-exchange on `_cut` and its `Cancel` has not finished, the `200` can still go out,
and the connection then closes at its next read; that outcome is harmless. The usual order, the connection's thread
winning and cancelling synchronously, gives the outcome above.

Likelihood: the window runs from the heartbeat's move to fired until the cancel reaches the pending read (pool
scheduling, longer under load), once per connection that reaches `KeepAliveTimeout`, and the request must arrive whole in
one read. Rare: an exporter whose next export lands at the 120 s mark. Severity low.

Refutation tried: the loop's own `limits.Next(CutPhase.KeptAlive)` checks its result (L346), but `HeadStarted` is the
transition that ends the kept-alive phase and the only one whose loss matters there. The cancelled token would stop a
read, but no read is made: the request is already buffered.

Tests: `A_transfer_completing_as_the_limit_fires_is_received_or_cut_never_both` and
`A_transfer_ending_after_the_limit_fired_is_cut` take `"export"` and `"answer"` only; no fact sends a request into a
kept-alive entry that has fired. A red fact with the existing seams: hold the action with `Watch.BeforeAct`, wait for the
kept-alive entry, advance past `KeepAlive`, wait for `HasFired`, send `WholeExport()` in one send, and assert
`RequestsReceived` 0 and the sink empty (today 1, with the spans mapped), and that nothing was written.

Fix: let `End()` report a cut that has already happened (return `Cut == CutPhase.None` when the entry is null), and have
`HeadStarted` throw `OperationCanceledException` when `Next` returns false, so the reader stops at the first byte as the
limit's.

## C2. A head that completes as the stall limit fires is counted as refused and as a send timeout

Where: OtlpTap.cs:353-356 (`if (Refusal(head) is { } refusal) { if (!limits.End()) break; ...`), `Refusal()` at 439-460.

Defect: `Refusal(head)` increments `UnauthenticatedRequests` (401) or `PayloadsRejected` (a `Content-Length` past
`int`, 413) and logs, before `limits.End()` settles the head's swap; when the swap was lost the code breaks to
`CutAsync(Sending)`, which counts a send timeout, logs again and answers `408`.

Scenario: an exporter without the secret, or one declaring `Content-Length: 3000000000`, stalls inside its head for
`CallerStallTimeout` and sends the rest just as the heartbeat fires (or the host's `Log`, called from `Refusal`, blocks
across the limit). `Diagnostics()` then says "1 export request(s) rejected with 401" (or "refused with 413 Payload Too
Large") and "1 exporter connection(s) stalled ... 1 sending an export"; the log has both lines; the exporter heard `408`,
not the `401` or `413` that was counted. An invalid `Content-Length` in the same race logs "refused ... not a number" and
hears `408`.

Severity low: a double count and a misleading line, in a rare race. Confirmed by reading. Not covered: the race facts
send authenticated, well-framed requests.

Red fact: `ExpectedHeaders` set; send `"POST /v1/traces HTTP/1.1\r\nHost: x\r\n"`; `StalledAsync(SendingPhase)`; hold
`BeforeAct`; advance past the limit; wait for `HasFired`; send `"\r\n"`; assert `UnauthenticatedRequests` 0 and
`CallerTimeouts` 1 (today 1 and 1).

Fix: settle the swap first (`if (!limits.End()) break;` before the refusal is counted), or make `Refusal` pure and count
after `End()` has won.

## C3. The shared watch cannot be made while flow is suppressed, so StartAsync throws

Where: StallWatch.cs:51-52 (`using (ExecutionContext.SuppressFlow()) _heartbeat = clock.CreateTimer(...)`), reached
from OtlpTap.cs:223, and from ProxyTap.cs:267 and TcpTap.cs:220.

Defect: `ExecutionContext.SuppressFlow()` throws `InvalidOperationException` when flow is already suppressed on the
calling thread (its documented contract), so a tap started from such code cannot start.

Scenario: a fixture starts its host without flowing the test's `AsyncLocal`s into it, the very problem the watch's own
`SuppressFlow` solves: `using (ExecutionContext.SuppressFlow()) host.Start();`. `Host.StartAsync` runs the hosted
services on that thread until something yields, and `OtlpTapHostedService.StartAsync` calls the synchronous
`OtlpTap.StartAsync`, so `new StallWatch` throws and the host fails to start. The same start worked before 4.25.0 (before
4.23.0 for `ProxyTap`, 4.26.0 for `TcpTap`). `ProxyTap` makes its watch after `_listener.Start()`, so there the throw
also leaves the listener open.

Severity low: a loud failure at start, from an uncommon calling context; `null` for both limits avoids it. Confirmed by
reading. Not covered.

Fix: create the timer directly when `ExecutionContext.IsFlowSuppressed()` is already true, and suppress only otherwise.

## C4. A start that fails after the watch is made leaves its heartbeat running for the life of the process

Where: OtlpTap.cs:222-223 makes the watch before `Bind()` (240); only `DisposeAsync` (765) disposes it.

Defect: `TimeProvider.System.CreateTimer` returns a timer that stays rooted while it is scheduled (a `TimerQueueTimer`,
with no finalizer to stop it), so a watch made by a `StartAsync` that then throws keeps ticking once a second and, through
its log delegate, holds the tap, its options and its sink, until `DisposeAsync`.

Scenario: a fixed `ListenPort` that is in use: `Bind()` throws `SocketException` out of `StartAsync`. Under `await using`
the dispose closes it. Under `AddOtlpTapTestTracking` the tap is registered as an instance, which the container never
disposes, and a host whose start failed is not stopped, so the timer and the tap live until the process exits. Before
4.25.0 the same failure left only an unused `HttpClient` behind.

Severity low. Confirmed by reading. Not covered.

Fix: make the watch after `Bind()`, or dispose it in a catch around the rest of `StartAsync`.

## C5. A head without the secret can make the reader allocate about half a gigabyte

Where: HttpRequestReader.cs:123-135, the join at 134
(`headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;`), which runs in
`ReadHeadAsync`, before `Refusal` checks `ExpectedHeaders`.

Defect: each repeat of a header name copies the whole joined value so far, so N repeats cost O(N^2) characters. Inside
`MaxHeaderBytes` (64 KiB), a head of a request line and about 16,380 lines `a:` (4 bytes each with the CRLF) allocates the
sum of 2k characters for k up to 16,380: about 268 million characters, 0.54 GB of transient strings, each under the
85 KB large-object threshold (so many gen0 collections, each pausing every thread in the test process), and as much
copying, on the order of 0.1 s of CPU per 64 KiB received.

Scenario: on a non-loopback bind, where the docs ask for `ExpectedHeaders`, a caller without the secret sends such heads
on many connections; each costs it 64 KiB and the tap about a tenth of a core-second plus the collections. R0 moved the
secret check ahead of the body so that such a caller makes the tap do nothing large (its changelog calls the memory case a
security fix); the head parse still does this ahead of the check. Pre-existing: the join predates R0 and R2.

Severity medium for a LAN bind (about 8,000 bytes allocated per byte received), low on loopback. Confirmed by reading;
the cost is computed, not measured. Not covered.

Fix: collect repeated values in a list and join once, or cap the header lines (Kestrel allows 100 by default) or the
repeats of one name.

## C6. A CRLF before a request line starts the stall phase, and the request after it is dropped

Where: HttpRequestReader.cs:281-285 (`HeadStarted` on any first byte), 114-117 (an empty first line returns null).

Defect: the reader takes any first byte, a CR included, as a head's start, and does not skip empty lines before the
request line, which RFC 9112 §2.2 asks a server to do for at least one.

Scenario: a client that sends a CRLF after a POST body and then idles: the CRLF fires `HeadStarted`, so the idle wait is
timed by `CallerStallTimeout` and ends in a `408`, counted in `CallerTimeouts` and logged "stalled ... sending an export",
where an idle connection is closed quietly at `KeepAliveTimeout`. If it sends its next request instead, the head's first
line is empty, `ReadHeadAsync` returns null, and the connection closes with that request unanswered (the drop is
pre-existing; the misclassification is R2's).

Severity low: no current OTLP exporter sends a stray CRLF. Confirmed by reading. Not covered.

Fix: skip CRLFs before the request line, and fire `HeadStarted` on its first byte.

## C7. The answer side does not honour "moves bytes once a limit", and the guard cannot see it

Where: OtlpTap.cs:652-656 (progress only when a 16 KiB `WriteAsync` completes); CHANGELOG.md:112 ("an exporter that
moves bytes at least once a limit is never cut"); OtlpTapStallTests.cs:345-360 (the steady reader takes 1 MiB per 0.6 s
step against a 1 s limit).

Defect: for an answer, progress is a completed 16 KiB write, which needs the exporter to free at least 16 KiB of window;
an exporter reading a few bytes per limit is cut, against the changelog's sentence (the plan's §4.1 item 1 says it right:
16 KiB per limit). On Linux a writer blocked on a full send queue is woken only once a third of the send buffer is free
(`sk_stream_write_space`, `sk_stream_min_wspace`), and the send buffer autotunes into megabytes on loopback, so the real
requirement is likely hundreds of kilobytes per limit (plausible, not run; R3 met the same kind of Linux surprise for
`TcpTap`). The steady guard reads about 1.7 MB per limit, so it cannot tell 16 KiB per limit from a megabyte.

Severity low: collector answers are small and exporters read them at once. Confidence: the 16 KiB part confirmed by
reading, the Linux threshold plausible. Not covered.

Fix: correct the changelog and wiki sentence for the answer direction, and have the guard read 32 KiB per step on Linux
to learn the real rate.

## C8. Diagnostics()'s summary leaves out the time-limit line

Where: OtlpTap.cs:167-175 ("one `CaptureDegraded` entry per non-zero problem counter: accepts that failed, payloads
dropped, rejected or failed, exports refused as unauthenticated, forwards that failed"). 4.25.0 added the
`CallerTimeouts` line at 209-213 without naming it there. Low, documentation. Confirmed by reading.

## Outside the brief (found on the way)

- **X1.** `ResolveBindAddress` takes `"::"` (and parses `"::1"` or any IPv6 literal), but `DisplayHost()` (L315-319) passes
  it through unbracketed, so `TracesEndpoint` (L89) builds `http://:::port/v1/traces`, which `Uri` refuses
  (`UriFormatException`). `StartAsync` builds its "listening on" line from `TracesEndpoint` after `Bind()` and after the
  accept and map loops have started (L244-247); the interpolation runs before `TryLog`'s guard, so `StartAsync` throws
  with the listener open and serving, and `TracesEndpoint` throws for every caller. `"[::]"` works. Low; only the
  resolver is tested (`OtlpTapTests.cs:406`), no tap starts on an IPv6 host. Fix: bracket an IPv6 host in `DisplayHost`.
- **X2.** `Transfer-Encoding` is consulted only for `chunked` (L149-161). `Transfer-Encoding: gzip` with no
  `Content-Length` is read as no body, answered `200` and counted as received empty, and its body is then read as the next
  request's head, the out-of-step class R0 item 4 fixed for `Content-Length`; RFC 9112 §6.3 says 400 and close when
  chunked is not the final coding. Low: no exporter sends it.

## Checked and found sound

- The inner catch's filter (OtlpTap.cs:378-379) parses as `(ex is A or B or C or D) && Cut != None && !ct...`; every other
  exception reaches the outer catches, so nothing escapes `ServeAsync`, and `finally { limits.End(); }` ends every entry
  on every path (early returns, exceptions, the stop). `An_exchange_leaves_no_entry_behind` covers the common paths.
- One count per connection: `CutAsync` runs once, after the loop. An over-cap drain that ends as the limit fires is
  settled by `End()` before `HandleAsync`, so it is a `408` and never also a `413` (no double count, unlike C2).
- A body or an answer that ends after its limit fired is the limit's (L368, L659-660), and one that ends first is
  received; the action only cancels, and `CutBy` takes `_cut` once. A stale action cannot outlive its phase: a phase moves
  on only after its `TryEnd` won, which makes the heartbeat's `TryFire` lose.
- Pipelined and back-to-back requests: `HeadStarted` fires on buffered bytes with no await between `Next(KeptAlive)` and
  it, so a pipelined head never spends time in the kept-alive phase; the forward has no entry (R2-8).
- The 408: counted before it is written, bounded by its own 5 s token on the real clock, then a staged close (5 s quiet,
  30 s total, on the tap's token). A peer that resets mid-cut fails the write into the outer catch, counted once.
- Disposal: the heartbeat stops first; an action queued before it does nothing (`Act` checks the flag); a transfer whose
  entry fired then cuts itself, and is counted only if the tap's token is not yet cancelled.
- Linked token sources (`connection`, `bound`, `all`, `idle`) are all disposed; `CutBy` swallows a cancel on a disposed
  source. `connection.Cancel()` can run the read's continuation inline on Linux, which only runs the cut early.
- Options: zero and negative values are refused at registration and at start, naming the option; `null`,
  `Timeout.InfiniteTimeSpan` and `TimeSpan.MaxValue` mean none (the watch compares elapsed time, so no overflow); `Copy`
  carries both, and the reflection fact checks every public option.
- R0's secret check: header names are case-insensitive on both sides; a repeated header is joined, so it can never
  equal an exact secret; trailers and bodies are never consulted; the check runs on the head, before any body byte is
  read or allocated, and the 401's linger reads into the reader's existing 16 KiB buffer. `ReadExactAsync` grows a body
  as it arrives, for `Content-Length` bodies and chunks alike.
- `Content-Length` is digits alone (Unicode digits refused); past `int` is a `413` counted in `PayloadsRejected`; both
  framings close the connection; an over-cap drain cut short counts nothing.
- Every per-connection, map-loop, start and stop line goes through `TryLog`, as does the watch's own log delegate.
- Counters are read with `Interlocked`; `Diagnostics()` derives its total and split from the same two reads.
- The debugger seam is read once per beat and fed only to the watch; every fact sets it to "not attached".
