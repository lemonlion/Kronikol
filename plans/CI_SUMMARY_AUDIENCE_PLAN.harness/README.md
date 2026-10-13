# CI_SUMMARY_AUDIENCE_PLAN harness

`probe.cs` calls the public builders a run and `kronikol merge` call for the CI job summary and the GitHub annotation,
on the published Kronikol 4.18.1, with the inputs a run hands them. Run it from this folder with the .NET 10 SDK:

```bash
dotnet run --file probe.cs > probe.out.txt
```

`probe.out.txt` is its output from 2026-10-13 (Windows 11, SDK 10.0.300). The cases, as the plan cites them:

| Case | What it shows |
|---|---|
| A | The issue's failing run: the "Debug this run" section, word for word as #169 quotes it |
| B | The same run's `::notice` annotation, carrying the runner's folder |
| C | A green run with `WriteCiSummary`: the section `CiSummary.md` and the job summary end with, "Debug this run" included |
| D | `WriteCiSummary` with 11 failures and the default `MaxCiSummaryDiagrams` of 10: "see full report" and "Do not open `TestRunReport.html`" in one summary |
| E | A run that wrote no HTML: the rule still names `TestRunReport.html` |

The probe ran on Windows, so `Path.Combine` joined `query.cs` to the POSIX folder with a backslash in cases A and C. On a
Linux runner the separator is `/`; nothing in the plan rests on it.

Added by the plan's second pass, the same day:

| File | What it shows |
|---|---|
| `off-runner-checks.md` | `gh run download` and `gh run view` against this repository's run 38400333756 (the layout, a second download into the same folder, the host form of `--repo`, notices); `kronikol query` on a custom-named data file and on two reports in one download; `dotnet tool install -g` when the tool is already there |
| `ado-stdout.sh`, `ado-stdout.out.txt` | With `TF_BUILD=True`, whether the `##vso` logging commands Kronikol writes from the run-end hook reach `dotnet test`'s stdout, where an Azure DevOps agent would read them (F10). Run from the repository root |

Added by the release that implements the plan (`r1/`, R1 to R3 in one patch):

| File | What it shows |
|---|---|
| `r1/red-stub.txt` | The new and moved facts on the released 4.19.0 with the new internal entry points stubbed to 4.19.0's text: which fail, each on its own assertion, and which pass by design |
| `r1/mutations.txt` | Each mutation of the release, applied alone, and the facts that caught it |
| `r1/mutate.pl` | The mutations, as exact text replacements; `perl r1/mutate.pl list` names them |
| `r1/probe.cs`, `r1/probe.out.txt` | The text the released builders write for the issue's probe on GitHub (failing on a second attempt, and green), on GitHub without a run id, on Azure DevOps and off CI. Run it from this folder with the .NET 10 SDK: `dotnet run --file r1/probe.cs` |
| `r1/checks.txt` | The suites and builds run before the release, and the run of `ci-summary-preview.yml` the plan's section 5 asks for |
