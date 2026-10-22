# Each Reqnroll outline row named as Gherkin names its pickle (#101)

**Date:** 2026-10-22 · **Repo version:** 5.4.0 (`main` at `eabec2bd`; line numbers are for that commit) ·
**Status:** 📝 **Draft, not green-lit.** Written in a worktree of its own (`Kronikol-101`, branch
`plan/101-outline-row-names`) while other sessions worked in theirs. Nothing outside `plans/` changes.

Evidence labels: **RUN** (measured on 2026-10-22 against Kronikol 5.4.0 from nuget.org, Reqnroll 3.3.4 and Gherkin's own
compiler, with the scripts and verbatim answers in [`OUTLINE_ROW_NAMES_PLAN.harness/`](OUTLINE_ROW_NAMES_PLAN.harness/README.md)),
**READ** (in the source and tests at `eabec2bd`, the wiki at `247881f`, or Kronikol4J), **INFERRED** (reasoned from two
facts, stated by neither), **ESTIMATED** (not measured; the step that would measure it is named).

The owner's issue, [#101](https://github.com/lemonlion/Kronikol/issues/101) (2026-09-28, filed against 3.31.9), abridged:

> Gherkin lets a Scenario Outline's title use the same `<column>` placeholders as its steps, and Gherkin's own compiler
> fills them in for each Examples row. `Scenario Outline: A basket is weighed on the UK scale (<case>)` compiles to one
> pickle per row, named `A basket is weighed on the UK scale (empty basket)`, `… (one apple)` and `… (two apples)`.
>
> The Reqnroll lane names every row with the unfilled title instead. All three rows are called `A basket is weighed on
> the UK scale (<case>)`, which is also their `outlineId`. So wherever a row is listed on its own, such as a failure's
> heading or the timeline, the placeholder shows as written and the rows can't be told apart. The Cucumber lane already
> gets this right: it names each row from its pickle.
>
> **Ask:** Give each Reqnroll outline row the name Gherkin gives its pickle. Fill the title's `<header>` placeholders
> from the row's Examples values, as `PickleCompiler.Interpolate` does, and keep `OutlineId` as the raw title so the rows
> still group. Two things the fix has to settle:
>
> - **Stable ids.** `ScenarioStableId.Compute` hashes the display name. An expanded name would give every row whose
>   title has a placeholder a new id, once, and the history ledger would treat those rows as new scenarios. The example
>   values already tell the rows apart, so hashing the raw title would keep today's ids.
> - **Titles without placeholders** keep their names byte for byte, so no other scenario's name or id changes.
>
> #102 is a separate defect in the same area: an outline's grouped heading goes through the test-method humaniser.
> Neither depends on the other.

## TL;DR

- **The defect is still there in 5.4.0** (RUN, section 2.2). On a probe suite of 13 outlines and one plain scenario,
  all 29 outline rows are named with the title as written, and `Failures.md` heads the failing row
  `Baskets › A basket is weighed on the UK scale (<case>)`. The same run's Cucumber messages, ingested, name every row
  from its pickle (RUN, 2.3), so the two Gherkin lanes disagree on one run of one feature file.
- **Kronikol already holds Gherkin's own name for each row.** Reqnroll 3.3.4 embeds the feature's pickles in its
  generated code, and `ExamplesBlockResolver` already finds the running row's pickle by reflection to name its
  `Examples:` block (READ, 2.5). The pickle's `Name` is what Gherkin 35.0.0 compiled. R1 takes it from there.
- **Gherkin's rule is the fallback.** The pickles vanish without an error when reflection-based System.Text.Json is off
  (`PublishAot`, `PublishTrimmed`) or the resource is stripped (RUN, 2.5). Then the name is built from `ScenarioInfo.Title`
  and `ScenarioInfo.Arguments` by Gherkin's rule: one ordinal replace per column, in the order the columns were
  written, each on the result of the last. On the probe that rule gives the pickle's own name for 29 of 29 rows (RUN),
  and a single pass over the title does not (row E02: `x then x`, not `<b> then x`).
- **Today's ids are kept, and so is today's order.** The 15 places that compute a stable id from a scenario all hash
  its `DisplayName` (READ, 2.6). Today a Reqnroll row's id is `Compute(suite, feature, title, title, values)` (RUN:
  30 of 30 ids reproduced), and hashing the filled name would give 27 of the 29 rows a new id (RUN). A new id would make
  each row a new test to the history, and a run whose new ids pass a tenth of the suite is recorded as partial
  (READ, 2.6). Five writers also sort scenarios by `DisplayName`, so filled names would reorder an outline's rows and
  interleave its `Examples:` blocks (READ). R1 marks the rows whose name Kronikol filled (`KeyedByOutline`, internal),
  and one helper makes each of those ids and orders read the outline's title for a marked row, exactly the
  string they read today. The data file carries the mark (`"keyedByOutline": true`), so `kronikol merge` and the
  history import compute the same ids.
- **What a reader sees changes, nothing else does.** The failure heading, the timeline, failure clusters, the CI and
  console summaries, `Specifications.md`, the CTRF `name`, `kronikol query` and the test identity a captured call
  carries all show `A basket is weighed on the UK scale (two apples)`. The outline's grouped heading, its value table,
  every stable id, every `sN` address, every anchor and every order stay byte for byte. A title without a placeholder
  that matches a column changes nothing at all, so BreakfastProvider's 13 outlines, the repository's own and every
  other adapter's rows are untouched (READ, 2.6).
- **One patch, R1.** A bug fix with behaviour changes called out in the changelog. No public member is added; the data
  file gains one optional key (a Kronikol4J ledger line, "Nothing to port"). #102, the outline heading's humaniser,
  stays separate, as the issue says.
- **Six questions** (section 11); the first, keep the ids or let them move once, is the one with a cost either way.

## 0. Findings

| # | Finding | Evidence |
|---|---|---|
| F1 | 5.4.0 names all 29 outline rows of the probe with the raw title; the failure heading reads `(<case>)` | RUN, 2.2 |
| F2 | The same run's Cucumber messages, ingested by 5.4.0, name all 29 rows from their pickles | RUN, 2.3 |
| F3 | `ExamplesBlockResolver` already reads each row's pickle; its `Name` is Gherkin 35.0.0's filled name | READ, RUN, 2.5 |
| F4 | The embedded pickles vanish silently under `PublishAot`/`PublishTrimmed` or a stripped resource | RUN (research/reqnroll.md) |
| F5 | Gherkin's rule is sequential and ordinal; a one-pass substitution gives a different name for some rows | READ, RUN, 2.4 |
| F6 | `ScenarioStableId.Compute` is called at 17 sites: 15 from the `Scenario` record's `DisplayName`, two in imports of other tools' files; never in an adapter | READ, 2.6 |
| F7 | Today's Reqnroll ids are `Compute(suite, feature, title, title, values)`: 30 of 30 reproduced | RUN, 2.6 |
| F8 | Hashing the filled name moves 27 of 29 probe rows' ids; the history then counts them new and can mark runs partial | RUN, READ, 2.6 |
| F9 | After the fix a Reqnroll row and a Cucumber row have the same data shape, so no rule read off the data can keep both lanes' ids: a mark is needed | INFERRED from F2, F7 |
| F10 | The page, the three Specifications data writers and `Specifications.md` sort by `DisplayName`; the adapter sorts by the title | READ, 2.6 |
| F11 | `CapitaliseTitles` (on by default) re-cases `DisplayName` and `OutlineId` in place before any id is computed | READ, 2.6 |
| F12 | The test identity `(Title, id)` is set in `BeforeScenario`; calls join by id, the name is a label in headers, OTLP and popups | READ, 2.6 |
| F13 | The source line and the `Examples:` fallback match on the raw `ScenarioInfo.Title` and must keep it | READ, 2.5 |
| F14 | `ExampleDisplayName` cannot carry the name: any member that has one switches the group to the Fallback table | READ, 2.6 |
| F15 | Re-run lines are built from the stack frame, never the name, so they do not change | READ, RUN |
| F16 | No test or fixture has a placeholder in an outline title, and no test runs the Reqnroll `AfterScenario` hook | READ, 2.7 |
| F17 | Found on the way: a wrong doc comment on the Cucumber pickle name, and the Cucumber lane keeps the rightmost of two same-named columns where Gherkin takes the leftmost | READ, section 8 |

## 1. How far each claim of the issue was checked

| Claim | Checked | How |
|---|---|---|
| Every Reqnroll row is named with the unfilled title, which is also its `outlineId` | Yes, on 5.4.0 | RUN, probe P1 (2.2) |
| `exampleDisplayName` is null on every row | Yes | READ: no adapter sets it (2.6) |
| `Failures.md` heads the row with the placeholder and adds an `Example row:` line | Yes | RUN, `results/before-5.4.0/Failures.md` |
| The timeline lists the rows with the placeholder | From the code | READ, `ReportGenerator.cs:2258-2261` builds the entry from `DisplayName`; not opened (the report is never opened) |
| The Cucumber lane names each row from its pickle | Yes, on the same run | RUN, probe P2 (2.3); READ `CucumberFeatureSynthesizer.cs:489` |
| Gherkin's rule is `Interpolate`, one replace per header, an unmatched placeholder left as written | Yes, Gherkin 35.0.0 and 42.0.1 | READ and RUN (research/gherkin-cucumber.md 1.2-1.8) |
| The row's values are read before `ExampleValueGrouper` restructures them, keyed by the Examples headers | Yes | READ `ReqNRollTrackingHooks.cs:169-181`; RUN: generated code keys `Arguments` by the header as written (research/reqnroll.md) |
| An expanded name gives such rows a new id once | Yes | RUN, 27 of 29 (2.6) |
| Hashing the raw title keeps today's ids | Yes | RUN, 30 of 30 (2.6) |
| Titles without placeholders keep their names byte for byte | Yes, by construction | RUN: rows E01 fill to their title |
| `ScenarioStableId`'s doc already calls the input the expanded name | Yes | READ `ScenarioStableId.cs:27` |
| 3.27.2 behaves the same | Not re-run | 5.4.0 shows it (RUN), and 5.4.0 is what R1 changes |

## 2. What the code does today

### 2.1 Where a Reqnroll row gets its name (READ)

- `ReqNRollTrackingHooks.BeforeScenario` sets the test identity to `(ScenarioInfo.Title, guid)`
  (`src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs:55`).
- `AfterScenario` reads `ScenarioInfo.Arguments` into `flatValues` (`:169-177`), builds the structured `ExampleValues`
  with `ExampleValueGrouper` (`:181`, which can re-key them), and collects `ScenarioTitle = ScenarioInfo.Title` and
  `OutlineId = ScenarioInfo.Title` for a row (`:195-220`).
- `ScenarioInfoEnumerableExtensions` sorts a feature's rows by happy path, `ScenarioTitle`, `Examples:` block index and
  the joined values (`:29-37`), and makes each `Scenario` with `DisplayName = x.ScenarioTitle` (`:45`).
- There are Reqnroll packages for xUnit v2, xUnit v3 and TUnit, all through `Kronikol.ReqNRoll.Core`; none for NUnit
  or MSTest (research/name-readers.md B2). Kronikol pins Reqnroll 3.3.4 and consumers get `>= 3.3.4`
  (`Kronikol.ReqNRoll.Core.csproj:12`; research/reqnroll.md 1).

### 2.2 The repro on 5.4.0 (RUN, probe P1)

`probe/` holds a Reqnroll 3.3.4 + xUnit 2.9.3 suite on `Kronikol.ReqNRoll.xUnit2` 5.4.0 from nuget.org:
`Baskets.feature` is the issue's outline verbatim (its third row fails on purpose), `Edges.feature` thirteen more
(E01-E13: no placeholder, a value naming a later column, a placeholder with no column, a header with a space, a
non-ASCII header and a C# keyword, two named and tagged blocks, two rows filling to one name, escaped and empty cells,
markup, a plain `Scenario:` with `<x>` in its title, a rule, a placeholder written twice, a title that starts with a
placeholder, a 260-character value). Read back with the report's own `query.cs`, never by opening a report:

```
s0    pass      0s     0 calls  A basket is weighed on the UK scale (<case>)
s1    pass      0s     0 calls  A basket is weighed on the UK scale (<case>)
s2    FAIL   0.35s     0 calls  A basket is weighed on the UK scale (<case>)
s3    pass      0s     0 calls  <fruit> starts E12
...
s28   pass      0s     0 calls  E13 <v>
```

`Failures.md`: `### 1. Baskets › A basket is weighed on the UK scale (<case>)`, then `` `s2` · stableId
`48092cbd5fd324ec` ``, `written at Features/Baskets.feature:3`, `Example row: case=two apples, count=2, grams=301`, and
`` Re-run: `dotnet test --filter "FullyQualifiedName~Probe101.xUnit2.Features.BasketsFeature.ABasketIsWeighedOnTheUKScaleCase"` ``,
built from the method, not the name (F15). Verbatim answers: `results/before-5.4.0/`.

### 2.3 The Cucumber lane on the same run (RUN, probe P2)

The probe's `reqnroll.json` turns on Reqnroll's `message` formatter, so the run also writes Cucumber messages. Ingested
with `kronikol ingest --cucumber-messages` (5.4.0), the same 30 scenarios are named from their pickles:
`A basket is weighed on the UK scale (two apples)`, `E02 x then x, a value that names a later column`,
`E03 weighs 1 on <missing>`, `E04 the apple in klein and A`, `E07 escapes []`, `E07 escapes [pipe | bar]`, a name
holding a line feed (`E07 escapes [line` / `break]`), `E11 1 and 1 again`, `Apple starts E12` (ingest capitalises a
title's first letter, as a Reqnroll report's `CapitaliseTitles` does), and `E09 a plain scenario that writes <x> in its
title` unchanged. `results/cucumber-lane-5.4.0/`. The two lanes' ids differ today for every row (2.6).

### 2.4 Gherkin's rule (READ, RUN)

From research/gherkin-cucumber.md, which quotes the sources: Reqnroll 3.3.4 compiles its pickles with Gherkin 35.0.0's
own `PickleCompiler`, whose `Interpolate` is the same text at 42.0.1 and at the commit the issue cites. For each header
cell, left to right, `name = name.Replace("<" + header + ">", value)` on the string the last pass returned:

- ordinal and case-sensitive; every occurrence of a placeholder is filled (E11);
- a placeholder with no column stays as written (E03);
- a value holding a later column's placeholder is filled again, an earlier or its own is not (E02: `| <b> | x |` gives
  `x then x`);
- an empty value removes the placeholder (E07); a title of placeholders whose values are all empty compiles to `""`,
  and the Cucumber lane then shows the raw title (`CucumberFeatureSynthesizer.cs:489`);
- the parser trims header and cell text and unescapes `\|`, `\\` and `\n` before `Interpolate` sees them, so a name can
  hold a pipe and a line feed (E07);
- only an outline's rows are filled: a plain `Scenario:` keeps `<x>` (E09), and so does a `Scenario Outline:` with no
  `Examples:`, which Reqnroll compiles as a plain scenario.

Reqnroll refuses at generation what would make the rule ambiguous for it: duplicate headers in a block, blocks of one
outline with different or reordered headers, a block with no rows (research/reqnroll.md 3; gherkin-cucumber.md 1.6).
So within one outline the columns, and their order, are the same for every row.

### 2.5 What Reqnroll gives Kronikol at run time (READ, RUN)

- Generated code: one `argumentsOfScenario.Add("<header as written>", value)` per column, in header order, then
  `new ScenarioInfo("<raw title>", ..., argumentsOfScenario, featureTags, pickleIndex)`; each feature class builds
  `FeatureLevelCucumberMessages("Features/X.feature.ndjson", ...)` (research/reqnroll.md 2, for xUnit v2, v3 and TUnit).
  `Arguments` holds every column with the parser's decoded values (RUN, 13 of 13 rows).
- `ExamplesBlockResolver` reads the internal `FeatureInfo.FeatureCucumberMessages` and `ScenarioInfo.PickleId` /
  `PickleIdIndex` by reflection (`ExamplesBlockResolver.cs:38-40`), finds the row's pickle (`:116-135`; `PickleId` is
  set only when a formatter is on, so a default run takes `PickleIdIndex`), and accepts it only when the pickle's row
  cells equal the argument values (`:95-97`). A reflection-contract fact holds the members
  (`ExamplesBlockResolverTests.cs:197`).
- No public member gives the expanded name. No setting turns the embedded messages off, but they are gone without an
  error when the resource is stripped or reflection-based System.Text.Json is off: the agent's first `dotnet run --file`
  app, which defaults to `PublishAot`, saw no pickles at all (research/reqnroll.md 4). A test project with
  `PublishAot` or `PublishTrimmed` set would lose them the same way.
- Two lookups match on the raw title and must keep it: the declaration line (`ResolveSource`, `:74`, keyed at
  `:183-184`) and the `Examples:` value-match fallback (`:106`).

### 2.6 Who reads the name, and who keys on it (READ, RUN)

research/name-readers.md lists every reader with its line. What decides the design:

- **Ids.** `ScenarioStableId.Compute(suite, feature, displayName, outlineId, values)` hashes
  `{suite}::{feature}::{outlineId}::{displayName}::{values}` (`ScenarioStableId.cs:33-53`). It is called at 17 sites.
  Fifteen read the `Scenario` record: `ReportGenerator.cs:2258, 2495, 2973, 3493, 3669, 3776, 5355, 5943, 6235`,
  `CtrfReportGenerator.cs:179`, `FailuresDigestGenerator.cs:267`, `LabsReportGenerator.cs:113`,
  `MergeableReportMerger.cs:372` (the merge's identity), `HistoryRunBuilder.cs:58` and `RunSummaryConsoleWriter.cs:148`.
  The other two import another tool's file (`HistoryCommand.Maintenance.cs:547`, a CTRF file, which prefers the
  `extra.stableId` Kronikol wrote; `:613`, Allure) and have no `Scenario`. RUN (`probe/ids-check.cs`): under the
  probe's suite `Probe101.xUnit2`, `Compute(suite, feature, title, outlineId: title, values)` reproduces all 30 ids
  5.4.0 wrote (none without the suite); with the filled name, 27 of the 29 outline rows get a new id (E01's two rows
  fill to their title). The basket rows: `69eac833973c4e19`, `72500d3179f76468`, `48092cbd5fd324ec` today;
  `2e0156c2fba7e468`, `d00aec85a59a0f77`, `877942b57bc51e42` if the filled name were hashed.
- **What a moved id costs** (READ): the history counts each such row new and its old id gone; a run in which more than
  `HistoryPartialThreshold` (0.10 by default, `ReportConfigurationOptions.cs:829`) of the previous run's ids are missing
  is recorded as partial (`HistoryAnalyzer.cs:209-217`); quarantine is looked up by id (`HistoryAnalyzer.cs:625`);
  rename suggestions match feature and name together, so they find nothing (`HistoryCommand.Maintenance.cs:301-311`);
  `query diff`, `--baseline`, cross-run `--body`, saved `sid:` addresses and `#sid-` links all go by id.
- **Order.** The page sorts a feature's scenarios by `DisplayName` (`ReportGenerator.cs:2329`), then
  `ParameterGrouper.BuildGroup` keeps the blocks together by a stable sort on the block index (`ParameterGrouper.cs:88-94`);
  `Specifications.yml`, `.json` and `.xml` sort by `DisplayName` (`ReportGenerator.cs:6560, 6608, 6642`) and so does
  `Specifications.md` (`SpecificationsMarkdownGenerator.cs:103-108`). Today an outline's rows share one name, so these
  sorts leave them in the adapter's order; with filled names they would sort the rows by name, within a block on the
  page and across blocks in the Specifications files. The adapter's own sort (`ScenarioInfoEnumerableExtensions.cs:29-37`)
  sets the data file's order, and so every `sN` address and the history roster's order.
- **Capitalisation.** `CapitaliseTitles` (default `true`, `ReportConfigurationOptions.cs:677`, whose doc says the id is
  computed from the capitalised title) re-cases `DisplayName` and `OutlineId` in place (`StepText.cs:150-171`,
  `ReportGenerator.cs:254`) before any id is computed. A row whose title starts with a placeholder is newly capitalised
  (`Apple starts E12`); with the option off, the lower-case-titles diagnostic can count it.
- **Anchors.** Each scenario's in-page slug is made from `DisplayName`, with `-2`, `-3` for repeats
  (`ReportGenerator.cs:2133-2153`); the rows of an outline take `...-case`, `-2`, `-3` today and the group the next
  (research/name-readers.md A7). `#scenario-` links are documented as unstable (`report-url-hash-function.js:1-5`).
- **The test identity** (`ReqNRollTrackingHooks.cs:55`): calls join by its id (`DefaultDiagramsFetcher.cs:164`,
  `PlantUmlCreator.cs:114`, `HistoryRunBuilder.cs:75-77`); the name rides in the `test-tracking-current-test-name` header
  (`TestTrackingMessageHandler.cs:235`, encoded by `TrackingHeaderValue`), the OTLP `kronikol.test.name` attribute
  (`OtlpSpanMapper.cs:219-221`) and the component diagram's popups (`ComponentFlowSegmentBuilder.cs:84-87`), and the
  public `CurrentTestInfo.Fetcher` returns it to user code.
- **`ExampleDisplayName`** is set by no adapter; any group member that has one makes the group use the Fallback table,
  which drops the value columns (`ParameterGrouper.cs:119-121`).
- **Display only**, each of which shows the filled name after the fix: the timeline (`ReportGenerator.cs:2261`), failure
  clusters (`:2192`), a scenario's summary and Copy scenario name (`:2477, 2502`), the Fallback table's Test Case cell
  (`:3822`), the History tab (`HistoryHtml.cs:136`), the labs page, the data files' `name`, `Failures.md` and
  `Failures.jsonl`, the CI summary, the console summary, the CTRF `name`, `kronikol query` (`summary`, `scenarios`,
  `failures`, `repro`, `history`, `diff`) and the skill's `query.py`. Search and `--grep` match the filled text; the
  group still matches `<case>` through its heading. No page script sorts or groups scenarios by name.

### 2.7 The tests today (READ)

`tests/Kronikol.Tests/ReqNRoll/` has `ExamplesBlockResolverTests` (a fake `IFeatureLevelCucumberMessages` with a
Gherkin document and pickles, and real `FeatureInfo` and `ScenarioInfo` objects), `ReqNRollScenarioInfoExtensionsTests`
(its ordering facts use the title `Movement`, with no placeholder, `:604-633`), `ExampleValueGrouperTests` and the TUnit
converter's tests. `FullPipelineParameterizedE2ETests` runs the xUnit v3 Reqnroll example through `dotnet test` and reads
its page in Playwright (`:415+`). No feature file, ndjson or test anywhere has a placeholder in an outline title, and no
test runs the `AfterScenario` hook itself (research/name-readers.md E).

## 3. Design

### 3.1 What was considered

| Option | Names | Ids and order | Cost | Verdict |
|---|---|---|---|---|
| A. Fill the name; key and order marked rows by the outline title | the pickle's | unchanged, byte for byte | an internal mark, one helper at 15 id sites and 5 sorts, one optional data key | **Recommended** |
| B. Fill the name; let the ids follow it | the pickle's | 27 of 29 probe rows re-keyed once; rows reorder unless the sorts change anyway | changelog; every history, baseline and quarantine entry of those rows starts again | Q1's alternative |
| C. Keep `DisplayName` raw; fill at each reader | the pickle's, where a reader remembers | unchanged | every display reader above (twenty-odd) must call one function, and a missed one shows the raw title; the data file's `name` stays wrong | Rejected |
| D. Carry the filled name in `ExampleDisplayName` | the pickle's, in some views | unchanged | every group turns to the Fallback table (F14) | Rejected |
| E. Change `Compute` to hash the outline title for every outline row | the pickle's | Reqnroll's kept; every Cucumber row with a placeholder, and every xUnit, NUnit, MSTest and TUnit theory row, re-keyed (their `OutlineId` is the method name) | worse than B for other users | Rejected |

A rule read off the data alone (hash `OutlineId` when the name equals the title filled from the values) is E for the
Cucumber lane: after the fix its rows look exactly like Reqnroll's (F9). So A needs the mark.

### 3.2 The row's name (R1)

`ExamplesBlockResolver.ResolveRowName(FeatureInfo, ScenarioInfo)` (internal, beside `Resolve` and `ResolveSource`, same
cache, same never-throw contract):

1. With `Arguments` empty (a plain scenario), the name is `ScenarioInfo.Title`.
2. When `FindPickle` finds the row's pickle and its row cells equal the argument values (the check `ResolveCore` already
   makes), the name is `pickle.Name`, unless it is empty, in which case step 3 decides (the Cucumber lane's own fallback
   is the raw title, `CucumberFeatureSynthesizer.cs:489`; step 3 gives the same for an empty fill).
3. Otherwise Gherkin's rule over `ScenarioInfo.Title` and `Arguments` in their order: for each entry,
   `name = name.Replace("<" + key + ">", value, StringComparison.Ordinal)`; an empty result gives the title.

The hooks compute it once, in `BeforeScenario`, keep it in the scenario context, use it for the test identity (Q2) and
pass it to the collector as an internal `ReqNRollScenarioInfo.RowName`. `ScenarioTitle` stays the raw title: it is the
adapter's sort key and the two lookups' key (F13). The converter sets `DisplayName = RowName ?? ScenarioTitle`.

### 3.3 One key for identity and order (R1)

- `Scenario.KeyedByOutline` (internal, `Kronikol`; `Kronikol.ReqNRoll.Core` gains `InternalsVisibleTo`, as the xUnit
  adapters did for `ScenarioTitleResolver.FormatXunitDisplayName`): true when the converter's `DisplayName` differs from
  the row's `OutlineId`. A row whose title has no placeholder that matched a column is not marked, so it is exactly what
  5.4.0 wrote.
- `ScenarioStableId.KeyName(Scenario)` (internal): `OutlineId` when `KeyedByOutline` and `OutlineId` is set, else
  `DisplayName`. It reads `OutlineId` at the moment of use, so `CapitaliseTitles`' re-casing (F11) reaches the key the
  same way it reaches today's `DisplayName`; a copied raw-title string would miss it and move the id of every row whose
  title starts with a lower-case letter.
- `ScenarioStableId.For(suite, featureName, scenario)` (internal) = `Compute(suite, featureName, KeyName(s), s.OutlineId,
  s.ExampleValues)`. The 15 sites that hold a `Scenario` call it; the two imports keep `Compute`, and its public
  signature and rule do not change.
- The five sorts in 2.6 order by `KeyName` in place of `DisplayName`. The anchor slugs use `KeyName` too (Q3), so the
  rows keep `...-case`, `-2`, `-3`.
- `MergeableReportMerger`'s identity uses `For`, so a 5.4.0 shard (raw name, no mark) and a newer shard (filled name,
  mark) of the same row are one scenario, as two 5.4.0 shards are today.

### 3.4 The data file (R1)

`"keyedByOutline": true` on a marked scenario only (`ReportGenerator.cs:5378` area), declared optional in the schema
(`:7394` area), read by `MergeableReportReader` (`:261-267` area) into the internal property. The XML and YAML files
write `StableId` through `For` and need no flag, since nothing reads them back to compute an id. A data file a 5.4.0
tool reads (an old `kronikol merge` on new shards) ignores the key and hashes the filled name: the merged ids then differ
from the shards' (section 10).

### 3.5 The test identity (R1, Q2)

Recommended: the identity takes the filled name too, so a component diagram popup and the diagnostic report list the
row by the name its scenario shows. It is set in `BeforeScenario`, before any step, so every call carries one name. It
changes what `CurrentTestInfo.Fetcher` returns to user code and what a service sees in the
`test-tracking-current-test-name` header; `TrackingHeaderValue.Encode` already carries non-ASCII text and line feeds
(a fact checks the E07 line feed). The changelog says so.

### 3.6 What does not change

The outline's grouped heading (still `FormatScenarioDisplayName(OutlineId)`, #102), its value table, `OutlineId`,
`ExampleValues`, `ExampleRawValues`, `ExampleFlatValues`, the `Examples:` block, the source line, every stable id, every
`sN` address, the data file's scenario order, the page's and the Specifications files' order, every in-page anchor, the
re-run line, any scenario whose title has no filled placeholder, and every other adapter's rows.

### 3.7 What a reader sees

| Where | 5.4.0 | After R1 |
|---|---|---|
| `Failures.md` heading | `Baskets › A basket is weighed on the UK scale (<case>)` | `Baskets › A basket is weighed on the UK scale (two apples)` |
| Timeline entry | `16ms  A basket is weighed on the UK scale (<case>)` | `16ms  A basket is weighed on the UK scale (empty basket)` |
| `kronikol query scenarios` | three rows of `(<case>)` | `(empty basket)`, `(one apple)`, `(two apples)` |
| Group heading on the page | `A basket is weighed on the uk scale [<case>]` (#102) | unchanged (#102) |
| `stableId` | `48092cbd5fd324ec` | `48092cbd5fd324ec` |
| A title starting with a placeholder | `<fruit> starts E12` | `Apple starts E12` (capitalised, as the Cucumber lane shows it) |
| The test identity in a component popup | the raw title | the filled name (Q2) |

### 3.8 Kronikol4J

Nothing to port (research/gherkin-cucumber.md 3): the port has no Reqnroll adapter, and its one Gherkin lane names a
row `testCase.getName()`, the pickle's name, filled by Cucumber-JVM's Gherkin under the same rule; its ids are computed
by its own scheme, which records no outline. The ledger line (draft in that file, 3.3) names the new optional data key,
which the port never writes.

### 3.9 Relation to #102, #76 and #77

- **#102** (open): the grouped heading goes through the humaniser, and a title ending in `.` stops both HTML reports.
  R1 does not touch `ParameterGrouper`'s heading. Next to filled row names the humanised heading reads worse, which is
  an argument for doing #102 soon, not for folding it into R1: the issue keeps them apart, and a fix nobody asked for
  in this release is scope the owner has to notice. Either can land first; they share no line.
- **#76** (`query scenarios --json` without `sourceFile`/`sourceLine`) and **#77** (the runner's test-case identity):
  unaffected. #77's selection-to-filter verb would want the filled name and the method together, which R1 provides.

## 4. Tests, in the order they are written

All facts are written first and run red on 5.4.0 (copied into a worktree at the release tag, with internal helpers
stubbed where a fact needs one), then green.

### 4.1 The name (unit, `tests/Kronikol.Tests/ReqNRoll/OutlineRowNameTests.cs`)

- N1 A row whose pickle is found is named `pickle.Name`; `OutlineId` stays the title (the issue's basket).
- N2 No messages (`FeatureInfo` without them): Gherkin's rule over `Title` and `Arguments`.
- N3 A pickle index pointing at another row's pickle: the rule, never the other row's name.
- N4 Theory: the rule equals Gherkin 35.0.0's own `PickleCompiler` (a dependency of Reqnroll, so in the test project's
  graph) over the probe's outlines and research/gherkin-cucumber.md's, E02's later-column value among them.
- N5 A title with no placeholder, or none that matches a column (E03 keeps `<missing>`): the name is the title byte for
  byte and the row is not marked.
- N6 A plain scenario with `<x>` in its title is untouched; an empty fill gives the title.
- N7 The hooks: `BeforeScenario` and `AfterScenario` on real `ScenarioContext` objects (no test runs them today, F16)
  give the identity and the collected row one name.

### 4.2 Keys and order (unit, `tests/Kronikol.Tests/Reports/OutlineRowKeyTests.cs`)

- K1 A marked row's id is `Compute(suite, feature, title, title, values)` at every one of the 15 sites: the data file,
  the page's `sid-` anchors and timeline, `Failures.md`, `Failures.jsonl`, CTRF `extra.stableId`, the console summary,
  the labs page, the history roster, XML and YAML. One fact per site, so a site missed by the helper fails by name.
- K2 An unmarked row whose name differs from its `OutlineId` (the Cucumber shape) keeps hashing its name.
- K3 `CapitaliseTitles` on, a raw title starting with a lower-case letter: the id equals 5.4.0's.
- K4 `kronikol merge` of a shard written by the new code gives the shard's ids; a 5.4.0 shard and a new shard of the same
  row merge as one scenario; the data file round-trips `keyedByOutline` and the schema accepts it.
- K5 Order: an outline whose filled names sort differently from its rows' order, in two blocks, keeps 5.4.0's order in
  the data file, on the page, in `Specifications.yml`, `.json`, `.xml` and `.md`.
- K6 Anchors: the rows keep `...-case`, `-2`, `-3` and the group its slug (Q3).
- K7 The source line and the `Examples:` block resolve for a placeholder row, the block by value match when the index is
  wrong (F13).

### 4.3 Readers (unit)

- D1 `Failures.md` heads the row with the filled name and keeps its `Example row:` line; `Failures.jsonl` likewise.
- D2 `kronikol query scenarios`, `failures` and `summary` print the filled names; `--grep two` finds the row.
- D3 CTRF `name`, the CI summary and the console summary carry the filled name.
- D4 The identity header carries the filled name, encoded, for E07's line feed (Q2).

### 4.4 Playwright (`tests/Kronikol.Tests.EndToEnd/`)

- W1 The timeline lists the three basket rows by their filled names (the issue's second view).
- W2 The search finds a row by its filled text and the group by `<case>`.
- W3 Copy scenario name on a row copies the filled name; the group's copies its heading (unchanged).
- W4 Full pipeline: a new feature file with a placeholder title in the xUnit v3 Reqnroll example, read through
  `FullPipelineParameterizedE2ETests` (check first which facts count that example's scenarios).

### 4.5 Mutations

Each must turn a named fact red: the pickle taken without the row check (N3); a one-pass substitution (N4, E02); a
case-insensitive replace (N4); no fallback (N2); the mark never set (K1); the mark set for every outline row (K2); the
key copied as a string (K3); the reader dropping the key (K4); each of the five sorts left on `DisplayName` (K5); the
adapter sorting by `RowName` (K5); the slug from `DisplayName` (K6); `ResolveSource` given the filled name (K7); the
identity left raw (D4); one of the 15 sites left on `Compute` (K1).

### 4.6 Proofs before the tag

The core suite, the full Playwright suite (announced to the peers first), a Release build of `release.slnf`, and
`probe/run.sh <work> after <checkout>` with `ids-check.cs` pointed at its results: every id equal to
`results/before-5.4.0/ids.tsv`, every name equal to the Cucumber lane's.

## 5. Release and bump

One release, **R1, a patch**: bug fixes, with nothing new to call or configure. The internal members and the
`InternalsVisibleTo` add no public surface; the data key is report output, which CLAUDE.md files under the bump of the
change itself. The changelog calls out each behaviour change: the row's name in every listed reader, the test identity's
name (Q2), and the data file's new optional key. If Q1 takes B, it also says that those rows' ids change once.

## 6. Documentation

- Wiki: the "Scenario Outlines" sections of `Integration-ReqNRoll-xUnit2.md` (424-440), `-xUnit3.md` (407-424) and
  `-TUnit.md` (406-422), whose own example is `Creating a <flavour> cake`, gain the rule and the kept id; the
  `stableId` row of `Generated-Reports.md` (317) and `Querying-Reports.md` (203); `API-Reference.md` (26) if it describes
  the input; `Integration-Cucumber-Messages.md` (47) for parity (line numbers at wiki `247881f`).
- Doc comments: `ScenarioStableId.Compute`'s `scenarioDisplayName` (`:27`) keeps its contract and points at `For` for
  Reqnroll rows; `ReportGenerator.cs:3774-3775` ("every row of an outline shares one display name") is no longer true;
  `ReqNRollScenarioInfo.ScenarioTitle` says it is the title as written.
- `CHANGELOG.md`, and the Kronikol4J ledger line after publication.

## 7. Where it sits in the roadmap

No row yet. At green light it takes the next stage-1 bug row by "taking 1.N now", as the other issue plans did.

## 8. Found on the way, not in this plan

- `CucumberMessages.cs:278` documents a pickle's name as "the outline name, unexpanded"; Gherkin fills it, and
  `CucumberFeatureSynthesizer.cs:489` relies on that. A doc-comment fix, separate.
- The Cucumber lane's `ExampleValues` keep the rightmost of two same-named columns (`GherkinIndex.cs:123-124`), where
  Gherkin's name takes the leftmost. Reqnroll refuses such tables; cucumber-js and playwright-bdd were not checked.
- `ExamplesBlockResolver`'s line lookup keeps the last of two scenarios of one title (`:184`); Reqnroll refuses two
  scenarios of one name in a feature (research/gherkin-cucumber.md 1.6), so it cannot arise in this lane.
- Quarantine does not follow history rename aliases (`HistoryAnalyzer.cs:625`).
- The page's and the Specifications writers' `ThenBy(x => x.DisplayName)` use the culture's comparer, not an ordinal
  one; R1 keeps the comparer as it is, and the order question is the owner's (as 4.14.10 left the DisplayName sort).

## 9. What is not known

- Whether Reqnroll 3.4 or 4 keeps `FeatureCucumberMessages`, `PickleIdIndex` and the pickle shape: the
  reflection-contract fact says so the day it changes, and the fallback names the row meanwhile (ESTIMATED: no newer
  Reqnroll was in the NuGet cache).
- How the identity name change reads in a real component diagram (W-level check in R1's acceptance).
- The share of a real suite's rows with placeholder titles: BreakfastProvider's 13 outlines have none (research/
  name-readers.md E), so it is a control, not a measure; the issue's "larger suite" was not named.

## 10. Risks and traps

- A missed id site re-keys rows in one output only (say CTRF) and nowhere else: K1 has one fact per site for that.
- An old tool on a new data file: a 5.4.0 `kronikol merge` ignores the key and hashes the filled name; say it in the
  changelog, as mixed-version merges are documented to mismatch already (`MergeableReportMerger.cs:484`).
- Tests run before the version bump see 5.4.x stamps; nothing here keys on the version, so the order does not matter.
- The probe's file apps need `#:property PublishAot=false`, or System.Text.Json reflection is off (the very trap F4).
- Bash collapses doubled backslashes: the harness scripts are files, never heredocs, so `\n` and `\|` survive.

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Keep today's ids and order (A), or let the filled name re-key the rows once (B)? | **A**: the issue asks for it, and B makes the history count the rows new, can mark the first runs partial and drops their quarantine and baselines |
| Q2 | Does the test identity (headers, OTLP, popups, `CurrentTestInfo`) take the filled name? | **Yes**: one name per row in every view; the changelog names the public `CurrentTestInfo.Fetcher` |
| Q3 | Do the rows' in-page anchors keep today's slugs? | **Yes**: no link changes, and `#scenario-` links are documented as unstable anyway |
| Q4 | The data key's name | `keyedByOutline` |
| Q5 | #102 in the same release? | **No**: separate, as the issue says; worth doing next |
| Q6 | The two found-on-the-way items in section 8 | Separate patches, after R1 |

## Appendix A. Edit sites

| File | Change |
|---|---|
| `src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs` | `ResolveRowName` (3.2) beside `Resolve` and `ResolveSource` |
| `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs` | `:55` identity name; `:195-220` `RowName` |
| `src/Kronikol.ReqNRoll.Core/ReqNRollScenarioInfo.cs` | internal `RowName`; doc of `ScenarioTitle` |
| `src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs` | `:45` `DisplayName`, the mark; sort stays on `ScenarioTitle` |
| `src/Kronikol/Kronikol.csproj` | `InternalsVisibleTo Kronikol.ReqNRoll.Core` |
| `src/Kronikol/Reports/Scenario.cs` | internal `KeyedByOutline` |
| `src/Kronikol/Reports/ScenarioStableId.cs` | internal `KeyName`, `For`; doc |
| The 15 id sites that hold a `Scenario` (2.6) | `For` |
| `ReportGenerator.cs:2329, 6560, 6608, 6642`; `SpecificationsMarkdownGenerator.cs:103-108` | sort by `KeyName` |
| `ReportGenerator.cs:2133-2153` | slugs from `KeyName` (Q3) |
| `ReportGenerator.cs:5378`, `:7394`; `MergeableReportReader.cs:261-267` | the data key, the schema, the reader |
| `ReportGenerator.cs:3774-3775` | stale comment |

## Appendix B. The harness

[`OUTLINE_ROW_NAMES_PLAN.harness/README.md`](OUTLINE_ROW_NAMES_PLAN.harness/README.md): `probe/` (the suite, `run.sh`,
`tsv.py`, `ids-check.cs`, `results/`) and `research/` (`reqnroll.md`, `name-readers.md`, `gherkin-cucumber.md`, each
written by a read-only research agent and checked by hand where this plan cites it).

## Log

- 2026-10-22: written at the owner's ask ("a detailed well researched plan to fix issue 101") by session kronikol-0b
  in worktree `Kronikol-101`, from `main` at 5.4.0 (`eabec2bd`). Three read-only research agents mapped Reqnroll 3.3.4
  (generated code, the embedded pickles, the cases it refuses), every reader of a scenario's name, and Gherkin's rule
  with the Cucumber lane and Kronikol4J; their citations used here were re-read by hand. The probe ran on Kronikol
  5.4.0 from nuget.org; the Cucumber lane on the same run's messages; `ids-check.cs` reproduced every id. Not committed.
