# AZURE_DEVOPS_PARITY_PLAN harness

The scripts behind the numbers in [`../AZURE_DEVOPS_PARITY_PLAN.md`](../AZURE_DEVOPS_PARITY_PLAN.md) that
could be taken without an Azure DevOps organisation, with the output they printed on 2026-09-27 at
`e4c9e36` (3.31.9), SDK 10.0.401, Linux; and the probe pipeline for the half day that needs an
organisation (plan §7.5, S0).

| File | What it answers | Output |
|---|---|---|
| `stdout_matrix.sh` | Plan F1 across the frameworks: whether what the library prints at run end reaches the console of the command that ran the tests, for every example project, the way a pipeline would run it | `results-stdout-matrix.txt` |
| `stdout_channel.sh` | Plan F1 on the one project that posts a summary: whether its `##vso[task.uploadsummary]` line reaches `dotnet test`'s output at four verbosity settings, and what GitHub Actions' file channel receives from the same run | `results-stdout-channel.txt` |
| `f2_rerun_fold.sh` | Plan F2: the run id the library writes for a re-run Azure DevOps job, and what `kronikol history record` then records, today and with the attempt in the id | `results-f2-rerun-fold.txt` |
| `s0-probe.yml` | Plan §7.5: every Azure Pipelines claim the plan rests on and could not run here | none yet: `results-s0.txt` when it is run |
| `research-notes.md` | The evidence the plan was written from: Microsoft's pages and the agent's source (quotes and flags, including what the plan did not need), every provider-specific path in this repository by file and line, and what the other plans already say | (notes, not a script) |

Each script runs from the repository root with the .NET 10 SDK and sets the variables an Azure Pipelines
agent sets (`TF_BUILD`, `BUILD_*`, `SYSTEM_*`). None needs an organisation: an agent reads logging commands
from the output of the step's process and nowhere else, so a line that never reaches that output never
reaches an agent.

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

## `f2_rerun_fold.sh`

The same project, run twice under one build id (`BUILD_BUILDID=5000`) with `SYSTEM_JOBATTEMPT=1` and
then `2`, as "Rerun failed jobs" does to the job it re-runs. The reports directory is emptied first. Each
run's `History.run.json` is kept, as each attempt's artifact would be, and the first is edited to fail one
scenario, because it stands for the attempt that failed and was re-run. Then three folds with the real
`kronikol history record`: (A) today, a fold after each attempt; (B) today, one fold after the re-run; (C)
the second fragment's id set to `ado:5000:2`, what S1 makes the library write. Working paths in the
output are replaced by `<work>`.

Today both attempts are `ado:5000:1`, and the second keeps the first under `runs/ado_5000_1` as an earlier
attempt of the same run. A records the failure and throws the re-run away as a duplicate ("22 scenarios
from 2 shards (already in the ledger)"); B records one run of 22 scenarios, the same eleven twice; C
records two runs, the second all green.

## `s0-probe.yml`

A throwaway pipeline for a scratch Azure DevOps project. It reads no secret and prints none; the one token
it uses is the job's own `System.AccessToken`, mapped into three steps' environments. It pushes one orphan
branch, `kronikol-history-probe`, to delete by hand afterwards. How to run it, and what to write down from
each step, is in the file's header; plan §7.5 says what each answer decides.
