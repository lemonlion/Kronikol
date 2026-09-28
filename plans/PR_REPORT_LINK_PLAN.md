# PR report link plan (`ROADMAP.md` 2.2, #72, PR #73)

**Date:** 2026-09-27 · **Repo version:** 3.31.10 (`5ca0878a`; first written at 3.31.9, `e4c9e360`) · **PR head:**
`e31b42fa` on `pr-report-link-template`, one commit on `49f5ea87` (the tree of 2026-09-15, 3.19.0) · **Status:** a
plan, **not green-lit**. It needs the owner's answers to Q1 to Q5 (`ROADMAP.md` D25), then about a day of work,
then the owner's go-ahead to merge. §1 is what was RUN, READ and read on the web today, §2 the findings, §3 the
questions, §4 the slices, §9 the assumption ledger. The probes, the rehearsals and their output are in
`PR_REPORT_LINK_PLAN.harness/`.

**S1 rehearsed the same day** (§1.2; the output is `PR_REPORT_LINK_PLAN.harness/results-s1-rehearsal.txt`),
locally and pushed nowhere: the PR rebased onto `main` at `2de961ec` stops
on `CHANGELOG.md` alone, resolved as S1 says; its 17 facts pass, none skipped; all of `Kronikol.Tests` passes
but for three tests that expect a write to be refused, which fail the same way on `main` without the PR because
the machine ran as root (§6); and `Kronikol.Templates` packed from that tree carries the PR's paragraph with its
relative link and no `github-actions/` file (§1.4). **Re-checked against 3.31.10** (`5ca0878a`, later the same
day): `git merge-tree` still stops on `CHANGELOG.md` alone (RUN).

**Deepened the same evening** (§1.9, §1.10): **S2 and S3 rehearsed** on the PR rebased onto 3.31.10, nothing
pushed. Every new fact failed first for the reason §4.2 gives, then passed with the fixes: 24 of 24, and all of
`Kronikol.Tests` with nothing new failing. The four diffs are in the harness as patches that rebuild that tree
exactly. GitHub's own records were read: the PR's state, its checks, every re-run in the repository, how pull
requests here are merged, and `main`'s CI, which failed six of its eight runs that day (F17). actionlint was run on
the workflows (F16). Two more probes found F15 and the rule F3's fix needs. Q5 is new. The web questions left
open were answered the next morning (§1.6's last five rows): a job-level group takes `queue: max`, and a step
output keeps the last value written, which settles F12.

Roadmap item 2.2 reads: "Rebase and merge PR #73 (#72). One PR comment, one line per report artifact, kept
current by every run", in hours. This plan re-checks each premise of that row against today's `main` and
against GitHub as it is today, and says what landing the pull request takes.

**The headline.** The rebase is trivial: one conflict, in `CHANGELOG.md`. The `README.md` conflict the
roadmap names is gone (§1.1). The action is careful work, and its 17 facts guard what they say they guard
(§1.3). But the re-check found things that merging it as it stands would ship wrong or leave unproven. None is
visible to its tests, because each lives outside the script, or in a case the in-memory GitHub does not model:

1. **The README's workflow drops a lane when three link jobs meet** (F1). Its per-pull-request concurrency
   group keeps one job running and one waiting, and GitHub cancels the waiting one when a third arrives. A
   matrix of three link jobs meets this every time. Since 2026-05-07 GitHub has a one-line fix,
   `queue: max`.
2. **The README shows a comment the action does not write** (F2, RUN).
3. **The hidden run tag cannot take a field it does not know** (F3, RUN). A later version that adds one would
   be overwritten by any lane still pinned to this version. The first release tag that carries the action
   freezes the format, so it has to be settled before that tag (roadmap rule 6).
4. **A line break in `label` lets an older run replace a newer link** (F4, RUN).
5. **`templates/README.md` ships.** It is the `Kronikol.Templates` package README, so the PR's new paragraph
   reaches nuget.org, where its relative link renders with an empty address (F6). The changelog's "nothing a
   release ships changes" is wrong by that paragraph.
6. **The README's workflow pins actions one and two majors behind** (F7): `checkout@v5` and
   `upload-artifact@v5` where v7 is current, `setup-dotnet@v5` where v6 is. Its `github-script@v9` is current.
7. **Nothing in this repository has ever run the action on GitHub**, and no project here publishes a report
   the way its README tells a consumer to (F8). The PR says so itself. Its claim about re-runs rests on
   behaviour GitHub does not document, and the evidence on it conflicts (F13).
8. **The newest actionlint rejects `queue: max`** (F16, RUN). A consumer whose CI lints workflows fails on the
   copied job until they add one ignore line, which the README gives.
9. **`heading` and `report-file` belong to the comment, but every lane passes its own** (F15, RUN): lanes that
   differ swap the comment's heading and its "open this file" tip on every run.
10. **The comment's shape freezes with the first tag too** (F5, Q5). Roadmap 7.4 means to add its section to
    this comment, and a lane still on the first tag would drop it on each of its runs. An end marker, with what
    follows it kept, is seven lines of script, a two-line comment and one fact, rehearsed.

Each is small, and the fixes need no redesign. With them, 2.2 is about a day rather than hours. The rehearsal
(§1.9) turned most of that day into patches that apply as they stand, so what is left is mostly the live proof
(S3) and the owner's steps.

One more thing the re-check found belongs after 2.2, not in it, but it may matter more than any of the above
(§8): the issue ruled out a browsable report because "artifacts only download as a zip". **That stopped being
true on 2026-02-26.** `upload-artifact@v7` uploads a single file unzipped, and GitHub shows it in the browser
when the browser can render the type. Whether a Kronikol report works when opened that way is not known, and
S3 can measure it for the price of one more step.

---

## 0. Summary

| Slice | What | Bump | Tests it adds |
|---|---|---|---|
| S0 | Re-check at execution time: the PR head, the trial merge, the consumer's copy, the action majors, `main`'s CI, actionlint (§4.0) | none | none |
| S1 | Rebase onto `main`; the changelog conflict resolved by Q3; the 17 facts and then all of `Kronikol.Tests` on the rebased head; push (§4.1) | none | none: CI on the rebased head, read against `main`'s own run (F17), is the baseline |
| S2 | The fixes, each fact red first on the rebased head: `queue: max` in the README's calling job (F1); the README's sample is the action's output (F2); the run tag read wherever fields follow it, and the rule that they only follow it (F3); inputs flattened to one line (F4); current action majors (F7); the README's paragraphs on concurrency, actionlint (F16), ownership, sign-in, input text and what the link is (F14), and the inputs that belong to the comment (F15); the package README's link absolute, with a guard (F6); if Q5 is yes, the end marker (§4.2). **Rehearsed**: the harness's patches | none | three facts, one extended, one guard (two cases); a fourth fact if Q5 is yes |
| S3 | The live lane: `.github/workflows/pr-report-link.yml` runs the CI Preview example with `PublishCiArtifacts = true`, uploads the reports and calls the action by path, on pull requests that touch the action. It runs on PR #73 itself, which is the live proof, nine checks (§4.3). **Rehearsed** as far as a machine without GitHub reaches: the fact, the test step, actionlint | none | a fact holding the lane to the action |
| S4 | The documents in the pull request: the changelog entry reworded, the stale `[Unreleased]` link definition removed, the PR body (§4.4) | none | |
| S5 | Merge through the pull request, so "Closes #72" fires (§4.5) | none (Q1) | CI (28 of 28 where `main` is green, F17), CodeQL and the lane, green on the final head |
| S6 | After the merge: the wiki section, the first tag that carries the action, `ROADMAP.md` and `PLANS_STATUS.md`, the consumer (§4.6) | none | |

Order: S0 to S4, then S5 once CI and the live proof are green, then S6. §4.7 says which steps a Claude Code
session may be refused, and who runs them then.

---

## 1. What was checked (2026-09-27)

RUN means a command was executed and its output is in this file or in the harness folder. READ means a file or
an API response was read today. WEB means a current vendor page, release or source file was read today; §1.6
gives each address. PLAN means the PR, the issue or another plan says so and it was not re-checked. INFERRED
means reasoned from two facts, stated by neither source.

### 1.1 The pull request against today's `main` (RUN)

PR #73 was opened by the owner on 2026-09-15 and holds one commit, `e31b42fa`: six files, +892 −1.

| File | Lines |
|---|---|
| `templates/github-actions/kronikol-pr-report-link/action.yml` | +137 |
| `templates/github-actions/kronikol-pr-report-link/README.md` | +132 |
| `tests/Kronikol.Tests/Templates/PrReportLinkActionTests.cs` | +597 |
| `templates/README.md` | +7 |
| `README.md` | one line changed |
| `CHANGELOG.md` | +18 |

- Its parent, `49f5ea87`, is the tree of 2026-09-15. `main` is 67 commits and 42 releases ahead of it (3.20.0
  to 3.31.9), at `e4c9e360`. The clone was shallow and was deepened to find the merge base.
- A trial merge (`git merge --no-commit origin/pr-report-link-template` on `origin/main`, in a scratch
  worktree) stops on **one conflict, `CHANGELOG.md`**: the PR's `## [Unreleased]` against the 42 release
  sections written at the same place since. `README.md` merges cleanly, because the "CI artifact upload"
  paragraph the PR extends has not changed on `main`. The roadmap's "Conflicts are in `CHANGELOG.md` and
  `README.md` and grow with each release" is half stale: the README half never grew.
- The PR has no review and no comment. GitHub first reported `mergeable_state: unknown`, not yet computed; by the
  evening it read `dirty`, with `mergeable: false` and `rebaseable: false` (§1.10).
- **Against 3.31.10** (`5ca0878a`, 76 commits past the merge base): `git merge-tree` still stops on
  `CHANGELOG.md` alone (RUN).

### 1.2 The CI the pull request has had (READ, the check-runs API)

- One run, 34979831198, on 2026-09-15: all 28 CI jobs green, and CodeQL (30 check runs). Core Tests is the job
  that runs the 17 facts; the PR body quotes its log at 17 passed, 0 skipped, 0 failed.
- Nothing has run on it since. One change on `main` touches test organisation: a class that clears
  process-wide tracking state now shares one collection per assembly, with a guard (`b5cff009`). The 17 facts
  touch no tracking state, so they should be unaffected (INFERRED).
- **S1 rehearsed** (RUN, .NET SDK 10.0.401, node 22.22.2): PR #73 rebased onto `main` at `2de961ec` (3.31.9 and
  one plan-only commit) in a scratch worktree, the `CHANGELOG.md` conflict resolved as S1 step 2 says, nothing
  pushed. The 17 facts: 17 passed, 0 skipped, 0 failed.
- **All of `Kronikol.Tests` on that tree** (RUN): 5,967 tests, `main`'s 5,950 and the 17. 5,959 passed, 5
  skipped, 3 failed.
  - The skips: four tests only Windows can run ("only Windows refuses to move a file that a reader holds", and
    the same for a rename), and one that needs a generated 100 MB corpus.
  - The failures: `InitAgentsCommandTests.A_file_that_cannot_be_written_is_reported_rather_than_thrown`,
    `RunRotationTests.A_read_only_previous_report_stops_the_rotation_and_never_the_run` and
    `StaleOutputTests.The_pointer_does_not_name_the_previous_runs_file_when_this_run_could_not_replace_it`. Each
    expects a write to be refused, and the machine ran as root (uid 0), which a read-only file does not stop.
    **The same three fail the same way on `main` at `2de961ec` without the PR** (RUN), so they are not the
    PR's. CI runs unprivileged, so it never meets them. §6 has the fix.
- **`main`'s own CI is not the green baseline this section first assumed** (READ, §1.10, F17): six of its eight
  runs on 2026-09-27 failed, the newest on a Core Tests fact the PR does not touch.

### 1.3 What the tests stand on, and what they cannot see (READ)

On `main` today:

- `NodeProbe.IsAvailable` and `NodeProbe.RunWithStdin` are in `tests/Kronikol.Tests/NodeProbe.cs` (lines 30
  and 38).
- YamlDotNet 16.3.0 is referenced by `Kronikol.Tests.csproj` (line 28), which targets net10.0. CI's Core Tests
  job runs the whole project with node on the runner.
- No test reads `templates/github-actions/`. The four tests that read under `templates/` read `skills/`,
  `agents/CLAUDE.md` and `*.csproj` (`PluginManifestTests.cs:130`, `InitAgentsCommandTests.cs:29`,
  `FallbackScriptTests.cs:32`, `SkillDriftTests.cs:33`). `tests/Kronikol.Tests/Templates/` does not exist yet.

What the 17 facts do, from the file: the script is read out of `action.yml` with YamlDotNet; each input reaches
it through the step's `env` and its declared default, as the runner would pass it; a node driver runs it the way
actions/github-script does, as an async function given `github`, `context`, `core` and `process`, against
comments and artifacts held in memory. The PR's table shows each guard broken in turn and the facts that fail.
One fact holds the README's workflow to the action: declared inputs only, every required one, a
per-pull-request concurrency group, `pull-requests: write` and `actions: read`.

What they cannot see, by construction: anything GitHub does (the token's permissions, the real artifacts
endpoint, concurrency, re-runs, the bot's login, how the comment renders), and anything the README says about
the comment rather than about the workflow.

### 1.4 What ships (READ)

- `templates/Kronikol.Templates.csproj` packs each template folder by name (`kronikol-xunit3\**\*` and eleven
  more). Nothing under `templates/github-actions/` enters the `Kronikol.Templates` package, so **the action
  ships in no package**, as the PR says.
- The same project packs **`templates/README.md` as the package README** (`PackageReadmeFile`, and
  `<None Include="README.md" Pack="true" PackagePath="\" />`). The PR adds a section to that file, so the next
  release's page for `Kronikol.Templates` on nuget.org carries it. That is still no bump (a documentation
  change, `CLAUDE.md`), but the changelog line "No package contains it, so nothing a release ships changes"
  is not true as written.
- That section links `github-actions/kronikol-pr-report-link` relatively. The file's only other link is
  absolute, and so are all 12 links in `nuget-readme.md`, the README every other package ships.
- The root `README.md` ships in no package: `Directory.Build.props` gives every package `nuget-readme.md`.
- **Packed from the rehearsed tree** (RUN, `dotnet pack templates/Kronikol.Templates.csproj`):
  `Kronikol.Templates.3.31.9.nupkg`, 161 files, none under `github-actions/`. Its `README.md`, which the nuspec
  names as the package readme, carries the PR's section at line 62 with the link
  `(github-actions/kronikol-pr-report-link)`.

### 1.5 The action, probed (RUN)

`PR_REPORT_LINK_PLAN.harness/probe.js` runs the PR's script out of its `action.yml` the way the PR's own driver
does, against an in-memory GitHub, and prints what happened. Output: `results-probe.txt`.

| Probe | What | Result |
|---|---|---|
| P1 | The comment for the README's two lanes | The action writes a `> [!TIP]` alert (`> 💡 Each link downloads a zip of the latest reports. Open …`) and a footer line, `<sub>🤖 CI rewrites this comment every time a linked workflow runs.</sub>`. The README's sample, like the PR body's and the issue's, shows `💡 **Tip:** Each link downloads…` as a plain line and no footer |
| P2 | A line whose tag carries one field more than today (`run:500 wf:77`), then run 400 | **Run 400 overwrote it**: one write, and the line links run 400. The run id is read with `/ run:(\d+) -->/`, which needs ` -->` straight after the digits, so the guard reads run 0 |
| P3 | A `label` holding a line break: run 600, then run 550 | **Run 550 replaced run 600's link**: two writes. The line is written as two lines, and the next read recognises neither as its own |
| P4 | Text in the comment outside the lines (a `### Changed scenarios` section) | Dropped on the next rewrite: the body is rebuilt from the marker, the heading, the lines, the tip and the footer |
| P5 | Two lanes share the comment, one with the default `report-file` and `heading`, one with `UiReport.html` and `UI reports`; runs A, B, A | **The comment swaps with each run**: after A the tip names `TestRunReport.html` under the default heading, after B `UiReport.html` under `UI reports`, after A again the first pair. Both inputs are per call; the tip and the heading are per comment (F15) |
| P6 | A later version's field written **before** the run id (`wf:77 run:500`), then run 400 | **A second line for the same artifact**: the tag is not recognised as the line's own, so the run adds its line beside it. Fields may only follow the run id (F3) |

After S2's fixes (§1.9), the same probes: P2 and P3 no longer let the older run write (no write, and one write
linking run 600). P4 to P6 are unchanged: P4's section sits above any end marker, so Q5's patch drops it too; P5
is documented; P6's order is the rule the tag now keeps.

### 1.6 GitHub and nuget.org as they are today (WEB)

Read on 2026-09-27, and the last five rows on 2026-09-28. Tags were listed with `git ls-remote`, release dates and
runtimes read from each action's releases and `action.yml`. Every address, the full quotes and each answer's
verification mark are in `PR_REPORT_LINK_PLAN.harness/web-sources.md`.

| Question | Answer | Source |
|---|---|---|
| Concurrency, three jobs | The default, `queue: single`: "At most one job or workflow run can be `pending` in the concurrency group. When a new job or workflow run is queued, any existing pending job or workflow run in the same group is canceled and replaced." **`queue: max`, shipped 2026-05-07**: "Up to 100 jobs or workflow runs can be `pending`", processed first in, first out, though "ordering is not guaranteed"; not allowed with `cancel-in-progress: true` | docs.github.com, *Control the concurrency of workflows and jobs*; github.blog changelog 2026-05-07, *Concurrency groups now allow larger queues* |
| Group scope | Names are shared across a repository's workflows and are case-insensitive | same page |
| The token for comments | Creating and updating an issue comment needs Issues **or** Pull requests write, so `pull-requests: write` is enough; listing a run's artifacts needs Actions read; declaring any permission sets every other to none | docs.github.com REST *Issue comments*, *Permissions required for GitHub Apps*, workflow syntax |
| Fork pull requests | The token is read-only whatever the `permissions` block says, unless an admin sends write tokens to fork workflows | workflow syntax |
| Dependabot pull requests | Treated like forks, read-only by default, but "You can use the permissions key in your workflow to increase the access for the token", with `pull-requests: write` as the docs' own example. **So the README's job works on them** | docs.github.com, *Troubleshooting Dependabot on GitHub Actions* |
| The bot's login | `GITHUB_TOKEN` is an app installation token, and an installation token acts as the app's `[bot]` account. No page says `github-actions[bot]` in one sentence | docs.github.com, *GITHUB_TOKEN*; *Differences between GitHub Apps and OAuth apps* |
| Current majors | `github-script` v9 (2026-04-09; it breaks `require('@actions/github')` and redeclaring `getOctokit`, neither of which the script does), `checkout` v7 (2026-06-17; it refuses fork code under `pull_request_target` and `workflow_run`, which the README does not use), `setup-dotnet` v6 (2026-07-15), `upload-artifact` v7 (2026-02-26), `download-artifact` v8. All node24 | `git ls-remote --tags`, release notes |
| upload-artifact v7 | Outputs `artifact-id`, `artifact-url` ("only works for requests Authenticated with GitHub. Anonymous downloads will be prompted to first login") and `artifact-digest`. `overwrite: true` deletes a same-name artifact first, and the new one gets a new id. **`archive: false`**: one file, unzipped, named by the file (the `name` input is ignored); "If your browser supports viewing the file type natively, you can view files directly in your browser. This is great for simple HTML files (without links to CSS or JS)" | actions/upload-artifact README and `action.yml`; github.blog changelog 2026-02-26 |
| Re-runs | **Not documented.** GitHub's v4 announcement: "there cannot be multiple v4 artifacts with the same name, in the same workflow run". GitHub's own toolkit: "It is possible to have multiple artifacts with the same name in the same workflow run by using … @actions/artifact < v2 or it is a rerun", and download-artifact lists with `latest: true` "to avoid duplicates". Two public reports from September 2026 disagree: one lists two same-name artifacts across attempts, one fails with "an artifact with this name already exists" | github.blog v4 announcement; actions/toolkit `packages/artifact/src/internal/client.ts`; NSTA1/Orleans.Lattice#2924 and BenSheridanEdwards/StyleProof#688 |
| The artifacts `name` filter | "When specified, only artifacts with this name will be returned." Exactness is not stated; the PR measured it on a live run | docs.github.com REST *Artifacts* |
| Actions in a subdirectory | `{owner}/{repo}/{path}@{ref}`, where the ref is a tag, a branch or a commit; a commit SHA "is the safest for stability and security". **New, 2026-07-30**: `uses: $/path` runs an action from the workflow's own repository at the running commit, with no checkout and no `@ref`; runner 2.336.0 or later; not on GitHub Enterprise Server | workflow syntax; github.blog changelog 2026-07-30 |
| The Marketplace | "Each repository must contain a single action metadata file (action.yml or action.yaml) at the root"; actions in sub-folders "will not be automatically listed"; a public repository; a unique name; `branding` optional; no rule against workflow files | docs.github.com, *Publish actions in GitHub Marketplace*; metadata syntax |
| nuget.org links | Relative images are not rendered (docs). Relative links render with an empty address: NuGetGallery's `MarkdownService.cs` ("Allow only http or https links in markdown") clears any link that is neither absolute http(s) nor a `#` fragment | learn.microsoft.com, *Package readme on nuget.org*; NuGetGallery `main` at `e5977c4` |
| Alerts | "Alerts cannot be nested within other elements." A docs maintainer lists pull requests among the places they render | docs.github.com, *Basic writing and formatting syntax*; github/docs#27919 |
| Comment size | 65,536 characters (the API's 422, "Body is too long (maximum is 65536 characters)", and a GitHub staff answer); the REST page states no limit | github.com/orgs/community/discussions/27190 |
| Notifications | Creating a comment "triggers notifications"; the update endpoint says nothing either way | docs.github.com REST *Issue comments* |
| `always()` | "Avoid using always for any task that could suffer from a critical failure… use the recommended alternative: `if: ${{ !cancelled() }}`" | docs.github.com, *Expressions* |
| `queue` on a job | The workflow syntax reference carries the `queue` text, word for word, in its `jobs.<job_id>.concurrency` section too, so a job-level group takes `queue: max`. The how-to's only `queue: max` example is workflow-level | docs.github.com, workflow syntax (web-sources §16) |
| actionlint's `main` | Not only the 1.7.12 release: `main` at `011a6d15` parses `group` and `cancel-in-progress` alone (`ast.go`'s `Concurrency`, `parse.go`'s `parseConcurrency`), so F16 holds until a release adds `queue` | rhysd/actionlint source (§17) |
| A name written twice to `$GITHUB_OUTPUT` | **The last line wins.** The runner reads the file's lines in order and assigns each to the step's outputs by name (`outputs[outputName] = new StringContextData(value)`) | actions/runner `main` at `15231bed`: `FileCommandManager.cs`, `ExecutionContext.cs`, `StepsContext.cs` (§18) |
| CodeQL for Actions | 26 queries. The default suite's `actions/missing-workflow-permissions` wants a `permissions` key, which both workflows have; `actions/code-injection` advises passing input through an environment variable, as `action.yml` does; `actions/unpinned-tag` (pin by commit) is only in the extended suites | codeql.github.com query help (§19) |
| The majors, again | Unchanged on 2026-09-28: v9, v7, v6, v7, v8 | `git ls-remote --tags` (§20) |

### 1.7 The documents around the action (READ)

- **Wiki `CI-Artifact-Upload.md`** (214 lines, wiki `86a77c1`): its GitHub example uploads with `if: always()`,
  which GitHub's own docs advise against (§1.6), where the action's README uses `!cancelled()` and says why. Its
  examples pin `checkout@v5`, `setup-dotnet@v5` and `upload-artifact@v5`. It does not mention the action: the PR
  held the wiki back until the template is on `main`, and the section it drafted is in no file here, so S6
  writes it again. `_Sidebar.md:139` and `Home.md:57` link the page, so no new page is needed.
- **`CHANGELOG.md`** has no `[Unreleased]` practice: every entry since 3.0 sits in a release's section. Its
  link definitions stop at 2.0.139-beta, and `[Unreleased]` is defined as `compare/v2.0.139-beta...HEAD`
  (the file's last four lines: 7170 at 3.31.9, 7231 at 3.31.10), so the PR's `## [Unreleased]` heading would
  render as a link to a 2.0 comparison. No 3.x heading has a definition, so each renders as plain text. The three
  2.0.13x definitions below it appear once each. (A first version of this plan said twice; it was re-read at the
  PR's base, the PR's head, 3.31.9 and 3.31.10, once each every time.)
- **No example project sets `PublishCiArtifacts`.** Only `src/` and the tests mention it. So nothing in this
  repository has uploaded a report the way the action's README tells a consumer to, and
  `ci-summary-preview.yml` uploads only the history fragments.
- **`CiArtifactPublisher.Publish`** (`src/Kronikol/Reports/CiArtifactPublisher.cs:59-69`) appends
  `reports-path=<directory>` and `reports-retention-days=<n>` to `$GITHUB_OUTPUT` once per test run. The
  action's README relies on exactly these two names.
- **This repository's own workflows** pin `checkout@v5`, `setup-dotnet@v5` and, in `ci-summary-preview.yml`,
  `upload-artifact@v4` and `download-artifact@v4`, with node24 forced by `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24`.

### 1.8 What later stages expect of 2.2 (READ)

| Stage | What it says about 2.2 | What that asks of this plan |
|---|---|---|
| 2.3 | "A history composite action in 2.2's directory, shape and test pattern… It reuses 2.2's 597 lines of test scaffolding" | The scaffolding (`ActionDefinition`, `PullRequest`, the node `Driver`) is private to `PrReportLinkActionTests`. Roadmap §5 (added the same day by another session) has 2.3 branch from the rebased 2.2 and extract it once, in 2.3, and only its YAML half carries over (§7). The store plan's §11 Q7 names `templates/github-actions/kronikol-history`, which matches; its §6.1 sketch, `uses: lemonlion/kronikol/actions/history@v3`, names a directory and a tag that do not exist |
| 2.4 | An Azure Pipelines template as "the sibling of #72's composite action" (store plan §6.1 item 2) | Nothing now; §7 records what the sibling has to answer |
| 7.4 | "Add its line, and the changed scenarios' diagrams, to the comment of 2.2" | F5: the comment is rebuilt whole; §7 |
| 9.4 | The dashboard's workflow template goes "into stage 2's directory" | The same directory and README pattern |
| 9.6 | Alerting is "`history gate` plus the #72 pattern" | Same |
| 13.2 | "2.2 and 2.3 are the action", listed on the Marketplace | The Marketplace lists only an action at a repository's root (§1.6), so the listing needs a repository of its own; §7 |

### 1.9 S2 and S3 rehearsed (RUN)

The same evening, in a scratch worktree, nothing pushed: PR #73 rebased onto `main` at `5ca0878a` (3.31.10),
`CHANGELOG.md` resolved as S1 says, then S2 and S3 as §4.2 and §4.3 describe them. The four diffs are
`PR_REPORT_LINK_PLAN.harness/rehearsal/*.patch`; applied in order to that rebased head they give the rehearsal's
tree exactly (checked). The output is `results-s2-s3-rehearsal.txt`.

- **Red first, each for the reason §4.2 gives.** With the facts alone, five failed: the extended README fact
  ("leaves its concurrency group on the default queue"), the sample fact ("no ```markdown sample"), the tag fact
  and the line-break fact (two writes where one is right), and the guard's `templates/README.md` case (the
  relative link). The guard's `nuget-readme.md` case passed, and so did the PR's 16 other facts.
- **Green with the fixes:** 22 of 22. Q5's end marker made its own fact red without its seven lines of script
  and green with them, 23 of 23. The live lane's fact was red until the workflow file existed, then 24 of 24. If Q5
  is no, the other three patches apply without its patch and pass 23 of 23.
- **All of `Kronikol.Tests` with all four patches:** 5,975 tests, `main`'s 5,951 and these 24. 5,967 passed, 5
  skipped, and the three root-only failures of §1.2. Nothing else.
- **actionlint 1.7.12, the newest release, rejects `queue`** (F16): `unexpected key "queue" for "concurrency"
  section. expected one of "cancel-in-progress", "group"`, at job and at workflow level. The PR's README
  workflow and this repository's four workflows pass it. `-ignore 'unexpected key "queue" for "concurrency"
  section'` clears it, and so does the same pattern under `paths:` in `.github/actionlint.yaml`. actionlint does
  not read `action.yml` as an action (it reports a workflow without `jobs`), but from the repository's root it
  checks the lane's `with:` against the action's declared inputs: a copy with `artifact-nam:` failed on the
  missing required input.
- **The lane's test step, run locally** with `GITHUB_ACTIONS` and `GITHUB_OUTPUT` set: 11 of 11 passed, and the
  output file got `reports-path=<the project's bin/Release/net10.0/Reports>` and `reports-retention-days=1`.
  That directory held 13 files, 1.7 MB: `TestRunReport.html`, `.json` and `.schema.json`, `Failures.md` and
  `.jsonl`, `Run.json`, `History.run.json`, `Specifications.html` and `.yml`, `ComponentDiagram.html`,
  `CiSummary.md`, `CLAUDE.md` and `AGENTS.md`. S3 check 3 knows what its zip should hold.
- **F12, Kronikol's half:** two Kronikol projects in one `dotnet test` (a solution filter) wrote two pairs of lines
  to the one output file, one pair per project, in the order the projects finished. The runner's half is in §1.6:
  the last line of a name wins.

### 1.10 GitHub's own records (READ)

Read the same evening through the GitHub tools and REST. Ids and details are in
`PR_REPORT_LINK_PLAN.harness/github-records.md`.

- **PR #73 now reads as conflicted:** `mergeable: false`, `mergeable_state: dirty`, `rebaseable: false`. Still no
  review, comment or label, and its body closes #72.
- **CodeQL analyses C# only.** PR #73's 30 checks are CI's 28 jobs, the CodeQL job and the code-scanning check.
  Nothing analyses the `actions` language, so no scanner has read `action.yml`, and none would read the S3
  workflow (§6).
- **No re-run in this repository has ever had an artifact.** There were four re-runs since 2026-09-03, none of them
  uploading, and CI Summary Preview, the one workflow that uploads, has never been re-run (723 of 723 runs at
  attempt 1). F13 cannot be settled from history here.
- **Artifact ids do not follow upload order.** In run 36348913868 the first artifact created has the third
  highest id. The action takes the newest by `created_at`, which is right; the id would not be.
- **An artifact's `expires_at` is counted from the run's creation, not from its upload**: all four of that run's
  artifacts expire at its `created_at` plus 90 days, to the second, where the uploads came 39 to 48 seconds
  later. One run, attempt 1. What a re-run's artifact gets is for S3's check 5 to record (F13).
- **Pull requests here have always been merged with a merge commit** (20 of 20), and `main` has moved by direct
  push since May. Rebase merging is allowed (`allow_rebase_merge: true`). `main` is protected, but it requires
  no status check and has no ruleset.
- **`main`'s CI is not reliably green** (F17). Of its eight CI runs on 2026-09-27, six failed. Four failed on E2E
  (Popups & Flows), and one on a first fix for it; 3.31.10 (`571a98dc`) ended both. The newest, on `5ca0878a`,
  failed one Core Tests fact of 5,951,
  `NodeJsPlantUmlRendererTests.Code_cache_is_created_on_first_run_reused_afterwards_and_regenerated_when_v8_rejects_it`
  ("miss" expected, "hit" found), which passed in §1.9's local run.

---

## 2. Findings

| # | Finding | Level | What it costs | Where fixed |
|---|---|---|---|---|
| F1 | **The README's calling job loses a lane when three link jobs meet.** It asks for a per-pull-request concurrency group with the default queue, which holds one running and one waiting job and cancels the waiting one when a third arrives (§1.6). Two lanes are safe. Three that finish within a link job's length of each other can drop one lane's update, and a matrix of three or more link jobs, all queued at once, drops one every time. The dropped lane's line keeps its previous run's link, or is missing if that was its first run on the pull request, until the lane runs again. The job shows as cancelled, which reads as noise, not as a lost link. **`queue: max` fixes it**: every job waits its turn, and the order they run in does not matter, because a line written by a newer run is never replaced by an older one. The fix is one line, which the newest actionlint rejects (F16) | WEB + INFERRED; S3 runs it | A stale or missing link on exactly the pull requests with the most lanes, the case #72 was written for | S2 |
| F2 | **The README's sample is not the comment the action writes** (P1): no alert, no footer | RUN | The first thing a reader compares with their own pull request disagrees with it | S2 |
| F3 | **The run tag cannot take a field it does not know** (P2). Any lane still pinned to this version would overwrite a newer line whose tag carries one. A field written **before** the run id is worse: this version then writes a second line for the same artifact (P6). So the fix comes with a rule for every later version: fields go after the run id, never before it | RUN | Frozen by the first tag that carries the action (rule 6); cheap now, a compatibility problem later | S2, with the rule in the script and the README |
| F4 | **A line break in `label`, `icon` or `heading` breaks the line's identity** (P3), so an older run replaces a newer link. A `with:` value can span lines in YAML | RUN | Only a workflow author can cause it, but it defeats the guard the action exists for | S2 |
| F5 | **The comment is rebuilt whole** (P4). Anything written into it outside the lines is dropped by the next run of any lane. By design, and undocumented | RUN | 7.4 cannot simply append its section; a person's note in the comment vanishes. **And the first tag freezes it** (rule 6): 7.4 means to add its section to this comment, and a lane still on the first tag would drop it on each of its runs | S2 documents it; Q5 decides whether the first tag keeps what follows an end marker; §7 |
| F6 | **`templates/README.md` is the `Kronikol.Templates` package README** (§1.4), and nuget.org renders the new paragraph's relative link with an empty address (§1.6) | READ + WEB | A dead link on the package page; a changelog line that is not true | S2, S4 |
| F7 | **The README's workflow pins old majors**: `checkout@v5` and `upload-artifact@v5` where v7 is current, `setup-dotnet@v5` where v6 is (§1.6). `github-script@v9` in `action.yml` is current | WEB | A template that looks unmaintained the day it ships, and copies that start two majors behind | S2 |
| F8 | **Nothing here has run the action on GitHub, and nothing here publishes a report the way its README says to** (§1.7). The in-memory GitHub is faithful where it was checked (the `name` filter gave 2, 1 and 0 artifacts on a live run, per the PR), but it cannot hold permissions, the bot's identity, rendering, re-runs or concurrency | READ | The chain `PublishCiArtifacts` → `reports-path` → `upload-artifact` → the action has never run in the repository that ships it | S3 |
| F9 | **The consumer's copy was not read.** The PR says the logic was hardened on a consumer repository before it was generalised. That repository could not be opened from this session | not checked | A fix made there after 2026-09-15 would be missing here | S0 |
| F10 | **The changelog's `[Unreleased]` link definition is from 2.0.139-beta** (§1.7), so the PR's heading renders as a link to a 2.0 comparison | READ | A small falsehood in the file every release edits | S4 |
| F11 | **The roadmap row's premises moved.** The README conflict is gone; "hours" was the rebase alone | RUN | The row understates 2.2 | S6 |
| F12 | **Several Kronikol test projects in one `dotnet test` step each append `reports-path`** (§1.9: a pair of lines per project), and the runner keeps the last line of a name (§1.6), so the upload carries only the reports of the project that finished last, which varies from run to run. The wiki's example and the action's README both run a bare `dotnet test` | RUN + WEB | A lane that silently links part of its reports, and a different part each time | S2 (a sentence in the action's README, rehearsed); S6 (the wiki) |
| F13 | **The README's re-run claim is unproven.** It says "a re-run keeps its run id and uploads again, so the newest artifact of that name is linked". GitHub documents nothing about a same-name upload in a later attempt, and the evidence conflicts (§1.6). If the upload fails, the re-run's report is never uploaded and the comment keeps linking the first attempt's. Picking the newest of several is right either way. This repository's history cannot answer it: no re-run here has ever had an artifact (§1.10). And an artifact's expiry is counted from the run's creation, so a re-run's upload may expire sooner than its upload time suggests | WEB, READ | A re-run of a failed lane, the case most worth linking, may not reach the comment, or may reach it already short-lived | S3 check 5, which also records the re-run's expiry; S2 adds `overwrite: true` if the upload fails |
| F14 | **The issue's reason for ruling out a browsable report no longer holds** (§1.6, `archive: false`), and the action's README still says "GitHub serves artifacts only as downloads" | WEB | Not a defect of 2.2's code. Possibly the largest improvement on offer for 13.2 | S2 corrects the README's sentence; §8; S3 check 9 measures it |
| F15 | **`heading` and `report-file` belong to the comment, but each lane passes its own** (P5). The comment shows whichever lane wrote last, so lanes that differ swap its heading and its tip on every run, and the tip can name a file another lane's zip does not hold | RUN | A wrong instruction, half the time, in a comment shared by lanes that differ | S2 says so in the inputs table. Not frozen: a later version can name the file in each line, which a lane on this version keeps as it is |
| F16 | **The newest actionlint rejects `queue: max`** (§1.9): 1.7.12 knows only `group` and `cancel-in-progress`, at job and at workflow level. A consumer whose CI lints workflows fails on the README's job as soon as it is copied | RUN | F1's fix costs such a consumer one ignore line | S2: the README gives the line; S0 re-checks actionlint |
| F17 | **The baseline S1 and S5 lean on is not there**: `main`'s CI failed six of its eight runs on 2026-09-27, the newest on a Core Tests fact the PR does not touch (§1.10). "Green on the rebased head" can be out of the PR's reach | READ | A red S1 read as rebase fallout, or S5 held on a failure that is `main`'s | S1 step 5 and S5 read the PR's runs against `main`'s at the same base; §6 |

Checked and **not** a finding: `github-script@v9` exists and is current, and v9's breaking changes touch
nothing the script does; `pull-requests: write` is enough to comment; the README's job works on Dependabot's
pull requests, because a `permissions` block raises Dependabot's token (§1.6); the script takes the newest
artifact by `created_at` rather than by id, which is right, because ids do not follow upload order (§1.10); both
`github.paginate` calls ask for 100 a page, so a busy pull request's comment is still found.

---

## 3. Questions for the owner (`ROADMAP.md` D25)

D25, because the doorstep plan (`DOORSTEP_PLAN.md`, row 2.1) took D24 on `main` the same day.

**Q1. The version.** Merge with no bump, as the PR and the roadmap say, so that the action first appears in the
tag of whichever release follows the merge? And from that tag on, treat the action's inputs, the README's calling
workflow and the comment's format as public surface under the repository's semver: an added input is a minor;
a removed or renamed input, or a comment format older versions cannot read, is a major, and so waits for v4
(rule 7)?

*Recommendation: yes to both.* Nothing in a package changes except one README paragraph, a documentation
change, and a release that ships identical packages under a new number tells consumers nothing. But the tag is
how a consumer references the action (`uses: lemonlion/Kronikol/templates/github-actions/kronikol-pr-report-link@<tag>`),
so its interface follows the tag's semver whether or not a package moved. The consequence is the reason F3 is
fixed now: whatever the first tag carries can only be extended after it.

**Q2. A live lane in this repository.** A permanent workflow, `.github/workflows/pr-report-link.yml`, that runs
only on pull requests touching the action or itself. It runs the CI Preview all-passing example with
`PublishCiArtifacts = true`, uploads the reports and calls the action by its path. Or a one-off proof on PR #73
alone.

*Recommendation: permanent.* It is the only test of what the in-memory GitHub cannot hold (F8). It costs nothing
on a pull request that does not touch the action, it runs on PR #73 itself as the proof, and 2.3, 7.4 and 9.6
will change or copy this action (rule 3: the net goes up before the work it protects). It comments only on pull
requests that change the action. It must not be made a required check: a workflow filtered by `paths` that does
not run leaves a required check waiting.

**Q3. The changelog.** An `[Unreleased]` section at the top, which the next release folds into its own section,
with the stale `[Unreleased]` definition deleted so the heading reads as text like every 3.x heading?

*Recommendation: yes.* It is the first entry since 3.0 without a release, and `[Unreleased]` is Keep a
Changelog's own convention for one. The alternative, writing it into a release section at merge time, needs a
version number nobody knows yet. The release that folds it is the first tag that carries the action, which S6
records.

**Q4. The consumer.** Once a tag carries the action, should the consumer it was generalised from replace its
copy of the logic with `uses: lemonlion/Kronikol/templates/github-actions/kronikol-pr-report-link@<tag>`?

*Recommendation: yes.* It removes the second copy, which is how the two would drift (F9), and it is the second
live proof, on two real lanes.

**Q5. What a lane on the first tag does with content it does not know.** The first tag freezes it (Q1), and
roadmap 7.4 means to add "its line, and the changed scenarios' diagrams, to the comment of 2.2". As PR #73
stands, every lane rebuilds the comment whole (F5), so a lane still on the first tag would drop 7.4's section on
each of its runs. Should the action end its part of the comment with a hidden marker, `<!-- <comment-key>:end -->`,
keep whatever follows the marker as it is, and read lines only above it?

*Recommendation: yes.* It is seven lines of script and one fact, rehearsed (`rehearsal/s2-3-q5-end-marker.patch`,
§1.9), and it decides nothing about 7.4's section except where it goes. Without it, 7.4 writes a comment of its
own, which stays possible either way, or changes this comment's format in v4 (rule 7). A reader sees nothing new:
the marker is one more hidden line.

The rebase itself needs no decision: the roadmap says rebase, and the branch is the owner's own single commit.
If the owner would rather not have the branch force-pushed, S1 merges `main` into it instead and nothing else
changes.

---

## 4. The slices

### 4.0 S0: re-check (a quarter of an hour)

Premises expire (roadmap §8). Before any change:

1. `git fetch origin main pr-report-link-template`. The PR head is still `e31b42fa`, with no review or comment.
   If either moved, read what moved first.
2. A trial merge on `origin/main` still conflicts in `CHANGELOG.md` only.
3. **The consumer's copy (F9).** Diff the consumer's current link logic against the script in `action.yml`. Port
   any change made there after 2026-09-15 into S2, each with a fact. If the two are the same apart from the
   generalisation, say so in the execution log.
4. The majors in §1.6 are still the current ones (`git ls-remote --tags` on each action), and `queue: max` is
   still documented as §1.6 quotes it.
5. **`main`'s CI (F17).** The newest CI run on `main`, and which jobs are red there. S1 and S5 read the PR's runs
   against it.
6. **actionlint (F16).** Whether its newest release knows `queue` yet. If it does, S2's README drops its ignore
   line and §5's pre-push check its `-ignore`.
7. **The rehearsal's patches.** `git apply --check` of `rehearsal/*.patch` on the rebased head. They were made on
   `5ca0878a`; a patch that no longer applies is redone by hand from §4.2 and §4.3, which say the same.

### 4.1 S1: onto `main`

1. `git switch -C pr-report-link-template origin/pr-report-link-template`, then `git rebase origin/main`.
2. The one conflict, `CHANGELOG.md`: keep `main`'s file whole and put the PR's `## [Unreleased]` block above
   the newest release's section (`## [3.31.10] - 2026-09-27` at the last check) (Q3). S4 rewrites its words.
3. Run the 17 facts, then the whole project:
   - `dotnet test tests/Kronikol.Tests --configuration Release --filter "FullyQualifiedName~PrReportLinkActionTests"`:
     17 pass, none skipped.
   - `dotnet test tests/Kronikol.Tests --configuration Release`: `main`'s count plus 17 (5,951 on `5ca0878a`, so
     5,968). On Linux as root, expect §1.2's five skips and its three root-only failures, and nothing else.
4. `git push --force-with-lease=pr-report-link-template:e31b42fa origin pr-report-link-template`. The explicit
   expected head makes the push refuse, rather than overwrite, if anyone pushed to the branch meanwhile. A
   Claude Code session may be refused this push (§4.7).
5. CI on the rebased head is green wherever `main`'s own run at the same base is green: 28 of 28 jobs and CodeQL
   when `main` has them. A job red on `main` too (F17) is not the PR's: it is named once on the PR, with `main`'s
   run, and not fixed in 2.2. This run is the baseline that tells rebase fallout from S2's.

### 4.2 S2: the fixes

Each fact is written first and seen to fail on the rebased head, then the change makes it pass. All of them live
in `PrReportLinkActionTests`, except the guard. **Rehearsed** (§1.9): `rehearsal/s2-1-facts.patch` is the facts,
which fail as the table says; `s2-2-fixes.patch` the changes; `s2-3-q5-end-marker.patch` Q5's, if the owner says
yes. The executing session applies the facts first and sees the same five failures before it applies a fix.

| Fact | Red on the rebased head because | The change |
|---|---|---|
| `The_readme_workflow_calls_the_action_as_it_is_declared_from_a_job_that_can_use_it`, **extended** | F1: the calling job's `concurrency` has no `queue: max` | The README's calling job gains `queue: max`. The fact also asserts that `cancel-in-progress` is not `true`, which GitHub refuses beside `queue: max`, and that the job's `if` keeps the fork guard, without which every fork's pull request would fail the job. Its checks move into a helper, `CallsOfTheAction`, which the live lane's fact reuses (S3) |
| `The_readme_shows_the_comment_the_action_writes` | P1: the README's sample has no alert and no footer | The README's blockquote becomes the raw comment in a ```` ```markdown ```` fence, written by the driver for the README's two lanes with fixed dates, links included, and one sentence on how it renders (a list, a green Tip box, a small footer). A blockquote cannot show it faithfully, because an alert cannot be nested in a quote (§1.6). The fact renders the same two lanes and compares the fence byte for byte |
| `A_tag_with_a_field_this_version_does_not_know_still_stops_an_older_run` | P2: run 400 overwrites a line whose tag gained ` x:1` after the run id (the driver's new `tag-field` edit) | The run id is read as the digits straight after the line's own tag prefix, `<!-- <key>:<artifact> run:`, whatever follows them, by position, not by a pattern built from the artifact's name. A comment beside it states the rule for later versions: fields only after the run id (P6) |
| `A_line_break_in_an_input_keeps_the_line_whole` | P3: run 550 replaces run 600's link | `label`, `icon`, `heading` and `report-file` are collapsed to one line (each run of whitespace to one space, trimmed) before use, by one `oneLine` helper |
| `A_package_readme_links_only_to_absolute_addresses`, a theory in `Packaging/PackageReadmeLinkTests` | F6: the new paragraph's relative link | The link becomes `https://github.com/lemonlion/Kronikol/tree/main/templates/github-actions/kronikol-pr-report-link`. The guard finds each package README the way `dotnet pack` does, through every `PackageReadmeFile` in `Directory.Build.props` and the projects under `src/` and `templates/` (today `nuget-readme.md` and `templates/README.md`, one case each), and fails on any link, reference definition, `href` or `src` outside a code fence that is neither `http(s)://` nor a `#fragment`: nuget.org's own rule (§1.6) |
| `What_follows_the_end_marker_survives_every_rewrite_and_holds_no_line`, **if Q5 is yes** | P4: what follows the lines is dropped (the driver's new `section-after-end` edit appends a section holding a decoy line with this lane's tag and a newer run) | The body ends with `<!-- <comment-key>:end -->`; whatever follows the marker in the existing comment is appended as it is, and lines are read only above it. The README's sample gains the marker, so the sample fact holds it too |

And in the template README, with no fact of their own beyond the extended one (the sample fact holds the only
prose a test can):

- **The majors (F7).** `checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7`, as S0 re-checks them. The live
  lane (S3) uses the same, so it proves them.
- **One project per upload (F12).** Under "Use it": an output holds one value, the last written, so a step that
  runs several Kronikol test projects uploads only the one that finished last; each project gets its own step,
  upload and lane, or the runs are combined with `kronikol merge <inputs…> --publish-artifacts`, which writes the
  same two outputs.
- **Concurrency (F1).** "`cancel-in-progress` stays off, so the second job waits instead of being cancelled"
  becomes what GitHub does: with the default queue, a third job that arrives while one runs and one waits
  cancels the waiting one; `queue: max` keeps them all, in an order that does not matter because an older run
  never replaces a newer line. It names the date the key arrived, 2026-05-07, for anyone on a GitHub that lacks
  it.
- **actionlint (F16).** In the same paragraph: actionlint does not know `queue` yet (1.7.12), so run it with
  `-ignore 'unexpected key "queue" for "concurrency" section'`, or put that pattern under `paths:` in
  `.github/actionlint.yaml`. Both were run (§1.9).
- **The inputs that belong to the comment (F15).** The inputs table says `heading` and `report-file` are the
  comment's: every lane that shares it passes the same, or the comment shows whichever ran last.
- **The tag's rule and one-line inputs (F3, F4).** "How it behaves" adds that a later version adds fields to a
  line's tag only after the run id, and that an input given over several lines is written on one.
- **Re-runs (F13).** The sentence on re-runs waits for S3's check 5 and says what it found. If a same-name upload
  fails in a re-run, the upload step gains `overwrite: true`, and the README says the earlier attempt's artifact
  is replaced.
- **Without a checkout.** One sentence: on github.com, `uses: $/.github/actions/kronikol-pr-report-link` runs the
  copied action at the commit being tested with no checkout step (runner 2.336.0 or later, not GitHub Enterprise
  Server, §1.6). The checkout form stays the example, because it works everywhere.
- **Limits.** Three lines: the comment is the action's, and anything else written into it is dropped by the next
  run, so other content takes its own `comment-key` (F5; with Q5, "down to its end marker", and other content
  may go after the marker); the links download a zip, for a signed-in reader who can read the repository, which
  replaces the README's "GitHub serves artifacts only as downloads", untrue since 2026-02-26 (F14, §1.6);
  `label`, `icon` and `heading` are written into the comment as Markdown, so pass fixed text, never a value a
  pull request controls, such as its branch name or title. (No code can be injected, since the inputs reach the
  script as environment variables. This is about what the comment says.)

### 4.3 S3: the live lane, and the proof on PR #73

**The workflow** (Q2), `.github/workflows/pr-report-link.yml`, as rehearsed (`rehearsal/s3-lane.patch`):

```yaml
name: PR report link

# Runs the kronikol-pr-report-link action for real, on the pull requests that change it. The in-memory GitHub of
# PrReportLinkActionTests cannot hold permissions, the bot's identity, re-runs or concurrency. Not a required
# check: a workflow its paths filter skips never reports, and a required check would wait for it.
on:
  pull_request:
    branches: [ main ]
    paths:
      - 'templates/github-actions/kronikol-pr-report-link/**'
      - '.github/workflows/pr-report-link.yml'

permissions:
  contents: read

jobs:
  test:
    name: CI Preview, with its reports uploaded
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7

      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      - name: Test
        id: test
        run: dotnet test examples/Example.Api/tests/Example.Api.Tests.CiPreview.AllPassing --configuration Release

      # `error`, where the README's example says `ignore`: here a reports path that went missing must fail the lane.
      - name: Upload the Kronikol reports
        if: ${{ !cancelled() }}
        uses: actions/upload-artifact@v7
        with:
          name: ci-preview-reports
          path: ${{ steps.test.outputs.reports-path }}
          retention-days: ${{ steps.test.outputs.reports-retention-days }}
          if-no-files-found: error

  report-link:
    name: Link the Kronikol report on the PR
    needs: [test]
    if: ${{ !cancelled() && github.event_name == 'pull_request' && github.event.pull_request.head.repo.full_name == github.repository }}
    runs-on: ubuntu-latest
    permissions:
      contents: read
      actions: read
      pull-requests: write
    concurrency:
      group: kronikol-report-link-${{ github.event.pull_request.number }}
      queue: max
    steps:
      # The checkout form, not `$/`, because this lane proves what the README tells a consumer to copy.
      - uses: actions/checkout@v7
        with:
          sparse-checkout: templates/github-actions

      - uses: ./templates/github-actions/kronikol-pr-report-link
        with:
          artifact-name: ci-preview-reports
          label: CI Preview (all passing)
```

- `Example.Api.Tests.CiPreview.AllPassing/Infrastructure/TestRun.cs` gains `PublishCiArtifacts = true`, with a
  comment naming the lane. Kronikol has no environment switch for the option, so the project carries it. On
  GitHub that adds two `$GITHUB_OUTPUT` lines to `ci-summary-preview.yml`'s run of the same project, which
  nothing there reads, and no test reads the example's options. The all-passing project is chosen so that the
  lane is green when the action is.
- `if-no-files-found: error`, not the README's `ignore`: here a broken `reports-path` must fail the lane.
- The checkout form, not `$/`, because the lane proves what the README tells a consumer to copy.
- **Its fact,** `The_live_lane_calls_the_action_as_it_is_declared`: the workflow calls the action once, through
  the README fact's `CallsOfTheAction` (declared inputs, every required one, the per-pull-request group,
  `queue: max`, no `cancel-in-progress`, the fork guard, the two permissions), and its `paths` filter names the
  action's directory and the workflow itself. Rehearsed red (the file missing) and then green (§1.9).
- `actionlint` on the workflow, with F16's `-ignore`: the `queue` key is its only complaint (§1.9).
- **Rehearsed without GitHub** (§1.9): the test step, run locally with `GITHUB_ACTIONS` and `GITHUB_OUTPUT` set,
  passes and writes both outputs, and the directory it names holds 13 files. What only GitHub can show is the
  live proof below.

**The live proof**, once S2 and S3 are pushed to PR #73 (the lane runs on the pull request that adds it). Each
check goes into the execution log with its run and artifact ids:

1. One comment, by `github-actions[bot]`, whose body opens with `<!-- kronikol-report-link -->` and whose line
   links `…/actions/runs/<run>/artifacts/<id>`. The alert renders as a Tip box (A7).
2. The id, upload time and expiry match
   `GET /repos/lemonlion/Kronikol/actions/runs/<run>/artifacts?name=ci-preview-reports`, to the minute, in UTC.
3. The link downloads a zip holding the 13 files §1.9 lists, `TestRunReport.html`, `Failures.md` and `Run.json`
   among them.
4. Re-running the link job alone edits the same comment: still one comment.
5. **Re-runs (F13).** Re-run the whole workflow: record what the same-name upload does in attempt 2 (a second
   artifact, a replacement, or a failure), what the line links afterwards, and the new artifact's `expires_at`
   against the attempt's start (§1.10: attempt 1's expiry is counted from the run's creation). Then "re-run
   failed jobs" on a scratch commit whose test step fails, for the same record. This repository has no re-run
   with an artifact to learn from, so this is the first. The README's sentence (S2) is written from it.
6. A new commit: the line moves to the new run.
7. Re-running the older run's link job from the Actions page: its log says a newer run already linked, and the
   comment is unchanged.
8. **F1, run rather than read.** A scratch commit adds a matrix of three link jobs in the one group, with three
   artifact names, all queued at once: first with the PR's original concurrency block, where one job is expected
   to be cancelled and its line to be missing; then with `queue: max`, where all three lines must appear. The
   scratch commit is reverted before S5.
9. **Optional, for §8.** A scratch step uploads `TestRunReport.html` alone with `archive: false` and opens its
   link: does the page load, do its scripts run, do diagrams draw (the engine comes from jsDelivr), does the
   search work. A screenshot and the browser console go into the execution log. Reverted before S5.

F12 needs no scratch step: §1.9 ran two projects in one `dotnet test` and §1.6 read the runner's rule, so the
README says it (S2) and the wiki will (S6).

### 4.4 S4: the documents in the pull request

- **`CHANGELOG.md`**, the `[Unreleased]` entry (Q3), reworded: "No version change: a GitHub Actions template in
  the repository. The action ships in no package; the `Kronikol.Templates` package README gains a paragraph
  that points at it." The Added bullet keeps the PR's content and gains the live lane, `queue: max`, the tag's
  rule and, if Q5 is yes, the end marker; the tests line gains the package README guard. The line
  `[Unreleased]: https://github.com/lemonlion/Kronikol/compare/v2.0.139-beta...HEAD` is deleted (F10); the three
  2.0.13x definitions below it stay, since their headings use them.
- **The template README**: S2's changes, and the "reference it from a Kronikol release tag" sentence unchanged. It
  names no tag, and it becomes true with the first tag (S6).
- **The root `README.md`**: the PR's sentence stands.
- **The PR body**: the owner's text, so changed only with the owner's go-ahead. What moved: the rebase, the
  fixes, the lane, and its "Not verified in this repository" answered by the live proof.

### 4.5 S5: the merge

With the owner's go-ahead, once CI (28 of 28, or every job `main` passes at the same base, F17), CodeQL and the
lane are green on the final head and the live proof is logged: merge through the pull request, so that "Closes
#72" closes the issue. The repository allows every method (§1.10). Its pull requests, 20 of 20, have landed as
merge commits, and `main` has moved by direct push since May, so its first-parent history is linear. **Rebase and
merge** keeps it so and keeps S1 to S4's commits apart; a merge commit, the owner's old habit, groups them under
one. After S1's rebase both give the same tree, so the owner picks at the button. No tag and no version (Q1).
Nothing is posted on #72: the merge closes it.

### 4.6 S6: after the merge

1. **The wiki**, which publishes the moment it is pushed and so waits for the merge. It is one checkout, shared
   by every worktree: from a worktree under `.claude/worktrees/`, `../Kronikol.wiki` does not resolve, and
   `tools/wiki-links/wikilinks.py` reads `KRONIKOL_WIKI` (roadmap §5). `CI-Artifact-Upload.md`
   gains "Link the report on the pull request" after "Combining with CI Summary": what the action does, the
   calling job and why each part is there, taken from the template README, which the section links rather than
   copying the workflow a third time, and the limits. The words are drafted in
   `PR_REPORT_LINK_PLAN.harness/wiki-draft.md`, with the sentence S3's check 5 decides marked. The page's own examples move to the same majors (F7) and
   its upload step from `if: always()` to `if: ${{ !cancelled() }}`, with GitHub's reason (§1.6). F12's sentence:
   an output keeps the last value written, so several test projects in one step upload only the one that
   finished last; they run in their own steps, or are combined with `kronikol merge <inputs…>
   --publish-artifacts`, which writes the same two outputs (`MergedRunOutputs`). `wiki-draft.md` has the words.
2. **The first tag.** When the next release folds the `[Unreleased]` section (Q3), record its tag in
   `ROADMAP.md` 2.2 as the first that carries the action. From that tag the template README's "reference it from
   a release tag" is true.
3. **`ROADMAP.md`**: 2.2 struck with the merge and the tag; D25 moved into the stage.
4. **`PLANS_STATUS.md`**: this plan's row, executed.
5. **The consumer** (Q4): its copy replaced by the tag, and its two lanes' next comment checked as S3's check 2
   checks this repository's.

### 4.7 Who can run each step

This plan was written in a Claude Code session. The same environment, the same day, refused to push a tag
(`v3.31.10`) and the wiki (HTTP 403), where pushes to `main` went through (`DOORSTEP_PLAN.md` Appendix B). So:

| Step | Pushes to | From a Claude Code session |
|---|---|---|
| S1 step 4, S2 to S4 | `pr-report-link-template`, the owner's branch, with a force-push in S1 | Not tried: the first try would overwrite the owner's branch. If refused, the session pushes the same commits to its own branch and the owner moves the pull request's branch to them: `git push --force-with-lease=pr-report-link-template:e31b42fa origin <sha>:pr-report-link-template` |
| S3's live proof | the same branch (scratch commits for checks 5, 8 and 9, then their reverts) | As above. Re-running jobs (checks 4, 5, 7) is a click on the Actions page, the owner's if the session has no way to re-run |
| S5 | the merge button | The owner's (the go-ahead is theirs anyway) |
| S6 step 1 | the wiki | Expected to be refused, as it was for 3.31.10; the owner pushes `wiki-draft.md`'s text |
| S6 steps 2 to 4 | `main` (plan files) | Allowed, as this plan's own commits were |

---

## 5. Tests

| Fact | Slice | Proved red on | Kind |
|---|---|---|---|
| The 17 of PR #73 | S1 | the tree before the PR (the PR: all 17 failed before the action existed; each guard broken in turn fails at least one) | node driver, YAML |
| The README workflow fact, extended with `queue: max`, `cancel-in-progress` and the fork guard | S2 | the rebased head (F1); rehearsed | YAML |
| `The_readme_shows_the_comment_the_action_writes` | S2 | the rebased head (P1); rehearsed | node driver, README |
| `A_tag_with_a_field_this_version_does_not_know_still_stops_an_older_run` | S2 | the rebased head (P2); rehearsed | node driver |
| `A_line_break_in_an_input_keeps_the_line_whole` | S2 | the rebased head (P3); rehearsed | node driver |
| `A_package_readme_links_only_to_absolute_addresses`, two cases | S2 | the rebased head (F6), the `templates/README.md` case; rehearsed | file read |
| `What_follows_the_end_marker_survives_every_rewrite_and_holds_no_line`, if Q5 is yes | S2 | the fixed head without the marker's seven lines; rehearsed | node driver |
| `The_live_lane_calls_the_action_as_it_is_declared` | S3 | the head before the workflow exists; rehearsed | YAML |

The node facts skip where node is missing, as the 17 do (`NodeProbe`), and run in CI's Core Tests job, where node
is present. A fact that skipped everywhere would prove nothing, so CI's log is read for the count that ran in
`PrReportLinkActionTests`: 17 after S1, 20 after S2 (three new node facts; the extended fact needs no node), 21 if
Q5 is yes, and one more after S3. `PackageReadmeLinkTests` adds its two cases. The rehearsal ran 24 across both,
with Q5 (§1.9).

**Before each push**, the repository's own fast checks: the facts above; the whole of `Kronikol.Tests`;
`actionlint -ignore 'unexpected key "queue" for "concurrency" section'` on the README's workflow and
`pr-report-link.yml`, from the repository's root (actionlint reads workflows, not `action.yml`, but from the root
it holds the lane's `with:` to the action's declared inputs, as a typo proved, §1.9); the diff re-read for
anything CI would reject.
One validated push beats three speculative ones.

---

## 6. Found beside it

- F10 and F12 are defects of documents that predate PR #73. Both are fixed by this plan (S4, S6), because they
  sit in what it edits.
- **This repository's own workflows pin old majors** (§1.7): `upload-artifact@v4` and `download-artifact@v4`
  where v7 and v8 are current, `checkout@v5` and `setup-dotnet@v5` where v7 and v6 are. Not a defect today. A
  sweep belongs with the next change to `ci.yml` (roadmap 3.1, the same track), not here, because this plan
  would otherwise touch every CI job to land a template. The wiki's other pages with workflow examples go with
  that sweep.
- **`templates/` has two jobs now.** It is the `dotnet new` package's source and, from this plan, the home of
  copy-me CI templates. Only the `.csproj`'s explicit folder list keeps the two apart; a future
  `<Content Include="**\*" />` would pack the action. When a second CI template arrives (2.3), a check that no
  packed path starts with `github-actions/` belongs in CI's template-pack step, beside its agent-file checks.
- **Three tests fail whenever the suite runs as root** (§1.2), on `main` as on the PR. Each makes a file or
  directory read-only and expects the write to be refused, and root is never refused. CI runs unprivileged, so it
  never sees this; a container that runs as root, such as the one this plan was written in, does. The fix is the
  one the Windows-only tests already use: skip with a reason when the process runs as root, so such a run reports
  three skips rather than three failures. A test-only change, outside this plan.
- **A flaky fact on `main`** (F17, §1.10):
  `NodeJsPlantUmlRendererTests.Code_cache_is_created_on_first_run_reused_afterwards_and_regenerated_when_v8_rejects_it`
  failed CI on `5ca0878a` ("miss" expected at line 165, "hit" found) and passed in the local full run that
  evening. The fact deletes the process-wide V8 code cache, renders, and reads
  `NodeJsPlantUmlRenderer.LastCodeCacheStatus`, a static with a private setter, while other classes may render with
  node beside it (eight more test files name the node renderer, and the class has no collection to hold them
  apart). A render from another
  class between the delete and the fact's own render writes the cache back, and the fact reads "hit"
  (INFERRED from the code; not reproduced). Not the PR's. A test-only fix, putting the fact in the collection
  that owns the renderer's process-wide state or giving it a cache path of its own, belongs to whoever next
  touches the renderer's tests; until then S1 and S5 treat it as `main`'s.
- **CodeQL analyses C# only** (§1.10). Adding `actions` to `codeql.yml`'s languages would scan every workflow and
  composite action, the S3 lane and this action included. What 2.2 adds should pass its default suite: both
  workflows declare `permissions`, and the action passes its inputs through `env` as `actions/code-injection`
  advises (§1.6). The existing workflows are another matter, not read here. A change to `codeql.yml`, outside
  2.2; it fits with the workflow sweep above.
- **Two packing leftovers in `Kronikol.Templates`**, seen in §1.4's pack and older than PR #73: `LICENSE` is
  included twice (`Directory.Build.props:29` and the project's own line 65), so `dotnet pack` warns NU5118; and
  the package carries `nuget-readme.md` from `Directory.Build.props` beside the `README.md` its nuspec names, a
  file nothing reads. Both harmless; each is one line to remove, in whichever change next edits the project.

---

## 7. What 2.2 hands on

- **To 2.3, the history action.** The directory is `templates/github-actions/`, as the store plan's §11 Q7 says;
  its §6.1 sketch (`lemonlion/kronikol/actions/history@v3`) is corrected when 2.3 is planned, since no
  `actions/` directory and no floating `v3` tag exist. Roadmap §5 sets the order: 2.3 branches from 2.2 once 2.2
  is rebased, and they merge as 2.2, 2.3, 2.4. The scaffolding is extracted then, not before, and only its YAML
  half: `ActionDefinition` (the inputs, their `env` wiring, the workflow assertions) becomes shared. The node
  driver imitates actions/github-script and stays with this action, since the history action wraps a git fold and
  wants real repositories in a temporary directory (roadmap §5). A live lane like S3's suits it too, but the
  history action pushes a branch with `contents: write`, so its lane pushes to a scratch branch and never to
  `kronikol-history`.
- **To 2.4, Azure DevOps.** The sibling has to answer what the GitHub version gets from the platform: whose
  identity writes the comment (the author check rests on `github-actions[bot]`), which permission the build
  identity needs on pull request threads, and how the thread is found again. The store plan marks the Azure
  DevOps platform claims REFERENCE; 2.4's half day of checking covers these.
- **To 7.4, the changed scenarios.** The comment is rebuilt whole (F5). If Q5 is yes, 7.4's section goes after
  the end marker, where a lane on the first tag keeps it, and 7.4 designs the section itself then, with Q1's
  semver in view. If Q5 is no, 7.4 writes its own comment under its own `comment-key`, or waits for v4. A comment holds 65,536 characters (§1.6), where a step summary holds the 1 MiB that
  `V4_PLAN`'s 768 KiB budget is set against, so diagrams in a comment need a budget of their own.
- **To 9.4 and 9.6.** The same directory, README pattern and tests; alerting reuses the comment machinery.
- **To every later version of this action.** A line's tag takes new fields only after its run id (F3), new
  content goes after the end marker (Q5), and `heading` and `report-file` stay the comment's until a version
  names the file in each line (F15). The README's actionlint line goes when actionlint knows `queue` (F16).
- **To 13.2, the Marketplace.** Only an action at the root of a public repository is listed (§1.6), so the listing
  is a repository of its own, for example one that holds this action at its root, with a unique name and
  optional `branding`. Consumers who reference the path in this repository at a tag keep working, because a tag
  does not move. Whether §8's browsable report is part of what is listed is decided there, on S3's measurement.

---

## 8. Not doing, and why

- **A browsable report, in 2.2.** The issue ruled it out because "artifacts only download as a zip". Since
  2026-02-26 that is no longer so: `upload-artifact@v7` uploads one file unzipped with `archive: false`, and
  GitHub shows it in the browser when the browser can render the type, in GitHub's words "great for simple HTML
  files (without links to CSS or JS)". A Kronikol report is one file, but its scripts fetch the PlantUML engine
  from jsDelivr and decode gzip blocks in the page, and how GitHub serves such a page (its content type, any
  content security policy, any sandbox) is not documented. **Measure before designing**: S3's check 9. If the
  report works, "click the line, read the report" deserves its own roadmap row ahead of 13.2, as a new input (a
  second, unzipped artifact per lane, named by its file, so lanes in one run need distinct file names). It
  would be the difference between a demo and a download. If it does not, the reason is worth a line in the
  wiki.
- **Taking the artifact id from the upload step** (`artifact-id` is an output of `upload-artifact`) instead of
  listing by name. It would settle F13 by construction, but the link job is a separate job, so the id would have
  to travel through job outputs: more YAML for every consumer, for a case S3 measures first.
- **A token input, and a `workflow_run` workflow for fork pull requests.** The issue rules both out, for reasons
  that still hold: the author check relies on the comments `GITHUB_TOKEN` writes, and `workflow_run` runs only
  from the default branch, so a pull request can never exercise a change to it.
- **Extracting the test harness now.** One consumer; 2.3 is the second (§7).
- **Marking expired lines.** Each line already says when it expires.
- **A floating major tag, `branding`, a repository of its own.** 13.2's questions.
- **An Azure DevOps equivalent.** 2.4.
- **Upgrading this repository's own workflows.** §6.

---

## 9. Assumption ledger

| # | Assumption | Standing | How it is settled |
|---|---|---|---|
| A1 | Run ids only grow, across workflows, so a larger id is a newer run | PLAN: the consumer's runs agree; GitHub documents no order | A rule of the action, held by its facts; S3 checks 6 and 7 see it live |
| A2 | `pull-requests: write` lets the workflow token create and edit an issue comment on a pull request | WEB (§1.6) | S3 check 1 |
| A3 | The run artifacts endpoint's `name` filter is exact | PLAN: 2, 1 and 0 artifacts on a live run, per the PR; the docs do not say | S3 check 2 |
| A4 | The consumer's copy has not moved since 2026-09-15 | not checked (F9) | S0 step 3 |
| A5 | The default queue cancels a waiting job when a third arrives, and `queue: max` does not | WEB (§1.6) | S3 check 8 |
| A6 | nuget.org renders a relative link with an empty address | WEB, from NuGetGallery's source (§1.6) | Moot once the S2 guard holds |
| A7 | An alert renders in a pull request comment | WEB, a docs maintainer's word (§1.6) | S3 check 1, by eye |
| A8 | Editing a comment notifies nobody, so the action's rewrites are silent after the first | WEB for creation only; editing unverified | Needed by no step; noted because a noisy comment would be a reason to use the action less |
| A9 | Several `reports-path` lines in one step leave one value (F12) | RUN: a pair of lines per project (§1.9). WEB: the runner keeps the last line of a name (§1.6, its source) | Settled |
| A10 | The 17 facts still pass on today's `main` | RUN: 17 of 17 on the rehearsed tree (§1.2) | Settled; S1 step 3 runs them again at execution |
| A11 | The README's upload works in a re-run (F13) | WEB: undocumented, evidence conflicts. READ: no re-run in this repository has had an artifact (§1.10) | S3 check 5 |
| A12 | A job-level concurrency group takes `queue: max` | WEB: the workflow syntax reference documents `queue` in the `jobs.<job_id>.concurrency` section (§1.6); no example shows it on a job | S3 checks 1 and 8 run it |
| A13 | actionlint accepts the README's workflow | RUN: 1.7.12 rejects `queue` (F16); `-ignore` and the `paths` config clear it (§1.9) | Settled for 1.7.12; S0 step 6 re-checks the newest |
| A14 | A re-run's artifact lives a retention period from its upload | READ: attempt 1's `expires_at` is the run's `created_at` plus the retention, not the upload's (§1.10); a re-run's is unknown | S3 check 5 |
| A15 | A Claude Code session can push to `pr-report-link-template` | Not tried (§4.7): tag and wiki pushes were refused here the same day, `main` was not | S1 step 4; §4.7's fallback |
| A16 | `main`'s CI is green at S1's base | READ: two of eight runs green on 2026-09-27 (§1.10) | S0 step 5; S1 step 5 reads against it |
| A17 | S2's and S3's patches apply at execution | RUN: they rebuild the rehearsed tree on `5ca0878a` exactly (§1.9) | S0 step 7 |

---

## 10. Execution log

Empty until the plan is green-lit.
