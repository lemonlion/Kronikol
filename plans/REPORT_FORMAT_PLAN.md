# TestRunReport.krkl: a compressed report data file that only Kronikol reads (v5)

**Date:** 2026-10-18 · **Repo version:** 4.26.7 (`origin/main` at `a8563d21`; line numbers are for that commit, and
hold at 4.26.9, `354cb998`, whose two releases changed none of the files cited) · **Status: draft, not green-lit.**

This plan was written at the owner's asks of 2026-10-18:
- "Can we investigate reducing the size of TestRunReport.json more ... I'm inclined towards compression of the nested
  json in it rather than removing the whitespace ... It should only be the kronikol query engine that reads this
  stuff. I'm also thinking of scrapping the schema.json and changing the extension of the report from .json to .krkl."
- The same consumer concern: one consumer nests LightBDD composite steps up to 20 levels deep, which "adds an enormous
  amount to both their html and json sizes".
- Then: "put all of the above into a detailed well researched plan".

**Decided by the owner on 2026-10-18:**
1. The report data file is for Kronikol's own tool, not for people, `jq` or editors.
2. This work ships in a v5 major together with the report UI work running in another session. Everything `ROADMAP.md`
   lists for v5 today moves to v6 (§7).
3. The codec is zstd.
   - On net8.0 to net10.0 it comes from a source copy of ZstdSharp inside Kronikol, refreshed from its tagged releases
     every month, so that it is not a package dependency.
   - Wherever a target has `System.IO.Compression.ZstandardStream` (.NET 11 and later), Kronikol uses that instead,
     from the first release that can (§4.10).
4. v5 drops net8.0 and net9.0 from Kronikol's targets (Q17). Both leave support on 10 November 2026.

Everything else here is a recommendation, with the questions in §9.

Evidence labels:
- **RUN:** measured on 2026-10-18 on Windows 11 with Python 3.14.4, .NET 10.0.11 and ZstdSharp.Port 0.8.6, over
  BreakfastProvider's reports as its Pages site served them that day (written by Kronikol 4.9.1). Scripts and output
  are in [`REPORT_FORMAT_PLAN.harness/`](REPORT_FORMAT_PLAN.harness/README.md).
- **READ:** in the source at `a8563d21`, `file:line`.
- **INFERRED:** reasoned, not measured.
- **WEB:** a published source, linked.

## 0. Summary

**The format.** Today's `TestRunReport.json` becomes `TestRunReport.krkl`: one zstd frame (level 9, content checksum
on) around compact JSON.
- In front of it sits a 24-byte zstd skippable frame that names the file as Kronikol's and carries the decoded
  length. Standard zstd tools skip it.
- The JSON has no indentation, writes no `null` or empty-array keys, and declares `formatVersion` 3.

**What it saves.** On BreakfastProvider's LightBDD report, 6,150,370 bytes as written today, the `.krkl` would be
279,968 bytes, 4.6% of the file (RUN).
- gzip would give 494,624 (8.0%).
- Brotli 5, the closest alternative, would give 271,994; F6 says why it lost.

**Where zstd comes from** (§4.10):
- **On net8.0 to net10.0:** from ZstdSharp's source, copied into Kronikol.
  - The copy is internal, in a namespace of Kronikol's own, attributed in `THIRD-PARTY-NOTICES.md`, and refreshed from
    ZstdSharp's tagged releases by a monthly workflow.
  - ZstdSharp is the most used zstd library for .NET: 274 million downloads, and MongoDB's and Oracle's database
    drivers depend on it. It has no published advisory, and one maintainer (§2.6).
- **On net11.0 and later:** from .NET's own `ZstandardStream`, which Microsoft maintains. It ships only with .NET 11:
  it binds the runtime's native zstd and has no package for older targets, so it cannot serve net8.0 to net10.0
  (F21).
- **The copy's end.** .NET 8 and .NET 9 leave support on 10 November 2026, when .NET 11 ships. v5 drops both
  (Q17, decided), so the copy serves net10.0 alone from v5. It is deleted when net10.0 goes; .NET 10's support ends in
  November 2028.
- **Untrusted files.** The tool also builds for net11.0, so a file from an untrusted source is decoded by Microsoft's
  code wherever .NET 11 is installed.

**Two layers of compression don't mix.** Payloads compressed one at a time (`$z`, #85) are base64 that an outer
compressor cannot shrink, so the `.krkl` writes every payload as text. With `$z` kept at today's 8,192 characters, the
zstd 9 output is 66% larger, and brotli 5's 68% (RUN).

**Whitespace versus "compressing the nested JSON".** The owner's choice dissolves:
- Compressing more fields one at a time cannot reach the 35% of the file that is whitespace, nor the steps and call
  metadata.
- Compressing the whole file can reach them. Inside it, dropping the whitespace and the nulls is free, and it shrinks
  what the tool decompresses and scans.
- The two earlier refusals assumed that people read the file. They were ROADMAP D7 (whole-file gzip, "unreadable to
  `jq`, editors and validators") and V4_PLAN Q1 (indentation kept so `grep` does not print one huge line). The owner
  has now dropped that premise.

**Reading.** The tool tells the formats apart by their first bytes: Kronikol's marker or a bare zstd frame, gzip, or
plain JSON. It decodes the file once per command and keeps reading every `.json` report ever written:
- A writer without zstd writes gzip; Kronikol4J does, through `java.util.zip`.
- Old tools cannot read the new file, so the reader ships in 4.x, well before v5 makes it the default (§6).

**The schema.** From v5, `TestRunReport.schema.json` is no longer written beside each report by default (Q7). Nothing in
the product reads it at runtime (READ). Its generator and contract tests stay, as the internal specification that keeps
the .NET and Java writers in step.

**The deep composite steps** (F3, F9):
- *JSON.* Depth multiplies today's cost: one LightBDD step takes 1,564 bytes at depth 0 and 5,086 at depth 20. Written
  compact without nulls it takes 278 to 294 bytes at any depth (RUN).
- *Stopgap.* Until v5, that consumer can set `TestRunReportFullStepDetail = false`: 399 and 1,360 bytes, losing step
  parameters from the data file.
- *HTML.* The page does not grow with depth but with the number of steps. Each step repeats its status tooltip (45 to
  200 bytes of text), and every collapsed sub-step is rendered. The plan writes the tooltip once and draws collapsed
  sub-step trees on first expand. The second has a search trade-off (§9 Q8).

**Releases.**
- 4.x, ahead of v5:
  - R1 (minor): a `sizes` verb for #119 and a bytes column for #116;
  - R2 (patch): the tooltip written once;
  - R3 (minor): the reader for the new format, with the ZstdSharp copy, its notices, its sync workflow and
    `kronikol decode`;
  - R4 (minor): the writer behind an option, so the two consumers can opt in early;
  - R6 (a Kronikol4J release): Kronikol4J writes the format, as gzip, behind its own option;
  - R7 (minor): net11.0 builds of Kronikol and the tool when .NET 11 is released, on `ZstandardStream`.
- v5:
  - the `.krkl` by default;
  - no schema by default;
  - net8.0 and net9.0 dropped (Q17);
  - the documentation;
  - collapsed sub-steps on first expand (Q8);
  - together with the UI work.

## 1. How far each claim was checked

| Claim | Evidence |
|---|---|
| What fills the file, and every size and ratio in §2.4 | RUN, on three BreakfastProvider lanes; sizes only, never content |
| Codec sizes and times in .NET | RUN (`codecs.cs`): built-in gzip and brotli, and ZstdSharp.Port 0.8.6 for zstd |
| The per-step cost by depth | RUN on a synthetic step built to the full step mapper's shape (`deepmodel.py`). Not measured on the consumer's report, which is not on this machine |
| The HTML step costs | RUN on BreakfastProvider's LightBDD page (2,081 steps). The deep-steps consumer's page was not measured |
| Who reads, writes and names the file | READ: a census of `src/`, `templates/`, `tools/`, `tests/`, the skill, Kronikol4J and the wiki (Appendix A) |
| Platform support for each codec | READ and WEB (§2.5). Per-browser support for `DecompressionStream` brotli and zstd is not confirmed |
| ZstdSharp's standing and record | WEB, 2026-10-18: NuGet's listing and its "used by" list, the GitHub repository and its contributors, and GitHub's advisory database (no entries) |
| `ZstandardStream` is .NET 11 only | WEB: the API catalog lists it in `System.IO.Compression` 11.0.0.0 for net11.0, as preview; no package for older targets was found |
| .NET 8 and .NET 9 end of support | WEB: Microsoft's .NET blog, 10 November 2026 |
| How each decoder treats the container | RUN (§2.7): Python's standard library and ZstdSharp 0.8.8. The zstd command-line tool's handling comes from the format's specification, not a run |
| How the code treats absent keys, the allocation guard, Run.json and the run summary | READ: a second, read-only pass over the code (F23 to F28) |
| The 183 MB consumer reports of #119 and #120 | Their figures are quoted from the issues; those files are not on this machine |

## 2. Where it stands today

### 2.1 The file (READ)

**Writer settings.**
- `TestRunReportJsonOptions`, `ReportGenerator.cs:4716-4721`, serves the standard file, the mergeable one and a
  merge's output. It is camelCase, `WriteIndented = true` and the relaxed encoder.
- No `DefaultIgnoreCondition` is set, so every null is written.
- Line ends are `Environment.NewLine`, which is CRLF when the run is on Windows.

**Steps.**
- `TestRunReportFullStepDetail` defaults to `true` (`ReportConfigurationOptions.cs:275`), so every step goes through
  `MapStepJsonFull`.
- That writes 15 keys per step, whatever is set: `keyword`, `text`, `status`, `durationSeconds`, `bypassReason`,
  `docString`, `docStringMediaType`, `failureMessage`, `sourceFile`, `sourceLine`, `comments`, `subSteps`,
  `attachments`, `parameters` and `textSegments`.
- Each text segment then writes five keys and its parameter three more.
- The option's doc comment says step detail "is measured in kilobytes against megabytes of payload, so turn this off
  only if the file size genuinely matters". For a suite of deep composite steps that is no longer true (F3).

**Payloads.** `ReportPayloads` (`ReportPayloads.cs:32-112`) writes a body or diagram of 8,192 characters or more as
`{"$h","$n","$z"}`, and the file then says `formatVersion` 2.
- #85 shipped it in 3.35.0, and it has been on by default since 4.0.0.
- The threshold was 512 until 4.0.2. It was raised because a zipped CI artifact, or a host that serves the file
  gzipped, grew by 44 to 78% at 512: payloads that had compressed against each other stopped doing so.

**One string, one format.**
- `GenerateTestRunReportJson` (`:4723-4757`) builds the whole file as one string before `WriteFile` (`:6251`) writes
  it.
- `TestRunReportDataFormat` (`ReportConfigurationOptions.cs:440`) picks one format per run: JSON (the default), XML or
  YAML.
- The extension comes from `GetDataFormatExtension` (`ReportGenerator.cs:6350`).

**The schema.**
- `GenerateTestRunReportSchema` defaults to `true` (`ReportConfigurationOptions.cs:236`), and the schema is written
  even when the data file is switched off (`ReportGenerator.cs:529-532`).
- Its root `$comment` (`:6421`) tells agents not to read the data file.
- The data file carries no `$schema` link to it.
- XML runs also get `TestRunReport.schema.xsd` (`:6977`).

**Kronikol4J** writes the file with its own writer (`ReportFinalizer.java:231-259`):
- no `$z`;
- the schema only when `generateSchema` is set, which defaults to false (`ReportOptions.java:177`).

### 2.2 Earlier decisions this plan reopens

| Where | What was decided | Why it no longer holds |
|---|---|---|
| `ROADMAP.md` D7 (#85) | Compress payloads in place and keep the file JSON. Whole-file gzip (6.1 MB against 13.9 MB) was refused: "A report that tools cannot open defeats the schema work of 3.4.0" | The owner has decided the file is for the tool only |
| `V4_PLAN.md` F2 / Q1 | Keep the indentation: "a file without line breaks is one line, and `grep` on it prints the whole file" | A compressed file is binary: `grep` and `rg` print "binary file matches" and skip it |
| `ReportPayloads.Threshold` 8,192 (4.0.2) | A trade between the file on disk and the file zipped or served gzipped | A file that is compressed as a whole cannot be made larger by a zip or a gzip-serving host (INFERRED) |

### 2.3 Open issues this plan answers

| Issue | What it measured | Where it lands here |
|---|---|---|
| #121 | Indentation is 20.7% of a 4.0.1 report: 33,281,232 bytes against 26,384,510 without | Gone in the `.krkl` (§4.2); the `.json` keeps it until v5 |
| #120 | Each body is stored twice, as the call's content and again reformatted as its diagram note: about 24% of that report | Inside brotli or zstd the copies cost only 8 to 10% (F8), so it becomes a v5 follow-up, not a precondition (§9 Q6) |
| #119 | No command says what fills a report. One grew from 101,768,517 to 183,557,197 bytes, past GitHub's 100 MiB file limit | `kronikol query sizes`, 4.x minor (§4.9) |
| #116 | `interactions --group-by` has no bytes column | Same release as #119 |
| #154 | `kronikol merge <dir>` reads every `*.json` under it, so a JSON attachment aborts the merge | A dedicated extension removes the class; merge reads `*.krkl` and old `TestRunReport.json` names only (§4.6) |

#85 (closed) carried the earlier analysis. Its body measured whole-file gzip at 6,145,686 bytes against 82,685,589
raw, with 30 to 40 ms to inflate.

### 2.4 Measurements (RUN)

**What the LightBDD lane's file is made of** (`measure.py`). File 6,150,370 bytes, `formatVersion` 2.

| Part | Bytes |
|---|---:|
| Whitespace outside strings | 2,168,568 (35.3%) |
| Call metadata (everything in a call but its body; compact form) | 1,872,011 |
| Steps (compact form) | 743,824, of which 398,262 are keys written as `null` or `[]` |
| Bodies, as stored | 449,946 (881,434 decoded) |
| Diagrams, as stored | 682,354 (1,487,372 decoded) |
| Step depth | 613 steps at depth 0, 827 at 1, 593 at 2, 36 at 3, 12 at 4 |

The ReqNRoll lane (5,762,341 bytes) shows the same picture: 33.9% whitespace, and 295,486 of its 603,242 step bytes
are null or empty keys.

**The same data in six forms** (LightBDD lane; brotli with a 24-bit window):

| Form | Raw | gzip 6 | zstd 3 | zstd 19 | brotli 9 | xz 6 |
|---|---:|---:|---:|---:|---:|---:|
| As written | 6,150,370 | 664,456 | 562,368 | 466,107 | 458,805 | 458,844 |
| Compact, `$z` kept | 3,981,802 | 596,688 | 520,831 | 455,592 | 451,294 | 442,272 |
| Compact, no nulls, `$z` kept | 3,090,769 | 567,354 | 511,957 | 452,144 | 447,644 | 434,016 |
| `$z` decoded, indented | 7,607,997 | 619,419 | 400,717 | 264,608 | 270,434 | 276,836 |
| `$z` decoded, compact | 5,442,490 | 533,233 | 353,924 | 248,911 | 262,423 | 259,924 |
| **`$z` decoded, compact, no nulls** | **4,551,457** | **490,955** | **346,012** | **242,599** | **259,219** | **251,328** |

**Codecs on the last row in .NET 10** (`codecs.cs`; times are best of a warm run and move about 20% between runs):

| Codec | Bytes | Of today's file | Write | Read |
|---|---:|---:|---:|---:|
| gzip (`Optimal`) | 494,624 | 8.0% | 21 ms | 4.3 ms |
| zstd 3 | 346,012 | 5.6% | 19 ms | 5.8 ms |
| **zstd 9** | **279,968** | **4.6%** | **42 ms** | **5.0 ms** |
| zstd 12 | 272,906 | 4.4% | 81 ms | 4.8 ms |
| zstd 19 | 242,599 | 3.9% | 1,285 ms | 3.8 ms |
| brotli 5 | 271,994 | 4.4% | 35 ms | 6.7 ms |
| brotli 9 | 259,219 | 4.2% | 100 ms | 6.7 ms |

Python's bindings (`codecs.py`) give the same sizes. They also measured brotli 11 at 232,096 bytes in 4.5 s, and xz 6
at 251,328 bytes, 481 ms to write and 19 ms to read.

**One gzip member per scenario** (`blocked.py`). This would let the tool seek inside the compressed file, but it costs
+41.2% on the LightBDD lane (187 scenarios) and +49.9% on ReqNRoll (214). Scenarios share keys, services and bodies;
one stream keeps that.

**#120 and `$z` inside one compressed stream** (`notes.py`, LightBDD lane). The 881 body notes (916,708 characters),
replaced by references, change the size by:

| Codec | As is | Notes as references | Change |
|---|---:|---:|---:|
| gzip 6 | 490,955 | 396,705 | −19.2% |
| zstd 9 | 279,968 | 251,768 | −10.1% |
| zstd 19 | 242,599 | 220,390 | −9.2% |
| brotli 5 | 271,994 | 246,430 | −9.4% |

ReqNRoll: −19.5% under gzip, −10.2% under zstd 9 and −9.5% under brotli 5. Docker LightBDD: −13.7%, −8.4% and −8.1%.

Keeping `$z` inside the container:

| `$z` threshold | Decoded working set | zstd 9 | brotli 5 |
|---|---:|---:|---:|
| 8,192 | 3,090,501 | 464,803 | 455,857 |
| 65,536 | 3,636,915 | 342,602 | 335,948 |
| Off (nothing in these reports reaches 262,144) | 4,551,457 | 279,968 | 271,994 |

**A LightBDD composite step by depth** (`deepmodel.py`). Bytes for one step with three text segments, indented with
CRLF as a Windows run writes it:

| Depth | Full step detail (today's default) | Lean mapper (`TestRunReportFullStepDetail = false`) | Compact, no nulls |
|---:|---:|---:|---:|
| 0 | 1,564 | 399 | 278 |
| 5 | 2,444 | | 292 |
| 10 | 3,326 | | 294 |
| 20 | 5,086 | 1,360 | 294 |

Ten chains of 21 steps each, 20 deep, take 698,342 bytes indented and 61,490 compact without nulls (8.8%).

**The HTML** (BreakfastProvider's LightBDD page, 3,265,019 bytes, 2,081 steps):
- About 570 KB is step markup, about 274 bytes per step including its text.
- The status icon's `title` attribute repeats on every step: 112,374 bytes in total (93,645 of them text), one
  distinct string, since every step on this page passed with no skipped or bypassed sub-step.
- Every collapsed sub-step is in the page: 783 composites, all closed, hold 1,468 of the 2,081 steps (70%;
  `html_steps.py` and the depth histogram above).

### 2.5 Codec support by platform (READ, WEB)

| Runtime | gzip | zstd | brotli |
|---|---|---|---|
| .NET: Kronikol writes on net8.0, net9.0 and net10.0 (`Directory.Build.props` `MultiTargetFrameworks`); the tool runs on net10.0 | built in | ZstdSharp.Port, a pure C# port with no native binaries. `System.IO.Compression.ZstandardStream` arrives in .NET 11, in preview now ([API catalog](https://api.apireview.net/catalog/a25cf056ff3b2c416dc4a80d22246d05)) | built in on all three targets |
| Java 17 (Kronikol4J) | built in (`java.util.zip`) | a library: aircompressor (pure Java) or zstd-jni (native) | a pure-Java decoder exists; an encoder needs native code |
| Python (`query.py`) | built in | the standard library from 3.14 (`compression.zstd`, RUN on 3.14.4); a package before that | a package only |
| Node (the Node port plan) | built in | built in ([zlib docs](https://nodejs.org/dist/latest/docs/api/zlib.html); landed in [nodejs/node 4cc7907](https://redirect.github.com/nodejs/node/commit/4cc7907738), February 2025) | built in |
| Go (the Go platform plan) | built in | a library (`klauspost/compress`, pure Go) | a library (`andybalholm/brotli`) |
| Browsers | `DecompressionStream` everywhere | MDN lists `zstd` for `DecompressionStream` ([MDN](https://developer.mozilla.org/docs/Web/API/DecompressionStream/DecompressionStream)); per-browser support not confirmed | same as zstd |

Self-identification:
- gzip starts `1F 8B` and ends with a CRC-32 and the length.
- A zstd frame starts `28 B5 2F FD` and can end with a content checksum.
- A raw brotli stream has neither a signature nor a checksum (RFC 7932).

Nothing in the report page reads the data file (READ), so browser support matters only if a future dashboard does.

**What Kronikol depends on today (READ):**
- **The core package.** `Kronikol.csproj` references one package, `Microsoft.AspNetCore.Mvc.Testing`, one version per
  target (`:84-86`).
- **The tool.** `Kronikol.Tool.csproj` references no package at all: only `Kronikol` and `Kronikol.Extensions.Otlp`,
  which has none either.
- **The plans record this as a fact, not as a rule.**
  - `FLEET_EVIDENCE_PLAN.md` notes it, at `:224` and as its assumption A4 at `:466` ("PackageReference count 0").
  - The Otlp reader calls itself dependency-free (`OtlpTraceReader.cs:17`).
  - `QueryScriptGenerator.cs:15-20` rejected a `#:package` directive because its restore failed offline, not to
    avoid a dependency.
- **Kronikol4J** depends on no compression library. Its rule of no required runtime dependencies covers its core and
  diagram modules (`CLAUDE.md:13-15`); its report and runtime modules, which write the data file, have none today
  either.
- **No unsafe code.** No Kronikol project sets `AllowUnsafeBlocks` today.
- **ZstdSharp.Port is already in some consumers' apps.** The MongoDB extensions bring in 0.7.3 through MongoDB.Driver
  3.9.0 and 2.28.0.

### 2.6 ZstdSharp and `ZstandardStream` (WEB, READ)

**ZstdSharp.Port**, checked on 2026-10-18 on [NuGet](https://www.nuget.org/packages/ZstdSharp.Port) and
[GitHub](https://github.com/oleg-st/ZstdSharp):
- **What it is.** A C# translation of Meta's reference zstd, using `unsafe` code throughout. It is 232 C# files in
  `src/ZstdSharp`, and the DLL is about 410 KB.
- **Use.** 273,884,594 downloads, the most used zstd library for .NET by far; the next, IronCompress, has 16.8 million.
  - Packages that depend on it: MongoDB.Driver (439 million downloads), MongoDB.Driver.Core (395 million), MySql.Data
    (Oracle's MySQL connector, 155 million), Parquet.Net (33 million) and IronCompress.
  - Projects that use it include RavenDB, Duplicati, SteamKit and Nethermind.
- **History.** Created in 2021, with 31 releases. The latest is 0.8.8, of 2026-04-29, and it releases a few times a
  year. 392 stars and 7 open issues.
- **Licence.** MIT. Its `LICENSE` names only its author; zstd itself is BSD-3-Clause or GPLv2.
- **Maintainers.** One: 162 of about 172 commits.
- **Security record.**
  - No entries in [GitHub's advisory database](https://github.com/advisories), and no vulnerability or deprecation
    warning on NuGet.
  - No fuzzing found in its repository. Upstream zstd is fuzzed continuously; the translation inherits its logic, not
    that testing.
- **Targets.** .NET Standard 2.0, .NET Framework 4.6.2 and .NET 5, so it compiles for net8.0 to net10.0.

**`System.IO.Compression.ZstandardStream`:**
- **Where it ships.** In .NET 11, in `System.IO.Compression` 11.0.0.0. It is listed for net11.0 only, as a preview API
  in the previews ([API catalog](https://api.apireview.net/catalog/a25cf056ff3b2c416dc4a80d22246d05),
  [dotnet/runtime#59591](https://github.com/dotnet/runtime/issues/59591)).
- **Why it cannot reach net8.0 to net10.0.** It binds the native zstd that ships inside the .NET 11 runtime. Their own
  `System.IO.Compression` would load instead, without the native part. No package for older targets was found, and
  .NET's built-in brotli was never backported either.
- **Support dates.** .NET 8 (LTS) and .NET 9 (STS, now 24 months) both leave support on 10 November 2026
  ([.NET blog](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)). .NET 10 (LTS) is supported until
  November 2028.

**A package dependency against a source copy:**

| | ZstdSharp.Port as a package | A source copy with `THIRD-PARTY-NOTICES.md` |
|---|---|---|
| Company feeds that allow only approved packages | must be approved and mirrored, or restore fails | nothing new to approve |
| Dependabot, NuGet audit, `dotnet list package` | listed, and flagged if an advisory appears | not listed, so not flagged |
| Version conflicts with MongoDB.Driver or MySql.Data | possible: NuGet picks one version per app | none: the copy is internal |
| Licence scanners | MIT | MIT and BSD, both permissive |
| Scanners that fingerprint copied code (Black Duck and similar) | not applicable | find it either way; the notices make it declared and licensed |
| The code's vulnerabilities | the same code | the same code |
| Who patches | NuGet, Dependabot and the consumer | Kronikol, from its advisory watch (§4.10) |

The notices file is not optional. ZstdSharp's MIT licence requires its notice in "all copies or substantial
portions". zstd's BSD notice goes in too, since ZstdSharp translates zstd.

### 2.7 Checks of the container (RUN)

`marker.py` and `marker.cs` wrote Kronikol's marker (a zstd skippable frame) in front of a level 9 frame with its
checksum, over the LightBDD lane's content:
- **Python.** `compression.zstd.decompress` skips the marker and returns the content. The streaming `ZstdDecompressor`
  stops at the end of the first frame, which is the marker, having decoded nothing. A streaming reader must carry on
  with `unused_data` (§4.4).
- **ZstdSharp 0.8.8.** `Decompressor.Unwrap` and `DecompressionStream` both skip the marker. Both also decode the frame
  when the reader has already consumed the marker itself.
- **Damage.** A corrupted byte fails the checksum: `ZstdError` in Python, and `ZstdSharp.ZstdException` in .NET, which
  is not an `IOException`. A truncated file ends in `System.IO.EndOfStreamException` in .NET, which is one.
- **Size.** A frame written by ZstdSharp's streaming `CompressionStream` records no content size (frame header
  descriptor `0x04`). The marker's length is therefore the only size a reader has before decoding. The streamed frame
  was 280,416 bytes, against 279,972 written in one piece.

`scale.py` and `scale.cs` ran the 150 MB synthetic report of `tools/query-bench`, 115,396,210 bytes as `.krkl` content.
- It is so repetitive that zstd 9 reduces it to 0.74%, so its ratios mean nothing.
- It shows only that the size is no problem. ZstdSharp wrote it in 173 ms and decoded it in 62 ms, against 136 ms and
  45 ms for .NET's gzip; Python's native zstd took 120 ms and 50 ms.
- Long-distance matching made that file larger.

The zstd command-line tool was not on this machine. That it skips skippable frames comes from the format's
specification (RFC 8878), not from a run.

## 3. Findings

- **F1. A third of the file is whitespace.** 35.3% on the LightBDD lane and 33.9% on ReqNRoll; #121 measured 20.7% on
  a report with larger bodies. Under gzip the indentation still adds 11% (664,456 against 596,688), and it is 35% of
  what any reader scans.
- **F2. Most of a step is keys with nothing in them.** 54% of the LightBDD lane's step bytes are `null` or `[]`,
  because the full mapper writes all 15 keys and every segment writes five.
- **F3. Nesting multiplies the step cost.** Each `subSteps` level indents every line of a step by four more spaces (the
  array and the object). A LightBDD step is about 40 lines with its segments, so a step 20 deep costs 3.3 times one at
  the top: 5,086 bytes against 1,564. Compact JSON has no per-line cost, so depth stops mattering (278 to 294
  bytes). This is the consumer's problem in the JSON.
- **F4. `$z` and whole-file compression fight.** A wrapped payload is base64 of gzip, which no outer pass can shrink,
  and wrapping hides repeats between payloads from the outer compressor. Decoding every wrapper before compressing the
  whole is smaller under every codec (row 6 against row 3 in §2.4): the wrapped form is 68% larger under brotli 5 and
  66% under zstd 9.
- **F5. zstd 9 is the best point measured for this file.**
  - It is 279,968 bytes, 43% smaller than gzip.
  - It writes 4.5 MB in about 42 ms in .NET, about a second for a 100 MB report.
  - It reads about as fast as gzip, and 25% faster than brotli (5.0 against 6.7 ms).
  - Higher levels cost far more time for little: level 19 saves 13% more at 30 times the write time.
  - Its window is megabytes where gzip's is 32 KB, and it has a long-distance mode. Reports larger than these 6 MB ones
    repeat bodies further apart, so the gap over gzip should widen there (INFERRED; Q4).
- **F6. Brotli came within 3% and lost on everything else.** Brotli 5 is 271,994 bytes and is built into .NET, but:
  - a raw brotli stream has neither a signature nor a checksum (RFC 7932), so it needs a container of Kronikol's own;
  - Python has no brotli in its standard library, where it has zstd from 3.14;
  - Java has a pure brotli decoder but no pure encoder;
  - .NET 11 brings zstd into .NET itself, which removes the copy over time (§4.10). Nothing comparable is coming for
    brotli's gaps.
- **F7. One stream beats seekable blocks.** A gzip member per scenario costs 41 to 50%. The scanner reads the whole
  file in one forward pass on every command anyway (`ReportScanner.cs:84`), so decoding once into memory or a
  temporary file loses nothing a seekable layout would give.
- **F8. A large window absorbs half of #120.** Under gzip, replacing body notes by references saves 19%. Under brotli 5
  it saves 9.4% (8.1% on the Docker lane), and under zstd 9 10%, because a note and its body fall within the window.
  #120 stays worth doing, but the `.krkl` does not wait for it.
- **F9. The HTML grows with the number of steps, not their depth.** About 274 bytes per step, 54 of them the status
  tooltip, which takes 54 to 209 bytes by status. A collapsed composite step still renders all of its sub-steps. For a
  consumer whose composite steps nest 20 deep, the steps nobody opens are most of the page (INFERRED from the shape;
  their page was not measured).
- **F10. Nothing in the product reads the schema at runtime.**
  - Its readers are people and validators, the contract tests (`TestRunReportSchemaContractTests.cs:21-66`,
    `SchemaClosedContractTests`, `SchemaValidationTests`), and Kronikol4J's byte-for-byte pin of it.
  - The agent files Kronikol writes never mention it.
  - It is written even when no data file is.
- **F11. A rename touches every site in Appendix A, and some fail quietly.** Three are silent:
  - **The query hint disappears.** `RunSummaryConsoleWriter` only treats `.json` as queryable (`:789-790`,
    `:808-813`). With a `.krkl` the run-end pointer, the GitHub notice and the CI summary's agent block would stop
    printing it.
  - **Azure DevOps would not upload the file.** `CiArtifactPublisher.PublishedExtensions` (`:18`) leaves out unknown
    extensions, and Kronikol4J has the same filter.
  - **The baseline would be missed, or the wrong file taken.** The lookup tries `baseline/TestRunReport.json`, then
    the only `*.json` there that is not a schema (`QueryCommand.cs:519-533`). A `baseline/` holding a `.krkl`
    yields no baseline, or `Run.json` when that is its only `.json` (a copied run folder with `History.run.json`
    turned off; it is on by default, `ReportConfigurationOptions.cs:807`).

  Appendix A lists every site.
- **F12. Old tools cannot read the new file, and say so badly.**
  - `kronikol query`, `merge`, `ctrf` and `history` parse JSON (`ReportScanner.Scan`, `ReportScanner.cs:84-147`;
    `MergeableReportReader.Parse`, `:24-29`). An old tool given a `.krkl` reports "not valid JSON"
    (`QueryCommand.cs:183-185`) rather than "upgrade": the version check that would say so (`ReportGate.cs:29-63`)
    runs only on a file that parsed.
  - In a folder, an old tool does not find a `.krkl` at all.
  - Tools in the wild include the history action's pinned `VERSION`, `query.py` copies in skills and templates, and CI
    images. So the reader must ship well before any writer produces the format.
- **F13. The documented redaction check passes vacuously, and already does in 4.x.**
  - The wiki's Capture-Time-Redaction page checks that a secret is absent by reading the data file as text (lines
    69-74). Against compressed bytes that passes whatever the file holds.
  - It is already true of 4.x. Since 4.0.0 `$z` is on by default (`CompressTestRunReportPayloads`,
    `ReportConfigurationOptions.cs:259`), so a body or a diagram's source of 8,192 characters or more is gzipped
    inside the JSON, out of the check's reach. The page was fixed on 2026-10-18 (wiki `c6e85b9`, §8).
  - The check must go through the tool: `kronikol query grep` reads inside the wrapper, and will read the `.krkl`.
  - Two tests read the file as text too (`CaptureRedactionTests.cs:155-159`, `ProxyTapTests.cs:431-434`). Their
    bodies are short, and each also asserts that a string is present, so on a `.krkl` they fail rather than pass.
    They move to the decoder with the rest (§5, R4).
- **F14. The writer holds the whole file as one string.** For #119's 183 MB report that is a string of about 183
  million characters, plus its UTF-8 copy (INFERRED from `:4723-4757` and `WriteFile`). Streaming the JSON straight
  into the compressor removes both.
- **F15. A compressed file can expand without limit.** The tool reads reports from CI artifacts, which a pull request
  from a fork can produce. A small `.krkl` that decodes to gigabytes must be refused, not decoded (INFERRED; §4.4).
- **F16. ZstdSharp as a package would be the tool's first dependency; a source copy avoids that.**
  - The tool has no package reference today, and the core package one.
  - As a package, ZstdSharp would sit in every consumer's dependency graph, with the approval, scanning and version
    effects in §2.6, and `query.cs` would need an assembly resolver (F18).
  - Compiled from source into `Kronikol.dll`, it has none of those, and its licence obligation is a notices file
    (§4.10).
  - This plan's first draft chose brotli to avoid the dependency, and called the tool's zero count a property earlier
    plans keep. That overstated `FLEET_EVIDENCE_PLAN.md`, which only records it (§2.5).
- **F17. The reader's change is small, because the scanner reads forward only** (§4.4). Two hazards come with it:
  - `PayloadReader.Headers` sizes a read from the on-disk length (`:130`), which goes negative for offsets into
    decoded bytes.
  - A decode failure is not an `IOException`, so it escapes all five callers' error filters as a crash.
- **F18. `query.cs` cannot load a separate dependency of the engine.** It loads `Kronikol.dll` with `Assembly.LoadFrom`
  from the test run's output or the NuGet cache (`QueryScriptGenerator.cs:117`, `:129`, `:150-156`), and `LoadFrom`
  resolves dependencies only from that DLL's folder. Compiling the copy into `Kronikol.dll` (§4.10) leaves `query.cs`
  unchanged.
- **F19. The HTML has multipliers beyond depth.**
  - A parameterized group renders a full step tree in every row panel, all hidden but the first
    (`ReportGenerator.cs:3264-3279`).
  - `data-search` repeats every step's text at every depth (`:2183-2190`).
  - The long tooltip repeats on every passed ancestor of a skipped or bypassed step (`:2768-2788`).
- **F20. Sub-steps never draw their data tables.** `RenderStep` passes the default `skipTabularInline: true` to its
  children (`:3911` to `:3890`), and it looks unintended (§8).
- **F21. `ZstandardStream` is .NET 11 only.** It binds the native zstd inside the .NET 11 runtime and has no package
  for older targets, so net8.0 to net10.0 need another zstd (§2.6).
- **F22. .NET 8 and .NET 9 leave support on 10 November 2026,** when .NET 11 ships. Kronikol builds for both today
  (`Directory.Build.props`, `MultiTargetFrameworks`). Dropping them is a breaking change, so v5, a major, drops them
  (Q17, decided); the copy then serves net10.0 alone.
- **F23. Dropping null keys can make Kronikol4J's reports look unattributed.**
  - The scanner marks a report as having step attribution when it meets a `stepPath` or `failureMessage` key, even a
    null one (`ReportScanner.cs:764-770`, `:868-871`), or when `kronikolVersion` is 3.0.47 or later (`:283-284`).
  - Kronikol4J writes its own version there: `0.1.25-SNAPSHOT`, or `unknown` (`ReportData.java:29-31`).
  - Once it leaves nulls out (R6), a Java run with no step path, no failure and no annotation would print "report
    predates step attribution" on every query (`QueryCommand.cs:414-415`, `QueryCommand.Narrative.cs:354`). So would a
    merge of its shards.
  - The fix: the scanner treats `formatVersion` 3 as attributed (§4.4).
- **F24. The scanner's allocation guard would fail on a decoded snapshot.** `QueryStreamingTests` bounds what
  `ReportScanner.Scan` allocates:
  - under a third of the file on disk (`:55-77`, and over the query-bench corpus `:119-154`);
  - growing less than twice when the file grows eight times (`:79-97`).

  A snapshot holds the decoded size by design. The JSON facts stay, and the `.krkl` path gets its own guard (§5).
- **F25. Run.json lists the data file by name, and rotation moves only what it lists.**
  - `RunFileCollector.Record` runs inside `WriteFile` (`ReportGenerator.cs:6260`, with the fallback name at `:6278`;
    `RunManifest.cs:68-73`).
  - Rotation moves the files listed (`RunRotation.cs:189`).
  - So a writer that writes a temporary file and renames it must record the final name, or the next run leaves the
    report on top.
- **F26. The run summary's size line stops meaning what it was printed for.**
  - `RunSummaryConsoleWriter` prints the data file's size on disk (`:121-124`, `:159-162`) as a warning not to open
    it. Its CI rule gives the reason that "a real report reaches megabytes" (`:444`).
  - A `.krkl` is a tenth of that size, and binary.
  - The line should print the decoded size, or none, and the reason becomes "compressed: use `kronikol query`"
    (§4.6).
- **F27. `DataFormat` is shared with `SpecificationsDataFormat`.**
  - A `DataFormat.Krkl` member would be offered for the specifications file too. There `GenerateSpecificationsData`
    throws on it (`ReportGenerator.cs:5896`), and `GetSchemaExtension` has no case for it (`:6358-6364`).
  - Every check of `DataFormat.Json` would also need the new member, though the content is still JSON: mergeable data
    (`:514`), Failures.md (`:574-577`), `:680`, and `query.cs` (`:950-951`).
  - A separate option avoids all of that (§4.3, Q1).
- **F28. Merge copies `$z` from its inputs.** It writes compressed payloads whenever a shard had them
  (`MergeableReportRenderer.cs:209-210`, `MergeableReportMerger.cs:60`), not from an option. A v4 shard would therefore
  put `$z` into a merged `.krkl`, unless the `.krkl` writer ignores it (§4.5).

## 4. The design

### 4.1 The container

**Two zstd frames:**

| Bytes | What |
|---|---|
| `50 2A 4D 18` | A zstd skippable frame (magic `0x184D2A50`), which every zstd decoder skips |
| 4 | Its length: 16 |
| 16 | `KRKL`, the container version (1), three reserved bytes, and the decoded length (8 bytes, little-endian) |
| `28 B5 2F FD` and the rest | One zstd frame at level 9 (Q3), with the content checksum on |

- **The skippable frame names the file as Kronikol's without breaking standard tools:** `zstd -dc TestRunReport.krkl`
  prints the JSON.
  - It carries the decoded length, which the writer fills in once it knows it. The file is written under a temporary
    name and renamed at the end (§4.5).
  - The reader checks that length against the cap before decoding anything (F15), and sizes its buffer from it.
- **The zstd frame's checksum** (XXH64 of the decoded bytes) makes a truncated or damaged file fail at the end of
  decoding, with a message that says so. Merge already guards against truncated shards (`MergeCommand.cs:305-309`).
- **No dictionary.** Long-distance matching only if Q4 shows a gain on 100+ MB reports, since it raises the
  decoder's memory.

**Writers.**
- **.NET** writes zstd through the source copy on net8.0 to net10.0, and through .NET's own zstd on net11.0 and later
  (§4.10). Both write standard frames.
- **Kronikol4J** writes a bare gzip stream (Q12). gzip has its own signature (`1F 8B`), CRC-32 and length.

**Readers.** The first bytes decide:

| First bytes | Read as |
|---|---|
| `50 2A 4D 18` followed by `KRKL` | Kronikol's marker, then zstd |
| `28 B5 2F FD` | a bare zstd frame, such as a file recompressed with the zstd tool |
| `1F 8B` | gzip |
| `{`, optionally after a UTF-8 byte order mark | plain JSON, as every file written before R4 |
| Anything else | refused, naming the formats |

- The decoder stops at a hard cap on decoded bytes (Q11), even if the marker understated the length.
- A checksum failure is an error, naming the file.

**Rejected layouts.**
- *Brotli,* though 3% smaller: no signature or checksum, no support in Python's standard library, and no pure-Java
  encoder (F6).
- *gzip in .NET:* 77% larger than zstd 9 (494,624 against 279,968).
- *A member per scenario, or a ZIP of entries:* 41 to 50% larger (F7). Nothing needs random access inside the
  compressed bytes, because the scanner reads every byte once per command anyway.
- *A header of Kronikol's own in front of a bare stream,* which this plan's first draft had for brotli. Standard tools
  could not open the file, and zstd's skippable frame gives the same marker without that cost (Q16).

### 4.2 What the JSON inside holds

The `.krkl` holds the model the JSON writer builds today: `BuildFeaturesJsonModel` and, for a mergeable run, its
superset. It differs in four ways.

1. **No indentation, and no line breaks outside strings.**
2. **A key whose value is null, an empty array or an empty object is not written.**
   - A key the schema lists as required is always written, so the schema stays honest. The set is taken from the
     schema generator's `required` lists at every depth, not typed out (READ, `ReportGenerator.cs:6427` onwards):
     - the root's `formatVersion`, `startTime`, `endTime`, `features` and `background`; the background's `calls`,
       `afterScenarioEnd` and `interactions` (`:6465`); and each `afterScenarioEnd` entry's `scenarioId`, `scenario`
       and `calls` (`:6477`);
     - a feature's `name`, `labels` and `scenarios`, and a scenario's `id`, `stableId`, `name`, `result`,
       `durationSeconds`, `isHappyPath`, `labels`, `categories` and `steps` (`:6547`, `:6562`);
     - a step parameter's required keys (`:6674`, `:6684`, `:6694`, `:6710`, `:6727`, `:6741`, `:6754`), and those
       of component relationships, diagnostics and warm-up marks (`:6515`, `:6783`, `:6872`).

     Some required keys may hold null: a step parameter's `name`, a table column's `name`, and a tree node's `path`
     and `node`. So "always written" includes an explicit `null`. A fact derives the set from the schema and checks
     that every key is present in a written file.
   - `mergeableFormatVersion` is always written on a mergeable file, since the merge reader looks for it
     (`MergeableReportReader.cs:35`).
   - A reader treats an absent optional key exactly as null or empty.
   - `System.Text.Json`'s `WhenWritingNull` covers the nulls. The empty arrays need a type-info modifier, or the
     dictionaries built without them; R4 picks one.
3. **No `$z` wrapper.** Every body and diagram is a plain string (F4, Q5). `b:` addresses do not move, because the
   index hashes the decoded text (`ReportPayloads.Address`, `ReportScanner.HashBody`).
4. **`formatVersion` 3.** It means the keys of version 2, any optional one of which may be absent, with no wrapper
   written. A reader still accepts a wrapper, so that Q5 can change its mind without a fourth version.

**Unchanged:**
- the key order: `formatVersion` first, then each file's own order (`ciMetadata` comes before `features` in the
  standard file and after them in the mergeable one, `ReportGenerator.cs:4976`, `:4998`);
- the relaxed encoder;
- the full step mapper as the default. With nulls and empty arrays gone, a LightBDD step costs 278 to 294 bytes at any
  depth (§2.4), so the step detail no longer needs to be traded for size.

### 4.3 The name and the option

- **The file is `TestRunReport.krkl`.** The base name comes from `HtmlTestRunReportFileName` as now. The extension is
  `krkl` whenever the option below is on and the format is JSON.
- **The option is a new one, not a `DataFormat` member** (F27). It is `CompressTestRunReport` (name: Q1), read only
  when `TestRunReportDataFormat` (`ReportConfigurationOptions.cs:440`) is `Json`.
  - The content stays JSON, so every existing check of `DataFormat.Json` stays right, and `SpecificationsDataFormat`
    is untouched.
  - The default is `false` in 4.x and `true` at v5.
- **One data file per run,** as now. The `.krkl` replaces the `.json`; it is not written beside it.
- **`CompressTestRunReportPayloads` does not apply to the `.krkl`** (Q14).
- **Ingest and merge** write the format of their options:
  - Ingest gets a flag for it in R4.
  - Merge writes `.krkl` when any input is one or `-o` names that extension, so a merged report never loses ground
    against its shards.
  - A merged `.krkl` never carries `$z`, whatever its inputs held (F28).
  - Merge's `--no-json` keeps its meaning, the HTML only: no data file in either format. Its help
    (`MergeCommand.cs:374`) and the wiki say so.

### 4.4 The reader (R3)

**The seam is inside `ReportScanner.Scan(path)`** (`ReportScanner.cs:84`), as a `ReportSource`. It opens the file as
now (`OpenShared`, `FileShare.ReadWrite | Delete`, `:154-155`), records the length and write time on disk (`:87`), and
reads the first bytes:
- **Plain JSON** (`{`, or a byte order mark) takes today's path unchanged: offsets into the file, and payload reads
  that reopen and seek.
- **A zstd or gzip stream** is decoded while the scanner reads.
  - The scanner reads forward only (`stream.Read`, `:102`, in a 128 KB window that doubles for a large token,
    `:116-124`), so it can read from the decoding stream.
  - A tee copies the decoded bytes into a snapshot: in memory up to Q11's limit, above it a temporary file opened with
    `FileOptions.DeleteOnClose`.
  - The offsets the scanner records are then positions in the snapshot:
    - `windowStart + TokenStartIndex` through `TokenSlice` (`:934-935`) for bodies, headers and diagrams;
    - the wrapper objects (`:240`, `:536`), each a `Slice(Offset, Length)` (`ReportIndex.cs:375`).

**The index gains two properties, `Source` and `DecodedLength`.**
- `FileLength` stays the bytes on disk, which `summary` prints as `sizeBytes` (`QueryCommand.Overview.cs:38`, `:59`).
  `summary` gains `decodedBytes`.
- `PayloadReader.Headers` reads `Math.Min(index.FileLength - start, 1 << 20)` bytes (`PayloadReader.cs:130`), so it
  must use the decoded length. Otherwise an offset past the compressed size makes the count negative, and the
  `OverflowException` from `new byte[]` is caught nowhere.
- `PayloadReader.Open` (`:90-114`) returns a stream over the snapshot. `Read(FileStream, slice)` (`:39-59`) becomes
  `Read(Stream, slice)`, and `FindArrayStart` (`:166-180`) works on any seekable stream.
- `BodyCache` (`BodyCache.cs:26`) holds that stream for the command.

About ten edits in four files, plus `summary`. The four callers of `Scan` and the 15 payload call sites (8 `Read`, 3
`Headers`, 4 `BodyCache`) do not change.

**Change detection.**
- The snapshot is one consistent copy, so a payload read cannot see a half-written replacement. `ThrowIfChanged` still
  runs once on the file after the scan (`ReportScanner.cs:142`).
- The `AfterScan` tests (`QueryCommand.cs:103`, `:190`) replace the file after the scan and expect
  `ReportChangedException`. That stays true for a `.json`. A `.krkl` answers from its snapshot instead, and a new fact
  pins that.

**Errors.**
- A damaged gzip stream throws `InvalidDataException`, and a damaged zstd frame `ZstdException` (§2.7). Neither is an
  `IOException` (a truncated zstd file's `EndOfStreamException` is), so all five callers' filters would let them escape
  as a crash:
  - `QueryCommand.cs:173-187`;
  - `QueryCommand.Search.cs:484`;
  - `CtrfCommand.cs:82-91`;
  - `HistoryCommand.Maintenance.cs:175`, and `import`'s catch at `:456`.
- The source maps every decode failure to one error, naming the file and saying it is damaged or truncated. The
  frame's checksum (§4.1) catches what a decoder does not.

**The cap (F15).** The marker's decoded length is checked against the hard cap before anything is decoded, and the
decoder also stops at the cap in case the marker lies.

**The version gates** accept 3: `ReportGate.Refuse` (`ReportGate.cs:44-50`) and its twin in `MergeableReportReader`
(`:57-67`). A higher version keeps the "Upgrade Kronikol.Tool" message.

**Step attribution.** The scanner treats a `formatVersion` 3 file as attributed (F23), since the keys it used to detect
attribution by may now be absent.

**Merge.** `MergeableReportReader.ReadFile` (`:24`) reads with `File.ReadAllText`. It reads through the same source
instead, and parses the decoded bytes. R3's facts check that its key helpers read an absent key as null.

**Finding the file.** Every lookup accepts `*.krkl` and `*TestRunReport.json`:
- `ResolveReport` (`QueryCommand.cs:448`, `:456-458`);
- `ResolveBaseline` (`:522`, `:528-532`);
- `RetainedRunResolver` (`:100`, and `:108`, which hard-codes the name);
- `history import` (`:497`);
- merge (`MergeCommand.cs:286`, `:301`).

When a folder holds both, which can happen once (§4.5, rotation), the newer by write time wins and the tool says which
it read.

**query.py.** `load` (`:40-56`) reads the first bytes:
- **zstd:** `compression.zstd`, in the standard library from Python 3.14.
  - It decodes with the one-shot `decompress`, which skips the marker. The streaming `ZstdDecompressor` stops after the
    marker's frame (§2.7).
  - On an older Python it exits with a message naming the tool and `query.cs`.
- **gzip:** the standard library, on any Python.
- **`.json`:** as now.

The Python floor stays where it is.

**query.cs** needs nothing new. The copy is compiled into `Kronikol.dll`, which `LoadFrom` already loads (F18).

**`kronikol decode <report> [-o <file>] [--indent]`** (Q15) writes the decoded JSON for a script or a person who needs
it. It gives the documented `jq` recipe and the redaction check (F13) a supported route.

### 4.5 The writers (R4)

**.NET.** `GenerateTestRunReportData` (`ReportGenerator.cs:4503`, its switch `:4516-4522`) gains a branch for the new
option. It serializes with `JsonSerializer.Serialize(Stream, ...)`, not indented, straight into zstd behind the marker
(§4.1), through the copy or .NET's own zstd (§4.10).
- It writes to a temporary file in the reports folder and renames it over the target at the end, so a reader never sees
  half a file.
- `RunFileCollector.Record` is called with the final name, and with the `<name>2.krkl` fallback when that is used,
  never with the temporary one (F25).
- This removes F14's peak. Serializing to a string fills a pooled UTF-8 buffer and then converts it to UTF-16, about
  three times the file's size for a moment, on top of the model.

**`WriteFile`** (`:6251-6290`) takes only a string and falls back to `<name>2<ext>` when the target is locked
(`:6270-6274`). The `.krkl` needs a stream version with the same fallback.

**Public API.** `GenerateTestRunReportData` has a public overload (`:4496`), which Kronikol4J's parity harness calls. A
new parameter goes into a new overload, never as an optional parameter on the existing one: that would be a binary
break.

**Leaving out nulls and empty arrays.**
- Most of the model is dictionaries (`:4734`, `:4768`), and System.Text.Json's ignore conditions do not skip dictionary
  entries, so the builder has to leave them out itself.
- The step mappers are anonymous types: `WhenWritingNull` covers their nulls, and their empty arrays are left out
  explicitly.
- Keys that readers need even when empty stay:
  - `features` (`ReportGate.cs:31`, `MergeableReportReader.cs:32`, `ReportScanner.cs:237`);
  - `mergeableFormatVersion` (`MergeableReportReader.cs:35`);
  - the schema's required keys (§4.2).

**The mergeable writer and merge's output take the same path.** The mergeable writer is `GenerateMergeableReportJson`
(`:4922-5010`).
- It is chosen only when the format is JSON (`:514`), so with the option on it writes the `.krkl` the same way.
- Merge's writer ignores its inputs' `$z` for a `.krkl` (F28). Merge's output goes through:
1. `MergeableReportRenderer.Serialize` (`MergeableReportRenderer.cs:181`);
2. `MergeCommand.cs:261-270`;
3. `QueryWriter.TryWriteFile` (`QueryWriter.cs:318-347`).

**Nothing reuses the string after it is written.** Failures.md and Failures.jsonl (`:553-580`), CTRF (`:595-597`) and
the history fragment (`:370`, `:605`) are built from the model. Streaming changes nothing else.

**Rotation.** `RunRotation.Request(..., DataExtension, ...)` (`RunRotation.cs:67`, fed at `ReportGenerator.cs:394` and
by `PlannedFiles` at `:791-795`) recognises one extension. In a folder without `Run.json`, a v4 `TestRunReport.json`
is not recognised as this report's data file, so after an upgrade it would stay on top beside the new `.krkl`. R4
makes rotation treat both extensions as the report's data file, so the old one moves into `runs/` with its run.

**Smaller sites** learn the `.krkl` name:
- `ReportGenerator.cs:575`, where Failures.md's data file name hard-codes `.json`, and `:680`, the run summary's query
  target, through the data file's extension;
- `MergedRunOutputs.cs:88`, `:127-128` and `:141`;
- `IngestCommand.cs:462`.

`query.cs` is still written only for JSON (`:950-951`), which the `.krkl` holds, so that check needs no change.

The new option is public surface on `ReportConfigurationOptions`, which makes R4 a minor. `DataFormat`
(`src/Kronikol/Reports/DataFormat.cs:6-16`) gains no member (F27).

**Kronikol4J (R6).** It writes its JSON with its own serializer (`ReportDataSerializer.java`: `toJson` `:57-83`,
`writeJson` `:333-382`, `escapeJson` `:384-403`). That JSON is indented and writes nulls, as .NET's does, and
differs from it in three ways:
- it escapes as `System.Text.Json`'s default encoder does (`&<>'+` and every non-ASCII character), where .NET
  writes the file with the relaxed encoder (`ReportGenerator.cs:4719-4720`);
- it has no `formatVersion`;
- it has no `$z`.

A compact writer already exists (`CompactJson`). R6 writes a bare gzip stream named `.krkl` through
`java.util.zip`, which keeps the report module free of dependencies, as it is today. It also adds `formatVersion` 3,
leaves out nulls, and adds `.krkl` to its upload filter (`ReportFinalizer.java:215-216`). Its `ReportDataParityTest`
compares JSON text with fixtures captured from .NET 3.0.47 (`:36`). Its `.krkl` parity compares decoded content, as
the HTML parity test already does for compressed islands.

### 4.6 Every place that names the file

Appendix A lists the code sites, the documentation and the tests, by the release that changes them. Three need
care, because they fail quietly (F11):
- `RunSummaryConsoleWriter`, which stops printing the query hint for anything but `.json`;
- `CiArtifactPublisher.PublishedExtensions`, which would leave the `.krkl` out of an Azure DevOps upload (Kronikol4J
  has the same filter);
- the baseline lookup, which would find no baseline, or take `Run.json`.

#154 is fixed on the way: merge takes `*.krkl` and `*TestRunReport.json`, not every `*.json` in a folder.

**The run summary** (F26):
- For a `.krkl` it prints the decoded size, or none.
- It gives the reason not to open the file as "compressed: use `kronikol query`", where it now says a report reaches
  megabytes (`RunSummaryConsoleWriter.cs:444`). The same goes for `IngestCommand.cs:473-475`,
  `agent-instructions.md:7-11` and `SKILL.md:7`.
- `QueryTargetFor` exempts `TestRunReport.krkl` as it exempts `TestRunReport.json` (`:683`), so commands name the
  folder, not the file.

### 4.7 The schema, the XSD, and the XML and YAML formats

- **No schema beside a report from v5.** `GenerateTestRunReportSchema` defaults to `false` and stays as an opt-in for
  anyone who validates decoded files (Q7).
- **The generator stays, and so do its contract tests.** `TestRunReportSchemaContractTests`, `SchemaClosedContractTests`
  and `SchemaValidationTests` run over the decoded `.krkl`. "Every emitted key is declared, with a description" is
  what keeps two writers in step, and Kronikol4J goes on pinning the generator's output.
- **The schema's text changes.** `formatVersion` 3's description says optional keys may be absent and no wrapper is
  written, and the `compressedPayload` definition says it is read and no longer written.
- **The XSD and the XML and YAML formats are unchanged** (Q9). They do not have the step detail option, as today.
- **The only agent-facing pointer goes.** The agent files Kronikol writes never mentioned the schema; the wiki's
  AI-Integration-Prompt line that does is removed.

### 4.8 The deep composite steps

**JSON.** §4.2 solves it: 278 to 294 bytes per step at any depth, where today a step takes 1,564 to 5,086 bytes. Until
v5 the consumer has the stopgap (Q10).

**HTML, part (a): the status tooltip, written once (R2, a patch).**
- *Today.* The status span is 102 bytes, of which the `title` attribute is 54, 45 of them text
  (`ReportGenerator.cs:3691-3723`, `:3745-3748`). The attribute becomes 186 bytes, 177 of them text, when any descendant
  was skipped or bypassed. `HasAnySkipped` and `HasAnyBypassed` (`:2768-2788`) look at all descendants, so every passed
  ancestor of one skipped step carries the long text: a chain 20 deep carries it 20 times.
- *The change.*
  - The seven texts go into the page once, as a map from status class to text. Each status already has its own
    class, `passed-skipped` and `passed-bypassed` included (`:3693`), so the markup loses only the attribute.
  - One delegated `mouseover` and `focusin` handler sets `title` the first time a status icon is hovered or focused.
  - The icon stays as text, because `IngestedBypassTests` reads it (`:28-32`, `:53-57`).
- *Saving.* The whole attribute, 54 to 209 bytes per step: 112,374 bytes over BreakfastProvider's 2,081 steps, 3.4%
  of the page.

**HTML, part (b): collapsed sub-steps drawn on first expand (R5 or v5, Q8).**
- *Today.* A composite step renders its whole subtree whether it is open or not. Only a failed descendant opens it
  (`HasAnyFailed`, `:2757-2766`). Two things multiply that:
  - A parameterized group renders a full step tree in every row panel, all hidden but row 0 (`:3264-3279`).
  - `data-search` repeats the text of every step at every depth (`:2183-2190`, `CollectStepText` `:6340-6348`). This
    is the search's own data, and it stays.
- *The design.*
  - `RenderStep` renders the children of each outermost closed composite as now, but into a separate builder. That
    keeps encoding, numbering, background marking, the table behaviour and the theme classes identical, with no second
    renderer in JavaScript.
  - The HTML is gzipped and base64-encoded with the diagrams' `CompressToBase64`
    (`InternalFlowHtmlGenerator.cs:573-582`).
  - It is stored in its own `<script id="step-data" type="application/json">` map, keyed by an id on the empty
    `<div class="sub-steps">`. Steps must not share `#puml-data`, which the deep-search worker receives whole
    (`report-search-index.js:553`, `:577`).
  - Failed paths stay rendered and open. A chain 20 deep is one blob and one decompression.
  - Subtrees under a threshold stay inline, since base64 adds a third and the async step costs more than it saves.
- *The script.*
  - A capture-phase `toggle` listener inserts the HTML on first open. The pattern is already at
    `flame-chart-render-script.js:190-194` and `diagram-toggle-layout-script.js:59`.
  - It decodes with `decompressGzipBase64` (`report-decompress-helper.js:5-12`), which needs `DecompressionStream`,
    as the diagrams, the search index and the internal-flow popup already do. The page has no fallback decoder.
    Where the browser lacks it, the popup reports a failed decode (`internal-flow-popup-script.js:35-38`, `:84-90`),
    and an expanded composite does the same, with a line in place of its sub-steps.
- *What does not change.*
  - Kronikol's search reads `data-search` and the deep index built in C#, never step markup
    (`report-scenario-feature-map-helper.js:17`, `report-search-function.js:63`, `:142`,
    `report-search-index.js:324-346`, `:464-503`).
  - `revealSingleMatch` never opens composite steps (`report-search-function.js:13-21`).
  - Expand-all, jump to failure, `#sid-` links, copy and keyboard navigation work at scenario level.
  - The CSS indents by DOM nesting (`.sub-steps`, `stylesheets.css:834-838`) with no depth classes, so an inserted
    subtree looks the same. Themes keep their classes (`Stylesheets.cs:109-111`, `:124`).
- *What has to be handled.*
  - `selectRow` copies the open state of `<details>` between row panels by position
    (`report-select-row-function.js:11-30`). It needs a stable key, or a re-sync after insertion.
  - Export Filtered HTML clones the DOM and copies the body-level scripts, pruning only `#puml-data`
    (`report-export-function.js:52-79`, `:110-132`). It must carry and prune `#step-data` as well.
  - The browser's Ctrl+F cannot find text in a step that was never opened, where today Chromium opens a closed
    `<details>` to show a match. This is the trade in Q8.

Found on the way: sub-steps never draw their data tables, because `RenderStep` passes the default
`skipTabularInline: true` down (`:3911` to `:3890`). See §8.

### 4.9 Measuring a report: `kronikol query sizes` (#119) and bytes per group (#116)

`kronikol query sizes <report>` prints:
- the file's bytes on disk and decoded;
- decoded bytes by part: steps (and how many of them are empty keys), call metadata, bodies (distinct and in total),
  diagrams, internal-flow segments, whitespace (for a `.json`) and the rest;
- the 10 scenarios with the most bytes;
- with `--json`, the same as data.

For a `.json` with `$z` wrappers each part has its stored and decoded bytes, which is #119's ask. The scanner already
makes the pass that can total them and records a slice per part (`ReportScanner.cs`), as #119 points out.

`interactions --group-by` gains a bytes column: request and response body lengths, decoded, summed per bucket, as
`services` computes them. It also accepts `--sort bytes`, and `--json` rows carry the bytes (#116).

This release goes first because it is how the two consumers can measure their own reports (Q4, A1) without opening
them.

### 4.10 Where zstd comes from: ZstdSharp's source, then `ZstandardStream`

Decided by the owner on 2026-10-18: a source copy of ZstdSharp, refreshed monthly, so that zstd is not a package
dependency, and `ZstandardStream` wherever a target has it, as soon as it can.

**The copy.**
- `src/Kronikol/Compression/Zstd/` holds ZstdSharp's `src/ZstdSharp/` at a tagged release, with a `VERSION` file
  naming the tag and commit.
- An import script (`tools/vendor-zstd/`) makes the only changes:
  - every type becomes `internal`;
  - the namespace `ZstdSharp` becomes `Kronikol.Compression.Zstd`.

  Nothing is edited by hand, so the copy differs from its release by that script alone, and running it again on the
  same tag changes nothing. The types are internal because a public `ZstdSharp.*` type in `Kronikol.dll` would clash
  with the real package in a consumer's app (MongoDB.Driver, MySql.Data), with ambiguous-type compile errors.
- It compiles into `Kronikol.dll` on net8.0 to net10.0 only. On net11.0 and later an MSBuild condition removes it
  (`IsTargetFrameworkCompatible('$(TargetFramework)', 'net11.0')`).
- `AllowUnsafeBlocks` is turned on for `Kronikol`, which has none today. A fact checks that `unsafe` appears only under
  the copy's folder.
- An `.editorconfig` section marks the folder as generated code, so the repository's analyzers and nullable warnings
  stay quiet there. Whether CodeQL scans it is Q18.

**Notices.** `THIRD-PARTY-NOTICES.md` at the repository root holds ZstdSharp's MIT notice and zstd's BSD-3-Clause
notice. It is packed into `Kronikol` and `Kronikol.Tool`, and the README points to it. A pack test checks that it is
in both packages.

**The monthly sync.** A scheduled workflow, `.github/workflows/vendor-zstd.yml`, runs monthly and can also be run by
hand:
1. It compares `VERSION` with ZstdSharp's latest release (`gh api repos/oleg-st/ZstdSharp/releases/latest`). It only
   ever moves to a tagged release, never to the main branch.
2. On a newer release, it runs the import script, builds every target and runs the zstd facts of §5.
3. It opens a pull request named for the release. Once merged, the change ships in the next Kronikol release as a
   patch.

ZstdSharp releases a few times a year, so most months the workflow does nothing.

**The advisory watch.** Copying hides ZstdSharp from consumers' scanners, so Kronikol has to notice a vulnerability
itself.
- The same workflow queries GitHub's advisory database for ZstdSharp.Port and for facebook/zstd, whose decoder fixes
  ZstdSharp ports.
- It opens an issue when either has an entry newer than the copy.
- A fix ships as a patch release.

**`ZstandardStream`, as soon as each piece can:**
1. **When .NET 11 is released** (November 2026), Kronikol adds net11.0 to its targets (R7). That is a minor, since a
   new target is new surface. The net11.0 build of `Kronikol.dll` writes and reads with .NET's own zstd and contains
   no copy.
2. **The tool builds for net10.0 and net11.0.** A tool package carries both, and .NET runs the net11.0 build wherever
   that runtime is installed. There, a file from an untrusted source is decoded by Microsoft's code.
3. **`query.cs`** loads the engine's net10.0 build (`QueryScriptGenerator.cs:117`, `:129`). It learns to prefer the
   net11.0 build when the SDK running it is 11 or later.
4. **v5 drops net8.0 and net9.0** (Q17), both out of support on 10 November 2026. From then on the copy serves net10.0
   alone.
5. **When net10.0 leaves Kronikol's targets,** at the end of its support in November 2028 at the latest, the copy goes
   with everything that serves it: the script, the workflow, the notices entry and `AllowUnsafeBlocks`.

The API is marked preview in the .NET 11 previews, so R7 confirms its released shape before relying on it (A7).

**Kronikol4J** writes gzip (Q12) and has no copy to keep.

### 4.11 Not in this plan

- **#120's notes as references:** after v5 (Q6).
- **A trained compression dictionary.** Reports are large enough that a dictionary gains little.
- **The HTML's embedded diagrams** (`puml-data`) stay gzip, which every browser decodes.
- **The other run files stay as they are:** `History.run.json`, `Failures.jsonl`, `ctrf-report.json` and
  `Specifications.*` are small, and other tools read them.

## 5. Tests, red first

Each fact is shown failing on the release before it, as this repo's plans do: the new tests are copied onto the
previous tag with the change stubbed. Each release's guards are mutation-tested. Playwright facts follow `CLAUDE.md`:
no `force: true`, `PollingInterval = 200`, and `dispatchEvent` for SVG.

**R1 (`sizes`, bytes per group).**
- The parts add up to the decoded size, on a `.json` with and without `$z`.
- Stored and decoded bytes per part, the top scenarios in order, and `--json`.
- The group bytes equal what `services` sums for one service.
- `--sort bytes` is accepted and carried into the rerun prefix (the LLM-friendly plan's lesson: a flag that changes
  the order must be in it).

**R2 (tooltip).**
- Unit: one map per page, no `title=` on a status span, and a text in the map for every status class.
- Playwright: hovering and focusing each of the seven states shows 4.26.x's text, including a chain 20 deep with one
  skipped leaf. `StepRenderingReportTests:531-552` is turned round.

**R3 (reader).**
1. **The central fact:** every verb gives the same answer over a run's `.krkl` and its `.json`, apart from
   `summary`'s size lines.
2. **Detection:** the marker with zstd, a bare zstd frame, gzip, JSON with and without a byte order mark, and unknown
   bytes refused with the formats named.
3. **Offsets:** `summary` gives both sizes, and `Headers` reads a compressed report whose offsets exceed its size on
   disk (the `OverflowException`).
4. **Damage:** a truncated file and a damaged one each give one error from all five commands, so neither
   `InvalidDataException` nor a zstd exception escapes.
5. **The cap:** a small file that claims or decodes to more than the cap is refused.
6. **Versions:** `formatVersion` 3 with absent keys is read by the scanner, merge, `ctrf` and `history gate`; version 4
   gets the upgrade message.
7. **Lookup:**
   - a folder with only a `.krkl`, only a `.json`, or both (the newer wins and is named);
   - `runs/` and `baseline/` skipped;
   - the baseline lookup finds a `.krkl`, and never takes `Run.json` or `History.run.json`;
   - merge ignores a JSON attachment (#154's repro).
8. **Run outputs:** for a `.krkl`, `RunSummaryConsoleWriter` prints the query hint, the decoded size or none, and the
   new reason (F26), and `CiArtifactPublisher` publishes the file.
9. **query.py:** gzip on any Python, zstd on 3.14 or later, and the message otherwise.
   - It accepts and refuses exactly what the .NET reader does: a parity fact over the same inputs.
   - It decodes the marker with the one-shot call (§2.7).
   - `FallbackScriptTests` and `SkillDriftTests` are extended.
10. **Across implementations (A4):**
    - a file written by the copy decodes with Python's `compression.zstd` and the `zstd` tool;
    - files written by those decode with the copy;
    - Kronikol4J's gzip decodes in .NET.
11. **The snapshot:** a `.krkl` replaced after the scan answers from its snapshot, while a `.json` still throws
    `ReportChangedException`.

**R3, the scanner's guards.**
- **Memory.** `QueryStreamingTests` keeps its JSON facts. The `.krkl` path gets three of its own (F24):
  - apart from the snapshot's buffer, the scan allocates under a third of the decoded size, in step with the number of
    calls;
  - in total it allocates no more than the decoded size plus that bound, so the snapshot is held once, as bytes, never
    as text;
  - above the in-memory limit (lowered through an internal hook), nothing is held, and the temporary file is gone
    afterwards.

  Decoding runs on the scanning thread, because the allocation counter is per thread.
- **Attribution.** A `formatVersion` 3 file with no `stepPath`, no `failureMessage`, no annotation and a version that
  is not .NET's is read as attributed (F23).

**The copy (R3).**
- The import script, run again on the copy's own tag, changes nothing.
- No type from the copy is visible outside `Kronikol.dll`: a reflection fact over its exported types.
- `unsafe` appears only under the copy's folder: a source fact.
- `THIRD-PARTY-NOTICES.md` is in both packages: a pack fact.
- Round trips decode to their source at levels 3, 9 and 19 and at sizes from empty to 64 MB, as does a frame from
  ZstdSharp's own test data.

**R7 (net11.0).** The net11.0 build of `Kronikol.dll` contains no type from the copy. Files written by it and by the
net10.0 build decode in both: the hand-over test between the copy and `ZstandardStream`.

**R4 (writer).**
1. **Shape:**
   - The option writes `TestRunReport.krkl` and no `.json`.
   - The decoded content equals the JSON writer's model for the same run, minus nulls, empties, `$z` and whitespace.
     Both are parsed and compared.
   - Required keys are present even when empty, and no other key is null or empty.
2. **The pin** is of the decoded content, never the compressed bytes, which may differ between .NET versions.
3. **Peak memory:** an allocation fact, in the manner of `QueryStreamingTests`, that fails on the string-building path.
4. **The schema contract tests** run over a decoded `.krkl`.
5. **Merge:** `.krkl` and `.json` shards merge together, and a merge writes `.krkl` when any input is one.
6. **Rotation** moves a v4 `.json` into `runs/` when the first `.krkl` is written.
7. **Other outputs:** ingest writes the format when asked, and the agent files, Failures.md and `query.cs` name the
   `.krkl`.
8. **Size guard:** the fixture's `.krkl` stays under 10% of its `.json`.
9. **F13:** the two redaction tests read through the decoder. A sibling proves that the wiki's old recipe, an
   absence check on the raw text, passes on a `.krkl` whatever the file holds.
10. **Run.json** lists `TestRunReport.krkl`, and the fallback name when it is used, never the temporary file (F25).
11. **Merge:** a mergeable run with the option on writes a mergeable `.krkl`, and a merge of a shard with `$z` into a
    `.krkl` writes no `$z` (F28).
12. **Required keys:** every key the schema requires, at every depth, is present in a written `.krkl`, null where it
    may be null (§4.2).

**R5 (sub-steps on first expand).** Playwright:
- Expanding a closed composite inserts the markup the eager page had, compared with a 4.26.x render of the same
  fixture.
- A failed path renders open and eagerly, and a chain 20 deep opens with one decompression.
- `selectRow` keeps open states after insertion, and an exported page expands.
- With `DecompressionStream` removed, expanding a composite shows the failure line, as the internal-flow popup does.

Unit: small subtrees stay inline. `StepNumberingReportTests:59-102` moves to the expanded page.

**The test helper.** One `ReportFile.Read(path)` returns the decoded text. It serves the test files that read the data
file without the tool today (Appendix A.4: about 60 parse it, 32 read it as text), and replaces `ReportPayloadText` and
the xUnit2 lane's `ReportFile`.

## 6. Releases and records

### 6.1 Releases

| # | Release | Bump | What it changes | Blocked on |
|---|---|---|---|---|
| R0 | This plan and its harness | none | nothing in a package | nothing |
| R1 | `kronikol query sizes` (#119) and a bytes column with `--sort bytes` on `interactions --group-by` (#116) | 4.x minor | a new verb and column | nothing. It goes first so that the deep-steps consumer and the 183 MB consumer can measure their own files and answer Q4 |
| R2 | The status tooltip written once per page | 4.x patch | 54 to 209 bytes less per step in every HTML report | nothing |
| R3 | **The reader.** Every reader detects zstd (with Kronikol's marker or without) and gzip, and accepts `formatVersion` 3. Folders are searched for `*.krkl` as well. The F11 sites learn the extension. The ZstdSharp copy lands here with `THIRD-PARTY-NOTICES.md`, the import script and the monthly sync and advisory workflow (§4.10). `kronikol decode` is added (Q15). `query.py` reads gzip on any Python and zstd on 3.14 or later | 4.x minor | a new format accepted, a new verb, no package dependency | Q15, Q16. This is the release that has to reach the history action's `VERSION`, the skills and CI images long before v5 (F12) |
| R4 | **The writer, behind an option:** a new option, `CompressTestRunReport` (named in Q1), that writes `TestRunReport.krkl` as §4.1 to §4.3 describe (zstd 9, streamed, written atomically). The JSON format is unchanged | 4.x minor | reports that opt in shrink to about 5% | R3 published, so that every 4.x tool that can write the format can also read it |
| R5 | Collapsed sub-steps drawn on first expand (§4.8), with an option to turn it off | in v5 with the UI work, or a 4.x minor if Q8 says now | pages with many nested steps | Q8 |
| R6 | Kronikol4J writes `TestRunReport.krkl`, behind its own option, as a bare gzip stream (Q12). Its upload filter learns the extension, and its parity tests compare decoded content | a Kronikol4J release | Java reports | R4 |
| R7 | net11.0 added to Kronikol's targets and the tool's when .NET 11 is released. Those builds use .NET's own zstd and contain no copy (§4.10), and `query.cs` prefers them under an SDK 11 or later | 4.x minor | the copy leaves every .NET 11 process | .NET 11's release, and `ZstandardStream`'s released shape (A7) |
| v5 | **5.0.0:** `.krkl` is the default data format. The schema is no longer written by default (Q7). net8.0 and net9.0 are dropped (Q17). The docs are rewritten and a "Migrating to v5" page added. Together with the UI work of the parallel session | **major** | every report | R3 published at least one minor earlier, R4 run on BreakfastProvider, and the UI work ready |
| Later | #120: diagram notes as references to their call's body, inside the `.krkl`, as `formatVersion` 4 | by its own measure | about 10% more (F8) | Q6 |

### 6.2 Versions, by the repo's rule

- **R1, R3 and R4 are minors:** a new verb, a new format accepted, a new option.
- **R2 is a patch,** a change to report output that fixes size. Kronikol4J's ledger records the HTML divergence.
- **v5 is the major.** It flips two defaults, the data format and the schema, changes the name of the file consumers
  upload, and drops net8.0 and net9.0.
- **No release adds a package dependency.** zstd comes from the source copy on net8.0 to net10.0, and from .NET itself
  on net11.0 (§4.10). The copy's sync pull requests ship as patches: internal code, nothing new to call.
- **R7 is a minor,** because a new target framework is new surface. Dropping net8.0 and net9.0 is breaking, so it
  waits for v5 (Q17).

### 6.3 Records

- **Each release:** a changelog entry that says which part of the version moved and why, its wiki pages (Appendix A
  lists them by release), and a line in Kronikol4J's divergence ledger.
- **R1:** the new verb is in the skill's command list and in `VerbTable`, which `SkillDriftTests` enforces.
- **R3:** the history action's `VERSION` moves to the first tool that reads `.krkl`, and the skill's `query.py` copies
  are updated together, as `SkillDriftTests` requires.
- **R4:** BreakfastProvider opts in on one lane first, from a scratch clone, never from `C:/Code/BreakfastProvider`. It
  is the end-to-end check: the gate, the history action, Pages and the CI summary.
- **v5:** `CLAUDE.md`'s **MAJOR** line has pointed at this v5 since D39; once 5.0.0 ships it names v6. Its "Debugging a
  test run" section and `templates/agents/CLAUDE.md` name the new file. Its commits go on the `v5` branch, with
  changelog entries under `[Unreleased]` (§7). `PLANS_STATUS.md` records the plan as executed.
- **Doc comments made false** ship in the package, so fixing them is part of each release. They are in
  `ReportConfigurationOptions.cs`:
  - `:232` and `:239`, the data file and mergeable data;
  - `:249-259`, `$z`;
  - `:262-275`, step detail "measured in kilobytes against megabytes of payload";
  - `:316`, `query.cs` "beside a JSON data file";
  - `:469`, which names `TestRunReport.json`.

  Also the new option's own documentation.
- **Issues:** close #119 and #116 with R1, #154 with R3, and #121 with R4, each with a comment naming the release. #120
  closes when its follow-up ships.
- **The copy:** each sync names ZstdSharp's release in the changelog entry. `THIRD-PARTY-NOTICES.md` changes only when
  ZstdSharp's licence does.

## 7. Where it sits in the roadmap

The owner decided on 2026-10-18 that this plan ships in v5 together with the report UI work of the parallel session,
and that everything `ROADMAP.md` listed for v5 moves to v6, except toolbar Option C, which is that UI work. The list
was in `V5_PLAN.md` (now `V6_PLAN.md`) and `ROADMAP.md` stages 11 and 12, and `V4_PLAN.md:26-30` sums it up:
- Mermaid CI summaries;
- the Server, Local, IKVM and C4 removal;
- the five component diagram options nothing reads (`V6_PLAN.md` open question 8);
- the toggle-default unification;
- the maybes: numeric HTTP status in the data file, the dead `.examples-*` CSS rules, and dropping the JSON's
  indentation. The last is moot for the `.krkl`, which is compact, and stays open only for a run that keeps `.json`.

The move was made on 2026-10-18, at the owner's ask, as `ROADMAP.md` D39:
1. `V5_PLAN.md` is now `V6_PLAN.md`, as `V4_PLAN.md` became `V5_PLAN.md` on 2026-09-29, with a note at its top, and
   "v5" in it reads v6. A new `V5_PLAN.md` is the index of 5.0.0.
2. `ROADMAP.md` gains D39 and stage 1e, this plan's stage. Stages 11 and 12 are 6.0.0, D12 and D30 point to D39, and
   toolbar Option C (11.2, and its half of 12.1) moved to 1e.7.
3. The `PLANS_STATUS.md` rows that named v5 and `CLAUDE.md`'s **MAJOR** line say the same.
4. The doc comments of `CurrentStepTypeFetcher` on 24 options types, which put its removal in v5, now say v6 (under
   the changelog's `[Unreleased]`), and so do the wiki pages for those options.

D39 leaves one question open: whether 6.0.0 still comes before the launch, now that the "how it looks" half of the
old 5.0.0 is 5.0.0's.

**The `v5` branch.** The UI session's plan, `OPTION_C_PLAN.md` (committed on the local branch `v5` as `5594a857`,
green-lit on 2026-10-18), sets 5.0.0 as three pieces of work on one branch: all of Option C, the dependency filter's
OR default (`9452ddb5`), and this plan. The branch was cut from `origin/main` at 4.26.9 (`354cb998`) in the worktree
`Kronikol-v5`. It is not pushed (that plan's Q8), and its changelog collects entries under `[Unreleased]` until
5.0.0.
- R1 to R4, R6 and R7 ship from `main` as 4.x releases, and reach `v5` when `main` is merged into it.
- The v5 row of §6.1, and R5, are commits on `v5`.

**Where the two plans meet:**
1. **The status tooltip (R2).** Option C's stage S12 audits every `title` against a 53-row tooltip table (its D49).
   R2 lands on `main` first, so the audit meets the page's map and the delegated handler, not an attribute per step,
   and the table records that.
2. **Collapsed sub-steps (R5).** Option C redraws and re-indents steps (its items R17.16 and R17.18, stage S12) and
   replaces the stylesheet (S1). R5 is built on `v5` after S12, against that markup, and §4.8's claim that an
   inserted subtree looks the same is checked again against the new stylesheet.
3. **Export Filtered HTML.** Option C makes exports carry its look (its Q6), and R5 adds `#step-data` to what an
   export carries and prunes. One change to `report-export-function.js`, made once, on `v5`.
4. **The parity harness.** Option C's parity run merges `bp3-TestRunReport.json` with `--no-json` (its §6). If the
   suite runs on the branch's own packages, it writes a `.krkl` once `v5` flips the default. The flip's commit then
   updates the harness, or the flip waits for Option C's last stage (S14).
5. **Kronikol4J.** Both plans add divergence ledger entries. Neither ports its page changes to Kronikol4J; this
   plan's Java work is R6 alone.

v5 also drops net8.0 and net9.0 from Kronikol's targets (Q17, decided).
- **The consequence for consumers.** Every project under `src/` but one builds from `Directory.Build.props`'
  `MultiTargetFrameworks` (60 of 61; `Kronikol.Tool` is net10.0 only), so a suite on net8.0 or net9.0 stays on 4.x.
  "Migrating to v5" says so.
- **What the change touches:**
  - `Directory.Build.props:6`;
  - `Kronikol.csproj`'s per-target `Microsoft.AspNetCore.Mvc.Testing` references (`:84-86`);
  - the `Framework` choices in the twelve templates' `template.json`;
  - CI's scaffold job, which builds `kronikol-xunit3:net8.0` and `kronikol-tunit:net9.0` (`ci.yml:395-397`);
  - the test projects and fixtures that name either target (AssertionTracking, xUnit2 fixtures, Kafka build
    interception, EF Core relational);
  - two tests that name them in code: `tests/Kronikol.Tests/Packaging/TemplateTestHostTests.cs` (`:62`) and
    `tests/Kronikol.Tests/Tool/QueryScriptEndToEndTests.cs`, whose strict fixture targets net8.0 (`:48`, `:192`,
    `:348`).

## 8. Found on the way, not in this plan

- **The fallback readers search too much.** `query.py` (`load`, lines 40-56) and `kronikol history import`
  (`HistoryCommand.Maintenance.cs:497`) do not skip `runs/` and `baseline/` when they search a folder, as the .NET
  lookup does (`QueryCommand.cs:437-478`). A 4.x patch.
- **`init-agents` exposes the reports to search.** The `.ignore` it writes re-includes `bin/**/Reports/**`
  (`InitAgentsCommand.cs:56-67`), so `rg` sweeps reach today's JSON, which is 6 to 180 MB. A binary `.krkl` is skipped
  by `rg`. Until v5, the re-include could name the HTML, `Failures.md` and the agent files only.
- **The agent file names the wrong file for XML and YAML runs.** `agent-instructions.md:7` writes `__REPORT__.json`
  whatever the data format. A 4.x patch.
- **Sub-steps never draw their data tables** (F20): `RenderStep` passes `skipTabularInline: true` down to its
  children (`ReportGenerator.cs:3911` to `:3890`). This is a 4.x patch with its own failing test first, by the repo's
  rule that every bug found gets fixed.
- **The agent files understate the size.** `agent-instructions.md:8` and the skill's `SKILL.md:7` say a report
  reaches 10 MB, where the README (`:179`) already records 82.7 MB on one suite, and #119 reports 183 MB. A 4.x patch
  can raise the figure; v5 rewrites both for the `.krkl` (F26).
- **The wiki's redaction check misses compressed payloads today** (F13). Capture-Time-Redaction lines 69-74 read the
  data file as text, and since 4.0.0 that file holds every body and diagram source of 8,192 characters or more
  gzipped. The page should check through `kronikol query grep`, or have the checking test turn
  `CompressTestRunReportPayloads` off. A wiki edit, with no release. **Fixed 2026-10-18** (wiki `c6e85b9`): the page
  checks through `kronikol query grep` with every target named, since the default set leaves out headers and diagram
  notes, and a text assertion turns `CompressTestRunReportPayloads` off for its run.
- **The wiki calls the mergeable JSON payload-free.** Capture-Time-Redaction line 63 says it holds component
  relationships only and "is safe to share widely regardless". It holds every scenario's `httpInteractions`, with
  their `content` and `headers`, and its diagrams' PlantUML source (`ReportGenerator.cs:4976` through
  `BuildFeaturesJsonModel`, `:4816-4822`, `MapLogJson` `:5052`). Merging-Parallel-Reports line 26 says as much. A wiki
  edit, with no release, and an urgent one, since the page is about secrets. **Fixed 2026-10-18** (wiki `c6e85b9`).

## 9. Questions, with recommendations

1. **Q1. The file's extension and the option's name.**
   - Recommended: `.krkl`, the owner's choice, written when a new option, `CompressTestRunReport`, is on.
   - A separate option rather than a `DataFormat.Krkl` member, because `DataFormat` also serves the specifications file
     and the content is still JSON (F27).
   - `.kronikol` explains itself to someone who finds the file, at four more characters.
   - Not `.json.gz`: it invites `zcat | jq`, and the deep link would become `TestRunReport.json.html`
     (`QueryCommand.Narrative.cs:787`).
2. **Q2. The codec. Decided by the owner on 2026-10-18: zstd.**
   - Through a source copy of ZstdSharp on net8.0 to net10.0, and `ZstandardStream` on net11.0 (§4.10).
   - Brotli came within 3% and lost on portability and framing (F6). gzip alone is 77% larger.
3. **Q3. The zstd level.**
   - Recommended: 9 (279,968 bytes, 42 ms in .NET).
   - 3 if write time matters more than size: 346,012 bytes in 19 ms.
   - 19 is too slow for a 100 MB report: about 30 s.
4. **Q4. Measure a large report before R4 ships.**
   - Recommended: yes, one of #119's or #120's 100+ MB reports, through R1's verb and this harness.
   - It settles the window question (F5, and whether long-distance matching pays) and Q5 (memory).
5. **Q5. `$z` inside the `.krkl`.**
   - Recommended: off.
   - Alternatively, only for a payload of 1 MB or more, to bound the decoded working set. #120 cites a 1.71 MB
     BigQuery response that took 3.41 MB as content. Decide on Q4's numbers.
6. **Q6. #120 (notes as references).**
   - Recommended: after v5, as `formatVersion` 4, for its measured 10%.
   - Inside v5 if the large report of Q4 shows more.
7. **Q7. The schema.**
   - Recommended: v5 stops writing it by default but keeps `GenerateTestRunReportSchema` as an opt-in, so a consumer
     who validates decoded files has a way back.
   - The generator and contract tests stay as the internal specification.
   - Removing the option instead is also a major, and fits v5 or v6 equally.
8. **Q8. Collapsed sub-steps drawn on first expand.**
   - Recommended: yes, in v5 with the UI work, because both change the same page and scripts.
     - It is on by default for subtrees above a threshold of about 2 KB of HTML.
     - An option turns it off, for anyone who relies on Ctrl+F inside closed steps.
   - The trade is only the browser's Ctrl+F, which no longer finds text in a step nobody opened. Kronikol's own search
     finds it, from `data-search`, and opens the scenario.
   - A 4.x minor (R5), with the same option, if the deep-steps consumer's page measures as unusable before v5
     (`html_steps.py`, A1).
   - The alternative is not to do it, and rely on R2 and the JSON change alone.
9. **Q9. The XML and YAML data formats in v5.**
   - Recommended: keep them as they are, opt-in, for anyone who wants a readable data file. They cost nothing new.
   - Dropping them is a v6 question.
10. **Q10. The stopgap for the deep-steps consumer now.**
    - Recommended: tell them that `TestRunReportFullStepDetail = false` makes a step at depth 20 take 1,360 bytes
      instead of 5,086.
    - The cost: step parameters, text segments, doc strings and comments leave the data file, so `kronikol query
      steps` shows less. The HTML is unaffected.
11. **Q11. Where a decoded report lives.**
    - Recommended: in memory up to a limit (256 MB decoded), and in a temporary file above it, deleted when the
      command ends.
    - Refuse anything that would exceed a hard cap (4 GB), naming the cap (F15).
12. **Q12. Kronikol4J's codec.**
    - Recommended: a bare gzip stream through `java.util.zip`, which keeps the report module free of dependencies, as
      it is today.
    - zstd in Java means a library (aircompressor, pure Java) or a copy of one, as .NET does. A later release can
      choose it, and the reader takes both.
13. **Q13. A gzip writer in .NET as well.**
    - Recommended: no. One codec per writer keeps the test matrix small, and the reader takes both.
14. **Q14. `CompressTestRunReportPayloads` from v5.**
    - Recommended: ignored for `.krkl` (documented), `[Obsolete]` at v5 for the JSON format, and removed in v6.
15. **Q15. A `kronikol decode` verb.**
    - Recommended: yes, in R3. It writes the decoded JSON, indented on request.
    - It is the supported route for anyone who used to `jq` the file: the wiki's own recipe, consumers' gates, and the
      redaction check (F13).
    - It is a new verb, so it is listed here rather than assumed.
16. **Q16. Kronikol's marker as a zstd skippable frame.**
    - Recommended: yes (§4.1). It names the file and carries the decoded length for the cap, and standard zstd tools
      skip it.
    - Without it the file is a bare zstd frame. It is still recognised by its signature, but the cap is checked only
      while decoding.
17. **Q17. Drop net8.0 and net9.0 at v5. Decided by the owner on 2026-10-18: yes.**
    - Both leave support on 10 November 2026 (F22). v5 is the major that can carry the change.
    - The copy then serves net10.0 alone. §7 lists what the drop touches.
18. **Q18. CodeQL over the copy.**
    - Recommended: exclude its folder (`paths-ignore`), as is usual for copied third-party code. Rely on upstream's
      fuzzing, the advisory watch and the round-trip facts.
    - Scanning it is the stricter choice, at the cost of triaging alerts in translated C code.

## 10. Assumption ledger

| # | Assumption | How it gets checked | If it is wrong |
|---|---|---|---|
| A1 | The deep-steps consumer's report has the shape of the synthetic model in §2.4 | R1's `sizes` verb on one of their reports | The HTML and JSON halves are reprioritised; the format stands |
| A2 | zstd's window keeps its lead over gzip on large reports, where repeats lie further apart than in these 6 MB ones | Q4 | Try long-distance matching on the same file |
| A3 | Every reader of the file is in Appendix A | The census covered `src/`, `templates/`, `tools/`, `tests/`, the skill, Kronikol4J and the wiki. Consumers' own scripts are unknown | "Migrating to v5" says how to read a `.krkl` without Kronikol's library: `kronikol decode` or `kronikol query --json`, or `zstd -dc` for the JSON |
| A4 | Frames from the copy decode in libzstd, Python's `compression.zstd` and .NET 11's `ZstandardStream`, and the reverse | zstd's frame format is RFC 8878; the cross-implementation facts of R3 and R7 | Stop, and fix the copy before any writer uses it |
| A5 | A zipped artifact or a gzip-serving host does not make a `.krkl` larger | R4 on BreakfastProvider: the artifact's zip and the Pages response | Nothing to do; the file is already compressed |
| A6 | Decoding once per command stays fast at 180 MB | Q4. The copy decoded 4.5 MB in about 5 ms here, and 115 MB of synthetic content in 62 ms (§2.7) | A temporary file, or a scanner that reads the decoding stream directly (§4.4) |
| A7 | `ZstandardStream` ships in .NET 11 with the shape its previews show | R7 checks the released API before relying on it | R7 waits, and net11.0 builds keep the copy meanwhile |
| A8 | Old tools in the wild move to an R3 tool before v5 | The history action's `VERSION`, BreakfastProvider and the templates move with R3, and v5's migration page names the floor | Old tools fail on v5 files with "not valid JSON" (F12) |
| A9 | ZstdSharp stays maintained and keeps porting upstream zstd's fixes | The monthly workflow and the advisory watch (§4.10) | The copy is Kronikol's to patch until it leaves with net10.0 |

## 11. Log

- **2026-10-18:** written at 4.26.7 (`a8563d21`) in the worktree `C:/Code/Kronikol-krkl`, branch
  `plan/report-format`. Measured on BreakfastProvider's LightBDD, ReqNRoll and Docker LightBDD reports as published
  that day (written by 4.9.1), with the census of every reader in Appendix A. Records the owner's two decisions of the
  same day. Nothing committed.
- **2026-10-18, later the same day:** the owner chose zstd, through a source copy of ZstdSharp synced monthly and
  moving to `ZstandardStream` wherever a target has it (§4.10).
  - The first draft's brotli container gave way to zstd behind a skippable-frame marker. F16 had overstated why the
    tool should stay free of packages.
  - Added: §2.6, F21 (`ZstandardStream` is .NET 11 only), F22 (.NET 8 and 9 end of support), R7, and Q17 and Q18.
  - The owner answered Q17: v5 drops net8.0 and net9.0.
- **2026-10-18, verification pass** at the owner's ask ("make sure the plan is fully complete and accurate and do any
  deep dives that might change it"):
  - Every `file:line` reference was checked against `a8563d21`.
  - A second read-only pass covered how the code treats absent keys, the allocation guard, Run.json and the run
    summary.
  - The container was tested in Python and ZstdSharp (§2.7).
  - Changed: the option is a separate one, not a `DataFormat` member (F27). The always-written keys come from the
    schema at every depth (§4.2). Added F23 to F28, the scanner's guards, the run-summary wording, the public-API
    note, doc comments and issue closures in §6.3, and Appendix A rows.
  - The reference check read 384 references against `a8563d21`, Kronikol4J `92d6b28` and the wiki `2e66338`: 371
    were right, and the other 13, wrong or imprecise, are corrected. Among them: the tooltip's size (on
    BreakfastProvider's page the attribute is 112,374 bytes, its text 93,645), the baseline lookup (F11), the parse
    sites (F12), the scope of Kronikol4J's dependency rule, the v5 list in §7, and an R5 fallback decoder that does
    not exist.
  - Found while checking: the wiki's redaction recipe already passes vacuously in 4.x (F13), and the same page calls
    the mergeable JSON payload-free (§8). Both are wiki edits that need no release.
  - Then, at the owner's ask, both wiki errors were fixed (wiki `c6e85b9`), and this plan was committed.
- **2026-10-18, the roadmap move** (§7), at the owner's ask: `ROADMAP.md` D39 and stage 1e, `V5_PLAN.md` renamed
  `V6_PLAN.md` with a new `V5_PLAN.md` as the index of 5.0.0, the `PLANS_STATUS.md` rows, `CLAUDE.md`'s **MAJOR**
  line, 24 doc comments and the wiki pages that put a removal in v5 (wiki `450844e`).
  - Read against the UI session's `OPTION_C_PLAN.md` on the `v5` branch (`5594a857`), which names this plan as one
    of 5.0.0's three pieces: §7 gains the branch and the five places the two plans meet, and merge's `--no-json`
    keeps its meaning (§4.3). The line references hold at 4.26.9, whose releases changed none of the files cited.
  - A last read-through removed a sentence left from the first draft that gave `DataFormat` a new member (§4.5),
    and two counts of "30 sites" that the census had outgrown.

## Appendix A. Edit sites at `a8563d21`

Line numbers come from a read-only census of `origin/main`.

### A.1 Readers (R3)

| Site | What changes |
|---|---|
| `src/Kronikol/Query/QueryCommand.cs` `ResolveReport` `:437-478` | A folder is searched for `*.krkl` as well as `TestRunReport.json` and `*TestRunReport.json`, skipping `runs/` and `baseline/` as now. Q1 decides which wins when both exist |
| `QueryCommand.cs` `ResolveBaseline` `:519-545` (`:528-532`, the only `*.json` that is not a schema) | Try `TestRunReport.krkl` as it tries `TestRunReport.json`, and count only report files (`.krkl`, or a name ending `TestRunReport.json`), never `Run.json` or `History.run.json` |
| `QueryCommand.cs:183-185` | The "not valid JSON" message names the formats the tool reads |
| `src/Kronikol/Query/ReportScanner.cs:84`, window `:108-123`, slices `:240`, `:781`, `:934` | Scans the decoded bytes (§4.4) |
| `ReportScanner.cs:283-284`, `:764-770`, `:868-871` (`Enriched`) | A `formatVersion` 3 file counts as attributed (F23) |
| `src/Kronikol/Query/PayloadReader.cs`: seek `:44`, `$z` `:48-87`, `ThrowIfChanged` `:90-113`, header read `:130`, `:166-180` | Reads from the decoded source; the change check stays on the file on disk |
| `src/Kronikol/Query/BodyCache.cs:26` | Holds the decoded source for the command |
| `src/Kronikol/Query/ReportGate.cs:29-63` (help `:35`) | Accepts `formatVersion` 3, and names the version and the tool that reads it when it meets a higher one |
| `src/Kronikol/Query/RetainedRunResolver.cs:40`, `:98-103`, `:108` | Retained runs found by either extension |
| `src/Kronikol/Query/QueryCommand.Narrative.cs:787` | The HTML deep link strips `.krkl` as it strips `.json` |
| `src/Kronikol.Tool/CtrfCommand.cs:73-95` (help `:69`, `:203`, `:211`) | Through `ResolveReport` and the decoded source |
| `src/Kronikol.Tool/HistoryCommand.Maintenance.cs:166-181` (gate), `:497` (import) | Both extensions. Import also skips `runs/` and `baseline/` (§8) |
| `src/Kronikol.Tool/MergeCommand.cs`: `ResolveInputFiles` `:277-348` (the schema skip `:286`, `*.json` `:301`), names `:351`, output `:234`, `:263`, errors `:116-124` | Inputs are `*.krkl` and `*TestRunReport.json`, not every `*.json` (#154). The output takes the format of the run options |
| `src/Kronikol/Reports/Merge/MergeableReportReader.cs:24-29` (`File.ReadAllText`), `:32-67` | Reads the decoded text, and accepts `formatVersion` 3 with absent keys |
| `src/Kronikol/Reports/RunSummaryConsoleWriter.cs`: find recipe `:630`, `QueryTargetFor` `:677-684`, `QueryableDataFile` `:789-790`, `IsDataFile` `:808-813` | `.krkl` is a data file and is queryable (F11) |
| `RunSummaryConsoleWriter.cs:121-124`, `:159-162` (size), `:444` (the CI rule's reason), `:683` (`QueryTargetFor`) | The decoded size or none, the new reason, and `TestRunReport.krkl` exempted like `TestRunReport.json` (F26) |
| `src/Kronikol/Reports/CiArtifactPublisher.cs:18` | `.krkl` joins `PublishedExtensions` (F11) |
| `src/Kronikol/Reports/RunRotation.cs:412-414`, `:457` | Rotation knows the new extension |
| `.claude/skills/kronikol-test-debugging/scripts/query.py` and `templates/skills/kronikol-test-debugging/scripts/query.py`: `load` `:40-56`, inflate `:59-80` | Reads gzip with the standard library on any Python, and zstd with `compression.zstd` on 3.14 or later. Otherwise it names the tool and `query.cs`. Both copies are updated together |
| `src/Kronikol/Query/VerbTable.cs:90`, `:249`, `:253`, `:255`, `:314`; `QueryCommand.Describe.cs:36` (`--describe`'s `report` text); `src/Kronikol.Tool/Commands.cs:27`, `:33`, `:39` | Help and `--describe` text that name the file |
| `QueryCommand.cs:127`, `:555`; `QueryCommand.History.cs:120`, `:134`; `HistoryCommand.Maintenance.cs:467`; `MergeCommand.cs:76`, `:357`, `:374`; `IngestCommand.cs:658`, `:710`, `:714` | Error and help text that names `TestRunReport.json` |

### A.2 Writers (R4, v5)

| Site | What changes |
|---|---|
| `src/Kronikol/ReportConfigurationOptions.cs`: `TestRunReportDataFormat` `:440`, `GenerateTestRunReportSchema` `:236`, `CompressTestRunReportPayloads` `:259`, `TestRunReportFullStepDetail` `:275` | The new option `CompressTestRunReport` beside `TestRunReportDataFormat`; doc comments for the schema, `$z` and step detail under it. v5 flips the defaults |
| `src/Kronikol/Reports/ReportGenerator.cs`: queue `:525`, schema `:529-532`, planned files `:798-799`, `GenerateTestRunReportData` `:4503` and its format switch `:4516-4522`, options `:4716-4721`, JSON `:4723-4757`, mergeable `:4922`, `:4972`, `WriteFile` `:6251`, extension `:6350`, schema extension `:6358` | The new format's branch, a streaming writer (§4.5), and the extension |
| `ReportGenerator.cs:575` (Failures.md names the data file), `:626` (agent files), `:950-951` (`query.cs` written only for JSON) | Name the `.krkl`. `query.cs` is written for it unchanged, since the format is still JSON |
| `src/Kronikol/Reports/ReportPayloads.cs:32-112` | Not used for the `.krkl` (Q5) |
| `ReportGenerator.cs:514` (mergeable data only for JSON), `MergeableReportRenderer.cs:209-210`, `MergeableReportMerger.cs:60` (`$z` taken from the inputs) | Unchanged checks with the separate option; merge ignores `$z` for a `.krkl` (F27, F28) |
| `ReportGenerator.cs:6260`, `:6278`; `RunManifest.cs:68-73`; `RunRotation.cs:189` | Run.json records the final name, never the temporary one (F25) |
| `IngestCommand.cs:473-475` | The comment's reason for printing the size (F26) |
| `src/Kronikol/Reports/Merge/MergeableReportRenderer.cs:181`; `MergedRunOutputs.cs:96-99`, `:114-122` | Merge writes the format of its options, and no schema when the run would not |
| `src/Kronikol.Tool/IngestCommand.cs:387-388` (`--payloads`), `:462` (written files); `src/Kronikol/Ingestion/IngestPipeline.cs:347-354`, `:576` | Ingest writes the new format when asked (and by default at v5). Its file list names it |
| `src/Kronikol/Reports/agent-instructions.md:7` and `:112` (the data file), `:8-9` (its size); `AgentInstructionsGenerator.cs:35-39` | Name the real data file and its decoded size (§8, F26). `:89` names the HTML and stays |
| Kronikol4J `ReportFinalizer.java:231-259` (write), `:116` (mergeable), `:215-216` (upload filter), `:321` (fragments); `ReportOptions.java:177` | R6 |

### A.3 Documentation and agent files

- **Repo docs:**
  - `README.md:148`, `:152`, `:179`;
  - `nuget-readme.md:69`;
  - `src/Kronikol.Tool/Kronikol.Tool.csproj:22` (the package description);
  - `CLAUDE.md`, the "Debugging a test run" section and the **MAJOR** line.
- **Agent files and the skill** (both copies):
  - `templates/agents/CLAUDE.md:4`;
  - `SKILL.md:4`, `:7`, `:15`, `:27`, `:29`, `:46`, `:114`;
  - `references/commands.md:5`, `:429-430`, `:494`.
- **The history action:** `templates/github-actions/kronikol-history/gate/action.yml:9` and its `README.md:181` name the
  file in prose. `scripts/gate.sh` passes the input through and needs no edit.
- **Wiki, main pages** (`../Kronikol.wiki`):
  - **Generated-Reports**: `:11-12`, `:26`, `:29`, `:31`, `:117`, `:130-153` (with the "stays JSON" promise at
    `:147`), `:155`, `:256`, `:314-354` (Schema File), `:359-424`, `:891`, `:1012-1034`, `:1064`, `:1071-1081`,
    `:1110`.
  - **Querying-Reports**: `:3`, `:14`, `:76`, `:287-329`, `:388`, `:1050-1074`, `:1146`, `:1183`, `:1353-1361`,
    `:1386`, `:1446`, `:1471`.
  - **Merging-Parallel-Reports**: `:7`, `:26`, `:36`, `:58-59`, `:61`, `:67`, `:83`, `:100`, `:103-116`, `:121`,
    `:133-134`, `:178` and `:196-198` (CI upload paths), `:214-257`, `:270`, `:282`, `:301`, `:307`.
  - **Migrating-to-v4**: `:24-35`.
  - **Report-Configuration**: `:68`, `:71`, `:88-99`, `:186-189`.
  - **Marking-New-and-Changed-Tests**: `:30`, and `:120-174`, a `jq` recipe that has to become a `kronikol` command.
  - **Capture-Time-Redaction** (at wiki `c6e85b9`): `:13`; the mergeable JSON's line, `:63-66` (§8); and the
    Verifying section, `:70-91` (F13), whose text assertion must also turn `CompressTestRunReport` off once v5
    writes `.krkl`.
- **Wiki, other pages:**
  - CI-Artifact-Upload `:77-78`; CI-Summary-Integration `:66`, `:314`.
  - Diagnostics-and-Debugging `:9`, `:74`, `:163`.
  - Ingesting-External-Captures `:9`, `:39`, `:45`, `:253`, `:276`, `:536`, `:696-697`.
  - Cross-Run-History `:814`; AI-Integration-Prompt `:10`; API-Reference `:55`, `:59`.
  - Exporting-to-OpenTelemetry `:12`, `:56`, `:155`.
  - One-line field references: Tabular-Attributes `:107`, Step-Tracking `:149`, Phase-Aware-Tracking `:28`,
    Event-Annotations `:259`, Assertion-Tracking `:99`, Filtering-and-Redacting-Diagram-Content `:145`,
    Integration-OpenTelemetry-Extension `:7`, Integration-TcpTap-Extension `:236`, Integration-ProxyTap-Extension
    `:38`, Integration-Otlp-Extension `:3`, Diagram-Customisation `:154`.
- **A new page:** Migrating-to-v5.
- **Repo tools:** `tools/history-bench`'s `verify.py`, `volatile.py`, `pairwise.py`, `measure.py` and `churn.py` load
  a report with `json.load`, and `tools/query-bench`'s `gen` writes one (through `GenerateTestRunReportData`),
  `retok` scans one and `bench.ps1` names one (`:8`). Each learns the `.krkl`, or runs on `kronikol decode`'s output.

### A.4 Tests

- **Where the file is named.** 103 test files name it:
  - `Kronikol.Tests` 92 (Reports 42, Tool 30, Ingestion 15, others 5);
  - EndToEnd 5, the xUnit2 lane 4, ProxyTap 1, MongoDB 1.

  About 60 parse it with `JsonDocument.Parse(File.ReadAllText(...))`, 32 read it as text, and 39 go through the tool
  or the scanner.
- **Helpers:** `tests/Kronikol.Tests/ReportPayloadText.cs:12-21` (8 users) and
  `tests/Kronikol.Tests.xUnit2/Lane/ReportFile.cs:27`.
- **Pins:**
  - `ReportJsonEncodingTests.The_file_matches_its_pin` (`TestData/Reports/TestRunReport.pin.json`, 12,796 bytes);
  - `CiArtifactPublisherTests:20-25`;
  - `FallbackScriptTests` and `PayloadCompressionTests` (`query.py`);
  - `SkillDriftTests`;
  - `CompressedReportQueryTests` and `CompressedMergeTests`.
- **The schema:** `TestRunReportSchemaContractTests:21-66`, `SchemaClosedContractTests:51-61`, `SchemaValidationTests`,
  `YamlSchemaValidationTests`, `DataFormatParityTests:93-97`.
- **Tests that expect the schema file on disk:**
  - `ReportToggleTests:164`, `:175`;
  - `ReportsFolderPathTests:59`;
  - `RunRotationTests:404`, `:510`, `:803`, `:1013`;
  - `MergeWritesTheRunOutputsTests:151-152`;
  - the Ingest tests (`:281`, `:286`, `:102`, `:323`);
  - `OptionDiagnosticsTests:138`, `WarmUpDataTests:93`.
- **Tests that write a schema-shaped file of their own**, and keep working when none is generated:
  `ReportIdentityGateTests:56-68`, `MergeCommandTests:41-61`.
- **The allocation guard (F24):** `tests/Kronikol.Tests/Tool/QueryStreamingTests.cs:55-77`, `:79-97`, `:119-154`;
  `QueryCommandTests.cs:2158`, `:2168`, `:2178` assert one payload open (`PayloadOpens == 1`).
- **Raw-text secret checks (F13):** `CaptureRedactionTests:155-159`, `ProxyTapTests:431-434`. Each also asserts that
  a string is present, so on a `.krkl` they fail rather than pass.
- **Kronikol4J:**
  - `ReportDataParityTest` (`parity-harness/dotnet-capture/Program.cs:1913-1920`);
  - `ReportDataSchemaTest` (`:1362-1373`);
  - `REMAINING_PARITY.md:2393-2402`.

### A.5 The copy and the .NET 11 targets (R3, R7, v5)

| Site | What changes |
|---|---|
| `src/Kronikol/Compression/Zstd/` (new) | ZstdSharp's `src/ZstdSharp/` at a tagged release, internal and renamed, with `VERSION` |
| `tools/vendor-zstd/` (new) | The import script: copy, make internal, rename the namespace |
| `.github/workflows/vendor-zstd.yml` (new) | The monthly sync and the advisory watch |
| `THIRD-PARTY-NOTICES.md` (new) | ZstdSharp's MIT notice and zstd's BSD-3-Clause notice, packed into `Kronikol` and `Kronikol.Tool` |
| `src/Kronikol/Kronikol.csproj` | `AllowUnsafeBlocks`; the copy compiled only below net11.0; the notices packed |
| `src/Kronikol.Tool/Kronikol.Tool.csproj` | net10.0 and net11.0 (R7); the notices packed |
| `Directory.Build.props` `MultiTargetFrameworks` | net11.0 added (R7); net8.0 and net9.0 removed at v5 (Q17) |
| `.editorconfig` | The copy's folder marked as generated code |
| `.github/workflows/codeql.yml` or its configuration | `paths-ignore` for the copy, if Q18 says so |
| `src/Kronikol/Reports/QueryScriptGenerator.cs:117`, `:129` | Prefer the engine's net11.0 build under SDK 11 or later (R7) |
