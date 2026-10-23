# A compressed TestRunReport.html: the page as zstd frames inside a small loader (29 MB to 7 MB)

**Date:** 2026-10-23 · **Repo version:** 5.5.1 (`origin/main` at `91d1afd7`; line numbers are for that commit) ·
**Status: draft, not green-lit.**

This plan was written at the owner's asks of 2026-10-23:
- "Can we investigate options for getting the 29mb html report such as the one in
  "C:\Users\cex\Downloads\TestRunReportWithSlowToggle.zip" down to about 7mb, like we have for the equivalent .krkl
  file (which is 6.7mb). Look into using ztsd (I know it's natively only supported in firefox, but we could have a
  fallback to a non-native engine, and always have a check for a native version keeps it forward compatible). I don't
  mind a loading screen of up to 2 seconds (although the shorter the better)."
- On the level, after the first measurements: "Sounds like we should go with -9 for the moment".
- Then: "Write up the plan using the recommended design, including a follow up step to investigate the "26-38 s to fire
  load" thing".
- And: "another LLM said something a while back about one of the javascript ztsd decoders having some 32mb limit? I'm
  not sure if that's applicable with anything you planned, but make sure it's not something that we missed"
  (section 3.6).

**Decided by the owner on 2026-10-23:**
1. zstd at level 9 for now. Level 19 took 20 s on this report's diagram sources alone (section 3.2).
2. The design is the one recommended that day, called C2 below: two zstd frames in the page, the page first and the
   diagram sources second, with the diagram sources decoded in a Web Worker after the page is on screen.
3. A follow-up step investigates why WebKit fires `load` 26 to 38 s after the page is up (step F1, section 6).
4. No option: every `TestRunReport.html` is written compressed, and it is not a major. The owner: "I don't think this
   CompressHtmlReport should be an option at all. There is no reason to have it off, and I don't consider it a major
   change as it doesn't change any functionality from the user's point of view." (Section 4.7.)

Everything else here is a recommendation, with the questions in section 7.

Evidence labels:
- **RUN:** measured on 2026-10-23 on Windows 11 over the 5.4.4 report the owner shared (28,954,978 bytes), with Python
  3.14.4 (libzstd 1.5.7), Node 25.9.0, Playwright 1.59.0's own Chromium 147, Firefox 148 and WebKit 26.4 builds, and
  .NET 10 with ZstdSharp 0.8.8. Scripts and output are in [`HTML_COMPRESSION_PLAN.harness/`](HTML_COMPRESSION_PLAN.harness/README.md).
- **READ:** in the source at `91d1afd7`, `file:line`.
- **INFERRED:** reasoned, not measured.
- **WEB:** a published source, linked, read on 2026-10-23.

## 0. Summary

Today's page is 29 MB because every embedded item is gzipped on its own and then written as base64. That includes the
1,553 diagram sources, 102.8 MB of PlantUML that per-item gzip takes to 10.0 MB and base64 to 14.4 MB. No item can
reuse text from another. In one zstd stream the same sources take 3.48 MB, and the whole page's content 5.14 MB, at
level 9 (section 3.2). Wrapping today's page as it is gains far less (12.2 MB of zstd, 16.3 MB in a page) because its
items are already compressed (section 3.1).

The design (section 4) writes the report page with every item raw, compresses it as two zstd frames (the page, then
the diagram sources) at level 9 with an 8 MB window, and puts them in a small page as base64, beside a loader and an
8.4 KB pure-JavaScript zstd decoder (fzstd). The loader tries the browser's own `DecompressionStream('zstd')` first and
falls back to fzstd, rebuilds the report with `document.open`, `write` and `close`, and decodes the diagram frame in
a Web Worker. For this report the file is **6,977,369 bytes** (RUN).

| Measured (RUN, three runs each; run 1 is a new browser) | Today's page | C2 |
|---|---|---|
| File | 28,954,978 bytes | 6,977,369 bytes |
| Loading screen painted | none (the page renders as it parses) | 32 to 137 ms |
| Report painted, Chromium 147 | 816 to 818 ms (2,586 ms on a cold first run) | 685 to 709 ms (688 cold) |
| Report painted, Firefox 148 | 977 to 1,195 ms | 1,344 to 1,426 ms (fzstd); 1,335 to 1,372 ms (native, pref on) |
| Report painted, WebKit 26.4 (Windows) | 2,483 to 2,857 ms | 1,573 to 1,592 ms |
| Chromium JS heap + `ArrayBuffer` storage | 42 + 8 MB | 34 + 112 MB |

So the report is on screen inside the 2 s the owner allowed in every engine, sooner than today in Chromium and WebKit
and about 0.3 s later in Firefox. The cost is memory: the decoded diagram sources, about 100 MB here, stay in the tab.
Step S0 measures the whole tab and decides between one diagram frame and 16 MB pieces (+4% size) before R1.

No browser decodes zstd natively by default today (section 3.5): the Compression Streams standard has no zstd, Firefox
138 and later have it behind a preference that is off, Chromium closed the request, and Safari has nothing. The native
check stays (it costs one constructor call, and with Firefox's preference on it decoded these frames), but every reader
runs the fallback today.

fzstd's README warns that it handles back-references only up to 2^25 bytes (32 MiB). The frames' 2^23 window keeps
every back-reference at 8 MB or less, and the warning did not reproduce in fzstd 0.1.1 even with a forced 40 MiB
back-reference (section 3.6).

Steps (section 6): **S0** measures memory and picks the diagram layout; **R1** (a minor) writes every
`TestRunReport.html` compressed, with no option (the owner's decision 4), and moves the tests that read the page onto
a decoding helper; **F1** investigates WebKit's late `load` (a patch if the cause is the page's).

Corrected on the way: the page carries five copies of the gzip helper (`report-decompress-helper.js`). That is
deliberate (`DiagramContextMenu.cs:106-116`): each public script accessor prepends it so that a page composed without
the main report still works. It is not a defect, and inside a zstd stream the copies cost nothing.

## 1. How far each claim was checked

| Claim | Evidence |
|---|---|
| What fills the page, by element, attribute and content class | RUN (`anatomy.py`, `json_blocks.py`, `split.py`), sizes only, never content |
| Codec sizes and times | RUN: libzstd 1.5.7 through Python, gzip 1.14, brotli 1.2.0, xz 5.8.2 (`cbench.py`, `level9.py`) |
| The writer's cost in .NET | RUN: ZstdSharp 0.8.8 (`zsharp.cs`), the release the product's source copy is |
| Load times, memory and decoder paths in three engines | RUN (`bench.js`), Playwright's bundled builds, headless, three runs each |
| Deep links, Back, a diagram drawn, page errors | RUN (`functional.js`), Chromium and Firefox only |
| Deep search over diagram, flame and internal-flow text in a compressed page | NOT checked: the prototype's search worker path is broken (section 3.11) |
| Native zstd and brotli support per browser | WEB (section 3.5); Firefox's native zstd also RUN with the preference on |
| fzstd's 2^25 back-reference limit | WEB and READ (its README, and its code); RUN: it did not reproduce in 0.1.1 with a forced 40 MiB back-reference, one-shot or streaming (`fzcheck.js`, `fzstream.js`) |
| Who writes and reads each embedded item | READ (Appendix A) |
| Other reports (other suites, other sizes) | Not measured: one report, written by 5.4.4 |

## 2. What fills the page today

### 2.1 The parts (RUN)

| Content | Items | As stored in the page | gzip inside | Raw |
|---|---|---|---|---|
| Diagram sources (`#puml-data`) | 1,553 | 14,413,414 | 10,004,703 | 102,832,638 |
| Markup, scripts, CSS and plain attributes | | 12,284,541 | | 12,284,541 |
| Deep-search index (`#kron-search-index`, binary) | 1 | 1,035,322 | 776,489 | 1,346,203 |
| Internal-flow segments (`#iflow-segments`) | 1 | 482,868 | 335,921 | 4,146,822 |
| Flame charts (`data-flame-z`) | 771 | 448,500 | 335,591 | 963,831 |
| Held sub-steps (`#step-data`) | 359 | 272,583 | 188,390 | 3,097,565 |
| **Page** | | **28,954,978** | | **136 MB written back raw** |

Of the markup, 709,300 bytes are 15 inline scripts and 153,208 the stylesheet; the largest attributes are
`data-search` (2,328,656 bytes over 512 elements), `class` (1,877,270), `data-row-search` (1,247,916) and `title`
(1,141,694). `System.Text.Json` writes `+` as `\u002B`, so the base64 in JSON blocks is larger than plain base64.
The page holds no PlantUML engine: it comes from jsDelivr at run time (`TrackingDefaults.cs:34`).

### 2.2 How each item is written and read (READ)

Every item goes through `InternalFlowHtmlGenerator.CompressToBase64` (`InternalFlowHtmlGenerator.cs:573`), gzip then
base64, except the search index, which has its own (`SearchIndexBuilder.cs:248`). The page decodes them with one helper,
`decompressGzipBase64` (`report-decompress-helper.js:5-11`: `atob`, then `DecompressionStream('gzip')`), except the
search worker, which has its own `gunzipToText` and `gunzipToBytes` (`report-search-index.js:301-314`). Appendix A
lists every site. The page is assembled as one string in `GenerateHtmlReportCore` (`ReportGenerator.cs:1396`) and
written once (`ReportGenerator.cs:2720`), which is the one place a compressed page hooks in.

## 3. Findings

### 3.1 Wrapping today's page is not enough (RUN)

The page as it is, in one stream: zstd -9 12,257,677 bytes, zstd -19 12,157,731, brotli -q 11 12,024,206, gzip -9
12,772,165. Its items are already compressed, so a second pass over them gains nothing. In base64 inside a page
(prototype A) that is 16,347,583 bytes.

### 3.2 Raw content in one stream (RUN)

With every item written back raw, the classes at level 9 with a 2^23 window: markup 561,100 bytes, diagram sources
3,483,511, search index 719,857, internal-flow segments 279,762, flame charts 108,000, sub-steps 29,300. Together
5,181,530; in one frame 5,142,334. The diagrams gain the most: 10.0 MB of per-item gzip becomes 3.48 MB, because the
1,553 sources repeat each other.

gzip cannot do this even on raw content (11,404,891 bytes for the raw page): its 32 KB window cannot see from one
diagram to the next. Level 19 would take the classes to 4,316,965 bytes but took 20.5 s on the diagram sources alone,
against 0.33 s at level 9 (owner's decision 1). Brotli -q 11 on the raw page gave 4,058,511 bytes in 75 s; xz -9e
3,939,956 in 43 s.

### 3.3 The window: 2^23 (RUN, WEB)

RFC 9659 says that for zstd over HTTP, "decoders MUST support a Window_Size of up to and including 8 MB" and
"encoders MUST NOT generate frames requiring a Window_Size larger than 8 MB"
([RFC 9659](https://www.rfc-editor.org/rfc/rfc9659.html), September 2024). A native `DecompressionStream('zstd')` is
likely to keep the same cap (INFERRED), so the frames use a window of 2^23 (8 MB). A 2^27 window with long-distance
matching saved 2.4% (5,059,626 against 5,181,530 bytes) and is not worth a frame a native decoder may refuse. The
same cap also keeps the frames inside fzstd's limit (section 3.6).

### 3.4 How to store the diagram sources (RUN)

| Layout | Diagram bytes | Note |
|---|---|---|
| One frame | 3,483,511 | To read any diagram, the page decodes all 102.8 MB |
| Frames of 16 MB of source | 3,622,002 (+4%) | 7 frames; a diagram needs its own frame only |
| Frames of 8 MB | 3,781,262 (+9%) | 12 frames |
| Frames of 4 MB | 4,044,278 (+16%) | 24 frames |
| Frames of 1 MB | 4,751,961 (+36%) | 77 frames |
| One frame per diagram with a trained 110 KB dictionary | 6,316,355 (+81%) | Random access per diagram, at too high a cost |

A frame can only reuse text from inside itself, which is the cost of cutting. The page frame does not need cutting:
it is decoded once, whole, to build the page.

### 3.5 Native decoders (WEB, RUN)

- The [Compression Streams standard](https://compression.spec.whatwg.org) (Living Standard, updated 19 October 2026)
  defines `CompressionFormat` as `"brotli"`, `"deflate"`, `"deflate-raw"` and `"gzip"`. There is no zstd.
- `DecompressionStream('zstd')`: no support in Chrome, Edge or Safari; Firefox 138 and later have it, disabled by
  default ([caniuse](https://caniuse.com/mdn-api_decompressionstream_decompressionstream_zstd)), behind
  `dom.compression_streams.zstd.enabled`, which is `false`
  ([StaticPrefList.yaml](https://searchfox.org/firefox-main/source/modules/libpref/init/StaticPrefList.yaml)).
  Chromium's request ([issue 348499728](https://issues.chromium.org/issues/348499728)) was closed for want of a
  specification (as search results quote it; the issue page needs a sign-in).
- RUN: Playwright's Firefox 148 with the preference on decoded these frames natively. Native was about three times
  faster than fzstd on the 29 MB of prototype A (72 to 78 ms against 223 to 247 ms) and no faster on the 136 MB of
  prototype B (278 to 284 ms against 290 to 363 ms).
- `DecompressionStream('brotli')` is in the standard and ships in Firefox 147 and later
  ([bug 1921583](https://bugzilla.mozilla.org/show_bug.cgi?id=1921583)) and Safari 18.4 and later, iOS included, but
  not in Chrome or Edge ([caniuse](https://caniuse.com/mdn-api_decompressionstream_decompressionstream_brotli)).
  Section 7, Q5.

### 3.6 Fallback decoders, and fzstd's 32 MB limit (RUN, WEB, READ)

| Decoder | Size the page carries | Notes |
|---|---|---|
| fzstd 0.1.1, pure JavaScript, MIT | 8,394 bytes | Decoded 5.25 MB to 136 MB in 267 to 272 ms in Chromium; the 23.4 MB page frame in 67 to 76 ms |
| zstddec 0.3.4, Wasm in a module | 75,676 bytes | Needs the decoded size up front (its README); not timed |
| brotli-dec-wasm 2.3.2 | 208,439 bytes of Wasm | Carries brotli's dictionary; not timed |

**The 32 MB limit is fzstd's.** Its README: "unlike the reference implementation, `fzstd` only supports a maximum
backreference distance of 2^25 bytes. If you need to decompress files with an "ultra" compression level (20 or
greater) AND if your files can be above 32MB decompressed, `fzstd` *might* fail to decompress properly"
([fzstd](https://github.com/101arrowz/fzstd)).

- **It does not reach these frames.** A zstd back-reference cannot reach further back than the frame's window, and
  these frames use 2^23 (section 3.3), so no back-reference exceeds 8 MB, a quarter of the limit. fzstd decoded the
  102.8 MB diagram frame and the 136 MB frame of prototype B in all three engines (RUN).
- **It did not reproduce at all in 0.1.1** (RUN, `fzcheck.js` and `fzstream.js`, `results/fzstd-limit.txt`). Input:
  40 MiB of random bytes, then a copy of its first 4 MiB, so that the tail can only be a match 40 MiB back. Written
  with a 2^27 window and long-distance matching, and at level 22, both frames used that match (each about 4 MB
  smaller than the input). fzstd decoded both byte for byte, through its one-shot `decompress` and through its
  streaming `Decompress` fed in 64 KiB pieces. So did the diagram sources written the same two ways.
- **The plan keeps the 2^23 window regardless.** RFC 9659's cap needs it for a native decoder; the README's warning
  is the author's own and may cover cases not tried here; and a larger window saves only 2.4% (section 3.3). R1 pins
  the window with a test (section 5, test 3).

Two more properties of fzstd, both handled: its own check rejects only windows over 2,145,386,496 bytes (READ, its
`err(1)`), and it does not verify the frame checksum (READ: no XXH64 in its code). The loader therefore decodes text
with `TextDecoder('utf-8', { fatal: true })` and compares each frame's decoded length with the length the page
records; the native path verifies the checksum itself.

### 3.7 Putting the bytes in the page (WEB, INFERRED)

- **base64:** +33%, ASCII only, safe however the page is served. Chosen.
- **A `windows-1252` page carrying near-raw bytes** (+1.6%, with NUL, CR and `<` escaped): rejected. An HTTP
  `Content-Type` charset takes precedence over the page's `<meta charset>`, so a host that serves the report as UTF-8
  (GitHub Pages does) would corrupt the payload.
- **A UTF-8-safe denser encoding** (in the style of base-122, about +14%): about 6.0 MB for this report, at the cost
  of more decoder code. Section 7, Q4.

### 3.8 Rebuilding the page (RUN)

`document.open()`, `document.write(page)`, `document.close()` replace the document in the same window. Checked in
Chromium and Firefox against today's page (`results/functional.txt`): the address and its `#` fragment are kept, the
rebuilt document fires `DOMContentLoaded` and `load` (all three engines), Back after an in-page link stays in the same
document with no second decode, a diagram is drawn through the page's own pipeline with the engine from jsDelivr,
and no page errors occur. The report's scripts start on `DOMContentLoaded` (15 listeners, none on `load`, READ from
the page). A deep link to an element 60% down the page did not scroll into view in either page; whether the harness
picked an element the page scrolls to was not checked, and R1's deep-link fact compares with the plain page anyway.

### 3.9 Load times and memory (RUN)

C2 against today's page is the table in section 0 (`results/bench-c2.tsv`). The other prototypes, Chromium 147, three
runs each (`results/bench-variants.tsv`; C2's row from `results/bench-c2.tsv`). Here "painted" is the first paint
after the rebuilt page's `load`, the one measure both files share:

| Prototype | File | Painted after `load` | JS heap + `ArrayBuffer` |
|---|---|---|---|
| A: today's page in one frame | 16,347,583 | 1,056 to 1,076 ms | 37 + 25 MB |
| B: raw items in one frame | 7,012,279 | 1,967 to 2,089 ms | 207 + 17 MB |
| C: two frames, diagrams decoded on the main thread | 6,976,609 | 1,377 to 1,650 ms | 24 + 115 MB |
| C2: two frames, diagrams decoded in a worker | 6,977,369 | 697 to 722 ms | 34 + 112 MB |

B holds the whole decoded page as strings; C's diagram decode on the main thread held up the first paint, which C2's
worker removes. In WebKit every page, today's included, fires `load` 26 to 38 s after it is up (step F1).

### 3.10 The writer's cost (RUN)

ZstdSharp 0.8.8 at level 9 with a 2^23 window writes the same bytes as libzstd: 12.3 MB of markup in 100 to 160 ms,
the 102.8 MB of diagram sources in 348 to 368 ms, the whole 136 MB raw page in 471 to 490 ms. The compressed page also
skips today's 1,553 separate gzip calls, which were not timed.

### 3.11 What the prototype did not cover

- **Deep search.** The search worker verifies a match by decoding diagram, flame and internal-flow text with its own
  `gunzipToText` (`report-search-index.js:335-342`), from the `#puml-data` text the page posts to it
  (`report-search-index.js:574`). The prototype pointed that function at `window`, which a worker does not have, so
  deep search over those texts fails in it. Section 4.4 designs it.
- **Export** (`report-export-function.js`), the merge path (`kronikol merge`), the component diagram report, a
  corrupt payload, a page with JavaScript off, the dark theme on the loading screen, and printing.
- **Whole-tab memory:** only Chromium's JS heap and `ArrayBuffer` storage were read (step S0).
- **Other reports:** one report, written by 5.4.4. A report with few diagrams gains less; one with more gains more.

## 4. The design (C2)

### 4.1 The file

```html
<!doctype html>
<html lang="en"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="generator" content="Kronikol 5.x.y">
<meta name="kronikol-page" content="zstd-frames 1">
<title>{the report's own title}</title>
<script>{the report's theme boot, as today}</script>
<style>{the loading screen: light and dark tokens, following the theme boot's choice}</style>
</head><body>
<div id="kron-loading" role="status" aria-live="polite">Opening the report...</div>
<noscript>This report is compressed and needs JavaScript to open.</noscript>
<script id="kron-frame-page" type="application/octet-stream" data-length="{decoded bytes}">{base64}</script>
<script id="kron-frame-diagrams-1" type="application/octet-stream" data-length="{decoded bytes}">{base64}</script>
<script id="kron-zstd">{fzstd, vendored}</script>
<script>{the loader}</script>
</body></html>
```

- Each frame: zstd level 9, window log 23 set explicitly (never left to the level's default), the content size in the
  frame header, the checksum on. One `ZstdSharp` `Compressor` call each (`src/Kronikol/Compression/Zstd`).
- The diagram frames are numbered from the start, so S0's choice of one frame or 16 MB pieces is the writer's alone.
- `kronikol-page` marks the file for tools and tests (section 4.6 and Q9).
- The title and the theme boot make the tab read right and the loading screen match the reader's theme from the
  first paint.

### 4.2 The loader

1. Read each frame's bytes: `Uint8Array.fromBase64` where the browser has it, else `atob` and a loop.
2. Paint first: `requestAnimationFrame`, then `setTimeout(0)`, before any decoding.
3. Decode the page frame: `new DecompressionStream('zstd')` inside a `try`; if the constructor throws, or the native
   decode fails, use fzstd. Decode the text with `TextDecoder('utf-8', { fatal: true })` and check the length.
4. Set `window.__kronikolFrames` (the diagram index, the diagram frames' bytes and a promise for their decoded form),
   which survives the rebuild because the window does.
5. `document.open(); document.write(page); document.close();`, writing a page larger than 16 MB in slices, so no
   single string nears V8's limit of about 2^29 characters (INFERRED; this report's page frame is 23 MB).
6. Decode the diagram frames in a Web Worker made from the vendored fzstd text and a small handler (native first there
   too), transferring the bytes in and the result out. If a worker cannot be made (a Content Security Policy that
   refuses `blob:` workers), decode on the main thread after the first paint.
7. On any failure, replace the loading text with what failed and how to get a plain copy (`kronikol decode`, Q9).

### 4.3 Inside the page: one accessor for every item

`report-decompress-helper.js` learns the compressed page. With `window.__kronikolFrames` set, a value that is a diagram
token resolves from the decoded diagram frames (`TextDecoder` over the diagram's slice); any other value is already
raw text and resolves as it is. Without it, today's gzip and base64 path. All five copies on a page are the same file,
so they all behave the same, and a plain page is unchanged.

- `#puml-data` maps each id to a token instead of a gzip string: a string starting with U+0001 and the diagram's
  number, as the prototype did. A string keeps `getPumlZ`'s `||` fallback (`plantuml-browser-render-script.js:709-715`)
  working, where a number 0 would not.
- The diagram index, `[frame, start, end]` per number, is a JSON block in the page frame.
- `#step-data`, `#iflow-segments`' `z`, `data-flame-z`, and `data-plantuml-z` on the merge path are written raw
  (JSON- or attribute-escaped), so the accessor returns them as they are.
- The search index stays gzip and base64 in R1 (Q6): its reader is the worker's own `gunzipToBytes`, and inside the
  page frame its 1,035,322 bytes as stored compress to 777,168, where the raw binary index would take 719,857 (RUN,
  `search_index.py`).

### 4.4 The search worker

The page posts `#puml-data`'s text to the search worker, and the worker decodes sources itself to verify a match
(`report-search-index.js:335-342`, `:574`). In a compressed page:
- its `gunzipToText` passes raw text through (flame charts and `data-plantuml-z`), told the page's mode in the message
  that starts it;
- for diagram tokens, the page posts the diagram frames' compressed bytes (3.5 MB here, a copy) and the index with
  that message, and the worker decodes them itself on its first deep verify, with fzstd or natively.

That holds a second decoded copy of the diagram text while search is in use (about 100 MB here with one frame, one
piece at a time with pieces). The alternative is one copy in the decode worker, answering both the page and the
search worker through a `MessageChannel`, at the cost of more plumbing. Recommendation: the first, and S0 measures it.

### 4.5 Export

`report-export-function.js` copies the `#puml-data` and `#step-data` entries an export keeps
(`report-export-function.js:41-73`) and the flame attributes (`:86-101`) as the DOM holds them. From a compressed page
those are tokens and raw text, so the export resolves each kept entry through the accessor and writes it gzipped and in
base64 (`CompressionStream('gzip')`, which every engine has). The exported file is a plain page (Q7).

### 4.6 The writer

- **Where:** `GenerateHtmlReportCore` (`ReportGenerator.cs:1396`) keeps building the page. Each item site
  (Appendix A.1) writes its text raw through one function, which also collects the diagram sources for the diagram
  frames. At `ReportGenerator.cs:2720`, `WriteFile` receives the loader page in place of the report page.
- **Merge and ingest:** `kronikol merge` renders a merged page, where the internal-flow activity diagrams sit inline
  as `data-plantuml-z` rather than in `#puml-data` (`ReportGenerator.cs:2756-2757`). Merge and ingest write the
  compressed page too (Q8).
- **Other pages** (the component diagram report, the Labs and diagnostic pages): unchanged in R1 (Q8).
- **Tests and tools that read the HTML:** a test helper decodes a compressed page (frames, then the page with tokens
  resolved), and `kronikol decode` does the same for people (Q9); section 4.7 has what moves onto them.

### 4.7 No option (the owner's decision 4)

Every run, `merge` and `ingest` writes the compressed page; there is no setting to turn it off and no public type
changes. What follows from that:

- **The version:** not a major (the owner's ruling). R1 is a minor because `kronikol decode` learns the compressed
  page (Q9), which is new for a consumer to call; the page change on its own would be performance work, a patch.
- **Tests that read the generated HTML:** 251 test files do, 17 of them the item blocks by name. They move onto one
  test helper that returns the decoded page (the frames decoded and the tokens resolved), so that they read what ships
  (Q13). Golden pins of today's HTML become pins of the decoded page.
- **Browser tests:** the loader page's own `load` fires before the report is rebuilt, and the suites open reports with
  `GotoAsync`'s default wait for `load` (759 calls in 178 files, none with another `WaitUntil`, READ), through many
  per-class helpers rather than one. A locator action waits for its element and so waits for the rebuild by itself;
  an `EvaluateAsync` straight after `GotoAsync` would run against the loader page. So the loader sets a readiness flag
  once the rebuilt page has fired `DOMContentLoaded`, and the suites' opening helpers wait for it.
- **Kronikol4J** keeps writing the plain page until it is ported: a divergence ledger line, as `CLAUDE.md` asks for a
  change to report output (Q11).
- **People who search the HTML file** find base64: the changelog and the wiki say to use `kronikol decode`.

### 4.8 fzstd as a vendored copy

fzstd 0.1.1 (`umd/index.js`, 8,394 bytes, MIT, Arjun Barrett) as an embedded resource, pinned by version and hash,
with a `THIRD-PARTY-NOTICES.md` entry beside ZstdSharp's, and an update rule like ZstdSharp's (`tools/vendor-zstd`,
`.github/workflows/vendor-zstd.yml`): from tagged releases, read before taking.

### 4.9 Kronikol4J

Kronikol4J keeps writing the plain page. Its divergence ledger (`docs/REMAINING_PARITY.md`) records that the
compressed page is not ported (Q11).

### 4.10 Not in this plan

The `.krkl` (unchanged); the engine; the plain page's own per-item gzip (stays as it is); a sidecar file next to the
page (the report stays one file); levels above 9 (Q3).

## 5. Tests, red first

Writer (core suite):
1. Every writer path (a run, `kronikol merge`, `kronikol ingest`) writes the loader page, never the plain one.
2. The file is the loader page of section 4.1: the report's title, the loading screen, the marker, one frame element
   per frame with its decoded length, the decoder, the loader, and no report markup outside the frames.
3. Each frame's header: for content over 8 MB the window is exactly 2^23 (level 9's own default is 2^22, so a writer
   that forgot to set it fails; one that set 2^27 fails too), and for smaller content the frame is single-segment,
   its window the content size; the content size present; the checksum flag set. This is the guard for RFC 9659's
   8 MB cap and for fzstd's documented 2^25 back-reference limit.
4. Round trip: decoding the page frame and resolving its tokens gives every item the plain page holds, equal after
   decoding: `#puml-data` through the index, `#step-data`, `#iflow-segments`, `data-flame-z`, `data-plantuml-z` on the
   merge path, the component diagram; the search index unchanged.
5. The vendored fzstd, run in Node, decodes every frame of a fixture whose content is over 32 MB byte for byte through
   `decompress`, the call the loader makes. There is no failing control for the 2^25 limit, since it did not
   reproduce (section 3.6); test 3 is the guard.
6. The diagram index: every token resolves to its own source, the first and the last included, and across a frame
   boundary when there are pieces.
7. A merged page (`kronikol merge`) and an ingested one round-trip the same way.
7a. `kronikol decode TestRunReport.html` writes a plain page whose items equal the compressed page's after decoding,
    and which opens and works in a browser (test 13's checks).
7b. The test helper of section 4.7 returns, for a fixture, the same decoded page that `kronikol decode` writes.

Page (Playwright, the bundled Chromium; Firefox and WebKit where the suite runs them):
8. The loading screen paints, then the report replaces it, with no page errors.
9. A deep link to a scenario lands where it lands in the plain page.
10. Back and Forward after in-page navigation stay in the document, with no second decode.
11. A diagram, a flame chart, an internal-flow popup, held sub-steps on expand and the component diagram all work.
12. Deep search finds text that is only in a diagram source, only in a flame span, and only in internal-flow text.
13. Export from a compressed page writes a plain page that opens and works (diagrams drawn, sub-steps open).
14. The dark theme gives a dark loading screen, and the theme switch works after the rebuild.
15. A corrupted frame shows the failure in the loading screen, never a blank page.
16. With workers refused, the diagrams still draw (main-thread decode).
17. Firefox with `dom.compression_streams.zstd.enabled`: the native path decodes (if the suite runs Firefox; else a
    unit test of the loader's branch).
18. The suites' opening helpers wait for the rebuilt report: with the loader slowed down, an `EvaluateAsync` straight
    after the helper still sees the report, never the loader page.

Each new fact fails on the unchanged code first, and each change is shown to matter by mutation, as the repo's plans
do.

## 6. Steps and releases

| Step | What | Version |
|---|---|---|
| **S0** | Measure before building: the whole tab's memory (the browser's child processes' private bytes) for today's page, C2 with one diagram frame, and C2 with 16 MB and 8 MB pieces, in Chromium and Firefox, after load, after drawing the diagrams in view, and during a deep search; and the time to the first diagram in a piece. Answers Q2. Harness only | none |
| **R1** | Every `TestRunReport.html` compressed, with no option (section 4), `kronikol decode` for the page, the tests that read the HTML moved onto the decoding helper and the browser suites' helpers onto the readiness flag, section 5's tests, the docs and the records below | a minor (section 4.7) |
| **F1** | WebKit's late `load` (below) | none, or a patch if the cause is the page's |
| later | Each its own go: level 19 (Q3), a denser encoding (Q4), brotli (Q5) | |

S0 and F1 need no product change and can run in either order, before or alongside R1. Version numbers are claimed at
push, as usual.

### F1: why WebKit fires `load` 26 to 38 s late

**Seen (RUN):** in Playwright's WebKit 26.4 (the Windows build), today's page fires `DOMContentLoaded` at 727 to 875
ms and is painted at 2.5 to 2.9 s, but fires `load` at 26.5 to 38.0 s, in all six runs. The compressed prototypes
do the same (25.6 to 36.0 s), so compression neither causes nor cures it. Chromium and Firefox fire `load` 0.2 to
0.3 s after `DOMContentLoaded`.

**Why it matters:** the report starts on `DOMContentLoaded`, so it works. But a tab that counts as loading for half a
minute shows a progress indicator that long, and anything that waits for `load` waits too: Playwright's `goto` by
default, printing tools, people's own scripts. If real Safari behaves the same, every Safari reader sees it.

**The first lead (READ):** when the page draws on its main thread, `useMainThreadEngine`
(`plantuml-browser-render-script.js:439`) calls `loadEngineScripts()` (`:463`, defined at `:486`), which adds
`<script src>` elements for the engine from jsDelivr through `addScript` (`:465-475`). A script element added before
`load` holds `load` until it loads or fails. That route is taken, for one, when a worker cannot be made (`:248`).
About 30 s is a typical network time-out.

**Method:**
1. Read the render script's own telemetry (`engine`, `engineProblem`) in WebKit to see which route it took.
2. Record `performance.getEntriesByType('resource')` and Playwright's request, requestfinished and requestfailed
   events, with times, from navigation to `load`.
3. Run with the browser context offline (`setOffline`, which cuts the network and mocks nothing) and see whether
   `load` then comes at once.
4. Cut a copy of the page down (scripts and elements out, by halves) until `load` comes on time.
5. Try a small report, to see whether size matters.
6. Try real Safari on macOS (a GitHub macOS runner) and WebKit on Linux (the podman runner), to learn whether readers
   see it or only the Windows build does.

**Outcome:** if the cause is the page's, a patch with a WebKit fact that `load` comes within a few seconds of
`DOMContentLoaded` (which needs the E2E suite to run WebKit, a question for then). If it is the Windows build alone,
a line in this plan's log, and stop.

### Records (R1)

- `CHANGELOG.md`: the compressed page, the minor bump and why (section 4.7), and `kronikol decode` for anyone who
  searched the HTML file.
- The wiki: `Generated-Reports.md` (the page, the loading screen, `kronikol decode`). The pages that name
  `CompressTestRunReport` (`Report-Configuration.md`, `CI-Artifact-Upload.md`,
  `Capture-Time-Redaction.md`, `Ingesting-External-Captures.md`, `Migrating-to-v5.md`) to be read for anything a
  compressed page changes: a text search of the HTML file, for one, finds nothing in a compressed page.
- `THIRD-PARTY-NOTICES.md`: fzstd.
- Kronikol4J's ledger line; this plan's log and `PLANS_STATUS.md` row.

## 7. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | An option to turn the compressed page off? | **Answered by the owner on 2026-10-23: no option, and not a major** (decision 4, section 4.7) |
| Q2 | One diagram frame (6.98 MB, ~100 MB held while open) or 16 MB pieces (~7.16 MB, 16 to 32 MB held)? | Decide from S0; the format allows either |
| Q3 | A higher level later? Level 19 gives 4.32 MB of content in 20 s (libzstd) | Stay at 9 (the owner's decision 1). If ever, keep the window at 2^23: the ultra levels' larger windows exceed RFC 9659's cap, and fzstd's README warns about them (though 0.1.1 decoded them here) |
| Q4 | A denser UTF-8-safe encoding than base64 (~6.0 MB here)? | Later, and only if size still matters after R1 |
| Q5 | Brotli instead, for its native decoding in Firefox 147+ and Safari 18.4+? | Stay with zstd: the codec of the `.krkl`, its encoder already in the product, an 8 KB fallback against 208 KB, and Chrome (most readers) has neither natively. Brotli at its faster qualities was not measured |
| Q6 | The search index raw in a frame of its own (719,857 bytes) instead of gzip and base64 inside the page frame (777,168)? | Not in R1: 57 KB is not worth a binary frame and a new reader |
| Q7 | Export from a compressed page: a plain page? | Yes |
| Q8 | Merge and ingest write the compressed page; the other pages (component diagram report, Labs, diagnostics) stay plain? | Yes, in R1: the other pages are small |
| Q9 | `kronikol decode` writes a working plain copy of a compressed page? | Yes, in R1: with every page compressed it is the way to a searchable file, and the tests' helper uses the same decoding |
| Q10 | A reader with JavaScript off sees only a notice (today's page shows its markup)? | Accept: the report is a script application already (diagrams, search, tabs) |
| Q11 | Kronikol4J? | A ledger line in R1; port when the owner wants Kronikol4J to match |
| Q12 | A `ROADMAP.md` placement? | The owner's call; not placed |
| Q13 | Tests that read the HTML: through the decoding helper, or an internal switch that writes them the plain page? | The helper: tests then read what ships. A switch would keep the 251 files as they are but test a page no reader gets |

## 8. Log

- 2026-10-23 (kronikol-08): investigation at the owner's ask. Anatomy and codec measurements over the 5.4.4 report;
  native support read from the standard, caniuse, Firefox's preferences and Chromium's tracker; prototypes A, B, C
  and C2 built and timed in three engines; functional checks in two. The owner chose level 9. Plan written at the
  owner's ask, with step F1 for WebKit's late `load`. Corrected on the way: the five copies of the gzip helper are
  deliberate (`DiagramContextMenu.cs:106-116`), not a defect as first reported. Checked at the owner's prompt: the
  "32 MB limit" is fzstd's documented 2^25 back-reference distance, out of reach of 2^23-window frames; it did not
  reproduce in fzstd 0.1.1 with a forced 40 MiB back-reference, one-shot or streaming (section 3.6). A first draft's
  test 5 had a "failing control" for it, dropped as untrue.
- 2026-10-23: the owner ruled out the option ("I don't think this CompressHtmlReport should be an option at all ...")
  and ruled it not a major (decision 4). Sections 0, 4.6, 4.7, 5, 6 and 7 rewritten for it: R1 is a minor through
  `kronikol decode`, the tests that read the HTML move onto a decoding helper (Q13), and the browser suites wait for a
  readiness flag because the loader page's own `load` comes before the report.

## Appendix A. Edit sites at `91d1afd7`

### A.1 Writers

| Site | Item |
|---|---|
| `src/Kronikol/Reports/ReportGenerator.cs:2289` | the component diagram's source into `#puml-data` |
| `src/Kronikol/Reports/ReportGenerator.cs:2606`, `:2615` | scenario diagrams and their SVG sources into `#puml-data` (`GenerateHtmlReportCore`) |
| `src/Kronikol/Reports/ReportGenerator.cs:4073`, `:4080` | the same in `RenderDiagramsForScenario` |
| `src/Kronikol/Reports/ReportGenerator.cs:2697-2720` | the page's tail: `#puml-data`, `#step-data`, the search index, `WriteFile` |
| `src/Kronikol/Reports/ReportGenerator.cs:2751` | the search index (`SearchIndexBuilder.CompressToBase64`, `SearchIndexBuilder.cs:248`) |
| `src/Kronikol/Reports/StepDeferral.cs:34` | held sub-steps into `#step-data` |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:70` | `#iflow-segments`' `z` (`WrapSegmentData`) |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:395`, `:401` | an activity diagram's `data-plantuml-z` |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:436`, `:476`, `:494` | `data-flame-z` (`GetWholeTestFlowContent`) |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:522`, `:530` | the whole-test activity diagram's batches |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:573`, `:584` | `CompressToBase64`, `DecompressFromBase64` |
| `src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs:199-200` | the component diagram report (unchanged in R1) |

### A.2 Readers (page scripts, `src/Kronikol/Reports/`)

| Site | Reads |
|---|---|
| `report-decompress-helper.js:5-12` | the helper itself; five copies per page (`ReportGenerator.cs:1486`, `:1718`; `DiagramContextMenu.cs:113-116`, `:69`) |
| `plantuml-browser-render-script.js:709-716`, `:1694`, `:1736` | `#puml-data` and `data-plantuml-z` for drawing |
| `context-menu-script.js:74` | a diagram's source for the menu's copy actions |
| `internal-flow-popup-script.js:11`, `:37`, `:195` | `#iflow-segments` and popup diagrams |
| `flame-chart-render-script.js:110`, `:150-155` | `data-flame-z` |
| `report-sub-steps-on-expand.js:11`, `:27` | `#step-data` |
| `report-components-script.js:213-214` | the component diagram's source |
| `report-search-index.js:301-314`, `:335-342`, `:386`, `:542-574` | the search worker's own decoders, and what the page posts to it |
| `report-export-function.js:41-101` | the entries an export copies |
