# Participant order plan (#228): the service under test next to the test

**Status:** draft of 2026-11-07, **not green-lit**. Written on `896ddad3` (5.31.1), on the local branch
`plan/228-participant-order`, not pushed. Issue: [#228](https://github.com/lemonlion/Kronikol/issues/228), filed
2026-11-04. Harness: [`PARTICIPANT_ORDER_PLAN.harness/`](PARTICIPANT_ORDER_PLAN.harness/README.md).

## 1. In short

- **What #228 reports.** A scenario that sets up its own stores first draws them before the service under test:
  `Caller, disk, git, app`, and every call the app makes to those stores points left. No option changes the order,
  and declaring the app `PartyKind.UnderTest` changes only the parties table. Reproduced on `896ddad3` in §2.1.
- **Root cause.** Two rules in the product answer "who is the test and who is under test". The party resolver (#190,
  5.14.0) answers it from declarations, observation and inference, and feeds the parties table, `query parties` and
  the service card. The sequence diagram answers it with an older rule of its own: the first caller nobody calls,
  then everyone in the order the log met them. That rule knows no roles, and the log starts with the test's own setup.
  The diagram also cannot ask the resolver today: the diagrams are drawn at `ReportGenerator.cs:354`, and the parties
  are resolved at `:530`.
- **The fix (R1).** One answer for both. Resolve the parties once, before the diagrams, and draw each test as
  *the test's parties, then the parties under test, then everyone else in the order of the first call, as now*. The
  issue's suite is then fixed by one declaration it can already write (`app` as `UnderTest`, which today feeds only
  the parties table), or by `ServiceId` alone, with nothing new to configure. Q1 decides whether a run that declares nothing is laid out by the inferred roles too. The plan
  recommends not, so that run keeps its bytes, as #190 promised.
- **Measured on real data.** 1,396 diagrams from the reference consumer's latest CI run (BreakfastProvider, seven
  lanes, run `42359580430`): the rule changes **none** of them, because the subject is already second wherever the
  test calls it. The candidate rules that need no roles, "callers left of callees" and "the action phase first",
  reorder 37 to 39 diagrams per lane and 40 to 44 per BDD lane, and the first of them moves message brokers in front
  of the service. §4.
- **A sibling defect, same cause (F2, R2).** Where a scenario never calls the service through a tracked client, the
  diagram draws the service under test as the stick figure meant for the test: five such diagrams in those seven
  lanes (§2.4). R2 draws it as a box. That is a change to what the diagram draws, so it is its own release and
  question (Q3).
- **Not recommended: the issue's `SequenceDiagramParticipantOrder`.** It would be a third way to name the service
  under test, beside `Parties` and `ServiceId`, with its own name matching and its own diagnostic, where the existing
  declaration says the same thing in one line. Q2 keeps it open for orders a role cannot express (dependencies among
  themselves), with no evidence yet that a suite needs one.

Six questions for the owner are in §9, each with a recommendation. Each finding is traced to its fix and its test in
§10.

## 2. What happens today, measured

### 2.1 Reproduced on `896ddad3`

[`repro/issue_shape.cs`](PARTICIPANT_ORDER_PLAN.harness/repro/issue_shape.cs) runs the issue's scenario through the
real `PlantUmlCreator`: the test makes a folder on the disk and commits files to git, runs the app, and the app reads
git and keeps a file on the disk. Its output
([`results/issue-shape-896ddad3.txt`](PARTICIPANT_ORDER_PLAN.harness/results/issue-shape-896ddad3.txt)) is the
issue's diagram, whether the calls' phases are unknown (a plain test) or recorded (Given as `Setup`, When as
`Action`, as a BDD adapter records them; responses left out here):

```plantuml
actor "Caller" as caller
database "disk" as disk
database "git" as git
participant "app" as app
caller -[#0E7C86]> disk: Make a folder: /work
caller -[#0E7C86]> git: Commit files: /repo
caller -[#438DD5]> app: Run: /run
app -[#0E7C86]> git: Read files: /repo
app -[#0E7C86]> disk: Keep: /work/out
```

### 2.2 The rule in the code

`PlantUmlCreator.CreateEntitiesPlantUml` (`src/Kronikol/PlantUml/PlantUmlCreator.cs:1312-1460`):

- It reads every call of the test except the markers (`:1322-1324`).
- The "pure caller", the first `CallerName` that is never a `ServiceName` in this test, is declared first, as an
  `actor` unless its calls carry a caller category (`:1338-1388`).
- Then, call by call in log order, the caller and the service are each declared the first time they appear
  (`:1406-1457`). A second caller without a category is a `participant` (`:1433`).

The order is decided once per test. `CreatePlantUmlPrefix` (`:1232`) builds it again for each fragment of a split
diagram, and `DeclareOnlyUsedParticipants` (`:1990`) removes what a fragment does not draw, keeping the order. The
page lines the fragments up by that declaration order (`plantuml-browser-render-script.js:1247-1259`).

`SeparateSetup` (default `false`) draws a `Setup` partition around the calls before the boundary (`:242-320`), but
`CreateEntitiesPlantUml` takes no part in it: the order is the same with the partition on. The call's own
`Phase` (`RequestResponseLog.cs:159`, from `TestPhaseContext.Current` when it was captured) is never read here.

### 2.3 What the run knows that the diagram does not

`PartyResolver` (`src/Kronikol/Parties/PartyResolver.cs`) gives every party a `PartyKind` (`Test`, `UnderTest`,
`Real`, `Double`, `Unknown`) and says where it came from (`kindSource`). For each name the first that applies wins
(`:83-91`):

1. `ReportConfigurationOptions.Parties`, then the parties file (`.kronikol/parties.json`, `KRONIKOL_PARTIES`,
   `PartiesFilePath`): **declared**.
2. A client built by `CreateTestTrackingClient<T>` (`WebApplicationFactoryExtensions.cs:22-33`): its caller is the
   test and whom it calls is under test (`TestTrackingMessageHandler.cs:247-248`): **observed**.
3. Every call to it carries a `testDouble` mark: a double, **observed**.
4. The callers nobody calls whose calls carry no caller category, or only `User`, and are mostly attributed to the test:
   the test, **inferred** (`InferTests`, `:205-227`).
5. When nothing declared or observed is under test (`:93-100`): the test parties' callees, stores left out by the
   diagram's own palette (`IsStore`, `:250-253`), one that makes calls of its own first, then the most called; a
   second only when it too makes calls and takes a quarter of the tests' calls: under test, **inferred**
   (`InferSubjects`, `:233-248`).

`ServiceId` sets no kind; it names the subject's row (`:102-119`). The wiki states the contract (Report-Configuration,
wiki `f73bdd6`, lines 281-292): *"Only a run that declares something writes the table ... The diagrams draw every
party by its name alone ... A run that declares nothing writes the same report as before"*. The #190 plan says the
same (`PARTY_IDENTITY_PLAN.md` §5.13, lines 785-788: *"An observed or inferred kind draws nothing, so no suite's
diagrams change until it declares"*).

**Drawn before it is known.** `ReportGenerator.CreateStandardReportsWithDiagramsCore` draws every diagram at
`ReportGenerator.cs:354` (`DefaultDiagramsFetcher.GetDiagramsFetcher(fetcherOptions)()`, over `runLogs`, `:271`) and
resolves the parties at `:530` (`PartiesReport.Resolve`, over `dataLogs`, `:390`). Everything `Resolve` reads exists
before `:354`. From 5.14.0 to 5.16.1 the diagrams did take something from the parties: an ambient scope,
`DiagramParties.Declare(...)`, around `:354` put «test double» on declared doubles, *"because the diagrams are drawn
through public methods whose parameter lists a new option must not grow"*. 5.16.2 (`8725fb5d`) removed it because the
owner reversed that decision (no stereotype on the drawing), not for any technical reason.

### 2.4 Real data

The reference consumer's latest CI run (BreakfastProvider, run `42359580430` of 2026-11-07, Kronikol 5.14.0) gave
seven lane reports. The run declares `serviceId: breakfast-provider` and every one of its 18 parties (`query
parties`: `Breakfast Provider` `underTest`, `Caller` `test`, the other 16 doubles), so it is the case Q1 (a) lays
out by roles. Each lane's diagrams were written out with the published tool (`Kronikol.Tool 5.31.1`, `query diagram <report> --out DIR`), 1,396 in all, and read by
[`order_rules.py`](PARTICIPANT_ORDER_PLAN.harness/order_rules.py). The script rebuilds today's order from the arrows
and agrees with the declared order on every diagram that has arrows, which is the check that it reads what the code
wrote. Results: [`results/breakfastprovider-5.14.0.txt`](PARTICIPANT_ORDER_PLAN.harness/results/breakfastprovider-5.14.0.txt).

| First declaration | Diagrams | What it is |
|---|---:|---|
| `actor "Caller"` | 1,362 | the test, then the service: right |
| `participant "(no interactions)"` | 14 | steps and assertions only |
| `queue "Kafka Broker"` / `"Google Cloud Pub/Sub"` | 10 / 5 | a scenario driven by a message, the service second: right |
| `actor "Breakfast Provider"` | 5 | **F2**: no tracked call from the test, so the service under test is drawn as the test's stick figure |

- The service under test is second in every diagram where the test calls it. The consumer's tests never call a store
  directly, so #228's shape does not occur there. This corpus measures what a rule breaks, not what it fixes; §2.1
  is the shape it fixes.
- 38 to 40 diagrams per in-memory lane have requests that point left. All 564 such requests are a broker delivering
  a message to the service (`kafkaBroker -> breakfastProvider: Consume`, [`results/leftward-breakfastprovider-5.14.0.txt`](PARTICIPANT_ORDER_PLAN.harness/results/leftward-breakfastprovider-5.14.0.txt)),
  the broker drawn right of the service as a dependency. That is the
  usual layout for a consumed message, not a defect.
- Run-wide, the resolver's inference (rebuilt from the sources, stores read off the shapes) names
  `breakfastProvider` the subject in every lane.

The suite #228 was found in is not on this machine (searched `C:/Code` and the user folders); the repro stands in
for it.

## 3. Root cause

The defect is not the order rule's choice of "first call". It is that the diagram decides roles on its own, without
the knowledge the run has:

1. **The diagram's rule knows no roles.** "The first caller nobody calls, then the order of the log" makes the log's
   order the layout, and a test's log begins with whatever the test arranges. A scenario that arranges through the
   service (BreakfastProvider) looks right by luck; one that arranges its stores directly (#228) does not.
2. **The roles exist, in the wrong place in time.** Since 5.14.0 the resolver knows who is under test, declared,
   observed or inferred. It runs after the diagrams are drawn (§2.3), so the diagrams cannot use it, and the report
   holds two answers to one question: in a run that declares `app` under test, the parties table says so and the
   diagram draws it last.
3. **The same cause draws F2.** In a scenario where the test makes no tracked call, the diagram's "first caller
   nobody calls" is the service under test, and it is drawn as the `actor`, the shape the diagram uses for the test.

The issue's first suggestion, a list of participant names to declare first, fixes the symptom in a suite that sets
it, and leaves the cause: a third answer to "who is under test" (beside `Parties` and `ServiceId`), held in a
different option, matched by a different rule, reported by a different diagnostic. Its second suggestion (the
`UnderTest` party after the caller) is the cause's fix. This plan takes it, without the option, because a run's
declaration is already the opt-in: a run that declares nothing is not touched (Q1), and in one that does, only the
diagrams where the declared subject is not second change, which are the defect itself.

## 4. Rules considered, measured

Each rule was run over the 1,396 diagrams of §2.4 and over the repro. "Reordered" counts diagrams whose declaration
order differs from today's, per lane (in-memory lanes; the external-SUT lanes change under no rule).

| Rule | Repro (#228) | Reordered per lane | Verdict |
|---|---|---:|---|
| R0 today: the first caller nobody calls, then first appearance | `caller, disk, git, app` | 0 | the defect |
| RA callers left of callees (topological over requests, ties by first appearance) | `caller, app, disk, git` | 37-39 | **rejected**: a broker that delivers to the service becomes its caller and moves in front of it (`caller, azureEventHub, breakfastProvider, ...`) |
| RB the action phase first: first appearance among When/Then calls, then the rest | `caller, app, git, disk` | 40-44 (BDD lanes) | **rejected**: reorders the dependencies of a fifth of the BDD diagrams for nothing, and does nothing for a plain test, whose calls carry no phase unless it calls `StartAction()` (only steps and `StartAction()` set one) |
| RC RB's least move: only the test's first callee in the action phase goes second | `caller, app, disk, git` | 0 | works, but a second inference beside the resolver's, and needs a phase |
| RU the party the run resolves as under test goes second (**R1**) | `caller, app, disk, git` | 0 | **chosen**: the run's own answer, no new rule |
| The issue's `SequenceDiagramParticipantOrder = ["app"]` | `caller, app, disk, git` | 0 (unset) | not recommended (§3, Q2) |
| The issue's "UnderTest after the caller, behind an option" | `caller, app, disk, git` | 0 | R1 without the option (Q1) |

RC is worth keeping in mind: if Q1 chooses to lay out runs that declare nothing, the resolver's inference
(`InferSubjects`) is the rule to use, not RC, so that the table, `query parties`, the service card and the diagram
still agree.

## 5. The design (R1)

### 5.1 The rule

For each test, the participants are declared in three groups, each in the order of its first call, as today:

1. **The test**: the pure caller as today, then any other party the run resolves as `Test` that this test's calls
   carry. The pure caller keeps the `actor`; with no pure caller there is no actor (as today). The pure caller stays
   first even when the run resolves it as something else: a broker delivering the message a scenario starts from
   reads left to right as `broker, service, dependencies` (15 diagrams in §2.4), and only R2 touches a pure caller
   that is under test.
2. **Under test**: every party the run resolves as `UnderTest` that this test's calls carry.
3. **The rest**: everyone else, in the order of the first call, exactly as now.

Shapes, colours, aliases and every arrow and note are unchanged; only the declaration lines move. A test whose
subject is already second, or whose calls carry no subject, is byte-identical. A party under test that is the pure
caller (F2) stays first in R1; R2 decides its shape.

### 5.2 Where the roles come from

- **One resolution per report.** Move `PartiesReport.Resolve` from `ReportGenerator.cs:530` to before `:354`, over the
  calls the report carries, and pass the same result to `:530`'s readers. Those calls are `dataLogs` (`:390`), which
  either branch builds as the run's logs without `TrackingIgnore` (`:363`, `:390`), so they can be computed before
  `:354` from `runLogs` alone. The table, the diagnostics and the diagrams then read one answer. The diagnostics are still recorded
  before the snapshot at `:546`.
- **Which kinds move a party is Q1.** Recommended (a): only when the run writes the table, that is, declares
  something (`ServiceId`, `Parties` or a parties file), and then every kind the table holds, whatever its source.
  `ServiceId` alone turns it on: the table is written, and the subject is observed or inferred inside it.

### 5.3 How the roles reach the diagram

- `DiagramsFetcherOptions` gets an **internal** `Roles` (the names resolved `Test` and `UnderTest`), set by the
  report generator and by ingest. Not public: nothing new for a consumer to set.
- `PlantUmlCreator.GetPlantUmlImageTagsPerTestId` is public, and an optional parameter added to it is a binary break.
  The roles reach `CreateEntitiesPlantUml` through an internal overload, or through an ambient scope as
  `DiagramParties` did (§2.3); the overload is preferred, since a scope is invisible at the call site.
- **The process-wide cache.** `DefaultDiagramsFetcher` caches the first diagram set for the life of the process and
  ignores the options of later calls (`DefaultDiagramsFetcher.cs:14-36`). BDDfy's batch processor can fill it first,
  without roles (`DiagramEnhancingBatchProcessor.cs:119`), and the report would then reuse diagrams drawn without
  them. The cache must remember the roles it drew with and draw again when a later fetch carries different ones. A
  fact pins this (P4 in §7).
- **Ingest** resets the cache and draws again (`IngestPipeline.cs:687, 703`) and already resolves parties from
  `--parties` and `--service-id` (#190 R2): it passes its roles the same way. **Merge** never draws again
  (`MergeableReportMerger.cs:378, 478`): each shard keeps the order its own run drew, so a merged report agrees with
  its shards.
- **The CI summary's diagrams** (`ReportGenerator.cs:799`) are drawn after the resolution and take the same roles.

### 5.4 Names

The resolver matches the raw `CallerName` and `ServiceName`, ordinal (`PartiesReport.cs:42-44`,
`PartyResolver.cs:57-58`); the diagram keys participants by alias (`SanitizePlantUmlAlias`, `:1498-1504`, which drops
spaces, `-` and `_`). Roles are looked up by the raw name, then declared by alias. Two raw names that share an alias
are already one participant today; it is under test if either name is. A fact pins that (T7).

### 5.5 What does not change

- A run that declares nothing (under Q1 (a)): every byte of every diagram, in the report, the data file and every
  output.
- Kronikol4J: it has no parties (it mirrors #190's R0 only), so under Q1 (a) its output and the .NET output agree on
  every run Kronikol4J can produce. The ledger records the new rule as not mirrored, with the reason (§8.4).
- The data file, the parties table, the diagnostics and `query parties`: unchanged; the table is the input.
- The component diagram keeps its order (callers first seen, then services, `ComponentDiagramGenerator.cs:261-267`).
  Q4 asks whether it should follow.

### 5.6 F2 (R2): the service drawn as the test

In R2, a pure caller the run resolves as anything other than `Test` is declared as a service is, by its category's
shape or a `participant`, never as the `actor`. The `actor` stays the shape of the test. Measured: the five
`actor "Breakfast Provider"` diagrams of §2.4 change, nothing else. Q3.

## 6. Releases

| Release | What | Bump | Gate |
|---|---|---|---|
| R1 | §5.1-5.5: the roles resolved once, before the diagrams; the test's parties, then the parties under test, then the rest | minor recommended (Q5) | Q1, Q5 |
| R2 | §5.6: a party under test is never drawn as the actor | patch | Q3 |
| (R3) | the issue's explicit order option, only if Q2 says so | minor | Q2 |

No version number is written until a release is pushed; peers take numbers at push time.

## 7. Tests (red first)

Every fact below is written first and run red on `896ddad3`, then green. Unit facts go in
`tests/Kronikol.Tests/PlantUml/` beside the existing `CreateEntitiesPlantUml` facts; whole-pipeline facts take
`ReportTestHelper.WholePipeline` (it resets the diagram cache) and the `DiagramsFetcher` collection.

**Unit (`PlantUmlCreator`, roles passed through the internal overload):**

- T1 the repro's calls, `app` under test: `caller, app, disk, git`, with `separateSetup` off and on. Red today.
- T2 the same calls, no roles: the whole source is byte-identical to today's.
- T3 the subject already second: byte-identical.
- T4 a test whose calls carry no subject: byte-identical.
- T5 two parties under test: both after the caller, in the order of their first call.
- T6 a diagram split for width (`MaxParticipantRowWidthPx`): every fragment declares the caller, then the subject
  when it draws it, then the rest in the whole test's order.
- T7 two raw names with one alias, the role given for either: the one participant moves.
- T8 the subject is the pure caller (F2): unchanged in R1; in R2 a `participant` (or its category's shape), no
  `actor`.
- T9 two test parties: both first, in the order of their first call; the first keeps the `actor`.
- T10 only declarations move: the source with its declaration lines removed is byte-identical to today's.

**Pipeline (`CreateStandardReportsWithDiagrams`, ingest):**

- P1 `Parties = [new("app") { Kind = PartyKind.UnderTest }]`: the report's diagram source for the scenario declares
  `app` second; the data file's parties block says `underTest`, `declared`.
- P2 `ServiceId` alone, the app observed through `CreateTestTrackingClient`: `app` second.
- P2b `ServiceId` alone, nothing observed, the repro's calls: the inferred subject `app` second, both with the stores'
  category `Database` and with one the palette does not know (`Disk`, which resolves to no store): `app` makes calls
  and the stores do not, so the inference still names `app` alone (`InferSubjects`, `PartyResolver.cs:233-248`).
- P3 nothing declared: every diagram source the page carries equals the same run's on the previous release's rule
  (Q1 (a)).
- P4 the cache filled first without roles (a fetch with no roles, as BDDfy's batch processor makes), then the report:
  `app` second.
- P5 `kronikol ingest --parties` with `app` under test: `app` second.
- P6 the CI summary's diagram for the scenario matches the report's.

**Playwright (`tests/Kronikol.Tests.EndToEnd/`):** a report of the repro's scenario with `app` declared under test,
drawn by the default renderer: the drawn `app` box's left edge is left of `disk`'s and `git`'s, read from the SVG
after the diagram draws (`WaitForFunctionAsync` with `PollingInterval = 200`). A control in the same class: nothing
declared, `disk` left of `app`. Light and dark themes are not needed: the layout, not the colour, is under test.

**Mutations, each to be killed by a named fact:** M1 skip the under-test group (T1, P1); M2 look roles up by alias
(T7); M3 a cache that ignores roles (P4); M4 apply roles when the run writes no table (P3); M5 order the subjects by
name (T5); M6 decide the order per fragment (T6); M7 resolve after the diagrams again (P1, P2).

## 8. What the change touches

From a read-only sweep of both repositories on 2026-11-07; each line below was re-read by hand.

### 8.1 Code

- `src/Kronikol/PlantUml/PlantUmlCreator.cs`: `CreateEntitiesPlantUml` (`1312-1460`) takes the roles and declares the
  three groups; `CreatePlantUmlPrefix` (`1232-1258`) and both `DiagramBuilder` call sites (`2043-2046`, `2266-2267`)
  pass them; an internal overload of `GetPlantUmlImageTagsPerTestId` (`77-161`) and `CreatePlantUml` (`171-229`)
  carries them. The prefix must stay a pure function of the traces, the options and the roles: a fragment's body is
  cut from the buffer by the prefix's length (`2254`).
- `src/Kronikol/DiagramsFetcherOptions.cs`: the internal `Roles`. `src/Kronikol/DefaultDiagramsFetcher.cs`: passes them
  (`257-291`) and keeps them with the cache (`14-36`).
- `src/Kronikol/Reports/ReportGenerator.cs`: the resolution moves before `:354` with the `dataLogs` expression (`:390`);
  `:530` reads the result; the fetcher options (`321-353`) and the CI summary (`799`) take the roles.
- `src/Kronikol/Parties/PartiesReport.cs` / `PartyResolver.cs`: a way to read the `Test` and `UnderTest` names off
  the resolved table (no rule changes).
- `src/Kronikol/Ingest/IngestPipeline.cs` (`687-703`): passes the roles it resolves from `--parties` and `--service-id`.
- `src/Kronikol/Parties/PartyKind.cs:12-13`: `UnderTest`'s doc says what it now does to the diagram.
- `ServiceId` alone counts as declaring (`PartyDeclarations.Any`, `PartyResolver.cs:21`), so it is enough to turn R1
  on; the `ServiceId` doc comment says so.

Nothing public is added. Two constraints from the page's readers hold the design: the order is expressed by the
order of the declaration lines, never PlantUML's `order N` (the line-up of a diagram drawn in parts pairs the first
part's declarations with its lifelines left to right, `plantuml-browser-render-script.js:1256-1269, 1484-1510`), and
nothing new may sit between two declarations (`parseDiagramStructure` ends the header at the first line it does not
know, `800-829`).

### 8.2 What reads the order, and is fine

- History fingerprints interactions, never diagram source (`InteractionShape.cs:335, 489, 507`): no `behaviour-changed`.
- The search index takes the names in declaration order (`ReportGenerator.cs:7651-7669`): its bytes move, its results
  do not. Dependency chips are sorted.
- The role pass and the note filters find participants by geometry and keyword
  (`plantuml-browser-render-script.js:2459-2463`, `collapsible-notes-script.js:2815-2880`).
- `kronikol merge` keeps each shard's source and deduplicates diagrams by exact text
  (`MergeableReportMerger.cs:515-523`): shards drawn before and after R1 for the same test id would keep both
  diagrams. Shards of one run come from one version, so this needs no change; the changelog says it.
- Old reports keep their order: the data file holds PlantUML source only, and only ingest draws again.

### 8.3 Tests that change

- No C# test pins declaration order today: 40 declaration checks in `PlantUmlCreatorTests.cs` and those in
  `DiagramLookTests.cs:121-122`, `IngestPipelineTests.cs:378-380` and the IKVM `SequenceDiagramWidthTests.cs:192-226`
  are `Contains` checks, and keep passing while each line keeps its text and keyword.
- `Parties/DeclaredDoubleDiagramTests.cs:105-106` asserts that declaring parties leaves every declaration in place,
  in order. It pins #190's "a kind draws nothing", which R1 changes for order: it is rewritten to "declaring doubles
  changes no declaration's text", and its fixture checked for an inferred subject that is not second.
- R2 only: `extractCallerPayloads` (`context-menu-script.js:643-651`) takes the first `actor` as the test's caller. A
  diagram with no tracked call from the test has no actor after R2, so "Copy all caller request payloads" copies
  nothing there instead of the service's own requests. A Playwright fact pins it.

### 8.4 Kronikol4J and the documentation

- Kronikol4J's `appendParticipants` (`kronikol4j-diagram/.../plantuml/PlantUmlCreator.java:205-303`) has today's rule,
  pinned by `PlantUmlParityTest.pureCallerDeclaredFirst` (`277-283`). Kronikol4J has no parties, so R1 is a ledger
  entry in `docs/REMAINING_PARITY.md` (the section at `:1877`, on its `origin/main`), "Not mirrored", with the reason:
  only a run that declares parties is laid out by roles, and a Kronikol4J run cannot declare them. R2 likewise.
- Wiki (`Report-Configuration.md`, the Parties section, lines 236-300 on `f73bdd6`): the sentence "The diagrams draw
  every party by its name alone" gains the order, with the repro's before and after; the `kindSource` table says which
  kinds move a party (Q1). `Generated-Reports.md`'s sequence diagram description says how participants are ordered.
  The Integration pages that show `ServiceId` and `Parties` link to it.
- `CHANGELOG.md`: what moved, for which runs, the bump and why; the merge note of §8.2.
- `PARTY_IDENTITY_PLAN.md` §5.13 gains a line pointing here, since its "draws nothing" no longer holds for order.

## 9. Questions for the owner

- **Q1. Which runs are laid out by roles?** (a) Only a run that declares something (`ServiceId`, `Parties` or a
  parties file), with every kind its table holds. (b) Every run, inferred roles included. (c) Only a party declared
  `UnderTest`. **Recommended: (a).** It keeps #190's promise that a run declaring nothing writes the same report, it
  makes the diagram and the table agree wherever there is a table, and one line (`ServiceId`) turns it on. (b) fixes
  undeclared suites too and changed nothing on the reference consumer, but reorders, on a guess, diagrams nobody asked
  to change; it can follow later as its own release if wanted.
- **Q2. The issue's `SequenceDiagramParticipantOrder`?** **Recommended: not now.** R1 covers the issue's suite with
  the declaration it already has. If an order among dependencies is wanted later, it should name parties through the
  same table (a new `PartyDescriptor` field, `DeclaredPartyNotSeen` for a name no call carries) rather than a second
  list.
- **Q3. R2: draw a service under test that the test never called as a box, not as the test's stick figure?**
  **Recommended: yes**, a patch after R1. It changes five diagrams of the consumer's 1,396 and only in runs that
  declare. It is asked rather than folded in because it changes what the diagram draws, as the «test double»
  stereotype did, which was reversed in 5.16.2.
- **Q4. Should the component diagram follow?** **Recommended: no.** It lays out by edges, not by time, and #228 is
  about the sequence diagram.
- **Q5. R1's bump.** **Recommended: minor.** No new public surface, but an existing declaration gains a visible
  effect on every diagram of a declaring run, and the issue is an enhancement. A patch is defensible as a bug fix
  with a changed behaviour.
- **Q6. BDDfy's own `BDDfy.html`.** The batch processor draws its diagrams with options of its own and no
  declarations. **Recommended: leave it as it is** and make only the Kronikol report role-aware (§5.3); the BDDfy page
  then shows today's order until a parties file declares, which it reads.

## 10. Findings

| # | Finding | Fix | Fact |
|---|---|---|---|
| F1 | #228: stores the test sets up first are drawn before the service under test | R1 | T1, P1, P2, P5, Playwright |
| F2 | a service under test the test never called is drawn as the test's `actor` (5 of 1,396) | R2 (Q3) | T8 |
| F3 | the diagrams are drawn before the parties are resolved, so the report holds two answers to "who is under test" | R1 §5.2 | P1, M7 |
| F4 | the diagram cache keeps the first set drawn and ignores every later fetch's options; a fetch without roles (BDDfy's batch processor) would win. Whether that processor fetches before the report in a real BDDfy run, so that the report's other options are lost the same way, is the first thing R1 checks | R1 §5.3 | P4, M3 |
| F5 | `SeparateSetup` draws a Setup partition, but the order still comes from the setup calls | R1 (the subject's place no longer depends on which calls came first) | T1 (`separateSetup` on) |

## Appendix A. Harness

- `repro/issue_shape.cs`: the issue's scenario through the real `PlantUmlCreator` (`dotnet run --file`).
- `order_rules.py`: the rules of §4 over diagram sources (`python -I order_rules.py [--under-test alias] [--list] DIR...`).
- `results/`: the outputs quoted here. The consumer's reports and the diagram sources are not committed (7 lanes,
  14 MB); `README.md` says how to fetch them again.
