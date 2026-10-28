# Query for programs: a run read by the keys its producer holds (#184)

**Status:** green-lit. Written 2026-10-28 against 5.19.0 and audited the same day against 5.20.0 (`69ed51a7`);
section 10 records what the audit changed. The same day the owner asked for the plan to be implemented in full and
took every recommendation in section 8, so R4 is in (Q1) and the scenario row is made whole (Q2). Every line reference
is to `69ed51a7` unless it names another revision. The wiki references are to `Kronikol.wiki` `28724ec`. The plan
answers #184, its siblings #182 and #183, found in the same move, and #76's remaining half (roadmap 7.1).

Evidence legend:
- **READ**: read in the code or docs at the revisions above.
- **RUN**: measured by this plan's harness (`QUERY_FOR_PROGRAMS_PLAN.harness/`) against the published 5.19.0
  package, whose code is unchanged in 5.20.0 for everything the probes exercise (5.20.0 changed
  `QueryCommand.Overview.cs` only inside `Parties`, from line 492).
- **INFERRED**: reasoned, and to be checked by the release that relies on it.

---

## 1. Summary

#184 was filed on 2026-10-20 at 5.0.0. It asks for three things so that a host can check its own capture through
`kronikol query` rather than the data file:

- the scenario's `id` and its diagram count on each `scenarios --json` row, perhaps with an `id:` address;
- `diagram sN --out DIR` writing every diagram of a scenario in one call;
- `caller` on each `interactions --json` row.

At 5.20.0 one of the three is done. `caller` shipped in 5.19.0 (#192 R2), and its comment cites #184. The other two are
open, and each is a symptom of a wider problem the issue does not name.

**The underlying problem.**
- **Who reads the engine now.** On 2026-10-18 the owner made the data file Kronikol's own, and 5.0.0 compressed it.
  `kronikol query` therefore became the way a program reads a run, and `kronikol decode`'s own help sends a question
  to query, "which never needs" the JSON.
- **Who it was built for.** The engine was built for a different reader: an agent exploring a run it has never seen,
  under a byte budget. Its machine surface (the `--json` items, the address grammar and the refusals) mirrors those
  text answers.
- **What a program that already knows the run lacks.** Four things:
  1. **Identity.** A program cannot name a scenario by the id its producer gave it. The engine reads that id, the
     schema requires it and calls it unique within the run, and the report's own diagnostics refer to scenarios by
     it, yet no verb prints it and no address takes it. The keys on offer fail such a program: `sN` is an ordinal it
     cannot know in advance, and `sid:` is a hash of the name that two tests can share.
  2. **Enumeration.** It cannot find out how many diagrams a scenario holds except by probing until a refusal.
  3. **Refusals it can branch on.** "That is not in the report", "there is no report" and "you used me wrong" all
     exit 2, and the `--json` error carries only prose.
  4. **Completeness.** Where a text answer summarises or leaves a fact out, `--json` does the same, and nothing in it
     says what was left out.
- **Not one issue.** Eleven open issues break one or more of the four (section 4.1), and four verbs read scenario
  addresses wrongly (F18, F19).

**The proposal.** Rules for the machine surface, applied where they are broken and kept by tests:

- **R1, identity and the scenario row (minor).**
  - `id` on every item that names a scenario of one run, `Failures.jsonl` included.
  - An `id:<test id>` address wherever a scenario is taken.
  - The scenario row made whole: `calls`, `diagrams`, `sourceFile` and `sourceLine` (roadmap 7.1), `rule`,
    `isHappyPath` and `featureSourceFile`.
  - A test that fails when a fact the engine holds about a scenario neither reaches the row nor is excluded for a
    stated reason.
  - Two fixes: `services`, `parties` and `values` refuse `sid:`; `compare` says a `sid:` scenario is not in the report
    and reads `s3/i2` as `s3`.
- **R2, the diagram verb (minor).**
  - `diagram <scenario>` lists the scenario's diagrams.
  - `diagram <scenario> --out DIR` writes all of them, and `diagram --out DIR` writes every diagram in the run.
  - `diagram` answers `--json`.
- **R3, refusal kinds (minor).** `error.kind` in the `--json` envelope, with seven kinds (`usage`, `notFound`,
  `ambiguous`, `noReport`, `unsupported`, `unreadable` and `writeFailed`), each refusal declaring its own. The exit
  codes do not change.
- **R4, optional (Q1).** #183's every diagnostic in `summary --json`, and #182's line naming what `grep --count` did
  not search.

## 2. What the issue says, checked at 5.20.0

| # | Claim in #184 | At 5.20.0 | Evidence |
|---|---|---|---|
| C1 | A `scenarios --json` item has no `id` | **Still true.** The item is `address, stableId, feature, scenario, result, failed, durationSeconds, interactions, errorMessage, attempt, stableIdShared, tags, exampleValues`, plus `warmUpSeconds` and `notATest` when they apply. It also has no `sourceFile` or `sourceLine` (#76) | READ `QueryCommand.Overview.cs:291-308`; RUN `results/gaps-on-5.19.0.txt` |
| C2 | The name returned is not the one written (`lists orders` reads `Lists orders`) | **True, and documented.** `CapitaliseTitles` (default `true`) re-cases every title, and `kronikol ingest --no-capitalise` turns it off. With `id`, a name no longer has to be a key, so this plan leaves it alone | READ `ReportConfigurationOptions.cs:705-711`, `ReportGenerator.cs:256-257`; RUN |
| C3 | Nothing says how many diagrams a scenario has | **Still true.** No item carries a count, and no verb lists a scenario's diagrams | READ `Overview.cs:291-308`, `QueryCommand.Payloads.cs:647-675` |
| C4 | `diagram` takes one `sN/dK` and writes only to `--out` | **Still true.** `diagram s0 --out DIR` is refused with "Which diagram? Pass s3/d0.", and `diagram` is one of the verbs that refuse `--json` | READ `Payloads.cs:649-653`, `VerbTable.cs:241-245`; RUN |
| C5 | Probing past the last diagram exits 2, as a usage error does | **Still true** (`No diagram d1 in s0 ... it has 1.`, exit 2) | READ `Payloads.cs:689-692`; RUN |
| C6 | An `interactions --json` item has no caller | **Fixed in 5.19.0** (#192 R2) | READ `Payloads.cs:136-137`; RUN |

## 3. Findings

### 3.1 Identity

| # | Finding | Evidence |
|---|---|---|
| F1 | The engine reads every scenario's `id` (`ScenarioEntry.Id`), and no verb prints it or accepts it. | READ `ReportScanner.cs:1015`, `ReportIndex.cs:187`; a grep of `src/Kronikol/Query` |
| F2 | The schema requires `id`: "The runtime id the test framework gave this scenario (a test case id, a pickle id). Unique within the run but not stable across runs; use stableId for that." | READ `ReportGenerator.cs:7789-7792` |
| F3 | `stableId` cannot stand in for `id`. Two ingested tests with one name and feature share a `stableId` (`stableIdShared: 2`), though their `testId`s, callers and results differ. Each verb that takes `sid:` then refuses it and asks for the one meant, which leaves only the ordinal. | RUN `results/gaps-on-5.19.0.txt`, `results/sid-on-5.19.0.txt`; READ `QueryCommand.Narrative.cs:857-882` |
| F4 | The report's own cross-references use `id`. A `RenderFailure` diagnostic names its scenario by the test id (`scenarioId`), and the data file looks diagrams up by it. The engine reads a diagnostic's `scenarioId` and prints it nowhere. | READ `DefaultDiagramsFetcher.cs:108`, `:174`; `DiagnosticEntry.cs:181`; `ReportGenerator.cs:5670`; `ReportScanner.cs:752`; `ReportIndex.cs:477-482` |
| F5 | What an id is, per producer. Within one run every producer keeps it unique, because each collects its scenarios keyed by it. The shapes vary: short counters, GUIDs, hashes, `Class.Method(row)`, and JUnit's `[engine:junit-jupiter]/[class:...]/[method:...]` with `/`, `:`, brackets and parentheses. BreakfastProvider's NUnit lane has 212 distinct ids for 212 scenarios. The full list is in the table after this one. | READ as cited there; RUN `results/breakfastprovider-nunit-ids.txt` |
| F6 | `kronikol merge` changes ids. Shards from different processes can mint the same id for different tests (NUnit's counter), and the merge renames each later collision to `<id>#N`. One scenario that ran in two shards keeps its id twice, both runs kept. The merge's comment says "nothing user-facing keys on it", which `id:` changes. | READ `Reports/Merge/MergeableReportMerger.cs:20-21`, `:283-294`, `:405-440` |
| F7 | #77 (roadmap 7.3) is a different identity: the runner's own name for a test (`TestCaseId`), stable across runs, which a re-run filter needs. `id` is the runtime id and serves #184 and not #77. The address grammar is prefix-based, so #77 can add a form of its own later. | READ `ROADMAP.md:451`; #77 |
| F8 | The name `id` is already taken in two other places. `services` and `parties` rows carry `id` as the party's declared id (5.14.0). Each verb's items have their own shape, so a scenario row's `id` does not clash in the JSON, but the docs must say which `id` each row means (Q3). | READ `Overview.cs:435`; wiki `Querying-Reports.md:398-399` |

| Producer | Where `id` comes from | Shape |
|---|---|---|
| Ingest (NDJSON tests file) | `testId`, one scenario per id (`Ingestion/FeatureSynthesizer.cs:180-195`) | any string the host writes |
| Cucumber messages | resolved against the ids already used (`Ingestion/Cucumber/CucumberFeatureSynthesizer.cs:304`) | pickle-derived |
| NUnit | `Test.ID` (`Kronikol.NUnit4/TestContextEnumerableExtensions.cs:61`) | `0-1287`: a per-process counter |
| xUnit v3 | `Test.UniqueID` (`Kronikol.xUnit3/TestContextEnumerableExtensions.cs:59`) | the framework's hash |
| BDDfy on xUnit v3 | the xUnit v3 `UniqueID` (`Kronikol.BDDfy.xUnit3/DiagramCapturingProcessor.cs:48`) | the framework's hash |
| xUnit v2 | the tracking attribute's id, or a fresh GUID for a test it never saw (`Kronikol.xUnit2/TestTrackingAttribute.cs:74-76`, `KronikolResultsSink.cs:155`) | GUID |
| MSTest | `Class.Method`, or `Class.<display name>` for a data row (`Kronikol.MSTest/DiagrammedComponentTest.cs:104-113`) | stable, name-shaped |
| TUnit | its test context's id (`Kronikol.TUnit/TestContextEnumerableExtensions.cs:57`) | the framework's (INFERRED) |
| LightBDD | `RuntimeId` (`Kronikol.LightBDD.Core/FeatureResultExtensions.cs:88`) | GUID |
| ReqNRoll | a GUID per scenario run (`Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs:51`) | GUID |
| Kronikol4J (JUnit 5) | `context.getUniqueId()` as `testId` (`kronikol4j-junit5/.../KronikolExtension.java:70`, `Scenario.java:30`) | `[engine:junit-jupiter]/[class:...]/[method:...]` |

### 3.2 Enumeration

| # | Finding | Evidence |
|---|---|---|
| F9 | `note sN/dK` lists a diagram's notes, but nothing lists a scenario's diagrams, so the only way to count them is to probe until a refusal. | READ `Payloads.cs:612-618` against `:647-675` |
| F10 | `diagram` and `note` are declared without a JSON form (`Json: false`), so neither answers `--json` today. | READ `VerbTable.cs:235-245`, `QueryCommand.cs:246` |
| F11 | How many diagrams a scenario holds depends on the rendering. At the default, `BrowserJs`, the browser splits diagrams, so the data file holds one diagram for a scenario that made calls and none for one that made none (212 of 212 on BreakfastProvider's NUnit lane). Every other rendering splits server-side, at 8,000 encoded characters (`NodeJs`, `Local`) or 2,000 (a server), so a long scenario holds several. | READ `DefaultDiagramsFetcher.cs:251-253`, `ReportConfigurationOptions.cs:167`, `PlantUml/PlantUmlCreator.cs:114` (diagrams are built per test that made calls); RUN |

### 3.3 Refusals

| # | Finding | Evidence |
|---|---|---|
| F12 | Bad usage and a well-formed address the report lacks share exit code 2: the table's own entry for 2 lists "a malformed or out-of-range address" with usage errors. A report path that holds no report also exits 2, through `ResolveReport`. The `--json` error is `{ exitCode, message, hint }`: the first line of the prose and the rest, with no kind. | READ `VerbTable.cs:331-337`, `QueryWriter.cs:27-62`, `QueryCommand.cs:163-165`, `:443-478`; wiki `Querying-Reports.md:1751-1762`; RUN |
| F13 | The engine writes 190 messages to its error writer, across 17 files. The first draft counted only the 33 that begin `No ...`. They fall into seven kinds a program would act on differently (5.3). | RUN `results/refusals-5.20.0.tsv` |
| F14 | The envelope is assembled once, in `Run`. The error writer is teed under `--json`, so no refusal site had to change. Two per-call `[ThreadStatic]` fields already carry the report and version to the envelope, and a kind fits beside them. | READ `QueryCommand.cs:76-92`, `:96-98`, `:108` |

### 3.4 Completeness and correctness

| # | Finding | Evidence |
|---|---|---|
| F15 | The scenario row leaves out seven facts the engine holds: `id`, `sourceFile`, `sourceLine`, `featureSourceFile`, `rule`, `isHappyPath` and `errorStackTrace`. Roadmap 7.1 already schedules `sourceFile` and `sourceLine` as "shippable now, alone", "the first thing any join key will read" (#76's remaining half). | READ `ReportIndex.cs:173-291` against `Overview.cs:291-308`; `ROADMAP.md:449` |
| F16 | A row's `interactions` counts records, two for an answered call, while its text row prints calls. `summary`'s `interactions` counts records too. The issue's own example reads `"interactions":2` for one call. | READ `Overview.cs:35`, `:95`, `:282-288`, `:300`; RUN (4 for 2 calls) |
| F17 | `failures --json` items deliberately use `Failures.jsonl`'s field names ("One concept, one shape"), and a `Failures.jsonl` failure line has no `id`. Adding `id` to one means adding it to the other. | READ `tests/Kronikol.Tests/Tool/QueryJsonTests.cs:384-397`, `Reports/FailuresDigestGenerator.cs:962-966`; wiki `Generated-Reports.md:614` |
| F18 | **Bug:** `services`, `parties` and `values` refuse `sid:` with "Not a scenario address", although `sid:` has been documented as a scenario address since 3.5.0. Each parses its address itself and accepts only an ordinal. | RUN `results/scenario-forms-on-5.19.0.txt`; READ `Overview.cs:351`, `:471`, `QueryCommand.Values.cs:33`; wiki `Querying-Reports.md:172` |
| F19 | **Bug:** `compare` reads both addresses as ordinals. With `compare sid:<s0> s1`, a scenario in the report is refused as not being in the report. `compare s0/i0 s1` compares `s0` with `s1` without saying so. `compare b:<hash> s1` is refused as a missing scenario. | RUN `results/scenario-forms-on-5.19.0.txt`; READ `QueryCommand.Search.cs:227-240` |
| F20 | `grep` with no hits reads its needle as an address and says so (`! … is an address, not text`). Once `id:` exists, a search for the text `id:42` gets that note, and the note needs an `id:` case. | READ `Search.cs:165-178` |
| F21 | #182 and #183 are open at 5.20.0. `grep`'s default leaves out headers and notes (`Search.cs:27`), and `--count` returns before saying so (`:159-163`). `summary` and the `!` notes keep only the first message of each kind (`Overview.cs:201-214`, `QueryCommand.cs:418-429`). | READ; RUN `results/gaps-on-5.19.0.txt` |
| F22 | `kronikol decode` (4.30.0, a day before #184) writes the whole JSON, but only in the tool. Its decoder in core is internal, so a .NET host with only the `Kronikol` package reads a `.krkl` in-process through the public `QueryCommand.Run` alone, which the `query.cs` beside every report also binds. Decode's help sends questions to query. | READ `src/Kronikol.Tool/DecodeCommand.cs:309-327`, `Compression/ReportFileFormat.cs:57`, `QueryCommand.cs:23-29` |
| F23 | The envelope's `formatVersion` is "bumped when a consumer would have to change". Added members do not change it, which is how `caller` (5.19.0) and `origin` and `cause` (5.20.0) arrived. | READ `QueryWriter.cs:81-85` |
| F24 | Kronikol4J has no query engine, so nothing here is ported. Its reports are read by this engine, and their ids are F5's JUnit unique ids. | READ (no `QueryCommand` in `C:/Code/Kronikol4J`) |

## 4. The underlying problem

The issue reads as three missing fields. The cause is one gap: **the engine's machine surface was designed as the text
answers in JSON, for a reader who explores, and it has become the read API for a reader who verifies.**

- **The explorer** starts from `summary` and follows the addresses each answer prints. It needs ordinals, budgets,
  summaries and prose.
- **The verifier** already knows the run: a host's own tests, a CI gate, or a script built around one run. It wrote
  the tests and their ids, and it knows which services it called. It asks a narrow question about a known thing and
  branches on the answer.

The verifier needs four properties the explorer never missed:

| Property | What the verifier needs | What the exploring design gives |
|---|---|---|
| Identity | to name a scenario by the key it holds | `sN`, known only after a listing, and `sid:`, a hash of the name that two tests can share (F3) |
| Enumeration | to know what a scenario holds and fetch it without probing | listings for steps, calls, notes and attachments; for diagrams, only probing (F9) |
| Branchable outcomes | to tell "not there", "no report" and "misused" apart without reading prose | one exit code and prose (F12) |
| Completeness | every fact in `--json`, or a member saying what was left out | the text answer's selection (F15, F16, F21) |

### 4.1 One problem, eleven issues

The same four properties account for every open issue in which a program cannot get from `kronikol query` what the
report holds (`results/related-issues.md`):

| Property | Issues |
|---|---|
| Identity | **#184** (`id`); #77 (`TestCaseId`, roadmap 7.3, a different key) |
| Enumeration | **#184** (diagrams); #201 (background calls); #206 (activity diagram spans) |
| Branchable outcomes | **#184** (diagram past the end); #151 (`<verb> --help` at exit 2) |
| Completeness | **#183**; **#182**; **#76** (roadmap 7.1); #118 (`grep --json`); #146 (call and step times) |

The eleventh, #114, breaks the channel itself: a NuGet warning lands in a first `query.cs` run's stdout, so `--json`
does not parse.

This plan takes the issues in bold, all of which concern a row or a refusal a host's check reads. The others need
designs of their own: a new verb, a new field from every adapter, or a timing model. The rules give each of them its
acceptance test. R1's guard (5.1) makes the next omission from the scenario row fail a test instead of waiting for an
issue.

### 4.2 Alternatives considered

- **A. The issue's three asks only.** That fixes C1, C3 and C4, and leaves F3, F12, F15, F18, F19 and #182/#183 for
  the next host to find. Rejected as the whole answer; kept as the core of R1 and R2.
- **B. Point hosts at `kronikol decode` or `CompressTestRunReport = false`.**
  - They work today, and the docs will say so.
  - They move the host back onto the data file the owner made Kronikol's own.
  - They do not help a .NET host in-process without the tool (F22), and decode's help says the opposite.
  - Rejected as the answer; documented as the way out.
- **C. A public in-process read API, a typed model or a public decoder.** This would freeze the data file's shape as
  public surface. Not recommended (Q9).
- **D. The four properties as rules for the machine surface, with tests that keep them.** Chosen.
  - Every change is additive: new members, a new address form and a new envelope member.
  - The bug fixes give four verbs the addresses the docs promise, and make `compare` refuse what it misread.

## 5. Design

### 5.1 R1: identity and the scenario row

**`id` on every item that names a scenario of one run.**
- **Where:**
  - `scenarios` (`Overview.cs:291-308`);
  - `summary`'s `failed` (`:141-148`);
  - `failures` (`Narrative.cs:212`);
  - `repro` (`:355`);
  - `Failures.jsonl`'s failure lines (`FailuresDigestGenerator.cs:962-966`), which `failures --json` must match (F17).
- **Placement and empty ids:** it goes beside `stableId`. It is `null` only when the report holds an empty id.
- **Not on `diff`'s rows** (`Search.cs:881-891`). They compare two runs and are keyed by the cross-run `stableId`, and
  an `id` belongs to one run.

**`id:<test id>`, an address for one scenario.**
- **Grammar.** The prefix is mandatory, for the reason `sid:` gives (`Address.cs:17-22`): `diff` tells an address
  from a report path by this grammar. Everything after `id:` is the id, taken whole and matched exactly (ordinal,
  case-sensitive, Q4). The form has no child addresses, because an id may hold `/` (F5): `id:a/i3` is the scenario
  whose id is `a/i3`. A call or diagram is reached through the `sN` the answer prints, or by passing `id:` to a verb
  that lists children (`interactions id:X`, `diagram id:X` after R2). `diff --body` keeps taking `sN/iK`.
- **Resolution, in one place.** A `TryTestId` beside `TryStableId` (`Narrative.cs:857-882`), reached from
  `TryScenario`:
  - No match: refused with `No scenario with id X in <file>`, and the hint that `scenarios --json` lists every id.
    When ids of the form `X#N` exist, the hint names them, since a merge renamed them (F6).
  - One match: that scenario.
  - Several (a merged report that kept two runs of one scenario, F6): refused with every match listed, as `sid:` is.
- **`--describe`, `--help` and the address table** gain the form `id:<test id>` (example `id:0-1287`), described as
  "A scenario by the id its test framework or host gave it: unique within one run, not across runs (`sid:` is the
  cross-run one)." `DescribeTests` already fails if the kind has no example (`DescribeTests.cs:134-145`).

**One scenario resolver for every verb that takes a scenario.**
- `services`, `parties` and `values` (F18) and `compare` (F19) resolve through a scenario-only variant of
  `TryScenario`, so all of them take `sN`, `sid:` and `id:`.
- `compare` refuses an interaction, step, diagram or body address, naming the verb that reads it, where it widened
  the address or misreported it.
- `services`, `parties` and `values` keep refusing a step path.
- `grep`'s zero-hit note gets an `id:` case (F20).

**The scenario row made whole (Q2).**
- **New members:**
  - `calls`: requests, as the text row counts them (F16). `summary`'s `run` gains it too.
  - `diagrams`: the count, read without opening a payload.
  - `sourceFile` and `sourceLine`: roadmap 7.1.
  - `rule`, `isHappyPath` and `featureSourceFile`.
- **Kept:** `interactions` stays as written. Redefining it would break existing readers.
- **The projection guard,** a new test. It walks `ScenarioEntry`'s public properties (`ReportIndex.cs:173-291`) and
  fails unless each one reaches the row or is on an exclusion list that names why. The proposed exclusions:

| Property | Why it is excluded |
|---|---|
| `Steps`, `BackgroundSteps` | the step tree, which `steps sN` lists |
| `Interactions`, `Annotations`, `Attachments` | listed by their verbs |
| `Diagrams` | counted on the row; listed by `diagram sN` (R2) |
| `ErrorStackTrace` | kilobytes; `failures` and `repro` read it as `thrownAt` (`Narrative.cs:117`, `:207`, `:332`) |
| `TimeLeftSeconds`, `RowTags`, `EffectiveTags`, `FeatureTags`, `Tags`, `Address`, `Failed`, `Ordinal` | derived from members the row has |

**The merge.** Its comment's "nothing user-facing keys on it" (`MergeableReportMerger.cs:409`) stops being true and
is corrected. The renaming rule stays, and the docs say that a merged report's colliding ids carry `#N` and that a
host checks its own run's report.

### 5.2 R2: the diagram verb

- **The listing.** `diagram <scenario>` (`sN`, `sid:` or `id:`) lists the scenario's diagrams the way `note sN/dK`
  lists notes: one row each, with its address, its size and its note count. Reading each diagram to size it is
  bounded by one scenario's diagrams (F11: one at the default rendering).
- **One scenario's files.** `diagram <scenario> --out DIR` writes every diagram of the scenario as `DIR/sN-dK.puml`,
  creating `DIR`, in one call. With a scenario address `--out` names a directory. With `sN/dK` it names a file, as
  today.
- **The whole run.** `diagram --out DIR` with no address writes every diagram in the run (Q5). For #184's
  100-scenario run that is one process where today there are about 200.
- **Unchanged refusals:**
  - `diagram` with neither an address nor `--out` is still refused (usage), with a hint naming both forms.
  - `sN/dK` without `--out` is still refused, because a diagram can fill a context window.
- **JSON.** `diagram` answers `--json` (F10). It gives one item per diagram listed or written (`address`, `bytes`,
  `notes`, plus `path` when written). `note` keeps its prose form.
- **File names.** Files are named by address, not by id (Q6). F5's ids are not safe as file names, and
  `scenarios --json` maps one to the other.
- **The verb table entry.** Its summary and forms (`VerbTable.cs:241-245`) change to match. `SkillDriftTests` and
  `DescribeTests.cs:95` check them.

### 5.3 R3: refusals a program can branch on

`error.kind` joins `exitCode`, `message` and `hint` in the `--json` error envelope (`QueryWriter.cs:27-62`). The exit
codes do not change (Q7). Moving a refusal to a new code would change what an existing script sees, which is a major
under this repo's rule. The kinds come from the 190-message inventory (`results/refusals-5.20.0.tsv`):

| Kind | Exit | For | Examples |
|---|---|---|---|
| `usage` | 2 | an unknown verb or flag, a malformed address, a form a verb does not have | `Unknown query command`, `summary answers for the whole run` (`Overview.cs:18`), `--json` on a verb without one (`QueryCommand.cs:246`) |
| `notFound` | 2 | a well-formed name the report or ledger does not hold | `No scenario s9`, `No diagram d1 in s0`, `No step`, `No body`, `No trace`, `No scenario with stableId`, `id:` with no match, `No suite`, `No baseline` |
| `ambiguous` | 2 | a name that matches several | `sid:` or `id:` matching two scenarios, `Several reports under` a directory (`QueryCommand.cs:472`), a trace prefix (`QueryCommand.Trace.cs:91`) |
| `noReport` | 2 | no report where one was named | `No such file or directory` (`QueryCommand.cs:450`), no report under a directory (`:467`) |
| `unsupported` | 2 | a report or environment that cannot answer this verb | reports with no `stableId`s to match (`Search.cs:466`), history switched off |
| `unreadable` | 1 | a report, body or ledger that cannot be read | not valid JSON (`QueryCommand.cs:186`), a body that cannot be read back (`Payloads.cs:475`, `QueryCommand.Diff.cs:320`), a ledger read failure (`QueryCommand.History.cs:36`, `:129`) |
| `writeFailed` | 1 | `--out` empty or not writable | `QueryWriter.cs:327`, `:357`, `:411` |

**Mechanism (F14).**
- A per-call `[ThreadStatic]` kind sits beside `_lastResolvedReport` (`QueryCommand.cs:96-98`) and is reset with it
  (`:108`).
- A small `Refuse` helper writes the prose to `error` and records the kind.
- `Run` passes the kind to `QueryErrorEnvelope.Build` (`:85-92`). A refusal that names no kind falls back by exit
  code: 2 to `usage`, 1 to `unreadable`.
- Text mode does not change.
- R3's first task is to classify each of the 190 messages by hand, in the harness TSV. The keyword rule there is a
  first guess, and it misfiles some (it reads `summary answers for the whole run` as `notFound`).

**Kept by tests.**
- One fact per kind through `Run` with `--json`.
- The defaults.
- A source walker that fails on an `error.WriteLine` beginning `No ` anywhere in `src/Kronikol/Query` that does not
  go through the helper.
- `--describe`'s envelope text (`QueryCommand.Describe.cs:42`) and a new `errorKinds` list, with a `DescribeTests`
  fact that every kind the code uses is described.

### 5.4 R4 (only if Q1 says so): completeness of `summary` and `grep --count`

- **#183 (a minor).** `summary --json` keeps `kind`, `count` and `message` on each group and adds `entries`: every
  diagnostic of the kind, each with `message` and, when it names a scenario, `id` and `scenario` (its address,
  through R1's resolver, F4). The text and the `!` notes stay one line per kind, plus a pointer (`summary --json
  lists all N`) where `count` is more than 1.
- **#182 (a patch).** When `grep --count` searched the default targets, stderr names what it did not search, as the
  text answer does (`! not searched: headers, notes (--in ... widens it)`), and stdout stays one number. Putting
  headers into the default would change existing counts, a change of default and so a major, which waits for v6.

## 6. Releases

The versions are claimed at push, after the peers' releases in flight (#191 R1 and R2). Each release is test first,
with every new fact red on 5.20.0 before the code changes.

### R1 (minor): identity and the scenario row

**Code:**
- `Address.cs`: `AddressKind.TestId`, parse and `ToString`.
- `QueryCommand.Narrative.cs`: `TryTestId`, a scenario-only resolver.
- `QueryCommand.Overview.cs`: the row, `summary`, `services` and `parties`.
- `QueryCommand.Values.cs`.
- `QueryCommand.Search.cs`: `compare` and the `grep` note.
- `VerbTable.cs`: the address form, plus the help text.
- `Reports/FailuresDigestGenerator.cs`: `id` on failure lines.
- `Reports/Merge/MergeableReportMerger.cs`: the comment.

**Facts, red first:**
- The probe's two tests with one name are each reached by `id:`, where `sid:` is refused.
- `id:` with no match exits 2 with its message and hint; a renamed `X#1` is named in that hint.
- Two scenarios with one id (a written merged report) are refused, with both listed.
- An id holding `/`, `:`, brackets and a space round-trips, Kronikol4J's shape among them.
- `id`:
  - matches the tests file's `testId` on `scenarios --json` rows, and an adapter-written report's id;
  - is on the `summary` failed, `failures`, `repro` and `Failures.jsonl` items, and not on `diff`'s;
  - is in `QueryJsonTests.cs:384-397`'s one-shape list.
- `services`, `parties` and `values` take `sid:` and `id:` (red today, F18), and still refuse a step path.
- `compare`:
  - `compare sid:<x> s1` answers;
  - `compare s0/i0 s1` and `compare b:<h> s1` are refused, naming the verb that reads each (red today, F19).
- The scenario row:
  - `calls` counts calls (half of `interactions` for answered calls; once for a one-record call);
  - `diagrams`, `sourceFile`, `sourceLine`, `rule`, `isHappyPath` and `featureSourceFile` match the data file;
  - the projection guard fails when a new `ScenarioEntry` property is neither projected nor excluded.
- `--describe` lists `id:`.
- `grep` with no hits for `id:42` names the address.

**Measure before relying on it:** the ids of BreakfastProvider's six lanes and of `kronikol merge` over two of them
(the `#N` renames and repeated ids), and TUnit's id shape.

**Docs:**
- Wiki `Querying-Reports.md`: the address table (`:172`), the prose (`:203`), and the members table's `scenarios`,
  `failures` and `repro` rows (`:394`, `:395`, `:397`), with what `id` means on a `services` or `parties` row beside
  it.
- Wiki `Generated-Reports.md:614` (the `Failures.jsonl` sample).
- The skill: `.claude/skills/kronikol-test-debugging/SKILL.md:166-167` and `references/commands.md:25`, `:37`, and the
  identical copies under `templates/skills/`.
- `src/Kronikol/Reports/agent-instructions.md:89`, `:96`.
- The changelog: a minor, for a new address form and new members.
- Roadmap 7.1's projection half is marked shipped (Q10).

### R2 (minor): the diagram verb

**Code:** `QueryCommand.Payloads.cs` (`Diagram` and `TryDiagram`) and `VerbTable.cs` (`Json: true`, forms, summary and
help line).

**Facts:**
- `diagram s0` lists a scenario with two diagrams (a report written with a server-side rendering, or a written
  fixture).
- `diagram s0 --out DIR` and `diagram id:X --out DIR` write `s0-d0.puml` and `s0-d1.puml`, byte-identical to `s0/d0`
  and `s0/d1`.
- `diagram --out DIR` writes every scenario's diagrams, and none for a scenario with none.
- `--json` gives the items.
- With a scenario address, `--out` naming an existing file is refused.
- `sN/dK` behaves exactly as today (`QueryCommandTests.cs:505-520`, `CompressedReportQueryTests.cs:65`,
  `QueryCultureTests.cs:89`).

**Docs:**
- Wiki `Querying-Reports.md`: the `diagram` section, `:593`, and a `diagram` row in the members table.
- The skill's `diagram` entry, in both copies.
- The schema's description of `diagrams` (`ReportGenerator.cs:7839` says they are "never needed to answer a
  question", which stays true for an agent, though an agent is no longer the only reader).
- The changelog.

### R3 (minor): refusal kinds

**Code:**
- `QueryWriter.cs` (`QueryErrorEnvelope.Build` takes the kind) and `QueryCommand.cs` (the field, the reset and `Run`).
- The `Refuse` helper and the classified sites.
- `VerbTable.cs` (`ErrorKinds`) and `QueryCommand.Describe.cs`.

**Facts:** as in 5.3, with `query.cs`'s reflection binding still running against the new build, and every other
envelope member unchanged on every path.

**Docs:** wiki `Querying-Reports.md` (the error example, `:371-373`, and the exit codes section, `:1751-1762`), the
skill's `--json` notes in both copies, and the changelog.

### R4 (optional, Q1): #183 as a minor, #182 as a patch

As in 5.4. #183's facts use the issue's two degraded taps. #182's facts assert the stderr line and an unchanged stdout.
The wiki and the skill are updated in both cases.

## 7. Compatibility

- **Additive throughout.** The envelope's `formatVersion` stays 1 (F23).
- **Text answers.** These change only where a form was refused or misread before: `diagram sN`, `id:`, `sid:` on four
  verbs, and `compare`. `compare s0/i0 s1` answered for `s0` and is now refused; that is the one answer a script could
  have relied on that goes away, and it was a wrong answer.
- **The data file.** It is untouched, so older tools and Kronikol4J are unaffected. `Failures.jsonl` gains `id`, an
  added member.
- **`QueryCommand.Run`'s signature,** which `query.cs` binds, does not change.
- **Pinned output.** A golden that pins the scenario row's member order sees the new members arrive. None pins it
  today (no test reads `stableIdShared`), but each release's notes list any it meets.

## 8. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | Take #183 (R4a, a minor) and #182's stderr line (R4b, a patch) into this plan? | Yes. Same rule, same host, same afternoon; and #183's entries use R1's resolver. |
| Q2 | Make the scenario row whole in R1 (`calls`, `diagrams`, `sourceFile`, `sourceLine`, `rule`, `isHappyPath` and `featureSourceFile`, with the guard), or add only `id`? | All of it. The row is touched anyway, roadmap 7.1 calls its two fields "shippable now", and the guard is what stops the next #76. |
| Q3 | Name the member `id` and the address `id:`, as the data file and the issue do, or `testId` and something like `tid:`, since `services` and `parties` rows already use `id` for a party? | `id` and `id:`, with the docs saying which `id` each row means. |
| Q4 | Match `id:` exactly, with no child forms? | Yes. Ids are free text with `/` in them (F5). |
| Q5 | Should `diagram --out DIR` with no address write the whole run's diagrams? | Yes. One process per run. |
| Q6 | Name the files by address (`s0-d0.puml`) or by id? | By address. |
| Q7 | Seven kinds in `error.kind`, and no new exit code? | Yes. A new code for `notFound` or `noReport` could come with v6. |
| Q8 | When the merge renames a colliding id to `<id>#N`, should it also record the producer's id? | No. The `id:` hint names the renamed ids, and a host checks its own run's report. |
| Q9 | A public in-process decoder or typed model for .NET hosts? | No. It would make the data file public surface. |
| Q10 | Where on the roadmap? | Beside 7.1, whose projection half R1 ships. #77 (7.3) stays its own. |

## 9. Harness

See `QUERY_FOR_PROGRAMS_PLAN.harness/README.md`. It holds three probes and their output, BreakfastProvider's ids, the
190-message refusal inventory, the related open issues and the issue as filed.

## 10. Audit log (2026-10-28)

The owner asked for the plan to be checked in full and for any deep dive that could change it.

**Rebased.**
- From 5.19.0 to 5.20.0 (`69ed51a7`, #192 R3).
- Every citation was re-read. Eight moved (five in `QueryCommand.Payloads.cs`, by 5 lines, and three in
  `ReportIndex.cs`, by 6), and the plan now cites the new lines.
- The wiki was re-read at `28724ec`, where the exit codes section moved to `:1751`.

**Deep dives that changed the plan:**
- **Producers' ids** (F5, F24). These set the grammar (no child forms, matched whole) and the file naming (Q6).
- **The merge's id handling** (F6). This adds the `#N` hint and Q8.
- **Two verbs the first draft missed** (F18, F19). `values` refuses `sid:` (RUN). `compare` refuses a `sid:`
  that is in the report and widens `s0/i0` to `s0` (RUN). Both fixes are in R1.
- **`diagram` has no JSON form** (F10). R2 adds one.
- **Diagram counts follow the rendering** (F11). This explains the default's one diagram per scenario and gives R2's
  fixture.
- **The refusal inventory**: 190 messages in 17 files, not 33 in eight (F13). It gave two more kinds, `noReport` and
  `unsupported`, and the classification task.
- **Failures.jsonl's shared names** (F17). `id` goes there too.
- **The scenario row's missing facts** (F15). These led to Q2, the guard, and the link to roadmap 7.1.
- **The related open issues** (4.1). These place #184 in its family, separate #77's identity (F7), and set the scope
  line.
- **The name clash on `id`** (F8). This is Q3.

**Moved or dropped:**
- `diagrams` moved from R2 to R1, being a row member R1's guard must decide.
- The first draft's Q8, on re-cased names, is answered by the code: the behaviour is documented and has an opt-out
  (C2).
- **Checked, no change:** the tests file's failure message key is `error` (`Ingestion/TestRunRecord.cs:58`). A probe
  that wrote `errorMessage` read a null message for that reason, not because of a bug.
