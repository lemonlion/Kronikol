# List caps plan (#161)

**Status:** written 2026-10-19 at 4.27.0 (`6f6043e2`) by kronikol-71, at the owner's ask ("critically analyse issue
161 and create a detailed, well researched plan to fix its underlying issue(s)"). **Draft, not green-lit.** Nothing in
a package changes.

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
`results/json-record-sizes-4.26.9.txt` holds the sizes in section 3.4.

The harness ran on Kronikol.Tool 4.26.9, installed with `--tool-path` under the session's scratchpad. 4.27.0 was
tagged but not yet listed on nuget.org. `QueryCommand.Narrative.cs`, `QueryWriter.cs`, `ReportIndex.cs` and
`FailuresDigestGenerator.cs` are unchanged between `v4.26.9` and `6f6043e2`. Windows 11, .NET SDK 10.

Line numbers are to `6f6043e2` (4.27.0). Wiki lines are on `origin/master` `450844e`. Short names used below:

| Short name | File |
|---|---|
| Narr | `src/Kronikol/Query/QueryCommand.Narrative.cs` |
| QW | `src/Kronikol/Query/QueryWriter.cs` |
| QO | `src/Kronikol/Query/QueryOptions.cs` |
| FDG | `src/Kronikol/Reports/FailuresDigestGenerator.cs` |

---

## 1. Summary

The issue is right, and it understates the defect. The repo's own Playwright capture takes a screenshot after each step.
On it, `query failures` prints 4 of a scenario's 10 attachments: the start screenshot and three step screenshots. The
failure screenshot, the end screenshot, the video, `error-context` and the trace are all cut, in the text and in
`--json`, and nothing says so. In a playwright-bdd report ingested with its hooks kept, the same artefacts sit on the
after-hook step, and `failures` shows 1 of 10, again with no count.

There are five underlying causes:

1. **No view prints a scenario's own attachments in full.** `steps` prints only those made in a step. So the cap had
   no view to name, and it could not follow the tool's own rule that "an ellipsis always has an address" (QW:538).
2. **The cap keeps the wrong end.** Producers attach failure-time artefacts last: Playwright does at teardown, and so
   does a .NET after-hook that calls `Track.Attachment`. So "the first four" is the end least likely to matter.
3. **Inner lists have no shared primitive.** `QueryWriter.Page` renders a listing and its footer "so no command can page
   without saying so" (QW:358). A list inside a record is a hand-written `.Take(n)`, and silence is the default.
   - A sweep found 32 list caps across the query tool, `history gate`, the failures digest and the CI summary. Nine
     are silent in at least one output, and nothing checks them.
   - The defect came back after the issue was filed: 4.26.4 capped a failing step's attachments at six, with a
     "… N more" line in the text and a silent `Take(6)` in `--json`.
4. **`--json` has no convention for a capped inner list.** There is one precedent: `summary`'s `failed` and
   `failedTotal`.
5. **`Failures.md`, the documented fallback, misses most failures.** It works through one failure per cluster. Every
   other failure, even in a two-failure run, gets a table row with no attachments. The skill tells agents that
   `Failures.md` "lists every one".

The plan has two releases:

- **R1 (#161), a minor** by the owner's precedent in the #175 plan (Q14: a new JSON member is a minor).
  - `steps sN` prints the scenario's own attachments.
  - `failures` keeps both ends of a capped attachment list (the first one and the last five). In place of the cut it
    prints a line naming what was cut and the view that lists it.
  - It counts attachments on steps that did not fail, and names the view for cut calls.
  - `--json` carries the same entries and a total for every capped list.
  - `Failures.md` counts the attachments of every failure it does not work through.
- **R2, a patch,** can be split off from R1. It adds a list-cut primitive to `QueryWriter` and announces the remaining
  silent caps through it. A census fact and a source lint make a new silent cap fail the tests.

The issue's second suggestion, giving `--json` every attachment, is not taken as stated. `--json` is under the same
6,000-byte budget and drops a record that does not fit whole, so a failure with about 20 attachments would vanish from
the answer (section 3.4). There are eight questions for the owner (section 9), and six things found on the way that this
plan leaves alone (section 3.10).

## 2. What the issue says, checked

| # | The issue says | Checked at `6f6043e2` |
|---|---|---|
| 1 | `query failures <report> s1` lists the first four attachments, in the text and in `--json`, and nothing says there are more | **True, still at 4.27.0.** Narr:144-146 (text) and Narr:217-218 (JSON). The census shows 4 of 10 in the recipe and messages lanes. |
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

Appendix A lists every list cap the sweep found: 32, of which 9 are silent in at least one output. A listing's page goes
through `QueryWriter.Page`, which "renders a paged listing and its footer in one place, so no command can page without
saying so" (QW:358). A list inside a record has no such primitive. Each site writes `.Take(n)`, followed by a line only
if its author remembered.

No fact or lint checks that a cap is announced. The only cap fact,
`DigestSuppressionCapTests.Every_cap_says_how_many_it_left_out`, covers the digest's suppression list.

4.26.4 shows the cost. It was written by an audit that knew about #161. It added a correct "… N more" line to the text,
and a silent `Take(6)` to the JSON of the same list.

### 3.4 F4: "give --json all of them", measured against the budget

`--json` has the same budget as the text: 6,000 bytes by default (QO:425). Each item is charged whole, and a record that
does not fit is dropped whole, with `truncated` and `next` saying so (QW:245-270; `Querying-Reports.md` L315-320).

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
lists the rest. That view is `flow sN --step P` (Narr:489-515, the step filter at L505). The JSON `calls` is `Take(6)`
with no count (Narr:200-203).

### 3.8 F8: the code keeps a record of the divergence

The doc comment on `FailureRecord` (Narr:158-172) lists how its JSON differs from `Failures.jsonl`, including "The
scenario's own attachments are capped at four here and uncapped there". That list is worth keeping, and R1 updates it.

### 3.9 F9: a report's own query.cs keeps its version's cap

`query.cs`, written beside every report, loads the `Kronikol.dll` of the version that wrote the report and calls
`QueryCommand.Run` by reflection (`QueryScriptGenerator.cs` L115-155, L216-226). So a report written before R1 keeps the
cap when read through its own `query.cs`. `kronikol` (the tool) at R1 or later reads the same report with the fix. The
changelog says so.

### 3.10 Found on the way, not in this plan

- **ANSI colour codes.** Playwright's error messages carry SGR escape sequences, and Kronikol prints them raw. They
  appear in `query failures` and in `Failures.md`'s cluster heading, error block and table, which puts them in the
  cluster key too (census output). No issue is filed (Q7).
- **The HTML lightbox.** A read-only sweep reports that scenario-level images open in a new tab with no lightbox
  (`ReportGenerator.cs` L2273), while step-level images open in a lightbox (L3876-3877). `Integration-Playwright.md`
  L116 promises a lightbox. This plan did not re-check it. A fix would change report output, which needs a Kronikol4J
  ledger entry (Q7).
- **The CI summary's count.** The green-run diagrams section prints "*{n} more scenario(s) not shown — see full
  report*" with n = every scenario minus those shown (`CiSummaryGenerator.cs` L31, L165). So scenarios that have no
  diagram are counted as hidden. Kronikol4J ports this generator byte for byte (Q7).
- **Two collision suffixes.** A run's copier names a collision `name_2.ext` (`ReportGenerator.cs` L6183-6199), and
  `kronikol merge` names it `stem-1.ext` (`MergeableReportRenderer.cs` L306). Cosmetic.
- **`scripts/query.py` prints no attachments.** This is documented (`SKILL.md` L164-166) (Q7).
- **#148** (a missing attachment keeps its path, with no diagnostic). Every line this plan adds prints the path as
  `AttachmentEntry.Resolve` returns it, so #148's "(not found when the report was drawn)" can later be added to the same
  line. No conflict.

## 4. Design

### 4.1 `steps sN` prints the scenario's own attachments

Unscoped `steps sN` prints each of the scenario's own attachments, with no cap, in the form `failures` uses
(`attachment: name → path`). They go after the header lines (`stableId`, `at`, `open:`, `example:`) and before the tree:
at the top, not the end, so that when the byte budget cuts a long tree it cuts steps, not artefacts.

A scoped view (`steps sN/k`) prints none, just as it prints no other scenario line.

This makes `steps sN` the complete view of a scenario's attachments, as it already is for its steps' attachments. It is
therefore the view that every attachment cut names.

### 4.2 A capped attachment list keeps both ends and names the cut

Both attachment lists in `failures` (the scenario's own, and each failing step's) use one cap, six. That is today's
`MaxFailingStepAttachments`, renamed `MaxAttachmentsShown`. A capped list keeps the first entry and the last five, and
prints one line in place of the cut:

```
  attachment: screenshot-start.png → …/attachments/6-screenshot-start.png
  … 4 more attachment(s) (after-step.png ×3, step-shot.png): steps s0
  attachment: screenshot → …/attachments/test-failed-1.png
  attachment: screenshot-end.png → …/attachments/11-screenshot-end.png
  attachment: video → …/attachments/video.webm
  attachment: error-context → …/attachments/error-context.md
  attachment: trace → …/attachments/trace.zip
```

- **The names in the parentheses** are the cut entries' names, in order. A repeated name appears once with ×N, and the
  whole is cut by `QueryWriter.OneLine` at 100 characters.
- **The address** is the scenario's (`steps s0`) for its own list, and the step's (`steps s0/3`) for a step's list.
- **With six entries or fewer**, everything is printed and no line is added.

The split is one at the start and five at the end. The first attachment is the test's own starting state, the cheapest
context for the rest. Playwright's failure set is four or five entries: the screenshot, video, error context and trace,
plus a fixture's end screenshot. The split is Q3.

### 4.3 What `failures` does not list, it counts

After the scenario's own list, if the scenario holds attachments on steps other than its failing steps, `failures`
prints one more line, with names as in 4.2:

```
  … 9 more attachment(s) on other steps (after-step.png ×2, step-shot.png, screenshot, screenshot-start.png, error-context, trace, …): steps s0
```

That is the hooks lane's `s0`. Its failing step `s0/3` holds one attachment, and the other nine are on two earlier steps,
the "After Hooks" step (`screenshot`, `screenshot-start.png`, `error-context`, `trace`) and two fixture steps
(`screenshot-end.png`, `video`). This closes F5 without listing them. That keeps the #175 plan's Q15, under which hook
artefacts are listed by `Failures.md` and `steps`, not by `failures`; what changes is that `failures` now says they
exist.

### 4.4 `--json`: the same entries, and a total for every capped list

The JSON keeps the entries the text prints (both ends), so the two cannot disagree. It adds totals as sibling members.
They are always present, by the envelope's rule for `total` (`Querying-Reports.md` L311-314):

| Member | Counts |
|---|---|
| `attachmentsTotal` | the scenario's own attachments |
| `stepAttachmentsTotal` | the attachments on all of its steps, so that `attachmentsTotal + stepAttachmentsTotal` is the number `Failures.jsonl` lists |
| `failingSteps[].attachmentsTotal` | the attachments on that failing step |
| `failingSteps[].callsTotal` | the calls made in that failing step |

`attachments` stays an array of `{name, path}`. So `formatVersion` stays 1, and no consumer of today's shape breaks
(`QueryJsonTests.cs` L385-396). The precedent is `summary`'s `failed` and `failedTotal` (`QueryCommand.Overview.cs`
L114-123).

### 4.5 Calls under a failing step

The text keeps "{n} calls in this step:". When it cuts, it ends the list with "… {n-6} more: flow sN --step P", which
names the view that lists them (Narr:489-515). The JSON gains `callsTotal` (4.4).

### 4.6 `Failures.md` counts the attachments of every failure it does not work through

- **The further-failures table gains an `Attachments` column.** It holds the failure's count (its own attachments plus
  every step's, as `Failures.jsonl` lists them), or nothing when there are none. The table has at most 100 rows
  (FDG:79), so the column costs a few bytes per row.
- **The sentence above the table gains a pointer:** "`kronikol query steps {here} sN` lists a row's attachments".
- **The cluster table keeps its columns.** Every cluster member is also in the further-failures table.
- **The skill's sentence (`SKILL.md` L164-166) is made true:** "`Failures.md` lists every attachment of each failure it
  works through, and counts them for the rest; `steps sN` lists a scenario's."

### 4.7 What does not change

The data files, the HTML, `Failures.jsonl`, ingest, merge, how `query.cs` loads the engine, and `scripts/query.py`
(Q7).

### 4.8 Options considered

| Option | For | Against | Taken |
|---|---|---|---|
| Say how many were cut (issue's suggestion a) | Cheap | There is no view to name today | Yes, with 4.1 |
| `--json` lists every attachment (issue's suggestion b) | Complete | The budget drops the whole record at about 20 attachments (F4) | No. "Every attachment under `--out`" is offered in Q2 |
| Every name on one line (issue's suggestion c) | Cheap | A name is not a file | In part: the cut line names the cut entries |
| Raise the cap to 10 | Simple | Still silent past 10, and a failure's lines nearly double | No |
| Keep the last six | Keeps Playwright's set | Loses the start state, and a step's first attachment can be the thing under test | No |
| Rank by media type (trace first) | Suits Playwright | A heuristic per producer, and .NET attachments carry no media type (`Track.cs` L164-168) | No |
| A new `attachments sN` verb, text and `--json`, listing every attachment with its step | The cleanest full view, JSON included | A new verb is a minor and more surface, and `steps` is already the scenario's detail view | Q1 |

### 4.9 R2: one primitive and a guard for every list cut

**The primitive.** `QueryWriter.ListCut` (the name is open) takes a list, a cap, a head size, a noun and the address of
the full view. It returns the entries to print and writes the cut line in their place. JSON callers take the same
selection and add a `<list>Total` member. R1's sites move onto it.

**Every silent cap in Appendix A gets the same treatment**, naming a view where one exists:

- **`steps` parameters** (Narr:343, cap 8): the unscoped tree cuts with "… N more parameter(s): steps sN/k", and a
  scoped `steps sN/k` prints them all.
- **`services` status kinds** (`QueryCommand.Overview.cs` L443-444, cap 4): "+N more", pointing at `--json`, which
  carries the whole map (L385).
- **`grep --values` paths** (`QueryCommand.Search.cs` L131, cap 4): "… N more path(s)".
- **`compare`** (`QueryCommand.Search.cs` L307-325, cap 20): "… N more difference(s)". Today the heading counts the
  sequences, not the differences.
- **`history sN`** (`QueryCommand.History.cs` L411, the last 15): "{n} earlier point(s) not shown: history sN --json".
  The JSON carries every run (L801).
- **`Failures.md` and `Failures.jsonl`:** "Steps before it" (FDG:276, cap 3) and the calls table (FDG:352, cap 8) each
  get a count line pointing at `kronikol query steps` or `flow` for that failure. FDG:62-63 already says `flow` is the
  answer.

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
  registry, so that adding a cap is a decision someone writes down.

## 5. Releases

| Release | Bump | Contents | Closes |
|---|---|---|---|
| R1 | Minor: new JSON members (4.4) and a new `Failures.md` column (4.6), by the precedent of #175's Q14 (Q6) | 4.1 to 4.6, the docs in section 7, the fact changes in 6.3 | #161 |
| R2 | Patch, if it adds no JSON member (a `<list>Total` for another verb would make it a minor, Q5) | 4.9 | nothing filed; Appendix A's silent rows |

Version numbers are claimed when each release is pushed (section 8). Neither release changes report output (HTML,
PlantUML or embedded scripts). R1 changes `Failures.md`, a file Kronikol4J does not write.

## 6. Tests

Following TDD: each fact is written first and run red against the release before R1 (4.27.0 today, or whatever is newest
when R1 starts). It is then made green.

### 6.1 Fixtures

- **Real:** the Playwright 1.62.1 capture (`playwright-bdd-9.2-attachments`), ingested in the three lanes through the
  ingest CLI path that `CucumberAttachmentIngestTests` already uses (L416-445).
- **Built:** a report with:
  - one failing scenario holding 60 attachments of its own;
  - a failing step with 60 attachments and 47 calls;
  - a passing step with 9 attachments;
  - a second failure in the same cluster.

  It is built through the report writer, as `DigestStepAttachmentTests` does (L230-254), so that every fact runs through
  `QueryCommand.Run`, the public entry point, and compiles unchanged on the previous tag for the red proof.

### 6.2 R1 facts

| ID | Fact | Asserts | Red on 4.27.0 because |
|---|---|---|---|
| T1 | `Failures_keeps_both_ends_of_a_scenarios_attachments_and_names_what_it_cut` | Recipe lane `s0`, in order: `screenshot-start.png`, then `… 4 more attachment(s) (after-step.png ×3, step-shot.png): steps s0`, then `screenshot`, `screenshot-end.png`, `video`, `error-context`, `trace` | The first four are printed, and there is no cut line |
| T2 | `Steps_lists_every_attachment_of_the_scenario_itself` | Recipe `steps s0` prints all 10 in order, before the tree | It prints none |
| T3 | `A_scoped_steps_view_lists_no_scenario_attachment` | `steps s0/k` prints none of the scenario's own | Guard, green on 4.27.0: proved by mutation M5 |
| T4 | `Every_attachment_cut_names_a_view_that_prints_every_entry` | Built report: for each cut line, run the command it names and find every cut entry's path in its output | There is no cut line for the scenario list |
| T5 | `Failures_counts_attachments_on_steps_that_did_not_fail` | Hooks lane `s0`: `… 9 more attachment(s) on other steps (…): steps s0`; the built report likewise | Nothing is said |
| T6 | `Failures_json_carries_the_texts_entries_and_every_lists_total` | In all three lanes and the built report: the JSON names equal the text's, plus `attachmentsTotal`, `stepAttachmentsTotal`, `failingSteps[].attachmentsTotal` and `callsTotal`; own plus step totals equal `Failures.jsonl`'s count | The members are absent, and the first four are kept |
| T7 | `A_failure_with_sixty_attachments_still_fits_the_default_budget` | Built report, `failures --json`: the record is present and `truncated` is null | Guard, green on 4.27.0 (cap 4): proves R1 kept a cap (M8) |
| T8 | `Failures_names_the_view_for_a_steps_cut_calls` | Built report: `… 41 more: flow s0 --step N`, and that command prints 47 calls | There is no pointer |
| T9 | `Failures_md_counts_the_attachments_of_a_failure_it_does_not_work_through` | Recipe lane: the `s4` row says 10, and the sentence names `kronikol query steps . sN` | There is no column |
| T10 | `ListCut` unit facts | Selection for n = 0, 1, 6, 7 and 60 at cap 6, head 1: the positions and the cut count | Unit spec; proved by mutations M1, M3 and M11 |

`QueryCultureTests` already runs `failures` under several cultures (L75). Its fact gains the cut line, so the count is
held to the invariant culture.

### 6.3 Existing facts that change

- `CucumberAttachmentIngestTests.cs` L444-445 asserts the first four names. It becomes both ends plus the cut line, as
  T1.
- `DigestStepAttachmentTests.cs`:
  - L244-248: six lines and "… 54 more" stay, but which six are printed changes, and the cut line moves to after the
    first entry. The JSON's six paths change the same way.
  - L172: the scenario's JSON list stays `["trace"]`.
- `QueryJsonTests.cs` L385-396: add the totals if the fact lists keys.

### 6.4 Proofs and mutations

**Red proofs.** Every fact runs through `QueryCommand.Run` or the ingest CLI, so the new test files compile on the
previous tag unchanged. Run them in a worktree at that tag, with nothing stubbed, and read each failure's message (a fact
can fail for the wrong reason). T3 and T7 are guards and are proved by mutation.

**R1 mutations**, each of which must be killed. Run them in a snapshot worktree (`git stash create`), and keep
`mutations.py` in the harness.

| ID | Mutation | Killed by |
|---|---|---|
| M1 | Keep the first six (head 6) | T1, T6 |
| M2 | Drop the cut line | T1, T4 |
| M3 | Count off by one | T1, T6 |
| M4 | `attachmentsTotal` = the printed count | T6 |
| M5 | Print the scenario's attachments in a scoped view | T3 |
| M6 | A step's cut line names the scenario | T4 |
| M7 | The "other steps" count includes the failing steps | T5 |
| M8 | The JSON lists every attachment | T7 |
| M9 | The `Failures.md` column counts the scenario's own attachments only | T9 |
| M10 | `steps` prints them after the tree | T2 |
| M11 | Repeated names are not grouped | T1 |

### 6.5 R2 facts

- `CapCensusTests`: one theory row per declared cap, read from the registry.
- Its vacuity fact.
- `ListCapSourceTests`, with a control: a planted silent `Take(5)` in a source string is found.
- Mutations: remove each announcement in turn, and its census row goes red.

### 6.6 Checks for each release

- The core suite, including `SkillDriftTests` and `InitAgentsCommandTests` (the two skill copies and the embedded one),
  `QueryCultureTests` and `FallbackScriptTests`.
- `release.slnf` built in Release for every target. `Query/` compiles for net10.0 only, so no code under `Reports/` may
  call the new helper.
- No Playwright run is needed: no HTML changes.
- `repro.sh` and `census.py` run on the built tool, with the table kept as `results/census-<version>.md`. It should show
  the target row in every lane.

## 7. Documentation and ledger

**Wiki:**

- `Querying-Reports.md`:
  - L464-468 (`failures`): both ends, the cut line, the count for other steps.
  - L470-482 (the example): an attachment and a cut line.
  - L357 (the `--json` table): the totals.
  - L529-533 (`steps`): the scenario's own attachments first.
- `Generated-Reports.md` L437-438, L497 and L503-505: the query against the digest, and the further-failures column.
- `Integration-Playwright.md` L116-129: where Playwright's artefacts appear in `query failures`.

**Skill** (both copies are byte-identical, and `InitAgentsCommandTests` holds the embedded copy to them):
`references/commands.md` L150-159 and L175; `SKILL.md` L53-54 and L164-166.

**Doc comments:** Narr:12 (the constant), Narr:124-125, Narr:158-172 (the divergence list) and Narr:214; the
FDG:677-679 sentence.

**CHANGELOG:** R1's section states the minor (the new JSON members and the `Failures.md` column), the `query.cs` note
(F9), and that #161 is closed.

**Kronikol4J:** it has no query tool and no `Failures.md` (its `README.md` L8-11). R1's column gets one entry in its
ledger (`docs/REMAINING_PARITY.md`), "not mirrored, a ledger entry only", as 4.3.0's digest change did (L2404). Check the
ledger's tip on origin before writing: the local checkout's newest entry is 4.16.3, and the #175 entries are on origin.

**ROADMAP.md:** in Appendix C (L1025), the #161 cell points at this plan once it is committed.

**#161:** close it after R1 is published, with a comment naming the release, and confirm with `gh issue view 161`.

## 8. Coordination

- **This plan touched nothing shared.** The research was read-only. The harness ran on a published tool installed under
  the session's scratchpad. The plan is written in worktree `C:/Code/Kronikol-161` (branch `plan/161-list-caps`), and
  origin was read through private refs. Live when it was written: kronikol-0b, kronikol-39 and kronikol-22 (busy).
- **Files R1 touches:** `src/Kronikol/Query/QueryCommand.Narrative.cs`, `src/Kronikol/Query/QueryWriter.cs` (the helper),
  `src/Kronikol/Reports/FailuresDigestGenerator.cs` (the table), the facts above, the skill and three wiki pages.
- **Files R2 touches:** most of the `QueryCommand.*.cs` files, and the digest.
- **`REPORT_FORMAT_PLAN.md`'s execution** (worktree `Kronikol-krkl`, branch `release/r2-tooltip` at the time of
  writing) works in the query tool's reader and in `sizes`. R2 overlaps its files more than R1 does.
- **Before either release:** announce the files and the version number by SendMessage, and claim a version with
  "taking X now" only when about to push.
- **`PLANS_STATUS.md`:** this plan's row only. No `ROADMAP.md` row is taken (Q8).

## 9. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | The full view: extend `steps sN` (4.1), or add an `attachments sN` verb with `--json`? | `steps`. It adds no verb, and it is the view `failures` already names for a step's attachments. A new verb is a minor and more surface. |
| Q2 | `--json`: the text's entries plus totals; every attachment when the budget is off (`--out`, `--max-bytes 0`); or every attachment always? | Entries plus totals. "Always" drops a failure with about 20 attachments at the default budget (F4), and "when unbudgeted" gives one verb two shapes. |
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

## Appendix A. Every list cap (sweep at `6f6043e2`)

"Checked" means re-read in the code for this plan. The other rows come from the sweep and are re-checked before R2
changes them.

| # | Site | Output | List, cap | What it says | Plan |
|---|---|---|---|---|---|
| 1 | Narr:126-129 | `failures` | a failing step's attachments, 6 | Text: "… N more attachment(s): steps sN/path". JSON (L215): nothing | R1 (checked) |
| 2 | Narr:144-146 | `failures` | the scenario's own attachments, 4 | Nothing, in the text or the JSON (L217) | R1 (checked) |
| 3 | Narr:134-135 | `failures` | calls in a failing step, 6 | Text: the count, no view. JSON (L200-203): nothing | R1 (checked) |
| 4 | Narr:343 | `steps` | step parameters, 8 | Nothing | R2 (checked) |
| 5 | `QueryCommand.Overview.cs` L106-110 | `summary` | failed scenarios, 10 | "… N more · scenarios --result Failed"; JSON `failedTotal` | Fine |
| 6 | `QueryCommand.Overview.cs` L132-138 | `summary` | slowest scenarios, 3 | A ranking labelled "Slowest:" | Declared exception (checked) |
| 7 | `QueryCommand.Overview.cs` L177-179; `QueryCommand.cs` L418-424 | `summary`, banners | diagnostics, the first message per kind | Counted with ×N | Declared exception |
| 8 | `QueryCommand.Overview.cs` L443-444 | `services` | status kinds per service, 4 | Nothing in the text; the JSON has the whole map | R2 (checked) |
| 9 | `QueryCommand.Sizes.cs` L93-121 | `sizes` | the largest scenarios, 10 | "The 10 largest of N"; JSON with no total | R2, text fine |
| 10 | `QueryCommand.Payload.cs` L277-281, L433-437 | `http b:…`, `body` | addresses holding a body, 12 | Counted, with no view | R2 |
| 11 | `QueryCommand.Search.cs` L125-127; `QueryCommand.NumberGrep.cs` L103-105 | `grep` | the same list, 1 | "first ×N" | Fine |
| 12 | `QueryCommand.Search.cs` L131 | `grep --values` | matching paths per body, 4 | Nothing | R2 (checked) |
| 13 | `QueryCommand.Values.cs` L125-127, L175-176 | `values` | addresses per value | "×N e.g. address" | Fine |
| 14 | `QueryCommand.Search.cs` L307-325 | `compare` | differences, 20 | The heading counts the sequences, not the differences | R2 (checked) |
| 15 | `QueryCommand.Search.cs` L801-874 | `diff` (runs) | 15 and 10 per section | "… N more (--json lists every row)" | Fine (the JSON is budgeted) |
| 16 | `QueryCommand.Diff.cs` L476-504 | `diff` (bodies) | differing lines, 20 | Marked, with no count | R2 |
| 17 | `QueryCommand.Diff.cs` L541-546 | `diff` (bodies) | keys of an object, 6 | `{a, b, …}` | Fine |
| 18 | `PathEngine.cs` L257-271 | `--keys` | array elements, depth | `array (N)`, `object (N keys)` | Fine |
| 19 | `QueryCommand.Diff.cs` L266-272 | `diff --body` refusal | candidates, 6 | "(6 of N)" plus `interactions` | Fine |
| 20 | `QueryCommand.History.cs` L337-340 | `history` (run) | absent scenarios, 20 | "… and N more"; the JSON is whole | Fine |
| 21 | `QueryCommand.History.cs` L996-1041 | `history` hints | addresses, 5 | "… and N more" | Fine |
| 22 | `QueryCommand.History.cs` L411 | `history sN` | runs, the last 15 | Nothing (the line before counts the runs in the window); the JSON is whole | R2 (checked) |
| 23 | `QueryCommand.History.cs` L896-903 | `--run` not found | runs, 10 | Counted | Fine |
| 24 | `QueryCommand.cs` L471-475 | report lookup | reports, 20 | "… and N more" | Fine |
| 25 | FDG:276 | `Failures.md`, `stepsBefore` | steps before it, 3 | Nothing | R2 (checked) |
| 26 | FDG:352 | `Failures.md` calls, `calls` | calls, 8 | Nothing (FDG:62-63 names `flow` in a comment only) | R2 (checked) |
| 27 | FDG:655-659 | `Failures.md` clusters | members, 20 | "N more not listed …" | Fine |
| 28 | FDG:667-679 | `Failures.md` | worked failures, 25 | "## N further failures" | Fine |
| 29 | FDG:682-690 | `Failures.md` table | further failures, 100 | An elision line | Fine; R1 adds a column |
| 30 | `CiSummaryGenerator.cs` L80-127 | CI summary | failed scenarios, 10 | "*N more failed scenario(s) not shown*" | Fine |
| 31 | `CiSummaryGenerator.cs` L151-169 | CI summary, green run | diagrams, 10 | A wrong count (3.10) | Q7 (checked) |
| 32 | `HistoryCommand.Maintenance.cs` L125-132 | `history gate` | scenarios per section, 50 | "… and N more" | Fine |
