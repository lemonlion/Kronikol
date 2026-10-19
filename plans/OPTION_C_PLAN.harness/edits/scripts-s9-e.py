# S9c (the page), after scripts-s9-d.py: every note button says what a click does (D42 in the structure inventory;
# the S9 row's "tooltips kept"). The preview gave the buttons their round 9 titles by their glyph text, which round 17's
# drawn triangles no longer have (plan rule 7 names the loss), so each title is set by name when its button is made.
# The + of a long note opens it to the line limit (onExpand), not in full as round 9's wording said, so it says so.
# Also renames the preview's kTriangle and _kdy, whose "k" means nothing in the product. usage: python scripts-s9-e.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/collapsible-notes-script.js'
s = open(P, encoding='utf-8', newline='').read()


def sub(text, old, new, count=1):
    assert text.count(old) == count, (old[:70], text.count(old))
    return text.replace(old, new)


s = sub(s, 'kTriangle(', 'drawnTriangle(', 5)
s = sub(s, '_kdy', '_followDy', 2)
s = sub(s, """        function packTopRight(dy) {""",
        """        // Every button says what a click does (5.0.0, plans/OPTION_C_PLAN.md S9, D42): a drawn triangle has no glyph
        // to go by, so each button is given its title by name when it is made. Returns the button's rect.
        function giveTitle(rect, text) {
            var t = document.createElementNS(SVGNS, 'title');
            t.textContent = text;
            rect.appendChild(t);
            return rect;
        }
        function packTopRight(dy) {""")
s = sub(s, "ga.appendChild(bgA);", "ga.appendChild(giveTitle(bgA, 'Cut this box back to the line limit'));")
s = sub(s, "gc.appendChild(bgC);", "gc.appendChild(giveTitle(bgC, 'Fold this box to one line'));")
s = sub(s, "gp.appendChild(bgP);",
        "gp.appendChild(giveTitle(bgP, longNote ? 'Open this box to the line limit' : 'Open this box in full'));")
s = sub(s, "ge.appendChild(bgE);", "ge.appendChild(giveTitle(bgE, 'Show more of this box'));")
s = sub(s, "gcp.appendChild(bgCP);", "gcp.appendChild(giveTitle(bgCP, 'Fold this box to one line'));")
s = sub(s, "gbc.appendChild(bgBC);", "gbc.appendChild(giveTitle(bgBC, 'Cut this box back to the line limit'));")
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
