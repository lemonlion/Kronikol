# S12 D49 and D50: a title on every control, prepared and not applied

> Applied on 2026-10-20 over TAGS_PLAN T2 (`f077cda6`), after this was written at `a1235d98` and `d385c715`. T2
> has one Tags row (a category is a tag), so the scripts here are the ported ones: the rows and Clear All name no
> Categories row, `FilterChip` keeps T2's `more` parameter, `FilterRowsTests` pins 18 chip titles where 12 were
> pinned, and `ControlTitlesTests` names the rows as T2 has them. Line numbers below are at the older bases. The
> plan's log has the measured red, green and mutations.

Prepared on 2026-10-19 as a dry-run package. Base: `a1235d98` (branch v5). Branch v5 has since moved to `d385c715`
(db663d88, the 4.32.0 merge c010bcb6, the test fix 7200017e, D43 c7abf98f, S12a d385c715); the scripts apply there
too and make the same edits (section 13). Of the files the scripts edit, only `ReportGenerator.cs` and
`DiagramContextMenuTests.cs` differ between the two bases. File:line references are at `a1235d98` unless marked HEAD;
at `d385c715` a `ReportGenerator.cs` line between 829 and 4945 is 14 lines later, and the other files have the same
lines. Nothing in any worktree was changed; no build, test run, Playwright or node test runner touched a worktree.

| File the scripts edit | md5 at a1235d98 | md5 at d385c715 |
|---|---|---|
| src/Kronikol/Reports/ReportGenerator.cs (UTF-8 BOM, kept) | d32cfefa40f50f61afe675d379ca36ed | 61b130ec82099f3d80dd403a78365c01 |
| src/Kronikol/History/HistoryHtml.cs | 4da5f9dd9662274f0d201f93f366ed7e | same |
| src/Kronikol/Reports/ParameterValueRenderer.cs | c2f9e463c094c5c3b5caef32fa56c4ce | same |
| src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs | 46b573edcf7e953c0b73b349ecc27446 | same |
| src/Kronikol/Reports/internal-flow-popup-script.js | 8594b8e19ad49f481312c6c31a1e79b6 | same |
| src/Kronikol/Reports/plantuml-browser-render-script.js | 2b600a8d787907e12c73ffd111b58934 | same |
| src/Kronikol/Reports/report-step-status-titles.js | 2c98ccce54af0119a98c17d9126e12da | same |
| plans/OPTION_C_PLAN.harness/checks/nav-check.js (D50) | a5c55255b21e701a824665b95184c5e1 | same |
| plans/OPTION_C_PLAN.harness/checks/q-test.js (D50) | 342b1d8543b589fd79f3d1e813a7d3d9 | same |
| tests/Kronikol.Tests/Reports/DiagramContextMenuTests.cs | 74c858dd59a08dd34de7c2d84c7e55b5 | 1a1f06a7158e96872cfff23f99218fc2 |
| the 21 other pinned test files (section 10) | `work/dry-base.md5` | same |

Apply with `python scripts-s12d49.py <root>` (the product, and D50's two harness checks), then
`python tests-s12d49.py <root>` (the two new test classes and the 90 pin lines). Each checks every anchor in memory,
exactly as often as it expects, and writes nothing unless all hold; each refuses a second run ("S12 D49 is in already",
"already there: S12 D49 tests are in already"). Each keeps a file's byte order mark and its line ends: it edits with
LF and writes a file back with CRLF where it had CRLF. That matters in `C:/Code/Kronikol-v5`, whose `nav-check.js` is
CRLF on disk although git holds it LF; a file that mixes the two is refused. Copy `checks/mutate-s12d49.py` into
`plans/OPTION_C_PLAN.harness/checks/`. For the red step, run `tests-s12d49.py` first: the new classes compile against
the base and fail on their assertions (section 9). `fix-img-facts.py` is separate and not D49 (section 14).

## 1. What the preview does, and what the plan asks

SB = `plans/OPTION_C_PLAN.harness/inventory/structure-and-behaviour.md`. SB 730-744 (D49): the preview's
`c-round9-tips.js` sets `title` from a rule table on load, when a tab opens, and on `pointerover` and `focusin`, with
"where" words: "every sequence diagram in the report" in the report bar, "this scenario's sequence diagrams" in a
scenario's bar. The port is mostly STRUCTURE: every static title and every where-word is written by the C#, where the
control is written; scripts title only markup they draw. SB lists its defects: the chip rules never apply (the count
titles win), the History and Diagnostics headings say "click to show or hide" on summaries D20 made inert, Background
Steps, Activity Diagrams and Flame Chart have no rule, the `.collapse-expand-all` rule is dead, and "This step has one
step inside it: click to show or hide them" says "them" for one (SB 899-913, items 2, 3, 8, 12). SB 955-956: finish
with an audit against section 3, and settle D50. The table of SB 3.1 (53 rows) is at SB 760-814 and the other titles
(3.2) at SB 816-838.

## 2. The 53 rows of SB 3.1

State at the base: DONE (the row's title is there), PARTLY (a title is there and says less), MISSING. After D49 every
row has its title. "Written by" is the line after the scripts, at `a1235d98`. RG is `ReportGenerator.cs`, HH
`HistoryHtml.cs`. {where} is "every sequence diagram in the report" on the report bar and "this scenario's sequence
diagrams" on a scenario's bar, from `PayloadsWhere(scenarioLevel)` (RG 1106), which `BuildDetailsRadio` (RG 1063) and
`BuildFilterToggleButton` (RG 1094) and the format and width titles reach through the `scenarioLevel` they already
take.

| # | Control | At the base | Title after D49 | Written by |
|---|---|---|---|---|
| 1 | Features tab | MISSING, RG 1961 (`ReportTab` 2847) | The features and their scenarios, with the filters applied | RG 2007, `ReportTab(key, label, title, ...)` 2893 |
| 2 | Timeline tab | MISSING, RG 1962 | Every scenario as a bar, slowest first; with ", with the first-call warm-up it paid" only where a scenario paid one (`warmUp.ScenarioMs.Count > 0`) | RG 2008 |
| 3 | Components tab | MISSING, RG 1963 | The services, databases and queues the scenarios called, drawn as one diagram | RG 2009 |
| 4 | History tab | MISSING, RG 1964 | This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone | `HistoryHtml.Description` HH 103, RG 2010 |
| 5 | Diagnostics tab | MISSING, RG 1965 | What Kronikol noticed while building this report, such as calls it could not put under a scenario | `DiagnosticsDescription` RG 4853, RG 2011 |
| 6 | `.nav-readout` | MISSING, RG 3021 | The scenarios and features the filters leave showing | `FiltersLeaveShowing` RG 3089, `NavFilters` 3110 |
| 7 | `.nav-clear` | PARTLY, RG 3021: "Clear every filter" | Clear every filter: the search with its dep:, duration: and is: terms, and the {rows the box has} (Q1) | `ClearAllTitle` RG 3054, built once at 1855, passed to `NavFilters` 3110 |
| 8 | `.view-btn` | DONE, RG 3039 | unchanged | |
| 9 | `.sort-select` | DONE, RG 3071: "The order the features are listed in" | unchanged | |
| 10 | `.collapse-expand-all` | DONE: the class is gone (D22); the fold buttons carry 3.2's titles, RG 3055 | unchanged | |
| 11 | Expanded | MISSING, RG 1072 | The payloads in {where} show in full | RG 1075 |
| 12 | Collapsed | MISSING, RG 1073 | The payloads in {where} fold to one line each; a box's + button opens it | RG 1076 |
| 13 | Truncated | MISSING, RG 1074 | Payloads in {where} longer than the line limit beside this are cut at that many lines | RG 1077 |
| 14 | Payloads label | MISSING, RG 1071 | How the payloads in {where} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit | RG 1074 |
| 15 | line count select | MISSING (no title and no name), RG 1075 | How many lines a cut payload box keeps in {where} (with Truncated picked); and `aria-label="Payload length"`, its group's heading, as the format and width selects are named by theirs | RG 1078 |
| 16 | Headers | MISSING, RG 1095 | Show or hide the headers at the top of each payload in {where} | `ToggleTitle` RG 1110-1118, used at 1099 |
| 17 | Assertions | MISSING, RG 1095 | Show or hide the boxes with the test's assertions in {where} | same |
| 18 | Steps | MISSING, RG 1095 | Show or hide the bars marking where each step starts in {where} | same |
| 19 | Databases | MISSING, RG 1095 | Show or hide the databases and their calls in {where} | same |
| 20 | Events | MISSING, RG 1095 | Show or hide the calls to and from message brokers (the participants drawn as queues), with their payloads, in {where} | same |
| 21 | Setup | MISSING, RG 1095 | Show or hide the setup in {where}: everything before the first When step, or the Setup section where the report keeps setup apart | same |
| 22 | format select | PARTLY, RG 1540, 1543: "Payload format" | How the payloads in {where} are written: JSON as captured, or the same data as YAML | `NoteFormatTitle` RG 1122, used at 1583, 1586 |
| 23 | width select | PARTLY, RG 1553 (const), 1556, 1559: "Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram." | How wide the payloads are in {where}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram. | `NoteWidthTitle` RG 1126, used at 1598, 1601 |
| 24 | Clear All (box) | MISSING, RG 1815 | as row 7 | RG 1859 |
| 25 | Export HTML | MISSING, RG 1815 | Download a copy of this report holding only the features the filters show | RG 1859 |
| 26 | Export CSV | MISSING, RG 1815 | Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds | RG 1859 |
| 27 | `.filter-readout` | MISSING, RG 1815 | as row 6 | RG 1859 |
| 28 | `#searchbar` | MISSING, RG 1816 | Search names, steps, tags and diagram text. Combine words with && (and), &#124;&#124; (or) and !! (not); @tag and $status pick by tag or status, dep:, duration: and is:happy by dependency, time and happy path; ? shows the syntax | `SearchTitle` RG 3092 (held HTML-encoded), RG 1860 |
| 29 | `.search-help-toggle` | PARTLY, RG 1816: "Search syntax help" | How to write a search: words, "phrases", @tags, $statuses, dep:, duration:, is:happy, &&, &#124;&#124;, !! and parentheses | `SearchHelpTitle` RG 3095, RG 1860 |
| 30 | status chips | MISSING, RG 1851 | Show only the scenarios that {passed, failed, were skipped, were bypassed}; pick more statuses to show them together | `StatusToggleTitle` RG 3076, RG 1896 |
| 31 | `#duration-threshold` | MISSING, RG 1878 | Seconds: scenarios taking at least this long; with ", not counting first-call warm-up" where the run marked one (the `notWarm` the percentile buttons use) | RG 1923 |
| 32 | happy-path segments | MISSING, RG 1883 | Show: "Happy-path scenarios and the rest"; Only: "Only the scenarios tagged as happy paths"; Hide: "Leave out the scenarios tagged as happy paths" | RG 1928 |
| 33 | mode switches | DONE, RG 2987 (`FilterModeToggle`; texts at 1893, 1906, 1925, D11's) | unchanged | |
| 34 | Untagged and the other none chips | PARTLY, RG 2981: the count alone | {N scenario(s)}. Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once. (Q4) | `FilterChip` RG 3028, `ChipClicks` 3031 |
| 35 | tag chips | PARTLY, RG 2981 | as row 34 | same |
| 36 | dependency chips (and category chips) | PARTLY, RG 2981 | as row 34 | same |
| 37 | `.group-clear` | DONE, RG 2991 | unchanged | |
| 38 | `.filters-empty-clear` | MISSING, RG 3029 | Clear every filter | RG 3118 |
| 39 | `.mobile-filter-toggle` | MISSING, RG 1817 | Show or hide the filters | RG 1861 |
| 40-44 | `.tl-seg-btn` (Timeline, Components) | DONE, RG 2884 (S11d, S11e) | unchanged | |
| 45 | diagram view tabs | MISSING, RG 1161 | Sequence: "The calls between the test and the services, in order"; Activity: "The work inside the services, span by span, as an activity diagram"; Flame: "Where the time went inside the services, span by span, as nested bars" | `DiagramViewTitle` RG 1198, used at 1192 |
| 46 | `summary.h4` | MISSING: diagrams RG 1180; Failure Result 2418 and, for an outline's example, 3769; Background Steps 4016; Steps 4058 | Diagrams: "This scenario's diagrams" (several views), "This scenario's sequence diagrams" (sequence only), else the one view's words (row 45). Failure Result: "Why the scenario failed: the error, with the expected and actual values where it gives them". Background Steps: "The steps of the feature's background, which run before this scenario's own". Steps: "This scenario's steps", with ", with how long each took" where a step has a time | RG 1222-1223; `FailureResultTitle` 6724 at 2464 and 3858; 4105; 4147-4148 |
| 47 | composite step's summary | MISSING, RG 4151 (stays bare) | One: "This step has one step inside it: click to show or hide it"; more: "This step has N steps inside it: click to show or hide them" | `report-step-status-titles.js` 11-19 (`giveSummaryTitle`) and 33-34, on first `mouseover` or `focusin`, as the step's icon is titled since REPORT_FORMAT_PLAN R2 |
| 48 | attachments | MISSING: file links RG 2455, 3757, 4305; images 2451, 3753, 4300 | File: "Open {name}, a file this {scenario, step} attached", "a link" for a URL; image: "Show {name}, an image this {scenario, step} attached, at full size" | `AttachmentLink` RG 6707 and `AttachmentImageLink` 6717, called at 2497, 2501 (a scenario's), 3842, 3846 (an outline example's), 4390, 4395 (a step's) |
| 49 | Raw PlantUML, History, Diagnostics summaries | MISSING: render script 178, 1169, 1320; HH 150; RG 4785 | Raw PlantUML: "The PlantUML source this diagram was drawn from". History: in its tab `HistoryHtml.Description` (row 4), as a disclosure "This run against the earlier runs on the same branch: click to show or hide the details". Diagnostics: in its tab `DiagnosticsDescription` (row 5), as a disclosure "What Kronikol noticed while building this report: click to show or hide the list" (Q3) | render script 178, 1169, 1320; HH 159 (`DisclosureTitle` 106); RG 4879-4880 |
| 50 | `a.history-link` | MISSING, HH 137, 143 | In the report: "Go to this scenario on the Features tab"; on the labs page: "Open this scenario in the report" | HH 146, 152 |
| 51 | Next Failure | MISSING, RG 2587 | Go to the next failed scenario; the count says which one of how many | RG 2633 |
| 52 | back to top | MISSING (no title and no name: its text is an arrow), RG 2591 | Back to the top of the report; and `aria-label="Back to top"` | RG 2637 |
| 53 | CI run link | MISSING, RG 3134 | Open the CI run this report came from | RG 3223 |

Counts: 10 rows DONE at the base and untouched (8, 9, 10, 33, 37, 40 to 44); 7 PARTLY whose title changes (7, 22,
23, 29, 34, 35, 36); 36 MISSING whose title is added. The brief's 05be4469 counts (5 DONE, 4 PARTLY, 3 DECISION,
5 BLOCKED, 36 MISSING) re-baselined: S11d and S11e made 40 to 44 DONE, and the chips' count titles are PARTLY here.

### 2.1 SB 3.2, the other titles

All present at the base and untouched: the theme switch (RG 95), the gears (RG 1154, 1158, 3077), the duration label
and the percentile buttons with their warm-up clause (RG 1877-1878), the floating bar's Filters button and the box's
close (RG 3021, 3025), More filters (RG 3014), the fold buttons (RG 3055, `FoldButton`), the mode switches (row 33),
the Components info mark (S11e, RG 2902), the note buttons' and the note format and width buttons' SVG titles
(`collapsible-notes-script.js` 467, 488, 521, 544, 565, 604, 622, 645), the scenario settings gear's two titles (S11),
and the diagram zoom slider ("Zoom level", `context-menu-script.js` 1076 at HEAD). SB's `.k-scenario-settings-toggle`
is the product's per-scenario gear (D23).

## 3. Controls outside SB's table, titled too

The audit (section 9) walks every control the report writes, and these had no title.

| Control | At the base | Title | Written by |
|---|---|---|---|
| `button.feature-open` (the Summary view's feature names) | RG 1739 | Open this feature's card in the Full view (Q5) | RG 1781 |
| a failure cluster's scenario link | RG 2097 | Go to this scenario | RG 2143 |
| a step's table reference (`button.step-table-ref`) | RG 4237 | Show {name} in its table | RG 4327 |
| a step's inline value button (`step-table-ref` with `data-value`) | RG 4220 | Show or hide the value of {name} | RG 4310 |
| a table cell's whole value (`details.param-expand > summary`) | ParameterValueRenderer.cs 202, 649 | Show or hide the whole value | same lines |
| an empty call's diagnostic info in the popup | InternalFlowHtmlGenerator.cs 344, 372 | Why this call may have no spans: click to show or hide the details | same lines |
| the popup's close | internal-flow-popup-script.js 62 (text "×", no name) | `aria-label` "Close", title "Close (Esc)" (Esc closes it, line 264) | script 65-66 |
| the popup's view tabs | markup InternalFlowHtmlGenerator.cs 185-186 | Activity: "The work inside the services for this call, span by span, as an activity diagram"; Flame Chart: "Where the time went inside the services for this call, span by span, as nested bars" (Q6) | script 199-201, where it makes the tabs live; a title in the markup would be paid in every segment's data |

### 3.1 Left without a title (Q9)

The headings whose own text names what they hold: a feature's card, a rule's, an outline's group, the failure
clusters and each cluster, the background calls (RG 2225, 2282, 2086, 2099, 4839). Both audits name them
(`Headings`) and pass over them. Also left: the example image's summary under server-side rendering (RG 2521, 3973: a
title would show over the whole picture), the labs page's own "Open the report" link and "Every scenario" summary
(`LabsReportGenerator.cs` 84, 115), the public `InternalFlowHtmlGenerator.GenerateWholeTestFlowHtml` (464, 497-498),
which no report calls (tests and consumers only), the CI summary's Markdown and the separate diagnostic report. The
download links the scripts create, click and drop (`context-menu-script.js` 761, 772, 782, 847 at HEAD;
`report-export-function.js` 120, 152) never show.

## 4. Where-words and how they reach the controls

`PayloadsWhere(bool scenarioLevel)` (RG 1106) is the only place the two phrases live. `BuildDetailsRadio(toggles,
scenarioLevel)` passes it to the label, the three buttons and the line count; `BuildFilterToggleButton(name, shown,
scenarioLevel)` to `ToggleTitle(name, diagrams)`; the format and width selects call `NoteFormatTitle(scenarioLevel:
...)` and `NoteWidthTitle(scenarioLevel: ...)` at the four sites that build them (RG 1583, 1586, 1598, 1601). The
scenario bar is built once (`BuildScenarioDiagramToolbar`, RG 1161 at the base) and written into every scenario's
`.scenario-settings`, so an outline group's bar, being the same string, reads "this scenario's" too.

## 5. Owner questions, each with a recommendation

Asked by the brief:

- **Q1, rows 7 and 24 (Clear All).** SB: "Clear every filter: the search, statuses, duration, happy paths, tags and
  dependencies". Built: "Clear every filter: the search with its dep:, duration: and is: terms, and the " and then the
  rows the box has, from the list More filters' title already names (`FilterRows`): statuses always, then duration,
  happy paths, tags, dependencies, categories where the run has them. SB's fixed words are wrong for a run without
  dependencies and silent about categories; the qualifiers are named because Clear All clears them in the search (D16).
  Recommend: as built.
- **Q2, row 47.** "This step has one step inside it: click to show or hide it", and "them" for two or more. Recommend:
  as built.
- **Q3, row 49.** In its tab, History's and Diagnostics' headings are nothing to click (D20), so their titles say what
  the section is (the tab's own words) and no "click". Where the section is a disclosure (the labs page, or a report
  that keeps it out of the tabs), the heading keeps SB's "click to show or hide". Recommend: as built.
- **Q4, rows 34 to 36 (chips).** Built: the count, then what a click does: "2 scenarios. Click: only these. Click
  again: leave them out. Once more: off. Alt+click or right-click leaves them out at once." The same words on every
  chip of the three rows; the chip's own text names what it picks. Alternatives: SB's per-row text with the name
  ("Click: only scenarios tagged api. ...") and no count, which drops D9 and D10's count; or the count alone, as today,
  where the instructions never show (SB defect 3). Recommend: as built.
- **Q5, `button.feature-open`.** "Open this feature's card in the Full view". Recommend: as built.
- **Q6, the popup's view tabs.** "The work inside the services for this call, span by span, as an activity diagram" and
  "Where the time went inside the services for this call, span by span, as nested bars", given by the popup script.
  Recommend: as built.

Raised by the work:

- **Q7, bytes.** The scenario bar's titles are written into every scenario with a sequence diagram. Measured with the
  bar's own builder (`work/measure/Measure.cs`, by reflection on the built DLLs, no report written): the bar grows from
  2,023 to 2,962 bytes (939 bytes, 46%), or from 2,721 to 4,065 with every toggle; the format and width selects add 95
  and 49 more where a run has them. With the diagrams' and the steps' headings a typical scenario carries about 1,036
  bytes more: about 1 MB in a report of 1,000 such scenarios, before compression (`work/bytes.txt`). The report bar's
  titles are paid once (1,527 bytes). The other way is REPORT_FORMAT_PLAN R2's: the scenario bar carries no titles, and
  the page gives a control its title, from a table held once, when it is first pointed at or focused, as
  `report-step-status-titles.js` does for a step's icon; the report bar keeps its titles in the markup. That changes
  `BuildDetailsRadio`, `BuildFilterToggleButton` and the format and width sites at scenario level, a script table, and
  ControlTitlesTests' fact 4 (to a Playwright fact). Recommend: ship as built, which is what the plan asks (titles and
  where-words from the C#), and take R2's way as a follow-up if the bytes matter more than a title in the markup.
- **Q8, the History tab's words.** SB row 4: "what is still failing, new, slower or gone". Built: "what is failing,
  flaky, fixed, slower, new or gone", the lists the section shows (New failures, Failing since, Flaky, Newly fixed,
  Slower, New scenarios, Absent since the previous run). Recommend: as built.
- **Q9, left without a title** (section 3.1). Recommend: as listed.

### 5.1 Wording that differs from SB's table, and why

| Row | SB | Built, and why |
|---|---|---|
| 13 | "... cut at that many lines (the default)" | no "(the default)": `ReportToggleDefaults.Details` sets the start state, so it would be wrong in a report configured otherwise |
| 16 | "Show or hide the HTTP headers in the payloads of {where}" | "the headers at the top of each payload": Kafka headers and gRPC metadata are drawn the same way |
| 20 | "the messages to and from message brokers (Kafka, Event Hubs, Pub/Sub, Service Bus) and their payloads" | "the calls to and from message brokers (the participants drawn as queues), with their payloads": the toggle hides `queue` participants, whatever the broker |
| 23 | no full stop | ends with one, as the product's title did |
| 26 | "... status and duration" | "... status and duration in milliseconds": the column is `data-duration-ms` (`report-export-function.js` 148) |
| 31 | the warm-up clause always | only where the run marked a warm-up, as the percentile buttons' titles |
| 45 | Activity "The steps and the calls each made, as an activity diagram"; Flame "Where the time went, call by call, as nested bars" | both are drawn from the spans inside the services: "The work inside the services, span by span, ..." and "Where the time went inside the services, span by span, ..." |
| 46 | Sequence Diagrams "The sequence diagrams of this part of the scenario"; Failure Result "... the expected and actual values"; Steps "... with how long each took" | "This scenario's sequence diagrams"; "... where it gives them" (the diff shows only when the message holds both); the steps' clause only where a step has a time; a one-view section other than sequence takes its view's words (SB had no rule for Activity Diagrams and Flame Chart) |
| 48 | "Open {name}, a file this step attached" | a scenario's attachments say "this scenario", a URL says "a link", and an image says "Show {name}, an image this {owner} attached, at full size" |
| 50 | "Go to this scenario on the Features tab" | the same in the report; "Open this scenario in the report" on the labs page, whose link opens the report |
| 52 | "Back to the top of the report" | the same, and `aria-label="Back to top"`: the button's text is an arrow, so it had no name |

## 6. D50, settled (SB 955-956)

The product exposes none of the preview's debug globals (`__kronikolNavFilters`, `__kronikolTips`, `__kronikolQuery`,
`__kronikolTokenDiff`): searched in `src/Kronikol` at HEAD with D49 applied, none. (The product's own
`__kronikolRender`, `__kronikolWorkerHost` and `__kronikolSearch` are telemetry the Playwright suite reads, not D50's.)
The two harness checks that read the preview's globals now use the product's own controls and functions:
`nav-check.js` 169-170 clicks `.nav-filters-toggle` where it called `window.__kronikolNavFilters.open()`, and
`q-test.js` 79-85 calls the page function `split_search_qualifiers` (`report-search-function.js`), falling back to
`window.__kronikolQuery.split` so the check still runs against the preview. Nothing is kept as a test hook. Both parse
under node; neither was run (no Playwright here).

## 7. What the generator writes, by file (lines after the scripts)

- `ReportGenerator.cs`: `BuildDetailsRadio` 1063-1079 (the five titles and the line count's name);
  `BuildFilterToggleButton` 1094-1100; `PayloadsWhere` 1106, `ToggleTitle` 1110-1118, `NoteFormatTitle` 1122,
  `NoteWidthTitle` 1126; the view tab 1192 (`BuildDiagramTab`) and `DiagramViewTitle` 1194-1203; the diagrams' heading
  1222-1223; the format and width sites 1583, 1586, 1598, 1601 (the `noteWidthTitle` const at the base's 1553 goes);
  `feature-open` 1781; `clearTitle` 1855; the box's header, search line and phone toggle 1859-1861; the chips' comment
  1886-1887; status 1896; seconds 1923; happy paths 1928; the tabs 2006-2011 and their comment; the cluster link 2143;
  Failure Result 2464 and 3858; the attachment
  calls 2497, 2501, 3842, 3846, 4390, 4395; Next Failure 2633, back to top 2637; `ReportTab` 2892-2894; `FilterChip`
  3024-3031 with `ChipClicks`; `FilterRowsTitle`, `ClearAllTitle`, `FilterRows`, `Listed` 3047-3073 (FilterRowsTitle's
  body moves into `FilterRows`, unchanged); `StatusToggleTitle` 3076, `FiltersLeaveShowing` 3089, `SearchTitle` 3092,
  `SearchHelpTitle` 3095; `NavFilters` 3106-3110; the empty state 3118; the run link 3223; Background Steps 4105;
  Steps 4147-4148; the step table references 4310, 4327; `DiagnosticsDescription` 4853 and the Diagnostics heading
  4877-4880; `AttachmentLink`, `AttachmentImageLink` and `FailureResultTitle` 6702-6724.
- `HistoryHtml.cs`: `Description` 103, `DisclosureTitle` 106; the links 146, 152; the heading 159.
- `ParameterValueRenderer.cs` 202, 649; `InternalFlowHtmlGenerator.cs` 344, 372.
- `plantuml-browser-render-script.js` 178, 1169, 1320; `internal-flow-popup-script.js` 64-66, 198-201;
  `report-step-status-titles.js` 5-7 (comment), 11-19, 33-34.

## 8. Tag chips: the lines D49 touches (for kronikol-89)

D49 touches the chips' titles and nothing else of their markup. At `a1235d98` (HEAD `d385c715` in brackets):

- `ReportGenerator.cs` 1842 [1856]: the comment's last line, "A chip's title counts the scenarios it picks out.",
  becomes two lines that add "and says what a click does (D49)".
- `ReportGenerator.cs` 2978 [2992]: `FilterChip`'s doc comment, "Its title counts the scenarios it picks out.", becomes
  two lines.
- `ReportGenerator.cs` 2981 [2995]: in `FilterChip`'s markup, `title=\"{ScenarioCount(scenarios)}\"` becomes
  `title=\"{ScenarioCount(scenarios)}. {ChipClicks}\"`; after the method, three new lines (a blank line, a summary,
  `private const string ChipClicks = "..."`).
- `ReportGenerator.cs` 2993-3006 [3007-3020]: `FilterRowsTitle`'s body moves, unchanged, into a new
  `FilterRows(features, hasDurations, hasDependencies, status)` that `FilterRowsTitle` and the new `ClearAllTitle` share,
  with the join moved to `Listed`. The rows' names and their `Labels` and `Categories` conditions are the same lines; a
  rebase that renames the rows (TAGS_PLAN) makes that change in `FilterRows`, and Clear All's title follows.
- `FilterRowsTests.cs` 108-111 (the Tags row), 122-126 (Dependencies), 137-139 (Categories): each `title=\"N
  scenario(s)\"` gains ". Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click
  leaves them out at once."
- New: `ControlTitlesTests.A_chip_gives_the_count_of_scenarios_it_picks_out_then_what_a_click_does` reads
  `.tag-toggle, .dependency-toggle, .category-toggle` and the titles of `.dependency-toggle[data-dependency='Caller']`,
  `[data-dependency='OrderDb']` and `[data-none]`; `Clear_All_names...` expects "tags, dependencies and categories" for
  a run with all three rows.

## 9. Tests (written first)

Core, `tests/Kronikol.Tests/Reports/ControlTitlesTests.cs` (new, 18 facts and a theory of 2 cases). Each fact parses the
report with AngleSharp and reads an attribute of the element a selector picks, never a bare substring. The report is
built in the class: one feature with a control of every kind (a happy path with a tag, a category and timed steps, a
background, a composite step, attachments of a file, a link and images on the scenario and a step, two failures that
cluster, a skipped and a bypassed scenario, one with a whole-test flow), every tab (timeline, components, history from
a seeded ledger, diagnostics), a CI run, and the labs page of the same run.

1. Each_tab_of_the_report_bar_says_what_it_shows
2. The_timeline_tab_and_the_seconds_field_name_the_warm_up_only_where_a_scenario_paid_one
3. The_report_bars_payload_controls_say_they_act_on_every_sequence_diagram_in_the_report
4. A_scenarios_payload_controls_say_they_act_on_its_own_sequence_diagrams (four bars)
5. The_filter_box_says_what_each_of_its_controls_does
6. Clear_All_names_the_search_and_every_row_the_box_has_in_the_box_and_in_the_bar (three runs: every row, times only,
   statuses only)
7. Both_readouts_say_what_they_count
8. A_chip_gives_the_count_of_scenarios_it_picks_out_then_what_a_click_does
9. The_pages_own_buttons_and_links_say_where_they_go_or_what_they_do
10. A_scenarios_sections_say_what_they_hold
11. A_section_with_one_view_is_titled_with_what_that_view_shows (theory: activity, flame)
12. An_attachment_says_what_it_opens_and_whose_it_is
13. An_outlines_failed_example_and_its_attachments_have_the_titles_a_scenarios_have
14. A_steps_parameter_buttons_and_a_tables_whole_values_say_what_they_show
15. In_its_tab_a_sections_heading_says_what_the_section_is_and_not_that_a_click_shows_or_hides_it
16. Outside_a_tab_a_sections_heading_says_a_click_shows_or_hides_it (the labs page)
17. A_history_link_says_where_it_goes
18. The_scripts_title_what_they_draw_or_make_live (the render, popup and steps scripts' text)
19. No_control_the_generator_writes_is_left_without_a_title: every `button, a[href], summary, select, input, [role=tab],
    [role=button]` of the report but the headings of section 3.1 and composite steps' summaries has a title, and there
    are more than 80.

Playwright, `tests/Kronikol.Tests.EndToEnd/ControlTitlesAuditTests.cs` (new, 3 facts, collection
`PlaywrightCollections.Reports`):

1. Every_control_that_shows_has_a_title_and_a_name: a report with every control, taken through each state that shows
   one (everything open with the filter rows, the custom seconds field and every scenario's settings; scrolled to the
   end; the bar floating with the box in it; no scenario left; the Summary view; each tab; 390 x 844). In each, every
   control (the selector above) that has a box and is not inside an SVG, after a `mouseover` dispatched on it as a
   pointer would, must have a title (headings excepted) and a name (a title, `aria-label`, `aria-labelledby`, an
   image's alt, a label, or text with a letter or a digit). It asserts the list of misses is empty.
2. A_composite_steps_summary_says_how_many_steps_it_holds_once_it_is_pointed_at_or_focused: no title at first; after a
   dispatched `mouseover`, "This step has 2 steps inside it: click to show or hide them"; after focus on a one-step
   composite, "This step has one step inside it: click to show or hide it".
3. The_internal_flow_popup_titles_its_close_its_view_tabs_and_its_diagnostic_info (`TestPageGenerator`'s popup page).

They keep the suite's rules: no forced click, `PollingInterval = 200` on each `WaitForFunctionAsync`, `.First` and
`.Nth` where a selector can match more than one, local files only (`ServePage` writes one), and `dispatchEvent` where
a pointer event is needed.

**Red and green, measured in scratch** (no worktree; the SDK's C# compiler over copies of v5 HEAD, and a reflection
runner over the facts, `work/run-facts.py`): ControlTitlesTests fails 20 of 20 on the base product (each at its first
missing title) and passes 20 of 20 with the scripts applied. The 22 rewritten classes and ControlTitlesTests, with 11
neighbouring classes that read titles or the same markup (WarmUpHtmlTests, WarmUpPagesTests, DefaultReportCleanTests,
ComponentsTabHtmlTests, TimelineControlsTests, MergeCarriesWarmUpTests, SearchFunctionTests, ReportHtmlEncodingTests,
DeepLinkReportTests, UrlHashFilterTests, ScenarioTruncateAndCollapsibleNotesTests): 693 passed and 2 failed with
everything applied, the 2 being the StepRenderingReportTests faults of section 14, which fail the same way on the base.
The Playwright facts compile (0 errors) and were not run: run them red before `scripts-s12d49.py` (the audit lists the
missing titles; the composite fact fails at its first title; the popup fact at the close's name) and green after. An
audit miss nobody foresaw (a control a script draws in a state the list does not reach) shows there first.

## 10. Pins moved (90 lines in 22 files)

Searched: every `*.cs` under `tests/` (bin and obj left out), 1,022 files at HEAD, for the text just before each place a
title is written (`work/insertion-scan.py`, `work/insertion-scan.txt`), for title readers (`GetAttribute("title")`,
`ToHaveAttributeAsync("title"`, `[title`, `GetByTitle`, `getAttribute('title')`) and for role or label locators. Only
these pin a control D49 changes; the Playwright readers of titles (ThemeTests, ScenarioSettingsTests, FilterBarTests'
More filters, ReportToolbarTests, WarmUpMarkTests, HistorySparklineTests, ComponentsTabTests, WholeTestFlowTests) read
titles D49 leaves as they are. Each row: the line at the base, and what the rewrite puts there (`\"` as the test's C#
writes a quote). A pin that held a control's opening tag now holds it with its title.

| Pin (line at the base) | Change |
|---|---|
| HistoryHtmlTests.cs:124 | after `data-tab=\"history\"` insert ` title=\"This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone\"` |
| HistoryHtmlTests.cs:126, HistoryOutputsTests.cs:327, MergeCommandTests.cs:222 | after `<summary class=\"h2\" tabindex=\"-1\"` insert the same title |
| IngestAttachmentTests.cs:109 | after `href=\"attachments/trace.zip\"` insert ` title=\"Open trace.zip, a file this scenario attached\"` |
| IngestAttachmentTests.cs:130 | after `href=\"attachments/report.png\"` insert ` title=\"Open report.png, a file this scenario attached\"` |
| IngestHostDiagnosticsTests.cs:176 | `<details class=\"report-diagnostics\"><summary>` becomes `...<summary title=\"What Kronikol noticed while building this report: click to show or hide the list\">` |
| PopupNotesByClassTests.cs:47 | `<summary>Diagnostic info` becomes `<summary title=\"Why this call may have no spans: click to show or hide the details\">Diagnostic info` |
| DiagramContextMenuTests.cs:1156 (the same at HEAD) | `<summary>Raw PlantUML</summary>` becomes `<summary title=\"The PlantUML source this diagram was drawn from\">Raw PlantUML</summary>` |
| DiagramToggleReportTests.cs:96, 206; ScenarioToolbarTests.cs:137 | `<summary class=\"h4\">Sequence Diagrams<button` becomes `<summary class=\"h4\" title=\"This scenario's sequence diagrams\">Sequence Diagrams<button` |
| DiagramToggleReportTests.cs:123 | `<summary class=\"h4\">Diagrams<button` gains ` title=\"This scenario's diagrams\"` |
| DiagramToggleReportTests.cs:124 | turned round: `Assert.DoesNotContain("<summary class=\"h4\">Sequence Diagrams", content)` could not fail once every heading carries a title; it is `Assert.DoesNotContain(">Sequence Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle", content)` |
| DiagramToggleReportTests.cs:340 | `<summary class=\"h4\">Activity Diagrams<span` gains ` title=\"The work inside the services, span by span, as an activity diagram\"` |
| DiagramToggleReportTests.cs:367 | `data-dtype=\"flame\">Flame Chart</button><span` gains ` title=\"Where the time went inside the services, span by span, as nested bars\"` |
| FeaturesToolsTests.cs:135, 136 | `class=\"feature-open\"` gains ` title=\"Open this feature's card in the Full view\"` |
| FilterBarTests.cs:84 | `class=\"nav-readout\" aria-hidden=\"true\"` gains ` title=\"The scenarios and features the filters leave showing\"` |
| FilterBarTests.cs:85 | `title=\"Clear every filter\"` becomes `title=\"Clear every filter: the search with its dep:, duration: and is: terms, and the statuses, duration, happy paths, tags and categories\"` (the Bakery has no call) |
| FilterBarTests.cs:98 | `"onclick=\"export_csv()\">",` gains the CSV title (row 26) |
| FilterBarTests.cs:109, ReportTabsTests.cs:153 | `class=\"filters-empty-clear\"` gains ` title=\"Clear every filter\"` |
| FilterRowsTests.cs:92, 93, 94 | the three happy-path segments gain row 32's titles |
| FilterRowsTests.cs:108-111, 122-126, 137-139 | the twelve chips' titles gain the click text (section 8) |
| FilterRowsTests.cs:148, 154 | `class=\"filter-readout\" role=\"status\"` gains ` title=\"The scenarios and features the filters leave showing\"` |
| FilterRowsTests.cs:149 | the box's Clear All gains ` title=\"Clear every filter: the search with its dep:, duration: and is: terms, and the statuses, duration, happy paths, tags, dependencies and categories\"` |
| FilterRowsTests.cs:161, 162 | Export HTML and Export CSV gain rows 25 and 26's titles |
| LabsReportTests.cs:117 | the labs page's history link gains ` title=\"Open this scenario in the report\"` |
| MissingAttachmentTests.cs:406 | the image link gains ` title=\"Show shot#1.png, an image this scenario attached, at full size\"` |
| MissingAttachmentTests.cs:408, 409 | gain ` title=\"Open what?.txt, a file this step attached\"` and ` title=\"Open a%#.log, a file this step attached\"` |
| MissingAttachmentTests.cs:412 | gains ` title=\"Open Trace, a link this scenario attached\"` |
| ScenarioImageLightboxTests.cs:34 | the expected link gains ` title=\"Show {name}, an image this {(name == "step.png" ? "step" : "scenario")} attached, at full size\"` |
| ParameterValueRendererTests.cs:274, 468, 511 | `Assert.Contains("<summary>", ...)` becomes `Assert.Contains("<summary title=\"Show or hide the whole value\">", ...)` |
| ParameterizedGroupRenderTests.cs:928, StepRenderingReportTests.cs:101 | `<summary class=\"h4\">Steps</summary>` gains ` title=\"This scenario's steps\"` (no step has a time) |
| ParameterizedGroupRenderTests.cs:1330, StepRenderingReportTests.cs:984 | `<summary class=\"h4\">Background Steps</summary>` gains row 46's Background title |
| ParameterizedGroupRenderTests.cs:1059, 1079 | the outline's Assertions and Steps toggles gain the scenario-level titles (rows 17, 18) |
| ReportGeneratorDiagnosticsScopeTests.cs:65, ReportTabsTests.cs:88 | `<summary tabindex=\"-1\">` gains ` title=\"What Kronikol noticed while building this report, such as calls it could not put under a scenario\"` |
| ReportTabsTests.cs:58-59 | the `Tab` helper writes ` title=\"{TabTitle(key)}\"` after `data-tab`; a new `TabTitle` switch holds rows 1 to 5 (the timeline's without the warm-up clause: none of these runs paid one) |
| RunBandTests.cs:57 | the run link gains ` title=\"Open the CI run this report came from\"` |
| ScenarioToolbarTests.cs:86, 87, 103, 104; ToggleDefaultsMarkupTests.cs:439, 440, 449 | the view tabs gain row 45's titles |
| ScenarioToolbarTests.cs:147-151 | the scenario bar's label, three buttons and line count gain the scenario-level titles, and the line count `aria-label=\"Payload length\"` |
| ScenarioToolbarTests.cs:162, 163 | the scenario Headers and Steps toggles gain their titles |
| ScenarioToolbarTests.cs:173 | `title=\"Payload format\"` becomes `title=\"How the payloads in this scenario's sequence diagrams are written: JSON as captured, or the same data as YAML\"` |
| ScenarioToolbarTests.cs:174 | `title=\"Payload width. Wrap: ...\"` becomes `title=\"How wide the payloads are in this scenario's sequence diagrams. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.\"` |
| ToggleDefaultsMarkupTests.cs:178, 618 (the merge renderer's), 179, 182 | the report bar's Expanded, Truncated and line count gain the report-level titles (and the line count its name) |
| ToggleDefaultsMarkupTests.cs:184 | the scenario bar's Expanded gains its title |
| ToggleDefaultsMarkupTests.cs:212, 213, 220, 227, 234 | Headers (report and scenario), Assertions, Steps and Databases gain their titles |
| ToggleDefaultsMarkupTests.cs:279 | the report's width title becomes `How wide the payloads are in every sequence diagram in the report. Wrap: ...` |

`work/pin-table.md` has every line with the text around it, and `work/tests.diff` the whole diff.

## 11. Mutations (`checks/mutate-s12d49.py`, 101)

Each undoes one title, or one where-word or condition a title holds, and names the test that must fail: 93 run the
core suite (`ControlTitlesTests`, and for the renderer and popup sites `ParameterValueRendererTests` and
`PopupNotesByClassTests`), 8 the Playwright suite (the popup's close name, close title, view tabs and their order; the
composite summary never titled and one step counted as many; and two the browser audit catches: a feature's name in
the Summary view, and back to top with neither name nor title). `--check [root]` checks every anchor once (101 of 101
in `dry/`, in `work/head-dry/` at HEAD, and in `work/dry-crlf/`), and an optional argument runs only the mutations whose
test name holds it: `python mutate-s12d49.py ControlTitlesAuditTests` runs the 8 Playwright ones. It matches and
mutates with LF and puts each file back byte for byte.

**The 93 core mutations were run in scratch and all 93 are killed** (`work/mutate-scratch.py`: for each, the product
rebuilt with the one file mutated, then the named fact run by the scratch runner; `work/mutate-scratch.txt`). The 8
Playwright ones are for the worktree run: `python plans/OPTION_C_PLAN.harness/checks/mutate-s12d49.py
ControlTitlesAuditTests`.

## 12. Draft text for later steps

**Changelog, `[Unreleased]` (5.0.0), under Added:**

- **Every control says what it does.** Each tab of the report bar, each payload control, each filter, each heading of
  a scenario's sections, each link and the page's own buttons has a tooltip saying what it does or what it holds. A
  payload control's tooltip names the diagrams it acts on: every sequence diagram in the report on the report bar,
  the scenario's own on a scenario's bar. Clear All's names the search and every filter row the box has. A chip's gives
  the number of scenarios it picks out and then what a click does. A composite step's says how many steps it holds,
  given when it is first pointed at or focused. In their tabs, the History and Diagnostics headings say what the
  section is, not that a click opens it.

and in the Tags entry already there, "(its tooltip says how many scenarios carry it)" becomes "(its tooltip says how
many scenarios carry it, and what a click does)".

**Under Fixed:**

- **Three controls have names.** The back-to-top button (an arrow), the payload line count dropdown and the
  internal-flow popup's close (a cross) had no name a screen reader could read; they are "Back to top", "Payload
  length" and "Close".

No version bump on branch v5 (5.0.0 only).

**Kronikol4J ledger** (for `ledger-s10.md`, after S12 D43's line):

- S12 D49: the run report writes a `title` on every control: the report bar's tabs; the payload controls (the radio's
  label, buttons and line count, the toggles, the format and width selects) with "every sequence diagram in the
  report" on the report bar and "this scenario's sequence diagrams" on a scenario's, and `aria-label="Payload length"`
  on the line count; the filter box (readouts, Clear All built from the rows the box has, exports, search field and
  help, status chips, seconds field, happy-path segments, the phone's Filters); the chips' titles add the click text
  after the count; the view tabs and the diagrams' heading; Failure Result, Background Steps and Steps headings;
  attachments (`AttachmentLink`, `AttachmentImageLink`: file, link or image, the scenario's or a step's); the step
  table references; the cluster links; Next Failure; back to top (`aria-label="Back to top"`); the CI run link; the
  Summary view's feature names. `HistoryHtml` titles its heading (in the tab and as a disclosure) and its links;
  `ParameterValueRenderer` its whole-value summary; `InternalFlowHtmlGenerator` its diagnostic info. Page resources:
  `plantuml-browser-render-script.js` (Raw PlantUML), `internal-flow-popup-script.js` (the close's name and title, the
  view tabs' titles), `report-step-status-titles.js` (a composite step's summary, on first pointer or focus). Kronikol4J
  writes the controls without them: byte parity breaks until mirrored.

**Wiki** (`C:/Code/Kronikol.wiki`):

- `Generated-Reports.md`, "Report Features": a bullet: "**Tooltips** (5.0.0): every control has one saying what it does
  or what it holds. The payload controls name the diagrams they act on (every sequence diagram in the report on the
  report bar, the scenario's own on a scenario's bar), Clear All names what it clears, and a tag, dependency or
  category chip gives its count and then what a click does." In the same list, the "Export Filtered HTML / CSV" bullet
  can say the CSV's duration is in milliseconds.
- `Internal-Flow-Tracking.md`, where it describes the popup (line 21): "The popup closes with its × button (named
  Close) or Esc; its Activity and Flame Chart tabs say what each draws."
- `Search-Syntax.md`, optional: "Hovering the search field shows the syntax in one line; ? opens the full help."
- `Report-Configuration.md`: nothing (no option changes).

**Plan log line** (OPTION_C_PLAN): "S12 D49 and D50: a title on every control (53 rows of SB 3.1: 36 added, 7 changed,
10 already there; 8 controls outside the table), the where-words from the C#, the scripts only for what they draw; D50
settled (no debug global; nav-check.js and q-test.js use the product's controls). Tests: ControlTitlesTests (18 facts
and a theory), ControlTitlesAuditTests (3 Playwright facts); 90 pin lines in 22 files. Mutations: checks/mutate-s12d49.py
(101)."

## 13. Dry run (2026-10-19)

`dry-base/` holds exactly the 31 files the scripts edit, from `a1235d98` (`git show`), and an empty
`tests/Kronikol.Tests.EndToEnd/`. `work/dry-run.py <base> <out> [--crlf] [--same-as <folder>]` copies the base, applies
`scripts-s12d49.py` and then `tests-s12d49.py`, runs each again, and checks every file's byte order mark and line ends
against its base copy.

- `dry/` from `dry-base/` (LF, as git holds the files): both scripts exit 0 (9 product and harness files; 22 test files
  and the 2 new classes); a second run of each refuses (`AssertionError: S12 D49 is in already`; `('already there:
  S12 D49 tests are in already', ...)`) and leaves every file as it was; 31 files changed and 2 new; ReportGenerator.cs
  keeps its BOM and no other file gains one; every file stays LF. `dry-after.md5` lists the results.
- `work/dry-crlf/`: the same base with CRLF line ends throughout, as a checkout with `core.autocrlf=true` holds it:
  both scripts apply, every file is written back CRLF (the new Playwright class stays LF, having no file beside it in
  the dry run; in a worktree it takes its neighbours' ends), and every file equals `dry/`'s once its line ends are LF.
- `work/head-dry/` from `work/head-base/` (the same 31 files at `d385c715`): both scripts apply and refuse a second
  run; the edits to ReportGenerator.cs and DiagramContextMenuTests.cs, the two files that differ, are line for line the
  same as at `a1235d98` (`work/head-dry-after.md5`).
- `python checks/mutate-s12d49.py --check dry` (and `work/head-dry`, `work/dry-crlf`): 101 of 101 anchors found once;
  on `dry-base/`, 0 (every anchor is D49's text).
- Compile, in scratch with the SDK's compiler, below normal priority: the product with the edited files and its XML
  documentation (as the project builds it, so a broken `cref` would show), 0 errors and 0 warnings in them, and 336
  warnings in all, as at the base (`work/compile-product.py`); the core test project with the package's 23 files, 0 errors and 0
  warnings in them; the Playwright project with the new class, 0 errors and 0 warnings (`work/compile-check.py core
  --asp`, `e2e --asp`).
- The edited scripts parse under node (`report-step-status-titles.js`, and each `<script>` block of
  `internal-flow-popup-script.js` and `plantuml-browser-render-script.js`), and so do `nav-check.js` and `q-test.js`.
- Runs (section 9): ControlTitlesTests 0 of 20 on the base, 20 of 20 after; 693 passed and 2 failed (section 14) over
  34 classes after, and with `fix-img-facts.py` too, StepRenderingReportTests 62 of 62.
- Mutations: 93 of 93 core mutations killed in scratch (`work/mutate-scratch.txt`); the 8 Playwright ones not run.

## 14. Found on the way, outside D49

**Two facts of StepRenderingReportTests fail on v5 since D43 (c7abf98f).** `Report_renders_non_image_attachments_as_links`
and `Report_renders_scenario_level_non_image_attachment_as_link` check `Assert.DoesNotContain("<img", content)` over the
whole page. D43's `context-menu-script.js`, which every report embeds, writes `'<img alt="Diagram" ...'` into the tab
it opens for a PNG or an SVG (lines 271, 280 at HEAD), so both fail whatever the attachment does: measured in scratch,
they fail on `d385c715` unchanged, with and without D49. D43's commit ran the context menu's classes only, so the full
core run on v5 will show them. `fix-img-facts.py <root>` anchors both on what the generator writes for an attachment
image, `Assert.DoesNotContain("<img class=\"attachment-image\"", content)`, as the image fact beside them does. Proved
in scratch: both fail on `d385c715` before the fix and pass after (with `Report_image_attachment_is_wrapped_in_clickable_link`,
3 of 3), and with D49 applied the class passes 62 of 62; with the product mutated to draw a text attachment as an
image, both fixed facts fail. It applies before or after D49 (its lines are not D49's) and wants its own commit.

## 15. The package

| File | What it is |
|---|---|
| `design.md` | this |
| `scripts-s12d49.py <root>` | the product edits (12 sections) and D50's two harness checks |
| `tests-s12d49.py <root>` | copies the two new classes in, rewrites the 90 pin lines |
| `ControlTitlesTests.cs`, `ControlTitlesAuditTests.cs` | the new classes, copied by `tests-s12d49.py` |
| `checks/mutate-s12d49.py` | the mutations, rooted on its own place in `plans/OPTION_C_PLAN.harness/checks/` |
| `fix-img-facts.py <root>` | section 14, not D49 |
| `dry-base/`, `dry/`, `dry-after.md5` | the dry run at `a1235d98` |
| `ref/` | the parts of the `a1235d98` tree the scratch compile checks build against |
| `work/` | the scratch tooling and its output: `dry-run.py`, `head-base/` and `head-dry/` (at `d385c715`), `dry-crlf/`, `compile-*.py`, `run-facts.py` and `runner/`, `mutate-scratch.py` and its log, `insertion-scan.py`, `pin-table.py`, `rows.py`, `bytes.py`, `measure/`, `inventory.py` |
