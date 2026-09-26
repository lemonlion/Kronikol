# Diagram colours, and an honest theme option (stage 1, P3)

**Date:** 2026-09-22 · **Repo version:** 3.27.2 (`2843018a`) · **Status: executed** (green-lit
2026-09-25), in two releases: 3.29.6 (S3a, S4, S5 and F26) and 3.30.0 (S1, S2 and S3b). The escapes it
measured and deferred followed as 3.30.1, an audit of all three as 3.30.2 (§12.4), and a second audit, which
drew every kind of captured text on both engines, as 3.31.2 (§12.5). §12 is the execution log. This is P3 of [`STAGE_1_PLAN.md`](STAGE_1_PLAN.md),
`ROADMAP.md` item 1.6, written as its own plan. §10 is the assumption ledger: what was **RUN** against
the pinned engine on 2026-09-22, what was only **READ**, and what is still open.

**Revised the same day, second pass.** The first pass measured the third item in a bench page whose real
`document` answered the engine's script request; the second pass ran it through the shipped worker host
and the Node renderer, where a theme is not ignored but **hangs the render** (F9, F10). S3 is rewritten
around that: the fix is a fail-fast mock DOM plus the diagnostic, not the diagnostic alone. The same pass
ran the link-binding path on the engine's own output (F14), found the component diagram's links on the
same path (F12), and answered I1, I4, I5 and I6.

**Status at 2026-09-25.** The repo is at 3.29.3 (`b74c8ddc`). Seven releases shipped since the base
commit, all from other plans (3.27.3 to 3.29.3). None touched the three defects, the engine pin, the
mock DOMs, `DiagnosticKind` (it still ends at `ReportRotationFailed`) or the tool's answer-affecting set,
and the 130 tag lines in 14 test files are unchanged. The only cited files that moved are
`ReportGenerator.cs` (12 to 39 lines down), `ReportConfigurationOptions.cs` (3 down) and the wiki's kinds
table (5 down); their citations below are updated. This plan's own release was written as 3.28.0, with
3.27.3 for the patch-only split; both numbers are taken. The third pass's two-release shape reads
**3.29.6** for the patch and **3.30.0** for the minor. The toolbar audit (another session) took 3.29.4 and
3.29.5: 3.29.4's release build failed at restore for a reason unrelated to it, so 3.29.5 (`bb262d37`)
fixed the restore and carries 3.29.4 to NuGet. Both numbers move again if another release lands first. This
file and its harness are still untracked, although the committed `STAGE_1_PLAN.md` and
`PLANS_STATUS.md` rows link to it.

**Third pass, 2026-09-25.** Both earlier passes assumed that `themes.js` is the only file the engine
loads by script tag. It is not (F15). The engine loads OpenIconic icons (`<&name>`), emoji (`<:name:>`)
and stdlib modules the same way, and Kronikol's payload escaper lets the first two through. A captured
body quoting Rust's `Vec<&str>` therefore leaves its diagram undrawn in the **default** configuration,
with no theme set (F16). One hung render also stops every render after it in the same worker or Node
process (F17), so when that diagram is the first to render, the report draws nothing. Following the same
path turned up two more defects in the files S3 edits:
- Step, test and assertion text is not escaped at all (F19), and every LightBDD table-parameter step loses
  its `<$name>` from the diagram.
- The Node renderer's SVG is not XML (F20). XML bodies paint nothing by default, and with internal-flow
  tracking off a bare `&` breaks the whole image.

The theme hang itself is now dated: 3.0.76 turned the audit's silent no-op into a hang (F21). The plan
gains two slices, S4 and S5, and splits S3 into its hardening (S3a) and its diagnostic (S3b). It now
recommends two releases, with the fixes that lose diagrams first (Q3, Q9). Everything new was RUN through
Kronikol's own emitter, the shipped worker host and the shipped Node script (§1, R19 to R28).

**I3 run, 2026-09-25.** The one investigation left open needed a candidate build. S1 was built as §3
writes it, in a throwaway worktree (`DIAGRAM_COLOURS_PLAN.harness/s1-candidate.patch`), and its reports
were merged with reports from today's build and from 3.1.0 (R29). The two header forms behave
identically in one merged page. The same inputs merged by today's tool break every header behaviour on
the new form, so the check can fail. The run found two defects outside this plan:
- A request label long enough to be truncated leaves its whole diagram undrawn in the browser worker
  (F25, handed to P4).
- "Copy all caller request payloads" is never offered with the default coloured arrows (F26, Q11).

The P4 session reported two things the same day, and both are in: S5 turns an existing test red (§5.7),
and P4's engine has a loader hook of its own (§5.3).

Covers the three parts of roadmap item 1.6, which the PlantUML Theme Audit (2026-08-30, artifact
`Ay7qjfLMhhfTx2r2uVgCzU`) found and `THEME_PLAN.md` §2.3, §2.6 and §2.7 folded into the theme work:

1. **The grey note header** (`<color:gray>`, painted `#808080`) is below AA on every note it lands on.
2. **The internal-flow link hover restore** writes `#000000` instead of the text's own colour.
3. **`PlantUmlTheme` is silent** when it does nothing, which is in the default rendering mode.

None of the three depends on a theme shipping, which is why they leave `THEME_PLAN` and ship first. The
stage plan's reason for keeping this apart from P2 (the toolbar) stands: P2 moves CSS bytes, this moves
diagram bytes.

Measuring the third item found two defects the roadmap row does not name:
- **S4:** text Kronikol copies from a test or a payload reaches the same script loader the theme does.
- **S5:** the Node renderer writes SVG that is not XML.

They are here because they share a mechanism, files and probes with S3. They are not part of the ask,
so Q9 asks the owner whether they stay.

**Standing rules this plan is written under:** CLAUDE.md's TDD cycle (every slice lists its red tests
first), its Playwright rules for `tests/Kronikol.Tests.EndToEnd/` (no forced clicks, `PollingInterval =
200`, `dispatchEvent` for SVG hover), the versioning rule (the highest-ranking change decides the bump),
and the lesson of `plan-recommendations-are-not-green-lights`: the ask is the three items above; the
measured neighbours (§6) are recorded, not folded in. S4 and S5 are bugs in shipped code, which
CLAUDE.md says to fix when found, not features. They are in the plan, but they stay only if the owner
says yes to Q9.

---

## 0. Summary

Six slices. The recommendation (Q3, revised in the third pass) is two releases:
1. First a **patch**, 3.29.6, with everything that loses a diagram (S3a, S4 and S5).
2. Then a **minor**, 3.30.0, with the two colour fixes and the honest option (S1, S2 and S3b).

What makes the second release a minor is Q2 (§9). A dedicated diagnostic kind is new public surface,
which fits the roadmap's "possibly a minor". Reusing `Other` would make it a patch, and F7 says why that
is the worse patch.

| Slice | What | Files | Bump on its own |
|---|---|---|---|
| S1 | The header ink becomes a computed value, `#686868`, that clears 4.5 : 1 on both fills it lands on; the twelve script sites that read the tag as a marker accept the old and the new form | `PlantUmlCreator.cs`, `collapsible-notes-script.js`, `context-menu-script.js`, 14 test files | patch |
| S2 | Link text rests at the colour of the text around it and is highlighted in its own painted colour; blue text that is not a Kronikol link is left alone | `plantuml-browser-render-script.js`, E2E | patch |
| S3a | The two mock DOMs (worker host, Node script) answer a `<script>` append with `onerror`. A theme, a stdlib `!include`, an OpenIconic icon or an emoji then fails at once, instead of hanging its render and every render queued behind it in the same engine. The page explains a loader failure instead of printing a raw Java exception | `plantuml-worker-host.js`, `plantuml-render.js`, `plantuml-browser-render-script.js` (`describeEngineFailure`), the `RenderMany` doc comment | patch |
| S3b | A theme set under `BrowserJs` or `NodeJs` produces a `DiagnosticKind.OptionNotApplied` entry, a console line and an honest doc comment. The wiki says the same everywhere it mentions the option | `DiagnosticEntry.cs`, `ReportGenerator.cs`, `ReportConfigurationOptions.cs`, `ComponentDiagramOptions.cs`, `DiagramsFetcherOptions.cs`, 5 wiki pages plus a code sample | minor (new enum member, new schema value) |
| S4 | Text that Kronikol copies in from a payload or a test cannot reach the loader. The payload escaper also escapes `<&`, `<:` and `<$`, and the same three are escaped in the step label, the test delimiter and both assertion-note builders | `PlantUmlCreator.cs`, `StepBarPlantUml.cs`, `TrackingDiagramOverride.cs`, `Track.cs`, `InteractionRecord.cs`, `gen-vectors.js`, 2 wiki pages | patch |
| S5 | The Node renderer escapes what it serializes, so its SVG is XML. XML bodies paint, and a `&` no longer breaks an image | `plantuml-render.js` | patch |

**What this plan found that the roadmap row does not say.** The row was written from the audit and
never re-checked at HEAD (`ROADMAP.md` 1.6: "PLAN: neither was re-checked at HEAD"). Re-checking
changed the shape of S2 and S3 and settled two of the open choices:

| # | Finding | How known | Where used |
|---|---|---|---|
| F1 | **The browser build paints a link as blue text with no `<a>` element.** `[[#iflow-x GET /orders]]` and `[[https://example.com docs]]` both come out as `<text fill="#0000FF">` and nothing else. So the render script's blue-text path (`plantuml-browser-render-script.js:1057-1118`) is the **live** path for every internal-flow link under `BrowserJs`, not a fallback for old engines, and the `<a>` path above it (`:1037-1056`) is reached only by Java-rendered SVG (`Server`, `Local` with inline SVG) | RUN, §1 R3 | §4 |
| F2 | **The hover blackout is latent in every shipping configuration.** Under `BrowserJs` the theme is ignored (F6), so body text is `#000000` and a restore to `#000000` is invisible; under `Server` and `Local` the engine emits anchors and the CSS path (`internal-flow-popup-styles.css:149-152`) never writes a fill. It becomes visible the day `THEME_PLAN` 6.1 makes a theme reach the browser engine. The fix is still right and small; the plan says plainly that on today's default it is inert | RUN + READ | §4.1 |
| F3 | **What is live today is the collateral.** The same eager recolour (`:1062-1068`) blacks out **every** `#0000FF` text in a diagram that has internal-flow links: fields emphasised with `FocusEmphasis.Colored` (`JsonFocusFormatter.cs:243`, also painted `#0000FF`), and any `[[https://…]]` hyperlink in a note. Permanently, not on hover. Narrow (two opt-in features together), real, and fixed by the same change | RUN, §1 R12 (F14) | §4.1 |
| F4 | **The header is below 4.5 on every fill it lands on, and worst on the event note.** `#808080` on the default note `#FEFFDD` is 3.87, on the event note `#CFECF7` is 3.20. One neutral grey, `#686868`, clears 4.5 on both (5.46 and 4.51). The lightest grey that clears the default note alone, `#757575`, fails the event note at 3.73, so "clears AA on the default note" is not enough of a target | RUN (arithmetic), §1 R4 | §3.2 |
| F5 | **The tag is a marker, not only a colour.** Twelve sites in two scripts match `^<color:gray>` literally to find header lines: hide-headers, copy text, the JSON⇄YAML toggle, split-note continuation, context-menu copy. Changing the emitted tag without them is a silent break of five features, and merged reports carry sources from older runs, so the old form has to stay readable | RUN (grep), §1 R5 | §3.3 |
| F6 | **A theme is one console warning and byte-identical output only where a real `document` answers the engine's script request.** The engine never fetches `themes.js` itself: the pinned build has no `import()`, `import.meta`, `importScripts` or `currentScript`. It reads `globalThis.PLANTUML_THEMES` and, when that is absent, appends `<script src="themes.js">` to `document.head` and waits for `onload` or `onerror`. In the bench page the 404 fired `onerror` and the engine logged `PlantUML: themes.js could not be loaded, so '!theme cerulean' was ignored. Serve themes.js next to the page or register globalThis.PLANTUML_THEMES (importing themes.js as a module does this).` and drew the diagram unthemed. The CDN tag behind the pin **ships** `themes.js` (326,345 bytes) beside `plantuml.js`; nothing in Kronikol loads it, by `THEME_PLAN` §2.1's design (embed one theme, never fetch the bundle). With it served, the same engine applies `cerulean` (lifeline strokes `#BABDBF`, geometry moves). 6.1's gate is registration in the worker, and the diagnostic S3b adds must be **removed** when 6.1 lands; §5.4's guard test fails on that day | RUN, §1 R1, R15, R18 | §5 |
| F7 | **`Other` is in the tool's answer-affecting set** (`QueryCommand.cs:364-376`, `AnswerAffectingDiagnostics`). A cosmetic warning filed under `Other` would be printed as a provenance line at the head of every `kronikol query` answer on that report. That is the concrete reason Q2 recommends a dedicated kind over the cheaper bump | RUN (read of the set) | §5.2, Q2 |
| F8 | **Golden churn is narrower than the grouping feared.** No stored `.puml`, `.svg` or `.html` golden pins the tag; what pins it is 130 assertion lines across 14 test files, most of them hand-written E2E fixture sources that can stay as they are and become the legacy-form coverage, plus two mergeable JSON fixtures that are inputs, not expectations. The byte-identity baseline (`ToggleDefaultsBaselineTests`) compares three outputs of the same run and does not move | RUN (grep), §3.5 | §3.5 |
| F9 | **In the shipped worker host a themed diagram never renders.** The host's mock `document.head.appendChild` stores the script element and fires nothing, so the engine waits for a load that never completes. The render queue gives the item up after 60 s without writing anything (`data-rendered` never set, the element left empty, no message, `:972-976`); the worker posts an error after its own 150 s timeout, which the shim discards as already given up; telemetry counts one error. Measured: the themed diagram unrendered after 200 s, beside two plain diagrams drawn in under a second on the same page. In a themed report every source carries the directive, so the first render never completes and the lazy workers, gated on the first completion (`:176`, `:190`), never start: **no diagram of a themed `BrowserJs` report is drawn.** The audit's "silently ignored", the roadmap row, the stage-0 memory and the wiki's "the theme text never reaches the worker" are all wrong in the same direction (the audit was right when written: F21) | RUN, §1 R12 (the single-diagram hang), R24 (a page of six themed diagrams: none drawn in 400 s) | §5.1 |
| F10 | **Under `NodeJs` every themed diagram fails after 20 s.** Same mock-DOM shape in `plantuml-render.js` (`head: new MockElement('head')`), same wait, ended by the script's own poll: `ERROR: Timed out waiting for SVG render (20000ms)`, twice with two theme names, 25 s each. `RenderNodeBatchIsolated` turns each into a `RenderFailure` diagnostic (in the tool's answer-affecting set, F7) and a placeholder. The warning the engine would log goes to stderr, which the renderer surfaces only when the process itself fails. The third pass measured the batch the library actually runs (F17): 24.3 s per diagram here (nominal 20 s), so a forty-diagram themed report spends about sixteen minutes producing forty placeholders. From about 75 diagrams the process reaches `RenderMany`'s 30-minute cap and is killed, and every diagram becomes a placeholder with no retry | RUN, §1 R13, R23 | §5.1, Q4 |
| F11 | **One hook makes both mocks honest, and it fixes three more hangs.** A `head.appendChild` that calls the script's `onerror` on the next tick (prototyped as `_onAppend` on the worker host's head, and the same three lines on the Node mock) makes the themed render complete in the worker in 2.8 s, byte-identical to the unthemed one, with the engine's warning forwarded to the page console; in Node in 389 ms with the warning on stderr. `!include <C4/C4_Context>`, which `ComponentDiagramReportGenerator.cs:42-45` avoids because "the C4 flavour hangs the Node renderer until its timeout", hangs the shipped worker the same way (never rendered, R12) and goes from a 20 s timeout in Node, and never in the worker, to a 253 ms and a 2.8 s render on the same patch: the stdlib loader is the same script-tag path. So do the OpenIconic and emoji loaders (F15), and the hook ends the poisoning of later renders too (F17) | RUN, §1 R12, R14, R21 to R23 | §5.3 |
| F12 | **The component diagram's own links take the same blue-text path.** `ComponentDiagramGenerator.cs:234` writes `[[#iflow-rel-<caller>-<service> HTTP: GET, POST]]` arrow labels; under `BrowserJs` and `NodeJs` that page renders through the same engine and shim, `extractIflowMap` (`:438-447`) already strips the label's `\n`, and the component palette carries no `#0000FF`, so every blue text there is a link. S2's change covers it and its E2E must include one | RUN (grep), READ (script) | §4.2, §4.3 |
| F13 | **Playwright forwards a Blob worker's console to the page.** `console.warn`, `error` and `log` from a Blob worker arrive on `page.on('console')` and `context.on('console')` (Playwright 1.59.1, the E2E project's driver). A test can assert the engine's warning without reaching into the worker | RUN, §1 R16 | §5.4 |
| F14 | **The bind path does what §4.1 read, now run on the engine's own output.** After `bindIflowLinks`: `GET` and `/orders` `#000000`, the `<color:blue>` field `#000000`, the `[[https://…]]` hyperlink `#000000`; `mouseenter` on `GET` gives `#0000FF underline` on the two link texts only; `mouseleave` gives `#000000` again. Text order in the painted SVG: autonumber, the link's texts, the note's lines top to bottom, the next arrow's autonumber. So a focus-coloured text is DOM-adjacent to a link only when it is the **first painted line of the request note**, which needs the note to carry no header block and the body's first line to be a focused field; a JSON body starts with `{`. I2 is answered by structure, not by counting | RUN, §1 R12 | §4.2 |

**Third pass (2026-09-25): what else reaches the loader.** Both earlier passes looked for the theme,
so they found the theme. This pass asked the engine what it loads.

| # | Finding | How known | Where used |
|---|---|---|---|
| F15 | **The theme is one of four bundles the engine loads by script tag.** The loader `EK_` has six call sites: three for a stdlib module (`!include <lib/…>` and the stdlib lookups), one each for `themes.js`, `openiconic.js` (`<&name>`) and `emoji.js` (`<:name:>`). Each first reads a global (`PLANTUML_STDLIB*`, `PLANTUML_THEMES`, `PLANTUML_OPENICONIC`, `PLANTUML_EMOJI`), and nothing under `src/` registers any of them. The loader keeps its state per URL, so a load left `loading` holds every later request for the same bundle behind it | RUN (grep of the pinned build), §1 R19 | §5.1 |
| F16 | **Captured payloads reach the loader on the default configuration.** `EscapeCreoleMarkup` escapes `<` only before a letter, `/` or `#` (`IsCreoleTagStart`, `PlantUmlCreator.cs:559`), so `<&`, `<:` and `<$` from a payload reach the engine as markup. Two sources written by Kronikol's own emitter were run through the shipped worker: a 400 whose JSON body quotes Rust's `Vec<&str>` (the OpenIconic syntax), and a request carrying `<:rocket:>` (the emoji syntax). Neither was ever drawn: after 200 s the element was empty, telemetry showed `errors: 2`, and the console was silent. Under Node each times out after 20 s. `<$foo>` (the sprite syntax) does not hang, but it is silently dropped from the note. No theme is involved: this is `BrowserJs`, the default | RUN, §1 R20, R21 | §5.5 |
| F17 | **One hung render stops every render after it in the same engine.** In the worker, with the payload diagram first on the page, nothing on the page was drawn in 400 s. The lazy workers wait for a first successful render that never comes, and the one worker, freed by its host's 150 s timeout, hangs again on a plain diagram (`workers: 1`, `renders: 0`, `errors: 2`). In the Node batch, the mode `RenderMany` uses with one process per report, every diagram after a hung one times out, plain ones included: six diagrams, 146 s, no SVG. With the hook the same batch takes 0.7 s and only the two payload diagrams fail. `RenderMany`'s doc comment says a failing diagram "never affects the others" (`NodeJsPlantUmlRenderer.cs:78-79`), which is wrong for a hang. The P4 session saw the same through the shipped worker with the pool already started (`ENGINE_PIN_PLAN.md` §1.14 to §1.16, reported, not re-run here): with one worker nothing was drawn for 100 s after an icon diagram; with four, the icon diagram and an emoji diagram each held a worker and the rest drew on the other two. By the same mechanism, as many hanging diagrams as there are workers would leave every later diagram undrawn (inferred from R22, not run) | RUN, §1 R22, R23 | §5.1, §5.3 |
| F18 | **A themed page draws nothing (F9's whole-report claim) was RUN.** Six themed diagrams on one page, 400 s: none drawn, `workers: 1`, `renders: 0`, `errors: 2`, no console message | RUN, §1 R24 | §5.1, §10.2 |
| F19 | **Step, test and assertion text is not escaped at all.** None of these is escaped: the step label (`StepBarPlantUml.Build`, kept "byte-identical to what shipped before"), the test identifier (`TrackingDiagramOverride.cs:78`), and assertion text (`Track.cs:440-450`, `InteractionRecord.cs:407-415`). LightBDD puts `<$name>` in a step name where a table parameter goes, so every LightBDD step with a table parameter loses the reference from its diagram bar: built through the real `StepBarPlantUml.Build`, the bar paints `Given I have data [inputs: ""]`. A step whose inline parameter value carries `<&x>` hangs exactly as a payload does. `~<` works in every one of these forms (styled bar, coloured bar, assertion note, note): each paints `<$inputs>`, `Vec<&str>` and `<:rocket:>` literally, in about 350 ms | RUN (the bar, the escapes); READ (the LightBDD `<$name>` convention, from the repo's own LightBDD fixtures), §1 R25 | §5.5 |
| F20 | **The Node renderer's SVG is not XML.** `serializeElement` (`plantuml-render.js:124-141`) writes text nodes and attribute values unescaped. `NodeJs` inlines the SVG when `InternalFlowTracking` is on (the default: `ReportGenerator.cs:259-265` forces `InlineSvgRendering`), and there an XML body's text lands inside `<order>` and `<result>` elements and paints 0 px wide: **XML payloads show blank note lines**. When tracking is off, each SVG is a `data:image/svg+xml` image, and a bare `&` makes it fail to parse and fail to load (`naturalWidth` 0). That `&` can be a query string such as `?page=1&size=10` in the arrow label, or `fish & chips` in a JSON string, and the whole diagram becomes a broken image. `kronikol ingest --render nodejs` and the component diagram take the same path. The worker host escapes (`escText`, `escAttr`), so `BrowserJs` is not affected | RUN, §1 R26 | §5.7 |
| F21 | **Each hang arrived with an engine pin.** The worker host and the Node batch arrived in 3.0.45, whose pin (`v1.2026.3beta6-patched`) already loaded `emoji.js` and stdlib by script tag. 3.0.50's pin (`v1.2026.6-patched`) added `openiconic.js`, and 3.0.76's (today's) added `themes.js`. On both older pins the themed probe renders byte-identical to the plain one in 0.4 s, so from 3.0.45 to 3.0.75 a theme under the JS engines was exactly the silent no-op the audit (2026-08-30) described. 3.0.76 (2026-09-04) made it a hang. The audit and the roadmap row were overtaken, not mistaken | RUN (the two older pins from the CDN, today's Node script), §1 R27 | §5.1, §7.2 |
| F22 | **The wiki's own configuration sample produces a report with no diagrams.** `Report-Configuration.md:26` sets `PlantUmlTheme = "spacelab"`, and three lines further down sets `PlantUmlRendering = PlantUmlRendering.BrowserJs`, the two together. It also sets `FocusEmphasis.Colored` and `InternalFlowTracking = true`, the F3 pair | READ | §7.2 |
| F23 | **Kronikol4J has the option.** `DiagramOptions.plantUmlTheme` and `ComponentDiagramRenderOptions.plantUmlTheme` both write `!theme`. The port's pin is `v1.2026.3beta6-patched`, where a theme is a silent no-op and `<&name>` is dropped (RUN, R27), and it has no creole escaper at all. §7.4's "has neither … nor a `PlantUmlTheme` option" was wrong and is rewritten | READ + RUN | §7.4 |
| F24 | **With the hook alone, a loader failure reaches the reader as a Java exception.** For `<&name>` or `<:name:>` the engine writes `java.lang.RuntimeException: Failed to load openiconic.js` (or `emoji.js`) as text. `describeEngineFailure` (`plantuml-browser-render-script.js:892-920`) recognises only the too-large and syntax cases, so the reader gets the raw exception and no source. After S4 no payload takes that path, but a user's own `InsertPlantUml` markup does, and that markup rendered its icons under `BrowserJs` before 3.0.50 | RUN, §1 R21 | §5.3 |
| F25 | **Found while running I3: a truncated request label loses its whole diagram in the browser.** When a label is too long, the emitter caps the message statement at `PlantUmlStatementLimits.MaxMessageStatementChars`, 2,000 characters, counting the `[[#iflow-…]]` link that internal-flow tracking (the default) wraps around it (`PlantUmlCreator.cs:352-363`). Through the shipped worker, that statement is not drawn: the element holds the engine's text `java.lang.RuntimeException: (JavaScript) RangeError: Maximum call stack size exceeded`, the render telemetry counts no error, and every other call in the test is lost with it. Cut to shorter lengths the edge is not a number: with the link, 1,000 and 1,800 characters drew, and 1,200, 1,400, 1,500, 1,600, 1,700, 1,900 and 2,000 did not. Without the link, 1,500, 1,800 and 2,000 drew. The Node renderer draws all twelve. So `[Full path]`, which `AppendFullPathToNote` adds only to a truncated label's note, is never seen in a default `BrowserJs` report. It is not a colour, theme or loader defect. The P4 session confirmed it the same day on both engine builds, with one worker (`tools/render-bench/results/statement-limits-worker-2026-09-25.txt`). With the link, 1,000 and 1,600 drew, and every other length from 1,100 to 2,000 failed on the fork and on npm 1.2026.8 alike, so the pin changes nothing. A loop label and a coloured bar carrying the same text drew at every length to 2,000. `ENGINE_PIN_PLAN.md` §1.11 and its Q9 now own it (§6) | RUN, §1 R30; P4's run | §6 |
| F26 | **Found while running I3: "Copy all caller request payloads" is never offered on a default report.** `extractCallerPayloads` (`context-menu-script.js:263-305`) finds the caller's requests by a plain `caller -> ` arrow. `SequenceDiagramArrowColors` is on by default (`ReportConfigurationOptions.cs:400`), so the emitter writes `caller -[#438DD5]> …`, and the menu item never appears. On the merged page it was offered on none of the 21 default-arrow diagrams that drew (three writers: 3.1.0, today's build and the candidate; the other two are F25's), and on both plain-arrow POSTs. Its one test (`NoteCopyFidelityTests.cs:103`) passes because its source is hand-typed with a plain arrow (`ReportTestHelper.cs:3598`). The site it runs through is one of S1's twelve (`:296`) | RUN, §1 R29; READ for the test | §6, Q11 |

---

## 1. How far each claim was checked

The engine probe: `tools/render-bench/core-1.2026.8beta1-0e4f452.js` is the stock build behind the
current pin (`TrackingDefaults.cs:28`, `v1.2026.8beta1-0e4f452`). Two sources were rendered through
it in headless Chromium on 2026-09-22 with the bench's own page shell (`render-svg.js`, plus a console
listener and a route that answers 404 for `/themes.js`, which is the Web Worker's situation: no
`document`, no theme bundle). The probe, its two sources, the three rendered SVGs and the contrast script
are kept in [`DIAGRAM_COLOURS_PLAN.harness/`](DIAGRAM_COLOURS_PLAN.harness/README.md), and §11 says how to
repeat it.

The second pass added a probe of the **shipped** path: `worker-probe.js` builds a page the way
`TestPageGenerator.GenerateBrowserJsSequenceDiagramPage` does (the shipped render script with
`DiagramContextMenu`'s substitutions, the worker host inlined, the engine fetched from the CDN into a Blob
worker), opens it from `file://` in headless Chromium, and reads what the page paints; `FAILFAST=1` runs it
again with the one-hook prototype of §5.3. The Node renderer was run as the library runs it, from its own
cache directory, with the shipped `plantuml-render.js` and with a copy carrying the same hook.

The third pass added four probes:
- `emit-payload-sources.fsx` writes sources through Kronikol's own emitter: `PlantUmlCreator` on the
  built `Kronikol.dll`, and `StepBarPlantUml.Build` by reflection, so each source is byte for byte
  what a report carries.
- `loader-probe.js` renders those sources, a page of themed sources, and a page whose first diagram
  hangs, through the shipped worker path, stock and with `FAILFAST=1`.
- `node-svg-probe.js` loads the Node renderer's SVG the two ways a report embeds it, as a data-URI
  image and inline.
- The two engines pinned before today's were fetched from the CDN and rendered with today's Node
  script.

| # | Claim | Status | Evidence |
|---|---|---|---|
| R1 | A theme set today under `BrowserJs` is ignored, with a console warning and no other trace | **RUN, true only on a main thread with a real `document`; false in the shipped worker (R12)** | `probe-themed.puml` (`!theme cerulean` after `@startuml`) in the bench page with `/themes.js` answering 404: `CONSOLE[warning] PlantUML: themes.js could not be loaded, so '!theme cerulean' was ignored. …`; the SVG, with processing instructions and comments stripped, is byte-identical to the unthemed render. With `themes.js` served (the bench directory carries one from the upstream PR work) the render changes: `viewBox 552×431` → `589×435`, lifelines `#181818` 0.5 px → `#BABDBF` 1 px. The first pass took this page for the report's situation; it is not (F9) |
| R2 | The fills the header lands on, and what the tags paint | **RUN** | Unthemed: note `path fill="#FEFFDD"`, event note `#CFECF7`, body `<text fill="#000000">`, `<color:gray>` → `#808080`, `<color:#686868>` → `#686868` (accepted, unclosed, colours the rest of the line exactly as `gray` does), `<color:lightgray>` → `#D3D3D3`, `<color:blue>` → `#0000FF` |
| R3 | Links are anchors the script can bind | **RUN, false** | `grep -c 'href=' probe.svg` → 0. `[[#iflow-abc123 GET /orders]]` is three `<text fill="#0000FF">` elements (`GET`, a space, `/orders`); `[[https://example.com docs]]` is one. F1 |
| R4 | The contrast numbers | **RUN** | WCAG 2.1 relative luminance with sRGB linearisation, computed for every Kronikol fill; the table is in §3.2 |
| R5 | The two emitter sites and the twelve marker sites | **RUN** (grep at HEAD) | `PlantUmlCreator.cs:454` (`AppendFullPathToNote`) and `:1280` (`BatchGray`, called from `:974` for the header block). Readers: `collapsible-notes-script.js:69, 268, 614, 853, 1017, 1414, 1435, 1792, 1802`; `context-menu-script.js:234, 245, 296`. `report-search-index.js:56` strips `<color…>` generically and needs nothing. No C# outside the emitter, and nothing in `Kronikol.Tool`, matches the tag |
| R6 | Where the option flows, and who could apply it | **RUN** (grep) | `ReportGenerator.cs:282` → `DiagramsFetcherOptions.PlantUmlTheme` → `DefaultDiagramsFetcher.cs:257` → `CreatePlantUmlPrefix` (`PlantUmlCreator.cs:641`, `!theme <name>` as the line after `@startuml`); `ComponentDiagramGenerator.cs:152` (after its skinparam block); `ComponentDiagramReportGenerator.cs:42-45` uses the JS engine for both `BrowserJs` and `NodeJs`. No renderer reads the option; the render script only classifies `!theme ` as a prefix line (`:475`). `Kronikol.Tool` never sets a theme (its options object is fixed, `IngestCommand.cs:23, 267`) |
| R7 | The diagnostic surfaces a new kind would have to reach | **RUN** (read) | `DiagnosticKind` (`DiagnosticEntry.cs`, last member `ReportRotationFailed`, "new kinds are appended, never renumbered"); the JSON schema enumerates kinds with `Enum.GetNames` (`ReportGenerator.cs:6151`), so a new member is in the schema without a second edit; `kronikol ingest --diagnostic` parses any kind name (`IngestCommand.cs:403`); `query summary`'s inventory lists every kind, and `AnswerAffectingDiagnostics` (`QueryCommand.cs:364-376`) is a separate, curated set that includes `Other` (F7) |
| R8 | The script's blue-text path is what runs, and what it does | **RUN** (second pass, R12) | `plantuml-browser-render-script.js:1031-1118`, read in full: `bound === 0` after the anchor pass, `extractIflowMap(source)` non-empty, then every `#0000ff` text is set to `#000000` and stripped of `text-decoration` (`:1062-1068`) before any group is matched. Run on the engine's own output through the shipped script with a truthy `window.__iflowSegments['iflow-abc123']`: the fills in F14 are what it leaves behind, and `mouseenter`/`mouseleave` behave as read |
| R9 | A captured payload cannot start a line with a live colour tag | **READ** | `EscapeCreoleMarkup` / `IsCreoleTagStart` (`PlantUmlCreator.cs:484-559`): `<` followed by a letter, `/` or `#` is escaped, so `<color:#686868>` in a body cannot reach the note as markup. Kronikol's own tags are added after escaping (`AppendFullPathToNote`'s comment). §3.4 adds the test that pins it |
| R10 | The Java port | **READ** (third pass: re-read at the port's `e2a77ba`, and its pin RUN in R27) | `Kronikol4J/…/NoteFormatter.java:158-163` mirrors `BatchGray` with `<color:gray>`; the port has no `[Full path]` block (it arrived in .NET 3.0.48, `af4cfdec`; the port's fixtures are 3.0.43); its script copies are the 3.0.43 assets (render script and collapsible notes; no worker host, no Node renderer) and already diverge from .NET's (`diff` of the render script: 1,782 lines). It **does** have the option: `DiagramOptions.plantUmlTheme` and `ComponentDiagramRenderOptions.plantUmlTheme`, written as `!theme` (`ComponentDiagramGenerator.java:114-115`); its pin is `v1.2026.3beta6-patched` (`DotNetHtmlReportRenderer.java:71-72`); it has no creole escaper. Ledger entry in §7.4 |
| R11 | The wiki's current wording | **READ** | `PlantUML-Browser-Rendering.md:279` ("No effect: the theme text never reaches the worker") and `:321` ("the diagram renders unthemed (the engine logs a console warning)") were written at stage 0 from the same main-thread picture and are **wrong** at HEAD (F9). `Diagram-Customisation.md:137-156` still says a theme "changes colours, fonts, and styling"; `Report-Configuration.md:125`, `Component-Diagrams.md:128, 272-283, 679` and `API-Reference.md:5-6` describe the option with no caveat; `Report-Configuration.md:26`, the options sample, sets a theme beside `BrowserJs` (F22, third pass); `Diagnostics-and-Debugging.md:90-105` is the kinds table |
| R12 | The shipped worker path with a plain, a themed, a trailing and an `!include <C4/C4_Context>` diagram; the bind path on the plain one | **RUN** (twice) | `worker-probe.js`, headless Chromium, `file://` page, engine from the CDN (fetch 216 ms), mode `worker`. Plain: rendered at +978 ms, lifelines `#181818`, viewBox `0 0 341 349`. Themed: not rendered within 200 s, element empty, `data-rendered` unset. Trailing: rendered on a second worker. Include (second run): not rendered within 70 s, element empty; telemetry `errors: 2`, one per hung diagram, `workers: 3`. Fills after bind, on `mouseenter` and on `mouseleave`: F14. Zero console messages in both runs: the host silences `console.log`, and the engine never reached its warning |
| R13 | The Node renderer with and without a theme, and with `!include <C4/C4_Context>` | **RUN** | Shipped `plantuml-render.js` from `%LOCALAPPDATA%/Kronikol/plantuml-js/v1.2026.8beta1-0e4f452/`, node 25.9.0: `probe.puml` → 6,216-byte SVG in 452 ms; `probe-themed.puml` → exit 1, no SVG, `ERROR: Timed out waiting for SVG render (20000ms)`, 25 s; `!theme plain` the same; the C4 include the same 20 s timeout |
| R14 | The fail-fast hook, both hosts | **RUN** | Worker host copy with `mockDocument.head._onAppend` firing the script's `onerror` on the next tick (`FAILFAST=1`): the plain, themed, trailing and `!include <C4/C4_Context>` diagrams all rendered by +2.8 s, one worker, `errors: 0`, the themed SVG byte-identical to the plain one (`worker-themed.failfast.svg` vs `worker-plain.failfast.svg`, PIs stripped), the plain SVG identical to the stock run's, the include an SVG of `viewBox 0 0 413 117` (the engine's own picture), and `page.on('console')` received `[warning] PlantUML: themes.js could not be loaded, so '!theme cerulean' was ignored. …` from the worker. Node copy with the same hook on `head`: themed → 6,216 bytes in 389 ms, identical to unthemed, the warning on stderr; the C4 include → a 1,331-byte SVG in 253 ms |
| R15 | What the CDN tag ships beside the engine | **RUN** | jsDelivr's file listing for `gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.8beta1-0e4f452`: `plantuml.js` 3,946,817 B, `themes.js` 326,345 B, `viz-global.js`, `emoji.js`, `openiconic.js`, `README.md`; `HEAD /themes.js` → 200 |
| R16 | Whether Playwright surfaces a worker's console | **RUN** | A Blob worker calling `console.warn`, `console.error`, `console.log`: each arrives on `page.on('console')` and on `context.on('console')`; Playwright 1.59.1 (the E2E project's driver package) |
| R17 | Whether any consumer reads the tag from the JSON (I6) | **RUN** (grep) | `BreakfastProvider`: matches only under `java/tests/*/target/` (Kronikol4J's own run outputs and fragments), no source file; `Kronikol4J`: the port's emitter, its two 3.0.43 script copies, and its parity fixtures. No consumer greps the tag from `TestRunReport.json` |
| R18 | How the engine reaches for its theme bundle | **RUN** (grep of the pinned build) | `PLANTUML_THEMES` is read through `globalThis` (`BR$`, `CC7`); the string table carries the warning and `"themes.js"`; the only script loader is `EK_`, which does `document.createElement('script')`, sets `onload`/`onerror`, and `document.head.appendChild(s)`; the build contains no `import(`, `import.meta`, `importScripts` or `currentScript` |
| R19 | Everything the engine loads by script tag | **RUN** (grep of the pinned build, third pass) | `EK_` is reached only through `AUj`, which has six call sites: three stdlib (the library name plus a fixed suffix, for `!include <…>` and the stdlib lookups, which read `PLANTUML_STDLIB`, `PLANTUML_STDLIB_INFO`, `PLANTUML_STDLIB_JSON`), `themes.js`, `openiconic.js` (`E(8678)`; the creole pattern `<(#\w+)?&([-\w]+)…>`, lookups through `PLANTUML_OPENICONIC`) and `emoji.js` (`<(#\w+)?:([0-9a-z][0-9_a-z]*):…>`, `PLANTUML_EMOJI`, `PLANTUML_EMOJI_SHORTCUT`). State is kept per URL in `window.__pl_script_state` (`loading`, `loaded`, `error`); a request that finds `loading` queues its callbacks and waits. No file under `src/` sets any `PLANTUML_*` global. The CDN tag ships `openiconic.js` 51,021 B and `emoji.js` 1,874,010 B beside `themes.js` |
| R20 | What the payload escaper lets through | **RUN** (Kronikol's emitter, `emit-payload-sources.fsx`, on the built `Kronikol.dll`) | `IsCreoleTagStart` (`PlantUmlCreator.cs:559`) is `/`, `#` or an ASCII letter. A 400 with body `{"error":"expected Vec<&str>, found String"}` is emitted as the note line `"error": "expected Vec<&str>, found String"`; `<:rocket:>` passes the same way. Every text payload takes this path (`escapePayload = !IsBinaryContent(content)`, `:913`): bodies of every format, headers (`BatchGray`), the full path, action notes |
| R21 | Those payloads through the shipped worker and Node script | **RUN** (`loader-probe.js`; Node single mode) | Worker, stock: `d-rust` and `d-emoji` never drawn in 200 s, elements empty, `errors: 2`, zero console messages; the control, the two escaped variants and a trailing diagram drawn by +4.2 s on other workers. Node, stock: exit 1 after 25.2 s and 24.5 s, `Timed out waiting for SVG render`. With the hook, both hosts: the engine writes `java.lang.RuntimeException: Failed to load openiconic.js` (`emoji.js`) as the diagram's text in under 0.5 s. Escaped as S4 would (`Vec~<&str>`, `~<:rocket:>`): drawn in both hosts, painting `Vec<&str>,` and `<:rocket:>` as captured. `<$foo>` and `<$aws/Compute/EC2>` never hang: the note paints `"tpl": "a ` and ` b"`, the reference gone |
| R22 | A hung render and the renders queued behind it, worker | **RUN** (`loader-probe.js`, `SET=poison`) | `d-rust` first, then three plain diagrams: nothing drawn in 400 s, `workers: 1`, `renders: 0`, `errors: 2`: the host's 150 s timeout for `d-rust`, then another for the plain `d-control` that the freed worker took next. `firstDone` is set only on a `done` message (`plantuml-browser-render-script.js:176`) and gates every further worker (`:190`), so none started. With the hook: all four settled by +1.2 s, three drawn, `d-rust` holding the engine's text |
| R23 | A hung render and the renders after it, Node batch | **RUN** (`--batch`, the mode `RenderMany` spawns) | Six lines, the payload one first: all six `Timed out`, plain and themed ones included, 145.8 s, no SVG. With the hook: 684 ms; `1-rust` and `3-emoji` fail with the engine's text, the other four draw (the themed one 6,216 B with the theme warning on stderr). 24.3 s per timeout here (400 polls of a 50 ms timer, Windows timer resolution). `RenderMany` caps the process at min(30 min, 60 s + 25 s × N) (`NodeJsPlantUmlRenderer.cs:100-106`); a cap hit throws, and `RenderNodeBatchIsolated` then gives every diagram a placeholder and runs no retry batch (`DefaultDiagramsFetcher.cs:381-419`, READ) |
| R24 | A page whose every source is themed | **RUN** (`loader-probe.js`, `SET=allthemed`) | Six themed diagrams, 400 s: none drawn, `workers: 1`, `renders: 0`, `errors: 2`, `inFlight: 1`, zero console messages |
| R25 | Step, test and assertion text | **RUN** (`StepBarPlantUml.Build` by reflection, `emit-payload-sources.fsx`; Node) | The step label is not escaped (`StepBarPlantUml.cs:62-68, 112-117`; callers `StepCollector.cs:84-99`, `InteractionRecord.cs:398-402`), nor the test identifier (`TrackingDiagramOverride.cs:78`), nor assertion text (`Track.cs:440-450`, `InteractionRecord.cs:407-415`). The LightBDD adapter fills parameters from `FormattedValue` (`StepTrackingStepDecorator.cs:61-76`), and the repo's LightBDD fixtures show a table parameter formatted as `<$name>` (`FeatureResultExtensionsTests.cs:448-465`, `ParameterRenderingReportTests.cs:225, 331`). The bar built for `Given I have data [inputs: "<$inputs>"]` with a table paints `Given I have data [inputs: "` `"]`. A bar quoting `Vec<&str>` times out in Node (23.6 s) and fails at once with the hook. `~<` in the styled bar, the coloured bar, an assertion note and a note: each paints `<$inputs>`, `Vec<&str>` and `<:rocket:>` literally, no tilde, 336 to 355 ms. `<U+003C>` in a bar label also works (the text is there), but only `~<` works in all four forms and matches the payload escaper |
| R26 | The Node renderer's SVG as a report embeds it | **RUN** (`node-svg-probe.js`) | `serializeElement` (`plantuml-render.js:124-141`) writes text and attribute values as they are. `payload-amp` (`?page=1&size=10`, `fish & chips`): `DOMParser` as `image/svg+xml` reports `EntityRef: expecting ';'`; the data-URI `<img>` fires `error`, `naturalWidth` 0; inline, both texts paint (the HTML parser tolerates a bare `&`). `payload-xml`: well-formed (the body's tags parse as elements), the image loads; inline, the texts `A1` and `true` sit in `<order>` and `<result>` elements, painted width 0. `payload-control`: well-formed, loads, paints. `InternalFlowTracking` on (the default, `ReportConfigurationOptions.cs:148`) forces `InlineSvgRendering` for `NodeJs` (`ReportGenerator.cs:259-265`); off, `DefaultDiagramsFetcher.cs:358-360` makes a data URI. The worker host's serializer escapes (`plantuml-worker-host.js:44-49`) |
| R27 | When each loader, and each hang, arrived | **RUN** (the two earlier pins fetched from the CDN, rendered by today's `plantuml-render.js`) | 3.0.45 (`05b3e321`: the worker host and the Node batch) pinned `v1.2026.3beta6-patched`: script-tag loaders for `emoji.js` and stdlib, OpenIconic built in, no theme loader. 3.0.50 (`64209fc5`) pinned `v1.2026.6-patched`: `openiconic.js` joins. 3.0.76 (`a5477f78`) pinned `v1.2026.8beta1-0e4f452`: `themes.js` joins. On both older pins `probe-themed.puml` is byte-identical to `probe.puml` (0.4 s), and `include-probe.puml` (`!include <C4/C4_Context>`) times out (27.2 s and 28.0 s). On `3beta6`, `payload-rust` draws with `<&str>` dropped (`expected Vec, found String`) and `payload-emoji` times out |
| R28 | The hook, and S5's escaping, on renders that load nothing | **RUN** (the render-bench corpus `corpus-hash.js` selects: 22 diagrams, from 3 messages to 500) | Node `--batch`: stock, hooked, and hooked with a `serializeElement` that escapes, 22 of 22 byte-identical across all three, about 3 s each. Worker (`loader-probe.js`, `SET=corpus`): stock and `FAILFAST=1` hash identically, 22 of 22 (33 renders with fragments, 2,252,474 cache bytes in both). The same escaping prototype makes `payload-amp` well-formed (the image loads, 670 px) and paints `payload-xml`'s body as text (185 and 181 px) |
| R29 | Old and new header forms in one merged report (I3) | **RUN** (a candidate S1 build, `s1-candidate.patch`, in a throwaway worktree; `emit-merge-inputs.fsx` on it and on today's build; each build's own `kronikol merge`; `merge-probe.js` in Chromium) | The inputs are three scenarios per writer (headers with JSON bodies, a headers-only request, a truncated path), written by today's build (11 `<color:gray>` tags per report) and by the candidate (11 `<color:#686868>`). Each writer ran with the default settings, with arrow colours off, and with internal-flow tracking off. The two 3.1.0 fixtures add 17 old-form diagrams. On the candidate's merged pages, every old/new pair agrees on every behaviour column (nine pairs). The new form paints `#686868`, the old `#808080`, and no page logs an error. Merged by today's tool, the new form fails every header behaviour: no note offers the YAML button; the tag reaches Copy box text and the collapsed note's tooltip; every header stays painted after hide-headers; the collapsed preview carries the headers; and with plain arrows the payload copy carries the tag and a headers-only request is offered as a payload. The 17 fixture rows are identical on both pages. `[Full path]` was seen only with internal-flow tracking off, because with it on, the truncated-path diagram is not drawn (F25) |
| R30 | The capped message statement, through the shipped worker (F25) | **RUN** (`loader-probe.js`, `SET=statement`, four workers; the source is `payload-longpath.puml`, today's emitter on a GET with a 2,300-character query; the Node batch on the same twelve sources) | The emitter caps that statement at exactly 2,000 characters, `…]]` included. Cut to each length with the cap's ellipsis: with the `[[#iflow-…]]` link, 1,000 and 1,800 drew, and 1,200, 1,400, 1,500, 1,600, 1,700, 1,900 and 2,000 were not drawn. Each of those elements holds `java.lang.RuntimeException: (JavaScript) RangeError: Maximum call stack size exceeded`, with telemetry `errors: 0`, because the engine writes the failure into its target. Without the link, 1,500, 1,800 and 2,000 drew. The Node renderer drew all twelve, in 1.1 s. One run, not a bisection. An edge that is not monotonic in length fits a stack limit rather than a parser limit, as `ENGINE_PIN_PLAN.md` §1.11 found for block labels in Node, here lower, in a Chromium worker |

---

## 2. Slice order and why

Two releases (Q3; the third pass reversed the earlier "one release"), each in one working tree:

1. **3.29.6, a patch: no diagram is lost to the renderer.** S3a, then S4, then S5.
   - S3a comes first because it is the safety net under the other two. With it, a source that still
     reaches the loader (a merged report from an older release, a user's own markup) costs one diagram,
     shown with a legible failure, instead of the page.
   - S4 makes captured and test text draw as written, and S5 makes the Node output parse.
   - None adds public surface. S3a moves no bytes of an ordinary render (R14, R28). S4 moves only sources
     whose text carries `<&`, `<:` or `<$`. S5 moves only Node SVG whose text or attributes carry `&`,
     `<`, `>` or `"`.
   - The release fixes the default configuration (F16, F17), `NodeJs` (F20) and the theme hang (F9,
     F10) at once.
2. **3.30.0, a minor: the colours, and the honest option.** S1, S2 and S3b, ordered as before.
   - S1 moves diagram bytes and re-pins tests, and S2 is verified on top of that re-pinned suite.
   - S2's E2E uses S1's fixture work.
   - S3b's guard test (§5.4) renders a themed report through the real engine, with the hardening
     already shipped.

The alternative is one release carrying all six. It saves one release pass, but a blank diagram on the
default configuration then waits behind the contrast re-pin and the new public kind. If only one item
ships, it is S3a: without it, a report can have no diagrams at all (F9, F17).

**Shared files.** S1 and S4 both edit `PlantUmlCreator.cs` (`:454` and `:1280`, against `:559`). S3a
and S2 both edit the render script (`describeEngineFailure` against `bindIflowLinks`). S3a and S5 both
edit `plantuml-render.js` (the mock `head` against `serializeElement`). No two of these edits share a
line.

**Overlap with P5** (`INTERNAL_FLOW_BLOB_PLAN.md`, its "File overlap with P3" note): both plans edit
`bindIflowLinks`. P5 changes one membership line (a segment listed as hidden is treated as absent) and
keeps the function synchronous; S2 here rewrites the blue-text half's colour handling (`:1062-1117`) and
does not touch the membership test. Whichever lands second rebases onto the other; the two edits do not
share a line. Neither plan should re-pin the other's E2E fixtures.

**Overlap with P4** (`ENGINE_PIN_PLAN.md`, reported by its session on 2026-09-25 and checked against the
file): P4's S3 also edits `plantuml-render.js`, in `loadEngineWithCodeCache` (`:243-272`, a checksum for
`plantuml.js.v8cache`). S3a edits the mock `head` (`:172`) and S5 edits `serializeElement` (`:124-141`),
so no line is shared. P4's pin also changes what S3a is up against; §5.3 says how.

---

## 3. S1 — the header ink

### 3.1 What is wrong

Every request and response note carries its headers as `<color:gray>[key=value]` lines (`BatchGray`),
and a request note carries the untruncated path under `<color:gray>[Full path]`
(`AppendFullPathToNote`). PlantUML paints `gray` as `#808080`. On the default note fill that is
3.87 : 1, on the event note 3.20 : 1. WCAG 2.1 AA asks 4.5 : 1 for text under 18 pt; the note body is
13 px. The header is meant to read as secondary, and it does: black body text on the same fills is
20.56 and 16.99, so there is room to be both compliant and clearly quieter than the body.

### 3.2 The computed value

Measured, WCAG 2.1 relative luminance with sRGB linearisation (§11 has the script):

| Foreground | default note `#FEFFDD` | event note `#CFECF7` | pass bar `#D4EDDA` | fail bar `#F8D7DA` | setup `#F6F6F6` | white page |
|---|---|---|---|---|---|---|
| today `#808080` | 3.87 | **3.20** | 3.18 | 2.96 | 3.65 | 3.95 |
| `#757575` (lightest clearing the default note) | 4.51 | 3.73 | 3.71 | 3.45 | 4.52 | 4.61 |
| **`#686868` (lightest clearing both note fills)** | **5.46** | **4.51** | 4.49 | 4.17 | 5.61 | 5.57 |
| `#666666` (the same with margin) | 5.62 | 4.65 | 4.63 | 4.30 | 5.77 | 5.74 |
| body `#000000` | 20.56 | 16.99 | 16.93 | 15.72 | 19.43 | 21.00 |

Header lines land on two fills: PlantUML's default note and Kronikol's event note (`AddEventStyling`,
`PlantUmlCreator.cs:708`). They never land on the assertion bars, the step bar or the setup partition;
those columns are there so the number is on record for `THEME_PLAN` §2.3, which will derive a header
ink per theme and must not land below this floor (§7.5).

**The value is computed, not typed in**, so a change to either fill moves the header with it and a test
says by how much:

```csharp
// src/Kronikol/PlantUml/NotePalette.cs (new, internal)
internal static class NotePalette
{
    /// <summary>PlantUML's own note fill, unthemed. Measured on the pinned engine (DIAGRAM_COLOURS_PLAN §1 R2).</summary>
    internal const string DefaultNoteFill = "#FEFFDD";
    /// <summary>What <c>AddEventStyling</c> gives the event note.</summary>
    internal const string EventNoteFill = "#CFECF7";
    /// <summary>WCAG 2.1 AA for text under 18 pt. The note body is 13 px.</summary>
    internal const double HeaderContrastFloor = 4.5;
    /// <summary>
    /// The lightest neutral grey that clears the floor on every fill a header line lands on, found by
    /// walking down from today's <c>gray</c> (#808080). Pinned by NotePaletteTests as #686868.
    /// </summary>
    internal static readonly string HeaderInk = WcagContrast.LightestGreyClearing(HeaderContrastFloor, 0x80, DefaultNoteFill, EventNoteFill);
    internal static string HeaderTag => "<color:" + HeaderInk + ">";
}
```

`WcagContrast` (new, internal, ~30 lines): `Ratio(fg, bg)` per WCAG 2.1 (`c/12.92` below 0.04045, else
`((c+0.055)/1.055)^2.4`; `L = 0.2126R + 0.7152G + 0.0722B`; `(Lmax+0.05)/(Lmin+0.05)`), and
`LightestGreyClearing(floor, from, fills)` walking `g` from `from` down to 0 and returning the first
`#gggggg` whose minimum ratio over `fills` is at or above the floor. The walk starts at today's grey so
the answer can never be lighter than what ships now.

`AppendFullPathToNote` (`:454`) and `BatchGray` (`:1280`) use `NotePalette.HeaderTag`. `BatchGray`
keeps its name: the Java port names its mirror after it, and a rename buys nothing.

### 3.3 The twelve marker sites

The tag is how the report scripts recognise a header line (F5). Each literal `^<color:gray>` becomes
one regex that accepts the old form and any hex form:

```js
var NOTE_HEADER_TAG = /^<color:(?:gray|#[0-9A-Fa-f]{6})>/;      // once per file
```

- `collapsible-notes-script.js`: `:69, 268, 853, 1017, 1414, 1435, 1792, 1802` test the trimmed line
  and use the constant directly; `:614` strips it after leading whitespace and becomes
  `l.replace(/^\s*<color:(?:gray|#[0-9A-Fa-f]{6})>/, '')`.
- `context-menu-script.js`: `:234, 296` strip after whitespace, `:245` tests the trimmed line.
- The two files cannot share a constant without a page global; each carries the literal, and
  `DiagramContextMenuTests` pins that both carry the same one (§3.4).

**Why the hex class and not the exact value.** The scripts already take baked constants
(`DiagramContextMenu.cs:61-65, 112-114`), so `__NOTE_HEADER_INK__` could bake `#686868` and the regex
could be exact. Two things argue against it: a merged report (`kronikol merge`) renders sources from
several runs, which after 6.1 may carry several inks; and `gray` has to stay in the alternation anyway
for sources written before this release. The hex class is safe because Kronikol's other own tags are
named colours (`blue`, `lightgray`, `white`) and a captured payload cannot start a line with a live tag
(R9, pinned by a test).

The C# search normaliser and `report-search-index.js:56` strip `<color…>` tags generically: no change,
one new vector (§3.4).

### 3.4 Tests (red first)

Unit, `tests/Kronikol.Tests/PlantUml/`:

- `NotePaletteTests` (new): `HeaderInk` is `#686868`; `WcagContrast.Ratio` gives 3.87 for
  `#808080` on `#FEFFDD` and 3.20 on `#CFECF7` (the audit's numbers, so the formula is the audit's);
  the ratio of `HeaderInk` on each fill is at or above 4.5; walking from `0x80` with the default fill
  alone returns `#757575` (so the second fill is what moves the answer, and a reader of the test sees
  why).
- `PlantUmlCreatorTests`: the twelve assertions naming `<color:gray>` move to `NotePalette.HeaderTag`;
  one new case: a header block and a `[Full path]` block on the same note both carry the tag, and no
  `<color:gray>` remains anywhere in emitted source (`Assert.DoesNotContain("<color:gray>", …)` over
  the full-fixture output, the regression guard for a third site appearing later).
- `PlantUmlCreatorTests`, R9: a captured body whose line begins `<color:#686868>x` is escaped by
  `EscapeCreoleMarkup` and does not begin with a live tag after `FormatNoteContent`.
- `NoteCopyFidelityTests`: the rejoin cases run on emitter output (they already do) and pass unchanged
  once the strippers accept the hex form; one added case feeds a source in the **old** form to the
  same strip-and-rejoin path, which is the merge case (I3).
- `SearchNormalizerEquivalenceTests`: one vector with `<color:#686868>[k=v]`, asserted equal between
  the C# and JS normalisers, as the `gray` vectors are. The shared vectors are generated, not typed:
  the case goes into `tools/search-bench/gen-vectors.js` and is regenerated into
  `tests/shared-vectors/search-index-vectors.json`, which `SearchIndexVectorTests`, the Jint tests and
  the Java port all pin (the port's `README.md:178` says the vectors moved with it; §7.4).
- `DiagramContextMenuTests`: the seven assertions on the scripts' literal regex text (`:169-198`) move
  to the new literal; one added assertion that both scripts carry the same `NOTE_HEADER_TAG` source
  text (drift guard).

Playwright, `tests/Kronikol.Tests.EndToEnd/` (paint level, `PollingInterval = 200`):

- `NoteHeaderContrastTests` (new): a report generated through the real emitter (a fixture with headers,
  a full path, and one event note); after render, read the header `<text>` fill and the enclosing note
  `path` fill from the painted SVG, compute the ratio in-page, assert ≥ 4.5 on the request note and on
  the event note, and assert the header fill is not the body fill (it still reads as secondary). This
  is the "measure what the display paints" test the design memory asks for, and it is the one that
  fails if the pin move (P4) changes PlantUML's default note fill.
- `HeadersDetailsInterferenceTests` (12 lines), `NoteYamlInternalsTests` (5), `LargeNoteSplitTests`
  (3) and the two `ReportTestHelper` fixture files (47 and 33 lines): the hand-written sources move to
  `NotePalette.HeaderTag` so they track the emitter, **except** one fixture per script behaviour
  (hide headers, copy text, JSON⇄YAML, split-note continuation, context-menu copy) which keeps the
  old `<color:gray>` on purpose and is renamed `…_legacy_source` with a comment saying why: it is the
  merged-report coverage. R29 ran that case end to end on a candidate build, and these fixtures are its
  regression guard. The context-menu payload site (`:296`) is reachable only with arrow colours off
  (F26), so its legacy fixture keeps a plain `caller ->` arrow, as today's does.

### 3.5 What moves, and what does not

| Pins today | Count | Action |
|---|---|---|
| `tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs` | 47 | constant, minus the legacy fixtures |
| `tests/TestTrackingDiagrams.Tests.EndToEnd/ReportTestHelper.cs` | 33 | same |
| `PlantUmlCreatorTests.cs` | 12 | constant |
| `HeadersDetailsInterferenceTests.cs` | 12 | constant |
| `DiagramContextMenuTests.cs` | 7 | new regex text |
| `NoteYamlInternalsTests.cs` | 5 | constant |
| `HeadersDetailsInterferenceReportTests.cs`, `SearchNormalizerEquivalenceTests.cs`, `LargeNoteSplitTests.cs` | 3 each | constant; one vector added |
| `ToggleDefaultsMarkupTests.cs`, `ToggleDefaultsBaselineTests.cs`, `SearchIndexVectorTests.cs`, `ScenarioTruncateAndCollapsibleNotesTests.cs`, `NoteCopyFidelityTests.cs` | 1 each | fixture inputs; stay as legacy-form sources |

The byte-identity baseline generates three reports in one run and compares them with each other: it
does not move. No `.puml`, `.svg` or `.html` golden under `tests/` carries the tag. Two mergeable JSON
fixtures do (`tests/Kronikol.Tests/TestData/Reports/all-passing.mergeable.json`, 11 lines, and
`failing-with-steps.mergeable.json`, 6): they are inputs to `RetainedRunsTests`, never compared against
the tag, and they stay as they are: they are old-form sources by definition and the material for I3. The
Java port's parity fixtures are pinned at 3.0.43 in the port's own repository and are not touched (§7.4).
No script reads `[Full path]` by name; only the tag in front of it.

---

## 4. S2 — the link hover restore

### 4.1 What is wrong

`bindIflowLinks` (`plantuml-browser-render-script.js:1031-1118`) has two halves. The first binds
`<a href="#iflow-…">` anchors; the second, entered only when the first bound nothing, finds link text by
its fill. Under `BrowserJs` the first half never binds anything (F1), so the second is the path every
internal-flow link takes.

That path does this, in order:

1. `:1062-1068`: **every** `<text>` whose fill is `#0000ff` is set to `#000000` and stripped of its
   underline, before any of it is identified as a link.
2. `:1069-1080`: consecutive blue texts are grouped; a group's text with whitespace removed is looked
   up in the map `extractIflowMap(source)` built from the `[[#iflow-… label]]` markup.
3. `:1081-1117`: a group that maps to a segment with data gets, in the default `ShowLinkOnHover`
   mode, `mouseenter` → `#0000FF` + underline and `mouseleave` → `#000000`; in `ShowLink` mode it gets
   `#0000FF` + underline back at once. Any other blue text stays black.

Three consequences:

- **The rest colour is a literal.** Under a theme whose text is not black, every link rests black,
  and after a hover it rests black again: the audit's "hovering a link permanently blacks out its
  text". Latent today (F2), visible the day 6.1 lands.
- **The highlight colour is a literal too** (`#0000FF` at `:1093` and `:1106`), so a theme's own
  link colour is overwritten on hover. Same day.
- **Blue text that is not a Kronikol link is blacked for good** (F3): `FocusEmphasis.Colored`
  paints focused fields `<color:blue>`, which is `#0000FF` (R2), and a `[[https://…]]` hyperlink in a
  payload is painted the same. In a diagram with at least one internal-flow link, both lose their
  colour on load. Live today, on the default theme, for the two opt-ins together. Observed (F14): after
  the shipped script binds the engine's own output, the focused field and the hyperlink are `#000000`
  and stay so through a hover of the link; only the link's texts go blue and underlined on `mouseenter`.

The same function binds the **component diagram's** links (F12): `ComponentDiagram.html` under
`BrowserJs` or `NodeJs` renders `[[#iflow-rel-…]]` arrow labels through the same shim, so its link
texts are blacked on load and re-blued on hover by the same lines, and the fix below applies to it
without a second change.

### 4.2 The change

Same file, same function; the anchor half is untouched (it never writes a fill).

1. **No eager recolour.** Collect the blue texts and their original fill (`t.getAttribute('fill')`);
   change nothing yet.
2. **Only groups whose key is in the map are Kronikol's.** A group whose key is not in `iflowMap` is
   left exactly as painted: fill, underline, no handlers. That is the F3 fix. A group in the map whose
   segment has no data is *un-linked* as today (rest colour, no underline, no handlers): Kronikol
   wrote the markup and knows the popup would be empty, so it must not look like a link.
3. **The rest colour is read, not written.** `restFillFor(textEl)`: the fill of the nearest preceding
   `<text>` in the container that is not link-coloured; failing that, the most common non-link text
   fill in the container; failing that, `#000000`. On the default theme every branch answers
   `#000000`, which is what makes this change inert there (and the E2E in §4.3 pins that).
4. **The highlight colour is the captured original.** `mouseenter` sets the element's own painted
   fill back (`#0000FF` today, the theme's link colour after 6.1) plus the underline; `mouseleave`
   sets the rest colour and removes it. `ShowLink` mode restores the original fill and underline and
   adds the handlers, which on today's engine is a no-op restore.

`THEME_PLAN` §2.7's row for this site says "capture each link text's original fill before recolouring
and restore *that*". Read literally for the hover-only mode that restores **blue** on `mouseleave`,
which would leave every link blue after its first hover. The rest colour has to come from the text
around the link, which is what step 3 does; §7.5 records the correction against the theme plan. The
detection literal `#0000ff` at `:1063` stays: replacing it with a data attribute is §2.7's second row
and needs the palette that 6.1 introduces.

**Known limit, recorded, not fixed here:** a focus-coloured text immediately adjacent in DOM order to a
link text is merged into the link's group by the consecutive-index rule (`:1069-1080`), the merged key
matches nothing, and both are left as painted: the link is then blue but unbound. Today both are
blacked. How often (I2): the painted order is autonumber, the link's texts, the request note's lines
top to bottom, then the response's autonumber (F14), and only request arrows carry a link
(`PlantUmlCreator.cs:366`). Adjacency therefore needs the request note's **first painted line** to be a
focused field: no header block (all headers excluded, or none captured) and a body whose first line is
focused, which a JSON body (`{`) never is and a YAML body can be. Rare, opt-in on three counts, and
bounded. The clean fix is a link colour distinct from the focus colour (`skinparam hyperlinkColor`,
Q6), which is 6.1's business, not a patch's.

### 4.3 Tests (red first)

Playwright, `tests/Kronikol.Tests.EndToEnd/IflowLinkColourTests.cs` (new). Hover through
`dispatchEvent(new MouseEvent('mouseenter'))` on the `<text>` element, per the repo rule; every wait
`PollingInterval = 200`.

- **Real engine, default theme, inert by construction.** A report generated with internal-flow
  tracking and a segment with data (the `IflowPopupTests` fixture family): after render, the link
  text's fill is `#000000` and it has no `text-decoration`; on `mouseenter` it is `#0000FF` with an
  underline; on `mouseleave` it is `#000000` again. This is today's behaviour, pinned so the change is
  provably invisible on the default theme.
- **Synthetic SVG, non-black body (the theme-independent proof).** `TestPageGenerator` already builds
  pages with a hand-written SVG and an `__iflowSegments` map (`:340-348`); a variant paints body text
  `#E6EBE9` on a dark rect and the link text `#0000FF`, with the link's label in the page's source
  map. Rest fill is `#E6EBE9`, hover is `#0000FF` with underline, leave is `#E6EBE9`. This is the test
  that fails on HEAD (it reads `#000000`).
- **Blue text that is not a link keeps its colour.** Same page, one more `<text fill="#0000FF">` whose
  content is in no map: after binding it is still `#0000FF` and keeps its `text-decoration`.
- **Real engine, the live collateral (I1).** A report with `FocusEmphasis = FocusEmphasis.Colored`,
  a focused field, and an internal-flow link in the same diagram: after render the focused field's
  `<text>` fill is `#0000FF`. Fails on HEAD. This is the one test that proves F3 rather than reads it.
- **A mapped link with no segment data is un-linked**, as today: rest colour, no underline, no popup
  on click (`Missing_segment_shows_no_data_message` in `IflowPopupTests` covers the popup half; this
  covers the paint).
- **`ShowLink` mode**: the link is `#0000FF` with underline at rest and opens the popup on click.
- **The component diagram** (F12): `ComponentFlowPopupTests`' page rendered through the real engine;
  a relationship's link text rests `#000000`, is `#0000FF` and underlined on `mouseenter`, and opens the
  relationship popup on click. Today's behaviour, pinned so the shared function stays shared.

Unit: `DiagramContextMenuTests` gains one assertion that the bind function's source no longer contains
`setAttribute('fill', '#000000')`, so the literal cannot come back in a refactor.

---

## 5. S3, S4, S5 — the engine's script loader, and what reaches it

S3 is the roadmap's third item and is split in two. S3a stops the hosts hanging and ships in the first
release. S3b makes the option honest and ships in the second. S4 and S5 were found by following the same
path (the third pass) and ship with S3a (Q9). §5.2 to §5.4 are S3, §5.5 and §5.6 are S4, and §5.7 is
S5.

### 5.1 What is wrong

`ReportConfigurationOptions.PlantUmlTheme` (`:99-100`) says "PlantUML theme name to apply to all
diagrams". `PlantUmlRendering` defaults to `BrowserJs` (`:115`). What the option does in each mode,
measured (F6, F9, F10, F17, F18):

| Mode | Directive written | What the engine does with it | What the reader gets |
|---|---|---|---|
| `Server`, `Local` | yes | applies it, unvalidated (the audit: 12 of 43 themes lose their arrow labels on the report page, 4 lose the note controls) | a themed report, possibly a broken one |
| `BrowserJs` (the default) | yes | appends `<script src="themes.js">` to the worker host's mock `head` and waits for a load that never completes | **no diagram in the report is drawn** (RUN, R24). The elements stay empty with no message, the one worker hangs on every render it takes (R22), and the lazy workers never start |
| `NodeJs` | yes | the same wait in `plantuml-render.js`'s mock, cut by the script's poll: 20 s nominal, 24.3 s measured here | every diagram a placeholder, one `RenderFailure` each, and those head every `kronikol query` answer (F7). About 24 s per diagram, and from about 75 diagrams `RenderMany`'s 30-minute cap kills the process (R23) |

Before 3.0.76 the pinned engines had no theme loader, so a theme under both JS modes was the silent
no-op the audit described (R27). The pin move in 3.0.76 (2026-09-04), four days after the audit, made it
a hang.

**The theme is one of four bundles** (F15). The same loader serves stdlib modules, OpenIconic icons and
emoji, and captured text reaches the last two:
- Kronikol's payload escaper does not escape `<&`, `<:` or `<$` (F16).
- Step labels, test identifiers and assertion text are not escaped at all (F19).

A body quoting Rust's `Vec<&str>`, or a step whose inline parameter value does the same, therefore hangs
its diagram on the default configuration, with no theme set. Because one hung render stops every render after it in
the same engine (F17), a report whose first-rendered diagram carries one draws nothing. Under `NodeJs`,
every diagram after it in the report fails. `<$name>` does not hang, but it is dropped, which is how every
LightBDD table-parameter step loses its reference from the diagram.

**The Node renderer's SVG is not XML** (F20). XML bodies paint blank under `NodeJs` by default. With
internal-flow tracking off, a `&` in a URL or a body turns the whole diagram into a broken image.

`ComponentDiagramOptions.PlantUmlTheme` (`:19-20`) says the same and behaves the same, with the added
defect that the component generator writes its skinparam palette first and the theme after it
(`ComponentDiagramGenerator.cs:152`), so where the theme does apply it discards the dependency-type
colours. That ordering is theme-dependent and stays with 6.1.

The audit's options were: make it work, throw, or warn; "silently ignoring it is the worst of the
three". Measured, it is not the worst: hanging is. Two of the three are still out:

- **Throwing changes a default.** A consumer whose options carry a theme today gets a report; after a
  throw they get no report. CLAUDE.md calls a changed default a MAJOR, and the wiki's rule for report
  generation is "diagnostics, never a reason for a run to fail".
- **Making it work is 6.1**, with one correction to what "the work" is. The engine is ready and the CDN
  tag even ships the bundle (F6). What 6.1 has to do is register `globalThis.PLANTUML_THEMES` in the
  worker host and the Node host before the first render, by embedding the one selected theme
  (`THEME_PLAN` §2.1), and then validate 43 themes against Kronikol's notes and page. Fetching the CDN's
  `themes.js` into the worker instead would be a few lines and is not taken: Q8.

So, four things:
- The hosts stop waiting for a load that cannot happen (S3a).
- Text Kronikol copies in stops reaching the loader (S4).
- The Node output becomes XML (S5).
- A diagnostic says the option did nothing, until 6.1 makes it work (S3b).

### 5.2 The kind

Q2 has the choice. The recommendation is a new member:

```csharp
/// <summary>
/// A configured option had no effect in this run's configuration. The message names the option, why it
/// did nothing, and what would apply it. One entry per option per run; the run itself is unaffected.
/// </summary>
OptionNotApplied,
```

appended after `ReportRotationFailed` (never renumbered). It is general on purpose but this plan gives
it **one** emitter; the other options `BrowserJs` cannot honour (`PlantUmlImageFormat`,
`LocalDiagramRenderer`, `LocalDiagramImageDirectory`, `PlantUmlServerBaseUrl`, already listed as
ignored on the wiki) are candidates for later and are recorded in §6, not added here.

Why not `Other`: F7. `Other` is in the tool's answer-affecting set and means "a host's entry with no
kind yet"; a library-emitted configuration warning under it would head every query answer on the
report as provenance. A dedicated kind is not in that set (it changes nothing about what the report
holds) and still appears in `summary`'s inventory, where it belongs.

### 5.3 The change

**S3a, the hardening, first release.**
- **Where.** In `plantuml-worker-host.js` the mock `head` is created at `:172`; in `plantuml-render.js`,
  the mock document's `head: new MockElement('head')` is at `:172`.
- **The change.** Appending a `<script>` element to `head` schedules its `onerror` on the next tick,
  with a message saying scripts cannot load in this host. That is what a real document does for a URL
  that does not resolve, and the engine already handles it (R14, R21, R23).
- **What each loader does then:**
  - A theme becomes the warning plus an unthemed render, byte-identical to the unthemed one: 2.8 s in
    the worker, 389 ms in Node.
  - A stdlib `!include` becomes the engine's own picture (it reads "Fatal parsing error" under the
    `!include` line), drawn in 253 ms instead of a 20 s timeout.
  - An OpenIconic or emoji reference becomes the engine's text `java.lang.RuntimeException: Failed to
    load openiconic.js` (or `emoji.js`) in under 0.5 s, instead of a hang.
  - Every render after it draws: the poisoned batch goes from 146 s with no SVG to 0.7 s.
- **The code.** The worker host's `MockElement` already has the `_onAppend` hook the prototype used
  (`:96-100`); the Node mock gets the equivalent three lines. Nothing else in either host changes, and an
  ordinary render is byte-identical before and after.

Why the hook fires `onerror` rather than trying to load:
- A Blob worker created by a `file://` page cannot fetch (the host's own header says so).
- The Node host has no network by design.
- 6.1's registration path makes the engine never ask (`CC7` true, no `EK_` call).

**After P4's pin.** npm `@plantuml/core` 1.2026.8, the build `ENGINE_PIN_PLAN.md` moves to, consults
`globalThis.PLANTUML_STDLIB_LOADER` before it appends a script (upstream #2873); the pinned fork build
`0e4f452` does not. The P4 session measured a loader that fails at once on the npm build, in
`plantuml-render.js` (its §1.14 to §1.16, reported 2026-09-25, not re-run here):
- icon and emoji fail in about 260 ms with the engine's text;
- `!theme` renders unthemed with the warning;
- the C4 include draws the engine's own picture;
- a five-diagram batch isolates each failure, in 566 ms.

S3a's `_onAppend` hook works on both builds, so it is right whichever plan lands first. After the pin,
the engine's hook is an alternative that does not depend on the mock DOM's shape. Moving to it is P4's
or 6.1's call, and this plan does not wait for it.

Failing at once is the honest answer in every configuration, and it is the answer the engine's authors
wrote the warning for.

**The failure is made legible (F24).** Without this, the engine's text reaches the reader as a raw
Java exception:
- **The change.** `describeEngineFailure` (`plantuml-browser-render-script.js:892-920`) gets a third
  branch, beside the too-large and syntax ones. Text matching `/Failed to load (openiconic|emoji)\.js/`
  becomes a block. It says the diagram uses PlantUML icons or emoji (`<&name>`, `<:name:>`), which this
  report's renderer does not load, and puts the raw PlantUML under it, as the too-large branch does.
- **Who sees it.** After S4 no text Kronikol writes takes this path. A user's own `InsertPlantUml`
  markup does, and so does a source merged from a report written before S4.
- **Node.** `NodeJs` needs no counterpart: the text becomes a `RenderFailure` and a placeholder that
  quotes it, through `RenderNodeBatchIsolated`.

**The doc comment on `RenderMany`** (`NodeJsPlantUmlRenderer.cs:78-79`) says a failing diagram "never
affects the others". It changes to say what holds after the hook: a diagram the engine refuses gets its
own error. It also records why no host may wait on a script load: a render that never finishes stops
every later render in the process (F17).

**S3b, the diagnostic, second release.** A private `RecordOptionDiagnostics(options)` is called from
`ReportGenerator.CreateStandardReportsWithDiagramsCore`, placed carefully:
- after the zero-scenario guard, so a discovery pass does not warn;
- before the diagnostics snapshot at `:415`, or the entry misses the JSON.

```text
PlantUmlTheme "cerulean" has no effect under PlantUmlRendering.BrowserJs: the report's renderer loads
the engine without its theme bundle, so every diagram is drawn unthemed and the browser console says
"themes.js could not be loaded". Server and Local apply a theme; leave PlantUmlTheme unset to silence
this.
```

Under `NodeJs` the same message names `PlantUmlRendering.NodeJs`, and its middle clause reads "so every
diagram is drawn unthemed; the engine's warning goes to the renderer's stderr, which a successful render
does not keep". The browser console is not where a `NodeJs` reader would look.

- Recorded through `ReportDiagnosticsScope.Record(DiagnosticKind.OptionNotApplied, …)` (no scenario),
  which lands in `TestRunReport.json`'s `diagnostics`, the mergeable report, the HTML block when
  `ShowReportDiagnosticsSection` is on, and the tool's inventory.
- Also printed once as `⚠ WARNING: …` with `Console.WriteLine`, in the style of the other
  generation-time warnings (`:209, :732, :772`). `dotnet test` swallows library stdout on most runners
  (the measured channel table in `LLM_FRIENDLY_PLAN` §11), so the entry is the channel and the line is
  a courtesy.
- The same for `options.ComponentDiagramOptions?.PlantUmlTheme` when `GenerateComponentDiagram` is on,
  naming `ComponentDiagramOptions.PlantUmlTheme`, since the component diagram goes through the same
  engine (`ComponentDiagramReportGenerator.cs:42-45`).
- `NodeJs` is included (Q4, answered by F10), with the variant above. The Node host's warning goes to
  stderr, which the renderer discards on success, so the entry is the only channel there.
- Not covered, by design: a host calling `DefaultDiagramsFetcher` directly with
  `DiagramsFetcherOptions.PlantUmlTheme` (no report, no collector to speak to), and `kronikol merge` and
  `kronikol ingest` (their options object cannot carry a theme, R6). A **source** that carries `!theme`
  from a `Server` run and is merged or ingested into a `BrowserJs` page is what the hardening is for:
  the directive travels with the source, and nothing at merge time can know where it came from.

**Doc comments** (they ship in the package XML, so they are part of the fix):

- `ReportConfigurationOptions.PlantUmlTheme`: "PlantUML theme name written as `!theme <name>` into
  every diagram source, so it is in the report's JSON and mergeable output too. Applied by the engine
  under `PlantUmlRendering.Server` and `Local`, unvalidated against Kronikol's notes and page. **No
  effect under `BrowserJs` (the default) and `NodeJs`**: those hosts load the engine without its theme
  bundle, the diagrams render unthemed, and the run records a `DiagnosticKind.OptionNotApplied` entry
  saying so. `null` (the default) writes no directive."
- `ComponentDiagramOptions.PlantUmlTheme`: the same, plus "written after the generator's own
  skinparam palette, so where it applies it overrides the dependency-type colours".
- `DiagramsFetcherOptions.PlantUmlTheme` gets the first sentence.

**Not taken, and why** (Q8): stripping the directive for `BrowserJs` and `NodeJs` at
`DefaultDiagramsFetcher.cs:257` and `ComponentDiagramGenerator.cs:152` would keep the hang for merged
and ingested sources and would have to be undone by 6.1; fetching the CDN's `themes.js` (326 KB) into
the worker would make themes work today at the price `THEME_PLAN` §2.1 refused: the bundle's 31
deprecated-padding banners, a network fetch per report, and 43 unvalidated themes reaching the default
mode.

### 5.4 Tests (red first)

S3a, unit, `tests/Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs` (the existing class; it skips
without `node` on PATH):

- **A themed source renders.** `RenderMany([themed, plain])` returns two SVGs, byte-identical once
  processing instructions are stripped, in well under the 20 s poll. Assert the pair under 10 s after the
  class's existing warm-up. Red on HEAD: the themed one is an error after 20 s (R13).
- **A stdlib include draws.** `!include <C4/C4_Context>` returns an SVG (the engine's own picture), not a
  timeout. Red on HEAD.
- **A render that asks for a bundle no longer stalls the batch** (F17). `RenderMany([iconMarkup, plain, plain])`,
  where the first source is raw user markup with `<&check>`, returns the engine's `Failed to load
  openiconic.js` for the first and SVGs for the other two, all under 10 s. Red on HEAD: all three time
  out, about 70 s (R23).
- **The hook is pinned.** `DiagramContextMenuTests` checks that the worker host source carries the hook,
  as literal text the way the other script literals are pinned, so a refactor of the mock cannot drop it
  silently.

S3a, Playwright, on `TestPageGenerator`'s BrowserJs page:
- **A page whose first diagram needs a bundle still draws the rest.** The first source is raw markup
  with `<&check>`, followed by three plain sources. The three plain ones reach `data-rendered`. The first
  holds the legible block (`[data-engine-failure="loader"]`, its raw PlantUML under a `details`) and no
  raw `java.lang.RuntimeException` text. Red on HEAD: nothing on the page is drawn (R22).

S3b:

Unit, `tests/Kronikol.Tests/Reports/OptionDiagnosticsTests.cs` (new), each generating a minimal report
under a scoped `ReportDiagnosticsCollector`:

- theme + `BrowserJs` → exactly one `OptionNotApplied` naming `PlantUmlTheme`, the theme, and the mode;
- theme + `NodeJs` → one; theme + `Server` → none; theme + `Local` → none; no theme + `BrowserJs` → none;
- `ComponentDiagramOptions.PlantUmlTheme` + `BrowserJs` + `GenerateComponentDiagram` → one naming the
  component option; with `GenerateComponentDiagram = false` → none;
- both options set → two entries, one each;
- the entry is in the written `TestRunReport.json` `diagnostics` array (kind name `OptionNotApplied`,
  no `scenarioId`), and the JSON schema's `kind` enum contains the name;
- the console line is written once, scoped to the run's own directory (`ConsoleLines.About`, the
  process-wide capture rule from `ci-flake-classes`).

Tool: `QueryCommandTests` gains one case that a report carrying the entry lists it in `summary`'s
Diagnostics section and does **not** print it as a provenance line on `failures`.

Playwright, `ThemedSourceRendersTests` (new), on `TestPageGenerator`'s BrowserJs page
(`GenerateBrowserJsSequenceDiagramPage`: the shipped scripts, the real engine, a Blob worker). Its first
two cases test S3a and ship in the first release. The third needs S3b's entry and joins in the second
release; that release also rewrites the first case's guard message once the emitter exists:

- **A themed source renders in the worker.** A `!theme cerulean` source reaches `data-rendered` within
  the standard render wait; its SVG is byte-identical (processing instructions stripped) to the same
  source without the directive on the same page; `Page.Console` received a message containing
  `themes.js could not be loaded` (F13: the worker's warning reaches the page). Red on HEAD: the wait
  times out (R12). This is also the **6.1 guard**: when the worker registers `PLANTUML_THEMES`, the
  SVGs differ and the warning is gone, the test fails, and its message says: remove the
  `OptionNotApplied` emitter for `BrowserJs`, update the doc comments and the wiki. The CDN layout is
  not the trigger (the tag already ships `themes.js`, F6); registration is.
- **A stdlib include renders instead of hanging.** `!include <C4/C4_Context>` reaches `data-rendered`
  within the standard wait, with an SVG, whatever the engine chose to say in it. Red on HEAD: never
  rendered in the shipped worker (R12), an SVG in 2.8 s with the hook (R14).
- **A themed report end to end.** A report generated with `PlantUmlTheme = "cerulean"` under
  `BrowserJs` through `ReportTestHelper`: every diagram reaches `data-rendered`, `TestRunReport.json`
  carries one `OptionNotApplied` and no `RenderFailure`. Red on HEAD on the first count.

Wiki and docs are in §7.2.

### 5.5 S4 — text Kronikol copies in cannot reach the loader: the change

Two rules, one per kind of text.

**Payloads.** `IsCreoleTagStart` (`PlantUmlCreator.cs:559`) becomes

```csharp
// `<&` is an OpenIconic icon, `<:` an emoji, `<$` a sprite. The first two make the engine load a bundle
// by script tag, which the report's hosts cannot answer; the third drops the text (DIAGRAM_COLOURS_PLAN F16).
private static bool IsCreoleTagStart(char c) => c is '/' or '#' or '&' or ':' or '$' || char.IsAsciiLetter(c);
```

and the doc comment of `EscapeCreoleMarkup` names the three. Every text payload already goes through
it: bodies of every format, headers, the full path and action notes (R20). A `<` followed by anything
else, such as `a < b` or `x<-y`, is left alone, as today.

**Step, test and assertion text.** None of this was ever creole-neutralised (F19). A new
`PlantUmlCreator.EscapeLoaderMarkup(string)` puts `~` before a `<` that is followed by `&`, `:` or `$`,
and nothing else. It applies at four sites:
- `StepBarPlantUml.Build`'s label line, before it is wrapped and capped (`:62-68`), which covers
  `StepCollector` and ingest alike. Only the label needs it: the bar's body already writes every `<` as
  `<U+003C>`.
- The test identifier in `TrackingDiagramOverride.InsertTestDelimiter` (`:78`).
- The assertion label and failure message in `Track.cs:440-447`, before `WrapBlockNoteBody`.
- The ingest assertion note in `InteractionRecord.cs:407-415`.

Why the rule is this narrow:
- These texts have always reached PlantUML raw. A step name or an assertion message that uses `<b>`,
  `**` or `//` is styled today, and changing that changes how existing reports look. That is a separate
  decision (§6).
- The three prefixes are the ones that hang a render or lose text.

Why `~` and not `<U+003C>`: R25 measured `~<` in all four forms (styled bar, coloured bar, assertion
note, note), and it matches what the payload escaper already writes.

Where the `~` goes afterwards:
- The copy path drops `~` before `<` (`collapsible-notes-script.js:1039`).
- The search normalisers drop it too (`report-search-index.js:55`, and `SearchNormalizer.cs:88-93`), so
  `Vec~<&str>` indexes as `vec<&str>`, the same as today.
- The two scripts that read bars and assertion notes strip them whole (`collapsible-notes-script.js:2089,
  2093`).

The cap in `TruncateLabel` can cut between a `~` and its `<`. What that paints is pinned by a test, not
assumed.

**Bytes.** Only sources whose payload, step, test or assertion text carries one of the three prefixes
move. No golden pins such a source: the only `<$…>` in `tests/` are the LightBDD HTML fixtures, which
never reach a bar (READ).

### 5.6 S4 — tests (red first)

Unit:

- `PlantUmlCreatorTests`: bodies carrying `Vec<&str>`, `<:rocket:>` and `<$foo>` are emitted with `~<`
  before each; `a < b`, `x<-y` and `<>` are emitted unchanged. Red on HEAD for the first three.
- `StepBarPlantUmlTests`: the label `Given I have data [inputs: "<$inputs>"]` carries `"~<$inputs>"` in
  the styled and the coloured form; a label with no `<` is byte-identical to today (the coloured form's
  promise); a label capped between `~` and `<` is pinned as it renders. Red on HEAD for the first.
- `TrackThatTests` (the assertion note), `InteractionRecordTests` (the ingest bar and assertion note)
  and a new case beside the existing test-delimiter coverage (`TrackingDiagramOverrideTests`, new if
  none fits): the three prefixes are escaped.
- `NodeJsPlantUmlRendererTests`: the emitter's `Vec<&str>` source renders an SVG in well under 10 s
  whose text carries `Vec<&str>` (escaped as XML after S5). Red on HEAD: a timeout (R21).
- `NoteCopyFidelityTests`: copying a note that carries `Vec~<&str>` yields `Vec<&str>`.
- `SearchNormalizerEquivalenceTests`: a vector with `Vec~<&str>` normalises to `vec<&str>` in C# and in
  JS, added through `tools/search-bench/gen-vectors.js` like §3.4's.

Playwright, `LoaderMarkupInCapturedTextTests` (new), through `ReportTestHelper` and the real emitter:

- **A captured `Vec<&str>` as the first scenario.** Then a request carrying `<:rocket:>`, then plain
  scenarios. Every diagram reaches `data-rendered`, and the notes paint `Vec<&str>` and `<:rocket:>`
  (read from the SVG's `<text>`). Red on HEAD: nothing on the page is drawn (R22).
- **A LightBDD table-parameter step.** The report's step bar paints `[inputs: "<$inputs>"]`. Red on HEAD:
  it paints `[inputs: ""]` (R25). This runs on the LightBDD fixture the E2E project already builds its
  step-bar tests on.

### 5.7 S5 — the Node renderer writes XML

**The change.** `serializeElement` (`plantuml-render.js:124-141`) escapes what it writes:
- Text nodes: `&`, `<` and `>` become `&amp;`, `&lt;` and `&gt;`.
- Attribute values: the same, plus `"` as `&quot;`.

These are XML escapes only. The worker host's `escText` is not copied, because it also writes NBSP as
`&nbsp;`, an HTML entity that XML does not define: an image SVG carrying it would fail to parse the way
`&` does today. `InlineSvgRendering` and the data-URI path (`DefaultDiagramsFetcher.cs:358-360`) need
nothing. The SVG is simply well-formed now.

**Bytes.** Only Node SVG whose text or attributes carry one of the four characters moves. No test pins
Node SVG bytes for such a source (READ). R28 ran the 22-diagram corpus through a prototype of this change
and no byte moved.

**One existing test pins the unescaped text, and turns red.** `NodeJsPlantUmlRendererTests.Creole_markup_in_a_captured_body_reaches_the_svg_as_text`
(`:197-229`) pulls each `<text>` element's content out of the raw SVG with a regex and asserts
`Assert.Contains("<b>raw</b>", rendered)` (`:228`). After S5 the raw SVG holds `&lt;b&gt;raw&lt;/b&gt;`, so
the assertion fails although what is painted is unchanged. The P4 session found it; the second and
third passes missed it. The fix is to read the text through `XDocument.Parse` instead of the regex, which
decodes the entities and asserts well-formedness in the same step. It is the only assertion in the file
that reads marked-up text out of Node SVG (READ: the other 16 calls check `<svg` or an empty body).

**Tests (red first):**

- Unit, `NodeJsPlantUmlRendererTests`:
  - The `&` source (`?page=1&size=10`, `fish & chips`) and the XML source each render an SVG that
    `XDocument.Parse` accepts. Red on HEAD for the first.
  - The XML source's body is character data: the document's text contains `<order>`. Red on HEAD: it
    parses as elements.
- Unit, the fetcher's `NodeJs` path with `InternalFlowTracking = false`: the data URI decodes to
  well-formed XML.
- Playwright, `NodeJsSvgFidelityTests` (new), a `NodeJs` report through `ReportTestHelper`:
  - An XML-bodied scenario's note paints its tags as text: the `<text>` whose content starts with
    `<order>` has `getComputedTextLength() > 0`. Red on HEAD: 0 (R26).
  - With `InternalFlowTracking = false`, a scenario whose path carries `&` shows its diagram image:
    `naturalWidth > 0`. Red on HEAD: 0.

---

## 6. What is not taken, and what is fixed in passing

Seen while measuring, with the number that makes each a decision rather than an oversight:

| Item | Measured | Why not here | Where it goes |
|---|---|---|---|
| `FocusDeEmphasis.LightGray` (the **default**) paints non-focused fields `<color:lightgray>` = `#D3D3D3`: **1.47 : 1** on the default note, 1.21 on the event note | R2, R4 | It is an opt-in feature (focus fields per trace) whose option is named for the colour it paints, and a reader is meant to skip those fields. Changing it is a design change to a documented default, not a contrast fix | Q7 for the owner; the natural home is the toolbar plan's token layer (11.2) or 6.1's palette, where "de-emphasised" gets a value that still reads |
| The `&` divider in form bodies, `<font color="lightgray">` (`PlantUmlCreator.cs:1254`) | 1.47 | A glyph between fields, not text to read | none |
| The step bar `#black:<color:white>` and the setup partition `#F6F6F6` assume a light diagram | audit | Theme-dependent | 6.1 (`THEME_PLAN` §2.6) |
| Component generator writes `!theme` after the C4 `!include` under `Server` and `Local`, and its own palette only under the JS engines, where no theme applies (this row said "after its palette" until the 3.30.2 audit); `ComponentDiagramDiffer` never writes it | audit; R6 | Only observable where a theme applies (`Server`, `Local`) | 6.1 phase 5 |
| Anchor-path hover colour is a CSS literal `#0000EE` (`internal-flow-popup-styles.css:151-152`) | READ | Java-rendered SVG only; theme-dependent | 6.1 (`THEME_PLAN` §2.7) |
| The detection literal `#0000ff` (`:1063`) | R3 | Needs a palette to replace it with data | 6.1 (`THEME_PLAN` §2.7) |
| Link and focus colour are the same `#0000FF`, so the script cannot tell them apart (§4.2's limit) | R2 | `skinparam hyperlinkColor` would separate them and moves bytes for a second reason in one patch | Q6, decide with 6.1 |
| The other options `BrowserJs` ignores (`PlantUmlImageFormat`, `LocalDiagramRenderer`, `LocalDiagramImageDirectory`, `PlantUmlServerBaseUrl`) | wiki table | Each is a one-line emitter on the same kind; this plan is 1.6, which is the theme | A follow-up patch after `OptionNotApplied` exists; not this release |
| A host using `DefaultDiagramsFetcher` directly, or `kronikol merge`, with a theme | R6 | No collector, or no way to set one | Documented in the doc comment as "recorded when a report is generated" |
| `spacelab-white` and the `-outline` themes, `carbon-gray` on the jar | audit | Theme defects | 6.1 |
| Stripping `!theme` from `BrowserJs` and `NodeJs` sources at the two emitter sites (`DefaultDiagramsFetcher.cs:257`, `ComponentDiagramGenerator.cs:152`) | R6 | Keeps the hang for merged and ingested sources, since the directive travels with the source, and 6.1 would put the line back | Q8, not taken |
| Loading the CDN's `themes.js` (326,345 B, R15) into the worker so themes work today | R15, R18 | `THEME_PLAN` §2.1 refused the bundle: 31 deprecated-padding banners, a network fetch per report, 43 unvalidated themes in the default mode | 6.1 (embed the one selected theme) |
| `AnswerAffectingDiagnostics` includes `RenderFailure`, so a themed `NodeJs` run today heads every query answer with N provenance lines | F10 | Right for a real render failure; the flood is the hang's, not the set's | Goes away with the hardening |
| `ComponentDiagramReportGenerator.cs:42-45` avoids C4 under the JS engines because "the C4 flavour hangs the Node renderer until its timeout" | R13, R14 | The hang is the same mechanism and the hook ends it, but the stdlib content is still absent from the JS build, so the plain syntax stays | none; the comment can say why the hang is gone |
| A hung engine is never recovered: after the host's 150 s timeout the worker keeps its stuck state, a Node batch keeps its process, and the lazy workers start only on a success | R22, R23 | After S3a no loader path can hang, which leaves only a render slower than 150 s (worker) or 20 s (Node) as a trigger. Recovering means terminating the worker after a host timeout, re-running a batch's remainder in a fresh process, and counting a failure towards starting the lazy workers. That is the render pool's robustness, not the loader's | A follow-up patch. `ENGINE_PIN_PLAN` (P4) re-measures render times and is the natural owner |
| Registering `PLANTUML_OPENICONIC` (`openiconic.js`, 51,021 B) or `PLANTUML_EMOJI` (`emoji.js`, 1,874,010 B) in the hosts, so a user's own `<&icon>` or `<:emoji:>` markup draws, as OpenIconic did under `BrowserJs` before 3.0.50 | R19, R27 | After S4 no text Kronikol writes needs them, and a user's markup gets S3a's legible failure. Loading them is a feature and a download per report | Q10, with 6.1, which registers bundles in the hosts anyway |
| Full creole neutralisation of step, test and assertion text: `<b>`, `**`, `//` and `[[…]]` still style or link there after S4 | R25 (`~` is honoured in all four forms, so the change is mechanical) | It changes how existing step names and assertion messages look, and none of those hangs or loses text | A follow-up, if the owner wants step text shown exactly as written |
| `InternalFlowTracking` silently forces `InlineSvgRendering` for `NodeJs` (`ReportGenerator.cs:259-265`), which is why a bare `&` breaks an image only when tracking is off | R26 | After S5 the SVG is XML either way, and the forcing no longer matters for correctness | none |
| A truncated request label loses its whole diagram in the browser worker, and `[Full path]` is never seen there (F25) | R30 | Not a colour, theme or loader defect. The limit is `PlantUmlStatementLimits.MaxMessageStatementChars`, whose doc comment P4's S1 rewords and whose edges P4's §1.11 measured in Node only | A patch of its own ahead of P4: `ENGINE_PIN_PLAN.md` Q9 caps the text inside the link in the emitter, bisects the worker edge in that fix's red run, and adds a worker E2E at the cap. P4's S1 then rewords the doc comment. Firefox and WebKit workers are not measured |
| "Copy all caller request payloads" is never offered on a report written with the default arrow colours (F26) | R29 | A context-menu defect, not a header one, although its line is one of S1's twelve | Q11 |

**Fixed in passing** (inside the slices, not extra scope):
- The eager blackout of non-link blue text (F3) is the same lines as S2.
- The emitter's `<color:gray>` becomes one constant, so a third header site can no longer drift.
- Three hangs besides the theme's go with S3a, because they are the same hook: stdlib `!include`
  (F11), OpenIconic and emoji (F15). So does the poisoning of every later render in the same engine
  (F17). Each has its own red test (§5.4).
- The `RenderMany` doc comment's "never affects the others" is corrected with S3a.

---

## 7. Versioning, docs, parity

### 7.1 The bump

- **First release, 3.29.6: patch.** S3a, S4 and S5 are bug fixes. S4 and S5 change observable output
  only for the sources they fix. The legible failure block is how a fix presents its outcome, not a new
  control. Nothing new is there to call.
- **Second release, 3.30.0: minor.** S1 and S2 are patch-level fixes that move diagram bytes, and the
  changelog says which bytes and why. S3b's `OptionNotApplied` adds a public enum member and a JSON schema
  value, which is **minor** by the CLAUDE.md rule, as `ReportRotationFailed` was counted in 3.27.0. The
  highest-ranking change decides. If Q2 takes `Other` instead of the kind, the second release is a patch,
  3.29.7.
- **One-release alternative (Q3):** all six slices as 3.30.0, with the two changelog entries below
  joined under the minor heading.

All packages move together each time.

### 7.2 Docs

Changelog, under each bump's heading, stating which part moved and why.

**3.29.6:**

> **Patch - no diagram is lost to the renderer.** The patch part moved because every change is a fix,
> with nothing new to call. The browser and Node renderers give the PlantUML engine a minimal page, and
> the engine asks that page for four bundles by adding a script element: themes, stdlib modules,
> OpenIconic icons and emoji. Neither page ever answered. A diagram that needed a bundle never finished,
> and nothing after it in the same engine finished either. A response body quoting Rust's `Vec<&str>`
> (PlantUML's icon syntax) left every diagram of the report undrawn when its diagram rendered first, and
> under `NodeJs` failed every later diagram of the run after about 20 seconds each. A theme under
> `BrowserJs`, the default, left the report without diagrams. Both pages now answer at once. A theme is
> ignored with the engine's console warning, as it was before 3.0.76. A stdlib `!include` shows the
> engine's error picture, and an icon or emoji in a user's own diagram markup shows a readable failure
> with its PlantUML. Captured payloads, step text, test names and assertion text now escape `<&`, `<:`
> and `<$`, so they are drawn as captured: a LightBDD step with a table parameter keeps its `<$name>` in
> the diagram's step bar. The `NodeJs` renderer now writes well-formed SVG. XML bodies were painted
> blank, and with internal-flow tracking off, a `&` in a URL or a body turned the whole diagram into a
> broken image.

**3.30.0:**

> **Minor - `PlantUmlTheme` says when it does nothing; two diagram colours are what they should have
> been.** The minor part moved because there is one new public value, `DiagnosticKind.OptionNotApplied`,
> in the enum and the report schema. The fixes are patches. `BrowserJs`, the default, and `NodeJs` load
> the engine without its theme bundle, so a theme set under either draws the diagram unthemed. The run
> now records `OptionNotApplied` naming the option and the mode, and the option's documentation says so.
> The header lines of every note are painted `#686868` instead of `#808080`, computed as the lightest grey
> that clears 4.5 : 1 on both note fills (3.87 and 3.20 before; 5.46 and 4.51 after). Internal-flow link
> text rests in the colour of the text around it instead of black. On the default theme that changes
> nothing, and it stops the same code blacking out `FocusEmphasis.Colored` fields and hyperlinks in
> payloads, in sequence and component diagrams alike.

Wiki (`../Kronikol.wiki`). The theme wording is wrong today, whatever the release, so it is corrected
with the **first** release, and the second release adds the diagnostic clause to the same pages:

- `Report-Configuration.md:26`: the options sample sets `PlantUmlTheme = "spacelab"` beside
  `PlantUmlRendering = PlantUmlRendering.BrowserJs` (F22). The line becomes a comment saying the option
  applies under `Server` and `Local` only.
- `Diagram-Customisation.md:137-156` (PlantUML Themes): replace "changing colours, fonts, and styling"
  with the mode table's truth. Say which modes apply it and that the default does not. From 3.30.0, say
  it records `OptionNotApplied`. Link `PlantUML-Browser-Rendering` and `THEME_PLAN`.
- `Report-Configuration.md:125` (the options table row) and `:140` (the component options row): one
  clause each.
- `Component-Diagrams.md:128`, `:272-283` and `:679`, the options class listing: the same clause and the
  palette-ordering note.
- `PlantUML-Browser-Rendering.md:279` and `:321`: both sentences are wrong at HEAD (R11). The theme text
  does reach the worker, and the engine acts on it; what is missing is the bundle. Rewrite as: the hosts
  load the engine without its theme bundle, the diagram renders unthemed, and the engine warns in the
  console. Record that from 3.0.76 to 3.29.5 the same setting left the report without diagrams, so a
  reader on an older version knows what they are looking at. From 3.30.0, add that the run records
  `OptionNotApplied`.
- `Large-Response-and-Diagram-Handling.md:90` (the table of what is creole-neutralised) and the step-bar
  passage of `Step-Tracking.md` (around `:572`): payloads, step text, test names and assertion text
  escape `<&`, `<:` and `<$` from 3.29.6.
- `Diagnostics-and-Debugging.md:90-105`: the `OptionNotApplied` row, between `ReportRotationFailed` and
  `Other`, from 3.30.0.
- `API-Reference.md:5-6, 86`: no change needed, since the enum is not listed there.
- README: it does not mention the option (R11), so nothing changes.
- No wiki page describes the Node renderer's SVG, so S5 lives in the changelog.

`plan-execution-audit-checklist` applied before each tag:
- Doc comments are part of the change (they ship), including `RenderMany`'s.
- Every decision has a paint-level E2E (§3.4, §4.3, §5.4, §5.6, §5.7).
- No fact is weakened by a new default, because no default changes.
- The sibling wiki pages are the ones above.
- The CLI's fixed options object cannot carry a theme, so it stays out of S3b's test matrix by fact, not
  by omission. `kronikol ingest --render nodejs` does reach S5, through the same `RenderMany` path that
  S5's unit tests cover.

### 7.3 `PLANS_STATUS.md` and the roadmap

This file has a row, added with the plan. On each release, update:
- The row's status and release.
- The `STAGE_1_PLAN.md` P3 row.
- `ROADMAP.md` 1.6.
- On the second release, the `THEME_PLAN` row's note that 6.1 must remove the diagnostic.

S4 and S5 are not roadmap items today. If the owner keeps them here (Q9), 1.6's row gains a clause for
them. Otherwise they need a stage 1 row of their own.

### 7.4 Kronikol4J

One ledger entry in `Kronikol4J/README.md`, under the standing divergence chain, in the port's voice:

> .NET 3.29.6 and 3.30.0 change what the port mirrors in four places.
>
> 1. **Header ink.** Note header lines are painted with a computed ink, `<color:#686868>` in place of
>    `<color:gray>`, at both emitter sites (`BatchGray`, and the `[Full path]` block the port does not
>    have). This is a PlantUML-source divergence from every 3.0.43 fixture that carries a header. The
>    port's `NoteFormatter.batchGray` keeps `<color:gray>`, and its 3.0.43 script assets keep matching it
>    literally. That stays self-consistent, because the port never renders .NET-produced sources.
> 2. **Escaping.** .NET's payload escaper now escapes `<&`, `<:` and `<$`, and its step bars, test
>    delimiters and assertion notes escape the same three. The port has no creole escaper, so its notes
>    and bars still read them as PlantUML markup. On the port's pin, `v1.2026.3beta6-patched`, `<&name>`
>    is dropped from the text, and `<:name:>` asks for `emoji.js` by script tag (in Node, a 20 s timeout).
> 3. **Scripts.** `plantuml-browser-render-script.js` reads link rest and highlight colours from the SVG
>    instead of `#000000` and `#0000FF` literals, and explains a bundle-load failure.
>    `plantuml-worker-host.js` and `plantuml-render.js` answer a script append with `onerror`, and the
>    Node renderer escapes the SVG it writes. The port renders through its 3.0.43 assets, which predate
>    the worker host and the Node renderer, so none of this has a counterpart there.
> 4. **The report schema** gains `DiagnosticKind.OptionNotApplied`, and one shared search-index vector
>    joins. The port has `plantUmlTheme` on `DiagramOptions` and on `ComponentDiagramRenderOptions`. On
>    its pin a theme is a silent no-op, the state .NET was in from 3.0.45 to 3.0.75, and the port emits no
>    diagnostic for it.

D11 (freeze the rendering half of the ledger with one entry) is not taken by this plan. If D11 is taken
first, this entry folds into the freeze note.

### 7.5 Corrections to record in `THEME_PLAN.md` when this ships

So that 6.1 does not undo P3:

- §2.3, the header-line row: "stopping at 3.9, matching today's 3.87" becomes "stopping no lower than
  **4.5**, the floor `NotePalette.HeaderContrastFloor` set in 3.30.0; the derivation may land darker,
  never lighter". The audit checklist's item 06 ("at least 3.0") is superseded the same way.
- §2.6, the legacy-literal rewrite: the list gains `<color:#686868>` beside `<color:gray>`, since both
  forms are in the wild once this ships.
- §2.7, the first row: "restore that, not `#000000`" is replaced by the rest-colour rule of §4.2 (the
  original fill is the **highlight** colour; the rest colour comes from the surrounding text, and 6.1
  may hand it over as data instead).
- §0 "What they do not change" (line 65) and §2.1 (lines 134-136): "the engine cannot fetch `themes.js`:
  `TeaVmScriptLoader` has no `document` to append a script tag to" is not what happens. The worker host
  gives the engine a `document` with a `head`. The append succeeds and nothing answers, which has been a
  hang since 3.0.76, until 3.29.6 makes the append fail fast (F9, F11, F21). The design (embed one theme,
  register it) stands. The same section should say that the engine loads OpenIconic, emoji and stdlib
  the same way (F15), so 6.1's registration pattern is the one any later bundle would follow.
- The "Worker registration is load-bearing" note (lines 446-448) says "#2848's behaviour there is to
  render the diagram *unthemed* rather than fail". That was false in the worker from 3.0.76 to 3.29.5
  (the diagram never rendered, and nothing after it did either) and is true afterwards only because of
  the hook. The Phase 3 no-fetch assertion stays load-bearing.

Outside `THEME_PLAN`:
- `ROADMAP.md` 1.6 (line 220, "a theme set under BrowserJs is silently ignored") and the audit's summary
  row (line 837, "`!theme` is a silent no-op in BrowserJs") each get a one-clause correction. Both were
  true when written, on the pre-3.0.76 pin (F21).
- The stage-0 memory ("themes under BrowserJs do nothing: the worker never receives the theme text") is
  corrected with the plan.
- The audit artifact itself is not edited.

---

## 8. Acceptance

Before each tag, on the real thing rather than the fixtures. Steps 1 and 5 apply to both releases.
Steps 2, 3a and 4 are the second release's. Steps 3b to 3e are the first release's.

1. The full suite is green, E2E included, twice. The second run, under load, is what catches a
   `WaitForFunctionAsync` without `PollingInterval`.
2. Generate two reports on the candidate build: the repo's own E2E `LargeReportFixture` report, and one
   from BreakfastProvider. Open each in Chromium and read from the painted SVG, not the source:
   - A header line's fill against its note's fill gives a ratio of at least 4.5, on a request note and on
     an event note.
   - An internal-flow link at rest is the body colour, hovered it is blue and underlined, and released it
     is the body colour again.
   - A `FocusEmphasis.Colored` report keeps its blue fields.
3. Themes and captured text:
   - **3a.** Set `PlantUmlTheme = "cerulean"` on the same fixture under `BrowserJs`.
     - Every diagram is drawn, unthemed, within the usual time, and the browser console shows the
       engine's warning.
     - `TestRunReport.json` carries one `OptionNotApplied` and no `RenderFailure`. `kronikol query
       summary` lists it under Diagnostics, and `kronikol query failures` prints no provenance line for
       it.
     - Under `NodeJs`, the same fixture's report is written in seconds, not minutes, with the same single
       entry.
     - Under `Server` (or `Local` with the IKVM package) there is no entry, and the theme visibly applies.
     - The red half is part of the record: on HEAD before the fix, the themed `BrowserJs` fixture shows no
       diagram at all.
   - **3b.** A fixture whose first scenario's response body quotes `Vec<&str>` and whose second request
     carries `<:rocket:>`, under `BrowserJs`: every diagram is drawn, and the notes read `Vec<&str>` and
     `<:rocket:>`. On HEAD, record the red half: nothing on the page is drawn.
   - **3c.** The same fixture under `NodeJs`: no `RenderFailure`, and the run takes seconds. On HEAD:
     every diagram from the first one onwards is a placeholder.
   - **3d.** A LightBDD scenario with a table parameter, from the E2E project's LightBDD fixture or
     BreakfastProvider's LightBDD lanes: its step bar reads `<$name>`. On HEAD it reads `""`.
   - **3e.** `NodeJs` with an XML-bodied scenario and a scenario whose path carries `?a=1&b=2`, once with
     internal-flow tracking on and once with it off: every note paints, and every image loads.
4. `kronikol merge` of a mergeable report written by the release before (old header form) with one
   written by the candidate: every header behaviour works on notes from both (I3). `merge-probe.js` is
   the harness (R29). Run it with internal-flow tracking off as well, to see `[Full path]`, until F25
   is fixed.
5. The wiki pages of §7.2 are re-read against the shipped behaviour, and the doc comments are read in the
   package XML, not the source.

---

## 9. Open questions, recommendations attached

| # | Question | Recommendation | Why |
|---|---|---|---|
| Q1 | The contrast floor: 4.5 (AA, text under 18 pt) or 3.0 (the audit checklist's item 06, the large-text and UI-component figure) | **4.5.** With a margin if wanted: floor 4.6 lands on `#666666` (5.62 / 4.65) | The note body is 13 px; the roadmap row already calls 3.87 "below AA", which is the 4.5 reading. 3.0 would keep `#808080` on the default note and only move the event note |
| Q2 | Reuse `Other` (patch) or add `OptionNotApplied` (minor) | **The kind.** | F7: `Other` heads every query answer as provenance. The rule makes the minor mechanical, and P1 already puts a minor in the patch train |
| Q3 | One release or two | **Two: 3.29.6 (S3a, S4, S5), then 3.30.0 (S1, S2, S3b).** This reverses the second pass, which said one release, 3.30.0 | The first release fixes defects that lose diagrams on the default configuration (F16, F17), on `NodeJs` (F20) and under a theme (F9, F10). It needs no decision beyond Q9 and adds no public surface. Holding it behind the contrast re-pin and a new public kind delays it for nothing. One release with all six saves a release pass and is the alternative |
| Q4 | Include `NodeJs` in the diagnostic | **Yes.** Answered by F10 | Same engine and the same mock-DOM hang, cut at 20 s per diagram. Today a themed `NodeJs` run is worse than `BrowserJs` in one way (N `RenderFailure` entries head every query answer) and better in another (the report is written) |
| Q5 | The rest colour: nearest preceding non-link text, or a container-wide mode | **Nearest preceding, mode as fallback** | A link on an arrow label and a link in a note can sit on different inks under a theme; on the default theme both rules answer `#000000` |
| Q6 | Emit `skinparam hyperlinkColor` so links and `FocusEmphasis.Colored` stop sharing `#0000FF` | **Not now.** Decide inside 6.1 | It fixes §4.2's known limit and moves bytes for a second reason; 6.1 sets a link colour per theme anyway |
| Q7 | `FocusDeEmphasis.LightGray` at 1.47 : 1 as the default | **Leave, and put it on 6.1's or 11.2's list** | A documented default named for its colour; changing it is design, and "de-emphasised but legible" needs the palette work |
| Q8 | What to do about the hang: the mock DOMs fail fast, or strip the directive from `BrowserJs`/`NodeJs` sources, or load the CDN's `themes.js` into the worker so themes work today | **Fail fast, plus the diagnostic.** | Two hook lines, no change to sources or JSON. They also end the stdlib, OpenIconic and emoji hangs and the poisoning of later renders (F15, F17), honest in every configuration, and 6.1 builds on them unchanged. Stripping leaves merged and ingested sources hanging. The CDN bundle is what `THEME_PLAN` §2.1 refused, and would put 43 unvalidated themes into the default mode |
| Q9 | Keep S4 (the three escapes) and S5 (the Node serializer) in this plan, or move them to a plan of their own | **Keep them here, in the first release.** | Roadmap 1.6 does not name them, so they stay only with the owner's yes. They share S3a's mechanism: S4 keeps captured text away from the loader S3a hardens, and without S4 a `Vec<&str>` body still costs its diagram. S5 is in `plantuml-render.js`, which S3a edits. All three share the probes and the acceptance pass (§8 step 3). If they move out, 1.6's first release is S3a alone and the defects wait for a new stage 1 row |
| Q10 | Load OpenIconic (`openiconic.js`, 51 KB) so a user's own `<&icon>` markup draws under the JS engines, as it did under `BrowserJs` before 3.0.50 | **Not now.** Decide with 6.1 | After S4 no text Kronikol writes needs it, and a user's own markup gets S3a's legible failure. Loading it is a feature and a download per report, and 6.1 builds the registration path any bundle would use. Emoji (1.87 MB) is not worth proposing |
| Q11 | Fix F26, the caller-payload menu item, and in which release | **Its own patch, at any time.** The fix is the arrow test in `extractCallerPayloads`: accept `-[#…]>` as well as `->`, for example `^\s*caller\s+-(?:\[[^\]]*\])?>\s+`. Its test must run on emitter output, since the hand-typed plain arrow is why today's test passes | Arrow colours have been the default since 2026-04-22 (`ddbe88e1`), and the plain-arrow match has been in the script since 3.0.0, so by the history (READ) the item has never appeared on a default report. It shares a line with S1 but not a cause, and it is not the ask. S1's executor should know that the `:296` site can only be reached with arrow colours off until this is fixed |

---

## 10. Assumption ledger

### 10.0 The error class, and the rule that catches it

The roadmap row was written from an audit that spliced theme text into an engine build, a
configuration that does not ship, and never re-checked at HEAD. Two of its three sentences were right
for the wrong configuration: the hover blackout is real code and invisible in every shipping mode
(F2), and "at two sites in the render script" counts the `#000000` writes and misses the two `#0000FF`
writes that are the same defect. The rule this plan applies: **a defect in a browser script is verified
by rendering through the pinned engine and reading the painted SVG, not by reading the script** (R1,
R2, R3), and anything only read is marked READ below and has an execution-time test that runs it.

The second pass found the same class one level down. R1 was RUN, but in a bench page whose real
`document` answered the engine's script request with a 404; the report's hosts answer with silence, and
"one warning, byte-identical output" became "no diagram at all" (F9). The rule, sharpened: **a defect
in the browser path is verified through the shipped worker host and the shipped Node script**, the
page-level shell being a different configuration in exactly the place that mattered. Everything in §5
now rests on R12 to R14, and the E2E in §5.4 runs the shipped scripts, not a shell.

The third pass found a third form of the error, and this one was about scope. The earlier passes asked "what does
the theme do?", grepped for the theme, and found the theme. Asking the engine instead ("what do you
load by script tag?", R19) turned one hang into four. It then led to where captured text meets the
loader (F16, F19) and to the serializer beside the mock DOM (F20). The rule, sharpened again: **when a
defect is a mechanism, enumerate every caller of the mechanism before scoping the fix**, and follow the
data that reaches it from Kronikol's own emitter, not from hand-written sources. R20 and R25 were
written by the real emitter and the real `StepBarPlantUml`, which is why their bytes can be trusted.

### 10.1a Existence and shape (cheap to check, checked)

- R5: the two emitter sites, the twelve marker sites, and no other reader of the tag in C#, the tool
  or the search normaliser.
- R6: the option's whole path; no renderer reads it; the CLI cannot set it.
- R7: the enum, the schema's `Enum.GetNames`, the ingest parser, the tool's two sets.
- R10: the port's emitter and its script copies.
- R11: the wiki lines.

### 10.1b Behaviour (the kind that gets reversed)

- R1 (RUN, in a page): missing `themes.js` → the warning text quoted in F6 and byte-identical SVG.
  Served `themes.js` → the theme applies. **In the shipped worker (R12) the same source never renders**;
  the wiki's and `THEME_PLAN`'s statements about the worker were READ and are corrected in §7.
- R2 (RUN): the fills and the tag colours.
- R3 (RUN): no anchors in browser-rendered SVG for either link form.
- R4 (RUN): the contrast arithmetic; the audit's 3.87 reproduces exactly, so the formula matches the
  audit's.
- R8 (RUN, R12): the script's blue-text path, on the engine's own output through the shipped script.
- R9 (READ): the escaper's treatment of `<color:`; §3.4 pins it.
- R12, R13 (RUN): the hang, in the worker and in Node. R14 (RUN): the hook ends it in both, and ends the
  `!include` hang.
- R19 (RUN): the four bundles and six call sites.
- R20, R21 (RUN): payloads reach the loader, and hang or lose text.
- R22, R23 (RUN): one hang poisons every later render in the same engine.
- R24 (RUN): a page of themed diagrams draws nothing. That makes F9's whole-report consequence RUN at
  page level; it was READ from the gating at `:190`.
- R25 (RUN): step and assertion text; `~<` works in every marker form.
- R26 (RUN): the Node SVG, as image and inline.
- R27 (RUN): the dates of the loaders.
- R28 (RUN): the hook, and S5's escaping, leave the corpus byte-identical on both hosts.
- R29 (RUN, on a candidate build): old and new header forms behave identically in one merged report, and
  today's scripts break every header behaviour on the new form (I3).
- R30 (RUN): a message statement at the 2,000-character cap is not drawn in the browser worker when the
  internal-flow link wraps it; Node draws it (F25).

### 10.2 Not verified, and what this plan does about each

| Gap | What is done |
|---|---|
| That a themed **report** generated by `ReportGenerator` draws nothing. R24 is the same page scripts on a probe page; a real report adds the notes scripts and renders by visibility | §5.4's end-to-end themed report test, red on HEAD; §8 step 3a's red half |
| That `EscapeCreoleMarkup` escapes a hex colour tag at line start (R9 is from the tag-start rule) | §3.4's test, red first |
| That the twelve script sites are all the readers (grep is exact, but a reader that builds the tag from parts would not match) | `Assert.DoesNotContain("<color:gray>", …)` over full fixture output plus the five behaviour E2Es on new-form sources; a missed reader shows up as a failed E2E, not as a silent break |
| ~~That the hook has no effect on any render that does not append a script~~ | **Closed by R28.** The corpus is byte-identical on both hosts. `corpus-hash.js` renders in a page shell and would not have shown it, which is why R28 went through the hosts |
| That LightBDD formats every table parameter as `<$name>` (R25 reads it from the repo's LightBDD fixtures, not from LightBDD) | §5.6's LightBDD E2E runs a real LightBDD table step, red first; §8 step 3d |
| That the port's page answers `<:name:>` with the engine's failure text (a real `document`, READ) | Recorded in the ledger entry as READ; the port is not changed by this plan |
| `RenderMany`'s 30-minute cap turning every diagram into a placeholder (READ from the code, R23) | Not run: it takes 30 minutes, and after S3a nothing on the loader path reaches it. §6 records the pool's recovery as a follow-up |
| BreakfastProvider's report on the candidate build (§8 step 2) | Execution, before the tag |

### 10.3 Investigations

| # | Question | Status | Answer |
|---|---|---|---|
| I1 | Is F3 observable on a real render: `FocusEmphasis.Colored` + one internal-flow link → the focused field's `<text>` is `#000000` after load on HEAD | **Answered** (R12, F14) | Yes: `"focused":` is `#000000` after bind, and stays so through a hover of the link; the `[[https://…]]` hyperlink too |
| I2 | How often a focus-coloured text is DOM-adjacent to a link text (the merged-group limit of §4.2) | **Answered by structure** (F14, §4.2) | Only when the request note's first painted line is a focused field: no header block and a non-JSON body whose first line is focused. Q6 stays with 6.1 |
| I3 | `kronikol merge` of a mergeable report from before the change with a candidate one: old and new header forms in one page | **Answered** (R29) | Yes. On the candidate's merged page the two forms behave identically on hide-headers, Copy box text, the YAML toggle, YAML switched on, the collapsed preview and its tooltip, and the payload copy. On the same inputs merged by today's tool, the new form fails all of them. The old writers were today's build (3.29.3) and 3.1.0, the §3.5 fixtures, not 3.27.2; every release before the change writes the same `<color:gray>`. The run's by-products are F25 and F26 |
| I4 | `NodeJs` with a theme: identical bytes to unthemed? | **Answered** (R13, F10) | No: no bytes at all, a 20 s timeout per diagram. With the hook (R14): identical bytes and the warning on stderr |
| I5 | Worker console messages in Playwright | **Answered** (R16, F13) | Forwarded to `page.on('console')` and `context.on('console')` on 1.59.1 |
| I6 | Does any consumer read `<color:gray>` from `TestRunReport.json`'s diagram sources | **Answered** (R17) | No source in BreakfastProvider or Kronikol4J does; the port's own emitter and fixtures carry it, self-consistently |
| I7 | Does the engine load anything besides `themes.js` by script tag | **Answered** (R19, F15) | Yes: stdlib modules, `openiconic.js` and `emoji.js`, through the same loader and the same unanswered append |
| I8 | Can text Kronikol writes reach that loader | **Answered** (R20, R21, R25; F16, F19) | Yes, on the default configuration: payloads through the gap in `IsCreoleTagStart`; step labels, test identifiers and assertion text through no escaping at all |
| I9 | Is a hang contained to its own diagram | **Answered** (R22, R23; F17) | No. A hung render stops every later render on the same worker or in the same Node process, and when it is the first render on a page, the lazy workers never start |
| I10 | When did each hang start, and was the audit wrong | **Answered** (R27; F21) | `<:name:>` and stdlib hang from 3.0.45, `<&name>` from 3.0.50, and the theme from 3.0.76. The audit's silent no-op was true on its pin and was overtaken four days later |
| I11 | Is the Node renderer's SVG what a report can embed | **Answered** (R26; F20) | No: XML bodies paint blank inline (the default), and a bare `&` breaks the data-URI image. An escaping prototype fixes both and moves no corpus byte (R28) |

---

## 11. Repeating the probe

Everything below is in `DIAGRAM_COLOURS_PLAN.harness/` (`render-probe.js` is the shell with the
`themes.js` route blocked and the console captured; the sources and SVGs are beside it).

```text
# tools/render-bench, headless Chromium via the E2E project's Playwright driver
# (build tests/Kronikol.Tests.EndToEnd once). A page shell that imports the engine as an ES module,
# renders a source, and (for R1) answers 404 for /themes.js while logging console messages.
node render-svg.js core-1.2026.8beta1-0e4f452.js probe.puml       # writes probe.svg
grep -o 'fill="[^"]*"' probe.svg | sort | uniq -c                  # R2: #FEFFDD, #CFECF7, #808080, #686868
grep -c 'href=' probe.svg                                          # R3: 0
```

The probe source is a two-participant sequence with one `[[#iflow-… ]]` arrow label and one note
carrying a `<color:gray>` header, a `<color:#686868>` header, a `<color:blue>` field, a
`<color:lightgray>` field and a `[[https://…]]` hyperlink; the themed variant adds `!theme cerulean`
after `@startuml`. `themes.js` sits in `tools/render-bench/` from the upstream PR work, which is why the
first, unblocked run of the themed variant applied the theme (F6's second half).

The shipped path (second pass), from the repository root; the E2E project built once, network to the CDN:

```text
node plans/DIAGRAM_COLOURS_PLAN.harness/worker-probe.js            # F9, F14: the themed diagram never renders; fills after bind
FAILFAST=1 node plans/DIAGRAM_COLOURS_PLAN.harness/worker-probe.js # F11: with the hook, all three by 2.8 s, byte-identical, the warning on the page console
# the Node renderer as the library runs it (its cache directory carries the CDN tag; node on PATH)
C=$LOCALAPPDATA/Kronikol/plantuml-js/v1.2026.8beta1-0e4f452
node $C/plantuml-render.js $C/viz-global.js $C/plantuml.js < probe-themed.puml   # F10: exit 1, "Timed out … (20000ms)", 25 s
```

The Node hook prototype is a three-line patch of `plantuml-render.js`'s `head:` line; the harness
README quotes it rather than carrying a 20 KB copy of the script.

The third pass, from the repository root:

```text
dotnet fsi plans/DIAGRAM_COLOURS_PLAN.harness/emit-payload-sources.fsx   # writes payload-*.puml through Kronikol's own emitter
node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js                  # F16: Vec<&str> and <:rocket:> never drawn
FAILFAST=1 node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js       # the engine's text instead; the ~< variants draw
SET=poison node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js       # F17: nothing on the page in 400 s
SET=allthemed node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js    # F18: six themed diagrams, none drawn
SET=corpus node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js       # R28: run again with FAILFAST=1, compare the hashes
node plans/DIAGRAM_COLOURS_PLAN.harness/node-svg-probe.js                # F20: parsererror and a broken image for &, blank XML
RENDERER=<escaping copy> node plans/DIAGRAM_COLOURS_PLAN.harness/node-svg-probe.js   # S5's prototype: well-formed, painted
# the Node batch, the mode RenderMany runs (one NDJSON line per diagram: {"id":…,"source":…})
node $C/plantuml-render.js $C/viz-global.js $C/plantuml.js --batch < batch.ndjson
# the two older pins, for R27
curl -sO https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.6-patched/plantuml.js
```

I3 (R29) and the statement series (R30), from the repository root. Build each build's tool first; the
candidate is `s1-candidate.patch` applied to a separate worktree:

```text
dotnet fsi -r:<build>/Kronikol.dll plans/DIAGRAM_COLOURS_PLAN.harness/emit-merge-inputs.fsx <label> <dir>/out-<label> [plain|noflow]
dotnet <build>/Kronikol.Tool.dll merge <dir>/out-*/TestRunReport.json tests/Kronikol.Tests/TestData/Reports/*.mergeable.json -o <dir>/merged-<build>/TestRunReport.html
node plans/DIAGRAM_COLOURS_PLAN.harness/merge-probe.js <dir>/merged-<build>/TestRunReport.html <label>   # prints TWIN lines
SET=statement node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js      # R30, the worker half
node plans/DIAGRAM_COLOURS_PLAN.harness/statement-node.js                   # R30, the Node half's sources
```

Both prototypes are applied to copies, never to `src/`:
- The Node hook is the `head:` line quoted in the harness README.
- S5's escaping is three `escXml` calls in `serializeElement`, also quoted there.

Contrast, in Python, for the table in §3.2:

```python
def lin(c): c /= 255; return c/12.92 if c <= 0.04045 else ((c+0.055)/1.055)**2.4
def lum(h): r,g,b = (int(h[i:i+2],16) for i in (1,3,5)); return 0.2126*lin(r)+0.7152*lin(g)+0.0722*lin(b)
def ratio(fg,bg): a,b = sorted((lum(fg),lum(bg)), reverse=True); return (a+0.05)/(b+0.05)
```

---

## 12. Execution log

**Green-lit 2026-09-25** ("implement P3 in full"). The §9 recommendations were taken as the answers:
- Q1: the 4.5 floor.
- Q2: the new kind.
- Q3: two releases.
- Q4: `NodeJs` included.
- Q5: nearest preceding, with the mode as fallback.
- Q8: fail fast, plus the diagnostic.
- Q9: S4 and S5 stay.
- Q11: F26 is fixed, in the first release. It is a patch-level fix to shipped code, and CLAUDE.md says to
  fix bugs found on the way.
- Q6, Q7 and Q10 stay "not now".

The work was done in its own worktree, from `53b9f6c8`.

### 12.1 First release, 3.29.6 (patch): S3a, S4, S5, F26

**S3a, as written.**
- The worker host answers through the `_onAppend` hook. The Node renderer overrides `head.appendChild`
  (six lines).
- `describeEngineFailure` gains the `loader` branch.
- The `RenderMany` doc comment is rewritten.

Departures:
- **The hook is pinned by behaviour, not by its text.** §5.4 asked for a literal pin in
  `DiagramContextMenuTests`. Instead, `PlantUmlWorkerHostTests` loads the host into a driver, appends a
  script element and checks that `onerror` fires on the next tick. It checks both handler orders, and
  that a non-script element gets no answer. `NodeJsPlantUmlRendererTests` does the same for the Node
  mock, through a stub engine. A literal pin passes on a hook that no longer runs; these do not.
- **The E2E page draws the rest.** "A page whose first diagram needs a bundle still draws the rest"
  became `ThemedSourceRendersTests`' third fact. It shares the class with the theme and include facts,
  on a new `TestPageGenerator.GenerateBrowserJsPage` that takes several sources. The class's third
  §5.4 fact, the themed report with its `OptionNotApplied` entry, joins with S3b.

**S4.**
- `EscapeLoaderMarkup` exists, and the four sites the plan names are covered. The assertion notes are
  escaped once, inside `DiagramWidth.WrapBlockNoteBody`, which both builders (`Track.cs` and
  `InteractionRecord.cs`) call.
- The payload rule went into `EscapeCreoleLine` beside `IsCreoleTagStart`, not into it.

Departures:
- **The parity rule.** A `<` that already follows an odd run of tildes is not escaped again.
  - Measured: PlantUML reads a run of tildes in pairs, and a pair paints as two tildes and escapes
    nothing. One more `~` in front of a user's own `~<&x>` would free the markup the user escaped.
  - The plan's one-line `IsCreoleTagStart` change would have done that. So would `EscapeLoaderMarkup`
    run twice over one text, which happens where a text passes two escapers.
  - `IsTildeEscaped` holds the rule, in C# and in the YAML view's JS copy. The creole-tag escape keeps
    its old behaviour, and no byte of an existing source moves.
- **Four sites the plan missed.** Each was found by following where the same text goes, and each has a
  red-first test:
  - The YAML note view rebuilds a note's lines in the browser (`escapeNoteLine` in
    `collapsible-notes-script.js`). It escaped only tag starts. Switching a note that quotes
    `Vec<&str>` to YAML sent the markup to the engine again. `unescapeNoteDisplayLine`, its reverse,
    reads the three new prefixes.
  - The UI action label (`PlantUmlCreator`), the tracker's own words for a step. It can quote a locator
    or typed text.
  - Internal-flow span names, in both the activity and the Gantt form (`InternalFlowRenderer`).
  - The render-error placeholder's note (`DefaultDiagramsFetcher.EscapeNoteText`). An exception message
    can quote anything.
- **The cap between `~` and `<`** cannot happen for the step bar. `DiagramWidth.WrapLines` never cuts a
  word holding a `<`, and a test pins that.
- **Copy, search and the vectors, as planned.** The `creole-loader-markup-escapes` vector is added
  through `gen-vectors.js`, and the regeneration adds only it. The copy fact runs in the E2E class, on
  the emitter's note, instead of in `NoteCopyFidelityTests`.
- **The LightBDD step.** It is emitted through the report helper's own scenario, not the LightBDD
  fixture. The bar painted `[inputs: ""]` before and paints `[inputs: "<$inputs>"]` now.

**S5, as written**, with one addition. The engine's single processing instruction (the `plantuml-src`
source encoding) was serialised as an element named after the mock's fallback: `<div></div>` inside
the SVG. Inline in an HTML page, a `<div>` start tag inside `<svg>` makes the parser leave SVG content.
The instruction is now dropped, since the report already holds every source.
- `NodeJsSvgFidelityTests.An_inline_svg_ends_where_the_renderer_ended_it` pins the drop.
- The two §5.7 E2E facts are the class's other two.
- `Creole_markup_in_a_captured_body_reaches_the_svg_as_text` reads through `XDocument`, as §5.7 said.

**F26, as §9 Q11 wrote it.** The arrow tests in `extractCallerPayloads` accept a coloured arrow on both
the request and the response line. The E2E fact runs on the emitter's own diagram, where the item was
never offered before.

**Found on the way and fixed (not in the plan):**
- **The raw-PlantUML block unescaped `&`.** The render script's four places that show a source inside
  markup escaped only `<`:
  - the too-large block;
  - the engine-failure `details`;
  - the too-large `code` excerpt;
  - the worker failure path.

  A source holding `&lt;`, `&amp;` or `&copy` was shown decoded. The excerpt also sliced after
  escaping, so it could end in half an entity. All four go through one `escapeMarkupText` (`&`, then
  `<`), and the excerpt is sliced first.
- **A vacuous guard.** `ComponentDiagramReportTests.GenerateComponentDiagramReport_BrowserJs_DoesNotContainC4Include`
  searched the whole page for `!include`. The page carries the render scripts, so the new worker-host
  comment turned it red. It never reached the diagram source either, which is gzipped into
  `data-plantuml-z`. It now decodes the embedded sources and checks them and `result.PlantUml`.

- **`kronikol query` lost a LightBDD step's text.** Found while checking acceptance step 3d with the
  tool: `steps` printed the table step as a bare `AND`. The report writes a step with parameters twice,
  whole as `text` and cut into `textSegments`. The scanner read every `text` key under a step, and a
  segment holding a value or a table reference has a null `text`, so the step came out empty and `grep
  --in steps` could not find it. The scanner now skips the segments. Two red-first facts in
  `QueryCommandTests` use the real writer's output.

**Measured and deferred: the preprocessor reaches captured text too.**
- S4 closes the loader. Captured text also reaches PlantUML's preprocessor, which runs before creole.
  Measured on the pinned engine through the emitter:
  - a body line starting with `'` is dropped as a comment (an SQL `  'a',` line);
  - `/'` opens a block comment (syntax error);
  - `!define`, `!include`, `!theme`, `!assert` and `!ifdef` lines are executed;
  - a `%builtin(…)` call such as `%upper("x")` is evaluated anywhere in a line, and `%getenv` is one of
    them;
  - an `end note` line closes the note (syntax error);
  - `@enduml` splits the source in the page.
- Escapes that paint as written:
  - `<U+0027>` for `'`;
  - `<U+0021>` for `!`;
  - `<U+0025>` for `%`;
  - `<U+0065>nd note`;
  - `~/'`.

  `~'`, `~!` and `~%` do not escape.
- **A literal `~` belongs to the same class.** The payload escaper never escapes a `~`. Measured on the
  pinned engine: a `~` followed by any of `* _ - [ ] # . " / <` is taken as creole's escape, and it is
  missing from the painted text. So `~/.bashrc` paints as `/.bashrc`, and a JSON `"~"` paints as `""`.
  Two `~~` on one line are creole's wave-underline pair. A captured `~<b>x</b>` is emitted as
  `~~<b>x~</b>`, and the `~~` escapes nothing, so the `<b>` tag is live and `x</b>` is drawn in bold.
  The fix is the same kind of escape: `<U+007E>` paints a `~` and escapes nothing (measured).
- Every reader of the text needs a decoder for these, as `~<` has: copy, search, the YAML view, the
  shared vectors and Kronikol4J. It is a class of its own, with its own review surface. It is proposed
  as its own patch after this plan's second release, and is not folded in. It shipped as 3.30.1 (§12.3).

**Docs.**
- The plan pointed the payload rule at `Large-Response-and-Diagram-Handling.md:90`. That is a table of
  width bounds, so the rule went into `Content-Formatting.md`, beside the passage on how a captured
  body reaches its note.
- The §7.5 corrections that 3.29.6 makes true were made with it:
  - `THEME_PLAN` §0, §2.1, and the "Worker registration is load-bearing" note;
  - `ROADMAP.md`'s audit summary row.

  §2.3, §2.6 and §2.7 wait for 3.30.0, which makes them true.

**Acceptance (§8), first release.**
- **3b** is `LoaderMarkupInCapturedTextTests`, on the real emitter and the real engine in the worker.
- **3c and 3e** used a throwaway program that runs the production pipeline
  (`ReportGenerator.CreateStandardReportsWithDiagrams`, `PlantUmlRendering.NodeJs`). Its five scenarios:
  - a 400 quoting `Vec<&str>`, first;
  - a request carrying `<:rocket:>`;
  - an XML body;
  - a path with `?a=1&b=2` and a body saying `fish & chips`;
  - a plain call.

  | Run | Time | `RenderFailure` | SVG | Painted |
  |---|---|---|---|---|
  | The fix, tracking on (inline SVG) | 0.8 s | none | 7 of 7 well-formed XML, no `<div>` | every expected text |
  | The fix, tracking off (data URIs) | 0.8 s | none | 7 of 7 well-formed XML, no `<div>` | every expected text |
  | `53b9f6c8`, before the fix | 124.7 s | 5, every diagram from the first on (20 s timeouts) | 5 of 7 not XML | — |

  `kronikol query summary` read the entries.
- **3d.** On the real thing: `Example.Api.Tests.Component.LightBDD.xUnit3` run on the fix.
  - Its two table steps' bars read `[missingIngredientsFromRequest: "~<$missingIngredientsFromRequest>"]`
    and `[expectedOutputs: "~<$expectedOutputs>"]` (`kronikol query diagram`).
  - That diagram, rendered by the new Node renderer, paints both names and no `""`.
- **Step 5.** The §7.2 wiki pages were re-read against the shipped behaviour, and one sentence was
  corrected: the `NodeJs` theme warning goes to the renderer's stderr, which is not kept. The
  `RenderMany` doc comment was read in the built `Kronikol.xml`.

**Tests.** Every new or changed fact was run red on the old code first: the file set aside, the source
restored, then put back. Where the new code added a member, the old code got a pass-through stub.

**The suites:**
- Core unit project: 5,581 passed, 1 skipped, 0 failed (after the tool fix; 5,579 before it).
- Adapters, all green: StepTracking 43; AssertionTracking 97, 111 and 125 on net8.0, net9.0 and
  net10.0; MSTest 51; xUnit2 12; xUnit3 15; LightBDD.xUnit3 26; TUnit 18; LightBDD.TUnit 25.
- Example.Api, all green: xUnit3 5; LightBDD.xUnit3 6; BDDfy.xUnit3 2; ReqNRoll.xUnit3 8; NUnit4 2.
- E2E (the full project less the wiki GIF, screenshot and showcase classes): 860 passed, 0 failed, in
  6 min 55 s, and again 860 passed, 0 failed, in 6 min 54 s.

### 12.2 Second release, 3.30.0 (minor): S1, S2, S3b

**S1, as §3.2 and §3.3 write it**, the candidate build of I3 made real.
- `WcagContrast` (`Ratio`, `LightestGreyClearing`) and `NotePalette` (`DefaultNoteFill`, `EventNoteFill`,
  `HeaderContrastFloor`, `HeaderInk`, `HeaderTag`) are new in `src/Kronikol/PlantUml/`. The ink is computed
  when the type loads, as the lightest grey from `#808080` down that clears 4.5 on both fills. It comes out
  `#686868`, 5.46 and 4.51 to 1. The default note alone would settle on `#757575`: the event note's fill is
  what sets it, and a fact says so.
- Both emitter sites (`AppendFullPathToNote`, `BatchGray`) write `NotePalette.HeaderTag`.
- The twelve script sites read the tag through one pattern per file, `NOTE_HEADER_TAG`
  (`/^<color:(?:gray|#[0-9A-Fa-f]{6})>/`) and its indented variant, declared at the top of each IIFE.
  `gray` stays in the pattern: a merged report, an ingested capture or a Kronikol4J source still carries it.
- R9 held and is pinned: a captured line that starts like the header tag is escaped by
  `EscapeCreoleMarkup`, so no script reads a body line as a header.

Knock-ons, each with a red-first test:
- **The YAML view.** `reconstructNoteJson` strips header lines before parsing the body. On the literal, a
  new-form note's headers stayed in, the body did not parse, and the note was never offered the YAML view.
  `NoteHeaderFormsTests` and a `NoteYamlInternalsTests` theory run it on both tags.
- **Search.** The normalizers strip any `<color:…>` tag already. The equivalence test gains the new tag,
  and the shared vectors gain `header-ink-tag` (the regeneration adds only it).
- **Fixtures.** `ReportTestHelper`'s 46 hand-written header tags, `HeadersDetailsInterferenceTests`,
  `LargeNoteSplitTests` and the unit fixture move to the new tag. `NoteHeaderFormsTests` keeps both.
- A collapsed note's tooltip is a `<title>` inside the SVG, so `svg.textContent` holds the headers even
  when the preview does not. The fact reads `path > title` for the tooltip and the text elements for the
  preview.

**S2, as §4.2 writes it.**
- Nothing is recoloured until a group of link-coloured text is known to be one of Kronikol's links
  (`[[#iflow-…]]` names it). Blue text no link names keeps its colour (F3).
- A link's rest colour is the nearest earlier text fill that is not the link colour, else the diagram's
  most common text fill, else `#000000` (Q5). Its highlight is the fill the engine painted it in.
- Show-link mode keeps the painted fill and adds the underline, instead of writing `#0000FF`.
- A link whose segment has no data rests like the text around it and is not bound.
- `DiagramContextMenuTests.Internal_flow_link_binding_writes_no_colour_of_its_own` fails if a literal
  `setAttribute('fill', '#000000')` or `'#0000FF'` comes back. Its first cut matched any `#000000` in the
  function and was narrowed to the `setAttribute` calls.
- `IflowLinkColourTests`, six facts: five fail on 3.29.6. The sixth, a real link on the default theme
  resting black and lighting up blue, passes on both, as the no-regression guard.

**S3b, as §5.3 writes it.**
- `DiagnosticKind.OptionNotApplied` is appended after `ReportRotationFailed`, so no existing value moves.
  The schema's enum is generated from `Enum.GetNames`, so it follows.
- `ReportGenerator.RecordOptionDiagnostics` runs after the zero-scenario guard and the background-call
  entries, before the diagnostics snapshot. It records one entry for each theme option set under
  `BrowserJs` or `NodeJs`, and prints the same text as a `⚠ WARNING:` line.
- The message names the mode's own symptom. Under `BrowserJs` it points at the console warning. Under
  `NodeJs` it says the warning goes to the renderer's stderr, which a successful render does not keep,
  as 3.29.6's acceptance found.
- The three `PlantUmlTheme` doc comments say which modes apply a theme.

**The pre-tag audit** (`plan-execution-audit-checklist`) found four gaps, all closed before the tag:
- §5.4 listed two facts the first cut did not have. `OptionDiagnosticsTests` gained the console line,
  read through `ThreadScopedConsole` because the line names no directory. `ThemedSourceRendersTests`
  gained the themed report end to end: a run report written by the whole pipeline, both diagrams drawn,
  one `OptionNotApplied` in the data file. `ReportTestHelper.GenerateThemedRunReport` is the first E2E
  helper that runs `CreateStandardReportsWithDiagrams`. Both facts fail with the call disabled.
- §5.4 also asked the release to rewrite the 6.1 guard's message. It is now the assertion's message, not
  a comment, and names `RecordOptionDiagnostics` and `OptionDiagnosticsTests`.
- Two changelog claims had no test: "a run with no scenarios records nothing" (added; it fails when the
  call moves above the guard) and "one step lighter fails the floor" (added: `#696969` on the event fill).
- The Kronikol4J entry went to `docs/REMAINING_PARITY.md`, where the ledger chain continues, not to the
  README that §7.4 named. The README's chain stops at 3.2.0.

**Docs.** The changelog, as §7.2 wrote it. The wiki clauses listed in §7.2: `Diagnostics-and-Debugging`'s
kinds table, the theme rows of `Report-Configuration`, `Diagram-Customisation`,
`PlantUML-Browser-Rendering` and `Component-Diagrams`, and `PlantUML-Browser-Rendering`'s "gray headers"
wording. The §7.5 corrections that 3.30.0 makes true: `THEME_PLAN` §2.3 (the 4.5 floor, and Phase 0's
item that said 3.0), §2.6 (both header literals, and the one pattern to widen) and §2.7 (the rest-colour
rule), and the `THEME_PLAN` row's note that 6.1 must remove the emitter.

**Acceptance (§8), second release.** Read from the painted SVG in Chromium by a throwaway probe (the
3.29.6 acceptance's twin), never from the page source.
- **2, on real content.** The example API's LightBDD suite (`Example.Api.Tests.Component.LightBDD.xUnit3`:
  real HTTP calls, real internal-flow spans) was run with `FocusEmphasis.Colored` on the Cake request's
  `milk` field:
  - 13 diagrams drawn;
  - 23 header tokens, every one `#686868` on `#FEFFDD`, at 5.46 : 1;
  - 58 link texts, each resting in the ink of the text before it (`#000000`), blue and underlined under
    the pointer, and back at rest after it;
  - 6 blue focus-field texts kept, in the 2 diagrams that also hold links. 3.29.6 painted those black.

  The event note's header came from the 3a program below: `#686868` on `#CFECF7`, 4.51 : 1.
  BreakfastProvider was not run. It consumes published packages, so its report on 3.30.0 can only be
  read once NuGet has the release. `LargeReportFixture` was not used either: its sources are
  hand-written, with no header lines.
- **3a.** A throwaway program wrote a run report through `CreateStandardReportsWithDiagrams`, with header
  lines on both fills, a `[Full path]` block and a focus field, once under each renderer:

  | Run | Time | `OptionNotApplied` | `RenderFailure` | The diagrams |
  |---|---|---|---|---|
  | `BrowserJs`, `cerulean` | 0.3 s | 1 | 0 | 3 of 3 drawn in 214 ms, unthemed; the engine's console warning 3 times |
  | `BrowserJs`, no theme | 0.2 s | 0 | 0 | 3 of 3 drawn; no warning |
  | `NodeJs`, `cerulean` | 0.8 s | 1 | 0 | 3 SVGs, unthemed, headers `#686868` on both fills |
  | `Local` (IKVM), `cerulean` | 5.8 s | 0 | 0 | the theme applies: cerulean's lifeline `#BABDBF` in 3 of 3 |
  | `Local` (IKVM), no theme | 1.8 s | 0 | 0 | `#BABDBF` in 0 of 3 |

  `kronikol query summary` lists `OptionNotApplied ×1` under Diagnostics, beside the temp directory's
  `HistoryUnavailable`, and `kronikol query failures` prints nothing for it. `Server` was not run,
  because it would send the sources to plantuml.com; `Local` shows the claim the message makes. The red
  half, a themed report with no diagram at all, is 3.29.6's (§12.1).
- **4.** I3 again on the release build (harness README, `i3-release*.txt`): the old form written by the
  3.29.3 build, the new form and the merge by 3.30.0, one page each for the default settings, plain arrows
  and no internal-flow tracking, each with the two 3.1.0 fixtures. Every twin agrees on all three pages,
  nine pairs, with no page errors. Against the candidate's rows, every row is the same except `payload`,
  which moved from `none` to `-/-`: the candidate predates 3.29.6's F26 fix. F25 still loses the
  long-path diagram with tracking on, in both forms.
- **5.** The §7.2 wiki pages were re-read against the runs above. One clause was added:
  `Diagnostics-and-Debugging` now says the run also prints the entry as a `⚠ WARNING:` line. The four
  doc comments were read in the built `Kronikol.xml`.

**Found on the way:** `NodeJsPlantUmlRendererTests.Batch_of_five_is_faster_than_five_single_spawns`
failed once in the second unit run, which overlapped the merge probe's Chromium. It compares two
wall-clock times, so a load spike during one half can flip it; alone it passed. It now takes up to three
measurements and passes on the first that shows the batch ahead. Forced to lose, it fails and lists all
three (about 505 ms against 1.2 s each).

**The suites:**
- Core unit project: 5,611 passed, 1 skipped, 0 failed (5,608 before the pre-tag audit added three facts).
- Adapters, all green: StepTracking 43; AssertionTracking 97, 111 and 125 on net8.0, net9.0 and
  net10.0; MSTest 51; xUnit2 12; xUnit3 15; LightBDD.xUnit3 26; TUnit 18; LightBDD.TUnit 25.
- Example.Api, all green: xUnit3 5; LightBDD.xUnit3 6; BDDfy.xUnit3 2; ReqNRoll.xUnit3 8; NUnit4 2.
- E2E (the full project less the wiki GIF, screenshot and showcase classes): 881 passed, 0 failed, in
  6 min 41 s, and again 881 passed, 0 failed, in 6 min 44 s, the second with the acceptance programs
  running beside it.

3.29.6 is green on CI, Release, CodeQL and CI Summary Preview (`ad289f55`), and NuGet has it.

3.30.0 is green on CI, Release, CodeQL and CI Summary Preview (`ea486766`), and NuGet has it.

### 12.3 Third release, 3.30.1 (patch): the preprocessor and tilde escapes

Asked for on 2026-09-25, once 3.30.0 had shipped, together with the BreakfastProvider check below: the
escape class §12.1 measured and deferred, as its own patch. The number was agreed with the session holding
P5 (#100), which takes the next free one. The work was done in the same worktree, from `ea486766`.

**Measured first.** `preproc-probe.js` (harness README) puts one or two captured lines in a note in the
emitter's form and reads what is painted. It ran 214 cases on the pin, once with a plain prefix and once
with Kronikol's, and rendered the same sources under the Java engine (IKVM, PlantUML 1.2024.6) through
`ikvm-render.cs`. What it adds to §12.1's list:
- **Only a line's start counts** for `'`, `/'`, `!`, `@start`/`@end` and `{{`, after any indentation.
  `x 'a'`, `x /' y`, `x !define y` and `x @enduml` paint as written.
- **A directive does different things.** `!define` runs and rewrites the text after it (`FOO here` paints
  `BAR here`). `!ifdef`, `!endif` and `/'` break the diagram. `!assert`, `!log` and `!$x = 1` vanish.
  `!theme` vanishes on the pin, and under Java writes the theme's own source into the note.
- **`!include` reads the local disk under Java.** A captured `!include <path>` drew that file's lines into
  the note (the probe includes a file it writes itself). The pin cannot read a file, and the diagram fails
  with `cannot include`. So under `PlantUmlRendering.Local`, a body quoting an include line put a file from
  the machine generating the report into the report.
- **A builtin call is a known name and a `(`.** `%upper("x")` paints `X`, `%date()` the date, `%true()`
  `1`, and `a%upper("x")b` `aXb`. An unknown name, a space before the `(`, `%Upper`, `%1(`, a URL escape
  (`/f%C3(x)`) and `50%off(today)` paint as written. The escaper takes any `%name(`: a code point where
  the engine would not have acted costs nothing on the page.
- **Every form of the terminator.** A whole line reading `end note`, `END NOTE`, `End Note` or `endnote`,
  indented, with trailing blanks or a tab, closes a `note`, and `end hnote` closes the assertion note's
  `hnote`. The diagram then fails on all three engines: `Syntax Error` at the next line, or
  `Cannot create group` at the emitter's own `end note` when the captured line was the body's last.
  `end note x`, `x end note`, `end ref` and `end` paint.
- **Java only:** a line opening with `@end` or `@start` (`@enduml`, `@endjson`, `@end`, `@endfoo`, not
  `@ENDUML`) breaks the diagram, and a line ending in an odd run of backslashes is joined to the next
  (`abc\` then `def` paints `abcdef`; a trailing blank stops it). The pin paints both.
- **Creole.** `= x` and `==x==` are headings, `| a | b |` a table and `..x..` a separator, each painted
  without its markers. `....`, `--`, `---`, `___` and `==` alone are rules that paint nothing. `* x` and
  `# x` are list items (`# x` paints `1. x`). `a << b >> c` paints `a «b» c`. `&#65;` paints `A`, while
  `&#x42;`, `&amp;` and `&copy;` paint as written.
- **The tilde.** Creole takes `~` as its escape before `/ < . " ] # * _ - [` and paints only what
  follows, and `~~x~~` paints `x`. `~=` and `~|` are not escapes: their tilde paints, so the old heading
  escape `~=` drew a stray tilde.
- **The code point.** `<U+hhhh>`, with four to six upper-case hex digits, paints its character on all
  three and is acted on no further. `<u+0027>` and `<U+27>` paint as written.
- **The character reference is the exception.** Under Kronikol's prefix (teoz and the note wrap width)
  the engine decodes code points before references, so `<U+0026>#39;` still paints `'`. A zero-width space
  after the `&`, `&<U+200B>#39;`, paints `&#39;` on all three.
- **The two escapers write a rule line differently** (j1 to j10). The generator escapes a marker only
  when the line pairs it, and writes `<U+002D>-`. The browser's YAML view escapes every doubled marker, and
  writes `~-~-`. Both paint `--` on all three.

**The change.**
- `PlantUmlCreator.EscapeCreoleMarkup` writes each of these as its code point where a line's content
  starts: `'`, `/'`, `!`, `@start`/`@end`, a whole-line terminator or `{{`, `=`, a table row, a separator or
  a rule. Anywhere in a line, it writes a `%name(` call, a literal `~`, a `<<` with a `>>` after it and an
  odd trailing backslash the same way. A decimal reference gets a zero-width space after its `&`, and a
  bullet or a numbered item keeps its `~`.
- Step names, test names and assertion text keep their markup: `EscapeLoaderMarkup` also escapes a builtin
  call. An assertion note's lines get the line-start escapes through `EscapePreprocessorLine`. That
  replaced the zero-width space `DiagramWidth` put in front of an exact `end note`.
- The lines Kronikol starts itself are escaped the same way: a run the width bound cuts
  (`WrapUnbreakableRuns`), a wrapped assertion line, and each part of the 15,000-character response split.
  That split now cuts between lines (`ChunkNoteAtLineBreaks`), as the browser's splitter does, and a line
  longer than the limit after the last space that fits, never inside `<…>` or after a `~`.
- The browser's YAML view writes the same (`escapeNoteLine`, `escapeContinuationStart`). Its long-run wrap
  and a collapsed note's preview never cut inside `<…>` or leave a `~` at the end of a piece.
- Every reader decodes in one pass from left to right, `~X` or `<U+hhhh>`. It drops the zero-width space
  and keeps a surrogate or a value above U+10FFFF as text. The readers are copy and Open box text
  (`context-menu-script.js`), the YAML view and note copy (`collapsible-notes-script.js`), and search
  (`SearchNormalizer`, `report-search-index.js`, `tools/search-bench/normalize.js`). Search now decodes
  before the ASCII fold, so an escaped letter folds, and the query side skips the pass: a reader types what
  the note shows.

**Found on the way:**
- **The browser's splitter lost a note quoting a diagram.** `splitWithChunkedNotes` decided whether a part
  of a split note had its header with `indexOf('@startuml')`. A payload quoting a PlantUML source mid-line
  made a later part look complete, so that part was drawn without its header and the rest of the note was
  lost. It now reads `@startuml` and `@enduml` off whole lines. On 3.30.0's script, the E2E fact's second
  part came out 408 characters long, and steps 200 and 319 were not drawn.
- **The response split cut at exactly 15,000 characters.** It could cut an escape in two, and it started
  the next part mid-line with whatever character fell there.
- **`~=` drew a stray tilde** in front of a captured line that opened with `=`.
- **An assertion line that the wrap started with a quote** was dropped as a comment. A line reading
  `endnote` or `END NOTE` got past the old check, which caught only an exact `end note`.
- **The probe misread its own error cases.** Its first version found `BEFORE` and `AFTER` inside the
  engine's error picture, which lists the source, and printed the lines between them as painted. Every
  terminator row read as drawn, which is why §12.1's "closes the note" looked wrong for a while. It was
  right. The committed probe reports an error picture as `BROKEN`, and the paint facts in both test
  projects now reject one outright.

**Tests.** Each is red on 3.30.0, with 3.30.0's source or script in place:
- `CapturedTextEscapeTests` (unit, 102 cases): the escaper's rules and their controls, `EscapeLoaderMarkup`,
  the continuation starts, the assertion wrap, and the response split. One fact renders a body holding
  every hazard through the Node renderer on the pin. The class was written before the escaper changed, and
  66 of its cases failed; the rest are controls that pass on either.
- `CapturedTextEscapeTests` (IKVM): the same body under the Java engine, and an `!include` of a temp file.
  On 3.30.0 the first drew an error picture and the second drew the file's `KRONIKOL-SECRET-MARKER`.
- `CapturedTextEscapeTests` (Playwright, four facts): in the render worker every diagram is drawn and paints
  the captured lines, Copy box text returns them, the YAML view paints and copies them, and a split note
  quoting a diagram draws every part. All four failed on 3.30.0's source.
- `NoteYamlInternalsTests`: the YAML view's escaper against the generator for each kind of line (the rule
  lines pinned in its own form), its inverse, and one changed row. All three failed on 3.30.0's script.
- Search and the decoders: six new shared vectors and two changed, `SearchNormalizerEquivalenceTests`, two
  Jint facts over the shipped search script, `DiagramContextMenuTests.Both_scripts_decode_note_source_by_the_same_rule`,
  a changed `NoteCopyFidelityScriptTests` pin and three changed `PlantUmlCreatorTests` rows. On 3.30.0,
  3 Jint facts and 12 unit facts and rows failed.

**BreakfastProvider on 3.30.0.** This is the §12.2 check that had to wait for NuGet. It ran in a scratch
clone with its 30 package pins moved to 3.30.0, and nothing was pushed:
- restore clean; xUnit 203 of 203, LightBDD 178 of 178;
- 409 and 359 diagrams drawn;
- 816 and 816 header tokens, every one `#686868` on `#FEFFDD`, at 5.46 : 1;
- 4,561 and 4,421 links rest, light up under the pointer and settle back as they should, with no blue text
  outside a link;
- no console warnings, no page errors, and no `OptionNotApplied` (it sets no theme).

The first read of the header ink reported 138 tokens on `#E2E2F0`, 34 on `#438DD5` and 9 on `#000000`.
That was the probe, not the report: it took the smallest shape under a token as its background, and those
are a participant's fill and arrowheads drawn before the note. Reading the shape painted last under each
token instead, every token sits on the note fill.

**The suites:**
- Core unit project: 5,718 passed, 1 skipped, 0 failed. Search engine (Jint): 212. IKVM: 51.
- Adapters, all green: StepTracking 43; AssertionTracking 97, 111 and 125 on net8.0, net9.0 and net10.0;
  MSTest 51; xUnit2 12; xUnit3 15; LightBDD.xUnit3 26; TUnit 18; LightBDD.TUnit 25.
- Example.Api, all green: xUnit3 5; LightBDD.xUnit3 6; BDDfy.xUnit3 2; ReqNRoll.xUnit3 8; NUnit4 2.
- E2E (the full project less the wiki GIF, screenshot and showcase classes): 887 passed, 0 failed, in
  6 min 30 s, and again in 6 min 48 s. The first full run failed one `NoteYamlInternalsTests` row, which
  pinned 3.30.0's rule for a payload's own `~<&str>`. That row was changed and the two parity facts added
  before these two runs.

**Docs.** Wiki `27c8fdd`: `Content-Formatting` (the escapes, and copy, the YAML view and search reading them
back), `PlantUML-Browser-Rendering` (the YAML view's tilde caveat is for reports before 3.30.1),
`Step-Tracking` (a builtin call in step, test and assertion text), `Large-Response-and-Diagram-Handling` (the
response split falls between lines) and `Search-Syntax` (search indexes the captured characters and keeps a
query's tildes).

**Released.** 3.30.1 is green on CI, Release, CodeQL and CI Summary Preview (`512bc85a`), and NuGet has it.

### 12.4 The audit, 3.30.2 (patch)

Asked for on 2026-09-25: make sure everything P3 promised is done, and done correctly. Every deliverable of
§3 to §8 and of §12.1 to §12.3 was checked against the code, the tests, CI, NuGet, the documentation and
the port's ledger. The number was agreed with the sessions holding P4 and P5. The work was done in its own
worktree, from `512bc85a`, and each finding was measured before it was fixed
(`DIAGRAM_COLOURS_PLAN.harness/README.md`, the audit section).

**Checked and sound:**
- Every fact §3.4, §4.3, §5.4, §5.6 and §5.7 list exists and ran in CI with full counts, except S2's
  component-diagram fact (below). The twelve script sites read `NOTE_HEADER_TAG`, and S3b's matrix is
  complete.
- Public API: only 3.30.0 added a member (`DiagnosticKind.OptionNotApplied`). ApiCompat against the
  published packages finds nothing added by 3.29.6 or 3.30.1, so their patch numbers are right.
- CI, Release, CodeQL and CI Summary Preview are green on `ad289f55`, `ea486766` and `512bc85a`; NuGet has
  all 62 packages at each version, and the plugin manifests carry them.
- `THEME_PLAN` §7.5's corrections, the stage-0 memory correction and the status rows are in.
- 3.30.2 itself adds no public surface: a metadata diff of its `Kronikol.dll` against 3.30.1's finds the same
  6,473 public and protected members.

**Found, and fixed in 3.30.2:**
- **A redaction regression (3.30.1).** The body has been escaped before a mid-processor sees it since
  3.0.47. 3.30.1's code points made that matter: the wiki's Bearer recipe stops at `<`, so a token holding
  `~` kept its tail (`redact-probe.txt`: 3.30.0 redacts it whole, 3.30.1 leaks `<U+007E>def<U+007E>ghi`).
  The mid-processor now runs before the escaper, and a form body reaches it one whole field per line. What
  it returns is escaped, which is a behaviour change for a processor that returned markup.
- **Headers were never covered by the documented recipes**, on any version: a mid-processor is not given
  them, and a post-processor gets each value cut into 80-character lines, so 642 of a 700-character token
  stayed as a post-processor and all 700 as a mid-processor (`redact-headers-probe.txt`). No code change:
  `CaptureRedaction.Secrets()` and `ExcludedHeaders` exist, and the wiki now leads with them and gives a
  post-processor regex that follows a value across its lines (0 characters left on 3.29.5, 3.30.1 and
  3.30.2).
- **The request label was captured text nobody escaped** (`PlantUmlCreator`, the `{method}: {path}` label).
  With tracking off, `__x__`, `--x--` and `//x//` styled it; with tracking on, the engine ate `~` and ran
  `%date()`, and a `]` in the path ended `extractIflowMap`'s key regex, so a JSON:API `page[size]` link was
  drawn and never opened. `EscapeCapturedLabel` writes them as code points, both brackets always: an escaped
  `]` beside a raw `[` made the engine draw the whole `[[#iflow-… …]]` as black text (`link-fill-probe.txt`,
  `closeOnlyCp`). The page decodes code points in the key, and `TruncateLabel` no longer cuts inside one.
- **Step bars** (`StepBarPlantUml`): a doc string line or a cell lost `~`, ran `%date()`, decoded `&#39;`,
  and drew `..x..`, `....` and `~~x~~` as a separator, a rule and a wave (`bar-probe.txt`). A `{{` line
  swallowed the bar; the Node paint fact found that one.
- **The render-error placeholder** (3.0.45) was `hnote across` with no participant, and every engine drew
  its syntax-error picture instead (`placeholder-probe.txt`, `placeholder-probe-java.txt`). It now declares
  a transparent participant.
- **The tool read note source as written:** `query note` printed tags, markers and code points; `grep --in
  notes` missed code-pointed text; number grep matched `<U+0027>` as 27; `end notes follow` ended a note.
  `NoteSourceText.Decode` (in `Kronikol`, visible to the tool) reads what the note draws.
- **S2's component-diagram fact was missing** (§4.3 item 7), though 3.30.0's changelog claimed the fix for
  sequence and component diagrams alike. `IflowLinkColourTests` has it now; it passes on 3.30.1, so it pins
  shipped behaviour.
- **Doc comments:** `ComponentDiagramOptions.PlantUmlTheme` described a palette that is written only where
  no theme applies (and §6's row said the same; corrected above); the processor options now say what each
  processor is given and whether its output is escaped; `ComponentDiagramReportGenerator` said the C4
  flavour hangs the Node renderer, which 3.29.6 ended; `ReportLowercaseSteps` had lost its summary to the
  method 3.30.0 inserted above it.
- **The wiki:** `$color(gray)` in `Content-Formatting` was never emitted; `SplitLongWords()` was called built
  in and the chained helpers were never defined; the HTML-strip recipe as a post-processor deleted header
  tags and code points; `PlantUML-Browser-Rendering`, `Step-Tracking`, `Large-Response-and-Diagram-Handling`,
  `Component-Diagrams` and `Diagram-Customisation` each had one claim wrong (the changelog lists them).
  3.30.1's changelog says an assertion note's lines are escaped like a payload's; they get the preprocessor's
  line-start escapes only, which 3.30.2's entry corrects.
- **Small:** the template pins stood at 3.22.1 (now 3.30.1), and the shared vectors' comment named
  Kronikol4J among their consumers (the port has no search index; the vectors are its spec).
- **The port's ledger** (`Kronikol4J/docs/REMAINING_PARITY.md`) said the port was unaffected by four script
  defects it still has, and that it would fail vectors it never runs. Corrected there.

**Not written, with the reason:**
- §3.4's legacy fixture for "split-note continuation". Its site, `makeNotesCollapsible`'s `sourceIndexMap`
  (`collapsible-notes-script.js`, the `noteGroups.length < noteBlocks.length` branch), cannot be reached:
  `buildSourceWithNoteStates` gives every note a preview or a non-breaking space, so an SVG always has as
  many note groups as the source has blocks. A mutation that made the site ignore `gray` failed none of the
  ten `NoteButtonIndexTests`, with `gray` variants of the header-only fixtures added; the variants were
  removed again, since they guarded nothing. The other four behaviours have their `gray` coverage in
  `NoteHeaderFormsTests`, over both tags.

**Left to the owner:**
- Span names and UI action labels keep creole by 3.29.6's design (§6, "Full creole neutralisation"): a
  PostgreSQL span holding `~*` or an XPath `//a//b` in a UI action label styles its text.
- The port's four script defects are recorded in its ledger, not fixed there.

**Tests.** Each new fact was red on 3.30.1's source: in the unit project 30 of the 139 facts and rows the
filter ran (every new one but the form-identity and header pins, which hold on either); in the IKVM project
the placeholder fact; in the E2E project 4 of 25 (the step bar, the placeholder, the Bearer recipe and the
bracket link; the component fact passes on either).

**The suites:**
- Core unit project: 5,755 passed, 1 skipped, 0 failed. Search engine (Jint): 212. IKVM: 52.
- Adapters, all green: StepTracking 43; AssertionTracking 97, 111 and 125 on net8.0, net9.0 and net10.0;
  MSTest 51; xUnit2 12; xUnit3 15; LightBDD.xUnit3 26; TUnit 18; LightBDD.TUnit 25.
- Example.Api, all green: xUnit3 5; LightBDD.xUnit3 6; BDDfy.xUnit3 2; ReqNRoll.xUnit3 8; NUnit4 2.
- E2E (the full project less the wiki GIF, screenshot and showcase classes): 892 passed, 0 failed, in
  6 min 54 s, and again in 6 min 35 s.
- Three templates (xUnit v3, TUnit, ReqNRoll on xUnit v3), copied out of the repository, build against the
  3.30.1 packages the new pins name.

**Released** as `8fd58c64` (tag `v3.30.2`): CI, Release, CodeQL and CI Summary Preview green, and NuGet has it.

### 12.5 The second audit, 3.31.2 (patch)

Asked for on 2026-09-26: was anything missed from P3, or not good in its implementation; fix it, without
getting in the way of the session holding P4. The first audit (§12.4) checked each deliverable against the code,
the tests and the documentation. This one attacked the escapes: a differential probe put 13,605 cases of
captured text through the real escapers in five contexts (a note's lines, a request label with and without its
internal-flow link, a step bar's doc string, a table cell), drew each on the pin through the shipped Node
renderer and on the Java engine through IKVM, and compared the drawn text with the captured text. On the
escapers 3.30.2 to 3.30.4 ship, the pin drew all but 126 as captured and Java all but 466. Each class of
difference was then reproduced alone, and every fix form was measured on both engines before it was written
(`DIAGRAM_COLOURS_PLAN.harness/README.md`, the second audit's section). The work was done in its own worktree,
from `9c7a4cd9`. The numbering was agreed with the sessions holding P4 (3.30.4) and `FLOW_NESTING_PLAN` S2 (a
minor, 3.31.0 whenever it lands); the two edit ranges in `PlantUmlCreator.cs` do not overlap P4's.

**Found, and fixed in 3.31.2:**
- **A carriage return inside a line was a line break for the Java engine, past every line-start escape.** The
  escaper splits a body at `\n`; the Java preprocessor also ends a line at a carriage return with no `\n` after
  it. `x`, a carriage return and `!include <sentinel>` drew the sentinel file into the note under IKVM
  (`java-bypass-gen.js`); `@enduml` or `end note` after one broke the diagram, and `'` dropped the rest. NEL,
  LINE SEPARATOR, PARAGRAPH SEPARATOR, VT and FF are inert in a note on both engines. Such a carriage return is
  written `<U+000D>` in a note, in an assertion note's lines and in the YAML view's mirror (`escapeNoteLine`):
  the Java engine draws it at no width and runs nothing, and the pin draws the character (`cand6.json`).
- **A step bar drew `U+200B>` for every backslash under the Java engine, since 3.0.78.** The bar wrote
  `\<U+200B>` after each backslash of a doc string or a cell, and the Java engine reads `\<` inside a one-line
  statement as an escape: 352 of the 466 Java differences (`fuzz-java.txt`). The bar writes
  `<U+005C><U+200B>`. `<U+005C>` followed by the zero-width space as the character draws on both engines too
  (`cand5-pin.txt`, `cand5-java.txt`), and was the first form written, but search reads the diagram's source and
  keeps that character, so a doc string's `C:\temp` could not be found; both as code points draw on both
  engines (`cand7-pin.txt`, `cand7-java.txt`) and search as the text. A code point alone is read back as a
  backslash before the next character.
- **U+0085, U+2028 and U+2029 ended a one-line statement on both engines**, and the diagram was lost: a doc
  string or a cell (`StepBarPlantUml.EscapeInline`), a step name (`EscapeLoaderMarkup`), a request label
  (`EscapeCapturedLabel`). They are written as code points (`cand4.json`).
- **A line break in a test name split the test delimiter's statement**
  (`TrackingDiagramOverride.InsertTestDelimiter`). It is folded to a space.
- **A captured `<U+hhhh>` was drawn as the character it names.** The engines decode four or five hex digits
  in either case after `~<` and after `<U+003C>` alike: 13 cases each in a note and a doc string on both
  engines, and in a label on the pin. A `<U+D800>` in a bar made the Java engine's SVG writer fail. A `<` that
  opens a code point is written `<U+003C><U+200B>`, which keeps it text in all six contexts on both engines;
  every reader drops the zero-width space (`cand4.json`).
- **`\t` in a note was a tab, and a backslash took an escaping `~` with it**, on both engines: `\<b>`, written
  `\~<b>`, drew `~<b>`, and `\[[x]]` lost its `~` the same way. A `<U+200B>` between the backslash and the `t`
  or the `~` keeps both. `PlantUmlCreator`'s comment and `Content-Formatting` said `\t` could not be
  protected; both are corrected.
- **`|_` at a line's start is a creole tree item**, drawn without its marker. The `|` is written as its code
  point in a note (`LineStartEscape`) and in the YAML view's mirror.
- **A collapsed note's tooltip read the source as written** (`collapsible-notes-script.js`, `tipLines`):
  code points, escapes and wrap markers. It now reads like Copy box text.
- **A component diagram's first link rested white.** `bindIflowLinks` rested a link in the ink of the nearest
  earlier text that is not link-coloured. On a component diagram generated with relationship stats, the text
  before the first edge's link is a component's name, `#FFFFFF` with the default palette, so the link rested at
  1.00 to 1 on the page (`link-rest.txt`). It now takes whichever of the nearest texts before and after the
  link group is nearer on the page, its own stats line, `#666666`. §12.4's component fact passed on either
  rule: its hand-written source had no palette. It now renders `ComponentDiagramGenerator`'s own output.
- **Captured text reached two arrows unescaped.** A GraphQL request's label carries the `operationName` read
  from the body's raw JSON text, and a status recorded as thrown (a leading `!`) is drawn as recorded; a line
  break in either ended the arrow's statement, so the rest ran as a line of its own (`!include` under Java).
  `EscapeCapturedLabel` writes a line break as a space, and the response label now goes through it: a status
  that is not thrown is title-cased to its words, so its output is unchanged.

**Found in this release's own escapes, before it shipped:** the width bound (`ChooseCut`) could end a line
right after an escape's zero-width space, so the line ended in two markers, which every reader takes for a break
that had a space in it (`\ tb`, `< U+0041>`). On 3.30.4 the only such escape, `&<U+200B>`, sits after an `&`,
which the cut prefers as a punctuation boundary, so nothing shipped was affected; the guard written first put
`&#39;` beside `\tb` and passed on either for the same reason. The cut now backs up before a trailing
`<U+200B>`, and the guard is a theory that puts each escape alone between letters. Two more, found reviewing the
diff: the first form written for a bar's backslash, `<U+005C>` and the zero-width space as the character, hid a
doc string's `C:\temp` from search, which indexes the diagram's source and keeps that character; the fact written
for it passed on the defect, because xUnit's `Assert.Contains` on two strings compares by culture, and a culture
comparison ignores a zero-width space. The bar writes both as code points, and the fact compares ordinally.
Folding a test name's line breaks threw on a null name, which wrote `Test ` before; it is folded as empty.

**Found outside P3, recorded for the owner:** no generated report passes relationship stats to
`ComponentDiagramGenerator.GeneratePlantUml` (`ReportGenerator`, `MergeableReportRenderer`,
`ComponentDiagramReportGenerator`), and none has since `74b9763c` (2.0.92-beta, 2026-04-14) removed the stats
path. The P50/P95/P99 lines, the error rate and the `[[#iflow-rel-…]]` links exist only when a consumer
computes the stats and passes the diagram in, while `Component-Diagrams` described them as report features.
The session holding P4 corrected its 3.30.4 text (wiki `ec9dc33`); the page's stats section now says so too.
Whether to restore them in reports is in `ROADMAP.md` Appendix C.

**Not fixed, with the reason:**
- A backslash in an arrow label is still read: `\n` breaks the label, `\t` is a tab, and Java reads `\<`
  (`fuzz-*.txt`, label rows). A request path cannot hold one (`uri-probe.fsx`: `Uri` turns it into `/` or
  `%5C`), so it comes only from a GraphQL operation name, a thrown status or an ingested method. The label is
  escaped after Kronikol's own `\n` display breaks are woven in, so escaping its backslashes means building the
  label from escaped parts, in the lines P4's S0 changed. Appendix C.
- The Java engine draws a lone surrogate as U+FFFD. The engine's, and the text is not valid UTF-16.
- `ChunksUpTo` can cut a long path label inside a surrogate pair. Older than P3.
- Under `DiagramFormat.Mermaid`, the `OptionNotApplied` entry for a theme names the wrong reason (it says the
  engine ignores the theme; the theme is never written). Minor. Appendix C.
- A component diagram's method and participant names are written as given. Not captured payload text, and
  outside the sequence-diagram escapes P3 made. Appendix C.

**Left to the owner** (all in `ROADMAP.md` Appendix C, where §6 and §12.4 had left them without a row): Q7
(`FocusDeEmphasis.LightGray` at 1.47 to 1), hung-engine recovery (P4 is its natural owner, and
`ENGINE_PIN_PLAN.md` §7 points at the row), `OptionNotApplied` for the other options `BrowserJs` ignores, Q10
(OpenIconic), full creole neutralisation of step, test, assertion, span and action text, and the port's four
script defects.

**Tests.** Each new fact was red on 3.30.4's source, whose escapers and scripts are 3.30.2's. In the unit
project 21 of the 210 facts and rows in the four touched classes failed: the eleven new rows of the escape
theory (its five controls pass), the eight new facts, and the two pins this release moves (a bar cell's
backslash, and the form a captured `<U+200B>` is written in). The width-bound theory passes on 3.30.4, since it
guards this release's own escapes; against those escapes without the cut rule, three of its five rows failed.
So do the two facts added last, on a bar's backslash searching as the text and a null test name still being
written: each failed on this release's first form of its change, not on 3.30.4.
In the IKVM project the carriage-return and bar-backslash facts failed (2 of 5), and in the E2E project the
tooltip fact and the YAML view's parity fact (2 of 59 in their classes) and the component fact (1 of 8). The IKVM
and unit helpers that read painted text misread the Java engine's self-closing `<text/>` (a run drawn at no
width, which is how it draws a carriage return); both read it now.

**The suites:**
- Core unit project on 3.31.1: 5,878 passed, 1 skipped, 0 failed. IKVM: 54.
- On the tree rebased on 3.31.0: search engine (Jint) 212; adapters, all green: StepTracking 43;
  AssertionTracking 97, 111 and 125 on net8.0, net9.0 and net10.0; MSTest 51; xUnit2 12; xUnit3 15;
  LightBDD.xUnit3 26; TUnit 18; LightBDD.TUnit 25. Example.Api, all green: xUnit3 5; LightBDD.xUnit3 6;
  BDDfy.xUnit3 2; ReqNRoll.xUnit3 8; NUnit4 2.
- E2E (the full project less the wiki GIF, screenshot and showcase classes): 893 passed twice on the first
  base (7 min 50 s, 7 min 56 s), 895 on 3.30.4 and on 3.31.0 (8 min 29 s, 9 min 38 s), and 899 (7 min 59 s) on 3.31.1.
- The differential fuzz was not run again on the final build: the paint facts cover each fixed class on both
  engines, and a second pass costs 14 minutes of the pin and the Java renders beside the timing-sensitive E2E run.
