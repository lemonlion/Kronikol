# Stage 1 plan grouping

**Date:** 2026-09-22 · **Repo version:** 3.27.2 · **Status:** a grouping, not a plan. It green-lights
nothing and writes none of the five plans it names.

Stage 1 of [`ROADMAP.md`](ROADMAP.md) is "the patch train": live defects in shipped code. This file
says how its items group into plans. Items 1.1 and 1.2 shipped as 3.25.3 and 3.26.0, confirmed
against the tags. Seven items are left, and all seven defects were checked against HEAD on
2026-09-22 and are still present.

| Plan | Stage 1 items | Where it lives | Releases | Decision needed |
|---|---|---|---|---|
| P1. The ingest feed loses nothing. **Executed:** [`INGEST_FEED_PLAN.md`](INGEST_FEED_PLAN.md) (2026-09-22; revised the same evening: S2 prototyped and measured byte-identical against a real suite, F7 added; **executed 2026-09-23: R1 as 3.27.4, R2 as 3.29.0, R3 run on the shipped code; audited 2026-09-24, follow-ups as 3.29.1**) | 1.3, 1.4, 1.7 | Ingestion records, the ingest command (tracks D and A) | one patch, then one minor | D4 |
| P2. The toolbar at every width. **Written:** [`TOOLBAR_AT_EVERY_WIDTH_PLAN.md`](TOOLBAR_AT_EVERY_WIDTH_PLAN.md) (2026-09-22, revised the same evening, not green-lit; measured at HEAD, harness beside it; found on the way: a long branch name scrolls the page to 1320 px, the violet theme's active toggles go pale on hover, and `InternalFlowPopupCustomStyleSheet` has never been applied) | 1.5 | the two report stylesheets, the violet constant, the `<style>` order in `ReportGenerator`, E2E (track B) | one patch | D5 (and its Q6) |
| P3. Diagram colours, and an honest theme option. **Written:** [`DIAGRAM_COLOURS_PLAN.md`](DIAGRAM_COLOURS_PLAN.md) (2026-09-22, not green-lit; revised the same day: measured through the shipped worker host, a theme under BrowserJs leaves every diagram undrawn, and under NodeJs fails each after 20 s, because the mock DOMs never answer the engine's script append, so the theme slice is a fail-fast mock DOM plus the diagnostic) | 1.6 | PlantUML source emitter, render script, worker host and Node script, diagnostics (track B) | one patch, possibly a minor | none |
| P4. The engine pin. **Written:** [`ENGINE_PIN_PLAN.md`](ENGINE_PIN_PLAN.md) (2026-09-22, not green-lit; measured: npm 1.2026.8 is SVG-identical to the pinned build on the corpus, so no golden re-pin; second pass: the browser verifies the hash itself in three engines, so the check ships no digest code) | 1.8 | engine constants, Node renderer, worker host, render-bench (track B) | one patch, golden re-pin only if bytes move | D6 |
| P5. Internal-flow segments as one blob. **Written:** [`INTERNAL_FLOW_BLOB_PLAN.md`](INTERNAL_FLOW_BLOB_PLAN.md) (2026-09-22, not green-lit; measured on the largest local report and in Chromium: 8.15 MB to about 3.14 MB, or 2.73 MB with the map's own islands carried raw (its Q7); the head the export copies 6.18 MB to about 1.17 MB; 55% of that map is segments the data file does not contain, its Q8) | 1.9 | InternalFlow generator, popup script, merge, export (track B) | one patch | none |

**P1, why together:** all three are the `kronikol ingest` path, and one acceptance harness proves all
of them. Capture a real suite through the NDJSON writer, ingest it, and compare what came out with
what went in. Contents: the log-to-record mapping at
[InteractionRecord.cs:164](../src/Kronikol/Ingestion/InteractionRecord.cs#L164) sets the duration; the
record gains a field for user PlantUML, with the member-by-member diff test the store plan's R4 asks
for; the CLI's local render option stops throwing; the zero-byte Specifications output, verified
before it is fixed. Two releases because D4's rule puts #94 alone in a patch. Flag: the member diff
will name six further losses beyond #93 and #94. The plan should pin those as known gaps feeding
14.1 rather than fix them, or it doubles in size.

**P2, why alone:** the toolbar plan's own section 2.1 calls it an own release independent of
everything else, and its verification is a viewport sweep, unlike any other report item. Contents:
no-wrap on the export buttons, min-width on the search input, the minimal band wrap below about
1000 px that D5 decides, the toggle base styles moved out of the internal-flow stylesheet so the
toolbar is styled without flow tracking, the violet active-hover rule, and the 320 to 1400 px sweep
as a permanent Playwright guard. The existing responsive tests spot-check four widths, so the sweep
is new. One Kronikol4J ledger entry, since the stylesheet is byte-shared.

**P3, why alone:** it changes diagram bytes, where P2 changes only CSS, and its spec sits in the
theme plan rather than the toolbar plan. Contents: the grey note header replaced by a computed value
that clears AA on the default note, emitted at two sites in
[PlantUmlCreator.cs:454](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L454); the link hover restore
writes the captured original fill instead of black, at two sites in the render script; a diagnostic
when a theme is set under BrowserJs. Flag: a new diagnostic kind counted as public surface in the
3.27.0 changelog, so that slice is a minor unless it reuses an existing kind. Golden churn is wide
because every chunked note carries the header, so it ships as its own release.

**P4, why alone:** the riskiest item, and the one to revert on its own. Contents: the pin at
[TrackingDefaults.cs:28](../src/Kronikol/Constants/TrackingDefaults.cs#L28) moves from the fork tag to
the published npm package on jsDelivr's immutable route, with the fork URL also referenced by two
more source files, two test files and two render-bench scripts; the size cap pass-through kept at all
three engine sites; a known-hash check on the engine fetch in the worker host and the Node cache; the
perf-budget assertion the Teoz plan promised; the fragment-height re-measure recorded as a number
only, because the default flip waits for 12.1. Needs D6, whose recommendation is to move now.

**P5, why alone:** self-contained, needs no decision, and the roadmap wants it landed before #86 and
#87 change segment keying. Contents: the segment map at
[InternalFlowHtmlGenerator.cs:41](../src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L41)
becomes one gzip-plus-base64 blob, decompressed on the first popup using the decompressor every
report already ships. Two things issue #89 does not cover and the plan must: the merge path, since
the mergeable report carries the segments and must read the block back; and filtered export, which
will carry the blob whole, to be measured and either accepted or pruned. The puml-data trap goes into
the plan in writing. One ledger entry.

If fewer plans are wanted, P2 and P3 merge cleanly into one report-look plan with two releases,
giving four. Nothing else merges without crossing tracks.

**Suggested order:** P1 first, since it is the cheapest and waits on nothing but D4's bump. Then P5
while D5 and D6 are answered. Then P2, P3 and P4. The last four are all track B, so in one working
tree they run one after another. In separate sessions their files are disjoint, but only one of them
should be re-pinning goldens at a time.

**Three decisions to answer ahead of the work:** D4, D5 and D6. All three have a recommendation in
the roadmap's decision queue (section 3).
