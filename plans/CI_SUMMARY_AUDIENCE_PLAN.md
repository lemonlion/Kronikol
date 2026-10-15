# Who the CI job summary is written for (#169)

**Date:** 2026-10-13 · **Repo version:** 4.19.0 (`main` at `b7c633f4`; line numbers were read at `34145c44`, 4.18.1,
and checked again at `f03452fc`, where no file cited here had changed, and none has changed since, through 4.18.3 and
4.19.0; the wiki's are for `origin/master` at `e5c5a5b`,
Kronikol4J's for `origin/main` at `4ff5c07`) · **Status:** **green-lit in full by the owner on 2026-10-13** ("implement
the plan in full"), every §9 question as recommended, with a roadmap row (Q9) and the Azure DevOps issue (Q13) left to
the owner; **executed as 4.19.7** (R1, R2 and R3 in one patch, §11); **audited on 2026-10-15, its fixes released and
published as 4.20.6** (§12). Drafted from #169 (filed 2026-10-12 against
4.14.10) at the owner's ask of 2026-10-13 to analyse the issue critically and plan a fix for the underlying problem,
then checked again at the owner's ask the same day (§11). No stage row in `ROADMAP.md` (Q9); its leftovers are a row of
Appendix C.

Evidence labels: **RUN** (measured on 2026-10-13 on Windows 11 with the .NET SDK 10.0.300: the published Kronikol
4.18.1 from nuget.org, by [`CI_SUMMARY_AUDIENCE_PLAN.harness/probe.cs`](CI_SUMMARY_AUDIENCE_PLAN.harness/README.md), whose
output is `probe.out.txt` beside it; `gh` 2.92.0 and the published Kronikol.Tool against this repository's own runs
(`off-runner-checks.md`); and `dotnet test` of the repository's own example project (`ado-stdout.sh`)), **READ** (in
Kronikol's source, tests, workflows, wiki or changelog at the commits above, or in Kronikol4J's), **SOURCED** (GitHub's or
Microsoft's documentation, changelogs or issue trackers, read on 2026-10-13, linked where cited), **INFERRED** (reasoned
from two facts, stated by neither), **ESTIMATED** (not measured).

> **The CI job summary tells everyone not to open TestRunReport.html, the report people are meant to read.** When a run
> on GitHub Actions has a failure, Kronikol appends a **Debug this run** block to the job summary. Its first sentence is:
> "Do not open `TestRunReport.json` or `TestRunReport.html` — a real report reaches megabytes, and a single embedded
> diagram can be larger than a context window. Query it instead:" The reason it gives is a context window, so the rule
> is for AI agents. The block doesn't say so, though, and the job summary is the page a person lands on from a failed
> check. [...] **Ask:** Say who each instruction in the block is for. Tell a person to open `TestRunReport.html` from the
> run's artifact, and keep "don't open the JSON or the HTML, query it instead" as the rule for AI agents.

## 0. Summary

The symptom is real and reproduces word for word on 4.18.1 (§1). The ask is right: the job summary should send a person
to the report and keep "query it, don't open it" for agents. But the unscoped sentence is the visible half of a deeper
fault, and two labels on top of today's text would leave most of it in place.

**The underlying problem: Kronikol's CI text is written for an AI agent sitting on the runner, and on CI no reader is
there.**

1. **The job summary is read by people only.** No GitHub API, `gh` command or GitHub MCP tool returns a job summary
   (SOURCED: [cli/cli#11356](https://github.com/cli/cli/issues/11356), open and labelled `blocked`, where a maintainer
   wrote "GitHub APIs do not currently provide GitHub Actions job summaries"; RUN: on this repository's run 38281857162
   the job object has no summary field and the check run's `output.summary` is null in REST and GraphQL). Only a
   signed-in person with read access sees it, in a browser (RUN: a signed-out fetch of the run page has none of it).
   Azure DevOps differs: a summary there is a build attachment, which its REST API returns to a token with Build Read
   (SOURCED, §2.1), but under `dotnet test` it never arrives (F10). Kronikol already knows the GitHub half: the
   option's own documentation says "a summary-only block is invisible to the reader it is written for"
   (`ReportConfigurationOptions.cs:365-367`), its test calls the job summary "where a person looks"
   (`CiDebugSectionTests.cs:118`), and the wiki calls the CI summary "the human-facing half"
   (`CI-Summary-Integration.md:7`). Yet every line of the block speaks to an agent, and the one thing it tells its real
   reader about the report is not to open it.
2. **No reader of any CI channel is on the runner.** Every command in the block, the "previous run" line and the
   `::notice` annotation name the runner's absolute folder (`/home/runner/work/…/Reports`), and on a hosted runner
   "When the job has finished, the VM is automatically decommissioned" (SOURCED,
   [Using GitHub-hosted runners](https://docs.github.com/en/enterprise-cloud@latest/actions/how-tos/manage-runners/github-hosted-runners/use-github-hosted-runners)).
   Agents reach a CI run through `gh run view` (annotations), `gh run view --log`, the GitHub MCP server's job logs and
   its artifact download, all off the runner. Under `dotnet test`, the log and the annotation carry nothing of
   Kronikol's at all (RUN: the three failing legs of this repository's run 38400333756), so on the most common setup
   the job summary and the files in the artifact are the only channels left. Not one printed command works where any
   reader is. The `query.cs` line's "no network needed" holds off the runner only where the reader's NuGet cache holds
   the exact Kronikol version that wrote the report (READ, `QueryScriptGenerator.cs:113-133`).
3. **Nothing tells a person where the report is.** The block never says the HTML is in an artifact, nor which folder or
   where artifacts are on the page. "The artifact also carries `Failures.md`" asserts an upload Kronikol cannot see on
   GitHub, and Kronikol's own `ci-summary-preview.yml` prints it with no report artifact behind it (READ, §3 F3).
4. **Kronikol contradicts itself.** Its PR comment action says "Open `TestRunReport.html` inside it"
   (`kronikol-pr-report-link/action.yml:137-147`), `Failures.md` links each failure into the HTML
   (`FailuresDigestGenerator.cs:667`), `CLAUDE.md` names the HTML as the view "for a human"
   (`agent-instructions.md:88`), and with `WriteCiSummary` on, one summary says "see full report" a few lines above
   "Do not open `TestRunReport.html`" (RUN, probe D; the wiki shows the pair at `CI-Summary-Integration.md:231-233`).

Three smaller defects ride along. A green run with `WriteCiSummary` on still ends with "Debug this run" (RUN, probe C),
against the rule the section's own option states (F5). A run that wrote no HTML is still told not to open
`TestRunReport.html` (RUN, probe E). And a run whose data file is not `TestRunReport.json` (a custom
`HtmlTestRunReportFileName`, or an XML or YAML data file) prints a query command that answers "No TestRunReport.json
under …" (RUN, F11).

One defect found on the way is larger than the issue. **On Azure DevOps the summary never arrives under `dotnet test`,
and neither does the artifact upload** (F10): both are logging commands Kronikol writes to stdout from the run-end hook,
and `dotnet test` swallows them. That was measured at normal and detailed verbosity, and with VSTest's two switches for
test-host output. The wiki and the README promise the opposite ("No `PublishBuildArtifacts` task is needed"). Their
text is corrected with this plan's release; a code fix is the owner's call (Q13).

**The plan: three patches, recommended as one release (Q1).**

- **R1, the ask, made true.** The block says who each part is for, people first. The people part names what to open
  (`TestRunReport.html`, or `Failures.md`), the folder the job wrote it to relative to the workspace, and a link to the
  run's artifacts. It names only files the run wrote and states an upload only where Kronikol made it. It prints a query
  command only for a data file the tool reads, and names that file when a folder lookup would not find it (F11). The
  agent part keeps today's rule, labelled.
- **R2, commands that work where the reader is.** The agent part downloads the run's artifacts and queries the download
  (`gh run download <run> --repo <repo> --dir …`, then `kronikol query … <dir>`). The download is guarded, so the block
  can be pasted twice, and it carries the host on GitHub Enterprise. The runner's folder is named once, for a later step
  of the same job. The annotation and the "previous run" line follow suit. When a download holds several reports, as
  every matrix and sharded run's does, the tool's list of them says which is which (Q12).
- **R3, the full summary agrees with the block.** On a green run, `WriteCiSummary`'s summary ends with the people part
  under its own heading, not "Debug this run".

All three are patches: bug fixes in generated CI text and in the query tool's error output, with no new option and no
new public member (§4.6). The wiki's and the README's Azure DevOps text is corrected with the release (F10). Kronikol4J
writes no such block and gets a ledger line; its claim that the CI summary links the report is stale (§6.5). The
neighbouring ideas, a GitHub output naming the artifact, the unzipped one-click report of roadmap row 2.5 and a
collapsed agent section, are questions, not scope (§9).

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | When a run on GitHub Actions has a failure, Kronikol appends a "Debug this run" block to the job summary | **Confirmed, and wider.** Azure DevOps is sent it too, through `##vso[task.uploadsummary]` on stdout (`CiSummaryWriter.cs:30-32`), which under `dotnet test` never arrives (F10). `kronikol merge` writes it (`MergedRunOutputs.cs:164-171`), and so does `kronikol ingest`, through the run's own path (`IngestPipeline.cs:524` to `ReportGenerator.cs:752-758`). With `WriteCiSummary` on, every run ends with it, green ones included (`ReportGenerator.cs:693-715`) | READ; RUN (probe C, `ado-stdout.sh`) |
| 2 | Its first sentence is "Do not open `TestRunReport.json` or `TestRunReport.html` — …" | **Confirmed** word for word on 4.18.1 | RUN (probe A) |
| 3 | The reason is a context window, so the rule is for AI agents, and the block does not say so | **Confirmed.** The code names the reader: "the command an agent reaches for" (`ReportGenerator.cs:746-747`, `ReportConfigurationOptions.cs:365-367`) | READ |
| 4 | The job summary is the page a person lands on from a failed check | **Not quite.** A check's Details link opens that job's log page, `/actions/runs/<run>/job/<job>` (RUN: the check runs on `34145c44`). Job summaries are on the run's Summary page, one click away. The issue's conclusion holds, and more strongly than it says: the summary is read by people and nobody else (§0, item 1) | RUN; SOURCED |
| 5 | For a person, `TestRunReport.html` is the thing to open, and the block only says not to | **Confirmed.** Kronikol says the opposite everywhere else: `README.md:120`, the PR comment action, `Failures.md`'s links and `agent-instructions.md:88` (§0, item 4) | READ |
| 6 | The README scopes the rule to agents (`README.md#L124`) | **Confirmed** | READ |
| 7 | The job summary is the one place both audiences read the same text, and the one place the rule is not scoped | **Wrong on both counts.** Agents cannot read a GitHub job summary at all (§0, item 1). The same unscoped text also goes to the job log (`ReportGenerator.cs:757`, `MergedRunOutputs.cs:170`), which agents do read, from a step that owns its stdout (under `dotnet test` the log gets nothing, RUN), and into `CiSummary.md` in the artifact. Elsewhere the rule is scoped: the console pointer says "agents:" (`RunSummaryConsoleWriter.cs:183-185`), and `CLAUDE.md`, `AGENTS.md` and the skill are agent files by name | READ; SOURCED; RUN |
| 8 | The repro on 4.14.10 | **Confirmed on 4.18.1;** the builder has not changed since | RUN |
| 9 | A consumer can only fix it in their own workflow, by redirecting `GITHUB_STEP_SUMMARY` and adding the block back | **Mostly.** `WriteCiDebugSection = false` removes the block from both channels in one line, but nothing keeps the agent part while adding a people part. The redirection the issue describes would also hide every other summary the test step writes | READ |
| 10 | The suggested wording: "**People:** open `TestRunReport.html` from this run's artifact" | **Right in kind, not true in every run.** On GitHub Kronikol cannot see whether the job uploads an artifact or what it is called: `PublishCiArtifacts` writes `reports-path` to `$GITHUB_OUTPUT` and the consumer's step chooses the name (`CiArtifactPublisher.cs`, wiki `CI-Artifact-Upload.md:236`). Some runs write no HTML at all (probe E). R1's wording holds in each case | READ; RUN |

## 2. Where it stands today

### 2.1 The block and its channels (READ, SOURCED)

`RunSummaryConsoleWriter.BuildCiSummarySection` (`RunSummaryConsoleWriter.cs:251-282`) builds one markdown text: the
heading; the rule (`:258-259`); `dotnet tool install` and `kronikol query summary` and `failures` on the runner's folder
(`:260-264`); the `query.cs` line (`:266-268`); the history line (`:270-271`); the previous run, in the runner's
`runs/` folder (`:275-276`); and "The artifact also carries `Failures.md`" whenever the digest was written (`:278-279`).
The same text goes everywhere:

| Channel | Written | Read by |
|---|---|---|
| GitHub job summary (`$GITHUB_STEP_SUMMARY`) | A failing CI run (`ReportGenerator.cs:752-758`) or merge (`MergedRunOutputs.cs:164-171`) with `WriteCiDebugSection` (default on); every run with `WriteCiSummary` (`ReportGenerator.cs:693-715`, `MergedRunOutputs.cs:149-162`) | Signed-in people with read access, in a browser. Not `gh`, the REST or GraphQL API, or the GitHub MCP server (SOURCED, §0). Not signed-out visitors to a public repository either: run 38400333756's page and its `summary_partial`, fetched without a sign-in, hold the run's status and its four artifact names but none of the summary's text (RUN), and a `summary_raw` route reported in GitHub's community forum ("You do need to be logged in") answers 404 without one (SOURCED) |
| Azure DevOps summary | The same text, through a temporary file and a `##vso[task.uploadsummary]` line written to **stdout** (`CiSummaryWriter.cs:30-32`). Under `dotnet test` that line never reaches the agent, so there is no summary (RUN, F10); it arrives from `kronikol merge`, `kronikol ingest` and a directly run MTP host | People, on the run's Extensions tab, whose markdown rendering "is different from Azure DevOps wiki rendering"; which markdown renders there is undocumented. Also anything with a token holding Build Read, through the REST API's build attachments (type `DistributedTask.Core.Summary`); no `az` command reads them (SOURCED, [logging commands](https://learn.microsoft.com/en-us/azure/devops/pipelines/scripts/logging-commands), the agent's `TaskCommandExtension.cs:249-274`, REST "Attachments - List") |
| Job log | `Console.WriteLine` beside the summary write (`ReportGenerator.cs:757`, `MergedRunOutputs.cs:170`); `WriteCiDebugSection` only | Agents (`gh run view --log`, the MCP server's `get_job_logs`, "Fix with Copilot" on the logs page) and people. Under `dotnet test` it is swallowed: measured for 3.3.0 (`RunSummaryConsoleWriter.cs:80-87`), and again on 2026-10-13, absent from this repository's run 38400333756 and from a local `dotnet test` at normal and detailed verbosity (RUN). It reaches the log from `kronikol merge`, `kronikol ingest` and a directly run MTP host |
| `CiSummary.md` | Beside the report, with `WriteCiSummary` (`ReportGenerator.cs:708`, `MergedRunOutputs.cs:160`) | Whoever downloads the artifact |

The `::notice` annotation (`BuildGitHubNotice`, `RunSummaryConsoleWriter.cs:200-209`) is the one CI channel both
audiences read: people on the run page and the pull request's checks, agents through `gh run view`, which lists a run's
annotations of every level, notices included (RUN). It carries `kronikol query failures <runner folder>`, and the
console's swallowing applies to it too, since it is a workflow command on stdout: `gh run view 38400333756` lists no
Kronikol notice for any of the three failing legs (RUN).

### 2.2 What a reader can do with it (RUN, SOURCED)

- **A person** opens the run's Summary page and reads the block. To read the report, they download the artifact that
  holds the reports folder, a zip at the foot of the same page ("At the bottom of the workflow summary page, there is a
  dedicated section for artifacts", the [upload-artifact README](https://github.com/actions/upload-artifact)). The
  section carries `id="artifacts"`, and GitHub's own "Artifacts N" link in the page header points at `#artifacts`
  (one of each on the page served for run 38400333756, RUN); the anchor is undocumented. The wiki says the same of the
  download: "The links download a zip, need a GitHub sign-in, and expire with the artifact: `CiArtifactRetentionDays`
  defaults to one day" (`CI-Artifact-Upload.md:191`).
- **An agent** with `gh` lists the run (`gh run view <run>`: jobs, annotations, artifact names, RUN), reads its log, and
  downloads its artifacts. `gh run download <run> --repo <owner/repo> --dir <d>` puts each artifact in
  `<d>/<artifact name>/`, and one `--name` with `--dir` extracts straight into `<d>` (RUN). It stops at the first
  artifact whose files are already there ("error extracting … The file exists", exit 1, RUN), so the same command
  pasted twice fails. `--repo` also takes `<host>/<owner>/<repo>`: it works on github.com (RUN) and is how `gh` reaches
  GitHub Enterprise (SOURCED, §4.4). It works while the run is still in progress, and needs a sign-in with Actions read
  access (SOURCED, gh source and help).
- **Inside an artifact** the report sits where the upload step put it. A single folder path, as the wiki's
  `reports-path` recipe uploads (`CI-Artifact-Upload.md:133-140`), puts the folder's files at the artifact's top; a
  wildcard such as `'**/Reports/'` (`CI-Summary-Integration.md:99-104`) keeps the path from the workspace (SOURCED,
  upload-artifact README; RUN: the dogfood's history artifacts keep `examples/…`). `kronikol query <dir>` searches a
  folder recursively, skips `baseline/` and `runs/`, and takes the one `*TestRunReport.json` it finds, so the layout
  does not matter (READ, `QueryCommand.cs:437-475`). With several it lists them and stops: "Several reports under <d> —
  name the one you mean:", exit 2 (RUN). So `gh run download` followed by `kronikol query failures <d>` answers with no
  artifact name when the run uploaded one report, and lists the reports when it uploaded more. That is every matrix
  run that uploads a report per leg, every sharded run (the shards' artifacts beside the merge's), and Kronikol's own
  four-leg dogfood once Q8 adds its upload. The wiki already teaches the download-and-query route: "`kronikol query`
  can be pointed at a downloaded artifact folder (`kronikol query failures ./TestReports`)"
  (`CI-Artifact-Upload.md:91-92`). The block does not.
- **A re-run of failed jobs** uploads the same artifact names again, and both copies stay listed (SOURCED: observed on
  a public run whose listing held both; the wiki says the same, `CI-Artifact-Upload.md:189-190`). The API lists every
  attempt's artifacts by id, and `gh run download` keeps the first of each name and skips the rest (SOURCED, gh
  2.102.0 `download.go:159`). On the observed run the ids ran against the upload order, so after a re-run a download
  holds one attempt's report, not always the newest. `kronikol query summary` names a report's attempt when it is not
  the first (`QueryCommand.Overview.cs:71-74`).
- **`query.cs` off the runner** loads `Kronikol.dll` from the test run's own build output (a path on the runner) or from
  the NuGet cache at exactly the version that wrote the report, and restores nothing (READ,
  `QueryScriptGenerator.cs:15-25` and `:113-133`). On a developer's machine that has restored the repository at that
  version it works; elsewhere it does not.

### 2.3 How it came to be (READ, INFERRED)

3.3.0 (2026-09-13, `LLM_FIRST_PLAN` M3) added `WriteCiDebugSection` so that "a failing CI job says how to debug itself"
without the 48 KB `CiSummary.md`. It writes to two channels on two measured facts: runners swallow the console, and
the job summary "survives". The text was the agent's text, and the job summary was chosen as the fallback channel *for
the agent*. That a summary survives was measured; who reads it was assumed (INFERRED from the changelog's 3.3.0 entry
and the option's documentation, which records both facts and draws neither conclusion). The survival was GitHub's: its
summary is a file the runner reads. Azure DevOps's summary travels as a logging command on stdout, the channel the
same measurement found swallowed, yet the code and the wiki say "the job summary" survives with no platform named
(`ReportGenerator.cs:702-703` and `:712`, `RunSummaryConsoleWriter.cs:84-86`, `CI-Summary-Integration.md:62`;
INFERRED that Azure DevOps was assumed to share GitHub's channel, F10). 3.32.0 added the `query.cs` line, and 4.6.0
kept the labs page out of the rule; neither revisited the audience.

## 3. Findings the issue does not state

- **F1. The job summary has no agent readers** (§0, item 1). The agent half of the block is read only by a person who
  may hand it to an agent, so its commands must work on that person's or that agent's machine.
- **F2. Every path in the CI text is the runner's.** The block's two commands, its `query.cs` line, its previous-run
  line, and the annotation all name `/home/runner/…/Reports` (RUN, probes A and B). After the job, it does not exist.
- **F3. "The artifact also carries `Failures.md`" is said without an artifact.** It is printed whenever the digest was
  written (`RunSummaryConsoleWriter.cs:278-279`, `:321-322`). Kronikol's own `ci-summary-preview.yml` runs four example
  projects with `WriteCiSummary = true` (`TestRun.cs:23` and its siblings) and uploads only history fragments
  (`ci-summary-preview.yml:65-68`), so its summaries name an artifact that does not exist (READ).
- **F4. Kronikol tells people to open the HTML in four places** and not to in this one (§0, item 4).
- **F5. A green run with `WriteCiSummary` says "Debug this run".** The section is appended whatever the result
  (`ReportGenerator.cs:704`, `MergedRunOutputs.cs:158`; RUN, probe C). The wiki documents it ("**Always, at the end, a
  `## Debug this run` section**", `CI-Summary-Integration.md:62`, with a green run's example at `:231-242`), but it
  breaks the rule the standalone section states for itself: "Silent on a green run …: a block that speaks on every run
  is a block people learn to skip" (`WriteCiDebugSection`, `ReportConfigurationOptions.cs:373-374`). So R3 changes
  documented behaviour, and the changelog says so. No test covers the green case:
  `The_ci_summary_says_how_to_debug_the_run` (`ReportGeneratorAgentOutputsTests.cs:324-336`) runs a failing run only.
- **F6. An HTML file that was not written is named.** `HtmlFileName` falls back to `TestRunReport.html`
  (`RunSummaryConsoleWriter.cs:334-336`), so a run with `GenerateTestRunReport = false` is told not to open a file it
  does not have (RUN, probe E). R1's people line would otherwise send people to it.
- **F7. The annotation is the one channel agents and people share,** and it carries the runner's path (RUN, probe B).
- **F8. On Azure DevOps Kronikol knows the artifact and does not say so.** With `PublishCiArtifacts` it asks the agent
  to upload the folder's files as `CiArtifactName` (`##vso[artifact.upload … artifactname=TestReports]`,
  `CiArtifactPublisher.cs:57-72`), on the same stdout as the summary, yet the block says only "the artifact" (READ).
- **F9. Kronikol4J's out-of-scope list says the CI summary's inline images were "replaced by a link to the HTML report
  artifact"** (`docs/REMAINING_PARITY.md:2447-2448`). No such link exists in either port (READ).
- **F10. On Azure DevOps the summary and the artifact upload never arrive under `dotnet test`.** Both are logging
  commands written with `Console.WriteLine` from the run-end hook: `##vso[task.uploadsummary]`
  (`CiSummaryWriter.cs:30-32`) and one `##vso[artifact.upload …]` per file (`CiArtifactPublisher.cs:57-72`). The agent
  acts on them only when they reach the step's stdout ("only processed when written to stdout by tasks and scripts
  running directly on the agent", SOURCED, logging commands), and `dotnet test` swallows a library's output from that
  hook. Measured with `TF_BUILD=True` on the All Failing example (xUnit v3 through VSTest): no `##vso` line on stdout or
  stderr at normal or detailed verbosity, nor with `VSTEST_DISABLE_STANDARD_OUTPUT_CAPTURING=1`, the console logger at
  normal, or both with the logger at detailed, though the writer ran each time and left its temporary file (RUN,
  `ado-stdout.sh`). Yet the wiki says "No `PublishBuildArtifacts` task is needed" (`CI-Artifact-Upload.md:216`, and
  `:5`, `:22`, `:46`) and that the summary appears on the Extensions tab (`CI-Summary-Integration.md:28`, `:178`), and
  the README says "no additional pipeline configuration needed" (`README.md:144`). The one Azure DevOps example that
  does publish the folder names it by a wildcard (`pathToPublish: '**/Reports'`, `CI-Summary-Integration.md:171-175`),
  which neither publishing task takes ("Wildcards aren't supported", SOURCED: Microsoft's `build-artifacts.md:113` and
  `pipeline-artifacts.md:462`; `PublishBuildArtifactsV1` also requires the path to exist). Outside #169, but the
  release corrects that text (§6.3) and Q13 asks about a code fix. The people part's Azure DevOps rows (§4.3) are read
  where the summary does arrive: from `kronikol merge`, `kronikol ingest`, a directly run MTP host, or a pipeline step that
  uploads `CiSummary.md` itself.
- **F11. A data file the folder lookup cannot find, or the tool cannot read, still gets a query command.**
  `ReportGenerator` passes no `queryTarget` (`ReportGenerator.cs:660-678`; only the merge does,
  `MergedRunOutputs.cs:139`), so every command names the folder, and a folder lookup takes only a
  `*TestRunReport.json` (`QueryCommand.cs:456-458`). A run with `HtmlTestRunReportFileName = "Checkout"` writes
  `Checkout.json`, and its pointer, section and annotation say `kronikol query failures <folder>`, which answers "No
  TestRunReport.json under <folder>." and exits 2 (RUN, `off-runner-checks.md`). An XML or YAML data file
  (`TestRunReportDataFormat`) is never read by the tool, which searches `*.json` only, and with
  `GenerateTestRunReportData = false` there is no data file at all, while `DataFileName` falls back to
  `TestRunReport.json` (`RunSummaryConsoleWriter.cs:331-332`; READ). R1 names the file when it is not
  `TestRunReport.json`, as the merge does, and prints no query command when no JSON data file was written.
- **F12. A download with several reports answers with bare paths.** The list (§2.2, RUN) gives a reader choosing
  between a matrix's legs, or between shards and their merge, the artifact names and nothing else. Each run's
  `Run.json`, beside its report, already holds its suite, scenario count and failure count in a few hundred bytes
  (`RunManifest.cs:48-66`); a merge writes none. R2 puts them in the list (Q12).
- **F13. Three doc comments say what the code does not do.** `PublishCiArtifacts` "publishes report files as CI
  artifacts (GitHub Actions)" (`ReportConfigurationOptions.cs:381`): on GitHub it writes `reports-path` for the job's
  upload step and uploads nothing (`CiArtifactPublisher.cs:74-83`). The console option's remarks say a VSTest runner
  swallows run-end output "unless the verbosity is `detailed`" (`ReportConfigurationOptions.cs:285-287`, as 3.3.0's
  changelog measured), while the class says "at every verbosity" (`RunSummaryConsoleWriter.cs:81-83`); on 2026-10-13
  xUnit v3 3.2.2 with its VSTest adapter 3.1.5 swallowed it at detailed too (RUN). And "the CI job summary … survives"
  (`RunSummaryConsoleWriter.cs:84-86`, `ReportGenerator.cs:702-703` and `:712`) holds on GitHub only (F10). R1
  corrects all three (§6.4).

## 4. The design

### 4.1 Principles

1. **People first, then agents, each labelled.** The job summary is people's page; the label tells everyone else what
   applies to them. The issue's labels, `**People:**` and `**AI agents:**`, are used as written.
2. **One text for every channel, in plain markdown.** The labelled text reads correctly in the job summary, the log and
   `CiSummary.md`. No `<details>`: Azure DevOps does not document HTML on its Extensions tab, and the log shows tags raw
   (Q3).
3. **Every path a reader is meant to type or find is one that exists where that reader is:** the run's id, the
   download folder, and the reports folder relative to the workspace. That is where a person finds the folder in the
   repository, and inside the artifact when the job uploads it by a wildcard; uploaded as a single folder, its files
   sit at the artifact's top (§2.2). The commands search the whole download, so neither layout matters to them. The
   runner's absolute folder appears once, labelled for a later step of the same job.
4. **Say only what Kronikol knows.** Name an HTML file only when the run wrote it, an artifact only where Kronikol
   uploaded it, and otherwise say what has to be true ("when the job uploads that folder").
5. **Nothing captured, as today.** The block carries paths, counts, ids and Kronikol's own words. The new values
   (repository, run id and attempt, server host, workspace) come from the CI platform, and pass through `OneLine` and
   `Quote` like the paths do.
6. **No new public surface.** The CI facts reach the builder through an internal overload (§4.6).

### 4.2 The block after R1 and R2

For the issue's own probe on GitHub Actions, with `GITHUB_WORKSPACE=/home/runner/work/shop/shop`,
`GITHUB_REPOSITORY=acme/shop` and `GITHUB_RUN_ID=123456789`, and `PublishCiArtifacts` off (the common setup):

````markdown
## Debug this run

**People:** open `TestRunReport.html` in a browser to read the whole run, diagrams included, or `Failures.md` for every failure in context. This job wrote them to `tests/Acceptance/bin/Release/net10.0/Reports`; they are in this run's [artifacts](https://github.com/acme/shop/actions/runs/123456789#artifacts) when the job uploads that folder.

**AI agents:** do not open `TestRunReport.json` or `TestRunReport.html` — a real report reaches megabytes, and a single embedded diagram can be larger than a context window. Download the run's artifacts and query them instead:

```bash
test -d kronikol-123456789 || gh run download 123456789 --repo acme/shop --dir kronikol-123456789
dotnet tool install -g Kronikol.Tool
kronikol query summary kronikol-123456789
kronikol query failures kronikol-123456789
```

Read `Failures.md` in the download first. If the run uploaded several reports (a matrix, or shards beside their merge), the query lists them with their counts: pass the one you mean. With nothing installed, `dotnet run --file query.cs -- failures .` in the folder that holds the report runs the same engine, given the .NET 10 SDK and Kronikol <version> in the NuGet cache. A later step of this job finds the folder at `/home/runner/work/shop/shop/tests/Acceptance/bin/Release/net10.0/Reports`.
````

`<version>` is the version that wrote the report. The history line and the previous-run line follow as today, the
latter relative to the report: "The previous run (3 failed) is kept beside the report in `runs/gh_123456789_1`: add
`--run last-failed` to a query."

- **The guard** (`test -d … ||`) is there because a second `gh run download` into the same folder stops with "The file
  exists" (RUN); pasted again, the block queries what the first download left. `dotnet tool install -g` needs none: on
  the .NET 10 SDK it installs, updates or leaves the tool, and exits 0 each time (RUN).
- **On GitHub Enterprise** (`GITHUB_SERVER_URL` other than `https://github.com`), `--repo` carries the host:
  `--repo ghe.example.com/acme/shop` (§4.4). The people part's link already uses `GITHUB_SERVER_URL`
  (`CiMetadata.cs:42`).
- **Off CI, or on a CI run whose id or repository is missing,** the agent part keeps today's commands on the folder:
  the reader is on that machine. `CiSummary.md` is written off CI too, whenever `WriteCiSummary` is on.
- **The block stays bash**, as today's is: `Quote` escapes for POSIX shells (`RunSummaryConsoleWriter.cs:308-311`), and
  the fence says so.

R1 alone (Q1 answered "separately") brings the two labels, the people paragraph and the file-name fixes (F6, F11), and
keeps today's commands under the agent label. R2 replaces those commands and the sentences after them.

### 4.3 What the people part says, by what Kronikol knows

| Platform and setup | Kronikol knows | The people part ends |
|---|---|---|
| GitHub, `PublishCiArtifacts` off | nothing about an upload | "…; they are in this run's [artifacts](<run>#artifacts) when the job uploads that folder." |
| GitHub, `PublishCiArtifacts` on | it handed `reports-path` to the job's upload step, not the name | "…; the job's upload step puts them in this run's [artifacts](<run>#artifacts)." |
| Azure DevOps, `PublishCiArtifacts` on | it wrote an upload of each file as `CiArtifactName` to the same stdout as the summary, so a reader who has the summary from Kronikol has the artifact too | "…; they are in this build's `TestReports` artifact ([build](<build>))." |
| Azure DevOps, `PublishCiArtifacts` off | nothing about an upload | "…; they are in this build's artifacts ([build](<build>)) when the pipeline publishes that folder." |
| The run URL cannot be built | (a variable missing) | the same sentence, without the link |

`<run>` and `<build>` are `CiMetadata.PipelineUrl` (`CiMetadataDetector`, READ). On Azure DevOps it puts
`SYSTEM_TEAMPROJECT` into the URL unencoded (`CiMetadata.cs:65`), and a project name may hold a space, which ends a
markdown link, so the link's target is written with spaces encoded (test 3). The Azure DevOps rows are read only where
the summary arrives at all: from `kronikol merge`, `kronikol ingest`, a directly run MTP host, or a pipeline step that
uploads `CiSummary.md` (F10, §6.3). The folder is the reports folder relative to `GITHUB_WORKSPACE` or
`BUILD_SOURCESDIRECTORY`, written with `/`, or the absolute folder when it lies outside the workspace. Only files the run
wrote are named: no HTML, no "open `TestRunReport.html`" (the people part names `Failures.md` alone, or only the
folder); a merge names its own file (`Combined.html`).

### 4.4 What the agent part says (R2)

- **GitHub:** `test -d kronikol-<run> || gh run download <run> --repo <repo> --dir kronikol-<run>`, then
  `kronikol query summary` and `failures` on that folder. No artifact name is needed (§2.2). When the data file is not
  `TestRunReport.json`, as after `kronikol merge -o Combined.html` or with a custom `HtmlTestRunReportFileName`, a folder
  lookup cannot find it (`QueryCommand.cs:456-458` matches `*TestRunReport.json`), and the line names the file:
  `kronikol query failures "$(find kronikol-<run> -name Combined.json -print -quit)"`. `-quit` is in GNU find, in the
  BSD find macOS ships, and in BusyBox from 1.30 (SOURCED: their manuals and source).
- **GitHub Enterprise:** when `GITHUB_SERVER_URL` is not `https://github.com`, `--repo` is `<host>/<owner>/<repo>`.
  `gh` takes `[HOST/]OWNER/REPO` (SOURCED, gh 2.102.0 `pkg/cmdutil/repo_override.go`, `internal/ghrepo/repo.go:44-58`;
  RUN on github.com), and sends its calls to `https://<host>/api/v3/` for GitHub Enterprise Server and
  `https://api.<host>/` for GHE.com (`internal/ghinstance/host.go:59-70`). The reader needs a sign-in for that host
  (`gh auth login -h <host>`, or `GH_ENTERPRISE_TOKEN`), which gh's own error names on a 401.
- **A re-run** (`GITHUB_RUN_ATTEMPT` above 1) adds one sentence: "This is attempt 2. After a re-run, `gh run download`
  keeps one upload of each artifact name, not always this attempt's: `kronikol query summary` names the report's
  attempt." The first attempt's block, read after a re-run, cannot know one happened (§8).
- **Azure DevOps:** the artifact is named where Kronikol uploaded it, and the commands query the folder a download of it
  gives (`kronikol query failures TestReports`). No `az` line: `az pipelines runs artifact download` fetches pipeline
  artifacts and fails on the container artifacts `##vso[artifact.upload]` creates ("Artifact with name 'drop' was found,
  but is of type 'Container'"; SOURCED, azure-devops-cli-extension issues #569 and #982, the second open). The
  documented route, REST `…/_apis/build/builds/{id}/artifacts?artifactName=TestReports` and its `downloadUrl`, needs a
  token the reader's shell may not hold (Q7).
- **The `query.cs` line** says what it needs off the runner: the .NET 10 SDK and the report's Kronikol version in the
  NuGet cache. Today's "Nothing to install and no network needed" is true only on the runner.
- **The runner's folder** appears once, as the place a later step of the same job finds the report (Q4).
- **The annotation** becomes, for the probe: `::notice title=Kronikol::1 failed of 42 scenarios. Reports in
  tests/Acceptance/bin/Release/net10.0/Reports (people: TestRunReport.html or Failures.md; agents: gh run download
  123456789 --repo acme/shop --dir kronikol-123456789, then kronikol query failures kronikol-123456789)`. One line, no
  runner path, nothing captured, and complete without the job summary, which its agent readers cannot see. About 250
  characters, under the runner's 4,096, and one notice per step, under its ten (SOURCED, actions/runner
  `ExecutionContext.cs`). It reaches the run only from a step that owns its stdout (§2.1).

### 4.5 R3: the full summary on a green run

`ReportGenerator.cs:704` and `MergedRunOutputs.cs:158` append the section to `CiSummary.md` whatever the result. On a
green run they append the people part alone, under `## The report`: there is nothing to debug, and "a block that speaks
on every run is a block people learn to skip" (the standalone section's own rule, F5), while the pointer to the HTML is
worth having under a summary whose diagrams are a preview (Q5). The wiki's "Always, at the end" changes with it (§6.3).
`CiSummaryGenerator`'s "see full report" lines (`:126`, `:168`) stay as they are: after R1 the block below them agrees,
and Kronikol4J ports those lines byte for byte.

### 4.6 Code shape, version and public surface (READ)

- `RunSummaryConsoleWriter` gets an internal `CiReader` record: provider, run URL, run id and attempt, repository, the
  server's host when it is not github.com, workspace, the artifact name when Kronikol uploads it, whether
  `PublishCiArtifacts` handed over the folder, and the Kronikol version. `CiReader.Detect(Func<string, string?> getEnv,
  ReportConfigurationOptions options)` builds it from `CiMetadataDetector.Detect(getEnv)`.
- An internal `BuildCiSummarySection(RunSummary, CiReader)` does the work. The public `BuildCiSummarySection(RunSummary)`
  keeps its signature and reads the process's environment with default options (Q11). `BuildGitHubNotice` and `Write`
  gain internal overloads the same way.
- The callers already hold both inputs: `ReportGenerator.cs:704`, `:756` and `:764` (`getEnv`, `options`),
  `MergedRunOutputs.cs:158`, `:169` and `:182` (`getEnv`, `options`), and `IngestCommand.cs:479` (the process).
- `HtmlFileName` returns null when the run wrote no HTML (F6). `ReportGenerator` hands `Summarise` the data file as its
  `queryTarget` when its name is not `TestRunReport.json`, by the rule `MergedRunOutputs.QueryTarget` already uses
  (`MergedRunOutputs.cs:192-199`), and the builder prints no query command when the run wrote no JSON data file (F11).
  `Summarise` already takes the parameter, so nothing public changes.
- `QueryCommand.ResolveReport`'s list of several reports adds, for each, its size and, from a `Run.json` beside it, its
  suite and failure count (F12, Q12). It still resolves nothing by picking one.
- **Version: a patch.** The repository's rule: "A bug fix that necessarily changes observable behaviour is still a
  patch", and a change to generated output "is not on its own a major bump". No option, public type or member is added:
  `RunSummary` stays as it is, because a new property on that public record would be new surface and a minor bump.
  The changelog calls out the behaviour change: the job summary, `CiSummary.md`, the log copy and the annotation read
  differently, and a script matching the old first sentence stops matching; a run with a custom data file name prints
  that file in its commands; and the query tool's list of several reports carries more on each line.

### 4.7 Not in this plan

- **The report one click away** (`ROADMAP.md:343`, row 2.5, not green-lit): an unzipped `TestRunReport.html` artifact,
  which GitHub serves inline (measured by `PR_REPORT_LINK_PLAN.md` check 9). Its id exists only after the upload step,
  so a later step would write that link (SOURCED); the people part here is what it would replace. Note §8's catch first.
- **Naming the artifact on GitHub,** for example a `reports-artifact-name` output the upload step reads (a minor, Q6).
- **The console pointer off CI** (already says "agents:"), and `CLAUDE.md`, `AGENTS.md` and the skill, which are agent
  files by name and right as they are. The copies in the reports folder carry no path, only the report's file name
  (`agent-instructions.md` holds one placeholder, `__REPORT__`; READ), so they read the same after a download.
- **The run-end pointer on CI** (Q14). It names the runner's folder too, and it is the last thing a run or a merge says
  (`ReportGenerator.cs:762-764`, `MergedRunOutputs.cs:180-182`). Left as it is: under `dotnet test` it never reaches the
  log (RUN); where it does (a merge, an ingest, an MTP host), its folder is true for a later step of the job, and on
  GitHub the annotation printed right after it carries the off-runner route once R2 ships.
- **Delivery on Azure DevOps under `dotnet test`** (F10). This plan corrects the docs; a code fix is Q13.

## 5. Tests, red first

Unit facts in `tests/Kronikol.Tests/Reports/`, calling the internal overload with a `CiReader`, so none of them sets
process-wide environment variables (unlike `CiDebugSectionTests`, which has to discount blocks from parallel classes,
`:100-108`). Each fails on 4.18.1 unless marked. A fact that goes through `ReportGenerator`'s public path reads the
process's environment, which on Kronikol's own CI is a GitHub Actions run with a real `GITHUB_RUN_ID` and
`GITHUB_REPOSITORY`, and off it is nothing: such a fact asserts what holds both ways, or passes the environment it
reads, as `RunRotationTests` does.

**R1** (`CiSummaryAudienceTests`):
1. The people part comes first, and the rule appears only after `**AI agents:**`.
2. The people part names `TestRunReport.html` and `Failures.md` when both were written; neither HTML nor the rule names
   an HTML file the run did not write (probe E); a merge names `Combined.html`.
3. On GitHub, the people part links `https://github.com/acme/shop/actions/runs/123456789#artifacts`; on Azure DevOps,
   the build, and a project named `My Project` gives a link target with no space in it; with no run URL, no link.
4. The folder is relative to the workspace, with `/`; a folder outside the workspace is named in full. Windows paths
   on a Windows host only: `Path.GetRelativePath` reads the host's separators.
5. The upload is stated per §4.3's four rows; "The artifact also carries" appears in none.
6. Each existing safety fact holds for the new values (`RunEndPointerSafetyTests`: a folder with spaces, quotes and line
   breaks, and a repository, server or workspace variable holding a line break).
7. **The data file is named when a folder lookup would miss it** (F11), through `ReportGenerator`: with
   `HtmlTestRunReportFileName = "Checkout"`, the pointer, the section and the annotation name `Checkout.json`, and the
   pointer's command answers through `QueryCommand` (exit 0); with an XML data file, or none, no query command is printed.

**R2:**
8. No line of the bash block names the runner's folder; the block downloads by run id behind the `test -d` guard and
   queries the download.
9. The runner's folder appears exactly once, in the later-step sentence.
10. **The printed commands answer on the layout `gh` gives** (the strongest fact): copy a real run's reports folder to
    `<temp>/kronikol-123456789/<artifact>/`, take each `kronikol query` line from the block, and run it through
    `QueryCommand`: exit 0 and the failing scenario named. Three times: a default run; a merge with `-o Combined.html`,
    through its line that finds the file; and two runs in two artifact folders, where the command exits 2 and lists both
    with their suites and failure counts (F12).
11. With `GITHUB_SERVER_URL=https://ghe.example.com` the block says `--repo ghe.example.com/acme/shop`; with
    `https://github.com`, `--repo acme/shop`.
12. With no run id or no repository, the agent part is today's commands on the folder (green on 4.18.1: it pins what
    R2 keeps).
13. With `GITHUB_RUN_ATTEMPT=2` the agent part names the attempt and says a download may hold another attempt's report;
    with `1`, or none, it says nothing of attempts.
14. The previous run is named as `runs/<run>` with `--run last-failed`.
15. The `query.cs` line names the SDK and the version, not "no network needed".
16. The annotation has no runner path and names the folder, the download and the query.
17. Beside `RetainedRunsTests`' folder-lookup facts (`tests/Kronikol.Tests/Tool/`, through its `Query` helper): the list
    of several reports gives each one's size and, where a `Run.json` sits beside it, its suite and failure count, and
    the path and size alone where none does (a merge). No test checks the list today.

**R3:**
18. A green run with `WriteCiSummary` writes `## The report` with the people part and no `## Debug this run` (through
    `ReportGenerator`, F5). `The_full_ci_summary_is_still_one_block_not_two` stays green unchanged.

**Pins that move with the text** (READ, checked at `f03452fc`): the rule's capital "Do not open" at
`RunEndPointerTests.cs:53` and `LabsReportRunTests.cs:236`; the runner-folder commands at `RunEndPointerTests.cs:52`
and `:123`, `RunEndPointerSafetyTests.cs:114-115`, `MergeWritesTheRunOutputsTests.cs:239` and
`ReportGeneratorAgentOutputsTests.cs:334` (which then has to hold on and off CI); and the annotation's command at
`RunEndPointerTests.cs:54`. The headings and the history line stay, so `RunRotationTests.cs:879`,
`HistoryOutputsTests.cs:266` and `MergeWritesTheRunOutputsTests.cs:222`, `:238`, `:240`, `:241` and `:256` (a failing
merge) keep passing. So does `CiDebugSectionTests.cs:107-108`, whose block counter keys on the folder's name, which the
block still carries: in the commands where the run id or repository is missing, as in that class, and in the
later-step sentence where they are set. No golden file holds the block.

**Proofs,** as the last plans did: the new facts copied onto 4.18.1 each fail on their own assertion, and these
mutations are killed: drop either label, swap the parts, put the runner's folder back in the bash block, drop the
guard, drop the host, drop the attempt sentence, drop the file-name check, drop the data-file target, name an artifact
on GitHub, drop the green-run branch, drop the list's counts, and print the annotation's old target.

**On a real run (RUN, recorded in §11 before the release):** Kronikol's own `ci-summary-preview.yml` runs four example
projects with `WriteCiSummary = true` as a matrix, three of them failing. Add an upload of each leg's reports folder
(Q8, a workflow change with no bump), dispatch it on the release branch (it has `workflow_dispatch`,
`ci-summary-preview.yml:6`), and check that each leg's summary renders people first and that the `#artifacts` link lands
on the artifacts. Then paste a failing leg's agent part into an empty folder on this machine: it downloads the four
reports, lists them with their counts, and the listed All Failing report answers. Paste it a second time: the guard
holds.

No Playwright: nothing in the HTML report changes.

## 6. Releases and records

### 6.1 Releases

One patch, the next free number when it is taken (4.18.2 shipped on 2026-10-13, another session's audit; nothing in it
touches the section; 4.18.3 and 4.19.0, #170's two releases, shipped the same day and changed none of the files cited
here, so the next free number is 4.19.1 or later).
R1, R2 and R3 change the same builder (`RunSummaryConsoleWriter.cs:200-336`) and the same pins, so splitting them means
rewriting the pins twice (Q1). The Azure DevOps doc corrections (F10) ride with it and need no bump of their own.

### 6.2 Changelog draft

> **Patch: the CI job summary says who each instruction is for (#169).** A patch: bug fixes, nothing new to call. The
> "Debug this run" section told every reader "Do not open `TestRunReport.json` or `TestRunReport.html`", a rule for AI
> agents, on the page only people can see: GitHub serves job summaries to signed-in people in a browser, and no API,
> `gh` command or MCP tool returns them. It now speaks to people first: open `TestRunReport.html` (or `Failures.md`),
> which this job wrote to the named folder, from the run's artifacts, with a link to them. The rule follows, labelled
> for AI agents. Every command in it named the runner's folder, which is gone once the job ends; it now downloads the
> run's artifacts (`gh run download <run> --repo <repo> --dir kronikol-<run>`, guarded so that pasting it twice works,
> and with the host on GitHub Enterprise) and queries the download, and the runner's folder is named once, for a later
> step of the same job. The `::notice` annotation, which `gh run view` shows to agents, carries the same download and
> query, and the previous-run line names `runs/<run>` beside the report. The section names no HTML file the run did not
> write, and states an upload only where Kronikol made one (Azure DevOps with `PublishCiArtifacts`, by its name). A run
> whose data file is not `TestRunReport.json`, such as one with a custom `HtmlTestRunReportFileName`, printed
> `kronikol query failures <folder>`, which found nothing; its pointer, section and annotation now name the file, and a
> run with no JSON data file prints no query command. When a folder holds several reports, `kronikol query` now lists
> each with its size, suite and failure count. With `WriteCiSummary` on, a green run's summary ends with the people part
> under "The report" in place of "Debug this run", where the wiki had documented "Debug this run" on every run.
> Behaviour change: the section, `CiSummary.md`, the log copy, the annotation and the list of several reports read
> differently. Docs: on Azure DevOps under `dotnet test`, the summary and the artifact upload never reached the
> pipeline. Both are logging commands written to stdout at the end of the run, which `dotnet test` swallows. The wiki
> and the README said no pipeline step was needed; they now give the steps.

### 6.3 Wiki (from a wiki worktree; the shared checkout may hold a peer's edits)

`CI-Summary-Integration.md:62` (what the section holds, its "Always" (R3), and its claim that the block survives every
runner, which is GitHub's summary only, F10), `:189` (the option row), `:231-242` and `:301-315` (the block shown
verbatim, twice); `Report-Configuration.md:165`; `Merging-Parallel-Reports.md:62` and `:112-113`;
`CI-Artifact-Upload.md:91-92` (the download-and-query recipe, which the block now prints). The two pages show different
upload steps (`CI-Artifact-Upload.md:133-140` and `CI-Summary-Integration.md:99-104`): left as they are (§8).

Azure DevOps (F10): `CI-Summary-Integration.md:28`, `:152-178` and the troubleshooting row at `:336`;
`CI-Artifact-Upload.md:5`, `:22`, `:46` and `:196-216`; and, in the repository, `README.md:140` and `:144`. Each says
what arrives under `dotnet test` (neither the summary nor the upload) and what arrives from a merge, an ingest or an MTP
host (both). The Azure DevOps examples gain two steps after the tests, each with `condition: succeededOrFailed()`: a
`PublishPipelineArtifact@1` of the reports folder, named literally since no publishing task takes a wildcard (the
present `pathToPublish: '**/Reports'` at `CI-Summary-Integration.md:171-175` goes), and a script that echoes
`##vso[task.uploadsummary]` with the path of `CiSummary.md` (`WriteCiSummary` on). Both are documented Azure DevOps
features, not run on Azure DevOps by this plan (INFERRED). With them the examples leave `PublishCiArtifacts` off, since
under `dotnet test` its upload never arrives.

### 6.4 Doc comments (they ship in the package)

`ReportConfigurationOptions.cs:352-375` (`WriteCiDebugSection`: who each part is for, the job summary's readers as
measured here, and that on Azure DevOps the block travels on stdout, F10), `:284-290` (the console's remarks: which
verbosities swallow it, and that the job summary that survives is GitHub's, F13) and `:381` (`PublishCiArtifacts`
uploads on Azure DevOps and hands the folder to GitHub's upload step, F13); `RunSummaryConsoleWriter.cs:80-87` (the
same two corrections) and `:247-250`; the code comments at `ReportGenerator.cs:702-703` and `:712`;
`MergeCommand.cs:376-381` (its wording still holds, checked).

### 6.5 Plans, Kronikol4J and the release routine

- `PLANS_STATUS.md` row (added with this draft); a `ROADMAP.md` placement is the owner's (Q9).
- Kronikol4J's ledger (`docs/REMAINING_PARITY.md`, the divergence section from `:1877`): "Not mirrored, nothing to
  port: the port writes no Debug this run section, annotation or pointer, and has no query tool." In the same edit,
  correct `:2447-2448` (F9; at `4ff5c07`). No Kronikol4J release.
- As every release: the templates pin the previous release, and the history action's `VERSION` the new one.

## 7. Where it sits in the roadmap

Unplaced. It is small (ESTIMATED a day of code and docs, and half a day more with the real-run check), touches no other
plan's files, and is the first thing a person on a failing CI run reads, so a stage 1 row would fit (Q9).

## 8. Found on the way, not in this plan

- **`gh run download` fails on an unzipped artifact, and stops** ("zip: not a valid zip file",
  [cli/cli#13012](https://github.com/cli/cli/issues/13012), open, its fix PR #13013 unmerged; SOURCED). The artifacts
  listed before it are extracted and the rest are never tried. A run that uploads `TestRunReport.html` with
  `archive: false` (upload-artifact 7.0.0 and later), as roadmap row 2.5 proposes, breaks R2's download-everything line.
  Row 2.5 must then name the zipped artifact in it (`--name`), which needs Q6.
- **A reports folder with a backtick in its name breaks the block's code spans** (INFERRED: `Quote` escapes spaces and
  quotes, `RunSummaryConsoleWriter.cs:313-319`, not backticks). Rare, and the same today.
- **The wiki shows two upload steps,** `name: test-reports` with `reports-path` and `name: test-reports` with
  `'**/Reports/'` (READ). The first puts the report at the artifact's top and the second keeps its path (§2.2); a folder
  lookup finds it either way.
- **The first attempt's block, read after a re-run, may download the other attempt's report** (§2.2). It cannot know a
  re-run happened; `kronikol query summary` names a later attempt, so the reader can tell. A download by artifact id
  would be exact, but nothing in the job knows the id before the upload step runs.
- **`gh run view` probably lists at most 30 annotations per job** (INFERRED: gh 2.102.0 asks once, with the API's
  default page size). On a job with many build warnings, the notice may be missing from that list, though it stays on
  the run page.
- **The Azure DevOps link is built from `System.TeamFoundationServerUri`** (`CiMetadata.cs:60-65`), which the agent
  sets for back-compatibility ("back compat variable, do not document", agent `Constants.cs:543`). Microsoft's own
  artifact-download task builds the same build URL from it, so it holds for build pipelines; in a classic release job it
  names the release service. Left as it is.
- **Runs now expire** with the Actions retention setting, at most 90 days for a public repository (SOURCED,
  [changelog 2026-10-01](https://github.blog/changelog/2026-10-01-actions-retention-now-covers-checks-runs-and-statuses/):
  runs are "now governed by the same GitHub Actions retention setting"; that their job summaries go with them is
  INFERRED). `PublishCiArtifacts` asks for one day by default, so a summary read two days later points at an expired
  artifact, which the API marks `expired` and `gh run download` skips. Not a wording problem.

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Ship R1, R2 and R3 as one patch? | **Yes.** One builder, one set of pins. R1 alone is the ask, but it would leave the agent part's commands unusable where its readers are |
| Q2 | Word the people part conditionally when Kronikol cannot see an upload ("when the job uploads that folder"), or assertively as the issue's example does? | **Conditionally.** Kronikol's own dogfood shows the assertive form is false in real runs (F3) |
| Q3 | Fold the agent part into `<details>` on GitHub? | **No.** One text serves the job summary, the log and `CiSummary.md`; Azure DevOps does not document HTML; the labels already tell people to skip it |
| Q4 | Keep the runner's folder at all? | **Yes, once, labelled** for a later step of the same job or a self-hosted runner. It also keeps `CiDebugSectionTests`' counter working |
| Q5 | A green run with `WriteCiSummary`: the people part alone, under `## The report`? | **Yes** (F5) |
| Q6 | Let the GitHub section name the artifact, for example a `reports-artifact-name` output from `PublishCiArtifacts` for the upload step to read? | **Not now.** It is new surface (a minor), and only needed once row 2.5 makes the download-everything line fragile (§8) |
| Q7 | Print an `az pipelines runs artifact download` line on Azure DevOps? | **No.** It fails on the container artifacts `##vso[artifact.upload]` makes (§4.4, SOURCED). Name the artifact and the query |
| Q8 | Upload each leg's reports folder in `ci-summary-preview.yml`, so Kronikol's own dogfood has the artifacts its summaries name? | **Yes**, with this release (no bump). Its four legs then make the dogfood a several-reports download, which §5's real-run check uses |
| Q9 | Place it in `ROADMAP.md`? | The owner's call; a stage 1 row (§7) |
| Q10 | The annotation's wording (§4.4), the one text both audiences read | **As drafted:** complete on its own, because its agent readers cannot see the summary |
| Q11 | Should the public `BuildCiSummarySection(RunSummary)` read the process's CI environment? | **Yes.** Called from a CI job, it then prints what the run prints; off CI the people part has no link |
| Q12 | Put each report's size, suite and failure count, from its `Run.json`, in the query tool's list of several reports? | **Yes, with R2.** The download-everything recipe lands on that list on every matrix and sharded run, and in the dogfood (F12). Dropping it leaves bare paths, which still work |
| Q13 | Fix the Azure DevOps delivery in code (F10)? | **Not in this plan; file it as its own issue.** The options are a `kronikol` command that a pipeline step runs after the tests and that owns its stdout (new surface, a minor), or the section written to a fixed file beside the report for a documented step to upload (a patch). This plan corrects the docs and gives the two steps (§6.3) |
| Q14 | Leave the run-end pointer on CI as it is? | **Yes** (§4.7): it rarely reaches a CI log, its folder is true for a later step, and the annotation after it carries the off-runner route |

## 10. Assumption ledger

| # | Assumption | Basis | If wrong |
|---|---|---|---|
| A1 | Agents cannot read a GitHub job summary; signed-in people can, and signed-out visitors cannot | SOURCED, RUN (§0, §2.1) | If GitHub adds an API, the agent part is then also read directly, and it already works off the runner. On Azure DevOps a token can read it already (§2.1); nothing in the design depends on it |
| A2 | Most consumers upload the reports folder as an artifact | ESTIMATED; the wiki and the PR link action recommend it | The conditional wording stays true |
| A3 | `gh run download`'s layout and the recursive folder lookup together find one report, or list several | RUN (`off-runner-checks.md`), READ; fact 10 measures it | Fact 10 fails first |
| A4 | `#artifacts` stays the artifacts section's anchor | GitHub's own header link; undocumented | The link lands at the top of the same page |
| A5 | The Extensions tab renders bold, inline code, a fenced block and a link | INFERRED from "markdown … different from wiki rendering" | The text still reads in order; checked on an Azure DevOps run if one is available, with A8 |
| A6 | `query.cs` off the runner works with the report's version in the NuGet cache | READ | Its line says what it needs either way |
| A7 | `gh` keeps taking `[HOST/]OWNER/REPO` for GitHub Enterprise | SOURCED (gh 2.102.0); RUN on github.com | The Enterprise line fails with gh's own error, which names the fix |
| A8 | The two Azure DevOps steps of §6.3 deliver the summary and the folder under `dotnet test` | INFERRED from Microsoft's documentation; not run on Azure DevOps | The corrected recipe is wrong in a new way; check it on an Azure DevOps run if one is available |

## 11. Log

- **2026-10-13 (kronikol-21).** Drafted at the owner's ask, in worktree `C:/Code/Kronikol-169` (branch
  `plan/169-ci-summary-audience` at `34145c44`). The probe ran on the published 4.18.1 (harness). GitHub's side was
  checked with `gh` 2.92.0 against this repository's runs, and against GitHub's documentation, changelogs and the
  `cli/cli` tracker on 2026-10-13. Nothing in a package changes. Moved onto `2b7508d5` (4.18.2 and the #168 and #172
  plans) the same day, before any commit: no file it cites changed.
- **2026-10-13, second pass (kronikol-21),** at the owner's ask to check the plan for accuracy and completeness and to
  do any deep dive that would change it. Every cited line of source, tests, wiki and Kronikol4J was read again at
  `f03452fc` (wiki `e5c5a5b`, Kronikol4J `4ff5c07`). Fixed: `MergedRunOutputs.cs:160`, Kronikol4J's `:2447-2448`, the
  pin list in §5, principle 3's claim about the artifact's layout, F5 (the wiki documents "Always"), the hosted-runner
  quote's page, and the retention changelog, which says nothing about expired artifacts leaving the UI. Measured
  (`off-runner-checks.md`, `ado-stdout.sh`):
  - `gh run download` into a folder that already holds the download stops with "The file exists".
  - `--repo` takes a host on github.com.
  - `gh run view` lists notices.
  - The dogfood's failing legs put no notice, pointer or section in the log under `dotnet test`.
  - With `TF_BUILD=True`, `dotnet test` passes no `##vso` line at any verbosity or with either VSTest switch (F10).
  - A custom-named data file's printed command fails (F11).
  - A download with two reports answers with bare paths (F12).
  - `dotnet tool install -g` is idempotent on SDK 10.

  Two research agents read gh 2.102.0's source (Enterprise hosts, duplicate names after a re-run, re-download),
  GitHub's and Microsoft's documentation, the Azure DevOps agent, tasks and CLI, and the `find -quit` manuals; the
  tasks' own text showed that the wiki's one Azure DevOps publish step names a wildcard no publishing task takes. The
  design gained the download guard, the Enterprise host, the re-run sentence, the data-file rule, the list's counts and
  the Azure DevOps doc corrections. Q7 is answered; Q12 to Q14 are new. Still not green-lit, with no version and no roadmap
  row taken. Moved onto `71426b3f` (plan files only since `f03452fc`) before any commit. 4.18.3 and 4.19.0 (#170)
  shipped later that day; a diff to `baa411bb` shows neither touched a file cited here.
- **2026-10-13, green-lit (kronikol-21).** The owner asked to "implement the plan in full". Every §9 question is taken
  as recommended: one patch (Q1), the conditional wording (Q2), no `<details>` (Q3), the runner's folder once (Q4),
  `## The report` on a green run (Q5), no artifact-name output (Q6), no `az` line (Q7), an upload per leg in
  `ci-summary-preview.yml` (Q8), the annotation as drafted (Q10), the public builder reading the process's environment
  (Q11), the counts in the list (Q12) and the run-end pointer as it is (Q14). Two answers are acts only the owner
  takes, a roadmap row (Q9) and filing the Azure DevOps code fix as an issue (Q13), so neither is done here. Moved
  onto `b7c633f4` first: no file cited here changed.
- **2026-10-13, implemented (kronikol-21)** as 4.19.7, one patch, in worktree `C:/Code/Kronikol-169` (branch
  `impl/169`). An internal `CiReader` (`src/Kronikol/Reports/CiReader.cs`) carries what the CI text needs: the run's
  page, id and attempt, the repository, the host on GitHub Enterprise, the workspace, the artifact Kronikol uploads on
  Azure DevOps, whether `PublishCiArtifacts` handed the folder to a GitHub upload step, and the Kronikol version. The
  section, the annotation and `Write` take it through internal overloads; the public ones read the process's CI with
  default options (Q11). The agent part has three forms: a guarded download on GitHub with a run id and a repository,
  the uploaded artifact on Azure DevOps with `PublishCiArtifacts`, and today's commands on the folder otherwise.
  `RunSummaryConsoleWriter.QueryTargetFor` is the merge's rule, now shared with the run (F11). `kronikol query`'s list
  of several reports reads each one's `Run.json` (F12, Q12), and takes it only when it lists the report.
  Decided while building, beyond the plan's text:
  - **A green run's "The report" keeps the history line and the previous-run line** when either has something to
    say. §4.5 said "the people part alone"; read literally it would have dropped the ledger's line, which
    `Cross-Run-History.md` says the job summary carries, and the previous-run line #80 added so that a green re-run
    does not hide the failing run before it. Neither speaks on every run.
  - **The `find` for a data file the folder lookup misses skips `runs/` and `baseline/`**, as the lookup does
    (`ReportFolders.IsReserved`): `-print -quit` takes the first match in directory order, which can be a kept run.
    In a download of several legs it takes the first leg's file, and the sentence after the block says so and how
    to list them.
  - **No part names a file the run did not write, the pointer included**: with no data file the pointer forbids the
    HTML, and with no JSON data file it prints no query command (F6, F11). `RunEndPointerSafetyTests`' fixture gained
    a `TestRunReport.json`, since its facts quote the commands.
  - **The Azure DevOps build link encodes spaces and parentheses**, which `SYSTEM_TEAMPROJECT` may hold.
  - Found on the way: the remark on `CreateStandardReportsWithDiagramsInEnvironment` said the CI channels read the
    real environment; they read `getEnv` (corrected). `CiArtifactPublisher`'s summary said it uploads on GitHub
    (corrected, F13). The plan's pin list missed `RunEndPointerTests.GitHub_notice_is_one_line_with_counts_and_paths_only`,
    which the public builder would have broken on this repository's own CI, and `Generated-Reports.md` and
    `Querying-Reports.md` in the wiki.
  - Both copies of the skill's `references/commands.md` (the tool embeds one, the templates package packs the other)
    now say the list of several reports gives each one's size, suite and failure count.
  - `ROADMAP.md`: no stage row (Q9 is the owner's); this plan's leftovers (Q13, Q6 and §8's catches) are a row of
    Appendix C.
  Real-run check (§5), before the release: the implementation was snapshotted on a throwaway branch
  (`check/169-ci-summary`, deleted after) and `ci-summary-preview.yml` dispatched on it, run 38433876527. The All
  Passing leg, whose `PublishCiArtifacts` is on, ends with "## The report" and says the job's upload step puts the report
  in the run's artifacts; the three failing legs put people first, with the folder relative to the workspace and "when
  the job uploads that folder", then the labelled rule and the guarded download of run 38433876527. The run page holds
  `id="artifacts"`. The All Failing leg's block, pasted into an empty folder on Windows with the built tool standing in
  for `kronikol`, downloaded the run's artifacts and listed the four reports with their sizes, suites and failures (11 of
  11, 0 of 11, 5 of 6 and 15 of 20; the history artifacts hold no report), and the listed All Failing path answered
  `failures`. Pasted again, the guard did nothing; the same download without it failed on the existing folder.
  Tests: `CiSummaryAudienceTests` (new, 40 cases), `CiSummaryRunTests` (new, 8 cases: the printed commands run against
  the layout `gh run download` gives), one new `RetainedRunsTests` fact, and the pins moved in four classes. Proofs
  (harness `r1/`): 41 of the 108 facts in the touched classes fail on 4.19.0, each on its own assertion
  (`red-stub.txt`); 33 of 33 mutations are killed (`mutations.txt`), among them M33 for the fact added after the red
  run; the probe prints the plan's examples (`probe.out.txt`). Checks: after the bump, the core suite passed 7,511 with
  2 skipped on 4.19.5 and 7,516 with 2 skipped rebased onto 4.19.6; `release.slnf` builds in Release for every target,
  once nuget.org listed the EF Core 10.0.13 packages it had half-published that day (two attempts failed at restore
  before) (`checks.txt`).
- **2026-10-13, published (kronikol-21).** 4.19.7 is commit `04e27710` on the plan commit `ceaf43f3`, tag `v4.19.7`.
  Release run 38447740240, CodeQL 38447735848 and CI Summary Preview 38447735870 passed; CI 38447735905 passed on its
  second attempt, the first having failed one ProxyTap fact (`ProxyTapTraceContextTests`, a listener port another test
  in the process had taken, the race kronikol-ef fixed in `ced2e03c`). nuget.org listed all 62 ids at 19:40 UTC. The
  templates pin 4.19.4, the newest version listed at the push. The wiki has the edits (`26c0c53`), Kronikol4J's ledger
  its line and the corrected out-of-scope line (`0b1f55c`), and #169 has a comment saying what changed and what is left
  (Q13) and is closed. Before the tag, the Release build of `release.slnf` failed twice at restore while nuget.org
  listed EF Core 10.0.13 ahead of its dependencies (harness `r1/checks.txt`); the tag waited until restore passed.
- **2026-10-15, audited (kronikol-21)** at the owner's ask, in worktree `C:/Code/Kronikol-169-audit` (branch
  `audit/169`) on 4.20.2: one gap and fourteen defects, fixed as 4.20.6 (§12, harness `audit/`).
- **2026-10-15, published (kronikol-21).** 4.20.6 is commit `43e963e3`, tag `v4.20.6`, rebased onto 4.20.5
  (`e80eb4b5`): #174's R1 (4.20.3), #165's R1 (4.20.4) and #168's audit (4.20.5) took the numbers before it. Release
  38723272350, CI 38723268002, CodeQL 38723268038 and CI Summary Preview 38723267999 passed on their first attempt, and
  nuget.org listed all 62 ids by 11:19 UTC. The templates pin 4.20.4, the newest version listed for all 62 ids at the
  push. The wiki has the audit's edits (`d4093aa`), Kronikol4J's ledger its line (`3c02ee9`, nothing to port), and #169
  a comment naming the release. On the real run, the All Failing leg's block from run 38723267999, pasted into an empty
  folder with the built tool, downloaded the run, listed its four reports with their counts and answered for the one
  named (exit 0). The same run showed G16 (§12), fixed in the docs without a bump.

## 12. Audit, 2026-10-15

The owner asked on 2026-10-15 to "make sure nothing was missed from the plan and that the implementation was good with
no bugs". Two independent reviews ran on 4.20.2 (`origin/main` at `3240c983`; no commit after 4.19.7 had touched the
plan's files). One ticked every deliverable of §4 to §6 and §9 against the release, the wiki and Kronikol4J. The other
looked for inputs that make the printed text wrong, running the printed lines in Git Bash against folders laid out as
`gh run download` lays them out. Every finding was read again in the code before anything changed. Fixed in one patch
(4.20.6), with the facts in harness `audit/`.

Everything the plan listed shipped, with one gap: on Azure DevOps without `PublishCiArtifacts` the agent part still
named the agent's folder (G1 below). The plan was silent on that case; principle 3 covers it. The defects:

| # | Found by | Defect | Fix | Fact |
|---|---|---|---|---|
| G1 | both | On Azure DevOps without `PublishCiArtifacts`, the setup the 4.19.7 wiki recommends under `dotnet test`, the agent part queried the agent's folder (gone after the job), "Nothing to install and no network needed" included | The commands query a download of what the pipeline publishes, into `kronikol-<build>`; `query.cs` as read off the agent; the agent's folder once, for a later step | `On_azure_devops_without_an_upload_the_agent_part_queries_a_download_of_the_published_folder` |
| G2 | bugs | `--run last-failed` in the previous-run line, the pointer and `Failures.md` opens the newest run with a failure, the one on top included: on a failing run, the run itself. Pre-existing (#80, 3.27.0) in the pointer and the digest | `--run <the kept folder's name>` everywhere; the green section's line names the query it belongs to | `The_previous_run_is_opened_by_its_folder_never_by_last_failed`, `On_a_failing_run_the_pointer_and_the_digest_open_the_kept_run_by_its_folder` |
| G3 | bugs | `--run` on a report with its own name (`HtmlTestRunReportFileName`) answered "No TestRunReport.json under …runs/<name>" for every kept run: the kept folder was looked up as a folder. Pre-existing (#80) | The kept run's report is the file named as the one on top, when there | `Run_opens_a_kept_run_whose_report_has_its_own_name`, `A_custom_data_file_name_is_found_in_the_download_and_its_kept_run_opens` |
| G4 | bugs | `Failures.md` printed `kronikol query … .` ten times, which answers "No TestRunReport.json under ." for a custom data file name or a merge's `-o` (F11's class, in a file the plan did not list) | Every command names the data file when the folder lookup would miss it; a default name is byte for byte as before | `The_digest_of_a_run_with_its_own_report_name_names_its_data_file_in_every_command`, `Merge_writes_the_failures_digest_beside_the_report` |
| G5 | bugs | The ```` ```bash ```` block quoted only for a space or a quote: bash ate a Windows folder's backslashes (`C:UsersmeReports`) and expanded a `$` | Each word in single quotes when it holds anything outside `[A-Za-z0-9_./:=@%+,-]`; the console pointer keeps double quotes, which cmd needs | `The_bash_block_quotes_a_folder_as_bash_reads_it` |
| G6 | bugs | `find -name` read `[`, `*` and `?` in a custom report name as a pattern: `Report[linux].json` matched nothing and the query was handed `""` | The pattern escapes them | `A_data_file_named_with_pattern_characters_is_found_by_its_own_name` |
| G7 | bugs | GitHub with `PublishCiArtifacts`: "the job's upload step puts them in this run's artifacts" was false for all but the last of several test projects in one step, since `reports-path` holds one folder (documented on CI-Artifact-Upload) | "this run named the folder in `reports-path` for the job's upload step, which uploads the last folder named there" | `The_upload_is_stated_only_where_Kronikol_made_it` |
| G8 | bugs | A re-run's block downloaded into the first attempt's folder, which the guard then kept: attempt 2's block queried attempt 1's download | `kronikol-<run>-<attempt>` after a re-run | `A_re_run_downloads_into_a_folder_of_its_own` |
| G9 | bugs | The several-reports list stopped at twenty without a word | It says how many more | `A_list_of_more_than_twenty_reports_says_how_many_it_leaves_out` |
| G10 | bugs | A name carried GitHub's older `##[command]` or Azure DevOps' `##vso[` into the pointer, both read anywhere in a line (`##vso[task.setvariable]` sets a variable). Pre-existing (3.1.0) | A space after the first `#` | `A_name_holding_a_logging_command_prints_as_text` |
| G11 | bugs | Small: the attempt sentence named `kronikol query summary` with an XML or YAML data file; the annotation named a download for agents with nothing in it; a folder that is the workspace was "`.`" | Each fixed | `A_re_run_says_its_attempt…`, `The_annotation_names_a_download_only_when_an_agent_has_something_in_it`, `A_folder_that_is_the_workspace_is_named_as_its_root` |
| G12 | plan | The Q11 fact could fail only on a GitHub runner, and read the process's variables while `CiDebugSectionTests` set them | `CiSummaryPublicBuilderTests` sets the run's variables in that class's collection | mutations A17 to A19 |
| G13 | plan | Wiki: `Report-Configuration.md`'s console row kept F13's "unless verbosity is `detailed`"; `CI-Summary-Integration.md` said `PublishCiArtifacts` uploads the CTRF | Corrected | (docs) |
| G14 | plan | Harness: `r1/red-stub.txt` credited M29 to the line-break theory; only the folder fact catches it | Note corrected; A20 to A22 prove the server URL and run id cases | mutations |
| G15 | plan | The changelog said `CiSummaryRunTests` ran a custom data file name through a download; that fact had no run id | `A_custom_data_file_name_is_found_in_the_download_and_its_kept_run_opens` does | (fact) |
| G16 | real run | Kronikol's `::notice` never reaches a GitHub run tested under `dotnet test`: none in preview runs 38433876527 (4.19.7), 38718437816 (4.20.4) or 38723267999 (4.20.6). §2 measured it, yet the wiki and four doc comments said agents read the annotation through `gh run view` | The wiki says so, with a troubleshooting row (`d4093aa`); the doc comments (`WriteCiDebugSection`, `RunSummaryConsoleWriter`, `BuildGitHubNotice`, `CiReader`) in a commit without a bump, under the changelog's `[Unreleased]`. Nothing in the code can carry it there: GitHub reads a workflow command only from a step's output | (docs) |

Left as they are, for the owner:

- **A download made while other jobs of the run were still going holds only their artifacts uploaded by then**, and
  the guard keeps it: another leg's block, pasted later into the same folder, can answer from the wrong leg's report
  with exit 0. The environment names no job of a matrix (no leg id), so the folder cannot be named per leg; the wiki
  says to delete the folder and paste again.
- **Azure DevOps with `PublishCiArtifacts` left on under `dotnet test`:** the summary uploaded by a pipeline step names
  a `TestReports` artifact that never arrived (F10). Kronikol cannot see from inside the run whether its stdout reaches
  the agent; the wiki says to leave the option off with the two steps.
- **`act`, Gitea and Forgejo set `GITHUB_*`,** so the block names a run `gh` cannot reach there.
- **`reports-path` holds one folder per step** (G7). Holding every folder of the step (a multi-line output, which
  `upload-artifact` takes) would change what the output's readers get: new behaviour, the owner's call.
- **Q13** (the Azure DevOps delivery fix) is still the owner's, as is Q9.

## Appendix A. Edit sites at `f03452fc`

| File | Lines | Release |
|---|---|---|
| `src/Kronikol/Reports/RunSummaryConsoleWriter.cs` | `:80-87` (doc), `:200-209` (annotation), `:211-245` (`Write` overloads), `:247-282` (the section), `:331-336` (file names) | R1, R2 |
| `src/Kronikol/Reports/ReportGenerator.cs` | `:660-688` (the data file as `queryTarget`, F11), `:693-715`, `:752-764`; comments at `:702-703` and `:712` | R1, R3 |
| `src/Kronikol/Reports/Merge/MergedRunOutputs.cs` | `:149-182` | R1, R3 |
| `src/Kronikol/Query/QueryCommand.cs` | `:462-473` (the list of several reports, F12) | R2 |
| `src/Kronikol/ReportConfigurationOptions.cs` | `:284-290`, `:352-375`, `:381` (doc only) | R1 |
| `src/Kronikol.Tool/IngestCommand.cs` | `:459-479` (the pointer and annotation through the public path) | R2 |
| `.github/workflows/ci-summary-preview.yml` | an upload step per leg (Q8) | R1 |
| `tests/Kronikol.Tests/Reports/`, `tests/Kronikol.Tests/Tool/` | new `CiSummaryAudienceTests.cs`; the list's fact beside `RetainedRunsTests`; the pins in §5 | R1 to R3 |
| `README.md` | `:140`, `:144` (Azure DevOps, F10) | docs |
