# .NET HttpListener: timers, timeout settings, cancellation and Abort (source notes for #173)

Read on 2026-10-14 for `plans/TAP_CALLER_TIME_LIMITS_PLAN.md`. Nothing in this file was run. Each claim is tagged:

- **SOURCE**: read in the code at the commit named below, with file and line.
- **DOC**: quoted from a vendor document (URL given, fetched 2026-10-14).
- **INFERRED**: follows from the SOURCE lines cited, but was not run. Section 8 lists each one as a probe to run.

## Commits read

| Repo | Ref | Commit | Committed |
|---|---|---|---|
| dotnet/runtime | `release/10.0` | `591ede817344fa749dd1af56d7e73b356da3e71a` | 2026-10-12T23:01:10Z |
| dotnet/runtime | `main` | `56ba1c0be24c3837e5ac4ed13267b40646b906c7` | 2026-10-14T05:56:30Z |
| dotnet/aspnetcore | `release/10.0` | `c4b88d9d275a53a56edca52763b543b0c24cf84e` | 2026-10-13T21:30:33Z |
| dotnet/yarp | `main` | `2aa3d83550a9b7a719e347ef6a0363e5ea9505a9` | 2026-10-05T15:38:55Z |

The files were fetched from raw.githubusercontent.com at those SHAs, and the line numbers come from those files. Every
line number below is `release/10.0` unless it says "main".

Path abbreviations:

- `HL/` = `src/libraries/System.Net.HttpListener/src/System/Net/`; `HL/M/` = `HL/Managed/`; `HL/W/` = `HL/Windows/`
- `HLref` = `src/libraries/System.Net.HttpListener/ref/System.Net.HttpListener.cs`
- `HLtests/` = `src/libraries/System.Net.HttpListener/tests/`
- `SK/` = `src/libraries/System.Net.Sockets/src/System/Net/Sockets/`
- `CL/Stream.cs` = `src/libraries/System.Private.CoreLib/src/System/IO/Stream.cs`

**release/10.0 against main.** Of the files read, these differ: `HL/M/HttpConnection.cs` (main inserts a 5-line comment
at 87-91 saying TLS is never negotiated by the managed implementation, so every `release/10.0` line after 86 is +5 on
main), `HL/M/ChunkStream.cs` (chunk-size parsing), `HL/M/HttpListenerRequest.Managed.cs` (header trimming),
`HL/M/HttpListenerResponse.Managed.cs` (status description as Latin1), `HL/M/HttpListenerContext.Managed.cs` (Basic
header test), `HL/HttpListener.cs` (prefix compare), and on Windows `HL/W/HttpListener.Windows.cs` and
`HL/W/HttpResponseStream.Windows.cs` (main adds an `EnableKernelResponseBuffering` AppContext switch, from #123425:
+7 lines after line 33 and +4 lines after line 33 respectively) and `HL/W/HttpListenerResponse.Windows.cs` (Latin1
header encoding). None of these diffs touches a timer, a `TimeoutManager` property, a stream override, `Abort` or
`Close`. So everything below holds on both refs.

---

## 0. The design-relevant facts in one place

1. **The managed listener has exactly one timer, and it only covers the wait for the first read of a request head.**
   It is armed at `HL/M/HttpConnection.cs:195` (90 s on a new connection, 15 s on a kept-alive one, hard-coded at
   :63 and :193-194), stopped at :257 on *every* read completion, and the partial-head re-read at **:320** (main
   :325) does not re-arm it. The issue's claim is correct at that exact line. One byte and then silence, or one byte
   at a time under 32 KiB, holds the connection with no limit. (SOURCE)
2. **No `HttpListenerTimeoutManager` property reaches that timer.** The managed `TimeoutManager` discards its
   listener (`HL/M/HttpListenerTimeoutManager.Managed.cs:13`); `DrainEntityBody` and `IdleConnection` only round-trip
   a value; `EntityBody`, `HeaderWait`, `MinSendBytesPerSecond` and `RequestQueue` throw `PlatformNotSupportedException`
   on set. Nothing in `HL/M/` reads any of them. (SOURCE)
3. **After a context is dispatched, the managed listener has no timer at all**: not for the queue before
   `GetContext`, not for body reads, not for response writes, not for a FIN (#39183). The only other bound is the
   1-second-per-read drain in `FlushInput` (`HL/M/HttpListenerRequest.Managed.cs:265`), run on `Close()`, never on
   `Abort()`, with no overall cap. (SOURCE)
4. **Tokens do nothing to body I/O already running, on either platform.** No listener stream overrides
   `ReadAsync`/`WriteAsync`/`CopyToAsync`; the base `Stream` checks the token once (`CL/Stream.cs:303-306`,
   `:723-728`) and then runs `BeginRead`/`EndRead` through `FromAsyncTrim` with no token (`:460-463`, `:764-771`).
   (SOURCE)
5. **`Response.Abort()` on the managed listener is not an abortive close.** It first writes the head if none was
   sent (#88196: a complete response with whatever status is set, 200 by default), writes the terminating
   `0\r\n\r\n` if the response is chunked, then `Shutdown(Both)` and `Close()`, which is a FIN, not a RST
   (`HL/M/HttpConnection.cs:504-545`, `HL/M/HttpResponseStream.Managed.cs:56-90`, `SK/SafeSocketHandle.cs:145-150`).
   So a chunked response cut by `Abort()` between writes **looks complete to the caller**; a Content-Length response
   cut by it is short and detectable. (SOURCE)
6. **After a managed `Abort()`, a pending `WriteAsync` completes successfully**, because `EndWriteCore` returns early
   once the stream is closed (`HL/M/HttpResponseStream.Managed.cs:261-262`). A pending body read faults
   (`IOException`), or reads EOF if the shutdown wins the race, which a chunked body turns into a clean end (#135680).
   The tap must take "a limit fired" from its own timer state, not from the exception of the I/O it interrupted.
   (INFERRED)
7. **A chunked managed response can block a thread synchronously**, and `Abort()` can block behind it: chunk headers,
   the trailing CRLF and the final chunk are synchronous socket writes (`HL/M/HttpResponseStream.Managed.cs:134-155,
   232-236, 268-270, 77-82`), and on Unix a synchronous send waits behind a pending async send with no time limit
   (`SK/SocketAsyncContext.Unix.cs:1947-1977`, `SK/SafeSocketHandle.Unix.cs:16`). ProxyTap's answer is Content-Length
   (per `read/proxytap.md`), so its own write path is fully asynchronous. (INFERRED)
8. **Windows (http.sys) has timers of its own, all 2 minutes except MinSendRate 150 bytes/s** (DOC). They time out
   the connection; no 408 is documented. `IdleConnection` and `HeaderWait` set through `HttpListener` apply only after
   the first request on a connection, because `HttpListener` sets them on its URL group (`HL/W/HttpListener.Windows.cs:134-136`).
   The getters return `TimeSpan.Zero`/0 until set, not the effective defaults (`HL/W/HttpListenerTimeoutManager.Windows.cs:29-41`).
9. **Disposing the listener answers stalled callers.** `HttpListener.Close/Stop/Abort` closes every connection with
   `HttpConnection.Close(true)`, which writes a head for an unbound context first: a caller stalled mid-head or idle on
   keep-alive gets `HTTP/1.1 200 OK` and a FIN. The 90/15 s timer, by contrast, closes with a RST and no response.
   (INFERRED)
10. **The runtime will not fix this.** A .NET networking maintainer wrote on 2026-01-22 (#123463) that "`HttpListener`
    is in maintenance mode (and has been for a while) so it's unlikely it will see new API additions". #39183,
    #88196, #63236 and #21808 have been open since 2017-2023.

---

## 1. Managed `HttpConnection`: the only timer (question 1)

File: `HL/M/HttpConnection.cs` (main line = release line + 5 for every line after 86).

### 1.1 The timer and its duration

| Line | Code | Note |
|---|---|---|
| 63 | `private int _timeout = 90000; // 90k ms for first request, 15k ms from then on` | hard-coded, not read from `TimeoutManager` |
| 64 | `private readonly Timer _timer;` | one `System.Threading.Timer` per connection |
| 103 (main 108) | `_timer = new Timer(OnTimeout, null, Timeout.Infinite, Timeout.Infinite);` | created disarmed; never disposed anywhere in the file |
| 193-194 (main 198-199) | `if (_reuses == 1) _timeout = 15000;` | the field keeps 15000 for every later request on the connection |

**Which `TimeoutManager` property sets it: none.** `HL/HttpListener.cs:39` builds the manager with
`new HttpListenerTimeoutManager(this)`, and the managed constructor throws the argument away
(`HL/M/HttpListenerTimeoutManager.Managed.cs:13`: `internal HttpListenerTimeoutManager(HttpListener _) { }`). A search
of every file under `HL/M/` for `timeout|timer|TimeoutManager` finds only the lines in this table, the property getter
`HL/M/HttpListener.Managed.cs:21-28`, the 1-second wait in `FlushInput` (section 1.6) and the WaitOne calls in
`EndRead`/`EndGetContext`. (SOURCE)

### 1.2 Where it is armed

`BeginReadRequest()`, lines 188-204 (main 193-209):

```csharp
188  public void BeginReadRequest()
190      _buffer ??= new byte[BufferSize];
193      if (_reuses == 1)
194          _timeout = 15000;
195      _timer.Change(_timeout, Timeout.Infinite);
196      _stream.BeginRead(_buffer, 0, BufferSize, s_onreadCallback, this);
198  catch
200      _timer.Change(Timeout.Infinite, Timeout.Infinite);
201      CloseSocket();
202      Unbind();
```

It is called from exactly two places (SOURCE):

- on accept: `HL/M/HttpEndPointListener.cs:136` (`conn.BeginReadRequest();` at the end of `ProcessAccept`, 108-137);
- after a kept-alive response: `HL/M/HttpConnection.cs:519-525` (`if (!force && _context.Request.FlushInput()) { _reuses++; Unbind(); Init(); BeginReadRequest(); return; }`).

### 1.3 Where it is stopped

- **Line 257** (main 262), the first statement of `OnReadInternal`: `_timer.Change(Timeout.Infinite, Timeout.Infinite);`.
  This runs on every read completion, whatever was read.
- Line 200 (main 205), when `BeginRead` throws synchronously.

### 1.4 The partial-head re-read does not re-arm it: confirmed at line 320

`OnReadInternal`, lines 255-321 (main 260-326):

```csharp
257      _timer.Change(Timeout.Infinite, Timeout.Infinite);   // stop, on every completion
261      nread = _stream.EndRead(ares);
262      _memoryStream!.Write(_buffer!, 0, nread);
263      if (_memoryStream.Length > 32768)                     // head cap: 400 and close
265          SendError(HttpStatusDescription.Get(400), 400);
266          Close(true);
282      if (nread == 0) { CloseSocket(); Unbind(); return; }  // FIN before a whole head
289      if (ProcessInput(_memoryStream))                       // whole head: bind and dispatch
301          if (!_epl.BindContext(_context)) { ... 404 ... }
316          _contextBound = true;
317          listener.RegisterContext(_context);
318          return;
320      _stream.BeginRead(_buffer!, 0, BufferSize, s_onreadCallback, this);   // partial head: read again, no timer
```

**Confirmed: line 320 on `release/10.0` at 591ede8, line 325 on main at 56ba1c0.** (SOURCE)

What the timer therefore bounds: the time until the **first read of a head completes**, i.e. until the first segment of
the request arrives: 90 s on a fresh connection, 15 s between requests on a kept-alive one. It does not bound the head
as a whole. A caller that sends one byte and stops, or sends a byte at a time, is never timed out; the 32 KiB cap
(263-268) bounds the bytes, not the time. Such a caller has no `HttpListenerContext` yet, so nothing in the application
can reach it; only disposal releases it (section 1.8). (SOURCE)

### 1.5 What happens on timeout

```csharp
182  private void OnTimeout(object? unused)
184      CloseSocket();
185      Unbind();

486  private void CloseSocket()
488      if (_socket == null) return;
493      _socket.Close();          // no Shutdown first
498      _socket = null;
501      RemoveConnection();
```

- **No HTTP response** is written (no 408): `CloseSocket` closes the socket directly. (SOURCE)
- **It is a RST**, not a FIN. When the timer fires, the head read started at line 196 is still pending.
  Closing a socket with a pending operation and no prior `Shutdown(Send|Both)` switches to an abortive close:
  `SK/SafeSocketHandle.cs:143-150` (`canceledOperations |= OnHandleClose(); if (canceledOperations && !_hasShutdownSend) abortive = true;`),
  `SK/SafeSocketHandle.Unix.cs:173-177` (`OnHandleClose` returns `_asyncContext?.StopAndAbort()`), and
  `SK/SafeSocketHandle.Unix.cs:266-273` (abortive close sets `SO_LINGER` to `{1, 0}`: "set linger timeout to zero to get
  an abortive close (RST)"). That path is the same on Linux and macOS. (The comment at
  `SK/SafeSocketHandle.Unix.cs:186-187`, "On OSX, TCP connections will be closed with a FIN close instead of an
  abortive RST close", is about `TryUnblockSocket`, used only while a thread is blocked in a syscall on the handle,
  which an async receive is not.) (SOURCE)
- This is what #63236 reports: the 15 s keep-alive timer is armed right after a response is closed (1.2), even when the
  response's tail is still in the kernel send buffer; if the caller takes longer than 15 s to read that tail, the RST
  discards it and the caller gets a truncated response. (SOURCE for the mechanism; #63236 for the observation)
- The aborted head read then completes into `OnRead` (240-253) and `OnReadInternal`'s catch (270-280); since no head
  byte can have arrived (any arrival stops the timer), `SendError` is skipped and `_socket` is already null. (SOURCE)

### 1.6 Is there any timer after dispatch?

**No.** Line-by-line (SOURCE):

| Phase | Timer? | Where |
|---|---|---|
| Context queued, waiting for `GetContext` | none (no `RequestQueue` analogue) | `HL/M/HttpListener.Managed.cs:188-211` adds it to `_contextQueue` |
| Handler running | none | the timer was stopped at line 257 and is only re-armed by `BeginReadRequest` |
| Request body read | none | `HL/M/HttpRequestStream.Managed.cs:129-162` goes straight to `_stream.BeginRead` (161); `HL/M/ChunkedInputStream.cs:79-113` likewise via `base.BeginReadCore` (111, 143) |
| Response write | none | `HL/M/HttpResponseStream.Managed.cs:204-257` goes straight to `_stream.BeginWrite` (240) |
| Caller sends FIN mid-request | not noticed | #39183: nothing reads the socket between head and the app's own body reads |
| Body drained on `Close()` | 1 s **per read**, no overall cap | `FlushInput`, `HL/M/HttpListenerRequest.Managed.cs:250-280` |
| Idle between kept-alive requests | 15 s, until the first byte of the next head | 1.2, 1.4 |

`FlushInput` (`HL/M/HttpListenerRequest.Managed.cs:250-280`), called from `HttpConnection.Close(false)` at
`HL/M/HttpConnection.cs:519` when the app closes a response without having read the whole body:

```csharp
255      int length = 2048;
264      IAsyncResult ares = InputStream.BeginRead(bytes, 0, length, null, null);
265      if (!ares.IsCompleted && !ares.AsyncWaitHandle.WaitOne(1000))
266          return false;                 // -> Close(true): socket shut down and closed
267      if (InputStream.EndRead(ares) <= 0)
268          return true;                  // -> keep-alive: BeginReadRequest()
```

It runs synchronously on the thread that called `Response.Close()`. A caller that sends at least a byte per second
keeps that thread in the loop for as long as it likes (to the end of its Content-Length, or for ever if chunked).
`Abort()` passes `force: true`, and `!force && ...` at line 519 skips the drain. (SOURCE)

### 1.7 The managed listener has no TLS

`HL/M/HttpListener.Certificates.cs:20-24`: `LoadCertificateAndKey` returns `null` (comment: "TODO
https://github.com/dotnet/runtime/issues/19752"), and `HL/M/HttpEndPointListener.cs:115-119` closes every accepted
socket on a secure endpoint with no certificate. main's new comment at `HttpConnection.cs:87-91` says the same. So the
`SslStream` path in `HttpConnection` never runs. (SOURCE)

### 1.8 What disposal does to a caller with no context (INFERRED)

`HttpListener.Stop()`/`Abort()`/`Dispose()` (`HL/M/HttpListener.Managed.cs:68-149`) call `Close(bool)` (151-156):
`HttpEndPointManager.RemoveListener(this)` and then `Cleanup` (213-255).

- `Cleanup` calls `Connection.Close(true)` on every bound context (217-226), every registered connection (228-236)
  and every queued context (237-243).
- `RemoveListener` ends, for the last listener on an endpoint, in `HttpEndPointListener.Close()`
  (`HL/M/HttpEndPointListener.cs:306-318`), which calls `Close(true)` on every unregistered connection, i.e. every
  connection still waiting for or inside its first head.

`HttpConnection.Close(true)` (504-545) begins with `GetResponseStream()?.Close()` (508). For a connection whose current
context is not bound (a caller mid-head, or a kept-alive connection waiting for its next request after `Init()` at
523), `GetResponseStream` returns a fresh stream (232-233: `if (listener == null) return new HttpResponseStream(_stream, _context.Response, true);`),
and closing it runs `DisposeCore` (section 4.2), which writes a head because none was sent. The fresh request's
version is `HttpVersion.Version10` (`HL/M/HttpListenerRequest.Managed.cs:74`) unless a request line was parsed, so
the head is `HTTP/1.1 200 OK` with `Connection: close` and no length (or, after an HTTP/1.1 request line, a chunked 200
with `0\r\n\r\n`). Then the socket is shut down and closed (528-540). So **a caller stuck in its head, or idle on a
kept-alive connection, receives a complete empty 200 when the tap is disposed**, where the 90/15 s timer would have
sent it a RST and nothing else.

---

## 2. `HttpListenerTimeoutManager` on the managed side (question 2)

File: `HL/M/HttpListenerTimeoutManager.Managed.cs` (identical on main). (SOURCE)

| Property | Lines | Getter | Setter | Read by the listener? |
|---|---|---|---|---|
| `DrainEntityBody` | 15-25 | the stored value (field at 10, starts `TimeSpan.Zero`) | validates, stores: "Managed implementation currently doesn't pool connections, so this is a nop other than roundtripping the value." | no |
| `IdleConnection` | 27-37 | the stored value (field at 11) | same comment, validates, stores | no |
| `EntityBody` | 39-48 | `TimeSpan.Zero` | validates, then `throw new PlatformNotSupportedException(); // low usage, not currently implemented` | no |
| `HeaderWait` | 50-59 | `TimeSpan.Zero` | validates, then PNSE | no |
| `MinSendBytesPerSecond` | 61-71 | `0` | validates 0..`uint.MaxValue`, then PNSE | no |
| `RequestQueue` | 73-82 | `TimeSpan.Zero` | validates, then PNSE | no |

- Validation (84-89): whole seconds, 0..65535 (`ushort.MaxValue`), else `ArgumentOutOfRangeException`.
- The setters of `EntityBody`, `HeaderWait`, `MinSendBytesPerSecond` and `RequestQueue` carry
  `[SupportedOSPlatform("windows")]` (42, 53, 64, 76; also `HLref:145-149`), so the platform analyzer flags them;
  `DrainEntityBody` and `IdleConnection` have no attribute.
- **What the managed `HttpConnection` actually reads: none of them** (section 1.1). The comment's premise is also out of
  date: the managed listener does keep connections alive (`HttpConnection.cs:519-525`) with a fixed 15 s idle timer,
  so `IdleConnection` has a natural meaning there; it is just not wired.
- Tests pin this: `HLtests/HttpListenerTimeoutManagerTests.cs:394-440` (`HttpListenerTimeoutManagerUnixTests`,
  gated on `Helpers.IsManagedImplementation`), with `UnsupportedProperties_Throw` at 412-421 asserting PNSE for the four
  setters, and round-trip tests for `DrainEntityBody` (425-437) and `IdleConnection` (439-).
- History: dotnet/corefx#19128 "Remove a few PNSEs from HttpListener" (merged 2017-04-29) made the getter return an
  instance and made `DrainEntityBody`/`IdleConnection` round-trip "as we can implicitly 'respect' them due to them being
  about connection pooling and the managed implementation not doing any". Mono's copy throws
  `NotImplementedException` instead (mono/mono#12652, open).

---

## 3. Windows: http.sys timers (question 3)

### 3.1 The code (SOURCE)

`HL/W/HttpListenerTimeoutManager.Windows.cs`:

- 10-11: points at the native `HTTP_TIMEOUT_LIMIT_INFO` documentation.
- 29-30: `_timeouts = new int[5]`, "No initialization is required because a value of zero indicates that system
  defaults should be used."
- 35-41 `GetTimeout` and 169-177 `MinSendBytesPerSecond.get`: "Since we maintain local state, GET is local." So
  **reading a property returns `TimeSpan.Zero` (or 0) until the application sets it, not the 2-minute or
  150-bytes/s value http.sys is enforcing.** A plan that reads `TimeoutManager` to report the effective limit gets 0.
- 43-65 `SetTimespanTimeout`: whole seconds, 0..65535, then `_listener.SetServerTimeout(currentTimeouts, _minSendBytesPerSecond)`.
  Small runtime bug: line 61 `int[] currentTimeouts = _timeouts;` aliases the array, so line 62 updates local state
  before the native call, against the comment at 57-59 ("if that call succeeds, update local state"); a failed set
  leaves the getter reporting the new value.
- 71-89 `EntityBody`, 91-110 `DrainEntityBody`, 112-127 `RequestQueue`, 129-144 `IdleConnection`, 146-163
  `HeaderWait`, 165-190 `MinSendBytesPerSecond` (comment: "The default response send rate is 150 bytes-per-second.
  To disable this timer set it to UInt32.MaxValue").

`HL/W/HttpListener.Windows.cs`:

- 115-137 `SetServerTimeout`: fills `HTTP_TIMEOUT_LIMIT_INFO` with `Flags = HTTP_PROPERTY_FLAG_PRESENT` and all six
  values, then `SetUrlGroupProperty(HttpServerTimeoutsProperty, ...)` (134-136).
- 96-113 `SetUrlGroupProperty`: `HttpSetUrlGroupProperty` on `_urlGroupId`; a failure throws `HttpListenerException`.
- So `HttpListener` always sets the timers **on its URL group**, never on the server session or service-wide.

### 3.2 Meaning and defaults (DOC)

Source: [HTTP_TIMEOUT_LIMIT_INFO](https://learn.microsoft.com/en-us/windows/win32/api/http/ns-http-http_timeout_limit_info)
(updated 2024-02-22). Member text, verbatim:

- `EntityBody`: "The time, in seconds, allowed for the request entity body to arrive. The HTTP Server API turns on this
  timer when the request has an entity body. The timer expiration is initially set to the configured value. When the
  HTTP Server API receives additional data indications on the request, it resets the timer to give the connection
  another interval." (So it is an **idle** timer between body arrivals, not a total.)
- `DrainEntityBody`: "The time, in seconds, allowed for the HTTP Server API to drain the entity body on a Keep-Alive
  connection. On a Keep-Alive connection, after the application has sent a response for a request and before the
  request entity body has completely arrived, the HTTP Server API starts draining the remainder of the entity body to
  reach another potentially pipelined request from the client. If the time to drain the remaining entity body exceeds
  the allowed period the connection is timed out."
- `RequestQueue`: "The time, in seconds, allowed for the request to remain in the request queue before the
  application picks it up."
- `IdleConnection`: "The time, in seconds, allowed for an idle connection. This timeout is only enforced after the
  first request on the connection is routed to the application."
- `HeaderWait`: "The time, in seconds, allowed for the HTTP Server API to parse the request header. This timeout is
  only enforced after the first request on the connection is routed to the application."
- `MinSendRate`: "The minimum send rate, in bytes-per-second, for the response. The default response send rate is 150
  bytes-per-second. To disable this timer, set MinSendRate to MAXULONG."

Defaults table, same page:

| Timer | HTTP Server API Default | HTTP Server API Wide Configuration | Application Specific Configuration |
|---|---|---|---|
| EntityBody | 2 Minutes | No | Yes |
| DrainEntityBody | 2 Minutes | No | Yes |
| RequestQueue | 2 Minutes | No | Yes |
| IdleConnection | 2 Minutes | Yes | Limited |
| HeaderWait | 2 Minutes | Yes | Limited |
| MinSendRate | 150 bytes/second | No | Yes |

Remarks, verbatim: "The application-specific IdleConnection and HeaderWait timers are set on a limited basis. The HTTP
Server API cannot determine the request queue or URL group that the request is associated with until the headers have
been parsed. Therefore, the HTTP Server API enforces the default IdleConnection and HeaderWait timers for the first
request on a connection. Subsequent requests on a Keep-Alive connection will use the application specific timeouts."
And: "Setting a timeout to zero on a server session causes the HTTP Server API to revert to the default value for that
timer. For timers set on a URL Group, the server session timeout is used if present, otherwise the HTTP Server API
default is used."

[Server Timeouts Property](https://learn.microsoft.com/en-us/windows/win32/http/server-timeouts-property) (updated
2025-03-12) gives the same defaults as "120 Sec" with one-line definitions ("The connection expired while waiting for
the request entity body to arrive", and so on). Its table swaps two units ("MinSendRate ... 150 Sec", "RequestQueue
... 120 Bytes/Sec"): a typo; the structure page above is the reliable one.

### 3.3 What http.sys does when a timer fires (DOC, not measured)

[Types of Errors Logged by the HTTP Server API](https://learn.microsoft.com/en-us/windows/win32/http/types-of-errors-logged-by-the-http-server-api)
(updated 2025-04-15) separates two kinds:

- "Responses to clients": "The HTTP Server API sends an error response to a client, for example, a 400 error caused by
  a parse error in the last received request. After sending the error response, the HTTP Server API terminates the
  connection."
- "Connection time-outs": "The HTTP Server API times out a connection. If a request is pending when the connection
  times out, it is used to provide more information about the connection in the error log."

The timer reason phrases (logged to HTTPERR), verbatim:

- `Timer_AppPool` (the RequestQueue timer): "The connection expired because a request waited too long in an
  application pool queue for a server application to remove it from the queue and process it. This timeout duration is
  ConnectionTimeout, which is set by default to 2 minutes."
- `Timer_ConnectionIdle`: "The connection expired and remains idle. The default ConnectionTimeout duration is 2 minutes."
- `Timer_EntityBody`: "The connection expired before the request entity body arrived. ... Each time another data
  indication is received on this request, the HTTP Server API resets the timer to give the connection an additional
  2 minutes (or whatever is specified in ConnectionTimeout)."
- `Timer_HeaderWait`: "The connection expired because the header parsing for a request took more time than the default
  limit of 2 minutes."
- `Timer_MinBytesPerSecond`: "The connection expired because the client was not receiving a response at a reasonable
  speed, which means that the response send rate was slower than the default of 150 bytes/sec."
- `Timer_Response`: "Reserved. Not used." (http.sys has no limit on the application's own processing time.)

**So every timer is documented as the connection expiring; none is documented as sending a 408.** Whether http.sys
writes anything before closing, and whether the close is a FIN or a RST, is not stated: probe it if the plan's
Windows behaviour depends on it. For an `HttpListener` app the consequence (INFERRED) is that a pending
`HttpReceiveRequestEntityBody`/`HttpSendResponseEntityBody` completes with an error, which `EndRead` turns into
`HttpListenerException(errorCode)` (`HL/W/HttpRequestStream.Windows.cs:303-313`); an asynchronous send completion
error becomes `HttpListenerException(errorCode)` (`HL/W/HttpResponseStreamAsyncResult.cs:192-195`) and `EndWrite`
rethrows it after calling `_httpContext.Abort()` (`HL/W/HttpResponseStream.Windows.cs:236-260`).
`IgnoreWriteExceptions` suppresses only a failure returned synchronously by `HttpSendResponseEntityBody` once the head
is sent (`HL/W/HttpResponseStream.Windows.cs:204-210`), not an asynchronous one.

### 3.4 `Abort()` on Windows (SOURCE + DOC)

- `HL/W/HttpListenerResponse.Windows.cs:96-105`: `_responseState = ResponseState.Closed; HttpListenerContext.Abort();`.
- `HL/W/HttpListenerContext.Windows.cs:104-115` `Abort()` -> `ForceCancelRequest` (132-144): `HttpCancelHttpRequest`;
  if it returns `ERROR_CONNECTION_INVALID` ("the last write is in progress. The requestId becomes invalid as soon as
  the last Content-Length write starts"), `CancelLastWrite` uses `CancelIoEx` on that write
  (`HL/W/HttpResponseStream.Windows.cs:371-381`). Comment at 372: "Sync can only be cancelled by CancelSynchronousIo,
  but we don't attempt this right now." So **a synchronous `Write` on Windows is not interrupted by `Abort()`**; only
  http.sys's MinSendRate bounds it.
- [HttpListenerResponse.Abort](https://learn.microsoft.com/en-us/dotnet/api/system.net.httplistenerresponse.abort?view=net-10.0):
  "Closes the connection to the client without sending a response."
- `HLtests/HttpListenerResponseTests.cs:156-158`: `Abort_Invoke_ForciblyTerminatesConnection` runs on the Windows
  implementation only (`// [ActiveIssue("https://github.com/dotnet/runtime/issues/21808", TestPlatforms.AnyUnix)]`)
  and expects the client to get a `SocketException` (183-184).

---

## 4. Streams, tokens, and what `Abort()`/`Close()` do to pending I/O (question 4)

### 4.1 No listener stream overrides `ReadAsync`, `WriteAsync` or `CopyToAsync` (SOURCE)

| File | Overrides (lines) |
|---|---|
| `HL/HttpRequestStream.cs` (shared) | `Read` 16-28, `BeginRead` 30-37, `FlushAsync` 40 (returns `Task.CompletedTask`), `Write`/`BeginWrite`/`EndWrite` 54-61 (throw), `Dispose` 65-71 |
| `HL/M/HttpRequestStream.Managed.cs` | `EndRead` 164-213; virtual `ReadCore` 95-127 and `BeginReadCore` 129-162 |
| `HL/M/ChunkedInputStream.cs` | `ReadCore` 73-77, `BeginReadCore` 79-113, `EndRead` 152-174 |
| `HL/HttpResponseStream.cs` (shared) | `FlushAsync` 20 (returns `Task.CompletedTask`), `Read`/`BeginRead`/`EndRead` 34-41 (throw), `Write` 43-55, `BeginWrite` 57-64, `EndWrite` 66-73, `Dispose` 75-94 |
| `HL/M/HttpResponseStream.Managed.cs` | `DisposeCore` 56-90, `WriteCore` 166-202, `BeginWriteCore` 204-257, `EndWriteCore` 259-295 |
| `HL/W/HttpRequestStream.Windows.cs`, `HL/W/HttpResponseStream.Windows.cs` | Begin/End pairs only (`EndRead` at 217); no `ReadAsync`/`WriteAsync` |

A search for `ReadAsync|WriteAsync|CopyToAsync` over `HL/` finds only the WebSocket handshake's internal writes
(`HL/M/HttpResponseStream.Managed.cs:109, 157-163`).

So the base class runs (SOURCE, `CL/Stream.cs`):

- `ReadAsync(byte[], int, int, CancellationToken)`, 303-306: `cancellationToken.IsCancellationRequested ? Task.FromCanceled<int>(cancellationToken) : BeginEndReadAsync(buffer, offset, count);` (the token is not passed on).
- `ReadAsync(Memory<byte>, CancellationToken)`, 308-331: delegates to the array overload (312) or a rented array (316).
- `BeginEndReadAsync`, 450-464: because `BeginRead`/`EndRead` are overridden, `TaskFactory<int>.FromAsyncTrim(..., stream.BeginRead(...), stream.EndRead(...))` (460-463), with no token.
- `WriteAsync(byte[], ...)`, 723-728, `WriteAsync(ReadOnlyMemory<byte>, ...)`, 730-740, `BeginEndWriteAsync`, 754-772: the same shape.
- `CopyToAsync(Stream, int, CancellationToken)`, 85-116: a loop of `ReadAsync(..., cancellationToken)` (106) and `WriteAsync(..., cancellationToken)` (108), so a token is checked once per chunk, before each read and each write, never during one.
- The two `FlushAsync` overrides ignore their token entirely, even one already cancelled.

**A token that fires while a read or write is pending has no effect.** The operation ends when bytes move, or when the
socket fails or is closed. `Task.WaitAsync(token)` would only abandon the task, leaving the I/O pending (the point made
by a maintainer on #123463 about `GetContextAsync`).

### 4.2 `Abort()` on the managed listener, step by step (SOURCE)

1. `HL/M/HttpListenerResponse.Managed.cs:98-104` `Abort()`: `if (Disposed) return; Close(true);`. A second `Abort()`
   (or `Close()`) returns at once.
2. 106-110 `Close(bool force)`: `Disposed = true; _httpContext!.Connection.Close(force);`.
3. `HL/M/HttpConnection.cs:504-545` `Close(true)`:
   - 506-511: `GetResponseStream()?.Close(); _responseStream = null;`
   - `HL/HttpResponseStream.cs:75-94` `Dispose`: sets `_closed = true` (86) and calls `DisposeCore` (87).
   - `HL/M/HttpResponseStream.Managed.cs:56-90` `DisposeCore`:
     - 59 `GetHeaders(true)` builds a head if none was sent (114-128 -> `SendHeaders(closing: true, ...)`,
       `HL/M/HttpListenerResponse.Managed.cs:162-285`). For HTTP/1.1 with no length set, `SendHeaders` makes the
       response chunked (176-185); 68-75 then appends the last chunk. This is **#88196**: the caller gets a complete
       response with the status set at that moment, 200 by default, and an empty body. Status codes 400, 408, 411,
       413, 414, 500 and 503 add `Connection: close` (211-235).
     - 77-82: if the head was already sent and the response is chunked and the trailer not yet sent, it writes
       `0\r\n\r\n` (`GetChunkSizeBytes(0, true)`, 131-132).
     - Both writes go through `InternalWrite` (134-155), a **synchronous** `_stream.Write`.
     - 84-87 swallows `HttpListenerException`; 89 `_response.Close()` is a no-op (already `Disposed`).
   - 515-517 decide `force` (already true); 519 `FlushInput` is skipped because `force` is true.
   - 528-540: `s.Shutdown(SocketShutdown.Both)` (532) then `s.Close()` (539); 541-542 `Unbind(); RemoveConnection();`.
4. **FIN, not RST.** `Socket.Shutdown` (`SK/Socket.cs:2808-2827`) -> `SocketPal.Shutdown` records it
   (`SK/SocketPal.Unix.cs:2049-2065`, `handle.TrackShutdown(how)`), which sets `_hasShutdownSend`
   (`SK/SafeSocketHandle.cs:62-69`); the close then stays graceful even though pending operations are aborted
   (`SK/SafeSocketHandle.cs:145-150`: "Unless the user requested a normal close using Socket.Shutdown").
   `Socket.Close()` with the default `_closeTimeout` of -1 (`SK/Socket.cs:23, 69`) takes `CloseAsIs(abortive: false)`
   (`SK/Socket.cs:3446-3451`).

What the caller sees from a managed `Abort()` (SOURCE, by the state of the response):

| State when `Abort()` runs | Bytes the caller receives | Can the caller tell? |
|---|---|---|
| Head not sent | a full head with the current status (200 unless set), empty body, then FIN (#88196) | no, unless the status was set first |
| Content-Length head sent, body short | the bytes already written, then FIN | yes: fewer bytes than Content-Length |
| Chunked, between writes | the chunks written, then `0\r\n\r\n`, then FIN | **no: a well-formed end of body** |
| Chunked, a data write in flight | `size\r\n` + partial data, then `0\r\n\r\n`, no CRLF after the data (`EndWriteCore` skips it once `_closed`, 261-262) | yes: malformed chunking |

Setting `StatusCode` before `Abort()` (as the #172 hang-up plan does) changes only the first row.

### 4.3 A body read pending at `Abort()` (INFERRED from SOURCE)

- `Abort()` never disposes the request stream, so `HttpRequestStream.Managed.cs:188-189` (`if (_closed) return 0;`)
  does not apply; `EndRead` calls `_stream.EndRead` (194).
- `NetworkStream.EndRead` (`SK/NetworkStream.cs:468-483`) wraps the socket's failure in an `IOException`
  (`net_io_readfailure`, 481), and `HttpRequestStream.Managed.cs:196-199` unwraps only `ArgumentException` and
  `InvalidOperationException`, so the awaiting `ReadAsync` gets `IOException` (inner `SocketException`,
  OperationAborted: `SK/SocketAsyncContext.Unix.cs:1213-1245, 1324-1340` cancel every queued operation when the handle
  closes).
- But `Shutdown(Both)` (532) runs before `Close()` (539), and on Linux a shutdown wakes a pending receive with EOF. If
  that completion is processed first, the read returns 0:
  - Content-Length body: `HttpListenerException(400)` (`HttpRequestStream.Managed.cs:201-206`);
  - chunked body: `ChunkedInputStream.OnRead` treats 0 as the end (`HL/M/ChunkedInputStream.cs:122-127`) and the read
    completes normally with what arrived, then 0: no exception (#135680).
  - If instead the abort is seen, a chunked read goes through `OnRead`'s catch (145-149: `SendError(e.Message, 400)`,
    harmless once disposed) and `EndRead` throws `HttpListenerException(400, net_io_operation_aborted)` (170-171).
- So after a timer's `Abort()`, the read may throw `IOException`, may throw `HttpListenerException`, or (chunked) may
  end as if complete. Which one wins is timing; not measured.

### 4.4 A write pending at `Abort()` (INFERRED from SOURCE)

- `Dispose` sets `HttpResponseStream._closed = true` (`HL/HttpResponseStream.cs:86`) **before** the socket is closed
  (`HttpConnection.cs:508` precedes 528-540).
- The socket close aborts the pending `NetworkStream.BeginWrite` (`SK/NetworkStream.cs:499-525`); its callback runs
  `FromAsyncTrim`'s end delegate (`CL/Stream.cs:767-771`) -> `HttpResponseStream.EndWrite` (`HL/HttpResponseStream.cs:66-73`)
  -> `EndWriteCore`, which returns at 261-262 (`if (_closed) return;`) without calling `_stream.EndWrite`.
- **So the awaiting `WriteAsync` completes successfully** although the data was not delivered, and the inner fault is
  never observed (compare #63247, an unobserved socket exception from the same listener). On Windows the same write
  throws (`HL/W/HttpResponseStreamAsyncResult.cs:192-195`, `HL/W/HttpResponseStream.Windows.cs:251-257`). A tap that
  decides "timed out" from the write's exception would miss it on Linux and macOS.
- `HttpResponseStream.Write` after close returns silently (`HL/HttpResponseStream.cs:49-52`), and `BeginWriteCore`
  completes at once when closed (`HL/M/HttpResponseStream.Managed.cs:206-213`), so later writes are silent no-ops
  too. (The Windows-only test expects `ObjectDisposedException` there: `HLtests/HttpListenerResponseTests.cs:169-181`.)

### 4.5 Where a managed write blocks a thread (INFERRED from SOURCE)

Synchronous socket writes inside the managed response stream:

- `BeginWriteCore` writes a chunk's size line with `InternalWrite` **before** starting the async data write
  (`HL/M/HttpResponseStream.Managed.cs:232-236`, then 240).
- `EndWriteCore` writes the chunk's trailing CRLF synchronously (268-270 and 278-280).
- `DisposeCore` writes the head and/or last chunk synchronously (74, 80), i.e. inside `Close()` and `Abort()`.
- `InternalWrite` is `_stream.Write` (134-155); `WriteCore` (sync `Write`) is all synchronous; and the managed request
  writes `100 Continue` synchronously while parsing the head (`HL/M/HttpListenerRequest.Managed.cs:201-205`).

On Unix a synchronous send waits behind any pending async send: `SocketAsyncContext.SendTo` tries the send only if
the queue is ready (`SK/SocketAsyncContext.Unix.cs:1956-1961`), otherwise queues it and blocks in
`PerformSyncOperation` (1973; 1372-1417, `e.Wait(timeout)` at 1393) with `timeout = handle.SendTimeout`
(`SK/SocketPal.Unix.cs:1194-1198`), which is -1, infinite, by default (`SK/SafeSocketHandle.Unix.cs:16`). With a caller
that has stopped reading:

- a **chunked** response's `OutputStream.WriteAsync` can block the calling thread inside `BeginWrite`, before any task
  is returned, so a timer racing a `Task` never gets the chance;
- `Response.Abort()` on that response blocks in `DisposeCore`'s last-chunk write, before reaching `Shutdown`/`Close`;
  a second `Abort()` returns at once (`Disposed`); only `HttpListener.Stop/Close/Abort` (which closes the socket from
  another thread, `HL/M/HttpListener.Managed.cs:213-243`) or the peer going away releases it;
- `Response.Close()` likewise blocks there, and then in `FlushInput` (section 1.6).

A **Content-Length** response written with `WriteAsync` is fully asynchronous: the first write sends head and body in
one `BeginWrite` (216-231, 240), later writes go straight to `BeginWrite`, and `DisposeCore` writes nothing once the
head is out. ProxyTap's answer is Content-Length with one `WriteAsync` (`read/proxytap.md`, "Content-Length, never
chunked"), so this hazard applies to it only if a future change makes it chunked; its synchronous `Write` in
`TryRespondBadGateway` is a sync send, bounded on Windows only by MinSendRate.

### 4.6 There is no public `HttpListenerContext.Abort()`

`HLref:42-52`: `HttpListenerContext` exposes `Request`, `Response`, `User` and `AcceptWebSocketAsync` only. The
per-request levers are `HttpListenerResponse.Abort()` and `Close()`; the rest are listener-wide. (SOURCE)

---

## 5. Prior art for defaults (question 5)

| Server | Setting | Default | What it bounds | On expiry | Source |
|---|---|---|---|---|---|
| Kestrel | `KeepAliveTimeout` | **130 s** | idle between requests | connection ends | `KestrelServerLimits.cs:35-37` ("Slightly more than SocketHttpHandler's old PooledConnectionIdleTimeout of 2 minutes", dotnet/runtime#52267), 170-187; [API doc](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.keepalivetimeout?view=aspnetcore-10.0) "Defaults to 130 seconds." |
| Kestrel | `RequestHeadersTimeout` | **30 s** | whole head (total) | **408** if a request had begun | `KestrelServerLimits.cs:39, 189-206`; `Internal/Http/Http1Connection.cs:773-777` ("send a 408 response"); `KestrelBadHttpRequestException.cs:70-71` |
| Kestrel | `MinRequestBodyDataRate` | **240 bytes/s, 5 s grace** | average body rate over time spent reading | **408** (`RequestBodyTimeout`) if the response has not started | `KestrelServerLimits.cs:349-360` ("Matches the default IIS minBytesPerSecond"); `Http1Connection.cs:158-165`; `KestrelBadHttpRequestException.cs:73-74`; `Internal/Infrastructure/TimeoutControl.cs:114-123` |
| Kestrel | `MinResponseDataRate` | **240 bytes/s, 5 s grace** | each write: timer = max(grace, bytes / rate) | connection **aborted** | `KestrelServerLimits.cs:362-384` (remarks 371-380); `Internal/HttpConnection.cs:289-291` |
| Kestrel | `MaxRequestBodySize` | 30,000,000 bytes | size | 413 | `KestrelServerLimits.cs:30` |
| YARP | `ForwarderRequestConfig.ActivityTimeout` | **100 s** | idle: reset after every successful read or write of request or response data, and on response headers | **504** before the response starts; after it starts, the client connection is **aborted** (HTTP/1.1) or the stream reset | `Forwarder/HttpForwarder.cs:28` (`DefaultTimeout = TimeSpan.FromSeconds(100)`), 130; `Forwarder/ForwarderRequestConfig.cs:21-27`; `Forwarder/StreamCopier.cs:84-85, 112-113`; 504 at `HttpForwarder.cs:688-692`; abort at 939-949, 967-980 ("so the client doesn't get the mistaken impression that the truncated response is complete") |
| nginx | `client_header_timeout` | **60 s** | whole head | **408** | [core module](https://nginx.org/en/docs/http/ngx_http_core_module.html#client_header_timeout) |
| nginx | `client_body_timeout` | **60 s** | "only for a period between two successive read operations, not for the transmission of the whole request body" | **408** | [core module](https://nginx.org/en/docs/http/ngx_http_core_module.html#client_body_timeout) |
| nginx | `send_timeout` | **60 s** | "only between two successive write operations, not for the transmission of the whole response" | "the connection is closed" | [core module](https://nginx.org/en/docs/http/ngx_http_core_module.html#send_timeout) |
| nginx | `keepalive_timeout` | 75 s | idle keep-alive | close | [core module](https://nginx.org/en/docs/http/ngx_http_core_module.html#keepalive_timeout) |
| nginx | `reset_timedout_connection` | off | | when on, timed-out connections close with RST (`SO_LINGER` 0); "timed out keep-alive connections are closed normally" | [core module](https://nginx.org/en/docs/http/ngx_http_core_module.html#reset_timedout_connection) |
| nginx | `proxy_read_timeout` / `proxy_send_timeout` | 60 s / 60 s | between two successive operations with the upstream | close | [proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_read_timeout) |
| Envoy | `stream_idle_timeout` (HCM) | **5 min** | no activity on the stream either way | **408** "if no upstream response header has been received", otherwise stream reset | [HCM proto](https://www.envoyproxy.io/docs/envoy/latest/api-v3/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto) (page shows 1.41.0-dev) |
| Envoy | `request_timeout` (HCM) | **disabled** | the entire request | | same page: "If not specified or set to 0, this timeout is disabled." |
| Envoy | `request_headers_timeout` (HCM) | **disabled** | headers only | | same page |
| Envoy | `common_http_protocol_options.idle_timeout` | 1 hour | connection with no active streams | close (drain on HTTP/2) | [timeouts FAQ](https://www.envoyproxy.io/docs/envoy/latest/faq/configuration/timeouts) |
| Envoy | route `timeout` | 15 s | upstream response, from end of downstream request | | timeouts FAQ |
| Envoy edge guidance | | `stream_idle_timeout: 300s`, `request_timeout: 300s`, route `timeout: 15s` and `idle_timeout: 15s`, `idle_timeout: 3600s`; each "must be disabled for long-lived and streaming requests" | | | [edge best practices](https://www.envoyproxy.io/docs/envoy/latest/configuration/best_practices/edge) |
| http.sys | see 3.2 | 2 min each, MinSendRate 150 bytes/s | EntityBody is idle-between-arrivals | connection timed out | DOC 3.2 |

Kestrel docs ([options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0),
updated 2026-10-08): "Kestrel checks every second if data is arriving at the specified rate in bytes/second. If the rate
drops below the minimum, the connection is timed out. The grace period is the amount of time Kestrel allows the client to
increase its send rate up to the minimum. ... A minimum rate also applies to the response." Also: none of these limits
is enforced while a debugger is attached ("Behavior with debugger attached"). YARP docs
([timeouts](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0)):
"Requests do not have any timeouts by default, other than the ActivityTimeout used to clean up idle requests", and
ActivityTimeout "always applies, even when a debugger is attached".

Patterns the defaults share: header limits are **totals** (Kestrel 30 s, nginx 60 s, http.sys 2 min); body and
response limits are **idle-between-operations** (nginx 60 s, YARP 100 s, Envoy 5 min, http.sys EntityBody 2 min) or a
**minimum rate** (Kestrel 240 bytes/s after 5 s; http.sys 150 bytes/s); an expired head or body before any response
gets **408**; an expired response write **closes/aborts** the connection, and YARP aborts precisely so a truncated
response is not taken for a whole one.

---

## 6. dotnet/runtime issues (question 6)

States as of 2026-10-14 (`gh issue view`).

| Issue | State | Title | Relevance |
|---|---|---|---|
| [#39183](https://github.com/dotnet/runtime/issues/39183) | **open**, milestone Future, labels os-linux, os-macos | HttpListener (managed) does not monitor client disconnection (FIN) | nothing reads between head and app reads; davidfowl (2021-03-03): the fix "is non trivial because HttpListener would need to implement a buffering layer for request body reading"; last activity 2025-09-03 ("Bump") |
| [#88196](https://github.com/dotnet/runtime/issues/88196) | **open**, milestone Future, labels os-macos | HttpListenerResponse.Abort does not abort and sends a response on Mac OS | section 4.2; a comment traces it to `Close(force: true)` -> `GetResponseStream()?.Close()` and says it is every non-Windows platform; wfurt: "I would consider all cancellation and abort operation as best effort" |
| [#63236](https://github.com/dotnet/runtime/issues/63236) | **open** | Managed HttpListener does not shut down socket properly before closing | the keep-alive timer's `CloseSocket` resets a response still draining (section 1.5); karelz (2022-01-04) would take a Shutdown-before-Close PR |
| [#63247](https://github.com/dotnet/runtime/issues/63247) | closed 2022-02-18 | Managed HttpListener lets socket close exceptions on kept-alive connections bubble into finalizer thread | confirms the hard-coded keep-alive timeout ("9/15 seconds (hardcoded timeout value in HttpListener)") |
| [#21808](https://github.com/dotnet/runtime/issues/21808) | **open** (2017) | [HttpListener] [Managed] HttpListenerResponse should align with Windows behaviour for Close(byte[], bool) | its unchecked item `Abort_Invoke_ForciblyTerminatesConnection` is the Windows-only test in 3.4 |
| [#135680](https://github.com/dotnet/runtime/issues/135680) | **open** (2026-10-13) | HttpListener (managed): a chunked request body cut short by the client reads as complete | section 4.3; filed from Kronikol's #172 work |
| [#123463](https://github.com/dotnet/runtime/issues/123463) | closed 2026-01-27 | [API Proposal]: HttpListener.GetContextAsync() shall add CancellationToken support | maintainer: "HttpListener is in maintenance mode ... unlikely it will see new API additions"; `WaitAsync` "will not stop the underlying operation" |
| [#63941](https://github.com/dotnet/runtime/issues/63941) | closed | Deprecate HttpListener | referenced from #123463 as the reason it was not obsoleted |
| [#19752](https://github.com/dotnet/runtime/issues/19752) | **open** (2016) | Implement https connection support for the managed HttpListener | section 1.7 |
| [#21897](https://github.com/dotnet/runtime/issues/21897) | closed | [HttpListener] HttpListenerResponse.Close(byte[], bool) does not properly shutdown all the time | the older form of #63236 |
| [#133665](https://github.com/dotnet/runtime/pull/133665) | open PR (2026-09-11) | Reject bare CR in managed HttpListener request parsing | the managed parser still takes small fixes |
| [dotnet/corefx#19128](https://github.com/dotnet/corefx/pull/19128) | merged 2017-04-29 | Remove a few PNSEs from HttpListener | origin of the managed `TimeoutManager` behaviour (section 2) |
| [mono/mono#12652](https://github.com/mono/mono/issues/12652) | open | HttpListener.TimeoutManager throws System.NotImplementedException | Mono's equivalent |

**Not found:** no dotnet/runtime issue asks for `TimeoutManager` on Unix, a body/send timer on the managed listener,
or reports slowloris against it. Searches run (GitHub search API, repo dotnet/runtime): `HttpListener TimeoutManager`,
`HttpListener timeout`, `HttpListener slowloris` (0), `HttpListener "slow client"`, `HttpListener idle`,
`HttpListener "denial of service"` (0), `HttpListener DoS`, `HttpListener EntityBody`, `HttpListener HeaderWait`,
`HttpListener DrainEntityBody`, `HttpListener IdleConnection`, `"managed HttpListener" timeout`,
`HttpListener "PlatformNotSupportedException" timeout`, `HttpListener hang(s)`, `HttpListener Abort`,
`HttpListener cancellation`, `HttpListener CancellationToken`, `HttpListener "15 seconds"`, `HttpListener 90 seconds`.
The hits were test hangs, the issues above, and unrelated items.

---

## 7. What follows for the plan (my reading, for the plan author to weigh)

- On the managed listener the tap's limits have to be **its own timers that call `Response.Abort()`**, and the tap
  has to record the outcome itself: the interrupted `WriteAsync` may complete normally (4.4) and the interrupted read
  may end normally on a chunked body (4.3).
- **Set the status before `Abort()`** whenever the head may not be out (4.2 row 1); 408 for a caller too slow to send,
  since it also adds `Connection: close` (`HttpListenerResponse.Managed.cs:220-223`). Once a Content-Length head is
  out, `Abort()` leaves a detectably short body; once a chunked head is out, it does not (4.2).
- **A stall inside the head cannot be bounded by the tap** (1.4); it is bounded only by the 32 KiB cap and disposal,
  and disposal sends such callers a 200 (1.8). Options outside the public API (reflection on `HttpConnection._socket`,
  or a front `TcpListener`) are the only ways to do better on Linux/macOS.
- On Windows, http.sys already times out a caller idle for 2 minutes in a body or reading below 150 bytes/s, and the
  tap could set `TimeoutManager.EntityBody`/`MinSendBytesPerSecond` there (Windows-only setters, URL-group scope,
  and the first request's head always uses the 2-minute defaults). The same values cannot be set on the managed side
  (PNSE), so a cross-platform default needs the tap's own timers on both, or two behaviours.
- Idle-between-operations limits (nginx, YARP, Envoy, http.sys EntityBody) match how a tap observes its I/O (each
  completed read or write resets a timer) better than Kestrel's averaged rate, which needs byte accounting per second.

---

## 8. INFERRED claims to probe before the plan relies on them

1. Managed `Abort()` during a stalled **chunked** `WriteAsync` blocks the aborting thread (4.5), and `WriteAsync` itself
   can block synchronously in `BeginWrite`.
2. Managed `Abort()` during a pending Content-Length `WriteAsync`: the write task completes without an exception (4.4).
3. Managed `Abort()` during a pending body `ReadAsync`: how often `IOException`, `HttpListenerException(400)`, or a
   clean end (chunked) occurs on Linux and on macOS (4.3).
4. Listener disposal sends a 200 head to a connection stalled mid-head and to an idle kept-alive connection (1.8).
5. The 90 s first-read timer (and the 15 s keep-alive one) closes with a RST and no response, on Linux and on macOS
   (1.5).
6. http.sys: what the caller sees when EntityBody, HeaderWait and MinSendRate fire (FIN or RST, any 408), and what
   exception the application's pending read/write gets (3.3).
