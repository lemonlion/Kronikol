# Listener probe for `TAP_CALLER_TIME_LIMITS_PLAN.md` (#173)

`abort-stall-probe.cs` measures the six INFERRED claims in section 8 of
[`../source/dotnet-listener-timers.md`](../source/dotnet-listener-timers.md) on a bare `System.Net.HttpListener`, with
raw-socket callers and no Kronikol package. It is one .NET 10 file-based app, the same file on each OS:
`dotnet run abort-stall-probe.cs`. stdout is the result and stderr is progress; the argument `quick` makes a short smoke
run that is not a measurement.

Measured on 2026-10-14:

- `abort-stall-windows.txt`: Windows 11 10.0.26200.9711, .NET 10.0.11 (SDK 10.0.300). `HttpListener` is http.sys.
- `abort-stall-linux.txt`: Ubuntu 24.04.5 in `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.401, .NET 10.0.12), in the
  podman WSL machine (kernel 6.18.54.1-microsoft-standard-WSL2). `HttpListener` is the managed implementation, the one
  macOS also runs; macOS itself was not measured.

Every case runs 5 times. The writes, `Response.Abort()` and `HttpListener.Close()` each run on a thread of their own
with a 10 s watchdog, so a call that blocks is printed as `BLOCKED >10 s` and the run goes on. Cases 5 and 6 watch idle
and stalled callers for minutes, so they run concurrently with cases 1 to 4, each case on its own listener and port. A
full run takes 4 minutes on Linux and 7.5 on Windows. An earlier full run on each platform, before case 1b was added,
gave the same tallies (timer firings within 0.1 s).

## What each case does

| Case | Setup | Reported |
|---|---|---|
| 1 | GET; `SendChunked = true`; 16 MB as 256 awaited `WriteAsync` calls of 64 KiB on a thread of their own; the caller (receive buffer 16 KiB) does not read. `Response.Abort()` runs 1 s after the writes stall (no write completed for 1 s) | whether the stalled `WriteAsync` call had returned its Task; whether `Abort()` returned within 10 s; then the caller reads everything: bytes, head, a chunked parse, then FIN or RST; when `Abort()` and the write returned in the end |
| 1b | As 1, with 4 MB as writes of 1 to 8 bytes (random sizes, seeded by the run) | The same. Added because with 64 KiB writes (and 1 KiB writes in a trial) the stall never fell on one of the listener's synchronous writes |
| 2a, 2b | `ContentLength64 = 16 MB`, one 16 MB `WriteAsync` (2a) or 256 of 64 KiB (2b); the caller does not read; `Abort()` 1 s after the stall | the pending write's outcome and when; the listener's tables by reflection (`_listenerContexts`, `_connections`; on Windows http.sys's active requests from `netsh`) and the server side's TCP sockets (Linux `/proc`: state, whether the process still holds the fd, bytes unsent) before the Abort, 0.2 s after it and after the caller's read; what the caller then reads |
| 3a | POST with `Content-Length: 100000` and 1,000 body bytes, then the caller stalls with the socket open and a receive pending. The server reads until a `ReadAsync` stays pending for 0.5 s; 1 s later `StatusCode = 408` and `Abort()` | the pending read's result or exception and when; what the caller receives |
| 3b | As 3a with `Transfer-Encoding: chunked` and one 1,000-byte chunk | The same |
| 3c | Beyond the six claims: a body that trickles in (1,000 bytes, 100 at 1 s, 100 at 2 s, 70,000 at 3 s), for each body kind, one `ReadAsync(64 KiB)` pending at a time | when each read returns, with how many bytes |
| 4 | A new listener per run. Caller (a) sends a request line and Host, no blank line; caller (b) completes one exchange (200, `Content-Length: 2`) and idles on keep-alive. Then `HttpListener.Close()` | whether `Close()` returned; what each caller receives within 6 s |
| 5a, 5b, 5c | Connects and sends nothing; one exchange, then idle; one byte of a head (`G`), then nothing. Watched to 210 s | FIN or RST, when, and any bytes |
| 6 | A new listener that is never started: each `TimeoutManager` getter, then each setter (5 s, or 10,000 bytes/s). On Windows also: 6a/6a5, a body stalled as in 3a with `EntityBody` at its default or 5 s; 6b/6b10k, a 64 MB answer in 64 KiB writes to a caller that does not read, with `MinSendBytesPerSecond` at its default or 10,000; 6c/6c5, a partial second head after one exchange with `HeaderWait` at its default or 5 s; 6c5first, a partial first head with `HeaderWait` 5 s; 6d5/6d5first, `IdleConnection` 5 s after one exchange or on a connection that sends nothing. Watched to 210 s (6b to 540 s) | what the caller sees and when; the server's pending read or write; the URL group's timeouts as `netsh` reports them after the set |

## Results

| Claim (section 8) | Linux (managed listener) | Windows (http.sys) | Verdict |
|---|---|---|---|
| **1.** `Abort()` during a stalled chunked write blocks the aborting thread, and `WriteAsync` itself can block synchronously in `BeginWrite` | 64 KiB writes: every `WriteAsync` call returned its Task (the longest took 0.8 ms); the stall was the asynchronous data send of write 37, 5/5. `Abort()` **blocked >10 s, 5/5**, and returned 1 to 5 ms after the caller began to read. The interrupted write then completed with no exception, and the 219 later calls completed at once as no-ops. The caller read 37 chunks, then the last chunk (`0` CRLF CRLF) where chunk 37's closing CRLF belongs (malformed), then FIN. 1b, writes of 1 to 8 bytes: the **`WriteAsync` call itself blocked before returning a Task in 4/5**, for 12.5 s, until the caller read: 3 on the chunk's size line, 1 on the chunk's closing CRLF (which runs inside the call when the data send completes synchronously); 1/5 stalled on the data send. `Abort()` blocked 5/5. What the caller then got: cut or malformed in 4/5, but **whole chunks and a proper last chunk in 1/5** (the CRLF case), a cut body that looks complete | `Abort()` returned in 0.3 to 3.3 ms in all 10 runs (1 and 1b); the pending write faulted 0 to 4 ms later with `HttpListenerException` 1236 ("The network connection was aborted by the local system"). Every call returned its Task. The caller got an RST and no bytes | **Confirmed** on Linux, with a correction to the second half: whether the call blocks depends on where the stall falls. It never did with 64 KiB writes (0/5) and did in 4/5 with writes of 1 to 8 bytes. `Abort()` blocks either way, until the caller reads (the caller leaving, or the listener closing, was not measured). Neither blocks on Windows |
| **2.** `Abort()` during a pending Content-Length write: the write completes with no exception | 2a and 2b, 5/5 each: the pending write **completed with no exception** 0 to 1 ms after `Abort()`, which returned in 0.3 to 0.6 ms; in 2b the 219 later calls were silent no-ops. `_listenerContexts` and `_connections` went from 1 to 0 within 0.2 s, 10/10. The process released the socket (no fd) while the kernel kept about 2.3 MB unsent in FIN_WAIT1. The caller then read a short body (2,367,375 of 16,777,216 bytes) and a FIN | The write threw 0 to 1 ms after `Abort()`: `HttpListenerException` 995 for the one 16 MB write (2a), 1236 for 64 KiB writes (2b), 5/5 each. http.sys showed no active request. The caller got an RST and no bytes | **Confirmed** on Linux (10/10), and the connection is released at once. Windows throws instead, as SOURCE section 4.4 says |
| **3.** `Abort()` during a pending body read: `IOException`, `HttpListenerException(400)`, or (chunked) a clean end | 3a, Content-Length: `IOException` with an inner `SocketException` OperationAborted (125), 1 to 4 ms after `Abort()`, 5/5. 3b, chunked: `HttpListenerException` 400 ("I/O operation aborted: ..."), 5/5. **No clean end** in these 10 runs, nor in the 10 reads 3c aborted. The caller received `HTTP/1.1 408 Request Timeout` with `Connection: close`, `Transfer-Encoding: chunked` and an empty body (`0` CRLF CRLF), then a FIN, 10/10 | `HttpListenerException` 995 within 1 ms of `Abort()`, 5/5 in 3a and in 3b. The caller got an RST and no bytes: no 408 | **Corrected**: after the application's own `Abort()` the read threw every time (20/20 with 3c); the end of stream that `Abort()`'s shutdown makes never won the race. A chunked body that reads as complete (dotnet/runtime#135680) comes from the caller's FIN, not from `Abort()`. The exception depends on the body kind |
| **4.** Disposal sends a 200 head to a connection stalled mid-head and to an idle kept-alive one | `Close()` returned in 0.6 to 1.8 ms. (a) mid-head: `HTTP/1.1 200 OK`, `Transfer-Encoding: chunked`, an empty body, **then the last chunk a second time** (5 stray bytes), then FIN, 5/5. (b) idle kept-alive: `HTTP/1.1 200 OK` with `Connection: close`, no length and no body, then FIN, 5/5. Both within 2 ms of the call | `Close()` returned in under 1 ms. Both callers got an RST within 1 ms and no bytes, 5/5 | **Confirmed** for the managed listener: both callers get a well-formed 200. http.sys resets them |
| **5.** The 90 s first-read timer and the 15 s keep-alive one close with an RST and no response | 5a: **RST at 90.0 s**, no bytes, 5/5. 5b: **RST 15.0 s** after the answer, no bytes, 5/5. 5c, one byte of a head: **still open at 210 s**, 5/5 | 5a, 5b and 5c all reset at 125.0 to 125.2 s with no bytes (http.sys's 120 s timers fire on a partial head too) | **Confirmed** on Linux |
| **6.** http.sys: what the caller sees when EntityBody, HeaderWait and MinSendRate fire, and what the pending read or write gets | `EntityBody`, `HeaderWait`, `MinSendBytesPerSecond` and `RequestQueue` throw `PlatformNotSupportedException` when set (their getters read 0); `DrainEntityBody` and `IdleConnection` are stored and read back (SOURCE section 2 says nothing reads them; not probed here) | Every timer ended in an **RST with no bytes; none sent a 408**. EntityBody, default: 125.2 s, and the server's pending read threw `HttpListenerException` 995. MinSendRate, default 150 bytes/s: the stalled 64 KiB write faulted with `HttpListenerException` 1236 at **445.7 s**. HeaderWait, default, on a later request: 125.0 to 125.2 s. Set values: `EntityBody` = 5 s and `MinSendBytesPerSecond` = 10,000 **apply to the first request** on a connection (RST at 9.9 s; the write faulted at 14.9 s); `HeaderWait` = 5 s and `IdleConnection` = 5 s **apply only after the first request** (9.8 to 9.9 s on a later request; the default, 125.1 s, on the first). The getters return 0 until set; `netsh` shows each set value on the URL group | **Measured** (it was DOC). The documentation holds: a reset, no 408, and the first request keeps the default HeaderWait and IdleConnection, while EntityBody and MinSendRate apply to it. Every timer fires about 5 s late (below) |

## Also found

1. **A pending read waits to fill its buffer** (3c). On Windows for both body kinds, and on Linux for chunked bodies, a
   `ReadAsync(64 KiB)` returned nothing while 1,200 bytes arrived over 2 s, and returned 65,536 bytes only when the
   70,000-byte piece came. The other 5,664 bytes stayed in the next pending read and were lost at `Abort()`. On Linux a
   Content-Length body returns each arrival (1,000, 100, 100, 65,536, 4,464 bytes). So a timer that only restarts when a
   read completes would take a slow but live caller for a stalled one: on Windows always, on Linux for chunked bodies. It
   is also why the server read none of the 1,000 bytes in 3a on Windows and in 3b on Linux.
2. **http.sys's timers fire late.** Every firing fits "the deadline rounded up to a multiple of 5 s, plus 5 s": 120 s
   fired at 125.0 to 125.2 s, 5 s at 9.8 to 9.9 s, MinSendRate at 10,000 bytes/s (a 65,536-byte write plus the head is
   about 6.6 s) at 14.9 s, and at 150 bytes/s (about 437.7 s) at 445.7 s. The MinSendRate firings match a deadline taken
   from the bytes of the pending send, so a whole 8 MB answer in one write would be allowed about 15.5 hours at the
   default, which fits `../probe/`'s W1 (held through 200 s); that one was not run to its end here.
3. **On Windows a reset discards what the caller had not read.** In 1b, 11,851 to 15,808 bytes of writes had completed
   before the stall, yet the caller read no bytes once the RST came. Every Windows caller that read after a timer or `Abort()` got
   nothing but the RST. On Linux the same callers read everything sent before the FIN.
4. **The 200 that disposal sends a mid-head caller on Linux ends with the last chunk twice** (`0` CRLF CRLF `0` CRLF
   CRLF). A client that keeps the connection would read the extra 5 bytes as the start of the next response. The 408
   that `Abort()` writes (3a, 3b) has it once.
5. **After a Content-Length `Abort()` on Linux the kernel keeps sending.** The process no longer holds the socket, but
   about 2.3 MB stays in its send queue in FIN_WAIT1, and the caller could still read it all afterwards.
6. **One last write hides the request from `netsh` on Windows.** In 2a http.sys reported no active request while the
   single 16 MB write was still pending (the request stops being active once its last write starts), and that write
   ended with 995, not the 1236 of 2b (cancelled through `CancelIoEx`, SOURCE section 3.4).

## Running it

Windows: `dotnet run abort-stall-probe.cs > abort-stall-windows.txt`.

Linux, from Git Bash on this machine, through the podman WSL machine as in
`../../PROXY_TAP_HANG_UP_PLAN.harness/README.md` (`MSYS_NO_PATHCONV=1` before each `wsl`, and `tar -xzm` so the copy is
stamped as new):

```sh
tar --force-local -czf k173l.tgz abort-stall-probe.cs
wsl -d podman-machine-default -u root -- sh -c "mkdir -p /root/k173l /root/nuget173l && tar -xzmf /mnt/c/<path>/k173l.tgz -C /root/k173l"
wsl -d podman-machine-default -u root -- sh -c "podman run --rm --name k173l-probe -v /root/k173l:/w \
  -v /root/nuget173l:/root/.nuget/packages -w /w mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run abort-stall-probe.cs > /root/k173l/abort-stall-linux.txt"
```

Then copy the output out and remove both directories (`wsl ... --cd /root -- rm -rf k173l nuget173l`).
