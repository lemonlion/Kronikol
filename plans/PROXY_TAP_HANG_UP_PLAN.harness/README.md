# Harness for `PROXY_TAP_HANG_UP_PLAN.md`

Measured on 2026-10-13 at Kronikol 4.18.1 (`origin/main` at `34145c44`): on Windows 11 26200 with .NET 10.0.11
(`HttpListener` is http.sys) and on Ubuntu 24.04.5 in `mcr.microsoft.com/dotnet/sdk:10.0` with .NET 10.0.12 (the
managed `HttpListener`, as on macOS). Every probe is a file-based app: `dotnet run <file>.cs`.

| Path | What it is |
|---|---|
| `probe/probe.cs` | The issue's repro widened to five paths against `Kronikol.Extensions.ProxyTap` 4.18.1 from nuget.org: A, a reset while the tap writes an 8 MB answer; B, a reset mid-body; C, a half-close mid-body with the caller still reading; D, an upstream (a raw socket) that resets after the caller hung up; E, 20 keep-alive requests. Per path it prints the tap's counters, its `proxy-tap error` lines, the managed listener's tables (`_listenerContexts`, `_connections`, by reflection), on Linux the socket descriptors no TCP table lists any more, on Windows http.sys's active requests for the tap's queue, and what a half-closed caller hears |
| `probe/make-local.sh` | Writes `probe-local.cs`, the same probe against the worktree's ProxyTap project (`#:project`) instead of the package, to measure the prototype |
| `probe/probe-4.18.1-{linux,windows}.txt` | 4.18.1: Linux holds 10 of 10 connections on A to D, Windows none |
| `probe/probe-prototype-{linux,windows}.txt` | `../PROXY_TAP_HANG_UP_PLAN.prototype.patch` applied: nothing held anywhere, C answered 400 |
| `listener/listener-probe.cs` | A bare `HttpListener`: the exception a body read throws when the caller resets or half-closes; what `Abort()` and `Close()` put on the wire for a caller still listening; the answer write to a reset caller; `Abort()` after `Close()`, twice, on a closed keep-alive response, and after `HttpListener.Close()` |
| `listener/listener-{linux,windows}.txt` | Its output on each platform (plan §2.3) |
| `otlp/otlp-probe.cs` | `Kronikol.Extensions.Otlp` 4.18.1: O1, forwarding on, an exporter that resets before the collector's answer; O2, a chunked export whose exporter half-closes before the last chunk, and one with a size line that is not hex |
| `otlp/otlp-4.18.1-{linux,windows}.txt` | Its output (plan §2.4) |

## Running the Linux half from Windows

The podman WSL distro with the SDK image (the recipe in the session notes of 2026-09-16): ship the tree in, extract with
`tar -xzm` (plain `-xz` keeps Windows mtimes and MSBuild then runs a stale build), run in the container, copy the output
back, and remove `/root/k172` and the NuGet cache when done.

```sh
git ls-files > files.txt; git ls-files --others --exclude-standard plans/PROXY_TAP_HANG_UP_PLAN.harness >> files.txt
tar --force-local -czf k172.tgz -T files.txt
wsl -d podman-machine-default -u root -- sh -c "mkdir -p /root/k172/repo && tar -xzmf /mnt/c/<path>/k172.tgz -C /root/k172/repo"
wsl -d podman-machine-default -u root -- sh -c "podman run --rm -v /root/k172/repo:/repo -v /root/nuget172:/root/.nuget/packages \
  -w /repo/plans/PROXY_TAP_HANG_UP_PLAN.harness/probe mcr.microsoft.com/dotnet/sdk:10.0 dotnet run probe.cs 10"
```

The same container runs the plan's managed facts red on `34145c44` (`dotnet test tests/Kronikol.Tests.ProxyTap`), since
this Windows machine cannot: http.sys never holds the connection.
