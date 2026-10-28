# Titles as written plan (#139)

**Status:** draft, written 2026-10-27 at 5.12.0 (`1ee6373a`) by kronikol-50, at the owner's ask to analyse #139
critically and plan a fix for the underlying problem, which may differ from the one the issue proposes. Revised the
same day at 5.14.0 (`8d7d1e1d`) after five deep dives (section 10). **Not green-lit**: section 9's questions are the
owner's.

**Issue:** [#139](https://github.com/lemonlion/Kronikol/issues/139), "Generated feature and scenario names lowercase
brand names and acronyms; let a suite list the words to keep", filed 2026-10-07 against 4.6.0 from an integration of
Kronikol into a .NET 8 multi-host suite. Related: [#110](https://github.com/lemonlion/Kronikol/issues/110) (a Reqnroll
feature title Titleized) and [#102](https://github.com/lemonlion/Kronikol/issues/102) (an outline heading put through the
method-name humaniser), both still open: #102's ask and #110's first ask shipped in 5.9.1, #110's second did not
(section 3.8).

**Harness:** `plans/TITLES_AS_WRITTEN_PLAN.harness/`, described in its `README.md`. `harvest.py` reads the test names of
three corpora from source (13,342 tests: BreakfastProvider's 864, Kronikol's own 12,358, its examples' 120); `probe.cs`
runs 5.12.0's own formatters over them through `#:project`, beside two candidate rules and a prototype of the word list.
`examples.txt`, `corpus.txt` and `words.txt` are its output. Windows 11, .NET SDK 10.0.300.

Line numbers are to `1ee6373a` (5.12.0); read a file there with `git show 1ee6373a:<path>`. Six releases followed
while the plan was written. None changed the title, id or adapter code this plan changes, but some cited files moved:
5.12.1 (`79e8d644`) `CHANGELOG.md`; 5.13.0 (`2b74a2e4`) `HistoryAnalyzer.cs`, `HistoryRunContext.cs`,
`ReportConfigurationOptions.cs`, one line of `ReportGenerator.cs` and `CHANGELOG.md`; 5.14.0 (`8d7d1e1d`)
`ReportGenerator.cs`, the `Merge/` files (the feature pairing of `MergeableReportMerger.cs:117-143` is `:183-209` there,
unchanged), `ReportConfigurationOptions.cs` and `HistoryRunContext.cs`; 5.15.0 (`6bbc2ce1`) `ReportGenerator.cs` and
one line of `MergeableReportReader.cs`, below the one cited; 5.15.1 (`5ac5022f`) and 5.16.0 (`15a95d4c`) none.
`ScenarioTitleResolver.cs` has not changed since 4.16.3
(`65b43fe4`), so the behaviour described is the issue's too, apart from 4.11.1's dot and parenthesis fixes, 4.14.11's
xUnit display name reading and 4.16.3's culture-invariant casing. Wiki lines are on `origin/master` `ae3393f`; no
later wiki commit touched a line cited, but 5.14.0's (`183dbb0`) moved `Report-Configuration.md`'s by 3 and
`Generated-Reports.md`'s by 1, and 5.15.0's and 5.15.1's (`5d037bd`, `e10a398`) moved
`Ingesting-External-Captures.md`'s by 4.

## 1. Summary

The issue's diagnosis is right as far as it goes, and its examples are exactly the case a casing rule cannot fix: an
acronym written in Pascal case, as the .NET naming guidelines ask (`Api`, `Mca`), and a brand written as one Pascal word
(`AcmePay`). Nothing in `WhenMcaFundingActivates` says that `Mca` is an acronym. For those words a list is the right
tool.

But the list is not the underlying problem, and on its own it would leave most of the damage in place. Measured at 5.12.0:

1. **The scenario humaniser lower-cases every word after the first** (`ScenarioTitleResolver.cs:165-175`), including
   words the identifier writes in capitals. `WhenMCAFundingActivates_TheLOCIsDrawn` reads "When mca funding activates the
   loc is drawn", `Given_the_UK_store_when_I_pay_then_VAT_is_added` reads "Given the uk store when i pay then vat is
   added". In Kronikol's own tests 335 test rows (305 names) write a word in capitals after the first and all 335
   lose it, 43 of them only for an interface's `I` (`Implements_ITrackingComponent`); 40 more lose a first word in
   capitals (`HTTP_service_uses_participant_shape` reads "Http service uses participant shape"). In BreakfastProvider,
   5 of 5, one title in five methods (`The_Scalar_UI_endpoint...` reads "The scalar ui endpoint"). The feature
   humaniser keeps those words (`MCAFundingTests` reads "MCA Funding Tests"), so a feature and its scenarios disagree.
2. **A name a person wrote goes through it too.** On xUnit v2 and v3, TUnit, NUnit and MSTest, the frameworks' own
   display names (`[Fact(DisplayName = ...)]`, `[DisplayName]`, `TestName`, `DisplayName`) are split and lower-cased like
   method names: "Orders are sent to Kafka and stored in MongoDB" reads "Orders are sent to kafka and stored in mongo
   db", and "AcmePay rejects a webhook" reads "Acme pay rejects a webhook". A written name is also parsed as a method:
   one that ends in a parenthesis loses it to an argument list ("Returns 404 (not found)" reads "Returns 404 [not
   found]"), and one with no space, or an xUnit v3 row's own name, is cut at its last dot ("Order v1.2 ships" reads "2
   ships"). So writing the title through the framework's attribute does not work on any of those lanes. (The issue says
   its suite sets its titles and runs them through a glossary, not how; see section 2.)
3. **The same rule runs twice on an outline heading** (`ParameterGrouper.cs:54`), and on the LightBDD, BDDfy and
   ingest lanes it lowers a heading whose rows are never humanised (a LightBDD outline whose rows read "VISA" is headed
   "visa"). **A copy of it** names woven steps (`StepWeaver.cs:635-654`), which the wiki documents as turning
   `GetHTTPResponse` into "Get http response". Digits glue to the next word in both (`Returns404WhenMissing` reads
   "Returns404when missing"): 56 of Kronikol's test names, none of BreakfastProvider's.
4. **Every title is an id.** A scenario's stable id hashes its feature's name and its own (`ScenarioStableId.cs:37-76`),
   and the theory rows' outline id is cut from the humanised name. Any change to a title, from a fix or from a list,
   gives the scenario a new id: its history, quarantine entry, `diff --baseline`, CTRF continuity and `#sid-` links go,
   and a rename of more than a tenth of a suite marks every later run partial for weeks (section 3.6). 5.9.1 solved
   this for feature names (a frozen `TitleizeAsOf590`, `Feature.StableIdName`) on the Reqnroll, BDDfy, LightBDD and
   NUnit lanes; xUnit v2 and v3, TUnit and MSTest record no feature key, and a scenario has a recorded key only as an
   outline row named from its values (`KeyedByOutline`, 5.4.3).

The plan fixes the cause first and adds the list second:

- **R1, a patch** (its number is in section 5). One sentence-case rule for every title Kronikol makes from an identifier:
  split as today, plus between a digit and a capital that starts a word; keep any word in capitals (a plural such as
  `URLs` with it) and any one-letter capital but `A`; read a name with no lower-case letter as a sentence; lower-case
  the rest. A name the test wrote is shown as written, brackets and dots included. Outline headings come from the shown
  names. The step weaver takes the same rule. No id changes: each lane records the name 5.12.0 would have hashed, as
  5.9.1 does for features, and reads the outline and the row's values from it. Measured, the rule changes 5 of
  BreakfastProvider's 864 titles (the 5 above, case only) and 468 of Kronikol's 12,358 (413 case only): about two
  thirds plain corrections, the rest interface and product names it gets half right ("I tracking component", "Light
  BDD"), which R2's list finishes. R1 also fixes the bugs of section 3.10.
- **R2, a minor.** `ReportConfigurationOptions.TitleWords`, the issue's list: each entry is how
  a word is spelled, and it replaces any run of whole words whose letters are its own, ignoring case and spaces, in the
  titles Kronikol generated. So `AcmePay` fixes "Acme Pay", `gRPC` fixes both "Grpc" and "g RPC", and `ClickHouse` fixes
  "Click House". It never moves an id, never touches a name a person wrote, and runs in one pass that every lane goes
  through. The prototype fixes every example in the issue and the 14 BreakfastProvider feature names that hold one of
  its 46 words; at least eight more (`ClickHouse`, `EventGrid`, `Xss`, `Tsv`) need entries of their own.

Also found: #102 and #110's first ask shipped in 5.9.1 and both are still open, while #110's second ask did not ship
(Q7); xUnit v2's collection-fixture path names a scenario from its method alone, ignoring its DisplayName (F4); three
sorts compare titles with case, one of them a bug today, and the history ledger keeps a scenario's old title for as
long as its id is kept (F9); the bugs R1 fixes on the way are listed in section 3.10.

## 2. What the issue says, checked

| The issue says | At 5.12.0 | Verdict |
|---|---|---|
| `FormatFeatureName` splits brand names: `AcmePayWebhookTests` reads "Acme Pay Webhook Tests" | Same (`examples.txt`). It also splits product names written as one word: `PostgreSQLStoreTests` "Postgre SQL Store Tests", `GraphQLTests` "Graph QL Tests", `OAuth2LoginTests` "O Auth 2 Login Tests" | True, and wider |
| `FormatScenarioDisplayName` writes sentence case, so acronyms are lowered: "via api journey", "When mca funding activates" | Same. The examples are written `Api` and `Mca`, so they cannot show the larger loss: a word written in capitals is lowered too (`ViaAPIJourney` reads "via api journey") | True, and wider (F1) |
| "We set the titles ourselves and run them through a small glossary afterwards" | The issue does not say how. At 4.6.0 a DisplayName never reached the report (xUnit v2 paired a result with its scenario by method, `ReportingTestFramework.cs:133-149` at `a074cf14`, and named it from the method, `TestTrackingAttribute.cs:35-36`), so the suite perhaps renames the model it passes to a public `CreateStandardReportsWithDiagrams` overload (`XUnit2ReportGenerator.cs:37`, `ReportGenerator.cs:155`), whose names are used as given (`ScenarioInfoCollectionExtensions.cs:21`, `:37`; only `CapitaliseTitles` touches them). Since 4.14.11 a DisplayName does reach the report, split and lowered on every lane that reads one | Plausible. The attribute route is closed at 5.12.0 (F2); renaming the model works |
| A list of words "applied wherever titles are generated (xUnit v2/v3, NUnit, MSTest, TUnit, and the Reqnroll titles in #110)" | 5.9.1 shows a Gherkin title as written on both Gherkin lanes, with ids kept, so Reqnroll's titles need no list. BDDfy and LightBDD generate titles too and are not in the issue's list | Right lanes, less one, plus two |
| `ReportConfigurationOptions.TitleWords = ["AcmePay", "API", "MCA"]` | Works for words an identifier cannot carry (section 4.5). Alone, it leaves F1 to F4, makes a suite list every acronym it already writes in capitals, and moves every renamed scenario's id unless the key is recorded (F6) | Needed, not sufficient |

## 3. Findings

### 3.1 F1: the scenario humaniser lowers every word but the first

`ScenarioTitleResolver.Humanize` (`ScenarioTitleResolver.cs:165-175`) splits at a lower-case letter before a capital and
inside a run of capitals (`SplitPascalCase`, `:177-182`), turns underscores into spaces, then upper-cases the first
character and lower-cases all the rest. It names every scenario on xUnit v2 (both paths), xUnit v3, TUnit, NUnit and
MSTest, the BDDfy fallback when a scenario's title is its test class's name (`ResolveScenarioTitle`, `:14-24`, called
from `DiagramCapturingProcessor.cs:49-50`), and the heading of every parameterised group that is not Gherkin.

| Corpus (`corpus.txt`) | Tests | Names writing a word in capitals | Of them lowered |
|---|---|---|---|
| BreakfastProvider (all underscore names) | 864 | 5 | 5 |
| Kronikol's own tests | 12,358 | 335 | 335 |
| Kronikol's examples | 120 | 0 | 0 |

"A word in capitals" is read from the identifier as 5.12.0 splits it: two or more capitals after the first word, or a
lone `I`. The counts are of test rows: Kronikol's 335 are 305 distinct names, and 43 of them write only an interface's
`I` (`Implements_ITrackingComponent` 30 times, `IHttpContextAccessor`, `IDatabase`, `IMessage`, `IPv6`); no test in any
corpus uses the pronoun. The probe reads from the second word on, so 40 names whose only capitals are their first word
are outside the 335, though 5.12.0 lowers them too. BreakfastProvider's 5 are one title in five methods
(`The_Scalar_UI_endpoint_should_return_a_valid_page` four times and a Title_Case copy). Samples:
`Response_Has_OK_StatusCode` reads "Response has ok status code", `A_port_the_OS_offers_twice...` "A port the os
offers twice...", `The_error_is_an_IOException...` "The error is an io exception..."; of the 40,
`JSON_that_is_not_whole...` "Json that is not whole..."; and from `examples.txt`, `WhenIOpenTheApp` "When i open the
app".

The feature humaniser is a port of Humanizer (`StringCasing.cs`) and keeps a word in capitals, so `MCAFundingTests`
reads "MCA Funding Tests" above scenarios reading "mca".

### 3.2 F2: a name a person wrote is humanised as if it were a method

| Lane | What reaches the humaniser | Site |
|---|---|---|
| xUnit v3 | the test's display name and its explicit `DisplayName`; the DisplayName branch still ends in `Humanize` (`WithArguments`, `:131-143`) | `Kronikol.xUnit3/TestContextEnumerableExtensions.cs:35` |
| xUnit v2, results sink | the same | `Kronikol.xUnit2/KronikolResultsSink.cs:125-126` |
| TUnit | `Metadata.DisplayName`, a `[DisplayName]` or a `DisplayNameFormatter` result included | `Kronikol.TUnit/TestContextEnumerableExtensions.cs:35` |
| NUnit | `Test.Name`, which is a `TestName` when one is set | `Kronikol.NUnit4/TestContextEnumerableExtensions.cs:38` |
| MSTest | `TestDisplayName ?? TestMethodName`, a `[TestMethod(DisplayName = ...)]` or `DataRow(DisplayName = ...)` included | `Kronikol.MSTest/TestContextEnumerableExtensions.cs:31` |

What a written name becomes (`examples.txt`, the same on every lane):

| Written | Shown at 5.12.0 |
|---|---|
| Order API returns 404 for a missing SKU | Order api returns 404 for a missing sku |
| Orders are sent to Kafka and stored in MongoDB | Orders are sent to kafka and stored in mongo db |
| AcmePay rejects a webhook without a signature | Acme pay rejects a webhook without a signature |
| When MCA funding activates, the LOC is drawn | When mca funding activates, the loc is drawn |
| iOS users see the new checkout | I os users see the new checkout |

Kronikol's own fixture for dotted DisplayNames (`DisplayNameDots.DottedVersion`, "Order API returns 404 for v1.2")
shows "Order api returns 404 for v1.2". 4.11.1 fixed that fixture's dots and recorded "casing is left as it is (#139)"
(`CHANGELOG.md:7480`).

A written name is also read as a method. On TUnit, NUnit and MSTest it goes through `FormatScenarioDisplayName`
(`:70-97`): a name that ends in `)` is split into a method and arguments (`:75-86`), so "Returns 404 (not found)" reads
"Returns 404 [not found]" and its group grows an `Arg 0` column, and a name with no space is cut at its last dot
(`MethodName`, `:149-159`). On xUnit v3, a row's own name (`TestDisplayName` on a data attribute or a `TheoryDataRow`)
and a row's `Label` reach the branch for xUnit's default names, which keeps only the text after the last dot or comma
(`XunitMethodPart`, `:120-129`): a row named "Order v1.2 ships" reads "2 ships" (section 3.10). Only a `[Fact]` or
`[Theory]` DisplayName keeps its own dots and parentheses (`FormatXunitDisplayName`, `:105-114`).

Written names are rare in the three corpora (5 of 13,342), so this matters less for how often it happens than for what
it leaves a suite: there is no way to put a title in front of a reader as written through these five lanes'
attributes, where the Gherkin lanes have shown titles as written since 5.9.1. Renaming the model a suite passes to a
public `CreateStandardReportsWithDiagrams` overload does work (section 2).

### 3.3 F3: two rules in three places, and digits glued

| Where | Used for | Digits |
|---|---|---|
| `ScenarioTitleResolver.Humanize` (`:165-182`) | scenario titles, non-Gherkin group headings, BDDfy's fallback | glued: `Returns404WhenMissing` "Returns404when missing", `Retries3TimesThenFails` "Retries3times then fails" |
| `StepWeaver.HumanizeMethodName` (`Kronikol.StepTracking/StepWeaver.cs:635-654`) | woven step text with no `Description`, at the consumer's build | the same code, line for line, so the same loss: the wiki's own table has `GetHTTPResponse` as "Get http response" (`Step-Tracking.md:164-176`) |
| `StringCasing.Titleize` (`StringCasing.cs:43-55`), the other rule | feature names, BDDfy stories, LightBDD features, NUnit fixtures, parameter column headers, a string response status | split: `FormatFeatureName("Returns404WhenMissing")` reads "Returns 404 When Missing" |

Names where a digit is glued to the capital after it, the defect: none of BreakfastProvider's 864, 56 of Kronikol's
12,358. (Every digit against a letter, `p95`, `utf8` and `Base64` included, which are right as they are: 4 and 381.)

The weaver targets netstandard2.0 (`Kronikol.StepTracking.csproj:4`) and references no Kronikol assembly, so it cannot
call the core's rule; it needs the same source linked in, written for netstandard2.0 (section 4.1).

### 3.4 F4: headings read a title twice, and xUnit v2's two paths disagree

- **The heading is humanised again.** On xUnit v3, TUnit, NUnit and MSTest, a theory row's `OutlineId` is cut from its
  already humanised name (`ParameterParser.ExtractBaseName(displayName)`: xUnit v3 `:67`, TUnit `:56`, NUnit `:59`,
  MSTest `:51`; xUnit v2 `ScenarioInfoCollectionExtensions.cs:50`), and `ParameterGrouper.cs:54` humanises it again for
  the heading. With a rule that keeps written text, the second pass would lower a written name's "Kafka" again, and it
  reads a trailing parenthesis as arguments again ("Returns 404 (not found)" is headed "Returns 404 [not found]").
- **It lowers headings whose rows are never humanised.** LightBDD (`OutlineId` from its name format,
  `FeatureResultExtensions.cs:72`, `:81`), BDDfy (the raw method name, `ScenarioInfoExtensions.cs:42`, `:50`) and the
  tests-NDJSON ingest lane (`FeatureSynthesizer.cs:209`) set an `OutlineId` without `NameAsWritten`, so
  `ParameterGrouper.cs:54` humanises their headings: a LightBDD outline whose rows read "Pays with VISA" is headed "Pays
  with visa".
- **A structured row's heading is its method's.** Where the adapter reads the arguments themselves, `OutlineId` is the
  method's name (`GetStructuredOutlineId`, xUnit v3 `:125-130`; TUnit and NUnit the same), so a theory that writes
  `DisplayName = "Orders ship to the UK"` has rows named after it under a heading made from `ShipsToTheUK`.
- **xUnit v2's collection-fixture path names a scenario from its method alone** (`TestTrackingAttribute.cs:50-51`): no
  DisplayName and no arguments, where the sink path (`KronikolResultsSink.cs:125`) reads both. The sink's name replaces
  the attribute's when the sink pairs the test (`KronikolResultsSink.cs:222`), so the method-only name is what a report
  shows exactly when the sink pairs nothing: the collection-fixture path the wiki documents.

### 3.5 F5: the words an identifier cannot carry

The .NET guidelines write an acronym of three or more letters in Pascal case (`ApiClient`, `HtmlTag`; section 4.6 has
the rule), and a brand or product written as one word is split at its capitals. Nothing in the identifier tells `Api`
the acronym from `Api` the word, or `AcmePay` the brand from "Acme Pay". These are the issue's examples, and only a list
or a written name fixes them.

| Corpus | Scenario names with a Pascal acronym | Feature names with one |
|---|---|---|
| BreakfastProvider | 13 of 864 | 14 of 148 |
| Kronikol's own tests | 474 of 12,358 | 91 of 1,113 |
| Kronikol's examples | 1 of 120 | 0 of 24 |

The acronym list in `probe.cs` has 46 entries (`Api`, `Http`, `Json`, `Sql`, `Url`...), so these are rough counts:
brand and product names are not in it, and an entry such as `Vat` can be the plain word. The 148 feature names also
hold six things the harvest took for class names (`_getSteps`, `await`, `exists`...). BreakfastProvider's affected
features read "Grpc Order Status Tests", "Specifications Open Api Tests", "Specifications Async Api Feature", "Click
House Sql Translator Tests" and "Duck Db Click House Query Engine Tests"; at least eight more need a word the list
lacks (`ClickHouseConformanceTests`, `InMemoryClickHouseHandlerTests`, `Reporting_EventGrid_Webhook_Tests`,
`Toppings_Xss_Validation_Tests`, `TsvWriterTests`...).

### 3.6 F6: a title is an id

`ScenarioStableId.Compute` (`ScenarioStableId.cs:37-57`) hashes the suite, the feature's key, the outline id, the
scenario's key name and the example values. Since 5.9.1 every reader of a report goes through three helpers (the CTRF
and Allure history importers call `Compute` on the names they import, `HistoryCommand.Maintenance.cs:547`, `:613`,
which this plan does not touch): `KeyName` (`:65-66`,
the display name, or the outline title for a Reqnroll row named from its values), `FeatureKey` (`:76`,
`Feature.StableIdName ?? DisplayName`) and `For` (`:69-80`). Their callers are the history run builder, the CTRF writer,
`Failures.md`, the labs page, the merger, the console summary, the Specifications Markdown and 17 sites in the report
generator (anchors, order, the timeline, the data files' `stableId` in JSON, XML and YAML).

So any change to a scenario's title gives it a new id unless its key is recorded, and the outline id carries the
humanised name into the hash a second time. 5.9.1's N3 (`plans/REPORT_UI_FIXES_PLAN.md` section 4.4, Q5 "keep the ids
on every lane") recorded the key for features only, because only feature names changed.

What a new id costs, beyond the history of the scenario itself:

- **The runs are recorded as partial, for weeks.** `HistoryAnalyzer.IsPartial` (`HistoryAnalyzer.cs:209-216`) calls a
  run partial when more than `HistoryPartialThreshold` (0.10, `ReportConfigurationOptions.cs:830`) of the last full
  run's ids are missing, and compares raw ids, not aliases. The last full run is found by `LastFull` (`:80-81`,
  `:196-202`), which skips partial runs. So once a fix renames more than a tenth of a suite, every later run is
  recorded as partial (`HistoryRunContext.cs:146-151`) for as long as the last full run before the rename stays in
  `HistoryWindow` (50 runs by default, `ReportConfigurationOptions.cs:722`), and nothing is "fixed" against a partial
  run.
- **No rename is suggested.** `SuggestRenames` (`HistoryCommand.Maintenance.cs:301-311`) pairs a gone id with an arrived
  one by feature and name, so a scenario whose name changed is never offered to `--accept-renames`.
- **A merge cannot help.** `MergeableReportReader` recomputes every id from the names it reads and ignores the stored
  `stableId`, so only a recorded key in the data file keeps a merged report's ids.

Order and anchors follow the key too: the report orders scenarios by `KeyName` (`ReportGenerator.cs:2346`, `:6769`,
`:6817`, `:6851`, `SpecificationsMarkdownGenerator.cs:106`) and anchors them by it (`:2154`, `:3968`), so a recorded key
keeps them; the data model's own order on xUnit v2 and BDDfy is by shown title (F9).
Features are ordered by display name (`:5537`), and a group's anchor is its heading (`:3772`). An anchor is a
lower-cased slug (`GenerateScenarioAnchorId`, `:5082-5087`), so a change of case alone moves none.

### 3.7 F7: no lane can be told how a word is spelled

There is no option, hook or attribute that rewrites a generated title. The step attributes have `Description`, and
`CapitaliseTitles` (on by default, `ReportConfigurationOptions.cs:678`) only upper-cases a first letter (`Capitalise`,
`StepText.cs:63-87`). It leaves a first word with a capital inside it alone (`IsCamelCaseWord`, `:95-107`), so `gRPC`
or `iOS` survive it; an all-lower-case first word does not, so a written "npm audit passes" reads "Npm audit passes".
A suite's recourse is to rename the model it passes to a public `CreateStandardReportsWithDiagrams` overload (section
2), or to post-process the report.

### 3.8 F8: #110 and #102 are done, but for one ask, and still open

- #110 asks for two things: a Reqnroll feature title as written, with its scenarios' ids kept, and to "keep
  humanising a title that is a single identifier" (`Feature: BasketCheckout` reading "Basket Checkout"). 5.9.1 shipped
  the first (`ScenarioInfoEnumerableExtensions.cs:83-88`: `DisplayName = featureGroup.Key`, `StableIdName =
  TitleizeAsOf590(...)`; `CHANGELOG.md:342-351`). It shows every title as written, a single identifier included, so
  `Feature: BasketCheckout` now reads "BasketCheckout"; `REPORT_UI_FIXES_PLAN.md` never mentions the second ask.
- #102 asks for an outline heading as written, the copy button's value with it, and no throw. 4.11.1 stopped the throw
  and the cut at the last dot; 5.9.1 heads a Gherkin outline with its title as written (`ParameterGrouper.cs:54`,
  `NameAsWritten`), and the group's copy button carries its heading (`data-scenario-name="{encodedGroupName}"`,
  `ReportGenerator.cs:3779`).

Neither issue was closed: 5.9.1's changelog cites #102 in passing and #110 not at all, and its commit names neither. Q7
asks whether to close both with a comment naming the releases, and what to do about #110's second ask.

### 3.9 F9: three sorts read case, two lanes order by the shown title, and the ledger keeps old names

- **Sorts with case.** The Search tab's sort by name compares with JavaScript's `<`
  (`report-search-results-script.js:153-156`), so it orders by code unit: "Get HTTP response" before "Get a list", and a
  title that starts in lower case ("gRPC...") after every capital. The Features list's sort already ignores case
  (`localeCompare` at base sensitivity, `report-features-tools-script.js:70`), so the Search tab's is a bug today
  (3.10). The History tab and the labs page sort by feature and then name with `StringComparer.Ordinal`
  (`History/HistoryHtml.cs:225`), and `kronikol merge` orders features the same way (`MergeableReportMerger.cs:143`),
  which also hands out the `-2` suffix of two scenario anchors that clash across features
  (`ReportGenerator.cs:2151-2163`). A change of case moves entries in all three.
- **Two lanes order the model by the shown title.** xUnit v2 (`Kronikol.xUnit2/ScenarioInfoCollectionExtensions.cs:26`)
  and BDDfy (`ScenarioInfoExtensions.cs:22`) sort scenarios by their names before the report sees them, which sets the
  data files' order, the `sN` ordinals, the roster's order and hash, and BDDfy's ` [n]` duplicate numbering
  (`:114-131`). The culture comparer ignores a change of case at its first level, so R1's case changes alone move
  nothing, but a split digit or a written name shown as written can, and BDDfy's numbering can swap two ids (4.4).
- **The ledger keeps old names.** A roster's hash covers the suite and the ids alone (`History/HistoryModel.cs:94-99`),
  a roster line is written only when no line with its hash is there (`HistoryLedgerWriter.cs:222-244`, and `:169-170`
  in `Amend`), and the reader keeps the last line for each hash (`HistoryLedger.cs:367`). With ids and order kept, a run
  after R1 reuses the roster line written before it, so `kronikol query history` without a report
  (`Query/QueryCommand.History.cs:186-201`), the absent and failing-outside lists (`HistoryAnalyzer.cs:107`, `:133`) and
  `history compact` show "Get http response" until the scenario set changes. Feature names have been stale the same
  way since 5.9.1.
- **Outside tools key by name.** The CTRF export's `name` and `suite` are the shown titles
  (`CtrfReportGenerator.cs:186`, `:189`). Kronikol's own `history import --from-ctrf` reads `extra.stableId`
  (`HistoryCommand.Maintenance.cs:544-547`), but a CTRF consumer elsewhere that matches tests across runs by name sees
  every retitled scenario as a new test once.

### 3.10 Bugs found on the way, fixed in R1

- **B1.** An xUnit v3 row's own name (`TestDisplayName`) or `Label` is cut at its last dot or comma and lower-cased
  (`XunitMethodPart`, `ScenarioTitleResolver.cs:120-129`): "Order v1.2 ships" reads "2 ships", and a label is read as
  arguments ("Ships [ [UK]]" with a DisplayName; lower-cased and cut at a dot without one). Read in the code, not yet
  run.
- **B2.** A written name on TUnit, NUnit or MSTest that ends in a parenthesis is split into a method and arguments, and
  one with no space is cut at its last dot (F2).
- **B3.** The Search tab's sort by name reads case (F9).
- **B4.** LightBDD, BDDfy and ingest outline headings are lowered although their rows are not (F4).
- **B5.** The ledger keeps a feature's pre-5.9.1 name, and would keep a scenario's pre-R1 one (F9): the writer also
  writes a roster line when one with its hash is there but its names differ, and the reader already keeps the last
  (Q12).
- **B6.** `kronikol merge` orders features, and the History tab and labs page order entries, with
  `StringComparer.Ordinal` (`MergeableReportMerger.cs:143`, `History/HistoryHtml.cs:225`), where a single report orders
  features with the culture comparer (`ReportGenerator.cs:5537`): a merged report can order its features differently
  from each shard's own. R1 gives both the report's comparer, and the Search tab's sort (B3) `localeCompare` at base
  sensitivity, as the Features list has.

### 3.11 Found on the way, not in this plan

- The assertion label formatter lower-cases method words too (`AssertionExpressionFormatter.cs`, `SplitPascalCase` then
  `ToLowerInvariant`, from `:59`). Its labels are #210's subject ("a call in the subject reads `to string( culture info
  invariant culture)`"), whose plan kronikol-be is writing in the worktree `Kronikol-210`; section 8 hands it over.
- A feature name keeps its class's `Tests` suffix ("Acme Pay Webhook Tests"). Nobody has asked for it to go, and it is
  not a defect, so it stays.

## 4. Design

### 4.1 The rule (R1)

One rule turns an identifier into a sentence, in one internal class (`Kronikol.Naming.SentenceCase`, say), used by
`ScenarioTitleResolver` and linked into the weaver as source. The weaver targets netstandard2.0, so the file is
written for it: `Regex` instances in static fields rather than `[GeneratedRegex]`, `Substring` rather than ranges. One
linked file is one rule, so it needs no parity fact.

1. **Split** where 5.12.0 splits (a lower-case letter before a capital; the last capital of a run before a capital and
   a lower-case letter), except that a run of two or more capitals and a lone `s`, with no lower-case letter after it,
   is a plural and stays whole (`URLs`, `IDs`, `VMs`; a final `Is` or `As` still splits, so `XIs` reads "X is"). Also
   split **between a digit and a capital that starts a word**: a capital before a lower-case letter, or two or more
   capitals with no lower-case letter after them (`404When`, `3Times`, `200OK`, `64MB`), so `E2E`, `P2P` and `F6F6F6`
   stay whole. Underscores and whitespace are spaces; runs of spaces are one.
2. **Case each word.** A word in capitals (its letters all upper case, and two or more of them or a digit among them:
   `API`, `UK`, `C4`, `R1`), its plural (`URLs`), and a one-letter capital other than `A` (`I`, `N`, `X`) keep their
   case. Every other word is lower-cased, and the first word then gets a capital.
3. **A name with no lower-case letter**, in two or more words, is a sentence written in capitals
   (`GIVEN_VALID_INPUT_WHEN_SAVED`): each word is cased as an ordinary word, as 5.12.0 does and as `FormatFeatureName`
   already does (`StringCasing.cs:123-124`). `HTTP_GET` reads "Http get", as in 5.12.0.

Nothing else moves: a run of letters and digits stays whole (`p95`, `utf8`, `Returns404`), and so do 5.12.0's
200-character cut of arguments and its reading of any whitespace as a space.

Measured over the three corpora. `probe.cs`'s `CandidateC` is rules 1 and 2 without the plural, the start-of-word
digit split and the one-letter capitals, and without rule 3; those four were measured on a verbatim copy of it
(section 10):

| Corpus | Titles that change | Of them, case only |
|---|---|---|
| BreakfastProvider | 5 of 864 | 5 |
| Kronikol's own tests | 468 of 12,358 | 413 |
| Kronikol's examples | 0 of 120 | 0 |

About two thirds of the changes are plain corrections: "The scalar UI endpoint", "Response has OK status code", "JSON
that is not whole", "An IO exception", "Implicit AND inserted", "uses C4 false", "render R1". The rest get a word half
right, no worse than 5.12.0 and closer to the source: interface names ("Implements I tracking component", 31 tests;
"resolves I http context accessor from DI") and product names split at their capitals ("Light BDD attachment",
"Graph QL named query", "Two g RPC hops", "N unit", "Req N roll"). Those are the list's work (R2), or Q9's. Two shapes
read wrong under every rule tried: a number glued to the word before it ("more than10 visible", 9 titles; the same
shape is right in about 165 others, `Net8`, `XUnit3`, `Resp3`) and "is A500" (5 titles in one class), so neither gets
a rule.

The plural rule and rule 3 change no title in the corpora, and the start-of-word digit split changes one
("F6F6F6" stays whole); none of the corpora holds a plural acronym, a name in capitals or an `E2E`. They rest on how
such names read without them, each worse than 5.12.0: `ParsesURLs` "Parses UR ls", `GIVEN_VALID_INPUT_WHEN_SAVED`
"GIVEN VALID INPUT WHEN SAVED", `E2ETestsPass` "E2 e tests pass". The one-letter capitals change 43 titles: 2 variables
that now read right ("first N scenarios") and 41 halves of product names ("N unit", "Req N roll"), no worse.

**Candidate B rejected.** The obvious alternative is the feature humaniser's own word reader in sentence case:
Kronikol's port of Humanizer (`StringCasing`), which sends an underscore name through its Pascal splitter. It changes
67 of BreakfastProvider's titles, and 63 of the 67 are wrong: it keeps a lone capital after a space, so every
Title_Case_Underscore name reads "should return A fresh batch". It also splits digits from the letters before them
("p 95", "utf 8", "x unit 3"). Humanizer itself (2.14.1 and 3.0.10, run from the packages) splits an underscore name at
its underscores only and never re-cases it, keeps a name in capitals as it is, and splits a plural acronym as
candidate C does ("Parses UR ls").

### 4.2 A name the test wrote is shown as written (R1)

Each adapter can tell whether the name in hand is one the test wrote. The test is the same on every lane: is this the
framework's default name for the method?

| Lane | Generated when | Otherwise written, and shown |
|---|---|---|
| xUnit v3, xUnit v2 sink | the display name is xUnit's default for the method: `Ns.Class.Method`, or `Method` under `methodDisplay: method`, with any `methodDisplayOptions` applied, then `(args)` | as written: a `[Fact]` or `[Theory]` DisplayName with a row's arguments after it in brackets, as today; a row's own `TestDisplayName`; a ` [label]` read as arguments (B1) |
| xUnit v2 fixture path | the method has no `DisplayName` (`TestTrackingAttribute` reads the attribute, as the sink does) | as written |
| TUnit | `Metadata.DisplayName` is the method's name (`TestDetails.MethodName`, `:87`), or starts with it and then `(` or `<` | as written: a `[DisplayName]`, a formatter's result, an `[Arguments(DisplayName)]` or `TestDataRow` name, with `$param` values filled in |
| NUnit | `Test.Name` is the method's name (`Test.MethodName`, `:129`), or starts with it and then `(` or `<`, once a leading `Namespace.Class.` is set aside (NUnit's `{c}`, `{C}`, `{M}`, `{n}` and `{i}` templates and the adapter's `DefaultTestNamePattern` put one there) | as written: a `TestName`, `SetName` or `SetArgDisplayNames` |
| MSTest | `TestDisplayName` is unset, is the method's name, or starts with it and then `(` or ` (`; a data row under `[TestMethod(DisplayName = "Adds numbers")]` is named "Adds numbers (1,2)", so that DisplayName, read through `TestMethodOf` (`DiagrammedComponentTest.cs:121`), is the written base and the rest its arguments | as written; a `DynamicDataDisplayName` result as written |

The boundary after the method's name matters both ways. On a method `Order`, "Orders are sent to Kafka" is written,
where a bare prefix test calls it generated; on `Login`, "Login via PayPal" is written, where a bare prefix test puts it
through 4.1's rule ("Login via pay pal"). A generated name on these lanes is the method's name and its arguments, and
4.1's rule runs on the method part only, as today.

A written name is shown as written: not split, not re-cased, not cut at a dot, and read for arguments only where the
lane says where they are (an xUnit theory row's, an MSTest data row's). It sets `Scenario.NameAsWritten`, so 5.9.1's
rule that a written name ending in brackets is not a parameter list (`ParameterGrouper.cs:68-72`) holds on these lanes
too, and the doc and schema text that say "a Gherkin scenario" (`Scenario.cs:108-114`, `ReportGenerator.cs:7599`) say
"a name the test wrote". Only `CapitaliseTitles` still touches it, as it touches a Gherkin title (4.7).

The adapters take the method name through a new internal overload, so `FormatScenarioDisplayName(string)` keeps its
signature (an optional parameter would be a binary break and new surface). Its output changes by the rule of 4.1: that
is the fix, a patch by the repo's rule. The flag set here, generated or written, is the one R2's list reads (4.5), so
it is R1's.

### 4.3 Headings (R1)

A group that is not Gherkin is headed by what its members share, never by humanising `OutlineId` again:

- When every member's shown name, less its arguments (`ParameterParser.ExtractBaseName`), is the same, that is the
  heading: a generated theory is headed by 4.1's title of its method, a written theory name heads its own group.
- When the members' names differ (TUnit `[Arguments(DisplayName)]` and `$param` names, NUnit per-case `TestName`,
  xUnit v3 per-row `TestDisplayName`; `ParameterizedGroupRenderTests.cs:127-130` already has rows "Happy path UK" and
  "Happy path US" under one outline), the heading is the method's own title: the theory's `DisplayName` if it has one,
  else 4.1's title of the method; on LightBDD, whose inline parameters keep rows apart (`Ordering "apple"`), its name
  format less placeholders as LightBDD wrote it (`FeatureResultExtensions.cs:67-72`); on ingest, the outline as the
  input wrote it. A first-member rule would head the first group "Happy path UK".

So no lane's heading is humanised a second time, and LightBDD, BDDfy and ingest headings read as their rows do (B4).

The method's title, where a heading needs it, is written to the data files as `outlineTitle` where it differs from
the shared base, as `stableIdName` is, so `kronikol merge` heads a group as the run did; a data file written before R1
has none, and the merge falls back to the shared base, then to 5.12.0's heading. A Gherkin outline keeps its title as
written, as in 5.9.1. A group's anchor follows its heading, as 5.9.1's Gherkin headings did, so a heading whose words
change gets a new anchor: a written theory name, a split digit, and the LightBDD, BDDfy and ingest headings no longer
lowered.

### 4.4 Ids are kept (R1)

5.9.1's N3, extended to scenarios:

- **A frozen copy.** `ScenarioTitleResolver` keeps today's rule as internal `...AsOf5160` copies of
  `FormatScenarioDisplayName`, `FormatXunitDisplayName` and `ResolveScenarioTitle`, never changed again, their comments
  saying why, as `TitleizeAsOf590` does. Like it (5.9.0's `Titleize`, frozen by 5.9.1), they take the number of the
  last release before the change: 5.16.0 as things stand, whose rule is 5.12.0's.
- **The recorded key.** `Scenario.StableIdName` (internal) holds the name 5.12.0 would have given, set by a lane only
  where it differs from the shown name, and `KeyName` reads `StableIdName ?? (KeyedByOutline ? OutlineId :
  DisplayName)`. Each path records what that path gave at 5.12.0: the xUnit v2 fixture path its method's name, the sink
  its display name. 5.12.0 hashed the name after `CapitaliseTitles` (`StepText.cs:158-172`, which capitalises
  `DisplayName`, `Rule` and `OutlineId`), so `ApplyToTitles` capitalises a recorded key exactly as it capitalises the
  name it stands for, and the key is what 5.12.0 hashed with the option on or off, a written name behind a marker
  (`- returns 404`, `StepText.cs:43`, `:247-257`) included.
- **The row's values.** The hash also takes `ExampleValues` (`ScenarioStableId.cs:47-53`), which a lane without
  structured arguments parses from the shown name (xUnit v3 `:41`, TUnit `:40`, NUnit `:43`, MSTest `:32`, xUnit v2
  `ScenarioInfoCollectionExtensions.cs:29`). 5.12.0 humanised a written name whole, bracket text included, so
  `DisplayName = "Ships to UK [EU]"` hashed `arg0=eu`, and shown as written it would hash `arg0=EU`. So where a key is
  recorded, the lane parses `ExampleValues` and `OutlineId` from the recorded name: one rule for both. A generated name
  kept its arguments raw at 5.12.0 (`ScenarioTitleResolver.cs:92-96`, `:131-143`), so for it nothing moves either way.
- **The outline key.** A row whose `OutlineId` is cut from its name cuts it from the recorded name (above), so the
  hash's outline part is 5.12.0's too. `OutlineId` is a key, not shown (the heading is 4.3's), so its text staying as
  5.12.0 wrote it is invisible in the HTML; the data files' `outlineId` (`ReportGenerator.cs:5564`) keeps it, as a key.
- **xUnit v2's two paths.** When the sink pairs a test with the scenario the attribute made, it copies the name across
  (`KronikolResultsSink.cs:222`). It copies the recorded key with it, so a paired theory row or DisplayName test keeps
  the sink's 5.12.0 key, not the fixture path's method-only one. The key rides on an internal property of the public
  `ScenarioInfo` (`src/Kronikol.xUnit2/ScenarioInfo.cs`), and the outline is cut from it, not from the shown name
  (`ScenarioInfoCollectionExtensions.cs:50`).
- **Model order.** xUnit v2 sorts its model by scenario name (`ScenarioInfoCollectionExtensions.cs:26`) and BDDfy by
  title (`ScenarioInfoExtensions.cs:22`); both sort by the recorded key, then the shown name, so the data files' order,
  the `sN` ordinals and the roster's order stay as they were (F9).
- **BDDfy's duplicate counter.** BDDfy suffixes a title that repeats in a story with ` [n]`, and the suffix is in the
  id (`ScenarioInfoExtensions.cs:114-131`). It numbers titles in the sorted order, where 5.12.0's equal titles ("Gets
  api" from `GetsApi` and `GetsAPI`) kept the collector's order, so the sort and the counter both run over the recorded
  keys, which travel on an internal property of the public `BDDfyScenarioInfo` from
  `DiagramCapturingProcessor.cs:49-56`. The shown titles take no suffix they no longer need; two that R1 makes alike
  ("Returns404When" and "Returns404_When", both "Returns404 when") show alike, their ids apart. Kronikol writes its
  title back into BDDfy's own `Scenario.Title`
  (`:58-62`), so BDDfy's own reports show R1's fallback titles too.
- **The data files.** `stableIdName` on a scenario, written only where it is set, with its schema entry and the merge
  reader, exactly as on a feature (`ReportGenerator.cs:5619-5620`, `MergeableReportReader.cs:124`), so `kronikol merge`
  computes the same ids. The schema's `stableId` text, "derived from feature name + scenario display name"
  (`ReportGenerator.cs:7591`), names the recorded key first.
- **Public callers.** `FormatScenarioDisplayName` and `ResolveScenarioTitle` are public (`API-Reference.md:33`). A
  consumer that builds its own `Feature[]` with them gets R1's titles and, with `StableIdName` internal, new ids for the
  scenarios whose titles change. Ids are kept on Kronikol's own lanes; the changelog says the rest.

The alternative is new ids, stated in the changelog with `kronikol history rename` as the way across. It renames one
scenario at a time, which #110 already measured as no answer for a suite (Q2).

### 4.5 The word list (R2)

- **The option.** `ReportConfigurationOptions.TitleWords`, a list of strings, empty by default. An entry is a word as
  the suite spells it: `AcmePay`, `API`, `gRPC`, `PostgreSQL`, `iOS`.
- **The match.** An entry matches a run of whole words in a title whose letters and digits, read without case, are the
  entry's own letters and digits. The run is replaced by the entry as written, and the last word's trailing punctuation
  stays (`UI,` keeps its comma). Longer entries are tried first, and at each place the longest run that matches wins.
  So `AcmePay` matches "Acme Pay", `gRPC` matches "Grpc" and "g RPC", `MongoDB` matches "Mongo DB" and "mongo db".
  Matching on letters rather than on the entry's own split is what makes `gRPC` work: split, it is "g RPC", while the
  class `Grpc_Order_Status_Tests` writes "Grpc".
- **Where.** One pass over the finished model, after `CapitaliseTitles` (`ReportGenerator.cs:248-258`, which runs it
  at `:254-255`), so every writer reads its result: HTML, the data files, Specifications, `Failures.md`, CTRF, the
  console and CI summaries. It rewrites the titles Kronikol generated: feature names on the lanes that make them from a
  class or a library's name, scenario names made by 4.1's rule, LightBDD's names (always from its method and class
  names), BDDfy's own titles, and the headings 4.3 makes from those. It rewrites a title's words, never its arguments,
  labels or filled-in values (`[EU]`, TUnit's `$param`, NUnit's `{0}`, LightBDD's inline values), which stay as the
  row's `ExampleValues` have them. BDDfy turns a method's name into a title and also takes one a test passes
  (`BDDfy("...")`, `[Story(Title)]`): the adapter calls a title BDDfy's own when it equals BDDfy's humanising of the
  method's name (`Configurator.Humanizer`), and leaves any other alone (Q11). The list mends BDDfy's own "When m c a
  funding activates" into "When MCA funding activates" (`words.txt`). The flag is 4.2's: Gherkin titles and written
  names are never marked, so they stay as written (Q3); on xUnit v2 it rides on the public `ScenarioInfo` as an
  internal property, and the public overload that takes a consumer's `ScenarioInfo` marks none. `kronikol ingest`
  makes no title from an identifier (its Cucumber and tests NDJSON lanes show names as written,
  `CucumberFeatureSynthesizer.cs:307`, `:489`, `FeatureSynthesizer.cs:185-196`, and after 4.3 their headings too), so it
  has nothing for the list to rewrite and takes no flag.
- **Ids.** Before rewriting, the pass records the key (`StableIdName ??= KeyName`, and the feature's
  `StableIdName ??= FeatureKey`), so adding, removing or editing an entry never moves an id. It must run after
  `ApplyToTitles`: run first, it would record an uncapitalised key and move the id of every title `CapitaliseTitles`
  changes (LightBDD's lower-case names, lower-case BDDfy titles).
- **Merges.** `kronikol merge` pairs features by their exact display name (`MergeableReportMerger.cs:117-135`), so two
  shards that spell a feature differently (one with the list, one without, or two releases) stay two features. R2 pairs
  them by `FeatureKey` where both names are generated, which the data file marks (`nameGenerated` on a feature, written
  only where true), and by exact name otherwise: `FeatureKey` is not unique on a Gherkin lane, where two titles that
  differ only in case ("order flow", "Order Flow") are two features with one key
  (`ScenarioInfoEnumerableExtensions.cs:13`, `:88`). A shard written before R2 has no mark and pairs by name, as
  today. A merge does not run the pass itself: each shard's data file holds its titles as its run showed them, and
  merging re-renders those, as it does for
  `CapitaliseTitles` (`MergeableReportRenderer.cs:93`).
- **Where it does not run.** The public `GenerateHtmlReport` and `GenerateTestRunReportData` take the model as their
  caller made it and run neither `CapitaliseTitles` nor this pass (`ReportGenerator.cs:1295`, `:5256`). A lane's own
  grouping happens before the pass, so the list never joins two features a lane kept apart (xUnit v2 groups by its
  formatted names, `ScenarioInfoCollectionExtensions.cs:11-12`).
- **Saved searches and links.** Every search ignores case: the search's `feature:` term (`report-search-function.js:94`,
  `:128`), the page's `q=` and `search=` (`report-search-function.js:233-300`), the CLI's `--grep`, `--feature` and
  `search --in names` (`QueryCommand.Overview.cs:325`, `:330`; `QueryCommand.Search.cs:92-93`). So a case change
  matches as before, and a change of words does not: a join ("Acme Pay" to "AcmePay"), and in R1 a split digit
  ("404when" to "404 when"). A group's anchor follows its heading's words (4.3), and a group carries no
  `data-stable-id`, so a saved link to one, or a Back entry keyed by its element id (`report-navigation-script.js:135`),
  finds nothing after a join. The changelog says both.
- **Trailing digits.** The last word of a run may carry a trailing run of digits, kept as written, so `xUnit` mends "X
  unit3" into "xUnit3" and `OAuth` "O auth2 token" into "OAuth2 token". The prototype lacks it: 29 places in Kronikol's
  tests need it.
- **What it does not do**, to be documented: a plural is its own entry (`APIs`); an entry that is a run of ordinary
  words (`CheckOut`) also rewrites them ("Check out the basket" reads "CheckOut the basket"); an entry with no letter or
  digit is ignored.

Measured with the prototype (`words.txt`):

| Title (rule of 4.1) | Entries | With the list |
|---|---|---|
| Acme Pay Webhook Tests | the issue's five | AcmePay Webhook Tests |
| Webhook lead created via api journey for an eligible customer | the issue's five | Webhook lead created via API journey for an eligible customer |
| When mca funding activates the loc is drawn | the issue's five | When MCA funding activates the LOC is drawn |
| The sbr is filed for the loc | the issue's five | The SBR is filed for the LOC |
| Grpc Order Status Tests | BreakfastProvider's seven | gRPC Order Status Tests |
| Specifications Open Api Scalar UI Tests | the same | Specifications OpenAPI Scalar UI Tests |
| Duck Db Click House Query Engine Tests | the same | DuckDB ClickHouse Query Engine Tests |
| Two g RPC hops carry the identity to the third host | the same | Two gRPC hops carry the identity to the third host |

### 4.6 Options considered

| Option | For | Against | Verdict |
|---|---|---|---|
| The issue's list alone, after today's humaniser | one change | a suite must list every acronym it already writes in capitals and every product name in its own DisplayNames; F2 to F4 stay; ids move unless recorded | the list, yes; alone, no |
| A formatter hook (`Func<...>` per title, as JUnit 5's `DisplayNameGenerator` and LightBDD's name formatter) | any rule a suite wants | code, not configuration; each lane names at a different time; `kronikol merge` and `ingest` cannot run a suite's code; more surface to keep | not now; it can sit on the same pass later |
| A built-in acronym list | nothing to configure | changes every suite's titles; English-specific; `Id`, `Io`, `Ui` and `Db` are ambiguous | no (Q8) |
| Learn spellings from the suite's own written names | nothing to configure | a partial run titles differently from a full one | no |
| Key ids on the raw identifier instead of frozen copies | one key for ever | moves every id once | a v6 question at most |
| A Kronikol attribute naming a feature | per-class control | new surface on every lane; only NUnit has a class display name to compare with; the list fixes brands without annotating classes | not now |

**What other tools do.** Each package's own DLL was loaded and run where it could be; the rest is from the sources
linked.

| Tool | A name made from an identifier | How a suite keeps a word |
|---|---|---|
| Humanizer 2.14.1 and 3.0.10 ([source](https://github.com/Humanizr/Humanizer/blob/v3.0.10/src/Humanizer/StringHumanizeExtensions.cs#L49-L72)) | keeps a run of capitals: `WhenMCAFundingActivates` reads "When MCA funding activates"; `Mca` and `AcmePay` are lost | none released; the next major adds `Vocabularies.Default.AddAcronym(...)`, process-wide, letters only, matching without case and printing the registered spelling, `iOS` included ([#796](https://github.com/Humanizr/Humanizer/issues/796), closed 2026-07-28 via #1858) |
| Rails `inflect.acronym` ([API](https://api.rubyonrails.org/classes/ActiveSupport/Inflector/Inflections.html#method-i-acronym)) | `titleize` and `humanize` split and re-case | the acronym list: "a camelized string that contains the acronym will maintain the acronym when titleized or humanized", for `RESTful` and `McDonald` too, matched as a delimited unit |
| BDDfy 10.0.13 ([source](https://github.com/TestStack/TestStack.BDDfy/blob/main/src/TestStack.BDDfy/DefaultHumanizer.cs)) | worse: "When m c a funding activates"; an underscored name keeps its case | per step and story titles (`[StepTitle]`, `[Story(Title)]`), or a whole humaniser (`Configurator.Humanizer`) |
| Serenity BDD ([NameConverter](https://github.com/serenity-bdd/serenity-core/blob/main/serenity-model/src/main/java/net/thucydides/model/util/NameConverter.java)) | keeps a word with capitals at both ends (`aTESTMethod` reads "A TEST method") | `@Title` on the method |
| LightBDD 3.12.1 ([formatter](https://github.com/LightBDD/LightBDD/blob/master/src/LightBDD.Framework/Formatting/DefaultNameFormatter.cs)) | underscores to spaces, never splits PascalCase, never changes case | a custom `INameFormatter` |
| JUnit 5 ([guide](https://docs.junit.org/6.1.3/writing-tests/display-names.html)), xUnit v2 and v3 ([`methodDisplayOptions`](https://xunit.net/docs/config-xunit-runner-json#methodDisplayOptions)) | no generator splits camelCase or changes case | `@DisplayName` and a `DisplayNameGenerator`; xUnit's `DisplayName` |
| NUnit, MSTest, TUnit | the method's name, or the test's own (`TestName` templates; `DisplayName`, MSTest v4 having removed `[TestMethod("...")]`; TUnit's `[DisplayName("... $param ...")]` and `DisplayNameFormatterAttribute` on an assembly, class or method) | the framework's own name |
| Allure, ReportPortal | show the framework's name as it is | `[AllureName]`; none |
| ReSharper and Rider "Abbreviations" ([docs](https://www.jetbrains.com/help/resharper/Coding_Assistance__Naming_Style.html#abbreviations)) | n/a | a list that only stops the naming inspection flagging `MCAFunding`; it changes no displayed name |

What follows for this plan:

- Of the test tools surveyed, only BDDfy's default lowers a word an identifier writes in capitals, and even it keeps one
  in an underscored name (Rails' inflector lowers it too, which is what its acronym list is for). Kronikol's scenario
  humaniser lowers both, and it is worse than the Humanizer its own feature humaniser was ported from. R1's rule keeps
  words in capitals as Humanizer does, without the lone capital Kronikol's port of it keeps (candidate B), and keeps a
  plural acronym whole, which Humanizer does not (4.1).
- The frameworks' own answer to "name this test my way" is their display name, and TUnit even has a formatter hook for
  it. Kronikol then humanises what that answer produced (F2), so on these lanes the frameworks' answer cannot work
  today. R1 makes it work.
- The two list precedents, Rails and Humanizer's next major, match an entry without case at word boundaries and print
  its registered spelling. `TitleWords` does the same, and matches across a split as well ("Acme Pay", "g RPC", BDDfy's
  "m c a"), because Kronikol's splitter has already cut such words in two before any list sees them. Unlike
  Humanizer's, it takes digits (`OAuth2`, `S3`) and is per suite, not per process.
- The .NET guidelines ([Capitalization Conventions](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/capitalization-conventions))
  write an acronym of three or more letters as a word (`HtmlTag`), both letters of a two-letter one in capitals
  (`IOStream`), and `Id` and `Ok` as words. So a guideline-following identifier shows a humaniser only two-letter
  acronyms; `Api`, `Mca`, `Id` and `AcmePay` need the list whatever the rule.

### 4.7 What does not change

- Gherkin titles, rules, outline headings and step text: as written since 5.9.1, apart from `CapitaliseTitles` (on by
  default), which upper-cases an all-lower-case first word of any title, a written one included ("npm audit passes"
  reads "Npm audit passes"), and leaves a first word with a capital inside it alone (`iPhone`, `gRPC`).
- Feature names on every lane in R1 (`Titleize` already keeps capitals), and so no feature id.
- Any scenario id or anchor in R1 or R2 (4.4), and the order of the report and of the data files (4.4's model order).
  What does move: a group's anchor where its heading's words change (4.3); entries in the three sorts that read case,
  which R1 makes ignore it (B3, B6); a retitled scenario's CTRF `name`, once (F9); a saved search for words that
  changed (4.5).
- `FormatFeatureName`, `FormatScenarioDisplayName` and `Titleize` keep their signatures.

## 5. Releases

| Release | Contents | Bump | Number |
|---|---|---|---|
| R1 | F1 to F4, F6 and F9, and B1 to B6: the rule of 4.1 for scenario titles, headings, BDDfy's fallback and the weaver; written names as written, and the generated-or-written flag; headings from shown names; the recorded scenario key, with the row's values and outline read from it, and the data keys `stableIdName` and `outlineTitle`; the model's order by the key; the sorts; the ledger's names | **patch**: every change corrects generated output, a sort or the ledger, and nothing is new for a consumer to call: the new members are internal, and the two data keys are data-file plumbing on the pattern 5.9.1 shipped in a patch | the patch after the newest release on `main` at push: 5.16.1 as things stand |
| R2 | F5 and F7: `TitleWords`, its pass, and the merge's pairing of generated features by key (`nameGenerated`) | **minor**: a new configuration option | the next minor: 5.17.0 as things stand |

R2 builds on R1's recorded key and flag. Q1 asks whether to ship them as one minor instead.

#190's four releases (kronikol-67) shipped as 5.13.0 (`2b74a2e4`), 5.14.0 (`8d7d1e1d`), 5.15.0 (`6bbc2ce1`) and 5.16.0
(`15a95d4c`), and 5.15.1 (`5ac5022f`) between them, while this plan was written; hence 5.16.1 and 5.17.0 above. A
later release by anyone moves these two numbers and the frozen copies' suffix (4.4), and nothing else in the plan.

## 6. Tests (TDD: each written red against 5.12.0, then made green)

### 6.1 Unit facts (core)

- **Pins of the defect, turned round.** `ScenarioTitleResolverTests.cs` (39 methods, 82 cases) asserts F1 and F2
  outright: `:244-246` ("Order API returns 404 for v1.2" read as "Order api..."), and the explicit-DisplayName rows at
  `:357-358` ("System.Text.Json parses numbers" read as "System.text.json parses numbers"). Each is rewritten to the
  fixed output first, so it is the red fact. The rows that pin a Pascal acronym (`:197`, "Validate otp validation
  tests") and a generic method's type arguments (`:346`, `Places<int32, string>`) stay as they are: the rule of 4.1
  leaves both. `tests/Kronikol.Tests.StepTracking/StepWeaverTests.cs:592-595` asserts
  `HumanizeMethodName("GetHTTPResponse")` is "Get http response" and turns round with Q5. No other test asserts a
  lowered acronym.
- **A lane that checks itself.** `tests/Kronikol.Tests.xUnit2/Lane/Expected.cs:29` builds its expected names with the
  formatter under test, so no formatter change can fail it. It gets literal names for the rows R1 changes.
- `SentenceCase` theory: every row of `examples.txt`'s candidate C column, plus `Response_Has_OK_StatusCode`,
  `XUnit3_assembly...`, `IPv4`, `p95`, `utf8`, and the shapes of section 4.1: `ParsesURLs`, `GetsAllIDs`, `XIs`,
  `GIVEN_VALID_INPUT_WHEN_SAVED`, `HTTP_GET`, `E2ETestsPass`, `P2PSync`, `color_F6F6F6`, `Returns200OK`, `IsXValid`,
  `First_N_items`, `ReturnsAValue`; a single word, an empty and an all-underscore name, a tab, a 300-character argument
  list (cut at 200, as 5.12.0 does), a non-Latin name, a surrogate pair, and Turkish and Azeri cultures
  (`CultureRun.Under`), as the 4.16.3 casing guard does. The verbatim copy of section 10 (`deepdive-rule/rules.cs`)
  gives the expected rows.
- Written names, per lane: the five names of section 3.2 shown as written, with a theory row's arguments after it; a
  written name ending in a parenthesis ("Returns 404 (not found)") and one with no space but a dot, each kept whole
  (B2); on xUnit v3 a row's own `TestDisplayName` ("Order v1.2 ships") and a `Label` (B1); the boundary both ways
  (`Order` with "Orders are sent to Kafka", `Login` with "Login via PayPal"); xUnit's `methodDisplay` and each
  `methodDisplayOptions` read as generated, `useEscapeSequences`' decoded dot included; NUnit's `{m}`, `{c}.{m}` and
  `{M}` templates read as generated; MSTest's data rows under a `[TestMethod(DisplayName)]` ("Adds numbers (1,2)") and a
  `DynamicDataDisplayName`; a TUnit `$param` name.
- Frozen copies: 4.4's `...AsOf` copies give 5.12.0's output on a fixture of corpus names with their 5.12.0 titles
  (the 473 that the rule of 4.1 changes and a thousand more drawn at random, checked in), so a later edit to a frozen
  copy fails at
  once.
- Ids: per lane, a scenario whose title R1 changes keeps 5.12.0's `stableId`, through `kronikol merge` too; a written
  name with text in brackets ("Ships to UK [EU]") keeps it (the row's values read from the key); the outline part of a
  theory row's hash is unchanged; xUnit v2's paired theory row and DisplayName test keep theirs (the key copied across);
  BDDfy's "Gets api" pair keeps its ` [1]` and ` [2]` ids; a written name behind a marker (`- returns 404`) keeps its id
  with `CapitaliseTitles` on and off; `KeyName` with `StableIdName` set and with `KeyedByOutline`; a consumer's
  `ScenarioInfo` through the public overload is never marked generated.
- Order: xUnit v2's and BDDfy's model order and the `sN` ordinals are 5.12.0's for a suite whose titles R1 changes.
- Headings: a written theory DisplayName heads its group; rows with their own names are headed by the method's title,
  not the first row's (`ParameterizedGroupRenderTests.cs:127-130`'s shape); LightBDD's inline-parameter outline, a
  BDDfy outline and an ingested one are headed as their rows read (B4); no heading is humanised twice; `outlineTitle`
  reaches a merged report; a Gherkin outline is unchanged.
- Sorts and the ledger: the merge's feature order and the History tab's entries follow the report's comparer (B6); a
  ledger appended after a run whose names changed, its ids not, reads the new names (B5).
- Weaver: `StepWeaver.HumanizeMethodName` gives the core rule's output on the shape theory's rows, so the link cannot
  quietly turn into a copy that drifts.
- R2: the match table of 4.5, punctuation kept, longest first, trailing digits (`xUnit` on "X unit3"), a plural, an
  entry of ordinary words, an entry with no letters; a row's arguments, a label and a `$param` value left alone; a
  written name, a Gherkin title and an explicit BDDfy title left alone; ids unchanged with the list added, edited and
  removed, `CapitaliseTitles` on, a LightBDD lower-case name included; a merge pairs two shards' generated feature
  across a list change and keeps "order flow" and "Order Flow" apart.

### 6.2 Lanes

The adapter projects (`Kronikol.Tests.xUnit2` with its fixture lane, and the xUnit v3, TUnit, NUnit, MSTest and
LightBDD suites; BDDfy has no adapter project, so its facts go in `tests/Kronikol.Tests/BDDfy` and its example lane):
one scenario per lane with a word in capitals and one with a written name, read back from the report's data file.

### 6.3 Playwright

A fact on the painted page: a written DisplayName and a word in capitals appear as written in the scenario's heading, in
the "Copy scenario name" value, in a group heading and in the search's result; the Search tab's sort by name puts "Get a
list", "Get HTTP response" and "gRPC call" in the order the Features list would (B3); R2 adds the same for a listed
word.

### 6.4 Mutations

Each rule broken alone must turn a fact red: drop the digit break; split before any capital after a digit; drop the
plural; drop rule 3; lower a one-letter capital; keep a lone `A`; humanise a written name; drop the boundary after the
method's name; heading from `OutlineId`; heading from the first member; record no key; record the key after the rename;
cut the outline, or read the row's values, from the shown name; copy no key across xUnit v2's pairing; sort BDDfy by
title; leave a recorded key uncapitalised; run R2's pass before `ApplyToTitles`; match by the entry's split instead of
its letters; shortest entry first; rewrite a row's arguments; apply the list to a written name; pair Gherkin features
by key.

### 6.5 Checks

The core suite after the version bump, every adapter project, the full Playwright suite (titles reach many facts'
selectors), the example lanes, the Release build of `release.slnf` for every target, and BreakfastProvider on local
packages with nothing configured: its 5 scenarios read their capitals, every `stableId` is as before, and its history
gate reports no new or absent scenario and no partial run.

## 7. Documentation and ledger

- Wiki: `API-Reference.md:33` and `:71` (what the two formatters do); `Step-Tracking.md:160-176` (the table and the
  algorithm: `GetHTTPResponse` reads "Get HTTP response"); `Search-Syntax.md:308` ("test method name, humanized");
  `Report-Configuration.md` (R2's option; its `TitleizeParameterNames` rows, `:63` and `:184`, for Q6's headers; the
  page never mentions `CapitaliseTitles`); `Ingesting-External-Captures.md:731-737`, the one page that documents
  `CapitaliseTitles`, whose "a scenario's `stableId` is computed from the displayed title" names the recorded key
  first; `Generated-Reports.md:390` (the `stableId`'s inputs, "scenario display name") and rows for `stableIdName` and
  `outlineTitle` beside `keyedByOutline` (`:395`); `Integration-xUnit2.md:76-78` and its collection-fixture section
  (`:464`), for F4; `Step-Tracking.md`'s mention of `CapitaliseTitles`; `Cross-Run-History.md` (ids kept, the ledger's
  names); each framework's page that shows a DisplayName. The README says nothing on casing.
- `CHANGELOG.md`: R1 names the rule, the written names, the headings, the bugs of 3.10 and that no id moves on
  Kronikol's own lanes, and says what does move: a group's anchor whose heading's words change, the sorts' order where
  case differs, a retitled scenario's CTRF `name` (once), a saved search for changed words, BDDfy's own reports'
  fallback titles, and the ids a consumer computes from the public formatters. R2 names the option, its match, and the
  merge's pairing of generated features.
- Doc comments: `ScenarioTitleResolver`'s summaries, `FormatScenarioDisplayName`'s example, `StepWeaver.GetStepText`,
  `Scenario.NameAsWritten` (`Scenario.cs:108-114`, "a Gherkin scenario"), and the schema texts of `stableId` and
  `nameAsWritten` (`ReportGenerator.cs:7591`, `:7599`).
- Kronikol4J (`C:/Code/Kronikol4J`, lines on its `origin/main` `6d9c6a5`): a ledger line per release in
  `docs/REMAINING_PARITY.md`, its divergence ledger section (`:1877`). The port is behind on this code already: its
  `StringCasing.java` has 5.9.0's pattern and no `TitleizeAsOf590`, its `ScenarioTitleResolver.java` predates 4.11.1
  (`charAt(0)` throws on an empty name, `:98`), and its ids carry no suite and no `stableIdName`. Its JUnit 5 and
  Spock adapters pass raw names and call no humaniser (`KronikolExtension.java:68-70`, `KronikolSpockExtension.java:50`);
  only its renderer's group headings do (`ParameterGrouper.java:68`); `DotNetHtmlReportRenderer.java:1271` and `:1517`
  are the parameter columns' sub-headers, through `StringCasing.titleize`. So each line reads "diverges": the
  headings' rule is the one to port, the rest has no Java caller. The 4.11.1 entry's last sentence, "Casing is
  unchanged on both sides (#139)" (`:2415`), is superseded by R1's entry. The parity goldens (`GoldenHtmlParityTest`,
  43 goldens among the parity folder's 59 files) hold titles byte for byte, so a golden whose heading changes is
  recaptured. Its `origin/main` has since moved to `d7ad2b0`, where the two ledger lines are `:1878` and `:2416`.

## 8. Coordination

- #210 (assertion labels; kronikol-be, worktree `Kronikol-210`): told of 3.11's first point by message on 2026-10-27.
  Its plan (`ASSERTION_LABELS_PLAN.md`, not committed, not green-lit) cites it in its section 8, item 1 (`HTTPStatus`
  reads "http status"), and its Q7 asks the owner whether what #139 decides applies to assertion labels too, in #139's
  release, with this plan's 4.1 and `probe.cs`'s `CandidateC` as the rule. This plan does not fold that in. If the
  owner says yes there, R1 also takes the label's case rule in `AssertionExpressionFormatter.cs`, the rule only: #210's
  R1 changes the callers of its `SplitPascalCase`, so the two do not collide. Whichever release ships second rebases
  onto the first.
- #205 (kronikol-2d, BigQuery): asked about overlap; there is none.
- #190 (party identity; kronikol-67), complete: R0 shipped as 5.13.0 (`2b74a2e4`), R1 as 5.14.0 (`8d7d1e1d`), R2 as
  5.15.0 (`6bbc2ce1`) and R3 as 5.16.0 (`15a95d4c`). R0 changed a schema description in `ReportGenerator.cs`
  (`:7894`, a log entry's `capturedBy`) and the doc comment of `FixedNameForReceivingService`
  (`ReportConfigurationOptions.cs:454`); R1 changed `ReportGenerator.cs` (+127 lines), the `Merge/` files and
  `ReportConfigurationOptions.cs`; R2 changed `ReportGenerator.cs` and one line of `MergeableReportReader.cs`; R3 no
  cited file. None touched a site this plan changes: the merge's feature pairing is
  the same code at `MergeableReportMerger.cs:183-209`. This plan's worktree is rebased onto 5.16.0.
- #212 (awaited assertion labels; kronikol-c6, worktree `Kronikol-212`) changes how
  `AssertionExpressionFormatter.Format` reads a statement's head, not the case rule; its plan says it leaves
  `SplitPascalCase` alone.
- Files R1 touches: `ScenarioTitleResolver.cs`, a new naming file and its link in `Kronikol.StepTracking.csproj`,
  `ScenarioStableId.cs`, `Scenario.cs`, `StepText.cs`, `ParameterGrouper.cs`, the data writers and their schema
  (`ReportGenerator.cs`), the merge reader and merger, each adapter's naming lines, xUnit v2's `ScenarioInfo.cs`,
  `ScenarioInfoCollectionExtensions.cs` and `KronikolResultsSink.cs`, BDDfy's `BDDfyScenarioInfo.cs`,
  `DiagramCapturingProcessor.cs` and `ScenarioInfoExtensions.cs`, LightBDD's `FeatureResultExtensions.cs`,
  `StepWeaver.cs`, `HistoryLedgerWriter.cs`, `HistoryHtml.cs` and `report-search-results-script.js`. A peer release
  touching the same files goes first or after by message; the number is claimed at push.

## 9. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | Two releases (R1 a patch with the fixes, then R2 a minor with the list) or one minor with both? | two: the fixes help every suite now, and the option is surface worth its own release |
| Q2 | When a scenario's title changes (R1's rule, a written name, R2's list), keep its id (the recorded key of 4.4) or let it take a new one? | keep, as 5.9.1 chose for features (its Q5): a rename of more than a tenth of a suite would mark every later run partial for as long as the last full run stays in the history window (3.6) |
| Q3 | Does the list rewrite only the titles Kronikol generated, or names a person wrote too (DisplayNames, Gherkin)? | generated only: a written name is shown as written, the rule 5.9.1 set |
| Q4 | The option's name and shape: `TitleWords`, a list of strings, as the issue has it? | yes |
| Q5 | Does R1 give the step weaver the same rule, so woven step text keeps its capitals ("Get HTTP response")? | yes: it is the same defect, and step text and titles should read alike |
| Q6 | Does R2's list also rewrite woven step text and the parameter column headers (`TitleizeParameterNames`)? | headers yes, at their two render sites (`ReportGenerator.cs:3801`, `:3897`); step text not now |
| Q7 | Close #102 with a comment naming 4.11.1 and 5.9.1, and #110 with one naming 5.9.1 for its first ask? Its second, humanising a Gherkin title that is a single identifier (`Feature: BasketCheckout` reading "Basket Checkout"), did not ship: say so, or have R1 do it? | close both, and say #110's second ask is not taken: a title a person wrote is shown as written, 5.9.1's rule and Q3's |
| Q8 | Ship a built-in list of common acronyms? | no |
| Q9 | Show an underscore-separated part that is itself an identifier as written (`stableId`, `gRPC`, `xUnit3`, `innerHTML`, `ITrackingComponent`, `IPv6`)? It changes 218 of Kronikol's test titles beyond 4.1's rule and none of BreakfastProvider's, and mends 48 of the 57 interface `I`s; but a BDD-style part such as `Given_IAmLoggedIn` would stay glued, a shape no corpus here holds | not now: R2's list mends named identifiers without that risk |
| Q10 | Read a lone `s` after a word as a possessive (`The_scenario_s_tags` reading "The scenario's tags")? 212 of Kronikol's test titles, 211 of them possessives; it writes an apostrophe the name does not hold, and misreads `writes_s_and_o` | no: a rule should not add a character the name lacks |
| Q11 | On BDDfy, count a title as generated (so R2's list rewrites it) only when it equals BDDfy's own humanising of the method's name, and an explicit `BDDfy("...")` or `[Story(Title)]` as written (4.5)? | yes: it keeps the promise that the list never touches a name a person wrote |
| Q12 | Have the ledger writer write a roster line again when its hash is there but its names differ, so `query history` and the absent lists show today's titles (B5)? A run whose names changed adds one roster line, once | yes, in R1: it also mends the feature names 5.9.1 left stale |

## 10. Log

- 2026-10-27, kronikol-50: plan written at 5.12.0 in the worktree `Kronikol-139` (branch `plan/139-title-words`).
  Every measurement is the harness's, run against the worktree's own `src/Kronikol`. Two read-only agents helped: a
  sweep of every reader of a title as a key, every generator and every pinning test (each claim used here was re-read
  in the code by hand: the merge's pairing by name, `IsPartial`'s threshold, `SuggestRenames`' pairing, BDDfy's
  duplicate counter, the Kronikol4J lines), and a survey of other tools, which ran Humanizer 2.14.1 and 3.0.10, BDDfy
  10.0.13, LightBDD 3.12.1 and xUnit v3's formatter from their own DLLs (Humanizer #796 was read on GitHub). The first
  prototype rule (candidate A in `probe.cs`) glued digits to the capital after them ("more Than10Visible"); that is why
  4.1 breaks between a digit and a capital. Not green-lit.
- 2026-10-27, kronikol-50: #210's plan (`ASSERTION_LABELS_PLAN.md`, kronikol-be) cites 3.11's first point, and its Q7
  asks the owner whether 4.1's rule applies to assertion labels in this plan's release; section 8 says how the two
  releases fit. 5.12.1 (`79e8d644`) shipped the same day and touched none of the code cited (its changelog entry moved
  `CHANGELOG.md`'s lines, as the header now says), so nothing measured changes: R1's number moves to 5.12.2 as
  things stand, and the frozen copies' suffix to `AsOf5121`. #190's R0
  (committed as 5.13.0, not pushed) and #212 were checked for overlap: neither touches a site this plan changes
  (section 8). 5.13.0 then shipped (`2b74a2e4`): R1 is 5.13.1 and R2 5.14.0 as things stand, the frozen copies
  `AsOf5130`, and two history lines moved (header). Not green-lit.
- 2026-10-27, kronikol-50, at the owner's ask to make the plan complete and accurate and to dive into whatever might
  change it: five read-only agents ran while #190 shipped as 5.13.0 and 5.14.0. Two audited the plan against the code
  (sections 1 to 3, and 4 to 10), one traced every reader that keys, sorts or links by a title, one traced each lane's
  title and id from the frameworks' own naming code (decompiled from the NuGet cache), and one ran 5.12.0's rule and
  candidate C, copied verbatim into a standalone app, over all 13,342 corpus names and the shapes of 4.1 (its copy
  reproduces `details.tsv` row for row; Humanizer 2.14.1 and 3.0.10 were run beside it). Each claim used was re-read in
  the code by hand at `1ee6373a` or `8d7d1e1d` first; one was dropped (that 5.9.1's plan had put #110's second ask to
  the owner: it never mentions #110). What changed: the rule gained the plural, the start-of-word digit split, the
  one-letter capitals and rule 3 (4.1; 468 titles where candidate C changed 427, the measures in section 4.1); the
  written-or-generated test was redesigned per lane, with the boundary after the method's name (4.2); headings come
  from what the members share, with `outlineTitle` for the rest (4.3); ids now hold through a written name's bracket
  text, xUnit v2's pairing, BDDfy's numbering and the model's order (4.4); R2 runs after `CapitaliseTitles`, leaves
  arguments alone, tells BDDfy's own titles from a test's, and pairs only generated features by key (4.5); new findings
  F9 (sorts, the ledger, CTRF) and bugs B1 to B6 (3.10); four questions added (Q9 to Q12) and Q7 rewritten; the counts
  of 3.1, 3.3 and 3.5, the claim that the issue's workaround was forced, and the wiki and Kronikol4J citations
  corrected. Not green-lit.
- 2026-10-28, kronikol-50: #190's R2 shipped as 5.15.0 (`6bbc2ce1`), touching no site here; the worktree is rebased
  onto it, R1 is 5.15.1 and R2 5.16.0 as things stand, and the frozen copies `AsOf5150`. The release numbers now live
  in section 5 and 4.4's frozen-copy example only. Not green-lit.
- 2026-10-28, kronikol-50: 5.15.1 (`5ac5022f`) and #190's R3 as 5.16.0 (`15a95d4c`) shipped, touching no cited code;
  the worktree is rebased onto 5.16.0, R1 is 5.16.1 and R2 5.17.0 as things stand, the frozen copies `AsOf5160`, and
  the wiki's `Ingesting-External-Captures.md` lines moved by 4 (header). Not green-lit.
- 2026-10-28, kronikol-50, at the owner's word ("Can you implement the plan in full"): every question takes its
  recommendation (Q1 to Q12), as the owner's "in full" took them for REPORT_UI_FIXES_PLAN. R1 shipped as 5.16.5
  (tag `v5.16.5`), after 5.16.2 (kronikol-7b) and 5.16.3 and 5.16.4 (#210's R1 and R2, kronikol-be), so the frozen
  copies are `AsOf5164`.
  #210's Q7 ("follow #139's decision, in #139's release") puts the label's case rule in R1: `NameWords`, the one helper
  every word site of `AssertionExpressionFormatter` calls since 5.16.3, is `SentenceCase.Words`. Departures from the
  design, each for the reason given:
  - **Ids from a recorded key, not from the recorded name parsed again** (4.4). Reading the outline and the row's values
    back from 5.16.x's name would show that name's lowered values in the parameter table, and keep a written name's
    "(not found)" as an `Arg 0` column (against B2 and 4.2). Each lane records what 5.16.x computed, the name, the
    outline id and the example values (`Scenario.StableIdKey`), only where one of them differs, and the data files
    write them as `stableIdName`, `stableIdOutlineId` and `stableIdExampleValues`.
  - **xUnit v2's model and BDDfy order by the recorded key alone,** where 4.4 said the key and then the shown name: the
    key alone is 5.16.x's order exactly, and it is stable.
  - **The ledger's `Amend` writes no roster.** `HistoryFold` keeps a run's earliest names, so a roster written by an
    amend was never read; the append writes the roster again when a run's names differ (B5, Q12).
  - **xUnit's generated names are found with xUnit's own public `DisplayNameFormatter`** (v3 `Xunit.v3`, v2
    `Xunit.Sdk`) under all 16 combinations of `TestMethodDisplayOptions`, with no configuration read; a name whose
    method part follows a dot, on a path with no whitespace or bracket, is generated too (a display name made
    elsewhere, as xUnit v2's own executor tests make one).
  - **TUnit has no constructible `TestContext` in its test project,** so its lane is covered by
    `FormatLaneScenarioName`'s facts and its example lane.
  - **`SentenceCase.Words` judges each run of letters and digits between punctuation** for keeping capitals, so
    `Encode(1U)` reads "encode(1U)": the label measurement found "encode(1u)". The shouting test still counts
    whitespace words only.
  - **6.3's sort fact compares with the culture's order of the names,** the order the data files and, since R1, a merge
    give features: the HTML Features list keeps the order the lane hands over, so "the order the Features list would"
    is that comparer.
  - Rows turned round: MSTest's happy-path ordering fact (`ZZZ_HappyPath` reads "ZZZ happy path"), the label
    `a?.B?.C` ("A B C should not be null": a one-letter capital keeps its case), and `GetsTheUKVATRate`, which no rule
    can part ("Gets the UKVAT rate"; the lane facts use `Gets_the_UK_VAT_rate`).
  Measured: on top of 5.16.3, which already keeps a call's brackets as written, the label rule changes 1 of
  BreakfastProvider's 1,132 labels ("Reader is DB null(4) should be true"), none of the 53 in the examples and none of
  the 88 written over several lines (#210's `probe.cs` over its corpora, run against both trees). Mutations (6.4, R1's 23): all 23 of the list run against the core, MSTest, xUnit v2 and StepTracking
  suites and the Search tab sort against its Playwright fact turn a fact red. Checks (6.5): the core suite (10,778), every adapter suite, the xUnit v2 fixture lane, the new Playwright class, the Release
  build of `release.slnf` for every target, and BreakfastProvider's xUnit, NUnit, BDDfy and TUnit lanes on local
  packages, whose 848 stable ids all match a run on its published 5.14.0 and whose 18 changed titles differ in case
  only; the full Playwright suite (2,095 facts, none failed; 28 skipped and 2 not run, as on `main`). A read-only review of the diff found four faults, each fixed with a fact before release: xUnit's
  formatter throws under `UseEscapeSequences` on a name such as `Rejects_UD800`, which the default-name probe tried and
  lost the report on; a heading cut a Gherkin title's own "(<case>)" (the 5.9.1 fact went red); the compressed data
  file dropped a key's none, so a merge read the scenario's outline in its place; `CapitaliseTitles` left an outline
  title lower-case.
- 2026-10-28, kronikol-50: R2 shipped as 5.18.0 (tag `v5.18.0`), after #192's 5.17.0 (kronikol-bf) took the number 4.5's section 5 gave it. Departures from 4.5, each for the
  reason given:
  - **Three marks, not one.** A row's name and its outline's title are made apart: an xUnit theory row with its own
    `TestDisplayName`, or an MSTest row under a `DisplayName`, has a written name and the method's title as its outline
    title. So a scenario carries `NameGenerated` and `OutlineTitleGenerated`, and a feature `NameGenerated`, each set by
    its lane (internal; on xUnit v2 they ride on `ScenarioInfo`, and a consumer's `ScenarioInfo` sets none). Only the
    feature's goes into the data files (`nameGenerated`), which is all a merge needs.
  - **`OutlineId` is never rewritten.** It is a key the rows are grouped and hashed by, and a theory's written and
    generated rows share it: rewriting it on the generated rows alone would split the group. The heading's words come
    from the rows' names and `OutlineTitle`, which are rewritten.
  - **BDDfy's humaniser is `Configurator.Scanners.Humanize`** in BDDfy 8.0.1.3, the version Kronikol builds against; 4.5
    named 10.x's `Configurator.Humanizer`. A title is BDDfy's own where it is that humaniser's reading of the method's or
    the class's name, or the class's name itself (which Kronikol reads the method's in place of); a story's where no
    `[Story]` names it, or its title is the humaniser's reading of its class's name.
  - **A run's leading mark stays too, and a mark inside a run stops it.** 4.5 kept the last word's trailing punctuation;
    the first word's leading one ("(acme pay)") stays as well, and a run with a mark before an inner word, after an inner
    word or inside a word ("acme, pay", "acme-pay") does not match, so no character is ever dropped. A digit-only word
    after the run is a word of its own ("x unit 3" reads "xUnit 3").
  - **A merge pairs by name first, then by key.** A generated feature pairs with one of the same name as today, and
    otherwise with a generated feature whose ids key on the same name; a merged feature keeps the mark only when both
    halves carry it.
  - **A feature's place follows its new name.** 4.7 said no order moves; every view orders features by name, so a feature
    the list renames takes its place by that name. The scenarios' order within a feature is the lane's, made before the
    pass, and does not move.
  - **The parameter headers' list is the run's.** A merge re-renders headers titleized without the list, since the list
    is a run option the merge does not have.
  - **The Report Configuration page also gains the `CapitaliseTitles` and `CapitaliseStepText` rows** it lacked (section
    7 noted the first missing).
  - **TUnit has no lane fact,** as in R1: its marks are the NUnit lane's, line for line, and its example lane runs.
  - **The adapter test projects of xUnit v3, NUnit and MSTest now see Kronikol's internals,** as xUnit v2's did, so their
    facts read the marks and run the pass on the lane's own model.
  Found on the way and fixed in R2: 5.16.5's CI run failed only `OtlpTapStallTests.A_slow_steady_export_is_received`,
  which moved the fake clock before the tap had read the head, so a loaded runner reached the kept-alive limit (ProxyTap's
  slow body fact had the same gap); and `AttachmentProblemTests` and `NotATestReportTests` wrote a ledger into the
  repository root (`_ => null` as the environment switches history on). Measured on BreakfastProvider's names from R1's
  lane runs, with the harness's seven words (`gRPC`, `OpenAPI`, `AsyncAPI`, `ClickHouse`, `SQL`, `DuckDB`, `UI`): 10 of 73
  features and 9 of 212 scenarios change on each of the xUnit, NUnit and TUnit lanes, and 1 of 27 features and 9
  scenarios on BDDfy's, each as `words.txt` has it ("Grpc Order Status Tests" reads "gRPC Order Status Tests").
  Checks: the core suite (11,068, 2 skipped), every adapter suite (xUnit v2 110, MSTest 84, NUnit 48, xUnit v3 32, TUnit 48, StepTracking 61, LightBDD 26 and 25), every other test project CI's remainder job runs, the Release build of `release.slnf`, the full Playwright suite (2,096, 0 failed) and the example lanes. Mutations: 36 of 36 killed, one of them (a mark before an inner word) only after the row "acme (pay)" was added. #139 is closed with a comment naming 5.16.5 and 5.18.0; #102 and
  #110 are closed (Q7), #110's comment saying its second ask is not taken.
