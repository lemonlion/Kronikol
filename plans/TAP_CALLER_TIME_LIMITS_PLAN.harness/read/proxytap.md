# ProxyTap, read for #173 (caller-side time limits)

Read at `3240c983` (Kronikol 4.20.2). The package last changed in `ba85a495` (4.19.5, #171), after `3607253a` (4.19.2,
#172). Line numbers are at `3240c983`. Tags: **READ** is the code at that commit; **INFERRED** is reasoned from it, not
run; **UNVERIFIED** is recalled from .NET's source or docs, for `source/` or `probe/` to confirm.

Files read:

| File | Lines |
|---|---|
| `src/Kronikol.Extensions.ProxyTap/ProxyTap.cs` | 1,006 |
| `src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs` | 233 |
| `src/Kronikol.Extensions.ProxyTap/ProxyTapServiceCollectionExtensions.cs` | 91 |
| `src/Kronikol.Extensions.ProxyTap/ForwardFailure.cs` | 82 |
| `src/Kronikol.Extensions.ProxyTap/InFlightIdentityRegistry.cs` | 106 |
| `tests/Kronikol.Tests.ProxyTap/*.cs` (8 files) | 3,181 |

The package targets `net8.0;net9.0;net10.0` (`Directory.Build.props` L6) and has
`InternalsVisibleTo Kronikol.Tests.ProxyTap` (its csproj). The test project builds `net10.0` only.

## 1. Accept loop, handler, in-flight count, disposal (READ)

- Fields (ProxyTap.cs): `_listener = new()` (L48), `_upstream` (L49), `_cts` (L50), `_loop` (L51), `_inflight` (L52),
  `_disposed` (L53), counters L54-62.
- `StartAsync` (L172-214): `_options.Validate()` (L174); builds `_upstream` (L176-193);
  `_listener.Prefixes.Add(ListenUri.ToString())` (L195) and `_listener.Start()` (L196). **Nothing else is set on the
  listener**: no `TimeoutManager`, no `IgnoreWriteExceptions`, no authentication. `_cts` is linked to the caller's token
  (L198). The loop is started with `Activity.Current` cleared (L202-211):
  `_loop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None)` (L206).
- `AcceptLoopAsync` (L216-232):
  ```csharp
  context = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);   // L223
  ...
  _ = Task.Run(() => GuardedHandleAsync(context), CancellationToken.None);          // L230
  ```
  **Fire-and-forget**: the task is discarded. **No concurrency cap** of any kind: no semaphore, and nothing reads
  `_inflight` to refuse work. The loop returns on `OperationCanceledException`, `HttpListenerException` or
  `ObjectDisposedException` (L225-228). The listener dispatches a context only once the request head has been parsed, so
  the tap's clock can start no earlier than L230/L236 (the issue's second constraint: a caller stalled inside its head
  never reaches the tap).
- `GuardedHandleAsync` (L234-265):
  ```csharp
  Interlocked.Increment(ref _inflight);              // L236
  var answer = new CallerAnswer(context.Response);   // L237
  try { await HandleAsync(context, answer).ConfigureAwait(false); }   // L240
  catch (Exception ex)                               // L242-257
  {
      _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap error: {ex.Message}");   // L247
      if (answer.Closed) CaptureFault("the tap", ex);                                   // L248-251
      else { Interlocked.Increment(ref _forwardFailures); TryRespondBadGateway(answer, BadGatewayBody(ex.Message)); } // L252-256
  }
  finally { answer.EndUnclosed(); Interlocked.Decrement(ref _inflight); }               // L258-264
  ```
- `InFlight` (internal, L94-95): `Volatile.Read(ref _inflight)`, commented as being for Kronikol.Tests.ProxyTap ("a fact
  hangs up only once the tap holds the request").
- `DisposeAsync` (L963-1005), in order:
  1. once only: `Interlocked.Exchange(ref _disposed, 1)` (L965-966);
  2. `await _cts.CancelAsync()` (L968-969): unblocks the accept loop and, through the linked `deadline` (L398), cuts any
     forward in flight (classified `Stopping`, not counted);
  3. `_listener.Close()`, never `Stop()` first (L971-985; the port-reclaim reason, #74, is in the comment);
  4. `await _loop` (L987-997);
  5. the drain, which waits and aborts nothing:
     ```csharp
     for (var waited = 0; Volatile.Read(ref _inflight) > 0 && waited < 2_000; waited += 25)   // L999
         await Task.Delay(25).ConfigureAwait(false);                                         // L1000
     ```
  6. `_upstream?.Dispose()` (L1002), `_cts?.Dispose()` (L1003), and the log line
     `proxy-tap stopped ({RequestsHandled} forwarded, {RequestsCaptured} captured)` (L1004).

  Its doc comment (L958-962): "Stops listening, drains in-flight exchanges briefly (≤ 2 s) so their captures land, and
  releases the upstream client." A handler still running after the drain keeps running with no owner.
- **What releases a stalled caller today:** only step 3. Closing the managed listener closes its connections, the stalled
  read or write then fails, and the handler leaves through its catches (hang-up plan §2.2: "Every held connection on
  Linux was released by `DisposeAsync`").
- **INFERRED, a disposal edge new code must not copy:** `_cts?.Token` (L397) throws `ObjectDisposedException` once L1003
  has disposed `_cts` (the `Token` getter checks disposal). A handler that reaches L397 after the 2 s drain, say behind a
  slow `IdentityResolver`, lands in the catch-all at L242 and is counted as a forward failure with a 502 attempt. A new
  timer that links to the lifetime from inside a handler has the same hazard unless it takes the token once, early.

## 2. The body read (READ)

ProxyTap.cs L279-299:

```csharp
byte[] requestBody = [];
if (request.HasEntityBody)
{
    try
    {
        using var buffer = new MemoryStream();
        var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
        await body.CopyToAsync(buffer).ConfigureAwait(false);
        requestBody = buffer.ToArray();
    }
    catch (Exception)
    {
        // (L291-295: the exception says which only by platform and by how the caller closed, so the step decides)
        answer.StatusIfUnclosed = 400;
        return;
    }
}
```

- Read whole, `Stream.CopyToAsync(Stream)` into a `MemoryStream` with the default copy buffer, then `ToArray()` (a
  second copy). **No size cap**: `ProxyTapOptions` has no `MaxRequestBodyBytes` or the like (the OTLP tap's
  `MaxRequestBytes`, 32 MiB, is its own). `BodyCapBytes` (ProxyTapOptions.cs L54) caps only the captured copy
  (`CapturedBody`, L732-744), never the read or the forwarded bytes. A slow-drip caller can grow the buffer without bound.
- **No token at all** on the read (none to `CopyToAsync`, none to the seam). Passing one would not help on the managed
  listener: a token does not interrupt body I/O already running (issue #173; hang-up plan §2.3, last bullet).
- **When it runs:** after the head is read (`ReadHeaders` L275, traceparent L276, `requestStarted` L277), before identity
  (L302), the in-flight registration (L312-314), the spans (L329-360) and the `deadline` (L397-399). A stalled body
  holds the handler, its `CallerAnswer`, the context and the listener's connection, and nothing else of the tap's.
- **The seam** `WrapRequestBody` (L97-99), `internal Func<Stream, Stream>? WrapRequestBody { get; set; }`, null in
  production; it wraps `request.InputStream` at L285. `CopyToAsync` reads in pieces, so a production wrapper at the same
  point could re-arm an idle timer per read. If both exist, the order matters to the facts that use the seam
  (`FailingReadStream`, section 9).
- **The catch is untyped on purpose** (L289-298). It sets `StatusIfUnclosed = 400` and returns: no forward, no
  `ForwardFailures`, no log line, no record, **no counter of any kind**. A read failed by a timer's `Abort()` lands here
  too (INFERRED), so the read side needs no new catch. But the status a timer wants (408, say) has to be set **before**
  its `Abort()`: the managed `Abort()` writes the unsent head with the status set at that moment (hang-up plan §2.3), and
  this catch's 400 comes only after the read has failed, when the head has gone. `EndUnclosed()` uses `StatusIfUnclosed`,
  whose default is 502, so a timer that calls it without setting a status first answers a stalled upload 502.
- Whether the forward carries content: `if (requestBody.Length > 0 || request.HasEntityBody)` (L402-403).
- Not a timer matter, for completeness: a chunked body cut short by a FIN reads as whole on the managed listener and is
  forwarded (hang-up plan §3.10; dotnet/runtime#135680).

## 3. The forward (READ)

- `ForwardTimeout` (ProxyTapOptions.cs L138-139), default `TimeSpan.FromSeconds(200)`. Doc: "Upper bound for one
  forwarded exchange. Generous by default (200 s) — a tap must never be the tightest deadline on the path."
- `ConnectTimeout` (L141-142), default 5 s, applied as `SocketsHttpHandler.ConnectTimeout` (ProxyTap.cs L182).
- `_upstream` (L176-193): `SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression =
  None, ConnectTimeout, UseCookies = false, ActivityHeadersPropagator = null }`, `BaseAddress = ForwardBaseUri`,
  `Timeout = _options.ForwardTimeout + TimeSpan.FromSeconds(10)` (L192). `MaxResponseContentBufferSize` is not set, so it
  is HttpClient's default, `int.MaxValue`.
- The deadline (L396-399):
  ```csharp
  var lifetime = _cts?.Token ?? CancellationToken.None;
  using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
  deadline.CancelAfter(_options.ForwardTimeout);
  ```
  Made after the body read, so the read's time is not in it. It is a `using var`, alive until `HandleAsync` returns
  (through the answer write), but its token goes only to the two forward calls.
- Send and read (L424-434):
  ```csharp
  using var response = await _upstream!.SendAsync(message, HttpCompletionOption.ResponseContentRead, deadline.Token)...; // L426
  responseBody = await response.Content.ReadAsByteArrayAsync(deadline.Token)...;                                      // L427
  ```
  `ResponseContentRead` buffers the whole upstream body inside `SendAsync`; `ReadAsByteArrayAsync` copies it into
  `byte[] responseBody`. **No size cap** beyond HttpClient's default buffer limit. The `HttpResponseMessage` is disposed
  at the end of the try, `responseBody` lives on to the record.
- The forward's catch (L435-474): `ForwardFailure.Classify(ex, deadline.Token, lifetime)` (L438), `Reason` (L439), the
  502 body (L441), spans ended with an error (L443-448), the record (L452-455), counters and log (L457-469; `Stopping` is
  logged `proxy-tap stopping:` and not counted, everything else `proxy-tap error:` and counted), then
  `TryRespondBadGateway(answer, badGateway)` (L472) and `return`.
- `ForwardFailure.cs`: `ForwardFailureKind { Failed, ConnectTimedOut, TimedOut, Stopping }` (L6-19). `Classify`
  (L31-44): lifetime cancelled and an OCE or ODE is `Stopping`; anything not an OCE is `Failed`; deadline cancelled is
  `TimedOut`; an inner `TimeoutException` is `ConnectTimedOut`; else `Failed`. `Reason` (L50-60) words `TimedOut` as
  `no answer from {authority} within ForwardTimeout ({options.ForwardTimeout}). Caused by: {chain}` (L56).
- **INFERRED defect, found reading (not run):** `ForwardTimeout = Timeout.InfiniteTimeSpan` (-1 ms) makes
  `HttpClient.Timeout` 9.999 s (L192), which then fires first. HttpClient throws a `TaskCanceledException` with an inner
  `TimeoutException`, neither tap token is cancelled, and `Classify` (ForwardFailure.cs L40-42) files it as
  `ConnectTimedOut`: the caller's 502 says "could not connect ... within ConnectTimeout (00:00:05)" about a forward that
  connected and ran 10 s. `plans/PROXY_TAP_FAILED_FORWARD_PLAN.md` L138 says `HttpClient.Timeout` "can't fire first",
  which holds only for a non-negative `ForwardTimeout`. Also: `ForwardTimeout = TimeSpan.Zero` cancels every forward at
  once; a negative value other than -1 ms throws from `CancelAfter` at L399, outside the forward's try (L424), so the
  catch-all counts every request as a forward failure; at -10 s or below the `HttpClient.Timeout` setter throws in
  `StartAsync`. `Validate` checks neither duration (section 5). It bears on #173 because the new option's rule will be
  the package's first on a duration, and the plan can settle `ForwardTimeout`'s (and whether "infinite" is allowed) in
  the same release. A one-line probe confirms it.

## 4. The answer write (READ)

- `RespondAsync` (L825-859), `private static`, takes the `CallerAnswer`:
  ```csharp
  response.StatusCode = status;                                            // L831
  // L832-844: upstream headers, skipping HopByHop, Content-Type and Content-Encoding; a restricted one is skipped on ArgumentException
  if (contentType is not null) response.ContentType = contentType;        // L846-847
  if (contentEncoding is not null) response.Headers["Content-Encoding"] = contentEncoding;   // L848-849
  response.ContentLength64 = body.Length;                                  // L851
  await response.OutputStream.WriteAsync(body).ConfigureAwait(false);      // L852
  answer.Close();                                                          // L853
  ...
  catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)  // L855
  ```
  **Content-Length, never chunked** (`SendChunked` is never set). **The whole body in one `WriteAsync`**, with no token
  and no piece size, so nothing can observe progress inside the write. A total deadline on the write needs nothing more;
  a progress-based limit (idle, or a minimum rate as http.sys has) needs the write split into pieces. The catch swallows
  ("The client hung up; the exchange is still recorded, and GuardedHandleAsync aborts the response", L857).
- **The catch is typed.** Anything else the write throws leaves `RespondAsync`, reaches the catch-all (L242) with
  `answer.Closed` false, and is counted as a forward failure with a `proxy-tap error` line and a 502 attempt. A write cut
  by a timer's `Abort()` therefore has to throw one of the three types on both platforms. Hang-up plan §2.3 says the
  managed response stream turns a socket `IOException` into `HttpListenerException` and a disposed stream throws
  `ObjectDisposedException`; worth one probe line per platform for a write aborted mid-flight from another thread.
- After a write that failed silently `HandleAsync` goes on: `_handled++` (L490), `AfterAnswered?.Invoke()` (L491), then
  the record with the upstream's status (L493-503). Today a caller that resets mid-answer is handled and captured
  (`ProxyTapHangUpTests` L46-50 pin `RequestsHandled` 1, `RequestsCaptured` 1, 2 records, `ForwardFailures` 0). A write
  cut by a time limit is counted the same way unless the plan says otherwise.
- `TryRespondBadGateway` (L887-902), `private static`: `StatusCode = 502`, `ContentType = "application/json"`,
  `ContentLength64 = payload.Length`, then a **synchronous** `response.OutputStream.Write(payload)` (L895) and
  `answer.Close()` (L896); the same three-type catch (L898-901). Called from the forward's catch (L472) and the
  catch-all (L255). The payload is the small JSON from `BadGatewayBody` (L865-879), so it normally fits the socket
  buffers, but a stall there would block a pool thread rather than await.
- `CallerAnswer` (L904-956), complete but for its doc comments (L904-909, L914, L917, L926-929) and the comments inside
  `EndUnclosed` (L935-937, L944, L953):
  ```csharp
  internal sealed class CallerAnswer(HttpListenerResponse response)          // L910
  {
      public HttpListenerResponse Response { get; } = response;              // L912
      public int StatusIfUnclosed { get; set; } = 502;                       // L915
      public bool Closed { get; private set; }                               // L918

      public void Close()                                                    // L920-924
      {
          Response.Close();
          Closed = true;
      }

      public void EndUnclosed()                                              // L930-955
      {
          if (Closed)
              return;
          try { Response.StatusCode = StatusIfUnclosed; } catch (Exception) { }   // L938-945
          try { Response.Abort(); } catch (Exception) { }                         // L947-954
      }
  }
  ```
  Doc of `StatusIfUnclosed` (L914): "The status a response not yet started is ended with: 502, or 400 when the request
  never arrived whole." Comment at L935-937: "The managed listener's Abort() first writes a head not yet sent, with the
  status set (200 unless told otherwise), so a failure status makes that head true; once a head is out, the late status
  is never sent and Abort() only closes. http.sys sends nothing more."
- **Not thread-safe.** `Closed` is a plain auto-property set on the handler's thread after `Response.Close()` returns, and
  nothing guards `EndUnclosed` against a second thread. A timer that calls it while the handler is in `Close()` or in the
  `finally` is unmeasured; the "twice" case of `Ending_an_unclosed_response_never_throws` is sequential
  (ProxyTapHangUpTests L210-212). That fact also builds a `CallerAnswer` directly, `new Tap.CallerAnswer(context.Response)`
  (L200), so a changed constructor touches it.
- **The `finally`** (L258-264) runs `EndUnclosed()` and then the decrement. A timer callback that aborts needs the same
  never-throws rule: an exception on a timer thread is unobserved at best.
- A timer armed before the head is sent can race it: if it fired between `StatusCode = status` (L831) and the write
  (L852), the managed `Abort()` would send the upstream's status with an empty body (INFERRED). Arming a write limit
  immediately around L852 avoids that.

## 5. Options, `Validate`, the DI copy (READ)

`ProxyTapOptions` (ProxyTapOptions.cs L27-200), 32 public properties:

| Line | Option | Type | Default |
|---|---|---|---|
| 30 | `ListenPort` | `int` | 0 (required) |
| 33 | `ListenHost` | `string` | `"localhost"` |
| 39 | `ForwardBaseUri` | `Uri?` | null (required) |
| 42 | `CallerName` | `string` | `""` (required) |
| 45 | `ServiceName` | `string` | `""` (required) |
| 48 | `DependencyCategory` | `string?` | null |
| 51 | `CallerDependencyCategory` | `string?` | null |
| 54 | `BodyCapBytes` | `int?` | 65536 |
| 57 | `CaptureBodies` | `bool` | true |
| 60 | `HeaderPolicy` | `HeaderCapturePolicy` | `AllExceptSecrets` |
| 63 | `HeaderWhitelist` | `HashSet<string>`, get-only | empty |
| 70 | `SecretDenylist` | `HashSet<string>`, get-only | `CaptureRedaction.DefaultSecretHeaders` |
| 73 | `DropSecretHeaders` | `bool` | false |
| 76 | `RedactedValue` | `string` | `"[REDACTED]"` |
| 83 | `ReinjectCorrelation` | `bool` | true |
| 91 | `IdentityFromTraceparent` | `bool` | true |
| 94 | `TestNameHeaderFallbacks` | `List<string>`, get-only | empty |
| 97 | `TestIdHeaderFallbacks` | `List<string>`, get-only | empty |
| 104 | `IdentityResolver` | `Func<…>?` | null |
| 107 | `FallbackTestName` | `string` | `TestIdentityScope.UnknownTestName` |
| 113 | `CaptureUnattributedRequests` | `bool` | false |
| 120 | `FallbackTestId` | `string?` | null |
| 130 | `InFlightRegistry` | `InFlightIdentityRegistry?` | null |
| 133 | `Sink` | `IRequestResponseSink` | `RequestResponseLoggerSink.Instance` |
| 136 | `Phase` | `TestPhase` | `Unknown` |
| 139 | `ForwardTimeout` | `TimeSpan` | 200 s |
| 142 | `ConnectTimeout` | `TimeSpan` | 5 s |
| 154 | `EmitActivities` | `bool` | true |
| 160 | `SynthesizeTraceparent` | `bool` | true |
| 176 | `TraceIdFromIdentity` | `bool` | false |
| 179 | `Log` | `Action<string>?` | null |
| 182 | `Name` | `string?` | null |

`DisplayName` (L184) is internal: `Name ?? $"{CallerName}→{ServiceName}"`.

- **`Validate`** (L186-199), `internal void`, five checks only: `ListenPort` 1-65535 (L188-189,
  `ArgumentOutOfRangeException`), `ForwardBaseUri` absolute http or https (L191-192), `CallerName` (L193-194),
  `ServiceName` (L195-196), `ListenHost` (L197-198), the last four `ArgumentException`. **No duration is validated.** It
  runs in `ProxyTap.StartAsync` (ProxyTap.cs L174) and at registration in `AddProxyTapTestTracking`
  (ProxyTapServiceCollectionExtensions.cs L21), so a bad option fails at registration as well as at start. Facts:
  `ProxyTapTests.Options_are_validated` (ProxyTapTests.cs L528-534) and
  `ProxyTapForwardTargetTests.A_forward_base_that_is_not_http_is_rejected_at_start` (L128).
- **Precedent in a sibling tap:** `TcpTapOptions.ReapStuckConnectionsAfter` (TcpTapOptions.cs L173), a `TimeSpan?` with
  null meaning off, validated at L210-211: "ReapStuckConnectionsAfter must be positive, or null to disable the reaper."
  `TcpTapOptions.DrainTimeout` (L108) is a 2 s `TimeSpan`.
- **`Copy`** (ProxyTapServiceCollectionExtensions.cs L42-77, used by the list overload `AddProxyTapTestTracking(IEnumerable
  <ProxyTapOptions>)` at L30-40): one line per option for all 32, in declaration order (`SecretDenylist` takes two, a
  `Clear()` at L55 and a `UnionWith` at L56; `ForwardTimeout` L70, `ConnectTimeout` L71). A new option needs a line here.
- **The reflection fact:** `ProxyTapRegistrationTests.Registering_taps_from_a_list_copies_every_option`
  (ProxyTapRegistrationTests.cs L34-74). It sets every public instance property of a source to a non-default value
  (L39-55; get-only `HashSet<string>` and `List<string>` get an item), registers it through the list overload, and
  compares every property of the registered tap's `Options` (L63-73). `NonDefault` (L16-32) knows `int` (`ListenPort`
  gets 54321, L18), `int`/`int?` (+17, L19), `string`, `Uri`, `bool`, enums, `TimeSpan` (`fresh + 1 s`, L24),
  `IRequestResponseSink`, `InFlightIdentityRegistry`, `Action<string>` and the resolver's `Func`; any other type throws
  "No test value for ProxyTapOptions.{property.Name} ({t}); add one so the copy of the new option is checked." (L30-31).
  A plain `TimeSpan` option is covered as the fact stands. **A `TimeSpan?` option (null = off) is not**:
  `typeof(TimeSpan?) != typeof(TimeSpan)`, so it needs an arm, e.g. `t == typeof(TimeSpan?) => TimeSpan.FromSeconds(7)`;
  L47 asserts the value differs from the default.

## 6. `Diagnostics()`, counters and log lines (READ)

Public counters and their doc comments:

- `RequestsHandled` (L76-77): "Exchanges answered from the upstream so far. A forward that failed is not one."
- `RequestsCaptured` (L79-80): "Of RequestsHandled, the exchanges captured (attributed to a test and recorded). A failed
  forward that was recorded is not added here."
- `ForwardFailures` (L82-89): "Requests the tap could not forward (the upstream was unreachable, timed out or the
  exchange threw) and answered `502 Bad Gateway` instead. One that names a test, or that CaptureUnattributedRequests
  takes, is recorded as a failed call. A request whose caller hangs up before it has arrived whole is not counted: it was
  never forwarded, and its caller is not there to answer. Nor is a forward the tap's own disposal cut short, nor a fault in
  capture, which Diagnostics reports on a line of its own." A body that times out is "never arrived whole" without a
  hang-up, so this sentence would need widening.
- `IsListening` (L91-92): "Whether the listener is accepting connections."
- Internal: `InFlight` (L95), `CaptureFaults` (L162). Private: `_failuresRecorded` (L57), `_failuresWithoutIdentity`
  (L58), `_uncaptured` (L59), `_firstCaptureFault` (L61), `_traceIdNotApplied` (L62).

`Diagnostics()` (L108-137; doc L101-107: "one CaptureDegraded entry per non-zero problem counter, worded for a report
reader … Empty when the tap is healthy"). Its four entries, each only when its counter is non-zero:

| Lines | Kind | Text |
|---|---|---|
| L113-116 | `CaptureDegraded` | `{name}: {forwardFailures:N0} request(s) could not be forwarded and were answered 502 Bad Gateway — the upstream was unreachable, timed out or the exchange threw; {WhichWereRecorded(forwardFailures)}` |
| L120-124 | `CaptureDegraded` | `{name}: {uncaptured:N0} of {handled:N0} forwarded request(s) carried no test identity and were not captured (no test-tracking headers or traceparent reached this hop; CaptureUnattributedRequests is off)` |
| L126-129 | `OptionNotApplied` | `{name}: TraceIdFromIdentity did not apply to {notApplied:N0} request(s), because their test id is not a W3C trace id (32 lower-case hex digits, not all zero); they were forwarded under a random trace, so the services behind this tap cannot be matched to the test` |
| L131-134 | `CaptureDegraded` | `{name}: {captureFaults:N0} capture fault(s): the IdentityResolver, the sink, the body decoder or an ActivityListener threw, so a call may be missing, unattributed or untraced (the first: {first}); what the callers got was not changed` |

`WhichWereRecorded` (L140-158) ends the first line with "each is recorded as a failed call", "they carried no test
identity and were not recorded", or a list of "{n} is/are recorded as a failed call/failed calls", "{n} carried no test
identity and was/were not recorded" and "{n} was/were not recorded".

Every `_options.Log` line: capture fault `[{DisplayName}] proxy-tap: {what} threw {Type}: {Message}` (L168); listening
(L212); the catch-all's `proxy-tap error: {ex.Message}` (L247); `proxy-tap stopping: {error}` (L459);
`proxy-tap error: {error}` (L468); the first `TraceIdFromIdentity` miss (L608); stopped (L1004).

**No counter, line or log entry exists for any caller-side failure** (hang-up, half-close, broken body). That was the
hang-up plan's Q1 ("If the owner wants a trace, the alternative is a new counter (say `CallerHangUps`) and a
`Diagnostics()` line, which is new public surface and so a minor"). A time limit that should be visible needs the same:
a public counter and an entry. `DiagnosticKind.CaptureDegraded`'s own doc (src/Kronikol/Reports/DiagnosticEntry.cs
L41-45) says "forwarding was never affected", which a caller cut off by a limit is not, so the kind is a choice.

Facts that pin the exact texts: `ProxyTapFailureTests` `ForwardFailureLine` (L210-211) and the prefix in
`Forward_failure_diagnostics_say_which_calls_are_recorded` (L449-477, prefix at L452); the whole capture-fault line
(L618); `ProxyTapTests` L617 and L640. The hang-up facts assert `Errors(log)` empty (lines containing
`proxy-tap error`, ProxyTapHangUpTests L395-399) for every caller-side failure (L74, L98, L178).

## 7. Existing time limits (READ)

Searched the package for `Timeout`, `TimeSpan`, `CancelAfter`, `Timer`, `TimeoutManager`, `Stopwatch`, `Task.Delay`,
`WaitAsync`. **None bounds the caller's side.** Every timer in the package:

| Where | What it bounds |
|---|---|
| ProxyTap.cs L182 | `ConnectTimeout` on the upstream handler: the connect to the upstream |
| ProxyTap.cs L192 | `HttpClient.Timeout = ForwardTimeout + 10 s`: the forward, including buffering the upstream body |
| ProxyTap.cs L398-399 | `deadline.CancelAfter(ForwardTimeout)`, linked to the lifetime: the two forward calls only |
| ProxyTap.cs L223 | `WaitAsync(ct)` on `GetContextAsync`: the accept loop's lifetime |
| ProxyTap.cs L999-1000 | the 2 s drain in `DisposeAsync`: waits, never aborts |
| ProxyTap.cs L418, L477 | a `Stopwatch` started and stopped and never read (dead code) |

`HttpListener.TimeoutManager` is used nowhere in the repository (search over the whole worktree, outside bin/obj), and
no `TimeProvider` is used anywhere in `src/`. On the managed listener a dispatched context is watched by nothing (hang-up
plan §2.3, first row); the listener's own read timer covers only the wait for a request's first bytes (plan §1 item 1:
armed in `BeginReadRequest`, M/HttpConnection.cs:195, stopped after the first read, :257, and not re-armed for a partial
head, :320).

## 8. Keep-alive (READ)

- The tap never sets `response.KeepAlive`, a `Connection` or a `Keep-Alive` header. `HopByHop` (L41-45: `Host`,
  `Content-Length`, `Transfer-Encoding`, `Connection`, `Keep-Alive`, `Proxy-Connection`, `Expect`, `Upgrade`, `TE`,
  `Trailer`) is dropped from the upstream's answer (`RespondAsync` L834) and from the forwarded request
  (`CopyRequestHeaders` L812), so the caller's persistence is the listener's decision from the caller's own request.
- **Yes, a caller's next request on one connection reaches the tap again**, as a new context from `GetContextAsync` and
  a new `GuardedHandleAsync` (and a new `CallerAnswer`). Pinned by
  `ProxyTapHangUpTests.Keep_alive_requests_are_answered_on_one_connection` (L263-302): `MaxConnectionsPerServer = 1`, a
  `ConnectCallback` that counts connections, 20 requests, `connections == 1` (L299), `RequestsHandled == 20` (L300); the
  hang-up plan's control row E. Between two requests on a connection the tap holds nothing; the idle wait is the
  listener's.
- Consequence for a per-exchange timer: it must act on its own exchange's `HttpListenerResponse`, never the connection,
  and be disarmed once that response is closed, since the same connection then serves the next request. Measured on
  .NET 10, `Abort()` after `Close()` or on a closed keep-alive response does nothing and the next request is unharmed
  (hang-up plan §2.3), but R1 chose the `Closed` flag over relying on that ("the BCL's behaviour on one version").
- UNVERIFIED (memory of HttpListenerResponse.Managed.cs): the managed listener sends `Connection: close` for statuses
  400, 408, 411, 413, 414, 500 and 503, so a 408 written by a timer's `Abort()` would close the connection on its own;
  hang-up plan §2.3 measured `400` then `Abort()` as "400 Bad Request with Connection: close, empty, then FIN".

## 9. The test project (READ)

`tests/Kronikol.Tests.ProxyTap`, xunit.v3 3.2.2 (csproj L16), `test.runsettings` sets `KRONIKOL_HISTORY=off` and
`KRONIKOL_KEEP_RUNS=off`. No assembly-level collection behaviour, so each class is its own collection and classes run in
parallel. CI runs it on `ubuntu-latest` only, in the "Extension Tests" entry (`.github/workflows/ci.yml` L81-86); the
Windows and macOS lanes run template facts only (L139-146). This Windows machine cannot run the managed facts red; the
hang-up harness README describes the podman container that does.

Test classes:

| File | Class | Notes |
|---|---|---|
| ProxyTapTests.cs | `ProxyTapTests` (L90) | also defines `StubUpstream` (L17-81), `ListSink` (L84-88), `ClosedPort` (L781-790) |
| ProxyTapFailureTests.cs | `ProxyTapFailureTests` (L145) | also `SilentUpstream` (L16-59), `CutShortUpstream` (L65-133), `ThrowingSink` (L136-139); `StartAsync` with an options hook (L147-157), `SendAsync` over HttpClient (L159-175), `RecordsAsync`/`EventuallyAsync` 10 s (L178-198) |
| ProxyTapHangUpTests.cs | `ProxyTapHangUpTests` (L18) | #172; no `[Collection]` |
| ProxyTapFailureSpanTests.cs | `ProxyTapFailureSpanTests` (L13) | in the trace-context collection (L12) |
| ProxyTapTraceContextTests.cs | `ProxyTapTraceContextTests` (L24) | `[CollectionDefinition(nameof(ProxyTapTraceContextTests), DisableParallelization = true)]` (L19-20), the only collection definition |
| ProxyTapForwardTargetTests.cs | `ProxyTapForwardTargetTests` (L15) | raw `TcpClient` caller `RawAsync` (L35-46) |
| ProxyTapRegistrationTests.cs | `ProxyTapRegistrationTests` (L13) | the copy fact (section 5) |
| InFlightIdentityRegistryTests.cs | `InFlightIdentityRegistryTests` (L9) | |

`StubUpstream` (ProxyTapTests.cs L17-81) is an `HttpListener` on `LoopbackPort.Free()` (`FreePort`, L72) answering a
canned reply; it records what arrived in `Seen` (L21) and aborts a response whatever its exchange threw (L62-68).

**`ProxyTapHangUpTests`** (8 methods, 16 cases):

| Line | Fact | Platforms |
|---|---|---|
| 30 | `A_caller_that_resets_while_the_answer_is_written_leaves_no_connection_held`: 8 MB answer, `receiveBuffer: 16 * 1024` (L40), reads one byte, then an RST | managed only |
| 56 | `A_caller_that_resets_mid_body_leaves_no_connection_held_and_is_no_forward_failure` (RST alone; FIN then RST) | managed only |
| 81 | `A_caller_that_half_closes_mid_body_is_answered_400_and_let_go` | all; tables checked off Windows (L100-101) |
| 105 | `An_upstream_that_fails_after_its_caller_hung_up_is_a_forward_failure_and_leaves_no_connection_held` | managed only |
| 141 | `A_body_read_that_throws_is_the_callers_failure_whatever_the_exception` (4 types through `WrapRequestBody`) | all |
| 188 | `Ending_an_unclosed_response_never_throws` (5 cases, a bare `HttpListener`) | all |
| 241 | `An_upstream_body_cut_short_stays_a_forward_failure` | all |
| 264 | `Keep_alive_requests_are_answered_on_one_connection` | all |

- Linux and macOS only: `Assert.SkipWhen(OperatingSystem.IsWindows(), HttpSysDropsIt)` at L32, L58, L107; the reason is
  the constant at L20-22. `Patience` is 5 s (L24).
- Raw-socket caller helpers: `SendAsync(Tap, string request, int? receiveBuffer)` (L324-332, a `TcpClient` that writes
  the request text as given), `ResetAlone` (L335-339, zero linger then `Socket.Dispose()`), `FinThenReset` (L342-347,
  `TcpClient.Dispose()` with a zero linger), `HearAsync` (L350-373, the first line heard and whether the caller was let
  go within `Patience`), `FirstLine` (L375-380), `WaitUntilAsync` (L382-393, polls every 20 ms up to `Patience`).
- The tap helper `StartAsync(Uri forward, ListSink sink, List<string> log, Action<Tap>? configure)` (L306-322) sets
  `CaptureUnattributedRequests = true` and `FallbackTestId = "hang-up"` and hooks the **tap** (for seams), not the
  options; a fact for a new option needs an options hook as well.
- The `InFlight` seam is read at L44, L65, L71, L89, L96, L127, L176 (`WaitUntilAsync(() => tap.InFlight == 1)` before
  hanging up; `== 0` after).
- The managed listener's tables, by reflection: `AssertNothingHeldAsync` (L401-407), `Held` (L413-417) reads ProxyTap's
  `_listener` and then the listener's `_listenerContexts` and `_connections` (L416); `Count` (L419-424) locks each
  table's `SyncRoot`; `Field` (L426-430) throws a message naming `PROXY_TAP_HANG_UP_PLAN.harness/probe/probe.cs` if a
  field is gone. These are the managed implementation's fields, which is why the check is never made on Windows.
- `FailingReadStream` (L436-476) reads the whole inner body (so the listener has nothing unread) and then throws the
  given exception; `RawUpstream` (L479-543) is a raw-socket upstream on `LoopbackPort.Listening()` (L481) with
  `ReadHeadAsync` (L523-534) and `Reset` (L536-540).
- `LoopbackPort` is linked from `tests/Kronikol.Tests/LoopbackPort.cs` (csproj L36-37): `Free()` (L16) hands out a port
  never handed out before in the process, `Listening()` (L57-65) a started `TcpListener`, `Closed()` (L71-79) a bound
  socket that never listens. Users here: `StubUpstream.FreePort` (ProxyTapTests.cs L72), `ClosedPort` (L783),
  `SilentUpstream` and `CutShortUpstream` (ProxyTapFailureTests.cs L18, L67), `RawUpstream` (ProxyTapHangUpTests.cs L481).
- Timing precedent for a short limit: the failure facts set `ForwardTimeout = TimeSpan.FromSeconds(1)` (ProxyTapFailureTests.cs
  L249, L421, L530) and wait up to 10 s.

## 10. Other things a caller-side limit must know

- **A stalled answer write holds more than the socket (INFERRED).** The in-flight registration is a `using var` from
  L312-314 to the end of `HandleAsync`, so it spans the answer write (L489) and the record (L499-503), although its
  comment (L308-311) says "for the length of the forward". While a caller does not read, `InFlightIdentityRegistry
  .MostRecentFor(ServiceName)` (InFlightIdentityRegistry.cs L67-80) can keep naming that test (it returns the newest
  registration still held) and `CountFor` (L83-86) counts it, so a database tee in the same process can file unrelated
  traffic under it. The handler also keeps `requestBody` and `responseBody` (the full upstream body: 8 MB in the test
  at L33) and the `HttpRequestMessage` (L401). The spans are already ended (L485-486).
- **Respond first, record second** (L488-503): a stalled write delays the record, `_handled` and `_captured` until the
  write ends; a limit bounds that delay.
- **`ProxyTapFailureTests.Every_resolved_request_ends_in_one_response_record`** (L406-446) pins that each
  `IdentityResolver` call ends in exactly one response record. Identity is resolved after the body (L302), and a failed
  read returns before it; a body limit handled in the same place keeps the invariant, while resolving identity earlier
  to record a timed-out request would break it.
- **What a caller hears at a limit differs by platform** (hang-up plan §2.3): on the managed listener `Abort()` with no
  head sent writes the status then set, an empty body and `Connection: close`, then a FIN; with the head already sent it
  only closes. http.sys sends nothing (the caller sees a reset), and on Windows `Abort()` after the listener closed throws
  `ObjectDisposedException` (swallowed by `EndUnclosed`).
- **Windows already has limits (UNVERIFIED defaults):** http.sys's `HttpListenerTimeoutManager` defaults are, from the
  docs as recalled, `EntityBody`, `DrainEntityBody`, `HeaderWait`, `IdleConnection` and `RequestQueue` of 2 minutes and
  `MinSendBytesPerSecond` of 150. On the managed listener, as recalled from its source, the setters of `EntityBody`,
  `HeaderWait`, `MinSendBytesPerSecond` and `RequestQueue` throw `PlatformNotSupportedException`, and `DrainEntityBody`
  and `IdleConnection` are validated and ignored. The managed connection's head timer is, as recalled, 90 s before the
  first request and 15 s between keep-alive requests. `source/` should confirm all three before the plan leans on them.
- **Where a limit can hook in (INFERRED, not a recommendation):** the body read at L284-287 (the seam's position, L285,
  suits an idle timer); the answer write at L851-853 (one call, so a total deadline, unless split); the 502 write at
  L895 (small and synchronous). The forward has its own deadline, so one exchange-wide watchdog armed at L236-237 would
  have to pause over the forward or exceed `ForwardTimeout` ("a tap must never be the tightest deadline on the path").
  Whatever fires must set its status before `Abort()`, never throw, and leave `Closed`/`EndUnclosed` consistent across two
  threads.
- **Disposal interplay:** with a limit shorter than 2 s the drain (L999-1000) would end handlers on its own clock; with
  a longer one, `_listener.Close()` (L980) still releases stalled callers as today. The `_cts` edge in section 1 applies
  to any timer code that links to the lifetime.
- **Versioning:** a new option is a minor bump even if its default preserves today's behaviour (CLAUDE.md); a default
  that cuts callers off changes behaviour and goes in the changelog as such.
- **Kronikol4J** has no proxy tap (hang-up plan §2.4: `TrackingProxy` is a `java.lang.reflect.Proxy`; its ledger says
  "Java has no ProxyTap equivalent"), so there is nothing to port, only a ledger line.
- **The issue names three taps** (`ProxyTap`, `OtlpTap`, `TcpTap`); this file covers `ProxyTap` only.
