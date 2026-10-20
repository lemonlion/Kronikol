# PlantUmlStatementLimits says what 5.0.0's engine does with the statements its caps were measured on
# (plans/WASM_ENGINE_PLAN.md). usage: python -I statement-limits-doc.py <repo root>
import sys

root = sys.argv[1]
p = root + '/src/Kronikol/PlantUml/PlantUmlStatementLimits.cs'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

pairs = [
    ("""/// re-verified against the <c>@v1.2026.6-patched</c> build, the stock <c>@v1.2026.8beta1-0e4f452</c>
/// build, and the published <c>@plantuml/core@1.2026.8</c> that replaced it, where every pin below holds and
/// every edge sits within a few characters of the build before: plans/ENGINE_PIN_PLAN.md §1.11). They are per statement
""",
     """/// re-verified against the <c>@v1.2026.6-patched</c> build, the stock <c>@v1.2026.8beta1-0e4f452</c>
/// build, and the published <c>@plantuml/core@1.2026.8</c> that replaced it, where every pin below holds and
/// every edge sits within a few characters of the build before: plans/ENGINE_PIN_PLAN.md §1.11; for the engine 5.0.0
/// ships, which no stack edge below binds, see the last paragraphs). They are per statement
"""),
    ("""/// the TeaVM build, which Java draws far past. Every cap is applied where the source is written rather than where
/// a renderer is chosen, because the same source may be rendered either way.
/// </para>
""",
     """/// the TeaVM build, which Java draws far past. Every cap is applied where the source is written rather than where
/// a renderer is chosen, because the same source may be rendered either way.
/// </para>
/// <para>
/// <b>5.0.0's engine.</b> From 5.0.0 the engine is a build of PlantUML's master on TeaVM 0.17.0-dev-2, whose regular
/// expressions no longer recurse once per character (plans/WASM_ENGINE_PLAN.md). In node it draws a block opener of
/// 16,000 characters, a coloured bar of 6,000 and a participant's name of 8,000, and fails past those only where the
/// diagram is wider than its 98,304-pixel limit (measured 2026-10-20); in a Chrome worker, an earlier master on the same
/// TeaVM drew an edge label of 102,400 characters and a participant's name of 12,800 in both its engines (2026-10-17).
/// It still runs out of stack on deep nesting, from about 2,000 groups in node, and fails that as text. The message
/// limit is PlantUML's and holds. The constants above stay where the 4.x engine's worker put them for now: raising them
/// changes what every report writes, which is a change of its own.
/// </para>
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, old[:100]
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
