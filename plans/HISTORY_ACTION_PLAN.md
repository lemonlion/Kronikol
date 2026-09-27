# History action plan: cross-run history on CI without copied YAML (roadmap 2.3)

**Date:** 2026-09-27 · **Repo version:** 3.31.9 (`main` at `dc619d35`) · **Status: a plan. Nothing
built, NOT green-lit; needs D25** (the owner's questions are §10). Roadmap item **2.3**, stage 2, track
C. It comes after 2.2 (PR #73), whose branch it starts from, and before 2.4 (Azure DevOps), which builds
to the interface in §4.2. It changes no package, so it bumps nothing (§7). The scripts behind every
RUN mark are in [`HISTORY_ACTION_PLAN.harness/`](HISTORY_ACTION_PLAN.harness/README.md), with their
output.

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
applies to 2.2's README. F6: `kronikol history record` reads the ledger twice per suite, about 5 s for
eighteen suites today and growing. F7: the ledger's tip grows by about 0.5 MB a day at the consumer, and
every lane fetches it. F8: the dogfood's artifact naming would collide under reusable workflows, and a
reports directory in a hidden folder uploads nothing. F9: the recipes depend on how `actions/checkout`
stores credentials, which changed in v6. F10: a re-run reads its own first attempt as history. F11: the
gate exits 2 when there is no ledger yet. F12: an action cannot learn its own release. F13: referencing
a folder of this repository downloads the whole repository, 17.5 MB, per job. F14: a floating major tag
would run a release.

**The design** (§4). Four composite actions in one folder,
`templates/github-actions/kronikol-history/`: **read** (before the tests), **gate** (after them,
optional), **save** (the fragments as an artifact) and **record** (in a job after every test job). Each
keeps its own git repository under `RUNNER_TEMP` and authenticates with a `token` input, so none depends
on the job's checkout. Record never rebases and never force-pushes. A lost race is recorded again on the
new tip. A refusal while the branch stands still is reported at once, not retried. The tool is
installed to a path of its own, at the version the folder ships with. The logic lives in bash scripts
written to run unchanged on Linux, Windows and macOS, so 2.4 can carry the same code.

**Slices** (§6). S0 makes the wiki's recipe true, with no code, and can happen now. S1 is the shared
test scaffolding, on 2.2's rebased branch. S2 is read, save and record. S3 is the gate. S4 switches the
dogfood to the action and adds the Windows and macOS legs. S5 is the documentation and the consumer's
switch. **No bump at any slice.** A consumer can reference the action from the first release tag that
contains it.

---

## 1. How far each claim was checked

Marks as in `ROADMAP.md`: **RUN** (a command was executed for this plan), **READ** (the source was read
for it), **VERIFIED** (a primary source was read on 2026-09-27: docs.github.com, the runner, the
`actions/*` repositories and the runner-images readmes, read through the web by a sub-agent of this
session and quoted in Appendix B), **PARTIAL** (the primary source leaves part of it open), **INFERRED**
(reasoned from two facts, stated by neither).

**RUN means the harness.** Its unit is a bare "origin" on disk, with fresh checkouts made the way
`actions/checkout@v5` makes them. Git runs with no global or system configuration, because a hosted
runner has none. The tool is built from this repository at 3.31.9 (SDK 10.0.401, Release,
framework-dependent). Fragments are built from BreakfastProvider's real ledger lines, its
`kronikol-history` branch at `39bd177`: 664 lines, 18 suites. The machine is a Linux container with 4
cores and git 2.43.0. **Not run:** a GitHub-hosted runner, Windows, macOS, a network between the fold
and its origin, `actions/upload-artifact` and `actions/download-artifact` themselves.

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
| `actions/checkout` v6 keeps credentials in `$RUNNER_TEMP`, wired in with `includeIf.gitdir`. Worktree support came in v6.0.1 | F9 |
| `upload-artifact` v7.0.1 (2026-04-10) and `download-artifact` v8.0.1 (2026-03-11) run on node24 (runner 2.327.1 or later) and are not supported on GHES. Node 20 left the runners on 2026-09-23 | §4.8 |
| `git ls-remote --exit-code` exits 2 when no ref matches (RUN too: 2 absent, 0 present, 128 unreachable, `results-s5-edges.txt`) | The only way to a new branch (§4.5) |

### 2.5 What a fold costs on the consumer's ledger (RUN, `results-s4-cost.txt`)

The consumer's ledger at `39bd177` is 7,112,729 bytes: 1 header, 18 rosters (586,639 bytes, about 32 KB
each), 17 shapes lines, and 628 run lines (6,275,481 bytes, about 10 KB each). It holds 37 CI runs from
2026-09-14 to 2026-09-27, 12.7 days, which is 49 run lines a day. gzip -9 takes it to 1,005,838 bytes.

| Measured | |
|---|---|
| `kronikol history record` of one run of 18 lanes, five in a row | 4.78, 4.88, 5.04, 5.21 and 5.24 s as the ledger grew from 7,309,790 to 8,098,034 bytes |
| What one run of 18 lanes adds to the tip | 197,061 bytes |
| What one run adds to the branch's packed objects | 28 KB (1,032 to 1,172 KB over five runs, after `git gc`) |
| `kronikol history show` on the 8.1 MB ledger | 0.51 s |

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
action needs no group, since its loop is the lock, and its README must never show one without
`queue: max`.**

The same rule applies to 2.2. #73's README says "`cancel-in-progress` stays off, so the second job waits
instead of being cancelled". That is true for two lanes finishing together and false for three: the
third cancels the second, and the second lane's line is lost. For 2.2: add `queue: max` to its README's
group and to its drift fact (§9).

**F6. `kronikol history record` reads the whole ledger twice per suite** (READ; RUN). `LastFullRoster`
reads the ledger once per folded run (`HistoryCommand.cs:304`). `HistoryLedgerWriter.AppendLocked` reads
it again to look for a duplicate (`HistoryLedgerWriter.cs:201`).

- Eighteen suites on 7.3 MB take 4.78 s, and each MB adds about 0.58 s (§2.5).
- At the consumer's 49 run lines a day (about 0.5 MB), a year brings a fold to about 110 s.
- The loop in §4.5 pays that again on every lost race.

The fix belongs to the tool: read once per invocation and look for duplicates in that one pass. That is
a patch in track A, not part of 2.3 (§9). It is recorded here because §4.5's choice multiplies it.

**F7. The tip grows without bound, and every lane fetches all of it** (RUN; READ). The consumer's ledger
went from nothing to 7.1 MB in 12.7 days (1.0 MB gzipped). Git packs the history well: 28 KB a run.
But every lane's depth-1 fetch and every fold read the whole tip.

`HistoryWindow` bounds what a run parses, not the file. `kronikol history prune` shrinks the tip at the
cost of a larger pack (the wiki measured 55% larger over 400 commits). A year at today's rate is about
180 MB raw, roughly 25 MB gzipped, per lane per run (INFERRED, linear). Retention is Q3. 9.4's P1 is
where years of trends are meant to live: monthly view files on this same branch.

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

**F9. The recipes depend on how `actions/checkout` stores credentials** (VERIFIED; the effect on the
recipes is INFERRED, not run). Both working copies push from a `git worktree` of the job's checkout.
`checkout` v6 moved its credentials into `$RUNNER_TEMP`, keyed on the checkout's `gitdir`, and only
v6.0.1 extended them to worktrees. The dogfood and the consumer pin `checkout@v5`.

An action that keeps its own repository and sets its own authorization depends on none of this:

- the checkout's version,
- `persist-credentials: false`,
- whether the job has a checkout at all, as when a test job runs a downloaded build.

**F10. A re-run of a failed job reads its own first attempt as history** (READ; the gate's outcome
INFERRED). A run's history ends at the first line with its exact id (`HistoryLedger.cs:121-125`). A
re-run is `gh:N:2` (`HistoryRunBuilder.cs:174`), and `gh:N:1` was recorded by attempt 1's fold, which
runs `!cancelled()`. So a failure that fails again on the re-run reads as already failing, and
`new-failures` does not trip: re-running a red job turns it green without a fix.

For the next push this is the design ("a gate that fails on any red cannot tell a regression from the
test that has flipped for a month"). For a re-run of the same commit it is arguably not. Changing it is
a change to what a report says, the owner's call (Q6), and it is not 2.3's. The action's README states
it.

**F11. The gate exits 2 when there is no ledger yet** (READ, `HistoryCommand.Maintenance.cs:74`,
`mustExist: true`). On a new repository the first run's gate step is a usage error. The consumer carries
a second step for this: "Tests failed and no ledger to read them against". The gate phase folds that in
(§4.6).

**F12. An action cannot learn its own release** (VERIFIED). `github.action_ref` is empty in `run:`, and a
caller pinning a commit gives a commit. So the tool version the action installs rides in the action's
folder, and a fact holds it to `Directory.Build.props` (§4.7).

**F13. Referencing a folder of this repository downloads the repository** (the layout VERIFIED; the size
RUN, the transfer INFERRED). A runner fetches an action's whole repository at the ref into
`_actions/<owner>/<repo>/<ref>/`. This repository's archive at `dc619d35` is 17,526,523 bytes (3,140
files).

A consumer run with eighteen lanes and a fold job would fetch it 19 times, about 333 MB per run.
Copying the folder into the consumer's `.github/actions/` fetches nothing. 13.2's own repository would
end it.

**F14. A floating major tag would run a release** (READ, `release.yml`). The release workflow runs on
`push: tags: ['v*']`: it packs with the tag's text as the version, pushes to NuGet with
`--skip-duplicate`, and creates a GitHub Release. A `v3` tag would do all three with "3". So there is no
`@v3` reference unless the trigger narrows first (Q4).

---

## 4. Design

### 4.1 Four actions in one folder

```
templates/github-actions/kronikol-history/
  README.md              the page: both workflow shapes, every input and output, the limits
  VERSION                the release this folder ships in, and the tool version it installs (F12)
  read/action.yml        before the tests
  gate/action.yml        after the tests, optional
  save/action.yml        after the tests: the fragments as an artifact
  record/action.yml      in a job after every test job: fold and push
  scripts/read.sh  gate.sh  record.sh  tool.sh      the logic; no GitHub-only variable inside
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
      - uses: actions/checkout@v5
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
That is roughly 167 lines to 38.

### 4.3 read

1. A repository of its own at `$RUNNER_TEMP/kronikol-history-read`, with `origin` set to
   `$GITHUB_SERVER_URL/<repository>.git`. The token goes in as `http.extraheader`: a basic
   `x-access-token` pair, base64-encoded, masked with `::add-mask::` before first use, never in a URL
   and never echoed. This is `actions/checkout`'s own mechanism, on a repository nothing else uses
   (F9).
2. `git ls-remote --exit-code origin refs/heads/<branch>`:
   - 2 means no branch yet: `found=false`, and a notice that the first `record` starts it.
   - Anything other than 0 or 2 is a warning naming the exit code, and the run continues without
     history.
3. `git fetch --depth=1` of the tip. `git show` then writes `history.jsonl`, `quarantine.json` and
   `aliases.json` into `$RUNNER_TEMP/kronikol-history/`. A companion the branch does not hold is not
   there afterwards.
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

**The loop.** This is `prototype_record.sh`, which ran in the race and refusal scenarios (`s2`, `s2b`,
`s5`):

```
for attempt in 1..attempts:
    base = ls-remote(branch)                        # a commit, or "absent" (exit 2), or an error: stop
    a fresh repository in RUNNER_TEMP
    if base is absent:  orphan branch; kronikol history init (header and .gitattributes); README
    else:               fetch base at depth 1; check it out
    kronikol history record <fragments> --history history.jsonl
    nothing staged -> "nothing new to record", exit 0                     # every run a duplicate
    commit "Record run <run id>:<attempt> (<sha7>)"                         # the dogfood's message
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
- **The price is F6:** each lost race costs one more full `record`.

**Why eight attempts are enough.** A push is refused in a race only because another writer's push
landed, and a writer that has landed stops. So with N folds finishing together, no fold can lose more
than N - 1 times, and eight attempts cover eight at once. A ninth has its job fail with the error, and
a re-run records it, because recording again is safe.

**Why a refusal while the branch stands still is not retried.** A ruleset, a hook, a token without
`contents: write` or a fork's read-only token does not go away in a second. In the harness a
`pre-receive` hook refusing the branch made the prototype stop after one push, naming the refusal (E2).
Keying on the branch's tip, not on git's message, is what makes this independent of the server's
wording. The tip is compared after the pause, so a competing push that still held the ref's lock has
landed before the comparison.

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
- **Its limit** is F10, stated in the README: a re-run reads its first attempt.

### 4.7 The tool

- **Version.** `VERSION` holds the release, and the release process bumps it with `Directory.Build.props`
  (a fact holds the two equal, §5.3). A tag therefore installs its own tool. A reference to `main`, or
  to a tag in the minutes before `release.yml` has published the packages, fails the install naming the
  version (documented).
- **Install.** `dotnet tool install Kronikol.Tool --version <v> --tool-path
  "$RUNNER_TEMP/kronikol-tool/<v>"`, reused if a step in the same job already installed it. Not
  `--global`: a global install fails when another version is installed, and changes the job's `PATH`.
- **Runtime.** Hosted images carry .NET 10 (§2.4). When `dotnet --list-runtimes` has no
  `Microsoft.NETCore.App 10.`, `dotnet-install.sh --runtime dotnet --channel 10.0` installs one into
  `$RUNNER_TEMP/kronikol-dotnet`. `DOTNET_ROOT` is set for the tool's own calls only, never in
  `GITHUB_ENV`, so the caller's later `dotnet` steps are untouched.
- **`tool-command`.** When set, it replaces the install. Kronikol's own dogfood builds from source (S4):
  `dotnet run --project src/Kronikol.Tool/Kronikol.Tool.csproj --framework net10.0 -c Release --`.

### 4.8 Credentials and trust

- `token` defaults to `github.token`. Read and gate need `contents: read`, and record needs `contents:
  write`, on the job that calls it.
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

CI runs the behaviour facts on Windows and macOS as well (S4). The harness ran Linux only.

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

The runner's own facts: each expression form, a rejected unknown expression, env isolation, and the
artifact rules.

### 5.3 Static facts, per `action.yml` and the README (S2, S3)

- Every `run:` step declares `shell: bash`. No `run:` body contains `${{`.
- Every declared input reaches its step's `env`, and no script reads an undeclared input: 2.2's wiring
  fact, generalised.
- Every `uses:` is pinned to a 40-character commit (Q5).
- No script pushes with `--force` or a `+` refspec.
- Every expression is one the runner evaluates.
- `VERSION` equals `Directory.Build.props`'s `<Version>`.
- **README drift:**
  - Each workflow in the README calls each phase with declared inputs only and every required one.
  - The job that calls `record` has `contents: write`. In the two-job shape the test job does not.
  - `save` and `record` run `!cancelled()`.
  - No job that calls `record` has a concurrency group without `queue: max` (F5).

### 5.4 Behaviour facts, each red before its code exists

| Phase | Fact |
|---|---|
| read | `Without_a_history_branch_read_finds_nothing_and_the_run_goes_on_without_history` |
| read | `Read_copies_the_ledger_and_both_companions_side_by_side_and_names_the_ledger` (F2; red against the dogfood's read) |
| read | `A_companion_the_branch_does_not_hold_is_not_in_the_copy` |
| read | `An_unreachable_origin_is_a_warning_and_never_fails_the_job` |
| read | `Read_leaves_the_checkout_untouched` (no ref, no `FETCH_HEAD`, no config written in the workspace) |
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
| **S0** | **The wiki's recipe made true.** "The recommended shape" rewritten from the prototype: fetch with the companions; a fold that makes the branch on the first run (`kronikol history init`), sets an identity, records, and on a lost race records again on the new tip (eight attempts, a pause); where the companions live on a data branch. It is marked as the manual route until the action ships | nothing: it describes shipped tools, so the wiki may publish it at once | The new text, run verbatim through `s1_wiki_recipe.sh` (as W5) and `s2b_race_trials.sh`, passes the first run, the later runs and ten races |
| **S1** | The scaffolding (§5.2), on 2.2's rebased branch | 2.2 rebased | 2.2's 17 facts green through the extracted `ActionDefinition`; the runner's and `BareOrigin`'s own facts green |
| **S2** | `read`, `save` and `record`, `scripts/`, `VERSION`, the README; §5.3 and §5.4's facts for the three; §5.5's mutations | S1 | Every fact red first, then green; the mutation table filled in; the harness scenarios re-run against the action's `record.sh` |
| **S3** | `gate` and its facts | S2 | As S2 |
| **S4** | `ci-summary-preview.yml` switches to the four phases by path, with `tool-command` building from source, the same branch and the same commit message. The Windows and macOS CI entry is added | S2, S3 merged, 2.2 merged first (roadmap §5) | Three pushes to `main` recorded, one line per suite each, `history verify` clean, continuity with the branch's earlier lines (`history show`). The new CI entry green |
| **S5** | Documentation (§8). The consumer switches after the first release tag that holds the folder | S4, a release tag | A week of BreakfastProvider's CI: every run recorded once per lane (`history show` count against the Actions run list), the gate's summaries unchanged, about 167 lines down to about 38 |

**Effort.** S0 about an hour. S1 a day. S2 two days. S3 half a day. S4 half a day. S5 half a day plus
the week of runs. That is the roadmap's "days".

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
  data branch (Q7), and reading the branch locally.
- **Wiki `Cross-Run-History`**: "On CI" and "The recommended shape" rewritten around the action (after
  S2 merges: roadmap §5, the wiki publishes on push). S0's manual recipe stays below it as "Without the
  action". A paragraph on retention and size (F7).
- **Wiki `CI-Artifact-Upload`**: a line pointing at `save`, and why fragments get their own artifact.
- `templates/README.md`: a section beside 2.2's.
- Root `README.md`: one sentence after the `kronikol history gate` line.
- `CHANGELOG.md` `[Unreleased]`.
- `ROADMAP.md` 2.3 and D25, and this plan's row in `PLANS_STATUS.md`.
- `CROSS_RUN_HISTORY_PLAN.md` §11 names the dogfood's recipe: one line saying S4 replaced it.

---

## 9. What this hands to others

| To | What |
|---|---|
| **2.2** (PR #73) | F5: its README's group needs `queue: max`, and its drift fact should assert it. The extracted `ActionDefinition` (S1) |
| **2.4** (Azure DevOps) | §4.2's interface. The scripts are forge-neutral: the Azure template carries byte-identical copies held by a drift fact, as `SkillDriftTests` holds the two `query.py` copies, unless Q1 moves the logic into the tool. What differs there is credentials (`System.AccessToken`, the build identity's Contribute permission) and artifacts (`PublishPipelineArtifact`), which are the store plan's REFERENCE claims that 2.4 checks first |
| **The tool** (track A, a patch) | F6: read the ledger once per `record` and look for duplicates in that pass. F10 is the owner's question (Q6) |
| **9.4** (dashboard) | `record` gains `view: true` once `record --view` exists. Recording again regenerates the view, so the dashboard plan's §4.6 conflict loop is not needed. F7's growth is P1's retention question |
| **9.6** (alerting) | The gate's `result` output and its summary are where per-verdict events hook in |
| **13.2** (Marketplace) | One root `action.yml` per repository, subfolder actions not listed, workflow files allowed since 2026-05-25 (§2.4). So the listing is a small repository whose root action is `record`, or a root `action.yml` here. The first ends F13 as well |
| **14.4** (monorepo) | The tag split changes the action's reference form |

---

## 10. Open questions for the owner (D25), with recommendations

1. **Where the git logic lives.** (a) Scripts in the action: no bump, track C, the roadmap's placement.
   (b) A `kronikol history publish` verb: a minor in track A, one implementation for every forge and
   for a person at a terminal. (c) (a) now, and (b) if 2.4 would carry a copy that has to change
   independently. **Recommended: (c).** The fold is about 70 lines of bash, proven by the harness, and a
   drift fact keeps a second copy honest.
2. **Record pull-request runs?** **Recommended: not by default.** `record-pull-requests: true` records
   each pull request as its own stream. A fork's pull request cannot push anyway, and the ledger's
   growth (F7) does not need every push of every branch.
3. **Retention.** **Recommended: none in 2.3.** Report the size, warn from 50 MB, and decide with 9.4,
   whose P1 moves years of trends into monthly view files that make a pruned tip affordable.
4. **A floating `v3` tag.** **Recommended: not now.** If it is wanted, narrow `release.yml` to
   `v[0-9]+.[0-9]+.[0-9]+` first (F14). A Marketplace repository (13.2) can carry its own `v1`.
5. **Pinning the actions it calls.** **Recommended: full commit SHAs with the version in a comment**,
   because `record` runs with `contents: write`. Dependabot keeps SHA pins current.
6. **F10: should a re-run's reading leave out its own earlier attempts?** **Recommended: the owner's
   call, outside 2.3.** It changes what a report and the gate say, so it is its own patch or minor in
   the library. The action documents today's behaviour.
7. **Where quarantine and aliases live for a team on a data branch.** On the branch, beside the ledger,
   or on `main`? **Recommended: on the branch.** It is the resolver's one rule, and no new option. The
   README gives the commands: fetch, `git worktree add`, `kronikol history quarantine ... --history`,
   commit and push, or a pull request against the branch, which keeps review. A convenience for this
   can come later.
8. **Fragment artifact retention.** **Recommended: 7 days** (§4.4), against the dogfood's 90.

---

## 11. What is not known

- **Windows and macOS.** The scripts are written to the constraints of §4.9, but nothing ran there. S4's
  CI entry finds out.
- **GitHub's own push races.** `file://` origins refuse a stale push the way GitHub does ("fetch first",
  "cannot lock ref"). The loop keys on the tip, not the message. Whether GitHub's secondary rate limits
  bite at eight fast attempts is not known, and the pause is there for it.
- **The artifact service on re-runs** (PARTIAL, §2.4): regressions as recent as July 2026. A record job
  that cannot download an earlier attempt's artifact fails, and it is re-run.
- **The tool's install time from NuGet on a hosted runner.** The consumer pays it in eighteen lanes
  today, uncached. It is measured in S4.
- **Whether hosted images keep .NET 10** through ubuntu 26.04 (VERIFIED for today's images). §4.7's
  private runtime covers a gap.

## 12. What this plan does not do

It builds nothing for Azure DevOps (2.4), the dashboard (9.x), alerting (9.6), the Marketplace (13.2),
GitLab (§6 of the roadmap), or the tool's F6 patch. It changes no library behaviour (F10 is Q6). It does
not prune a ledger.

## 13. Assumption ledger

| Claim | Mark | Where |
|---|---|---|
| The wiki's recipe fails on first and later runs, then on identity | RUN | `results-s1-wiki-recipe.txt` |
| A hand-made branch has no `merge=union`, and its rebase conflicts | RUN | `results-s5-edges.txt` E1 |
| The read step drops the companions | RUN, READ `HistoryQuarantine.cs:49`, `:155` | `results-s3-companions.txt` |
| The dogfood's loop loses runs and a first-run race duplicates rosters; recording again does neither | RUN | `results-s2b-race-trials.txt` |
| Losses come from the retry budget, duplicates from rebasing | RUN | the fair variant's row |
| A server refusal stops the prototype at once | RUN | `results-s5-edges.txt` E2 |
| `ls-remote --exit-code`: 2 absent, 0 present, 128 unreachable | RUN, VERIFIED | E3; git docs |
| `record` reads the ledger twice per suite | READ `HistoryCommand.cs:304`, `HistoryLedgerWriter.cs:201` | |
| `record` of 18 lanes: 4.78 s at 7.3 MB, about 0.58 s per MB | RUN | `results-s4-cost.txt` |
| A year at the consumer's rate brings a fold to about 110 s | INFERRED (linear, from the slope) | §2.5 |
| The consumer adds 49 run lines a day, about 0.5 MB | RUN (the ledger's own dates) | §2.5 |
| The default concurrency queue cancels an older pending job; `queue: max` holds 100 | VERIFIED | Appendix B |
| #73's README group loses a line with three lanes finishing together | INFERRED from the rule | F5 |
| Composite actions have no post steps | VERIFIED | Appendix B |
| `github.action_ref` is empty in `run:` | VERIFIED | Appendix B |
| Artifact names are unique per run; a re-run's same name is undocumented | VERIFIED, PARTIAL | Appendix B |
| Hidden folders are not uploaded without the flag | VERIFIED | Appendix B |
| The consumer's lanes share one job id | READ (reusable workflow called 18 times) | `ci-main.yml` |
| checkout v6 moved credentials; worktrees from v6.0.1 | VERIFIED | Appendix B |
| That breaks the recipes' worktree push under v6.0.0 | INFERRED, not run | F9 |
| A re-run reads its first attempt, and a persisting failure passes the gate | READ; outcome INFERRED | F10 |
| The repository's archive is 17.5 MB | RUN (`git archive` at `dc619d35`) | F13 |
| A runner downloads the whole repository for a subfolder action | VERIFIED (layout), INFERRED (the transfer's size) | F13 |
| A `v3` tag would run `release.yml` | READ | F14 |
| Hosted images carry .NET 10 and bash; macOS bash is 3.2.57 | VERIFIED | Appendix B |
| 2.2's reusable part is about 150 of 597 lines | READ (PR #73 at `e31b42fa`) | §2.3 |

---

## Appendix A. The harness

[`HISTORY_ACTION_PLAN.harness/README.md`](HISTORY_ACTION_PLAN.harness/README.md) lists every script, what
it answers and its output file, and how to re-run them: the tool built from this repository, and
BreakfastProvider's ledger fetched at `39bd177`. `prototype_record.sh` is the reference implementation of
§4.5 that S2's `record.sh` starts from, and the recipes are kept verbatim as the controls the mutation
facts are red against.

## Appendix B. Platform sources (read 2026-09-27)

Read by a sub-agent of this session through docs.github.com's Markdown API, raw source files and
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
