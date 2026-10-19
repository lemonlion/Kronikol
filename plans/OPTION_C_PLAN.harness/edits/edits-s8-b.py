# S8: the diagram section's card and title (structure inventory row 14). The preview's c-skin.css 389-390, 621,
# 678-679, 1603 and 1621-1622 in their final state: a card on the card ink with the hairline frame, its title set like
# the Steps title, its corners following the frame's inner curve and squared at the bottom while open.
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/stylesheets.css'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new):
    global s
    assert s.count(old) == 1, (s.count(old), old[:90])
    s = s.replace(old, new)

sub('''        .example-diagrams {
            border-radius: 1em;
            border: 1px solid;
            border-color: rgb(224, 224, 224);
        }
''', '''        /* A scenario's diagrams (5.0.0): a card in the scenario, its title set like the Steps title */
        .example-diagrams { margin-top: 10px; background: var(--kron-card); border: 1px solid var(--kron-hairline); border-radius: 10px; }
''')
sub('''        .example-diagrams > summary {
            background-color: white;
            padding: 1em;
            border-radius: 1em;
        }
''', '''        .example-diagrams > summary {
            padding: 9px 14px; background: var(--kron-card); border-radius: 9px;
            font-size: 13px; font-weight: 600; color: var(--kron-ink-soft);
        }
        .example-diagrams[open] > summary { border-radius: 9px 9px 0 0; }
''')
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
