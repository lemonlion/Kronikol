# S13b's tests: the core class ReportSheetColoursTests and the Playwright class DarkThemeColoursTests (both new, copied
# from beside this script), and the sparkline pins moved to the tokens' light values (HistoryHtmlTests reads the names
# the page writes; HistorySparklineTests the colours the browser paints). Checks every anchor in memory and writes
# nothing unless all hold; keeps BOMs and line endings; refuses a second run.
# usage: python tests-s13b.py <root>
import os, shutil, sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))
NEW = {
    'ReportSheetColoursTests.cs': 'tests/Kronikol.Tests/Reports/ReportSheetColoursTests.cs',
    'DarkThemeColoursTests.cs': 'tests/Kronikol.Tests.EndToEnd/DarkThemeColoursTests.cs',
}
EDITS = {
    'tests/Kronikol.Tests/History/HistoryHtmlTests.cs': [
        ('        Assert.Contains("#bf0000 75%,#bf0000 100%", pay); // the current failure is the rightmost stop\n',
         '        Assert.Contains("var(--sf) 75%,var(--sf) 100%", pay); // the current failure is the rightmost stop\n', 1),
        ('        Assert.Contains("#dcdcdc", head);\n',
         '        Assert.Contains("var(--sn)", head); // the not-a-test colour\'s name\n', 1),
    ],
    'tests/Kronikol.Tests.EndToEnd/GradualZoomTests.cs': [
        ('        var boxShadow = await container.EvaluateAsync<string>(\n'
         '            "e => window.getComputedStyle(e).boxShadow");\n'
         '        // Should contain a blue-ish rgba value in the box-shadow\n'
         '        Assert.Matches(new Regex(@"rgba?\\(59,\\s*130,\\s*246"), boxShadow);\n',
         '        var glow = await container.EvaluateAsync<string[]>(\n'
         '            "e => { const p = document.createElement(\'div\'); p.style.color = \'color-mix(in srgb, var(--kron-accent) 50%, transparent)\'; "\n'
         '            + "document.body.appendChild(p); const ring = getComputedStyle(p).color; p.remove(); return [getComputedStyle(e).boxShadow, ring]; }");\n'
         '        // the theme\'s accent at half strength, blue in the default theme (5.0.0, plans/OPTION_C_PLAN.md S13b: 4.x wrote\n'
         '        // rgba(59, 130, 246, 0.5) in every theme)\n'
         '        Assert.StartsWith(glow[1], glow[0]);\n', 1),
    ],
    'tests/Kronikol.Tests.EndToEnd/HistorySparklineTests.cs': [
        ('"rgb(191, 0, 0)"', '"rgb(179, 55, 46)"', 2),
        ('"rgb(34, 139, 34)"', '"rgb(63, 154, 98)"', 2),
        ('        // One hard-stop pair in the not-a-test grey, six passes, and nothing in the bypassed yellow.\n'
         '        Assert.Equal(2, CountOf(painted, "rgb(220, 220, 220)"));\n',
         '        // One hard-stop pair in the not-a-test grey, six passes, and nothing in the colour a bypassed run takes\n'
         '        // (the tokens\' light values: --kron-hairline-soft, --kron-pass-ring, --kron-warn).\n'
         '        Assert.Equal(2, CountOf(painted, "rgb(226, 231, 238)"));\n', 1),
        ('        Assert.Equal(0, CountOf(painted, "rgb(184, 160, 0)"));\n',
         '        Assert.Equal(0, CountOf(painted, "rgb(138, 90, 23)"));\n', 1),
    ],
}


def read(path):
    raw = open(ROOT + path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw[3:].decode('utf-8') if bom else raw.decode('utf-8')
    crlf = '\r\n' in text
    return text.replace('\r\n', '\n'), bom, crlf


for source, target in NEW.items():
    assert os.path.isfile(os.path.join(HERE, source)), ('missing beside this script', source)
    assert not os.path.exists(ROOT + target), ('already there: the S13b tests are in already', target)
    assert os.path.isdir(os.path.dirname(ROOT + target)), ('no such folder: is this a worktree root?', target)

results = {}
for path, edits in EDITS.items():
    text, bom, crlf = read(path)
    for old, new, count in edits:
        n = text.count(old)
        assert n == count, (path, n, count, old[:70])
        text = text.replace(old, new)
    results[path] = (text, bom, crlf)

for path, (text, bom, crlf) in results.items():
    if crlf:
        text = text.replace('\n', '\r\n')
    open(ROOT + path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
for source, target in NEW.items():
    # a new file takes the line endings of the files beside it
    _, _, crlf = read(next(iter(EDITS)) if 'EndToEnd' not in target else 'tests/Kronikol.Tests.EndToEnd/HistorySparklineTests.cs')
    text = open(os.path.join(HERE, source), encoding='utf-8', newline='').read().replace('\r\n', '\n')
    with open(ROOT + target, 'w', encoding='utf-8', newline='') as f:
        f.write(text.replace('\n', '\r\n') if crlf else text)
print('ok: ' + ', '.join(sorted(list(results) + list(NEW.values()))))
