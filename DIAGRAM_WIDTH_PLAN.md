# Diagram width — the crop nobody sees

Opened alongside the 3.0.83 component-diagram fix. **Executed in 3.0.85** (2026-09-11); the
component-diagram half had already shipped in 3.0.83. Everything below is measured.

**What shipped, against the ranked list below:** #1 (user-action label), #2 (internal-flow activity
diagrams — `skinparam wrapWidth` *and* a character budget, on node labels and swimlane names), #3
(step bar, which now switches to the styled form when its label has to be broken), #4 (table cells,
elided against a shared **row** budget — one cell's budget is not enough, because a row is the sum of
its cells), #5 and #6 (the two note bodies that bypassed the formatter), #7 (participant names), #7b
(participant count), #8 (titles, in both the generator and the differ). **#9 stays documented rather
than fixed**, by design: `InsertPlantUml` is arbitrary user-authored PlantUML.

All four items of *Shape of a fix* landed, the first as `PlantUml/DiagramWidth.cs` — the wrapper
`ComponentDiagramGenerator` had grown for 3.0.83, lifted out and now shared by every emitter.

**Two corrections the execution forced.**

1. **Splitting is not a width bound on its own.** `CreatePlantUmlPrefix` builds from the whole test's
   trace list, so every fragment re-declared every participant the test ever touched and each one came
   out as wide as the unsplit diagram. A participant-count guard is useless without this; each
   fragment now declares only the participants it draws.
2. **The participant guard cannot be gated on `!clientSideSplitting`** like the other two. Encoded
   length and estimated height both change with note state, which is why `BrowserJs` leaves them to
   the browser; participant count does not, so the guard applies on both paths — otherwise the default
   renderer keeps the axis.

**One axis this work did NOT close, recorded honestly:** internal-flow activity diagrams have no
**height** guard at all (~57px per node, so about seventy nodes crop vertically whatever their labels
say), and wrapping a long label trades width for height. `RenderActivityDiagramBatched` caps at three
batches, so making the batcher height-aware would start hiding content rather than showing it — a
separate decision, not a silent one.

## The defect class

PlantUML's default `PLANTUML_LIMIT_SIZE` is **4096 pixels**. A diagram wider than that is not rejected —
it is **cropped**, silently, keeping the top-left corner and discarding the rest. There is no error in the
output and nothing in the log. From a user's side the diagram is simply missing most of what it should
show, which reads as "the diagram doesn't render".

**Measured 2026-09-11, and it narrows this plan's scope: the limit is raster-only.** The same source
through the IKVM renderer (no limit set, jar default in force) drew **SVG at 24 185 px, uncropped**,
and **PNG at exactly 4 096 px**. So the crop bites `PlantUmlImageFormat.Png` and nothing else — and
never the report itself, whose browser engine runs at `{ maxSvgSize: 98304 }`. With
`PlantUmlRendering.Server` being phased out, the remaining exposure is `Local`/`NodeJs` with PNG, and
a reader who copies a source out and rasterises it.

Kronikol has character caps (`PlantUmlStatementLimits`) but **none of them is a width bound**. Measured
against real Java PlantUML (`plantuml-mit-1.2024.6.jar`, the jar shipped for IKVM):

| cap | what it is for | width at the cap |
|---|---|---|
| 2000 — message statement | parse limit (PlantUML's own) | ≈ 11–12 000 px |
| 1471 — block opener | JS-build parse limit | ≈ 10 600 px |
| 1400 — coloured note bar | JS-build stack overflow | ≈ 10 200 px |
| 16000 — note line | backstop | ≈ 100 000 px |

They are parse and crash caps. Nothing anywhere bounds how wide a diagram draws.

### Which engines it reaches

`PlantUmlImageFormat` defaults to `Png` (`ReportConfigurationOptions.cs`), so under `Server` and `Local`
rendering the crop bites by default. Under `BrowserJs` the embedded TeaVM engine wraps some things Java
does not, so the in-report render can look fine — but the source still reaches real PlantUML the moment
anyone uses the context menu's *Copy / Open PlantUML source*, which is exactly how 3.0.83 was reported.

### What `skinparam wrapWidth` actually does (measured)

| construct | does `wrapWidth` wrap it? |
|---|---|
| message / arrow labels | **no** (1960-char label with spaces → 11 977 px) |
| `loop` / `group` labels | **no** (1460-char label → 7 976 px) |
| participant boxes | **no** in sequence diagrams; **yes** in component diagrams, but only at whitespace |
| note bodies, `hnote across` bars | **yes**, but only at whitespace |
| activity diagrams | Kronikol emits no `wrapWidth` there at all |

`skinparam maxMessageSize` *does* wrap message labels (a possible knob), but again only at spaces.

## Fixed in 3.0.83

- Component-diagram **edge labels** — wrapped at 100 characters. Reported: 6697 px → 1240 px.
- Component-diagram **participant names** — wrapped at 80 characters, with creole bold reopened per line.

## Not fixed — ranked by how reachable each is

Slopes and crossings are measured, not estimated; repro `.puml` files were built for each.

**1. Sequence-diagram user-action label — `PlantUmlCreator.cs:262-264`.**
The one message label in the codebase that is neither chunked nor constant. Bounded only by the 2000-char
parse cap. Text comes from `InteractionRecord.UserAction(… label …)` — Playwright locator chains and UI
adapter action strings, routinely 150–400 characters. ≈5.6 px/char, **crops at ≈730 characters**
(measured: 620 chars → 3488 px; 1656 → 9168 px; PNG at 1970 chars = 4096×212, cropped).

**2. Internal-flow activity diagrams — `InternalFlowRenderer.cs:111-113` (label), `:105`, `:19-22`, `:72-75`.**
The only diagram family with **no character cap at all** and **no `wrapWidth`**. The label is
`Span.DisplayName ?? Span.OperationName`, touched only by `EscapePlantUml` (which escapes `|` and `;`).
≈6.0 px/char, **crops at ≈670 characters**; a real EF-shaped SQL `DisplayName` of 197 chars already draws
1584 px. Adding `skinparam wrapWidth 800` to the two header emitters alone fixes it (measured 6115 → 834 px).

**3. Step-delimiter bar, coloured form — `StepBarPlantUml.cs:108-109`.**
Safe with spaces (`wrapWidth` applies to notes): a 1340-char step draws 843 px. Not safe with an unbroken
run — a Gherkin step quoting a JWT, base64 blob, URL or GUID list. ≈7.2 px/char, **crops at ≈560
unbroken characters**.

**4. Step bar rich form, creole table cells — `StepBarPlantUml.cs:117-123`, `:142-153`.**
Creole table cells never wrap, **not even at spaces**, so this is more reachable than #3: one ordinary
data-table cell holding SQL or JSON is enough. ≈6.7 px/char, **crops at ≈590 characters in one cell**, and
a row is the sum of its cells.

**5. Assertion notes — `Track.cs:441-447`, `InteractionRecord.cs:299-303`.**
The body never passes through `FormatNoteContent`, so it misses `WrapUnbreakableRuns`. Spaces are fine
(1200-char message → 821 px); an unbroken run is not. **Crops at ≈760 unbroken characters** — an
assertion diff quoting a token, minified JSON or a connection string.

**6. User-action note body — `PlantUmlCreator.cs:266-272`.**
`trace.Content` bypasses `FormatNoteContent`, so it gets neither `EscapeCreoleMarkup` nor
`WrapUnbreakableRuns` — the protection every request/response note has. `TruncateNoteContent` caps line
*count* only, and defaults to off. **Crops at ≈600 unbroken characters.** Secondary: unescaped creole in
a UI detail string.

**7. Sequence-diagram participant declarations — `PlantUmlCreator.cs:707-712, 716-719, 757-762, 767-771, 784-789.**
`ServiceName` / `CallerName` verbatim; the statement guard classifies these as `Other`, so no cap, and
sequence participant boxes never wrap. Same defect as the component-diagram participant names fixed in
3.0.83, different site. **Crops at ≈650 characters.**

**7b. Participant *count*, same emitter.** Width accumulates ≈144 px per participant: 10 → 1455 px,
20 → 2893 px, **30 → 4331 px**. There is a height guard (`MaxEstimatedDiagramHeight = 12_000`) but **no
width or participant-count guard**, so about 28 participants crop even with short names. This one needs
no pathological input at all.

**8. Component-diagram title — `ComponentDiagramGenerator.cs` (`title {options.Title}`), same in
`ComponentDiagramDiffer.cs`.** Titles do not wrap: a 500-char title draws 3286 px, ≈6.5 px/char, crossing
at ≈630. Only reachable through a user-configured `ComponentDiagramOptions.Title`.

**9. `DefaultTrackingDiagramOverride.InsertPlantUml`.** Arbitrary user-authored PlantUML, unbounded by
design. Worth documenting rather than fixing.

## Shape of a fix, if it is taken up

1. A shared width-budget wrapper, the way `ComponentDiagramGenerator.Wrap` now works: break at atoms, then
   at whitespace, hard-break only tokens that carry no creole markup.
2. `skinparam wrapWidth` on the internal-flow activity headers — the cheapest single win (#2), and it costs
   one line.
3. A participant-count guard alongside `MaxEstimatedDiagramHeight` (#7b) — the only entry here that a
   perfectly ordinary test suite can hit.
4. Route the two note bodies that bypass it (#5, #6) through `FormatNoteContent`, which already has
   `WrapUnbreakableRuns`.

Whatever is done, the regression test that matters is the one 3.0.83 added: render through **real** PlantUML
(the IKVM jar) and assert the drawn `viewBox` stays under 4096. Asserting on the source cannot see this
class of bug at all — the source is always valid.
