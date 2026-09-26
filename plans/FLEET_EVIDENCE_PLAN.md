# Fleet evidence: every service's test run as queryable metadata for an LLM

**Date:** 2026-09-26 · **Repo version:** 3.31.2 (`2d5c8f42`, origin/main) · **Status: written, nothing
implemented, NOT green-lit; needs D23.** Roadmap items **14.11 to 14.15**; S2 (the `graph` verb) may be
pulled forward by rule 8. §9 is the assumption ledger. Nothing here has a harness yet; §7.5 says what the
first script must measure before S1 starts.

The owner asked on 2026-09-26 how Kronikol should sit in an architecture where every service in a company
(C#, JavaScript, Java or Python) publishes its `TestRunReport.json` as queryable metadata with high-level
summaries, and an LLM deciding a feature reads a company-level description, then a service-level one,
then queries the report, the OpenAPI and AsyncAPI specs and the generated diagrams of the services it
touches. The question was where Kronikol fits, how to build the whole so the model makes the right change
in the right place for the fewest tokens, and what that puts on the roadmap. This plan is the answer, with
each claim marked by how far it was checked.

What it adds to the idea, in one paragraph. The architecture has three layers with different truth
values: **intent** (the specs, a line of purpose and ownership), **evidence** (what each service did under
test, in named scenarios, with bodies) and **navigation** (which service, who owns it, who talks to
whom). Kronikol owns the evidence layer outright and should emit the derived half of navigation, as
files, and own nothing else: no catalogue, no host, no spec registry. Two things the report does not carry
today would make the evidence lie to a model, and both are format fields: **which party is the service
under test, and whether each other party was the real thing or a test double** (F2), and **a service
identity that joins across reports** (F3). Those go into the cross-language contract before it freezes
(roadmap rule 6, 14.5). Everything else is tooling on top and can wait for the launch.

---

## 0. Summary

`kronikol query` already answers questions about one run under a byte budget and ends every answer with
the addresses that fetch the next thing (READ: `VerbTable.cs`, twenty verbs, `--max-bytes` default 6,000).
The fleet is the same ladder one level up: a company map that ends in service cards, cards that end in
queries, queries that end in a body or a source location. A feature decision that touches three services
should cost the model roughly one map, three cards and six or eight queries, on the order of 15 to 20K
tokens (INFERRED, §4.6). Opening one report costs 2.7M (the skill's measured figure).

| | Today | After this plan |
|---|---|---|
| Who the report is about | inferred: `FixedNameForReceivingService`, a port map, the test assembly (`RunSuite`) | declared: `ServiceId`, joined across reports |
| Whether a party's answer was the real service's | not recorded; `capturedBy` says wire or span, never real or double | `Parties[].Kind`: `test`, `underTest`, `real`, `double`, `unknown`; the card says which evidence is which |
| The tested surface against the spec | nothing reads a spec (RUN: no `openapi`, `asyncapi` or `swagger` in `src/`) | `coverage`: exercised, unexercised, unspecified, undeclared statuses |
| The graph as data | `ComponentDiagram.html`; `diagram` prints PlantUML source; the ledger holds `caller>service` pairs (READ) | `graph` prints participants and edges in 1 to 2 KB |
| A per-service summary | `Failures.md`, `CLAUDE.md`, `AGENTS.md`, per run | `Service.md` and `service.json`, per run, byte-stable when behaviour is |
| Across services | nothing | `kronikol fleet index`, `who-handles`, `graph`; `Fleet.md` and `fleet.json`; addresses with a service locator |

---

## 1. How far each claim was checked

READ: the source or plan was read today. RUN: a command was executed today. PLAN: another plan says so
and it was not re-checked. INFERRED: reasoned from two facts, stated by neither. ASSUMED: not checked;
each is a row in §9.

---

## 2. What exists today (READ, RUN)

### 2.1 The report knows its run, not its service

- `CiMetadata` records `BuildNumber`, `Branch`, `CommitSha`, `PipelineUrl`, `Repository`, `RunId` and
  `RunAttempt`, read from `GITHUB_SHA`, `BUILD_SOURCEVERSION` and their siblings
  (`src/Kronikol/Reports/CiMetadata.cs`). The history ledger's run line carries `Suite`, `Branch`,
  `Commit`, `Provider`, `Url` and `Deps` (`HistoryRunBuilder.cs:131`, `:148`). So a fleet snapshot can be
  stated as (suite, commit) pairs today.
- The suite is resolved from the test assembly, because "nothing on the model carried a suite"
  (`RunSuite.cs` doc comment). `SuiteName` overrides it. It names the *test project*, not the service.
- The receiving service is a display name: `FixedNameForReceivingService`, else inferred from the port
  through `PortsToServiceNames` (`TestTrackingMessageHandler.cs:60`); `ClientNamesToServiceNames` and
  `CallerName` (default `"Caller"`) name the other parties (`TestTrackingMessageHandlerOptions.cs`).
  `ServiceTypeOverrides` maps a display name to a dependency category (`"CosmosDB"`, `"Redis"`), which is
  the one place today where a name is given a property: the precedent for §4.1.
- `DependencyType` has eight technology kinds: `HttpApi`, `Database`, `Cache`, `MessageQueue`, `Storage`,
  `AI`, `User`, `Unknown`. It says what a party *is*, never whether it was *real*.

### 2.2 What an interaction carries

`InteractionRecord` (`src/Kronikol/Ingestion/InteractionRecord.cs`): `type method uri serviceName
callerName content headers statusCode traceId requestResponseId timestamp testId testName
dependencyCategory callerDependencyCategory phase metaType kind durationMs text keyword table docString
passed message markerKind plantUml markerEnd activityTraceId activitySpanId trackingIgnore capturedBy key
value`. Nulls are omitted (`JsonIgnoreCondition.WhenWritingNull`, line 31), so an optional field that is
never set changes no existing byte. `capturedBy` is `"wire"`, `"span"` or `"wire + span"`
(`InteractionMerger.cs:29-35`), a statement about the capturer, not about the party.

### 2.3 Edges exist as display names

`InteractionShape.Dependencies` returns the sorted distinct `caller>service` pairs of a run
(`History/InteractionShape.cs`), written to the ledger and to `DASHBOARD_PLAN`'s `history.view.json` as
`deps` (`"Caller>Breakfast Provider"`). A fleet graph can be read from the history branches without
opening a report, once the names join (F3).

### 2.4 Where source locations are

Only Cucumber Messages carry a feature file and line (`Ingestion/Cucumber/CucumberMessages.cs`:
`location`, `line`, `fileName`). The .NET adapters record a test id and name; `Failures.md`'s "source
location" is the frame that threw (`LLM_FIRST_PLAN` M7, the `repro` verb). So "where the tests for this
flow live" is exact on a failure and on an ingested Cucumber run, and otherwise a namespace read from the
test id. #76's second half (Gherkin step locations, roadmap 0.1 and 7.1) is the same gap seen from the
PR-delta side.

### 2.5 The tool's shape

Twenty verbs in five groups (Overview, Narrative, Aggregation, Payloads, Search and comparison), nine
with `--json`. Seven address forms: `sN`, `sN/<stepPath>`, `sN/iM`, `sN/dK`, `sN/dK/nJ`, `b:<hash>`,
`sid:<16 hex>` (`VerbTable.AddressForms`). `kronikol ingest` takes `*.ndjson` and `*.jsonl` interaction
streams, `--tests`, `--cucumber-messages`, and folds a wire tap and an OTLP span tap with
`--merge-duplicates` (`IngestCommand.cs`). **`Kronikol.Tool.csproj` has zero `PackageReference`s** (RUN),
which roadmap 10.1 also states: the MCP SDK would be its first. An OpenAPI reader is therefore a
dependency decision, not a detail (Q3).

### 2.6 The run-end files

`TestRunReport.json`, `.html`, `Failures.md`, `Failures.jsonl`, `CLAUDE.md`, `AGENTS.md`,
`ComponentDiagram.html`, `ctrf-report.json`, and by option `.yml`, `.xml`, `Specifications.*`.
`GenerateFailuresDigest` and `WriteAgentInstructions` default to `true`: the precedent for a small new
file that is on by default. The skill's rung zero is "read the directory before querying it".

### 2.7 What the other plans have settled

- `PLATFORM_FOUNDATIONS_PLAN` §0: every platform is a capturer writing two NDJSON streams and an options
  file; one renderer; one query engine. §4 lists the contract questions; §8.1 says `captureFormatVersion
  1` freezes after F3 and F4 (roadmap 14.5). §4.4: security redaction at capture marks the record, display
  redaction in the renderer. §8.5: an OTLP-to-NDJSON bridge is a spike, not costed.
- `NEXT_LANGUAGE_PLAN` §1.1: OpenTelemetry cannot carry bodies, and bodies are about 90% of a report and
  usually the answer. An OTel-only capturer "would deliver the topology and lose the product". So OTel is
  not the fleet's on-ramp for JavaScript and Python; the contract's NDJSON is, and the native ports come
  at 14.9.
- `DASHBOARD_PLAN` §4.6, §4.8, §6: a derived view is regenerated beside the ledger on the
  `kronikol-history` branch, never merged; across repositories, grouping is text equality and no verdict
  arithmetic crosses a source; no server, database, account or push. The fleet index follows all three.
- `MCP_PLAN` §4.3: `kronikol_find_reports` is the one new capability, because a host with no shell cannot
  take the skill's first step. A fleet root is the same question asked once more.
- `ROADMAP` 10.0: measured, a Mermaid rendering did not help an LLM; the one thing it carried that `flow`
  did not was nesting, which 3.31.0 added to `flow`. Diagrams stay the human channel.

---

## 3. Findings

Each is what the architecture would get wrong without a change, or what it gets for free.

| # | Finding | Basis | Consequence |
|---|---|---|---|
| F1 | **Evidence describes the tested surface.** `services` says "a service missing here was never called", true of one run and false of the service. Untested operations do not exist to a reader of the report | READ (`QueryCommand.Overview.cs:319`) | Pair each report with its spec and state the gap in numbers (S3). A card without a spec must say it lists the tested surface, not the service (§4.4) |
| F2 | **A double's answer reads as the dependency's contract.** A response captured from WireMock or an in-process fake is what the test pretended the party says. Nothing marks the party | READ (§2.1, §2.2) | `Parties[].Kind` (S1). Default `unknown`, never `real`: unknown is honest, real is a claim |
| F3 | **Display names do not join.** Service A calls `payments`; service B is `Payments API` in its own report and `payments-svc` in C's. The ledger's `Deps` are these strings | READ (§2.1, §2.3) | `ServiceId` on the report and an id per party (S1). The fleet index joins on ids and reports an edge it cannot resolve rather than guessing (S5) |
| F4 | **The plumbing for one report exists and is the right shape.** Budgeted verbs, addresses, run-end digests, history, `diff --baseline`, ingest for the other languages | READ | The fleet is the same ladder one level up; no new mechanism, three new emitters and one new verb group |
| F5 | **Token cost multiplies by services, not by report size.** One report is safe because nobody opens it. Twenty cards read to find the right one cost twenty cards | INFERRED | Search is index-first: `who-handles` answers from `fleet.json`, never by reading cards (S5). Cards are byte-stable so a prompt cache hits (§4.4) |
| F6 | **Diagrams are the human channel.** The roadmap measured Mermaid against `flow` (10.0); PlantUML source runs to 663 KB | PLAN (10.0), the skill | `graph` prints the data the component diagram is drawn from (S2). The model cites a diagram address in what it writes, for the human reviewing it |
| F7 | **Where to make the change is four facts, and the report has two.** Purpose and owner (nobody's), the operations (spec, S3), the flows (scenario names, today), the tests to extend (exact only on a failure or a Cucumber run, §2.4) | READ | The card carries the four, with the test location at the precision the run allows and a sentence saying which precision it is (§4.4) |
| F8 | **Publishing bodies company-wide is a data policy.** Redaction exists at capture and at ingest, and the platform plan marks redacted records | READ, PLAN (§4.4 there) | The card carries an attestation: which redaction ran, how many values and headers it touched (§4.4). A fleet shares one policy file (Q5) |
| F9 | **Thin suites make thin cards.** A service with three integration tests yields a card with three flows | INFERRED | F1's numbers make it visible: "3 of 22 operations exercised" is a card's most useful line to a team, not only to a model |
| F10 | **The fleet is not in the launch bar.** Section 0 of the roadmap is a first look at one .NET report; nothing there needs a second service | READ | Rule 9 puts the tooling after the launch. Rule 6 puts S1's fields before 14.5. Rule 8 lets S2 move any time (§7.6, D23) |

---

## 4. The design

### 4.1 S1: the two contract fields

**`ReportConfigurationOptions.ServiceId`** (`string?`, default `null`): the canonical, stable identity of
the service under test, lower-case, the string every other service uses for it (`orders-api`). Distinct
from `SuiteName` (the test project) and from `TestRunReportTitle` (display). Written to the report's
header block as `serviceId`, and to the history run line, so a fleet snapshot can be keyed on it.

**`ReportConfigurationOptions.Parties`** (`IList<PartyDescriptor>`, default empty), with
`PartyDescriptor { string Name; string? Id; PartyKind Kind = Unknown; string? Note }`. `Name` is the
display name the trackers already produce, the same join key `ServiceTypeOverrides` uses. `Kind`:

| Value | Means | A reader may |
|---|---|---|
| `test` | the test process; `CallerName`'s party by default | ignore its shape: it is the driver |
| `underTest` | the service this report describes; `FixedNameForReceivingService`'s party by default when `ServiceId` is set | trust its responses as the service's own |
| `real` | a real instance: deployed, or a real image run for the test | trust its responses as evidence of that service |
| `double` | a stub server, a recorded response, an in-process fake | trust nothing about that service from them; they show what *this* service expects |
| `unknown` | not declared (the default) | neither; the card counts these and names them |

Five values, not seven: a container-run real image is `real`, and whether a double is WireMock or a
hand-written fake changes no reader's trust; `Note` carries "WireMock" for the human. Fewer states are
fewer places for a table to lie (the 3.30.3 lesson).

Written to the report as a `parties` block: `[{ name, id, kind, category, note }]`, one row per distinct
`serviceName` or `callerName` seen in the run, declared rows carrying their declaration, undeclared rows
`kind: "unknown"`. Interactions are unchanged; a reader joins on `name`. The block is emitted only when
`ServiceId` or `Parties` is set, so every existing corpus stays byte-identical (INFERRED from §2.2's null
omission and the report writer's options; §7.5 checks it). In the cross-language contract the block is a
section of the options file (F4: "the options file carries all options"), not a stream record (Q7).

Automatic kinds are limited to the two the options already imply (`test`, `underTest`). No detection of
doubles in core (Q4): a WireMock port looks like any port. `services --json` and `graph` print `id` and
`kind` columns; `services` text gains a `kind` column and a footer line `N parties of unknown kind: declare
them in Parties`.

**Bump:** minor (new options, new report block). **Records:** a Kronikol4J divergence-ledger entry (the
block is rendering output); `PLATFORM_FOUNDATIONS_PLAN` §4 gains 4.7 pointing here, so the freeze at 14.5
does not miss it.

### 4.2 S2: `graph`, the diagram as data

`kronikol query graph <report> [sN] [--service TEXT] [--json]`. Participants (name, id, kind, category,
calls, error count) and edges (`caller>service`, calls, status mix, p50 ms, first address), sorted by id
then name; a footer naming the unresolved parties. Text under the budget; JSON as an envelope like the
nine that have one. Built from `RequestResponseLog` the way `InteractionShape.Dependencies` and
`ComponentDiagramGenerator` already are; no new model. Tool-only, no report byte changes. It is the verb
the fleet index reads, and the one an agent reads instead of a diagram.

**Bump:** minor. **May be pulled forward** to any point when no other track A stage edits `VerbTable`
(rule 5), like 10.0 was.

### 4.3 S3: `coverage`, the spec against the run

`kronikol query coverage <report> --openapi <file> [--asyncapi <file>] [--json]`, and the same numbers in
the card when `ReportConfigurationOptions.OpenApiSpecPath` and `AsyncApiSpecPath` are set.

For OpenAPI: operations are (method, path template) pairs from `paths`; requests to the `underTest` party
match by method and template (a `{segment}` matches one segment; the most literal template wins, as the
OpenAPI path-matching text has it). Output, in this order: **exercised** (count, statuses seen, p50 ms,
first address), **unexercised**, **unspecified** (captured, not in the spec), **undeclared statuses**
(seen, not under `responses`), and **required properties never present** (top-level `required` of the
matched response schema against the body's keys; v1 goes no deeper). For AsyncAPI: channels against
`MessageQueue`-category interactions, matching the channel name to the interaction's topic or queue
(§9 A6 says where that string sits is unread).

**JSON specs only in v1** (Q3). The tool has no YAML reader and no third-party dependency; every
generator emits JSON, and a YAML spec converts in one command. The verb refuses a YAML file by name and
says so.

**Bump:** minor (a verb, two options, the card's section).

### 4.4 S4: the service card

`Service.md` and `service.json`, written at run end by a `ServiceCardGenerator` beside
`FailuresDigestGenerator`, behind `GenerateServiceCard` (default `true`, Q2). Target 8 KB; each section
truncates to its budget and ends with the verb that lists the rest. Ordering is total (id, then name, then
method and path), so two runs with the same behaviour produce the same bytes (F5), and `diff` of two cards
is the service's observed changelog.

Sections, fixed:

1. **Identity.** `serviceId`, display name, suite, Kronikol version, commit, branch, run id, run time,
   results (P/F/S counts), and the provenance line the report already prints on a degraded or merged run.
2. **Exposes.** From `coverage` when a spec is configured; else the exercised operations alone under the
   sentence *"No spec configured: this is the tested surface, not the service."*
3. **Consumes.** The parties table with `kind`, calls and status mix, in three groups headed by what the
   evidence is of: *the real service*, *a double (what this service expects, not what that one does)*,
   *unknown*.
4. **Messages.** Channels published and consumed, against AsyncAPI when configured.
5. **Flows.** The scenarios with the most calls, each as one line: name, address, the party chain
   (`Caller>orders-api>payments-api`), status; then the scenario count and `kronikol query scenarios`.
6. **Where the tests live.** The suite assembly; feature files with lines when the run has them (Cucumber
   Messages); else the namespaces read from the test ids, grouped; failing steps' frames from `repro`. One
   sentence says which of the three precisions this run has (F7).
7. **History.** When a ledger is beside the report: new, flaky, regressed and slower counts with
   `history` addresses.
8. **Redaction.** Which redaction ran (capture, ingest, both), headers excluded, values masked, and
   whether `--no-redact` was in force (F8).
9. **Next.** The report path and the three verbs a reader most likely wants.

`service.json` holds the same content as data, with the parties table and the coverage block verbatim, for
S5. The skill's rung zero, both copies, and the emitted `CLAUDE.md` name the card first on a green run
(`Failures.md` stays first on a red one).

**Bump:** minor (an option, two files, two new options from S3 if not shipped before). **Records:** a
Kronikol4J ledger entry; wiki pages (§7.4).

### 4.5 S5: the fleet index

`kronikol fleet index <input>... [--overlay fleet.json] --out <dir>`. Inputs: `service.json` files,
directories holding them, reports (the card is derived on the fly), or `history.view.json` files (edges
only, as display names through each source's parties table when one is beside it). Output:

- **`fleet.json`:** services (id, name, purpose, owner, repo, specs, card path, last run: commit, branch,
  time, results), edges (`fromId>toId`, evidence kind: the caller's report, the callee's, or both;
  calls), **unresolved** edges (a display name no parties table maps, with the report it came from), and
  services known only from the overlay or a spec (*no evidence*, Q9).
- **`Fleet.md`:** one line per service, sorted by id, at about 120 bytes each, then the edges by from-id,
  then the unresolved names. Twenty services fit in 4 KB (INFERRED).

`kronikol fleet who-handles <METHOD path | topic> [--fleet fleet.json]`: the owning service (its
`underTest` operation match), its card path, the operation's coverage line, and the callers from the
reverse edges. `kronikol fleet graph [--service id]`: the joined graph, S2's format.

The overlay is JSON in v1 (Q3 again): per id, `purpose`, `owner`, `repo`, `specs`, and groups; it holds
only what no service can say about itself. Each service declares itself through its options
(`ServiceId`, `Parties`, the spec paths), so the overlay stays a page.

Distribution is the dashboard plan's: the job that records history commits `service.json` to the
`kronikol-history` branch; the fleet job reads N raw URLs or checkouts and commits `Fleet.md` and
`fleet.json` to a fleet repository whose `CLAUDE.md` is the entry point ("read `Fleet.md`; ask
`who-handles`; then `kronikol query` the service's report"). No server, no push, no cross-source verdict
arithmetic (dashboard §4.8: text equality on ids).

**Bump:** minor (a verb group). One tool, no new package (Q10).

### 4.6 S6: addresses across reports

An eighth address form: `<serviceId>@<run>/<address>`, `run` being `latest`, a run id, a commit prefix or
`previous`, resolved through `fleet.json` (`--fleet`, or `$KRONIKOL_FLEET`). `--describe` lists it; `diff`
tells it from a path by the `@`. `MCP_PLAN` §4.3's `find_reports` takes a fleet root and returns cards
with the reports. The budget arithmetic of §0: map 4 KB, card 8 KB, answer 6 KB; a model that reads one
map, three cards and eight answers has read about 76 KB, roughly 19K tokens (INFERRED at four bytes a
token).

**Bump:** minor.

### 4.7 S7: the gate

The plan's acceptance test, in the manner of roadmap 10.2, run cold before and after. Fixture:
BreakfastProvider's real report (service A, `ServiceId` declared, the party it calls declared with an id), a synthetic
service B built with `kronikol ingest` from hand-written NDJSON in which B is `underTest` under that same
id and calls a `double`, and a JSON OpenAPI spec for each with one operation unexercised on purpose. Task
to the agent, given only the fleet repository: *"add a field to B's operation X: name the service, the
namespace of its handler, the feature file or namespace to extend, and every caller affected."* Pass: B
named; the caller A named from the reverse edge; the unexercised sibling operation flagged as untested;
the double named as a double; no read of any `TestRunReport.*`; total tokens under a ceiling set from the
first cold run (a number to measure, not to assume). No bump.

---

## 5. Before and after

Today, on a BreakfastProvider report (PLAN: `DASHBOARD_PLAN` §4.2's `deps` line):

```
Caller>Breakfast Provider
```

is everything the ledger says about who talked to whom, and `services` lists `Breakfast Provider` and its
dependencies as display names with no kind. After S1 and S2, `kronikol query graph` (shape, not measured):

```
participants
  breakfast-provider   Breakfast Provider   underTest  HttpApi       calls 41
  (none)               Caller               test                     calls 41
  kafka                Kafka                real       MessageQueue  calls 12
  (none)               Bigtable             unknown    Database      calls 9
edges
  Caller>breakfast-provider          41   2xx 39 · 4xx 2     p50 18 ms   s0/i0
  breakfast-provider>kafka           12   ok 12              p50 3 ms    s2/i4
  breakfast-provider>Bigtable         9   ok 9               p50 6 ms    s1/i2
1 party of unknown kind: declare it in Parties
```

---

## 6. Tests, red first

- **S1:** `ReportConfigurationOptions` round trip and the `parties` block's emission rules (absent when
  nothing is declared: a byte-identity test over the existing golden corpus; present with the declared
  and inferred rows otherwise); `RequestResponseLogRoundTripTests` gains the block through ingest;
  `services` prints the `kind` column and the footer.
- **S2:** `GraphTests` in the manner of `FlowTests`: participants and edges from a synthetic report,
  sorting, the unresolved footer, `--service`, `--json`, the budget.
- **S3:** `PathTemplateMatchTests` (literal beats template, one segment per `{}`, trailing slash);
  `CoverageTests` over a hand-written spec and report: each of the five sections, the YAML refusal.
- **S4:** `ServiceCardTests`: every section present and in order; each section's truncation line; two
  generations byte-identical; the three test-location precisions each produce their sentence; the
  no-spec sentence.
- **S5, S6:** `FleetIndexTests` over two synthetic cards: the join, an unresolved edge reported, a
  no-evidence service from the overlay, `who-handles` with a reverse edge; `AddressParseTests` for the
  locator form and `diff`'s disambiguation.
- **Proving red:** each new fact fails on 3.31.2 for the reason its test names; the pins of unchanged
  behaviour (existing corpus byte-identical, `services` text without declarations) are green before and
  after.

No Playwright: nothing here changes the HTML report.

---

## 7. Slices, releases, records

### 7.1 Order

S1 (contract) → S2 (`graph`) → S3 (`coverage`) → S4 (card) → S5 (fleet index) → S6 (addresses, MCP) →
S7 (gate). S2 needs nothing and may go first or any time (§4.2). S1 must precede 14.5 (rule 6). S3 and
S4 may ship as one minor.

### 7.2 Bumps

Five minors and a gate with no bump. S1 and S4 change report output: a Kronikol4J divergence-ledger
entry each; the bump follows the nature of the change (a new block, a new file), per `CLAUDE.md`.

### 7.3 Where the code goes

| Slice | Files |
|---|---|
| S1 | `ReportConfigurationOptions.cs`, a new `Reports/PartyDescriptor.cs`, the report writer, `History/HistoryRunBuilder.cs` (the id on the run line), `QueryCommand.Overview.cs` (`services`), `IngestCommand.cs` (the options section), `PLATFORM_FOUNDATIONS_PLAN.md` §4.7 |
| S2 | `Query/VerbTable.cs`, a new `QueryCommand.Graph.cs`, both skill copies |
| S3 | a new `Query/OpenApiReader.cs` and `QueryCommand.Coverage.cs`, `VerbTable.cs`, two options |
| S4 | a new `Reports/ServiceCardGenerator.cs`, `ReportGenerator.cs` (the call), `AgentInstructionsGenerator.cs`, both skill copies |
| S5, S6 | a new `FleetCommand.cs` and `Fleet/` folder in the tool, `Query/VerbTable.cs` (the address form), `QueryCommand.Describe.cs`, `MCP_PLAN` §4.3 |

Tracks (roadmap §5): S1 is D (capture records) with one A file; S2 to S6 are A. Never beside another A
stage (rule 5: `VerbTable` has several claimants).

### 7.4 Docs

Wiki, at execution: a `Service-Card` page, a `Fleet` page, the configuration-options page rows
(`ServiceId`, `Parties`, `GenerateServiceCard`, the spec paths), the CLI reference (`graph`, `coverage`,
`fleet`, the address form), and `LLM-Friendly` or its successor page for the ladder. README: one sentence
under the agent section. Both skill copies. Changelog per release, stating the moved part.

### 7.5 Before S1 starts: measure

1. That the `parties` block's absence keeps every golden report byte-identical (the writer's serializer
   options, not only `InteractionRecord`'s).
2. Where a message tracker puts the topic or queue name (A6), on the Kafka and Service Bus adapters.
3. What fraction of scenarios carry a feature-file location per adapter, on BreakfastProvider's lanes
   (A7), so §4.4's sixth section promises what the runs have.
4. The card's size on BreakfastProvider's five lanes with no truncation, to set the 8 KB budget from a
   number.
5. Re-check §2 against the tree at the time: 3.31.x moves quickly and `VerbTable` has claimants.

### 7.6 Where it sits in the roadmap

Rows 14.11 to 14.15, after the launch (F10, rule 9), with S1 ordered before 14.5 by rule 6 and S2 free to
move by rule 8. D23 asks whether the owner wants it sooner for their own company's use; if so it runs as
tracks A and D beside stages 6 to 12, one A stage at a time. Nothing in it is in the bar and nothing in
the bar waits for it.

---

## 8. Not taken, and open questions

### 8.1 Designs not taken

- **Per-interaction `serviceId` and `partyKind` fields.** Denormalised; the same fact repeated on every
  record; two fields in every capturer of every platform. A table keyed on the name the interactions
  already carry is one place to read and one section of the options file.
- **Automatic detection of doubles in core.** A WireMock port is a port. An extension package can
  register its server's URLs later (Q4); core never guesses `real`.
- **Reading the specs from a URL.** A file the CI checked out at the tested commit is the spec of that
  commit; a URL is whatever is deployed.
- **A Kronikol-hosted fleet page or service.** The direction report's line, dashboard §6 and roadmap §6
  ("hosted identity and SSO: a buyer asks") all stand. Files on branches, and a page only if the
  dashboard plan builds one.
- **Feeding the model diagrams, or Mermaid.** Measured against `flow` at 10.0; `graph` gives the data.
- **An OTLP capturer as the JavaScript and Python on-ramp.** No bodies (§2.7). Ingest of NDJSON now,
  native ports at 14.9; the OTLP tap stays the tail.
- **A model inside Kronikol.** The plan gives an agent files and verbs; Kronikol calls no model.

### 8.2 Open questions

| # | Question | Recommendation |
|---|---|---|
| Q1 | Five party kinds, or seven with `container` and `inProcess` split out | Five. A reader's trust has three states; `Note` holds the rest |
| Q2 | `GenerateServiceCard` default on, and the file's name | On, like `Failures.md`; `Service.md` and `service.json`. A card on every run is what makes it current |
| Q3 | YAML specs and overlays: a dependency (`YamlDotNet`), a subset reader, or JSON only | JSON only in v1, refused by name with the conversion command. Measure the ask before adding the tool's first or second dependency |
| Q4 | Marking doubles automatically | Not in core. `Kronikol.Extensions.WireMock` or the like may register its URLs as `double` later, if asked for |
| Q5 | One redaction policy across a fleet | The platform's options file (F4) is the carrier; a fleet checks each card's attestation against it. Not before F4 |
| Q6 | Does the inbound surface include requests captured server-side (`HttpContextAccessor`) or only the test client's | READ before S3; the `underTest` party's received requests are the surface either way |
| Q7 | The parties table in the contract: options-file section or a stream record | Options file; the platform plan's F4 decides |
| Q8 | After the launch, or beside stages 6 to 12 for the owner's own use | After (rule 9), with S1's two fields decided now in `PLATFORM_FOUNDATIONS` §4 and S2 pulled forward when track A is idle. This is D23 |
| Q9 | A service with a spec and no report in `Fleet.md` | Yes, marked *no evidence*: a complete map that says where the evidence stops beats a partial one |
| Q10 | A separate `Kronikol.Fleet` package | No. Verbs on the one tool until a number says otherwise |

---

## 9. Assumption ledger

| # | Statement | Basis | If wrong |
|---|---|---|---|
| A1 | An unset `parties` block leaves every existing report byte-identical | INFERRED from `WhenWritingNull` on the record; the report writer unchecked | §7.5 item 1 catches it; emit the block only when declared, which holds regardless |
| A2 | Twenty verbs, default budget 6,000 bytes, seven address forms | RUN, READ | none |
| A3 | No spec-reading code anywhere in `src/` | RUN (`grep -ril "openapi\|asyncapi\|swagger"`, empty) | S3 shrinks to wiring |
| A4 | The tool has no third-party dependency | RUN (`PackageReference` count 0) | Q3's cost changes, not its shape |
| A5 | A fleet of twenty fits `Fleet.md` in 4 KB and a card in 8 KB | INFERRED | §7.5 item 4 sets the budgets from BreakfastProvider |
| A6 | Message trackers put the topic or queue where a matcher can read it | ASSUMED | S3's AsyncAPI half waits for the READ |
| A7 | .NET adapters carry no feature-file location; Cucumber Messages do | READ (§2.4) | the card's sixth section gains a precision |
| A8 | The history job can commit `service.json` beside `history.view.json` with the dashboard plan's rebase loop | PLAN (dashboard §4.6, simulated there) | a derived file conflicts; regenerate, never merge, as the view does |
| A9 | Four bytes a token for the ladder's text | ASSUMED, the usual figure for English | the 19K estimate moves; the ratio to 2.7M does not |
| A10 | A model given `graph` and a card needs no diagram | PLAN (10.0 measured Mermaid against `flow`) | S7's gate says so before anything ships |
| A11 | Every OpenAPI generator in the four languages emits JSON, or converts in one command | ASSUMED (Swashbuckle, NSwag, springdoc, FastAPI and swagger-jsdoc do, from memory) | Q3 is decided the other way |
