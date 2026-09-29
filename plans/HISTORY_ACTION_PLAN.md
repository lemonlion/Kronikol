# History action plan: cross-run history on CI without copied YAML (roadmap 2.3)

**Date:** 2026-09-27, second pass 2026-09-28 · **Repo version:** 3.31.9 (`main` at `dc619d35`); the
second pass re-read against 3.31.10 (`main` at `5ca0878a`), which changed no history code · **Status:
EXECUTED 2026-09-29, but for S5's consumer switch** (D26 taken as §10 recommends; the log is §14). Roadmap item **2.3**,
stage 2, track C. It comes after 2.2 (PR #73), whose branch it starts from, and before 2.4 (Azure
DevOps), which builds to the interface in §4.2. It changes no package, so it bumps nothing (§7). The
scripts behind every RUN mark are in [`HISTORY_ACTION_PLAN.harness/`](HISTORY_ACTION_PLAN.harness/README.md),
with their output.

Roadmap 2.3 asks for "a history composite action in 2.2's directory, shape and test pattern", because
"today cross-run history on CI costs about 45 lines of copied YAML (orphan branch, worktrees, fetch and
rebase retry). The action makes it three". The store plan puts it ahead of alerting
(`HISTORY_DASHBOARD_STORE_PLAN.md` §6.1, §11 Q7). Checking the premise found more than the item says:

- **The documented recipe does not run.** The wiki's "recommended shape" fails on a new repository's
  first run. It also fails on every run after that, because the fold job never fetches the branch it
  adds a worktree from. With that fixed, it fails on the committer identity (F1). The copy that works
  is Kronikol's own dogfood, at 82 lines. The one consumer's copy has grown to about 167 lines across
  three files, and it has fixes neither of the others has (F3).
- **The working copies lose runs.** Six fold jobs finishing together, ten times over: the dogfood's
  loop lost 9 of 60 runs when the branch existed, and 10 of 60 on a first run, where it also wrote 80
  duplicate roster lines. The loop this plan proposes records again on the branch as it now is, instead
  of rebasing. In the same races it lost none and duplicated nothing (F4).
- **The read step drops the quarantine.** `quarantine.json` and `aliases.json` are read from beside the
  ledger. The recipe copies the ledger alone, so on CI a quarantined failure is not quarantined (F2).
- **It is three lines only for a workflow with one test job.** A composite action cannot run after the
  caller's test step, because it has no post steps. The fold must also run in a job after every shard.
  So the general shape is three calls in two jobs: about a dozen lines, against 82 and 167 (§4.2).

**The second pass (2026-09-28)** ran what the first had only inferred. It ran the fold over smart HTTP
with a token, as github.com takes one. The origin was git's own `http-backend` behind basic
authentication on loopback, where every earlier RUN had used a `file://` origin with no authentication
at all. It changed four things:

- **One number was wrong.** A year of the consumer's ledger makes a fold of eighteen suites take 30 s and
  1 GB of memory, not the 110 s the first pass extrapolated from a 0.8 MB range (F6, `s8`).
- **Two findings are now RUN.** Under `actions/checkout` v6.0.0 the dogfood's recipe fails at its
  worktree push on every run (F9, `s6` H7). A re-run of a failed job does pass the gate on the failure
  its first attempt recorded (F10, `s7`).
- **Two findings are new, and both change the scripts.** A wrong token hands the machine's credential
  helpers the host, and git then erases the credential they stored (F15). A consumer who copies the
  folder onto a Windows runner gets scripts bash cannot run (F16).
- **The read step changed.** It now fetches only the files it reads: 31 KB where a depth-1 fetch took
  3.4 MB with a large file beside the ledger. Q3 gains a read window: a pruned copy of the ledger
  committed beside it. It reads identically, is 17.6 times smaller to fetch at a year's size, and costs
  about 1 KB a run of pack (F7, `s9`).

Prior art agrees with §4.5. `github-action-benchmark` stopped rebasing at v1.6.0 because rebases
conflicted, and now re-applies its change on the new tip. None of the seven actions surveyed tells a race
from a refusal by the remote tip: they match `[remote rejected]`, which a ruleset refusal prints too, or
retry on any error (§4.5).

---

## 0. Summary

**What a consumer writes** (non-blank, non-comment lines of workflow YAML):

| | Lines | First run | Companions | Gate | On a lost race | Tool version |
|---|---|---|---|---|---|---|
| The wiki's recipe (`Cross-Run-History`, "The recommended shape") | 21, in two blocks | **fails** (F1) | not read | none | one rebase | not installed |
| Kronikol's dogfood (`ci-summary-preview.yml`) | 82: fetch 9, upload 7, fold job 66 | works | not read | none | five rebases, no pause | built from source |
| BreakfastProvider (`_tests.yml`, `_tests-tunit.yml`, `ci-main.yml`) | about 167: 36, 36 and 95, of which the fold job's `needs` list is 18 | works | read | yes, with a step for the first run | five rebases, no pause | 3.31.4, written in three places |
| **The action, two jobs** (§4.2) | **11**, or 18 with the gate | works | read | optional phase | records again, up to 8 attempts, jittered | one file in the action, held to the release |
| **The action, one job** | **7** | works | read | optional | the same | the same |

**Findings** (§3). F1: the documented recipe fails on every run of a new repository. F2: its read step
drops the quarantine and the aliases. F3: three copies behave three ways. F4: the retry loops lose runs
and duplicate rosters. F5: a concurrency group with the default queue would cancel folds, and that also
applies to 2.2's README. F6: `kronikol history record` reads the ledger twice per suite: 4.6 s for
eighteen suites today, 30 s and 1 GB at a year's size. F7: the ledger's tip grows by about 0.5 MB a day
at the consumer, and every lane fetches it and every test run reads it. F8: the dogfood's artifact naming
would collide under reusable workflows, and a reports directory in a hidden folder uploads nothing. F9:
the recipes depend on how `actions/checkout` stores credentials; under v6.0.0 the dogfood's worktree push
fails. F10: a re-run reads its own first attempt as history, and passes the gate on it. F11: the gate
exits 2 when there is no ledger yet. F12: an action cannot learn its own release. F13: referencing a
folder of this repository downloads the whole repository, 17.5 MB, per job; 64% of it is one jar. F14: a
floating major tag would run a release. F15: a wrong token hands the machine's credential helpers the
host, and git erases what they store. F16: the folder copied onto a Windows runner arrives with CRLF
scripts, which bash cannot run.

**The design** (§4). Four composite actions in one folder,
`templates/github-actions/kronikol-history/`: **read** (before the tests), **gate** (after them,
optional), **save** (the fragments as an artifact) and **record** (in a job after every test job). Each
keeps its own git repository under `RUNNER_TEMP` and authenticates with a `token` input, so none depends
on the job's checkout. Its git keeps the machine's configuration but sets five things of its own: no
prompt, no credential helper, no line-ending conversion, no machine hook, and the token as a header
passed through the environment (§4.9). Read fetches only the three files it reads. Record never rebases
and never force-pushes. A lost race is recorded again on the new tip. A refusal while the branch stands
still is reported at once, not retried. The tool is installed to a path of its own, at the version the
folder ships with. The logic lives in bash scripts written to run unchanged on Linux, Windows and macOS,
so 2.4 can carry the same code.

**Slices** (§6). S0 makes the wiki's recipe true, with no code, and can happen now. S1 is the shared
test scaffolding, on 2.2's rebased branch. S2 is read, save and record. S3 is the gate. S3b runs the
action on GitHub in a live lane, on its own pull request, before anything merges. S4 switches the dogfood
to the action and adds the Windows and macOS legs. S5 is the documentation and the consumer's switch.
S6, a read window, only if Q3 takes it. **No bump at any slice.** A consumer can reference the action
from the first release tag that contains it.

---

## 1. How far each claim was checked

Marks as in `ROADMAP.md`: **RUN** (a command was executed for this plan), **READ** (the source was read
for it), **VERIFIED** (a primary source was read on 2026-09-27, in either pass: docs.github.com, the
runner, the `actions/*` repositories, the runner-images scripts and readmes, the Git for Windows
installer, and the source of seven actions that keep data on a branch; read through the web by
sub-agents of this session and quoted in Appendix B), **PARTIAL** (the primary source leaves part of it
open), **INFERRED** (reasoned from two facts, stated by neither).

**RUN means the harness.** Its unit is a bare "origin" on disk, with fresh checkouts made the way
`actions/checkout@v5` makes them. Git runs with no global or system configuration, because a hosted
runner sets no committer identity. The second pass adds a machine's configuration on purpose, in `s6`
H1 and `s10`. From `s6` on, the origin can also be served over smart HTTP: git's own `http-backend`
behind basic authentication (`githttp.py`), taking `x-access-token` and a token as github.com does,
with a read-only token whose push is answered 403. The tool is built from this repository at 3.31.9 (SDK
10.0.401, Release, framework-dependent); 3.31.10 changed no history code. Fragments are built from
BreakfastProvider's real ledger lines, its `kronikol-history` branch at `39bd177`: 664 lines, 18 suites.
Larger ledgers are that one grown with copies of its own run lines (`grow_ledger.py`). The machine is a
Linux container with 4 cores and git 2.43.0. **Not run:** a GitHub-hosted runner, Windows, macOS,
github.com itself (the HTTP origin is on loopback), `actions/upload-artifact` and
`actions/download-artifact` themselves.

**READ** covers the wiki at `Cross-Run-History.md` as of 2026-09-27, both workflows of this repository,
BreakfastProvider's workflows at `9f242d4` (2026-09-27), PR #73's branch at `e31b42fa`, and the source
lines cited by path.

---

## 2. What history on CI is today

### 2.1 The mechanism (READ)

A run on CI does not append to a ledger it only found (`WriteHistoryLedger = null`, the default). It
writes `History.run.json` beside its report instead. A step after every test job folds those fragments
into the ledger with `kronikol history record`. The fragments of one workflow run share a run id,
`gh:<run id>:<attempt>` (`HistoryRunBuilder.cs:174`), and fold into one line per suite. A run already in
the ledger is a duplicate and is skipped, so recording again is safe (`HistoryCommand.cs:249-374`).

For the run's own outputs to carry verdicts, meaning `Failures.md`, the CTRF document, the report's
sparklines and the job summary, the ledger has to be read **before** the tests. That is done by fetching
it and naming it in `KRONIKOL_HISTORY`. A ledger named that way is also appended to by the run, which
is harmless, because the copy is discarded. It also does no harm to the gate, because a run is read
against the lines before its own line (`HistoryLedger.cs:123`).

The recommended home is an **orphan data branch**, `kronikol-history`. It shares no ancestor with
`main`, it stays out of every pull request's diff, and it is written by one job.

### 2.2 The three copies (READ)

| | Wiki (lines 95-151) | Dogfood (`ci-summary-preview.yml:49-148`) | Consumer (BreakfastProvider `9f242d4`) |
|---|---|---|---|
| Fetch before the tests | `history.jsonl` only | `history.jsonl` only | `history.jsonl`, `quarantine.json`, `aliases.json`, in both lane workflows |
| Fragment upload | `history-${{ strategy.job-index }}`, 90 days | the same, `if-no-files-found: warn` | none of its own: the fragments ride in each lane's `*-report` artifact |
| Fold job: fetch of the branch | **none** (F1) | `git fetch origin kronikol-history` | the same |
| First run | a comment: "or --orphan on the first run" | orphan worktree, README, `.gitattributes` | the same |
| Committer identity | **none** (F1) | `github-actions[bot]` | the same |
| On a lost race | one fetch, rebase, push | five fetch, rebase, push rounds, no pause | the same |
| Tool | not installed | `dotnet run --project src/Kronikol.Tool` | `dotnet tool install --global Kronikol.Tool --version 3.31.4`, three times |
| Gate | mentioned in prose | none | every component lane: the test step `continue-on-error`, `kronikol history gate --min-runs 3`, its output in the summary, and a second step failing the job when there is no ledger yet |
| Push credentials | the checkout's | the checkout's (`checkout@v5`) | the checkout's, with a PAT preferred when the secret is set |

The consumer's fold job `needs` all eighteen component lanes, runs `if: always()`, and prints `history
show` into the summary. Kronikol's dogfood branch held 319 lines (626 KB, four suites) on 2026-09-27. The
consumer's held 664 lines (7.1 MB, eighteen suites).

### 2.3 What 2.2 gives 2.3 (READ)

PR #73 (892 lines over six files) adds `templates/github-actions/kronikol-pr-report-link/`, a composite
action with one `actions/github-script` step, its README and its tests. `PrReportLinkActionTests` (597
lines, 17 facts) reads `action.yml` with YamlDotNet into an `ActionDefinition`: the inputs, their
defaults, and the step's `env` wiring. It runs the script under node against a GitHub held in memory, and
holds the README's example workflow to the declared inputs, a concurrency group and two permissions.

What carries over to a git fold is the YAML half: `ActionDefinition`, the wiring fact and the README
drift fact, about 150 of the 597 lines. The node driver imitates `github-script`, which a git fold does
not use. `ROADMAP.md` §5 says the same as INFERRED. The harness confirms it: every behaviour worth
testing here needs real repositories.

The PR is open and conflicting. The roadmap's recheck found only `CHANGELOG.md` in conflict. The roadmap
orders it first: 2.3 branches from 2.2 once 2.2 is rebased and extracts the shared scaffolding once (§5
there).

2.2 has its own plan now: `PR_REPORT_LINK_PLAN.md`, written the same day by another session, whose
decision is D25. It agrees on all of this: the directory, the order, and only the YAML half carrying
over (its §7). It also hands 2.3 one thing this plan lacked, a **live lane** like its S3. That is a
workflow that runs the action on GitHub on the pull requests that change it, pushing to a scratch branch
and never to `kronikol-history`. This plan takes it as S3b (§6).

### 2.4 The platform (VERIFIED unless marked; sources in Appendix B)

| Fact | What it decides |
|---|---|
| A composite action's `runs` takes only `using` and `steps`. There are no `pre` or `post` steps (runner issue #1478, open, labelled "Future") | Phases are separate actions the caller places (§4.1), as `github/codeql-action` does with `init` and `analyze` |
| Composite steps see `github`, `inputs`, `strategy`, `matrix`, `steps`, `job`, `runner` and `env`, but not `secrets`, `vars` or `needs`. `github.job` and `strategy.*` are the calling job's | A token arrives as an input |
| `github.action_ref` is empty in `run:` and set only in a step's `env:`. A caller that pins a commit gets the commit | The action cannot learn its release (F12) |
| An artifact name must be unique in a run, and a duplicate upload fails with 409. `overwrite: true` replaces it. A re-run attempt uploading the same name is undocumented (PARTIAL: both copies kept in one report, a 409 in others) | Names carry the attempt and a random part (F8) |
| Upload keeps the tree below the directory before the first wildcard. Hidden files and folders are left out unless `include-hidden-files: true` | Fragments keep their reports directory. `.logs/` needs the flag (F8) |
| Download with `pattern` and no `merge-multiple`: two or more matches land in `path/<name>/`, and exactly one lands flat in `path` (issue #426, open). A re-run lists earlier attempts' artifacts too, keeping the newest per name, with no attempt filter (PARTIAL: regressions #486, July 2026, and #585) | Record reads a tree, whichever layout arrives |
| Concurrency groups are repository-wide across workflows. By default a group holds one running and one pending job, and a new pending job cancels the older one. `queue: max` (2026-05-07) keeps up to 100 pending, first in, first out | F5 |
| A push made with `GITHUB_TOKEN` starts no workflow. A ruleset can refuse a branch's creation or update to a token without bypass (PARTIAL: uncommon, but it happens in enterprises). Writing needs `contents: write`, and new personal repositories default to read. A fork's pull request gets a read-only token | Record needs `contents: write` and stops on a refusal (§4.5) |
| `pull_request_target` gives a fork's pull request a write token. It is blocked by default in public repositories from 2026-11-02 | Record on pull requests is opt-in (Q2) |
| Marketplace: exactly one `action.yml` at a repository's root. Actions in subfolders work but are not listed. Workflow files have been allowed since 2026-05-25 | 13.2 (§9) |
| Hosted images: ubuntu-latest (24.04, moving to 26.04 between October and November 2026), windows-latest (Server 2025) and macos-latest (26, arm64) all carry the .NET 10 SDK and git 2.55. `shell: bash` on Windows is Git for Windows' bash, and macOS bash is **3.2.57** | §4.7, §4.9 |
| `actions/checkout` v6.0.0 writes the header into `$RUNNER_TEMP/git-credentials-<uuid>.config` and includes it with `includeIf.gitdir:<workspace>/.git.path`. v6.0.1 (PR #2327) adds `includeIf.gitdir:<workspace>/.git/worktrees/*.path`. v7 (latest v7.0.1, 2026-07-20) keeps that and refuses a fork's code under `pull_request_target` and `workflow_run` | F9 |
| Checkout runs its git with `GIT_TERMINAL_PROMPT=0` and `GCM_INTERACTIVE=Never`. It writes a placeholder header with `git config` and swaps the real one into the file, "to avoid the credential being captured by process creation audit events" | §4.8, §4.9 |
| The Windows image installs Git for Windows with no `CRLFOption` and no credential-manager option, so the installer's defaults apply: `core.autocrlf=true` and `credential.helper=manager` in the system configuration (PARTIAL: from the installer's source and the image's script; the image's own `gitconfig` was not read). The image sets `GCM_INTERACTIVE=Never` machine-wide; a self-hosted machine need not | F15, F16 |
| `upload-artifact` v7.0.1 (2026-04-10) and `download-artifact` v8.0.1 (2026-03-11) run on node24 (runner 2.327.1 or later) and are not supported on GHES. Node 20 left the runners on 2026-09-23. A job can create at most 500 artifacts; no count per run is documented | §4.8 |
| github.com serves blobless fetches (`--filter=blob:none`), and `actions/checkout` itself combines `--filter=blob:none` with `--depth=1` against it. A server may refuse a filter and send everything | §4.3 |
| The runner downloads an action's whole repository per job, as a tarball (a zipball on Windows) into `_actions/<owner>/<repo>/<ref>/`. The only cache across jobs holds popular first-party actions. GitHub's archives are `git archive` output, so `export-ignore` shortens them (PARTIAL: documented for source archives, confirmed for the runner by others' reports) | F13, Q9 |
| `sleep 0.3` works in Git for Windows' bash (it ships GNU coreutils) and on macOS (`strtod`) | §4.5's pause |
| `git ls-remote --exit-code` exits 2 when no ref matches (RUN too: 2 absent, 0 present, 128 unreachable, `results-s5-edges.txt`; over HTTP 128 for a 401, a 404 and a server down, `results-s6-http.txt` H2) | The only way to a new branch (§4.5) |

### 2.5 What a fold costs on the consumer's ledger (RUN, `results-s4-cost.txt`, `results-s8-scaling.txt`)

The consumer's ledger at `39bd177` is 7,112,729 bytes: 1 header, 18 rosters (586,639 bytes, about 32 KB
each), 17 shapes lines, and 628 run lines (6,275,481 bytes, about 10 KB each). It holds 37 CI runs from
2026-09-14 to 2026-09-27, 12.7 days, which is 49 run lines a day. gzip -9 takes it to 1,005,838 bytes.

| Measured | |
|---|---|
| `kronikol history record` of one run of 18 lanes, five in a row | 4.78, 4.88, 5.04, 5.21 and 5.24 s as the ledger grew from 7,309,790 to 8,098,034 bytes |
| What one run of 18 lanes adds to the tip | 197,061 bytes |
| What one run adds to the branch's packed objects | 28 KB (1,032 to 1,172 KB over five runs, after `git gc`) |
| `kronikol history show` on the 8.1 MB ledger | 0.51 s |
| The fragments of one run of 18 lanes | 18 files, 40 to 66 KB each, 1,022,295 bytes in all |

**As the ledger grows** (`s8`). The same ledger, grown with copies of its own run lines to the sizes
below; 180 MB is about a year at the consumer's rate. Each cell is the wall time and the peak resident
memory. Each `record` folds one run into a fresh copy. `gate` reads one suite's report, with the reader
every test run uses at its end.

| Ledger (gzip -6) | record, 1 suite | record, 6 suites | record, 18 suites | gate | show |
|---|---|---|---|---|---|
| 7.1 MB (1.0 MB) | 0.58 s, 117 MB | 2.30 s, 148 MB | 4.59 s, 168 MB | 0.56 s, 107 MB | 0.39 s, 97 MB |
| 25 MB (3.4 MB) | 1.85 s, 282 MB | 3.24 s, 294 MB | 7.54 s, 293 MB | 0.81 s, 155 MB | 0.64 s, 146 MB |
| 50 MB (6.7 MB) | 1.92 s, 347 MB | 5.59 s, 434 MB | 10.87 s, 438 MB | 0.87 s, 201 MB | 0.77 s, 191 MB |
| 100 MB (13.2 MB) | 3.58 s, 564 MB | 6.11 s, 639 MB | 17.77 s, 654 MB | 1.09 s, 297 MB | 0.88 s, 287 MB |
| 180 MB (23.8 MB) | 5.43 s, 997 MB | 12.65 s, 1,087 MB | 30.45 s, 1,021 MB | 1.41 s, 454 MB | 1.01 s, 441 MB |

The first pass's slope, about 0.58 s per MB, came from five folds over a 0.8 MB range. Across 7 to 180
MB, eighteen suites cost 0.15 s per MB.

---

## 3. Findings

**F1. The documented recipe fails on every run of a new repository** (RUN, `results-s1-wiki-recipe.txt`).
The wiki's fold block, verbatim, in a fresh checkout under `bash -e`:

| | What happens |
|---|---|
| W1, the first run, no branch yet | exit 128, `fatal: invalid reference: origin/kronikol-history` |
| W2, the branch exists (made by the dogfood's recipe) | exit 128, the same: the fold job's checkout never fetched the branch |
| W3, with the missing `git fetch origin kronikol-history` added | exit 128, `Author identity unknown` |
| W4, with an identity added as well | exit 0 for one writer |

A branch made by hand, as the recipe's comment suggests, has no `.gitattributes`, so it has no
`merge=union`. The recipe's one rebase then conflicts on every race (RUN, `results-s5-edges.txt` E1: two
writers, `CONFLICT (content): Merge conflict in history.jsonl`, one run lost). The recipe installs no
tool, and Tier 0's step 2 does that separately. The store plan counted this recipe as "about 45 lines";
the wiki's two blocks hold 21 non-blank lines today.

This is roadmap section 0's "no document that states a falsehood": **S0 fixes the wiki now,
independently of the action.**

**F2. The read step drops the quarantine and the aliases** (RUN, `results-s3-companions.txt`; READ).
`HistoryQuarantine.PathBeside` and `HistoryAliases.PathBeside` (`HistoryQuarantine.cs:49`, `:155`) put
both companions beside the ledger. The gate and the run read them from there. A read step that copies
`history.jsonl` alone into `$RUNNER_TEMP` leaves them behind.

In the harness, a scenario quarantined on the data branch reads "nothing is quarantined" through the
wiki's and the dogfood's read step. It reads as quarantined through the consumer's, which copies all
three files. Two things follow on CI:

- A quarantined failure trips the gate and carries no `quarantined` verdict in the run's own report.
- A renamed scenario reads as one deletion plus one `new`.

The wiki does not say where the companions live when the ledger is on a data branch. They can only live
beside it, on the branch (Q7).

**F3. Three copies, three behaviours** (READ, §2.2). None of the three is the whole of the others.

- The wiki is the only one people are told to copy, and it does not run.
- The dogfood runs, but reads no companions, has no gate and builds the tool from source.
- The consumer's is the most complete. It has gained the companions, a gate with a first-run fallback,
  and a tool pin written three times. Nobody else has those.

That drift is the argument for one maintained implementation, more than the line count is.

**F4. The retry loops lose runs, and a first-run race duplicates rosters** (RUN, `results-s2-race.txt`,
`results-s2b-race-trials.txt`). Six fold jobs of six workflow runs start together, each with its own
checkout and three suites' fragments. Each row below is ten such races, sixty runs:

| Loop | Branch exists: runs lost | First run: runs lost | First run: duplicate roster lines |
|---|---|---|---|
| Dogfood as written (five rounds of fetch, rebase and push, no pause) | 9 | 10 | 80 |
| Dogfood with the prototype's budget (eight rounds, a pause of 0.1 to 0.9 s) | 0 | 0 | 100 |
| **Prototype** (record again on the tip, eight attempts, the same pause) | **0** | **0** | **0** |

In one race each, the wiki's recipe with W4's additions lost 3 of 6 runs, since it has one retry.

- **The losses come from the budget.** With eight rounds and a pause, rebasing lost nothing either.
- **The duplicates come from rebasing.** On a first run every writer makes its own orphan root. The loser
  replays its root onto the winner's, and `merge=union` keeps both copies of every roster and shapes
  line. `history verify` passes, because the reader tolerates them, but each of the consumer's roster
  lines is about 32 KB.
- **Recording again on the tip loses nothing and duplicates nothing.** It also recomputes whatever
  `record` derives from the ledger: the partial heuristic ("against the ledger as it stands",
  `HistoryCommand.cs:343`), the rename suggestions, and later 9.4's view. It does all of that
  against the ledger the line actually lands in.

**F5. A concurrency group with the default queue would cancel folds** (VERIFIED, §2.4). One job runs and
one waits. A third arrival cancels the waiting one. A fold is the only writer of its run's line, so a
cancelled fold leaves its run out of the ledger unless somebody notices and re-runs that job. **So the
action needs no group, since its loop is the lock, and its README shows none.** A consumer who adds one
needs `queue: max`, and the newest actionlint (1.7.12) rejects that key (`PR_REPORT_LINK_PLAN.md` F16).

The same rule applies to 2.2. #73's README says "`cancel-in-progress` stays off, so the second job waits
instead of being cancelled". That is true for two lanes finishing together and false for three: the
third cancels the second, and the second lane's line is lost. 2.2's own plan, written the same day by
another session, found this independently (`PR_REPORT_LINK_PLAN.md` F1) and adds `queue: max` to the
README's group (§9).

**F6. `kronikol history record` reads the whole ledger twice per suite** (READ; RUN, `s8`).
`LastFullRoster` reads the ledger once per folded run (`HistoryCommand.cs:304`).
`HistoryLedgerWriter.AppendLocked` reads it again to look for a duplicate (`HistoryLedgerWriter.cs:201`).

- Eighteen suites on 7.1 MB take 4.6 s. At 180 MB they take 30 s, with a peak of 1 GB of memory (§2.5).
  180 MB is about a year at the consumer's rate of 49 run lines, about 0.5 MB, a day (the rate INFERRED
  to hold). The first pass's 110 s was an extrapolation from a 0.8 MB range; this is measured.
- The cost is suites times size, as two reads per suite predict. Each suite past the first adds 0.24 s at
  7 MB and 1.5 s at 180 MB.
- The loop in §4.5 pays a whole `record` again on every lost race. A longer `record` also widens the
  window in which a race is lost. `github-action-benchmark` made its clone shallow for that reason: full
  clones "widen the window for push contention with other runs".

The fix belongs to the tool: read once per invocation and look for duplicates in that one pass. Eighteen
suites should then cost about what one costs today, 5.4 s at 180 MB (INFERRED from the one-suite
column). That is a patch in track A, not part of 2.3 (§9). It is recorded here because §4.5's choice
multiplies it.

**F7. The tip grows without bound; every lane fetches all of it, and every test run reads all of it**
(RUN, `s6` H6, `s8`, `s9`; READ). The consumer's ledger went from nothing to 7.1 MB in 12.7 days (1.0 MB
gzipped). Git packs the history well: 28 KB a run. But every lane's depth-1 fetch, the reading every
test run does at its end, and every fold take the whole tip.

- **At a year** (the ledger grown to 180 MB): 23.8 MB gzipped for every lane to fetch. Each test run's
  reading takes 1.4 s and 454 MB of memory, against 0.56 s and 107 MB today; the gate uses the same
  reader, so it is the measure. `HistoryWindow` bounds what a run parses, not what it scans or fetches.
- **Anything else on the tip is fetched too.** With a 25 MB file beside a 160 KB ledger, as 9.4's view
  files would sit, a depth-1 fetch took 3.4 MB to read the ledger. A blobless fetch took 31.5 KB. So read
  fetches blobless (§4.3).
- **Pruning** (`kronikol history prune`) drops the oldest runs from the tip. They survive only in the
  branch's git history, which no reader reads. It also makes the pack larger, not smaller: the wiki
  measured 55% larger over 400 commits.
- **A read window keeps every line and bounds what readers fetch** (`s9`). A reader looks back 50 runs by
  default. Record would prune a copy of the ledger to the window and commit it beside the ledger as
  `history.window.jsonl`. Read would fetch that instead:

| At 180 MB | The ledger | The window (`prune --window 50` of a copy) |
|---|---|---|
| Size (gzip -6) | 180 MB (23.8 MB) | 10.1 MB (1.35 MB) |
| The gate on one suite's failing report | 1.41 s, 451 MB | 0.79 s, 123 MB |
| The gate's output | | identical, but for the ledger's path |

Committing the window beside the ledger cost the pack 30 KB a run, against 29 KB without it. That was
measured over ten runs on a 25 MB ledger, where every run's window drops each suite's oldest line: git
deltas the window against the ledger. The window has two prices:

- `prune` at record time costs 12.4 s and 1.6 GB at 180 MB, until the tool writes the window in
  record's own pass.
- `prune` keeps the last N runs of each **suite** across streams (`HistoryLedgerWriter.cs:315-318`), and
  a reader looks back N runs of its own **stream**. So a ledger with several streams needs a per-stream
  window first.

Both are the tool's (§9). Retention is Q3, and the window is its option (d). 9.4's P1 is where years of
trends are meant to live: monthly view files on this same branch.

**F8. The dogfood's artifact naming collides under reusable workflows, and hidden folders upload
nothing** (VERIFIED; READ).

- **Collisions.** An artifact name must be unique in a run. The consumer's eighteen lanes are eighteen
  calls of one reusable workflow, each with the same job id and no matrix. A name built from
  `github.job` and `strategy.job-index`, as the dogfood's `history-${{ strategy.job-index }}` is, would
  be the same in every lane: the first upload succeeds and seventeen fail with 409. The consumer
  escapes only because its fragments ride in its own `<lane>-report` artifacts.
- **Hidden folders.** `upload-artifact` leaves out hidden folders by default. A reports directory under
  `.logs/kronikol/`, a location this repository's own `CLAUDE.md` names, would upload no fragment.
- **Staged rotations.** A staged rotation under `runs/.incoming-<name>/` must never be uploaded, since
  every reader of `runs/` ignores it.

**F9. The recipes depend on how `actions/checkout` stores credentials, and under v6.0.0 the dogfood's
fails** (VERIFIED; RUN, `s6` H7). Both working copies push from a `git worktree` of the job's checkout.
Checkout v6.0.0 wrote its header into a file under `$RUNNER_TEMP`, included with
`includeIf.gitdir:<workspace>/.git`. A worktree's git directory is `<workspace>/.git/worktrees/<name>`,
which that pattern does not match. v6.0.1 added the worktrees pattern (§2.4). The table shows the
dogfood's fold step against an HTTP origin that demands the token, in checkouts made as each version
makes them:

| Checkout | First run | The branch exists |
|---|---|---|
| v5 (the header in `.git/config`) | pushed | pushed |
| v6.0.0 | exit 128 at the worktree push: `could not read Username ...: terminal prompts disabled` | the same |
| v6.0.1 and later | pushed | pushed |

The dogfood's loop also took the refusal for a lost race: it fetched to rebase, and failed again. The
dogfood and the consumer pin `checkout@v5`, so neither fails today. Moving to v6.0.0 would have stopped
both. An action that keeps its own repository and sets its own authorization depends on none of this:

- the checkout's version,
- `persist-credentials: false`,
- whether the job has a checkout at all, as when a test job runs a downloaded build.

**F10. A re-run of a failed job reads its own first attempt as history, and passes the gate on it**
(READ; RUN, `s7`). A run's history ends at the first line with its exact id (`HistoryLedger.cs:121-125`).
A re-run is `gh:N:2` (`HistoryRunBuilder.cs:174`), and `gh:N:1` was recorded by attempt 1's fold, which
runs `!cancelled()`. The scenario takes one suite of the consumer's ledger and five green runs; then run
6 fails its first scenario:

| | The gate |
|---|---|
| A. Attempt 1 (`gh:6:1`), gated before its fold | exit 1: `broke — passed in gh:5:1, failing now` |
| B. Attempt 2 (`gh:6:2`), the re-run, the same failure | **exit 0**: `already-failing: 1`, `failing since gh:6:1, 2 runs` |
| C. Attempt 1's report gated again after its fold | exit 1: a run is read against the lines before its own |
| D. Attempt 2 against the ledger without `gh:6:1`'s line | exit 1 |

So re-running a red job turns it green without a fix. For the next push this is the design ("a gate
that fails on any red cannot tell a regression from the test that has flipped for a month"). For a
re-run of the same commit it is arguably not. D is what leaving a run's own earlier attempts out of its
reading would give. Changing it changes what a report says, so it is the owner's call (Q6), not 2.3's.
The action's README states it.

**F11. The gate exits 2 when there is no ledger yet** (READ, `HistoryCommand.Maintenance.cs:74`,
`mustExist: true`). On a new repository the first run's gate step is a usage error. The consumer carries
a second step for this: "Tests failed and no ledger to read them against". The gate phase folds that in
(§4.6).

**F12. An action cannot learn its own release** (VERIFIED). `github.action_ref` is empty in `run:`, and a
caller pinning a commit gives a commit. So the tool version the action installs rides in the action's
folder, and a fact holds it to `Directory.Build.props` (§4.7).

**F13. Referencing a folder of this repository downloads the repository** (the layout VERIFIED; the size
RUN). A runner fetches an action's whole repository at the ref, per job, into
`_actions/<owner>/<repo>/<ref>/`. Its only cache across jobs holds popular first-party actions. This
repository's tarball at `5ca0878a` is 17,473,103 bytes (3,140 files). The zip a Windows runner fetches
is 19,627,589 bytes.

- A consumer run with eighteen lanes and a fold job would fetch it 19 times, about 330 MB per run.
- **64% of it is one file**, the PlantUML jar in `src/Kronikol.PlantUml.Ikvm/PlantUml/`: 11.3 MB of the
  17.5 MB compressed. Without the jar the tarball is 6.3 MB. Without the jar, `tools/`, `tests/` and
  `plans/`, it is 1.7 MB. The action's own folder is 45 KB.
- An `export-ignore` attribute would shorten every archive of the repository, including a release's
  source download, which would then no longer build (Q9).
- Copying the folder into the consumer's `.github/actions/` fetches nothing. 13.2's own repository would
  end it.

**F14. A floating major tag would run a release** (READ, `release.yml`). The release workflow runs on
`push: tags: ['v*']`: it packs with the tag's text as the version, pushes to NuGet with
`--skip-duplicate`, and creates a GitHub Release. A `v3` tag would do all three with "3". So there is no
`@v3` reference unless the trigger narrows first (Q4).

**F15. A wrong token hands the machine's credential helpers the host, and git erases what they store**
(RUN, `s6` H1). With the token as a header, a 401 makes git ask whatever credential helpers the
machine's configuration names. A self-hosted runner may name one, and the Windows image names Git
Credential Manager (§2.4). Against the HTTP origin, with a wrong token:

| | What happened |
|---|---|
| The machine's helper holds another identity for the host | `Authentication failed`, and afterwards the machine's store held **no credential**: git had told the helper to erase the one it offered |
| The same, with `credential.helper` reset to empty | `could not read Username ...: terminal prompts disabled`. The store kept its credential |
| The machine's helper takes 8 s, as a credential window waits for a person | the step waited 8.0 s; with the reset, 0.0 s |
| No token at all, with `GIT_TERMINAL_PROMPT=0` or with no terminal | exit 128 at once |

So the scripts reset `credential.helper` and set `GIT_TERMINAL_PROMPT=0` and `GCM_INTERACTIVE=Never`,
the last two as `actions/checkout` sets them for its own git (§4.9). A wrong token then fails at once and
touches nothing of the machine's.

**F16. The folder, copied onto a Windows runner, arrives with scripts bash cannot run** (RUN, `s10` M1;
the Windows default PARTIAL, §2.4). Git for Windows' installer sets `core.autocrlf=true` unless told
otherwise, and the image does not tell it otherwise. This repository's root `.gitattributes` holds
`*.sh text eol=lf`. That protects this repository's own checkouts and the archive a reference fetches,
but not a copy in a consumer's repository. There, checked out with `core.autocrlf=true`, all 89 lines
of the record script ended in CR, and bash stopped at line 10: `set: pipefail\r: invalid option name`.
With a `.gitattributes` in the folder itself (`*.sh text eol=lf`), no line did. So the folder carries
one.

---

## 4. Design

### 4.1 Four actions in one folder

```
templates/github-actions/kronikol-history/
  README.md              the page: both workflow shapes, every input and output, the limits
  VERSION                the release this folder ships in, and the tool version it installs (F12)
  .gitattributes         *.sh text eol=lf: a copy of the folder keeps runnable scripts on Windows (F16)
  read/action.yml        before the tests
  gate/action.yml        after the tests, optional
  save/action.yml        after the tests: the fragments as an artifact
  record/action.yml      in a job after every test job: fold and push
  scripts/read.sh  gate.sh  record.sh  tool.sh      the logic; no GitHub-only variable inside
  scripts/git.sh         sourced by the others: the git settings and the token of §4.9
```

**Why four.**

- A composite action has no post step, so "before the tests" and "after the tests" are two calls.
- The fold must follow **every** shard, because a sharded suite's shards fold into one line. A shard
  recording its own fragment would make the next shard of the same run a duplicate, and drop it.
- The gate is optional and has the most inputs.
- Save is separate from record because in the one-job shape record reads the workspace directly and
  nothing is uploaded.

`github/codeql-action` (`init`, `analyze`) and `actions/cache` (`restore`, `save`) split the same way.
Each phase has only the inputs it reads, so the README drift fact (§5.3) can check the required ones
per phase.

**Not taken:**

| | Why not |
|---|---|
| One action with a `phase` input | Inputs that mean nothing in three of the four phases, and a required-input check that cannot be expressed |
| A JavaScript action with `pre` and `post` | A bundled `dist/`, node's deprecation cadence (Node 20 left the runners on 2026-09-23), and a third test pattern. All that for logic that is git plus a CLI |
| A composite that calls a small JavaScript action to borrow its `post` | VERIFIED only from the runner's source. Clever, and fragile |
| A reusable workflow for the fold job | It must live in this repository's `.github/workflows/`, and it cannot be copied into `.github/actions/`. It could later wrap `record` as sugar |
| The tool's version read from the fragments' `generator` | Mixed versions during an upgrade, locally built versions that are not on NuGet, and a version chosen by data the test run wrote |
| The logic as a tool verb, `kronikol history publish` | Q1: a minor in track A. Recommended only if 2.4 would otherwise carry a second copy |

### 4.2 The interface (what 2.4 builds to)

| Phase | Inputs (default) | Outputs | Effects |
|---|---|---|---|
| **read** | `branch` (`kronikol-history`), `repository` (`github.repository`), `token` (`github.token`) | `found` (`true`/`false`), `ledger` (the path, or empty), `bytes` | `KRONIKOL_HISTORY` in `GITHUB_ENV` when found, with `quarantine.json` and `aliases.json` beside it. **Never fails the job** |
| **gate** | `reports` (required: report files or directories, one per line), `test-outcome` (empty; pass `steps.<test>.outcome`), `fail-on` (`new-failures`), `min-runs`, `max-new-failures`, `min-pass-rate` (the tool's defaults when empty), `version` (`VERSION`), `tool-command` | `result` (`passed`, `failed`, `no-ledger`) | Each report's reading in the job summary. Fails on a trip; with no ledger, fails only if `test-outcome` is `failure` |
| **save** | `path` (`**/History.run.json`), `name` (`github.job`), `retention-days` (`7`) | `artifact` (the name, or empty), `files` | One artifact `kronikol-history-<name>-<attempt>-<8 hex>`. A warning, not a failure, when there are no fragments |
| **record** | `branch`, `repository`, `token` (as read), `artifacts` (`kronikol-history-*`), `path` (empty: download; a directory: fold it instead), `version`, `tool-command`, `record-pull-requests` (`false`), `attempts` (`8`), `accept-renames` (`false`) | `recorded`, `duplicates`, `pushed`, `commit`, `ledger-bytes` | One commit on `branch`, never forced. The recorded runs and the ledger's size in the summary. A warning from 50 MB (Q3). `view` is reserved for 9.4 |

The consumer's two-job shape, with the lines this plan counts marked:

```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: lemonlion/Kronikol/templates/github-actions/kronikol-history/read@v<release>   # 1
      - run: dotnet test
      - uses: lemonlion/Kronikol/templates/github-actions/kronikol-history/save@v<release>   # 2
        if: ${{ !cancelled() }}                                                             # 3

  history:                                                                                  # 4
    needs: test                                                                             # 5
    if: ${{ !cancelled() }}                                                                 # 6
    runs-on: ubuntu-latest                                                                  # 7
    permissions:                                                                            # 8
      contents: write                                                                       # 9
    steps:                                                                                  # 10
      - uses: lemonlion/Kronikol/templates/github-actions/kronikol-history/record@v<release> # 11
```

The one-job shape, for a workflow with a single test job and no pull-request recording: `permissions:
contents: write` on the job, `read` before the tests, and `record` with `if: ${{ !cancelled() }}` and
`with: { path: . }` after them. That is seven lines.

The gate adds five lines: the `uses`, an `if`, `with`, `reports` and `test-outcome`. It adds two more
(`id` and `continue-on-error`) on the test step when the gate, not the test step, decides the job, which
is the consumer's choice today.

For the consumer, `read` and `gate` replace 36 lines in each lane workflow with 6. `record` with
`artifacts: '*-report'` replaces the 77 lines of the fold job that are not its `needs` list with about 8.
That is roughly 167 lines to 38. That route downloads every lane's whole report artifact to reach its
fragment. With `save` in the two lane workflows (four more lines), the fold job downloads the fragments
alone: 1.0 MB for eighteen lanes, 40 to 66 KB each (§2.5).

### 4.3 read

1. A repository of its own at `$RUNNER_TEMP/kronikol-history-read`, with a remote named `origin` at
   `$GITHUB_SERVER_URL/<repository>.git`. It is named, not a bare URL, so a blobless fetch can fetch the
   blobs it lacks. The token goes in as `actions/checkout`'s header, through the environment
   (`scripts/git.sh`, §4.9), on a repository nothing else uses (F9).
2. `git ls-remote --exit-code origin refs/heads/<branch>`:
   - 2 means no branch yet: `found=false`, and a notice that the first `record` starts it. Over HTTP a
     401, a 404 and an unreachable server are all 128, never 2 (`s6` H2).
   - Anything other than 0 or 2 is a warning naming the exit code, and the run continues without
     history.
3. `git fetch --depth=1 --filter=blob:none origin <branch>`: the tip's commit and trees, no file. `git
   show` of `history.jsonl`, `quarantine.json` and `aliases.json` then fetches those three blobs and
   writes them into `$RUNNER_TEMP/kronikol-history/`. A companion the branch does not hold is not there
   afterwards. Whatever else the tip holds, such as 9.4's views, is never downloaded: 31.5 KB against
   3.4 MB with a 25 MB file beside the ledger (`s6` H6). A server that refuses the filter sends
   everything, which is the depth-1 fetch. If Q3's read window is taken, `history.window.jsonl` is read
   in place of the ledger when the tip holds one.
4. `KRONIKOL_HISTORY=<that directory>/history.jsonl` goes into `GITHUB_ENV`, as a native path
   (`cygpath -w` where it exists, §4.9). Then the outputs.

**It never fails the job.** Everything that stops it is a warning, and the tests run without history.
That matches the library's own rule, "A test run never fails over its history" (wiki, Diagnostics). The
tests append their own line to the copy (§2.1), and nothing reads that back.

### 4.4 save

- The fragments are found under `path`, leaving out `.git` and `runs/.incoming-*` (F8). None found is a
  warning, and nothing is uploaded.
- The artifact name is `kronikol-history-<name>-<GITHUB_RUN_ATTEMPT>-<8 random hex>`: unique across
  reusable-workflow calls and re-run attempts (F8), and matched by record's default pattern.
- `actions/upload-artifact` (pinned, Q5) uploads with `include-hidden-files: true` and an explicit
  exclusion for `runs/.incoming-*`. The tree below the workspace survives (§2.4), and that is what
  `record` needs. It groups fragments by reports directory, and a kept attempt under `runs/<name>/`
  folds as an attempt of the run on top of it (`HistoryCommand.cs`, `Attempts`).
- **Retention: 7 days** (Q8). The fold normally runs minutes after the upload. The week covers a record
  job re-run after a fix. The dogfood's 90 days served no reader: the next run reads the ledger, not a
  fragment.

### 4.5 record

**Guards.**

- On `pull_request` and `pull_request_target` it records nothing and says why, unless
  `record-pull-requests: true` (Q2).
- It refuses a `branch` equal to the repository's default branch: a data branch is never a code branch.

**Fragments.** With `path` set, it folds that directory. Otherwise `actions/download-artifact` (pinned)
takes `pattern: <artifacts>` into `$RUNNER_TEMP/kronikol-history-fragments`, without `merge-multiple`,
since merging is last-writer-wins on the same `History.run.json` path. No fragments is a notice, and
exit 0: a test job that failed before writing any leaves nothing to record.

**The loop.** This is `prototype_record.sh`, which ran in the race and refusal scenarios against
`file://` origins (`s2`, `s2b`, `s5`) and, with its token and git settings, over HTTP (`s6` H3, H4). Over
HTTP, 36 folds racing six at a time, three times on a first run and three on an existing branch, lost
no run, duplicated no roster line and failed no job; 64 lost races were recorded again. The loop:

```
for attempt in 1..attempts:
    base = ls-remote(branch)                        # a commit, or "absent" (exit 2), or an error: stop
    a fresh repository in RUNNER_TEMP
    if base is absent:  orphan branch; kronikol history init (header and .gitattributes); README
    else:               fetch base at depth 1; check it out
    kronikol history record <fragments> --history history.jsonl
    nothing staged -> "nothing new to record", exit 0                     # every run a duplicate
    commit "Record run <run id>:<attempt> (<sha7>)"                         # the dogfood's message; a
                                                                            # failed commit is an error
                                                                            # naming git's message (s10 M3)
    push HEAD:refs/heads/<branch>, never forced -> done
    pause 0.1-0.9 s                                                         # a competing push lands
    if ls-remote(branch) == base: stop, naming the server's refusal         # not a race (E2)
    go round                                                                # lost a race
fail after the last attempt
```

**Why record again, and not rebase.**

- F4: no losses and no duplicates.
- `merge=union` stops carrying correctness. The branch keeps it, because `history init` writes it and a
  person merging by hand still wants it.
- What `record` derives from the ledger is derived from the ledger the line lands in.
- For 9.4, the view is regenerated, not merged. The dashboard plan's §4.6 conflict loop
  (`GIT_EDITOR=true git rebase --continue` after regenerating the view) is not needed.
- **Prior art came to the same place.** `github-action-benchmark`, which appends to a JSON file on
  `gh-pages`, retried with `git pull --rebase` until v1.6.0. Its changelog says why it stopped:
  "Previously this action tried to rebase the local onto the remote but it sometimes failed due to
  conflicts". It now resets, re-reads the file on the new tip and adds its entry again (up to ten
  times, no pause). The other six surveyed (Appendix B) do one of three things:
  - They force-push, as `JamesIves/github-pages-deploy-action` does by default ("one of the commits will
    be destroyed", its #1052). With `force: false` it rebases, three times.
  - They pull and push again: `jgehrcke/github-repo-stats` every 10 s for up to 500 s, and
    `EndBug/add-and-commit`, whose `push_attempts` (v11.1.0, August 2026) retries any error with no pause
    and pulls only when `pull` is set.
  - They do not retry at all: `peaceiris/actions-gh-pages` and `git-auto-commit-action`, whose trackers
    ask for a retry on concurrent runs (#759, #1078; #170), and `python-coverage-comment-action`. That
    last one rewrites its file in full on every run and puts its job in a concurrency group with
    `cancel-in-progress: true`. That suits a file holding only the latest value, and would lose an
    append-only ledger's runs.
- **The price is F6:** each lost race costs one more full `record`.

**Why eight attempts are enough.** A push is refused in a race only because another writer's push
landed, and a writer that has landed stops. So with N folds finishing together, no fold can lose more
than N - 1 times, and eight attempts cover eight at once. A ninth has its job fail with the error, and
a re-run records it, because recording again is safe.

**Why a refusal while the branch stands still is not retried.** A ruleset, a hook, a token without
`contents: write` or a fork's read-only token does not go away in a second. In the harness a
`pre-receive` hook refusing the branch made the prototype stop after one push, naming the refusal (E2,
and `s6` H4 over HTTP). A read-only token, answered 403 as a fork's pull request is, did the same.
Keying on the branch's tip, not on git's message, is what makes this independent of the server's
wording. It matters: the message does not tell the two apart. A race lost on GitHub can print
`[remote rejected] … (cannot lock ref …)` (JamesIves #1000, EndBug #513), and a ruleset refusal prints
`[remote rejected] … (push declined due to repository rule violations)` (python-coverage-comment-action
#561). The two
actions surveyed that match on it retry refusals as races. None of the seven reads the tip. The tip is
compared after the pause, so a competing push that still held the ref's lock has landed before the
comparison. That a read right after a push sees it is not documented; §11 has what is known.

**The summary.** For each suite the line was recorded under: `kronikol history show --history <ledger>
--suite <suite>`, plus the ledger's size and the commit.

### 4.6 gate

- **Without a ledger** (`KRONIKOL_HISTORY` unset, because read found none), there is nothing to read the
  run against. The step says so, and fails only if `test-outcome` is `failure` (F11). The consumer's two
  steps become one.
- **With a ledger**, it installs the tool (§4.7) and, for each entry of `reports`, runs `kronikol history
  gate <report> --history "$KRONIKOL_HISTORY"` with the inputs given as flags. The output goes into the
  job summary under the report's path.
- **Exit codes.** An exit 1 from any report fails the step, after all of them have run. An exit 2 is an
  error naming the report: an unreadable report is never a pass.
- **Its limit** is F10, stated in the README: a re-run reads its first attempt, and passes on the
  failure that attempt recorded (`s7` B).
- **On a pull request** it reads against the branch the pull request targets (`GITHUB_BASE_REF`), as
  the run itself did (`HistoryCommand.Maintenance.cs:101-102`, `HistoryRunContext.cs:138-141`). So a
  pull request is gated on its target's history even though its own runs are not recorded (Q2).

### 4.7 The tool

- **Version.** `VERSION` holds the release, and the release process bumps it with `Directory.Build.props`
  (a fact holds the two equal, §5.3). That is the rule `PluginManifestTests` already applies to the
  plugin manifest's version (`The_plugin_version_is_the_repositorys_version`): "hand-written versions
  rot ... Equality makes the release helper move it or fail the build". The template pins are held
  differently, one release behind, because they restore the published package. A tag therefore installs
  its own tool. A reference to `main`, or to a tag in the minutes before `release.yml` has published the
  packages, fails the install naming the version (documented).
- **Install.** `dotnet tool install Kronikol.Tool --version <v> --tool-path
  "$RUNNER_TEMP/kronikol-tool/<v>"`, reused if a step in the same job already installed it. Not
  `--global`: a global install fails when another version is installed, and changes the job's `PATH`.
  The package is 7.5 MB (3.31.9). Installing it from nuget.org into an empty cache took 9.7 s in this
  container (RUN); a runner's time is S4's to measure. `dnx Kronikol.Tool` would skip the install, but
  it fetches from the feed on every call, and the action calls the tool several times.
- **Runtime.** Hosted images carry .NET 10 (§2.4). When `dotnet --list-runtimes` has no
  `Microsoft.NETCore.App 10.`, `dotnet-install.sh --runtime dotnet --channel 10.0` installs one into
  `$RUNNER_TEMP/kronikol-dotnet`. `DOTNET_ROOT` is set for the tool's own calls only, never in
  `GITHUB_ENV`, so the caller's later `dotnet` steps are untouched.
- **`tool-command`.** When set, it replaces the install. Kronikol's own dogfood builds from source (S4):
  `dotnet run --project src/Kronikol.Tool/Kronikol.Tool.csproj --framework net10.0 -c Release --`.

### 4.8 Credentials and trust

- `token` defaults to `github.token`. Read and gate need `contents: read`, and record needs `contents:
  write`, on the job that calls it.
- **How the token reaches git.** It is passed as `actions/checkout`'s header,
  `http.<server>/.extraheader`, scoped to the server, through the environment (`GIT_CONFIG_COUNT`,
  `GIT_CONFIG_KEY_<n>`, `GIT_CONFIG_VALUE_<n>`, git 2.31 and later). So it is in no file, no URL and no
  command line. Checkout keeps it off the command line too, by writing a placeholder and swapping the
  value into its file, "to avoid the credential being captured by process creation audit events". The
  base64 pair is masked with `::add-mask::` before its first use: the runner masks `github.token`
  itself, not its base64 form. In the 42 jobs of `s6`, the token and its header reached no output line
  and no file under `RUNNER_TEMP`, the action's own `.git/config` included (H5).
- **The one-job shape gives the test job `contents: write`.** The tests then run in a job whose token can
  push, and `actions/checkout` persists that token by default where the job's later processes can read
  it. The two-job shape keeps the test job at `contents: read`. The README says so.
- `repository` with a PAT or an App token puts the ledger in another repository, such as a central
  history repository for many services.
- On GHES, `upload-artifact` and `download-artifact` v4 and later are not supported (§2.4). The one-job
  shape (`record` with `path`) still works there.
- **The fragments are input written by the test job.** On a fork's pull request the tests are the
  contributor's code, which is why record is off for pull requests by default (Q2). The tool parses a
  fragment strictly and only appends. A crafted fragment can add a line, but cannot rewrite one.
- **No `${{ }}` inside a `run:` body.** Inputs reach the scripts as environment variables (2.2's rule),
  and a fact holds it.
- The actions it calls (`upload-artifact`, `download-artifact`) are pinned to commits, with the version
  in a comment (Q5).

### 4.9 Portability

The scripts run under `shell: bash` everywhere:

- macOS bash is 3.2, so no associative arrays, `mapfile`, `${var,,}` or `readarray`.
- No GNU-only flags: `base64 -w0` (use `| tr -d '\n'`), `sed -i`, `date +%N`, `readlink -f`,
  `find -printf`. `find ... -print -quit` works on both.
- Every path is built from `$RUNNER_TEMP`, never from `/tmp`. A path handed to .NET goes through
  `cygpath -w` where that exists.
- The pause is `sleep 0.<digit>`, which Git for Windows' coreutils and macOS both take (§2.4).
- The folder carries `.gitattributes` with `*.sh text eol=lf`, so a copy of it checked out on Windows
  keeps runnable scripts (F16).

**Git's settings.** The scripts' git reads the machine's configuration, so proxies, CA bundles, URL
rewrites and commit signing keep working. It sets five things of its own for its own repositories,
through the environment, in `scripts/git.sh`:

| Setting | Why | Evidence |
|---|---|---|
| `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=Never` | Nothing waits for a person | What `actions/checkout` sets for its own git; F15 |
| `credential.helper` set to empty | A wrong token fails at once. No helper of the machine's is asked, so none waits and none is told to erase what it stores | F15 (`s6` H1: the store emptied without it, kept with it; 8.0 s against 0.0 s) |
| `core.autocrlf=false` | The ledger's bytes are the tool's | Windows' default is `true` (§2.4) |
| `core.hooksPath` to a path that does not exist, and `--no-verify` | A machine's policy hook for people's commits does not stop the data commit | `s10` M2: such a hook refused a plain commit; the prototype pushed |
| `http.<server>/.extraheader` | The token (§4.8) | `s6` H1 c, H3, H5 |

`commit.gpgsign` is left alone. A machine that signs every commit signs this one, which a
signed-commits ruleset on the data branch needs. One that cannot sign fails the step with git's message
(`s10` M3: `gpg failed to sign the data`). The committer is set in the repository:
`github-actions[bot]`, as the dogfood's is.

CI runs the behaviour facts on Windows and macOS as well (S4). The harness ran Linux only; `s10`
reproduces Windows' `core.autocrlf` there.

### 4.10 What it does not do

- No concurrency group (F5).
- No force push.
- No pruning (Q3).
- No recording of pull requests by default (Q2).
- No writes to the default branch.
- No pull-request comment: that is 2.2's action, and 7.4's scenarios go into 2.2's comment.
- No alerting: 9.6 builds on the gate's outputs.
- No dashboard: 9.4 adds `view`.
- No Azure DevOps: 2.4.
- No artifacts on GHES.

### 4.11 Where it lives, and how it is referenced

- **The reference** is `lemonlion/Kronikol/templates/github-actions/kronikol-history/<phase>@v<release>`.
  Each job that uses it downloads the repository, 17.5 MB (F13). The README offers copying the folder
  into `.github/actions/kronikol-history/` as the second way, with `uses: ./.github/actions/...`.
- **No floating major tag** while `release.yml` triggers on `v*` (F14, Q4).
- **Dependabot's** `github-actions` ecosystem proposes each new release tag. At this repository's
  cadence that is several a day. The README says to group or schedule those updates.
- **14.4's split of the tags** into `dotnet-v*` and `java-v*` changes the reference form. The README
  changes with it.

---

## 5. Tests, red first

### 5.1 Where

All in `tests/Kronikol.Tests/Templates/`, beside 2.2's facts, so CI's Core Tests job runs them. A
Windows and macOS leg is added in S4. The test project already references `Kronikol.Tool`, so the real
tool is in the test output, and the facts run it through a `kronikol` shim, never a fake.

### 5.2 Scaffolding (S1)

- **`ActionDefinition`** is extracted from `PrReportLinkActionTests` into `Templates/ActionDefinition.cs`.
  It generalises to several steps, `run` steps with `shell`, and `uses` steps with `with`. 2.2's 17
  facts stay unchanged and green (roadmap §5: extract once, in 2.3).
- **`BashProbe`** and **`GitProbe`** skip where bash or git is missing, as `NodeProbe` does for node.
- **`CompositeActionRunner`** runs one `action.yml` the way the runner does:
  - It evaluates the expression subset the four actions use (`inputs.*`, `github.*`, `runner.*`,
    `env.*`, `steps.*.outputs.*`, `strategy.*`, `always()`, `!cancelled()`, `==`, `!=`, `&&`, `||` on
    strings) and **fails on any other expression**, which keeps logic in the scripts.
  - It runs each `run:` step under bash, applying `GITHUB_ENV`, `GITHUB_OUTPUT` and `GITHUB_PATH`
    between steps.
  - It resolves `upload-artifact` and `download-artifact` against a directory that stands in for the
    run's artifact store, **with the real rules**: a duplicate name fails, one match downloads flat, and
    several download under their names.
  - It treats `setup-dotnet` as a no-op.
  - **The environment is built, never inherited.** The test process carries `KRONIKOL_HISTORY=off` and
    `KRONIKOL_KEEP_RUNS=off` from `test.runsettings`, and a step that inherited them would test history
    switched off.
- **`BareOrigin`** is a bare repository in a temporary directory, with `pre-receive` hooks for refusals.
  It is the harness's `common.sh` made into C#.
- **`HttpOrigin`** serves a `BareOrigin` over smart HTTP: `git http-backend` behind an `HttpListener`
  that demands basic authentication. It has a token that reads and writes, a read-only token whose push
  is answered 403, and a log of every request with its status, its size and who it was served as. It is
  the harness's `githttp.py` made into C#, about a hundred lines. Every fact about the token, the
  machine's credential helpers or what read downloads needs it: a `file://` origin has no
  authentication and no status codes.
- **`MachineConfig`** writes a git configuration that `GIT_CONFIG_GLOBAL` names for one step: a credential
  store, a slow helper, a hooks path, a signing program. It stands for a self-hosted runner's
  configuration, or for the Windows image's.

The runner's own facts: each expression form, a rejected unknown expression, env isolation, and the
artifact rules.

### 5.3 Static facts, per `action.yml` and the README (S2, S3)

- Every `run:` step declares `shell: bash`. No `run:` body contains `${{`.
- Every declared input reaches its step's `env`, and no script reads an undeclared input: 2.2's wiring
  fact, generalised.
- Every `uses:` is pinned to a 40-character commit (Q5).
- No script pushes with `--force` or a `+` refspec.
- Every expression is one the runner evaluates.
- `VERSION` equals `Directory.Build.props`'s `<Version>`, as `PluginManifestTests` holds the plugin's.
- Every script that runs git sources `scripts/git.sh`. No script passes a header, a token or
  `credential.*` on git's command line (`-c`), and none writes them with `git config`.
- The folder's `.gitattributes` holds `*.sh text eol=lf`, and no script in the folder has a CR.
- **README drift:**
  - Each workflow in the README calls each phase with declared inputs only and every required one.
  - The job that calls `record` has `contents: write`. In the two-job shape the test job does not.
  - `save` and `record` run `!cancelled()`.
  - No workflow in the README has a concurrency group (F5).

### 5.4 Behaviour facts, each red before its code exists

| Phase | Fact |
|---|---|
| read | `Without_a_history_branch_read_finds_nothing_and_the_run_goes_on_without_history` |
| read | `Read_copies_the_ledger_and_both_companions_side_by_side_and_names_the_ledger` (F2; red against the dogfood's read) |
| read | `A_companion_the_branch_does_not_hold_is_not_in_the_copy` |
| read | `An_unreachable_origin_is_a_warning_and_never_fails_the_job` |
| read | `Read_leaves_the_checkout_untouched` (no ref, no `FETCH_HEAD`, no config written in the workspace) |
| read | `Read_downloads_only_the_files_it_reads` (`HttpOrigin`: a large file beside the ledger is never served; `s6` H6) |
| read, record | `A_wrong_token_fails_at_once_and_leaves_the_machines_stored_credentials_alone` (`HttpOrigin`, `MachineConfig` with a credential store; F15) |
| read, record | `A_credential_helper_that_waits_for_a_person_is_never_asked` (`MachineConfig` with a helper that sleeps; F15) |
| read, record | `The_token_reaches_no_output_no_file_and_no_command_line` (every output, every file under `RUNNER_TEMP`, and the argument lists of the git processes, captured through a `git` shim on `PATH`; `s6` H5) |
| save | `Save_uploads_each_fragment_under_its_reports_directory_with_kept_attempts_beside_it` |
| save | `A_reports_directory_under_a_hidden_folder_is_uploaded` (F8) |
| save | `A_staged_rotation_is_never_uploaded` (F8) |
| save | `Two_calls_with_the_same_job_and_index_upload_under_different_names` (F8: the reusable-workflow case) |
| save | `A_rerun_attempt_uploads_beside_the_first_attempts_artifact` |
| save | `No_fragment_is_a_warning_and_no_artifact` |
| record | `The_first_record_makes_an_orphan_branch_with_a_readme_the_merge_attribute_and_the_ledger` |
| record | `A_later_run_adds_one_line_per_suite_in_one_commit_named_for_the_run` |
| record | `Recording_the_same_run_again_changes_nothing_and_succeeds` |
| record | `Writers_racing_on_an_existing_branch_all_land_once` and `Writers_racing_on_a_first_run_all_land_once_with_no_duplicate_roster_line` (F4: four writers, the race run three times; the second is red against the dogfood's loop) |
| record | `A_push_refused_while_the_branch_stands_still_fails_at_once_naming_the_refusal` (a `pre-receive` hook) |
| record | `A_read_only_token_fails_at_once_naming_the_refusal` (`HttpOrigin`'s 403, as a fork's pull request gets; `s6` H4) |
| record | `A_machine_hook_does_not_stop_the_data_commit` (`MachineConfig` with a refusing `pre-commit`; `s10` M2) |
| record | `A_machine_that_cannot_sign_fails_naming_gits_message` (`MachineConfig` with a failing signing program; `s10` M3) |
| record | `A_copy_of_the_folder_checked_out_with_autocrlf_runs` (the folder committed into a repository and cloned with `core.autocrlf=true`; `s10` M1) |
| record | `An_unreachable_origin_is_an_error_and_never_a_new_branch` |
| record | `A_pull_request_run_is_not_recorded_unless_asked`, and `...is_recorded_under_its_own_stream_when_asked` |
| record | `Record_refuses_the_default_branch` |
| record | `Record_folds_a_workspace_path_without_artifacts` (the one-job shape) |
| record | `Shards_of_one_run_downloaded_from_several_artifacts_fold_into_one_line_per_suite` |
| record | `A_retry_inside_one_job_is_folded_as_an_attempt` (kept runs under `runs/` survive upload and download) |
| record | `The_summary_names_the_recorded_runs_and_the_ledgers_size`, and `A_ledger_over_the_threshold_is_a_warning` |
| gate | `Without_a_ledger_the_gate_passes_a_green_run_and_fails_a_red_one_saying_why` (F11) |
| gate | `A_new_failure_fails_the_gate_and_its_reading_is_in_the_summary` |
| gate | `A_quarantined_failure_passes_because_the_companion_came_with_the_ledger` (read then gate; F2; red against the dogfood's read) |
| gate | `Every_report_is_gated_and_one_trip_fails_the_step` |
| gate | `The_inputs_reach_the_tool_as_flags` |
| gate | `An_unreadable_report_is_an_error_not_a_pass` |
| tool | `The_tool_is_installed_to_a_path_of_its_own_and_the_jobs_PATH_is_unchanged` |
| tool | `A_tool_command_replaces_the_install` |
| tool | `Without_a_NET_10_runtime_one_is_installed_for_the_tools_calls_only` (a stub `dotnet` that lists no 10.x runtime, and a stub installer; the real install runs in S4's CI) |

The gate facts write a minimal `TestRunReport.json` by hand, as `HistoryGateTests` does.

### 5.5 Proving red

Each guard is broken in turn, the file restored afterwards, and the facts that fail recorded in the
execution log (the #73 and `FLOW_NESTING_PLAN` practice):

| Guard broken | Must fail |
|---|---|
| Companions not copied | read's companion fact, gate's quarantine fact |
| Rebase in place of recording again | the first-run race fact |
| The "branch did not move" check removed | the refusal fact (it pushes `attempts` times) |
| `ls-remote` exit 2 no longer required for a new branch | the unreachable-origin fact |
| The random part left out of the artifact name | the two-calls fact |
| `include-hidden-files` removed | the hidden-folder fact |
| The `.incoming-` exclusion removed | the staged-rotation fact |
| The pull-request guard removed | the pull-request fact |
| The default-branch guard removed | the default-branch fact |
| An input interpolated into a `run:` body | the static fact |
| `credential.helper` no longer reset | the stored-credentials fact, the waiting-helper fact |
| `--filter=blob:none` removed from read | the downloads-only fact |
| The token passed with `git -c` | the token fact (the argument lists), the static fact |
| `core.hooksPath` and `--no-verify` removed | the machine-hook fact |
| The folder's `.gitattributes` removed | the autocrlf fact, the static fact |

### 5.6 On real runners

The harness's scenarios become facts, but a hosted runner's own git, bash and artifact service are
still untested there. S4 closes that twice:

- **A CI entry "Template actions (Windows, macOS)"** runs `FullyQualifiedName~Kronikol.Tests.Templates`
  on `windows-latest` and `macos-latest`. The matrix is ubuntu-only today, so it needs
  `runs-on: ${{ matrix.os || 'ubuntu-latest' }}`.
- **The dogfood.** `ci-summary-preview.yml` uses the four phases from their paths on every push to
  `main`, against the real branch.

---

## 6. Slices

| Slice | What | Needs | Done when |
|---|---|---|---|
| **S0** | **The wiki's recipe made true.** "The recommended shape" rewritten from the prototype: fetch with the companions; a fold in a repository of its own, with the token as a header and git's settings of §4.9, never a worktree of the checkout (F9, F15); it makes the branch on the first run (`kronikol history init`), sets an identity, records, and on a lost race records again on the new tip (eight attempts, a pause); where the companions live on a data branch. It is marked as the manual route until the action ships | nothing: it describes shipped tools, so the wiki may publish it at once | The new text, run verbatim through `s1_wiki_recipe.sh` (as W5), `s2b_race_trials.sh` and `s6_http.sh` (H3, H4), passes the first run, the later runs, ten races and both refusals |
| **S1** | The scaffolding (§5.2), on 2.2's rebased branch | 2.2 rebased | 2.2's 17 facts green through the extracted `ActionDefinition`; the runner's, `BareOrigin`'s, `HttpOrigin`'s and `MachineConfig`'s own facts green |
| **S2** | `read`, `save` and `record`, `scripts/` with `git.sh`, `VERSION`, `.gitattributes`, the README; §5.3 and §5.4's facts for the three; §5.5's mutations | S1 | Every fact red first, then green; the mutation table filled in; the harness scenarios (`s2` to `s10`) re-run against the action's scripts |
| **S3** | `gate` and its facts | S2 | As S2 |
| **S3b** | **The live lane**, as 2.2's S3. A workflow on the pull requests that change the folder deletes a scratch branch (`kronikol-history-lane`, never `kronikol-history`), then runs read, a small test run, save and record by path, with `record-pull-requests: true`, on ubuntu, windows and macos. A second job has six `record` calls race on the scratch branch | S2, S3; a pull request from this repository (a fork's token cannot push) | On the pull request that adds the action: every leg green, one line per suite per leg on the scratch branch, and the six racers each landed once. It is the first run on github.com, on hosted Windows and macOS, and against the real artifact service (§11), before anything merges. Not a required check, as 2.2's lane is not |
| **S4** | `ci-summary-preview.yml` switches to the four phases by path, with `tool-command` building from source, the same branch and the same commit message. The Windows and macOS CI entry is added | S2, S3 merged, 2.2 merged first (roadmap §5) | Three pushes to `main` recorded, one line per suite each, `history verify` clean, continuity with the branch's earlier lines (`history show`). The new CI entry green |
| **S5** | Documentation (§8). The consumer switches after the first release tag that holds the folder | S4, a release tag | A week of BreakfastProvider's CI: every run recorded once per lane (`history show` count against the Actions run list), the gate's summaries unchanged, about 167 lines down to about 38 |
| **S6** *(only if Q3 takes (d))* | The read window: `record` commits `history.window.jsonl` beside the ledger, and `read` prefers it | Q3 (d); the tool's per-stream window, written in record's own pass (track A, §9) | `s9`'s comparison as a fact, on a ledger with two streams: the gate's output against the window equals its output against the ledger |

**Effort.** S0 about an hour. S1 a day and a half, half a day of it `HttpOrigin`. S2 two days. S3 half a
day. S3b half a day. S4 half a day. S5 half a day plus the week of runs. S6 half a day once the tool has
its part. That is the roadmap's "days".

---

## 7. Bumps and releases

**None, at any slice.** No package contains `templates/github-actions/`, and 2.2 set the same precedent
("a release built from this commit ships the same bytes"). Under `CLAUDE.md`'s rule the version moves
with what a package ships, and a commit touching only templates, tests, workflows, plans and the wiki
moves nothing.

- The `CHANGELOG.md` entry goes under `[Unreleased]`, which 2.2 opens ("No version change - a GitHub
  Actions template in the repository"). It moves under the first release that follows.
- That release is the first tag a consumer can reference, and the entry says so.
- `VERSION` then moves with every release, as the template pins do.
- No Kronikol4J ledger entry: no report byte changes.

---

## 8. Documentation

- **The action's README**: both shapes, the consumer's shape (§4.2), every input and output, the
  limits (F10's re-run reading, GHES artifacts, 17.5 MB per job or copy the folder), quarantining on a
  data branch (Q7), and reading the branch locally. Also: the one-job shape's write token in the test
  job (§4.8); a signed-commits ruleset needs the data branch excluded or a runner that signs (§4.9); and
  a copy of the folder keeps its `.gitattributes` (F16).
- **Wiki `Cross-Run-History`**: "On CI" and "The recommended shape" rewritten around the action (after
  S2 merges: roadmap §5, the wiki publishes on push). S0's manual recipe stays below it as "Without the
  action". A paragraph on retention and size (F7).
- **Wiki `CI-Artifact-Upload`**: a line pointing at `save`, and why fragments get their own artifact.
- `templates/README.md`: a section beside 2.2's.
- Root `README.md`: one sentence after the `kronikol history gate` line.
- `CHANGELOG.md` `[Unreleased]`.
- `ROADMAP.md` 2.3 and D26, and this plan's row in `PLANS_STATUS.md`.
- `CROSS_RUN_HISTORY_PLAN.md` §11 names the dogfood's recipe: one line saying S4 replaced it.

---

## 9. What this hands to others

| To | What |
|---|---|
| **2.2** (PR #73, `PR_REPORT_LINK_PLAN.md`) | F5 is its own F1 already, with the fix rehearsed. From here it gets the extracted `ActionDefinition` (S1); from it this plan takes the live lane (S3b) |
| **2.4** (Azure DevOps) | §4.2's interface. The scripts are forge-neutral: the Azure template carries byte-identical copies held by a drift fact, as `SkillDriftTests` holds the two `query.py` copies, unless Q1 moves the logic into the tool. What differs there is credentials (`System.AccessToken`, the build identity's Contribute permission) and artifacts (`PublishPipelineArtifact`), which are the store plan's REFERENCE claims that 2.4 checks first |
| **The tool** (track A, a patch) | F6: read the ledger once per `record` and look for duplicates in that pass. Measured today: eighteen suites at 180 MB take 30 s and 1 GB, where one suite takes 5.4 s; the patch should bring the eighteen near the one. F10 is the owner's question (Q6) |
| **The tool** (track A, only if Q3 takes (d)) | A window per stream, not per suite (`prune` keeps the last N runs of each suite across streams, `HistoryLedgerWriter.cs:315-318`), written by `record` in its own pass: `record --window-file <path> --window N`. Today's `prune` of a copy costs 12.4 s and 1.6 GB at 180 MB |
| **9.4** (dashboard) | `record` gains `view: true` once `record --view` exists. Recording again regenerates the view, so the dashboard plan's §4.6 conflict loop is not needed. Read is blobless, so view files on the branch cost the lanes nothing (`s6` H6). F7's growth is P1's retention question, and Q3's read window is measured for it (`s9`) |
| **9.6** (alerting) | The gate's `result` output and its summary are where per-verdict events hook in |
| **13.2** (Marketplace) | One root `action.yml` per repository, subfolder actions not listed, workflow files allowed since 2026-05-25 (§2.4). So the listing is a small repository whose root action is `record`, or a root `action.yml` here. The first ends F13 as well: 45 KB against 17.5 MB a job |
| **14.4** (monorepo) | The tag split changes the action's reference form |

---

## 10. Open questions for the owner (D26), with recommendations

1. **Where the git logic lives.** (a) Scripts in the action: no bump, track C, the roadmap's placement.
   (b) A `kronikol history publish` verb: a minor in track A, one implementation for every forge and
   for a person at a terminal. (c) (a) now, and (b) if 2.4 would carry a copy that has to change
   independently. **Recommended: (c).** The fold is about 70 lines of bash, proven by the harness, and a
   drift fact keeps a second copy honest.
2. **Record pull-request runs?** **Recommended: not by default.** `record-pull-requests: true` records
   each pull request as its own stream, named `<number>/merge` from `GITHUB_REF_NAME` (`CiMetadata.cs:47`).
   No reading uses such a stream by default: a pull request's build reads against the branch it targets
   (§4.6), so its own lines would be read only when someone asks for that stream. A fork's pull request
   cannot push anyway, and every recorded run grows the tip every lane fetches (F7). Pushes are recorded on
   any branch, each its own stream, which is what that branch's next push reads. A workflow that runs on
   every branch's push narrows that in its own `on.push.branches`.
3. **Retention.** (a) None. (b) Prune the ledger. (c) 9.4's monthly views, with a pruned tip. (d) A
   read window (F7, `s9`): the ledger keeps every line, and record commits beside it a copy pruned to
   the readers' window, which read fetches instead. At a year's size it read identically, fetched 17.6
   times less and took 3.7 times less memory in each test run. It cost the pack about 1 KB a run. It
   needs the tool's per-stream window (§9) before it is faithful for a ledger with several streams.
   **Recommended: (a) in 2.3**: report the size and warn from 50 MB. **When the owner takes Q3 with 9.4,
   (d) first**: it bounds what every lane pays without deleting a line, and leaves (b) and (c) to decide
   what the pack and the dashboard keep.
4. **A floating `v3` tag.** **Recommended: not now.** If it is wanted, narrow `release.yml` to
   `v[0-9]+.[0-9]+.[0-9]+` first (F14). A Marketplace repository (13.2) can carry its own `v1`.
5. **Pinning the actions it calls.** **Recommended: full commit SHAs with the version in a comment**,
   because `record` runs with `contents: write`. Dependabot keeps SHA pins current.
6. **F10: should a re-run's reading leave out its own earlier attempts?** **Recommended: the owner's
   call, outside 2.3.** It changes what a report and the gate say, so it is its own patch or minor in
   the library. The action documents today's behaviour. The read step could do it without the library,
   by leaving `gh:<run>:*` lines out of the copy it hands the tests (`s7` D shows the effect). That is
   not recommended: CI's reading would then differ from `kronikol query history` on the same ledger,
   and 2.4 would need the same filter under another name.
7. **Where quarantine and aliases live for a team on a data branch.** On the branch, beside the ledger,
   or on `main`? **Recommended: on the branch.** It is the resolver's one rule, and no new option. The
   README gives the commands: fetch, `git worktree add`, `kronikol history quarantine ... --history`,
   commit and push, or a pull request against the branch, which keeps review. A convenience for this
   can come later.
8. **Fragment artifact retention.** **Recommended: 7 days** (§4.4), against the dogfood's 90.
9. **Shorten what a reference downloads?** An `export-ignore` on the PlantUML jar alone would take every
   job's download from 17.5 MB to 6.3 MB. With `tools/`, `tests/` and `plans/` as well it would be 1.7
   MB (F13). But `export-ignore` shortens every archive of the repository, including a release's source
   download, which would no longer build. **Recommended: not now.** Copying the folder costs nothing
   today, and 13.2's repository ends F13 for the reference route. Take it only if the reference route
   is wanted cheap before 13.2.

---

## 11. What is not known

- **Windows and macOS.** The scripts are written to the constraints of §4.9, but nothing ran there.
  `s10` reproduces Windows' `core.autocrlf` on Linux, and nothing more. S3b's lane finds out first, and
  S4's CI entry keeps it true.
- **github.com's own push races.** `file://` origins refuse a stale push the way GitHub does ("fetch
  first", "cannot lock ref"). So does git's `http-backend` over HTTP (`s6` H3: 36 folds racing, none
  lost). github.com itself was not raced. The loop keys on the tip, not the message. Whether GitHub's
  secondary rate limits bite at eight fast attempts is not known, and the pause is there for it. S3b's
  six racers answer both on github.com before anything merges.
- **Whether a read right after another client's push sees it.** The refusal check assumes so, and it is
  not documented. GitHub's engineering posts on Spokes say a push is accepted only when "a strict
  majority of the replicas" apply it, and that reads go "to the closest replica that is in sync". That
  suggests it, without saying it (Appendix B). If a stale read ever made a race look like a refusal, the
  job would fail naming git's own race message, and a re-run would record the run. A second look after
  a few seconds is the one-line fix, if that is ever seen.
- **The artifact service on re-runs** (PARTIAL, §2.4): regressions as recent as July 2026. A record job
  that cannot download an earlier attempt's artifact fails, and it is re-run. S3b can re-run its record
  job once by hand to see it.
- **The tool's install time on a hosted runner.** From nuget.org into an empty cache it took 9.7 s here,
  for a 7.5 MB package. The consumer pays that in eighteen lanes today, uncached. A runner's time is
  measured in S4.
- **Whether hosted images keep .NET 10** through ubuntu 26.04 (VERIFIED for today's images). §4.7's
  private runtime covers a gap.

## 12. What this plan does not do

It builds nothing for Azure DevOps (2.4), the dashboard (9.x), alerting (9.6), the Marketplace (13.2),
GitLab (§6 of the roadmap), the tool's F6 patch, or the tool's per-stream window for Q3 (d). It changes no
library behaviour (F10 is Q6). It does not prune a ledger.

## 13. Assumption ledger

| Claim | Mark | Where |
|---|---|---|
| The wiki's recipe fails on first and later runs, then on identity | RUN | `results-s1-wiki-recipe.txt` |
| A hand-made branch has no `merge=union`, and its rebase conflicts | RUN | `results-s5-edges.txt` E1 |
| The read step drops the companions | RUN, READ `HistoryQuarantine.cs:49`, `:155` | `results-s3-companions.txt` |
| The dogfood's loop loses runs and a first-run race duplicates rosters; recording again does neither | RUN | `results-s2b-race-trials.txt` |
| Losses come from the retry budget, duplicates from rebasing | RUN | the fair variant's row |
| A server refusal stops the prototype at once | RUN | `results-s5-edges.txt` E2 |
| `ls-remote --exit-code`: 2 absent, 0 present, 128 unreachable; over HTTP 128 for a 401, a 404 and a server down | RUN, VERIFIED | E3; `results-s6-http.txt` H2; git docs |
| `record` reads the ledger twice per suite | READ `HistoryCommand.cs:304`, `HistoryLedgerWriter.cs:201` | |
| `record` of 18 lanes: 4.6 s at 7.1 MB, 30 s and 1 GB at 180 MB; each suite adds 0.24 s at 7 MB and 1.5 s at 180 MB | RUN (grown ledgers) | `results-s8-scaling.txt` |
| ~~A year at the consumer's rate brings a fold to about 110 s~~ (first pass; a slope from a 0.8 MB range) | superseded: 30 s, RUN | `results-s8-scaling.txt` |
| 180 MB is about a year at the consumer's rate | INFERRED (the rate held linear) | §2.5 |
| Each test run's reading at 180 MB takes 1.4 s and 454 MB, against 0.56 s and 107 MB today | RUN (the gate, which uses the same reader) | `results-s8-scaling.txt` |
| A read window (`prune --window 50` of a copy) reads identically, is 17.6 times smaller gzipped and costs the pack about 1 KB a run | RUN | `results-s9-read-window.txt` |
| `prune` keeps the last N runs per suite, across streams | READ `HistoryLedgerWriter.cs:315-318` | F7 |
| A blobless read downloads only the files it reads: 31.5 KB against 3.4 MB | RUN (HTTP origin) | `results-s6-http.txt` H6 |
| The consumer adds 49 run lines a day, about 0.5 MB | RUN (the ledger's own dates) | §2.5 |
| The default concurrency queue cancels an older pending job; `queue: max` holds 100 | VERIFIED | Appendix B |
| #73's README group loses a line with three lanes finishing together | INFERRED from the rule | F5 |
| Composite actions have no post steps | VERIFIED | Appendix B |
| `github.action_ref` is empty in `run:` | VERIFIED | Appendix B |
| Artifact names are unique per run; a re-run's same name is undocumented | VERIFIED, PARTIAL | Appendix B |
| Hidden folders are not uploaded without the flag | VERIFIED | Appendix B |
| The consumer's lanes share one job id | READ (reusable workflow called 18 times) | `ci-main.yml` |
| checkout v6.0.0 includes its credentials for `<workspace>/.git` only; v6.0.1 adds `worktrees/*` | VERIFIED (the source of both tags, PR #2327) | Appendix B |
| Under v6.0.0 the dogfood's worktree push fails, first run and later; v5 and v6.0.1 push | RUN (HTTP origin, checkouts made as each version makes them) | `results-s6-http.txt` H7 |
| A re-run reads its first attempt, and a persisting failure passes the gate | READ; RUN | `results-s7-rerun-reading.txt` |
| A pull request's build reads against its target branch; its own runs form a stream `<number>/merge` | READ `HistoryCommand.Maintenance.cs:101-102`, `HistoryRunContext.cs:138-141`, `CiMetadata.cs:47` | §4.6, Q2 |
| A wrong token, with the machine's helpers left in place, empties the machine's credential store; with `credential.helper` reset it does not | RUN (HTTP origin, a `store` helper) | `results-s6-http.txt` H1 e, f |
| A helper that waits holds the step; with the reset it is never asked | RUN (a helper that sleeps 8 s) | `results-s6-http.txt` H1 g, h |
| The token reaches no output and no file | RUN (42 jobs) | `results-s6-http.txt` H5 |
| Racing over HTTP loses no run; a read-only token and a hook stop the loop at once | RUN | `results-s6-http.txt` H3, H4 |
| The Windows image's git has `core.autocrlf=true` and the credential manager | PARTIAL (the installer's defaults and the image's script; not the image's file) | §2.4 |
| A copy of the folder checked out with `core.autocrlf=true` breaks bash; the folder's `.gitattributes` prevents it | RUN | `results-s10-machine-config.txt` M1 |
| A machine's pre-commit hook stops a plain commit and not the prototype's; a machine that cannot sign fails it | RUN | `results-s10-machine-config.txt` M2, M3 |
| The ledger writer writes `\n` on every platform | READ `HistoryLedgerWriter.cs:170-178`, `:240-256` | §4.9 |
| The repository's tarball is 17.5 MB, its zip 19.6 MB; the PlantUML jar is 11.3 MB of it | RUN (`git archive` at `5ca0878a`) | F13 |
| A runner downloads the whole repository for a subfolder action, per job; only popular first-party actions are cached | VERIFIED (runner source, action-versions README) | F13 |
| GitHub's archives honour `export-ignore` | PARTIAL (documented as `git archive`; others' reports for the runner) | F13, Q9 |
| A `v3` tag would run `release.yml` | READ | F14 |
| Hosted images carry .NET 10 and bash; macOS bash is 3.2.57 | VERIFIED | Appendix B |
| 2.2's reusable part is about 150 of 597 lines | READ (PR #73 at `e31b42fa`) | §2.3 |
| The tool's package is 7.5 MB and installs from nuget.org in 9.7 s into an empty cache | RUN (this container, not a runner) | §4.7 |
| `github-action-benchmark` re-applies on the new tip since v1.6.0, after rebases conflicted; none of seven actions reads the tip to tell a race from a refusal | VERIFIED (their source and changelogs) | §4.5, Appendix B |
| GitHub documents no read-after-push guarantee for another client | VERIFIED as far as the Spokes posts go | §11 |

---

## 14. Execution log (2026-09-29)

The owner asked for this plan "in full", on a worktree: the form D25 took, so D26 was taken as §10
recommends. Q1 (c): the logic is scripts in the action. Q2: pull requests are not recorded by default. Q3
(a): no pruning, and a warning from 50 MB, so S6 was not built. Q4: no floating tag. Q5: the actions it
calls are pinned to commits, with the version in a comment. Q6 stays apart from 2.3. Q7: quarantine and
aliases live on the data branch. Q8: fragments are kept seven days. Q9: no `export-ignore`. The owner then
asked for the work to go into `main` without a pull request. No version moved (§7): the changelog entry is
under `[Unreleased]`.

### 14.1 What was built

| Slice | What |
|---|---|
| S0 | The wiki's Cross-Run-History: "On CI" and "The recommended shape" rewritten around the action, and the manual recipe below it as "Without the action", run verbatim through the harness (§14.4) |
| S1 | `tests/Kronikol.Tests/Templates/`: `ActionDefinition` taken out of `PrReportLinkActionTests` with its API unchanged, then `BashProbe`, `GitProbe`, `ChildProcess`, `WorkflowExpression`, `ArtifactStore`, `CompositeActionRunner` (a composite `action.yml` run step by step, as the runner does), `BareOrigin`, `HttpOrigin` (`git http-backend` behind basic authentication, with a read-only token answered 403) and `MachineConfig`, each with facts of its own |
| S2, S3 | `templates/github-actions/kronikol-history/`: `read`, `gate`, `save` and `record`, the scripts under `scripts/`, `VERSION`, `.gitattributes` and the README (§8); the facts of §5.3 and §5.4 (`HistoryAction*Tests`, on the `HistoryWorld` fixture), each red before its script existed |
| §5.5 | Each of the 25 guards broken in turn fails the facts written for it: `HISTORY_ACTION_PLAN.harness/mutations.py`, the table in `results-mutations.txt` |
| S3b | `.github/workflows/history-action.yml`: the four phases on Ubuntu, Windows and macOS, one example project each, with `record-pull-requests: true`; six `record` calls racing on the lane's branch with copies of a leg's fragment under run ids of their own; the saved artifacts folded on a second branch; then `history verify` and a count of each branch's lines. It deletes both scratch branches first and writes nothing else. A static fact holds its calls to the phases' declared inputs |
| S4 | `ci-summary-preview.yml` reads, gates, saves and records through the phases by path, with `tool-command` running the tool built from the commit, on `kronikol-history` as before. `ci.yml` runs `Kronikol.Tests.Templates` on `windows-latest` and `macos-latest` as well (`runs-on: ${{ matrix.os \|\| 'ubuntu-latest' }}`, and `shell: bash` on its test step). A static fact holds the dogfood |
| S5 | This log, the changelog, `templates/README.md`, `README.md`, the wiki's CI-Artifact-Upload, roadmap 2.3, D26 and Appendix C, `PLANS_STATUS.md` and `CROSS_RUN_HISTORY_PLAN.md` §11. BreakfastProvider's switch waits for the first release tag that holds the folder (§6), which the owner has not asked for |

Also done here, because `PR_REPORT_LINK_PLAN.md` left it for the second template under
`templates/github-actions/`: CI's template-pack step fails when the package carries that folder. Proved
on a pack with a wildcard `Content` item, which it names.

### 14.2 Where the build differs from §4, and why

- **The tool needs a .NET 10 SDK, and §4.7's private runtime is gone.** Measured: an SDK 8 cannot
  `dotnet tool install` `Kronikol.Tool`, which targets `net10.0` only ("Settings file
  'DotnetToolSettings.xml' was not found", in the `sdk:8.0` image). So `tool.sh` looks for an SDK of 10
  or later in `dotnet --list-sdks` and, when there is none, fails naming `actions/setup-dotnet` and
  `tool-command`. It installs with `--tool-path` under `RUNNER_TEMP` and runs the tool with
  `DOTNET_ROOT` set to that SDK's root for its own calls only, so the job's `PATH` and environment do not
  change.
- **The scripts know no forge but through `lib.sh`.** They read neutral `KRONIKOL_HISTORY_*` variables,
  which each `action.yml` sets from its inputs and `github` context. `scripts/lib.sh` is the one file that
  writes workflow commands, `GITHUB_ENV`, `GITHUB_OUTPUT` and `GITHUB_STEP_SUMMARY`, masks, and wraps the
  tool's output in stop-commands. That is `AZURE_DEVOPS_PARITY_PLAN.md` §4.5's ask of 2.3's interface, and
  2.4 replaces one file.
- **From D27's asks, built in although D27 is not taken:** `[skip ci]` in every ledger commit
  (`Record run <id>:<attempt> (<sha>) [skip ci]`), and the header key emptied before the token is set
  (its F13), because both cost a line and hold on GitHub as well.
- **`record` gained `warn-ledger-mb`** (default 50, Q3 (a)), and its job summary gives the ledger's size.
- **`save` takes directories, not a glob.** `path` is one directory per line (default `.`). The script
  stages each `History.run.json` under its path below the workspace, leaving out `.git` and
  `runs/.incoming-*`, so `upload-artifact` gets one plain tree (`include-hidden-files: true`) and
  `record` can still tell reports directories apart.
- **`record` asks the remote which branch is its default** (`ls-remote --symref origin HEAD`) and
  refuses to write to it, rather than taking an input that could be wrong.
- **The token-on-a-command-line fact reads git's trace2 events.** Git for Windows' `bin/bash.exe`
  puts `/mingw64/bin` and `/usr/bin` first on `PATH`, so a `git` shim never wins there.
  `GIT_TRACE2_EVENT` records each git process's whole argv, `-c` included, on every platform.
- **Origins in the facts allow filters.** A blobless read fetches blobs lazily, which needs
  `uploadpack.allowFilter` and `uploadpack.allowAnySHA1InWant` on the origin; github.com allows both.
- **S3b ran after the merge.** Its done-when puts the lane on the pull request that adds the action,
  before anything merges. The owner asked for `main` without a pull request, so the lane was dispatched
  on `main` right after the push (§14.5).

### 14.3 Found on the way

- **F17. `kronikol history record` folded the files of a rotation a killed process left behind.**
  (RUN, fixed in the tool, `[Unreleased]`.) A rotation stages a retained run in
  `runs/.incoming-<name>/` and renames it when it is whole, and `ReportFolders`' contract is that such a
  folder is never a retained run. Given a directory, `record` read every `History.run.json` under it,
  those folders included, so a staged copy of the run on top was folded as an earlier attempt: every
  scenario read two attempts ("22" for "--"), and a directory holding only a staged folder was recorded.
  A search now leaves what it finds under such a folder alone and says how many files it left; a staged
  folder named as an input is still read, as `kronikol merge` reads a retained run it is named. Facts:
  three in `HistoryCommandTests`, two red on the old code and one control. `record.sh` and `save.sh`
  keep their own exclusion, because `VERSION` (3.33.0) predates the fix.
- **An apostrophe inside `${VAR:?message}` opens a quote in bash.** `bash -n` caught one before any fact
  ran; `Every_script_parses_under_bash` now parses each script, and the mutation table breaks one.
- **The mutation driver misread its own runs twice.** It looked for xUnit's `[FAIL]` marker, which the
  console logger prints only at normal verbosity, so every row read "not held"; then it missed a theory
  case's arguments after the fact's name. Both were fixed and the affected row re-run alone before the
  table was read.
- **The lane's race step would have failed on github.com.** It gave each racer's copy of a fragment its own
  run id with a pattern for `"id":"gh:..."`, and the writer indents, writing `"id": "gh:..."`, so `grep`
  found nothing and the step exited 1. No fact ran the step. One now does, on a fragment the library writes
  (`The_lanes_racers_give_a_real_fragment_a_run_id_of_their_own`, red on the old pattern), and the pattern
  allows the space and keeps the rest of the file byte for byte.
- **`download-artifact` v8.0.1 with a pattern that matches nothing succeeds and creates nothing,** so
  `record` checks for the directory rather than trusting the step's outcome.

### 14.4 Verification

- **Facts.** 152 facts under `tests/Kronikol.Tests/Templates/` besides 2.2's, green on Windows (Git for
  Windows' bash and git 2.53.0.windows.3, 1 min 47 s). On Linux, in the `dotnet/sdk:10.0` image (git
  2.43.0, bash 5.2.21), the folder's facts and `HistoryCommandTests` passed, 177 of them, with 23 of
  2.2's skipped because the image has no Node; the static facts for the dogfood and the lane's race step
  came later and ran on Windows. The whole of `Kronikol.Tests` passed on Windows: 6,266 facts, one skipped.
- **Mutations** (§5.5): 25 of 25 guards held (`results-mutations.txt`).
- **S0's recipe and the action's record script through the harness:** run by the session that took S0 (kronikol-2d) in the `dotnet/sdk:10.0` container, against the released
  Kronikol.Tool 3.33.0 (the version `VERSION` pins). The manual recipe's two `run:` blocks were extracted from the
  page's own text (`extract_recipe.py`, and the page and `recipe_manual_read.sh` and `recipe_manual_record.sh`
  agree), and the action's `record.sh` ran through `action_record.sh`, which sets what `record/action.yml` sets.
  - `s1` W5, the manual recipe: a first run made the branch with the ledger and `merge=union`
    (`Record run 101:1 (8d06dfc) [skip ci]`); a later run's read named `KRONIKOL_HISTORY` and its record appended;
    the record job re-run recorded nothing and committed nothing; a quarantine a person put on the branch was read
    by the next run and kept by its record; a record job with nothing downloaded passed; `history verify` was clean
    throughout. W1 to W4 printed what the first pass printed.
  - `s2b`, ten races of six writers on an existing branch and ten on a first run, for each recipe: the manual
    recipe, the prototype and the action's `record.sh` each lost no run, failed no job and duplicated no roster
    line, after recording again on 78 and 92, 88 and 93, and 80 and 91 lost races.
  - `s6` over smart HTTP with a token: the manual recipe and the action each lost 0 of 18 runs racing on a first
    run and 0 of 18 on an existing branch, with no duplicate; a read-only token (403) and a refusing hook each
    stopped them after one push, the action's error quoting the server's refusal; no token or header reached an
    output line or a file of 42 jobs (H5); and the manual read step read the ledger through the blobless fetch
    with the right token, and with a wrong one warned and let the tests run without history (H8).

### 14.5 On GitHub

The push of `118a3481` to `main` started no workflow. Its message named the action's ledger commit marker,
in square brackets, and GitHub skips the push workflows when a pushed commit's message holds that marker
anywhere, not only as a directive. So a commit message here never quotes it; the push after it started them.
The live lane, dispatched on `118a3481`, and CI and the dogfood on the push after it are recorded here once
they have run.

---

## Appendix A. The harness

[`HISTORY_ACTION_PLAN.harness/README.md`](HISTORY_ACTION_PLAN.harness/README.md) lists every script, what
it answers and its output file, and how to re-run them: the tool built from this repository, and
BreakfastProvider's ledger fetched at `39bd177`. `prototype_record.sh` is the reference implementation of
§4.5 that S2's `record.sh` starts from, and the recipes are kept verbatim as the controls the mutation
facts are red against. The second pass added:

- `s6_http.sh` with `githttp.py`, the HTTP origin: the token, the machine's credential helpers, exit codes,
  races, refusals, the token's hygiene, the blobless read, and F9's checkouts.
- `s7_rerun_reading.sh` (F10), with `make_report.py`.
- `s8_scaling.sh` (F6, F7), with `grow_ledger.py` and `measure.py`.
- `s9_read_window.sh` (Q3 (d)).
- `s10_machine_config.sh` (F16, §4.9).

`prototype_record.sh` gained its token and git settings in the second pass, after `s2`, `s2b` and `s5`
ran. Those lines touch nothing a `file://` origin uses, and `s6` re-ran the races and refusals with them.

## Appendix B. Platform sources (read 2026-09-27, both passes)

Read by sub-agents of this session through docs.github.com's Markdown API, raw source files and
github.blog. Issues were read as rendered pages, so where a thread was not visible the mark is PARTIAL.

| Topic | Source | Quote |
|---|---|---|
| Composite `runs` | docs.github.com, metadata syntax; runner `action_yaml.json`; issue actions/runner#1478 | `"composite-runs": {"mapping": {"properties": {"using": …, "steps": …}}}`; #1478 "Support pre and post steps in Composite Actions", open, "Future" |
| Contexts in a composite | runner `action_yaml.json`, `CompositeActionHandler.cs`; docs, contexts | `"step-with": {"context": ["github","inputs","strategy","matrix","steps","job","runner","env", …]}`; "The `secrets` context is not available for composite actions" |
| `action_ref` | docs, contexts; runner `ActionRunner.cs`; issues #2473 (open), #2525 (closed, not planned) | "Do not use in the `run` keyword. To make this context work with composite actions, reference it within the `env` context" |
| Artifact names | actions/upload-artifact README | "Artifact names must be unique since each created artifact is idempotent"; `overwrite`: "If false, the action will fail if an artifact for the given name already exists" |
| Upload paths | upload-artifact README, `src/shared/search.ts` | "the path hierarchy will be preserved after the first wildcard pattern"; hidden files "beginning with `.` or files within folders beginning with `.`" are excluded by default |
| Versions | upload-artifact v7.0.1 (2026-04-10), v6.0.0 notes; download-artifact v8.0.1 (2026-03-11) | "requires a minimum Actions Runner version of 2.327.1"; not supported on GHES |
| Download layout and re-runs | download-artifact `download-artifact.ts`; github.blog 2024-02-12; issues #426, #486, #585 | `isSingleArtifactDownload \|\| inputs.mergeMultiple \|\| artifacts.length === 1 ? resolvedPath : path.join(resolvedPath, artifact.name)`; "the action will only be able to download from the current workflow run and any previous run attempts" |
| Concurrency | docs, control workflow concurrency | "By default, any existing `pending` job or workflow in the same concurrency group will be canceled"; "`max`: Up to 100 jobs or workflow runs can be `pending`" |
| `GITHUB_TOKEN` | docs, GITHUB_TOKEN; events that trigger workflows; securely using `pull_request_target`; rulesets | "events triggered by the `GITHUB_TOKEN` will not create a new workflow run"; "read-only permissions in pull requests from forked repositories"; "On November 2, 2026, GitHub will enforce the default policy" |
| Marketplace | docs, publishing in GitHub Marketplace; github/docs commit of 2026-05-25 (#43229) | "Each repository must contain a single action metadata file (`action.yml` or `action.yaml`) at the root. Repositories may include other actions metadata files in sub-folders, but they will not be automatically listed"; "Remove no workflow files prerequisite" |
| Runner images | actions/runner-images README and image readmes (ubuntu 24.04 20260920, windows 2025 20260907, macOS 26 arm64 20260907) | ".NET Core SDK: … 10.0.401"; "Bash 3.2.57(1)-release"; "When specifying a bash shell on Windows, the bash shell included with Git for Windows is used" |
| checkout credentials | actions/checkout README (v6), CHANGELOG v6.0.1 | "`persist-credentials` now stores credentials in a separate file under `$RUNNER_TEMP`"; "Add worktree support for persist-credentials includeIf" |
| `ls-remote` | git-scm.com, git-ls-remote | "--exit-code Exit with status "2" when no matching refs are found" |

**The second pass's sources.** Prior-art code was read at the tip of each default branch, and is cited
by short commit.

| Topic | Source | Quote |
|---|---|---|
| checkout's credentials | actions/checkout `src/git-auth-helper.ts` at v6.0.0 and v6.0.1; PR #2327 (merged 2025-12-02, fixes #2318) | `includeIf.gitdir:${gitDir}.path`; v6.0.1 adds only `includeIf.gitdir:${gitDir}/worktrees/*.path`; "Write placeholder to the separate credentials config file using git config. This approach avoids the credential being captured by process creation audit events, which are commonly logged." |
| checkout's git environment | `src/git-command-manager.ts` at v6.0.0, unchanged at v7.0.1 | `GIT_TERMINAL_PROMPT: '0', // Disable git prompt`; `GCM_INTERACTIVE: 'Never' // Disable prompting for git credential manager` |
| checkout v7 | releases (v7.0.0 2026-06-18, v7.0.1 2026-07-20, "Latest"); CHANGELOG; github.blog changelog 2026-06-18 | "Block checking out fork PR for pull_request_target and workflow_run"; the opt-out input `allow-unsafe-pr-checkout`, default `false`, backported to v6.1.0 and v4.4.0 as "[BREAKING]"; `git-auth-helper.ts` otherwise unchanged |
| Git on the Windows image | runner-images `images/windows/scripts/build/Install-Git.ps1`; Git for Windows `installer/install.iss` | Installer arguments `/o:PathOption=CmdTools /o:BashTerminalOption=ConHost /o:EnableSymlinks=Enabled /COMPONENTS=gitlfs`, no CRLF or credential option; the installer's defaults end in `GitSystemConfigSet('core.autocrlf', …)` with `true` and `GitSystemConfigSet('credential.helper','manager')`; the image script: "# Disable GCM machine-wide" (`GCM_INTERACTIVE=Never`) |
| CRLF on Windows checkouts | actions/checkout #135, #226; runner-images #32 | "When using Windows, these seem to be converted"; "The checksum file is by default checked out with CRLF line endings" |
| Artifacts per job | upload-artifact README | "Within an individual job, there is a limit of 500 artifacts that can be created for that job." |
| Partial clone | github.blog, "Get up to speed with partial clone and shallow clone" (2020-12-21); checkout `git-command-manager.ts` at v7.0.1 | "On github.com and GitHub Enterprise Server 2.22+, there are two options available: … Blobless clones: git clone --filter=blob:none <url>"; "the server can choose to deny your filter and revert to a full clone"; checkout passes `--filter=${options.filter}` beside `--depth=${options.fetchDepth}` |
| The action download | runner `src/Runner.Worker/ActionManager.cs`; actions/action-versions README | `#if OS_WINDOWS … ZipballUrl … #else … TarballUrl`; "// We are running at the start of a job" before the actions directory is deleted; action-versions "only caches the most popular first party actions" |
| `export-ignore` | docs, downloading source code archives; git-scm, git-archive; paradedb/paradedb#6514 | "These snapshots are generated by the `git archive` command"; "attributes are by default taken from the .gitattributes files in the tree that is being archived"; "Excluding all of `.github` removes the action from that archive" |
| Fractional `sleep` | GNU `coreutils.texi`; Apple `shell_cmds-329` `sleep.1`; Git for Windows `make-file-list.sh` | "GNU sleep also accepts … floating-point numbers"; "Intervals can be written in any form allowed by strtod(3)"; coreutils is shipped outside MinGit |
| Prior art: re-apply | benchmark-action/github-action-benchmark `84ec6ea`: `src/write.ts`, `src/git.ts`, CHANGELOG, #310 | `return ['[remote rejected]', '[rejected]'].some((l) => err.message.includes(l));`, then `reset --hard HEAD~1` and a recursive retry, ten times; v1.6.0: "Now this action retries entire process… Previously this action tried to rebase the local onto the remote but it sometimes failed due to conflicts"; v1.22.2: full clones "widen the window for push contention with other runs" |
| Prior art: the rest | JamesIves/github-pages-deploy-action `8fd6702` (`action.yml`, `src/git.ts`, PR #1054, #1000, #1052); py-cov-action/python-coverage-comment-action `fbf0705` (`storage.py`, README, #561); peaceiris/actions-gh-pages `09d8f31` (#759, #1078); simple-elf/allure-report-action `385367c` (#50); stefanzweifel/git-auto-commit-action `9264814` (README, #170); EndBug/add-and-commit `9532192` (PR #764, #321, #513); jgehrcke/github-repo-stats `ff48771` (`entrypoint.sh`, #9) | JamesIves #1000: `[remote rejected] … (cannot lock ref 'refs/heads/gh-pages': is at … but expected …)`; #1052: "both force-push their changes and one of the commits will be destroyed"; py-cov #561: `! [remote rejected] … (push declined due to repository rule violations)`; git-auto-commit README: "No `git pull` when the repository is out of date with remote" |
| Read after a push | github.blog, "Building resilience in Spokes" (2016), "Stretching Spokes" (2017), "Introducing DGit" (2016) | "No updates to a repository—pushes, renames, edits to a wiki, etc.—are accepted unless a strict majority of the replicas can apply the change and get the same result"; "it routes reads to the closest replica that is in sync". No statement about another client's read right after a push |

**Also reported that day, and not yet used by the design.** Each bears on S2 to S5 or on a question in §10.

- **Sibling references with `$/`** (github.blog changelog, 2026-07-30; runner 2.336.0 or later): an action
  can call a sibling action at the commit it is running from. The four phases could share one `tool`
  phase this way once every runner a consumer uses has it. GHES and self-hosted runners lag, so the
  scripts share code through `$GITHUB_ACTION_PATH/../scripts/` instead (§4.1).
- **Organisations can require actions to be pinned to a commit** (since August 2025). A consumer may have
  to reference the action by SHA, which F12 already allows for: the version rides in `VERSION`, not in
  the ref.
- **`queue: max` cannot be combined with `cancel-in-progress: true`**, and concurrency group names are
  compared case-insensitively (docs, control workflow concurrency).
- **`actions/checkout`'s current major is v7** (v7.0.1, 2026-07-20). Under `pull_request_target` and
  `workflow_run` it refuses a fork's code. The plan's example uses `@v7`, as 2.2's plan moves #73's README
  to it (`PR_REPORT_LINK_PLAN.md` F7); this repository's own workflows still pin `@v5`. The action
  depends on none of it (F9).
- **Pull-request events created with `GITHUB_TOKEN`** have started in an approval-required state since
  June 2026. That is irrelevant to pushes to the data branch, which start nothing.
- **`pull_request_target` runs the default branch's workflow** since 2025-12-08.
