# Probes for `TAP_CALLER_TIME_LIMITS_PLAN.md` (#173)

Measured on 2026-10-14 against the published 4.20.2 packages (`#:package Kronikol.Extensions.*@4.20.2`): on Windows 11
26200 with .NET 10.0.11 (SDK 10.0.300), where `HttpListener` is http.sys, and on Ubuntu 24.04.5 in
`mcr.microsoft.com/dotnet/sdk:10.0` with .NET 10.0.12 (SDK 10.0.401), where it is the managed implementation (as on
macOS). Every probe is a .NET 10 file-based app, `dotnet run <file>.cs [args]`; the defaults are what was run.

Each case runs 3 concurrent callers (OTLP's B1, 10) and is sampled every 15 s. A caller that may read keeps one receive
pending, so a FIN, a reset or an answer is timed when it arrives; both ends of every connection are also read from the
TCP table once a second, the only view of a caller that never reads. Times are seconds after the caller's stall began.
Each probe ends by disposing each tap, timed, and looking again.

| Probe | What it does |
|---|---|
| `proxytap-stall-probe.cs` | ProxyTap, one tap per case, one raw-socket upstream counting requests per path, sampled to 200 s. H0 connects and sends nothing; H1 sends a request line and Host, no blank line; B1 a whole head with `Content-Length: 100000` and 1,000 body bytes, then stalls with no FIN; W1 a whole request for an 8 MB answer, never read (receive buffer 16 KiB); K1 one small exchange read whole, then idle on keep-alive. Per sample: what each caller heard and when, both ends' TCP state, the tap's `_inflight`, the upstream's count; on Linux the listener's `_listenerContexts` and `_connections` (with each connection's `_timeout`) and the endpoint listener's `_unregisteredConnections`, on Windows the request queue's counters and timeouts from `netsh http show servicestate view=requestq` |
| `otlp-stall-probe.cs` | OtlpTap, one tap per case, sampled to 200 s. H1 a partial head; B1 ten exporters, each declaring `Content-Length` = `MaxRequestBytes` (32 MiB) and sending 10 body bytes, with the process's memory (GC heap, GC committed, working set, private bytes) before, after and at every sample; K1 one export (JSON `{}`) read whole, then idle on keep-alive |
| `tcptap-stall-probe.cs` | TcpTap, one tap and one raw upstream per case, sampled to 90 s. T1 the client sends 7 bytes then `Shutdown(Send)`, the upstream reads and stays open and silent; T2 the upstream half-closes on accept, the client stays open and silent; T3 the upstream writes 8 MB, the client never reads (receive buffer 16 KiB); T4 an idle pair (control). T1 to T4 use the default options (reaper off) and a decoder that does nothing, since a plain TcpTap requires a `DecoderFactory`. T3N repeats T3 with `ReapStuckConnectionsAfter` = 20 s and the same decoder; T3R repeats it through `RedisTap` (a RESP `GET` answered by an 8 MB bulk string), because the reaper only reads `IProtocolDecoder.OldestUnansweredSince`, which the Redis and Mongo decoders report. Per sample: `LiveConnections` and the byte counters, both ends' TCP state for each socket, what each reading side saw, the bytes the upstream managed to write |
| `forward-timeout-infinite-probe.cs` | ProxyTap in front of an upstream that answers 20 s after each request, 3 concurrent callers per case: `ForwardTimeout` = `Timeout.InfiniteTimeSpan` and 30 s (control), then `TimeSpan.Zero`, -5 s and -10 s, none of which `Validate` rejects. Prints the tap's `HttpClient.Timeout` (by reflection), each caller's status, time and body, the upstream's count, the counters, `Diagnostics()` and the log |

Output files (each opens with the `dotnet --info` SDK, OS and Host lines):

- `proxytap-stall-probe-windows.txt`: Windows, http.sys.
- `proxytap-stall-probe-linux.txt`: Linux, managed listener.
- `otlp-stall-probe-windows.txt`: Windows.
- `otlp-stall-probe-linux.txt`: Linux.
- `tcptap-stall-probe-windows.txt`: Windows.
- `tcptap-stall-probe-linux.txt`: Linux.
- `forward-timeout-infinite-probe-windows.txt`: Windows.
- `forward-timeout-infinite-probe-linux.txt`: Linux.

## Results

"Held" means the connection was still open, and the tap or listener still held it, at the last sample. No ProxyTap or
OtlpTap caller heard a byte before disposal: every release before it was an RST, with no answer.

| Case | Windows | Linux |
|---|---|---|
| ProxyTap H0, nothing sent | RST at 125.2 s (http.sys timeouts are 120 s) | RST at 90.0 s. Until then it sits in `HttpEndPointListener._unregisteredConnections` (`_timeout` 90000), not in `_listenerContexts` or `_connections` |
| ProxyTap H1, partial head | RST at 125.2 s | held through 200 s, still in `_unregisteredConnections` with `_timeout` 90000 that never fires. At disposal: `HTTP/1.1 200 OK`, then FIN |
| ProxyTap B1, body stalled | RST at 125.2 s; the tap's in-flight drops to 0 with nothing counted or logged | held through 200 s, in-flight 3. At disposal each caller hears `HTTP/1.1 200 OK`, then FIN, one per second; `DisposeAsync` takes 3,028 ms |
| ProxyTap W1, answer never read | held through 200 s, in-flight 3 (http.sys counts 0 active requests). After disposal: RST, the 16 KiB buffered is lost | held through 200 s, in-flight 3. At disposal the tap's side goes FIN_WAIT1; the caller then reads 2,367,488 of 8,388,608 B, then FIN |
| ProxyTap K1, idle keep-alive | RST at 125.1-125.2 s | RST at 15.0 s (`_timeout` 15000) |
| OtlpTap H1, partial head | held through 200 s; FIN within 2 s of disposal | same |
| OtlpTap B1, 10 x 32 MiB declared, 10 B sent | held through 200 s. GC heap +322 MiB after a full collection, private bytes +330 MiB, working set +5 MiB | held through 200 s. GC heap +321 MiB after a full collection, working set +8 MiB |
| OtlpTap K1, idle keep-alive | held through 200 s | same |
| TcpTap T1, client FIN, upstream silent | held through 90 s, `LiveConnections` 3. Client FIN_WAIT2, tap CLOSE_WAIT; the upstream got the FIN at 0 s | same |
| TcpTap T2, upstream FIN, client silent | held through 90 s. The client got the FIN at 0 s and the pair stays | same |
| TcpTap T3, client never reads | held through 90 s. The upstream wrote 393,216 to 786,432 of 8,388,608 B, then blocked; the tap forwarded 98,304 B per pair | held through 90 s. The upstream wrote 4,784,128 to 5,242,880 B, then blocked; the tap forwarded about 2,350,000 B per pair |
| TcpTap T4, idle control | open through 90 s | same |
| TcpTap T3N, reaper on, no decoder | held through 90 s, never reaped. `DisposeAsync` 540 ms, against 31 ms or less without the reaper | same; `DisposeAsync` 954 ms |
| TcpTap T3R, reaper on, RedisTap | reaped at 20 s. `LiveConnections` 3 to 0; the upstream gets an RST; the client hears nothing (its tap side sits in FIN_WAIT1 behind the unread reply) | reaped at 20 s. The upstream gets a FIN and its next write fails (`Shutdown`); the client hears nothing |
| Forward, `ForwardTimeout` infinite | 502 at 10.1 s. Body says "could not connect to ... within ConnectTimeout (00:00:05). Caused by: ... HttpClient.Timeout of 9.999 seconds elapsing", yet the upstream had all 3 requests. `ForwardFailures` 3, recorded as failed calls | same |
| Forward, 30 s (control) | 200 at 20.1 s | 200 at 20.0 s |
| Forward, `TimeSpan.Zero` | 502 at once ("no answer ... within ForwardTimeout (00:00:00)"); upstream saw nothing | same |
| Forward, -5 s | 502 at once ("Specified argument was out of the range of valid values. (Parameter 'delay')"). Counted as `ForwardFailures` 3 but "3 were not recorded" | same |
| Forward, -10 s | `StartAsync` throws `ArgumentOutOfRangeException` (an `HttpClient.Timeout` of zero) | same |

Also measured:

- The netsh view shows the http.sys session timeouts: entity body, drain entity body, request queue, idle connection and
  header wait all 120 s, and a minimum send rate of 150 B/s. The `TimeoutManager` getters read 0 on both listeners.
- `DisposeAsync` ends every held connection on both platforms: the caller sees a FIN or an RST, or, for one that is not
  reading, the tap's side closes behind the unread data (FIN_WAIT1). It took 31 ms at most, except Linux B1 (3,028 ms,
  the FINs a second apart) and TcpTap with the reaper on (540 and 954 ms; the reaper's 1 s tick takes no token,
  `read/tcptap.md` F8).
- Linux only: TcpTap's stall heuristic adds a `Diagnostics()` line to T3, T3N and T3R ("7,051,776 B have flowed since the
  last recorded interaction"). On Windows less than its 1 MiB passed.

## Running the Linux half from Windows

This uses the hang-up plan's runner: podman's WSL machine (`podman-machine-default`) and the SDK image. Only `probe/`
goes in; extract with `tar -xzm`, keep a NuGet cache outside the container, and run the probes side by side:

```sh
tar --force-local -czf k173-probe.tgz probe/*.cs    # from plans/TAP_CALLER_TIME_LIMITS_PLAN.harness
wsl -d podman-machine-default -u root -- sh -c "mkdir -p /root/k173 /root/nuget173 && tar -xzmf /mnt/c/<path>/k173-probe.tgz -C /root/k173"
wsl -d podman-machine-default -u root -- sh -c "podman run --rm -v /root/k173:/k173 -v /root/nuget173:/root/.nuget/packages \
  -w /k173/probe mcr.microsoft.com/dotnet/sdk:10.0 dotnet run proxytap-stall-probe.cs"
```

From Git Bash, prefix `MSYS_NO_PATHCONV=1` and pass the commands as a script file with LF endings. Remove `/root/k173` and
`/root/nuget173` when done (`wsl ... --cd /root -- rm -rf k173 nuget173`).
