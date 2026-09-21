# History verdict noise plan (#75, #83)

**Date:** 2026-09-18, third round 2026-09-19 · **Repo version:** 3.21.0 (`2491185e`; the patch still
applies at 3.22.1, `82abeb7f`) · **Status:** **EXECUTED IN FULL 2026-09-21 as 3.22.2 to 3.25.0 (§11 is the log, and
says where execution departed from what follows).** As written before execution: plan written, **S1–S4 prototyped in a throwaway worktree
and proven** (§1.1, §1.2; the patch is `HISTORY_VERDICT_NOISE_PLAN.prototype.patch`), nothing
implemented in the repository, **NOT green-lit**. **S2 was redesigned in a second round, after a test
reversed this plan's own claim about it (F7). S4 was redesigned in a third, after the first real
partial and degraded runs — made for this plan on BreakfastProvider — reversed three more (F8).**
§10 is the assumption ledger: what is RUN, what is only READ, what is not known, and the eleven
statements of this plan that checking overturned.

Covers GitHub issues **#75** (history verdicts on a parallel suite are mostly noise) and **#83**
(flakiness statistics do not say the failing run was degraded). They are one plan because #83 is built
on the same run-speed arithmetic that #75 §3 shows to be wrong; see
`OPEN_ISSUES_TRIAGE_2026-09-18.md` for how the other open issues were grouped.

**Standing permission that shapes this plan (user, 2026-09-18):** breaking changes to recent features
are fine, because nobody is using them yet. History (3.9.0+) and the ingest attribution passes are
recent. So this plan carries no compatibility shims, renames text and JSON freely, and prefers the
right semantics over the smallest diff. It still never bumps MAJOR (CLAUDE.md: ask first) — §9.

---

## 0. Summary

On the reporter's 95-scenario parallel Playwright suite, 72 scenarios were flagged and 1,494 new/gone
call lines printed, almost none of it behaviour. Every cause the issue names reproduces on `main`
(§1.1), and eight things it does not say turned up — the last two of them against this plan's own
drafts (§10.0):

| # | Finding not in the issues | Where |
|---|---|---|
| F1 | **Merge-before-drop rescues Mongo, not Redis.** The 17 starved scenarios had *zero* Redis calls, and Redis has no span twin to merge with, so §2a of the issue cannot bring those records back. | §4.3 |
| F2 | **Excluding partial runs from the alternating memory alone turns `alternating` into `behaviour-changed`.** The full run after a partial one still diffs against it, because `previousShaped` has no partial filter. | §5.2 |
| F3 | **The partial-run diagnostic already promises what the analyzer does not do.** It says the run "is not the run the next one is compared against"; that is true for `absent` only. | §5.2 |
| F4 | **#83's "5.6× its p95" is not a meaningful ratio.** It divides a raw reading from one run by a bar scaled to a *different, partial* run's speed. Against the scenario's own full-run median the reading was 6.4×. | §6.1 |
| F5 | **In #83's own case the verdict was never `flaky`.** Flaky needs two failing episodes; the scenario had one. What misled was the statistics line, so the fix is mostly what that line and the `F` row say. (`EVIDENCE_SURVIVES_A_RERUN_PLAN.md` reached the same reading of #84 independently.) | §6.1 |
| F6 | **A merge can throw attribution away, today.** `Adopt` takes the span's `TestId` unconditionally, so a wire record that knows its test loses it to a span twin carrying only the fallback id. Shown by a failing test on `main`. | §4.2 |
| F7 | **Merging before attribution — the issue's §2a, and this plan's first draft — puts a stranger's payload in the scenario.** `DropUnattributed` today removes unidentifiable records *before* they can compete for a span twin; merge first and one that overlaps better takes it. Built, run, seen: two Mongo calls where `main` has one. S2 is redesigned: merge after the attribution passes, before the drop, ranking pairs that already agree on their test first. | §4.2 |
| F8 | **A degraded run hands out `slower` verdicts on load alone, and then hides real ones.** Made for this plan: BreakfastProvider's suite sharing two processors with six busy loops. Slowdown under contention is nowhere near uniform (p25 2×, p90 54×), so the run-median normalisation of 3.16.0 cannot absorb it: **5, 20 and 7 of 203 scenarios** read `slower` on `main` in three such runs, all false, and in the healthy run afterwards 36 of 198 bars sat more than 2× too high. #83 asks for a label; the first real degraded run says the label has to gate `slower` as well. The same runs showed this plan's pace statistic to be the wrong one (a run with twenty false verdicts read 2.25 against a threshold of 2.0) and found the right one. | §6.2, §6.3 |

Five slices, each independently shippable, in this order:

| Slice | What | Issue sections | Bump |
|---|---|---|---|
| **S1** | Templater: separator-agnostic GUIDs, short hex digests, binary segments; `InteractionShape.Version` 3 → 4 | #75 §1a–c | patch |
| **S2** | Ingest: merge wire and span twins after the attribution passes and before the drop, pairs that agree on their test first; name the contested tests; `Adopt` keeps an attributed wire id | #75 §2 | patch |
| **S3** | Analyzer: partial runs leave the duration and behaviour baselines of a full run; the other alternating state is named; the fold scenario is not a test | #75 §3, §4, §5 | minor |
| **S4** | Degraded runs: a run's pace, the label on failures inside a degraded run, the reading against the scenario's usual — and a degraded run's durations leave `slower` and its baseline, as a partial run's do (F8) | #83 | minor |
| **S5** | Consumer templating rules (`HistoryShapeTemplates`) and the "only ids differ" hint | #75 §1d, §1f | minor |

Not taken: claims-aware templating (#75 §1e) and discounting degraded runs from the flip and fail
rates (#83 ask 3). Reasons in §8.

---

## 1. How far each claim was checked

The repo's own rule (`CROSS_RUN_HISTORY_PLAN.md` §17.0): an existence check must not stand in for a
behaviour check. Every claim below is marked.

- **RUN** — executed here, output read.
- **COMPUTED** — arithmetic done by hand from the code and the issue's numbers.
- **READ** — the code was read and the claim follows; nothing was executed.
- **ISSUE** — taken from the issue; not reproducible here (private suite).

| Claim | Level |
|---|---|
| The current `IdPattern` leaves all four of the issue's examples un-templated as described | **RUN** (the same .NET regex, in PowerShell) |
| The proposed patterns give exactly the issue's three expected strings, and `{bin}` for the fourth | **RUN** |
| The proposed patterns change **0 of 215** distinct call lines in BreakfastProvider's CI ledger, 0 of 200 in its local ledger, 0 of 7 in Kronikol's | **RUN** |
| Eighteen hand-picked probes: no route word, version segment, colour or plain number is templated | **RUN** (§3.2) |
| Across 1,745 source and fixture files of Kronikol and BreakfastProvider (628 distinct URL or path strings), the short-hex rule matches 9 tokens, **every one a group of a full hyphenated GUID** that the GUID alternative takes first | **RUN** |
| Every one of the issue's repro tests fails on `main` as the issue says, and its controls pass | **RUN** (§1.1) — `p95 = 32133` to the millisecond, as computed by hand first: 9079 / median(97, 739, 2482, 11110) × 5700 |
| `DropUnattributed` runs before `InteractionMerger.Merge` | **RUN** (the issue's ingest test fails on `main`, passes with the order swapped) |
| The merger never looks at `TestId` when matching | **READ** — `InteractionMerger.KeyOf`, `MatchPairs` |
| `Adopt` overwrites the wire record's `TestId` unconditionally, and that loses attribution | **RUN** (F6: a new test, red on `main`) |
| Partial runs feed `RunSpeeds`, `timed`, `shaped`, `memory` | **RUN** (the fix is a filter on exactly those, and it turns the tests green) |
| F2: the issue's narrower fix yields `behaviour-changed` | **RUN** (§5.2 — built, run, watched) |
| S1 + S2 + S3 together break nothing: **5,156 unit tests, 0 failed**, 1 skipped | **RUN** (§1.1). The Playwright and per-framework suites were not run |
| A run's pace over **394** healthy CI runs never exceeds **1.56**, by the C# prototype with the 10 ms floor, every run read leave-one-out against its stream (1.63 without the floor). First round, a PowerShell reimplementation over the 304 runs with five priors, read against priors only: 1.58 by run median, 1.44 by per-scenario ratio; on the four local ledgers where both were run the two implementations agree to the digit | **RUN** (§6.2) |
| A real partial run changes no verdict on BreakfastProvider under S3, moves 5 of 203 bars by at most 7 ms, and ran a median **1.32× slower** per scenario than the full runs | **RUN** (§1.2) |
| F4 on a second suite: in that partial run every p95 bar is 1.50–2.18× (median 1.88×) the bar the same scenario had in the full run ninety seconds before — 1,229 ms → 2,683 ms under a 1,256 ms reading | **RUN** (§1.2) |
| F8: 5 / 20 / 7 false `slower` verdicts in three contended runs on unmodified `main`; 0 / 0 / 0 with the degraded rule; pace 7.38 / 6.85 / 20.93 there, 1.47 / 1.68 when merely pinned, none of 394 CI runs labelled | **RUN** (§1.2, §6.2) |
| S1–S4 together break nothing: **5,187 unit tests, 0 failed**, 1 skipped | **RUN** (§1.2) |
| Kronikol4J has no history, templater or merger, so there is no Java parity work | **RUN** (grep of `../Kronikol4J`, zero hits) |
| The 1,494-line classification, the 17/30 Mongo split, the 784 dropped records | **ISSUE** |
| S4's surfaces (the `history sN` row, `--json`, the HTML tooltip, the option's plumbing) — its analyzer is built; S5 as designed; §5.2's naming of the other state, the `N` result, the relabel; §4.2's contested-pair diagnostic | **not built** — designed from READ |

### 1.1 The prototype

Built in a detached `git worktree` of `2491185e` under a temp directory, removed afterwards; the
repository's working tree was never touched. What it holds is saved beside this plan as
`HISTORY_VERDICT_NOISE_PLAN.prototype.patch` (9 files, +847 −26 after the third round: the redesigned
S2, S4's analyzer with its sixteen test cases, and the replay probe of §1.2 with its cost
measurement; `git apply --check` passes on `main` at `82abeb7f`, 3.22.1).
It is evidence, not the implementation: it skips the version bump, the doc comments, the diagnostics
and everything marked *not built* above.

| Step | Result |
|---|---|
| The issue's tests plus this plan's (F2, F6, negative probes), on unmodified `main` | **10 of 18 fail**, each as predicted; the 8 that pass are the controls and the negative probes |
| + S1 (the pattern of §3.2, `{bin}` before it) | the 4 templater tests green; **all 182 History tests green**, including every existing `Variable_path_parts_are_templated` pin and the base64 word list |
| + the issue's *narrow* §4a fix (partial runs leave `memory` only) | the first full run after the partial one reads **`behaviour-changed`** — F2, observed |
| + S3 as planned (partial points leave `timed`, `RunSpeeds` and `shaped` when the current run is full) | all History tests green: p95 in range, `slower` found, both following full runs `stable` |
| + S2 **as first drafted** (merge before `Attribute`, sorted as `Order` sorts; `Adopt` per F6) | all 124 existing Ingestion tests green, the issue's repro and F6's included — no existing merge or attribution test needed re-pinning. **Which is exactly why it was not enough: no existing test has a stranger in it** |
| Everything together, whole unit suite | **5,156 tests, 0 failed** |
| *Second round.* + the stranger test (F7), on `main`'s order / on merge-first | green / **red — two Mongo calls in worker-a, one carrying `cust-999`** |
| + S2 **as redesigned** (merge inside `Attribute`, after the passes, before the drop; `Agree` ranked first) | **all 126 Ingestion tests green**: the 124, the issue's repro (both cases), F6, the stranger, F1's Redis pin |
| Everything together again, whole unit suite | **5,158 tests, 0 failed** (the 5,156 plus the two new pins) |

Two things the prototype taught that the reading had not:

- **S2 re-pins nothing.** §4.4 expected diagnostics counts to move in existing tests; none did. The
  expectation stays in the slice (a real contested-claims fixture will move them) but it is not a cost.
- **Running the test executable directly needs `KRONIKOL_HISTORY=off`.**
  `Nothing_about_history_reaches_the_outputs_when_it_is_off` assumes it, `dotnet test` supplies it, a
  bare `Kronikol.Tests.exe` does not — it fails for a reason that has nothing to do with the change.
  Worth a line in CONTRIBUTING.

### 1.2 The real runs (third round)

No ledger anywhere held a partial run or a degraded one, so §10.3 said to make them. They were made
on BreakfastProvider's xUnit in-memory component suite (203 scenarios, Kronikol 3.20.0), with
`KRONIKOL_HISTORY` pointed at a copy of its local ledger (11 healthy full runs) so that the
repository's own ledger was never written. Each ledger was then replayed through the analyzer — cut
at the run being read, because the analyzer does not cut (§2) — on unmodified `main` and on the
prototype, by a probe that is in the patch (`LedgerReplayProbe.cs`; it is M0 in miniature, §7).

**The partial run.** Full run, then `-namespace …Scenarios.Orders` (33 scenarios, recorded
`partial: true` with its own roster and shapes lines), then a full run.

| Observed | |
|---|---|
| Verdicts, `main` against prototype, all three runs | identical. BreakfastProvider's scenarios make the same calls filtered or not, so **the call-set half of #75 §4 does not reproduce here**; F2 stays proven by its unit test and by the reporter's numbers, not by a second suite |
| Bars in the full run after the partial one | 5 of 203 move under S3, by 1–7 ms, all of them scenarios the partial run had run |
| The partial run's scenarios against their own full-run medians | **slower**: median 1.32×, p75 1.79×, max 4.74× (128 ms against 27). A filtered run pays the cold start with fewer scenarios to spread it over |
| The partial run's bars against the same scenarios' bars in the full run before it | 1.50–2.18×, median **1.88×**: its "speed" is the median of 33 Orders scenarios (15 ms) where a full run's is the median of 203 (8 ms). F4, measured rather than computed |

**The degraded runs.** Taking processors away (affinity to 8, 4, 2, 1 logical processors) barely
registers: the suite is idle-dominated, and even on one processor the wall time rose 20% and pace
read 1.68. Competing for them does: the suite pinned to two processors (then one) that six busy
loops were pinned to as well, at normal priority — a noisy neighbour on a CI agent, with the rest of
the machine left alone. Those runs took 1.7–2.6× the wall time; every scenario still passed. §6.2
holds the table; what they overturned is F8 and three rows of §10.0.

| Step | Result |
|---|---|
| S4's analyzer built on the prototype (pace, degraded, `TimesUsual`, the failure note, the counts) | its pace agrees to the digit with the first round's PowerShell reimplementation on the four ledgers where both ran |
| Three contended runs on unmodified `main` | **5, 20 and 7 false `slower`** of 203; none in the pinned or healthy runs |
| + the degraded rule (no `slower` in a degraded run; degraded points leave `timed`) | 0, 0 and 0; nothing else moves; the healthy run after two degraded ones is identical either way — but without the rule 47 of its 198 bars are more than 1.5× too high |
| The pace statistic, seven candidates over 394 + 13 healthy runs and the seven made here | the plain median separates by 1.6×, the median over scenarios with a usual of 10 ms or more by 4.4× (§6.2) |
| Everything together, whole unit suite | **5,187 tests, 0 failed** (sixteen new S4 cases among them) |

The ledgers, the per-run statistics and the scripts that made the runs are kept beside this plan as
`HISTORY_VERDICT_NOISE_PLAN.evidence.zip` (216 KB, with a README): they are the only real partial and
degraded runs there are, and §6.4's test 10 is a replay of them. Making fresh ones takes about twenty
minutes of machine time and no code.

---

## 2. Slice order and why

```
S1 templater ─┐
S2 ingest ────┼─ independent; either may go first
S3 partial ───┴─► S4 degraded (needs S3's clean full-run baseline)
S5 hook ──────── after S1 (extends the same rule pipeline and the same version key)
```

S1 removes 1,094 of the 1,494 lines (73%) on the reporter's suite by the issue's own table
(839 + 216 + 39), so it goes first. S3 must precede S4: S4's label compares a reading with the
scenario's usual, and "usual" is only meaningful once partial runs are out of it (F4).

**Renderer conflict.** S3 and S4 edit `QueryCommand.History.cs` `WriteScenario`, and so do S1 and S2
of `EVIDENCE_SURVIVES_A_RERUN_PLAN.md` (#82, #84). Interleave, never parallel. Suggested order: that
plan's S1/S2 patch first (it moves the error onto a continuation line, which is where S4's
`[run degraded …]` note then has room to go), then this plan's S3, then S4.

**Two dependencies on that plan.** Its F4 — `AnalyseStream` takes as "prior" every run of the stream
but the current id, never cutting at the current run — is harmless while the current run is the
newest, and wrong for M0's replay (§7), which analyses *old* runs. The replay cuts the ledger itself
until that plan's S3 makes the analyzer do it. S4's pace is computed over the analysis window, so it
inherits the cut and needs nothing of its own.

**And one on `HISTORY_ANALYZER_COST_PLAN.md` (#91), written after this plan's second round.** It
rewrites the `points` loop of `AnalyseScenario` that S3 filters, asks to land first, and says S3's
hunk there will need redoing by hand — agreed: it is one hunk, and the prototype patch is evidence,
not the implementation. It also owns the budget S4 now has to fit (§6.2, Cost): S3 and S4 go in after
it, under its ratio guard, and S4 reuses its per-roster position map rather than building a second
index of the same rosters. After F8, S4 touches the same `comparable` filter S3 introduces, so the
arrow from S3 to S4 above is a code dependency now as well as a semantic one.

---

## 3. S1 — the templater

### 3.1 What is wrong

`InteractionShape.IdPattern` (`InteractionShape.cs:54`) knows hyphenated GUIDs, hex runs of 16+, and
ULIDs. It misses:

- **GUIDs with `_` separators.** `Google.Cloud.BigQuery.V2` names every job `job_<guid with
  underscores>` and polls `…/queries/<that>`. No group reaches 16 hex, so nothing matches; an all-digit
  group becomes `{n}` and the rest stays. 839 lines (56%) on the reporter's suite.
- **Short hex digests** (`Convert.ToHexString(hash, 0, 6)` → 12 hex). 216 lines.
- **Binary hash bytes** in a cache key, captured as `%1D(%EF%BF%BD4…` — the `%EF%BF%BD` is U+FFFD, so
  the capture was already lossy. 39 lines.

The delayed cost is the worse one: once a scenario has `MinRuns` shaped runs whose set changed on every
pair, it is `unstable-shape` and every behaviour verdict for it is suppressed
(`HistoryAnalyzer.cs:360`). The noise ends by hiding real changes.

### 3.2 The change

One alternation, in this order (order matters — the GUID alternative must be tried before the short
run, or a GUID's first group would match alone):

```regex
(?<![0-9A-Za-z])(?:
    [0-9A-Fa-f]{8}(?<sep>[-_])[0-9A-Fa-f]{4}\k<sep>[0-9A-Fa-f]{4}\k<sep>[0-9A-Fa-f]{4}\k<sep>[0-9A-Fa-f]{12}
  | [0-9A-Fa-f]{16,}
  | [0-9A-HJKMNP-TV-Z]{26}
  | (?=[0-9A-Fa-f]*\d)(?=[0-9A-Fa-f]*[A-Fa-f])[0-9A-Fa-f]{8,15}
)(?![0-9A-Za-z])
```

`Options` has `ExplicitCapture`, so the back-reference has to be named (`sep`), as the issue says. The
short-run rule needs at least one digit and one letter: plain numbers are already `{n}`, and hex-only
words (`facade`, `deadbeef`, `feedface`) have no digit.

Binary segments, applied **before** the id rule:

```regex
(?<=^|[/\-_:.])[^/\-_:.]*(?:%EF%BF%BD|%[01][0-9A-Fa-f])[^/]*   →   {bin}
```

From the delimiter before the first U+FFFD or C0 escape to the end of the path segment. Walking back to
the delimiter matters: a SHA-1's leading bytes are printable about a third of the time, and those
characters vary per run just as the escaped ones do. Residual risk, accepted: a raw byte that is itself
one of `-_:.` ahead of the first escape leaves a short varying prefix (about 4 in 256 per leading
byte). S5's hook is the answer for a consumer who hits it.

Measured (**RUN**), current rule → proposed:

| Input | Current | Proposed |
|---|---|---|
| `…/queries/job_09c68d49_adcf_47be_bf9e_fc041cdc4a8e` | unchanged | `…/queries/job_{id}` |
| `…/queries/job_0094e738_b6a9_4411_ba74_9379763e3183` | `…_b6a9_{n}_ba74_…` | `…/queries/job_{id}` |
| `/app:_v2:charts-agg-100000000000001-D692278CF6C9-Weekly` | `…-{n}-D692278CF6C9-Weekly` | `…-{n}-{id}-Weekly` |
| `/app:_customerDates_100000000000001-%1D(%EF%BF%BD4…:` | `…_{n}-%1D(%EF…` | `…_{n}-{bin}` |
| `/commits/3787de7c2f2e`, `/containers/0a1b2c3d4e5f/logs`, `/assets/app.3f9a1c2b.js` | unchanged | `{id}` in each — wanted |
| `/customers/abc123`, `/v2/orders`, `/colours/ff00aa`, `/facade/deadbeef/decade/feedface`, `/api/v1/accede1/b2b`, `/builds/20260918/12345678` | — | **identical to current** |

A mixed-separator GUID (`550e8400-e29b_41d4-…`) is deliberately not a GUID; its first group falls to
the short-run rule. Nothing writes those.

**The wider corpus (RUN).** The ledgers are one application's calls, so the short-hex rule — the
nearest thing here to the base64 rule that was measured and thrown out — was also run over every URL
or path-like string in both repositories' sources and fixtures: 1,745 files, 628 distinct strings. It
matched 9 tokens, and all 9 are groups of a full hyphenated GUID (`550e8400`, `00c04fd430c8`, …) that
the GUID alternative consumes first. **No route word, asset name or version segment in either
codebase is templated by it.** What this corpus cannot show is a false positive in somebody else's
URL scheme; S5's "only ids differ" hint has a mirror image worth adding for that — see §8.

### 3.3 The version

`InteractionShape.Version` 3 → 4, with the doc comment's history line extended. The analyzer already
handles it (`HistoryAnalyzer.cs:349–354`): the first run under the new rule reads "fingerprinted by an
earlier rule; behaviour is compared from the next run", and `shaped` restarts, so `unstable-shape` and
the count stretch need `MinRuns` runs again. **One quiet run per stream, by design.** On the three
ledgers measured no line would have changed, but the bump is unconditional: a fingerprint is comparable
only with one the same rule made.

`TemplateStatement` shares `IdPattern`, so SQL and document heads take the short-hex rule too. The
probe for that (hex-looking identifiers in a statement head) is part of the slice's tests, not assumed.

### 3.4 Tests (red first)

1. The issue's `Ids_the_templater_misses` theory, verbatim, plus the `{bin}` case.
2. Every existing `Variable_path_parts_are_templated` row stays green (it pins `abc123`, `v2`, `cust-{n}`).
3. New negative rows: the six "identical to current" probes above.
4. `There_is_no_base64_rule_because_it_templated_real_words` is re-read and extended: the short-hex rule
   is the nearest thing to the base64 rule that was measured and rejected (§5.6 of the history plan),
   so its word list is run through the new pattern and must survive.
5. A statement-head probe: `SELECT c0.added1d FROM …`-style identifiers are not templated.
6. `The_shape_rule_has_a_version_a_run_records` moves to 4.

---

## 4. S2 — ingest: merge before the drop, not before attribution

### 4.1 What is wrong

`IngestPipeline.Run` calls `Attribute` (`:341`) — claims pass, window pass, phase pass, then
`DropUnattributed` (`:596`) — and only later `Order` (`:349`), which calls
`InteractionMerger.Merge` (`:716`). A wire record whose claim is contested is unattributed, so it is
dropped before the merger could have given it its span twin's exact `TestId`. `Attribute`'s own doc
comment says a record reaches the filter "once every chance to identify it has been taken"; the span
twin is such a chance.

On the reporter's suite one long sweep scenario honestly claims every seeded customer (as the
`AttributeByClaims` remarks advise). For its 111 s window every Redis and Mongo wire record from the
worker sharing a customer with it was contested and dropped: 784 records.

### 4.2 The change

1. **Merge after the attribution passes and before `DropUnattributed` — and make the merger
   identity-aware.** Inside `Attribute`: claims pass → window pass → phase pass → **merge** → drop.
   Stable-sort by timestamp first (the sort `Order` already does, extracted so both use one helper);
   `Order` keeps its sort and loses its merge call. In `MatchPairs`, candidates are ranked first by
   whether the two records are *already known to belong to the same test* (neither needs attribution,
   same `TestId`), then by overlap, start distance and index as today.
   - *Why sort first:* `MatchPairs` breaks ties by `RequestIndex`, so the input order decides which of
     two equal candidates pairs.
   - **Why not merge first, which is what the issue proposes (§2a) and what this plan's first draft
     said (F7).** The first draft reasoned: the merger never reads `TestId` when matching, so running
     it earlier changes *which records survive*, not *which records pair*. **That is false, and a test
     shows it.** Today `DropUnattributed` runs before the merger and so *shrinks its candidate set*:
     a stranger's wire record — a seeder's, another worker's — that nobody could identify is gone
     before it can compete for a span. Merge first and it competes. The test
     (`A_record_that_would_have_been_dropped_does_not_take_another_records_span_twin`): worker-a's
     span, its true wire twin (1–9 ms, `cust-111`), and a stranger's wire record on the same
     collection (0–10 ms, `cust-999`, no claim of anyone's, inside two test windows). Both overlap the
     span fully; the stranger starts closer, wins the tie, and adopts worker-a's identity. **On `main`:
     one Mongo call in worker-a, `cust-111`. Merge-first: two — the stranger's payload under
     worker-a's name, and the true twin beside it, wire-only.** A fix for dropped records that puts
     another customer's data in the scenario is worse than the bug.
   - *Why the new order is right:* by the time the merger runs, the claims pass has said the true twin
     is worker-a's, so it *agrees* with the span and pairs first; the stranger finds no span left and
     is dropped as it is today. In the issue's case the twin is contested and so unattributed, it is
     the only candidate, and it is rescued. **RUN: all 126 Ingestion tests green** — the 124 existing
     ones, the issue's repro in both cases, F6's, the stranger's and F1's.
   - *What stays possible:* a twin that is itself unattributed **and** out-competed by a stranger
     still mis-pairs. That is the cross-test mis-merge `main` already has (§8), not a new one.
2. **`Adopt` keeps an attributed identity.** Today `TestId = span.TestId` unconditionally. When the
   span itself is unattributed (no baggage, or the fallback id) and the wire record has a real id — an
   HTTP proxy tap that read a header — the merge *loses* attribution. `Merge` gains an optional
   `fallbackTestId` and adopts the span's id only when the span's does not need attribution; otherwise
   the wire's id stands and the record goes on to the claims pass with the wire's content. **RUN (F6):**
   `A_merge_does_not_trade_a_known_test_for_the_fallback` is red on `main` — both merged records come
   out as `session` — and green with the change. It is a bug independent of #75 and worth its own
   changelog line.
3. **Name the contest** (#75 §2b). `AttributeByClaims` returns the contested count; it should also
   return the top contested pairs. The diagnostic becomes
   `776 interaction record(s) matched the claims of more than one in-flight test …; most contested: sweep × worker-a on "cust-111" (512), …`
   — top three. Finding the cause took the reporter a hand correlation of `tests.jsonl` windows.

### 4.3 What this does **not** fix (F1)

The issue's split table: of 47 Mongo scenarios, the 17 span-only ones had **zero Redis calls**. Redis
has no OpenTelemetry twin in that stack, so there is nothing to merge with, and contested Redis wire
records are still dropped after this slice. Of the 388 lines the issue files under §2, only the Mongo
`Find` half comes back. **RUN:** `A_contested_wire_record_with_no_span_twin_is_still_dropped` passes
under every order tried — it pins the limit as a limit, so nobody later reads S2 as having fixed it.

Options for the rest, none in this slice:

- **Consumer-side, today:** the suite already has a serial pass; the sweep belongs in it. Worth saying
  in the wiki's `AttributeByClaims` section: a test that claims everything contests everything for as
  long as it runs.
- **Time-scoped claims (candidate follow-up):** a `claims` event already exists for adding claims
  mid-run, but `BuildClaimWindows` unions every claim over the whole test window, retroactively. If a
  `claims` event applied from its own timestamp (and a `release` ended it), the sweep could claim each
  customer only while visiting it. Small, but it is a new record contract — its own issue.
- The residue is already the right verdict: a scenario whose Redis lines come and go with scheduling
  *has* two states, and `alternating` says so.

### 4.4 Tests (red first)

1. The issue's `MergeBeforeDropReproTests`, verbatim (it carries its own passing control).
2. A wire record with a real `TestId` and a span twin with the fallback id keeps the wire's id (item 2).
2a. The stranger test of item 1 — green on `main`, and it must stay green: it is the guard against the
   obvious "simplification" back to merge-first.
2b. F1's Redis test (§4.3).
3. Byte-identity: an existing merge fixture with no contested claims produces the same
   `TestRunReport.json` before and after.
4. The diagnostic names the contested pair.
5. Diagnostics counts that legitimately move ("N attributed by content claims" falls, because merged
   records no longer need it) are called out in the changelog. The prototype moved none in the 124
   existing Ingestion tests (§1.1), so add one fixture where it does and pin the new numbers there.

---

## 5. S3 — partial runs, the other state, the fold scenario

### 5.1 The principle

> A partial run's **pass or fail** is a fact about the scenario. Its **duration relative to the run**
> and its **set of captured calls** are facts about the run's conditions.

So status keeps reading partial runs — #80–#84's whole session depends on the `P` rows of filtered
re-runs appearing — and duration and behaviour stop. The evidence that conditions differ is in both
issues: #83's scenario took 12.5 s in full runs and 1.2 s in filtered ones (10× faster, alone on the
machine); #75 §4's captured 74 calls against 22 (with one worker running, `ExclusiveOnly` window
attribution is suddenly exclusive for everything). **They differ in either direction**: the real
partial run made for this plan (§1.2) ran a median 1.32× *slower* per scenario than the full runs
around it, because a filtered run pays the cold start with fewer scenarios to spread it over — and its
bars stood at 1.88× the full run's from the mix alone. "Filtered runs are faster" is the reporter's
suite, not a rule; "a filtered run's durations are not comparable" is the rule. (And on that suite the
calls did *not* differ, so how much of #75 §4 a given consumer sees depends on how its calls are
attributed — after the fact and by time window on the reporter's ingest suite, in-process from the
current test's identity on BreakfastProvider's, where a filter cannot change whose call it is.)

### 5.2 The change

Add `bool Partial` to `HistoryPoint` (from `run.Partial == true`, the same test `LastFull` uses).
Then, **when the current run is full**, partial points are removed from:

| Baseline | Code | Effect |
|---|---|---|
| `RunSpeeds` and `timed` | `:85`, `:320` | #75 §3: the issue's two failing tests pass; the control stays green |
| `shaped` — and therefore `previousShaped`, `changes` (unstable), `memory` (alternating), `counted` (count stretch) | `:350` | #75 §4a, **and F2** |

**F2, spelled out — and watched (§1.1).** The issue asks only for partial runs to leave `memory`. Do only that and its own
example — `22 ×3 · 74 (partial) · 22 ×2` — gets *worse*: on the first full run after the partial one,
`previousShaped` is still the partial point, so `changed` is true; the current set is found in memory
but nothing different follows it there, so it is not `alternating`; it falls through to
**`behaviour-changed`** against a run the diagnostic (`HistoryRunContext.cs:140`) already says "is not
the run the next one is compared against" (**F3**). Filtering `shaped` once, upstream, fixes all four
consumers and makes the diagnostic true.

**When the current run is partial:** behaviour compares against every shaped point as today (a
developer who filtered to one scenario after an edit wants to hear that its calls changed), but no
`slower` verdict is read — its speed is the median of an arbitrary subset — and the p95 line prints
the raw, unscaled p95 of full runs, labelled as such.

**The label (#75 §3c).** `p95 of earlier runs` → `p95 of earlier full runs, at this run's speed`. The
bar is scaled to the current run's speed, so it can legitimately exceed every raw reading under it (36
of 95 scenarios on the reporter's suite); the old label read as a bug.

**Naming the other state (#75 §4b).** When a scenario is `alternating` and the current set equals the
previous one, the diff against the previous run is empty and the evidence lists no calls (7 scenarios
on the reporter's suite). Diff instead against the most recent memory point holding a *different* set,
and name it: `…; other set last held in <runId>: gone there: …, new there: …`.

**The fold scenario (#75 §5).** `FoldUnknownTestsInto`'s scenario exists only when unattributed traffic
survived, so it flickers in and out of `absent`. It cannot leave the roster: a roster position *is*
the scenario's `sN` address (`HistoryRunBuilder.cs:47–51`). Instead:

- `Scenario` gains a flag the pipeline sets where it already special-cases the fold (`IngestPipeline.cs:365–369`).
- The run line writes a new result character **`N` — not a test** — for it. `IsRealVerdict` is already
  false for anything but `P`/`F`, so an older reader treats it as it treats a skip; no format version.
- The analyzer reads no verdict for an `N` position and never reports one `absent`.
- Nothing in `src/` validates a result character (RUN, grep), so a reader from before this slice takes
  `N` without complaint. Two places render one and need an arm: `HistoryHtml.cs:264–267` (the label)
  and `:276–278` (the colour) — both have a default today, so an `N` would draw as an unnamed grey bar.

### 5.3 Tests (red first)

1. The issue's three duration tests, verbatim (two red, one control).
2. `22 ×3 · 74 (partial) · 22 ×2`, analysed as each of the two following full runs: `stable`, not
   `alternating`, not `behaviour-changed`. (This is the F2 test; written before the fix it shows the
   `behaviour-changed` the narrower fix would have shipped.)
3. A partial current run still reads `behaviour-changed` against a full previous run, and reads no `slower`.
4. `A_partial_prior_run_is_not_the_run_verdicts_compare_against` gets a sibling where the partial run
   *did* run the scenario: status still reads it (`P` after a partial `P` after a full `F` is not
   `fixed` again) — pinning the principle, not just the case that happened to pass.
5. Alternating with an unchanged previous set names the other run and lists its calls.
6. An `N` position is never `absent`, never `new`, carries no behaviour verdict.
7. `QueryHistoryTests`: the new label; `(partial)` on a partial row of `history sN`.

---

## 6. S4 — degraded runs (#83)

### 6.1 What the issue gets right, and two corrections

Right: a failure inside a run where everything was slow is weak evidence against the test, the ledger
already holds every number needed, and one degraded run explains a whole cluster of "new flaky tests".

**F4 — the ratio.** "That run took 5.6× its p95" divides the failing run's raw 82,215 ms by a bar
(14,731 ms) that was scaled to the speed of the *current* run — a 34-scenario filtered re-run. After
S3 that bar no longer exists for a partial current run. The honest per-scenario number is the reading
against the scenario's own usual in full runs: median(12,502, 13,034) = 12,768 ms → **6.4×**.

**F5 — the verdict.** The scenario's verdict was `behaviour-changed`, not `flaky`: flaky needs two
failing *episodes* (`HistoryAnalyzer.cs:295–301`) and it had one. What read as "unreliable test" was
the statistics line, `flip rate 0.50 · fail rate 0.20`. So the work is what that line and the `F` row
say, not a change to when `flaky` fires.

**A caution the issue half-states.** A failing test is usually slow *because* it failed — a polling
assertion runs to its timeout. So "this reading was 6.4× usual" on a failing row is a fact, not a
cause, and the per-scenario note must not say "resource-starved". Only the run-level signal earns a
causal word. And in #83's own incident the reporter says exactly one scenario of 396 lost the race, so
the run-level signal **may not have fired** there. Both notes ship; they answer different questions.

### 6.2 A run's pace — definition and measurement

> **pace(r)** = the median, over the scenarios that **passed** in *r*, have a baseline, and **usually
> take at least 10 ms**, of *duration in r* ÷ *the scenario's usual* — its median **passing** duration
> over the full runs in the window, *r* excluded.

Passing scenarios only, so timeouts do not inflate it. Needs at least `MinRuns` other full runs behind
each usual and at least 5 scenarios that qualify; otherwise pace is unknown and nothing is said.
Partial runs never enter a baseline and have no pace: their conditions differ **in either direction**
— 10× faster on the reporter's suite (#83), and a median 1.32× *slower* (up to 4.7×) on
BreakfastProvider's, where a filtered run concentrates the cold start on the few scenarios it has
(RUN, §1.2). A degraded run **stays** in the usual: a median shrugs off a minority of bad runs, and if
degraded runs left it a genuine, sustained slowdown of the whole suite would read as degraded for
ever instead of for half a window (below).

**The statistic was chosen on real degraded runs, and the first choice lost (§1.2, F8).** Seven
statistics were computed for every run of BreakfastProvider's CI ledger (18 streams, **394 healthy
runs**, every run read leave-one-out against its whole stream, by the C# prototype), for 13 healthy
local runs, and for the seven runs made for this plan with the suite deliberately starved:

| Statistic | Worst healthy run (of 407) | Weakest really-degraded run | Apart by |
|---|---|---|---|
| Median of per-scenario ratios — this plan's first definition | 1.63 | 2.58 | 1.6× |
| Share of scenarios at ≥ 2× their usual | 0.41 | 0.52 | 1.3× |
| 75th percentile of the ratios | 3.14 | 15.1 | 4.8×, but a fat healthy tail (p99 2.36) |
| **Median over scenarios whose usual is ≥ 10 ms (chosen)** | **1.56** | **6.85** | **4.4×** |

The first definition put a run that handed out **twenty false `slower` verdicts** at 2.25 when it was
made — 12% over the threshold — and the same load at 7.1 a quarter of an hour earlier. The distribution
under contention looked bimodal (p25 1.0×, p50 2.5×, p75 15×), and the median sat on the cliff. It is
mostly millisecond quantisation: a scenario that usually takes 3 ms barely registers load, and about
100 of the 186 baselined scenarios of BreakfastProvider's in-memory suite are under 10 ms. A **small** floor on the usual removes
them; a large one throws away the signal instead, because in that suite the long scenarios are
timer-bound (a 100 ms floor reads 4.98 where 10 ms reads 7.38). The run-median definition of the issue
(run median ÷ median of prior runs' medians) was measured in the first round and is worse again: it
reads a *healthy partial* run at **2.14**, from the mix alone.

**Threshold: `HistoryDegradedBy = 2.0`**, and both sides of it are now measured:

| Run (BreakfastProvider xUnit in-memory, 203 scenarios, all passing in every run) | Wall | Pace | Label | False `slower` on `main` |
|---|---|---|---|---|
| 394 healthy CI runs, 18 streams | — | p95 1.29 · p99 1.48 · **max 1.56** | none | — |
| 13 healthy local runs | 159–175 s (the three timed) | 0.93–1.22 | none | 0 |
| Pinned to 8 / 4 logical processors | 160 / 193 s | 1.00 / 1.00 | none | 0 |
| Pinned to 2 / 1 logical processors, nothing competing | 196 / 192 s | **1.47 / 1.68** | none — and rightly: +20% wall, no false verdict | 0 |
| Pinned to 2 processors shared with 6 busy loops (A) | 292 s | **7.38** | degraded | 5 |
| The same again, straight after (B) | 276 s | **6.85** | degraded | **20** |
| Pinned to 1 processor shared with 6 busy loops | 418 s | **20.93** | degraded | 7 |

28% headroom over the worst healthy run, a factor of 3.4 under the weakest degraded one. The suite is
idle-dominated — its scenarios sum to 10–15 s of a 160 s run — which is why taking cores away does so
little and competing for them does so much. **What is still not measured: a failure.** Every scenario
passed in every starved run, up to 2.6× the wall time, so the note on a failing row is proven by unit
test only, and one suite's load profile is one suite's.

**A limit to state, not to fix.** Pace cannot tell a slow machine from a suite that really did get
twice as slow everywhere (a new DI container, a debug build). Such a shift reads as degraded until the
shifted runs are half the window — the first 25 of them at the default, RUN — and then stops by
itself. That is
acceptable because `slower` is already blind to a uniform shift by construction (3.16.0 reads every
scenario against its run), and "passing scenarios took 2.3× their usual" on every run is the first
surface that would show such a shift at all. §6.4 pins the behaviour.

**Cost — measured, and not free.** The first two rounds said "250k divisions against a measured 65 ms
analysis", from arithmetic. Both halves were wrong. `HISTORY_ANALYZER_COST_PLAN.md` (#91) showed the
65 ms was a prototype harness and the shipped analyzer takes 2,060 ms at 5,000 scenarios × 50 runs,
200–310 ms after its fix; and the prototype's pace, timed at that size (Debug build, five attempts):
**100–115 ms to build** the usuals and the 51 paces, **42–53 ms** for the 255,000 `TimesUsual`
readings. That is half as much again on top of the fixed analyzer, so the slice does not ship the
prototype's shape: S4 lands **after** the cost plan, keys its usuals by that plan's per-roster
position map instead of a dictionary of `(id, slot)` tuples, reads `TimesUsual` only for the points a
surface prints, and goes under the cost plan's ratio guard like everything else in that loop.

### 6.3 What changes on the surfaces

Analyzer: `RunPoint` gains `Pace` and `Degraded`; `HistoryPoint` gains `RunDegraded` and
`TimesUsual` (the reading ÷ the scenario's usual; null with fewer than two other full-run passing
readings, and null in a partial run — on BreakfastProvider's real partial run a scenario read 128 ms
against a usual of 27, over both halves of the test below, and it was only the cold start).
`ScenarioHistory` gains `FailuresInDegradedRuns`. New option `HistoryDegradedBy` / `--degraded-by`.
All of it is built (§1.2).

**The row's note and the run's label wait for different things (fourth round, §1.3).** The prototype
first made both wait for the suite's `MinRuns`. Read against #83 *as the ledger really was* — a stream
of 22 runs (#84 prints the count), a scenario that had been in two of them, the run it failed in, then
two 34-scenario filtered re-runs (#80's timeline, #81) — that left the very row the issue is about
bare: two full-run readings are under `MinRuns`, and the filtered runs are rightly in no usual. The
unit test carrying #83's numbers was green only because it set `MinRuns = 2`; the "after" below was
unreachable at the default. So the two are split, each on something measured:

- **The row** is read against its usual from **two** other full-run passing readings — which is when
  `main` first prints a duration bar beside a reading. It is a reading, not a verdict, and the 100 ms
  floor below still applies to it.
- **The run** is paced only from scenarios with `MinRuns` other readings each, and the label and what
  it does to `slower` (F8) wait with it. Measured on the 394 healthy CI runs, every window of *k* + 1
  consecutive runs of every stream, each run paced against the other *k*:

  | Paced against | Paces | p99 | Max | At or over 2.0 |
  |---|---|---|---|---|
  | 2 other runs | 1,074 | 1.46 | 2.27 | 1 |
  | 3 | 1,360 | 1.58 | 2.19 | 3 |
  | 4 | 1,610 | 1.45 | 2.02 | 1 |
  | **5 (the default `MinRuns`)** | 1,824 | 1.50 | **1.76** | **0** |
  | 8 | 2,250 | 1.47 | 1.67 | 0 |

  A healthy run read against fewer than five others is called degraded one to three times in a
  thousand; against five, never. On a ledger younger than that nothing is said about the run, and the
  degraded one is labelled after the fact, by the first analysis that has the runs (pinned, §6.4).

**F8 — a degraded run's durations are the machine's, and `slower` must stop reading them.** This plan
said S4 "labels and never discounts", and for pass and fail that stands. For durations it does not
survive the first real degraded run. Under contention the slowdown is nowhere near uniform — one run:
p25 2.0×, p50 7.1×, p75 19.8×, p90 53.9×, max 136× — so reading each scenario against its run's median
(3.16.0) cannot absorb it, and every scenario whose previous reading happened to sit over its bar is
handed a `slower`. On unmodified `main`: **5, 20 and 7 of 203 scenarios** in the three contended
runs, all false; the 20 is the second of two consecutive degraded runs, which is what a bad day on a
shared CI agent looks like. And it costs twice: the nearest-rank p95 of fewer than twenty readings *is*
the maximum, so in the healthy run that followed, leaving the two degraded runs in the baseline raised
**47 of 198 bars by more than 1.5×, 36 by more than 2×, 7 by more than 5×** — a real slowdown in any of
those scenarios is hidden for as long as the degraded runs stay in the window.

So a degraded run is, for durations, what S3 makes a partial run: **when the current run is degraded
no `slower` is read; when it is not, degraded points leave `timed`** — one more clause on S3's
`comparable` filter. Built and run on the real ledgers: 5 → 0, 20 → 0, 7 → 0, the healthy run after
them unchanged, nothing else moves; two unit tests show each direction red without the rule and green
with it. What it costs is one run of delay: `slower` needs two consecutive readings over the bar, and
for a genuine regression that lands during a degraded run those are now the first two healthy runs
after it. The threshold now gates a verdict, not only a note — which is why §6.2 measures it from
both sides rather than choosing it.

**`TimesUsual` needs the floor `slower` already has.** BreakfastProvider's in-memory suite has a
*median scenario duration of 6–9 ms* (RUN, its local ledger): 3 ms against 9 ms is "3× usual" and
means nothing. The note prints only when the reading is over the factor **and** over the usual by
`HistorySlowerMinMs` (100 ms), the same two-part test `slower` applies and for the same reason. Pace
needs a floor too, but a different one and for the opposite reason — §6.2: the 100 ms floor that keeps
a *note* honest would throw away most of a fast suite's *signal*, so pace counts scenarios whose usual
is at least 10 ms and the note keeps its 100.

**On a surface that lists a whole run, the run label speaks, not the rows.** In the contended runs
72–88 of 203 rows cleared both halves of the note's test. `history sN` is one scenario down the runs,
so there the note is one per row and earns its place; a per-run listing prints the run's label once.

`history sN`, before and after (the issue's own scenario):

```
runs seen: 4 · verdicts: 5 · failed 1 · flips 2 · flip rate 0.50 · fail rate 0.20 · last failed 2 run(s) ago
  F  local:…T101611Z  82215 ms  The service bigquery has thrown…
```
```
runs seen: 4 · verdicts: 5 · failed 1 (1 in a degraded run) · flips 2 · flip rate 0.50 · fail rate 0.20 · last failed 2 run(s) ago
  F  local:…T101611Z  82215 ms (6.4× usual)  [run degraded: passing scenarios took 2.3× their usual]  The service bigquery has thrown…
```

`(N× usual)` prints on any row at or over `HistoryDegradedBy`, failing or not. A `flaky` verdict whose
failures all sit in degraded runs leads its evidence with that. `--json` rows gain `runs[].timesUsual`,
`runs[].runDegraded`; the run-level envelope gains `pace`. `HistoryHtml`'s run tooltip gains
`· degraded 2.3×`. The Playwright suite gets the tooltip check (CLAUDE.md: UI features need one).

### 6.4 Tests (red first)

Tests 1–3, 5–9 and 11–13 exist and are green in the prototype (`NoisePlanDegradedTests.cs`, nineteen
cases); 7 is written as a theory over a prototype-only switch so that it shows `main`'s behaviour and
the fix side by side — the slice keeps the green half.

1. A run whose passing scenarios all take 3× their usual is degraded; the same run with one scenario
   at 30× and the rest normal is not (the median is robust — that is the point of it).
2. A failure in a degraded run carries the note; the same failure in a normal run does not.
3. Failing scenarios' durations do not move pace (all failures at 60 s, passes normal → not degraded).
4. Under `MinRuns` full runs, or under 5 qualifying scenarios: pace is null and nothing prints.
5. A partial run has no pace, is in no scenario's usual, and its rows carry no `TimesUsual`.
6. The 6.4× from #83's numbers, as a unit test on its readings (82,215 against 12,502 and 13,034).
7. **F8, both directions.** A contended run — nine scenarios at 2×, ten at 6×, one at 30× whose
   previous reading was a spike — reads no `slower`; and a real 2.5× regression in the two healthy
   runs after a contended one *is* `slower`, where with the degraded run in the baseline its bar sits
   at 5× and hides it.
8. Scenarios under the 10 ms floor do not vote: a run where only they slowed down is not degraded, and
   a suite made only of them has no pace.
9. A sustained shift after fifty healthy runs: every run at 3× is degraded up to the 25th and not
   from the 26th (§6.2's limit, pinned so that a change to it is a decision).
10. The replay of §7 over the three ledgers of §1.2, as the slice's acceptance: the contended runs
    labelled, the pinned ones not, none of 394 CI runs, and 5 / 20 / 7 `slower` → 0.
11. **#83 as the ledger was**, at the default `MinRuns`: six runs without the scenario, two with it
    (12,502 and 13,034 ms), then the run it fails in at 82,215 ms while every other scenario takes
    5.6× its usual. The failing row reads 6.44× its usual, the run is paced 5.6 and labelled, the
    evidence says so; and after two filtered re-runs (1,181 and 1,270 ms) the row still reads 6.44×,
    because a filtered run is in no usual. Test 6 is the same arithmetic with `MinRuns = 2`, and that
    option is the only reason it ever passed.
12. A row with one other reading is not read against a usual.
13. **A young ledger.** Five runs, the third degraded with a failure in it: no run has a pace, the
    failing row is still read against its usual; with a sixth run the third is paced, labelled, and
    the scenario counts one failure in a degraded run.

---

## 7. M0 — the ledger replay, before any slice

Every slice changes verdicts, and the repo has a 394-run real ledger to check that against. Before S1:

- A dev-only harness (`tools/history-replay/`, the `tools/query-bench` pattern): for each run in a
  ledger, analyse it against its priors — **cutting the ledger at that run itself**, because the
  analyzer does not (§2) — and write the verdict counts per run as CSV. Run on the base
  commit and on the slice; diff.
- **Acceptance per slice on BreakfastProvider's ledger:** S1 — no count moves except the one quiet run
  per stream; S3 — no count moves (the ledger holds no partial runs: measured, 0 of 304); S4 — zero
  runs labelled degraded.
- **Acceptance on the reporter's ledger:** they offered PRs and hold the only ledger with partial
  runs, contested claims and a degraded run. Ask for the replay's before/after CSV rather than the
  ledger itself. Target from the issue: 72 flagged of 95 → the residue of §4.3 and the 12
  consumer-side UI-label lines.

- **The gap in that acceptance, closed without the reporter (§1.2).** No ledger held a partial run —
  0 of 304 on CI, 0 of 11 locally (RUN) — so "S3 moves no count" on BreakfastProvider was true and
  proved nothing about partial runs. One was made: a filtered run between two full local ones. S3's
  before and after on it: no verdict moves, five bars move by milliseconds — and the partial run's
  own bars showed F4 on a second suite. The same trick gave S4 its degraded runs, and those changed
  the slice (F8). **The harness is no longer hypothetical either:** the probe that did all of this is
  in the patch, and `tools/history-replay/` is that probe with a command line.
- **What the made runs cannot give.** A failure inside a degraded run (every scenario passed, up to
  2.6× the wall time), a partial run whose calls differ (BreakfastProvider attributes in-process from
  the current test's identity, not by time window, so they do not), and a second suite's load
  profile. The reporter's replay is still the only source for all three.

The repro tests already exist: the prototype patch (§1.1) carries the issue's, verbatim, plus this
plan's for F2 and F6, in `NoisePlanReproTests.cs` and `NoisePlanIngestReproTests.cs`. Each slice
starts by applying its part of that patch's tests and watching them fail.

---

## 8. S5, and what is not taken

**S5 — `ReportConfigurationOptions.HistoryShapeTemplates`** (#75 §1d): an ordered list of
`(regex, placeholder)` applied before the built-in rules. Cache-key formats are app-specific; no
built-in rule covers them all, and §3.2's `{bin}` residue is the proof. The run line gains a
`shapeRules` hash beside `shapeVersion`, and the analyzer's comparability test
(`(p.ShapeVersion ?? 1) == rule`) becomes equality of the pair — so a rule edit costs one quiet run
instead of flagging every scenario once. A rule that throws or times out (`RegexMatchTimeoutException`;
the regexes are consumer-supplied) is skipped and reported as a diagnostic, never fatal.

*A trap in the ledger writer (READ).* `kronikol history record` folds shards by rebuilding the
`HistoryRun` **field by field** (`HistoryLedgerWriter.cs:379–409`), so a new run-line field that is not
added there is dropped silently — for every sharded CI run, which is every CI run that matters. And
the fold takes `ShapeVersion` from *the first shard that has one*. For a version that is harmless;
for a rules hash it is not: eight shards configured differently would fold into one line claiming
one rule. The fold must carry `shapeRules`, and when shards disagree it must blank the fingerprints
and say so rather than pick. The same check applies to anything S4 adds to the run line (the current
design adds nothing there — pace is computed, not stored — and that is a reason to keep it so).

**S5 — the "only ids differ" hint** (#75 §1f): when new and gone lines pair 1:1 after masking every
alphanumeric run containing a digit, append
`the calls differ only in what looks like an id (job_09c6… → job_1f3a…): a HistoryShapeTemplates rule would make them compare equal`.
**The verdict stays `behaviour-changed`.** The mask cannot tell a missed id from `/v2/` → `/v3/`, which
pairs 1:1 too and is exactly the change the verdict exists for. This makes every future templater gap
announce itself, which is why it is worth having even after S1.

**S5 — the mirror image: say what the templater took.** S1's rules can be wrong in a URL scheme nobody
here has seen, and a wrong `{id}` is silent: two different routes collapse into one line and a real
change disappears. `kronikol query history sN --calls` (or a line under `calls:`) prints the
scenario's templated call lines, so a reader can see `/assets/app.{id}.js` and object. The `shapes`
ledger line already holds them; this is a renderer change only.

**Not taken — claims-aware templating** (#75 §1e). The reporter's customer ids are numeric and already
`{n}`; what is left is 12 UI-label lines (0.8%). Claims exist only on the ingest lane
(`TestRunRecord.Claims`), so it would need plumbing through `Scenario` to `HistoryRunBuilder` for a
gain S5's hook already delivers.

**Not taken yet — discounting degraded runs** from flips and fail rate (#83 ask 3, "default on"). A
failure in a degraded run is still a failure; `history gate` reads these numbers; and although the
threshold is now measured from both sides (§6.2), **no run made here produced a failure to discount**,
so what discounting would do to a real flip rate is still unseen. Label first. Revisit with a ledger
that holds a failure in a degraded run, as `HistoryIgnoreDegraded`, default **off**. This is not in
tension with F8: a duration read in a degraded run is a measurement of the machine and is dropped as
S3 drops a partial run's; a failure in one is still a fact about the test and is kept, labelled.

**Open question for the user — cross-test mis-merge.** Because the merger ignores `TestId`, two
workers hitting the same collection within the same 2 ms can pair a wire record with the *other*
worker's span today, putting one customer's payload in another's scenario. S2 does not make this
worse (§4.2). A cheap detector: after the claims pass, count merged records whose content matches
exactly one in-flight claimant that is *not* the adopted test, and report it. Worth an issue of its own
once that count has been seen non-zero.

---

## 9. Versioning, docs, parity

- **Bumps** (CLAUDE.md): S1 and S2 are bug fixes that change observable output → **patch**, with the
  behaviour change called out. S3 adds a public member and a result character, S4 and S5 add options →
  **minor** each. After F8, S4 also changes when `slower` fires (never in a degraded run, never
  against one); that rides the same minor, called out in the changelog as S3's duration change is.
  Nothing here is MAJOR even on a strict reading *given the standing permission above*;
  the relabelled p95 line and the new JSON members are changes to a feature with no users yet. If that
  permission is narrower than read here, S3's label change is the one to rule on.
- **One release per slice**, each: all packages to the same version, changelog stating which part
  moved and why, tag, push.
- **Wiki** (`../Kronikol.wiki`): `Cross-Run-History.md` (templating rules and rule 4, partial-run
  semantics per §5.1, the `N` result, pace and `HistoryDegradedBy`, `HistoryShapeTemplates`),
  `Ingesting-External-Captures.md` (merge-then-attribute order, the contested-claims diagnostic, the
  sweep advice of §4.3), `Querying-Reports.md` (the new `history sN` lines and JSON members).
- **Skill and agent docs:** the string `p95 of earlier` appears nowhere outside the tool, the analyzer,
  their tests and the changelog (RUN, grep), so the relabel touches no skill or template. S4's new
  `history sN` lines are worth one row in the `kronikol-test-debugging` skill's recipe table; the
  drift guards compare the copies.
- **Kronikol4J:** no history, templater or merger exists there (RUN), so no port work. One
  divergence-ledger line for S2, because it changes `TestRunReport.json` content on the ingest lane.
- **`plans/PLANS_STATUS.md`:** row added with this plan; update it per slice.
- **Issues:** comment on #75 with F1, F2 and F7 before starting — each changes what the reporter
  should expect from the fix they proposed, and F7 says not to build §2a as written — and on #83 with
  F4, F5 and F8, where F8 comes with a question only they can answer: how many `slower` verdicts did
  their degraded run hand out, and did any scenario fail in it besides the one? Posting is the
  user's call; nothing has been posted.

---

## 10. Assumption ledger

Every load-bearing claim in this plan, with how it is known — the form of
`CROSS_RUN_HISTORY_PLAN.md` §17. §1 holds the transcripts; this section is the bookkeeping, including
what is **not** known.

### 10.0 The error class this plan has made, and the rule that catches it

Eleven of this plan's own statements were reversed by checking them, inside two days. They are the
history plan's mistake again, unchanged: **a claim about existence or shape was verified, and allowed
to transfer to a claim about behaviour that was not.** Two came from the issues and were nearly
inherited; nine are this plan's own drafts. Three fell to the first real partial and degraded runs
(§1.2) — every one of them had survived two rounds of reading — and the last to a stopwatch, after
another plan (`HISTORY_ANALYZER_COST_PLAN.md`) found the figure it leaned on to be somebody else's.

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| The merger never reads `TestId` when matching (`KeyOf`, `MatchPairs` — true) | so merging *before* attribution changes which records survive, not which records pair | the drop that runs first today **shrinks the merger's candidate set**. Merge first and a stranger's record competes, wins on start distance, and worker-a's scenario shows two Mongo calls, one carrying another customer's payload (RUN, F7). All 124 existing Ingestion tests passed under the wrong design — none has a stranger in it |
| #75 §4 names the cause correctly: partial runs sit in the alternating `memory` | so taking them out of `memory` is the fix (#75 §4a) | built: the first full run after the partial one reads **`behaviour-changed`**, because `previousShaped` is still the partial point (RUN, F2) |
| #83 prints `82215 ms` and `p95 of earlier runs 14731 ms` | "that run took 5.6× its p95" | the bar is scaled to the speed of the *current* run, a 34-scenario filtered re-run; the two numbers are not in the same units. Against the scenario's own full-run median: 6.4× (F4) |
| S2 moves when records are attributed, and diagnostics count attributions | existing tests will need their diagnostics counts re-pinned | none moved, in either S2 design |
| BreakfastProvider's ledger replays with no verdict count moving under S3 | S3 is accepted on a real ledger | true and empty: **no partial run exists** in 304 CI runs or 11 local ones, so the filter is the identity there |
| The relabelled string `p95 of earlier runs` is user-facing | the skill's recipe table quotes it and must change | grep: it appears in the tool, the analyzer, their tests and the changelog, nowhere else |
| Pace is "the scenario's median over the full runs in the window, the run itself excluded" | S4 must cut the ledger at each run "or a degraded run is judged against its own future" | the definition is leave-one-out over the analysis window and already inherits whatever cut the analysis has; the sentence contradicted the definition it sat under, and is gone |
| #83's filtered runs were 10× faster than its full ones | "partial runs … run *faster*" (§6.2, as a reason they are never degraded) | the first real partial run made here ran a median **1.32× slower** per scenario, up to 4.7×: the cold start, shared among 33 scenarios instead of 203. The rule is that a partial run's durations are not comparable, in either direction; one suite's direction had been written down as the rule |
| `slower` needs the previous reading over the bar as well as this one; 3.16.0 reads every scenario against its run's speed | so one degraded run cannot produce a `slower`, and S4 may "label and never discount" | contention is not a uniform slowdown (p25 2×, p90 54× in one run), the median cannot absorb it, and "previous over the bar" is an ordinary state for a handful of scenarios in any run. **5, 20 and 7 false `slower` of 203 on `main`**, and 36 bars more than 2× too high in the healthy run after (RUN, F8) |
| Pace over 304 healthy runs never exceeds 1.44, a median over ~200 ratios | "Pace itself needs no floor", and 2.0 has "39% headroom" | the healthy side was the only side measured. On real degraded runs the plain median read 2.25 for a run with twenty false verdicts: scenarios under 10 ms barely register load and were half the votes. With a 10 ms floor on the usual the same run reads 6.85 and the worst healthy run 1.56. The 100 ms floor tried first makes it *worse* (4.98 for 7.38) — so "a floor hurts" was also measured, and also wrong as a generalisation |
| The history plan's ledger says "Analyzer cost is 65 ms at 5,000 × 50 — RUN"; pace is 250k divisions | "Pace costs nothing noticeable" (COMPUTED) | the 65 ms was a prototype harness, not the analyzer (#91's plan: 2,060 ms shipped, 200–310 ms fixed), and the prototype's pace is 100–115 ms to build plus 42–53 ms to read at that size (RUN). A DOC figure was used as the denominator of a COMPUTED one, and neither had been timed |

The first row is the one to remember. It had a correct citation, a plausible argument and a green
test suite beside it, and it was the issue author's proposal as well as this plan's. The rule that
caught it is the history plan's, and it is mechanical: for any claim that decides what gets built, do
not ask *how do I know this is true* — ask **what would I have seen if it were false, and did I look
there?** "There" was one test with a third record in it.

The last three rows add the other half of the rule. Each was filed honestly under "not verified" with
the reason "no such run exists in any ledger here" — and the run could be **made**, in twenty minutes,
on a suite in the next directory. A missing measurement that can be manufactured is not an open
question; it is a job. What those runs cost was an evening; what they changed was what S4 *is*.

### 10.1a Existence and shape — cheap to check

| Claim | How verified |
|---|---|
| `IdPattern` knows hyphenated GUIDs, hex runs of 16+, ULIDs, and nothing else; `TemplateStatement` shares it | `InteractionShape.cs:54`, `:85`, `:134` read |
| Pipeline order today: `Attribute` (claims → window → phase → `DropUnattributed`), then `Order` → `Merge` | `IngestPipeline.cs:341`, `:349`, `:559–596`, `:716` read |
| `Adopt` sets `TestId = span.TestId` with no condition | `InteractionMerger.cs:282–290` read |
| Matching key is caller, service, first word of the method, last path segment; then overlap ≥ 0.8 of the shorter interval; ties by start distance, then index | `InteractionMerger.cs:171–176`, `:210–254` read |
| Partial runs enter `RunSpeeds`, `timed`, `shaped`, and through `shaped` the `memory`, `changes` and `counted` lists | `HistoryAnalyzer.cs:85`, `:320`, `:350`, `:358–370`, `:424` read |
| The only partial filter in the analyzer is `LastFull`, used for `absent` and the heuristic | `HistoryAnalyzer.cs:82–83`, `:142–148` read |
| The partial diagnostic says the run "is not the run the next one is compared against" | `HistoryRunContext.cs:140` read |
| `Partial` is resolved to true or false when the line is recorded, so a ledger line rarely holds null | `HistoryRunContext.cs:135–144`, `HistoryCommand.cs:324–328`, `HistoryCommand.Maintenance.cs:439` read |
| A roster position is the scenario's `sN` address, so the fold scenario cannot simply leave the roster | `HistoryRunBuilder.cs:47–57` read, and its own comment |
| The fold scenario is special-cased in one place already | `IngestPipeline.cs:361–370` read |
| Nothing validates a result character; two renderers switch on it | grep of `src/`; `HistoryHtml.cs:264–278` |
| Claims declared mid-run apply to the whole test window, retroactively | `IngestAttribution.BuildClaimWindows` (`:200–223`) read |
| The shard fold rebuilds the run line field by field and takes `ShapeVersion` from the first shard that has one | `HistoryLedgerWriter.cs:379–409` read |
| A rule change costs one quiet run, and a same-rule run further back is still compared against | test-pinned: `Shapes_made_by_an_earlier_rule_are_not_compared`, `HistoryAnalyzerTests.cs:311` — green under the prototype |
| Kronikol4J has no history, templater or merger | grep of `../Kronikol4J`, zero hits |
| The patch applies to `main` | `git apply --check`, against `2491185e` (where it was made) and `125a32cc` (where `main` stood when this was written) |

### 10.1b Behaviour — the kind that gets reversed

`RUN` = executed here and the output read; `COMPUTED` = arithmetic by hand; `READ` = source read and
reasoned about; `DOC` = somebody else's statement taken at its word. **A `READ` row is not evidence
that the value reaches anything.**

| Claim | Depth | How verified |
|---|---|---|
| Every repro test of #75 fails on `main` as the issue says; its controls pass | **RUN** | 18 tests on unmodified `2491185e`: 10 red, each as predicted |
| `p95 = 32133` for the issue's first analyzer test | **RUN** + COMPUTED | the test's own failure message; by hand first, 9079 / median(97, 739, 2482, 11110) × 5700 |
| The S1 patterns give exactly the issue's three expected strings, and `{bin}` for the fourth; every existing templater pin holds | **RUN** | in the analyzer's own test project (182 History tests), and separately with the same .NET regex in PowerShell |
| The S1 patterns move 0 of 215 / 0 of 200 / 0 of 7 distinct call lines in three real ledgers — statement heads included | **RUN** | the ledgers' `shapes` lines, re-templated |
| The short-hex rule has no false positive in either codebase | **RUN** | 1,745 files, 628 URL or path strings, 9 matches, all groups of full GUIDs |
| #75 §4a alone yields `behaviour-changed` | **RUN** | built as the issue states it, run, then replaced |
| S3 as planned: p95 in range, `slower` found, both following full runs `stable`, nothing else moves | **RUN** | 182 History tests green |
| A merge trades a known test for the fallback id (F6) | **RUN** | red on `main`: both merged records `session` |
| Merge-first lets a would-be-dropped record take a span twin (F7) | **RUN** | the stranger test: green on `main`'s order, red on merge-first, green on the redesign |
| A contested wire record with no span twin is dropped under every order (F1) | **RUN** | the Redis pin |
| S2 as redesigned breaks nothing | **RUN** | 126 of 126 Ingestion tests |
| S1 + S2 (redesigned) + S3 together break nothing in the unit suite | **RUN** | **5,158 tests, 0 failed, 1 skipped** (5,156 in the first round, before the two new pins) |
| A run's pace over 394 healthy CI runs never exceeds 1.56 (1.63 without the 10 ms floor); none is labelled | **RUN** | the C# prototype's `RunPaces`, every stream's every run read leave-one-out. It replaces the first round's `pace.ps1` figure (1.44 over 304 runs, priors only), and agrees with that script to the digit on the four local ledgers where both ran |
| A real partial run moves no verdict under S3, moves 5 of 203 bars by ≤ 7 ms, ran 1.32× slower per scenario, and its own bars stood at 1.88× the full run's | **RUN** | the replay probe on a full · partial · full ledger made on BreakfastProvider, `main` against prototype (§1.2) |
| Contended runs read pace 7.38, 6.85 and 20.93; pinned-only runs 1.00, 1.00, 1.47, 1.68; healthy local runs 0.93–1.22 | **RUN** | seven runs made for the purpose (§1.2), read by the prototype |
| On `main` those contended runs hand out 5, 20 and 7 false `slower`; with the degraded rule 0, 0, 0 and nothing else moves; without it 47 of 198 bars in the next healthy run are > 1.5× too high | **RUN** | the probe built from unmodified `main` and from the prototype over the same ledgers; both directions also as unit tests over a prototype-only switch |
| The median over scenarios with a usual ≥ 10 ms separates healthy from degraded by 4.4×; the plain median by 1.6×, the share at ≥ 2× by 1.3× | **RUN** | seven statistics × (394 + 13 + 7) runs, `stats.*.csv` in the evidence archive |
| A sustained 3× shift after fifty healthy runs is degraded up to the 25th run and not from the 26th | **RUN** | a theory in `NoisePlanDegradedTests.cs` |
| S1–S4 together break nothing in the unit suite | **RUN** | **5,187 tests, 0 failed, 1 skipped** |
| BreakfastProvider's in-memory suite has a median scenario duration of 6–9 ms | **RUN** | its local ledger, 11 runs |
| After the move, `Order`'s second `Merge` call is a no-op | **READ** | any pair available to a second pass was available to the first, which is greedy only in skipping *used* records. The plan removes the call regardless, so nothing rests on this |
| SQL and document heads are safe under the short-hex rule | **RUN** | the 215 ledger lines include statement heads, and eight constructed heads were tried (§10.3 item 6): nothing taken from an identifier, parameter or keyword |
| Status verdicts read partial runs exactly as before S3 | **READ** | the filter touches `comparable`, which status never reads; S3 test 4 pins it |
| Pace costs 100–115 ms to build and 42–53 ms to read at 5,000 × 50 (Debug) — against an analyzer of 2,060 ms on `main` and 200–310 ms after `HISTORY_ANALYZER_COST_PLAN.md` | **RUN** | `Cost_of_pace` in the probe, five attempts. It replaces "costs nothing noticeable — COMPUTED against a measured 65 ms", of which neither half was true (§10.0) |
| The reporter's suite behaves as described: 1,494 lines, the 56/26/14/3% split, 784 dropped records, the 17/30 Mongo split | **DOC** | #75; private suite |
| In #83's incident exactly one scenario of 396 was affected | **DOC** | #83 |

### 10.2 Not verified — and what this plan does about each

| Assumption | Status | How the plan avoids depending on it |
|---|---|---|
| The reporter's numbers | **Private; taken from the issues** | Every mechanism was reproduced on synthetic data before anything was designed from it. The only figure sized from theirs is "S1 removes 73% of the lines", which orders the slices and changes nothing in them |
| `HistoryDegradedBy = 2.0` catches a degraded run | **Measured on one suite, one kind of load**: 394 + 13 healthy runs at most 1.56, three CPU-contended runs at least 6.85, pinned-only runs (1.47, 1.68) unlabelled and harmless. Not measured: I/O or memory pressure, a suite of long scenarios, a suite with external stores | After F8 the threshold gates `slower` as well as a note, so a threshold that is too low silences `slower` for a run and one that is too high lets false ones through — both visible, both one run long. It is an option; the run-level pace prints on every surface, so a consumer can see where their own runs sit before trusting it |
| A failing row in a degraded run carries the right note | **Unit test only** — no starved run here produced a failure, up to 2.6× the wall time | The note states a fact about the run and makes no claim about the failure; §6.1's caution stands |
| The run-level signal would have fired in #83's own incident | **Probably not** — one scenario of 396 lost the race | The per-reading `(N× usual)` note ships with it and makes no causal claim; §6.1 says which note answers which question |
| `{bin}` handles binary keys other than the one example in the issue | **One sample** | The rule is scoped to U+FFFD and C0 escapes, which text does not contain. What it misses, S5's hook takes; what it wrongly takes, the rule version and the `--calls` view expose |
| The short-hex rule is safe in URL schemes nobody here has seen | **Cannot be measured from here** | 8–15 characters, a digit *and* a letter, whole token only. `--calls` (S5) prints what the templater took, so a wrong `{id}` is visible rather than silent; `InteractionShape.Version` makes any later correction cost one quiet run |
| A twin that is itself unattributed, out-competed by a stranger, mis-pairs | **True on `main` today, and after S2** | Not made worse (F7's redesign is exactly the guarantee of that). §8's detector counts it; §10.3 item 3 |
| The Playwright and per-framework suites pass with the prototype | **Not run** | Nothing in S1–S3 touches report HTML or an adapter; each slice's release runs the full suite anyway (CLAUDE.md) |
| S4's surfaces, S5, the naming of the other alternating state, the `N` result, the relabel, the contested-pair diagnostic | **Not built — designed from READ.** S4's analyzer *was* built in the third round, and being built is what redesigned it | Each has its red tests listed first; three rounds have now shown what a READ design is worth, so S5's first tests are its acceptance, not its decoration |
| Whether a consumer's partial runs change its calls (#75 §4) | **Depends on how it attributes**: not at all on BreakfastProvider (in-process, by the current test), heavily on the reporter's (ingest, by window) | S3's filter is the same either way and costs nothing where there is nothing to filter |
| The shard fold carries new run-line fields | **Known not to** (§8) | S4 stores nothing on the run line; S5's `shapeRules` is added to the fold in the same commit, with a test that folds two disagreeing shards |
| The constants: 8–15 hex, five qualifying scenarios for a pace, top three contested pairs, the 100 ms floor on `(N× usual)` | **Chosen, not derived** | Each is one constant or one option; the text surfaces state them rather than hiding them |
| The constants 2.0 and the 10 ms floor on a usual | **Measured, on one suite** (§6.2): 10 ms is where BreakfastProvider's scenarios start to register load; 5 and 20 ms give 8.06 and 7.38 where 10 gives 7.38, so nothing hangs on the exact value | 2.0 is an option. The floor is a constant until a second suite says otherwise |

### 10.3 Open investigations

1. **~~A real partial run.~~ CLOSED (RUN, §1.2).** S3 moves no verdict and five bars by milliseconds;
   the partial run was *slower* per scenario, not faster; its bars showed F4 on a second suite; its
   calls did not differ, because BreakfastProvider does not attribute by window.
2. **~~A real degraded run.~~ CLOSED (RUN, §1.2, §6.2) — and it redesigned the slice.** The test this
   item set — "if it reads under 2.0 with the suite visibly crawling, the threshold is wrong" — came
   within 12% of failing for the statistic this plan had chosen (2.25, in a run with twenty false
   verdicts), so the statistic changed; and the runs showed that a label is not enough (F8).
3. **How often does a merge cross tests?** Count merged records whose content matches exactly one
   in-flight claimant that is *not* the adopted test. Needs a parallel ingest suite with claims;
   nothing here is one, the reporter's is. Zero retires §8's open question; non-zero makes it a slice.
4. **The reporter's replay.** Ask for M0's before/after CSV rather than the ledger. It is the only
   ledger with partial runs, contested claims and (perhaps) a degraded run all at once.
5. **Time-scoped claims** (§4.3) as its own issue: the only thing found that would bring the Redis
   half of #75 §2 back.
6. **~~The statement-head probe.~~ CLOSED (RUN, the same .NET regex).** Eight constructed heads — EF
   Core bracketed and quoted identifiers, `@__id_0` and `@p0` parameters, `e0`/`c` aliases, a Cosmos
   query, a Mongo command document, a ClickHouse query with `SETTINGS` — and the rule took nothing
   from an identifier, a parameter or a keyword; `0xDEADBEEF12` is left alone because the `x` fails
   the boundary. It took three things: two bare unquoted hex values on the right of a comparison
   (`lease = abcdef12`), which are data, and the suffix of `sales_a1b2c3d4e5`, a hash-suffixed table
   name — an id, and the same case as `job_<guid>`. These eight heads become S1's test 5 as written.
7. **A failure inside a degraded run.** None could be provoked on BreakfastProvider. A suite with
   tight timeouts would give one; the reporter's ledger has one (#83). Until then the failing-row note
   rests on a unit test.
8. **Other kinds of load.** Everything measured is CPU contention on an idle-dominated in-memory
   suite. Disk or network pressure on a suite with real stores, or memory pressure, may spread the
   ratios differently; the statistics CSV in the evidence archive is the template for measuring one.
9. **The armed set.** F8's false verdicts need a previous reading over the bar. How many scenarios sit
   there in an ordinary healthy run — 5 of 203 after one mildly loaded run, 20 after a degraded one —
   says how sharp the bar itself is (a nearest-rank p95 of a dozen readings is their maximum), and
   that is a question about `slower`, not about this plan.

---

## 11. Execution log

**Green-lit by the user on 2026-09-21** ("implement in full"). One release per slice, as §9 says.
**Executed in full on 2026-09-21: 3.22.2, 3.22.3, 3.23.0, 3.24.0, 3.25.0.** Not done, by §9's own word:
nothing was posted on #75 or #83 (posting is the user's call), and the reporter's replay (§10.3 item 4)
has not been asked for.

Two ordering requests of §2 were not followed, because the plans they name are themselves not green-lit:
`HISTORY_ANALYZER_COST_PLAN.md` (#91) has not landed, so S3 and S4 filter the `points` loop as it stands
on `main` and that plan redoes one hunk when it lands; and `EVIDENCE_SURVIVES_A_RERUN_PLAN.md` S1/S2 has
not landed, so S4's row note goes where §6.3's before-and-after shows it, ahead of the error.

| Slice | Release | What happened that the plan did not say |
|---|---|---|
| M0 | with 3.22.2 | `tools/history-replay` (csproj + `Program.cs`, public API only; pace and degraded read by reflection so the same source builds at a base commit from before S4). Base replay of the evidence ledgers reproduces §1.2: 5 and 20 `slower` in the two contended runs of the chain ledger. The CI ledger has grown to **430** runs. |
| S1 | **3.22.2** (patch) | As designed. Re-templated the real ledgers again: 0 of 212 (CI) and 0 of 200 (local) distinct call lines move. One limit found while writing test 5 and pinned as a limit: an identifier of eight or more characters spelt wholly in hex with a digit (`c.deadbeef1`) is taken; it is templated the same on every run, so it never reads as a change. Unit suite 5,178 / 0 failed. |
| S2 | **3.22.3** (patch) | As redesigned in the second round. Three things the plan did not say. (1) `Merge` did not gain an optional parameter: that is a binary break on a public method, and a new public overload would have been a minor bump, so the identity-aware form is `internal` and the public signature is untouched. (2) When the wire record's test stands, its **name** stands too (`Adopt` took the span's name first, which for an unattributed span is the placeholder). (3) The contest is named as the sorted claimants and the first claim the record held, top three by records, with "and N more". Red first: 5 of 9 new facts failed on the stub (the repro with the sweep overlapping, the rescued-record counts, the diagnostic, F6 and the name); the control, F1's Redis pin, the stranger and the no-contest equivalence passed before and after. Test 3 (byte identity) is held as an equivalence, because no fixture with both views exists to diff: the pipeline's merge equals merging the sorted input by hand and ingesting that with the merge off. Test 5: no existing count moved (195 existing Ingestion facts untouched); the new fixture pins that a rescued record is neither "dropped by DropUnattributed" nor "could not be attributed". The wiki had never documented `AttributeByClaims` or `ExclusiveOnly` at all; it does now. Unit suite 5,187 / 0 failed. |
| S3 | **3.23.0** (minor) | As designed, on the `points` loop as it stands (the cost plan has not landed). Red first: 9 of 15 new analyzer facts failed with only the members added. Decisions the plan left open: (1) the raw bar of a partial current run is the p95 of **all** the scenario's full-run readings; the previous reading is held out of a scaled bar so its spike cannot lift the bar it is judged against, and a partial run judges nothing. It is flagged (`ScenarioHistory.DurationP95IsRaw`) so the label can say so. (2) An `N` position returns early as `stable` with the evidence "not a test: it collects the traffic no test could be given", runs seen 0, and is excluded from `absent` by the previous full run's own `N`. (3) The partial diagnostic was reworded rather than left: status still reads a partial run, so "is not the run the next one is compared against" was still only half true. (4) The sparkline tooltip names a partial run's row as well. Known limit, stated in the changelog: a run rebuilt from the report alone cannot know the fold scenario. **Replay (M0 acceptance):** CI ledger 430 runs, 0 lines differ, bars included; chain ledger, no verdict count moves, the partial run's bars sum 11,437 to 5,458 (the 1.88x of §1.2 undone), the four full runs after it move by 14 to 225 ms summed over 203 bars. |
| S4 | **3.24.0** (minor) | The fourth-round design (row from two readings, run from `MinRuns`), which the prototype patch beside this plan predates: the patch still makes both wait for `MinRuns` and carries the `DegradedLeavesDurations` switch; neither shipped. Red first: 16 of 21 analyzer facts failed with only the members added, both directions of F8 among them. **Replay (test 10, the slice's acceptance), to the digit:** chain ledger pace 7.38 and 6.85, `slower` 5 to 0 and 20 to 0; one processor with six hogs 20.93, 7 to 0; pinned two and one processors 1.47 and 1.68, unlabelled; CI ledger, 430 runs, 340 paced, highest 1.50, none labelled, no cell moves against S3. **One figure of §6.2's table is a slip:** the runs pinned to 8 and 4 processors read **1.07 and 1.05**, not "1.00 / 1.00"; the evidence archive's own `stats.8core.csv` and `stats.4core.csv` say 1.07 and 1.05 too. Nothing rests on it. **Cost, measured instead of inherited:** `history-replay --bench`, 5,000 x 50, two same-session pairs, 2,041 and 1,948 ms at the base commit against 2,131 and 1,928 ms with S3 and S4: inside the analyzer's own noise. The cost plan's position map does not exist yet, so the usual is keyed by one dictionary lookup per scenario of each DISTINCT roster (reference-keyed), arrays after, and a scenario's `TimesUsual` readings are binary searches in its own sorted array. Decisions the plan left open: `OverUsual` is computed by the analyzer (the two-part test needs the options, and three surfaces print it); `HistoryDegradedBy = 0` switches the feature off, since the threshold now gates a verdict; `history gate` takes the flag and prints an advisory; the sparkline tooltip names a degraded run's row. Found and fixed on the way: the skill's vocabulary table had never listed `alternating`. Unit suite 5,232 / 0 failed; 12 history Playwright facts green. |
| S5 | **3.25.0** (minor) | All three parts: `HistoryShapeTemplates` (types `HistoryShapeTemplate`, `HistoryShapeRules`; `shapeRules` on the run line; the analyzer compares the pair), the "only ids differ" hint, and `history sN --calls`. The ledger-writer trap of §8 was real and wider than stated: the fold also took `shapeVersion` from the first shard that had one, so shards on two Kronikol versions already folded into one line claiming one rule. Shards that disagree on the pair are now recorded without fingerprints and `kronikol history record` says so (`HistoryFoldedRun.Note`). Decisions the plan left open: the placeholder is a replacement pattern (`$1` works); the hash is over the rules as configured, compiled or not; the time limit is 250 ms per rule per text, and a rule that times out is skipped for that text and said once; the query string is not offered to the rules (its values are dropped anyway); the hint masks runs of letters, digits, hyphens and underscores that hold a digit and never the status, so 200 becoming 404 is not an id; `--calls` prints the current run's lines from the fragment and says when there is none. The timeout fact first failed for a reason worth keeping: forty `a`s are a hex run of sixteen or more, and the built-in rule took them after the consumer's rule had timed out. **Replay:** nothing moves against S4 on any ledger. `kronikol merge` and `kronikol ingest` take no rules (fixed options); stated, no flag added. Unit suite 5,247 / 0 failed. |
