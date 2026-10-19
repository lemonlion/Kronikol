# S9c (the page): createNoteButtons as the preview's round 17 left it (P15: B1 to B8, items R17.9, R17.13 to R17.15
# and 24, with P10's one-line collapsed payload), re-derived on the product's function, which equals the preview's base
# copy but for P10's three lines. Not ported: keepPlace's wait on the fragment it was given, which a re-render
# replaces, so a diagram drawn in fragments never scrolled (F4: it waits on the diagram that owns the fragment); the
# unused slot parameters (F8); the copy of the "Fragment note indexing" comment (F13). usage: python scripts-s9-d.py
ROOT = 'C:/Code/Kronikol-v5/'
P = ROOT + 'src/Kronikol/Reports/collapsible-notes-script.js'
MOCK = ROOT + 'plans/OPTION_C_PLAN.harness/mock/'

s = open(P, encoding='utf-8', newline='').read()
assert '\r\n' not in s
mock = open(MOCK + 'c-round17-buttons.js', encoding='utf-8', newline='').read().replace(chr(13) + chr(10), chr(10))
fn = mock[:mock.index('    // ── Fragment note indexing')].rstrip() + '\n'
assert fn.startswith('function createNoteButtons(')
fn = '    ' + fn


def sub(text, old, new, count=1):
    assert text.count(old) == count, (old[:70], text.count(old))
    return text.replace(old, new)


fn = sub(fn, "        var longNote = forceIsLong || isLongNote(contentLines, container._truncateLines, hdrHidden); var kronWhole = state === 'collapsed' && kronCollapsedIsWhole(contentLines, hdrHidden);\n",
         "        var longNote = forceIsLong || isLongNote(contentLines, container._truncateLines, hdrHidden);\n"
         "        // a collapsed note that already shows all of itself gets no + and no bottom ▼, which would open nothing\n"
         "        var shownWhole = state === 'collapsed' && collapsedIsWhole(contentLines, hdrHidden);\n")
fn = sub(fn, "!kronWhole", "!shownWhole", 2)
fn = sub(fn, """        // Round 17 (owner, item 9): a note made shorter from its lower part (its bottom buttons, or a click low in a
        // tall note) pulled everything below it up past the window, so the reader landed somewhere else, often in
        // another scenario. Once the diagram is drawn again, the note's new bottom is brought to where the click was.
        function keepPlace(fn, ev) {
            var y = ev && typeof ev.clientY === 'number' ? ev.clientY : null;
            var rects = Array.prototype.slice.call(container.querySelectorAll('svg .note-hover-rect'));""",
         """        // Keep the reader's place (5.0.0, plans/OPTION_C_PLAN.md R17.9): a note made shorter from its lower part (its
        // bottom buttons, or a click low in a tall note) pulled everything below it up past the window, so the reader
        // landed somewhere else, often in another scenario. Once the diagram is drawn again, the note's new bottom is
        // brought to where the click was. A diagram drawn in fragments is watched through the diagram that owns them,
        // as drawing it again replaces every fragment and the owner carries the rendering flag.
        var host = container.classList && container.classList.contains('puml-fragment') && container.parentElement
            ? container.parentElement : container;
        function keepPlace(fn, ev) {
            var y = ev && typeof ev.clientY === 'number' ? ev.clientY : null;
            var rects = Array.prototype.slice.call(host.querySelectorAll('svg .note-hover-rect'));""")
fn = sub(fn, """            var oldSvg = container.querySelector('svg');""", """            var oldSvg = host.querySelector('svg');""")
fn = sub(fn, """                var now = container.querySelector('svg');
                var ready = now && now !== oldSvg && !container._noteRendering && now.querySelector('.note-hover-rect');""",
         """                var now = host.querySelector('svg');
                var ready = now && now !== oldSvg && !host._noteRendering && host.querySelector('svg .note-hover-rect');""")
fn = sub(fn, """                var r = container.querySelectorAll('svg .note-hover-rect')[idx];""",
         """                var r = host.querySelectorAll('svg .note-hover-rect')[idx];""")
fn = sub(fn, """        // Round 17 (owner): the top-right buttons sit side by side from the note's right edge, with no gap where a
        // hidden button (an ineligible format or width button) would be (item 14); each is created at the first
        // place and moved by a transform, which also carries a tall note's scroll offset. The triangles are drawn,
        // not set as text, exactly 2px clear of their button's top and bottom edges in every browser (item 13).""",
         """        // The top-right buttons sit side by side from the note's right edge, with no gap where a hidden button (an
        // ineligible format or width button) would be (5.0.0, R17.14); each is made at the first place and moved by a
        // transform, which also carries a tall note's scroll offset. The triangles are drawn, not set as text, 2 px
        // clear of their button's top and bottom edges in every browser (R17.13).""")
fn = sub(fn, """        // One top-right glyph button, at `slot` places in from the right edge of the note.
        // Created hidden when `lazy` — eligibility is decided on the note's first hover, never in a
        // per-render sweep.
        function glyphButton(slot, glyph, title, onClick, btnName, lazy) {""",
         """        // One top-right glyph button, made at the first place from the note's right edge (packTopRight moves it).
        // Created hidden when `lazy`: eligibility is decided on the note's first hover, never in a per-render sweep.
        function glyphButton(glyph, title, onClick, btnName, lazy) {""")
fn = sub(fn, """                var slot = (state === 'expanded' && longNote) ? 3 : 2;
""", "")
fn = sub(fn, """                var nextSlot = (state === 'expanded' && longNote) ? 4 : 3;
                widthBtn = glyphButton(nextSlot,
""", """                widthBtn = glyphButton(
""")
fn = sub(fn, """            // Round 17 (owner, item 15): a truncated note can go either way, so its bottom holds ▲ (collapse) and
            // ▼ (expand) side by side, each two thirds of the single button's width.""",
         """            // A truncated note can go either way, so its bottom holds ▲ (collapse) and ▼ (expand) side by side,
            // each two thirds of the single button's width (5.0.0, R17.15).""")
# "the owner container" is the product's own word for the diagram that owns a note's state; "owner" alone was the
# preview's for the person
left = [l for l in fn.split('\n') if 'Round 1' in l or 'kron' in l
        or ('owner' in l and 'owns them' not in l and 'the owner carries' not in l and 'owner container' not in l)]
assert not left, 'a preview word left in the port: ' + ' | '.join(l.strip() for l in left)

# P10's rule, from the preview's c-round16-notes.js
whole = """    // A collapsed payload already shows all of itself when it has one line to show (its headers hidden, or none)
    // and its one-line preview is that line in full ("1 row"): its + and bottom ▼ would open nothing (5.0.0).
    function collapsedIsWhole(contentLines, hdrHidden) {
        if (!contentLines) return false;
        var shown = [];
        for (var i = 0; i < contentLines.length; i++) {
            var t = contentLines[i].trim();
            if (NOTE_HEADER_TAG.test(t)) { if (!hdrHidden) return false; continue; }
            if (t !== '') shown.push(t);
        }
        if (shown.length > 1) return false;
        return !shown.length || getNotePreview(contentLines) === shown[0];
    }

"""
start = s.index('    function createNoteButtons(')
end = s.index('    // ── Fragment note indexing', start)
s = s[:start] + whole + fn + '\n' + s[end:]
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
