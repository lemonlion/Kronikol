# E: published claims vs shipped code, taps' time limits (#173), 4.22.2 to 4.26.1

Code: `C:/Code/Kronikol-173-audit` at 09369515 (origin/main). Wiki: `C:/Code/Kronikol.wiki` origin/master at 17d42b6.
Paths below are relative to the worktree unless they name the wiki.

Sources read in full: CHANGELOG.md 4.22.2, 4.23.0, 4.25.0, 4.26.0, 4.26.1; wiki Integration-ProxyTap-Extension.md,
Integration-Otlp-Extension.md, Integration-TcpTap-Extension.md, and the CaptureDegraded row and host-diagnostics section of
Diagnostics-and-Debugging.md; the XML docs of every public member the releases added or changed. Code read in full:
ProxyTap.cs, ProxyTapOptions.cs, ProxyTapServiceCollectionExtensions.cs, OtlpTap.cs, OtlpTapOptions.cs,
OtlpTapServiceCollectionExtensions.cs, HttpRequestReader.cs, StallWatch.cs, TcpTap.cs, TcpTapOptions.cs,
TcpTapServiceCollectionExtensions.cs, CaptureDegradation.cs, DiagnosticEntry.cs, and the decoders' OnConnectionClosed.

Found true (no entry): every default (ProxyTap 100 s; Otlp 100 s and 120 s; TcpTap 100 s and 100 s; reaper off); null and
Timeout.InfiniteTimeSpan accepted and zero or negative refused for the five new options; the phases timed (ProxyTap body
and answer only, never the head, the forward or an idle connection; Otlp keep-alive wait, head plus body from the first
byte, answer, never the forward; TcpTap a pending write, and silence after the first pump ends); 16 KiB pieces (ProxyTap
answer's last piece 16 to 32 KiB, about 164 B/s); ProxyTap 408 then close on Linux/macOS and a reset on Windows, the
request neither forwarded nor recorded, an answer cut recorded and its in-flight identity released; 4.26.1's count before
the abort; Otlp 408 with `Connection: close` under a 5 s bound and a close in stages, an over-cap drain or chunk-framing
stall as a 408 not in PayloadsRejected, keep-alive and disposal closes uncounted; the debugger rule (the reaper alone acts
under one); the reaper on the heartbeat and monotonic clock, its event on a thread-pool thread; the TcpTap log lines and
both new counters' Diagnostics lines; Otlp and TcpTap stop lines; every registration copy (ProxyTap Copy, Otlp Copy,
TcpTap CopyTo and the Redis/Mongo overrides); all 4.22.2 fixes (secret checked from the head, buffer grown as bytes
arrive, Content-Length digits only, both framings, drain cut short, stop line, Log guards, ForwardTimeout/ConnectTimeout
ranges, negative caps, DisposeAsync settling each exchange by phase).

---

## E1 (medium): a pair a time limit closes does raise OnCaptureDegraded, and does lose capture, with the shipped decoders

Sources:
- CHANGELOG.md:59-60 (4.26.0): "A pair a limit closes has lost none of its capture, so no `OnCaptureDegraded` event is
  raised for it."
- CHANGELOG.md:24-26 (4.26.1): "`TcpTapStallTests.A_pair_a_limit_closes_raises_no_capture_degradation` covers a sentence
  of 4.26.0's changelog that no fact checked, that a pair a limit closes raises no `OnCaptureDegraded` event"
- Wiki Integration-TcpTap-Extension.md:302-303 (Time limits): "A pair a limit closes has lost none of its capture, so no
  `OnCaptureDegraded` event is raised for it."
- XML doc on `CaptureDegradationKind.StuckConnectionReaped`, src/Kronikol.Extensions.TcpTap/CaptureDegradation.cs:27-29:
  "the other two, HalfClosedTimeout and StalledWriteTimeout, are counted but not reported here, since a pair they close
  has lost none of its capture."

Code: every pair end, including one `PairLimits.Close` caused, completes the decode queue and calls
`decoder.OnConnectionClosed()` (src/Kronikol.Extensions.TcpTap/TcpTap.cs:322-327, 652-653).
`RedisProtocolDecoder.OnConnectionClosed` (Protocols/RedisProtocolDecoder.cs:188-205) and
`MongoProtocolDecoder.OnConnectionClosed` (Protocols/MongoProtocolDecoder.cs:198-215) call `ReportClosedMidMessage` when a
command is unanswered or a command or reply is partly received. That is `TcpTap.CountClosedMidMessage` (TcpTap.cs:742-747):
`ConnectionsClosedMidMessage` + 1, a Log line "closed mid-message (...); its last interaction(s) were not recorded",
`OnCaptureDegraded(ConnectionClosedMidMessage)`, and a Diagnostics() line "closed mid-message — their last command(s) were
not recorded" (TcpTap.cs:800-803).

StalledWriteTimeout's two documented cases always leave that state: a client that stops reading a large reply leaves its
command pending with a partial reply (the Redis decoder dequeues a command only on a whole reply,
RedisProtocolDecoder.cs:345-348), and a server that stops reading leaves the commands already queued for decoding (the
pump queues before it writes, TcpTap.cs:478-487) unanswered. A half-closed pair whose server never answered the last
command does the same. So with RedisTap or MongoTap such a close raises an event and the last interaction is not
recorded. The 4.26.1 fact (tests/Kronikol.Tests.TcpTap/TcpTapStallTests.cs:64-87) and every other stall fact run on
`OneCommandDecoder` (TcpTapStallTests.cs:490), whose `OnConnectionClosed()` is empty (TcpTapStallTests.cs:586), so none
can see it. What does hold: no event kind exists for the limit's close itself, and a half-closed pair with every command
answered raises nothing.

## E2 (low): ProxyTap's body-stall log line on Windows is not the documented one

Sources: CHANGELOG.md:285-286 (4.23.0): "Each cut is logged (`proxy-tap caller stalled 00:01:40 sending its request body;
not forwarded, answered 408 and closed`, or `... reading its answer; closed`)." Wiki Integration-ProxyTap-Extension.md:111-112
gives the same example with no Windows form.

Code: src/Kronikol.Extensions.ProxyTap/ProxyTap.cs:399 ends the line with
`OperatingSystem.IsWindows() ? "connection reset" : "answered 408 and closed"`, so on Windows it reads
"... sending its request body; not forwarded, connection reset". The only test of the line
(tests/Kronikol.Tests.ProxyTap/ProxyTapStallTests.cs:88) checks the common prefix. A host matching the documented text
misses every Windows cut.

## E3 (low): "Timeout.InfiniteTimeSpan turns a limit off" fails for the reaper the same page counts as one of three limits

Source: wiki Integration-TcpTap-Extension.md:304-305: "No limit fires while a debugger is attached to the tap's process;
the reaper still acts, as it always has. `null` or `Timeout.InfiniteTimeSpan` turns a limit off, and zero or a negative
value fails at start." The options row (line 152) calls `ReapStuckConnectionsAfter` "one of three limits that close a
connection (see [Time limits](#time-limits))", as does its XML doc (TcpTapOptions.cs:184-186).

Code: src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs:267-268 refuses `ReapStuckConnectionsAfter <= TimeSpan.Zero`, which
includes `Timeout.InfiniteTimeSpan` (-1 ms): `ArgumentOutOfRangeException` "must be positive, or null to disable the
reaper" at StartAsync or registration. Only `HalfClosedTimeout` and `StalledWriteTimeout` take it (TcpTapOptions.cs:224-226,
269-272). It fails fast with a clear message, hence low.

## E4 (low): 4.26.1 names a member that does not exist

Source: CHANGELOG.md:20: "`DiagnosticEntry.CaptureDegraded`'s summary says a tap also closes a connection ..."

Code: the member is `DiagnosticKind.CaptureDegraded` (src/Kronikol/Reports/DiagnosticEntry.cs:9 and 48);
`DiagnosticEntry` is the record at DiagnosticEntry.cs:131 and has no such member. (The task brief uses the same name.)

## E5 (low): doc comments of changed public members left out of date or incomplete

- `OtlpTap.Diagnostics()` (src/Kronikol.Extensions.Otlp/OtlpTap.cs:167-175) lists its entries as "accepts that failed,
  payloads dropped, rejected or failed, exports refused as unauthenticated, forwards that failed"; since 4.25.0 it also
  returns the CallerTimeouts line (OtlpTap.cs:209-213).
- `TcpTap.Diagnostics()` (src/Kronikol.Extensions.TcpTap/TcpTap.cs:749-757) lists "... decoder resets, dropped segments,
  connections closed mid-message" and omits the reaper's line and 4.26.0's two new lines (TcpTap.cs:805-818), which the
  4.26.0 changelog describes as "beside the reaper's".
- `TcpTapOptions.HalfClosedTimeout` and `StalledWriteTimeout` (TcpTapOptions.cs:203-221) do not say that zero or a negative
  value is refused at start (TcpTapOptions.cs:269-272), which the changelog (CHANGELOG.md:52), the wiki and their twins'
  docs say (ProxyTapOptions.cs:153-154; OtlpTapOptions.cs:73-74 and 87-88).

## E6 (low): release headers say any byte keeps a caller alive; the code needs a whole piece per limit

Sources: CHANGELOG.md:272-273 (4.23.0): "a caller that moves bytes at least once a limit is never cut."
CHANGELOG.md:39 (4.26.0): "a pair whose sides keep moving bytes, or that sits idle, is never closed."
CHANGELOG.md:112 (4.25.0): "an exporter that moves bytes at least once a limit is never cut."

Code: progress is stamped only when a whole read or write completes. ProxyTap reads the body in 16 KiB pieces and writes
the answer in 16 to 32 KiB pieces (ProxyTap.cs:129-131, 369-381, 1049-1055, 1075), and by its own comment a body read on
Windows, and a chunked one on Linux, "returns only once its buffer is full", so there a caller sending a few bytes every
minute is cut after 100 s. TcpTap watches each whole write of up to `ReadBufferBytes` (TcpTap.cs:484-500), so a reader
taking less than 32 KiB per limit once the buffers are full is closed. OtlpTap's answer progresses per 16 KiB piece
(OtlpTap.cs:650-656); its reads do progress per byte. The releases' own bullets and the wiki state the real rule
(Integration-ProxyTap-Extension.md:103-106: "a caller has to move one piece per limit, about 164 bytes a second";
CHANGELOG.md:51; Integration-TcpTap-Extension.md:297), so each header contradicts its own details.

## E7 (low): OtlpTap can receive an export and close its connection unanswered when it arrives as KeepAliveTimeout fires

Sources: CHANGELOG.md:123-125 (4.25.0): a connection waiting for a request's first byte "is closed after this long, with
nothing written and nothing counted"; wiki Integration-Otlp-Extension.md:152-153 the same; CHANGELOG.md:153-154 (4.25.0
tests): "a transfer completing as the limit fires is received or cut, never both".

Code: the reader reports the first byte through `HeadStarted` (HttpRequestReader.cs:281-285), wired as
`HeadStarted = () => limits.Next(CutPhase.Sending)` (src/Kronikol.Extensions.Otlp/OtlpTap.cs:342), which drops Next's
result. If the keep-alive entry fired first, Next's `End()` loses its move, cuts with KeptAlive (cancelling the
connection's token) and leaves `_entry` null without starting the export's watch (OtlpTap.cs:828-853). When the whole
request already sits in the reader's 16 KiB buffer, neither the head nor the body needs a socket read, and the next
`limits.End()` (OtlpTap.cs:368) returns true because `_entry` is null. HandleAsync then counts `RequestsReceived` (or
`UnauthenticatedRequests` on the refusal path, line 355) and queues the payload for mapping (OtlpTap.cs:501-515) before its
answer write or forward fails on the cancelled token, and the catch at OtlpTap.cs:378-385 takes that as the keep-alive cut:
closed, nothing written, nothing logged. The spans reach the report while the exporter sees a dropped connection and may
send them again. The window is between the heartbeat's `TryFire` and the action's `Cancel` (the read's own progress stamp
prevents the fire otherwise), so it is rare.

## E8 (low): the exposure paragraph overstates what the new limits bound

Source: wiki Integration-Otlp-Extension.md:188: "`MaxRequestBytes`, `QueueCapacity` and, since 4.25.0,
`CallerStallTimeout` and `KeepAliveTimeout` bound the rest."

Code: both limits time silence only. Every socket read that returns a byte restarts the export's wait
(HttpRequestReader.cs:452-459, OtlpTap.cs:843); a head may be 64 KiB (HttpRequestReader.cs:75) and is read before
`ExpectedHeaders` is checked (OtlpTap.cs:348-353); nothing caps a head's total time or the number of connections. A caller
without the secret that sends one byte every 99 s keeps its connection (64 KiB of head at that rate is about 75 days), and
can open as many as it likes. In a paragraph about a port others can reach, the limits bound idle and silent connections,
not a slow sender.

## E9 (low): the ProxyTap chunked-body limitation contradicts the stall section

Source: wiki Integration-ProxyTap-Extension.md:275-277: "On Linux and macOS a **chunked** request whose caller stops
sending before the last chunk reaches the service as if whole". The same page, lines 90-91: "One that stops sending
without hanging up is cut after `CallerStallTimeout`".

Code: a caller that stalls mid-chunked-body is cut by the body watch and never forwarded, even when the abort ends the read
as if the body were whole (ProxyTap.cs:423-452: `reading.TryEnd()` lost, `CutStalled`, return). Only a caller that
half-closes (a FIN) before the last chunk is forwarded as if whole. The bullet dates from 4.19.2 and 4.23.0's edit left it;
it should say "half-closes" or "hangs up".

## E10 (low): the TcpTap page says a tap closes a connection only for a time limit

Source: wiki Integration-TcpTap-Extension.md:37 (edited for 4.26.0): "A tap closes a connection only for a time limit: a
command left unanswered (`ReapStuckConnectionsAfter`, off by default) and, since 4.26.0, a half-closed pair that goes
silent or a side that stops reading".

Code: a connect to the real server that is refused or fails also closes the caller's connection (TcpTap.cs:289-290
returns, and the finally at TcpTap.cs:335-341 disposes the client), as the same page's ConnectTimeout row (line 151) says.
A refusal is not a time limit.

## E11 (low; pre-existing, outside the members asked about): TcpTap's class remarks state the old pump order

Source: XML remarks on `TcpTap`, src/Kronikol.Extensions.TcpTap/TcpTap.cs:21-23: "Each pump writes the bytes downstream
before it tries to enqueue the copy".

Code: `PumpAsync` queues the copy first and writes after (TcpTap.cs:477-487), as its own summary (TcpTap.cs:448-454) and the
wiki (Integration-TcpTap-Extension.md:32, "Before 3.32.1 each pump wrote downstream first") say.
