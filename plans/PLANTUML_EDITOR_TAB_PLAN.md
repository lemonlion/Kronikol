# PlantUML editor tab plan

**Date:** 2026-10-25 · **Base:** 5.8.0 (`29e551f3`) · **Status:** executed as 5.9.0 (2026-10-26)

## 1. The ask

The owner (2026-10-25), on the diagram menu's "Open PlantUML source in new tab":

> For plantuml, for very large diagrams there is usually nothing around that will render them at that size so the
> plantuml will error when they place it in a renderer. I was thinking that when we open as plantuml in new tab, we
> could open up in the new tab a whole plantuml rendering page. [...] have it point to the renderer of a patched
> plantuml version which has a maxsvg limit of 20 times the current patched one we have (or just set that [...]). We
> could have it styled to match Kronikol. [...] If there's errors with internet connection we could maybe fallback to
> showing something more raw.

Correction the owner made to the first analysis: the menu hands out a diagram's **whole** source, while the report
only ever draws it in parts, so "the report's own cap is enough" was wrong (section 2). Then: "ok go ahead".

## 2. What exists

- **The menu** (`context-menu-script.js`): "Open PlantUML source in new tab" opens `about:blank` in the click
  (`openTab`), and writes the source into it as a `<pre>` (`writeTab`, `openTextInTab`). The source is the
  container's `data-plantuml`: the whole, unsplit diagram, and for "current" with long notes uncapped
  (`window._noteUncappedSource`). When a note toggle changed the source there are two items, full and current.
- **The report never draws that source in one go.** The page splits it into parts of 12,000 px estimated height
  (`BrowserFragmentMaxHeight`; 45 px an arrow) and cuts notes over 15,000 characters across parts
  (`splitWithChunkedNotes`), and draws each part at `maxSvgSize: 98304`. Twenty parts are about 158,000 px drawn
  (section 4), past the report's cap, at which the engine refuses the whole diagram.
- **The cap is a render option**, not a patch: `{ maxSvgSize: 98304 }` at the three render sites (worker host, main
  thread fallback, Node renderer). The stock default is 8192, which is what the upstream playground runs at.
- **The engine shim** (`plantuml-browser-render-script.js`) fetches the pinned engine under its SRI hash, picks the
  Wasm GC build where it runs and `plantuml.js` elsewhere, rewrites the module's `export` tail, and builds one worker
  script: the worker host, Graphviz, the engine.
- **The upstream playground** (plantuml.github.io/plantuml/js-plantuml) is 31 KB of its own code (10 KB gzip), plus
  fflate for URL sharing. The repository is LGPL-3.0 and the page's files carry no header; Kronikol is MIT. The page
  is written here from scratch.

## 3. Design

**Menu.** A new item beside the source one: "Open in PlantUML editor", with the same full/current pair when a note
toggle changed the source. "Open PlantUML source in new tab" stays as it is: it needs no network and its tests pin it.
The item is offered only where the page carries the engine shim (`window.plantuml.openEditor`).

**The tab.** Opened in the click as today, then written by the shim (`window.plantuml.openEditor(tab, source)`) from
one resource, `plantuml-editor-tab.html` (markup, CSS and script), inlined into the report as a JSON string literal
the way the worker host is. Layout: a bar (title, status, Render, Copy, Save .puml, Save SVG, Save PNG, Whole/Parts),
the source in a textarea and the picture beside it; stacked below 768 px. Light and dark follow the report's theme.

**The engine in the tab.** The shim builds the worker script it already builds for its own workers (shared with
`acquireEngine`, so the hash check, Wasm/JS choice and export rewrite are one code path) and hands the text to the
tab. The tab makes its own Blob worker, so once started it no longer needs the report. The worker host takes the cap
from its `init` message (default 98304, unchanged for the report); the tab passes **1,966,080** (20 × 98304).

**Rendering a large source.**
- One render at a time in the worker. Edits re-render after a pause in typing for a source under 200,000 characters;
  above that only on Render (or Ctrl+Enter), and the status says the picture is out of date.
- A render in flight for an older source is abandoned by terminating its worker and starting another.
- The status shows elapsed seconds while a render runs; the worker host's 150 s limit ends one that never answers.
- **Parts** draws the source the way the report does (the report's own `_splitWithChunkedNotes`), one part under
  another: the fallback when the whole diagram fails or is too slow. It needs the report open; without it the button
  is disabled and says why.
- **Save PNG** draws at the largest scale a canvas holds (about 32,767 px a side), and says when that is below 1×.

**Fallback.** When the engine cannot be had (offline with a cold cache, a refused hash, no Worker), the tab still
opens with the source in the editor, copyable and savable, and the status names the reason.

**Edits are not lost silently.** A written `about:blank` tab is blank after a reload, so leaving an edited tab asks
first (`beforeunload`).

## 4. Measurements

2026-10-25, bundled Chromium 149 headless, the pinned engine (`v1.2026.10beta1-f44b054-teavm0.17.0-dev-2`) in a Blob
worker built as the page builds it, the worker host taking its cap from `init`. Sources from the consumer's live report
(BreakfastProvider, 408 distinct diagrams); parts counted by that page's own `_splitWithChunkedNotes`. The largest real
diagram is ten parts; the 21- and 41-part sources join the largest real ones. Median of three cold runs.

| parts | characters | drawn (px) | render, Wasm / JS | worker heap, Wasm / JS | insert | scroll frames (median / p95) | PNG at 1x |
|---|---|---|---|---|---|---|---|
| 5 | 70,829 | 2,923 × 31,663 | 0.41 s / 0.68 s | 34 / 45 MB | 20 ms | 16.7 / 16.8 ms | works |
| 10 | 147,247 | 2,863 × 65,948 | 0.69 s / 1.22 s | 59 / 70 MB | 45 ms | 16.7 / 16.7 ms | 0.99x at most |
| 21 | 345,557 | 2,920 × 157,736 | 1.44 s / 2.52 s | 109 / 89 MB | 110 ms | 16.7 / 16.8 ms | 0.41x at most |
| 41 | 664,738 | 3,205 × 335,709 | 3.57 s / 5.95 s | 181 / 231 MB | 232 ms | 33.3 / 50 ms | 0.19x at most |

What follows from it:
- **The whole diagram is cheap.** Nothing crashed, nothing came near the 150 s limit, and every picture ends with the
  source's last message. So the tab opens on the whole diagram, and Parts is the fallback, not the default.
- **The report's cap refuses rather than crops.** At 98,304 the 21- and 41-part sources come back as the engine's text
  `java.lang.RuntimeException: Diagram too large for browser rendering: 2919x157736 (max 98304; ...)`, after the whole
  layout (refusing takes as long as drawing). The tab shows that text and points to Parts. A part of the report's
  estimate draws about 7,500 px, not 12,000, so twenty parts are about 158,000 px; 20 x 98304 leaves room for
  about 250.
- **The 50,000-character threshold was far too low.** Ten parts redraw in 0.7 s (Wasm) and 1.2 s (JS). The tab redraws
  after a pause in typing up to 200,000 characters (about twelve parts) and waits for Render past that.
- **PNG.** This Chromium holds a canvas up to 65,535 px a side and 268,435,456 px in all; past that `toBlob` answers
  null with no error. The tab keeps the report's 32,767 px side, which Firefox needs.
- **An SVG straight from the page is not XML.** The engine's `<?plantuml-src ...?>` is held as a comment that can contain
  `--`, so the image does not decode. The tab writes the SVG as the report's menu does (the comment back as an
  instruction, controls replaced), which the first build of the tab did not: two E2E facts found it.

Harness: `C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/d0688552-b74c-4e4c-ad31-4e6a12da83e5/scratchpad/measure/`
(`run.js <src> <wasm|js> <cap>`, `run-all.js`, `results.md`); not kept in the repository.

## 5. Release

One minor release: a new menu item is new surface (CLAUDE.md). The report's output changes (a new resource and menu
item), so the Kronikol4J divergence ledger gets an entry. Wiki: the diagram menu's page.

## 6. Tests

Unit (C#): the editor page is inlined into a report that carries the shim and into no other; its literal never holds
`</script>`; the worker host reads `maxSvgSize` from `init` and keeps 98304 without it.

Playwright (`OpenInEditorTabTests`):
1. the item opens its tab in the click (before any async work) and the tab shows the source in the editor;
2. the tab draws the whole source as one picture, wider or taller than one part, where the report draws parts;
3. an edit redraws the picture; a source over 200,000 characters waits for Render, and one taller than 98,304 px is
   drawn whole;
4. full and current items open the matching source;
5. an engine that cannot be fetched leaves the source in the editor and names the reason;
6. Parts draws the report's parts; Save PNG/SVG/.puml give files, also for a source comment holding `--` and a text
   holding a control character; leaving an edited tab asks first;
7. the tab keeps drawing after the report tab is closed;
8. dark theme follows the report.

## 7. Open questions

- Q1: should the plain source item eventually go, with the editor as the only "source in new tab"? Kept for now.

## 8. Log

- 2026-10-26, **5.9.0**: built as designed, with two changes the measurements made (section 4): the tab redraws
  after a pause in typing up to 200,000 characters, not 50,000, and saves its SVG as the report's menu does (the
  first build saved an SVG that was not XML for a source comment holding `--`, which two E2E facts found). Facts:
  `OpenInEditorTabTests` (13), `PlantUmlWorkerHostTests` (3 new), `DiagramContextMenuTests` (3 new, 3 pins moved to
  `buildWorkerSource`). Unit facts red against 5.8.0's sources; seven mutations (the tab's cap, its unload prompt,
  its redraw threshold, the theme tokens, the parts, the no-worker path, the full/current items) each failed a fact.
  Full suites: core 8,810 passed, 2 skipped; Playwright 1,729 passed, 28 skipped; IKVM 55. The first two full
  Playwright runs each failed the long-source fact under the suite's load: Playwright's `FillAsync` enters a
  multi-line text line by line (16,000 input events, 13 s alone for 307,000 characters, past its 30 s limit under
  load), where the menu sets the tab's value in 10 ms and a real paste lands in 54 ms as one input event. The fact now
  opens its source from the menu and types its edit; the tab was not at fault.
