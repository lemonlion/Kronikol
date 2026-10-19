# LightBDD file attachments in Kronikol's report: measured 2026-10-19

`<probe>` = `<scratch>\lightbdd-probe`

## Versions

| Package | Version | Source |
|---|---|---|
| Kronikol.LightBDD.xUnit3 | 4.27.0 (newest on nuget.org; built from commit 6f6043e2) | nuget.org |
| Kronikol.LightBDD.Core, Kronikol.xUnit3, Kronikol | 4.27.0 (transitive) | nuget.org |
| LightBDD.XUnit3 | 3.12.0 (what Kronikol.LightBDD.xUnit3 4.27.0 declares; the wiki pins it too) | nuget.org |
| LightBDD.Framework, LightBDD.Core | 3.12.0 (transitive) | nuget.org |
| xunit.v3 | 3.2.2 | nuget.org |
| xunit.runner.visualstudio | 3.1.5 | nuget.org |
| Microsoft.NET.Test.Sdk | 18.3.0 | nuget.org |
| Kronikol.Tool (`kronikol query`) | 4.27.0, `--tool-path <probe>\tool` | nuget.org |

The SDK was 10.0.300, the runtime .NET 10.0.11 and the target net10.0, run with `dotnet test --no-build` (VSTest adapter). `nuget.config` clears every
source but nuget.org and restores into `<probe>\packages`. Every Kronikol and LightBDD package's
`.nupkg.metadata` says `"source":"https://api.nuget.org/v3/index.json"`, so no locally built copy was used.

LightBDD.Framework's `FileAttachmentsManager`, `FrameworkConfigurationExtensions` and `FilePathHelper` decompile
identically in 3.12.0 and 3.12.1 (`decompiled/fam-*.cs`, `fce-*.cs`, `fph-*.cs`), so the result holds for 3.12.1.

## Configuration code

Shared step (`Shared/Attachments_Feature.cs`), one scenario, one step. It attaches both files, logs what LightBDD returned,
and throws:

```csharp
await StepExecution.Current.AttachFile(async m => text = await m.CreateFromFile("probe-notes", textSource)); // .txt, moved in
await StepExecution.Current.AttachFile(async m => png = await m.CreateFromData("probe-screenshot", "png", Png1x1));
throw new InvalidOperationException("Probe failure: this step attached a text file and a PNG, then failed");
```

**A**: Kronikol's documented setup, verbatim, with nothing about attachments (`ProbeA/ConfiguredLightBddScope.cs`):

```csharp
[assembly: TestPipelineStartup(typeof(Probe.ConfiguredLightBddScope))]
public class ConfiguredLightBddScope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "LightBDD attachments probe A"
        });
    }
}
```

The attachments manager needs no call. `LightBddScope.Configure()` starts from `WithFrameworkDefaults()`
(LightBddScope.cs:78), which calls `RegisterDefaultFileAttachmentManager()` (FrameworkConfigurationExtensions.cs:27):
`new FileAttachmentsManager("~\Reports")`, i.e. `AppContext.BaseDirectory\Reports`. Kronikol's
`CreateStandardReportsWithDiagrams` clears LightBDD's writers but leaves the manager alone. Kronikol's
`ReportsFolderPath` default `"Reports"` resolves against `AppDomain.CurrentDomain.BaseDirectory`, so in A the two
folders are the same folder.

**B**: A plus a LightBDD attachments folder that is not Kronikol's reports folder (`ProbeB/ConfiguredLightBddScope.cs`):

```csharp
configuration.ReportWritersConfiguration()
    .UpdateFileAttachmentsManager(new FileAttachmentsManager("~/LightBddAttachments"));
```

**C (control, not asked for)**: B, plus, in the same step, Kronikol's own API given LightBDD's absolute path:
`Kronikol.Tracking.Track.Attachment(text.FilePath, ...)` and the same for the PNG (`#if PROBE_CONTROL`).

## What LightBDD wrote (from the step's own log, `probe-attachments.log`)

| | FilePath (absolute, where the bytes are) | RelativePath (what Kronikol maps) |
|---|---|---|
| A txt | `<probe>\ProbeA\bin\Debug\net10.0\Reports\8186c21b-4b6d-4745-8bdf-32db784309fd.txt` (22 B) | `8186c21b-4b6d-4745-8bdf-32db784309fd.txt` |
| A png | `<probe>\ProbeA\bin\Debug\net10.0\Reports\02c60087-8862-4d55-82ff-f878aecf7d8a.png` (70 B) | `02c60087-8862-4d55-82ff-f878aecf7d8a.png` |
| B txt | `<probe>\ProbeB\bin\Debug\net10.0\LightBddAttachments\a1382d15-4046-4f21-9264-7ca0f99db3c3.txt` | `a1382d15-4046-4f21-9264-7ca0f99db3c3.txt` |
| B png | `<probe>\ProbeB\bin\Debug\net10.0\LightBddAttachments\83075fec-1b08-4532-a386-02d7b81954c9.png` | `83075fec-1b08-4532-a386-02d7b81954c9.png` |

The display name is not used for the file. LightBDD names every file `{Guid}{ext}`, and `CreateFromFile` moved the source
(`sourceStillThere=False`, `removeOriginalFile` defaults to true). The working directory under `dotnet test` was
the bin folder.

## First run

| | A (LightBDD folder = Reports) | B (LightBDD folder = LightBddAttachments) |
|---|---|---|
| Kronikol reports folder | `<probe>\ProbeA\bin\Debug\net10.0\Reports` | `<probe>\ProbeB\bin\Debug\net10.0\Reports` |
| Path the report records (Failures.md, Failures.jsonl `attachments[].path`) | `8186c21b-….txt`, `02c60087-….png` (bare names, unchanged) | `a1382d15-….txt`, `83075fec-….png` (bare names, unchanged) |
| Relative to the folder holding TestRunReport.html, does it name a file? | **Yes**, both (`Reports\<guid>.ext`), so the link works | **No**, neither (`Reports\<guid>.ext` does not exist), so the link is dead |
| `<reports>\attachments\` | does not exist | does not exist |
| `Run.json` `files` | the 10 outputs: ComponentDiagram.html, Failures.jsonl, Failures.md, History.run.json, Specifications.html, Specifications.yml, TestRunReport.html, TestRunReport.json, TestRunReport.labs.html, TestRunReport.schema.json | the same 10 |
| `Run.json` `attachments` | `[]` | `[]` |
| Diagnostics (`kronikol query summary`) | only `HistoryUnavailable` (no .git above) | only `HistoryUnavailable`; nothing says the attachments are missing |

The HTML writes `href`/`src` as `attachment.RelativePath` verbatim (ReportGenerator.cs:2273-2277, 3288-3292,
3876-3881 at 6f6043e2), so the link is the recorded path. Per the hard rule, the HTML itself was not opened.

## Second run (rotation)

| | A | B |
|---|---|---|
| First run moved under `<reports>\runs\<id>\`? | **Yes**: `runs\local_20261019T123728Z_11497a1c\` with exactly the 10 listed files + Run.json | **Yes**: `runs\local_20261019T123829Z_76900fb9\`, the same 11 files |
| First run's attachment files moved with it? | **No.** Both stay at `Reports\` top level, beside the new run's two (4 GUID files there; 6 after a third run). Nothing rotates or prunes them | **No.** They stay in `LightBddAttachments\` (4 files after two runs) |
| Retained run's recorded path, relative to `runs\<id>\` | `runs\…\8186c21b-….txt`: **missing**; `runs\…\02c60087-….png`: **missing**. The retained report's links are dead, though the files exist two levels up | **Missing**. They were dead from the start |
| Newest run's links | work (new GUID files at `Reports\`) | dead |
| `Failures.md` of run 2 | opens with "The run before this one failed ... kept, whole, in `runs/<id>`" | same |

`kronikol query failures <reports> --run <id>` and `query steps` print each attachment resolved against the run's
folder (`…\Reports\runs\<id>\8186c21b-….txt`). In B, and in A's retained run, that is a path to nothing, and the tool
gives no sign of it.

## Control C: Kronikol given the absolute path

| | C, first run | C, second run |
|---|---|---|
| Recorded path | `attachments/d3894a83-….txt`, `attachments/d8ba9c8c-….png` (the native bare entries for the same files are still there, still dead) | |
| `<reports>\attachments\` | holds both copies | the new run's copies |
| `Run.json` `attachments` | `["attachments/d3894a83-….txt", "attachments/d8ba9c8c-….png"]` | the new run's two |
| Retained run | | `runs\local_20261019T124036Z_dd06b89c\attachments\` holds both. Its recorded `attachments/…` paths **exist** relative to the retained folder |

So the copier, the manifest and the rotation work for a LightBDD run once they are handed the absolute path.
Mapping `a.FilePath` in FeatureResultExtensions.cs:237-238 (plan F3) is enough, at least for the case measured here.

## Side observations

1. Wiki `Step-Tracking.md:371` gives the `attachments/` skip as "to avoid double-copying from frameworks like LightBDD
   that manage their own attachment copies". LightBDD 3.12.x never produces an `attachments/` path: its RelativePath is
   always the bare `{Guid}{ext}` relative to its own folder. The code comment (ReportGenerator.cs:6104-6111) gives the
   real reason: the same model is processed twice in one process.
2. `.generation-complete` follows the working directory. Kronikol registers LightBDD's writer at
   `"Reports/.generation-complete"` (Kronikol.LightBDD.Core ReportWritersConfigurationExtensions.cs:79-83). LightBDD's
   `FilePathHelper.ResolveAbsolutePath` resolves a relative path without `~` against `Directory.GetCurrentDirectory()`.
   Running `ProbeA.exe` from `<probe>` created a stray `<probe>\Reports\.generation-complete` (0 bytes), while the
   report went to the bin folder. Under `dotnet test` the working directory is the bin folder, so the two coincide. The
   stray folder was deleted.
3. In A, the top-level `Reports\` collects a pair of GUID files per run, never listed, moved or pruned.

## Files

- Sources: `nuget.config`, `Shared/Attachments_Feature.cs`, `ProbeA/`, `ProbeB/`, `ProbeC/` (csproj + scope).
- Console output of every run: `logs/A-run1.txt`, `A-run2.txt`, `A-run3-exe-from-probe-root.txt`, `B-run1.txt`,
  `B-run2.txt`, `C-run1.txt`, `C-run2.txt`.
- Decompiled LightBDD.Framework snippets, 3.12.0 and 3.12.1: `decompiled/fam-*.cs` (FileAttachmentsManager),
  `fce-*.cs` (FrameworkConfigurationExtensions), `fph-*.cs` (FilePathHelper).
- Removed after measuring: every `bin/` and `obj/`, the restored package cache `packages/`, the local tool `tool/`, and
  the full decompiled projects.
