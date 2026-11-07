# Participant order plan (#228): the service under test next to the test

**Status:** written 2026-11-07; **green-lit in full** by the owner the same day, each question at its recommendation
(§12). R1 shipped as 5.33.0 and R2 as 5.34.1; the `BDDfy.html` patch (R4, #241) to come. Written on `896ddad3` (5.31.1) and pushed
with R1. Checked again the same day on `55999241` (5.31.2), whose only change to
the code cited here moved one line of the tracking handler; that pass measured R1's rule in full, read BDDfy's own
code for when its batch processor runs, and corrected what §2.3, §5.3, §8 and §9 said (§11 lists each change).
Issue: [#228](https://github.com/lemonlion/Kronikol/issues/228), filed 2026-11-04. Harness:
[`PARTICIPANT_ORDER_PLAN.harness/`](PARTICIPANT_ORDER_PLAN.harness/README.md).

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
  the parties table), or by `ServiceId` alone, with nothing new to configure. Q1 decides whether a run that declares
  nothing is laid out by the inferred roles too. The plan recommends not, so that run keeps its bytes, as the wiki
  promises (§2.3).
- **Where this departs from the issue.** The issue's second suggestion is this rule, but *"behind an option so that
  existing reports keep their bytes"*. R1 has no option: a run's declaration is the opt-in, so a run that declares
  nothing keeps every byte, and a run that declares changes only where the declared subject is not already second.
  That is a choice for the owner, not a finding: Q1 (d) is the issue's option, with what it costs.
- **Measured on real data.** 1,396 diagrams from the reference consumer's latest CI run (BreakfastProvider, seven
  lanes, run `42359580430`): the rule, measured in full as §5.1 states it, changes **none** of them, because the
  subject is already second wherever the test calls it. Neither does it change any of the 7 diagrams of Kronikol's
  own example reports, which declare nothing, under the roles the resolver infers for them (Q1 (b)). The candidate
  rules that need no roles, "callers left of callees" and "the action phase first", reorder 37 to 39 diagrams per
  lane and 40 to 44 per BDD lane, and the first of them moves message brokers in front of the service. §4.
- **A sibling defect, same cause (F2, R2).** Where a scenario never calls the service through a tracked client, the
  diagram draws the service under test as the stick figure meant for the test: five such diagrams in those seven
  lanes (§2.4). R2 draws it as a box. That is a change to what the diagram draws, so it is its own release and
  question (Q3).
- **Not recommended: the issue's `SequenceDiagramParticipantOrder`.** It would be a third way to name the service
  under test, beside `Parties` and `ServiceId`, with its own name matching and its own diagnostic, where the existing
  declaration says the same thing in one line. Q2 keeps it open for orders a role cannot express (dependencies among
  themselves), with no evidence yet that a suite needs one.
- **Found on the way.**
  - F4: the diagram cache hands the report whatever set was fetched first, so a fetch made before the report costs
    the report its own options, roles included. BDDfy's processor is not such a fetch: it runs at process exit,
    after the report. R1 fixes it by having the report draw its own set (Q7).
  - F7: a test with no pure caller draws every caller without a category as an `actor`, though the code's comment
    says only the first. It was read from the code, in no measured diagram, and goes with R2.
  - F6, outside #228: the diagrams Kronikol puts into BDDfy's own `BDDfy.html` are images whose address is empty
    under the default renderer (`BrowserJs`). So that page has shown no diagram by default since at least 3.0.0,
    and no test reads it. It has nothing to do with order and is in neither R1 nor R2: Q6 asks whether it gets its
    own patch.

Seven questions for the owner are in §9, each with a recommendation. Each finding is traced to its fix and its test
in §10.

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
   test and whom it calls is under test (`TestTrackingMessageHandler.cs:249`, `TestClient?.Saw(serviceName)`, read
   by `TestClientRegistry.Observations()`): **observed**.
3. Every call to it carries a `testDouble` mark: a double, **observed**.
4. The callers nobody calls whose calls carry no caller category, or only `User`, and are mostly attributed to the test:
   the test, **inferred** (`InferTests`, `:205-227`).
5. When nothing declared or observed is under test (`:93-100`): the test parties' callees, stores left out by the
   diagram's own palette (`IsStore`, `:250-253`), one that makes calls of its own first, then the most called; a
   second only when it too makes calls and takes a quarter of the tests' calls: under test, **inferred**
   (`InferSubjects`, `:233-248`).

`ServiceId` sets no kind; it names the subject's row (`:102-119`). The wiki states the contract (Report-Configuration,
wiki `7f72c19`, lines 287-291): *"Only a run that declares something writes the table ... The diagrams draw every
party by its name alone ... A run that declares nothing writes the same report as before"*. The #190 plan's own
words are narrower (`PARTY_IDENTITY_PLAN.md` §5.13, lines 785-788): *"An observed or inferred kind draws nothing, so
no suite's diagrams change until it declares a double"*, said of the «test double» stereotype that 5.16.2 removed.
So the promise R1 must keep is the wiki's: nothing changes for a run that declares nothing. No document promises
that a declaring run's order never changes. The wiki's "by its name alone" and 5.16.2's changelog entry ("drawn as a
run that declares nothing draws it") are about a declared double, which R1 does not move; §8.4 adds the order to the
wiki.

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
- R1's rule in full ([`r1_rule.py`](PARTICIPANT_ORDER_PLAN.harness/r1_rule.py): the pure caller, the parties
  resolved `Test`, those resolved `UnderTest`, then the rest), with the run's roles (`caller` the test,
  `breakfastProvider` under test), reorders **none** of the 1,396. Every diagram with arrows has a pure caller, holds
  the subject, and holds no test party but the caller
  ([`results/r1-rule-breakfastprovider-5.14.0.txt`](PARTICIPANT_ORDER_PLAN.harness/results/r1-rule-breakfastprovider-5.14.0.txt)).
  `order_rules.py`'s `RU` moves the subject alone; this script also moves the other test parties and handles a test
  with no pure caller, as §5.1 does.
- Kronikol's own example reports on this machine (Example.Api's LightBDD and ReqNRoll suites on xUnit v3, from an
  earlier run of the example suites) declare nothing. `query parties` infers `Caller` the test and `Dessert Provider`
  under test, and R1 with those roles reorders none of their 7 diagrams with arrows. That is what Q1 (b) would do to
  Kronikol's own examples; 7 diagrams of one service stand for no other suite.

The suite #228 was found in is not on this machine (searched `C:/Code` and the user folders); the repro stands in
for it. The issue was found on 5.28.2 (Kronikol.ReqNRoll.xUnit3 and Kronikol.StepTracking), and the code it links
(`PlantUmlCreator.cs` L1305-1309 and L1373-1424 at `9d994a4b`) is the same rule as `:1312-1460` today.

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
diagrams where the declared subject is not second change. Those are the defect itself, with one exception that
§5.1 names: a party the test calls on its way to the subject that no one declared moves right of it. The issue asked
for the option so that existing reports keep their bytes; Q1 (d) keeps that choice open.

## 4. Rules considered, measured

Each rule was run over the 1,396 diagrams of §2.4 and over the repro. "Reordered" counts diagrams whose declaration
order differs from today's, per lane (in-memory lanes; the external-SUT lanes change under no rule).

| Rule | Repro (#228) | Reordered per lane | Verdict |
|---|---|---:|---|
| R0 today: the first caller nobody calls, then first appearance | `caller, disk, git, app` | 0 | the defect |
| RA callers left of callees (topological over requests, ties by first appearance) | `caller, app, disk, git` | 37-39 | **rejected**: a broker that delivers to the service becomes its caller and moves in front of it (`caller, azureEventHub, breakfastProvider, ...`) |
| RB the action phase first: first appearance among When/Then calls, then the rest | `caller, app, git, disk` | 40-44 (BDD lanes) | **rejected**: reorders the dependencies of a fifth of the BDD diagrams for nothing, and does nothing for a plain test, whose calls carry no phase unless it calls `StartAction()` (only steps and `StartAction()` set one) |
| RC RB's least move: only the test's first callee in the action phase goes second | `caller, app, disk, git` | 0 | works, but a second inference beside the resolver's, and needs a phase |
| RU the party the run resolves as under test goes second (**R1**; R1 in full, §5.1, also moves any other test party forward: `r1_rule.py`) | `caller, app, disk, git` | 0 (RU and R1 in full alike) | **chosen**: the run's own answer, no new rule |
| The issue's `SequenceDiagramParticipantOrder = ["app"]` | `caller, app, disk, git` | 0 (unset) | not recommended (§3, Q2) |
| The issue's "UnderTest after the caller, behind an option" | `caller, app, disk, git` | 0 | R1 without the option (Q1) |

RC is worth keeping in mind: if Q1 chooses to lay out runs that declare nothing, the resolver's inference
(`InferSubjects`) is the rule to use, not RC, so that the table, `query parties`, the service card and the diagram
still agree.

## 5. The design (R1)

### 5.1 The rule

For each test, the participants are declared in three groups, each in the order of its first call, as today:

1. **The test**: the pure caller as today, then any other party the run resolves as `Test` that this test's calls
   carry. The pure caller keeps its shape: the `actor`, unless its calls carry a caller category. With no pure
   caller, every participant keeps the shape today's loop gives it, which can be more than one `actor` (F7); no
   diagram of §2.4 or of the example reports lacks a pure caller. The pure caller stays first even when the run
   resolves it as something else: a broker delivering the message a scenario starts from reads left to right as
   `broker, service, dependencies` (15 diagrams in §2.4), and only R2 touches a pure caller that is under test.
2. **Under test**: every party the run resolves as `UnderTest` that this test's calls carry.
3. **The rest**: everyone else, in the order of the first call, exactly as now.

Shapes, colours, aliases and every arrow and note are unchanged; only the declaration lines move. Each line is the
one today's loop writes for that participant, so R1 is a stable reordering of today's lines and writes none of them
differently (T10). A test whose subject is already second, or whose calls carry no subject, is byte-identical. A
party under test that is the pure caller (F2) stays first in R1; R2 decides its shape.

**A limit, by design.** A party the test calls on its way to the subject, such as a gateway, that the run does not
resolve as `Test` or `UnderTest` is in the rest. It moves right of the subject, and its calls to the subject then
point left: `caller, app, gateway`. That needs a declaring run whose test reaches the subject through such a party
without a tracked test client: a client built by `CreateTestTrackingClient` makes its callee observed under test, so
a gateway reached that way joins the subject in group 2, in the order of first call. No diagram of §2.4 has the
shape. A suite that has it can declare the gateway `UnderTest` too. A rule that kept every caller of the subject in
front of it is RA, which moves the brokers (§4); an explicit order is Q2.

### 5.2 Where the roles come from

- **One resolution per report.** Move `PartiesReport.Resolve` from `ReportGenerator.cs:530` to before `:354`, over the
  calls the report carries, and pass the same result to `:530`'s readers. Those calls are `dataLogs` (`:390`), which
  either branch builds as the run's logs without `TrackingIgnore` (`:363`, `:390`), so they can be computed before
  `:354` from `runLogs` alone. The table, the diagnostics and the diagrams then read one answer. `Resolve` reads
  nothing else that is not settled before `:354`: the options, the features, the parties file (`PartiesFile.Locate`:
  the path the option or `KRONIKOL_PARTIES` names, else the repository's `.kronikol/parties.json`; the rotation
  between `:354` and `:530` moves only the report's own planned files) and the test clients the process built
  (`TestClientRegistry.Observations()`). Its findings are still recorded
  where they are today (`:531-532`), so the report's diagnostics keep their order, and before the snapshot at
  `:546`.
- **Which kinds move a party is Q1.** Recommended (a): only when the run writes the table, that is, declares
  something (`ServiceId`, `Parties` or a parties file), and then every kind the table holds, whatever its source.
  `ServiceId` alone turns it on: the table is written, and the subject is observed or inferred inside it.

### 5.3 How the roles reach the diagram

- `DiagramsFetcherOptions` gets an **internal** `Roles` (the names resolved `Test` and `UnderTest`), set by the
  report generator alone (ingest goes through it, below). Not public: nothing new for a consumer to set.
- `PlantUmlCreator.GetPlantUmlImageTagsPerTestId` is public, and an optional parameter added to it is a binary break.
  The roles reach `CreateEntitiesPlantUml` through an internal overload, or through an ambient scope as
  `DiagramParties` did (§2.3); the overload is preferred, since a scope is invisible at the call site.
- **The process-wide cache.** `DefaultDiagramsFetcher` caches the first diagram set for the life of the process and
  ignores the options of later calls (`DefaultDiagramsFetcher.cs:14-36`). In every wiring the wiki documents, the
  report fetches first. BDDfy runs its batch processors when the process unloads (`AssemblyLoadContext.Default.Unloading`,
  hooked in BDDfy 8.0.1.3's `Engine` static constructor), after the assembly fixture's `DisposeAsync` has written the
  report (`Integration-BDDfy-xUnit3.md:82-138`, the example's `BDDfyTestSetup.cs:57-71`). So the processor's fetch
  (`DiagramEnhancingBatchProcessor.cs:119`) takes the report's set, roles included, and `BDDfy.html` follows R1 with
  nothing to do. The cache still hands a fetch made before the report to the report, which then loses its own
  options, roles and all (F4). That fetch can be a host's own `GetDiagramsFetcher` call (public, and shown on the
  wiki's PlantUML-Browser-Rendering page), or BDDfy's when a suite writes its report after the unload hooks.
  **Recommended:** the report draws its own set every time (`Reset()` before its fetch at `:354`). It then always
  draws with its own options and roles, every later fetch (BDDfy's) still takes the report's set, and no test pins
  the reuse (`ReportsFolderPathTests.cs:75` asks only that a set is cached after the report, which stays true). The
  narrower alternative is a cache that remembers the roles it drew with and draws again for a fetch that carries
  other roles; it fixes the roles and leaves F4 for every other option. P4 pins whichever is chosen.
- **Ingest** needs nothing of its own: it resets the cache, then calls
  `ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment` with options that carry `--parties` and
  `--service-id` (`src/Kronikol/Ingestion/IngestPipeline.cs:687-703`, #190 R2), so the same hoisted resolution draws
  its diagrams. **Merge** never draws again (`MergeableReportMerger.cs:378, 478`): each shard keeps the order its own
  run drew, so a merged report agrees with its shards.
- **The CI summary's diagrams** are drawn after the resolution by `DefaultDiagramsFetcher.GetCiSummaryDiagrams`
  (`ReportGenerator.cs:799`), uncached, with the report's own `fetcherOptions` and through the same
  `GetPlantUmlPerTestId` (`DefaultDiagramsFetcher.cs:257-291`), so they take the roles with nothing more.

### 5.4 Names

The resolver matches the raw `CallerName` and `ServiceName`, ordinal (`PartiesReport.cs:42-44`,
`PartyResolver.cs:57-58`); the diagram keys participants by alias (`SanitizePlantUmlAlias`, `:1498-1504`: the name
camel-cased by `StringCasing.Camelize`, which removes each run of spaces, `_` and `-` and upper-cases the character
after it, then lower-cases the first, and then any character but a letter, a digit or `_` made `_`). So `App` and
`app`, or `breakfast-provider` and `Breakfast Provider`, are one participant, drawn by the first name met. Roles are
looked up by the raw name, then declared by alias: a participant is under test if any raw name with its alias is. A
fact pins that (T7).

### 5.5 What does not change

- A run that declares nothing (under Q1 (a)): every byte of every diagram, in the report, the data file and every
  output.
- Kronikol4J: it has no parties (it mirrors #190's R0 only), so under Q1 (a) its output and the .NET output agree on
  every run Kronikol4J can produce. The ledger records the new rule as not mirrored, with the reason (§8.4).
- The data file, the parties table, the diagnostics and `query parties`: unchanged; the table is the input.
- A host that draws its own diagrams through the public `DefaultDiagramsFetcher.GetDiagramsFetcher` (or
  `LightBddDiagramsFetcher`) and hands them to `GenerateHtmlReport` keeps today's order: `DiagramsFetcherOptions`
  gains no public member, so roles reach only what `CreateStandardReportsWithDiagrams` draws, and through it ingest,
  the CI summary and `BDDfy.html` (§5.3).
- The component diagram keeps its order (callers first seen, then services, `ComponentDiagramGenerator.cs:261-267`).
  Q4 asks whether it should follow.

### 5.6 F2 (R2): the service drawn as the test

In R2, a pure caller the run resolves as anything other than `Test` is declared as a service is, by its category's
shape or a `participant`, never as the `actor`. The `actor` stays the shape of the test. Measured: the five
`actor "Breakfast Provider"` diagrams of §2.4 change, nothing else. Q3.

**F7, the same function.** Only the pure caller's branch sets `actorDefined` (`PlantUmlCreator.cs:1387`). The loop
draws each caller without a category that it meets before that caller is called as `actorDefined ? "participant" :
"actor"` (`:1433`) and never sets the flag, though its comment says *"an actor (the first) or a box"* (`:1432`). So
a test with no pure caller draws every such caller as an `actor`. That was read from the code, not seen: no diagram of
§2.4 or of the example reports lacks a pure caller. The fix sets the flag where the loop draws its actor, so the first
is the only one, in every run, declaring or not. It changes what is drawn, so it goes with R2 (Q3), as a patch.

## 6. Releases

| Release | What | Bump | Gate |
|---|---|---|---|
| R1 | §5.1-5.5: the roles resolved once, before the diagrams; the test's parties, then the parties under test, then the rest; the report draws its own diagrams (F4) | minor recommended (Q5) | Q1, Q5 |
| R2 | §5.6: a party under test is never drawn as the actor, and a test with no pure caller draws one actor (F7) | patch | Q3 |
| (R3) | the issue's explicit order option, only if Q2 says so | minor | Q2 |
| (R4) | outside #228: `BDDfy.html`'s diagrams drawn as pictures under the default renderer (F6), only if Q6 says so | patch | Q6 |

No version number is written until a release is pushed; peers take numbers at push time.

## 7. Tests (red first)

Every fact below is written first and run red on the release's base, then green. Unit facts go in
`tests/Kronikol.Tests/PlantUml/` beside the existing `CreateEntitiesPlantUml` facts. Whole-pipeline facts in
`tests/Kronikol.Tests` join the `DiagramsFetcher` collection (a class that reads the process's call log must,
`ProcessGlobalStoreTests`) and call `DefaultDiagramsFetcher.Reset()` before each report, as
`DeclaredDoubleDiagramTests` does; they tag every party name, since the call log and `TestClientRegistry` hold every
test's calls and clients. Playwright facts take `lock (ReportTestHelper.WholePipeline)` and reset inside it, as
`DeclaredDoubleTests` does.

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
- T9 two test parties, the pure caller and one that is also called (a callback to the test): the pure caller first
  with its `actor`, then the other, then the subject.
- T10 only declarations move: the source with its declaration lines removed is byte-identical to today's, and the
  declaration lines are today's lines, reordered.
- T11 a test with no pure caller, roles given: the test parties, then the subject, then the rest, each line as today.
- T12 (R2, F7) no pure caller, two callers without a category each met before it is called: one `actor`, the first.
  Red today: two.
- T13 §5.1's limit, pinned so that a change to it is deliberate: `app` declared under test, the test calls an
  undeclared `gateway` that calls `app`: `caller, app, gateway`.

**Pipeline (`CreateStandardReportsWithDiagrams`, ingest):**

- P1 `Parties = [new("app") { Kind = PartyKind.UnderTest }]`: the report's diagram source for the scenario declares
  `app` second; the data file's parties block says `underTest`, `declared`.
- P2 `ServiceId` alone, the app observed through `CreateTestTrackingClient`: `app` second.
- P2b `ServiceId` alone, nothing observed, the repro's calls: the inferred subject `app` second, both with the stores'
  category `Database` and with one the palette does not know (`Disk`, which resolves to no store): `app` makes calls
  and the stores do not, so the inference still names `app` alone (`InferSubjects`, `PartyResolver.cs:233-248`).
- P3 nothing declared: every diagram source the page carries equals the same run's on the previous release's rule
  (Q1 (a)).
- P4 a fetch through the public `GetDiagramsFetcher` before the report, with options of its own and no roles, then
  the report: `app` second, and the report's own options apply (`SeparateSetup` on in the report and off in the early
  fetch: the partition is drawn). Then a fetch with no options, as BDDfy's batch processor makes at process exit,
  returns the report's set. The first half is red today (F4).
- P5 `kronikol ingest --parties` with `app` under test: `app` second.
- P6 the CI summary's diagram for the scenario matches the report's.

**Playwright (`tests/Kronikol.Tests.EndToEnd/`):** a report of the repro's scenario with `app` declared under test,
drawn by the default renderer: the drawn `app` box's left edge is left of `disk`'s and `git`'s, read from the SVG
after the diagram draws (`WaitForFunctionAsync` with `PollingInterval = 200`). A control in the same class: nothing
declared, `disk` left of `app`. Light and dark themes are not needed: the layout, not the colour, is under test.

**Mutations, each to be killed by a named fact:** M1 skip the under-test group (T1, P1); M2 look roles up by alias
(T7); M3 the report takes a set fetched before it (P4); M4 apply roles when the run writes no table (P3); M5 order the
subjects by name (T5); M6 decide the order per fragment (T6); M7 resolve after the diagrams again (P1, P2); M8 write a
moved party's line as a caller's (`participant` or `actor`) rather than the line today's loop wrote for it (T10,
T11); M9 (R2) leave the loop's `actorDefined` unset (T12).

## 8. What the change touches

From a read-only sweep of both repositories on 2026-11-07; each line below was re-read by hand.

### 8.1 Code

- `src/Kronikol/PlantUml/PlantUmlCreator.cs`: `CreateEntitiesPlantUml` (`1312-1460`) takes the roles and declares the
  three groups; `CreatePlantUmlPrefix` (`1232-1258`) and both `DiagramBuilder` call sites (`2043-2046`, `2266-2267`)
  pass them; an internal overload of `GetPlantUmlImageTagsPerTestId` (`77-161`) and `CreatePlantUml` (`171-229`)
  carries them. The prefix must stay a pure function of the traces, the options and the roles: a fragment's body is
  cut from the buffer by the prefix's length (`2254`).
- `src/Kronikol/DiagramsFetcherOptions.cs`: the internal `Roles`. `src/Kronikol/DefaultDiagramsFetcher.cs`: passes them
  (`257-291`); under the alternative of §5.3 it also keeps them with the cache (`14-36`).
- `src/Kronikol/Reports/ReportGenerator.cs`: the resolution moves before `:354` with the `dataLogs` expression (`:390`);
  `:530` reads the result and records its findings where it does today; the fetcher options (`321-353`) take the
  roles, which the CI summary (`799`) reads through them; the report resets the cache before its fetch (§5.3).
- Ingest (`src/Kronikol/Ingestion/IngestPipeline.cs:687-703`): no change, since it draws through the report generator.
- `src/Kronikol/Parties/PartiesReport.cs` / `PartyResolver.cs`: a way to read the `Test` and `UnderTest` names off
  the resolved table (no rule changes).
- `src/Kronikol/Parties/PartyKind.cs:12-13`: `UnderTest`'s doc says what it now does to the diagram.
- R2: `PlantUmlCreator.cs:1360-1388` (the pure caller's shape) and `:1433` (F7's flag).
- `ServiceId` alone counts as declaring (`PartyDeclarations.Any`, `PartyResolver.cs:21`), so it is enough to turn R1
  on; the `ServiceId` doc comment says so.

Nothing public is added. Two constraints from the page's readers hold the design: the order is expressed by the
order of the declaration lines, never PlantUML's `order N` (the line-up of a diagram drawn in parts pairs the first
part's declarations with its lifelines left to right, `plantuml-browser-render-script.js:1256-1269, 1484-1510`), and
nothing new may sit between two declarations (`parseDiagramStructure` ends the header at the first line it does not
know, `800-829`).

### 8.2 What reads the order, and is fine

- History fingerprints interactions, never diagram source (`InteractionShape.cs:335, 489, 507`): no `behaviour-changed`.
  Neither history nor the query engine reads a diagram's source at all: only the report writer, the CI summary, merge
  and BDDfy's processor read `CodeBehind`.
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
  in order. Its run declares all three of its parties doubles, so none is resolved `Test` or `UnderTest` (a declared
  name is never inferred, and no subject is inferred among declared names), and R1 moves nothing: it keeps passing
  unchanged. Only its summary's "each draws its participants as a run that declares nothing does" is narrowed to the
  doubles it declares. The Playwright `DeclaredDoubleTests` declares one double and its caller is inferred the test
  and already first, so it keeps passing too.
- No example project declares `ServiceId`, `Parties` or a parties file, so under Q1 (a) no example report changes,
  and no Playwright fact on one.
- R2 only: `extractCallerPayloads` (`context-menu-script.js:643-651`) takes the first `actor` as the test's caller. A
  diagram with no tracked call from the test has no actor after R2, so "Copy all caller request payloads" copies
  nothing there instead of the service's own requests. A Playwright fact pins it.

### 8.4 Kronikol4J and the documentation

- Kronikol4J's `appendParticipants` (`kronikol4j-diagram/.../plantuml/PlantUmlCreator.java:210-303`) has today's rule,
  pinned by `PlantUmlParityTest.pureCallerDeclaredFirst` (`277-283`). Kronikol4J has no parties (its ledger's #190 R1
  entry: not mirrored, as the owner chose), so R1 is a ledger entry in `docs/REMAINING_PARITY.md` (the section at
  `:1878`, on its `origin/main`, `bd47938`), "Not mirrored", with the reason: only a run that declares parties is
  laid out by roles, and a Kronikol4J run cannot declare them. R2's party half likewise. F7's fix needs no parties,
  and Kronikol4J has the same flag (`actorDefined`, set at `:262` only, read at `:286-287`), so it is mirrored there
  with its own red fact, or the ledger says why not.
- Wiki (`Report-Configuration.md`, the Parties section, lines 236-305 on `7f72c19`): the sentence "The diagrams draw
  every party by its name alone" gains the order, with the repro's before and after; the `kindSource` table says which
  kinds move a party (Q1). `Generated-Reports.md`'s sequence diagram description says how participants are ordered.
  The Integration pages that show `ServiceId` and `Parties` link to it.
- `CHANGELOG.md`: what moved, for which runs, the bump and why; the merge note of §8.2.
- `PARTY_IDENTITY_PLAN.md` §5.13 gains a line pointing here, since its "draws nothing" no longer holds for order.

## 9. Questions for the owner

- **Q1. Which runs are laid out by roles?** (a) Only a run that declares something (`ServiceId`, `Parties` or a
  parties file), with every kind its table holds. (b) Every run, inferred roles included. (c) Only a party declared
  `UnderTest`. (d) The issue's: as (a), but behind a new option that is off by default (for example
  `SequenceDiagramSubjectFirst`), so every existing report keeps its bytes until a suite sets it.
  **Recommended: (a).** It keeps the wiki's promise that a run declaring nothing writes the same report, it makes the
  diagram and the table agree wherever there is a table, and one line (`ServiceId`) turns it on. (b) fixes undeclared
  suites too, the issue's among them, and changed nothing on the reference consumer or on Kronikol's own examples
  (§2.4). But it reorders, on the inference's guess, diagrams of suites that declared nothing; it can follow later as
  its own release if wanted. (d) keeps every byte of every existing report, as the issue asked. It costs a public
  option (a minor whatever Q5 says), and its default leaves a declaring run's table and diagram disagreeing. On the
  one declaring consumer measured, (a) changed no diagram, so the bytes (d) protects are, as far as measured, those
  of diagrams that show the defect (and §5.1's limit).
- **Q2. The issue's `SequenceDiagramParticipantOrder`?** **Recommended: not now.** R1 covers the issue's suite with
  the declaration it already has. If an order among dependencies is wanted later, it should name parties through the
  same table (a new `PartyDescriptor` field, `DeclaredPartyNotSeen` for a name no call carries) rather than a second
  list.
- **Q3. R2: draw a service under test that the test never called as a box, not as the test's stick figure?**
  **Recommended: yes**, a patch after R1. It changes five diagrams of the consumer's 1,396 and only in runs that
  declare. It is asked rather than folded in because it changes what the diagram draws, as the «test double»
  stereotype did, which was reversed in 5.16.2. R2 also carries F7's one-line fix (one `actor` when a test has no
  pure caller), which applies to every run; it was read from the code and seen in no diagram measured.
- **Q4. Should the component diagram follow?** **Recommended: no.** It lays out by edges, not by time, and #228 is
  about the sequence diagram.
- **Q5. R1's bump.** **Recommended: minor.** No new public surface, but an existing declaration gains a visible
  effect on a declaring run's diagrams wherever its subject is not already second, and the issue is an enhancement.
  A patch is defensible as a bug fix with a changed behaviour. Q1 (d) makes it a minor in any case.
- **Q6. F6: `BDDfy.html` shows no diagram under the default renderer. Fix it, as its own issue and patch?** It is
  not part of #228: the page takes the report's diagrams (§5.3), so it follows R1 by itself. But under `BrowserJs`,
  the default since at least 3.0.0 (`ReportConfigurationOptions.cs:168`), a diagram's image address is empty
  (`DefaultDiagramsFetcher.cs:360-367`), and the processor writes it into `<img src="">`
  (`DiagramEnhancingBatchProcessor.cs:189`). So each scenario's diagram there is a disclosure with no picture and the
  raw source inside. No test reads the page. **Recommended: yes, as its own issue**, filed on the owner's word, with
  a Playwright fact on the page first. Where the image address is empty, the fix links each scenario to its place in
  the Kronikol report, rather than giving an address on a PlantUML server, which would send every diagram's source
  off the machine.
- **Q7. The report and the diagram cache (F4).** **Recommended: the report draws its own diagrams every time**
  (§5.3). It fixes F4 for every option, not only the roles, and `BDDfy.html` still takes the report's set. A host that
  fetched first through the public `GetDiagramsFetcher` then gets a report drawn with the report's own options
  rather than with its early fetch's, which is what those options say; the changelog says so. The alternative keys
  the cache by the roles alone.

## 10. Findings

| # | Finding | Fix | Fact |
|---|---|---|---|
| F1 | #228: stores the test sets up first are drawn before the service under test | R1 | T1, P1, P2, P5, Playwright |
| F2 | a service under test the test never called is drawn as the test's `actor` (5 of 1,396) | R2 (Q3) | T8 |
| F3 | the diagrams are drawn before the parties are resolved, so the report holds two answers to "who is under test" | R1 §5.2 | P1, M7 |
| F4 | the diagram cache keeps the first set drawn and ignores every later fetch's options, so a report written after another fetch draws with that fetch's options, roles and all. Checked: BDDfy's processor fetches at process exit, after the report, in every documented wiring, and takes the report's set; a host's own early `GetDiagramsFetcher` call, or a report written after the unload hooks, meets it | R1 §5.3 (Q7) | P4, M3 |
| F5 | `SeparateSetup` draws a Setup partition, but the order still comes from the setup calls | R1 (the subject's place no longer depends on which calls came first) | T1 (`separateSetup` on) |
| F6 | outside #228: under the default `BrowserJs` renderer, the diagrams Kronikol puts into `BDDfy.html` are `<img src="">`, so that page shows none; no test reads the page | its own issue and patch (Q6, R4) | a Playwright fact on `BDDfy.html` |
| F7 | a test with no pure caller draws every caller without a category that it meets before that caller is called as an `actor`: the flag is set only for a pure caller (`PlantUmlCreator.cs:1387`). Read from the code, in no measured diagram; Kronikol4J has the same flag | R2 (Q3) | T12, M9 |

## 11. Checked again on 5.31.2: what changed in this plan

The same day, on `55999241`, every citation was read again and each open point was followed up. What changed:

- **R1 measured in full.** The first pass measured `RU`, which moves the subject alone. `r1_rule.py` measures §5.1
  as written, other test parties and a test with no pure caller included: still 0 of 1,396, and 0 of the 7 example
  diagrams under inferred roles (§2.4).
- **F4 was backwards.** BDDfy's batch processor runs at process exit, after the report, so it takes the report's
  diagrams, and `BDDfy.html` follows R1 with nothing to do (§5.3). F4 stands for a fetch made before the report; the
  recommended fix (the report draws its own set) is Q7, and P4 and M3 changed with it. The old Q6 ("leave it as it
  is ... until a parties file declares, which it reads") was wrong twice over: the page takes the report's order,
  and nothing on the drawing path reads a parties file. It is gone.
- **New findings.** F6: `BDDfy.html` shows no diagram under the default renderer (Q6, outside #228). F7: a test with
  no pure caller can draw more than one `actor` (R2).
- **The issue's option is a choice, not a footnote.** §1 says where R1 departs from the issue, and Q1 (d) is the
  issue's option with its cost.
- **A limit of R1**, a gateway nobody declared moving right of the subject, is stated in §5.1 and pinned by T13.
- **Corrections.**
  - Ingest needs no change: it draws through the report generator, and its path is `src/Kronikol/Ingestion/`.
  - The CI summary takes the roles through the fetcher options.
  - `DeclaredDoubleDiagramTests` keeps passing (it declares only doubles), where §8.3 said it would be rewritten.
  - The alias rule camel-cases (§5.4).
  - The #190 plan's sentence quoted in §2.3 ends "until it declares a double" and is about the stereotype.
  - The resolver's findings stay recorded where they are, so the diagnostics keep their order (§5.2).
  - Whole-pipeline facts reset the cache themselves: `ReportTestHelper.WholePipeline` is the Playwright project's
    lock, not a reset.
  - T9 now matches §5.1.
- **Citations moved.** `TestTrackingMessageHandler.cs:249` (moved by 5.31.2); the wiki's Parties section at
  `7f72c19`; Kronikol4J's `appendParticipants` at `:210-303` and its ledger section at `:1878` (`bd47938`). Everything
  else cited was read again on `55999241` and holds.

## 12. Execution

**Go-ahead.** On 2026-11-07 the owner asked to "implement the plan in full". Each question of §9 is taken at its
recommendation: Q1 (a), only a declaring run is laid out by roles; Q2, no explicit order option; Q3, R2 ships; Q4, the
component diagram keeps its order; Q5, R1 is a minor; Q6, F6 gets its own issue and patch (R4); Q7, the report draws its
own diagrams.

### R1 (5.33.0)

- **Code.** `DiagramRoles` (internal, `src/Kronikol/PlantUml/DiagramRoles.cs`) holds the names resolved `Test` and
  `UnderTest`; `PartyTable.DiagramRoles` gives them for a declaring run and null otherwise. `ReportGenerator` resolves the
  parties before its fetcher options are built, sets the internal `DiagramsFetcherOptions.Roles`, resets the cache and
  fetches; the findings are still recorded where they were. The roles reach `CreateEntitiesPlantUml` through
  `PlantUmlCreator.GetPlantUmlImageTagsPerTestIdCore` (internal; the public method forwards to it with none), `CreatePlantUml`,
  `DiagramBuilder` and `CreatePlantUmlPrefix`. `CreateEntitiesPlantUml` writes every line as before and remembers where
  each participant's line sits, then, given roles, sorts the lines stably: the pure caller, the test's parties, the
  parties under test, the rest.
- **Facts.** `tests/Kronikol.Tests/PlantUml/ParticipantOrderTests.cs` (T1 to T11, T13; T12 is R2's) and
  `tests/Kronikol.Tests/Parties/ParticipantOrderPipelineTests.cs` (P1, P2, P2b twice, P3 to P6, and P7: a host's own
  fetch keeps today's order, since the roles are the report's to give), and the Playwright
  `tests/Kronikol.Tests.EndToEnd/ParticipantOrderTests.cs` (the app's drawn name left of the stores in a declaring run, a
  control that declares nothing).
- **Red before R1** (the previous release's code, with a stub for the new internal names that ignores the roles): on
  5.32.0, 17 of the 22 core facts then written fail, each at its order assertion (T10 at its premise that the source
  changes), and T2, T3, T4, T8 and P3, which pin what does not change, pass, as they should; P7, added later, is of the
  second kind. On 5.32.1 the Playwright fact fails (the app drawn at x = 341.7, the disk at 123.0) and its control
  passes. `r1/red.txt`.
- **Corrected on the way.** P4's first check for the Setup partition read `Contains("partition")`, which every
  sequence diagram's style block satisfies (`sequenceDiagram { partition { ... } }`), so on 5.32.0 it passed and P4 failed
  only at the order. It now matches the partition's own line, and on 5.32.0 P4 fails there: F4 reproduced on its own.
- **Mutations** (`r1/mutate.py`, `r1/mutations.txt`): M1 to M8 each caught; M3 by P4 alone, M4 by P3 alone.

### R2 (5.34.1)

- **Code.** `CreateEntitiesPlantUml` reads the roles' raw names before it writes any line. In a run with roles, a pure
  caller the run holds as under test is written as a service is, a `participant` (its category's shape when it has one,
  as before), so the actor is left to the test (F2). The loop's caller without a category is the actor only while none
  has been drawn, and it now marks one drawn (F7); in a run with roles, a party held as under test is never it. The pure
  caller still marks the actor's place as taken whatever its shape, so a broker-first diagram is unchanged.
- **Decided on the way.** §5.6 said a pure caller held as "anything other than `Test`" loses the actor; §6, Q3 and the
  measurement say a party under test. The wider reading failed two facts the moment it ran: T2 (a declaring run whose
  table holds no test and no subject lost its actor, so it no longer drew as a run that declares nothing) and
  `DeclaredDoubleDiagramTests` (its run declares the scenario's first caller a double, which then lost its actor). R2
  takes the narrower rule: only a party held as under test is kept from the actor, so F2 is fixed and a stub or a real
  upstream that starts a scenario keeps the stick figure (T8c, M12). With no pure caller, F7's fix alone would make the
  first caller the loop meets the actor, which in a declaring run can be the service under test, F2 by another path: in
  a run with roles the loop skips a party under test (T14, M11).
- **Measured.** Over the 1,412 diagrams kept from §2.4 and the example reports: 5 draw the service under test as the
  actor (all `actor "Breakfast Provider"`, F2) and none draws more than one actor (F7), so R2 changes those 5 alone.
- **Facts.** T8 pins the participant, T8b a pure caller with a category, T8c a pure caller held as neither, T11 the test
  party as the actor, T12 one actor with no pure caller (F7), T14 the actor kept from the party under test; T8, T11, T12
  and T14 fail on 5.34.0 (`r2/red.txt`). A Playwright fact: a declaring run's diagram whose test made no tracked call
  does not offer "Copy all caller request payloads" (on 5.34.0 it offered the app's own request), and a control does.
- **Mutations** (`r2/mutate.py`, `r2/mutations.txt`): M9 to M12, each caught.
- **Kronikol4J.** F7 mirrored (`actorDefined` set where the loop draws its actor), with
  `PlantUmlParityTest.oneActorWithNoPureCaller`, red before the fix; F2 not mirrored (no parties).

## Appendix A. Harness

- `repro/issue_shape.cs`: the issue's scenario through the real `PlantUmlCreator` (`dotnet run --file`).
- `order_rules.py`: the rules of §4 over diagram sources (`python -I order_rules.py [--under-test alias] [--list] DIR...`).
- `r1_rule.py`: R1's rule in full, §5.1, with the roles given as aliases
  (`python -I r1_rule.py --test caller --under-test breakfastProvider [--list] DIR...`).
- `leftward.py`: every request that points left under today's order, counted by pair.
- `results/`: the outputs quoted here. The consumer's reports and the diagram sources are not committed (7 lanes,
  14 MB); `README.md` says how to fetch them again.
