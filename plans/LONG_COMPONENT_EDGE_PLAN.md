# Long component edges in the render worker (#162)

**Date:** 2026-10-09 · **Repo version:** 4.14.4 (`main` at `9f2cd394`; line numbers are for that commit) · **Status:**
**green-lit in full 2026-10-10, EXECUTED (R1, R3 as 4.14.5; R2 as 4.14.6).** Drafted from #162 (filed 2026-10-08), at the owner's ask of 2026-10-09 to
analyse the issue critically and plan a fix for the underlying problem. The owner's ask of 2026-10-10: implement the plan
in full, including raising the issue with TeaVM and opening any PRs for the fix, so every §9 question is taken as
recommended (Q4 yes). R1 and R3 shipped as 4.14.5 and R2 as 4.14.6, both published (§11); the upstream half is konsoletyper/teavm#1295 and #1296.

Evidence labels: **RUN** (measured on 2026-10-09 on Windows 11 with the published 4.14.4 packages and the pinned engine,
`@plantuml/core@1.2026.8`; Chrome 154.0.8037.99, Playwright 1.59.1's Chromium 147.0.7727.15, Firefox 148.0.2, WebKit 26.4,
node 25.9, OpenJDK 25; scripts and output in [`LONG_COMPONENT_EDGE_PLAN.harness/`](LONG_COMPONENT_EDGE_PLAN.harness/README.md)),
**READ** (in Kronikol's source, tests, wiki or changelog at that commit, or in PlantUML's and TeaVM's source),
**INFERRED** (reasoned from two facts, stated by neither), **ESTIMATED** (not measured).

> **In Chrome, the component diagram is replaced by a stack-overflow error once one edge's line passes 1,600
> characters.** Under the default renderer (`BrowserJs`, four Web Workers), a component diagram with one long edge
> draws nothing in Chromium but the engine's error picture, `RangeError: Maximum call stack size exceeded`. [...]
> **Ask:** Give the component diagram's edge a statement cap of its own, and apply it in `GeneratePlantUml` in place of
> the borrowed message limit. Measure it for the unlinked edge in the `BrowserJs` worker, as `MaxLinkedLabelChars` was
> measured for the link: cold, warm, and with the optimizing compilers off.

## 0. Summary

The symptom is real and every figure in the issue reproduces within 2 to 10% (§1). The ask is right as far as it goes,
but the underlying problem has three layers, and the ask reaches only the middle one.

1. **The engine (the root cause).** TeaVM's port of `java.util.regex`, which PlantUML's JavaScript build runs on,
   recurses once per character in two common constructs: a bracket class under `+` or `*`, and a lazy `.*?`. PlantUML
   runs such patterns over a component edge's label (`Labels`), a link's text, a participant's name, a block opener, a
   coloured note bar and an activity action. The JVM's regex does not recurse there, so Java PlantUML draws all of them
   at 100,000 characters. In a Chromium worker, whose stack is half the main thread's, with V8's optimizing compilers
   off, a label of 520 to 550 characters exhausts it (§2.2, §2.3).
2. **Kronikol's statement guard.** Kronikol has learnt the engine's limits one statement kind at a time, each after a
   lost diagram: coloured bars (3.0.48), block openers (1.2026.8beta1), links (3.30.4), and now component edges. The
   component emitter borrowed the sequence-diagram message limit, labelled "unmeasured" in its own comment. The 3.0.48
   plan asked for the probe (`LONG_LINE_SYNTAX_ERROR_PLAN.md` §6 Q2) and the roadmap parked it until "a report of a lost
   component diagram" (`ROADMAP.md:632`); #162 is that report. 3.30.4 measured the component edge only with its link and
   blamed the link, and the doc comment then said unlinked text draws to the message limit. For this statement it does
   not, linked or not (§3, F2). Two older caps fail too: in a cold worker with the JIT on, the default configuration,
   a block opener fails from 1,010 characters and the emitter's coloured step bar from 1,040, under caps of 1,471 and
   1,400. Both were measured in node, and 3.30.4's worker check counted any SVG as drawn, error pictures included.
   Participant names and activity actions have no cap and fail from 275 to 820 characters with the optimizers off
   (§2.4).
3. **Kronikol's page.** The worker hands back the error picture as a finished render. The page counts it as a
   success, caches it, and shows PlantUML's picture, which asks the reader to mail plantuml@gmail.com. The node
   renderer does the same. Nothing in Kronikol could have noticed #162, and nothing will notice the next one (§3, F5).

The plan:

- **R1 (patch), the ask, done so the edge keeps its counts.** A measured cap on the component edge's label as written.
  A longer method list loses whole entries from its end, marked `…`, and keeps `- N calls across M tests` (the cut the
  ask implies, `TruncateLabel` on the whole label, would drop the counts first). The value comes from R1's own re-measure;
  on today's figures it is about 375 characters, roughly ten SQL statements.
- **R2 (patch), the page and node notice a stack overflow.** The engine's stack-overflow picture counts as a failed
  render, stays out of the cache, and is named by Kronikol's own message, as a `Syntax Error?` already is.
- **R3 (patch), the rest of the class.** The block-opener and coloured-bar caps come down to their worker edges (about
  600 each), and participant names (about 200) and activity actions (about 600) get caps of their own, each measured in
  the worker with a verdict that rejects every error picture. The probe's verdict is fixed, and a corpus invariant
  extends to the component and activity emitters. Two of these fail in the default configuration, so R3 is recommended
  in the same release as R1 (Q8).
- **Upstream, the owner's call (Q4).** Both TeaVM fixes were prototyped: an iterative greedy quantifier over a
  character class and an iterative lazy one over the dot. With both, every statement measured draws at 4,000
  characters or more (§2.3, §2.4). Reporting it is the only change that removes the cause. Kronikol then re-pins and
  raises its caps in a later plan.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | Workers, JIT on, a fresh worker per page: a 1,600-character edge line draws and 1,610 fails | **Different.** A cold worker draws to 1,770 (Chrome 154) and 1,930 (Chromium 147). A warm one (50 shorter renders first) fails from 1,580 in one run of five in Chrome 154, from 1,700 in five of five, and from 1,650 in Chromium 147. The issue's figure sits in the warm band; with the issue's own CDP script, 1,600 and 1,610 both drew 3 of 3 here | RUN |
| 2 | Optimizing compilers off (`--js-flags="--no-opt --no-maglev"`): 540 draws, 550 fails | **Different by 30.** 570 draws, 580 fails, in both builds | RUN |
| 3 | Main thread, JIT on: draws every length to the cap | **Confirmed** (1,984 and 2,000, 3 of 3) | RUN |
| 4 | Main thread, optimizers off: 1,040 draws, 1,050 fails | **Different by 60.** 1,100 and 1,110 | RUN |
| 5 | The line's length counts, not its breaks | **Confirmed,** and its content does not count either: commas, `\n` escapes and an all-`x` line fail at the same length. Only a quote character changes it, because it ends the run the engine walks. The exact measure is the label's length: the aliases are matched separately and do not add to it | RUN |
| 6 | 160 short lines (832 characters) draw | **Confirmed with the JIT on; they fail** in a worker with the optimizers off (2 of 2), since 832 is past 580 | RUN |
| 7 | 44 statements draw; 48, 56 and 66 error in the workers; all draw on the main thread | Lengths and display-line counts **match exactly**. Here 48 draws in both builds, 56 draws in Chromium 147 and fails in Chrome 154, 66 fails in both | RUN |
| 8 | Every failure is the same error picture | **Confirmed**: 357 failures, each the RangeError picture at the edge line | RUN |
| 9 | The edge is `caller --> warehouse : "…"` | A default run writes `caller -[#E74C3C]-> warehouse : "…"`: `ArrowColorMode.DependencyType` is the default and always yields a colour ([ComponentDiagramOptions.cs:61](../src/Kronikol/ComponentDiagram/ComponentDiagramOptions.cs#L61), [DependencyPalette.cs:70-77](../src/Kronikol/DependencyPalette.cs#L70)). `-->` is written only under `ArrowColorMode.Performance`. The issue's lengths are the coloured form's | READ, RUN |
| 10 | The unlinked edge is the only kind a generated report writes | **Confirmed.** No caller passes stats ([ReportGenerator.cs:413](../src/Kronikol/Reports/ReportGenerator.cs#L413), [ComponentDiagramReportGenerator.cs:48](../src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs#L48), [MergeableReportRenderer.cs:45](../src/Kronikol/Reports/Merge/MergeableReportRenderer.cs#L45)) | READ |
| 11 | `MaxLinkedLabelChars`' doc says unlinked text draws to the message limit, which does not hold here | **Confirmed,** and more: the link was never the cause for this statement (§3, F2) | RUN |
| 12 | A real suite: 43 statements, 1,810 characters on 24 display lines, the error picture on 4.5.0 and 4.13.2, in both views | Not re-run (the suite is described by counts only). Consistent: 1,810 is past every worker edge measured here except Chromium 147's cold one | INFERRED |
| 13 | About 45 tables reach 1,600 characters; with the optimizers off about 15 | 44 statements make 1,493 and 48 make 1,622. With the optimizers off, 15 (555) draw here and 20 (717) fail | RUN |
| 14 | The main thread is no way out: with the optimizers off it fails from 1,050 | **Confirmed in kind** (1,110 here). With the JIT on it draws every length, so `BrowserRenderWorkers = 0` is a workaround for the default configuration only | RUN |

**Why the issue's figures are lower.** Its `check.js` opens `'file://' + path`, which suggests a POSIX path, so its
figures probably come from Linux or macOS, where thread stacks are sized differently (INFERRED). Chrome 154 also
gives a cold edge 160 characters below Chromium 147's on this machine, while the optimizers-off edge is the same in both
(RUN). A cap therefore needs a margin under figures from platforms this machine does not run, and R1 measures Linux
too (§4.1).

*Measured 2026-10-10:* Linux is not the reason. Playwright's Chromium 147 on Linux (its own image, under WSL) gives the
same edges as on Windows to the character, with the optimizing compilers off and with the JIT on, so the issue's
platform stays unknown (macOS and Edge were not measured). The figure the caps keep their margin under is this
machine's: a label of 550 as written, the line's 580 in the table above (§11, 2026-10-10).

## 2. Where it stands today

### 2.1 The code (READ)

- **The label** ([ComponentDiagramGenerator.cs:217-242](../src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs#L217)):
  `"{Protocol}: {m1}, {m2}, … - {N} calls across {M} tests"`, one entry per distinct method, sorted. A
  `RelationshipLabelFormatter` replaces it whole; the stats form puts the method list in `[[#iflow-rel-… ]]` with two
  stats lines after it.
- **Wrapping and the caps** (`:253-258`): `WrapLabel` breaks display lines at 100 characters with two-character `\n`
  escapes; `CapLinkText` cuts the text inside a stats link to `MaxLinkedLabelChars` (350); then `TruncateLabel` cuts the
  whole label to `MaxMessageStatementChars - (caller alias + service alias + 40)`. Its comment (`:244-252`) calls that a
  defensive ceiling "rather than a measured one".
- **The emitted forms** (`:279-296`): `a -[#colour]-> b : "label"` by default, `a --> b : "label"` under
  `ArrowColorMode.Performance`, `a ..> b` for a low-coverage stats edge, and C4's `Rel(a, b, "label", $tags=…)` when
  `useC4` is set, which only Server and Local rendering do ([ComponentDiagramReportGenerator.cs:73](../src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs#L73)).
  Server and Local draw with Java PlantUML, which is not affected (§2.3).
- **Where it is drawn.** The run report's component panel (by the page under BrowserJs; at write time through
  `DrawEmbedded` under NodeJs, Local and Server), `ComponentDiagram.html` (`#comp-diagram`), `kronikol merge` (the panel,
  always page-drawn, `useC4: false`) and `kronikol ingest` (as a run). Not on Specifications.html or the labs page.
- **Which edges grow.** The edge lists each distinct `Method`. At the default `Detailed` verbosity that is one entry per
  table for the SQL family, ClickHouse, Dapper and Spanner (`INSERT INTO <table>`), per collection for MongoDB, per topic
  for Kafka (per message under `Raw`), per queue for Service Bus, per table for Bigtable, per index for Elasticsearch,
  per RPC for gRPC, and whatever the capture holds for ingest and OTLP. HTTP verbs, EF Core, Redis, Cosmos, Blob, S3,
  DynamoDB, BigQuery and MediatR stay short.
- **What happens to the error picture** ([plantuml-worker-host.js:232-267](../src/Kronikol/Reports/plantuml-worker-host.js#L232),
  [plantuml-browser-render-script.js](../src/Kronikol/Reports/plantuml-browser-render-script.js)). The worker posts any
  SVG back as `done`. The page counts it as a render and caches anything holding `<svg` (`:131-139`, `:219-223`).
  `describeEngineFailure` (`:1046-1083`) knows "too large", the openiconic and emoji load failures and `Syntax Error?`,
  and the statement check (`:1018-1041`) knows messages over 2,000 and block labels over 1,471. Neither mentions
  `RangeError`, so `__kronikolRender.errors` stays 0. Under NodeJs any `<svg` is a success
  ([NodeJsPlantUmlRenderer.cs:134-136, 186-188](../src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs#L134)). The only
  RangeError check in the product is the internal-flow popup's, for activity diagrams
  ([internal-flow-popup-script.js:149-158](../src/Kronikol/Reports/internal-flow-popup-script.js#L149)).
- **What tests cover.** No test renders a long unlinked component edge. `EdgeLabel_IsCappedAtTheStatementLimit` and
  `Wrapping_StillHonoursTheStatementLimit` ([ComponentDiagramGeneratorTests.cs:470-491, 1230-1241](../tests/Kronikol.Tests/ComponentDiagram/ComponentDiagramGeneratorTests.cs#L470))
  check the source against 2,000. The S0 theory (`:1270-1301`) and `LongStatementRenderingTests`' fixture
  ([ReportTestHelper.cs:309-334](../tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs#L309)) use the stats form. The
  corpus invariant ([PlantUmlStatementLengthTests.cs:478-506](../tests/Kronikol.Tests/PlantUml/PlantUmlStatementLengthTests.cs#L478))
  covers sequence diagrams only. The worker probe's `--scan` reads only lines holding `[[#iflow-`, and `emitter-corpus`
  writes the component edge only with stats ([tools/render-bench](../tools/render-bench/README.md)).

### 2.2 The measurements (RUN)

Edge line, coloured default form, last length that draws / first that fails. Ten-character steps, every verdict
repeated at least three times with the JIT on and twice with the optimizers off. Cold means a fresh browser per case.

| Configuration | Chrome 154 | Chromium 147 |
|---|---|---|
| Workers, JIT on, cold | 1,770 / 1,780 | 1,930 / 1,940 |
| Workers, JIT on, warm (50 shorter renders first) | 1,550 always drew; 1 of 5 failed at 1,580, 1,610 and 1,630; 4 of 5 at 1,650; 5 of 5 from 1,700 | 1,640 / 1,650 |
| Workers, optimizers off | 570 / 580 | 570 / 580 |
| Main thread, JIT on | every length to 2,000 | the same |
| Main thread, optimizers off | 1,100 / 1,110 | the same |
| The issue (probably Linux or macOS) | workers 1,600 / 1,610; optimizers off 540 / 550; main thread, optimizers off 1,040 / 1,050 | |

- Firefox 148 draws every length to 2,000, in workers and on the main thread, with its JITs on and off. WebKit 26.4 draws
  every length too, but this Windows build has no `OffscreenCanvas`, so a four-worker page falls back to the main
  thread: WebKit's worker path cannot be measured here.
- The real emitter, for 10, 15, 20, 30, 40, 44, 48, 56 and 66 statements, writes lines of 393, 555, 717, 1,040, 1,363,
  1,493, 1,622, 1,881 and 1,979 characters. In workers with the optimizers off, 15 draw and 20 fail.
- **Stack depth** of a trivial recursive function, main thread against worker: Chrome 154 12,447 against 6,349
  with the optimizers off and 20,710 against 10,583 after tier-up (a ratio of 1.96); Chromium 147 the same; Firefox 148
  39,457 against 21,609; WebKit 70,978 on both (its main thread). The edge's own main-to-worker ratio with the
  optimizers off is about 1.92: the edge is a stack budget, halved in a worker.

### 2.3 The cause, in the engine (RUN, READ)

The overflow's own stack was captured in node (`Error.stackTraceLimit = Infinity`, a hook in a copy of the engine), on
the npm build and on an unobfuscated build of upstream master, and the regex engine's nesting was counted per match.

- **The cycle.** `GroupQuantifierSet.matches → CompositeRangeSet.matches → SupplRangeSet.matches` and round again: three
  Java frames, six JavaScript frames, per character of the label. The nesting is exactly 3 × length + 5. The stack cost
  is about 920 bytes per character with the optimizing compilers off and about 280 with the JIT on.
- **In TeaVM** (0.14.1; the classes are the same in 0.16.0): `TPattern.processQuantifier` (`TPattern.java:653, 667`)
  gives any term that is not a `TLeafSet` a recursive `TGroupQuantifierSet`. Every bracket class becomes such a term,
  because the `TCharClass` constructor marks every class as able to hold supplementary code points
  (`TAbstractCharClass.java:154`), and `hasLowHighSurrogates()` (`:88-91`) is true for a negated class and, through a
  `-1 < 2048` comparison, for a positive one too, so `processRangeSet` (`TPattern.java:1203-1210`) builds a
  `TCompositeRangeSet`. TeaVM master's 846242b3d (2026-10-04, unreleased) fixes the positive-class comparison only.
- **In PlantUML**, `Labels` (`Labels.java:62`, `init` at `:80-81`, `:87-88` and `:94-95`) matches the label against
  `^[%g]([^%g]+)[%g]…`-style patterns to find a link's first and second labels. `CommandLinkElement.java:309` (component
  and deployment diagrams) and `CommandLinkClass.java:343` (class diagrams) call it. The link command's own regex
  stays at 105 levels at any length; UBrex, the preprocessor and creole play no part.
- **Why a sequence message of the same length draws:** `CommandArrow` takes the message with `(.*)`
  (`CommandArrow.java:132`), which TeaVM compiles to a loop, and no `Labels` pass follows. A `[[…]]` link in it recurses
  in `UrlBuilder`'s `[^\[\]]*`: the same defect, and the reason for `MaxLinkedLabelChars`.
- **The JVM** (OpenJDK 25, `-Xss256k`) runs the same three patterns over 1,000,000-character labels without overflow,
  while a control, `(?:a|[^g])+`, overflows at 500. The 1.2026.8beta1 jar draws 20,000-character component edges at that
  stack. So Server and Local rendering are not affected.
- **Upstream:** PlantUML master 57a3d2848 (2026-10-06) has the same `Labels` patterns. Neither tracker has a report.

At one fixed budget (node, `--stack-size=400`, a fresh process per probe), the longest statement that draws:

| Shape | Optimizers off | JIT on |
|---|---|---|
| `caller --> warehouse : "…"` | 450 | 1,431 |
| the same, unquoted | 452 | 1,432 |
| `caller -[#E74C3C]-> warehouse : "…"` | 460 | 1,440 |
| wrapped every 100 characters / one line | 452 / 450 | 1,431 / 1,431 |
| the real ClickHouse label / only `\n` / only commas | 450 / 451 / 450 | 1,430 / 1,433 / 1,432 |
| quote characters inside the label | 4,000 or more | 4,000 or more |
| a sequence message | 2,000 (PlantUML's own limit) | 2,000 |
| a sequence `[[#link …]]` | 433 | 795 |
| a class-diagram link | 438 | 806 |
| C4 `Rel(…)` (stdlib loaded locally) | 450 | 1,285 |
| 200-character aliases | 837 | 1,779 |

With the optimizers off every shape that goes through `Labels` fails at the same label length (426 to 427 here), and
the edge scales linearly with the stack (228, 450, 898 and 1,786 characters at 200, 400, 800 and 1,600 KB). The label's
length is the exact measure, and a cap on the whole statement is conservative. With the JIT on the edge moves with
warm-up, so only the optimizers-off figure is stable enough to set a cap from.

**Two fixes were prototyped** in memory on the unobfuscated build:

- **TeaVM:** an iterative greedy quantifier for `TSupplRangeSet` and `TCompositeRangeSet` (walk forward over code points,
  then try the rest of the pattern from the longest match down, as OpenJDK's `CharPropertyGreedy` does), and an
  iterative lazy one over the dot (§2.4). On a 30-diagram corpus and 35 further renders the SVGs were byte-identical.
  Component edges then draw at 30,000 characters and more, and a sequence link stops only at the 2,000-character message
  limit.
- **PlantUML:** `Labels.init` runs each pattern only when quote characters sit where its anchors need them. Ten `Labels`
  cases were byte-identical and edges drew at 30,000 and more, but this leaves links, participants, block openers,
  coloured bars and activity actions as they are.

### 2.4 The same defect elsewhere (RUN)

The second cycle is `ReluctantGroupQuantifierSet.matches ↔ DotSet.matches` (`TReluctantGroupQuantifierSet.java:55-57`,
`TDotSet.java:72`), two Java frames per character, which TeaVM builds for every `*?` or `+?` over the dot
(`TPattern.java:673-676`; a greedy `.*` gets a loop). The JVM runs every pattern below at 100,000 characters.

Each statement was written by Kronikol 4.14.4's own emitter, put in a page with 4 workers and the pinned engine, and
rendered as the first render of a fresh browser (642 cases, one at a time). The verdict rejects the RangeError picture
and text, `Syntax Error?`, `An error has occurred` and any picture whose first drawn line starts `PlantUML `, and a
sequence diagram must draw each participant twice. Figures are the last length that draws and the first that fails;
with the optimizers off Chromium 147 and Chrome 154 agree.

| Statement Kronikol writes | Its cap today | Worker, optimizers off | Worker, JIT on, cold | Node 400 KB, optimizers off | Where the recursion starts |
|---|---|---|---|---|---|
| Component edge (line) | 2,000 (borrowed) | 570 / 580 | 1,930 / 1,940 (Chromium 147), 1,770 / 1,780 (Chrome 154); warm from 1,580 | 450 | `Labels`, greedy class |
| Stats edge, link text (line) | 350 (line 491, draws) | 430 / 440 (571 / 581) | every length to 1,500 | | `Labels` over the whole label |
| Sequence `[[#iflow-…]]` link text | 350 | 496 to 500 (3.30.4) | 980 to 1,010 (3.30.4) | 433 | `UrlBuilder` `[^\[\]]*`, greedy class |
| Block opener, `loop` or `partition` | 1,471 | 830 / 840 | 1,000 / 1,010; Chrome 154 flaky to 1,050 | 658 | `CommandGrouping` COMMENT `(.*?)` (`CommandGrouping.java:72`), lazy |
| Coloured step bar, as the emitter writes it | 1,400 | 870 / 880 | 1,030 / 1,040 | 692 | `CommandCreoleColorChange` `…>(.*?)</color>` (`:58`, `:77-80`), lazy |
| Styled step bar (a step with spaces) | 16,000 | every length to 13,600 | the same | | none |
| Sequence participant name (`entity`, `database`) | none | 270 / 275 (line 553) | 515 to 550, flaky in places (line about 1,080) | 435 (line) | `CommandParticipantA` `[%g]([^%g]+)[%g]` (`:58`), greedy class |
| Component `database` name | none | 330 / 335 (line 673) | 720 / 725 | 648 (line) | `CommandCreateElementFull` `[%g].+?[%g]` (`:128`, `:130`), lazy |
| Component `rectangle <<system>>` name, wrapped at 80 | none | 305 / 310 (line 685) | 680 / 685 | 647 (line) | the same |
| The same rectangle with one unbroken name | wrapped at 80, which avoids it | | | 330 | `CreoleStripeSimpleParser` alternation group (`:69`, `:120`), about 5 frames per character |
| Activity action, span name ([InternalFlowRenderer.cs:110, 116-120](../src/Kronikol/InternalFlow/InternalFlowRenderer.cs#L110)) | none (wrapped at 100) | 810 / 820 | 980 / 990 | 653 (line) | `CommandActivity3` `(.*?)` (`:70`), lazy |
| `title` | none | | | 4,000 or more | none |

- **Two existing caps fail in the default configuration.** In a cold worker with the JIT on, a block opener fails from
  1,010 characters and the coloured step bar from 1,040, under caps of 1,471 and 1,400. Both caps were measured in node,
  whose stack is the main thread's (about 2,000 there), and 3.30.4's worker check did not catch them (below).
- **How each fails.** Openers, participant declarations and activity actions fail as a `Syntax Error?` picture naming the
  declaration line, since the RangeError is swallowed while a command is chosen. The coloured bar fails as plain
  RangeError text, with no picture. The edges fail as the RangeError picture.
- **What reaches them.** Kronikol's own openers are short (`loop ×6 · 37 ms`, `partition #F6F6F6 Setup`); a long one
  comes only from spliced PlantUML, which the guard caps at 1,471. The emitter writes the coloured bar only for a step
  with no break in it, such as a JSON array written as one token; a step with spaces takes the styled form, which draws
  to 13,600. Participant names come from service names, hosts and ingested captures; activity actions from span names,
  which some instrumentations set to a whole SQL statement.
- **3.30.4's worker check was not sound for these rows.** The probe's legacy verdict
  ([statement-limits-worker-probe.js:52-54](../tools/render-bench/statement-limits-worker-probe.js#L52)) counts any SVG as
  drawn, error pictures included. Re-run with a strict verdict, every loop label from 1,000 to 2,000 is a `Syntax Error?`
  picture with `--jitless` or the optimizers off, so the "drawn to 2,000" in
  [results line 23](../tools/render-bench/results/statement-limits-worker-2026-09-26.txt) is false for them. The coloured
  bars drew because the probe's filler breaks every 110 characters or so, which the emitter never writes for that form;
  the same filler without its breaks fails from 880. Messages do draw to 2,000. The JIT-on run of 2026-09-25 in one warm
  page was right: warm, loop labels draw to 2,000.

## 3. Findings the issue does not state

- **F1. The cause is TeaVM's regex port, not PlantUML's component parser and not Kronikol (§2.3).** Server and Local
  rendering, which run Java PlantUML, are not affected. BrowserJs and NodeJs are.
- **F2. The link was never the cause on a component edge.** 3.30.4 measured the component edge only with its stats
  link, attributed the overflow to the link, and capped the link's text at 350
  ([ENGINE_PIN_PLAN.md §10.1](ENGINE_PIN_PLAN.md)). `Labels` walks the whole label, link or not, and the link's own
  recursion (`UrlBuilder`, over the link's text alone) is a separate, shallower pass, so the whole label's length is
  what binds (RUN in node: the depths of two passes do not add). A stats label whose link text is cut to 350 is about 460 characters with its wrapper, stats lines and wrap escapes (INFERRED from the
  label's format): under the edge, with about a tenth of margin where 3.30.4 meant to keep a quarter. The doc sentence "The same text unlinked draws at every length to the message
  limit" ([PlantUmlStatementLimits.cs:132-133](../src/Kronikol/PlantUml/PlantUmlStatementLimits.cs#L132)) holds for a
  sequence message only; its copies are in `CHANGELOG.md:3145-3150`, `PlantUmlStatementLengthTests.cs:274-275` and
  `ENGINE_PIN_PLAN.md:382, 592, 1152`.
- **F3. The binding configuration is a worker with the optimizing compilers off.** With the JIT on, a warm worker
  fails earlier than a cold one, and Chrome 154's warm edge is not even stable (1,580 fails one run in five). A
  configuration with the optimizers off is not exotic: 3.30.4 set `MaxLinkedLabelChars` against it because an enterprise
  policy or a browser security mode turns them off (READ, [ENGINE_PIN_PLAN.md §10.1](ENGINE_PIN_PLAN.md)).
- **F4. The figures depend on the platform and the browser version** (§1). The cap needs a margin under figures this
  machine cannot produce, and a Linux run.
- **F5. Nothing in Kronikol notices the failure** (§2.1). The picture is cached as a success, `__kronikolRender.errors`
  stays 0, a NodeJs report embeds it without a diagnostic, and the reader sees PlantUML's mail-us picture with nothing
  that points at the label or at Kronikol. This is why #162 surfaced as a user report, and why every earlier limit did.
- **F6. A cut at the end of the label loses the counts.** `TruncateLabel` on the whole label, which is what the ask's
  "costs the end of its own label" gives, cuts `- 48 calls across 1 tests` first. The 3.30.4 precedent, `CapLinkText`,
  cuts inside the link and keeps the lines after it; R1 does the same for the method list.
- **F7. The same defect reaches the other statements, and two existing caps do not hold** (§2.4). Block openers fail
  from 1,010 and coloured bars from 1,040 in a cold worker with the JIT on, under caps of 1,471 and 1,400; participant
  names fail from 275 characters and activity actions from 820 with the optimizers off, with no cap at all. Aliases,
  derived from the names, are matched at three levels per character in a pass of their own.
- **F7a. 3.30.4's worker evidence for those caps was a false pass.** Its probe counted any SVG as drawn, so loop labels
  that drew `Syntax Error?` from 1,000 characters read as drawn to 2,000, and its coloured-bar filler had line breaks the
  emitter never writes in that form (§2.4). A probe must reject every error picture, and generated inputs must be in
  the real writer's format.
- **F8. The roadmap was waiting for this.** `ROADMAP.md:632` lists "component-diagram parser limits" under deliberately
  not scheduled, with the trigger "A report of a lost component diagram".
- **F9. WebKit's worker figures do not exist on Windows.** This WebKit build has no `OffscreenCanvas`, so its rows in
  3.30.4's table and in the wiki are main-thread figures (INFERRED for the 3.30.4 run, which used the same build).
- **F10. Workarounds today**, for the issue's reply and the changelog: an adapter's `Verbosity = Summarised` (the SQL
  family then records the bare verb), a `RelationshipLabelFormatter`, a `ParticipantFilter` that drops the edge, or
  `BrowserRenderWorkers = 0`, which draws every length with the JIT on but fails from about 1,100 with the optimizers off.
- **F11. Kronikol4J** writes the edge with no wrapping and no cap
  (`kronikol4j-diagram/…/component/ComponentDiagramGenerator.java:157-190`), and renders on the main thread with its
  3.0.43 script.

## 4. The design

### 4.1 R1: a measured cap on the component edge's label (the ask)

**The value.** A new internal constant, `PlantUmlStatementLimits.MaxComponentEdgeLabelChars`: the longest edge label, as
written (after `WrapLabel`, every `\n` escape counted), that the worker draws. It is set from R1's first step:

1. `emitter-corpus` gains `--component-edges <dir>`: unlinked edges written by `ComponentDiagramGenerator` with default
   options (the coloured form), for four method shapes (SQL statements, gRPC methods, Kafka topics, non-ASCII table
   names) and a formatter's label, plus the stats form.
2. The worker probe gains a scan over those edges (`--scan-edges`), cutting the label with R1's own cut, with the
   verdicts of the `--scan` mode (`STACK-PICTURE`, `ERROR-PICTURE`, drawn).
3. Configurations: Chrome stable and Playwright's Chromium; workers cold, warm (50 renders first) and with the optimizers
   off; the main thread with the optimizers off; Firefox; WebKit; and Playwright's Chromium on Linux (a container under
   WSL works on this machine), since the issue's lower figures probably came from a POSIX platform. Results go to `tools/render-bench/results/component-edge-worker-<date>.txt`.
4. The constant is at most 75% of the lowest label edge found, the margin `MaxLinkedLabelChars` and `MaxBlockLabelChars`
   keep. The issue's 550-character line, less the 34 characters around its label, is a label edge of 516, so the cap
   comes to about 385; **375 is the expected value**, about ten `INSERT INTO <table>` entries.

**The cut.** In `GeneratePlantUml`, after the label is built and wrapped:

- A label within the cap is unchanged, byte for byte. The tests whose labels pass the cap are the ones §5 changes;
  Kronikol4J's goldens (labels of about 40 characters) do not move.
- The default form drops whole entries from the end of the sorted method list, adds `…` as the last entry, re-wraps, and
  repeats until the label fits: `ClickHouse: DELETE FROM a, INSERT INTO b, … - 48 calls across 1 tests`. The counts
  are kept. If one entry alone is too long, that entry is cut with `TruncateLabel`, as today.
- The stats form does the same inside the link, so the link stays closed and both stats lines are kept; the whole
  label, not only the link's text, is within the cap. `CapLinkText` stays as it is: its 350 is now never the binding
  limit.
- A formatter's label is the consumer's format, so it is cut at its end with `TruncateLabel`, as today, to the new cap.
- The 2,000-character statement cap stays as the backstop it is.

Whether the marker also says how many entries were left out (`…, +38 more`) is Q1.

**What the reader loses.** An edge with more entries than fit shows the first ten or so in sorted order, and its
counts. Every call is still in its scenario's sequence diagram. The changelog says so.

### 4.2 R2: the page and node notice a stack overflow

- **One detector**, `isStackOverflow(result)`: the engine wrote RangeError text and no SVG (how a coloured bar fails),
  or the SVG is PlantUML's error picture (its first drawn line starts
  `PlantUML `, the test 3.30.1's probes settled on) and its error line names a stack overflow: `Maximum call stack size
  exceeded` (V8 and JavaScriptCore) or `too much recursion` (SpiderMonkey). Captured text that quotes the phrase in a
  drawn diagram is not a failure. It lives in the render script and is shared with the
  internal-flow popup, whose own check (`internal-flow-popup-script.js:149-158`) it replaces.
- **In the page**, on a worker's `done` and after a main-thread render: a stack picture counts in
  `__kronikolRender.errors`, is not cached, and gets the same treatment `describeEngineFailure` gives `Syntax Error?`:
  Kronikol's line names the cause (the engine ran out of stack) and the longest statement in the source, with its
  length. The engine's own picture stays below it. A block opener's overflow already reaches the reader as
  `Syntax Error?`, as do participant declarations and activity actions (§2.4): that picture cannot be told from a real
  syntax error, so the `Syntax Error?` branch also names the line's length when it is past the measured cap for its kind.
- **In node**, `NodeJsPlantUmlRenderer` treats a stack picture as a failed render, so `DrawEmbedded`'s catch writes the
  `component-diagram-failure` block and a `RenderFailure` diagnostic, as it does for an exception today. A sequence
  diagram's stack picture under NodeJs takes the placeholder path `DefaultDiagramsFetcher` already has for a refused
  diagram (`:386-430`).
- **No retry.** A second render on the main thread would draw the default configuration's long edges but hide the
  failure again, and fails the same way with the optimizers off. That is Q5.

### 4.3 R3: every statement Kronikol writes has a measured cap

- **Fix the probe first.** The legacy mode of `statement-limits-worker-probe.js` takes the `--scan` mode's verdict
  (reject the RangeError picture and text, `Syntax Error?`, `An error has occurred`, any picture whose first drawn line
  starts `PlantUML `, and a sequence participant drawn once), and every source comes from the emitter, in the form the
  emitter writes it: the coloured bar with no break in it, the participant line as `PlantUmlCreator` writes it.
- **Re-measure** each row of §2.4 in the worker with the optimizers off and with the JIT on, cold and warm, on Windows
  and on Linux, and set each cap at no more than 75% of its lowest edge. On today's figures, with the optimizers off
  binding (Q2):

| Statement | Cap today | Lowest edge | Expected cap |
|---|---|---|---|
| Block opener | 1,471 (`MaxBlockLabelChars`) | 840 | about 600 |
| Coloured step bar | 1,400 (`MaxColouredNoteBarChars`) | 880 | about 600 |
| Participant name, sequence and component | none | 275 (sequence), 310 (component) | about 200, one constant |
| Activity action | none | 820 | about 600 |
| Placeholder note ([DefaultDiagramsFetcher.cs:77-81](../src/Kronikol/DefaultDiagramsFetcher.cs#L77)) | none | measure | measure |

- **A cut name keeps its participant distinct.** The alias is derived from the name, so two names with a common 200-
  character prefix would merge into one participant. A cut name's alias takes a short hash of the whole name, and the
  whole name stays in the report's data.
- **A cut step bar keeps its step.** The bar is a step's label; the step text stays in the report's step list.
- **Route** sequence participant lines through the guard, or cap them where they are written; today they are written in
  the prefix and bypass it ([PlantUmlCreator.cs:1194-1208, 2049-2053](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L1194)).
- **Hold the class with a fact.** The corpus invariant ([PlantUmlStatementLengthTests.cs:478-506](../tests/Kronikol.Tests/PlantUml/PlantUmlStatementLengthTests.cs#L478))
  extends to the component and activity emitters: every line each emitter writes, for a corpus that drives every field
  to its maximum, is within the measured cap for its kind. A second fact fails when an emitter writes a statement kind
  the table does not know. `PlantUmlStatementLimits`' doc table records, per kind, where each figure was measured: node,
  the main thread, the worker cold and warm, and with the optimizers off.
- **The browser's own check** (`plantuml-browser-render-script.js:1018-1041`), which names an over-long line when a
  diagram fails, takes the new caps.

### 4.4 Upstream: the engine (the owner's call, Q4)

- **TeaVM** is where one change removes every limit in §2.4 but the unbroken bold name, which Kronikol's wrapping
  already avoids. A report would carry the root cause (§2.3), a minimal Java repro (`[^"]+` over a long string), the
  two prototypes, and the byte-identical corpus. The owner has reported to TeaVM before (teavm#1247, fixed; teavm#1248).
- **PlantUML** could guard `Labels.init` on its own. That fixes component and class edges only.
- Nothing is posted without the owner's go-ahead, and the text says nothing the owner has not agreed to.
- When a fixed engine reaches npm, a later plan re-pins it (the `ENGINE_PIN_PLAN.md` process: SRI hashes, the code
  cache, the speed budgets), re-measures, and raises the JS-only caps.

### 4.5 Not in this plan

- **A retry on the main thread** (Q5), **a summarised label that groups statements by verb** (Q3), and **carrying a
  patched engine** (Q6) are each a decision for the owner.
- **Breaking the run with a quote character**, which ends the engine's walk (§2.3): it would change what is drawn and
  how PlantUML splits a label into its first and second parts. Rejected.
- **A cap chosen by the reader's browser**: the source is written before any browser sees it.

## 5. Tests, red first

Each new fact is run against v4.14.4 in a worktree (tests copied in) and fails there for its own reason, read from
the failure message.

**R1, unit** (`ComponentDiagramGeneratorTests`, `PlantUmlStatementLengthTests`):
- `A_long_method_list_is_cut_to_the_label_cap_and_keeps_its_counts`: 66 ClickHouse statements, default options. The
  label as written is within `MaxComponentEdgeLabelChars`, ends `- 66 calls across 1 tests`, holds only whole entries,
  and holds `…`. Red: the label is about 1,945 characters.
- `A_label_within_the_cap_keeps_its_bytes`: the 10-statement edge equals 4.14.4's output. A pin, green on both.
- `A_formatters_label_is_cut_to_the_label_cap`: replaces `EdgeLabel_IsCappedAtTheStatementLimit` (6,000 `x`). Red.
- The S0 theory (`ManyMethods_KeepTheInternalFlowLinkClosed…`, both `useC4` values) also asserts the whole label is
  within the cap and both stats lines survive. Red if §3 F2's arithmetic holds.
- `Wrapping_StillHonoursTheStatementLimit` moves to the new cap; the constant gets its line in `:110-118`.
- `kronikol merge`'s panel: a merged report's component source has its edge within the cap
  (`MergeableReportRenderer` passes its own options).

**R1, Playwright** (`LongStatementRenderingTests`, `PollingInterval = 200`, `.First` on per-diagram selectors):
- `A_component_edge_with_many_statements_draws_in_the_worker`: a report built through the real run path with default
  options and 66 statements, no stats; the run report's panel and `ComponentDiagram.html`'s `#comp-diagram` each draw
  an SVG holding both participants' names and `66 calls across 1 tests`, and no stack or syntax picture; mode is
  `worker`. Red on 4.14.4 in the shared Chromium 147 (66 statements fail cold there).
- The same in a Chromium launched with `--js-flags=--no-opt --no-maglev`, as `:143-160` does. Red.

**R1, node** (`NodeJsPlantUmlRendererTests`, Integration): the capped 66-statement source draws. A pin.

**R2:**
- Playwright, `A_stack_overflow_picture_counts_as_a_failed_render`: a component source with a 10,000-character edge,
  handed to `GenerateHtmlReport` as `componentDiagramPlantUml` (a consumer's own source, which Kronikol does not cap),
  so it fails in every configuration. `__kronikolRender.errors` is 1, the cache holds no entry for it, and Kronikol's
  line names the edge's line and length. Red today (errors 0, cached). A sibling with `BrowserRenderWorkers = 0`.
- Unit, the detector over the three engines' messages and over a drawn diagram whose text quotes the phrase (a note
  holding "Maximum call stack size exceeded" as captured text is not a failure). Uses the `ShimFunction` helper in
  `DiagramContextMenuTests`.
- Node, Integration: the same 10,000-character source under NodeJs gives a `RenderFailure` diagnostic and the failure
  block, not an embedded picture. Red today.

**R3:** per capped kind, a unit cap fact and a Playwright fact at the cap in the optimizers-off Chromium, each red on
4.14.4: a 1,200-character coloured step bar (a one-token JSON step) and a `loop` label spliced in at 1,200 fail there
even in the shared Chromium with the JIT on; a 400-character service name and a 900-character span name fail with the
optimizers off. Two cut names keep two participants (the alias hash). The probe's strict verdict gets a fact of its
own: a `Syntax Error?` picture and a RangeError text are not drawn. And the corpus invariant with its unknown-kind
sibling (§4.3).

**Mutations, one per behaviour**, each shown to turn a fact red before the tag: the cap raised by half; the cut taken
from the label's end instead of its entries; the cap applied before wrapping; a formatter's label left uncapped; the
stats lines dropped; the detector removed; the stack picture cached; the error count left alone; the node check
removed. Run in a `git stash create` snapshot worktree, never by `git checkout` in the working tree.

## 6. Releases and records

### 6.1 Releases

| Release | Holds | Bump | Why |
|---|---|---|---|
| R1 | §4.1 | patch | A bug fix. The constant is internal; nothing is public. It changes report output (long labels are cut shorter), which is called out |
| R2 | §4.2 | patch | A bug fix: a failed render was counted as a success and cached. Kronikol's new line is the existing failure message extended to one more failure |
| R3 | §4.3 | patch | Bug fixes, as R1 |

R1 and R3 are recommended as one release, since both are caps from one measurement and R3's coloured bar fails in the
default configuration too; R2 follows (Q8). The changelog's
`[Unreleased]` section (the template README's documentation bullet) folds into R1. Before each tag:
`git log v4.14.4..origin/main` for commits touching these files, the new facts red on the previous tag, the core suite,
the full Playwright suite, `release.slnf` built in Release for every target, and BreakfastProvider's in-memory lane on
the local packages with its component source compared against 4.14.4's.

### 6.2 Changelog draft (R1)

> **Patch - a component diagram with a long edge draws in the render worker (#162, `plans/LONG_COMPONENT_EDGE_PLAN.md`
> R1).** A fix, so the patch part moved. The limit it adds, `PlantUmlStatementLimits.MaxComponentEdgeLabelChars`, is
> internal, so nothing new is public. It shortens what a long edge shows, and that is called out.
>
> ### Fixed
> - **A component diagram edge listing many operations replaced the whole diagram with the engine's stack-overflow
>   picture** in Chrome, in `ComponentDiagram.html` and the report's component panel. An edge lists each
>   distinct operation, so a suite writing to about 50 tables reached it with the JIT on, and about 20 with V8's
>   optimizing compilers off. The edge's label now has a measured cap of its own, N characters as written. A longer
>   list keeps its first entries, ends with `…`, and keeps its call and test counts; every call is still in its
>   scenario's sequence diagram. The cap borrowed from the sequence diagram's message limit was never measured for this
>   statement, and was more than three times too high. Until a release carries this: an adapter's `Verbosity = Summarised`, a
>   `RelationshipLabelFormatter` or `BrowserRenderWorkers = 0` avoid it.
> - `MaxLinkedLabelChars`' doc said the same text unlinked draws at every length to the message limit. That holds for
>   a sequence message only.

If R3 ships in the same release, its bullets follow under the same heading:

> - **A long step bar or spliced `loop` label could cost its sequence diagram on the first render in a tab.** A step
>   written as one unbroken token (a JSON array, say) past about 1,040 characters drew the engine's RangeError in place
>   of the diagram, and a `loop` label past about 1,010 a `Syntax Error?`, under caps of 1,400 and 1,471 that were
>   measured in node, whose stack is twice the render worker's. Both caps now come from the worker. Service names past
>   about 275 characters and span names in the internal-flow diagram past about 820 did the same with V8's optimizing
>   compilers off; they now have caps of their own, and two cut names stay two participants.

### 6.3 Wiki (a worktree of the wiki of its own; the local checkout is 24 commits behind)

- `Component-Diagrams.md:342` (the "Label length cap (3.0.48+)" paragraph) and `:206-214` (the stats link).
- `Large-Response-and-Diagram-Handling.md`, the statement-limit table (`:39-46`): a component-edge row; `:46` (the
  link row's "the page's main thread, node, Firefox and WebKit draw every length"), `:52` (the list of capped emitters).
- `PlantUML-Browser-Rendering.md:108` ("every component diagram draws in each of those configurations") and `:352`
  (only the coloured bar named as a stack failure); R2's telemetry change where `__kronikolRender` is documented.
- Grep the wiki for `Maximum call stack`, `2,000`, `unmeasured` and `WebKit` before calling it done.

### 6.4 Doc comments (they ship in the package: fixing one is part of the patch)

`PlantUmlStatementLimits`' class summary (a bullet and a table row for the component edge, R3's rows), `MaxBlockLabelChars`
and `MaxColouredNoteBarChars` (their figures are node's) and `MaxLinkedLabelChars` (`:124-136`); `ComponentDiagramGenerator`'s cap comment (`:244-252`) and `CapLinkText`'s summary;
test comments at `PlantUmlStatementLengthTests.cs:271-282`, `ComponentDiagramGeneratorTests.cs:473-475, 1275-1278` and
`ComponentDiagramLabelWidthTests.cs:175`.

### 6.5 Plans, roadmap and Kronikol4J

- `ENGINE_PIN_PLAN.md` §10.1 gets a dated note pointing here for F2; `LONG_LINE_SYNTAX_ERROR_PLAN.md` §6 Q2 is
  answered; `ROADMAP.md:632` moves out of "deliberately not scheduled"; leftovers (U, Q3, Q6) go to Appendix C.
- Kronikol4J's ledger (`docs/REMAINING_PARITY.md`) gets one entry per release, "Not mirrored": the port's edge has no
  wrap and no cap, and its page draws on the main thread, where the .NET page's engine fails from about 1,100
  characters with the optimizers off. Worth checking in the port with its own engine.
- #162 is closed with a comment naming the release, once it is published.

## 7. Where it sits in the roadmap

`ROADMAP.md:632` parks exactly this until a lost component diagram is reported. R1 is a small patch with no decision
in it beyond Q1 and Q2; R2 and R3 are hardening; U is the owner's.

## 8. Found on the way, not in this plan

- `MergeableReportRenderer` passes `useC4: false` and does not copy `DependencyColors` (`:43-45`), so a merged report's
  edges may be coloured differently from the run's. Whether that is meant is not recorded.
- `ComponentDiagramDiffer` (`:136, 157-173`) is public, has no caller in `src/`, and writes names neither wrapped nor
  capped.
- `GeneratePlantUml` writes `!theme` under BrowserJs too (`:153`); whether a themed component diagram draws in the worker
  belongs to `THEME_PLAN.md`'s row.

## 9. Questions, with recommendations

- **Q1. The marker for a cut list.** `…` alone, as every other cut, or `…, +38 more`? **Recommended: `+N more`.** A reader
  learns how much is missing for about twelve characters, and the count cannot be got from the diagram otherwise.
- **Q2. Which configuration sets the cap.** The worker with the optimizers off (about 375, ten statements) or the
  default configuration's warm worker (about 1,150)? The same choice sets R3's caps (about 600 against about 750 for
  openers and bars, 200 against about 380 for names). **Recommended: the optimizers off**, as 3.30.4 chose for links. A
  label of 24 display lines is not readable on one arrow anyway.
- **Q3. A summarised label** that groups by verb (`INSERT INTO (37 tables), DELETE FROM (3)`) would keep every table
  count in a short label. It is a design change to what the edge says. **Recommended: not in this plan**; a candidate for
  the v5 component diagram, which drops C4 (`V5_PLAN.md` 0b).
- **Q4. Report to TeaVM** (§4.4)? **Recommended: yes**, the root cause with a Java repro and the two prototypes offered,
  after the owner reads the text.
- **Q5. Retry a stack failure on the main thread?** **Recommended: no.** It doubles the cost, still fails with the
  optimizers off, and would hide the next unmeasured statement.
- **Q6. Carry a patched engine** (a TeaVM build of PlantUML with both fixes, hosted as the fork once was)? It lifts every
  JS-only limit now, against the `ENGINE_PIN_PLAN.md` decision to pin npm. **Recommended: no**, unless upstream declines.
- **Q7. One constant for every greedy run?** Link text and the component label cost the same per character. If R1's
  edge lands within a few percent of the link's, `MaxLinkedLabelChars` could serve both. **Recommended: keep two
  constants**, each pinned by its own measurement.
- **Q8. How to group the releases.** R1 alone ends #162 soonest; R1 with R3 fixes every lost diagram the measurement
  found, two of them in the default configuration, from one harness. **Recommended: R1 and R3 together, then R2.**

## 10. Assumption ledger

| Assumption | Status |
|---|---|
| The pin stays `@plantuml/core@1.2026.8` through R3 | READ (`TrackingDefaults.PlantUmlJsCdnBase`) |
| The label's length is the exact measure for the component edge | RUN in node; the worker's shapes agree (§2.2) |
| A Chromium worker has about half the main thread's stack | RUN on Windows only (1.96) |
| The issue's lower figures come from Linux or macOS | INFERRED from its `check.js`. **Not Linux:** Chromium 147 on Linux agrees with Windows to the character (RUN, 2026-10-10); macOS unmeasured |
| Firefox and WebKit draw every length to 2,000 | RUN on Windows; WebKit on its main thread only. **True of the edge only:** Firefox 148's worker fails names, openers, the coloured bar and actions from 500 to 1,250 characters, every one above R3's caps (RUN, 2026-10-10) |
| Edge behaves as Chrome | ESTIMATED (same engine; not measured) |
| No generated report passes stats | READ |
| Java PlantUML is not affected | RUN (OpenJDK 25, `-Xss256k`, 1.2026.8beta1 jar) |
| The emitter writes the coloured step bar only for a step with no break, and a long opener only from spliced source | RUN (§2.4) |
| A cold worker is the first render a reader's page makes | INFERRED (a fresh page starts its workers on load) |

## 11. Log

- **2026-10-09.** Drafted from #162 at 4.14.4. The claims were re-measured (§1, §2.2), the cause traced through the
  engine's own stack to TeaVM's regex port (§2.3), and the other statements Kronikol writes measured in node (§2.4).
- **2026-10-09, later.** §2.4 measured in the worker (642 cases, Chromium 147 and Chrome 154, a strict verdict): the
  block-opener and coloured-bar caps fail in a cold worker with the JIT on (from 1,010 and 1,040), participant names and
  activity actions fail with the optimizers off (from 275 and 820), and the stats edge at 3.30.4's cap draws (line 491;
  its edge is line 581, the unlinked edge's). 3.30.4's legacy verdicts were not sound (F7a). R3 widened from "re-check"
  to "lower two caps and add two", and Q8 now recommends R1 with R3.
- **2026-10-10. Green-lit in full; R1 and R3 executed as 4.14.5.** The measurement of §4.1 was made with Kronikol's own
  emitters (`emitter-corpus -- --component-edges` and `--statement-kinds`) and the strict verdict in every mode of
  `statement-limits-worker-probe.js`, in Chromium 147 and Chrome 154 on Windows, Chromium 147 on Linux, Firefox 148 and
  WebKit 26.4, cold, warm and with the optimizing compilers off
  (`tools/render-bench/results/statement-limits-worker-2026-10-10.txt`, not `component-edge-worker-<date>.txt` as §4.1
  named it, since it holds every kind). With the optimizing compilers off the three Chromium runs agree to the
  character: the edge's label as written fails from 550, a block opener from 840, the coloured bar from 880, a sequence
  participant's name from 280, a component node's from 310 to 340, an activity action from 820, and an activity
  swimlane from 540, a kind §2.4 did not list (`[^|]+`, drawn as the RangeError picture). A placeholder note draws to
  15,900. Caps: the edge 375, openers and the bar 600, names, aliases and swimlanes 200, actions 600, and the
  placeholder note held to the 16,000 note ceiling, which it had not been. The cut keeps whole entries and ends
  `…, +N more` (Q1): #162's 66 statements keep 10. A cut name's alias takes an FNV-1a hash of the whole name, so two
  names that share their first 200 characters stay two participants. The page's check of the failing line now takes
  every cap from C# (`__PLANTUML_STATEMENT_LIMITS__`) and names each capped kind. Found while executing, fixed in the
  same release: `ComponentDiagramDiffer` (§8) wrote its names with no cap; the coloured-bar fact in
  `NodeJsPlantUmlRendererTests` checked the styled bar, not the one-token one, so it could not fail; the
  name-wrapping fact used a 321-character name, now cut. Red first: copied onto 4.14.4, 22 core facts and 12
  Playwright facts fail (`LONG_COMPONENT_EDGE_PLAN.harness/r1r3/red/`); mutations in `r1r3/mutate_r1r3.py`, results
  beside it.
- **2026-10-10, later. R2 executed as 4.14.6.** The page's detector (`isStackOverflow`, exposed as
  `window._isStackOverflow`) reads an engine answer as a stack failure when it is text naming the overflow, or the error
  picture (first line `PlantUML `) whose last line names it; only the last line is read, so a syntax error's picture of
  a source that quotes the phrase is not one. On a worker's answer and after a main-thread render, the failure counts in
  `__kronikolRender.errors` and is not cached, and `describeEngineFailure` puts a line above the engine's picture (or in
  place of its text) naming the statement past its cap, or else the longest. Found while executing and fixed: the
  internal-flow popup's own check ran on a 100 ms timer, before a worker answers, so it never saw a failure; it now reads
  the answer when the engine writes it and calls the same description. `NodeJsPlantUmlRenderer` fails a diagram whose
  answer is the stack picture or text, naming the line the engine reported (`[From textarea (line N)]`) and its length,
  so a sequence diagram takes the placeholder path and a component diagram the failed panel and a `RenderFailure`
  diagnostic. Found on the way and not fixable here: a component edge whose label has no line breaks, once past the
  stack, is drawn with the label `0` and no error, in the worker and on the main thread (from no more than 2,600
  characters with the JIT on and 830 with the optimizing compilers off; `r2/failure-forms-browser.js`). The engine
  swallows the overflow and reads the line by another rule, so no detector can tell the result from a drawing. Kronikol's
  emitter wraps every long label and caps it at 375, so its own diagrams are not affected; the form is in the TeaVM
  report. The plan's §5 test of a 10,000-character edge would have met this form; the tests use the emitter's wrapped
  form instead, which fails as the picture. Red first on 4.14.5 (`r2/red/`: 7 core facts and 5 Playwright facts fail);
  mutations in `r2/mutate_r2.py`, results beside it.
- **2026-10-10, published, and the upstream half posted.** 4.14.5 (`0321192f`; Release run 38012982612, CI 38012981081,
  CodeQL 38012981101) and 4.14.6 (`98c4806d`; Release run 38015347936, CI 38015345821) passed, and nuget.org listed all
  62 ids at each. The wiki has R1 and R3's edits (`8c86217`) and R2's (`8a57576`), Kronikol4J's ledger a line for each
  release (`1a69119`, `7e9c7d5`), and #162 is closed with a comment naming both releases. U, the engine: the TeaVM
  change was built by a subagent and reviewed here. `TCodePointSet` is implemented by the range, dot and surrogate nodes,
  and `TCodePointQuantifierSet` and `TReluctantCodePointQuantifierSet` walk the same search tree as the recursive
  quantifiers in a loop. 193 of 193 regex and `String` tests pass on the JVM, JS and Wasm GC, and the 4 new long-input
  tests fail on master. A differential fuzz against master's classes found no difference in 13,265,280 cases. In node it
  runs at 0.33 to 1.21 times master's time. PlantUML built with it draws every statement kind past 30,000 characters,
  with 538 renders byte-identical. It is posted as konsoletyper/teavm#1295 (the issue) and #1296 (the pull request,
  commit `10acb854` on `lemonlion/teavm`, branch `regex-iterative-quantifiers`). The caps are raised in a later plan,
  once a published PlantUML build carries the fix and the engine is re-pinned (Appendix C).

## Appendix A. Edit sites at `9f2cd394`

| File | Lines | R | What |
|---|---|---|---|
| `src/Kronikol/PlantUml/PlantUmlStatementLimits.cs` | 41-97, 124-136 | R1, R3 | the new constant, the class summary, `MaxLinkedLabelChars`' doc |
| `src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs` | 217-258, 319-336 | R1 | the cut by entries, the cap, the comment, `CapLinkText`'s summary |
| `src/Kronikol/Reports/plantuml-browser-render-script.js` | 131-139, 219-223, 1018-1083 | R2 | the detector, no caching, the error count, the message |
| `src/Kronikol/Reports/plantuml-worker-host.js` | 232-267 | R2 | (if the worker reports the failure itself) |
| `src/Kronikol/Reports/internal-flow-popup-script.js` | 149-158 | R2 | the shared detector |
| `src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs` | 134-136, 186-188 | R2 | a stack picture is a failed render |
| `src/Kronikol/PlantUml/PlantUmlCreator.cs` | 1194-1208, 1332-1414, 2049-2053 | R3 | participant names |
| `src/Kronikol/InternalFlow/InternalFlowRenderer.cs` | 110, 116-120 | R3 | activity actions |
| `src/Kronikol/DefaultDiagramsFetcher.cs` | 77-81 | R3 | the placeholder's note |
| `tools/render-bench/emitter-corpus/Program.cs`, `statement-limits-worker-probe.js` | probe 52-54 | R1, R3 | `--component-edges`, `--scan-edges`; the legacy verdict |
| `tests/Kronikol.Tests/ComponentDiagram/ComponentDiagramGeneratorTests.cs` | 470-491, 1230-1241, 1270-1301 | R1 | §5 |
| `tests/Kronikol.Tests/PlantUml/PlantUmlStatementLengthTests.cs` | 110-118, 271-282, 478-506 | R1, R3 | §5 |
| `tests/Kronikol.Tests.EndToEnd/LongStatementRenderingTests.cs` | 100-180 | R1, R2 | §5 |
| `tests/Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs` | | R1, R2 | §5 |
