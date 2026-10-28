# The service card: one call graph, and `service.json` from any run (#191)

**Date:** 2026-10-26, audited and revised 2026-10-27 · **Repo version:** written against 5.11.0 (`origin/main` at
`44bcc62a`); a READ citation is at that commit unless it says `at 8d7d1e1d` (5.14.0, the base this branch is rebased
on), and Appendix A is at `8d7d1e1d` · **Status:** ❌ plan written, nothing implemented, **NOT green-lit**. Written at
the owner's request of 2026-10-26: analyse #191 critically, then plan a fix for the underlying problem, which may not be
the fix the issue proposes. Audited on 2026-10-27 by three reviews and revised to them (section 9).

Two sibling plans were written the same day, by agreement, so that the three issues get one design rather than three:

- `PARTY_IDENTITY_PLAN.md` (#190, session kronikol-67): the subject's identity, each party's id, kind and `kindSource`,
  and the rule that infers the test client and the subject when nothing is declared. Its R2 (planned as 5.15.0) adds the
  per-call `testDouble` mark and changes that rule; its R3 (planned as 5.16.0) indexes the data file's root background
  calls apart from the scenarios' and resolves parties at run scope over both (PEER, agreed by message on 2026-10-27).
- `CALL_OPERATION_AND_ORIGIN_PLAN.md` (#192, session kronikol-bf): the one key that says what operation a call is
  (built on `InteractionShape`), `origin`, `cause` (its open Q4), and the `--group-by` dimensions.

This plan cites them for those rules and does not restate them. Section 4.0 has the split. `PARTY_IDENTITY_PLAN.md`
has been on `main` since 5.13.0 (`2b74a2e4`), which shipped its R0, and 5.14.0 (`8d7d1e1d`) shipped its R1; its R2 and
R3 as cited here are its session's plan, not yet on `main`. `CALL_OPERATION_AND_ORIGIN_PLAN.md` lives in its session's
worktree (`C:/Code/Kronikol-192`), not on `main`.

Evidence labels:

- **RUN** means measured with the probes in [`SERVICE_CARD_PLAN.harness/`](SERVICE_CARD_PLAN.harness/README.md), on
  Windows 11 26200 with .NET 10: on 2026-10-26 with the 5.11.0 tool built from `44bcc62a`, and on 2026-10-27 with the
  5.14.0 tool and source (`8d7d1e1d`) by this plan's audit (files named `audit-A-`, `audit-B-` and `audit-C-`) and its
  revision (files named `revision-`). The inputs were:
  - BreakfastProvider, the reference consumer: CI run 40290999167 on `cf47045` (Kronikol 5.4.1), four lanes' data
    files (`xunit-in-memory`, `reqnroll-in-memory`, `xunit-external-sut`, `reqnroll-in-docker`), fetched with
    `gh run download` and decoded with `kronikol decode`; from 2026-10-27 also all 18 lanes of runs 40473765203
    (`cf47045` again, a day later) and 40491266394 (`68a8760`, Kronikol 5.12.1), and its `xunit-in-memory` and
    `reqnroll-in-memory` lanes run twice on this machine at `68a8760` (`probes/audit-B-download.sh`,
    `audit-B-local-lane-twice.sh`);
  - its history ledger, `history.jsonl` on its `kronikol-history` branch: 1,599 run lines from 18 suites;
  - the three Example.Api component reports (5.8.0), copied into `inputs/examples-5.8.0/`;
  - stand-ins kronikol-bf made on 2026-10-27 for the stacks the issue was measured on (A3): the same CI run's
    `nunit`, `tunit`, `lightbdd` and `bddfy` in-memory lanes, and a Jest ingest (5.11.0, from
    `INGEST_FIDELITY_PLAN.harness/otel-jest` through the wiki's recipe), kept in its plan's harness.
- **READ** means Kronikol's source at `44bcc62a`, or at `8d7d1e1d` where the citation says so.
- **PEER** means measured or read by a sibling plan's session, or planned by it and agreed by message; the script is
  named where there is one.
- **ISSUE** means the issue's own numbers, not measured again: the three reports it used are not on this machine
  (searched on 2026-10-27, A3).
- **INFERRED** means reasoned from two facts above and stated by neither.

## 0. Summary

- **The issue names two problems, and only one of them is about a file.**
  - *Access.* An indexer has to decode every report and rebuild the edges itself. True, but cheap to fix: one pass
    over a report costs about what one query costs (PEER: 0.25 to 0.33 s a query on four of BreakfastProvider's lanes,
    process start included, after a cold first call of 1.6 s, #192's `results/measure/_cli-timings.txt`; ISSUE: 0.75 s
    on a 198 MB report).
  - *Meaning.* The indexer "can't tell the service's calls from the test's". This is the real problem, and a file
    does not solve it. A file only moves the guess from the indexer into Kronikol, and if Kronikol guesses from the
    names on the calls, it is wrong in the same places.
- **The underlying problem: Kronikol has no single model of who called whom, and why.**
  - It derives "who calls what" five times, with four rules for what one call is: the component diagram's
    relationships, history's shapes and `deps`, the warm-up's shapes, `query services` and `--group-by` (READ, PEER,
    section 2.1). None publishes who calls whom as data in the standard report. The issue's `service.json`, with a
    templating rule of its own, would be the sixth.
  - A call records who made it, never why. The caller's name is a static option of each tracker's registration
    (READ), so a call the test makes through the service's own clients carries the service's name. The query engine
    already works out which call each call ran inside (`CallNesting`), but only to indent `flow`.
  - A report never says what it could not see. On BreakfastProvider's external-SUT lanes none of the service's own
    calls are captured (RUN: one edge, `Caller>Breakfast Provider`), and Kronikol4J's runs mostly leave no calls in a
    data file: its merge writes no data file at all, and a standalone run writes its calls only when asked (F11). A
    summary of such a run lists a service with no dependencies, and that reads as a fact.
- **What the measurements change in the issue's design.**
  1. **The service's own calls are not one thing.** On three lanes, 755, 731 and 665 of them ran inside a call the
     service received; 86, 68 and 151 ran outside any. On BreakfastProvider the second group is background work: the
     outbox poller, gRPC notifications, Cosmos DB housekeeping, BigQuery and ClickHouse inserts. Those are real
     dependencies. In a suite that seeds data through `factory.Services`, the same position holds the seeding (RUN,
     INFERRED). #192 makes `origin` the caller's role and offers `cause` (test, request, background, unknown) as its
     Q4, recommended for its R3. `cause` tells the calls made while serving a request from the rest; no recorded signal
     tells the service's background work from a test seeding through the service's own clients (80 of the 86 calls
     outside any request on `xunit-in-memory` are attributed `TestContext`, PEER). The card carries both, and keeps
     the classification it needs itself if #192 declines `cause` (4.2).
  2. **Phase cannot break the tie, but nesting can repair it.** On the Reqnroll lane every call the service made
     inside a received request has no phase, 731 of 731. The identity reaches the service in a request header
     (`attributionSource: RequestHeader`), and the header carries the test's id but not its phase (RUN, PEER). The
     phase of the call they ran inside is known. A background call keeps the phase it recorded, as a rule: one was
     nested under an unrelated request by the order it was recorded in (RUN, 2.4).
  3. **"Byte-identical for the same behaviour" contradicts the issue's own fields, and holds for presence.** Over
     1,005 pairs of consecutive runs of one suite on one commit (RUN, the ledger): some scenario's call count changed
     in 37.6% of the pairs, some call order in 79.1%, the run's set of distinct operations in 2.9% (of the 933 pairs
     that carry call lists), its edges in 0.4%, and the edges never where the scenarios' results were the same. The
     card itself, on ten pairs of runs of one lane on one commit: its `behaviour` is byte-identical on 10 of 10 as
     revised (4.3), against 6 of 10 as first written, and its counts moved on 6 of the 10 (RUN, 2.4).
  4. **The templating rule already exists.** `InteractionShape.Template`, version 6 here (7 since 5.13.0, for a change
     in what the HTTP handler records, not in the templating), folds GUIDs, hex ids, ULIDs, cache-key bytes,
     timestamps and numbers, keeps query keys and drops their values, applies the consumer's `HistoryShapeTemplates`
     first, and gives statement-shaped calls (SQL, Cosmos DB, MongoDB) their templated statement head. History and the
     warm-up already compare calls by it. `/orders/123` becomes `/orders/{n}`, not `/orders/{id}`. On
     BreakfastProvider it leaves 175 operations where raw paths make 207 buckets (RUN); #192's key gives 179 on the
     same lane (GraphQL operations split, query keys dropped), and 184 with its two head fixes, which split the
     queries Cosmos DB and BigQuery send as documents; that lane has no batch behind a `SET` prologue (PEER, #192's
     `results/route/_summary.txt`). #192 owns the one shared key built on it.
  5. **Most parties have no path, and many have no host.** Most of BreakfastProvider's 18 parties are databases and
     brokers. On its in-memory lanes 7 of 16 called parties have no host in any call's URI (both SQL Server databases,
     Pub/Sub, Kafka, MongoDB, the gRPC notification service, Spanner) and Event Grid has only the placeholder
     `topics`; in docker, 6 of 17 and the same placeholder (PEER, `hosts.py`). An operation is a method and a
     templated target: a path, a statement head or a topic.
  6. **It is not an 8 KB file.** In the revised shape, on BreakfastProvider's in-process lanes (187 to 214 scenarios,
     168 to 180 operations), the whole file is 159 to 178 KB written compact and 23 to 25 KB gzipped; `behaviour`
     without its scenario index (edges, operations, parties, `exposes`, `messages`) is 49 to 54 KB (RUN, 2.3). That is
     fine for an indexer. A model's first read is FLEET's `Service.md`, which this plan does not build.
- **The plan.**
  - **One call graph, in core** (4.2): parties, edges, operations, and for each call its parent, `origin`, `cause` and
    effective phase. It is built from a neutral call record that both a running suite and any report Kronikol can
    read map into, the way `kronikol ctrf` already serves CTRF from one writer and two readers.
  - **`service.json` is a projection of it** (4.3), in two parts. `behaviour` holds sets only, in a total order, and
    is byte-identical when the behaviour is; its SHA-256 is `behaviourHash`, over the bytes the file holds. `run` holds
    the per-run facts: counts, errors, example addresses, hosts, and every operation each scenario reached. A `capture`
    block says what the run could not see. The card is a function of the data file alone: what a run observed and did
    not write never reaches it (Q15).
  - **`kronikol card <report>`** writes the same file from any report, 4.x JSON or 5.x `.krkl`, through the same
    writer (4.4).
  - **At run end** the file is written after the outputs, once the data file is on disk, behind `GenerateServiceCard`,
    and `Run.json` lists it and carries its hash (4.5). Ingested runs get it when the option is on by default, merged
    runs from `kronikol merge`, and a Kronikol4J run through the tool, from a standalone run that writes its calls
    (F11).
- **Releases.** R1, a minor: the call graph, `kronikol card` and the committed JSON Schema, with the one function that
  resolves parties from a report and the inference's version; no report byte changes. R2, a minor: the run-end file,
  its option, `Run.json`'s new key and the merge's card. The order across the three plans: #190 R0 (5.13.0), R1
  (5.14.0), R2 and R3, then #192 (R1 to R3), then this plan, since the card needs #190's resolver and its background
  calls at run scope, and #192's operation key, `origin` and `cause`. Seventeen questions are in section 7; Q6 is
  answered by measurement.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | One 781-scenario report is a 6.7 MB `.krkl`, or 198 MB as JSON | **Plausible.** BreakfastProvider's 212- and 214-scenario lanes are 136 to 388 KB on disk and 2.1 to 14.1 MB as JSON. The largest ratio is 36 to 1 (`reqnroll-in-docker`); the issue's is 30 to 1. | ISSUE; RUN (`results/revision-report-sizes.txt`) |
| 2 | `interactions --json` has no caller (#184) | **True** at 5.11.0 and 5.14.0. The item carries `service` and no caller (`QueryCommand.Payloads.cs:131`). #192's R2 adds it, #184's third ask. | READ, PEER |
| 3 | `--group-by` has no caller or origin dimension | **True.** Nine dimensions (`QueryCommand.GroupBy.cs:14-15`). | READ |
| 4 | Paths aren't templated | **True of `--group-by`**, which groups by the raw `AbsolutePath` (L117-123). **False of Kronikol**: history and the warm-up template every call (`InteractionShape.cs:102`). | READ |
| 5 | Rebuilding the edges means running `flow` per scenario, or `decode` plus custom parsing | **True for a reader outside Kronikol.** Inside, every run end builds the edges for the component diagram (`ComponentDiagramGenerator.cs:54`) and writes them as data only into the mergeable data file (`ReportGenerator.cs:5780`), which is off by default. | READ, PEER |
| 6 | Every query decodes the whole file, about 0.75 s each | **True**: each invocation scans the report (`QueryCommand.cs:172`). 0.25 to 0.33 s a query on four of BreakfastProvider's lanes, process start included, after a cold first call of 1.6 s; the largest lane, `reqnroll-in-docker`, was not timed. It costs a reader that asks many questions of each report; a card is one pass. | ISSUE; READ; PEER (#192's `results/measure/_cli-timings.txt`) |
| 7 | That report is 100 pages of `interactions --json` | **Plausible.** The listing pages at 120 calls (`QueryCommand.Payloads.cs:109`). | ISSUE; READ |
| 8 | No run-end file an indexer can fetch beside `Run.json` | **True.** `Run.json` is the natural first fetch: written last, it names the run, the suite, the counts and every file that reached disk (`RunManifest.cs:27`). | READ |
| 9 | One report marks the test's seeding as Setup and leaves the service's downstream calls with no phase | **Reproduced and explained.** On the Reqnroll lane the service's calls inside a received request have phase `Unknown`, 731 of 731 (RUN), all attributed by `RequestHeader`. The test's own calls, attributed by `TestContext`, have theirs (Setup 227, Action 216) (PEER, #192's `results/measure/bp-ci-reqnroll-in-memory.txt`, section 9). | RUN, PEER |
| 10 | Jest marks every call Action, under one synthetic `Run:` step | **Not measured on the issue's report.** The stand-in made with the wiki's recipe records no phase on its 8 calls (2.3). Ingest gives Action only to a step whose keyword or keyword type is When, Then, Action or Outcome; a step of another keyword keeps the previous step's phase, none on a test's first (`IngestAttribution.cs:448-453`, `:480`). So the issue's report must have sent its `Run:` step as one of those. | ISSUE; RUN; READ |
| 11 | `origin` works even where phase is missing | **Half true.** As the caller's role it works where the test acts with a client of its own. It cannot separate a test that acts through the service's own clients, because caller names are static per registration (2.2). `cause` covers part of that; no recorded signal covers the rest (#192). | READ, RUN, PEER |
| 12 | Two runs with the same behaviour can produce byte-identical files | **False for the fields the issue lists, true for presence.** Over 1,005 same-commit run pairs: counts 37.6%, order 79.1%, the operation set 2.9% of the 933 pairs that carry call lists, edges 0.4% and none where the scenarios' results were the same. The card's `behaviour` as revised: byte-identical on 10 of 10 same-commit lane pairs (2.4). | RUN |
| 13 | `kronikol card` can write the file from a 4.x JSON report and from a 5.x `.krkl` | **Feasible.** `ReportScanner` reads both, as `kronikol ctrf` does (`CtrfCommand.cs:80`), and has read `attributionSource` since 5.14.0; the root background block it reads after #190's R3. Without declarations in the file, the card infers every party and says so. No run is written by both a 4.x and a 5.x writer, so the fixtures are per writer (5.3.1). | READ, PEER |
| 14 | Source file and line "when the run has it" | **Reqnroll lanes, the Jest stand-in and Example.Api's Reqnroll report: every scenario** (214 of 214 on Reqnroll, feature `sourceFile` too). **xUnit, NUnit and TUnit lanes: none** (0 of 212). LightBDD and BDDfy: none at the scenario; a step's bare file name on 179 of 187 and 211 of 212, which is no path and which the card does not take. | RUN (`results/audit-C-source-locations.txt`) |
| 15 | `framework` (e.g. `reqnroll+xunit3`) | **Not recorded anywhere.** A data file's root holds `formatVersion`, `kronikolVersion`, `suite`, the two times, `ciMetadata`, `environment`, `features`, `background`, `diagnostics` when there are any, and since 5.14.0 `serviceId` and `parties` when the run declares something. Null for every existing report. | RUN (`results/audit-C-background-block-lanes.txt`), READ |
| 16 | `FixedNameForReceivingService` is a display name | **True, and narrower than that.** It is read only for the report's title (`ReportGenerator.cs:1063`). The name a party gets on the calls comes from the handler's options, so a subject declared from this option could name a party no call carries. | PEER, READ |

## 2. What happens today

### 2.1 Five derivations of "who calls what"

| Where | What one call is | Rule | Published as |
|---|---|---|---|
| Component diagram: `ComponentDiagramGenerator.ExtractRelationships` (`ComponentDiagramGenerator.cs:54`) | caller, service, protocol; protocol is the dependency category, else for an event its method (`Event` when it has none), else `HTTP` (L531-539); with the set of methods | ordinal; requests only, without markers or ignored calls | `ComponentDiagram.html` and the report's Components tab (pictures); the mergeable data file's `componentRelationships` (`ReportGenerator.cs:5780`), only with `GenerateMergeableData` |
| History: `InteractionShape.Calls` (`InteractionShape.cs:209`) | `Caller>Service Method Target Status`, the target templated (L246) | templater version 6 (L52); markers and ignored calls out | the ledger: two hashes and a count per scenario, and the run's distinct call lines |
| History: `InteractionShape.Dependencies` (L278) | `caller>service` | ordinal | the ledger's `deps` (`HistoryRunBuilder.cs:150`) |
| Warm-up: `WarmUpCalls` | a templated target of its own: unescaped, query dropped | | the `warmUp` marks in the data file, on the calls it marks |
| `query services` (`QueryCommand.Overview.cs:335`) | the called service alone | case-insensitive (L353) | text and `--json` |
| `query interactions --group-by` | any of nine dimensions; the raw `AbsolutePath` | ordinal | text and `--json` |

The component diagram's endpoint breakdown is a fourth target rule: GUIDs only, so `/orders/123` and `/orders/456` are
two endpoints there (PEER: `ComponentFlowSegmentBuilder.cs` L237-238, L710-715). The ledger's call lines cannot be read
back into fields either: party names hold spaces (`Breakfast Provider>Cow Service GET /milk 200`), so a reader cannot
cut the edge from the line (RUN; the probe reads `deps` instead).

### 2.2 A call says who made it, not why

- **Caller names are configuration.** Every tracker's options carry `CallerName` (alias `CallingServiceName`):
  Cosmos DB (`CosmosTrackingMessageHandlerOptions.cs:14`, the alias at `:18`), Redis, Kafka, MongoDB, SQS and the rest.
  EF Core copies the HTTP options' caller (`SqlTrackingInterceptorOptionsExtensions.cs:24`). The defaults are
  `"Caller"` (`TrackingDefaults.cs:12`) and, for Playwright, `"Browser"` (`TestTrackingIdentity.cs:23`). A client
  registered in the service's container names every call through it after the service, whoever made the call (READ).
- **Nesting is computed, then thrown away.** `CallNesting.Parents` (`CallNesting.cs:32`) gives each request its parent:
  the innermost open request whose service is this request's caller, or, for another caller, one on the same Kronikol
  `traceId` (how a delivery sits inside the call that published it). Only `flow` reads it, to indent (READ).
- **How a call reached its test** is in the data file as `attributionSource`, which `ReportScanner` has read since
  5.14.0 (#190 R1: `ReportScanner.cs:835` at `8d7d1e1d`) and only `query parties` uses. Its values
  (`AttributionSource.cs`): `RequestHeader` (a request the test sent carried the identity in), `TestContext` (the
  test's own context, and any work that inherited it), `Scope` (an explicit scope, or an identity a message carried),
  `DocumentOwner` and `DocumentFlow` (a document a scenario wrote named it), `Detached`, `GlobalFallback`, `None`, and
  `Expired` (resolved after its scenario had ended). On the Reqnroll lane (requests; phase RUN, attribution PEER:
  #192's `results/measure/bp-ci-reqnroll-in-memory.txt`, section 9):

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
`TemplateStatement`, and infers the test client and the subject by a prototype rule. #190's rule (`infer.py`, PEER)
agrees on the three lanes both measured and finds the same pair on `xunit-in-docker`. 5.14.0's resolver itself, through
`query parties`, picks the same test client and subject on all 52 reports audit B ran it on (one, Example.Api's xUnit3
report, holds no calls), and its second-subject rule never fires, so no number below moves (RUN,
`results/audit-B-parties-summary.txt`). Results are in `results/card-prototype.txt`.

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

On the five in-memory lanes other than Reqnroll's, five test frameworks driving the same service, the calls inside a
received call are 755 every time, and the calls outside any vary from 56 to 90 between frameworks: the Cosmos DB
metadata reads repeat per host start (11 to 22 each), and the frameworks start the host a different number of times.
Across every in-process lane measured, the docker ones included, the range is 56 to 159 (PEER: `xunit-in-docker` gives
159, counted by `attributionSource`). These are differences between frameworks in one CI run; what background calls do
between runs of one suite is measured in 2.4. The test's own phases are recorded on the Gherkin-style lanes (LightBDD:
Setup 234, Action 199) and not on xUnit, NUnit and TUnit (445 of 451 unknown). The Jest stand-in carries no
`attributionSource` and no phase on any of its 8 calls, but its records nest: the service's 4 calls sit inside the
test's (`results/card-prototype-stand-ins.txt`). The prototype has no GraphQL rule, so its 5 operations count the
stand-in's two GraphQL operations as one; #192's 4.2 splits them.

The service's calls outside any received call, commonest first: Cosmos DB metadata reads (`Other` on `/`,
`/dbs/BreakfastDb` and `/colls/orders`, 15 to 21 each), the outbox poller's query (`SELECT VALUE root FROM root WHERE
true`, 54 in docker), a `Replace` of an outbox document, `SendOrderConfirmation` over gRPC, one BigQuery insert and one
ClickHouse insert. All of them are the service's own work. "Other" is mostly deliveries: a broker's call into the
service. Read from `attributionSource` alone instead of nesting, the split of the service's calls is the same on both
in-memory lanes, 755 and 86, 731 and 68 (PEER, #192's measurement). The two signals agree on the subject's calls (one
call of 25,831 differs over 46 reports) and differ on deliveries, which are top level by nesting and `RequestHeader`
by attribution (RUN, `results/audit-B-cards-all.txt`; 4.2). The docker lane above is the run's `reqnroll-in-docker`
artifact; the same run's `xunit-in-docker` lane holds 1,297 requests (PEER). With R1's subject, `exposes` is 81
operations on the in-memory and docker lanes and 74 on the external-SUT lanes (73 on Reqnroll's), and `messages` 24 in
memory and 22 in docker (RUN, audit B; the revised probe gives the same, `results/revision-sizes.txt`).

The card in the revised shape (4.3), written by `probes/revision-card-revised.cs` from the 18 lanes of run 40491266394
(`68a8760`, Kronikol 5.12.1, the newest), in bytes of UTF-8, gzipped at level 9 (`results/revision-sizes-table.txt`):

| Reports | Operations | Whole file, compact | gzipped | Indented | gzipped | `behaviour`, compact | `behaviour` without the scenario index | `run`, compact |
|---|---|---|---|---|---|---|---|---|
| 6 in-memory lanes (187 to 214 scenarios) | 179 to 180 | 168,952 to 177,632 | 24,505 to 25,375 | 294,880 to 314,029 | 28,441 to 29,746 | 104,148 to 113,640 | 53,313 to 53,586 | 63,250 to 64,764 |
| 6 in-docker lanes | 168 to 171 | 159,041 to 167,624 | 23,494 to 24,459 | 277,736 to 297,145 | 27,302 to 28,495 | 99,968 to 108,969 | 48,690 to 49,443 | 57,688 to 59,564 |
| 6 external-SUT lanes | 73 to 74 | 87,679 to 102,173 | 13,259 to 15,928 | 151,710 to 176,944 | 15,035 to 17,902 | 60,417 to 74,141 | 15,425 to 15,680 | 27,032 to 28,375 |
| Example.Api Reqnroll (10 scenarios, 5.8.0) | 6 | 7,378 | 1,816 | 13,133 | 2,021 | 4,529 | 2,224 | 2,809 |
| Jest ingest stand-in (2 scenarios) | 6 | 4,631 | 1,250 | 8,780 | 1,421 | 2,516 | 2,055 | 2,075 |

On `xunit-in-memory` the compact file's 173,310 bytes are `behaviour`'s 108,695 (the scenario index 55,369, the edges
with their operations 46,627, the rest 6,699) and `run`'s 64,575 (`run.scenarios` 30,795, `counts` 23,773,
`examples` 8,157, `parties` 1,124). The first draft's table here measured the prototype's smaller shape (no op ids, no
status, cause or phase sets, no `run` part); audit B measured the first draft's 4.3 at 143,806 to 152,218 bytes
compact on the same six lanes. The probe approximates #192's key (no `SET` skip, no templating of a document's
`query`), which moves the operation counts, not the shape.

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
| a scenario's operations, status dropped (the first draft's scenario index) | 88 of 933 run pairs (9.4%); 80 of the 921 whose scenarios all had the same result (8.7%) |

The operations that come and go are real non-determinism or a failure's footprint (RUN,
`results/audit-A-ops-diff.txt`, `audit-A-ops-diff-results.txt`): a Cosmos DB `Replace` (an optimistic-concurrency
retry, 18 pairs, 2 of them with 3 scenarios whose results differ), a Spanner `Rollback` (5), and a block of MongoDB
calls with a `Breakfast Provider GET` (4) that comes and goes with 11 scenarios that passed in one run and failed in the
other. Where the scenarios all had the same result, the edges never changed (0 of 993 pairs) and the operations changed
in 21 of 921 (2.3%) (`results/audit-A-same-results.txt`, `audit-A-scenario-index.txt`). A run averages 123.5 distinct
operations and 13 edges.

The ledger holds history's lines, not the card. The card itself was measured on ten pairs of runs of one lane on one
commit, 2,101 scenario rows: eight CI lanes of BreakfastProvider's runs 40290999167 and 40473765203 (`cf47045`,
Kronikol 5.4.1, Linux), a day apart, and its `xunit-in-memory` and `reqnroll-in-memory` lanes run twice on this
machine (`68a8760`, 5.12.1, Windows). Audit B's probe (`probes/audit-B-card-behaviour.cs`) read them five ways; this
revision's probe wrote the revised card in full (RUN, `results/audit-B-readings-10pairs.txt`,
`results/revision-pairs.txt`):

| Reading | `behaviour` byte-identical | Identical without the scenario index | Scenario rows whose operations changed |
|---|---|---|---|
| P0: `cause` by nesting first; phase from any parent; every call in a scenario's operations | 6 of 10 | 9 of 10 | 5 |
| P1, the first draft (4.0, 4.2): `cause` by `attributionSource` first | 6 of 10 | 9 of 10 | 5 |
| P2: P1, and a background call keeps the phase it recorded | 6 of 10 | 10 of 10 | 5 |
| P3: P2, and background calls out of a scenario's operations (Q6) | 8 of 10 | 10 of 10 | 2 |
| P4: P2, and a scenario's operations the test's own | 10 of 10 | 10 of 10 | 0 |
| **As revised (4.3), the whole card** | **10 of 10** | 10 of 10 | 0 |

- Three of the five changed rows were background calls: the Cosmos DB SDK's metadata reads at host start, landing in
  whichever scenario started the host (TUnit, 2 rows), and the outbox poller's `Replace` of a document one scenario
  wrote (BDDfy, 1 row). That answers Q6: background calls leave every scenario's operations.
- One of those background calls was recorded while an unrelated request to the subject was open, so `CallNesting` made
  that request its parent, and with the phase taken from any parent it moved `effectivePhases` (P1). It is the only
  such call in 25,831 of the subject's calls over 46 reports (`results/audit-B-cards-all.txt`). A background call
  therefore keeps its recorded phase as a rule (4.2).
- The other two rows are real behaviour moving between scenarios through state they share: BreakfastProvider's
  `MenuService` caches the menu for 5 minutes in a host-wide `IMemoryCache`, and which of six `GET /menu` scenarios
  misses, and so calls Supplier Service and publishes, depends on order and timing (4 misses in one run, 5 in the
  other). Only an index of the test's own operations removes it (P4); the full list moves to `run` (Q16).
- No edge, operation, status, origin, party, `exposes`, `messages`, `unresolved` or `capture` member differed in any
  pair. The revised card's `run.counts` differs on 6 of the 10 pairs, on one or two operations a lane.

### 2.5 What there is to build on

- **`kronikol ctrf` is the pattern.** A run can write `ctrf-report.json`, and the tool rebuilds it from any report:
  `ReportScanner.Scan` (`CtrfCommand.cs:80`) into a neutral `CtrfTest` list, then the one writer,
  `CtrfReportGenerator.Build` (`CtrfReportGenerator.cs:212`). Its doc comment gives the reason it is not a `query` verb:
  "a document that gets truncated is not a document".
- **`Run.json` is written last** and lists what reached disk, from the outputs that actually wrote
  (`ReportGenerator.cs:781`). Its reader ignores members it does not know (`RunManifest.cs:135`), so a new key is
  additive. A new output added to the run-end list (`Add(...)`, as `ComponentDiagram.html` at L579 and the failures
  digest at L594) is listed in its `files` without further work. The list of files a run plans to write, which the
  rotation moves aside for a run that left no manifest (`PlannedFiles`, `ReportGenerator.cs:852-895` at `8d7d1e1d`),
  names each by hand. A merged run writes no `Run.json` (`Generated-Reports.md:537`).
- **Ingest goes through the same run end** (`IngestPipeline.cs:694`), with the library's default options and a curated
  flag set (`IngestPipeline.DefaultOptions`, `IngestPipeline.cs:428-435` at `8d7d1e1d`). A new run-end file reaches an
  ingested run when it is on by default, and not otherwise: ingest has no `--ctrf` either. **`kronikol merge`** writes
  the run outputs of a sharded run through `MergedRunOutputs.Write` (`MergedRunOutputs.cs:37-198` at `8d7d1e1d`), from
  every merged call; sharded runs are "the one shape of run that is always on CI" (`MergeCommand.cs:169-171`).
  Kronikol4J writes its own reports and would not get the file.
- **One target framework.** 5.x builds `net10.0` only (`Directory.Build.props:5-6`), so the old rule that `Query/` code
  must not be reached from `Reports/` no longer applies.
- **Schemas.** No JSON Schema is committed to the repo. The data file's schema is generated at run end
  (`GenerateTestRunReportSchema`) and closed (`additionalProperties: false` on every object that declares properties,
  `CloseDeclaredObjects`), and `tests/Kronikol.Tests` validates documents with JsonSchema.Net. Two facts hold it: every
  key the writer emits is declared
  (`TestRunReportSchemaContractTests.Every_key_the_json_writer_emits_is_declared_in_the_schema`), and an undeclared
  key is a violation (`SchemaClosedContractTests.An_undeclared_key_is_a_violation`).

## 3. Findings the issue does not state

| # | Finding | Basis | Consequence |
|---|---|---|---|
| F1 | Five derivations of the call graph, four rules for one call; the card would be a sixth | READ, PEER (2.1) | Build one call graph and make the card a projection of it; pin the component diagram's edges against it |
| F2 | A caller's name is configuration, not observation | READ (2.2) | `origin` as the caller's role cannot separate a test acting through the service's clients. `cause` narrows it; the card must not claim more |
| F3 | The service's calls outside any received call are real work on BreakfastProvider, and the seeding's position elsewhere | RUN (2.3), INFERRED | Neither "subject" nor "test" alone describes them. The card carries `cause` beside `origin` (#192's Q4, with this plan's fallback, 4.2); both read `background` there |
| F4 | Phase is lost at the request header, and is a property of the test's narrative, not of who called | RUN (2.2) | The card derives an effective phase through nesting (4.2), and never uses phase to decide origin |
| F5 | Counts and order are not stable from run to run; presence is | RUN (2.4) | Byte identity is promised for presence only; counts move to the run part (4.3). On real re-runs the revised card's `behaviour` held on 10 of 10 pairs |
| F6 | The templating rule exists, versioned, with consumer rules | READ | One operation key (#192); numbers are `{n}` |
| F7 | Databases and brokers have no path; a Kafka topic is in the URI only at `Detailed` or `Raw` verbosity (`KafkaOperationClassifier.cs:40-51`; `Summarised` writes `kafka:///`) | READ, PEER | An operation has a `target`, which may be a statement head or a topic; a message's topic is read from the URI by a table per scheme (4.2), and a destination the capture did not record is said so (`capture.emptyTargets`) |
| F8 | A run can miss whole sides of the graph: the external-SUT lanes record none of the service's calls | RUN | A `capture` block states it; absence is "not recorded", never "none" |
| F9 | A party's kind is per run (a fake on one lane, an emulator on another, a fake answering for the production host); its id is per codebase | PEER | The card's kinds describe the run, and say how each was known (`kindSource`, #190) |
| F10 | The card is 159 to 178 KB compact on an in-process lane of 187 to 214 scenarios (23 to 25 KB gzipped), not 8 KB | RUN (2.3) | Fine for an indexer, and written compact (4.3); `Service.md`, a model's first read, stays FLEET S4's and keeps its budget |
| F11 | Kronikol4J writes its own reports, and its calls rarely reach a data file. Its fragments carry no calls (`ReportFragment.java:32-40`: calls survive only as diagrams and `componentRelationships`), and `kronikol4j merge` writes the HTML report alone, no data file at all (`MergeCommand.java:64-65`). A standalone run writes calls only when `kronikol.report.dataFormats` names a format, which is empty by default (`ReportOptions.java:122-123`, `:559-568`; 0.1.24). Its data file's root has no `formatVersion`, `suite`, `ciMetadata`, `environment` or `background`, and a call no `attributionSource`, `statusText` or `error` (`ReportDataSerializer.java:57-81`, `:290-322`) | READ (Kronikol4J `origin/main` at `d7ad2b0`, audit C); PEER (#192's 3.6, which tried to make a Java stand-in, and its 3.9) | `kronikol card` gives a Java suite a card only from a standalone run with `dataFormats=json`, and that card has `run.repository`, `commit`, `branch`, `suite` and `run.report.formatVersion` null even on CI, `cause` from nesting alone (which finds little: Kronikol4J logs a call after the calls it made, PEER), and a thrown call it cannot tell apart. A merged Java run has no data file and so no card. Q7's Kronikol4J issue asks for the rest; ledger lines at R1 and R2 |
| F12 | `sid:` addresses a scenario, never a call; `sN/iM` depends on order | READ (`Address.cs:46-52`), RUN (2.4) | An example address belongs to the run part, and a call from the root background block has none |
| F13 | The test framework is recorded nowhere | RUN, READ | `run.framework` is null until a capturer writes it (Q12) |
| F14 | `FixedNameForReceivingService` names the title only | PEER, READ | The subject comes from #190's `ServiceId` or inference, never from this option |
| F15 | At run end the resolver also reads which test client `CreateTestTrackingClient` built (`observed`, with the application as `note`), and the data file records it only when the run declares something; `kronikol card` and `query parties` infer from the file. All 12 `dotnet new` template files and 15 Example.Api suites build their client that way and declare nothing | READ (`PartiesReport.cs:18-25`, `ReportGenerator.cs:5850-5853`, `QueryCommand.Overview.cs:518` at `8d7d1e1d`); RUN (`results/audit-C-example-parties.txt`: the plan's own example report infers `Caller` and `Dessert Provider`, both `inferred`) | The card is a function of the data file: at run end too it reads the block or infers, never the observations (Q15) |
| F16 | The data file's root `background.interactions` holds calls no scenario owns (expired, or with no identity). The scanner never reads it, while the run end, the component diagram and the resolver include it | READ (`ReportScanner.cs:322-347`, `:441`, `:495-507` at `8d7d1e1d`); RUN (`results/audit-C-background-probe.txt`: `services` and `parties` leave out the party only a background call reaches; audit B: two docker lanes of runs 40473765203 and 40491266394 hold 3 `Expired` calls there, the only calls of one operation) | #190's R3 indexes it (planned 5.16.0). The card reads it: on edges and operations with `cause` `background`, in no scenario's operations, with no example address |
| F17 | A run-end output cannot hash the data file: the outputs run in parallel, the data file among them, and its `formatVersion` is known only once its payloads are written | READ (`ReportGenerator.cs:939-951`, the data file's action at `:575`, `:607-610`, `:5536-5537` at `8d7d1e1d`) | The card is written after the output list and before `Run.json` (4.5) |
| F18 | The first draft's message rule read most brokers' receives as publishes, and took the topic from the target's last segment, which is `{n}@{n}` for Kafka at Raw, `{n}` for Event Hubs at Detailed and Raw, and nothing for Service Bus and EventBridge, whose queue or bus is the URI's authority | READ (the table in 4.2) | Direction and topic come from a table per URI scheme built from each classifier (4.2) |
| F19 | Three things move the card's `behaviour` with no change of behaviour beyond its own rules: the platform (the Cosmos DB SDK asks for a query plan on Linux only), Kronikol's own titles (5.9.1) and a recorded phase (LightBDD, 5.4.1 to 5.12.1) | RUN (`results/revision-pairs.txt`, audit B) | Listed in 4.3 with what tells each apart; `run.environment` added |

**Found on the way** (5.14.0; owner kronikol-67, fixed in #190's R3; lines at `8d7d1e1d`):

1. `RunManifest.ToJson`'s doc says "the ten members" (`RunManifest.cs:81`); the file has written eleven since 5.14.0
   added `serviceId`.
2. `ReportPartyEntry` was inserted between `ReportDiagnosticEntry`'s doc comment and its class
   (`ReportIndex.cs:435-450`), so `ReportPartyEntry` carries two summaries and `ReportDiagnosticEntry` none.
3. `query parties` counts calls over the scenarios only and lists only parties with a count
   (`QueryCommand.Overview.cs:496-502`, `:524-526`), while the run end resolves, and a declaring run writes into the
   block, the background block's parties too (`PartiesReport.cs:23`). On a declared report whose block names
   `Outbox Store` (`real`, id `outbox-db`), called only in the background, it prints two parties, and `--kind real`
   answers "no parties of kind real" (RUN, `results/audit-C-background-probe-declared.txt`).

## 4. The design

### 4.0 Who owns what

| Concern | Owner | What this plan needs from it |
|---|---|---|
| `ServiceId`, each party's id, kind (`test`, `underTest`, `real`, `double`, `unknown`) and `kindSource` (`declared`, `observed`, `inferred`); the `serviceId` and `parties` blocks in the data file, written only when something is declared; the inference of the test client and the subject; the per-call `testDouble` mark and the change to the inference that comes with it (its R2, planned 5.15.0); the root background block indexed apart from the scenarios' calls, with parties resolved at run scope over both (its R3, planned 5.16.0) | #190, `PARTY_IDENTITY_PLAN.md`: the resolver §5.1, inference §5.4, the data file block §5.5, `Run.json` §5.8, the mark §5.10, what this plan may rely on §5.12; R2 and R3 as its session plans them (PEER, agreed by message on 2026-10-27) | The resolver in core (`src/Kronikol/Parties/`), which the card calls with what the data file holds and nothing else: the block's rows when the run declared something, else the calls alone, the root block's included. What a run observed and did not write (an observed `kindSource`, the application as `note`) never reaches the card (Q15). Inferred rows say `inferred`; an unknown row carries no `kindSource`, as 5.14.0 writes (`PartyResolver.cs:116-117` at `8d7d1e1d`). Rows for every caller and every callee, and no row set that implies completeness (§5.5) |
| The operation key (`CallOperation.Of`, internal, built on `InteractionShape.Template` and `StatementHead`; query keys dropped for the card; the GraphQL operation joining it; version 8, as 5.13.0 took 7); `origin` (the caller's role) and `cause` (`test`, `request`, `background`, `unknown`, from `attributionSource` first, else nesting); the consumer's templating rules written into the data file; `--group-by caller,origin,route`; `caller` on `interactions --json` rows (#184's third ask) | #192, `CALL_OPERATION_AND_ORIGIN_PLAN.md`: the key 4.1 (`CallOperation.Of`, with query keys off for the card), GraphQL 4.2, `origin` 4.3 (with the seeding limit), the engine's dimensions 4.4, the `shapeTemplates` block 4.5, `cause` its section 9 Q4, open; releases R1 (the key), R2 (the dimensions, `caller` on rows, the rules block) and R3 (`origin`, and `cause` if Q4 says so) | All of them pure functions of what a report holds, in core. `cause` recommended to the owner with this plan's reason (F3, Q5). If Q4 is declined, the graph keeps the background classification it needs itself (4.2) and the card leaves `causes` out |
| The call graph (parties, edges, operations, per-call parent, origin, cause and effective phase); the message table; the one function that resolves parties from what a report holds and returns the subject row, and `InferenceVersion` beside the inference (this plan's R1, agreed with kronikol-67 by message on 2026-10-27); `service.json`, its schema, its stability and its `capture` block; `kronikol card`; the run-end and merge files and `Run.json`'s key | this plan | |

The order: #190 R0 (released as 5.13.0, a minor once the owner chose history's bridge), #190 R1 (5.14.0: the resolver,
the declarations and the block), #190 R2 (planned 5.15.0: `testDouble`, and the inference rule this plan calls version
1) and R3 (planned 5.16.0: background calls at run scope), then #192 (R1 to R3: the key, the rules block, then `origin`
and `cause`, which read #190's roles), then R1 here. Version numbers are claimed at push; 5.15.0 and 5.16.0 are #190's
session's plan (PEER).

For the sibling plans, found by this plan's audit and not changes to this plan:

- #192's key drops a URI's authority (its 4.1, step 1), so two Service Bus queues at Summarised
  (`servicebus://orders/`, `servicebus://payments/`) are one operation, and an EventBridge bus is not in the target;
  the card reads both from the URI for `messages` (4.2). Its 3.6 puts its 4.1 key at 179 on `xunit-in-memory` where
  its own route results give 184 with the head fixes (181 on `xunit-in-docker`); its 4.1, 4.8 and section 8 still say
  "6 to 7, or 7 to 8", and 5.13.0 shipped 7 with the bridge; its F8 ("the engine never reads a call's attribution")
  has been false since 5.14.0.
- #190's §5.1 and §5.12 promise that a card written later from a report matches the run-end one; that holds because
  the card reads only the file (Q15). `query parties` differs from the run's own table on a report with background
  calls (section 3, found on the way, 3).

### 4.1 Principles

1. **One writer, two readers, one input.** The card is built from a neutral call record and a parties table, and both
   come from what the data file holds. A running suite maps the calls its data file writes and the parties its block
   writes, or the inference over those calls; `kronikol card` maps a scanned report the same way. One builder, one
   writer, as CTRF has. What a run knew and did not write (the test client `CreateTestTrackingClient` observed) never
   reaches the card (Q15).
2. **Presence is behaviour; counts are the run.** The `behaviour` part holds sets. Anything that changes when the same
   code runs again lives in `run`: counts, hosts, diagnostics, and what reached each scenario through state the
   scenarios share.
3. **Say what was not seen.** A side of the graph with no calls, a destination the capture did not record, a phase
   that was derived: each is stated. Unknown is `null`, inferred is labelled `inferred`, and nothing is guessed
   silently.
4. **Additive keys do not bump `formatVersion`.** Readers ignore keys they do not know, as `Run.json`'s reader does;
   the schema is closed, so a consumer validating against an older copy rejects a newer card (4.6).

### 4.2 The call graph (core)

A new folder, `src/Kronikol/CallGraph/`:

- **`CallRecord`**, the neutral input: the scenario (ordinal, `stableId`, feature, name, tags, source file and line,
  whether it is a test), or none for a call from the data file's root `background` block; position in the scenario;
  request or response; caller, service, method, URI; dependency category, caller category, meta type; recorded phase;
  `attributionSource`; `requestResponseId` and Kronikol `traceId`; status code and text; `isUserAction`; #190 R2's
  `testDouble`; and the request content of a statement-shaped call or a POST (a GraphQL candidate), as #192's
  `CallOperation.Of` reads it: its `SET` skip reads past the first line, and its GraphQL reader parses a body whole, so
  a pretty-printed body or Apollo's key order is read too. Never a body otherwise.
- **The call set** is the data file's, on both sides: every scenario's `httpInteractions` and the root
  `background.interactions` (#190's R3 indexes the latter apart, PEER), without diagram markers and `TrackingIgnore`
  calls, which the writer drops (`ReportGenerator.cs:5281`, `:382-384` at `8d7d1e1d`), and without calls whose test id
  names no scenario, which the data file leaves out (`PartiesReport.cs:15-16` at `8d7d1e1d`). The component diagram
  draws those orphaned calls too (`OrphanedCalls.cs:29-30`) and honours `ComponentDiagramOptions.ParticipantFilter`; the
  card does neither.
- **The mapping rules both sides share**, one function each: the empty Guid is no id (`ReportScanner.cs:987-992` at
  `8d7d1e1d`: markers and user actions travel with it, and kept as an id it would pair every user action with every
  other in `CallNesting`); a phase of `Unknown` and a meta type of `Default` are none (`:831-832`); a status is read
  through `InteractionStatus.Split` at run end and `InteractionStatus.Read` from a report (`InteractionStatus.cs:29`,
  `:62`), and written as the code's digits, else the label, else `none` for a call with no response; `iM` counts the
  scenario's list as the data file writes it, as `Failures.md` counts it (`FailuresDigestGenerator.cs:244-254`).
- **`CallGraphBuilder.Build(records, parties, rules)`** returns the graph:
  - **Parent** of each request, by `CallNesting`'s rule. The rule moves from `Query/CallNesting.cs` into this folder,
    taking `CallRecord`; `flow` keeps asking it through a mapping. One rule, as its doc comment requires.
  - **`origin`** and **`cause`**: #192's functions. `origin` is the caller's role read from the parties (`test`,
    `subject`, else `other`). `cause` is #192's Q4, open, and recommended (Q5). The graph needs the background
    classification whatever Q4 says, for the scenario index (4.3) and the phase rule below, so it computes it the way
    #192 describes `cause` and audit B measured it:
    1. a call the test party made: `test`;
    2. a call from the root background block: `background`;
    3. else by `attributionSource`: `RequestHeader` is `request`, and any other value (`TestContext`, `Scope`,
       `DocumentOwner`, `DocumentFlow`, `Detached`, `Expired`, `GlobalFallback`, `None`) is `background`;
    4. else, where a producer records no `attributionSource` (ingest, the taps, Kronikol4J): `request` when the call
       ran inside a call its caller received, else `unknown`.

    Attribution comes before nesting because nesting places by recording order: read nesting first, the outbox
    `Replace` recorded while an unrelated request was open reads `request`, and the BDDfy pair of 2.4 stays different
    (P0). The two readings agree on the subject's calls and differ on deliveries: a broker's call into the subject is
    top level by nesting and `RequestHeader` by attribution, 108 to 114 of 111 to 117 a lane in memory (RUN,
    `results/audit-B-cards-all.txt`), so a delivery reads `request`. A `TestContext` call the subject makes outside any
    request reads `background`: it is the service's own background work or a test seeding through the service's
    clients, which no recorded signal tells apart (#192 4.3, item 2); 80 of the 86 such calls on `xunit-in-memory`
    are `TestContext` (PEER). If #192's Q4 is declined, this classification stays inside the graph and the card leaves
    `causes` out (Q5).
  - **Effective phase**: the recorded phase when it is `Setup` or `Action`; else, for a call whose cause is not
    `background`, the effective phase of the call it ran inside, when there is one; else `unknown`. A background call
    keeps the phase it recorded as a rule, not because it has no parent: one was nested under an unrelated request by
    recording order and took that request's `action` (2.4). This gives the service's in-request calls the phase of the
    test's call they served (the 731 of 2.2). The card counts the phases derived (`run.counts`), and names the field
    `effectivePhases`: #192's `--phase` filter reads the recorded phase, so for a nested call the two readings differ
    on purpose, and the names say which is which.
  - **Parties**: every caller and service name in the call set, with the set of categories its calls carried (a
    party's category is per record: the service is plain HTTP on most calls and gRPC on nine, PEER), and the row the
    parties function gives (below).
  - **Edges**: (caller, service, protocol), the protocol by the component diagram's rule (`GetProtocol`,
    `ComponentDiagramGenerator.cs:534` at `8d7d1e1d`, split to take the category, the meta type and the method, and
    shared rather than copied), so over the same calls and with no participant filter the card's edges are the
    diagram's edges.
  - **Operations**: (edge, method, target, GraphQL operation), the target by #192's key with query keys off, and
    without the GraphQL suffix, which the card writes apart. Each operation keeps its sets: statuses seen, origins,
    causes, effective phases, scenarios. Counts are kept beside them for the run part. A Playwright user action
    (`isUserAction`, caller `Browser`) is an operation on its edge, as the diagram draws it, and never in `exposes`.
- **The parties, one function** (this plan's R1, agreed with kronikol-67 by message on 2026-10-27). `query parties`
  resolves inline today: the block's rows as written when the report has one, else `PartyResolver.Resolve` over the
  scenarios' calls with no declarations and no observations (`QueryCommand.Overview.cs:504-523` at `8d7d1e1d`), and the
  subject row is computed inside the resolver and dropped (`PartyResolver.cs:97-99` at `8d7d1e1d`). R1 lifts that branch
  into one function in `src/Kronikol/Parties/` taking neutral inputs (the block's rows as written, `serviceId`, the
  calls) and returning the table with its subject row; `query parties`, the card and #192's `origin` call it. It takes
  no observations: what a run observed and did not write never reaches a reader of its file (Q15).
  - **The subject row** is the `underTest` party that received the most calls in the call set, then the first by ordinal
    name: the rule 5.14.0's resolver applies (`PartyResolver.cs:98-99` at `8d7d1e1d`), so on a report with the block it
    is the row that carries `serviceId`. When the inference names two subjects (a second that makes calls and takes a
    quarter of the test's calls, #190 §5.4), the subject row is the one with more calls received, and the other stays an
    `underTest` row of `parties`. On the 52 reports audit B ran 5.14.0's resolver on, the second-subject rule never
    fired.
  - **`PartyResolver.InferenceVersion`**, a constant beside the inference. Version 1 is the rule 5.15.0 (#190 R2)
    ships: a party every call to which carries `testDouble` is `double`, `observed`, and the subject is chosen only
    among parties of unknown kind; before it, a known-kind party the test called most blocked the subject (PEER:
    kronikol-67's R2 snapshot `bae9b02b`). The version moves when the table the inference returns for some set of
    calls changes: `InferTests`, `InferSubjects`, the store list they read (`IsStore`, from `DependencyPalette.Resolve`
    and `DependencyCategories.IsStatementShaped`, so a palette change that moves a category into or out of a store
    type moves it too), the double rule, and the subject row's choice. A guard fact holds a corpus of call sets, one
    per category the palette names among them, with the table each gives; a changed answer fails it until the version
    moves and the answers are pinned again, as #192's guard holds `InteractionShape.Version`. #190's R3 does not touch
    the inference (PEER).
- **Messages.** A message is the subject's: an operation the table below calls a publish (`publishes`) or a receive
  (`consumes`), on a call from the subject to a broker, or a broker's delivery into the subject (a call that carries a
  caller category, broker to consumer), which is `consumes` in every scheme. The table is built from each tracker's
  classifier, its label function over its operation enum at every verbosity, so a label a classifier can write maps
  back to its operation: labels change with verbosity (`Receive ← q`, `Receive (×3) ← q`, `Receive`), and no list of
  method names matches them. Settlement and management operations stay operations on their edges and are not
  messages. A label the table cannot map (a capturer newer than the card's writer) gives a row with `direction: null`,
  never `publishes`. One fact per scheme. Lines are at `8d7d1e1d`.

  | Scheme | Publishes (the subject's call) | Receives (the subject's call) | Neither | Where the destination is |
  |---|---|---|---|---|
  | `kafka` | Produce | none: Kafka logs every consume as the broker's delivery (`KafkaTracker.cs:154`, `:320`, `:385`) | Subscribe, Unsubscribe, Commit, Flush and the transaction calls (`:240-252`, `:369`; opt-in, `KafkaTrackingOptions.cs:40-44`) | the first path segment: `kafka:///{topic}` at Detailed, `kafka:///{topic}/{partition}@{offset}` at Raw, none at Summarised (`kafka:///`, `KafkaOperationClassifier.cs:44-50`) |
  | `eventhubs` | Send, SendBatch | ReadEvents, ReadEventsFromPartition, ProcessEvent | CreateBatch, the property reads, StartProcessing, StopProcessing (`EventHubsOperationClassifier.cs:13-23`) | the first path segment, the hub (`eventhubs:///{hub}/{partition}` at Detailed and Raw, `EventHubsTracker.cs:109-113`) |
  | `servicebus` | Send, SendBatch, Schedule | Receive, ReceiveBatch, Peek | CancelSchedule, Complete, Abandon, DeadLetter, Defer, the lock renewals, the session state, StartProcessing, StopProcessing (`ServiceBusOperationClassifier.cs:20-39`) | the authority, the queue or topic (`servicebus://{queue}/{subscription}`, `ServiceBusTracker.cs:151-157`), which #192's path cut drops from the target |
  | `pubsub` | Publish, PublishBatch | Pull, Receive | Acknowledge, ModifyAckDeadline, StartSubscriber, StopSubscriber (`PubSubOperationClassifier.cs:13-20`; logged caller to Pub/Sub as a publish is, `PubSubTracker.cs:50-56`) | the first path segment, the topic or subscription; `unknown` is the tracker's placeholder for none (`PubSubTracker.cs:112-114`) |
  | `sqs` | SendMessage, SendMessageBatch | ReceiveMessage | DeleteMessage, ChangeMessageVisibility and their batches, the queue management calls (`SqsOperationClassifier.cs:75-88`) | the first path segment; none in `sqs:///` (`SqsTrackingMessageHandler.cs:183-185`) |
  | `sns` | Publish, PublishBatch | none: SNS delivers through the subscriber's own transport | Subscribe, Unsubscribe, the topic and subscription management calls (`SnsOperationClassifier.cs:72-83`) | the first path segment; none in `sns:///` (`SnsTrackingMessageHandler.cs:183-185`) |
  | `storagequeue` | SendMessage | ReceiveMessages, PeekMessages | DeleteMessage, UpdateMessage, ClearMessages, the queue management calls (`StorageQueueOperationClassifier.cs:34-46`) | the first path segment; none in `storagequeue:///` (`StorageQueueTrackingMessageHandler.cs:163-165`) |
  | `masstransit` | Send, Publish | Consume | none | the first path segment: the queue of a send, the message type of a publish or a consume; `unknown` when none (`MassTransitOperationClassifier.cs:61-62`, `:76-79`) |
  | `eventbridge` | PutEvents | none | the rule, target and bus management calls (`EventBridgeOperationClassifier.cs:122-136`) | the authority, the bus (`eventbridge://{bus}/`, `EventBridgeTrackingMessageHandler.cs:189`) |
  | any other: the generic `MessageTracker`, or an application's own scheme (`event://` on Example.Api; `eventgrid://` and `eventhub://` on BreakfastProvider, `results/revision-schemes.txt`) | a call from the subject | none: a consume is logged broker to consumer (`TrackConsumeEvent`, `MessageTracker.cs:318-331`) | none | the last path segment, else the authority |

Nothing in the report changes. The graph is built in memory at run end and by the tool.

### 4.3 `service.json`

One file, two parts. The order of members is fixed; every list is sorted by ordinal comparison on the keys named
below; numbers and statuses are written invariant; the text is UTF-8, LF, no BOM, written compact on one line with a
final LF by `Utf8JsonWriter` with the default encoder, as `Run.json` is written (indented there).

```json
{
  "formatVersion": 1,
  "run": {
    "id": "local:20261025T202804Z:00c4406a",
    "at": "2026-10-25T20:28:04Z",
    "repository": null,
    "commit": null,
    "branch": null,
    "suite": "Example.Api.Tests.Component.ReqNRoll.xUnit3",
    "framework": null,
    "kronikolVersion": "5.N.0",
    "report": { "file": "TestRunReport.krkl", "formatVersion": 3, "kronikolVersion": "5.8.0+efb9ce9d…", "sha256": "e43a7d0b…" },
    "environment": { "os": "Microsoft Windows 10.0.26200", "runtime": ".NET 10.0.11" },
    "results": { "scenarios": 10, "passed": 10, "failed": 0, "skipped": 0, "bypassed": 0 },
    "behaviourHash": "d679a460…",
    "parties": [
      { "name": "Caller", "hosts": [], "doubleCalls": 0 },
      { "name": "Cow Service", "hosts": ["localhost:15037"], "doubleCalls": 0 },
      { "name": "Dessert Provider", "hosts": ["localhost"], "doubleCalls": 0 },
      { "name": "Event broker", "hosts": ["event-broker"], "doubleCalls": 0 }
    ],
    "counts": [
      { "op": "0570087c4f77cc66", "calls": 4, "errors": 1, "statuses": { "200": 3, "400": 1 },
        "phases": { "setup": 0, "action": 4, "unknown": 0 }, "derivedPhases": 0 },
      { "op": "c0334af35aa6f6db", "calls": 4, "errors": 0, "statuses": { "200": 4 },
        "phases": { "setup": 4, "action": 0, "unknown": 0 }, "derivedPhases": 4 }
    ],
    "examples": [ { "op": "0570087c4f77cc66", "address": "s0/i8" }, { "op": "c0334af35aa6f6db", "address": "s0/i1" } ],
    "scenarios": [
      { "sid": "6fb32362a4f528b0",
        "ops": ["0570087c4f77cc66", "3a793919f403a8c0", "5fda56c40a778149", "c0334af35aa6f6db", "f757d1819ca0004d"] }
    ],
    "degraded": {}
  },
  "behaviour": {
    "rules": { "operationKey": 8, "templateRules": null, "inference": 1 },
    "subject": { "name": "Dessert Provider", "id": null, "source": "inferred" },
    "parties": [
      { "name": "Caller", "id": null, "kind": "test", "kindSource": "inferred", "note": null, "categories": [] },
      { "name": "Cow Service", "id": null, "kind": "unknown", "kindSource": null, "note": null, "categories": [] },
      { "name": "Dessert Provider", "id": null, "kind": "underTest", "kindSource": "inferred", "note": null, "categories": [] },
      { "name": "Event broker", "id": null, "kind": "unknown", "kindSource": null, "note": null, "categories": ["MessageQueue"] }
    ],
    "edges": [
      {
        "from": "Caller", "to": "Dessert Provider", "protocol": "HTTP",
        "operations": [
          { "op": "0570087c4f77cc66", "method": "POST", "target": "/cake", "graphqlOperation": null,
            "statuses": ["200", "400"], "origins": ["test"], "causes": ["test"], "effectivePhases": ["action"] }
        ]
      },
      {
        "from": "Dessert Provider", "to": "Cow Service", "protocol": "HTTP",
        "operations": [
          { "op": "c0334af35aa6f6db", "method": "GET", "target": "/milk", "graphqlOperation": null,
            "statuses": ["200"], "origins": ["subject"], "causes": ["request"], "effectivePhases": ["setup"] }
        ]
      }
    ],
    "exposes": { "basis": "tested", "ops": ["0570087c4f77cc66", "3a793919f403a8c0", "5fda56c40a778149", "f757d1819ca0004d"] },
    "messages": [ { "op": "a3715b1bed820890", "direction": "publishes", "party": "Event broker", "topic": "cake_events" } ],
    "scenarios": [
      { "sid": "6fb32362a4f528b0", "feature": "Cake", "scenario": "Calling Create Cake Endpoint Without Eggs",
        "tags": [], "source": { "file": "Features/Cake.feature", "line": 13 },
        "ops": ["0570087c4f77cc66", "3a793919f403a8c0", "5fda56c40a778149", "f757d1819ca0004d"] }
    ],
    "unresolved": ["Caller", "Cow Service", "Dessert Provider", "Event broker"],
    "capture": { "calls": "recorded", "subjectCalls": "recorded", "emptyTargets": [] }
  }
}
```

(Illustrative: `kronikol card` on the Example.Api Reqnroll report in `inputs/examples-5.8.0/`, as the revision's probe
writes it, which approximates #192's key. Shown indented, where the file is one line; the excerpt shows two of three
edges with one operation each, two of six counts and examples, and one of ten scenarios in each part, with the hashes
cut.)

**`behaviour`**, byte-identical whenever the run's behaviour is:

| Member | Holds | Sorted by |
|---|---|---|
| `rules` | `operationKey`: the version of #192's key, `InteractionShape.Version` (7 since 5.13.0, 8 after #192's R1). `templateRules`: `HistoryShapeRules.Hash` (`HistoryShapeRules.cs:70` at `8d7d1e1d`, what history records as `shapeRules`) of the consumer's rules the report recorded in #192's `shapeTemplates` block, `"none"` when it recorded none, null when the report predates recording them (decided by its `kronikolVersion`, as #192 4.4 decides). `inference`: `PartyResolver.InferenceVersion` when the card inferred a row, null when every row came from the report's block, whose rows the writer resolved under a version the report does not record. Two cards made under different rules are not compared by their hash (below) | |
| `subject` | the subject row (4.2): the `underTest` party that received the most calls, then by name; its name, id and `source` (`declared`, `observed` or `inferred`). Another `underTest` party stays in `parties`, and `exposes` is the subject's alone | |
| `parties` | name, id, kind, `kindSource` (null on an unknown row) and `note` as the parties function gives them, and the set of categories seen. A party some of whose calls carry #190's `testDouble` mark keeps its kind (§5.10); how many were marked is a count, `run.parties[].doubleCalls` | name |
| `edges` | from, to, protocol, and the operations. An edge names its parties; their kinds are on `parties`, joined by name (the issue asks for edges that carry each party's kind) | from, to, protocol |
| an operation | `op` (the first 16 hex of SHA-256 over from, to, protocol, method, target and the GraphQL operation when there is one, joined by U+001F), method, `target` (#192's key, query keys off, without its GraphQL suffix), `graphqlOperation` (#192 §4.2, which names an anonymous operation by its root fields, `query { orderSummaries }`: all 8 of BreakfastProvider's GraphQL POSTs are anonymous, and `TryExtractLabel` reads each as `query`, PEER), and the sets of statuses, origins, causes (left out if #192 declines `cause`) and effective phases | method, target, GraphQL operation |
| `exposes` | the operations received by the subject that are neither deliveries nor user actions; `basis: "tested"` says they are the tested surface, not the service's (FLEET F1). S3's coverage block joins it later | `op` |
| `messages` | `op`, direction (`publishes`, `consumes`, or null for a label the table cannot map), party (the broker), topic (from the URI by the table, 4.2). One row for each topic an operation's calls name: two Service Bus queues at Summarised share one operation and give two rows | party, topic, direction, `op` |
| `scenarios` | `sid`, feature, scenario, tags; `source`: the scenario's own file and line, else its feature's file with a null line, as `ctrf` takes them (`CtrfCommand.cs:139-150` at `8d7d1e1d`), else null; `ops`: the test's own operations in the scenario, the operations of the calls the test party made there, or, with no test party (an in-process subject declared `underTest`, #190 §5.2), the scenario's top-level calls that are not background (Q16). Rows sharing a `sid` are one row; scenarios that are not tests are left out, as `ctrf` leaves them (`CtrfCommand.cs:103`) | `sid` |
| `unresolved` | party names with no id | name |
| `capture` | `calls` (`recorded`, or `notRecorded` when the data file cannot carry calls: a mergeable file written before 3.1.0, `ReportScanner.CarriesInteractions`, `ReportScanner.cs:56-57` at `8d7d1e1d`; a run that made no call is `recorded`, with no edges); `subjectCalls` (`recorded`, or `notRecorded` when no call made by the subject is in the run); `emptyTargets`, the `op`s of a scheme in the message table whose URI holds no destination where the table says one belongs, or only a tracker's placeholder (`kafka:///` at Summarised; `sqs:///`, `sns:///` and `storagequeue:///` when the classifier found no name; `unknown`). A target of `/` on HTTP is a real path, never an empty one: BreakfastProvider has two, `GET /` on the service and the Cosmos DB account read `Other /` (RUN, audit B) | |

Where the scenarios' sources stand on the reports measured: the Reqnroll lanes, Example.Api's Reqnroll report and the
Jest ingest give every scenario its file and line; the xUnit, NUnit and TUnit lanes give none, and `run.suite` is the
only "where" there; LightBDD and BDDfy give none at the scenario, and a step's bare file name, which is no path, is not
taken (RUN, `results/audit-C-source-locations.txt`). No lane measured has a feature file without scenario files, so the
feature fallback adds nothing on them.

Operation ids instead of positions keep a change local: a new operation adds one id and moves no other reference.

**`run`**, the facts of this run:

| Member | Holds | Sorted by |
|---|---|---|
| `id`, `at` | at run end, the run's own, as `Run.json` writes them; from `kronikol card`, a `Run.json` beside the report whose `files` lists it (the check `QueryCommand.cs:505-506` at `8d7d1e1d` makes), else null. A local run's id is salted with the machine's name and the test assembly's directory (`HistoryRunBuilder.cs:221-251` at `8d7d1e1d`), which no report holds | |
| `repository`, `commit`, `branch` | from the report's `ciMetadata`; null off CI | |
| `suite` | the report's `suite`, else null | |
| `framework` | null until a capturer records it (F13, Q12) | |
| `kronikolVersion` | of the build that wrote the card, which for `kronikol card` may differ from the report's | |
| `report` | the data file's name, its `formatVersion`, the `kronikolVersion` that wrote it (the issue's `producer`, folded into `run`: every writer records it, Kronikol4J's too, `ReportDataSerializer.java:59`), and the SHA-256 of its bytes | |
| `environment` | the data file's `environment` (`os`, `runtime`), else null: one commit's operations differ by platform (below) | |
| `results` | scenarios, passed, failed, skipped (`Skipped` and `SkippedAfterFailure`), bypassed, which sum to scenarios, as `query summary` counts them; scenarios that are not tests are not counted (as `Run.json`) | |
| `behaviourHash` | the SHA-256 of the `behaviour` member's bytes as the file holds them (below) | |
| `parties` | per party: `hosts`, the distinct URI authorities of the calls it received, as written (`localhost:15037`), leaving out a scheme whose authority the message table says is a destination (Service Bus, EventBridge); a placeholder a tracker writes there is listed as written (BreakfastProvider's Event Grid writes `topics`), and a party reached only through URIs with no authority (Kafka, Pub/Sub, gRPC, MongoDB, Spanner on BreakfastProvider) has none. `doubleCalls`, how many of the calls it received carry #190's `testDouble` mark, 0 when none. Hosts vary by lane (random ports, container names), so they are run facts | name |
| `counts` | per `op`: calls; `errors`, the calls whose response `InteractionStatus.IsError` reads as an error (a code of 400 or more, or a label that is no success, such as `!TimeoutException` or `Nack`; `InteractionStatus.cs:101` at `8d7d1e1d`), the one classifier behind `services`, `--group-by` and the digest; calls by status; `phases`, calls by effective phase (`setup`, `action`, `unknown`); `derivedPhases`, the phases derived through nesting | `op` |
| `examples` | per `op`: the first call's address in report order, `sN/iM`; null for an operation seen only in the root background block, whose calls have no address | `op` |
| `scenarios` | per `sid`: every operation the scenario's calls reached, the service's and the brokers' included, background calls left out (Q16) | `sid` |
| `degraded` | per kind, how many of the report's diagnostics say the capture lost or misplaced calls: `CaptureDegraded`, `DroppedUnattributed`, `DroppedOutsideRunWindow`, `UnattributedInteractions`, `MalformedLine`, `BackgroundCalls`, `StepAttributionMismatch`, read from the snapshot the data file writes (`reportDiagnostics`, `ReportGenerator.cs:522` at `8d7d1e1d`). Some are timing (`BackgroundCalls` fires when a call lands after its scenario ended), so they are run facts | kind |

**The hashed bytes, and why the file is compact.** `behaviourHash` is the SHA-256 of the `behaviour` member's bytes
exactly as the file holds them. The file is written compact, and `behaviour` is its last member, so those bytes run
from the `{` after the last `"behaviour":` in the file to the byte before the file's closing `}`. With the default
encoder a `"` inside a string is written `\u0022`, so `"behaviour":` cannot occur inside a value. A reader in any
language verifies the hash over that byte range with no JSON serializer; re-serialising (`jq -c .behaviour`, Python's
`json`) changes the escapes and does not give the same bytes. The revision's probe checks the range on all 20 cards it
wrote (RUN, `results/revision-sizes.txt`). Compact is the choice for that reason first, and it is 42.5 to 43.4%
smaller than indented on the in-memory lanes (13.5 to 14.7% once gzipped, as an artifact zip stores it; 2.3). A person
reads it through `jq .`; a git branch of cards (FLEET S5) would keep them pretty-printed if it wants line diffs.

So the file as a whole changes on every run, as `Run.json` does, and `behaviour` changes only when the behaviour does:
on ten same-commit lane pairs it was byte-identical on all ten (2.4). Background calls are in no scenario's operations
(Q6, answered), and a scenario's operations in `behaviour` are the test's own (Q16): what the service did while serving
a scenario can move between scenarios through state they share (a 5-minute menu cache on BreakfastProvider), and is in
`run.scenarios`. An operation seen only in the root background block arrives after its scenario ended, by timing, so
it can come and go between runs as 2.4's Cosmos DB `Replace` does. The blocks of the ten same-commit pairs were empty;
`nunit-in-docker` and `tunit-in-docker` hold 3 calls there in both run 40473765203 and run 40491266394, and their
`behaviour` is the same across that upgrade (RUN, `results/revision-pairs.txt`).

**What moves `behaviourHash` with no change of behaviour**, and how a reader tells it apart:

- **The card's own rules**: the operation key, the consumer's templating rules, the inference. Each changes `rules`
  too, and `kronikol card` rebuilds an older card under the newer rules from its report, while the report is kept.
- **The inference changed once already.** 5.15.0 (#190 R2) made a party every call to which carries `testDouble`
  `double`, `observed`, and chose the subject only among parties of unknown kind; version 1 is that rule. A block a
  5.14.0 run wrote holds rows resolved under the earlier rule, and a newer tool infers an undeclared old report under
  version 1, so two cards of one suite made on either side of 5.15.0 can differ with nothing changed. `rules.inference`
  is null for a card whose rows came from a block; `run.report.kronikolVersion` says which writer resolved them (PEER).
- **A capturer naming a party anew.** 5.13.0 (#190 R0) is one: a call to an unmapped remote host is named by its host,
  `pricing.example.com:443`, where it was `localhost:443`, so the card of a suite that makes such calls moves once
  across that upgrade. The card cannot see the rename; `run.report.kronikolVersion` says which side of 5.13.0 a report
  was written on. A card rebuilt from a report written before 5.13.0 keeps `localhost:443`, the name that report
  recorded, and does not guess the new one from the call's address: what tells the fallback's name apart is not in the
  report (`PARTY_IDENTITY_PLAN.md` §6, R0, says the same of history). It lists `localhost:443` in `unresolved`, as it
  lists every party with no id.
- **The platform.** The same commit on Linux (CI) and on Windows (this machine) differs by one operation,
  `Breakfast Provider > CosmosDB Other /colls/orders` with a `{"query":…}` body: 15 calls in 12 scenarios on Linux,
  none on Windows. All 15 carry `x-ms-cosmos-is-query-plan-request: True`: the Cosmos DB SDK asks the gateway for a
  query plan on Linux and computes it in process on Windows x64 (RUN, `results/revision-pairs.txt`; audit B).
  `run.environment` tells the two apart.
- **Kronikol's own titles.** 5.9.1's "Names as written" (`CHANGELOG.md` L465 at `8d7d1e1d`) renamed 7 of
  BreakfastProvider's Reqnroll feature titles, on 13 scenario rows, under the same `sid`s (`Recipe Summary Via G RPC`
  became `Recipe Summary via gRPC`), which moves the hash of all three Reqnroll lanes across that upgrade. Over run
  40473765203 (5.4.1) against 40491266394 (5.12.1), 13 of 18 lanes kept their hash, and 16 of 18 without the
  scenario index (RUN, `results/revision-pairs.txt`). `run.report.kronikolVersion` tells them apart.
- **A recorded phase.** On two LightBDD lanes across the same upgrade, `Caller > Breakfast Provider POST /graphql`
  (`query { equipmentAlerts }`, `query { batchCompletions }`) recorded no phase on one call in 5.4.1 and `Action` in
  5.12.1, and on the docker lane the subject's SQL call inside it takes it through nesting. Whether the adapter or
  timing changed it is not determined, and it was not seen within one version (RUN, audit B).
- **The tests' own text** (INFERRED): a scenario renamed, a tag added, or a source line moved by an edit above it
  changes `scenarios`, with no change of what the service does.

### 4.4 `kronikol card`

```
kronikol card <report> [-o <file>]
```

- A tool command beside `kronikol ctrf`, not a `query` verb, for `ctrf`'s reason: a budget would truncate a document.
- `<report>` is a `TestRunReport.krkl` or `TestRunReport.json` of any version the scanner reads, or a directory
  holding one, found and gated as `ctrf` finds and gates one (`QueryCommand.ResolveReport`, `ReportGate.Refuse`;
  `CtrfCommand.cs:73`, `:97` at `8d7d1e1d`). The scan maps into `CallRecord`, reading only the request bodies the key
  and the GraphQL rule need (statement-shaped calls and POSTs) through the index's offsets, and the root background
  block's calls from the list #190's R3 adds to the index.
- It takes no declarations. The card is a function of the report, and a declaration on the command line would give
  one report two cards. A report without #190's block gets inferred parties; one without #192's rules block gets the
  built-in rules, with `rules.templateRules` `"none"` or null as the report's version says (4.3). Both say so in the
  card itself.
- `run.id` and `run.at` come from a `Run.json` beside the report that lists it, else null (4.3).
- Writes to stdout without `-o`. `-o` and `--out` as `ctrf` takes them, and a folder that does not exist is created,
  as `ctrf` creates it (`CtrfCommand.cs:115-119` at `8d7d1e1d`). Any other option, the issue's `--json` included, is
  refused with exit 2, as `ctrf` refuses one: the output is always JSON. Exit codes as the tool's: 0; 1 for a runtime
  failure (an unreadable report, a write that fails); 2 for a usage error (no report, an unknown option).
- One pass. A query of one of BreakfastProvider's lanes costs 0.25 to 0.33 s, process start included (PEER, #192's
  timings on four lanes; the largest, `reqnroll-in-docker`, was not timed); a card adds the graph, linear in calls,
  and the bodies the key reads. R1 times the card itself on the largest lane, `reqnroll-in-docker`, in its acceptance
  (5.5), and 5.2.8 holds a budget on synthetic reports.

### 4.5 At run end, and from a merge

- **`ReportConfigurationOptions.GenerateServiceCard`** (Q2 for the default).
- **After the output list, before `Run.json`.** The run-end outputs run in parallel (`RunOutputs`, `Parallel.Invoke`,
  `ReportGenerator.cs:939-951` at `8d7d1e1d`), the data file among them, and its `formatVersion` is known only once its
  payloads are written (F17); the digest's own comment says a sibling cannot know what another wrote (`:607-610`). So
  the card is written after the list (`RunOutputs` at `:694`), after the data file's other name is removed
  (`:697-699`), beside `history?.Append()` (`:703`), inside `RunIsolated` (`:959`) and recorded with
  `RunFileCollector.Record`, as `CiSummary.md` is (`:764-780`), and before `WriteRunManifest` (`:791`).
- **Only with the JSON data file.** It is written when `written` holds this run's JSON data file (`.json` or
  `.krkl`): the card is a function of that file, and with the file off, failed, or written as XML or YAML there is
  nothing for `kronikol card` to agree with. Such a run writes no card and no `serviceCard`. An `OptionNotApplied`
  diagnostic says why when the file is off or not JSON, recorded before the diagnostics snapshot as
  `RecordOptionDiagnostics` records its own (`ReportGenerator.cs:998` at `8d7d1e1d`); a failed write is already an
  `OutputFailure`.
- **Its inputs** are the data file's: the calls the file holds, mapped by the shared rules (4.2), and the parties the
  file holds, the block's rows when the run declared something, else the inference over those calls (Q15). Never the
  table `ReportGenerator` resolved with the run's observations (`ReportGenerator.cs:518` at `8d7d1e1d`). The run end
  maps the calls it holds rather than scanning the file it has just written; it reads the file's bytes once, for the
  hash, and 5.4.4 holds the two readers equal.
- **Ingested runs** get it the way they get any output (2.5): when the option is on by default (Q2). If it is off, an
  ingested run gets a card only through a new ingest flag, which is new surface.
- **`kronikol merge`** writes it too (R2): `MergedRunOutputs.Write` holds every merged call
  (`MergeableReport.Interactions`, already handed to the digest at `MergedRunOutputs.cs:79` at `8d7d1e1d`) and the
  merged parties block, and its options object (`MergeCommand.cs:174-180` at `8d7d1e1d`) gains the switch. A fact holds
  the merge's card equal to `kronikol card` on the merged data file. A merge writes no `Run.json`, so a merged run's
  card is listed in none, and its `run.id` and `run.at` are null.
- **`Run.json`** lists it in `files` as every output, and gains one additive key after `serviceId`, `serviceCard`:
  `{ "file": "service.json", "behaviourHash": "…" }`, written last and only when the card was written;
  `runManifestVersion` stays 1. 5.14.0 writes `serviceId` there when the run declares it (`RunManifest.cs:78-79`,
  `:101-102` at `8d7d1e1d`), so an indexer that fetches `Run.json` first learns which service a declaring run is about,
  and can skip a run whose behaviour it has already seen.
  - The member is a new public record on `RunManifest` (new surface, in R2's minor). `Parse` (`RunManifest.cs:164-194`
    at `8d7d1e1d`) reads it and treats a malformed one as absent, since a null manifest stops pruning and every lookup
    that reads one.
  - The second writer, `RunRotation.Reconstruct` (`RunRotation.cs:491-575` at `8d7d1e1d`), which rebuilds a manifest for
    a run that left none, leaves `serviceCard` out on purpose: it cannot tell this run's card from an older run's
    without hashing the data file. The card itself moves with its run, since `PlannedFiles` lists `service.json`;
    without it, a run killed before its manifest would leave the previous run's card on top.
  - The doc of `RunManifest.ToJson` ("the ten members", `:81`) is corrected with it (section 3, found on the way).
- **The name is fixed**, `service.json`, as `Failures.md`, `ctrf-report.json` and `Run.json` are
  (`ReportGenerator.cs:919-921`, `CtrfReportGenerator.cs:145`, `RunManifest.cs:30` at `8d7d1e1d`); only the report's
  own files take `HtmlTestRunReportFileName` (Q11).

### 4.6 The schema

- `schemas/service-card.v1.schema.json`, committed, with `$id` its raw URL on `main`. The issue asks for a fixed path in
  the repo; this is the first schema committed, so the folder is new (Q13). The card carries no `$schema` member:
  `formatVersion` names its shape.
- Closed, as the data file's schema is: `additionalProperties: false` on every object. An additive key (4.1) joins the
  v1 file on `main` in the release that writes it, so a consumer validating against an older copy rejects a newer
  card, as #190 §5.5 says of the data file's schema; the `$id` on `main` is the copy to validate against.
- Two facts hold it, on the models of the data file's: every card the tests write validates and every member the
  writer can emit is declared (`TestRunReportSchemaContractTests`' pattern), and an undeclared member is a violation
  (`SchemaClosedContractTests`'). The tests read the schema from the repository root, as `SkillDriftTests` reads the
  skill (`SkillDriftTests.cs:29-33` at `8d7d1e1d`), and parse it, never compare its bytes: a Windows checkout has CRLF.

### 4.7 Not in this plan

- `Service.md`, coverage against an OpenAPI or AsyncAPI spec, the fleet index and addresses across reports (FLEET S3 to
  S6), and S7, the cold-agent gate (FLEET §4.7), which needs S5's index. `exposes` keeps a place for S3's block.
- A `graph` verb (FLEET S2). Once the call graph exists it is a projection of a few dozen lines; asked separately (Q9).
- Moving the component diagram, `services` or history onto the call graph. Each would change output; the card pins
  the diagram's edges against the graph instead (5.1), and the rest is Q10.
- Propagating the phase in the identity header. Nesting repairs it for the card; a capture fix is a separate issue
  (Q8).
- Indexing the data file's root background block, and resolving parties over it at run scope: #190's R3 (PEER).
- `behaviourHash` on history's run line (Q17).

## 5. Tests, red first

TDD as `CLAUDE.md` asks. R1's facts are written red against the release before R1, after #190's R3 and #192's R3:
`CallRecord`, `CallGraphBuilder` and the `card` command go in first as stubs that compile and answer nothing, so each
fact fails on its assertion, not on a missing type or on "Unknown command: card". Each red message is read before the
code is written, and every fact changed later is run red again. R2's facts set `GenerateServiceCard`, which goes in
first as an option that does nothing. Facts go through a public entry point where there is one (`kronikol card`
through the tool's `Commands`, the run end through `ReportGenerator`, the merge through `MergedRunOutputs.Write`), and
through the builder for the hand-written records of 5.1.

### 5.1 The call graph (R1)

1. **Origin, cause and phase on the shapes that matter**, from hand-written records: the test calls the subject, which
   calls a dependency inside that request (`request`, the request's phase); the test seeds a database with its own
   client (`test`); the test seeds through the subject's own client between requests (origin `subject`, cause
   `background`: the documented limit, pinned); the subject's background job calls a dependency with no test in the
   stack (`background`, its recorded phase); a background call recorded while an unrelated request is open (it keeps
   its recorded phase, never the request's); a broker delivers to the subject (`RequestHeader`, so `request`); a call
   from the root background block (`background`, in no scenario); a stream with no `attributionSource` (nesting alone:
   `request` inside a received call, `unknown` outside, never `background`).
2. **Parity with the component diagram over the same calls**: for every report the test suites write, the card's edge
   set equals `ExtractRelationships`' (caller, service, protocol) set over the data file's call set with no participant
   filter; and a run with `ComponentDiagramOptions.ParticipantFilter` set filters its diagram, not its card. A mutation
   of either protocol rule fails it.
3. **Parity with #192's key**: each operation's target, with its GraphQL operation, equals `CallOperation.Of` at
   `queryKeys` off (`route`'s setting) for the same call: one key, not two. History keeps query keys, so the parity is
   not with history.
4. **`flow` unchanged**: the existing `flow` facts pass with `CallNesting` moved.
5. **The parties function**: on a report with the block, the rows as written and the subject row; without it, the
   inference over the call set, the root block's calls included; `query parties` prints what it printed before the
   lift on every fixture of its tests; the subject row is the `underTest` party with the most calls received, then by
   name, also when the inference keeps two.
6. **The inference version's guard**: the corpus of call sets with their tables, one per palette category among them;
   a changed answer fails until `InferenceVersion` moves.
7. **The message table**: per scheme, every label its classifier writes, for each operation at each verbosity, maps to
   the direction the table gives; a delivery reads `consumes`; Service Bus's queue and EventBridge's bus come from the
   authority; Kafka at Raw gives the topic, not `{n}@{n}`; a label the table cannot map gives `direction: null`.

### 5.2 `service.json` (R1)

1. **Stability**: two record sets that differ only in call counts, call order, durations and run id give
   byte-identical `behaviour` and equal `behaviourHash`; `run` differs. Then the same with the records shuffled
   within each scenario as parallel steps would. Then two that differ only in a background call moving between
   scenarios, or a downstream call of the subject moving between scenarios (the menu cache of 2.4): `behaviour` equal,
   `run.scenarios` not.
2. **Change is local**: one new operation adds one `op` and changes no other line of `behaviour`.
3. **Culture**: the same card under `cs-CZ`, `da-DK` and `tr-TR` (`CultureRun.Under`), with names that sort differently
   there (`ch`, capitals, `I`).
4. **The issue's fixture**: a declared double, a declared real party, a test client seeding a database, GraphQL POSTs
   with two operations, `/orders/123` and `/orders/456`. Expect: each party's kind; the seeding with origin `test`;
   the subject's calls with origin `subject`; two GraphQL operations; one operation `/orders/{n}`. Then the same
   fixture seeding through the subject's client: the seeding has origin `subject` and cause `background`.
5. **Not recorded is not none**: an external-SUT shaped run gives `capture.subjectCalls: "notRecorded"`; a mergeable
   file written before 3.1.0 (`mergeableFormatVersion` set, `kronikolVersion` 3.0.x, hand-written) gives
   `capture.calls: "notRecorded"`, and a run that made no call gives `recorded` and no edges; a Kafka call at
   Summarised (`kafka:///`) lands in `emptyTargets`, and an HTTP `GET /` and a Service Bus queue at Summarised
   (`servicebus://orders/`) do not.
6. **Null, never guessed**: `framework` null; `source` null on a scenario with none; `run.id` and `run.at` null for a
   report with no `Run.json` beside it; `id` null on a report without declarations; `kindSource` `inferred` on the rows
   whose kind was inferred, null on unknown rows; `rules.templateRules` null for a report written before #192's R2 and
   `"none"` after it when the run had no rules.
7. **Schema**: every card written by the facts above validates; every member the writer can emit is declared; an
   undeclared member is a violation.
8. **Size and cost**: a card on a 2,000-scenario synthetic report, and on one scenario of 1,000 calls (`CallNesting`
   keeps a list of open calls per record, `CallNesting.cs:42-57`), is written in one scan, the bodies the key reads
   included, under a time set from R1's first measurement.
9. **The hash**: for every card the facts write, `behaviourHash` equals the SHA-256 of the bytes from the last
   `"behaviour":` to the file's closing brace.

### 5.3 `kronikol card` (R1)

1. **Writers**: one run written by one build as `TestRunReport.json` (`CompressTestRunReport = false`) and as `.krkl`
   gives equal `behaviour`. Older writers get kept fixtures, each with its own golden card, since writers changed what
   they record between 4.x and 5.x (tags in 5.0.0, names and stable ids in 5.9.x, party names in 5.13.0) and no run is
   written by both: a 4.x standard report made from one NDJSON fixture through `kronikol ingest` of the 4.32.x tool
   (`formatVersion` 2, compressed payloads), with the same NDJSON through the current tool beside it and their
   expected differences listed before either card is written; and an in-process-shaped 4.x fixture with
   `attributionSource`, statement and GraphQL bodies, and a non-empty root background block. The test data holds no
   4.x standard report today (`TestData/Reports/` has the current pin file and two 3.1.0 mergeable files).
2. **One writer**: for a run made in the tests, `kronikol card` on its data file gives the same `behaviour` as the
   run's own builder (R2 makes this the run-end file). The runs include a suite that builds its client with
   `CreateTestTrackingClient` and declares nothing (`inferred` both ways, Q15), one that declares a `ServiceId` (both
   read the block, its `observed` rows included), and one with a non-empty root background block.
3. **Usage**: no report (exit 2), an unknown option such as `--json` (2), an unreadable file (1); `-o` to a folder that
   does not exist creates it and exits 0, as `ctrf` does.
4. **An old name kept**: a report written before 5.13.0 whose call to a remote host was named `localhost:443` by the
   fallback gives a card that keeps `localhost:443` and lists it in `unresolved` (4.3).
5. **The run's id**: `run.id` and `run.at` come from the `Run.json` beside the report when it lists the report, and
   are null otherwise.

### 5.4 The run end and the merge (R2)

1. With the option on, `service.json` is written after the output list and before `Run.json`, listed in `Run.json`,
   and `serviceCard.behaviourHash` equals the file's; `run.report.sha256` equals the data file's on disk. With it off,
   neither.
2. A write that fails leaves the file out of `Run.json` (`StaleOutputTests`' rule); with the data file off or written
   as XML, no card, no `serviceCard`, and an `OptionNotApplied` diagnostic.
3. An ingested run writes it (`kronikol ingest` over an NDJSON fixture) when the option is on by default (Q2).
4. The run-end file's `behaviour` equals `kronikol card`'s on the same run's data file (5.3.2 against the real file),
   for the three runs of 5.3.2.
5. `kronikol merge` of two shards writes `service.json` beside the merged report, equal to `kronikol card` on the
   merged data file.
6. `Run.json`: a new fact on the model of `RunManifestTests.cs:106-119` at `8d7d1e1d` (12 members with both keys,
   `serviceId` then `serviceCard`, round trip, absent when off), with `:89-103` kept as it is; a malformed `serviceCard`
   reads as absent, never as a null manifest; `RunRotation.Reconstruct` leaves it out; `PlannedFiles` lists
   `service.json`, so a run killed before its manifest moves its card with it.
7. The facts a new default changes, run again with the card on: `RunRotationTests.cs:189`, `:405-416`
   (`With_keep_runs_at_zero_the_directory_is_what_it_always_was_plus_the_manifest`, eight files) and `:1052`, and
   `StaleOutputTests`; each gains `service.json` if Q2 is on.
8. **Run-end cost**: on a 2,000-scenario synthetic run, the card adds to the run end no more than a budget set from
   R2's first measurement, beside the data file's own write; with the option off it adds nothing.

No Playwright: nothing here changes the HTML report.

### 5.5 Acceptance, on real reports

- On BreakfastProvider's four lanes, after R2: the run-end file's `behaviour` equals `kronikol card`'s on the same
  data file, and the external-SUT lane says `subjectCalls: "notRecorded"`.
- On two runs of one lane of the same commit whose scenarios all had the same result: `behaviour` byte-identical, as
  measured on 10 of 10 pairs (2.4); `run.counts` may differ.
- On the ingested runs of A3, kronikol-bf's Jest stand-in and a Cucumber Messages run built in R1's tests, on a tap
  capture (ProxyTap or TcpTap) and on a `kronikol merge` of shards: the card is written. Where a producer records no
  `attributionSource` (ingest, the taps), `cause` and `effectivePhases` come from nesting alone, and nesting is only as
  good as the producer's order (ingest and Kronikol4J's HTTP adapters reorder records, `FLOW_NESTING_PLAN.md`).
  `effectivePhases` reports what was recorded: unknown throughout on the stand-in, which records no phase, and Action
  throughout on a run recorded as the issue's Jest report was. Where the stream names the service as the caller of its
  own calls, as the stand-in does, `origin` separates them from the test's. Where it names the test as the caller of
  every call, only a declaration settles it (#190 §5.2): undeclared, the card infers the dependency as the subject and
  the calls as the test's, every row labelled `inferred`, and the acceptance pins that limit; the same stream with the
  participant declared `underTest` through ingest's `--parties` (#190 R2) reads its calls as the subject's.
- Run cold, the indexer's question: from `Run.json` and `service.json` alone, list BreakfastProvider's dependencies
  used while serving requests (`cause` `request`), and those used only outside requests (`cause` other than
  `request`). Compared by hand with `flow` on five scenarios.
- Timed: `kronikol card` on `reqnroll-in-docker` (14.1 MB as JSON), and the run end of `xunit-in-memory` with the
  card on and off. The numbers go in the release's notes and set the budgets of 5.2.8 and 5.4.8.

### 5.6 Mutations

Each named mutation must fail at least one fact; one that survives means a fixture never reaches its case:

- the recorded phase used for the effective phase, or a background call's phase taken from its parent;
- nesting's trace clause dropped from `CallNesting`'s rule;
- a culture-sensitive sort, or a list left unsorted;
- a count written into `behaviour`;
- background calls kept in a scenario's operations, or the root background block skipped;
- the run's observations used at run end, or the block ignored by the tool;
- the subject row taken first by name instead of by calls received;
- `GetProtocol` copied instead of shared;
- the empty Guid kept as an id at run end;
- the card written inside the parallel output list;
- a label the message table cannot map written as `publishes`, or the destination taken from the target's last
  segment;
- `emptyTargets` keyed on a target of `/`;
- `InferenceVersion` left where it is after a change to the inference's answers;
- `templateRules` written `"none"` for a report written before #192's R2;
- `capture.calls` keyed on "no call at all" instead of `CarriesInteractions`;
- `behaviourHash` taken over re-serialised bytes instead of the file's.

## 6. Releases and records

### 6.1 Releases

| Release | Holds | Bump | Waits for |
|---|---|---|---|
| **R1** | `CallGraph/` (with `CallNesting` moved and the message table), the shared parties function and `InferenceVersion` in `src/Kronikol/Parties/` (`query parties` calls the function), the card writer, `kronikol card`, the committed schema, docs | **minor**: a new command | #190's R2 and R3 (planned 5.15.0 and 5.16.0: version 1 of the inference is R2's rule, and R3 indexes the root background block and resolves parties at run scope); #192's R1 (the key), R2 (the rules block) and R3 (`origin`, and `cause` if its Q4 says so; if not, R1 keeps the graph's own classification and leaves `causes` out) |
| **R2** | `GenerateServiceCard`, the run-end file after the output list, `Run.json`'s `serviceCard` key with its reader and the rotation's reconstruction, `PlannedFiles`, the card from `kronikol merge`, and an ingest flag only if Q2 is off | **minor**: a new option and a new output | R1 |

R1 changes no report bytes and no golden pin: `query parties` prints what it printed (5.1.5). R2 adds a file, and with
Q2 on it adds `service.json` to the files `RunRotationTests` pins; the report itself is unchanged. If the owner wants
the run-end file in the first release, R1 and R2 merge into one minor.

### 6.2 Changelog drafts

- **R1**: "`kronikol card <report>` writes `service.json`: the run's parties, the edges between them with each
  operation's method, templated target, statuses, origins, causes and effective phases, each scenario's own
  operations, the messages the service publishes and consumes, and what the capture could not see; its `run` part
  holds the counts, errors, example addresses, hosts and every operation each scenario reached. Its `behaviour` part
  is byte-identical when the run's behaviour is, and its SHA-256 is `behaviourHash`, over the bytes the file holds.
  Works on any report the tool reads, 4.x JSON or 5.x `.krkl`; parties come from the report's block, else from the
  inference over its calls, never from what the run observed and did not write. The schema is
  `schemas/service-card.v1.schema.json`. A minor: a new command."
- **R2**: "`GenerateServiceCard` writes `service.json` at run end, after the report and before `Run.json`, which lists
  it and gains `serviceCard.behaviourHash`; `kronikol merge` writes it beside a merged report. A minor: a new option
  and a new output."

### 6.3 Wiki (`../Kronikol.wiki`, `origin/master` at `183dbb0`, 5.14.0)

- A new `Service-Card` page: what the file holds; what is stable, and what moves the hash with no change of behaviour
  (4.3); what `origin`, `cause` and `capture` mean, with their limits (seeding through the service's clients reads
  `background`; an undeclared in-process subject reads as the test); how an indexer uses `behaviourHash` and verifies
  it; what an inferred row means, and why a run's observed test client reaches the card only when the run declares
  something.
- `Generated-Reports.md`: a row in the file table (beside `Failures.md`, L16, and `Run.json`, L23), `Run.json`'s new
  key (its example at L541-552), and the merged run (L537).
- `Querying-Reports.md`: `kronikol card` beside `kronikol ctrf`.
- `Report-Configuration.md`: `GenerateServiceCard` (R2); its Parties section (5.14.0) links the card.
- `Cross-Run-History.md`: `HistoryShapeTemplates` now also shapes the card's targets (after #192).
- `CI-Artifact-Upload.md`, `Merging-Parallel-Reports.md`, `Ingesting-External-Captures.md`,
  `Exporting-to-OpenTelemetry.md`, `Diagnostics-and-Debugging.md` and `Migrating-to-v5-Tags.md`, which list the run-end
  files or the tool's commands (audit C's `git grep` of `kronikol ctrf`, `Run.json` and `ctrf-report.json`).
- `_Sidebar.md` and `Home.md`: the new page.
- Grep the wiki for every page that lists the run-end files or the tool's commands again before each release.
- Outside the wiki: `README.md:223`, `nuget-readme.md:55` and `:79`, and the tool's package description and tags
  (`Kronikol.Tool.csproj:22`, `:24`) list the tool's commands.

### 6.4 Plans, roadmap, Kronikol4J, skill, issues

- `PLANS_STATUS.md`: this plan's row.
- `ROADMAP.md`: the card is row 14.13 (FLEET S4) and D23 is still open. #191 asks for part of S4 now, for the owner's
  own company, which is D23's question; placing R1 and R2 is Q1. When R2 ships, an Appendix C row for what this plan
  leaves: Q7's Kronikol4J issue, and the questions answered "not here" (Q8, Q9, Q10, Q12, Q17).
- `FLEET_EVIDENCE_PLAN.md` §4.4: point S4's `service.json` here; its 8 KB is `Service.md`'s budget, not this file's.
- Kronikol4J (`docs/REMAINING_PARITY.md`), a line per release, as the port keeps one for every .NET release, the ones
  it does not mirror included: R1, "a tool command; a Java run gets a card only from a standalone run with
  `kronikol.report.dataFormats=json`, and that card has no repository, commit, branch, suite, format version or
  `cause` from attribution (F11)"; R2, "a run-end file the port does not write: not mirrored; a Java run's card comes
  from `kronikol card`" (Q7).
- Both skill copies (`.claude/skills/kronikol-test-debugging/references/commands.md` and
  `templates/skills/kronikol-test-debugging/references/commands.md`): `kronikol card` in a `##` section in prose, as
  `kronikol decode` has one (`commands.md:726` at `8d7d1e1d`). A `###` heading must be a query verb
  (`SkillDriftTests.cs:109-119`), every `--flag` in a code context goes through the query parser (`:126-158`), so the
  section names `-o` and never `--out` in code, and the two copies stay identical (`:414`).
- Issues: #184's third ask (`caller` on `interactions --json` rows) is #192's R2; #191 is closed when R2 ships
  (`gh issue close`), or when R1 does if the owner declines R2.

## 7. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Where in the roadmap: now, for the owner's company (D23's "sooner"), or after the launch as 14.13 | Now, R1 first: it is tool-only, changes no report and no pin, and serves every report already written. It waits for #190's R2 and R3 and #192's three releases (6.1). R2 when the owner wants the file at run end |
| Q2 | `GenerateServiceCard` on by default | On, like `Failures.md` and as D23 recommends for the card: an indexer is only as current as the runs that write the file. The cost is one file of 159 to 178 KB a run on BreakfastProvider's in-process lanes (23 to 25 KB gzipped, as an artifact zip stores it) and 88 to 102 KB on its external-SUT lanes (RUN, 2.3), and the graph's build at every run end (budgeted in R2, 5.4.8). On also decides ingest: `kronikol ingest` has no flag for an output that is off by default, so off means no card for ingested runs unless R2 adds one |
| Q3 | One file with `run` and `behaviour`, or two files | One file. The issue asks for one, the indexer compares `behaviourHash`, and the hashed bytes are the `behaviour` member as the file holds them (4.3); `jq .behaviour` re-escapes and is for reading, not for hashing. Two files would let a git branch of cards diff cleanly, which FLEET S5 may want later |
| Q4 | Counts in the card at all | Yes, in `run`. They are the issue's ask and cost the stable part nothing there |
| Q5 | `cause` on the card (#192 asks it too, its section 9 Q4, with this card as its second reason) | Yes. Without it the card cannot say which dependencies are used while serving requests; "only outside requests" is `cause` other than `request`. It cannot tell background work from seeding through the service's own clients (both `TestContext`, both top level, #192 4.3), and the docs say so. If #192's Q4 is declined, the graph keeps the classification it needs for the scenario index and the phase rule (4.2), and the card leaves `causes` out |
| Q6 | Background calls in a scenario's `ops` | **Answered (2026-10-27, audit B): no.** Three of the five scenario rows that changed between same-commit runs were background calls (2.4); the operation stays on its edge. The rest of the trade is Q16 |
| Q7 | Kronikol4J: port the writer, or `kronikol card` in Java CI | The tool, until a port asks: one implementation keeps one format. A Java card needs a standalone run with `kronikol.report.dataFormats=json`, and even then lacks `ciMetadata`, `suite`, `formatVersion` and `attributionSource` (F11). Recommend one Kronikol4J issue, raised when R1 ships: calls in fragments and a merged data file, and those root members and `attributionSource` on calls |
| Q8 | Fix phase at capture (carry it in the identity header) | Not here. Nesting repairs it for every reader of the graph. A capture fix changes the wire and every port; a separate issue if wanted |
| Q9 | A `query graph` verb now | Not in this plan. Cheap once the graph exists; ask when an agent needs it (FLEET S2) |
| Q10 | Move the component diagram, `services` and history onto the graph | No: each changes output. The card pins the diagram's edges; the others stay as they are until a defect makes the move worth a pin change |
| Q11 | The file's name under a custom `HtmlTestRunReportFileName` | `service.json` always, as `Failures.md`, `ctrf-report.json` and `Run.json` are fixed (4.5): an indexer looks for one name. A suite that writes two reports into one folder is already a problem `Run.json` has |
| Q12 | `run.framework` | Null now. It needs each capturer to record its framework, which is the contract question at 14.5 (`GO_PLATFORM_PLAN`'s ask of the raw test name with its framework). Record it there |
| Q13 | Where committed schemas live | `schemas/` at the repo root, one file per format version, raw URL as `$id`; the card carries no `$schema` member |
| Q14 | The command's name | `kronikol card`, the issue's. It is short and pairs with `ctrf`; the wiki page says "service card" in full. Its options are `ctrf`'s, `-o` and `--out`; the issue's `--json` is refused, since the output is always JSON |
| Q15 | What a run observed and did not write. At run end Kronikol knows which client `CreateTestTrackingClient` built (`observed`, with the application as `note`), and the data file records it only when the run also declares something (F15). (a) The card is a function of the data file: built at run end from the block when there is one, else from the inference over the calls, as `kronikol card` and `query parties` build it from the same file, so an undeclared run's card says `inferred` where the run observed. (b) #190 writes the block whenever a test client was observed: every such suite's report changes bytes, which #190 §5.14 declined as "a block on every run". (c) The run-end card uses what it observed and the tool what the file holds: one report then has two cards, and an indexer mixing run-end and rebuilt cards sees a change that is none | (a). One writer, two readers holds, and a card rebuilt from a kept report agrees with the one its run wrote. A suite that wants `observed` on its card declares its `ServiceId`, which writes the block, observed rows included |
| Q16 | What `behaviour.scenarios[].ops` holds. (a) The test's own operations in each scenario, with the full list, the service's included, in `run.scenarios`; (b) every call but background ones (Q6's answer alone); (c) every call, as first written; (d) (a), with titles, tags and source moved to `run` beside the full list. Measured (2.4, RUN): same-commit pairs byte-identical on 10 of 10 (a), 8 of 10 (b), 6 of 10 (c); across 5.4.1 to 5.12.1 on 18 lanes, 13 of 18 (a) and 16 of 18 (d), the difference being 5.9.1's renamed titles | (a). It is the only reading that held on every pair. Its cost: which scenarios reach a dependency (Supplier Service, through the menu cache) is answered from `run.scenarios`, which is not hashed. (d) costs a reader of `behaviour` alone the scenario's name |
| Q17 | `behaviourHash` on history's run line. 5.14.0 put `serviceId` there, and the ledger is where an indexer reads many runs at once | Not in this plan. History's own fingerprints already say, scenario by scenario, when behaviour changed; the card's hash would be a second, coarser signal on a format suites on many versions share. Ask again with FLEET S5, which reads the history branches |

## 8. Assumption ledger

| # | Statement | Basis | If wrong |
|---|---|---|---|
| A1 | BreakfastProvider's lanes are representative of in-process .NET suites | RUN on one consumer | The rates in 2.4 move; the design does not, since it promises nothing for counts |
| A2 | The service's calls outside any received call are seeding in suites that seed through the container | INFERRED from 2.2; not observed on BreakfastProvider | `cause` reads `background` for both (4.2), and says what is known; 5.2.4's second fixture pins what the card prints |
| A3 | The three reports in the issue behave like BreakfastProvider's lanes | ISSUE. Searched for on 2026-10-27 and not found: every file under `C:/Users/cex` (PEER, the list in #192's session scratchpad, `found-reports-profile.txt`), C: (the only drive), `C:/Code` outside Kronikol's worktrees; the session transcripts show the issues were written on another machine. Stand-ins measured instead (2.3): four more BreakfastProvider lanes and a Jest ingest (RUN on PEER-made inputs). A Kronikol4J stand-in could not be made with calls (F11) | R1's acceptance adds, in its own tests, a Jest-shaped run as the issue describes it (one synthetic `Run:` step, every call Action, MSW-style doubles answering in 1 to 2 ms) and a Cucumber Messages run, both from hand-written NDJSON through `kronikol ingest` as #184's repro was, and pins them like the BreakfastProvider lanes |
| A4 | #192's key and cause, and #190's inference, are pure functions of what a report holds | PEER, agreed by message. True of the inference; the run end's resolver also reads observations (`PartiesReport.cs:18-25` at `8d7d1e1d`), which the card does not read (Q15) | R1 waits, or the card gets a field that differs between run end and backfill; 5.3.2 catches it |
| A5 | `ReportScanner` can give the card every field it needs | READ: `attributionSource` and `callerDependencyCategory` read since 5.14.0 (`ReportScanner.cs:834-835` at `8d7d1e1d`); the root `background` block not read until #190's R3 (F16, PEER) | R1 teaches the scanner the block itself, in a list of its own on `ReportIndex`, so the existing verbs keep their output |
| A6 | 16 hex of SHA-256 is enough for an operation id | INFERRED: about 200 operations a run | A collision would merge two operations; a fact over the corpus checks there is none |
| A7 | Ingested runs reach the run-end list | READ (`IngestPipeline.cs:694`), for an output on by default (`IngestPipeline.cs:428-435` at `8d7d1e1d`) | With Q2 off, ingest needs a flag (4.5); 5.4.3 catches it |
| A8 | Card sizes scale with scenarios and operations as measured | RUN on 187 to 214 scenarios, in the revised shape (2.3) | Q2's cost line moves; 5.2.8 measures a large run |
| A9 | #190's R3 indexes the root background block and resolves parties at run scope | PEER, agreed by message on 2026-10-27 | R1 does it (A5's "if wrong"), and #190's three found-on-the-way defects stay open until someone fixes them |
| A10 | Ten pairs say how often `behaviour` moves on a re-run | RUN: one consumer, ten pairs, Linux CI and one Windows machine | It moves more often than 2.4 says; the design promises presence only, and `run` takes what moves |

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
- 2026-10-27: after #192's revision. Its key now also splits SQL batches behind a `SET` prologue, query bodies sent as
  documents and GraphQL operations (179 operations on `xunit-in-memory`, cited in 0). Two sentences here said more than
  was measured and were cut back: the spread of background calls between frameworks is not section 2.4's change between
  runs, and the cause of the scenario index's churn is named by the code, not measured (both now point at Q6).
- 2026-10-27: rebased onto 5.13.0 (`2b74a2e4`), which released #190's R0 as a minor and put `PARTY_IDENTITY_PLAN.md`
  on `main`. The sections of it cited here were checked against the committed file and are unchanged. Corrected: R0 is
  a minor, not a patch (4.0); the fingerprint version is 7 since R0, so #192's key moves it to 8 (4.0, the example's
  `rules`). Added: what moves `behaviourHash` with no change of behaviour, R0's rename among them (4.3), and a test
  that a card keeps the name its report recorded (5.3.4).
- 2026-10-27: rebased onto 5.14.0 (`8d7d1e1d`), which released #190's R1: the resolver in `src/Kronikol/Parties/`
  (internal: `PartiesReport.Resolve`, `PartyResolver.Resolve`), the block (`serviceId`, and `parties` rows with `name`
  and `kind` always and `id`, `kindSource` and `note` when known), `serviceId` in `Run.json` and on the run line, and
  `kronikol query parties`. The card can rely on all of it as 4.0 and 4.3 say. Corrected: a subject's `source` can be
  `observed` (a tracking client made from a `WebApplicationFactory`), and as R1 can mark more than one party
  `underTest`, the card's `subject` is R1's subject row (4.3).
- 2026-10-27: audited by three reviews in this session, and revised to them and to the decisions taken on them. A,
  accuracy: every number and READ citation against `44bcc62a` and `8d7d1e1d` (15 findings that change the design, 21
  of wording). B, deep dives: the card's stability on ten real same-commit lane pairs, Q6, 5.14.0's resolver on 52
  reports, the card's sizes. C, completeness: the issue point by point, backfill, the resolver, `Run.json`,
  Kronikol4J, the edit sites, the repo's rules. Their scripts and small results are in the harness (`audit-A-`,
  `audit-B-`, `audit-C-`), with this revision's probe of the revised card (`revision-`). What changed:
  - the card is a function of the data file alone, at run end too, so what a run observed and did not write never
    reaches it (4.1, 4.5, F15, Q15); it is written after the output list and before `Run.json`, and only with the JSON
    data file (4.5, F17);
  - background calls come from the data file's root block, which #190's R3 (planned 5.16.0) indexes; they sit on edges
    and operations with `cause` `background`, in no scenario's operations, with no example address, and keep their
    recorded phase as a rule (4.2, 4.3, F16); Q6 answered from audit B's pairs, and `behaviour.scenarios[].ops` holds
    the test's own operations, the full list moving to `run` (2.4, Q16);
  - `cause` waits on #192's Q4, with a fallback; the graph's classification written out, deliveries and a subject's
    `TestContext` calls included (4.2, Q5); the message rule replaced by a table per URI scheme (4.2, F18);
  - `kronikol card` takes no declarations, reads `run.id` and `run.at` from a `Run.json` beside the report, creates
    `-o`'s folder and refuses `--json` (4.4, 5.3.3, 5.3.4); `rules.templateRules` and `rules.inference` defined, the
    shared parties function and `InferenceVersion` in this plan's R1, agreed with kronikol-67 (4.2, 4.3);
  - hosts, errors and phase counts placed in `run`, `capture.degraded` moved there as `run.degraded`,
    `results.bypassed`, `run.report.kronikolVersion` and `run.environment` added, `kindSource` null on unknown rows,
    `capture.calls` keyed on `CarriesInteractions`, `capture.emptyTargets` defined by the message table (4.3); the
    hashed bytes defined and the file written compact (4.3);
  - what moves `behaviourHash` with no change of behaviour: the 5.15.0 inference, the platform, 5.9.1's titles, a
    recorded phase, the tests' own text (4.3, F19); sizes measured again in the revised shape on the 18 lanes of run
    40491266394 (2.3, F10, Q2);
  - the merge writes the card, ingest only with Q2 on; `Run.json`'s reader, second writer and pins, `PlannedFiles`,
    the rotation pins, a run-end cost budget, the TDD wording, the mutations, the docs, the skills and the records
    added (4.5, 5, 6);
    Appendix A refreshed at `8d7d1e1d`;
  - corrected: the 179 operations' explanation (184 with #192's head fixes; that lane has no `SET` batch, against the
    entry above), the timings (PEER), Jest's phase (claim 10), the sizes, the edge parity's call set (5.1.2), the
    fixtures across writers (5.3.1), the name rule (4.5 against Q11), and 5.14.0's scanner, which reads
    `attributionSource` (2.2, A5). Q15 to Q17 added; three 5.14.0 defects recorded for kronikol-67 (section 3, found on
    the way). No section, finding, question or test number another plan cites moved; `capture.emptyTargets` keeps its
    name with the narrower meaning.

## Appendix A. Edit sites at `8d7d1e1d`

Line numbers here are at `8d7d1e1d` (5.14.0); the READ citations in the rest of the plan stay at `44bcc62a` unless
they say otherwise. #190's R2 and R3 and #192's releases come first and will move some of them; each release reads its
sites again.

| Release | File | Change |
|---|---|---|
| R1 | `src/Kronikol/CallGraph/` (new) | `CallRecord`, `CallGraph`, `CallGraphBuilder`, the nesting rule moved here, the message table, the mapping rules both readers share |
| R1 | `src/Kronikol/Query/CallNesting.cs` (unchanged since `44bcc62a`: `Parents` L32, the rule L62-76) | delegates to the moved rule; its doc (L3-25, "The rule lives here alone") changes. Callers: `QueryCommand.Narrative.cs:674`, `tests/Kronikol.Tests/Tool/CallNestingTests.cs:215`, `FlowTreeTests.cs:140` |
| R1 | `src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs:534` (`GetProtocol`, private, takes a `RequestResponseLog`) | split to take the category, the meta type and the method, and shared with the graph; `DrawnCalls` (L75-90) is the filter the run-end mapping shares; `ExtractRelationships` L54 |
| R1 | `src/Kronikol/Parties/PartyResolver.cs:38` (`PartyTable`), `:97-99` (the subject row, computed and dropped), `:197-234` (`InferTests`, `InferSubjects`) | `PartyTable` gains its subject; `InferenceVersion` beside the inference |
| R1 | `src/Kronikol/Parties/` (a new function) and `src/Kronikol/Query/QueryCommand.Overview.cs:466-566` (`Parties`: the block-or-infer branch at L504-523, its doc at L460-465) | the branch lifted into one function that returns the table and its subject row; `query parties` calls it |
| R1 | `src/Kronikol/Parties/PartiesReport.cs:13-25` | its doc says the run end's table is the report's when declared; the card calls the shared function, never `Resolve` with observations (Q15) |
| R1 | `src/Kronikol/Query/ReportScanner.cs`, `ReportIndex.cs` | none here: 5.14.0 reads `attributionSource` (L835), and #190's R3 indexes the root background block (`Enter` L426-478, `Leave` L480-541, `KnownNames` L322-347; `ReportIndex.cs:82-95`). The card reads that list |
| R1 | `src/Kronikol/Reports/ServiceCardWriter.cs` (new) | the one writer |
| R1 | `src/Kronikol/Reports/InteractionStatus.cs:29`, `:62`, `:101`; `src/Kronikol/History/HistoryShapeRules.cs:70` | one status reader per side, the error rule; `rules.templateRules` |
| R1 | `src/Kronikol.Tool/CardCommand.cs` (new), `src/Kronikol.Tool/Commands.cs` (`Table` L24-50, `ctrf` L41-43) | the command, beside `ctrf`; it takes and gates a report as `CtrfCommand.cs:73`, `:97` do (`QueryCommand.ResolveReport`, `QueryCommand.cs:443`; `ReportGate.Refuse`, `ReportGate.cs:29`) and reads `Run.json` as `QueryCommand.cs:505-506` does |
| R1 | `src/Kronikol.Tool/Kronikol.Tool.csproj:22` (the description lists every command), `:24` (tags); `README.md:223`; `nuget-readme.md:55`, `:79` | `kronikol card` |
| R1 | `schemas/service-card.v1.schema.json` (new) | the schema; no schema is committed anywhere yet |
| R1 | both skill copies, `references/commands.md` | `kronikol card` in a `##` section in prose (6.4) |
| R1 | `tests/Kronikol.Tests/Compression/KrklLookupTests.cs:161`, `:310`, `:371` | the readers that must find a `.krkl` gain `card` |
| R1 | `tests/Kronikol.Tests/Tool/QueryCultureTests.cs:134` | `card` scans outside the query's invariant culture scope, as `ctrf` does |
| R1 | `tests/Kronikol.Tests/Tool/CtrfCommandTests.cs` | the model for `CardCommandTests` |
| R1 | `tests/Kronikol.Tests/Tool/SkillDriftTests.cs:109-119`, `:126-158`, `:414` | what the skill copies may say about `card` (6.4) |
| R2 | `src/Kronikol/ReportConfigurationOptions.cs` (beside `GenerateFailuresDigest` L365 or `GenerateCtrfReport` L400) | `GenerateServiceCard` |
| R2 | `src/Kronikol/Reports/ReportGenerator.cs:694` (`RunOutputs`), `:697-703`, `:764-780` (`CiSummary.md`'s isolation and record), `:959` (`RunIsolated`), `:998` (`RecordOptionDiagnostics`) | the card after the output list, before `Run.json`, only with the JSON data file |
| R2 | `src/Kronikol/Reports/ReportGenerator.cs:791-802` (`WriteRunManifest`) | `serviceCard` set on the manifest |
| R2 | `src/Kronikol/Reports/ReportGenerator.cs:852-895` (`PlannedFiles`) | lists `service.json` |
| R2 | `src/Kronikol/Reports/RunManifest.cs:78-82`, `:101-102`, `:164-194` | the `serviceCard` member after `serviceId`, written last when set, read in `Parse` (a malformed one read as absent); the doc at L81 |
| R2 | `src/Kronikol/Reports/RunRotation.cs:491-575` (`Reconstruct`) | leaves `serviceCard` out on purpose, with a fact |
| R2 | `src/Kronikol/Reports/Merge/MergedRunOutputs.cs:37-198`; `src/Kronikol.Tool/MergeCommand.cs:174-180` | the merge's card |
| R2 | `src/Kronikol/Ingestion/IngestPipeline.cs:428-435`; `src/Kronikol.Tool/IngestCommand.cs` | nothing if Q2 is on; an ingest flag if it is off |
| R2 | `tests/Kronikol.Tests/Reports/RunManifestTests.cs:89-119`; `RunRotationTests.cs:189`, `:405-416`, `:1052`; `StaleOutputTests.cs` | the pins (5.4) |
| R2 | Wiki `Generated-Reports.md:16-23`, `:537`, `:541-552`, and the pages of 6.3 | 6.3 |
