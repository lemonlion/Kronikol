# S10b-2 (the stylesheet): the roles the role pass gives a drawn sequence diagram, coloured from the theme's tokens
# (P31: the preview's c-diagram-look.css 33-46 without the badges, and c-round17.css 47, 50, 51 for the step table's
# header and rules). In inline-svg-styles.css, the diagrams' own sheet, which ships wherever the page draws them.
# usage: python scripts-s10b-c.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/inline-svg-styles.css'
s = open(P, encoding='utf-8', newline='').read()
assert '.k-lifeline' not in s
s = s.rstrip('\n') + '\n' + '''
/* The roles the page gives a drawn sequence diagram's elements (5.0.0, plans/OPTION_C_PLAN.md S10), coloured from the
   theme's tokens, so a PlantUML theme or a recoloured source changes nothing here. Doubled classes: the dark theme's
   mapping of the engine's own colours has the same specificity otherwise. */
.plantuml-browser svg .k-lifeline.k-lifeline { stroke: var(--dg-lifeline) !important; }
.plantuml-browser svg .k-participant.k-participant { stroke: var(--dg-participant-edge) !important; }
.plantuml-browser svg .k-participant-fill.k-participant-fill { fill: var(--dg-participant) !important; }
/* notes as cards, an event note in the event colour; the fold stays, hidden, for the note script that finds notes by it */
.plantuml-browser svg .k-card.k-card { fill: var(--dg-note) !important; stroke: var(--dg-card-edge) !important; }
.plantuml-browser svg .k-card.k-card-event { fill: var(--dg-event) !important; stroke: var(--dg-event-card-edge) !important; }
.plantuml-browser svg .k-fold.k-fold { visibility: hidden; }
/* step bars as light bands with dark ink; a table drawn in one sits on its own ground, with its header row shaded */
.plantuml-browser svg .k-step.k-step { fill: var(--dg-step) !important; stroke: var(--dg-step-edge) !important; }
.plantuml-browser svg .k-step-text.k-step-text { fill: var(--dg-step-ink) !important; }
.plantuml-browser svg .k-step-rule.k-step-rule { stroke: var(--kron-hairline) !important; }
.plantuml-browser svg .k-step-table.k-step-table { fill: var(--dg-step-table) !important; stroke: none !important; }
.plantuml-browser svg .k-step-table-head.k-step-table-head { fill: var(--kron-strip) !important; stroke: none !important; }
.plantuml-browser svg .k-step-table-head-text.k-step-table-head-text { fill: var(--kron-ink-soft) !important; }
'''
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
