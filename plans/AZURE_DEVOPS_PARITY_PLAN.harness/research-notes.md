# Research notes behind AZURE_DEVOPS_PARITY_PLAN.md

The evidence the plan was written from, kept so executing a slice does not mean redoing the reading.
Gathered 2026-09-27 at `e4c9e36` (3.31.9). Three parts: what Microsoft's pages and the agent's source say
(the plan's DOC rows), where every provider-specific path lives in this repository (READ), and where the
other plans already speak about Azure DevOps (PLAN). Keys are the plan's Appendix B keys. A quote is
verbatim from the page as published markdown; **UNVERIFIED** means neither the docs nor the source settled
it; **FLAG** marks a place where Microsoft's own pages disagree or the answer depends on a date.

## 1. Azure Pipelines, from Microsoft's pages and the agent's source

### 1.1 The build identity and pushing

- Two identities: "`Project Collection Build Service ({OrgName})`" and "`{Project Name} Build Service ({Org
  Name})`" [AT]. "If **Limit job authorization scope to current project for non-release pipelines** is
  enabled, then the scope is **project** … Otherwise, the scope is **collection**" [AT]. An
  organisation-level project scope cannot be changed per project [AT]; a public project is always project
  scope [AT].
- **FLAG, the default scope:** new projects and organisations have had the project scope since December
  2019 ("every new project and organization that you create will automatically have this setting turned
  on", [RN160]), while [AT] still says "By default, the collection-scoped identity is used, unless
  configured otherwise".
- "A new access token is generated for each job, and it expires once the job completes" [RN160].
- "Protect access to repositories in YAML pipelines" is on by default for organisations and projects
  created after May 2020 [ARG]; it limits a job to repositories named by a `checkout` step or a `uses`
  statement; "doesn't apply to GitHub repositories" [SEC]; a pipeline asks permission for a repository the
  first time and someone selects **Permit** [SEC].
- Repository permissions [PERM]: **Contribute** "can push their changes to existing branches … Doesn't
  override restrictions in place from branch policies"; **Create branch** "Can create and publish branches
  in the repository"; **Bypass policies when pushing** "Can push to a branch that has branch policies
  enabled"; **Contribute to pull requests** "Can create, comment on, and vote on pull requests". Bypass and
  Force push are "not set for any security group" by default [RPERM].
- "By default, this identity can read from the repo but can't push any changes to it"; grant "typically
  **Create branch**, **Contribute**, **Read**, and **Create tag**" [GIT]. **FLAG:** [GIT] names only the
  project-scoped identity.
- A branch's creator gets Contribute, Edit Policies, Force Push, Manage Permissions and Remove Others' Locks
  on it [PERM]; whether that holds for the build identity is UNVERIFIED; since sprint 224 creators no longer
  get Edit policies [RPERM].
- `persistCredentials`: "Set to 'true' to leave the OAuth token in the Git config after the initial fetch.
  The default is not to leave it" [CO]. The agent writes `http.{repositoryUrl}.extraheader = "AUTHORIZATION:
  <bearer|basic> <token>"` into the repository's config [GSP]; a `git worktree` shares it (git's rule).
  Without persisting: `git clone -c http.extraheader="AUTHORIZATION: bearer $(System.AccessToken)" <url>`
  [ARG]. In YAML, `System.AccessToken` must be mapped explicitly [VARS].
- A push by the build identity triggers CI: [GIT]'s FAQ "How can I avoid triggering a CI build when the
  script pushes?" answers "add `[skip ci]`". Skip markers [ARG], [GIT]: `[skip ci]`, `[ci skip]`,
  `skip-checks: true`, `skip-checks:true`, `[skip azurepipelines]`, `[azurepipelines skip]`,
  `[skip azpipelines]`, `[azpipelines skip]`, `[skip azp]`, `[azp skip]`, `***NO_CI***`. "The tag's message
  isn't evaluated" [ARG].
- "For CI triggers, the YAML file that is in the branch you are pushing is evaluated to see if a CI build
  should be run" [ARG]: a branch without the pipeline's YAML should not trigger that pipeline (an inference;
  no page says it outright).
- Shallow fetch: "In some organizations, new pipelines created after the September 2022 Azure DevOps sprint
  209 update have **Shallow fetch** enabled by default and configured with a depth of 1"; `fetchDepth: 0`
  fetches all history; tag sync defaults to `false` for those pipelines [ARG]. With a depth the agent fetches
  only `+{sourceVersion}:refs/remotes/origin/{sha}` [GSP], so `origin/<other branch>` does not exist locally.

### 1.2 Re-runs, attempts, ids

- "The resultant run will have the same run number … Only those jobs that failed in the initial run and any
  dependent downstream jobs will be run again … This is the same behavior as clicking 'Retry run'" [GH].
- The stage retry is `PATCH …/builds/{buildId}/stages/{stageRefName}` with `forceRetryAllJobs` [STG], a call
  on the same build. No page says "same `Build.BuildId`" in those words.
- `System.JobAttempt`: "Set to 1 the first time this job is attempted, and increments every time the job is
  retried"; `System.StageAttempt` the same per stage; `System.PhaseAttempt` "increments every time the job is
  retried"; `System.JobId`: "A unique identifier for a single attempt of a single job" [VARS]. No run-level
  attempt variable is documented; the Timeline REST API has per-record `attempt` and `previousAttempts`
  [TL]. `System.JobAttempt` and `TF_BUILD` are also in the Server 2022 tables [VARS].
- Environment names: "variable names become uppercase, and periods turn into underscores" [PVARS]
  (`SYSTEM_JOBATTEMPT`); secret variables are not mapped [PVARS].
- `Build.BuildId`: "An internal, immutable ID, also called the `Run ID`, that's unique in the Azure DevOps
  organization" [RUNNO]. The build number is only a name (customisable, `##vso[build.updatebuildnumber]`
  [LOG]; default `$(Date:yyyyMMdd).$(Rev:r)` [RUNNO]).
- "Pipeline artifacts cannot be deleted or overwritten. To regenerate artifacts when rerunning a failed job,
  include the job ID in the artifact name using the variable `$(System.JobId)`" [ART].

### 1.3 Pull-request variables

- `System.PullRequest.TargetBranch`: "`refs/heads/main` when your repository is in Azure Repos and `main`
  when your repository is in GitHub" [VARS]. `System.PullRequest.targetBranchName` exists in current docs,
  including the Server 2022 table; since when is UNVERIFIED.
- `System.PullRequest.PullRequestId`: "initialized only if the build ran because of a Git PR affected by a
  branch policy"; `PullRequestNumber` is "populated for pull requests from GitHub that have a different pull
  request ID and pull request number"; `IsFork` is "set to `True`" for forks [VARS].
- `Build.Reason` `PullRequest`: "A Git branch policy that requires a build triggers the build" [VARS].
- `Build.SourceBranch` on a PR build: `refs/pull/1/merge`; `Build.SourceBranchName` is "The last path segment
  in the ref" [VARS].
- `Build.Repository.Provider`: the docs list `TfsGit`, `TfsVersionControl`, `Git`, `GitHub`, `Svn`; the
  agent's source also uses `GitHubEnterprise` and `Bitbucket` (**FLAG**).
- `System.CollectionUri` and `System.TeamFoundationCollectionUri`: "The URI of the Azure DevOps organization
  or collection. For example: `https://dev.azure.com/fabrikamfiber/`" [VARS]. `System.TeamFoundationServerUri`
  appears only in the classic release variables: "The URL of the service connection … Example:
  `https://fabrikam.vsrm.visualstudio.com/`" [RELV]. `System.TeamProject` is "The name of the project".
- Forks: secrets, service connections, secure files and secret variables are withheld from pull-request
  builds of forks [GH]; Azure Repos forks are "no different" [ARG]. **FLAG:** [GH] says new organisations
  default to "Disable building pull requests from forked repositories" (sprint 229) and, on the same page,
  that "Starting September 2023, new organizations have **Securely build pull requests from forked
  repositories** turned on by default".

### 1.4 GitHub repositories built by Azure Pipelines

- The GitHub app runs as "The Azure Pipelines identity"; OAuth and PAT connections run as "Your personal
  GitHub identity" (a PAT needs `repo`, `admin:repo_hook`, `read:user`, `user:email`) [GH].
- The app's permissions [GH]: "Write access to code", "Read access to metadata", "Read and write access to
  checks", "Read and write access to pull requests". Commit statuses and issues are not listed. Whether its
  token can push in a given run, and its lifetime (reported as about an hour, agent issue #4713), are
  UNVERIFIED.
- "Azure Pipelines ensures the pipeline can't change any GitHub repository content. This restriction applies
  *only* if you use the Azure Pipelines GitHub app" — said of fork pull requests [MISC].
- `GitHubComment@0` writes "a comment to your GitHub entity … Pull Request"; its connection "must be based on
  a GitHub user's OAuth or a GitHub personal access token"; with `id` empty "in a PR pipeline … dynamically
  figure out the ID" [GHC].

### 1.5 Pull requests on Azure Repos

- Threads: `POST https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads?api-version=7.1`
  with `comments`, `status` and `properties` ("Optional properties associated with the thread as a
  collection of key-value pairs", a marker to find one's own thread) [THC]; list with `GET …/threads` [THL];
  update a comment with `PATCH …/threads/{threadId}/comments/{commentId}` [TCU]. Scopes `vso.code_write` or
  `vso.threads_full`.
- Authentication from a script: `Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN"`, with
  `SYSTEM_ACCESSTOKEN: $(System.AccessToken)` mapped in `env` [PS].
- The build identity lacks "Contribute to pull requests" by default; the refusal is "TF401027: You need the
  Git 'PullRequestContribute' permission" [QA] (a Q&A answer, not a doc).
- Statuses: `POST …/pullRequests/{id}/statuses` with `context`, `state`, `description`, `targetUrl` [PRS];
  "When a `targetUrl` is applied, the description will be rendered as a link", and "only the latest of which
  is shown for each unique `context`" [PRSD]. Per iteration: `POST …/pullRequests/{pullRequestId}/iterations/{iterationId}/statuses?api-version=7.1`,
  "the same result as Create status on pull request with specified iteration ID in the request body"; the
  only required field is `context.name`; states `notSet`, `pending`, `succeeded`, `failed`, `error`,
  `notApplicable`; scopes `vso.code_write`, `vso.code_status` [PRIS]. Which repository permission the build
  identity needs to post one is UNVERIFIED.
- Mermaid: fenced ```` ```mermaid ```` became supported in "wiki pages, pull requests, and work items" in May
  2026 [RN274].

### 1.6 Logging commands, summaries, artifacts

- The agent finds a command anywhere in a line: `int prefixIndex = message.IndexOf(LoggingCommandPrefix,
  StringComparison.Ordinal);` with `LoggingCommandPrefix = "##vso["`, case-sensitive [CMD]. A processed line
  is not written to the log: `if (!CommandManager.TryProcessCommand(...)) { ExecutionContext.Output(e.Data); }`
  [NH]. It reads stderr as well as stdout (`StepHost.OutputDataReceived += OnDataReceived;
  StepHost.ErrorDataReceived += OnDataReceived;`, [NH]), which [LOG]'s "only processed when written to
  stdout" contradicts (**FLAG**). Lines come from `reader.ReadLine()` [PI], so a bare `\r` ends a line.
- The agent's own neutraliser: `Regex.Replace(input, "##vso", "**vso", RegexOptions.IgnoreCase)`
  (`Agent.Sdk/Util/StringUtil.cs`). The `##[section]`, `##[warning]`, `##[error]`, `##[group]` formatting
  commands are rendered by the web log viewer; whether they must start a line is UNVERIFIED (the agent's
  logger uses `message.Contains("##[group]")`).
- Restricted mode: `target: { commands: restricted, settableVariables: none | [...] }` [TGT], [SECT]; an empty
  `settableVariables` disallows "all variable setting"; artifacts and attachments are blocked. Still allowed
  in restricted mode [TCE]: `logdetail`, `logissue`, `complete`, `setprogress`, `setsecret`, `setvariable`,
  `debug`, `settaskvariable`, `prependpath` (the variable and path ones gated by `settableVariables`). An
  injected `##vso[task.complete result=Succeeded;done=true]` force-completes the step ("Treating the task as
  complete", [NH]); the result merge only ever worsens a result.
- `##vso[task.uploadsummary]`: "The summary appears on the **Extensions** tab of your pipeline run"; "Markdown
  rendering on the Extensions tab is different from Azure DevOps wiki rendering" [LOG]. Which HTML tags,
  external images or Mermaid render there, and any size limit: UNVERIFIED (2018 issues #6223 and #6997 in
  `azure-pipelines-tasks` say JavaScript, `<style>` and collapsing do not work). The agent checks the file
  exists when it reads the command and uploads it later from a queue [EC], [JSQ]: the file must stay until
  the job ends.
- `##vso[task.logissue type=error|warning;sourcepath=…;linenumber=…]`: "Log an error or warning message in
  the timeline record of the current task" [LOG]; no notice type; the agent keeps the first 10 errors and 10
  warnings per record [EC]; `AddIssue` does not set the result [EC]. **FLAG:** [RUNS] says steps report
  errors and warnings "by marking the tasks as succeeded with issues".
- `##vso[task.complete result=Succeeded|SucceededWithIssues|Failed]`; `##vso[build.addbuildtag]` (no colon);
  `##vso[task.setvariable variable=X]v` "exposed to the following tasks as an environment variable", the
  `GITHUB_ENV` equivalent, with `isOutput=true` and a step `name:` for `dependencies.A.outputs['step.X']`
  and `stageDependencies` [LOG], [SVS]; `task.prependpath` is `GITHUB_PATH`; `task.setsecret` is `add-mask`.
- `##vso[task.addattachment]`: "These files aren't available for download with logs. These can only be
  referred to by extensions" [LOG].
- `##vso[artifact.upload]`: "Upload a local file into a file container folder" [LOG]; not deprecated, but
  "We recommend using … Publish Pipeline Artifacts for faster performance" [BA]; pipeline artifacts are
  "Recommended for faster performance in DevOps Services" [ART].
- `PublishBuildArtifacts@1` `PathtoPublish`: "Wildcards are not supported" [PBA].
- `PublishPipelineArtifact@1` and the `publish` step: "This task is supported on Azure DevOps Services only …
  use Build Artifact Task instead" [PPA], [PUB].
- Retention: deleting a run deletes its "Logs, All pipeline and build artifacts … Test results" [RET]; there
  are "no longer … per-pipeline retention rules" [RET]; default day counts UNVERIFIED.
- A run's page: `https://dev.azure.com/fabrikam-inc/FabrikamFiber/_build/results?buildId=1088&view=results`
  [DPA]; `&view=artifacts&type=publishedArtifacts` UNVERIFIED; `GET …/_apis/build/builds/{buildId}/artifacts?artifactName=…`
  returns a `downloadUrl` [RA]. Viewing an HTML artifact in the browser: reportedly download only.
- `PublishTestResults@2` formats: `JUnit`, `NUnit`, `VSTest`, `XUnit`, `CTest`; no CTRF [PTR].
- HTML tabs from the Marketplace (none Microsoft's): `JakubRumpca.azure-pipelines-html-report`
  (`PublishHtmlReport@1`, about 5k installs, 2021), `LakshayKaushik.PublishHTMLReports` (about 6.8k, 2021),
  `blakyaks.azure-pipelines-html-reports` (2025). Installing needs Project Collection Administrators [INST].
- The wiki renders Markdown only, "Markdown in Azure DevOps doesn't support JavaScript or iframes" [MD].

### 1.7 Templates, macros, testing YAML

- `resources.repositories` plus `- template: file.yml@alias`, pinned by `ref: refs/tags/v1.0` or a 40-character
  SHA; "If no `ref` is specified, the pipeline defaults to using `refs/heads/main`" [TPL]. "GitHub repos require
  a GitHub service connection for authorization" [RR]. **FLAG:** [TPL]'s example omits `endpoint:`; whether a
  public repository works without one is UNVERIFIED (assume not). **FLAG:** [TE] says expressions are not
  allowed in `repositories`; [RR] says "Template expressions are supported for the `ref` property".
- "Only the template files are used … you can't use scripts from the template repo" [TPL]; the alternative is
  a `- checkout: <alias>`, which puts every repository in a subfolder named for it [MR].
- Limits: "No more than 100 separate YAML files", "100 levels of template nesting", "20 megabytes of memory
  consumed while parsing" [TPL].
- Macros: "When the system encounters a macro expression, it replaces the expression with the contents of the
  variable. If there's no variable by that name, the macro expression doesn't change"; "The system only
  expands macro syntax variables for **task inputs**" (an inline script is one); template variables "silently
  coalesce to empty strings when a replacement value isn't found" [PVARS].
- Preview: `POST …/_apis/pipelines/{pipelineId}/preview?api-version=7.1` "Queues a dry run … returns … the final
  yaml"; `previewRun` returns "the final YAML document after parsing templates"; `yamlOverride` previews
  uncommitted YAML; needs an existing pipeline; also in server-rest-7.0 [PRV].
- Schema: `service-schema.json` in `microsoft/azure-pipelines-vscode` (draft-07, `"$comment": "v1.261.1"`); an
  organisation's own at `https://dev.azure.com/<org>/_apis/distributedtask/yamlschema` [VSC]. No official
  local runner was found (UNVERIFIED that none exists).
- Marketplace publishing: "Anyone can create a publisher" [EXT]; `tfx extension publish --publisher …
  --share-with …`, authenticated by an Entra token or a PAT with **Marketplace (publish)** [EXTCLI].

### 1.8 Agents, images, free grant, Server

- `windows-latest` is Windows Server 2025, `ubuntu-latest` Ubuntu 24.04 ("the default image for YAML
  pipelines"), `macOS-latest` macOS 15 [HOST]. `script:` "runs a script using cmd.exe on Windows and Bash on
  other platforms". **FLAG, `bash` on Windows:** [BASH3] "On a Windows host this runs bash from the WSL default
  distribution" and "The Bash task will find the first Bash implementation on your system"; [XPLAT] "Windows
  agents can use Git Bash or … WSL Bash"; the Windows 2025 image has both `gitbash.exe` and `wslbash.exe`
  [RI]. `pwsh:` runs PowerShell 7.x, installed on every Microsoft-hosted agent [XPLAT].
- The September 2026 images [RI]: .NET SDK 8.0, 9.0 and 10.0 (10.0.401), Git 2.55, PowerShell 7.6; **macOS
  ships Bash 3.2.57**. `UseDotNet@2` pins the SDK or uses `useGlobalJson`. Git's `user.name`/`user.email`: "If
  necessary, set the Git user as the first step after checkout" [GIT].
- Public projects: "are retired, and new public projects can no longer be created. In 2027, all existing
  public projects will automatically convert to private" (announced April 2026) [PPR]. Private projects' free
  grant: "one free job … up to 60 minutes each time … 1,800 minutes (30 hours)", after you "link your Azure
  DevOps organization to a valid Azure subscription" (June 2026) [CJ], replacing the 2021 request form
  (devblogs, 2021-03-16); a January 2026 Q&A answer still cites the form.
- Azure DevOps Server 2022: `TF_BUILD` and `System.JobAttempt` exist [VARS]; templates from other repositories
  and `type: github` resources apply [TPL], [RR]; pipeline artifacts are not supported [PPA], [PUB]. REST: Server
  2022 is 7.0, 2022.1 is 7.1, vNext 7.2 [REST]. **FLAG:** a versionless "Azure DevOps Server" shipped
  2025-12-09 [SRVRN]; its pipeline-artifact support reads inconsistently across doc monikers.

Extra keys used above and not in the plan's Appendix B (all under
`https://learn.microsoft.com/en-us/azure/devops/` unless marked): [SEC] `pipelines/security/secure-access-to-repos`,
[MISC] `pipelines/security/misc`, [RUNNO] `pipelines/process/run-number`, [TL] REST `build/timeline/get`,
[THC], [THL], [TCU] REST `git/pull-request-threads/create`, `…/list`, `git/pull-request-thread-comments/update`,
[PRS] REST `git/pull-request-statuses/create`, [PS] `pipelines/scripts/powershell`, [MD]
`project/wiki/markdown-guidance`, [BA] `pipelines/artifacts/build-artifacts`, [PUB]
`pipelines/yaml-schema/steps-publish`, [RA] REST `build/artifacts/get-artifact`, [PTR]
`pipelines/tasks/reference/publish-test-results-v2`, [INST] `marketplace/install-extension`, [TE]
`pipelines/process/template-expressions`, [EXT] `extend/publish/overview`, [EXTCLI]
`extend/publish/command-line`, [HOST] `pipelines/agents/hosted`, [REST] REST API index, [SRVRN]
`server/release-notes/azuredevopsserver`, [SVS] `pipelines/process/set-variables-scripts`. REST pages are
under `https://learn.microsoft.com/en-us/rest/api/azure/devops/` with `?view=azure-devops-rest-7.1`.

## 2. This repository, provider by provider (READ)

- Detection: `src/Kronikol/Reports/CiEnvironment.cs:27-31`, `GITHUB_ACTIONS` first, then `TF_BUILD`.
- Metadata: `CiMetadata.cs:34-55` (GitHub: `GITHUB_RUN_ID`, `GITHUB_SERVER_URL`, `GITHUB_REPOSITORY`,
  `GITHUB_RUN_NUMBER`, `GITHUB_REF_NAME`, `GITHUB_SHA`, `GITHUB_RUN_ATTEMPT`), `:57-77` (Azure DevOps:
  `BUILD_BUILDID`, `SYSTEM_TEAMFOUNDATIONSERVERURI`, `SYSTEM_TEAMPROJECT`, `BUILD_BUILDNUMBER`,
  `BUILD_SOURCEBRANCH`, `BUILD_SOURCEVERSION`, `BUILD_REPOSITORY_NAME`; the link at `:63-65`; the "no
  equivalent variable" comment at `:75-76`), `PullRequestTarget` at `:80-96`.
- Summary: `CiSummaryWriter.cs:21-33` (Azure DevOps: temp file `ci-summary-{guid}.md` in `Path.GetTempPath()`,
  then `##vso[task.uploadsummary]` at `:32`). Artifacts: `CiArtifactPublisher.cs:40-74` (`:46` and `:55`
  print the uploads; `retentionDays` unused on Azure DevOps). Notice: `RunSummaryConsoleWriter.cs:193`
  builds it, `:229` emits it on GitHub only; `:274` documents the `::error::` injection it guards against.
- Run id: `src/Kronikol/History/HistoryRunBuilder.cs:170-183` (`ado:{RunId ?? BuildNumber}:1` at `:178`, doc
  at `:163`); `HistoryModel.cs:168`; `HistoryRunId.DirectoryName` (`HistoryRunId.cs:52-81`) makes `ado_<id>_1`;
  `RunRotation.cs:229`, `:384-393` (the `-2` suffix), `:82-96` (`KeepRuns`: option, `KRONIKOL_KEEP_RUNS`, 3 off
  CI, 0 on CI), `:217-220` and `:541-544` (same-id runs kept on CI).
- The report's CI block: `ReportGenerator.cs:327` detects metadata, `:350-351` picks the run id, `:615-632`
  writes and posts the summary, `:649-662` publishes artifacts (top level, six extensions), `:672-681` the
  debug section, `:684-685` the pointer; `:1526-1545` the HTML CI table; `:5984-5990` the schema
  descriptions (`runAttempt` "Null off GitHub Actions").
- History context: `HistoryRunContext.cs:88-89` (the option overrides the id), `:141-142` (the PR target is
  the stream), `:166` (`WriteHistoryLedger` default), `:205-237` (a same-id line is amended).
- Merge: `MergedRunOutputs.cs:126` detects, `:128-151` posts the summary or debug section, `:153-161`
  publishes with no kept runs; `MergeCommand.cs:49-54` (`--ci-summary`, `--publish-artifacts`), `:191-192`,
  `:209`, help at `:375-380`.
- Tool: `QueryCommand.History.cs:593-617` rebuilds a run id from a report (`CiOf` at `:708-713`, PR target at
  `:844`); `HistoryCommand.Maintenance.cs:102` (the gate's branch); `IngestCommand.cs:359` (pointer and
  notice); `QueryCommand.Overview.cs:63-75` (the `run:` line prints the attempt only when not `1`);
  `QueryWriter.cs:140-160` (`Line` flattens, `Payload` does not); `CtrfReportGenerator.cs:98-112`,
  `CtrfCommand.cs:158-177`. `HistoryLedgerWriter.cs:233-235` returns `Duplicate` for an existing id and suite.
  The tool starts no process (no `ProcessStartInfo` in `src/Kronikol.Tool`).
- Options (`ReportConfigurationOptions.cs`): `WriteRunSummaryToConsole` 272 (remarks 265-270 hold the
  swallowing measurement), `GenerateCtrfReport` 302, `WriteCiSummary` 316, `WriteCiDebugSection` 342,
  `MaxCiSummaryDiagrams` 345, `PublishCiArtifacts` 348 (its doc says "(GitHub Actions)"), `CiArtifactName`
  351, `CiArtifactRetentionDays` 354, `HistoryFilePath` 563, `HistoryRunId` 568-573, `HistoryBranch` 710,
  `HistoryCompareBranch` 717, `GenerateHistoryFragment` 724, `WriteHistoryLedger` 733, `KeepRuns` 771.
- Tests that exercise Azure DevOps (all `tests/Kronikol.Tests`): `CiEnvironmentDetectorTests` (16, 32),
  `CiMetadataDetectorTests` (`Azure_devops_reports_no_attempt_rather_than_a_guess` 67, metadata 80, PR target
  107-118), `CiSummaryWriterTests` (25, 77), `CiArtifactPublisherTests` (8, 28, 63, 139), `RunEndPointerTests`
  (153, no notice on Azure DevOps), `HistoryRunBuilderTests` (165-169, `ado:9001:1`), `RunManifestTests` (28-30).
  GitHub-only, with no Azure DevOps twin: `CiDebugSectionTests` (all six), `MergeWritesTheRunOutputsTests`
  (the `GitHub()` helper at 242), `RunRotationTests` (the `GitHub(runId, attempt)` helper at 68-69),
  `RunIdentityDataTests`, `CiMetadataReportTests`, `CtrfReportGeneratorTests` (the run-identity fact),
  `RunEndPointerSafetyTests`, `HistoryGateTests` and `HistoryOutputsTests` (PR target), `HistoryCommandTests`
  and `RetainedRunsTests` (`gh:` ids), `QueryCommandTests` (`Summary_says_which_run_the_file_is_when_it_was_built_on_ci`),
  `MergeableReportTests` (the round trip), `tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs:2492/2702/2772`.
- Workflows: `ci.yml` has no history, summary or artifact upload, and runs on `ubuntu-latest` only;
  `ci-summary-preview.yml` holds the dogfooded history recipe (43-57 read, 62-70 upload, 72-148 the fold job,
  98-141 the fold step), 82 lines of history YAML in all. No `azure-pipelines*.yml` in the repository.
  `templates/Kronikol.Templates.csproj` packs twelve named template folders only.
- The CI-preview examples set `WriteCiSummary` (`AllPassing/Infrastructure/TestRun.cs:26`, `AllFailing` and
  `Mixed` `TestRun.cs:23`, `FailingWithSteps/…/ConfiguredLightBddScope.cs:27`). Nine example projects reference
  `GitHubActionsTestLogger` 2.4.1; no workflow passes `--logger`.
- Wiki pages that mention Azure DevOps: `CI-Artifact-Upload`, `CI-Summary-Integration`, `Cross-Run-History`
  (86, 158, 590), `Merging-Parallel-Reports` (62-63, 218: the `DownloadPipelineArtifact@2 latestFromBranch`
  baseline recipe), `Generated-Reports` (450, 724, 800, 997), `Querying-Reports` (361, 880),
  `Report-Configuration` (163, 192, 202), `API-Reference` (87-91), `PlantUML-Browser-Rendering` (322).

## 3. What the other plans already say (PLAN)

- `HISTORY_DASHBOARD_STORE_PLAN.md`: the three tiers at 888-935 (Tier 0 "Copy about 45 lines of YAML" at
  898-900; the three-line action at 923-930; "Recommended ahead of the alerting follow-up" at 932-935); forge
  portability at 937-993 (the owner's requirement at 939-941; the four Azure DevOps requirements at 970-981;
  every Azure platform claim REFERENCE at 989-993); §6.3 at 1091-1145 ("A .NET shop disproportionately runs
  Azure DevOps, on Azure, with Entra ID" at 1098-1100; "Azure plumbing outranks a second language" at 1138;
  "Half a day confirming them comes before any of the four items is planned" at 1142-1145); §8.0 at
  1189-1223 (P1: on Azure DevOps the workflow copies the files beside the page, 1198); §11 Q2 at 1505-1511 (says
  the vendor half day was done, citing R19), Q7 at 1524-1529 (the action before alerting), Q10 at 1542-1546,
  Q17 at 1572-1573 ("per-provider shell that Kronikol cannot test, or an opt-in `Kronikol.Extensions.Store.*`
  package"); R7 at 1942-1945 (one job folds; the rebase retry is for concurrent workflow runs); R16 at 2087;
  R19 at 2120-2136 (checked Blob, Static Web Apps, App Service Auth, Entra, the S3 API, and none of the four
  Pipelines claims).
- `CROSS_RUN_HISTORY_PLAN.md`: `<provider>:<runId>:<attempt>` at 804-805; "`DetectAzureDevOps` | ADO's
  equivalent — **variable name not verified here**" at 823; the attempt's purpose at 842-845; the orphan
  branch at 1225-1240 ("The plumbing is `git worktree`/`commit`/`push` in a workflow, so §14's 'Kronikol
  opens no socket' is untouched"); `merge=union` at 1050-1091; `DownloadPipelineArtifact` `latestFromBranch`
  at 1268-1274; Azure DevOps' own flaky management at 105-111, 152-153, 1619-1620, 2248-2249 (Q10: do not
  consume it in v1), 2417-2421; §14, the no-socket contract, at 2087-2110.
- `V4_PLAN.md`: GitHub renders Mermaid in step summaries (17); the 1 MiB cap and 768 KiB budget (88, 148);
  Azure DevOps per surface at 130-138 (the summary tab is "the one surface with no evidence of support"; the
  recommendation to emit fences unconditionally); the `CI-Summary-Integration` rewrite at 218; Q4 at 235.
- `EVIDENCE_SURVIVES_A_RERUN_PLAN.md`: F9 at 52 (a run id is shared by every step, shard and project); F13 at
  57; `runs/` naming at 664-668; `KeepRuns` at 670-672, 834-842, 998; the Azure DevOps upload of kept runs at
  1337-1339.
- `DASHBOARD_PLAN.md` 451-453: a run URL becomes a link only if its host is the provider's (`github.com` for
  GitHub Actions); 9.x will need Azure DevOps' hosts on that list. Its Azure DevOps mention otherwise is 73.
- `NODE_PORT_PLAN.md` 508-511 mirrors this repository's detection, summary and artifact paths for Node.
- `ROADMAP.md` (line numbers after this plan's rows were added): the bar's "both forges first-class" at 68;
  2.1 to 2.4 at 245-248; 9.4 at 354; 13.2 at 415; track C at 459.

## 4. The second pass (2026-09-28)

What the second pass read, beyond §1 to §3. Source files were read from `raw.githubusercontent.com` at
their default branch on 2026-09-28; line numbers are those files'.

### 4.1 Azure Pipelines: the agent, the tasks, the images

- **Summary attachments** [TCE] 249-273: `uploadSummaryProperties.Add(TaskAddAttachmentEventProperties.Type,
  CoreAttachmentType.Summary); var fileName = Path.GetFileName(data);
  uploadSummaryProperties.Add(TaskAddAttachmentEventProperties.Name, fileName);`, so the name is the file
  name with its extension. `AddAttachment` (304-361) refuses a name holding `Path.GetInvalidFileNameChars()`
  of the agent's OS, needs the file to exist (`MissingAttachmentFile`), and calls
  `context.QueueAttachFile(type, name, filePath)`; [EC] 821-833 queues it as
  `_jobServerQueue.QueueFileUpload(_mainTimelineId, _record.Id, type, name, filePath, deleteSource: false)`,
  on the posting step's own record. [LOG]: uploadsummary "is a short hand form for the command
  `##vso[task.addattachment type=Distributedtask.Core.Summary;name=testsummaryname;]c:\testsummary.md`".
  How the Extensions tab titles a section: UNVERIFIED (S0 A4).
- **Restricted mode** [TCE]: `[CommandRestriction(AllowedInRestrictedMode = true)]` marks `logdetail`,
  `logissue`, `complete`, `setprogress`, `setsecret`, `setvariable`, `debug`, `settaskvariable` and
  `prependpath`, and not `uploadsummary`, `uploadfile`, `addattachment` or `setendpoint`. A blocked command
  logs a warning: `context.Warning(StringUtil.Loc("CommandNotAllowed", command.Area, command.Event))`
  (`Agent.Worker/TaskRestrictionsChecker.cs`, [TRC]).
- **The checkout's credential** [GSP]: "handle expose creds" writes `http.{repositoryUrl.AbsoluteUri}.extraheader`
  only `if (gitSupportAuthHeader && exposeCred)` (1215-1231), where `exposeCred` is the `persistCredentials`
  input (358); `PostJobCleanupAsync` removes it after the job (1481-1512); every checkout first removes any
  extraheader left behind (785-805).
- **`##[` in the agent**: only the agent's own output prefixes (`ExecutionContext` constants `##[section]`,
  `##[command]`, `##[error]`, `##[warning]`, `##[debug]`) and the paging logger's
  `message.Contains(groupStartTag, StringComparison.OrdinalIgnoreCase)` for `##[group]`
  (`Microsoft.VisualStudio.Services.Agent/Logging.cs`). The agent does not turn `##[error]` into an issue.
- **Which bash** [BASHSRC] `Tasks/BashV3/bash.ts`: `let bashPath: string = tl.which('bash', true);` (108); on
  `win32` it translates the script's path by running `bash -c pwd` in its folder (113-114, 170-171), which
  works for either bash. [IMG] `images/windows/scripts/build/Install-Git.ps1:51`:
  `Add-MachinePathItem "C:\Program Files\Git\bin"`; `scripts/helpers/PathHelpers.ps1:92-94`:
  `$newPath = $PathItem + ';' + $currentPath` (it prepends); `scripts/build/Install-WSL2.ps1`:
  `wsl.exe --install --no-distribution`; `scripts/build/Install-Msys2.ps1:110-132` puts msys2 on the path
  for its own install only and restores it; `scripts/build/Configure-Shell.ps1` links `C:\shells\gitbash.exe`
  and `wslbash.exe`. `Windows2025-Readme.md`, image 20260922.270.2: "Bash 5.3.15(2)-release", "Git
  2.55.0.windows.5", "Windows Subsystem for Linux (Default, WSLv2): 2.7.14.0". [BASH3]'s prose ("runs bash
  from the WSL default distribution") is contradicted by these. The plan reads A20 as Git Bash, INFERRED.
- **Pull-request iterations**: `System.PullRequest.SourceCommitId`, "The commit that is being reviewed in a
  pull request. (This variable is initialized only if the build ran because of a Git PR affected by a
  branch policy)" (`pipelines/build/includes/variables-hosted.md:143`, [VARS]); iterations list
  `sourceRefCommit`, `targetRefCommit`, `commonRefCommit` [ITL]; "Posting status to a specific iteration of
  a PR guarantees that status applies only to the code that was evaluated and none of the future updates"
  [PRSD]; "Microsoft Entra ID token with the **Code (status)** scope to have permission to change PR status"
  [PRSS]. Neither status page names a repository permission.
- **Reruns** [RNSS] (2024 roadmap): "In YAML pipelines, you can rerun a successful stage, but it triggers all
  stages that depend on it to rerun as well … We plan to give you the ability to rerun a single stage,
  without causing following stages to rerun too." [STG] (REST 7.1) documents `forceRetryAllJobs` and
  `state` (`cancel`, `retry`) only; [EXTAPI] `UpdateStageParameters` adds `retryDependencies`, undescribed.
- **`DownloadPipelineArtifact@2`** [DPA]: `artifactName` "If the value is left empty, the task downloads all
  artifacts associated with the pipeline run"; `targetPath` "If the multi-download option is applied (by
  leaving an empty artifact name), a sub-directory will be created for each download"; `itemPattern`
  "Unlike other tasks, this task will download a file that matches any pattern. Exclude patterns cannot
  be used to exclude previously included files"; aliases `path | downloadPath` for `targetPath`,
  `patterns` for `itemPattern`, `artifact` for `artifactName`.
- **`Build.Repository.Uri`** [VARS]: "The URL for the triggering repository. For example: Git:
  `https://fabrikamfiber@dev.azure.com/fabrikamfiber/_git/Scripts`"; available in templates. That it is set
  in a job with `checkout: none` is INFERRED (S0 prints it).
- **The schema** [VSC], measured by `schema_check.sh`: draft-07, `$comment` "v1.261.1", 1,640,523 bytes,
  86,347 gzipped at `-9`; 119 definitions; a boolean is `anyOf` strings matching `^true$`, `^y$`, `^yes$`,
  `^on$` and the rest; `stepTarget` has `container`, `commands` (`any`, `restricted`) and
  `settableVariables`; task inputs are listed by canonical name only (`DownloadPipelineArtifact@2`:
  `targetPath`, `itemPattern`, `artifactName`, …, `additionalProperties: false`); a non-standard
  `ignoreCase` keyword.

### 4.2 GitHub

- **The runner's legacy commands** [GHR]: `Runner.Common/ActionCommand.cs`: `public const string Prefix =
  "##[";` (35); `TryParse` begins `int prefixIndex = message.IndexOf(Prefix);` (132), anywhere in the line,
  for any registered command. `Runner.Worker/ActionCommandManager.cs`: `if
  (!ActionCommand.TryParseV2(input, _registeredCommands, out actionCommand) && !ActionCommand.TryParse(input,
  _registeredCommands, out actionCommand))` (70-71); `TryParseV2` requires `::` at the start after
  `TrimStart()` (`ActionCommand.cs:62-63`). `SetOutputCommandExtension` (308-351) warns only when
  `DistributedTask.DeprecateStepOutputCommands` is set, then sets the output; `set-env` and `add-path` are
  refused unless `ACTIONS_ALLOW_UNSECURE_COMMANDS`; `stop-commands` pauses all processing until its token.
- **Skip markers** [GHSKIP]: "Workflows that would otherwise be triggered using `on: push` or `on:
  pull_request` won't be triggered if you add any of the following strings to the commit message in a
  push, or the HEAD commit of a pull request: `[skip ci]`, `[ci skip]`, `[no ci]`, `[skip actions]`,
  `[actions skip]`".
- **git's header precedence** (RUN, `record_script.sh` G1 to G10, git 2.43.0): with
  `http.<host>/.extraheader` or `http.<repository URL>.extraheader` in `.git/config`, a command-line `-c
  http.extraheader=<token>` is not sent, and `-c http.extraheader=` does not clear it; `-c
  http.<host>/.extraheader=<token>` adds a second header. `actions/checkout` persists
  `http.https://github.com/.extraheader` ("AUTHORIZATION: basic" of `x-access-token:<token>`).

### 4.3 This repository and its neighbours (READ)

- The tool's console: `Program.cs` ends `return Commands.Dispatch(args, Console.Out, Console.Error);`, so
  every verb writes through those two writers. `MergedRunOutputs.cs` hands `output.WriteLine` to
  `CiSummaryWriter.Write` (139) and `CiArtifactPublisher.Publish` (159), the writer everything else uses.
- Run-derived text the first pass did not list: `HistoryCommand.Maintenance.cs:129` (`history gate`:
  `{scenario.Address}  {scenario.FeatureName} › {scenario.Name}  {verdict} — {QueryWriter.OneLine(evidence,
  160)}`, the names unflattened); `HistoryCommand.cs:322`, `:331` (rename suggestions with names);
  `HistoryCommand.Maintenance.cs:477-479` (`history import` labels).
- JSON the tool writes uses System.Text.Json's default encoder, which leaves `#` alone:
  `Query/QueryWriter.cs:32`, `:91`; `Query/PathEngine.cs:22`, `:413`; `Query/PayloadReader.cs:197` (a JSON
  body, pretty-printed); `QueryCommand.Describe.cs:8`. `--json` is a query flag (`QueryOptions.cs:279`).
- `history record`: `FragmentFiles` searches each input folder recursively
  (`SearchOption.AllDirectories`, `HistoryCommand.cs:454`); `Attempts` (386-432) folds a fragment under
  `runs/` with the same id as an earlier attempt and leaves one of another run alone; `LastFullRoster` and
  the partial heuristic read the ledger as it stands (304-345). `history verify` reports "a header that is
  not the first line"; `history show`, `record` and `doctor` still read such a ledger.
- `ReportConfigurationOptions.cs:265-270`: "measured on .NET 10, every VSTest-hosted runner under
  <c>dotnet test</c> swallows what a library writes from a run-end hook unless the verbosity is
  <c>detailed</c>, and TUnit's runner suppresses it at any verbosity".
- Wiki `Cross-Run-History` 95-150 at `86a77c1`: the printed fold step (`git worktree add
  "$RUNNER_TEMP/history-branch" origin/kronikol-history   # or --orphan on the first run`, one retry) under
  "This is the recipe Kronikol's own repository runs … so it is executed, not merely written".
- Kronikol4J at `34120a7` (a public repository, read through the session's git proxy): `CiEnvironment.java:30`
  detects `TF_BUILD`; `ReportFinalizer.java:184` and `:189` call `CiSummaryWriter.write` (46:
  `writeLine.accept("##vso[task.uploadsummary]" + tempPath)`) and `CiArtifactPublisher.publish` (51: one
  `##vso[artifact.upload …]` per file) with `System.out::println`, from the test JVM. No `runAttempt`, run
  id, `History.run.json`, run-end pointer, `::notice` or query verb was found in its main sources.

### 4.4 Beside `HISTORY_ACTION_PLAN.md` (2.3), read on `main` at `a9713c5`

- It was written the same days for 2.3 and designs the history scripts for both forges: four composite
  actions in `templates/github-actions/kronikol-history/` over bash scripts that keep their own repository
  under `RUNNER_TEMP` and take a token (its §4.1 to §4.5); git's settings in `scripts/git.sh` (§4.9: no
  prompt, no credential helper, `core.autocrlf=false`, no machine hook, the token as
  `http.<server>/.extraheader` through `GIT_CONFIG_COUNT`); its §4.2 interface is "what 2.4 builds to", and
  its §9 hands 2.4 byte-identical copies held by a drift fact.
- Its findings this plan's harness confirms independently: the wiki's recipe fails as printed (its F1;
  here W1, W2); record again rather than rebase (its §4.5; here v2). Its findings this plan does not have:
  F4 (the dogfood's loop lost 9 of 60 runs with six folds racing, ten times), F9 (under `actions/checkout`
  v6.0.0 the dogfood's worktree push fails), F15 (a wrong token and the machine's credential helpers), F16
  (CRLF scripts on Windows). This plan's that it does not have: S6b (a failed read starts a second ledger
  that `verify` rejects), S7 (a kept workspace fails every fold after the first), G2 to G4 and G11 (which
  header git sends, and the empty-then-token reset), S12.
- Its S0 rewrites the wiki's recipe now (no code); its S4 moves `ci-summary-preview.yml` onto the action.
  Its decision is D26; `main`'s D24 is the doorstep and D25 #72's landing, so this plan's decision is D27.
- `PR_REPORT_LINK_PLAN.md` (2.2) hands 2.4 three questions for the pull-request link: whose identity writes,
  which permission the build identity needs on threads, how the thread is found again.
- G11 (RUN, git 2.43.0): global `http.<host>/.extraheader` STALE; `-c http.extraheader=GOOD` sends STALE;
  `-c http.<host>/.extraheader=GOOD` sends STALE and GOOD; `-c http.<host>/.extraheader=` then
  `-c http.<host>/.extraheader=GOOD` sends GOOD, and so does the same pair through `GIT_CONFIG_COUNT`.
