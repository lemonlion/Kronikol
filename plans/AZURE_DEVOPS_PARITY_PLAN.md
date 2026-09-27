# Azure DevOps at the same level (roadmap 2.4)

**Date:** 2026-09-27 · **Repo version:** 3.31.9 (`e4c9e36`, origin/main) · **Status: written, nothing
implemented, NOT green-lit; needs D24.** Roadmap item **2.4**, the Azure DevOps half of the bar's
"both forges first-class" row (`ROADMAP.md` §0), and a proposed stage-1 row, **1.14**, for the live
defects checking found. §9 is the assumption ledger and Appendix B lists every Microsoft page it cites.
The three measurements that could be taken without an Azure DevOps organisation are in
[`AZURE_DEVOPS_PARITY_PLAN.harness/`](AZURE_DEVOPS_PARITY_PLAN.harness/README.md) with their output, and so
is the probe pipeline for the half day that needs one (S0, §7.5).

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
  line) was built for GitHub's runner and protects nothing here.
- **Most of what the GitHub recipe assumes does not hold on Azure Pipelines** (F9, DOC): the checkout keeps
  no credential for later steps, a shallow checkout has no remote branches to put a worktree on, a push by
  the build identity triggers CI where `GITHUB_TOKEN`'s does not, a re-run cannot republish an artifact
  under its old name, and the build identity "can read from the repo but can't push any changes to it"
  until someone grants it Contribute.

**What this changes about the row.** It was "days" of templates with no bump. Checking moved the first
work out of it and ahead of it: the library's own Azure DevOps channels (F1 to F4, F6, F11) are live
defects, so by rule 1 they are a patch in stage 1, proposed as **1.14**. The row itself becomes four
slices in an order the findings set: the reports first (without them an Azure DevOps user sees nothing
at all), then the history recipe and its templates (the row's two named deliverables), then the
pull-request link (the sibling of 2.2), then a lane on a real organisation that keeps all of it true.
Nothing in the row is in a package, so the row still moves no version; 1.14 is a patch.

---

## 0. Summary

| # | Finding | Basis | Where |
|---|---|---|---|
| F1 | Every Azure DevOps channel the library has is a line on the test host's stdout, which `dotnet test` swallows: 0 of 8 VSTest projects at the default verbosity, 3 of 8 at `normal`, 0 of 3 TUnit projects under `dotnet run`. GitHub's channels are files | RUN | §3.1 |
| F2 | `ado:<build id>:1` whatever the attempt; `ciMetadata.runAttempt` always null on Azure DevOps. A re-run job's fragment becomes a duplicate or a phantom shard | RUN; DOC for the variable | §3.2 |
| F3 | For a GitHub repository built by Azure Pipelines the pull-request target is `main` while runs record under `refs/heads/main`, so every pull request reads against a stream with no runs | DOC | §3.3 |
| F4 | An Azure DevOps artifact carries less than GitHub's: top-level files of six extensions, never `attachments/`; `merge --publish-artifacts` never uploads `runs/`; every job uploads into one container name, through the upload path Microsoft now discourages | READ, DOC | §3.4 |
| F5 | GitHub gets a `::notice` annotation on a failing run, Azure DevOps nothing | READ | §3.5 |
| F6 | The agent acts on `##vso[` anywhere in a line; the tool prints scenario names, error messages and captured bodies to stdout | DOC (agent source) | §3.6 |
| F7 | Eighteen statements, in seven groups, in the wiki, the README, XML docs, code comments and the report schema are false on Azure DevOps | READ, RUN, DOC | §3.7 |
| F8 | The store plan's four Azure Pipelines claims were never checked; its §11 Q2 says the half day was spent, but R19 checked storage only | READ | §3.8 |
| F9 | What the GitHub recipe assumes and Azure Pipelines does not give | DOC | §3.9 |
| F10 | The three-line `kronikol-history` action the store plan promises cannot be one composite action: the recipe acts at three points in two jobs. It limits 2.3 as much as 2.4 | READ | §3.10 |
| F11 | The run's link is built from `SYSTEM_TEAMFOUNDATIONSERVERURI`, which the build-variables reference does not list (the release reference gives it as the Release Management URL), and the project name goes into it unescaped | READ, DOC | §3.11 |

| Slice | What | Bump | Roadmap |
|---|---|---|---|
| S0 | The half day on a real organisation: `s0-probe.yml`, ledger rows marked S0 | none | 2.4, first; the owner's hand |
| S1 | The library on Azure DevOps: the attempt (F2), the pull-request target (F3), what an artifact carries (F4), the summary posted from the reports directory (F1), `##vso[` neutralised in run-derived output (F6), the run link (F11), the statements of F7 | **patch** | **1.14** (new, rule 1) |
| S2 | Reports on Azure DevOps: the recipe and `kronikol-reports.yml` (publish each reports directory, post its summary, annotate a failure) | none | 2.4 |
| S3 | Tier 0 history on Azure Pipelines: the recipe and the history templates, one script shared with 2.3 | none | 2.4 |
| S4 | The pull-request link on Azure Repos, the sibling of 2.2 | none | 2.4, pending Q3 |
| S5 | An acceptance lane on a real organisation | none | 2.4, pending Q2 |

---

## 1. How far each claim was checked

- **RUN**: executed here on 2026-09-27 (SDK 10.0.401, Linux), output in the harness.
- **READ**: the source, a plan or the wiki (`../Kronikol.wiki`, cloned at its head today) was read today.
- **DOC**: Microsoft's documentation, read today as published markdown, or the agent's source
  (`microsoft/azure-pipelines-agent`, `master`); the pages are keyed in Appendix B. Nothing Azure-side was
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
| Tier 0 history recipe | wiki `Cross-Run-History`, "The recommended shape"; dogfooded in `ci-summary-preview.yml` (82 lines of history YAML: 9 read, 7 upload, 66 in the fold job, 44 of them the fold step) | none | READ |
| Last-green baseline recipe | `gh run download` | `DownloadPipelineArtifact@2`, `runVersion: latestFromBranch` (wiki `Merging-Parallel-Reports`) | READ |
| Pull-request link (2.2) | PR #73, green since 2026-09-15, conflicting | none | READ |
| History action (2.3) | not built; `templates/github-actions/` does not exist | not built | READ |
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
| TUnit (plain, LightBDD, Reqnroll) | | | `dotnet run --project`: 0 of 3 |

The count is of the run-end pointer ("Kronikol: reports written to"), which goes through the same
`Console.WriteLine` in the same hook as the logging commands, a few lines later. On the one project that
posts a summary (`CiPreview.AllPassing`, xUnit v3), the `##vso[task.uploadsummary]` line itself reached
`dotnet test`'s stdout and stderr 0 times at the default verbosity, at `normal`, at `detailed` and under
`--logger "console;verbosity=detailed"`, while the file it names was written every time (one file in a
per-run `TMPDIR`); the same run with GitHub's variables put 35,220 bytes into `$GITHUB_STEP_SUMMARY`. Not
even `detailed` let it through on that project, where the remark above says it should. Nor is stderr a
channel: the agent reads it too, but the repository's own measurement has xUnit 2 swallowing both streams
(NUnit 4 let stderr through), so it would trade one runner's silence for another's.

**What an Azure DevOps user gets today**, following the wiki (`CI-Artifact-Upload`: "On Azure DevOps,
that's all you need — artifacts are uploaded automatically"; `CI-Summary-Integration`: "The summary is
automatically uploaded"): no summary, no artifact and no debug section, with no error, unless the tests
run under xUnit 2 at `normal` or above, or as an xUnit v3 executable. `kronikol merge` and `kronikol
ingest` do work, because there the tool is the step's own process.

**The fix is GitHub's shape, not a better stdout.** On GitHub the library writes files and a workflow step
uploads them. Azure DevOps has no file channel for logging commands (DOC [LOG]), so the equivalent is a
pipeline step after the tests that posts what the library wrote: the recipe and template of S2. The
library's part (S1) is small. It posts the summary from `Reports/CiSummary.md` rather than a temp copy,
so the file the pipeline posts is the file the library would have posted; the agent needs that file to
stay on disk until the job ends, because it uploads it later from a queue (DOC [EC], [JSQ]), and a reports
directory does. It writes the debug section of a failing run to `CiSummary.md` when the full summary is
off, so there is always one file to post. And the docs stop promising an upload that `dotnet test`
prevents. The library keeps printing its commands: where they get through (xUnit 2 at `normal`, an
executable, the tool) they work. What the agent does with one file posted twice is S0's A4 (§4.2).

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
| Once, after the re-run | one run of "22 scenarios from 2 shards: 21 passed, 1 failed", the same eleven scenarios twice | |

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

### 3.6 F6: on Azure DevOps a captured body can drive the agent

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
RegexOptions.IgnoreCase)` (DOC, `StringUtil.cs`). S1 does the same, and does `##[` too (the log viewer's
formatting commands; whether they must start a line is unverified, so they are treated as if not). A
step-level `target: { commands: restricted, settableVariables: none }` is the platform's second line: it
blocks artifacts, attachments and variables, but still lets `task.complete` through, so an injected
`##vso[task.complete result=Succeeded;done=true]` can still end a step early (DOC [TGT], [SECT], [TCE]).
The templates use it on steps that run the tool; it does not replace the rewrite.

### 3.7 F7: what is written down that is false on Azure DevOps

| Where | Says | Why false |
|---|---|---|
| `CiMetadata.cs:75-76`; `HistoryRunBuilder.cs:163`; `HistoryModel.cs:168`; `ReportConfigurationOptions.cs:568`; the report schema's `runAttempt` description (`ReportGenerator.cs:5990`, "Null off GitHub Actions"); wiki `Querying-Reports` (361) and `Report-Configuration` (192); the changelog entry that introduced `ciMetadata.runAttempt` | Azure DevOps has no attempt number, or the id is `ado:<build>:1` | F2 |
| Wiki `Cross-Run-History` (86) | "A re-run of a failed job has a different attempt number and is a different run" | not on Azure DevOps (F2) |
| Wiki `CI-Artifact-Upload` | "On Azure DevOps, that's all you need — artifacts are uploaded automatically" | F1 |
| Wiki `CI-Summary-Integration` and `Generated-Reports` (724, 800); README (136-142); `nuget-readme.md` (62) | the summary and the artifacts reach Azure DevOps automatically | F1 |
| Wiki `CI-Summary-Integration`, the Azure DevOps example | `PublishBuildArtifacts@1` with `pathToPublish: '**/Reports'` | "Wildcards are not supported" (DOC [PBA]) |
| `ReportConfigurationOptions.cs:348` (`PublishCiArtifacts`) | "(GitHub Actions)" | names one provider of the two the code serves |
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
| `actions/checkout` leaves its token in `.git/config`, so a later `git fetch origin kronikol-history` works | `persistCredentials` is off by default: "The default is not to leave it" | each git command that talks to `origin` passes the token itself, in one step, never persisted | DOC [CO], [ARG] |
| the fetch in the fold step "also updated origin/kronikol-history, which can" be named from a worktree | a shallow checkout (depth 1 in pipelines created since September 2022) fetches the build commit alone, so no remote branch exists locally | every fetch of the data branch names an explicit refspec | DOC [ARG], [GSP] |
| `GITHUB_TOKEN` with `contents: write` in the fold job only | `System.AccessToken` is the build identity in every job, with whatever the project grants it | grant Contribute on the data branch alone, not on the repository | DOC [AT], [PERM] |
| a push made with `GITHUB_TOKEN` starts no workflow | "How can I avoid triggering a CI build when the script pushes?" "add `[skip ci]`" | `***NO_CI***` in every ledger commit, and the data branch excluded from the trigger | DOC [GIT], [ARG] |
| the workflow token may push a new branch | "By default, this identity can read from the repo but can't push any changes to it" | the recipe names the identity and two permissions, or the branch is made once by hand | DOC [GIT] |
| "Re-run failed jobs" re-runs the jobs that `needs` them | "Only those jobs that failed … and any dependent downstream jobs will be run again" | holds: the fold stays a dependent job (S0's A10 confirms it for a dependent that succeeded) | DOC [GH] |
| an artifact name reused by a re-run is replaced | "Pipeline artifacts cannot be deleted or overwritten. To regenerate artifacts when rerunning a failed job, include the job ID in the artifact name" | every artifact the templates publish ends in `$(System.JobId)` | DOC [ART] |
| a composite action carries its own script files | "Only the template files are used … you can't use scripts from the template repo" | scripts are inline in the templates, so the script shared with 2.3 is held byte-identical by a test | DOC [TPL] |
| in a `run:` script only `${{ }}` is the runner's; `$(…)` is the shell's | an inline script is a task input: `$(name)` is replaced before the shell runs when a variable of that name exists, and `${{ }}` at compile time, empty when unknown | the shared script contains no `$(identifier)` and no `${{` | DOC [PVARS] |
| one runner, `ubuntu-latest`, and its bash | the agent's OS is the consumer's choice; macOS images ship bash 3.2.57; on Windows the docs disagree whether a `bash` step runs WSL's or Git's | the script is bash 3.2 compatible; S0's A20 records which bash Windows runs | DOC [RI], [BASH3], [XPLAT] |

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
3. prints `##vso[task.uploadsummary]` for each `CiSummary.md`, and `##vso[task.logissue type=warning]`
   with the pointer's first line for each `Run.json` with `failed > 0`;
4. publishes the staging folder as one pipeline artifact whose name ends in `$(System.JobId)` (F9).

**One summary posted twice.** Where the library's own command does get through (xUnit 2 at `normal` or
above), the template posts the same file again. If the agent shows one section for one path posted twice,
that costs nothing. If it shows two, the page names the one case where it happens; nothing in the library
changes for it, because turning the library's command off would take the summary from `kronikol merge`
and `ingest`, where it is the only channel. S0's A4 decides which sentence the page carries (Q6).

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

One function, used by every writer that prints run-derived text to a console (`QueryWriter.Line` and
`Payload`, `RunSummaryConsoleWriter`, the merge and ingest pointers), rewrites `##vso` to `**vso`, ignoring
case, as the agent's own helper does, and `##[` to `**[` the same way. It applies on every provider,
because a report written on GitHub is also read by `kronikol query` on Azure DevOps, and the change is
visible, so a reader sees the text was altered rather than wondering at an invisible character. `OneLine`
already folds a bare carriage return, which the agent also treats as a line end. Files (`Failures.md`, the
digest, the report, anything `--out` writes) are not touched: the agent never reads a file as a log. So a
captured body printed by `kronikol query body` can differ from the report in exactly these two prefixes,
and `--out` gives it back byte for byte. GitHub's column-zero rule stands as it is.

### 4.5 Tier 0 history on Azure Pipelines (S3)

The ledger lives where it lives on GitHub: `history.jsonl` on an orphan branch, `kronikol-history`, with
`history.jsonl merge=union text eol=lf` in its `.gitattributes`. Three places in the pipeline:

- **Before the tests, in every test job: read.** Fetch the data branch with the token passed for that one
  command: `git -c http.extraheader="AUTHORIZATION: bearer $SYSTEM_ACCESSTOKEN" fetch --depth=1 origin
  +refs/heads/kronikol-history:refs/remotes/origin/kronikol-history` (the header is the agent's own form,
  DOC [GSP], [ARG]). Write `history.jsonl` into `Agent.TempDirectory` and set `KRONIKOL_HISTORY` for later
  steps with `##vso[task.setvariable]`, the equivalent of `GITHUB_ENV` (DOC [LOG]). The token is mapped
  into that step's environment only and never persisted, so the test step never holds it. No branch yet
  means no history, as on GitHub.
- **After the tests, in every test job: publish.** Nothing new: `History.run.json` sits in the reports
  directory and S2's artifact carries it. A run's artifacts live as long as the run (DOC [RET]), so the
  fragment needs no artifact of its own; on GitHub it has one only because the reports artifact keeps one
  day.
- **Once, after every test job: fold and push.** Download `**/History.run.json` from every artifact of
  this run into one folder (DOC [DPA]); `kronikol history record <folder> --history
  <worktree>/history.jsonl`; commit with `***NO_CI***` in the message; push with the fetch-and-rebase
  retry. A run already recorded is a duplicate, so the job is safe to re-run, and after a job re-run it
  records the new attempt (§3.2's third column). It skips pull-request builds (`Build.Reason`), as GitHub's
  recipe records only pushes of `main`.

If S0 finds a succeeded dependent job is not re-run (A10, against the DOC reading above), the record step
moves to the end of each test job instead, where a job's re-run re-runs it. That is correct when each job
runs its own suite (one test project per job, the documented setup), because a ledger holds a line per run
and suite; a suite split across jobs keeps the fold job and a documented "Rerun stage" of it. The script
is the same either way; only its place differs.

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

**Triggers.** The trigger excludes `kronikol-history`, and every ledger commit carries `***NO_CI***`
(DOC [ARG]), so neither this pipeline nor any other on the repository starts a run for a ledger commit.
A branch without the pipeline's YAML probably triggers nothing on its own ("the YAML file that is in the
branch you are pushing is evaluated", DOC [ARG]); the marker is kept regardless, for pipelines whose YAML
lives elsewhere.

### 4.6 GitHub repositories built by Azure Pipelines

The same recipe, with two differences. The read and the push need a credential GitHub accepts, not
`System.AccessToken`. The Azure Pipelines app is granted "Write access to code" (DOC [GH]), so the
checkout's persisted credential may push, but whether its token can for a given run, and for how long it
lives, is unverified; the templates therefore take the authorisation header as a parameter, defaulting to
the build identity's for Azure Repos, and the GitHub page says which to pass (S0's step 8 decides between
the app's persisted token and a secret variable). And the pull-request target needs F3's fix. S4 does not
apply there: that repository's pull requests are on GitHub, where 2.2's action runs only under GitHub
Actions. Azure Pipelines' `GitHubComment@0` task can add a comment through an OAuth or PAT connection
(DOC [GHC]); keeping one comment current is what 2.2's action does, so the GitHub page points there.

### 4.7 The templates

```
templates/azure-pipelines/
  README.md
  kronikol-reports.yml          # steps, after the tests (S2)
  kronikol-history-read.yml     # steps, before the tests (S3)
  kronikol-history-record.yml   # steps, in the fold job (or at the end of each test job, A10) (S3)
  kronikol-pr-link.yml          # steps, on pull-request builds of Azure Repos (S4)
templates/scripts/
  kronikol-history-record.sh    # the canonical fold-and-push script, inlined by both forges (S3)
```

- **Step templates are the unit**, the sibling of a composite action. Each takes typed parameters and
  passes every one to its script through `env:`, never into the script text: an Azure DevOps parameter in a
  script body is the same injection PR #73's action avoids for `${{ inputs }}`.
- **No template expression beyond `${{ parameters.x }}`** in the step templates: no `${{ if }}`, no
  `${{ each }}`. Conditions are runtime `condition:` expressions. That keeps the tests' resolver as small as
  PR #73's, and each template reads as the YAML it expands to.
- **The history script is written once**, in `templates/scripts/`, and inlined into 2.3's action and into
  `kronikol-history-record.yml`; a drift test holds the copies byte-identical, as `SkillDriftTests` holds
  the two `query.py` copies. It contains no `$(identifier)` and no `${{` (F9), is bash 3.2 compatible, and
  reads everything provider-specific from neutral variables its wrapper sets (`KRONIKOL_TEMP`,
  `KRONIKOL_RUN_LABEL`, `KRONIKOL_GIT_AUTH`, `KRONIKOL_FRAGMENTS`).
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
the build's artifacts. The iteration is the one whose source commit the build ran, so an older build that
finishes last should not cover a newer one's status (INFERRED; S0's A21 shows what the pull request
displays). No body to parse, no concurrency group, no drift test against PR #73. The script is Node, as
PR #73's is, so its tests reuse `NodeProbe` and an in-memory driver; the template installs Node with
`UseNode@1` where an agent lacks it. A status is also what a branch policy can require, which is how a team
would make `kronikol history gate` block a merge; that is a later row, not this one.

A thread is the richer surface, and since May 2026 a comment renders fenced Mermaid (DOC [RN274]). It is
where 7.4's diagrams of the scenarios a change touched belong, beside 2.2's comment on GitHub. S4 does not
build it. Whether the build identity may post statuses and threads is S0's A21: the threads API needs
"Contribute to pull requests", which the build identity lacks by default (DOC [PERM]; the refusal is
TF401027, [QA]).

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
- F6: a scenario named `a ##vso[task.setvariable variable=X]y` and a body holding `##VSO[` reach stdout as
  `**vso[` in the pointer, the debug section, and `query scenarios`, `failures` and `body` (red on each); a
  body is otherwise byte-identical (guard); GitHub's flattening facts still pass.

**S2 and S3 (templates, the pattern of PR #73's `PrReportLinkActionTests`):**
- YamlDotNet reads each template; every parameter is wired through `env:`; no script body contains `${{`
  or a `$(identifier)` (F9); the README's pipelines pass only declared parameters and every required one.
- Each template, converted to JSON, validates against a pinned copy of Microsoft's `service-schema.json`
  (draft-07, published with the VS Code extension, DOC [VSC]) with `JsonSchema.Net`, which the test project
  already references. The copy's size is to be measured before it is vendored.
- The reports script runs under `bash` against a temporary workspace with a fake Azure DevOps
  environment: two reports directories of this build and a stale one of another build. It stages exactly
  the two, `runs/` included, prints one `uploadsummary` per `CiSummary.md` and one `logissue` per failing
  run.
- The history script runs against a local bare repository standing in for `origin` (real `git`, no network,
  nothing mocked): the first run creates the orphan branch with its `.gitattributes`; the second appends;
  two clones folding at once both land (the loser is refused, rebases through `merge=union`, and pushes);
  a refusal that never clears gives up after the retries with a message; re-running with the same
  fragments commits nothing; every commit message carries `***NO_CI***`; the fetch works from a depth-1
  clone with no remote branches, as an Azure DevOps checkout leaves it.
- The drift fact: the script inlined in the Azure DevOps template (and in 2.3's action, once it exists) is
  byte-identical to `templates/scripts/kronikol-history-record.sh`.
- **Windows and macOS lanes for these facts.** CI runs Linux only (`ci.yml`, READ). GitHub's hosted
  runners are built from the same image definitions as Microsoft-hosted agents (`actions/runner-images`,
  DOC [RI]), so running the template facts on `windows-latest` and `macos-latest` covers Git Bash's paths
  and line endings and macOS's bash 3.2. Which bash an Azure DevOps `bash` step picks on Windows is the
  agent's choice, not the image's, and is S0's A20.

**S4:** a driver like PR #73's, with the statuses API held in memory: one status per lane, a newer
iteration's status left alone by an older build, `failed` when `Run.json` says so, nothing posted outside a
pull-request build.

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
are what every Azure DevOps user lacks today. S4 after S3 or beside it (it touches none of S3's files). S5
once S2 exists, growing as each slice lands.

### 7.2 Bumps

| Slice | Bump | Why |
|---|---|---|
| S1 | **patch** | Bug fixes. Behaviour a user can see changes, and the changelog says so: `runAttempt` is set on Azure DevOps; a re-run job's id is `ado:<build>:2`; a pull request of a GitHub repository built on Azure DevOps reads against `refs/heads/<target>`; the run link uses the collection URI; a failing CI run with `WriteCiSummary` off writes `CiSummary.md`; `##vso` and `##[` in run-derived console output read `**vso` and `**[`; an Azure DevOps artifact carries `attachments/`. No option, member or format field is added |
| S2 to S5 | none | Templates, scripts, docs and tests; no package contains them (§2). The changelog records them under `[Unreleased]`, as PR #73 does |

### 7.3 Where the code goes

| Slice | Files | Roadmap track |
|---|---|---|
| S1 | `src/Kronikol/Reports/CiMetadata.cs`, `CiSummaryWriter.cs`, `CiArtifactPublisher.cs`, `RunSummaryConsoleWriter.cs`, `ReportGenerator.cs` (the CI block, 615-685, and the schema description), `Reports/Merge/MergedRunOutputs.cs`, `History/HistoryRunBuilder.cs`, `src/Kronikol.Tool/Query/QueryWriter.cs`, the XML docs of §3.7 | A and B, lightly; no `VerbTable` or stylesheet change |
| S2 to S4 | `templates/azure-pipelines/`, `templates/scripts/`, `tests/Kronikol.Tests/Templates/`, a `windows-latest` and a `macos-latest` job in `ci.yml` for those tests | C |
| S5 | a pipeline file in the repository, run by an organisation outside it | C |

### 7.4 Docs

| Where | What | Slice |
|---|---|---|
| Wiki, new `Azure-DevOps.md` | the one page an Azure DevOps user needs: what works, the template steps, the permissions, the differences from GitHub and why | S2, grown by S3 and S4 |
| Wiki `CI-Artifact-Upload`, `CI-Summary-Integration` | the Azure DevOps sections rewritten: F1 stated, the template step, the corrected example | S1, S2 |
| Wiki `Cross-Run-History` | "On Azure DevOps" beside "The recommended shape"; the attempt | S1, S3 |
| Wiki `Querying-Reports`, `Report-Configuration`, `Generated-Reports`, `API-Reference` | the attempt, the posted summary's path, the run link | S1 |
| README, `nuget-readme.md` | the Azure DevOps sentences of the CI paragraphs | S1 |
| `templates/README.md`, `templates/azure-pipelines/README.md` | the templates | S2 to S4 |
| Both skill copies (`commands.md`) | an `ado:` example beside `gh:` | S1 |
| CHANGELOG | per slice | all |
| Kronikol4J divergence ledger | one capture-side entry for the attempt and the link, if its CI detector mirrors this one (D11 keeps ledgering capture changes; the port is not in this session, so not checked) | S1 |

### 7.5 S0: the half day on a real organisation

`AZURE_DEVOPS_PARITY_PLAN.harness/s0-probe.yml`, run as its header says: once, then "Rerun failed jobs",
then with Contribute granted, then as a pull-request build, then on `windows-latest`, then from a GitHub
repository. Each step is named for the ledger rows it settles; the output goes to `results-s0.txt`. What
it decides: A2 and A10 confirm §4.5's shape; A3 confirms F6's reach as the agent's source reads; A4
settles §4.2's duplicate rule and F7's last row; A5 confirms F3; A7 and A8 the artifact names; A11 to A14
the recipe's credential, permission and trigger text; A20 which bash Windows runs; A21 S4's permission.

### 7.6 Where it sits in the roadmap

- **1.14 (new), rule 1**: S1. A live defect on a documented feature, for every Azure DevOps user of it.
- **2.4**: S0, S2 to S5. Track C, beside anything. The row's "days" becomes about a week of work over four
  slices, plus the owner's half day for S0 and an organisation for S5.
- **One coupling.** 2.3 and S3 share one script: whichever lands first writes
  `templates/scripts/kronikol-history-record.sh` and the other inlines it. S4 shares nothing with 2.2,
  because it posts statuses rather than a comment, so 2.2 need not merge first.
- **For D12 and 11.1** (Q7): the Mermaid summary is a regression on Azure DevOps unless the summary tab
  renders it, and "same level" is a row of the bar.

### 7.7 Records

`PLANS_STATUS.md` gains this plan's row; `ROADMAP.md` 2.4 points here, row 1.14 and decision D24 are added,
and §7's coverage line names this plan. Written with the plan.

---

## 8. Not taken, and open questions

### 8.1 Designs not taken

- **The fetch and push in the tool** (`kronikol history push`). One implementation for every CI system,
  tested in .NET. It breaks `CROSS_RUN_HISTORY_PLAN.md` §14: the tool would make the call. The shared script
  gives one implementation without that.
- **A better stdout for the library**: the parent's console, or walking the process tree to `dotnet test`'s
  descriptor. Platform-specific and fragile, and the rule that binds it (the agent reads the step's own
  process) would still hold.
- **The library uploading through the REST API with `System.AccessToken`.** §14 again: token handling and
  provider API code in the library.
- **Running the template repository's scripts from files** by checking that repository out as well. A
  second `checkout` puts every repository, the consumer's own included, in a subfolder named for it (DOC
  [MR]), which breaks the consumer's paths.
- **PowerShell for the Azure DevOps side.** It runs on every hosted agent, and it would make 2.3 and S3
  two scripts, the maintenance the shared script exists to avoid. Self-hosted Windows agents need Git for
  Windows' bash on the path, which the README states.
- **A comment thread as S4's link.** It needs the machinery 2.2 carries, which statuses do not; it stays
  the surface for 7.4's diagrams.
- **A Marketplace extension now.** Tasks are a package with a release train of their own, and a listing is
  outreach (rule 9). It is the natural home for a run tab that shows the HTML report, which nothing on
  GitHub matches; proposed for 13.2's Azure DevOps side.

### 8.2 Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | The green light, and the order: S1 as roadmap 1.14 now, then S2, S3, S4 | Yes. F1 and F2 are live on documented features; the rest is the row as written |
| Q2 | Who runs S0, and in which organisation; does the project keep one for S5 | The owner's organisation, a scratch project. S5 wants a permanent one, or the templates are claims nobody runs; a private project's free grant needs the organisation linked to an Azure subscription (§3.8) |
| Q3 | Is S4 part of 2.4, or a row of its own | Part of 2.4, after S3. The bar's "pull request loop" row is per forge |
| Q4 | Distribution: copy-in, a repository resource pinned to a tag, or a Marketplace extension | Copy-in as the documented default, the resource as the alternative, the extension at 13.2 |
| Q5 | Azure DevOps Server: supported, or not obstructed | Not obstructed. The page says where it differs (build artifacts in place of pipeline artifacts, API 7.0) and nothing is run on Server |
| Q6 | When both the library and the template post the summary | S0's A4 decides the sentence (§4.2) |
| Q7 | The v4 Mermaid summary on Azure DevOps | Put it to D12: keep a PlantUML image section on Azure DevOps until the summary tab renders Mermaid, or accept code blocks there and say so |
| Q8 | The fold job, or a record step in each test job | The fold job, as on GitHub (DOC [GH]); the other only if S0's A10 contradicts the page |

---

## 9. Assumption ledger

| # | Statement | Basis | If wrong |
|---|---|---|---|
| A1 | `System.JobAttempt` is 1 on a job's first attempt and counts up on each retry; `System.StageAttempt` and `System.PhaseAttempt` likewise; each is an environment variable, upper-cased with `_` for `.` (`SYSTEM_JOBATTEMPT`) | DOC [VARS], [PVARS]; also listed for Server 2022 | F2's fix reads nothing; S0 A2 prints the real variables |
| A2 | "Rerun failed jobs" stays in the same run, under the same `Build.BuildId`, re-running failed jobs and their dependents | DOC [GH], [STG] (the stage retry is a call on the same build); no page says "same `Build.BuildId`" in those words | F2 would be smaller than stated; S0 A2 |
| A3 | The agent finds `##vso[` anywhere in a line, reads stderr too, splits on a bare CR, drops processed lines from the log; restricted mode still allows `task.complete` | DOC [CMD], [NH], [PI], [TCE], [TGT]; the "Logging commands" page says stdout and "a line that matches", which the source contradicts | F6 shrinks to lines that start with the prefix; S0 A3 |
| A4 | What the Extensions tab renders (`<details>`, external images, tables, Mermaid) and whether one path posted twice shows once | S0 only. DOC [LOG] says only that it differs from the wiki's rendering; issues #6223 and #6997 (2018) say collapse does not work | §4.2's duplicate rule; F7's last row |
| A5 | `System.PullRequest.TargetBranch` is `main` for a GitHub repository and `refs/heads/main` for Azure Repos; `Build.SourceBranch` is `refs/heads/main` on both | DOC [VARS] | F3 is not a defect; S0 A5 |
| A6 | The Azure Pipelines app's persisted token can push to the GitHub repository | DOC [GH] ("Write access to code"); per run, UNVERIFIED | §4.6 defaults to a secret variable |
| A7 | What several jobs uploading one container artifact name with `##vso[artifact.upload]` leaves | S0 only | F4's third clause |
| A8 | A pipeline artifact cannot be overwritten, so a re-run needs `$(System.JobId)` in the name; `PublishBuildArtifacts@1` takes a path, not a pattern | DOC [ART], [PBA] | the names in §4.2 |
| A9 | `##vso[task.logissue type=warning]` goes on the task's timeline record, ten per record, without changing the result | DOC [LOG], [EC]; [RUNS] says tasks that report warnings are "succeeded with issues" | F5's fix could turn a green job orange |
| A10 | A succeeded job that depends on a re-run job runs again | DOC [GH] ("any dependent downstream jobs"); S0 confirms | §4.5 moves the record step into each test job |
| A11 | `checkout` persists no credential by default; pipelines created since September 2022 fetch at depth 1 in some organisations; a shallow fetch brings the build commit alone | DOC [CO], [ARG], [GSP] | §4.5's read step could rely on the checkout |
| A12 | The two build identities, which the job authorization scope selects, and that neither can push by default | DOC [AT], [RN160], [GIT]; [AT] and [RN160] disagree on the default scope | §4.5's permission text |
| A13 | Contribute can be granted to the build identity on one branch | DOC [RPERM] (branch permissions); for the build identity, S0 A13 | the recipe grants it on the repository |
| A14 | A push by the build identity triggers CI; `***NO_CI***`, `[skip ci]` and their variants skip it; a pushed tag's message is not read | DOC [GIT], [ARG] | runs start on the data branch |
| A15 | The agent reads logging commands from the output of the step's process | DOC [NH] | F1 would have another way through |
| A16 | Azure DevOps has no file-based channel for logging commands | DOC [LOG] (none documented) | the library could write it as it does for GitHub, and S2 would shrink |
| A17 | Public projects are retired and become private in 2027; the free grant for private projects is one job and 1,800 minutes a month once linked to an Azure subscription | DOC [PPR], [CJ] (2026) | S5's cost; 9.4's note |
| A18 | A template in a GitHub repository needs a service connection; a template's repository contributes no scripts; a second `checkout` moves the sources into subfolders | DOC [RR], [TPL], [MR]; [TPL]'s own example omits the endpoint | distribution in §4.7 |
| A19 | `$(name)` in an inline script is replaced before the shell runs when the variable exists and left as written when it does not; `${{ }}` is replaced at compile time and empty when unknown | DOC [PVARS] | the macro test is a guard rather than a need |
| A20 | Microsoft-hosted images carry bash (3.2.57 on macOS); a `bash` step on Windows runs WSL's or Git's bash | DOC [RI], [BASH3], [XPLAT], which disagree; S0 A20 | the shared script needs a PowerShell twin for Windows |
| A21 | The build identity may post iteration statuses; threads need "Contribute to pull requests", not granted by default | DOC [PRIS], [PERM], [QA]; the statuses permission is UNVERIFIED | S4 needs a permission step in the recipe |
| A22 | `System.CollectionUri` is the organisation URI in builds; `System.TeamFoundationServerUri` is listed only for classic releases, as the Release Management URL | DOC [VARS], [RELV] | F11 is cosmetic |
| A23 | GitHub-hosted runners and Microsoft-hosted agents are built from the same image definitions | DOC [RI] | the Windows and macOS lanes of §6 prove less |

---

## Appendix A. The recipe, drafted

The shape S3 documents for an Azure Repos repository. Every line is a DOC claim until S0 runs it.

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
    - template: templates/azure-pipelines/kronikol-reports.yml
      parameters:
        searchRoot: $(project)

- stage: history
  dependsOn: test
  condition: and(not(canceled()), ne(variables['Build.Reason'], 'PullRequest'))
  jobs:
  - job: record
    steps:
    - checkout: self
      fetchDepth: 1
    - task: UseDotNet@2
      inputs: { packageType: sdk, version: 10.0.x }
    - task: DownloadPipelineArtifact@2
      inputs:
        itemPattern: '**/History.run.json'
        path: $(Agent.TempDirectory)/fragments
    - template: templates/azure-pipelines/kronikol-history-record.yml
      parameters:
        fragments: $(Agent.TempDirectory)/fragments
```

What the record script does, in the order GitHub's fold step does it, with the differences marked. The
wrapper sets `KRONIKOL_GIT_AUTH` to `AUTHORIZATION: bearer <System.AccessToken>` on Azure Repos, mapped into
this step alone, and installs the tool at the release the template names.

```bash
set -euo pipefail
git config user.name "Kronikol history"
git config user.email "kronikol-history@noreply.invalid"
auth=(-c "http.extraheader=$KRONIKOL_GIT_AUTH")        # Azure DevOps: passed per command, never persisted
ref=+refs/heads/kronikol-history:refs/remotes/origin/kronikol-history   # a shallow checkout has no remote branches
wt="$KRONIKOL_TEMP/history-branch"
if git "${auth[@]}" fetch origin "$ref"; then
  git worktree add -B kronikol-history "$wt" origin/kronikol-history
else
  git worktree add --detach "$wt"
  git -C "$wt" checkout -q --orphan kronikol-history
  git -C "$wt" rm -rfq .
  printf '%s\n' "history.jsonl merge=union text eol=lf" > "$wt/.gitattributes"
fi
kronikol history record "$KRONIKOL_FRAGMENTS" --history "$wt/history.jsonl"
cd "$wt"
git add history.jsonl .gitattributes
if git diff --cached --quiet; then echo "nothing new to record"; exit 0; fi
git commit -q -m "Record $KRONIKOL_RUN_LABEL ***NO_CI***"   # Azure DevOps: the build identity's push triggers CI
for attempt in 1 2 3 4 5; do
  if git "${auth[@]}" push origin HEAD:refs/heads/kronikol-history; then exit 0; fi
  echo "push $attempt lost the race; rebasing onto the branch as it is now"
  git "${auth[@]}" fetch origin "$ref"
  git rebase origin/kronikol-history
done
echo "could not push the ledger after 5 attempts"; exit 1
```

## Appendix B. Sources

Read on 2026-09-27. Microsoft Learn pages under `https://learn.microsoft.com/en-us/azure/devops/` unless
the key says otherwise.

| Key | Page |
|---|---|
| [AT] | `pipelines/process/access-tokens` (job access tokens, the two build identities, the scope setting) |
| [ARG] | `pipelines/repos/azure-repos-git` (CI triggers, skip markers, shallow fetch, the extraheader form) |
| [ART] | `pipelines/artifacts/artifacts-overview` (pipeline artifacts cannot be overwritten; `$(System.JobId)`) |
| [BASH3] | `pipelines/tasks/reference/bash-v3` |
| [CJ] | `pipelines/licensing/concurrent-jobs` (the 2026 free grant) |
| [CO] | `pipelines/yaml-schema/steps-checkout` (`persistCredentials`) |
| [DPA] | `pipelines/tasks/reference/download-pipeline-artifact-v2` |
| [GH] | `pipelines/repos/github` (the app's permissions, re-runs, forks) |
| [GHC] | `pipelines/tasks/reference/github-comment-v0` |
| [GIT] | `pipelines/scripts/git-commands` (the build identity cannot push by default; skipping CI) |
| [LOG] | `pipelines/scripts/logging-commands` |
| [MR] | `pipelines/repos/multi-repo-checkout` |
| [PBA] | `pipelines/tasks/reference/publish-build-artifacts-v1` ("Wildcards are not supported") |
| [PERM] | `organizations/security/permissions` (Contribute, Create branch, Bypass policies, Contribute to pull requests) |
| [PPA] | `pipelines/tasks/reference/publish-pipeline-artifact-v1` (Services only) |
| [PPR] | `organizations/projects/public-projects-retirement` |
| [PRIS] | REST: `https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-iteration-statuses/create?view=azure-devops-rest-7.1` |
| [PRSD] | `repos/git/pull-request-status` |
| [PRV] | REST: `https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/preview/preview?view=azure-devops-rest-7.1` |
| [PVARS] | `pipelines/process/variables` (macro, template and runtime syntax; environment names) |
| [QA] | `https://learn.microsoft.com/en-us/answers/questions/5770166/permissions-error-with-pipeline` (TF401027) |
| [RELV] | `pipelines/release/variables` |
| [RET] | `pipelines/policies/retention` |
| [RI] | `https://github.com/actions/runner-images` (Windows 2025, Ubuntu 24.04, macOS 15 image readmes) |
| [RN160] | `release-notes/2019/sprint-160-update` |
| [RN274] | `release-notes/2026/wiki/sprint-274-update` (Mermaid in pull-request comments) |
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
| Agent source | `https://github.com/microsoft/azure-pipelines-agent/blob/master/src/`: [CMD] `Microsoft.VisualStudio.Services.Agent/Command.cs`; [NH] `Agent.Worker/Handlers/NodeHandler.cs`; [PI] `Agent.Sdk/ProcessInvoker.cs`; [TCE] `Agent.Worker/TaskCommandExtension.cs`; [EC] `Agent.Worker/ExecutionContext.cs`; [JSQ] `Microsoft.VisualStudio.Services.Agent/JobServerQueue.cs`; [GSP] `Agent.Plugins/GitSourceProvider.cs`; `Agent.Sdk/Util/StringUtil.cs` |
| Marketplace | the three HTML-tab extensions: `JakubRumpca.azure-pipelines-html-report`, `LakshayKaushik.PublishHTMLReports`, `blakyaks.azure-pipelines-html-reports` (`https://marketplace.visualstudio.com/items?itemName=…`) |
