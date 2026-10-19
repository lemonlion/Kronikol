# S9c: the note buttons' triangles are drawn (polygons, item 13), so the facts that found a ▲ or ▼ button by its text
# find it by its name (B6): ▲ was the expanded long note's truncate buttons (top and bottom), ▼ the bottom expand
# button. The truncated note's new ▲ in its bottom pair has a name of its own, collapse-bottom, so no fact that meant
# the truncate ▲ finds it. Also F15: DiagramNotePlaywrightBase's wait polls every 200 ms, as every wait must.
# usage: python tests-s9c-a.py
import re

ROOT = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/'
UP = "/^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')"
DOWN = "i.getAttribute('data-note-btn') === 'expand-bottom'"
up_rx = re.compile(r"Array\.from\(i\.querySelectorAll\('text'\)\)\.some\(t => t\.textContent\.includes\('(?:▲|\\u25B2)'\)\)")
down_rx = re.compile(r"Array\.from\(i\.querySelectorAll\('text'\)\)\.some\(t => t\.textContent\.includes\('(?:▼|\\u25BC)'\)\)")

FILES = ['DiagramNoteLongNoteTests.cs', 'NoteButtonsAfterHeaderHideTests.cs', 'NoteExpandArrowHeaderHiddenTests.cs',
         'NoteYamlToggleTests.cs', 'YamlNoteCopyTextTests.cs', 'DiagramNotePlaywrightBase.cs']
total = 0
for f in FILES:
    p = ROOT + f
    s = open(p, encoding='utf-8', newline='').read()
    s, n1 = up_rx.subn(UP, s)
    s, n2 = down_rx.subn(DOWN, s)
    total += n1 + n2
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print(f, n1, n2)


def once(path, old, new):
    p = ROOT + path
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    if crlf:
        old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
    assert s.count(old) == 1, (path, old[:60], s.count(old))
    open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))


once('DiagramNotePlaywrightBase.cs',
     """            "() => { var icons = document.querySelectorAll('.note-toggle-icon'); " +
            "return Array.from(icons).some(i => { " +
            "var texts = i.querySelectorAll('text'); " +
            "return Array.from(texts).some(t => t.textContent.includes('▼')) && i.style.opacity !== '0'; }); }");""",
     """            "() => { var icons = document.querySelectorAll('.note-toggle-icon'); " +
            "return Array.from(icons).some(i => i.getAttribute('data-note-btn') === 'expand-bottom' && i.style.opacity !== '0'); }",
            null, new() { PollingInterval = 200 });""")
once('ContinuationNoteInteractivityTests.cs',
     """                        var txt = icons[i].querySelector('text');
                        if (txt && (txt.textContent.indexOf('▼') >= 0 || txt.textContent.indexOf('▲') >= 0)) hasExpand = true;""",
     """                        // the triangles are drawn, and named (5.0.0)
                        if (/^(expand-bottom|truncate|truncate-bottom|collapse-bottom)$/.test(icons[i].getAttribute('data-note-btn') || '')) hasExpand = true;""")
print('replaced', total, 'glyph lookups')
