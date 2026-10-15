# Line-reference sweep: `plans/TAP_CALLER_TIME_LIMITS_PLAN.md` against `3240c983`

Checked against `C:/Code/Kronikol-173` at `3240c983` (`git diff 3240c983 -- src tests` is empty). Every citation of
Kronikol's own code was read at its line. Citations of .NET and ASP.NET Core source (`M/HttpConnection.cs`, CoreLib
`Stream.cs`, `TimeoutControl.cs`) and RUN measurements are out of scope. Three behavioural facts were settled with a
scratch probe (`line-refs-probe.cs` in this folder), run on Windows 11 26200 with .NET 10.0.11 and in
`mcr.microsoft.com/dotnet/sdk:10.0` on Linux with .NET 10.0.12. Both platforms gave the same output:

```
Content-Length 'abc': TryParse=False, value=0, takes body path=False
Content-Length '-5': TryParse=True, value=-5, takes body path=False
Content-Length '10, 10': TryParse=False, value=0, takes body path=False
Content-Length '3000000000': TryParse=False, value=0, takes body path=False
Content-Length '+5': TryParse=True, value=5, takes body path=True
Content-Length '0': TryParse=True, value=0, takes body path=False
Content-Length '2147483648': TryParse=False, value=0, takes body path=False
Content-Length '2147483647': TryParse=True, value=2147483647, takes body path=True
CopyToAsync read sizes requested: 131072
NoDelay after peer reset (round 0..2): no exception (3 of 3)
```

## 1. WRONG and IMPRECISE

| Plan line | Citation | The plan's claim | Verdict | The correct fact, with lines at `3240c983` |
|---|---|---|---|---|
| 259 | ProxyTap.cs L285-287 | "`CopyToAsync` reads 80 KiB at a time" | **WRONG** | `body.CopyToAsync(buffer)` (L286) is the base `Stream.CopyToAsync(Stream)`: it asks for an 81,920-byte buffer, `ArrayPool<byte>.Shared` rounds that up to 131,072, and the loop reads into the whole rented array. Each read asks for **128 KiB** (probe: every request was 131,072 bytes, on both platforms; .NET's own comment in `Stream.GetCopyBufferSize` says the same). With the plan's RUN finding that a Windows read, or a Linux chunked read, waits to fill its buffer, today's piece is 128 KiB. The argument for 16 KiB pieces stands, but the figure is wrong |
| 401-403 | OtlpTap.cs:281, L282 | "A socket reset between the accept and L281 throws there, and the `TcpClient` is left to its finalizer, holding a descriptor until a GC" | **WRONG as stated** (lines OK) | The lines are right: `client.NoDelay = true` (L281) runs inside the `try`, before `using (client)` (L282), and a throw there would be swallowed by the catch at L298 with the client undisposed. But setting `NoDelay` on an accepted socket after the peer's RST **did not throw**: 3 of 3 rounds on Windows and 3 of 3 in the Linux container. `read/otlptap.md` §11.5 itself says "If the setter threw ... Not seen". BSD and macOS `setsockopt` may fail after a reset (not run). Moving the line is harmless hygiene, but the leak is not shown on either platform the plan measures. §5 also has no fact for item 5, so "each gets a fact" (line 364) does not hold for it, or for item 8 |
| 394-396 | HttpRequestReader.cs L92-94 | "a `Content-Length` that does not parse as an `int` is read as 'no body' ... `abc`, `-5`, `10, 10` and `3000000000` all fall through" | **WRONG for `-5`** (the fall-through is right) | All four do end with `body = []`, but **`-5` parses**: `int.TryParse("-5", NumberStyles.Integer, ...)` is true and gives -5, and the value falls through on `length > 0` (L94). `abc`, `10, 10` and `3000000000` fail the parse; `10, 10` can also come from two headers joined at L70. Not in the plan: `NumberStyles.Integer` allows a sign, so **`+5` parses to 5 and is read as a 5-byte body today**, although RFC 9110's `Content-Length` is `1*DIGIT`. A fix that keeps `NumberStyles.Integer` and refuses only parse failures and negative values still takes `+5`; `NumberStyles.None` refuses it. R0-4's theory could add `+5`, and a guard that `0` stays a valid empty body. `2147483648` (2 GiB) also fails the parse |
| 395-396 | OtlpTap.cs (implied) | "The export is answered `200`, and its body is then read as the next request's head" | IMPRECISE | `200` only with no `ForwardBaseUri`, on `TracesPath` and `POST` (L349-357). With a forward, the empty body is forwarded and the collector's answer relayed (L345-348), as `read/otlptap.md` §11.3 says. Either way `RequestsReceived` moves (L330) and nothing is queued (L338 needs a body). The body is read as the next head only on a keep-alive connection (the loop at L286-295) |
| 174, 354, 489 | TcpTap.cs L294, L360 | "Its opt-in reaper reads the wall clock (L294, L360)"; R3-6: "the reaper's two `DateTimeOffset.UtcNow` reads (L294, L360)" | IMPRECISE | Only L360 is in `ReapWhenStuckAsync` (L335-371). **L294 is in `PumpAsync`**: it stamps every `TapSegment`. The decoders keep that stamp as the command's time (Redis `_timestamp` at L155 and L176 of RedisProtocolDecoder.cs, published at L121-122; Mongo `pending.Timestamp`, published at L83-85). It reaches the reaper through the **public** `IProtocolDecoder.OldestUnansweredSince` (IProtocolDecoder.cs L55, a `DateTimeOffset?`), and it is also every recorded interaction's timestamp. So routing L294 through a seam moves recorded timestamps too. And §4.5 item 5's monotonic reaper ("a clock jump can no longer age a command") needs more than a clock swap in the reaper: the command's time arrives as a wall-clock value through a public interface member, so the tap must keep a monotonic first-seen time per oldest value, or the interface must change |
| 421 | Options.cs:139-141, L366 | "`OnCaptureDegraded`'s 'never on a byte pump' (Options.cs:139-141), when the reaper calls it on its own task (L366)" | IMPRECISE | The file is TcpTapOptions.cs (the plan never says which `Options.cs`). The doc (L136-142) reads "Invoked on the decoder task (never on a byte pump, except for dropped segments ...)". The reaper's task is not a byte pump, so the quoted part is true of it. The false part is "**Invoked on the decoder task**". The doc's list of events (L137-138) also leaves out the reap. L366 (`Degrade(..., StuckConnectionReaped, ...)` from the reaper's task) is right, and so is CaptureDegradation.cs:3 |
| 407 | TcpTap.cs L447, L478, L495, L517, L690 | "at L447, L478, L495, L517 and in `Diagnostic` (L690 ...) it turns into a decoder error and disables decoding" | IMPRECISE | **L495** runs only after `OnCaptureDegraded` threw, and `Degrade` is called from the pump's `CountDrop` (L391), the reaper (L366) and the decoder paths (L450, L481, L509, L518, L786). From L391 a throw escapes `PumpAsync`, as at L383. From L366 it skips the disposal, as at L365. Only on the decoder paths is it a decoder error. **L478**, with a callback that always throws (the plan's xUnit case): the outer catch's `DisableDecoding` (L431, then L478) throws again, so the decode task faults and stops draining the channel. `DecodingDisabledConnections` (L473) and `DecodeErrors` (L442, L430) count twice, later segments count as drops, and the tap's first drop then logs at L383 from a pump. **L517** is reached from `OnConnectionClosed` (L425), outside the inner catch, so it disables decoding on a connection that is already closing |
| 410-411; App. A 651 | OtlpTap.cs "L304, 313, 321 and on" | the per-connection `Log` calls | IMPRECISE (complete only through "and on") | The other per-connection calls are **L341** (payload dropped), **L409** (bad target) and **L448** (forward failed). The plan covers them only as "and on" and "the other per-connection `Log` calls". At each, a throw comes after the counter has moved (L340, none, L447) and before the answer is written, so the exporter gets a closed connection and no answer. Better named one by one for R0-5's theory |
| App. A 652 | OtlpTap.cs 310-321 (R0) | "the secret checked after the head, before the body" | IMPRECISE | L310-316 is the 413 branch. The auth check is **L318-324**, and the range stops at its log line, before the 401 write (L322) and the `return` (L323). The move cannot be made in OtlpTap.cs alone. The head and the body are read by one `HttpRequestReader.ReadAsync` (L44-109: head L46-80, chunked body L84-91, Content-Length body and the over-cap drain L92-106), called once at OtlpTap.cs L288. So R0's sites need `ReadAsync` L82-106, both framings, since a chunked body is read at L87 (Appendix A's reader row lists only 92-106 and 154, for the 400 and the growing buffer), and `ServeAsync` L286-295. The 400 for a bad `Content-Length` (item 4) also needs a new result flag and a branch in `HandleAsync` (L308-367), besides L310-321 |
| App. A 659 | TcpTap.cs 241-261 (R3) | "the pair's token and release" | IMPRECISE | The pumps take the token at **L234-235**, and the reaper is armed at **L237-239**. A per-pair CTS is made and handed over in L229-239; 241-261 is only the wait and the release |
| App. A 638, 647, 656, 662 | option rows | R0 `ForwardTimeout` "refused at start" at ProxyTap.cs 172-192; R1, R2 and R3 option rows | IMPRECISE (omissions) | "As it refuses other bad options" (line 375) means `ProxyTapOptions.Validate` (**L186-199**), which `StartAsync` calls (L174) and so does registration (ProxyTapServiceCollectionExtensions.cs L21); it is not in the table. Likewise R1's refusal (`ProxyTapOptions.Validate` L186-199), R2's (`OtlpTapOptions.Validate` **L140-155**) and R3's (`TcpTapOptions.Validate` **L188-214**). R3's two options also need lines in `TcpTapOptions.CopyTo` (**L217-252**): the Redis and Mongo list overloads copy through it (TcpTapServiceCollectionExtensions.cs L37, L71), and the generic tap has no list overload. Appendix A names `Copy` for ProxyTap and OtlpTap but not `CopyTo` for TcpTap |
| App. A 642 | ProxyTap.cs 825-859, 887-902 (R1) | "the 502 written with an awaited `WriteAsync`" | IMPRECISE (minor) | `TryRespondBadGateway` is `private static void`. Awaiting it changes both call sites: L255 (inside 234-265) and **L472** (the forward's catch, in no listed range) |
| 559-561 | HttpRequestReader.cs L96-100 | "A caller that declares a 2 GiB body and sends it steadily holds the handler for the whole of it" | IMPRECISE | Lines right. A declared 2 GiB (2,147,483,648) **fails the int parse and is read as no body** (the R0 item 4 case; after R0, a 413 and a close). The drain takes a `Content-Length` up to `int.MaxValue` (2,147,483,647). A **chunked** body over the cap is drained chunk by chunk with no total bound (L192-197). `read/otlptap.md` §0 item 5 says "any int, about 2 GiB" |
| 84 | Options.cs L160-173 | idle pair: nothing, by design (3.0.49) | IMPRECISE (minor) | TcpTapOptions.cs: `ReapStuckConnectionsAfter`'s doc is L156-172 and the property L173, and L160 starts mid-sentence. The idle-pair rule is **L165-167**, and §4.1 item 2 cites 163-167 |
| 226 | Kronikol.Extensions.MongoDB.V2.csproj L28-32 | the precedent for linked `<Compile Include Link>` | IMPRECISE (minor) | The linked-file `ItemGroup` is **L27-40**: twelve `<Compile Include=... Link=...>` lines, L28-39. L28-32 holds the first five |
| 545 | TcpTap L191 | "#174 owns both taps' accept loops (`TcpTap` L191, ...)" | IMPRECISE (minor) | `TcpTap.AcceptLoopAsync` is **L181-199**. L191 is its catch filter (the `SocketException` exit, L191-194). `OtlpTap.AcceptLoopAsync` L259-275 is right |
| 344 | TcpTap.cs L299 | "such a pump waits in `WriteAsync` (L299) for ever" | IMPRECISE (minor) | Until disposal: the write takes the tap token `ct` (L299), which `DisposeAsync` cancels (L698-699). It can also end through a peer close or, with the reaper on and a Redis reply still incomplete, a reap. §1 and §2.2 say "until disposal" |

## 2. OK (compact)

ProxyTap.cs and its options:
- 58, 76: deadline L397-399, made after the body read; its token goes only to `SendAsync` L426 and `ReadAsByteArrayAsync` L427. `HttpClient.Timeout = ForwardTimeout + 10 s` at L192. `ForwardTimeout` defaults to 200 s (ProxyTapOptions.cs L139).
- 59, 75, 566: body copy L279-299 with no token (`CopyToAsync(buffer)` L286), an untyped `catch (Exception)` L289-298, no size cap (`BodyCapBytes` L54 caps only `CapturedBody` L740-741).
- 59, 273: the answer in one `WriteAsync(body)` with no token, L852.
- 77, 256-257: `RespondAsync` L825-859, `TryRespondBadGateway` L887-902, synchronous `Write` L895.
- 87, 153, 282: the in-flight registration L310-314 (the `using var` is L312-314, its comment L308-311) lasts to the end of `HandleAsync`, through `RespondAsync` (L489) and the record (L499). `MostRecentFor` returns the newest registration or null (InFlightIdentityRegistry.cs L67-80).
- 251-252: `Copy` L42-77 copies all 32 `ProxyTapOptions` properties. `Registering_taps_from_a_list_copies_every_option` is L34-74, and its `NonDefault` table (L16-32) has a `TimeSpan` arm (L24) and no `TimeSpan?` arm, so a `TimeSpan?` option needs one.
- 281: `ProxyTapHangUpTests` L46-50 pin handled 1, captured 1, 2 records, `ForwardFailures` 0.
- 283: `CallerAnswer` L904-956; `Closed` (L918) is a plain auto-property.
- 290: `Diagnostics()` L108-137.
- 366-375: L192 gives -1 ms + 10 s = 9.999 s, and `CancelAfter` (L399) treats -1 ms as never. `ForwardFailure.Classify` (ForwardFailure.cs L40-42) files HttpClient's own timeout (an inner `TimeoutException`, neither token cancelled) as `ConnectTimedOut`, worded at L55 "could not connect ... within ConnectTimeout". Zero cancels at once. -5 s throws `ArgumentOutOfRangeException` from `CancelAfter`, outside the forward's `try` (L424), so the catch-all (L242) counts it, the caller gets a 502, and nothing is recorded. -10 s sets `HttpClient.Timeout` to 0, and the setter throws in `StartAsync`. `Validate` (L186-199) checks no duration.
- 382-383: only a count today (`_inflight`, L52).
- 410: ProxyTap's `Log` calls are exactly L168, 212, 247, 459, 468, 608 and 1004, all unguarded.
- 185-187 (§3 item 15): `ProxyTapHangUpTests` reads `_listenerContexts` and `_connections` (L416).

OtlpTap.cs and HttpRequestReader.cs:
- 62, 88: 16 KiB buffer (L39), a 64 KiB head (`MaxHeaderBytes` L36, checked at L146), `new byte[count]` (L154), `MaxRequestBytes` 32 MiB (OtlpTapOptions.cs L68).
- 79, 318: `FillAsync` L285-290 and `WriteResponseAsync` L459-475. An idle keep-alive connection and a partial head both wait at `ReadHeadAsync` L130, then `FillAsync` L288, and nothing marks the first byte.
- 80, 442, 557: the forward's `HttpClient.Timeout` is 30 s, with no option (L172).
- 168, 387-388: the body is read in `reader.ReadAsync` (L288) before `IsAuthenticated` (L318).
- 389: `ExpectedHeaders` is asked for on a non-loopback bind (the sentence is on OtlpTap.cs L33, and also at OtlpTapOptions.cs L41-42 and L58).
- 315: `Copy` L42-67 copies all 20 options, and no fact in `tests/Kronikol.Tests.Otlp` checks it.
- 323: `ServeAsync`'s first catch, L298-301, swallows `OperationCanceledException` with a hang-up.
- 411-413: `MapPayload`'s catch logs at L526. `MapLoopAsync` (L481-498) catches only `OperationCanceledException` (L491), so a throw from L526 ends the loop. `DisposeAsync` awaits it catching only that (L601-606), so it rethrows, and also skips L609-613.
- 559: the over-cap drain, L96-100 (the size wording is above).

TcpTap.cs and its options:
- 63, 81, 351: the pair is released only after `Task.WhenAll` (L241). The `finally` L255-261 disposes, after the decode drain (L244) and the reaper (L245).
- 82, 344: the pump waits in `WriteAsync` at L299.
- 83: the reaper is L323-371, off by default (TcpTapOptions.cs L173).
- 176, 355: `Task.Delay(1_000)` at L340 takes no token. L245 awaits the reaper.
- 208: TcpTapOptions.cs L163-167, and CHANGELOG.md L8451 (3.0.49: the silence-based reaper killed idle pool connections).
- 336: `Shutdown(SocketShutdown.Send)` in the pump's `finally`, L318.
- 349: `LastActivityTicks` (L731) is written only at L311 and read nowhere in `src/` or `tests/`. It was added already unread in 3.0.49 (398655f4).
- 357, 422: `StuckConnectionsReaped`'s line L565-568, the stop line L715-718 (it leaves out the reaped and closed-mid-message counts), and `Diagnostics()` L529-580.
- 405-409: L383 escapes `PumpAsync`; through `WhenAll` it skips L242-245, so `Closed` is never set, the channel is never completed and the decode task waits for ever; the sockets are still released by the `finally`. L257 skips L258-260 (only after `decoder.Dispose()` threw). L365 leaves a counted reap that disposes nothing. L466 and L672 are reached from the decoders. L690 `Diagnostic` is reached through `context.Log` (L774).
- 416-418: `BodyCapBytes` (L96) is not refused in `Validate` (L188-214). Redis: `EffectiveMaxBulkBytes` (RedisTapOptions.cs L68) goes negative, and `RespStreamParser`'s constructor throws (L85) inside `RedisProtocolDecoder`'s constructor (L95), which the factory runs at TcpTap.cs L219. The catch at L251-254 and the `finally` close the pair. With `MaxBulkBytes` set, `PreviewBytes` (L137-143) goes negative and L86 throws instead. Mongo: `Cap` (L683-684, `text[..cap]`) throws at the first `Record` (L616). `RecordPair` (Mongo L378) does not catch it, so it lands in the inner catch, then `HandleDecodeError`, then `DisableDecoding`.
- 420-421: CaptureDegradation.cs L3 ("Forwarding is never affected by any of them") stands beside `StuckConnectionReaped` (L21-29). L366 calls `Degrade` from the reaper's task.
- 573 (§8): every reap is counted twice. Redis's `OnConnectionClosed` (L194-204) reports the still-pending command, and `CountClosedMidMessage` (L514-519) counts it.
- 88-89 (§2.1): two pump buffers of `ReadBufferBytes`, 32 KiB (TcpTapOptions.cs L154, rented at TcpTap.cs L275).

Elsewhere:
- 238-241, 452-453: no `TimeProvider` anywhere in `src/`. `InternalsVisibleTo` exists for each test project (ProxyTap csproj L18, Otlp L18, TcpTap L48-49). `LoopbackPort` is linked into the ProxyTap and Otlp test projects and not into the TcpTap ones.
- Copy counts (the plan states none; `read/` is right): ProxyTap `Copy` 32, OtlpTap `Copy` 20, `TcpTapOptions.CopyTo` 32 (L220-251), plus Redis 5 and Mongo 7.
- Appendix A rows not listed in section 1 hold the code their "Change" column names: ProxyTap.cs 52, 234-265 and 963-1005 (`_listener.Close()` at L980, the drain at L999-1000); 168-1004 (`TryLog`); 234-265 and 279-299; 904-956 (R0 and R1); 108-137; 963-1005 (R1). ProxyTapOptions.cs 138-142. ProxyTapServiceCollectionExtensions.cs 42-77. OtlpTap.cs 281-282 and 277-306. HttpRequestReader.cs 92-106, 154, 124-150 and 285-290. OtlpTapServiceCollectionExtensions.cs 42-67. TcpTap.cs 257-690 (10 sites), 271-321, 731, 323-371, 529-580 and 715-718. TcpTapOptions.cs 96 and 188-214. CaptureDegradation.cs 3. TcpTapOptions.cs 139-141.

## 3. Every `Log` call site

Files searched: ProxyTap.cs, OtlpTap.cs, HttpRequestReader.cs, and everything under `src/Kronikol.Extensions.TcpTap/`
(the patterns were `Log?.Invoke`, `.Log(`, `Diagnostic(` and `context.Log`). `Sink.Log` calls (record writes, not the
callback) are left out. HttpRequestReader.cs, RedisTap.cs, MongoTap.cs, the `*Options.cs` files,
TcpTapServiceCollectionExtensions.cs, IProtocolDecoder.cs, CaptureDegradation.cs, RespParser.cs, RespStreamParser.cs,
RespValue.cs, MongoWireProtocol.cs, WireBuffer.cs and TapProtocolException.cs have none. No call is guarded.

| File:line | Where | Guarded | Owner | What a throw does today |
|---|---|---|---|---|
| ProxyTap.cs:168 | `CaptureFault` | no | #173 (§4.6 item 6, App. A) | It escapes whichever catch called it: `StartSpan` (L533), `EndSpan` (L554), `TryRecord` (L572), `ResolveIdentity` (L626) or `GuardedHandleAsync` (L250), then hits L247. If that is before the answer, the request is never forwarded (resolver, span start) or the upstream's answer is never written (span end at L485-486, before `RespondAsync`). The caller then gets `EndUnclosed`'s 502 head on the managed listener, or a reset from http.sys. A case for R0-5 |
| ProxyTap.cs:212 | `StartAsync` | no | #173 | `StartAsync` throws after the listener has started and the loop is running |
| ProxyTap.cs:247 | `GuardedHandleAsync` catch | no | #173 | It skips the forward-failure count and the 502 body. The `finally` still aborts (a 502 head, or a reset) and decrements in-flight. The task faults unobserved |
| ProxyTap.cs:459 | forward catch, stopping | no | #173 | It skips `TryRespondBadGateway`, then the L247 path |
| ProxyTap.cs:468 | forward catch, error | no | #173 | After `_forwardFailures++` (L467) it skips the 502 body. If L247 then logs without throwing, the failure is counted again (L254) |
| ProxyTap.cs:608 | `NotApplied` | no | #173 | Thrown at L324, before the forward: the request is never forwarded, and the caller gets the 502 head or a reset |
| ProxyTap.cs:1004 | `DisposeAsync` | no | #173 | `DisposeAsync` throws after everything is released |
| OtlpTap.cs:182 | `StartAsync` | no | #174 | `StartAsync` throws with the listeners bound and the loops running |
| OtlpTap.cs:210 | `Bind` | no | #174 | `StartAsync` throws after the first listener has started, and it stays bound until `DisposeAsync` |
| OtlpTap.cs:304 | `ServeAsync` second catch | no | #173 (named) | It escapes `ServeAsync` as an unobserved task exception. The socket is already closed by the `using` |
| OtlpTap.cs:313 | 413 branch | no | #173 (named) | `_rejected` has moved; the 413 is never written; then the L304 path |
| OtlpTap.cs:321 | 401 branch | no | #173 (named) | `_unauthenticated` has moved; the 401 is never written |
| OtlpTap.cs:341 | queue full or stopping | no | #173 ("and on", not named) | `_dropped` has moved; the export is neither answered nor forwarded |
| OtlpTap.cs:409 | target with no path | no | #173 ("and on", not named) | The 400 is never written |
| OtlpTap.cs:448 | forward failed | no | #173 ("and on", not named) | `_forwardFailures` has moved; the 502 is never written |
| OtlpTap.cs:526 | `MapPayload` catch | no | #173 (named) | The map loop ends for good. The queue then fills, and every later export is dropped (and logs at L341). `DisposeAsync` rethrows (L601-606) and skips L609-613 |
| OtlpTap.cs:611 | `DisposeAsync` | no | #174 | `DisposeAsync` throws at its end |
| TcpTap.cs:162 | `StartAsync` | no | #174 | `StartAsync` throws with the listener started and the loop running |
| TcpTap.cs:253 | connection catch | no | #174 | It escapes the catch, but the `finally` (L255-261) still releases the pair. The task faults unobserved |
| TcpTap.cs:257 | `finally`, after `decoder.Dispose()` threw | no | #173 | L258-260 are skipped: both sockets are left to finalizers, `_live` never falls, and every `DisposeAsync` waits the full `DrainTimeout` |
| TcpTap.cs:365 | reaper | no | #173 | The reap is counted, but there is no `Degrade` and no disposal. The wedged pair is held until disposal |
| TcpTap.cs:383 | `CountDrop` (a pump) | no | #173 | It escapes `PumpAsync`. Once the other pump ends, `WhenAll` skips L242-245: `Closed` is never set, so a reaper ticks for ever, and the channel is never completed, so the decode task waits for ever |
| TcpTap.cs:447 | `HandleDecodeError`, reset | no | #173 | From the inner catch to the outer catch (L427), then `DisableDecoding` |
| TcpTap.cs:466 | `TryReset` catch | no | #173 | The same path. It is reached when `decoder.TryReset` throws, including through the decoders' `context.Log` (Redis L238, Mongo L241) |
| TcpTap.cs:478 | `DisableDecoding` | no | #173 | It runs again from the outer catch (L431). If it throws again, the decode task faults and stops draining: double counts, then drops (section 1) |
| TcpTap.cs:495 | `Degrade`, after `OnCaptureDegraded` threw | no | #173 | It depends on the caller: from L391 as at L383, from L366 as at L365, from the decoder paths a decoder error |
| TcpTap.cs:517 | `CountClosedMidMessage` | no | #173 | From `OnConnectionClosed` (L425) to the outer catch: decoding is disabled on a closing connection |
| TcpTap.cs:672 | `ResolveIdentity`, after the resolver threw | no | #173 | `Record` throws inside the decoder: a decoder error |
| TcpTap.cs:690 | `Diagnostic` | no | #173 | The wrapper for `TcpTapConnectionContext.Log` (L774). Guarding it covers every decoder call below |
| TcpTap.cs:715 | `DisposeAsync` | no | #174 | `DisposeAsync` throws at its end |
| TcpTap.cs:774 | `TcpTapConnectionContext.Log` | via L690 | #173 (through L690) | It forwards to L690 |
| RedisProtocolDecoder.cs:238 | `TryReset` (gives up) | via L690 | #173 (through L690) | It escapes `decoder.TryReset`, then the L464 catch and the L466 path |
| MongoProtocolDecoder.cs:125 | `OnClientToServer`, an oversize command | via L690 | #173 (through L690) | A decoder error |
| MongoProtocolDecoder.cs:241 | `TryReset` (gives up) | via L690 | #173 (through L690) | As Redis L238 |
| MongoProtocolDecoder.cs:475, 481 | `ReportCompression` | via L690 | #173 (through L690) | A decoder error |
| MongoProtocolDecoder.cs:494, 498 | `ReportLegacyHandshake` | via L690 | #173 (through L690) | A decoder error |

**No site is in neither plan.** All 13 TcpTap.cs sites, all 10 OtlpTap.cs sites and all 7 ProxyTap.cs sites are covered. The
decoder sites are covered through L690. The plan names OtlpTap L341, L409 and L448 only as "and on".

## 4. Found on the way (outside the citations)

- **`ProxyTapOptions.BodyCapBytes` (L54) and `OtlpTapOptions.ContentCapBytes` (L125) are not validated either**
  (ProxyTapOptions.cs L186-199, OtlpTapOptions.cs L140-155). A negative value makes ProxyTap's `CapturedBody`
  (`text[..cap]`, L740-741) throw for every non-empty body inside `TryRecord`. Every such exchange becomes a capture
  fault and is not recorded. OtlpTap's `SpanToInteractionMapper.Cap` (`value[..limit]`, L418-425) throws in the same way,
  so `MapPayload` counts every payload with a capped attribute as failed. R0 item 7 refuses only TcpTap's.
