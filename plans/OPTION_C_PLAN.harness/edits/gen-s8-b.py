# S8 part b: the format and width controls name the payloads (D37), and the format select takes its heading (R17.3).
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/ReportGenerator.cs'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:80])
    s = s.replace(old, new)

sub('''        // carry its meaning instead. It takes no <optgroup> heading, unlike the
        // appearance selects: its options are the names of formats and say what
        // they do, and Chromium indents grouped options, which measured 15px of
        // closed-state width per select on a toolbar that has none to spare.
''', '''        // carry its meaning instead. Since 5.0.0 it takes an <optgroup> heading,
        // "Payload format", like the width and line-count selects: the owner's
        // choice, over the 15px of closed-state width Chromium adds to a select
        // whose options are grouped.
''')
for handler in ('_setNoteFormat', '_setScenarioNoteFormat'):
    sub(f'''aria-label=\\"Note payload format\\" title=\\"Note payload format\\" onchange=\\"window.{handler}(this)\\">{{noteFormatOptions}}</select>''',
        f'''aria-label=\\"Payload format\\" title=\\"Payload format\\" onchange=\\"window.{handler}(this)\\"><optgroup label=\\"Payload format\\">{{noteFormatOptions}}</optgroup></select>''')
sub('''const string noteWidthTitle = "Note width. Wrap: notes wrap at the report's note width. Wide: notes widen to fill the diagram.";''',
    '''const string noteWidthTitle = "Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.";''')
sub('''BuildNoteAppearanceSelect("note-width-select", "Note width", noteWidthTitle''',
    '''BuildNoteAppearanceSelect("note-width-select", "Payload width", noteWidthTitle''', count=2)

open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
