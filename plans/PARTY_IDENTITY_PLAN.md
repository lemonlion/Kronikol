# Who each party is: the service under test, party ids and test doubles (#190)

**Date:** 2026-10-26, revised 2026-10-27 · **Repo version:** written against 5.11.0 (`origin/main` at `44bcc62a`), and
every line number here is to that commit.

- The issue's line numbers are to 5.9.0 (`0905e079`). The files it cites are unchanged since, but for one line added to
  `ReportConfigurationOptions.cs` above `ServiceTypeOverrides`, so its L604 is L605 here.
- Re-checked on 2026-10-27 against `origin/main` at `1ee6373a` (5.12.0). Only two groups of the cited files have
  changed:
  - `Reports/ReportGenerator.cs`: its edits are between lines 1941 and 3290, so every line cited past 3300 is 14
    higher on 5.12.0.
  - the SQL trackers: 5.11.1 (#193) replaced their address code with `Sql/SqlEndpoint.cs` (Appendix B).

**Status:** ⏳ **green-lit 2026-10-27** ("Can you implement the plan in full, make sure to not interfere with any other
sessions"), every question in section 10 answered by the owner the same day; being executed. Written by kronikol-67 at
the owner's request of 2026-10-26: analyse #190 critically, then plan a fix for the underlying problem, which may not
be the fix the issue proposes.

**Issue:** [#190](https://github.com/lemonlion/Kronikol/issues/190), "A report can't say which party is the service
under test or which parties were test doubles, so an indexer reads a stub's answers as a dependency's contract and
can't join services across reports". It was filed on 2026-10-26 with #191 and #192, from the same indexer design. Their
plans were written the same day beside this one, and the split was agreed by message (section 9):

- `SERVICE_CARD_PLAN.md` (#191, kronikol-ed, worktree `C:/Code/Kronikol-191`): the per-run `service.json`.
- `CALL_OPERATION_AND_ORIGIN_PLAN.md` (#192, kronikol-bf, worktree `C:/Code/Kronikol-192`): `origin`, the route
  template and the GraphQL operation.

This plan owns what a party is: its id, its kind, how the kind became known, and the rule that names the test and the
service under test when nothing is declared. The other two cite it.

**Harness:** `plans/PARTY_IDENTITY_PLAN.harness/` (its `README.md` says how to run each part).

- `probe-names/` sends eight requests through the real `TestTrackingMessageHandler` and prints the names it records.
- `bp-reports/` holds four scripts and their results. They read four real reports of the owner's reference consumer,
  BreakfastProvider (CI run `40290999167`, 2026-10-26, Kronikol 5.4.1), decoded with `kronikol decode` built from
  `44bcc62a`. The reports are not committed; the README says how to fetch them again.
- `compat/` gives a real report the plan's block and per-call field, and runs the readers of two released tools on it
  (5.5).
- `sweeps/trackers.md` records how every capturer in `src/` names its two parties (Appendix B).

Evidence labels:

- **RUN**: measured on 2026-10-26 and 2026-10-27 with the harness, on Windows 11 26200, .NET SDK 10.0.300.
- **READ**: Kronikol's source at `44bcc62a`, or the wiki at `origin/master` `2f165c5` (the pages cited are unchanged at
  `ae3393f`).
- **PEER**: measured by a sibling plan's session, cited to its harness.
- **ISSUE**: the issue's own figures, not measured again (its three reports are not on this machine).
- **PLAN**: another plan says so.

---

## 0. Summary

- **The issue's three findings hold** (RUN, on BreakfastProvider's four lanes, 4,437 calls):
  - nothing in a report says which party it is about;
  - nothing marks a double: `capturedBy` is null on every call, and `DependencyType` says what a party is, never
    whether it was real;
  - parties are named by display strings.
- **The core of its design is right.** Ids are declared and free text, kinds default to `unknown`, `real` is never
  inferred, and the block is written only when something is declared, so existing reports keep their bytes.
- **Four parts of the request do not survive the code or the reports:**
  1. **`kronikol ingest` has no options file.** It exists only in an unapproved plan (`PLATFORM_FOUNDATIONS_PLAN` F4),
     so the ingest acceptance line has nothing to land on (READ: 42 flags, no file). R2 adds three flags instead, and F4
     takes the same section unchanged when it lands.
  2. **One shared map of "hosts, ports and client names to id and kind for all suites" is wrong twice.**
     - Kinds change between lanes of one suite. On BreakfastProvider, `CosmosDB` is an in-memory fake on one lane and
       the emulator on another. `BigQuery` is a fake answering for `https://bigquery.googleapis.com` on one lane and an
       emulator on `localhost:9050` on the other.
     - Ports change too: the Cow Service fake is on 5141, 5171 and 5031.
     - 7 of 16 parties carry no host in any URI (RUN).
     - The display names are the same on every lane. So the key is the name, a shared file carries ids, and each run
       declares its own kinds, field by field.
  3. **The default for the subject points at an option that names nothing.** `ReportConfigurationOptions.FixedNameForReceivingService`
     only titles the report (`ReportGenerator.cs:1057-1065`), although its doc says it names the receiving service. The
     subject's name is really typed by hand three times: as the test client's `FixedNameForReceivingService`, as the
     service's `CallerName`, and to its message tracker (2.2). So the subject is found from the calls, and from the
     test client Kronikol itself builds, and `ServiceId` attaches to that party.
  4. **`services` cannot list "every party".** It lists parties that were called. The test client is never called,
     and neither is a broker that only delivers (Azure Event Hub on the in-memory lanes). A `parties` listing is needed.
- **It also misses one thing: a capturer can know, call by call, that a double answered.**
  - The Jest recipe receives MSW's `response:mocked` and `response:bypass` events, and writes both the same way
    (`Ingesting-From-Jest.md:237-238`).
  - A table keyed on the party cannot say that 20 of its 23 calls were mocked.
  - One optional field on the interaction record can say it. The capture contract freezes at roadmap 14.5, so the field
    costs nothing to add now and a format version later (rule 6).
- **The underlying problem: Kronikol has no party model.**
  - A party is the string each tracker writes from its own options, and nothing in the run says what the string stands
    for.
  - Capture drops what it knew: the client name, whether the answer came over a socket or from code in the process,
    and which application a `WebApplicationFactory` hosted.
  - The strings are worse keys than they look (RUN). With nothing mapped, `https://pricing.example.com` and
    `https://api.openai.example` become one party called `localhost:443`, as if both ran on this machine. A stub on a
    random port gets a new name every run, and history reads that as a new dependency every run.
- **Releases** (section 6):
  - **R0, a minor:** the fallback name keeps a remote host (a loopback address stays `localhost`), and history
    bridges the change (Q2): while runs made under the previous rule are in the window, each run also fingerprints
    its own calls under that rule, so the rename costs no quiet run, no false verdict and no false new dependency.
    The bridge is `CALL_OPERATION_AND_ORIGIN_PLAN.md` §4.8 part 1, built here for a change of built-in rule and agreed
    with kronikol-bf; #192's R1 extends it. The docs that describe the fallback are corrected.
  - **R1, a minor:** the party model.
    - Declarations, observations and inference feed one resolver in core.
    - The block goes into every form of the data file, and merges.
    - A `parties` verb is added, and `serviceId` goes on the run line and into `Run.json`.
    - A declared double is drawn with the stereotype «test double» (Q11).
  - **R2, a minor:** ingest's flags, the per-call `testDouble` field, and the Jest recipe.
  - **R3, a minor:** the .NET handler marks a call that code in the process answered.
- Green-lit 2026-10-27. The owner's answers to the sixteen questions are in section 10.

---

## 1. What the issue asks

**Findings** (5.9.0; ISSUE, from three real reports: Cucumber-JVM, a Jest ingest, Reqnroll):

1. The service under test is known only by position (`Caller → <service>` in one report, `Test → <service>` in two).
   `FixedNameForReceivingService` is a display name, and `suite` is the test assembly, null on both ingested runs.
2. Test doubles read as real dependencies: a WireMock stub of a sibling service, a stubbed LLM, 20 MSW handlers, a
   Firestore and a BigQuery emulator. `capturedBy` is null, and `DependencyType` says what a party is.
3. Names do not join across reports (`Pricing Engine` here, `pricing-engine` in the service's own report).
   `ServiceTypeOverrides` maps a name to a category; nothing maps a name to an identity.

**Asks:**

1. `ReportConfigurationOptions.ServiceId` and `Parties` (`PartyDescriptor { Name; Id; Kind; Note }`, kinds `test`,
   `underTest`, `real`, `double`, `unknown`, default `unknown`, `real` never inferred). They are written as `serviceId`
   and a `parties` block, and to the history run line, "available the same way through the ingest options file".
2. Ids are free text chosen by the user. Kronikol never invents or normalises one.
3. `kindSource` (`declared` or `inferred`) on each row.
4. Optional: `PartiesFile`, one JSON map "of hosts, ports and client names to id and kind for all its suites".

**Acceptance:**

- Golden reports are byte-identical when nothing is set.
- `services --json` and the data file show `id`, `kind` and `kindSource` for every party.
- Undeclared parties show `unknown`, and the `services` footer names them.
- The same works through `kronikol ingest` via the options file.

**Related:** `FLEET_EVIDENCE_PLAN.md` §3 (F2, F3), §4.1 (S1), §7.5; `ROADMAP.md` 14.11; rule 6, before the 14.5
freeze; #191 reads these fields; #192 uses the kinds.

---

## 2. What exists today

### 2.1 How a party gets its name (READ, RUN)

**The HTTP handler** (`src/Kronikol/Tracking/TestTrackingMessageHandler.cs`):

- The callee is resolved by `ResolveServiceName` (`:72-114`), in this order:
  1. `FixedNameForReceivingService`;
  2. `ClientNamesToServiceNames`, exact, then a suffix or contains match for Refit's names;
  3. `PortsToServiceNames`;
  4. else the literal `localhost:{port}` (`:67-70`).
- It is given the port alone (`:239`). The request's host is read only to skip excluded hosts (`:146-147`), never to
  name the party.
- The caller is `CallerName`, a string fixed per handler (`:51`), by default `"Caller"`
  (`Constants/TrackingDefaults.cs:12`).
- The client name the handler was built for is kept only to pick a name (`:48`). It is never recorded; an unmatched
  one goes to a diagnostics registry (`:109`).
- To decide who writes `traceparent`, the handler walks to the handler at the end of its chain and asks whether it is
  a socket transport (`:387-393`). It keeps nothing of the answer.

**Every other capturer** (`sweeps/trackers.md`, Appendix B):

- The caller's name is a fixed option. Nothing works out who is calling when a call happens.
- One party has several default names: `"Redis"` in process and `"redis"` from `RedisTap`; `"MongoDB"` and `"mongo"`;
  `"SQL Server"` through SqlClient and `"Database"` through EF Core or Dapper.
- The default caller is `"Caller"` for every in-process tracker. The others differ: `""` for ProxyTap and TcpTap
  (`ProxyTapOptions.cs:42`, `TcpTapOptions.cs:61`), `"unknown-service"` for the OTLP tap (`OtlpTapOptions.cs:153`),
  and `"Browser"` for Playwright's header (`TestTrackingIdentity.cs:23`). `TrackingDefaults.CallerName`'s doc says it
  is the default "on all tracking options classes" (`:10`).

**The probe** (RUN, `probe-names/results.txt`), through the real handler at 5.11.0:

| Request | Mapping | Recorded service | Recorded caller |
|---|---|---|---|
| `https://pricing.example.com/v1/prices` | none | `localhost:443` | `Caller` |
| `https://api.openai.example/v1/chat` | none | `localhost:443` | `Caller` |
| `http://localhost:54321/stub` | none | `localhost:54321` | `Caller` |
| `http://127.0.0.1:54987/stub` | none | `localhost:54987` | `Caller` |
| `http://pricing.internal:5141/x` | port 5141 is `Cow Service` | `Cow Service` | `Caller` |
| `http://other.internal:5141/x` | port 5141 is `Cow Service` | `Cow Service` | `Caller` |
| `https://pricing.example.com/v1/prices` | client `IPricingApi`, map has `billing` only | `localhost:443` | `Caller` |
| `http://localhost:5000/x` | fixed name `Breakfast Provider` | `Breakfast Provider` | `Caller` |

So two different remote APIs are one party, and a remote one is called `localhost`. Kronikol4J does the same
(`kronikol4j-core/.../naming/ServiceNameResolver.java:88-90`).

The port map matching any host is documented behaviour ("Maps port numbers", `TestTrackingMessageHandlerOptions.cs:9`).
So is the fallback name, for a local port (wiki `Tracking-Dependencies.md:699`, and `HTTP-Tracking-Setup.md:42`, which
spells it `localhost_80` where the code writes `localhost:80`). Nowhere says that a remote host gets the same name.

### 2.2 How the service under test is named (READ)

The repository's canonical setup (`examples/Example.Api/tests/Example.Api.Tests.Component.xUnit3/Infrastructure/BaseFixture.cs`)
types the service's name three times:

- `CallerName = ServiceUnderTestName` on the handler inside the service (`:32`);
- `TrackMessagesForDiagrams(ServiceUnderTestName)` (`:35`);
- `FixedNameForReceivingService = ServiceUnderTestName` on the test client (`:43`).

If they disagree, the diagram draws two participants. `ReportConfigurationOptions.FixedNameForReceivingService`
(`:454-455`) says "all tracked requests use this name as the receiving service". Its one reader is the report's title
(`ReportGenerator.cs:1063`).

`CreateTestTrackingClient<T>` (`WebApplicationFactoryExtensions.cs:22-29`, and its overload at `:40-50`) knows `T`,
the entry point of the application the factory hosts. It records nothing of it.

### 2.3 What a call record carries about its parties (READ)

- **The record:** `RequestResponseLog` carries `ServiceName`, `CallerName`, `Uri`, `DependencyCategory`,
  `CallerDependencyCategory`, `AttributionSource`, `CapturedBy` and `Phase`.
- **`CapturedBy`:** documented as `wire` for the taps (`RequestResponseLog.cs:118-123`). Only the OTLP mapper sets a
  value (`span`), and merge writes `wire + span`.
- **The data file:** it writes a call's every member, nulls included, except `warmUp`, which is left out when it has no
  value (`ReportGenerator.cs:5892-5923`, the attribute at `:5919-5920`). So a new member of a call must be written
  only when set, as `warmUp` is.
- **The ingest record:** 35 fields. `serviceName` and `callerName` are required and taken verbatim, with no default
  (`Ingestion/InteractionRecord.cs:46`, `:49`).

### 2.4 What the run carries (READ)

- **The data file's root keys** (`BuildTestRunReportModel`, `ReportGenerator.cs:5478-5513`): `formatVersion`,
  `kronikolVersion`, `suite`, `startTime`, `endTime`, `ciMetadata`, `environment` (only when known), `features`,
  `background`, `diagnostics`. No title, framework or service.
- **The mergeable file** (`:5768-5812`) adds `mergeableFormatVersion`, `wholeTestVisualization`,
  `internalFlowSegments`, `wholeTestFlow` and `componentRelationships` (caller, service, protocol, methods, counts,
  category, keyed on display names). It writes `features` before `ciMetadata`, and `environment` last.
- **The compressed writer** leaves out every key that holds nothing (`:7144-7160`).
- **`Run.json`:** ten members in a fixed order. Its reader ignores members it does not know (`Reports/RunManifest.cs`).
- **The history run line:**
  - carries the `suite` (`HistoryRunBuilder.cs:129`);
  - carries `deps`, the sorted distinct `caller>service` display pairs (`InteractionShape.cs:278-290`, ordinal);
  - the analyzer names as new any pair no earlier run had (`HistoryAnalyzer.cs:141-142`). A stub named by a random
    port is therefore a new dependency on every run.
- **The suite on an ingested run:** null, with no flag to set it (`RunSuite.cs`; `IngestCommand.cs`).

### 2.5 Who compares names, and how (READ)

| Reader | Comparison |
|---|---|
| Call-tree nesting (`IngestPipeline.cs:1607`) | exact |
| Wire and span merge (`InteractionMerger.cs:189-190`) | lower-cased |
| Diagram category caches (`PlantUmlCreator.cs:1310`, `:1356`, `:2037`) | ignoring case |
| `query services` (`QueryCommand.Overview.cs:353`) | ignoring case |
| History `deps` (`InteractionShape.cs:281`) | exact |
| The component diagram (`ComponentDiagram/ComponentDiagramGenerator.cs:58`) | exact |
| The sequence diagram's participant list (`PlantUmlCreator.cs:1304-1307`, `:1320`) | exact |
| `ServiceTypeOverrides` (`PlantUmlCreator.cs:1314`, `:1360`, `:2043`) | the dictionary's own comparer, exact by default |

The list is the readers this plan touches, not every one.

`ServiceTypeOverrides` is the precedent `FLEET_EVIDENCE_PLAN` §4.1 names for a table keyed on a name. It reaches the
sequence diagrams only: not the component diagram, the data file, the query engine or a merge. A party table must name
every reader it reaches (5.5 to 5.8).

### 2.6 Four lanes of one real suite (RUN, `bp-reports/`)

BreakfastProvider runs one suite of about 212 scenarios on several lanes of one CI run. Four were measured:

| Lane | Calls | Callee names | Test, subject (5.4) | Notes |
|---|---|---|---|---|
| xUnit, in memory | 1,409 | 16 | Caller, Breakfast Provider | `capturedBy` null on all |
| xUnit, in Docker | 1,297 | 17 | Caller, Breakfast Provider | dependencies in containers |
| xUnit, external service | 378 | 1 | Caller, Breakfast Provider | the service runs out of process, so none of its calls is captured |
| Reqnroll, in memory | 1,353 | 16 | Caller, Breakfast Provider | |

What the four show:

- **Names are stable across lanes.** The suite maps them itself.
- **What answers behind a name is not stable:**

  | Party | In memory | In Docker |
  |---|---|---|
  | `CosmosDB` | `https://localhost:9999` | `https://localhost:8081`, the emulator (the suite's `AccountEndpoint`) |
  | `BigQuery` | `https://bigquery.googleapis.com`, answered on a CI runner with no credentials | `http://localhost:9050`, the emulator (`EmulatorEndpoint`) |
  | the two SQL Server databases | `sql://` | `sql://localhost:1433` |
  | Cow Service | port 5141 (xUnit), 5171 (Reqnroll) | port 5031 |

  The five sibling services (Cow, Goat, Kitchen, Supplier, Notification) are fakes on every lane.
- **Hosts are often missing.** On the in-memory lanes, 7 of 16 callee names carry no host in any URI (both SQL Server
  databases, Pub/Sub, Kafka, MongoDB, the gRPC Notification Service, Spanner), and Event Grid carries only the
  placeholder `topics` (`results-hosts.txt`).
- **Brokers call the service.** `Kafka Broker`, `Azure Event Hub` and `Google Cloud Pub/Sub` deliver (42, 39 and 36
  calls on the xUnit in-memory lane). Nearly all are attributed `RequestHeader` (40 of 42, 39 of 39, 35 of 36), and all
  carry the caller category `MessageQueue`.
  `Azure Event Hub` is never called on the in-memory lanes, so "a name that only calls" does not identify the test.
- **The subject's own calls are not all inside a request.**
  - `Breakfast Provider > CosmosDB` is attributed `TestContext` 74 times of 280 (xUnit in memory), 147 of 353 (Docker)
    and 56 of 257 (Reqnroll). On Reqnroll, 38 of the edge's 257 calls carry the Setup phase. The harness does not
    cross the two counts, so it does not say how many of the 38 are among the 56.
  - A sibling plan traced such calls to the service's background work (the outbox poller, the Cosmos SDK's metadata
    reads), which inherits the phase of the step that started the host (PEER: `SERVICE_CARD_PLAN.harness/probes/card-prototype.cs`).
  - So neither phase nor the caller's name says who caused a call. That is #192's to settle, and it reads this plan's
    roles (section 9).
- **A party's category is per call.** `Breakfast Provider` is uncategorised on HTTP and `gRPC` on 9 calls.

### 2.7 What ingest can be told (READ)

- `kronikol ingest` takes 42 flags and no options file (`src/Kronikol.Tool/IngestCommand.cs:70-331`). The options file
  is `PLATFORM_FOUNDATIONS_PLAN` F4, not green-lit.
- No flag sets `SuiteName`, `ServiceTypeOverrides`, `DependencyColors` or `FixedNameForReceivingService`.
- The Jest recipe (wiki `Ingesting-From-Jest.md`) names the parties as follows:
  - the test is `'Test'` (`:192`, `:212`);
  - the service is `KRONIKOL_SERVICE`, else `'Service'` (`:168`);
  - a dependency is the URL's host (`:224`).
- It hands MSW's mocked and passed-through responses to one function (`:237-238`).

---

## 3. The issue, claim by claim

| Claim | Verdict | Evidence |
|---|---|---|
| The service under test is known only by position | **Holds**, and the position is two option objects that must agree by hand | 2.2 |
| `FixedNameForReceivingService` is a display name | **Holds for the handler's option. The report-level one names no party at all**; its doc is wrong | 2.2 |
| `suite` is the test assembly, null on ingested runs | **Holds**: ingest has no `--suite` | 2.4, 2.7 |
| Doubles are indistinguishable from real parties | **Holds, by design**: the wiki says "it doesn't matter where the dependent service lives" (`Tracking-Dependencies.md:71-80`), and the AI integration prompt asks integrators to track mocked calls as dependencies (`AI-Integration-Prompt.md:10`) | 2.6 |
| `capturedBy` is null on every call | **Holds** (0 of 4,437 set) and is expected: it names a capture path, set only by the OTLP tap and merge | 2.3 |
| Names do not join across reports | **Holds across codebases.** Inside one codebase they are stable across lanes, so the display name is a sound key there and an id is what is missing | 2.6 |
| Ask 1: `ServiceId`, `Parties`, the block, the run line | **Right shape**, with three changes: the subject is found, not configured (5.2); `Kind` is optional per declaration so a run can set only its kinds (5.2); the block lists callers too (5.5) | |
| Ask 1: "the same way through the ingest options file" | **No such file** | 2.7; R2 adds flags (5.9) |
| Ask 2: free-text ids, never normalised | **Agreed.** Kronikol warns about a declared name that differs from a seen one only in case, and changes nothing | 5.2 |
| Ask 3: `kindSource` `declared` or `inferred` | **Agreed, plus `observed`**: what Kronikol or a capturer saw (the test client Kronikol built; a call a capturer marked) is neither a person's word nor a guess | 5.3 |
| Ask 4: one map of hosts, ports and client names to id and kind for all suites | **Partly.** One shared file is right for ids. It is wrong for kinds (they vary by lane) and for endpoint keys (ports vary by lane, 7 of 16 parties have no host, a fake can answer for a production host, client names are never recorded) | 2.6 |
| Acceptance: byte-identical when unset | **Holds by construction** (the block is written only when declared; the compressed writer drops empty keys), pinned by the existing golden tests | 7.4 |
| Acceptance: `services --json` shows every party | **Cannot hold.** `services` lists callees; `Caller` and `Azure Event Hub` (in memory) never appear | 5.7 |
| Acceptance: undeclared parties `unknown`, a footer | **Agreed**, on `parties` and on `services` | 5.7 |
| Acceptance: through ingest via the options file | **No file**; flags instead | 5.9 |
| FLEET S1's default: "`underTest`: `FixedNameForReceivingService`'s party" | **Points at the title option** | 2.2 |
| FLEET §8.1: no per-call kind ("denormalised") | **Right for declarations, wrong for capturers that know per call** (MSW). One optional field, not a field in every capturer | 5.10 |
| Not in the issue: merge, the XML and YAML forms, the schema, history's shard folds, Kronikol4J | **Missing.** A merge can hold two subjects. The data file has four forms and a schema. History builds a run line in seven places, two of which copy members by hand (5.8). The Java port writes reports of its own. The issue's Java stack most likely did not use the port: the issue calls two of its three runs ingested, and the Jest run is one, so the other is the Cucumber-JVM run (inferred: the Reqnroll run has Kronikol's own adapter). Ingest (R2) is then what reaches it | 5.5, 5.6, 5.8, 8 |

---

## 4. The underlying problem

### 4.1 There is no party, only a string

Every reader keys on `serviceName` and `callerName`: the diagrams, the component diagram, the data file, the query
engine, history's `deps`, merge's pairing, `ServiceTypeOverrides`, correlation keys (`CorrelationKeys.cs:14-15`). Each
tracker writes those strings from its own options, so one party's name is decided in as many places as there are
clients that call it (2.2), and nothing records what the string stands for. The issue adds the second map keyed on a
party's name; `ServiceTypeOverrides` is the first. The port map and the client-name map key on what produces a name,
and the category colours key on a category. The map is the right idea; on its own, though, it gives a string a property
without saying what the string is.

### 4.2 Capture drops what it knew

At the moment of the call Kronikol knows more than it keeps:

- the client name the handler was built for (2.1);
- whether the answer came over a socket or from code in the process (2.1, `:387-393`);
- for `CreateTestTrackingClient<T>`, that the caller is the test and the callee is the application `T` (2.2);
- in the Jest recipe, whether MSW answered or let the call through (2.7).

Only the display string and the URI survive, and the URI often has no host (2.6).

### 4.3 The strings are poor keys

- The fallback merges parties: two remote APIs become `localhost:443` (2.1). Loopback addresses becoming
  `localhost` is right, since they are this machine; remote hosts becoming it is not.
- A random port renames a party every run, which history reports as a new dependency every run (2.4).
- One product has several default names, and callers have four default names (2.1).
- The readers this plan touches compare names in three ways (2.5).

### 4.4 What can be known, and by whom

| Fact | Who knows it | When | How sure |
|---|---|---|---|
| A party's id | the team (a catalogue name) | before any run | declared only. Kronikol never invents one |
| Which party is the service under test | the team; Kronikol, when it built the test client for a `WebApplicationFactory<T>`; the calls, by position | per suite; at capture; afterwards | declared, observed, inferred, in that order |
| Which party is the test | the same | the same | the same |
| Whether a party is a double | the team, per lane (the lane decides what is wired) | per run | declared |
| Whether one call's answer came from a double | the capturer, when it saw the double answer (MSW; a handler in the process) | at capture | observed |
| Whether a party is real | the team | per run | declared only. A port proves nothing |

The fix follows the table: every fact is taken from whoever knows it, at the time they know it. Each fact is recorded
with how it became known, and a reader is never handed a guess dressed as a declaration.

---

## 5. The design

### 5.1 One resolver, three inputs

`Kronikol.Parties.PartyResolver`, in core (`src/Kronikol/Parties/`), is a pure function. Its inputs are:

- the run's calls;
- the declarations (5.2);
- the observations (5.3).

It returns the parties table. Every reader calls it: the report writer, merge, the query engine, and #191's card (at
kronikol-ed's request, so a card written later from a report matches the one written at the end of the run).

- The writer resolves once, before the outputs are written in parallel (`ReportGenerator.cs:932`), and hands every
  output the same table and diagnostics. Resolved inside each output, a diagnostic would reach some files and not
  others.
- It reads the parties file into its own state, never into the caller's options object, which the generator already
  changes in place (`ReportGenerator.cs:295-297`, `:305-306`).

Each field of a row is resolved separately, in this order:

1. a declaration in the run's options;
2. a declaration in the parties file;
3. an observation;
4. the inference (5.4);
5. `unknown`.

### 5.2 Declarations

**`ReportConfigurationOptions`:**

- `ServiceId` (`string?`): the id of the subject. The subject's row takes it, whichever way that row was found
  (5.4). This is the one-line declaration most suites need.
- `Parties` (`IList<PartyDescriptor>`, empty by default, the shape of the one list option there is,
  `HistoryShapeTemplates`, `ReportConfigurationOptions.cs:810`), with
  `PartyDescriptor { string Name; string? Id; PartyKind? Kind; string? Note }`.
  - `Kind` is nullable, unlike the issue's: a declaration that says nothing about the kind must not overwrite one that
    does. A lane then declares only its kinds, and the shared file only its ids.
- `PartiesFilePath` (`string?`): a JSON file with the same rows. When unset, the path is looked up the way the history
  ledger's is (`HistoryFilePath`, `ReportConfigurationOptions.cs:704`): `KRONIKOL_PARTIES`, then
  `<repository root>/.kronikol/parties.json` if it exists (Q4).
  ```json
  { "partiesFormatVersion": 1,
    "parties": [ { "name": "Cow Service", "id": "cow-service", "kind": "double", "note": "Dependencies.Fakes.CowService" },
                 { "name": "CosmosDB", "id": "orders-store" } ] }
  ```

**Reading the file:** it is JSON only, for the reason `FLEET_EVIDENCE_PLAN` Q3 gives for specs and overlays (the tool
has no YAML reader and no third-party dependency).

- Kinds are read ignoring case (`"Double"` is `double`).
- An unknown kind gives a diagnostic and leaves that field undeclared.
- A name listed twice gives `PartyDeclarationConflict`.
- A file whose `partiesFormatVersion` is newer than the build reads, or that does not parse, gives a diagnostic and is
  not used. It never fails the run, as a damaged history ledger never does.

**Matching rules:**

- Rows match a call's name exactly (ordinal), the comparison history's `deps` uses.
- A declared name that matches no recorded name, but matches one when case or surrounding white space is ignored,
  gives one diagnostic (`PartyNameNearMiss`) naming both. It never matches.
- Names declared in the options that no call carries are listed in one diagnostic (`DeclaredPartyNotSeen`). Its text
  says that a run of only some of the tests can cause it.
- Unseen names from the file are silent: a shared file names parties that most suites never call.

**When the diagnostics are written:** only by a run that declares something.

- A report that declares nothing keeps its bytes, and gains no Diagnostics tab and no labs page (the labs page appears
  whenever a report has any diagnostic, `LabsReportGenerator.cs:31-32`).
- On such a report the `parties` verb runs the same checks and prints them as notes, `TestPartyNameInsideHost`
  included.
- The new kinds are appended to the public `DiagnosticKind`, never renumbered (`DiagnosticEntry.cs:6-9`). None is
  answer-affecting (`QueryCommand.cs:393-406`), so none raises a `!` banner. `ProvenanceBannerTests` holds each to no
  banner, as it holds the wording kinds (`:57-61`), and the schema's contract fixture lists them
  (`TestRunReportSchemaContractTests.cs:294-297`).

**Conflicts:** two declarations disagreeing about one field at the same level give `PartyDeclarationConflict`, and the
later one in the list wins. A `ServiceId` different from the declared id of the subject's row gives the same
diagnostic, and `ServiceId` wins.

**Kinds:** the issue's five.

- `real`: the party's own software produced the answer. A deployed instance counts, and so does its real image run for
  the test, such as a Testcontainers PostgreSQL.
- `double`: anything else that answered in its place. Stubs, fakes, recordings, MSW handlers and vendor emulators all
  count (Q13). `Note` carries what it was.

**The in-process subject:** when the test calls the service's code directly (a Jest test of handler functions; a .NET
test of a class with no host), the test and the subject are one participant. Declaring that participant `underTest`
covers it. The table then has no `test` row, and #192 reads its calls as the subject's (PEER, kronikol-bf).

### 5.3 Observations

**O1, the test client Kronikol builds.**

- `CreateTestTrackingClient<T>` records three facts in a run-level registry, beside `TrackingComponentRegistry`:
  - its handler's caller name;
  - the callee names that handler resolves;
  - the assembly of `T`.
- At report time the caller name is `test` and the callees are `underTest`, both `observed`. The row's `note`, when
  none is declared, names the application (`BreakfastProvider.Api`).
- This makes `ServiceId` alone safe in the common setup, where inference would also be right (5.4). It also covers the
  cases where inference is wrong: a renamed test client, or a service whose trackers were left at `"Caller"`.
- The registry is filled from parallel tests, so it is a concurrent collection, one per process. A run is one process,
  so nothing in a consumer's path needs to clear it. It gets a `Clear()` beside `TrackingComponentRegistry.Clear()`
  (`TrackingComponentRegistry.cs:35`), which only Kronikol's own tests call.

**A check on the test party.** A call attributed `RequestHeader` was made by a host while it served a request the test
sent (`AttributionSource.cs:11`). If such a call carries the test party's name as its caller, the host's trackers were
left at the test's name, which is the default `"Caller"` of every tracker (2.1). The table cannot split one name into
two parties. One diagnostic, `TestPartyNameInsideHost`, counts those calls and names the setting to change (each
tracker's `CallerName`). On BreakfastProvider it would stay silent: all 451 of `Caller`'s calls are `TestContext`
(RUN). #192 relies on it, because `origin` would otherwise read those calls as the test's.

**O2, the per-call mark** (R2, 5.10): a party all of whose calls carry a capturer's `testDouble` mark is `double`,
`observed`.

### 5.4 Inference, when nothing is declared or observed

The rule is a pure function of the calls, so it runs the same on a 4.x JSON report, a 5.x `.krkl` and a fresh run
(`bp-reports/infer.py`).

- **The test parties:** every name that is never called and whose calls carry no caller category but `User`.
  - A broker's delivery carries `MessageQueue`, so it is excluded.
  - A user of an ingested UI test, `User`, is kept.
  - Where the calls record how they got their scenario, a test party's calls are mostly attributed `TestContext`. A
    scheduler or hosted service that calls the service is `Scope`, `Detached` or `None`, and is left out. If no
    candidate qualifies, the one with the most `TestContext` calls is kept.
  - Where nothing records it (an ingest without `attributionSource`), every candidate qualifies.
- **The subject:** among the test parties' callees, leave out stores.
  - A store is a callee whose category resolves to a database, cache, queue, storage or AI type
    (`DependencyPalette.Resolve`, `DependencyPalette.cs:62-67`), or is statement-shaped
    (`DependencyCategories.IsStatementShaped`, `DependencyCategories.cs:96-97`; it adds Atlas Data API, which the
    palette leaves unmapped). Both ignore case.
  - A call with no category resolves to an HTTP API, so it can be the subject. So can a category neither list knows,
    such as a custom or an ingested one: it resolves to `Unknown`, and the diagram draws it as a plain participant
    as well.
  - Prefer a callee that makes calls of its own, then the one with the most calls.
  - A second subject is kept only when it too makes calls and takes at least a quarter of the test parties' calls.
- **Everything else:** `unknown`. Inference never says `real` or `double`.

Results (RUN):

- BreakfastProvider's four lanes all give `Caller` and `Breakfast Provider` (`results-infer.txt`), as kronikol-ed's own
  rule did (PEER). Both result files were made again on 2026-10-27 with the store list taken from the palette, and
  nothing changed.
- On ten synthetic shapes (`cases.py`, `results-cases.txt`), nine are right:
  - the Jest recipe;
  - an ingested UI test;
  - two subjects;
  - a service whose trackers keep the default caller name;
  - a test seeding a store through its own client;
  - a test that calls only stores;
  - a consumer test with no test client;
  - two test clients with different caller names;
  - a scheduler that calls the service beside the test.
- The tenth is the in-process subject. The rule names its HTTP dependency as the subject. From the calls alone it is
  indistinguishable from an external-service lane (one pure caller, one callee that makes no calls), so only a
  declaration settles it. It is labelled `inferred`, which says exactly that.

### 5.5 In the data file

Written only when something is declared (`ServiceId`, `Parties` or a parties file). Otherwise nothing is written, and
readers run the resolver over the report itself.

Placement and shape:

- In the data file (and so the `.krkl`): top level, after `ciMetadata` and `environment`, before `features`, so `head`
  and a streaming reader see it before the megabytes. That is the reason the code gives for `ciMetadata`'s place
  (`ReportGenerator.cs:5498-5499`).
- In the mergeable file: after `environment`, which that file writes last (`:5806-5809`). A merge reads the file
  whole, so the place costs nothing there.
- Rows are sorted by name (ordinal), and members that hold nothing are left out.
- There is one row for every distinct `serviceName` or `callerName` among the calls, markers excluded. Callers are
  listed too.

```json
"serviceId": "breakfast-provider",
"parties": [
  { "name": "Breakfast Provider", "id": "breakfast-provider", "kind": "underTest", "kindSource": "declared", "note": "BreakfastProvider.Api" },
  { "name": "Caller", "kind": "test", "kindSource": "observed" },
  { "name": "CosmosDB", "id": "orders-store", "kind": "double", "kindSource": "declared", "note": "in-memory fake" },
  { "name": "Kafka Broker", "kind": "unknown" }
]
```

Every form of the file gets the block:

- the standard JSON;
- the `.krkl`;
- the mergeable file (`kronikol merge` needs it);
- XML and YAML, in their own idiom;
- the JSON schema (`ReportGenerator.cs:7401`) and the XSD (`:7990`), with `name` and `kind` required in a row.

**The schema is closed.** Every object in it that declares properties gets `additionalProperties: false`
(`CloseDeclaredObjects`, `ReportGenerator.cs:7903`, `:7937-7954`), and has since 3.4.0. So:

- `serviceId`, `parties`, a row's five members and a call's `testDouble` are each declared, with a description
  (`TestRunReportSchemaContractTests.cs:43` requires one). Its fixture (`:188`) gains a declared party and a marked
  call, or `Every_key_the_json_writer_emits_is_declared_in_the_schema` (`:24`) never sees them.
- None goes into the root's `required` (`:7437`), so the `.krkl` pruner still drops them when empty
  (`ReportJsonPruner.cs:7-12`).
- The XSD's sequences are ordered (the root at `:8255-8313`, a call at `:8048-8094`), so each new element takes a fixed
  place with `minOccurs="0"`.
- A consumer who validates against a copy of an older schema rejects a report that declares parties or marks a call.
  Validating against the schema written beside the report passes. The changelog says so. The schema file is written
  only when `GenerateTestRunReportSchema` is on, which it is not by default since 5.0.

**The rows hold no arrays.** The query engine's scanner opens an entity at any array named `features`, `scenarios`,
`httpInteractions`, `annotations`, `attachments`, `diagnostics` or a step array, at any depth
(`ReportScanner.cs:423-466`). An array of those names inside the block would be read as report data by every released
tool.

Row members are fixed at five. Calls in and out are the query engine's to count, not the file's (#191's card counts
them). So is a party's category: it is per call (2.6), and a reader that wants one takes the commonest.

Two names that differ only in case or punctuation are two rows here, but share one participant in the diagram
(`ROADMAP.md` Appendix C, `LONG_COMPONENT_EDGE_PLAN`'s audit). `PartyNameNearMiss` flags the case of such a pair.

**Older readers are not disturbed (RUN, `compat/`).** A real report (BreakfastProvider's xUnit in-memory lane) was given
the block and a `testDouble` on 98 records. The 5.11.0 tool and the 5.4.1 tool BreakfastProvider pins then ran six
readers on it and on the untouched copy: `summary`, `services --json`, `interactions --json`, `flow`, `grep` and
`ctrf`. Each answered the same as it did on the copy, but for the file's path and size, and exited 0. The tool's readers
ignore what they do not know.

A table with no dependency rows means none were captured. It never means there were none. The card words this
(`capture.subjectCalls: notRecorded`, PEER).

### 5.6 Merge

- A row present in several shards keeps each field they agree on.
- A field they disagree on is left out, the row's kind becomes `unknown`, and a `PartyDisagreement` diagnostic names
  the shards.
- `serviceId` is kept only when every shard has the same one, a shard without one included, which is the rule
  `suite` and `environment` already follow (`MergeableReportMerger.cs:28-35`, `:49-55`).
- A merge of two suites therefore has two `underTest` rows, each with its own id, and no top-level `serviceId`.
- Rows a shard inferred are inferred again over the merged calls.
- The block rides through `MergeableReport`, its reader, the merger and the renderer's `Model()`
  (`MergeableReportRenderer.cs:219-248`), which passes the run's facts by name and needs the new ones. A shard
  disagreement is reported where the others are (`MergeableReportMerger.cs:466-503`).
- An older `kronikol merge` reads the shards with `TryGetProperty` throughout, so it drops the block, and it reads a
  diagnostic kind it does not know as `Other` (`MergeableReportReader.cs:657`), which does raise a banner. The
  changelog says to move the tool with the library.

### 5.7 Query

**A new verb, `parties`:**

- `kronikol query parties <report> [sN] [--kind K] [--json]`, one `VerbTable` line in the Overview group.
- Every party, callers included, with id, kind, how the kind is known, calls made, calls received and note.
- The footer: `N parties of unknown kind: declare them in Parties or .kronikol/parties.json`.
- On a report with no block it shows the inferred table, and its footer says the report declares nothing.

**`services`:** gains `id` and `kind` (text and `--json`) only on a report that has the block. Its output for every
existing report stays byte for byte as it is, so the query conformance corpus (roadmap 14.3) does not move.

- The new columns go before `statuses`, which stays last: it is the one of variable width
  (`QueryCommand.Overview.cs:389`, `:486`), and `CapCensusTests.cs:135` reads a row's end.
- In `--json` they are added with `WithMember` (`QueryWriter.cs:585`), the way a member only some items carry is added
  today.
- A name is looked up in the table ignoring case, as `services` groups (`:353`).

**`summary`:** one `service:` line after the times line (`:69`), and `run.serviceId` in `--json`, only on a report that
has the block. The pinned `run:` line (`QueryCommandTests.cs:152`) is untouched, and so is `summary` on every other
report: the inferred table is `parties`' to show.

**Registering the verb:** the suite holds every verb to several lists, and each fails until the verb is in it:

- `VerbTable` (its usage line opens with two spaces and the verb, `VerbTable.cs:17`) and the dispatch
  (`QueryCommand.cs:253-279`);
- `DescribeTests.cs:54-69`;
- `SkillDriftTests`: the usage list (`:56-58`), the skill's example and reference entry (`:86`, `:98`), the `--json`
  row (`:405`) and both copies of the skill (`:414`);
- `QueryCultureTests.cs:69` and `:124-126`, `CountFlagTests.cs:74-85`;
- `KrklQueryTests.cs:32`, where a report with no block must answer the same as its `.krkl`, which drops the empty
  keys (`:45`).

The Python fallback script keeps its six verbs (`templates/skills/kronikol-test-debugging/scripts/query.py:631-637`);
`parties` is not one of them.

### 5.8 History and `Run.json`

- `serviceId` goes on the run line and into `Run.json` when set, and is absent otherwise. It is an optional member of
  the existing run line, never a line kind of its own and never a reason to move `historyFormatVersion`. Ledgers are
  shared by suites on different Kronikol versions, and every released build behaves as follows (READ, confirmed with
  kronikol-bf):
  - It reads a run line's members by name (`HistoryJson.ParseRun`, `:417`), so it skips one it does not know.
  - It counts a line kind it does not know as damage (`HistoryLedger.cs:393-395`). `history doctor` then reports a
    damaged line (`HistoryCommand.Maintenance.cs:354-356`), and `history verify` calls it unreadable, with the message
    the parser throws (`HistoryLedger.cs:474`, `HistoryJson.cs:373`).
  - It refuses the whole ledger when `historyFormatVersion` is higher than its own (`HistoryLedger.cs:359-361`).

  `Run.json`'s reader also ignores unknown members (2.4).
- A run line is built in seven places. Five of them must carry the new member, or they drop it silently: the member is
  optional, so a site that is missed still compiles (READ):
  - `HistoryRunBuilder.cs:126`, the run's own line;
  - `HistoryJson.cs:431`, the parser, and its writer `HistoryJson.WriteRun` (`:127`, beside `deps` at `:202`);
  - `HistoryLedgerWriter.cs:486` and `:610`, the two folds of a run's shards, which list every member by hand;
  - `QueryCommand.History.cs:608`, the line `kronikol query history` builds from a report, which takes it from the
    report's block.

  The other two are the imports of other formats' runs (`HistoryCommand.Maintenance.cs:559` for CTRF, `:627` for
  Allure). Those carry no block, so they have no `serviceId` to keep.

  Shards are grouped by suite and run id before they are folded (`HistoryLedgerWriter.cs:390-397`). The folded line
  keeps `serviceId` only when every shard has the same one, which is the merge's rule (5.6).
- The member is a plain `init` property of `HistoryRun`, not a `required` one as most are (`HistoryModel.cs:165-209`),
  so the two import sites and every test that builds a run compile unchanged.
- An older tool that rewrites a ledger drops the member from every line it writes: `history prune` and `compact`
  (`HistoryLedgerWriter.cs:288-300`) and `amend` (`:118`) re-serialise each run from what they parsed, and the
  ledger's version (`HistoryFormat.cs:28`) does not stop them. Only past runs' ids are lost, and the next run writes
  its own. The changelog says so.
- It is not part of a stream's key: a stream is still its suite, as `Peek` and the folds group
  (`HistoryJson.cs:306-317`, `HistoryLedgerWriter.cs:390-397`).
- `Run.json` is built in two places, `ReportGenerator.cs:781-791` and `RunRotation.cs:560-571`, and its ten members are
  pinned in order (`RunManifestTests.cs:98`); the member is added after them, only when set. A merged run writes no
  `Run.json` (`MergedRunOutputs.cs`).
- `Failures.jsonl`'s header carries `suite` (`FailuresDigestGenerator.cs:936`), and gains `serviceId` the same way, so
  a script reading failures from several services' runs can tell them apart.
- Party ids on the run line are Q10. `deps` stays display pairs, so the new-dependency signal does not change meaning.

### 5.9 Ingest (R2)

- `--service-id <id>` and `--parties <file>` (the file of 5.2), and discovery of `.kronikol/parties.json` above the
  directory ingest runs in, as history does.
- `--suite <name>`, so an ingested run is no longer suite-less (2.4).
  - It sets `SuiteName` (`ReportConfigurationOptions.cs:51`), which the pipeline and the pointer ingest prints both
    resolve (`IngestCommand.cs:561-567`; that comment warns they agree today only because neither has a suite).
  - Giving an ingest a suite changes every scenario's stable id, which the suite prefixes (`ScenarioStableId.cs:44`),
    and starts a new history stream: the runs ingested before it are under no suite. That is what a suite is for, and
    the changelog and the ingest page say it before anyone sets the flag.
- Each flag requires its value. None takes an optional one the way `--attribute-by-window` does
  (`IngestCommand.cs:282-288`), which would read a following input path as the value.
- When F4's options file lands it carries `serviceId` and `parties` in this shape. `PLATFORM_FOUNDATIONS_PLAN` §4.7 is
  amended to say so.

### 5.10 The per-call mark (R2)

**The field:** `testDouble`, an optional string naming what answered (`"MSW"`, `"WireMock"`, a handler's type name).

- It goes on `InteractionRecord`, on `RequestResponseLog`, and on a call in the data file, written only when set.
- A capturer writes it only when it saw a double answer. An unmarked call is unknown, never real.

**Its shape, held by what already reads a call:**

- It is a string, and stays one. Every released scanner reads any scalar under a call as the call's own member
  (`ReportScanner.cs:705-709`), from a switch of 21 names with no default (`:771-854`). An object here would have its
  members read as the call's: a `serviceName` inside it would rename the call, and a number under a name read as a
  string stops the tool (`:776-780`). R2 also gives the scanner the guard scenarios got in 4.16.3 (`:924-928`), so a
  call reads only its own members from then on.
- In the data file it is the call's last member, after `stepPath` (`ReportGenerator.cs:5922`), written only when set
  (`WhenWritingNull`, as `warmUp`).
  `WarmUpDataTests.cs:60` pins `warmUp` straight after `durationMs`, and the pin file (`TestRunReport.pin.json`) keeps
  its bytes.
- On `RequestResponseLog` it is a property, not a positional parameter: the record is public and positional, so a new
  parameter would change its constructor. `RequestResponseLogRoundTripTests` lists it in `Carried` (the build fails
  otherwise, `:14`).
- In the NDJSON record a value that is not a string is read as its text, as `error` is (`AnyJsonAsTextConverter`,
  `InteractionRecord.cs:67`). `InteractionRecord.Pair` (`:544`) gets an overload.
- A wire and span pair merged at ingest keeps the wire record and takes identity from the span
  (`InteractionMerger.cs:315-325`). The merged call takes `testDouble` from the wire record, else the span.
- The OTLP export writes it as `kronikol.double`, beside `kronikol.captured.by` (`OtlpSpanMapper.cs:231`); nothing
  reads either back. Not `kronikol.test.*`, which is the test's identity.

**What the resolver does with it:**

- A party all of whose calls are marked is `double`, `observed`.
- A party some of whose calls are marked keeps its declared or unknown kind. How many were marked is the `parties`
  verb's to count (`doubleCalls` in `--json`), as calls in and out are, so the row keeps its five members (5.5).
- A declared `real` party with marked calls gives `PartyDeclarationConflict`. Each marked call is still a double's
  answer.

**The Jest recipe:** passes `testDouble: 'MSW'` on `response:mocked` and nothing on `response:bypass`.

**The contract:** the field enters before `captureFormatVersion 1` freezes (14.5, rule 6). `PLATFORM_FOUNDATIONS_PLAN`
§4.7 is amended: the party table stays an options section, and this one field is the only stream field.

### 5.11 In-process doubles in .NET (R3, optional, Q8)

The HTTP handler sets `testDouble` to the type name of the handler at the end of its chain, when that handler is none
of `SocketsHttpHandler`, `HttpClientHandler` and TestServer's handler. It already walks the chain (`:387-393`).

The limits, stated in the docs:

- An interceptor in the middle of the chain is not seen (JustEat's `InterceptingHttpMessageHandler` delegates on).
- A stub on a socket (WireMock) is not seen.
- Nothing is ever inferred from a port.

### 5.12 What #191 and #192 can rely on

- **The vocabulary:** kinds `test`, `underTest`, `real`, `double`, `unknown`, and `kindSource` `declared`, `observed`
  or `inferred`.
- **The data:** `serviceId` and `parties` in the data file when declared, and the resolver in core when not.
- **#192:** `origin` is the caller's role read from this table (`test`, `underTest`, else other). #192 keeps a separate
  `cause` axis (PEER).
- **#191:** the card runs the resolver itself, so a backfilled card matches one written at the end of the run.

### 5.13 What does not change

- **Diagrams:** a double is still drawn as the party it stands for, as the wiki intends, with one addition the owner
  asked for (Q11): a party declared `double` carries the stereotype «test double» on its participant, in the sequence
  and the component diagrams. An observed or inferred kind draws nothing, so no suite's diagrams change until it
  declares a double.
- **Names:** every name a tracker writes is unchanged, but for R0's fallback.
- **Existing reports:** nothing is added to a report that declares nothing.

### 5.14 Designs not taken

- **Declarations keyed on endpoints** (host, port, client name). Ports vary by lane, most non-HTTP parties have no host,
  a fake can answer for a production host, and client names are not recorded (2.6).
- **An id or kind on every call.** The table states each fact once. The one per-call field is the observation only a
  capturer has (5.10).
- **Inferring `real`, or a double from a port.** A port proves nothing.
- **Normalising ids** (lower case, slugs). The catalogue's name is the id.
- **A sixth kind for emulators or containers.** It changes no reader's trust; `note` holds it (`FLEET_EVIDENCE_PLAN` Q1).
- **A block on every run, inferred rows included.** It changes every report's bytes, every golden pin and Kronikol4J's
  parity, for a table any reader can compute.
- **Moving naming into the renderer now** (`PLATFORM_FOUNDATIONS_PLAN` F3). That is the right end state and a
  platform's worth of work. The resolver is where it will plug in.

---

## 6. Releases

**R0, a minor** (bug fixes, and history's bridge, which adds a member to the ledger's run line and so is new; the
behaviour changes are called out in the changelog per `CLAUDE.md`). Written as a patch; the owner chose the bridge on
2026-10-27 (Q2), which made it a minor:

- The fallback name keeps the request's host when it is not a loopback address: `pricing.example.com:443`.
  - A loopback address (`localhost` in any case, `127.0.0.1`, `[::1]`: `Uri.IsLoopback`) keeps today's
    `localhost:{port}`. One stub reached two ways stays one party, and a lane whose unmapped calls all go to this
    machine sees no change. `Falls_back_to_localhost_port_when_port_not_in_mapping` stays as it is (Q2).
  - The host is `Uri.Host` as .NET gives it (RUN, PowerShell on .NET 10): ASCII lower-cased, IPv6 in brackets with no
    zone id, a Unicode name as written. Not `IdnHost` or `DnsSafeHost`, which drop the brackets and keep the zone.
  - A trailing dot (`localhost.` is not loopback) or a punycode spelling is another host, so another party; a mapping
    joins them. A request with no host keeps today's name.
  - `ResolveServiceName` is given the request's URI in place of its port (`:72`, called at `:239`). The port map is
    still looked up by the port alone, as documented. The handler routes no other test's call through the fallback
    (READ: every test, example and template that reaches it maps or fixes the name, or calls `localhost`), and a
    `WebApplicationFactory` client's address is `http://localhost`, so it is unchanged.
- **History: the bridge** (`CALL_OPERATION_AND_ORIGIN_PLAN.md` §4.8 part 1, agreed with kronikol-bf, who designed it).
  The callee's name is part of every call's fingerprint (`InteractionShape.cs:231`), and a version move used to restart
  every scenario's window (kronikol-bf: the `SameRule` filter runs upstream of the previous shape, the unstable count,
  the alternating memory and the count stretch, `HistoryAnalyzer.cs:492-498`), in every suite, renamed or not.
  - `InteractionShape.Version` moves from 6 to 7, and rule 6 is kept as a function of the raw call: a callee the
    handler named by its remote-host fallback reads as `localhost:{port}`, and nothing else differs. The handler marks
    such a call with an internal, never-serialized `RequestResponseLog.NamedByRemoteHostFallback`, so rule 6 never
    guesses from a name: an ingested name of the same form kept that name under 6 too.
  - Each run made in process writes `bridge` on its run line, keyed by rule (`"6"`, or `"6+<rules hash>"` with
    consumer rules): the run's set and ordered fingerprints, call sets (into a shapes line of the rule's own) and the
    dependency pairs the rule names otherwise. A member left out holds the run's own, so a suite R0 renames nothing in
    writes `"bridge":{"6":{}}`, and only while runs under 6 are in the window. A fragment bridges every kept rule;
    the in-process append and `history record` keep only the rules their window holds.
  - The analyzer compares two runs under one rule: their own when they share it, else through the bridge one of them
    wrote. That holds for the previous shape, the unstable count (a pair neither run bridges is skipped), the
    alternating memory, the count stretch, the calls the evidence names and `NewDependencies`. A pair no bridge reaches
    reads as every pair across a move did.
  - Across the move, runs compare as rule 6 saw them: two remote hosts that 6 called one party still compare as one,
    and a new host on the port of a renamed one is not a new dependency on that first run, though the scenario that
    calls it reads changed and names the call. The evidence spells calls as rule 6 did.
  - Prune, compact, amend and both folds (shards and attempts) carry the bridge and its shapes line; `history verify`
    checks a bridge as it checks a run's own arrays. A run rebuilt from a report (`kronikol merge --history`) bridges
    nothing: what tells rule 6 apart is not in the report, and a guess could only add false verdicts.
  - Left to #192's R1, as agreed: consumer patterns recorded and bridged, the tool version on run lines, writers
    carrying members they do not know, the loader skipping unknown kinds, `record`'s warning, the guard corpus, and a
    fold of shards made under two rules (it still blanks, as today).
- **The changelog** names what moves once, on a suite with unmapped remote hosts: `query diff` lists the old name as no
  longer tracked; `diff --body` cannot pair those calls across the upgrade; `dep:localhost` stops matching them; and a
  `ServiceTypeOverrides` key such as `localhost:443` stops matching (`PlantUmlCreator.cs:1314`). History does not move.
- **Drawing the new names** (checked by a fact before anything was changed):
  - A participant's name was wrapped but not creole-escaped (`PlantUmlCreator.cs:1436-1437`), while a note's is
    (`:532-564`). The fact (3c) was red: `xn--e1afmkfd.xn--p1ai:443` drew as `xn`, a struck-through `e1afmkfd.xn` and
    `p1ai:443`. Any name with two `--`, `**`, `//`, `__` or `~~` was restyled the same way, and a name holding a `"`
    turned the whole sequence diagram into PlantUML's Syntax Error picture (found by the same fact). R0 escapes every
    drawn line of a name as a note's line is, and writes a quote as `<U+0022>`, in the sequence diagram, the component
    diagram and the component diff (`ComponentDiagramDiffer.cs`, which wrote names raw as well). A name with nothing
    creole acts on is unchanged, so no golden moves.
  - An alias may now start with `_` (`[fe80::1]:8080` gives `_fe80__1__8080`). The fact renders one through the engine;
    it draws.
- `ReportConfigurationOptions.FixedNameForReceivingService`'s doc says what it does: it titles the report.
- The `CapturedBy` doc stops saying that the taps write `wire`. No Kronikol capturer does; merge works out which side a
  record is (`InteractionMerger.cs:165-185`), and an external capturer may write `wire` itself
  (`Ingesting-External-Captures.md:36`). The same text is in `RequestResponseLog.cs:118-123`,
  `InteractionRecord.cs:219-224`, `InteractionMerger.cs:31` and the schema's description (`ReportGenerator.cs:7880`).
- The doc of `MessageTrackerOptions.ServiceName` says it names the tracker in diagnostics only
  (`MessageTracker.cs:51`, `:91`), not the destination.
- The doc of `TrackingDefaults.CallerName` (`:10`) says it is the in-process trackers' default, not every class's.
- Other source text that describes the fallback: the comment at `TestTrackingMessageHandler.cs:112`,
  `TestTrackingMessageHandlerOptions.cs:22` ("instead of inferring from the port"), and the diagnostic page's
  unmatched-client text (`DiagnosticReportGenerator.cs:261`).
- The wiki, 15 lines on 13 pages (READ, `origin/master`):
  - `HTTP-Tracking-Setup.md:42-43`, `Tracking-Dependencies.md:182` and `:699`, `Diagnostics-and-Debugging.md:586`;
  - the same line on ten integration pages, "Unmapped ports appear as `localhost_80`, `localhost_5001`, etc.", which
    is wrong today as well (`localhost_80` is the alias; the diagram shows `localhost:80`): `Integration-xUnit3.md:371`,
    `Integration-xUnit2.md:515`, `Integration-NUnit.md:369`, `Integration-TUnit.md:357`,
    `Integration-LightBDD-TUnit.md:339`, `Integration-LightBDD-xUnit2.md:326`, `Integration-LightBDD-xUnit3.md:344`,
    `Integration-ReqNRoll-TUnit.md:348`, `Integration-ReqNRoll-xUnit2.md:344`, `Integration-ReqNRoll-xUnit3.md:342`;
  - a defect beside them, fixed in the same edit: `Tracking-Dependencies.md:192-201` gives both named clients
    `https://` addresses (port 443), and `:227-231` and `:248-252` then map Auth to port 8080.
- Kronikol4J: a ledger entry, and the same fallback fix in `ServiceNameResolver` (Q12).
  - Its `resolve(int port)` (`ServiceNameResolver.java:46`, `:88-90`) takes the host too, at four call sites
    (`KronikolOkHttpInterceptor.java:108`, `TrackingHttpClient.java:201`, `KronikolWebClientConnector.java:85`,
    `KronikolWebClientFilter.java:94`). A `resolve(int port)` overload keeps meaning `localhost`, so its 11 existing
    test calls stay green.
  - The host is put in .NET's form, which Java's libraries do not give: IPv6 in brackets (OkHttp's `host()` has
    none), lower case with `Locale.ROOT` (`URI.getHost()` keeps the case written), loopback as `localhost`, and
    today's name when `getHost()` is null (an underscore or non-ASCII host). From the libraries' documentation, not
    run.
  - Docs: its comment's .NET line numbers (`:9`, "63-105", now 72-114), `docs/REMAINING_PARITY.md:428-433`, and the
    template comment "the receiving service is inferred from the host/port" (`SampleComponentTest.java:23` and its
    Maven twin), which is false today and true after R0.

**R1, a minor:** 5.1 to 5.8.

- New options: `ServiceId`, `Parties`, `PartiesFilePath`.
- A new block in the data file, in every form.
- Merge, the `parties` verb, and `services`' columns.
- `serviceId` on the run line and in `Run.json`.
- `summary`'s service line, and `serviceId` in `Failures.jsonl`'s header and in CTRF (Q16), each only when declared.
- O1.
- A Kronikol4J ledger entry (report output).
- Public methods gain overloads, never new optional parameters: a new optional parameter breaks a compiled caller, and
  no project in the repository has an API baseline or package validation to catch it. In R1 that is
  `GenerateTestRunReportData` (`ReportGenerator.cs:5242`), `HistoryRunBuilder.Build` (`HistoryRunBuilder.cs:45`) and
  `CtrfReportGenerator.Generate`; in R2, `InteractionRecord.Pair`.

**R2, a minor:** 5.9 and 5.10.

- Three ingest flags.
- The `testDouble` field: record, log, data file and OTLP export, and the scanner's guard on a call's members.
- The Jest recipe on the wiki.
- `PLATFORM_FOUNDATIONS_PLAN` §4.7 amended.

**R3, optional (Q8), a minor:** 5.11.

**Where the code goes** (paths under `src/Kronikol/` unless named):

| Release | Files |
|---|---|
| R0 | `Tracking/TestTrackingMessageHandler.cs` (the fallback is given the request's URI, not its port, and marks what it named) and `Tracking/RequestResponseLog.cs` (the mark); `History/InteractionShape.cs` (version 7, rule 6 kept); the bridge in `History/HistoryModel.cs`, `History/HistoryBridges.cs` (new), `History/HistoryRunBuilder.cs`, `History/HistoryJson.cs`, `History/HistoryAnalyzer.cs`, `History/HistoryRunContext.cs`, `History/HistoryLedgerWriter.cs`, `History/HistoryLedger.cs` and `src/Kronikol.Tool/HistoryCommand.cs`; the name escape in `PlantUml/PlantUmlCreator.cs`, `ComponentDiagram/ComponentDiagramGenerator.cs` and `ComponentDiagram/ComponentDiagramDiffer.cs`; the doc comments in `ReportConfigurationOptions.cs`, `Tracking/RequestResponseLog.cs`, `Tracking/MessageTrackerOptions.cs`, `Tracking/TestTrackingMessageHandlerOptions.cs`, `Constants/TrackingDefaults.cs`, `Ingestion/InteractionRecord.cs` and `Ingestion/InteractionMerger.cs`, the schema's `capturedBy` description in `Reports/ReportGenerator.cs`, and the text in `Reports/DiagnosticReportGenerator.cs`; `tests/Kronikol.Tests/Tracking/TestTrackingMessageHandlerTests.cs`, `tests/Kronikol.Tests/History/InteractionShapeTests.cs`, `tests/Kronikol.Tests/History/HistoryBridgeTests.cs` (new), `tests/Kronikol.Tests/Tool/HistoryCommandTests.cs`, `tests/Kronikol.Tests.PlantUml.Ikvm/HostNamedParticipantTests.cs` (new); Kronikol4J's `ServiceNameResolver.java`, its four callers and its test; the 13 wiki pages |
| R1 | a new `Parties/` folder (`PartyDescriptor`, `PartyKind`, `PartyResolver`, `PartiesFile`, the test-client registry); `ReportConfigurationOptions.cs`; `WebApplicationFactoryExtensions.cs` and `Tracking/TestTrackingMessageHandler.cs` (O1); `Reports/ReportGenerator.cs` (the standard and mergeable models, XML, YAML, the JSON schema and the XSD); `Reports/Merge/` (the reader, the merger, the renderer); the query engine's report index, `Query/QueryCommand.Overview.cs` (`services`, `parties`), `Query/VerbTable.cs`, `Query/QueryCommand.History.cs`; `History/` (`HistoryModel.cs`, `HistoryRunBuilder.cs`, `HistoryJson.cs`, `HistoryLedgerWriter.cs`); `Reports/RunManifest.cs` and `Reports/RunRotation.cs`; `Reports/FailuresDigestGenerator.cs`; `Reports/CtrfReportGenerator.cs` and `src/Kronikol.Tool/CtrfCommand.cs` (Q16); `Query/QueryCommand.Overview.cs` (`summary`); `Reports/DiagnosticEntry.cs` (the kinds); both skill copies |
| R2 | `src/Kronikol.Tool/IngestCommand.cs`; `Ingestion/IngestPipeline.cs`, `Ingestion/InteractionRecord.cs`, `Ingestion/InteractionMerger.cs` (a merged call keeps either view's `testDouble`); `Tracking/RequestResponseLog.cs`; `Reports/ReportGenerator.cs` (the call's member and the schema); `Query/ReportScanner.cs` (the guard); `src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs`; the wiki's two ingest pages; `PLATFORM_FOUNDATIONS_PLAN.md` §4.7 |
| R3 | `Tracking/TestTrackingMessageHandler.cs` |

**Order and timing:**

- The order is R0, R1, R2, then R3.
- Numbers are claimed at push. #193 shipped as 5.11.1, the search as 5.12.0 and its fix as 5.12.1, so R0, a minor,
  is 5.13.0 at the earliest, and other sessions have releases in flight.
- #192's `origin` needs R1's roles, and #191's card needs both. So the order across the three plans is #190 R0, #190 R1,
  #192, then #191.
- `VerbTable.cs` and `ReportIndex.cs` have three claimants, and rule 5 runs them one after another (section 9).

---

## 7. Tests (TDD: each written red against 5.11.0, then made green)

No Playwright: nothing here changes the HTML report's page or controls; R0 changes only the names it draws (if Q11 is
ever taken, its facts come with it).

### 7.1 R0

1. `Names_an_unmapped_remote_host_by_its_host_and_port`: `https://pricing.example.com/` gives
   `pricing.example.com:443`. Red on 5.11.0: `localhost:443`.
2. `Two_unmapped_hosts_on_one_port_are_two_parties`.
3. `A_loopback_address_keeps_the_localhost_name`: `127.0.0.1:54987` and `[::1]:5000` give `localhost:54987` and
   `localhost:5000`. Green on 5.11.0 too: it guards the rule, so a mutation that drops the loopback check turns it red.
3a. `Names_a_host_as_Uri_gives_it`: `API.Example.COM`, `[fe80::1%25eth0]:8080`, a Unicode host and a trailing dot.
3b. `InteractionShapeTests.cs:300` expects 7, with rule 6 kept; `Rule_6_reads_a_host_the_fallback_named_as_localhost_and_nothing_else`.
3c. `HostNamedParticipantTests` (IKVM): a participant named `xn--e1afmkfd.xn--p1ai:443`, and one whose alias starts with
    `_`, rendered through the engine: the first draws without a strike, the second draws at all; and every name of a
    list of marked names (two of each creole marker, quotes) draws as written in the sequence diagram, the component
    diagram (still bold) and the component diff.
3d. `HistoryBridgeTests`: the rename reads no change, no quiet run and no new dependency; without a bridge the move
    reads as before; a real change across it reads changed and names the call; a new dependency on another port is
    new; one on a renamed host's port is not, while its scenario reads changed; two hosts 6 called one compare as one;
    the alternating memory survives the move; interleaved runs under both rules are not an unstable shape; an
    untouched suite writes `{}`; the bridge holds only what the rule changes; it survives the line and the fragment;
    the ledger holds its shapes and `verify` finds it sound, and finds a short one; prune and compact keep it; a fold
    of shards and a fold of attempts carry it position by position; the in-process append keeps it only while the
    window needs it, and the fragment always; `ForWindow`. `HistoryCommandTests`: `record` keeps it only while the
    ledger's window holds rule 6.
4. The existing `localhost:7777` fact stays green, unchanged.
5. Kronikol4J: `ServiceNameResolverTest` mirrors 1 to 3a through each of the four callers' host forms.

### 7.2 R1

6. `PartyResolverTests` (pure):
   - each precedence step beats the next;
   - file and options merge field by field (a lane's `Kind` with the file's `Id`);
   - exact match, and the case near miss's diagnostic;
   - `ServiceId` attaches to a declared, observed or inferred subject;
   - each conflict diagnostic;
   - the in-process subject;
   - every case of `cases.py`, and a BreakfastProvider-shaped fixture from `results-parties.txt`.
7. `PartiesBlockTests`:
   - no declaration writes no key, in each of the four forms;
   - a declaration writes the block in each;
   - rows sorted, nulls left out;
   - callers have rows;
   - the schema validates, and its contract fixture declares a party and marks a call, so the closed schema is held
     to every new key (5.5).
8. The byte pins: `ReportJsonEncodingTests` and every golden that pins a data file pass unchanged (they declare nothing).
9. `TestClientObservationTests`: a real `WebApplicationFactory` test gives `Caller` `test` and the fixed name
   `underTest`, both `observed`, with the entry assembly as the note.
10. `MergePartiesTests`: agreeing shards; disagreeing shards (`unknown` plus the diagnostic); two suites (two subjects,
    no `serviceId`).
11. `PartiesQueryTests`:
    - text, `--json`, `--kind` and the footer;
    - a 5.11.0 report without the block gives the inferred table;
    - `services` is byte-identical without the block, and shows `id` and `kind` with it, before `statuses`;
    - `summary` has its service line only with the block.
11a. Registration: the existing guards of 5.7 (`DescribeTests`, `SkillDriftTests`, `QueryCultureTests`,
    `CountFlagTests`, `KrklQueryTests`) are red until `parties` is in each list, then green.
12. `HistoryRunLineTests` and `RunManifestTests`:
    - `serviceId` is present when set and absent otherwise.
    - Both readers read old and new lines.
    - Each of the five sites of 5.8 that must carry it does: a run's own line, a parsed and re-written line, both
      folds (shards that agree keep it; shards that disagree, or one without it, keep none), and `query history`'s
      line from a report. A CTRF or Allure import writes none.
    - A ledger mixing lines with and without `serviceId` loads with no line counted as damaged, and `history doctor`
      reports nothing about it.
    - The header's `historyFormatVersion` is unchanged.
13. `PartiesFileDiscoveryTests`:
    - found above the test output;
    - `KRONIKOL_PARTIES` wins;
    - a malformed file, an unknown kind, a duplicate name and a newer `partiesFormatVersion` each give a diagnostic and
      never fail the run;
    - kinds are read ignoring case.
13a. `TestPartyNameInsideHostTests`: a call attributed `RequestHeader` with the test party's caller name gives the
    diagnostic with the count. BreakfastProvider's shape gives none.
13b. `PartyDiagnosticsTests`:
    - a report that declares nothing has no party diagnostic and no labs page, and `parties` prints the checks as notes;
    - one that declares something has them in every output, from one resolution before the parallel writes;
    - none raises a `!` banner.
13c. `serviceId` in `Failures.jsonl`'s header and, if Q16 is taken, in CTRF's `environment.appName` from both
    producers, each only when declared.

### 7.3 R2

14. Ingest: `--service-id`, `--parties` and `--suite` each reach the report, and each refuses a missing value.
    `testDouble` is read, kept and written, and absent when not supplied. With `--suite`, the pointer ingest prints
    addresses the report's own ids.
14a. `testDouble`'s shape:
    - a hand-made report whose call holds an object under `testDouble` keeps the call's own `serviceName` in R2's
      scanner (red before the guard);
    - `RequestResponseLogRoundTripTests` lists it in `Carried`;
    - a merged wire and span pair keeps the wire's value, else the span's;
    - the OTLP export carries `kronikol.double`.
15. Resolver: all calls marked gives `double` `observed`; some leaves the kind, and `parties --json` counts
    `doubleCalls`; marked calls on a declared `real` party give the conflict.
16. The Jest recipe's fixture (MSW mocked and bypassed in one test) gives the counts.

### 7.4 Mutations, each turning a fact red

- Precedence reversed (inference over declaration).
- Name compared ignoring case.
- Block written unconditionally (fact 8 fails across the corpus).
- Rows unsorted.
- Callers dropped from the table.
- `Kind` defaulted to `Unknown` before the merge (the field-wise fact fails).
- `serviceId` kept from a disagreeing shard.
- The fallback's host taken from the port's map.
- The loopback check dropped (fact 3 fails).
- The shape version left at 6 (fact 3b fails).
- Party diagnostics written on a report that declares nothing (facts 8 and 13b fail).
- `testDouble` written as null on every call (fact 8 fails).
- The call guard removed from the scanner (fact 14a fails).
- The broker rule removed (the broker case fails).

### 7.5 Checks

The full suite per release. The Kronikol4J goldens for R0. BreakfastProvider's lanes re-run on the release candidate:
names unchanged, since it maps every party, and the block absent until it declares. `compat/run.sh` again on R1's and
R2's real output, with 5.4.1 and the release before.

### 7.6 The issue's acceptance, as met here

| The issue's line | How it is met | Facts |
|---|---|---|
| Golden reports stay byte-identical when neither option is set | as written. This also answers `FLEET_EVIDENCE_PLAN` §7.5 item 1 | 7, 8, the mutation "block written unconditionally" |
| With `ServiceId` set and one double declared, `services --json` and the data file show `id`, `kind` and `kindSource` for every party | reworded: `parties --json` and the data file show them for every party, callers included. `services --json` shows `id` and `kind` for every callee of a declared report | 6, 7, 11 |
| Undeclared parties show `unknown`, and the footer names them | on `parties` and on `services` | 11 |
| The same declarations work through `kronikol ingest` via the options file | through `--service-id` and `--parties` until F4's file exists, then through that file, in the same shape | 14 |

---

## 8. Documentation and records

**Wiki** (`origin/master` `2f165c5`), page by page:

- `Report-Configuration.md`: `ServiceId`, `Parties`, `PartiesFilePath`.
- `Querying-Reports.md`: `parties`, and the `services` columns.
- `Tracking-Dependencies.md` §"What About External/Fake Dependencies?" (`:71`): declare doubles per lane, with
  BreakfastProvider's shape as the example.
- R0's fallback name: the 15 lines on 13 pages listed in section 6, `HTTP-Tracking-Setup.md` among them.
- `Ingesting-External-Captures.md`: `testDouble`, `--service-id`, `--parties`, `--suite` (and that a suite re-keys
  the stable ids and starts a new history stream, 5.9).
- `Ingesting-From-Jest.md`: the recipe's mark.
- `Cross-Run-History.md`: `serviceId` on the run line, that an older tool's rewrite drops it, and R0's one quiet run.
- `AI-Integration-Prompt.md`: ask integrators to declare doubles (Q14).
- `Generated-Reports.md` §"CTRF report", if Q16 is taken (`Home.md:32` links it as `#ctrf-reportjson-optional`).

**Not edited:** the `CLAUDE.md` and `AGENTS.md` a run writes beside its report. They are static text
(`Reports/agent-instructions.md`, read by `AgentInstructionsGenerator.cs:35-50`), so a change there changes those
files for every run, and this repository's managed block with them (`SkillDriftTests.cs:448`). The skill documents the
verb instead.

**Other records:**

- README: one line under the agent section.
- `CHANGELOG.md`: per release, stating the part that moved.
- Kronikol4J ledger entries: R0 naming, R1 data file, R2 record field.
- `PLATFORM_FOUNDATIONS_PLAN.md` §4.7.
- `FLEET_EVIDENCE_PLAN.md` §4.1: a note that this plan supersedes S1's details.
- `ROADMAP.md` placement (Q1).
- The `PLANS_STATUS.md` row.

---

## 9. Coordination

Agreed by message on 2026-10-26:

- **kronikol-ed (#191):** cites this plan for the kinds, `kindSource`, the inference and the block. The card runs the
  resolver when the block is absent. Measured: the ledger's edge set changed in 0.4% of 1,005 same-commit run pairs
  (`SERVICE_CARD_PLAN.md` 2.4).
- **kronikol-bf (#192):** owns `origin`, which is the caller's role from this table, with `cause` separate. Its rule's
  input is 5.4.
- **The peer on #138** (`INTERNAL_FLOW_SOURCES_PLAN.md`, in the shared checkout): it changes the span listener and also
  touches `TestTrackingMessageHandler`. R0 edits that file's `GetPortTranslator`, `ResolveServiceName` and the call
  between them (`:67-114`, `:239`). Whichever ships second rebases.
- **#193:** released as 5.11.1 (`a0df84b9`). It rewrote the SQL trackers' address code, which this plan cites only in
  Appendix B. No file in common with R0 to R3.

---

## 10. Questions for the owner

**Answered by the owner on 2026-10-27**, asked before "implement the plan in full" was acted on, in two rounds:

- Q1: in full, now: R0, R1, R2 and R3.
- Q2: yes to the remote host's name and to loopback keeping `localhost:{port}`. History: asked again once kronikol-bf
  showed that a version move restarts every scenario's window, in every suite, the owner chose "R0 builds the bridge"
  over "version move now, bridge later" and "hold R0" (section 6).
- Q3, Q4, Q5, Q6, Q7, Q9, Q10, Q13, Q14, Q15, Q16: as recommended.
- Q8: yes, R3.
- Q11: **yes**, against the recommendation: a declared double carries a stereotype on its participant, in the sequence
  and component diagrams (5.13).
- Q12: mirror R0 in Kronikol4J; R1's block there only when asked.

1. **Placement (D23).** The issue was filed while designing the owner's own indexer.
   - Run R0 now, as a stage 1 patch (rule 1: shipped and wrong).
   - Run R1 and R2 before the launch if the indexer is wanted now; otherwise at 14.11, before 14.5.
   - Recommended: R0 now. R1 and R2 at the owner's word.
2. **R0's fallback fix as a patch,** with the changelog naming the change.
   - Every report with unmapped remote hosts renames them once, and history names each as a new dependency once.
   - Loopback keeps `localhost:{port}`. The alternative, naming `127.0.0.1` as written, gives one stub two names when
     a lane reaches it both ways, for no gain: it is the same machine.
   - `InteractionShape.Version` 6 to 7 costs every suite one run without behaviour verdicts. Left at 6, each scenario
     that calls an unmapped remote host reads as `BehaviourChanged` once, which the analyzer's own rule exists to
     prevent (`HistoryAnalyzer.cs:488-491`).
   - Recommended: yes to all three, the version as 4.20.0 did for MongoDB.
3. **A third `kindSource`, `observed`, with O1.** Recommended: yes.
4. **Discovering `.kronikol/parties.json` and `KRONIKOL_PARTIES`,** as history's ledger is discovered. Recommended: yes.
5. **`Kind` optional per declaration** (field by field), so a shared file holds ids and a lane its kinds.
   Recommended: yes.
6. **A `parties` verb,** with `services` columns only on declared reports, rather than widening `services`.
   Recommended: yes.
7. **The per-call `testDouble` field** (R2), beyond the issue, decided before 14.5. Recommended: yes.
8. **R3,** .NET's in-process mark. Recommended: yes, after R2.
9. **Ingest flags now,** rather than waiting for F4. Recommended: yes.
10. **Party ids and kinds on the history run line.** Recommended: `serviceId` only, as asked. Add ids when a fleet
    index reads the ledger.
11. **Drawing a declared double differently** in the HTML report (a stereotype). Recommended: not in this plan. It
    changes the page for human readers, and the wiki draws the production shape on purpose.
12. **Kronikol4J:** mirror R0 (recommended: yes, for parity) and R1's block (recommended: only when asked).
13. **Emulators are `double`, with a note.** Real means the party's own software. Recommended: yes.
14. **The AI integration prompt asks integrators to declare doubles** (documentation). Recommended: yes.
15. **Close #190 with R1 and R2,** and leave #191 and #192 to their plans. Recommended: yes.
16. **`serviceId` as CTRF's `environment.appName`.** CTRF's schema declares the key, and closes `environment`
    (`ctrf-io/ctrf`, `schema/ctrf.schema.json` on `main`, read 2026-10-27).
    - Both of Kronikol's producers write `environment` only on CI (`CtrfReportGenerator.cs:89-91`,
      `CtrfCommand.cs:168-169`), so a declared id off CI needs both gates opened for it, and the two must keep giving
      the same document (`CtrfCommandTests`).
    - `kronikol ctrf` takes it from the report's block.
    - Recommended: yes, in R1, only when declared. A dashboard that reads CTRF then names the service without reading
      Kronikol's own file.

---

## 11. Assumption ledger

| # | Assumption | Basis | If wrong |
|---|---|---|---|
| A1 | The block's absence leaves every report byte-identical, and its presence breaks no reader | READ for the first (the writers add keys only when set; the compressed writer drops empty ones). RUN for the second: six readers of 5.11.0 and 5.4.1 (`compat/results.txt`) | fact 8 catches the first. A reader outside the tool (a consumer's own parser or schema check) is the remaining risk; see the schema item in 5.5 |
| A2 | Display names are stable across a suite's lanes | RUN on one suite (four lanes); PEER (edges changed in 0.4% of 1,005 pairs) | a lane-specific name needs its own row; the near-miss diagnostic shows case drift |
| A3 | BreakfastProvider's in-memory `CosmosDB` and `BigQuery` are fakes | RUN (endpoints, a production host answered on CI with no credentials), READ (its settings name the Docker emulators) | the per-lane argument still holds through the SQL and port differences |
| A4 | The issue's three reports name their test party `Caller` or `Test` | ISSUE | the inference's test rule does not depend on the name |
| A5 | A `WebApplicationFactory` client's base address is `http://localhost` | READ: Mvc.Testing 10.0.7's own documentation (`CreateDefaultClient` sets it; `ClientOptions.BaseAddress` defaults to it). .NET 10's `UseKestrel()` mode is not checked | O1 does not rely on it. R0 does, and its loopback rule keeps `localhost:{port}` for any loopback address, so only a client pointed at a remote address would be renamed |
| A6 | MSW's `response:mocked` fires only for a handler's answer, and `response:bypass` for everything that reached the network | SOURCE: msw 2.11.0, the version the recipe's harness pins (`INGEST_FIDELITY_PLAN.harness/otel-jest/package.json:25`), `src/node/SetupServerCommonApi.ts:109-119` emits `isMockedResponse ? 'response:mocked' : 'response:bypass'` with `requestId`. MSW's own tests (`test/msw-api/setup-server/life-cycle-events/on.node.test.ts`, main) send an unhandled request and a handler that returns nothing to `response:bypass`. The docs (mswjs.io/docs/api/life-cycle-events) say "whenever a mocked response is sent" | the Jest fixture (fact 16) checks it. MSW 3 (v3.0.3, the latest release) keeps both events (`interceptor-source.ts:102` on main) |

---

## 12. Log

- 2026-10-26, kronikol-67:
  - Read #190, #191 and #192, `FLEET_EVIDENCE_PLAN.md`, `ROADMAP.md` (rules, 14.5, 14.11, D23) and
    `PLATFORM_FOUNDATIONS_PLAN.md` (F3, F4, §4.7).
  - Agreed the split with kronikol-ed and kronikol-bf (section 9).
  - Probed the handler's naming (`probe-names/`).
  - Downloaded four BreakfastProvider lanes, decoded them with the tool built from `44bcc62a`, and measured them with
    `bp-reports/`.
  - Two read-only sweeps: every tracker's naming (`sweeps/trackers.md`), and ingest's fields and options.
  - Corrected two of my own early claims after re-measuring or a peer's trace: the host count (7 of 16, not 9), and
    the Setup-phase calls (the service's background work, not the test seeding data).
  - kronikol-bf's measurement agent found that `parties.py` read the background list as `httpInteractions`, a
    scenario's name for it; the data file calls it `interactions` (the schema's `background.required`). All three
    scripts now read it under that name. The four lanes have no background calls (`calls` 0 on each), so every result
    is unchanged, re-run and diffed.
- 2026-10-27, kronikol-67, at the owner's request to make sure nothing is missing, that the plan is accurate, and to
  dive into whatever might change it:
  - Re-checked every citation against `44bcc62a` and against `1ee6373a` (5.12.0), with a read-only sweep and by hand.
    Corrected 19 that had drifted or were wrong, among them: the run line is built in seven places, not five; `history
    doctor` reports a damaged line, and "unreadable" is `history verify`'s word; merge keeps `suite` and `environment`
    only when every shard has the same one, null included, and `serviceId` now follows that rule; the mergeable file
    writes `environment` last, so the block's place there is after it; "Caller" is the in-process trackers' default
    only; no Kronikol capturer writes `capturedBy: wire`; #193 shipped as 5.11.1 and 5.12.0 followed.
  - Proved the compatibility claim (A1) by running it: a real report given the block and `testDouble` reads the same
    through six readers of the 5.4.1 and 5.11.0 tools (`compat/`).
  - Checked MSW's events against its source (A6), and refined the inference rule: several test parties, stores taken
    from Kronikol's own palette, a scheduler and a second test client (`cases.py`, 9 of 10; the tenth, the in-process
    subject, is the blind spot only a declaration settles).
  - A sweep of R0's reach changed R0: a loopback address keeps `localhost:{port}`; the history shape version moves to 7
    so the rename gives one quiet run rather than a false `BehaviourChanged` per scenario (Q2); 15 wiki lines on 13
    pages, not one; Kronikol4J's four call sites and its host forms; and two drawing checks for the new names. No
    existing test or golden changes.
  - A sweep of what the first draft did not cover added: the schema is closed, so every key is declared and an old
    copy rejects a declared report; `testDouble` must stay a string, since released scanners read a call's scalars
    by name at any depth; the block holds no arrays; party diagnostics only on a declaring run, never a banner; an older
    tool's ledger rewrite drops `serviceId`; `--suite` re-keys an ingest's stable ids; the verb's registration lists;
    `services`' column order and `summary`'s line; overloads, not optional parameters; one resolution before the
    parallel writes; `Failures.jsonl`'s header; and CTRF's `appName` (Q16).
  - Fixed one inconsistency of my own: 5.10 gave a row a `doubleCalls` member while 5.5 fixes rows at five; the count is
    the `parties` verb's.
- 2026-10-27, kronikol-67, executing R0 (the owner's answers are in section 10):
  - The handler's fallback, its mark and the docs, TDD: six facts red on `localhost:...` first
    (`TestTrackingMessageHandlerTests`), then green; a seventh pins the mark.
  - The creole fact (3c) was red, and found worse than the plan feared: a name holding a `"` turned the whole sequence
    diagram into the Syntax Error picture. Names are escaped line by line in all three diagrams that draw them.
  - The bridge, built to kronikol-bf's design (`CALL_OPERATION_AND_ORIGIN_PLAN.md` §4.8 part 1). One correction to my
    own first draft of it: a run rebuilt from a report cannot tell what rule 6 named, so only a run made in process
    writes a bridge (`HistoryBuildOptions.Bridges`); a guess would have read a real change as none.
  - One fact I wrote expected a new host on a renamed host's port to be a new dependency; under rule 6 it is not, and
    the scenario reads changed instead. The fact now pins that limit, and the changelog and the wiki say it.
  - Kronikol4J's mirror (Q12) in its own worktree: the resolver takes the host, red then green in each adapter.
  - Core suite with the bridge: 8986 passed, 2 skipped, none failed.
  - 25 mutations of the handler, the bridge and the escape, in a worktree of their own, all caught in the end. Two
    survived the first pass, because their facts were too loose: the calls a verdict names were only checked when the
    current run bridges the one before it, and the prune fact's earlier run held the bridge's calls itself, so a lost
    shapes line went unseen. Checking the other direction, a run an older tool appends after a bridged run, found that
    `NewDependencies` read only the current run's bridge. It now reads the bridge either run wrote, as the design says,
    with a fact that was red first.
- 2026-10-27, kronikol-67, executing R1:
  - The facts of 7.2, each beside the code it holds: the resolver's precedence and checks, the parties file's
    discovery, the block in the four forms and the mergeable file, the schema and the XSD (the contract fixture
    declares a party), the test client observation through a real `WebApplicationFactory`, merge, the verb and its
    registration in every guard list, the run line at each of its five sites (a ledger mixing lines loads whole at
    version 1, and `history doctor` says "none damaged"), `Run.json`, the digest's header, CTRF from both producers,
    the diagnostics on a declaring run only, none a banner.
  - 29 mutations of the resolver, the file, the block, merge, history, the outputs, the verb and the drawing, in a
    worktree of their own, all caught in the end. Two survived the first pass because their facts were too loose: the
    near miss was tried only with spaces around the name, so a match that ignored case went unseen; and the
    field-by-field fact had only the options give the kind, so an options row read as declaring `unknown` went unseen.
    Both facts were widened, and the mutants then failed them.
  - Q11's stereotype, as the owner chose: an ambient scope (`DiagramParties`) holds the declared doubles while the
    run's diagrams are drawn, so no public drawing method gains a parameter. The sequence diagram writes
    `<<test double>>` after the alias and before the colour, and the page's own parse of a declaration
    (`DECLARED`) now allows it, so the participant keeps its dependency colour; the component diagram's rectangles keep
    their styling stereotypes and carry «test double» as a line of their label.
  - One fault of my own, found by the whole-pipeline diagnostics fact: the table was resolved over every call the
    process logged, including calls whose test id names no scenario, which the report leaves out. In a real suite an
    untracked test's calls would have named parties the report never shows. It is now resolved over the calls the
    report carries, a scenario's or the background's, with a fact that was red first.
  - Two decisions within the plan's lines: an import of a CTRF document reads no `appName` into `serviceId` (5.8 names
    the imports as carrying none); and the `parties` verb counts a scenario's calls only, as `services` does, so a
    background call is not counted on a report that declares nothing.
  - The verb's text output was checked on a real report written by the release build (`impl/parties-demo/`), which is
    also where the wiki's examples come from; a row with no id and no note ended in two spaces, now trimmed.
- 2026-10-27, kronikol-67, executing R2:
  - The facts of 7.3, each beside the code it holds: the mark in the JSON, XML and YAML forms (the XML validated
    against the XSD), absent when unset; an object under the key leaves the call's own `serviceName` standing in the
    scanner (red before the guard); the round trip from a log to a record and back; a mark on one half of an ingested
    call reaching both; a blank mark marking nothing; the merged wire and span pair; the mergeable file read back; the
    OTLP attribute; the resolver's three rules; `doubleCalls`; ingest's three options, each refusing a missing value,
    the pointer addressing the report's own ids under `--suite`, discovery from where the ingest runs, and a parties
    file among the inputs read as the parties file rather than as a capture of malformed lines.
  - The Jest recipe's fixture is a capture shaped as the recipe writes it, not a run of Jest, which needs packages from
    the network: one host MSW mocked once and let through once (`unknown`, two calls received, one a double's) and one
    it mocked every time (`double`, `observed`, on a report that declares nothing).
  - 31 of 31 mutations caught.
  - One fault in R1 found on the way, fixed with a fact red first: the subject's inference took the party the test called
    most and, when that party's kind was known already, kept it and inferred nothing, so a test that called a stub more
    than its service left the service `unknown`. The inference now chooses among the parties of unknown kind.
  - `PLATFORM_FOUNDATIONS_PLAN.md` 4.7 is amended as 5.10 says, and `FLEET_EVIDENCE_PLAN.md` 4.1's note names the mark.
  - Six of the mutants had no fact to fail until one was written for each, each passing on the code and failing on its
    mutant: a blank mark, a parties file among the inputs, the mergeable reader, the `Pair` overload, the XML
    validated against the XSD, and a host MSW answered every time (the verb's inferred path).

---

## Appendix A. BreakfastProvider's parties, by lane (RUN)

From `bp-reports/results-parties.txt`, calls by caller and callee, xUnit lanes:

| Caller > callee | In memory | In Docker |
|---|---|---|
| Caller > Breakfast Provider | 451 | 450 |
| Breakfast Provider > CosmosDB | 280 | 353 |
| Breakfast Provider > Reporting Database (SQL Server) | 189 | 75 |
| Breakfast Provider > Google Cloud Pub/Sub | 68 | 68 |
| Breakfast Provider > Cow Service | 49 | 49 |
| Breakfast Provider > Notification Service (gRPC) | 41 | 41 |
| Breakfast Provider > Kafka Broker | 40 | 40 |
| Breakfast Provider > Kitchen Service | 40 | 40 |
| Breakfast Provider > Azure Event Hub | none | 39 |
| Breakfast Provider > Event Grid | 33 | 33 |
| Breakfast Provider > Breakfast Database (SQL Server) | 31 | 31 |
| Breakfast Provider > MongoDB, ClickHouse, Spanner, BigQuery | 16, 12, 12, 11 | 16, 17, 12, 11 |
| Breakfast Provider > Supplier Service, Goat Service | 10, 9 | 10, 9 |
| Kafka Broker > Breakfast Provider | 42 | 2 |
| Azure Event Hub > Breakfast Provider | 39 | none |
| Google Cloud Pub/Sub > Breakfast Provider | 36 | 1 |

## Appendix B. How each tracker names its parties

`PARTY_IDENTITY_PLAN.harness/sweeps/trackers.md`, at `44bcc62a`.
