# TcpTap read for the caller-side time limits plan (#173)

Read at **`3240c983`** (worktree `C:/Code/Kronikol-173`, branch `plan/173-tap-caller-time-limits`, packages at 4.20.2).
Nothing was built or run. Every statement is READ from the source unless marked **INFERRED** (reasoned about OS, server
or client-library behaviour, not measured here). Short paths:

- `TcpTap.cs` = `src/Kronikol.Extensions.TcpTap/TcpTap.cs`
- `Options.cs` = `src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs`
- `Redis.cs` / `Mongo.cs` = `src/Kronikol.Extensions.TcpTap/Protocols/RedisProtocolDecoder.cs` / `MongoProtocolDecoder.cs`
- `Resp.cs` = `src/Kronikol.Extensions.TcpTap/Protocols/RespStreamParser.cs`
- `Tests.cs` = `tests/Kronikol.Tests.TcpTap/TcpTapTests.cs`, `Support.cs` = `tests/Kronikol.Tests.TcpTap/TestSupport.cs`

Scope split: #174 owns the accept and connect path (TcpTap.cs:181-216 and the catch at 247-250). This note describes it
only in passing; everything after both sockets are open is #173's.

## 0. The facts that shape the design

1. **A pair has one release point.** `await Task.WhenAll(toServer, toClient)` (TcpTap.cs:241); the `finally` that
   disposes both sockets (255-261) runs only after both pumps end, the decode task drains (244) and, with the reaper on,
   the reaper's next 1 s tick (245). When one pump ends, nothing is armed for the other: no per-pair token, no timer,
   no record of which pump ended or when.
2. **The only per-pair ways out today** are a peer's action (FIN, RST, or a send that fails), the opt-in reaper (default
   off), and `DisposeAsync`, which cancels the tap-wide token that every pump reads and writes with (698-699, 283, 299).
3. **The reaper only reads the decoder.** It acts when a *decoded command* has been unanswered for the threshold
   (360-369). It never acts on an idle pair, on a half-closed pair with nothing unanswered, on a stalled partial
   command, or on a connection that dropped a segment or lost decoding (345-348).
4. **Idle is normal and must stay untouched by default.** The option doc (Options.cs:163-167) and the 3.0.49 changelog
   (CHANGELOG.md:8451) record that a first, silence-based reaper killed healthy idle pool connections.
5. **Back-pressure has no bound.** A pump parked in `destination.WriteAsync` (299) waits for as long as the reader
   does not read. It stops reading its source meanwhile, which is correct TCP transparency, but unbounded in time.
6. **Dead state is already there.** `ConnectionPumpState.LastActivityTicks` (731) is written after every completed
   write (311) and read nowhere. It has been dead since 3.0.49 (398655f4). It is shared by both directions and does not
   move while a write is stuck.
7. **Only `NoDelay` is set** on either socket (209-210). There is no keep-alive, linger, buffer size or
   Receive/SendTimeout. The timeouts don't apply to async I/O anyway (INFERRED, .NET docs).
8. **Tests run on real loopback sockets and the real clock.** There is no `TimeProvider` in `src/`. The reaper's tick
   is a literal (340). The existing stub server always closes after a half-close (Support.cs:339-343), so no fact
   models a silent upstream. `LiveConnections` (88) is asserted nowhere.

## 1. Accept loop and per-pair task (briefly; #174's)

- `StartAsync` (TcpTap.cs:149-165) validates (152) and calls `listener.Start(AcceptBacklog)` (156). It links the tap's
  lifetime token to the **StartAsync caller's token** (`_cts = CreateLinkedTokenSource(cancellationToken)`, 160), and
  `TcpTapHostedService.StartAsync` passes the host's startup token through
  (TcpTapServiceCollectionExtensions.cs:105). A caller that passes a token it later cancels ends the accept loop and
  every pump, the same as disposal.
- `AcceptLoopAsync` (181-199) awaits `AcceptTcpClientAsync(ct)` (189). It **returns for good on any
  `SocketException`** (191-193), while `IsListening` (82) keeps reporting true. That belongs to #174. It counts the
  connection (196) and starts `HandleConnectionAsync` fire-and-forget with `Task.Run` (197). There is no registry of
  live pairs, only the `_live` counter.
- `HandleConnectionAsync` (201-262) increments `_live` *before* the connect (203). It sets `NoDelay` (209-210) and
  connects with a linked token plus `CancelAfter(ConnectTimeout)` (212-216). A connect timeout surfaces as an
  `OperationCanceledException` into the silent "Shutting down" catch (247-250). That is #174's.
- After the connect, still inside the same `try`: the context and the decoder are built (218-219). A factory that
  throws lands in the catch at 251-254 (one log line) and the `finally` closes the just-opened pair (see §9 F2). Then
  come the channel (221-227), the pump state (229), the decode task (230), both pumps (232-235) and the reaper (237-239).

## 2. The two pumps

### 2.1 Wiring (TcpTap.cs:232-235)

```csharp
var toServer = PumpAsync(clientStream, upstreamStream, upstream.Client, TapDirection.ClientToServer, channel.Writer, pumpState, ct);
var toClient = PumpAsync(upstreamStream, clientStream, client.Client, TapDirection.ServerToClient, channel.Writer, pumpState, ct);
```

`ct` is the tap-wide token from `AcceptLoopAsync`. There is no per-pair `CancellationTokenSource`. `PumpAsync` is
`internal` (271) and is called directly, without `Task.Run`, so each pump runs synchronously up to its first pending read.

### 2.2 Loop body (TcpTap.cs:271-321)

- Buffer: `ArrayPool<byte>.Shared.Rent(ReadBufferBytes)` (275), 32 KiB by default (Options.cs:154). It is returned in
  the `finally` (316). Each read is capped at `ReadBufferBytes` (283), even when the pool hands back a bigger array.
- Read: `await source.ReadAsync(buffer.AsMemory(0, ReadBufferBytes), ct)` (283). The token is the tap token and there
  is no deadline.
- Read failure: `IOException`, `ObjectDisposedException`, `SocketException` or `OperationCanceledException` leads to
  `break` (285-288), with no log and no count.
- EOF: `read == 0` leads to `break` (290-291). EOF and a read error are not told apart, and neither is recorded.
- Copy and queue **before** forwarding (3.32.1, CHANGELOG.md:4139): `buffer.AsSpan(0, read).ToArray()` (293), then
  `writer.TryWrite(new TapSegment(direction, copy, DateTimeOffset.UtcNow))`. When that fails, `CountDrop` runs
  (294-295). `TryWrite` on the bounded channel (221-227, `FullMode = Wait`, `SingleReader`) never blocks; it returns
  false when the channel is full.
- Write: `await destination.WriteAsync(buffer.AsMemory(0, read), ct)` (299). The token is the tap token and there is no
  deadline. Failure on the same four exception types leads to `break` (301-304), again with no log and no count.
- After a completed write: the tap-wide byte counters (306-309), `_bytesSinceLastInteraction` (310) and
  `state.LastActivityTicks = Environment.TickCount64` (311). None of these moves while a write is parked.

### 2.3 What ends a pump, and what it does on the way out (TcpTap.cs:314-320)

```csharp
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
    // Half-close: tell the far side this direction is done, but keep the other one pumping.
    try { destinationSocket.Shutdown(SocketShutdown.Send); }
    catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { /* already gone */ }
}
```

- **EOF from the source** sends a FIN to the destination. The pair is now half-closed and the other pump runs on.
- **A read error from the source** (for example an RST) does the same: the destination gets a graceful FIN, not a
  reset. The source socket is left alone.
- **A write error to the destination** calls `Shutdown(Send)` on that same broken destination. The source is not shut
  down or closed, so its peer learns nothing until the other pump ends too.
- Any **other** exception (for example from `CountDrop`'s unguarded `Log` call, §9 F3) escapes `PumpAsync` after the
  `finally`.

### 2.4 Pair release (TcpTap.cs:241-261)

```csharp
await Task.WhenAll(toServer, toClient).ConfigureAwait(false);   // 241
Volatile.Write(ref pumpState.Closed, 1);                         // 242
channel.Writer.TryComplete();                                    // 243
await decodeTask.ConfigureAwait(false);                          // 244
await reaper.ConfigureAwait(false);                              // 245
...
finally
{
    try { decoder?.Dispose(); } catch (Exception ex) { _options.Log?.Invoke(...); }  // 257
    upstream?.Dispose();                                                             // 258
    client.Dispose();                                                                // 259
    Interlocked.Decrement(ref _live);                                                // 260
}
```

- The `finally` runs after **both** pumps have ended (`Task.WhenAll`), the decode task has drained the channel and
  called `OnConnectionClosed` (424-425), and the reaper task has returned.
- With the reaper on, its `Task.Delay(1_000)` (340) takes no token, so the release (sockets disposed, `_live`
  decremented) lags up to about 1 s behind the second pump's end. At disposal that fits inside `DrainTimeout` (2 s).
- An unexpected exception out of `WhenAll` skips 242-245. `Closed` then never becomes 1, so a reaper keeps ticking
  for ever, and the channel is never completed, so the decode task waits for ever. The sockets are still disposed by
  the `finally` (§9 F3).
- `ConnectionPumpState` (724-746) holds the following fields. `LastDropReportTicks` (728) throttles drop reports.
  `LastActivityTicks` (731) is written and never read. `Closed` (734) is set only when both pumps end, and only the
  reaper reads it. `DecodingBroken` (737) and `DecodeViewIncomplete` (745) are never cleared once set.

### 2.5 Decode task (TcpTap.cs:395-433)

`ReadAllAsync(CancellationToken.None)` (400) waits on the channel. A decoder that gives up keeps draining (402-403) and
sets `DecodingBroken` (419). The task ends only when the channel is completed (243), and it then calls
`decoder.OnConnectionClosed()` (424-425). The decoder therefore never learns of a half-close: it sees EOF only once both
directions have ended. An outer failure sets `DecodingBroken` and disables decoding (427-432).

## 3. The stuck-connection reaper (TcpTap.cs:323-371)

- **Armed** per connection at connect time, only when `ReapStuckConnectionsAfter` is non-null (237-239). The default
  is **null, so it is off** (Options.cs:173, doc at 156-172). Validation: the value must be positive or null
  (Options.cs:210-211). There is no lower bound tied to the tick and no fact asserts the rejection.
- **Tick:** `while (Closed == 0) { await Task.Delay(1_000); ... }` (338-340). The 1 s is fixed, takes no
  cancellation token and is not configurable.
- **Stand-down conditions** (345-348): `Closed != 0`, `DecodingBroken != 0` or `DecodeViewIncomplete != 0` make it
  `return`, which stops reaping that connection for good. Reading `decoder.OldestUnansweredSince` and getting an
  exception also makes it `return` (350-358).
- **Trigger** (360-361): `since is not { } oldest || DateTimeOffset.UtcNow - oldest < threshold` leads to `continue`.
  The clock is the **wall clock** (the segment timestamps come from `DateTimeOffset.UtcNow` at 294), not the
  monotonic `TickCount64` used at 311 and 386.
- **Action** (363-369): it increments `_stuckConnectionsReaped` and builds the detail
  `"a command sent at {oldest:HH:mm:ss}Z is still unanswered after {threshold} — closed so a wedged client can reconnect"`.
  It writes a `Log` line `"[{DisplayName}] tcp-tap connection {id} reaped: {detail}"` and calls
  `Degrade(id, StuckConnectionReaped, detail)`. Then it runs `client.Dispose()` and `upstream.Dispose()`, each in its
  own try/catch, and returns.
  - The default `LingerState` is untouched, so `Dispose` closes gracefully: a FIN, or an RST when unread bytes remain
    (INFERRED; the #172 plan measured zero-linger `Dispose` as a FIN then an RST).
  - The parked pumps then fail with `ObjectDisposedException` or `IOException`, break, and the pair releases.
  - Because the unanswered command is still pending when `OnConnectionClosed` runs, **every reap is also counted as
    closed mid-message** (Redis.cs:194-204, Mongo.cs:202-212, then TcpTap.cs:514-519). That gives two `Degrade`
    events and two `Diagnostics()` lines per reap.
- **Idle pair, nothing unanswered:** `since` is null, so the loop continues for ever. It **never acts**.
- **Half-closed pair:** the reaper does not know about half-close, since `Closed` means *both* pumps ended. It acts
  only if a decoded command is still unanswered and the decode view is intact.
- **What feeds it** (`OldestUnansweredSince`, IProtocolDecoder.cs:48-55, default null, so a custom decoder never reaps):
  - **Redis**: the head of the FIFO (Redis.cs:111-122). Every command takes a slot, **excluded ones included** (PING,
    CLIENT and the rest; 320-324). Pushes never answer (338-343). A reply with nothing pending is ignored (345-346). The
    timestamp is that of the segment that **completed** the command (155, 295). A reply completes only after its last
    byte and trailer (Resp.cs:191-203), so a long reply keeps its command pending for the whole transfer. More than
    4,096 pending is a recoverable reset that clears the queue (326-333).
  - **Mongo**: the minimum timestamp over the pending map (Mongo.cs:73-85). **Excluded commands never become pending**:
    `hello`/`isMaster` and the rest, and `getMore` unless `TrackGetMore` (287-288, 387-396). `moreToCome` (w:0) writes
    are recorded at once (296-301). A reply over `MaxBufferedBytes` clears its command at the header (155-166).
- **False positives the decoders don't know about** (READ that there is no list; INFERRED semantics). These matter if
  the plan reuses "unanswered" as a signal.
  - Redis blocking commands: BLPOP, BRPOP, BLMOVE, BLMPOP, BZPOPMIN/MAX, BZMPOP, XREAD and XREADGROUP with BLOCK, WAIT
    and WAITAOF. A search for them in `src/Kronikol.Extensions.TcpTap` finds nothing.
  - `CLIENT REPLY OFF|SKIP`.
  - A big Redis reply read slowly by its client: the age is measured from the command's time.
  - Mongo with `TrackGetMore = true` and awaitData or tailable `getMore` (change streams).
  - **Decode lag**: the reaper reads the decoder's view, which trails the wire by the queue (up to 1,024 segments).
    A slow `Sink` or `IdentityResolver` delays the dequeue of a command that was in fact answered.
- History: added in 3.0.49 (CHANGELOG.md:8451). It stands down after a dropped segment since 3.4.1 (7652). The pump
  order that made it reap healthy connections was fixed in 3.32.1 (4139).

### Capture drops (TcpTap.cs:373-393)

`CountDrop` sets `DecodeViewIncomplete = 1` first (377), permanently, so the reaper stands down for the life of the
connection. It then increments the per-direction counter (379-381). The first drop *in each direction* tap-wide writes a
`Log` line, and that call is not guarded (382-383). A `Degrade(SegmentsDropped)` is sent at most once per connection
per minute, throttled by a CAS on `LastDropReportTicks` (386-392; `DropReportInterval` at 43). A time limit built on
socket state rather than the decoder's view would not need to stand down after a drop.

## 4. Options

### 4.1 `TcpTapOptions` (Options.cs)

| Option | Line | Default | Validated (Validate, 188-214) |
| --- | --- | --- | --- |
| ListenPort | 32 | 0 (= ephemeral) | 0..65535 (190-191) |
| ListenHost | 35 | `localhost` | not blank (194-195) |
| ForwardHost | 38 | `localhost` | not blank (196-197) |
| ForwardPort | 41 | 0 (Redis 6379, Mongo 27017 via ctor) | 1..65535 (192-193) |
| CallerName | 44 | `""` | required (198-199) |
| ServiceName | 47 | `""` (Redis `redis`, Mongo `mongo`) | required (200-201) |
| DependencyCategory | 50 | null (set by Redis/Mongo) | no |
| CallerDependencyCategory | 53 | null | no |
| Name | 56 | null | no |
| Log | 59 | null | no |
| Sink | 62 | `RequestResponseLoggerSink.Instance` | no |
| Phase | 65 | `TestPhase.Unknown` | no |
| FallbackTestName | 72 | `TestIdentityScope.UnknownTestName` | no |
| FallbackTestId | 79 | `TestIdentityScope.UnknownTestId` | no |
| IdentityResolver | 87 | null | no |
| EmitActivities | 90 | true | no |
| Verbosity | 93 | `Detailed` | no |
| BodyCapBytes | 96 | 65536 (null = unlimited) | **no** (negative breaks it, §9 F1) |
| CaptureReplies | 99 | true | no |
| AcceptBacklog | 102 | 128 | no |
| ConnectTimeout | 105 | 5 s | **no** (#174) |
| DrainTimeout | 108 | 2 s | no (negative is clamped to 0 at TcpTap.cs:710) |
| ChannelCapacity | 115 | 1024 | > 0 (202-203) |
| MaxBufferedBytes | 124 | 8 MiB | > 0 (206-207) |
| ResyncAfterOverflow | 134 | true | no |
| OnCaptureDegraded | 143 | null | no |
| DecodingStallBytes | 151 | 1 MiB (null = off) | positive or null (208-209) |
| ReadBufferBytes | 154 | 32 KiB | > 0 (204-205) |
| ReapStuckConnectionsAfter | 173 | **null (off)** | positive or null (210-211) |
| KeyRedaction | 176 | null | no |
| ValueRedaction | 179 | null | no |
| DecoderFactory | 182 | null (set by Redis/Mongo ctor) | required (212-213) |
| DisplayName (get-only) | 185 | `Name ?? $"{CallerName}→{ServiceName}"` | n/a |

The class remarks (Options.cs:24-27) say it "mirrors `ProxyTapOptions` option-for-option where the two taps mean the
same thing". `ProxyTapOptions` today has `ForwardTimeout` (200 s, ProxyTapOptions.cs:139) and `ConnectTimeout` (5 s,
142). Any new name should line up with whatever #173 gives the proxy tap.

### 4.2 `RedisTapOptions` (RedisTapOptions.cs)

- The ctor (13-19) sets `ServiceName`, `ForwardPort`, `DependencyCategory` and `DecoderFactory`.
- `ExcludedCommands` (get-only set, 33-37) holds PING, CLIENT, CONFIG, INFO, ECHO, HELLO, SELECT, AUTH, COMMAND,
  CLUSTER, SENTINEL, the (S/P)(UN)SUBSCRIBE verbs and QUIT. These still occupy pending slots.
- `ExcludedKeyPrefixes` (44) defaults to `__Booksleeve_` and `__redis__`.
- `DefaultDatabase` (47) defaults to 0 and is not validated. `CapturePubSub` (54) defaults to true.
- `MaxBulkBytes` (65) defaults to null and must be >= 0 or null (`Validate` at 71-76).
  `EffectiveMaxBulkBytes` (68) is computed.
- `CopyTo` (79-91) copies all five.

### 4.3 `MongoTapOptions` (MongoTapOptions.cs)

- The ctor (13-19) sets the same four as Redis.
- `ExcludedCommands` (31-36, 17 names including hello/isMaster/ping/saslStart).
- `TrackGetMore` (39) false, `LogFilterText` (42) true, `LogResponseContent` (49) true.
- `MaxResponseDocuments` (55) 10 and `MaxRequestDocuments` (62) 10.
- `DocumentRedaction` (65) null.
- There is **no `Validate` override**, so the document maxima are unchecked.
- `CopyTo` (68-81) copies all seven.

### 4.4 DI and the copy

- `AddRedisTapTestTracking(Action)` (TcpTapServiceCollectionExtensions.cs:14-28), `AddMongoTapTestTracking(Action)`
  (48-62) and `AddTcpTapTestTracking(Action)` (82-95) each build the options, configure them, `Validate()` (21, 55, 89),
  construct the tap and register a `TcpTapHostedService`.
- The list overloads (31-41, 65-75) call `captured.CopyTo(target)` (37, 71). The generic tap has no list overload.
- `TcpTapHostedService.StopAsync` ignores its token and awaits `DisposeAsync` (108).
- `TcpTapOptions.CopyTo` (Options.cs:217-252) assigns all 32 settable properties (220-251). A new option must be added
  there by hand.
- **No fact checks that every option is copied.**
  - `Tests.cs:636-669` (`CopyToCarriesEveryProtocolOption`) asserts the seven Mongo options plus ListenPort, ForwardPort,
    CallerName and Verbosity.
  - `RedisDecoderTests.cs:435-456` asserts MaxBulkBytes, ResyncAfterOverflow, OnCaptureDegraded and DecodingStallBytes.
  - ReapStuckConnectionsAfter, ConnectTimeout, DrainTimeout, ReadBufferBytes, ChannelCapacity and Redis's sets,
    DefaultDatabase and CapturePubSub are never asserted.
  - The proxy tap has the reflection-based fact the TcpTap lacks:
    `ProxyTapRegistrationTests.Registering_taps_from_a_list_copies_every_option`
    (tests/Kronikol.Tests.ProxyTap/ProxyTapRegistrationTests.cs:35).

## 5. Counters, `Diagnostics()`, log lines, degradation kinds

**Public counters** (TcpTap.cs):

- ConnectionsAccepted (85), **LiveConnections** (88, asserted by no test), InteractionsCaptured (91), DecodeErrors (97)
- SegmentsDropped (100), split as ClientToServer (103) and ServerToClient (106)
- BytesClientToServer (109), BytesServerToClient (112)
- OversizePayloadsSkipped (115), BytesSkipped (118), LargestOversizePayload (121)
- DecoderResets (124), DecodingDisabledConnections (127), ConnectionsClosedMidMessage (130)
- **StuckConnectionsReaped** (133), LastInteractionAt (136-143), BytesSinceLastInteraction (146)

None counts connect failures (#174), pairs held, half-closes, or pairs closed by EOF versus error.

**`Diagnostics()`** (529-580): one `DiagnosticKind.CaptureDegraded` entry per non-zero counter, in this order.

| Lines | Entry |
| --- | --- |
| 534-543 | decoding disabled `on N connection(s) — {ServiceName} arrows on them{ after HH:mm:ssZ} are missing ({reason})` |
| 545-548 | oversize payloads |
| 550-553 | decoder resets |
| 555-558 | dropped segments, by direction |
| 560-563 | `N connection(s) closed mid-message — their last command(s) were not recorded` |
| **565-568** | `{name}: {reaped:N0} stuck connection(s) reaped (a command unanswered for {ReapStuckConnectionsAfter}) so a wedged client could reconnect` |
| 570-577 | the stall heuristic, from `BytesSinceLastInteraction` and `DecodingStallBytes` |

**Connection-level `Log` lines** (all unguarded, §9 F3):

- connection error (253)
- decoder dispose failed (257)
- reaped (365)
- first drop per direction (383)
- decoder reset (447-449)
- decoding disabled (478-480)
- `OnCaptureDegraded` threw (495)
- closed mid-message (517)
- the stop summary (715-718), which omits StuckConnectionsReaped, ConnectionsClosedMidMessage and any pair still live

**`CaptureDegradationKind`** (CaptureDegradation.cs:4-30): OversizePayloadSkipped, DecoderReset, DecodingDisabled,
SegmentsDropped, ConnectionClosedMidMessage, **StuckConnectionReaped** (21-29).

- The enum's summary says "Forwarding is never affected by any of them" (3). StuckConnectionReaped's own doc calls
  itself "the one intervention that ever touches forwarding". A new kind for a time limit would make the summary more
  wrong.
- `OnCaptureDegraded`'s doc says it is "Invoked on the decoder task (never on a byte pump, except for dropped
  segments...)" (Options.cs:139-141). The reaper invokes it on its own task (TcpTap.cs:366).

## 6. Existing timeouts and socket options

| What | Where | Value | Scope |
| --- | --- | --- | --- |
| `ConnectTimeout` | Options.cs:105; TcpTap.cs:212-216 (`CancelAfter`) | 5 s | upstream connect only (#174) |
| `DrainTimeout` | Options.cs:108; TcpTap.cs:710-712 (polls `_live` every 25 ms) | 2 s | `DisposeAsync` only |
| reaper tick | TcpTap.cs:340 | 1 s, literal, no token | reaper only |
| `ReapStuckConnectionsAfter` | Options.cs:173; TcpTap.cs:237-239, 360 | null (off) | unanswered decoded command |
| `DropReportInterval` | TcpTap.cs:43, 386-392 | 1 min | throttles drop reports |

A search for `Timeout|TimeSpan|CancelAfter|Timer|KeepAlive|Linger|SetSocketOption|TickCount|Task.Delay|WaitAsync` over
`src/Kronikol.Extensions.TcpTap` finds only the rows above. The only socket option set is `NoDelay` (TcpTap.cs:209-210).

- No `KeepAlive` and no `TcpKeepAliveTime`. An upstream host that vanishes without a FIN or RST is never detected.
  Keep-alive would catch a dead *host*, but not a live peer that is merely silent (INFERRED).
- No `LingerState`, no buffer sizes, no `ReceiveTimeout`/`SendTimeout`. Those two only govern synchronous calls and
  would not touch `ReadAsync`/`WriteAsync` (INFERRED from .NET docs).
- There is no `TimeProvider` anywhere in `src/`.

## 7. What a pair in each state does today (by reading)

In the table, "toServer" reads the client and writes upstream; "toClient" reads upstream and writes the client.

| State | toServer | toClient | Released by (reaper off, the default) | Reaper on |
| --- | --- | --- | --- | --- |
| (a) client FIN, upstream silent and open | ended: EOF (290), then upstream FIN (318) | parked in `ReadAsync` (283) | upstream sends or closes, or disposal | only if a decoded command is unanswered |
| (b) upstream FIN, client silent and open | parked in `ReadAsync` (283) | ended: EOF, then client FIN (318) | client sends or closes, or disposal | only if a decoded command is unanswered |
| (c) both idle, nothing unanswered | parked in `ReadAsync` | parked in `ReadAsync` | either peer closes, or disposal (**correct for pools**) | never (since is null) |
| (d) client not reading, upstream sending | free | parked in `WriteAsync` (299) | client reads or closes, or disposal | only while the reply is still incomplete in the decoded bytes |
| (e) partial command, then stall | parked in `ReadAsync` | parked in `ReadAsync` | either peer closes, or disposal | never (a partial command is not pending) |

**(a) Client half-closes, upstream stays silent and open** (the issue's TcpTap case).

- `toServer` reads 0 (290-291), breaks, and sends a FIN to the upstream (318). `toClient` stays parked in
  `upstreamStream.ReadAsync` (283) with only the tap token.
- `WhenAll` (241) never completes. `Closed` stays 0, the channel stays open and the decoder never sees
  `OnConnectionClosed`. The `finally` never runs, so both sockets stay open.
  - The tap's end of the client connection sits in CLOSE_WAIT, and the client's end in FIN_WAIT_2 (INFERRED).
  - `LiveConnections` counts the pair.
  - `toClient` keeps its rented 32 KiB buffer.
  - The decoder keeps its state; a Mongo `WireBuffer` can hold up to `MaxBufferedBytes` per direction.
- What releases it: the upstream sends something (the write to a fully closed client then fails, breaks and releases)
  or closes, `DisposeAsync` (the token cancels the read), or, with the reaper on, a decoded command unanswered past the
  threshold. The common half-close comes after all replies, so nothing is unanswered and the reaper never fires.
- The same happens when the client resets instead of sending a FIN: the read error breaks the same way, and the tap
  sends the upstream a graceful FIN.
- INFERRED: Redis and mongod close a client connection when they read EOF, so with a healthy server this pair ends at
  once. It is held only behind an upstream that is up at the TCP level but does not act. Examples: a frozen process
  (`docker pause`, where the kernel still ACKs), a wedged server, an intermediary that keeps half-open connections (a
  port-forward or a second proxy), or a host gone with no FIN or RST, given that no keep-alive is set.

**(b) Upstream half-closes, client stays silent and open.**

- `toClient` reads 0, breaks, and sends a FIN to the client (318). `toServer` stays parked reading the client.
- If the client later writes, `toServer` forwards it. The decoder then gains a pending command that will never be
  answered. If the upstream had fully closed, it answers with an RST and a later write fails (INFERRED).
- The reaper (when on) acts only when something decoded is unanswered.
- INFERRED client behaviour:
  - A client that always keeps a read pending (StackExchange.Redis) sees the FIN at once and reconnects, closing the
    old socket, so the pair ends.
  - A pooled MongoDB.Driver connection between checkouts has no read pending. It notices at its next checkout or when
    pool maintenance prunes it (the driver's `MaxConnectionIdleTime`, 10 minutes by default; not checked in this
    repo). The pair is held until then.
  - A wedged client (the 2026-08-24 shape, Options.cs:157-163) never notices, so the pair is held until disposal.

**(c) Both idle, nothing unanswered (a pool member between uses).**

- Both pumps sit parked in `ReadAsync`, and nothing else runs except the reaper's 1 s tick when it is on (since is
  null, so it continues).
- This is the normal state of every pooled connection, and it is exactly what the first, silence-based reaper killed
  (Options.cs:163-167, CHANGELOG.md:8451: "a Mongo pool member between checkouts is silent for minutes; per-connection
  keep-alives are not universal").
- Anything that closes idle pairs has to be off by default, or longer than every client's own idle handling.
- The existing guard fact is `AnIdleConnectionWithNothingUnansweredIsNeverReaped` (Tests.cs:98-127).

**(d) The client stops reading while the upstream sends a big reply.**

- `toClient` reads up to 32 KiB, queues the copy (293-295, never blocking) and parks in `WriteAsync` (299). That happens
  once the client's receive window and the tap's send buffer are full. There is no deadline, and the token only cancels
  at disposal.
- Parked in the write, it stops reading the upstream, so back-pressure reaches the server. This is correct, but
  unbounded in time.
- `toServer` is unaffected.
- The byte counters and `LastActivityTicks` don't move (306-311).
- If the upstream closes or resets meanwhile, `toClient` does not see it, because it is in a write, not a read.
  `toServer` sees it only on its next write.
- Reaper: the decoder has already seen the stuck segment, because it was queued before the write.
  - Redis keeps the command pending until the reply's last byte is decoded (Resp.cs:191-203). A large reply read slowly
    is therefore reaped after the threshold, measured from the **command's** time, even though the server answered at
    once.
  - Mongo clears the command at the header of a reply over `MaxBufferedBytes` (Mongo.cs:155-165), so such a reply never
    trips the reaper. A smaller reply keeps it pending until it is whole.
  - If the reply already completed in the decoded bytes, nothing is pending and there is no reap.
  - Pub/sub deliveries are never pending, so a subscriber that stops reading is never reaped. INFERRED: Redis drops
    such a client past `client-output-buffer-limit pubsub` (32mb 8mb 60 by default), but the tap does not see that
    close while it is parked in the write.
- A client that stops reading and then closes its socket does release the pair: the parked write fails with a reset,
  and the read on the client fails too (INFERRED).

**(e) The client sends a partial command and stalls.**

- The partial bytes are forwarded at once (299) and the server holds them. INFERRED: Redis with its default `timeout 0`
  and mongod do not time out a partial message.
- Both pumps then sit parked in `ReadAsync`.
- The decoder holds the partial value and adds nothing to the pending queue until the command completes.
  - Redis enqueues in `OnCommand`, which runs only when the top-level value completes (Redis.cs:285-296, 321-324).
  - Mongo returns while the buffer is short (Mongo.cs:129-130) and adds at 303.
  - So `OldestUnansweredSince` stays null (unless an older command is pending) and the reaper never acts.
- When the pair finally closes, `OnConnectionClosed` reports `a partial command (N B held...)` (Redis.cs:195-204,
  Mongo.cs:203-212), which counts as ConnectionsClosedMidMessage.
- Variant: a partial command followed by a client FIN goes the (a) way. A healthy server reads EOF and closes, so the
  pair ends.

## 8. Tests (`tests/Kronikol.Tests.TcpTap`, plus `Kronikol.Tests.TcpTap.Bson3`)

**Classes:**

- `TcpTapTests` (Tests.cs:13): socket-level facts.
- `BusyConnectionTests` (BusyConnectionTests.cs:14): 8 connections of 1,000 GETs each, checking pairing.
- `PumpOrderTests` (PumpOrderTests.cs:23): the `PumpAsync` seam.
- `RedisDecoderTests` (11) and `MongoDecoderTests` (10): `DecoderHarness`, no sockets.
- `RespParserTests` (6), `RespStreamParserTests` (10), `MongoWireParserTests` (6).
- `InProcessParityTests` (20): labels, no sockets.
- `TapNoteGoldenTests` (16): also compiled into the Bson3 project, which links `TestSupport.cs` and holds only this class.
- `ContainerEndToEndTests` (21): Testcontainers `redis:7` and `mongo:7`, with `Assert.SkipWhen(!DockerAvailable(), ...)`
  at 53, 113, 169 and 238.

At 4.20.2 the suite passed 398 with 4 skipped, and Bson3 1 of 1 (release commit 83e9783b).

**Reaper facts:**

- `AConnectionWithACommandUnansweredPastTheThresholdIsReaped` (Tests.cs:57-96): threshold 1,500 ms, a stub that never
  replies. Expects EOF or an `IOException` within 15 s, `StuckConnectionsReaped == 1` and the degradation kind.
- `AnIdleConnectionWithNothingUnansweredIsNeverReaped` (98-127): threshold 1,200 ms. A PING/PONG, then 4 s idle, then
  asserts no reap, `client.Connected`, and that a second PING works.
- `AConnectionWhoseSegmentsWereDroppedIsNeverReaped` (709-743): `ChannelCapacity = 2`, `ReadBufferBytes = 1024`, and
  `StuckBlockingDecoder` (762-773) reporting a command unanswered for 5 minutes. Waits 2.6 s, then asserts 0 reaps.
- Missing: the `DecodingBroken` stand-down, any reaper fact through `MongoTap`, and the validation at Options.cs:210-211.

**Half-close fact:** `AHalfCloseIsPropagatedUpstreamWhileTheReplyStillFlowsBack` (Tests.cs:191-220).

- The client half-closes after the stub has started its reply. The fact asserts `server.ClientHalfClosed`, then reads
  `"late reply"`.
- The stub closes on EOF (Support.cs:339-343), so the upstream always closes after a half-close.
- No fact covers (a), (b), (d) or (e) as held states. `LiveConnections` is never asserted.

**How pairs are driven:** real loopback TCP throughout.

- `StubServer` (Support.cs:289-364):
  - It is a `TcpListener(IPAddress.Loopback, 0)` kept bound (300-302). `Func<byte[], byte[]?>` answers each received
    chunk (348-353).
  - One `ClientHalfClosed` TCS serves the whole server (295, 315).
  - On EOF it sets the TCS and closes (339-343). It cannot stay open after a half-close, push data unasked, stop
    reading, or half-close itself.
- The tap uses `ListenPort = 0` with `BoundPort` (Tests.cs:15-29).
- The client side uses `ConnectAsync` (31-37) and `ReadAtLeastAsync` with a CTS deadline (39-53). A test half-closes
  with `client.Client.Shutdown(SocketShutdown.Send)` (214).
- `Wait.UntilAsync` (Support.cs:366-388) polls every 20 ms with a 30 s default. It has two `<summary>` blocks
  (368-375); see §9 F5.

**LoopbackPort:** `tests/Kronikol.Tests/LoopbackPort.cs` is **not linked** into either TcpTap test project. Only
ProxyTap and Otlp link it, in their csproj files at 37 and 39.

- It is not needed for the existing facts: the tap and the stub both bind port 0 and keep it.
- A "refused upstream" fact for #174 would want `LoopbackPort.Closed()`.

**Collections:** there are no `[Collection]`/`CollectionDefinition` attributes, no assembly attributes and no
`xunit.runner.json`. Under xUnit v3 defaults each class is its own collection and classes run in parallel. That
includes the Docker container facts in the same process, which `Wait.UntilAsync`'s doc says block pool threads for
seconds (Support.cs:369-375).

**Seams:** `InternalsVisibleTo` names `Kronikol.Tests.TcpTap` and `Kronikol.Tests.TcpTap.Bson3`
(Kronikol.Extensions.TcpTap.csproj). Test global alias: `TcpTapCore = Kronikol.Extensions.TcpTap.TcpTap`
(GlobalUsings.cs:3).

| Seam | Location | Notes |
| --- | --- | --- |
| `PumpAsync` | TcpTap.cs:271 | Driven by PumpOrderTests with a `MemoryStream` source, a recording destination `Stream` and an unconnected `Socket`. A destination whose `WriteAsync` never completes would test write stalls without sockets. |
| `TapSegment` | 722 | |
| `ConnectionPumpState` | 724-746 | Public mutable fields. |
| `HandleDecodeError` | 440 | |
| `Degrade` | 484 | |
| `CountOversizePayload` | 499 | |
| `CountClosedMidMessage` | 514 | |
| `Cap` | 679 | |
| `CountDecodeError` / `Diagnostic` | 688 / 690 | |
| `TcpTapConnectionContext` ctor | 754 | Used by `DecoderHarness`, Support.cs:56. |
| `TapInteraction.Error` | IProtocolDecoder.cs:86 | |

- **Private, so not seams:** `AcceptLoopAsync`, `HandleConnectionAsync`, `ReapWhenStuckAsync`, `CountDrop` and
  `DecodeLoopAsync`.
- **Timing:** there is no clock seam. The reaper's tick is a literal, so timing facts use the real clock. Today's use
  thresholds of 1.2 to 1.5 s and waits of 2.6 to 4 s.

## 9. Found along the way (outside the time-limit question; each READ unless marked)

- **F1. A negative `BodyCapBytes` is not validated** (Options.cs:96, 188-214).
  - Redis: `EffectiveMaxBulkBytes` (RedisTapOptions.cs:68) becomes -1. `RespStreamParser`'s ctor throws (Resp.cs:85-86)
    inside the decoder factory (Redis.cs:93-96). The result is that **every Redis connection is closed right after its
    upstream connect**, with only a `tcp-tap connection N error` log line (TcpTap.cs:251-254, then the `finally`).
  - Mongo: `Cap` throws at record time (TcpTap.cs:683-684), which disables decoding for the connection.
- **F2. A `DecoderFactory` that throws closes the pair** (TcpTap.cs:219 runs inside the `try`, then the catch at 251 and
  the `finally` at 255-261). Nothing counts it: no counter, no `Degrade`, no `Diagnostics()` line. This contradicts the
  class remarks: "the connection keeps forwarding either way" (TcpTap.cs:21-27).
- **F3. A `Log` callback that throws is not contained.** The calls are unguarded at 253, 257, 365, 383, 447, 478, 495
  and 517.
  - At 383 (`CountDrop`, on a pump), the exception escapes `PumpAsync`. Through `WhenAll` it skips 242-245, so `Closed`
    is never set (a reaper loops for ever) and the channel is never completed (the decode task waits for ever).
  - At 365 the reaper has already counted the reap but disposes nothing, so the wedged connection stays open.
  - At 257 (inside the `finally`'s catch) it skips 258-260: the sockets are not disposed and `_live` is never
    decremented.
  - INFERRED trigger: a callback that writes to a test's output after that test has ended. The proxy tap's `Log` calls
    are unguarded the same way (ProxyTap.cs:168, 212, 247, 459, 468, 608, 1004).
- **F4. The accept loop ends for good on any `SocketException`**, while `IsListening` stays true (TcpTap.cs:82,
  191-193). This is #174's to take.
- **F5. `Wait.UntilAsync` has two `<summary>` blocks**, and the first is stale (Support.cs:368-375).
- **F6. Every reap is also counted closed mid-message** (§3). Both counts are true, but one event yields two
  `Diagnostics()` lines.
- **F7. The reaper measures age on the wall clock** (TcpTap.cs:294, 360). A forward clock jump, for example a VM resumed
  from suspend, can age a just-sent command (INFERRED).
- **F8. The reaper's tick is not cancellable** (340). With the reaper on, each pair's release lags its pumps by up to
  1 s (245).
- **F9. Doc drift:** the `CaptureDegradationKind` summary (CaptureDegradation.cs:3) and the `OnCaptureDegraded` thread
  claim (Options.cs:139-141); see §5. The `DisposeAsync` stop line omits the reaped and closed-mid-message counts
  (TcpTap.cs:715-718).

## 10. Levers visible in the code (observations, not recommendations)

- **Where a time limit would hook in.** Either pump's `finally` (314-320) is where a pair becomes half-closed. A
  timestamp or flag in `ConnectionPumpState` set there could arm a timer for the other direction. The closer would be
  what the reaper does: dispose both `TcpClient`s (367-368). The existing release then follows untouched (241-261).
- **How to stop parked pumps.** A per-pair `CancellationTokenSource` linked to `ct` would stop them through the tokens
  they already pass (283, 299). Disposal relies on exactly that today (698-699).
- **Signals available without the decoder:**
  - one pump ended, but EOF and error are not told apart today (285-291);
  - bytes moved per pair, via `LastActivityTicks`, which is per pair but not per direction, dead, and frozen during a
    stuck write;
  - a write in progress, which is not tracked.
- **Signals from the decoder:** only `OldestUnansweredSince`.
  - A "partial message since" signal would need a new published value. The parsers' `IsAtValueBoundary`/`HeldBytes`
    (Resp.cs:112, 121) and Mongo's `WireBuffer.Length` are mutated on the decode task only.
  - Anything decoder-based inherits the drop and disable stand-downs (345-348) and the decode lag (§3).
- **Pools set the bounds.** Idle pairs must survive by default (Options.cs:163-167). A half-closed pair is a different
  state, since one side has said it is done. One counter-case needs a "quiet after half-close" rule rather than an
  absolute deadline: the existing fact at Tests.cs:191-220 shows a reply that legitimately flows after a client
  half-close.
- **Naming should follow the proxy tap** (Options.cs:24-27). A new option is a minor bump under CLAUDE.md even if it
  defaults to today's behaviour. It also needs a line in `CopyTo` (Options.cs:217-252), and no fact would notice the
  line missing (§4.4).
