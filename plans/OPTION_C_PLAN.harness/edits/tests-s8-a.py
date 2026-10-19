# S8: the Playwright tests that use a scenario's payload controls open its settings first (they sit behind the gear
# of its diagrams, closed until it is pressed) with PlaywrightTestBase.OpenScenarioSettingsAsync. One call per test
# method and scope, before the first line that uses that scope's controls. Run from anywhere; --dry lists only.
import re, sys, glob

E = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/'
# not preceded by a word character or a hyphen: .diagram-toggle-btn, a view tab, is no payload control
CONTROLS = r'(?<![\w-])(details-radio-btn|toggle-btn|truncate-lines-select|note-format-select|note-width-select|diagram-options-bar)'
# a page-wide locator into a scenario's bar: .diagram-toggle, details.scenario or the options bar before the control
PAGE = re.compile(r'\b(Page|page|_page|lan\.Page)\.Locator\(\$?"(\.diagram-toggle|details\.scenario|\.diagram-options-bar|\.scenario-settings)[^"]*' + CONTROLS)
# a locator scoped to a scenario variable, its control named directly
SCOPED = re.compile(r'\b([a-z]\w*)\.Locator\(\$?"[^"]*\.' + CONTROLS)
NOT_SCOPES = {'page', 'popup', 'toolbar', 'bar', 'nav', 'tools', 'report', 'reportBar', 'lan'}
# locators a file returns from members of its own: a use of one opens the settings of the scenario it names
HELPERS = re.compile(r'\b(AssertionShowBtn|AssertionHideBtn)\((\w+)\)')
PROPERTIES = re.compile(r'(?<![\w.])(ScenarioNoteFormatSelect|ScenarioTruncateSelect)\b')
DEFINITION = re.compile(r'^\s*(private|public|protected|internal)\b[^=]*=>')
METHOD = re.compile(r'^\s*(public|private|protected|internal)\s+(static\s+)?(async\s+)?[\w<>\[\],? ]+\s+\w+\(.*\)\s*$')
DRY = '--dry' in sys.argv

def page_var(line):
    m = PAGE.search(line)
    return m.group(1) if m else None

total = 0
for path in sorted(glob.glob(E + '*.cs')):
    if path.endswith('PlaywrightTestBase.cs'):
        continue
    lines = open(path, encoding='utf-8', newline='').read().split('\n')
    out, done, prev = [], set(), ''
    for line in lines:
        if METHOD.match(line):
            done = set()
        scope = None
        # a member that returns a locator is no statement: its uses open the settings instead
        if DEFINITION.match(line) or prev.rstrip().endswith('=>'):
            out.append(line)
            prev = line
            continue
        prev = line
        pv = page_var(line)
        hm, pm = HELPERS.search(line), PROPERTIES.search(line)
        if pv and 'toolbar-right' not in line and 'report-nav' not in line:
            scope = ('page', pv)
        elif hm:
            scope = ('var', hm.group(2))
        elif pm:
            scope = ('page', '_page')
        else:
            m = SCOPED.search(line)
            if m and m.group(1) not in NOT_SCOPES and 'toolbar-right' not in line:
                scope = ('var', m.group(1))
        if scope and scope not in done and not line.lstrip().startswith('//'):
            done.add(scope)
            indent = line[:len(line) - len(line.lstrip())]
            if scope[0] == 'page':
                call = 'await OpenScenarioSettingsAsync();' if scope[1] == 'Page' else f'await PlaywrightTestBase.OpenScenarioSettingsAsync({scope[1]});'
            else:
                call = f'await OpenScenarioSettingsAsync({scope[1]});'
            total += 1
            if DRY:
                print(f'{path[len(E):]}: {call}  <- {line.strip()[:110]}')
            out.append(indent + call)
        out.append(line)
    if not DRY:
        open(path, 'w', encoding='utf-8', newline='').write('\n'.join(out))
print('calls', total)
