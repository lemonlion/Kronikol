# Call operation and origin plan (#192)

**Status:** written 2026-10-26 at 5.11.0 (`44bcc62a`) by kronikol-bf, at the owner's ask to analyse #192 critically and
plan a fix for the underlying problem; checked again on 2026-10-27 at 5.12.1 (`79e8d644`), at the owner's ask to make
sure it is complete and accurate (section 10). **Green-lit on 2026-10-28**, with every question in section 9 answered,
and implemented from there in the same worktree: R1 first. What changed from the text on the way is in the log
(section 10) and in place below.

**Issue:** [#192](https://github.com/lemonlion/Kronikol/issues/192), "`--group-by` has no caller or origin dimension and
doesn't template paths or GraphQL operations, so an edge list can't be read through the engine". Siblings filed the same
day from the same work, each planned by another session at the same time (section 8):
[#184](https://github.com/lemonlion/Kronikol/issues/184) (the caller on `interactions --json` rows),
[#190](https://github.com/lemonlion/Kronikol/issues/190) (which party is the subject, which are doubles: kronikol-67,
`plans/PARTY_IDENTITY_PLAN.md`) and [#191](https://github.com/lemonlion/Kronikol/issues/191) (a per-run `service.json`:
kronikol-ed, `plans/SERVICE_CARD_PLAN.md`). This plan owns three rules the other two cite rather than restate: the
**operation** a call is (4.1), the **GraphQL operation** inside it (4.2) and the **origin** of a call (4.3).

**Harness:** `plans/CALL_OPERATION_AND_ORIGIN_PLAN.harness/`, described in its `README.md`.

Line numbers are to `79e8d644` (5.12.1); of the files cited, only `ReportGenerator.cs` changed since `44bcc62a`. The
issue cites `0905e07` (5.9.0); the two files it cites did not change since. Wiki lines are on `origin/master`
`c9c7b5a`.

---

## 1. Summary

The issue is right that `--group-by` cannot build an edge list: it has no caller, its `path` is the raw URI path, and
every GraphQL call is one bucket. It is wrong about what is missing, and two parts of what it asks for would make
things worse: folding long opaque segments, and taking "the caller is the test client" as origin's rule, where it can
only be the last resort it is in #190's resolver, after a declaration and what the run observed.

- **Kronikol already knows which operation a call is.** The history fingerprint has had a templater since 3.9.0,
  versioned, measured on real paths and fed by the consumer's own rules (`HistoryShapeTemplates`); the warm-up marks
  and `diff --body`'s call pairing use it too. `--group-by` does not. The issue's fold rule would be one more answer
  beside six that already disagree (F1), and its "long opaque segments" are a rule history's design proposed,
  measured and dropped because it took real event, queue and table names (F2).
- **The GraphQL blind spot is not the engine's alone.** History reads a changed GraphQL operation as unchanged
  behaviour, and warm-up judges every operation of an endpoint against one baseline (F3): a defect of shipped features.
- **"The caller is the test client" is configuration, not evidence.** Caller names are fixed tracker options, so a
  test that seeds through the subject's own clients writes the subject's name, and a subject that runs in the test's
  process writes the test's (F5). Measured on BreakfastProvider, the subject's work outside any request is real
  dependency work, and phase cannot tell it from the test's.

**The underlying problem** is that "which operation is this call" has six answers in six places, the engine's own
`path` the crudest of them, and none is shared; and that origin was asked of the caller's name, which records
configuration.

**The fix:**

1. **One operation key** (4.1): the history templater, with the GraphQL operation added (4.2) and the statement head
   it already uses for SQL and document stores, three defects of that head fixed (3.6), behind history, warm-up, the
   component diagram, call pairing and the engine. History's shape version moves once.
2. **The run's templating rules written into the report** (4.5), so the engine templates a report as its run did.
3. **`caller` and `route` dimensions, `caller` on JSON rows and a `--phase` filter** (4.4), as asked; `path` keeps its
   meaning.
4. **`origin` as the caller's role** (4.3), read from #190's parties, with the one case nothing recorded can settle
   (seeding through the subject's clients) documented, and the cause of a call offered as its own dimension (Q4).
5. **History that survives a change of name** (4.8), asked for on 2026-10-27: while runs made under an earlier rule
   are in the window, each run also fingerprints its own calls under that rule (a bridge), so a change of Kronikol's
   rule, of the consumer's or of what a tracker records no longer restarts every scenario's behaviour history, this
   plan's own version move first.

Three releases: a minor for history continuity and the defects (R1), a minor for the engine (R2), a minor for origin
once #190's R1 has shipped its parties (R3). Eighteen questions in section 9.

---

## 2. What the issue says, checked

| # | The issue says | Checked | Verdict |
|---|---|---|---|
| C1 | `--group-by` takes `service method status path step phase category kind capturedBy` | READ: `QueryCommand.GroupBy.cs` L14-15 | True |
| C2 | Without the caller, the test client's calls and the service's own calls land in one bucket | READ: `DimensionValue`, L103-115, never reads `CallerName`; the index has it (`ReportIndex.cs` L304, read at `ReportScanner.cs` L775) | True, and the cheapest part of the fix: the field is already in the index |
| C3 | The `path` dimension is the URI's `AbsolutePath`, so ids, cache keys and GUIDs give every call its own bucket | READ: `UriPath`, L117-123 | True. But Kronikol already has a templater, versioned, measured and used by three features (F1) |
| C4 | Fold numeric ids, GUIDs, hex hashes **and long opaque segments** to `{id}`, keeping literals | READ: `InteractionShape.cs` L28-30, L58-80; `plans/CROSS_RUN_HISTORY_PLAN.md` L441-448; RUN: section 3.6 | Half wrong. The opaque-segment rule was proposed in a draft of history's 3.9.0 design, measured on BreakfastProvider, and dropped: it took 10.2% of path segments and every one was a literal (event, queue, topic and table names). The existing rule writes `/orders/{n}`, not `/orders/{id}` |
| C5 | Use the same rule as `service.json` (#191), so the two agree | READ: #191 says "using the same rule as #192" | Circular: each issue points at the other. This plan owns the rule (section 8) |
| C6 | Every GraphQL call is a POST to one endpoint, so `path` makes one bucket of them | READ: `GraphQlOperationDetector.cs` L16-73 is read only by `PlantUml/` | True, and the same blind spot is in history's fingerprint and the warm-up shapes (F3) |
| C7 | Take the operation name with the existing detector | READ, RUN: section 3.4 | The right source, with gaps of its own (F4) |
| C8 | `origin` is `test` / `subject` / `other` from #190's party kinds; with nothing declared, "the caller is the test client" | READ: section 3.5 | Wrong for two common setups: a test that seeds through the subject's own clients, and a subject that runs in the test's process (F5) |
| C9 | `caller` and `origin` on `interactions --json` rows | READ: `QueryCommand.Payloads.cs` L127-143 | True: neither is there (#184 asks for `caller`) |
| C10 | Optional: a `--phase` filter; phase is only a grouping today | READ: `Matches`, `QueryCommand.Payloads.cs` L171-205; `VerbTable.cs` L201-210 | True |
| C11 | 3, 188 and 4,385 buckets for `service,method,path` on three real reports; 660 GraphQL calls in one bucket, `operationName` null in 654 of 660 | Searched for on this machine (the whole user profile and `C:/Code`) and not found; no session here filed the issues | Section 3.6 measures BreakfastProvider's reports and stand-ins made here for the Cucumber-JVM and Jest producers |

---

## 3. Findings

### 3.1 F1: six places decide which operation a call is, six ways

| Site | Input | Rule | Query string | GraphQL operations |
|---|---|---|---|---|
| History fingerprint, `InteractionShape.Target` (`InteractionShape.cs` L246) | `Uri.PathAndQuery`, escaped | the templater with the consumer's rules, plus the statement head | keys kept | one shape |
| Warm-up shapes, `WarmUpCalls.TargetOf` (`WarmUpCalls.cs` L245) | `PathOf(Uri.ToString())`, the data file's form | the same templater and statement head | dropped | one shape |
| Component diagram endpoints, `ComponentFlowSegmentBuilder.cs` L237-238 | `Uri.AbsolutePath` | GUIDs only, as `{guid}` (L710-715) | dropped | one endpoint |
| Background calls table, `ReportGenerator.cs` L5147-5153 | `Uri.AbsolutePath` | none | dropped | one row |
| Engine, `--group-by path` (`QueryCommand.GroupBy.cs` L117-123) | `Uri.AbsolutePath` | none | dropped | one bucket |
| Call pairing for `diff --body` and `compare`, `CallPairing.cs` L79, L124, L157 (#115) | `Uri.PathAndQuery`, escaped | the templater without the consumer's rules | keys kept | one shape: the n-th `POST /graphql` pairs with the n-th, whatever the operation |

The sequence diagrams read the same question for GraphQL only: their arrow label reads the operation from the body
(`PlantUmlCreator`, through `GraphQlOperationDetector`), and so does the key that collapses a repeated arrow
(`SequenceCollapser.KeyFor`, L126-137), so two different anonymous queries in a row draw as one arrow repeated
(3.9). `compare` also diffs each call's raw `ServiceName` and summary (`QueryCommand.Search.cs` L263-266), which is a
listing, not a key, and stays as it is.

So "what did this run call" has a different answer in the history verdict, the warm-up marks, the component diagram's
endpoint list and the engine, and #191's card would be one more. The history templater is the one that was designed
and measured for the job (`plans/CROSS_RUN_HISTORY_PLAN.md` §2.4; its id rule was checked over both repositories'
sources, 628 distinct paths, `InteractionShape.cs` L58-64; F2), and it is the one to keep.

The component diagram's endpoint list (`RelationshipStats.EndpointBreakdown`, a public record) is read by nothing under `src/` at `79e8d644`, yet the wiki's Component-Diagrams page (L398-409) describes
clickable per-endpoint rows under each relationship. Either the page describes a feature that is gone, a docs defect,
or something draws the record without naming it; R1 settles which with a paint-level check before changing the rule.

### 3.2 F2: the fold the issue proposes was proposed before, measured and dropped

`InteractionShape.Template` (L102-126) already folds what the issue lists, with one difference of spelling and one of
substance:

- **Numbers are `{n}`, not `{id}`.** `/orders/123` is `/orders/{n}` (`InteractionShapeTests.cs` L21), GUIDs, hex runs of
  16 or more, ULIDs and 8 to 15 hex characters with a digit and a letter are `{id}`, ISO dates `{ts}`, bytes captured
  as text `{bin}`. The acceptance's `/orders/{id}` becomes `/orders/{n}`: renaming the placeholder would move every
  history fingerprint for no reader's gain (Q2).
- **Long opaque segments are kept, on purpose.** The rule was in a draft of the 3.9.0 design and was dropped after a
  run over BreakfastProvider: it took 10.2% of all path segments, and every one was a literal, among them
  `breakfast-equipment-alerts`, `BatchCompletionRecords` and `PancakeBatchCompletedEvent`
  (`plans/CROSS_RUN_HISTORY_PLAN.md` L441-448: "Any 'long opaque token' heuristic is rejected on this evidence").
  Event, queue, topic and table names are exactly what an edge list must keep apart.
  `There_is_no_base64_rule_because_it_templated_real_words` (`InteractionShapeTests.cs` L109-116) pins the base64
  case only (`SGVsbG8gV29ybGQ` and `customers/download` kept); no fact pins a longer opaque segment, and R1 adds one
  (6.2).
- **What no built-in rule can know** (an application's cache-key format) is what `HistoryShapeTemplates` is for
  (`ReportConfigurationOptions.cs` L791-810), and the data file not carrying them (4.5) is why the engine cannot apply
  them today.

Section 3.6 measures the templater against the issue's rule on real reports.

### 3.3 F3: GraphQL operations are invisible to history and warm-up, not only to the engine

Every operation posted to one endpoint is one shape: `POST /graphql` for history and warm-up, one endpoint for the
component diagram. A scenario that stopped sending `mutation CreateOrder` and now sends `mutation CancelOrder` keeps
its fingerprint, and the history verdict reads "unchanged" where the behaviour changed, the false negative the
fingerprint exists to prevent. The warm-up rule judges a cheap query and an expensive mutation against one baseline.
Only the sequence diagrams name the operation. This is a defect of the shipped history feature, independent of the
issue, and R1 fixes it (4.1, one version move).

### 3.4 F4: the operation detector misses bodies real clients send

**Measured on BreakfastProvider**: each in-process lane posts 8 GraphQL bodies to `/graphql` (and makes three GETs
under it: `/graphql/` and two schema files). All 8 are anonymous shorthand queries, `{ orderSummaries { ... } }`, `{ recipeReports { ... } }`,
`{ batchCompletions { ... } }` and four more root fields, none with `operationName`. `TryExtractLabel` reads every one
as `query`: one distinct label for seven operations. The issue's own report had `operationName` null in 654 of 660
bodies. Taking the label as the operation, as the issue proposes, would leave BreakfastProvider's GraphQL calls in one
bucket, which is why 4.2 names an anonymous operation by its root fields. The Jest stand-in (3.6) is the same shape:
its 4 GraphQL POSTs are an anonymous `{ __typename }` twice and `query Charge` twice, and none of its bodies has an
`operationName` key.

**Probed** (`probes/graphql-detector.cs`, `results/graphql-detector.txt`): the detector finds the first `"query"` key
anywhere in the body, and returns nothing when that one is nested, even with a top-level `query` after it.

| Body | Label today | Should be |
|---|---|---|
| `{"query":"query GetUser { ... }"}` | `query GetUser` | the same |
| Apollo's key order, `operationName`, `variables`, `query` | `query GetUser` | the same |
| a variable named `query` before the query (search APIs take one), with or without `operationName` | none | `query Search` |
| an input object holding a key named `query`, before the query | none | `mutation Save` |
| a persisted query sent by hash, `operationName` and `extensions.persistedQuery`, no `query` | none | `GetUser` |
| Apollo's registering retry of that query, its text and `extensions.persistedQuery` | `query GetUser` | `GetUser`, as the hash-only request |
| a document that opens with a fragment definition (Relay, code generators) or a `#` comment | none | `query Q` |
| a name on the line after its keyword (`query\nGetUser`) | `query` | `query GetUser` |
| two operations, `operationName` naming the second, a mutation | `query B` | `mutation B` |
| a POST to a search API whose `query` member holds a search document | `query` | none |
| a batch, a JSON array of two operations | none | none: pinned by design (Q6) |

The first rows are `probes/graphql-detector.cs`'s; the five after the persisted query are the route prototype's probes
P1 to P4 and P12 (`harness/route/`, `results/route/corpus.txt`), each also read by a stricter reader, below. Each
"none" is a POST the diagrams draw without its operation today, and that 4.2's key would leave in the endpoint's one
bucket. The detector's GET path does not exist at all: a GET's operation is in its query string, and nothing reads it.
A GET rule must also not invent one: `GET /search?query=mutation%20testing` is a search, not a mutation (P10).

The detector finds its fields with regular expressions over the JSON text, which is why a key named `query` anywhere,
an escaped newline or a fragment first misleads it. The prototype's strict reader takes the top level with a JSON
reader, parses the `query` string as a GraphQL document and picks the operation by `operationName`: it fixes every
probe above, agrees with the detector on all 230 bodies of `GraphQlOperationDetectorTests.cs`, and costs the same
(`results/route/corpus.txt`).

### 3.5 F5: who made a call is configuration; why it was made is recorded, and neither is the issue's fallback

**Who.** A call's `callerName` is a fixed option of the tracker that captured it, never an observation:

- default `"Caller"` on every .NET options class (`TrackingDefaults.cs` L12), `"Test"` in Kronikol4J
  (`TrackingDefaults.java` L10), `"Browser"` for Playwright, required on every NDJSON record with no default
  (`InteractionRecord.cs` L49);
- the HTTP handler takes it once (`TestTrackingMessageHandler.cs` L51), the subject's own clients get a handler built
  from the `TrackDependenciesForDiagrams` options (`TrackingHttpMessageHandlerBuilderFilter.cs` L22-23), and EF Core
  copies the HTTP options' caller (`SqlTrackingInterceptorOptionsExtensions.cs` L24);
- only the message trackers pick it per call, by direction (`KafkaTracker.cs` L385, `MessageTracker.cs` L318: a
  delivery is broker → consumer), and the OTLP tap per span (`SpanToInteractionMapper.cs` L298).

**Why.** Each in-process capture also records how it got its scenario, `attributionSource` (`AttributionSource.cs`):
`RequestHeader` when a request the test sent carried the identity in, `Scope` for the propagation middleware's scope or
a delivered message, `TestContext` for the test's own context **and any work that inherited it**, `DocumentFlow` and
`Detached` for background work (`TestInfoResolver.cs` L87-120), and `DocumentOwner` and `Expired`, which are marked when
the report is written (`AttributionSource.cs`). The query engine
also computes, per scenario, which call each call ran inside (`CallNesting.cs` L32-76, FLOW_NESTING R4).

Measured on BreakfastProvider's CI run 40290999167 (Kronikol 5.4.1) by the two sibling plans, whose scripts are cited
so the numbers can be re-run:

| Lane | Requests | The test's calls | The subject's, inside a request it served | The subject's, outside any | Other parties' |
|---|---|---|---|---|---|
| xunit-in-memory | 1,409 | 451 | 755 | 86 | 117 |
| reqnroll-in-memory | 1,353 | 443 | 731 | 68 | 111 |
| reqnroll-in-docker | 1,261 | 442 | 665 | 151 | 3 |
| xunit-external-sut | 378 | 378 | not captured | not captured | 0 |

(kronikol-ed, `C:/Code/Kronikol-191/plans/SERVICE_CARD_PLAN.harness/probes/card-prototype.cs`, `CallNesting` ported,
targets from `InteractionShape` v6.)

- **The calls outside any request are real dependency work**: the Cosmos SDK's metadata reads, the outbox poller
  (`SELECT VALUE root` 54 times on the docker lane), a Replace, gRPC `SendOrderConfirmation`, BigQuery and ClickHouse
  inserts. Calling them scaffolding would drop real edges.
- **Phase cannot tell them from the test's work.** The subject's calls inside a request carry no phase (731 of 731 on
  Reqnroll), and its background calls carry the phase of the step that started the host: Setup 38, Action 21,
  Unknown 9. On xunit-in-memory 445 of the test's 451 calls have no phase at all, and 372 of 378 on the external-SUT
  lane.
- **Attribution follows the cause.** On reqnroll-in-memory the edge `Breakfast Provider > CosmosDB` (257 calls) is
  `RequestHeader` 198, `TestContext` 56 and `DocumentOwner` 3; every one of the test's own calls is `TestContext` (443
  there, 451 on xunit-in-memory)
  (kronikol-67, `C:/Code/Kronikol-190/plans/PARTY_IDENTITY_PLAN.harness/bp-reports/parties.py`).
  Read by itself, attribution gives the table's split exactly on xunit-in-memory (`RequestHeader` 755; `TestContext`,
  `Scope` and `DocumentOwner` 86) and on reqnroll-in-memory (731; 68). Where a record has no attribution (the ingest,
  the taps, Kronikol4J), the nesting is the only signal, and Q4's rule says which wins where both exist
  (`harness/results/measure/`, item 9; the docker lanes were measured one each, xunit by this plan and Reqnroll by
  the prototype).
- **Counts move more than edges across runs.** Over 1,005 pairs of BreakfastProvider runs of one commit, some
  scenario's call count changed in 37.6% of pairs, the run's set of templated operations in 27 of the 933 pairs that
  carry call lists, and its set of edges in 0.4% (kronikol-ed, `SERVICE_CARD_PLAN.md` 2.4,
  `results/ledger-stability-breakfastprovider.txt` in its harness, which measures no cause for the count noise). A
  bucket's presence compares across runs; its count is that run's.
- **Phase depends on the adapter.** The same suite on four more lanes (3.6): NUnit and TUnit record no phase on the
  test's calls (Setup 5 and Action 1 of its 451), LightBDD and BDDfy record most of them (LightBDD Setup 234, Action
  199 and 18 without; BDDfy Setup 234, Action 217), and no lane records one on the subject's calls inside a request
  (755 a lane, all `Unknown`; `SERVICE_CARD_PLAN.harness/results/card-prototype-stand-ins.txt`). The Jest ingest records none, though `--separate-setup` draws its `beforeEach` call in the Setup box. A
  rule that leaned on phase would answer differently for one suite by adapter.
- **Some producers record no cause at all.** The ingest, the taps and Kronikol4J never write `attributionSource`
  (3.8), and on Kronikol4J's reports, as `FLOW_NESTING_PLAN.md` measured them (F16), the nesting finds nothing
  either, since Kronikol4J logs a call after the calls it made. There `cause` (Q4) can only say `unknown`. The ingest
  is better placed: on the Jest stand-in the nesting puts the service's 4 calls inside the test's 4, with no
  attribution and no phase, so `cause` would read `request` there from the nesting alone. On the four extra
  BreakfastProvider lanes the same prototype finds 755 calls inside a request on every lane and 56 to 90 outside,
  as often as the framework starts the host (kronikol-ed, `card-prototype.cs`).
- **Brokers are callers too.** On xunit-in-memory deliveries are recorded as `Kafka Broker > Breakfast Provider` (42),
  `Azure Event Hub > Breakfast Provider` (39) and `Google Cloud Pub/Sub > Breakfast Provider` (36), and Azure Event Hub is
  never a callee on the in-memory lane, so "a name that only calls" is not the test client.

What that means for the issue's rule, "with nothing declared, the caller is the test client":

1. **A test that seeds through the subject's own clients** (`factory.Services` and its DbContext or repository) writes
   those calls under the subject's name, and no recorded signal separates them from the subject's background work:
   both are `TestContext`, both are top level, and phase is the host's. Origin reads them as `subject`. This is a
   limit to document (section 7), not a rule to write. On BreakfastProvider the calls of that shape are the
   background work above, as the prototype classified them.
2. **A subject that runs in the test's process** (a .NET test calling a service class whose clients are tracked with
   the test's options; an ingest whose spans name the test as every call's caller) records its dependency calls under
   the test's caller name, `Test → X`. No causal signal exists there at all, since no request is served. Only a
   declaration that the party is the subject (#190) can say so, and then nothing can split the test's own calls from
   the subject's. The wiki's Jest recipe avoids it by naming outbound calls by the service (`callerName: SERVICE`,
   Ingesting-From-Jest L224), as the Jest stand-in shows (`Payments GraphQL → PSP`, `→ Risk`); whether the issue's
   Jest report followed it is not known here.
3. **The test client is not "the caller"**: brokers call the subject, and two producers name the test differently.
   Inferring it is #190's job; its rule, measured on the run above, finds `Caller` and `Breakfast Provider` on all four
   lanes, with one blind spot (a single caller calling one callee that makes no calls is an external-SUT lane and an
   in-process subject alike, and only a declaration settles it).

### 3.6 F6 and the measurements: buckets on real reports

`harness/measure/measure.cs` over eight of BreakfastProvider's CI lanes of run 40290999167 (5.4.1), a local 4.26.4
Reqnroll run of the same suite, the example API's Reqnroll run (built at `44bcc62a`, which reports itself as
`5.11.1+44bcc62a`), and a Jest ingest made here from the repo's
own Jest harness (Fastify 5 and Mercurius 16, `plans/INGEST_FIDELITY_PLAN.harness/otel-jest`, taken through the
wiki's recipe and `kronikol ingest`; the report is kept as `harness/stand-ins/jest-ingest-TestRunReport.krkl`).
Buckets of (service, method, what each rule makes of the path), request records only:

| Report | Calls | Today's `path` | Templater, query keys kept | Templater, query dropped | `route` (4.1: + statement head with its fixes + GraphQL operation) | The issue's rule, ids only | The issue's rule with opaque segments |
|---|---|---|---|---|---|---|---|
| xunit-in-memory | 1,409 | 207 | 158 | 156 | 184 | 157 | 144 |
| xunit-in-docker | 1,297 | 195 | 146 | 144 | 181 | 145 | 134 |
| xunit-external-sut | 378 | 98 | 72 | 70 | 74 | 71 | 70 |
| reqnroll-in-memory | 1,353 | 206 | 158 | 156 | 184 | 157 | 144 |
| reqnroll, local, 4.26.4 | 1,290 | 180 | 153 | 151 | 167 | 152 | 139 |
| nunit-, lightbdd- and bddfy-in-memory | 1,409, 1,379, 1,409 | 207 | 158 | 156 | 184 | 157 | 144 |
| tunit-in-memory | 1,413 | 208 | 159 | 157 | 185 | 158 | 145 |
| Jest ingest, 5.11.0 | 8 | 5 | 5 | 5 | 6 | 5 | 5 |
| example API, `44bcc62a` | 23 | 6 | 6 | 6 | 6 | 6 | 6 |

The `route` column is the key as 4.1 states it, prototyped over the same files (`harness/route/`, results in
`harness/results/route/_summary.txt`, its "alt head route" column); without the two head fixes it is 179 on
xunit-in-memory and 168 on xunit-in-docker. The other columns are `measure.cs`'s.

- **The templater already does the issue's job.** It folds every segment the issue's id rule folds (on xunit-in-memory
  6 digit runs, 41 GUIDs and 5 hex runs out of 165 distinct segments, none left unfolded) and a few more inside
  segments (`Recipe-{id}`, `Oven-{id}`).
- **Every extra merge the opaque-segment rule makes is wrong.** On xunit-in-memory it folds 28 segments the id rule
  does not (317 requests); the templater changes part of 7 of them (`Recipe-{id}`) and keeps 21 (310 requests), and
  those 21 are all names: `breakfast_recipe_logs` (80), `breakfast-equipment-alerts` (39),
  `BatchCompletionRecords` (36), `SendOrderConfirmation` (36, a gRPC method), `PancakeBatchCompletedEvent` (35),
  `breakfast_batch_completions` (35), `customer-preferences` (7, a REST resource) and fourteen more event, topic,
  table and gRPC names. The variant that needs a digit folds `grpc.reflection.v1.ServerReflection`. This is the
  history plan's finding again, on 5.4.1.
- **What remains after templating** is the right grain: 14 of the 156 templated-path buckets hold more than one raw path,
  led by `CosmosDB Read /colls/orders/docs/{id}` (23 paths, 38 calls) and `PATCH /orders/{id}/status` (14 paths,
  27 calls). The query keys the history form keeps add two buckets (158 against 156), from calls that send different
  parameters to one path (four such groups, among them `GET /audit-logs?entityType` and `GET /audit-logs?entityId`),
  which is why `route` drops them.
- **The route** adds 28 buckets to the templated path on xunit-in-memory: 17 from statement heads, 5 more from the
  head fixes and 6 from GraphQL,
  whose 8 POSTs are 7 operations (`query { orderSummaries }` twice, then `batchCompletions`, `equipmentAlerts`,
  `ingredientShipments`, `ingredientUsage`, `popularRecipes` and `recipeReports`). The Jest ingest's 4 GraphQL POSTs
  are 2 (`query { __typename }`, `query Charge`). The caller adds no bucket on any of these reports: no bucket has two
  callers, so it is the acceptance's seeding fixture (6.1) that proves the dimension. The data file's string and
  `Uri.PathAndQuery` give the same templated path for every record of all eleven reports.
- **Three statement heads make poor keys**, in history and the warm-up marks today as much as in `route` (4.1 fixes
  them in the version move): every EF Core SaveChanges batch on SQL Server keys as its prologue, `SET
  IMPLICIT_TRANSACTIONS OFF;` (on the docker lane 53 batches of INSERT, UPDATE and DELETE in 2 buckets, 10 with the
  SET lines skipped); a document that carries its statement as a string (Cosmos DB's `{"query":"..."}`, 61 calls on
  the docker lane, and BigQuery's `/query`) keys as `{"query":"{v}"}`, since every string in a document is data, so
  the outbox poller's query and three filtered ones share a bucket (3 buckets, 8 with that string templated as a
  statement); and the data file's string carries control characters unescaped, which the `{bin}` rule matches only
  as escapes, so the key `InteractionShapeTests.cs` L50 pins as `{bin}` stays as it is. The warm-up marks, which
  already read that string, miss it today.
- **Non-HTTP calls** are 384 to 568 per in-process lane, and the templater changes nothing for them: BreakfastProvider's
  trackers already put the table, collection or topic in the path (`sql:///main/RecipeReports`,
  `kafka:///breakfast_recipe_logs`) and the operation in the method (`Insert`, `Produce`). Every path rule gives them
  the same 50 to 62 buckets; the statement head, which `route` adds, makes 58 to 69. It matters most for the document
  stores reached over HTTPS: CosmosDB's and BigQuery's 291 calls are 34 buckets by raw path, 12 templated and 22 with
  the head, because `Create /colls/orders` writes audit-log, recipe-log and order documents alike.
- **Cost**: the engine's `--group-by service,method,path --count` answers in 17 to 87 ms in process on these files,
  warm, decoding included (18 to 89 cold; 55 to 118 on the stand-ins), and in 247 to 333 ms as a CLI process, most of
  it start-up (the first call 1,565). The route over a whole report takes
  1.4 to 1.9 ms, and in the engine adds 0.2 to 3.7 ms to a 15 to 20 ms group-by, its body reads under 1 ms: 669 of
  the in-process lanes' 1,018 request bodies (450 statements and 219 POSTs, 210 of them JSON that is not GraphQL),
  over the one payload handle.
- **The Jest ingest** is small (8 calls: `Test` to `Payments GraphQL` 4, which calls a payment provider and a risk
  service 2 each), and every path rule gives it the same 5 buckets (the route 6, for its two GraphQL operations): its
  ids `c2` and `c3` are a letter and a digit, which no built-in rule folds (`abc123` and `v2` are kept on purpose,
  `InteractionShapeTests.cs` L28, L23), so `/charges/c2` and `/charges/c3` stay apart. Only a consumer rule could
  fold them, and an ingest cannot carry one (4.5, Q7).
- **Not produced: a JVM report with calls.** Kronikol4J's fragment format has no field for interactions (calls
  survive only as pre-rendered diagrams), so the BreakfastProvider Java twin's fragments hold none by design; a
  standalone run writes them only with `kronikol.report.dataFormats=json` in 0.1.24, and the twin's in-process mode
  starts its backends in containers (Cosmos DB, Kafka, SQL Server, MongoDB, Pub/Sub and Spanner, and three generic
  ones: BreakfastProvider's `java-twin` branch), which this shared machine does not run. Kronikol4J writes no
  `attributionSource` either (F5).

F6: the consumer's own rules are the part no corpus here can show. None of these runs sets `HistoryShapeTemplates`,
and the issue's 4,385-bucket report, "mostly cache keys and ids", is the case the option exists for, so R2's
acceptance proves it on a fixture whose cache-key paths only a consumer rule folds (6.1).

### 3.7 F7: `services` and `--group-by service` disagree when names differ only in case

`services` keys its rows with `StringComparer.OrdinalIgnoreCase` (`QueryCommand.Overview.cs` L353) and `--group-by`
with `StringComparer.Ordinal` (`QueryCommand.GroupBy.cs` L35). Probed on two calls, one to `Orders` and one to
`orders` (`probes/service-case.cs`, `results/service-case.txt`):

```text
$ query services
service                  calls errors     bytes      p50      max  statuses
Orders                       2      0      18 B    10 ms    10 ms  OK×2

$ query interactions --group-by service
service  calls errors   median      max bodies     bytes
Orders       1      0    10 ms    10 ms      1       7 B
orders       1      0    10 ms    10 ms      1      11 B
```

The wiki says a `--group-by service` bucket "carries the same figure as that service's row" (Querying-Reports
L871-872); here it does not. It happens when two trackers spell one service differently, and the naming options
(`PortsToServiceNames`, `ClientNamesToServiceNames`) are typed by hand. A `caller` dimension inherits the same
choice, so R1 makes it once: `--group-by` folds a service or caller value to the first spelling `services` would show
for it, before the bucket key is joined. The key itself stays ordinal (`QueryCommand.GroupBy.cs` L35-39 joins every
value), so `path` still splits `/Orders` from `/orders`. The views that pair or nest calls inside one report
(`CallNesting.cs` L67-69, `diff`'s tracking-loss counts, `QueryCommand.Search.cs` L384, and the request and response
pairing, `QueryCommand.Shared.cs` L59, L84) compare names as recorded and stay ordinal. Q14 asks whether you would
rather have the reverse.

### 3.8 F8: the engine never reads a call's attribution

The data file writes `attributionSource` on every call the in-process trackers capture (`ReportGenerator.cs`
L5868-5899; the schema's description at L7889), and the scanner skips it (`ReportScanner.cs` L769-807 reads the call's
other fields; nothing under `src/Kronikol/Query` names it). The ingest, the taps and Kronikol4J never set it
(`InteractionRecord.cs` L241-280; `ProxyTap.cs` L971-993). It matters only to Q4's `cause` dimension, so it is read in
the release that adds that dimension, if any.

*Since 5.14.0 the scanner reads it:* #190's R1 put it in the index (`InteractionEntry.AttributionSource`) for the
parties resolver, so R3 reads it from there and the scanner needs nothing for `cause`.

### 3.9 Found on the way

Fixed in this plan, each with a fact that is red first (section 6):

- **A retried run leaves the consumer's rules behind.** `HistoryFold.Attempts` builds the folded run field by field
  and sets `shapeVersion` but not `shapeRules` (`HistoryLedgerWriter.cs` L486-510, called at L154), so for a consumer
  who sets `HistoryShapeTemplates` every run that needed a retry reads as made under another rule and drops out of the
  behaviour comparison. R1, with 4.8's rule that writers carry every member through.
- **`kronikol merge --history` reads with default options.** It builds the merged run without the consumer's rules
  and reads a fixed window of 50 (`MergeCommand.cs` L209, L217-218), so a merged report's behaviour verdicts are made
  under no rules against runs recorded under them. R2, from the `shapeTemplates` block of its inputs (4.5).
- **`query history` after `history record` reads the ledger twice.** The read keeps the newest `--window` runs, the
  run's own line among them, and the analysis wants the window before it, so the oldest is always one past what was
  kept and `PriorRuns` reads the whole file again (`HistoryLedger.cs` L113-150, `QueryCommand.History.cs` L33): on
  BreakfastProvider's ledger 1,127 ms against 310 for the same 18 analyses, medians (`results/ledger/loadtime.txt`).
  R1 reads one run more than the window.
- **An SQL batch that opens with two SET statements is `Other`.** `UnifiedSqlClassifier`'s SET prefix rule is anchored
  and runs once (`src/Kronikol/Sql/UnifiedSqlClassifier.cs` L17-19, applied at L186), so EF Core's SQL Server
  SaveChanges batch (`SET IMPLICIT_TRANSACTIONS OFF; SET NOCOUNT ON; INSERT ...`) is classified `Other` with no table:
  53 calls on BreakfastProvider's docker lane. R1, with the version move, since it changes recorded methods (4.8).
- **Two different anonymous GraphQL queries in a row draw as one arrow.** `SequenceCollapser.KeyFor` keys a repeated
  call on the detector's label, `query` for every anonymous operation (L126-137). R1 keys it on 4.2's operation. Its
  parts were never run together, as this plan first said: they are joined by U+001F written as a raw character inside
  the quotes (L130), which an editor shows as `""`. `The_collapse_key_keeps_its_parts_apart` pins it.
- **Three statement heads make poor keys** (3.6), and **the detector misreads seven kinds of body** (3.4). R1.

For other plans:

- **The largest break in BreakfastProvider's history was its own setting.** Its commit `cf47045` (2026-10-25) moved
  its trackers from Summarised to Detailed, and 946 scenario positions changed in one run, each read "behaviour
  changed" (`results/ledger/masschange.txt`). No rule version can bridge it, since the ledger records no tracker
  setting. Q16 asks whether a run that changes most scenarios at once should be read as one change.
- **The ingest's Setup box and its phase disagree.** `kronikol ingest --separate-setup` draws a test's `beforeEach`
  call in the Setup box and leaves its `phase` `Unknown`, so `--group-by phase` and R2's `--phase setup` miss a call
  the diagram shows as setup (the Jest stand-in, 3.6). Two readers of one fact; it belongs to the ingest's phase work
  (`plans/PHASE_FROM_STEPS_PLAN.md`, #130), not to this plan.
- **A merged Kronikol4J report carries no calls.** Kronikol4J's fragment format has no interactions, so a report
  `merge` builds from its fragments has nothing for `--group-by`, `services` or #191's card to count, only diagrams.
  For the divergence ledger, and for #191's backfill verb, which would read such a report as empty.

---

## 4. Design

### 4.1 The rule: one operation key

What a call *is*, comparable across calls, is one function in core, `CallOperation.Of`, internal to `Kronikol`. It
takes what both the run and the data file hold, as strings, so the run-time features and the engine reading the file
compute the same key:

```text
CallOperation.Of(method, uri, dependencyCategory, requestContent, rules, queryKeys)
```

1. **The path.** The URI as the data file writes it (`log.Uri.ToString()`, `ReportGenerator.cs` L5872), cut by
   `WarmUpCalls.PathOf` (L255): an absolute URI loses its scheme and authority, any URI its query and fragment. With
   `queryKeys` on, the query's keys follow, sorted, values dropped, as history writes them today (`InteractionShape.cs`
   L118-125).
2. **Templated** by `InteractionShape.Template`: the consumer's rules first, then `{bin}`, `{id}`, `{ts}` and `{n}`.
   No new built-in rule for segments (C4, F2): `/orders/123` is `/orders/{n}`. One rule widens: the data file's string
   carries control characters unescaped, so `{bin}` also matches them and U+FFFD written raw, where today it matches
   only their escapes (3.6). The consumer's rules then see the data file's string, as the warm-up marks' already do
   (`WarmUpCalls.cs` L247), where history's see the escaped path today (Q17).
3. **A statement-shaped call** (`DependencyCategories.IsStatementShaped`, `DependencyCategories.cs` L96-103) with
   content adds a space and `InteractionShape.StatementHead` (L260), as history and warm-up do today: the URI names
   the connection, the statement is the operation. Two fixes the measurements ask for (3.6): the head skips leading
   `SET` statements, EF Core's SQL Server prologue; and a document whose top-level `query` member is a string has that
   string templated as a statement in place of `{v}`, so Cosmos DB's and BigQuery's queries keep apart.
4. **A GraphQL request** adds a space and its operation (4.2): `/graphql query GetUser`.

Every site that answers "which call is this" calls it, and the copies go:

| Site | Today | After | `queryKeys` |
|---|---|---|---|
| History fingerprint, `InteractionShape.Target` (L246) | `Uri.PathAndQuery` (escaped), no GraphQL | `CallOperation.Of` | on |
| Warm-up shape, `WarmUpCalls.TargetOf` (L245) | `PathOf(Uri.ToString())`, no GraphQL | `CallOperation.Of` | off |
| Component diagram endpoints, `ComponentFlowSegmentBuilder.cs` L237-238 | `NormalizePathGuids(AbsolutePath)`, GUIDs only | `CallOperation.Of` | off |
| Call pairing for `diff --body` and `compare`, `CallPairing.cs` L79, L157 | `Template(PathAndQuery)`, no rules, no GraphQL | `CallOperation.Of`, with the report's recorded rules (R2) | on |
| Background calls table (Q8), `ReportGenerator.cs` L5147-5153 | `Uri.AbsolutePath` | `CallOperation.Of`, if Q8 says so | off |
| Engine, new `route` dimension (4.4) | none (`path` is the raw `AbsolutePath`) | `CallOperation.Of` over the index | off |
| `service.json` operations (#191) | none | cites this section | off |

`path` keeps its meaning (`/orders/123`, what the wiki and the skill's `--group-by service,path --sort bytes` recipe
describe); the templated form is the new `route` dimension, so no existing dimension's values change, `service` apart
(F7).

A run computes each call's key once, and history, the warm-up marks and the component diagram read it from there, so
a request body is parsed once per run, not once per feature; R1 measures the run-time cost and states it in the
changelog.

History's input moves from the escaped `PathAndQuery` to the data file's string, and GraphQL, the two head fixes and
the wider `{bin}` join its key. Each changes fingerprints, so `InteractionShape.Version` moves once for all of them (6
to 7, or 7 to 8 if #190's R0 ships first; numbers are claimed at push), and 4.8's bridge carries every scenario's
history across the move: no quiet run and no lost memory. Doing them in one move is the reason they ship together.

### 4.2 The GraphQL operation

From the request, by one reader shared with the diagrams' arrow label, the key that collapses a repeated arrow and the
note formatter (one reader per field, the 3.29.1 lesson). The reader takes the body's top level with a JSON reader,
not with regular expressions over its text, and parses the `query` string as a GraphQL document: the prototype's
strict reader (3.4).

1. **The operation the body names.** `operationName` picks one of the document's operations, else its only one, with
   its type: `query GetUser`, `mutation CreateOrder`. A document that opens with fragments or a comment, or puts the
   name on the line after its keyword, reads the same.
2. **An anonymous operation is named by its root fields**, sorted and de-duplicated, aliases and arguments dropped:
   `{ orderSummaries { orderId } }` is `query { orderSummaries }`, and `{ recipeReports { recipeType } }` another
   operation, where the detector's label for both is `query`. On BreakfastProvider every GraphQL POST is of this kind
   (F4), so without this rule the GraphQL operation splits nothing there. Sorted, because `{ a b }` and `{ b a }` ask
   for the same data; de-duplicated, because two aliases of one field ask for that field twice.
3. **A persisted query** (Apollo APQ): any body with `extensions.persistedQuery` is named by its `operationName`
   alone, `GetUser`, whether it carries the query text (the registering retry) or the hash only, so the two forms of
   one operation are one key. Today the hash-only form reads as no GraphQL at all (F4).
4. **A GET** with `query` or `operationName` in its query string: the same reading over the decoded parameters, and
   only when `query` parses as a GraphQL document, so a search API's `?query=` is not one (3.4).
5. Otherwise none, and the key is the path alone: a body whose `query` member is not a GraphQL document (a search
   API's), and a batched body (a JSON array), which the detector pins (`GraphQlOperationDetectorTests.cs` L98): a
   batch is several operations, and Q6 asks whether to mark it.

The fixes are made in the shared reader, so the diagrams' labels, the collapse key (3.9) and `GraphQlBodyFormatter`,
which formats a note's body as GraphQL only when the detector finds an operation (L51), get them too: a note may now
format as GraphQL a body it showed as JSON. Kronikol4J's port (`GraphQlOperationDetector.java`) takes the same fixes,
or its divergence ledger says it does not (section 7). Whether the arrow label also shows an anonymous operation's
root fields (`query { orderSummaries }` where it shows `query` today) is Q13.

### 4.3 Origin

`origin` is what the issue says it is: `test`, `subject` or `other`. It is **the role of the party that made the call**,
and the roles come from #190 (`PARTY_IDENTITY_PLAN.md`, kronikol-67): one resolver in core,
`src/Kronikol/Parties/PartyResolver`, taking a declaration first, then what the run observed, then inference (its
§5.1); the inference of the test client and the subject is its §5.4, and what this plan relies on is listed in its
§5.12.

| The caller's role (#190) | `origin` |
|---|---|
| the test client (`test`) | `test` |
| the subject (`underTest`) | `subject` |
| the subject and the test in one process (a Jest test calling handler code, a .NET test calling a service class), declared as the subject | `subject` |
| any other role, or a role nobody declared and #190 cannot infer | `other` |

Three things this plan measured or checked decide that shape, against the issue's fallback and against a causal rule
(F5):

1. **Caller names are configuration, not observation.** Every in-process tracker takes its caller from a fixed option,
   default `"Caller"` (`TrackingDefaults.cs` L12); EF Core copies the HTTP options' caller
   (`SqlTrackingInterceptorOptionsExtensions.cs` L24). So a client the subject registers carries the subject's name
   whoever uses it: a test that seeds through `factory.Services` writes calls under the subject's name.
2. **No recorded signal separates that seeding from the subject's own background work.** Both run outside any
   request, on the test's context (`attributionSource` `TestContext`), at the top level of the nesting. Phase does not
   separate them either: the subject's background work carries the phase of the step that started the host (F5). The
   plan records the limit and a recipe (section 7): seed through a client tracked under the test's caller name.
3. **Background work is real dependency work.** On seven of BreakfastProvider's lanes 56 to 151 of the subject's calls run
   outside any request it served, and they are the Cosmos SDK's metadata reads, the outbox poller, gRPC and BigQuery
   writes (kronikol-ed, measured). Reading "outside a request" as scaffolding would drop real edges; `subject` keeps
   them.

So on the runs measured causality would change no `origin`, and for the one misread (seeding through the subject's
clients) it has no answer either. It is a second axis, why a call was made, and Q4 asks whether the
engine should offer it as its own dimension, `cause` (`test`, `request`, `background`, `unknown`), from the record's
`attributionSource` first and the flow nesting (`CallNesting.cs`) where a record has none.

`origin` depends on #190's roles, so it ships with or after #190's R1, which brings the `parties` block and the
resolver (`PARTY_IDENTITY_PLAN.md` section 6; its R0 fixes names only). The block is written only when a run declares
something (its 5.12), so for a run that declares nothing `origin` reads the roles #190's resolver infers, as the card
does. With nothing declared, a subject that runs in the test's process still reads `test`: that is the limit of
inference, documented beside the seeding recipe (section 7), and only a declaration settles it.

### 4.4 What the engine gains

| | What | Where | Release |
|---|---|---|---|
| `caller` dimension | the record's `callerName`, already in the index | `QueryCommand.GroupBy.cs` `GroupByDimensions`, `DimensionValue` | R2 |
| `route` dimension | `CallOperation.Of(...)` with `queryKeys` off: `/orders/{n}`, `/graphql query { orderSummaries }`, `/colls/orders/docs/{id}`; cut in text output as other wide columns are (47 of xunit-in-memory's 160 distinct routes are over 100 characters) | the same, plus the request body for the two kinds of call that need it (below) | R2 |
| `--phase` filter | `setup`, `action`, `unknown`; on `interactions`, beside `--service`. `unknown` matches a call recorded without a phase, which the scanner stores as none (`ReportScanner.cs` L798) | `Matches`, `QueryCommand.Payloads.cs` L171; `QueryOptions`; `VerbTable` | R2 |
| `caller` on `interactions --json` rows | the field #184 asks for, added after `service` | `QueryCommand.Payloads.cs` L127-143 | R2 |
| `origin` dimension and row field | 4.3, from the `parties` block #190 writes, else its resolver | the scanner reads the block; `DimensionValue` | R3 |

The text rows of `interactions` do not change: the issue asks for the fields on the JSON rows, and a wider text row
costs every listing's budget.

**Body reads.** `route` needs a call's request body in two cases: a statement-shaped call (its statement head) and a
POST, which may be GraphQL whatever its endpoint is called. On BreakfastProvider every GraphQL call also had `graphql`
in its path, but a rule keyed on the name would miss an endpoint called `/api` that the diagrams label. A GET is read
from its URI, and every other call stays index-only. The index does not say what kind of body a POST carries
(`ReportIndex.cs`, `InteractionEntry`: a hash, a length and a slice), so every POST body is read, once, through
`BodyCache` over the one shared handle the perf tests count (`ReportIndex.PayloadOpens`), and only when `route` is
among the dimensions; `GroupedInteractions` is given the cache for it. Measured (3.6): 669 of an in-process lane's
1,018 request bodies, under 1 ms.

**A report without recorded rules** (section 4.5) is templated with the built-in rules only. A report written before
R2 cannot say whether its run had rules, so the answer says so once, in a note, decided by the report's
`kronikolVersion`: `route: built-in templating only; this report predates recorded HistoryShapeTemplates`. From R2 a
report carries the block exactly when its run had rules, so its absence means there were none, and nothing is said.

**Patterns read from a file.** The engine compiles a report's patterns under the run's limits (250 ms a match), and
drops a pattern for the rest of the command at its first timeout. A run keeps a timed-out pattern for the next text
(`HistoryShapeRules.Apply`, L80-94), which a run can afford; a query of a report cannot, since a pattern near the limit
over the issue's 4,385 paths would take 18 minutes.

### 4.5 The run's templating rules, in the data file

The consumer's `HistoryShapeTemplates` decide what the run's shapes are, and the data file does not carry them: the
ledger run line holds their hash (`HistoryLedgerWriter.cs` L596-598), not the patterns. A query of the file therefore
cannot template as the run did, and `route` would disagree with history, with the warm-up marks and with #191's card
for exactly the consumers who wrote rules, which are the ones whose paths no built-in rule covers (the issue's 4,385
buckets were "mostly cache keys and ids", the case the option exists for).

So the report writes them: a root key `shapeTemplates`, `[{ "pattern": "...", "placeholder": "..." }]`, named as the
public `HistoryShapeTemplate(Pattern, Placeholder)` is (`HistoryShapeRules.cs` L14) and the wiki's "(regex,
placeholder)", in the order given, **only when the list is not empty** (every existing report stays byte-identical).
It is written in each data format, JSON, YAML and XML (`ReportGenerator.cs` L5281-5282), with each format's schema,
and the public `GenerateTestRunReportData` (L5256), which takes no options, gains an optional parameter for it. The
scanner reads it into the index, and the engine builds `HistoryShapeRules` from it with the same compile and 250 ms
timeout rules (`HistoryShapeRules.cs`) and 4.4's drop at the first timeout, so a pattern that misbehaves on the
reader's machine is skipped and named, as it is in a run.

- `kronikol merge` writes the block when every input carries the same list, and none otherwise, with a diagnostic
  naming the inputs that differ, as `history record`'s fold blanks a run whose shards differ
  (`HistoryLedgerWriter.cs` L590-602). Its `--history` reading builds the merged run with those rules (3.9).
- `kronikol ingest` has no way to set the rules today (`IngestCommand.cs` L455-484 is a curated flag set), so an
  ingested report carries none. Q7 asks whether to add one.
- Kronikol4J writes no such block; a ledger entry says so (section 7).

### 4.6 What does not change

- `path`, and every other existing dimension, bucket for bucket, `service` apart, where R1 folds names that differ
  only in case (F7).
- `services`.
- History's verdicts and what they mean. The fingerprint input moves with the version (4.1), and 4.8's bridge
  replaces the rule gate's quiet run.
- The diagrams' arrow labels, apart from what the detector's fixes add (4.2) and Q13.
- The data file, apart from `shapeTemplates` (R2), #190's `parties` block (its R1) and the warm-up shapes, which R1's
  key changes for GraphQL and statement calls (`warmUp.shape`, `ReportGenerator.cs` L5945, and the same field on
  `interactions --json` rows, `QueryCommand.Payloads.cs` L151).

### 4.7 Options considered

| Option | Why not |
|---|---|
| A new templating rule, as the issue proposes, with long opaque segments as ids | Proposed in a draft of 3.9.0's design, measured and dropped, and measured wrong again here (C4, F2, 3.6); a sixth rule beside the five |
| Template `path` in place | Changes an existing output that the wiki, the skill's recipes and any script read; `route` is new and can be exact |
| A `graphql` dimension beside `route` | The acceptance asks `--group-by caller,origin,route` to split the operations, so the operation belongs in the route. Q5 asks whether a separate dimension is wanted as well |
| Write each call's route into the data file at run time | Bytes on every call of every report, a second implementation in Kronikol4J and the ingest, and older reports still need the query-time rule. The rules block is one small key, written only when there are rules |
| Origin from causality (`attributionSource`, then nesting) | Measured: it would change no origin on the runs measured, and cannot settle the one misread (4.3). Offered as its own dimension instead (Q4) |
| Origin from the issue's fallback alone, "the caller is the test client" | It is #190's last resort, after a declaration and what the run observed; taken alone it reads an in-process subject's dependency calls as the test's whatever is declared (F5) |
| FLEET_EVIDENCE_PLAN's S2 `graph` verb | The curated edge list, behind D23 and not this issue's ask; this plan gives it and #191 their pieces and does not pull it forward |
| Translate the stored call lines across a version move, in place of a bridge | Measured on BreakfastProvider's real 4 to 6 move: the best translation recovered 70.6% of what moved, and needed a re-cut of every head, a guess at each line's kind and a field parse (4.8). Kept only for a change whose new record has lost what the old line held |
| Keep the rule gate and its quiet run | The owner asked for history that survives a change (4.8). The quiet run takes one run's verdicts from every suite at every move, and costs an alternating scenario its memory |


### 4.8 History that survives a change of name

Asked by the owner on 2026-10-27, answering Q2: renaming is fine if it is much better going forward, "but surely
there's a way to avoid it now and in the future? ... Every time we do something that changes the name at the moment
it breaks all history". It does today, and by more than the version note says.

**What breaks now.** A fingerprint is compared only with one the same rule made (`SameRule`, `HistoryAnalyzer.cs`
L492): the built-in version and the hash of the consumer's `HistoryShapeTemplates`. The filter runs once, upstream of
everything that reads it (L493-498), so after a change of either, every earlier run drops out of the behaviour
comparison at once, not for one run: the previous shape, the unstable count, the alternating memory and the count
stretch all start again from the runs made under the new rule. Two comparisons have no rule gate at all: the
dependency pairs (`NewDependencies`, L141-142), so a renamed party reads as a new dependency whatever the version says,
and the calls the evidence names (`CallSetOf`, L286, read at L529-537 and L564), which diff stored lines. A change in
what a tracker records is the same break by another road: 4.20.0 moved the version for MongoDB's recorded request,
3.15.1's capture fix did not, and each scenario whose calls it changed read "behaviour changed" once (as
`EMULATOR_URL_SHAPES_PLAN.md` reads that release). Two plans written beside this one change recorded names or URLs
(#190's R0, `PARTY_IDENTITY_PLAN.md` L813-817; #125, `EMULATOR_URL_SHAPES_PLAN.md` section 7).

Scenario ids are kept another way, at a cost: whenever Kronikol changed a name a stable id is computed from, it kept
the old computation for good (a null suite gives the pre-3.1.0 id, `ScenarioStableId.cs`; a Reqnroll outline row
hashes its outline's title since #101; 5.9.0's `Titleize` is kept frozen, `StringCasing.cs` L58). A scenario the user
renames needs `kronikol history rename`, which `history record` suggests (`HistoryCommand.Maintenance.cs` L296-312).

**Measured on BreakfastProvider's ledger** (its `kronikol-history` branch on 2026-10-27: 1,635 runs of 18 suites from
2026-09-14; scripts in `harness/ledger/`, their output in `harness/results/ledger/`):

- Four version moves per suite have already happened (1 to 2 and 2 to 3 on 09-14, 3 to 4 on 09-22, 4 to 6 on 10-19
  with 4.32.0), and the last is still inside every window: today the analyzer ignores a median of 33 of each
  scenario's 50 points.
- A move's quiet run hides one run's behaviour verdicts (on the latest runs, 9 alternating and 1 changed among 3,714
  scenarios). The memory restart costs little here: a move replayed ten runs back changes 6 of 37,140 readings (3
  alternating and 1 count verdict lost, 2 alternating verdicts read as a false "changed").
- The quiet run also hides what the upgrade changed besides the rule. On the real 4 to 6 move, 255 of 3,714 positions
  changed, about 70% by the rule and 30% by capture changes shipped in the same upgrade (gRPC statuses 200 to 408 and
  404, CosmosDB reads newly attributed to their scenario).
- The largest break in the window was neither. BreakfastProvider's own commit `cf47045` (2026-10-25) moved its
  trackers from Summarised to Detailed: 946 positions changed in one run, each read "behaviour changed". No rule
  version can bridge that, since the ledger records no tracker setting (3.9, Q16).

**Why the stored lines are not translated.** Since 3.17.0 the ledger stores each run's distinct call lines (a `shapes`
line) and, for every scenario, which of them it made (`callSets`, `HistoryModel.cs` L236-246). On BreakfastProvider
every stored set fingerprint rebuilds from them exactly, 294,100 of 294,100. But a stored line is a poor input for a
translation. It joins `caller>service`, method, target and status with spaces (`HistoryModel.cs` L134), and every
`caller>service` pair there and 22 of its 44 methods hold a space (`Breakfast Provider`, `Produce → topic`). It does not
record the call's category. 40 of its 114 statement heads were cut at 120 characters, so a rule that shortens a head
reads characters the old line never stored. Replayed on the real 4 to 6 move, the best translation brought back 180
of the 255 changed positions (70.6%), and only with a re-cut of every head, a guess at each line's kind from its
service name and a parse keyed on the run's own dependency pairs.

**What makes it avoidable.** A run is recorded while its raw calls are in hand. If Kronikol keeps the rules it has
released (from version 6 on), the run can fingerprint its own calls under the rule an earlier run was made by, and the two compare
exactly. The consumer's earlier patterns are the one input a run does not hold, so the ledger records them.

The design has five parts:

1. **A bridge for every change of rule.** While the analysis window holds runs made under a rule other than the
   current one (a built-in version, a set of consumer patterns, or both), each run also writes its set fingerprints
   under each such rule, `bridge` on its run line, keyed by the rule. The analyzer compares an earlier run with the
   current run's bridge for that run's rule, and a pair of earlier runs through the bridge the later one wrote, so
   every comparison is made under one rule, with nothing parsed and nothing lost. Per rule it holds the set and
   ordered fingerprints of every position and the call sets, indexed into a `shapes` line of the rule's own (an
   existing kind, content-hashed, so runs that made the same calls share one), so the evidence names calls and
   `Reordered` compares across a change as within one rule. It needs no stored call lines from the earlier runs, so it
   covers runs from before 3.17.0 too. Kronikol keeps each released built-in rule from version 6 on as a function of
   the raw call (version 6 is today's `Target`, kept as it is). A window still holding runs under version 5 or
   earlier resets as today, and the version note says so. A change in what a tracker records ships the function from
   the new record to the old line: for #190's R0, a fallback name `{host}:{port}` reads back as `localhost:{port}` from
   the record's own URI.
2. **A translation where the new record has lost what the old line held.** #125's old lines came from the raw URL,
   base and all, which its classifiers read, and a Detailed or Summarised record keeps no raw URL, so no function of
   the new record gives the old line.
   Such a change declares a translation of the stored lines instead, to a form both sides reach
   (`EMULATOR_URL_SHAPES_PLAN.md` section 7 tabulates #125's). Given the measurements above, a translation rewrites
   patterns in the whole line and never looks for a field. No change in this plan needs one, so R1 builds none: the
   first change that does, #125's, builds the translation with it, and the fact that a line no translation reaches
   quiets only its own scenario.
3. **Line by line, never "changed".** A scenario holding a call that neither a bridge nor a translation reaches gets
   no behaviour verdict on its first run after the change, and its comparison restarts from runs under the new rule.
   It never reads "changed", and every other scenario keeps its memory. Every comparison across runs goes through
   parts 1 and 2: the set fingerprint, the alternating memory, the unstable count, the count stretch, the calls the
   evidence names and the dependency pairs (a bridge carries the pairs it makes when a change renames a party).
4. **The tool version on every run line.** A run line records the shape version and no tool version
   (`HistoryModel.cs` L224-228; the writer is named once, on the header). R1 writes it on each run line, so a change
   that shipped without a version move can still be translated afterwards (a bridge needs the raw calls, which are
   gone by then), and the evidence of a behaviour change says when the run before it was recorded by another version
   of Kronikol.
5. **Scenario ids: Kronikol writes its own aliases.** User renames keep `history rename` and its suggestions. When
   Kronikol itself changes what an id is computed from, the run computes each id the old way as well and writes the
   pairs that differ on its run line (`aliases`), which the analyzer reads as it reads `aliases.json`. A display
   change then no longer needs the old computation kept forever, only while runs made under it are in the history
   window. No change in this plan needs it, so Q15 asks whether to build it now.

**Where a bridge is made.** A bridge needs the run's raw calls and the rules of the window, both at once. A run that
reads its ledger as it is recorded has both, once R1 reads the ledger before it builds the run (today it builds
first, `HistoryRunContext.cs` L88, and reads after, L103). A run that only writes a fragment for `history record` to
fold later (the History action's flow) does not know the window, so it writes a bridge for every built-in version
Kronikol keeps, and `record`, which has the ledger, keeps the ones the window needs. A consumer's earlier patterns are
in the ledger alone, so in that flow an edit to them costs what it costs today, unless the fragment carries the run's
distinct untemplated targets for `record` to bridge from (Q18). It does from R1: the fragment's `rawCalls` member holds
each distinct call as the templater takes it in (the path before any rule, a statement's first line, a GraphQL
operation, and what rules 6 and 7 read otherwise), and each position's calls in order as indices into them. An older
reader passes over it, since it reads a fragment's members by name, and the ledger never holds it. `record`, and
`import`, `gate` and `query history`, which also read a fragment's run against a ledger, bridge the window's patterns
from it. On BreakfastProvider's lanes it adds 15% to 76% to a fragment as written (18 KB to 147 KB; 2 KB to 20 KB
gzipped), most of it the first lines of SQL statements (`harness/results/q18-raw-calls.txt`, its "written" columns).

**Counts are bridged too.** A bridge carried each position's fingerprints and calls but not its count, and the analyzer
compared counts across rules as they were. Rule 8 records calls rule 7's trackers left out (the SET batches above), so
a scenario making one of them read "the same calls made a different number of times" once the new count had held.
From R1 a bridge also holds each position's count wherever the rule counts otherwise, and the analyzer compares counts
under one rule, as it compares sets: the current run's when every counted run reaches it, else the earliest that every
one reaches.

**Shards and older writers.** A fold whose shards were made under different rules, one on an older package, blanks the
run's fingerprints today (`HistoryLedgerWriter.cs` L590-602); with bridges it folds under a rule every shard reached,
the older shard's, through the newer shard's bridge. `HistoryFold.Attempts` (L486-510) and `FoldGroup` (L610-635)
build each run field by field, and `prune` and `compact` write every run again, so a member a writer does not know is
dropped (`Attempts` already drops `shapeRules`, 3.9). From R1 every writer carries the members and line kinds it does
not know through as they were. `record` warns when a fragment was written by a newer Kronikol than the tool folding
it: the History action installs the tool its folder's `VERSION` file names, so a consumer who upgrades the package
and not the folder folds with an older tool, which drops the bridge, as an older `prune` or `compact` would. That
costs the change its continuity and nothing else. `history verify` checks a bridge's length against its roster, as it
does the call sets.

**A guard keeps it that way.** The call lines of a fixed corpus of captures, one or more per tracker, are pinned per
version. A change that moves any of them fails the build unless it moves the version and declares a bridge, a
translation or the lines neither reaches. This is what would have named the 4 to 6 upgrade's gRPC and CosmosDB changes
before they shipped.

**The consumer's patterns are recorded.** A consumer's pattern is written for the raw path, so the run that bridges
to the previous patterns needs them, and only the ledger has them. Every run line that has rules records their
patterns beside the hash it already carries (`HistoryLedgerWriter.cs` L596-598), a few hundred bytes for a typical
set, on every such line so that pruning a ledger never loses them.

**What it costs.** A bridge is eight hex characters per scenario for each other rule in the window, written only while
such runs remain, and one more templating pass over the run's calls per rule, which R1 measures and states in the
changelog. Reading costs nothing more: a bridge is a stored fingerprint. Only a translation reads call lists, which
add 97 ms to a warm read of BreakfastProvider's ledger (160 ms against 62, medians), and rebuilding every list in all 18
windows, memoised, 9 ms. The window is the report's `HistoryWindow` (default 50); a reader asking for a longer one
(`query history --window`, `VerbTable.cs` L110) finds no bridge for runs past the writer's and reads them as today.
Versions are claimed at push: this plan's move is 6 to 7, or 7 to 8 if #190's R0 ships first, and R1 then bridges R0's
move as well, since R0 moves the version.

A fact holds each change to its bridge: a corpus of runs recorded under the old rule and read by the new gives the
same verdicts as the same runs recorded under the new rule throughout, except where the new rule sees more, which is
pinned scenario by scenario (section 6).

**Older builds keep reading the ledger.** A ledger is often shared by projects that upgrade Kronikol at different
times, so everything new is an optional member of the existing run line, which every reader skips when it does not
know it. A new kind of line would not be: the loader counts a kind it does not know as a damaged line
(`HistoryLedger.cs` L393-395) and `history doctor` calls it unreadable (`HistoryJson.cs` L360-373 throws). A new
`historyFormatVersion` would be worse: an older build refuses the whole ledger (`HistoryLedger.cs` L359-361). R1 also
makes the loader and the line parser skip a kind they do not know, so that a later change can add one without that
cost to the builds released from then on.

---

## 5. Releases

| Release | Bump | What | Waits for |
|---|---|---|---|
| R1 | minor | 4.8: a bridge for every change of rule (each released built-in rule from 6 on kept as a function, the consumer's patterns recorded on run lines), the tool version on every run line, writers that carry what they do not know, the loader and the line parser skipping a kind they do not know, `record`'s warning, the guard, and a fold of shards under one rule. The key (4.1): `CallOperation.Of` (internal) behind history, warm-up and the component diagram's endpoint list, with the GraphQL operation, the two head fixes and the wider `{bin}`; `InteractionShape.Version` moves once, bridged. The shared GraphQL reader (4.2), so labels, the collapse key and the note formatter get it. F7. The defects of 3.9: `Attempts` keeping the rules, `query history` reading the ledger once, the SQL classifier's SET prologue (in the version move, bridged) and the collapse key | nothing; if #190's R0 ships first, R1 bridges its move too (section 8) |
| R2 | minor | the `caller` and `route` dimensions; `caller` on `interactions --json` rows; `--phase` on `interactions`; `shapeTemplates` written by the run in every data format (with the new optional parameter) and read by the engine, call pairing and `merge`, whose `--history` then reads with the shards' rules | R1, for the function |
| R3 | minor | the `origin` dimension and row field (and `cause`, if Q4 says so) | #190's R1: its `parties` block and resolver |

**Why R1 is a minor.** History that keeps its verdicts across a change of rule is a new capability, and the run line
gains new members (`bridge`, the recorded patterns, the tool version); the highest-ranking change decides the bump.
Without it the fixes would be a patch that resets every consumer's behaviour history, which is why they ship together.
The fixes change what users see, each named in its changelog entry:

- no quiet history run at the version move; from it on, a changed GraphQL operation, a changed query inside a Cosmos
  DB or BigQuery document and a changed EF Core SaveChanges statement read as behaviour changed, where they read
  unchanged;
- the calls the evidence names (`newCalls`, `goneCalls`) and `query history --calls` print lines in the new form: the
  data file's path, the GraphQL operation, heads without a SET prologue;
- the consumer's `HistoryShapeTemplates` see the data file's string in history, as in the warm-up marks (Q17), and a
  pattern holding a `%XX` escape gets a diagnostic;
- warm-up marks move on GraphQL and statement calls (`warmUp.shape` in the data files and on `interactions --json`
  rows), each operation judged against its own baseline;
- the component diagram's `EndpointBreakdown` folds ids, and the public outputs of `ComputeRelationshipStats` and
  `InteractionShape.Calls` change with the key;
- more request bodies get a GraphQL label in the diagrams, and their notes may format as GraphQL (F4); two different
  anonymous queries in a row no longer collapse into one arrow;
- EF Core's SQL Server batches are classified by their statement, not `Other`;
- `--group-by service` merges names that differ only in case, as `services` does;
- a retried run keeps its consumer rules in the ledger, and `query history` reads the ledger once.

**Why R2 and R3 are minors.** New dimensions, a new filter, new JSON fields, a new data-file key and a new optional
parameter of a public method are new public surface, whatever their defaults (CLAUDE.md).

R2 and R3 can be one minor if #190's R1 lands first (Q3).

---

## 6. Tests (TDD: each written red against 5.12.1, then made green)

Every fact goes through the public entry points (`QueryCommand.Run`, `IngestPipeline.Run`, the history analyzer and
writer), so the same files compile at the 5.12.1 tag and the red run is literal: a worktree at the tag, the new test
files copied in, nothing stubbed (as `DIFF_BODY_CALL_PAIRING_PLAN.md` was executed).

### 6.1 Fixtures

- **The issue's acceptance run**, written twice: as an NDJSON ingest and as an in-process .NET run on the example API.
  A test client seeding a database through a client of its own, the subject calling a declared double and a real
  party, `/orders/123` and `/orders/456`, two named GraphQL operations (one by `operationName`, one inline) and two
  anonymous ones with different root fields (BreakfastProvider's kind), a statement-shaped call, and a cache-key path
  only a consumer rule folds.
- **The same run seeding through the subject's own client**, which pins the documented limit (4.3, item 2).
- **A run with `HistoryShapeTemplates` set**, and the same without.
- **A ledger written by the 5.12.1 code**: version 6 runs with and without consumer rules, sharded, retried and
  partial, and a scenario that alternates, so every continuity fact starts from what consumers hold today.
- **Old reports**: a 4.x JSON report and a 5.12.1 `.krkl`, for `route` with the built-in rules and its note.
- **GraphQL bodies** as clients send them: `probes/graphql-detector.cs`'s and the route prototype's probes P1 to P19
  (`results/route/corpus.txt`).
- **Statements**: EF Core's SQL Server batches with one and two SET lines, and Cosmos DB and BigQuery query documents.

### 6.2 Facts

R1, history continuity (4.8):

- `A_ledger_recorded_under_rules_6_and_7_reads_the_same_verdicts_after_the_move`: the fixture ledger continued under the
  new version gives the verdicts the same runs give recorded under the new version throughout, except where the new
  rule sees more (a changed GraphQL operation or statement reads changed), pinned scenario by scenario. Red on 5.12.1
  by construction.
- `The_first_run_after_the_move_reads_its_verdicts_through_the_bridge`, and `A_version_move_keeps_the_alternating_memory`.
- `Every_kept_version_computes_its_lines_from_the_raw_call` and `Every_change_to_a_recorded_call_line_moves_the_version`:
  the guards, so a move cannot ship without its function and a capture change cannot ship without a move.
- `Editing_a_consumer_rule_keeps_every_verdict`, `Removing_a_consumer_rule_keeps_every_verdict`: through the bridge,
  with the ledger read before the run is built.
- `The_bridge_is_written_only_while_runs_under_another_rule_are_in_the_window`,
  `A_fragment_without_a_ledger_bridges_every_kept_version` and `Record_keeps_only_the_bridges_the_window_needs`.
- `The_evidence_names_calls_across_a_move`, `Reordered_compares_across_a_move` and
  `A_renamed_party_is_not_a_new_dependency_across_a_bridged_change`.
- `A_fold_of_shards_on_two_versions_keeps_its_fingerprints`.
- `A_pruned_ledger_keeps_the_patterns_its_hashes_name`, and
  `Prune_compact_attempts_and_fold_keep_members_they_do_not_know`.
- `Record_warns_when_a_fragment_is_newer_than_the_tool`, `Verify_checks_a_bridges_length_against_its_roster` and
  `The_run_line_names_the_tool_that_wrote_it`.
- `The_loader_and_the_line_parser_skip_a_kind_they_do_not_know`. `A_line_the_translation_cannot_reach_quiets_only_its_own_scenario`
  (a translated change's line with no shared form, as #125's "no" rows: no verdict on its first run after the change,
  never "changed", and the other scenarios keep their memory) goes with the translation, in #125.
- Where #190's R0 proved a property for its own move, the fact kept its name there (`HistoryBridgeTests`:
  `The_alternating_memory_survives_the_move`, `A_real_change_across_the_move_reads_changed_and_names_the_call`,
  `A_renamed_party_is_not_a_new_dependency_and_a_new_one_still_is`,
  `A_run_appends_its_bridge_while_the_window_needs_it_and_its_fragment_always_carries_it`), and
  `Record_keeps_a_fragments_bridge_only_while_the_ledger_window_holds_its_rule` is the record fact; this move's are in
  `HistoryMoveReplayTests`, `HistoryPatternBridgeTests`, `HistoryRawCallsTests`, `HistoryRecordRawCallsTests`,
  `HistoryIdAliasesTests` and `ShapeGuardTests`, and the tracker half of the guard is one
  `Every_change_to_a_recorded_call_line_moves_the_version` in each tracker's project.

R1, the defects:

- `One_operation_rule_answers_for_every_site`: one corpus of URIs (characters that escape included, `%20`, a control
  character and non-ASCII) and bodies through history, warm-up and the component diagram, one answer per site's
  `queryKeys` setting (two implementations of one rule get a parity fact, 3.30.1).
- `Two_graphql_operations_on_one_endpoint_are_two_shapes`,
  `Two_anonymous_operations_with_different_root_fields_are_two_shapes`, `Root_fields_are_sorted_and_de_duplicated`
  and, through the analyzer over two runs, `A_changed_graphql_operation_reads_behaviour_changed`.
- `A_set_prologue_is_not_the_statement_head`, `A_query_carried_in_a_document_is_templated_as_a_statement` and
  `Bytes_written_raw_are_bin` (`InteractionShapeTests.cs` L50's key, read through the data file's string).
- `A_long_opaque_segment_is_kept`: `breakfast-equipment-alerts` and `PancakeBatchCompletedEvent` stay, so the rule
  F2 rejects stays rejected.
- `Warm_up_judges_each_graphql_operation_against_its_own_baseline`, and
  `The_component_diagrams_endpoints_fold_ids_as_history_does`.
- `A_variable_named_query_is_not_the_operation`, `A_key_named_query_in_an_input_object_is_not_the_operation`,
  `A_persisted_query_is_one_operation_in_both_forms`, `A_get_is_named_from_its_query_string`,
  `A_search_apis_query_parameter_is_not_an_operation`,
  `A_document_that_opens_with_a_fragment_or_a_comment_names_its_operation` and
  `OperationName_picks_the_operation_and_its_type`, each also through `PlantUmlCreator`, so the drawn label is
  checked, not only the reader's answer.
- `Two_anonymous_queries_in_a_row_are_two_arrows`, `The_collapse_key_keeps_its_parts_apart` and
  `A_body_the_reader_names_is_formatted_as_graphql`.
- `An_sql_batch_with_two_set_statements_is_classified_by_its_statement`.
- `A_retried_run_keeps_its_consumer_rules`, and `Query_history_after_record_reads_the_ledger_once` (counted reads).
- `Services_and_group_by_service_agree_row_by_row`, and `Path_still_splits_names_that_differ_in_case`.

R2:

- `Group_by_caller_separates_the_seeding_from_the_subjects_calls` (the acceptance's first clause, test-owned seeding).
- `Group_by_route_folds_ids_and_splits_graphql_operations` (its second and third).
- `Route_applies_the_runs_recorded_templates`, `Route_on_a_report_from_before_the_block_says_so_once` and
  `Route_on_a_report_without_rules_says_nothing`.
- `Route_reads_only_statement_and_post_bodies_once_each`: counted through `BodyCache`; GETs and PUTs stay unread, and
  every body read is read once over one handle.
- `A_report_pattern_that_times_out_is_dropped_for_the_rest_of_the_command`.
- `Phase_filters_setup_action_and_unknown` (`unknown` matching a call recorded without a phase), and refuses any other
  value at exit 2.
- `Interactions_json_rows_name_the_caller`, and `Wide_routes_are_cut_in_text_output`.
- `The_templates_block_is_written_only_when_there_are_rules` (the golden corpus byte-identical without rules), and
  `The_templates_block_is_in_every_data_format`.
- `Merge_keeps_the_templates_when_every_input_agrees`, and drops them with a diagnostic when two inputs differ;
  `Merge_history_reads_with_the_shards_rules`.
- `Pairing_pairs_graphql_calls_by_operation`, and with the recorded rules.
- The existing `SkillDriftTests` facts on the dimension list (L250, L265), red until the skill and the help name the
  new dimensions, and `DescribeTests` for `--phase`.

R3:

- `Origin_is_the_callers_role_from_the_parties_block`, and `Origin_uses_the_inferred_roles_when_nothing_is_declared`
  (#190's resolver).
- `A_declared_in_process_subject_reads_subject`, `An_undeclared_in_process_subject_reads_test` (the limit, pinned) and
  `Brokers_deliveries_read_other`.
- `A_seed_through_the_subjects_client_reads_subject`: the limit, pinned, so a later change to it is a decision.

### 6.3 Mutations

Each named mutation must fail at least one fact; one that survives means a fixture never reaches its case (5.7.3):

- drop the GraphQL suffix from `CallOperation.Of`;
- name an anonymous operation by its type alone, or keep its root fields in the order written;
- keep the query keys in `route`;
- keep `PathAndQuery` as history's input;
- keep the SET prologue as the head, keep a document's query string as `{v}`, or match `{bin}` on escapes only;
- read GET and PUT bodies for `route`;
- skip the recorded templates in the engine, or keep a report's timed-out pattern;
- write `shapeTemplates` when the list is empty;
- leave `--group-by service` on the ordinal comparer, or change the bucket dictionary's comparer (folding `path`);
- restore the detector's first match on `"query"`, or read a search API's `?query=` as an operation;
- leave `InteractionShape.Version` where it is;
- compare runs of different rules without the bridge (5.12.1's `SameRule`);
- quiet every scenario when one line cannot be bridged or translated, or read that line's scenario as changed;
- drop the bridge from the run line, write it on every run, or make it from the run's templated lines rather than its
  raw calls;
- leave `NewDependencies` unbridged;
- let `Attempts` drop `shapeRules` again, or let `prune` drop a member it does not know;
- read a ledger with an unknown line kind as damaged again;
- strip one SET statement only;
- key the arrow collapse on the detector's label.

### 6.4 Checks

- The harness's measurements (3.6, `measure/` and `route/`) run again on the built tool, on the same reports and the
  stand-ins, before R2 ships.
- The ledger replay (`harness/ledger/`) over BreakfastProvider's `kronikol-history` branch with the R1 build: within
  one rule the bridge gives every reading the rule gate gave, and across the move the readings a gateless analyzer
  gives, less the changes the rule made (`move46.cs`, `resetcost.cs`).
- The published 5.12.1 tool (`dnx Kronikol.Tool@5.12.1 history verify`, nothing installed) over a ledger R1 wrote:
  every run read, no damaged line. A unit fact cannot hold two versions of one assembly, so this is a check.
- A Release build of `release.slnf` (net10.0, `Directory.Build.props` L5-6).
- The full core suite, and the Playwright classes that draw GraphQL labels, collapsed arrows and the component
  diagram, since R1 changes report output.

---

## 7. Documentation and ledger

Grep the wiki for every name a release touches before it ships; the lists below are where to start, not where to stop.

- **Querying-Reports**: the `--group-by` section (L856-880: the dimension list, "URI path, query stripped", the claim
  that a `service` bucket carries the same bytes as the service's row, L871-872, which R1 makes true), the JSON table
  (L402), `--phase` beside the other filters, `caller` on the `interactions --json` row, and in R3 the origin rule
  with its limits: seeding through the subject's own clients reads `subject`, so seed through a client tracked under
  the test's caller name to keep it apart; an undeclared in-process subject reads `test`.
- **Cross-Run-History** (six mentions of `HistoryShapeTemplates`) and **Report-Configuration**: the rules now also shape
  `route` and call pairing, see the data file's string, and are written into the report and the ledger; the shape
  version moves, with what a GraphQL operation and the head fixes do to the fingerprint. Its account of a rule change
  (L453, "the first run after an upgrade ..."; L499, "A rule edit costs one quiet run"; the option row at L897)
  becomes the bridge, and the ledger's description gains the run line's `bridge`, recorded patterns and tool version.
- **The History action's page**: bump the action folder's `VERSION` with the package, or `record` folds with an older
  tool, which drops what it does not know and now says so (4.8).
- **Why-Is-My-First-Scenario-Slow** and **Generated-Reports**: warm-up shapes split by GraphQL operation and statement.
- **Content-Formatting**: the GraphQL arrow labels for persisted queries, GETs, fragments first and a variable named
  `query`, and notes that now format as GraphQL.
- **Component-Diagrams** (L398-409) and **API-Reference**: whatever R1's check finds about the endpoint rows.
- **Ingesting-External-Captures** and **Ingesting-From-Jest**: an ingested report carries no templating rules; the
  in-process subject case for origin (R3).
- **Phase-Aware-Tracking** (L32): `--phase` as a filter.
- **The skill**, both copies (`.claude/skills/...` and `templates/skills/...`): `references/commands.md` L152 and
  L318-337, `SKILL.md` L111 and L123. `SkillDriftTests` holds the dimension list to the code.
- **Doc comments that go stale**: `ReportConfigurationOptions.HistoryShapeTemplates` (L791-810: now also the route
  and call pairing, the data file's string as input, written into the report and the ledger, and "editing a rule
  costs one quiet run" no longer true), `HistoryRun.ShapeRules` (`HistoryModel.cs` L230-235, the same sentence),
  `InteractionShape.Version` (L36-51: the move, the bridge, the versions kept), the comment on `SameRule`
  (`HistoryAnalyzer.cs` L488-492), the `UriPath` and `GroupByDimensions` comments,
  `GraphQlOperationDetector.TryExtractLabel`'s summary and `SequenceCollapser.KeyFor`'s.
- **The data file's schemas** (`ReportGenerator.cs`, the JSON schema's root object at L7444, and the YAML and XML
  forms): `shapeTemplates`, and R3's reading of #190's block.
- **CHANGELOG**: per release, which part of the version moved and why, with section 5's list of what users see.
- **Kronikol4J divergence ledger**: the reader's fixes change drawn labels, the collapse and note formatting, and
  Kronikol4J has its own port (`kronikol4j-diagram/.../GraphQlOperationDetector.java`), so either the port gets the
  same fixes or the ledger says it does not; the same for the SQL classifier's SET rule where it is ported; Kronikol4J
  writes no `shapeTemplates` block; it has no history fingerprint, so the version move does not reach it.

---

## 8. Coordination

Three sessions planned the three sibling issues on 2026-10-26, each in its own worktree from `44bcc62a`, and agreed who
owns what by message. On 2026-10-27 two more plans were found to change what this plan's rules read, and were told:

| Plan | Session, worktree | Owns | Takes from this plan |
|---|---|---|---|
| this plan (#192) | kronikol-bf, `C:/Code/Kronikol-192` | the operation key (4.1), the GraphQL operation (4.2), origin (4.3), the engine's dimensions and filter (4.4), the rules block (4.5), history across a change of rule (4.8) | |
| `PARTY_IDENTITY_PLAN.md` (#190) | kronikol-67, `C:/Code/Kronikol-190` | what a party is: id, kind, `kindSource`, the `parties` block, the inference of the test client and the subject (one resolver in `src/Kronikol/Parties/`, for the writer, merge, the engine and the card); R0, fallback names | 4.8 for R0's version move; origin reads its roles |
| `SERVICE_CARD_PLAN.md` (#191) | kronikol-ed, `C:/Code/Kronikol-191` | `service.json`: its edges and operations, format, schema, byte stability, its `Run.json` listing, the backfill verb | 4.1, 4.2, 4.3, 4.5 and, if Q4 says so, `cause` |
| `EMULATOR_URL_SHAPES_PLAN.md` (#125) | kronikol-5d, `plans/` of the shared checkout | what four Google and Azure extensions record against emulators | 4.8: its changes move recorded lines |

- **#190's R0** also moves `InteractionShape.Version` (`PARTY_IDENTITY_PLAN.md` L813-817; "R0 now", L1080): a patch
  that renames fallback callee names, planned with today's quiet run. Agreed by message on 2026-10-27: numbers are
  claimed at push, and whichever ships second takes the next version. If R0 ships first, R1 bridges R0's move too,
  with R0's function from the new record to the old line (`{host}:{port}` read back as `localhost:{port}` from the
  record's URI). R0 moves the version, which is what lets R1 tell its runs apart: a run line records no tool version
  today (`HistoryModel.cs` L224-228).
- **#125** changes recorded methods and URLs, and a Detailed or Summarised record keeps no raw URL, so its change needs
  a translation of stored lines (4.8, part 2), not a bridge. Its section 7 tabulates which lines a translation
  reaches; this plan cites that table rather than restating it. Its Q7 asks whether #125 waits for R1 (recommended
  there) or ships first, with or without a version move.
- **One requirement the card puts on this plan**: the operation key, origin and `cause` are functions of what a report
  holds, so `kronikol card` run on an old report and the file a run writes agree. The design keeps to it: the key reads
  the data file's URI string, the bodies and the recorded rules; origin reads the `parties` block or #190's resolver;
  `cause` reads `attributionSource` and the nesting.
- **#184**: R2 delivers its third ask (`caller` on rows). Its other asks (the test id, a diagram count,
  `diagram sN --out DIR`) are not in this plan.
- **FLEET_EVIDENCE_PLAN**: #190 is its S1 and #191 its S4. Its S2 `graph` verb stays where D23 leaves it.
- **Order**: R1 needs no other plan; #190's R0 may ship before or after it (above). Then #190's R1, then R2 and R3,
  and the card last, since it reads all three (`PARTY_IDENTITY_PLAN.md` section 6 gives the same order). Version
  numbers are claimed at push ("taking X now"), never in advance.
- **Shared files**: `InteractionShape.cs` and `src/Kronikol/History/` (#190's R0 and R1), `QueryCommand.GroupBy.cs`
  (R1's F7 and R2's dimensions) and `VerbTable.cs` (R2; also `CHANGED_SCENARIOS_PLAN.md`'s `query changes`, #78),
  one stage at a time (roadmap rule 5); `ReportScanner.cs` (#190 reads its block there too), `ReportGenerator.cs`'s
  schemas, `CHANGELOG.md`, `PLANS_STATUS.md`, and the wiki's Querying-Reports and Cross-Run-History pages.

---

## 9. Questions for the owner

All answered on 2026-10-27 and 28, in the owner's words where they gave them; the design text above follows the
answers.

1. **Q1. Closed (2026-10-27).** The issue's three reports are not on this machine; section 3.6 measures stand-ins
   made here instead, and nothing is asked of the owner.
2. **Q2. `{n}` or `{id}` for numbers.** *Answered: keep `{n}`.* A number in a path is not always an id.
3. **Q3. When origin ships.** *Answered: after #190's R1.* #190's R1 shipped as 5.14.0, so origin and `cause` ship in
   R2 with the engine's dimensions (section 5).
4. **Q4. A `cause` dimension** (`test`, `request`, `background`, `unknown`). *Answered: yes, with origin.*
5. **Q5. A separate `graphql` dimension.** *Answered: no;* `route` carries the operation.
6. **Q6. Batched GraphQL bodies.** *Answered: mark as `batch`,* only when the body is an array whose every element is
   a GraphQL operation.
7. **Q7. Templating rules for `kronikol ingest`.** *Answered: add it now,* in R2: an ingest option that takes the
   rules and writes them into the report's `shapeTemplates` block.
8. **Q8. The background calls table.** *Answered: fold it* with the key, in R1, called out in the changelog.
9. **Q9. The option's name.** *Answered: keep `HistoryShapeTemplates`;* its doc comment and the wiki say what it
   governs.
10. **Q10. `--phase` on other verbs.** *Answered: `interactions` and `values`.*
11. **Q11. Seeding through the subject's own clients.** *Answered: document the limit and the recipe;* no marker.
12. **Q12. `route` on `interactions --json` rows.** *Answered: not now.*
13. **Q13. The arrow label of an anonymous GraphQL operation.** *Answered: the label shows the root fields,*
    `query { orderSummaries }`.
14. **Q14. Which way the service comparer goes.** *Answered: merge, as `services` does;* bucket keys, `path` and the
    views that pair or nest calls stay ordinal.
15. **Q15. Kronikol's own aliases for scenario ids.** *Answered: build it in R1* (not the recommendation). When
    Kronikol changes what a stable id is computed from, the run writes the pairs that differ on its run line, and the
    analyzer reads them as it reads `aliases.json` (4.8). Built as the run line's `aliases` member and
    `ScenarioStableId.EarlierRules`, empty until an id changes; the analyzer, `record` (its rename suggestions and its
    partial check) and `import` read them.
16. **Q16. A run that changes most scenarios at once.** *Answered: its own issue,* reading such a run as one
    suite-wide change, named once with the lines that moved most. Filed when this plan is committed, as
    [#217](https://github.com/lemonlion/Kronikol/issues/217).
17. **Q17. What the consumer's rules see.** *Answered: the data file's unescaped string everywhere,* with a
    diagnostic naming a pattern that holds a `%XX` escape, called out in the changelog.
18. **Q18. A consumer's rule edit in the fragment flow.** *Answered: close the gap.* The fragment carries the run's
    distinct untemplated targets, so `history record`, which has the ledger, bridges the edit; R1 measures the
    fragment's growth on BreakfastProvider's lanes first and states it in the changelog. Measured: 15% to 76% more
    fragment, 18 KB to 147 KB as written, and nothing more in the ledger (4.8).

---

## 10. Log

- 2026-10-26: written at 5.11.0 (`44bcc62a`) in the worktree `C:/Code/Kronikol-192`, branch `plan/192-group-by-edges`.
  Ownership of the shared rules agreed with kronikol-67 (#190) and kronikol-ed (#191) by message; their measurements
  on BreakfastProvider's CI run 40290999167 changed origin from a causal rule to the caller's role (phase cannot
  separate the test's work from the subject's background work).
- 2026-10-26 to 27: a read-only code sweep (caller naming, causality fields, nesting, attribution, route templates,
  phase) and a measurement agent over five BreakfastProvider reports (section 3.6, `harness/results/measure/`); the
  anonymous GraphQL finding (F4) came from it. Probes run: `graphql-detector.cs`, `service-case.cs`. Every claim
  re-read against its source, and a dozen corrected (a proposed rule called built, lanes mixed, unsupported phrases).
- 2026-10-27: the owner closed Q1 (the reports are not here, and the work is ours to do: stand-ins are being made) and
  asked, on Q2, for history that survives a change of name. Section 4.8 added; R1 became a minor; Q15 added. Checking
  the design against the ledger code found that a new line kind would read as damaged in older builds, so everything
  new is a member of the run line.
- 2026-10-27: stand-ins measured (3.6): a Jest ingest made from the repo's own harness, and BreakfastProvider's
  NUnit, TUnit, LightBDD and BDDfy lanes; a Kronikol4J report with calls could not be made here (3.6). Two findings
  for other plans recorded in 3.9.
- 2026-10-27: checked again at 5.12.1 (`79e8d644`), at the owner's ask to make sure it is complete and accurate, by
  four reviews: every citation (159 held, 33 corrected), an adversarial reading of the design (19 findings, each
  re-read in the code before it was taken), a replay of BreakfastProvider's real ledger (`harness/ledger/`,
  `results/ledger/`) and a prototype of the key on the same reports (`harness/route/`, `results/route/`). What
  changed: 4.8 bridges a change instead of translating stored lines, since a stored line cannot be split into its
  fields and the best translation of the real 4 to 6 move recovered 70.6% of it; every comparison across runs goes
  through the bridge, dependency pairs and named calls included; the run line gains the tool version; writers carry
  what they do not know. The key gained two head fixes and a wider `{bin}`, and the GraphQL reading became a strict
  reader with sorted root fields. Six defects found and put in R1 and R2 (3.9). #190's R0 and #125 found to change
  the same rules, and both sessions told; #125 adopted the line-by-line rule. Q16 to Q18 added, Q15's
  recommendation turned to "not now".
- 2026-10-28: green-lit, with the owner's answers to Q2 to Q18 (section 9), and revised for the 5.16.x baseline:
  #190's R1 to R3 had shipped, so origin reads its parties table, and F8 was already closed by it (the attribution
  source is in the index since 5.14.0). Q15 was answered "build it" and Q18 "close the gap", so R1 carries Kronikol's
  own scenario aliases and the fragment's raw calls. R1 built in the worktree on rule 8, test first. Found on the way
  and fixed in R1: `HistoryReordered` never fired since 3.15.0 (the count's branch took every run before it); a
  rename of more than a tenth of a suite made the next run partial (the partial check read the previous roster
  without `aliases.json`); counts were compared across rules unbridged, so a bridge now holds a position's count
  where its rule counts otherwise; an EventBridge bus given by its ARN threw into the consumer's `PutEvents`; the
  merge's defaulted-result diagnostic named an unnamed scenario as a blank. The collapse key's "no separator" claim
  was wrong (it joins with U+001F); the plan says so and a fact pins it. The guard of 4.8 was checked against the
  release that made the kept rules: `ShapeGuardTests`' 42 captures, read by 5.16.4 as its trackers recorded them and
  by 5.17.0 from the raw call, give byte-identical lines under rules 6 and 7, with no patterns and with nine
  (`harness/kept-rules/`, `results/kept-rules/`). Q18's block measured again with the encoder the fragment writes
  (`harness/q18/`, `results/q18-raw-calls.txt`). An independent review of the built code before release found
  six defects in it, each fixed test first: a newer writer's member carried from an indented fragment split its run
  line; a fold beside a run without fingerprints threw; the patterns bridged were the suite's last lines' rather than
  each compared stream's window (#95), so a stream another's runs crowded out still took a quiet run; the ingest's
  scheme mend took text before a `://` in a query for a scheme; a span written as records lost its rule 6 and 7
  reading; and hand-edited aliases or counts, or a torn line of a newer kind, threw or misread.
- 2026-10-28: R2 built on 5.18.0 and released as 5.19.0, test first: the `caller` and `route` dimensions, `caller` on
  `interactions --json` rows, `--phase` on `interactions` and `values`, the `shapeTemplates` block in every data format
  and its schemas, read by the engine (a pattern dropped at its first timeout), by call pairing, by `merge` and by
  `merge --history`, and Q7's ingest option. Q7's option takes a file, `--shape-templates FILE`, in the block's own
  shape, since a pattern holds what every shell quotes and `(?<=` holds the `=` a `PATTERN=PLACEHOLDER` form would split
  on. A route is cut at 80 characters in text. The note on an older report is 4.4's but for one phrase: a report
  Kronikol4J writes carries the port's own version and records no rules, so "predates recorded HistoryShapeTemplates"
  was false of it, and the note says the report "cannot say whether its run had HistoryShapeTemplates". Found on the
  way and fixed: `merge --history` read the window alone, so
  a merged run its shards had recorded sent the analysis back to read the whole ledger, as `query history` did before
  R1, and it bridged no edit of the consumer's patterns, which `query history` has since R1; call pairing's exact key
  held no GraphQL operation either, so the operation in the shape key alone (the table in 4.1) would still have paired
  the n-th `POST /graphql` with the n-th, since the shape key is tried only for what the exact key leaves; both keys
  now hold it. A run that writes the block for a rule given no placeholder writes an empty one, where the
  YAML writer threw. kronikol-ed's card found, on 5.17.0's key, that a Cosmos DB LINQ statement keeps its boolean
  literals (`WHERE ((true OR ...` beside `((false OR ...`), since a query's text templates numbers and strings and a
  document's booleans only: stable across runs, and finer than the operation. Templating them moves the fingerprint,
  so it is left for the owner rather than taken into this plan. An independent review of the built code before
  release found seven defects in it, each fixed test first: `route`'s notes broke `--count`'s one token; a null rule
  list or a null rule lost the run its data file, and an empty pattern was recorded; keying each report's calls under
  its own rules stopped `diff --body` pairing on the shape across a rule edit, so the two reports' calls are keyed
  under the rules both recorded, or the built-in rules alone where they differ; a response recorded without its
  request had its rows read as its statement; diff and compare skipped a failing rule in silence; the note on an
  older report was said of one that holds the block; and two statements on one table paired by position, so the
  exact key holds a statement's head, under the built-in rules, as it holds a GraphQL operation. It also found that a
  dimension named twice threw under `--json` since `--group-by` was written, now refused at exit 2.
