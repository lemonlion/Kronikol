# OtlpTap: what bounds the caller's side (read for #173)

Commit `3240c983` (worktree `C:/Code/Kronikol-173`, version 4.20.2 at `Directory.Build.props` L4). Every line number
below is at that commit. Files read whole: `src/Kronikol.Extensions.Otlp/OtlpTap.cs` (617 lines),
`HttpRequestReader.cs` (291), `OtlpTapOptions.cs` (156), `OtlpTapServiceCollectionExtensions.cs` (81), and
`tests/Kronikol.Tests.Otlp/OtlpTapTests.cs`, `OtlpTapHangUpTests.cs`, `OtlpTapForwardTargetTests.cs`, plus the two tap
facts in `OtlpExporterTests.cs`.

Tags: **READ** is code read at this commit. **SOURCE** is .NET runtime source read for this note (dotnet/runtime
`release/10.0`), not run. **REFERENCE** is outside documentation or a spec, not checked here. Nothing was built or run.

## 0. The facts a design needs first

1. **No read or write on the caller's side has a time limit (READ).** Every read in the reader goes through one method,
   `HttpRequestReader.FillAsync` (L285-290), and its only token is the tap's lifetime token (passed from
   `ServeAsync`, OtlpTap.cs:288). Every answer goes through one method, `WriteResponseAsync` (OtlpTap.cs:459-475), with
   the same token. The only time limit in the tap is the forward's hard-coded `HttpClient.Timeout` of 30 s
   (OtlpTap.cs:172), which has no option.
2. **A token does interrupt this tap's I/O (SOURCE, §1.4).** The streams are `NetworkStream`s, whose `ReadAsync` and
   `WriteAsync` hand the token to the socket, and a pending socket operation is cancelled when it fires (`CancelIoEx` on
   Windows, `TryCancel` on Unix). This is the opposite of ProxyTap's listener streams (PROXY_TAP_HANG_UP_PLAN.md §2.3,
   where only `Abort()` works). A linked token per phase, the shape ProxyTap.cs:397-399 and TcpTap.cs:212-215 already
   use for their upstream deadlines, can bound a read or a write here. It surfaces as `OperationCanceledException`, which
   `ServeAsync`'s first catch (L298-301) swallows exactly like a hang-up or the tap stopping: no count, no log.
3. **The tap reads the request head itself**, byte by byte (HttpRequestReader.cs:124-150), so a head time limit is
   possible here. For ProxyTap it is not: its listener reads the head before the tap sees the request
   (PROXY_TAP_HANG_UP_PLAN.md §3 item 9).
4. **Connections are neither capped nor tracked (READ).** Each accepted client is a discarded `Task.Run` (L273).
   `DisposeAsync` (L573-614) awaits only the accept and map loops; a handler ends when the cancelled token reaches its
   pending I/O and `using (client)` (L282) closes its socket. The issue's "until the tap is disposed" holds.
5. **What a stalled exporter holds (READ, table in §3.7):** its handler task, socket and `NetworkStream`, a 16 KiB
   buffer (HttpRequestReader.cs:39), up to 64 KiB of head (L36, L146), and, once its head names a `Content-Length`
   within `MaxRequestBytes`, an array of that length allocated at L154 before a single body byte is read (32 MiB at the
   default, OtlpTapOptions.cs:68). A chunked body holds what has arrived plus the current chunk's own up-front array.
   A body over the cap is drained before its 413 goes out (L96-100), so a slow over-cap exporter holds the handler for up
   to its whole declared length (any `int`, about 2 GiB).
6. **An idle keep-alive connection and a connection that never sends a byte wait in the same place**
   (`ReadHeadAsync`, L130, then `FillAsync`, L288). The reader does not tell "waiting for the next request" from
   "inside a head": no event marks the first byte. No TCP keep-alive is set on accepted sockets (the package's only
   socket option is `NoDelay`, OtlpTap.cs:281), so a peer that vanishes without a FIN or RST (host gone, container
   network removed, NAT state dropped) is held until disposal.
7. **The shared secret is checked after the whole body is read (READ).** `IsAuthenticated` runs at L318, after
   `reader.ReadAsync` (L288) has read and buffered the body, and a body over the cap is answered 413 at L310 before the
   secret is looked at. On a non-loopback bind, where the docs ask for `ExpectedHeaders` (OtlpTap.cs:32-33,
   OtlpTapOptions.cs:41-42), a caller without the secret can make the tap allocate up to `MaxRequestBytes` per
   connection, on as many connections as it opens, and hold each by stalling.
8. **Nothing counts connections, connections closed without an answer, or anything timing out.** A new option, counter
   or `Diagnostics()` line is new public surface: a minor (CLAUDE.md). Closing connections the tap keeps today is a
   behaviour change for the changelog.
9. **`Copy` copies all 20 public options today (L42-67 of the extensions), but no fact checks it.** ProxyTap has that
   fact (`ProxyTapRegistrationTests.cs:34-74`); an OtlpTap twin needs cases its `NonDefault` table lacks (a get-only
   `Dictionary<string, string>`, `Func<string, bool>`). A new option must be added to `Copy` by hand.
10. **No fact drives a stalled or idle exporter.** The raw-socket helpers in `OtlpTapHangUpTests` are the tools such a
    fact needs. Nothing in the package or its tests uses `TimeProvider`, so a time-limit fact would run on real time with
    a short configured limit. The project runs in CI on Linux only (ci.yml L79-80, the loop at L287-303).

## 1. The accept loop and disposal

### 1.1 `StartAsync` (L157-185)

- `_options.Validate()` (L159). The forward's `HttpClient` is built only when `ForwardBaseUri` is set (L161-174).
- `Bind()` (L176, body L193-217): `localhost` takes 127.0.0.1 and ::1 as two listeners (L222-223); `+`/`any` takes one
  dual-mode IPv6 listener (L224-225). `listener.Start()` (L204) with the default backlog.
- `_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` (L178): the token handed to `StartAsync`
  becomes part of the tap's lifetime. Cancelling it later stops the loops without `DisposeAsync`.
- One accept loop per listener, `Task.WhenAll(_listeners.Select(l => Task.Run(() => AcceptLoopAsync(l, token),
  CancellationToken.None)))` (L180); the map loop (L181).

### 1.2 `AcceptLoopAsync` (L259-275)

```csharp
client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);                                    // L266
...
catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
{
    return;                                                                                                  // L268-271
}

_ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None);                                         // L273
```

- **No concurrency cap:** no semaphore, no connection count, no backlog argument. Every accepted socket gets a handler.
- **Handlers are not tracked:** the task is discarded, so nothing can await, count or close the live connections.
- **Any `SocketException` from accept ends that listener's loop for good**, with no log line, while `IsListening`
  (L85: `_listeners.Count > 0 && _disposed == 0`) still says true. See §11.1.

### 1.3 `DisposeAsync` (L573-614), in order

1. `Interlocked.Exchange(ref _disposed, 1)` (L575).
2. `_queue.Writer.TryComplete()` (L578): a handler that queues after this counts a drop logged "(the tap is shutting
   down)" (L340-342).
3. `await _cts.CancelAsync()` (L580-581).
4. `listener.Stop()` for each listener (L583-593). This closes the listening sockets, not accepted ones.
5. Awaits `_acceptLoop` and `_mapLoop` only (L595-607).
6. `_upstream?.Dispose()` (L609), `_cts?.Dispose()` (L610), the `stopped (...)` log line (L611-613).

It does not wait for connection handlers and does not close accepted sockets itself. Each handler ends when the
cancelled token reaches its pending read, write or forward, and its `using (client)` (L282) and `await using var stream`
(L284) close the socket. Two facts rely on that: `OtlpTapHangUpTests.cs:65` ("DisposeAsync returns before the export's
handler ends; the exporter's end of stream is the handler's end.") and `OtlpExporterTests.cs:248-268`, where
`DisposeAsync` completes while a handler is parked in the `AfterResponseWritten` seam. The hang-up plan measured the
consequence: the `stopped (...)` line can print a count before a late handler moves it (PROXY_TAP_HANG_UP_PLAN.md §2.4
O1). A timeout counted by a handler after disposal would be missing from that line the same way.

### 1.4 Does cancellation reach a pending socket operation? (SOURCE)

- `NetworkStream.ReadAsync(Memory<byte>, CancellationToken)` returns `_streamSocket.ReceiveAsync(buffer,
  SocketFlags.None, fromNetworkStream: true, cancellationToken: cancellationToken)`; `WriteAsync(ReadOnlyMemory<byte>,
  CancellationToken)` returns `_streamSocket.SendAsyncForNetworkStream(buffer, SocketFlags.None, cancellationToken)`;
  `FlushAsync` returns `Task.CompletedTask` and `Flush()` is empty (NetworkStream.cs).
- Windows: `ProcessIOCPResult` registers, for an operation left pending, `cancellationToken.UnsafeRegister(...)` that
  calls `Interop.Kernel32.CancelIoEx(handle, thisRef._pendingOverlappedForCancellation)` (SocketAsyncEventArgs.Windows.cs).
- Unix: `OperationQueue.StartAsyncOperation` sets `operation.CancellationRegistration =
  cancellationToken.UnsafeRegister(s => ((TOperation)s!).TryCancel(), operation)`, and `ProcessCancellation` sets
  `SocketError.OperationAborted` (SocketAsyncContext.Unix.cs).
- The exception (Socket.Tasks.cs, `ThrowException`/`CreateException`): `OperationAborted` or `ConnectionAborted` with the
  token cancelled becomes `OperationCanceledException`, unwrapped; any other error on a `NetworkStream` becomes an
  `IOException` wrapping the `SocketException`. So a deadline token gives `OperationCanceledException`; a timer that
  disposes the client under a pending read gives `OperationAborted` with the token not cancelled, so an `IOException`.
  Both land in `ServeAsync`'s silent catch (L298). Not run here: a probe should confirm both forms on Windows and Linux.
- `NetworkStream.ReadTimeout`/`WriteTimeout` set the socket's `ReceiveTimeout`/`SendTimeout` options (NetworkStream.cs),
  which apply to synchronous calls only (REFERENCE: the `Socket.ReceiveTimeout` docs). The reader and the writer are
  async throughout, so neither property would bound them; nor would `TcpClient.ReceiveTimeout`.

## 2. `ServeAsync` (L277-306)

```csharp
try
{
    client.NoDelay = true;                                                        // L281
    using (client)                                                                // L282
    {
        await using var stream = client.GetStream();                              // L284
        var reader = new HttpRequestReader(stream, _options.MaxRequestBytes);     // L285
        while (!ct.IsCancellationRequested)                                       // L286
        {
            var request = await reader.ReadAsync(ct).ConfigureAwait(false);       // L288
            if (request is null)
                return;                                                           // L289-290
            var keepAlive = await HandleAsync(request, stream, ct).ConfigureAwait(false);   // L292
            if (!keepAlive)
                return;                                                           // L293-294
        }
    }
}
catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
{
    // The exporter hung up, or we are shutting down.                             // L298-301
}
catch (Exception ex)
{
    _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap connection error: {ex.Message}");   // L302-305
}
```

- One reader, and so one 16 KiB buffer, per connection (L285).
- A `null` request (the peer closed, cut a body short, or sent a head or chunk the reader cannot take) ends the
  connection with no answer (L289-290). No 400 or 431 is ever written for a malformed or oversized head.
- **Catches:** the first (L298-301) takes `IOException`, `SocketException`, `ObjectDisposedException` and
  `OperationCanceledException` and does nothing: not counted, not logged. The second (L302-305) logs
  `otlp-tap connection error: {message}`, not counted, not in `Diagnostics()`. `HandleAsync` has no catch of its own;
  `ForwardAsync` has one (§4.2).
- **Nothing bounds an idle keep-alive connection.** After an answer with keep-alive the loop goes straight back to
  `reader.ReadAsync(ct)` (L288), which waits in `FillAsync` for as long as the tap lives. A FIN from the exporter ends it
  at once (`FillAsync` returns false, `ReadHeadAsync` returns null at L130-131).
- `client.NoDelay = true` (L281) runs inside the `try` but before `using (client)` (L282). See §11.5.

## 3. `HttpRequestReader`

### 3.1 What one connection allocates, and the caps

| What | Where | Size |
|---|---|---|
| Read buffer | L39 `private readonly byte[] _buffer = new byte[16 * 1024];` | 16 KiB per connection (one reader per connection, OtlpTap.cs:285) |
| Head | L126 `new List<byte>(1024)`, grown one byte at a time (L133-136) | up to `MaxHeaderBytes` |
| A line (chunk size, chunk CRLF, trailer) | L271 `new List<byte>(16)` | up to the caller's `maxBytes` |
| Chunked body | L177 `new MemoryStream()` | up to `maxBodyBytes`, plus one `ToArray()` copy at L225 |
| A body or a chunk | L154 `var result = new byte[count];` | the declared length, before any of it is read |

Constants: `MaxHeaderBytes = 64 * 1024` (L36), `MaxChunkLineBytes = 1024` (L37). `maxBodyBytes` is the constructor's
argument (L34), `_options.MaxRequestBytes` (OtlpTap.cs:285).

### 3.2 The head (`ReadHeadAsync`, L124-150; parsed in `ReadAsync`, L44-80)

- Bytes are copied from `_buffer` into the list one at a time, and a CRLFCRLF state machine (L137-142) ends the head
  (L144-145). The stream ending first gives `null` (L130-131).
- `head.Count > MaxHeaderBytes` gives `null` (L146-147): the connection closes with no answer.
- There is no per-line cap on the request line or a header line, only the 64 KiB total.
- A request line with fewer than two parts gives `null` (L51-53). The version defaults to HTTP/1.1 (L57). Repeated
  headers are joined with ", " (L70). Keep-alive is off for HTTP/1.0 and follows `Connection: close`/`keep-alive`
  (L73-80).
- The tap never sends `100 Continue`. A client that sends `Expect: 100-continue` waits out its own expectation timeout
  before sending the body (REFERENCE: 1 s in .NET's `HttpClient` and in curl). A limit on the first body byte would
  have to allow for that.

### 3.3 A `Content-Length` body (L92-106)

```csharp
else if (headers.TryGetValue("Content-Length", out var lengthText)
         && int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
         && length > 0)
{
    if (length > maxBodyBytes)
    {
        // Drain rather than buffer: the connection stays clean, so the 413 actually reaches the caller.
        await DiscardAsync(length, ct).ConfigureAwait(false);                                       // L99
        return new HttpRequestMessageData(method, target, PathOf(target), headers, [], keepAlive, true);   // L100
    }

    body = await ReadExactAsync(length, ct).ConfigureAwait(false);                                  // L103
    if (body.Length < length)
        return null; // truncated                                                                    // L104-105
}
```

- **Over the cap:** decided from the header alone, before any allocation (L96). `DiscardAsync` (L255-266) then reads
  and drops the whole declared length through the 16 KiB buffer. The 413 is written only after the drain, so it waits
  for the exporter to send everything it declared. If the stream ends mid-drain, `DiscardAsync` just returns (L260-261)
  and the request still comes back as too large (see §11.4).
- **Within the cap:** `ReadExactAsync` (L152-167) allocates `new byte[count]` at L154, then fills it. This is the issue's
  "body array sized up front to its Content-Length". A body cut short is copied once more (`result[..written]`, L166)
  and then dropped (`return null`, L104-105).
- A `Content-Length` that does not parse as a positive `int` takes the no-body path. See §11.3.

### 3.4 A chunked body (`ReadChunkedAsync`, L175-226)

- Taken when `Transfer-Encoding` contains "chunked" (L84-85), ahead of any `Content-Length`. A `null` result makes the
  whole request `null` (L87-88); `keepAlive &= chunked.Reusable` (L90).
- Size line: `ReadLineAsync(MaxChunkLineBytes, ct)` (L181); an extension is cut at `;` (L184-185); hex parsed by
  `TryParseChunkSize` (L232-253), which saturates past `int` rather than wrapping.
- `size > int.MaxValue` returns `([], true, false)` (L187-188): too large and not reusable, so a 413 with
  `Connection: close` and no drain.
- Over the cap (`tooLarge || body.Length + size > maxBodyBytes`, L192): `DiscardAsync((int)size, ct)` (L196) and keep
  going. Within it: `ReadExactAsync((int)size, ct)` (L200), so each chunk's array is allocated up front at L154 (a single
  chunk can be up to the cap less what arrived), then copied into the `MemoryStream` (L201).
- Each chunk's CRLF must read as an empty line (L205-206). Trailers are read to the blank line, each line and the
  section held to 64 KiB (L212-223). Result: `(tooLarge ? [] : body.ToArray(), tooLarge, true)` (L225).

### 3.5 Every read, and its token

All reads end in `FillAsync`, the reader's single I/O point:

```csharp
private async Task<bool> FillAsync(CancellationToken ct)          // L285
{
    _start = 0;
    _end = await stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);   // L288
    return _end > 0;
}
```

| Caller | Line | Call |
|---|---|---|
| `ReadAsync` | 46 | `ReadHeadAsync(ct)` |
| `ReadAsync` | 87 | `ReadChunkedAsync(ct)` |
| `ReadAsync` | 99 | `DiscardAsync(length, ct)` |
| `ReadAsync` | 103 | `ReadExactAsync(length, ct)` |
| `ReadHeadAsync` | 130 | `FillAsync(ct)` |
| `ReadExactAsync` | 158 | `FillAsync(ct)` |
| `ReadChunkedAsync` | 181, 205 | `ReadLineAsync(MaxChunkLineBytes, ct)` |
| `ReadChunkedAsync` | 196 | `DiscardAsync((int)size, ct)` |
| `ReadChunkedAsync` | 200 | `ReadExactAsync((int)size, ct)` |
| `ReadChunkedAsync` | 215 | `ReadLineAsync(MaxHeaderBytes, ct)` |
| `DiscardAsync` | 260 | `FillAsync(ct)` |
| `ReadLineAsync` | 274 | `FillAsync(ct)` |

Every `ct` is the one `ServeAsync` passes at OtlpTap.cs:288: the accept loop's token (L273), which is `_cts.Token`
(L179-180), linked to `StartAsync`'s token (L178). No read has a timeout of its own, and nothing records when a read
started or how many bytes a phase has taken. The reader is `internal` and takes any `Stream` (L34); the extension's
csproj gives `Kronikol.Tests.Otlp` its internals (L18), but no fact uses the reader directly.

### 3.6 `MaxRequestBytes` and the 413

- Option: `public int MaxRequestBytes { get; set; } = 32 * 1024 * 1024;` (OtlpTapOptions.cs:68), validated `> 0`
  (L153-154).
- Enforced before allocation in both framings: from `Content-Length` at L96, per chunk at L192.
- Answered in `HandleAsync` (OtlpTap.cs:310-316): `_rejected++` (L312), log `otlp-tap refused an export over
  MaxRequestBytes ({n})` (L313), then `413 Payload Too Large`, `application/json`, `{"error":"payload too large"}`, with
  `Connection: keep-alive` unless the request was HTTP/1.0, asked to close, or had a chunk size past `int` (L314-315).
- The 413 check comes before the `ExpectedHeaders` check (L310 before L318).
- The same value caps decompression in the map worker (`MapPayload`, L504, into `Decompress`, L531-570, which stops
  once past the cap, L559-560, and returns the partial buffer).

### 3.7 Where a stalled exporter waits, and what it holds

Always held: the handler task, the `TcpClient` and its `NetworkStream`, the reader and its 16 KiB buffer.

| The exporter stops | Waits at | Also holds | Released by |
|---|---|---|---|
| after connecting, or idle between keep-alive requests | `ReadHeadAsync` L130, `FillAsync` L288 | an empty head list (capacity 1,024) | disposal only |
| inside its head | same | the head so far, up to 64 KiB (L146) | disposal, or 64 KiB of head (closed, no answer) |
| inside a `Content-Length` body within the cap | `ReadExactAsync` L158 | `new byte[Content-Length]` from L154, up to `MaxRequestBytes` | disposal |
| inside a drained over-cap body | `DiscardAsync` L260 | nothing more; its 413 waits for the drain | disposal |
| inside a chunked body | `ReadLineAsync` L274, `ReadExactAsync` L158 or `DiscardAsync` L260 | the chunks so far (up to the cap) and the current chunk's array | disposal |
| waiting for the forward (not the exporter's stall) | `_upstream.SendAsync`, L434 | the body array | `HttpClient.Timeout` 30 s, then 502 |
| not reading its answer | `stream.WriteAsync`, L471 or L473 | the answer | disposal; only an answer larger than the socket's send buffer can wait, since the tap's own answers are under 200 bytes and only a relayed collector answer can be large |

Payloads already accepted wait in the bounded queue, up to `QueueCapacity` (256) of up to `MaxRequestBytes` each; that
bound is the sink's, not the caller's.

## 4. The answer write and the forward

### 4.1 `WriteResponseAsync` (L459-475)

```csharp
await stream.WriteAsync(Utf8(head.ToString()), ct).ConfigureAwait(false);    // L471
if (body.Length > 0)
    await stream.WriteAsync(body, ct).ConfigureAwait(false);                  // L473
await stream.FlushAsync(ct).ConfigureAwait(false);                            // L474
```

- The connection's `NetworkStream` (typed `Stream`) with the lifetime token. A `byte[]` binds to
  `WriteAsync(ReadOnlyMemory<byte>, CancellationToken)`, which `NetworkStream` overrides, so the token reaches the
  socket (SOURCE, §1.4). The flush is a no-op.
- The head carries `Content-Type` and `Content-Encoding` when given, `Content-Length` and `Connection: keep-alive|close`
  echoing the request (L462-469).
- Seven call sites: 413 (L314), 401 (L322), 200 (L352-356), 404 (L360), 400 (L410-411), 502 (L449-450), and the
  collector's answer relayed (L456).

### 4.2 The forward (`ForwardAsync`, L404-457)

- `_upstream` (L163-173): `new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false,
  AutomaticDecompression = DecompressionMethods.None, UseCookies = false }) { BaseAddress = _options.ForwardBaseUri,
  Timeout = TimeSpan.FromSeconds(30) }`. **There is no `ForwardTimeout` or `ConnectTimeout` option**; the handler's
  `ConnectTimeout` is left at its default, so the 30 s covers the connect too. ProxyTap, by contrast, exposes
  `ForwardTimeout` (200 s, "a tap must never be the tightest deadline on the path") and `ConnectTimeout` (5 s)
  (ProxyTapOptions.cs:138-142) and links its deadline to the tap's lifetime (ProxyTap.cs:397-399).
- A target with no origin form is answered 400 and not forwarded (L407-413).
- In the `try`: `SendAsync(message, HttpCompletionOption.ResponseContentRead, ct)` (L434) and `ReadAsByteArrayAsync(ct)`
  (L435).
- The catch (L443): `(HttpRequestException or TaskCanceledException or IOException or SocketException) &&
  !ct.IsCancellationRequested` counts `_forwardFailures` (L447), logs `otlp-tap forward failed: {message}` (L448) and
  answers 502 (L449-450). The client's 30 s timeout arrives as a `TaskCanceledException` and takes this catch; the
  tap's own cancellation does not, and ends the connection uncounted (comment L445-446).
- Since 4.19.3 (#172 R2) the relay write is outside the `try` (L454-456): a failure there is the exporter's and falls to
  `ServeAsync`'s silent catch.
- The forward runs before the answer, so the exporter waits up to 30 s for it. REFERENCE: OTel exporters default to a
  10 s export timeout (`OTEL_EXPORTER_OTLP_TIMEOUT`), so with a slow collector the exporter gives up first.

## 5. Options, validation and DI

### 5.1 Every option (`OtlpTapOptions.cs`)

| Option | Line | Default | `Validate()` (L140-155) | `Copy` line (extensions) |
|---|---|---|---|---|
| `ListenPort` | 34 | 0 | 0 to 65535 (L142-143) | 44 |
| `ListenHost` | 44 | `localhost` | not blank (L144-145) | 45 |
| `ForwardBaseUri` | 53 | null | absolute http or https (L147-148) | 46 |
| `ExpectedHeaders` (get-only) | 62 | empty, names case-insensitive | none | 47-48 |
| `TracesPath` | 65 | `/v1/traces` | not blank, starts with `/` (L149-150) | 49 |
| `MaxRequestBytes` | 68 | 32 MiB | `> 0` (L153-154) | 50 |
| `QueueCapacity` | 75 | 256 | `> 0` (L151-152) | 51 |
| `Sink` | 78 | `RequestResponseLoggerSink.Instance` | none | 52 |
| `Phase` | 81 | `TestPhase.Unknown` | none | 53 |
| `ServiceNameMap` (get-only) | 90 | empty | none | 54-55 |
| `CaptureKinds` (get-only) | 93 | `db`, `http` | none | 56-57 |
| `IncludeServerSpans` | 100 | false | none | 58 |
| `AttributeByTraceId` | 109 | true | none | 59 |
| `KnownTestIds` | 116 | null | none | 60 |
| `FallbackTestId` | 119 | null | none | 61 |
| `FallbackTestName` | 122 | `TestIdentityScope.UnknownTestName` | none | 62 |
| `ContentCapBytes` | 125 | 65536 | none | 63 |
| `DefaultCallerName` | 128 | `unknown-service` | none | 64 |
| `Log` | 133 | null | none | 65 |
| `Name` | 136 | null (`DisplayName` falls back to `otlp`, L138) | none | 66 |

No option is a time. The class is also the input to `SpanToInteractionMapper` for offline mapping (L28-29), so a
listener-only option would sit beside mapping options, as `MaxRequestBytes` and `QueueCapacity` already do. Precedent
for a `TimeSpan` option in this package: `OtlpExportOptions.Validate` rejects `<= TimeSpan.Zero` for `FlushInterval`,
`PendingRequestTtl` and `ShutdownTimeout` (OtlpExportOptions.cs:140-145). `ProxyTapOptions.Validate` (L186-199) does
not validate its two timeouts.

### 5.2 DI (`OtlpTapServiceCollectionExtensions.cs`)

- `AddOtlpTapTestTracking(Action<OtlpTapOptions>)` (L14-27): new options, configure, `Validate()` (L21), one
  `OtlpTap` singleton and one `OtlpTapHostedService` (L23-25).
- `AddOtlpTapTestTracking(IEnumerable<OtlpTapOptions>)` (L30-40) goes through the first overload with
  `Copy(captured, o)` (L36). `Copy` (L42-67) copies all 20 public options.
- `OtlpTapHostedService` (L71-81): `StartAsync` passes the host's start token to `Tap.StartAsync` (L77), which links it
  into the tap's lifetime (OtlpTap.cs:178); `StopAsync` disposes the tap and ignores the stop token (L80).
- **Facts:** the only DI fact uses the `Action` overload (`DI_registration_starts_and_stops_the_tap_with_the_host`,
  OtlpTapTests.cs:410-434). The list overload and `Copy` have none. The model is
  `Registering_taps_from_a_list_copies_every_option` (tests/Kronikol.Tests.ProxyTap/ProxyTapRegistrationTests.cs:34-74),
  written after #170 found ProxyTap's `Copy` missing `InFlightRegistry`; its `NonDefault` table (L16-32) has a
  `TimeSpan` case (L24) but none for a get-only `Dictionary<string, string>` (`ExpectedHeaders`, `ServiceNameMap`) or
  `Func<string, bool>` (`KnownTestIds`).
- `Options_are_validated` (OtlpTapTests.cs:389-397) covers `ListenPort`, `ListenHost`, `TracesPath` and
  `QueueCapacity`; `ForwardBaseUri`'s scheme is covered through `StartAsync` (OtlpTapForwardTargetTests.cs:73-83);
  `MaxRequestBytes <= 0` has no fact.

## 6. Counters, `Diagnostics()` and log lines

### 6.1 Public counters (`Interlocked` fields L51-59)

| Property | Line | Moves at | Doc comment |
|---|---|---|---|
| `RequestsReceived` | 88 | L330 (traces path and POST, authenticated, body read whole) | "Authenticated export requests accepted so far." |
| `UnauthenticatedRequests` | 91 | L320 | "Requests rejected with `401` because `ExpectedHeaders` was not satisfied." |
| `ForwardFailures` | 94 | L447 | "Requests whose forward to `ForwardBaseUri` failed (answered `502`)." |
| `PayloadsDropped` | 97 | L340 | "Export payloads dropped because the mapping queue was full (D3: capture never blocks)." |
| `PayloadsRejected` | 100 | L312 | "Export requests refused with `413` because the body exceeded `MaxRequestBytes`; their spans were never read." |
| `PayloadsFailed` | 103 | L525 | "Accepted payloads whose decode, span mapping or sink write threw ...; their spans are lost." |
| `SpansReceived` | 106 | L506 | "Spans decoded from accepted payloads." |
| `SpansMapped` | 109 | L520 | "Spans that became interactions." |
| `SpansIgnored` | 112 | L513 | "Spans decoded but not captured ..." |

A stalled export reaches none of them: `RequestsReceived` moves only once the whole request is read. Nothing counts
connections, connections closed with no answer (cut short, malformed, oversized head), `connection error` lines, or
time limits.

### 6.2 `Diagnostics()` (L114-154)

One `DiagnosticKind.CaptureDegraded` entry per non-zero problem counter, each built with
`string.Create(CultureInfo.InvariantCulture, ...)`, in this order (`{name}` is `DisplayName`):

1. L131: `{name}: {dropped:N0} export payload(s) dropped because the mapping queue was full (QueueCapacity {n}) — their spans are missing from the diagrams; the exporter was never delayed`
2. L136: `{name}: {rejected:N0} export request(s) refused with 413 Payload Too Large (MaxRequestBytes {n:N0}) — their spans were never read`
3. L141: `{name}: {failed:N0} export payload(s) failed while being decoded, mapped or written to the sink — their spans are lost (see the tap's log for the exception)`
4. L146: `{name}: {unauthenticated:N0} export request(s) rejected with 401 — ExpectedHeaders not satisfied; if that was your exporter, its spans never reached the report`
5. L151: `{name}: {forwardFailures:N0} export(s) could not be forwarded to {ForwardBaseUri} and were answered 502 Bad Gateway — the spans were still mapped here, but the real collector never saw them`

The summary (L114-122) says the list feeds `IngestRequest.HostDiagnostics` and that `SpansIgnored` is by design and not
reported. Facts: empty when healthy (OtlpTapTests.cs:475-488), the culture fact for line 2 only (L490-507), lines 2 and 3
(L509-535), lines 4, 5 and 1 (L537-572).

### 6.3 Log lines (all `[{DisplayName}] otlp-tap ...`)

`listening on {TracesEndpoint}[ → {ForwardBaseUri}]` (L182-183); `could not also bind {address}: {message}` (L210);
`connection error: {message}` (L304); `refused an export over MaxRequestBytes ({n})` (L313);
`rejected an unauthenticated {Method} {Path}` (L321); `dropped an export payload (the tap is shutting down)` or
`(queue full)` (L341-342); `refused a request target with no path to forward: {Method} {Target}` (L409);
`forward failed: {message}` (L448); `could not map an export payload: {message}` (L526); and
`stopped ({RequestsReceived} exports, {SpansReceived} spans, {SpansMapped} mapped, {SpansIgnored} ignored,
{PayloadsDropped} dropped, {UnauthenticatedRequests} unauthenticated, {ForwardFailures} forward failures)` (L611-613),
which leaves out `PayloadsRejected` and `PayloadsFailed`.

## 7. Test seams

- `internal Func<Task>? BeforeQueued { get; set; }` (L371), awaited at L335-336 for a traces request only, after
  authentication and the `RequestsReceived` count and before `TryWrite`.
- `internal Func<Task>? AfterResponseWritten { get; set; }` (L373), awaited at L363-364 after the 200, 404, 400, 502 or
  relayed answer. Not after a 413 or 401: those return early (L315, L323).
- Comment L369-370: a fact disposes the tap at either point. Used by `OtlpExporterTests.cs:258` and `:280-284`.
- `InternalsVisibleTo Kronikol.Tests.Otlp` (Kronikol.Extensions.Otlp.csproj L18).
- There is no seam in `ServeAsync` or the reader (no hook when a read starts or a phase changes) and no clock
  abstraction. `TimeProvider` appears nowhere in `src/` or `tests/`.

## 8. Every time limit in the package (searched: `Timeout`, `TimeSpan`, `CancelAfter`, `Timer`, `ReceiveTimeout`, `SendTimeout`, `Stopwatch`, `Delay`, `WaitAsync`, socket options)

| Where | What | Side |
|---|---|---|
| OtlpTap.cs:172 | forward `HttpClient.Timeout = TimeSpan.FromSeconds(30)` | the tap's upstream; the only one in the tap |
| OtlpExporter.cs:69 | `HttpClient.Timeout = TimeSpan.FromSeconds(30)` | client |
| OtlpExportOptions.cs:101, 107, 114 | `FlushInterval` 2 s, `PendingRequestTtl` 30 s, `ShutdownTimeout` 5 s | client |
| OtlpExportSink.cs:131, 149, 197, 323 | `FlushAsync(TimeSpan?)`, `WaitAsync(limit)`, a `Task.Delay` wait, `_worker.WaitAsync(ShutdownTimeout)` | client |

No `CancelAfter`, `Timer`, `ReceiveTimeout`, `SendTimeout`, `Stopwatch`, linger or TCP keep-alive option anywhere in the
package; the only socket option set is `NoDelay` (OtlpTap.cs:281). Sibling precedents for a deadline:
`ProxyTap.cs:397-399` (`CreateLinkedTokenSource(lifetime)` then `CancelAfter(ForwardTimeout)`) and `TcpTap.cs:212-215`
(`CreateLinkedTokenSource(ct)` then `CancelAfter(ConnectTimeout)` around `ConnectAsync`).

## 9. Tests (`tests/Kronikol.Tests.Otlp/`)

### 9.1 Classes and shared helpers

| Class or helper | Where | What it is |
|---|---|---|
| `ListSink`, `BlockingSink`, `ThrowingSink` | OtlpTapTests.cs:15-33, 36-39, 42-45 | sinks shared by the project |
| `StubCollector` | OtlpTapTests.cs:48-111 | `HttpListener` on `http://localhost:{LoopbackPort.Free()}/` (L57-58, L102), records method, path, headers, body |
| `OtlpTapTests` | OtlpTapTests.cs:113-573 | 19 tests (one a theory), 17 of them through `HttpClient` (`PostAsync`, L122-137, a new client per call); `WaitForAsync` 5 s (L139-150) |
| `ClosedPort` | OtlpTapTests.cs:582-591 | a bound, never-listening socket from `LoopbackPort.Closed()` |
| `OtlpTapHangUpTests` | OtlpTapHangUpTests.cs:14-408 | raw-socket exporters and a raw-socket collector (#172 R2) |
| `HeldCollector` | OtlpTapHangUpTests.cs:321-407 | raw `TcpListener` from `LoopbackPort.Listening()` (L323); reads one export, signals `HasExport`, answers 200 only on `Answer()` |
| `OtlpTapForwardTargetTests` | OtlpTapForwardTargetTests.cs:12-84 | `RawAsync` (L22-32): one request line by `TcpClient`, `Content-Length: 0`, `Connection: close`, `ReadToEndAsync().WaitAsync(30 s)` |
| `OtlpExporterTests` (two tap facts) | OtlpExporterTests.cs:248-268, 270-296 | dispose the tap at `AfterResponseWritten` and at `BeforeQueued` |
| `SequencedCollector` | OtlpExporterTests.cs:10-69 | `HttpListener` collector for exporter facts |

### 9.2 How a raw exporter is driven (`OtlpTapHangUpTests`)

- The tap: `ListenPort = 0`, `ListenHost = "127.0.0.1"`, a `ListSink`, a locked `List<string>` log (`StartAsync`,
  L173-186). It binds port 0 itself and keeps it; only the stubs take ports from `LoopbackPort`.
- The exporter: `new TcpClient()` connected to `IPAddress.Loopback:tap.BoundPort` (`ConnectAsync`, L188-193); bytes
  sent with `exporter.Client.SendAsync(...)` (L37, L92, L114, L139-140, L161). Requests are built by hand:
  `ContentLengthHead` (L20-21), `ContentLengthExport` (L23), `ChunkedHead` (L25-26), `Chunked` (L196-207, 100-byte
  chunks with an optional extension).
- A FIN: `exporter.Client.Shutdown(SocketShutdown.Send)` (L94). An RST alone: `ResetAlone` (L210-214,
  `LingerState(true, 0)` then `Socket.Dispose()`).
- Observing: `HearAsync` (L234-257) returns the first line heard and whether the tap let go (end of stream or reset)
  within `Patience` (5 s, L16), reading with `ReceiveAsync(buffer, SocketFlags.None, token)`; `ReadAnswersAsync`
  (L267-315) reads N answers by `Content-Length` and fails on a close or reset. `Lines(log, text)` (L216-220) and
  `WaitForAsync` (L222-231) poll.
- A time-limit fact fits this shape: configure a short limit, connect, send nothing or a partial request, and assert
  `HearAsync` reports the tap let go well inside `Patience`, with the new counter or log line. The comment at L160 is
  the reset rule such a fact must respect: a tap that closes with bytes it never read resets the connection.

### 9.3 Ports, parallelism, CI

- `LoopbackPort.cs` is linked from `tests/Kronikol.Tests/` (csproj L38-39): `Free()` for `HttpListener` stubs,
  `Listening()` and `Closed()` for raw ones, never one port twice in the process.
- No `[Collection]`, `[CollectionDefinition]`, `CollectionBehavior` or `xunit.runner.json` in the project: with xunit.v3
  (3.2.2, csproj L15) each class is its own collection, classes run in parallel and a class's facts in sequence.
  `NoWarn xUnit1051` (L6). `CultureRun.cs` is linked (L33).
- CI runs the project only in the "Remaining Unit Tests (auto-discovered)" entry (ci.yml L79-80; the loop L287-303),
  on `ubuntu-latest` (the matrix default, L60). No Windows or macOS lane runs it.

### 9.4 What no fact covers

A stalled or idle exporter (nothing at all); the list overload and `Copy`; `MaxRequestBytes <= 0`; a chunked body over
`MaxRequestBytes` that fits an `int` (drained, connection kept); whether a connection stays usable after a drained 413
(each `PostAsync` uses a new `HttpClient`); a `Content-Length` that does not parse; an unauthenticated over-cap request.

## 10. `OtlpExporter` and `OtlpExportSink` are clients, out of scope

`OtlpExporter` (OtlpExporter.cs:51) owns an `HttpClient` (L54, built L61-70) and POSTs pages of spans to
`OtlpExportOptions.Endpoint` (`PostAsync`, L121; `SendAsync`, L149); its doc calls it "a standalone `HttpClient`
POSTing to a URL" (L43). `OtlpExportSink` (OtlpExportSink.cs:32) is an `IRequestResponseSink` with a bounded channel
(L52-56) and a worker (L60) that pushes through an `OtlpExporter` (L35, L51, L304). Neither opens a listener or accepts
a connection, so #173's caller-side limits do not apply to them.

## 11. Found on the way (READ; none run)

1. **The accept loop dies on any `SocketException` (L268-271).** One failed accept ends that listener's loop for good,
   with no log line and `IsListening` still true. With connections unbounded and held, running out of file descriptors
   would fail an accept that way. REFERENCE, not checked here: Kestrel's socket listener treats `ConnectionReset` at
   accept as one lost connection and keeps accepting. A probe should find which accept errors .NET raises per platform.
2. **The secret is checked after the body, and the 413 before the secret (§0 item 7).** Besides the memory exposure,
   an over-cap request without the secret is answered 413 and counted in `PayloadsRejected`, while `ExpectedHeaders`'
   doc says anything missing the secret is answered 401 and counted in `UnauthenticatedRequests`
   (OtlpTapOptions.cs:56-61).
3. **A `Content-Length` that does not parse is read as no body (L92-94).** A value past `int`, a negative one, a
   duplicated header joined to `10, 10` (L70), or junk all fall through with `body = []`. On the traces path the tap
   counts it in `RequestsReceived` (L330), queues nothing (L338 needs a body) and answers 200 (or forwards an empty
   body), then, on a keep-alive connection, reads the body bytes as the next request's head. REFERENCE: RFC 9112 §6.3 asks for a 400 and a close for an invalid `Content-Length`, and for
   identical repeated values to be taken as one. Real exporters send a valid length, so this is rare, but the export is
   lost under a 200.
4. **An over-cap `Content-Length` drain cut short still counts.** `DiscardAsync` returns quietly at end of stream
   (L260-261), so `ReadAsync` returns a too-large request (L100), `PayloadsRejected` moves and a 413 is written to a
   connection the exporter has left. The chunked drain cut short returns `null` instead (L204-206), as #172 R2 made a
   body cut short.
5. **`client.NoDelay = true` (L281) is outside `using (client)` (L282).** If the setter threw (a `SocketException` from
   `setsockopt`), the catch at L298 would take it and the client would never be disposed; its socket would wait for
   finalization. Not seen; worth one line of reordering if `ServeAsync` is touched.
6. **The `stopped (...)` log line (L611-613) leaves out `PayloadsRejected` and `PayloadsFailed`**, the same two the
   wiki's counter line leaves out (PROXY_TAP_HANG_UP_PLAN.md §6.3).
7. **Both `Transfer-Encoding` and `Content-Length`:** the reader takes the chunked framing (L84-91) and keeps the
   connection. REFERENCE: RFC 9112 §6.1 says a server must close the connection after answering such a request. Low
   impact here, since the forward re-frames the body and drops both headers (hop-by-hop list, L37-41).

## 12. Facts that shape the design

- **Hooks:** one read point (`FillAsync`, L285-290), one write point (`WriteResponseAsync`, L459-475), one place per
  connection that owns the loop (`ServeAsync`, L286-295). A per-request or per-phase deadline can be a CTS linked to
  `ct` with `CancelAfter`, passed where `ct` goes today, or a timer that disposes the client. Telling a timeout from the
  tap stopping needs a filter like the forward's (L443: the deadline cancelled while `ct` is not).
- **Phases the reader would have to expose** for separate limits: waiting for a request's first byte (idle keep-alive
  included), the rest of the head, the body (or drain), and the answer write. Today none is visible from outside the
  reader.
- **Closing on a timeout:** a close with unread bytes in the receive buffer is a reset (the reason for the drain, L98,
  and the comment at OtlpTapHangUpTests.cs:160), so an answer written just before such a close may never be read.
  REFERENCE: 408 Request Timeout is RFC 9110 §15.5.9; the OTLP/HTTP spec retries only 429, 502, 503 and 504, so a
  compliant exporter would not retry a 408, while a dropped connection is a transport error most SDKs retry.
- **What a limit cannot reach without other changes:** memory held by a slow but live exporter within the limit (the
  up-front array, L154), and the secret checked after the body (§11.2).
- **Defaults:** ProxyTap's rule that a tap must never be the tightest deadline on the path (ProxyTapOptions.cs:138);
  OTel exporters' 10 s default (REFERENCE); for scale, Kestrel's defaults (REFERENCE, not checked here): keep-alive
  130 s, request headers 30 s, a minimum body rate of 240 bytes/s after a 5 s grace.
- **Semver and records:** a new option, counter or diagnostic line is a minor; the option goes into `Copy` (L42-67) and
  needs the every-option fact; the counter into `Diagnostics()` and probably the `stopped` line; the wiki page
  `Integration-Otlp-Extension.md` lists the counters (PROXY_TAP_HANG_UP_PLAN.md §6.3 names its L143).
- **Line references that moved:** PROXY_TAP_HANG_UP_PLAN.md §8 put the 16 KiB buffer at HttpRequestReader.cs:38 and the
  up-front array at L150; at `3240c983` they are L39 and L154.
