# query-bench — the QUERY_PERF_PLAN.md measurement harness

Manual wall-clock benchmarks for `kronikol query` on a large report. **Not wired into CI** — wall-clock
in CI is noise; this exists so before/after comparisons and any future regression hunt are one command.

## Protocol

- **Same-session medians of 3** (after 1 warmup rep), Release builds, on an otherwise idle machine.
- **Never compare numbers across sessions or machines** — absolute numbers drift with machine load,
  hardware and runtime version. Every change must be measured against a baseline taken in the same
  session; record machine, OS and date per row.
- Perf *properties* (opens-per-command, the runtimeconfig pin) are asserted deterministically in
  `Kronikol.Tests` (`QueryCommandTests`, "Perf observables" section) — wall-clock never is.

## Pieces

| Piece | What it measures |
|---|---|
| `gen/` | Synthesizes the benchmark report: 200 scenarios × 60 request/response pairs (24,000 interaction entries), every body distinct (dedup wins nothing), responses ~8 KB — ≈140 MB. |
| `bench.ps1` | CLI wall-clock: `summary` (the index-only floor), `values --path $.status`, `grep 4173 --number --count`. |
| `internals/` | Per-stage steady-state (warmed, in-process): scan time+alloc, raw-read-all, parse-all, path-eval — via the `InternalsVisibleTo("Kronikol.Tests")` grant (`AssemblyName` trick). |
| `retok/` | BCL-only minimal re-tokenization of the same file (property names materialized, content strings unescaped + SHA-1-hashed) — the scan's honest floor; scan minus this is walker overhead. |

## Running

```bash
dotnet build src/Kronikol.Tool -c Release
dotnet run -c Release --project tools/query-bench/gen -- tools/query-bench/TestRunReport.query-bench.json
pwsh tools/query-bench/bench.ps1
dotnet run -c Release --project tools/query-bench/internals -- tools/query-bench/TestRunReport.query-bench.json
dotnet run -c Release --project tools/query-bench/retok -- tools/query-bench/TestRunReport.query-bench.json
```

## Record

### 2026-08-26 — the plan's original cost model (dev box A, Windows, 129.7 MB report)

The measurements Part 1 of `QUERY_PERF_PLAN.md` is built on (pre-harness session; preserved from the
plan, not reproducible by this harness byte-for-byte):

| Command | Baseline | With §3.1 alone |
|---|---|---|
| `summary` | 1.86 s | 1.31 s |
| `values --path '$.status'` | 3.63 s | 2.64 s |
| `grep 4173 --number --count` | 4.72 s | 4.28 s |

In-process: scan 0.67 s / 252 MB alloc; minimal re-tokenization 0.35 s; `Raw` over all 23,999 bodies
0.93 s (open-per-body); parse-all 0.37 s; eval `$.status` 0.01 s.

### 2026-08-30 — landing the fixes (dev box, Windows 11, .NET 10.0.300, 142.9 MB report, this harness)

CLI (`bench.ps1`, medians of 3, same session throughout):

| Command | Baseline | +§3.1 tiering | +§3.2/§3.3 shared handle | +§3.4 walker diet |
|---|---|---|---|---|
| `summary` | 1.39 s | 1.18 s | 1.21 s | 1.19 s |
| `values --path '$.status'` | 2.89 s | 2.36 s | 1.98 s | 1.95 s |
| `grep 4173 --number --count` | 4.53 s | 3.34 s | 2.79 s | 2.64 s |

Overall: `summary` −14%, `values` −33%, `grep --number` −42%. Every §4.4 expectation met: §3.1
reproduced its measured column, §3.2/§3.3 removed ~0.4 s from `values` and ~0.55 s from `grep`
(payload opens per command: 24,000+ → 1, pinned by test).

Internals (same session): scan 0.63 s / 359 MB before §3.4 → 0.60 s / 324 MB after; raw-all over one
handle 0.48 s; parse-all 0.14 s; eval `$.status` 0.01 s. Minimal re-tokenization: 0.60 s — after
§3.4 the scan sits at its re-tokenization floor.
