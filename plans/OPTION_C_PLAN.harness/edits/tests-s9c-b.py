# S9c (the core suite): the facts that pin createNoteButtons' text move to what the port writes, each saying why: the
# expand condition leaves out a collapsed note that shows all of itself (B1), the triangles are drawn (B5), the top-right
# buttons are made at the first place and packed (B3, B4), and the shortening clicks go through keepPlace (R17.9).
# usage: python tests-s9c-b.py
ROOT = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests/Reports/'


def edit(path, pairs):
    s = open(ROOT + path, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:70], s.count(old))
        s = s.replace(old, new)
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(s)


edit('DiagramContextMenuTests.cs', [
    ("""        // Expand button shown for collapsed or truncated
        Assert.Contains("state === 'collapsed' || state === 'truncated'", funcBody);""",
     """        // Expand button shown for collapsed or truncated; from 5.0.0 not for a collapsed note that already shows all
        // of itself, which it would open to nothing (plans/OPTION_C_PLAN.md S9, B1)
        Assert.Contains("(state === 'collapsed' && !shownWhole) || state === 'truncated'", funcBody);"""),
    (r"""        // Bottom expand button uses ▼ (large downward triangle)
        Assert.Contains("\\u25BC", funcBody);""",
     """        // Bottom expand button draws a downward triangle (from 5.0.0 drawn, not the ▼ glyph: S9, B5)
        Assert.Contains("ge.appendChild(drawnTriangle(ex, ey, expandW, expandH, false));", funcBody);"""),
    (r"""        Assert.Contains("\\u25B2", funcBody); // ▲ large""",
     """        Assert.Contains("gbc.appendChild(drawnTriangle(cx, cy, contractW, contractH, true));", funcBody); // drawn ▲ (B5)"""),
    ("""        // For expanded long notes: ▴ arrow appears to the left of −
        // The ▴ block uses offset: topSize * 2 + pad * 2
        Assert.Contains("bbox.x + bbox.width - topSize * 2 - pad * 2", funcBody);""",
     """        // For expanded long notes: ▴ arrow appears to the left of −. From 5.0.0 every top-right button is made at the
        // first place and packTopRight moves it, skipping hidden ones (S9, B3), so − is packed first and the ▴ next
        Assert.Contains("var ax = bbox.x + bbox.width - topSize - pad;", funcBody);
        Assert.True(funcBody.IndexOf("topRight.push(gc);") < funcBody.IndexOf("if (upTop) topRight.push(upTop);"));"""),
    ("""        // Find the block for the ▴ arrow (uses bgA click handler)
        Assert.Contains("onTruncate()", funcBody);""",
     """        // Find the block for the ▴ arrow (uses bgA click handler); from 5.0.0 through keepPlace (S9, R17.9)
        Assert.Contains("bgA.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onTruncate, ev); });", funcBody);"""),
    (r"""        var bottomBlock = funcBody.Substring(funcBody.IndexOf("\\u25B2"));
        Assert.Contains("onTruncate()", bottomBlock);""",
     """        var bottomBlock = funcBody.Substring(funcBody.IndexOf("'truncate-bottom'"));
        Assert.Contains("bgBC.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onTruncate, ev); });", bottomBlock);"""),
    ("""        // The − (minus) button calls onContract for full collapse
        Assert.Contains("onContract()", funcBody);""",
     """        // The − (minus) button calls onContract for full collapse, from 5.0.0 through keepPlace (S9, R17.9)
        Assert.Contains("bgC.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onContract, ev); });", funcBody);"""),
])
edit('NoteAppearanceScriptTests.cs', [
    ("""        // …and the tall-note repositioning sweep has to move it with the rest of the cluster.
        Assert.Contains("btn === 'format' || btn === 'width'", singleQuoted);""",
     """        // …and the tall-note repositioning has to move it with the rest of the cluster: from 5.0.0 every top-right
        // button is in topRight, which packTopRight moves (plans/OPTION_C_PLAN.md S9, B4).
        Assert.Contains("topRight.push(widthBtn);", singleQuoted);
        Assert.Contains("packTopRight(dy);", singleQuoted);"""),
])
print('ok')
