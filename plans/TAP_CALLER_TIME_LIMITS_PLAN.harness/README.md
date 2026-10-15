# Harness for `TAP_CALLER_TIME_LIMITS_PLAN.md`

Written on 2026-10-14 at Kronikol 4.20.2 (`origin/main` at `3240c983`), and swept on 2026-10-15 (`sweep/`). Probes run against the published 4.20.2
packages from nuget.org, or a bare `HttpListener`, on Windows 11 26200 (where `HttpListener` is http.sys) and on Ubuntu in
`mcr.microsoft.com/dotnet/sdk:10.0` (the managed `HttpListener`, as on macOS). Every probe is a file-based app:
`dotnet run <file>.cs`. Each output sits beside its probe as `*-windows.txt` and `*-linux.txt`, with the runtime and OS
at the top.

| Path | What it is |
|---|---|
| `read/proxytap.md` | `ProxyTap` read for the caller's side: the accept loop, the body read, the forward, the answer write, `CallerAnswer`, options, `Diagnostics()`, the tests (READ, line numbers at `3240c983`) |
| `read/otlptap.md` | `OtlpTap` and `HttpRequestReader`: what a stalled or idle exporter holds, every read and its token, the secret check, options, counters, the tests |
| `read/tcptap.md` | `TcpTap`: the two pumps and the pair's release, the reaper, options, counters, five states of a pair, and what the reading found broken (§9) |
| `source/dotnet-listener-timers.md` | .NET's `HttpListener` on `release/10.0` (`591ede8`) and `main` (`56ba1c0`): the managed listener's only timer, `TimeoutManager` on each side, http.sys's timers and defaults, what `Abort()` does to pending I/O, the open runtime issues; its §8 lists the claims inferred from source, which `listener/` measures |
| `source/prior-art.md` | How Kestrel (`v10.0.13`), YARP, nginx, HAProxy, Envoy, socat, Toxiproxy, mitmproxy and WireMock bound a stalled peer, their defaults, Kestrel's debugger rule, and .NET socket cancellation |
| `probe/` | The three taps at 4.20.2 under stalled callers and peers, and `ProxyTap` with an infinite `ForwardTimeout` (its own README has the table) |
| `listener/` | A bare `HttpListener`: `Abort()` against a stalled write and a stalled read, disposal with a caller mid-head, the managed listener's own timer, http.sys's timers (its own README has the table) |
| `sweep/` | The sweep of 2026-10-15: notes on the plan's citations of the code, the repository and the sibling plans, outside sources, and an adversarial review of the design; and runs of an answer sent before the body is read, an answer after a cancelled read (on .NET 8 too), http.sys's send-rate timer per piece, the BCL's duration limits and three facts about the code (its own README has the tables) |
| `r0/` | R0's proofs (4.22.2): the facts on 4.22.1 with two seams stubbed, on both platforms; 44 mutations; the released code's runs; what the run found (its own README) |

## Running the Linux half from Windows

Podman's WSL machine (`podman-machine-default`) running the SDK image, as `PROXY_TAP_HANG_UP_PLAN.harness/README.md`
describes: ship the probe directory in, extract with `tar -xzm` (plain `-xz` keeps Windows mtimes and MSBuild then runs a
stale build), run in the container with a NuGet cache of its own, copy the output back, and remove the directories and the
cache when done. Write the shell script with LF line endings to a file and run it with
`MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/<path>/run.sh`.
