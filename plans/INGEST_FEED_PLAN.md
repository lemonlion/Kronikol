# Ingest feed plan: the feed loses nothing (#93, #94, stage 1.7)

**Date:** 2026-09-22 · **Repo version:** 3.27.2 (`2843018a`) · **Status: EXECUTING (2026-09-23): R1
shipped as 3.27.4; R2 (3.28.0) and R3 follow.** §9 is the log. This is P1 of
[`STAGE_1_PLAN.md`](STAGE_1_PLAN.md): roadmap items 1.3 (#94), 1.4 (#93)
and 1.7 (two CLI defects), track D (`Ingestion/`, `RequestResponseLog.cs`, `IngestCommand.cs`), which
may run beside tracks A, B and C. It needs **D4** answered (§7). One probe was run against HEAD, and
the same evening a throwaway prototype of S2 was built in a detached worktree and measured; the probe
sources, their outputs and the prototype patch are kept in
[`INGEST_FEED_PLAN.harness/`](INGEST_FEED_PLAN.harness/); §1 says which claim rests on which.

**Revised 2026-09-22 (evening) against 3.27.3 (`25a0773a`), the gap audit the owner asked for.** It
added F7, a shipped defect the first pass missed (the ingest path never classifies a marker, so no
ingested run has ever carried a `stepPath`), and F8, a warning every ingest prints for a feature it
turns off; moved R1 to 3.27.4 because 3.27.3 shipped in between;
turned T5 to T7 and the `SeparateSetup` question in §11 from designs into measurements with the
prototype; ran S3 on a real suite with the prototype (§6.1); and corrected the wire shape (§4.2) and
the example-suite facts (§6) against the code.

Covers GitHub issues **#93** (`InteractionRecord` has no field for user-injected PlantUML, so the
NDJSON writer loses it) and **#94** (`InteractionRecord.FromLog` drops `DurationMs`), both filed on
2026-09-20 from `HISTORY_DASHBOARD_STORE_PLAN.md` §3.1, and the two defects roadmap 1.7 records: the
tool's `--render local` throws, and a real `kronikol ingest` wrote 0-byte `Specifications.html` and
`.yml` (`PLATFORM_FOUNDATIONS_PLAN.md` §11, "probable ingest-path bug, not fixed here").

**What the grouping asked for, and what this plan does with it.** `STAGE_1_PLAN.md` said: one
acceptance harness proves all three items (capture a real suite through the NDJSON writer, ingest it,
compare what came out with what went in); the log-to-record mapping sets the duration; the record
gains a field for user PlantUML with the member-by-member diff the store plan's R4 asks for; the
CLI's local render option stops throwing; the zero-byte Specifications output is verified before it
is fixed; two releases because D4 puts #94 in a patch; and the member diff will name six further
losses that are to be pinned as known gaps feeding 14.1, not fixed, "or it doubles in size". All of
that is kept. Two things the grouping did not know are in §0: the zero-byte output is not a defect,
and the writer corrupts an ingested diagram the moment a marker reaches it, which shapes the design
of #93.

---

## 0. Summary

The two issues are correct and undercount. Running the mapping over a fully populated log, over one
log of every marker kind, and over a whole capture written by the writer and read back by the
pipeline gave six findings the issues and the two plans that cite them do not state; the revision's
prototype added a seventh, on the ingest side rather than the writer's:

| # | Finding | Where |
|---|---|---|
| F1 | **The mapping loses 16 of 38 members, not 3 (store plan F7) and not 9 (its R4).** A fully populated `RequestResponseLog` through `FromLog` → JSON → `ToLog` comes back with 12 members lost: `ActionVariant`, `AttributionSource`, `CollapsedCount`, `CollapsedSummary`, `DurationMs`, `Error`, `ExpiredFromTestId`, `FocusFields`, `MarkerKind`, `NoteOnRight`, `PlantUml`, `SetupVariant`. Three more, `IsOverrideStart`, `IsOverrideEnd` and `IsActionStart`, read as "same" only because the probe record was not a marker; on a marker log they are lost too (F2), and `IsDiagramMarker` is derived from them. Of the 16: seven are this plan's (§3), two are set by the diagram pipeline and are excluded by design, seven are pinned for 14.1 | §3, harness §1 |
| F2 | **Every marker log the writer sees becomes the same junk line, and after ingest that junk is drawn.** A `Custom`, `Row`, `Step` or `Phase` marker, start half or end half, is written as `{"type":"Request","method":"","uri":"http://override.com/","serviceName":"","callerName":"","content":"",…}` with no `kind` and no PlantUML. Read back it is an ordinary request labelled `CALL` between two participants named `""`. A capture of two real pairs and seven marker halves ingested as **11 interactions**: the seven junk ones reached `httpInteractions` in `TestRunReport.json`, `annotations` was `[]` where the in-process report would hold the `Row` and `Custom` annotations, and the scenario's diagram source opens with `actor "" as ` and carries seven arrows of the form ` -[#438DD5]> : CALL: /` with no sender and no receiver. This is why #93 cannot be closed by a `plantUml` property alone: without a kind and a start/end flag the record still emits the junk for the two kinds it cannot rebuild and for the phase boundary | §2.2, §4.2, harness §2, §3, §6 |
| F3 | **#94 measured: a measured 77 ms came out as 49.9999 ms**, the timestamp delta, exactly as the issue predicts. What the issue does not say: **no shipped capturer or tap sets `RequestResponseLog.DurationMs`.** The only writers of that member are `InteractionRecord.ToLog` (a record that carried `durationMs`), `InteractionRecord.UserAction` and the merge reader. The "686 distinct values" the issue counted are the report's derived durations. So the loss is live for a run that was itself ingested or merged and is projected again, for `kind: ui` actions, and for a library user who builds logs with a measured duration and writes them through the writer; it is not live for a proxy, TCP or OTLP tap today. Still one line, still a defect in a documented contract | §2.3, §4.1 |
| F4 | **The 0-byte `Specifications.html` and `.yml` are the blank-on-a-failed-run rule, not an ingest bug.** The same capture ingested with a `passed` verdict wrote 472,431 and 149 bytes; with a `failed` verdict, 0 and 0. Both files are generated with `generateBlankOnFailedTests: true` at `ReportGenerator.cs:414` and `:424`, the rule the wiki documents for in-process runs (`Generated-Reports.md:43`). Nothing on the ingest path says so: not the console, not the ingest wiki page. Roadmap 1.7 said "verify the second before fixing it"; verified, and there is nothing in the pipeline to fix | §2.4, §4.4, harness §3, §4 |
| F5 | **`--render local` is an unhandled exception, not an error.** `TryParseRender` accepts `local`, the pipeline runs as far as the diagram fetcher, and `DefaultDiagramsFetcher.cs:220` throws `InvalidOperationException: PlantUmlRendering.Local requires a LocalDiagramRenderer to be configured. Install the Kronikol.PlantUml.Ikvm package and set LocalDiagramRenderer = IkvmPlantUmlRenderer.Render.` `IngestCommand` catches only `FormatException` and `FileNotFoundException` (`:350-359`), `Commands.Dispatch` catches nothing, so the real tool prints its `Ingesting …` lines, then `Unhandled exception.` and a stack trace, creates no output directory, and exits with the runtime's crash status (127 as Git Bash reports it), not the documented 1 or 2. The message tells the user to set a delegate the command line cannot set. The help text (`:445`) and the wiki (`Ingesting-External-Captures.md:180`) list `local` as a usable mode | §2.5, §4.3, harness §5, §7 |
| F6 | **In-process capture cannot reach the writer at all**, which is why F2 has never been seen and why the grouping's harness cannot be built the way it is phrased. `RequestResponseLogger.Log` (`:38-77`) redacts, truncates, stamps a time, resolves a held flow and enqueues; it fans out to no sink. `IRequestResponseSink` is consulted only by the three taps through their `Sink` option. A marker, a handler-captured call, a database call: none of them can be "captured through the NDJSON writer" today. "Capture a real suite through the writer" therefore means: run the suite in-process, then write the store's logs through the writer at run end, which is the projection `HISTORY_DASHBOARD_STORE_PLAN.md` M0.3 describes and foundations G2 records as missing. §5 builds the permanent fixture that way and §6 the one-off run | §2.1, §5, §6 |
| F7 | **The ingest path never classifies a marker: every ingested step bar and assertion note is a `Custom` marker.** `ToLogs` builds the override pair for a `step` or `assertion` record without setting `MarkerKind` (`InteractionRecord.cs:306-314`), so the log carries the enum default. Measured at HEAD: a tests file with one step and one assertion ingests to marker logs with `MarkerKind = Custom`, an `annotations` array listing the bar (`"kind": "Custom", "text": "hnote across <<stepDelimiter>> #black:<color:white>Given a basket"`) and the note (`"✓ The basket is empty\nend note"`), and `stepPath` null on every interaction. Three consumers switch on the kind: step attribution (`ReportGenerator.cs:4111-4142`; so **no ingested run has ever carried a `stepPath`**, and `Failures.md`'s "calls made inside that step" is empty for every ingested failure), the annotation export (`:4138-4139`), and the Setup partition's narration rule (`PlantUmlCreator.cs:243-268`, which treats an ingested step bar as a boundary that closes the partition). Live since 3.0.47 (`05b4e605`), the commit that added `MarkerKind` to the log and `DurationMs` to `ToLog` without touching the ingest builders, which makes it #94's twin. Nothing pins it: no ingestion test reads `annotations` or `stepPath`. One assignment fixes it; with it the same input gives `Step` and `Assertion`, `annotations: []` and `stepPath: "0"` on both interactions | §2.2, §4.5, harness `p1proto-at-head.txt` §1 and `p1proto-after-s2.txt` §1 |
| F8 | **Every `kronikol ingest` warns that activity diagrams will be empty, for a feature the ingest path turns off.** `ReportDiagnostics.Analyse` (`:53-55`) adds `Warning: InternalFlowSpanStore has 0 spans — activity diagrams will be empty.` whenever the span store is empty; it is not told whether `InternalFlowTracking` is on, and `IngestPipeline.DefaultOptions()` sets it off. Seen on the S3 run's console (§6.1). Same family as F4: the ingest console says the wrong thing where it should say nothing. No test pins the message | §4.6, §6.1 |

Three slices, two releases:

| Slice | What | Bump |
|---|---|---|
| **S1** | #94 (`DurationMs = log.DurationMs`), F7 (the ingest builder sets `MarkerKind`, §4.5), F8 (the internal-flow warning only when the option is on, §4.6), the `--render local` refusal as a usage error with a message the command line can act on, one console line on the ingest path saying why the specification files are blank, the member-diff guard test with the pinned-gap list, and the documentation that goes with each | **patch** (§7) |
| **S2** | #93 as a marker record: `kind: "marker"` with `markerKind`, `plantUml` and `markerEnd`, emitted by `FromLog` for every marker log and restored by `ToLogs` as the same override half, `IsMarker` widened so the three ingestion sites that skip markers skip these too, the tests-file step de-duplication rule, and the in-process-versus-ingest fixture asserting byte-identical diagram source and annotations. Prototyped: `harness/s2.prototype.patch` (73 lines over two files) is the shape, measured in §1 | **minor** (D4) |
| **S3** | The one-off acceptance run on a real suite, projected at run end, and its log in §9 | none (plan file, and a test-only hook gated by an environment variable) |

**Not taken:** the seven members pinned for 14.1 (§3, table 3.2; `Error` is the one worth a decision,
Q4), the writer emitting step and assertion *structure* (`text`, `keyword`, `table`, …) rather than
verbatim PlantUML (store plan M0.2, and 14.1's row), a projection command or a sink hook on
`RequestResponseLogger` (M0.3, G2, 14.1), and any change to what the report generator does with
markers, annotations or durations.

---

## 1. How far each claim was checked

- **RUN**: executed against HEAD `2843018a` on 2026-09-22, output read. The probe is
  `INGEST_FEED_PLAN.harness/P1ProbeTests.cs`, a temporary xUnit fact that was compiled into
  `Kronikol.Tests`, run alone (`--filter FullyQualifiedName~P1ProbeTests`, 548 ms), and deleted
  from the test project; `p1probe.txt` beside it is what it wrote, plus the ingested diagram source
  and a run of the built tool. To repeat it: copy the file into `tests/Kronikol.Tests/Ingestion/`,
  run the filter, read `%TEMP%\p1probe.txt`, delete the file.
- **RUN (prototype)**: executed on 2026-09-22 in a detached worktree at `25a0773a` (3.27.3) with
  `s2.prototype.patch` applied: `Kinds.Marker`, the three members, `IsMarker` widened, `FromLog`
  and `ToLogs` per §4.2, `OverrideLog` classifying (§4.5), the steps-twice rule in
  `AddDiagramMarkers`, and nothing else. `P1PrototypeProbeTests.cs` is the second probe: it
  measures F7 on a tests file, renders one fixture (§5, T7's) in-process through the store and the
  generator and again through the writer and the pipeline, compares `httpInteractions` member by
  member, `annotations`, and the diagram source byte for byte, with and without `SeparateSetup`,
  then adds a tests-file step to a capture that already carries the writer's step marker.
  `p1proto-at-head.txt` is its output before the patch and `p1proto-after-s2.txt` after. The
  worktree was removed; the patch is the record.
- **READ**: the code was read and the claim follows; nothing executed.
- **ISSUE**: taken from #93 or #94 as filed.

| Claim | Level |
|---|---|
| `FromLog` drops `DurationMs`; `ToLog` restores it | **RUN** (harness §1: `123.4` → `null`) and **ISSUE** |
| The report degrades a measured duration to the timestamp delta | **RUN** (harness §3: response `DurationMs = 77` on a 50 ms pair reads `durationMs: 49.9999`; a 5 ms pair with `5` on both halves reads `4.9999`) |
| 12 of 38 public members do not survive `FromLog` → JSON → `ToLog` | **RUN** (harness §1, the table) |
| The three marker flags are lost on a real marker log | **RUN** (harness §2: five marker shapes, every one read back with all three flags false and `MarkerKind = Custom`, the enum default) |
| Every marker kind is written as the same junk line, with no `kind` and no PlantUML | **RUN** (harness §2) |
| The junk reaches `httpInteractions`, empties `annotations`, and draws in the diagram | **RUN** (harness §3, §6: 11 interactions of which 7 junk, `annotations: []`, `actor "" as ` and seven empty arrows in the `@startuml` source) |
| No shipped capturer or tap sets `RequestResponseLog.DurationMs` | **RUN** (`grep "DurationMs = "` over `src/`: `InteractionRecord.cs:249` and `:330`, `MergeableReportReader.cs:518`, the history analyzer's own type; nothing under `Kronikol.Extensions.*` or `Tracking/`) |
| `RequestResponseLogger.Log` fans out to no sink | **READ** (`RequestResponseLogger.cs:38-77`; the only sink consumers are `OtlpTapOptions.Sink` and the two other taps' `Sink` options) |
| `Specifications.html` and `.yml` are 0 bytes on a failed ingest and populated on a green one | **RUN** (harness §3, §4) |
| That is the `generateBlankOnFailedTests` rule, shared with in-process runs | **READ** (`ReportGenerator.cs:414`, `:424`, `:952-953`, `:5234-5235`; `Generated-Reports.md:43`) |
| `--render local` escapes `IngestCommand.Run` as `InvalidOperationException` before any output is written | **RUN** (harness §5) |
| Through the real tool it is `Unhandled exception.`, no output directory, a crash exit status | **RUN** (harness §7, `dotnet Kronikol.Tool.dll ingest … --render local`) |
| `IngestCommand` catches only two exception types and `Commands.Dispatch` none | **READ** (`IngestCommand.cs:350-359`, `Commands.cs:95-96`, `Program.cs:25`) |
| `Custom` markers come from a public API and `Row` markers from tabular inputs; neither can be rebuilt from structure | **READ** (`TrackingDiagramOverride.cs:70`, `:113`; `TabularInputs.cs:84-85`; `DiagramMarkerKind` at `RequestResponseLog.cs:146-165`) and **ISSUE** |
| `Step` and `Assertion` survive today only because `ToLogs` rebuilds them from `text`/`keyword`/`passed`/`message` | **READ** (`InteractionRecord.cs:259-274`) and **ISSUE**; but only when the record came from a tests file or an external capturer, never from the writer (F2) |
| OTLP export never exports a marker, so S2 does not change what `kronikol export` sends | **READ** (`OtlpSpanMapper.cs:268`: `IsDiagramMarker || TrackingIgnore` are always skipped) |
| The merge path rebuilds markers from `annotations`, not from records, so S2 does not touch it | **READ** (`MergeableReportReader.cs:539-546`) |
| Kronikol4J has no NDJSON path, so no divergence-ledger entry is due | **READ** (foundations G8; `grep -ril ndjson` over the Java tree matches nothing) |
| An ingested `step` or `assertion` marker carries `MarkerKind.Custom`, is exported as a `Custom` annotation, and attributes no `stepPath` (F7) | **RUN** (`p1proto-at-head.txt` §1: `MarkerKind=Custom` on all four marker logs, two `Custom` annotations, `stepPath` null on both interactions; **RUN (prototype)** `p1proto-after-s2.txt` §1: `Step`/`Assertion`, `annotations: []`, `stepPath: "0"` on both) |
| After S2 the in-process and the ingested report agree on every `httpInteractions` member but `error`, on `annotations`, and on the diagram source byte for byte (T7) | **RUN (prototype)** (`p1proto-after-s2.txt` §2: 6 and 6 interactions, five identical and one differing only in `error: "HttpRequestException: boom" vs null`; annotations identical; diagram source 853 of 853 chars identical. At HEAD the same fixture gave 6 against 15 interactions, `[]` annotations and 57 differing diagram lines) |
| The same holds with `SeparateSetup = true`: the replayed `Phase` marker partitions Setup and Action identically | **RUN (prototype)** (`p1proto-after-s2.txt` §3: 883 of 883 chars identical, `partition #F6F6F6 Setup` … `end` in both) |
| A capture carrying the writer's step marker plus a tests file with the same step draws one bar, not two, and the tests file still fills the step list and the `stepPath` | **RUN (prototype)** (`p1proto-after-s2.txt` §4: one `<<stepDelimiter>>`, one `<<assertionNote>>`, 6 interactions all `stepPath: "0"`; without the rule the bar would draw twice, and at HEAD the same input gave 15 interactions, 9 of them junk) |
| Timestamps survive the wire exactly; the `49.9999` in the first probe is that probe's own `AddSeconds(2.05)` truncating to a tick | **RUN (prototype)**: the second fixture is tick-aligned and every `timestamp` and derived `durationMs` compared identical; **READ**: `Timestamp` is a `DateTimeOffset` serialised at full precision (`InteractionRecord.cs:71`) |
| A C# member named `PlantUml` on `InteractionRecord` shadows the `Kronikol.PlantUml` namespace inside the record's static helpers | **RUN (prototype)**: two `CS0120` errors at `StepDelimiterPlantUml` and `AssertionNotePlantUml`; fixed by qualifying the two references (`Kronikol.PlantUml.StepBarPlantUml`, `Kronikol.PlantUml.DiagramWidth`) |
| `Replayed N interaction record(s)` counts replayed logs, markers included | **READ** (`IngestPipeline.cs:415`: `logs.Count`) and **RUN (prototype)**: 15 for the fixture's 6 interactions and 9 marker halves, before and after S2 |
| `--chronological` and `kronikol query annotations` exist as §6 uses them | **READ** (`IngestCommand.cs:137`, `QueryCommand.cs:240`) |
| The one-off run on a real suite (S3) | **RUN (prototype)**, §6.1 |

---

## 2. The feed today

### 2.1 Who writes records, who reads them

The wire type is `InteractionRecord` (`src/Kronikol/Ingestion/InteractionRecord.cs`), 29
`[JsonPropertyName]` members shaped like the report's `httpInteraction` plus attribution. It is
written by three things and read by two:

| Writer | Path | Ever carries a marker? | Ever carries `DurationMs`? |
|---|---|---|---|
| The three taps (proxy, TCP, OTLP) through `Sink = new NdjsonInteractionWriter(…)` | `NdjsonInteractionWriter.Log` (`:42`) → `InteractionRecord.FromLog` (`:164`) | No: a tap sees wire traffic and spans, never `DefaultTrackingDiagramOverride` | No: `MappedSpan.ToLogs` and the wire taps build logs with timestamps only (F3) |
| An external capturer in any language (the Playwright reporter's NDJSON among them) | writes lines directly, `kind: ui`/`step`/`assertion` included | Yes, as structured `step`/`assertion` records | Yes, when it measured |
| A library user writing their own `RequestResponseLog`s | `NdjsonInteractionWriter.Log` | If they include the store's logs: yes, and F2 applies | If they set it: yes, and #94 applies |

| Reader | Path |
|---|---|
| `kronikol ingest` / `IngestPipeline.Run` | `NdjsonInteractionReader` → attribution, ordering, `AddDiagramMarkers` (`IngestPipeline.cs:715`) from the tests file → `ToLogs` → `RequestResponseLogger.Log` → `ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment` (`:408`) |
| `kronikol export` | `NdjsonInteractionReader` → `ToLogs` → `OtlpSpanMapper` (markers skipped, `:268`) |

`FromLog` has exactly one production caller, the writer. `RequestResponseLogger.Log` (`:38-77`) has
no sink: in-process capture (HTTP handlers, database trackers, `StepCollector`, `Track`,
`DefaultTrackingDiagramOverride`) lands in the store and nowhere else. That is foundations G2, and it
is F6: the only way a store log meets the writer is code that reads
`RequestResponseLogger.RequestAndResponseLogs` and writes each one, which nothing shipped does.

### 2.2 What the mapping does with a marker

A marker is a `RequestResponseLog` built by `DefaultTrackingDiagramOverride` (`:10-31`, `:33-54`,
`:83-104`): empty method, `http://override.com`, empty participants, `IsOverrideStart` or
`IsOverrideEnd` or `IsActionStart`, `MarkerKind`, and for the opening half `PlantUml` wrapped in
newlines (`ToBufferedPlantUml`, `:56-62`). `InsertPlantUml` (`:70-74`) is a start half with the
fragment and an end half without. `FromLog` (`:164-193`) reads none of the six members that make it a
marker and writes the empty strings it does read, so every kind and every half comes out as the line
in F2. `ToLog` (`:200-251`) then builds a real request from it: `ParseMethod("")` is `CALL`
(`:431-432`), the URI is `http://override.com/`, participants are `""`. The diagram builder draws
what it is given (`PlantUmlCreator.cs:243-268` handles `IsOverrideStart`; a log without it is a
call), and the data writer lists it (`ReportGenerator.cs:4052-4054` skips `IsDiagramMarker`, which is
false). Harness §3 and §6 show both.

`Step` and `Assertion` are re-derivable **only** from a structured record (`ToLogs`, `:267-269`).
The writer never produces one. So today the writer loses all five kinds, and #93's table ("Step:
survives, Assertion: survives") describes the tests-file path, not the writer.

And on that path they survive as drawings only (F7). `OverrideLog` (`:306-314`) sets the two
override flags, the buffered PlantUML and the timestamp, and not `MarkerKind`; the log keeps the
enum default, `Custom`, which the enum's own comment reserves so that "an unclassified marker is
never mistaken for a known one". The diagram is right, because the builder draws whatever fragment
an override start carries. Everything that reads the kind is wrong: `AttributeInteractionsToStepPaths`
(`ReportGenerator.cs:4111-4142`) advances its step cursor only on `DiagramMarkerKind.Step`, so every
interaction of every ingested run has `stepPath: null` and the `StepAttributionMismatch` diagnostic
can never fire; the same loop exports `Row` and `Custom` starts as annotations, so each step bar and
assertion note becomes a `Custom` annotation whose text is the raw PlantUML; and the Setup partition
(`PlantUmlCreator.cs:243-268`) treats a `Custom` start as the boundary override that closes the
partition, where a `Step` start is narration that renders inside it. The tests file, the Cucumber
messages path (its markers become the same `step` records) and the Playwright reporter's NDJSON all
go through this builder. `p1proto-at-head.txt` §1 is the measurement.

### 2.3 What the mapping does with a duration

`RequestResponseLog.DurationMs` (`:110-115`) exists so that a capturer that measured the call, or
sent it as one record, can be believed over the timestamp delta:
`ReportGenerator.ComputeInteractionDurations` (`:4203-4229`) takes the first non-null `DurationMs`
of a pair and only then falls back to the delta. `ToLog` sets it (`:249`, added in `05b4e605`,
3.0.47, "closing the round trip"); `FromLog` was not touched in that commit and never has been.
That is #94's origin, and harness §3 is its measurement.

### 2.4 What the pipeline does with the specification files

`IngestCommand` takes `IngestPipeline.DefaultOptions()` (`:261`), which leaves
`GenerateSpecificationsReport` and `GenerateSpecificationsData` at their defaults (on), and the
generator writes both with `generateBlankOnFailedTests: true` (`:414`, `:424`): when any scenario
failed, `WriteFile(string.Empty, …)` (`:952-953`, `:5234-5235`). The rule is deliberate and
documented for in-process runs ("so a broken build cannot publish half-truths as documentation",
`Generated-Reports.md:74-76`). On the ingest path the console prints counts, diagnostics and the
run-summary pointer (`IngestCommand.cs:324-346`), none of which mentions the two files, and the
ingest wiki page never names them. A user whose first ingest is of a red run sees two empty files.

### 2.5 What the tool does with `--render local`

`PlantUmlRendering.Local` "renders locally at report generation time using a user-supplied delegate"
(`PlantUmlRendering.cs:14-15`): `ReportConfigurationOptions.LocalDiagramRenderer`
(`:103`). The command line has no way to supply one. `TryParseRender` (`IngestCommand.cs:420-430`)
accepts `local` anyway; the fetcher throws (`DefaultDiagramsFetcher.cs:217-223`) and deliberately
rethrows `InvalidOperationException` as "the caller's bug" (`:46-53`), which is right for the
library and wrong for the one caller that cannot act on the message. `V4_PLAN.md:167` deletes
`local` and `server` from the CLI at 4.0.0 and names this throw as "a latent bug"; nothing before
v4 is planned for it.

---

## 3. The member diff

`RequestResponseLog` (`src/Kronikol/Tracking/RequestResponseLog.cs:10-116`) has 38 public instance
members: 16 positional, 22 properties, one of them (`IsDiagramMarker`) derived. `InteractionRecord`
has 29 wire members. This is the diff the store plan's R4 asked for, produced mechanically by the
probe (harness §1, §2) and classified here. The guard test in S1 (§5, T2) pins this table: a member
in none of its rows fails the build.

### 3.1 Carried today (22)

`ActivitySpanId`, `ActivityTraceId`, `CallerDependencyCategory`, `CallerName`, `CapturedBy`,
`Content`, `DependencyCategory`, `Headers`, `IsUserAction` (as `kind: ui`), `MetaType`, `Method`,
`Phase`, `RequestResponseId`, `ServiceName`, `StatusCode`, `TestId`, `TestName`, `Timestamp`,
`TraceId`, `TrackingIgnore`, `Type`, `Uri`.

Two of these are lossy in a way the guard must state rather than assert away: `Phase = Unknown`
and `MetaType = Default` are written as absent and read back as the same default (exact), and a
`StatusCode` that is an `HttpStatusCode` is written as its number and parsed back (exact for
every defined code; a custom label round-trips as text).

### 3.2 Not carried (16), by what this plan does with each

| Member | Set by | Where it surfaces in a report | This plan |
|---|---|---|---|
| `DurationMs` | `ToLog`, `UserAction`, merge, library users | `httpInteractions[].durationMs` (wins over the delta) | **S1, #94** |
| `PlantUml` | `DefaultTrackingDiagramOverride`, `StepCollector.cs:100`, `Track.cs:456`, `TabularInputs.cs:85` | the diagram; `annotations[].text` for `Row` and `Custom` | **S2, #93** |
| `MarkerKind` | the same emitters | `annotations[].kind`; step attribution (`ReportGenerator.cs:4111-4142`); the Setup partition's narration rule | **S2** on the writer side; and **S1, F7**: the ingest builder for `step`/`assertion` records never set it, so the tests-file path loses it today too |
| `IsOverrideStart`, `IsOverrideEnd` | the same emitters | the diagram (the fragment is spliced between the halves) | **S2** |
| `IsActionStart` | `DefaultTrackingDiagramOverride.StartAction` (`:83-104`) | the Setup/Action partition when `SeparateSetup` is on (`PlantUmlCreator.cs:193-222`) | **S2** (`markerKind: Phase`) |
| `IsDiagramMarker` | derived from the three flags | every consumer that skips markers | follows from S2 |
| `CollapsedCount`, `CollapsedSummary` | "the diagram pipeline, never by capturers" (`:86-93`) | the `loop ×N` label | **excluded by design**: computed at render time from adjacent pairs; the store never holds them. The guard lists them as excluded with this reason |
| `Error` | `FailedSend` (3.18.0) | `httpInteractions[].error`, the fingerprint, the diagram's `!Type` | **pinned for 14.1.** Evidence, one string, and the report already exposes it; the one gap worth pulling forward (Q4). Not here, because it is a new contract field and the grouping said not to double the plan |
| `AttributionSource`, `ExpiredFromTestId` | 3.17.0 to 3.20.0 provenance | `httpInteractions[].attributionSource` and its schema `enum` | **pinned for 14.1.** Provenance of how a call got its scenario is meaningless to an external capturer and cannot be recomputed at replay |
| `FocusFields` | author-supplied rendering intent | the note | **pinned for 14.1** |
| `NoteOnRight` | author-supplied rendering intent | the note's side | **pinned for 14.1** |
| `SetupVariant`, `ActionVariant` | the capturing extension's verbosity rules (`:73-84`) | the note text per phase | **pinned for 14.1.** Not re-derivable at replay without the extension's options, which is the F4 options-file problem |

The roadmap's "six further losses" are the last five rows counted as the roadmap counts them (the
two variants as one). R4 named all of them; F1 adds the two collapsed fields and the three flags.

What the diff does **not** cover, on purpose: the reverse direction (record → log → record). A
record's `text`, `keyword`, `table`, `docString`, `passed` and `message` become PlantUML in `ToLogs`
and no log member holds them back; that is the store plan's M0.2 and 14.1's row, not this plan's.

---

## 4. Design

### 4.1 S1: `DurationMs` (#94)

One line in `FromLog`: `DurationMs = log.DurationMs,`. The JSON already has the member (`:101`),
the reader already parses it, `ToLog` already restores it (`:249`), and the report already prefers
it. The wire contract gains nothing: `durationMs` has been in the documented interaction format
since 3.0.44 and documented as reaching the report since 3.0.47.

One correction to the first probe's numbers: the `49.9999` it read is not a wire loss. The probe
built its response timestamp with `AddSeconds(2.05)`, which lands one tick short of 50 ms, and the
report derived exactly that. `Timestamp` is a `DateTimeOffset` serialised at full tick precision;
the prototype's tick-aligned fixture round-trips every timestamp and every derived `durationMs`
identically (§1). The finding stands: the measured `77` was dropped and the delta used.

The one thing to decide is what a **pair** carries. In-process a measured duration, when one exists,
sits on whichever half the capturer set it on; `ComputeInteractionDurations` takes the first non-null
of the pair. `FromLog` writes the member per log and `ToLog` reads it per log, so a pair round-trips
exactly whatever it held. No change to that.

### 4.2 S2: markers as records (#93 and F2)

**The shape.** One new `kind` and three new optional members, all camelCase on the wire, all
omitted when null:

| Member | Type | Meaning |
|---|---|---|
| `kind` | `"marker"` | one half of a diagram marker: a zero-length control record, never a request. Joins `interaction`, `ui`, `step`, `assertion` in `Kinds` |
| `markerKind` | `"Custom"`, `"Row"`, `"Step"`, `"Assertion"`, `"Phase"` | `DiagramMarkerKind` by name, the same strings `annotations[].kind` already uses in `TestRunReport.json` |
| `plantUml` | string | the fragment, **verbatim**: the buffered form with its newlines exactly as `RequestResponseLog.PlantUml` holds it, so the diagram source is byte-identical after replay. Absent on an end half and on a `Phase` marker |
| `markerEnd` | `true` | this is the closing half (`IsOverrideEnd`); absent means the opening half (`IsOverrideStart`), or for `Phase` the boundary (`IsActionStart`) |

`testId`, `testName`, `timestamp` and the two ids are written as for any log. `uri`,
`serviceName` and `callerName` are the strings the marker log carries (`http://override.com/` and
`""`): the reader's `required` members (`type`, `uri`, `serviceName`, `callerName`, `testId`) are
enforced by the serialiser for every record, and loosening them for markers would let a malformed
interaction through. `method` and `content` are omitted, since a marker has neither; `ToLogs`
restores the empty strings the store held.

Two example lines as the prototype wrote them (`p1proto-after-s2.txt` §2), the pair a
`DiagramMarkerKind.Step` bar produces; a `Custom` fragment differs only in `markerKind` and the
fragment:

```json
{"type":"Request","uri":"http://override.com/","serviceName":"","callerName":"","traceId":"c6887e9e-…","requestResponseId":"930c7fd6-…","timestamp":"2026-09-22T10:00:01+00:00","testId":"0af7…","testName":"Probe","kind":"marker","markerKind":"Step","plantUml":"\nhnote across <<stepDelimiter>> #black:<color:white>Given a basket\n\n"}
{"type":"Request","uri":"http://override.com/","serviceName":"","callerName":"","traceId":"35e7eb50-…","requestResponseId":"2675b405-…","timestamp":"2026-09-22T10:00:01.001+00:00","testId":"0af7…","testName":"Probe","kind":"marker","markerKind":"Step","markerEnd":true}
```

**`FromLog`.** For a log with `IsDiagramMarker`: `Kind = Kinds.Marker`, `MarkerKind =
log.MarkerKind.ToString()`, `PlantUml = log.PlantUml`, `MarkerEnd = log.IsOverrideEnd ? true :
null`, `Method` and `Content` null. For any other log: unchanged. A `Phase` marker is
`IsActionStart` with `MarkerKind = Phase` and no PlantUML (`:83-104`), so it needs no special case
beyond the kind.

**`ToLogs`.** A `marker` record yields exactly **one** log, the override half it stands for:
`OverrideLog`-shaped (`:306-314`, generalised) with `IsOverrideStart = !MarkerEnd`,
`IsOverrideEnd = MarkerEnd`, `MarkerKind` parsed by name (unknown or absent → `Custom`, the
enum's own rule for "unclassified"), `PlantUml` verbatim, `Timestamp` carried, and the two ids
carried through `ToGuid` rather than minted, so the guard (T2) can count them. `markerKind: Phase`
yields `IsActionStart = true` and neither override flag. The existing `step` and `assertion` kinds
keep yielding their **pair** from structure; they are untouched, and so is every external
capturer.

**Two facts the prototype supplied.** A C# member named `PlantUml` shadows the `Kronikol.PlantUml`
namespace inside the record's own static helpers (`StepDelimiterPlantUml`, `AssertionNotePlantUml`
use `PlantUml.StepBarPlantUml` and `PlantUml.DiagramWidth`), which fails to compile; the two
references are qualified, and the member keeps the name that matches `RequestResponseLog.PlantUml`
and the wire name #93 asked for (Q8). And forward compatibility costs nothing extra: the reader
validates no `kind` value, so a 3.27 tool given a 3.28 capture treats a `marker` line as an
interaction, which is exactly today's junk (F2), no worse; a 3.28 tool reads a 3.27 capture
unchanged.

**Why one half per record rather than one record per pair.** `StartOverride` and `EndOverride`
are public (`:10`, `:33`) and both take PlantUML, so a consumer can wrap a run of calls in a
fragment (`group` … `end`) with the calls between the halves. The writer sees one log at a time
and cannot know where the end will fall. One record per half is the only lossless mapping, and it
is what the in-process store holds anyway.

**Why a new kind rather than `plantUml` on `step` and `assertion`.** The issue's suggestion, a
`plantUml` property set in `FromLog` and restored in `ToLog`, closes `Custom` and `Row` but leaves
`Step`, `Assertion` and `Phase` as junk (F2), and it puts two contracts on one kind: a `step` record
with `text` yields a pair, a `step` record with `plantUml` would have to yield one half. `marker` is
one contract: raw halves, verbatim, whatever the kind. Structure stays primary for anything that
has it, exactly as #93 asks ("a fallback for kinds with no structured form, not a replacement").

**`IsMarker` widens** (`:134-136`) to `step`, `assertion` or `marker`. That is the one change the
three ingestion sites need: the call-tree ordering nests markers (`IngestPipeline.cs:879-884`),
phase-from-steps leaves them alone (`IngestAttribution.cs:487`), the wire/span merger never pairs
them (`InteractionMerger.cs:110`). `IsUserAction` is unchanged.

**Steps twice.** A projected in-process run carries its step bars as `marker` records of kind
`Step`. If the same ingest is given a tests file with `step` events for that scenario,
`AddDiagramMarkers` (`:715`) would add a second bar per step. Rule: when the interaction records
hold any `marker` record of kind `Step` for a test id, the tests file's `step` and `assertion`
events for that id draw no marker (they still fill the step list). This is the rule the Cucumber
path already applies to the reporter's own step events (`IngestPipeline.cs:336-337`), for the same
reason. A test pins it (T6).

**What does not change.** `httpInteractions` (markers were never listed), `annotations` (built from
the restored logs by the same code, `ReportGenerator.cs:4138-4139`), the report schema, the merge
reader (annotations, `MergeableReportReader.cs:539-546`), the `kronikol export` code (markers
skipped, `OtlpSpanMapper.cs:268`; what changes is its output for a writer-produced capture, which
today exports each junk marker line as a span and after S2 exports none), `kronikol query`, and
Kronikol4J (no NDJSON path; no ledger entry). The console line `Replayed N interaction record(s)`
counts replayed logs (`IngestPipeline.cs:415`), so a marker half counts one before and after; it is
a pipeline detail and no test asserts it.

### 4.3 S1: `--render local` on the command line

`TryParseRender` keeps recognising `local`; `Run` refuses it before the pipeline starts, as a usage
error (exit 2) with a message a command-line user can act on:

```
--render local needs a LocalDiagramRenderer delegate, which only the library API can supply.
Use nodejs for offline SVG (needs node on PATH) or browserjs (the default).
```

The help line (`:445`) says `browserjs (default, needs internet at view time) | nodejs | server;
local is library-only`, and the unknown-mode message at `:73` (`expected browserjs|nodejs|local|server`)
lists the same. The wiki row matches. Removing `local` from the accepted values would be
the cleaner shape, and `V4_PLAN.md` does exactly that at 4.0.0; doing it in 3.x would remove an
accepted option value, which the versioning rule reserves for a major, so the value stays and is
refused. `server` is untouched: it works, over the network, and v4 removes it too.

A library caller of `IngestPipeline.Run` with `Local` and no delegate keeps the exception: the
message names the fix and the fetcher's comment is right that it is the caller's bug.

### 4.4 S1: saying why the specification files are blank

Nothing in the pipeline changes. `IngestCommand.Run`, after `PrintDiagnostics` and before the
run-summary pointer, prints one line when the specification outputs were generated and any
scenario failed:

```
Specifications.html and Specifications.yml are blank: 1 of 1 scenario(s) failed, and the
specification is only published from a green run.
```

It reads `result.Features` (the same `Feature[]` the summary reads) and the two `Generate…`
options, so it says the file names the options chose. Why a console line and not a diagnostic:
`DiagnosticKind` has no fitting kind and a new one is public surface (a minor, as the 3.27.0
changelog counted one); `Other` would put an intended behaviour in a list whose documented meaning
is "silence means a clean run" (`IngestCommand.cs:366-369`). The ingest wiki page gains the
sentence; `PLATFORM_FOUNDATIONS_PLAN.md:529-530` is corrected in place ("verified 2026-09-22: the
blank-on-failed-run rule, not an ingest defect; `INGEST_FEED_PLAN.md` F4").

### 4.5 S1: the ingest builder classifies the markers it builds (F7)

`OverrideLog` (`InteractionRecord.cs:306-314`) gains a `DiagramMarkerKind` parameter and sets
`MarkerKind` from it; `ToLogs` passes `Step` for a `step` record and `Assertion` for an
`assertion` record. That is the whole change (the prototype patch has it), and it is a fix, not a
feature: the report's `stepPath`, `annotations` and Setup partition already do the right thing for
a marker that says what it is. Measured effect on the same input (`p1proto-after-s2.txt` §1):
`annotations` goes from two `Custom` entries to `[]`, and both interactions gain `stepPath: "0"`.

Three things follow for an ingested run once the kind is set, and each is what the in-process run
already does, so none is a behaviour to decide: a step bar that does not match the step list's
next step raises `StepAttributionMismatch` and leaves `stepPath` null after it; `Failures.md` names
the calls made inside the failing step; and under `SeparateSetup` a step bar before the first setup
call opens the partition rather than closing it. The changelog says the first two plainly, because
a consumer who ingests today sees `annotations` shrink to nothing and `stepPath` appear.

### 4.6 S1: the internal-flow warning knows the option (F8)

`ReportDiagnostics.Analyse` gains an `internalFlowTracking` argument, passed from
`options.InternalFlowTracking` at its one call (`ReportGenerator.cs:531`), and adds the
`InternalFlowSpanStore has 0 spans` warning only when it is true. An in-process run with the
default (on) and no spans keeps its warning; `kronikol ingest`, which turns the feature off,
stops printing one. Nothing else in the diagnostics changes.

---

## 5. Tests, in the order they are written

Every test is red first. All are unit tests in `tests/Kronikol.Tests`; nothing here touches the
report's UI, so there is no Playwright test to write, and the E2E suite's ingest fixtures are not
changed. File placement follows the tests already there.

| # | Test | File | Red on | Slice |
|---|---|---|---|---|
| T1 | `Round_trips_a_log_through_json_preserving_identity_and_pairing` gains `DurationMs = 123.4` on the log and `Assert.Equal(123.4, back.DurationMs)`; a second assertion that the JSON line contains `"durationMs":123.4` | `Ingestion/InteractionRecordTests.cs:12` | `FromLog` | S1 |
| T2 | **The member guard.** A fully populated log (every settable member non-default, as the probe's) through `FromLog` → `ToJson` → `FromJson` → `ToLog`; every public instance member of `RequestResponseLog` is compared by reflection and must be in exactly one of three sets: `Carried` (asserted equal), `ExcludedByDesign` (`CollapsedCount`, `CollapsedSummary`, `IsDiagramMarker`, each with its reason in the test), `KnownGaps` (§3.2's seven pinned members, each with its reason and "14.1"). A member in no set, or a `KnownGaps` member that turns out to round-trip, fails with a message naming the member and this plan. A `Carried` member that is lost fails naming it. In S1 `DurationMs` moves from lost to `Carried`; in S2 the five marker members move | new `Ingestion/RequestResponseLogRoundTripTests.cs` | today: the five marker members and `DurationMs` are in no set | S1, S2 |
| T3 | **`--render local` is refused.** `IngestCommand.Run(["x.ndjson", "--render", "local"], …)` returns 2 with the message of §4.3 on stderr and creates nothing; `TryParseRender("local")` still returns true; `PrintUsage` names `local` as library-only | `Tool/IngestCommandTests.cs` (beside `Render_mode_parsing`, `:175`) | the throw | S1 |
| T4 | **The blank line.** A two-line capture with a `failed` end record: exit 0, both specification files 0 bytes, stdout has the §4.4 line with `1 of 1`; the same with `passed`: both files non-empty, the line absent. The command line has no flag that turns the two files off (the pipeline's defaults leave both on), so the option-off case is a pipeline-level fact in `IngestPipelineTests`: with both `Generate…` options false nothing is written and there is nothing to say | `Tool/IngestCommandTests.cs`, `Ingestion/IngestPipelineTests.cs` | the line | S1 |
| T4b | **Ingested markers are classified (F7).** One pair plus a tests file with a `step` event before it and an `assertion` event after it, `CallTreeOrdering = false`: the four marker logs for the test id carry `MarkerKind.Step`, `Step`, `Assertion`, `Assertion`; `annotations` is `[]`; both interactions carry `stepPath: "0"`; no `StepAttributionMismatch` diagnostic. Then the mismatch case: a tests file whose step text differs from the bar's raises exactly one `StepAttributionMismatch` and leaves the paths null, as `ReportAttributionTests` already pins for the in-process path. Also a unit-level fact in `InteractionRecordTests`: `StepMarker(...).ToLogs()` yields logs with `MarkerKind.Step`, `AssertionMarker(...)` with `.Assertion` | `Ingestion/IngestPipelineTests.cs`, `Ingestion/InteractionRecordTests.cs:158` | today: `Custom`, `Custom`; two annotations; null paths (measured) | S1 |
| T4c | **The internal-flow warning follows the option (F8).** `ReportDiagnostics.Analyse` over logs with an empty span store: with `internalFlowTracking: true` the warning is present, with `false` absent; and `IngestCommand.Run` over a two-line capture prints no line containing `InternalFlowSpanStore` (console captured and scoped to the run's own directory, per the process-wide-capture rule) | `Reports/ReportDiagnosticsTests.cs` (or beside the existing warning tests), `Tool/IngestCommandTests.cs` | the unconditional warning | S1 |
| T5 | **Every marker half round-trips.** For each `DiagramMarkerKind` and each half (start with PlantUML, end without; `Phase` as `IsActionStart`): `FromLog` → JSON → `ToLogs` yields one log with the three flags, `MarkerKind`, `PlantUml` (byte-equal, buffering newlines included), `Timestamp`, `TestId` and `TestName` equal to the original; the JSON line has `"kind":"marker"` and no `"kind"` other than that; a wrapping pair (`StartOverride` with `group x` … `EndOverride` with `end`) round-trips as two halves with both fragments | `Ingestion/InteractionRecordTests.cs` (beside `User_actions_and_markers…`, `:158`) | junk lines | S2 |
| T6 | **No junk, no double bars.** The probe's capture (two pairs, seven marker halves) written through the writer and run through `IngestPipeline.Run` with in-memory `TestRecords`: `httpInteractions` has exactly 4 entries (the printed interaction count is a pipeline detail and is not asserted), `annotations` has `Row 3` and `cache warmed` with their kinds, the diagram source contains no `actor ""` and no ` -[` arrow with an empty sender, and the `Custom` fragment appears in it once. Then the same with a tests file carrying a `step` event for the scenario: the step bar appears **once** in the source | `Ingestion/IngestPipelineTests.cs` | F2; the double bar | S2 |
| T7 | **In-process versus ingest.** One fixture of logs (pairs with and without measured durations, a custom fragment, a row band, a step bar, an assertion note, a phase boundary, a failed send with `Error`, strictly increasing **tick-aligned** timestamps, no overlapping calls) rendered twice: (a) in-process: the logs through `RequestResponseLogger.Log`, the features from `FeatureSynthesizer.Build` over the same tests records, and `ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment`, the way an adapter's run end does it (the generator reads the store; `GenerateTestRunReportData` writes no `diagrams`, so it cannot serve here); (b) the logs written through `NdjsonInteractionWriter` and ingested with `CallTreeOrdering = false`, the same tests records and the same options. Compared per scenario: `httpInteractions` member by member, `annotations`, and `diagrams[0]` **byte for byte**, once with default options and once with `SeparateSetup = true`. The expected differences are enumerated in the test and must be the only ones: `error` on the failed send (pinned, §3.2) and nothing else. The prototype ran exactly this (`P1PrototypeProbeTests.cs`, `Compare`): 6 and 6 interactions, 5 identical, `error` the sixth's only difference, annotations identical, diagram 853 of 853 and 883 of 883 chars identical | new `Ingestion/IngestRoundTripTests.cs` | today: 6 vs 15 interactions, `[]` annotations against two, 57 differing diagram lines (measured) | S2 |
| T8 | The `IsMarker` sites: a `marker` record is never paired by `InteractionMerger`, never re-phased by phase-from-steps, and nests as a zero-length unit in call-tree order between its neighbours (the probe's order is preserved with `CallTreeOrdering = true`) | `Ingestion/InteractionMergerTests.cs`, `IngestAttributionTests.cs`, `IngestPipelineTests.cs` | S2's widening | S2 |

T7 is the harness the grouping asked for, at the size a patch train can carry. It does not claim
what 14.1 will claim (a byte-for-byte report from a real suite through a real capture mode); it
claims that for everything the writer sees, what comes out equals what went in, and it names what
still does not (the pinned seven, of which only `Error` appears in `httpInteractions`).

Three cautions from the memory of this repo's suites, written into the tests: every read of
`RequestResponseLogger.RequestAndResponseLogs` is scoped by the fixture's own test id and the store
is never cleared by a test (the pipeline clears it under `ClearExistingLogs`, which is why T6 and T7
share the `DiagramsFetcher` collection like their neighbours; T7's in-process half therefore runs
**first**, and reads its JSON before the ingest half clears the store; the prototype probe called
`Clear()` itself, which a permanent test must not); a report-HTML assertion anchors on an emitted
attribute or the JSON, never on a bare substring of the HTML, because the BrowserJs page holds every
diagram source gzipped (the probe's `'cache warmed' in html: 0` on a report that did carry it is
that trap in the raw); and fixture timestamps are built from whole milliseconds, never
`AddSeconds(2.05)`, or the derived durations land a tick short and the comparison reads it as a
loss (§4.1).

---

## 6. The one-off run on a real suite (S3)

The permanent fixture is synthetic by design. The grouping also asked for one real suite. Given F6,
the recipe is:

1. **A test-only projection hook**, gated by `KRONIKOL_PROJECT_NDJSON=<path>` and otherwise
   inert, in the run-end fixture of the chosen suite: write every log of
   `RequestResponseLogger.RequestAndResponseLogs` through `NdjsonInteractionWriter`. Not a library
   feature, not an option; a sink hook on the logger is 14.1's (G2) and this must not pre-empt its
   design.
2. **The suite.** One that emits every marker kind in-process: steps and assertions from the
   BDD adapter, `[HeadIn]` tabular inputs for `Row` markers (`Cake_Feature.cs` in
   `Example.Api.Tests.Component.LightBDD.xUnit2`, `…LightBDD.xUnit3` and `…LightBDD.TUnit`),
   `TrackingDiagramOverride.StartAction()` for `Phase` (`Cake_Feature.steps.cs:116` in the xUnit3
   example), and `TrackingDiagramOverride.InsertTestDelimiter(...)` for `Custom`
   (`Cake_Feature.steps.cs:127`; no example calls `InsertPlantUml` directly, and the first draft's
   "`TrackingDiagramOverrideTests` has two" was wrong). The LightBDD xUnit3 example has all five, is
   in-process (`WebApplicationFactory`, no containers), and is the one §6.1 ran.
3. **Run** the suite with the variable set, keep its own `Reports/` as the reference, then ingest
   the capture twice: through `IngestPipeline.Run` with the suite's own `ReportConfigurationOptions`
   (like for like: the CLI cannot set `SeparateSetup`, and its defaults differ from an in-process
   run's in `CollapseConsecutiveIdenticalCalls` and `InternalFlowTracking`), and through
   `kronikol ingest <file> -o <dir2> --chronological` (what a user gets). Compare the two reports
   with `harness/compare-reports.py`, which matches scenarios by id and prints counts, the members
   that differ and the first differing diagram lines, never a report's content. Without a tests
   file the ingested run has one scenario per test id named from `testName`, no steps and every
   verdict `Passed` (`ResultWhenUnknown`), which is fine for comparing interactions and diagrams
   and useless for anything else; that is the known limit until 14.1 writes tests NDJSON.
4. **Record** in §9: counts before and after S2, every difference, and which row of §3.2 or
   which ordering rule explains it. A difference no row explains is a new finding and a new row in
   §3.2 before S2 ships.

The expected differences going in: `error` on failed sends (pinned); interactions ordered by
enqueue in-process and by timestamp on ingest, identical only while no two calls of one scenario
overlap (concurrent calls will differ, and `--chronological` makes the ingested order the
timestamp order, not the enqueue order; the report of the difference is the deliverable, not a
fix); `suite` and `stableId`; step lists (no tests file).

### 6.1 The run, done with the prototype on 2026-09-22

`harness/real-suite-run.md` is the full record; the numbers:

- **The suite:** `Example.Api.Tests.Component.LightBDD.xUnit3`, 6 tests, all passed, 583 ms, with
  the hook of Q7 (`s3.projection-hook.patch`, a `RegisterGlobalTearDown` in
  `ConfiguredLightBddScope`). It wrote 85 lines for 6 test ids: 49 marker halves (`Step` 40,
  `Custom` 6 from `InsertTestDelimiter`, `Phase` 3 from `StartAction`) and 18 pairs. It emitted no
  `Row` and no `Assertion` marker, so those two kinds rest on the synthetic fixture (T5, T7).
- **Ingested like for like** (`P1RealSuiteIngestTests.cs`: the suite's options, `SeparateSetup =
  true`, `CallTreeOrdering = false`, no tests file): **6 of 6 diagram sources byte-identical**
  (570, 4116, 2479, 2737, 579, 576 chars), `annotations` identical, `httpInteractions` 14/14,
  10/10, 12/12 and 0/0 three times, with every member identical except `attributionSource`
  (`TestContext` in-process, `None` after ingest), which is §3.2's pinned row. `steps` 3 or 4
  against 0, the no-tests-file limit, so `stepPath` was excluded from the comparison. One
  diagnostic, `ResultDefaulted`, for the same reason.
- **Ingested by the tool with its defaults** (`--chronological`): exit 0, `Replayed 85 interaction
  record(s) into 6 scenario(s).`, 3 of 6 diagrams identical; the three scenarios with calls differ
  only where the in-process run drew `partition #F6F6F6 Setup` … `end`, because the command line
  has no `SeparateSetup` (Q11). Neither `CollapseConsecutiveIdenticalCalls` (on by default on the
  tool, off in-process) nor `InternalFlowTracking` changed a byte on this suite.
- **A difference no row explains:** none. No new row in §3.2.
- **A side observation** for the ingest path's console (F8): the tool printed `Warning:
  InternalFlowSpanStore has 0 spans — activity diagrams will be empty.` on a run whose options had
  internal-flow tracking off, as every `kronikol ingest` does.

---

## 7. Releases and bumps

`CLAUDE.md`: a fix is a patch, anything new for a consumer to call is a minor, the highest-ranking
change in a release decides. D4 (roadmap §3) recommends: #93 adds a public property, so it is a
**minor**; #94 is a true patch and ships alone.

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| **R1** | S1: #94, F7, F8, the `--render local` refusal, the blank-specifications line, T1 to T4c, the wiki and plan corrections | **patch**, 3.27.4 (3.27.3 shipped on 2026-09-22 with the failures-verb fix while this plan was being revised; the number is whatever the next patch is at execution) | Nothing new to call: one member the mapping should always have copied, one classification the ingest builder should always have set, a usage error where a crash was, one console line where silence was. Precedent: 3.27.1 and 3.27.2 counted new output lines as patches. F7 changes what an ingested report contains (`stepPath` appears, two bogus annotations vanish), which the rule says to call out in the changelog, not to inflate |
| **R2** | S2: `Kinds.Marker`, `MarkerKind`, `PlantUml` and `MarkerEnd` on `InteractionRecord`, the widened `IsMarker`, `FromLog`/`ToLogs`, the step rule, T5 to T8, the wiki field table | **minor**, 3.28.0 | New public members and a new value in a documented contract, per D4 and the rule. The four members are the whole of what 14.5 will freeze from this plan ("the contract freezes holding 1.4's `plantUml`") |
| **R3** | S3: the projection hook (test-only), the comparison script, §9 | none | Nothing in a package |

If the owner reads D4's "ships alone" as "#94 in a patch with nothing else", the two CLI fixes move
to R2 (Q1). They are patches either way and change nothing about the bump of the release they land
in.

**Per release**, in this order: the full suite green; `Directory.Build.props:4`,
`.claude-plugin/plugin.json:5` and `.claude-plugin/marketplace.json:14` to the same number
(`PluginManifestTests` holds the first two equal); the changelog entry stating which part moved and
why; the wiki; commit; tag `v3.27.4` / `v3.28.0`; push both; **read the CI run of the pushed
sha** before starting the next release (the 3.27.2 run is where the last Mongo flake was found).

---

## 8. Documentation

| Where | Change | Release |
|---|---|---|
| `CHANGELOG.md` | R1: five `Fixed` entries (#94 with the measured 77 dropped for the delta; F7, since 3.0.47, with what an ingested report gains and loses; F8; the refusal; the line), and the correction that 3.0.47's "closing the round trip" closed `ToLog` only. R2: one `Added` entry for the marker record with the two example lines, the `IsMarker` widening, and the step rule; a `Fixed` entry for F2 naming what a projected marker used to become | R1, R2 |
| `../Kronikol.wiki/Ingesting-External-Captures.md` | the field table (`:16-37`) gains `kind: marker`, `markerKind`, `plantUml`, `markerEnd` (R2); the `--render` row (`:180`) says `local` is library-only (R1); a sentence under `kronikol ingest` that the specification files are blank when any scenario failed, linking the Generated-Reports rule (R1); the steps bullet (`:82`) gains that ingested steps attribute `stepPath` to the calls under them from 3.27.4, as in-process runs do (R1); the callout "the round trip is lossless from 3.0.47" (`:39-43`) restated as what it is, capturer → report, with one sentence on what the writer direction lost until 3.27.4 / 3.28.0 (R1, R2); the `.NET` sentence at `:61` names `marker` records as what the writer produces for a diagram marker (R2) |
| `../Kronikol.wiki/Generated-Reports.md` | nothing: the blank rule is already there (`:43`, `:74-76`); the interaction-fields table's `durationMs` row already says "taken verbatim when the capturer measured it" | |
| `src/Kronikol/Reports/ReportGenerator.cs:6185` | the schema's `durationMs` description says "derived from the two timestamps" and nothing about a measured value; one clause, R1 (a doc comment in shipped output: patch) | R1 |
| `IngestCommand.PrintUsage` | the `--render` line (R1); nothing for markers (they are input format, documented on the wiki) | R1 |
| `plans/PLATFORM_FOUNDATIONS_PLAN.md:529-530` | the side finding corrected in place with a pointer to F4 | R1 |
| `plans/HISTORY_DASHBOARD_STORE_PLAN.md` | M0.1 marked done with the release numbers; `:1360`'s "M0's two writer fixes are PATCH" annotated with D4's ruling (#93 is a minor); R4's list extended by the two collapsed fields and the three flags (F1) | R2 |
| `plans/JAVA_PORT_PLAN.md:622`, `plans/NODE_PORT_PLAN.md` | the NDJSON field list in each gains the four members, so a port written from the plan carries them | R2 |
| `plans/ROADMAP.md` | 1.3, 1.4 and 1.7 marked shipped with the numbers; 1.7's second defect recorded as "not a defect (F4)"; 14.1's row gains "the writer's marker record is the shape 14.1's projection emits" | R2 |
| `plans/PLANS_STATUS.md`, `plans/STAGE_1_PLAN.md` | rows and the P1 line updated per release | each |

---

## 9. Log

### R1, 3.27.4 (2026-09-23)

Shipped as S1 specifies: #94 (`FromLog` carries `DurationMs`), F7 (`OverrideLog` takes the kind and
`ToLogs` passes `Step` or `Assertion`), F8 (`ReportDiagnostics.Analyse(…, internalFlowTracking)`, the
generator passing `options.InternalFlowTracking`), the `--render local` refusal at parse time (exit 2,
the two-line message of §4.3; the unknown-mode message reads `expected browserjs|nodejs|server; local
is library-only`), the blank-specifications line (`IngestCommand.ExplainBlankSpecifications`, printed
after the diagnostics from `result.Features` and the two `Generate…` options, with the generator's
`Failed`-only predicate), the schema clause, and T1, T2, T3, T4, T4b and T4c. The wiki, the changelog,
`PLATFORM_FOUNDATIONS_PLAN.md` §11 and roadmap rows 1.3 and 1.7 updated as §8 lists.

Departures from the plan as written:

- **T2 has a fourth set in R1**, `LostUntilMarkersAreRecords` (the five marker members), asserted
  lost on three marker probes (a `Row` start half, its end half, the `Phase` boundary), because "a
  member in no set fails" would have left the guard red for the whole of R1. R2 moves the five to
  `Carried` and retires the fact. `Carried` is asserted on the fully populated interaction probe only in
  R1: on the junk marker line a marker's `Method` (`""`) comes back as `CALL` (F2), so it cannot hold on
  the marker probes until S2. R2 asserts it there too.
- **T4b's mismatch case is not constructible on the tests-file path.** The bar and the step list are
  built from the same record, and `StepMarkerMatches` is a containment test, so they cannot disagree.
  It is pinned in R2's T6 instead, where a capture's `marker` record can carry a bar text the tests
  file's step does not.
- **T4c's command-level fact captures the console per thread** (`ThreadScopedConsole`), not per
  directory: the diagnostics lines name no directory, and the InternalFlow test classes run outside the
  serial collection, so neither `ConsoleLines.About` nor `InternalFlowSpanStore.Clear()` was safe. The
  unit-level fact expects whichever span-store line the store's current size earns with the option on,
  and none with it off.
- **F8 gates both span-store lines**, the warning and the `N span(s)` count, not the warning alone: with
  the feature off neither is information, and the command-level fact asserts no line at all.
- **One Kronikol4J ledger entry** (`../Kronikol4J/docs/REMAINING_PARITY.md`): the Java port copies both
  the schema sentence and the unconditional warning. §4.2's "no ledger entry" was about S2's NDJSON
  path, which still holds.
- Roadmap rows 1.3 and 1.7 were marked at R1, when they shipped, rather than at R2.

Full `Kronikol.Tests` suite before the release: 5,466 passed, 4 failed, the four `RetainedRunsTests`
facts for a `--baseline-run` option another session was adding at the time (its uncommitted test
edits, red by TDD; not this release's). The CI run of `25a0773a`, the previous release, was green.

---

## 10. Open questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | D4 says #94 "ships alone". Do the two 1.7 fixes ride in the same patch (R1) or wait for the minor (R2)? | R1. All three are fixes; one release fewer; the alternative is a third release or a minor carrying crash fixes it has nothing to do with |
| Q2 | `kind: "marker"` with `markerKind`/`plantUml`/`markerEnd` (§4.2), or #93's literal suggestion of `plantUml` on the existing kinds? | The marker kind. #93's shape leaves `Step`, `Assertion` and `Phase` as junk (F2) and overloads `step` with two contracts. The field name `plantUml` is kept, so 14.5's sentence still holds |
| Q3 | Should `FromLog` emit `Step` and `Assertion` markers as structure (`text`, `keyword`, …) instead of verbatim PlantUML, so a newer Kronikol re-renders them? | Not here. It needs the step collector's structure at the sink, which the log does not carry (`StepBarPlantUml` is one-way), and it is the store plan's M0.2 and 14.1's row. Verbatim is exact for the version that wrote it, which is the promise 14.1 tests |
| Q4 | Pull `Error` forward? One nullable string, evidence of a failed send, and `httpInteractions[].error` already exists | No, by the grouping's own rule (the pinned seven feed 14.1, or the plan doubles). If the owner wants it, it is a fourth member in R2 with one row in T2 and T7 moving from `KnownGaps` to `Carried`, and one line in the wiki table; nothing else changes |
| Q5 | The blank-specifications notice: a console line (§4.4), or also a diagnostic in `TestRunReport.json`? | Console only. A diagnostic would reuse `Other` for intended behaviour, against the list's stated meaning, and a new kind is a minor for one sentence |
| Q6 | Keep `local` as an accepted-then-refused value, or drop it from `TryParseRender` now? | Keep and refuse. Dropping an accepted value is the major-only shape by the rule; `V4_PLAN.md:167` drops it at 4.0.0 |
| Q7 | Where does the S3 projection hook live? | In the chosen example suite's run-end fixture, gated by the environment variable, never on `RequestResponseLogger` and never in a package. The prototype put it in `ConfiguredLightBddScope` as a `RegisterGlobalTearDown` (§6.1); it worked and is the recommendation |
| Q8 | The C# member for the fragment: `PlantUml`, which shadows the `Kronikol.PlantUml` namespace inside the record (two references to qualify), or another name such as `PlantUmlFragment`? | `PlantUml`. The wire name is the contract and #93's, `RequestResponseLog.PlantUml` is the member it mirrors, and qualifying two internal references is nothing. A different C# name would be the only member whose C# and wire names disagree |
| Q9 | On a marker line, write `method` and `content` as `""` (what the log holds) or omit them? | Omit (§4.2, as measured). `ToLogs` restores `""`; the diagram and the data files never read either on a marker; and the line reads as what it is, a control record |
| Q10 | F7 is a fix on the tests-file path, not part of #93 or #94. R1 or its own patch? | R1. It is one assignment, its test is the same shape as T6, and it is the twin of #94 by origin. Shipping it alone would be a fourth release for one line |
| Q11 | The tool cannot reproduce a suite that partitions setup: no `--separate-setup` flag, so the S3 run's default ingest lost the partition on 3 of 6 diagrams. Add the flag here? | Not here. A new flag is a new option (a minor, and four places to edit per the VerbTable rule), and the projection that would make it matter is 14.1's. Record it on 14.1's row in `ROADMAP.md` when R2 ships; until then a library caller passes the option |

---

## 11. What is not known

- ~~**Whether the real-suite run (S3) will show a difference no row of §3.2 explains.**~~ Run
  with the prototype (§6.1): none. The LightBDD adapter's logs round-trip to the byte; the one
  member that differs is the pinned `AttributionSource`. What the run could not test: `Row` and
  `Assertion` markers (the suite emits neither), a suite with overlapping calls (its calls are
  sequential), and a suite that sets `DurationMs` (none does, F3). Execution repeats the run on the
  shipped code before R2 and records it in §9.
- **Whether any consumer writes store logs through the writer today.** Nothing shipped does (F6);
  a library user could. If one does, F2 is live for them and R2 is the fix; R1 does not change what
  they see.
- ~~**How the four flags interact with `SeparateSetup` after replay.**~~ Answered by the prototype:
  identical to the byte for the fixture (`p1proto-after-s2.txt` §3, 883 of 883 chars), and §6.1
  says what the real suite, which runs with `SeparateSetup = true`, showed.
- **The exit status of the crash on Linux.** Measured on Windows only (127 under Git Bash). It is
  the runtime's unhandled-exception status either way and R1 replaces it with 2.

---

## 12. What this plan does not do

- Add a sink or capture mode to `RequestResponseLogger` (G2, 14.1 F1).
- Write tests NDJSON from .NET (G1, 14.1).
- Emit step or assertion structure from the writer (store plan M0.2).
- Carry `Error`, provenance, focus fields, note side or phase variants on the wire (§3.2, 14.1
  F5), or freeze the contract (14.5).
- Change how the report generator draws, lists or times anything.
- Touch `kronikol export`, `kronikol merge`, the query engine, or Kronikol4J.
- Remove `--render local` or `--render server` (V4).
