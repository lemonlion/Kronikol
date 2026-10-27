# The service card: one call graph, and `service.json` from any run (#191)

**Date:** 2026-10-26 · **Repo version:** written against 5.11.0 (`origin/main` at `44bcc62a`); every Kronikol line cited is
at that commit · **Status:** ❌ plan written, nothing implemented, **NOT green-lit**. Written at the owner's request of
2026-10-26: analyse #191 critically, then plan a fix for the underlying problem, which may not be the fix the issue
proposes.

Two sibling plans were written the same day, by agreement, so that the three issues get one design rather than three:

- `PARTY_IDENTITY_PLAN.md` (#190, session kronikol-67): the subject's identity, each party's id, kind and `kindSource`,
  and the rule that infers the test client and the subject when nothing is declared.
- `CALL_OPERATION_AND_ORIGIN_PLAN.md` (#192, session kronikol-bf): the one key that says what operation a call is
  (built on `InteractionShape`), `origin`, `cause`, and the `--group-by` dimensions.

This plan cites them for those rules and does not restate them. Section 4.0 has the split. At the time of writing both
live in their sessions' worktrees (`C:/Code/Kronikol-190`, `C:/Code/Kronikol-192`), not on `main`.

Evidence labels:

- **RUN** means measured on 2026-10-26 with the probes in [`SERVICE_CARD_PLAN.harness/`](SERVICE_CARD_PLAN.harness/README.md),
  on Windows 11 26200 with .NET 10 and the 5.11.0 tool built from `44bcc62a`. The inputs were:
  - BreakfastProvider, the reference consumer: CI run 40290999167 on `cf47045` (Kronikol 5.4.1), four lanes' data
    files (`xunit-in-memory`, `reqnroll-in-memory`, `xunit-external-sut`, `reqnroll-in-docker`), fetched with
    `gh run download` and decoded with `kronikol decode`;
  - its history ledger, `history.jsonl` on its `kronikol-history` branch: 1,599 run lines from 18 suites;
  - the three Example.Api component reports (5.8.0), copied into `inputs/examples-5.8.0/`;
  - stand-ins kronikol-bf made on 2026-10-27 for the stacks the issue was measured on (A3): the same CI run's
    `nunit`, `tunit`, `lightbdd` and `bddfy` in-memory lanes, and a Jest ingest (5.11.0, from
    `INGEST_FIDELITY_PLAN.harness/otel-jest` through the wiki's recipe), kept in its plan's harness.
- **READ** means Kronikol's source at `44bcc62a`.
- **PEER** means measured or read by a sibling plan's session; the script is named where there is one.
- **ISSUE** means the issue's own numbers, not measured again: the three reports it used are not on this machine
  (searched on 2026-10-27, A3).
- **INFERRED** means reasoned from two facts above and stated by neither.

## 0. Summary

- **The issue names two problems, and only one of them is about a file.**
  - *Access.* An indexer has to decode every report and rebuild the edges itself. True, but cheap to fix: one pass
    over a report costs what one query costs (RUN: 0.26 to 0.34 s on BreakfastProvider's lanes, process start
    included; ISSUE: 0.75 s on a 198 MB report).
  - *Meaning.* The indexer "can't tell the service's calls from the test's". This is the real problem, and a file
    does not solve it. A file only moves the guess from the indexer into Kronikol, and if Kronikol guesses from the
    names on the calls, it is wrong in the same places.
- **The underlying problem: Kronikol has no single model of who called whom, and why.**
  - It derives "who calls what" five times, with four rules for what one call is: the component diagram's
    relationships, history's shapes and `deps`, the warm-up's shapes, `query services` and `--group-by` (READ, PEER,
    section 2.1). None is published as data in the standard report. The issue's `service.json`, with a templating
    rule of its own, would be the sixth.
  - A call records who made it, never why. The caller's name is a static option of each tracker's registration
    (READ), so a call the test makes through the service's own clients carries the service's name. The query engine
    already works out which call each call ran inside (`CallNesting`), but only to indent `flow`.
  - A report never says what it could not see. On BreakfastProvider's external-SUT lanes none of the service's own
    calls are captured (RUN: one edge, `Caller>Breakfast Provider`), and a merged Kronikol4J report holds no calls at
    all (PEER, F11). A summary of such a run lists a service with no dependencies, and that reads as a fact.
- **What the measurements change in the issue's design.**
  1. **The service's own calls are not one thing.** On three lanes, 755, 731 and 665 of them ran inside a call the
     service received; 86, 68 and 151 ran outside any. On BreakfastProvider the second group is background work: the
     outbox poller, gRPC notifications, Cosmos DB housekeeping, BigQuery and ClickHouse inserts. Those are real
     dependencies. In a suite that seeds data through `factory.Services`, the same position holds the seeding (RUN,
     INFERRED). #192 now makes `origin` the caller's role and adds `cause` (test, request, background, unknown) to say
     which; the card carries both.
  2. **Phase cannot break the tie, but nesting can repair it.** On the Reqnroll lane every call the service made
     inside a received request has no phase, 731 of 731. The identity reaches the service in a request header
     (`attributionSource: RequestHeader`), and the header carries the test's id but not its phase (RUN). The phase of
     the call they ran inside is known.
  3. **"Byte-identical for the same behaviour" contradicts the issue's own fields.** Over 1,005 pairs of consecutive
     runs of one suite on one commit (RUN, the ledger): some scenario's call count changed in 37.6% of the pairs, some
     call order in 79.1%, the run's set of distinct operations in 2.9%, its edges in 0.4%. Counts, errors, example
     addresses, run ids, commits and the data file's SHA-256 change from run to run. Only presence (which edges, which
     operations, which statuses) is stable enough to promise bytes for.
  4. **The templating rule already exists.** `InteractionShape.Template`, version 6, folds GUIDs, hex ids, ULIDs,
     cache-key bytes, timestamps and numbers, keeps query keys and drops their values, applies the consumer's
     `HistoryShapeTemplates` first, and gives statement-shaped calls (SQL, Cosmos DB, MongoDB) their templated
     statement head. History and the warm-up already compare calls by it. `/orders/123` becomes `/orders/{n}`, not
     `/orders/{id}`. On BreakfastProvider it leaves 175 operations where raw paths make 207 buckets (RUN). #192 owns
     the one shared key built on it.
  5. **Most parties have no path, and many have no host.** Most of BreakfastProvider's 18 parties are databases and
     brokers. On its in-memory lanes 7 of 16 called parties have no host in any call's URI (both SQL Server databases,
     Pub/Sub, Kafka, MongoDB, the gRPC notification service, Spanner) and Event Grid has only the placeholder
     `topics`; in docker, 6 of 17 and the same placeholder (PEER, `hosts.py`). An operation is a method and a
     templated target: a path, a statement head or a topic.
  6. **It is not an 8 KB file.** On a 212-scenario lane the edges and operations alone are 20 to 32 KB of compact JSON;
     with the scenario index and counts, 67 to 78 KB (RUN). That is fine for an indexer. A model's first read is
     FLEET's `Service.md`, which this plan does not build.
- **The plan.**
  - **One call graph, in core** (4.2): parties, edges, operations, and for each call its parent, `origin`, `cause` and
    effective phase. It is built from a neutral call record that both a running suite and any report Kronikol can
    read map into, the way `kronikol ctrf` already serves CTRF from one writer and two readers.
  - **`service.json` is a projection of it** (4.3), in two parts. `behaviour` holds sets only, in a total order, and
    is byte-identical when the behaviour is; its SHA-256 is `behaviourHash`. `run` holds the per-run facts, the counts
    and the example addresses. A `capture` block says what the run could not see.
  - **`kronikol card <report>`** writes the same file from any report, 4.x JSON or 5.x `.krkl`, through the same
    writer (4.4).
  - **At run end** the file is written beside `Failures.md`, behind `GenerateServiceCard`, and `Run.json` lists it
    and carries its hash (4.5). Ingested runs get it too; Kronikol4J's runs get it through the tool.
- **Releases.** R1, a minor: the call graph and `kronikol card`, with the committed JSON Schema; no report byte
  changes. R2, a minor: the run-end file, its option and `Run.json`'s new key. The three plans run in the order
  `PARTY_IDENTITY_PLAN.md` §6 sets: #190 R0, #190 R1, #192, then this plan, since the card needs #190's resolver and
  #192's operation key, `origin` and `cause`. Fourteen questions are in section 7.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | One 781-scenario report is a 6.7 MB `.krkl`, or 198 MB as JSON | **Plausible.** BreakfastProvider's 212- and 214-scenario lanes are 136 to 388 KB on disk and 2.0 to 14.1 MB as JSON. The largest ratio is 36 to 1; the issue's is 30 to 1. | ISSUE; RUN |
| 2 | `interactions --json` has no caller (#184) | **True** at 5.11.0. The item carries `service` and no caller (`QueryCommand.Payloads.cs:131`). | READ |
| 3 | `--group-by` has no caller or origin dimension | **True.** Nine dimensions (`QueryCommand.GroupBy.cs:14-15`). | READ |
| 4 | Paths aren't templated | **True of `--group-by`**, which groups by the raw `AbsolutePath` (L117-123). **False of Kronikol**: history and the warm-up template every call (`InteractionShape.cs:102`). | READ |
| 5 | Rebuilding the edges means running `flow` per scenario, or `decode` plus custom parsing | **True for a reader outside Kronikol.** Inside, every run end builds the edges for the component diagram (`ComponentDiagramGenerator.cs:54`) and writes them as data only into the mergeable data file (`ReportGenerator.cs:5780`), which is off by default. | READ, PEER |
| 6 | Every query decodes the whole file, about 0.75 s each | **True**: each invocation scans the report (`QueryCommand.cs:172`). 0.26 to 0.34 s on BreakfastProvider. It costs a reader that asks many questions of each report; a card is one pass. | ISSUE; RUN |
| 7 | That report is 100 pages of `interactions --json` | **Plausible.** The listing pages at 120 calls (`QueryCommand.Payloads.cs:109`). | ISSUE; READ |
| 8 | No run-end file an indexer can fetch beside `Run.json` | **True.** `Run.json` is the natural first fetch: written last, it names the run, the suite, the counts and every file that reached disk (`RunManifest.cs:27`). | READ |
| 9 | One report marks the test's seeding as Setup and leaves the service's downstream calls with no phase | **Reproduced and explained.** On the Reqnroll lane the service's calls inside a received request have phase `Unknown`, 731 of 731, all attributed by `RequestHeader`. The test's own calls, attributed by `TestContext`, have theirs (Setup 227, Action 216). | RUN |
| 10 | Jest marks every call Action, under one synthetic `Run:` step | **Not measured** (no Jest report here). It agrees with ingest's keyword rule (`IngestAttribution.cs:450-451`). | ISSUE; READ |
| 11 | `origin` works even where phase is missing | **Half true.** As the caller's role it works where the test acts with a client of its own. It cannot separate a test that acts through the service's own clients, because caller names are static per registration (2.2). `cause` covers part of that; no recorded signal covers the rest (#192). | READ, RUN, PEER |
| 12 | Two runs with the same behaviour can produce byte-identical files | **False for the fields the issue lists, true for presence.** Over 1,005 same-commit run pairs: counts 37.6%, order 79.1%, operation set 2.9%, edges 0.4% (2.4). | RUN |
| 13 | `kronikol card` can write the file from a 4.x JSON report and from a 5.x `.krkl` | **Feasible.** `ReportScanner` reads both, as `kronikol ctrf` does (`CtrfCommand.cs:80`). Without declarations in the file, the card infers every party and says so. | READ |
| 14 | Source file and line "when the run has it" | **Reqnroll lanes: every scenario** (214 of 214, feature `sourceFile` too). **xUnit lanes: none** (0 of 212). | RUN |
| 15 | `framework` (e.g. `reqnroll+xunit3`) | **Not recorded anywhere.** A data file's root holds `formatVersion`, `kronikolVersion`, `suite`, the two times, `ciMetadata`, `environment`, `features` and `background`. Null for every existing report. | RUN, READ |
| 16 | `FixedNameForReceivingService` is a display name | **True, and narrower than that.** It is read only for the report's title (`ReportGenerator.cs:1063`). The name a party gets on the calls comes from the handler's options, so a subject declared from this option could name a party no call carries. | PEER, READ |

## 2. What happens today

### 2.1 Five derivations of "who calls what"

| Where | What one call is | Rule | Published as |
|---|---|---|---|
| Component diagram: `ComponentDiagramGenerator.ExtractRelationships` (`ComponentDiagramGenerator.cs:54`) | caller, service, protocol; protocol is the dependency category, else `HTTP`, else an event's method (L531); with the set of methods | ordinal; requests only, without markers or ignored calls | `ComponentDiagram.html` and the report's Components tab (pictures); the mergeable data file's `componentRelationships` (`ReportGenerator.cs:5780`), only with `GenerateMergeableData` |
| History: `InteractionShape.Calls` (`InteractionShape.cs:209`) | `Caller>Service Method Target Status`, the target templated (L246) | templater version 6 (L52); markers and ignored calls out | the ledger: two hashes and a count per scenario, and the run's distinct call lines |
| History: `InteractionShape.Dependencies` (L278) | `caller>service` | ordinal | the ledger's `deps` (`HistoryRunBuilder.cs:150`) |
| Warm-up: `WarmUpCalls` | a templated target of its own: unescaped, query dropped | | the `warmUp` marks in the data file |
| `query services` (`QueryCommand.Overview.cs:335`) | the called service alone | case-insensitive (L353) | text and `--json` |
| `query interactions --group-by` | any of nine dimensions; the raw `AbsolutePath` | ordinal | text and `--json` |

The component diagram's endpoint breakdown is a fourth target rule: GUIDs only, so `/orders/123` and `/orders/456` are
two endpoints there (PEER: `ComponentFlowSegmentBuilder.cs` L237-238, L710-715). The ledger's call lines cannot be read
back into fields either: party names hold spaces (`Breakfast Provider>Cow Service GET /milk 200`), so a reader cannot
cut the edge from the line (RUN; the probe reads `deps` instead).

### 2.2 A call says who made it, not why

- **Caller names are configuration.** Every tracker's options carry `CallerName` (alias `CallingServiceName`):
  Cosmos DB (`CosmosTrackingMessageHandlerOptions.cs:18`), Redis, Kafka, MongoDB, SQS and the rest. EF Core copies the
  HTTP options' caller (`SqlTrackingInterceptorOptionsExtensions.cs:24`). The defaults are `"Caller"`
  (`TrackingDefaults.cs:12`) and, for Playwright, `"Browser"` (`TestTrackingIdentity.cs:23`). A client registered in the
  service's container names every call through it after the service, whoever made the call (READ).
- **Nesting is computed, then thrown away.** `CallNesting.Parents` (`CallNesting.cs:32`) gives each request its parent:
  the innermost open request whose service is this request's caller, or, for another caller, one on the same Kronikol
  `traceId` (how a delivery sits inside the call that published it). Only `flow` reads it, to indent (READ).
- **How a call reached its test** is in the data file as `attributionSource`, which `ReportScanner` does not read
  (PEER). On the Reqnroll lane (RUN, requests):

  | Caller | `attributionSource` | Phase | Calls |
  |---|---|---|---|
  | the service | `RequestHeader` | Unknown | 731 |
  | the test client | `TestContext` | Setup / Action | 227 / 216 |
  | another party | `RequestHeader` | Unknown | 108 |
  | the service | `TestContext` | Setup / Action / Unknown | 35 / 18 / 9 |
  | the service | `Scope` / `DocumentOwner` | Action / Setup | 3 / 3 |
  | another party | `TestContext` | Action | 3 |

  The header that carries a test's identity into the service does not carry its phase. Calls made in the test's own
  context carry the phase of whatever step started them, including background work started by a step.

### 2.3 Measured on BreakfastProvider

`probes/card-prototype.cs` ports `CallNesting`, calls the product's own `InteractionShape.Template` and
`TemplateStatement`, and infers the test client and the subject by a prototype rule; #190's rule (`infer.py`, PEER)
agrees on all four lanes. Results are in `results/card-prototype.txt`.

| Lane | Scenarios | Requests | Parties | Edges | Operations (raw buckets) | Test | Service, inside a received call | Service, outside any | Other |
|---|---|---|---|---|---|---|---|---|---|
| `xunit-in-memory` | 212 | 1,409 | 18 | 20 | 175 (207) | 451 | 755 | 86 | 117 |
| `reqnroll-in-memory` | 214 | 1,353 | 18 | 20 | 175 (206) | 443 | 731 | 68 | 111 |
| `reqnroll-in-docker` | 214 | 1,261 | 18 | 20 | 164 (194) | 442 | 665 | 151 | 3 |
| `xunit-external-sut` | 212 | 378 | 2 | 1 | 72 (98) | 378 | 0 | 0 | 0 |
| `nunit-in-memory` | 212 | 1,409 | 18 | 20 | 175 (207) | 451 | 755 | 86 | 117 |
| `tunit-in-memory` | 212 | 1,413 | 18 | 20 | 176 (208) | 451 | 755 | 90 | 117 |
| `lightbdd-in-memory` | 187 | 1,379 | 18 | 20 | 175 (207) | 451 | 755 | 56 | 117 |
| `bddfy-in-memory` | 212 | 1,409 | 18 | 20 | 175 (207) | 451 | 755 | 86 | 117 |
| Jest ingest stand-in | 2 | 8 | 4 | 3 | 5 (5) | 4 | 4 | 0 | 0 |

On the five in-memory lanes, five test frameworks driving the same service, the calls inside a received call are 755
every time, and the calls outside any vary from 56 to 90: the Cosmos DB housekeeping repeats once per host start, so
how often a framework starts the host moves it. That is section 2.4's count noise inside one run's numbers. The test's
own phases are recorded on the Gherkin-style lanes (LightBDD: Setup 234, Action 199) and not on xUnit, NUnit and TUnit
(445 of 451 unknown). The Jest stand-in carries no `attributionSource` and no phase on any of its 8 calls, but its
records nest: the service's 4 calls sit inside the test's (`results/card-prototype-stand-ins.txt`). The prototype has
no GraphQL rule, so its 5 operations count the stand-in's two GraphQL operations as one; #192's 4.2 splits them.

The service's calls outside any received call, commonest first: Cosmos DB metadata reads (`Other` on `/`,
`/dbs/BreakfastDb` and `/colls/orders`, 15 to 21 each), the outbox poller's query (`SELECT VALUE root FROM root WHERE
true`, 54 in docker), a `Replace` of an outbox document, `SendOrderConfirmation` over gRPC, one BigQuery insert and one
ClickHouse insert. All of them are the service's own work. "Other" is mostly deliveries: a broker's call into the
service. Read from `attributionSource` alone instead of nesting, the split is the same on both in-memory lanes, 755
and 86, 731 and 68 (PEER, #192's measurement), so the two signals agree where both exist. The docker lane above is the
run's `reqnroll-in-docker` artifact; the same run's `xunit-in-docker` lane holds 1,297 requests (PEER).

The card's behaviour (the prototype's shape, close to 4.3's), in bytes, indented and compact:

| Lane | Everything | Without the scenario index | Without counts or the index |
|---|---|---|---|
| `xunit-in-memory` | 132,762 / 77,781 | 61,969 / 31,979 | 37,773 / 21,683 |
| `reqnroll-in-memory` | 125,067 / 70,129 | 62,560 / 32,165 | 37,773 / 21,683 |
| `reqnroll-in-docker` | 118,201 / 67,065 | 58,349 / 29,788 | 35,133 / 19,978 |
| `xunit-external-sut` | 78,753 / 51,626 | 21,070 / 9,092 | 11,058 / 4,843 |
| Example.Api Reqnroll (10 scenarios) | 4,424 / 2,482 | 2,214 / 1,023 | 1,399 / 682 |
| the other four in-memory lanes | 129,153 to 130,377 / 73,872 to 77,061 | 61,969 to 62,387 / 31,979 to 32,131 | 37,773 to 37,952 / 21,683 to 21,777 |
| Jest ingest stand-in (2 scenarios) | 2,383 / 1,175 | 1,923 / 896 | 1,238 / 606 |

### 2.4 What a summary would keep from one run to the next

`probes/ledger_stability.py` over BreakfastProvider's ledger compares every two consecutive run lines of one suite on
one commit, with the same roster and shape version and neither partial: 1,005 pairs over 18 suites, 197,644 scenario
slots passed in both (`results/ledger-stability-breakfastprovider.txt`).

| What the summary would carry | Changed between two runs of the same code |
|---|---|
| a scenario's call count | 0.39% of scenarios; **37.6% of run pairs** |
| a scenario's set of distinct calls | 0.16% of scenarios; 12.4% of run pairs |
| a scenario's calls in order | 1.69% of scenarios; **79.1% of run pairs** |
| the run's distinct operations (templated, no status) | 2.9% of run pairs (27 of 933 with a shapes table) |
| the run's distinct calls with status | 3.6% of run pairs |
| the run's edges (`deps`) | **0.4% of run pairs** |

The operations that come and go are real non-determinism: a Cosmos DB `Replace` (an optimistic-concurrency retry,
18 pairs), a Spanner `Rollback` (5), and a block of MongoDB calls (4). A run averages 123.5 distinct operations and 13
edges.

### 2.5 What there is to build on

- **`kronikol ctrf` is the pattern.** A run can write `ctrf-report.json`, and the tool rebuilds it from any report:
  `ReportScanner.Scan` (`CtrfCommand.cs:80`) into a neutral `CtrfTest` list, then the one writer,
  `CtrfReportGenerator.Build` (`CtrfReportGenerator.cs:212`). Its doc comment gives the reason it is not a `query` verb:
  "a document that gets truncated is not a document".
- **`Run.json` is written last** and lists what reached disk, from the outputs that actually wrote
  (`ReportGenerator.cs:781`). Its reader ignores members it does not know (`RunManifest.cs:135`), so a new key is
  additive. A new output added to the run-end list (`Add(...)`, as `ComponentDiagram.html` at L579 and the failures
  digest at L594) is listed in it without further work.
- **Ingest goes through the same run end** (`IngestPipeline.cs:694`), so a Jest or Cucumber ingest gets any new run-end
  file. Kronikol4J writes its own reports and would not.
- **One target framework.** 5.x builds `net10.0` only (`Directory.Build.props:5-6`), so the old rule that `Query/` code
  must not be reached from `Reports/` no longer applies.
- **Schemas.** No JSON Schema is committed to the repo. The data file's schema is generated at run end
  (`GenerateTestRunReportSchema`), and `tests/Kronikol.Tests` validates documents with JsonSchema.Net
  (`SchemaValidationTests`, `SchemaClosedContractTests`).

## 3. Findings the issue does not state

| # | Finding | Basis | Consequence |
|---|---|---|---|
| F1 | Five derivations of the call graph, four rules for one call; the card would be a sixth | READ, PEER (2.1) | Build one call graph and make the card a projection of it; pin the component diagram's edges against it |
| F2 | A caller's name is configuration, not observation | READ (2.2) | `origin` as the caller's role cannot separate a test acting through the service's clients. `cause` narrows it; the card must not claim more |
| F3 | The service's calls outside any received call are real work on BreakfastProvider, and the seeding's position elsewhere | RUN (2.3), INFERRED | Neither "subject" nor "test" alone describes them. The card carries `cause` beside `origin` (#192) |
| F4 | Phase is lost at the request header, and is a property of the test's narrative, not of who called | RUN (2.2) | The card derives an effective phase through nesting (4.2), and never uses phase to decide origin |
| F5 | Counts and order are not stable from run to run | RUN (2.4) | Byte identity is promised for presence only; counts move to the run part (4.3) |
| F6 | The templating rule exists, versioned, with consumer rules | READ | One operation key (#192); numbers are `{n}` |
| F7 | Databases and brokers have no path; a Kafka topic is in the URI only at `Detailed` or `Raw` verbosity (`KafkaOperationClassifier.cs:40-51`; `Summarised` writes `kafka:///`) | READ, PEER | An operation has a `target`, which may be a statement head or a topic; a target a verbosity setting emptied is said so (`capture`) |
| F8 | A run can miss whole sides of the graph: the external-SUT lanes record none of the service's calls | RUN | A `capture` block states it; absence is "not recorded", never "none" |
| F9 | A party's kind is per run (a fake on one lane, an emulator on another, a fake answering for the production host); its id is per codebase | PEER | The card's kinds describe the run, and say how each was known (`kindSource`, #190) |
| F10 | The card is tens of KB, not 8 | RUN (2.3) | Fine for an indexer; `Service.md`, a model's first read, stays FLEET S4's and keeps its budget |
| F11 | Kronikol4J writes its own reports, and their calls do not always reach the data file: its fragment format has no interactions (calls survive only as diagrams), so a merged Kronikol4J report holds no calls; a standalone run writes them only with `kronikol.report.dataFormats=json` (0.1.24) | READ; PEER (#192's 3.9, which tried to make a Java stand-in) | `kronikol card` gives a Java suite a card only from a standalone run with that setting. On a merged run the card is written and says `capture.calls: "notRecorded"`, never an empty graph as a fact. Fragments that carry calls are a Kronikol4J change (Q7). A divergence-ledger line at R2 |
| F12 | `sid:` addresses a scenario, never a call; `sN/iM` depends on order | READ (`Address.cs:46-52`), RUN (2.4) | An example address belongs to the run part |
| F13 | The test framework is recorded nowhere | RUN, READ | `run.framework` is null until a capturer writes it (Q12) |
| F14 | `FixedNameForReceivingService` names the title only | PEER, READ | The subject comes from #190's `ServiceId` or inference, never from this option |

## 4. The design

### 4.0 Who owns what

| Concern | Owner | What this plan needs from it |
|---|---|---|
| `ServiceId`, each party's id, kind (`test`, `underTest`, `real`, `double`, `unknown`) and `kindSource` (`declared`, `observed`, `inferred`); the `serviceId` and `parties` blocks in the data file, written only when something is declared; the inference of the test client and the subject; the per-call `testDouble` mark (its R2) | #190, `PARTY_IDENTITY_PLAN.md`: the resolver §5.1, inference §5.4, the data file block §5.5, `Run.json` §5.8, the mark §5.10, what this plan may rely on §5.12 | The resolver in core (`src/Kronikol/Parties/`, shared by the card, the run's writer, `merge` and `query`) as a pure function of what any report holds, so a card from an old report agrees with one written at run end; the card runs it itself when the block is absent (§5.12). Inferred rows say `inferred`. Rows for every caller and every callee, and no row set that implies completeness (§5.5) |
| The operation key (one function on `InteractionShape`, query keys dropped for the edge key, the GraphQL operation joining it, version 7); `origin` (the caller's role) and `cause` (`test`, `request`, `background`, `unknown`, from `attributionSource`, else nesting); the consumer's templating rules written into the data file; `--group-by caller,origin,route` | #192, `CALL_OPERATION_AND_ORIGIN_PLAN.md`: the key 4.1 (`CallOperation.Of`, with query keys off for the card), GraphQL 4.2, `origin` 4.3 (with the seeding limit), the `shapeTemplates` block 4.5, `cause` its section 9 Q4 | All of them pure functions of what a report holds, in core. `cause` recommended to the owner with this plan's reason (F3) |
| The call graph (parties, edges, operations, per-call parent, origin, cause and effective phase); `service.json`, its schema, its stability and its `capture` block; `kronikol card`; the run-end file and `Run.json`'s key | this plan | |

The order, as `PARTY_IDENTITY_PLAN.md` §6 sets it for all three: #190 R0 (a patch), #190 R1 (the resolver, the
declarations and the block), #192 (whose `origin` reads #190's roles), then R1 here. #190's R2 (`testDouble`) can land
before or after this plan's releases; its fields reach the card as additive keys.

### 4.1 Principles

1. **One writer, two readers.** The card is built from a neutral call record. A running suite maps
   `RequestResponseLog` into it; `kronikol card` maps a scanned report into it. One builder, one writer, as CTRF has.
2. **Presence is behaviour; counts are the run.** The `behaviour` part holds sets. Anything that changes when the same
   code runs again lives in `run`.
3. **Say what was not seen.** A side of the graph with no calls, a target a verbosity setting emptied, a phase that
   was derived: each is stated. Unknown is `null`, inferred is labelled `inferred`, and nothing is guessed silently.
4. **Additive keys do not bump `formatVersion`.** Readers ignore keys they do not know, as `Run.json`'s reader does.

### 4.2 The call graph (core)

A new folder, `src/Kronikol/CallGraph/`:

- **`CallRecord`**, the neutral input: scenario (ordinal, `stableId`, feature, name, tags, source file and line),
  position in the scenario, request or response, caller, service, method, URI, dependency category, caller category,
  meta type, recorded phase, `attributionSource`, `requestResponseId`, Kronikol `traceId`, status code and text, and
  the request body's first line for a statement-shaped category or a GraphQL candidate. Never a body otherwise.
- **`CallGraphBuilder.Build(records, rules)`** returns the graph:
  - **Parent** of each request, by `CallNesting`'s rule. The rule moves from `Query/CallNesting.cs` into this folder,
    taking `CallRecord`; `flow` keeps asking it through a mapping. One rule, as its doc comment requires.
  - **`origin`** and **`cause`**: #192's functions.
  - **Effective phase**: the recorded phase when it is `Setup` or `Action`; else the effective phase of the call it
    ran inside, when there is one; else `unknown`. This gives the service's in-request calls the phase of the test's
    call they served (the 731 of 2.2). Background calls keep the phase they recorded. The card says how many phases
    were derived (`run.counts`), and names the field `effectivePhases`: #192's `--phase` filter reads the recorded
    phase, so for a nested call the two readings differ on purpose, and the names say which is which.
  - **Parties**: every caller and service name, with the set of categories its calls carried (a party's category is
    per record: the service is plain HTTP on most calls and gRPC on nine, PEER), and #190's id, kind and `kindSource`.
  - **Edges**: (caller, service, protocol), the protocol by the component diagram's rule (`GetProtocol`,
    `ComponentDiagramGenerator.cs:531`), shared rather than copied, so the card's edges are the diagram's edges.
  - **Operations**: (edge, method, target), the target by #192's key. Each operation keeps its sets: statuses seen,
    origins, causes, effective phases, scenarios. Counts are kept beside them for the run part.
- **Messages** are operations whose protocol is a message category or whose meta type is `Event`. A call from the
  subject to a broker publishes, unless its method is a consume (Kafka's `Consume`); a call from a broker to the subject
  is a delivery, which the subject consumes. The topic is the target's last segment (`kafka:///orders` → `orders`,
  `event://event-broker/cake_events` → `cake_events`). A target with no segment is a topic the capture did not record.

Nothing in the report changes. The graph is built in memory at run end and by the tool.

### 4.3 `service.json`

One file, two parts. The order of members is fixed; every list is sorted by ordinal comparison on the keys named
below; numbers and statuses are written invariant; the text is UTF-8, LF, no BOM, indented two spaces, written by
`Utf8JsonWriter` with the default encoder as `Run.json` is.

```json
{
  "formatVersion": 1,
  "run": {
    "id": "local:20261025T202804Z:3f2a9c1e",
    "at": "2026-10-25T20:28:04Z",
    "repository": null,
    "commit": null,
    "branch": null,
    "suite": "Example.Api.Tests.Component.ReqNRoll.xUnit3",
    "framework": null,
    "kronikolVersion": "5.N.0",
    "report": { "file": "TestRunReport.krkl", "formatVersion": 3, "sha256": "9c0e…" },
    "results": { "scenarios": 10, "passed": 10, "failed": 0, "skipped": 0 },
    "behaviourHash": "5b1d…",
    "counts": [
      { "op": "1f0c6e2a9b4d7e31", "calls": 4, "statuses": { "200": 3, "400": 1 }, "derivedPhases": 0 }
    ],
    "examples": [ { "op": "1f0c6e2a9b4d7e31", "address": "s0/i8" } ]
  },
  "behaviour": {
    "rules": { "operationKey": 7, "templateRules": null, "inference": 1 },
    "subject": { "name": "Dessert Provider", "id": null, "source": "inferred" },
    "parties": [
      { "name": "Caller", "id": null, "kind": "test", "kindSource": "inferred", "categories": [] },
      { "name": "Cow Service", "id": null, "kind": "unknown", "kindSource": "inferred", "categories": [] },
      { "name": "Dessert Provider", "id": null, "kind": "underTest", "kindSource": "inferred", "categories": [] },
      { "name": "Event broker", "id": null, "kind": "unknown", "kindSource": "inferred", "categories": ["MessageQueue"] }
    ],
    "edges": [
      {
        "from": "Caller", "to": "Dessert Provider", "protocol": "HTTP",
        "operations": [
          { "op": "1f0c6e2a9b4d7e31", "method": "POST", "target": "/cake", "graphqlOperation": null,
            "statuses": ["200", "400"], "origins": ["test"], "causes": ["test"], "effectivePhases": ["action"] }
        ]
      },
      {
        "from": "Dessert Provider", "to": "Cow Service", "protocol": "HTTP",
        "operations": [
          { "op": "7a93…", "method": "GET", "target": "/milk", "graphqlOperation": null,
            "statuses": ["200"], "origins": ["subject"], "causes": ["request"], "effectivePhases": ["setup"] }
        ]
      }
    ],
    "exposes": { "basis": "tested", "ops": ["1f0c6e2a9b4d7e31"] },
    "messages": [ { "op": "c41e…", "direction": "publishes", "party": "Event broker", "topic": "cake_events" } ],
    "scenarios": [
      { "sid": "6fb32362a4f528b0", "feature": "Cake", "scenario": "Calling Create Cake Endpoint Without Egg",
        "tags": [], "source": { "file": "Features/Cake.feature", "line": 13 }, "ops": ["1f0c6e2a9b4d7e31", "7a93…"] }
    ],
    "unresolved": ["Caller", "Cow Service", "Dessert Provider", "Event broker"],
    "capture": { "calls": "recorded", "subjectCalls": "recorded", "emptyTargets": [], "degraded": [] }
  }
}
```

(Illustrative: the values are the Example.Api Reqnroll run's, the hashes cut; the excerpt shows one operation per
edge.)

**`behaviour`**, byte-identical whenever the run's behaviour is:

| Member | Holds | Sorted by |
|---|---|---|
| `rules` | the operation key's version, the hash of the consumer's templating rules (null for none), the inference rule's version: a card made under other rules is not comparable, as history already says of shapes | |
| `subject` | name, id, `source` (`declared` or `inferred`) (#190) | |
| `parties` | name, id, kind, `kindSource` and `note` as #190's resolver gives them (§5.1), and the set of categories seen. A party some of whose calls carry #190's `testDouble` mark keeps its kind (§5.10); how many of its calls were marked is a count, so `doubleCalls` goes to `run` | name |
| `edges` | from, to, protocol, and the operations | from, to, protocol |
| an operation | `op` (the first 16 hex of SHA-256 over from, to, protocol, method and target, joined by U+001F), method, target, `graphqlOperation` (#192 §4.2, which names an anonymous operation by its root fields, `query { orderSummaries }`: all 8 of BreakfastProvider's GraphQL POSTs are anonymous, and `TryExtractLabel` reads each as `query`, PEER), and the sets of statuses, origins, causes and effective phases | method, target |
| `exposes` | the operations received by the subject that are not deliveries; `basis: "tested"` says they are the tested surface, not the service's (FLEET F1). S3's coverage block joins it later | `op` |
| `messages` | `op`, direction, party, topic | party, topic, direction |
| `scenarios` | `sid`, feature, scenario, tags, source (`file`, `line`) or null, the `op`s it touched. Rows sharing a `sid` are one row | `sid` |
| `unresolved` | party names with no id | name |
| `capture` | `calls` (`recorded`, or `notRecorded` when the data file holds no call at all, as a merged Kronikol4J report does, F11), `subjectCalls` (`recorded`, or `notRecorded` when no call made by the subject is in the run), `emptyTargets` (the `op`s whose target the capture left empty), `degraded` (the kinds of degraded-capture diagnostics the report carries) | |

Operation ids instead of positions keep a change local: a new operation adds one id and moves no other reference.

**`run`**, the facts of this run:

| Member | Holds |
|---|---|
| `id`, `at`, `suite` | as `Run.json` writes them |
| `repository`, `commit`, `branch` | from the report's `ciMetadata`; null off CI |
| `framework` | null until a capturer records it (F13, Q12) |
| `kronikolVersion` | of the build that wrote the card, which for `kronikol card` may differ from the report's |
| `report` | the data file's name, its `formatVersion` and the SHA-256 of its bytes |
| `results` | scenarios, passed, failed, skipped; scenarios that are not tests are not counted (as `Run.json`) |
| `behaviourHash` | SHA-256 of the `behaviour` member's compact UTF-8 bytes |
| `counts` | per `op`: calls, calls by status, phases derived through nesting; per party: `doubleCalls`, when #190's mark is on some but not all of its calls |
| `examples` | per `op`: the first call's address in report order, `sN/iM` |

So the file as a whole changes on every run, as `Run.json` does, and `behaviour` changes only when the behaviour does.
On BreakfastProvider's history that is about 3% of re-runs of the same code for the operation set (2.4). The scenario
index changes more often: a scenario's call set changed in 12.4% of pairs, and part of that is background work landing
in whichever scenario was running. Whether background calls belong in a scenario's `ops` is Q6, measured before R1.

### 4.4 `kronikol card`

```
kronikol card <report> [-o <file>]
```

- A tool command beside `kronikol ctrf`, not a `query` verb, for `ctrf`'s reason: a budget would truncate a document.
- `<report>` is a `TestRunReport.krkl` or `TestRunReport.json` of any version the scanner reads, or a directory
  holding one. The scan maps into `CallRecord`, reading only the request bodies the target and the GraphQL rule need,
  through the index's offsets.
- Writes to stdout without `-o`. Exit codes as the tool's: 0, 1 for a runtime failure, 2 for a usage error.
- A report without #190's block gets inferred parties; one without #192's rules block gets the built-in rules,
  `rules.templateRules: null`. Both say so in the card itself.
- One pass: on BreakfastProvider's largest lane a scan costs 0.3 s (RUN); a card adds the graph, linear in calls.

### 4.5 At run end

- **`ReportConfigurationOptions.GenerateServiceCard`** (Q2 for the default). When on, the run-end list gains
  `Add("service.json", ...)` beside the failures digest. Ingested runs get it the same way (2.5).
- **`Run.json`** lists it in `files` as every output, and gains one additive key, `serviceCard`:
  `{ "file": "service.json", "behaviourHash": "…" }`. #190 R1 adds `serviceId` beside it (`PARTY_IDENTITY_PLAN.md`
  §5.8), so an indexer that fetches `Run.json` first learns which service the run is about and can skip a run whose
  behaviour it has already seen. `runManifestVersion` stays 1.
- The file name follows the report's: `service.json` beside `TestRunReport.*`, and under a custom
  `HtmlTestRunReportFileName` the same prefix rule the other outputs use (Q11).

### 4.6 The schema

- `schemas/service-card.v1.schema.json`, committed, with `$id` its raw URL on `main`. The issue asks for a fixed path in
  the repo; this is the first schema committed, so the folder is new (Q13).
- Closed for the members the writer emits, open for additions under the rule of 4.1.
- Two facts hold it: every card the tests write validates, and every member the writer can emit is in the schema
  (the `SchemaClosedContractTests` pattern).

### 4.7 Not in this plan

- `Service.md`, coverage against an OpenAPI or AsyncAPI spec, the fleet index and addresses across reports (FLEET S3 to
  S6). `exposes` keeps a place for S3's block.
- A `graph` verb (FLEET S2). Once the call graph exists it is a projection of a few dozen lines; asked separately (Q9).
- Moving the component diagram, `services` or history onto the call graph. Each would change output; the card pins
  the diagram's edges against the graph instead (5.1), and the rest is Q10.
- Propagating the phase in the identity header. Nesting repairs it for the card; a capture fix is a separate issue
  (Q8).

## 5. Tests, red first

Every fact goes through a public entry point (`kronikol card` through the tool's `Commands`, the run end through
`ReportGenerator`), so the same files compile on 5.11.0 and the red proof is literal.

### 5.1 The call graph (R1)

1. **Origin, cause and phase on the shapes that matter**, from hand-written records: the test calls the subject, which
   calls a dependency inside that request; the test seeds a database with its own client; the test seeds through the
   subject's own client between requests; the subject's background job calls a dependency with no test in the stack; a
   broker delivers to the subject. Each call's origin and cause as #192 defines them, and effective phase: the
   dependency call inside the request takes the request's phase, the background call keeps its own.
2. **Parity with the component diagram**: over every report the test suites write, the card's edge set equals
   `ExtractRelationships`' (caller, service, protocol) set. A mutation of either rule fails it.
3. **Parity with history**: each operation's target equals the key #192's function gives for the same call (one key,
   not two).
4. **`flow` unchanged**: the existing `flow` facts pass with `CallNesting` moved.

### 5.2 `service.json` (R1)

1. **Stability**: two record sets that differ only in call counts, call order, durations and run id give
   byte-identical `behaviour` and equal `behaviourHash`; `run` differs. Then the same with the records shuffled
   within each scenario as parallel steps would.
2. **Change is local**: one new operation adds one `op` and changes no other line of `behaviour`.
3. **Culture**: the same card under `cs-CZ`, `da-DK` and `tr-TR` (`CultureRun.Under`), with names that sort differently
   there (`ch`, capitals, `I`).
4. **The issue's fixture**: a declared double, a declared real party, a test client seeding a database, GraphQL POSTs
   with two operations, `/orders/123` and `/orders/456`. Expect: each party's kind; the seeding with origin `test`;
   the subject's calls with origin `subject`; two GraphQL operations; one operation `/orders/{n}`. Then the same
   fixture seeding through the subject's client: the seeding has origin `subject` and a cause other than `request`.
5. **Not recorded is not none**: an external-SUT shaped run gives `capture.subjectCalls: "notRecorded"`; a data file
   with scenarios and no calls, the shape of a merged Kronikol4J report (F11), gives `capture.calls: "notRecorded"`; a
   Kafka call at `Summarised` verbosity lands in `emptyTargets`.
6. **Null, never guessed**: `framework` null; `source` null on a scenario with none; `id` null and `kindSource`
   `inferred` on a report without declarations.
7. **Schema**: every card written by the facts above validates; the closed-contract fact.
8. **Size**: a card on a 2,000-scenario synthetic report is written in one scan, under a time set from R1's first
   measurement.

### 5.3 `kronikol card` (R1)

1. From a 4.x `TestRunReport.json` and from a 5.x `.krkl` of the same run (fixtures kept in the tests, written by the
   old writers): equal `behaviour`.
2. One writer: for a run made in the tests, `kronikol card` on its data file gives the same `behaviour` as the graph
   built from the run's own logs (R2 makes this the run-end file).
3. Usage: no report, an unreadable file, `-o` to a directory that does not exist; exit codes 2, 1, 1.

### 5.4 The run end (R2)

1. With the option on, `service.json` is written and listed in `Run.json`, and `serviceCard.behaviourHash` equals the
   file's. With it off, neither.
2. A write that fails leaves the file out of `Run.json` (`StaleOutputTests`' rule).
3. An ingested run writes it (`kronikol ingest` over an NDJSON fixture).
4. The run-end file's `behaviour` equals `kronikol card`'s on the same run's data file (5.3.2 against the real file).

No Playwright: nothing here changes the HTML report.

### 5.5 Acceptance, on real reports

- On BreakfastProvider's four lanes, after R2: the run-end file's `behaviour` equals `kronikol card`'s on the same
  data file, and the external-SUT lane says `notRecorded`.
- On two runs of one lane of the same commit (a re-run of CI or of the lane locally): `behaviourHash` equal, or the
  difference is one of the operations section 2.4 names.
- On the ingested runs of A3, kronikol-bf's Jest stand-in and a Cucumber Messages run built in R1's tests: the card is
  written. `effectivePhases` reports what was recorded: unknown throughout on the stand-in, which records no phase, and
  Action throughout on a run recorded as the issue's Jest report was. Where the stream names the service as the caller
  of its own calls, as the stand-in does, `origin` separates them from the test's; where it names the test for every
  call, the card says so through `capture.subjectCalls: "notRecorded"` rather than listing the service's dependencies
  as the test's.
- Run cold, the indexer's question: from `Run.json` and `service.json` alone, list BreakfastProvider's dependencies
  used while serving requests, and those used only in the background. Compared by hand with `flow` on five scenarios.

## 6. Releases and records

### 6.1 Releases

| Release | Holds | Bump | Waits for |
|---|---|---|---|
| **R1** | `CallGraph/` (with `CallNesting` moved), the card writer, `kronikol card`, the committed schema, docs | **minor**: a new command and a new file format | #190 R1 (the resolver, `PARTY_IDENTITY_PLAN.md` §5.1 to §5.5); #192 (the key, `origin` and `cause`) |
| **R2** | `GenerateServiceCard`, the run-end file, `Run.json`'s `serviceCard` key | **minor**: a new option and a new output | R1 |

R1 changes no report bytes and no golden pin. R2 adds a file; the report itself is unchanged. If the owner wants the
run-end file in the first release, R1 and R2 merge into one minor.

### 6.2 Changelog drafts

- **R1**: "`kronikol card <report>` writes `service.json`: the run's parties, the edges between them with each
  operation's method, templated target, statuses, origins, causes and effective phases, the scenarios that exercised each, the
  messages published and consumed, and what the capture could not see. Its `behaviour` part is byte-identical when the
  run's behaviour is, and its SHA-256 is `behaviourHash`; counts and example addresses are in `run`. Works on any
  report the tool reads, 4.x JSON or 5.x `.krkl`. The schema is `schemas/service-card.v1.schema.json`. A minor (new
  command)."
- **R2**: "`GenerateServiceCard` writes `service.json` at run end, listed in `Run.json`, which gains
  `serviceCard.behaviourHash`. A minor (a new option and output)."

### 6.3 Wiki (`../Kronikol.wiki`, `origin/master` at `2f165c5`)

- A new `Service-Card` page: what the file holds, what is stable, what `origin`, `cause` and `capture` mean, how an
  indexer uses `behaviourHash`, what an inferred row means.
- `Generated-Reports.md`: a row in the file table (beside `Failures.md`, L16, and `Run.json`, L23) and `Run.json`'s new
  key.
- `Querying-Reports.md`: `kronikol card` beside `kronikol ctrf`.
- `Report-Configuration.md`: `GenerateServiceCard` (R2).
- `Cross-Run-History.md`: `HistoryShapeTemplates` now also shapes the card's targets (after #192).
- `_Sidebar.md` and `Home.md`: the new page.
- Grep the wiki for every page that lists the run-end files or the tool's commands before each release.

### 6.4 Plans, roadmap, Kronikol4J, skill

- `PLANS_STATUS.md`: this plan's row.
- `ROADMAP.md`: the card is row 14.13 (FLEET S4) and D23 is still open. #191 asks for part of S4 now, for the owner's
  own company, which is D23's question; placing R1 and R2 is Q1.
- `FLEET_EVIDENCE_PLAN.md` §4.4: point S4's `service.json` here; its 8 KB is `Service.md`'s budget, not this file's.
- Kronikol4J (`docs/REMAINING_PARITY.md`): R1 has no Java form (a tool command). R2 adds a run-end file the port does not
  write: one line, "not mirrored; a Java run's card comes from `kronikol card`" (Q7).
- Both skill copies (`references/commands.md`): `kronikol card`.

## 7. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Where in the roadmap: now, for the owner's company (D23's "sooner"), or after the launch as 14.13 | Now, R1 first: it is tool-only, changes no report and no pin, and serves every report already written. R2 when the owner wants the file at run end |
| Q2 | `GenerateServiceCard` on by default | On, like `Failures.md` and as D23 recommends for the card: an indexer is only as current as the runs that write the file. The cost is one file of 10 to 80 KB per run on BreakfastProvider (RUN) |
| Q3 | One file with `run` and `behaviour`, or two files | One file. The issue asks for one, the indexer compares `behaviourHash`, and `jq .behaviour` gives the stable bytes. Two files would let a git branch of cards diff cleanly, which FLEET S5 may want later |
| Q4 | Counts in the card at all | Yes, in `run`. They are the issue's ask and cost the stable part nothing there |
| Q5 | `cause` on the card (#192 asks it too, its section 9 Q4, with this card as its second reason) | Yes. Without it the card cannot say whether a dependency is used while serving requests or only in the background, and seeding through the service's own clients reads as the service's |
| Q6 | Background calls in a scenario's `ops` | Measure first: the share of the scenario index's churn that background calls cause, on two runs of one lane. Leave them out if it is most of it; the operation stays on its edge either way |
| Q7 | Kronikol4J: port the writer, or `kronikol card` in Java CI | The tool, until a port asks: one implementation keeps one format. It needs the Java run's calls in its data file (F11), so a merged Kronikol4J run gets a card only once its fragments carry interactions; recommend that as a Kronikol4J issue of its own, raised when the card ships |
| Q8 | Fix phase at capture (carry it in the identity header) | Not here. Nesting repairs it for every reader of the graph. A capture fix changes the wire and every port; a separate issue if wanted |
| Q9 | A `query graph` verb now | Not in this plan. Cheap once the graph exists; ask when an agent needs it (FLEET S2) |
| Q10 | Move the component diagram, `services` and history onto the graph | No: each changes output. The card pins the diagram's edges; the others stay as they are until a defect makes the move worth a pin change |
| Q11 | The file's name under a custom `HtmlTestRunReportFileName` | `service.json` always: an indexer looks for one name. A suite that writes two reports into one folder is already a problem `Run.json` has |
| Q12 | `run.framework` | Null now. It needs each capturer to record its framework, which is the contract question at 14.5 (`GO_PLATFORM_PLAN`'s ask of the raw test name with its framework). Record it there |
| Q13 | Where committed schemas live | `schemas/` at the repo root, one file per format version, raw URL as `$id` |
| Q14 | The command's name | `kronikol card`, the issue's. It is short and pairs with `ctrf`; the wiki page says "service card" in full |

## 8. Assumption ledger

| # | Statement | Basis | If wrong |
|---|---|---|---|
| A1 | BreakfastProvider's lanes are representative of in-process .NET suites | RUN on one consumer | The rates in 2.4 move; the design does not, since it promises nothing for counts |
| A2 | The service's calls outside any received call are seeding in suites that seed through the container | INFERRED from 2.2; not observed on BreakfastProvider | `cause` still says what is known; 5.2.4's second fixture pins what the card prints |
| A3 | The three reports in the issue behave like BreakfastProvider's lanes | ISSUE. Searched for on 2026-10-27 and not found: every file under `C:/Users/cex` (PEER, the list in #192's session scratchpad, `found-reports-profile.txt`), C: (the only drive), `C:/Code` outside Kronikol's worktrees; the session transcripts show the issues were written on another machine. Stand-ins measured instead (2.3): four more BreakfastProvider lanes and a Jest ingest (RUN on PEER-made inputs). A Kronikol4J stand-in could not be made with calls (F11) | R1's acceptance adds, in its own tests, a Jest-shaped run as the issue describes it (one synthetic `Run:` step, every call Action, MSW-style doubles answering in 1 to 2 ms) and a Cucumber Messages run, both from hand-written NDJSON through `kronikol ingest` as #184's repro was, and pins them like the BreakfastProvider lanes |
| A4 | #192's key and cause, and #190's inference, are pure functions of what a report holds | PEER, agreed by message | R1 waits, or the card gets a field that differs between run end and backfill; 5.3.2 catches it |
| A5 | `ReportScanner` can give the card every field it needs, `attributionSource` included | READ: it does not read `attributionSource` today (PEER) | R1 adds it to the scan, as #192 needs too |
| A6 | 16 hex of SHA-256 is enough for an operation id | INFERRED: about 200 operations a run | A collision would merge two operations; a fact over the corpus checks there is none |
| A7 | Ingested runs reach the run-end list | READ (`IngestPipeline.cs:694`) | 5.4.3 catches it |
| A8 | Card sizes scale with scenarios and operations as measured | RUN on 212 scenarios | Q2's cost line moves; 5.2.8 measures a large run |

## 9. Log

- 2026-10-26: written (session kronikol-ed), with kronikol-67 (#190) and kronikol-bf (#192) by message. Probes and
  results in the harness. The split in 4.0 and the facts marked PEER came from those messages; each peer cites this
  plan's harness for the numbers in 2.2 to 2.4.
- 2026-10-27: `PARTY_IDENTITY_PLAN.md` written; its sections cited in 4.0, 4.3, 4.5 and 6.1, and its order across the
  three plans taken (#190 R0, #190 R1, #192, #191). Its `testDouble` mark (§5.10) reaches the card as party fields and
  a `doubleCalls` count in `run`.
- 2026-10-27: `CALL_OPERATION_AND_ORIGIN_PLAN.md` drafted; its sections cited in 4.0, 4.3 and Q5. Taken from it: the
  GraphQL rule for anonymous operations (all 8 of BreakfastProvider's GraphQL POSTs are anonymous), and the field
  renamed `effectivePhases` so it is not read as the recorded phase `--phase` filters. Its measurement that
  `attributionSource` alone gives the same split as nesting on both in-memory lanes is cited in 2.3.
- 2026-10-27: the issue's three reports searched for and not found (A3); kronikol-bf's stand-ins measured with the
  card prototype (2.3, `results/card-prototype-stand-ins.txt`): four more in-memory lanes, 755 calls inside received
  calls on every one, and a Jest ingest that nests with no `attributionSource`. Kronikol4J's merged reports hold no
  calls (F11): `capture.calls` added, Q7 and the acceptance updated.

## Appendix A. Edit sites at `44bcc62a`

| Release | File | Change |
|---|---|---|
| R1 | `src/Kronikol/CallGraph/` (new) | `CallRecord`, `CallGraph`, `CallGraphBuilder`, the nesting rule moved here |
| R1 | `src/Kronikol/Query/CallNesting.cs` | delegates to the moved rule |
| R1 | `src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs:531` | `GetProtocol` shared with the graph, not copied |
| R1 | `src/Kronikol/Reports/ServiceCardWriter.cs` (new) | the one writer |
| R1 | `src/Kronikol/Query/ReportScanner.cs` | reads `attributionSource` (if #192 has not) |
| R1 | `src/Kronikol.Tool/CardCommand.cs` (new), `src/Kronikol.Tool/Commands.cs` | the command, beside `ctrf` |
| R1 | `schemas/service-card.v1.schema.json` (new) | the schema |
| R1 | both skill copies, `references/commands.md` | `kronikol card` |
| R2 | `src/Kronikol/ReportConfigurationOptions.cs` | `GenerateServiceCard` |
| R2 | `src/Kronikol/Reports/ReportGenerator.cs:594` (beside the digest) | `Add("service.json", …)` |
| R2 | `src/Kronikol/Reports/RunManifest.cs:79` | the `serviceCard` member |
