# Note copy fidelity — the breaks Kronikol adds, and the ones it hands back

**Status: EXECUTED IN FULL, shipped as 3.0.86.** M1–M6, including the optional M5 (Q2 recommended
deferring it; the instruction was to implement the plan in full, so it shipped).
**Bump:** patch (bug fix), under the semantic-versioning rule added to CLAUDE.md the same day.
Generated diagram bytes change, so it carries a Kronikol4J ledger entry.
**Reported by:** the user, 2026-09-11 — "right clicking where there's heavy word wrap … and when you
right click and open in new tab those same breaks appear unnecessarily".

### Decisions taken on the open questions

- **Q1 — escape or literal.** The **escape**, as recommended. §7.1's argument held up in the code: the
  unescaped/escaped distinction is what `endsWithOwnMarker` keys on, and it is pinned in all four
  implementations.
- **Q2 — search rule 5b.** Taken, not deferred. And it went further than "becomes exact": with every
  break marked, the flush-left heuristic had nothing left to do that the marker does not do better, so
  it was **removed** rather than rewritten — 93 lines of C#, with `IsNoteOpener`, `JsTrim` and the
  JS-whitespace class it needed. The rejoin moved from pass 5b to pass **1b**, ahead of the ASCII fold
  and the creole-escape strip, because pass 3 deletes the `~` the guard depends on.
- **Q3 — strip `\u200b` from Copy Highlighted Text.** Yes, and from every path that reads the painted
  SVG rather than the source (`paintedNoteText`).
- **Q4 — older reports.** The JSON-grammar walk stays as the fallback, so the YAML view still works on
  them; the copy paths behave as they did.
- **Q5 — encoded length.** No fragment-count change observed across the suite; the marker is 8
  characters per break in a payload that is gzipped and base64'd.

### What the execution changed about the plan

1. **§7.3 understated the ordering constraint.** Rejoin-before-unescape is not a nicety: the copy path
   had to be restructured around it, because the old code unescaped per line inside a `.map()` before
   anything was joined. `noteLinesToText` now owns strip → join → rejoin → unescape in one place, and a
   test asserts the rejoin precedes the unescape in `reconstructNoteJson`.
2. **A third loss surfaced at site 6** that §4 only half-caught: `Atoms` split with
   `RemoveEmptyEntries`, and `WrapOneLine` tracked "have I started a line" as `current.Length > 0`.
   Preserving space runs needs both fixed — an empty atom is a legitimate atom, and a leading one means
   the line began with a space.
3. **`extractCallerPayloads` was worse than recorded.** It did not unescape creole either, so copied
   payloads carried `~` escapes. Fixed in the same pass, since it now shares `noteLinesToText`.
4. **One real defect the plan did not foresee**, found by the E2E suite: the deep-search Web Worker is
   built by serialising a **fixed list of functions** with `toString()`. A helper not on that list does
   not exist inside the worker, so the first normalization threw and deep search never returned — no
   error, just a timeout, across all 18 `DeepSearchTests`. The roster now carries the two rule-1b
   helpers, their marker constants are literals **inside** the function bodies rather than module-scope
   `var`s, and a structural test guards both.

### Measured, after

- A marked note and an unmarked one draw at **668 × 145** either way, on real Java PlantUML — the
  marker is free, which is what made the approach viable.
- The **browser** engine resolves `<U+200B>` the same way Java does. This was a real risk (the Java
  measurement did not cover it) and is now pinned by an E2E fact that reads the painted SVG.
- Full run: 3 986 unit + 202 SearchEngine + 44 IKVM + 257 TcpTap + 750 E2E green.

---

## 1. The defect in one paragraph

Kronikol bakes line breaks into note source so the diagram stays inside its width budget. Those
breaks are a *display* decision. But every path that hands note text back to a user — **Copy box
text**, **Open box text in new tab**, **Copy all caller request payloads** — reads the note's source
lines and joins them with `\n`, so the user gets Kronikol's display breaks as if they were in the
payload. When the break landed mid-token, which is the common case, the token is **corrupted**: a JWT
copied out of a report does not parse.

## 2. Two kinds of wrap, and only one of them is the problem

**Engine wrapping** — `skinparam wrapWidth 800`. Happens at *draw* time. The source keeps one long
line and PlantUML decides where to break it when painting. Copy gives the original line back.
Harmless, and not in scope.

**Source wrapping** — Kronikol writes a real `\n` into the note body. This is what the user is
seeing. It exists because `wrapWidth` only breaks at whitespace, and the payloads that get long
(tokens, connection strings, base64, minified JSON, locator chains) frequently have none.

## 3. Every site that bakes a break into a note body

Measured through the generator itself, 2026-09-11:

| # | Site | Rule | Break kind |
|---|---|---|---|
| 1 | `PlantUmlCreator.cs:958` — `FormatNoteContent` | runs > **120** chars with no whitespace | hard mid-token cut |
| 2 | `PlantUmlCreator.cs:320` — user-action note bodies | same 120-char rule | hard mid-token cut |
| 3 | `PlantUmlCreator.cs:450` — `AppendFullPathToNote` | URL in **80**-char chunks | hard cut |
| 4 | `PlantUmlCreator.cs:1253` — `FormatFormUrlEncodedContent` | **80**-char chunks per field | hard cut |
| 5 | `PlantUmlCreator.cs:1267` — `BatchGray` | **80**-char chunks of a header value | hard cut |
| 6 | `DiagramWidth.WrapBlockNoteBody` — assertion notes | **110** chars, whitespace preferred | **either** |

Sites 2 and 6 are new in 3.0.85 — closing two width axes routed two more note bodies through source
wrapping without anyone looking at the copy path. Sites 1, 3, 4 and 5 predate it.

**What it actually looks like.** A 235-character JSON body carrying a JWT, through
`WrapUnbreakableRuns`:

```
[  1] {
[131]   "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkFkYSBMb3ZlbGFjZSIsImlhdCI6MTUxNjIzOTAyMiwicm
[ 80] 9sZXMiOlsiYWRtaW4iLCJhdWRpdG9yIl19.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c",
[ 19]   "expiresIn": 3600
[  1] }
```

Copy that and the token is two strings. Note the continuation line is **flush-left** even though the
JSON around it is indented — that matters in §5.

**What is NOT affected:** a 180-character SQL statement with ordinary spaces came back
`changed=False`. `WrapUnbreakableRuns` only touches runs over 120 characters with no whitespace at
all, so most payload text is untouched. The blast radius is tokens, base64, long URLs, GUID lists and
minified bodies — which is exactly the content someone copies a note to get at.

## 4. The two break kinds are not interchangeable

`WrapOneLine` (site 6) breaks two different ways, and a rejoin has to tell them apart:

- **Between atoms** (`DiagramWidth.cs:215`) — atoms are rejoined with `current.Append(' ')`, so
  breaking between them **consumes a space**. Rejoining must put one back.
- **Hard mid-word cut** (`DiagramWidth.cs:237`) — no space involved. Rejoining must **not** insert one.

Sites 1–5 only ever do the second kind.

**A second, separate loss at site 6.** `Atoms` splits an over-budget line with
`item.Split(' ', RemoveEmptyEntries)`, so a run of several spaces **collapses to one** — before any
break is inserted. A padded assertion message over 110 characters loses its column alignment outright,
which no rejoin can recover and which quietly defeats the monospace note control shipped in the same
release. Lines at or under the budget are returned untouched, so this only bites long lines.

## 5. Three consumers, three different answers, and the clipboard got none

The codebase has already solved this problem twice, in two places, with two different techniques —
and the paths the user actually clicks got neither.

**(a) The search index — a structural heuristic.** `SearchNormalizer.Normalize` rule **5b**,
`RejoinNoteBodies`: inside a note, a continuation line that does not begin with whitespace is joined
to the previous line with no separator. Works on the JSON example above because the continuation is
flush-left and the genuine lines are indented. **It is a heuristic and it over-joins**: a plain-text
or SQL payload whose real lines are flush-left gets them welded together. Tolerable for trigram
matching, fatal for a clipboard.

**(b) The YAML view — a grammar proof.** `reconstructNoteJson` in `collapsible-notes-script.js`:
"JSON forbids raw newlines inside string literals, so a line ending while a string is open is
provably a wrap break." Exact, and it is the right shape of argument — but it only reaches breaks
that land inside a JSON string, and it only runs for payloads that `JSON.parse` accepts. Every
non-JSON payload — SQL, form-encoded, GraphQL, plain text, base64 — gets nothing.

**(c) The copy and open paths — nothing at all.**

| Consumer | Where | Today |
|---|---|---|
| Copy box text (full / current) | `context-menu-script.js:442,445,450` | source lines joined with `\n` |
| Open box text in new tab (full / current) | `context-menu-script.js:604–616` | same text, as a blob |
| Copy all caller request payloads | `context-menu-script.js:647` via `extractCallerPayloads` | same |
| Copy Highlighted Text | `context-menu-script.js:485` | browser selection over painted `<text>` |

This is the same class as the three forked source-rebuild chains factored into `composeNoteSource`
during 3.0.85, and the continuation-chunk index forked in 3.0.67: **the same arithmetic implemented
separately in several places, drifting.** The fix has to end with one implementation, not a fourth.

## 6. Measured: an invisible marker is geometrically free

Probed through real Java PlantUML (IKVM, SVG viewBox), a note body holding a 120-character token cut
in half:

| Case | Drawn | ZWSP reaches the SVG |
|---|---|---|
| unwrapped, one line | 1109 × 129 | — |
| plain `\n` break | **668 × 145** | no |
| literal `U+200B` before the break | **668 × 145** | yes, as `&#8203;` |
| `<U+200B>` escape before the break | **668 × 145** | yes, as `&#8203;` |
| either form *after* the break | **668 × 145** | yes |

Three facts worth keeping:

1. **The marker costs nothing.** Same width, same height, to the pixel, as the plain break.
2. **The escape and the literal character render identically** — PlantUML resolves `<U+200B>` to the
   same character, so the choice between them is a *source* question, not a rendering one.
3. **The character does reach the painted SVG.** So anything that reads text out of the DOM rather
   than the source — *Copy Highlighted Text*, and the `noteGroups[…].texts` fallback in the context
   menu — must strip `​` explicitly.

## 7. The fix

### 7.1 Mark the breaks Kronikol inserts

Emit an invisible marker at the end of every line a wrap site broke, so a rejoin can undo exactly
those breaks and leave every genuine newline alone. Two markers, because §4 established two break
kinds:

- `<U+200B>` — "the next line continues this one with **nothing** between".
- `<U+200B><U+200B>` — "…with **one space** between". (Only site 6 ever emits it.)

**Use the escape form, not a literal character.** `EscapeCreoleMarkup` escapes `<`, so a payload that
literally contains the text `<U+200B>` arrives in the source as `~<U+200B>`. An **unescaped**
`<U+200B>` is therefore provably Kronikol's own — the identical argument `reconstructNoteJson`
already makes about focus-emphasis tags. A literal `​` would be indistinguishable from one in
the user's payload. The cost is 8 source characters per break, inside a payload that is gzipped and
base64'd into the report.

### 7.2 Stop site 6 collapsing spaces

`Atoms` must preserve runs of spaces rather than `RemoveEmptyEntries`-ing them, so that what the
rejoin produces is the original line. Independent of the marker, and needed for the monospace control
to mean anything on a long padded line.

### 7.3 One rejoin, used by everything

A single `rejoinWrappedNoteLines(lines)` in `collapsible-notes-script.js`, exported on `window` the
way `_parseNoteBlocks` and `_getNoteBBox` already are, consumed by:

- `noteText` and `currentText` in the context menu (covers Copy **and** Open in new tab, which share
  the same two strings)
- `extractCallerPayloads`
- `reconstructNoteJson` — the marker makes it exact, and lets the `endsInsideJsonString` grammar walk
  become a **fallback for pre-marker reports** rather than the primary path
- the `noteGroups[…].texts` fallback, plus a `​` strip for *Copy Highlighted Text*

### 7.4 Search rule 5b becomes exact

With markers present, `RejoinNoteBodies` can join on the marker instead of guessing at flush-left,
which also fixes the over-join on flush-left plain-text payloads. This touches the **three** files
that must stay byte-identical — `SearchNormalizer.cs`, `report-search-index.js` and
`tools/search-bench/normalize.js` — and invalidates `SearchIndexBuildCache`. See Q2; it is separable
from the rest and could ship later.

## 8. What this deliberately does not change

- **Rendered output.** §6 measured the marker as pixel-identical. The IKVM pin in M1 holds that.
- **Where the breaks go.** The width budget is unchanged; only its reversibility changes.
- **`Copy source` / `Open source in new tab`.** That is the PlantUML source and the breaks are
  genuinely part of it. The marker will be visible there as `<U+200B>`, which is honest.
- **Non-`BrowserJs` reports.** No context menu, no copy paths — they get the generator half (markers)
  and nothing else.

## 9. Tests

Following the repo's rule that a regression test must be able to see the actual symptom:

- **C# unit** — each of the six sites emits the marker; an unwrapped payload is byte-identical to
  today (this is what keeps ordinary reports unchanged); the two marker kinds land on the right break
  kind; `Atoms` preserves space runs.
- **IKVM** — a marked note and an unmarked one draw at the same size, and the marker never produces
  `Syntax Error`. Extends `SequenceDiagramWidthTests`' existing harness.
- **JS unit** (`NoteAppearanceScriptTests` style) — `rejoinWrappedNoteLines` round-trips each break
  kind, leaves genuine newlines alone, and leaves a payload's own escaped `~<U+200B>` untouched.
- **Playwright E2E** — the real symptom: a report whose payload holds a >120-character JWT, copied
  via the context menu, yields the JWT **unbroken**. Clipboard joins lines with `\r\n` on Windows, so
  assert per line (the 3.0.79 copy-text contract). The same fact against *Open box text in new tab*
  by reading the opened blob. All must fail against today's build.
- **Search** (only if Q2 is taken) — the cross-language vector pins must move together;
  `normalize.js` stays the reference.

## 10. Milestones

| | | |
|---|---|---|
| **M1** | Marker emission at all six sites + `Atoms` space fix | C# unit + IKVM |
| **M2** | `rejoinWrappedNoteLines`, one implementation | JS unit |
| **M3** | Wire copy / open-in-new-tab / caller payloads | E2E |
| **M4** | `reconstructNoteJson` switches to the marker, grammar walk demoted to fallback | JS unit |
| **M5** | *(optional, Q2)* search rule 5b becomes exact | cross-language pins |
| **M6** | Wiki, changelog, Kronikol4J ledger | — |

M1–M3 are the user-visible fix and are worth shipping without M4/M5 if they get complicated.

## 11. Open questions

- **Q1.** Escape form or literal character? §7.1 recommends the **escape**, for the provably-ours
  argument. The counter-argument is 8 source bytes per break and visible clutter in *Copy source*.
- **Q2.** Does search rule 5b change in this release? It is the only part that touches the
  three-file cross-language vector and invalidates the index cache. **Recommend deferring** to keep
  the user-visible fix small; the over-join it would fix is a search-quality issue, not a corruption.
- **Q3.** Should *Copy Highlighted Text* strip `​`? Recommend **yes** — it is unconditional and
  costs one `replace`.
- **Q4.** Older reports have no markers. `reconstructNoteJson`'s grammar walk stays as a fallback, so
  the YAML view keeps working on them; the copy paths simply behave as they do today. Confirm that is
  acceptable rather than attempting a heuristic for old reports.
- **Q5.** The marker adds ~8 characters per break to the diagram source, which feeds
  `EncodedDiagramExceedsMaxLength`. Measure whether any realistic report changes its **fragment
  count** — if it does, the marker must be excluded from the length estimate rather than the budget
  being raised.

## 12. What a port needs to know

Kronikol4J pins report and diagram output byte-for-byte. Every marker emitted at the six sites is a
byte change, so the standing divergence widens and the README ledger needs an entry. The client-side
half is already divergent (`collapsible-notes-script.js` has been since 3.0.59).
