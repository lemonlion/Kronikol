# S7: the tests that use a filter row's control at a desktop width open the rows first (More filters folds them away,
# D15) with PlaywrightTestBase.OpenFilterRowsAsync. Run from the v5 worktree root.
import re

E = 'tests/Kronikol.Tests.EndToEnd/'
OPEN = 'await OpenFilterRowsAsync();'

def lines_of(path):
    return open(path, encoding='utf-8', newline='').read().split('\n')

def save(path, lines):
    open(path, 'w', encoding='utf-8', newline='').write('\n'.join(lines))

def before(path, numbers):
    """insert the call before each 1-based line, indented as that line, the last first so the numbers hold"""
    lines = lines_of(path)
    for n in sorted(numbers, reverse=True):
        target = lines[n - 1]
        indent = target[:len(target) - len(target.lstrip())]
        lines.insert(n - 1, indent + OPEN)
    save(path, lines)

def after_each(path, pattern):
    """insert the call after every line matching the pattern, indented as that line"""
    out = []
    for line in lines_of(path):
        out.append(line)
        if re.search(pattern, line):
            out.append(line[:len(line) - len(line.lstrip())] + OPEN)
    save(path, out)

before(E + 'ScenarioInteractionTests.cs', [328, 350, 373])
before(E + 'WarmUpMarkTests.cs', [156, 171])
before(E + 'StableIdDeepLinkTests.cs', [157])
before(E + 'ReportToolbarTests.cs', [309])
before(E + 'RuleRenderingTests.cs', [202])
before(E + 'ToolbarStylingTests.cs', [80, 116, 130])

after_each(E + 'FilterModeDefaultsTests.cs', r'await Page\.Locator\("details\.feature"\)\.First\.WaitForAsync\(\);')
after_each(E + 'UrlHashFilterHighlightTests.cs', r'^\s+await Page\.(GotoAsync\(.*\)|ReloadAsync\(\));\s*$')

# the pipeline's tests open the rows with the page
p = E + 'FilterPipelineTests.cs'
s = open(p, encoding='utf-8', newline='').read()
a = '''        await Page.GotoAsync(Generate(fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync();
    }
'''
assert s.count(a) == 1
s = s.replace(a, a.replace('WaitForAsync();\n    }', 'WaitForAsync();\n        ' + OPEN + '\n    }'))
open(p, 'w', encoding='utf-8', newline='').write(s)

# the filter box held narrow: its rows open for the chip that must break inside it
p = E + 'ReportSectionWidthTests.cs'
s = open(p, encoding='utf-8', newline='').read()
a = '''        if (filteringBox > 0)
            await HoldFilteringBox(filteringBox);
'''
assert s.count(a) == 1
s = s.replace(a, '''        if (filteringBox > 0)
        {
            await HoldFilteringBox(filteringBox);
            ''' + OPEN + '''
        }
''')
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
