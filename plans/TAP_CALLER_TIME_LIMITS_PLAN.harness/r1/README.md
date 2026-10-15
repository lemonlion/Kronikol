# R1's proofs (4.23.0)

R1 was written in a worktree of its own (`C:/Code/Kronikol-173-r1`, branch `release/173-r1`, from `origin/main` at
`9b7886d9`, 4.22.2, rebased onto `609ba8f3`, 4.22.3, before its release). Windows is Windows 11 26200 with .NET 10
(http.sys); Linux is `mcr.microsoft.com/dotnet/sdk:10.0` under podman's WSL machine (the managed `HttpListener`).

| Path | What it is |
|---|---|
| `red1_stubs.py`, `red1-stubs.diff` | R1's seams stubbed onto 4.22.2 so its facts compile there: the option with no default and no check, `CallerTimeouts` that stays 0, the clock and debugger seams, no watch, one piece for any answer, a `CallerAnswer` that never times out; `StallWatch.cs` linked for its types alone (the diff leaves out that untracked file) |
| `red-4.22.2-final-windows.txt` | R1's facts as released, on that tree: 27 of 39 fail. The 12 that pass are the steady bodies, no limit as null or infinite, six answer lengths a single piece already serves, the race R1-7 (proven by mutation, P9) and an older registration fact. Many fail for want of the watch they order their steps by; the mutations prove those |
| `red-4.22.2-proxytap-windows.txt` | The first such run, before R1-2 was rewritten and before R1-1 and R1-3 checked `Diagnostics()`: 26 of 38 |
| `probe-dispose-race-4.22.2-windows.txt`, `red-4.22.2-dispose-race-windows.txt` | A tap disposed straight after it starts, on 4.22.2: 61, then 4, of 400 disposals threw `ArgumentException` ("The handle is invalid"); with the fact's 1,000 rounds, 123 (in the final run above) |
| `mutations_r1.py`, `mutations-windows-part1.txt`, `mutations-windows-part2.txt` | 20 mutations, run with R0's runner. The first run was stopped after P10 so a peer's Playwright run had the machine; it had left P4 alive, a body read the limit raced, which `TryBeginForward` refused anyway, so R1-2 now holds the limit's action with a seam (`StallWatch.BeforeAct`) while the read ends. Part 2 is P4 again, P11, D1 and O1 to O3 |
| `linux_mutants.py`, `linux-r1.sh`, `linux-r1-mutants.sh`, `linux-summary.txt`, `linux-mutants.txt` | The ProxyTap suite on Linux, and the two mutants only the managed listener can show: P7 (no `408`) and P8 (the answer sent chunked). The first Linux mutant run tested stale binaries (the overlaid file was older than the tree's build output, so MSBuild skipped the build), so `linux-r1-mutants.sh` touches each overlaid file; both are killed |
| `linux-final.txt`, `checks-windows.txt` | The ProxyTap and TcpTap suites on the released tree on Linux, and on Windows with the core suite and the Release build |

## What the run found

- `ProxyTap.DisposeAsync` threw `ArgumentException` on Windows for a tap disposed straight after it started: the accept
  loop's first wait began while the listener closed. Since R0 the loop has waited for the listener's close rather than
  the tap's token, which widened the window. Fixed in 4.23.0; `Disposing_a_tap_straight_after_starting_it_never_throws`.
- `-1` ms is `Timeout.InfiniteTimeSpan`, so the refused-values theory uses `-2` and `-1000`.
