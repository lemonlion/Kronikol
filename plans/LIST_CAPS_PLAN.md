# List caps plan (#161)

**Status:** written 2026-10-19 at 4.27.0 (`6f6043e2`) by kronikol-71, at the owner's ask ("critically analyse issue
161 and create a detailed, well researched plan to fix its underlying issue(s)"). Revised the same day after a
verification pass the owner asked for ("make sure the plan is complete and fully accurate, and do any deep dives that
might change the plan"): section 10 lists what changed. **Executing** since the owner's ask of 2026-10-19 ("implement
the plan in full"), with section 9's recommendations; CLAUDE.md's rule to fix every bug found along the way answers Q7,
so the things in 3.10 are fixed rather than filed. **Executed: R1 is 4.28.0, R2 is 4.31.0, and the two other things
3.10 found (the ANSI codes and the lightbox) are 4.31.1.** Section 10 logs each release.

**Issue:** [#161](https://github.com/lemonlion/Kronikol/issues/161), "`query failures` keeps a scenario's first four
attachments and doesn't say there are more" (filed 2026-10-08 on a 4.6.0 report, label `bug`).

**Harness:** `plans/LIST_CAPS_PLAN.harness/`. `repro.sh` ingests a copy of the real Playwright 1.62.1 capture the repo
already holds (`tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-attachments`, from the #175 plan's probe) in
three lanes, then runs `query failures` (text, `--json`, `--json --out`) and `query steps`, and keeps `Failures.md` and
`Failures.jsonl`. The lanes:

- **recipe:** `--tests` alone, as the wiki's reporter recipe writes it, with no `step` index.
- **messages:** with playwright-bdd's Cucumber messages.
- **hooks:** with the messages and `--include-hooks`.

`census.py` compares what each reader shows with what the data holds (`results/census-4.26.9.md`).
`results/json-record-sizes-4.26.9.txt` holds the sizes in section 3.4. `r1_bytes.py` renders R1's attachment lines from
each lane's `Failures.jsonl` and measures what they add to a failure (section 3.12).

The harness ran on Kronikol.Tool 4.26.9, installed with `--tool-path` under the session's scratchpad. 4.27.0 was
tagged but not yet listed on nuget.org. `QueryCommand.Narrative.cs`, `QueryWriter.cs`, `ReportIndex.cs` and
`FailuresDigestGenerator.cs` are unchanged between `v4.26.9` and `6f6043e2`. Windows 11, .NET SDK 10.

Line numbers are to `6f6043e2` (4.27.0), still the tip of `origin/main` when the verification pass ran. Wiki lines
are on `origin/master` `21914fa` (4.27.0's `sizes` edit, which moved `Querying-Reports.md` below its line 359). Short
names used below:

| Short name | File |
|---|---|
| Narr | `src/Kronikol/Query/QueryCommand.Narrative.cs` |
| QW | `src/Kronikol/Query/QueryWriter.cs` |
| QO | `src/Kronikol/Query/QueryOptions.cs` |
| FDG | `src/Kronikol/Reports/FailuresDigestGenerator.cs` |

---

## 1. Summary

The issue is right, and it understates the defect. The repo's own Playwright capture takes a screenshot after each step.
Ingested as the wiki's reporter recipe writes it, `query failures` prints 4 of a scenario's 10 attachments: the start
screenshot and three step screenshots. The failure screenshot, the end screenshot, the video, `error-context` and the
trace are all cut, in the text and in `--json`, and nothing says so. Ingested with playwright-bdd's messages, it prints
5 of 10: `error-context` and the trace are cut, and three screenshots on steps that passed go unmentioned. With the
hooks kept, the same artefacts sit on the after-hook and fixture steps, and `failures` shows 1 of 10, again with no
count.

There are five underlying causes:

1. **No view prints a scenario's own attachments in full.** `steps` prints only those made in a step. So the cap had
   no view to name, and it could not follow the tool's own rule that "an ellipsis always has an address" (QW:538).
2. **The cap keeps the wrong end.** Producers attach failure-time artefacts last: Playwright does at teardown, and so
   does a .NET after-hook that calls `Track.Attachment`. So "the first four" is the end least likely to matter.
3. **Inner lists have no shared primitive.** `QueryWriter.Page` renders a listing and its footer "so no command can page
   without saying so" (QW:358). A list inside a record is a hand-written `.Take(n)`, and silence is the default.
   - A sweep found 34 list caps across the query tool, `history gate`, the failures digest and the CI summary. Twelve
     are silent in at least one output, and nothing checks them.
   - The defect came back after the issue was filed: 4.26.4 capped a failing step's attachments at six, with a
     "… N more" line in the text and a silent `Take(6)` in `--json`.
4. **`--json` has no convention for a capped inner list.** There is one precedent: `summary`'s `failed` and
   `failedTotal`.
5. **`Failures.md`, the documented fallback, misses most failures.** It works through one failure per cluster. Every
   other failure, even in a two-failure run, gets a table row with no attachments. The skill tells agents that
   `Failures.md` "lists every one".

The plan has two releases:

- **R1 (#161), a minor**, as the #175 plan's Q14 made 4.24.0 a minor for one new `Failures.jsonl` member, by
  CLAUDE.md's "a new option, public type or member" (approved with that plan).
  - A full view of a scenario's attachments, which every cut names. Recommended: a paged verb, `attachments sN`, in
    text and `--json`. The alternative, `steps sN`, cannot page, so a long list would push the step tree out of the
    byte budget (F10, Q1).
  - `failures` keeps both ends of a capped attachment list (the first one and the last five). In place of the cut it
    prints a line naming what was cut and the view that lists it.
  - It counts attachments on steps that did not fail, and names the view for cut calls.
  - `--json` carries the same entries and a total for every capped list.
  - `Failures.md` counts the attachments of every failure it does not work through.
- **R2, a patch** unless it adds totals to other verbs' JSON (Q5), can be split off from R1. It adds a list-cut
  primitive to `QueryWriter` and announces the remaining silent caps through it, which also fixes `diff --body`'s
  count. A census fact and a source lint make a new silent cap fail the tests.

The issue's second suggestion, giving `failures --json` every attachment, is not taken as stated. `--json` is under the
same 6,000-byte budget and drops a record that does not fit whole, so a failure with about 20 attachments would vanish
from the answer (section 3.4). The recommended `attachments sN --json` gives every attachment, one small item each,
paged like any listing, which is the suggestion in a form the budget allows.

R1 costs bytes. A failure gains up to two attachment lines, a cut line when a list runs past six, and a line for
attachments on other steps, so a default page of `failures` holds one or two fewer failures (measured in section 3.12).
Nothing is lost, because `failures` pages.

There are eight questions for the owner (section 9), and eight things found on the way that this plan leaves alone
(section 3.10), two of them wrong counts of the same class.

## 2. What the issue says, checked

| # | The issue says | Checked at `6f6043e2` |
|---|---|---|
| 1 | `query failures <report> s1` lists the first four attachments, in the text and in `--json`, and nothing says there are more | **True, still at 4.27.0.** Narr:144-146 (text) and Narr:217-218 (JSON). The census shows the first 4 of the scenario's own 10 in the recipe lane, and of its own 6 in the messages lane. |
| 2 | The cap is `Take(4)` at L135 and L200 of `8f653f6` | **Moved, not changed:** it is now at L145 and L217. |
| 3 | The calls under a failing step are capped too (`Take(6)`), but that list starts with its count | **True for the text** (Narr:134-135). The JSON is `Take(6)` with no count (Narr:200-203), and neither names a view that lists the rest (F7). |
| 4 | The attachments have no count | **True for the scenario's own list.** Since the issue was filed, 4.24.0 nested a failing step's attachments under it. 4.26.4 then capped those at six with "… N more attachment(s): steps sN/path" (Narr:126-129). Their `--json` is a silent `Take(6)` (Narr:215). |
| 5 | The two that are cut are the ones you most want | **True in the issue's report.** On a capture with step screenshots, five of the six that matter are cut, the failure screenshot among them (census, recipe lane). |
| 6 | No other verb lists a scenario's attachments: `steps` prints only a step's own | **True.** Narr:347-348; `scenario.Attachments` is read only at Narr:144-145 and Narr:217. |
| 7 | `Failures.md` lists them all, but only for the failures it writes out in full; a "further failures" row has none | **True, and the row is not only for a run past the budget.** A failure clustered with another is never worked through, so a two-failure run already has such a row (FDG:665-692; census `s4` in all three lanes). |
| 8 | So the trace can only be found in `Failures.jsonl` or the HTML | **True.** `Failures.jsonl` lists every attachment of every failure (FDG:919, FDG:981-983). |
| a | Suggestion: say how many were left out | **Necessary, not sufficient.** The line must name a view that prints the rest, and none exists (F1). |
| b | Suggestion: give `--json` all of them, or at least an `attachmentCount` | **The count, yes. All of them, no:** see the budget in section 3.4. |
| c | Suggestion: list every name on one line and the paths of the first few | **Not enough on its own:** a name is not the file (`screenshot` is `attachments/test-failed-1.png`). The names are kept as part of the cut line (4.2). |

## 3. Findings

### 3.1 F1: no view prints a scenario's own attachments

`steps` (Narr:295-358) prints each step's attachments (L347-348) and never the scenario's. `failures` is a digest. The
data holds both lists: `Scenario.Attachments` and `ScenarioStep.Attachments`. The reader gives an attachment to the
innermost open step, or else to the scenario (`ReportScanner.cs:477-487`).

So the query tool has no complete view of a scenario's own attachments, and a cut in `failures` had nothing to point
at. The step list's "… N more" line (4.26.4) could name `steps`; the scenario's could not.

Which attachments are the scenario's own depends on the producer:

- every Playwright artefact written by the wiki's reporter recipe. It writes no `step` index
  (`Integration-Playwright.md` L105-114), and L129 says so;
- whatever a .NET test attaches while no step is active (`Track.cs:164-168`, through
  `StepCollector.AddAttachment`, `StepCollector.cs:243-266`);
- a Cucumber hook's attachments when hooks are dropped (`CucumberFeatureSynthesizer.cs:536-538`).

### 3.2 F2: the cap keeps the end least likely to matter

In the census's recipe lane, the scenario's ten attachments are, in order:

1. `screenshot-start.png`
2. `after-step.png`
3. `step-shot.png`
4. `after-step.png`
5. `after-step.png`
6. `screenshot` (`test-failed-1.png`)
7. `screenshot-end.png`
8. `video`
9. `error-context`
10. `trace`

`failures` prints the first four. Playwright attaches its failure screenshot, video, error context and trace when the
test ends, after anything the test attached itself. The capture's last five records share one millisecond
(`tests.ndjson` lines 7-11), so only the ingest's stable sort (`FeatureSynthesizer.cs:89`) keeps them in order. A .NET
suite that attaches a screenshot in an after-hook has the same shape.

The text cuts in this verb already keep both ends, because "an assertion's verdict is at the end" (Narr:63-65). A list
cut should do the same.

### 3.3 F3: the defect is a class, and it came back

Appendix A lists every list cap the sweep found: 34, of which 12 are silent in at least one output. A listing's page goes
through `QueryWriter.Page`, which "renders a paged listing and its footer in one place, so no command can page without
saying so" (QW:358). A list inside a record has no such primitive. Each site writes `.Take(n)`, followed by a line only
if its author remembered.

No fact or lint checks that a cap is announced. The only cap fact,
`DigestSuppressionCapTests.Every_cap_says_how_many_it_left_out` (L78-88), covers two of the digest's caps: on 1,200
failures it finds "more not listed" in `Failures.md` and all 1,200 in `Failures.jsonl` (Appendix A rows 27 and 29).

4.26.4 shows the cost. It was written by an audit that knew about #161. It added a correct "… N more" line to the text,
and a silent `Take(6)` to the JSON of the same list.

### 3.4 F4: "give --json all of them", measured against the budget

`--json` has the same budget as the text: 6,000 bytes by default (QO:425). Each item is charged whole, and a record that
does not fit is dropped whole, with `truncated` and `next` saying so (QW:239-270). The wiki says items "are costed and
appended whole, because half a serialized row is not JSON" (`Querying-Reports.md` L315-319).

Measured on the harness (`results/json-record-sizes-4.26.9.txt`):

- A failure record is 1,745 bytes in the recipe lane and 2,957 bytes in the messages lane.
- One attachment entry is about 206 bytes, with a 161-character path. A CI runner's paths are shorter.
- In the messages lane, the default budget already holds only one record of two (`total` 2,
  `truncated.droppedItems` 1, `next` with `--offset 1`).

A record holding every attachment would stop fitting at all at about 20 attachments in the messages lane and 25 in the
recipe lane. `failures --json` would then return no record for that scenario, only a note to raise `--max-bytes`. A
scenario with a screenshot after each of fifteen steps, plus Playwright's five artefacts, has twenty.

`--json --out` lifts the budget but not the cap: it still prints four (census).

### 3.5 F5: attachments on steps that did not fail get no mention

`failures` prints the scenario's own list and each failing step's list (Narr:116-139). An attachment on any other step
is neither shown nor counted. With `--include-hooks`, playwright-bdd's own artefacts are on the after-hook step
(`CucumberFeatureSynthesizer.cs:549-555`). In the census's hooks lane, `failures` shows 1 of 10 (the failing step's
own), and nothing says that nine more exist. `steps s0` shows all ten.

### 3.6 F6: the documented fallback misses the same failures

`Failures.md` works through at most `MaxDetailedFailures` (25, FDG:56) failures, after clustering. A failure whose error
shares its first line with another is listed under its cluster and is not worked through (FDG:665-667). Every failure
that is not worked through gets one row of `| Address | stableId | Scenario | Error |` (FDG:677-692), and no
attachments.

The census run has two failures. `s4` is in the same cluster as `s0`, so its own ten artefacts (its own trace and its own
video) are in no list meant for a person.

The skill tells agents the opposite: "`scripts/query.py` lists none: read `Failures.md`, which lists every one with the
step that holds it" (`SKILL.md` L164-166, in both copies).

### 3.7 F7: cut calls name no view, and their JSON has no count

"{n} calls in this step:" (Narr:134) gives the count, but only six calls are listed, and nothing names the view that
lists the rest. That view is `flow sN/P`, the same question as `flow sN --step P` (Narr:494-495, the step filter at
L505). The JSON `calls` is `Take(6)` with no count (Narr:200-203).

The two do not count the same calls. `failures` lists the calls made in the step itself (`StepPath == path`, Narr:131
and L201). `flow`'s step filter covers the step's sub-steps too (`Address.PathCoveredBy`, L505). So `flow sN/P` lists
every call `failures` cut, and for a step with sub-steps it lists more.

### 3.8 F8: the code keeps a record of the divergence

The doc comment on `FailureRecord` (Narr:158-172) lists how its JSON differs from `Failures.jsonl`, including "The
scenario's own attachments are capped at four here and uncapped there". That list is worth keeping, and R1 updates it.

### 3.9 F9: a report's own query.cs keeps its version's cap

`query.cs`, written beside every report, loads the `Kronikol.dll` of the version that wrote the report and calls
`QueryCommand.Run` by reflection (`QueryScriptGenerator.cs` L115-155, L216-226). So a report written before R1 keeps the
cap when read through its own `query.cs`. `kronikol` (the tool) at R1 or later reads the same report with the fix. The
changelog says so.

### 3.10 Found on the way, not in this plan

- **ANSI colour codes.** Playwright's reporter API gives `result.error.message` with SGR escape sequences, and the
  wiki's recipe writes it to the tests file as it is: the fixture's `tests.ndjson` has 24 per failure. Nothing in `src/`
  strips them. The ingest keeps the text (`FeatureSynthesizer.cs` L140 and L193), and so do `QueryWriter.Flat` and
  `OneLine`. So in the recipe lane they reach `query failures` and `Failures.md`'s cluster heading, error block and
  table, which puts them in the cluster key too (census output). playwright-bdd's messages carry the same error without
  them. When both files are ingested, the messages' text wins (`CucumberFeatureMerger.cs` L333), which is why the
  messages and hooks lanes are clean. No issue is filed (Q7).
- **The HTML lightbox (checked).** A scenario-level image is a link with `target="_blank"` and no `onclick`
  (`ReportGenerator.cs` L2273, and L3288 in the parameter panel). A step-level image calls `openLightbox` (L3876). The
  lightbox binds only through that `onclick` (`report-lightbox-function.js` L7-12), so the shared CSS class brings no
  lightbox. `Integration-Playwright.md` L116 promises one for the failure screenshot, which is scenario-level.
  `FileAttachment.cs` L16, `TestRunRecord.cs` L155, `Ingesting-External-Captures.md` L211 and `Step-Tracking.md` L388
  say the same. Only the step-level `onclick` is tested (`AttachmentRenderingTests.cs` L116-119). A fix changes report
  output, so it needs a Kronikol4J ledger entry (Q7).
- **The CI summary's count (checked).** The green-run diagrams section lists the scenarios that have a diagram
  (`CiSummaryGenerator.cs` L141-146), shows up to `maxDiagrams` (L151-163), then prints "*{n} more scenario(s) not
  shown — see full report*" (L168) with n = every scenario in the run minus those shown (L31, L165). So 40 scenarios
  with 3 diagrams print "*37 more scenario(s) not shown*" when nothing was cut. Kronikol4J ports the line
  (`CiSummaryGenerator.java` L175) (Q7).
- **`diff --body` counts its own marker (checked).** When either body is not JSON, `DiffLines` keeps 20 differing lines,
  then adds an `elided` row, "… more lines differ — --out both bodies and diff the files", and stops counting
  (`QueryCommand.Diff.cs` L476-504). `Page` counts that row as a difference (L337-339): the footer says "21 paths
  differ", `--json` gives `total` 21 with a `{"kind":"elided"}` item, and `--count` prints 21 (L315-319), whether 21
  lines differ or 2,000. Appendix A row 16; R2's primitive fixes it (4.9).
- **The skill and the wiki date the cap of six to 4.24.0.** `references/commands.md` L152-154 says "since 4.24.0: the
  first six", and `Querying-Reports.md` L508-509 "from 4.24.0". 4.24.0 printed every attachment of the failing step;
  the cap arrived in 4.26.4 (`CHANGELOG.md` L375-378). R1 rewrites both sentences (section 7).
- **Two collision suffixes.** A run's copier names a collision `name_2.ext` (`ReportGenerator.cs` L6183-6199), and
  `kronikol merge` names it `stem-1.ext` (`MergeableReportRenderer.cs` L306), reusing a file with the same bytes
  (L314-315). Cosmetic.
- **`scripts/query.py` prints no attachments.** This is documented (`SKILL.md` L164-166) (Q7).
- **#148** (a missing attachment keeps its path, with no diagnostic). Every line this plan adds prints the path as
  `AttachmentEntry.Resolve` returns it, so #148's "(not found when the report was drawn)" can later be added to the same
  line. No conflict.

### 3.11 F10: `steps` and `flow` cannot page, so neither can hold a long list

`steps` and `flow` print a whole tree, by design: "the narrative layer ... is 0.4% of a report by size, which is why
these commands hand back whole trees rather than pages of them" (Narr:5-9). Neither goes through `QueryWriter.Page`,
and neither takes `--offset`, `--limit` or `--json` (`VerbTable.cs` L161-165 and L173-177). Past the byte budget the
writer drops the line that does not fit and every line after it (QW:167-187), then ends with "… output truncated at
6000 bytes · raise with --max-bytes, or filter harder" (QW:441-447). That announces the cut, but it gives no exact
resume, which a paged listing's `next:` does (QW:403-409).

An attachment list is not narrative-sized. An attachment line is about 200 bytes with the harness's paths and about 130
with a CI runner's. So a list of about 25 to 40 attachments printed at the top of `steps sN` would fill the default
budget, and the step tree would be cut away. The first version of this plan made `steps sN` the full view without this
measurement; section 4.1 now recommends a listing that pages.

The same limit applies to `flow sN/P` as the pointer for cut calls (F7). A `flow` line is 100 to 150 bytes, so a step
with forty or more calls can run past 6,000 bytes. Such a step is rare, and the truncation line says how to raise the
budget. T8 runs the pointer with `--max-bytes 0`, so the fact does not depend on where the budget falls.

### 3.12 F11: what R1 costs a page of `failures`

`r1_bytes.py` renders R1's attachment lines for each failure in the three lanes from `Failures.jsonl`. It measures them
twice: with the harness's paths (a 161-character report path), and with a CI runner's report path
(`/home/runner/work/shop/shop/tests/e2e/Reports/`, 46 characters).

| Lane | Attachments (own + failing step + other steps) | Bytes added per failure | Failures per 6,000-byte page |
|---|---|---|---|
| recipe | 10 + 0 + 0 | +433 to +438 (harness), +271 to +276 (CI) | 5 → 3 (harness), 7 → 5 (CI) |
| messages | 6 + 1 + 3 | +459 to +463 (harness), +293 to +297 (CI) | 3 → 2 (harness), 4 → 3 (CI) |
| hooks | 0 + 1 + 9 | +155 (the other-steps line) | 7 → 5 (harness), 7 → 6 (CI) |

The last column is 6,000 divided by a failure's bytes, before and after, leaving out the footer. The cut lines name
`attachments`, as recommended (4.1); naming `steps` would save 6 bytes a line. `results/r1-bytes-4.26.9.txt` holds every
row.

So a default page holds one or two fewer failures. Nothing is dropped: `failures` pages through `QueryWriter.Page`
(Narr:89), so the page ends with a `next:` line, and `--max-bytes` and `--out` lift the budget. The trade is deliberate:
the bytes buy the four artefacts that explain a Playwright failure. A failure with six attachments or fewer, and none on
other steps, costs nothing more.

## 4. Design

### 4.1 The full view: `attachments sN` (Q1)

A new verb lists every attachment of one scenario, or of one step and its sub-steps. On the messages lane's `s0`:

```
$ kronikol query attachments Reports s0
s0     screenshot-start.png → …/attachments/6-screenshot-start.png
s0     screenshot → …/attachments/test-failed-1.png
s0     screenshot-end.png → …/attachments/11-screenshot-end.png
s0     video → …/attachments/video.webm
s0     error-context → …/attachments/error-context.md
s0     trace → …/attachments/trace.zip
s0/0   after-step.png → …/attachments/7-after-step.png
s0/1   step-shot.png → …/attachments/8-step-shot.png
s0/1   after-step.png → …/attachments/9-after-step.png
s0/2   after-step.png → …/attachments/10-after-step.png
10 attachments
```

- **Order:** the scenario's own attachments, then each step's in `AllSteps` order: background steps, then steps, each
  followed by its sub-steps (`ReportIndex.cs` L175-189). The digest walks steps the same way (FDG:496-513), so the list
  matches `Failures.jsonl` entry for entry.
- **A step address** (`attachments s0/1`) lists that step's attachments and its sub-steps', the scope a step address
  has in `steps` and `flow` (`Address.PathCoveredBy`).
- **Paging:** through `QueryWriter.Page`, as `failures` does, with `--offset`, `--limit`, `--count` and `--json`. The page
  ceiling is 200, as for most listings (`QueryOptions.PageSize`), so the byte budget decides where a page ends, and the
  `next:` line resumes there exactly.
- **`--json`:** one item per attachment, `{name, path, step}`, the field names `Failures.jsonl` uses, with `step` null
  for the scenario's own. `path` is resolved as `failures` resolves it. An item is about 200 bytes, so a long list costs
  pages, never a whole record. This is the issue's "give `--json` all of them" in a form the budget allows.
- **Declared** as one `VerbTable` row in the Narrative group, next to `steps`. It adds no flag.

Unscoped `steps sN` gains one line after its header lines when the scenario holds attachments, for example
`attachments: 6 of its own, 4 on its steps · attachments s0`. The detail view then says they exist, and stays a tree. A
scoped `steps sN/k` prints no scenario line, as today.

Every cut in sections 4.2 and 4.3 names this view: `attachments sN` for a scenario's list, and `attachments sN/P` for a
step's.

**The alternative (Q1)** is the first version of this plan: unscoped `steps sN` prints the scenario's own attachments in
full after its header lines, and every cut names `steps`. It adds no verb. Against it, measured: it cannot page (F10),
so about 25 to 40 attachments fill the default budget and cut the tree away; and `steps` has no `--json`, so a script
would still find no complete list in the query tool.

### 4.2 A capped attachment list keeps both ends and names the cut

Both attachment lists in `failures` (the scenario's own, and each failing step's) use one cap, six. That is today's
`MaxFailingStepAttachments`, renamed `MaxAttachmentsShown`. A capped list keeps the first entry and the last five, and
prints one line in place of the cut:

```
  attachment: screenshot-start.png → …/attachments/6-screenshot-start.png
  … 4 more attachment(s) (after-step.png ×3, step-shot.png): attachments s0
  attachment: screenshot → …/attachments/test-failed-1.png
  attachment: screenshot-end.png → …/attachments/11-screenshot-end.png
  attachment: video → …/attachments/video.webm
  attachment: error-context → …/attachments/error-context.md
  attachment: trace → …/attachments/trace.zip
```

- **The names in the parentheses** are the cut entries' names, in the order of first appearance. A repeated name appears
  once with ×N. Whole names are added while the list stays within 100 characters, then `, …` ends it. This is not
  `QueryWriter.OneLine`, which cuts at the character and would end the 4.3 example on `screenshot-…`
  (`FailureText.Truncate`, `FailureText.cs` L117-130).
- **The address** is the scenario's (`attachments s0`) for its own list, and the step's (`attachments s0/3`) for a
  step's list. Today's step cut line names `steps s0/3` (Narr:129); it moves to the new view with the rest.
- **With six entries or fewer**, everything is printed and no line is added.

The split is one at the start and five at the end. The first attachment is the test's own starting state, the cheapest
context for the rest. Playwright's failure set is four or five entries: the screenshot, video, error context and trace,
plus a fixture's end screenshot. The split is Q3.

### 4.3 What `failures` does not list, it counts

After the scenario's own list, if the scenario holds attachments on steps other than its failing steps, `failures`
prints one more line, with names as in 4.2:

```
  … 9 more attachment(s) on other steps (after-step.png ×2, step-shot.png, screenshot, screenshot-start.png, error-context, trace, …): attachments s0
```

That is the hooks lane's `s0`. Its failing step `s0/3` holds one attachment, and the other nine are on two earlier steps
(`s0/1` and `s0/2`: `after-step.png` ×2 and `step-shot.png`), the "After Hooks" step `s0/4` (`screenshot`,
`screenshot-start.png`, `error-context`, `trace`) and two fixture steps (`s0/5` and `s0/6`: `screenshot-end.png` and
`video`). The names run to 88 characters through `trace`, and `screenshot-end.png` would take them past 100. In the
messages lane the line is `… 3 more attachment(s) on other steps (after-step.png ×2, step-shot.png): attachments s0`,
after all six of the scenario's own.

This closes F5 without listing them in `failures`. That keeps the #175 plan's Q15, under which hook artefacts are listed
by `Failures.md` and `steps`, not by `failures` (`ATTACHMENT_MERGE_PLAN.md` L563, which left "a cap" to #161). What
changes is that `failures` now says they exist, and `attachments` lists them.

### 4.4 `--json`: the same entries, and a total for every capped list

The JSON keeps the entries the text prints (both ends), so the two cannot disagree. It adds totals as sibling members.
They are always present, by the envelope's rule for `total` (`Querying-Reports.md` L311-314):

| Member | Counts |
|---|---|
| `attachmentsTotal` | the scenario's own attachments |
| `stepAttachmentsTotal` | the attachments on all of its steps, background steps and sub-steps included (`ScenarioEntry.AllSteps`, the walk FDG:496-513 makes), so that `attachmentsTotal + stepAttachmentsTotal` is the number `Failures.jsonl` lists |
| `failingSteps[].attachmentsTotal` | the attachments on that failing step |
| `failingSteps[].callsTotal` | the calls made in that failing step |

`attachments` stays an array of `{name, path}`. So `formatVersion` stays 1, and no consumer of today's shape breaks
(`QueryJsonTests.cs` L385-396). The precedent is `summary`'s `failed` and `failedTotal` (`QueryCommand.Overview.cs`
L114-123).

### 4.5 Calls under a failing step

The text keeps "{n} calls in this step:". When it cuts, it ends the list with "… {n-6} more call(s): flow sN/P", which
names the view that lists them (Narr:489-515). `flow sN/P` also lists the calls of the step's sub-steps (F7), and it
does not page (F10); neither changes. The JSON gains `callsTotal` (4.4), which counts what `failures` counts: the calls
made in the step itself.

### 4.6 `Failures.md` counts the attachments of every failure it does not work through

- **The further-failures table gains an `Attachments` column.** It holds the failure's count (its own attachments plus
  every step's, as `Failures.jsonl` lists them), or nothing when there are none. The table has at most 100 rows
  (FDG:79), so the column costs a few bytes per row.
- **The sentence above the table gains a pointer:** "`kronikol query attachments {here} sN` lists a row's
  attachments". `{here}` is the report token every command in the digest already uses (FDG:608-611, L679).
- **The cluster table keeps its columns.** Every cluster member is also in the further-failures table.
- **Nothing reads the table back.** Its header exists only in the generator (FDG:680). The one fact on it asserts the
  heading (`FailuresDigestGeneratorTests.cs` L325), and the only fact that splits table cells reads another table
  (`DigestFencingTests.cs` L91-99).
- **The skill's sentence (`SKILL.md` L164-166) is made true:** "`Failures.md` lists every attachment of each failure it
  works through, and counts them for the rest; `kronikol query attachments <report> sN` lists a scenario's." The
  same `SKILL.md` lines say `scripts/query.py` lists none, which stays true (4.7).

### 4.7 What does not change

The data files, the HTML, `Failures.jsonl`, ingest, merge, how `query.cs` loads the engine, and `scripts/query.py`
(Q7).

### 4.8 Options considered

| Option | For | Against | Taken |
|---|---|---|---|
| Say how many were cut (issue's suggestion a) | Cheap | There is no view to name today | Yes, with 4.1 |
| `failures --json` lists every attachment (issue's suggestion b) | Complete | The budget drops the whole record at about 20 attachments (F4) | No. `attachments sN --json` lists every one, paged (4.1); "every attachment under `--out`" is offered in Q2 |
| Every name on one line (issue's suggestion c) | Cheap | A name is not a file | In part: the cut line names the cut entries |
| Raise the cap to 10 | Simple | Still silent past 10, and a failure's lines nearly double | No |
| Keep the last six | Keeps Playwright's set | Loses the start state, and a step's first attachment can be the thing under test | No |
| Rank by media type (trace first) | Suits Playwright | A heuristic per producer, and .NET attachments carry no media type (`Track.cs` L164-168) | No |
| A new `attachments sN` verb, text and `--json`, listing every attachment with its step | Pages, so it holds a list of any length (F10); JSON included; one list in `Failures.jsonl`'s order | One more verb to declare, test and document. R1 is a minor either way, so it costs no version | Yes (4.1, Q1) |
| `steps sN` prints the scenario's own attachments (this plan's first version) | No new verb; `steps` is the scenario's detail view | Cannot page: 25 to 40 attachments fill the budget and cut the tree away (F10); no `--json` | The alternative in Q1 |

### 4.9 R2: one primitive and a guard for every list cut

**The primitive.** `QueryWriter.ListCut` (the name is open) takes a list, a cap, a head size, a noun and the address of
the full view. It returns the entries to print and writes the cut line in their place. JSON callers take the same
selection and add a `<list>Total` member. R1's sites move onto it.

**Every silent cap in Appendix A gets the same treatment**, naming a view where one exists:

- **`steps` parameters** (Narr:343, cap 8): the unscoped tree cuts with "… N more parameter(s): steps sN/k", and a
  scoped `steps sN/k` prints them all.
- **`steps`' step attachments** (Narr:347-348) are not capped at all, in a view that cannot page (F10): a step with 60
  attachments fills the budget by itself. Once `attachments` exists they take the same cut as `failures` (one and five),
  naming `attachments sN/P`.
- **`services` status kinds** (`QueryCommand.Overview.cs` L443-444, cap 4): "+N more", pointing at `--json`, which
  carries the whole map (L385).
- **`sizes`' largest scenarios** (`QueryCommand.Sizes.cs` L93-121, cap 10): the text says "The 10 largest of N"; the
  JSON's `largestScenarios` gains that N. It is a new JSON member, so it is one of the totals Q5 asks about.
- **`grep --values` and `grep --number` paths** (`QueryCommand.Search.cs` L131, and `QueryCommand.NumberGrep.cs`
  L116-122, which stops at four with no `Take`): "… N more path(s)".
- **`diff --body` lines** (`QueryCommand.Diff.cs` L476-504, cap 20): the marker stops being a row, so the footer,
  `total` and `--count` count the differences, and the marker says how many lines it left out (3.10).
- **`compare`** (`QueryCommand.Search.cs` L307-325, cap 20): "… N more difference(s)". Today the heading counts the
  sequences, not the differences.
- **`history sN`** (`QueryCommand.History.cs` L411, the last 15): "{n} earlier point(s) not shown: history sN --json".
  The count at L408 counts the stream's runs, not this scenario's points, so the cut cannot be worked out today. The
  JSON carries every run (L801).
- **`Failures.md` and `Failures.jsonl`:** "Steps before it" (FDG:276, cap 3) and the calls table (FDG:352, cap 8) each
  get a count line pointing at `kronikol query steps` or `flow` for that failure. A comment says `flow` is the answer
  (FDG:61-66), and the digest's command block names `flow` for any scenario (FDG:610), but nothing marks the cut.

**Declared exceptions**, each with its reason in a registry:

- page sizes (`Page`);
- `summary`'s "Slowest:", a ranking labelled as such (`QueryCommand.Overview.cs` L132-138);
- the diagnostics' first message per kind, which is counted with ×N.

**The guard** has two parts:

- `CapCensusTests` builds one report in which every capped list is over its cap. It runs every verb that prints such a
  list, in text and `--json`, and asserts for each list that the cut line or total is there with the right number, and
  that its pointer, when run, prints every entry. A vacuity fact asserts that each list in the fixture really is over
  its cap.
- A source lint, `ListCapSourceTests` (after `CultureInvariantSourceTests`), lists every `Take(` and `TakeLast(` with a
  constant under `src/Kronikol/Query/` and in the digest. It fails on any that is neither a `ListCut` call nor in the
  registry, so that adding a cap is a decision someone writes down. A cap written as a counter, like `grep --number`'s
  (row 33), is outside what such a lint can see, so the census fact is the guard for it.

## 5. Releases

| Release | Bump | Contents | Closes |
|---|---|---|---|
| R1 | Minor: a new verb (4.1), new JSON members (4.4) and a new `Failures.md` column (4.6), by the precedent of #175's Q14 (Q6) | 4.1 to 4.6, the docs in section 7, the fact changes in 6.3 | #161 |
| R2 | Patch, if it adds no JSON member (a `<list>Total` for another verb would make it a minor, Q5) | 4.9 | nothing filed; Appendix A's silent rows |

Version numbers are claimed when each release is pushed (section 8). Neither release changes report output (HTML,
PlantUML or embedded scripts). R1 changes `Failures.md`, a file Kronikol4J does not write.

## 6. Tests

Following TDD: each fact is written first and run red against the release before R1 (4.27.0 today, or whatever is newest
when R1 starts). It is then made green.

### 6.1 Fixtures

- **Real:** the Playwright 1.62.1 capture (`playwright-bdd-9.2-attachments`), ingested in the three lanes through the
  ingest CLI path that `CucumberAttachmentIngestTests` already uses (L416-445).
- **Built:** a report with two failing scenarios:
  - `s0` holds 60 attachments of its own;
  - its step `s0/0` passes and holds 9 attachments, 4 of them on its sub-step `s0/0.0`;
  - its step `s0/1` fails and holds 60 attachments and 47 calls;
  - `s1` fails with the same message, so the two are one cluster.

  So `attachments s0` lists 129 (60 + 9 + 60), and `failures` says 9 more on other steps.

  It is written from a `Feature[]` with `ReportGenerator.GenerateTestRunReportData`, as the query facts write theirs
  (`QueryCultureTests` L202-257, the call at L251), because T3 needs sub-steps. `DigestStepAttachmentTests` ingests a
  tests file instead (L127-146, its helper at L181-200), which holds top-level steps only. Every fact runs through
  `QueryCommand.Run`, the public entry point, so it compiles unchanged on the previous tag for the red proof.

### 6.2 R1 facts

| ID | Fact | Asserts | Red on 4.27.0 because |
|---|---|---|---|
| T1 | `Failures_keeps_both_ends_of_a_scenarios_attachments_and_names_what_it_cut` | Recipe lane `s0`, in order: `screenshot-start.png`, then `… 4 more attachment(s) (after-step.png ×3, step-shot.png): attachments s0`, then `screenshot`, `screenshot-end.png`, `video`, `error-context`, `trace` | The first four are printed, and there is no cut line |
| T2 | `Attachments_lists_every_attachment_in_the_digests_order` | All three lanes and the built report: `attachments sN`, text and `--json`, gives every entry of `Failures.jsonl` for that failure, in its order, with its step address | The verb does not exist |
| T3 | `A_step_address_lists_that_steps_attachments_and_its_sub_steps` | Built report: `attachments s0/0` lists 9, the 4 of `s0/0.0` among them, and none of the scenario's own | The verb does not exist |
| T4 | `Every_attachment_cut_names_a_view_that_prints_every_entry` | Built report: for each cut line, run the command it names, follow its `next:` lines to the end, and find every cut entry's path | There is no cut line for the scenario list |
| T5 | `Failures_counts_attachments_on_steps_that_did_not_fail` | Hooks lane `s0`: the 4.3 line, ending `trace, …): attachments s0`; messages lane `s0`: `… 3 more attachment(s) on other steps (after-step.png ×2, step-shot.png): attachments s0`; the built report likewise | Nothing is said |
| T6 | `Failures_json_carries_the_texts_entries_and_every_lists_total` | In all three lanes and the built report: the JSON names equal the text's, plus `attachmentsTotal`, `stepAttachmentsTotal`, `failingSteps[].attachmentsTotal` and `callsTotal`; own plus step totals equal `Failures.jsonl`'s count | The members are absent, and the first four are kept |
| T7 | `A_failure_with_sixty_attachments_still_fits_the_default_budget` | Built report, `failures s0 --json` (scoped, so `s1` cannot take the budget): the record is present and `truncated` is null | Guard, green on 4.27.0 (cap 4): proves R1 kept a cap (M8) |
| T8 | `Failures_names_the_view_for_a_steps_cut_calls` | Built report: `… 41 more call(s): flow s0/1`, and that command, run with `--max-bytes 0`, prints every one of the 47 calls' addresses | There is no pointer |
| T9 | `Failures_md_counts_the_attachments_of_a_failure_it_does_not_work_through` | Recipe lane: the `s4` row says 10, and the sentence names `kronikol query attachments {here} sN` with the digest's own report token | There is no column |
| T10 | `ListCut` unit facts | Selection for n = 0, 1, 6, 7 and 60 at cap 6, head 1: the positions and the cut count | Unit spec; proved by mutations M1, M3 and M11 |
| T11 | `Attachments_pages_a_long_list_and_resumes_exactly` | Built report, `attachments s0` at the default budget: the first page ends with `next:`, and following it gives the rest with no entry twice; `--json` gives `total` 129 and `next`; `--count` prints 129 | The verb does not exist |
| T12 | `Steps_says_a_scenario_has_attachments_and_names_the_view` | Recipe lane `steps s0`: the `attachments: 10 of its own · attachments s0` line after the header; none in `steps s0/k`; none for a scenario without attachments | There is no such line |

`QueryCultureTests` runs `failures` (its row at L75) under de-DE, fi-FI, th-TH and ar-SA (the theory at L104-119). Its
fixture (L202-257) has no attachments, so R1 gives one failing scenario seven, and adds a row for `attachments`, so that
the cut line's count and the new verb are held to the invariant culture.

The new verb also enters the facts that hold every verb to the one table: `DescribeTests` (the `describe` listing and
every flag, L58, L103-115 and L174), `CountFlagTests` (its list of verbs that take `--count`, L35) and `SkillDriftTests`
(the skill's `commands.md` held to the verbs and their flags).

### 6.3 Existing facts that change

- `CucumberAttachmentIngestTests.cs` L444-445 asserts that the scenario's own lines (two spaces, then `attachment:`) are
  `screenshot-start.png`, `screenshot`, `screenshot-end.png` and `video`. The fact ingests the messages too, so this is
  the messages lane's list of six, cut at four. Under R1 all six fit the cap: the expected list gains `error-context`
  and `trace`, with no cut line. The other-steps line follows them, and the fact's pattern does not match it.
- `DigestStepAttachmentTests.cs` (60 attachments on a failing step):
  - L244: six four-space `attachment:` lines. Still six, but the first and the last five: `…-declined-00.png`, then
    `-55` to `-59` (the fixture numbers them 00 to 59, L234-238).
  - L245: the whole line `    … 54 more attachment(s): steps s0/0` becomes the 4.2 form, with the cut entries' names
    and `attachments s0/0`, and it moves to after the first entry. Each name is its file name, 57 characters
    (`Path.GetFileName`, L238), so one fits before `, …`.
  - L248: the six JSON paths change the same way.
  - L252-253: `steps` still prints `declined-06.png` in R1. Its output is already cut at the default budget (60 lines of
    about 220 bytes, F10), and nothing checks the later ones. R2's cut of `steps`' step attachments (4.9) moves the fact
    to `attachments s0/0`.
  - L172: the scenario's JSON list stays `["trace"]`.
- `QueryJsonTests.cs` L391-396 checks that eleven keys are present: add `attachmentsTotal` and `stepAttachmentsTotal`,
  and `attachmentsTotal` and `callsTotal` on the failing step the fact already reads (L398-400).

### 6.4 Proofs and mutations

**Red proofs.** Every fact runs through `QueryCommand.Run` or the ingest CLI, so the new test files compile on the
previous tag unchanged. Run them in a worktree at that tag, with nothing stubbed, and read each failure's message (a fact
can fail for the wrong reason). T7 is a guard, green on 4.27.0, and is proved by mutation (M8).

**R1 mutations**, each of which must be killed. Run them in a snapshot worktree (`git stash create`), and keep
`mutations.py` in the harness.

| ID | Mutation | Killed by |
|---|---|---|
| M1 | Keep the first six (head 6) | T1, T6 |
| M2 | Drop the cut line | T1, T4 |
| M3 | Count off by one | T1, T6 |
| M4 | `attachmentsTotal` = the printed count | T6 |
| M5 | A step address lists the scenario's own attachments too | T3 |
| M6 | A step's cut line names the scenario | T4 |
| M7 | The "other steps" count includes the failing steps | T5 |
| M8 | The JSON lists every attachment | T7 |
| M9 | The `Failures.md` column counts the scenario's own attachments only | T9 |
| M10 | `attachments` lists the steps' attachments before the scenario's own | T2 |
| M11 | Repeated names are not grouped | T1 |
| M12 | `attachments` resumes one entry late (`next:` offset + 1) | T11 |
| M13 | `steps` prints its `attachments:` line in a scoped view | T12 |
| M14 | The name list is cut with `OneLine` at 100 characters | T5 |

### 6.5 R2 facts

- `CapCensusTests`: one theory row per declared cap, read from the registry.
- Its vacuity fact.
- `ListCapSourceTests`, with a control: a planted silent `Take(5)` in a source string is found.
- Mutations: remove each announcement in turn, and its census row goes red.

### 6.6 Checks for each release

- The core suite, including `SkillDriftTests` and `InitAgentsCommandTests` (the two skill copies and the embedded one),
  `DescribeTests` and `CountFlagTests` (the verb table), `QueryCultureTests` and `FallbackScriptTests`.
- `release.slnf` built in Release for every target. `Query/` compiles for net10.0 only, so no code under `Reports/` may
  call the new helper.
- No Playwright run is needed: no HTML changes.
- `repro.sh` and `census.py` run on the built tool, with the table kept as `results/census-<version>.md`. It should show
  the target row in every lane.

## 7. Documentation and ledger

**Wiki:**

- `Querying-Reports.md`:
  - L507-511 (`failures`): both ends, the cut line, the count for other steps. L508-509's "from 4.24.0: the first six"
    becomes 4.26.4's (3.10), and L509-510's "first four" goes.
  - L513-525 (the example): an attachment and a cut line. It has no attachment line today.
  - L357 (the `--json` table): the totals on `failures`, and a row for `attachments`.
  - L572-576 (`steps`): the `attachments:` line.
  - A section for `attachments`, after `steps`.
  - L1508-1511 (the verbs `query.py` implements) stays as it is: `attachments` is not one of them.
- `Generated-Reports.md` L437-438, L497 and L503-505: the query against the digest, and the further-failures column.
- `Integration-Playwright.md` L116-129: where Playwright's artefacts appear in `query failures` and `attachments`. L116's
  lightbox sentence is Q7's.

**Skill** (both copies are byte-identical; `SkillDriftTests` holds them equal up to line endings, L413-437, and
`InitAgentsCommandTests` holds the installed copy to `templates/` byte for byte, L60-69):

- `references/commands.md`: L152-154 (`failures`: its "since 4.24.0: the first six" becomes 4.26.4's), L158-159 (the
  JSON members), L175 (`steps`), and an entry for `attachments`, which `SkillDriftTests` requires for any verb.
- `SKILL.md`: L53-55 and L164-166.

**Doc comments:** Narr:12 (the constant), Narr:124-125, Narr:158-172 (the divergence list) and Narr:214; the
FDG:677-679 sentence.

**CHANGELOG:** R1's section states the minor (the new verb, the new JSON members and the `Failures.md` column), the
`query.cs` note (F9), the docs' corrected version for the cap of six, and that #161 is closed.

**Kronikol4J:** it has no query tool and no `Failures.md` (its `README.md` L8-11). R1's column gets one entry in its
ledger (`docs/REMAINING_PARITY.md`), "not mirrored, a ledger entry only", as 4.3.0's digest change did (L2404). Check the
ledger's tip on origin before writing: the local checkout's newest entry is 4.16.3, and the #175 entries are on origin.

**ROADMAP.md:** in Appendix C (L1025), the #161 cell points at this plan once it is committed.

**#161:** close it after R1 is published, with a comment naming the release, and confirm with `gh issue view 161`.

## 8. Coordination

- **This plan touched nothing shared.** The research was read-only. The harness ran on a published tool installed under
  the session's scratchpad. The plan is written in worktree `C:/Code/Kronikol-161` (branch `plan/161-list-caps`), and
  origin was read through private refs. Live when it was written: kronikol-0b, kronikol-39 and kronikol-22 (busy).
- **Files R1 touches:** `src/Kronikol/Query/QueryCommand.Narrative.cs` (and the new verb, there or in a file of its
  own), `src/Kronikol/Query/VerbTable.cs` and the dispatch in `QueryCommand.cs` (L257 is `steps`'s),
  `src/Kronikol/Query/QueryWriter.cs` (the helper), `src/Kronikol/Reports/FailuresDigestGenerator.cs` (the table), the
  facts above, the skill and three wiki pages.
- **Peers in the same files at the verification pass:** kronikol-39's 4.27.1 (REPORT_FORMAT_PLAN R2 and its section 8)
  edits the skill's `scripts/query.py` and has uncommitted wiki edits near `Querying-Reports.md` L1484 and
  `Generated-Reports.md` L557, below every line this plan cites. Rebase onto the newest `main` and re-anchor the wiki
  lines before R1 edits either. The #154, #179 and #78 plans touch none of R1's files.
- **Files R2 touches:** most of the `QueryCommand.*.cs` files, and the digest.
- **`REPORT_FORMAT_PLAN.md`'s execution** (worktree `Kronikol-krkl`, branch `release/r2-tooltip` at the time of
  writing) works in the query tool's reader and in `sizes`. R2 overlaps its files more than R1 does.
- **Before either release:** announce the files and the version number by SendMessage, and claim a version with
  "taking X now" only when about to push.
- **`PLANS_STATUS.md`:** this plan's row only. No `ROADMAP.md` row is taken (Q8).

## 9. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | The full view: a paged `attachments sN` verb (4.1), or `steps sN` printing the scenario's own attachments (this plan's first version)? | The verb. It pages, so a list of any length comes whole and resumes exactly, and its `--json` lists every attachment. `steps` can do neither (F10). R1 is a minor either way. The first version recommended `steps`, before F10 was measured. |
| Q2 | `failures --json`: the text's entries plus totals; every attachment when the budget is off (`--out`, `--max-bytes 0`); or every attachment always? | Entries plus totals, with `attachments sN --json` for the whole list. "Always" drops a failure with about 20 attachments at the default budget (F4), and "when unbudgeted" gives one verb two shapes. |
| Q3 | The cap and the split: six, the first one and the last five, or another? | Six, one and five (4.2). |
| Q4 | `Failures.md`: a count column for the failures it does not work through, or every attachment of every failure (one line each, for up to 100 rows)? | The count column. The file's budget is spent on the failures it works through. |
| Q5 | Scope: R1 and R2, or R1 now and R2 later? | Both. R2 is what stops the class coming back (F3). If R2 adds JSON totals for other verbs, it becomes a minor; without them it stays a patch. |
| Q6 | The version: R1 as a minor, by #175's Q14, or as a patch, reading the totals as part of a bug fix? | A minor. |
| Q7 | The things in 3.10: file issues for the ANSI codes, the HTML lightbox (after re-checking it), the CI summary's count and `query.py`'s attachments, or leave them? | File the four, each with its evidence. Leave the suffixes. |
| Q8 | A ROADMAP row for this plan, or leave #161 in Appendix C until it is green-lit? | Leave it until it is green-lit. |

## 10. Log

**2026-10-19 (kronikol-71):** written at 4.27.0.

- The issue's claims were read against `6f6043e2` and `8f653f6`.
- Three read-only sweeps ran: every list cap in the query tool, the digest and the CI summary; every producer and reader
  of attachments; and every document and fact that pins these outputs. Each finding this plan relies on was re-read in
  the code, including Appendix A's rows marked "checked".
- The harness ran on Kronikol.Tool 4.26.9, which has the same code as 4.27.0 for these files.
- Nothing was built, and the shared checkout was not written to.

**2026-10-19, verification pass (kronikol-71),** at the owner's ask. `origin/main` was still `6f6043e2`. The wiki had
moved to `21914fa`, and its lines were re-anchored. Three read-only sweeps re-read every citation (code, tests, skill,
wiki, changelog and Kronikol4J) and every Appendix A row. Each discrepancy they reported was re-read by hand before the
plan changed.

- **Measured:** `steps` and `flow` cannot page (F10), and what R1 adds to a failure (F11, `r1_bytes.py`). Q1's
  recommendation moves from extending `steps` to a paged `attachments` verb, which also answers the issue's `--json`
  suggestion.
- **Corrected:**
  - the messages lane shows 5 of 10, not 4;
  - the name list is cut at whole names, not by `OneLine`;
  - the calls pointer is `flow sN/P`, which covers sub-steps;
  - `stepAttachmentsTotal` covers background steps and sub-steps;
  - 34 caps, 12 of them silent (rows 33 and 34 were missed);
  - rows 10, 15, 16, 22, 25, 26 and 29 to 32;
  - the fixtures and the existing facts in section 6;
  - what Q14 decided;
  - the wiki and skill lines.
- **Found:** `diff --body` counts its own marker as a difference. The skill and the wiki date the cap of six to 4.24.0,
  not 4.26.4. The lightbox and the CI summary's count were checked, and both hold.
- Nothing was built, and the shared checkout was not written to.

**2026-10-19, R1 executed as 4.28.0 (kronikol-71),** in worktree `C:/Code/Kronikol-161` on branch `release/161-r1`,
rebased onto 4.27.1 (`d4158464`) before release.

- **Built as designed** (4.1 to 4.6): `QueryWriter.CutList`, `NameList` and `CutLine` (the primitive R2 extends), the
  `attachments` verb, `failures`' cut lines, other-steps line and calls pointer, the `--json` totals, `steps`' line and
  the `Failures.md` column. Every `attachment:` line of `failures`, `steps` and `attachments` is rendered by one helper
  (`AttachmentText`, with `AttachmentObject` its JSON form), so #148's R3 marker goes in one place (agreed with
  kronikol-7c, which is executing `MISSING_ATTACHMENTS_PLAN.md`).
- **Decided where the plan was silent:** a cut line whose first cut name is over 100 characters drops the parentheses
  (`… N more attachment(s): attachments sN`); the calls pointer sits at the calls' indentation; `attachments` on a
  scenario with none says so in a note and exits 0.
- **Facts:** T1 to T12 as section 6.2 has them, in `QueryAttachmentListTests` and `QueryListCutTests` (T10), plus four:
  the totals against `Failures.jsonl` in all three lanes, the built scenario's order, a scenario without attachments,
  and the refusals. T9 runs in all three lanes: in the recipe lane every attachment is the scenario's own, so it alone
  could not kill M9. In tests, a lane is ingested as `repro.sh` does it, with an empty capture file and
  `--allow-empty`, since the tests file alone is not an input.
- **Red proof:** on 4.27.0's code 19 of the 20 facts failed, each at the missing verb, the missing member or the missing
  line (the messages were read); T7 passed, as the guard it is. **Mutations:** 19 of 19 killed: M1 to M14, and M15 to
  M19 for `callsTotal`, the calls pointer, the sub-steps in `steps`' count and in `stepAttachmentsTotal`, and a failing
  step's JSON keeping its first six (`mutations.py`, `results/mutations-r1.txt`).
- **Existing facts changed as 6.3 says,** and one more: `DigestStepAttachmentTests.Query_failures_prints_the_failing_steps_attachments_under_it`
  held that a passing step's screenshot appears nowhere in `failures`; the other-steps line now names it, so the fact
  holds that no `attachment:` line prints it and that the count line does.
- **Q7:** the lightbox fix changes the same HTML lines as #148's patch (`ReportGenerator.cs`, the scenario-level image
  links), so it waits for that patch to reach `main`.
- **Measured on the built tool** (`release.slnf` in Release): `repro.sh` gained an `attachments sN` call per failure,
  and `census.py` columns for it, for the JSON totals and for the digest's new column. `results/census-4.28.0.md` shows
  the target row in every lane: each failure's ten attachments are shown or counted by `failures`, listed by
  `attachments sN`, totalled in `--json`, and counted by `Failures.md` for the failure it does not work through. R1
  adds +433 to +438 bytes to a failure in the recipe lane, +459 to +463 in the messages lane and +155 in the hooks lane,
  r1_bytes.py's estimates to the byte (`results/r1-bytes-measured-4.28.0.txt`).
- **Checks:** the core suite passes 7,824 with 2 skipped, after the version bump and the rebase onto 4.27.2 (its first
  run failed one guard, `ProcessGlobalStoreTests`: a class that runs an ingest must sit in the `DiagramsFetcher`
  collection, and `QueryAttachmentListTests` now does); `release.slnf` builds in Release for every target, with no
  warning from the changed files.

**2026-10-19, R2 executed as 4.31.0 (kronikol-71),** in worktree `C:/Code/Kronikol-161` on branch `release/161-r2`,
from 4.28.0 (`978b1b1c`) and rebased onto 4.30.0 (`aa311cd7`) before release. The number moved twice while it was built:
kronikol-7c took 4.29.0 (#148 R3) and kronikol-39 4.30.0 (`REPORT_FORMAT_PLAN.md` R3).

- **Built as designed** (4.9): every silent row of Appendix A says what it cut and names a view that prints the rest.
  `steps` cuts a step's parameters past eight (`… N more parameter(s): steps sN/P`) and its attachments as `failures`
  does; `services` ends a long status mix with `+N more` and names `services --json`; `sizes --json` carries
  `largestScenariosTotal`; `grep --values` and `--number` end a body's last row with how many more paths it holds and
  `body b:… --keys`; the line diff of `diff --body` no longer counts its marker, and its footer, `total` and `--count`
  count every line that differs; `compare` counts the differences in its heading and names both trees past twenty;
  `history sN` counts the runs before its last fifteen and names `--json`; `http b:` and `body` name
  `interactions --json` past twelve carriers; `Failures.md` counts the steps before the last three and the calls past
  the table's eight, naming `steps` and `flow`, and `Failures.jsonl` carries `stepsBeforeTotal` and `callsTotal`.
- **Decided where the plan was silent:** a step's parameters keep their first eight (they are read in order, a name
  then a value); `grep`'s count goes on the last row it prints, so its rows, `--count` and footer still count rows;
  `QueryWriter.Page` gained an optional `total`, so `diff --body` pages over the lines it lists while its footer and
  JSON count all of them; `http b:` and `body` name `interactions --json`, the one listing that gives every call's body
  hashes; `history sN`'s line goes above its runs, since the ones it leaves out are the earliest; in `Failures.md` the
  steps line is the first bullet under "Steps before it" and the calls line follows the table's blank line, so a
  renderer does not read it as a row.
- **Q5:** three new members (`largestScenariosTotal`, `stepsBeforeTotal`, `callsTotal`), so a minor. **Q7:** the CI
  summary's green-run count (3.10) is fixed here; the ANSI codes at ingest and the scenario-level lightbox follow as
  patches.
- **Guards** (4.9 and 6.5): `ListCapRegistry` holds every cap with a census row, the fact that holds it, or the reason
  it needs none; `ListCapSourceTests` refuses a `Take` or `TakeLast` with a constant that no entry covers (its control
  plants a silent `Take(5)` in a real file and finds it on its line), holds each entry to the lines it names, and checks
  that each fact an entry names exists; `CapCensusTests` runs one row per census cap, plus a fact that every check is a
  registered cap and the vacuity fact, which reads each cap from the code by reflection. `history sN` is held by its
  own fact, since the census report has no ledger, and R1's caps by R1's facts.
- **Found by the registry and fixed:** four caps announced before #161 had no fact (`summary`'s failed scenarios,
  `history`'s absent scenarios, `history --run`'s list when no run matches, and the run diff's Tracking section), and
  `DigestSuppressionCapTests` accepted either of the digest's two elision lines; each is held now, the elision lines
  with their numbers. One more: no fact read the digest's
  `callsTotal` for calls attributed to the failing step, so `DigestCallScopeTests`' cap fact reads it now, with
  the `Failures.md` line, and the mutation of that total (N22) is killed by it.
- **Fixture traps:** a run lacking more than a tenth of the last full run's roster is read as partial
  (`HistoryVerdicts.PartialThreshold`), and a partial run prints no absent list, so that fact's last run has 212
  scenarios; the run diff's Tracking section counts its `total` row.
- **Red proof:** the facts for each fixed list were written first and failed on 4.28.0's code at the missing line or
  member. The guards passed on their first run, as guards do; the planted control is what shows the lint can fail, and
  the named-fact check failed until the four new facts existed. **Mutations:** 30 of 30 killed (`mutations-r2.py`, `results/mutations-r2.txt`), each
  announcement removed or miscounted in turn. The run was paused after N21 for a peer's Playwright run and resumed
  with N22 to N30 after the rebase onto 4.30.0.
- **Checks:** the core suite passes 8,099 with 2 skipped after the version bump and the rebase onto 4.30.0; its one
  failure was the CI fault this release fixes (`ZstdCopyTests`, 19 of 19 with the fix, run with no package in the
  build output, as on CI). `release.slnf` builds in Release for every target with no warning from the changed code,
  and `repro.sh` and `census.py` on the built tool give R1's rows unchanged (`results/census-4.31.0.md`).

**2026-10-19, the found bugs executed as 4.31.1 (kronikol-71),** in worktree `C:/Code/Kronikol-161q7` on branch
`release/161-q7`, from 4.30.0 and rebased onto 4.31.0.

- **The ANSI codes (3.10):** `TestRunRecord`'s `error` and `stackTrace` drop a terminal's control sequences (CSI and
  OSC, `TerminalCodes`) as a record is built or read, so the scenario, its steps, `Failures.md`'s cluster key and every
  query get the text without them. A bracket without the escape before it stays.
- **The lightbox (3.10):** a scenario's own image (`GenerateHtmlReportCore`) and a parameterized row's
  (`RenderParameterizedGroup`) call `openLightbox` as a step's does, in place of `target="_blank"`.
  `MissingAttachmentTests`' pin of the encoded href follows. Kronikol4J's `DotNetHtmlReportRenderer.java` L1141 still
  opens a tab (ledger).
- **Red proof:** the ANSI facts failed on 4.30.0's code (five of seven rows; the two controls passed), the unit
  lightbox fact failed at the scenario's own image, and the browser fact timed out waiting for the lightbox with the
  fix stashed; each passed with it (474 ingestion facts, 371 report and attachment facts, and the 15 attachment
  rendering facts in the browser).
- **Checks:** the core suite passes 8,108 with 2 skipped after the version bump and the rebase onto 4.31.0, the 15
  attachment rendering facts pass in the browser, and `release.slnf` builds in Release for every target with no
  warning from the changed code.

## Appendix A. Every list cap (sweep at `6f6043e2`)

Every row was re-read in the code at the verification pass. Twelve are silent in at least one output: rows 1, 2, 3, 4, 8,
9, 12, 14, 22, 25, 26 and 33. Eleven verbs declare `Json: false` (`steps`, `flow`, `annotations`, `values`, `http`,
`body`, `note`, `diagram`, `grep`, `trace` and `compare`), and `QueryCommand.cs` L240-245 refuses `--json` on them, so
their rows have no JSON output to be silent in.

| # | Site | Output | List, cap | What it says | Plan |
|---|---|---|---|---|---|
| 1 | Narr:126-129 | `failures` | a failing step's attachments, 6 | Text: "… N more attachment(s): steps sN/path". JSON (L215): nothing | R1 |
| 2 | Narr:144-146 | `failures` | the scenario's own attachments, 4 | Nothing, in the text or the JSON (L217) | R1 |
| 3 | Narr:134-135 | `failures` | calls in a failing step, 6 | Text: the count, no view. JSON (L200-203): nothing | R1 |
| 4 | Narr:343 | `steps` | step parameters, 8 | Nothing | R2 |
| 5 | `QueryCommand.Overview.cs` L106-110 | `summary` | failed scenarios, 10 | "… N more · scenarios --result Failed"; JSON `failedTotal` | Fine |
| 6 | `QueryCommand.Overview.cs` L132-138 | `summary` | slowest scenarios, 3 | A ranking labelled "Slowest:" | Declared exception |
| 7 | `QueryCommand.Overview.cs` L177-179; `QueryCommand.cs` L418-424 | `summary`, banners | diagnostics, the first message per kind | Counted with ×N | Declared exception |
| 8 | `QueryCommand.Overview.cs` L443-444 | `services` | status kinds per service, 4 | Nothing in the text; the JSON has the whole map | R2 |
| 9 | `QueryCommand.Sizes.cs` L93-121 | `sizes` | the largest scenarios, 10 | "The 10 largest of N"; JSON with no total | R2, text fine (the JSON has no total) |
| 10 | `QueryCommand.Payloads.cs` L277-281, L433-437 | `http b:…`, `body` | addresses holding a body, 12 | Counted, with no view | R2 |
| 11 | `QueryCommand.Search.cs` L125-127; `QueryCommand.NumberGrep.cs` L103-105 | `grep` | the same list, 1 | "first ×N" | Fine |
| 12 | `QueryCommand.Search.cs` L131 | `grep --values` | matching paths per body, 4 | Nothing | R2 |
| 13 | `QueryCommand.Values.cs` L125-127, L175-176 | `values` | addresses per value | "×N e.g. address" | Fine |
| 14 | `QueryCommand.Search.cs` L307-325 | `compare` | differences, 20 | The heading counts the sequences, not the differences | R2 |
| 15 | `QueryCommand.Search.cs` L801-874 | `diff` (runs) | Broken, Fixed, New and Slower 15; Gone and Tracking 10 | "… N more (--json lists every row)"; the JSON is whole | Fine (the comment at L810 still says only the heading counts) |
| 16 | `QueryCommand.Diff.cs` L476-504 | `diff` (bodies) | differing lines, 20 | A marker row with no count, which `Page` counts as a 21st difference (3.10) | R2 |
| 17 | `QueryCommand.Diff.cs` L541-546 | `diff` (bodies) | keys of an object, 6 | `{a, b, …}` | Fine |
| 18 | `PathEngine.cs` L257-271 | `--keys` | array elements, depth | `array (N)`, `object (N keys)` | Fine |
| 19 | `QueryCommand.Diff.cs` L266-272 | `diff --body` refusal | candidates, 6 | "(6 of N)" plus `interactions` | Fine |
| 20 | `QueryCommand.History.cs` L337-340 | `history` (run) | absent scenarios, 20 | "… and N more"; the JSON is whole | Fine |
| 21 | `QueryCommand.History.cs` L996-1041 | `history` hints | addresses, 5 | "… and N more" | Fine |
| 22 | `QueryCommand.History.cs` L411 | `history sN` | runs, the last 15 | Nothing: the heading (L409) is a label, and the count at L408 is the stream's runs, not this scenario's points; the JSON is whole (L801) | R2 |
| 23 | `QueryCommand.History.cs` L896-903 | `--run` not found | runs, 10 | Counted | Fine |
| 24 | `QueryCommand.cs` L471-475 | report lookup | reports, 20 | "… and N more" | Fine |
| 25 | FDG:276 (the constant at L59) | `Failures.md` "Steps before it:" (L750-756), jsonl `stepsBefore` (L963) | steps before it, 3 | Nothing | R2 |
| 26 | FDG:352, L365-367 (the constant at L67) | `Failures.md` calls table (L772-775), jsonl `calls` (L965-975) | calls, 8 | Nothing at the cut. A comment (FDG:61-66) and the command block (FDG:610) name `flow` | R2 |
| 27 | FDG:655-659 | `Failures.md` clusters | members, 20 | "N more not listed …" | Fine |
| 28 | FDG:667-679 | `Failures.md` | worked failures, 25 | "## N further failures" | Fine |
| 29 | FDG:681-691 (the constant at L79) | `Failures.md` table | further failures, 100 | An elision line | Fine; R1 adds a column |
| 30 | `CiSummaryGenerator.cs` L80-127 | CI summary | failed scenarios, `maxDiagrams` (10 unless `MaxCiSummaryDiagrams` says otherwise) | "*N more failed scenario(s) not shown*", an exact count | Fine |
| 31 | `CiSummaryGenerator.cs` L141-168 | CI summary, green run | diagrams, `maxDiagrams` | A wrong count (3.10) | Q7 |
| 32 | `src/Kronikol.Tool/HistoryCommand.Maintenance.cs` L134-141 | `history gate` | scenarios per section, 50 | "… and N more"; the gate has no JSON | Fine |
| 33 | `QueryCommand.NumberGrep.cs` L116-122 | `grep --number` | matching paths per body, 4 (a counter, not a `Take`) | Nothing | R2 |
| 34 | FDG:96, L665 | `Failures.md` | worked examples per cluster: one, plus one per ten members, at most five | Announced through rows 27 to 29 | Fine |
