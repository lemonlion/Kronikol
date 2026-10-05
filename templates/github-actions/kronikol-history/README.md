# Kronikol history

Cross-run history on GitHub Actions: each run's tests read against the runs before them, so the labs page beside a
report, its `Failures.md` and the job summary say what is new, what has failed for a week and what flips, and each run is then
recorded for the next one. The ledger lives on an orphan data branch, `kronikol-history`, which shares no history
with your code and stays out of every pull request's diff
([Cross-Run History](https://github.com/lemonlion/Kronikol/wiki/Cross-Run-History)).

It is four small actions, because a composite action has no step that runs after the job, and the fold must follow
every test job of the run:

| Phase | Where | What it does |
|---|---|---|
| `read` | each test job, before the tests | Fetches the ledger with its quarantine and aliases and names it in `KRONIKOL_HISTORY`. Never fails the job. |
| `gate` | each test job, after the tests, optional | Fails on what the ledger says is new (`kronikol history gate`), with each reading in the job summary. |
| `save` | each test job, after the tests | Uploads the run's `History.run.json` fragments as an artifact of their own. |
| `record` | one job after every test job | Folds the fragments into the ledger and pushes it. |

## Use it

Copy this folder to `.github/actions/kronikol-history/` in your repository, keeping its `.gitattributes` (below).
A workflow with test jobs and a job after them:

```yaml
name: Tests

on:
  push:
  pull_request:

permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'
      - uses: ./.github/actions/kronikol-history/read
      - run: dotnet test
      - uses: ./.github/actions/kronikol-history/save
        if: ${{ !cancelled() }}

  history:
    needs: test
    if: ${{ !cancelled() }}
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v7
        with:
          sparse-checkout: .github/actions
      - uses: ./.github/actions/kronikol-history/record
```

A workflow with a single test job can record from the workspace, with no artifact:

```yaml
name: Tests

on:
  push:

jobs:
  test:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'
      - uses: ./.github/actions/kronikol-history/read
      - run: dotnet test
      - uses: ./.github/actions/kronikol-history/record
        if: ${{ !cancelled() }}
        with:
          path: .
```

To let the history decide the job instead of the test step, gate each lane:

```yaml
name: Tests

on:
  push:
  pull_request:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'
      - uses: ./.github/actions/kronikol-history/read
      - id: test
        continue-on-error: true
        run: dotnet test
      - uses: ./.github/actions/kronikol-history/gate
        if: ${{ !cancelled() }}
        with:
          reports: tests/MyService.Tests/bin/Debug/net10.0/Reports
          test-outcome: ${{ steps.test.outcome }}
      - uses: ./.github/actions/kronikol-history/save
        if: ${{ !cancelled() }}

  history:
    needs: test
    if: ${{ !cancelled() }}
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v7
        with:
          sparse-checkout: .github/actions
      - uses: ./.github/actions/kronikol-history/record
```

With several test jobs, list them all in the history job's `needs`. Lanes that already upload their reports
directory as an artifact can skip `save` and give `record` those artifacts instead, for example
`artifacts: '*-report'`: each lane's fragment sits in its reports directory, so `record` finds it there, at the
price of downloading every report to reach it.

### Copy it, or reference it

Instead of copying the folder you can reference it from a Kronikol release tag that contains it (the first is
`v3.34.0`) and drop the `history` job's checkout, for example
`uses: lemonlion/Kronikol/templates/github-actions/kronikol-history/record@<tag>`. That is 11 lines of workflow in
two jobs, and 7 in one. Two costs come with it:

- **Every job that references it downloads the whole repository at that tag,** about 17.5 MB, most of it the
  PlantUML jar. A copied folder downloads nothing.
- **Pin a release tag or a commit, never `main`.** `record` holds `contents: write`. There is no floating `v3` tag,
  because every `v*` tag in this repository runs a release. Dependabot proposes each new tag, several a week, so
  group or schedule its `github-actions` updates.

On github.com a copied folder also runs without a checkout step, from the commit being tested:
`uses: $/.github/actions/kronikol-history/record` (runner 2.336.0 or later, not on GitHub Enterprise Server). The
examples keep the checkout, which works everywhere.

**Why each part is there:**

- **`read` before the tests.** A run reads the ledger at its end, so the verdicts reach its own labs page,
  `Failures.md` and job summary, not only the next run's.
- **`!cancelled()` on `save`, `gate` and the `history` job, rather than `always()`.** A failed run is the one most
  worth recording. A cancelled one is not a result.
- **A job of its own for `record`.** The shards and lanes of one run fold into one ledger line per suite, so the
  fold waits for all of them. The test jobs keep a read-only token; only the `history` job can write.
- **No concurrency group.** `record` retries a push another run's push beat, so it needs no lock. A group would
  hurt: by default it holds one waiting job, and a third arrival cancels the waiting one, whose run is then never
  recorded. If you add one anyway, give it `queue: max`.
- **The single-job shape gives the test job `contents: write`,** and `actions/checkout` leaves that token where the
  tests can read it. Use two jobs when the tests should not hold a token that can push.

## Inputs and outputs

**read**

| Input | Default | Description |
|---|---|---|
| `branch` | `kronikol-history` | The data branch the ledger lives on. |
| `repository` | this repository | The repository that holds the branch, as `owner/name`. |
| `token` | `github.token` | A token that can read the repository's contents. |

Outputs: `found` (`true` or `false`), `ledger` (the path, or empty), `bytes`. When the ledger is found,
`KRONIKOL_HISTORY` names it for the job's later steps.

**gate**

| Input | Default | Description |
|---|---|---|
| `reports` | *(required)* | The reports to gate, one per line: a `TestRunReport.json`, or the directory holding it. |
| `test-outcome` | empty | The test step's outcome, `${{ steps.<id>.outcome }}`. Without a ledger the phase fails only when this is `failure`. |
| `fail-on` | `new-failures` | What trips the gate: a comma list of `new-failures`, `flaky`, `duration-regression` and `behaviour-change`. |
| `min-runs` | the tool's | Runs of history the statistical verdicts need before they trip the gate. |
| `max-new-failures` | the tool's | New failures allowed before the gate trips. |
| `min-pass-rate` | the tool's | The pass rate below which the gate trips, from 0 to 1. |
| `version` | this folder's | The `Kronikol.Tool` version to install. |
| `tool-command` | empty | A shell command line that runs the tool, in place of installing it. |

Output: `result`, one of `passed`, `failed` and `no-ledger`.

**save**

| Input | Default | Description |
|---|---|---|
| `path` | `.` | Where to look for fragments, one directory per line, relative to the workspace. |
| `name` | the job's id | Part of the artifact's name, `kronikol-history-<name>-<attempt>-<random>`. |
| `retention-days` | `7` | How long the artifact is kept. `record` reads it minutes later; the week covers a `history` job re-run after a fix. |

Outputs: `artifact` (the name, or empty when there were no fragments) and `files`.

**record**

| Input | Default | Description |
|---|---|---|
| `branch` | `kronikol-history` | The data branch. Made on the first run; never the repository's default branch. |
| `repository` | this repository | The repository that holds the branch, as `owner/name`. |
| `token` | `github.token` | A token that can push to the branch. |
| `artifacts` | `kronikol-history-*` | The artifacts holding the fragments, as a pattern. |
| `path` | empty | A directory to fold instead of downloading artifacts, such as `.` in the single-job shape. |
| `version` | this folder's | The `Kronikol.Tool` version to install. |
| `tool-command` | empty | A shell command line that runs the tool, in place of installing it. |
| `record-pull-requests` | `false` | Record a pull request's run too, under a stream of its own. |
| `attempts` | `8` | How many pushes to try when other runs' pushes land first. |
| `accept-renames` | `false` | Alias the scenarios `kronikol history record` finds renamed. |
| `warn-ledger-mb` | `50` | Warn when the ledger reaches this many megabytes. `0` never warns. |

Outputs: `recorded` and `duplicates` (runs appended, and runs already there), `pushed` (`true` or `false`),
`commit` (the commit pushed, or empty) and `ledger-bytes`.

## How it behaves

- **None of it uses the job's checkout.** `read` and `record` each start a repository of their own in
  `RUNNER_TEMP` and authenticate with `token`. So the checkout's version, `persist-credentials: false` and a job
  with no checkout at all change nothing.
- **`read` fetches only what it reads:** the tip's commit and trees, then `history.jsonl`, `quarantine.json` and
  `aliases.json`. Anything else on the branch is never downloaded. With no branch yet it says so and the tests run
  without history; with a remote it cannot read it warns and they run without history.
- **`save` keeps the tree below the workspace,** so `record` can tell reports directories apart and fold a run kept
  under `runs/` as an attempt of the run beside it. A reports directory in a hidden folder, such as
  `.logs/kronikol`, is saved. A rotation a killed run left half-done (`runs/.incoming-*`) is not.
- **`record` makes the branch on the first run,** as an orphan holding the ledger, a `.gitattributes` with
  `history.jsonl merge=union` and a README. Only a remote that answers "no such branch" starts it: a remote it
  cannot read is an error, never an empty ledger.
- **It never rebases and never force-pushes.** When another run's push lands first, it records again on the branch
  as it now is, which is safe because a run already in the ledger is skipped as a duplicate. When a push is refused
  and the branch did not move, a ruleset, a hook or a token without `contents: write` refused it, and the phase
  fails at once, naming the refusal.
- **One commit per workflow run,** `Record run <run id>:<attempt> (<sha>) [skip ci]`, by `github-actions[bot]`.
  Recording the same run again changes nothing and succeeds, so re-running the `history` job is safe.
- **Pull requests are not recorded** unless `record-pull-requests: true`, which records each under a stream of its
  own. A pull request's build reads against the branch it targets anyway, so its gate compares with that branch.
- **Its job summary** names the runs recorded, the ledger's size and the commit, and shows
  `kronikol history show` for each suite recorded. From `warn-ledger-mb` it warns that the ledger is large: every
  read fetches it whole, and every test run reads it.
- **`gate` gates every report before it fails.** A trip in any report fails the step, and a report the tool cannot
  read is an error, never a pass. Each reading goes into the job summary under the report's path.

## The tool

`record` and `gate` run `kronikol` from the `Kronikol.Tool` package, at the release this folder ships in (its
`VERSION` file). It is installed with `dotnet tool install` into a directory of its own under `RUNNER_TEMP`, once
per job, and runs on the SDK's own .NET, so nothing is added to the job's `PATH` or environment. It needs a .NET 10
SDK: an older SDK cannot install it. GitHub's hosted images have one. Elsewhere, add `actions/setup-dotnet` with
`dotnet-version: '10.0.x'` before the step, or give `tool-command`, a shell command line that runs the tool, such as
`dotnet run --project src/Kronikol.Tool --framework net10.0 --`.

A copy of the folder taken from `main`, or a tag referenced in the minutes before its release has published the
package, installs a version that is not on nuget.org yet, and the step fails naming it.

## Credentials

- **`token` defaults to the workflow's token.** `read` and `gate` need `contents: read`, and `record` needs
  `contents: write`, on the job that runs them. A fork's pull request gets a read-only token, so its push is
  refused, which is one more reason pull requests are not recorded by default.
- **Another repository.** With a personal access token or a GitHub App token, `repository` can name a central
  repository that keeps the ledgers of many services.
- **What git is told.** The token goes to git as `actions/checkout`'s header, for the repository's server only,
  through the environment: it is in no file, no URL and no command line, and it is masked. The scripts' git keeps
  the machine's configuration (proxies, certificates, URL rewrites, commit signing) and sets five things over it:
  no prompt, no credential helper (so a wrong token fails at once and no helper of the machine's is asked or told to
  forget what it stores), no line-ending conversion, no hook of the machine's, and the header, emptied first so
  that one the machine holds for that server is not sent beside it.
- **Signed commits.** A machine that signs every commit signs the ledger's commit too. A ruleset that requires
  signed commits on every branch needs the data branch excluded, or runners that can sign.

## Quarantine and renames

`kronikol history quarantine` and `kronikol history rename` write `quarantine.json` and `aliases.json` beside the
ledger, and every reader looks for them there. On a data branch that means on the branch:

```bash
git fetch origin kronikol-history
git worktree add ../kronikol-history kronikol-history
kronikol history quarantine <stableId> --reason "flaky upstream" --history ../kronikol-history/history.jsonl
git -C ../kronikol-history add quarantine.json
git -C ../kronikol-history commit -m "Quarantine <stableId>"
git -C ../kronikol-history push origin kronikol-history
```

A pull request against `kronikol-history` works as well, and keeps a review.

To read the ledger on your machine: `git fetch origin kronikol-history && git show FETCH_HEAD:history.jsonl >
history.jsonl`, then `kronikol history show --history history.jsonl` or
`kronikol query history ./Reports --history history.jsonl`.

## Limits

- **A re-run passes on the failure it re-ran.** A re-run of a failed job is a new attempt of the same run, and its
  history includes the first attempt, which `record` already folded. So a failure that persists reads as already
  failing, and the gate passes it. For the next push that is the design: a gate that fails on any red cannot tell a
  regression from a test that has flipped for a month.
- **The ledger grows with every run,** and every lane fetches it whole. `record` reports its size and warns from
  `warn-ledger-mb`. Pruning the tip (`kronikol history prune`) makes the branch's packed history larger, not
  smaller.
- **GitHub Enterprise Server** does not support `actions/upload-artifact` and `actions/download-artifact` v4 and
  later, so there use the single-job shape, `record` with `path`.
- **Keep the folder's `.gitattributes`** when you copy it. A Windows runner checks files out with CRLF line endings
  unless told otherwise, and bash cannot run a script with them.
- **The actions it calls are pinned to commits,** `actions/upload-artifact` v7.0.1 and
  `actions/download-artifact` v8.0.1, because `record` runs with `contents: write`.
