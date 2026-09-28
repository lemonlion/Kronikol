# Azure DevOps at the same level (roadmap 2.4)

**Date:** 2026-09-27, second pass 2026-09-28 · **Repo version:** 3.31.9 (`e4c9e36`, origin/main) ·
**Status: written, nothing implemented, NOT green-lit; needs D27.** Roadmap item **2.4**, the Azure
DevOps half of the bar's "both forges first-class" row (`ROADMAP.md` §0), and a proposed stage-1 row,
**1.14**, for the live defects checking found in the library. It is written beside
[`HISTORY_ACTION_PLAN.md`](HISTORY_ACTION_PLAN.md) (2.3, D26), which designs the history scripts both forges
will run; §4.5 builds on its interface. §9 is the assumption ledger and Appendix B lists every source it
cites. What could be measured without an Azure DevOps organisation is in
[`AZURE_DEVOPS_PARITY_PLAN.harness/`](AZURE_DEVOPS_PARITY_PLAN.harness/README.md) with its output: the
library's console channel under every runner (F1), the re-run fold (F2), the history scripts against a git
server that demands a token, under bash 3.2 and 5.2 (F12, F13, §4.5), and the drafted YAML against
Microsoft's schema (§6). So is the probe pipeline for the half day that needs an organisation (S0, §7.5).

**The second pass (2026-09-28)** tested the history recipe before designing around it, and read the
sources the first pass had left as questions. It found two live defects in the recipe this repository runs
and the wiki prints (F12), git rules that silently drop or double the credential a step passes (F13), and
that GitHub's runner has the Azure DevOps injection problem too (F6). It settled A10 and A20 from source,
narrowed A4, and measured F1 under the SDK's Microsoft.Testing.Platform mode. Its prototype of the history
scripts came out the shape `HISTORY_ACTION_PLAN.md`, written on `main` the same days for 2.3, settled on:
a repository of their own, `ls-remote` first, a lost race folded again rather than rebased. So the scripts
are 2.3's; this plan brings the Azure DevOps side of their interface and the cases its harness adds.

The row asks for "a Tier 0 recipe for Azure Pipelines (with the build-identity Contribute permission it
needs) and a pipeline template as the sibling of 2.3", and warns that "the plan's platform claims are
REFERENCE and need half a day of checking first". The plan it means is `HISTORY_DASHBOARD_STORE_PLAN.md`,
whose §6.1 and §6.3 list what "Azure DevOps first-class" requires. Those sections take one thing for
granted: that everything short of the history recipe already works on Azure DevOps ("Azure DevOps is
already detected", "Capture, report, verdicts, gate and query are CI-agnostic"). This plan checked that
first, and it does not hold.

- **Nothing the library writes for Azure DevOps from a test run reaches the agent** (F1, RUN). The CI
  summary and the artifact upload are `##vso[...]` logging commands printed with `Console.WriteLine`
  inside the test host at run end. On GitHub Actions the same two features write files, which no runner
  can swallow. Measured on this repository's own example projects: under plain `dotnet test` the run-end
  output reached the console from **0 of 8** VSTest projects, and from 3 (the xUnit 2 ones) at
  `--verbosity normal`; under `dotnet run` TUnit let **0 of 3** through. The one CI-preview project with
  `WriteCiSummary` on wrote its summary file every time, and got its `##vso[task.uploadsummary]` line to
  the console **0 times** at four verbosity settings, while GitHub's summary file received all 35,220
  bytes. The wiki says of Azure DevOps "that's all you need — artifacts are uploaded automatically".
- **An Azure DevOps re-run is recorded as the run it re-ran** (F2, RUN). The run id is hard-coded to
  `ado:<build id>:1`, and the code, the XML docs, the report schema, two wiki pages and the changelog say
  Azure DevOps has no attempt number ("Azure DevOps exposes no equivalent variable"). It has:
  `System.JobAttempt`, "set to 1 the first time this job is attempted, and increments every time the job
  is retried". Replayed through `kronikol history record`, a re-run job's passing fragment is thrown away
  as a duplicate, or folded in as a phantom second shard ("22 scenarios from 2 shards").
- **A captured body can drive the agent** (F6, DOC). The agent looks for `##vso[` anywhere in a line, on
  stderr as well as stdout, treats a bare carriage return as a line end, and drops every line it acts on
  from the log. The tool's protection against workflow commands (keep run-derived text from starting a
  line) was built for GitHub's runner and protects nothing here. Nor does it on GitHub: the runner's
  legacy parser acts on `##[set-output …]`, `##[add-mask]` or `##[stop-commands]` anywhere in a line (DOC,
  the runner's source).
- **The history recipe this repository runs, and the wiki prints, can corrupt its ledger or stop
  recording** (F12, RUN). A failed read of the data branch is taken for "no branch yet". The fold starts
  a second ledger and rebases it over the real one, and on a ledger an earlier release started,
  `merge=union` keeps both header lines. The job ends green and `kronikol history verify` fails from then
  on. On an agent that keeps its sources folder (any self-hosted one), every fold after the first fails,
  because the worktree the first one registered outlives its temp folder. The plan's own first draft had
  both defects, and it dropped the credential it was given whenever the checkout had persisted one (F13).
  The design 2.3's plan settled on avoids all three, and a prototype of it here passes every one of these
  cases under bash 3.2 and 5.2.
- **Most of what the GitHub recipe assumes does not hold on Azure Pipelines** (F9, DOC): the checkout keeps
  no credential for later steps, a shallow checkout has no remote branches to put a worktree on, a push by
  the build identity triggers CI where `GITHUB_TOKEN`'s does not, a re-run cannot republish an artifact
  under its old name, and the build identity "can read from the repo but can't push any changes to it"
  until someone grants it Contribute.

**What this changes about the row.** It was "days" of templates with no bump. Checking moved the first
work out of it and ahead of it: the library's own Azure DevOps channels (F1 to F4, F6, F11) are live
defects, so by rule 1 they are a patch in stage 1, proposed as **1.14**. The history recipe's fold step
(F12) is a live defect in a documented recipe, and its fix is already planned as 2.3's: `HISTORY_ACTION_PLAN.md`
S0 rewrites the wiki's recipe now, with no code, and its S4 moves this repository's workflow onto the
action. F12 adds two cases to its facts and a reason to take its S0 first. The row itself becomes four
slices in an order the findings set: the reports first (without them an Azure DevOps user sees nothing at all), then the history recipe
and its templates (the row's two named deliverables), then the pull-request link (the sibling of 2.2),
then a lane on a real organisation that keeps all of it true. Nothing in the row is in a package, so the
row still moves no version; 1.14 is a patch.

---

## 0. Summary

| # | Finding | Basis | Where |
|---|---|---|---|
| F1 | Every Azure DevOps channel the library has is a line on the test host's stdout, which `dotnet test` swallows: 0 of 8 VSTest projects at the default verbosity, 3 of 8 at `normal`, 0 of 3 TUnit projects under `dotnet run`, 0 of 6 under the SDK's Microsoft.Testing.Platform mode by default (3 of 3 TUnit with `--output Detailed`). GitHub's channels are files | RUN | §3.1 |
| F2 | `ado:<build id>:1` whatever the attempt; `ciMetadata.runAttempt` always null on Azure DevOps. A re-run job's fragment becomes a duplicate or a phantom shard | RUN; DOC for the variable | §3.2 |
| F3 | For a GitHub repository built by Azure Pipelines the pull-request target is `main` while runs record under `refs/heads/main`, so every pull request reads against a stream with no runs | DOC | §3.3 |
| F4 | An Azure DevOps artifact carries less than GitHub's: top-level files of six extensions, never `attachments/`; `merge --publish-artifacts` never uploads `runs/`; every job uploads into one container name, through the upload path Microsoft now discourages | READ, DOC | §3.4 |
| F5 | GitHub gets a `::notice` annotation on a failing run, Azure DevOps nothing | READ | §3.5 |
| F6 | The agent acts on `##vso[` anywhere in a line; the tool prints scenario names, error messages and captured bodies to stdout. GitHub's runner acts on `##[<command>]` anywhere in a line, so the tool's GitHub protection leaks too; and the tool's printers are more than §4.4 first listed (`history gate`, `history record`) | DOC (agent and runner source), READ | §3.6 |
| F7 | Nineteen statements, in eight groups, in the wiki, the README, XML docs, code comments and the report schema are false on Azure DevOps | READ, RUN, DOC | §3.7 |
| F8 | The store plan's four Azure Pipelines claims were never checked; its §11 Q2 says the half day was spent, but R19 checked storage only | READ | §3.8 |
| F9 | What the GitHub recipe assumes and Azure Pipelines does not give | DOC | §3.9 |
| F10 | The three-line `kronikol-history` action the store plan promises cannot be one composite action: the recipe acts at three points in two jobs. It limits 2.3 as much as 2.4 | READ | §3.10 |
| F11 | The run's link is built from `SYSTEM_TEAMFOUNDATIONSERVERURI`, which the build-variables reference does not list (the release reference gives it as the Release Management URL), and the project name goes into it unescaped | READ, DOC | §3.11 |
| F12 | The fold step this repository runs and the wiki prints: a failed read of the data branch starts a second ledger, which lands as a ledger `verify` rejects with the job green; on a kept workspace every fold after the first fails; the wiki's copy cannot run as printed. 2.3's S0 and S4 fix it | RUN | §3.12 |
| F13 | A credential a step passes with `git -c http.extraheader=…` is silently dropped when a checkout persisted one for the host, and one passed under the server's own key is sent beside a header the machine's configuration holds: two Authorization headers. Setting the key empty first sends the token alone | RUN | §3.13 |

| Slice | What | Bump | Roadmap |
|---|---|---|---|
| S0 | The half day on a real organisation: `s0-probe.yml`, ledger rows marked S0 | none | 2.4, first; the owner's hand |
| S1 | The library on Azure DevOps: the attempt (F2), the pull-request target (F3), what an artifact carries (F4), the summary posted from the reports directory (F1), `##vso[` and `##[` neutralised in run-derived output on every provider (F6), the run link (F11), the statements of F7 | **patch** | **1.14** (new, rule 1) |
| S2 | Reports on Azure DevOps: the recipe and `kronikol-reports.yml` (publish each reports directory, post its summary, annotate a failure) | none | 2.4 |
| S3 | Tier 0 history on Azure Pipelines: the recipe and the history templates around 2.3's scripts (`HISTORY_ACTION_PLAN.md` §4.2), with the Azure DevOps side of their interface | none | 2.4, after 2.3's S2 |
| S4 | The pull-request link on Azure Repos, the sibling of 2.2 | none | 2.4, pending Q3 |
| S5 | An acceptance lane on a real organisation | none | 2.4, pending Q2 |

---

## 1. How far each claim was checked

- **RUN**: executed here on 2026-09-27, or on 2026-09-28 in the second pass (SDK 10.0.401, git 2.43.0,
  bash 3.2.57 and 5.2.21, Linux), output in the harness.
- **READ**: the source, a plan or the wiki (`../Kronikol.wiki`, cloned at its head today) was read today.
- **DOC**: Microsoft's documentation, read as published markdown, or source: the agent's
  (`microsoft/azure-pipelines-agent`, `master`), the Bash task's (`microsoft/azure-pipelines-tasks`), the
  hosted images' (`actions/runner-images`) and GitHub's runner (`actions/runner`); keyed in Appendix B. Nothing Azure-side was
  run: no organisation is reachable from where this was written. Every DOC row a slice depends on is also
  a step of S0.
- **PLAN**: another plan says so and it was not re-checked. **INFERRED**: reasoned from two facts, stated
  by neither. **ASSUMED**: not checked; each is a row of §9 with what breaks if it is wrong.

The store plan marked every Azure platform claim REFERENCE ("reasoned from working knowledge and not
checked against either vendor's documentation"). This plan restates each as DOC or leaves it ASSUMED;
none is carried over unmarked. Where Microsoft's pages contradict each other, §9 says so and S0 decides.

---

## 2. What exists today

Five places in `src/` branch on the provider (READ): `CiMetadataDetector`, `CiSummaryWriter`,
`CiArtifactPublisher`, `RunSummaryConsoleWriter` (the `::notice`) and `HistoryRunBuilder.RunId`.
Everything else reaches the provider through them. No extension package, adapter, `.props` or `.targets`
file mentions CI, and the repository has no `azure-pipelines*.yml` anywhere.

| Capability | GitHub Actions | Azure DevOps | Basis |
|---|---|---|---|
| Detection | `GITHUB_ACTIONS`, first | `TF_BUILD` (`CiEnvironment.cs:27-31`) | READ |
| Run metadata | eight keys, `runAttempt` from `GITHUB_RUN_ATTEMPT`; branch `GITHUB_REF_NAME` (`main`) | seven keys, `runAttempt` never set; branch `BUILD_SOURCEBRANCH` (`refs/heads/main`); the run link from `SYSTEM_TEAMFOUNDATIONSERVERURI` and the project name (F11) | READ `CiMetadata.cs:34-77` |
| Run identity | `gh:<run>:<attempt>` | `ado:<build>:1` (`HistoryRunBuilder.cs:178`), F2 | READ, RUN |
| Pull-request stream | `GITHUB_BASE_REF` | `SYSTEM_PULLREQUEST_TARGETBRANCH`, kept raw (`CiMetadata.cs:86-96`), F3 | READ |
| CI summary | appended to the file `$GITHUB_STEP_SUMMARY` | a temp copy plus `##vso[task.uploadsummary]` on stdout (`CiSummaryWriter.cs:21-33`), F1 | READ, RUN |
| Debug section of a failing run | file and stdout | stdout only, twice, F1 | READ |
| Annotation | `::notice` on stdout (`RunSummaryConsoleWriter.cs:229`) | none, F5 | READ |
| Report artifacts | `reports-path` written to the file `$GITHUB_OUTPUT`; the workflow uploads the directory | one `##vso[artifact.upload]` per top-level file of six extensions, plus `runs/` (`CiArtifactPublisher.cs:40-74`); `CiArtifactRetentionDays` unused; F1, F4 | READ |
| `kronikol merge --ci-summary --publish-artifacts` | works: the tool is the step's own process | works for the same reason (help text since 3.6.0), without `runs/` (`MergedRunOutputs.cs:153-161`) | READ |
| Tier 0 history recipe | wiki `Cross-Run-History`, "The recommended shape"; dogfooded in `ci-summary-preview.yml` (82 lines of history YAML: 9 read, 7 upload, 66 in the fold job, 44 of them the fold step). A failed read starts a second ledger, a kept workspace fails every fold after the first, and the wiki's copy does not run as printed (F12) | none | READ, RUN |
| Last-green baseline recipe | `gh run download` | `DownloadPipelineArtifact@2`, `runVersion: latestFromBranch` (wiki `Merging-Parallel-Reports`) | READ |
| Pull-request link (2.2) | PR #73, green since 2026-09-15, conflicting | none | READ |
| History action (2.3) | planned in `HISTORY_ACTION_PLAN.md` (D26): four composite actions over bash scripts that keep their own repository; not built | not built | READ |
| Tier 1 dashboard (9.x) | Pages and raw URLs (planned) | neither; 9.4 copies the files beside the page | PLAN |
| CI summary in Mermaid (v4) | renders | no evidence the summary tab renders it (`V4_PLAN.md` Q4) | PLAN |
| Tests | integration tests drive `ReportGenerator`, `kronikol merge`, rotation, the gate and the PR stream with GitHub's variables | helper-level only: nothing drives a run, a merge, rotation, history or the gate with `TF_BUILD` set | READ |

Two constraints the table does not show. **The tool never starts a process** (no `ProcessStartInfo` in
`src/Kronikol.Tool`, READ), and `CROSS_RUN_HISTORY_PLAN.md` §14 makes that a contract: "Kronikol itself
opens no socket — no HTTP client, no token handling, no provider-specific API code in the library or the
tool … Who makes the call is the distinction". Every fetch, push and REST call in this plan is therefore
a pipeline step, as it is on GitHub. And **`templates/Kronikol.Templates.csproj` packs twelve named
folders** (READ), so a `templates/azure-pipelines/` folder ships in no package, as `templates/github-actions/`
would not.

---

## 3. Findings

### 3.1 F1: the library's Azure DevOps channels are lines nobody reads

An agent acts on a logging command when it reads it from the output of the step's own process: the agent
parses the lines of the step's stdout and stderr (DOC [CMD], [NH]). Kronikol prints its three Azure DevOps
channels through `Console.WriteLine`, from the run-end hook that writes the report, inside the test host:
the summary (`CiSummaryWriter.cs:32`), the artifact upload (`CiArtifactPublisher.cs:46,55`) and the
debug section of a failing run (`ReportGenerator.cs:672-681`, which prints it and then posts it through
the same writer). The repository already knew that channel is unreliable. The remarks on
`WriteRunSummaryToConsole` say "every VSTest-hosted runner under `dotnet test` swallows what a library
writes from a run-end hook unless the verbosity is `detailed`, and TUnit's runner suppresses it at any
verbosity", and that is why GitHub's summary goes to a file. Azure DevOps' went to stdout and stayed there.

Measured (RUN, `stdout_matrix.sh`, `stdout_channel.sh`; every example project, with the variables an
Azure DevOps agent sets):

| Framework | `dotnet test` | `dotnet test --verbosity normal` | Other |
|---|---|---|---|
| xUnit 2 (plain, LightBDD, Reqnroll) | 0 of 3 | 3 of 3 | |
| xUnit v3 (plain, LightBDD, Reqnroll, BDDfy) | 0 of 4 | 0 of 4 | the test executable run directly: 4 of 4 |
| NUnit 4 | 0 of 1 | 0 of 1 | |
| TUnit (plain, LightBDD, Reqnroll) | | | `dotnet run --project`: 0 of 3; `dotnet test` in Microsoft.Testing.Platform mode: 0 of 3, and **3 of 3** with `--output Detailed` |
| xUnit v3 in Microsoft.Testing.Platform mode (plain, Reqnroll, BDDfy) | | | `dotnet test`: 0 of 3, with or without `--output Detailed` |

The count is of the run-end pointer ("Kronikol: reports written to"), which goes through the same
`Console.WriteLine` in the same hook as the logging commands, a few lines later. On the one project that
posts a summary (`CiPreview.AllPassing`, xUnit v3), the `##vso[task.uploadsummary]` line itself reached
`dotnet test`'s stdout and stderr 0 times at the default verbosity, at `normal`, at `detailed` and under
`--logger "console;verbosity=detailed"`, while the file it names was written every time (one file in a
per-run `TMPDIR`); the same run with GitHub's variables put 35,220 bytes into `$GITHUB_STEP_SUMMARY`. Not
even `detailed` let it through on that project, where the remark above says it should. Nor is stderr a
channel: the agent reads it too, but the repository's own measurement has xUnit 2 swallowing both streams
(NUnit 4 let stderr through), so it would trade one runner's silence for another's.

The .NET 10 SDK's other `dotnet test`, the Microsoft.Testing.Platform mode a `test` section in
`global.json` selects, is the only way a TUnit project runs under `dotnet test` at all, and an xUnit v3
project can join it (RUN, `stdout_mtp.sh`). By default it shows no test application's output. With
`--output Detailed` it replays each application's standard output once the application ends, indented
under "Standard output", and TUnit's run-end lines are in it: 3 of 3. The agent looks for `##vso[`
anywhere in a line, so there the library's own commands would reach it. xUnit v3's run-end lines are not
replayed either way (0 of 3). A fourth xUnit v3 project, the LightBDD one, did not start in this mode,
because it reads its configuration from the working folder, which this mode moves to the project's
folder. That is not a Kronikol defect. So a TUnit pipeline at `--output Detailed` joins xUnit 2 at
`normal` as a case where the library's own command would land beside the template's post; §4.2 closes
both by running the test step in restricted mode. The remark on
`WriteRunSummaryToConsole` is wrong three ways and joins F7: xUnit 2 lets the lines through below
`detailed`, the xUnit v3 project not even at `detailed`, and TUnit does at `--output Detailed`.

**What an Azure DevOps user gets today**, following the wiki (`CI-Artifact-Upload`: "On Azure DevOps,
that's all you need — artifacts are uploaded automatically"; `CI-Summary-Integration`: "The summary is
automatically uploaded"): no summary, no artifact and no debug section, with no error, unless the tests
run under xUnit 2 at `normal` or above, as an xUnit v3 executable, or as TUnit under `dotnet test
--output Detailed`. `kronikol merge` and `kronikol ingest` do work, because there the tool is the step's
own process.

**The fix is GitHub's shape, not a better stdout.** On GitHub the library writes files and a workflow step
uploads them. Azure DevOps has no file channel for logging commands (DOC [LOG]), so the equivalent is a
pipeline step after the tests that posts what the library wrote: the recipe and template of S2. The
library's part (S1) is small. It posts the summary from `Reports/CiSummary.md` rather than a temp copy,
so the file the pipeline posts is the file the library would have posted; the agent needs that file to
stay on disk until the job ends, because it uploads it later from a queue (DOC [EC], [JSQ]), and a reports
directory does. It writes the debug section of a failing run to `CiSummary.md` when the full summary is
off, so there is always one file to post. And the docs stop promising an upload that `dotnet test`
prevents. The library keeps printing its commands: where they get through (xUnit 2 at `normal`, an
executable, TUnit at `--output Detailed`, the tool) they work. What the agent does with one file posted
twice is §4.2 and S0's A4.

### 3.2 F2: a re-run job is the run it re-ran

`HistoryRunBuilder.RunId` returns `ado:{BUILD_BUILDID}:1`, and `CiMetadataDetector` never sets `RunAttempt`
on Azure DevOps, with the comment "Azure DevOps exposes no equivalent variable, and a null is honest where
a guess would not be. Its own retry handling re-runs the pipeline under a new BUILD_BUILDID"
(`CiMetadata.cs:75-76`). Both halves are wrong. `System.JobAttempt` is "set to 1 the first time this job is
attempted, and increments every time the job is retried", with `System.StageAttempt` and
`System.PhaseAttempt` beside it (DOC [VARS]), and a re-run stays in its run: "The resultant run will have
the same run number … Only those jobs that failed in the initial run and any dependent downstream jobs
will be run again" (DOC [GH]). `CROSS_RUN_HISTORY_PLAN.md` §3.3 had left exactly this open: "ADO's
equivalent — **variable name not verified here**".

Measured (RUN, `f2_rerun_fold.sh`): the CI-preview project run twice under `BUILD_BUILDID=5000`, with
`SYSTEM_JOBATTEMPT=1` and then `2`, writes `ado:5000:1` both times, and the second run keeps the first
under `runs/ado_5000_1` as an earlier attempt of the same run. With the first attempt's fragment set to
fail one scenario, as the attempt that was re-run:

| Fold | Today | With the attempt in the id |
|---|---|---|
| After attempt 1, then after the re-run over both fragments (the fold job is a dependent, so it re-runs) | `duplicate ado:5000:1 … 22 scenarios from 2 shards (already in the ledger)`: the ledger keeps the failure and the re-run's pass never arrives | two runs: `ado:5000:1` (10 passed, 1 failed), then `ado:5000:2` (11 passed) |
| Once, after the re-run | one run of "22 scenarios from 2 shards: 21 passed, 1 failed", the same eleven scenarios twice | the same two runs, from one fold |

On GitHub the same sequence gives two runs, because `gh:<run>:<attempt>` changes with the attempt. The wiki
promises that of both providers: "A re-run of a failed job has a different attempt number and is a
different run, so it never erases the failure it re-ran" (`Cross-Run-History`), in the paragraph that
gives the Azure DevOps id as `ado:<build id>:1`.

**Which attempt.** `System.JobAttempt`. Every job of a build's first attempt shares `ado:<build>:1`, so
shards still fold into one line, and a re-run job is `:2`. It counts per job where GitHub's counts per run,
so two jobs re-run separately both carry `:2` and fold together, as a partial run where GitHub would give
the second `:3`. For the ledger that is no worse (a partial run is read for status only,
`Cross-Run-History`). `System.JobId` is not an identity: it is "a unique identifier for a single attempt of
a single job" (DOC [VARS]), so shards of one attempt would never fold. Attempt 1 keeps the id it has today,
so no ledger written so far changes meaning.

### 3.3 F3: a GitHub repository built by Azure Pipelines reads its pull requests cold

The pull-request stream is kept as the provider gives it because "it has to name the stream the target's
own runs record under" (`CiMetadata.cs:80-85`). For Azure Repos that holds. For a GitHub repository it does
not: `System.PullRequest.TargetBranch` is "`refs/heads/main` when your repository is in Azure Repos and
`main` when your repository is in GitHub" (DOC [VARS]), while runs of `main` record under
`Build.SourceBranch`, `refs/heads/main` on both. So every pull request of a GitHub repository built by
Azure Pipelines reads against a stream nobody records under, and the 3.13.0 default ("a failure that
passed on `main` is the regression it is") silently does nothing. The fix puts `refs/heads/` in front of a
target that is not a ref, on Azure DevOps only. It cannot go the other way (record runs under the short
name) without renaming every Azure DevOps stream already in a ledger.

### 3.4 F4: an Azure DevOps artifact carries less than GitHub's

On GitHub the library hands the workflow the reports directory and the workflow uploads all of it. On Azure
DevOps the library uploads file by file: `Directory.GetFiles` over the top level, six extensions
(`ReportGenerator.cs:649-662`), plus the kept runs. So `attachments/` (the screenshots a report shows) is
never uploaded, and a downloaded report shows broken images. `kronikol merge --publish-artifacts` passes
no kept runs (`MergedRunOutputs.cs:153-161`). Every job uploads into the one container named by
`CiArtifactName`, so shards write the same relative paths into one artifact (what the agent keeps is
S0's A7). And `##vso[artifact.upload]` is the file-container path Microsoft now steers away from: "we
recommend using Download Pipeline Artifacts and Publish Pipeline Artifacts for faster performance" (DOC
[PBA]). Where F1 holds none of this matters; S1 fixes it for where the commands do get through.

### 3.5 F5: no annotation on Azure DevOps

A failing run on GitHub ends with `::notice title=Kronikol::{n} failed of {m} scenarios — kronikol query
failures …`. Azure DevOps' nearest is `##vso[task.logissue type=warning]`, which logs the message "in the
timeline record of the current task", keeps the first ten per record and does not change the result
(DOC [LOG], [EC]); there is no notice level. The library emits nothing. It is the smallest gap, and from
the test host it would share F1's fate; the template of S2 emits it instead.

### 3.6 F6: a captured body can drive the agent, and GitHub's runner too

GitHub's runner reads a workflow command only at the start of a line, after whitespace. The tool relies
on that: `QueryWriter.Line` flattens every run-derived string onto its line so that a scenario named
`"a\n::error::x"` cannot start one (`QueryWriter.cs:140-160`, `RunSummaryConsoleWriter.cs:274`). The Azure
Pipelines agent finds the prefix anywhere: `int prefixIndex = message.IndexOf(LoggingCommandPrefix,
StringComparison.Ordinal)` with the prefix `##vso[` (DOC [CMD]). It reads stderr as well as stdout, splits
lines on a bare carriage return, and a line it processes is not written to the log at all (DOC [NH],
[PI]). So on Azure DevOps flattening protects nothing. A scenario name, an assertion message or a captured
response body that contains `##vso[task.setvariable variable=…]`, `##vso[task.prependpath]`,
`##vso[task.uploadfile]` or `##vso[artifact.upload …]` is acted on, silently, when `kronikol query`,
`merge` or `ingest` prints it in a pipeline step, and the payload verbs (`body`, `http`, `note`) print
captured bodies whole by design. Those bodies are the system under test's and its dependencies'
responses: third-party text.

The agent's own answer to untrusted text is to rewrite the prefix: `Regex.Replace(input, "##vso", "**vso",
RegexOptions.IgnoreCase)` (DOC, `StringUtil.cs`). S1 does the same, and does `##[` too: on Azure DevOps
those are the log viewer's formatting commands (the agent's own logger looks for `##[group]` anywhere in a
line, `message.Contains`, DOC), and on GitHub they are the runner's legacy commands, matched anywhere
(below). A
step-level `target: { commands: restricted, settableVariables: none }` is the platform's second line: it
blocks artifacts, attachments and variables, but still lets `task.complete` through, so an injected
`##vso[task.complete result=Succeeded;done=true]` can still end a step early (DOC [TGT], [SECT], [TCE]).
The templates use it on steps that run the tool; it does not replace the rewrite.

**GitHub's runner has the same hole** (DOC, [GHR]). `ActionCommandManager.TryProcessCommand` tries the
`::` form first, which must start the line after leading whitespace, and then falls back to the legacy
form: `ActionCommand.TryParse` looks for `##[` anywhere, `int prefixIndex = message.IndexOf(Prefix)` with
`Prefix = "##["`. Every registered command is honoured that way. `set-output` still sets the step's output
(the deprecation adds a warning only when a server flag asks for one), and `add-mask`, `add-matcher`,
`save-state`, `debug`, `warning`, `error`, `notice` and `group` all work. `stop-commands` switches command
processing off for the rest of the step. Only `set-env` and `add-path` are refused, unless
`ACTIONS_ALLOW_UNSECURE_COMMANDS` is set. So on GitHub a body holding `##[add-mask]Failed` masks every
later "Failed" in the log. `##[stop-commands]x` silences Kronikol's own `::notice`, and
`##[set-output name=…]` writes a step output. The column-zero rule covers only the `::` form, so §4.4's
rewrite of `##[` is a GitHub fix as well. That is why it applies on every provider.

**The printers are more than §4.4 first listed** (READ). `kronikol history gate`, which is meant to run as
a pipeline step, prints each new failure as `{address}  {feature} › {name}  {verdict} — {evidence}` with
the feature and scenario names unflattened (`HistoryCommand.Maintenance.cs:129`). `history record`
prints rename suggestions with scenario names (`HistoryCommand.cs:322`, `:331`), and `history import`
prints labels read from CTRF and Allure files. Every verb reaches the console through the two writers
`Program.cs` hands to `Commands.Dispatch`, which is where §4.4 now puts the guard. One boundary no guard
of Kronikol's reaches: `dotnet test` prints a failing test's assertion message itself, so a body quoted in
an assertion reaches the agent through the consumer's own test step. The recipe's answer is restricted
mode on that step (§4.2), which costs nothing once S2 posts the reports.

### 3.7 F7: what is written down that is false on Azure DevOps

| Where | Says | Why false |
|---|---|---|
| `CiMetadata.cs:75-76`; `HistoryRunBuilder.cs:163`; `HistoryModel.cs:168`; `ReportConfigurationOptions.cs:568`; the report schema's `runAttempt` description (`ReportGenerator.cs:5990`, "Null off GitHub Actions"); wiki `Querying-Reports` (361) and `Report-Configuration` (192); the changelog entry that introduced `ciMetadata.runAttempt` | Azure DevOps has no attempt number, or the id is `ado:<build>:1` | F2 |
| Wiki `Cross-Run-History` (86) | "A re-run of a failed job has a different attempt number and is a different run" | not on Azure DevOps (F2) |
| Wiki `CI-Artifact-Upload` | "On Azure DevOps, that's all you need — artifacts are uploaded automatically" | F1 |
| Wiki `CI-Summary-Integration` and `Generated-Reports` (724, 800); README (136-142); `nuget-readme.md` (62) | the summary and the artifacts reach Azure DevOps automatically | F1 |
| Wiki `CI-Summary-Integration`, the Azure DevOps example | `PublishBuildArtifacts@1` with `pathToPublish: '**/Reports'` | "Wildcards are not supported" (DOC [PBA]) |
| `ReportConfigurationOptions.cs:348` (`PublishCiArtifacts`) | "(GitHub Actions)" | names one provider of the two the code serves |
| `ReportConfigurationOptions.cs:265-270`, the remark on `WriteRunSummaryToConsole` | VSTest runners swallow run-end output "unless the verbosity is `detailed`", and "TUnit's runner suppresses it at any verbosity" | xUnit 2 lets it through at `normal`, the xUnit v3 CI-preview project not even at `detailed`, and TUnit does at `--output Detailed` in the Microsoft.Testing.Platform mode (F1, RUN) |
| Wiki `CI-Summary-Integration`, Limitations | "Azure DevOps renders Markdown summaries with slightly different styling … The content is the same" | "Markdown rendering on the Extensions tab is different from Azure DevOps wiki rendering" (DOC [LOG]), and 2018 reports say collapsing blocks do not collapse there; the summary is built of `<details>` blocks. S0's A4 |

Once F2 is fixed as §4.3 describes, two more lines become true that are false today: the `run:` line of
`kronikol query summary` ("attempt 2", which it never prints on Azure DevOps) and CTRF's `runAttempt`.

### 3.8 F8: the store plan's half day was never spent on Pipelines

Store plan §11 Q2 says "the half-day of vendor verification §6.3 names as its first task was done on
2026-09-21 (§13 R19)". R19 checked Blob static websites, Static Web Apps, App Service Auth, the Entra token
route and the S3 API. It did not check the four claims that are this row's: no Pages, no anonymous raw
access, the build-identity permission, template not action (READ). This plan checks the last two (§3.9)
and leaves the first two to 9.4, which is where they matter, with one fact for it: public projects "are
retired, and new public projects can no longer be created. In 2027, all existing public projects will
automatically convert to private" (DOC [PPR]), so "no anonymous raw access" becomes true of every project.
A second 2026 change matters to S5: a private project's free grant is one Microsoft-hosted job and 1,800
minutes a month, and only once the organisation is linked to an Azure subscription (DOC [CJ]).

### 3.9 F9: what the GitHub recipe assumes

| The GitHub recipe relies on | On Azure Pipelines | Consequence for the recipe | Basis |
|---|---|---|---|
| `actions/checkout` leaves its token in `.git/config`, so a later `git fetch origin kronikol-history` works | `persistCredentials` is off by default: "The default is not to leave it" | each script passes the token to every git command that talks to the remote, in a repository of its own, never persisted (and not in the checkout, where a persisted credential would win, F13) | DOC [CO], [ARG] |
| the fetch in the fold step "also updated origin/kronikol-history, which can" be named from a worktree | a shallow checkout (depth 1 in pipelines created since September 2022) fetches the build commit alone, so no remote branch exists locally | every fetch of the data branch names an explicit refspec, one commit deep | DOC [ARG], [GSP] |
| `GITHUB_TOKEN` with `contents: write` in the fold job only | `System.AccessToken` is the build identity in every job, with whatever the project grants it | grant Contribute on the data branch alone, not on the repository | DOC [AT], [PERM] |
| a push made with `GITHUB_TOKEN` starts no workflow | "How can I avoid triggering a CI build when the script pushes?" "add `[skip ci]`" | `[skip ci]` in every ledger commit (honoured by both forges), and the data branch excluded from the trigger | DOC [GIT], [ARG], [GHSKIP] |
| the workflow token may push a new branch | "By default, this identity can read from the repo but can't push any changes to it" | the recipe names the identity and two permissions, or the branch is made once by hand | DOC [GIT] |
| "Re-run failed jobs" re-runs the jobs that `needs` them | "Only those jobs that failed … and any dependent downstream jobs will be run again" | holds: the fold stays a dependent, and a rerun stage reruns the stages that depend on it (S0's A10 confirms both shapes for a dependent that succeeded) | DOC [GH], [RNSS] |
| an artifact name reused by a re-run is replaced | "Pipeline artifacts cannot be deleted or overwritten. To regenerate artifacts when rerunning a failed job, include the job ID in the artifact name" | every artifact the templates publish ends in `$(System.JobId)` | DOC [ART] |
| a composite action carries its own script files | "Only the template files are used … you can't use scripts from the template repo" | the templates write each script out from a heredoc, so the scripts shared with 2.3 are held byte-identical by a test | DOC [TPL] |
| in a `run:` script only `${{ }}` is the runner's; `$(…)` is the shell's | an inline script is a task input: `$(name)` is replaced before the shell runs when a variable of that name exists, and `${{ }}` at compile time, empty when unknown | the shared scripts contain no `$(identifier)` and no `${{` (RUN, S10) | DOC [PVARS] |
| one runner, `ubuntu-latest`, and its bash | the agent's OS is the consumer's choice; macOS images ship bash 3.2.57; on Windows the docs disagree whether a `bash` step runs WSL's or Git's, and the task's and the image's source say Git's | the scripts pass their cases under bash 3.2.57 (RUN); the Windows lane is Git Bash (A20) | DOC [RI], [BASH3], [XPLAT], [BASHSRC], [IMG] |

### 3.10 F10: three lines is not one action

The store plan's onboarding fix reduces Tier 0 to `uses: lemonlion/kronikol/actions/history@v3` with a
`suite`. The recipe acts before the tests (read the ledger), after them (publish the fragment) and in a job
of its own after every shard (fold and push). A composite action is a step in one job and has no
post-step. So 2.3 is two or three steps on GitHub too, and the siblings are symmetrical: the same three
points, the same script at the one that matters. Azure Pipelines can go further than GitHub here, with a
jobs template that wraps the consumer's test steps (`stepList` parameter), but that takes over the
consumer's job definition; §4.7 keeps step templates as the unit and offers the wrapper as an extra.

### 3.11 F11: the run's link

The pipeline link is `SYSTEM_TEAMFOUNDATIONSERVERURI` + `SYSTEM_TEAMPROJECT` + `/_build/results?buildId=`
(`CiMetadata.cs:57-65`). The build-variables reference lists `System.CollectionUri` ("The URI of the Azure
DevOps organization or collection. For example: `https://dev.azure.com/fabrikamfiber/`") and not the
variable the code reads, which the classic release reference gives as the Release Management URL,
`https://fabrikam.vsrm.visualstudio.com/` (DOC [VARS], [RELV]). In a YAML build on Services the two are
probably equal (INFERRED; S0 prints both). The project name is written into the URL as it is, spaces and
all. S1 reads `SYSTEM_COLLECTIONURI` first and escapes the project name. The link travels into the report's
CI table, the history run line (`url`, which the dashboard plan will render as a link), CTRF's `buildUrl`
and the merged report.

### 3.12 F12: the fold step this repository runs, and the wiki prints

This plan's first draft of the record script was modelled on this repository's fold step
(`ci-summary-preview.yml` at `e4c9e36`, lines 98-141). The second pass ran both, and a revision, before
designing around any of them (RUN, `record_script.sh`). The remote is a local smart-HTTP git server that
demands a token, and every case runs under bash 3.2.57 and 5.2.21. All three pass what §6 listed: a first
run, an append, a re-run that records nothing, a lost race, a refusal that never clears, and four
pipeline runs at once. Two cases §6 did not list break the fold step and the draft:

- **A failed read is taken for "no branch yet"** (S6, S6b). `if git fetch origin kronikol-history; then …
  else` cannot tell a missing branch from a network error, a 5xx or an expired token, so every failure
  takes the orphan path. The job records into an empty ledger, and its push is refused because the branch
  exists. The retry then rebases the orphan commit over the real branch, where `merge=union` resolves the
  add/add conflict by keeping both files' lines. When the two ledgers begin with the same header, git's
  merge folds the common head away and the result reads sound (S6). When an earlier release wrote the real
  ledger's header, as on every ledger that has outlived an upgrade, both header lines stay (S6b). The job
  exits 0, and from that run on the branch fails `kronikol history verify` ("line 5: a header that is not
  the first line") and `history doctor` reports it. The readers tolerate it, so nothing downstream fails
  loudly. The run was also recorded against an empty ledger, so its partial flag and its rename
  suggestions were worked out against nothing (READ, `HistoryCommand.cs:304-345`).
- **A kept workspace fails every fold after the first** (S7). The fold adds a worktree of the job's checkout
  under the job's temp folder. The agent (and a GitHub self-hosted runner) empties that folder between
  jobs but keeps the sources folder, and with it the worktree's registration. The next job's `git worktree
  add -B kronikol-history` stops at "fatal: 'kronikol-history' is already used by worktree at
  '…/history-branch'". On an Azure DevOps self-hosted agent, keeping the sources folder is the default.

The wiki's copy (`Cross-Run-History`, "The recommended shape", wiki `86a77c1`) says it is the recipe this
repository runs, "so it is executed, not merely written". The printed snippet is not that recipe (W1, W2).
It has no fetch before `git worktree add … origin/kronikol-history`, a ref a default depth-1 checkout does
not have, so it stops at its first line. With the fetch put back, the commit stops for want of an
identity, and its one retry is a single rebase. A consumer who copies the page gets no history until they
rebuild what it leaves out.

**Where it is fixed.** `HISTORY_ACTION_PLAN.md`, written for 2.3 on the same days, found the wiki's recipe
broken on its own (its F1: no fetch, no identity) and this repository's loop losing runs when six folds
race, 9 of 60 (its F4), and it plans the fix. Its S0 rewrites the wiki's recipe now, around a repository of
its own and never a worktree, and its S4 moves this repository's workflow onto the action. S6b and S7 are
not among its findings. Its design avoids both: a record against an unreachable origin is "an error and
never a new branch", and there is no worktree. So they are two more facts for its record set, and two more
reasons its S0 is rule 1 work. This plan's own prototype of the same design (`kronikol-history-record.sh`,
and `kronikol-history-read.sh` for the read before the tests) passes every case above on both shells, and
R1 to R6. Four folds at once lost nothing here (S8), in one trial; 2.3's six-at-a-time races, ten times
over, are the stronger evidence on races.

### 3.13 F13: a credential a step passes loses to one the checkout persisted

Both forges' checkouts can leave a credential in `.git/config` under a URL-specific key. `actions/checkout`
does it by default (`http.https://github.com/.extraheader`). The Azure Pipelines agent does it with
`persistCredentials: true`, under `http.<repository URL>.extraheader`, and removes it when the job ends (DOC
[GSP]). Git reads `http.extraheader` through its URL-matching configuration, where the more specific key
wins, and a `-c http.extraheader=…` on the command line names no URL. So when a URL-specific value exists,
the one a step passes is never sent (RUN, G2 and G3b: only the persisted token reached the server). An empty
value does not clear the persisted one either (G3); only the same URL-specific key on the command line
adds to it, and then both are sent (G4).

A step that passes a token per command therefore uses whatever the checkout persisted. The first draft did
exactly that, and §4.6's parameter for GitHub repositories assumed it could choose the credential. That is
harmless when the two are the same token. When they are not, the step pushes as the wrong identity or
fails: with a stale token persisted, the draft took F12's orphan path and ended in "could not read
Username" (S11). The same draft could not be shared with 2.3 as written either. On GitHub, where the
wrapper passes no header, it stopped under `set -u` when the variable was unset (S9). A repository of the
scripts' own, as 2.3's design has, is out of reach of anything a checkout persisted (S11, R5).

**The machine's configuration still reaches it** (RUN, G11). 2.3's scripts pass the token as
`http.<server>/.extraheader`, through the environment (`GIT_CONFIG_COUNT`, its §4.9). A header the
machine's global configuration holds for that server, as a self-hosted runner's can, is then sent beside
it: two Authorization headers, which a server may refuse or read in either order. One given for no URL
would not be sent at all (G2). Setting the same key empty first and then to the token sends the token
alone, through `-c` and through `GIT_CONFIG_COUNT` alike (G11). This plan's prototypes do that (S12, R6).
The price is that any other header the machine adds for that server is dropped for these calls; a
credential is the realistic case. §4.5 asks 2.3's `git.sh` for the same two settings.

---

## 4. The design

### 4.1 What "the same level" means

Every capability a GitHub user gets from the library, the tool, the wiki and the templates, an Azure DevOps
user gets with no more steps, or the page says plainly what differs and why. It does not mean a feature
Azure DevOps has and GitHub lacks (its own flaky-test management, test plans, the Tests tab), and it does
not mean the Tier 1 and Tier 2 rungs, which have their own rows (9.4, 9.5). §5 counts the steps.

Supported by the end of this plan: Azure DevOps Services; Azure Repos and GitHub repositories;
Microsoft-hosted Linux, Windows and macOS agents, and self-hosted agents with `bash` and `git` on the path.
Azure DevOps Server is "not obstructed" and not claimed (Q5): pipeline artifacts, which the templates use,
are "supported on Azure DevOps Services only" (DOC [PPA]).

### 4.2 Reports: the pipeline posts what the library wrote (S1, S2)

The library already writes what the pipeline needs into the reports directory: `CiSummary.md` when
`WriteCiSummary` is on, `Run.json` naming the run and listing its files, and the report itself. S1 makes
two changes so that holds in every case: the summary is posted from `Reports/CiSummary.md` (today a temp
copy), and a failing run with `WriteCiSummary` off writes its debug section to `CiSummary.md`, the text it
is posting anyway. Then S2's step, after the tests:

1. finds every reports directory this build wrote under a search root: a `Run.json` whose `run` begins
   `ado:<Build.BuildId>:`, so a self-hosted agent's stale directories from other builds are skipped;
2. copies each into one staging folder under its path relative to the root, `runs/` included;
3. posts each `CiSummary.md` as a summary section named for its suite (`Run.json`'s `suite`), and prints
   `##vso[task.logissue type=warning]` with the pointer's first line for each `Run.json` with `failed > 0`;
4. publishes the staging folder as one pipeline artifact whose name ends in `$(System.JobId)` (F9).

Its steps run with `condition: succeededOrFailed()`. The consumer's test step fails the job when a test
fails, and a failing run is the one whose reports, summary and fragment matter most.

**The section's name.** The agent names a summary attachment after its file (`Path.GetFileName`, DOC
[TCE]), so with `uploadsummary` every lane's section would be called `CiSummary.md`. The long form the
logging-commands page gives for it, `##vso[task.addattachment type=Distributedtask.Core.Summary;name=…]`
(DOC [LOG]), takes the name, so the template posts `name=Kronikol <suite>`, keeping letters, digits, dots
and dashes (the agent refuses a name holding a character its OS forbids in file names, DOC [TCE]). That
the Extensions tab titles a section with that name is INFERRED; S0's A4 shows it.

**One summary posted twice.** The agent queues every attachment against the timeline record of the step
that printed it (`QueueFileUpload(_mainTimelineId, _record.Id, type, name, …)`, DOC [EC]). So where the
library's own command gets through, its post and the template's are two attachments on two records, and
two sections (INFERRED; S0's A4 shows the tab). The recipe removes the case rather than documenting it:
the consumer's test step runs with `target: { commands: restricted, settableVariables: none }`. Restricted
mode blocks attachments and artifact uploads (DOC [TCE]), so the library's commands never land from the
test step, and the template is the only poster. It also keeps a response body quoted in an assertion
message, which `dotnet test` prints itself, from setting variables or uploading files (§3.6's boundary).
`task.complete` stays allowed in restricted mode, so that one command remains out of reach. Where the
library's command would have got through, the agent now logs that it was not allowed (a warning,
`CommandNotAllowed`, DOC [TRC]); whether a warning turns the step orange is A9's question. Nothing in the
library changes for this. Turning the library's command off would take the summary from `kronikol merge`
and `ingest`, where it is the only channel, and the xUnit v3 executables where it works today (Q6).

### 4.3 Run identity and the run link (S1)

`CiMetadataDetector.DetectAzureDevOps` reads `SYSTEM_JOBATTEMPT` into `RunAttempt`, and `HistoryRunBuilder`
uses it exactly as the GitHub branch uses its attempt (a missing or unparseable attempt is 1). `runAttempt`
then travels every path it already travels on GitHub: the report's `ciMetadata` in JSON, XML and YAML, the
schema, CTRF's `extra`, the `run:` line of `kronikol query summary`, `Run.json`, the `runs/` directory
names (`ado_5000_2`). No format gains a field: `runAttempt` has been in the schema since it was added, and
was null. The pull-request target (F3) gets `refs/heads/` in front when it is not a ref, on Azure DevOps
only. The run link (F11) reads `SYSTEM_COLLECTIONURI`, then `SYSTEM_TEAMFOUNDATIONSERVERURI`, and escapes
the project name.

### 4.4 Logging-command safety (S1)

One rewrite: `##vso` to `**vso`, ignoring case, as the agent's own helper does, and `##[` to `**[` the
same way. **In the tool it is a guard, not a call-site edit.** Every verb reaches the console through the
two writers `Program.cs` hands to `Commands.Dispatch`, so wrapping those two once covers every verb there
is and every one yet to come, the history ones §3.6 found included. The logging commands the tool means to
print, `kronikol merge`'s and `ingest`'s summary and artifact lines, reach the unguarded writer through the
`writeLine` delegate that `CiSummaryWriter.Write` and `CiArtifactPublisher.Publish` already take
(`MergedRunOutputs.cs:139` and `:159` hand them the writer everything else uses today). **In the library**, which does not own the
test host's console, `RunSummaryConsoleWriter` and the debug section call the same function where they
build their lines.

**JSON the tool writes escapes `#` as `\u0023`** (the `--json` forms and the JSON bodies `query body`
pretty-prints; the tool's `JsonSerializerOptions` use the default encoder today, which leaves `#` alone,
READ). A JSON reader gets exactly the same value, and the agent sees no prefix, so the guard never has to
alter a byte of it. Everything else is rewritten visibly, so a reader sees the text was altered rather than
wondering at an invisible character.

It applies on every provider, because GitHub's runner reads `##[` anywhere in a line (§3.6), and a report
written on one forge is read by `kronikol query` on the other. `OneLine` already folds a bare carriage
return, which the agent also treats as a line end. Files (`Failures.md`, the digest, the report, anything
`--out` writes) are not touched: no runner reads a file as a log. So a captured text body printed by
`kronikol query body` can differ from the report in exactly these two prefixes, and `--out` gives it back
byte for byte. GitHub's column-zero rule stands as it is.

### 4.5 Tier 0 history on Azure Pipelines (S3): 2.3's scripts, wrapped

The ledger lives where it lives on GitHub: `history.jsonl` on an orphan branch, `kronikol-history`, with
`history.jsonl merge=union text eol=lf` in its `.gitattributes` (kept for anyone who merges by hand; the
scripts no longer rely on it). **The scripts are 2.3's.** `HISTORY_ACTION_PLAN.md` designs them (§4.3 read,
§4.5 record, §4.9 portability and git's settings), and its §9 hands 2.4 its §4.2 interface, with the
scripts carried byte for byte into the Azure templates and held there by a drift fact. Each keeps a
repository of its own under the job's temp folder, so nothing the checkout persisted reaches it (F13), no
worktree outlives a job (F12), a multi-repository checkout whose working folder is no repository changes
nothing, and the fold job needs no checkout at all (`checkout: none`). Each asks `git ls-remote --exit-code`
first: 2 is "no such branch", anything else is a failure (RUN, G6 to G8, and 2.3's own `s6`), so a remote
that cannot be read is never taken for an empty one. Record folds again on the new tip when a push loses a
race, and stops at once when the tip has not moved. This plan's harness prototypes the same design
independently and runs it with an Azure DevOps credential (`kronikol-history-read.sh`,
`kronikol-history-record.sh`, §3.12).

**What the Azure templates supply.** The scripts must take these from their caller, not from GitHub's
variables. That is what this plan asks of 2.3's §4.2, and the two settings at the bottom are asked of its
`git.sh`:

| | GitHub (2.3) | Azure Pipelines (S3) |
|---|---|---|
| The remote | `$GITHUB_SERVER_URL/<repository>.git` | `Build.Repository.Uri`, which can carry a user name (DOC [VARS]; RUN, G7b) |
| The credential | `x-access-token` and a token, as a basic header | `System.AccessToken` as a bearer header, the agent's own form (DOC [GSP]); for a GitHub repository, §4.6 |
| The temp folder | `RUNNER_TEMP` | `Agent.TempDirectory` |
| Where `KRONIKOL_HISTORY` goes | `GITHUB_ENV` | `##vso[task.setvariable]` (DOC [LOG]) |
| The fragments | `actions/download-artifact` | `DownloadPipelineArtifact@2`, a sub-directory per artifact (DOC [DPA]) |
| A marker in the commit message | not needed: a `GITHUB_TOKEN` push starts no workflow | **`[skip ci]`**: the build identity's push does start CI (DOC [GIT]). It is a skip marker on GitHub too ([GHSKIP]), so the scripts can always write it |
| The header's key | the server's key, **set empty, then to the token** (G11) | the same: otherwise a header a machine holds for the server goes out beside the token |

- **Before the tests, in every test job: read.** 2.3's read, then `##vso[task.setvariable
  variable=KRONIKOL_HISTORY]` when a ledger was found. It never fails the job: no branch yet means no
  history, and a remote that cannot be read is a warning (`##vso[task.logissue type=warning]`) and the
  tests run without history (R1 to R6). The token is mapped into this step's environment only, so the test
  step never holds it.
- **After the tests, in every test job: publish.** Nothing new: `History.run.json` sits in the reports
  directory, and S2's artifact carries it with `runs/`. `history record` reads that layout correctly. A
  fragment kept under `runs/` for an earlier attempt of the same run is folded in as an attempt, and one
  of another run is left alone (READ, `HistoryCommand.cs`, `Attempts`). A run's artifacts live as long as
  the run (DOC [RET]), so the fragment needs no artifact of its own. On GitHub it gets one from 2.3's
  `save`, because the reports artifact there keeps one day.
- **Once, after every test job: fold and push.** `DownloadPipelineArtifact@2` takes `**/History.run.json`
  from every artifact of this run (with no artifact name it makes a sub-directory per artifact, and its
  exclude patterns do not exclude, DOC [DPA]; `history record` searches the folder recursively, READ).
  Then 2.3's record, whose folder input (`path`) takes the downloaded folder. A run already on the branch
  is a duplicate, so the job is safe to re-run, and after a job re-run it records the new attempt (§3.2's
  third column). It skips pull-request builds (`Build.Reason`), as 2.3's record does unless told to record
  them.

**When the fold runs again.** Rerunning a stage "triggers all stages that depend on it to rerun as well",
so a history stage that depends on the test stage folds again after a rerun of the test stage (DOC
[RNSS], 2024). The same page announces a way to rerun a single stage *without* the stages after it. Used
on the test stage, that would leave the re-run attempt unrecorded: the ledger keeps the failure it re-ran
and moves on with the next build. The stage-retry REST call documents `forceRetryAllJobs` and `state` only
[STG], and the extension API's `UpdateStageParameters` adds an undescribed `retryDependencies` [EXTAPI],
probably that switch. S0's A10 runs both shapes, a dependent job and a dependent stage. If the fold is
not re-run, the record step moves to the end of each test job, where a job's re-run re-runs it. That is
correct when each job runs its own suite (one test project per job, the documented setup), because a
ledger holds a line per run and suite; a suite split across jobs keeps the fold job. The script is the
same either way; only its place differs.

**Permissions, in the order the recipe states them.** Which identity: `{Project Name} Build Service
({Org Name})` when "Limit job authorization scope to current project for non-release pipelines" is on,
else `Project Collection Build Service ({OrgName})` (DOC [AT]). The setting has been on for new projects
and organisations since December 2019 (DOC [RN160]), while the access-token page still calls the
collection identity the default: the recipe names both and says how to tell which applies. What to grant:
create `kronikol-history` once by hand (an orphan commit holding the `.gitattributes` line), then give the
identity **Contribute on that branch only**, so a token that leaks from any job can write the ledger and
nothing else. Or grant Contribute and Create branch on the repository and let the first run create it. A
branch policy that covers `refs/heads/*` also needs "Bypass policies when pushing" on that branch (DOC
[PERM]).

**Triggers.** The trigger excludes `kronikol-history`, and every ledger commit carries `[skip ci]`, which
both forges honour (DOC [ARG]; [GHSKIP] for pushes on GitHub). That is why this plan asks 2.3's record to
write it, rather than Azure DevOps' own `***NO_CI***`. So neither this pipeline nor any other on the repository
starts a run for a ledger commit. A branch without the pipeline's YAML probably triggers nothing on its own
("the YAML file that is in the branch you are pushing is evaluated", DOC [ARG]); the marker is kept
regardless, for pipelines whose YAML lives elsewhere.

### 4.6 GitHub repositories built by Azure Pipelines

The same recipe, with two differences. The read and the push need a credential GitHub accepts, not
`System.AccessToken`. The templates take the authorisation header as a parameter, defaulting to the build
identity's for Azure Repos, and because the scripts work in a repository of their own and set the key
empty first, the header passed is the one sent, whatever the checkout persisted or the machine holds (F13). Under the first draft's per-command header, a
persisted app token would silently have won. For a GitHub repository the parameter is a basic header for a
token GitHub accepts (`x-access-token`, as `actions/checkout` sends it), from a secret variable. The Azure
Pipelines app is granted "Write access to code" (DOC [GH]). Whether its own token can push for a given
run, and how long it lives, is unverified; S0's step 8 decides whether the GitHub page offers it. And the
pull-request target needs F3's fix. S4 does not apply there: that repository's pull requests are on
GitHub, where 2.2's action runs only under GitHub Actions. Azure Pipelines' `GitHubComment@0` task can add
a comment through an OAuth or PAT connection (DOC [GHC]); keeping one comment current is what 2.2's
action does, so the GitHub page points there.

### 4.7 The templates

```
templates/azure-pipelines/
  README.md
  kronikol-reports.yml          # steps, after the tests (S2)
  kronikol-history-read.yml     # steps, before the tests (S3)
  kronikol-history-record.yml   # steps, in the fold job (or at the end of each test job, A10) (S3)
  kronikol-pr-link.yml          # steps, on pull-request builds of Azure Repos (S4)
```

The scripts are not here: they are 2.3's, under `templates/github-actions/kronikol-history/`
(`HISTORY_ACTION_PLAN.md` §4.1, §4.11), and each history template carries a copy.

- **Step templates are the unit**, the sibling of a composite action. Each takes typed parameters and
  passes every one to its script through `env:`, never into the script text: an Azure DevOps parameter in a
  script body is the same injection PR #73's action avoids for `${{ inputs }}`.
- **No template expression beyond `${{ parameters.x }}`** in the step templates: no `${{ if }}`, no
  `${{ each }}`. Conditions are runtime `condition:` expressions. That keeps the tests' resolver as small as
  PR #73's, and each template reads as the YAML it expands to.
- **The history scripts are 2.3's, copied.** A composite action carries its files; an Azure DevOps
  template cannot (F9). So each history template writes 2.3's scripts out from quoted heredocs (`cat >
  "$KRONIKOL_TEMP/…" <<'KRONIKOL'`, which the shell leaves literal) and runs them with `bash`, and a drift
  fact holds each heredoc body byte-identical to its file, as `SkillDriftTests` holds the two `query.py`
  copies. Two rules the Azure side adds to 2.3's §4.9: no `$(identifier)` and no `${{` anywhere in them
  (F9, S10), since the whole inline script is a task input the agent expands; and a Windows agent runs
  them under Git Bash, as GitHub's runner does. Bash@3 takes the first `bash` on the path (`tl.which('bash',
  true)`, DOC [BASHSRC]), and the hosted Windows 2025 image puts `C:\Program Files\Git\bin` at the front of
  the machine path, with WSL's `bash.exe` in System32 behind it and WSL installed with no distribution
  (DOC [IMG]; A20). Bash 3.2 on macOS is already 2.3's rule, and this plan's prototypes pass under it too
  (RUN).
- **Task inputs by their canonical names.** Microsoft's schema knows only those, so `targetPath`, not the
  `path` alias the service also accepts (§6).
- **Distribution**: copy the folder into the repository (the default, as PR #73's README does for its
  action), or reference it through `resources.repositories` pinned with `ref: refs/tags/<release>`, which
  needs a GitHub service connection even for this public repository ("GitHub repos require a GitHub service
  connection for authorization", DOC [RR]). A Marketplace extension with real tasks is the Azure DevOps side
  of 13.2's listing, not this row (§8.1).
- **A wrapper for the common case**, optional: `kronikol-tests-with-history.yml`, a jobs template taking
  the consumer's test steps as a `stepList` and emitting the test job with the read, reports and record
  steps around them, and the fold job. It is the Azure DevOps answer to F10's three lines, offered and never
  required, because it owns the job definition.

### 4.8 The pull-request link on Azure Repos (S4)

2.2 keeps one comment with a line per artifact, and most of its script is the machinery that keeps that
true: markers, ordering by label, a guard so an older run never replaces a newer line, tolerance of hand
edits. Azure Repos has that machinery built in. A pull-request status is keyed by its context, "only the
latest of which is shown for each unique context", its description renders as a link when it has a
`targetUrl` (DOC [PRSD]), and a status can be posted to one iteration of the pull request (DOC [PRIS]). So
S4 posts, per lane, one iteration status: context `kronikol/<label>`, state from `Run.json`
(`succeeded`, or `failed` when `failed > 0`), a description such as "203 scenarios, 2 failed", and a link to
the build's artifacts. The iteration is the one whose source commit the build ran. The build names that
commit in `System.PullRequest.SourceCommitId`, "the commit that is being reviewed in a pull request" (DOC
[VARS]). The script lists the iterations and takes the newest whose `sourceRefCommit` is that commit (DOC
[ITL]); the latest iteration is not the same thing when a push lands while the build runs. Posting to an
iteration "guarantees that status applies only to the code that was evaluated and none of the future
updates" (DOC [PRSD]), so an older build that finishes last should not cover a newer one's status
(INFERRED; S0's A21 shows what the pull request displays). No body to parse, no concurrency group, no
drift test against PR #73. The script is Node, as PR #73's is, so its tests reuse `NodeProbe` and an
in-memory driver; the template installs Node with `UseNode@1` where an agent lacks it. A status is also what a branch policy can require, which is how a team
would make `kronikol history gate` block a merge; that is a later row, not this one. `PR_REPORT_LINK_PLAN.md` (2.2)
hands 2.4 three questions: whose identity writes, which permission the build identity needs on threads,
and how the thread is found again. A status answers the third by its design, since it is keyed by its
context; S0's A21 answers the other two.

A thread is the richer surface, and since May 2026 a comment renders fenced Mermaid (DOC [RN274]). It is
where 7.4's diagrams of the scenarios a change touched belong, beside 2.2's comment on GitHub. S4 does not
build it. Whether the build identity may post statuses and threads is S0's A21: the threads API needs
"Contribute to pull requests", which the build identity lacks by default (DOC [PERM]; the refusal is
TF401027, [QA]). Microsoft's status pages name only the scope a status needs, Code (status)
(`vso.code_status`, DOC [PRSS], [PRIS]), and no repository permission; the probe finds out.

### 4.9 What stays out, and where it goes

| Item | Why not here | Where |
|---|---|---|
| Serving a dashboard without Pages | Tier 1; Blob static hosting or Static Web Apps | 9.4 (store plan §6.3 item 3) |
| Azure Blob as a store | Tier 2 | 9.5 (store plan §6.3 item 4) |
| Viewing the HTML report in the browser | Azure DevOps serves artifacts as downloads, as GitHub does. Three third-party Marketplace extensions add an HTML tab to a run today (DOC, Appendix B); installing one needs Project Collection Administrators | the wiki page names the option neutrally; a Kronikol tab is 13.2's Azure DevOps sibling (Q4) |
| The v4 summary in Mermaid on Azure DevOps | no evidence the summary tab renders it; v4 would take Azure DevOps users from diagram images to code blocks | raised for D12 and 11.1 (Q7) |
| `dotnet new` templates that scaffold a pipeline for either forge | neither forge has one today, so parity holds; it is a feature | later, on request |
| Classic release pipelines | `BUILD_BUILDID` there is the artifact's build, not the release | not claimed |
| GitLab | "where practical" (store plan Q10) | roadmap §6 |

---

## 5. Before and after

| An Azure DevOps user wants | Today | After |
|---|---|---|
| The summary on the run's page | sets `WriteCiSummary`; nothing appears (F1) | `WriteCiSummary` and one template step; GitHub needs no step for the summary but one for the upload, so the counts match |
| The reports as an artifact | sets `PublishCiArtifacts`; nothing appears (F1) | the same template step; `PublishCiArtifacts` not needed |
| To see from the summary page that the run failed | nothing | the template's warning |
| Cross-run history | no recipe; the GitHub one fails at its first fetch (F9) | the read step, the record job, one permission on one branch |
| History on a self-hosted agent, or through a flaky network | the second fold fails, or a failed read leaves a ledger `verify` rejects (F12; the GitHub recipe too) | the scripts keep nothing in the checkout and stop on a failed read |
| A test whose assertion message quotes a response body | a `##vso[` line in it is acted on (F6) | the test step runs in restricted mode; the template posts the reports |
| A re-run job recorded as a re-run | recorded as the run it re-ran (F2) | `ado:<build>:2` |
| A pull request read against its target | works on Azure Repos, cold on GitHub repositories (F3) | works on both |
| The report reachable from the pull request | nothing | S4's status, one per lane |

---

## 6. Tests, red first

Per `CLAUDE.md`, each fact is written and seen red before the code that makes it green.

**S1 (unit, `Kronikol.Tests`):**
- `CiMetadataDetectorTests`: Azure DevOps reports the job attempt (red: null). It replaces
  `Azure_devops_reports_no_attempt_rather_than_a_guess`, which pinned the defect. An unparseable attempt
  is 1, as on GitHub.
- `HistoryRunBuilderTests`: `ado:9001:2` for attempt 2 (red).
- **Azure DevOps twins of the GitHub-only integration facts** (§2's last row). The helpers that set
  GitHub's variables (`RunRotationTests.GitHub(runId, attempt)`, `MergeWritesTheRunOutputsTests.GitHub()`,
  `CiDebugSectionTests`) take the provider, and the facts run for both. Red on Azure DevOps: a re-run job is
  a different run in rotation; `query summary`'s `run:` line says "attempt 2"; CTRF carries `runAttempt`;
  the JSON, XML and YAML identity blocks carry it; `history record` of two attempts records two runs
  (`f2_rerun_fold.sh` as a fact).
- F3: `PullRequestTarget` gives `refs/heads/main` on Azure DevOps for `main` (red), and leaves
  `refs/heads/main` and GitHub's `main` alone (guards).
- F11: the link reads `SYSTEM_COLLECTIONURI` (red) and escapes a project name with a space (red).
- F1: on Azure DevOps the posted path is `Reports/CiSummary.md` (red: a temp copy); a failing run with
  `WriteCiSummary` off writes the debug section to `CiSummary.md` and lists it in `Run.json` (red).
- F4: an Azure DevOps upload includes `attachments/` (red); `merge --publish-artifacts` uploads `runs/`
  (red).
- F6, the guard: every verb `VerbTable` lists, and `history gate`, `record` and `import`, run against a
  report whose scenario names, messages and bodies hold `##vso[`, `##VSO[` and `##[set-output name=x]`,
  prints none of them (red on each), except the logging commands `merge --ci-summary --publish-artifacts`
  and `ingest` mean to print, which are unchanged (guard). One table-driven fact over the verb list, so a
  verb added later is covered by being listed. `--json` output holds `\u0023` where the report holds `#`
  and parses to the same values (red). A text body is otherwise byte-identical (guard), `--out` gives it
  back byte for byte (guard), and GitHub's flattening facts still pass. In the library: the pointer and
  the debug section, on both providers (red).

**The scripts' facts are 2.3's** (`HISTORY_ACTION_PLAN.md` §5.4, over real `bash` and `git` against a bare
origin and an HTTP one that demands a token). This plan's harness runs cases it should hold as well, each
with Azure DevOps' bearer header as well as GitHub's basic one: a failed read on a ledger an earlier
release started records nothing (S6b), a reused sources folder changes nothing (S7), a stale credential in
the checkout (S11, R5) or in the machine's global configuration (S12, R6) changes nothing, and the header's
key is set empty before the token (G11). Every commit message carries `[skip ci]`, and the ledger passes
`kronikol history verify` after each. They join 2.3's facts; S3 adds only the drift fact and the templates'
own.

**S2 and S3 (templates, the pattern of PR #73's `PrReportLinkActionTests`):**
- YamlDotNet reads each template; every parameter is wired through `env:`; no script body contains `${{`
  or a `$(identifier)` (F9); the README's pipelines pass only declared parameters and every required one.
- Each template, and each pipeline the README shows, validates against a pinned copy of Microsoft's
  `service-schema.json` (draft-07, published with the VS Code extension, DOC [VSC]) with `JsonSchema.Net`,
  which the test project already references. Measured (RUN, `schema_check.sh`): `$comment` v1.261.1,
  1,640,523 bytes, 86,347 gzipped (`-9`), so it is vendored gzipped and the test names the version it
  holds. Three things the check needs, all shown by running it on this plan's own drafts. The YAML is read with every
  scalar kept as text, as the pipeline parser reads it: the schema types a boolean as a string matching
  `^true$` and its spellings, so `fetchDepth: 1` read as a number fails. Task inputs use their canonical
  names, because the schema lists no aliases: the first draft's `DownloadPipelineArtifact@2` with `path:`
  failed, and `targetPath:` passes. And a deliberately wrong file must fail. The schema's own `ignoreCase`
  keyword is not standard, so the check is case-sensitive where the service is not; the templates keep
  the canonical casing.
- The reports script runs under `bash` against a temporary workspace with a fake Azure DevOps
  environment: two reports directories of this build and a stale one of another build. It stages exactly
  the two, `runs/` included, posts one summary per `CiSummary.md` named for its suite, and prints one
  `logissue` per failing run. Its steps carry `condition: succeededOrFailed()`.
- The drift fact: each heredoc body in the Azure DevOps templates is byte-identical to its file in 2.3's
  `templates/github-actions/kronikol-history/`.
- **Windows and macOS lanes for these facts.** CI runs Linux only (`ci.yml`, READ). GitHub's hosted
  runners are built from the same image definitions as Microsoft-hosted agents (`actions/runner-images`,
  DOC [RI]), so running the template and script facts on `windows-latest` and `macos-latest` covers Git
  Bash's paths and line endings and macOS's bash 3.2. On Windows the agent runs the same Git Bash (A20,
  settled from the task's and the image's source; S0 confirms it on an agent).

**S4:** a driver like PR #73's, with the statuses API held in memory: one status per lane, posted to the
iteration whose `sourceRefCommit` is `System.PullRequest.SourceCommitId` even when a newer iteration exists,
`failed` when `Run.json` says so, nothing posted outside a pull-request build.

**S5, the acceptance lane:** a pipeline in a real organisation runs the CI-preview projects with the
templates on every push of `main` and every release tag, and checks from the outside (REST, read-only)
that the run has the artifact, the summary section, the warning and a new ledger line, and that the ledger
push started no run. Template expansion is checked there too, with the preview API (`previewRun`,
`yamlOverride`: "return the final YAML document after parsing templates", DOC [PRV]).

---

## 7. Slices, releases, records

### 7.1 Order

S0 whenever the owner has an organisation to run it in; it blocks only the rows of §9 marked for it. S1
first of the code, by rule 1. S2 before S3: history on Azure DevOps rides on S2's artifact, and the reports
are what every Azure DevOps user lacks today. S3 after 2.3's S2, whose scripts it wraps; the roadmap's
§5 already runs 2.2, 2.3 and 2.4 as a chain. S4 after S3 or beside it (it touches none of S3's files). S5
once S2 exists, growing as each slice lands.

### 7.2 Bumps

| Slice | Bump | Why |
|---|---|---|
| S1 | **patch** | Bug fixes. Behaviour a user can see changes, and the changelog says so: `runAttempt` is set on Azure DevOps; a re-run job's id is `ado:<build>:2`; a pull request of a GitHub repository built on Azure DevOps reads against `refs/heads/<target>`; the run link uses the collection URI; a failing CI run with `WriteCiSummary` off writes `CiSummary.md`; `##vso` and `##[` in run-derived console output read `**vso` and `**[`, and the JSON the tool prints writes `#` as `\u0023` (the same values to a JSON reader); an Azure DevOps artifact carries `attachments/`. No option, member or format field is added |
| S2 to S5 | none | Templates, scripts, docs and tests; no package contains them (§2). The changelog records them under `[Unreleased]`, as PR #73 does |

### 7.3 Where the code goes

| Slice | Files | Roadmap track |
|---|---|---|
| S1 | `src/Kronikol/Reports/CiMetadata.cs`, `CiSummaryWriter.cs`, `CiArtifactPublisher.cs`, `RunSummaryConsoleWriter.cs`, `ReportGenerator.cs` (the CI block, 615-685, and the schema description), `Reports/Merge/MergedRunOutputs.cs`, `History/HistoryRunBuilder.cs`; in the tool the guard at `Program.cs`/`Commands.Dispatch` and the JSON encoder its writers share (`Query/QueryWriter.cs`, `PathEngine.cs`, `PayloadReader.cs`, `QueryCommand.Describe.cs`); the XML docs of §3.7 | A and B, lightly; no `VerbTable` or stylesheet change |
| S2 to S4 | `templates/azure-pipelines/`, `tests/Kronikol.Tests/Templates/`, the vendored schema; the Windows and macOS legs 2.3's S4 adds to CI run these facts too | C |
| S5 | a pipeline file in the repository, run by an organisation outside it | C |

### 7.4 Docs

| Where | What | Slice |
|---|---|---|
| Wiki, new `Azure-DevOps.md` | the one page an Azure DevOps user needs: what works, the template steps, the permissions, the differences from GitHub and why | S2, grown by S3 and S4 |
| Wiki `CI-Artifact-Upload`, `CI-Summary-Integration` | the Azure DevOps sections rewritten: F1 stated, the template step, the corrected example | S1, S2 |
| Wiki `Cross-Run-History` | "The recommended shape" is 2.3's S0 (F12 adds S6b and S7 to what it must survive); "On Azure DevOps" beside it; the attempt | S1, S3 |
| Wiki `Querying-Reports`, `Report-Configuration`, `Generated-Reports`, `API-Reference` | the attempt, the posted summary's path, the run link | S1 |
| README, `nuget-readme.md` | the Azure DevOps sentences of the CI paragraphs | S1 |
| `templates/README.md`, `templates/azure-pipelines/README.md` | the templates | S2 to S4 |
| Both skill copies (`commands.md`) | an `ado:` example beside `gh:` | S1 |
| CHANGELOG | per slice | all |
| Kronikol4J divergence ledger | Checked 2026-09-28 at the port's `34120a7` (READ). It prints the same two Azure DevOps lines from the test JVM at run end (`ReportFinalizer.java:184`, `:189`; `CiSummaryWriter.java:46`, a temp copy; `CiArtifactPublisher.java:51`, file by file), so F1 and F4 are its questions too: whether Gradle's test task or Surefire lets a line from the test JVM reach the console is its own measurement. It has no run identity, pull-request stream, run link or query tool, and no run-end pointer or `::notice` was found, so F2, F3, F6 and F11 are .NET-only. S1's entries: the posted summary's path and `attachments/` in the artifact (D11 keeps ledgering capture changes) | S1 |

### 7.5 S0: the half day on a real organisation

`AZURE_DEVOPS_PARITY_PLAN.harness/s0-probe.yml`, run as its header says: once, then "Rerun failed jobs",
then with Contribute granted, then as a pull-request build, then on `windows-latest`, then from a GitHub
repository. Each step is named for the ledger rows it settles; the output goes to `results-s0.txt`. What
it decides: A2 and A10 confirm §4.5's shape, for a dependent job and a dependent stage; A3 confirms F6's
reach as the agent's source reads; A4 shows how the Extensions tab titles and renders a section and
settles F7's last row; A5 confirms F3; A7 and A8 the artifact names; A11 to A14 the recipe's credential,
permission and trigger text, `[skip ci]` included; A20 confirms the Git Bash the source points to; A21
and A26 S4's permission and the iteration a status lands on; A28 and A9 what restricted mode blocks, what it
logs, and whether a warning turns a step orange. The history scripts themselves need no organisation:
the harness already runs them against a server that behaves as both forges' git endpoints do.

### 7.6 Where it sits in the roadmap

- **1.14 (new), rule 1**: S1. A live defect on a documented feature, for every Azure DevOps user of it,
  and one GitHub gap the same fix closes (`##[` anywhere in a line).
- **2.4**: S0, S2 to S5. Track C, beside anything. The row's "days" becomes about a week of work over four
  slices, plus the owner's half day for S0 and an organisation for S5.
- **One coupling.** 2.3 writes the scripts (`HISTORY_ACTION_PLAN.md` S2) and S3 wraps them, so S3
  follows 2.3's S2, and the Azure DevOps inputs of §4.5 are best written into 2.3's §4.2 before its S2
  builds it (Q9). F12 is the case for 2.3's S0 now. S4 shares nothing with 2.2, because it posts statuses
  rather than a comment, so 2.2 need not merge first.
- **For D12 and 11.1** (Q7): the Mermaid summary is a regression on Azure DevOps unless the summary tab
  renders it, and "same level" is a row of the bar.

### 7.7 Records

`PLANS_STATUS.md` gains this plan's row; `ROADMAP.md` 2.4 points here, row 1.14 and decision D27 are added
(`main` had taken D24 to D26 by the time this plan reached it), and §7's coverage line names this plan.
Written with the plan, and brought up to date by the second pass.

---

## 8. Not taken, and open questions

### 8.1 Designs not taken

- **The fetch and push in the tool** (`kronikol history push`). One implementation for every CI system,
  tested in .NET. It breaks `CROSS_RUN_HISTORY_PLAN.md` §14: the tool would make the call. The shared
  scripts give one implementation without that.
- **A better stdout for the library**: the parent's console, or walking the process tree to `dotnet test`'s
  descriptor. Platform-specific and fragile, and the rule that binds it (the agent reads the step's own
  process) would still hold.
- **The library uploading through the REST API with `System.AccessToken`.** §14 again: token handling and
  provider API code in the library.
- **Running the template repository's scripts from files** by checking that repository out as well. A
  second `checkout` puts every repository, the consumer's own included, in a subfolder named for it (DOC
  [MR]), which breaks the consumer's paths.
- **PowerShell for the Azure DevOps side.** It runs on every hosted agent, and it would give each shared
  script a twin, the maintenance sharing exists to avoid; the hosted Windows agents run Git Bash anyway
  (A20). Self-hosted Windows agents need Git for Windows' bash on the path, which the README states.
- **A comment thread as S4's link.** It needs the machinery 2.2 carries, which statuses do not; it stays
  the surface for 7.4's diagrams.
- **A Marketplace extension now.** Tasks are a package with a release train of their own, and a listing is
  outreach (rule 9). It is the natural home for a run tab that shows the HTML report, which nothing on
  GitHub matches; proposed for 13.2's Azure DevOps side.
- **The ledger in a worktree of the job's checkout**, as this repository's fold step and the first draft
  keep it. The worktree's registration outlives the job on any agent that keeps its sources folder (F12,
  S7), and the checkout's persisted credential beats the one passed (F13). A repository of its own costs
  one `git init`.
- **Rebasing through `merge=union` when a push loses a race.** It merges two ledgers as text, which is
  sound only while both sides append to the same head; F12's S6b is a case where they do not, and a
  rewriting verb (`history prune`, `compact`) would be another, by the same reasoning (not run). Folding
  again from the new tip lets the tool decide, against the ledger as it stands, and the tool already treats
  a recorded run as a duplicate.
- **Reading a failed fetch as "no branch yet" in the record step.** It is right for the read before the
  tests, where the cost is a run without verdicts, and wrong for the fold, which must not start a ledger
  it cannot see (F12). Both scripts ask `ls-remote`, and only the read carries on after a failure.
- **`***NO_CI***` as the marker.** It is Azure DevOps' own; `[skip ci]` is honoured by both forges, so one
  script serves both.

### 8.2 Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | The green light, and the order: S1 as roadmap 1.14 now, S0 and S2 now, S3 after 2.3's S2, then S4 | Yes. F1 and F2 are live on documented features; the rest is the row as written |
| Q2 | Who runs S0, and in which organisation; does the project keep one for S5 | The owner's organisation, a scratch project. S5 wants a permanent one, or the templates are claims nobody runs; a private project's free grant needs the organisation linked to an Azure subscription (§3.8) |
| Q3 | Is S4 part of 2.4, or a row of its own | Part of 2.4, after S3. The bar's "pull request loop" row is per forge |
| Q4 | Distribution: copy-in, a repository resource pinned to a tag, or a Marketplace extension | Copy-in as the documented default, the resource as the alternative, the extension at 13.2 |
| Q5 | Azure DevOps Server: supported, or not obstructed | Not obstructed. The page says where it differs (build artifacts in place of pipeline artifacts, API 7.0) and nothing is run on Server |
| Q6 | When both the library and the template post the summary | Settled by the recipe instead of a sentence: the test step runs in restricted mode, which blocks the library's attachment and upload, so only the template posts (§4.2). S0's A4 shows the tab |
| Q7 | The v4 Mermaid summary on Azure DevOps | Put it to D12: keep a PlantUML image section on Azure DevOps until the summary tab renders Mermaid, or accept code blocks there and say so |
| Q8 | The fold job, or a record step in each test job | The fold job, as on GitHub: a stage rerun reruns the stages that depend on it (DOC [RNSS], [GH]). The other only if S0's A10 contradicts the page, or if the announced rerun of a single stage becomes the way people rerun |
| Q9 | For D26 rather than D27: take 2.3's S0 now, and write §4.5's Azure DevOps inputs, `[skip ci]` and the header reset into its §4.2 and `git.sh` before its S2 builds them | Yes. F12 is rule 1 work: the wiki's recipe does not run as printed, and this repository's own fold can leave a ledger `verify` rejects after one failed fetch; 2.3's S0 needs no code. Written in before S2, the inputs cost nothing; after it, they are a second change to tested scripts |

---

## 9. Assumption ledger

| # | Statement | Basis | If wrong |
|---|---|---|---|
| A1 | `System.JobAttempt` is 1 on a job's first attempt and counts up on each retry; `System.StageAttempt` and `System.PhaseAttempt` likewise; each is an environment variable, upper-cased with `_` for `.` (`SYSTEM_JOBATTEMPT`) | DOC [VARS], [PVARS]; also listed for Server 2022 | F2's fix reads nothing; S0 A2 prints the real variables |
| A2 | "Rerun failed jobs" stays in the same run, under the same `Build.BuildId`, re-running failed jobs and their dependents | DOC [GH], [STG] (the stage retry is a call on the same build); no page says "same `Build.BuildId`" in those words | F2 would be smaller than stated; S0 A2 |
| A3 | The agent finds `##vso[` anywhere in a line, reads stderr too, splits on a bare CR, drops processed lines from the log; restricted mode still allows `task.complete`. GitHub's runner finds `##[<command>]` anywhere in a line (its legacy parser) | DOC [CMD], [NH], [PI], [TCE], [TGT], [GHR]; the "Logging commands" page says stdout and "a line that matches", which the source contradicts | F6 shrinks to lines that start with the prefix; S0 A3 |
| A4 | What the Extensions tab renders (`<details>`, external images, tables, Mermaid) and how it titles a section. The agent names a summary after its file and queues it on the posting step's own timeline record, so two steps posting one file make two attachments; `task.addattachment` takes a name | DOC [TCE], [EC] for the naming and the records; the tab itself S0 only. DOC [LOG] says only that it differs from the wiki's rendering; issues #6223 and #6997 (2018) say collapse does not work | §4.2's section names; F7's last row |
| A5 | `System.PullRequest.TargetBranch` is `main` for a GitHub repository and `refs/heads/main` for Azure Repos; `Build.SourceBranch` is `refs/heads/main` on both | DOC [VARS] | F3 is not a defect; S0 A5 |
| A6 | The Azure Pipelines app's persisted token can push to the GitHub repository | DOC [GH] ("Write access to code"); per run, UNVERIFIED | §4.6 defaults to a secret variable |
| A7 | What several jobs uploading one container artifact name with `##vso[artifact.upload]` leaves | S0 only | F4's third clause |
| A8 | A pipeline artifact cannot be overwritten, so a re-run needs `$(System.JobId)` in the name; `PublishBuildArtifacts@1` takes a path, not a pattern; task input aliases (`path` for `targetPath`) work in the service and not in Microsoft's schema | DOC [ART], [PBA], [DPA]; RUN for the schema (`schema_check.sh`) | the names in §4.2; §6's schema fact |
| A9 | `##vso[task.logissue type=warning]` goes on the task's timeline record, ten per record, without changing the result | DOC [LOG], [EC]; [RUNS] says tasks that report warnings are "succeeded with issues" | F5's fix could turn a green job orange |
| A10 | A succeeded job that depends on a re-run job runs again, and so does a stage that depends on a rerun stage | DOC [GH] ("any dependent downstream jobs"), [RNSS] ("triggers all stages that depend on it to rerun as well", with a single-stage rerun announced); S0 runs both shapes | §4.5 moves the record step into each test job |
| A11 | `checkout` persists no credential by default, and with `persistCredentials` writes `http.<repository URL>.extraheader`, removed when the job ends; pipelines created since September 2022 fetch at depth 1 in some organisations; a shallow fetch brings the build commit alone | DOC [CO], [ARG], [GSP] | nothing in §4.5 now: the scripts use their own repository |
| A12 | The two build identities, which the job authorization scope selects, and that neither can push by default | DOC [AT], [RN160], [GIT]; [AT] and [RN160] disagree on the default scope | §4.5's permission text |
| A13 | Contribute can be granted to the build identity on one branch | DOC [RPERM] (branch permissions); for the build identity, S0 A13 | the recipe grants it on the repository |
| A14 | A push by the build identity triggers CI; `***NO_CI***`, `[skip ci]` and their variants skip it, and GitHub skips `push` workflows for `[skip ci]` too; a pushed tag's message is not read | DOC [GIT], [ARG], [GHSKIP] | runs start on the data branch |
| A15 | The agent reads logging commands from the output of the step's process | DOC [NH] | F1 would have another way through |
| A16 | Azure DevOps has no file-based channel for logging commands | DOC [LOG] (none documented) | the library could write it as it does for GitHub, and S2 would shrink |
| A17 | Public projects are retired and become private in 2027; the free grant for private projects is one job and 1,800 minutes a month once linked to an Azure subscription | DOC [PPR], [CJ] (2026) | S5's cost; 9.4's note |
| A18 | A template in a GitHub repository needs a service connection; a template's repository contributes no scripts; a second `checkout` moves the sources into subfolders | DOC [RR], [TPL], [MR]; [TPL]'s own example omits the endpoint | distribution in §4.7 |
| A19 | `$(name)` in an inline script is replaced before the shell runs when the variable exists and left as written when it does not; `${{ }}` is replaced at compile time and empty when unknown | DOC [PVARS] | the macro test is a guard rather than a need |
| A20 | Microsoft-hosted images carry bash (3.2.57 on macOS); a `bash` step on Windows runs Git Bash (5.3 on the September 2026 image), not WSL's | INFERRED from source, against [BASH3]'s prose: Bash@3 runs the first `bash` on the path (DOC [BASHSRC]); the Windows 2025 image prepends `C:\Program Files\Git\bin` to the machine path and installs WSL with no distribution (DOC [IMG]); S0 A20 confirms on an agent | the shared scripts need a PowerShell twin for Windows |
| A21 | The build identity may post iteration statuses; threads need "Contribute to pull requests", not granted by default | DOC [PRIS], [PRSS] (the scope `vso.code_status`, no repository permission named), [PERM], [QA]; the statuses permission is UNVERIFIED | S4 needs a permission step in the recipe |
| A22 | `System.CollectionUri` is the organisation URI in builds; `System.TeamFoundationServerUri` is listed only for classic releases, as the Release Management URL | DOC [VARS], [RELV] | F11 is cosmetic |
| A23 | GitHub-hosted runners and Microsoft-hosted agents are built from the same image definitions | DOC [RI] | the Windows and macOS lanes of §6 prove less |
| A24 | A `-c http.extraheader=…` with no URL is not sent when the configuration holds a URL-specific one, and an empty one does not clear it | RUN (git 2.43; `record_script.sh` G2, G3, G3b, G4) | F13 is not a defect; the scripts' own repository stays the simpler shape |
| A25 | `git ls-remote --exit-code` exits 2 for a missing ref, and 128 for a refused credential or a server error | RUN (G6 to G8) | the scripts could not tell a missing branch from a failure |
| A26 | `System.PullRequest.SourceCommitId` is set on a pull-request build a branch policy triggers, and a pull request iteration lists its `sourceRefCommit` | DOC [VARS], [ITL] | S4 falls back to the latest iteration |
| A27 | `DownloadPipelineArtifact@2` with no artifact name downloads every artifact of the run, a sub-directory each, and its exclude patterns do not exclude | DOC [DPA] | the fold's download step |
| A28 | Restricted mode blocks `task.addattachment`, `task.uploadsummary` and `artifact.upload` from the step it is set on, and logs a warning for each | DOC [TCE], [TRC], [TGT], [SECT] | §4.2 falls back to naming the duplicate case on the page (Q6) |
| A29 | Setting `http.<server>.extraheader` empty and then to the token sends the token alone, whatever the machine's configuration holds for that server or for none, through `-c` or `GIT_CONFIG_COUNT` | RUN (git 2.43.0; `record_script.sh` G11, S12, R6) | 2.3's scripts, and these, send two Authorization headers on such a machine |

---

## Appendix A. The recipe, drafted

The shape S3 documents for an Azure Repos repository. Both YAML blocks pass Microsoft's schema
(`schema_check.sh`, §6). The platform behaviour they rely on is DOC until S0 runs it; the scripts' design is
RUN (prototyped and tested in `record_script.sh`).

```yaml
trigger:
  branches:
    include: [ main ]
    exclude: [ kronikol-history ]

pool:
  vmImage: ubuntu-latest

stages:
- stage: test
  jobs:
  - job: tests
    strategy:
      matrix:
        component:   { project: tests/Shop.Tests.Component }
        integration: { project: tests/Shop.Tests.Integration }
    steps:
    - checkout: self                  # no persistCredentials: the tests never hold a token
    - task: UseDotNet@2
      inputs: { packageType: sdk, version: 10.0.x }
    - template: templates/azure-pipelines/kronikol-history-read.yml
    - script: dotnet test $(project) -c Release
      displayName: Test
      target:                         # §4.2: nothing the tests print may drive the agent;
        commands: restricted          # the reports template posts the summary and the artifact
        settableVariables: none
    - template: templates/azure-pipelines/kronikol-reports.yml   # its steps: succeededOrFailed()
      parameters:
        searchRoot: $(project)

- stage: history
  dependsOn: test
  condition: and(not(canceled()), ne(variables['Build.Reason'], 'PullRequest'))
  jobs:
  - job: record
    steps:
    - checkout: none                  # §4.5: the ledger gets a repository of its own
    - task: UseDotNet@2
      inputs: { packageType: sdk, version: 10.0.x }
    - task: DownloadPipelineArtifact@2
      inputs:
        itemPattern: '**/History.run.json'
        targetPath: $(Agent.TempDirectory)/fragments
    - template: templates/azure-pipelines/kronikol-history-record.yml
      parameters:
        fragments: $(Agent.TempDirectory)/fragments
```

The record template, drafted. Every parameter reaches the scripts through `env:`, under the neutral names
this plan proposes for 2.3's §4.2 (Q9). Each script is written out from a quoted heredoc of its own, byte
for byte 2.3's file (the drift fact), and run with `bash`. The read template has the same shape around
2.3's read, and turns a remote it could not read into `##vso[task.logissue type=warning]` and a ledger it
found into `##vso[task.setvariable variable=KRONIKOL_HISTORY]`.

```yaml
parameters:
- name: fragments
  type: string
- name: auth                          # §4.6: a GitHub repository passes a header GitHub accepts
  type: string
  default: 'AUTHORIZATION: bearer $(System.AccessToken)'
- name: toolVersion
  type: string
  default: '3.31.9'

steps:
- bash: |
    dotnet tool install Kronikol.Tool --version "$KRONIKOL_TOOL_VERSION" --tool-path "$AGENT_TEMPDIRECTORY/kronikol-tool"
    echo "##vso[task.prependpath]$AGENT_TEMPDIRECTORY/kronikol-tool"
  displayName: Install the Kronikol tool
  env:
    KRONIKOL_TOOL_VERSION: ${{ parameters.toolVersion }}
- bash: |
    cat > "$KRONIKOL_TEMP/kronikol-history-record.sh" <<'KRONIKOL'
    set -euo pipefail
    # … the rest of 2.3's record script, byte for byte (and git.sh beside it, from a heredoc of its own) …
    KRONIKOL
    bash "$KRONIKOL_TEMP/kronikol-history-record.sh"
  displayName: Record the runs in the history ledger
  target:                             # the tool prints run-derived text (§3.6)
    commands: restricted
    settableVariables: none
  env:
    KRONIKOL_REMOTE: $(Build.Repository.Uri)
    KRONIKOL_GIT_AUTH: ${{ parameters.auth }}
    KRONIKOL_FRAGMENTS: ${{ parameters.fragments }}
    KRONIKOL_TEMP: $(Agent.TempDirectory)
    KRONIKOL_RUN_LABEL: ado:$(Build.BuildId) $(Build.SourceVersion)
```

What the record script does, against the fold step it replaces: 2.3's design (`HISTORY_ACTION_PLAN.md`
§4.5, §4.9), as prototyped here in `kronikol-history-record.sh` and run in `record_script.sh`:

| | This repository's fold step (v0) | 2.3's design, prototyped here |
|---|---|---|
| Where the ledger is checked out | a worktree of the job's checkout, under the temp folder | a repository of its own under the temp folder; no checkout needed |
| Credential | whatever the checkout persisted | the header passed, under the server's key set empty first: the only one sent (F13, G11) |
| No branch, or no reachable remote | one test, `git fetch`: any failure is "no branch" | `ls-remote --exit-code`: 2 is "no branch", anything else stops the step (F12) |
| Fetch depth | the branch's whole history | one commit, however long the branch grows |
| A lost race | fetch and `rebase` through `merge=union` | start again from the new tip and fold again (2.3 adds a jittered pause, eight attempts, and a stop when the tip has not moved) |
| No fragments | exits 0 | exits 0 |
| Marker | none (a `GITHUB_TOKEN` push starts no workflow) | `[skip ci]`, which both forges honour |
| Bash | 5 on `ubuntu-latest` | 3.2 or later: every array expansion guarded for `set -u` |

## Appendix B. Sources

Read on 2026-09-27; the keys marked † were read on 2026-09-28, in the second pass. Microsoft Learn pages
under `https://learn.microsoft.com/en-us/azure/devops/` unless the key says otherwise.

| Key | Page |
|---|---|
| [AT] | `pipelines/process/access-tokens` (job access tokens, the two build identities, the scope setting) |
| [ARG] | `pipelines/repos/azure-repos-git` (CI triggers, skip markers, shallow fetch, the extraheader form) |
| [ART] | `pipelines/artifacts/artifacts-overview` (pipeline artifacts cannot be overwritten; `$(System.JobId)`) |
| [BASH3] | `pipelines/tasks/reference/bash-v3` |
| [BASHSRC] † | `https://github.com/microsoft/azure-pipelines-tasks/blob/master/Tasks/BashV3/bash.ts` (`let bashPath: string = tl.which('bash', true);`, line 108; on Windows the script path is translated by running `bash -c pwd`) |
| [CJ] | `pipelines/licensing/concurrent-jobs` (the 2026 free grant) |
| [CO] | `pipelines/yaml-schema/steps-checkout` (`persistCredentials`) |
| [DPA] | `pipelines/tasks/reference/download-pipeline-artifact-v2` († for the multi-download sub-directories, "Exclude patterns cannot be used to exclude previously included files", and the aliases `path`, `downloadPath`) |
| [EXTAPI] † | `https://learn.microsoft.com/en-us/javascript/api/azure-devops-extension-api/updatestageparameters` (`forceRetryAllJobs`, `retryDependencies`, `state`, none described) |
| [GH] | `pipelines/repos/github` (the app's permissions, re-runs, forks) |
| [GHR] † | `https://github.com/actions/runner/blob/main/src/`: `Runner.Common/ActionCommand.cs` (`Prefix = "##["` at 35; `TryParse` finds it anywhere, `message.IndexOf(Prefix)` at 132) and `Runner.Worker/ActionCommandManager.cs` (`TryProcessCommand` tries the `::` form, then the legacy one, 60-71; `set-output` at 308-351; `set-env` and `add-path` refused without `ACTIONS_ALLOW_UNSECURE_COMMANDS`) |
| [GHSKIP] † | `https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs` (`[skip ci]`, `[ci skip]`, `[no ci]`, `[skip actions]`, `[actions skip]`, for `push` and `pull_request`) |
| [GHC] | `pipelines/tasks/reference/github-comment-v0` |
| [GIT] | `pipelines/scripts/git-commands` (the build identity cannot push by default; skipping CI) |
| [IMG] † | `https://github.com/actions/runner-images/tree/main/images/windows`: `scripts/build/Install-Git.ps1:51` (`Add-MachinePathItem "C:\Program Files\Git\bin"`), `scripts/helpers/PathHelpers.ps1:92-94` (it prepends: `$PathItem + ';' + $currentPath`), `scripts/build/Install-WSL2.ps1` (`wsl.exe --install --no-distribution`), `scripts/build/Install-Msys2.ps1` (msys2 on the path only during its own install), `scripts/build/Configure-Shell.ps1` (`gitbash.exe`, `wslbash.exe`), `Windows2025-Readme.md` (image 20260922.270.2: Bash 5.3.15, Git 2.55.0) |
| [ITL] † | REST: `https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-iterations/list?view=azure-devops-rest-7.1` (`sourceRefCommit`, `targetRefCommit`, `commonRefCommit`) |
| [LOG] | `pipelines/scripts/logging-commands` |
| [MR] | `pipelines/repos/multi-repo-checkout` |
| [PBA] | `pipelines/tasks/reference/publish-build-artifacts-v1` ("Wildcards are not supported") |
| [PERM] | `organizations/security/permissions` (Contribute, Create branch, Bypass policies, Contribute to pull requests) |
| [PPA] | `pipelines/tasks/reference/publish-pipeline-artifact-v1` (Services only) |
| [PPR] | `organizations/projects/public-projects-retirement` |
| [PRIS] | REST: `https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-iteration-statuses/create?view=azure-devops-rest-7.1` |
| [PRSD] | `repos/git/pull-request-status` († "Posting status to a specific iteration of a PR guarantees that status applies only to the code that was evaluated") |
| [PRSS] † | `repos/git/create-pr-status-server` ("Microsoft Entra ID token with the **Code (status)** scope to have permission to change PR status") |
| [PRV] | REST: `https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/preview/preview?view=azure-devops-rest-7.1` |
| [PVARS] | `pipelines/process/variables` (macro, template and runtime syntax; environment names) |
| [QA] | `https://learn.microsoft.com/en-us/answers/questions/5770166/permissions-error-with-pipeline` (TF401027) |
| [RELV] | `pipelines/release/variables` |
| [RET] | `pipelines/policies/retention` |
| [RI] | `https://github.com/actions/runner-images` (Windows 2025, Ubuntu 24.04, macOS 15 image readmes) |
| [RN160] | `release-notes/2019/sprint-160-update` |
| [RN274] | `release-notes/2026/wiki/sprint-274-update` (Mermaid in pull-request comments) |
| [RNSS] † | `release-notes/roadmap/2024/rerun-single-stage` ("you can rerun a successful stage, but it triggers all stages that depend on it to rerun as well"; a single-stage rerun announced) |
| [RPERM] | `repos/git/set-git-repository-permissions` |
| [RR] | `pipelines/yaml-schema/resources-repositories-repository` |
| [RUNS] | `pipelines/process/runs` |
| [SECT] | `pipelines/security/templates` (agent logging-command restrictions) |
| [STG] | REST: `https://learn.microsoft.com/en-us/rest/api/azure/devops/build/stages/update?view=azure-devops-rest-7.1` |
| [TGT] | `pipelines/yaml-schema/target` |
| [TPL] | `pipelines/process/templates` |
| [VARS] | `pipelines/build/variables` (predefined variables, including the Server 2022 table) |
| [VSC] | `https://github.com/microsoft/azure-pipelines-vscode` (`service-schema.json`) |
| [XPLAT] | `pipelines/scripts/cross-platform-scripting` |
| Agent source | `https://github.com/microsoft/azure-pipelines-agent/blob/master/src/`: [CMD] `Microsoft.VisualStudio.Services.Agent/Command.cs`; [NH] `Agent.Worker/Handlers/NodeHandler.cs`; [PI] `Agent.Sdk/ProcessInvoker.cs`; [TCE] `Agent.Worker/TaskCommandExtension.cs` († `uploadsummary` names the attachment `Path.GetFileName(data)`, 249-273; `addattachment` takes `type` and `name`, 304-361; which commands carry `AllowedInRestrictedMode`); [EC] `Agent.Worker/ExecutionContext.cs` († `QueueAttachFile` queues on the step's own record, 821-833); [JSQ] `Microsoft.VisualStudio.Services.Agent/JobServerQueue.cs`; [GSP] `Agent.Plugins/GitSourceProvider.cs` († the credential written only with `persistCredentials`, "handle expose creds", 1215-1231, and removed after the job, 1481-1512); [TRC] † `Agent.Worker/TaskRestrictionsChecker.cs` (a blocked command logs `CommandNotAllowed` as a warning); `Agent.Sdk/Util/StringUtil.cs`; the logger's `message.Contains("##[group]")` in `Microsoft.VisualStudio.Services.Agent/Logging.cs` |
| Marketplace | the three HTML-tab extensions: `JakubRumpca.azure-pipelines-html-report`, `LakshayKaushik.PublishHTMLReports`, `blakyaks.azure-pipelines-html-reports` (`https://marketplace.visualstudio.com/items?itemName=…`) |
