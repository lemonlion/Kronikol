# Evidence survives a re-run plan (#80, #81, #82, #84)

**Date:** 2026-09-18, investigated further 2026-09-19 (§11.5) · **Repo version:** written against
3.21.0 (`2491185e`), re-checked against 3.22.1 (`82abeb7f`) — 3.22.0 and 3.22.1 touch no history,
query, merge or ledger code, and the one citation that moved is `ReportGenerator.WriteFile`, now
`:5420` · **Status: EXECUTED 2026-09-21 as 3.25.3 (S0, S1), 3.26.0 (S2, S3) and 3.27.0 (S4), then
audited item by item on 2026-09-22 - the four gaps found fixed as 3.27.1, §9 run on BreakfastProvider
and the two defects it found fixed as 3.27.2, then on 2026-09-23 the leftovers dealt with at the owner's
word (open question 6 shipped as 3.28.0, the real `--retry-failed-tests` run repeated on the shipped
packages, the four issues answered and closed); §12 is the log and says where execution departed from
the plan.** As written before that: plan written, **the analyzer cut (F4), the missing-fragment fix (F14) and the
retry overlay (F15) prototyped in a throwaway worktree** — `EVIDENCE_SURVIVES_A_RERUN_PLAN.prototype.patch`,
8 files, +456 −4, every new test red on `main` first, the unit suite 5,160 / 0 failed with it — nothing
implemented in the repository, **NOT green-lit**.

Covers GitHub issues **#80** (a later run overwrites the failing run's report), **#81** (`history`
cannot read the ledger without a report), **#82** (`history` truncates the failure message) and **#84**
("no scenario with a verdict (failing)" reads as "nothing failed"). They are one plan because they are
one debugging session — a failure, an instinctive re-run, the evidence gone — and because #80 and #81
both need the same thing: **one address for a run**. This is "plan 2" of
`OPEN_ISSUES_TRIAGE_2026-09-18.md`, which grouped the issues without checking them against the source;
this file is that check.

**Standing permission (user, 2026-09-18, given for this plan and recorded in
`HISTORY_VERDICT_NOISE_PLAN.md` too):** breaking changes to recent features are fine, because nobody
is using them yet. `kronikol query history` and the ledger are recent (3.9.0+), so
S1–S3 rename text and `--json` members freely and carry no shims. The **reports directory layout is
not recent** — every consumer's CI globs it — so S4 keeps the top level byte-for-byte as it is today
and is careful about its default (§6.4). MAJOR is never bumped (CLAUDE.md: ask first) — §8.

---

## 0. Summary

Every issue describes a real dead end, and the code confirms the mechanism of each. Reading and
running it also finds fifteen things the issues do not say. F2, F4, F9 and F10 change a proposed fix,
F11, F12 and F14 are bugs that are live today, and F15 reverses this plan's own remedy for F13. §11 is
the assumption ledger: what was run, what was
only read, and what is not known.

| # | Finding not in the issues | Where |
|---|---|---|
| F1 | **#81's "exits 0" does not reproduce.** The usage error has returned 2 since the verb shipped (3.0.47), returns 2 on 3.21.0, and returns 2 through `dnx` as well. It reads 0 only behind a pipe (`… \| head; echo $?` reports `head`'s code). Nothing to fix; one test to pin it, one reply on the issue. | §3.4 |
| F2 | **#84's threshold explanation is wrong, so the proposed hint would be wrong.** The scenario had `verdicts: 5`, which *meets* `--min-runs 5`. It is not flaky because `P P F P P` is two flips but **one failing episode**, and flaky needs two (`HistoryAnalyzer.cs:295-301`). A hint that blames `--min-runs` would send the reader to lower a bar that was never the obstacle. Same fact as F5 of the noise plan. | §5.2 |
| F3 | **#82's truncation is not terminal width — it is a fixed 80** (`QueryCommand.History.cs:186`), and the "full text" in `--json` is itself capped: the ledger stores only the **first line, ≤ 199 chars** (`HistoryFormat.ErrorKeyLimit`, `HistoryRunBuilder.cs:96`). Un-truncating the view fixes the issue's case (125 chars) but the *whole* error only ever exists in the report — which is #80. | §4.1 |
| F4 | **A reading of an older run counts the future as the past — run, not inferred.** `AnalyseStream` takes as "prior" every run of the stream except the current id (`HistoryAnalyzer.cs:74-76`); it never cuts at the current run. Asked about the 12th of 23 runs in BreakfastProvider's CI ledger, the analyzer reported **22 earlier runs, 11 of them dated after it**, and named a run from three days later as the previous one. Harmless today only because the current run is always the newest; wrong the moment `--run` or a retained report names an older one — and already wrong for anyone querying a downloaded CI report against a ledger that has moved on, which is the verb's own stated use (`QueryCommand.History.cs:10-12`). | §3.2 |
| F5 | **The ledger reader's window can hide the run being asked for.** `HistoryLedgerReader.Read` keeps the last 50 runs *per suite*, not per stream (`HistoryLedger.cs:137`, `:232-238`), and `query history` has no `--window` (`QueryOptions.cs:115` is a constant). Theoretical on the ledgers measured here (one stream per suite in all 18 CI suites); real for a ledger that local re-runs and a branch stream share. | §3.2 |
| F6 | **A `runs/` folder breaks three recursive sweeps — run, and worse than double-counting.** `QueryCommand.ResolveReport` (`:366`), `MergeCommand.ResolveInputFiles` (`:298`) and `HistoryCommand.FragmentFiles` (`:375`) all search `AllDirectories`; the first two exempt only `baseline/`, the third nothing. Unfixed, `kronikol merge ./Reports` takes a retained run as a shard. An identical copy is de-duplicated (11 scenarios stay 11), but a run that *differs* is merged in: a green report with one retained failing run beside it came out as **12 scenarios, 1 failed** — the old failure listed next to its own green re-run, in the report and in `Failures.md`. | §6.3 |
| F11 | **`kronikol merge <reports-dir>` is already broken by the file history writes — a live bug, independent of this plan.** The sweep takes every `*.json` but `*.schema.json`, and the reader throws on one without `features`. A directory holding `TestRunReport.json` merges (exit 0); add the `History.run.json` every history-enabled run has written since 3.9.0 and it is `Failed to read a report: …History.run.json: Not a recognised Kronikol test-run report`, **exit 1**. `ctrf-report.json` has the same shape of problem, and S4's `Run.json` would be a third. No merge test has a fragment beside a shard. | §6.3 |
| F7 | **A run id is not a directory name.** Every form has two colons (`local:<stamp>:<hash>`, `gh:<id>:<attempt>`), illegal in a Windows path segment, and the repo's existing sanitising idiom (`Path.GetInvalidFileNameChars`) gives a *different* name on Linux. No run-id-to-path function exists. | §6.2 |
| F8 | **Retained copies of `CLAUDE.md` would each claim to be the run.** The per-directory agent file says "You are in a Kronikol reports directory… if `Failures.md` is absent, the run did not finish", and a nested `CLAUDE.md` auto-loads when an agent reads any file beside it. Five retained runs must not mean five of those. | §6.2 |
| F9 | **On CI a run id does not identify a report.** `gh:<runId>:<attempt>` is shared by every step, shard and test project of one workflow run (`HistoryRunBuilder.cs:168-170`), and the ledger treats a second line with the same suite and id as a `Duplicate` (`HistoryLedgerWriter.cs:136`) or a shard to fold (`:279-303`). So the CI case #80 names — "a second workflow step that runs a subset" — produces two reports with **one** id. `runs/<runId>/` cannot be the directory name on its own, and "same id as the current run → do not rotate" would skip rotation in exactly that case. | §6.2 |
| F10 | **A green run's `Failures.jsonl` is not empty.** It carries a header line with `failures: 0` (`FailuresDigestGenerator.cs:779-796`, added so that "empty" and "never written" stop reading alike); the record's own doc comment (`:12`) still says "empty when nothing failed". Anything deciding "did that run fail?" from the file's size is wrong. | §6.2 |

| F12 | **A query and a finishing run already collide, both ways.** `kronikol query` holds the report with read-sharing only, so on Windows a run that finishes while a query is open cannot replace its own `TestRunReport.json` (run: `IOException`, for a move and for today's overwrite alike). And between the index scan and a payload read nothing checks the file is still the same one: a replaced report is read at the old offsets and the bytes returned as the answer (`PayloadReader.cs:27-44`). S4's rotation makes the first more likely; the fix for both is small and goes in S0. | §6.2, §11.3 |

| F13 | **A test runner's retry extension is #80, automatically, inside one command (RUN, §11.4).** `--retry-failed-tests` (Microsoft.Testing.Extensions.Retry — any MTP runner) re-runs the failed tests in a **new process**, which overwrites the reports directory two seconds after the failing run wrote it: `Failures.md` then reads "# No failures — All 1 scenarios passed". Off CI the ledger still holds both runs. **On CI it does not**: only `History.run.json` is written, the retry overwrites it, and `history record` stores one run of one passing scenario — the failure never reaches the ledger at all. This reverses two of this plan's own statements: "a fresh CI checkout has nothing to rotate" (§6.4) and "exempt `runs/` from the `record` sweep" (§6.3). | §6.3, §6.4, §11.4 |

| F14 | **A local report read without its `History.run.json` is read against its own ledger line — a live bug (RUN, §11.5 row 2).** Without the fragment the tool mints the run id itself, and a local id is salted with `AppContext.BaseDirectory` (`HistoryRunBuilder.cs:179-185`) — the *tool's* install directory, never the test project's. The same BreakfastProvider report is `local:20260918T230303Z:14e947b5` with its fragment and `…:a25831f6` without. So the run's own line counts as an earlier run: a scenario that **broke** prints `failing  PPPFF  failing since <its own run>, 2 runs`, and `--regressed` misses it. It also means F4's cut and S4's `--run` cannot find the run they are asked about. CI ids come from CI metadata and are not affected. Prototyped: adopt the ledger's own line for the run, which brings the fingerprints back too. | §3.2, §11.5 |

| F15 | **F13's own remedy — "fold the retained attempt as a shard" — invents a scenario (RUN on the real retry fragments, §11.5 row 1).** A fold gives a scenario that ran twice a second *slot*, which is what slots are for (a `[Theory]` row repeated, the same suite on two matrix legs). For a retry it means: the retried run reads `broke` plus a **`new`** scenario of the same name, and the next run reads `fixed` plus that scenario **ABSENT** — "deleted". The ledger has had the right home since v1: `attempts`, which the analyzer already reads as *flaky — passed on retry 2 in this run* (`HistoryAnalyzer.cs:300`, history plan §5.8) and which no native lane has ever filled. Prototyped: fragments in **one reports directory** with one run id are attempts of each other and are overlaid, oldest first by the run's `at` (path order puts the newest first); fragments in **different** directories are shards and fold as today. No format change. | §6.3, §11.5 |

Also found along the way (CLAUDE.md: fix what you find): the managed block in this repo's own
`CLAUDE.md` and `AGENTS.md` has drifted from `templates/agents/CLAUDE.md` — it lacks the
`kronikol query history` line — and no test pins it (§7).

**Round of 2026-09-19 (§11.5), in one paragraph.** Four of this plan's own statements were reversed by
running them: F13's fold (F15, above); **F4's fix as worded** — it cut `prior` only when the run was
found among its *own stream's* runs, so the `--compare-branch` reading kept counting the future (RUN:
the red test stays red under that wording; it is green when the run is looked for among the *suite's*
runs); open question 6's command, which does not exist (`--baseline` takes no value); and §6.1's "every
existing pin holds unchanged", which the manifest breaks for `merge`. The corrected F4 cut was then
checked against the whole CI ledger: **394 runs read in place agree with the hand-cut replay on every
verdict and every evidence string, 0 disagreements**. Linux is no longer open: ext4 and overlayfs, moves
in 0.14–0.27 ms, 478 kills inside a rotation, 0 bad states. #91's cause was found on the way and is
the same one `HISTORY_ANALYZER_COST_PLAN.md` found independently; that plan owns it.

Four slices and one bug fix, each independently shippable, in this order:

| Slice | What | Issues | Bump |
|---|---|---|---|
| **S0** | The live bugs, each shippable today and alone: `kronikol merge <dir>` stops failing on the `History.run.json` beside a report (F11); a report read without its fragment stops being compared with itself (F14); and, conditionally (§11.3, §11.5 row 5), the query tool stops blocking a finishing run from replacing its report | — | patch |
| **S1** | The single-scenario view prints every stored error whole; list views that cut say so and name the address that does not | #82 | patch |
| **S2** | An empty filter says what it is empty *of* — this run — and points at what the window still holds, with the true reason a near-miss missed | #84 | patch |
| **S3** | `history` without a report: `--run`, `--sid`, `--window`; the analyzer cuts at the run it is asked about | #81, #84's partial-run hint | minor |
| **S4** | The last N runs are kept under `Reports/runs/<run>/`; `--run` on every verb opens one; a failing run is never the one pruned | #80 | minor |

Not taken: refusing to overwrite a failing report (#80 fix 2), a `--full` flag (#82 fix 1), tail-first
truncation of the *stored* error key (#82 fix 4, in part). Reasons in §7.

---

## 1. How far each claim was checked

Same marks as the noise plan (`CROSS_RUN_HISTORY_PLAN.md` §17.0: an existence check must not stand in
for a behaviour check).

| Claim | Level |
|---|---|
| `kronikol query history --history F --flaky` with no report exits **2** on 3.21.0; the same line piped through `cat` reports 0 with `PIPESTATUS[0]` = 2; `--json` emits the error envelope with `exitCode: 2` | **RUN** (the built `Kronikol.Tool.dll`) |
| The "No report given" line has returned 2 since it was written | **RUN** — `git log -S'No report given'` → one commit, `05b4e605` (3.0.47) |
| The run rows cut the error at a fixed 80 chars; the issue's visible text (`…BadRequest. inva…`) is 80 chars | **READ** `QueryCommand.History.cs:186` + **COMPUTED** (45 + 30 + 5) |
| The ledger stores the first line of the message, cut to 199 chars + ellipsis; `compact` drops error text outside the window | **READ** `HistoryRunBuilder.cs:96`, `HistoryLedgerWriter.cs:248-249` |
| `P P F P P` → `real.Count` 5 ≥ `MinRuns` 5, `episodes` 1 < 2 → not flaky | **READ** + **COMPUTED** `HistoryAnalyzer.cs:297-301`; agrees with noise plan F5 |
| Everything #84's hints need for scenarios *in the roster* is already on `ScenarioHistory` (`Failures`, `Flips`, `LastFailedRunsAgo`, `RealVerdicts`) — only the *reason* a near-miss missed is not | **READ** `HistoryVerdicts.cs:185-206` |
| The ledger holds what a report-less reading needs: rosters carry names, features and sources; runs carry results, durations, errors and a `ShapesHash`, so behaviour verdicts work too | **READ** `HistoryModel.cs:27-48`, `:162-244`; `HistoryLedger.cs:62-73` |
| `AnalyseStream` does not cut "prior" at the current run | **RUN** — a file-based app over `Kronikol.csproj`, `HistoryLedgerReader.Parse(ciLedger, 0)` then `Analyse` with `reqnroll-in-memory` run 12 of 23 (`gh:34939097672:1`) as current: `RunsRecorded` 22, 11 points dated after it, second-to-last point `gh:35320802737:1` (three days later) |
| The reader's window is per suite, and no measured ledger has two streams in one suite | **READ** + **RUN** — BreakfastProvider's CI ledger (`origin/kronikol-history`): 394 runs, 18 suites, one stream each; its local ledger: 11 runs, one suite |
| **Real error text is cut by the 80-char limit more often than not, and never by the ledger's cap.** Of the 8 distinct error texts in that CI ledger, **5 exceed 80 chars and 0 reach 199**. The longest four are FluentAssertions lines of 146–151 chars, and at 80 every one is cut *before its actual value*: `Expected _patchSteps.ResponseMessage!.StatusCode to be HttpStatusCode.NotFound {` — the `but found … 500` is what goes. #82's shape, on a second suite | **RUN** |
| `dnx Kronikol.Tool query history` exits 2 | **RUN** (PowerShell, `$LASTEXITCODE`) |
| On GitHub Actions every step of a workflow run shares one run id; a same-suite same-id line is `Duplicate` on append and a shard on `record` | **READ** (F9). No duplicate `(suite, id)` exists in either measured ledger, so the fold of a *re-run* as a shard is unobserved — open question 5 |
| A green run's `Failures.jsonl` has a header line with `failures: 0` | **READ** (F10) |
| `kronikol merge <dir>` over the `all-passing.mergeable.json` fixture: alone → exit 0; with a `History.run.json` beside it → exit 1 "missing 'features'" (F11); with `runs/x/` holding an identical copy → 11 scenarios; with that copy's first scenario flipped to Failed → 12 scenarios, 1 failed, `# Failures — 1 of 12 scenarios` (F6) | **RUN** (built 3.21.0 tool; the fragment was hand-written, but the failure is the absence of `features`, which no real fragment has either) |
| BreakfastProvider's CI uploads the **whole reports directory** as the artifact and copies it into the Pages site (`_tests.yml:410-416`, `reports_dir`); it does not glob for `TestRunReport.json` | **RUN** (grep of its workflows) — corrects this plan's first draft, and changes the argument for the CI default (§6.4) |
| Measured directory sizes, BreakfastProvider, six suites: `TestRunReport.json` 5.5–7.6 MB, the whole `Reports/` 12–22 MB | **RUN** |
| `kronikol history show` already reads a ledger with no report, and lists only suite totals | **READ** `HistoryCommand.cs:469-519` |
| Report files are written in place, never temp-and-move; nothing in the run path deletes anything; the run id exists before the first file is written and after the discovery-pass guard | **READ** `ReportGenerator.cs:168-181`, `:323`, `:461`, `:5403-5436` |
| With `KRONIKOL_HISTORY=off` no run id exists at all; `HistoryRunBuilder.RunId` is public and mints one regardless | **READ** `HistoryRunContext.cs:76-77`, `HistoryRunBuilder.cs:154-177` |
| Two writers bypass the ambient reports directory (`ComponentDiagramReportGenerator.cs:52`, `DiagnosticReportGenerator.cs:19`) | **READ** — it is why S4 rotates the *old* run rather than redirecting the *new* one (§6.1) |
| Every `query` verb that touches siblings of the report resolves them from `index.Directory`, so a retained run opened as a directory works if its siblings moved with it | **READ** `QueryCommand.History.cs:288`, `QueryCommand.Narrative.cs:103,168,297,654` |
| Measured report sizes: 82.7 MB (#85), 31.8 MB (#89), README's "10 MB" is stale | **ISSUE** / triage — drives the retention default |
| Kronikol4J has no query tool, no history, no reports-folder option: no parity work, one divergence-ledger entry for S4 | **RUN** (grep of `../Kronikol4J`) |
| The two skill trees are byte-identical and pinned; the `CLAUDE.md`/`AGENTS.md` block is drifted and unpinned | **RUN** (MD5) |
| The reporter's session (17-minute suite, Testcontainers) | **ISSUE** — BreakfastProvider reproduces its shape (§9) |

---

## 2. Slice order and why

```
S0 F11 ──── alone, first: it is a bug today and S4 would add a third file that trips it
S1 #82 ──┐
S2 #84 ──┴─ one patch release, one PR each; both edit WriteScenario / the empty-filter note
S3 #81 ───► S4 #80   (S4's --run is S3's --run, resolved one step earlier)
```

S1 and S2 are small, touch one file, and remove the two misreadings that cost the reporter the
session. S3 must precede S4: S3 defines what `--run` means and fixes the analyzer cut (F4) that S4
makes reachable from the other side (a retained report of an older run read against a newer ledger).

**Renderer conflict (both plans say this).** S1 and S2 edit `WriteScenario` and the run view; so do
the noise plan's S3/S4. Suggested interleave: **this plan's S1+S2 first** (a patch, no analyzer
change), then the noise plan, then this plan's S3 and S4. S1 moves the error onto its own line
precisely so that the noise plan's `(6.4× usual) [run degraded…]` labels have the row to themselves.

---

## 3. S3 — `history` without a report (#81)

*(Written before S1/S2 because it defines the address they point at.)*

### 3.1 What is wrong

`RunCore` refuses any verb with no `<report>` (`QueryCommand.cs:96-100`) and scans one before
dispatch, so `--history FILE` can only ever *redirect* a reading that is still anchored to whichever
report happens to be on disk. After a re-run that is a filtered green run of 34 scenarios, and the
failing run — still in the ledger, two lines back — cannot be asked about. `kronikol history show`
reads a bare ledger but prints only suite totals.

### 3.2 The change

**One address for a run: `--run ID`.** Accepted: a full id (`local:20260918T101611Z:ab12cd34`), any
substring of one that is unique in the window (`T101611Z`), and two aliases — `last-failed` (the newest
run of the stream with an `F`) and `previous`. Ambiguous or unknown → exit 2, listing the last ten
runs of the stream with their `P/F` totals, so the refusal is itself the index. `last-failed` is the
reporter's actual question and costs one loop.

**Ledger-only mode** is `history` with no `<report>`:

```
kronikol query history --failing                       # ledger found above the cwd
kronikol query history --history .kronikol/history.jsonl --run last-failed
kronikol query history --run T101611Z --sid 3fa9c0d1e2b47a65
```

- `RunCore` gains one branch before "No report given": verb is `history` and `File` is null →
  `HistoryFromLedger(options, …)`. Every other verb keeps today's refusal (DescribeTests `:81` pins
  that `--describe` is the only other report-less path; it gains a sibling).
- The "current run" is `--run`, else the newest run of the suite (`ledger.LatestRun`). Its roster is
  `ledger.Roster(run.RosterHash)`, its shapes `ledger.Shapes(run.ShapesHash)` — exactly the three
  arguments `HistoryAnalyzer.Analyse` already takes, so **the analyzer is reused unchanged except for
  F4**, and behaviour verdicts are on (the ledger line carries the fingerprints a report does not).
- Several suites in the ledger and no `--suite` → exit 2 naming them (the `--suite` flag exists).
- Scenario ordinals are per-report, so rows print `sid:<id>` where a report-anchored answer prints
  `sN`, and `--sid ID` selects one scenario (the `sid:` address kind already exists —
  `VerbTable.cs:296` — but a positional would be parsed as the report path, hence a flag). With a
  report present `--sid` works too, as a synonym for the `sid:` positional.
- The header says what it is: `ledger only — no report read; steps, calls and payloads need one
  (kronikol query failures <reports-dir> --run <id>, when the run is retained)`. After S4 the tool
  checks and says whether it is.

> **Built 2026-09-19 (§11.6 rows 5–8; `QueryHistoryLedgerOnlyTests`, 15 tests).** As designed, with
> five things the design did not say. *(a)* `last-failed` and `previous` are taken over the **suite's**
> runs (`--branch` narrows them): with no report there is no "current stream" until a run has been
> chosen. *(b)* **A report and `--run` together**: the report names the ledger and the suite, the flag
> names the run. Its own id is the ordinary reading; any other run is read from the ledger under a
> banner — `! <report> describes <its run> — reading <the other> from the ledger instead` — and once S4
> exists the retained report is tried first. *(c)* **A `next:` pointer carries the run the alias
> resolved to**, never the alias: `last-failed` is a different run the moment another failure is
> recorded, and page two must not be of it (seen wrong in the CLI before it was fixed). *(d)* `--sid`
> takes the bare id and the `sid:` form alike — the form is what every listing prints. *(e)* No ledger
> at all, or an empty one, is the usage error; `history` is still the only verb that answers without a
> report. Through the real CLI, on the real CI ledger: `--run last-failed` found `gh:34945338589:1` in
> the **middle** of `xunit-in-memory`'s 23 runs and read it against the 13 before it — `1 broke …
> PPPPPPPPPF` — in 0.39 s wall, no report anywhere.

**F4 — cut at the run asked about.** In `AnalyseStream`, when `current.Id` is found among the
**suite's** runs, `prior` is the runs of the stream being analysed that come *before it in append
order*; when it is not found (a report not yet recorded), today's behaviour stands. Append order, not
`At`: CI clocks skew and the ledger's order is the one `history verify` already vouches for.

> **Corrected 2026-09-19 (RUN, §11.5 row 3).** This paragraph first said "found among the *stream's*
> runs". `Analyse` calls `AnalyseStream` a second time for `--compare-branch`, and that stream never
> holds the run being read, so under the first wording the comparison went on counting runs recorded
> after it. Built as first worded: the single-stream test goes green and
> `A_run_read_from_the_middle_of_the_ledger_is_compared_with_the_other_stream_as_it_stood_then` stays
> red (`RunsRecorded` 3, expected 2). Looked for among the suite's runs, one position cuts every
> stream, and both are green. The rule was then held against real data: every one of the 394 runs of
> BreakfastProvider's CI ledger, read **in place** against the whole ledger, gives the same
> `RunsRecorded`, the same absent list and the same verdicts and evidence for every scenario as the
> same run read against a ledger truncated by hand after it — **0 disagreements**, and the 712
> `behaviour-changed` verdicts of §3.2's first replay exactly. The unit suite is 5,160 / 0 failed
> with the cut in: nothing depended on the old reading.

**F14 — the run has to be findable.** The cut above, and S4's `--run`, both start from "the run this
report describes is *that* ledger line". With the fragment beside the report it is: the fragment
carries the id the run recorded itself under. Without it `ReportHistory.TryBuild` mints one
(`QueryCommand.History.cs:350`), and for a local run the salt is the minting process's own directory,
so the tool can never mint the id the test process did. Today that is a wrong answer, not a missing
one (F14's `PPPFF`). The fix, in S0: when the run was rebuilt from the report, look in the ledger for
its own line — same suite, same scenario ids in the same order, same results, and for a `local:` id
the same second whatever the salt — and read *that* as the current run. It is the better reading
anyway: the ledger's line carries the fingerprints a rebuilt run lacks, so the "behaviour verdicts are
off" note goes away whenever the run was recorded. Once S4 exists `Run.json` carries the true id and
the match is by id. **The reader's window applies here too (F5):** a fragment-less report older than
the last 50 runs of its suite is not found by a windowed read; the tool re-reads with window 0 when
the report's end time is older than the oldest run it was given.

> **The prototype of this fix was itself wrong, and a test caught it (RUN, §11.6 row 7).** `OwnLine`
> first adopted the ledger's line on an **id match alone**. On CI the ledger's line for an id is the
> *fold of every shard*, so one shard's report, read without its fragment, was handed the folded run's
> results and read each of its positions out of somebody else's: a **passing** `Pay by card` came back
> `broke … PPF`, carrying another shard's failure. On `main` the same reading is right (the rebuilt run
> keeps the id, and the analyzer leaves its own id out of "earlier"). The rule is now *same scenarios
> in the same order*, for an id match as for the same-second match; a line that fails it leaves the
> rebuilt run in place, whose id still tells F4's cut where it stands. This is §11.0's error class
> inside the fix for it — "the ids are equal" was checked, "so the line is this report's" was not.

**Measured, ledger only.** The F4 experiment and the replay in §5.2 *are* this mode: a ledger parsed,
a run and its roster taken from it, `Analyse` called, no report anywhere — 394 times. Passing the
ledger's `shapes` line changes no verdict (712 `behaviour-changed` with and without); what it adds is
the *names* of the new and gone calls (39 scenarios). So the mode loses nothing a report-anchored
reading has, provided it passes the shapes. Reading every run is not a cost at this size:
`Read(path, 0)` 139 ms against 213 ms for the windowed read of the same 394-run file (fastest of
three; the file is under the window in every suite, so the two do the same work).

**F5 — the window.** `--run` reads the ledger with window 0 (every run), then the analyzer applies
`options.Window` behind the chosen run. Add `--window N` to `query history` (the report option
`HistoryWindow` has had no CLI twin; `gate` and `show` already take it). The reader's
`LinesParsed ≤ window × suites + …` perf pin is untouched — it pins `Read(path, 50)`.

> **F5 has a twin that is live on `main` (RUN, §11.6 row 6).** The reader keeps the last *N* runs of a
> suite, and once a run has been recorded **it is one of them**. Read back by `query history`, a
> recorded run is therefore analysed against *N − 1* earlier runs where the run itself — analysed
> before it appended its line — saw *N*: the red test reads `runsRecorded` **49 for 50**, and
> `--window 2` gave one earlier run. At the window's edge the report's embedded verdicts and the
> tool's can differ by a run. The tool now asks the reader for `window + 1`; the analyzer's own cut
> trims the extra when the run is not in the ledger. The per-suite-not-per-stream half of F5 is
> untouched by this and still needs the reader to bucket by stream.

**JSON.** The envelope's `report` is `null` in ledger-only mode (the error envelope already allows
it), rows carry `address: "sid:…"`, and the `history` member gains `source: "ledger" | "report"`.

### 3.3 Registration checklist (the machinery that refuses half-added flags)

`--run ID`, `--sid ID`, `--window N`, each: parse case + property in `QueryOptions.cs`
(`:168-332`), `KnownFlags` (`:135-143`), `VerbTable.Flags`, the `history` row's `Flags` and `Usage`
(`VerbTable.cs:252-262`), `RerunArgs()` (`:378-433` — all three change the corpus a `next:` resumes),
`templates/skills/kronikol-test-debugging/references/commands.md` flag table and `### history`,
mirrored to `.claude/skills/…` in the same commit. Pinned by `DescribeTests` `:85-116` and
`SkillDriftTests` `:138-218`, `:394-418`. In S4 `--run` moves from the `history` row to
`UniversalFlags`.

> **Run, and three guards this list did not name (§11.6 row 8).** Adding the three flags with no docs
> turned exactly the predicted test red (`Every_flag_the_parser_accepts_is_documented_in_the_reference`:
> "--run, --sid, --window"). Also red, and not on the list: **(1)** every `! …` banner the tool can
> print must be explained in `SKILL.md` (`Every_banner_the_tool_can_print_is_explained_in_the_skill`,
> which extracts them from the source by regex) — the new "reading … from the ledger instead" banner
> needed its rule in both skill copies; **(2)** the skill is an **embedded resource** of the tool, so
> `InitAgentsCommandTests` (2 tests) stay red after the templates are edited until the tool is
> *rebuilt* — a docs-only edit is not docs-only; **(3)** in S4, `DescribeTests.cs:147` pins the
> universal flags as an exact list, so moving `--run` there is a deliberate edit of that pin. All
> green in the prototype, both skill copies byte-identical.

### 3.4 The exit code (F1)

No code change. Add `A_missing_report_is_a_usage_error_with_exit_2` to `QueryCommandTests` (nothing
pins it today — the only test mentioning the string asserts its *absence*; the nearest pin is
`:1481`, an unknown verb), and reply on #81 with the `PIPESTATUS` transcript from §1. `dnx` was the
other way a 0 could have happened; it forwards the 2 (§1), so the docs line recommending it stands.
Once S3 lands the message itself changes for `history` — no report *and* no ledger is the usage
error — and the test pins the new wording for `history` and today's for every other verb.

### 3.5 Tests (red first)

*Tests 1–9 built 2026-09-19 as `QueryHistoryLedgerOnlyTests` (15 tests; 14 red on `main`, the fifteenth
is §3.4's pin, green as predicted), with four the list lacked: `previous`; `--window`; a report plus
`--run`; and the resume pointer naming the resolved run. 10–12 were already in the patch; 13–14 are new.*

1. No report, ledger named: the newest run's verdicts are listed with `sid:` addresses; exit 0.
2. `--run <older id>`: a run recorded *after* it does not appear in `Points`, and a scenario that
   failed only in the later run reads `stable` — **the F4 test; fails on main's analyzer.**
3. `--run last-failed` picks the newest run holding an `F`; on an all-green stream it is exit 2 saying so.
4. `--run` substring: unique → resolves; two matches → exit 2 listing both.
5. A run 60 lines back is found by `--run` (F5) and absent from a plain read.
6. `--sid` selects one scenario with the same block a report-anchored `history s8` prints, error whole (S1).
7. Two suites, no `--suite` → exit 2 naming both. `KRONIKOL_HISTORY=off` with no `--history` → today's refusal.
8. `--json`: `report: null`, `source: "ledger"`.
9. Any other verb with no report still exits 2 with today's message.
10. **The comparison stream is cut at the same place** — append order `main, main, feature (the run
    read), main (fails)`: `Compare.RunsRecorded` is 2 and the failing run is not among its points.
    Red on `main`, and red under this plan's first wording of F4. *(In the prototype patch.)*
11. A run the ledger does not hold is read against every run, as today. *(In the patch; green on `main`.)*
12. **F14, in S0:** a local run recorded under the id its own process minted, its report read with
    no fragment beside it → `broke`, series `PPPF`, three earlier runs; on `main` it is `failing`,
    `PPPFF`, "failing since" its own id, four runs. *(In the patch.)*
13. **F14's other side:** one shard's report, read without its fragment, against a ledger whose line
    for that id is the fold of several shards → the shard's own results, no `broke` borrowed from
    another shard. Green on `main`, **red under F14's first prototype**, green now. *(In the patch.)*
14. **F5's twin:** fifty runs and the run itself recorded → `runsRecorded` 50; on `main` 49. *(In the patch.)*

---

## 4. S1 — the error, whole (#82)

### 4.1 What is wrong

`WriteScenario` prints `QueryWriter.OneLine(e, 80)` per run row. The view is at most 15 rows of a
string the ledger has already capped at 200, so the cut saves ≤ 1.8 KB and, in the issue, removed the
seven characters that identified a Go JSON parser — the emulator, not the service. It is not one
suite's bad luck: in BreakfastProvider's CI ledger 5 of the 8 distinct error texts are longer than 80,
and every one of them is an assertion whose *actual value* sits past the cut (§1). Eighty characters
is, in practice, "expected X" without "but found Y". None of the eight reaches the ledger's own cap,
so printing what is stored fixes every measured case. The agent guidance
(`SKILL.md:126`, `agent-instructions.md:92`) correctly steers readers away from `--json`, which was
the only rendering holding the rest.

### 4.2 The change

- **The single-scenario view never cuts.** The error moves to a continuation line under its row:
  ```
    F  local:20260918T101611Z:ab12cd34  2026-09-18T10:16:11Z  82215 ms
       The service bigquery has thrown an exception. HttpStatusCode is BadRequest. invalid character '"' looking for beginning of value
  ```
  Same for `evidence:` (cut at 400 today) and the `on <stream>:` comparison line (200).
- **When the stored text is itself the cut** (it ends in the ledger's ellipsis, i.e. the message was
  longer than a line of 199), the row says where the rest is: `… first line only — the whole message:
  kronikol query failures <dir> --run <id>` (the pointer is printed only once S4 can honour it; until
  then: `the whole message is in that run's Failures.md`).
- **A list view that cuts, says so.** `QueryWriter` gains an instance `Cut(text, max)` beside the
  static `OneLine`; it records that something was elided, and the footer appends
  `· … marks cut text — history s8 prints it whole`. Applied to the run view's evidence column
  (`:130`). This is the issue's fix 3, generalised to the one rule "an ellipsis always has an address".
- **Where a list must cut an exception string, keep both ends.** `Cut` keeps the head and the tail
  around ` … ` (60/40). `<Wrapper>. <Status>. <underlying message>` puts the signal last — issue fix 4.
  View-only; the *stored* key is not touched (§7).
- The guidance stays as it is — after this change no text-visible field is complete only in `--json`,
  and test 3 below keeps it that way.
- **Two more cuts of the same kind, found by reading the view against its `--json` row (2026-09-19).**
  The quarantine reason is cut at 160 (`:175`). And the analyzer itself writes an ellipsis with no
  address: evidence names three new and three gone calls and then says `and 2 more`
  (`HistoryAnalyzer.NamedCalls`, `:538-546`), while the whole lists exist only as `newCalls` /
  `goneCalls` in `--json` — the scenario view never prints them. The single-scenario view prints both
  lists whole, one call per line, under `calls:`; the run view keeps the three and the footer names
  `history sN`.

**What the cuts cost on real data (RUN, the §11.5 replay: 1,217 readings with a verdict other than
`stable`, BreakfastProvider's CI ledger).** Evidence is longer than the run view's 180 in **13** of
them (1%) and never reaches the scenario view's 400 — the longest is 261. No reading has more than
three new or three gone calls. So of S1's cuts only one bites on this ledger, and it is the one the
issue reported: the 80 on the error (5 of 8 distinct texts, §1). The others are fixed because the
rule — an ellipsis always has an address — is cheaper to keep whole than to keep measuring.

### 4.3 Tests (red first)

1. A 199-char error appears whole in `history s1`; **fails on main** (cut at 80).
2. A stored error ending in the ledger's ellipsis prints the "first line only" pointer; a short one does not.
3. *Parity of renderings:* every **free-text** member of the detailed `--json` row — `evidence`,
   `runs[].error`, `quarantine.reason`, each of `newCalls` and `goneCalls` — appears verbatim in the
   text of the same query. This is the test that stops the class of bug, not the instance. *Scoped
   2026-09-19 (READ of `ReportHistory.Row` against `WriteScenario`): as first written — "every string
   member" — it fails on four members that are abbreviated on purpose and lose nothing: `result`
   (`"Passed"`; the text prints `P`), `commit` (40 characters; the text prints 7), `at` (the JSON
   serialiser's `+00:00`; the text prints `Z`) and `series`, which the run rows spell out. A test
   that is red for a good reason on day one gets an ignore list, and an ignore list is how the class
   of bug comes back.*
4. The run view's footer carries the cut notice only when something was cut.
5. `Cut` keeps head and tail, never splits a surrogate pair (the `FailureText.Truncate` rule), and is
   the identity under the limit.

---

## 5. S2 — an empty answer that is not a dead end (#84)

### 5.1 What is wrong

`no scenario with a verdict (failing)` is a statement about *the run the report describes*, written as
if about the scenario. The header above it does say `· partial`, in the same weight as the stream
name. Both are true and the pair is unreadable as anything but "nothing failed".

### 5.2 The change

**Say which run, and how much of the suite it was.** `HistoryVerdicts` gains `PreviousFullCount` (the
roster size of `LastFull`, already computed at `HistoryAnalyzer.cs:82`). The header becomes:

```
ledger: …/history.jsonl · stream local · run local:20260918T102009Z:ab12cd34
this run is partial: 34 scenarios, the last full run had 396 — a filter answers for these 34 only
```

*(Worded "34 of the 396" in the first draft; a filtered run can hold a scenario the last full run did
not, so the two counts are stated side by side and neither as a share of the other. Built, §11.6.)*

**Reword the empty note per filter** — `no scenario is failing in this run`, `…broke in this run`,
`…is flaky as of this run`, `…is new in this run`, `…changed in this run`. (`QueryHistoryTests.cs:133`
pins the old string; it moves.)

> **The first of those can be false (RUN, §11.6 row 2).** `failing` is a *verdict* — failing now **and**
> in the previous run — so a scenario that **broke** in this run fails in it and is not in the
> `--failing` answer. "no scenario is failing in this run" would then be printed over a run with a
> failure in it: #84's misreading, reintroduced by its own fix. It is not a corner: in
> BreakfastProvider's CI ledger **every one of the 3 failing runs is such a run** (each failure is a
> `broke`). So the note depends on the data. With no failure in the run it is the strong sentence
> above; with one it reads `no scenario has been failing since an earlier run (what fails in this one
> broke in it)`, and a hint line names them with the verdict they do have — `1 scenario failed in this
> run under another verdict: s3 (broke) · next: history s3`. `--regressed` is the mirror image.

**Then point at what the window holds.** From the `ScenarioHistory` rows already in hand:

| Filter | Near-miss | Line |
|---|---|---|
| `--failing`, `--regressed` | `LastFailedRunsAgo is not null` | `1 scenario here failed earlier in the window: s8 (2 runs ago, local:…T101611Z) · next: history s8` |
| `--flaky` | `Flips > 0` and not flaky | `1 scenario has flips but is not flaky: s8 — 2 flips, one failing episode (broke once, recovered); flaky needs two · next: history s8` |

**F2 — the reason must be the real one.** The analyzer records why flakiness was not given:
`ScenarioHistory.FlakyShortfall` ∈ `TooFewVerdicts` (`real.Count < MinRuns` — *this* is the case that
names `--min-runs`, with both numbers), `OneEpisode`, `BelowRate`. The hint prints from it. The
single-scenario stats line gains `· failing episodes 1` next to `flips 2`, because `flips 2 · flip
rate 0.50` with no episode count is what made a one-off read as flaky (the noise plan's F5 reaches the
same line from #83; whichever lands second keeps the other's addition).

> **One reason is F2 again, smaller (RUN, §11.6 row 1).** As first written the shortfall was a single
> value and §5.3's test 3 said "`P F P` with `--min-runs 5`: the reason *is* the bar". It is not: that
> scenario is under the bar **and** one episode, and at `--min-runs 3` it is still not flaky — the test
> asserted the very sentence this finding exists to stop. `FlakyShortfall` is a `[Flags]` set of
> **every** condition missed; the view leads with the episode whenever it applies and then states the
> bar *without its flag* (`one failing episode and 4 of the 5 verdicts`), and names `--min-runs` only
> when the bar is the sole obstacle (`F P F P` → `4 of the 5 verdicts flaky needs (--min-runs)`, and
> `--min-runs 4` does make it flaky). On the CI ledger the 174 near-miss readings split **139
> `OneEpisode`, 35 `OneEpisode | BelowRate`, 0 where the bar alone stood in the way** — a single-valued
> reason would have hidden the 35.

**Outside the roster (needs S3).** On a partial run the scenarios that failed may not be in this
report at all. ~~One pass over the window's runs counts `F` positions whose id is not in the current
roster: `also: 3 scenarios outside this partial run failed in the window · next: history --run
last-failed --failing`.~~ Until S3 lands the line ends at the count.

> **Both halves of that were wrong (RUN, §11.6 row 3).** *The rule:* "any `F` in the window" names a
> scenario that failed thirty runs ago and has passed ever since. What a filtered green re-run hides is
> narrower — a scenario outside it whose **latest verdict in the window is a failure** (failed, and not
> run again). `HistoryVerdicts.FailingOutside`, computed on a partial run only; on a full run the same
> scenarios are `absent` and said so. *The pointer:* `--run last-failed --failing` is a second dead
> end. The scenario **broke** in that run, so `--failing` is empty there too (the test follows the
> pointer and checks), and `last-failed` is the newest run with *any* failure, which a newer filtered
> run can be. The line names the run and drops the filter: `also: 1 scenario outside this partial run
> was failing when it last ran (gh:3:1) · next: history --run gh:3:1`.

**How often, and how loud — measured.** Replaying BreakfastProvider's CI ledger run by run, each run
read against only what preceded it (394 runs, 18 suites, 3 of them with a failure):

- the `--failing` hint would fire on **21 of 391 green runs (5%)**, naming up to **11** scenarios;
- the `--flaky` near-miss line on 24 of 394 runs, up to 11 scenarios;
- **the reason was `OneEpisode` for all 174 near-misses** — never the `--min-runs` bar, never the
  rate. F2 is not one reporter's corner case: on this ledger the explanation #84 proposes would have
  been wrong every single time it was printed.

Two consequences. Eleven addresses is a list, not a hint, so the cap stays. And one failure echoes for
as long as it stays in the window — three failing runs produced 21 hint-bearing runs — so the hint
names only failures **at most 5 runs back**, newest first, and counts the rest
(`… and 2 older, up to 19 runs ago`). "A failure you saw minutes ago" is the case; ~~"something failed
last month" is what the unfiltered view is for.~~

> **The unfiltered view does not have it (RUN, §11.6 row 4).** The run view lists *verdicts*, and a
> scenario that failed eight runs ago and has passed since is `stable`. Asked of the newest
> `xunit-in-memory` run in the CI ledger — one such scenario in its window — the unfiltered view
> printed `no scenario in this run has a verdict other than stable`. A hint that only *counted* the
> older failures would have been this plan's own dead end. So a counted failure still carries one
> address: `… · and 1 older, the newest 6 runs ago in gh:1:1`, and with nothing recent, `1 scenario
> here failed more than 5 runs ago, the newest s3 (7 runs ago, gh:1:1) · next: history s3`. With the
> 5-run rule built, the replay reads: the hint **names** something on **15 of 391 green runs**, up to 11
> scenarios; 6 more runs have only older failures (15 + 6 = the 21 above); the `--flaky` near-miss line
> on 24 runs, 18 of them with a failure in the last five.

At most five addresses per hint, then `… and N more`. Exit code stays 0 — the question was answered.
`--json` gains `history.nearMisses: [{address, stableId, kind, reason, runId, runsAgo}]` (`kind` ∈
`failed-earlier`, `failed-now`, `flips-not-flaky`; `reason` is the shortfall set, `one-episode+below-rate`,
or for `failed-now` the verdict it has), `history.previousFullCount`, `history.failingOutside`, and each
row gains `failingEpisodes`. "N runs ago" is the distance in the *stream's* runs, read off
`HistoryVerdicts.Runs` — `LastFailedRunsAgo` counts the scenario's own verdicts, which on a stream of
filtered re-runs is a smaller number than the reader means.

### 5.3 Tests (red first)

*Built 2026-09-19 (`QueryHistoryHintsTests`, 14 tests, every one red on `main` first; in the prototype
patch). Tests 3, 6 and 8 changed by being built — §11.6.*

1. The issue's ledger (`P P F P P`, current green and partial): `--failing` names s8 and `history s8`.
2. Same ledger, `--flaky`: the reason is *one failing episode*, and the text does **not** contain `--min-runs`.
3. ~~`P F P` with `--min-runs 5`: the reason *is* the bar, printed as `3 of 5 verdicts`.~~ **Two tests:**
   `F P F P` (two episodes, four verdicts) → the bar, with both numbers and its flag, and `--min-runs 4`
   makes it flaky; `P F P P` → *one failing episode and 4 of the 5 verdicts*, no flag, and at
   `--min-runs 3` it is still not flaky.
4. A full green run with a clean window prints the reworded note and no hint line.
5. Partial header appears only on a partial run and carries both counts.
6. (after S3) a failure outside a partial run's roster produces the `also:` line and its `next:` —
   **and the test follows the pointer**: `history --run <that run>` lists the scenario as `broke`, and
   the first draft's `--run last-failed --failing` does not list it at all.
7. `--count` still prints one bare number — hints go to stderr on that path, the rule `:59-61` already applies.
8. A failure 6 runs back is counted (`… and 1 older, the newest 6 runs ago in <run>`), not named; one 5
   runs back is named. Seven near-misses print five addresses and `… and 2 more`. **A failure too old
   to list still comes with an address**, and the unfiltered view is checked *not* to contain it.
8a. A run in which something **broke** is never told "no scenario is failing in this run".
8b. `--json`: `nearMisses` (kind, reason), `previousFullCount`, `failingOutside`; the scenario view
    prints `failing episodes` beside the flips.
9. *Replay guard:* the §5.2 replay, committed as a test over a trimmed copy of the CI ledger, asserts
   that no near-miss reason is ever printed that the analyzer's own `FlakyShortfall` does not give —
   the F2 mistake, made impossible rather than corrected.

---

## 6. S4 — the last N runs are kept (#80)

### 6.1 The shape, and why rotate rather than redirect

`Reports/` keeps holding the newest run, byte-for-byte as today. Before a run writes, the **previous**
run's files are moved to `Reports/runs/<run>/`. A move inside one volume is a rename: constant time
for an 83 MB report, no second copy on disk, no double write.

The alternative — write each run into `runs/<id>/` and publish a copy as latest — doubles the IO of
the largest file Kronikol writes, and cannot be done by scoping `ActiveReportsDirectory` anyway: two
generators bypass it (§1). Rotation never touches how the new run is written, so every existing pin on
the top-level file set (`ReportGeneratorAgentOutputsTests`, `StaleOutputTests`,
`MergeWritesTheRunOutputsTests`' exact listing) holds unchanged.

> **Half wrong (READ, 2026-09-19).** True of the *rotation*; false of the *manifest* this section
> goes on to add, which is a new top-level file. `No_json_writes_the_html_alone`
> (`MergeWritesTheRunOutputsTests.cs:125`) asserts the merge output directory is exactly
> `["Combined.html"]`. It survives for a reason the first draft also had wrong: **`kronikol merge`
> never reaches `CreateStandardReportsWithDiagramsCore`** — it has its own writer
> (`MergeCommand.WriteMergedData`, `MergedRunOutputs`); the callers of the core method are the
> framework adapters, LightBDD's formatter and `IngestPipeline` (`:399`). So the decision has to be
> made rather than inherited, and it is: **`merge` neither rotates nor writes a `Run.json`.** Its
> output is derived — the shards are the evidence, and they are what S4 protects where they were
> written. The pin stays true, and §6.2's re-entrancy rule has two callers to guard, not three.

### 6.2 The change

**Where.** One step in `CreateStandardReportsWithDiagramsCore`, after the zero-scenario guard
(`:168-181` — a discovery pass must never rotate a real run away) and after `HistoryRunContext.Create`
(`:323`), before `Directory.CreateDirectory` and the attachment copies (`:343`).

**A run says what it wrote: `Run.json`.** Three questions the rotation must answer — *which files are
the previous run's, what was it called, did it fail* — have no cheap honest answer today. The file
names depend on four options; the id is absent when history is off and not unique on CI (F9); the
attachments a run owns are named only inside its 8–80 MB report; and the digest's size says nothing
(F10). So each run writes one small manifest, last.

**Where the file list comes from — not from `RunOutputs`.** This plan's first draft said "from the
`written` list"; that set holds output *labels*, and two of them are not file names
(`"TestRunReport schema"` at `:391`; `"ComponentDiagram.html"` at `:396`, which also writes a `.png` or
`.svg`), while `CiSummary.md` (`:510`), `DiagnosticReport.html` (`:474`) and the attachment copies
(`:345`) never pass through it at all (§11.0). The list is collected where bytes reach disk: a per-run
collector, scoped like `ActiveReportsDirectory` (an `AsyncLocal` that already flows into the
`Parallel.Invoke` workers), that `ReportGenerator.WriteFile` (`:5403`) adds to on success — including
the `<name>2.<ext>` salvage name when that is what was written — plus the four writers that bypass
`WriteFile`: the component diagram (`ComponentDiagramReportGenerator.cs:61`, `:93`), the diagnostic
report (`DiagnosticReportGenerator.cs:21`), the inline `CiSummary.md` write, and
`CopyAttachmentsToReportsFolder`'s destination names. A manifest of what was *planned* would repeat
the mistake `StaleOutputTests` exists to catch.

```json
{"runManifestVersion":1,"run":"local:20260918T101611Z:ab12cd34","at":"2026-09-18T10:16:11Z",
 "suite":"…","scenarios":396,"failed":1,"partial":null,"kronikolVersion":"3.x",
 "files":["TestRunReport.html","TestRunReport.json","Failures.md","Failures.jsonl","History.run.json","…"],
 "attachments":["attachments/checkout_failure.png"]}
```

`run` is `history.Run.Id` when history is on and `HistoryRunBuilder.RunId(ci, end)` — the public
minting function — when it is off, so a run always has a name. Written last, so **a directory with no
`Run.json` is a run that did not finish** — the rule the digest states today, now in a file whose
presence means "every output is here".

**What moves.** Exactly the manifest's `files` and `attachments` that still exist, plus `Run.json`
itself. Nothing else: a host that renders several differently-named reports into one folder (the case
`IngestPipeline.CleanAttachments`' comment protects) keeps the others, and an attachment two reports
share is *copied* when another manifest-less report is present and *moved* otherwise. **Never moved:**
`CLAUDE.md`, `AGENTS.md` (F8 — the top-level pair is spliced in place as today; they are not in
`files`). A directory written before S4 has no manifest: the first rotation after upgrading falls back
to "files this run is about to write", leaves `attachments/` alone, and names the run from
`History.run.json` or the report's timestamp. That path is exercised once per directory, ever.

**Staged, then published — because a reader can block it.** `kronikol query` opens the report with
`File.OpenRead` (`ReportScanner.cs:73`, `PayloadReader.cs:50`): read-sharing only. Run on NTFS (§11.1b):
while such a handle is open, `File.Move` of the report **fails** — and so does today's in-place
overwrite, which is the state `StaleOutputTests` and the `<name>2` salvage were written for. Without a
holder a 100 MB move takes 1 ms and a directory rename 1 ms. So:

1. Files move into `runs/.incoming-<name>/`, **`TestRunReport.json` first** — the file most likely to
   be held. If that first move fails, nothing has moved: the rotation is abandoned whole and the run
   carries on exactly as it does today.
2. A later failure moves the staged files back, best effort, and records what it could not restore.
3. One `Directory.Move` publishes `.incoming-<name>` as `<name>`. A retained run is therefore never
   half-present: `--run` and pruning ignore `.incoming-*`, and `kronikol history doctor` names one
   left behind by a killed process.
4. **The tool stops being the thing that blocks a run**: both opens become
   `FileShare.ReadWrite | FileShare.Delete`. With that sharing the move succeeds under an open reader
   (run, same harness). This is a patch-level fix to a collision that exists today — a query in flight
   while the suite finishes already costs the run its `TestRunReport.json` — and it ships with S0,
   **on one condition (§11.3, investigation 1):** the index is scanned once and payloads are fetched
   later by byte offset through a *fresh* open of the same path, so a report replaced in between
   would be read at the old offsets. Today the failed overwrite prevents that, at the run's expense.
   The sharing change goes in only with a check that a payload read fails loudly on a changed file
   (the index already holds each payload's hash and length).

   > **The scan needs the check as well (RUN on NTFS and on ext4, §11.5 row 5).** With the new sharing,
   > today's in-place overwrite *succeeds* under the open reader, and **the same open handle then reads
   > the new bytes**: written `{"first"…}`, opened, overwritten, re-read from 0 → `{"second"…}`. The
   > index scan is one streaming pass over one handle (`ReportScanner.cs:71-73`), so a report
   > overwritten mid-scan is indexed half from the old file and half from the new. On Linux that is
   > true **today** — nothing there ever blocked the overwrite (same run, ext4: `File.OpenRead` holder,
   > `File.WriteAllText` succeeds). So S0's check is three places, not one: at the end of the scan, the
   > handle's length and last-write time are what they were when it was opened; at each payload open,
   > the path's are what the index recorded; and an `IOException` out of a verb is exit 1. All three
   > say the same thing — *the report changed while it was being read; run the command again*.
   >
   > **And rotation removes the hazard it was feared to add.** When the previous report is *moved*,
   > the new one is a new file: a reader holding the old one keeps reading the old one, whole (same
   > run: after the move the open handle still read the moved file, and a fresh open of the path read
   > the new report). The torn read needs an overwrite **in place**, which is what happens today and
   > what S4 stops doing whenever it rotates.

**Re-entrancy, and F9.** Not "same id as the current run" — on CI a genuine re-run has the same id. A
process rotates a given directory **at most once** (a static set of full paths): LightBDD's formatter
and `kronikol ingest` each reach this method (`merge` does not — §6.1's correction), and a second call
in one process is the same run writing again. A new process is a new run, whatever its id.

**One manifest per directory, like the digest.** `HtmlTestRunReportFileName` lets a host write
`Checkout.*` and `Payments.*` into one folder, but `Failures.md`, `History.run.json` and
`ctrf-report.json` already have one name each, so the second report overwrites the first's today, and
`kronikol query <dir>` refuses two reports as ambiguous. `Run.json` follows them: the last writer's
manifest is the directory's, only the files *it* lists rotate, and a second report in the same folder
is left exactly as it is now — unretained, untouched (READ).

**F7 — the directory name.** `HistoryRunId.DirectoryName(id)`: every character outside
`[A-Za-z0-9._-]` becomes `_`, on every OS (`local_20260918T101611Z_ab12cd34`, `gh_18273645_1`), with
`-2`, `-3` appended while the name is taken (F9: two steps of one workflow run). The true id is in
`Run.json`, which is what `--run` reads — sanitised names are for people and for shells, never parsed
back. `--run gh:18273645:1` matching two retained runs refuses with both, by time and `failed` count.

**Pruning.** After rotating, keep the newest `KeepRuns` directories, **except that the newest retained
run with `failed > 0` is never pruned** — so the directory holds at most `KeepRuns + 1` runs and the
last failure survives any number of green re-runs, which is the issue's whole complaint. Read from
each `Run.json` (a few hundred bytes), never from a report or a digest. A directory under `runs/`
with no `Run.json` is an interrupted rotation: never pruned automatically, named by
`kronikol history doctor`.

**It never fails the run** (`CROSS_RUN_HISTORY_PLAN.md` §6.2: a test run must never fail over its history).
A locked file → the rotation stops, a `ReportRotationFailed` diagnostic names the file, and the run
overwrites as it does today. Partial rotations are left as they are and said so.

**It says what it did.** One console line beside the existing pointer, only when the rotated run had
failures: `previous run (1 failed) kept: Reports/runs/local_20260918T101611Z_ab12cd34 — kronikol query
failures <dir> --run last-failed`. The issue's "nothing warned me", answered at the moment of overwrite.

**Reading one.** `--run` becomes a universal flag: given a reports directory, any verb opens
`runs/<run>/TestRunReport.json` instead — `index.Directory` follows, so `History.run.json`,
attachments and the `#sid-` deep link resolve beside it (§1). Not retained → exit 2:
`run … is not retained under … (retained: …) — the ledger still has its results: history --run …`.
`history --run` prefers the retained report and falls back to the ledger (S3). This is the point of
the plan: **the same `--run last-failed` works whether the report survived or only the ledger did.**

> **Built, tool side (§11.6 row 11; `RetainedRunsTests`, 6 tests, all red on `main`).**
> `RetainedRunResolver` reads nothing but `Run.json`s. A run answers to its manifest's id, to a unique
> part of it, to `last-failed` / `previous`, **and to its folder name** — F9 makes that necessary, not
> a nicety: two steps of one workflow run are `gh_1_1` and `gh_1_1-2` with one id, `--run gh:1:1`
> refuses with both (time, scenarios, failed), and the folder is the only name left that picks one.
> The run on top answers to its own id; `.incoming-*` is never offered. **The §11.2 READ row is RUN:**
> opened with `--run`, the retained run's `history` reading finds the fragment beside it (no "no
> History.run.json" banner, behaviour verdicts on), `failures` prints its attachment as
> `…/runs/gh_7_1/attachments/checkout.png` and the file is there, and the `#sid-` deep link is built
> against the retained `TestRunReport.html`, not the newest run's.
>
> **`history doctor` was the wrong home as written (READ, then built).** `doctor` is a *ledger*
> command: it takes `--history` and no reports directory, so it could never have named a staging
> folder. It now takes reports directories as positionals — `kronikol history doctor <reports-dir>` —
> and says how many runs are kept, which is the newest failing one, and what an interrupted rotation
> left; exit 1 when it left anything.

**The agent file.** `agent-instructions.md` gains a short "Earlier runs" section — this directory
holds the newest run; if the failure you were sent for is not in `Failures.md`, `history --failing`
names the run that has it and `--run` opens it — and "`# No failures` means nothing failed" becomes
"…nothing failed *in the newest run*" in all eight places that say it (list in §8).

### 6.3 The sweeps (F6)

One helper, `ReportFolders.IsReserved(root, file)` — `baseline/` or `runs/` as a path segment —
replaces both `IsUnderBaselineFolder` copies and is added to `HistoryCommand.FragmentFiles` — there
with one exception (F13, §11.4): a retained fragment whose run id equals the top-level fragment's is
an earlier attempt of the same run and **is** recorded, ~~folding as a shard~~ **overlaid as an
attempt (F15)**. A retained run named explicitly (`kronikol merge Reports/runs/<run>`) is still read:
the exemption is for what a sweep *finds*, not for what it is *given*.

**F15 — an attempt is not a shard (RUN, prototyped, §11.5 row 1).** `HistoryFold` concatenates: each
fragment's scenarios are appended, and an id seen before gets the next slot
(`Fold_renumbers_slots_when_two_shards_carry_the_same_stableId` pins it, rightly — that is what makes
the same suite on two matrix legs two histories instead of one corrupted one). Fed a retry, that gives
the retried scenario a second holder. Measured on the two fragments the retry extension really wrote,
in front of seven green runs and followed by two:

| The CI ledger holds | The retried run reads | The next run reads |
|---|---|---|
| both attempts **folded as shards** (F13 as first written) | `broke` on slot 0, and a **`new`** scenario of the same name on slot 1 — 14 scenarios in a suite of 13 | `fixed`, and that scenario **ABSENT — "in gh:777:1 and not in this run"** |
| **one position, the later attempt's result, `attempts` = 2** | **`flaky` — "failed 0 of the last 8 runs with a verdict, 0 flips, passed on retry 2 in this run"** | nothing |
| what CI stores today: the retry's fragment alone | nothing — one passing scenario, `partial` | nothing |

The second row is not a new idea; it is the history plan's §5.8 ("a test that passes on retry is flaky
now"), written into the format as `attempts` from v1, read by the analyzer since it shipped, printed
by the scenario view as `attempt 2`, exported by CTRF as `retries` — and filled by nothing but Cucumber
ingest, because no native lane can see a retry that happens in another process. S4 can.

**The rule.** A retry cannot be told from a shard by overlap — two matrix legs overlap completely —
so it is told by **where the fragments are**: `record` groups what it finds by reports directory.
Inside one directory, the fragment on top and the retained fragments *with its run id* are attempts of
one another: `HistoryFold.Attempts` overlays them oldest first, a scenario run again keeping its one
position, taking the later attempt's result, duration and calls, and counting (`-` and `-` make `2`). A
scenario only one of them ran is kept as it is, which is also the right answer for #80's "second
workflow step that runs a subset". Across directories the results are shards and fold exactly as today.
Order is the fragments' `at`, **never the path**: `Reports/History.run.json` sorts before
`Reports/runs/…`, so path order — which `FragmentFiles` uses, for good reason, between shards — would
make the first attempt the verdict. No ledger-format change, no new line kind, nothing for Kronikol4J's
reader to learn.

Runner-specific signals exist and are not used: the MTP extension hands its child
`--internal-retry-pipename` and has a `RetryAttemptNumber` (strings of
`Microsoft.Testing.Extensions.Retry` 2.4.1). Co-location covers that runner, every other, and the
second CI step, with one rule.

**One decision in it, flagged for review — now with a recommendation: keep it, labelled (§11.6 row
10).** A scenario that passed on retry keeps the *failure's* error text on its now-passing position,
so the text survives even when `KeepRuns` does not keep the report — and the ledger, which lives in
git, is the only store that outlasts a CI artifact's retention. What was READ is now RUN: `history
verify` calls a ledger holding such a line `sound`, the line round-trips through the writer and the
reader with the text intact, and nothing clusters on it. **What running it showed is that keeping it
is not free as first built:** every surface prints a point's error whatever its result, so the row
read `P  gh:3:1 … attempt 2  Expected 200 but got 500` — a pass that reads as a failure — and the HTML
history tooltip read `passed · 120 ms · Expected 200…` with no mention of an attempt at all. Both now
say whose error it is: `attempt 2  an earlier attempt failed: …` in `history sN`, `passed on attempt 2
· an earlier attempt failed: …` in the tooltip (two lines of code, one red test). The alternative —
drop the text — is still one line in `HistoryFold.Attempts`, and the two renderers then never meet the
case. *The decision is the user's; the prototype is complete either way.*

**The direct-append case — covered (RUN on the real retry fragments, prototyped, §11.6 row 9).** It
is not the exotic corner the first draft took it for. A run appends to the ledger itself whenever
`WriteHistoryLedger ?? (the ledger was named by option or environment || not on CI)`
(`HistoryRunContext.cs:155`) — so **any CI job that sets `KRONIKOL_HISTORY=<file>` or `HistoryFilePath`
is a direct-append job**, persistent runner or not. Under a retry extension, today:

| | Today (RUN) | With `Amend` (RUN) |
|---|---|---|
| the retry's append | `Duplicate`, swallowed without a word (`HistoryRunContext.cs:194`) | `Amended` |
| the ledger's line for the run | `PPFPPPPPPPPPP`, attempts `-------------` — a **failure, for a job that went green** | `PPPPPPPPPPPPP`, `--2----------`, attempt 1's error text kept, still 8 runs not 9 |
| the run read back | `broke` | `flaky … passed on retry 2 in this run` |
| the next run | `fixed` — a break and a fix that never happened | `stable` |
| `history verify` | sound | sound |

`HistoryLedgerWriter.Amend` (and `HistoryAppendOutcome.Amended`): when the ledger already holds a
line for the run's suite and id whose `at` is **earlier** than this run's, the two are overlaid with
`HistoryFold.Attempts` — the same function `record` uses, so the two lanes cannot disagree — and the
line is replaced **in place, under the writer's lock, every other line keeping its bytes and its
order**. A line that is not older is the same attempt written twice and stays a `Duplicate`.
`HistoryRunContext.Append` calls it only for a duplicate **another process** wrote (a static set of
what this process appended): LightBDD's formatter and the adapter both reach the generator, and a run
overlaid on itself would count every scenario as retried. The pin `The_same_run_identity_is_not_appended_twice`
holds and now also asserts the first line stands untouched.

**This is a second decision for the user, because it bends a stated rule.** The writer's own comment
(`Compact`, and the history plan §3.5) says *nothing rewrites the ledger during a test run*: a silent
rewrite breaks the append-only shape `merge=union` and git's deltas rely on. `Amend` is a rewrite
during a test run. The case for it: it changes one line, the run's own, written seconds earlier by
the same job and therefore not yet pushed or merged anywhere; and the alternative that keeps the rule
— append a second line for the id and teach every reader to overlay it — is a format-semantics change
that old readers would see as two runs. **Known wrong answer it introduces:** two matrix legs with the
*same suite name* appending to *one shared file* — today the second leg is dropped as a `Duplicate`;
amended, the later leg's results win and count as a retry. That configuration already loses a leg
today; the docs' answer (a suite name per leg) is the fix for both.

**Still not done here:** the retry's *own report* knows nothing of attempt 1. Its analysis reads the
retried scenario `stable [PPPPPPPP]` — attempt 1's line has its id and is left out of "earlier" — and
its `Failures.md` says nothing failed. With S4 the retry has just rotated attempt 1's fragment, so it
can analyse the overlay and say `flaky — passed on retry 2` in its own digest; §11.4's "the newest run
says so" covers the sentence, not yet the verdict.

**F11 goes first, as its own patch, whatever happens to the rest of this plan.** In a *directory
sweep*, `kronikol merge` skips the files Kronikol itself writes beside a report that are not reports —
`History.run.json`, `ctrf-report.json`, and later `Run.json` — by name, and says how many it skipped.
A file *named explicitly* that is not a report stays the error it is today: the distinction is again
found versus given. Skipping "anything without `features`" is the tempting rule and the wrong one — a
truncated or foreign shard would vanish from a CI merge without a word. Test: the fixture plus a real
fragment (generated, not hand-written) merges with exit 0; the same fragment named on the command line
is exit 1. *Built (§11.6 row 11): the sweep skips `History.run.json`, `ctrf-report.json` and
`Run.json` by name, says `skipped 3 files Kronikol writes beside a report (…) — name one explicitly to
have it read`, and the same fragment named on the command line is still handed to the reader; all
three sweeps use `ReportFolders.IsReserved`, and `query summary <parent>` — "Several reports under …"
on `main`, the two retained runs among them — resolves to the one report.* `PublishCiArtifacts` (`ReportGenerator.cs:522`)
and `MergedRunOutputs.cs:155` are top-level only; with `KeepRuns > 0` on CI the artifact includes
`runs/`, otherwise the retried-job case in #80 §3 keeps the evidence on a runner nobody can reach.

### 6.4 The option, and the default

`ReportConfigurationOptions.KeepRuns` (`int?`), `KRONIKOL_KEEP_RUNS` (a number, or `off`), option
beats environment, read through the injected `getEnv` (the `KRONIKOL_HISTORY` idiom; only two runtime
variables exist today and both work this way).

> **Superseded in part by F13 (§11.4):** on CI the earlier attempts *of the same run id* are kept even
> at `KeepRuns = 0`. "A fresh CI checkout has nothing to rotate" is false under a retry extension.

**Default: 3 off CI, 0 on CI** — the `WriteHistoryLedger` precedent (`HistoryRunContext.cs:155`:
on unless CI). Reasons: locally is where the instinctive re-run happens; a fresh CI checkout has
nothing to rotate; and the one consumer pipeline checked does not glob for the report — it **uploads
the whole reports directory** as the artifact and copies it into a Pages site (BreakfastProvider
`_tests.yml:410-416`). A default-on `runs/` there would not break anything; it would quietly multiply
every artifact and publish old runs to a public site. That is a worse failure than a broken glob,
because nothing reports it.

Sizes, with the pin (≤ 4 retained runs): 50–90 MB of `bin/` per suite on BreakfastProvider (measured
12–22 MB a directory), ~330 MB for #85's 82.7 MB report, ~130 MB for #89's. The issue's 5 would be
~500 MB on the large one; 3 plus the failure pin protects the same evidence. (Plan 5's compression, if
taken, divides the large figures by six.) `dotnet clean` does not remove `Reports/`, so this is
space that stays until pruned — the reason there is a cap at all.

Kronikol's own test projects set `KRONIKOL_KEEP_RUNS=off` beside `KRONIKOL_HISTORY=off` — that is
line 12 of `test.runsettings` in exactly five projects (`Kronikol.Tests`, `.EndToEnd`,
`.LightBDD.TUnit`, `.LightBDD.xUnit3`, `.ProxyTap`). Several test classes generate into the shared
default `Reports/` in parallel, and rotation there would be a race this feature does not need to win.
A runsettings variable does not reach a test executable started directly, which is how the MTP
projects are often run here — so the switch is a convenience, not the safety. The safety is the
once-per-process guard, and new tests writing only into their own temporary directories.

### 6.5 Tests (red first)

1. Two runs into one directory: the first run's `TestRunReport.json`, `Failures.md`, `Failures.jsonl`
   and `History.run.json` are under `runs/<first>/`, byte-identical to what it wrote; the top level is
   the second run's. **The issue, as a test.**
2. Failing run, then four green runs with `KeepRuns = 2`: the failing run is still retained; exactly three directories.
3. A zero-scenario pass rotates nothing. The same run id twice rotates nothing.
4. A differently-named report in the same folder is untouched; `CLAUDE.md`/`AGENTS.md` never appear under `runs/`.
5. A read-only previous report: the run completes, the diagnostic names the file, `StaleOutputTests` still pass.
6. `DirectoryName` gives the same string on Windows and Linux for `local:`, `gh:` and `ado:` ids and a hostile custom id.
7. `kronikol query failures <dir> --run last-failed` answers from the retained report; `interactions`/`http`
   walk the ladder the issue could not; an unretained id exits 2 and names `history --run`.
8. `kronikol merge <dir>`, `kronikol query summary <parent-of-dir>` and `kronikol history record <dir>`
   ignore `runs/` — **on main a green directory with a failing retained run merges to 12 scenarios, 1
   failed (F6); the test is that transcript.**
9. History off: `Run.json` still names the run, and rotation uses it.
10. `KeepRuns = 0` / `KRONIKOL_KEEP_RUNS=off`: the directory is what 3.21.0 writes plus `Run.json`.
11. **F9:** two runs with the same CI run id into one directory, from two processes → both retained, as
    `gh_1_1` and `gh_1_1-2`; `--run gh:1:1` refuses and lists both. Two calls in *one* process → one rotation.
12. A run killed before `Run.json` is written leaves none; the next run rotates by the fallback rule and
    `doctor` names the manifest-less directory.
13. An attachment is in the retained run's `attachments/`, the retained HTML's link to it resolves, and
    the newest run's own attachment of the same file name is untouched.
14. A directory written by 3.21.0 (no manifest) rotates once by the fallback and never again.
15. **The manifest lists what reached disk:** with the schema, a component diagram image, `CiSummary.md`
    and an attachment all on, every file in the directory except `CLAUDE.md`/`AGENTS.md`/`Run.json` is in
    `files` or `attachments`, and nothing in the manifest is missing from disk. An output made to fail
    (the directory-in-the-way setup of `ReportGeneratorAgentOutputsTests` `:137`) is absent from it; a
    write that fell back to `Failures2.md` is listed under that name.
16. **A held report (Windows-only test):** with `TestRunReport.json` open read-only, the run completes,
    nothing is under `runs/`, no `.incoming-*` remains, and the diagnostic names the file. With a
    *later* file held instead, the staged files are back where they were.
17. `.incoming-x/` left behind: `--run` does not offer it, pruning does not count it, `doctor` names it.
18. **F13:** a fail-once test under `--retry-failed-tests` leaves the failing attempt under `runs/`, on
    CI variables at `KeepRuns = 0` too, and the newest run's `Failures.md` names it.
19. **F15 — a retained attempt is an attempt** (`Record_reads_a_retained_attempt_of_the_same_run_as_an_attempt_not_as_a_shard`):
    `Reports/History.run.json` (the retry: one scenario, `P`) over `Reports/runs/gh_7_1/` (three, `PFP`)
    → one run, `PPP`, `attempts` `-2-`, slots `0,0,0`, one shard, the failure's text kept. **On `main`:
    "4 scenarios from 2 shards".** *(In the prototype patch, with 20 and 21.)*
20. **F15:** a retained run with *another* id is left alone — `main` records it, out of order.
21. **F15:** two shards in two directories, one of which kept an attempt → `Shards` 2, `PPPP`, `---2`.
22. **F15, analyzer end:** the overlaid run reads `flaky`, evidence "passed on retry 2 in this run",
    no scenario reads `new`, and the roster is still three. A retry that fails again is `F` on attempt 2.
23. `kronikol merge` writes no `Run.json` and rotates nothing: `No_json_writes_the_html_alone` stays
    exactly as it is (§6.1's correction).

---

## 7. What is not taken, and what is fixed in passing

- **Refusing to overwrite a failing report (#80 fix 2).** A test run that stops to ask for `--force`
  is a test run that fails over its bookkeeping. The failure pin gives the same guarantee silently.
- **`--full` / `--no-truncate` (#82 fix 1).** A flag the reader must already know to pass is the
  problem restated. The detail view is whole by default; lists name the detail view.
- **Tail-first cutting of the stored error key (#82 fix 4, the stored half).** The key clusters
  failures across runs and is the one cross-language contract history has (`REMAINING_PARITY.md`
  `:1985`); changing how it is cut splits every existing cluster. View-only. Raising
  `ErrorKeyLimit` is a separate question — open question 3.
- **Keeping only `Failures.*` per run (#80 fix 3).** Subsumed: those files move with the rest, and
  they are what the failure pin reads.
- **Fixed in passing:** F11 and F14 (S0 — both live today, neither one of the four issues); the drifted managed block in `CLAUDE.md` / `AGENTS.md`, plus a test
  holding the repo's own two files to `templates/agents/CLAUDE.md` the way `SkillDriftTests` `:394`
  holds the skill; the missing exit-2 pin (§3.4); the stale doc comment on `FailuresDigest.Jsonl`
  ("empty when nothing failed", F10); F4's analyzer cut, which is a fix for today's
  downloaded-CI-report reading as much as groundwork for `--run`.

---

## 8. Versioning, docs, parity

| Release | Slices | Bump and why |
|---|---|---|
| next patch | S0 (may ship alone, ahead of everything) and S1, S2 | **Patch** — two misleading outputs corrected; no flag, option or type a consumer can call. (`nearMisses` and `FlakyShortfall` are additive members of a surface the standing permission covers; if that reading is too generous, S2 waits for the minor after it.) |
| next minor | S3 | **Minor** — `--run`, `--sid`, `--window`; a new mode of the verb. The F4 cut is a fix riding along, called out in the changelog. |
| the minor after | S4 | **Minor** — `KeepRuns`, `KRONIKOL_KEEP_RUNS`, `--run` universal. The default changes nothing at the top level and nothing at all on CI. |

No numbers are written here on purpose: `PLANS_STATUS.md` already records `NOTE_APPEARANCE_CONTROLS_PLAN.md` as
shipped in 3.22.0 while `Directory.Build.props` and the tags still say 3.21.0 (checked 2026-09-18), and
the noise plan may land in between. Take the next free number of the stated kind at release time.

**Docs, each in the slice that changes the behaviour:** `templates/skills/…/SKILL.md` (the ladder
gains "the report is not the run you want" → `history --failing` → `--run`) and
`references/commands.md` (both copies); `src/Kronikol/Reports/agent-instructions.md`;
`templates/agents/CLAUDE.md` + the repo's two copies; wiki `Querying-Reports.md` (`### history`
`:824`, `## Exit codes` `:971`), `Cross-Run-History.md` (`## Reading it back` `:278`),
`Generated-Reports.md` (output-files table `:3` — gains `Run.json` and `runs/`, with the manifest's
fields; `## Failures.md` `:292`), `Report-Configuration.md`
(`:188` and a `KeepRuns` row), `Merging-Parallel-Reports.md` and `CI-Artifact-Upload.md` (the
`runs/` exemption); `README.md:198`. The "`# No failures`" sentence lives in `CLAUDE.md:79`,
`AGENTS.md:18`, `templates/agents/CLAUDE.md:18`, `agent-instructions.md:28`,
`ReportConfigurationOptions.cs:250`, wiki `Report-Configuration.md:188`, `Generated-Reports.md:14`, `:325`.

**Parity.** Kronikol4J has none of this. One `REMAINING_PARITY.md` entry for S4 (.NET-only `runs/`
layout; report bytes unchanged, so byte parity holds with `KRONIKOL_KEEP_RUNS=off`, which the harness
should set beside `KRONIKOL_HISTORY=off`). The ledger format does not move in any slice.

**Unshipped plans that read this surface:** `MCP_PLAN.md` returns `--json` bytes and excludes
`history`; `QUERY_PORTABILITY_PLAN.md` wraps the binary, so flags ride free; `QUERY_FALLBACK_PLAN.md`'s
`query.py` has no `history`. No work in any of them; `--run` as a universal flag is worth a line in
the MCP plan's tool schema when that plan is next touched.

---

## 9. Consumer acceptance (BreakfastProvider)

The reporter's suite is private; BreakfastProvider has the same shape (Reqnroll + xUnit among six
frameworks, emulator-backed, `.kronikol/history.jsonl` in use) and one thing theirs lacks — a LightBDD
suite, which drops an extra `.generation-complete` into the reports directory and must survive rotation.
(READ, 2026-09-19: it does by construction. LightBDD creates that file itself — it is the path
Kronikol hands `AddFileWriter`, `ReportWritersConfigurationExtensions.cs:83` — before the pipeline
runs, so it is never in a manifest and never moves; and its open handle is on a sibling of the files
that do move, which blocks nothing. The replay still checks it.)

Replay the session, once per release, and record the transcript in §12:

1. Break one scenario (point the BigQuery emulator at a closed port for that feature). Full run → 1 failed.
2. Re-run that feature, filtered, twice → green. *The destructive instinct, on purpose.*
3. `history <dir> --failing` → **S2:** names the scenario and the run. `history <dir> s<n>` → **S1:** the emulator's message, whole.
4. Delete `TestRunReport.json`. `history --run last-failed --failing` → **S3:** answers from the ledger.
5. Restore; `failures <dir> --run last-failed` → `interactions … --service BigQuery` → `http …` → **S4:**
   the ladder the issue could not walk, against the retained run.
6. ~~`kronikol merge` over the suite's reports directories~~ **Does not apply to this consumer (RUN,
   §11.4 row 2):** BreakfastProvider has `GenerateMergeableData` off, so `merge` refuses its reports
   before F11 or F6 can matter. S0 and the sweep exemption are accepted on Kronikol's own fixtures
   (§6.5 test 8) instead. *Kept for a consumer that merges:* exit 0 with `History.run.json` present,
   which is exit 1 on 3.21.0; scenario and failure counts equal the newest runs' sums, the retained
   failure absent (F6).
6a. **F14:** copy the newest report out of its directory *without* `History.run.json` and ask
   `history <copy> --regressed` after step 1's failing run → the broken scenario is named. On 3.22.1
   it reads `failing … 2 runs` and `--regressed` is empty.

BreakfastProvider's CI also sets the expectation for the CI default: its artifact step uploads the
whole reports directory and its Pages job publishes it, so step 7 is a CI run on the S4 release with
no configuration change, asserting the artifact holds no `runs/` folder.

---

## 10. Open questions (recommendations attached)

1. **`KeepRuns` default on CI.** *Recommend 0*, as written. The retried-step case (#80 §3) is real but
   opt-in costs one environment line, and default-on changes what consumers' globs match.
2. **Should a retained green run keep its 83 MB report, or only the small files?** *Recommend keep
   whole* — `diff --baseline` against the last green run is the next thing a reader wants, and the
   count is already small. Revisit if plan 5's compression does not land.
3. **Raise `ErrorKeyLimit` from 200?** *Recommend no, for now.* S4 makes the whole message reachable;
   a longer key buys little and re-clusters every ledger.
4. **`--run` substring matching.** *Recommend yes* — run ids are 31 characters nobody will type, and
   an ambiguous match refuses with the list. Cut it if it reads as too clever in review.
5. ~~**What should the ledger say about a re-run inside one CI job (F9)?**~~ **ANSWERED — F13, then
   F15 (§6.3).** This question's own worry was right and its recommendation wrong: "folding them as
   shards would give one run holding the same scenario twice" is exactly what F13's first remedy then
   did (RUN: a phantom `new`, then ABSENT), and the answer is not a step ordinal in the id or any
   other format change — it is `attempts`, which the format has had since v1. One position, the later
   attempt's result, the count. *Left of the question:* the direct-append case (§6.3, "Not covered").
6. **`diff` against a retained run.** "What differs between the failing run and the green re-run" is
   the flaky-debugging question, and S4 puts both on disk. ~~`diff <dir> --baseline <dir>/runs/<name>`
   already works once the sweeps are fixed.~~ **That command does not exist (RUN, 3.22.0 tool,
   §11.5 row 4):** `--baseline` takes no value — it means `<reports>/baseline/TestRunReport.json` or
   `$KRONIKOL_BASELINE` — so the line above is exit 2, "No baseline to compare against". Three forms
   *do* work today against a `runs/` layout, none of them waiting on a sweep fix: `diff
   <dir>/runs/<name>/TestRunReport.json <dir>/TestRunReport.json`, the same with the two
   **directories** (`diff <dir>/runs/<name> <dir>` — a reports directory with `runs/` beneath it still
   resolves to its own report), and `KRONIKOL_BASELINE=<retained report> … diff <dir> --baseline`.
   Each answered `Fixed (1): fixed s0 …`, exit 0. *Recommendation unchanged in substance:*
   `--baseline-run ID` (the `--run` resolver, so `--baseline-run last-failed`), wired through the
   path `$KRONIKOL_BASELINE` already takes — under a day; otherwise the skill's recipe table gets the
   two-directory form, which needs no flag at all.

---

## 11. Assumption ledger

Every load-bearing claim in this plan, with how it is known — the form of
`CROSS_RUN_HISTORY_PLAN.md` §17. §1 holds the transcripts; this section is the bookkeeping, including
what is **not** known.

### 11.0 The error class this plan has made, and the rule that catches it

Six of this plan's own statements were reversed by checking them, inside one day — and four more the day after (the rows marked 2026-09-19). They are the
history plan's mistake again, unchanged: **a claim about existence or shape was verified, and allowed
to transfer to a claim about behaviour that was not.** Two came from the issues and were nearly
inherited; four are this plan's own first draft.

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| #81 shows a transcript ending `echo $?` → `0` | the tool exits 0 on a usage error | it exits 2, and has since 3.0.47; the 0 belonged to the last command of a pipe |
| #84's scenario shows `runs seen: 4`, and `--min-runs` is 5 | the bar is why it was not flaky | the bar counts *verdicts*, 5, and was met; one failing episode was the reason — for all 174 near-misses in a real ledger, too |
| three sweeps search `AllDirectories` | a retained run would be **double-counted** by `merge` | identical copies are de-duplicated; a run that *differs* is merged in — a green report gains a failure |
| `Failures.jsonl` is "one JSON object per line, empty when nothing failed" (its own doc comment) | an empty file means a green run | a green run writes a header line; the comment is stale |
| `RunOutputs` returns the set of outputs `written` | it lists the files a run wrote | it lists *labels*; two are not file names and three writers never pass through it |
| report file names are configurable, so consumers must glob for them | default-on `runs/` would break CI globs | the consumer checked uploads and publishes the *whole directory* — nothing breaks, old runs are silently published |
| *(2026-09-19)* given both fragments, `record` folds them into one run with the `F` and the `P` and the error text kept | so folding the retained attempt "reaches the ledger" as it should | the fold gives the retried scenario a second slot: the analyzer reads a **new** scenario, then an **absent** one (F15). The fold was run; what the analyzer made of its output was not |
| *(2026-09-19)* `AnalyseStream` builds `prior` from the stream's runs | cutting it when the run is "found among the stream's runs" fixes F4 | `Analyse` calls it again for the comparison stream, which never holds the run; that reading stayed wrong |
| *(2026-09-19)* `diff` has a `--baseline` flag and a `runs/` folder holds reports | `diff <dir> --baseline <dir>/runs/<name>` works | the flag takes no value: exit 2. The forms that work were one command away |
| *(2026-09-19)* rotation does not change how a run is written | every pin on the top-level file set holds | the *manifest* is a new top-level file, and the pin that lists a directory exactly is on `merge` — which turned out not to reach the writer at all |

Ten now, not six. Every one had a real citation beside it. The rule that caught them is the history plan's, and it is
mechanical: for any claim that decides what gets built, do not ask *how do I know this is true* — ask
**what would I have seen if it were false, and did I look there?** For five of the six, "there" was
one command away.

### 11.1a Existence and shape — cheap to check

| Claim | How verified |
|---|---|
| The run rows cut the error at a fixed 80; evidence at 400, the run list at 180 | `QueryCommand.History.cs:186`, `:164`, `:130` read |
| The ledger keeps the first line of a message, ≤ 199 chars; `compact` drops error text outside the window | `HistoryFormat.cs:88`, `HistoryRunBuilder.cs:96`, `HistoryLedgerWriter.cs:248` read |
| Rosters carry names, features and sources; runs carry results, durations, errors, a shapes hash | `HistoryModel.cs:27-48`, `:162-244` read |
| `ScenarioHistory` already has `Failures`, `Flips`, `LastFailedRunsAgo`, `RealVerdicts`; it has no episode count and no reason-not-flaky | `HistoryVerdicts.cs:158-253` read |
| `query history` has no `--window`, `--run` or `--sid`; `sid:` exists as an address kind | `QueryOptions.cs:115`, `:135-143`; `VerbTable.cs:296` |
| A flag must be registered in six places and documented in both skill copies or tests fail | `DescribeTests.cs:85-116`, `SkillDriftTests.cs:138-218`, `:394-418` read |
| Every run id form has two colons; no run-id-to-path function exists | `HistoryRunBuilder.cs:154-177`; grep for sanitisers across `src/` |
| The run id exists before the first file is written, after the zero-scenario guard | `ReportGenerator.cs:168-181`, `:323`, `:461` read |
| Nothing in the run path deletes or moves a file; every write is in place | grep for `File.Delete`, `File.Replace`, temp-and-move under `src/Kronikol/Reports` |
| History is switched off for Kronikol's tests in five `test.runsettings` files, line 12 | grep |
| Exit codes are stated in four places, one machine-readable and test-pinned | `VerbTable.cs:299-305`, `DescribeTests.cs:134-140`, `commands.md:492`, wiki `Querying-Reports.md:971` |
| The two skill trees are byte-identical and pinned; the `CLAUDE.md`/`AGENTS.md` block is drifted and unpinned | MD5 of each; `SkillDriftTests.cs:394-418`; no test names the repo's own two files |
| Kronikol4J has no query tool, no history, no reports-folder option | grep of `../Kronikol4J` |
| `v3.21.1` is unused | `git tag -l` → `v3.21.0` only |
| BreakfastProvider's CI uploads and publishes the whole reports directory | `_tests.yml:410-416`, `ci-main.yml:705-811` read |

### 11.1b Behaviour — the kind that gets reversed

`RUN` = executed here and the output read; `READ` = source read and reasoned about; `DOC` = somebody
else's statement taken at its word. **A `READ` row is not evidence that the value reaches anything.**

| Claim | Depth | How verified |
|---|---|---|
| A missing report is exit 2 — bare, under `--json`, and through `dnx`; 0 appears only as a pipe's last stage | **RUN** | built 3.21.0 tool in bash (`PIPESTATUS`) and PowerShell (`$LASTEXITCODE`) |
| The analyzer counts runs *after* the current one as prior | **RUN** | run 12 of 23 of `reqnroll-in-memory`: 22 "earlier" runs, 11 dated after it |
| A report-less reading works and loses nothing but call names unless the shapes line is passed | **RUN** | 394 ledger-only analyses; 712 behaviour verdicts with and without shapes, 39 scenarios gain named calls |
| Reading every run of the ledger is affordable | **RUN** — at 394 runs only | 139 ms; the 5,000 × 50 synthetic ledger of the history plan was not re-measured with window 0 (§11.2) |
| 80 characters cuts real messages before their "but found" | **RUN** | 5 of 8 distinct CI error texts; 0 reach the 199 cap |
| The `--failing` hint fires on 5% of green runs, up to 11 scenarios; every flaky near-miss is `OneEpisode` | **RUN** | the replay, §5.2 |
| `kronikol merge <dir>` fails on a `History.run.json` beside the report | **RUN** | fixture + fragment → exit 1; the fragment was hand-written, the failure is the absent `features` key |
| A differing retained run is merged in as a shard; an identical one is not | **RUN** | 12 scenarios, 1 failed / 11 scenarios |
| A `runs/` folder leaves `query <reports-dir>` and `query <reports-dir>/runs/<x>` working and makes `query <parent>` ambiguous | **RUN** | BreakfastProvider's ReqNRoll report in that layout: exit 0, 0, 2 "Several reports under" |
| A move inside a volume is constant-time; a directory rename publishes atomically | **RUN**, three file systems | 100 MB `File.Move` 1 ms, `Directory.Move` 1 ms (NTFS); 0.14 ms and 0.25 ms (ext4); 0.17 ms and 0.27 ms (overlayfs) — §11.5 row 6 |
| A reader holding the report the way `query` does blocks the move **and** today's overwrite; delete-sharing unblocks the move | **RUN** | `File.OpenRead` holder → both `IOException`; `FileShare.ReadWrite\|Delete` holder → move succeeds (NTFS). **POSIX never blocks either — RUN since (ext4, §11.5 row 6):** under a `File.OpenRead` holder both `File.WriteAllText` and `File.Move` succeed; only a `FileShare.None` writer is refused (.NET's advisory `flock`), and `WriteFile` is `File.WriteAllText`. The permissive case has its own cost — §6.2's torn scan |
| `P P F P P` is not flaky because it is one episode, with `MinRuns` met | **READ** + computed | `HistoryAnalyzer.cs:297-301`; agrees with the noise plan's F5 and with the replay's 174/174 |
| A second line with the same suite and run id is `Duplicate` on append | **READ**, test-pinned | `HistoryLedgerWriter.cs:136`; `HistoryOutputsTests.cs:171-179` |
| Sibling files of a report resolve from `index.Directory`, so a retained run read as a directory finds its fragment and attachments | **READ** | `QueryCommand.History.cs:288`, `QueryCommand.Narrative.cs:103,168,297,654` — the RUN above had no fragment or attachments beside it |
| The `AsyncLocal` reports directory flows into the `Parallel.Invoke` workers, so a collector scoped the same way will too | **RUN** (2026-09-19) | a `ConcurrentBag` behind an `AsyncLocal`: adds from two workers, from a `Task` inside a worker and from a `Thread` inside a worker all reach the parent (4 of 4); a worker that *replaces* the value does not disturb the parent's. No `SuppressFlow` anywhere in `src/` |
| A nested `CLAUDE.md` loads when an agent reads a file beside it | **DOC** | `AgentInstructionsGenerator.cs:7-11`'s own comment. The design does not depend on it: retained runs get no `CLAUDE.md` either way |

### 11.2 Not verified — and what this plan does about each

| Assumption | Status | How the plan avoids depending on it |
|---|---|---|
| The reporter's session happened as described (17-minute suite, 396 scenarios, Testcontainers) | **Private; taken from the issues** | Every mechanism was reproduced on other data; §9 replays the session's *shape* on BreakfastProvider. Nothing is sized from their numbers except the retention default's upper bound (#85's 82.7 MB) |
| ~~How a re-run inside one CI job folds into the ledger~~ | **RUN, §11.4 row 3** — it does not fold: the later fragment overwrites the earlier one. Given both, `record` folds them as shards — **and that fold is wrong for a retry (RUN, §11.5 row 1: a phantom `new`, then ABSENT)** | F13: S4 keeps the earlier attempt; **F15:** `record` overlays it as an attempt, never folds it as a shard (§6.3) |
| `KeepRuns = 3`, the failure pin, "at most 5 runs back" for the hint, five addresses per hint, the 60/40 head–tail cut | **Chosen, not derived** | Each is an option or a constant in one place; the hint bounds are derived from one ledger (§5.2) and the text states them rather than hiding them |
| Rotation is safe against a run in *another* process writing the same directory at the same moment | **Not examined** | It is no less safe than today, where two such runs already interleave their overwrites. The staged publish means the retained copy is whole or absent, never mixed |
| Reading every run stays cheap on a large ledger | **RUN at 5,000 × 250 — 64 MB, ≈1 s** (§11.4 row 6) | `--run` is the only path that does it, and it is an explicit question; a second on an extreme ledger is acceptable, so no windowed fallback is built |
| ~~Long paths: `…/bin/Debug/net10.0/Reports/runs/<31 chars>/attachments/<name>` adds ~45 characters~~ | **RUN (§11.4, long paths)** — a 440-character path rotates and is queried; Explorer and browsers not tried | A failure is a rotation failure — announced, never fatal |
| The tool can find, in the ledger, the run a report describes | **RUN — false for a local report without its fragment (F14)** | S0 adopts the ledger's own line; S4's `Run.json` carries the id |
| A retained run read as a directory finds its fragment and attachments beside it (§11.1b, READ) | **Still READ** — the RUN of that layout had no fragment or attachments in it | S4's test 7 and test 13 are that RUN; nothing earlier depends on it |
| S2's `FlakyShortfall` and S3's ledger-only mode behave as designed | **Not prototyped** — the mechanism of each was run as a harness (the 394-run replay *is* ledger-only mode, and gave every near-miss reason), the verb was not built | Both are one file each; their tests (§3.5, §5.3) are the red-first list |
| A test runner's retry extension re-runs failed tests in a new process that writes a second, smaller report into the same directory | **RUN — true (F13)**, for the MTP retry extension under TUnit; it restarts the test host whatever the framework, other runners not run | "No further work" was wrong: S4 needed its CI default and the `record` sweep changed (§11.4) |
| ~~BreakfastProvider has `GenerateMergeableData` on~~ | **RUN — it is off**; `merge` refuses its reports | §9 step 6 does not apply to that consumer |
| `FileShare.Delete` on the tool's opens is safe for a query in flight | **Unsafe as-is — investigation 1, closed** | §6.2 item 4 ships only behind the changed-file check that investigation specifies |

### 11.3 Open investigations

1. **~~Can a query read the wrong bytes if the report is replaced under it?~~ CLOSED — yes, and
   silently (READ, followed to the returned value).** The index is built by one scan; payloads are
   read later by offset through a new open of the same path (`PayloadReader.cs:47-51`).
   `PayloadReader.Read` (`:27-44`) seeks, reads `slice.Length` bytes, and on a `JsonException`
   **returns the raw bytes as text** — nothing compares a hash, a length or a timestamp. So a report
   replaced between the scan and a payload read produces a slice of some other payload, presented as
   the answer. On Windows the window is between opens within one command; on Linux and macOS nothing
   ever blocked it. **Fix, in S0, before the sharing change:** `ReportIndex` already records
   `FileLength`; add the last-write time at scan, check both in `PayloadReader.Open`, and fail with
   exit 1 — `the report changed while it was being read; run the command again`. Then the two opens
   can take `FileShare.ReadWrite | FileShare.Delete`, and the tool stops costing a finishing run its
   report. Red test: scan, overwrite the file with a longer report, read a payload → today a wrong
   string, after the fix exit 1. *RUN since (§11.4 row 1): a wrong string for a longer
   replacement, an uncaught `EndOfStreamException` for a shorter one.*
2. **~~Does the hand-written fragment stand in for a real one in F11?~~ CLOSED — yes, and a real `ctrf-report.json` fails it too (§11.4 row 2).** Almost certainly — the reader
   rejects on the absent `features` key — but S0's first red test uses a fragment a real run wrote,
   and `ctrf-report.json` beside it, before any fix is written.
3. **~~A killed rotation.~~ CLOSED on NTFS — resumable, 0 bad states in 367 kills (§11.4 row 4); and on Linux since — ext4 0 in 278, overlayfs 0 in 200 (§11.5 row 6).** Kill the process between the first move and the publish, on Windows and on
   Linux, and check the next run's behaviour and `doctor`'s wording against §6.2. This is the one
   part of S4 that a unit test cannot honestly cover.
4. **~~The `dnx` line in the agent block.~~ CLOSED — byte-identical at 86 KB (§11.4 row 5).** `dnx` forwards the exit code (run); whether it also forwards
   a *long* stdout unbuffered under the byte budget was not looked at, and is the other half of
   "`dnx` is a safe recommendation".

### 11.4 Investigations run on 2026-09-18 (late) — results, and what they change

All RUN against an isolated `git archive` copy of HEAD (3.21.0), never the working tree.

| # | Question | Result | What it changes |
|---|---|---|---|
| 1 | Investigation 1's red test (F12) | **Confirmed.** Scan a report, overwrite it with a longer one, read the *last* body: the answer is a raw mid-string slice of a different body (`u0022:"value …`), no error. Overwritten with a *shorter* one: `EndOfStreamException`, which nothing catches — `RunCore` guards only the scan (`QueryCommand.cs:113`), the verb dispatch (`:158-181`) and `Program.cs` have no handler, so the tool crashes (crash path READ). The first probe read the *first* body, which sits at the same offset with the same bytes in both files, and "passed" — the §11.0 rule, again | S0's test reads a late body; S0 also turns an `IOException` from a verb into exit 1 |
| 2 | Investigation 2 (real fragment, F11) | **Confirmed**, and wider: a real `History.run.json` **or** a real `ctrf-report.json` alone beside a mergeable report → exit 1. `Failures.jsonl`, `Specifications.yml`, the schema → harmless. The wiki's recipe survives only because it uploads `**/Reports/TestRunReport.json` and the fragments as a separate artifact; a pipeline that uploads the directory whole does not. BreakfastProvider has `GenerateMergeableData` off, so §9 step 6 does not apply there | none to the fix; §9 step 6 is dropped for that consumer |
| 3 | Retry extensions; a same-job CI re-run (§11.2 rows 2 and 7, open question 5) | **F13.** Attempt 1: 13 scenarios, 1 failed. Attempt 2, new process: 1 scenario, passed, and the directory says "No failures". Local ledger: both runs, the second `partial: true`, verdict `fixed FP`. CI (`gh:777:1`): one fragment survives, `record` stores 1 scenario `P`. Given **both** fragments, `record` folds them in either order into one run — "14 scenarios from 2 shards", `PPFPPPPPPPPPPP`, error text kept | see below |
| 4 | Investigation 3 (killed rotation), NTFS | 367 kills landed inside a 402-file rotation: **0 bad states**. Every file existed exactly once and whole; the report JSON was always staged first; the top-level `Run.json` was always still there while staging was incomplete. Linux not run (`rename(2)` is atomic — DOC; a cross-device move is copy-then-delete and is not) | a killed rotation is **resumable**, see below |
| 5 | Investigation 4 (`dnx`, long stdout) | 86,766 bytes through `dnx` byte-identical to the direct run; exit code forwarded; stderr empty; an early-closed pipe is fine. 86 KB is the largest answer the row limits allowed on that report | closed |
| 6 | Whole-ledger read on a large ledger (§11.2 row 5) | 5,000 scenarios × 250 runs **in the real line format is 64 MB** (≈250 KB a run line, not the prototype's 13 KB). Read, window 50: 230 ms; window 0: ≈1,000 ms, 270 MB of heap | `--run` stays an explicit, whole-ledger read; it prints nothing new below a second |

**What F13 changes in S4.**

1. **§6.4's CI default.** Not 0: on CI, *keep the earlier attempts of this same run* — retained runs whose
   `Run.json` id equals the current run's. On a fresh checkout that is empty unless a retry or a second
   step happened, which is exactly the case worth keeping; on a persistent runner it still does not
   accumulate old runs or publish them. `KeepRuns` keeps its meaning for everything else (0 on CI).
2. **§6.3's `record` sweep.** `HistoryCommand.FragmentFiles` does not blanket-exempt `runs/`: it takes a
   retained fragment **whose run id equals the top-level fragment's** and leaves the rest. They fold as
   shards (row 3), so the failed attempt reaches the CI ledger beside the retry's pass. `merge` and
   `query` keep the full exemption.
3. **The newest run says so where an agent reads first.** When the rotated run had failures,
   `Failures.md` and the agent block carry the line the console gets (§6.2): *an earlier attempt of this
   run failed — `runs/<name>`*. "All 1 scenarios passed" directly after a failure is the most misleading
   sentence in this whole plan's territory.
4. **Tests to add to §6.5's list:** a fail-once test under `--retry-failed-tests` leaves the failing attempt in
   `runs/`; under CI variables `history record <dir>` stores the `F` and the `P`.

**What row 4 changes in §6.2.** At the start of a rotation: a `.incoming-*` that contains a `Run.json` is
complete — publish it; a `.incoming-<name>` matching the top-level `Run.json` is an interrupted staging
— carry on moving into it. `history doctor` reports only what neither rule explains.

**Found on the way, not this plan's.** `HistoryAnalyzer.Analyse` took **≈2.2 s at 5,000 scenarios × 50
runs** (1.7 s with bare run lines), against the history plan's measured 0.8 ms and 150 ms budget. The
pinned test (`HistoryLedgerTests.cs:591-628`) times the *read* only, on bare lines, against 1,500 ms —
so nothing guards the analysis, and it runs at the end of every test run. Cause not investigated. Filed as
**#91**; it should be settled before any slice here adds analyzer work (S2's `FlakyShortfall` is per-scenario
and cheap, but it is measured against this baseline, not the old one).
*Since (2026-09-19): the cause is `HistoryRoster.IndexOf`, a linear scan asked once per scenario per
prior run. `HISTORY_ANALYZER_COST_PLAN.md` owns the fix and lands first; §11.5 row 7 is this plan's
independent measurement of the same thing.*
*Settled (2026-09-21): shipped as 3.25.2. The baseline S2's `FlakyShortfall` is measured against is now
about 200 ms at 5,000 × 50 (1,950 before), linear in scenarios × window, and whatever a slice adds to
`AnalyseScenario` is timed with `tools/history-replay --time <ledger>` and replayed with `--full`. Two
things in that loop moved since this plan's prototype patch was cut: a prior roster is reached through
`priorPositions` (an index per distinct roster), and a point no longer carries its call list
(`HistoryPoint.CallSet` is gone; `CallSetOf(point)` spells one out on demand).*

**Long paths (RUN, Windows, `LongPathsEnabled = 0`):** the staged rotation — two `File.Move`s, then
`Directory.Move` — succeeded on a 440-character path, and `kronikol query summary` on the retained
run there answered with exit 0. .NET's file APIs are not bound by `MAX_PATH`; Explorer and browsers were
not tried. §11.2's long-path row is closed for the tool and the rotation.

~~Still open: ext4/overlayfs timings; open question 6; striking open question 5 in §10 and §9 step 6
(both answered above) is editorial and not yet done.~~ All four done on 2026-09-19 — §11.5.

### 11.5 Investigations run on 2026-09-19 — results, and what they change

All RUN in a detached `git worktree` of `125a32cc` (3.22.0) under a scratch directory, never the
working tree; harnesses are file-based apps over that worktree's `Kronikol.csproj`. The prototype is
`EVIDENCE_SURVIVES_A_RERUN_PLAN.prototype.patch` (8 files, +456 −4): **every new test was red on
`main` before its fix**, the whole unit suite is **5,160 tests / 0 failed / 1 skipped** with it, it
applies to 3.22.1 (`82abeb7f`), and it applies **with** `HISTORY_ANALYZER_COST_PLAN.prototype.patch`
in either order — built together, the five history test classes are green (53 + 36 + 18 + 19 + 14).
`HISTORY_VERDICT_NOISE_PLAN.prototype.patch` applies after it too (apply-check only). It is evidence,
not the implementation: no docs, no changelog, no `--run`. The harnesses, the two real retry
fragments and every output quoted below are in `EVIDENCE_SURVIVES_A_RERUN_PLAN.evidence.zip` beside
this file (12 files, 17 KB; its `README.md` says how to point them at a fresh worktree).

| # | Question | Result | What it changes |
|---|---|---|---|
| 1 | What does the analyzer *say* about F13's folded run? (the fold had been run; its reading had not) | **F15.** Folded as shards: `broke` + a phantom **`new`** on slot 1; next run `fixed` + **ABSENT**. As one position with `attempts` = 2: `flaky … passed on retry 2 in this run`, then nothing. `HistoryFold.Attempts` built and run on the **real** retry fragments: roster 13, `PPPPPPPPPPPPP`, `--2----------`, the first attempt's error kept, call sets re-indexed. `record` wired to group by reports directory: three new tests, red on `main` ("4 scenarios from 2 shards"; a retained run of another id recorded out of order; 3 shards for 2) | §6.3 rewritten; open question 5 answered; tests 19–22 |
| 2 | Can the tool find the run a report describes? | **F14 — not for a local report without its fragment.** BreakfastProvider's xUnit report: `…:14e947b5` with the fragment, `…:a25831f6` without. In a test: a broken scenario reads `failing  PPPFF  failing since <its own id>, 2 runs`, "4 runs recorded" for 3. Prototyped (`ReportHistory.OwnLine`): `broke  PPPF  passed in <the previous run>`, the run under its true id | S0 gains it; §3.2; test 12 |
| 3 | Is F4's fix right as worded? | **No — it misses the comparison stream** (red test stays red). Looked for among the suite's runs: green, and **394 / 394 runs of the CI ledger read in place agree with the hand-cut replay** — `RunsRecorded`, absent list, verdicts and evidence of every scenario; 712 `behaviour-changed`, the first replay's figure exactly | §3.2 corrected; test 10 |
| 4 | Open question 6 | The plan's command is exit 2. Two paths, two directories, or `$KRONIKOL_BASELINE` each work today: `Fixed (1): fixed s0 …`, exit 0 | §10.6 |
| 5 | What does the sharing change let a reader see? | On NTFS with `ReadWrite\|Delete`, and on ext4 **today**: the overwrite succeeds in place and the *same open handle* reads the new bytes. After a **move** the handle keeps the old file whole, and a fresh open of the path gets the new one | S0's check goes at the end of the scan too; rotation removes the torn read rather than adding one (§6.2) |
| 6 | Linux (§11.2, §11.3 item 3, "still open") | Fedora 43 on the WSL2 kernel, .NET 10.0.8, self-contained. **ext4:** 100 MB `File.Move` 0.14 ms, 402-file `Directory.Move` 0.25 ms; kills inside a 402-file rotation: 242 mid-staging, 3 staged-unpublished, 33 after publish — **0 bad states in 278**. **overlayfs:** 0.17 / 0.27 ms; a rotation whose files all sit in the *lower* layer publishes 402 of 402, report intact, nothing left staged; **0 bad states in 200 kills**. F7: `Path.GetInvalidFileNameChars()` is 2 characters there against 41 on Windows — the repo's idiom leaves `gh:18273645:1` as it is on Linux and even keeps the backslash, colon, star and question mark of a hostile id, while `[A-Za-z0-9._-]` gives one name on both. *Not shown: `Directory.Move` of a directory that exists only in a lower layer (`mv` masks `EXDEV` by copying); the rotation never does that — `.incoming-*` is always created by the running process* | §11.1b rows; §11.3 item 3 closed for Linux; the resume rule of §11.4 stands on three file systems |
| 7 | #91's cause (before `HISTORY_ANALYZER_COST_PLAN.md` was seen) | `HistoryRoster.IndexOf` alone reproduces the whole cost; analysis 0.19 → 0.61 → 7.5 → 24.9 s at 1,250 → 2,500 → 5,000 → 10,000 scenarios on a machine busier than the issue's; with one lookup per distinct roster, 0.23 → 0.27 → 0.42 → 0.65 s — linear. Same cause, same fix as that plan, reached separately | Nothing here: that plan owns it, and its hunk sits just below F4's. The lookup was taken back out of this prototype so the two patches do not collide |
| 8 | S1's cuts on real data | 1,217 readings with a verdict: evidence over 180 in 13, over 400 in 0 (longest 261); never more than 3 new or gone calls | §4.2: two more cuts named, measured as rare; parity test scoped |
| 9 | S4's READ rows | The collector pattern: **RUN** (§11.1b). `.generation-complete`: LightBDD's own file, never in a manifest (§9). `merge` does not reach the core writer, and one pin lists its output exactly (§6.1). Several reports in one folder: one manifest, like the digest (§6.2) | as cited |

**Seen on the way, not this plan's.** `NodeJsPlantUmlRendererTests.Batch_of_five_is_faster_than_five_single_spawns`
failed once in four full runs — "batch of 5 took 2499 ms, five single spawns 1903 ms" — while a
`dotnet publish` was running beside the suite, and passes alone. It compares two wall-clock times with
no margin; `HISTORY_ANALYZER_COST_PLAN.md` F4 is about exactly this kind of pin.

**A note for whoever next scripts WSL from Git Bash.** `wsl.exe -- sh -c '…$VAR…'` has the variable
expanded by an outer shell first. A cleanup line reading `sudo rm -rf $O` ran with `$O` empty — no
operand, no harm, by luck. Scripts go in a file, with `set -eu` and literal paths.

**Still open after this round:** S2 and S3 as a verb, and S4 itself, are unbuilt (§11.2); a retained
run read through its fragment and attachments is still READ; the direct-append retry case (§6.3).
None blocks S0, S1 or S2.

---

## 12. Execution log

Green-lit 2026-09-21 ("implement in full"). Three releases, carved by what CLAUDE.md's versioning rule
makes of each slice rather than by §8's table: S2 adds public types to the library
(`HistoryFlakyShortfall`, `FailingOutside`, two members of `ScenarioHistory`), which is a minor, so it
ships with S3 and not with the patch.

**What had moved under the plan before execution.** The noise plan (3.22.2 to 3.25.0), the as-of fix
(3.25.1, #95) and the analyzer cost plan (3.25.2) all landed first. 3.25.1 **is F4 and both halves of
F5**: `HistoryLedger.PriorRuns` cuts at the run's own line for every stream, the comparison stream
included, the window is per stream, and a run outside the window is parsed on demand. So S3 carries no
analyzer cut, §3.5's tests 2, 10, 11 and 14 are green before it as pins, and F5's twin ("49 for 50") no
longer reproduces. The fuller prototypes of §11.6 (S2, S3, the tool side of S4, the rotation) were found
alive in two scratch worktrees of 3.22.1 and ported by hand; §11.6 itself was never written into this
file.

### 3.25.3 (patch) - S0 and S1

- **F11** `MergeCommand.ResolveInputFiles` skips `History.run.json` and `ctrf-report.json` by name in a
  sweep and says so; named explicitly they are still refused. Tests generate both files with the
  writers a run uses. `Run.json` joins the list in S4.
- **F14** `ReportHistory.AdoptOwnLine` / `OwnLine`, used by the `history` verb and by the `history:`
  line of `failures` (`TryVerdictsSilently`), with the same-roster rule for an id match (the shard
  case) and the window-0 re-read for a report older than the window.
- **F12** `ReportIndex.LastWriteUtc`; `ReportScanner.ThrowIfChanged` at the end of the scan and in
  `PayloadReader.Open`; `ReportChangedException`; every open through `ReportScanner.OpenShared`
  (`FileShare.ReadWrite | Delete`); `RunCore` turns an `IOException` out of a verb into exit 1. Four
  tests, red first: a longer replacement (a wrong string on 3.25.2), a shorter one
  (`EndOfStreamException`), the CLI path through a `QueryCommand.AfterScan` seam, and the holder that
  no longer blocks an overwrite or a move. *Departure:* the plan's "the move succeeds under an open
  reader" is true of moving the held file AWAY, which is what rotation does; replacing a held file BY
  a move onto it is still refused by Windows, delete sharing or not (RUN: `UnauthorizedAccessException`).
- **S1** as §4.2, with `QueryWriter.Cut` (60/40 head and tail, surrogate-safe at both ends, never
  longer than the limit), `QueryWriter.Flat`, `CutNotice`. The "first line only" pointer is printed
  once under the run list, not per row. The parity test covers evidence, every `runs[].error`, the
  quarantine reason and both call lists, on a fixture with four new and four gone calls.
  `A_failure_in_a_degraded_run_says_so_on_its_row_and_in_the_statistics` moved with the error: the
  labels now end the row, which is what §2 moved it for.
- In passing: the managed block in `CLAUDE.md` (and the git-ignored `AGENTS.md`) and its new guard in
  `SkillDriftTests`; the `FailuresDigest.Jsonl` doc comment (F10).
- Verified by the whole solution: 51 test assemblies, 0 failed (Kronikol.Tests 5,296).

### 3.26.0 (minor) - S2 and S3

- **S2** as §5.2 with every correction of the 2026-09-19 round: `HistoryFlakyShortfall` is a `[Flags]`
  set, `FailingEpisodes`, `PreviousFullCount`, `FailingOutside` (latest verdict a failure, partial runs
  only), the per-filter empty note that depends on whether the run holds a failure, the five-runs-back
  and five-addresses bounds, an address on every counted failure. `QueryHistoryHintsTests` (14) ported
  unchanged.
- **§5.3 test 9, the replay guard, was not in any prototype and is new:**
  `QueryHistoryShortfallReplayTests` over `TestData/History/ci-ledger.xunit-in-docker.jsonl`, one suite
  of BreakfastProvider's CI ledger (25 runs, 3 of them failing) trimmed to what the status verdicts
  read, 76 KB. It holds the analyzer's shortfall to an independent count of each scenario's own points,
  and drives the real CLI over every run to check no hint names `--min-runs` or an episode the analyzer
  did not give.
- **S3** as §3.2 and its "Built" note, minus what 3.25.1 had already done. `HistorySubject` /
  `HistoryReading` carry one renderer for both sources; `--calls` works in ledger mode too, because the
  reading carries the run line and its shapes. `RunCore`'s three report-less refusals are one method,
  so a flag `history` cannot read is refused with no report as with one. An `s3` positional with a
  ledger reading is exit 2 naming `--sid`. `QueryHistoryLedgerOnlyTests` (15) ported; one pin moved with
  S1's error line.
- **Found by driving the built CLI over the real ledger, not by a test:** a hint printed under `--run
  gh:3:1` said `next: history --sid <id>`, which read the NEWEST run when followed. Every pointer of a
  ledger reading now names its run (`HistoryReading.Next`), the newest included; red test first. The
  wiki's example block was also corrected against real output (the summary line reads `nothing
  changed`, and notes carry no bullet).
- `--run` on the other verbs, the retained report tried first, and `RunIsResolved` wait for S4, as do
  the HTML tooltip's attempt label and `--run` among the universal flags.
- Verified by the whole solution: 51 test assemblies, 0 failed (Kronikol.Tests 5,330); CI and the
  Release workflow green for 3.25.3 before it was pushed.
- Cost: `tools/history-replay --time` over the whole CI ledger, warm analysis 5 ms and 12.5 KB a
  scenario, inside the 28 KB bound the cost plan set. `FailingOutside` runs on partial runs only.

### 3.27.0 (minor) - S4

**The owner's two decisions (2026-09-21):** a pass on retry keeps the failed attempt's error text,
labelled; and `HistoryLedgerWriter.Amend` ships.

- The rotation prototype (`RunRotation`, `RunManifest`, `RunFileCollector`, `ReportFolders`,
  `HistoryRunId`, 40 + 15 tests) applied to 3.26.0 with one conflict, an enum member beside the noise
  plan's. The tool side (`RetainedRunResolver`, `record`'s attempts, `Amend`, `doctor`, with their tests)
  applied with one, the tooltip's attempt label beside the partial and degraded labels. `--run` on
  every verb, the three sweeps on `ReportFolders.IsReserved`, `Run.json` in merge's skip list and
  `RunIsResolved` were ported by hand onto 3.26.0's `RunCore` and `History`.
- **§6.5, test by test:** 1 to 6, 9 to 17 and the CI rules are `RunRotationTests` and
  `RunManifestTests`; 7, 8 and 11's refusal are `RetainedRunsTests`; 19 to 21 are `HistoryCommandTests`;
  22 and the amend lane are `HistoryLedgerTests` and `HistoryOutputsTests`; 23 is new
  (`A_merge_is_not_a_run_it_keeps_nothing_and_writes_no_manifest`, green before and after: a pin).
  **18 is covered at unit level only** (CI variables, two processes simulated by the once-per-process
  guard's test reset). The real `--retry-failed-tests` run of §11.4 was not repeated: it needs a
  fail-once test in an example project, and a test that fails on purpose does not belong in the repo.
- **New, in no prototype:**
  - Azure DevOps artifact upload of the kept runs, each file under the folder it has on disk
    (`CiArtifactPublisher.RetainedFiles`); GitHub Actions is handed the directory. §6.3's last
    paragraph asked for it.
  - **"The run before this one failed".** Found by doing #80 for real on
    `Example.Api.Tests.CiPreview.Mixed`: 15 of 20 failing, then a filtered green re-run. Everything
    worked (`runs/` held the failing run, `failures --run last-failed` walked the ladder,
    `interactions --run previous s0`, `diff runs/<name> .`, the unretained refusal). But `dotnet test`
    swallows the console line, and the new `Failures.md` said `# No failures` and nothing else. The
    prototype said so only for a same-run retry. `FailuresDigestEarlierAttempt.SameRun` is false for
    the run just moved aside, and the digest says it once, by the run that replaced it. Red first.
  - `history doctor <reports-dir>` answered only `KRONIKOL_HISTORY=off` for a suite with history off.
    Kept runs do not depend on the ledger: it now answers for the directory whatever the ledger's
    state.
- **Not taken, on purpose:** `--baseline-run` (open question 6). The recommendation was a recommendation;
  the skill's recipe table and the wiki carry the two-directory `diff`, which needs no flag and was RUN
  on the real directory. *Taken on 2026-09-23 as 3.28.0, when the owner asked for the leftovers to be
  dealt with: the last section of this log.*
- **Statements the new default weakens, found and qualified:** "nothing rewrites the ledger during a
  test run" (the `Prune` and `Compact` doc comments and `Cross-Run-History.md`), `# No failures` means
  nothing failed (the agent file, the managed block, `Generated-Reports.md`), "a subfolder is never
  uploaded" (`CI-Artifact-Upload.md`).
- Docs: wiki `Generated-Reports` (a section of its own), `Report-Configuration`, `Querying-Reports`,
  `Cross-Run-History` ("A retry is an attempt, not a shard"), `Merging-Parallel-Reports`,
  `CI-Artifact-Upload`, `Diagnostics-and-Debugging`; README; both skill copies; the per-directory agent
  file; Kronikol4J's divergence ledger (the harness must set `KRONIKOL_KEEP_RUNS=off`).
- Nothing was posted on #80, #81, #82 or #84. #81's exit-code claim does not reproduce (F1) and the
  reply with the `PIPESTATUS` transcript is the owner's to send. *Posted and closed on 2026-09-23, each
  naming the release and the shape that shipped; #81's reply carries the codes measured on 3.27.3: 2
  direct, 0 only as `$?` behind a pipe, 2 in `PIPESTATUS[0]`.*

### 3.27.1 (patch) - the audit of 2026-09-22

Every deliverable of §3 to §8 was held against the shipped code, its tests and the docs, and the built
tool was run: over the trimmed CI ledger (every S2 and S3 answer, the exit-2 listing, the usage errors,
the `--json` envelope), over the example directory that kept three runs, and over BreakfastProvider's
own report and ledger. Everything held except four things, fixed here:

- **§3.2, "after S4 the tool checks and says whether it is [retained]" - not done.** The ledger-only
  header said `when the run is retained` in every case, including `history <reports-dir> --run <other
  run>`, where the run had just been looked for under `runs/` and not found. `HistoryReading` now
  carries the report's directory when there was one, and the line says `run <id> is not kept under
  <dir>` and what is kept there. Red first; RUN on BreakfastProvider's report against its ledger.
- **§4.2, "the row says where the rest is" - printed as placeholders.** The pointer under a stored
  message that ends in the ledger's own ellipsis read `kronikol query failures <reports-dir> --run
  <id>` with the brackets as they stand, once under the list. It is now under each such row, with the
  run's id and the reports directory, and says whether that run is kept there
  (`ReportHistory.WholeMessagePointer`, which reads manifests only): the report on top for the run being
  read, `--run <id>` when it is kept, `is not kept under <dir>` when it is not, and the condition only
  when no report was given. Three tests red first.
- **§8's eight places of "`# No failures` means nothing failed" - three still said it.** The
  `GenerateFailuresDigest` doc comment (it ships), `Report-Configuration.md`'s row and
  `Generated-Reports.md`'s prose ("its contents say which of the two happened") each still equated a
  green run with the file. Qualified: nothing failed *in the newest run*.
- **`plans/PLANS_STATUS.md`'s working copy** held the mid-execution row while HEAD held the final one
  (the row had been staged as a blob so the owner's uncommitted paragraph stayed out of the commit).
  Restored in the working copy; nothing to commit.

**§9 had not been run.** The #80 session of 3.27.0 was done on Kronikol's own example project, not on
the consumer, and no CI run of BreakfastProvider on the release existed. Run on 2026-09-22 - the
transcript follows. Bumping the consumer found one thing first: `Kronikol.Extensions.Grpc` pins
`Grpc.Net.Client` as `2.*`, so every release's package takes the newest at pack time, and a consumer
pinned to 2.83.0 restores 3.27.0 with `NU1605` (a downgrade is an error); the consumer bump moves it to
2.84.0, as the earlier bumps moved the earlier floors. Not this plan's, recorded for the pin's owner.

### 3.27.2 (patch) - §9 run on BreakfastProvider, and the two defects it found

**The session, on the xUnit lane in memory** (packages 3.27.0, the tool built from 3.27.1; the report
side is the same in every 3.27.x). The lane's last local run, of 2026-09-19 on 3.20.0, had 34 failures
of its own (a 500 on `POST /orders` in that environment) and, being 3.20.0's, no manifest. The lane's
`global.json` selects the Microsoft testing platform runner, so `dotnet test` forwards what it does
not know to the test host: no `--nologo`, and a filter is `-- --filter-class <type>`.

1. One scenario broken on purpose (`RecipeCost_Analysis_Tests`: a FluentAssertions line of 172
   characters whose `but found 4.99M` sits past the 80th), full run: 203 scenarios, **1 failed**. The
   3.20.0 directory on top was rotated by the manifest-less fallback to
   `runs/local_20260919T040511Z_14e947b5` (its id read from `History.run.json`), and the new
   `Failures.md` opened with `> The run before this one failed — 34 of its 203 scenarios failed, and
   that run is kept, whole, in runs/local_20260919T040511Z_14e947b5`; the console pointer said the
   same. `Run.json`: 11 files, 2 attachments, `failed: 1`.
2. The break reverted; the class re-run twice, filtered: green, 1 scenario, partial. Three runs kept
   (the 34-failure run, the 1-failure run, the first green re-run). `Failures.md`: `# No failures`
   and, under it, `The run before this one failed — 1 of its 203 scenarios failed, and that run is
   kept, whole, in runs/local_20260922T080715Z_14e947b5`; the second re-run's says only `# No
   failures`, the run before it having been green.
3. **S2:** `history <dir> --failing` on the green partial run: `this run is partial: 1 scenario, the
   last full run had 203 — a filter answers for this one only`, `no scenario is failing in this run`,
   `1 scenario here failed earlier in the window: s0 (2 runs ago, local:20260922T080715Z:14e947b5) ·
   next: history s0`. On the failing run itself `--failing` was the broke case: `no scenario has been
   failing since an earlier run (what fails in this one broke in it) · 1 scenario failed in this run
   under another verdict: s144 (broke) · next: history s144`. **S1:** `history <dir> s144` on the
   failing run, and `s0` on the green one, print the 172 characters whole under the `F` row.
4. **S3:** with `TestRunReport.json` deleted, `kronikol query history --run last-failed --failing`
   from the repository root - no report, the ledger found above the working directory - answered from
   the ledger: the failing run, `ledger only — no report read`, the broke hint with its `sid:` and
   `next: history --run <id> --sid <id>`. `failures <dir> --run last-failed` in the same state was
   refused, `No TestRunReport.json under <dir>`: **the first defect, below.**
5. **S4:** `failures <dir> --run last-failed` (the run's digest, error whole) → `interactions <dir>
   --run last-failed s144` (3 calls; `--service BigQuery` leaves `Insert
   /breakfast_analytics/table/recipe_costs` at `s144/i2`) → `http <dir> --run last-failed s144/i2`
   (the 294 B body by hash, `--body` for all of it): the ladder the issue could not walk, against the
   kept run. `diff runs/<failing> .` from the reports directory (open question 6's two-directory
   form): `Fixed (1): fixed s0 …`, `Gone (202)` - the re-run is partial. `history doctor <dir>`: `3
   runs retained, the newest failing one local_20260922T080715Z_14e947b5 (1 failed)`; its one
   `problem` is the consumer's `.gitattributes`, not the runs.
6a. **F14:** the failing run's report copied out without `History.run.json`, `history <copy>
   --regressed` → `s144 broke PPPPPPPPPF passed in local:20260915T145915Z:14e947b5, failing now`, the
   run under its true id `local:20260922T080715Z:14e947b5`, adopted from the ledger's own line.
7. **CI, on the release, no configuration change** (BreakfastProvider `d87efa38`, packages and tool
   3.27.1; run 35704113870): 17 of the 18 lanes green on the first attempt, `TUnit in memory` red on
   `NU1102: Unable to find package Kronikol.TUnit (>= 3.27.1) … Nearest version: 3.27.0` - NuGet's
   registration lag, the package having been published minutes before - and green on the rerun; the
   ledger recorded, the Pages site deployed, the whole run `success`. The `xunit-in-memory-report`
   artifact downloaded and listed: **the top level exactly as before plus `Run.json`** (`gh:35704113870:1`,
   `xunit-in-memory`, 203 scenarios, 0 failed, `3.27.1+f65e4239`) **and no `runs/` folder** - the
   assertion §9 asks for. `Grpc.Net.Client` 2.83.0 → 2.84.0 was the one change the bump needed (the
   floor above).

**Two defects, fixed here, red first:**

- **`--run` while the run on top is being written.** Step 4's state - no report on top, runs kept
  beneath - is also every minute of a long suite after the rotation and before the new report lands,
  which is the one moment the run before is most wanted. `RetainedRunResolver` needed the report on
  top to find the directory. A directory with kept runs beneath it now resolves `--run` against them;
  without `--run` the refusal is what it was.
- **`failures` cut the `history:` evidence with a bare ellipsis** (`behaviour is compare…` on the real
  run: 178 characters across a fingerprint-rule change, cut at 160). §4.2's rule had been applied to
  the history run view only. `failures` now cuts both ends and its footer names `history sN`.

**Left as found, then not (3.27.3).** The `failures` verb cut a scenario's error message at 240 with
no address, `steps sN` a failed step's at 180 and `assertions` at 180 - head-only, since 3.0.47, in no
design record; the whole was in `Failures.md` and `--json` only, which is #82's shape on the verbs §4.2
was not applied to. Asked whether that was by design, the owner had it fixed: `steps sN` is the detail
view and prints the message whole; `failures` and `assertions` cut both ends and their footer names
`steps sN` (and `history sN` for evidence). On the consumer's real failures 0 of 6 distinct messages
exceeded 240 and 1 exceeded 180; equivalency messages with a diff inside run to several hundred. The
34 failures of the consumer's earlier local run are the consumer's own.

**Seen on the way, not this plan's, fixed the same day (`b5cff009`, test code only):** the 3.27.2 CI run
failed once in `Kronikol.Tests.MongoDB` - a subscriber test read `[TestContext]` where it expected the
document owner and the flow - and passed on the rerun. Not timing: `ChangeStreamCorrelationTests` clears
the process-wide `TestCorrelationStore` in its constructor and was in no xUnit collection, so it ran in
parallel with the subscriber tests' collection. One collection per assembly for every class that clears
the store or resets the identity scope, held by `ProcessWideTrackingStateTests`, red on three assemblies
first.

### 3.28.0 (minor) - the leftovers, 2026-09-23

The audit's closing note listed what it had not done, and the owner said to deal with whatever should
be. Each item, and what was done:

- **Open question 6, `--baseline-run`: built.** `diff <reports-dir> --baseline-run ID` takes what `--run`
  takes and resolves it against the same directory, so `--run previous --baseline-run last-failed` diffs
  two kept runs; the run the report already is, a run that is not kept, `--baseline` beside it and a
  positional beside it are refused, exit 2. Four tests red first (`RetainedRunsTests`); the skill's recipe
  row and the wiki lead with the flag and keep the two-directory form. Built in a worktree on 3.27.4,
  because another session was releasing the ingest feed plan from the checkout at the time; the rule the
  two sessions agreed: whoever lands first takes 3.28.0, the other renumbers.
- **The real `--retry-failed-tests` run: repeated on the shipped 3.27.3 packages** (§6.5 item 18, §11.4
  row 3), in a scratch xunit.v3 project under the Microsoft testing platform with a fail-once test (a
  marker file: absent on attempt 1, so the test fails and writes it; present on attempt 2). The retry
  extension re-ran the one failed test in a new process. **Local defaults:** two run ids a second apart
  (`local:…T080018Z` and `…T080019Z`), the failing attempt kept whole under `runs/`, the top-level
  `Failures.md` reading `# No failures` and then `**The run before this one failed** - 1 of its 3
  scenarios failed, and that run is kept, whole, in runs/…` with the `--run last-failed` command.
  **GitHub Actions variables** (`GITHUB_RUN_ID=777`, attempt 1): both attempts `gh:777:1`, the failing
  one kept as `runs/gh_777_1` with nothing else retained, `Failures.md` reading `**An earlier attempt of
  this run failed** - … kept, whole, in runs/gh_777_1` with the path form of the command (the id names
  both attempts); `history record` on the directory: `recorded gh:777:1 RetryProbe 3 scenarios, 2
  attempts … from 2 fragment(s)`, one run line; `history --flaky` reading the scenario `flaky … passed on
  retry 2 in this run`; `history verify`: no findings. Trap: `Microsoft.Testing.Extensions.Retry` must
  sit on the platform line the SDK's MSBuild bridge uses (1.9.1 with SDK 10.0.300; 2.4.1 brings a
  platform the bridge cannot type-load, `IDataConsumer`, and zero tests run).
- **The four issues: answered and closed** (#80 once 3.28.0 was on NuGet, the others before), each
  naming the release and the shape; #81's second ask with the measured exit codes.
- **BreakfastProvider: bumped to 3.28.0**, packages and tool, one bump for 3.27.2 through 3.28.0.
- **The `2.*` pin on `Grpc.Net.Client`: not a Grpc defect but the repository's convention.** Twenty-three
  extension packages float their client dependency (`2.*`, `3.*`, `12.*`; `10.*` on net10.0 while the
  net8 and net9 pins are fixed), so each release's package takes the newest at pack time and a consumer
  pinned lower restores with NU1605. It is also what makes the extensions' CI test against the newest
  client for free. A floor per package, and a lane that tests at the floor, is a policy decision:
  recorded as an issue for the owner, not changed here.
- **The consumer's own failures, looked at with the tool and left to the consumer.** The 34 local
  failures of 2026-09-19 (in-memory xUnit lane; three clusters from one 500 on `POST /orders`) did not
  recur on the full run of 2026-09-22. The three Orders scenarios failing on every docker lane are event
  assertions timing out at 30 s (`Expected orderCreatedEvents {empty} to have an item matching …`; the
  outbox message never `Processed`), failing since the scheduled run of 2026-09-20 on a commit that had
  passed the five days before, on all five docker lanes at once: environment, not code, and the compose
  file pulls `docker.io/bitnamilegacy/kafka:latest`, a floating tag on a frozen repository. Kronikol's
  side answered every question asked of it: `history --failing --suite xunit-in-docker` from the CI
  ledger, the scenario's run rows with the stored error, and `failures` on the downloaded artifact.
