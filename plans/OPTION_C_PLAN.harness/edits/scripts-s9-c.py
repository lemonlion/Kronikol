# S9b (the page): the collapsed preview's braces (P1), and getNotePreview exposed to the tests.
# usage: python scripts-s9-c.py
ROOT = 'C:/Code/Kronikol-v5/'
P = ROOT + 'src/Kronikol/Reports/collapsible-notes-script.js'

s = open(P, encoding='utf-8', newline='').read()
assert '\r\n' not in s


def once(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


# P1: a collapsed note's one-line preview drops a JSON body's opening brace, and its closing one when the whole body
# fits (a cut body ends in "..." and has no closing brace left to drop); "{}" stays as it is, an array as it was
once("""        var raw = nonGray.join(' ').trim();
        if (!raw) return '';
        if (raw.length <= 60) return raw;""",
     r"""        var raw = nonGray.join(' ').trim();
        if (!raw) return '';
        // A JSON body's braces say nothing on one line (5.0.0): the opening one goes, and the closing one too when the
        // whole body fits. "{}" stays as it is.
        if (raw.charAt(0) === '{') {
            var body = raw.replace(/^\{\s*/, '');
            var whole = body.replace(/\s*\}$/, '');
            if (whole && whole !== body && whole.length <= 60) return whole;
            if (whole) raw = body;
        }
        if (raw.length <= 60) return raw;""")

once("""    window._parseNoteBlocks = parseNoteBlocks;
""", """    window._parseNoteBlocks = parseNoteBlocks;
    window._getNotePreview = getNotePreview;
""")

# P9 is not ported: F6 did not reproduce at v5 (ExactNoteMapTests pass before any change), see the plan log

open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
