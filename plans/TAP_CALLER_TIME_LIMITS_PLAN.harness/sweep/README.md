# Sweep of `TAP_CALLER_TIME_LIMITS_PLAN.md` (#173), 2026-10-15

The plan was checked for completeness and accuracy on 2026-10-15, and what could change it was looked into. The plan's
§11 log says what changed. Line numbers in these files are at `3240c983` unless they say otherwise, and the wiki's at
its `origin/master` `60daf39`.

| File | What it is |
|---|---|
| `notes-line-refs.md` | Every citation of Kronikol's code in the plan, checked against `3240c983`, and every `Log` call site in the three taps (READ). `line-refs-probe.cs` settles three of them by running |
| `notes-repo.md` | The repository around the plan: frameworks and packages, linked-file type conflicts, where tap options are copied, what runs through each tap, the tests and CI, what the wiki's pages say (and what of it is false), Kronikol4J, the roadmap, disclosure, the other worktrees, and the sibling plan #174 as it is being implemented |
| `external-facts.md` | Outside sources, linked: #173 and #174 since filing, the dotnet/runtime issues, `HttpListener` in .NET 10.0.13, how OTLP exporters reuse connections, a socket after a cancelled read, early answers and lingering closes, and a check of the plan's §2.4 |
| `notes-design-review.md` | An adversarial review of the design: 22 findings, each folded into the plan or answered in its §11 log |
| `early-answer-probe.cs`, `cancel-then-answer-probe.cs` | Two socket behaviours the design relies on, below, with outputs for Windows and Linux, and `cancel-then-answer-probe-windows-net8.txt` for .NET 8 |
| `send-rate-piece-probe.cs` | http.sys's send-rate timer against an answer written in pieces (Windows only), below |
| `timeout-limits-check.cs` | Where the BCL stops taking a duration, below |
| `line-refs-probe.cs` | Three facts about the code, below |
| `notes-rebase-4.22.1.md`, `mapline.py` | The plan against 4.22.1 (`fefb99e5`) once #174 had shipped: what changed under it, how Appendix A's 4.22.1 lines were mapped and checked, and what the design relies on there (READ) |

## Two socket behaviours the design relies on


Two probes, each a .NET 10 file-based app with no packages (`dotnet run <file>.cs`), measured on 2026-10-15 on Windows
and on Linux:

- `early-answer-probe.cs`, for R0 item 3: does a caller hear an answer that the server writes before it has read the
  request body, when the server then closes the connection?
- `cancel-then-answer-probe.cs`, for R2 items 3 and 4: is a socket still usable for an answer after a
  `CancellationToken` cancelled its pending read?

In short:

- An answer written before the body and followed by an immediate close is often lost. Closing with body bytes still
  unread sends an RST, and HttpClient (`SocketsHttpHandler`, which sends the whole body before it reads) then fails
  without the 401. Over variants a and b below, it lost the 401 with an 8 MiB body in 20 of 20 trials on both platforms;
  with 1 MiB in 15 to 17 of 20 on Windows and 10 to 14 on Linux; with a chunked 1 MiB body in 20 of 20 on Windows and
  10 to 16 on Linux; and with 64 KiB in 1 to 5 of 20 on Windows. A lingering close (the 401, `Shutdown(Send)`, then
  discarding the body until EOF, its declared length or 2 s) delivered the 401 in all 120 trials on each platform. So
  did discarding the body before answering, as the 413 path does today.
- A cancelled `NetworkStream.ReadAsync` or `Socket.ReceiveAsync` leaves the socket usable. On both platforms, in 20 of 20
  trials of each variant that cancels a read, the read threw `OperationCanceledException` (0.1 ms after the cancel,
  median) and the 408 written on the same socket reached the caller whole. The connection then closed with a FIN and no
  RST, and bytes the caller sent after the cancel were kept for a later read. Closing with such bytes unread still
  delivered the 408 to a caller that was reading, but an RST followed the FIN in 20 of 20.

### Where it ran

| | Windows | Linux |
|---|---|---|
| OS | Windows 11 10.0.26200.9711 | Ubuntu 24.04.5 in `mcr.microsoft.com/dotnet/sdk:10.0`, in the podman WSL machine (kernel 6.18.54.1-microsoft-standard-WSL2) |
| .NET | 10.0.11 (SDK 10.0.300) | 10.0.12 (SDK 10.0.401) |
| Outputs | `early-answer-probe-windows.txt`, `cancel-then-answer-probe-windows.txt` | `early-answer-probe-linux.txt`, `cancel-then-answer-probe-linux.txt` |

Each output opens with the SDK, OS and host lines of `dotnet --info`. Everything runs over 127.0.0.1 in one process, one
trial at a time, each trial on a new connection: 20 trials per cell, after one warm-up trial per caller (or variant)
that is not counted. The early-answer probe takes 3 to 5 s and the cancel probe about 35 s.

### The server is OtlpTap's connection code

Both servers serve a connection as `OtlpTap.ServeAsync` does at `3240c983` (`OtlpTap.cs` L277-306): a `TcpListener` on
127.0.0.1 (port 0, which is safe here because the probe keeps its listener), `TcpClient.NoDelay`,
`TcpClient.GetStream()`, a 16 KiB buffer filled by `NetworkStream.ReadAsync(Memory<byte>, CancellationToken)`
(`HttpRequestReader.cs` L285-290), answers written with `NetworkStream.WriteAsync` and `FlushAsync` (`OtlpTap.cs`
L459-475), and the stream and the client disposed by `await using` and `using`. `GetStream()` returns a stream that owns
the socket, and disposing it calls `Socket.Shutdown(Both)` and then closes the socket
([`TCPClient.cs` L259](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/TCPClient.cs#L259),
[`NetworkStream.cs` L403-410](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Sockets/src/System/Net/Sockets/NetworkStream.cs#L403-L410),
v10.0.13). The early-answer server reads the head and drains a body with a copy of `HttpRequestReader`'s code that
discards the body instead of keeping it.

### `early-answer-probe.cs`

The server reads to the end of the head, then answers `HTTP/1.1 401 Unauthorized` with `Content-Length: 0` and
`Connection: close`, in one of four ways:

| Variant | After the head, the server |
|---|---|
| a | writes the 401 and disposes at once |
| b | writes the 401, calls `Socket.Shutdown(Send)` and disposes at once |
| c | writes the 401, calls `Shutdown(Send)`, then reads and discards until EOF, 2 s, or the declared `Content-Length` (body bytes read with the head count towards it), whichever comes first, and disposes: a lingering close |
| d | reads and discards the whole body first, as the 413 path does today (`DiscardAsync` over the `Content-Length`, `HttpRequestReader.cs` L96-100; a chunked body chunk by chunk with its trailers, as `ReadChunkedAsync` drains a body over the cap), then writes the 401 and disposes |

| Caller | What it sends, on a new connection per trial |
|---|---|
| `http-4K`, `http-64K`, `http-1M`, `http-8M` | `HttpClient` over a new `SocketsHttpHandler` with its defaults: `POST /v1/traces` with a `ByteArrayContent` of 4 KiB, 64 KiB, 1 MiB or 8 MiB (`application/x-protobuf`) |
| `http-chunked-1M` | the same with a 1 MiB `StreamContent` over a stream that cannot seek, sent with `Transfer-Encoding: chunked` |
| `raw-1M` | a `Socket` that writes the head (`Content-Length: 1048576`) and the 1 MiB body in one `SendAsync`, then reads until EOF or an error, even when the send failed |

Each trial records what the caller got (the status, or the exception as a chain of types with each `HttpRequestError`
and `SocketErrorCode`) and when, and on the server's side how many body bytes came in with the head, whether the answer
was written, and how the drain ended. Every trial is bounded at 15 s; none came near it. An HttpClient failure is marked
"sending" or "reading" from its `IOException`'s message. Without `Expect: 100-continue`, `SocketsHttpHandler` sends the
whole body before it reads the answer
([`HttpConnection.cs` L570-598 at v10.0.13](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs#L570-L598)),
so "reading" means its send had completed. It flushes the head with the start of the body (its 4 KiB write buffer) and
writes the rest of a `ByteArrayContent` to the socket in one call
([L1412-1450](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs#L1412-L1450)),
which is why, with HttpClient as the caller, the server had usually read 3,979 to 3,985 body bytes with the head (a
full 16 KiB buffer in the other trials).

Before the trials, for context, the probe measures loopback buffering against a peer that accepts and never reads: how
many bytes 64 KiB sends get accepted before one waits 500 ms, and whether one `SendAsync` of 8 MiB completes within 1 s.

#### Results on Windows

Trials of 20 in which the caller got the 401, and in brackets what the others got. reset is `SocketException`
`ConnectionReset` (WSAECONNRESET) and aborted is `ConnectionAborted` (WSAECONNABORTED); for HttpClient, sending or
reading says where it failed.

| Caller | a: 401, dispose | b: 401, `Shutdown(Send)`, dispose | c: 401, `Shutdown(Send)`, drain, dispose | d: drain, 401, dispose |
|---|---|---|---|---|
| `http-4K` | 20 | 20 | 20 | 20 |
| `http-64K` | 15 (5 reset, reading) | 19 (1 aborted, reading) | 20 | 20 |
| `http-1M` | 3 (16 aborted and 1 reset, reading) | 5 (15 aborted, reading) | 20 | 20 |
| `http-8M` | 0 (20 aborted, reading) | 0 (20 aborted, reading) | 20 | 20 |
| `http-chunked-1M` | 0 (20 reset, sending) | 0 (18 aborted and 2 reset, sending) | 20 | 20 |
| `raw-1M` | 13 (7 read nothing before a reset) | 11 (9 read nothing before a reset or an abort) | 20 | 20 |

Every send of the raw caller completed. Loopback buffering on Windows: 64 KiB sends were accepted up to 262,144 to
393,216 bytes before one waited 500 ms, but one `SendAsync` of 8 MiB completed within the 1 s it was given, in 3 of 3,
with 65,495 to 65,536 bytes at the peer. So HttpClient's one large write completed, it went on to read, and the reset or
abort met it there: the 401, sent before the RST, was no longer there to read, as the raw caller's 7 and 9 empty reads
show. The chunked body goes out in separate writes, which do wait, so there HttpClient was still sending when the RST
came.

The server wrote its answer in every trial. Every outcome came within 30 ms of the start of the send (median under 5
ms). In c the longest drain took 29 ms and none reached the 2 s bound. A first full run on Windows, before the
buffering lines were added, gave the same picture: in a, 19, 13, 3, 0, 0 and 15 trials of 20 got the 401 (callers in
the table's order); in b, 20, 17, 6, 0, 0 and 16; in c and d, 20 in every cell.

#### Results on Linux

Same layout. broken pipe is `SocketException` `Shutdown`, .NET's name for EPIPE ("Broken pipe";
[`SocketErrorPal.Unix.cs` L70](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/Common/src/System/Net/Sockets/SocketErrorPal.Unix.cs#L70)).
Linux reports an RST that arrives after a FIN as EPIPE, not ECONNRESET
([`tcp_reset`, `tcp_input.c` L4650-4651 at v6.18](https://github.com/torvalds/linux/blob/v6.18/net/ipv4/tcp_input.c#L4650-L4651)),
and here the FIN came first: from `Shutdown(Send)` in b, and from the `Shutdown(Both)` inside `NetworkStream.Dispose`
in a.

| Caller | a: 401, dispose | b: 401, `Shutdown(Send)`, dispose | c: 401, `Shutdown(Send)`, drain, dispose | d: drain, 401, dispose |
|---|---|---|---|---|
| `http-4K` | 20 | 20 | 20 | 20 |
| `http-64K` | 20 | 20 | 20 | 20 |
| `http-1M` | 10 (10 broken pipe, sending) | 6 (14 broken pipe, sending) | 20 | 20 |
| `http-8M` | 0 (20 broken pipe, sending) | 0 (20 broken pipe, sending) | 20 | 20 |
| `http-chunked-1M` | 4 (16 broken pipe, sending) | 10 (10 broken pipe, sending) | 20 | 20 |
| `raw-1M` | 20 (4 of them after a broken pipe in the send) | 20 | 20 | 20 |

Loopback buffering on Linux: 64 KiB sends were accepted up to 2,621,440 bytes before one waited 500 ms (3 of 3, with
128,512 bytes at the peer), and one `SendAsync` of 8 MiB was still pending after 1 s in 3 of 3. So HttpClient was
still sending when the RST came, and a failed send ends its request without a read. The 401 itself survives the RST on
Linux: the raw caller read it, then a FIN, in all 40 trials of a and b, 4 of them after its own send had failed.

The server wrote its answer in every trial. Every outcome came within 34 ms of the start of the send (median under 4
ms). In c the longest drain took 4 ms and none reached the 2 s bound.

### `cancel-then-answer-probe.cs`

The caller sends a partial head (`POST /v1/traces HTTP/1.1`, `Host: x`, no blank line: 35 bytes) and stalls. It keeps
one receive pending throughout, so each byte, a FIN (a receive of 0) and an RST (`ConnectionReset`) is timed when it
arrives. Its loop stops at the first FIN or RST, so 50 ms later the probe also reads both ends of the connection from
the TCP table (`IPGlobalProperties.GetActiveTcpConnections`, before the caller closes): the caller in CloseWait and the
server in FinWait2 is a clean close, and both ends gone means an RST came, even after a FIN.

The server reads the partial head, then starts the read that stalls. Its answer is `HTTP/1.1 408 Request Timeout` with
`Content-Length: 0` and `Connection: close` (70 bytes).

| Variant | What the server does |
|---|---|
| a | `NetworkStream.ReadAsync(Memory<byte>, token)` is pending; 200 ms later the token's source is cancelled. The server awaits the read, writes the 408 with `WriteAsync` and `FlushAsync` under a fresh 5 s token, calls `Shutdown(Send)`, and disposes the stream and the client |
| b | the same with `Socket.ReceiveAsync(Memory<byte>, SocketFlags.None, token)` as the read |
| c | no token: 200 ms into the pending `NetworkStream.ReadAsync`, another thread calls `Socket.Send` with the 408, then `Shutdown(Both)`, then disposes the client 100 ms later (the gap tells whether the read ended at the shutdown or at the dispose) |
| d | as a, but the caller sends 100 more bytes about 50 ms after the cancel, and the server writes about 50 ms after that; after the write, one more read under a fresh 1 s token shows whether those bytes were kept |
| e | as d without the later read, so the server closes with the caller's 100 bytes unread. Added to the four variants the sweep set out to measure: it is what a tap does that answers and closes while a stalled caller has started sending again |

Times are milliseconds after the cut: the cancel in a, b, d and e, the start of the `Send` in c. The caller's later
bytes in d and e left 48 to 55 ms after the cancel on Linux and 52 to 88 ms after it on Windows, whose timer rounds a
50 ms delay up.

#### Results on Windows

Each cell is out of 20 trials; times are the median, with the maximum in brackets.

| Variant | The server's read | The 408 write | What the caller got | 50 ms after the end |
|---|---|---|---|---|
| a | `OperationCanceledException`, 20, at 0.1 ms (0.4) | ok, 20 | the whole 408, then FIN, 20, at 0.3 ms (0.5) | clean, 20 |
| b | `OperationCanceledException`, 20, at 0.1 ms (0.3) | ok, 20 | the whole 408, then FIN, 20, at 0.2 ms (0.5) | clean, 20 |
| c | still pending after `Shutdown(Both)`; ended at the dispose with `IOException` > `SocketException` `OperationAborted`, 20, at 107.6 ms (119.4) | `Socket.Send` ok, 20 | the whole 408, then FIN, 20, at 0.1 ms (0.2) | clean, 20 |
| d | `OperationCanceledException`, 20, at 0.1 ms (24.2); the later read returned the caller's 100 bytes, 20 | ok, 20 | the whole 408, then FIN, 20 | clean, 20 |
| e | `OperationCanceledException`, 20, at 0.1 ms (0.2); the caller's 100 bytes left unread | ok, 20 | the whole 408, then FIN, 20 | both ends gone (an RST after the FIN), 20 |

#### Results on Linux

Same layout.

| Variant | The server's read | The 408 write | What the caller got | 50 ms after the end |
|---|---|---|---|---|
| a | `OperationCanceledException`, 20, at 0.1 ms (0.2) | ok, 20 | the whole 408, then FIN, 20, at 0.3 ms (0.5) | clean, 20 |
| b | `OperationCanceledException`, 20, at 0.1 ms (0.2) | ok, 20 | the whole 408, then FIN, 20, at 0.3 ms (0.5) | clean, 20 |
| c | returned 0 bytes at `Shutdown(Both)`, before the dispose, 20, at 0.2 ms (0.3) | `Socket.Send` ok, 20 | the whole 408, then FIN, 20, at 0.2 ms (0.3) | clean, 20 |
| d | `OperationCanceledException`, 20, at 0.1 ms (0.2); the later read returned the caller's 100 bytes, 20 | ok, 20 | the whole 408, then FIN, 20 | clean, 20 |
| e | `OperationCanceledException`, 20, at 0.1 ms (0.1); the caller's 100 bytes left unread | ok, 20 | the whole 408, then FIN, 20 | both ends gone (an RST after the FIN), 20 |

The two platforms differ only in c: on Windows `Shutdown(Both)` leaves a pending read pending, and only the dispose ends
it; on Linux the shutdown ends it with 0 bytes. In a and b the 408 write had completed 0.2 ms after the cancel (median;
at most 0.5 ms), far inside its 5 s bound. Every caller here had a receive pending; one that is still writing when the
server closes is what `early-answer-probe.cs` measures.

### Running it

Windows, from this folder in Git Bash. Each probe is built first, so that build output stays out of the result, and its
output opens with the `dotnet --info` excerpt:

```sh
info() {
  echo "dotnet --info (excerpt):"
  dotnet --info | tr -d '\r' | awk '
    /^\.NET SDK:/ { print; n = 2; next }
    /^Runtime Environment:/ { print; n = 4; next }
    /^Host:/ { print; n = 3; next }
    n > 0 { print; n-- }'
  echo
}
for probe in early-answer-probe cancel-then-answer-probe; do
  dotnet build "$probe.cs" > /dev/null 2>&1
  { info; dotnet run "$probe.cs" 2>&1; } | tr -d '\r' > "$probe-windows.txt"
done
```

Linux, from Git Bash through podman's WSL machine (`podman-machine-default`) and the SDK image. The probes use no
packages, so no NuGet cache is needed. Ship both probes and a small `inner.sh` in a tarball
(`tar -czf k173s.tgz early-answer-probe.cs cancel-then-answer-probe.cs inner.sh`), where `inner.sh` is:

```sh
probe="$1"
mkdir -p /w/out
dotnet build "$probe.cs" > "/w/out/$probe-build.log" 2>&1
{
  echo "dotnet --info (excerpt):"
  dotnet --info | awk '
    /^\.NET SDK:/ { print; n = 2; next }
    /^Runtime Environment:/ { print; n = 4; next }
    /^Host:/ { print; n = 3; next }
    n > 0 { print; n-- }'
  echo
  dotnet run "$probe.cs" 2>&1
} > "/w/out/$probe-linux.txt"
```

Then write this `run.sh` with LF line endings and start it with
`MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/<path>/run.sh` (inline commands passed through
`wsl` get their quoting mangled). It extracts with `tar -xzm`: without the `m` the files keep their Windows times and
MSBuild can run a stale build. The two probes run one after the other, each in its own container:

```sh
set -e
T=/mnt/c/<path>
mkdir -p /root/k173s
tar -xzmf "$T/k173s.tgz" -C /root/k173s
for probe in early-answer-probe cancel-then-answer-probe; do
  podman run --rm --name "k173s-$probe" -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -v /root/k173s:/w -w /w mcr.microsoft.com/dotnet/sdk:10.0 sh /w/inner.sh "$probe"
done
cp /root/k173s/out/*-linux.txt "$T/"
```

When done, `podman rm -f` any `k173s-*` container that remains (with `--rm` none did), then remove the directory:
`wsl -d podman-machine-default -u root --cd /root -- rm -rf k173s`.

## `cancel-then-answer-probe.cs` on .NET 8 (Windows)

The taps also build for net8.0, where the runtime marks a socket disconnected after a cancelled operation
(dotnet/runtime#69397; .NET 9 stopped that, dotnet/runtime PR #99181). The same probe ran with
`#:property TargetFramework=net8.0` added at its top, on .NET 8.0.27 on Windows: `cancel-then-answer-probe-windows-net8.txt`.
It matches .NET 10. In each of variants a, b and d the cancelled read threw `OperationCanceledException` (0.1 ms after
the cancel, median), and the 408 written next reached the caller whole, then a FIN, 20 of 20. In d the caller's later
100 bytes came back on a later read, 20 of 20. In e, with those bytes left unread, the caller still heard the whole 408
and a FIN, but the connection did not end cleanly: both ends were gone from the TCP table 50 ms later, 20 of 20.

## `send-rate-piece-probe.cs`: http.sys's send-rate timer, piece by piece

In short: on Windows, http.sys reset a caller that had stopped reading 120.2 s after a 16 KiB piece stalled, so a 100 s
limit in the tap acts first. A 4 KiB piece was reset after 40.0 s, five seconds later than the model in
`../listener/README.md` predicts. A 1 KiB piece was reset after 396 to 406 s, not the predicted 15 s. The deadline
grows with the piece but is not taken from the piece alone.

Section 4.3 item 4 of the plan, as first written, had ProxyTap write each answer in pieces of at most 16 KiB (after
this probe the last piece takes the remainder, 16 to 32 KiB): `ContentLength64` first, then
one awaited `WriteAsync` per piece. On Windows `HttpListener` is http.sys, whose send-rate timer (150 B/s by default)
resets a connection whose send does not complete in time. `../listener/README.md` ("Also found", item 2) fitted a model
to that timer: a stalled send is reset (pending bytes / 150, rounded up to a multiple of 5 s) + 5 s after it began. Its
stalled send was always an answer's first, so the model had not been tried on a piece written after others.

### What the probe does

`send-rate-piece-probe.cs` is a .NET 10 file-based app with no packages, and runs on Windows only.

- Server: one `HttpListener` on `http://localhost:<free port>/` with its timeouts untouched. For `GET /?piece=N` it sets
  `ContentLength64` to 8 MiB and writes the body in pieces of N bytes, one awaited `WriteAsync` per piece, on a thread
  of its own. It records when each write is called and completes, and the time and exception of the write that fails.
- Callers: nine raw sockets to `::1` (localhost's first address), three for each N of 16 KiB, 4 KiB and 1 KiB, all
  running at once. Each sets `ReceiveBufferSize` to 16 KiB, sends `GET /?piece=N HTTP/1.1` with a Host header and posts
  no receive. Every 0.25 s the probe reads each caller's `Socket.Available`, which consumes nothing, and the TCP table.
  Once the server's write has failed, the caller reads what is left.
- The stall begins when the last write completed; the reset is when the next write failed.
- The output opens with the `dotnet --info` excerpt, the Windows build, http.sys's version, the request queue's
  timeouts from `netsh http show servicestate view=requestq` (the URL group inherits; the server session shows 120 s for
  each timer and a minimum send rate of 150 bytes/sec) and the answer's head (112 bytes, from a HEAD exchange). The run
  is capped at 8 minutes.

Beside the model, the output applies the same rule to every byte posted since the answer began, and to the bytes posted
but not in the caller's socket buffer at the stall.

### Where it ran

On 2026-10-15: Windows 11 25H2, build 10.0.26200.9711, http.sys 10.0.26100.9711, .NET 10.0.11 (SDK 10.0.300), 20 logical
CPUs. The output, `send-rate-piece-probe-windows.txt`, prints http.sys's version as 6.2.26100.9711: the process that
`dotnet run` starts reads 6.2 as the major and minor version of system files (kernel32.dll and ntdll.dll as well), the
version Windows reports to a process not manifested for Windows 10. PowerShell reads 10.0.26100.9711. The run took
407 s.

### Results

| N | Model's prediction | Measured reset after the stall | Body bytes written before the stall |
|---|---|---|---|
| 16 KiB | 115 s (16,384 / 150 = 109.2 s) | 120.2 s (3 of 3) | 16,384 (1 write) |
| 4 KiB | 35 s (4,096 / 150 = 27.3 s) | 40.0 s (3 of 3) | 32,768 (8 writes) |
| 1 KiB | 15 s (1,024 / 150 = 6.8 s) | 396.0 s (2 of 3), 406.0 s (1 of 3) | 98,304 (96 writes) |

Each reset failed the pending write with `HttpListenerException`, `ErrorCode` 1236 ("The network connection was aborted
by the local system", `HResult` 0x80004005). Both ends of the connection were gone from the TCP table at the next poll,
and the caller then read no bytes and an RST, though its socket had held 16,384 bytes since the stall.

- Every stall began within 3.1 ms of the first write. The writes completed past what the caller's socket held, which
  was 16,384 bytes throughout, and how far they got depended on the piece size.
- The 16 KiB and 4 KiB pieces were reset 5 s later than the model predicts. The resets fell on http.sys checks about
  5.013 s apart (failures at 40.779, 120.972, 396.732 and 406.744 s are 8, 24, 79 and 81 checks after one at 0.673 s),
  and the stalls began 0.09 s after a check. The model fits these two if it adds 10 s, not 5 s. A stall that begins
  later between checks would be reset up to 5 s sooner, so by that rule a 16 KiB piece is not reset before 115 s, and
  by the model's own rule not before 110 s.
- The 1 KiB pieces were not timed by the piece. By the same rule, 396 to 406 s is the deadline for 57,000 to 59,250
  bytes. The 96 completed writes had posted 98,416 bytes with the head, and the caller's socket held 16,384 of them, so
  a completed write had not necessarily reached the caller. A deadline taken from the bytes TCP still had to deliver
  when the piece was posted would explain all three sizes, but the probe cannot see TCP's queues, so that is an
  inference.
- The alternatives fail: every byte since the answer began predicts 225, 255 and 670 s, and the bytes not in the
  caller's socket buffer predict 115, 145 and 560 s.

For the plan: a stalled 16 KiB piece outlasts a 100 s limit, so the tap acts first. A smaller final piece can be reset
by http.sys sooner. The 4 KiB piece went at 40 s, and by the rule above any piece up to about 13,500 bytes goes before
100 s when nothing is queued ahead of it, but the 1 KiB pieces were held for minutes. Where http.sys acts first, the
tap's pending write throws `HttpListenerException` 1236 before the tap's limit expires, so the tap sees a failed write,
not its own timeout. This probe did not run an answer of 16 KiB pieces whose last piece is smaller.

### Running it

On Windows, from this folder:

```sh
dotnet build send-rate-piece-probe.cs
dotnet run send-rate-piece-probe.cs > send-rate-piece-probe-windows.txt
```

stdout is the result and stderr is progress. `dotnet run send-rate-piece-probe.cs quick` caps the run at 40 s, a smoke
test that is not a measurement.

## `timeout-limits-check.cs`

Where the BCL stops taking a duration, run on Windows with .NET 10.0.11 (SDK 10.0.300); output
`timeout-limits-check-windows.txt`. `TimeSpan.MaxValue` plus 10 s throws `OverflowException` (`ProxyTap.cs` L192 adds
10 s to `ForwardTimeout`). `HttpClient.Timeout` takes at most 24.20:31:23.647, so 25 days throws. `CancelAfter` takes
40 days and refuses 60 (its limit is about 49.7 days). `SocketsHttpHandler.ConnectTimeout` refuses zero and
`TimeSpan.MaxValue`. `Timeout.InfiniteTimeSpan` plus 10 s is 9.999 s, which is the infinite-`ForwardTimeout` bug (plan
R0 item 1). `TimeProvider.System.CreateTimer` refuses a due time of 60 days (at most 4,294,967,294 ms), one more reason
the watch compares elapsed time rather than arming a timer per limit (plan §4.2 item 2).

## `line-refs-probe.cs`

Three facts the citation check (`notes-line-refs.md`) settled by running, on Windows (`line-refs-probe-windows.txt`)
and, by that check's own run, in the Linux SDK container with the same output: how `HttpRequestReader` parses each
`Content-Length` (`-5` and `+5` parse; `abc`, `10, 10`, `3000000000` and `2147483648` do not); that `Stream.CopyToAsync`
asks a non-seekable source for 131,072 bytes a read; and that setting `NoDelay` on an accepted socket whose peer has
reset did not throw (3 of 3).
