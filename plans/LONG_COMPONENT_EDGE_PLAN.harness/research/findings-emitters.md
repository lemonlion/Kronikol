# Agent "Sweep emitters and render-error handling" result (2026-10-09)
Abbrev: CDG=src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs, CDR=.../ComponentDiagramReportGenerator.cs,
CDO=.../ComponentDiagramOptions.cs, RG=src/Kronikol/Reports/ReportGenerator.cs, MRR=src/Kronikol/Reports/Merge/MergeableReportRenderer.cs,
BRS=src/Kronikol/Reports/plantuml-browser-render-script.js, WH=src/Kronikol/Reports/plantuml-worker-host.js,
PUC=src/Kronikol/PlantUml/PlantUmlCreator.cs, PSL=src/Kronikol/PlantUml/PlantUmlStatementLimits.cs, NJR=src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs

HEADLINE: nothing anywhere notices a component diagram turning into the RangeError picture. Under BrowserJs it counts as a
successful render, is CACHED, and nothing is shown in its place; __kronikolRender.errors stays 0.

## 1. Callers / default edge
- Run report: RG:405-415 ExtractRelationships then GeneratePlantUml(rels, ComponentDiagramOptions ?? new(), useC4: UsesC4(rendering)). RG:4810 extract only (mergeable JSON).
- ComponentDiagram.html: CDR:46-48 (queued RG:530-537), same args.
- Merge: MRR:43-45 always useC4:false, does not copy DependencyColors; kronikol merge passes new ReportConfigurationOptions() (Kronikol.Tool/MergeCommand.cs:113).
- No caller passes stats.
- Defaults: no formatter (CDO:32), Title "Component Diagram" (:17), no theme (:26), ArrowColorMode.DependencyType (:61); GetColor always returns hex (DependencyPalette.cs:70-77).
- DEFAULT EDGE under BrowserJs: `a -[#E74C3C]-> b : "Proto: m1, m2… - N calls across M tests"` (CDG:241, 294). Server/Local use C4 `Rel(a, b, "…", $tags="#…")` (CDG:287; UsesC4 at CDR:73). Plain `-->` only with ArrowColorMode.Performance.
- Where drawn: run report panel (TestRunReport only; Specifications none, RG:495): BrowserJs page draws (RG:2010) in workers or main-thread fallback; NodeJs/Local/Server DrawEmbedded at write time (RG:468; CDR:94-139). ComponentDiagram.html: BrowserJs #comp-diagram same script (CDR:196-215), else GetImageSource (CDR:141-181). kronikol merge: panel only, page-drawn (MRR:66-112), no ComponentDiagram.html. kronikol ingest: same as run (IngestPipeline.cs:337-344; IngestCommand.cs:27,38,379,391). Labs page: not drawn.

## 2. Public options
- CDO: FileName :11, EmbedInTestRunReport :14, Title :17, PlantUmlTheme :26, ParticipantFilter :29, RelationshipLabelFormatter :32, ArrowColorMode :61, DependencyColors :64; five members do nothing.
- ReportConfigurationOptions: ComponentDiagramOptions :12, PlantUmlRendering :139, InlineSvgRendering :142, BrowserRenderWorkers :153, GenerateComponentDiagram :221, DependencyColors :509, ComponentDiagramVisible (ReportToggleDefaults.cs:66).
- Workarounds today: RelationshipLabelFormatter (still wrapped+capped CDG:218-221,253-258); ParticipantFilter removes edge; adapter Verbosity = Summarised or ExcludedOperations (SqlTrackingOptionsBase.cs:19,22); BrowserRenderWorkers = 0 (main thread, larger stack); non-BrowserJs renderer.

## 3. Method values (edge lists each distinct log.Method, CDG:74, 389). Grow with schema/traffic at default Detailed:
- SQL family (ClickHouse, SqlClient, Npgsql, MySql, Oracle, Sqlite): `SELECT FROM t`, `INSERT INTO t`, `UPDATE t`, `EXEC p`, `OPTIMIZE t` (Sql/UnifiedSqlClassifier.cs:227-246; SqlDiagnosticTracker.cs:68-71, 215-218); ClickHouse.Driver bulk insert `INSERT INTO {table}` (TrackingClickHouseClient.cs:232). Raw = first keyword; Summarised = bare verb.
- Dapper per table (DapperOperationClassifier.cs:46-54); Spanner per table (SpannerOperationClassifier.cs:94-108); MongoDB `Op (×N) (stages) ← coll` (MongoDbOperationClassifier.cs:75-98), Raw adds filter=; Kafka per topic, Raw adds partition+offset = one entry per message (KafkaOperationClassifier.cs:12-24); Service Bus `Send (×N) → q` (ServiceBusOperationClassifier.cs:75-100); Bigtable `MutateRows (×N) → t`, Raw adds row key; Elasticsearch per index; gRPC per RPC method; ingest/OTLP whatever capture holds (InteractionRecord.cs:40,596; SpanToInteractionMapper.cs:106-133,321,331).
- Short fixed sets: HTTP verbs, EF Core (SqlOperationClassifier.cs:29-36), Redis (RedisTracker.cs:55-57), Cosmos/Blob/S3/DynamoDB/BigQuery, MediatR (TrackingProxy.cs:95).

## 4. Other statements with no measured cap
- CDG: `!theme` :153 written even under BrowserJs; title :158 wrapped 100/line, total uncapped; participant + C4 declarations :187-204, 372-377 wrapped 80/line, total uncapped (only width measured); aliases :392 unbounded, repeated in every edge, label budget can reach zero (PSL:158-159).
- ComponentDiagramDiffer.cs:136, 157-173 (public, no caller in src): names neither wrapped nor capped.
- Sequence participants PUC:1332-1414 wrapped 80, total uncapped; built in prefix (:1194-1208) bypassing statement guard (:2049-2053).
- Sequence messages/loops/bars/notes: capped with measured values (PSL:60-136; PUC:303-385, 1166-1170).
- Activity diagrams InternalFlowRenderer.cs:110, 116-120: wrapped 100, no total cap, no guard, never measured.
- Error placeholder DefaultDiagramsFetcher.cs:77-81: exception message in a note body, uncapped.

## 5. Error-picture handling
- Worker WH:232-267: appended <svg> or written text posted as `done`, no content check; only throw/timeout = `error`.
- Page on done BRS:219-223: counts render, caches anything containing <svg (:131-139) -> error picture CACHED.
- describeEngineFailure BRS:1046-1083 (called :1121): recognises only "too large", openiconic/emoji load failures, "Syntax Error?". Over-long-statement check BRS:1018-1041 flags only messages > 2000 and block labels > 1471. No RangeError mention in either file -> picture stays, __kronikolRender.errors 0.
- Only RangeError check: internal-flow-popup-script.js:149-158 (activity popups, 100 ms timer).
- Main thread = page-wide mode switch (BRS:318-339), never per-render.
- Kronikol placeholder only DrawEmbedded catch: component-diagram-failure div + RenderFailure diagnostic (CDR:133-138; RG:2009), exceptions only.
- NodeJs: any <svg> = success (plantuml-render.js:485-527; NJR:134-136, 186-188) -> error picture passes.

## 6. Per-render fallback: none re-renders a failed diagram differently. Nearest: re-split at half height after "too large" (BRS:1153-1173); requeue on worker crash (BRS:190-204); NodeJs refused sequence diagram -> placeholder note (DefaultDiagramsFetcher.cs:386-430); scenario-by-scenario retry building diagrams (DefaultDiagramsFetcher.cs:150-172). None covers component diagram / error picture.
