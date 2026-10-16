# Audit A: plan coverage of TAP_CALLER_TIME_LIMITS_PLAN.md (#173)

Tree: `C:/Code/Kronikol-173-audit` at `09369515` (origin/main, 4.26.1). Releases audited: R0 4.22.2 (`9b7886d9`), R1
4.23.0 (`c7eef4b3`), R2 4.25.0 (`946e36e8`), R3 4.26.0 (`a3b20f99`), 4.26.1 (`be6384a9`). Wiki read only through
`git show origin/master:<Page>.md` and the four release edits (`bb13d64`, `01b1578`, `9ca835f`, `17d42b6`). Read-only:
nothing was built, run or edited.

The behaviour itself is in place. Every R0 item, every R1/R2/R3 requirement and every §5 test id has code and a fact
that checks it. The gaps below are one log string, two doc comments, a mechanism that differs from the plan on
TcpTap's hot path, mutation proofs, changelog and wiki text, two facts weaker than planned, and one start-up leak
found along the way. Only gaps are listed.

---

## A1. TcpTap's stop line still leaves out the reaped and closed-mid-message counts (medium)

- **Plan:** §4.6 item 8, L576-578: "The stop line (L715-718), a log string that leaves out the reaped and
  closed-mid-message counts, is R3's, which adds its counters after the failed accepts and connects #174 added." The
  plan files this under F9, "doc drift" (`read/tcptap.md` L319, L552-554). The harness's re-check at 4.22.1 is
  explicit: "`TcpTap`'s [stop line] names failed accepts and failed connects but not reaped or closed-mid-message
  connections (R3 adds them)" (`sweep/notes-rebase-4.22.1.md` L34). Appendix A L1069 (R3): "two counters, their lines,
  the stop line". OtlpTap's equivalent defect was fixed in R0 (item 10) and has its own fact, R0-10.
- **Shipped:** `src/Kronikol.Extensions.TcpTap/TcpTap.cs:987-991`. The stop line names `HalfClosedPairsClosed` and
  `StalledWritesClosed` but still not `StuckConnectionsReaped` or `ConnectionsClosedMidMessage`. No fact checks either:
  `TcpTapStallTests` (the R3-1 and R3-4 stop-line asserts) and
  `TcpTapConnectFailureTests.TheStopLineNamesFailedConnectsAndFailedAccepts` both end their `EndsWith` on
  `", N half-closed pairs closed, N stalled writes closed)"`. The 4.26.0 changelog does not mention it either.
- **Why medium:** this is the one planned code change that did not ship, and no fact covers it. The impact is limited
  to the log, because `Diagnostics()` still reports both counts.

## A2. ProxyTap `ForwardFailures` doc comment not updated for a body cut by the limit (low)

- **Plan:** Appendix A L1038, R1: "`ForwardFailures`' doc: a body cut by the limit is not a forward failure either".
- **Shipped:** `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs:95-102` is unchanged since R0. It excludes a caller that
  hangs up, the tap's own disposal and capture faults, but not a caller cut by `CallerStallTimeout`. R1's diff
  (`609ba8f3..c7eef4b3`) does not touch it. The wiki does say it (ProxyTap page, "Counters and diagnostics").

## A3. TcpTap's write watch registers a new entry for every forwarded segment (low)

- **Plan:** §4.5 item 4, L438-440: each pump "marks 'write pending since' before each `WriteAsync` and clears it
  after (a `Volatile.Write` of a timestamp each)". §4.2 item 1, L259-261: "Progress is one `Volatile.Write` ... so
  the hot path allocates nothing."
- **Shipped:** `TcpTap.cs:484-496` calls `PairLimits.Writing` (`TcpTap.cs:1014-1015`) around every write.
  `StallWatch.Watch` (`StallWatch.cs:70-77`) then allocates a new `Entry`, a closure capturing the pair and direction,
  and a `ConcurrentDictionary` node for each forwarded segment, and `TryEnd` removes it again. It is on by default
  (100 s), so every TcpTap pays it. Each beat also enumerates `_entries.Keys` (`StallWatch.cs:107`), which takes all of
  the dictionary's locks and briefly blocks those per-write adds and removes.
- **Effect:** behaviour is correct (R3-3, R3-4 and M5 pass). The hot path costs more than the plan's design allowed.

## A4. The reaper's check reads the clock while holding a lock (low)

- **Plan:** §4.2 item 3, L277-278: the walk "never reads the clock while it holds a lock (§5)". §5, L629 gives the
  reason: `FakeTimeProvider` runs due timer callbacks on whichever thread next reads it.
- **Shipped:** the reaper's `due` and `fired` closures (`TcpTap.cs:543-596`) run from the watch's walk and its action.
  Inside `lock (gate)` they call `Clock.GetTimestamp()`, `Clock.GetUtcNow()` and `Clock.GetElapsedTime(...)` (L579-583
  and L588-589).
- **Effect:** none expected. `TimeProvider.System` takes no lock, and `FakeTimeProvider`'s re-entrancy gate prevents a
  deadlock. It is still a departure from a rule the plan states.

## A5. R1's mutation proofs leave out two of the plan's mutations and substitute a third (low)

- **Plan:** §5 "Proofs", L688-697, names among others: "move the watch into the forward (R1-6)", "drop the
  `Interlocked` move in `TimeOut` (R1-7)" and "arm the body's entry at the first completed read (R1-1)".
- **Shipped:** `plans/TAP_CALLER_TIME_LIMITS_PLAN.harness/r1/mutations_r1.py` runs 20 mutants (S1-S5, P1-P11, D1,
  O1-O3), and the README and §11 log report "20 of 20 killed". None of them moves the watch into the forward or arms
  the body's entry at the first completed read. R1-7 is proven by P9, which widens the phase check
  (`phase > Closing`), not by dropping the compare-exchange. The facts would probably kill both missing mutants:
  `A_slow_upstream_is_not_the_callers_stall` asserts no active entry during the forward, and R1-1's `NoByte` case
  needs an entry before any read. But the proof the plan asks for was not run. R0, R2 and R3 ran every mutation §5
  names for them (r0 O1; r2 K1, F1, B4, A1; r3 M1-M5).

## A6. Option doc comments miss the debugger rule's limits (low)

- **Plan:** §4.1 item 4, L243-246: the debugger check sees only the tap's own process, so a service paused in a
  debugger in another process is cut after the limit. "The options' doc comments and the wiki say so, with `null` as
  the setting for such a session."
- **Shipped:** `TcpTapOptions.HalfClosedTimeout` and `StalledWriteTimeout` (`TcpTapOptions.cs:203-221`) say only "No
  limit fires while a debugger is attached to the tap's process". Neither mentions a peer paused in another process or
  `null` for such a session. `OtlpTapOptions.KeepAliveTimeout` (`OtlpTapOptions.cs:83-90`) says nothing about
  debuggers, although the keep-alive limit is also suspended under one. ProxyTap's and Otlp's `CallerStallTimeout`
  docs do carry the full rule, and so do all three wiki pages.

## A7. Changelog text falls short of §6.1 and §6.2 (low)

- **Plan:** §6.2, L715-729 (R1's draft) includes: the slow-caller rule as "16 KiB per limit, up to 32 KiB for an
  answer's last piece"; "the wait for the upstream is still `ForwardTimeout`'s"; "A caller that stalls inside its
  request head is beyond the tap ... (see the wiki's Limitations)"; and "a minor ... with the cut read as the fix of a
  defect rather than a changed default (Q1)". §6.1, L706-709 says each minor changes what untouched code does and "the
  changelog says so plainly". R2's and R3's entries "follow the same form" (L729).
- **Shipped:**
  - 4.23.0 (`CHANGELOG.md:267-329`) never mentions the request-head limitation.
  - Its header says "a caller that moves bytes at least once a limit is never cut" (L272-273). That is not true for a
    body read on Windows or a chunked one on Linux, where a read completes only at 16 KiB, nor for an answer piece.
    4.25.0's header (L112) says the same about the answer phase.
  - None of 4.23.0, 4.25.0 or 4.26.0 explains why a default that changes untouched behaviour ships as a minor (the
    cut read as a defect fix). Each only says "a new option ... so the minor part moves" and "on by default (Q1)".

## A8. TcpTap wiki page: `Diagnostics()` lines incomplete, and the rewritten "what closes a connection" sentence is wrong (low)

- **Plan:** §6.3, L754-767: R3 rewrites L37 ("A tap is never the reason a request fails") "to say what closes a
  connection". For R1 to R3, "the tap pages carry each tap's `Diagnostics()` lines", and Diagnostics-and-Debugging
  points to them.
- **Shipped (wiki `origin/master`, `17d42b6`):**
  - `Integration-TcpTap-Extension.md`'s `### Diagnostics()` example block lists one line per counter, including the
    reaper's, but not the two new lines ("closed after one side finished sending ...", "closed because a write to one
    side did not complete ..."). Diagnostics-and-Debugging now says "each tap's page lists its lines".
  - The new sentence reads "A tap closes a connection only for a time limit: a command left unanswered ... and, since
    4.26.0, a half-closed pair that goes silent or a side that stops reading". That leaves out the close #174 added in
    4.22.0, a connect to the real server that fails or times out, which the page's own `ConnectTimeout` row describes.

## A9. R1-1 on Windows does not assert the reset (low, weaker than planned)

- **Plan:** §5 R1-1, L655: "managed: `HTTP/1.1 408` then a close; http.sys: a reset."
- **Shipped:** `ProxyTapStallTests.A_caller_that_stalls_in_its_body_is_cut_and_not_forwarded`
  (`tests/Kronikol.Tests.ProxyTap/ProxyTapStallTests.cs:70-74`). On Windows it asserts only that the caller was let go
  and that its first line is not `HTTP/1.1 2…`. A FIN with no bytes, or a 4xx/5xx, would pass. R0-8's fact does
  assert `"a reset"` on Windows (`ProxyTapStoppingTests.cs:45-46`). CI runs these projects on Linux only, so the
  Windows branch is the one run by hand before each release.

## A10. R0-5 leaves out OtlpTap's per-connection "connection error" site (low, weaker than planned)

- **Plan:** §5 R0-5, L647: a theory "per tap and per call site that a fact can reach". §4.6 item 6, L554-556 lists
  OtlpTap's per-connection sites "L304, 313, 321, 341, 409 and 448" plus `MapPayload` (L526).
- **Shipped:** `OtlpTapLogTests.A_throwing_log_callback_changes_nothing` (`tests/Kronikol.Tests.Otlp/OtlpTapLogTests.cs:25-33`)
  covers 313, 321, 341, 409, 448 and 526 but not 304. That site is now `OtlpTap.cs:392-395` and goes through `TryLog`,
  so F3 holds. A fact can reach it: the internal `BeforeQueued` and `AfterResponseWritten` seams (`OtlpTap.cs:543-545`)
  can throw a non-I/O exception into the catch-all. R0's mutation set has no mutant for it either.

## A11. Found along the way: OtlpTap and TcpTap start the heartbeat before the bind that can fail (low)

- **Plan context:** §4.2 item 2, L262-270. The heartbeat is a `TimeProvider.CreateTimer` that beats once a second and
  stops only in `DisposeAsync`. The plan does not cover a failed start.
- **Shipped:** `OtlpTap.StartAsync` creates the watch (`OtlpTap.cs:222-223`) before `Bind()` (L240).
  `TcpTap.StartAsync` creates it (`TcpTap.cs:219-220`) before `ResolveBindAddress` and `listener.Start` (L222-224).
  If the port is taken, `StartAsync` throws with the timer already running. The system timer stays rooted while it is
  scheduled, so it keeps beating, and holding the tap through its log closure, until the tap is disposed. A host that
  does not dispose a tap whose start threw leaks it. `ProxyTap` creates its watch after `_listener.Start()`
  (`ProxyTap.cs:264-267`) and does not have the problem. R2 and R3 introduced it: before them there was no timer.

---

## F3: unguarded `Log` calls

None. Every `_options.Log?.Invoke` in `src/Kronikol.Extensions.ProxyTap`, `src/Kronikol.Extensions.Otlp` and
`src/Kronikol.Extensions.TcpTap` is inside a `TryLog` body: `ProxyTap.cs:226`, `OtlpTap.cs:878`, `TcpTap.cs:951`,
`OtlpExporter.cs:176` and `OtlpExportSink.cs:358`. The other ways a line reaches `Log` all go through `TryLog` too:

- `StallWatch` and `TapAcceptLoop` take their `_log` from the taps as `line => TryLog(...)` (`ProxyTap.cs:267`;
  `OtlpTap.cs:76`, `:223`; `TcpTap.cs:86-88`, `:220`).
- Decoders log through `TcpTapConnectionContext.Log` (`TcpTap.cs:1112`), then `TcpTap.Diagnostic` (`TcpTap.cs:941`),
  then `TryLog`.
- `RedisTap`, `MongoTap`, the registration extensions and the hosted services make no `Log` calls.

## Not verified

- The Kronikol4J ledger lines (`68a67b8`, `323c3a0`, `910a6bb`, `ca35e6c`, `6f5ec06`) are outside the folders this
  audit may read.

Items checked: 181. That is 12 R0 items, 20 R1-R3 requirements, 9 watch-design rules (§4.1, §4.2), 44 §5 test ids,
22 planned mutations, 16 docs and records items (wiki bullets, 5 changelog entries, PLANS_STATUS, ROADMAP), 42
Appendix A rows, the 3 packages for F3, and the 13 §9 decisions.
