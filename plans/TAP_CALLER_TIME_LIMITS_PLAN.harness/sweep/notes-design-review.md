# Design review: `plans/TAP_CALLER_TIME_LIMITS_PLAN.md` (#173)

Read-only review of the plan, its harness and the source at `3240c983` in `C:/Code/Kronikol-173`. `plan:N` is a line of
the plan; other paths are relative to the worktree unless they name the harness. The ten items the brief lists as
already known are left out. Severity: **bug** (the design as written ships a defect, or a fact as written fails or
proves nothing), **wrong claim**, **gap**, **nit**. Most severe first.

Facts checked by running (`timeout-limits-check.cs` in this folder, output in `timeout-limits-check-windows.txt`, .NET SDK 10.0.300): `TimeSpan.MaxValue + 10 s` throws
`OverflowException`; `HttpClient.Timeout` refuses more than `24.20:31:23.647`; `CancelAfter(60 days)` throws, 40 days
does not; `SocketsHttpHandler.ConnectTimeout` throws for zero and for `TimeSpan.MaxValue`. `FakeTimeProvider` was read
at dotnet/extensions `main`: `Advance` moves `_now` under its lock and calls `WakeWaiters()` after releasing it;
`GetTimestamp()` returns `GetUtcNow().Ticks` through the virtual `GetUtcNow()`, which also calls `WakeWaiters()`;
`WakeWaiters` is gated by a CAS and returns at once if another thread holds the gate. `TimeProvider.CreateTimer`'s
documentation: it captures the `ExecutionContext`, and callbacks "may be invoked simultaneously on two threads".

---

## 1. R0 item 2 answers 503 to exchanges the upstream already has (bug)

**Evidence.**
- plan:382-386: "`DisposeAsync` ends every answer still open with `503 Service Unavailable` before it closes the
  listener".
- `ProxyTap.cs:968-969` cancels `_cts` first. Through the linked deadline (L397-399) that cuts every forward in flight,
  and each handler then records the failure with the 502 body (L452-455), logs `proxy-tap stopping` (L457-460) and
  writes its own 502 (L472). Ending the same answers with 503 straight after the cancel races those 502s: the caller
  hears 502 or 503 by timing, while every record carries the 502 body. The existing fact
  `A_forward_cut_by_disposal_is_recorded_and_not_counted_as_a_forward_failure` (`ProxyTapFailureTests.cs:280`) pins
  the record, not what the caller hears, so nothing catches the mismatch.
- An exchange whose upstream has answered but whose `RespondAsync` (L489) has not begun is still "open". R0 ends it
  with 503; the handler's writes then go to an aborted response (silent no-ops on the managed listener,
  `source/dotnet-listener-timers.md` §4.4; `ObjectDisposedException`, caught, on Windows); L490-503 then count it
  handled and record the upstream's status. The caller is told the request could not be handled, which invites a
  retry, for a request the upstream executed, and the report says 200.
- On Windows `Abort()` sends nothing, so R0 turns the Stopping path's 502 into a reset.
- R0-8 (plan:466) covers only a caller stalled mid-body.

**Fix.** End with 503 only answers whose request body has not been read whole. Leave a forward in flight to the
existing Stopping path (cancel, record, 502) and an answer in hand to finish inside the 2 s drain; abort whatever is
still open after the drain. Say which status each phase gets, and add R0-8 cases: a forward cut by disposal hears the
502 its record names; an answer in hand is delivered whole or cut short, never replaced by 503.

## 2. The end of the body read and the limit's action are check-then-act, not a CAS (bug)

**Evidence.**
- plan:234-237 promises "one state field per transfer, moved by `Interlocked.CompareExchange`; whoever moves it first
  acts, the other does nothing".
- plan:266-271: when the copy returns, "the handler checks the answer's state first". A check is not a move. The
  answer stays `open` through identity, spans and the forward (`ProxyTap.cs:302-426`) until `Close()`. A heartbeat
  that sampled the body entry's progress just before the last read completed can therefore win `TimeOut(408)` after
  the handler has passed its check: the caller hears `408` (managed) or a reset (http.sys) while L426 forwards the
  request and L490-503 record it as handled.
- The answer phase has the mirror race: a stale sample just before the last piece completes lets `TimeOut()` (open to
  timed out) beat `Close()`, aborting a complete answer. On Windows the reset "discards what the caller had not read"
  (`listener/README.md`, "Also found" 3), and `CallerTimeouts` counts a caller that kept up.
- R1-2 (plan:468) drives only the opposite order (limit first, then a clean end of body); R1-7 (plan:474) races
  `TimeOut`, `Close` and `EndUnclosed`, not an entry's end against its firing.
- `OtlpTap`'s per-connection entry has the same shape: a progress write landing after the heartbeat sampled still
  gets the connection cancelled.

**Fix.** Give each entry its own state (active, ended, fired) moved only by CAS. The handler ends the entry (active
to ended) before it checks anything and treats a lost CAS as "timed out"; the heartbeat fires only after winning
active to fired, and only the winner calls `TimeOut`. Add a fact with a seam that holds the action between its
decision and its call, completes the read in that gap, and asserts: forwarded, 200, `CallerTimeouts` 0.

## 3. Durations at the top of the range still break start-up, or every forward (bug)

**Evidence** (run, see the head of this file).
- `ForwardTimeout = TimeSpan.MaxValue`, a common way to say "never", makes `StartAsync` throw `OverflowException` at
  `ProxyTap.cs:192` (`ForwardTimeout + TimeSpan.FromSeconds(10)`). R0 item 1 (plan:374-375) fixes only
  `Timeout.InfiniteTimeSpan`.
- Any `ForwardTimeout` from about 24.86 days less 10 s throws from the `HttpClient.Timeout` setter at L192, naming
  parameter `value`, not the option.
- Past about 49.7 days `CancelAfter` throws ("Parameter 'delay'"). Once L192 is made infinite for large values, L399
  throws per request outside the forward's `try` (L424), and the catch-all counts a forward failure and answers 502,
  exactly the -5 s symptom R0 sets out to remove (plan:371-372).
- `ProxyTapOptions.ConnectTimeout` (L142) is equally unvalidated: `SocketsHttpHandler.ConnectTimeout` (L182) throws
  for zero, negatives and values past `int.MaxValue` ms, from `StartAsync`, naming `value`.
- The four new options are refused only for "zero or another negative value" (plan:250-251, 313-314, 335, 342).
  Converting `TimeSpan.MaxValue` to timestamp ticks overflows on both platforms (frequency 1e9 on Linux, 1e7 on
  Windows), and a negative limit would cut every transfer at the first tick.
- R0-7 (plan:465) and R1-9 (plan:476) test zero and negatives only.

**Fix.** Define the range for `ForwardTimeout` and `ConnectTimeout`: treat anything at or above `int.MaxValue` ms as
infinite (and give the client an infinite timeout), refuse the rest in `Validate` naming the option. For the new
options compare elapsed `TimeSpan`s (`TimeProvider.GetElapsedTime`) instead of adding a limit to a timestamp, or cap
them. Add `TimeSpan.MaxValue` and 30 days to R0-7 and R1-9.

## 4. The fake-clock facts depend on thread scheduling, contrary to §5 (bug, in the tests)

**Evidence.**
- plan:447-451: "They wait for no real timer and do not depend on the machine's speed."
- Steady facts (R1-4, R1-5, R2-5, R3-5; plan:470-471, 482, 488). The caller's 16 KiB send lands in its socket buffer
  at once and the fact advances 0.6 s; nothing makes it wait for the tap to consume that piece. With a 1 s limit and
  a tick a second, the tap need only be two pieces behind (one thread-pool hop late) for a tick to see 1.2 s without
  progress. In the limit, the fact's loop runs all forty iterations before the tap's first read completes, and the
  cut is certain. On a loaded Ubuntu runner these guards fail or flake.
- Stall facts advance once past the limit. If the tap has not registered the entry yet, it is born at the new fake
  time and never expires: `ProxyTap`'s `InFlight` rises at `ProxyTap.cs:236`, before the first read is issued, and
  `OtlpTap` and `TcpTap` have no seam at all (`read/otlptap.md` §7, `read/tcptap.md` §8 "Seams"). R1-3 also has to
  wait until the answer has stopped moving before it advances, or the last stamps land at the new time.
- `FakeTimeProvider` does not always fire inside the caller's `Advance`: a tap thread that marks progress
  (`GetTimestamp()`, hence `GetUtcNow()`, hence `WakeWaiters()`) between `Advance`'s two steps takes the gate and runs
  the heartbeat itself, and the fact's `Advance` returns before the tick has run. A fact that asserts right after
  `Advance`, or flips the debugger seam after it (R1-8, plan:475), can test the wrong state. The same re-entry means
  the watch must never call `GetTimestamp()` while holding its own lock.

**Fix.** An internal seam on the watch (entries by phase, each with its state and last-progress stamp). Steady facts
wait in real time until the entry's stamp equals the current fake timestamp before each `Advance`; stall facts wait
for the entry to exist and its stamp to stop moving, then advance; every assertion polls for its effect (counter, log
line, close) with a patience. Reword plan:450-451.

## 5. The reaper cannot move to the monotonic clock as described (gap)

**Evidence.**
- plan:352-355 and R3-6 (plan:489): checked on the monotonic clock, so "a clock jump can no longer age a command".
- The age's input is `IProtocolDecoder.OldestUnansweredSince`, a public `DateTimeOffset?` (`IProtocolDecoder.cs:48-55`).
  Redis and Mongo publish `pending.Timestamp.UtcTicks` (`RedisProtocolDecoder.cs:111-122`,
  `MongoProtocolDecoder.cs:73-85`), the pump's wall-clock stamp (`TcpTap.cs:294`), which also stamps the recorded
  interactions and must stay wall-clock. A heartbeat on `GetTimestamp()` still has to subtract that instant from a
  wall clock, so R3-6 stays red until the plan names a conversion; the obvious one, a monotonic stamp per command,
  needs a new member on the public interface.
- The conversion that works without one (age = monotonic time since the heartbeat first saw that value) makes
  `AConnectionWhoseSegmentsWereDroppedIsNeverReaped` vacuous: its `StuckBlockingDecoder` returns a fresh
  `DateTimeOffset.UtcNow.AddMinutes(-5)` on every read (`TcpTapTests.cs:768`), so the reaper would never reap it with
  or without the drop stand-down that fact exists to guard.
- R3-6's clock: `FakeTimeProvider` is not sealed, but its `GetTimestamp()` dispatches to the virtual `GetUtcNow()`, so
  the natural subclass that jumps `GetUtcNow` jumps the timestamp as well, the opposite of what the fact needs.

**Fix.** State the mechanism in §4.5 item 5 without touching `IProtocolDecoder`: first sighting on the monotonic
clock, or `min(wall age, monotonic time since first sighting + one tick)`. Make `StuckBlockingDecoder` return a fixed
instant. Build R3-6's provider as a wrapper that delegates `GetTimestamp`, `TimestampFrequency` and `CreateTimer` to
a `FakeTimeProvider` and offsets only `GetUtcNow`.

## 6. After R1, http.sys's send-rate timer does bound the answer, per 16 KiB piece (wrong claim)

**Evidence.**
- plan:299-307 ("none of them bounds a caller that stops reading in time ... The answer's limit is therefore needed
  on both platforms"), plan:211-215 ("on Windows the tap acts, counts and logs before http.sys does, and both
  platforms behave alike") and Q10 (plan:596-599) rest on W1, one 8 MB write.
- The run's own model (`listener/README.md:55-60`: the pending send's bytes over the rate, rounded up to 5 s, plus
  5 s; it fits 445.7 s for 64 KiB at 150 B/s and 14.9 s at 10,000 B/s) gives a stalled 16 KiB piece about 115 s and
  a short last piece far less (576 B: about 10 s). R1 writes in 16 KiB pieces (plan:272-274), so after R1 Windows
  resets a non-reader at about 115 s with nothing counted whenever `CallerStallTimeout` is about 114 s or more, and
  earlier when the stall falls on a short last piece. The 100 s default wins by about 14 s. (Inferred from the
  model; not run.)

**Fix.** Probe a stalled 16 KiB piece and a short piece on Windows. Reword item 8, §4.1 item 3 and Q10: after R1
the answer, like the body, is capped by http.sys on Windows for values past about 110 s. Put that in the option's
doc comment and the changelog.

## 7. Q1's premise misreads CLAUDE.md's MAJOR rule (wrong claim)

**Evidence.** plan:577-578 ("No existing option's default changes, so the major rule (a changed default) does not
apply") and plan:506-508. CLAUDE.md:20 defines MAJOR by its effect, "changing a default so that existing code behaves
differently without being touched"; the minor judgement call at CLAUDE.md:31 covers a new option only when "its
default preserves today's behaviour exactly". R1 to R3 change what untouched code does. (Next to the known item about
the missing v5 alternative, but this is the stated reasoning, not the missing option.)

**Fix.** Say that under CLAUDE.md's wording a behaviour-changing default is the MAJOR trigger, and that a minor is
defensible only by treating the cut as a bug fix (CLAUDE.md:32: "A bug fix that necessarily changes observable
behaviour is still a patch"), which is the owner's call in Q1.

## 8. Diagnostic, log and changelog wording that is false in some cases (wrong claim)

- `OtlpTap`: plan:329 counts an answer stall in `CallerTimeouts` ("the export was already received and counted"),
  while the one `Diagnostics()` line (plan:330-331) says "the exports they were sending were not received". Split
  the line by phase, as `ProxyTap`'s is.
- `ProxyTap`'s log line "answered 408 and closed" (plan:291) is false on Windows, where http.sys sends nothing
  (`listener/README.md` claim 3: "The caller got an RST and no bytes: no 408"). Word it by what the tap did, or by
  platform.
- "(recorded)" in the `Diagnostics()` line (plan:289-290) and "an answer cut this way is still recorded" in the
  changelog draft (plan:518-519) are false for an exchange with no identity while `CaptureUnattributedRequests` is
  off (`ProxyTap.cs:493-497`). Say "recorded when captured".

## 9. An action that blocks stops every limit of its tap (gap)

**Evidence.**
- plan:234-237 says actions never throw; nothing says they never block, and every action runs on the tap's single
  heartbeat.
- `ProxyTap`'s body action ends in the managed `Abort()`, which writes the unsent 408 head with a synchronous
  `_stream.Write` (`source/dotnet-listener-timers.md` §4.2 step 3; §4.5: a synchronous send waits behind a pending one
  with no time limit). R0's disposal end does the same with 503.
- R3 puts the reaper's action on the heartbeat, and it calls the host's `OnCaptureDegraded` (`TcpTap.cs:366`,
  `484-497`); every action calls the host's `Log`.
- Timer callbacks may overlap, so a blocked action also pins a pool thread per tick and walks run concurrently.

**Fix.** The heartbeat only decides; it queues each fired action to the thread pool. Say the walk is re-entrant over
a concurrent collection, and that `DisposeAsync` disposes the timer without waiting for a running callback.

## 10. R3 changes the existing reaper under a debugger, unannounced (gap)

**Evidence.** plan:231-233 skips the whole walk while `Debugger.IsAttached`; plan:352-355 moves the reaper onto that
walk. `ReapStuckConnectionsAfter` would stop reaping while a debugger is attached, which is when someone is looking
at the wedged client it exists for, and it would call `Log` and `OnCaptureDegraded` on the heartbeat. Neither is in
R3's text or changelog. The reaper facts (`TcpTapTests.cs:57-127`) and the real-clock runs of R1-1, R2-1 and R3-1
would fail whenever a developer debugs the suite, unless they set the debugger seam.

**Fix.** Say it in §4.5 item 5 and in R3's changelog (or exempt the reaper from the debugger rule), and have every
limit fact and every reaper fact set the debugger seam to "not attached".

## 11. R2 leaves four of `OtlpTap`'s waits without a fact or a rule (gap)

- **The answer.** plan:311-312 and 329 cut a stalled answer, but `WriteResponseAsync` writes the head and then a
  relayed collector body in one `WriteAsync` each (`OtlpTap.cs:471-474`), so a large relayed body gets a total
  deadline, against §4.1 item 1. No fact, no mutation. Write it in 16 KiB pieces with progress after each (or say why
  it is always small), and add a fact: a non-reading exporter behind a collector that relays a body larger than the
  socket buffers.
- **The forward.** Nothing twins R1-6: no fact proves the forward (`OtlpTap.cs:434-435`, 30 s client timeout) is
  outside the watch. Were it inside, the cut would land after the export was counted and queued (L330, L338), against
  "not received". Add the fact and the mutation "watch the forward".
- **The drain, chunk lines and trailers.** The 413 drain (`HttpRequestReader.cs:96-100`, `DiscardAsync` L255-266),
  chunk size lines, chunk CRLFs and trailers (L175-226) stall through the same `FillAsync`, but the plan does not say
  whether a stalled drain answers 408 or 413, which counter moves, or test it. Decide (408 and `CallerTimeouts`,
  since the 413 has not been sent) and add a theory over a drained over-cap body and a chunked body stalled in a
  size line and in its trailers.
- **R2-3 and R2-4** (plan:480-481; R2-4's "Proves" cell is empty) cannot kill "the first-byte wait timed by
  `CallerStallTimeout`": 100 s is shorter than the 120 s keep-alive, and both facts only check a quiet close. Assert
  the connection is still open past `CallerStallTimeout`, closed after `KeepAliveTimeout`, nothing written.
- Also say which clock bounds the 408's "5-second bound of its own" (plan:328); on the fake clock it needs its own
  advance.

## 12. R1 guards that cannot fail on Linux CI (gap)

- R1-10 (plan:477) is `Keep_alive_requests_are_answered_on_one_connection`, which already exists
  (`ProxyTapHangUpTests.cs:264`). After R1 the default turns the limit on, so "with the limit on" adds nothing, and no
  named mutation turns it red. In `ProxyTap` an entry left armed after its exchange is invisible (its `TimeOut()`
  loses to a closed answer and counts nothing), so only a leak would show. Drive it with the fake clock, advance past
  the limit between requests, and assert `CallerTimeouts` 0 and the watch's entry count 0 after every exchange, after
  the 400 path and after a 502; add the mutation "never remove the entry".
- R1-1 (plan:467) should be a theory over Content-Length and chunked bodies and include a head with no body byte. On
  Linux CI a Content-Length read returns what has arrived, so only the chunked or zero-byte case kills "the entry
  armed at the first completed read rather than the first issued one".

## 13. R0 item 7 fixes one of three identical cap bugs (gap)

**Evidence.** plan:416-418 refuses a negative `BodyCapBytes` for `TcpTap` only.
- `ProxyTapOptions.BodyCapBytes` (L54) is not checked by `Validate` (L186-199) and reaches `text[..cap]`
  (`ProxyTap.cs:740-741`), which throws for a negative cap: every record with a body becomes a capture fault blamed on
  "the sink" (L570-573).
- `OtlpTapOptions.ContentCapBytes` (L125) is not checked (L140-155) and reaches `value[..limit]`
  (`SpanToInteractionMapper.cs:418-425`): every payload with a statement counts in `PayloadsFailed`.

CLAUDE.md asks for every bug found to be fixed. **Fix.** Refuse a negative cap in all three `Validate` methods in R0,
a fact each.

## 14. Bugs the reading notes found that the plan neither fixes nor lists (gap)

- `read/otlptap.md` §11.4: an over-cap drain cut short by a hang-up still returns a too-large request
  (`HttpRequestReader.cs:260-261`, L100), so `PayloadsRejected` moves and a 413 is written to a gone exporter (the
  chunked drain returns null since #172).
- §11.6: `OtlpTap`'s stop line (`OtlpTap.cs:611-613`) leaves out `PayloadsRejected` and `PayloadsFailed` (R3 fixes
  `TcpTap`'s, plan:356-357).
- §11.7: a request with both `Transfer-Encoding: chunked` and `Content-Length` keeps the connection
  (`HttpRequestReader.cs:84-91`); RFC 9112 §6.1 says the server MUST close it after answering.
- `read/proxytap.md` §1: `_cts?.Token` at `ProxyTap.cs:397` throws `ObjectDisposedException` once L1003 has disposed
  `_cts`, so a handler that reaches it after the 2 s drain (behind a slow `IdentityResolver`) is counted as a forward
  failure with a 502 attempt, against `ForwardFailures`' own doc (L86-87: "Nor is a forward the tap's own disposal cut
  short").

**Fix.** Put them in R0 with facts, or in §8 with a reason.

## 15. Doc comments R3 makes false, and doc edits Appendix A misses (gap)

- `CaptureDegradationKind.StuckConnectionReaped` (`CaptureDegradation.cs:22-24`) and `ReapStuckConnectionsAfter`
  (`TcpTapOptions.cs:157-158`) call the reaper "the one intervention that ever touches forwarding"; after R3 there are
  three.
- R0 item 8 rewrites `OnCaptureDegraded`'s thread claim (`TcpTapOptions.cs:139-141`) for the reaper's own task; R3
  moves the reaper onto the heartbeat, so it changes again.
- `ProxyTap.ForwardFailures` (`ProxyTap.cs:82-89`) excludes only a caller that hangs up; a body cut by the limit needs
  the same clause (`read/proxytap.md` §6).
- `DiagnosticKind.CaptureDegraded` (`src/Kronikol/Reports/DiagnosticEntry.cs:41-45`) says "forwarding was never
  affected", and R1 to R3 file their cut-caller lines under it (plan:290); R0 item 8 fixes the same drift in
  `TcpTap`'s enum.

**Fix.** List each in Appendix A against its release.

## 16. The wiki list leaves out R0 and `ProxyTap`'s counter (gap)

plan:525-531 lists only R1-R3 edits. R0 changes documented behaviour: which `ForwardTimeout` values are accepted and
that infinite now means infinite, what a caller hears when `ProxyTap` stops, `ExpectedHeaders` checked before the body
(and an over-cap request without the secret now counted as unauthenticated, not rejected), `Content-Length` answered
400 or 413, a negative cap refused. The `ProxyTap` entry also omits `CallerTimeouts`, though the `OtlpTap` and `TcpTap`
entries name their counters (plan:529-530). **Fix.** Add an R0 entry per page and the counter to `ProxyTap`'s.

## 17. `HalfClosedTimeout` defaults on for protocols the plan did not look at (gap)

plan:335-341 justifies 100 s by Redis and MongoDB clients, but `TcpTapOptions.DecoderFactory` is there for "any other
protocol" (`TcpTapOptions.cs:181`; `AddTcpTapTestTracking` registers a plain tap). A protocol that half-closes to end
its request and waits for a long reply (an HTTP/1.0-style exchange, a batch RPC) is cut at 100 s even once the limit
becomes inactivity in the remaining direction, since that direction is idle until the reply's first byte. **Fix.**
Default it in `RedisTapOptions` and `MongoTapOptions` and leave plain `TcpTapOptions` at null, or argue the generic
default in Q2.

## 18. The heartbeat captures the starting context (gap)

`TimeProvider.CreateTimer` "captures the ExecutionContext and stores that with the ITimer for use in invoking
callback each time it's called", and the timer is rooted while scheduled. Created in `StartAsync`, the heartbeat would
run every action under the starter's `Activity.Current` and AsyncLocal test identity and keep them alive for the
tap's life. `ProxyTap.StartAsync` already clears `Activity.Current` for its accept loop for this reason
(`ProxyTap.cs:199-211`). **Fix.** Create the timer under `ExecutionContext.SuppressFlow()` in all three taps and say so
in §4.2 item 2.

## 19. `TcpTap`: what R3 leaves untested or unstated (nit)

- plan:354-355 claims a pair is no longer released up to a second late, but no fact times it; the probe measured 540
  and 954 ms against 31 ms (`probe/README.md`, T3N). Add a fact on `DisposeAsync` with the reaper on and the mutation
  "keep the per-pair loop".
- R3-3 (plan:486) should open the idle pair with an exchange both ways, as
  `AnIdleConnectionWithNothingUnansweredIsNeverReaped` does, or the mutation "write-pending mark never cleared"
  survives.
- `await decodeTask` (`TcpTap.cs:244`) holds a pair's sockets while a blocked sink or `IdentityResolver` runs; that is
  capture, not a peer, but §4.5 should say why it is out of scope.

## 20. Two classifications still read an exception or a flag, not the state (nit)

plan:611 says R1 "decides by its own state after every read and write, never by the exception". `RespondAsync`'s catch
is typed (`ProxyTap.cs:855`): an unexpected type from a write racing `Abort()` (unmeasured: an abort while the handler
is inside the listener's synchronous `BeginWriteCore` rather than awaiting it) reaches the catch-all, which tests
`answer.Closed` (L248); with R0's five states a timed-out or ended answer there would count a forward failure and
attempt a 502. **Fix.** Once the answer is timed out or ended, take any exception as the limit's or the stop's, and
spell out how L248 reads the new states.

## 21. R0's claims about its own items (nit)

- plan:364 says each R0 item "gets a fact"; §5 has none for item 5 (`NoDelay`) or item 8, and item 5's premise, "A
  socket reset between the accept and L281 throws there" (plan:401-402), is marked "Not seen" in `read/otlptap.md`
  §11.5. Reword it as defensive, or add a seam and a fact.
- R0 item 4 (plan:394-400): `int.TryParse(..., NumberStyles.Integer, ...)` (`HttpRequestReader.cs:93`) accepts a
  sign, so `+10` passes while `-5` is refused; RFC 9110 has `Content-Length = 1*DIGIT`. Parse with
  `NumberStyles.None` and add `+10` to R0-4.

## 22. R0-8's timing assertion needs enough callers (nit)

plan:466: "`DisposeAsync` does not wait a second per such caller". After the fix `DisposeAsync` can still take up to
the 2 s drain (`ProxyTap.cs:999-1000`) on a loaded runner, and before it one stalled caller cost about 1 s (3,028 ms
for three, probe B1), so with one or two callers the fact cannot tell them apart reliably. Use at least four callers
and a bound below their count in seconds, or time `_listener.Close()` alone.
