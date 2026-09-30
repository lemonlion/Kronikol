# INGEST_FIDELITY_PLAN research notes

Four read-only passes over the code at `00abca6` (4.0.0), 2026-09-30, one per item, kept here because the plan
cites only what it needs and whoever executes a slice needs the rest. Everything is READ unless marked INFERRED.
Where this file and the plan disagree, the plan's RUN evidence wins. Line numbers are at `00abca6` and will drift.

## A. Item 2 (S2): a failed call's `error`

**The member.** `RequestResponseLog.Error` (`src/Kronikol/Tracking/RequestResponseLog.cs:62-68`), `string?`, a
settable property: "the exception's message chain … The exception's type stands where the status would, behind a
bang (`!HttpRequestException`) … Null on a request and on a call that answered". Its partner is the positional
`OneOf<HttpStatusCode, string>? StatusCode` (`:23`): the `!Type` text lives in the status, not in `Error`.

**Setters** (a grep of `src/` for `Error =`): only `TestTrackingMessageHandler.cs:286` and
`CosmosTrackingMessageHandler.cs:131`, both `FailedSend.Describe(ex)`. `FailedSend.Status` is `"!" + type`
(`FailedSend.cs:16-20`), `Describe` the outer message then `" Caused by: "` and each inner one (`:23-30`).

- The HttpClient handler logs the request before the send (`:237-258`); on an exception, the response half
  (`:271-293`) then `throw;` (`:294`). The `try` covers the send (`:264`) and reading the body (`:265`), so a
  broken body after a real response also logs `!Type` and loses the real status.
- The failure half: `Type = Response`, the request's method, URI, trace and pairing ids, `TrackingIgnore`; content
  null, headers `[]`, status the string arm, `Error`, focus fields, timestamp, activity ids, phase, attribution
  source; no `DurationMs`, no dependency category.
- Cosmos (`:115-134`): method the label (or the verb at Raw), cleaned URI, `DependencyCategory: CosmosDB`, no
  timestamp (the logger stamps it), no phase variants, rethrows (`:135`).
- Failures recorded without `Error`: SQL diagnostic trackers (`SqlDiagnosticTracker.cs:118-127`, `:250`, `:310`:
  content the message, status `"Error"`); `TrackingProxy.cs:118-133` (500, `"Type: message"`);
  `GrpcTrackingInterceptor.cs:122-125` (mapped status, `"{StatusCode}: {Message}"`); ten SDK handlers with no catch at
  all (CloudStorage, SQS, S3, BigQuery, DynamoDB, SNS, AtlasDataApi, StorageQueues, EventBridge, BlobStorage; e.g.
  `BigQueryTrackingMessageHandler.cs:69` logs the request, `:95` sends unguarded, `:100` logs the response).
- 3.18.0 in `CHANGELOG.md:3319-3350`: "Minor - a scenario with two states, and a call that threw."

**Readers of `Error`** (only these): `ReportGenerator.MapLogJson` (`:4786-4813`, `log.Error` at `:4806`; nulls are
written, `:4463-4468`; the same mapper serves scenarios, the mergeable file and background blocks; markers filtered
at `:4274`, `:4685`); XML `:5055`; YAML `:5434-5435`; the generated schema (`GenerateTestRunReportSchema` `:5958`;
`$defs.httpInteraction` `:6409`, `error` `:6444`; XSD `:6624`), pinned by `tests/Kronikol.Tests/Reports/FailedSendDataTests.cs:43-93`;
`kronikol query http` (`QueryCommand.Payloads.cs:296-297`, the response half's address only; `http` has no JSON
mode, `VerbTable.cs:199-203`; no test covers the line).

**Readers of the status instead:** the diagram (`PlantUmlCreator.cs:1156-1170`: `rawStatus[0] == '!' ? rawStatus :
rawStatus?.Titleize()`, pinned `PlantUmlCreatorTests.cs:380-389`; the note is headers and content only, `:414`);
fingerprint and history (`InteractionShape.cs:182-207`, `"-"` for none at `:19`; `HistoryRunBuilder.cs:89-94`);
`Failures.md` and `.jsonl` (`FailuresDigestGenerator.cs:309-317`, table `:728-730`, JSONL `:906-907`; ranking by
`InteractionStatus.IsError`, `:349-362`, which counts `!X`, `InteractionStatus.cs:101-112`); `interactions`
(`Payloads.cs:119-120`, its JSON rows have no `error`, `:127-140`); `flow --errors-only` (`Narrative.cs:486-489`);
`services` and `--group-by`/`--sort errors`; `grep --in errors` searches scenario errors only (`Search.cs:96-101`);
the component diagram counts only `HttpStatusCode >= 400` (`ComponentFlowSegmentBuilder.cs:199-211`, `:240`,
`:491-496`) and only calls with a response (`:169-178`).

**`InteractionRecord`** (`src/Kronikol/Ingestion/InteractionRecord.cs`): 32 wire members, all `{ get; init; }`:
`type`*, `method`, `uri`*, `serviceName`*, `callerName`*, `content`, `headers`, `statusCode`, `traceId`,
`requestResponseId`, `timestamp`, `testId`*, `testName`, `dependencyCategory`, `callerDependencyCategory`, `phase`,
`metaType`, `kind`, `durationMs`, `text`, `keyword`, `table`, `docString`, `passed`, `message`, `markerKind`,
`plantUml`, `markerEnd`, `activityTraceId`, `activitySpanId`, `trackingIgnore`, `capturedBy` (* required).
Options `:29-34`: web defaults, `WhenWritingNull`, enums as strings. `FromLog` `:208-245` (a new
`Error = log.Error` beside the status at `:219-224`); `ToLog` `:252-303` (initializer `:294-302`; `!X` parses back
to the string arm, `:261-267`); `ToLogs` `:312-352` (`MarkerLog` builds on `ToLog`, `:365`). No
`requestResponseId` means each half derives its own id and they never pair (`:258`). `Pair(...)` (`:488-539`) has no
error parameter; an optional one breaks binary compatibility.

**Tests that flip:** `RequestResponseLogRoundTripTests.cs`: Carried 28 (`:21-54`), ExcludedByDesign 3 (`:57-62`),
KnownGaps 7 (`:68-77`, `Error` at `:70`); `Known_gaps_are_still_lost…` (`:140`) goes red once carried;
`Carried_members_survive…` (`:125`) is the red step; `The_marker_probes_set_every_member_a_marker_line_carries`
(`:113`) goes red when `Error` joins Carried (add it to `notOnAMarker`, `:118`, or set it on the fourth marker
probe, `:200-212`). `IngestRoundTripTests.cs`: the theory at `:36-39`, the failed send at `:155`, the pin at
`:84-85` and `:102-105` becomes `Assert.Empty(differences)`.

**OTLP.** Receiving: `SpanToInteractionMapper.Build` (`:343-367`) makes an error span `500` plus the status message,
never `Error`; the reader skips events (protobuf field 11), so `exception.*` never arrives. Exporting:
`OtlpSpanMapper.StatusOf` (`:312-330`) and `IsFailureText` (`:332-333`) leave `!X` as `Unset` (also
`ExportCommand.cs:173-201`, `OtlpExportSink.cs:243`, `:253`, `:289-290`; no test covers a `!` status,
`OtlpSpanMapperTests.cs:155-181`). `InteractionMerger.Adopt` (`:312-320`) keeps the wire record's members, so a future
`error` follows the wire; a wire call with no response loses the span's response (`:85-88`).

**Taps.** `ProxyTap` records nothing for a failed forward (`:281-295`, `:309-316`; answers 502 via
`TryRespondBadGateway`, `:572-588`; counted, `:90-92`). `TcpTap` records nothing either: a refused connect is one log
line (`:212-216`, `:251-254`); a connect timeout is caught as "Shutting down" (`:247-250`) with no line at all.

**Merge.** `MergeableReportReader.ReadInteractions` (`:473-527`) reads no `error`, `attributionSource` or
`expiredFrom`.

**Wiki lines:** `Ingesting-External-Captures.md` table `:21-40` (`statusCode` row `:26`), lossless note `:46-55`;
`Generated-Reports.md:265`; `HTTP-Tracking-Setup.md:110-118`; `Exporting-to-OpenTelemetry.md:32`;
`Integration-Otlp-Extension.md:111`. Plans naming the gap: `INGEST_FEED_PLAN.md:248` (§3.2), `:741` (Q4),
`ROADMAP.md` 14.1, `HISTORY_DASHBOARD_STORE_PLAN.md:1747`, `:1984`; the port plans' field lists
(`JAVA_PORT_PLAN.md:622`, `NODE_PORT_PLAN.md:1199`) lack `error`.

## B. Item 3 (S3): attempts and source locations

**Model** (`src/Kronikol/Reports/`): `Scenario.FailureCause` `:35` (a category; "Nothing may group on this
field"), `ExampleDisplayName` `:47` (no doc), `Attempt` `:65` (1-based; "every lane but Cucumber Messages today"
is null), `SourceFile` `:98` (project-relative, forward slashes, "Deliberately NOT the bare-file-name contract of
`ScenarioStep.SourceFile`"), `SourceLine` `:105` (the `Scenario:` line; every outline row shares it); also `Id`
`:9`, `EndedAt` `:72`, `ResultDefaulted` `:81`. `ScenarioStep.FailureMessage` `:25`, `SourceFile` `:28` (file name
only), `SourceLine` `:31` ("zero when unknown"; `StepCollector.cs:231` stores null). `Feature.SourceFile` `:21`
(Gherkin lanes; the first path seen wins). No column anywhere (only the unused `CucumberLocation.Column`,
`CucumberMessages.cs:92-93`).

**`TestRunRecord`** (`:33-154`), 32 members: event, testId, testName, feature, timestamp, status, durationMs, error,
stackTrace, text, keyword, keywordType, level, background, docString, docStringMediaType, table, bypassReason,
featureDescription, description, rule, tags, claims, outlineId, exampleValues, examplesBlockName,
examplesBlockDescription, examplesBlockIndex, name, path (attachment), mediaType, step (attachment index).

**In-process setters.** Scenario source: ReqNRoll only (`ReqNRollTrackingHooks.cs:192-193`, `:217-218`;
`ExamplesBlockResolver.ResolveSource` `:63-82`; `ScenarioInfoEnumerableExtensions.cs:70-71`, feature `:90-92`). The
unit-test adapters set none, on purpose (`CHANGELOG.md:4469`: their seams give "the absolute build-machine path,
which is not something to bake into a downloadable artifact"). Step source: assertions only (`Track.cs:50-54`,
`:75-79`, `:101-105`, `:417-430`, `SourceFileName` `:459-472`; the weaver `AssertionWeaver.cs:1997-1998`,
`:2168-2176`; `StepCollector.cs:204-231`, mapped `:449-451`); `Track` also writes an unread
`'__^*__:{file}:L{line}` PlantUML comment (`:452-454`). `FailureMessage`: `StepCollector.CompleteStep` (`:141`),
`CompleteStepAsync` (`:468-495`), assertions (`:229`); not ReqNRoll's or LightBDD's Gherkin steps.
`FailureCause`: xUnit v3 (`TestContextEnumerableExtensions.cs:54-56`), MSTest (`DiagrammedComponentTest.cs:62`).
`ExampleDisplayName`: nobody but the merge reader (`MergeableReportReader.cs:228`). `Attempt`:
`CucumberFeatureSynthesizer.cs:524` and `MergeableReportReader.cs:218` only.

**Retries per adapter:** no adapter knows about retries. Each enqueues one context per execution and de-duplicates
by the framework's id, keeping the first (xUnit v3 `UniqueID` `:24`; NUnit4 `Test.ID` `:27`, whose `TearDown`
overwrites duration and end per id; MSTest `class.method` `:22`, so INFERRED a retried test reports its first
execution and `[DataRow]` rows collapse; TUnit `:29`; BDDfy `:23`); ReqNRoll mints a new GUID per execution
(`ReqNRollTrackingHooks.cs:51-52`), so INFERRED a re-execution is a second scenario with the same `stableId`;
LightBDD uses `RuntimeId` (`FeatureResultExtensions.cs:82`). Merge keeps entries that differ by attempt
(`MergeableReportMerger.cs:320-326`, doc `:135`).

**Report use.** JSON: feature `sourceFile` `:4520`; scenario `failureCause` `:4536`, `attempt` `:4540`,
`sourceFile`/`sourceLine` `:4541-4542`, `exampleDisplayName` `:4551`; steps `:4841-4843`, `:4863-4865`. XML
`:4946`, `:4962-4969`, `:5105-5107`; YAML `:5225-5257`, `:5392-5397` (neither emits `ExampleDisplayName`). Schema:
feature `sourceFile` `:6145` ("Null on the lanes that cannot supply one (unit-test adapters, the tests NDJSON)"),
scenario `:6157` (`id` unique), `:6158` (`stableId` shared by retries), `:6167-6180`, steps `:6404-6406`; XSD
`:6579-6581`, `:6654-6673`, `:6750`. HTML: no source link, no attempt badge; `retry N` labels render as labels
(`:2038-2047`); history tooltip " on attempt N" (`HistoryHtml.cs:255-256`, `:263-265`); "Cause:" for failed
scenarios (`:51-52`, `:2089`, `:3141-3143`); the anchor map is keyed by id (`:1753-1769`), `#sid-` resolves to the
first match (`report-url-hash-function.js:10-24`). `Failures.md`: failed scenarios only (`:154`); `written at`
(`:661-662`); `Thrown at` via `FailureText.ThrownAt` (`:283`, `:680-687`), .NET frames only (`FailureText.cs:215-251`,
so a V8 stack yields none); failing steps depth-first (`:257-264`, `:454-470`), printed with the step's own location
(`:710-715`, no fallback) and message (`:716-717`); JSONL `:891-905`; "Attempt" in the digest means a CI attempt
(`:15-29`, `:498-516`). Query: the scanner reads attempt and locations (`ReportScanner.cs:830-835`, `:869-871`),
not `failureCause` or `exampleDisplayName`; `scenarios` prints `attempt N` above 1 (`Overview.cs:196-198`), its JSON
(`:201-219`) has attempt but no locations (roadmap 7.1, unshipped); `failures` prints locations
(`Narrative.cs:85-86`, `:107-114`, JSON `:171-182`); `history` rows (`History.cs:420-427`). `ScenarioStableId.Compute`
(`:33-53`) uses neither. History: roster source `"file:line"` (`HistoryRunBuilder.cs:55-59`, `:153-156`), attempt
character (`:83`; `HistoryFormat.cs:93-94`, `:117-131`), `passedOnRetry` Flaky (`HistoryAnalyzer.cs:379-395`),
process-level attempt fold adds (`HistoryLedgerWriter.cs:406-449`, `:447`); sources reach nothing else and a roster
line is written only for a new id set (`:229-230`, `:243-244`). CTRF: `retries = Attempt - 1`
(`CtrfReportGenerator.cs:178`), `flaky` (`:197`), `filePath`/`line` (`:193-194`), `retry N` tags stripped
(`:157-161`); `kronikol ctrf` mirrors it (`CtrfCommand.cs:131-155`).

**Per-test versus CI attempts:** `Scenario.Attempt` is the runner's retry in one invocation; `CiMetadata.RunAttempt`
(`GITHUB_RUN_ATTEMPT`, `CiMetadata.cs:52-54`) is the provider's re-run and names the history run
(`gh:<run>:<attempt>`; Azure DevOps always `ado:<build>:1`, `HistoryRunBuilder.cs:161-178`); a same-run re-run in a
new process is `EVIDENCE_SURVIVES_A_RERUN_PLAN.md`'s case (`KeepRuns`, the ledger overlay, `:52`, `:57`,
`:723-747`).

**Ingest today** (`FeatureSynthesizer.cs`): one accumulator per id (`:47-60`), records sorted by time (`:62`);
name and feature last wins (`:73-74`); `start`: first start time wins (`:79`), other fields `??=`, tags appended
(`:87-88`, not de-duplicated by `ScenarioTags.Classify`, `ScenarioTags.cs:36-73`); steps appended, re-sorted and
nested (`BuildStepTree` `:340-376`); step errors to `Comments` (`:386`, `:401`, `:414-422`); attachments' `step`
index resolves against the combined list (`:288-294`); `end`: `acc.Status = record.Status ?? acc.Status`,
`acc.Error = record.Error ?? acc.Error` (`:104-111`); duration the last end's, else first start to last end
(`:160-163`); one `HasEnd` flag; scenario `Id = testId`, no attempt, location or `EndedAt` (`:151-177`); feature no
`SourceFile` (`:210-217`). Interactions of both attempts feed one diagram (`IngestPipeline.cs:729-740`, `:779-782`);
`--attribute-by-window` builds one window from first start to last end (`IngestAttribution.cs:62-71`);
`--phase-from-steps` carries the phase across attempts (`:445-458`). No test covers retries.

**Cucumber lane:** `TestCaseStarted.Attempt` 0-based (`CucumberMessages.cs:405-406`); grouped per test case
(`CucumberFeatureSynthesizer.cs:161-175`), winner `attempts[^1]` (`:202-203`), winner-only steps, markers and
attachments (`:274`, `:278`, `:334-345`, header doc `:99-101`), `retry N` labels (`:490-492`), `Attempt =
winner.Attempt + 1` (`:523-524`), id from the winner's `kronikol-test-id` attachment (`:601-615`) else
`$"{pickle.Id}#{winner.Attempt}"` (`:636`); `WillBeRetried` parsed, never read (`CucumberMessages.cs:421-422`);
`SourceFile` from the node's or pickle's URI (`:525-527`, `NormalisePath` `:824-833`), `SourceLine` from the node
(`:528`); step locations parsed (`CucumberGherkinStep.Location`, `:233-234`) and never mapped (step built at
`:417-432`); step failures to `Comments` (`:389`, `:424`); Cucumber-built scenarios replace tests-file ones
(`CucumberFeatureMerger.cs:12-23`, `:54-66`).

**No Node reporter contract in the repo** carries retries or locations; `NODE_PORT_PLAN.md`'s ingest appendix
(`:1205-1259`) and `JAVA_PORT_PLAN.md` App. C (`:616-682`) have neither.

## C. Item 4 (S4): `--separate-setup` and the phase boundary

**`IngestCommand.cs`:** locals `:19-55` (`phaseFromSteps` `:52`), the switch `:57-247`, options from
`IngestPipeline.DefaultOptions()` `:296` (`IngestPipeline.cs:282-289`), mapping `:297-319`, the request `:334-356`,
`PrintUsage` `:509-579`, unknown option exit 2 (`:238-243`). Flags and where they land: `--tests` `:62-65`→`:337`
(checked `:257-267`); `-o` `:66-69`→`:297`; `-t` `:70-73`→`:318-319`; `--render` `:74-93` (`TryParseRender`
`:497-507`, `local` refused `:81-92`)→`:302`; `--feature` `:94-97`→`:339`; `--collapse`/`--no-collapse`
`:98-103`→`:303`; `--collapse-threshold` `:104-110`→`:304`; `--max-arrows` `:111-118`→`:305`;
`--browser-render-workers` `:119-126`→`:306-307`; `--note-format` `:127-137`→`:308-309`; `--payloads`
`:138-146`→`:310-311`; `--headers` `:147-155`→`:312-313`; `--no-component-diagram` `:156-158`→`:314`;
`--diagnostics-section` `:159-161`→`:315`; `--no-redact`/`--redact-header` `:162-168`→`:327-330` (restored `:403`);
`--allow-empty` `:169-171`→`:340`; `--chronological` `:172-174`→`:341`; `--merge-duplicates` `:175-177`→`:342`;
`--cucumber-messages` `:178-181`→`:344` (checked `:269-280`); `--include-hooks` `:182-184`→`:345`;
`--fold-unknown` `:185-188`→`:343`; `--strict` `:189-191`→`:346`; `--no-capitalise` `:192-194`→`:316-317`;
`--run-window` `:195-197`→`:349`; `--run-start`/`--run-end` `:198-208`→`:350-351`; `--attribute-by-window [id]`
`:209-215`→`:347-348`; `--phase-from-steps` `:216-218`→`:352`; `--attachments-base` `:219-222`→`:353`;
`--clean-attachments` `:223-225`→`:354`; `--diagnostic` `:226-234`→`:355`. The tool never sets `SeparateSetup`,
`HighlightSetup` or `SetupHighlightColor`. The false usage line is `:564-565`.

**The pattern for a flag** (`ed81a13`, `--headers`): a nullable local with a comment, a case validating its value
inline, a conditional mapping, two usage lines with `(default: …)`, one `IngestCommandTests` fact (bad value, missing
value, each value and the flag left out, the usage text), the wiki row. A plain switch follows
`--diagnostics-section`. Flag facts: `IngestCommandTests.cs:62-235` (`--headers` `:137-186`), usage errors
`:237-253`, the no-activity-warning fact `:484-515`; no CLI fact exists for `--phase-from-steps`,
`--attribute-by-window`, `--run-window`, `--merge-duplicates`, `--no-capitalise`, `--attachments-base`,
`--clean-attachments`, `--collapse-threshold`, `--max-arrows`, `--no-component-diagram`, `--allow-empty` or `--title`.
`VerbTable` (`src/Kronikol/Query/VerbTable.cs:31-35`) is the query CLI's table only; its rule (roadmap 4.1: the
table, `KnownFlags`, the verb's list, both `commands.md` copies) and guards (`DescribeTests.cs:54-116`,
`SkillDriftTests.cs:141-221`) do not cover `ingest`.

**The options** (`ReportConfigurationOptions.cs:82-89`; the "setup/teardown steps" wording is inaccurate), copied
at `ReportGenerator.cs:287-289` to `DiagramsFetcherOptions`, passed at `DefaultDiagramsFetcher.cs:264-266` to
`PlantUmlCreator` (`:78-80`, `:122-124`). **The algorithm** (`PlantUmlCreator.cs`): collapse first (`:181`);
`hasActionStart` `:193`; the first marker only (`:194`); `hasSetupTraces` needs a real call before it (`:195-197`);
`partitionLine` `:200`; no marker means everything is action (`:201`); `lastSetupTraceIndex` `:203-216`; the marker
closes the partition (`:222-228`); a narration marker (Step, Assertion, Row) at or before the last setup call renders
inside it, anything else closes it (`:243-268`); a real call opens it (`:273-274`); variants by phase (`:277`, not on
the wire); builder `OpenPartition`/`ClosePartition` `:2068-2081`, split handling `:2128-2167`. Pins:
`PlantUmlCreatorTests.cs:1808-1830`, `:1848-1859` (no marker, no partition), `:2096-2178`.

**In-process markers:** only `TrackingDiagramOverride.cs:108` and `InteractionRecord.cs:330` set `IsActionStart`.
`StartAction` (`:91-112`) sets the phase context and logs the marker; `StartSetup` (`:114-117`) logs nothing. The
implicit rule (`TestTrackingMessageHandler.cs:341-368`) runs on the test's own client (`:227-228`) with a
`CurrentStepTypeFetcher` (LightBDD, ReqNRoll, BDDfy options), marks the Given section on Given/And/But, injects before
the first later request (`:228` before the log at `:237`), once per handler instance (`_actionStartInjected`, `:30`,
never reset). Pins: `TestTrackingMessageHandlerTests.cs:1270-1459` (`:1398`: no marker when the first step is a
When). `StepCollector`'s phase changes (`:103-118`) and the BDD adapters only set the context; so do database and
message trackers. `WhenTriggersAction` (`StepTrackingOptions.cs:15`) is documented as `StartAction()` and logs
nothing (INFERRED consequence: no partition). Four And/But vocabularies: `PhaseConfiguration.cs:47-54` (Setup),
`StepCollector.cs:106-117` (unchanged), `IngestAttribution.cs:419-426` (inherit), the handler (Given section).

**Ingest:** `PhaseFromSteps` doc `IngestPipeline.cs:187-194`, applied `:612-619`; `PhaseForStep`
`IngestAttribution.cs:417-427` (`keywordType ?? keyword`); `BuildStepWindows` `:440-462` (a step without
`durationMs` is a zero-length window); `ApplyPhaseFromSteps` `:471-505` (skips markers and set phases, `:487`);
`AddDiagramMarkers` `:719-741` (step and assertion markers only; `drawnByCapture` `:721-727`, skip `:731`);
`ByTimestamp` stable `:747-753`. `Phase` readers after ingest: `ReportGenerator.cs:4803`, `:5063`, `:5447`, schemas
`:6451`, `:6631`; `MergeableReportReader.cs:518`; `ReportScanner.cs:736`, `QueryCommand.GroupBy.cs:15`, `:103`,
`Payloads.cs:313`. None draws. Tests: `IngestAttributionTests.cs:177-297` (the pipeline one, `:254-297`, asserts the
phase on the data file only); `IngestRoundTripTests.cs:124-125` is the only ingested `partition` assertion, made by
the fixture's raw marker (`:147`); `IngestPipelineTests.cs:612-631` carries a marker without `SeparateSetup`.

**The synthesized boundary** (INFERRED design, adopted in the plan §3.4): a `kind: marker, markerKind: Phase`
record like `StepMarker` (`InteractionRecord.cs:449-465`), inserted at the front of the records so it sorts first at
equal timestamps; skip tests whose capture has a raw Phase marker (resolve the kind as the replay does,
`:166`, `:381-385`) or draws its own step bars. The plumbing needs the options and the step windows in
`AddDiagramMarkers` (the windows are local to `Attribute` today, `:614`). A synthesized marker is excluded from the
data files (`ReportGenerator.cs:4274`, `:4685`), step attribution and annotations (`:4333`, `:4341`), `Failures.md`
(`FailuresDigestGenerator.cs:239`), the component diagram (`ComponentDiagramGenerator.cs:62`), participants
(`PlantUmlCreator.cs:1282`), the merger and the phase pass (`IngestAttribution.cs:487`); it nests as a zero-length
root in call-tree order (`IngestPipeline.cs:886-896`). A marker breaks collapse runs (`SequenceCollapser.cs:15-16`,
`:46-55`), which is why the boundary is gated on `SeparateSetup`.

**Cucumber:** step records join `testRecords` (`IngestPipeline.cs:336-337`); `StepMarkerRecord`
(`CucumberFeatureSynthesizer.cs:552-567`) sets `Keyword` (`:414`, `:709`), the step's duration and level 0, no
`KeywordType`, no `Background`; the pickle type (`CucumberMessages.cs:312-317`) sits on
`CucumberStepWindow.KeywordType` (`:441`, doc `:41-46`), read by nothing (the windows only nest assertions,
`CucumberFeatureMerger.cs:146-160`).

**The LightBDD comparison** (`INGEST_FEED_PLAN.harness/real-suite-run.md`, `real-suite-compare-cli-defaults.txt`):
3 of 6 diagrams differ with the tool's defaults; the length gaps 366, 270 and 318 are each 30 + 48 × the scenario's
requests (7, 5, 6): the partition's 30 characters plus a 48-character `[[#iflow-<guid> ]]` wrapper per request
(`PlantUmlCreator.cs:368-377`), since the library defaults `InternalFlowTracking` on and ingest turns it off;
`compare-reports.py` prints only four differing lines (`:60-68`), which hid it (INFERRED from the arithmetic).

## D. Item 5 (S5): spans into internal flow

**The store** (`src/Kronikol/InternalFlow/InternalFlowSpanStore.cs`): a static class (`:14`), a
`ConcurrentQueue<Activity>` (`:16`) and a reference-identity seen-set (`:17-18`); `Add` (`:20-24`), `GetSpans`
snapshot (`:26`), `Complete` (`:32-37`, used by `TrackingProxy.cs:116-172` and `GrpcTrackingInterceptor.cs:269`),
`Clear` two separate clears (`:39-43`, INFERRED race). The OTel package's facade `TestTrackingSpanStore`
(`:10-16`). Readers: the collector (`InternalFlowSpanCollector.cs:37`, from `ReportGenerator.cs:325-327`) and
diagnostics only (`ReportDiagnostics.cs:67`, `InternalFlowHtmlGenerator.cs:209`, `DiagnosticReportGenerator.cs:145`,
`ActivitySourceDiscovery.cs:14`). Tests treat it as process-global (`SpanStoreClearCollection.cs:7-14`,
`ProcessGlobalStoreTests.cs:79-106`).

**The seven members read:** `TraceId` (builder `:106`, `:123`, `:125`; collector `:64-69`), `SpanId` (builder
`:269`, `:280`; renderer `:162`), `ParentSpanId` (builder `:125`; renderer `:168`), `DisplayName ?? OperationName`
(renderer `:116`, `:145`, `:233`, `:357`, `:437`), `Source.Name` (renderer `:107` swimlane, `:144`, `:221`, `:356`;
collector `:63`, `:80-82`), `StartTimeUtc` (builder `:69-71`, `:213-215`; renderer `:175-177`, `:209-211`, `:229`;
`ComponentFlowSegmentBuilder.cs:77-81`), `Duration` (renderer `:117-120`, `:210`, `:230-234`; builder `:71`). Never
kind, status, tags, events, links or baggage. Flame data `[srcIdx, name, leftPct, widthPct, depth, durationMs]`
(`InternalFlowRenderer.cs:236-237`, `:493-500`); activity diagrams batched at 100 spans and 3 parts (`:36-95`), the
flame chart stops at 2000 (`:203`, `:244-249`); duplicate span ids collapse in the tree (`:160-162`).

**Entry in-process:** `InternalFlowActivityListener` listens to everything but `System.Net.Http` (`:33-36`, `:48`),
samples `AllData` (`:49-50`), adds on stop (`:51`), ignores its `additionalActivitySources` (`:44-55`), started once
by the handler (`TestTrackingMessageHandler.cs:130-134`); DI `AddActivityListenerForInternalFlowTracking`
(`InternalFlowServiceCollectionExtensions.cs:31-43`); `Kronikol.Extensions.OpenTelemetry`'s
`AddTestTrackingExporter` (`OpenTelemetryTrackingExtensions.cs:22-25`, its doc at `:18` names a missing method).

**Filtering at report time** (`InternalFlowSpanCollector.cs:33-83`): `Full` keeps all (`:48`); `Manual` keeps named
sources, all when empty (`:73-83`); `AutoInstrumentation`, the default, keeps every span of a trace holding one of
`WellKnownAutoInstrumentationSources` (`:54-71`), a mutable public set of 14 .NET names (`:11-27`).

**Options** (`ReportConfigurationOptions.cs`): `InternalFlowTracking` `:167` true; `InternalFlowDisplay` `:171`,
`InternalFlowTrigger` `:175`, `InternalFlowContentStrategy` `:200`, `InternalFlowFragmentsFolderName` `:204` not
implemented; `InternalFlowDiagramStyle` `:178` (SequenceDiagram falls back, `InternalFlowHtmlGenerator.cs:146-151`);
`InternalFlowSpanGranularity` `:181`; `InternalFlowActivitySources` `:184` (read only under `Manual`);
`InternalFlowNoDataBehavior` `:187` (HideLink); `InternalFlowHasDataBehavior` `:190`; `InternalFlowShowFlameChart`
`:193`; `InternalFlowFlameChartPosition` `:196`; `InternalFlowPopupCustomStyleSheet` `:209`;
`WholeTestFlowVisualization` `:212`; `ActivitySourceDiscovery` `:409`; `DiagnosticMode` `:412`; toggle default
`InternalFlowTab` (`ReportToggleDefaults.cs:108`).

**Attribution** (`InternalFlowSegmentBuilder.cs`, 3.35.1): calls need a timestamp (`:112`, `:52`), `Type ==
Request` and not a marker or user action (`:176-184`, `:310`), `RequestResponseId` (the key `iflow-{id}`, `:218`; the
response ends the window, `:162-167`, `:193-194`), `TestId`, `ActivityTraceId` (`:115`, `:150`, `:203`, ordinal
against lowercase hex, `:92`, `:106`, `:118`), `ActivitySpanId` when the trace is shared (`:134`, subtree
`:261-290`, the anchor need not be in the store `:267-269`), not `TrackingIgnore` (`ReportGenerator.cs:321-323`);
Kronikol's own `TraceId` is never read. Rules: claims (`:112-120`), traced calls (`:132-142`, `:203-204`), untraced
calls (`:145-158`, `:205`, unclaimed traces `:123`), the window (`:186-198`, `:207-216`, 50 ms before the request,
else the next record, else 5 s), ambiguity (`:222-247`); the popup's left-out note (`InternalFlowHtmlGenerator.cs:175`,
`:195-203`, `:262-276`). Whole-test flow (`:38-84`): traces only that test claims, at any time, plus its calls' spans;
key `iflow-test-{TestId}` (`:73`); built when `WholeTestFlowVisualization != None` (`ReportGenerator.cs:331-334`).
The handler records `Activity.Current`'s ids, or mints a trace and injects `traceparent` (`:154-170`).

**Report side:** one `iflow-segments` element (`InternalFlowHtmlGenerator.cs:17`, `:57-72`, `:78-88`), built at
`ReportGenerator.cs:397-431` (`LinkedSegments` `:94-105`, 3.35.3; `StoreFlowsOnce` `:222-256`, 3.35.2); the mergeable
file carries rendered segments and whole-test fragments, not spans (`:4603-4637`, `:4724-4731`;
`WholeTestFlowFragment.cs:3-9`; merge keeps the first per key, `MergeableReportMerger.cs:42-43`, `:377-390`;
`MergeableReportRenderer.cs:29`, `:66-68`, `:76`, `:117-127`); the standard JSON keeps only the calls' activity ids
(`:4808-4809`). `ReportDiagnostics.Analyse` (`:61-72`) prints the span count or the "0 spans" warning only when
tracking is on, to the console only (`ReportGenerator.cs:603-608`), pinned for ingest by
`IngestCommandTests.cs:485-515`.

**OTLP package:** `OtlpTraceReader` decodes one document (`Read` `:37-55`, `ReadJson` `:59-108`, `ReadProtobuf`
`:302-315`), no line API, a parse failure returns nothing (`:64-71`), a root without `resourceSpans` nothing
(`:75-77`); ids hex or base64 (`NormaliseId` `:272-292`), nanos number or string (`:247-255`), kind int or name
(`:143-164`); protobuf skips events, links, tracestate. `OtlpSpan` (`:49-119`) has every field needed, `ScopeName`
standing for `Source.Name`. `SpanToInteractionMapper` (`:35-72`, families `:49-66`, `Build` `:343-367`,
`ResolveTestId` `:370-375`) reads no `kronikol.*` attribute; `MappedSpan` stamps `capturedBy: span`
(`:51-68`, `:92`, `:104`). `OtlpTap` never touches the span store; a test id reaches a span only as its trace id
(`OtlpTapOptions.cs:101-118`). `OtlpSpanMapper` (export) emits flat traces, no parent ids (`:101-103`),
`OtlpJsonEncoder` a fixed scope (`:20-21`, `:54-57`, `:72-106`): not reusable as a span-stream writer. The tool
references the package (`Kronikol.Tool.csproj:44`); core cannot (the package references core). Nothing converts OTLP
spans to internal-flow spans; `TcpTap.EmitActivities` (`:598-613`) and `SpanAttributionTests.cs:301-314` show why an
`Activity` cannot carry a given span id. Fixtures: `tests/Kronikol.Tests.Otlp/OtlpGoldens.cs` (Mongo `:22-54`, the
Node ioredis shape `:61-88`, HTTP `:91-135`, BigQuery `:138-163`, protobuf `:170-262`); reader facts
`OtlpTraceReaderTests.cs:9-91`.

**Public API typed on `Activity`** (why S5 adds a neutral record rather than changing types): `InternalFlowSegment`
(`InternalFlowSegment.cs:10-16`, `SpansLeftOut` `:22`), `BuildSegments` and `BuildWholeTestSegments`
(`InternalFlowSegmentBuilder.cs:24-27`, `:38-40`), `CollectSpans` (`:33`), `RelationshipFlowData`
(`ComponentFlowSegmentBuilder.cs:13-15`); internally `SpanNode(Activity)` and `BuildSpanTree` (renderer `:158`,
`:510-513`). The generator entry points that would take supplied spans: `CreateStandardReportsWithDiagramsInEnvironment`
(`ReportGenerator.cs:171`) and its core (`:196`), internal and visible to the tool (`Kronikol.csproj:18`); swap at
`:319-335`.

**What turning internal flow on changes:** request labels wrapped in `[[#iflow-…]]` with a 350-character cap
(`PlantUmlCreator.cs:368-377`; `PlantUmlStatementLimits.cs:136` via `DefaultDiagramsFetcher.cs:271`); popup, flame and
toggle scripts (`ReportGenerator.cs:1369-1375`); whole-test flow blocks (`:331-334`); NodeJs, Server and Local
rendering forced to inline SVG (`:270-277`).

**Global state around `IngestPipeline.Run`** (already not parallel-safe): it clears and replays the logger
(`:346-347`, `:804`), resets `DefaultDiagramsFetcher` (`:401`, `:413`), flips `StepText.CapitaliseEnabled`
(`:305-314`); the tool swaps `RequestResponseLogger.Redaction` (`IngestCommand.cs:327-330`, `:403`); tests run every
ingest serially in the `DiagramsFetcher` collection. Loading ingested spans into the static store would leak into
later reports and make a host's own spans "unclaimed", which untraced calls could take (`:123`, `:205`); passing them
explicitly adds no global.

**Inputs:** `CliInputs` (`IngestCommand.cs:590-635`) searches `*.ndjson` and `*.jsonl` recursively (`:15`,
`:604-608`); a spans file among the inputs would be read as interactions and fail on the required members
(`InteractionRecord.cs:37-70`; malformed, or `FormatException` under `--strict`, `NdjsonInteractionReader.cs:75-80`);
`--tests` and `--cucumber-messages` files are removed from the inputs (`:257-280`). The run window filters
interactions only (`IngestPipeline.cs:502-560`).

**Plans on the stream:** `PLATFORM_FOUNDATIONS_PLAN.md` G4 (`:115`, the line has drifted to 285), F1 (`:162-168`),
F5 (`:236-237`), §4.2 (`:351-356`, per-process streams, merge by concatenation), §4.6 (`:380-385`, span-sourced
arrows), §8 Decision 5 (`:507-510`), §11 Decision 5 (`:553-558`), §12.1 L9 (`:637`, in-memory records);
`GO_PLATFORM_PLAN.md` §3.5 (`:299-302`, "an OTLP file"), C1 `seq` (`:447`), C3 `attributionSource` (`:449`), C5
(`:451`), C7 (`:453`); `MOBILE_PLAN.md:51`, `:104-107`, `:131`, `:222`, `:236` (a separate backend process cannot hand
over its inside view; `TestIdAttribute` and baggage proposed); `NODE_PORT_PLAN.md` §3.16 (`:485-502`), §3.8
(`:225-227`); `SPAN_ATTRIBUTION_PLAN.md` §5 (`:77-79`) and §6 (`:83-84`, database trackers record no trace id);
`FLOW_NESTING_PLAN.harness/s2_ingest_causes.py:140-149` (two in five requests there carry no timestamp, and an
untimed call gets no segment).

## E. Executing from here

- **The .NET SDK** is not in the container image. `curl -sSL -o dotnet-install.sh https://dot.net/v1/dotnet-install.sh
  && bash dotnet-install.sh --channel 10.0 --install-dir /root/.dotnet` worked through the proxy; then
  `export PATH=/root/.dotnet:$PATH`. `dotnet build tests/Kronikol.Tests/Kronikol.Tests.csproj` took one minute
  (the test project targets net10.0 only).
- **Node 22 and npm** are installed and npm's registry is reachable; `otel-jest/run.sh` installs and runs the spike.
- **The wiki** is `https://github.com/lemonlion/Kronikol.wiki.git`; a clone beside the repository, at
  `../Kronikol.wiki`, is what `CLAUDE.md` and `tools/wiki-links/` expect.
- **The probes** are the pattern for a red test before a fix: copy one back, change it to assert, watch it fail.
- **Open GitHub issues touching this plan:** #111 (its `--headers` half shipped in 3.36.0; the rest is F4's), #76
  (the ingest lane's step locations are S3), #77 (7.3's `TestCaseId`, which the freeze at 14.5 waits for).
