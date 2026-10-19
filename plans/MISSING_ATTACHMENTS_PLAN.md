# Missing attachments plan (#148)

**Status:** written 2026-10-19 at 4.27.0 (`6f6043e2`) by kronikol-7c, at the owner's ask ("critically analyse issue
148 and create a detailed, well researched plan to fix its underlying issue(s)"). Revised the same day after a
verification pass the owner asked for ("make sure the plan is complete and fully accurate, and do any deep dives that
might change the plan"): every line reference re-read, five deep dives run (section 10), four findings added (F20 to
F23), five corrected (F2, F3, F5, F8, F10), and the R2 and merge designs changed. **Green-lit 2026-10-19**: the owner
asked for it to be implemented in full, and every question in section 8 was taken as recommended (the precedent of
`REPORT_FORMAT_PLAN.md`). R1 and R2 ship as one patch (Q2); section 10 logs each release.

**Issue:** [#148](https://github.com/lemonlion/Kronikol/issues/148), "An attachment whose file is missing when the
report is drawn keeps its original path, and no diagnostic says so" (filed 2026-10-08 against 4.6.0, label `bug`, no
comments). `ROADMAP.md` Appendix C lists it among `ATTACHMENT_MERGE_PLAN`'s leftovers.

**Harness:** `plans/MISSING_ATTACHMENTS_PLAN.harness/`, sources and results only (no binaries; scratch paths written
`<scratch>`):

| Folder | What it measured |
|---|---|
| `cwdprobe/` | the working directory under xUnit v3 3.1.0 and TUnit 1.53.0 (section 2.4) |
| `cwdprobe2/` | the same under MSTest 4.5.1, NUnit 5.0.0 and xUnit v2 2.9.3, on VSTest and Microsoft.Testing.Platform (section 2.4) |
| `lightbdd/` | a real LightBDD run on the published Kronikol.LightBDD.xUnit3 4.27.0 (F3) |
| `ingest/` | `kronikol ingest` on the published Kronikol.Tool 4.27.0: the issue's case, a blank path, a NUL path, `--clean-attachments` (F1, F8, F15, F16) |
| `pathprobe/` | `File.Copy` onto itself, `GetFullPath` on a NUL path and a relative base, URL escaping (F8, F10, F11) |

Windows 11, .NET SDK 10.0.300. Line numbers are to `6f6043e2` (4.27.0). Wiki lines are on `origin/master` `21914fa`.
LightBDD lines are from `ilspycmd` on `LightBDD.Framework` 3.12.0 and 3.12.1 (identical in the parts cited). Each finding
says how it was established: **measured** (run, with the result in the harness), **read** (the code was read and the
path traced by hand), or **reported** (found by a read-only sweep and not re-read line by line; only a few
side-findings in sections 3 and 9 are left so, and each says so).

---

## 1. Summary

The issue is right about what it saw, and what it saw is one case of a wider defect.

What it saw holds at 4.27.0, unchanged since the copier was written (`bd8537f2`, 2026-05-11), and was reproduced on
the published 4.27.0 tool (harness `ingest/`): `ReportGenerator.ProcessAttachments` keeps an attachment whose file
does not exist exactly as it was given and records nothing (`ReportGenerator.cs:6138-6142`). The HTML then links a path
that exists on no machine opening the report, `Failures.md` lists it like any other, and `kronikol query` prints no `!`
line.

The wider defect: **the model has no way to say "the report links a file it does not hold"**. `FileAttachment` has
one path field, which means three different things after the copier has run: a file inside the report
(`attachments/x.png`), a link elsewhere (`https://…`), or whatever the producer gave when nothing could be copied (an
absolute path on the producing machine, or a relative path resolved against the wrong folder). The copier knows which
of the three it produced and throws that knowledge away, so every reader after it (HTML, data files, `Failures.md`,
`Failures.jsonl`, `query`, `kronikol merge`, the CI upload) prints the third case as if it were the first. Section 3
lists 23 findings in that class. In 6 of them a file that **is** on disk ends up unreachable from the report, and in
2 the report shows the **wrong** file: a merged report can show another shard's screenshot under a missing one's name
(F5), and a file rewritten after it was attached is shown as its last version in every scenario that attached it
(F23).

Three points the issue does not raise change the order of the fix:

1. **The diagnostic alone would be wrong for LightBDD (measured).** Kronikol maps a LightBDD attachment's bare file name
   (`{guid}.png`), not its path (F3). In Kronikol's documented setup LightBDD writes into `<bin>/Reports`, the report's
   own folder, so the bare name happens to work as a link from the HTML, but the copier resolves it against `<bin>`,
   finds nothing, and keeps it. A "not found" diagnostic shipped first would report every LightBDD attachment as missing
   while its link works. The producers have to hand the copier a path it can find before the copier is allowed to say
   "not found" (R1 before R2).
2. **On 4.x the diagnostic alone does not reach a person reading the report.** The HTML's diagnostics block is behind
   `ShowReportDiagnosticsSection`, off by default (`ReportConfigurationOptions.cs:462-472`); an in-process run prints
   none of the recorded entries to the console (F22); `Failures.md` shows `ResultDefaulted` and no other kind
   (`FailuresDigestGenerator.cs:562-563`). The `!` line reaches an agent using `query`; a reviewer opening the HTML still
   sees a broken image. On the `v5` branch the option defaults to true and a run with any entry gets a Diagnostics tab
   with a count (`v5:src/Kronikol/ReportConfigurationOptions.cs:472`, `v5:src/Kronikol/Reports/ReportGenerator.cs:1899`,
   reported), so from 5.0.0 R2's entry is visible by default. Even then the reviewer looks at the failed scenario's
   screenshots, not at a tab, so the issue's "also worth having" (mark the attachment where it is drawn) is the half
   that answers its own story ("a reviewer asked where the screenshots were"). It is R3.
3. **The docs already promise the fix.** `DiagnosticKind.AttachmentFailure` says "A capture referenced an attachment
   that could not be **found** or copied" (`DiagnosticEntry.cs:29`); the skill lists "a missing attachment" among the
   diagnostics that print a `!` line (`.claude/skills/kronikol-test-debugging/SKILL.md:182-188`). No code records one.
   Making a shipped doc comment true is a bug fix, so R2 is a patch.

Four releases (section 5): **R1** (patch) the producers hand the copier a path it can find, and the page links what it
was given; **R2** (patch) a missing, unusable, uncopied or changed file is said, once per run, in the run's diagnostics
and in `kronikol merge`'s; **R3** (minor) the state is kept on the attachment and every reader shows it instead of a
dead link; **R4** (patch) an Azure DevOps upload carries the report's `attachments/` folder, which overlaps
`AZURE_DEVOPS_PARITY_PLAN.md` (Q8).

---

## 2. The issue, checked

### 2.1 Claims that hold

| Issue's claim | At 4.27.0 | How |
|---|---|---|
| `ResolveAttachmentPath` resolves against `AttachmentsBase` and never checks the file exists | Holds: `FeatureSynthesizer.cs:367-374` | read, measured (`ingest/` case1) |
| `CopyAttachmentsToReportsFolder` leaves a missing file unchanged, as its comment says | Holds: comment `ReportGenerator.cs:6046-6051`, code `:6138-6142` | read, measured |
| The HTML gets `<img src="/home/runner/…">` | Holds: the HTML writes `RelativePath` verbatim into `src` and `href` (`ReportGenerator.cs:2273, 2277, 3288, 3292, 3876, 3881`), HTML-encoded and nothing else | read |
| `Failures.md` lists the path like a copied one | Holds: `FailuresDigestGenerator.cs:791-798` prints `RelativePath` with no state | read, measured |
| `query summary` prints no `!` line | Holds: nothing is recorded, so `WriteProvenance` (`QueryCommand.cs:358-373`) has nothing to print | read, measured |
| `AttachmentFailure` exists, used by `CleanAttachmentsFolder` | Holds (`IngestPipeline.cs:1205`); also used twice by the copier itself (`ReportGenerator.cs:6133`, `:6167`) | read |

A test pins the silence: `AttachmentCopyTests.Missing_source_file_is_skipped_without_error`
(`tests/Kronikol.Tests/Reports/AttachmentCopyTests.cs:53-65`) asserts the path is kept and nothing is recorded about
it. R2 turns it round.

### 2.2 What the issue's suggested message would get wrong

The issue proposes `AttachmentFailure: 5 attachment file(s) were not found when the report was drawn:
tests/test-results/…/test-failed-1.png, …`, naming the path as the reporter wrote it. By the time the copier runs,
`FeatureSynthesizer` has already made it absolute against `--attachments-base` (or the working directory), and the text
the reporter wrote is gone (measured: `ingest/` case1 records `…\base\tests\test-results\a-retry1\test-failed-2.png`).
Two consequences:

- The message names the **resolved** path, which is the more useful one for the issue's own cause (it shows where the
  copy was missing a folder). For an ingest it also names the base it was resolved against, since a wrong base is the
  other common cause and the absolute path alone does not show it.
- A run with thousands of missing screenshots (a capture copied without `test-results/`, the issue's own case at
  scale) must not write thousands of paths into one message. The message gives the count, the number of distinct
  files, the first five, and "and N more", the shape `kronikol ingest` already prints for malformed lines
  (`IngestCommand.cs:547-564`).

### 2.3 Where the issue's "Expected" would and would not be seen

See section 1, point 2. Read: `ShowReportDiagnosticsSection` defaults to false at 4.27.0; the labs page always shows the
block when there is an entry (`LabsReportGenerator.cs:68-69`) but is not the report people open; `kronikol ingest`
prints each entry (`IngestCommand.cs:547-564`); an in-process run prints none (F22). The `!` line shows only the
**first** message of a kind, cut to 160 characters, with `(×N)` counting entries, and with no kind name in front
(`QueryCommand.cs:413-424`); `summary`'s inventory cuts it to 90 (`QueryCommand.Overview.cs:174-187`). Two design
rules follow:

- The message must lead with the count and say what it is about in its own words: a path first would be cut before
  the count was read, and nothing else on the line says "attachment".
- There must be **one** entry per run. Today a failed copy records an entry during the walk (`:6167`). Were R2 to add a
  not-found entry after it, the `!` line would show the copy failure's message and hide five hundred missing files
  behind `(×2)`.

### 2.4 The working directory: one live case (measured)

`Track.Attachment` stores a relative path as given (`StepCollector.cs:243-266`), and the copier resolves it against
`AppDomain.CurrentDomain.BaseDirectory` at the end of the run (`ReportGenerator.cs:6129`). The wiki's own example writes
a file with `File.WriteAllText("response.json", …)`, relative to the **working directory**, and attaches
`"response.json"` (`Step-Tracking.md`, "File Attachments"). Wherever the two folders differ, every such attachment is
silently lost.

Measured (harness `cwdprobe/`, `cwdprobe2/`), each from a folder other than the binary folder: xUnit v3 3.1.0 (VSTest,
`dotnet run`, the exe), TUnit 1.53.0 (`dotnet run`, the exe), MSTest 4.5.1 (VSTest; Microsoft.Testing.Platform by
`dotnet run` and the exe; `[DeploymentItem]`), NUnit 5.0.0 with NUnit3TestAdapter 6.3.1 (VSTest, also from an unrelated
folder and with an empty runsettings; MTP by `dotnet run` and the exe), xUnit v2 2.9.3 (VSTest). **In every default mode
the working directory was the binary folder** while the test ran: VSTest's host starts there, MSTest on MTP moves there
for the run (and moves back to the launch folder before the process exits), NUnit moves to its `WorkDirectory`, which
defaults to the binary folder.

**The live case:** NUnit's `<WorkDirectory>` runsettings setting moves the working directory to that folder, on VSTest
and on MTP alike. A test that writes `response.json` and attaches it by that relative name has its file in the work
directory and the copier looks in the binary folder. That is F20. A test that changes the working directory itself
does the same. The fix (R1) resolves a relative path when it is **attached**, not at the end of the run.

---

## 3. Findings

Kind: **dead** = a file that exists ends up unreachable from the report; **wrong** = the report shows a different file
or a different version than the one attached; **silent** = a link to nothing, or an attachment dropped, with no word
anywhere; **crash** = an exception that ends the run's report; **misleading** = a diagnostic that names the wrong cause;
**doc** = a shipped text that says otherwise.

| # | Finding | Where | How | Kind | Release |
|---|---|---|---|---|---|
| F1 | A missing source file is kept as given and nothing is recorded (the issue) | `ReportGenerator.cs:6138-6142` | measured | silent | R2 |
| F2 | A path starting `attachments/` is taken to be this run's copy; when no such file is in the reports folder it is kept and nothing is recorded. Reaches an in-process `Track.Attachment("attachments/x.png")` and a relative Cucumber `url` (F4); **not** a tests-file record, which `FeatureSynthesizer` makes absolute first (measured, `ingest/` case1) | `ReportGenerator.cs:6104-6121` | read, measured | silent | R2 |
| F3 | LightBDD: Kronikol maps `a.RelativePath`, which LightBDD sets to the bare file name (`{guid}.png`), relative to LightBDD's attachments folder; the absolute `a.FilePath` is ignored. The copier resolves the bare name against `<bin>`, finds nothing, keeps it. In the documented setup LightBDD's folder is `<bin>/Reports`, the report's own, so the link works by accident; the file is never copied, `Run.json` lists `"attachments": []`, and when the run is kept under `runs/<id>/` its links are dead while the files stay at the top of `Reports/` for ever. With LightBDD's folder set elsewhere every link is dead. Mapping `FilePath` fixes both (a control run passing it through `Track.Attachment` was copied, listed and moved) | `src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs:236-238`; LightBDD `FileAttachmentsManager` | measured (`lightbdd/`) | dead | R1 |
| F4 | Cucumber messages: an attachment `url` that is not an absolute `file:` URI (`shot.png`, `attachments/shot.png`) is passed on as is (`ToLocalPath`), and the copier resolves it against the **tool's install folder** (`kronikol ingest`'s base directory); the join's byte hashing opens the same path against the working directory (`CucumberFeatureMerger.cs:275-276`, reported), so the two disagree. `--attachments-base` is documented for `--tests` only (`IngestCommand.cs:698`) | `CucumberFeatureSynthesizer.cs:1170-1174, 1206-1211` | read | dead | R1 |
| F5 | `kronikol merge`: a shard's `attachments/x.png` that is not in the shard's folder stays relative, so beside the merged report it names `<merged>/attachments/x.png`: nothing, or **another shard's `x.png`** that the carrier placed under that name. A copy that fails keeps the absolute path. Nothing is recorded | `MergeableReportRenderer.cs:257-271, 280-321` | read | wrong | R1 (path), R2 (said) |
| F6 | A copy that fails keeps the producing machine's absolute path in the page. The diagnostic exists, but the HTML links `C:\…\shot.png`, which a browser reads as a URL with the scheme `c:` | `ReportGenerator.cs:6163-6169` | read | silent | R3 |
| F7 | Azure DevOps: the upload names the top-level report files and the runs kept under `runs/`, but not the report's own `attachments/` folder, so every copied attachment is a dead link in the artifact. The same for a merged report (`MergedRunOutputs.cs:179`). GitHub Actions uploads the whole folder | `CiArtifactPublisher.cs:18, 26-31, 58-73`; call `ReportGenerator.cs:751-753` | read | dead | R4 |
| F8 | `ResolveAttachmentPath` calls `Path.GetFullPath` unguarded. A path with a NUL character ends `kronikol ingest` with an unhandled `ArgumentException` and no report (measured: `ingest/` case2, exit 127); a relative `IngestRequest.AttachmentsBase` from a library caller throws the same way (measured in `pathprobe/`; the CLI makes the base absolute, `IngestCommand.cs:431`). `IngestCommand` catches only `FormatException` and `FileNotFoundException` (`:483-492`), and nothing above it catches: `Program.cs` returns `Commands.Dispatch` (`Commands.cs:63`) unguarded | `FeatureSynthesizer.cs:373` | measured | crash | R1 |
| F9 | `Directory.CreateDirectory(attachmentsDir)` sits outside the copy's `try`: a file named `attachments` in the reports folder throws out of report generation (the merge's carrier creates its folder inside its `try`) | `ReportGenerator.cs:6157` | read | crash | R1 |
| F10 | An absolute source already inside `<reports>/attachments/` is copied onto itself. On Windows `File.Copy` throws "The process cannot access the file … because it is being used by another process" (measured, `pathprobe/`; the file is intact; Linux not measured), so the run records that false cause as an `AttachmentFailure`, links the absolute path, and leaves the file out of `Run.json`. The merge handles the same case (`MergeableReportRenderer.cs:297-298`) | `ReportGenerator.cs:6153-6169` | measured | misleading | R1 |
| F11 | A file name with `#`, `?` or `%` is copied under the same name and linked HTML-encoded but not percent-encoded, so `attachments/shot#1.png` requests `attachments/shot` | `ReportGenerator.cs:2273-3881` (six sites), `:6153-6177` | read | dead | R1 |
| F12 | The page has no fallback for an image that fails to load (no `onerror`, no listener; the only error handlers in the report's scripts are on other elements); the lightbox copies the same broken `src` (`report-lightbox-function.js:9`). Every image in `AttachmentRenderingTests` points at a `files/…` path that does not exist (`tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs:1997, 2013-2014`) and no fact checks that an image drew, so the suite cannot see a broken image | `report-lightbox-function.js`; `AttachmentRenderingTests.cs` | read | silent | R3 |
| F13 | `Failures.md`, `Failures.jsonl`, `query failures`, `query steps` and their `--json` print the path with no state (`FailuresDigestGenerator.cs:791-798, 981-983`; `QueryCommand.Narrative.cs:126-129, 144-146, 347-348`). `AttachmentEntry.Resolve` (`ReportIndex.cs:338-350`) joins the path to the report's folder; for a Windows path read on Linux, which .NET does not read as rooted, that invents `<dir>/C:\…`. Existence at query time is the wrong question (the query may run elsewhere); the state at draw time is the right one, and nothing keeps it | as listed | read | silent | R3 |
| F14 | The data schema says `relativePath` is a "Path relative to the report directory (attachments/<file>), or an absolute URL", false whenever F1 to F6 apply | `ReportGenerator.cs:6412` | read | doc | R3 |
| F15 | `--clean-attachments` empties `<reports>/attachments/` before the copier runs, so a capture that points into that folder loses its files before they are read (measured, `ingest/` case3: deleted, linked, nothing recorded). With `--allow-empty` and no scenario it empties the folder and writes no report, so the previous report keeps its links to deleted files (`IngestPipeline.cs:566-567`, reported) | `IngestPipeline.cs:578, 1191-1208`; order `ReportGenerator.cs:400, 463` | measured | dead | R2 (said), Q6 (kept) |
| F16 | A blank `path` in a tests-file `attachment` record is dropped with no word | `FeatureSynthesizer.cs:325-327` | measured | silent | R2 |
| F17 | A Cucumber attachment body that cannot be written (an I/O error, or bad base64) is dropped and recorded as `Other`, not `AttachmentFailure` | `CucumberFeatureSynthesizer.cs:1199-1202`, `IngestPipeline.cs:455-456` | read | silent | R2 |
| F18 | Docs that say the old behaviour or nothing: wiki `Step-Tracking.md:334` ("Attachments appear as download links"), `:358` (a relative `filePath`, relative to nothing named), `:371` ("Files that don't exist on disk are left unchanged", and the `attachments/` skip explained as "avoid double-copying from frameworks like LightBDD", which never produce such a path; the real reason is `ReportGenerator.cs:6104-6111`), `Generated-Reports.md:546` ("downloadable links"), `Diagnostics-and-Debugging.md:107` and `Ingesting-External-Captures.md:670` ("could not be copied or deleted"); `Track.cs:161-162` ("rendered as a download link"); the `--attachments-base` help (`IngestCommand.cs:698`) | as listed | read | doc | R1, R2, R3 |
| F19 | `DiagnosticKind.AttachmentFailure`'s doc comment and the skill's `!` line list promise a "not found" entry no code records | `DiagnosticEntry.cs:29`; `SKILL.md:182-188` | read | doc | R2 |
| F20 | A relative `Track.Attachment` path is resolved against the binary folder at the end of the run, while the test wrote the file relative to the working directory when it attached it. They differ under NUnit's `WorkDirectory` (VSTest and MTP) and after a test changes the working directory | `StepCollector.cs:243-249`; `ReportGenerator.cs:6129` | measured (`cwdprobe2/`) | dead | R1 |
| F21 | An image attached under a display name with no extension renders as a link, not an image: `IsInlineImage` reads only the **name's** extension when no media type is given (`FileAttachment.cs:31-42`). Every LightBDD image (its names carry no extension and the mapping passes no media type), and `Track.Attachment(path, "Page Screenshot")`, the wiki's own example (`Step-Tracking.md:345`) | `FileAttachment.cs:31-42`; `FeatureResultExtensions.cs:237` | read | silent | R1 |
| F22 | `ShowReportDiagnosticsSection`'s doc comment says every diagnostic "still reaches … the console"; an in-process run prints none of the recorded entries (the run summary takes none, `RunSummaryConsoleWriter.Summarise`; `ReportGenerator.cs:654` prints only `ReportDiagnostics.Analyse`'s lines) | `ReportConfigurationOptions.cs:462-471` | read | doc | R2 |
| F23 | The copy is made at the end of the run, from wherever the path then points (`ReportGenerator.cs:463`), and once per source path (`:6146`). A file deleted after it was attached (a temp folder disposed at teardown) is lost with no word; a file rewritten after it was attached (one `screenshot.png` path reused by every test) is shown as its **last** version in every scenario that attached it | `StepCollector.cs:249`; `ReportGenerator.cs:463, 6146` | read | wrong | R2 (said), Q9 (kept) |

Not in this plan's class, recorded for `ROADMAP.md` Appendix C (section 9).

---

## 4. Design

### 4.1 One rule, in one place, for both writers

Two code paths decide where an attachment ends up: the run's copier (`ReportGenerator.ProcessAttachments`, every
in-process adapter and `kronikol ingest`) and `kronikol merge` (`MergeableReportRenderer.PointAttachmentsAtTheirShard`
and `CarryAttachments`). They already differ: the merge links a file already in its folder (F10), creates its folder
inside its `try` (F9), never overwrites, reuses a copy with the same bytes, and numbers a clash `name-1.ext`; the run
copier overwrites, keeps one copy per source path, and numbers a clash `name_2.ext` (`:6183-6199`). The plan gives
them one internal helper that answers, for one attachment, **where it now points and in which state**, and both call
it. A parity fact runs the same inputs through both (the 3.30.1 lesson: two implementations of one rule get a parity
fact). The naming rules stay as they are: changing either would rename files in every report with a clash.

States, after the copier:

| State | Meaning | Path kept |
|---|---|---|
| (none) | In the report (`attachments/…`), or a URL | as today |
| `notFound` | No file at the resolved path when the report was drawn | the resolved path (where it looked) |
| `notCopied` | The file is there but could not be copied (held, disk full, unreadable) | the resolved path |
| `notUsable` | The path is not a legal path on this machine | as given |

### 4.2 The diagnostic (R2)

**One** `AttachmentFailure` entry per run (and per merge), written after the walk, covering every state, because the
`!` line shows only the first message of a kind (section 2.3). A run with 5,000 missing screenshots writes one entry.
It leads with the count:

```
7 attachment(s) are not in the report: 5 not found when the report was drawn (3 files), 1 could not be copied (IOException: …), 1 has a path that is not usable: /home/runner/work/r/r/tests/test-results/a-retry1/test-failed-1.png, …, and 2 more
```

- The parts with a count of zero are left out; the first five paths are the first five in report order, whatever their
  state.
- For `kronikol ingest`, a second sentence names the base when any path was relative in the capture: `Relative paths
  were resolved against /home/runner/work/r/r (--attachments-base).`
- The copier's two existing per-file entries (`:6133`, `:6167`) fold into it. Facts that pin their old text change
  with it (`AttachmentCopyTests`, and any other found by grepping "Could not copy attachment" and "is not usable").
- An in-process run also prints it to the console as `⚠ WARNING: …`, the way the run already says what it could not do
  with its own files (`ReportGenerator.cs:269`), since nothing else prints a recorded entry there (F22). `kronikol
  ingest` prints it already, as it prints every entry.

**Changed after it was attached (F23).** `StepCollector.AddAttachment` records the file's length and last write time
when it is attached, when the file exists then (an internal init-only field, never serialised). The copier compares
before copying, which splits two cases the user acts on differently: "existed when attached, gone when the report was
drawn" (a teardown deleted it) and "changed after it was attached; the report shows the version it found" (a reused
path). Both go into the one entry. Ingest records carry no such stamp and are unchanged. Copying at attach time
instead, so the report keeps what was attached, is Q9.

**`kronikol merge`.** Merge opens no diagnostics scope, and recording with no scope is a silent no-op
(`DiagnosticEntry.cs:194-195`; `MergedRunOutputs.cs:14-18` says so of the merged report's whole tail). Opening one
would still reach nothing (read 2026-10-19):

- The merged HTML draws only the `diagnostics` it is handed, `report.Diagnostics`
  (`MergeableReportRenderer.cs:34`, the call to `GenerateHtmlReportCore`); the run's own snapshot (`ReportGenerator.cs:480`)
  is in a method the merge never calls.
- `MergeableReport` is a record whose `Diagnostics` is init-only (`MergeableReport.cs:99`).
- The files are carried inside `Render` (`MergeableReportRenderer.cs:68-69`), and `MergeCommand` then writes the data
  file and the rest (`WriteMergedData`, `MergedRunOutputs.Write`) from the `merged` object it already holds.

So the decision moves ahead of `Render`: `MergeCommand` runs the carry (through the 4.1 helper) before it renders, and
puts the entry on the report it holds (`merged = merged with { Diagnostics = [.. merged.Diagnostics, entry] }`), so the
HTML section, the data file's `diagnostics`, the labs page and `query`'s `!` line all read the same list. `Render` keeps
carrying for a library caller that calls it alone (a carried path is relative and is skipped the second time), and
adds the entry to the HTML it draws.

### 4.3 The state on the attachment (R3)

`FileAttachment` is a public positional record. Adding a positional parameter would change its constructor and
`Deconstruct` (a binary break), so the state is an init-only property:

```csharp
public record FileAttachment(string Name, string RelativePath, string? MediaType = null)
{
    /// <summary>Why the report does not hold this file, set by the report's writer; null when it does, or for a URL.</summary>
    public AttachmentProblem? Problem { get; init; }
}

public enum AttachmentProblem { NotFound, NotCopied, NotUsable }
```

A new public member and a new public type: a **minor** (CLAUDE.md). An internal property would avoid the bump, but a
host that draws its own view of the model (a custom writer, or a host that renders a live report, as
`ReportDiagnosticsScope`'s remarks describe) could not read it, and the data file would carry a key no public type
names. Q1 asks. `with` copies it, so the copier's rewrites keep it; record equality includes it, and nothing compares
attachments by value (the Cucumber join keys its origins by reference, `CucumberFeatureSynthesizer.cs:117-118`).

**The schema is closed.** `CloseDeclaredObjects` (`ReportGenerator.cs:6908-6941`) adds `additionalProperties: false` to
every object that declares its properties, the attachment item included. So `problem` must be declared in the same
release, and a consumer validating a new file against a 4.27.0 schema it kept rejects any attachment carrying it. That
is new surface (another reason R3 is a minor) and goes in the changelog. `problem` is in no `required` list.

Every writer and reader of the state:

| Reader / writer | Today | After R3 |
|---|---|---|
| JSON data file (`MapAttachmentJson`, `ReportGenerator.cs:5155-5160`) | `name`, `relativePath`, `mediaType` (written when null) | adds `problem` (`notFound`, `notCopied`, `notUsable`), **absent** when null: the 4.x precedent for an optional key is to omit it (`"assertion": true` on a tracked assertion only, `:5168-5169`, `:5206-5207`), and `REPORT_FORMAT_PLAN.md`'s `.krkl` drops null keys anyway |
| XML (`:5163-5167`), YAML (`:5671-5681`, `:5797-5806`) | the same three | adds `Problem` the same way |
| JSON schema (`Attachment()`, `:6402-6416`) and XSD (`:7017-7030`, `:7129-7140`) | `relativePath` described falsely (F14) | `problem` enum added; `relativePath` described truly |
| Mergeable reader (`MergeableReportReader.cs:393-401`) | reads three keys | reads `problem`, so a shard's state survives the merge |
| HTML, scenario, parameterized panel and step (six sites) | `<img src>` / `<a href>` to the path | with a problem: no `src` or `href`; `<span class="attachment-missing" title="{path}">{Name} (not found when the report was drawn)</span>` (wording per state, Q3) |
| Page script | nothing | one capture-phase `error` listener on the document marks an `img.attachment-image` that fails to load for any other reason (a report folder copied without `attachments/`, F7's case) with the same span, text "could not be loaded". A document listener, not an `onerror` attribute, so it also runs in an Export Filtered HTML copy (the copy is `outerHTML` with its scripts, `report-export-function.js:110-132`). Q5 |
| `Failures.md` (`:791-798`) | `- Name — \`path\`` and, for a step's, ` (step \`s0/2\`)` | the state after the path: `- Name — \`path\` (not found when the report was drawn; step \`s0/2\`)` |
| `Failures.jsonl` (`:981-983`) | `name`, `path`, `step` | adds `problem`, absent when null (the `step` precedent of 4.24.0 kept `formatVersion` 1) |
| `query failures`, `query steps` (`Narrative.cs:126-129, 144-146, 347-348`) and `--json` | `attachment: Name → <Resolve(path)>` | `attachment: Name → <path as recorded> (not found when the report was drawn)`; `--json` adds `problem`. `Resolve` is not applied to a path with a problem: the recorded text is the evidence, and on another OS `Resolve` invents a path (F13) |
| Kronikol4J | writes the path it is given; no copier, no merge copy (`StepCollector.java:200-214`, reported) | never sets a problem, so its output for every input it can produce is unchanged; ledger entries in section 4.6 |

### 4.4 The producers (R1)

- **F3 LightBDD:** map `a.FilePath` (absolute, always set by `FileAttachmentsManager`), falling back to `a.RelativePath`
  only when `FilePath` is blank. The stub fact `ToFeatures_maps_file_attachments` (`FeatureResultExtensionsTests.cs:150-162`)
  pins `"Reports/shot.png"`, a shape no LightBDD build produces; it is turned round to the shape `FileAttachmentsManager`
  writes, and a real-LightBDD fact (the harness's `lightbdd/` scenario as a test) attaches through `AttachFile` and
  asserts the copy, the `Run.json` entry and, after a second run, the kept run's link.
- **F21 the image rule:** with no media type, `IsInlineImage` reads the name's extension, then, for a file and not a
  URL, the path's. That makes F3's images draw as images, and `Track.Attachment(path, "Page Screenshot")` too.
- **F20 relative paths:** `StepCollector.AddAttachment` resolves a relative path when it is attached: against the
  working directory when the file is there, else against the binary folder when it is there, else it keeps the path as
  given and the copier reports it (naming both places). In every default mode measured the two folders are the same, so
  nothing changes there; under NUnit's `WorkDirectory` the file the test wrote is found. A fixture shipped beside the
  binaries (`TestData/x.json`) is still found.
- **F4 Cucumber `url`:** a relative URL is resolved against `--attachments-base` (default: the working directory), the
  same rule as a tests-file record, so an ingest has one base for every relative path and the join's hashing and the
  copier read the same file. Q4 asks whether the messages file's own folder is the better base.
- **F5 merge:** `PointAttachmentsAtTheirShard` makes a missing shard file's path absolute against its shard's folder,
  as it does for a present one, so it can never name another shard's file beside the merged report.
- **F8:** `ResolveAttachmentPath` catches `ArgumentException`, `NotSupportedException` and `PathTooLongException` and
  hands the path on as given, where the copier already records `notUsable`.
- **F9:** the folder is created inside the guarded block; a failure is `notCopied` for that attachment.
- **F10:** a source already inside `<reports>/attachments/` is linked where it is, as merge does, and recorded for
  `Run.json`.
- **F11:** the six sites percent-encode `%`, `#` and `?` in the path (in that order) and HTML-encode the result. Only
  those three: `Uri.EscapeDataString` would also encode spaces and non-ASCII letters (measured: `shot#1 50%.png` becomes
  `shot%231%2050%25.png`), changing every such link and every golden for no gain. The file keeps its name on disk, so
  `Failures.md` and `query`, which print a path and not a URL, are unchanged.

### 4.5 What R2 does not change

The path is still kept as it was, and the link is still drawn in R2: only R3 changes the page. R2 is a patch that adds
a diagnostic the docs already promise, folds two existing entries into it, prints it on an in-process run's console,
turns one fact round, and changes no output other than the diagnostics.

### 4.6 Kronikol4J, the `v5` branch, and the plans beside this one

**Kronikol4J** (read 2026-10-19 by a sweep; reported). It has no copier: `Track.attachment` stores the path as given,
the merge carries entries but no files, and nothing records `AttachmentFailure`. Its attachment markup matches .NET's
byte for byte, HTML-escaped only. Its goldens are frozen at .NET 3.0.43 (HTML) and 3.0.47 (data), and every golden
attachment path is `attachments/<ascii name>.<ext>`, so F11's three characters change none of them (encoding `/` would
change all). Ledger lines (`docs/REMAINING_PARITY.md`, one per .NET release): R1, "Worth porting" (the encoding and the
image rule; the port reads the name's extension only, `DotNetHtmlReportRenderer.java:2505-2511`); R2, "Nothing to
port" (no copier); R3, "Not mirrored, report output" for the markup and `problem`, "Worth porting" for the schema key.
Found on the way, already divergent and in no ledger entry: the port's image rule is .NET's 3.0.43 rule (five
extensions, media type ignored), its YAML writes no `MediaType`, and its JSON schema is the 3.0.47 shape. Offered as a
ledger catch-up line, not part of this plan.

**The `v5` branch** (tip `4ee8d049`, on 4.26.9; reported). The attachment markup, copier, data keys, digest and query
lines are unchanged there, only moved. Three things to carry when main is merged into it: the Diagnostics tab is on by
default (section 1, point 2); `.attachment-missing` needs the `--kron-*` colour tokens v5's stylesheet gives the other
attachment rules (`v5:src/Kronikol/Reports/stylesheets.css:1083-1123`), or it is unreadable in the dark theme; and
`OPTION_C_PLAN.md`'s titles audit (S12, inventory row 48) gives `a.step-attachment` a title at the same three link
lines, with "a file this step attached", which is wrong at the two scenario-level sites.

**Plans beside this one.**

- `LIST_CAPS_PLAN.md` (#161, kronikol-71, draft, local branch `plan/161-list-caps`) rewrites the same query lines
  (`Narrative.cs:126-129, 144-146, 200-203, 215-218`) and adds a count column to `Failures.md`. It expects #148's marker
  to be added to its lines later ("No conflict"). R3 lands after it (Q7).
- `AZURE_DEVOPS_PARITY_PLAN.md` (not green-lit, waits on D27) has the same `attachments/` upload gap as its F4, slice
  S1, roadmap row 1.14 (Q8).
- `REPORT_FORMAT_PLAN.md` (being executed) changes `MergeableReportReader` and adds `.krkl` to
  `CiArtifactPublisher.PublishedExtensions`: the same files as R3's reader change and R4, different lines. Whichever
  lands second rebases.
- `CHANGED_SCENARIOS_PLAN.md` (#78, untracked in its worktree, draft) edits the data writers, schema, XSD, mergeable
  reader and the HTML next to the attachments block. The same: the second rebases.
- `MERGE_INPUTS_PLAN.md` (#154, draft, local commit `e5e762c5`) reserves `attachments/` in the folder a merge sweeps.
  No conflict with R2's merge entry.

---

## 5. Releases

Numbers are claimed at push time (the parallel-sessions rule); each release below says only which part moves.

### R1 (patch): the producers hand the copier a path it can find

F3, F4, F5 (the path), F8, F9, F10, F11, F20, F21; F18's help text and LightBDD, Cucumber and `Step-Tracking.md:358`
sentences. Bug fixes with no new surface. Behaviour changes to call out in the changelog: LightBDD attachments are
copied into `attachments/`, listed in `Run.json` and kept with their run (they were linked in place); an image attached
under a display name now draws as an image; a relative Cucumber `url` is read against `--attachments-base`; a relative
`Track.Attachment` path is resolved when it is attached.

### R2 (patch): a missing file is said

F1, F2, F5 (the entry), F15 (the cleaned folder's lost files are named), F16, F17, F18 (the diagnostics half), F19,
F22, F23 (said). The one entry of section 4.2, in the run and in `kronikol merge`; the console line. Docs: the
`AttachmentFailure` doc comment becomes true as written; the copier's own doc comment (`ReportGenerator.cs:6046-6051`)
and `ShowReportDiagnosticsSection`'s are rewritten. **Must not ship before R1** (section 1, point 1).

### R3 (minor): the state is kept and every reader shows it

Section 4.3 in full: F6, F12, F13, F14, F18 (the `Track.cs` and wiki rendering sentences). Minor for the new public
member and type (Q1) and the schema key. Kronikol4J ledger entry. After `LIST_CAPS_PLAN.md`'s R1 if that is green-lit
(Q7).

### R4 (patch): the Azure DevOps upload carries the report's attachments

F7. `CiArtifactPublisher` adds the files this run recorded under `attachments/` (`RunFileCollector`, the same list
`Run.json` gets) with `containerfolder={artifact}/attachments`, as `RetainedFiles` does for `runs/`; the merge path
(`MergedRunOutputs.cs:179`) adds the merged report's. Independent of R1 to R3. Q8 decides whether it ships here or as
`AZURE_DEVOPS_PARITY_PLAN.md`'s.

---

## 6. Tests

Every fact is written first and run on 4.27.0, and must fail there **at the assertion it was written for** (read each
red message, the FLOW_NESTING S1 lesson). Facts go through the public entry points (`ReportGenerator`,
`IngestPipeline.Run`, `QueryCommand.Run`, `MergeCommand`) so the same files compile on the old tag. The harness cases
become facts where they can.

### R1

| Fact | Red on 4.27.0 because |
|---|---|
| LightBDD stub with the `FileAttachmentsManager` shape (`FilePath` absolute, `RelativePath` bare) maps to the absolute path | maps the bare name |
| LightBDD real run, documented setup: the report links `attachments/<file>`, the file is there, `Run.json` lists it; after a second run the kept run's link names an existing file | not copied, `"attachments": []` |
| LightBDD real run with its folder set elsewhere: copied | dead link |
| An image attached as `Track.Attachment(path, "Page Screenshot")`, and a LightBDD image named without an extension: drawn as `img` | drawn as a link |
| A relative `Track.Attachment` path with the working directory moved away from the binary folder (in a collection that runs alone, since the working directory is process state): copied | not found |
| A relative path to a file beside the binaries, working directory elsewhere: copied | (guards the fallback) |
| Cucumber messages with `url: "shot.png"` and `--attachments-base <dir>` holding it: copied | resolved against the tool folder |
| Cucumber `url: "attachments/shot.png"` outside the reports folder: copied | taken for this run's copy |
| Merge of two shards, A holding `attachments/x.png` and B's `attachments/x.png` gone: B's link does not name A's file | names A's file |
| Tests file with a NUL in `path` (harness `ingest/` case2): ingest finishes, report written | unhandled exception |
| Library `IngestRequest.AttachmentsBase` relative: ingest finishes | throws |
| A file named `attachments` in the reports folder: report written | throws |
| Source already in `<reports>/attachments/`: linked as `attachments/<name>`, in `Run.json`, no diagnostic | false "being used by another process" entry, absolute link |
| Playwright: `shot#1.png` and `50%.png` draw (`naturalWidth > 0`) and open in the lightbox | requests `attachments/shot` |

### R2

| Fact | Red on 4.27.0 because |
|---|---|
| `AttachmentCopyTests.Missing_source_file_is_skipped_without_error` turned round: one `AttachmentFailure` whose message starts with the count | no entry |
| 3 attachments of 2 distinct missing files and 1 present: one entry, "3 attachment(s) … (2 files)", the present one copied | no entry |
| 7 missing files: five named, "and 2 more" | no entry |
| One failed copy, then three missing files: **one** entry, count 4, and the `!` line shows it with no `(×N)` | two kinds of message, the first a copy failure |
| An in-process `Track.Attachment("attachments/x.png")` with no such file (F2) is in the entry | no entry |
| Ingest with a relative capture path (harness `ingest/` case1): the entry names the base | no entry |
| `query summary` and `query failures` print the `!` line, count first, within 160 characters | no line |
| In-process run: the console carries the `⚠ WARNING:` line | nothing printed |
| A file attached, then deleted before the report: the entry says it existed when attached | no entry |
| A path attached twice and rewritten between: the entry says it changed after it was attached | no entry |
| `kronikol merge` of a shard whose `attachments/x.png` is gone: the merged data file's `diagnostics`, its HTML section (with the option on) and its labs page all carry the entry | no entry |
| `--clean-attachments` with a capture pointing into the folder (harness `ingest/` case3): the entry names those files | no entry |
| Blank `path` record (harness `ingest/` case1): counted as not usable | dropped silently |
| Unwritable Cucumber body, and bad base64: `AttachmentFailure`, not `Other` | wrong kind |
| Parity: the run's copier and the merge's carrier give the same state for the same 8 inputs (present, missing, held, unusable, URL, already-in-folder, `attachments/` relative missing, directory path) | (new helper) |
| LightBDD: a run with attachments records **no** `AttachmentFailure` | (guards R1's order; red if R2 lands without R1) |
| An in-process xUnit v3 run (`Track.Attachment` with a relative path) records **no** `AttachmentFailure` | guards section 2.4 |

### R3

Unit: each data file writes `problem` only when set, and the schema and XSD validate both shapes; a 4.27.0 file (no
key) reads as no problem; a mergeable round trip keeps it; `Failures.md`, `Failures.jsonl`, `query failures`, `steps`
and `--json` each show it; `Resolve` is not applied to a path with a problem.

Playwright (`tests/Kronikol.Tests.EndToEnd/`, rules in CLAUDE.md, `PollingInterval = 200`): for each of the three
sites (scenario, parameterized panel, step), a `notFound` attachment paints its name and the state text and no `img`
or `a` with that path; a present image still paints (`naturalWidth > 0`); an image deleted after the report was written
paints "could not be loaded" through the document listener; the same in an Export Filtered HTML copy.
`AttachmentRenderingTests`' fixtures get real files and a `naturalWidth` check, so its facts stop passing on broken
images (F12).

### R4

`CiArtifactPublisher`: on Azure DevOps a run's `attachments/` files are named with `containerfolder={artifact}/attachments`,
and a merge's too; GitHub Actions is unchanged. Red on 4.27.0: not named.

### Mutations (each must be killed before the release)

R1: map `RelativePath` again; read the image extension from the name only; resolve a relative attachment against the
binary folder only; resolve Cucumber URLs against the working directory instead of the base when both are given; leave
a missing shard path relative; percent-encode the whole path (slashes too); encode `#` before `%`. R2: count the
attachments instead of the files; name all files (no cap); one entry per state instead of one per run; record before
the walk ends (a partial count); skip the `attachments/` branch; put the merge entry on the report after `Render`;
drop the console line. R3: write `problem: null`; drop the key in the mergeable reader; keep `src` on a problem; bind
the listener to the element instead of the document; apply `Resolve` to a problem path.

---

## 7. Documentation

| Where | R | Change |
|---|---|---|
| `FileAttachment.cs` | R1, R3 | the image rule (R1); the new property, and `RelativePath`'s doc gains the problem case (R3) |
| `IngestCommand.cs:698` (`--attachments-base` help) | R1 | covers a relative Cucumber `url` too |
| Wiki `Step-Tracking.md:358` | R1 | a relative path is read against the working directory when attached, then the binary folder |
| Wiki `Step-Tracking.md:371` | R1, R2 | the `attachments/` sentence gets its real reason; the missing-file sentence says what is recorded |
| Wiki `Ingesting-External-Captures.md:210, 269`, `Integration-Cucumber-Messages.md` | R1 | a Cucumber `url` resolves against `--attachments-base` |
| Wiki `Integration-LightBDD-xUnit2.md`, `-xUnit3.md`, `-TUnit.md` | R1 | attachments are copied and kept with their run |
| `DiagnosticEntry.cs:29` | R2 | the sentence already says "found or copied": keep, add "or its path is not usable, or it changed after it was attached" |
| `ReportGenerator.cs:6046-6051` | R2 | the copier's comment says what it records |
| `ReportConfigurationOptions.cs:462-471` | R2 | says which entries reach the console |
| Wiki `Diagnostics-and-Debugging.md:107`, `Ingesting-External-Captures.md:670` | R2 | `AttachmentFailure`: "not found, changed, could not be copied or deleted, or not a usable path" |
| Wiki `Merging-Parallel-Reports.md:303` | R2 | the merged report names missing shard files |
| Skill `SKILL.md`, `references/commands.md` (both copies, `.claude/skills/` and `templates/skills/`; `SkillDriftTests`) | R2, R3 | the `!` line now exists; the marked line |
| `Track.cs:161-162`, wiki `Step-Tracking.md:334`, `Generated-Reports.md:546` | R3 | "an image draws inline, anything else as a link; a file the report does not hold says so" |
| Wiki `Querying-Reports.md`, `Generated-Reports.md` | R3 | the marked attachment line, the page's text |
| `src/Kronikol/Reports/agent-instructions.md:134` | R3 | a marked attachment has no file to read |
| Wiki CI pages (grep `PublishCiArtifacts`) | R4 | Azure DevOps artifacts carry `attachments/` |
| `CHANGELOG.md` | each | which part moved and why |
| Kronikol4J divergence ledger | each | section 4.6's lines |

Before each release, grep the wiki for `AttachmentFailure`, `attachments-base`, `left unchanged`, `download link` and
`attachment` (the 2026-10-18 lesson: a changed meaning needs every page that names the field).

---

## 8. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | The state on `FileAttachment`: a public property and enum (R3 is a minor), or internal (a patch, but no public type names the data file's new key)? | Public: the issue asks every reader to show it, and some readers are hosts |
| Q2 | Ship as four releases, or fold R1 and R2 into one patch? | Fold R1 and R2: R2 must not ship alone (section 1, point 1), and one patch removes the window |
| Q3 | The page's text: "not found when the report was drawn" (the issue's words) for `notFound`, "could not be copied into the report" for `notCopied`, "not a usable path" for `notUsable`. Show the recorded path in the page text (it is the producing machine's path, already published today) or only in a `title`, `Failures.md`, `query` and the data file? | The issue's words; the path in a `title` only, so a published page shows no machine path in its text |
| Q4 | A relative Cucumber `url`: resolve against `--attachments-base` (one rule for the ingest) or against the messages file's own folder? The Cucumber messages schema gives `url` no base | `--attachments-base`, documented; the messages file's folder is the default only when no base is given (Q4b if wanted) |
| Q5 | R3's document listener changes the page for images that fail for reasons Kronikol cannot know (an uploaded HTML without its folder; every image in an Export Filtered HTML copy saved elsewhere, since links stay relative). Wanted, or only the recorded state? | Wanted: it covers F7's case for reports already uploaded, and says why an exported copy shows no images |
| Q6 | F15: say it (R2), or also stop `--clean-attachments` from deleting a file the capture points at, and from emptying the folder when `--allow-empty` writes no report? | Say it in R2; the keep rule is a behaviour decision of its own |
| Q7 | `LIST_CAPS_PLAN.md` (#161) rewrites the query lines R3 marks. Land #161 first and add the marker to its lines? | Yes: it already expects the marker, and the order costs nothing |
| Q8 | R4 is `AZURE_DEVOPS_PARITY_PLAN.md`'s F4 (attachments half), which waits on D27. Ship it here as a patch, or leave it there? | Here: a bug fix that needs no D27 decision; that plan marks its F4 attachment half done |
| Q9 | F23: keep copying at the end of the run (R2 says when a file changed or vanished after it was attached), or copy at attach time, as Playwright's `testInfo.attach` does, so the report keeps what was attached? | Keep R2's diagnostic first; copying at attach time costs a copy of every file during the run (videos and traces included) and needs the reports folder before the report exists. Decide when the diagnostic has been seen in real runs |

---

## 9. Leftovers for `ROADMAP.md` Appendix C

Not in this plan's class (attachments that never reach the model, or render choices), each read unless marked:

- The framework attachment APIs Kronikol does not capture: NUnit `TestContext.AddTestAttachment`, MSTest
  `TestContext.AddResultFile`, xUnit v3 `TestContext.Current` attachments, TUnit artifacts (a grep of every adapter
  finds none).
- `StepCollector.AddAttachment` returns silently when no test id resolves (`StepCollector.cs:245-246`).
- ReqNRoll: attachments added outside a scenario (before its first `BeforeScenario`, after `AfterScenario`: feature and
  test-run hooks) are dropped, and a failure to subscribe is swallowed by an empty `catch`
  (`ReqNRollTrackingHooks.cs:62-80`). `AttachmentCapturingPlugin` (`[assembly: RuntimePlugin]`,
  `AttachmentCapturingPlugin.cs:6`) is dead code, as `CHANGELOG.md:10871` records, while `CHANGELOG.md:10949` still
  says it is "Activated automatically"; it and its decorator and their tests can go.
- A scenario-level image opens in a new tab with no lightbox and no caption, where a step's has both
  (`ReportGenerator.cs:2273` against `:3876`), although `FileAttachment.cs:16` says images open in a lightbox.
- `query steps` never prints a scenario's own attachments, and no verb does for a passing scenario (`LIST_CAPS_PLAN.md`
  takes this).
- LightBDD's own copies pile up at the top of `Reports/`, never moved or pruned (prune deletes only `runs/`; measured:
  six files after three runs). R1 stops linking them; pointing LightBDD's attachments manager at `<reports>/attachments`
  would stop writing them twice.
- Kronikol's LightBDD writer is registered at the relative path `Reports/.generation-complete`
  (`ReportWritersConfigurationExtensions.cs:79-83`), which LightBDD resolves against the working directory: the exe run
  from another folder leaves a stray empty file there (measured).
- A tests-file attachment with no Cucumber counterpart is moved to the scenario's own list even when the tests file put
  it on a step (`CucumberFeatureMerger.cs:195-197`, reported).
- Two reports written to one folder overwrite each other's attachments of the same name (`File.Copy(…, overwrite:
  true)` with a name set that starts empty on each call, `ReportGenerator.cs:6056, 6161`).
- A file a test writes straight into `<reports>/attachments/<name>` is moved into the previous run's folder by rotation
  before the copier runs, whenever the previous `Run.json` lists that name (inferred from `RunRotation.cs`, reported).
- Kronikol4J's three undocumented divergences (section 4.6).
- Section 2.4's other runner: BDDfy and Reqnroll run under the hosts measured there.

---

## 10. Log

- 2026-10-19, kronikol-7c: written at 4.27.0 in worktree `Kronikol-148` (branch `plan/148-missing-attachments`). Two
  read-only sweeps (every producer, every reader of an attachment) found the findings first marked "reported"; F1 to
  F3, F4, F5, F7, F15, F19 and the section 2 claims were re-read by hand, and F3 was confirmed against LightBDD 3.12.1's
  own code. Section 2.4 measured for xUnit v3 and TUnit. No product code was changed or built.
- 2026-10-19, kronikol-7c, verification pass at the owner's ask. Every file and line reference re-read at `6f6043e2`
  (corrected: F8 cited `Program.cs:19`, which is the console-encoding guard and not a catch; a few ranges narrowed),
  the wiki re-read at `21914fa`. Deep dives:
  - **LightBDD, measured** on the published 4.27.0 packages (harness `lightbdd/`): F3 confirmed, with the documented
    setup's link working by accident and its kept runs dead; R1-before-R2 now measured rather than argued.
  - **Working directory, measured** under MSTest, NUnit and xUnit v2 on VSTest and MTP (`cwdprobe2/`): one live case,
    NUnit's `WorkDirectory`, became F20.
  - **Ingest, measured** on the published 4.27.0 tool (`ingest/`): the issue's case, F8's crash (no report), F15 and
    F16 measured; F2 found not to reach tests-file records.
  - **Paths, measured** (`pathprobe/`): F10 is a misleading entry, not silence; F11's encoding limited to three
    characters.
  - **Producers, readers, rotation, merge** (a sweep, its claims re-read where cited as read): F5 can show another
    shard's file; F21 (image rule), F22 (console doc) and F23 (copy at the end of the run) added; the ReqNRoll and
    rotation side-findings went to section 9.
  - **Kronikol4J and `v5`** (a sweep, reported): no copier in the port and frozen goldens; v5's Diagnostics tab is on
    by default, which changed section 1's point 2.
  - **Other plans and issues**: `LIST_CAPS_PLAN.md`, `AZURE_DEVOPS_PARITY_PLAN.md`, `REPORT_FORMAT_PLAN.md`,
    `CHANGED_SCENARIOS_PLAN.md`, `MERGE_INPUTS_PLAN.md` (section 4.6, Q7, Q8); merge's diagnostics found to need the
    report object, not a scope (section 4.2).
  Design changes: one entry per run instead of one per state; a console line on in-process runs; the merge entry put on
  the report before `Render`; the changed-after-attached check; the query marker after the path. No product code was
  changed or built.
- 2026-10-19, kronikol-7c, **R1 and R2 as one patch, 4.27.2** (Q2), in worktree `Kronikol-148` on 4.27.1 (`d4158464`).
  The owner asked for the plan to be implemented in full; every section 8 question was taken as recommended. What
  shipped follows sections 4.1, 4.2 and 4.4, with these differences, each found while building it:
  - **The merge's entry goes first, not last** (section 4.2 said `[.. merged.Diagnostics, entry]`). A shard that
    recorded its own entry about its own report would otherwise be the one `query`'s `!` line shows, with `(×2)`,
    hiding the merged report's count. A fact merges a shard with its own entry and checks the line.
  - **The run's copier reads every path before it copies anything.** Linking a file already in `attachments/` where it
    is (F10) made a second clash possible: a file of the same name copied in first was written over it (the run copier
    overwrites). The names of files already there are reserved first, so the other takes a number.
  - **F20 keeps a second chance.** A relative path that neither the working directory nor the binary folder holds when
    it is attached keeps the working directory it was attached in (internal `AttachedFrom`), and the copier looks there
    and in the binary folder when the report is drawn, since a test may write the file after attaching it; the entry
    names both places. A relative path found when attached gets the attach-time stamp (F23) like an absolute one.
  - **What an ingest drops before the model holds it is in the one entry**: a blank tests-file path (F16) and a
    Cucumber body that could not be written (F17), through an internal `AttachmentContext` the pipeline hands the
    generator, with the base and whether `--clean-attachments` ran. `CucumberSynthesisResult.Warnings`, which is public,
    still carries the write failure's text; the pipeline no longer records it as `Other`. Section 4.2's second sentence
    is two: the base, and `--clean-attachments emptied the report's attachments/ folder before these were read.`
  - **Q6 "say it" reaches the empty pass too**: `--clean-attachments` on a pass with no scenarios writes no report and
    now records how many files it deleted (F15's second half).
  - **A relative `IngestRequest.AttachmentsBase` is made absolute against the working directory**, as the CLI makes
    `--attachments-base`, besides the catch section 4.4 asked for (F8), so a library caller's relative base finds its
    files instead of having every path handed on.
  - **The console line is printed only when the run scoped its own diagnostics**: a host that scoped them (`kronikol
    ingest`) prints every entry already, and the line would have been printed twice there.
  - **The state is kept on the attachment already**, as an internal `Problem` with the internal enum R3 makes public,
    so the parity fact (section 6) reads both writers' decisions directly. Nothing serialises it in this release.
  Message shape as shipped: `N attachment(s) are not in the report: <parts>: <first five paths>, and M more.` with the
  parts in the order not found (`(F files)` when the count differs), there when attached and gone, could not be copied
  (`Type: message` of the first), has/have a path that is not usable, could not be written from the Cucumber messages;
  then `N attachment(s) changed after it was/they were attached, and the report holds the version found when it was
  drawn: <paths>.`, then the ingest's sentences.
  Proofs: 36 facts failed on 4.27.1's code, given only the declaration of the state the parity fact reads, each at its
  assertion, and the Playwright fact timed out waiting for `shot#1.png` to load; the 4 facts that passed there guard
  what 4.27.1 already did. Mutations: 33 of 33 killed, the section 6 list for R1 and R2 and one for each difference
  above. Checks: after the version bump the core suite passes 7,785 with 2 skipped and the full Playwright suite 1,073
  with 28 skipped when run alone, and release.slnf builds in Release for every target with no warning from the changed
  files. Published the same day: once nuget listed all 62 packages, the wiki edits, the Kronikol4J ledger line and a
  comment on #148 followed.
- 2026-10-19, kronikol-7c, **R4 as a patch, 4.28.1**, on 4.28.0 (`978b1b1c`), built on 4.27.2 (4.28.0 changed none of
  the files R4 touches). As section 5 has it: `CiArtifactPublisher.AttachmentFiles` turns the report's own links
  (`attachments/shot.png`) into files, each with its folder, as `RetainedFiles` does for `runs/`; a run hands it what
  `RunFileCollector` recorded under `attachments/` (the list `Run.json` gets), and a merge (`MergedRunOutputs`) the links
  of the attachments `CarryAttachments` placed. One difference, found while building it: **the merge leaves out an
  attachment it could not place** (`Problem` set). A path that could not be read (a NUL in it) keeps the `attachments/`
  prefix it was given, and reading it again for the upload threw, so the merge said the files beside its report were
  not written. A third fact holds it. Proofs: the two section 6 facts failed on 4.27.2's code at their assertions, no
  attachment named; the third failed with that check removed. Mutations: 5 of 5 killed (the run naming no attachment,
  the merge naming none, the files named at the artifact's root, a link under `attachments/` not recognised, the check
  removed). Checks: after the version bump the core suite passes 7,827 with 2 skipped and the full Playwright
  suite 1,073 with 28 skipped when run alone, and release.slnf builds in Release for every target.
