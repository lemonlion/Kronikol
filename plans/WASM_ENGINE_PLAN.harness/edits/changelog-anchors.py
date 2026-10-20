# The changelog's engine entry says what the report does with the anchors 5.0.0's engine draws (re-wrapped: one line had
# grown past the margin), and the keyboard entry no longer says the browser engine draws a link as text.
# usage: python -I changelog-anchors.py <repo root>
import sys

p = sys.argv[1] + '/CHANGELOG.md'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

pairs = [
    ("""  that passes the check and does not start gives way to `plantuml.js`, and a worker an error takes down while it draws
  is replaced, its diagram drawn once more and then failed alone, naming the error (at once, for a stack overflow). `window.__kronikolRender` says which
  engine draws (`engine`: `wasm` or `js`), why the page is not on the Wasm one (`engineProblem`) and how many workers
  crashed (`workerCrashes`). The new TeaVM no longer runs the engine out of stack on a long label, name or block opener,
  so a diagram that failed with "ran out of stack" in 4.x draws; the statement caps stay where they were. What still
  runs it out of stack is deep nesting, and the report and the Node renderer now say so when it does. The engine
  writes a little more into a diagram: each element's source line (`data-source-line`), a participant's qualified name,
  and an internal-flow link as an `<a>` round its text, as the Java engine draws it, which the report binds by its
  address. The Node renderer's stand-in DOM gained what the new engine asks of it, so a NodeJs report keeps every
  link's label, and its SVG declares the `xlink` prefix its links use (`plans/WASM_ENGINE_PLAN.md`).
""",
     """  that passes the check and does not start gives way to `plantuml.js`, and a worker an error takes down while it draws
  is replaced, its diagram drawn once more and then failed alone, naming the error (at once, for a stack overflow).
  `window.__kronikolRender` says which engine draws (`engine`: `wasm` or `js`), why the page is not on the Wasm one
  (`engineProblem`) and how many workers crashed (`workerCrashes`). The new TeaVM no longer runs the engine out of stack
  on a long label, name or block opener, so a diagram that failed with "ran out of stack" in 4.x draws; the statement
  caps stay where they were. What still runs it out of stack is deep nesting, and the report and the Node renderer now
  say so when it does. The engine writes a little more into a diagram: each element's source line
  (`data-source-line`), a participant's qualified name, and an internal-flow link as an `<a>` round each line of its
  label, as the Java engine draws it, which the report binds by its address. Each of those anchors names the segment in
  a title, which a browser shows as the link's tooltip and reads as its name: the report takes the title off, names the
  first anchor from the whole label and keeps it as the link's one Tab stop. The Node renderer's stand-in DOM gained
  what the new engine asks of it, so a NodeJs report keeps every link's label, and its SVG declares the `xlink` prefix
  its links use (`plans/WASM_ENGINE_PLAN.md`).
"""),
    ("""- **The keyboard opens an internal-flow arrow's popup.** With the browser engine (BrowserJs, the default) an arrow's
  link label was text only a pointer could open. A link with data is one Tab stop now, named from its label; Enter or
  Space opens its popup, and where links show only on hover, focus shows it. A diagram the Java engine drew keeps its
  links as they were.
""",
     """- **The keyboard opens an internal-flow arrow's popup.** With the 4.x browser engine an arrow's link label was text
  only a pointer could open, and the Java engine (Server, Local) drew a link for each line of the label, each named and
  titled with the segment's id. A link with data is one Tab stop now, named from its whole label; Enter or Space opens
  its popup, and where links show only on hover, focus shows it.
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, (t.count(old), old[:100])
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
