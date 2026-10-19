# S8: the scenario's diagram bars in stylesheets.css (D29, D34). The tabs bar in every report (a report drawn when it
# was written still has the Activity tab); the options bar, the settings, the gears and the payload controls are in
# collapsible-notes-styles.css, which only a report drawn in the page carries. The 4.x toolbar rules go: the spacer,
# the phone's "Diagram Settings" button (report-init-script.js no longer makes it), the "lines" label. The preview's
# c-skin.css 68-75, 144-148, 156-168 and 1238-1239 in their final state. Plain text edits: the blocks carry comments.
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/stylesheets.css'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new):
    global s
    assert s.count(old) == 1, (s.count(old), old[:90])
    s = s.replace(old, new)

def cut(start, end, new):
    """replace from `start` up to (not including) `end`, once"""
    global s
    assert s.count(start) == 1, start[:90]
    i = s.index(start)
    j = s.index(end, i)
    s = s[:i] + new + s[j:]

# the desktop block of 4.x's toolbar, from its comment to the end of its inline rules
cut('''        /* The scenario diagram toolbar, in every report (these rules lived in the internal-flow sheet,''',
    '''        .diagram-toggle[data-layout="inline"] + * {
            clear: right;
        }
''', '''        /* A scenario's diagram bars (5.0.0, D29): the views' tabs on a bar of their own, in every report, which sits on the
           section's title line where it fits (diagram-toggle-layout-script.js sets data-layout="inline" and the margin that
           centres it on the title) and under it where it does not; then, where the page draws the diagrams, the scenario's
           settings (collapsible-notes-styles.css). A bar wraps whole controls onto a new row. */
        .diagram-toggle {
            display: flex; flex-wrap: wrap; align-items: baseline; row-gap: 6px; width: 100%; box-sizing: border-box;
            font-family: var(--kron-font);
        }
        .diagram-toggle button, .diagram-toggle select { user-select: none; }
        .diagram-toggle-tabs { margin: 0; padding: 0 16px; }
        .diagram-toggle-btn {
            margin: 0; padding: 6px 11px; border: none; border-radius: 5px; background: none; cursor: pointer;
            font-size: 13px; line-height: 1.25; font-weight: 400; font-family: var(--kron-font); color: var(--kron-ink-quiet);
            transition: background .12s, color .12s;
        }
        .diagram-toggle-btn:hover { background: var(--kron-hover); color: var(--kron-ink); }
        .diagram-toggle-btn:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
        .diagram-toggle-active, .diagram-toggle-active:hover {
            color: var(--kron-accent); font-weight: 600; border-radius: 5px 5px 0 0; box-shadow: inset 0 -2px 0 var(--kron-accent);
        }
        .diagram-toggle-active { background: none; }
        .diagram-toggle-active:hover { background: var(--kron-hover); }
        .diagram-toggle[data-layout="inline"] { float: right; width: auto; margin: 0 1em 0 0; padding: 0; position: relative; z-index: 1; }
''')
sub('''        .diagram-toggle[data-layout="inline"] + * {
            clear: right;
        }
''', '''        .diagram-toggle[data-layout="inline"] + * { clear: right; }
''')

# 4.x's phone button for the scenario's controls, and its desktop rule that hid it
cut('''        /* #4 Per-scenario diagram controls — shown on desktop, toggle hidden */''',
    '''        /* #10 Back-to-top FAB */''', '')
cut('''            /* #4 Per-scenario diagram controls collapse on mobile */''',
    '''            /* #9 Step duration stays inline */''', '')

# the phone: the tabs share the row, and the controls' phone sheet is the notes sheet's
sub('''            .details-radio {
                flex-wrap: wrap;
            }

            .headers-radio {
                margin-left: 0;
            }

            .diagram-toggle {
                flex-wrap: wrap;
                gap: 0.3em;
            }

            .diagram-toggle-spacer {
                display: none;
            }
''', '''            /* the views' tabs share the row in equal parts; a label that wraps centres against its neighbours */
            .diagram-toggle { gap: 4px; }
            .diagram-toggle-tabs { align-items: center; }
            .diagram-toggle-tabs .diagram-toggle-btn { flex: 1 1 0; min-width: 0; text-align: center; }
''')
sub('''            /* #3 "lines" label wrapping fix */
            .truncate-lines-label {
                display: none;
            }
''', '''            .diagram-toggle-btn { min-height: 34px; }
''')
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
