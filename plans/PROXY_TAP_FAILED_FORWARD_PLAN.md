# ProxyTap failed forward plan (#171)

**Status:** written 2026-10-13 at 4.18.1 (`34145c44`) by kronikol-70, and green-lit in full by the owner the same day,
every question in section 9 as recommended, and executed: R0 as 4.19.1 (`4de3ee16`), R1 and R2 as one patch, 4.19.5,
on #172's `CallerAnswer` (section 8).

**Disclosure note.** Section 3.5 describes a security defect found while writing this plan: a request whose target
started with `//` (or, through `ProxyTap`'s listener, `/\`) was forwarded to whatever host it named, by `ProxyTap` and by
`OtlpTap` in forward mode (which also took an absolute-form target). 4.19.1 fixes it, and this file was committed once
that release was on nuget.org (Q8).

**Issue:** [#171](https://github.com/lemonlion/Kronikol/issues/171), "ProxyTap records nothing for a call it couldn't
forward, so the 502 its client got is missing from the diagram". Siblings filed the same day: #172 (caller
hang-ups, planned by kronikol-a5 in `PROXY_TAP_HANG_UP_PLAN.md`) and #170 (trace id from the resolved identity,
planned by kronikol-ef in `PROXYTAP_TRACE_FROM_IDENTITY_PLAN.md`). All three change `ProxyTap.HandleAsync`; section 8
holds the agreed split and order.

**Harness:** `plans/PROXY_TAP_FAILED_FORWARD_PLAN.harness/`. `probe/probe.cs` measures seven failure paths on the
published 4.18.1 package (`probe/probe-output-4.18.1.txt`), and `probe/probe-network-path.cs` measures section 3.5
(`probe/probe-network-path-output-4.18.1.txt`). `probe/probe-audit.cs`, written when the plan was audited, measures
more request targets on both taps, the `ConnectTimeout` shape, `HttpRequestError` per failure, an `ActivityListener`
that throws, and the R0 rule itself, on Windows and on Linux (`probe/probe-audit-output-4.18.1-{windows,linux}.txt`).
All run with `dotnet run --file <probe>.cs` and need no repo build. Measured on Windows 11 with .NET 10.0.11
(http.sys), and the audit probe also on Ubuntu 24.04 with .NET 10.0.12 (the managed listener, through the podman
runner). The forward side is `HttpClient` and behaves the same on both, except that a refused connect takes about
2 s on Windows and 1 ms on Linux. The listener side differs (sections 3.5 and 4.1), which is why section 6 runs the
facts on Linux.

Line numbers are to `34145c44` unless a line says otherwise.

---

## 1. Summary

The issue is right about what happens and about where the fix belongs. It understates the problem, though.
`GuardedHandleAsync` has one catch-all (`ProxyTap.cs` L161-166), and it treats every exception after the accept as a
forward failure: it counts `ForwardFailures`, logs `proxy-tap error:`, answers 502 and records nothing. Exceptions
reach it from three different places, and only one of them is the upstream:

| Where it threw | Example (measured, 4.18.1) | What the tap does today | What is true |
|---|---|---|---|
| The **upstream**: the forward (L282-296) | refused, timed out, body cut short | 502, `ForwardFailures` +1, **no record**, spans never ended | The call failed. It should be recorded and its spans ended (#171) |
| The **caller**: its body read (L185), the answer's write | the caller resets mid-request | on the managed listener (Linux, macOS), a 502 attempt and `ForwardFailures` +1, 10 of 10 on Linux for an RST alone (#172's harness `reset/`) | The caller left. Nothing to answer or count (#172) |
| **Capture**: `IdentityResolver`, `Sink.Log`, body decoding, span listeners | resolver throws; sink throws; `br` body that isn't brotli; an `ActivityListener` throws as a tap span starts or stops | resolver, listener: **caller gets 502**, though with a listener that throws at the stop the upstream had answered. Sink/decoder: `ForwardFailures` +1 and "carried no test identity", both false | Capture must never change what the caller gets, and its faults are capture faults |

So the underlying problem is that the tap does not tell apart *who* failed. #171 is the upstream row. This plan
fixes that row in full (R1), fixes the capture row (R2, found while writing it), and leaves the caller row to #172,
which lands first. It also found an unrelated defect in the same method, in section 3.5: request targets starting
with `//` or `/\` leave the configured upstream, one starting with `///` is answered 502, and `OtlpTap`'s forward mode
has the same defect (R0, security).

After R1, a forward that fails is drawn as a failed call. The request half is recorded as today. The response half
carries `!HttpRequestException` (or the exception's own type) where the status goes, the 502 body the tap actually
sent as its content, and the message chain in `Error`. The server and client spans end with an error status. The
`IdentityResolver`'s caller can then rely on the property the issue's host needs: **every request whose identity
resolved, with capture on, ends in exactly one response record**, unless the host's own sink throws.

## 2. What the issue says, checked

| Claim | Verdict | Evidence |
|---|---|---|
| A failed forward counts `ForwardFailures` (L163), logs (L164), answers 502 (L165) and records nothing | **True** | probe case 1: client 502, 0 records, `ForwardFailures=1` |
| The identity is resolved by then (L190) | **True for a forward failure**, not for every exception the catch takes | the body read (L182-187) runs before L190; a caller reset there is #172's |
| `Diagnostics()` says the call is gone | **True** | L93; probe case 1 prints it |
| The diagram has no arrow for the call | **True** | no record; nothing else draws it |
| A host pairing `IdentityResolver` with records can't see the call end | **True**, and there are more such paths than the issue names | sink throws, decoder throws (probe 5, 6); disposal mid-forward (section 3.3) |
| `TestTrackingMessageHandler` closed this gap with `FailedSend` | **True** | `TestTrackingMessageHandler.cs` L274-309, `FailedSend.cs`, CHANGELOG 3.18.0 |
| The catch belongs around the forward (L281-296), not in `GuardedHandleAsync` | **True, and it matters more than the issue says** | it puts the record after the body read, so a caller hang-up mid-request (#172) can never be recorded as a failed call, whichever plan lands first |
| The 502 body could be the response content | **Recommended, for a reason the issue doesn't give** | a diagram never draws `Error` (section 3.2): without the body, the cause is nowhere in the diagram |
| `ForwardFailures` and its diagnostic can stay, minus "those calls are missing from the diagrams" | **Partly** | the count must stop including stops, caller hang-ups and capture faults. The wording must say which calls are recorded. The kind is debatable (Q4) |

The issue gives no wording for a timeout. The forward's own message there is `The operation was canceled.` (probe
case 2), which names nothing. Section 3.3 covers it.

## 3. Findings

### 3.1 A failed forward leaves no record and never ends its spans (#171)

`HandleAsync` records only after the forward returned (L313). When `SendAsync` or `ReadAsByteArrayAsync` throws
(L284-285), the exception leaves `HandleAsync` past L298-304, so:

- no `RequestResponseLog` is written for either half (probe cases 1-3: `sink got 0 record(s)`);
- the server and client `Activity` are started (L212-225) but never stopped or disposed (probe cases 1-3:
  `activities started 2 (Server, Client), stopped 0`). With an exporter attached, a trace backend never receives a
  failed forward, so the OpenTelemetry view has the same gap as the diagram. Nothing is left current afterwards:
  `HandleAsync` is async, so its `Activity.Current` change does not flow back to `GuardedHandleAsync`.

Measured exception shapes, through the tap's own `HttpClient` settings (probe tail, `FailedSend` words):

| Failure | `FailedSend.Status` | `FailedSend.Describe` (shortened) |
|---|---|---|
| refused | `!HttpRequestException` | `No connection could be made because the target machine actively refused it. (127.0.0.1:p) Caused by: …` |
| no answer within `ForwardTimeout` (the linked `deadline` token, L258-259) | `!TaskCanceledException` | `The operation was canceled. Caused by: The operation was canceled. Caused by: Unable to read data …` |
| body cut short by a FIN (`ResponseContentRead`, L284) | `!HttpRequestException` | `Error while copying content to a stream. Caused by: The response ended prematurely. (ResponseEnded)` |
| body cut short by an RST | `!HttpRequestException` | `Error while copying content to a stream. Caused by: Unable to read data from the transport connection: Connection reset by peer. Caused by: …` (Linux's words) |

The cut-short rows hold on Windows and on Linux, for a chunked body without its last chunk too (kronikol-a5's
`PROXY_TAP_HANG_UP_PLAN.harness/forward/forward-{windows,linux}.txt`): `SendAsync` with `ResponseContentRead` wraps
every upstream body cut short in `HttpRequestException`, whose `HttpRequestError` is `ResponseEnded` after a FIN and
`Unknown` after an RST. None of them is an `OperationCanceledException`, so none can be taken for a timeout (4.2).

The 502 body carries only the outer `ex.Message` (L165, L567). For a cut-short body that is `Error while copying
content to a stream.`, which hides the cause that `Describe` gives.

### 3.2 Where a failed call shows in a report

Checked in source (paths at `34145c44`):

- **Diagram.** `PlantUmlCreator.cs` L1168-1169 draws a status starting with `!` verbatim as the arrow label (any other
  status is title-cased, so 502 draws as `Bad Gateway`). The note is built from headers and content only (L424, drawn
  at L1183-1189). **Nothing under `src/Kronikol/PlantUml` reads `Error`.** An in-process failed send (content null,
  no headers) is a bare arrow with no note. Its cause is not in the diagram.
- **Data files and tools.** `error` is written to JSON (`ReportGenerator.cs` L5059), XML (L5419) and YAML
  (L5812-5813), and printed by `kronikol query http` (`QueryCommand.Payloads.cs` L325-326) and the failures digest
  (`FailuresDigestGenerator.cs` L743-745).
- **History.** The call's shape is `{Caller}>{Service} {Method} {Uri} {Status}` (`InteractionShape.cs` L19), so a
  failed forward reads `app>api POST /query !HttpRequestException` and a 502 would read `… 502`.
- **Failed-ness.** `InteractionStatus.IsError` (L101-111) counts `!X` as failed, which feeds `query flow
  --errors-only`, `services`, group-by and the digest's ordering. `OtlpSpanMapper.cs` L326-327 maps `!X` to span status
  Error with `Error` as its message.
- **Ingest.** `InteractionRecord` carries `error` (L62-68, L259, L346), so a tap writing NDJSON loses nothing.

So the 502 body as content is the only way the cause reaches the diagram, and it is literally what the client
received. Drawing `Error` in the note for every failed send (the handler's, Cosmos's and Kafka's too) would be the
general fix. That is a renderer change with its own Kronikol4J consequences, out of scope here (Q10).

### 3.3 Cancellation has three causes, and the tap can tell them apart

The forward's token is `deadline`, linked to the tap's lifetime `_cts` (L257-259). It is cancelled when:

1. **`ForwardTimeout` elapses.** The upstream didn't answer in time. That's a forward failure, but the message
   (`The operation was canceled.`) doesn't say so (probe case 2). The tap knows which deadline it set.
2. **The tap is being disposed.** `DisposeAsync` cancels `_cts` *before* closing the listener (L589-590, then L601),
   so a forward in flight at disposal throws, is counted as a forward failure, and gets a 502 the upstream didn't
   cause (kronikol-a5 found this while planning #172 and handed it here). If R1 recorded it as an upstream failure, a
   report would blame the service for the tap's own shutdown.

`HttpClient.Timeout` (L120, `ForwardTimeout` + 10 s) can't fire first, so these are the only two ways *the tap's*
tokens end a forward.

A third cancellation is not the tap's: **`ConnectTimeout`** (L115, 5 s by default) elapsing. Measured with
`probe-audit.cs` against a black-hole address (`10.255.255.1`) with `ConnectTimeout` 1 s, on Windows and on Linux:
the forward throws `TaskCanceledException` after about 1 s with **neither token cancelled**, no `HttpRequestError`,
and `Describe` reads `The operation was canceled. Caused by: A connection could not be established within the
configured ConnectTimeout.` Through the tap, the caller's 502 says only `proxy-tap: The operation was canceled.` So
the exception's type can't tell a connect timeout from the other two cancellations, but the tokens can: when neither
is cancelled, the cancellation came from inside `HttpClient`. 4.2 words it as its own kind.

Measured the same way, a refused connect is an `HttpRequestException` with `HttpRequestError.ConnectionError`, and
an unknown host one with `NameResolutionError` (both OSes), which 4.4 uses for the span's `error.type`.

### 3.4 Capture faults break the call or are blamed on the upstream (R2)

Measured on 4.18.1:

- **An `IdentityResolver` that throws fails the caller's request** (probe case 4): the upstream is never reached,
  the client gets 502, and `ForwardFailures=1`. The resolver is host code, documented as "return … null to use the
  built-in rules" (`ProxyTapOptions.cs` L95-99), and a lookup that throws when no test holds a device is an easy
  mistake. The tap's own contract is that capture never sits on the request path (class remarks, L24-26).
- **A sink that throws is reported as two things that didn't happen** (probe case 5). The client got its 200, but
  `ForwardFailures=1` ("could not be forwarded and were answered 502") *and* "1 of 1 forwarded request(s) carried no
  test identity", because `_handled` was counted (L308) and `_captured` wasn't (L317). Then `TryRespondBadGateway`
  runs on a response that is already closed.
- **`TryDecodeBody` throws on a `br` body that isn't brotli** (probe case 6 and its last line). `BrotliStream` throws
  `InvalidOperationException` ("Decoder ran into invalid data."), which the filter at L486 doesn't take. The method is
  public and documented "False means opaque" (L447). Inside `Record`, both bodies are decoded before either half is
  logged (L382-383), so the whole pair is lost and blamed as in probe case 5. A gzip or deflate body that is not
  what it says is handled (`InvalidDataException`), so only `br` has this.
- **An `ActivityListener` that throws fails the call** (`probe-audit.cs`, both OSes). With a listener on
  `Kronikol.ProxyTap` whose `ActivityStarted` throws, the request is never forwarded. With one whose `ActivityStopped`
  throws, the upstream answers, but the success path disposes the spans (L303-304) before it responds (L307), so the
  exception skips the answer. Either way the caller gets 502 (`proxy-tap: listener threw`), nothing is recorded, and
  `ForwardFailures` is 1. A listener is host code (an OpenTelemetry processor, or a test's own listener), and the
  exception reaches the tap unguarded, so it is the tap's to catch.
- **The 502 body is not valid JSON when the message has a control character** (probe case 4: a raw line break). The
  hand-built body escapes `\` and turns `"` into `'` (L566-567), which also changes the message. Real network
  messages rarely contain a line break, but R1 makes this body a recorded content, and a resolver's or a future
  wrapper's message can.

Checked and not affected: `InFlightIdentityRegistry.Register` is Kronikol's own class and throws only on a blank
service name, which `Validate` rejects, and `TrackingHeaderValue.Decode` is written never to throw.

### 3.5 A request target naming another host leaves the configured upstream (R0, security)

`HandleAsync` builds the forwarded request from the raw path and query, `new HttpRequestMessage(method,
pathAndQuery)` (L261). That is a *relative* URI, which `HttpClient` resolves against `BaseAddress` by RFC 3986. A path
starting with `//` is a network-path reference there, and it **replaces the host**. `Record` resolves it the same way
(L376), so the capture names the wrong host too. Measured with `probe-network-path.cs`, with `ForwardBaseUri`
pointing at a server named `configured`:

```
request-target /plain                       -> client got: HTTP/1.1 200 OK / body 'configured'
request-target //127.0.0.1:51525/x          -> client got: HTTP/1.1 200 OK / body 'OTHER'
request-target //api/v1/items               -> client got: HTTP/1.1 502 Bad Gateway / body '{"error":{"code":502,"message":"proxy-tap: No such host is known. (api:80)"}}'
   recorded request uri: http://127.0.0.1:51525/x
```

The audit probe (`probe-audit.cs`) found two more forms, the same on Windows and on Linux:

- `/\127.0.0.1:{other}/x` reaches the other host too, and is recorded under it. The listener rewrites the backslash as
  a slash in `request.Url`, so `pathAndQuery` arrives as `//127.0.0.1:{other}/x`. An encoded backslash (`/%5C…`)
  stays on the configured host.
- `///127.0.0.1:{other}/x` is answered 502 (`proxy-tap: Invalid URI: The hostname could not be parsed.`) and counted
  as a forward failure: relative resolution reads an empty authority and throws `UriFormatException`, which the
  catch-all takes.

An absolute-form target naming another host never reaches that host. The managed listener answers it itself (404 Not
Found), and so does http.sys when the target's host name is not the one the tap's prefix uses (400 Bad Request). When
it is, http.sys hands the request to the tap under the tap's own authority, whatever port the target names, and the
tap forwards its path to the configured upstream (measured at R0 on 4.19.0, `r0/absform.cs`). One that names the tap
is forwarded as its path.

That's two defects in one line:

- **Correctness.** A legitimate path with a doubled leading slash (a client that joins `base + "/" + path` badly)
  goes to the wrong host and the caller gets a 502. One with a tripled slash always gets a 502.
- **Security (SSRF).** Anyone who can reach the tap's port can make it fetch any host the tap's machine can reach.
  `ListenHost` defaults to `localhost`, but a suite that drives devices through a tap (#170's reporter does) binds a
  LAN address, and on a CI VM the reachable hosts include the cloud metadata endpoint. The tap's answer goes back to
  the requester. Binding to `localhost` limits who can send such a request, not whether one can be sent: browsers keep
  a doubled slash in a URL's path, so a page open in a browser on that machine can make the tap send one, though it
  can't read the answer unless CORS allows it. That last point is reasoned, not measured.

The fix doesn't depend on #171. Build the forward URI from the base's scheme and authority plus the raw path and
query (`ForwardBaseUri.GetLeftPart(UriPartial.Authority) + pathAndQuery`), never by relative resolution, and record
that same URI. Every ordinary path forwards exactly as today. A base path in `ForwardBaseUri`, documented as "scheme
+ host + port", is already ignored for any path starting with `/`, so nothing that works now changes.

**`OtlpTap` in forward mode has the same defect, and a wider one** (spotted by kronikol-a5 while planning #172). It
parses HTTP/1.1 itself over sockets and forwards `new HttpRequestMessage(new HttpMethod(request.Method),
request.Target)` (`OtlpTap.cs` L393), with the raw request-target from the request line and `BaseAddress =
ForwardBaseUri` (L171). Its reader already strips an absolute-form target down to a path for routing (`PathOf`,
`HttpRequestReader.cs` L107-115), but the forward uses the raw target. When `ForwardBaseUri` is set, every request is
forwarded, whatever its method and path (L345-348). Measured with `probe-otlp-network-path.cs`
(`probe-otlp-network-path-output-4.18.1.txt`):

```
POST /v1/traces                                   -> client got: HTTP/1.1 200 OK / body 'configured'
POST //127.0.0.1:65075/v1/traces                  -> client got: HTTP/1.1 200 OK / body 'OTHER'
POST http://127.0.0.1:65075/v1/traces             -> client got: HTTP/1.1 200 OK / body 'OTHER'
GET http://127.0.0.1:65075/anything              -> client got: HTTP/1.1 200 OK / body 'OTHER'
```

ProxyTap is not open to the absolute form, because its listener answers one naming another host itself (fact 3 pins
that). OtlpTap is open to both forms. The audit probe adds three facts about OtlpTap (both OSes). A raw backslash
stays on the configured host, since relative resolution keeps the base and only `HttpListener` rewrites it. A `///`
target throws `UriFormatException` in `ForwardAsync`, outside its catch's filter (L414), so the exporter gets no answer
at all and its connection is closed. `OPTIONS *` is forwarded as `OPTIONS /*`. Only forward mode is affected:
`ForwardBaseUri` is null by default. Its `ListenHost` is documented with `host.docker.internal` (`OtlpTapOptions.cs`
L39-44), the set-up where containers reach the tap across a network boundary. No other `HttpRequestMessage` in
`src/` is built from a request's target: `OtlpExporter.cs` L137 posts to its fixed `Endpoint`, and `TcpTap` forwards
bytes, not requests.

---

## 4. Design

### 4.1 Who owns what (after #172)

#172's plan makes `GuardedHandleAsync` the one owner of the **response**: a flag records whether the tap closed it
(`RespondAsync` or `TryRespondBadGateway` reaching `Close()`), its `finally` aborts any response the tap didn't close,
and the body read (L182-187) gets its own catch that ends the exchange as "caller hung up". This plan builds on that
shape and doesn't touch it.

In #172's plan (`PROXY_TAP_HANG_UP_PLAN.md`, Appendix A, committed as `11912425` and checked again in `e5fd126d`;
its `.prototype.patch` applies to `34145c44` and `9cf89794`), that shape is an internal `CallerAnswer`:
- `GuardedHandleAsync` creates it and calls `HandleAsync(context, answer)`.
- `RespondAsync` and `TryRespondBadGateway` take it and call `answer.Close()`, with their catches unchanged.
- The body read's own catch sets `StatusIfUnclosed = 400` and returns.
- `EndUnclosed()` runs in the `finally`, before `_inflight` is decremented.

#172 changes nothing in L257-296 and leaves the generic catch's counting alone. So R1's forward catch goes in as 4.2
describes and answers through `TryRespondBadGateway(answer, …)`, and R1 makes the change to the generic catch's
counting (4.1's last paragraph).

Two of #172's measurements bear on the 502 path (its harness,
`PROXY_TAP_HANG_UP_PLAN.harness/listener/listener-{windows,linux}.txt`):

- On the **managed listener**, `Response.Abort()` before the head has gone out is not an abort. It writes the head
  with whatever `StatusCode` is set (200 by default) and `Content-Length: 0`, then closes (dotnet/runtime#88196,
  open). So `TryRespondBadGateway` must keep setting 502 before anything that can fail (L565 does today). A 502
  whose write then fails still reaches the caller as a 502 head when #172's `finally` aborts it.
- On **Windows**, `Abort()` after `HttpListener.Close()` throws `ObjectDisposedException`, and `Close()` on a
  connection http.sys already dropped throws `HttpListenerException` 1229. #172's helper guards both, and
  `TryRespondBadGateway`'s catch (L573-576) already takes both.

This plan makes `HandleAsync` the one owner of the **record**: a forward failure is handled where the forward
happens, with the identity, the trace ids and the request body in scope. With both plans landed,
`GuardedHandleAsync`'s generic catch only sees faults the tap didn't classify (a bug). #172's prototype keeps its
`TryRespondBadGateway(answer, ex.Message)` and adds `answer.EndUnclosed()` there, and R1 keeps both: the 502 attempt
is what a caller with no answer yet should get, and on an answer already closed it is a no-op (its catch takes the
throw). The catch logs `proxy-tap error:` as today and counts by what the caller got: a forward failure when its 502
was the answer (`answer.Closed` was false), otherwise a capture fault (4.5).

kronikol-a5's check of #172's plan (`e5fd126d`) changed three things this builds on. The release's `EndUnclosed()`
swallows every exception, since it runs in the `finally` before `_inflight` is decremented. The `StatusCode` setter
does not throw once the head has gone, only `ObjectDisposedException` on a closed response, so a late 502 is harmless
and never sent. And after #172, the typed catch at L157 (`HttpListenerException` or `ObjectDisposedException`, "the
caller hung up") no longer sees the listener, because the body read and the answer's write have their own catches.
What can still land there is an `ObjectDisposedException` from another step, and #172 leaves those to this plan. After
R1 and R2 there are two: the `HttpClient` that `DisposeAsync` releases after its 2 s drain, which is `Stopping` in the
forward's catch (4.2), and a disposed sink, which is a capture fault in the record's catch (4.6). So R1 removes the
typed catch. Anything else that reaches `GuardedHandleAsync` goes to the generic catch, which logs it, answers 502 if
the caller has no answer yet and counts it, rather than being silently taken for a hang-up.

One caller failure reaches no catch in either plan. On Linux, a chunked request cut short by a FIN is read as if it
were whole and forwarded, and the caller gets the upstream's answer (#172's harness `chunked/`, and its Q6). R1
records it as the ordinary exchange it looks like.

### 4.2 R1: the forward's catch

Around the forward (today's L282-296), in `HandleAsync`:

```csharp
catch (Exception ex)
{
    var failure = ForwardFailure.Classify(ex, deadline.Token, lifetime);
    // steps 1-6 below
}
```

The try holds only the forward's statements (`SendAsync`, the body read, the header reads), so whatever they throw
is the forward's, and the catch takes every exception. `failure` is one of:

| Kind | When | `Error` text (`Caused by:` chain from `FailedSend.Describe(ex)`) | `ForwardFailures` |
|---|---|---|---|
| `Failed` | any other exception from the forward: refused, DNS, TLS, a body cut short (3.1) | `FailedSend.Describe(ex)` unchanged | +1 |
| `ConnectTimedOut` | an `OperationCanceledException` with neither token cancelled and a `TimeoutException` in its chain (3.3) | `proxy-tap: could not connect to {base authority} within ConnectTimeout ({t}). Caused by: …` | +1 |
| `TimedOut` | an `OperationCanceledException` with `deadline` cancelled and `lifetime` not | `proxy-tap: no answer from {base authority} within ForwardTimeout ({t}). Caused by: …` | +1 |
| `Stopping` | an `OperationCanceledException`, or the `ObjectDisposedException` of a client `DisposeAsync` already released, with `lifetime` cancelled | `proxy-tap: the tap stopped before {ServiceName} answered. Caused by: …` | no |

`lifetime` is checked before `deadline`, since cancelling it cancels `deadline` too. The kinds that name a timeout or
the stop need an `OperationCanceledException`: a refusal that lands as the deadline passes or the tap stops is still
`Failed`, because the upstream did refuse. An `OperationCanceledException` that matches no row (neither token, no
`TimeoutException`) is `Failed` with its `Describe` text.

Then, in order:

1. **End the spans** (4.4).
2. **Answer the caller** 502 through `TryRespondBadGateway` with the body from 4.3. For `Stopping`, try the same
   answer: the listener may still be open (L589-601), and if it isn't, #172's `finally` aborts the response.
3. **Record** the call when `capture && identity is not null`, exactly as the success path decides (L310-311):
   - the request half as today (same method, URI, headers, body, encoding, `requestStarted`, ids, `ignore`, phase,
     categories);
   - the response half with `StatusCode = FailedSend.Status(ex)` (the exception's own type, so a timeout is
     `!TaskCanceledException` as the in-process handler records it), `Error` = the text above, headers
     `Content-Type: application/json` (passed through `FilterHeaders`), `Timestamp` = the moment the forward gave up,
     and the same `ActivityTraceId` and `ActivitySpanId` the request half carries;
   - its `Content` is the 502 body the tap answered with, capped like any body, and recorded whether or not the
     caller was still there to read it, as the success path records an answer whose write failed (L555-558). With
     `CaptureBodies` off it is null, like any body, and the cause is in `Error` and the arrow's status.
4. **Count**: `ForwardFailures` per the table. `RequestsHandled` and `RequestsCaptured` keep their meaning, which
   is forwarded exchanges (4.5).
5. **Log**: `proxy-tap error: {Error}` for a counted kind, the prefix today's catch-all logs, so a host watching its
   log for it still sees every forward failure; `proxy-tap stopping: {Error}` for `Stopping`.
6. **Return** normally. The exception is not rethrown: the call has been answered and recorded, and nothing above
   `HandleAsync` has anything left to do with it.

The record goes through the same `Record` method as a success. `Record` gains a response shape (status as
`OneOf<HttpStatusCode, string>`, headers, body, encoding, error) in place of its five response parameters, so the
two paths can't drift.

### 4.3 The 502 body

`{"error":{"code":502,"message":"proxy-tap: <text>"}}`, written with `Utf8JsonWriter` and
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. The output is always valid JSON, a quote stays a quote, and non-ASCII
text stays readable. `<text>` is the same text as `Error`. For an ordinary refusal it is unchanged from today. For a
cut-short body it now names the cause (`… Caused by: The response ended prematurely. (ResponseEnded)`), and for a
timeout it names the deadline. The shape and the `proxy-tap:` prefix that clients and
`Upstream_down_answers_502_from_the_tap` (L524) match on stay. The builder is an `internal static` method so the
escaping can be tested directly; the tests project already has `InternalsVisibleTo`
(`Kronikol.Extensions.ProxyTap.csproj` L18).

### 4.4 Spans

On every forward failure, before the answer:

- **client span:** status `Error` with the `Error` text as its description, tag `error.type` in .NET's own words for
  it, then dispose. No `http.response.status_code`, since none was received (OpenTelemetry HTTP semantic conventions).
  .NET's `HttpClient` spans and metrics take `error.type` from `DiagnosticsHelper.TryGetErrorType` in
  System.Net.Http: an `HttpRequestException`'s `HttpRequestError` as `connection_error`, `name_resolution_error`,
  `secure_connection_error`, `response_ended` and so on, else the exception's full type name. The tap uses the same
  table (`HttpRequestError` exists on net8.0), so a refused forward reads `connection_error` beside the services' own
  client spans, a body cut short by a FIN `response_ended`, one cut by an RST `System.Net.Http.HttpRequestException`
  (its `HttpRequestError` is `Unknown`, for which .NET falls back to the type name; 3.1), and a timeout
  `System.Threading.Tasks.TaskCanceledException`;
- **server span:** tag `http.response.status_code` = 502 (what the tap answered), status `Error`, then dispose.

`Activity.AddException` would be nicer, but it is .NET 9+ and the package also targets net8.0
(`Directory.Build.props` L6). Tags work on all three. The success path's span handling (L298-304) is unchanged.

### 4.5 Counters and diagnostics

Public counters keep their documented meaning, and only their doc comments get sharper:

- `RequestsHandled`: exchanges answered from the upstream. A failed forward is not one, as today
  (`A_failed_forward_is_counted_and_reported_as_capture_degraded` asserts 0, L637).
- `RequestsCaptured`: of those, the ones recorded. A recorded failed forward is **not** added here. Otherwise
  `handled - captured` (L96) goes wrong: one recorded failure plus two unattributed successes would read "1 of 2"
  instead of "2 of 2".
- `ForwardFailures`: upstream failures only (`Failed`, `ConnectTimedOut`, `TimedOut`), plus the generic catch's
  502 for an unclassified fault (4.1). Not `Stopping`, not capture faults, and after #172 not caller hang-ups.

New internal counters behind `Diagnostics()` (no public surface), each counted where its outcome happens rather than
worked out by subtraction: counted failures recorded, counted failures not recorded for want of an identity, forwarded
exchanges not captured for want of an identity, and capture faults (R2) with the first one's type and message. Today
the no-identity line is `handled - captured` (L96), which a record that threw (probe case 5) turns into a false
"carried no test identity"; a direct count can't.

`Diagnostics()` lines:

- **Forward failures**, today's text (L93) up to its semicolon, then a clause by how many were recorded (the kind is
  Q4):
  - all recorded: `app→api: 3 request(s) could not be forwarded and were answered 502 Bad Gateway — the upstream was unreachable, timed out or the exchange threw; each is recorded as a failed call`
  - none: `…; they carried no test identity and were not recorded`
  - mixed: `…; 2 are recorded as failed calls, 1 carried no test identity and was not recorded`

  Only the clause after the semicolon changes, so a host matching today's text up to there keeps working. It says
  "recorded" rather than "drawn": a call sent with the ignore header is recorded and not drawn.
- **No identity:** unchanged text, from its own counter.
- **Capture faults (R2), new:** `app→api: 2 capture fault(s): the IdentityResolver, the sink, the body decoder or an ActivityListener threw, so a call may be missing, unattributed or untraced (the first: IOException, disk full); what the callers got was not changed`. A resolver fault whose call the built-in rules then attributed is still one, since the host's rule did not run.
  Kind `CaptureDegraded`, whose documented meaning ("a capture component lost or skipped data; forwarding was never
  affected", `DiagnosticEntry.cs` L41-46) this matches exactly.

### 4.6 R2: capture faults

- **`IdentityResolver` throws:** log `proxy-tap: IdentityResolver threw …`, count a capture fault, and continue as
  if it returned null (the built-in rules decide). The call is forwarded as usual. Q7 has the alternatives.
- **`Record` throws** (the sink, or anything else in it): catch it in `HandleAsync` around the record (success and
  failure paths), log it, count a capture fault and return. The caller already has its answer, so no 502 is
  attempted and `ForwardFailures` doesn't move. A sink that took the request half and threw on the response half
  leaves half a pair; that's the host's sink, and the diagnostic says so.
- **`TryDecodeBody`** adds `InvalidOperationException` to its filter (L486), so a `br` body that isn't brotli is
  recorded as `<N bytes, undecodable content-encoding 'br'>`, as its doc comment already promises.
- **An `ActivityListener` throws** (3.4): starting the spans and ending them each go through a guard that logs
  `proxy-tap: an ActivityListener threw …` and counts a capture fault. A throw at the start leaves that span null,
  and the call goes on as if nobody listened: the traceparent choice (L228-254) already handles a null client span.
  A throw at the end is caught there, so the answer still goes out, and the failure path ends its spans through the
  same guard. #170 rewrites L189-254, where the spans start, and releases first (section 8), so R2 puts
  the start's guard around #170's code, not today's. That code clears `Activity.Current` before the spans start
  under a parent with no span id (kronikol-ef), and the guard must leave `Current` as it found it: `Activity.Start`
  appears to make the span current before it notifies listeners, so a throw there may leave an unreachable span
  current. R2's red step measures it, and the guard restores `Current` and ends that span if it is the tap's.

### 4.7 R0: the forward URI

`HandleAsync` builds `forwardUri = new Uri(ForwardBaseUri.GetLeftPart(UriPartial.Authority) + pathAndQuery)` once,
passes it to `HttpRequestMessage` as an absolute URI, and passes it to `Record` in place of L376's `new
Uri(_options.ForwardBaseUri!, pathAndQuery)`. `pathAndQuery` always starts with `/` (HttpListener's `Url`), so the
concatenation is a well-formed URI for every target. Measured with `probe-audit.cs` on both OSes, the rule keeps
`//other:6000/x`, `/\other:6000/x` and `///other:6000/x` on the configured authority with their paths, where today's
relative resolution sends the first to `other` and throws on the third, and it leaves `/plain?q=1` and dot segments
as today. An absolute-form target naming another host never reaches this code (3.5), and a fact pins that.

**OtlpTap** (`OtlpTap.cs` L393): forward to `ForwardBaseUri.GetLeftPart(UriPartial.Authority)` plus the target's
origin form, in this order:

1. A target starting with `/` is origin form, taken as it is. This test comes first because on Linux
   `Uri.TryCreate("/v1/traces", UriKind.Absolute, …)` succeeds as `file:///v1/traces` (measured; it fails on Windows),
   so an absolute-URI test first would misread every ordinary target there.
2. An absolute `http` or `https` URI is reduced to its path and query, as `PathOf` already does for routing, but
   keeping the query.
3. Anything else gets `400 Bad Request` rather than a guessed forward: the asterisk form (`OPTIONS *`, forwarded as
   `OPTIONS /*` today), the authority form (`CONNECT host:port`), and a target with no leading slash
   (`host:port/x`, forwarded today as a path under the base).

Both taps' `Validate` accepts any absolute `ForwardBaseUri` (L162-163 here, `OtlpTapOptions.cs` L145-146), but
`HttpClient` forwards only `http` and `https`. Measured with `probe-scheme.cs` (`probe-scheme-output-4.18.1.txt`): a
ProxyTap given `ftp://` or `file:///` starts, then answers every request 502 (`proxy-tap: The 'ftp' scheme is not
supported.`) and counts each as a forward failure. R0's `Validate` requires `http` or `https` in both taps, so
`StartAsync` throws, as its doc comment says it does for invalid options. A fact pins it for each tap.

This plan first said that a forward URI that can't be built (a `UriFormatException`) also gets 400 in both taps. Under
the rule nothing reaches that: measured at R0 (`r0/uri.cs`), every target starting with `/` builds (an unescaped `%`,
control characters, non-ASCII, brackets, 70,000 characters), so R0 adds no such answer, and a throw there would be a
bug the generic catch reports. The two taps are separate assemblies, so each applies the rule itself, and
the same table of targets is pinned in both test projects so the two can't drift. kronikol-a5's #172 plan changes
`ForwardAsync`'s answer write and chunked bodies, not L393.

---

## 5. Releases

| Release | Content | Bump | Depends on |
|---|---|---|---|
| **R0** | 4.7: a request target never leaves `ForwardBaseUri`, in `ProxyTap` (`//host`, `/\host`; `///` no longer a 502) and in `OtlpTap`'s forward mode (`//host` and absolute form; asterisk, authority and slashless targets answered 400); both `Validate`s require an `http` or `https` base | patch (a security fix, nothing new to call) | nothing; ship first (Q8) |
| **R1** | 4.2-4.5: failed forwards recorded, spans ended, timeouts and the stop told apart, 502 body escaped, counters and wording | patch, recommended (Q1) | #170's R0 and R1 and #172's release (section 8) |
| **R2** | 4.6 plus R2's half of 4.5: resolver, record, decoder and listener faults | patch | R1 (same catches, same counters) |

Recommended: R1 and R2 in one release (Q9), since they rewrite the same catch structure and the diagnostics in 4.5
span both. Version numbers are claimed with "taking X now" at push time. 4.18.2 went to
kronikol-f1's #136 audit (published 2026-10-13); kronikol-ef took 4.18.3 for #170's R0 and 4.19.0 for its R1 (a minor), both published or pushing on 2026-10-13, so this plan starts at 4.19.1.

## 6. Tests (TDD: each written red against the release before it, then made green)

All in `tests/Kronikol.Tests.ProxyTap/`. Failure facts go in a new `ProxyTapFailureTests.cs` and reuse
`StubUpstream`, `ListSink`, `ClosedPort` and `EventuallyAsync` from `ProxyTapTests.cs`. Two new stubs: a
**silent upstream** (a `TcpListener` that accepts and never answers) and a **cut-short upstream** (sends a status
line with `Content-Length: 100` and 10 bytes, then closes with a FIN or resets), both as in `probe.cs`. A fact that
resets a connection, the upstream's or the caller's, sends an RST alone: `LingerState(true, 0)`, then
`Socket.Dispose()`. `TcpClient.Dispose()` shuts the socket down first, so a FIN goes out before the RST, and Linux
reads the FIN as the end of the stream (kronikol-a5's finding, #172's harness `reset/`). After #170 lands, a trace id can
equal the test id (kronikol-ef's note), so trace facts assert ids against the span or the request half, never "not
the test id".

**R0**

1. `A_request_target_naming_another_host_is_forwarded_to_the_forward_base`, a theory over raw `GET
   //127.0.0.1:{other}/x` and `GET /\127.0.0.1:{other}/x`. The configured stub sees path `//127.0.0.1:{other}/x`, the
   other stub sees nothing, and the recorded URI's authority is the configured one. Red on 4.18.1: the other stub
   answers both (measured, `probe-audit.cs`).
2. `A_path_with_leading_slashes_reaches_the_upstream`, a theory over `//api/v1/items` and `///api/v1/items`: each
   gets a 200 from the configured stub with its path as sent. Red on 4.18.1: the first 502s (no host `api`), the
   second 502s with `Invalid URI` and counts a forward failure.
3. `An_absolute_form_target_naming_another_host_never_reaches_it`: `GET http://localhost:{other}/x` never reaches the
   other stub. Either the listener answers it itself, or, on http.sys with the host name the tap's prefix uses, it
   hands the tap the request under the tap's own authority and the tap forwards `/x` to the configured stub (3.5), so
   the fact accepts both. A sibling, `GET http://localhost:{tap}/self`, reaches the configured stub as `/self`.
   Pins, green today; they hold the claim in 3.5 that ProxyTap needs nothing for the absolute form.
3a. In `tests/Kronikol.Tests.Otlp`, `A_target_naming_another_host_is_forwarded_to_the_forward_base`, a theory over
   an ordinary `POST /v1/traces?x=1` (which kills the reordering mutation on Linux; the targets below do not parse as
   absolute there) and the same table: `POST //127.0.0.1:{other}/v1/traces`, `POST http://127.0.0.1:{other}/v1/traces?x=1` (the query
   kept), `GET http://127.0.0.1:{other}/anything` and `POST ///127.0.0.1:{other}/v1/traces`. Each reaches the
   configured collector stub with its origin form and the other stub sees nothing. Red on 4.18.1: the other stub
   answers the first three, and the fourth gets no answer. A second theory, `A_target_with_no_origin_form_gets_400`:
   `OPTIONS *`, `CONNECT 127.0.0.1:{other}` and `GET 127.0.0.1:{other}/x`. Red on 4.18.1: the first and third are
   forwarded and the second 502s. Both theories run on Linux too, where the absolute-URI test misreads `/v1/traces`
   if it comes first (4.7).
3b. `A_forward_base_that_is_not_http_is_rejected_at_start`, in both test projects: `ftp://` and `file:///` make
   `StartAsync` throw `ArgumentException` naming `ForwardBaseUri`. Red on 4.18.1: ProxyTap starts and answers 502
   (`probe-scheme.cs`); OtlpTap's red is measured in R0's red step.

**R1**

4. `A_forward_the_upstream_refused_is_recorded_as_a_failed_call`: two records with the same `RequestResponseId` and
   `TraceId`. The request half is as on success (method, URI, body, headers, identity). The response half has
   status `!HttpRequestException`, `Error` starting with the refusal message and holding `Caused by:`, `Content`
   equal byte for byte to the body the client received, `Content-Type` `application/json`, a timestamp at or after
   the request's, and the request half's `ActivityTraceId` and `ActivitySpanId`.
5. `A_forward_that_timed_out_names_the_deadline`: `ForwardTimeout` 1 s against the silent upstream. Status
   `!TaskCanceledException`, `Error` starting `proxy-tap: no answer from 127.0.0.1:{p} within ForwardTimeout
   (00:00:01)`, the 502 body's message the same, `ForwardFailures` 1.
6. `A_response_cut_short_is_recorded_with_its_cause`, a theory over the cut-short upstream closing with a FIN and
   with an RST: status `!HttpRequestException` for both, and `Error` and the 502 body both contain `The response
   ended prematurely` after the FIN and `Unable to read data from the transport connection` after the RST (the
   words both platforms share; 3.1).
7. `A_forward_cut_by_disposal_is_recorded_and_not_counted_as_a_forward_failure`: silent upstream, the request in
   flight, dispose. One response record whose `Error` starts `proxy-tap: the tap stopped before api answered`, and
   `ForwardFailures` 0.
7a. `Forward_failures_are_classified_by_the_token_that_ended_them`, unit facts on `ForwardFailure.Classify` with
   the measured shapes: a `TaskCanceledException` holding a `TimeoutException` with neither token cancelled is
   `ConnectTimedOut`, and its wording names `ConnectTimeout (00:00:01)`; the same with `deadline` cancelled is
   `TimedOut`; with `lifetime` cancelled, `Stopping`; an `HttpRequestException` with either token cancelled is
   `Failed`. End to end, a connect timeout needs an address that drops connects, which a CI runner may not
   route the same way, so the end-to-end check stays in the harness (`probe-audit.cs`) and the facts hold the rule.
8. `A_failed_forward_ends_its_spans_with_an_error_status`: with a listener, the client span stops with status
   Error, tag `error.type` = `connection_error` (refused) and no status-code tag; the server span stops with status
   Error and `http.response.status_code` 502. Red on 4.18.1: neither stops. A unit fact maps each `HttpRequestError`
   to .NET's word for it, `Unknown` (an RST mid-body) to `System.Net.Http.HttpRequestException`, and a
   `TaskCanceledException` to its full type name.
9. `An_unattributed_failed_forward_is_recorded_only_when_opted_in`: no identity gives 0 records and the "carried no
   test identity" wording on the forward-failure line. `CaptureUnattributedRequests` gives two records under the
   fallback identity.
10. `An_ignored_failed_forward_is_recorded_as_ignored`: the ignore header gives `TrackingIgnore` on both halves.
11. `The_bad_gateway_body_is_valid_json_and_keeps_the_message_exactly`: the internal builder with a quote, a
    backslash, a line break, a tab and `é`. `JsonDocument` parses it, and `error.message` reads back as
    `proxy-tap: ` plus the input.
12. `Every_resolved_request_ends_in_one_response_record`: the issue's invariant. A resolver counts starts and a sink
    counts response halves, across refused, timed out, cut short, healthy and disposal-cut calls. The two counts
    are equal.
13. `Forward_failure_diagnostics_say_which_calls_are_recorded`: all-recorded, none-recorded and mixed wordings, and the
    prefix `A_failed_forward_is_counted_and_reported_as_capture_degraded` matches (L640) still holds.
14. `A_failed_forward_survives_the_ndjson_round_trip`: through `NdjsonInteractionWriter` and
    `NdjsonInteractionReader`, `statusCode` reads `!HttpRequestException` and `error` is kept.
15. `A_failed_forward_is_drawn_with_its_cause`: through `ReportGenerator` as in
    `Secrets_never_reach_the_report_data_file_and_the_report_renders_the_hop` (L411). The scenario's PlantUML has the
    return arrow labelled `!HttpRequestException` and a note holding the 502 body's message. Asserted on the emitted
    arrow and note lines, never a bare substring (the report-HTML assertion rule).
15a. `A_failed_forward_is_recorded_though_its_caller_hung_up`: the silent upstream with `ForwardTimeout` 1 s, and
    the caller resets (an RST alone) after the tap took the request. The forward times out, the 502 write fails, and
    the call is still recorded as timed out with `ForwardFailures` 1. Run on Linux too, where the managed listener
    sees the reset differently (#172's measurements).

**R2**

16. `A_throwing_IdentityResolver_does_not_fail_the_call`: the upstream is reached, the client gets 200, the call is
    attributed by the built-in rules (a test-id header), `ForwardFailures` 0, and the capture-fault line names
    `KeyNotFoundException`. Red on 4.18.1: client 502.
17. `A_throwing_sink_is_a_capture_fault_not_a_forward_failure`: the client gets 200, `ForwardFailures` 0, one
    capture-fault line and no "carried no test identity" line. Red on 4.18.1: both false lines.
18. `TryDecodeBody_returns_false_for_a_br_body_that_is_not_brotli`: returns false and doesn't throw. End to end, the
    call is recorded with `<36 bytes, undecodable content-encoding 'br'>`.
19. `A_throwing_ActivityListener_does_not_fail_the_call`, a theory over a listener that throws in `ActivityStarted`
    and one that throws in `ActivityStopped`: the client gets the upstream's 200, the call is recorded,
    `ForwardFailures` is 0 and the capture-fault line names `InvalidOperationException`. A third case fails the
    forward under a listener that throws at the stop: the failure is still answered 502 and recorded. Red on 4.18.1:
    both first cases answer 502 with nothing recorded (measured, `probe-audit.cs`). Listeners are process-wide, so
    every fact that attaches one (8 and 19) joins the non-parallel xUnit collection that #170's R0 (4.18.3) gives
    `ProxyTapTraceContextTests` for the same reason (kronikol-ef). The listener still throws only for its own tap's
    spans, in case one outlives its fact: the server span is named `tap {Name}` with a `Name` unique to the fact, and
    the client span's parent is that span.

Existing facts that change: `A_failed_forward_is_counted_and_reported_as_capture_degraded` keeps its assertions and
gains the new tail. `Upstream_down_answers_502_from_the_tap` is unchanged.

**Proofs** (in the plan harness, as for earlier plans): copy the new facts onto the previous release with the new
code stubbed, and confirm each fails on its own assertion. Mutations to kill, at least: no span end on failure; the
response half's content null; `Error` without the tap's wording for `TimedOut`; `Stopping` counted in
`ForwardFailures`; a recorded failure added to `RequestsCaptured`; the 502 body built by the old concatenation; the
resolver's exception rethrown; the record's catch removed; `InvalidOperationException` dropped from L486's filter;
R0 reverted to relative resolution in either the forward or `Record`; `Classify` without its
`OperationCanceledException` test (a refusal racing the deadline read as a timeout); `ConnectTimedOut` folded into
`Failed`; `error.type` as the type name for an `HttpRequestException` with a known `HttpRequestError`; either span
guard removed; the generic catch counting by type rather than by `answer.Closed`; OtlpTap's origin-form test moved
after the absolute-URI test. That last one is killed only on Linux, so the Otlp theories must run there.

**Checks** before each release: the ProxyTap suite in Debug and Release on Windows **and on Linux** (podman recipe in
memory `linux-runner-via-podman-wsl`; the managed listener behaves differently on the response side, per #172's
measurements). After the version bump, the core suite, the full Playwright suite (serialized with peers: "starting
Playwright" / "Playwright done") and `release.slnf` in Release for every target, per CLAUDE.md.

## 7. Documentation and ledger

- **Wiki** `Integration-ProxyTap-Extension.md` (origin/master `85abe19`): the page doesn't mention `502`,
  `ForwardFailures` or `Diagnostics()` today. Add a section, "When the upstream fails", covering the 502 body, the
  failed call in the diagram (status, note, `Error` in the data files), the wording for the two timeouts and the
  stop, the spans, and the diagnostics with their counters. Add one line under the options table that `ForwardTimeout` names the
  deadline in the record. kronikol-ef edits the same page's options table for #170: different lines, applied at
  each release's publication and committed at once.
- **CHANGELOG:** one entry per release, saying which part moved and why. R1 states the behaviour change: sinks now
  receive failed forwards, the forward-failure diagnostic's wording changes, and the 502 body's message carries the
  cause chain.
- **README:** no change. Its ProxyTap lines (L159, L306) describe the tap in general.
- **Kronikol4J ledger:** one line per release saying there is nothing to port. `../Kronikol4J` has no proxy tap,
  and no renderer output changes.

## 8. Coordination (agreed by message on 2026-10-13)

- **#172 (kronikol-a5):** plan on origin/main as `11912425`, checked again in `e5fd126d`. Caller side only. The
  `Abort()` paths, the body read's own catch, and `GuardedHandleAsync` as owner of the response with its closed flag
  and aborting `finally`. Capture-side faults, the 502 JSON and the spans are this plan's. a5 cites
  `probe-output-4.18.1.txt` rather than re-measuring.
- **#170 (kronikol-ef):** L189-254 only (one parent context for the spans, and the forwarded traceparent from the
  client span's ids), a new opt-in option (a
  minor), `ProxyTapServiceCollectionExtensions` and the wiki options table. #171's record uses the same
  `activityTraceId` and `activitySpanId` locals, so #170's trace flows into a failed record with no extra work.
- **Order:** changed by the owner on 2026-10-13 (relayed by kronikol-ef): #170's R0 (a patch) and R1 (the minor)
  release as soon as they pass, without waiting for #172 or #171. Then #172's patch, then this plan's R1 and R2. R0 is
  independent and should go first (Q8). Every number is claimed with "taking X now" at push time. kronikol-ef keeps
  R0 out of its text, and its R1 wiki section carries no `ListenHost` or device recipe until R0 is published.
- **#170's edits** stay within `HandleAsync` L189-254 and add facts to `ProxyTapTests.cs`, with nothing in the
  forward's try (L256-304) or in `Record`, and they keep the `activityTraceId` and `activitySpanId` locals. R1 and
  R2 are written against #170's `HandleAsync`, and R2's span-start guard (4.6) wraps #170's code.
- **PLANS_STATUS.md:** each session adds only its own row.

## 9. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | R1's bump: the issue is labelled *enhancement*, but nothing new is callable (`FailedSend` is public since 3.18.0) | **Patch.** It closes a capture gap; CLAUDE.md: a fix that changes observable behaviour is still a patch, with the change called out |
| Q2 | Failed response content: the 502 body, or null as the in-process handler records | **The 502 body.** It's what the client got, and the only way the cause reaches the diagram (3.2) |
| Q3 | Failed response status: `!ExceptionType` or `502` | **`!ExceptionType`.** The service never answered. The note shows the 502, and the shape matches the in-process handler's |
| Q4 | The forward-failure line's kind: keep `CaptureDegraded`, or `Other`, or a new kind | **Keep `CaptureDegraded` for now.** A new kind is public API (a minor) and changes the report JSON's `kind`. Revisit at v5, where the kind could say "environment" |
| Q5 | A forward cut by the tap's own disposal: recorded as failed and not counted | **Yes**, with the "tap stopped" wording, so the issue's host still sees the call end |
| Q6 | Timeout wording in `Error` and the 502 body, for `ForwardTimeout` and for `ConnectTimeout` | **As in 4.2**, with the type kept as `!TaskCanceledException` for both. Today a connect timeout's 502 says only `The operation was canceled.` (3.3) |
| Q7 | A throwing `IdentityResolver`: fall back to the built-in rules, treat the call as unattributed, or keep today's 502 | **Fall back to the built-in rules.** "Null" already means that, and the call is forwarded either way |
| Q8 | R0 is a security fix in two published packages (`Kronikol.Extensions.ProxyTap` and `Kronikol.Extensions.Otlp`). Ship first, and how to disclose? | **Ship R0 first**, ahead of the agreed order, with a GitHub security advisory if you want one. Binding to `localhost` does not close it (a browser page can send a `//` path, 3.5), which argues for the advisory. Keep this file uncommitted until R0 is published, or commit it without section 3.5, 4.7, R0's facts and this row |
| Q9 | R1 and R2 in one release or two | **One.** Same catches, same counters |
| Q10 | Draw `Error` in the note of every failed send (handler, Cosmos, Kafka, tap) | **Separate issue.** It's a renderer change (golden pins, the Kronikol4J ledger), not needed for #171 once the 502 body is the content |
| Q11 | A `ROADMAP.md` row | None taken. Your call |

## 10. Log

- 2026-10-13: written at 4.18.1. Probes run on Windows (`probe-output-4.18.1.txt`,
  `probe-network-path-output-4.18.1.txt`). Split and order agreed with kronikol-a5 (#172) and kronikol-ef (#170).
  Both peers keep R0's details out of anything public, and their plans say only that a peer release may go first.
  kronikol-a5's listener measurements (managed `Abort()` writes the head, Windows `Abort()` after `Close()` throws)
  are in 4.1, and fact 15a was added for a failed forward whose caller hung up.
- 2026-10-13: kronikol-a5 pointed out that `OtlpTap.ForwardAsync` builds its forward the same way. Measured
  (`probe-otlp-network-path-output-4.18.1.txt`): its forward mode sends both `//host` and absolute-form targets to the
  named host. Folded into R0 (3.5, 4.7, fact 3a, Q8).
- 2026-10-13: audited at the owner's request for accuracy and completeness, with `probe-audit.cs` run on Windows and
  on Linux. Fact 3 expected an absolute-form target naming another host to reach the configured upstream, but the
  listener answers it itself (400 on Windows, 404 on Linux), so the fact now pins that. R0 grows: ProxyTap's listener
  turns `/\host` into `//host`, which escapes too, and a `///` target is answered 502; OtlpTap gives a `///` target no
  answer and forwards asterisk, authority and slashless targets, and its rule must test for a leading `/` first,
  because on Linux `/v1/traces` parses as an absolute file URI. A `ConnectTimeout` was measured and gets its own kind
  and wording (3.3, 4.2). `Classify` needs an `OperationCanceledException` before it blames a token, and R1 logs its
  failures. A throwing `ActivityListener` turns a good answer into a 502 (3.4), so R2 guards the spans (4.6, fact 19).
  The diagnostics count each outcome directly, say "recorded" rather than "drawn", and keep today's text up to the
  semicolon. The generic catch keeps #172's 502 attempt and counts by `answer.Closed`. The client span's `error.type`
  uses .NET's own words. Checked and unchanged: the in-flight registry and the header decoder can't throw,
  `AddException` stays out (no `System.Diagnostics.DiagnosticSource` package reference), and nothing outside the taps
  reads `ForwardFailures`. The same day the owner changed the order: #170's R0 and R1 release first (section 8).
- 2026-10-13: kronikol-a5's check of #172 (`e5fd126d`) folded in. Its design corrections are in 4.1: the release's
  `EndUnclosed()` swallows every exception, the `StatusCode` setter is harmless once the head has gone, and R1
  removes the typed catch at L157, whose remaining throws are `Stopping` or capture faults. Its measurements: a body
  cut short by an RST is an `HttpRequestException` whose `HttpRequestError` is `Unknown`, so `error.type` falls back
  to the type name (3.1, 4.4, facts 6 and 8); `TcpClient.Dispose()` sends a FIN before its RST, so every reset in
  section 6 is an RST alone (facts 6 and 15a); an RST alone mid-body counts 10 of 10 on Linux (section 1); and on
  Linux a chunked request cut short by a FIN reaches no catch (4.1). The `Unreachable` kind is renamed `Failed`,
  since a body cut short was reachable.
- 2026-10-13: the owner green-lit the plan in full, every question in section 9 as recommended, and the same day
  green-lit #172, so the order is R0, then #172's R1, then this plan's R1 and R2 on its `CallerAnswer`, with both
  chores in 4.1 (agreed with kronikol-a5). R0 written as 4.19.1 in its own worktree. Found while writing it: http.sys
  hands an absolute-form target naming the tap's host name at another port to the tap, which forwards its path (3.5
  and fact 3 corrected); no target builds a URI that throws, so no 400 for one (4.7); the Otlp theory needed an
  ordinary `/v1/traces` target before the reordering mutation died on Linux (fact 3a); and `ProxyTapTests`' span fact
  took every tap span in the process, so it failed on Linux 5 of 5 once the new class sent traffic beside it, and now
  counts only its own trace. Proofs and mutations: harness `r0/`.
- 2026-10-13: R0 published as 4.19.1 (`4de3ee16`). Release run 38424791397, CI 38424790813 and CodeQL 38424790811
  passed, and nuget.org listed all 62 ids. The wiki has R0's edits (`2a193d8`: the ProxyTap and OTLP options rows and
  the OTLP page's forwarding step), Kronikol4J's ledger its line (`8cc145c`, nothing to port), and #171 a comment
  naming the release; the issue stays open for R1. This file and its harness were committed once 4.19.1 was on
  nuget.org (Q8).
- 2026-10-13: R1 and R2 released as one patch, 4.19.5, on #172's `CallerAnswer` (4.19.2) and after #170's audit
  (4.19.4). Changed while writing them: a failed forward is recorded, counted and logged before its 502 goes out, not
  after (4.2's step order), because 4.19.2 counted it before answering and its facts, and hosts, read `ForwardFailures`
  as soon as the caller has its 502; fact 15 reads the diagram through `PlantUmlCreator` rather than a whole report;
  the generic catch is driven by two internal seams (`BeforeForward`, `AfterAnswered`), as OtlpTap's are; the span
  guards are internal so a fact can drive them. Measured (`r1r2/listener.cs`): a listener that throws at the start
  leaves the half-started span current, and one that throws at the stop leaves the stopped span current, so both guards
  put `Activity.Current` back. Two mutations survived the plan's facts and got facts of their own: the record's catch
  (a throwing sink on a failed forward would have become the 502's message) and the restore after a start that throws
  at both ends. Proofs: 26 of 26 facts fail on 4.19.3 with the new surface stubbed; 19 of 19 mutations killed
  (`r1r2/`).
- 2026-10-13: R1 and R2 published as 4.19.5 (`ba85a495`). Release run 38437247650, CodeQL 38437242092 and CI Summary
  Preview 38437242110 passed. CI 38437242072 failed once, in Template Scaffolds Build & Pack, because the templates pin
  4.19.3 and nuget.org had indexed 4.19.3 for only 39 of the 62 ids when CI restored them. The release was checked
  against the core id alone, and a pin has to be checked against all 62. The job passed when re-run after the other
  23 were listed. nuget.org listed all 62 ids at 4.19.5 at 19:06Z, about 100 minutes after the push, while it worked
  through four releases pushed within the hour. The wiki has the section "When the upstream fails" (`db93b0c`),
  Kronikol4J's ledger its line (`518b8a7`, nothing to port), and #171 is closed with a comment naming the release.
  On main after 4.19.5, Linux CI failed one of the new facts once: `StubUpstream.FreePort()` hands out a port it has
  released, and the three new parallel classes made a second test getting the same number likelier. kronikol-ef's
  test-only `ced2e03c` gives every listener a port no other test in the process got. The plan is complete.
