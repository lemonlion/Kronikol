# Audit D: StallWatch and TcpTap's time limits (4.22.2, 4.23.0, 4.26.0)

Read-only audit of `C:/Code/Kronikol-173-audit` (detached at origin/main, `09369515`). Nothing was built or run; every
finding comes from reading the code, the diffs `2ffd3f23..9b7886d9` (4.22.2) and `a434e2f6..a3b20f99` (4.26.0), the
plan (`plans/TAP_CALLER_TIME_LIMITS_PLAN.md` sections 4.2, 4.5, 4.6, 5, 11, Appendix A) and the tests
(`tests/Kronikol.Tests.TcpTap/TcpTapStallTests.cs`, `TcpTapLogTests.cs`, `TcpTapTests.cs`,
`TcpTapConnectFailureTests.cs`, `tests/Kronikol.Tests.ProxyTap/StallWatchTests.cs`). The .NET runtime behaviour cited
(`CancellationTokenSource.CancelAfter`'s range, `TimeProvider.System` timers rooting themselves) is from memory of the
runtime source and its docs, not checked against the source here.

I tried to refute each candidate before keeping it. The watch itself held up: the entry's single CAS state, overlapping
beats, a throwing action, a throwing check, a throwing debugger probe, `TimeSpan.MaxValue`, the flow-suppressed timer,
and an action queued just before disposal all behave as documented. What is left is one medium defect in option
validation and six low ones in how TcpTap uses the watch, plus some observations.

---

## D1 (medium). `TcpTapOptions.Validate` never checks `ConnectTimeout`; a negative or very large value drops every connection without counting it

- **Where:** `src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs:241-275` (Validate, no `ConnectTimeout` check; the option
  is at :130); `src/Kronikol.Extensions.TcpTap/TcpTap.cs:353-355` (`connectCts.CancelAfter(_options.ConnectTimeout)`
  runs *before* the `try` that sorts connect failures); `TcpTap.cs:329-334` (the connection's catch-all).
- **Defect:** A `ConnectTimeout` below -1 ms, or above `Timer.MaxSupportedTimeout` (4,294,967,294 ms, about 49.7 days,
  so `TimeSpan.MaxValue` too), makes `CancelAfter` throw `ArgumentOutOfRangeException` on every connection. That happens
  outside the connect's own `try`, so the exception reaches the catch-all as an "unexpected" error.
- **Failure scenario:** A host sets `ConnectTimeout = TimeSpan.MaxValue` to mean "no limit". That is the natural
  reading: the 4.26.0 sibling options take `TimeSpan.MaxValue` as "never", and `ProxyTapOptions.ConnectTimeout`, which
  `TcpTapOptions`' remarks say it mirrors option-for-option, has treated it as "no deadline" since 4.22.2. A typo giving
  a negative span does the same. `StartAsync` succeeds and logs "listening", then every accepted caller is closed at
  once with nothing forwarded. Each one logs only `tcp-tap connection N error: ArgumentOutOfRangeException: ...`.
  `UpstreamConnectFailures` stays 0, no `OnCaptureDegraded` event is raised and `Diagnostics()` is empty, so the report
  says capture was complete while the service could not reach its database at all. `TimeSpan.Zero` is not refused
  either, unlike in ProxyTap: every connect then counts as timed out (visible, but a misconfiguration ProxyTap rejects
  at start).
- **Why it was missed:** R0 item 1 (plan section 4.6) fixed exactly this class for ProxyTap
  (`ProxyTapOptions.cs:237-240`, plus `Deadline()` mapping at least `int.MaxValue` ms to none). Its TcpTap twin is
  older (the option and the call outside the `try` date from `f46ebdb6`) and was not part of that fix.
- **Confidence:** confirmed by reading (the `CancelAfter` range is documented .NET behaviour).
- **Tests:** none. No TcpTap fact sets an out-of-range `ConnectTimeout`; R3's `A_limit_of_zero_or_less_is_refused`
  covers only the two new options.

## D2 (low). The stop line still leaves out the reaped and closed-mid-message counts that the plan gave R3 to add

- **Where:** `TcpTap.cs:987-991`; plan section 4.6 item 8 (lines 576-578: "The stop line ..., a log string that leaves
  out the reaped and closed-mid-message counts, is R3's").
- **Defect:** R3 rewrote the stop line but added only its own two counters, so `StuckConnectionsReaped` and
  `ConnectionsClosedMidMessage` are still missing. The line now names two of the three limits that close a pair, the
  third being the reaper.
- **Failure scenario:** A run where the reaper closed 3 wedged connections and 5 closed mid-message ends with
  `... 0 half-closed pairs closed, 0 stalled writes closed)`, which reads as "nothing was closed". A reader of the log
  alone (the counters' only other outlet is `Diagnostics()`) never learns that connections were reaped.
- **Confidence:** confirmed by reading.
- **Tests:** `TcpTapConnectFailureTests.cs:335` and `TcpTapStallTests.cs:59-60, 157-159` pin the current line, so they
  would need updating; nothing checks the missing counts.

## D3 (low). The reaper closes a pair outside `PairLimits`' close-once compare-exchange, so one pair can be counted, logged and reported twice

- **Where:** `TcpTap.cs:585-596` (the reaper's `fired` counts, logs, raises and disposes unconditionally and never
  touches `_closedBy`); `TcpTap.cs:1033-1036` (`Close` arbitrates only between the two new limits); `TcpTap.cs:322-325`
  (the reaper's entry is ended only after both pumps end).
- **Defect:** The 4.26.0 docs call the reaper "one of three limits that close a pair", and the changelog says "a limit
  that fires closes the pair once (one compare-exchange)". That compare-exchange covers only `HalfClosedTimeout` and
  `StalledWriteTimeout`. The reaper's entry and a limit's entry are separate watch entries, so both can win their own
  `TryFire` in the same beat and both act.
- **Failure scenario:** `ReapStuckConnectionsAfter = 100 s` beside the default `StalledWriteTimeout = 100 s` (a
  natural "everything at 100 s" setting). A server freezes (paused, or stops reading) while a client streams a large
  command, such as a bulk MongoDB insert or a multi-MB Redis SET. The command is decoded as unanswered at T, and the
  tap's write to the server blocks a few milliseconds later, once the socket buffers fill. Both entries come due in the
  same one-second beat, so both actions run. The pair is counted in `StuckConnectionsReaped` *and* `StalledWritesClosed`,
  gets two close lines in the log, and a `StuckConnectionReaped` event is raised. `Diagnostics()` then reports two
  closed connections for one. The reverse order behaves the same way: when a limit closes the pair in the beat the
  reaper fires, the reaper still counts and raises its event. A related, mostly older window: the reaper's queued
  action re-checks nothing, so a command answered, or a connection the client itself closed, between the beat's check
  and the action (which now includes the thread pool's queue delay) is still reaped and reported.
- **Confidence:** confirmed by reading that both actions run. How often the timing lines up is an estimate (it needs
  both limits due in the same beat).
- **Tests:** none combine the reaper with a pair limit.

## D4 (low). A half-closed pair whose remaining pump is blocked in a write is counted as a half-close about half the time, and the log blames the wrong side

- **Where:** `TcpTap.cs:477, 484-500` (progress is recorded only after a read and after a completed write);
  `TcpTap.cs:1018-1024` (the half-closed entry); `TcpTap.cs:1037-1049` (the two messages).
- **Defect:** Once one side has finished sending, the remaining pump records no progress while it is blocked in a
  write. So when the side that half-closed then stops reading, the half-closed entry (last progress: the read just
  before the blocked write) and the write's own entry (created microseconds later) come due in the same beat. Both
  fire. Which one wins `_closedBy` depends on the order the dictionary enumerates them in (object hash codes) and on
  which action reaches the thread pool first: a coin flip.
- **Failure scenario:** A client (on a generic TcpTap, or any protocol that half-closes) sends a request, sends its FIN,
  and then stops reading an 8 MB reply. At 100 s the pair is closed, as it should be. But in about half the runs it is
  counted in `HalfClosedPairsClosed`, with the line `closed: the client finished sending and the server moved nothing
  for 00:01:40`. In fact the server sent everything and the client stopped reading, which is
  `StalledWritesClosed`'s case and wording. The `Diagnostics()` line ("the other moved nothing") misleads the same way.
  The mirror case (the server half-closes, then stops reading the client's commands) blames the client.
- **Confidence:** confirmed by reading. That the order is about 50/50 is inferred from hash-ordered enumeration and
  both actions being queued in the same beat.
- **Tests:** none. `A_client_that_stops_reading_a_large_reply_releases_the_pair` never half-closes.

## D5 (low). `StartAsync` creates the watch (on by default) before binding, so a failed bind leaks a heartbeat that ticks for ever

- **Where:** `TcpTap.cs:219-220` (the watch is created before `ResolveBindAddress` at :222 and `listener.Start` at
  :224); `StallWatch.cs:51-52` (a periodic `TimeProvider` timer). For comparison: `ProxyTap.cs:264-267` creates its
  watch *after* `_listener.Start()`, while `OtlpTap.cs:222-223` has TcpTap's order (watch, then `Bind()` at :239).
- **Defect:** Since 4.26.0 every TcpTap with default options has a watch. If the bind throws (port in use, a
  `ListenHost` that does not resolve, access denied), the watch's one-second timer has already been created.
  `TimeProvider.System` timers root themselves while they are scheduled, and a periodic one always is. So unless the
  host calls `DisposeAsync` on a tap that never started, the heartbeat runs, and keeps the tap's whole object graph
  alive (options, `Sink`, `Log`, `OnCaptureDegraded`, `IdentityResolver`), until the process exits. Calling
  `StartAsync` a second time on the same instance overwrites `_watch`, and the first watch is never disposed even by
  `DisposeAsync`.
- **Failure scenario:** `AddRedisTapTestTracking` with a fixed `ListenPort` that another fixture already holds. The
  hosted service's `StartAsync` throws, and the generic host neither stops that service nor disposes the tap, because
  MS.DI never disposes an instance passed to `AddSingleton(instance)`. The test process carries a ticking timer and the
  tap's graph for the rest of the run, one more per failed attempt in a retry-on-conflict loop. The impact is small
  (CPU and memory only, no wrong behaviour), but it is new since 4.26.0: before it, a failed start left nothing
  running.
- **Confidence:** confirmed by reading the ordering. The self-rooting timer is runtime behaviour, from memory of
  `TimeProvider.cs` ("CreateTimer needs to return a timer that's implicitly rooted while scheduled").
- **Tests:** none. No fact starts a TcpTap on a busy port and checks the watch afterwards.

## D6 (low). The reaper's check runs the decoder's `OldestUnansweredSince` on the shared heartbeat under a lock, so one blocking decoder stalls every pair's limits and loses a pool thread per beat

- **Where:** `TcpTap.cs:543-584` (`due` takes `lock (gate)` and calls `decoder.OldestUnansweredSince`, which a custom
  `DecoderFactory` supplies); `StallWatch.cs:107-110, 119-126` (the walk calls each check synchronously and in turn).
- **Defect:** Before 4.26.0 each pair's reaper ran on its own task, so a decoder whose getter blocked held one thread
  and affected one pair. Now the getter runs inside the tap-wide beat. While it blocks: (a) that beat never checks the
  entries enumerated after this one; (b) every later beat reaches the same entry and blocks on `lock (gate)`, which the
  first beat still holds. So each second, one more pool thread is stuck for good. The pairs whose entries sit past this
  one in bucket order lose their half-close, stalled-write and reaper limits for as long as the getter blocks.
- **Failure scenario:** A custom decoder (the public `DecoderFactory`, "to tee any other protocol") guards its pending
  queue with `lock (_gate)` both in `OldestUnansweredSince` and in `OnClientToServer`, where it calls `context.Record`.
  The host's sink blocks there (a full NDJSON pipe, say). The test suite's own `OneCommandDecoder` uses exactly that
  lock pattern in its getter. From then on the thread pool grows by about one blocked thread a second, and other
  connections' time limits stop firing.
- **Mitigation:** The interface doc (`IProtocolDecoder.cs:48-55`) asks implementations to publish this value with a
  volatile or interlocked field, and the shipped Redis and Mongo decoders do. So this needs a decoder that breaks that
  contract; the change is that the damage is now tap-wide and grows over time.
- **Confidence:** plausible (confirmed by reading that the walk blocks; the trigger needs a contract-breaking decoder).
- **Tests:** none.

## D7 (low, doc). The public docs say a pair a time limit closes "has lost none of its capture" and raises no event; with the shipped decoders the main cases do both

- **Where:** `CaptureDegradation.cs:25-34` ("the other two ... are counted but not reported here, since a pair they
  close has lost none of its capture"); CHANGELOG 4.26.0 Added ("A pair a limit closes has lost none of its capture,
  so no `OnCaptureDegraded` event is raised for it"); `RedisProtocolDecoder.cs:188-205` and
  `MongoProtocolDecoder.cs:198-215` (`ReportClosedMidMessage` on close); `TcpTapStallTests.cs:63-86` (the 4.26.1 fact).
- **Defect:** The main stalled-write case is a client that stops reading a large reply, so the reply is cut partway
  through. The main half-close case is a command that is never answered. In both, RedisTap or MongoTap closes with a
  pending command or a partial reply. `OnConnectionClosed` then reports `ConnectionClosedMidMessage`, which counts,
  logs, raises an `OnCaptureDegraded` event, and leaves the interaction unrecorded. Plan section 4.5 item 6 expected
  exactly this ("a command cut short is already `ConnectionClosedMidMessage`"); the public text dropped that half.
- **Failure scenario:** A host trusts the doc and treats any `OnCaptureDegraded` event as "capture lost elsewhere". It
  is then surprised by `ConnectionClosedMidMessage` events, and missing arrows, on every pair a stalled-write limit
  cuts.
- **Confidence:** confirmed by reading.
- **Tests:** `A_pair_a_limit_closes_raises_no_capture_degradation` uses `OneCommandDecoder`, whose `OnConnectionClosed`
  does nothing, so it cannot see this.

---

## Observations (checked, not reported as defects)

- **Hot-path allocation.** Plan sections 4.2 item 1 and 4.5 item 4 say marking a write pending costs one
  `Volatile.Write` and allocates nothing. The code instead allocates, on every forwarded segment of every pair (the
  limit is on by default): an `Entry`, a closure (`PairLimits.Writing`, `TcpTap.cs:1014-1015`) and a dictionary node.
  That is two lock-striped dictionary operations per write. It is correct, and the measured impact is unknown; it is a
  departure from the design, not a bug.
- **Clock jumps.** The claim that "a jump of the machine's clock no longer ages a command" holds only after the
  reaper's first sighting. The age at that sighting is wall-clock time (`TcpTap.cs:576-581`), so a jump between the
  segment's stamp and the first beat that sees the command still ages it. That gap is up to one beat, plus any backlog
  in the decode queue (or a VM resume). This is by design (plan section 4.5 item 5), but the changelog states it more
  broadly.
- **Byte counters.** A write that completed after its limit fired is not added to `BytesClientToServer` or
  `BytesServerToClient`, because the `HasFired` break at `TcpTap.cs:498-499` comes before the counting.
- **Clock read under a lock.** The reaper's check reads the clock while holding `gate`, against plan section 4.2 item 3
  ("never reads the clock while it holds a lock"). This is harmless with `TimeProvider.System`, and in the facts
  `FakeTimeProvider`'s non-blocking wake gate prevents a deadlock.
- **`PumpEnded` publish order.** `PumpEnded` increments before it publishes the half-closed entry, so the second pump
  can miss it. `End()` then ends it. The window is microseconds, because the `WhenAll` continuation runs inline, so only
  a limit of a few ticks could misfire. Not reported.
- **Ruled out:** a write entry left active on an exception path (the `finally` at :493-496 covers every exit); a pump
  left blocked after `Close` (cancel, then dispose); `LiveConnections` not decremented (the finally at :335-341 is
  unconditional); double dispose (`TcpClient` and `Socket` dispose are idempotent); `Close` after the pair's
  `CancellationTokenSource` has been disposed (the `ObjectDisposedException` is caught, and `End()`'s compare-exchange
  normally returns first); `CopyTo` (every public settable property is copied, checked by reflection, and the Redis and
  Mongo overrides call the base); Validate for `null`, `Timeout.InfiniteTimeSpan`, 0, -2 ms, `TimeSpan.MinValue` and
  `TimeSpan.MaxValue` on the two new options; the reaper's arithmetic (`DateTimeOffset` differences cannot overflow a
  `TimeSpan` sum; a negative age only delays the reap); the reaper when `ReapStuckConnectionsAfter` is null (no entry
  is created); the half-close direction and its wording (`finished` is the source of the pump that ended); disposal
  racing a beat (`Act` returns early, and the stop's cancel releases any pump that left on `HasFired`).
