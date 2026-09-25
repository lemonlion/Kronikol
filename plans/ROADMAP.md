# Roadmap

**Written:** 2026-09-21 · **Repo version:** 3.25.2 tagged, 3.25.3 committed and 3.26.0 in the working tree while this was written · **Status:** a proposed order. It green-lights nothing. **Amended 2026-09-22:** mobile placed as stage 12b, decision D20 and `MOBILE_PLAN.md`; nothing else moved.

One ordered list of everything that is open, drawn from four sources: the 19 open issues, every
plan in `plans/` that is not finished, the loose ends `PLANS_STATUS.md` records, and the twelve
published artifact reports (Appendix D), of which "Where Kronikol Goes Next" is the one about
direction. Each stage says what it holds, what it waits for, what version part it moves, and why it
sits where it does.

It replaces the "Suggested order" at the foot of `OPEN_ISSUES_TRIAGE_2026-09-18.md`, whose content
is carried over whole in Appendix A. `PLANS_STATUS.md` stays the index of plan status, as `CLAUDE.md`
requires; Appendix B is a register of it as of today. This file is the order.

## How to read it

- **The stages are a serial order for one session working alone.** Section 5 says which stages
  touch disjoint files and can run side by side.
- **A stage is not a release.** The bump is given per item, by the rule in `CLAUDE.md`. No version
  numbers are assigned, because every plan that assigned them was overtaken (the MCP plan says
  "3.2.0", `QUERY_FALLBACK_PLAN` says "3.4.0").
- **Most plans here are not green-lit.** A stage whose plan is not green-lit starts with the owner's
  decisions, which section 3 collects in one queue. A recommendation in this file is a
  recommendation. It is not a decision.
- **How far each fact was checked** is marked where it matters: RUN (a command was executed today),
  READ (the source, plan or report was read today), PLAN (a plan or report says so and it was not
  re-checked), INFERRED (reasoned from two facts, stated by neither source).

## 0. The goal, and the bar

"Where Kronikol Goes Next" (2026-09-12, written at 3.0.86 from usage data, a competitive scan and a
read of the repo) ends on an assumption it asks the owner to confirm: that adoption is the goal. If
it is not, it says, "the order inverts".

**The owner's answer, 2026-09-21.** Adoption is the goal, but not at any cost:

- Nothing ships to a wide audience that comes across as amateurish. The aim is the wow factor.
- A visitor who finds their use case missing writes the product off and does not look again when the
  use case is later covered. So the first look has to find breadth already there.
- The target is a wide audience, met at a high bar in three things at once: **how it looks, how few
  defects it has, and how much it covers.**

That answer keeps the report's diagnosis and reverses its order. The diagnosis, none of it re-checked
today: each of versions 3.0.67 to 3.0.83 drew between 874 and 1,016 NuGet downloads whatever its age,
the signature of mirrors and scanners fetching each version once; the repository had 31 unique
visitors in 14 days, 17 stars in three years, and one referrer that was neither a search engine nor
GitHub (ChatGPT). "The product is not the problem. Almost nobody is arriving." The category is
unoccupied (nothing else in .NET auto-instruments about 25 client libraries, draws sequence diagrams
and ships one self-contained HTML file), AppMap now sells "runtime evidence for AI-assisted
development" with a .NET agent that is Linux-only and unlisted, and Reqnroll's own docs tell users to
build their report from the Cucumber Messages file.

The report put distribution second of five moves and the visual milestone fourth, "worth more once
there are users to notice it". The owner's answer turns that round: **there is one launch, and it
happens at the bar.** Few people arriving is, for now, an advantage: there is almost nobody to
disappoint before the bar is met. So this roadmap has a launch stage (13), everything the bar needs
is ordered ahead of it, and everything it does not need comes after.

### The bar, proposed

The owner named the three columns. The rows are this file's proposal (decision D19), and they are
what stages 0 to 12 exist to reach.

| | In the bar | Stage | Out, and why |
|---|---|---|---|
| **How it looks** | The two toolbar defects and the unstyled-toolbar defect (**done: 3.29.2**). Themes that work, including a dark page. The segmented toolbar, then the quiet bar and the whole-report redesign on `--kron-*` tokens: "a report that looks like a product rather than a test artifact". Mermaid CI summaries, which are what a reviewer sees in a pull request | 1.5, 6, 11, 12 | |
| **How few defects** | Every live defect known today. The report that invents an N+1 (#87). The nets that catch the next class of defect. No document that states a falsehood. A port that says what it is | 0, 1, 3, 5 | |
| **How much it covers** | Both forges first-class (GitHub and Azure DevOps). Real suite sizes: a 31.8 MB page and an 82.7 MB JSON are the opposite of a wow. The pull request loop: a comment, and a delta report. A rerun that does not destroy the evidence. Coverage in the report. Trends across runs on a page. The agent channel. No captured data sent to a third party. A mobile end-to-end recipe on every platform, pending D20 | 2, 4, 7, 8, 9.0 to 9.4, 10, 11.1, 12b | Durable reports in a bucket, alerting, the warehouse, a second language (D18), the query fallback, in-app capture for MAUI, Android and iOS (D20). None is something a first look tests |

## 1. The rules the order follows

Rules 1 to 8 are in priority order and would hold under any goal. Rule 9 is the owner's answer in
section 0. Every "why here" below cites one of them.

1. **What is shipped and wrong comes before what is new.** A live defect costs every user on every
   run, and a patch is the cheapest release there is.
2. **Finish what is nearly finished, and start less.** Work that is built and green but unmerged
   only gets more expensive. The direction report's capacity note applies with more force now than
   when it was written: "Two finished beat six open." It counted eight plan files in flight; there
   are 22 unfinished ones today.
3. **A net goes up before the work it protects.** A mechanism that catches a class of defect is
   worth most when it lands before the changes that could cause that defect.
4. **Nothing is sized on data known to be wrong.** A measurement taken over a defect is re-taken
   after the fix, and the decision waits for it.
5. **Work that edits the same files runs one after the other.** The history renderer, `VerbTable`,
   `stylesheets.css` and the toolbar emit sites each have several claimants.
6. **Anything that changes a format lands before that format is frozen or copied.** Three freezes
   are coming: the cross-language capture contract, the query conformance corpus, and keys in an
   object store. A change after a freeze costs a version of the format. A change before it is free.
7. **MAJOR is reserved, so v4 is prepared in 3.x and spent once.** A new option is a minor, flipping
   its default is a major. Everything v4 needs is built behind options first, so 4.0.0 is flips and
   deletions.
8. **Cost and certainty of value break ties.** Small and measured goes before large and argued.
   Work whose own plan says nobody has asked for it yet goes to section 6.
9. **There is one launch, and it happens at the bar.** Three consequences. Anything that actively
   brings people in (a Marketplace listing, a registry listing, a newsletter, a community post) waits
   for stage 13, however cheap it is. Anything the bar needs is ordered before stage 13 by rules 1
   to 8, and the longest chain to the bar starts as early as its prerequisites allow, because it
   sets the launch date. Anything the bar does not need goes after the launch.

Waiting on someone else (the upstream maintainer, an owner decision) never holds a stage up. It is
listed in section 3 or section 5 and the order moves past it.

## 2. What changed since the triage of 2026-09-18

The triage read 16 issues and checked nothing against the source. Since then:

| Fact | Basis | Effect on the order |
|---|---|---|
| The noise plan shipped in full (3.22.2 to 3.25.0). #75 and #83 are still open only because nothing was posted on them | READ, noise plan §11 | Triage plan 1 is done. Closing the issues is an owner decision (D1) |
| The analyzer cost plan shipped (3.25.2, #91 closed) and the as-of cut shipped (3.25.1, #95 closed) | READ | The evidence plan's findings F4, F5 and F5's twin are already fixed. Its S3 shrinks to the verb and flag work |
| The evidence plan's prototype patch no longer applies, and the S2, S3 and S4 tool prototypes it describes are in no committed file | RUN (`git apply --check` fails) | Those slices are built again by hand. The plan cites a §11.6 that does not exist |
| **Another session is executing the evidence plan in this tree, as three releases, while this file is written.** `4ee3842d` is 3.25.3 (S0 and S1: the merge sweep, the self-compared local report, the file sharing, and #82), committed and not yet tagged or pushed. The working tree holds 3.26.0 (S2 and S3: #84 and #81, with `--run`, `--sid` and `--window`). #80 to #84 are all still open on GitHub | RUN (`git log`, `git status`, `CHANGELOG.md`) | 1.1, 1.2 and 4.1 are done or in flight and are kept below as the record of why they came first. What is left of the plan is 4.2 (S4, #80). Nothing else in stage 1 touches those files |
| **#72 is built.** PR #73 (`pr-report-link-template`, 892 lines with 597 lines of tests) has been open since 2026-09-15 with every CI check green. It is `CONFLICTING` against `main` | RUN (`gh pr view 73`) | It moves from "plan 7" to "rebase and merge" |
| Three new issues: #93 and #94 (`InteractionRecord.FromLog` loses user PlantUML and `DurationMs`), #96 (the ledger read) | READ | #94 is one line. #93 adds a contract field. #96 splits into a patch and a format change |
| `@plantuml/core` 1.2026.8 is on npm (released 2026-09-05). #2840, #2862 and #2873 merged. Kronikol still pins the fork tag `v1.2026.8beta1-0e4f452` | RUN (`gh`, `npm view`) | The pin can move today. `THEME_PLAN`'s upstream gate is lifted. `PERF_CI_PLAN` is complete |
| `QUERY_FALLBACK_PLAN` M0 is done: the two `query.py` copies are byte-identical and `SkillDriftTests` holds them | RUN (`diff`) | Its `PLANS_STATUS` row is stale |
| The MCP plan's three "shipped-product bugs" are fixed (flag legality 3.1.0, address round trip 3.5.0 and 3.7.0). Its gate, the LLM-first envelope work, has passed | READ | MCP is blocked on the green light alone |
| `V4_PLAN` 4a says "no option exists today". `ReportToggleDefaults.HeadersShown` shipped in 3.0.80 | READ | 4a is a default flip plus a `--headers` CLI flag |
| `PLATFORM_FOUNDATIONS_PLAN` says the WASI build dies at "exactly two" hashing call sites. Cross-run history added three more (`HistoryModel.cs:97`, `:151`, `InteractionShape.cs:262`) | READ | The managed-hashing task is five sites |
| `PLANTUML_JS_PARITY_PLAN.md` stops at §3.6. §4 to §7 and appendix B are referenced throughout and exist **nowhere**: not in the file, not in git (it was committed truncated in `f3f7318d`), and not in artifact `6a67776f`, which is the evidence base and holds no issue list, waves or drafts | READ (the artifact was read in full) | They have to be written again before the plan can be green-lit |
| `templates/github-actions/` does not exist on `main`, and three separate plans want to put something in it | RUN | Part of why stage 2 exists |

**What the artifact reports add** that no plan or issue holds:

| From | Adds | Where |
|---|---|---|
| Where Kronikol Goes Next | The adoption question, now answered. A distribution ladder ordered by effort. "Decide the Java port, in public." A list of what not to fund. MCP as the one piece left of its first move. An acceptance test for the agent work that has never been run | §0, rule 9, D10, D14, stage 13, 10.1, 10.2 |
| PlantUML Theme Audit | Two defects that do not depend on themes: `<color:gray>` is `#808080` at 3.87 to 1, below AA, on the default note; hovering a link permanently blacks out its text (`plantuml-browser-render-script.js:1099`). "Silently ignoring [the option] is the worst of the three" | 1.6 |
| The Road to Server Parity | The report's engine fetch has no integrity check ("cheap hardening"). Client-side PNG answers the server endpoint v4 removes. A sequence-only engine tier would be 517 KB gzipped against 1.07 MB | 1.8, 11.1, §5 |
| Kronikol Render Bench | `BrowserFragmentMaxHeight` stayed at 12,000 px on a decision that predates the 4 to 8 times faster engine; 4,000 px measured best | 1.8 |
| Mermaid Payload Placement | The Mermaid switch is independent of v4's other three breaks and closes the plantuml.com data leak | 11.1 |
| Diagram Toolbar Redesign | Eight items the plan carries only by reference to a memory file | 6.2 |
| What PlantUML Cannot Paint, Mermaid Skin for PlantUML | `puml-theme-mermaid-v2.puml` exists and ships nowhere. What a theme cannot reach until capture stops emitting literal colours | 6.1, D16 |

## 3. The decision queue

Decisions cost the owner minutes and block weeks. They are listed apart from the stages so they can
be answered ahead of the work. **Blocks** names the first item that cannot start without the answer.
D0, whether adoption is the goal, was answered on 2026-09-21 and is section 0.

| # | Decision | Recommendation, and the reason | Blocks |
|---|---|---|---|
| D1 | Post on and close #75 and #83. Retitle or close #76. Hide the promotional comment on #79 | Close #75 and #83 with the release numbers. Retitle #76 to the CLI projection (its author's own correction). Four stale issues make the list lie about what is open. **Taken 2026-09-22 (stage 0):** #75 and #83 closed with the release per section; #76 retitled to the two things its author's correction left (the `scenarios --json` projection and Gherkin step locations), with a comment; #79 has no comment left to hide, the promoter's comment is gone from the API, presumably with the account | 0.1 |
| D2 | *Given in the other session for S0 to S3 (section 2). What is left is S4's choices.* Evidence plan: the green light for S3 and S4, the `KeepRuns` default (3 off CI, 0 on CI, same-id attempts kept on CI), whether `Amend` may rewrite a ledger line, whether a pass-on-retry keeps the failure's text | Take the plan's recommendations. `Amend` is the one to look at: it bends the append-only rule and gives a known wrong answer when matrix legs share one suite name and one file (plan §6.3) | 4.2 |
| D3 | *Resolved 2026-09-21 the way recommended: S2 ships in the minor 3.26.0 with S3.* Is evidence S2 a patch? It adds public members (`FlakyShortfall`, `nearMisses` in `--json`) | By the `CLAUDE.md` rule it is a **minor**. Ship S0 and S1 as the patch and S2 with the next minor | 1.2 |
| D4 | Is #93 a patch? The store plan says so. It adds a public property to `InteractionRecord` | By the rule it is a **minor**. #94 is a true patch and ships alone | 1.4 |
| D5 | Toolbar plan §5 question 6: is a minimal header wrap below about 1000 px acceptable as a bug fix | Yes. Without it the sideways scroll between 769 and 1000 px stays. **Taken 2026-09-24:** yes, from 769 px (written `768.02px`) to 1160 px, the breakpoint `TOOLBAR_AT_EVERY_WIDTH_PLAN.md` measured clean under the WCAG text-spacing override; shipped 3.29.2. The edge its execution found (text spacing with a classic scrollbar, 1161 to 1172 px) was fixed in 3.29.4 without moving the breakpoint | 1.5 |
| D6 | Move the engine pin to npm 1.2026.8 now, or wait for 1.2026.9. Keep `viz-global.js` or drop it | Move now and keep `viz-global.js`. The point of the move is leaving a personal fork tag for the published, immutable package route. 1.2026.9 has no date, and its trailing-zero trimming forces a second golden re-pin whichever way this goes | 1.8 |
| D7 | #85: compress payload fields in place (file stays JSON, 82.7 to 13.9 MB) or also gzip the whole file (6.1 MB, unreadable to `jq`, editors and validators) | Option 1, behind an option, default off until v4. A report that tools cannot open defeats the schema work of 3.4.0 | 8.1 |
| D8 | PR delta selection: the key that joins `kronikol changed` to a runnable filter, given that a new scenario has no earlier report | Needs a plan. The triage suggests source location or a runner trait in place of a recorded id | 7.2 |
| D9 | Dashboard plan §9: enable Pages on `lemonlion/Kronikol`, the three names, the default window of 200, and where the P1 addendum lives | Take the plan's names. Put P1 in an addendum to `DASHBOARD_PLAN.md`, as the store plan §8.0 recommends | 9.1 |
| D10 | **The Java port, in public.** The direction report calls today's state the worst available: rendering byte-complete, capture substantially incomplete, "and from outside it looks finished". Fund the capture side, or freeze it and say so. Behind it: is "full feature parity" dropped for Java and Node, does Kronikol4J 1.0 wait for the shared renderer, WASI or NativeAOT for the Java host | Say it now, in one README sentence ("renderer parity only, capture not ported" is the report's wording). A port that looks finished and is not is exactly the amateurish first look section 0 rules out. It costs nothing and forecloses nothing in stage 14. Decide the architecture after 14.1's experiment, which is what tells WASI and NativeAOT apart. **Taken 2026-09-22 (stage 0):** `Kronikol4J/README.md` opens with a status paragraph (rendering is a byte-proven port of .NET 3.0.43; what .NET added since is named as absent; in-process capture ported, no taps, no NDJSON writer, no ingest layer) and its byte-identity claim names 3.0.43. Not the report's wording, because "capture not ported" is no longer true: 36 modules of in-process adapters exist. The architecture question stays open | 0.6, 14.4 |
| D11 | The Kronikol4J divergence ledger has no entry between 3.0.62 and 3.9.0, none for 3.3 to 3.8, and none after 3.22.3. Backfill it or freeze it | Freeze the rendering half with one entry that says so, and keep ledgering capture changes. The report calls the ledger "a standing tax". If D10 lands on a shared renderer the rendering entries describe code that will be deleted | 6 |
| D12 | V4: look at the Mermaid step-summary spike (run 33308470844, PROBE 7 run 33309938739, branch `v4-mermaid-spike`). Revisit dropping C4 now that the stdlib loader #2873 has merged. Who owns the combined 4.0.0 scope (the V4 plan and toolbar Option C each say the other is "tracked elsewhere") | The eyeball is the only thing blocking V4 phase 1, and v4 now sits on the launch's critical path. Answer it early | 11.1 |
| D13 | MCP: the green light, the registry name (Q3), the download comparison window | Yes. The plan's own case is presence on a shelf, and the direction report names MCP as the one missing piece of its first-ranked move. Build before the launch, list at the launch | 10.1 |
| D14 | Upstream: open the seven hotspot patches as PRs before the maintainer answers the 2026-09-21 offer. `SMETANA_PARITY_PLAN` go or no-go with its Q1 to Q7. `PLANTUML_JS_PARITY_PLAN`: write the missing sections or park it. Post the 32-attribute style proposal, or let DOM-native CSS supersede its tiers A to C | The direction report says HOLD the upstream queue ("waiting is free, nudging a ready PR is a five-minute action, not a workstream"), STOP the parity dive ("the conclusion is the deliverable": Kronikol needs sequence, activity and component), and GATE further Teoz work on a number. With a launch bar to reach, follow it. Wait for the answer on #2834, which was asked for today | §5 |
| D15 | Delete `PERF_CI_PLAN.md` and `SMETANA_PERF_PLAN.md` under the "finished plans are deleted" convention | Yes to both. Both goals are met and merged upstream. **Taken 2026-09-22 (stage 0):** both deleted, `git show 158d62e5:plans/<name>` retrieves them | 0.2 |
| D16 | Ship a Mermaid-look theme in Kronikol. `puml-theme-mermaid-v2.puml` exists in `C:\Code\plantuml-plans\` and lands participant box, message pitch and lifeline spacing within a few pixels of Mermaid's | Decide inside 6.1. It needs capture to stop emitting literal colours first, which `THEME_PLAN` §2.6 already does | 6.1 |
| D17 | The Teoz gate's number: "does it change a real report's render time enough for anyone to notice? If not, it is craft rather than product" | Set it from 1.8's perf budget once the pin has moved | §5 |
| D18 | **Is the launch a .NET launch, or does it wait for a second language?** Section 0's worry is a visitor who finds their use case missing and never returns | A .NET launch. The risk is per audience: a Java or Node developer arrives by other doors (Maven Central, npm, the Kronikol4J repository), so a .NET launch spends nothing of theirs, provided the .NET front door claims nothing about Java (D10). Waiting would put the launch behind stage 14, the largest and least certain work in this file. Each later platform then gets its own launch at its own bar | 13 |
| D19 | **The bar's rows** (section 0). What must be true, in each of the three columns, before stage 13 | Edit the proposal. Every row moved out of the bar moves its stage behind the launch, and every row added moves the launch back | 13 |
| D20 | **Mobile: in the bar, and at which layer?** `MOBILE_PLAN.md` has three layers: M1 the device at the edge (a recipe, a HAR importer, three tests-record converters, a sample; no code in the app; every platform at once), M2 in-app capture for .NET MAUI (blocked on a capture-only core package, the same cut as 14.1), M3 and M4 native Android and iOS capture (platform ports, after the shared renderer) | **M1 in, M2 to M4 out.** A MAUI developer is a .NET developer arriving through the front door, and section 0's worry is the visitor who finds their case missing. M1 is the only layer a first look can test and the only one that costs days. It is not a second language, so D18 stands | 12b |
| D21 | **#97: pin the extensions' client references, or keep the float and say so.** The issue's three options: a fixed floor per package, with a CI lane that keeps the floors true; the float kept and documented on the integration pages; or central package management, which makes the floors one list either way | Option 1. It removes both failures the float has caused, a consumer's NU1605 and a release build that could not restore; option 2 removes neither. Option 3 can hold either, so it is a separate choice | 1.11 |

## 4. The order

### Stage 0. Close the loops

No code, no bump, a few hours. **Why first:** rule 2. Every later stage reads the issue list and
`PLANS_STATUS.md` to decide what to do, and both currently say things that are not true.

| # | Item | Detail |
|---|---|---|
| 0.1 | Issues | D1. Four of the 19 open issues need no engineering |
| 0.2 | `PLANS_STATUS.md` | Header still reads "updated for 3.0.69". Rows to correct: `PERF_CI` (says #2840 open, R5 blocked; both merged), `TEOZ_PERF` (remaining item is the pin move and an answer on #2834), `THEME` (upstream gate lifted), `V4` (4a premise stale), `QUERY_FALLBACK` (M0 done), `EVIDENCE` (F4 and F5 fixed by 3.25.1, patch does not apply). **`MCP_PLAN.md` has no row of its own.** Cross-cutting items 4, 6 and 7 are closed |
| 0.3 | `PLANTUML_JS_PARITY_PLAN.md` | Mark the file as a fragment. Its missing sections are in no source (section 2), so "recover" is not an option. Writing them is D14 |
| 0.4 | Docs that state falsehoods | README line 177 says a real suite's report is "10 MB" (measured: 82.7 MB, #85). Wiki `CI-Summary-Integration.md` documents behaviour that never existed. Wiki `PlantUML-Browser-Rendering.md` names an option that does not exist (`CiSummaryPlantUmlRendering`) and says themes work |
| 0.5 | Still-open cross-cutting items | CHANGELOG has no `[3.0.66]` section. `--note-format` has no parse test in `IngestCommandTests`. `ExamplesTableReportTests` lines 50, 162, 259, 297 assert a substring only the stylesheet emits. 108 space-form wiki anchors across 43 pages, unverified whether GitHub resolves them |
| 0.6 | The Java port's one sentence | D10. In `Kronikol4J/README.md`, which today opens by saying it "automatically captures real dependency interactions" |

**Done 2026-09-22, no release** (documentation, plans and tests only). What each item found once
checked, where it differed from the table:

- **0.1:** #75 and #83 closed with the release per section, #76 retitled, #79 has no comment to
  hide (D1).
- **0.2:** rows corrected, `MCP_PLAN.md` indexed, the two plans deleted (D15), the index's
  cross-cutting list rewritten with two items open (the Java ledger, now D11, and the dead
  `examples-*` CSS and script that 0.5's assertions had been matching).
- **0.3:** the fragment banner; the file's own §0 and §1 describe the missing sections.
- **0.4:** README says 10.7 and 82.7 MB. `CI-Summary-Integration.md` rewritten: the summary has
  only ever written PlantUML server URLs with the source alongside (`GenerateMarkdown` accepts a
  local renderer and never reads it), and the two-version split appears whenever the compact form
  differs, which headers alone cause; its Node.js section, example output and configuration row
  described a summary that never existed. `PlantUML-Browser-Rendering.md`: themes have no effect
  under `BrowserJs` (the theme text never reaches the worker) and the phantom option is gone; the
  same size figures in three more wiki pages.
- **0.5:** `[3.0.66]` restored (lost in the 3.0.67 commit) and `087be03` recorded under 3.0.67;
  the `--note-format` test; eight vacuous assertions, not four; the 108 anchors, measured on the
  rendered wiki: GitHub keeps a fragment as written and reads `[[a|b]]` as label then target, so
  **none of them resolved** and five more links were reversed. 164 links rewritten across 57
  pages with a checker verified against five rendered pages (`tools/wiki-links/`), 16 dead
  `[[Integration: X Extension]]` page links repointed, 4 anchors to renamed sections repointed by
  hand, 2 typos fixed.
- **0.6:** a status paragraph rather than the report's sentence, because "capture not ported" is
  no longer true (D10).

### Stage 1. The patch train

Live defects in shipped code. Every item is a **patch** unless marked, each is small, and none waits
for a plan to be green-lit. **Why here:** rule 1. **Why in this internal order:** items 1.1 and 1.2
were already in flight (1.1 and the #82 half of 1.2 are 3.25.3; the #84 half is in 3.26.0), 1.3 is one line, and the rest are ordered by how many users meet the defect.

| # | Item | Source | Notes |
|---|---|---|---|
| 1.1 | **Evidence S0.** `kronikol merge <reports-dir>` exits 1 on the `History.run.json` every history-enabled run has written since 3.9.0 (F11). A local report read without its fragment gets a run id salted with the tool's directory and is compared with itself (F14). `kronikol query` opens the report without delete-sharing, so a finishing run cannot replace it on Windows, and a replaced report is read at old offsets (F12) | Evidence plan §2, §11.4, §11.5. READ at HEAD: all three still present | **Shipped 3.25.3** (`EVIDENCE_SURVIVES_A_RERUN_PLAN.md` S0, in flight in another session when this row was written). A directory merge skips the two files Kronikol writes beside a report. A local report adopts its own ledger line (F14, redone without `HistoryPoint.CallSet`). `query` opens the report sharing write and delete, and refuses one that changed while it was read |
| 1.2 | **Evidence S1 (#82)** the single-scenario view prints the stored error whole, every ellipsis names an address. **S2 (#84)** an empty filter says what the window holds and why a near-miss missed | Evidence plan §3, §4 | S1 rebases onto the row format the noise plan's S4 shipped. S2's bump is D3. The 394-run replay showed the reason for every one of 174 flaky near-misses is "one failing episode", never `--min-runs`, so #84's own explanation is not the one to print. **Shipped:** S1 (#82) in 3.25.3, S2 (#84) in 3.26.0 (`EVIDENCE_SURVIVES_A_RERUN_PLAN.md`); both issues closed 2026-09-23 |
| 1.3 | **#94** `FromLog` never sets `DurationMs`. One line, one assertion in the existing round-trip test. **Shipped 3.27.4** (`INGEST_FEED_PLAN.md` S1) | READ: `InteractionRecord.cs:164` | Reachable today through `NdjsonInteractionWriter`, the documented feed for `kronikol ingest` |
| 1.4 | **#93** `InteractionRecord` gains `plantUml` so `Custom` and `Row` markers survive. With it, the store plan's R4: a test that diffs `RequestResponseLog` against `InteractionRecord` member by member (it will also name `Error`, `AttributionSource`, `ExpiredFromTestId`, `FocusFields`, `NoteOnRight` and the phase variants). **Shipped 3.29.0** as `kind: marker` + `markerKind`/`plantUml`/`markerEnd`, one record per override half (every kind, not only `Custom` and `Row`: `INGEST_FEED_PLAN.md` F2); the member diff shipped in 3.27.4 (`RequestResponseLogRoundTripTests`, seven gaps pinned for 14.1) | #93, store plan §13 R4 | **Minor** by the rule (D4). The member diff is the cheap forerunner of 14.1's round-trip gate |
| 1.5 | **Toolbar §2.1.** Export labels wrap from 1200 px down (`.export-btn` has no `white-space: nowrap`, READ at `stylesheets.css:581`). Sideways scroll between about 769 and 1000 px (measured 1090 px of scroll width in a 900 px window). `.diagram-toggle` base styles live in the internal-flow popup stylesheet, so the toolbar is unstyled without internal-flow tracking (F5). Violet has no active-hover rule (F6) | Toolbar plan §1, §2.1, §2.7. The artifact: these "land first as their own release" | Needs D5. Make the 320 to 1400 px sweep a permanent E2E guard. `stylesheets.css` is byte-shared with Kronikol4J: one ledger entry. **Shipped 3.29.2** (`TOOLBAR_AT_EVERY_WIDTH_PLAN.md`), with what the plan found on the way: a full scenario toolbar clipped out of sight by `content-visibility: auto`, the top bar wrapping its labels with the default component diagram, a long branch scrolling the page, the custom sheet emitted before the component sheets, and `InternalFlowPopupCustomStyleSheet` never applied. The guard is `ViewportSweepTests`; one ledger entry. **Audited 2026-09-25, follow-ups as 3.29.4**: a long token outside the features (failure clusters, History, a dependency chip) scrolled the page sideways, the search help's table ran out of its panel under text spacing, the export buttons left the in-row box under text spacing |
| 1.6 | **Two diagram defects from the theme audit that do not depend on themes.** `<color:gray>` renders `#808080` at 3.87 to 1 on the default note, below AA: replace it with a computed value ("worth doing whether or not themes ever ship"). Hovering a link permanently blacks out its text, because the restore writes `#000000`. And make `PlantUmlTheme` honest: a theme set under BrowserJs is silently ignored, so say so in a diagnostic until 6.1 makes it work (true on the pin the audit measured; from 3.0.76 the same setting left the report without diagrams, `DIAGRAM_COLOURS_PLAN.md` F21). **First release shipped 3.29.6** (`DIAGRAM_COLOURS_PLAN.md` S3a, S4, S5: the renderers answer the engine's script loads at once, captured text escapes the icon, emoji and sprite syntax, the Node renderer writes XML); the two colours and the diagnostic (S1, S2, S3b) follow as 3.30.0 | PlantUML Theme Audit. PLAN: neither was re-checked at HEAD | `THEME_PLAN` §2.3 bundles the contrast fix into the theme work; it stands alone |
| 1.7 | Two CLI defects found in passing. `--render local` throws (V4 plan, P3 CLI bullet). Real `kronikol ingest` wrote 0-byte `Specifications.html` and `.yml` (foundations plan §11, recorded as probable and never fixed). **Shipped 3.27.4**: the first refused as a usage error; the second verified and **not a defect** (`INGEST_FEED_PLAN.md` F4: the blank-on-failed-run rule), so the command now says why the files are blank | PLAN | Verify the second before fixing it |
| 1.8 | **Move the engine pin** from the fork tag to `@plantuml/core@1.2026.8` through jsDelivr's `/npm/` route, which is versioned and immutable. Touches `TrackingDefaults`, the Node renderer cache and the worker host. **In the same change, a known-hash check on the engine fetch** in the worker host and the Node cache: today the report trusts the CDN tag. Add the perf-budget assertion `TEOZ_PERF_PLAN` promised after the upgrade. Then re-measure `BrowserFragmentMaxHeight`: 12,000 px was kept on a decision that predates this engine, and 4,000 px measured best (toggle 0.50 to 0.54 s) at the cost of more seams | RUN: npm has 1.2026.8. READ: `TrackingDefaults.cs:28`. The Road to Server Parity, part seven. Kronikol Render Bench | Needs D6. Golden re-pins and a ledger entry only if SVG bytes differ from `0e4f452`. The fragment height is a measurement here and nothing more: changing a default so that existing reports render differently is the kind of change `CLAUDE.md` reserves for a major, so a new value waits for 12.1 |
| 1.9 | **#89** gzip and base64 `__iflowSegments` as one blob, decompressed on the first popup. 31.8 to about 13.1 MB on the ClickHouse lane | #89. READ: still plain JSON at `InternalFlowHtmlGenerator.cs:41` | Performance work, so a patch. It changes report output: one ledger entry. **Do not apply the one-blob reasoning to `puml-data`**, which is read by key and must stay compressed per diagram (#88). It goes here and not with #86 because it changes neither keying nor rendering, so #87 cannot invalidate it |
| 1.10 | **The parameterized tables the published reports still clip** (proposed by `TOOLBAR_AT_EVERY_WIDTH_PLAN.md` Q7, placement not decided). A grouped `.param-test-table` wider than its scenario is cut off at 780 to 860 px, and the LightBDD row detail panels at 320 to 760 px, by the same `content-visibility` clipping 3.29.2 fixed for the toolbar; the run summary table overflows 19 px at 320 px under the WCAG text-spacing override. Found while executing: the base sheet's row hover and active tints never paint on a real row, because every row carries a status class whose later rule of equal specificity wins | `TOOLBAR_AT_EVERY_WIDTH_PLAN.md` §2.13, §10 Q7, §9 | The plan recommends its own patch, or 6.2. A sweep assertion that no `.scenario` or `.feature` holds content wider than itself would have caught the toolbar case too. **Shipped 3.29.3** (plan §9, second part): every parameter table in a scrolling wrapper; the detail panels' defect was step text holding a type name, and ordinary sub-steps lost text the same way, so a long token now breaks in any text a feature holds; step and combined tables, server-rendered diagrams and their source, the features summary and the run summary scroll; the doc string's rule, dropped by stray stylesheet text, restored; attachment images fit a phone; each row status darkens under the pointer and when selected. The sweep lays every scenario out and measures text as well as elements. Found and left to D5: under text spacing with a classic scrollbar the export cluster runs up to 11 px past its box at 1161 to 1172 px; fixed in 3.29.4 without moving the breakpoint (the one button wider than the box wraps its label) |
| 1.11 | **#97** The extension packages float their client library (`Azure.Messaging.ServiceBus` `7.*`, `Grpc.Net.Client` `2.*`: 29 references across 23 packages, with no central package list). NuGet resolves a float when the package is built, so each release raises the version a consumer must have (BreakfastProvider's pin of `Grpc.Net.Client` 2.83.0 failed with NU1605 against 3.27.1). A client release can also break Kronikol's own build: it stopped 3.29.4's release build at restore, and 3.29.5 raised only the pinned reference the float collided with | #97; 3.29.5's changelog. READ on `origin/main` at `53b9f6c8`: 29 floating references in `src/*.csproj` | Needs D21. Placed by rule 1: a live defect that has already stopped a consumer's restore and a release. Every option is a patch or documentation only. Added 2026-09-25 |

### Stage 2. The CI templates, and a tidy doorstep

Nothing here is in a package, so **no bump**. **Why here:** rule 2 for 2.2, rule 8 for the rest, and
both forges first-class is a row of the bar. The store plan's own review (§13 R16) found that the
highest-value near-term work in it sits outside its slices: it is 2.3 and 2.4. No file here is
touched by any other stage, so all of it can run beside anything. **None of it is outreach** (rule
9): a template in the repository and a filled-in description bring nobody in. They serve whoever
arrives anyway. The listing, the README relaunch and the posts are stage 13.

| # | Item | Effort | Detail |
|---|---|---|---|
| 2.1 | **The doorstep.** The repository's homepage field is empty (RUN): point it at the live BreakfastProvider report. Rewrite the description from mechanism to outcome; the direction report's draft is "see what your integration test actually did: every HTTP call, SQL query and message, as a sequence diagram" | about an hour | Passive: it changes what the fortnight's 31 visitors see and invites no more. Outward-facing text: the owner's hand, and no em-dashes or other tells |
| 2.2 | **Rebase and merge PR #73 (#72).** One PR comment, one line per report artifact, kept current by every run | hours | Green since 2026-09-15. Conflicts are in `CHANGELOG.md` and `README.md` and grow with each release |
| 2.3 | **A history composite action** in 2.2's directory, shape and test pattern. Today cross-run history on CI costs about 45 lines of copied YAML (orphan branch, worktrees, fetch and rebase retry). The action makes it three | days | Store plan §6.1 and §11 Q7, which order it ahead of alerting. It reuses 2.2's 597 lines of test scaffolding |
| 2.4 | **Azure DevOps at the same level.** A Tier 0 recipe for Azure Pipelines (with the build-identity Contribute permission it needs) and a pipeline template as the sibling of 2.3 | days | Store plan §6.3: the likely buyer of a .NET product runs Azure DevOps on Azure with Entra ID and is today the least served. Owner's requirement of 2026-09-20: GitHub and Azure DevOps both first-class. The library already detects ADO (`TF_BUILD`). The plan's platform claims are REFERENCE and need half a day of checking first |

### Stage 3. The nets

`VERIFICATION_MECHANISMS_PLAN`. Test files and `ci.yml` only, **no bump**. Its stated gate (LLM-first
M0 to M2) cleared at 3.8.0. **Why here:** rule 3. Every stage from here on changes what a report, a
digest or the tool prints. Stage 1 went first only because those defects are live now. A bar on
defects is reached by catching classes of them, not instances.

| # | Item | Why in this order |
|---|---|---|
| 3.1 | **M2, collect coverage.** The coverlet collector is already installed on 60 projects and no workflow passes `--collect`. The deliverable is a list of paths no test exercises | Cheapest of the three, and it aims at this repo's recurring defect class: a path gated on input no corpus holds (`rule` and `categories` at 0 of 1,349 scenarios). Its cobertura files are also what 8.2's coverage panel needs to dogfood on |
| 3.2 | **M1, five cross-artifact invariants.** Every printed address resolves, resolves to the right error, agrees with its `stableId`, every `#sid-` link lands, digest counts match the report. Run the plan's §9 falsifier first: revert the comparer fix of `338231b` and see invariant 3 go red | Before stage 4, whose S1 promises "every ellipsis names an address" and whose S4 moves reports into `runs/<id>/`. `AddressRoundTripTests` (3.5.0) covers only that the tool accepts its own addresses |
| 3.3 | **M3, regenerate and diff the 59 Kronikol4J goldens from the .NET side.** No regeneration script exists | Before stage 6, which moves bytes in `stylesheets.css` and the diagram prefix. Today such a change surfaces only later, as port breakage. Trap recorded in the plan: the unit-test outputs are not the parity fixtures. A limit to record beside it, from PlantUML Without a Server: browser geometry is per viewer (0 of 13 geometry matches), so report SVG cannot be golden-pinned across machines |

### Stage 4. Evidence survives a re-run: S3 and S4

Two **minors**. Needs D2. **4.1 is in the working tree as 3.26.0; 4.2 is what is left.** **Why here:**
rules 2 and 5. S3 and S4 edit the same files as 1.1 and 1.2, so they follow directly while that code is fresh, and nothing else should hold
`VerbTable` open meanwhile. **Why before stages 9 and 10:** rule 6, see the notes.

| # | Item | Notes |
|---|---|---|
| 4.1 | **S3 (#81)** `query history` without a report: `--run`, `--sid`, `--window` | Smaller than planned. The analyzer cut and the window fix it carried shipped as 3.25.1; its tests 2, 5, 10 and 14 should already be green (not run). A new flag goes in `VerbTable`, `KnownFlags`, the verb's legal list and both `commands.md` copies. #81's "exits 0 on a usage error" did not reproduce (exit 2, 0 only behind a pipe); pin the 2 |
| 4.2 | **S4 (#80)** keep the last N runs under `Reports/runs/<run>/` by rotating the previous run, never prune a failing run, a per-run `Run.json` manifest, `--run` on every verb | The largest slice (23 tests). Two findings change the design. On CI a run id is shared by every step of a workflow run, so it cannot name a retained directory. A retry extension re-runs inside one run id, so attempts overlay by `at` into the format's existing `attempts` field; folding one as a shard gives the scenario a phantom second slot (F15). Rotation is staged and published by one directory rename (367 kills, 0 bad states; ext4 and overlayfs closed) |

INFERRED, stated by neither plan: once `runs/` exists, the MCP plan's `find_reports` walk must skip
it or it lists every retained run as a separate report, and the store plan's P2 should link a run by
the same address `--run` takes. Both are reasons this stage precedes stages 9 and 10.

### Stage 5. Internal flow: the truth, then the size

Needs a plan. None exists for #87 and the triage checked nothing against the source.

| # | Item | Bump | Notes |
|---|---|---|---|
| 5.1 | **#87** popups pool the spans of concurrent requests. One shows 547 spans and 60 root requests, which manufactured an N+1 finding that did not exist. The issue's suspected cause is grouping by time window where it should group by trace or activity id | patch | **Why this high:** rule 1. A report that shows a defect the system does not have is worse than a large one |
| 5.2 | Re-measure #86's duplication figures on reports written after 5.1 | none | Rule 4. #86's numbers came from reports holding 63 multi-request segments, and the issue itself asks for confirmation |
| 5.3 | **#86** store each distinct flow once and reference it by key, if 5.2 still supports it | patch | After #89 it adds only 10 to 25% on file size. Its case is heap, parse time and the growth curve (size goes with about the square of spans per request) |

**Why before stage 14:** rule 6. The foundations plan's F5 wants a third capture stream,
`spans.ndjson`, because `kronikol ingest` cannot take internal-flow spans today
(`IngestPipeline.cs:285`). Designed before 5.1, that contract would encode the pooling.

### Stage 6. Report appearance in 3.x

**Why here, five stages earlier than the direction report would put it:** rule 9. "How it looks" is
a column of the bar, and this stage opens the longest chain to the launch: Option B has to finish
before the token layer (11.2), which has to precede Option C at 4.0.0 (12.1), and each of B's steps
is its own release. Started late, this chain alone sets the launch date. Both plans move bytes in
`stylesheets.css` and the diagram prefix, so both come after 3.3, and D11 should be settled first so
each release does not also cost a Java mirror (toolbar plan §5 question 4). **Why `THEME` first:**
`PlantUmlTheme` is a public option that does nothing in the default render mode, which is nearer a
defect than a feature; 1.6 only makes it say so.

| # | Item | Bump | Notes |
|---|---|---|---|
| 6.1 | **`THEME_PLAN` P0 to P6.** All 43 bundled themes in BrowserJs, note palettes derived per theme offline, an acceptance harness | one minor | P0 is test-only and can start any time. P1 to P3 are not useful alone. Appendix B is re-verified against the pin of 1.8 first. The plan reverses the audit's central trade: the audit pinned notes so they stayed Kronikol's, the plan lets notes follow the theme. From the two theme reports, into the harness: the six engine truths (the last matching rule wins, scope wrappers silently kill half the selectors, one property per line or the rule dies) and the junction check (a sweep of forty variants measured objects and missed lifelines sitting 26 px adrift of their boxes; the fix is `Margin 0 25`). `rnote` at the emission site where square notes are wanted, and participant padding's second number as the one per-project knob. D16. §6 Q4, a dark diagram on a light report page, is Option C's territory and is decided with 12.1. Appendix A's defects are Local-only and vanish at v4, so they are not patched. **Two collisions to settle up front:** §2.6 removes the inline pass and fail colours the V4 Mermaid mapping was going to key on (it must key on stereotypes or structured fields), and §2.7 introduces a CSS variable where the toolbar plan reserves `--kron-*` as the v4 contract |
| 6.2 | **Toolbar Option B,** §2.2 to §2.6, each step its own revertible release: the segmented skin, tri-state tags, dependencies and categories with Happy Paths retired, then `dep:`, quoted names and hash v2 | three minors | Fixes two defects whose fix is a new control: the dependency filter hides all 194 dependency-free items of 341 once any chip is active, and `@` names stop at whitespace. **§2.2 goes before V4's 4a** (rule 5): both rewrite the Headers chip emit sites and the `[data-shown='true']` tests, which should migrate once. **Before executing, fold into the plan what it carries only by reference to a memory file** (READ, the artifact): F8's touch-target half (the 36 px bump at 480 px skips `.details-radio-btn` and `.diagram-toggle-btn`, which stay about 25 px), the span-count warning as an amber badge, a system font stack, F4, F7 and F11 by name, the report top bar's phone treatment, the measured optical corrections, the rule that group separation "must never depend on the flex spacer alone", and the measured-differences table as a regression fixture |

### Stage 7. PR delta selection

Needs a plan and D8. The largest unplanned piece of work in the list. **Why here:** it holds the
triage's position relative to everything the triage ranked, and it is a row of the bar: the direction
report calls a pull request comment carrying "the diagram for the scenarios that changed" the only
distribution item that compounds, and 7.4 is what produces those scenarios. It comes after stage 4
by rule 5 (two new verbs through `VerbTable`) and before stage 14 by rule 6: #77 adds a field to
every scenario record from every adapter, and the capture contract should freeze with it.

| # | Item | Bump | Notes |
|---|---|---|---|
| 7.1 | **What is left of #76:** `kronikol query scenarios --json` projects `sourceFile` and `sourceLine`. Step-level locations are empty on a Gherkin suite (0 of 2,297) | minor | Shippable now, alone. It is the first thing any join key will read |
| 7.2 | The plan, with the join key designed first | none | `kronikol changed` works from git refs with no build and no report. `kronikol filter --from TestRunReport.json` needs a report holding each scenario's `TestCaseId`. **A new scenario has no earlier report, and in #78's own example 21 of 22 changed scenarios were new.** Neither issue states this, and it is why they are one plan |
| 7.3 | **#77** `TestCaseId` and `TestCaseIdKind` per scenario, and `kronikol filter` emitting each runner's grammar | minor | Touches every adapter package |
| 7.4 | **#78** `kronikol changed`, with its eleven-check conformance suite and 30-PR replay | minor | Carry one thing over from #79: the delta report's wording must tell "no scenario text changed" from "nothing in this PR affects the suite". Then add its line, and the changed scenarios' diagrams, to the comment of 2.2 |

### Stage 8. Report size and the coverage panel

| # | Item | Bump | Notes |
|---|---|---|---|
| 8.1 | **#85** compress `TestRunReport.json`'s payload fields (`{"$z": …}`), a schema change (`$defs/compressed`, a root `payloadEncoding`) that moves with `formatVersion`, behind an option | minor | Needs D7. `ReportIndex` seeks by byte offset, so it is a different problem from #89 and not a copy of it. The default flips at v4 (12.1) |
| 8.2 | **#90** run-level code coverage from any cobertura file, run-length encoded (16 KB for full per-line detail) | minor | The design constraint is that coverage does not exist at `[AfterTestRun]` time, so it needs an MSBuild target or a CLI step that merges into an already-written report. It must render "not collected", never 0%, and respect `HistoryPartialRun`. It reuses 1.9's decompress-on-first-open convention and dogfoods on 3.1's cobertura files |

**Why here and not later:** rule 6, twice. #85 changes what `kronikol query` reads, so it lands
before the query conformance corpus of 14.3 is pinned. It also takes ingest from 82.7 MB down, which
the store plan §5.2 wants before anything stores reports (9.5). **Why not earlier:** it needs a
decision and a schema version, and the triage ranked it after the PR delta work.

### Stage 9. History over time

Needs D9. Follows the order of work the owner fixed on 2026-09-21 (store plan §8.0): trends first,
durable reports second, the warehouse only on request. That order also keeps the direction report's
one hard line about this area: "Do not build a hosted dashboard: that trades away self-contained
HTML, no infrastructure for a fight with funded SaaS." A static page over the history branch is
inside that line. The warehouse is the part in tension with it, and it is in section 6. Prerequisites
have all shipped: the noise plan (nothing should display verdicts before it), #91 and #95. **Why
here:** trends on a page are what Allure TestOps, ReportPortal, Trunk and BuildPulse demo, so 9.0 to
9.4 are in the bar; rule 5 puts them after stages 4 and 7, which hold the same tool files. 9.5 and
9.6 are not something a first look tests, and may follow the launch.

| # | Item | Bump | Notes |
|---|---|---|---|
| 9.0 | Refresh `DASHBOARD_PLAN.md` before executing it | none | It predates 3.16 to 3.25.2. Its `analysis` block lists five knobs and `shapeVersion: 3`. There are twelve options now, the version is 4, and since 3.25.0 comparability keys on the pair of version and `shapeRules` hash. 3.25.2 removed `HistoryPoint.CallSet` |
| 9.1 | **M0, the view.** `history.view.json`, `history view`, `history record --view`, a closed-contract schema test | minor | Shippable alone. Size guard 130 KB gzipped |
| 9.2 | **#96, the part that needs no format change.** `HistoryJson.ParseRun` builds every array with a LINQ chain and allocates 94 MB for 13 MB of lines. Sized loops: 132 to 69 ms, 94 to 44 MB, arrays identical. Held by a count (bytes per kept line), never a timing | patch | **Why here:** the view is the first thing that reads whole ledgers routinely, across repositories. Before it, by the issue's own word, nobody is hurt |
| 9.3 | **M1, the page.** Snapshot mode, one repository, seven panels, opened from `file://` in Playwright | minor | Uses the `--kron-*` token names from the first byte so 12.1 does not rename them. It is a new page, so it is built to the bar's look from the start |
| 9.4 | **M2 plus P1.** Many repositories, live refresh, the workflow template (into stage 2's directory), and P1: the retention line lifted, the view written as monthly files on the existing history branch | minor | No bucket, no credential. On Azure DevOps the workflow copies the files beside the page, because ADO has neither Pages nor anonymous raw access |
| 9.5 | **P2, any past run's evidence.** The report CI already emits, uploaded to the team's own bucket, gzip at rest, under a key carrying branch class and run id, linked from the run line | minor | The branch class must be in the key from the first write, because keys are immutable (rule 6). After stage 4 so the link is the address `--run` takes. May follow the launch |
| 9.6 | **Alerting.** Structured per-verdict events a workflow step delivers | minor | The store plan orders it after the composite action (2.3). It is `history gate` plus the #72 pattern, half built already. May follow the launch |

### Stage 10. The agent channel

| # | Item | Bump | Notes |
|---|---|---|---|
| 10.1 | **`MCP_PLAN` N0 to N2.** A read-only local stdio server as `kronikol mcp`, packaged under both the `DotnetTool` and `McpServer` package types | minor | Needs D13. Re-measure first: the plan says 18 verbs and 7 JSON verbs, there are 9 JSON verbs now, and N1-1 can key on `VerbTable`. It adds `ModelContextProtocol` 2.2.0, the tool's first direct third-party dependency (11.0% on the package). **N3, the registry publish, is outreach and moves to 13.4** |
| 10.2 | The direction report's acceptance test for its whole first move, never run: an agent "given only the repo, finds the run, reads the failure, and names the interaction that broke, without being told Kronikol exists" | none | It is a test of 3.1.0 to 3.8.0 as much as of 10.1. Run it cold, before and after. It is also a gate of 13.0 |

**Why here:** rule 2. The direction report ranks finishing the agent-facing work first of its five
moves, because it was half built and "the LLM angle is what makes the next item worth reading".
Everything in that move has shipped except MCP. **Why not earlier:** a long-lived server that keeps
reports open makes 1.1's Windows file lock more likely, its `find_reports` has to know 4.2's layout,
and rule 5 keeps it from running beside stages 4, 7 and 9. It is last of the tool's stages because it
wraps the verbs the others add.

### Stage 11. Version 4, prepared in 3.x

`V4_PLAN` and toolbar Option C. **Why here:** rule 7. The direction report's advice is the same from
the other side: "Bundle the breaking changes into one release rather than bleeding them out."

| # | Item | Bump | Notes |
|---|---|---|---|
| 11.1 | **Pre-stage.** `MermaidCiSequenceCreator` and the single-pass `CiSummaryGenerator` behind a new CI-summary format option defaulting to PlantUML. `--headers` on `kronikol ingest`. Client-side PNG (a canvas wrapper in the browser, resvg or sharp under Node) to answer the server `/png/` path phase 3 removes. `[Obsolete]` on every member phase 3 deletes, a warning on `--render server\|local`, a final deprecated IKVM package | minors | Needs D12. INFERRED from the semver rule: the V4 plan itself ships all phases together. **No member phase 3 deletes carries `[Obsolete]` today** (READ), and a major that removes API with no deprecation release behind it is the thing to avoid. Keep status and outcome on response arrows (`201 requires_capture`, `402 card_declined`): the payload-placement report found it is what makes a failure stand out. This also closes the coverage gap on `GetCiSummaryDiagrams`, which has no direct tests. **The Mermaid option may be pulled forward to any point after stage 3.** It depends on nothing but D12, and it ends CI summaries sending diagram source to plantuml.com, a site the ad audit measured at 4,328 requests and 544 third-party cookies on one page, with a web-push service worker at its root since June 2022 |
| 11.2 | **The token-layer PR** for Option C, alone | minor | The toolbar plan puts it first in its phase 2. It needs 6.2 finished: B delivers the markup C restyles. A watch item from The Road to Server Parity: if upstream lands `var(--puml-*)` colours, diagram theming and the `--kron-*` tokens become one mechanism |

### Stage 12. 4.0.0

| # | Item | Bump | Notes |
|---|---|---|---|
| 12.1 | Delete Server and Local rendering. Flip the defaults: Mermaid CI summaries, headers hidden, YAML notes, #85's compression, and a fragment height if 1.8 measured a better one. Option C's page with `--kron-*` as the public theming contract. The toggle-default unification (V4 Q6), which breaks. Wiki "Migrating to v4" | **major** | One combined scope document is owed first (D12). The direction report's bar: "One breaking release, one migration note, and a report that looks like a product rather than a test artifact." **Why before the launch:** rule 9 twice over. It is the "how it looks" column, and a product launched at 3.x and broken by 4.0.0 a month later is its own kind of amateurish |

### Stage 12b. Mobile at the edge

`MOBILE_PLAN.md` M1. Needs D20. **Why here:** rule 9's bar, then rule 8. The launch is a .NET launch
(D18), and a MAUI developer arrives through the .NET front door; section 0 says a visitor who finds
their case missing does not return. M1 makes mobile end-to-end true on every platform, with no code
inside the app, for the cost of a page, one importer, three converters and a sample. It depends on
nothing, so it can run beside any track (section 5, track D plus one tool verb), and it is numbered
12b so that no existing citation moves. M2 (MAUI in-app capture) is 14.1's cut seen from the other
side and lands there; M3 and M4 (native Android and iOS) are platform plans after 14.7.

| # | Item | Bump | Notes |
|---|---|---|---|
| 12b.1 | **HAR importer**, `kronikol ingest --har`, reading identity from the request headers exactly as `ProxyTap` does, else left for `--attribute-by-window`. Foundations L4 lists it as a take; nothing is built | minor | Charles, Proxyman and mitmproxy export HAR (ASSUMED), so this meets mobile developers in the tool they already use. Fixtures from one export of each before the reader is pinned |
| 12b.2 | **Tests-record converters:** JUnit XML with sequentially reconstructed windows behind a diagnostic (Maestro, the Android Gradle plugin, Firebase Test Lab), an xcresult JSON converter, and nothing for Appium from .NET or Java, which is already a Kronikol test process | with 12b.1 | Reverses the foundations' L5 rejection of JUnit XML for the sequential dialects only; the plan's gates G1 and G2 measure the timing fields first |
| 12b.3 | **The identity shim recipe** per driver and platform, and the tap options for devices (`OtlpTapOptions.TestIdAttribute`, the LAN bind, TLS) | patch, or with 12b.1 | The existing four headers plus `traceparent`; no new names |
| 12b.4 | **Wiki page and sample.** `Mobile-End-to-End.md`; a MAUI app in the BreakfastProvider repository driven by Appium from xUnit, with a Maestro flow as the second driver; one manual run recorded with screenshots; CI holds fixtures, never an emulator | none | Q1 and Q2 of the plan. The search page for 13.3 is written here and published there |

### Stage 13. The launch

Needs D18 and D19. **Why here:** rule 9. The direction report's distribution ladder, held until the
bar is met and then done in one push, because a first look is not repeated.

| # | Item | Detail |
|---|---|---|
| 13.0 | **The gate.** The bar of section 0, row by row, checked and not assumed. No open issue labelled bug. 10.2's cold-agent test passes. The demo report walked by fresh eyes at phone, tablet and desktop widths in both themes (the toolbar plan's sweep, baseline audit and height probe). The wiki and README re-read for anything 4.0.0 made false. `plan-execution-audit-checklist`'s five misses checked for every plan executed since | This is the one stage whose length is unknown, because it finds work. What it finds goes ahead of 13.1 |
| 13.1 | **The README as a landing page.** Twenty-five words, the demo link, one real screenshot of the new look, depth below the fold. The frame: "your tests pass and you have no idea what happened" against "here is exactly what happened". The demo first, not "an inline link inside a 350-word paragraph" | Half a day. Refresh the BreakfastProvider demo on 4.0.0 first: it is the artifact people land in |
| 13.2 | **The action as a Marketplace listing.** "The only item that compounds": every pull request becomes a demo to every reviewer, and the Marketplace is "a searchable channel with real intent, and you have no presence in it". 2.2 and 2.3 are the action, 7.4 gives it the scenarios that changed, 11.1 gives it a summary that renders natively | To check first: a Marketplace listing wants the action's metadata file at the root of a public repository, which a template folder inside this repository is not |
| 13.3 | **Search pages.** One page per integration family, phrased as the problem ("what HTTP calls did my test make", "sequence diagram from EF Core queries"), each ending in a real report link. The report's sixth rung, reach without adapters (Cucumber Messages and CTRF ingest), is built (3.1.0, 3.10.0) and publicised nowhere: it gets a page | Ongoing |
| 13.4 | **The agent shelf.** MCP N3, the registry publish under the keyword-bearing name. `LLM_FIRST_PLAN` §14.3's outward steps: the plugin marketplace submission, `claude plugin validate` and `eval`, the old packages' deprecation, the reserved NuGet prefix | People-work, the owner's |
| 13.5 | **A borrowed audience.** A few emails to one .NET newsletter, and the Reqnroll community first: LivingDoc is Gherkin and pass or fail only, and their docs invite users to build a report from the Cucumber Messages file, which `CucumberMessagesReader` already reads. "Answering a question they are actively asking" | The owner's voice |

**Not funded, by the same report:** a marketing site ("a maintenance burden that will not move 31")
and a rename (the name is "distinctive and searchable"; the twenty repository topics are "done
right"). Its measure of success: "31 unique visitors a fortnight becomes 300, and the referrer list
has a source on it that is not GitHub."

### Stage 14. After the launch

What the bar does not need (rule 9), in the order rules 6 and 8 give it.

| # | Item | Bump | Notes |
|---|---|---|---|
| 14.1 | **`PLATFORM_FOUNDATIONS_PLAN` F1.** `NdjsonTestRunWriter`, `ReportConfigurationOptions.CaptureMode = InProcess\|Ndjson\|Both`, and a byte-for-byte test of the in-process report against `ingest(ndjson)`. The test is expected to be red, and each red is a row for F5 | minor | .NET only, no monorepo needed. The plan calls it "the gate for everything" and says that if it fails "nothing else is worth starting". It costs days and decides work that costs months, so it goes first of this stage, and nothing stops it running earlier beside any stage (section 5, track D). #93 and #94 are two of its rows found by accident. At least eleven gaps are already listed (§11), including G12, suite into `stableId`. **From `INGEST_FEED_PLAN.md` (3.27.4, 3.29.0):** the writer's `kind: marker` record is the shape 14.1's projection emits; `RequestResponseLogRoundTripTests` already pins the seven members the writer still loses (`Error`, `AttributionSource`, `ExpiredFromTestId`, `FocusFields`, `NoteOnRight`, `SetupVariant`, `ActionVariant`), so the round-trip gate starts with those rows written; `IngestRoundTripTests` is the synthetic byte-identity harness (in-process against ingest, diagram source byte for byte); and the tool has no `--separate-setup`, so a suite that partitions setup cannot be reproduced through the CLI (Q11: 3 of 6 LightBDD diagrams differ by the `partition` lines with the tool's defaults, 6 of 6 identical through the library with the suite's options) |
| 14.2 | **Managed SHA-256 and MD5** with identical output, at five call sites | patch | The WASI build runs the ingest pipeline until `PlatformNotSupportedException` at the hash. Byte-identity under WASI (P10) cannot be tested until this is done |
| 14.3 | **`QUERY_PORTABILITY_PLAN` M0 and M1.** A `JsonSerializerContext` over the 17 call sites with the AOT analyzers on for good, and a golden query conformance corpus | patch | Its gate A2 (`PublishAot` never attempted, no MSVC linker on the machine that investigated) is settled here. The corpus is pinned after 8.1 on purpose. Then the rest of D10, with evidence |
| 14.4 | **F0, the monorepo** (`MONOREPO_MIGRATION_PLAN`, adopted whole) | none | Its preconditions are a quiet tree, no branches, both repositories green and a release just shipped. The `v*` tag trigger must split into `dotnet-v*` and `java-v*` before Java lands, and `CLAUDE.md`'s versioning rules are rewritten in the same change |
| 14.5 | **F3 and F4, then F2.** Interpretation moves renderer-side, the options file carries all 95 options, and only then does `captureFormatVersion 1` freeze | minors | Rule 6. The contract freezes holding 1.4's `plantUml`, 7.3's `TestCaseId` and a span stream designed after 5.1 |
| 14.6 | **F5,** close what 14.1 turned red | patches | |
| 14.7 | **F6 and F7,** the shared renderer and query as its second entry point | minor | Java's host becomes `wasmtime-java` over JNI (the pure-Java premise was falsified, §11); NativeAOT is the fallback. v4 having deleted Server and Local rendering and the IKVM package, there is that much less for a shared renderer to carry |
| 14.8 | **F8, F9, F10.** The corpus reshaped, the platform template, `kronikol tap` | minors | |
| 14.9 | **Kronikol4J 1.0 by subtraction** (about 14,305 rendering lines replaced by a shim), then Node, then Python, each with a launch of its own at its own bar (D18) | | `NEXT_LANGUAGE_PLAN` ranks Python after Node and excludes Go, C, C++ and Rust. Its weakest claim, that HTTP plus SQL is the 80/20, has a named falsifier: ask three users which extensions they enable. Do that before Node. The store plan §6.3 ranks Azure plumbing above a second language. The direction report mentions neither Node nor Python |
| 14.10 | **`QUERY_FALLBACK_PLAN` M1 to M6.** Replace the skill's `query.py` with an emitted `query.cs` calling the real `QueryCommand.Run` | minor | M0 is done. Gates A9 (the engine compiling inside `Kronikol` under `#if NET10_0_OR_GREATER`) and A10 (byte-identical stdout; encoding is the likely divergence) are unverified. Needs SDK 10. Nothing waits on it |

The long-line statement caps never mirrored in `PlantUmlCreator.java` (`LONG_LINE_SYNTAX_ERROR_PLAN`)
become moot at 14.9 when `-diagram` is deleted. Do not port them.

## 5. What can run side by side, and what is off the path

By files touched. Two sessions in one working tree have collided before, and one is active now. The
launch date is set by the longer of tracks A and B, so running them together is what shortens it.

| Track | Stages | Files | May run beside |
|---|---|---|---|
| A. History and the tool | 1.1, 1.2, 4, 7, 9, 10 | `Kronikol.Tool/`, `History/`, `VerbTable`, both skill copies | B, C, D. **Never two of its own stages at once** |
| B. Report rendering | 1.5, 1.6, 1.8, 1.9, 5, 6, 8, 11, 12 | `ReportGenerator.cs`, `stylesheets.css`, the report scripts, `InternalFlow/` | A, C, D. 5 and 8.2 beside 6 only with care (both embed blocks in the HTML) |
| C. Templates and CI | 2, 3.1 | the repository page, `templates/github-actions/`, `ci.yml` | anything |
| D. Capture records | 1.3, 1.4, 12b, 14.1 to 14.3 | `Ingestion/`, `RequestResponseLog.cs`, and for 12b `IngestCommand.cs` | A, B, C |

**Upstream PlantUML is off the critical path**, and with a bar to reach the direction report's
verdicts on it stand (D14): hold the queue, stop the parity dive, gate Teoz on a number, and "native
GitHub rendering" is "a lottery ticket, not a budget" until the camo constraint is re-verified
(24,320 public files hotlink plantuml.com and camo fetches server to server, which no client-side
renderer can serve).

- **Done:** `SMETANA_PERF`, `PERF_CI`, and every PR posted so far (#2835 to #2839, #2848, #2849,
  #2852, #2858 to #2862, #2873).
- **Waiting on the maintainer:** the answer to the 2026-09-21 reply on #2834 (seven prototype
  patches offered, none opened; master measures 0.83 to 0.99 of 1.2026.8 and 66% of warm time is the
  TeaVM runtime and class library). teavm#1248. plantuml-for-github PRs #11 and #13.
- **Waiting on D14:** `SMETANA_PARITY` (18 to 22 days, an umbrella issue and nine PRs, nothing
  posted; its line numbers need re-checking against 1.2026.9beta2). `PLANTUML_JS_PARITY`, which its
  own §1 puts behind Smetana parity. The ladder the engineering report gives, in its order: one-line
  platform fixes, TeaVM gaps, `PSystemBuilder2` divergences and factories from 21 to 41, format
  export as one PR (the unpushed local branch `ascii-probe`, about 350 lines, 72 of 148 outputs
  byte-identical to the jar), size levers (a terser second pass saves 61 KB gzipped with SVG
  identical), a tiered engine split, DOM-native output in three PRs of 350 to 650 lines, then layout
  PRs. PlantUML Without a Server lists eight fixes "worth doing regardless", the first a one-line
  `.puml` suffix strip. The 32-attribute style proposal was never posted, and DOM-native CSS would
  erase its tier A.
- **What Kronikol would take if upstream lands it:** Teoz semantic classes in place of scraping
  `DrewMessage` structure; a sequence-only engine tier, since a report is nearly all sequence
  diagrams; `var(--puml-*)` colours (11.2). Smetana matters to Kronikol only for component diagrams
  and internal-flow popups, where it would let `viz-global.js` go. No upstream plan gates v4.
- **For the maintainer alone:** the plantuml.com ad audit. It asks nothing of this roadmap, and it
  should never travel with an engine PR.

## 6. Deliberately not scheduled

Each has a trigger. None has a stage.

| Item | Why it waits | Trigger |
|---|---|---|
| Store plan P3, the facts layer (M0 to M5) | Owner's order of 2026-09-21, and the direction report's line against a hosted dashboard. §13's corrections R1, R3, R6, R7 and R9 apply first, and Parquet was never measured (R15) | Somebody asks for the warehouse |
| Ledger format version 2 | #96's two format findings (`shapeOrdered` repeats `shapeSet` at 47.5% of positions, `errors` is a tenth of the run lines and nearly all `null`) and `DASHBOARD_PLAN` M3 (per-scenario dependencies, sparse errors) all want it. **Take them as one version with one fold and one two-form reader, never as three** | A ledger large enough to notice, or M2's users asking for dependencies |
| Hosted identity and SSO | Store plan §6.2: possible, deliberately not next | A buyer asks |
| #79, observed impact analysis | Its author measured a static matcher and recommends not shipping it | After 7.4, with evidence of need |
| `HistoryIgnoreDegraded` | No run yet holds a failure inside a degraded run, so what discounting does to a real flip rate is unseen (noise plan §8) | A ledger that holds one |
| Cross-test mis-merge detector | Two workers hitting one collection within 2 ms can pair a wire record with the other's span (noise plan §8) | The count seen non-zero once |
| `REPORT_QUERY_PLAN` §3.4, note-divergence detection | No agent-reported need | One report of it |
| `VERIFICATION_MECHANISMS_PLAN` M4, property and fuzz tests | Deferred by the plan | After M1 to M3 have run for a while |
| `TEOZ_PERF_PLAN` W0.4, the solver convergence probe | Today's profile puts `RealDelta`'s cost in debug-name strings, not iterations | A patch that targets the solver |
| GitLab and other forges | Cheapest "other": detection variables only | After 2.4 |
| `LONG_LINE_SYNTAX_ERROR_PLAN` §6 Q2, component-diagram parser limits | A defensive ceiling is in place | A report of a lost component diagram |
| A report-diff recipe for migrations | "Two Eras of Gherkin" proposes "same scenarios, both stores, diff the reports" as a ClickHouse acceptance gate. `kronikol diff --baseline` and behaviour history already do it. It is a wiki page, not a feature | With 13.3's pages |

## 7. Coverage check

Every open issue, every unfinished plan and every artifact report, and where it landed.

**Issues.** #72 → 2.2 · #75, #83 → 0.1 (shipped) · #76 → 0.1, 7.1 · #77 → 7.3 · #78 → 7.4 · #79 → §6 ·
#80 → 4.2 · #81 → 4.1 · #82, #84 → 1.2 · #85 → 8.1 · #86 → 5.3 · #87 → 5.1 · #89 → 1.9 · #90 → 8.2 ·
#93 → 1.4 · #94 → 1.3 · #96 → 9.2 and §6 · #97 → 1.11, D21

**Plans.** `EVIDENCE_SURVIVES_A_RERUN` → 1.1, 1.2, 4 · `VERIFICATION_MECHANISMS` → 3 · `THEME` → 1.6, 6.1 ·
`TOOLBAR_REDESIGN` → 1.5, 6.2, 11.2, 12.1 · `DASHBOARD` → 9.0 to 9.4 · `HISTORY_DASHBOARD_STORE` → 2.3,
2.4, 9.4 to 9.6, §6 · `MCP` → 10.1, 13.4 · `V4` → 11.1, 12.1 · `PLATFORM_FOUNDATIONS` → 14.1 to 14.9 ·
`QUERY_PORTABILITY` → 14.3, 14.7 · `MONOREPO_MIGRATION` → 14.4 · `KRONIKOL4J_PORTABILITY`, `JAVA_PORT`,
`NODE_PORT`, `NEXT_LANGUAGE` → 0.6, 14.9 · `QUERY_FALLBACK` → 14.10 · `MOBILE` → 12b, 14.1, after 14.7 (D20) · `LONG_LINE_SYNTAX_ERROR` → 14.9, §6 ·
`REPORT_QUERY` → §6 · `LLM_FRIENDLY` (M3.1) → 10.1 · `TEOZ_PERF` → 1.8, §5 · `PERF_CI`, `SMETANA_PERF` →
0.2 (D15) · `SMETANA_PARITY`, `PLANTUML_JS_PARITY` → §5 (D14), 0.3 · `CROSS_RUN_HISTORY`,
`HISTORY_VERDICT_NOISE`, `HISTORY_ANALYZER_COST` → executed; leftovers in §6 and Appendix C

**Artifact reports.** Where Kronikol Goes Next → §0, rules 2 and 9, D10, D14, 2.1, 10, 13 · Diagram
Toolbar Redesign → 1.5, 6.2 · PlantUML Theme Audit → 1.6, 6.1 · Mermaid Payload Placement → 11.1 ·
Mermaid Skin for PlantUML, What PlantUML Cannot Paint → 6.1, D16, §5 · The Road to Server Parity → 1.8,
11.1, 11.2, §5 · PlantUML Without a Server → 3.3, §5 · plantuml.com Ad Audit → 11.1, §5 · Kronikol
Render Bench, Browser Render Worker Plan → 1.8, Appendix C · Two Eras of Gherkin → §6

Nothing without a place was found.

## 8. Keeping this file true

- When a stage ships, strike its row and name the release. When a decision is made, move it out of
  section 3 into the stage it unblocked.
- When a new issue arrives, place it by the rules of section 1 and say which rule. A defect goes
  ahead of the launch by rule 1. A feature goes ahead of it only if it earns a row in the bar.
- The order is an argument from facts checked on 2026-09-21. Section 2 shows how fast such facts
  expire: three days changed thirteen of them. Re-check a stage's premises before starting it. The
  direction report's usage figures are nine days old and were not re-read; re-read them at 13.0, so
  the launch has a baseline to be measured against.

---

# Appendices: what the earlier documents held

The stages above are the argument. The appendices are the record, so that nothing the triage,
`PLANS_STATUS.md` or the artifact reports said is lost by reading this file alone. What is **not** copied: the item-by-item
verification records of finished work (which commit closed which bullet) and the full measured
detail of each plan. Those stay in `PLANS_STATUS.md`, in the plans, and for deleted plans in git
history.

## Appendix A. The triage of 2026-09-18, carried over

**Its basis.** Every issue body and comment was read. Nothing was checked against the source, so
what the issues say about the code (`IngestPipeline` ordering, `RunSpeeds`, `ReportIndex` seeking)
was the issues' claim. Sixteen issues became seven plans, one standalone and one parked.

| Triage plan | Issues | Why they belong together | Where it is now |
|---|---|---|---|
| 1. History verdict noise | #75, #83 | Same analyzer, same run-speed calculation | **Shipped**, 3.22.2 to 3.25.0. Stage 0.1 closes the issues |
| 2. Evidence survives a re-run | #80, #81, #82, #84 | One debugging session, one verb, one shared way of addressing a run | 1.1, 1.2, stage 4 |
| 3. Internal-flow span mixing | #87 | Correctness bug. Lands before plan 4's #86 | 5.1 |
| 4. Internal-flow payload size | #89, then #86 | Same payload. The issues state the order themselves | 1.9, then 5.2 and 5.3 |
| 5. Compress `TestRunReport.json` | #85 | Needs a maintainer decision and a schema change | 8.1 |
| 6. PR delta selection | #77, #78, the rest of #76 | Have to be designed together, because of the gap below | Stage 7 |
| 7. PR comment template | #72 | Self-contained | 2.2 (already built, PR #73) |
| Standalone | #90 | Code coverage panel | 8.2 |
| Parked | #79 | Its author argues against building it yet | §6 |

**Plan 1 as the triage saw it, and as it shipped.** #83 said it depends on the same run-speed notion
as #75 §3 and "may well be one change". The triage's order was the partial-run baseline first,
because otherwise #83's `run degraded: 5.6× p95` label is built on a number #75 shows to be wrong.
It split #75 into three PRs: the templater (GUIDs with `_` separators, short hex digests, binary
segments, optionally the `HistoryShapeTemplates` hook, claims-aware templating and the "only ids
differ" verdict), merge before drop at ingest (in `IngestPipeline`, changing report content as well
as history), and partial runs in the analyzer (out of the duration baseline and the alternating
memory, the fold scenario out of `absent`). The issue claimed items 1a, 1b, 2a, 3a and 4a remove
about 95% of the noise on its suite. What shipped, one release per slice: S1 the templater (3.22.2,
patch; 0 of 212 CI call lines moved), S2 the merge, **after** attribution and before the drop because
merging before attribution let a dropped record take another record's span twin (3.22.3, patch),
S3 partial runs (3.23.0), S4 degraded runs, where a degraded run's durations leave `slower` and its
baseline as a partial run's do (3.24.0; false `slower` verdicts 5, 20 and 7 of 203 to 0), S5
`HistoryShapeTemplates`, the "only ids differ" hint and `history sN --calls` (3.25.0).
`tools/history-replay` is the acceptance harness. **Not taken:** claims-aware templating (12 UI-label
lines, 0.8%, and S5's hook already delivers it). Not done, by the plan's own word: nothing posted on
#75 or #83, and the reporter's replay has not been asked for.

**Plan 2.** #81, #82 and #84 touch only `kronikol query history`. #81: query the ledger without a
report (`--sid`, `--run`), and exit 2 on a usage error. #82: stop truncating the error in the
single-scenario view, or say where the rest is. #84: when a filter matches nothing but the window
holds qualifying scenarios, say so and give the `next:` command, and name the `--min-runs` bar when
that excluded a scenario. #80 is the larger change on the report-writing side: keep the last N runs
under `Reports/runs/<runId>/`, or at least never overwrite a failing report silently. It belongs
with the others because its `--run <id>` has to be the same address as #81's. The triage warned that
plans 1 and 2 both edit the history text renderer and must run one after the other; plan 1 went
first, so plan 2 rebases.

**Plans 3 and 4.** #87: one popup shows 547 spans and 60 root requests, which manufactured an N+1
finding. Same code area as #86, a different problem, not to be folded in. #89 is cheap, changes
neither keying nor rendering, and was to land early regardless of #87. #86 adds only 10 to 25% on
file size after #89; its real case is heap, parse time and the growth curve.

**Plan 5.** Same theme as plan 4 but a different file and reader (`ReportIndex` seeks by byte
offset). Blocked on the choice between compressing payload fields in place (`{"$z": …}`, the file
stays valid JSON, 82.7 MB to 13.9 MB) and also gzipping the whole file (6.1 MB, but `jq`, editors and
schema validators cannot read it as shipped). Either way a schema change (`$defs/compressed`, a root
`payloadEncoding`) that moves with `formatVersion` and ships behind an option.

**Plan 6.** #76 is mostly invalid by its author's own correction: `sourceFile` and `sourceLine` are
on the scenario and populated, 379 of 379 (3.20.0, ReqNRoll and xUnit 2). What remains is the CLI
projection and step-level locations on a Gherkin suite (0 of 2,297 top-level steps). #77 records
`TestCaseId` and `TestCaseIdKind` per scenario and adds `kronikol filter`, which emits the right
runner grammar; it touches every adapter package. #78 is `kronikol changed`: which scenarios a diff
added, edited or removed, read from git refs by comparing each scenario's text at the merge base
with its text now, with an eleven-check conformance suite and a 30-PR replay. The gap neither issue
states is in 7.2.

**Plan 7.** A composite action under `templates/github-actions/` keeping one PR comment with one
line per report artifact. Only loosely tied to #78 (one extra line in the comment).

**#90.** Connected to two issues, neither strongly enough to merge: #79 (per-scenario coverage is the
footprint #79 wants, and #90 explains why coverlet cannot provide it) and #89 (the convention of
decompressing a block only when first opened).

**#79.** A discussion issue. Its author measured a static step-definition matcher, recommends not
shipping it, and recommends shipping #78 first. The observed-impact version it sketches is later,
separate work. Its only comment, from `kevin-lozada-santos`, promotes an external tool.

**The triage's suggested order, and what became of it.**

| Triage step | Now |
|---|---|
| 1. #72 and the #81/#82/#84 patch | #72 is 2.2. #82 and #84 are 1.2. #81 turned out to be a minor (new flags) and moved to 4.1 |
| 2. #89 | 1.9 |
| 3. Plan 1, then the rest of plan 2 | Plan 1 shipped. The rest of plan 2 is stage 4 |
| 4. Plan 3, then re-measure and decide #86 | Stage 5, unchanged |
| 5. Plan 6, once the join key is designed | Stage 7, unchanged |
| 6. Plan 5, once option 1 or 2 is chosen | 8.1, unchanged |
| 7. #90 | 8.2, unchanged |

What this roadmap adds around that order: the live defects the evidence plan found (1.1), #93, #94
and #96, the toolbar defects, the pin move, the templates stage, the nets, and every plan the triage
did not cover (stages 6 and 9 to 14), and the launch the owner's answer of 2026-09-21 calls for (stage 13).

## Appendix B. The plan register

Every row of `PLANS_STATUS.md` as of 2026-09-21, with today's corrections. Status marks: DONE,
PART, OPEN (written, nothing built), and NGL for not green-lit.

### B.1 Open or partly done

| Plan | Status | Shipped | Stage | What the row records |
|---|---|---|---|---|
| `EVIDENCE_SURVIVES_A_RERUN_PLAN` (2026-09-18) | OPEN, NGL. S0 in flight | none | 1.1, 1.2, 4 | #80, #81, #82, #84 against 3.20.0 and 3.21.0. Four slices plus S0. Eleven findings the issues do not state, a twelfth (the delete-sharing lock) and then F13 to F15. §11 is an assumption ledger: six of the plan's own statements were reversed by checking them, then four more. Measured on the 394-run CI ledger: 5 of 8 distinct error texts exceed the 80-character cut, each losing its "but found" half, and none reaches the ledger's 199 cap; the `--failing` hint would fire on 5% of green runs. An unfixed sweep would merge a differing retained run in as a shard (green report, 12 scenarios, 1 failed). The CI default is off because the consumer checked uploads and publishes the whole reports directory. Open question 6's command does not exist (`--baseline` takes no value). `kronikol merge` never reaches the core writer, so it neither rotates nor writes `Run.json`. §9 is a BreakfastProvider replay of the session. Prototype patch: 8 files, +456 −4, against 3.22.1 |
| `HISTORY_DASHBOARD_STORE_PLAN` (2026-09-19) | OPEN, NGL | none | 2.3, 2.4, 9.4 to 9.6, §6 | See B.3 |
| `DASHBOARD_PLAN` (2026-09-14) | OPEN, NGL | none | 9.0 to 9.4 | A static dashboard over the history ledgers. Each repository's `kronikol-history` branch gains a derived `history.view.json` (verdicts computed by `HistoryAnalyzer`, no fingerprints; 116 KB gzipped for BreakfastProvider's 108 runs against 226 KB for the ledger). `kronikol dashboard build` emits one self-contained page that renders the baked snapshot and refreshes live from public branches (raw serves `Access-Control-Allow-Origin: *`, a 5-minute cache and `304` on `If-None-Match`, all measured). Leads with what no competitor has: behaviour drift, calls per scenario, cluster ageing. Private repositories are snapshot-only and a private Pages site needs Enterprise Cloud. Three milestones, one minor each, plus an optional ledger v2. Proposes one sentence for `CROSS_RUN_HISTORY_PLAN` §14. Charts: Observable Plot on vendored D3 |
| `THEME_PLAN` (2026-08-30, revised 08-31) | OPEN | none | 1.6, 6.1 | `PlantUmlTheme` for all 43 bundled themes, note styling derived from the theme, an acceptance harness. Upstream #2848 and #2849 merged 2026-08-31 and are in the current pin. Harness prototypes: `tools/render-bench/theme-*.js` and `themeprobe/` |
| `TOOLBAR_REDESIGN_PLAN` (2026-08-31) | DRAFT, NGL | none | 1.5, 6.2, 11.2, 12.1 | Option B (segmented, contained toolbar) in 3.x, Option C (quiet bar, `--kron-*` token layer) at 4.0.0, tri-state filtering for tags, dependencies and categories. All design decisions closed (violet stays the Specifications default, status and duration all-or-nothing, a band grid from 1351 px). §5 holds six open questions. Three pre-publish gates: sweep, baseline audit, height probe |
| `V4_PLAN` (2026-08-30) | OPEN beyond the phase 0 spikes | none | 11.1, 12.1 | Four breaking changes: Mermaid CI summaries, all server-side rendering removed, headers hidden by default, YAML notes by default. Mermaid step-summary probe run, awaiting the owner's eye; branch `v4-mermaid-spike` |
| `VERIFICATION_MECHANISMS_PLAN` (2026-09-12) | OPEN, NGL | none | 3 | Successor to `LLM_FIRST_PLAN`: invariants that check two artifacts against each other, the class of defect a single-artifact audit cannot find (the comparer of `338231b`) |
| `MCP_PLAN` (2026-09-12) | OPEN, NGL. Row added to `PLANS_STATUS.md` 2026-09-22 | none | 10.1, 13.4 | A `kronikol mcp` subcommand. Hosting is ruled out on the credential-sink argument (§3.2). Ledger: 13 of 22 rows broken over six passes. Checked against a real host: roots are deprecated (SEP-2577), `structuredContent` and `outputSchema` are one SDK switch. Download baseline in `MCP_PLAN.baseline.json` (`Kronikol.Tool` at 4,084) |
| `QUERY_FALLBACK_PLAN` (2026-09-13) | OPEN, NGL. M0 done | none | 14.10 | `dotnet run query.cs` emitted beside the report, calling the real `QueryCommand.Run`: parity by construction. Measured: `#:package` resolves from the NuGet cache with every source cleared, cold 2.9 s, warm 0.25 s, works inside `bin/Debug/<tfm>/Reports/`, and a net8.0 test project still gets a working net10.0 fallback. Fails under a `global.json` pin to SDK 9 |
| `QUERY_PORTABILITY_PLAN` (2026-09-13) | OPEN, NGL | none | 14.3, 14.7 | `kronikol query` to Java and Node as a NativeAOT binary behind thin wrappers (the esbuild pattern for npm, the protoc-jar pattern for Maven behind the existing jbang alias). TeaVM does not help: it consumes JVM bytecode. §5 costs and rejects making Java canonical. Measured: 17 AOT warning sites under exactly two codes, all `JsonSerializer` without source generation, no reflection in 6,568 lines. §3.4 amended by the Kronikol4J portability plan |
| `KRONIKOL4J_PORTABILITY_PLAN` (2026-09-13) | OPEN, NGL | none | 14.9 | Kronikol4J is 32,100 lines: 45% rendering, 38% capture tail, 17% irreducible capture core. Of 13 divergence-ledger entries 7 created re-port work, 5 of those rendering detail. Recommendation revised once Kronikol4J was understood as pre-1.0: run the WASI experiment before 1.0 and let it choose the release architecture, because adopting the shared renderer now deletes 14,305 lines and after 1.0 it replaces a shipped renderer. One design constraint (§8.1): keep the module's syscall surface at Preview 1's floor. Several host claims since falsified by the foundations plan §11 |
| `PLATFORM_FOUNDATIONS_PLAN` (2026-09-14) | OPEN, NGL | none | 14.1 to 14.9 | The definitive plan for four platforms in one repository; where it contradicts another plan, it wins. Every platform is a capturer writing two NDJSON streams and an options file, one shared renderer built from .NET, one shared query engine. F0 to F10, eleven measured gaps G1 to G11 plus G12. §12 settles nine sharing levers. Taken: the taps as a shared artifact (F10), baggage as its carrier, HAR and CTRF importers, structured frames and an allow-listed environment under "the capturer never interprets, the renderer never reads outside its input", generated record and options types, processors as a `noteRules` list, one in-process rendering path through `IngestRequest.Interactions`. Rejected: OTel instrumentation hooks as the seam, host callbacks from the renderer. Nothing lost on .NET; one Java regression, `NoteProcessors` lambdas become rules and arbitrary logic such as JWT-claims extraction is lost unless it becomes a rule kind |
| `NEXT_LANGUAGE_PLAN` (2026-09-13) | OPEN, NGL | none | 14.9 | Written without reading the port and monorepo plans, and its §1 rediscovers `NODE_PORT_PLAN` §3.11 to §3.12. New in it: the audience ranking (Python best after Node, on pytest's near-monopoly: one adapter where Node needs six and .NET needed fourteen; Go deferred because it cannot be instrumented at runtime and eBPF shows generic database operations, not statements). **OTel cannot carry request and response bodies** (semconv captures sizes; spec issues #857 and #1219 open for years) and bodies are about 90% of a report, so OTel is a shallow catch-all beside deep native adapters. A scoped port is about 5,600 lines. A shared renderer takes rendering maintenance from N to 1 while capture stays at N |
| `MOBILE_PLAN` (2026-09-22) | OPEN, NGL | none | 12b, 14.1, after 14.7, D20 | Three layers. M1, the device at the edge: a HAR importer, JUnit XML and xcresult converters to tests records, the identity shim recipe per driver, a MAUI sample driven by Appium and Maestro; serves iOS, Android, MAUI, Flutter and React Native with no code in the app. M2, in-app capture for MAUI: blocked on a capture-only core package because `Microsoft.AspNetCore.Mvc.Testing` carries a framework reference to ASP.NET Core (READ from the manifest, not measured: no mobile workload on the machine). M3 and M4, Android through Kronikol4J (its OkHttp interceptor exists; no JUnit 4 adapter, no NDJSON writer) and iOS through a new Swift package, as §9 template instances after the shared renderer. Measured: a backend deployed as its own process cannot hand its inside view to a test host today (the span mapper reads no `kronikol.*` attribute, the logger has no file sink, F1 is not built). Fourteen ledger rows, four gates |
| `SMETANA_PARITY_PLAN` (2026-09-14) | OPEN, NGL, nothing posted | none | §5, D14 | Upstream. One umbrella issue and nine PRs. `skinparam linetype ortho` needs a hand port of about 4,800 lines of Graphviz 2.38 `lib/ortho`. Everything else is maker-side omission (`nodesep` and `ranksep` alone explain the #1703 density complaint, canvas ratio 0.79). 49 attribute and 31 port findings, a 52-probe corpus, issue and PR drafts in appendices A and B, seven open questions. Evidence kit in `C:/Code/plantuml-plans/smetana-parity/` |
| `PLANTUML_JS_PARITY_PLAN` (2026-09-10) | DRAFT, NGL. File truncated; marked as a fragment 2026-09-22 | none | 0.3, §5 | Upstream. A ladder of PRs taking the TeaVM build to server parity |
| `JAVA_PORT_PLAN` | PART | Kronikol4J v0.1.24 | 0.6, 14.9 | Output rendering byte-complete. Last Java-source commit 2026-08-23; everything since is ledger documentation. Outstanding: all of Appendix C (the tap, ingest and Playwright modules; `OTLP_TAP_PLAN.md` exists there untracked), cross-runtime parity CI, the wiki at 17 pages against 89, the `Clock` seam, context modules, GraalJS search-test reuse, the Playwright suite, a Java BreakfastProvider demo, the .NET-side parity-hardening items |
| `NODE_PORT_PLAN` | OPEN (design record) | none | 14.9 | Design phase complete, no code. No `js/`, no `package.json` |
| `MONOREPO_MIGRATION_PLAN` | OPEN (design record) | none | 14.4 | No phase executed. The 12 cross-repo `ProjectReference`s are intact, both wikis separate. Its motivating problem, hand-blessed goldens and a ledger that drifts, is live and growing |
| `TEOZ_PERF_PLAN` | PART, upstream-gated | 3.0.76 (pin) | 1.8, §5 | 1.2026.7 dropped the Puma sequence engine. Five patches with SVG-hash identity proofs merged as #2835 to #2839; teavm#1247 fixed; `AGGRESSIVE` trial recorded as a negative result. Kronikol pins a stock fork build and passes `{ maxSvgSize: 98304 }` at every ES-module render site, so the 98304 patch is retired. Measured 4 to 8 times faster warm (puml-19 789 to 181 ms, gen-500 5.8 to 0.8 s). `viz-global.js` kept by owner decision. Open in-plan: W0.4 and the optional speedscope export. Working artifacts in `tools/render-bench/` |
| `PERF_CI_PLAN` | DONE, deleted 2026-09-22 (`git show 158d62e5:plans/PERF_CI_PLAN.md`) | upstream | 0.2 | `perf-bench/` harness, workflow with three compare modes, 13 corpus fixtures, runner-calibrated bands. #2840 merged 2026-08-30, #2862 merged. Minor deviation: a static artifact name |
| `SMETANA_PERF_PLAN` (2026-09-01) | DONE, deleted 2026-09-22 (`git show 158d62e5:plans/SMETANA_PERF_PLAN.md`) | upstream 1.2026.8 | 0.2 | Browser Smetana against the viz.js bridge went from 2.8 to 7.7 times slower to 0.37 to 1.0: it wins or ties every row, SVG byte-identical on both layout paths. #2858 to #2861, #2866, #2867 merged 2026-09-03 |
| `LONG_LINE_SYNTAX_ERROR_PLAN` | PART, about 95% | 3.0.48 | 14.9, §6 | Both fix layers, all tests, docs and the IKVM verification done. Open: the Kronikol4J mirror and the component-diagram limit probe |
| `REPORT_QUERY_PLAN` | PART, about 99% | 3.0.47, tail 3.1.0 | §6 | `--json`, the dead `--raw` flag and the streaming test closed in 3.1.0 (the streaming test found the scanner materialising a UTF-16 copy of every payload: 324 MB for a 142 MB file, now 32 MB). Open: note-divergence detection, and golden output tests are still assertion-based |
| `LLM_FRIENDLY_PLAN` (2026-09-10) | DONE but M3.1 | 3.1.0 | 10.1 | Everything but MCP, which moved to `MCP_PLAN`. §11 holds the measured channel table: `dotnet test` swallows library stdout, so files are the discovery channel |

### B.2 Executed, kept or deleted

| Plan | Shipped | File | One line |
|---|---|---|---|
| `CROSS_RUN_HISTORY_PLAN` | 3.9.0 to 3.11.0, then 3.12 to 3.20 | kept (two other plans amend it) | The ledger, verdicts, `history gate`, `quarantine`, `rename`, `doctor`, `import`, history in the HTML report, the dogfood branch `kronikol-history`. Its §0.2: `ctrf-io/github-test-reporter` gives away status trends, and its `flakyRate` measures retry volume, so Kronikol's honest claim is flakiness detection at all for suites that do not retry. Fifteen of its own decisions were reversed by checking them, all the same mistake: an existence check standing in for a behaviour check |
| `HISTORY_VERDICT_NOISE_PLAN` | 3.22.2 to 3.25.0 | kept | Appendix A, plan 1 |
| `HISTORY_ANALYZER_COST_PLAN` | 3.25.2 | kept | #91: 1,950 to 200 ms at 5,000 × 50, through the tool 6.9 to 1.1 s. Held by two counts, not a timing (a ratio of two timings could not tell the analyzers apart inside a parallel suite). `tools/history-replay` gained `--full`, `--aliases`, `--time` |
| `NOTE_APPEARANCE_CONTROLS_PLAN` | 3.22.0, audit 3.22.1 | deleted 2026-09-19 | The monospace control is opt-in behind `ShowNoteFontControls`. An optgroup widens a closed select 15 px in Chromium |
| `BACKGROUND_ATTRIBUTION_PLAN` | 3.17.0 | deleted 2026-09-19 | A host started inside a test inherits its `AsyncLocal` identity. Provenance marks, expiry at scenario end, a background block, hosted services detached automatically |
| `ALTERNATING_AND_FAILED_SENDS_PLAN` | 3.18.0 | deleted 2026-09-19 | The `alternating` verdict; a thrown send recorded with `!Type` and `error` |
| `DOCUMENT_OWNERSHIP_PLAN` | 3.19.0 | deleted 2026-09-19 | An identity-less document operation lands with the document's last attributed writer. Spanner captures no key and does not take part |
| `OWNER_WINDOW_AND_COUNT_CONFIRMATION_PLAN` | 3.20.0 | deleted 2026-09-19 | A count-only change is a verdict on the second run that holds it; the owner window; Mongo claim by filter attributed by its reply |
| `LLM_FIRST_PLAN` | 3.1.0 to 3.8.0 | deleted 2026-09-19 | Eight milestones, one release each. 144 findings, 108 verified, 47 did not survive as written |
| `QUERY_PERF_PLAN` | 3.0.69 | deleted 2026-09-19 | On a 142.9 MB corpus: `summary` 1.39 to 1.19 s, `values` 2.89 to 1.95 s, `grep --number` 4.53 to 2.64 s |
| `SEARCH_INDEX_PLAN` | 3.0.70 to 3.0.72 | deleted 2026-09-19 | Trigram index and worker verify. Phase 2 (§10) deferred by design |
| `NOTE_YAML_TRAILING_WS_PLAN` | 3.0.79 | deleted 2026-09-19 | |
| `TOGGLE_DEFAULTS_PLAN` | 3.0.80 | deleted 2026-09-19 | `ReportToggleDefaults`, M1 to M8 |
| `NOTE_COPY_FIDELITY_PLAN` | 3.0.86 | deleted 2026-09-19 | Every width-budget break marked with U+200B and undone by one shared rejoin |
| `NOTE_WRAP_AND_WIDTH_PLAN` | 3.0.84, 3.0.85 | deleted 2026-09-19 | §2.11's HTML-panel alternative deliberately not taken |
| `DIAGRAM_WIDTH_PLAN` | 3.0.83, 3.0.85 | deleted 2026-09-19 | Nine axes closed but #9. The 4096 limit re-measured as raster-only |
| `BACKGROUND_STEPS_INLINE_PLAN` | 3.0.48 | deleted 2026-08-30 | |
| `QUERY_V2_PLAN` | 3.0.51 to 3.0.58 | deleted 2026-08-30 | Eight milestones. The `select` verb was a no-go |
| `BROWSER_RENDER_WORKER_PLAN` | 3.0.45, 3.0.50 | deleted 2026-08-30 | Perf budgets are contention-scaled since 3.0.69 (`ContentionScale`, floor 1, cap 5) |
| `NOTE_YAML_TOGGLE_PLAN` | 3.0.59 to 3.0.68 | deleted 2026-08-30 | |
| `OTLP_EXPORT_PLAN` | 3.0.60 | deleted 2026-08-30 | |
| `EXAMPLES_BLOCKS_PLAN` | 3.0.64 | deleted 2026-08-30 | |
| `REQNROLL_DUPLICATE_STEPS_PLAN` | 3.0.64 (#71) | deleted 2026-08-30 | |

Deleted plans are retrieved from git history: `git show 82abeb7f:plans/<name>` for the thirteen
deleted on 2026-09-19, `git show 159aef5:<name>` for the seven deleted on 2026-08-30. Source comments,
test comments, the changelog and other plans still name them.

### B.3 The store plan's row, by subject

The longest row in the index. Its findings, grouped, because stages 2 and 9 and section 6 lean on them.

- **Storage is not the constraint.** Full fidelity is 138,341 B per run on a real 203-scenario suite,
  681 B per scenario-execution, 4.6 GB a year at the consumer's measured 90.6 runs a day, and $0 on
  Cloudflare R2, whose 10 GB free tier exceeds the 4.5 GB tiered steady state. Storage is at most 5%
  of the bill; the rest is egress. Azure's first 100 GB a month of egress is free.
- **Retention, not compression, controls the bill.** Every one of 2,692 interactions is
  byte-distinct, a re-run that changed nothing still costs 154 KB, and random ids never compress.
  22.2 GB untiered at five years against 4.5 GB tiered. The payload layer must **not** be tiered to
  infrequent-access classes: at about 116 KB objects the 128 KB minimum billable size inflates the
  bill 12.7% to save $0.008 a month, and 30 and 90 day minimum durations collide with configurable
  retention.
- **Levers.** Store facts, not reports (`diagrams` is 33.3% and derived). Split a stable layer (22 KB
  a run, for ever) from a payload layer (116 KB a run, expires). Two layers, two stores: payloads as
  blobs by run id, the stable layer as Parquet on object storage read with DuckDB, ClickHouse the
  upgrade path. Document and relational databases rejected in writing.
- **Corrections to itself.** The first draft's 95,598 B measured a lossy renumbering (the restoring
  mapping costs 74,195, so the saving is 6.7% not 47%). Then R3: drop id renumbering altogether,
  because `OtlpSpanMapper` publishes both "safe" keys as span and trace ids and sequential integers
  collide across runs after `ToGuid` (MD5). F7: replay ships but **the projection from a report back
  to records does not exist** (#93 and #94 came from checking it). Byte-identity is no longer claimed;
  the promise is reproducing the evidence.
- **Verdicts are not write-once (R1).** Twelve options, the baseline stream, two editable companion
  files and append order decide them. The verdict rules carry no version and moved in 3.15, 3.16,
  3.18, 3.20, 3.23 and 3.24. Since 3.25.0 part of the fingerprint rule lives in the consumer's
  configuration, so a backfill needs the consumer's rules and not only their hash.
  `InteractionShape.Version` has moved three times and the analyzer refuses to compare across rules,
  so fingerprints become derived, recomputed from a re-templatable core of 22,069 B a run.
- **Performance is file count.** One file per run is 165,421 opens; monthly compaction is about 60
  and about 400 ms. Compaction is mandatory. **Parquet was never measured** (R15): every figure is
  zstd-19 over JSON and the "79 MB verdict layer" is the ledger. No browser decompresses zstd
  natively (R20). DuckDB-WASM cannot send an `Authorization` header, GETs whole files by default and
  fetches its Parquet reader from a third origin (35.9 MB raw), and DuckDB.NET is a 110 MB package
  against a 7.3 MB tool (R9).
- **No atomicity story (F10).** The ledger had one (`merge=union` and a fetch and rebase retry across
  18 lanes). Object storage has none. A manifest with an atomic pointer swap is M2's first task.
  Backblaze B2 has no conditional write and GCS uses its own header, so the swap is not one S3 code
  path. The compactor rewrites the only copy (R6): make immutable per-run bundles the record and
  Parquet a rebuildable index. The backfill does not reach backwards: a ledger keeps
  already-templated text, so migrated history is stuck at its rule version, which argues for
  switching a store on early.
- **§8.6, what the plan lacked:** migration (M5), backup now that a bucket and not a replicated git
  branch is the system of record, a store format version, what the gate does when the store is
  unreachable, other platforms writing to it, semver per slice, the wiki pages that move, and a
  testing strategy, the largest omission (which fake: Azurite, MinIO, LocalStack or the filesystem;
  MinIO was archived in April 2026).
- **The three exclusions are narrower than they read (§6.1).** Alerting by emitting events the
  workflow delivers (9.6). Workflow by prefilled PRs against the already-committed quarantine file.
  Accounts by inheriting the customer's. Hosted SSO is possible and deliberately not next (§6.2):
  the credential-sink argument is about accepting other people's data, and the invariant is never to
  hold a credential that can read customer data. Proxy (page and data behind one authenticating
  edge, the right default) or federated (PKCE, then temporary cloud credentials). R2 has no OIDC
  federation and works only behind Cloudflare Access. Azure Blob is the easiest for browser
  authentication because it takes a bearer token natively, but Azure has no zero-compute
  authenticating proxy (R19).
- **Three install tiers.** Tier 0, shipped: capture, `dotnet tool install`, about 45 lines of YAML.
  Tier 1, the dashboard plan: two more steps, no cloud account. Tier 2, this plan. Nobody onboards
  into the warehouse; the problem is Tier 0. Tier 1 does not port to Azure DevOps, so Tier 2 is the
  more portable rung.
- **The likely buyer is the least served (§6.3).** A .NET product's probable customer runs Azure
  DevOps on Azure with Entra ID: no Tier 0 recipe, no onboarding artifact, no possible Tier 1, no
  store path. `CiEnvironment` detects exactly two providers. The capture is .NET-only, so today's
  claim is best in class for .NET teams. The residual is onboarding friction, which no follow-up
  closes.
- **Three products built as one (R16).** Years of verdict-level trends and durable reports by run id
  need neither the projection nor a columnar store, and the slices built the third product first.
  Hence §8.0: P1, P2, P3.
- Harness: `HISTORY_DASHBOARD_STORE_PLAN.harness/measure.py` reproduces every figure in one pass.

## Appendix C. What finished plans left behind

Deferred by design or left as a nit. None has a stage. Each is here so it is not rediscovered.

| From | Left | Standing |
|---|---|---|
| `OTLP_EXPORT_PLAN` | A protobuf encoder, a header allow-list, `parentSpanId` inference | Deferred by design. `Kronikol.Extensions.Otlp` has had no commits since |
| `BROWSER_RENDER_WORKER_PLAN` | `_svgCache` retirement (`collapsible-notes-script.js` still has 13 references, so two caches coexist). Stable fragment boundaries: split on the original source and apply note states per fragment, so a toggle does not move the seams. `requestIdleCallback` chunking of large `innerHTML` injections (measured at 20 to 100 ms, so no evidence it is needed) | Labelled optional by the plan. Stable boundaries bear on the parked page-shift-on-toggle bug of 3.0.67 |
| Kronikol Render Bench | Node renderer parallelism across cores, advised "once the per-process cost is gone". It is gone (`RenderMany`, the V8 code cache) and there is no parallel code in `NodeJsPlantUmlRenderer`. The fragment-height default | Open. The second is 1.8. The Node renderer stays after v4, which deletes only Server and Local, so the first stays open too |
| `QUERY_V2_PLAN` and `QUERY_PERF_PLAN` | A SQL query surface (declined 2026-08-26: bodies are stringified JSON, no budget contract, worse errors, `grep --number` inexpressible). A persisted sidecar index | The sidecar stays gated on evidence of routine 250 MB reports. #85 lowers that pressure |
| `NOTE_YAML_TOGGLE_PLAN` | Kronikol4J script sync | Deferred by design, documented in `Kronikol4J/README.md` |
| `EXAMPLES_BLOCKS_PLAN` | M6.3, a Kronikol4J ledger entry for the 3.0.64 band rendering. Band CSS is light-only (the stylesheet has no dark theme) | D11. The dark half returns with Option C |
| `SEARCH_INDEX_PLAN` | Phase 2 (§10). No trigram index in Kronikol4J, and `normalize.js` is the cross-language reference | Deferred by design; the port half is D10 and D11 |
| `DIAGRAM_WIDTH_PLAN` | Axis 9, user-authored `InsertPlantUml` text is documented and not wrapped. Activity-diagram **height** is the axis that work did not close | Open, unscheduled |
| `NOTE_WRAP_AND_WIDTH_PLAN` | The HTML-panel alternative to notes (§2.11) | Deliberately not taken |
| `LLM_FIRST_PLAN` | §14.3, the outward steps: marketplace submission, `claude plugin validate` and `eval`, deprecating the old packages, the reserved NuGet prefix. From M4: `TestRunReportFullStepDetail` is JSON-only | People-work, the owner's. They are 13.4 |
| `CROSS_RUN_HISTORY_PLAN` | Q15, whether `diff --baseline` should read the ledger. `merge --history` has never been timed (mergeable shards cannot be made from outside the test project) | Open |
| `HISTORY_ANALYZER_COST_PLAN` | The residual is allocation: 21 KB per scenario, a quarter of the time at 10,000 scenarios is collector pauses, and the next lever is fewer per-scenario lists | "Its own plan", unwritten. Beside #96 |
| `OWNER_WINDOW_AND_COUNT_CONFIRMATION_PLAN` | Identity-less work that names no document, outside a detached flow, still lands nowhere | The known remaining attribution gap |
| `TEOZ_PERF_PLAN` | The move to npm | 1.8 |
| Documentation audit of 2026-08-29 | The Kronikol4J wiki at 17 pages | With `JAVA_PORT_PLAN` |

## Appendix D. The artifact reports

Twelve published reports (private pages on claude.ai), all read in full on 2026-09-21. For each: what
it argues, and what this roadmap took from it. Where a plan has since superseded a report, the plan
wins and the difference is stated.

### D.1 Where Kronikol Goes Next (2026-09-12, at 3.0.86)

<https://claude.ai/artifact/GsEZb8LW56h33qQuJKHCSt> · Basis: usage data, a competitive scan, a read of
the repo. Section 0 carries its thesis, its evidence and the owner's answer to its closing question.

**Its five moves, in its order, and where each stands.**

| # | Move | Its reasoning | Today |
|---|---|---|---|
| 1 | Finish the LLM-friendly work, to its smallest shippable slice: "run-end pointer, `Failures.md`, `--json`, MCP, plus the discovery files" | Half built, and the market validated it: "You already have the harder half in schema'd JSON, `kronikol query` and the deep-search index. What is missing is the loop." Flip condition: if it takes more than a few weeks, swap it with move 2 | All shipped in 3.1.0 to 3.8.0 but MCP (10.1). Its acceptance test has never been run (10.2) |
| 2 | Distribution, not features: "get people into the artifact faster" | "At two visitors a day, no product work compounds." Done looks like 31 visitors a fortnight becoming 300 | Not started. Under the owner's answer it is stage 13, after the bar, with its passive first rung at 2.1 |
| 3 | Decide the Java port, in public | "A standing tax": rendering byte-complete, capture incomplete, "and from outside it looks finished". Fund capture, or freeze it and say so in the README | Open. D10, 0.6 |
| 4 | V4 and the visual milestone, with themes and DOM-native styling folded in | "Specced, decisions locked, real product bugs already identified. Bundle the breaking changes into one release." It sat fourth because it is "worth more once there are users to notice it" | Not started. Under the owner's answer it moves ahead of the launch: stages 6, 11, 12 |
| 5 | Cross-run history, the minimal version: "a trend file in the repo". **"Do not build a hosted dashboard"** | The one real capability gap against Allure TestOps, ReportPortal, Trunk and BuildPulse | Done and exceeded (3.9.0 to 3.25.2). The line against hosting governs stage 9 and section 6 |

**Its distribution ladder, by effort.** About an hour: the demo where people land (2.1, 13.1). Half a
day: the README as a landing page (13.1). A project: a GitHub Action that comments on the pull request
with the diagram for the scenarios that changed, "the only item that compounds" (2.2, 2.3, 7.4, 13.2).
Ongoing: search pages, which are the same work as the agent channel (13.3). A few emails: a borrowed
audience, the Reqnroll community first (13.5). A project: reach without adapters through Cucumber
Messages and CTRF ingest, which is built and unpublicised (13.3). Do not: a marketing site, a rename.

**What not to fund.** HOLD the upstream PlantUML queue (extension PR, stdlib bundles, multiplatform
ports): "Waiting is free." HOLD native GitHub rendering: the blocker is camo, "a lottery ticket, not a
budget", and the constraint is to be re-verified before spending. STOP the JS and Java parity dive and
ascii export: "The conclusion is the deliverable", citing the 58 KB gzipped data ceiling and wasm-gc
measured dead. GATE further Teoz work on a number (D17). STOP eight plan files in flight: "Two
finished beat six open" (rule 2).

**What it does not address:** pricing or hosting beyond the dashboard line, test selection, coverage,
OTLP, Mermaid or CI summaries, Node or Python, NuGet listing, onboarding templates.

### D.2 The report's look

| Report | Argues | Taken from it | Superseded where |
|---|---|---|---|
| **Diagram Toolbar Redesign** (2026-08-31) <https://claude.ai/artifact/MK7HWJjoX5zX1i4Vs6Vc91> | Eleven measured defects F1 to F11 in the scenario toolbar and report top bar (two button families in one row, px and em mixed, a 4 px seam fusing unrelated groups, base styles that ship only with internal-flow tracking). Option B in 3.x, Option C at 4.0.0. "No decision remains open on this page" | The two shipped bugs "land first as their own release" (1.5). Eight items the plan holds only by reference (6.2). W1, W2 and W4 row layouts were explored and dropped; W3 split rows stands. The measured-differences table as a regression fixture | The plan is otherwise a superset. Its six open questions are the plan's §5 |
| **PlantUML Theme Audit** (2026-08-30) <https://claude.ai/artifact/Ay7qjfLMhhfTx2r2uVgCzU> | The remembered colour-matching breakage is fixed (fold-triangle detection holds on 39 of 43 themes). The real defects: `!theme` is a silent no-op in BrowserJs (true on the pin the audit ran; from 3.0.76 to 3.29.5 it hung the render instead, `DIAGRAM_COLOURS_PLAN.md` F21, and from 3.29.6 it renders unthemed with the engine's warning), 12 themes lose arrow labels on a white page, 4 `-outline` themes kill every note interaction, `carbon-gray` fails to parse on the jar, Kronikol's baked colours do not move with the theme. A seven-point admission checklist to run as a test | The two theme-independent defects and the honesty diagnostic (1.6). The checklist (6.1) | Upstream #2848 and #2849 merged the next day, so the plan emits `!theme` and registers `globalThis.PLANTUML_THEMES` where the audit spliced theme text. Background comes from `!$BGCOLOR`, not container CSS. **The plan reverses the audit's central trade:** notes follow the theme. The plan keeps all 43 themes including `carbon-gray` and scopes the feature to BrowserJs. Still unanswered from the audit: throw or warn for the render modes left out of scope |
| **Mermaid Payload Placement** (2026-08-30) <https://claude.ai/artifact/4CAr3rXLeBuKn3kmFXLvC4> | One 14-call checkout scenario rendered two ways as plain markdown for the step summary. Option 1, payloads inline on the arrows: "viable only for one-line body summaries". Option 2, `autonumber` plus a numbered `<details>` legend of JSON fences: "the natural fit for a CI summary, with the HTML report artifact keeping the PlantUML-fidelity view" | Keep a one-line status and outcome on response arrows. The Mermaid switch is independent of v4's other breaks and closes the plantuml.com leak, which is why it may be pulled forward (11.1) | `V4_PLAN` adopts Option 2 and hardens it: the numbering invariant, four-backtick fences, a 768 KiB budget against the 1 MiB cap, `autonumber <next>` across split parts. Open: the spike's eyeball, the `<details open>` heuristic (Q3), typed participants (Q2), Azure DevOps fidelity (Q4) |
| **Mermaid Skin for PlantUML** (fourth pass, 2026-08-31) <https://claude.ai/artifact/Hbh234tvk3z2sk5qnd6bGp> | A theme can land PlantUML on Mermaid's measured values (participant box 151×65 against 150×65, message pitch 49 against 48, lifeline spacing 209 against 200) once one real defect is fixed: boxes sat 26 px adrift of their lifelines, because `Margin`'s vertical component goes between box and line. `Margin 0 25` fixes it. Method lesson: measure junctions, not only objects | The junction check, the levers (`participant { Padding 23 64 }`, `SequenceMessagePadding 8`, `conditionStyle InsideDiamond`, `rnote`), the traps (a trailing `''` comment on a variable line zeroes it, a `<style>` block beats skinparam, `MinimumWidth` strands the label). On a real Kronikol diagram, event notes, assertion hnotes, the step bar and dependency arrows keep their capture-time literal colours, "exactly as `THEME_PLAN.md` §2.6 predicts" (6.1, D16) | Three things no theme can do (note padding, a 3,3 dash on replies only, centred condition labels) are upstream asks |
| **What PlantUML Cannot Paint** (fifth pass, 2026-09-02) <https://claude.ai/artifact/WRRTVLi9BaDhdkLRryb29f> | The Mermaid skin over all nine shared diagram families. A 38-probe binding matrix; 16 gaps closed with no engine change, shipped in `puml-theme-mermaid-v2.puml`; the rest becomes 32 proposed style attributes in five tiers (A wire dead names, B new properties, C selectors, D layout, E language). Six engine truths | The engine truths as authoring rules for the harness (6.1). `skinparam actorStyle awesome` as a one-line approximation | The proposal is upstream, was never posted, and The Road to Server Parity says a DOM-native CSS lane "erases tier A wholesale" (D14) |

### D.3 Rendering and the engine

| Report | Argues | Taken from it |
|---|---|---|
| **The Road to Server Parity** (2026-09-08) <https://claude.ai/artifact/E95QQfh4pnTU9XMmMoSZsT> · this is "artifact `6a67776f`", parts one to nine | Full parity is four unequal projects. A day of plumbing (the local `ascii-probe` branch) takes the browser build from 22 to 35 of 37 families and from 1 to 10 output formats, with 72 of 148 outputs byte-identical to the jar. The hard remainders are raster output, math and byte-stable graphviz geometry. Download size is the real cost: the full engine is +16.3% gzipped. Tiers: sequence-floor 517 KB, github-tier 911 KB, stock 1,072 KB, full 1,252 KB gzipped. Closed dead ends: `AGGRESSIVE` (2.6 times larger), wasm-gc (+62% gzipped and blocked by CSP), CLDR, timezone and Smetana severing | For Kronikol: the engine integrity check and the immutable npm route (1.8); client-side PNG (11.1); `NodeJsPlantUmlRenderer` stays Node-primary; Blob-built workers get no V8 code cache, so the parse recurs on every report open; app-level delta patching of engine text is CSP-clean; the Teoz class contract would replace `DrewMessage` scraping. The upstream ladder is in section 5. **It does not hold the parity plan's missing sections** |
| **PlantUML Without a Server** (2026-09-04) <https://claude.ai/artifact/Astq4gcrxPEGfH9UnaUfRs> | "Blocked on both." The renderer gap is about a year of ordinary Java work, and the URL contract is permanent: 24,320 public files hotlink plantuml.com and camo fetches server to server. Seven blockers, twenty major gaps, five migration paths of which the hybrid is "the realistic answer". No JS-against-Java parity harness exists | Per-viewer geometry means report SVG cannot be golden-pinned across machines (3.3). Kronikol already works round the engine being DOM-bound and non-reentrant with its own worker shims. The 8192 `maxSvgSize` trap is why Kronikol passes `maxSvgSize`. Eight upstream fixes "worth doing regardless". Done since: the stdlib loader #2873, the CSP Smetana fallback #2861, themes degrading with a warning, Salt |
| **plantuml.com Ad Audit** (2026-09-04) <https://claude.ai/artifact/MwRpq5P5ywkSRnXFjCiriu> | The ad provider is Ezoic on a legacy nameserver proxy. The component most likely to trip a corporate firewall is a 111-byte root service worker importing a web-push ad SDK, live since June 2022. On one docs page in two minutes: 4,328 requests, 544 third-party cookies, 83% of page weight ads, bidding before consent | One more reason Kronikol should send nothing to that site (11.1). Everything else is advice to its maintainer and is to travel alone, never with an engine PR (section 5) |
| **Kronikol Render Bench** (2026-08-22) <https://claude.ai/artifact/SnRvAwNxs9AVcABvNf8vVb> and **Browser Render Worker Plan** (2026-08-22) <https://claude.ai/artifact/2Eu2QRigZBAti1p9bSjMqx> | On a real 20-diagram report, a full render went from 28 to 46 s down to 6 to 10 s, main-thread blocking from 22 to 39 s down to about 0.2 s, a note toggle from 3.9 to 7.1 s down to about 0.5 s. Levers by impact: Web Workers, four workers, smaller fragments, a source-keyed cache with prefetch, the engine build | All shipped (3.0.45, 3.0.50, 3.0.76). Not done: the fragment-height re-measure (1.8) and the leftovers in Appendix C. Measured and gave nothing: eight workers, lazy Graphviz loading, `Error.stackTraceLimit=0`, hand-patching the compiled engine |

### D.4 Background only

**Two Eras of Gherkin** (2026-08-27) <https://claude.ai/artifact/U2cjFNnu2HtRbu6thXse7P>. An audit of a
consumer's Reqnroll suite (213 scenarios, 17 feature files, about 9,700 lines of step definitions)
against that repository's own 16-rule readable-tests skill. It makes no recommendation about
Kronikol. Three things in it bear on this file, all INFERRED: a real consumer publishes Kronikol's
Specifications output to readers outside the team, which supports the living-documentation gap
section 0 describes at Reqnroll; "setup that lives in a Background is setup the reader of that block
cannot see" is worth one check that Background steps show in each scenario block (3.0.48, 3.0.78,
3.0.81 should already cover it); and its ClickHouse acceptance gate, "same scenarios, both stores,
diff the reports", is the recipe in section 6.
