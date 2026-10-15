# External facts for TAP_CALLER_TIME_LIMITS_PLAN.md (#173), checked 2026-10-15

Read-only sweep. `gh` was used for reads only. Source links are pinned to a tag or commit. "Plan" means
`C:/Code/Kronikol-173/plans/TAP_CALLER_TIME_LIMITS_PLAN.md`; "notes" means its harness `source/` files.

## Contradictions and changes for the plan (read first)

1. **R3's `HalfClosedTimeout` is a total; every half-close bound the plan cites is an inactivity limit, socat's
   included** (§7.1). socat's `-t` re-arms its 0.5 s on every pass of the transfer loop (1.7.4.3, 1.7.4.4, 1.8.1.3
   source), so data in the other direction keeps a half-closed pair open; HAProxy's `client-fin` is "the inactivity
   timeout ... for half-closed connections"; nginx stream, Envoy and mitmproxy are idle timers. As written (§4.5 item 1,
   "closed if the other has not ended within this long") R3 would cut a half-closed pair that is still streaming at
   100 s, against the plan's own §4.1 item 1 ("a stall, not a duration"). Suggest counting only time with no bytes
   moved in the remaining direction, and rewording §2.4 and §4.5's socat line ("closes 0.5 s after the first EOF").
2. **Early answers need a staged close** (§6). R0 item 3 (401 before the body), R0 item 4 (400 or 413 "and a close")
   and R2 item 4 (408 "then closes") say nothing about how the socket closes. Closing with unread request bytes draws a
   RST that can erase the answer (RFC 9112 §9.6, RFC 9293 §3.6.1; Fielding's first example is a POST without
   authorization). nginx (5 s between reads, 30 s in all) and Apache (2 s, 30 s) half-close and discard, Go half-closes
   and waits 500 ms, and Kestrel drains up to 5 s, though not for its own 4xx. Today's reader drains a whole over-limit body for
   this very reason (HttpRequestReader.cs L98). Suggest shut down send, discard to EOF or a bound, then close, and a
   fact that an exporter mid-upload receives the 401 or 413, not a reset.
3. **"Every one that sends anything on a stalled head or body sends 408" (§2.4) is wrong for HAProxy:** a stalled body
   in its default streaming mode gets 400 (`http_ana.c` `return_cli_abort`, verified). 408 for a body needs
   `option http-buffer-request`. The design's 408 still has Kestrel, YARP and Envoy behind it; reword only.
4. **Cancelled socket reads (§4.2 item 5, §4.4 item 3, `prior-art.md` §8): close-after-cancel stays right, but the
   framing is dated** (§5). No document promises anything, yet since .NET 9 the runtime keeps a socket usable after a
   cancelled read on purpose and pins it in a test (PR #99181, `NetworkStream_ReadAsyncTimeout_RemainUseable`); on the
   taps' net8.0 target a cancel marks the socket disconnected; a cancelled write is undefined on every version. On
   Windows, the only "indeterminate state ... should be closed" text from Microsoft I found is for the blocking
   `SO_RCVTIMEO`/`SO_SNDTIMEO` timeouts, not for `CancelIoEx`.
5. **The 120 s keep-alive (§4.4 item 1, Q2) holds up, for a reason the plan does not give** (§4). It is above every
   default client idle limit whose client would not survive the race (.NET 60-75 s, .NET Framework 100 s, Go 90 s, JDK
   20+ 30 s), and the clients that keep connections longer (OkHttp 5 min, urllib3 and Node none, JDK 11-19 1200 s)
   check for the FIN and retry. Do not lower it to the Collector's 60 s: .NET would then meet the race and does not
   retry a POST. New prior art worth citing: the Collector's OTLP receiver closes idle connections at 60 s and, by
   default, bounds no stalled head, body or answer (its factory zeroes both timeouts "For backward compatibility").
6. **No change from the runtime side** (§2, §3). `release/10.0` is still 591ede8; 10.0.13 ships HttpListener source
   blob-identical to 10.0.11 and 10.0.12 (VMR tags); the timer lines (L63, L182-195, L257, L320; main L325) hold; none of
   the five dotnet/runtime issues has a fix, a PR or a backport. #123463 was closed by its reporter, not a maintainer.
   Nothing new on #173 or #174.
7. Notes-level nits (§7.1): nginx's header timeout calls `ngx_http_close_request` directly, not
   `ngx_http_terminate_request` (same outcome); Envoy's code answers 504, not 408, once the request is complete; two
   Kestrel line ranges.

---

## 1. Kronikol #173 and #174 since filing

Nothing has been added to either issue.

| | #173 | #174 |
|---|---|---|
| Created / updated | 2026-10-13T15:55:33Z / same instant | 2026-10-13T15:55:36Z / same instant |
| Comments | 0 | 0 |
| Labels, milestone, assignees | none | none |
| Body edits (GraphQL `userContentEdits`, `lastEditedAt`) | 0, null | 0, null |
| Title | unchanged ("Taps: no time limit on the caller's side") | unchanged ("TcpTap: a failed upstream connect is not counted") |
| Reactions | none | none |

The only timeline events on both, all on the day of filing: referenced by commit
[3607253a](https://github.com/lemonlion/Kronikol/commit/3607253a93c918c642c516c213700a113be7f7ca) (Release 4.19.2,
16:03:35Z), cross-referenced from [#172](https://github.com/lemonlion/Kronikol/issues/172) (16:43:10Z), referenced by
commit [90d18988](https://github.com/lemonlion/Kronikol/commit/90d1898882407f68c8fff1725c2b3690e7b5de52) (plan log,
17:48:55Z). Links: [#173](https://github.com/lemonlion/Kronikol/issues/173),
[#174](https://github.com/lemonlion/Kronikol/issues/174).

## 2. dotnet/runtime issues

None is fixed and none has a backport. No pull request in dotnet/runtime references any of them (timelines and PR
search).

| Issue | State | Milestone | Labels | Latest relevant comment |
|---|---|---|---|---|
| [#88196](https://github.com/dotnet/runtime/issues/88196) `HttpListenerResponse.Abort` sends a response on macOS | open | Future (wfurt, 2023-07-03) | area-System.Net.Http, os-macos | [wfurt, 2023-07-04](https://github.com/dotnet/runtime/issues/88196#issuecomment-1620535701): "I guess the question is if this is macOS specific or Unix specific ... In general I would consider all cancellation and abort operation as best effort e.g. the observed behavior may vary." The trace to `Close(force: true)` and "probably the problem not in MacOS only but for all platforms except Windows" is the reporter's ([AndirNotes, 2023-07-04](https://github.com/dotnet/runtime/issues/88196#issuecomment-1619471138)), not a maintainer's. Since then only cross-references (MihuBot triage 2026-01-24, a Kronikol commit 2026-10-13) |
| [#135680](https://github.com/dotnet/runtime/issues/135680) managed: a chunked body cut short reads as complete | open (filed 2026-10-13T15:55:44Z) | Future (wfurt, 2026-10-13T16:00:34Z, who also removed `untriaged`) | area-System.Net.Http | none from a person; the only comment is the area bot's ([2026-10-13](https://github.com/dotnet/runtime/issues/135680#issuecomment-6149629869)) |
| [#63236](https://github.com/dotnet/runtime/issues/63236) managed listener does not shut down the socket before closing | open | Future (karelz, 2022-01-04) | bug, area-System.Net | [karelz, 2022-01-04](https://github.com/dotnet/runtime/issues/63236#issuecomment-1005129615): "If the fix is as simple as adding Shutdown before Close call on the socket, we would take such change ... we would welcome your PR." No PR followed; only MihuBot triage cross-references (2025-12-19, 2026-07-13) |
| [#123463](https://github.com/dotnet/runtime/issues/123463) `GetContextAsync(CancellationToken)` | closed 2026-01-27 **by its reporter** (wsy), reason "completed" though nothing was added; locked by a bot 2026-02-27 | Future (ManickaP, 2026-01-22) | api-suggestion, area-System.Net.Http | [ManickaP, 2026-01-22](https://github.com/dotnet/runtime/issues/123463#issuecomment-3783225716): "`await contextTask.WaitAsync(cancellationToken);` will not stop the underlying operation ... `HttpListener` is in maintenance mode (and has been for a while) so it's unlikely it will see new API additions in foreseeable future. I strongly recommend using ASP.NET servers (e.g. Kestrel) instead." [Same day](https://github.com/dotnet/runtime/issues/123463#issuecomment-3784183024): "That's also the reason why we haven't obsoleted `HttpListener` (#63941) despite wanting to." |
| [#39183](https://github.com/dotnet/runtime/issues/39183) managed listener does not monitor client FIN | open | Future (scalablecory, 2020-07-13) | area-System.Net.Http, os-linux, os-macos | [davidfowl, 2021-03-03](https://github.com/dotnet/runtime/issues/39183#issuecomment-789516840): "The change here is non trivial because HttpListener would need to implement a buffering layer for request body reading ... You need them to be independent in order to detect FINs." Last comment: [softworkz "Bump", 2025-09-03](https://github.com/dotnet/runtime/issues/39183#issuecomment-3247234303). Since then cross-references only (MihuBot 2026-01-21, a third-party repo 2026-09-17, Kronikol and #135680 2026-10-13) |

The code agrees on `main` at ce202f8 (2026-10-15): `CloseSocket` still closes with no `Shutdown` (#63236);
`Close(bool force)` still begins with `GetResponseStream()?.Close()` (#88196); `ChunkedInputStream.OnRead` still sets
`_no_more_data = true` and completes on a 0-byte read (#135680, L122-127).

The plan's characterisations (notes `dotnet-listener-timers.md` §6, plan §4.3 item 7, Q7) hold. One precision: #123463
was closed by its reporter, not by a maintainer and not by a change.

## 3. Commits touching `src/libraries/System.Net.HttpListener`

### 3.1 `release/10.0` after 591ede8

**None.** On 2026-10-15 the head of `release/10.0` is still
[591ede8](https://github.com/dotnet/runtime/commit/591ede817344fa749dd1af56d7e73b356da3e71a) (2026-10-12T23:01:10Z);
`compare/591ede8...release/10.0` is "identical", 0 commits. There is no `release/10.0-staging` branch.

### 3.2 `main` since 2026-08-01

Seven commits ([query](https://github.com/dotnet/runtime/commits/main/src/libraries/System.Net.HttpListener)); none
touches `Managed/HttpConnection.cs`, a stream's read or write path, `Abort` or `Close` (f047cd12 changes only how
`ChunkStream` parses a chunk-size line):

| Commit | Date | PR | Files (under `System.Net.HttpListener/`) |
|---|---|---|---|
| [eb856275](https://github.com/dotnet/runtime/commit/eb85627524919e0dddab6be8cfdefa93a7167f6f) | 2026-10-07 | Improve NTLM test failure diagnostics (#135322) | tests only |
| [f7a13a8c](https://github.com/dotnet/runtime/commit/f7a13a8cc60db803411f2b84b13ab978665a472a) | 2026-09-30 | Use also extended policy for looking up auth context (#133195) | `Windows/HttpListener.Windows.cs` (auth), tests |
| [f047cd12](https://github.com/dotnet/runtime/commit/f047cd1205cf7fe85a3c32749169c6c163d6c79f) | 2026-09-02 | Reject whitespace in HttpListener chunk sizes (#132747) | `Managed/ChunkStream.cs` (chunk-size parse: no more skipping after a space, `uint` hex parse), tests |
| [6550ccf7](https://github.com/dotnet/runtime/commit/6550ccf7827cdc363035f8927f68a34edb01bf22) | 2026-08-18 | Fix hang in InvalidClientRequestTests ... (#132321) | tests only (backported as #132782) |
| [046e4ec1](https://github.com/dotnet/runtime/commit/046e4ec11e218a4dcde624ce68e1218f7917f3cf) | 2026-08-14 | Fix lock inversion in Windows WebSocket impl (#132314) | `Windows/WebSockets/WebSocketBase.cs`, tests |
| [761d9db4](https://github.com/dotnet/runtime/commit/761d9db478acd73b86d46286460da1fdc949ac5a) | 2026-08-14 | Mirror Windows behavior for managed HttpListener Content-Length (#130910) | `Managed/HttpListenerRequest.Managed.cs`, tests (already in 10.0, see 3.3) |
| [6bb40604](https://github.com/dotnet/runtime/commit/6bb40604e2127335269df8d71b60e23ed00a1a23) | 2026-08-14 | Limit header name in HttpListenerRequest (#132163) | `Managed/HttpListenerRequest.Managed.cs` (header name no longer trimmed), tests |

Main also differs from `release/10.0` in a file the notes do not list:
`Managed/ListenerAsyncResult.Managed.cs`, where a pending `GetContext` ended by `Stop`/`Abort`/`Close` now throws
`HttpListenerException` 995 (`OperationAborted`) instead of 500
([e4c1aa01](https://github.com/dotnet/runtime/commit/e4c1aa01ee), #123360, 2026-03-04; main only, so .NET 11). Not a
timer, and harmless to `ProxyTap`, which catches `HttpListenerException` by type, never by `ErrorCode` (ProxyTap.cs
L225, L855, L898, L982 at `3240c983`).

### 3.3 Did .NET 10.0.13 (2026-10-13) change `HttpListener`? No.

- .NET 10 is built from the VMR (dotnet/dotnet). Its `src/runtime/src/libraries/System.Net.HttpListener/src` is
  **blob-for-blob identical** at tags [v10.0.11](https://github.com/dotnet/dotnet/tree/v10.0.11) (2026-07-23),
  [v10.0.12](https://github.com/dotnet/dotnet/tree/v10.0.12) (2026-08-22) and
  [v10.0.13](https://github.com/dotnet/dotnet/tree/v10.0.13) (2026-10-04), and at the head of the VMR's
  `release/10.0.1xx` ([312ac99b](https://github.com/dotnet/dotnet/commit/312ac99b87975fb3b93b6f65806c23b56e81073b),
  2026-10-14). It is also identical to dotnet/runtime `release/10.0` at 591ede8. So the plan's source reading is
  exactly the code the measured 10.0.11 (Windows) and 10.0.12 (Linux) runtimes shipped, and 10.0.13 ships it unchanged.
- [10.0.13 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.13/10.0.13.md): 44 CVEs,
  listed by title only ("Denial of Service", "Remote Code Execution", ...), no component named; no mention of
  HttpListener. HttpListener is in the shared framework, so the package table does not list it either.
- Trap for anyone repeating the check: **dotnet/runtime's own tags lag the VMR for internal fixes.**
  `compare/v10.0.12...v10.0.13` in dotnet/runtime shows `Managed/HttpListenerRequest.Managed.cs` changed (the strict
  Content-Length parse: `long.TryParse(val, NumberStyles.None, ...)`, values past `long.MaxValue` rejected where they
  used to be read as 0). That change entered the VMR as internal PR 62812 "[internal/release/10.0.1xx] Reject all
  invalid content lengths in HttpListenerRequest.Managed" on 2026-07-13
  ([fc6b2f39](https://github.com/dotnet/dotnet/commit/fc6b2f39a6)), shipped first in **10.0.11** (absent at VMR v10.0.10),
  and reached dotnet/runtime `release/10.0` only by codeflow on 2026-08-26
  ([#132653](https://github.com/dotnet/runtime/commit/d32c8c4ba09e1143dc06fbe2539f0794ed00b628)). So both measured runtimes
  already parsed Content-Length strictly.
- 10.0.13 did change System.Net.Sockets, in `Socket.cs` and `SocketAsyncEventArgs.cs` only: an `accept()` that succeeds
  with an empty remote address on macOS (a peer reset before accept) no longer throws
  ("[release/10.0] Guard against empty remote address on macOS accept (#133567)",
  [5e9f5ef8](https://github.com/dotnet/runtime/commit/5e9f5ef85c), backport of #131869). Nothing about shutdown, close
  or cancellation; it concerns #174's accept loops, not this plan. CoreLib `Stream.cs` is the same blob at 10.0.11,
  10.0.12 and 10.0.13.

### 3.4 The managed timer lines at the `release/10.0` head

Unchanged, since the head is 591ede8. Verified at
[591ede8 `Managed/HttpConnection.cs`](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/System.Net.HttpListener/src/System/Net/Managed/HttpConnection.cs):
L63 `private int _timeout = 90000; // 90k ms for first request, 15k ms from then on`; L182-186 `OnTimeout` is
`CloseSocket(); Unbind();`; L193-195 `if (_reuses == 1) _timeout = 15000; _timer.Change(_timeout, Timeout.Infinite);`;
L257 `_timer.Change(Timeout.Infinite, Timeout.Infinite);`; L320 `_stream.BeginRead(_buffer!, 0, BufferSize,
s_onreadCallback, this);` (no re-arm). On `main` at
[ce202f8](https://github.com/dotnet/runtime/blob/ce202f8f8183262f77bf094a5860390f3af92a2d/src/libraries/System.Net.HttpListener/src/System/Net/Managed/HttpConnection.cs)
(2026-10-15) the same lines are L187, L198-200, L262 and L325: +5 from the TLS comment added by #128084 (2026-05-13),
plus `secure == false` written `!secure` at L80. The plan's "line 320 at 591ede8, line 325 on main" holds.

## 4. How OTLP/HTTP exporters reuse connections

The question: `OtlpTap` (§4.4 item 1) would close a kept-alive connection idle for 120 s, with a FIN and no bytes. With
default settings **no exporter below should lose an export to that**: the ones that would not recover from the race
close their idle connections first, and the ones that keep connections longer check for the FIN before reuse and retry.
Tags read: opentelemetry-dotnet `core-1.19.1`, dotnet/runtime `v10.0.13`, opentelemetry-java `v1.67.0`, okhttp
`parent-5.5.0`, openjdk `jdk-27-ga`, opentelemetry-go `v1.47.0`, go `go1.27.2`, opentelemetry-python `v1.45.1`,
urllib3 `2.8.0`, requests `v2.34.2`, opentelemetry-js `experimental/v0.223.0`, node `v24.22.0`. Lines marked (v) were
re-read by me; the rest are the research agent's, with its links.

### 4.1 One line each

| SDK | (i) Client idle limit vs 120 s | (ii) Notices the FIN before reuse? | (iii) Retries a POST that fails on a reused connection? |
|---|---|---|---|
| **.NET** (`HttpClient` over `SocketsHttpHandler`) | **60 s**, scavenged every 15 s, so gone by 60-75 s: client closes first (v) | yes: sync `Socket.Poll`, async read-ahead | **no**: handler retries only a request with no body or `Expect: 100-continue` (v); exporter retry **off** by default (v) |
| .NET Framework (net462 target) | 100 s (`MaxServicePointIdleTime`): client closes first | UNCONFIRMED | UNCONFIRMED |
| **Java, OkHttp** (default sender) | **5 min**: server closes first (v) | yes, for any non-GET on a connection idle 10 s or more: a 1 ms read (v) | **yes**: OkHttp's `retryOnConnectionFailure` (default true) covers "Stale pooled connections"; body not one-shot |
| Java, JDK `HttpClient` sender | 30 s on JDK 20+ (v), 1200 s on JDK 11-19 | yes (a cleanup trigger on pooled connections) | JDK retries only GET/HEAD; the OTel sender's own retry (on by default) retries the IOException |
| **Go** `otlptracehttp` | **90 s** (`IdleConnTimeout`): client closes first (v) | yes: background `readLoop` `Peek(1)` turns EOF into `errServerClosedIdle` | **no** once body bytes are written (POST not replayable) (v); exporter retries only `urlErr.Temporary()` (v) |
| **Python** (requests/urllib3) | none: server closes first | yes: `is_connection_dropped` (zero-timeout `wait_for_read`) at checkout | requests: `Retry(0, read=False)`; the exporter retries a connection error once at once ("These usually indicate a stale pooled connection") (v), then with backoff |
| **JS** (Node `http.Agent({ keepAlive: true })`) | none: server closes first (v) | yes: sockets are not half-open, so a FIN closes the socket and it leaves the pool | Node does not; the exporter's retrying transport retries `ECONNRESET`, `EPIPE` and others (5 attempts) |

Key sources:

- .NET: `DefaultPooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)`
  ([`HttpHandlerDefaults.cs` L29](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/Common/src/System/Net/Http/HttpHandlerDefaults.cs#L29));
  "1 minute in .NET 6 and later versions; ... 2 minutes in .NET Core and .NET 5"
  ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.pooledconnectionidletimeout));
  idle age is checked only by the scavenger (`IsUsable`,
  [`HttpConnectionBase.cs` L221-233](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnectionBase.cs#L221-L233)),
  while checkout checks only lifetime and `PrepareForReuse`
  ([`HttpConnectionPool.Http1.cs` L35-55](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ConnectionPool/HttpConnectionPool.Http1.cs#L35-L55));
  retry only "If we haven't started sending the request body yet (or there is no request body)":
  `if (request.Content is null || allowExpect100ToContinue is not null) { _canRetry = true; }`
  ([`HttpConnection.cs` L650-660](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs#L650-L660)).
  The exporter's default client is `new HttpClient { MaxResponseContentBufferSize = ..., Timeout = timeout }`
  ([`OtlpExporterOptions.cs` L125-129](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs#L125-L129)),
  and `OnSubmitRequestFailure(...) => false` unless `OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY` is `in_memory` or `disk`
  ([`OtlpExporterTransmissionHandler.cs` L103](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/Transmission/OtlpExporterTransmissionHandler.cs#L103),
  [`ExperimentalOptions.cs` L30-38](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/ExperimentalOptions.cs#L30-L38)).
- Java: `constructor() : this(5, 5, TimeUnit.MINUTES)`
  ([`ConnectionPool.kt` L83](https://github.com/square/okhttp/blob/parent-5.5.0/okhttp/src/commonJvmAndroid/kotlin/okhttp3/ConnectionPool.kt#L83));
  `private val doExtensiveHealthChecks = request.method != "GET"`
  ([`RealRoutePlanner.kt` L55](https://github.com/square/okhttp/blob/parent-5.5.0/okhttp/src/commonJvmAndroid/kotlin/okhttp3/internal/connection/RealRoutePlanner.kt#L55));
  [`RetryAndFollowUpInterceptor.kt` L139-151](https://github.com/square/okhttp/blob/parent-5.5.0/okhttp/src/commonJvmAndroid/kotlin/okhttp3/internal/http/RetryAndFollowUpInterceptor.kt#L139-L151).
  JDK: `static final int DEFAULT_KEEP_ALIVE_TIMEOUT = 30;`
  ([`HttpClientImpl.java` L125-126](https://github.com/openjdk/jdk/blob/jdk-27-ga/src/java.net.http/share/classes/jdk/internal/net/http/HttpClientImpl.java#L125-L126)),
  1200 before JDK-8297030 ([42b60ed](https://github.com/openjdk/jdk/commit/42b60ed22c02663eb1377d1ce78a559fdbb4348d));
  [module docs](https://docs.oracle.com/en/java/javase/25/docs/api/java.net.http/module-summary.html).
- Go: the exporter's `ourTransport` with `IdleConnTimeout: 90 * time.Second`
  ([`client.go` L50-61](https://github.com/open-telemetry/opentelemetry-go/blob/v1.47.0/exporters/otlp/otlptrace/otlptracehttp/client.go#L50-L61));
  retry only `if errors.As(err, &urlErr) && urlErr.Temporary()` (L192-195). Go names this exact case
  ([`transport.go` L1086-1090](https://github.com/golang/go/blob/go1.27.2/src/net/http/transport.go#L1086-L1090)):
  "errServerClosedIdle is not seen by users for idempotent requests, but may be seen by a user if the server shuts down
  an idle connection and sends its FIN in flight with already-written POST body bytes from the client."
- Python: [`http.py` L161-175](https://github.com/open-telemetry/opentelemetry-python/blob/v1.45.1/exporter/opentelemetry-exporter-otlp-common/src/opentelemetry/exporter/otlp/common/http.py#L161-L175);
  urllib3 [`connectionpool.py` L293-296](https://github.com/urllib3/urllib3/blob/2.8.0/src/urllib3/connectionpool.py#L293-L296)
  ("Resetting dropped connection").
- JS: `agentFactory: httpAgentFactoryFromOptions({ keepAlive: true })`
  ([`otlp-node-http-configuration.ts` L87](https://github.com/open-telemetry/opentelemetry-js/blob/experimental/v0.223.0/experimental/packages/otlp-exporter-base/src/configuration/otlp-node-http-configuration.ts#L87));
  Node docs on [`request.reusedSocket`](https://nodejs.org/docs/latest-v24.x/api/http.html#requestreusedsocket): "if
  server closes connection at unfortunate time, client may run into a 'ECONNRESET' error."

### 4.2 Where an export can still fail, and the window

- Only off the defaults: a .NET exporter given an `HttpClient` whose `PooledConnectionIdleTimeout` is 120 s or more
  (batch dropped unless the experimental retry is on); a Go exporter given `WithHTTPClient` over a transport with no
  `IdleConnTimeout`; the JDK sender on JDK 11-19 with its retry policy removed.
- The window is the race RFC 9112 §9.5 describes: a request handed a connection between the server's FIN leaving
  (T = 120 s after the last answer) and the client processing it. On loopback that is a thread-pool or event-loop turn.
  A batch exporter needs a 120 s quiet spell to get there (span and log exporters default to 5 s and 1 s intervals,
  metrics 60 s; [spec](https://opentelemetry.io/docs/specs/otel/configuration/sdk-environment-variables/)).
- If `OtlpTap` closes with unread bytes, Linux sends a RST rather than a FIN; every client above treats a reset as it
  treats EOF, so the verdict holds (agent; Windows UNCONFIRMED).
- A `Keep-Alive: timeout=N` response header would make .NET stop reusing at N-1 s (v,
  [`HttpConnection.cs` L1349-1359](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs#L1349-L1359):
  "Some servers are very strict with closing the connection exactly at the timeout"); Node's agent ignores it without a
  timeout of its own; others not checked. Not needed at 120 s.
- Side note for `OtlpTap`'s parser, not this plan: the JDK sender keeps HTTP/2 as the preferred version, so each POST to
  an `http://` endpoint carries `Connection: Upgrade, HTTP2-Settings` and `Upgrade: h2c` (agent's reading of
  `ExchangeImpl.java`, not re-read). The tap must not read that `Connection` value as a close; today's reader only
  looks for `close` and `keep-alive` in it (HttpRequestReader.cs L74-80), so it does not.

### 4.3 What it means for the plan

The plan's 120 s (§4.4 item 1, Q2) is well placed, for a reason the plan does not give: it sits above every default
client idle limit whose client would not recover from the race (.NET 60-75 s, .NET Framework 100 s, Go 90 s, JDK 20+
30 s), so those clients always close first, and the clients that keep connections longer (OkHttp, urllib3, Node, JDK
11-19) check for the FIN and retry. Kestrel chose 130 s by the same logic ("Slightly more than SocketHttpHandler's old
PooledConnectionIdleTimeout of 2 minutes", `KestrelServerLimits.cs` L35-37). Do not lower it toward the Collector's 60 s
(below): a .NET exporter keeps a connection up to 75 s, so at 60 s it would be exposed to the race. The window is narrow
(a FIN that has already arrived is caught by `PrepareForReuse`), but .NET does not retry a POST that falls in it.

### 4.4 The reference receiver already closes idle connections at 60 s

Prior art the plan does not cite, for `OtlpTap`'s `KeepAliveTimeout` (§4.4 item 1, Q2):

- The OpenTelemetry Collector's HTTP server config (used by the OTLP receiver, and so by Jaeger v2 and other
  Collector-based receivers) defaults the keep-alive idle limit to **1 minute**, at the newest release
  [v0.163.0](https://github.com/open-telemetry/opentelemetry-collector/releases/tag/v0.163.0) (2026-10-14):
  [`confighttp/server.go` L45-69](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.163.0/config/confighttp/server.go#L45-L69)
  (`WriteTimeout: 30 * time.Second`, `ReadHeaderTimeout: 1 * time.Minute`, `IdleTimeout: 1 * time.Minute`) and
  [`generated_config.go` L186-190](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.163.0/config/confighttp/generated_config.go#L186-L190)
  (`KeepaliveServerConfig{ IdleTimeout: 1 * time.Minute }` for the new `keepalive` section). The
  [README](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.163.0/config/confighttp/README.md):
  "`idle_timeout`: maximum amount of time to wait for the next request when keep-alives are enabled. If zero, the value
  of `read_timeout` is used. If both are zero, there is no timeout. Default: `1m`".
- The OTLP receiver then turns the other two off:
  [`otlpreceiver/factory.go` L48-53](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.163.0/receiver/otlpreceiver/factory.go#L48-L53):
  `// For backward compatibility:` ... `httpCfg.WriteTimeout = 0`, `httpCfg.ReadHeaderTimeout = 0`. With `read_timeout`
  0 as well, the reference OTLP/HTTP receiver bounds **no** stalled head, body or answer by default, only an idle
  connection (60 s).
- So exporters already meet a server that closes an idle kept-alive connection, at half the plan's 120 s. RFC 9112
  [§9.5](https://www.rfc-editor.org/rfc/rfc9112#section-9.5) states the race any such limit has: "a client might have
  started to send a new request at the same time that the server has decided to close the "idle" connection. From the
  server's point of view, the connection is being closed while it was idle, but from the client's point of view, a
  request is in progress." [§9.3.1](https://www.rfc-editor.org/rfc/rfc9112#section-9.3.1): "Implementations ought to
  anticipate the need to recover from asynchronous close events", under the retry rules of RFC 9110
  [§9.2.2](https://www.rfc-editor.org/rfc/rfc9110#section-9.2.2): "A client SHOULD NOT automatically retry a request with
  a non-idempotent method unless it has some means to know that the request semantics are actually idempotent ... or
  some means to detect that the original request was never applied."

## 5. Is a socket usable after a cancelled receive?

### 5.1 Windows: `CancelIoEx` on an overlapped `WSARecv`

- **No Microsoft document found says a socket is in an indeterminate state after `CancelIoEx` on an overlapped receive.**
  `WSARecv` and `closesocket` (sdk-api sources) say nothing about `CancelIoEx`; `closesocket` only says closing cancels
  pending overlapped operations and that "An application should not assume that any outstanding I/O operations on a
  socket will all be guaranteed to completed when closesocket returns."
- What Microsoft does say, generically, on [CancelIoEx](https://learn.microsoft.com/en-us/windows/win32/fileio/cancelioex-func#remarks):
  "CancelIoEx cancels only outstanding I/O on the handle, it does not change the state of the handle; this means that
  you cannot rely on the state of the handle because you cannot know whether the operation was completed successfully
  or canceled." and "The operation completed normally. This can occur even if the operation was canceled, because the
  cancel request might not have been submitted in time to cancel the operation." On
  [Canceling Pending I/O Operations](https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations):
  "There is no guarantee that underlying drivers correctly support cancellation."
- The "indeterminate" sentence exists only for the **blocking** timeouts, on
  [SOL_SOCKET socket options](https://learn.microsoft.com/en-us/windows/win32/winsock/sol-socket-socket-options)
  (ms.date 2021-05-12): `SO_RCVTIMEO` "If a blocking receive call times out, the connection is in an indeterminate state
  and should be closed. If the socket is created using the WSASocket function, then the dwFlags parameter must have the
  WSA_FLAG_OVERLAPPED attribute set for the timeout to function properly." (`SO_SNDTIMEO` says the same for send.) The
  overlapped requirement suggests the timeout is itself a cancelled receive inside Winsock (INFERENCE), so this is the
  nearest thing to an answer, but it is not stated for `CancelIoEx`.
- The race is real: [bpo-33694, msg318875](https://bugs.python.org/msg318875) (vstinner, 2018-06-07): after
  `CancelIoEx()` "the IOCP loop still gets an event for the completion of the recv ... since the Python future is
  cancelled, the even is ignored and so 176 bytes are lost." A caller that treats a cancelled receive as consuming
  nothing must still take data from a completion that succeeded.
- Windows also cancels more than asked: "On Windows, canceling a TCP receive will end up canceling all TCP receives
  pending on that socket" ([corefx#36516](https://github.com/dotnet/corefx/pull/36516), already in the notes).

### 5.2 .NET (Windows and Unix)

- **Docs: nothing** on the socket's state after a cancelled `Socket.ReceiveAsync` or `NetworkStream.ReadAsync` (as the
  notes say).
- **But since .NET 9 the runtime keeps the socket usable on purpose, and pins it in a test.** PR
  [#99181 "make Socket useable after cancellation"](https://github.com/dotnet/runtime/pull/99181) (wfurt, merged
  2024-05-10, milestone 9.0.0) adds `SocketError.OperationAborted` to the errors that do not mark the socket
  disconnected; at v10.0.13 it is
  [`Socket.cs` L3932-3934](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3932-L3934).
  The PR: "I personally don't see reason why the cancellation (or timeout) should change status of the socket since
  they are really independent. And I don't see any risk that we would loose data." The cross-platform functional test
  [`NetworkStream_ReadAsyncTimeout_RemainUseable`](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/tests/FunctionalTests/NetworkStreamTest.cs#L604-L636)
  cancels two `ReadAsync` calls, then reads the peer's next byte and asserts `Connected` throughout.
- Maintainers' caveats:
  - [tmds (Unix sockets), 2024-03-02](https://github.com/dotnet/runtime/pull/99181#issuecomment-1974782646): "The caller
    of `ReadAsync` won't loose data and can get it on the next call. Note that a caller of `ReadAtLeastAsync` might loose
    the data that was already received when it was less than `minimumBytes`." and "for stream sockets, I think it would
    be unusual to cancel a read and then read again."
  - [wfurt, 2024-04-01](https://github.com/dotnet/runtime/pull/99181#issuecomment-2030766901), on a cancelled partial
    write (asked by stephentoub): "since it throws, I think the operation state is undefined. When `WriteAsync` is
    cancelled there is no way to know if data were written or not."
  - [antonfirsov, 2024-11-21](https://github.com/dotnet/runtime/issues/48477#issuecomment-2491535479): "The standard way
    to achieve this would be to `Close`/`Dispose` the socket ... usually it's not safe to eg. issue a new read after a
    cancelled one from the application POV, since the status of the cancelled operation is undefined (buffer
    sent/partially sent/not sent)."
  - [wfurt, 2022-05-16 on #69397](https://github.com/dotnet/runtime/issues/69397#issuecomment-1127974199): "It is tricky
    to ensure consistency as the cancelation can be triggered at different places. e.g. some bytes may be read from OS
    but not delivered to application."
- **Before .NET 9 (so on the taps' net8.0 target) a cancelled socket operation marks the socket disconnected**
  (`Connected` false; [#69397](https://github.com/dotnet/runtime/issues/69397), .NET 6 on Windows; tmds on #99181: "The
  current behavior prevents that" read-after-cancel). [wfurt, 2024-05-11](https://github.com/dotnet/runtime/issues/69397#issuecomment-2105968980):
  "The fix for making `Socket` useable after cancellation was merged. (it will be part of 9.0)." The taps build for
  net8.0, net9.0 and net10.0 (`Directory.Build.props` `MultiTargetFrameworks`), so behaviour differs by target.
- Unix mechanism (notes, `SocketAsyncContext.Unix.cs` L204-260): a cancelled queued receive is dequeued with no syscall,
  so nothing is consumed; Windows goes through `CancelIoEx` and its race above.
- .NET does not lose data in that race: a receive's `GetResult` returns `BytesTransferred` whenever the operation's
  `SocketError` is `Success`, whatever the token
  ([`Socket.Tasks.cs` L1243-1260 at v10.0.13](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L1243-L1260));
  only `OperationAborted` or `ConnectionAborted` with the token cancelled becomes `OperationCanceledException`
  (L1351-1362). So a read cut by a limit can still return bytes; the tap decides by its own state, as the plan already
  does for the listener.

**For the plan (§4.4 item 3, §4.2 item 5):** "No documentation promises a socket is usable after a cancelled operation"
is still literally true, and closing after a limit fires is still right. But it should not read as if .NET leaves it
undefined: since .NET 9 the runtime deliberately keeps the socket usable after a cancelled read and tests that, while on
net8.0 a cancel marks it disconnected, and a cancelled write is undefined on every version. Cite #99181 beside
corefx#36516.

## 6. Early answers with an unread request body

Quotes were checked against the primary text (RFC plain text, source at the tag named, the docs page) unless marked as
the agent's.

### 6.1 The RFCs

- [RFC 9112 §9.6](https://www.rfc-editor.org/rfc/rfc9112#section-9.6), ¶9: "If a server performs an immediate close of a
  TCP connection, there is a significant risk that the client will not be able to read the last HTTP response. If the
  server receives additional data from the client on a fully closed connection, such as another request sent by the
  client before receiving the server's response, the server's TCP stack will send a reset packet to the client;
  unfortunately, the reset packet might erase the client's unacknowledged input buffers before they can be read and
  interpreted by the client's HTTP parser."
- Same section, ¶10: "To avoid the TCP reset problem, servers typically close a connection in stages. First, the
  server performs a half-close by closing only the write side of the read/write connection. The server then continues
  to read from the connection until it receives a corresponding close by the client, or until the server is reasonably
  certain that its own TCP stack has received the client's acknowledgement of the packet(s) containing the server's
  last response. Finally, the server fully closes the connection."
- [RFC 9112 §9.3](https://www.rfc-editor.org/rfc/rfc9112#section-9.3): "A server MUST read the entire request message
  body or close the connection after sending its response; otherwise, the remaining data on a persistent connection
  would be misinterpreted as the next request."
- [RFC 9112 §9.5](https://www.rfc-editor.org/rfc/rfc9112#section-9.5): "A client or server that wishes to time out
  SHOULD issue a graceful close on the connection." and "A client sending a message body SHOULD monitor the network
  connection for an error response while it is transmitting the request. If the client sees a response that indicates
  the server does not wish to receive the message body and is closing the connection, the client SHOULD immediately
  cease transmitting the body and close its side of the connection."
- [RFC 9110 §10.1.1](https://www.rfc-editor.org/rfc/rfc9110#section-10.1.1): "A server that responds with a final status
  code before reading the entire request content SHOULD indicate whether it intends to close the connection (e.g., see
  Section 9.6 of [HTTP/1.1]) or continue reading the request content." Its `100-continue` example names exactly this
  plan's case: it "allows the origin server to immediately respond with an error message, such as 401 (Unauthorized)
  ... before the client starts filling the pipes with an unnecessary data transfer."
- [RFC 9110 §15.5.14](https://www.rfc-editor.org/rfc/rfc9110#section-15.5.14) (413): "The server MAY terminate the
  request, if the protocol version in use allows it; otherwise, the server MAY close the connection."
- Why the reset happens, [RFC 9293 §3.6.1](https://www.rfc-editor.org/rfc/rfc9293#section-3.6.1): "If such a host issues
  a CLOSE call while received data is still pending in the TCP connection, or if new data is received after CLOSE is
  called, its TCP implementation SHOULD send a RST to show that data was lost (SHLD-3)."
- The 401 case by name, Roy Fielding in Apache's retired
  [FIN_WAIT_2 appendix](https://httpd.apache.org/docs/2.0/misc/fin_wait_2.html#appendix) (from agent; not re-read): the
  first trigger listed is "sending POST or PUT data without proper authorization", and "the RST will flush the error
  message before the client application has a chance to see it."

### 6.2 Kestrel (aspnetcore `v10.0.13`)

- **It answers, then drains, even when it will close.**
  [`HttpProtocol.cs` L774-775](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L741-L783):
  "// Even for non-keep-alive requests, try to consume the entire body to avoid RSTs." then
  `if (!_connectionAborted && _requestRejectedException == null && !messageBody.IsEmpty) { await messageBody.ConsumeAsync(); }`.
- **The drain is capped at 5 s, then a RST.** `RequestBodyDrainTimeout = TimeSpan.FromSeconds(5)`
  ([`Constants.cs` L32](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Infrastructure/Constants.cs#L32)),
  armed in [`Http1MessageBody.cs` L84-118](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1MessageBody.cs#L84-L118);
  on expiry `HttpConnection` aborts, and the socket transport's abort is `_socket.Close(timeout: 0)` ("This forces an
  abortive close with linger time 0"), unless the `Microsoft.AspNetCore.Server.Kestrel.FinOnError` switch is set. A
  graceful end is `Shutdown(Both)` then `Dispose()` (`prior-art.md` §2; the agent found `SocketShutdown.Send` only in
  tests): Kestrel does not half-close and linger.
- [Request draining docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/request-draining):
  "Kestrel tries to drain the request body. Draining the request body means reading and discarding the data without
  processing it." "Draining has a timeout of five seconds, which isn't configurable. If all of the data specified by the
  Content-Length or Transfer-Encoding header hasn't been read before the timeout, the connection is closed." On
  `HttpContext.Abort`: "There's no guarantee that the client has read the response before the connection closes."
- **Kestrel's own rejections (400, 408, 413) skip the drain** (the `_requestRejectedException == null` condition; the
  comment at L741: "4XX responses are written by TryProduceInvalidRequestResponse during connection tear down"). For
  `MaxRequestBodySize` (default 30,000,000 bytes) a declared Content-Length over it is checked when the app first reads
  ([`Http1ContentLengthMessageBody.cs` L244-252](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/Internal/Http/Http1ContentLengthMessageBody.cs#L244-L252):
  `_context.DisableKeepAlive(...)` then `RequestBodyTooLarge`), so the 413 goes out on the first read with
  `Connection: close` and the connection is closed without draining (test
  [`MaxRequestBodySizeTests.cs` L22-63](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/test/InMemory.FunctionalTests/MaxRequestBodySizeTests.cs#L22-L63)).
  The docs do not say this; only the code and tests do.

### 6.3 http.sys

- [HTTP_TIMEOUT_LIMIT_INFO](https://learn.microsoft.com/en-us/windows/win32/api/http/ns-http-http_timeout_limit_info),
  `DrainEntityBody`: "The time, in seconds, allowed for the HTTP Server API to drain the entity body on a Keep-Alive
  connection. On a Keep-Alive connection, after the application has sent a response for a request and before the
  request entity body has completely arrived, the HTTP Server API starts draining the remainder of the entity body to
  reach another potentially pipelined request from the client. If the time to drain the remaining entity body exceeds
  the allowed period the connection is timed out." Default 2 minutes.
- [HttpSendHttpResponse](https://learn.microsoft.com/en-us/windows/win32/api/http/nf-http-httpsendhttpresponse),
  `HTTP_SEND_RESPONSE_FLAG_DISCONNECT`: "The network connection should be disconnected after sending this response,
  overriding any persistent connection features associated with the version of HTTP in use."
- UNCONFIRMED: what http.sys does with an unread body on a response that disconnects (drain, linger or reset). Microsoft
  documents draining only for keep-alive connections.

### 6.4 nginx

[ngx_http_core_module](https://nginx.org/en/docs/http/ngx_http_core_module.html), checked on the live page:

- [`lingering_close`](https://nginx.org/en/docs/http/ngx_http_core_module.html#lingering_close), default `on`: "Controls
  how nginx closes client connections. The default value "on" instructs nginx to wait for and process additional data
  from a client before fully closing a connection, but only if heuristics suggests that a client may be sending more
  data. The value "always" will cause nginx to unconditionally wait for and process additional client data. The value
  "off" tells nginx to never wait for more data and close the connection immediately. This behavior breaks the protocol
  and should not be used under normal circumstances."
- [`lingering_time`](https://nginx.org/en/docs/http/ngx_http_core_module.html#lingering_time), default `30s`: "When
  lingering_close is in effect, this directive specifies the maximum time during which nginx will process (read and
  ignore) additional data coming from a client. After that, the connection will be closed, even if there will be more
  data."
- [`lingering_timeout`](https://nginx.org/en/docs/http/ngx_http_core_module.html#lingering_timeout), default `5s`: "When
  lingering_close is in effect, this directive specifies the maximum waiting time for more client data to arrive. If
  data are not received during this time, the connection is closed. Otherwise, the data are read and ignored, and nginx
  starts waiting for more data again. The "wait-read-ignore" cycle is repeated, but no longer than specified by the
  lingering_time directive."
- From the agent's reading of `release-1.31.7` source (not re-read): the lingering close is a write-side shutdown then
  4 KiB reads thrown away ([`ngx_http_request.c` L3727-3855](https://github.com/nginx/nginx/blob/release-1.31.7/src/http/ngx_http_request.c#L3727-L3855));
  a declared body over `client_max_body_size` is answered 413 before any of it is read; 400, 413, 414, 500 and 501 turn
  keep-alive off, while a 401 keeps the connection and nginx discards the body.

### 6.5 Apache httpd

- [Performance tuning, "Lingering Close"](https://httpd.apache.org/docs/2.4/misc/perf-tuning.html#compiletime): "in
  order for an HTTP server to **reliably** implement the protocol, it needs to shut down each direction of the
  communication independently", with the pseudo-code `shutdown (s, 1)`, `alarm (30)`, `select (s for reading, 2 second
  timeout)`, toss what arrives, `close (s)`; defining `NO_LINGCLOSE` "is not recommended at all".
- [`server/connection.c` at 2.4.69](https://github.com/apache/httpd/blob/2.4.69/server/connection.c#L46-L68): "the
  client will not be able to see the last response if their TCP buffer is flushed by a RST packet from us, which is what
  the server's TCP stack will send if it receives any request data after closing the connection";
  `#define MAX_SECS_TO_LINGER 30` (L67) and `#define SECONDS_TO_LINGER 2` (L119), used by `ap_lingering_close`: read
  for at most 30 s, giving up after 2 s without data.

### 6.6 Go `net/http` (extra data point)

[`server.go` at go1.27.2](https://github.com/golang/go/blob/go1.27.2/src/net/http/server.go#L1815-L1866): reads at most
`maxPostHandlerReadBytes = 256 << 10` of an unread body to keep the connection (L1164); otherwise `Connection: close`,
`CloseWrite()`, then `rstAvoidanceDelay = 500 * time.Millisecond` (L1826) before closing.

### 6.7 What it means for the plan

R0 item 3 (401 before the body), R0 item 4 (400 or 413 "and a close") and R2 item 4 (408, "then closes") say what is
written and that the connection closes, not how it closes. An exporter that sends its head and body in one go will
usually have body bytes in flight or unread when the tap answers 401 or 413; an immediate close then draws a RST that
can erase the answer, so the exporter reports a connection reset rather than 401 or 413. This is the case Fielding's
appendix names first. The prior art is a staged close with bounds: shut down the send side, read and discard until EOF
or a limit, then close. The bounds in use: nginx 5 s between reads and 30 s in all; Apache 2 s and 30 s; Kestrel a 5 s
drain, then a RST, and none at all for its own 4xx rejections; Go at most 256 KiB read, or else a 500 ms pause after the
FIN. For a stalled body (408) little is unread, so the risk there is small; for 401 and 413 it is the normal case.

Today's reader already knows this: an over-limit body is drained in full, with the comment "Drain rather than buffer:
the connection stays clean, so the 413 actually reaches the caller." (`HttpRequestReader.cs` L96-100 at `3240c983`).
The plan's §8 notes the cost of that drain (a 2 GiB body holds the handler) and R0 moves 401 and the `int`-overflow 413
ahead of the body. A bounded discard after a send-side shutdown keeps the reason for the drain without its cost.

## 7. Spot-check of the plan's §2.4 numbers

### 7.1 Discrepancies

The HAProxy one was found by the research agent and re-read by me in source; the socat one was found by both of us
independently.

**HAProxy answers a stalled request body 400, not 408 (default streaming mode).** This breaks the plan's §2.4 sentence
"every one that sends anything on a stalled head or body sends 408". A stalled head is 408
([`mux_h1.c` L3945-3974](https://github.com/haproxy/haproxy/blob/v3.4.0/src/mux_h1.c#L3945-L3974), agent). A stalled
body runs on `timeout client` (`timeout http-request`: "By default, this timeout only applies to the header part of the
request", `configuration.txt` L14964); on expiry `stream.c` calls `sc_abort(s->scf)`
([L1708-1712](https://github.com/haproxy/haproxy/blob/v3.4.0/src/stream.c#L1708-L1712)), and the body forwarder
`http_request_forward_body` goes to `return_cli_abort`:
`s->flags |= ((req->flags & CF_READ_TIMEOUT) ? SF_ERR_CLITO : SF_ERR_CLICL); status = 400;`, then
`http_reply_and_close` sends the 400 unless a response status is already set
([`http_ana.c` L1076-1079, L1119-1131, L1171-1179](https://github.com/haproxy/haproxy/blob/v3.4.0/src/http_ana.c#L1119-L1131)).
408 for a body comes only with `option http-buffer-request` or `wait-for-body` (`abort_req: txn->status = 408;`,
[L4380-4381](https://github.com/haproxy/haproxy/blob/v3.4.0/src/http_ana.c#L4349-L4388)). The others fit the
sentence: Kestrel 408 head and body; YARP 408 for the client's body; Envoy 408 while the request is incomplete; nginx
and http.sys send nothing. The design choice of 408 is unaffected (Kestrel, YARP, Envoy, RFC 9110 §15.5.9); only the
word "every" is wrong. (`prior-art.md` §0 lists HAProxy only for the head, which is right.)

**socat's `-t` is not a fixed 0.5 s after the first EOF; per its source it is 0.5 s without activity.** The plan (§2.4
"closes 0.5 s after the first EOF", §4.5 item 1 "closes 0.5 s after the first EOF") and `prior-art.md` (§0 "waits 0.5 s
after the first EOF and then closes both sides") follow the man page, whose wording reads as a total:
[`-t`](http://www.dest-unreach.org/socat/doc/socat.html#option_t) "When one channel has reached EOF ..., the write part
of the other channel is shut down. Then, socat waits <timeout> [timeval] seconds before terminating. When during the
timeout interval the read part of that other channel gives EOF, socat terminates without awaiting the timeout. Default
is 0.5 seconds, 0 is valid." and the closing phase: "For a defined time socat continues to transfer data in the other
direction, but then closes all remaining channels and terminates."

The code does something else. In `socat.c` of
[1.7.4.4](http://www.dest-unreach.org/socat/download/socat-1.7.4.4.tar.gz) (the image testcontainers-dotnet 4.16.0
runs), [1.7.4.3](http://www.dest-unreach.org/socat/download/socat-1.7.4.3.tar.gz) (testcontainers-java 2.0.5, identical
block), 1.8.0.3 (agent) and [1.8.1.3](http://www.dest-unreach.org/socat/download/socat-1.8.1.3.tar.gz) (newest), every
pass of the transfer loop re-arms the wait before `poll` (browsable in a GitHub import of the tarballs:
[pexip/os-socat `upstream/1.7.4.4` socat.c L917-922](https://github.com/pexip/os-socat/blob/upstream/1.7.4.4/socat.c#L917-L922)):

```c
/* socat 1.7.4.4 socat.c L917-922 (1.8.1.3: L1074-1079) */
	 if (closing>=1) {
	    /* first eof already occurred, start end timer */
	    timeout = socat_opts.closwait;
	    to = &timeout;
	    closing = 2;
	 }
```

and the loop ends only when `poll` returns 0 with `closing` set (L995-1022: "poll timed out (no data within ...)", then
`if (closing) { break; }`) or when both directions are at EOF (the `while` at L857). So after the first EOF, data in the
other direction keeps flowing (yes), and each transfer restarts the 0.5 s; the phase ends after 0.5 s with no event
in the remaining direction (no data to read and no writability for pending data), or at that direction's EOF. It does
not end at 0.5 s regardless. SOURCE, not run; a two-minute probe (client half-closes, upstream streams for 3 s in
100 ms steps) would settle it if the plan leans on it.

Consequence for the plan: every half-close bound the plan cites is an inactivity limit. HAProxy:
[`timeout client-fin`](https://docs.haproxy.org/3.4/configuration.html#4.2-timeout%20client-fin) "Set the inactivity
timeout on the client side for half-closed connections. ... The inactivity timeout applies when the client is expected
to acknowledge or send data while one direction is already shut down" (not set by default; the manual's example in
`timeout tunnel` pairs `timeout client-fin 30s` with `timeout tunnel 1h`, both confirmed,
[`configuration.txt` v3.4.0 L14779-14807, L15153-15160](https://github.com/haproxy/haproxy/blob/v3.4.0/doc/configuration.txt#L14779-L14807));
nginx stream `proxy_timeout` "between two successive read or write operations"; Envoy's TCP idle timer; mitmproxy's
600 s; socat's 0.5 s as above. R3's `HalfClosedTimeout` as written ("the pair is closed if the other has not ended
within this long", §4.5 item 1) is a total from the first FIN, which none of them is, and which the plan's own rule
(§4.1 item 1, "a stall, not a duration") argues against: a pair whose client half-closed after its request while the
upstream still streams a long reply would be cut at 100 s mid-transfer. T1 and T2 are fixed the same way by a limit that
counts only time with no bytes moved in the remaining direction.

**Smaller ones, for the notes rather than the plan:**

- nginx's **header** timeout does not go through `ngx_http_terminate_request`, as `prior-art.md` §5 says: it calls
  `ngx_http_close_request(r, NGX_HTTP_REQUEST_TIME_OUT)` directly
  ([`ngx_http_request.c` L1130-1134 at release-1.31.6](https://github.com/nginx/nginx/blob/release-1.31.6/src/http/ngx_http_request.c#L1130-L1135)).
  The body timeout and `send_timeout` do take the terminate path. Either way nothing is written to the client, so the
  conclusion (408 in the log only, then a FIN, or a RST with `reset_timedout_connection on`) stands. Same code at
  `release-1.31.7`.
- Envoy: the code is narrower than the quoted doc sentence. The stream idle timeout answers 408 only while the
  downstream request is incomplete and 504 once it is complete
  ([`utility.cc` L1537-1542](https://github.com/envoyproxy/envoy/blob/v1.40.0/source/common/http/utility.cc#L1537-L1542):
  `return remote_decode_complete ? Http::Code::GatewayTimeout : Http::Code::RequestTimeout;`). A stalled head or body
  is still 408. The plan's TCP-proxy quote stops before the warning's final "etc.".
- Kestrel line ranges in the notes: `RequestHeadersTimeout`'s default is L39 alone (L37 is `_keepAliveTimeout`), and the
  comment about the 2-minute client idle limit is L35-36, spelled "SocketHttpHandler's".

### 7.2 Confirmed (one line each)

- Kestrel, aspnetcore `v10.0.13`: `_keepAliveTimeout = TimeSpan.FromSeconds(130)` L37 and
  `_requestHeadersTimeout = TimeSpan.FromSeconds(30)` L39, with "Slightly more than SocketHttpHandler's old
  PooledConnectionIdleTimeout of 2 minutes" at L35
  ([`KestrelServerLimits.cs`](https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Servers/Kestrel/Core/src/KestrelServerLimits.cs#L35-L39));
  `new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5))` at L360 and L384 under "Matches the
  default IIS minBytesPerSecond"; `TimeoutControl.cs` L62 `if (!Debugger.IsAttached)`, L123 and L157 exactly as the plan
  quotes; the 10.0 options page has "Behavior with debugger attached" listing `KeepAliveTimeout`,
  `RequestHeadersTimeout`, `MinRequestBodyDataRate`, `MinResponseDataRate` and the three features.
- YARP `v2.3.0`: `DefaultTimeout = TimeSpan.FromSeconds(100)` at `HttpForwarder.cs` L30; the
  [timeouts page](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0#debugging-considerations):
  "ActivityTimeout always applies, even when a debugger is attached."
- nginx (live docs): `client_header_timeout 60s` (whole header, 408), `client_body_timeout 60s` ("only for a period
  between two successive read operations"), `send_timeout 60s` ("only between two successive write operations ... the
  connection is closed"), `keepalive_timeout 75s`; stream `proxy_timeout 10m` ("between two successive read or write
  operations"); `proxy_half_close off`, "This directive appeared in version 1.21.4".
- Envoy `v1.40.0` protos: HCM `stream_idle_timeout` "defaults to ``5 minutes``", with the 408 sentence verbatim
  ("If the timeout fires, the stream is terminated with a ``408 Request Timeout`` error code if no upstream response
  header has been received, otherwise a stream reset occurs.",
  [L590-624](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/http_connection_manager/v3/http_connection_manager.proto#L590-L624));
  `request_headers_timeout` "If not specified or set to 0, this timeout is disabled" (L650-656); HTTP `idle_timeout`
  "defaults to ``1 hour``" ([`protocol.proto` L312-327](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/config/core/v3/protocol.proto#L312-L327));
  TCP proxy `idle_timeout` "the default idle timeout is 1 hour" with "Disabling this timeout is likely to yield
  connection leaks due to lost TCP FIN packets, etc." ([`tcp_proxy.proto` L276-284](https://github.com/envoyproxy/envoy/blob/v1.40.0/api/envoy/extensions/filters/network/tcp_proxy/v3/tcp_proxy.proto#L276-L284)).
  The plan's quote is the TCP proxy's wording; the HTTP one says "has a highly likelihood of yielding connection leaks".
- HAProxy: `timeout http-keep-alive` "If this parameter is not set, the "http-request" timeout applies, and if both are
  not set, "timeout client" still applies at the lower level."
- socat default `-t` 0.5 s, and Testcontainers runs `socat TCP-LISTEN:<port>,fork,reuseaddr TCP:<target>` with no `-t`
  or `-T` (Java [`SocatContainer.java` L19, L45 at 2.0.5](https://github.com/testcontainers/testcontainers-java/blob/2.0.5/core/src/main/java/org/testcontainers/containers/SocatContainer.java#L19-L45),
  image `alpine/socat:1.7.4.3-r0`; .NET [`SocatBuilder.cs` L18, L104 at 4.16.0](https://github.com/testcontainers/testcontainers-dotnet/blob/4.16.0/src/Testcontainers/Containers/SocatBuilder.cs#L18-L104),
  image `alpine/socat:1.7.4.4`). Both are still the newest releases.
- http.sys, [HTTP_TIMEOUT_LIMIT_INFO](https://learn.microsoft.com/en-us/windows/win32/api/http/ns-http-http_timeout_limit_info):
  EntityBody, DrainEntityBody, RequestQueue, IdleConnection and HeaderWait "2 Minutes", MinSendRate "150
  bytes/second" ("The default response send rate is 150 bytes-per-second. To disable this timer, set MinSendRate to
  MAXULONG."). The [Server Timeouts Property](https://learn.microsoft.com/en-us/windows/win32/http/server-timeouts-property)
  page does swap the units ("MinSendRate ... 150 Sec", "RequestQueue ... 120 Bytes/Sec"), as the notes say.
