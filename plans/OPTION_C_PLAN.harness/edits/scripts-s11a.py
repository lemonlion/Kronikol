# S11a: the colours carried from S10, mapped to the theme's tokens: an activity diagram's (a scenario's whole-test flow
# and the internal-flow popup, both drawn in an .iflow-diagram box; InternalFlowRenderer's skin, which the pinned engine
# writes as fill="#F0F4FF" stroke="#666666" boxes and stroke="#CCCCCC" lanes), the component diagram's person box,
# database edge and edge labels, and a sequence note's header line (NotePalette.HeaderInk #686868). The preview's
# c-round5.css 90-110 and c-round17.css 119-121, scoped by container where one literal means two things (#666666 is an
# activity's edge and a component edge's label). Each mapping falls back to the engine's colour, so a page without the
# token sheet draws as before. usage: python scripts-s11a.py
import os
# in the checkout this file is in, whichever worktree that is
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'src', 'Kronikol', 'Reports', 'inline-svg-styles.css')
s = open(P, encoding='utf-8', newline='').read()
assert 'var(--dg-activity' not in s
anchor = '/* A diagram narrower than its box'
assert s.count(anchor) == 1
s = s.replace(anchor, '''/* An activity diagram's own colours (a scenario's whole-test flow and the internal-flow popup), the component diagram's,
   and a sequence note's header line, mapped to the theme's tokens (5.0.0, plans/OPTION_C_PLAN.md S11); each falls back
   to the colour the engine painted. */
.iflow-diagram svg { background: var(--dg-bg, transparent) !important; }
.iflow-diagram svg text[fill="#000000" i] { fill: var(--dg-ink, #000000); }
.iflow-diagram svg :is([style*="stroke:#181818" i], [stroke="#181818" i]) { stroke: var(--dg-line, #181818) !important; }
.iflow-diagram svg polygon[fill="#181818" i] { fill: var(--dg-line, #181818); }
.iflow-diagram svg rect[fill="#f0f4ff" i] { fill: var(--dg-activity, #f0f4ff); }
.iflow-diagram svg :is([style*="stroke:#666666" i], [stroke="#666666" i]) { stroke: var(--dg-activity-edge, #666666) !important; }
.iflow-diagram svg :is([style*="stroke:#cccccc" i], [stroke="#cccccc" i]) { stroke: var(--dg-lane, #cccccc) !important; }
#component-diagram svg [fill="#08427b" i] { fill: var(--dg-person, #08427b); }
#component-diagram svg :is([style*="stroke:#09616a" i], [stroke="#09616a" i]) { stroke: var(--dg-db-edge, #09616a) !important; }
#component-diagram svg text[fill="#666666" i] { fill: var(--dg-ink-soft, #666666); }
.plantuml-browser:where(:not(.iflow-diagram)) svg text[fill="#686868" i] { fill: var(--dg-ink-soft, #686868); }

''' + anchor)
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
