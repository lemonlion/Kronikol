# Agent "Sweep tests, port, wiki, plans" result (2026-10-09)

## Key for #162
- Generated reports never pass stats: callers ReportGenerator.cs:413, ComponentDiagramReportGenerator.cs:48, MergeableReportRenderer.cs:45 -> edge has no link: "{proto}: {methods} - N calls across M tests". CapLinkText no-op there. Only cap: ComponentDiagramGenerator.cs:253-258 TruncateLabel(label, 2000 - (aliases+40)); comment :244-252 "defensive ceiling... unmeasured".
- A long UNLINKED component edge was never measured or rendered: S0 "unlinked" scan covered only request shapes (tools/render-bench/results/statement-limits-worker-2026-09-26.txt:22, :111-117). Every long-edge render in E2E + probe uses the stats (linked) form.
- #162 not mentioned anywhere in plans/CHANGELOG/ROADMAP/V5_PLAN/wiki/K4J.

## Tests
- tests/Kronikol.Tests/ComponentDiagram/ComponentDiagramGeneratorTests.cs: :470-491 EdgeLabel_IsCappedAtTheStatementLimit (formatter 6,000 x; asserts <= MaxMessageStatementChars + "…"; comment :473-475 "limits are unmeasured"); :1230-1241 Wrapping_StillHonoursTheStatementLimit (900 x "operation", edge <= 2000); :1270-1301 S0 theory ManyMethods_KeepTheInternalFlowLinkClosedAndShortEnoughForTheWorker(bool useC4) (80 routes with stats, comment :1275-1278 quotes 1,050 warm / 475 no JIT); wrapping tests :1184, :1215, :1243, :1257, :1303; :1200-1213 pins exact "HTTP: GET, POST - 252 calls across 241 tests".
- tests/Kronikol.Tests/PlantUml/PlantUmlStatementLengthTests.cs: :110-118 pins all five constants (:117 = 350); :271-282 comment "The worker draws an unlinked label at every length up to the message limit"; :478-506 corpus invariant, no component diagram.
- tests/Kronikol.Tests.EndToEnd/LongStatementRenderingTests.cs: [Collection(PlaywrightCollections.Reports)] (:15); RenderLongLinkedLabelReport :110-122 calls _renderDiagramsInContainer(document.body) (component section hidden), waits .plantuml-browser[data-plantuml] data-rendered==='1' (120 s, polling 200); AssertDrawn :124-130 (no "Maximum call stack size exceeded", no "Render error", SVG present, expected text "calls across"); :143-160 own Chromium Args ["--js-flags=--no-opt --no-maglev"], OpenPageAsync 1920x1080, asserts mode=="worker"; :162-180 --jitless. Fixture ReportTestHelper.cs:309-334: 30 ClickHouse ops WITH stats, useC4:false (linked form).
- tests/Kronikol.Tests.PlantUml.Ikvm/IkvmStatementLimitTests.cs: 4 facts, sequence only; sibling ComponentDiagramLabelWidthTests.cs:175 comment "The statement cap allows a label of nearly 2000".
- NodeJsPlantUmlRendererTests.cs: Integration trait + SkipWhen(!IsNodeAvailable()); pins :427-564; only component fact :941-970 (jitless, short edge).
- Component E2Es all short edges: ComponentDiagramPageTests.cs, WebAssemblyOffRenderingTests.cs:25-54, DependencyColoringTests, ComponentDiagramDatabaseToggleTests, MergedReportTests:48, OnDemandRenderingTests:353, ToggleDefaultsTests:387-408, WikiGifTests:954.
- Goldens: .NET none for whole component source; exact pins GeneratorTests:1212, ComponentDiagramNameWrappingTests.cs:26. K4J goldens kronikol4j-diagram/src/test/resources/parity/component.puml (longest line 90), kronikol4j-report/.../component-diagram-report.html.

## Probes (tools/render-bench)
- statement-limits-worker-probe.js: legacy mode :19-65; --scan :85-196 reads linked-*.puml, finds line with [[#iflow-, cuts link text with JS TruncateLabel copy (:68-83); FROM/TO/STEP defaults 300/1975/25; env COLD=1 (fresh page per case, CONCURRENCY), JSFLAGS, BROWSER, LAUNCH, UNLINKED=1 (drops link, :108), ASIS=1, PINNED. Verdicts :142-163 STACK-PICTURE / ERROR-PICTURE / drawn / drawn-once / STACK.
- emitter-corpus/Program.cs: --linked-labels <dir> (:25-84) five request shapes, a loop, linked-component.puml (30 gRPC methods WITH stats, useC4:false); --shim <page> <workers>.
- Results: statement-limits-worker-2026-09-26.txt (summary :10-25, component rows :177-201, after-fix ASIS :203-288), -2026-09-25.txt, statement-limits-2026-09-22.{txt,json} (node).
- Rerun (README.md:57-75): build tests/Kronikol.Tests.EndToEnd first (probe uses its Playwright driver); `dotnet run --project tools/render-bench/emitter-corpus -- --shim shim1.html 1` (0 = main); `-- --linked-labels linked`; `COLD=1 node tools/render-bench/statement-limits-worker-probe.js --scan linked shim1.html npm=https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8`; JSFLAGS="--no-opt --no-maglev".
- GAP: probe cannot express #162's shape; needs a new corpus source (no stats, more methods, > ~1,150 chars).

## Kronikol4J
- C:/Code/Kronikol4J/kronikol4j-diagram/src/main/java/io/kronikol/diagram/component/ComponentDiagramGenerator.java: label :157-169 (stats :165-166, default :168), emitted raw :179-190; NO wrapping, NO link cap, NO statement cap; title unwrapped :117.
- Ledger: C:/Code/Kronikol4J/docs/REMAINING_PARITY.md "## .NET-side features shipped after this audit (divergence ledger)" (:1877). Format: `- **<what> (.NET <ver>, <date>, <plan/issue>).** <verdict>. <detail>`; commits "Ledger: .NET <ver> ...". Related :2012-2027 (3.0.48 caps), :2232-2247 (3.30.4: "Not mirrored, a ledger entry only (D11 is still unanswered)"; "The port emits both links ... and caps neither"). No entry for 3.0.83 label wrapping.

## Wiki (local C:/Code/Kronikol.wiki 24 commits behind origin/master; quote via git show origin/master:)
- Large-Response-and-Diagram-Handling.md "Statement-Length Limits (3.0.48+)" :26-63; table :39-46 no component-edge row; :46 link text row says main thread, node, Firefox, WebKit draw every length; :52 lists other capped emitters; :61 "Without internal-flow tracking a request label keeps the 2,000-character limit."
- Component-Diagrams.md:342 "Label length cap (3.0.48+): component-diagram edge labels are defensively capped at the sequence-diagram message limit (2,000 characters of whole statement; the component parser's own limits are unmeasured)..." -> becomes false. Also :206-214 (stats link 350), :344 wrapping note.
- PlantUML-Browser-Rendering.md :108 "every component diagram draws in each of those configurations" (true only for short edges); :352 names only coloured bar as stack-overflow failure.
- Claims "unlinked draws to message limit": PlantUmlStatementLimits.cs:132-133; CHANGELOG.md:3145-3150; PlantUmlStatementLengthTests.cs:274-275; ENGINE_PIN_PLAN.md:382, :592, :1152.

## LONG_LINE_SYNTAX_ERROR_PLAN.md
- Origin: 5,410-char Redis DELETE arrow -> Syntax Error over fragment. Limits per statement kind on whole trimmed statement. `\n` counts two chars. §2.6 ComponentDiagram.html "not scanned". §6 Q2 (:232-234): "Component diagrams use a different parser with its own limits — unmeasured. Worth a short probe." NEVER DONE. PLANS_STATUS :61 "~95% (Java mirror open)".

## PLANS_STATUS.md
- Columns `| Plan | Status | Shipped in |` (:18); newest rows at top (:20). Row style: `| \`X_PLAN.md\` *(new, 2026-10-08)* | <status prose> | <versions> |`.
- House plan structure (GRPC_IDENTITY_PROPAGATION_PLAN.md #134): # title; ## 0. Summary; ## 1. How far each claim was checked; ## 2. Where it stands today (2.1 code, 2.2 measurements); ## 3. Findings the issue does not state; ## 4. The design; ## 5. Tests, red first; ## 6. Slices, releases and records (6.1 Slices, 6.2 Releases, 6.3 Changelog drafts, 6.4 Wiki, 6.5 Doc comments, 6.6 Kronikol4J consumers acceptance, 6.7 Before declaring done); ## 7. Where it sits in the roadmap; ## 8. Found on the way, not in this plan; ## 9. Questions, with recommendations; ## 10. Assumption ledger; ## 11. Log; ## Appendix A. Edit sites at `<sha>`.
- SHOULDLY header: `**Date:** 2026-10-07 · **Repo version:** 4.9.0 (\`main\` at 417c8e58; line numbers are for that commit) · **Status:** ...` then Evidence labels: **RUN** (measured...; harness dir), **READ**, **INFERRED**, **ESTIMATED**. Issue quoted as > block.

## CHANGELOG
- `## [Unreleased]` exists (:7-13), only ### Documentation (template README bullet). 4.14.4 at :15-40 ("**Patch - ...**" paragraph then ### Fixed).
- 3.30.4 at :3129-3188; header "**Patch - a long label inside an internal-flow link no longer costs its diagram (...S0...).** Both changes are fixes, so the patch part moved. The limit they add, `PlantUmlStatementLimits.MaxLinkedLabelChars`, is internal, so nothing new is public. ..." Component bullet :3151-3158 calls 30-op ClickHouse edge "user-reported" (wrong per ENGINE_PIN_PLAN:1174).

## ROADMAP / V5
- ROADMAP.md:632 (§6 Deliberately not scheduled): "| `LONG_LINE_SYNTAX_ERROR_PLAN` §6 Q2, component-diagram parser limits | A defensive ceiling is in place | A report of a lost component diagram |" -> #162 IS that trigger.
- :950 Appendix C: stats labels restore-or-API-only = owner's choice. :240 row 1.8 done.
- V5_PLAN.md:33 "0b" drop C4 in v5; open question 8 (:234-239) removes five unread component options unless stats restored.
