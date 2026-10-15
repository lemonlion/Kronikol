# Harness for `TCPTAP_FAILED_CONNECT_PLAN.md`

Measured on 2026-10-14 against Kronikol 4.20.2 from nuget.org (`origin/main` at `3240c983`): on Windows 11 26200 with
.NET 10.0.11 and on Ubuntu 24.04.5 in `mcr.microsoft.com/dotnet/sdk:10.0` with .NET 10.0.12. Every probe is a
file-based app that names its packages with `#:package`: `dotnet run <file>.cs`, the same file on each OS.

| Path | What it is |
|---|---|
| `connect/connect-probe.cs` | A Redis tap whose upstream connect fails each way it can: A, refused (a port bound and never listening), by `127.0.0.1` and by `localhost`, with `ConnectTimeout` 1.5 s and 5 s; B, no answer to the SYN (a listener whose queue is full; TEST-NET-1 `192.0.2.1`); C, a host name that does not resolve; D, a `DecoderFactory` that throws while the upstream is healthy (D0 is the same tap with the Redis decoder); E, the tap disposed while its connect waits. Per case: what the client heard and when, the tap's counters, its `Log` lines, its `OnCaptureDegraded` events and its `Diagnostics()`. F times `localhost` on each side of a tap. G is `TcpClient.ConnectAsync` alone: what each failure throws, and whether the tap's own token being cancelled throws anything different |
| `connect/connect-{windows,linux}.txt` | Its output (plan §2.2, §3.2, §3.4) |
| `accept/accept-probe.cs` | The two taps that run their own accept loop, `TcpTap` and `OtlpTap`, and `ProxyTap`, whose `HttpListener` accepts for it: E0, a bare `TcpListener` whose loop records what `AcceptTcpClientAsync` throws under 1,000 clients that connect and reset at once; E1, 15 rounds of 200 such clients against each tap, each round followed by a liveness check of up to three tries; E2 (Linux), the process out of descriptors (`setrlimit` lowers `RLIMIT_NOFILE`, `/dev/null` handles take the rest) at the moment a client arrives, then every descriptor given back, with a bare listener alongside whose loop keeps going |
| `accept/accept-{windows,linux}.txt` | Its output (plan §3.1) |

## Notes on the runs

- The first version of `accept-probe.cs` checked a tap's liveness once per round and never again once a check had
  failed. Its first Windows run printed `OtlpTap STOPPED ANSWERING` after round 1, then "dead" for every later round
  without checking. That was a check that timed out under the burst's load, not a dead tap. The probe now checks
  every round, up to three times a second apart, and both taps answered in every round of every later run on both
  platforms. The plan cites only the corrected runs.
- A reset in these probes is `LingerState(true, 0)` and then `Socket.Dispose()`, an RST alone. `TcpClient.Dispose()`
  with a zero linger sends a FIN first (`plans/PROXY_TAP_HANG_UP_PLAN.harness/socket/`).
- The client in `connect-probe.cs` has already sent its PING when the tap gives up, so the tap closes a socket with
  unread bytes in it. Windows answers that with an RST, and the client reads `ConnectionReset`. On Linux the client
  reads an end of stream: `TcpClient.Dispose()` shuts the socket down (a FIN) before it closes it.
- `RLIMIT_NOFILE` is lowered only inside the probe's own process, and E2 runs only on Linux.

## Running the Linux half from Windows

Podman's WSL machine (`podman-machine-default`) with the SDK image, as in
`plans/PROXY_TAP_HANG_UP_PLAN.harness/README.md`. Copy the probe into the VM (`/root/k174`), run
`podman run --rm -v /root/k174:/probe -v /root/nuget:/root/.nuget/packages -w /probe mcr.microsoft.com/dotnet/sdk:10.0 dotnet run <file>.cs`,
copy the output back, then remove `/root/k174`. The NuGet cache under `/root/nuget` is shared with other sessions.
