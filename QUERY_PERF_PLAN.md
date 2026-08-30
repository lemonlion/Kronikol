# `kronikol query` hot paths — a measured cost model and the fixes it ranks

**Status: EXECUTED 2026-08-30 — shipped in 3.0.69.** §3.1, §3.2, §3.3 **and** §3.4 all landed (the
§3.4 gate was resolved by request: the plan was implemented in full). Harness committed at
`tools/query-bench/` with the same-session before/after record in its README: on a 142.9 MB corpus,
`summary` 1.39→1.19 s, `values` 2.89→1.95 s, `grep --number` 4.53→2.64 s; payload opens per bulk
command 24,000+→1 (test-pinned); after §3.4 the scan sits at its re-tokenization floor. The §5
QUERY_V2 addendum landed in `PLANS_STATUS.md` (QUERY_V2_PLAN.md was completed and deleted 2026-08-30).

The cost model below was fully measured (2026-08-26) — no section of
this plan rests on an unverified hypothesis; three plausible fixes that failed measurement are recorded
in §1.6 so they are not re-proposed.

**Builds on:** `QUERY_V2_PLAN.md` (complete as of 3.0.58). This plan changes no command surface, no
output byte, no report format — it is purely the cost of `kronikol query` on large reports. ~25% of
users have reports over 50 MB; the QUERY_V2_PLAN Part 7 perf row was calibrated on a corpus 250×
smaller (562 interactions, 0.53 MB of bodies) — §5 records the correction there.

**Benchmark subject:** a synthesized 129.7 MB report (200 scenarios, 24,000 interaction entries,
23,999 distinct bodies — request *and* response bodies all unique, the worst case since distinct-body
dedup wins nothing; responses ~6 KB). Timings are same-session medians of 3, warm, Release, Windows.
Absolute numbers drift with machine load between sessions — only same-session comparisons are used
anywhere in this plan, and the committed harness (§4.3) re-derives them in one command.

| Command | Baseline | With §3.1 (tiering) alone — measured | Expected after §3.1+§3.2 |
|---|---|---|---|
| `summary` (index-only; every command's floor) | 1.86 s | 1.31 s | ~1.3 s |
| `values --path '$.status'` | 3.63 s | 2.64 s | ~2.0–2.2 s |
| `grep 4173 --number --count` | 4.72 s | 4.28 s | ~3.3–3.6 s |

---

# Part 1 — The measured cost model

Instruments: CLI medians under controlled JIT modes; a `dotnet-trace` profile of the live command; a
BCL-only re-tokenization benchmark; and an in-process harness calling the tool's real internals
(`ReportScanner`, `BodyCache`, `PathEngine`) with per-stage time and allocation. All committed per §4.3.

## 1.1 Tier-0 JIT is the largest single cost — ~25–30% of every command

Every CLI invocation is a fresh process; the tool's own loop-heavy methods (`Walker.Consume`, the
evaluate loops) run their entire life in unoptimized tier-0 code — a 1–5 s process never reaches
tier-1. Measured: `DOTNET_TC_QuickJitForLoops=0` (methods with loops compile fully optimized
immediately) takes `summary` 1.86 → 1.42 s, `values` 3.63 → 2.99 s, `grep --number` 4.72 → 4.28 s as
an env var, and the same effect ships permanently via the runtimeconfig property — validated by
editing the built `Kronikol.Tool.runtimeconfig.json` directly: `summary` 1.31 s, `values` 2.64 s.
The steady-state comparison (§1.3's in-process scan at 0.67 s vs 1.31 s in the CLI) shows what
remains after the knob is mostly the optimizing JIT's own compile time, paid per invocation —
attackable only by AOT/R2R packaging, which is out of scope (§3.5).

## 1.2 One `File.OpenRead` per distinct body — up to ~0.75 s per bulk command

`PayloadReader.Read` (`src/Kronikol.Tool/Query/PayloadReader.cs:18`) opens the file per call;
`BodyCache.Raw` (`Query/BodyCache.cs:21`) calls it once per distinct body, and grep's loops do the
same directly (`QueryCommand.Search.cs:64`, `QueryCommand.NumberGrep.cs:97`; in-loop diagram reads at
`Search.cs:92`, `NumberGrep.cs:164`). Measured three ways in agreement: open-per-read vs shared-handle
on the raw file (0.55 s vs 0.03 s over 12k reads); ~570 ms inside `OSFileStreamStrategy..ctor` in the
`dotnet-trace` profile; and the internals harness, where `Raw` over all 23,999 distinct bodies costs
0.93 s of which the opens are the bulk. `values` (response-targeted) pays ~half of it; `grep` pays all
of it. Fix: §3.2–§3.3.

## 1.3 The scan's steady-state cost is 0.67 s — mostly honest, ~0.3 s of overhead

In-process, warmed: `ReportScanner.Scan` = 0.67 s, allocating 252 MB. A faithful minimal
re-tokenization of the same file (property names materialized, all 24k content strings unescaped and
SHA-1-hashed — the scanner's real obligations) runs 0.35 s. The ~0.3 s difference is the walker's
bookkeeping (`At(params string[])` allocating per call — 24 call sites; `CurrentKey()`'s
`Index.ToString()` per array element; `GetString()` on every property name where
`ValueTextEquals("…"u8)` is alloc-free) plus the 128 KB-window `BlockCopy` shuffling. A real but
modest target — §3.4, deliberately last.

## 1.4 Honest work, correctly sized

- Unescaping + hashing all body content during the scan: inside the 0.35 s above (SHA-1 itself ~0.1 s).
- `JsonDocument.Parse` of all 23,999 bodies: 0.37 s (the trace agrees: parse frames are ~0.1 s scale).
- Warm process startup: 0.04 s.

## 1.5 Path evaluation is free — the "values residue" dissolved

`PathEngine.SelectAll` over every parsed body: 0.01 s for `$.status`, 0.11 s for `$.items[*].price`.
The full values-shaped loop (per-occurrence truncation check + `Json` + `SelectAll`) adds only ~0.2 s
over fetch+parse. An earlier draft of this plan had ~1.4 s of `values` unexplained; it decomposes
entirely into §1.1 (tier-0) + §1.2 (opens). Nothing in the evaluation layer needs work.

## 1.6 Rejected by measurement — do not re-propose without new evidence

| Hypothesis | Measured | Verdict |
|---|---|---|
| Bytes-first body materialization (`CopyString` + `Parse(bytes)` instead of `Deserialize<string>` + `Parse(string)`) | 0.32 s vs 0.35 s over all bodies | ~30 ms; not worth the complexity (escaping-aware truncation handling, `Raw` contract churn) |
| Scanner body hashing over raw value bytes instead of `GetString`+`GetBytes` | ~0.02 s | noise |
| Walker allocations as the *main* scan cost | ~0.3 s of 0.67 s steady / ~1.3 s CLI | real but third-order; the CLI gap was tier-0 (§1.1), not allocations |
| Parse cost as a target | 0.37 s total | honest work; only parallelism could split it (§3.5) |

---

# Part 2 — Invariants (all of QUERY_V2_PLAN Part 1, plus)

- **Byte-identical output** on every existing test — this plan has no output of its own.
- `BodyCache` remains the owner of per-command payload lifetime: everything it hands out is valid
  until `Dispose`, exactly as documented on the class today.
- Body hashes (`b:` addresses) are stable across this work — they are printed output and cross-run keys.
- No new NuGet dependencies (`Kronikol.Tool.csproj` has none; that stays).
- No wall-clock assertions in the test suite — perf properties are tested via deterministic
  observables (§4); wall-clock is recorded manually via the benchmark harness (§4.3).

---

# Part 3 — The fixes, in value order

## 3.1 Ship optimized JIT for loop methods — one property

`<TieredCompilationQuickJitForLoops>false</TieredCompilationQuickJitForLoops>` in
`Kronikol.Tool.csproj`. This flows into the packed tool's `runtimeconfig.json` as
`System.Runtime.TieredCompilation.QuickJitForLoops: false` — the exact edit already validated by
hand against the built output (§1.1). Effect: loop-bearing methods compile optimized on first call
instead of living in tier-0. Scope: `Kronikol.Tool` only — a short-lived CLI is precisely the profile
this knob exists for; the library packages are consumed by long-lived test processes where default
tiering is right, so they are untouched. Cost: slightly more JIT time on the handful of loopy methods
at startup; measured net win on every command, and `merge`/`ingest` inherit it for free.

## 3.2 `BodyCache` owns one open handle

- `BodyCache` opens one `FileStream` over `index.Path` in its constructor and closes it in
  `Dispose()` (it is already `IDisposable` and already `using`-scoped at every creation site).
- `PayloadReader.Read` gains an overload taking the open stream; the static path-opening overload
  stays for the single-shot call sites (`http`, `body`, `note`, two-body `diff` — one or two opens
  each, not worth churning).
- `ReportIndex` gains an internal counter `PayloadOpens`, incremented wherever a payload
  `FileStream` is created — the deterministic observable the red tests assert on. Instance state,
  not static, so parallel test runs cannot race it.
- The cache's pipeline otherwise stays exactly as it is — cached strings, `Parse(string)` — per §1.6.

## 3.3 Grep's loops route through `BodyCache`

`Grep` (`Search.cs`) and `NumberGrep` iterate `index.Bodies` and read each body themselves — the very
pattern `BodyCache` exists to own ("grep established the rule; the cache makes it automatic"). Both
loops take a `using var cache = new BodyCache(index)` and read via `cache.Raw(hash)` /
`cache.Json(hash)`; the in-loop diagram reads go through a new `cache.ReadSlice(slice)` passthrough on
the same handle. Grep touches every distinct body (23,999 here), so it gains the most from §3.2.
Consolidation as much as perf — one fewer private copy of the read-a-body pattern.

## 3.4 Walker allocation diet — last, and only if the seconds still matter

The ~0.3 s of §1.3, attacked mechanically: property-name dispatch via `ValueTextEquals("…"u8)`
against the closed set of known names instead of `GetString()`-then-`switch`; fixed-arity `At`
overloads (or cached arrays) so the `params` allocation goes; `CurrentKey()` carrying array indices
as integers instead of `ToString()` per element. Every existing test is the behavior pin — the
scanner's output must not change by a byte. Land it as its own commit, separately revertible, and
only after §3.1–§3.3 are measured in: if large-report users are satisfied at ~1.3 s / ~2 s, this
section can stay unbuilt.

## 3.5 Deliberately out of scope — decisions, not omissions

| Excluded | Why, and the trigger to revisit |
|---|---|
| Bytes-first materialization; hash-over-bytes | Measured at noise — §1.6 |
| AOT / ReadyToRun tool packaging | Would remove the ~0.5 s of per-invocation JIT compile §3.1 leaves behind, but costs RID-specific packages and packaging churn for the whole release pipeline. Revisit if sub-second floors become a real demand |
| Parallel body parse/eval | Parse is 0.37 s and evaluation is free (§1.5) — there is no longer a prize worth the determinism care. Revisit only if reports 5× larger become routine |
| Evaluating bodies during the index scan | Same verdict as parallelism |
| Sidecar cache (persisted index; SQLite/DuckDB as an implementation detail) | Removes the scan floor entirely, at the price of invalidation, a persisted format, read-only-directory handling. Gated on evidence of routine 250 MB+ reports — see §5 |
| SQL query surface | Considered and declined 2026-08-26 alongside the M8 `select` no-go reasoning (bodies are stringified JSON in `content`; no budget/elision contract in SQL results; worse error messages; flagship `grep --number` inexpressible). Record lives in §5's addendum |

---

# Part 4 — TDD detail and measurement

## 4.1 Order of work

1. **§3.1** (tiering property) with its guard test; measure via the harness.
2. **Red tests + §3.2/§3.3** (shared handle, grep consolidation); measure.
3. Record the after-table (§4.4); docs and release (§5).
4. **§3.4** only if warranted then — its own cycle, its own measurement.

## 4.2 Red tests

Perf properties are asserted on deterministic observables — never on wall-clock. Unit-level tests
drive internals directly (`InternalsVisibleTo` already grants `Kronikol.Tests` access): build a report
via the existing `Write(...)` helper in `QueryCommandTests`, scan it with `ReportScanner`, exercise
`BodyCache` against the resulting `ReportIndex`.

- `Tool_runtimeconfig_pins_optimized_jit_for_loops` — reads the `Kronikol.Tool.runtimeconfig.json`
  beside the tool's built assembly and asserts the
  `System.Runtime.TieredCompilation.QuickJitForLoops: false` property — the guard that keeps the
  §3.1 csproj line from being silently lost. (Red before the property is added.)
- `BodyCache_opens_the_file_once_for_many_distinct_bodies` — read `Raw`+`Json` for every hash in a
  multi-body index; assert `PayloadOpens == 1`. (Red today: equals the distinct-body count.)
- `Grep_opens_the_file_once_across_bodies_and_diagrams` · `NumberGrep_opens_the_file_once` — same
  counter after routing the loops through the cache; the existing CLI-level grep tests pin that
  output is unchanged.
- `BodyCache_survives_a_missing_or_malformed_slice` — the null/fallback contracts of
  `PayloadReader.Read` hold on the new overload.
- §3.4, if built, adds no new observable — the entire existing suite is its pin.

Existing suite green throughout — it is the byte-identical-output guarantee.

## 4.3 Benchmark harness — committed, manual

`tools/query-bench/`, four pieces from the 2026-08-26 investigation:

- the report generator (a small console project referencing `Kronikol.csproj`; 200 scenarios × 60
  pairs, all-distinct ~6 KB bodies, ≈130 MB);
- the BCL-only re-tokenization benchmark behind §1.3/§1.6;
- the internals harness (`<AssemblyName>Kronikol.Tests</AssemblyName>` to use the existing
  `InternalsVisibleTo` grant) producing the per-stage steady-state table;
- a `README.md` with the protocol — same-session medians of 3, warm, Release; record machine, OS and
  date per row; never compare across sessions — and the baseline tables from Part 1.

Not wired into CI — wall-clock in CI is noise; the harness exists so before/after and any future
regression is one command.

## 4.4 Definition of done for the numbers

Re-run the harness after §3.1 and after §3.2/§3.3; append the after-columns to
`tools/query-bench/README.md`. Expectations: §3.1 reproduces its measured column (Part 1 table);
§3.2/§3.3 remove ~0.3–0.5 s from `values`/`--where` and ~0.6–0.9 s from `grep`. If a landed fix moves
its number materially less than measured here, find out why before shipping it.

---

# Part 5 — Documentation and release (per CLAUDE.md)

- **`QUERY_V2_PLAN.md` Part 7 addendum** (lands with this work): a dated note on the wide-run risk row
  recording the 130 MB benchmark (the corpus it cited was 250× smaller), the SQL-surface decline and
  its reasons, and a pointer to this plan and to the sidecar-cache trigger (routine 250 MB+ reports).
- `CHANGELOG.md` — perf entry with the before/after table; explicitly "no output or format change;
  Kronikol4J: none (tool is .NET-only)".
- Wiki: nothing — no user-facing surface changed. (`Querying-Reports.md` makes no perf claims to update.)
- Version: patch bump across **all** packages to the same number, tag `v{version}`, push commit + tag.

---

# Part 6 — Risks

| Risk | Position |
|---|---|
| §3.1 slows tool startup via eager optimizing JIT | Measured net win on every command tried; the knob only affects methods containing loops. If a pathological case appears, the property is one line to revert |
| The tiering knob's behavior shifts across future .NET majors (`RollForward: Major`) | The §4.2 runtimeconfig test pins the property is *present*; the harness detects if its *effect* regresses on a new runtime |
| Shared `FileStream` position races | `BodyCache` is per-command and single-threaded; if parallelism is ever revisited (§3.5), reads move to `RandomAccess.Read` (positional, handle-safe) at that point |
| Walker rewrite (§3.4) changes scanner output subtly | The full CLI-level suite is the pin; own commit, separately revertible; and the section is explicitly optional |
| Counter (`PayloadOpens`) rots into dead instrumentation | It is load-bearing: the §4.2 tests fail if opens regress, which is the counter's whole job |
| Benchmark numbers drift across machines/sessions | The README protocol mandates same-session medians and records machine + OS + date per row |
