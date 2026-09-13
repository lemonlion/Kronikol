# Note YAML view: trailing-whitespace lines defeat the block scalar — fix plan

**Status: ✅ EXECUTED IN FULL — shipped in 3.0.79 (2026-09-04).** Implemented exactly
as planned (all red tests, the §3 green change, the §3.5 refactor, docs, release);
the one deviation: the copy-text fact asserts per-line (the clipboard joins lines
with `\r\n` on Windows, so a blanket no-`\r` assertion would false-fail). Kept as a
design record; see `PLANS_STATUS.md`.

User report: a BigQuery note in a 3.0.73-generated report stays a one-line quoted
scalar when toggled to YAML — no multiline splitting. Behavior confirmed unchanged at
HEAD (3.0.76 working tree).

---

## 1. Symptom and root cause

The note's `query` string trips the **trailing-whitespace-line guard** in
`formatYamlString` (`src/Kronikol/Reports/collapsible-notes-script.js`, the
`/[ \t]$/.test(blockLines[i])` check in the eligibility loop, currently line 997).
Any block line ending in space/tab makes the whole string ineligible, so it falls to
`yamlQuote` — the one-line double-quoted form the user sees.

The reported payload has **two independent offenders** (either alone forces the fallback):

1. `SELECT \n` — the SQL's `SELECT` line has a trailing space before its break
   (typical of SQL authored in a C# raw/verbatim string).
2. The string ends `...GROUP BY daily.location_id\n            ` — 12 spaces after
   the last `\n` (the raw string's closing indentation), so the final block line is
   all-whitespace.

Verified against the real script (Node + vm harness driving the exported
`window._noteFormatInternals.jsonTextToYamlLines`):

- payload as-is → quoted one-liner (0 block lines)
- only the `SELECT ` space removed → still quoted
- only the all-space tail removed → still quoted
- both removed → `query: |2` block scalar, 12 content lines

Why 3.0.73 didn't cover it: 3.0.73 fixed the *mixed-breaks classification* (bare `\n`
opener + CRLF body). This payload has bare-LF breaks throughout, so the CRLF gate is
not even involved; the trailing-whitespace guard is a separate, later check that
3.0.61/3.0.63/3.0.73 never touched. The current behavior is documented as intended
(wiki `PlantUML-Browser-Rendering.md` § Bytes fidelity: "lines with trailing
whitespace ... fall back to a double-quoted scalar") and pinned by
`Crlf_string_that_takes_the_quoted_fallback_keeps_its_cr_bytes`
(`tests/Kronikol.Tests.EndToEnd/NoteYamlInternalsTests.cs`, ~line 431). But it defeats
the feature for exactly the payload class it was built for: SQL in raw strings
routinely carries trailing spaces and an all-space closing-indentation tail.

## 2. Design decision

Extend the 3.0.61 display-normalisation trade to trailing whitespace. Precedent: the
CRLF rule already trades invisible bytes for a readable block scalar — *the YAML view
drops them; the JSON view stays exact; quoted fallbacks always show original bytes*.
Trailing spaces/tabs before a line break are exactly as invisible as `\r`:

- the SVG note text cannot display them (they render as nothing);
- the copy-text path can't meaningfully carry them (and already drops `\r`);
- keeping the quoted fallback for them helps no reader.

**Rule:** for multiline strings, strip `[ \t]+` immediately before every line break
and any `[ \t]+` tail after the final break from the *display* value. `value` stays
untouched — every quoted fallback keeps quoting the original bytes, and the JSON view
is unaffected.

Explicit non-goals:
- Single-line strings are NOT normalised (`"abc "` still shows as `"abc "` — there is
  no readability payoff, so no reason to trade bytes).
- Other Unicode whitespace (` ` etc.) is NOT stripped — YAML block-scalar
  faithfulness only cares about ASCII space/tab, matching the current guard.
- Multiple-trailing-newline strings still take the quoted fallback (unchanged rule,
  but now judged on the normalised display — see edge cases).

## 3. The change (green step)

All in `formatYamlString` (`collapsible-notes-script.js` ~975-1010):

1. Keep the existing CRLF normalisation of `display` first (unchanged).
2. Keep the existing single-line early return (`display.indexOf('\n') < 0`) — it uses
   `value`, so single-line behavior is untouched. (Order note: stripping before this
   check would be harmless for the check itself, but strip *after* it to make the
   single-line non-goal structurally obvious.)
3. After the early return, normalise:
   `display = display.replace(/[ \t]+(?=\n)/g, '').replace(/[ \t]+$/, '');`
4. The `endsWithNewline` / `blockLines` / `firstContent` / eligibility logic then runs
   on the normalised display exactly as today. The multiple-trailing-newline guard
   (`!(endsWithNewline && /\n$/.test(display.slice(0, -1)))`) thereby judges the
   normalised form — intended (see edge cases).
5. Refactor step (only after green): delete the now-dead `/[ \t]$/.test(blockLines[i])`
   branch from the eligibility loop (no line can end in space/tab post-normalisation;
   `\x0b` and other control chars are already caught by the control-char check, and
   non-ASCII whitespace was never caught by this guard). Keep the loop itself —
   `hasOverlongRunAfterEscape` still needs it. Rewrite the function's design comment
   (lines ~960-974) to state the extended trade.

No other functions change. `emitYamlValue`, the `|2` indicator logic, `inSeq`
fallback, `yamlQuote`, escape/copy paths: all untouched.

## 4. Edge-case analysis (drives the red-test list)

| Input (JSON string value) | Today | After fix | Why |
|---|---|---|---|
| The reported BigQuery query (bare-LF breaks, `SELECT ` line, `\n` + 12-space tail) | quoted | `query: \|2` + empty opener + 12 stripped lines (exact expectation in §5) | both offenders stripped; tail-strip leaves a single trailing `\n` → keep-clip header (no `-`) |
| `"x \ny"` | quoted | `\|-` block `x` / `y` | mid-string trailing space stripped |
| `"x\t\ny"` | quoted | `\|-` block | tabs count as trailing whitespace |
| `"x\n   "` | quoted | `s: \|` + `  x` | tail-strip → ends with one `\n` → `\|` header |
| `"a\n   \nb"` | quoted | `\|-` block with empty middle line | interior whitespace-only line becomes an empty line (representable) |
| `"a \r\nb"` (uniform CRLF + trailing space) | quoted **with `\r` bytes** (pinned) | `\|-` block `a` / `b` | CRLF-normalised then stripped — the existing pin flips, rewrite it (§5) |
| `"a \r\nb\nc"` (mixed breaks + trailing space) | quoted original | quoted original (unchanged) | CRLF gate fails → `display` keeps `\r` → control-char check quotes `value` |
| `"abc "` (single line) | quoted `"abc "` | quoted `"abc "` (unchanged) | non-goal: no normalisation without a `\n` |
| `"a\n\n  "` | quoted | quoted original (still) | tail-strip → `"a\n\n"` → multiple trailing newlines → existing guard |
| `" \n \n"` (whitespace-only lines throughout) | quoted | quoted original (still) | strips to `"\n\n"` → no non-empty line to anchor → existing `firstContent` guard |
| `"a \nb"` | block (guard never matched NBSP) | block, NBSP preserved | non-goal pin: only ASCII space/tab stripped |

Interactions checked: the `|2` indicator anchors on the first non-empty line's
*leading* spaces (untouched); `inSeq` indicator fallback unchanged; overlong-run check
runs on stripped lines (same or shorter — can only gain eligibility, never lose it);
`hasNoteFill`/escape/wrap pipeline consumes emitted lines as today (`block: true`
lines still never wrapped).

## 5. TDD plan

### Red — `tests/Kronikol.Tests.EndToEnd/NoteYamlInternalsTests.cs` (Playwright-driven internals)

New facts, in a new "Trailing whitespace" region (names indicative):

1. `Trailing_space_line_emits_block_scalar` — `{"s":"x \ny"}` →
   `["s: |-", "  x", "  y"]`.
2. `Trailing_tab_line_emits_block_scalar` — `{"s":"x\t\ny"}` → same shape.
3. `All_space_tail_after_final_break_emits_keep_clip_block` — `{"s":"x\n   "}` →
   `["s: |", "  x"]`.
4. `Interior_whitespace_only_line_becomes_empty_block_line` — `{"s":"a\n \nb"}` →
   `["s: |-", "  a", "", "  b"]` (assert the empty line's `block` flag too).
5. `Bigquery_raw_string_sql_with_trailing_spaces_unfolds` — the real shape: value
   `"\n                -- c1\n                SELECT \n                    x\n            "`
   (leading `\n`, trailing-space `SELECT `, all-space tail) → `["q: |2", "",`
   `"                  -- c1", "                  SELECT", "                      x"]`.
   (Full-payload expectation validated in the harness: header `|2`, empty opener,
   12 content lines for the reported note.)
6. `Uniform_crlf_with_trailing_space_now_emits_block_scalar` — `{"t":"a \r\nb"}` →
   `["t: |-", "  a", "  b"]`. **Replaces** the body of
   `Crlf_string_that_takes_the_quoted_fallback_keeps_its_cr_bytes`.
7. `Mixed_breaks_with_trailing_space_keep_quoted_original_bytes` — `{"t":"a \r\nb\nc"}`
   → `["t: \"a \\r\\nb\\nc\""]` — the regression trap the old pin protected (quoted
   form shows ORIGINAL bytes incl. the space and `\r`), re-pinned on a still-quoted case.
8. `Single_line_trailing_space_stays_quoted_verbatim` — `{"s":"abc "}` →
   `["s: \"abc \""]` (non-goal pin).
9. `Multiple_trailing_newlines_after_strip_stay_quoted` — `{"s":"a\n\n  "}` →
   `["s: \"a\\n\\n  \""]` (original bytes).
10. `Whitespace_only_string_stays_quoted` — `{"s":" \n \n"}` → quoted original.
11. `Nbsp_is_not_stripped` — `{"s":"a \nb"}` → block with NBSP preserved
    (pin current behavior so the strip regex is never "improved" into Unicode).

Run first, confirm red (1-6 fail today; 7-11 should already pass — they are guards;
any that fail today reveal a second latent bug: investigate per CLAUDE.md before
proceeding).

### Red — UI level, `tests/Kronikol.Tests.EndToEnd` (per CLAUDE.md: UI features get Playwright coverage)

One end-to-end fact (e.g. in `NoteYamlToggleTests`): generate a report whose note
payload is the BigQuery job shape (reuse/extend an existing SQL-note fixture
generator; the fixture's query string must contain a trailing-space line AND the
`\n` + spaces tail), hover the note, click `Y`, assert the note now displays the
`|2` header line and one of the SQL lines as its own line (and NOT a `\\n`-bearing
one-liner). Follow the E2E rules: `PollingInterval = 200`, `mouseenter` dispatch,
`.First` on selectors.

### Red — copy-text guard

Extend `YamlNoteCopyTextTests` with one fact on the new fixture: copied YAML text
contains the stripped SQL lines (no trailing spaces, no `\r`), i.e. the copy contract
"exactly as displayed" holds for the newly-eligible strings.

### Green

Implement §3 items 1-4 only. Re-run the new facts + the full
`NoteYamlInternalsTests` / `NoteYamlToggleTests` / `YamlNoteCopyTextTests` /
`NoteFormatToggleScriptTests` set.

### Refactor

§3 item 5 (dead guard removal + comment rewrite). Audit
`tests/Kronikol.Tests/Reports/NoteFormatToggleScriptTests.cs` for textual pins on the
removed branch/comment. Full suite green.

## 6. Documentation

- Wiki `PlantUML-Browser-Rendering.md` § Bytes fidelity (~line 133): the fallback
  list drops "lines with trailing whitespace"; the CRLF-exception sentence grows the
  trailing-whitespace strip (3.0.7x+), stated as the same knowing trade (YAML view
  only; JSON exact; quoted fallbacks keep original bytes; copy-text yields displayed
  lines). Also the § header-recording paragraph (~135): note the tail-strip can turn
  an "ends without newline" string into a `|` (keep-clip) one.
- `CHANGELOG.md` entry (user-reported, BigQuery repro).
- Memory `note-yaml-toggle.md`: add the 3.0.7x paragraph alongside the 3.0.61/3.0.63/
  3.0.73 lineage.
- Kronikol4J: extends the standing `collapsible-notes-script.js` divergence (recorded
  in its README note; no report-output impact — client-side script only).

## 7. Release

Per CLAUDE.md: bump ALL packages one patch, changelog, commit, tag `v{version}`, push.
**Coordinate with the other in-progress session** — pick the version after it lands;
template pins track the previous release as usual.

## 8. Repro harness

Session scratchpad `repro-bigquery-yaml.js` (loads the real script in Node `vm` with a
stubbed `window`/`document`, runs `jsonTextToYamlLines` on the exact reported payload
and the fix-simulating variants; prints the expected `|2` emission). Scratchpad is
session-local — the essential inputs/expectations are inlined in §5, so the harness is
disposable.
