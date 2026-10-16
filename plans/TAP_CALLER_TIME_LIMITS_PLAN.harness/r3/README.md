# R3's proofs (4.26.0) and the patch that followed it (4.26.1)

R3 was written in a worktree of its own (`C:/Code/Kronikol-173-r3`, branch `release/173-r3`, from `origin/main` at
`769c55cd`, 4.23.0 with 4.24.0 and R1's plan log, rebased before its release onto `a434e2f6`, 4.25.0 with R2's plan log).
4.26.1 was made in the same worktree on top of 4.26.0 (`a3b20f99`). Windows is Windows 11 26200 with .NET 10; Linux is
`mcr.microsoft.com/dotnet/sdk:10.0` under podman's WSL machine.

| Path | What it is |
|---|---|
| `red3_stubs.py`, `red3-stubs.diff` | R3's seams stubbed onto 4.25.0 so its facts compile there: the options with no default, no check and no copy, counters that stay 0, the clock seam (fed to the accept loop's clock) and the debugger seam, no watch, the phase names, and, as the plan's R3-6 asks, the pump's stamp and the old reaper's wall clock read through the clock seam's `GetUtcNow`. The test project, `TcpTapStallTests` and the stop-line fact R3 updates are R3's; `StallWatchTests` is not, since it tests R1's watch and R3's new API on it |
| `red-4.25.0-windows.txt` | R3's facts and the stop-line fact on that tree: 24 of 26 fail. The two that pass are guards, a slow steady reader and the reaper under a debugger. The real-clock half-close (held), the wall-clock jump (the old reaper reaped the command at the jump), the disposal (the old reaper's one-second tick), the options, `CopyTo` and the stop line fail on their behaviour; the rest for want of the watch they order their steps by |
| `red-4.25.0-steady-reader-rerun-windows.txt` | The steady-reader guard as changed after the Linux run (below), on the same tree: it passes, as a guard does |
| `mutations_r3.py`, `mutations-windows.txt` | 20 mutations, run with R0's runner over the TcpTap suite and, for the watch, `StallWatchTests`: the plan's five (the half-close armed on an idle pair, the reaper reading the wall clock again, the half-close timed from the FIN rather than the last byte, the reaper's tick given a delay of its own again, a write's pending mark never cleared) and R3's own (a write unwatched, a close counted under the other limit, a limit that closes nothing, the reaper standing down under a debugger, a segment stamped by the machine's clock, every entry or no entry acting under a debugger, a condition entry always due, a throwing check left watched, and the options' checks, defaults and copies). All 20 are killed |
| `linux-r3.sh`, `linux-summary.txt`, `linux-summary-2.txt` | The TcpTap, ProxyTap and Otlp suites on Linux (all three compile the watch; `SUITES` picks fewer), then the TcpTap suite again after the fix below |
| `full-<suite>-windows-<n>.txt` | The suites on Windows, in order. TcpTap: the first run failing the stop-line fact R3 updates (1), then passing before and after the rebase (2 to 4); ProxyTap and Otlp before the rebase (1, the Otlp suite without R2) and after it (3); `full-ProxyTap-Otlp-windows-final.txt` after the bump |
| `checks-windows.txt` | The core suite and the Release build after 4.26.0's bump |

## What the run found

- Plan §5's W-1, the watch's own facts, were never written in R1. `StallWatchTests` (11 facts in `Kronikol.Tests.ProxyTap`)
  covers R1's behaviours and R3's additions to the watch.
- On Linux the first run of `A_slow_steady_reader_keeps_its_pair`, whose upstream sent its FIN after an 8 MB reply,
  counted a half-closed pair closed: Linux's loopback buffers took the tap's last write while the client still had about
  2 MB to read, and from then on the client sent nothing for longer than the limit. That is the rule as the plan states
  it (only what the other side sends is its progress; bytes the tap has passed on are not), and the close was harmless:
  the tap's side had nothing unread, so the client still got every byte. The fact was meant to guard the stalled-write
  limit, as the plan's R3-5 does, so the upstream now keeps its side open; the changelog says what counts as progress.
- An existing fact, `TheStopLineNamesFailedConnectsAndFailedAccepts`, pinned the stop line's old end; it now names the
  two new counters, and R3's half-close and stalled-write facts check the line with non-zero counts.

## 4.26.1

4.26.0's CI run (38831706828) failed two cases of R1's `A_caller_that_stalls_in_its_body_is_cut_and_not_forwarded` on a
loaded runner, at `Assert.Equal(1, tap.CallerTimeouts)`: R1's `CutStalled` ended the caller's response, which lets the
caller go, and only then counted the cut, so a fact (or a host) that read the counter as soon as the caller was gone could
read 0. In 4.26.1 `CallerAnswer.TimeOut` takes an action that runs once the cut has won and before the response is
aborted, and `CutStalled` counts in it. The audit of 4.26.0 against its changelog found a sentence no fact checked, that a
pair a limit closes raises no `OnCaptureDegraded` event, and `DiagnosticEntry.CaptureDegraded`'s summary naming only a
caller; 4.26.1 carries both.

| Path | What it is |
|---|---|
| `red-4.26.0-count-order-stub.diff`, `red-4.26.0-count-order-windows.txt` | The patch's seam stubbed onto 4.26.0 with its action run after the abort, where 4.26.0 counted: `A_cut_is_counted_before_the_caller_is_let_go` fails there ("the caller was let go before the cut was recorded") |
| `mutation-no-degradation.diff`, `mutation-no-degradation.txt` | 4.26.1 with a mutant that raises `OnCaptureDegraded` when a limit closes a pair: `A_pair_a_limit_closes_raises_no_capture_degradation` fails on it, both closes having raised the event |
| `full-TcpTap-windows-5.txt`, `linux-summary-3.txt` | The TcpTap suite with that fact on Windows (470, 4 skipped) and Linux (469, 5 skipped) |
| `patch-ProxyTap-windows.txt`, `patch-linux-summary.txt` | The ProxyTap suite with the fix on Windows (189, 4 skipped), and the ProxyTap (193) and TcpTap (469, 5 skipped) suites on Linux |
| `checks-4.26.1-windows.txt` | The core suite (7,645, 2 skipped) and the Release build after 4.26.1's bump |
