# S4, after the first parity measure: the features table's figures in the page's font (the preview's type pass,
# c-round7.css 25-31), and the Features tools on a phone (the desktop's order on one line while it fits; at 480 px and
# under two rows, the spacer as the line break). Applied to stylesheets.css after edits-s4-a.py.
P = 'src/Kronikol/Reports/stylesheets.css'
s = open(P, 'rb').read().decode('utf-8')

def sub(old, new):
    global s
    assert s.count(old) == 1, old[:80]
    s = s.replace(old, new)

sub("        .feature-summary-table td:not(:first-child) { font-family: var(--kron-mono); font-size: 12px; }",
    "        /* numbers are text: the page's font with tabular figures, so the columns line up without a second typeface */\n"
    "        .feature-summary-table td:not(:first-child) { font-size: 12px; }")
sub('''            /* the Features tools on a phone: the views and the sort on one line, the fold groups and the gear on the next,
               and the display settings behind the gear */
            .features-tools-line.toolbar-left { gap: 8px; padding: 10px 14px; }
            .view-switch { order: 1; }
            .sort-group { order: 1; margin-left: auto; }
            .tools-spacer { display: none; }
            .fold-group { order: 4; margin-left: 0; }
            .tools-gear {
                display: inline-block; order: 5; margin-left: auto; padding: 6px 9px; cursor: pointer;''', '''            /* the Features tools on a phone: the desktop's order while it fits on one line, the sort and the gear at its
               right end, and the display settings behind the gear */
            .features-tools-line.toolbar-left { gap: 8px; padding: 10px 14px; }
            .tools-spacer { display: none; }
            .sort-group { margin-left: auto; }
            .tools-gear {
                display: inline-block; padding: 6px 9px; cursor: pointer;''')
anchor = '''            /* an export button keeps the card's type and padding (5.0.0) and only grows to the touch height */
            .export-btn { min-height: 36px; }'''
sub(anchor, anchor + '''

            /* the Features tools in two rows: the views and the sort, then the fold groups and the gear; the spacer is
               the line break, its margin taking back the row gap it would add (5.0.0) */
            .view-switch { order: 1; }
            .sort-group { order: 2; }
            .tools-spacer { display: block; order: 3; flex: 0 0 100%; height: 0; margin-top: -8px; }
            .features-tools-line .fold-group { order: 4; margin-left: 0; }
            .features-tools-line .fold-group + .fold-group { margin-left: 14px; }
            .tools-gear { order: 5; margin-left: auto; }''')
open(P, 'wb').write(s.encode('utf-8'))
print('ok')
