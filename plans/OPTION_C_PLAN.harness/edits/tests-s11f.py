# -*- coding: utf-8 -*-
# S11f's tests: copies the two new classes into a root's test projects (core HistoryRingsMarkupTests, Playwright
# HistoryRingsTests), and moves the pins of the 4.x charts that the change breaks, each with an exact anchor:
#   tests/Kronikol.Tests/History/HistoryHtmlTests.cs      the degraded run's two tooltips (svg <title> to a title
#                                                          attribute) and the four bars of the section fact (rings now)
#   tests/Kronikol.Tests.EndToEnd/HistorySectionTests.cs  the two svg charts and their seven rects (charts and rings now),
#                                                          and the degraded run's tooltips read from the rings and bars
# Run it before scripts-s11f.py to see the new and moved facts fail on the unchanged code (the red step), then apply the
# product edits. usage: python tests-s11f.py <root>
# Every anchor is checked to occur exactly once, and the new files to be absent, before anything is written.
import os
import sys

if len(sys.argv) != 2:
    sys.exit('usage: python tests-s11f.py <root>')
ROOT = sys.argv[1].replace(chr(92), '/').rstrip('/') + '/'
HERE = os.path.dirname(os.path.abspath(__file__)).replace(chr(92), '/') + '/'

NEW_FILES = {
    'tests/HistoryRingsMarkupTests.cs': 'tests/Kronikol.Tests/History/HistoryRingsMarkupTests.cs',
    'tests/HistoryRingsTests.cs': 'tests/Kronikol.Tests.EndToEnd/HistoryRingsTests.cs',
}

CORE = 'tests/Kronikol.Tests/History/HistoryHtmlTests.cs'
E2E = 'tests/Kronikol.Tests.EndToEnd/HistorySectionTests.cs'

EDITS = {
    CORE: [
        (r'''        // Both charts' bars for the run say so; no healthy run's does.
        Assert.Equal(2, Regex.Matches(section, "<title>test:9:1 [^<]* · degraded 3\\.0×</title>").Count);
''',
         r'''        // Both charts say so for the run, in the tooltips of its ring and its bar (5.0.0, plans/OPTION_C_PLAN.md S11, where
        // 4.x had a title element in each chart's svg); no healthy run's does.
        Assert.Equal(2, Regex.Matches(section, "title=\"test:9:1 [^\"]* · degraded 3\\.0×\"").Count);
'''),
        (r'''        Assert.Equal(4, Regex.Matches(section.Value, "<rect class=\"history-bar history-bar-(pass|fail)").Count); // three prior runs and this one
''',
         r'''        // a ring per run since 5.0.0, where 4.x drew a bar in a stretched svg (plans/OPTION_C_PLAN.md S11)
        Assert.Equal(4, Regex.Matches(section.Value, "<div class=\"history-ring[ \"]").Count); // three prior runs and this one
'''),
    ],
    E2E: [
        (r'''        await Expect(section.Locator("svg.history-chart-svg")).ToHaveCountAsync(2);
        await Expect(section.Locator("svg.history-chart-svg").First.Locator("rect")).ToHaveCountAsync(7);
''',
         r'''        // the two charts: a ring per run and a bar per run since 5.0.0, where 4.x drew bars in two stretched svgs (S11)
        await Expect(section.Locator(".history-chart")).ToHaveCountAsync(2);
        await Expect(section.Locator(".history-rings > .history-ring")).ToHaveCountAsync(7);
'''),
        (r'''        // 3.24.0 (#83): the fourth earlier run took three times as long in every scenario. Its bar in each
        // chart names it; the run being reported is healthy, so nothing is said above the charts.
''',
         r'''        // 3.24.0 (#83): the fourth earlier run took three times as long in every scenario. Its ring and its bar
        // name it in their tooltips (since 5.0.0); the run being reported is healthy, so nothing is said above the charts.
'''),
        (r'''        var titles = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('#history-section .history-chart rect.history-bar title')).map(t => t.textContent)");
''',
         r'''        var titles = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('#history-section .history-chart [role=listitem]')).map(t => t.getAttribute('title'))");
'''),
    ],
}


def read(path):
    with open(path, encoding='utf-8', newline='') as f:
        return f.read()


# Pass 1: the new files are not there yet, their sources are, and every anchor occurs once.
for src, dst in NEW_FILES.items():
    if not os.path.isfile(HERE + src):
        sys.exit(f'missing {HERE + src}; nothing written')
    if os.path.exists(ROOT + dst):
        sys.exit(f'{dst} is already there; S11f tests look applied, nothing written')
    if not os.path.isdir(os.path.dirname(ROOT + dst)):
        sys.exit(f'no folder for {dst} under {ROOT}; nothing written')
texts = {}
for rel, pairs in EDITS.items():
    s = read(ROOT + rel)
    for old, _ in pairs:
        n = s.count(old)
        if n != 1:
            sys.exit(f'{rel}: anchor found {n} times, nothing written: {old.splitlines()[0].strip()!r}')
    for old, new in pairs:
        s = s.replace(old, new)
    texts[rel] = s

# What was meant to land is there, byte for byte (the regex escapes included).
checks = [
    (CORE, 'Regex.Matches(section, "title=\\"test:9:1 [^\\"]* · degraded 3\\\\.0×\\"").Count', 1),
    (CORE, 'Regex.Matches(section.Value, "<div class=\\"history-ring[ \\"]").Count', 1),
    (CORE, '<rect class=', 0),
    (E2E, 'section.Locator(".history-rings > .history-ring")', 1),
    (E2E, "querySelectorAll('#history-section .history-chart [role=listitem]')).map(t => t.getAttribute('title'))", 1),
    (E2E, 'svg.history-chart-svg', 0),
]
for rel, needle, want in checks:
    got = texts[rel].count(needle)
    if got != want:
        sys.exit(f'{rel}: {needle!r} occurs {got} times after the edit, {want} expected; nothing written')

# Pass 2: write.
for src, dst in NEW_FILES.items():
    with open(HERE + src, 'rb') as f:
        data = f.read()
    if b'\r' in data:
        sys.exit(f'{src} has carriage returns; the test projects are LF')
    with open(ROOT + dst, 'wb') as f:
        f.write(data)
    print('added', dst)
for rel, s in texts.items():
    with open(ROOT + rel, 'w', encoding='utf-8', newline='') as f:
        f.write(s)
    print('edited', rel)
print('S11f tests applied to', ROOT)
