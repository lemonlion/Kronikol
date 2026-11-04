# Harness: BACKGROUND_AS_WRITTEN_PLAN.md (#222)

The probes behind every RUN line of [`../BACKGROUND_AS_WRITTEN_PLAN.md`](../BACKGROUND_AS_WRITTEN_PLAN.md), with the output each one printed on
2026-11-02 and 2026-11-03 (.NET SDK 10.0.300, Windows 11). The suites take the **published 5.27.13** packages from nuget.org, so they measure what a consumer
has, and they build nothing in the repository: copy a suite to a scratch folder first, as `run-matrix.sh` does. `kronikol` below is `Kronikol.Tool`
5.27.13, installed with `dotnet tool install --tool-path <scratch>`. A browser probe uses Playwright's bundled Chromium, launched through `tools/playwright/installed-browser.js`, never an installed browser.

## The suites

| Folder | What it is |
|---|---|
| `probes/issue-suite/` | The suite in #222's details block, verbatim. `probes/extract-issue-suite.py` writes it from `probes/issue-222-body.md`. |
| `probes/shapes-suite/` | 20 feature shapes, 41 scenarios, on the issue's csproj. Each step definition calls a woven `Ran_for(who)` sub-step naming the scenario that really ran it, so a step in the report says whose it is. Environment: `KRK_MODE` picks a failure (`pass`, `fail-and`, `fail-when`, `fail-bg`, `fail-bg2`); `KRK_ATTACH=1` attaches a file in shape 18's shared step; `KRK_SEPARATE=1` sets `SeparateBackgroundSteps`; `KRK_MERGEABLE=1` sets `GenerateMergeableData`; `KRK_PICKLE_PROBE=<file>` writes what Reqnroll knows at run time. MSBuild: `-p:ReqnrollVersion=3.0.1` for the floor. |
| `probes/ingest-lane/` | A tests NDJSON with keyworded steps and no `background` field, with and without an attachment on a shared step. `run-attachment.sh` writes the attachment paths at run time. |
| `probes/edge-shapes/` | Shapes E24 to E49, added to a copy of the shape suite by its own `run.sh`, from one `Features/E24_E34.feature.txt` it cuts at each `#file` line: undefined, pending and run-time-ignored steps in a Background, an outline in a rule, awkward step text, a scenario repeating its Background, suites' hooks that throw at each order and level, a hook that runs a step after the scenario, a `[Step]` helper between a step and the steps it runs, an inner argument that does not convert, a row failing in its Background, a suite's `[AfterScenario]` and block hook that throw, and step texts the bar escapes or wraps. It ends with the whole suite under `stopAtFirstError`. |
| `probes/nested-shapes/` | Shapes 21 to 23, added to a copy of the shape suite by its own `run.sh`, so the matrix's counts stay the matrix's: a step that runs steps with a table and a doc string, an inner step that fails with `KRK_MODE=fail-nested`, and hooks that run a Gherkin step and a woven step beside a Background. |
| `probes/external-data/` | A Background over outlines fed from a CSV by Reqnroll's external-data plugin, alone and beside a written row, added to a copy of the shape suite by its own `run.sh`. |
| `probes/review-4/` | What the fourth independent review wrote: `features.txt`, cut at each `#file` line by `split.py`, with `suite/Steps/ReviewSteps.cs`, `api/Program.cs`, `ingest/make.py`, `run.sh`, `steps.sh`, and the browser probes `rowswitch.js`, `readerclose.js` and `composite.js`. Its `R` numbers are that feature file's rows. Its findings are `results/fourth-review.md`. |
| `probes/review-5/` | What the fifth independent review wrote: `Features/Q5/` and `Steps/Review5Steps.cs` over a copy of the shape suite, `gen/` for reports built by hand and from the options, the page probes, and `run.sh`, which runs its shapes and pages on one build. Its findings are `results/fifth-review.md`. |
| `probes/review-6/` | What the sixth independent review wrote: `mk-features.py` and `Steps/Review6Steps.cs` over a copy of the shape suite, `gen/` for reports from the options, `drive.js` with `sequences.js` for click sequences side by side on two builds, and `run.sh`. Its findings are `results/sixth-review.md`. |
| `probes/review-8/` | What the eighth, a narrow review of the hook and the search's lists, wrote: `drive8.js` with its `seq8*.js`, `gen/` and `pagesuite/` for its pages, and `suite/` with `run.sh` and `run-lane.sh` for its hooks. `run-final.sh <scratch> <tool> <dlls>` runs its fault shapes on a build. Its findings are `results/eighth-review.md`. | `results/eighth-review-on-the-final-cut.txt`, `results/eighth-review-hooks-final-*.txt` |
| `probes/r16-flake/run.sh <pristine> <patched> <rounds> [filter]` | `BackForwardRestoreTests.R16` on two trees by turns, with each failure's first line. | `results/r16-flake.txt`, `results/r16-flake-webkit.txt` |
| `probes/final-round/run-rest15.sh`, `run-rest8.sh`, `run-paint-row.sh`, `run-bp.sh` | The last round as it ran, every measurement on one build, with that session's scratch paths in them. The mutations ran again after it, unfiltered. | The numbers in the plan's last log entry |
| `probes/public-api/PublicApi.cs` | Every public and protected member of the assemblies given, one a line, for a diff of two builds. | `results/public-surface.txt` |
| `probes/non-public-hooks/run.sh <scratch>` | Which hook methods Reqnroll runs by visibility, own and inherited, on 3.3.4 and 3.0.1. | `results/non-public-hooks.txt` |
| `probes/review-7/` | What the seventh, a narrow review of the search's reveal and the hooks' last change, wrote: `drive7.js` with `seq.js`, `gen/`, and `Steps/`. Its findings are `results/seventh-review.md`. |

## The probes

| Probe | What it measures | Output |
|---|---|---|
| `probes/run-matrix.sh <label> <work> <kronikol> [dlls] [build args]` | The shape suite in all five modes and with an attachment, on the published packages or with a build's assemblies swapped into the same output. | `results/shapes-<label>-<mode>.txt`, `-attach.txt`, for `5.27.13`, `suggestion-only`, `prototype`, `prototype-r2`, `5.27.13-reqnroll-3.0.1`, `prototype-reqnroll-3.0.1` |
| `probes/shapes-table.py <kronikol> <reports>` | One row per scenario: written against marked, and whose copy each step is. Reads the report through `kronikol query` only. Its `WRITTEN` table is by hand, and `pickle-probe-*.tsv`'s `leadingBackground` says the same. | as above |
| `probes/upgrade-across-the-fix.sh <work> <kronikol> <dlls>` | Seven runs on 5.27.13 and one on the fixed build, one ledger: history's verdict, `query diff`, a merge. | `results/upgrade-across-the-fix.txt` |
| `probes/nested-shapes/run.sh <label> <work> <kronikol> [dlls] [build args]` | Shapes 21 to 23 on one build: `query steps` for each, and the digest of the failing one. `KRK_PROBE_LANE=xunit3\|tunit` runs them on that adapter. | `results/nested-shapes-5.27.13.txt`, `-prototype-on-5.28.2.txt`, `-prototype-reqnroll-3.0.1.txt`, `-prototype-lane-<lane>.txt`, `-with-223.txt` |
| `probes/edge-shapes/run.sh <label> <work> <kronikol> [dlls] [build args]` | Shapes E24 to E49 on one build: `query steps` for each, whether a report came out, the query hint (E49), whether a throwing `[AfterScenario]` leaves a row (E47), the digest's failing steps, then `stopAtFirstError`. `KRK_PROBE_LANE=xunit3\|tunit` runs them on that adapter. | `results/edge-shapes-5.27.13.txt`, `-prototype.txt`, `-prototype-reqnroll-3.0.1.txt`, `-prototype-lane-<lane>.txt`, `-with-223.txt` |
| `probes/run-lane.sh <xunit3\|tunit\|norowtests> <label> <work> <kronikol> [dlls]` | The matrix in all five modes on another adapter or generator setting, by `probes/lane-switch.py`. | `results/shapes-<label>-lane-<lane>-<mode>.txt` |
| `probes/compare-across-the-fix.py <kronikol> <reports-a> <reports-b>` | Two reports of one suite, scenario by scenario through `kronikol query`, per-run ids and times aside: every diagram's PlantUML, `interactions`, `flow` with step headers set aside, then `Specifications.yml` and `Failures.md`. | `results/across-the-fix-<mode>.txt`, pristine main against main with R1 |
| `probes/addresses-across-the-fix.py <kronikol> <reports-a> <reports-b>` | Every scenario's top-level step addresses in two reports of one suite, matched on stable id. Quick enough for a real suite, where a full compare takes over an hour. | `results/breakfastprovider-across-the-fix.txt` |
| `probes/census-against-report.py <kronikol> <reports> <features-folder>` | Per source file, a report's rows against what the feature files write. | `results/breakfastprovider-across-the-fix.txt` |
| `probes/paint-row-switch.js <reports> <title-part>` | With the separate section, whether a row's section is open once the row is selected. | `results/paint-row-switch-*.txt` |
| `probes/external-data/run.sh <label> <work> <kronikol> [dlls]` | `query steps` for each row of that suite on one build. | `results/external-data-shapes.txt` |
| `probes/ingest-lane/run-attachment.sh <work> <command...>` | The crash through `kronikol ingest`, on any build. | `results/ingest-lane-attachment-two-builds.txt` |
| `probes/compare-lanes.py <a> <b>` | Whether two `shapes-table.py` tables mark the same number of steps per scenario. | `results/lanes-agree.txt` |
| `probes/corpus-backgrounds.py <folder>` | A static census of `.feature` files against the detector's rule, every scenario passing. English keywords only. | `results/corpus-*.txt` |
| `probes/paint-failed-background.js`, `probes/paint-separate-section.js`, `probes/features-table.js` | What is painted: colours and borders of each step, whether the separate section starts open, and the Features summary's counts. | `results/paint-*`, `results/features-table-*.txt` |

No probe of their own, as they are commands over one run:

- `results/issue-suite-scenarios-and-failures.txt`, `issue-suite-steps-as-reported.txt`, `issue-suite-on-prototype.txt`: `query scenarios`, `steps`, `interactions`, `flow`, and `Failures.jsonl` for the issue's suite.
- `results/one-page-two-answers-and-specifications.txt`: `s9`'s step list, and `Specifications.yml` of the green shape run. `s9`'s diagram is read with `query diagram <reports> s9/d0 --out F`; the file ends with how many of its lines hold each value.
- `results/shapes-cucumber-lane-pass.txt`, `skipped-steps-two-lanes.txt`: the suite with Reqnroll's `message` formatter on (`"formatters": {"message": {"outputFilePath": "messages.ndjson"}}` in the output's `reqnroll.json`), then `kronikol ingest --cucumber-messages`. `probes/compare-lanes.py` compares that table with the prototype's: `results/lanes-agree.txt`.
- `results/pickle-probe-3.3.4.tsv`, `-formatter-on.tsv`, `pickle-probe-3.0.1.tsv`: `KRK_PICKLE_PROBE`, on the whole suite.
- `results/filtered-run-5.27.13.txt`: `dotnet test --filter "DisplayName=04 one"`, against the same scenario in the whole run.
- `results/shapes-prototype-steps-11.txt`: `query steps` for shape 11 on the prototype and on published 5.28.2.
- `results/ingest-lane-*.txt`: `kronikol ingest <captures> --tests <file>`, and with the detector's own-copies build.
- `results/shapes-prototype-on-5.28.2-lane-<lane>-<mode>.txt`: `probes/run-lane.sh`, all five modes on each lane, on the final cut.
- `results/r3-trial-facts.txt`: R3's one-line removal on main, and which of the 591 ingest facts then fail.
- `results/with-223-its-own-suite.diff.txt`: that plan's `repro` suite built with `-p:KronikolProto=` on `638f275d` plus `prototype/apply_on_223.py`, read with its `read-report.sh` and `results/compact.awk`, against its `triggers-prototype-final.compact.txt`, times aside.
- `results/breakfastprovider-across-the-fix.txt`: a scratch clone of BreakfastProvider at `640c190` with its 27 Kronikol pins moved to 5.28.2. `KRONIKOL_SUITE=reqnroll-in-memory dotnet test ./tests/BreakfastProvider.Tests.Component.ReqNRoll/BreakfastProvider.Tests.Component.ReqNRoll.csproj -p:WarningLevel=0 --no-build` from the clone's root, as its global.json picks the test runner, on the published packages and with the two assemblies swapped, twice each. It says which scenarios' addresses moved, and where the census's 205 rows and the report's 214 part.
- `results/reqnroll-examples-on-prototype.txt`: the three Reqnroll example projects on their own, with `prototype/apply_examples.py` applied, then every fact of `Example.Api.Tests.Integration`.

- `results/playwright-on-prototype.txt`, `playwright-whole-suite-on-prototype.txt`: `dotnet test tests/Kronikol.Tests.EndToEnd`, the classes nearest the change, then every fact.
- `results/fourth-review.md`: each fault the fourth independent review reproduced, what became of it, what it found fine, and #223's fifth review on the same matcher.
- `results/seventh-review.md`: the same for the seventh, with `results/seventh-review-that-cut-*.txt` as it read them.
- `results/eighth-review.md`: the same for the eighth, with `results/eighth-review-{faults,search,hooks}-*.txt` as it read them on that cut. Its fifth fault is gone by the design, not by its suggested fix.
- `results/sixth-review.md`: the same for the sixth, with `results/sixth-review-that-cut-*.txt` as it read them and `results/sixth-review-on-the-final-cut.txt` from `probes/review-6/run.sh`.
- `results/hook-shift-on-the-final-cut.txt`: the fourth review's `R02`, `R03` and `R60` on the final patch, a woven step in a hook shifting sub-steps with nothing drawn twice.
- `results/fifth-review.md`: the same for the fifth. `results/fifth-review-published-*.txt` and `-that-cut-*.txt` are what it read on 5.28.2 and on that cut; `results/fifth-review-on-the-final-cut.txt` is `probes/review-5/run.sh` on the final patch; `-loadtime.txt` and `-clicktime.txt` are its measured cost of drawing a failed composite's sub-steps.
- `results/edge-shapes-collect-on-finish.txt`, `-reqnroll-3.0.1.txt`, `results/shapes-collect-on-finish-*.txt`: `probes/edge-shapes/run.sh` and `run-matrix.sh` on pristine main with R4 alone. `results/edge-shapes-prototype-with-r4.txt`: the same on R1 with R4. The tail of each edge file reads E47 alone and beside a passing scenario.

Nothing of the real BreakfastProvider suite is copied here. Its files were read for the census (`corpus-breakfastprovider-reqnroll.txt`), and it was then run (`breakfastprovider-across-the-fix.txt`).

## The prototypes

Written against a checkout of `27cc0e46` and, from `apply_r1d.py` on, of `9d994a4b`, each in a throwaway worktree (`git -c core.autocrlf=false -c core.eol=lf worktree add --detach <dir> <commit>`). The patches are diffs of `9d994a4b`, and they are what to apply; the scripts record how R1 came to be:

| File | What it is |
|---|---|
| `prototype/apply_suggestion_only.py` | The issue's own suggestion and nothing else. |
| `prototype/apply_r1.py`, `apply_r1b.py`, `apply_r1c.py` | R1: the document, the count, nested steps, own copies; then the attachment walk and the section; then a nested step's arguments and outcome, and the walk over scenarios, after the first independent review. |
| `prototype/apply_r1d.py`, `apply_r1e.py`, `apply_r1f.py` | R1, after the audit of 2026-11-03: a frame per level; the count over the mapped rows and the query hint; the second review's fixes. Each file's docstring says why. |
| `prototype/apply_r1g.py` | R1, after a fresh verifier: three changes the fix did not need come out, and four faults the plan had left are fixed. |
| `prototype/apply_r1h_matcher.py`, `apply_r1i_review4.py` | The matcher in the form #225's R2 had then, and then the fourth review's fixes, which take it back to published's: the row switch by kind, the toggle's flag, a nested step's keyword, the new Playwright class. |
| `prototype/apply_r1j_copied_toggles.py` | A last read of the plan: `BuiltIn with { BackgroundStepsOpen = false }` carried the flag with it, so setting the property now clears it. |
| `prototype/apply_r1k_review5.py`, `apply_r1k_facts.py` | The fifth review's faults and facts: a nested step paired by what opened it, the public record back as published with the options saying what was left unset, what the reader left kept for each outline, a failed composite holding as a section does, and the search opening its match's sections after it chooses a row. |
| `prototype/apply_r1l_review6.py` | The sixth review's: the search opening only the row it chose and recording what it opened, a frame left open described as at the scenario's end, the public stopwatch set where a step opens in earnest, and the facts each break needed. |
| `prototype/apply_r1m_review7.py` | The seventh's: a step left open closing as the AfterScenario hooks begin, and the search keeping what it opened on a row apart and opening a row once. |
| `prototype/apply_r1n_internal_hook.py` | The hook that closes what a suite left open, internal in place of a new public method, and the fact that pins the class's public methods. |
| `prototype/apply_r1o_search_by_row.py` | After the eighth review: the search without its lists, opening the sections of the row shown once each until it is emptied, and its own row switches keeping and bringing nothing; a step a suite's `[AfterScenario]` hook left open closed by the adapter's last hook; the facts for each. The last change to the patch, reviewed by no one after it. |
| `prototype/apply_examples.py` | Section 6.3's real run: three features in three example projects, and the Integration facts on them. In the patch. |
| `prototype/apply_on_223.py` | What landing R1 second takes, on #223's prototype at `638f275d`. |
| `prototype/BackgroundAsWrittenTests.cs` | The first-cut facts, in `tests/Kronikol.Tests/ReqNRoll/`. |
| `prototype/r1-first-cut.patch` | All of R1 with its facts and section 6.3's first cut, as a diff of `9d994a4b`. |
| `prototype/apply_r2.py`, `prototype/r2-first-cut.patch` | R2 on top of R1: the steps that never ran. |
| `prototype/apply_collect_on_finish.py`, `prototype/collect-on-finish.patch` | R4, the plan's F16: a scenario is collected from Reqnroll's `ScenarioFinishedEvent` when the adapter's `AfterScenario` was not reached. The script applies on main and on main with R1; the patch is its diff on main. |
| `prototype/mutations.py` | One-line breaks of the final R1, each built and run against the facts, those in the hooks against section 6.3's real run, and those in the page's script against `BackgroundSectionRowSwitchTests`: `results/mutations-first-cut.txt`. Names after the two paths run only those. |

A release is written red-first from the plan's section 6. These show that the design works, and are not the release.

`results/first-cut-facts.txt` is the facts with their neighbours (`BackgroundStepsDetectorTests`, `ExamplesBlockResolverTests`, `AttachmentCopyTests`,
`ToggleDefaultsMarkupTests`), `results/core-suite-on-prototype.txt` the whole core suite, and `results/core-suite-r1-before-its-facts.txt` the same before the facts were added.

## Traps met

- `prototype/mutations.py` stops at the first entry whose old text has moved, and a log filtered for its verdicts then shows only a low count. Check every old text against the tree first, and run it unfiltered.
- A claim about public surface wants measuring: a new `public` hook on a public class passed every fact and seven reviews, and `probes/public-api/PublicApi.cs` shows it in a line. Reqnroll runs a non-public hook, so it was never needed.

- A real suite differs from itself between two runs (ids, timing-dependent calls), so `compare-across-the-fix.py` reads as noise there. On BreakfastProvider, `query diff`, `Specifications.yml`'s `BackgroundSteps:` blocks and `addresses-across-the-fix.py` are the measure, against a second control run.
- `query` prints under a byte budget, and its `next:` line carries that budget's flag ahead of `--offset`, so a probe looking for `next: --offset` stops at the first page: `compare-across-the-fix.py` once read 200 of 214 scenarios. Under `--json`, `next` is an argument list.
- A result is only as good as the cut it ran on: every prototype table here, and the mutations, were written again on the final patch, in one go, after the last change to it, and the earlier cuts' were deleted. The 5.27.13 and suggestion tables, the ingest and pickle probes, `core-suite-r1-before-its-facts.txt`, which the plan names as the first cut's, and `fifth-review-that-cut-*.txt` stay as they ran. `corpus-breakfastprovider-reqnroll.txt` was written again on the clone at `640c190`, as the first reading took an older checkout.
- A fact using `Assert.Contains` on a list of failing steps passed while the inner one was lost: mutation M15 survived it, and the fact now names each in order. A mutation that survives a real run is a question about the fact as well as the code.
- A form taken from a sibling plan still needs its own review. The matcher came from #225's R2 with its 20 labels measured, and it refused raw bars and matched falsely where published does neither (`results/fourth-review.md`). It was also no part of this issue.
- A fix needs its own review as much as what it fixes. The fifth review's search fault was answered by opening every row, which the sixth found losing what the reader closed. A new test project was also found red by a reviewer, not by the measurements, as nothing here ran `Kronikol.Tests.SearchEngine`; a change to a page script belongs beside every project that loads it.
- A design that draws a fault in every review is the fault. A flag on the public toggle record was wrong three ways in three reviews: a record built by hand, a copy with `with`, then equality and a round trip. Each fact was right and each fix was small. Taking the flag off the record ended all three, and the record has no diff now.
- The page embeds its own scripts, so `Assert.DoesNotContain("data-holds-failure", html)` finds the row script. Look for an attribute on its element's own tag.
- A mutation that survives may be pointing at the design, not at a missing fact. M27 set the flag's default to true and no fact noticed, because the two forms differed only for a record that left the section unset, and nobody had asked which was right.
- A whole-suite browser run shows load as well as faults: three facts failed in one run, two of them after minutes, and all three passed alone in seconds. Read them alone before believing them, and say so.
- A census of a sibling checkout reads that checkout. `corpus-breakfastprovider-reqnroll.txt` first said 67 features and 205 rows, from a local clone a month old, where the run said 214.
- A test source calling `IngestPipeline.Run` has to sit in `[Collection("DiagramsFetcher")]`: `ProcessGlobalStoreTests` scans for it, and the core suite goes red on a new fact that does not.
- A stop script matching a job by its command line can match itself: PowerShell's own command line held the pattern, and it stopped its own shell. Take pids from `ps` first.
- An early `07 a one (1 and 0)` in `across-the-fix-fail-bg.txt` was a read under load; it did not repeat.
- A scenario can carry its warm-up time after its name in `query scenarios`, which read as `BORROWED` in `shapes-table.py` and as a missing name in `compare-across-the-fix.py`. Both now take it off. Only the two tables it reached were written again.
- `shapes-table.py` once read "4 calls belong to no step" as a step's address, so the `steps` column was one too many for any scenario with such calls. The verdicts were not affected. It is fixed, and every table was written again.
- A heredoc holding `'''` or a backslash breaks or changes in the Bash tool, so these scripts were written with the Write tool.
- `KRONIKOL_HISTORY=off` and `KRONIKOL_KEEP_RUNS=off` keep repeated runs independent; `upgrade-across-the-fix.sh` wants the ledger, and makes `.kronikol/` first.
- A label containing a version number made no difference to `kronikol query`, but a report folder with no data file gives "No TestRunReport.json or TestRunReport.krkl", which on 5.27.13 was the crash of F3 showing up as an empty table. An empty table from `shapes-table.py` means that: read `<label>-attach.console.txt`.
