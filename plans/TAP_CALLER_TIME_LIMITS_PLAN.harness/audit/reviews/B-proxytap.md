# B: ProxyTap caller time limits, audit (read-only)

Worktree `C:/Code/Kronikol-173-audit`, detached at `09369515` (origin/main, 4.26.1 + plan log). Scope: R1 (4.23.0,
`609ba8f3..c7eef4b3`), its fix 4.26.1 (`be6384a9`), and ProxyTap's part of R0 (4.22.2, `2ffd3f23..9b7886d9`). Files
read in full: `src/Kronikol.Extensions.ProxyTap/{ProxyTap,ProxyTapOptions,ProxyTapServiceCollectionExtensions,InFlightIdentityRegistry,ForwardFailure}.cs`,
`src/Kronikol.Extensions.TcpTap/StallWatch.cs`, `tests/Kronikol.Tests.ProxyTap/{ProxyTapStallTests,StallWatchTests,ProxyTapStoppingTests,ProxyTapHangUpTests,ProxyTapOptionRangeTests,ProxyTapRegistrationTests,RawCaller}.cs`,
the plan's R0/R1 sections and the harness's `source/dotnet-listener-timers.md` §4. Nothing was built or run.

Line numbers are at `09369515`.

## Summary

| Id | Where | Defect | Severity | Confidence | Covered by a test |
|---|---|---|---|---|---|
| B1 | StallWatch.cs:51 | `ExecutionContext.SuppressFlow()` throws when flow is already suppressed, so `StartAsync` throws (ProxyTap leaves its listener started) | medium | confirmed by reading | no |
| B2 | ProxyTap.cs:308, 1302-1320 | an accepted context whose handler has not started is untracked; `_listener.Close()` answers it `200 OK` on Linux/macOS | low | plausible | no |
| B3 | ProxyTap.cs:319-320, 1041, 1302-1303 | the 503 refuse runs in the `Reading` phase, so `EndWhileReading(503)` can abort it mid-write (a 503 head with no body) | low | plausible | no |
| B4 | ProxyTap.cs:1343 | the stop line names no failure counter; `CallerTimeouts` is missing where OtlpTap's and TcpTap's lines name theirs | low | confirmed | no |
| B5 | ProxyTapStallTests.cs:86-88, 149-151 (product ProxyTap.cs:397-404) | R1-1/R1-3 assert the cut's log line without waiting; the line is written after the abort, the same interleaving that failed 4.26.0's CI | low (test flake) | confirmed by reading | it is the test |
| B6 | ProxyTapStallTests.cs:296-317 | 4.26.1's fact tests `TimeOut`'s ordering, not that `CutStalled` counts inside it; reverting `CutStalled` to the 4.26.0 shape passes every fact on an idle machine | low (coverage) | confirmed | it is the gap |
| B7 | ProxyTap.cs:1007, 1041 | a HEAD answer goes out with `Content-Length: 0` whatever the service said (older than R1, carried into the 4.23.0 writer) | low | confirmed by reading | no |

No double count, missed count, "answered and cut", leaked entry or leaked identity was found in R1's state machine (see
"Checked and refuted" at the end).

---

## B1. The watch's constructor throws when the caller's execution-context flow is already suppressed

- **Where:** `src/Kronikol.Extensions.TcpTap/StallWatch.cs:51-52`
  (`using (ExecutionContext.SuppressFlow()) _heartbeat = clock.CreateTimer(...)`). Reached from
  `ProxyTap.StartAsync`, `ProxyTap.cs:266-267`, after `_listener.Start()` at 264. The same file is compiled into
  OtlpTap (`OtlpTap.cs:222-223`, since 4.25.0) and TcpTap (`TcpTap.cs:219-220`, since 4.26.0).
- **Defect:** `ExecutionContext.SuppressFlow()` throws `InvalidOperationException` ("the context flow is already
  suppressed") when the current thread's flow is already suppressed. The constructor does not check
  `ExecutionContext.IsFlowSuppressed()`. When flow is already suppressed the timer captures no context anyway, so there
  is nothing left to do.
- **Failure scenario:** a fixture starts its host without letting the test's AsyncLocals flow into it, the usual idiom
  being:
  ```csharp
  Task started;
  using (ExecutionContext.SuppressFlow())
      started = host.StartAsync();   // ProxyTapHostedService.StartAsync -> ProxyTap.StartAsync runs synchronously here
  await started;
  ```
  The same happens if the fixture calls `tap.StartAsync()` directly inside the block. With the default
  `CallerStallTimeout` (100 s, so the watch is always made), `StartAsync` throws `InvalidOperationException`. On
  4.22.3 the same call worked. ProxyTap throws after `_listener.Start()`, so its port stays bound with no accept loop,
  `_cts` is null and `_lifetime` is default. A caller that connects has its head read and its context queued, and
  nobody answers it until `DisposeAsync`, which a host whose `StartAsync` failed may never call. OtlpTap and TcpTap
  throw before they bind, so they only fail to start.
- **Severity:** medium. It is a regression that turns a working start into an exception for a legitimate (if
  uncommon) calling pattern, and it is on by default in all three tap packages. **Confidence:** confirmed by reading;
  the throw is the documented BCL contract of `SuppressFlow`.
- **Tests:** none. `StallWatchTests` and `ProxyTapStallTests` always construct on a thread whose flow is not
  suppressed.
- **Fix:** use `var flow = ExecutionContext.IsFlowSuppressed() ? (AsyncFlowControl?)null : ExecutionContext.SuppressFlow();`
  around the timer creation, then `flow?.Dispose()`, or the BCL's `restoreFlow` pattern. In ProxyTap, also consider
  making the watch before `_listener.Start()`, as the other two taps do. Fact: build a `StallWatch`, and start each
  tap, inside a `SuppressFlow()` block.

## B2. `DisposeAsync` closes the listener under a context it has not seen: the caller hears `200 OK` (Linux, macOS)

- **Where:** `ProxyTap.cs:308` (the accept loop hands the context to `Task.Run`; it is tracked only at 314-316, in
  the handler's first lines), against `DisposeAsync` 1302-1320 (settle loops over `_answers`, drain on `_inflight`,
  then `_listener.Close()`).
- **Defect:** a context the accept loop has received, or one the managed listener has registered and queued, is in
  neither `_answers` nor `_inflight` until its handler runs. `DisposeAsync`'s settle loops and its drain do not see
  it. `_listener.Close()` then closes it, and the managed listener writes its unsent head with the default status
  (dotnet/runtime#88196): `HTTP/1.1 200 OK`, empty body.
- **Failure scenario:** on Linux, with nothing in flight, a request's head completes as the fixture disposes the tap
  (a service's background poller, or a client retry). `DisposeAsync` runs synchronously all the way to
  `_listener.Close()`: `CancelAsync` completes inline with no registrations, `_inflight == 0` skips the drain, and both
  settle loops are empty. It gets there before the pool has run the two hops the context still needs (the accept
  loop's continuation, then `Task.Run`). Under thread-pool starvation, which is common in parallel test runs, those
  hops take tens to hundreds of milliseconds. The caller is told `200 OK` for a request that was never forwarded,
  which is the outcome R0 item 2 (`Stopping_the_tap_never_tells_a_caller_its_request_succeeded`) set out to remove.
  The handler then runs, sees `Stopping`, and its 503 write fails silently. On Windows http.sys resets instead.
- **Severity:** low (it needs a request in the teardown window). **Confidence:** plausible: the window is confirmed by
  reading and was not reproduced.
- **Tests:** none. R0's facts cover requests whose handler is already running (four in flight, or one arriving during
  a drain held open by a stalled answer).
- **Fix:** track the context in the accept loop before handing it off (increment `_inflight` and add the answer
  there), so the drain waits for it and the settle loops end it. For contexts the listener has queued but the loop has
  not yet taken, keep taking and refusing (503) for a short time after the drain, before `Close()`.

## B3. A 503 refuse is still "reading", so the stop's first settle loop can abort it mid-write

- **Where:** `ProxyTap.cs:319-320` and 363 (`GuardedHandleAsync` refuses with the answer still in `Reading`), 1041
  (`WriteAndCloseAsync` sets `ContentLength64` before the write is issued), 1302-1303 (`EndWhileReading(503)`), and
  `CallerAnswer.TryEnd` 1225-1275.
- **Defect:** `RefuseWhileStoppingAsync`, called from `GuardedHandleAsync`, writes its 503 without moving the answer
  out of `Reading`. `DisposeAsync`'s first loop treats every `Reading` answer as a request still receiving its body,
  and aborts it.
- **Failure scenario (managed listener):** request A arrives as `DisposeAsync` begins. A's handler adds its answer to
  `_answers`, sees `Stopping` (`_disposed` is already 1) and starts the refuse. `DisposeAsync`'s `_answers.Keys`
  snapshot includes A. If its `EndWhileReading(503)` lands after `ContentLength64 = 70` and before the write is issued,
  `Abort()` writes `HTTP/1.1 503 ... Content-Length: 70, Connection: close` and closes with no body, and A's write is a
  no-op on the closed stream. An `HttpClient` caller gets "the response ended prematurely" instead of a 503. Earlier in
  the window the caller gets an empty but well-formed 503, which is benign. The window is microseconds wide, so this
  is rare.
- **Severity:** low. **Confidence:** plausible.
- **Tests:** none.
- **Fix:** refuse from a phase `EndWhileReading` leaves alone: call `TryBeginForward()` before refusing in
  `GuardedHandleAsync` (HandleAsync's own refuse, at 465-468, already runs in `Forwarding`), or give the refuse a state
  of its own.

## B4. The stop line does not name `CallerTimeouts` (or `ForwardFailures`, or capture faults)

- **Where:** `ProxyTap.cs:1343`, `proxy-tap stopped ({RequestsHandled} forwarded, {RequestsCaptured} captured)`.
- **Defect:** R2 and R3 each gave their counters "a place in the stop line": OtlpTap's line names `forward failures`
  and `caller timeouts` (`OtlpTap.cs:799-803`, pinned by `OtlpTapStallTests.cs:204`), and TcpTap's names
  `half-closed pairs closed` and `stalled writes closed` (`TcpTap.cs:987-991`). R1 gave ProxyTap's line nothing, so the
  one tap whose limit is on by default for HTTP callers says nothing about cuts when it stops.
- **Failure scenario:** three callers are cut during a run. The log ends `proxy-tap stopped (40 forwarded, 40
  captured)`, and a host that reads only its log at shutdown cannot see the cuts. It could for its OtlpTap and TcpTap
  siblings.
- **Severity:** low. **Confidence:** confirmed.
- **Tests:** `ProxyTapLogTests` checks only that the line starts with `proxy-tap stopped (`.
- **Fix:** append `{ForwardFailures} forward failures, {CallerTimeouts} caller timeouts` (and capture faults), and add
  a fact like Otlp's.

## B5. R1-1 and R1-3 assert the cut's log line without waiting for it (the 4.26.0 race, one line later)

- **Where:** tests `ProxyTapStallTests.cs:86-88` (R1-1, all six cases) and 149-151 (R1-3). In the product,
  `ProxyTap.cs:394-404`: `CutStalled` logs only after `TimeOut` returns, that is, after `Abort()` has shut down and
  closed the caller's socket.
- **Defect:** on the limit's pool thread the handler can finish (`InFlight == 0`) before the line is written. Its read
  fails as soon as the abort's shutdown or close reaches it, while the action thread is still in `Abort()`'s
  `Unbind`/`RemoveConnection`, or has been descheduled. The tests read the log right after `InFlight == 0`.
- **Failure scenario:** 4.26.0's CI run 38831706828 hit this interleaving: the action thread had not returned from
  `TimeOut` when the handler had ended, so `CallerTimeouts` read 0. 4.26.1 moved the count into `claimed`, before the
  abort, but left the log line where the count used to be. The same interleaving now passes the `CallerTimeouts`
  assertion and fails `Assert.Contains(log, ... "sending its request body; not forwarded")`. R1-1 is the most exposed,
  since its handler does almost nothing after its read fails, and that includes its real-clock cases. R1-3 is less
  exposed, because its handler decodes an 8 MB body to record it. On the product side, a host that polls its `Log`
  right after the caller is gone can miss the line too, which 4.26.1's notes accept.
- **Severity:** low (a test flake, likely on loaded CI). **Confidence:** confirmed by reading.
- **Fix:** poll for the line (`WaitUntilAsync`), or write the log line inside `claimed` as well, before the abort.

## B6. 4.26.1's fact checks `TimeOut`, not `CutStalled`

- **Where:** `ProxyTapStallTests.A_cut_is_counted_before_the_caller_is_let_go`, `ProxyTapStallTests.cs:296-317`,
  against `ProxyTap.cs:394`.
- **Defect:** the fact calls `CallerAnswer.TimeOut(408, claimed)` directly and proves that `claimed` runs before the
  abort. Nothing deterministic proves that `CutStalled` uses `claimed`. A regression to the 4.26.0 shape (no `claimed`,
  `Interlocked.Increment` after `TimeOut` returns) passes this fact and every other fact on an idle machine. Only R1-1's
  load-dependent race catches it, as it did on CI.
- **Severity:** low (coverage). **Confidence:** confirmed.
- **Fix:** drive `CutStalled` itself through the watch, with a seam that holds the cut just after `Abort()` (or a
  caller-side check made the moment the caller is let go, while the action is held), and assert
  `CallerTimeouts == 1` and the `Diagnostics()` line at that point.

## B7. HEAD answers lose the upstream's `Content-Length` (pre-existing; carried into R1's writer)

- **Where:** `ProxyTap.cs:1007` (`Content-Length` is skipped as hop-by-hop) and 1041
  (`response.ContentLength64 = body.Length`).
- **Defect:** for a HEAD, `HttpClient` returns an empty body, so the tap answers `Content-Length: 0` whatever the
  service sent. The tap is documented as a transparent tee.
- **Failure scenario:** `curl -I`, or a client that sizes a download with HEAD, through the tap gets
  `Content-Length: 0` for a 5 MB resource, while the record keeps the upstream's `Content-Length: 5242880`, so the
  diagram and the caller disagree. 204 and 304 also get `Content-Length: 0`, but that is the managed listener's own
  behaviour (`CanSendResponseBody`), not the tap's.
- **Severity:** low. **Confidence:** the 0 is confirmed by reading. That both listeners accept `ContentLength64 = N`
  with no body for a HEAD (http.sys counts 0 bytes left to write for HEAD) is from memory of the source, so the fix is
  plausible but unverified.
- **Tests:** none; there is no HEAD request in the ProxyTap suite.
- **Fix:** for HEAD, set `ContentLength64` from the upstream's `Content-Length`, and add a fact. This defect predates
  R1. Report it under "found on the way".

---

## Checked and refuted (no defect)

- **One cut per exchange, never "forwarded and cut":** all five `CutStalled` call sites (the body action, the body
  catch, body success after a lost swap, the answer action, the writer's `finally`) count only when
  `CallerAnswer.TryEnd` wins its compare-exchange. The body entry is ended before `TryBeginForward`, and the answer
  entry before `Close()`, so neither can fire while the answer is `Closing`. A read or write that completes after the
  entry fired is the limit's, by the plan's rule. A caller cut after its last piece was fully written is counted as
  cut, which the plan intends.
- **No leaked entries:** every `Watch` reaches `TryEnd` on every path (catch, normal return, `finally`), and fired
  entries are removed in `Look`. `Watch` racing `Dispose` adds an entry that its transfer still removes. The in-flight
  identity is released when `HandleAsync` returns after an answer cut (the `using` scope also covers the record).
- **Pieces:** `PieceLength` is correct at 16,383, 16,384, 16,385, 32,767, 32,768 and 32,769, with a last piece of
  16,384 to 32,767. The body is read in 16 KiB pieces, with progress only when a read returns bytes. An empty answer
  writes nothing and closes.
- **Linux writes after an abort complete without error:** the loop exits on `IsTimedOut`. After a stop's
  `EndWith(503)` the remaining pieces are no-op writes on the closed stream. That is harmless, and the exchange is
  recorded as R0 intends.
- **Limits:** `Validate` refuses zero and negative values except -1 ms. `StallLimit` maps Infinite to null.
  `TimeSpan.MaxValue` is compared as elapsed time, so there is no overflow. `ForwardTimeout`/`ConnectTimeout`
  `Deadline` and `LongestDeadline` keep `HttpClient.Timeout`, `ConnectTimeout` and `CancelAfter` in range.
  `ForwardFailure.Classify`'s "inner `TimeoutException` means connect" can no longer be reached through the client's
  backstop.
- **Registration:** `Copy` includes `CallerStallTimeout`, and the reflection fact covers every option.
- **Counters and wording:** both counters use `Interlocked`, and `Diagnostics()` and the log lines match the plan.
- **Exceptions:** `TryEnd` swallows the status, `KeepAlive` and `Abort` exceptions on both listeners. `Close()`
  restores the phase when it throws, so `EndUnclosed` still aborts. The accept loop ends quietly once `_disposed` is
  set. `Act` and `IsDue` log through the guarded `TryLog`.
- **Debugger seam:** it is read on every beat, and a throwing check counts as "not attached".
- **Disposal:** the heartbeat stops first, and `Act` checks `_disposed`. An action already past that check can still
  cut a genuine stall while the tap stops, which only counts a real stall.
- **Note, not a defect:** with a frozen `FakeTimeProvider`, `StalledAsync`'s "stamp stopped moving" check is
  vacuous, because the stamp cannot move unless the clock does. It reduces to "the entry exists, plus 200 ms", which is
  still sufficient for every stall fact.
