# Tags: one list in place of labels and categories (v5)

**Date:** 2026-10-19, reviewed the same day (§11) · **Repo version:** 4.30.0 (`origin/main` at `aa311cd7`) · **Status:
executed on `v5`** (the owner's ask of 2026-10-19, in full, with §9's recommendations; T1 to T4 by 2026-10-20,
§11; the wiki publishes with 5.0.0, and the Kronikol4J port waits on its branch).

**Line numbers.**
- **Plain** ones are for 4.27.0, `6f6043e2`. Of the files cited, 4.27.1 to 4.30.0 changed these, so T1 re-takes the
  lines it edits there:
  - `ReportGenerator.cs`, `FeatureSynthesizer.cs`, `CucumberFeatureSynthesizer.cs`, LightBDD's
    `FeatureResultExtensions.cs`;
  - `VerbTable.cs`, `QueryCommand.cs`, `QueryCommand.Overview.cs`, `ReportScanner.cs`, `ReportIndex.cs`;
  - `MergeableReportReader.cs`, `MergeableReportRenderer.cs`, `CtrfCommand.cs`.
- **`v5:`** ones are for the local `v5` branch at `a91d4b91` (OPTION_C_PLAN S10c). They hold at `05be4469` (S11b),
  whose five commits changed none of the source files cited.
- **Wiki** ones are for `origin/master` at `23edeca`.

The owner's asks, 2026-10-19:

> Is there any actual reason to keep these distinct (other than not having conflicts in rare cases with LightBDD between
> tags and ScenarioCategories, which we could maybe get around by putting a suffix or something on them in those very rare
> cases?). At the very least, in the UI it seems ridiculous to have them separated.

> ok, can you create a detailed, well researched plan to implement this (for v5)

> [on the 4.x patch the first draft proposed for the defects found on the way] No, just include them in the plan as
> part of v5

**Set by the owner on 2026-10-19:**
1. Labels and categories stop being two things. A scenario and a feature each carry one list of tags, and the report
   has one Tags row.
2. The change ships in 5.0.0 from the `v5` branch.
3. So do the fixes for the defects found on the way (§8). There is no 4.x patch for them.
4. The plan is implemented in full, with §9's recommendations: "Can you implement the plan in full, make sure you dont
   interfere with the other sessions though".

The rest of this plan was written as recommendations, and §9 lists the questions they answer.

Evidence is marked as in `REPORT_FORMAT_PLAN.md`: **RUN** (executed here, with the command or script named), **READ**
(read in the code or the docs at the commit above), **INFERRED** (reasoned from what was read, not executed) and **WEB**.
None of this plan's claims is WEB.

## 0. Summary

Kronikol keeps three lists where a test framework keeps one: `Feature.Labels`, `Scenario.Labels` and
`Scenario.Categories` (`src/Kronikol/Reports/Feature.cs:12`, `Scenario.cs:40-41`). The only difference the report makes
between a label and a category is that a category gets a filter button and a label gets a badge. Search and
`kronikol query scenarios --label` already treat them as one set.

The split came from LightBDD, which has `[Label]` for ticket numbers and `[ScenarioCategory]` for grouping, and was
copied into the shared model in v2.0.0-beta (§2.5). In practice it does not hold:

- Six of the eight in-process adapters cannot produce a category at all (§2.2). The wiki says four of them can
  (`Tags-and-Attributes.md:30-41`), and the JSON schema says the same (`ReportGenerator.cs:6578`).
- The reference consumer, BreakfastProvider, uses no category anywhere, across six frameworks (§2.6).
- On the `v5` branch the word "tag" now means three different sets: the Tags row shows labels and Happy Path, `@tag`
  searches labels and categories, and a feature's labels appear only as badges and free text (F2).

The plan:

1. **Model.** `Feature.Tags` and `Scenario.Tags` replace the three lists. A scenario's *effective* tags are its
   feature's tags plus its own (§4.2).
2. **One classifier.** `ScenarioTags.Classify` serves every tag-based lane. `@category:smoke` keeps working and
   becomes the tag `smoke` (§4.4).
3. **Every adapter fills it** from its framework's own tagging: NUnit `[Category]`, MSTest `[TestCategory]`, xUnit
   `[Trait("Category", …)]`, TUnit `[Category]`, BDDfy tags, LightBDD `[Label]` and `[ScenarioCategory]`, and Gherkin
   tags. Class-level tags go on the feature where the framework says which level a tag came from (§4.7).
4. **One Tags row in the report**, with the Categories row and its script deleted. One `data-tags` attribute, safe for
   any character. Feature tags filter their scenarios, and `@tag` searches exactly what the row shows (§4.8 to §4.10).
5. **One key in every output.** `tags` in JSON, YAML, XML, the schemas, Specifications and CTRF. Readers still accept
   `labels` and `categories` from older files (§4.12, §4.13).
6. **The query tool** gets `--tag` and a `tags` field (§4.13).

The work is four commits on `v5` (T1 to T4, §6), and they also fix the defects §8 lists, at the owner's decision.
Nothing ships on `main` unless Q1 is answered that way:
- The 4.x reader that REPORT_FORMAT_PLAN shipped as 4.30.0 parses a 5.0.0 file and skips the `tags` key it does not
  know.
- So a 4.x tool shows a 5.0.0 report untagged, while history, which never reads tags, works as before.

## 1. How far each claim was checked

| Area | How | Result |
|---|---|---|
| The model, the classifier, every writer and reader on `main` | READ, a full sweep of `src/` for `Labels`, `Categories`, `data-labels`, `data-categories` and the JSON, YAML and XML keys | Appendix A.1 to A.4 |
| Each adapter's access to its framework's tags | READ, plus each package's XML docs in the local NuGet cache, at the versions the projects pin (NUnit 4.6.0, MSTest 4.2.2, xUnit 2.9.3 and 3.2.2, TUnit 1.43.11, BDDfy 8.0.1.3, LightBDD 3.12.0 and 3.12.1, Reqnroll 3.3.4) | §2.2, §4.7 |
| LightBDD's two attributes | RUN, reflection over `LightBDD.Framework.dll` 3.12.1: `[Label]` and `[ScenarioCategory]` are both valid on `Class, Method`, `AllowMultiple = true`. The doc comments are quoted in §2.5 | §2.5 |
| Where NUnit 4.6.0 puts a `[TestCase]` method's own attributes | RUN, `TAGS_PLAN.harness/nunit-testcase-probe/`: NUnit's own `DefaultTestAssemblyBuilder` builds a fixture with a plain test and a `[TestCase]` method, and the probe prints each node's properties, `Parent` and `AllCategories()` | F14 |
| The APIs §4.7 names | READ, the package XML docs and binaries: NUnit 4.6.0 `TestAdapter.Parent`, `.Properties`, `.AllCategories()`, `.PropertyHierarchy()`, `TestCaseAttribute.Category`, `TestFixtureAttribute.Category`; MSTest 4.2.2 `TestContext` has no category member; xUnit 2.9.3 has `Xunit.Sdk.TraitHelper` (`xunit.execution.*.dll`); Kronikol's xUnit markers use the trait keys `Happy Path` and `Endpoint` | §4.7 |
| What a 4.x tool does with a 5.0.0 file | READ at `aa311cd7`: `ReportGate.cs:45-49` refuses only a `formatVersion` the tool does not know; `ReportScanner.cs:905-906` reads keys by name and skips the rest | Q1 |
| The `v5` report | READ through `git show v5:` | §2.3, Appendix A.5 |
| Real use | READ, a grep of BreakfastProvider's source (`C:/Code/BreakfastProvider`) for `[Label`, `[ScenarioCategory`, `@category:`, `WithTags`, `[Category`, `[TestCategory` and `[Trait`, and of this repository's `examples/` | §2.6 |
| Line numbers | RUN, a token check of each line cited in §2, §3 and §8 against `git show 6f6043e2:<file>` (and `a91d4b91` for `v5:`). The review checked 35 more from §4, §5 and Appendix A the same way, and re-took every wiki line at `23edeca`. Appendix A's other lines come from read-only sweeps | T1 re-takes the ones it edits |

## 2. Where it stands today

### 2.1 The model and the classifier

- `Feature.Labels` (`Feature.cs:12`), `Scenario.Labels` and `Scenario.Categories` (`Scenario.cs:40-41`), all
  `string[]?`. A feature has no categories.
- `ScenarioTags.Classify` (`ScenarioTags.cs:36-73`) is written for "an external producer" and splits raw tags as
  follows:
  - `category:x` becomes a category `x`;
  - the first `endpoint:x` becomes the feature's endpoint;
  - `happy-path`, `happy_path` and `happypath` set the happy-path flag (`HappyPathDetection.cs:13-18`);
  - anything else becomes a label.

  Its only caller is the tests-NDJSON ingest (`FeatureSynthesizer.cs:180`). ReqNRoll and the Cucumber importer repeat
  the same rules with constants of their own (`ReqNRollConstants.cs:11-12`, `CucumberFeatureSynthesizer.cs:167-168`).

### 2.2 What each lane fills

| Lane | Feature labels | Scenario labels | Scenario categories | Happy path from | Endpoint from |
|---|---|---|---|---|---|
| LightBDD (`Kronikol.LightBDD.Core/FeatureResultExtensions.cs`) | the class's `[Label]` (`:29`, `:39`) | the method's `[Label]` (`:47`, `:92`), "Happy Path" kept in it | `[ScenarioCategory]` on the method and on the declaring type (`:48`, `:93`) | `labels.Contains("Happy Path")`, ordinal and case-sensitive (`:91`) | none |
| ReqNRoll (`Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs`) | the first scenario's `CombinedTags` minus that scenario's own tags (`:22-27`) | the scenario's own tags minus the conventions (`:42-46`) | `category:` values from `CombinedTags`, so feature-level ones are inherited (`:48-51`) | the scenario's own tags | the first scenario's `CombinedTags` (`:18-20`) |
| BDDfy (`Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs`) | none | every tag but happy-path and `endpoint:` (`:28-31`); `category:x` stays a label | none | `HappyPathDetection` | the first `endpoint:` tag in the story (`:17-20`) |
| xUnit v2, xUnit v3, NUnit 4, MSTest, TUnit | none | none | none | a Kronikol attribute (trait, property or category) | a Kronikol attribute |
| Tests NDJSON (`Ingestion/FeatureSynthesizer.cs`) | labels every scenario shares, compared case-insensitively (`SharedLabels`, `:258`, `:279-293`) | `Classify` labels plus a generated `retry N` per earlier attempt (`:182`) | `Classify` categories (`:211`) | `Classify` | `Classify` |
| Cucumber Messages (`Ingestion/Cucumber/CucumberFeatureSynthesizer.cs`) | the feature node's tags (`:1133-1147`) | the scenario's and its Examples block's own tags, plus `retry {n}` (`:692-701`) | `category:` values from the pickle's tags, which include feature, rule and examples tags (`:703-706`) | own tags (`:720`) | pickle tags |

None of the five adapters in the fourth row has a test of its tag mapping, and NUnit 4, xUnit v3 and TUnit have no
adapter mapping tests at all (READ, `tests/`).

### 2.3 What the report does with them

On `main` (`ReportGenerator.cs`):

- **Categories row** (`:1805-1822`): an All button, one button per category, an Uncategorized button and an OR/AND
  switch seeded from `ReportToggleDefaults.CategoryFilterMode` (`:1315`). The script is
  `report-category-filter-function.js`.
- **Label badges**: on the feature (`:2047`) and on the scenario (`:2201-2205`). A category is never drawn on its
  scenario.
- **Attributes**: `data-categories` and `data-labels` (`:2192-2198`), both the scenario's own values joined with a
  comma.
- **Free-text search** includes feature labels, categories and labels (`:2179-2181`). **`@tag` search** reads
  `data-categories` and `data-labels` only (`report-search-function.js:57-60`).

On `v5` (OPTION_C_PLAN S5 and S6):

- **Tags row** (`v5:ReportGenerator.cs:1842-1855`): the scenario's labels, with Happy Path pinned first (`TagsOf`,
  `:1843`); the Happy paths segments drive that chip (D8). Chips cycle off, has and has-not. Its ANY/ALL mode is
  hard-coded to OR (`:1848`).
- **Categories row** (`:1870-1887`): the same chip component, seeded from `CategoryFilterMode`.
- **Two scripts**: `report-filter-chips-script.js` for tags, and a rewritten `report-category-filter-function.js` for
  categories.
- **The hash** carries `tags`, `tagx`, `tagmode`, `cats`, `catx`, `catmode`, `hp` and `hpx`
  (`v5:report-url-hash-function.js:79-91`, `:130-170`).
- **`@tag` search** still reads labels and categories (`v5:report-search-function.js:146-155`).

### 2.4 Outputs and readers

- **Writers.** Labels and categories are written to:
  - `TestRunReport.json` and the mergeable JSON (`:4774`, `:4790-4791`);
  - `TestRunReport.yml` (`:5614`, `:5647-5648`) and `TestRunReport.xml` (`:5321`, `:5340-5341`);
  - `Specifications.yml`, `.json` and `.xml` (`:5916-5943`, `:5979-5985`, `:6013-6020`) and `Specifications.md`
    (`SpecificationsMarkdownGenerator.cs:57`, `:70-71`);
  - the JSON schema, where feature `labels` and scenario `labels` and `categories` are required (`:6547`, `:6562`), and
    the XSD (`:7096-7108`, `:7192-7197`);
  - `ctrf-report.json`, where `tags` holds the labels and categories travel in `extra.categories`
    (`CtrfReportGenerator.cs:179-201`, `:287`).
- **Readers.**
  - The query index (`Query/ReportScanner.cs:882-883`, `:710-714`; `ReportIndex.cs:161-162`).
  - `scenarios --json` (`QueryCommand.Overview.cs:276-277`) and `--label`, a case-insensitive substring match over
    labels, categories and feature labels (`:303-305`).
  - The merge reader (`Merge/MergeableReportReader.cs:109`, `:234-235`).
  - `kronikol ctrf` (`Kronikol.Tool/CtrfCommand.cs:147`, `:153`).
- **History** reads neither field (`History/HistoryRunBuilder.cs:82-158`). A scenario's stable id does not use them.

### 2.5 Why there are two

- **LightBDD's two attributes.** LightBDD's own docs give them two purposes (READ, `LightBDD.Framework.xml` 3.12.1):
  - `LabelAttribute`: "May be used to link feature/scenario with external tools by storing ticket number."
  - `ScenarioCategoryAttribute`: "May be used to associate scenarios with specific categories."
- **Kronikol copied them across.** v2.0.0-beta (`55321028`, 2026-04-07) folded LightBDD's report into the shared one.
  Its plan, `docs/plan-unify-lightbdd-features.md` (deleted in `4a2b0b3f`; `git show 55321028:<path>`), has two
  decisions:
  - DD-5 mapped `Info.Labels` to `Labels` and `Info.Categories` to `Categories`, one to one.
  - DD-8 added the category filter, "Consistent with LightBDD's UX".
- **The prefix came later.** `6d680ddb` (2026-04-15) gave ReqNRoll the `category:` prefix, since a Gherkin tag cannot
  otherwise say which kind it is.
- **TOOLBAR_REDESIGN_PLAN §2.4** designed one chip component for "three rows" (Tags, Dependencies, Categories) without
  revisiting the split, and `v5` built it.
- **The only reason the code gives** is the comment on `CtrfReportGeneratorTests.Categories_travel_in_extra_rather_than_pretending_to_be_tags`
  (`tests/Kronikol.Tests/Reports/CtrfReportGeneratorTests.cs:220-223`): "A Kronikol category comes from an @category:
  tag and is a different axis from a label." The owner's direction of 2026-10-19 retires that premise.

### 2.6 How much they are used

- **BreakfastProvider.** Its six test suites have no `[Label]`, no `[ScenarioCategory]`, no `@category:` and no
  `WithTags` (READ, grep of its source).
  - **Gherkin tags:** `@happy-path` 45 times, then `@IgnoreIfExternalSut` 15, `@SkipUnlessFakesControllable` 13,
    `@IgnoreIfNeedsDirectDbAccess` 5, `@IgnoreIfNeedsEventInfrastructure` 2, and `@MenuDownstreamFailure`,
    `@MenuCaching`, `@MenuAvailability` and `@IgnoreUnlessInMemoryDb` once each. That is eight tags besides Happy Path.
    TOOLBAR_REDESIGN_PLAN counted the same on its report: 0 categories and "real 9 tags"
    (`TOOLBAR_REDESIGN_PLAN.md:101`, `:208`).
  - **Unread attributes:** its xUnit `[Trait("Produces", "openapi.json")]` and NUnit
    `[Category("Produces: openapi.json")]` attributes (three each) reach the report as nothing.
  - **Its code** uses none of the members this plan removes or renames: `Labels`, `Categories`,
    `CategoryFilterMode` and `ScenarioTags` (READ). Its 5.0.0 upgrade changes only what its reports and its committed
    `docs/Specifications.yml` contain.
- **This repository's fixtures.** `categories` appears 0 times in 1,349 fixture scenarios
  (`VERIFICATION_MECHANISMS_PLAN.md:47`, `:99`).
- **This repository's examples** carry no tag besides Kronikol's own conventions: `@endpoint:` five times and
  `@happy-path` four (READ, `examples/**/*.feature`). The only `@category:` tags in the repository are in a test
  fixture, `tests/Kronikol.Tests/TestData/Cucumber/kronikol-demo.feature:1`, `:23`, `:41`.
- **The Option C preview's report had no Categories row** (`v5:plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md:909-910`).

So one merged row would hold a handful of chips on every report seen. §4.8 still sizes the row for a suite that tags
every scenario with a ticket number, the use LightBDD documents for `[Label]`.

## 3. Findings

- **F1. Five of the eight adapters make no tags at all, and the docs say four of them make categories.**
  - xUnit v2, xUnit v3, NUnit 4, MSTest and TUnit set neither `Labels` nor `Categories` (READ, §2.2). BDDfy sets labels
    only. LightBDD and ReqNRoll are the only adapters that set categories.
  - `Tags-and-Attributes.md:36-40` says categories come from xUnit `[Trait("Category", "smoke")]`, NUnit
    `[Category("smoke")]`, MSTest `[TestCategory("smoke")]`, BDDfy `.WithTags("category:smoke")` and LightBDD
    `[Label("smoke")]`. The first four reach the report as nothing (BDDfy's as the label `category:smoke`), and the
    fifth makes a label.
  - The schema's description of `categories` says the same: "the framework's category attribute otherwise"
    (`ReportGenerator.cs:6578`).
- **F2. On `v5`, "tag" means three sets.**
  - The Tags row shows labels plus Happy Path (`v5:ReportGenerator.cs:1843`).
  - `@tag` searches labels plus categories, without Happy Path (`v5:report-search-function.js:146-155`).
  - A feature's labels are badges and free text only (`v5:ReportGenerator.cs:2160`, `:2288`).

  The Option C inventory recorded the first two disagreeing (`v5:plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md:251-253`).
- **F3. A feature's tags never filter, and `@` cannot find them.**
  - `data-labels` carries the scenario's own labels only (`:2192-2198`; `v5:ReportGenerator.cs:2305-2307`), so a
    feature label is in no chip and no `@tag` set.
  - CTRF never exports them (`CtrfReportGenerator.cs:179`).
  - Free-text search and `query --label` do include them (`:2179`, `QueryCommand.Overview.cs:304`), so the same
    report answers "which scenarios are tagged X" three ways.
- **F4. A tag containing a comma is split in two.**
  - The C# side joins with a comma (`ReportGenerator.cs:2193`, `:2197`, `:2981`, `:2983`), and the scripts split on it:
    `report-category-filter-function.js:44`, `report-search-function.js:59-60`, `report-search-index.js:472-473` and
    `report-url-hash-function.js:83`, `:157`.
  - Effects: the chip `a,b` never matches its own scenarios, so choosing it hides everything; `@a` matches it; and a
    link turns it into the two chips `a` and `b`.
  - LightBDD labels and categories, the tests NDJSON and Gherkin all allow a comma in a tag. A Gherkin tag ends only at
    whitespace (INFERRED from the Gherkin grammar).
- **F5. A tag containing a double quote breaks a shared link.**
  - The hash reader builds `'.category-toggle[data-category="' + c + '"]'` from the link's text
    (`report-url-hash-function.js:158`).
  - A `"`, `]` or `\` in the name makes `querySelector` throw. The category filter is then never applied, and the
    scenario the link points at is never revealed, since that reveal comes last (READ, `:154-165`).
- **F6. Case gives two chips and one search.**
  - The row is built with an ordinal `Distinct` (`ReportGenerator.cs:1809`), and the chip filter compares exactly
    (`report-category-filter-function.js:44-53`).
  - `@tag` lowercases both sides (`report-search-function.js:56-60`).
  - So "Smoke" and "smoke" are two chips that each match their own spelling, while `@smoke` matches both.
- **F7. The chips' order depends on the machine's culture.** `.OrderBy(c => c)` (`ReportGenerator.cs:1810`) uses the
  current culture, and Kronikol4J sorts ordinally (an audit note in the `LONG_COMPONENT_EDGE_PLAN` row,
  `ROADMAP.md:1007`).
- **F8. A tag with a space cannot be searched.**
  - An `@` token ends at whitespace, a bracket or a quote (`advanced-search.js:85-97`). Happy Path and the generated
    `retry 2` are such tags.
  - TOOLBAR_REDESIGN_PLAN §2.5 asked for quoted `@"tag names"`, and Option C did not build it
    (`v5:plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md:338`).
- **F9. ReqNRoll takes a feature's labels from its first scenario.**
  - `Feature.Labels` is the first scenario's `CombinedTags` minus that scenario's own tags
    (`ScenarioInfoEnumerableExtensions.cs:22-27`). So:
    - that scenario's rule tags become the whole feature's;
    - a feature tag the first scenario repeats is lost.
  - `FeatureInfo.Tags` and `RuleInfo.Tags` exist in Reqnroll 3.3.4 and are not read (READ, the package XML docs).
- **F10. The Cucumber importer drops a plain rule tag.**
  - A scenario's own tags leave out feature and rule tags by design (`GherkinIndex.cs:15-17`).
  - Of the pickle's tags, which include them, only `category:` and `endpoint:` are read
    (`CucumberFeatureSynthesizer.cs:703-711`).
  - So `@slow` on a `Rule:` reaches the report as nothing.
- **F11. Happy Path is three different things.**
  - **LightBDD:** a stored label. `HappyPathAttribute` is `LabelAttribute("Happy Path")`, detected with an ordinal
    `Contains` (`Kronikol.LightBDD.Core/HappyPathAttribute.cs:8-10`, `FeatureResultExtensions.cs:91`). It therefore
    reaches `data-labels`, `@tag`, the JSON, YAML and XML, CTRF `tags` and `Specifications.md`.
  - **TUnit:** a category (`Kronikol.TUnit/HappyPathAttribute.cs:8-10`). It is read only to set the flag.
  - **Gherkin, BDDfy and the NDJSON:** a tag that is stripped. `HappyPathDetection` knows `happy-path`, `happy_path`
    and `happypath`, not "Happy Path" with a space (`HappyPathDetection.cs:13-18`).
- **F12. Kronikol's own markers are native tags.**
  - Happy Path is an NUnit property, an xUnit trait, a TUnit category and a LightBDD label.
  - Endpoint is an NUnit property, a TUnit property and an xUnit trait (READ, each adapter's `HappyPathAttribute.cs`
    and `EndpointAttribute.cs`).
  - An adapter that copied every native tag across would show both as chips.
- **F13. CTRF drops an author's own `retry 2`.**
  - The filter that keeps Kronikol's generated retry labels out of `tags` matches any label of that shape
    (`CtrfReportGenerator.cs:157-161`, `Kronikol.Tool/CtrfCommand.cs:182-185`).
  - Nothing else needs the generated labels. CTRF counts `retries` from `Scenario.Attempt`
    (`CtrfReportGenerator.cs:178`), and so does the history's "passed on retry" text (`History/HistoryAnalyzer.cs:395`
    at `aa311cd7`).
- **F14. An NUnit `[TestCase]` test loses its happy path, and can cost its feature the endpoint (RUN).**
  - NUnit 4.6.0 puts a parameterized method's own `[Category]` and `[Property]` attributes on its
    `ParameterizedMethodSuite`, not on each case, and a case's `Parent` is that suite, not the fixture
    (`TAGS_PLAN.harness/nunit-testcase-probe/results.txt`).
  - The adapter reads the happy path from the case's own `Test.Properties`
    (`Kronikol.NUnit4/TestContextEnumerableExtensions.cs:28`, `:44`), so a `[HappyPath]` `[TestCase]` method is never a
    happy path.
  - It reads the feature's endpoint from the first captured test's `Test.Parent` (`:19-20`), so a fixture whose first
    captured test is a `[TestCase]` loses its endpoint.
  - `[TestCase(Category = …)]` lands on the case itself, and `AllCategories()` returns the case's, the method's and the
    fixture's categories, in that order.
- **F15. `formatVersion` 3 already names these keys, and its reader has shipped.**
  - REPORT_FORMAT_PLAN's version 3 "means the keys of version 2" and writes every required key. The required keys
    include a feature's `labels` and a scenario's `labels` and `categories` (`REPORT_FORMAT_PLAN.md:582-604`).
  - Its R3, the reader, shipped as 4.30.0. R4, the writer, ships v3 in 4.x behind an option.
  - The 4.30.0 reader does two things that matter here (READ at `aa311cd7`):
    - it refuses a `formatVersion` it does not know with "Upgrade Kronikol.Tool" (`Query/ReportGate.cs:45-49`);
    - it reads keys by name and skips the rest (`Query/ReportScanner.cs:905-906`).

    So it reads a 5.0.0 file that keeps `formatVersion` 3 as untagged, with no error.
- **F16. The toolbar plan's compatibility contract names these.**
  - §2.0 keeps `.category-toggle`, `.category-active`, `.cat-mode-toggle`, the meaning of every hash parameter "forever",
    and `data-categories` and `data-labels` because "other tooling reads them" (`TOOLBAR_REDESIGN_PLAN.md:105-125`).
  - Its v4 (now v5) note, "Not broken: URL hashes, `data-*` attributes, options semantics, data outputs" (`:303`),
    covers that plan's own changes: "this plan claims only the redesign + tokens" (`:304`).
  - So this plan is a separate break, and it has to say what it keeps (§4.10, Q3).
- **F17. #78's plan feeds the same tag set.** CHANGED_SCENARIOS_PLAN (uncommitted, worktree `Kronikol-78`) pushes
  `$added`, `$edited` and `$changed` into "the existing `tags` set in `fc()` and in `collectItemMeta`" (its `:531-532`).
  That set is built from labels plus categories.
- **F18. The wiki's recipe for marking new and changed tests is built on categories.** "Give each one of two
  categories, `pr-new` or `pr-edited` … a button for each in its Categories row", and "Any other tag becomes a label …
  with no button" (`Marking-New-and-Changed-Tests.md:2-16`, recipes `:50-165`).
- **F19. Dead code on the way.**
  - `.cat-hidden` (`stylesheets.css:953-955`).
  - `GenerateYamlSpecs`, a public second Specifications YAML writer with no caller (`ReportGenerator.cs:2594-2621`).
  - An unused `categories` local (`Kronikol.TUnit/TestContextEnumerableExtensions.cs:18`).
  - `tests/TestTrackingDiagrams.Tests.EndToEnd/ReportTestHelper.cs`, which no project includes.
- **F20. What no test covers.**
  - The Uncategorized button.
  - `query scenarios --label`, and the `labels` and `categories` fields of `scenarios --json`.
  - `ScenarioTags.Classify` directly.
  - The tag mapping of NUnit 4, MSTest, xUnit v2, xUnit v3 and TUnit.
  - On `v5`, `CategoryMultiSelectReportTests` (`:54`) asserts the word `allMatch`, which now occurs only in the search
    script, so it passes without testing the category filter.
- **F21. The modes.** `CategoryFilterMode` can be set only in code (`ReportToggleDefaults.cs:72`; no configuration
  key, CLI flag or MSBuild property reads it). On `v5` the Tags row's mode is the literal `"OR"`
  (`v5:ReportGenerator.cs:1848`), though TOOLBAR_REDESIGN_PLAN §2.4 asked for a default per row.
- **F22. Merging shards keeps the first shard's feature labels** (`Merge/MergeableReportMerger.cs:134`,
  `existing.Labels ?? feature.Labels`).
  - On the Gherkin and class-level lanes every shard carries the same set, so nothing is lost.
  - The NDJSON lane lifts per shard the tags that shard's scenarios share (§2.2), so the first shard's set can name a
    tag that another shard's scenarios lack.
  - Today that mislabels only the feature's header. Under effective tags (§4.2) it would tag those scenarios too, so
    §4.13 merges by intersection.
- **F23. An outline drawn as a parameterized group shows none of its tags.**
  - The group's element carries its rows' labels and categories for filtering, but its summary draws only the pass
    count and the Happy Path badge (`v5:ReportGenerator.cs:3218-3222`, `:3243-3251`; `ReportGenerator.cs:2980-2983`,
    `:3005-3014` on `main`).
  - So a Gherkin outline's own tags are visible nowhere once its rows are grouped.

## 4. Design

### 4.1 What a tag is

A tag is a name attached to a feature or a scenario, by its author or through the framework's own tagging. Tags have no
kinds. Kronikol's conventions (`endpoint:`, the happy-path names) are read by the classifier and are not tags.

### 4.2 The model

```csharp
public record Feature  { … public string[]? Tags { get; set; } … }   // replaces Labels
public record Scenario { … public string[]? Tags { get; set; } … }   // replaces Labels and Categories
```

- **What each list holds.** Each list holds the tags written at that level: a scenario's own tags (and, in Gherkin,
  those of its rule and its Examples block, §4.5), and a feature's own tags (the class, the fixture or `Feature:`).
  Each is null when empty, like the model's other optional arrays.
- **Effective tags.** A scenario's effective tags are its feature's tags followed by its own, deduplicated under §4.3.
  One internal helper computes them, `ScenarioTags.Effective(Feature, Scenario)`, next to `Classify`. Its consumers
  are the HTML, CTRF and Specifications, all in the core assembly. The query tool and `kronikol ctrf` read the data
  file through their own index (§4.13), so the helper needs no public surface. Filtering and search use effective tags.
  Badges and the data files use each level's own list.
- **Removed:**
  - `Feature.Labels`, `Scenario.Labels` and `Scenario.Categories`;
  - the `Labels` and `Categories` members of `ScenarioTags.Classified`;
  - `ReqNRollConstants.CategoryTagPrefix` and the Cucumber importer's private copy.

  `ScenarioTags.CategoryPrefix` stays, documented as a prefix that is accepted and dropped (§4.4).

### 4.3 Identity, spelling and order

- **Identity.** Two tags are the same tag when they are equal once their ASCII letters are folded (A to Z read as
  a to z, nothing else changed), after the classifier trims them and strips a leading `@`. This fixes F6.
- **Why ASCII only.** The three runtimes that write or read a report fold the rest of Unicode differently (RUN,
  `TAGS_PLAN.harness/case-fold/`, with PowerShell 7, Node and OpenJDK 25):

  | Comparison | .NET `OrdinalIgnoreCase` | JavaScript `toLowerCase` | Java `equalsIgnoreCase` and `CASE_INSENSITIVE_ORDER` |
  |---|---|---|---|
  | Kelvin sign (U+212A) and `k` | different | the same | the same |
  | dotless ı (U+0131) and `i` | different | different | the same |

  A rule that leans on any of them would let Kronikol4J's chips differ from .NET's, and a chip from the scenarios it
  picks. ASCII folding means the same on all three. A tag that differs only in the case of a non-ASCII letter ("Über",
  "über") is two chips, a rare and visible cost.
- **Spelling.** A list keeps the first spelling it meets, so in effective tags the feature's spelling wins. A chip
  shows the first spelling in report order (Q4).
- **One spelling per report in the page.** The HTML writes every tag in its chip's spelling (`data-tags`, §4.9). So the
  chip filter compares strings exactly and never folds case in the browser. Only `@` search folds case, as it does
  today.
- **Order.** Each list keeps its source order. The row's chips sort Happy Path first (D9), then by the ASCII-folded
  name, then by UTF-16 code units to break ties. That order is the same in .NET (`string.CompareOrdinal`), Java
  (`String.compareTo`) and JavaScript, and never depends on the current culture (F7). Kronikol4J uses the same rule
  (§4.14).

### 4.4 One classifier

`ScenarioTags.Classify(IEnumerable<string>? tags)` returns `Classified(string[] Tags, string? Endpoint, bool IsHappyPath)`:

- **Clean-up:** a leading `@` and surrounding whitespace are removed, and an empty tag is skipped (as today).
- **Endpoint:** the first `endpoint:x` sets the endpoint and is not a tag.
- **Happy path:** a happy-path name sets the flag and is not a tag. `HappyPathDetection` also accepts "Happy Path" with
  a space. That is LightBDD's label and TUnit's category, so every lane treats Happy Path the same way (F11).
- **`category:x`** becomes the tag `x`. A scenario carrying both `@smoke` and `@category:smoke` has one tag.
- **Anything else** is a tag, deduplicated under §4.3.

Every lane runs its raw names through it:
- ReqNRoll (in place of its constants), BDDfy, the Cucumber importer (in place of its own constants) and the NDJSON
  ingest;
- each adapter's native list of §4.7, so `[Category("happy-path")]` on NUnit means what `@happy-path` means in
  Gherkin.

### 4.5 Gherkin: feature, rule and Examples tags

- **Feature tags** are the feature's own tags: Reqnroll's `FeatureInfo.Tags` (fixes F9) and the Cucumber feature
  node's tags (as today).
- **Scenario tags** are the pickle's tags minus the feature's, so the scenario's own, its Examples block's and its
  rule's. On ReqNRoll that is `CombinedTags` minus `FeatureInfo.Tags`. On Cucumber it is the pickle's tags minus the
  feature node's (fixes F10).
- **Effective tags** are therefore exactly the pickle's tags, which is Cucumber's own inheritance rule.
- **Unchanged:** the endpoint (now read from the feature's tags first, then the scenarios') and the happy-path flag (the
  scenario's and its Examples block's tags, as today).

### 4.6 Tags Kronikol makes itself

The ingest lanes add a `retry N` label for each earlier attempt (`FeatureSynthesizer.cs:182`,
`CucumberFeatureSynthesizer.cs:701`). As a tag it would become a chip. Recommended (Q6):
- stop generating it, and draw the badge from `Scenario.Attempt`, whose doc comment already describes it as matching
  "the `retry N` label rendered beside it" (`Scenario.cs:58-66`);
- delete CTRF's retry filter with it, which fixes F13.

Nothing else reads the generated labels. CTRF's `retries` and the history's "passed on retry" text already come from
`Attempt` (F13).

- **The HTML has no badge of its own.** It draws nothing from `Attempt` today: `Attempt` appears only in the JSON, XML
  and YAML writers (`ReportGenerator.cs:4840`, `:5398`, `:5679` at `aa311cd7`). So T2 adds the badge, in the place and
  wording the label had.
- **Older files.** A 4.x file still carries the generated labels in `labels`. The readers of §4.13 drop a `retry N`
  from an old file's labels when the scenario's `attempt` accounts for it, so `kronikol ctrf` on a 4.x file does not
  start offering them as tags.

The two ingest lanes number the labels differently: the NDJSON lane writes the earlier attempts' numbers, and the
Cucumber lane writes `Attempt + 1` of each earlier attempt (`CucumberFeatureSynthesizer.cs:701`). So T1 pins each
lane's numbering with a test before anything moves.

### 4.7 The adapters

| Lane | Feature tags | Scenario tags | Read through |
|---|---|---|---|
| NUnit 4 | `[Category]` and `[TestFixture(Category = …)]` on the fixture, and `[Category]` on the assembly | `[Category]` on the test; for a `[TestCase]`, its own `Category =` and the method's `[Category]`, which sits on the `ParameterizedMethodSuite` (F14) | `TestAdapter.PropertyHierarchy()`, which gives each value's level, or a walk up `Parent` reading `Properties["Category"]`. `AllCategories()` gives the same values with no level. The same walk reads `Happy Path` from the case or its method suite and `Endpoint` from the nearest fixture, which fixes F14 |
| MSTest 4 | `[TestCategory]` on the class and the assembly | `[TestCategory]` on the method | reflection over `TestCategoryBaseAttribute.TestCategories` on the `MethodInfo`, its type and `type.Assembly`, all of which `[TestCleanup]` already holds (`DiagrammedComponentTest.cs:33-69`). `TestContext` 4.2.2 has no category member |
| xUnit v3 | `Category` traits of `TestClass.Traits` | `Category` traits of `Test.Traits` minus the class's | the `TestContext.Current` the adapter already captures (`TestContextEnumerableExtensions.cs:23-43`); class traits merge into a test's, so the difference gives the level |
| xUnit v2 | `Category` traits on the class (`TraitHelper.GetTraits(type)`) | `TestCase.Traits["Category"]` minus the class's | `ITestStarting` in the results sink (`KronikolResultsSink.cs:110-136`), and `TraitHelper.GetTraits(MemberInfo)` on the attribute fallback (`TestTrackingAttribute.cs:27-75`) |
| TUnit | `[Category]` on the class (reflection over `ClassType`) | `TestDetails.Categories` minus the class's | `[After(Test)]` (`DiagrammedComponentTest.cs:17-22`). `TestDetails.Categories` merges method, class and assembly values with no level |
| BDDfy | none, since a `Story` has no tags | the scenario's tags | `DiagramCapturingProcessor.Process` (`:74`). `category:x` becomes `x` through `Classify`, which fixes BDDfy's half of F1 |
| LightBDD | the class's `[Label]` (`IFeatureInfo.Labels`) | `Info.Labels` and `Info.Categories` | unchanged capture. A class-level `[ScenarioCategory]` arrives on every scenario with no level and stays a scenario tag (Q8) |
| ReqNRoll, Cucumber | §4.5 | §4.5 | `FeatureInfo.Tags` is new to the ReqNRoll hook (`ReqNRollTrackingHooks.cs:159-219`) |
| Tests NDJSON | tags every scenario of the feature shares (the `SharedLabels` rule, `FeatureSynthesizer.cs:279-293`, renamed) | the scenario's `tags` | unchanged; the shared tags stay on the scenarios, where effective tags deduplicate them |

- **Kept out everywhere:** Kronikol's own markers (F12). Reading only the `Category` trait key, the `Category`
  property, `[TestCategory]` and TUnit's `[Category]` (whose "Happy Path" value `Classify` turns into the flag) keeps
  `Happy Path` and `Endpoint` out without a list of exclusions.
- **Not read in 5.0.0:** other trait keys and properties, such as BreakfastProvider's `[Trait("Produces", …)]` (Q7).
- **Every lane gets a mapping test** (§5). NUnit 4, MSTest, xUnit v2, xUnit v3 and TUnit have none today (F20).

### 4.8 The report (v5)

- **One Tags row.** It shows a chip per effective tag of any scenario, with Happy Path first (D9) and the rest in §4.3's
  order. Chips carry their counts, as now, and Untagged (`__untagged__`) picks the scenarios with no effective tag.
  ANY/ALL comes from `TagFilterMode` (§4.11), and the row has Clear (D12).
- **Deleted:** the Categories row (`v5:ReportGenerator.cs:1870-1887`), `report-category-filter-function.js`,
  `item_categories`, and the `category` family of `FILTER_CHIP_ROWS` (`v5:report-filter-chips-script.js:8-12`,
  `:69-72`).
- **Badges:**
  - a feature shows its own tags, where labels are drawn today;
  - a scenario shows its own tags minus its feature's, so nothing is drawn twice, plus the Happy Path badge as today;
  - a parameterized group shows the tags all its rows share, minus its feature's (F23: today it draws none).
- **Filtering.**
  - A scenario matches a chip when the tag is among its effective tags, so a feature's tags filter all of its
    scenarios (F3).
  - A parameterized group matches when any of its rows does, as today (`data-tags` holds the union).
  - Rows from different Examples blocks can carry different tags (§4.5), so a chip for one block's tag shows the
    whole group. Whether a chip should also hide the other rows is Q14.
- **Search.**
  - `@tag` reads the same set as the row, Happy Path included (F2), still ignoring case.
  - `@"two words"` matches a tag that has a space (F8), as TOOLBAR_REDESIGN_PLAN §2.5 asked.
  - Free-text search is unchanged.
- **Volume** (Q9). The row wraps, as `v5` does. T2 renders fixtures with 0, 1, 9 (BreakfastProvider's count), 40 and
  400 distinct tags at 1280 and 390 px, and measures the height of the painted row. If 400 tags take more than a
  screen, the row shows the tags with the highest counts and a "N more" chip that opens the rest.

### 4.9 One attribute, safe for any name

- **The attribute.** `data-tags` holds a scenario's effective tags, each in its chip's spelling (§4.3), percent-encoded
  (`Uri.EscapeDataString`) and joined with commas: `data-tags="smoke,a%2Cb,Happy%20Path"`. The scripts split on the comma and apply
  `decodeURIComponent`, through one helper that the chip script's item cache calls once per item. A comma, quote or
  space inside a name survives (F4, F5).
  - The URL hash uses the same encoding (§4.10).
  - It costs bytes only where a name has such a character.
- **Chips** keep their name in `data-tag`. No script builds a selector from a name: lookups compare `getAttribute`
  values (F5).
- **`data-labels` and `data-categories` go** (F16, Q3).

### 4.10 The link

- **Written:**
  - `tags` (has), `tagx` (has not) and `tagmode` (only when not the default), as `v5` already writes them;
  - each name percent-encoded, then joined with commas;
  - Happy Path stays `hp` and `hpx`.
- **Read:**
  - everything `v5` reads;
  - 4.x `cats=a,b`, read as `tags` (has);
  - `catmode`, read as `tagmode` when the link has no `tagmode`;
  - `__uncategorized__`, read as `__untagged__`.
- **`catx` is dropped.** It exists only on the unreleased `v5` branch.
- **Old links keep their meaning.** Every link a published report wrote means what it meant, with "category" read as
  "tag", which is TOOLBAR_REDESIGN_PLAN §2.0's rule.
- **One bad value loses only itself.** Each parameter is read inside its own `try` (F5).

### 4.11 The option

`ReportToggleDefaults.TagFilterMode` (`FilterCombinationMode?`, default `Or`) replaces `CategoryFilterMode`
(`ReportToggleDefaults.cs:71-72`), and the resolver follows. It seeds the Tags row's switch and the chip script's
`_tagModeDefault`, which is hard-coded today (F21). As before, it can be set only in code.

### 4.12 Outputs

| Output | Today | 5.0.0 |
|---|---|---|
| `TestRunReport.json`, mergeable JSON | feature `labels`; scenario `labels`, `categories` | feature `tags`; scenario `tags` (own) |
| `TestRunReport.schema.json` | `labels`, `categories` required (`:6547`, `:6562`) | `tags` declared, not required, so absent when empty (`formatVersion` 3 leaves empty optional keys out) |
| `TestRunReport.yml`, `.xml`, `.schema.xsd` | `Labels`, `Categories` | `Tags`, `<Tags><Tag>` |
| `Specifications.json`, `.yml`, `.xml`, `.md` | `Labels`, `Categories` | `Tags`; the markdown prints one "Tags:" line |
| `ctrf-report.json` | `tags` = labels minus generated retries; `extra.categories` | `tags` = effective tags; no `extra.categories` |
| `TestRunReport.html`, `Specifications.html` (one generator) | `data-labels`, `data-categories` | `data-tags` (§4.9) |

- **Own tags, not effective ones.** The data files hold each level's own tags, so a feature's tag is written once and
  readers compute effective tags (§4.13).
- **A feature's `tags` comes before its `scenarios`.** The query index is built in one pass and copies a feature's
  tags into each scenario as that scenario starts (`ReportScanner.cs:434`, `:735-736` at `aa311cd7`, for `labels`
  today). The writer already orders `labels` that way (`ReportGenerator.cs:4774-4775`), and a fact pins it for `tags`.
- **Committed outputs change.** A consumer that commits a generated `Specifications.yml` (BreakfastProvider's
  `docs/Specifications.yml`) sees its `Labels` and `Categories` keys become `Tags`. "Migrating to v5" says so.

`formatVersion`, and whether a 4.x reader should learn `tags`: Q1.

### 4.13 Readers and the query tool

- **`ReportScanner`** (`query`, `ctrf`) and **`MergeableReportReader`** (`merge`) read `tags`. From an older file they
  read `labels` then `categories` as one list, deduplicated, and a feature's `labels` as its tags. A 4.x file
  therefore reads as tagged with both, less the generated `retry N` labels that its `attempt` accounts for (§4.6).
- **Merging shards** gives a feature the tags every shard that carries it agrees on, an intersection (F22).
  - On the Gherkin and class-level lanes that is each shard's set, since they are equal.
  - On the NDJSON lane it is what all the feature's scenarios share. The lifted tags also stay on the scenarios, so no
    scenario loses one.
  - A union would lift a tag onto scenarios that never carried it.
- **`kronikol query scenarios`:**
  - Its `--json` field is `tags`, holding the effective tags, with the feature's first.
  - `--tag T` keeps today's `--label` match: a case-insensitive substring over effective tags, so the wiki's
    `--label pr-` trick still lists both kinds.
  - `--label` stays as an alias (Q2). `VerbTable` holds both, as the one source of the CLI.
- **The skill** changes in both copies (`templates/skills/kronikol-test-debugging/references/commands.md:53`, `:121`,
  and `.claude/skills/…`), and `SkillDriftTests` holds them equal (`:142`, `:161`, `:206`).
- **History** is untouched (§2.4).

### 4.14 Kronikol4J

- **Model:** `Scenario` and `Feature` get `tags` in place of `labels` and `categories` (`Scenario.java:21-27`,
  `Feature.java:9-15`).
- **Adapters, new behaviour:** today none of the four maps a tag (JUnit 5 `KronikolExtension.java:67-71`, Cucumber-JVM
  `KronikolCucumberPlugin.java:53-59`, TestNG `KronikolTestNgListener.java:57-65`, Spock
  `KronikolSpockExtension.java:64`). They map:
  - JUnit 5 `@Tag` on the class to the feature and on the method to the scenario;
  - Cucumber-JVM pickle tags by §4.5;
  - TestNG groups and Spock tags the same way.
- **No change is needed for 5.0.0 to read its files.** The 5.x readers accept `labels` and `categories` (§4.13), so
  Kronikol4J's reports stay readable whatever it writes. Its data writer moves to `tags` with its model
  (`ReportDataSerializer.java:68`, `:96-97`; `MergeableReportMerger.java:87`, `:92`).
- **Its HTML** is pinned to .NET 3.0.43's rendering (Kronikol4J `README.md:7-12`; `DotNetHtmlReportRenderer.java:797-808`,
  `:943-946`). So the one Tags row and `data-tags` reach it only when it ports the v5 report.
- **Identity and order** follow §4.3's ASCII fold. Java's `equalsIgnoreCase` and `CASE_INSENSITIVE_ORDER` must not be
  used, since they fold the Kelvin sign and the dotless ı that .NET keeps apart (A5).
- **Ledger:** an entry in `docs/REMAINING_PARITY.md` (the divergence ledger, `:1877`).

### 4.15 Not in this plan

- Other trait keys, NUnit and TUnit properties as `key: value` tags (Q7).
- Tag colours, groups or hierarchies.
- Keying history by tags.
- #78's change tokens, which CHANGED_SCENARIOS_PLAN owns. After this plan they join the one tag set it already
  names (F17).

## 5. Tests, red first

Every fact below is written before its code. It must fail on the unchanged code at the line it names, and the step's
commit message says so ("N of N failed first"). Mutations of the new code are killed afterwards. The Playwright facts
follow CLAUDE.md's rules: no forced clicks, no network mocking, `PollingInterval = 200`, `dispatchEvent` for SVG, `.First`
or `.Nth`, and `FillSearchBar()`.

### 5.1 The classifier and the model (T1)

- `ScenarioTagsTests` is new, since there is no direct test of `Classify` today (F20). It covers:
  - the `category:` prefix dropped, and `@smoke` with `@category:smoke` giving one tag;
  - the endpoint, and every happy-path name including "Happy Path";
  - empty names skipped;
  - deduplication by the ASCII fold keeping the first spelling, and source order kept;
  - a Kelvin-sign tag and `k`, and a dotless-ı tag and `i`, kept apart (§4.3's table).
- `ScenarioTags.Effective`: feature tags first, a scenario tag that repeats a feature tag in another case dropped, and
  both lists null giving an empty result.

### 5.2 The adapters (T1)

One fact per lane puts a class-level tag on the feature and a method-level tag on the scenario, and one shows that
Kronikol's markers are not tags:

- **NUnit 4:**
  - fixture `[Category("Slow")]` on the feature, method `[Category("Smoke")]` on the scenario;
  - a `[TestCase]` method's `[Category]` and a `[TestCase(Category = …)]` on each case's scenario;
  - F14's red tests: a `[TestCase]` method with `[HappyPath]` read as a happy path, and a fixture whose first captured
    test is a `[TestCase]` keeping its `[Endpoint]`. The probe's tree (`TAGS_PLAN.harness/nunit-testcase-probe/`) is
    the shape to build them from.
- **MSTest:** `[TestCategory]` on the class, the method and the assembly.
- **xUnit v3 and xUnit v2:** `[Trait("Category", …)]` on the class and the method. A `[Trait("Produces", …)]` is not a
  tag. On v2, both capture paths: the results sink and the attribute fallback.
- **TUnit:** `[Category]` on the class and the method. `[HappyPath]` sets the flag and is not a tag.
- **BDDfy:** `category:x` gives `x`. This replaces `BDDfyScenarioInfoExtensionsTests.cs:96` and `:110`.
- **LightBDD:**
  - the class's `[Label]` on the feature;
  - the method's `[Label]` and `[ScenarioCategory]` on the scenario;
  - `[HappyPath]` sets the flag and is not a tag.

  This replaces `FeatureResultExtensionsTests.cs:51` and `:61`.
- **ReqNRoll:**
  - feature tags come from `FeatureInfo.Tags`;
  - a rule's tags reach that rule's scenarios and no others;
  - a feature tag the first scenario repeats stays on the feature.

  The last two are F9's red tests. This replaces `ReqNRollScenarioInfoExtensionsTests.cs:277`, `:291`, `:341` and
  `ReqNRollTUnitScenarioInfoExtensionsTests.cs:87`.
- **Cucumber:** a rule's tags reach its scenarios (F10). This replaces `CucumberFeatureSynthesizerTests.cs:44` and
  `:92`, including the inherited `@category:` at `:97`.
- **Tests NDJSON:** `WidenedTestRunRecordTests.cs:88` and `:105` are renamed to tags. `IngestAttemptTests.cs:39` and
  `:116` pin the retry badge's number before `retry N` stops being a label (§4.6).

The NUnit 4, xUnit v3 and TUnit facts have no home today, since those adapters have no mapping tests. T1 adds them
beside the existing adapter tests, in a project that runs under the framework where the facts need its runtime objects.

### 5.3 Outputs and readers (T1, T3)

- **Writers:**
  - `tags` in JSON, YAML, XML, both schemas and Specifications. This rewrites:
    - `TestRunReportDataTests.cs:151`, `YamlStepsReportTests.cs:79`;
    - `ScenarioStepModelTests.cs:104`, `:116`, `:139`;
    - `SpecificationsMarkdownGeneratorTests.cs:106`, `:127`, `SpecificationsDataTests.cs:34`, `:119`.
  - The `RichFeatures` fixture behind the schema contract facts changes (`TestRunReportSchemaContractTests.cs:149-170`).
  - So does the pin `tests/Kronikol.Tests/TestData/Reports/TestRunReport.pin.json:23-26`, `:40-45`.
- **Readers:**
  - a 4.x file with `labels` and `categories` reads as tagged with both, in `query`, `merge` and `ctrf`;
  - a 5.0.0 file with `tags` reads the same;
  - a merge of one shard of each kind works;
  - two NDJSON shards where only one lifts `X` give the merged feature no `X`, and every scenario keeps its own `X`
    (F22).
- **CTRF:**
  - `Categories_travel_in_extra_rather_than_pretending_to_be_tags` (`CtrfReportGeneratorTests.cs:220`) is replaced by
    "every effective tag is a CTRF tag";
  - `:193` becomes "an author's `retry 2` survives" (F13);
  - the key list at `:342` drops `categories`.
- **Query:**
  - the first facts the flag has ever had (F20): `--tag` and `--label` match a case-insensitive substring of an
    effective tag, a feature's included;
  - `scenarios --json` carries `tags`;
  - the 20 tool test files with raw `"labels": []` and `"categories": []` JSON keep working as old-format input. One
    fact per verb reads a `tags` file.

### 5.4 The report (T2)

- **Markup:**
  - `FilterRowsTests` (`v5:`) loses its Categories row facts (`:132`). It gains:
    - a Tags row built from effective tags;
    - one chip for Smoke and smoke;
    - chips in §4.3's order and not by culture, run under a Turkish and a Swedish `CurrentCulture` (F7);
    - the switch seeded from `TagFilterMode`.
  - `CategoryMultiSelectReportTests` is deleted (F20). Rewritten to the Tags row:
    - `StepRenderingReportTests.cs:594`, `:607`, `ExamplesTableReportTests.cs:379`;
    - `SearchDataAttributeTests.cs:194-250`, `TagExpressionSearchReportTests.cs:50`;
    - `ToggleDefaultsMarkupTests.cs:351`, `ReportToggleDefaultsResolverTests.cs:23`, `:58`;
    - `UrlHashFilterTests.cs:96-116` and `StylesheetRulesTests.cs:129`.
  - `ToggleDefaultsBaselineTests.cs:264` keeps an unset configuration byte-identical to the built-in defaults.
- **Playwright** (`v5:FilterPipelineTests.cs` and the files after it):
  - a feature's tag filters its scenarios (F3);
  - a tag with a comma, one with a double quote and one with a space each filter, search and survive a link
    (F4, F5, F8);
  - `@` finds Happy Path (F2);
  - a 4.x link with `cats=` and `catmode=` reads as tags;
  - a link with one malformed value applies the rest.
  - Rewritten to tags: `FilterModeDefaultsTests.cs:78`, `ScenarioInteractionTests.cs:389`,
    `AdvancedSearchTests.cs:126`, `:135`, `:157`, `:195`, `ToolbarStylingTests.cs:71` and
    `ExportFilteredCsvTests.cs:83`, `:100`.
  - The shared fixtures move from categories to tags (`ReportTestHelper.cs:102`, `:114`, `:126`, which the `@smoke`
    counts depend on). The volume fixtures of §4.8 are added.

### 5.5 Kronikol4J (T4)

`ReportDataParityTest.java:68` and `GoldenHtmlParityTest.java:328` against the new .NET goldens. Adapter facts for
`@Tag`, TestNG groups, Spock tags and Cucumber-JVM pickle tags.

## 6. Releases and records

| Step | Where | Bump | What |
|---|---|---|---|
| T1 | `v5` | none (branch) | the model, the classifier, the adapters, the writers, the readers and the schemas (§4.2 to §4.7, §4.12, §4.13's readers), with F9, F10, F11 and F14 |
| T2 | `v5` | none | the report: one Tags row, `data-tags`, the link, search, `TagFilterMode` and the retry badge (§4.6, §4.8 to §4.11), with F2 to F8, F20's vacuous test, F21 and F23 |
| T3 | `v5` | none | the query tool, CTRF, Specifications, merge and the skill (§4.13), with F13 and F22 |
| T4 | `v5`, the wiki, Kronikol4J | none | the docs, "Migrating to v5", the `[Unreleased]` changelog and the Kronikol4J port (§4.14), with F1's docs and the glued wiki rows |

- **Nothing ships on `main`.** All of it is on `v5`, the fixes for the defects of §8 included, at the owner's decision
  of 2026-10-19. The one exception would be a reader step, if Q1 is answered (a).
- **Commit form.** Each `v5` step is one commit, worded as the branch's commits are: "5.0.0 only (no bump, branch
  v5): TAGS_PLAN T1, …". 5.0.0 itself is released with the other pieces (`V5_PLAN.md`).
- **The two branches.**
  - `v5` has not merged `main` since 4.26.9 (`354cb998`). At `05be4469` and `aa311cd7`, `v5` is 34 commits ahead of
    `main` and 14 behind.
  - T1 edits files that 4.27.0 to 4.30.0 changed (the header lists them), 4.30.0's report readers among them.
  - So `main` is merged into `v5` before T1. That merge is the call of the session building `v5`.
- **The changelog** (`v5:CHANGELOG.md`, `[Unreleased]`):
  - **Changed (breaking):**
    - `Labels` and `Categories` become `Tags`, and `ScenarioTags.Classified` loses its `Labels` and `Categories`;
    - the `labels` and `categories` keys become `tags`, and so do the `scenarios --json` fields;
    - the Categories row is gone, with its classes (`.category-filters`, `.category-toggle`, `.category-active`,
      `.cat-mode-toggle`), which a custom stylesheet may target;
    - `data-labels` and `data-categories` become `data-tags`;
    - `CategoryFilterMode` becomes `TagFilterMode`;
    - CTRF's `extra.categories` is gone, and CTRF `tags` carry every tag;
    - `--label` becomes `--tag`;
    - the generated `retry N` labels are gone, and a badge drawn from the attempt replaces them;
    - tags that differ only in the case of an ASCII letter are one tag;
    - `HappyPathDetection` also takes "Happy Path".
  - **Added:**
    - the adapters read their framework's tags;
    - rule tags reach their scenarios on both Gherkin lanes;
    - quoted `@"…"` search;
    - a parameterized group shows the tags its rows share.
  - **Fixed:** F2 to F11, F13, F14, F22 and F23. F9 and F14 change what existing reports show: some features' tags,
    and the happy path and endpoint of NUnit `[TestCase]` tests.
- **The wiki** (`origin/master` at `23edeca`):
  - `Tags-and-Attributes.md`, rewritten as one Tags section with a table per framework, in place of the Category table
    (`:30-43`) and the Labels section (`:47-49`);
  - `Search-Syntax.md:79-88`, `:186`;
  - `Generated-Reports.md:183`, `:525`, `:862`;
  - `Report-Configuration.md:254`, `:272`;
  - `Querying-Reports.md:269`, `:391`, `:479`;
  - `Marking-New-and-Changed-Tests.md`, where any tag now gets a button, so the recipes add plain tags;
  - `API-Reference.md:60-61`, `Integration-Cucumber-Messages.md:83`, `:87` and `Ingesting-External-Captures.md:134`;
  - the three ReqNRoll integration pages, `Integration-TUnit.md`, `Integration-BDDfy-xUnit3.md`,
    `Integration-Playwright.md`, `Framework-Integration-Guides.md`, `How-To-Guides.md`, `Home.md` and `_Sidebar.md`.

  T4 re-takes those lines at its own revision.
- **Kronikol4J:** its own release, and a ledger entry.
- **Records:** a `PLANS_STATUS.md` row (with this plan), and §7's placement once the plan is green-lit.

## 7. Roadmap and index

The owner put this in 5.0.0 on 2026-10-19 ("for v5"). Placing it takes these edits, each claimed by message first
("taking D40 and 1e.9 now"), since other sessions edit the same files:

- `V5_PLAN.md`: a fourth row in the table of pieces, and "three pieces" made four (`:9`). The same words appear in
  `REPORT_FORMAT_PLAN.md:1151` and `:1342`, and in stage 1e's rows (`ROADMAP.md:344-351`).
- `ROADMAP.md`:
  - a stage 1e row (1e.9) and decision D40 (the next free number; D39 is the last);
  - an "Amended 2026-10-19:" sentence on line 3;
  - the §7 plan list and an Appendix B.1 row.
- `CLAUDE.md`'s MAJOR line, which lists what v5 holds.
- `plans/OPTION_C_PLAN.md`'s header on `v5`, whose scope names the pieces.
- `REPORT_FORMAT_PLAN.md` §4.2, if Q1 is answered as recommended: `formatVersion` 3 carries either `tags` or the older
  `labels` and `categories` (`:582-604`), and its v5 row says that 5.0.0 writes `tags`.
- The unexecuted port plans: `NODE_PORT_PLAN.md` (`:600`, `:629`, `:877`, `:1222`) and `JAVA_PORT_PLAN.md`
  (`:657-658`) describe labels and categories, the category filter and the ReqNRoll tag conventions. Each gets a line
  pointing here, so a port is built to one tag list.

## 8. Found on the way

Per CLAUDE.md, every defect found is fixed with its test first. At the owner's decision of 2026-10-19 every fix ships
in 5.0.0, in the `v5` step that rewrites the code it lives in, and none as a 4.x patch. Those that hold on 4.x stay
there until 5.0.0, and the changelog says so for the ones that change what an existing report shows.

| Finding | Fixed in | Exists on 4.x today |
|---|---|---|
| F4, a comma splits a tag | T2 (§4.9's encoding) | yes: wrong filtering |
| F5, a quote in a category breaks a link | T2 (§4.10) | yes: a shared link loses its category filter and its target |
| F6, case gives two chips | T2 (§4.3) | yes |
| F7, the chips' order depends on culture | T2 (§4.3) | yes, and differs from Kronikol4J |
| F8, a tag with a space cannot be searched | T2 (§4.8) | yes |
| F9, ReqNRoll's feature labels from the first scenario | T1 (§4.5) | yes: some features' labels are wrong |
| F10, the Cucumber importer drops rule tags | T1 (§4.5) | yes |
| F11, Happy Path is three things | T1 (§4.4) | yes |
| F13, CTRF drops an author's `retry 2` | T1 and T3 (§4.6) | yes |
| F14, NUnit `[TestCase]` loses its happy path and can cost the endpoint | T1 (§4.7) | yes (RUN) |
| F19, dead code | T1 and T2; the orphan `TestTrackingDiagrams.Tests.EndToEnd/ReportTestHelper.cs` too | `GenerateYamlSpecs` is public, so removing it is a 5.0.0 change anyway |
| F20, `v5`'s vacuous `CategoryMultiSelectReportTests` | T2, which deletes it | no, `v5` only |
| F21, the modes | T2 (§4.11) | yes |
| F22, merging keeps one shard's feature labels | T3 (§4.13, by intersection) | yes, on the NDJSON lane: a merged feature can show a tag some of its scenarios lack |
| F23, an outline's tags are never drawn | T2 (§4.8) | yes |
| Two ReqNRoll pages glue two table rows into one with `\|\|`, so the `@category:` row does not render (`Integration-ReqNRoll-TUnit.md:223`, `Integration-ReqNRoll-xUnit2.md:228`) | T4, which rewrites those rows | yes, in the live docs |
| F1's wiki table and schema description | T4, and T1 for the schema text | yes, in the live docs and every 4.x schema |

## 9. Questions

1. **Q1. What a 4.x tool does with a 5.0.0 file, and what `formatVersion` says.**
   - REPORT_FORMAT_PLAN's R3 reader shipped as 4.30.0. It refuses an unknown `formatVersion` with "Upgrade
     Kronikol.Tool" and skips keys it does not know (F15).
   - **Recommended: no 4.x step.**
     - Keep `formatVersion` 3 for 5.0.0's files, and define it in REPORT_FORMAT_PLAN §4.2 as carrying either `tags` or
       the older `labels` and `categories`.
     - A 4.x tool then reads a 5.0.0 file without its tags. That includes the history action pinned to a 4.x
       Kronikol.Tool, the tool most likely to meet a newer file, and history never reads tags, so it works unchanged.
     - Only `query`'s tag filter and `ctrf` lose the tags when run by an old tool. "Migrating to v5" says to read
       5.0.0 files with Kronikol.Tool 5.x.
     - This keeps all the work on `v5`, as the owner asked for the fixes.
   - **Alternative (a): T0, a 4.x patch** that teaches `ReportScanner`, `MergeableReportReader` and `kronikol ctrf`
     the `tags` key. An old tool from that patch on sees the tags.
   - **Alternative (b): `formatVersion` 4** for 5.0.0's files, and `mergeableFormatVersion` 2.
     - Every 4.x tool then refuses them, the pinned history action included, until its version moves.
     - It is the cleanest answer for `kronikol merge`, since an old merge would otherwise write a merged report
       without tags.
     - Version 4 is earmarked for #120 (`REPORT_FORMAT_PLAN.md:1087`), which would move to 5.
2. **Q2. `--label`.** Recommended: keep it as an alias of `--tag` through 5.x, since skill copies and scripts in the
   wild use it. The alternative is to remove it at 5.0.0 and say so under "Changed (breaking)".
3. **Q3. `data-labels` and `data-categories`.** Recommended: drop both in favour of `data-tags` (A1). The alternative
   is to keep `data-labels` as a copy of `data-tags` for 5.x, honouring TOOLBAR_REDESIGN_PLAN §2.0's "other tooling
   reads them" at the price of one more attribute per scenario.
4. **Q4. A chip's spelling.** Recommended: the first spelling in report order. The alternative is the spelling most
   scenarios use, which costs a count per spelling.
5. **Q5. `@category:`.** Recommended: accept it and drop the prefix, with no warning, for good. The alternative is a
   report diagnostic that suggests the bare tag.
6. **Q6. The generated `retry N` labels.** Recommended: stop generating them, and draw the badge from
   `Scenario.Attempt` (§4.6). The alternative is to keep them as tags, which makes them chips.
7. **Q7. Other trait keys and properties as `key: value` tags** (BreakfastProvider's `[Trait("Produces", …)]`).
   Recommended: not in 5.0.0. Only the framework's category attribute is read, and a later minor can add the rest.
8. **Q8. LightBDD's class-level `[ScenarioCategory]`.** Recommended: leave it on each scenario, since LightBDD does
   not say where it came from. The alternative is to lift it to the feature when every scenario carries it (the NDJSON
   rule), which can also lift a method-level category that happens to be on every method.
9. **Q9. Many tags.** Recommended: measure in T2 (§4.8), and collapse the row only if 400 tags take more than a
   screen at 1280 px.
10. **Q10. Which fixes ship on 4.x.** Decided by the owner on 2026-10-19: none. "No, just include them in the plan as
    part of v5." Every fix of §8 ships in 5.0.0.
11. **Q11. BDDfy and xUnit traits.** `DiagramCapturingProcessor` already holds `Xunit.TestContext.Current` (`:22`).
    Recommended: also read the test's `Category` traits there, since a BDDfy test is an xUnit test.
12. **Q12. Assembly-level categories** (NUnit, MSTest, TUnit). Recommended: tags of every feature. Under effective
    tags that is the same as tags of every scenario, and it is drawn once per feature.
13. **Q13. Kronikol4J.** Nothing in 5.0.0 depends on it, since the 5.x readers accept its `labels` and `categories`
    (§4.14). Recommended: its tag mapping (`@Tag`, groups, pickle tags) ships in its own next minor, writing `tags` and
    following §4.3's fold. Its four adapters map no tag today, so that is new to its users whichever way it ships.
14. **Q14. Rows of a parameterized group with different tags.** A Gherkin outline's Examples blocks can carry
    different tags (§4.5), and a chip filters the whole group (§4.8). Recommended: keep group-level filtering in 5.0.0,
    as today, and record the row-level question for later. The alternative is a `data-tags` on each row, with a chip
    hiding the rows it does not match the way search marks the rows it does.

## 10. Assumptions

| # | Assumption | Basis | What settles it |
|---|---|---|---|
| A1 | No tool outside Kronikol reads `data-labels` or `data-categories` | TOOLBAR_REDESIGN_PLAN §2.0 says "other tooling reads them" without naming any; no wiki page mentions either (READ) | Q3 |
| A2 | Settled 2026-10-19: NUnit 4.6.0 puts a `[TestCase]` method's own attributes on its `ParameterizedMethodSuite` | RUN, `TAGS_PLAN.harness/nunit-testcase-probe/` | F14, now a finding |
| A3 | LightBDD's xUnit hosts map `[Trait("Category", x)]` to scenario categories | INFERRED from a type reference and one string literal | none needed: §4.7 reads `Info.Categories` either way |
| A4 | MSTest 4.2.2's `TestContext.Properties` does not carry categories at run time | READ, the XML docs; not run | §5.2's MSTest fact |
| A5 | Settled 2026-10-19: Java's `equalsIgnoreCase` and `CASE_INSENSITIVE_ORDER` do not match .NET's `OrdinalIgnoreCase` outside ASCII | RUN, `TAGS_PLAN.harness/case-fold/` | §4.3 folds ASCII letters only, and T4's parity fixture carries a Kelvin-sign tag |
| A6 | BreakfastProvider's use is typical of a suite that tags at all | one consumer | §4.8's 400-tag fixture covers a ticket-per-scenario suite |
| A7 | TUnit's `TestDetails.Categories` is in declaration order | not confirmed | §5.2's TUnit fact |

## 11. Log

- **2026-10-19.** Written at the owner's ask, after they decided labels and categories are one concept, for v5.
  - **Sources:**
    - five read-only sweeps: the adapters; `main`'s writers and readers; the `v5` rows; the tests, the wiki and
      Kronikol4J; and the other plans;
    - a grep of BreakfastProvider's source;
    - a reflection run over LightBDD 3.12.1.
  - **Checked:** every line cited in §2, §3 and §8 was token-checked against `6f6043e2` or `a91d4b91`, and the wiki's
    against `f3898f7`.
  - Nothing in a package changes.
- **2026-10-19, review.** At the owner's ask to "double check the plan for completeness and accuracy and do any deep
  dives".
  - **The owner decided** that the fixes for the defects found on the way ship in 5.0.0, not as a 4.x patch. The
    first draft's P1 is gone (§6, §8, Q10).
  - **`main` reached 4.30.0**, which shipped REPORT_FORMAT_PLAN's R3 reader. Q1 is rewritten around what that reader
    does with a 5.0.0 file, and now recommends no 4.x step. F15 says what the reader does.
  - **RUN:**
    - `TAGS_PLAN.harness/nunit-testcase-probe/` confirms F14 and widens it: a fixture can also lose its endpoint.
    - `TAGS_PLAN.harness/case-fold/` shows .NET, JavaScript and Java folding case differently outside ASCII. So §4.3
      folds ASCII letters only, where the first draft said `OrdinalIgnoreCase` (A5 settled).
  - **READ:**
    - the APIs §4.7 names exist at the pinned versions;
    - Kronikol's xUnit markers use their own trait keys;
    - MSTest's `TestContext` has no category member;
    - nothing but CTRF's filter reads the generated `retry N` labels, and the HTML draws nothing from `Attempt` (§4.6);
    - the query index needs a feature's `tags` before its `scenarios` (§4.12);
    - BreakfastProvider's code uses none of the members removed (§2.6).
  - **Added:**
    - F23, an outline's tags are never drawn;
    - Q14, rows with different tags;
    - the chip spelling written into the page (§4.3);
    - an internal, not public, `ScenarioTags.Effective` (§4.2);
    - older files' generated labels dropped on read (§4.13);
    - Kronikol4J needing nothing for 5.0.0 (§4.14, Q13);
    - the breaking-change list completed (§6).
  - **Corrected:**
    - §2.6: the repository's `@category:` tags are a test fixture's, not the examples';
    - §4.8: a parameterized group draws no tags today;
    - F22 and §4.13: shards merge a feature's tags by intersection. The first draft's union would have lifted an
      NDJSON shard's shared tag onto another shard's scenarios;
    - §7's port-plan lines;
    - the wiki lines, now at `23edeca`, where `Querying-Reports.md` moved.
  - **`v5` reached `05be4469`** (S11b), and none of its five new commits touched a source file cited.
- **2026-10-19, execution begins.** The owner asked for the plan "in full", without interfering with the other
  sessions. So §9's recommendations are taken, Q1's among them (nothing ships on `main`).
  - **Where the work happens.** The `v5` steps are commits on a branch of their own, `v5-tags`, in the worktree
    `Kronikol-tags`. The session building `v5` (Option C) moves the `v5` ref, so `v5-tags` is rebased onto `v5`'s tip and
    that session fast-forwards `v5` to it at a stage boundary.
  - **`main` in `v5`.** T1 needs `main`'s 4.27.0 to 4.30.0 readers. The merge of `main` into `v5` is that session's, at
    the end of its stage S11; until then `v5-tags` stands on a private merge and moves onto the real one when it lands.
  - **Kept for the other `v5` work:** the `kron-filter-change` event and the `style.display` each scenario element
    carries, which the Timeline and Components tabs read.
- **2026-10-19, T2 built first** (`v5-tags`, `f077cda6`, fast-forwarded onto `v5` by the Option C session that night;
  it stands on `3fce16bb`, which holds `9bb77de6`, the merge of `main` 4.31.1).
  - **Why T2 first.** T1 removes `Scenario.Categories`, which takes the Categories row and its tests with it, so T1 would
    have carried T2; and T2's files are the ones Option C's S12 touches. T2 reads the model as it stands: a feature's
    labels, and a scenario's labels and categories, are one tag list (`ScenarioTags.Effective`, `OwnOf`). T1 then
    renames the model, and the report changes only where it reads it.
  - **§4.3, worded more exactly.** A chip's spelling is the first met "in the order the report's data lists features and
    their scenarios", not the order the page draws them in, which a sort changes.
  - **Q9, measured** (`TagRowVolumeTests`, explicit). At 1280 px the row painted 130 px for 40 tags, 198 px for 60,
    300 px for 100 and 1,150 px for 400 (1.44 screens of 800 px); at 390 px, 264 px for BreakfastProvider's 9, 600 px
    for 40, 852 px for 60 (a full screen of 844 px) and 5,640 px for 400. 400 tags take more than a screen, so the row
    is cut as §4.8 says: it starts with the 30 tags the most scenarios carry (`TagChips.MostShown`, about three lines
    at 1280 px), a tie going to the row's order, and a More chip after Untagged shows the rest. Happy Path and Untagged
    always show, and so does a chip that is set. Cut, the row paints 96 px at 1280 px and 474 px at 390 px for any
    count above 30.
  - **Found on the way, fixed in T2:** the violet theme's Tags row switch kept the default ground (its rule named the
    Categories row's switch); removing the Categories label from the 430 px container rule had taken the rule's
    declarations with it (caught by comparing every rule against `HEAD`, then pinned by a Playwright fact); Clear put
    focus on the row's first chip even when the long row starts without it.
  - **Found on the way, the Option C session's:** seven Playwright facts failed on `v5` itself (six AttachmentRenderingTests
    merged from `main` waited for 4.x's Expand All; ToolbarStylingTests' popup theory held 4.x colours since S11c),
    fixed by that session in `7200017e`, under T2.
  - **Proofs:** see the commit. Every C# and script mutation of the new code was killed.
- **2026-10-20, T1** (`v5-tags`, `8da2c182`).
  - **One rule for a scenario's own tags.** On every class-level lane and both Gherkin lanes, a scenario's tags are its
    framework's scenario-level tags less its feature's, under §4.3's fold, so a tag the scenario repeats (in another
    case, too) stays its feature's and is drawn once. The tests NDJSON lane is the one exception, as §4.7 says: the tags
    it lifts to the feature stay on the scenarios (F22).
  - **xUnit v2, one reader.** §4.7 named `TestCase.Traits` minus the class's for the results sink. Both of the lane's
    capture paths (the sink's `ITestStarting` and the attribute's `Before`) now read `TraitHelper.GetTraits` on the class,
    its base classes after it, and the method, through one helper (`CategoryTraits`), so a test is tagged the same
    whichever made its scenario. xUnit v2 reads no assembly-level trait, so Q12 does not reach it.
  - **Moved from T3 to T1:** CTRF's tags (effective, `extra.categories` gone, no retry filter), `Specifications.md`'s
    one `Tags:` line, `scenarios --json`'s `tags` and `--label` over effective tags. Each reads the renamed model, so it
    could not wait for T3. T3 keeps `--tag` (with `--label` as its alias), the merge by intersection (F22) and the skill.
  - **Also in T1:** `GenerateYamlSpecs` removed (F19); `ReqNRollConstants.CategoryTagPrefix` and the Cucumber importer's
    private prefixes removed (§4.2); the schema's descriptions say where tags come from (F1's schema text). The orphan
    `tests/TestTrackingDiagrams.Tests.EndToEnd/ReportTestHelper.cs` of F19 was already gone from `v5` (`b9872f72`).
  - **Found on the way, fixed in T1:** StepRenderingReportTests' feature-labels fact passed whatever the feature carried, its two words being in the page's scripts, and asserts the feature's badges now; FilterCombinationMode's doc named categories and gave the
    dependency filter 4.x's AND; the YAML writer's remark said these keys are required by the schema; the query
    scanner's interned key names lacked `tags`; TOOLBAR_AT_EVERY_WIDTH_PLAN's harness, built against `src/`, set `Labels`
    and `Categories`.
  - **Rebased onto `4e75ce61`** (D48, D49's control titles, the phone's second part, the keyboard links): one conflict,
    FilterRowsTests, where D49's title is kept; three of those commits' fixtures set `Tags`; and TagsMarkupTests'
    Untagged pin, which D49's titles had broken on `v5` itself, holds the title D49 writes. Also fixed in T1:
    `kronikol ingest`'s two retry diagnostics, which said a retried test carries a retry label for each earlier attempt.
  - **The full Playwright run's one failure** was ThemedSourceRendersTests' race with the page's zoom check, which leaves
    `style=""` on an svg a frame after it is drawn; fixed in the test (`34954884`). `main`'s copy has the same race.
  - **Proofs:** see the commit.
- **2026-10-20, T3** (`v5-tags`, `506b46f4`, on S13's `b7fa97d1`).
  - **`--tag`, with `--label` as its older name (Q2).** One option, `QueryOptions.Tag`; a listing cut short says `--tag`
    on its `next:` line whichever name was typed, and another verb refuses `--tag` as it refuses `--label`.
  - **F22, completed.** The merged feature keeps the tags every shard gives it, by intersection as §4.13 says, and each
    shard's other feature tags move onto that shard's scenarios, so no scenario's tags change in a merge. Intersection
    alone would have taken a class-level shard's feature tag off its scenarios wherever another shard's feature lacked it.
  - **Proofs:** 7 of the 55 cases red on T1; 13 of 13 mutations killed, one only after the three-shard fact gave each
    shard a spelling of its own (see the commit).
- **2026-10-20, T4** (`v5-tags`, `09a966e9`; the wiki and Kronikol4J as below).
  - **The schema's `attempt` text** said it matched "the retry N label in the HTML"; it names the badge T2 draws now
    (`09a966e9`, with a fact in TestRunReportSchemaContractTests).
  - **The wiki, as a patch for the release, not published:** 29 pages and a page of its own, "Migrating to v5: tags",
    against the live wiki at `450844e`, in `TAGS_PLAN.harness/wiki/` with its notes and the answers to its eight
    questions. `patch -p1` applies it to a checkout of `450844e`, and the links check clean (260 ok, 676 page-ok). The
    Option C session writes its own wiki edits over it, the Happy paths control's wording among them.
  - **The tagging recipes** of Marking-New-and-Changed-Tests.md were checked on a 5.0.0 build: a tests capture, a
    playwright-bdd run's Messages and a data file tagged by source location and by name each give
    `query scenarios --tag pr-` the tests marked and no others (`TAGS_PLAN.harness/wiki/recipes/`). jq is not installed
    on the machine, so a script of the same logic made the edits, and the page says so.
  - **Placed on `main`** as `47c250ec`: D40 and 1e.9, V5_PLAN's four pieces, CLAUDE.md's MAJOR line, and REPORT_FORMAT_PLAN
    §4.2, where neither `formatVersion` moves for the tags.
  - **Kronikol4J:** ported on Kronikol4J's branch `tags-5.0.0`, on its `origin/main` `1a62e28`, not pushed or released: `0bfe7cd`,
    with `6a90931`, a Spock fix found on the way (an inherited feature method was recorded under its abstract base
    spec). The data files match .NET 5.0.0's; the HTML keeps .NET 3.0.43's page with tags drawn where labels were, so
    one golden changes (`report-filterstoggles.html`, by three edits); the JUnit 5, TestNG, Spock and Cucumber-JVM
    adapters map tags for the first time; the merge follows F22; the ledger records it. `./gradlew build` passes, 1,015
    tests with 17 skipped; 68 of the first 84 facts failed red, and 115 of 116 mutations by hand are killed, the 116th
    showing a redundant check, since removed. Its wiki edits are a patch, `TAGS_PLAN.harness/wiki/kronikol4j-wiki.patch`
    (4 pages, against the Kronikol4J wiki at `ba1bd8d`). Open, for the owner: a class-level `endpoint:x` tag sets the
    endpoint in Java, where .NET's class-level lanes leave it out of the tags without setting one, against the
    classifier's rule that the first `endpoint:x` sets the endpoint.

## Appendix A. Edit sites

At `6f6043e2` (4.27.0) unless marked `v5:` (`a91d4b91`). Paths are under `src/` unless they start with `tests/` or
`plans/`.

### A.1 The model and the classifier (T1)

- `Kronikol/Reports/Scenario.cs:40-41`, `Feature.cs:12`.
- `Kronikol/Reports/ScenarioTags.cs:9-15`, `:26-30`, `:36-73`; `Kronikol/HappyPathDetection.cs:13-18`.
- `Kronikol.ReqNRoll.Core/ReqNRollConstants.cs:11-12`; `Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs:167-168`.

### A.2 The adapters and the ingest lanes (T1)

- **LightBDD:** `Kronikol.LightBDD.Core/FeatureResultExtensions.cs:29`, `:39`, `:47-48`, `:91-93`;
  `Kronikol.LightBDD.Core/HappyPathAttribute.cs:8-10`.
- **ReqNRoll:** `Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs:18-27`, `:42-51`, `:67-68`, `:93`;
  `ReqNRollTrackingHooks.cs:159-219` (add `FeatureInfo.Tags`).
- **BDDfy:** `Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs:17-31`, `:74`; `DiagramCapturingProcessor.cs:22`, `:74`.
- **NUnit 4:** `Kronikol.NUnit4/TestContextEnumerableExtensions.cs:19-20`, `:22-56`; `DiagrammedComponentTest.cs:23-31`;
  `UncapturedTests.cs:73`.
- **MSTest:** `Kronikol.MSTest/TestContextEnumerableExtensions.cs:17-47`; `DiagrammedComponentTest.cs:33-69`,
  `:103-114`.
- **xUnit v3:** `Kronikol.xUnit3/TestContextEnumerableExtensions.cs:20-67`; `DiagrammedComponentTest.cs:30`, `:37-42`;
  `CaptureTestArgumentsAttribute.cs:34-39`.
- **xUnit v2:** `Kronikol.xUnit2/ScenarioInfoCollectionExtensions.cs:18-43`; `KronikolResultsSink.cs:95-96`,
  `:110-136`; `TestTrackingAttribute.cs:27-75`.
- **TUnit:** `Kronikol.TUnit/TestContextEnumerableExtensions.cs:18`, `:24-60`; `DiagrammedComponentTest.cs:17-22`;
  `HappyPathAttribute.cs:8-10`.
- **Tests NDJSON:** `Kronikol/Ingestion/FeatureSynthesizer.cs:180-182`, `:210-211`, `:229`, `:258`, `:279-293`.
- **Cucumber:** `Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs:692-711`, `:720`, `:729-730`, `:1133-1147`;
  `GherkinIndex.cs:15-26`.

### A.3 The writers (T1)

All in `Kronikol/Reports/ReportGenerator.cs` unless named:
- JSON `:4774`, `:4790-4791`; XML `:5321`, `:5340-5341`; YAML `:5614`, `:5647-5648` (and `AppendYamlSequence`
  `:5555-5571`).
- `Specifications.yml` `:5916-5943`, `.json` `:5979-5985`, `.xml` `:6013-6020`; the unused `GenerateYamlSpecs`
  `:2594-2621`.
- JSON schema `:6547`, `:6554`, `:6562`, `:6577-6578`; XSD `:7096-7108`, `:7192-7197`.
- `SpecificationsMarkdownGenerator.cs:57`, `:70-71`, `:123-130`.
- `CtrfReportGenerator.cs:42-43`, `:60-61`, `:157-161`, `:179-201`, `:279`, `:287`.

### A.4 The readers, the query tool and merge (T1, T3; Q1 (a) only on `main`)

- `Kronikol/Query/ReportScanner.cs:192-193`, `:312`, `:407-412`, `:450`, `:710-714`, `:882-883`;
  `ReportIndex.cs:124`, `:161-162`.
- `Kronikol/Query/QueryCommand.Overview.cs:276-277`, `:303-305`; `QueryOptions.cs:31`, `:174`, `:267`, `:476`;
  `VerbTable.cs:65`, `:132-135`; `QueryCommand.cs:300`.
- `Kronikol/Reports/Merge/MergeableReportReader.cs:109`, `:234-235`, `:728-734`; `MergeableReportMerger.cs:134`.
- `Kronikol.Tool/CtrfCommand.cs:147`, `:153`, `:181-185`.
- `Kronikol/Reports/ReportToggleDefaults.cs:71-72`; `ReportToggleDefaultsResolver.cs:32`, `:85`;
  `Merge/MergeableReportRenderer.cs:99`; `ReportConfigurationOptions.cs:583`, `:590`.
- The skill: `templates/skills/kronikol-test-debugging/references/commands.md:53`, `:121`, and the `.claude/skills/`
  copy.

### A.5 The report on `v5` (T2)

All in `v5:src/Kronikol/Reports/` unless named:
- **`ReportGenerator.cs`:** rows `:1842-1855` (Tags), `:1870-1887` (Categories); `catModeText` `:1394`, `:1398-1399`;
  helpers `:2786-2820`; attributes `:2301-2307`, `:3218-3222`; badges `:2160`, `:2310-2314`, `:2338`; search help
  `:1771`, `:1783`.
- **`report-filter-chips-script.js`:** `:6-13`, `:62-80`, `:92-96`; `report-category-filter-function.js` (deleted).
- **`report-url-hash-function.js`:** `:66-91`, `:130-170`.
- **Search:** `report-search-function.js:146-155`, `:211-251`, `:296-396`; `advanced-search.js:85-96`, `:267-268`;
  `report-search-index.js:470-474`.
- **Other scripts:** `report-scenario-feature-map-helper.js:22`, `:39`; `report-export-function.js:17-28`;
  `report-filter-bar-script.js:58-66`.
- **CSS:** `stylesheets.css:490-549`, `:2150-2158`; `v5:src/Kronikol/Constants/Stylesheets.cs:66-86` (violet).
- **Toggle defaults:** `v5:src/Kronikol/Reports/ReportToggleDefaults.cs:93-94`; `ReportToggleDefaultsResolver.cs:34`,
  `:89`.

### A.6 Kronikol4J (T4)

`kronikol4j-report/src/main/java/io/kronikol/report/model/Scenario.java:21-27`, `:59-62`, `:81-82`, `:102-103`;
`Feature.java:9-15`; the adapters named in §4.14; `ReportDataSerializer.java:68`, `:96-97`, `:430`, `:457-458`,
`:664`, `:685-686`; `SpecificationsData.java:62-80`, `:133-146`, `:216-237`; `DotNetHtmlReportRenderer.java:797-808`,
`:943-946`; `MergeableReportMerger.java:87`, `:92`; `RunResults.java:44`. Kronikol4J `main` is at `92d6b28`.
