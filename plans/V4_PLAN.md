# V4 Plan — Mermaid CI Summaries, No Server-Side Rendering, New Report Defaults

Version 4.0.0. Four coordinated changes, all breaking:

1. **CI summaries switch to Mermaid** (autonumber sequence diagram + numbered payload legend) — no more plantuml.com image URLs.
2. **All server-side rendering is removed**: `PlantUmlRendering.Server`, `PlantUmlRendering.Local`, the `Kronikol.PlantUml.Ikvm` package, and every option that exists only for them. Surviving renderers: `BrowserJs` (default) and `NodeJs`.
3. **Headers hidden by default** in HTML reports (new option, configurable).
4. **YAML notes by default** in HTML reports (existing `NotePayloadFormat` option, default flipped).

Investigated 2026-08-30. All file:line references verified against the working tree at that date.

---

## Why (recorded for the changelog / migration guide)

- The CI summary is the **only** remaining consumer of server-side rendering: it emits
  `![diagram](https://plantuml.com/plantuml/svg/{encoded})` ([CiSummaryGenerator.cs:183](src/Kronikol/Reports/CiSummaryGenerator.cs)) regardless of `PlantUmlRendering`. GitHub markdown cannot display locally-rendered images (no data URIs, no inline SVG, no auth-gated artifact URLs — Camo only proxies anonymous http(s)), so the fix is to stop needing an image: GitHub renders ```` ```mermaid ```` fences natively in step summaries.
- Removing the plantuml.com URLs also closes a quiet data leak: today every summary encodes captured request/response content into URLs fetched by plantuml.com and cached by GitHub Camo.
- IKVM exists only to power `PlantUmlRendering.Local`. Its PNG capability has no JS replacement (the TeaVM `@plantuml/core` build is SVG-only — the old CheerpJ `plantuml-core` had `convertPng` but is discontinued; the "PNG" strings in the TeaVM bundle are vestigial `FileFormat` enum constants with no raster pipeline). v4 accepts SVG-only; canvas/`resvg-js` rasterisation is the documented escape hatch if PNG demand ever returns.

---

## Phase 0 — Spikes (before any code)

**0a. Prove Mermaid in a real step summary — RUN, awaiting eyeball (2026-08-31).** Sample summary at `tools/mermaid-spike/sample-summary.md` (6 labelled probes: fence rendering with `autonumber`/`rect`/`loop`/`Note over`; numbered `<details>`+```` ```json ```` legend with one `open`; bare & four-backtick fences; `autonumber 15` continuation; label edge characters; ~120-arrow size probe), emitted by `.github/workflows/mermaid-spike.yml` on branch `v4-mermaid-spike`. Run: https://github.com/lemonlion/Kronikol/actions/runs/33308470844 — **eyeball each probe on the run page** (summaries aren't fetchable via API). Gotcha found: `workflow_dispatch` needs the workflow on the default branch, hence the branch-scoped push trigger. Local pre-validation via `@mermaid-js/mermaid-cli` (latest mermaid, Edge): all 4 diagram blocks parse and render; `autonumber 15` numbers arrows 15/16; the 120-arrow diagram renders completely (~3.4 KB source, far under mermaid's 50 000-char default `maxTextSize`); quotes in participant display names render *literally* (emitter must not emit surrounding quotes); `&`/unicode fine. GitHub pins its own mermaid version, so the run page remains the authoritative check. Delete the spike branch + workflow after Phase 1 lands.

**0b. Component-diagram C4 decision — RESOLVED (2026-08-30): drop C4 in v4.** Spiked against the production pipeline (cached `v1.2026.6-patched` engine via `plantuml-render.js`): `!include <C4/C4_Component>` hangs to the script's 20 s timeout with no SVG, while the plain component syntax renders fine — the TeaVM engine bundles no stdlib, confirming the existing comment at ComponentDiagramReportGenerator.cs:43 on the current build. C4 was only reachable via Server/Local (the modes being deleted; [ComponentDiagramReportTests.cs:277](tests/Kronikol.Tests/ComponentDiagram/ComponentDiagramReportTests.cs) is the sole C4 test and asserts a server URL), and BrowserJs/NodeJs already use plain component syntax, so no surviving mode loses anything. Actions: delete `useC4` plumbing + the C4 branch of `ComponentDiagramGenerator.GeneratePlantUml`, delete the :277 test, list "C4-flavoured component diagrams removed" as a breaking change.

---

## Phase 1 — Mermaid emitter (new code, TDD)

### Architecture

There is **no intermediate diagram model** to reuse: `PlantUmlCreator.CreatePlantUml` ([PlantUmlCreator.cs:111](src/Kronikol/PlantUml/PlantUmlCreator.cs)) is a monolithic `StringBuilder` emitter switching on `trace.Type`. The Mermaid emitter is therefore a **parallel emitter from `RequestResponseLog[]`** — new class, suggested `src/Kronikol/PlantUml/MermaidCiSequenceCreator.cs` (internal; CI-summary-only, not a general `DiagramFormat`).

Do NOT translate emitted PlantUML. Two reasons pinned by investigation:
- Note content at PlantUML-emit time is already PlantUML-flavoured (creole escaping, `<color:gray>` header lines, `WrapUnbreakableRuns` hard breaks). For clean fences, call the structured formatters directly: `TryFormatAsJson` (:869), `TryFormatTruncatedJson` (:896), `GraphQlBodyFormatter.TryFormat`, `JsonFocusFormatter` — and skip `EscapeCreoleMarkup` / `WrapUnbreakableRuns` / `BatchGray`.
- User-injected raw PlantUML (`RequestResponseLog.PlantUml`, spliced verbatim at :199/:226 — step bars from `StepCollector.cs:81`, assertion bars from `Track.cs:447`, tabular bands, test delimiters, `InsertPlantUml`) cannot be translated. Re-render from the structured fields, keyed by `DiagramMarkerKind` ([RequestResponseLog.cs:129](src/Kronikol/Tracking/RequestResponseLog.cs)). Truly opaque user PlantUML (no marker kind) is dropped from the mermaid diagram (documented limitation; the HTML report keeps it).

Reusable as-is: `SequenceCollapser.Apply` (collapse runs + `MaxArrowsPerDiagram` cap), `DependencyPalette` (participant categorisation), the pre/mid/post formatting processors.

### Output contract

Per test, the emitter returns:

```
MermaidCiDiagram(
    string MermaidSource,                  // sequenceDiagram + autonumber + participants + arrows + notes
    IReadOnlyList<MermaidPayload> Payloads // (int Number, string From, string To, string Label, string? Body, bool IsJson)
)
```

`DiagramAsCode` has no payload field — return this new type from a sibling of `GetCiSummaryDiagrams` rather than widening `DiagramAsCode`.

**Numbering invariant (the critical correctness detail):** Mermaid `autonumber` numbers *every arrow line* in source order. The legend must be numbered by counting the emitter's own emitted arrow lines — never by reusing PlantUML's `_stepNumber` or trace indices. Markers become `Note`/`rect` lines (not arrows) so they consume no number, same as PlantUML today (markers `continue` before the increment at :363). A unit test must pin: for a fixture with markers + collapsed loops interleaved, legend numbers == rendered autonumber for first/middle/last arrows.

### Statement mapping (complete inventory from the CI path)

| PlantUML (CI path emits) | Mermaid |
|---|---|
| `!theme`, `!pragma teoz`, `skinparam wrapWidth`, `<style>` blocks | omit (no equivalent needed) |
| `autonumber N` (reseeded per part) | `autonumber` (per-part reseed: `autonumber N` is supported) |
| `entity/database/collections/queue/control/actor/participant "X" as x` (shape from `DependencyPalette.GetSequenceShape`) | **1:1 via mermaid typed participants**: `actor` for User; `participant x@{ "type": "entity" \| "database" \| "collections" \| "queue" \| "control", "alias": "X" }` for HttpApi/Database/Cache/MessageQueue/AI; plain `participant` for Unknown. Verified rendering on latest mermaid (PROBE 7, spike run 33309938739 — stickman + distinct symbols); **if GitHub's pinned mermaid rejects the `@{}` syntax the whole block errors, so gate on the PROBE 7 eyeball**: fallback = plain participants. Display names must be emitted *unquoted* (quotes render literally) |
| participant colour suffix (`#E74C3C`, off by default) | omit (mermaid participant colour needs init-directive theming; not worth it) |
| Request arrow `a -[#438DD5]> b: POST: /x` | `a->>b: POST /x` (colours dropped; GitHub theme provides contrast) |
| Response arrow `b -[#438DD5]-> a: Created` | `b-->>a: 201 Created` — prefer status-code+text if available; `(Redirect)` suffix kept |
| User action `user -[#7D3C98]> web: Click "…"` | `user->>web: Click "…"` |
| Internal-flow link-wrapped label `[[#iflow-… GET: /x]]` | plain label (links don't work in summary SVG) |
| `note left/right` + body (payload) | **no note emitted** — payload goes to the legend, keyed by arrow number |
| Step bar `hnote across <<stepDelimiter>> #black:...` | `Note over <firstParticipant>,<lastParticipant>: <step text>` |
| Assertion bar `hnote across <<assertionNote>> #ccffcc / ✓ …` | `Note over <first>,<last>: ✓ …` (✅/❌ prefix by pass/fail colour) |
| Tabular row band / test delimiter hnotes | `Note over` first,last with the row/test text |
| Setup `partition #F6F6F6 Setup` … `end` | `rect rgb(246,246,246)` … `end` |
| Collapsed run `loop ×5 · 12–48 ms` … `end` | `loop x5 - 12-48 ms` … `end` (mermaid has native `loop`) |
| Arrow-cap `...+7 more calls omitted...` | `Note over <first>,<last>: +7 more calls omitted` |
| Render-error placeholder diagram | single-participant diagram + `Note over` with the ⚠ message |
| Assertion source-location comment `'__^*__:…` | omit |

**Escaping rules for mermaid text** (unit-test each): strip/replace `;` and `#` in labels (`#` starts an entity, `#59;` emits `;` if ever needed); no raw newlines in a message (use `<br/>` only if a label must wrap — normally labels are one line); participant aliases restricted to `[A-Za-z0-9_]` (generate `p1…pN`, display names carry the real text); display names containing mermaid-reserved tokens get quoted-safe handling. `EscapeHtml` (exists in CiSummaryGenerator) still applies to anything landing inside `<summary>`.

### Payloads (legend content)

- Source: the same content that becomes `noteContent` today, but formatted via the JSON/GraphQL formatters directly — **no creole escaping, no wrap-runs, no header lines** (headers excluded from CI summaries entirely; matches both today's truncated CI variant and v4's headers-hidden philosophy).
- Fence language: ```` ```json ```` when `TryFormatAsJson` succeeded, bare fence otherwise. If a body contains ```` ``` ````, use a four-backtick fence.
- Truncation: cap each payload at `truncateNotesAfterLines` real lines (default stays 10 via the existing parameter; now they're *JSON* lines, not wrapped display lines — an improvement worth a changelog sentence) with a `… (+N more lines — see full report)` trailer.
- **The truncated/full dual-pass dies.** It existed because notes lived inside the rendered image. v4: one pass, one diagram, one legend. `GetCiSummaryDiagrams`'s two-pass shape, the `wasTruncated` content-comparison heuristic (:170-171), and the `Truncated/Full Sequence Diagram` details structure all go.
- Size guard: `GITHUB_STEP_SUMMARY` caps at 1 MiB. Track emitted bytes; when the budget (say 768 KiB) is exhausted, stop emitting payload bodies (keep the numbered summaries with a "budget exceeded — see full report" line) and finally stop emitting scenarios (existing `*N more not shown*` line).

### Splitting

- URL-length splitting (`maxEncodedDiagramLength`, `EncodedDiagramExceedsMaxLength`) is irrelevant to fenced mermaid — not carried over. `PlantUmlTextEncoder` itself **stays** (the HTML report path still uses encoded length as its splitting heuristic at PlantUmlCreator.cs:1294).
- Keep a simple per-part **arrow cap** for GitHub's mermaid renderer limits, threshold from spike 0a (expect ~100+ arrows fine; pick conservatively). Parts continue `autonumber <next>` and the legend runs continuously across parts.

### Tests (red first, per TDD)

New `tests/Kronikol.Tests/PlantUml/MermaidCiSequenceCreatorTests.cs`: participant mapping per category; request/response/user-action arrows; status titleising + redirect suffix; marker mapping (step/assertion/tabular/delimiter → Note over); setup rect; collapsed loop; arrow-cap note; numbering invariant with interleaved markers; escaping (`;`, `#`, newline, backticks-in-payload → 4-fence); JSON vs non-JSON fence choice; per-payload truncation; part splitting + autonumber reseed; render-error placeholder; opaque user PlantUML dropped.

---

## Phase 2 — CiSummaryGenerator rewrite

### Shape of the new markdown

Unchanged: `# Diagrammed Test Run Summary`, the metric table, failed-scenario `<details>` with `**Error:**` + stack trace, `## ❌ Failed Scenarios (N)` / `## Sequence Diagrams` split, `MaxCiSummaryDiagrams` cap, `*N more not shown*` lines.

Replaced (`AppendDiagramImages`, :159-255): per scenario →

````
```mermaid
sequenceDiagram
    autonumber
    ...
```

<details><summary><strong>3</strong> Orders → Inventory · POST /inventory/reservations</summary>

```json
{ ... }
```

</details>
...
````

For a **failed** scenario, emit the payload `<details>` nearest the failure `<details open>` (heuristic: the last arrows before the failure, or all when ≤4) — restores the "failure at a glance" value the old `<details open>` truncated image had.

Dropped: `![diagram]` images, `PlantUmlTextEncoder.Encode` calls, `plantUmlServerBaseUrl` parameter, the dead `diagramFormat` + `localDiagramRenderer` parameters (never read — confirmed), `DeactivateUrls` + `UrlProtocolRegex` + the `partial` keyword + `System.Text.RegularExpressions`/`Kronikol.PlantUml` usings, the ```` ```plantuml ```` source fences (mermaid fence is the source; full fidelity lives in the HTML report artifact). Kept: `EscapeHtml` (summaries), `EscapeMarkdown` (error line), `FormatDuration`.

### Azure DevOps

Verified 2026-08-30: Azure DevOps **does** support Mermaid, but per surface, and the pipeline summary tab is the one surface with no evidence of support:

- **Wiki**: long-standing support; both `::: mermaid` containers and standard ```` ```mermaid ```` fences render (current markdown-guidance doc). Sequence diagrams supported, with "limited syntax support" caveats (most HTML tags unsupported, etc.).
- **Repo markdown file previews** (README/ADRs): native Mermaid since the December 2025 release (previously wiki-only / third-party extensions).
- **Pipeline run summary tab** (`##vso[task.uploadsummary]`): Microsoft's logging-commands doc says this tab's markdown rendering "is different from wiki rendering" and documents only a limited subset; no documentation or community report shows Mermaid rendering there.

**Design consequence — no environment seam needed.** Emit standard ```` ```mermaid ```` fences unconditionally: GitHub renders them; Azure wiki/file surfaces render them; on the Azure summary tab they degrade gracefully to a plain code block (readable source, not breakage), and they light up automatically if Microsoft extends Mermaid to that surface — the December 2025 expansion suggests the direction of travel. This is simpler than the environment-branching alternative (today Detect() runs *after* generation anyway — ReportGenerator.cs:305 vs :312 — and the identical markdown already goes to both targets, HTML `<details>` included). If Azure summary fidelity ever matters, the fix is a rendering check on a real org first (Azure's Mermaid syntax subset — e.g. `autonumber` — should be verified where it *does* render before assuming parity with GitHub).

### Wiki screenshot blocker

`WikiGifTests.Feature09_CI_Summary_Screenshot` ([WikiGifTests.cs:1103-1165](tests/Kronikol.Tests.EndToEnd/WikiGifTests.cs)) renders the summary via `marked.min.js` and scrolls to the first `<img>`. Fix when regenerating assets: add `mermaid.min.js` from jsDelivr to the preview page (it already loads marked from jsDelivr), run `mermaid.run()` over rendered `code.language-mermaid` blocks, scroll target → the first `.mermaid svg` / rendered diagram. Regenerate `whats-new-ci-summary.png`.

### Test changes

- Rewrite the URL-coupled facts in `CiSummaryGeneratorTests` (:40, :60, :107, :137, :156, :231 — assert mermaid fence content / legend entries instead of `Encoded(...)`); delete :289/:301 (`DeactivateUrls`); rewrite the fence-structure facts (:311-:382) for the new single-pass shape; keep the format-agnostic facts (:29-:278) as-is.
- `CiSummaryWriterTests`, `ReportGeneratorCiSummaryTests`, `ReportGeneratorCiArtifactTests`, `ReportsFolderPathTests:59` — unaffected (verified format-agnostic).
- New facts: failed-scenario payload `<details open>`; 1 MiB budget guard.
- **Close a discovered coverage gap:** `GetCiSummaryDiagrams` has exactly one caller (ReportGenerator.cs:304) and no direct tests — every existing test hand-builds `DiagramAsCode` arrays for `GenerateMarkdown`, so nothing pins `truncateNotesAfterLines: 10`, `excludeAllHeaders: true`, or the encoded-length budget at the fetcher layer. The replacement fetcher method (the Mermaid sibling) gets direct unit coverage for payload production, truncation default, and header exclusion — a regression there today would land silently.
- CiPreview example projects unchanged (they only set `WriteCiSummary = true`); push and eyeball via `ci-summary-preview.yml` — this is the real acceptance test.

---

## Phase 3 — Remove Server + Local/IKVM

### Enum & options (public API breaks)

- `PlantUmlRendering`: delete `Server` (ordinal 0!) and `Local` → `BrowserJs` becomes ordinal 0, so `default(PlantUmlRendering)` lands on the v4 default. Current defaults are already `BrowserJs` everywhere (DiagramsFetcherOptions.cs:28, ReportConfigurationOptions.cs:86, IngestPipeline.cs:283, IngestCommand.cs:23, ReportGenerator.cs:415) — no default changes needed.
- Delete options: `PlantUmlServerBaseUrl`, `LocalDiagramRenderer`, `LocalDiagramImageDirectory`, **`PlantUmlImageFormat` (entire enum + both options)**. Verified: Png/Base64Png are unreachable without Server/Local; NodeJs and BrowserJs ignore the option entirely; the sole internal use (component diagram base64-vs-file choice, ComponentDiagramReportGenerator.cs:31-33/76-95) becomes a bool or hard-coded file output. `NodeJsPlantUmlRenderer.Render`'s format parameter → drop or reduce to a bool base64 flag.
- Keep `InlineSvgRendering` (NodeJs-only semantic; the `InternalFlowTracking` force-set at ReportGenerator.cs:135-142 simplifies to NodeJs-only).

### Code deletions/edits

- `DefaultDiagramsFetcher`: delete `GetServerRenderedDiagrams` (:181), `GetServerRenderedInlineSvgDiagrams` (:201, the file's only HttpClient), `GetLocallyRenderedDiagrams` (:217), `RenderLocally` (:291), `RenderLocallyAsInlineSvg` (:418). Switch (:39-45): `BrowserJs` / `NodeJs` arms + default→BrowserJs. `maxEncodedDiagramLength` ternary (:235) → unconditional 8000.
- `ComponentDiagramReportGenerator`: delete the server-URL branch (:97-103) + `plantUmlServerBaseUrl` param; `useJsEngine` → `!useBrowserJs`; apply the Phase-0b C4 decision.
- `PlantUmlCreator`: delete the dead `ImageTags` machinery (`GetPlantUmlImageTag` :1339, record member :1350, `plantUmlServerRendererUrl` :34 — produced but consumed only by tests).
- CLI: `--render` accepts `browserjs|nodejs` only (IngestCommand.cs:68-73, :391-400, help :405/:416). Removing `local` fixes a latent bug (`--render local` currently throws — CLI never sets the delegate). Env hook `KRONIKOL_PLANTUML_SERVER_BASE_URL` (Example.Api.Tests.Component.Shared/IntegrationTestConfiguration.cs:38-42) dies.
- Stale user-facing strings: plantuml-browser-render-script.js:110 & :1012 ("Use PlantUmlRendering.Server or .Local for large diagrams" → rewrite advice, keep the first sentence — DiagramContextMenuTests:973 and BrowserRenderWorkerTests:329 pin it); DefaultDiagramsFetcher.cs:185/:221 IKVM install hints (methods die anyway); internal-flow-popup-script.js:124 comment; ReportConfigurationOptions.cs:76; PlantUmlRendering.cs:8; PlantUmlCreator.cs:399/:638 comments.

### IKVM package removal

- Delete `src/Kronikol.PlantUml.Ikvm/` (incl. `PlantUml/plantuml-mit-1.2024.6.jar`) and `tests/Kronikol.Tests.PlantUml.Ikvm/`.
- `Kronikol.sln` (:32, :34, config blocks :396-419), `release.slnf:53` (drops it from `dotnet pack`/push in release.yml).
- `ci.yml`: delete the `PlantUml IKVM Tests` matrix entry (:77-79), the `Free disk space (IKVM)` step (:172-180, exists solely for this job), and the `Kronikol.Tests.PlantUml.Ikvm` entry in the auto-discovery SKIP list (:244). Leave the unrelated disk-space steps in release.yml/codeql.yml.
- **Statement-limit ground truth**: `IkvmStatementLimitTests` is the executable evidence behind `PlantUmlStatementLimits` (which caps are real PlantUML's vs TeaVM-build artifacts). Preserve as documentation: copy the four findings (2,000-char message limit is PlantUML's own; block-opener ~1,476 and coloured-note-bar ~1,458 are JS-build artifacts; long-note behaviour) into `PlantUmlStatementLimits.cs` XML docs (fixing the :68 citation) and the wiki. If re-verification is ever needed, a plain `java -jar plantuml.jar` script beats resurrecting IKVM — note this in the doc comment.

### Test changes

- Delete: `LocalDiagramRenderingTests` (9 facts), `DefaultDiagramsFetcherTests` (3 server-URL facts), `ComponentDiagramReportTests` server/local facts (:127, :157, :170, :183, :238; :277 per Phase-0b), `ConfigurationOverrideTests.CustomPlantUmlServerBaseUrl_AppearsInDiagramImgSrc` (:188-210), IngestCommandTests `server` parse assert (:137), PlantUmlCreatorTests ImageTags/server-URL block (:915-:971, :1518-:1533, :2701-:2724).
- Re-point `DiagramFailureIsolationTests` (:64-98): it uses a throwing `LocalDiagramRenderer` as the cheap failing-renderer seam. The per-scenario isolation behaviour must survive — replace with a NodeJs-path seam (e.g. an internal render-func hook on `RenderNodeBatchIsolated`, or an unrenderable diagram fixture).
- Swap the `PlantUmlServerBaseUrl = "http://custom-server.com"` smoke values in the four LightBDD options tests for another property.

---

## Phase 4 — Report default flips

### 4a. Headers hidden by default (new option)

No option exists today — `window._headersHidden = false` is a hardcoded literal (collapsible-notes-script.js:1630) and hiding is a source-rewrite + re-render, not CSS. Follow the `NotePayloadFormat` plumbing pattern exactly:

1. `ReportConfigurationOptions.HideHeadersByDefault` — `bool`, **default `true`** in v4. XML-doc it as BrowserJs-only (like NotePayloadFormat's doc at :307).
2. Thread as `GenerateHtmlReport` parameter beside `notePayloadFormat` (ReportGenerator.cs:439), callers :250/:255, merge path `MergeableReportRenderer.cs:70`.
3. New `__HEADERS_HIDDEN_DEFAULT__` token at collapsible-notes-script.js:1630, substituted in `DiagramContextMenu.GetCollapsibleNotesScript` (:90-92).
4. Toolbar buttons must match the default or the UI lies: hoist the six copy-pasted `Headers Shown` literals (ReportGenerator.cs:920, 1290, 1306, 1331, 2176, 2192) into one built variable (the `noteFormatOptions` pattern at :579-586) emitting `data-shown`/label/`details-active` from the option.
5. Optional polish: a `hasHttpHeaders` content probe (mirror `hasJsonNotePayloads` :571-573) so the button is suppressed when no note has gray lines.
6. CLI: `kronikol ingest --headers shown|hidden` beside `--note-format` (IngestCommand.cs:109-119).

Interaction check: header-only notes vanish from the SVG when hidden — the `sourceIndexMap` remapping (:711-741) and `isLongNote` gray-line skipping (:202-216) already handle a hidden initial state (they key off `container._headersHidden`, seeded from the global at :1895/:2100), but E2E must prove the *initial* hidden render, not just toggle-to-hidden.

Tests: flip `ReportToolbarTests.Headers_shown_is_active_by_default` (:155) and `DiagramContextMenuTests.Globals_headersHidden_defaults_to_false` (:342 — becomes a token-substitution test like `NoteFormatToggleScriptTests:87-98`); `HeadersDetailsInterferenceTests` clicks `[data-shown='true']` throughout — either generate those fixtures with `HideHeadersByDefault = false` (testing the toggle mechanics unchanged) or update selectors; also touched: `NoteButtonsAfterHeaderHideTests`, `NoteExpandArrowHeaderHiddenTests`, `DiagramNoteBasicTests:110-125`, `ToggleButtonPendingTests`, `MobileResponsiveTests:234`. New E2E: default-hidden initial render (headers absent, note buttons/indices correct, toggle to shown works).

### 4b. YAML by default (default flip only)

The option shipped in 3.0.66 and is fully plumbed (`ReportConfigurationOptions.NotePayloadFormat`, `--note-format`, `__NOTE_FORMAT_DEFAULT__` token, dropdown `selected`, single init point at collapsible-notes-script.js:1874). v4 changes:

1. `ReportConfigurationOptions.cs:312` → `= NotePayloadFormat.Yaml`.
2. Align mirrored defaults: `ReportGenerator.cs:439` param default, `MergeableReportRenderer.cs:70`, `IngestCommand.cs:28`.
3. `DiagramContextMenu.GetCollapsibleNotesScript()` parameterless overload (:83) hardcodes Json — make it follow the v4 default so it doesn't lie, and update the ~40 structural assertions in `NoteFormatToggleScriptTests`/`DiagramContextMenuTests` that ride on it.
4. Do **not** touch the `|| 'json'` fallbacks in JS — "unset means JSON" is load-bearing for ineligible notes; YAML-default works by explicit `setAllNoteFormats` stamping (already proven end-to-end by `NoteFormatDefaultTests`).

Tests: flip `ReportConfigurationOptionsDefaultsTests.NotePayloadFormat_defaults_to_Json` (:26-31); `NoteFormatDefaultTests` inverts into a Json-override suite; close the known gap (PLANS_STATUS.md:90): add `IngestCommandTests` coverage for `--note-format` (and the new `--headers`) parsing.

---

## Phase 5 — Docs, versioning, release

**Wiki (`../Kronikol.wiki`)** — no page mentions "mermaid" today:
- `CI-Summary-Integration.md`: full rewrite (it currently documents behaviour that never existed — NodeJs/Local inline-base64 summaries at :56/:105/:112-137/:140-149, `PlantUmlRendering` as a CI knob at :189, plantuml URL examples at :213/:219, `darkred` markup at :242-266, the Azure "content is the same" claim at :279 which becomes actively wrong).
- Delete/redirect: `Integration-PlantUML-IKVM.md`, `PlantUml-Server-Configuration.md`. Consistency pass: `PlantUML-Browser-Rendering.md` (references a nonexistent `CiSummaryPlantUmlRendering` option at :182), `Inline-SVG-Rendering.md`, `Large-Response-and-Diagram-Handling.md`, `Component-Diagrams.md`, `Generated-Reports.md` (:13, :446-464), `Report-Configuration.md`, `API-Reference.md` (:86 stale signature), `FAQ.md`, `Diagram-Customisation.md`, ~15 `Integration-*.md` quick-starts naming plantuml.com, `Home.md`/`_Sidebar.md`/`How-To-Guides.md` nav.
- New page: **Migrating to v4** (table: removed API → replacement; default flips + how to restore v3 behaviour: `HideHeadersByDefault = false`, `NotePayloadFormat = NotePayloadFormat.Json`).
- README.md (:102, :134-140, :284 package table, :311 wiki link), nuget-readme.md (:47 row).
- Regenerate `whats-new-ci-summary.png` (Phase 2).

**Kronikol4J**: report-asset byte-compat diverges further (already diverged since 3.0.45); the Java port keeps its own summary/rendering. Note in its README parity section — no code action in this repo.

**Release**: bump **all** packages to 4.0.0 (same number everywhere per project convention), CHANGELOG with an explicit **Breaking changes** section (removed: `PlantUmlRendering.Server/.Local`, `PlantUmlServerBaseUrl`, `LocalDiagramRenderer`, `LocalDiagramImageDirectory`, `PlantUmlImageFormat`, `Kronikol.PlantUml.Ikvm` package, `--render server|local`, `KRONIKOL_PLANTUML_SERVER_BASE_URL`; changed: CI summary format, headers hidden, YAML default; possibly C4 per Phase 0b), full test suite, commit, tag `v4.0.0`, push commit + tag.

---

## Open questions (decide during implementation)

1. ~~Phase 0b outcome — C4 component diagrams~~: resolved, drop (see Phase 0b).
2. ~~Participant category signalling~~: resolved pending PROBE 7 eyeball — mermaid typed participants (`@{ "type": "database" }` etc.) map 1:1 onto Kronikol's PlantUML shape set; fallback to plain participants only if GitHub's mermaid version rejects the syntax (the error takes down the whole block, so this is all-or-nothing per GitHub's version).
3. Failed-scenario `<details open>` payload heuristic — last-N-before-failure vs all-when-few.
4. Azure DevOps summary-tab fidelity: fences are emitted unconditionally (degrade to code blocks there today) — if an Azure user reports wanting rendered diagrams, options are an Azure wiki-publish step or waiting on Microsoft extending Mermaid to the summary tab (wiki + file previews already render it as of Dec 2025).
5. Whether the parameterless `GetCollapsibleNotesScript()` overload should exist at all post-v4 (it exists for tests; consider deleting in favour of the explicit overload).
6. **Toggle-defaults unification (from TOGGLE_DEFAULTS_PLAN, shipped 3.0.80).** Two candidates:
   (a) fold the flat `NotePayloadFormat` option into the `ReportToggleDefaults` group (breaking —
   today the group value wins when set and the flat property stays the simple both-reports knob);
   note items 3/4 above become one-liners via the group built-ins once folded. (b) Unify the two
   coexisting Specifications conventions: the old flat pairs (`SpecificationsShowStepNumbers`,
   `SpecificationsDataFormat`, …) are independent values that do NOT inherit, while
   `SpecificationsToggleDefaults` inherits-unless-overridden — converting the flat pairs to the
   inheriting group model is a breaking change deferred to v4.
7. **The monospace note control (from NOTE_APPEARANCE_CONTROLS_PLAN, 3.22.0).** Shipped default-visible in
   3.0.85 on a plan recommendation, withdrawn to opt-in behind `ShowNoteFontControls` in 3.22.0. Two candidates:
   drop it outright (removes `ShowNoteFontControls`, `NoteFontFamily`, `ReportToggleDefaults.NoteFont`, the
   `kronNoteMono` class and both handlers; breaking, so only here), or keep it opt-in as it is. Decide on
   whether anyone has set the option by then. No recommendation yet.
