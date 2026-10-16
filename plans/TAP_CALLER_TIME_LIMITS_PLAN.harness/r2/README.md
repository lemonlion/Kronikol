# R2's proofs (4.25.0)

R2 was written in a worktree of its own (`C:/Code/Kronikol-173-r2`, branch `release/173-r2`, from `origin/main` at
`c7eef4b3`, 4.23.0, rebased before its release onto `12839e21`, 4.24.0 with its plan log and R1's; 4.24.0 changed
nothing under `Otlp`). Windows is Windows 11 26200 with .NET 10; Linux is `mcr.microsoft.com/dotnet/sdk:10.0` under podman's WSL
machine. `OtlpTap` reads and writes raw sockets, so no listener differs between the two.

| Path | What it is |
|---|---|
| `red2_stubs.py`, `red2-stubs.diff` | R2's seams stubbed onto 4.23.0 so its facts compile there: the options with no default and no check, `CallerTimeouts` that stays 0, the clock seam (fed to the accept loop's clock, the one the tap reads today), the debugger seam, no watch and the phase names; `StallWatch.cs` linked for its types alone. The test project, `RawExporter`, the shared `HeldCollector` and the new facts are R2's as they are |
| `red-4.23.0-windows.txt`, `red-4.23.0-after-the-limit-windows.txt` | R2's facts on that tree: 36 of 37, then the two cases of the after-the-limit theory added later, so 38 of the 39 fail. The one that passes is `A_slow_steady_export_is_received`, a guard. Most fail for want of the watch they order their steps by (no entry appears, or `Watch` is null), so the mutations prove them; the real-clock stalled head fails on its behaviour (4.23.0 answered nothing within 15 s), as do the option facts and the registration fact |
| `mutations_r2.py`, `mutations-windows.txt` | 23 mutations, run with R0's runner over the Otlp suite's `OtlpTap` facts: the plan's (count a keep-alive close, watch the forward, count a stalled drain as a `413`, write the relayed answer in one piece) and R2's own (the keep-alive wait timed by the stall limit or by nothing, the head's first byte unmarked, reads recording no progress, a body or an answer that ends after its limit fired treated as whole, no `408` or a kept-alive one, a limit that cancels nothing, the answer unwatched, an answer stall counted as a sending one, an entry left behind, limits under a debugger, and the options' checks, defaults and copies) |
| `linux-r2.sh`, `linux-summary.txt` | The Otlp suite on Linux |
| `full-windows-*.txt`, `checks-windows.txt` | The Otlp suite on Windows, and the core suite and the Release build |

## What the run found

- Plan §5's W-1, the watch's own facts, were never written in R1: no fact makes a `StallWatch` of its own. R3 adds
  them (`StallWatchTests` in `Kronikol.Tests.ProxyTap`), with its own changes to the watch.
- Python's `Path.write_text` on Windows writes CRLF; one test file went in with CRLF before it was caught (`git ls-files
  --eol`), so every script in this harness writes bytes.
