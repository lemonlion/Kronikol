# AZURE_DEVOPS_PARITY_PLAN harness

The scripts behind the numbers in [`../AZURE_DEVOPS_PARITY_PLAN.md`](../AZURE_DEVOPS_PARITY_PLAN.md) that
could be taken without an Azure DevOps organisation, with the output they printed, and the probe pipeline
for the half day that needs one (plan §7.5, S0). The first pass ran on 2026-09-27 at `e4c9e36` (3.31.9),
SDK 10.0.401, Linux; the second pass (2026-09-28, same commit and SDK) added the history scripts, the
Microsoft.Testing.Platform matrix and the schema check.

| File | What it answers | Output |
|---|---|---|
| `stdout_matrix.sh` | Plan F1 across the frameworks: whether what the library prints at run end reaches the console of the command that ran the tests, for every example project, the way a pipeline would run it | `results-stdout-matrix.txt` |
| `stdout_channel.sh` | Plan F1 on the one project that posts a summary: whether its `##vso[task.uploadsummary]` line reaches `dotnet test`'s output at four verbosity settings, and what GitHub Actions' file channel receives from the same run | `results-stdout-channel.txt` |
| `stdout_mtp.sh` | Plan F1 under the .NET 10 SDK's Microsoft.Testing.Platform mode of `dotnet test` (TUnit, and xUnit v3 joined to it), at the default output and at `--output Detailed` | `results-stdout-mtp.txt` |
| `f2_rerun_fold.sh` | Plan F2: the run id the library writes for a re-run Azure DevOps job, and what `kronikol history record` then records, today and with the attempt in the id | `results-f2-rerun-fold.txt` |
| `record_script.sh` | Plan F12, F13 and §4.5: the history scripts, tested before they are written. This repository's fold step (v0), the plan's first draft (v1) and the revision (v2) through the same cases, against a git server that demands a token, under bash 3.2.57 and the local bash; the read step; the wiki's printed recipe | `results-record-script.txt` |
| `kronikol-history-record.sh`, `kronikol-history-read.sh` | Prototypes of the design `HISTORY_ACTION_PLAN.md` settled on for 2.3's scripts, written independently: what `record_script.sh` runs as v2 and as the read step. The scripts that ship are 2.3's; these carry the cases this plan asks them to hold (plan §6) | (the scripts under test) |
| `githttp.py` | The git server `record_script.sh` runs: git's own `http-backend` behind a check for the token, logging every request's Authorization headers, with a fault a test can arm | (a helper) |
| `schema_check.sh` | Plan §6's schema fact on what the plan drafts today: every YAML block of Appendix A and `s0-probe.yml` against Microsoft's `service-schema.json`, with two files that must fail | `results-schema-check.txt` |
| `s0-probe.yml` | Plan §7.5: every Azure Pipelines claim the plan rests on and could not run here | none yet: `results-s0.txt` when it is run |
| `research-notes.md` | The evidence the plan was written from: Microsoft's pages and the agent's source (quotes and flags, including what the plan did not need), every provider-specific path in this repository by file and line, and what the other plans already say | (notes, not a script) |

Each script runs from the repository root. The .NET ones need the .NET 10 SDK and set the variables an
Azure Pipelines agent sets (`TF_BUILD`, `BUILD_*`, `SYSTEM_*`). None needs an organisation: an agent reads
logging commands from the output of the step's process and nowhere else, so a line that never reaches that
output never reaches an agent.

## `stdout_matrix.sh`

Every example project prints the run-end pointer ("Kronikol: reports written to …") through the same
`Console.WriteLine`, in the same run-end hook, that prints Kronikol's Azure DevOps logging commands a few
lines later, so it stands in for them on projects that post no summary. Each project runs under
`dotnet test` at the default verbosity and at `--verbosity normal`, TUnit under `dotnet run --project`
(it refuses VSTest-mode `dotnet test` on the .NET 10 SDK), and each xUnit v3 test executable directly.

| Framework | `dotnet test` | `--verbosity normal` | Other |
|---|---|---|---|
| xUnit 2 (plain, LightBDD, Reqnroll) | 0 of 3 | 3 of 3 | |
| xUnit v3 (plain, LightBDD, Reqnroll, BDDfy) | 0 of 4 | 0 of 4 | the executable: 4 of 4 |
| NUnit 4 | 0 of 1 | 0 of 1 | |
| TUnit (plain, LightBDD, Reqnroll) | | | `dotnet run --project`: 0 of 3 |

The first version of the script ran the executables without `DOTNET_ROOT` and they exited 131 before
running a test (".NET location: Not found"); the script now sets it from the `dotnet` on the path, as
`UseDotNet@2` and `setup-dotnet` do on an agent.

## `stdout_channel.sh`

The project is `CiPreview.AllPassing`: xUnit v3 under VSTest (`xunit.runner.visualstudio` 3.1.5,
`Microsoft.NET.Test.Sdk` 18.3.0) with `WriteCiSummary = true`. Build it first with
`dotnet build examples/Example.Api/tests/Example.Api.Tests.CiPreview.AllPassing -c Release`. Each run gets
its own `TMPDIR`, so the count of summary files there proves the Azure DevOps branch of `CiSummaryWriter`
ran; the count of `##vso[` lines on `dotnet test`'s stdout and stderr is all an agent would ever see.

```
Azure DevOps, default verbosity: summary files written for the agent 1; ##vso[ lines on stdout 0, on stderr 0
Azure DevOps, --verbosity normal: summary files written for the agent 1; ##vso[ lines on stdout 0, on stderr 0
Azure DevOps, --verbosity detailed: summary files written for the agent 1; ##vso[ lines on stdout 0, on stderr 0
Azure DevOps, --logger console;verbosity=detailed: summary files written for the agent 1; ##vso[ lines on stdout 0, on stderr 0

CiSummary.md beside the report (the library wrote it either way): 35228 bytes
GitHub Actions, default verbosity: $GITHUB_STEP_SUMMARY received 35220 bytes
```

The byte counts differ by a few bytes between runs because the summary states the run's duration. The
repository's own remark on `WriteRunSummaryToConsole` says VSTest runners let run-end output through at
`detailed`; on this project nothing got through at any setting.

## `stdout_mtp.sh`

The mode is chosen by a `test` section in `global.json`, so each project gets a `global.json` of its own for
the run (the repository's own is not touched) and it is removed afterwards. xUnit v3 projects are built with
`-p:UseMicrosoftTestingPlatformRunner=true`, which leaves their build output in that form until the next
plain build.

| Framework | default output | `--output Detailed` |
|---|---|---|
| TUnit (plain, LightBDD, Reqnroll) | 0 of 3 | **3 of 3**, replayed indented under "Standard output" |
| xUnit v3 (plain, Reqnroll, BDDfy) | 0 of 3 | 0 of 3 |

The LightBDD xUnit v3 project does not start in this mode ("The configuration file 'appsettings.json' was
not found"): it reads its configuration from the working folder, which the mode moves to the project's
folder. The script says so instead of counting it as a 0.

## `f2_rerun_fold.sh`

The same project, run twice under one build id (`BUILD_BUILDID=5000`) with `SYSTEM_JOBATTEMPT=1` and
then `2`, as "Rerun failed jobs" does to the job it re-runs. The reports directory is emptied first. Each
run's `History.run.json` is kept, as each attempt's artifact would be, and the first is edited to fail one
scenario, because it stands for the attempt that failed and was re-run. Then three folds with the real
`kronikol history record`: (A) today, a fold after each attempt; (B) today, one fold after the re-run; (C)
the second fragment's id set to `ado:5000:2`, what S1 makes the library write, folded as in A; (D) that id,
folded once as in B (added in the second pass). Working paths in the output are replaced by `<work>`.

Today both attempts are `ado:5000:1`, and the second keeps the first under `runs/ado_5000_1` as an earlier
attempt of the same run. A records the failure and throws the re-run away as a duplicate ("22 scenarios
from 2 shards (already in the ledger)"); B records one run of 22 scenarios, the same eleven twice; C and
D record two runs, the second all green.

## `record_script.sh`

Three versions of the fold step go through the same cases: **v0**, the fold step of
`.github/workflows/ci-summary-preview.yml` at `e4c9e36`, in a checkout made the way `actions/checkout`
makes one (depth 1, the token persisted for the host); **v1**, the plan's Appendix A draft as first
committed (`bbbcdb1`), in a checkout made the way the Azure Pipelines agent makes one (depth 1, detached,
nothing persisted, the token passed per command); **v2**, `kronikol-history-record.sh`, with no checkout,
given the header as Azure DevOps would send it (bearer) and as GitHub would (basic, `x-access-token`).
v0 and v1 are extracted from those commits, so the script tests what was written; the one edit is v0's
`dotnet run --project src/Kronikol.Tool …`, which becomes `kronikol`. The fragments are one real
`History.run.json` from the CI-preview project, copied under twelve run ids. Git runs with no global or
system configuration; bash 3.2.57 is built from GNU's tarball (its hash is pinned in the script) unless
`BASH32` names one. The ledger is judged by `kronikol history verify` and by the run lines on the branch.
Both prototypes are clean under ShellCheck 0.11.0 at `-S info`.

- **G, what git sends** (by the server's log). A credential persisted for the host or the repository URL
  wins over `-c http.extraheader=<token>`: the passed one is never sent, and an empty one clears nothing
  (G2, G3, G3b). Only the same URL-specific key passed on the command line adds a second header (G4). With
  a stale header for the host in the machine's global configuration (G11), `-c http.extraheader=<token>`
  sends the stale one alone and `-c http.<host>/.extraheader=<token>` sends both; setting that key empty
  first and then to the token sends the token alone, through `-c` and through `GIT_CONFIG_COUNT` alike,
  and so does the remote's own URL as the key.
  `ls-remote --exit-code` exits 2 for a missing branch and 128 for a wrong token or a 500 (G6 to G8), where
  `fetch` exits 128 for all three (G9, G10). A URL carrying a user name, as `Build.Repository.Uri` does,
  works with the header (G7b).
- **S, the fold, per version and shell.** All three pass S1 to S5 and S8: a first run, an append, the same
  fragments again, a run landing between the fetch and the push (a pre-receive hook plays it), a refusal
  that never clears, four pipeline runs at once. S8's rejection messages vary with timing from run to run;
  its outcome does not. **S6b**: a 500 on the first read, on a ledger whose header an earlier release
  wrote. v0 and v1 take the orphan path, rebase through `merge=union`, exit 0, and leave two header lines:
  `verify` fails ("line 5: a header that is not the first line"). v2 stops with nothing recorded. (S6 is
  the same with matching headers, where the merge folds the common head away and the result reads sound.)
  **S7**: the next job reuses the sources folder, as a self-hosted agent does, with the temp folder
  emptied. v0 and v1 stop at "'kronikol-history' is already used by worktree"; v2 records. **S9**: v1 on
  GitHub with no header stops under `set -u` when the variable is unset. **S11**: a stale credential
  persisted in the checkout; v1 sends it and fails, v2 records. **S12**: a stale header in the machine's
  global configuration; v1 fails, v2 (the key set empty first) records. **S10**: none of the scripts holds a
  `$(identifier)` or `${{`. S8 raced four folds once and lost nothing on any version;
  `HISTORY_ACTION_PLAN.md`'s harness races six at a time, ten times, and the dogfood's loop lost 9 of 60
  there (its F4), so S8 is weak evidence on races.
- **R, the read step** (`kronikol-history-read.sh`): no branch (exit 0, no file), a ledger (exit 0, the
  branch's file byte for byte), a 500 or a wrong token (exit 3, no file), and a stale credential in the
  surrounding checkout (R5) or in the global configuration (R6), neither of which has any effect.
- **W, the wiki's printed recipe** (`Cross-Run-History`, wiki `86a77c1`), under GitHub's default `bash -e`:
  as printed it stops at "fatal: invalid reference: origin/kronikol-history" (W1); with the fetch the
  dogfood has put before it, at "Author identity unknown" (W2).

## `schema_check.sh`

```
schema: http://json-schema.org/draft-07/schema# v1.261.1, 1640523 bytes, 86347 gzipped, sha256 f00a9630f6550204…
appendix-a-1.yml: valid
appendix-a-2.yml: valid
s0-probe.yml: valid
must-fail-input-alias.yml: INVALID
    at steps/0/inputs: Additional properties are not allowed ('path' was unexpected)
must-fail-misspelt-key.yml: INVALID
    at steps/0: Additional properties are not allowed ('checkout', 'persistCredential' were unexpected)
```

The YAML is read with every scalar kept as text, because the schema types every value as a string (a
boolean is a string matching `^true$` and its spellings), so a `fetchDepth: 1` read as a number would fail.
The first draft's pipeline failed the check on its `DownloadPipelineArtifact@2` `path:` input, an alias the
service accepts and the schema does not list; it now says `targetPath:`. The check also caught a YAML error
in the second pass's own edit of the probe (an unquoted `echo "stage after: …"`).

## `s0-probe.yml`

A throwaway pipeline for a scratch Azure DevOps project. It reads no secret and prints none; the one token
it uses is the job's own `System.AccessToken`, mapped into three steps' environments. It pushes one orphan
branch, `kronikol-history-probe`, to delete by hand afterwards. How to run it, and what to write down from
each step, is in the file's header; plan §7.5 says what each answer decides.
