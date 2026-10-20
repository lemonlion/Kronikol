# WASM_ENGINE_PLAN.md and the wiki series' notes record what the full suites found on the merged branch: the engine's
# anchors carry the segment's id as a title and come one to a line, so the binder takes the titles off and makes the
# first anchor the link's one stop (D8, section 6, section 7, section 8); patch 0010 now carries Generated Reports' line
# and the series' tree is b9885ddd. usage: python -I plan-anchors.py <repo root>
import sys

root = sys.argv[1]


def edit(rel, pairs):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    for old, new in pairs:
        assert t.count(old) == 1, (rel, t.count(old), old[:100])
        t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8'))


edit('plans/WASM_ENGINE_PLAN.md', [
    ("""- **D8. A link stays the `<a>` the engine draws.** From 1.2026.10 the TeaVM engine draws an internal-flow link as the Java
  engine does, an `<a href="#iflow-…" target="_top">` round its underlined text, where the 4.x engine drew the text
  alone. The report already binds the Java engine's `<a>` by its address (Server and Local rendering; the popup
  script's capture-phase click handler stops the navigation), so a browser-drawn link now takes that path. The Node renderer keeps the link too
  (section 4).
""",
     """- **D8. A link stays the `<a>` the engine draws.** From 1.2026.10 the TeaVM engine draws an internal-flow link as the Java
  engine does, an `<a href="#iflow-…" target="_top">` round its underlined text, where the 4.x engine drew the text
  alone. The report already binds the Java engine's `<a>` by its address (Server and Local rendering; the popup
  script's capture-phase click handler stops the navigation), so a browser-drawn link now takes that path. The Node
  renderer keeps the link too (section 4). The engine draws one `<a>` round each line of a label, and each carries the
  segment's id in `title` and `xlink:title`, which a browser shows as the tooltip and reads as the link's name: a
  two-line label was two Tab stops, each read as "#iflow-…", where Option C's S12 promised one stop named from the
  label. The binder takes the titles off, names the first anchor from the whole label (`aria-label`) and leaves the
  rest to the pointer (`tabindex="-1"`, `aria-hidden`); where links show only on hover, focus on the first shows the
  link. The Java engine's anchors take the same path. The SVG a report stores keeps the titles.
"""),
    ("""on drawing. The integrity and mirror facts name the engine each path fetches.
""",
     """on drawing. The integrity and mirror facts name the engine each path fetches. `IflowLinkKeyboardTests`: anchors drawn as
the engine draws them, one to a line with the title in each of its three forms, are one stop named from the whole
label and lose every title, bound twice or with no data; focus on the first shows a link that shows only on hover.
`ThemedSourceRendersTests` compares a themed and a plain drawing without their source lines, which the `!theme` line
moves, and `FocusClearOfTheBarTests` tabs to the `<a>` the engine draws (its fixture wrapped the label in a second one).
"""),
    ("""- **A link's tooltip:** the engine's `title` and `xlink:title` name the segment (`#iflow-…`), as the Java engine's always
  did.
""", ""),
    ("""- 2026-10-20: built, published on the fork, measured; Kronikol's side written test first in the worktree
  `Kronikol-v5-wasm` from `v5` (`475410f0`).
""",
     """- 2026-10-20: built, published on the fork, measured; Kronikol's side written test first in the worktree
  `Kronikol-v5-wasm` from `v5` (`475410f0`).
- 2026-10-20, the full suites on the branch merged with `main` (`a48a4582`): core 8,507 passed with 2 skipped;
  Playwright 1,382 passed, 28 skipped and 3 failed, all from the new engine's output. A themed and a plain drawing
  differed in their source lines, and a focus fixture wrapped the engine's own `<a>` in a second one; reading why found
  the anchors' titles and one stop to a line (D8). The binder's facts failed first on the script before the change (all
  three, for the titles, the name and the focus), and ten of the eleven mutations in `checks/mutate-anchors.py` are
  killed; the one that survives binds a link's anchors again on every pass, which adds the same name and listeners and
  no reader can see. The classes that draw links, focus, flows or exports pass: 168 with 4 skipped.
"""),
])
edit('plans/OPTION_C_PLAN.harness/wiki/NOTES.md', [
    ("""all ten: tree `94a3e5ac`).""",
     """all ten: tree `94a3e5ac`; again after `0010` gained Generated Reports' keyboard line: tree `b9885ddd`)."""),
    ("""| `0010` | `WASM_ENGINE_PLAN.md` | The engine 5.0.0 draws with: PlantUML Browser Rendering (a new section, The Wasm engine), Large Response and Diagram Handling, Diagnostics and Debugging, Report Configuration |""",
     """| `0010` | `WASM_ENGINE_PLAN.md` | The engine 5.0.0 draws with: PlantUML Browser Rendering (a new section, The Wasm engine), Large Response and Diagram Handling, Diagnostics and Debugging, Report Configuration, and Generated Reports' line on what the keyboard reaches in an internal-flow link |"""),
])
print('ok')
