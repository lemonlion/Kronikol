# history-bench — the measurements behind plans/CROSS_RUN_HISTORY_PLAN.md

Working harnesses, untracked like `tools/query-bench` and `tools/render-bench`. Every number in
`plans/CROSS_RUN_HISTORY_PLAN.md` §2 and §6.5 came from these; `M0.1` promotes them to committed
regression guards. **Fifteen of that plan's own design decisions were reversed by what these produced** — including two that had been *added to the plan the same day* on the strength of reading the code,
so re-run them before trusting any of its defaults.

Corpora are **not** vendored (same rule as `query-bench`): the scripts point at reports that already
exist on disk.

## Python harnesses (corpus: `../../../BreakfastProvider/tests/*/bin/Debug/net10.0/Reports/TestRunReport.json`)

| Script | Answers | Result recorded in the plan |
|---|---|---|
| `measure.py` | Suite shape; URI segment classes; distinct URIs per templating policy | §2.1 — 203 scenarios, 2,693 interactions, mean 13.3 calls |
| `segs.py` | Prints real examples of each non-literal segment class | §2.4 — **killed the base64 rule**: every "opaque token" was an event/queue/table name |
| `verify.py` | Cross-project `stableId` collisions; templated-URI counts | §2.3 — **16.0% of stableIds collide across test projects** → suite scoping |
| `churn.py` / `churn2.py` | Fingerprint agreement for the same logical test under successive templating policies | §2.4 — ladder 66.9% → 77.9% (GUID-only) → 89.7% → 93.8% (intra-segment) → 96.6% (order-insensitive) |
| `ledger.py` | Builds a realistic roster-interned ledger and reports size/gzip at several windows | §2.2 — 228 KB / 36 KB gz at window 50 |

Run with any Python 3; pass report paths as arguments, e.g.

```bash
cd ../../../BreakfastProvider
python ../Kronikol/tools/history-bench/verify.py $(ls -d tests/BreakfastProvider.Tests.Component.*/bin/Debug/net10.0/Reports/TestRunReport.json)
```

## `appendtest/` — concurrent-append safety (§6.5)

`dotnet run -c Release -- <dir> <procs> <linesEach> <lock|nolock> <lineBytes>`. Spawns N copies of
itself appending to one file, then validates the result (line count, well-formedness, torn lines,
byte total, per-process completeness).

Measured 2026-09-12 on Windows/NTFS natively and Linux (Ubuntu 24.04, .NET 10.0.11) on **both**
overlayfs and an ext4 Docker volume:

- **`nolock` silently loses 34–67% of lines on every platform** (one torn line also seen on ext4).
  The realistic shape — 32 projects appending **one** line each — kept only **21 of 32**.
- **`FileShare.None` + retry is clean everywhere**: 320/320 and 32/32, at 4.3 KB and 128 KB lines.
- Under deliberate stress (16 × 200) three writers exhausted a 200-attempt budget **and said so** —
  starvation is detectable, which is what makes "keep the fragment, diagnose, fold next run" safe.

Running it on Linux without an `mcr.microsoft.com` pull: build on Windows, then mount
`bin/Release/net10.0` into any image carrying the .NET 10 runtime and run `dotnet appendtest.dll`.
**Test on the container's own filesystem, not a Windows bind mount**, or you measure the mount.

## `histbench/` — analyzer cost (§2.6)

`dotnet run -c Release -- <scenarios> <runs> <window> <extraRunsBeyondWindow>`. Generates a
roster-interned ledger, then reads (window-bounded) and computes flip/fail verdicts, warm third pass.

| Suite | Ledger | Read | Parse | Analyse | Total |
|---|---|---|---|---|---|
| 203 × 50 | 0.14 MB | 0.3 ms | 3.9 ms | 0.0 ms | **4.2 ms** |
| 1,000 × 50 | 0.67 MB | 0.9 ms | 18.4 ms | 0.1 ms | **19.4 ms** |
| 5,000 × 50 | 3.34 MB | 4.2 ms | 60.3 ms | 0.8 ms | **65.3 ms** |
| 5,000 × 250 lines, window 50 | 15.66 MB | 21.5 ms | 74.9 ms | 0.8 ms | **97.2 ms** |

Two findings beyond the budget: **parsing is window-bounded, scanning is not** (the last row parses
50 lines but scans 252), so the pinned observable is `LinesParsed` and `history prune` has a real
performance job; and the run flagged **4,265 of 5,000 scenarios as flaky** under a "≥2 flips"
rule — an absolute flip threshold does not scale with window length, so `flaky` must key on a *rate*.

## `mergetest.sh` — what git does to an append-only ledger (§5.10, §3.1)

`bash mergetest.sh [workdir]`. Five throwaway repos, no network, seconds to run.

| Scenario | Result |
|---|---|
| Two branches append, **no** `.gitattributes` | `CONFLICT (content)` — the trivial two-line one |
| Same, `merge=union` | auto-merged, both runs kept, in order |
| Union, identical line both sides | **de-duplicated** to one line |
| Union, **counter-keyed roster** | merges "cleanly" into **two rosters sharing key `r2`** — silent corruption, hence the content-hash rule |
| Union + `core.autocrlf=true` + `text eol=lf` | LF preserved in working tree **and** object store; `git rebase` behaves as `git merge` |

Scenarios 6-9 answer the question this harness originally could not — **where git reads the
attribute from**, via `git merge-tree --write-tree`, the no-worktree plumbing a server-side merge uses:

| Context | Result |
|---|---|
| normal clone, attribute checked out | **clean union merge** |
| same clone, `.gitattributes` deleted from the worktree (still committed) | **conflict** |
| **bare** repository | **conflict** |
| bare + `-c attr.tree=HEAD` (git ≥ 2.40, off by default) | **clean** |

**The attribute comes from the working tree, not the commit.** So union works for every merge with a
checkout — `merge`, `rebase`, `pull`, CI commit-back — and should be assumed **not** to work for
GitHub's merge button until a real PR proves otherwise.

## `dsl.js` — the report's search grammar, executed (§8.1)

`node dsl.js` from the repo root. Loads the shipped `src/Kronikol/Reports/advanced-search.js` and
dumps tokens and match results. It reversed two conclusions that reading the same file had produced:

- `is:new` makes `isAdvancedSearch` return **false**, so it never reaches the advanced path at all —
  it is routed to legacy free-text search, which rules out an "unknown operator" hint at parse time.
- `$flaky` tokenises and parses fine but **evaluates false for exactly the scenarios it should
  match**: `advancedSearchEvaluate` compares `status.toLowerCase() === ast.value` against a single
  execution-status string, and a flaky scenario's status is `Passed`. Verdicts need their own
  argument, not a widened `status`.

## `prune.js` — does a verdict-only query scan everything? (§8.1)

`node prune.js` from the repo root. Slices the pruner and its helpers out of the shipped
`report-search-index.js` and runs them over a synthetic 24-doc index.

| Query | `kronIsDeepEligible` | Candidates of 24 |
|---|---|---|
| `$flaky` / `@slow` / `$flaky && @slow` | **false** | 24 |
| `checkout`, `checkout && $flaky` | true | 0 |
| **`$flaky \|\| checkout`** | true | **24** |
| `ab` (under 3 chars) | false | 24 |

**This reversed a claim added to the plan hours earlier.** Reading `kronCandidateDocsForQuery` alone
says `default: // tag, status, not — never prune`, which looks like "`$flaky` scans the whole
corpus". It does not: `kronIsDeepEligible` gates entry to the deep path on there being a text or
phrase term of ≥3 code units, so a verdict-only query never gets there. The real full-scan case is a
**disjunction** mixing a non-pruning term with a text term, and it is pre-existing (`@slow ||
checkout` does it today).

Note the trap the harness itself hit: `kronCandidateDocsForQuery` returns a **docs array** built by
iterating `ix.docCount`, so an index literal missing `docCount` silently reports **zero** candidates
and looks like perfect pruning.

## `ctrfrun/` — the free chain's insights arithmetic, executed

See `ctrfrun/README.md`. Runs `github-test-reporter`'s own `enrichReportWithInsights` verbatim, and
is what narrowed §0.2 from "the status half is commodity" to "flakiness is commodity **only for
suites that retry**".

## `gitgrowth.py` — what a committed ledger costs a repository (§5.10)

`python gitgrowth.py [builds] [window]`. Commits one run per build against a 203-scenario ledger,
`gc`s, and measures `.git` for four designs. Slow — 400 builds is a few minutes per design.

| Design | Working | `.git` loose | **packed** | per build |
|---|---|---|---|---|
| JSONL, window 50 | 180 KB | 11.9 MB | **0.69 MB** | 1.8 KB |
| JSONL, unbounded | 1.4 MB | 45.2 MB | **0.45 MB** | 1.1 KB |
| Document, window 50 | 365 KB | 15.2 MB | 0.89 MB | 2.3 KB |
| Document, unbounded | 2.8 MB | 59.1 MB | 0.49 MB | 1.2 KB |

Two results contradict what the plan assumed: **pruning to a window makes the repo 55% bigger**
(dropping the oldest line breaks git's delta chains, where a pure append deltas almost perfectly),
and **JSONL's storage advantage over a rewritten document is only 8–21%** — so JSONL is right for
its `SIGKILL` and merge behaviour, not for size. The absolute figure, 1.8 KB packed per build
(~6.5 MB a year at ten builds a day), is what answers "will a team accept this in their repo".

## `hbs/` — rendering the reporter's flaky table

See `hbs/README.md`. Established that **the free chain's own retry detection does not fill its own
Flaky Tests table** — a `retries: 2` test renders "No flaky tests in this run", while a producer-set
`flaky` renders the row.

## `volatile.py` — is any scenario identity run-varying? (§2.9)

`python volatile.py <TestRunReport.json> [...]`. `stableId` hashes every example value, so a scenario
parameterised by a timestamp or GUID mints a new id every run and can never accrue history. Scans for
five run-varying shapes (GUID, hex-32, ISO timestamp, epoch-ms, long digit runs).

> **1,195 scenarios across the six BreakfastProvider suites; 325 parameterised (27.2%); zero
> run-varying values.** A real hazard with no instances in a large real corpus.

Note what this script does **not** answer: pointing it at six *different* suites measures cross-suite
overlap, not volatility — that run just reproduces §2.3's 145 shared ids. Volatility is about one
suite over repeated runs, which is why the check is static, on example values, instead.

## `jsd/` — the report's export function, executed

See `jsd/README.md`. Confirms a body-level `#history-data` script rides along automatically, and that
a **nested** one silently does not.

## `pairwise.py` — is the residual fingerprint disagreement drift, or harness difference? (§2.10)

`python pairwise.py <TestRunReport.json>...`. Pairwise agreement matrix, plus the decisive test:
**do the same scenarios disagree in every pair, and do they differ in call count?**

Over the six BreakfastProvider reports — of which **only NUnit, TUnit and xUnit share any
`stableId`** — `shapeSet` agreement is 96.6–98.6%, and exactly **five** scenarios disagree. Four
differ in raw call count (6 vs 2, 2 vs 6, 18 vs 20); the fifth is fixture data
(`…/ingredient/Sugar-{id}` vs `Flour-{id}`). **None is drift**, which closes the gate §2.4 left open
without needing a docker re-run.

## `allure/` — Allure's own `historyId`, executed

`node --experimental-transform-types drive.ts`. `historyid.ts` is `md5` + `getTestResultHistoryId`
lifted verbatim from `allure-js-commons/src/sdk/reporter/utils.ts`. Confirms parameters are sorted,
that `excluded: true` neutralises a parameter **even when its value changes**, that an explicit
`historyId` is returned verbatim — and that **Allure is rename-fragile too** without that override.

Two further Allure findings came from reading its source rather than running it, and both are in the
plan: `allure3/packages/core/src/history.ts`'s `appendHistory` **is not an append** — it writes from
`start: 0`, copies the tail over the file and truncates, so Allure is *not* precedent for the
crash-safety argument in plan §3.1; and `allure2` `HistoryPlugin.java` holds both the literal
`.limit(20)` retention and a **5-run bounded-lookback flakiness rule** that is neither a count nor a
rate (plan §7.2).

## `readtest/` — what a READER sees during a write (§3.4)

`dotnet run -c Release -- <path> <lines> <writerShare> <readerShare>`. §6.5 covered writer-vs-writer;
this covers the case several test projects in one repo hit, since `dotnet test` runs projects in
parallel and they share one ledger.

| Writer | Reader | Reads | **Denied** | Torn tail |
|---|---|---|---|---|
| `FileShare.None` | `None` | 53,963 | **5,015 (9.3%)** | 0 |
| `FileShare.None` | `Read` | 47,551 | **4,742** | 0 |
| `FileShare.None` | `ReadWrite` | 131,117 | **85,199** | 0 |
| `FileShare.ReadWrite` | `ReadWrite` | 1,850 | 0 | 0 |

**The reader needs its own retry budget** — it is locked out about a tenth of the time, and the only
zero-denial combination is the unlocked mode §6.5 proved loses a third of all lines. Also: **zero
torn tails in ~380,000 reads**, so tail-skipping is belt-and-braces, not the primary defence.

## Not yet measured

**Nothing blocking.** The same-framework churn repeat on a volatile-id suite is now confirmatory
rather than load-bearing (`pairwise.py` closed its question from existing data). (The repo-root
template sweep that used to sit here is **done** — all twelve templates set no folder path and are therefore one layout; see plan
§2.8.) BreakfastProvider's component tests run with `EnableDockerInSetupAndTearDown: false` and
expect **eight docker-compose stacks** up already, so what remains is a scripted environment
bring-up, not a `dotnet test`.
