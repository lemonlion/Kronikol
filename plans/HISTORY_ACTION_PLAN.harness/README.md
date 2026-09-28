# HISTORY_ACTION_PLAN harness

The scripts behind every RUN mark in [`../HISTORY_ACTION_PLAN.md`](../HISTORY_ACTION_PLAN.md), with the
output they printed: `s1` to `s5` on 2026-09-27, `s6` to `s10` in the plan's second pass on 2026-09-27 and
28. Bash, git and Python 3 (standard library only), and `kronikol` built from this repository. No GitHub
runner and no network beyond loopback: an "origin" is a bare repository on disk, served over smart HTTP
on 127.0.0.1 from `s6` on.

## What a world is

`common.sh` is sourced by every scenario. It makes a **world**: a bare `origin.git` holding a `main` with two
files, a `kronikol` on `PATH` that runs the tool built from this repository, and git with **no global or
system configuration** (`GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_NOSYSTEM=1`), because a fresh
GitHub-hosted runner has no committer identity and `actions/checkout` does not set one. `checkout` makes a
checkout the way `actions/checkout@v5` does by default: `git init`, `git remote add origin` (which configures
the default `+refs/heads/*:refs/remotes/origin/*` refspec), one commit fetched at depth 1, checked out.
`job_env` gives a job its `RUNNER_TEMP`, `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, `GITHUB_SHA`, `GITHUB_ENV`,
`GITHUB_OUTPUT` and `GITHUB_STEP_SUMMARY`. `branch_report` prints what `kronikol-history` holds on origin:
lines by kind, runs by id, duplicate roster lines, `kronikol history verify`, and the commit count.

**The fragments are the consumer's own lines.** `make_fragments.py` takes a ledger and writes, for one
synthetic CI run `gh:<n>:<attempt>`, one `History.run.json` per suite (the first N suites of the ledger, in
the order they first appear): that suite's last roster, its shapes line and its last run line, with the id,
time and branch replaced. So a fold here appends lines the size BreakfastProvider's do (about 10 KB a run
line).

## The scripts

| Script | What it answers | Output |
|---|---|---|
| `recipe_wiki.sh` | The fold job of the wiki's `Cross-Run-History`, "The recommended shape", verbatim, run as a `run:` block with no `shell:` runs on ubuntu (`bash -e`) | (used by the scenarios) |
| `recipe_dogfood.sh` | The fold step of `.github/workflows/ci-summary-preview.yml` at `e4c9e36`, verbatim but for the tool (the workflow builds it from source) | (used by the scenarios) |
| `recipe_dogfood_fair.sh` | `recipe_dogfood.sh` with the prototype's retry budget (8 attempts, a pause of 0.1 to 0.9 s): a race then compares rebasing with recording again, and nothing else | (used by the scenarios) |
| `prototype_record.sh` | The record step the plan proposes (plan §4.5): its own repository per attempt, `ls-remote` exit 2 as the only way to a new branch, `kronikol history init` for the header and `.gitattributes`, record, commit, push; on a rejected push, record again on the branch as it now is when the branch moved, and stop when it did not. With `KRONIKOL_TOKEN`, the token goes in as checkout's header through the environment; git runs with no prompt, no credential helper, no line-ending conversion and no machine hook (plan §4.9) | (used by the scenarios) |
| `s1_wiki_recipe.sh` | Plan F1. The wiki's recipe on a new repository's first run (W1), on a run after the branch exists (W2), with the fetch it lacks added (W3), and with an identity added too (W4) | `results-s1-wiki-recipe.txt` |
| `s2_race.sh` | Plan F4, one race each: six fold jobs of six workflow runs finishing together, each with its own checkout and three suites' fragments, for the wiki recipe (with W4's additions), the dogfood recipe and the prototype, on an existing branch and on a first run | `results-s2-race.txt` |
| `s2b_race_trials.sh` | Plan F4, counted: `TRIALS` races of `N` writers per recipe, the runs lost and the roster lines duplicated | `results-s2b-race-trials.txt` (10 races of 6) |
| `s3_companions.sh` | Plan F2. A scenario quarantined on the data branch, then the read step as the wiki and the dogfood write it (the ledger alone) and as BreakfastProvider's copy writes it (with the two companions), each asked `kronikol history quarantine --list` | `results-s3-companions.txt` |
| `s4_cost.sh` | Plan F6, F7. On the consumer's real ledger: five folds of one run of eighteen lanes (the time of each `kronikol history record`), what each recorded run adds to the branch's packed objects, the tip's raw and gzipped size, and `history show` | `results-s4-cost.txt` |
| `s5_edges.sh` | Plan F1 (a branch made by hand as the wiki's comment says, with no `.gitattributes`, two writers), §4.5 (origin refuses every push to the branch, through a `pre-receive` hook: the prototype stops at once and names the refusal), and what `git ls-remote --exit-code` answers for a branch absent, present, and an origin unreachable | `results-s5-edges.txt` |
| `githttp.py` | An origin over smart HTTP: `git http-backend` behind basic authentication, taking `x-access-token` and a token as github.com takes `GITHUB_TOKEN`. A second, read-only token has its push answered 403, as a fork's pull request token does. A third identity serves the stored-credential case. Every request is logged with its status, its size and who it was served as, never a secret | (used by `s6`) |
| `s6_http.sh` | Plan §4.3, §4.5, F9, F15 over HTTP. H1: the token, prompts, and a machine's credential helpers (a store holding another identity, a helper that waits 8 s), with and without `credential.helper` reset. H2: `ls-remote --exit-code` for a 401, a 404 and a server down. H3: `N` writers racing `TRIALS` times on a first run and on an existing branch, with the token. H4: a read-only token and a `pre-receive` hook. H5: the token and its header in every output and every file under the jobs' `RUNNER_TEMP`. H6: a depth-1 read against a blobless one when a 25 MB file sits beside the ledger. H7: the dogfood's fold in checkouts made as `actions/checkout` v5, v6.0.0 and v6.0.1 make them | `results-s6-http.txt` (`N=6 TRIALS=3`) |
| `s7_rerun_reading.sh` | Plan F10. Five green runs of one suite; run 6 fails. Attempt 1 gated before and after its fold, attempt 2 (the re-run) after it, and attempt 2 against the ledger without attempt 1's line | `results-s7-rerun-reading.txt` |
| `s8_scaling.sh` | Plan F6, F7. The consumer's ledger grown to 25, 50, 100 and 180 MB (`grow_ledger.py`): `record` of 1, 6 and 18 suites, `gate` and `show`, each timed with its peak memory (`measure.py`) | `results-s8-scaling.txt` |
| `s9_read_window.sh` | Plan F7, Q3 (d). `prune --window 50` of a 180 MB copy (its cost and size); one suite's failing report gated against the ledger and the window, the outputs compared; then ten runs recorded with and without a window file committed beside the ledger, and what each costs the pack | `results-s9-read-window.txt` |
| `s10_machine_config.sh` | Plan F16, §4.9. A machine's own git configuration: the folder copied into a repository and checked out with `core.autocrlf=true`, with and without the folder's `.gitattributes` (M1); a machine-wide hook that refuses every commit (M2); a machine that signs every commit with no key it can use (M3) | `results-s10-machine-config.txt` |
| `make_report.py` | Writes the `TestRunReport.json` a fragment stands beside, with only what the gate reads (the shape `HistoryGateTests` writes by hand) | (used by `s7` to `s9`) |
| `grow_ledger.py` | Grows a ledger to a size the way time grows it: copies of its own run lines, each with a new id and a later time; no roster or shapes line repeated | (used by `s8`, `s9`) |
| `measure.py` | Runs one command and prints its wall time and its peak resident memory: the largest of any process it waited for, here the tool's own (`ru_maxrss`) | (used by `s8`, `s9`) |

`results-s2-race.txt` shows `error: ca` for git's `error: cannot lock ref`, cut by the script's own `grep -o`.
Paths in the results are replaced by `<origin>`, `<person>` and `<tmp>`, and the HTTP origin's port by
`<port>`.

**`prototype_record.sh` changed after `s2`, `s2b` and `s5` ran.** The second pass gave it the token (as a
header, through `GIT_CONFIG_COUNT`, masked first) and git's settings: no prompt, `credential.helper` reset,
`core.autocrlf=false`, no machine hook (`core.hooksPath`, `--no-verify`). None of that touches a `file://`
origin. `s6` re-ran the races and refusals with them over HTTP, and `s10` ran with them.

## Re-running

```bash
# The tool, from this repository (any .NET 10 SDK; the numbers were taken with 10.0.401):
dotnet publish src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -f net10.0 -o /tmp/kronikol-tool
export KRONIKOL_TOOL_DIR=/tmp/kronikol-tool DOTNET_ROOT=$(dirname "$(readlink -f "$(command -v dotnet)")")

# The fixture: BreakfastProvider's ledger as it stood at 39bd177 (a public repository, 7,112,729 bytes):
git clone --depth 1 https://github.com/lemonlion/BreakfastProvider /tmp/bp
git -C /tmp/bp fetch --depth=20 origin kronikol-history
git -C /tmp/bp show 39bd177:history.jsonl > plans/HISTORY_ACTION_PLAN.harness/bp-history.jsonl

cd plans/HISTORY_ACTION_PLAN.harness
./s1_wiki_recipe.sh /tmp/kh/s1 > results-s1-wiki-recipe.txt 2>&1
N=6 ./s2_race.sh /tmp/kh/s2 > results-s2-race.txt 2>&1
N=6 TRIALS=10 ./s2b_race_trials.sh /tmp/kh/s2b > results-s2b-race-trials.txt 2>&1
./s3_companions.sh /tmp/kh/s3 > results-s3-companions.txt 2>&1
./s4_cost.sh /tmp/kh/s4 > results-s4-cost.txt 2>&1
./s5_edges.sh /tmp/kh/s5 > results-s5-edges.txt 2>&1
N=6 TRIALS=3 ./s6_http.sh /tmp/kh/s6 > results-s6-http.txt 2>&1     # needs bc, curl and setsid
./s7_rerun_reading.sh /tmp/kh/s7 > results-s7-rerun-reading.txt 2>&1
./s8_scaling.sh /tmp/kh/s8 > results-s8-scaling.txt 2>&1             # about 4 GB of disk, a few minutes
./s9_read_window.sh /tmp/kh/s9 > results-s9-read-window.txt 2>&1
./s10_machine_config.sh /tmp/kh/s10 > results-s10-machine-config.txt 2>&1
```

`bp-history.jsonl` is ignored here (`.gitignore`): it is the consumer's data, and 7 MB.

The numbers were taken on a Linux container with four cores, 16 GB, git 2.43.0 and Python 3.11. The races
depend on timing, so a re-run gives other counts of the same kind: in these runs the dogfood recipe lost
runs in most races of six writers, and the prototype lost none in any. The times in `s4_cost.sh` and
`s8_scaling.sh` are for that machine. `s4`'s five folds span 0.8 MB, too little for a slope; `s8`'s span 7
to 180 MB.
