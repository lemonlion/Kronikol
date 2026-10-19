# S12 D48 (round 17 item 20): focus the keyboard moves is never left under the pinned report bar. When Tab or Shift+Tab
# moves focus to a control the pinned bar covers, or a scenario's open settings stuck under it (S8), the page brings it
# 12 px below them, and one past the foot of the window 12 px above the foot; for a second after, it brings back a control
# the page moves away. Focus a click or a script gives is left where it lands (the R17.20 bug: clicking in and out of a
# diagram scrolled the page), and so is a control in the bar, in those settings or in anything fixed, and a control
# taller than the room below the bar that already shows in it. Nothing changes on a phone, whose bar scrolls away.
# The script is report-focus-script.js, beside this file: written to src/Kronikol/Reports/, where Kronikol.csproj's
# Reports\report-*.js wildcard embeds it, and loaded in the head after the tabs script, in a page that has the bar. The
# tab flags the bar depends on move up above the head (they were worked out just before the bar is written), so that the
# head can say whether there is a bar. No stylesheet changes.
# ReportGenerator.cs, for stacking with the other S12 stages (line numbers at a1235d98): a block inserted after line 1602
# (the components script), line 1670 (the head's last script line) and lines 1937 to 1945 (the tabs' comment and flags,
# cut down to the comment on the tab the page opens on).
# usage: python scripts-s12d48.py <worktree root>
import os
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
R = ROOT + 'src/Kronikol/'
HERE = os.path.dirname(os.path.abspath(__file__))

# All or nothing: every edit is made in memory and checked, and the files are written only once all of them hold, so a
# moved anchor leaves the tree as it was.
files = {}
originals = {}
created = {}


def text(path):
    if path not in files:
        files[path] = originals[path] = open(R + path, encoding='utf-8', newline='').read()
    return files[path]


def edit(path, old, new, count=1):
    s = text(path)
    n = s.count(old)
    assert n == count, (path, n, old[:80])
    files[path] = s.replace(old, new)


def create(path, content):
    assert not os.path.exists(R + path), ('already there: D48 is in already', path)
    created[path] = content


RG = 'Reports/ReportGenerator.cs'
JS = 'Reports/report-focus-script.js'
# The new script is embedded by the csproj's wildcard, as every report-*.js is; nothing to add there.
assert '<EmbeddedResource Include="Reports\\report-*.js" />' in text('Kronikol.csproj'), 'the Reports\\report-*.js wildcard is gone'
assert 'report-focus-script.js' not in text(RG), 'D48 is in already'
# D48 builds on S3 (the tabs and the bar), the mark the bar pins below (D25) and S11e (the components script it follows).
assert '<script id="kronikol-report-tabs">{{reportTabsScript}}</script>' in text(RG), 'S3 is not in'
assert '<div class="nav-mark" aria-hidden="true"></div>' in text(RG), 'the bar has no mark to pin below (D25)'
assert 'report-components-script.js' in text(RG), 'S11e is not in: apply scripts-s11e.py first'

# ---------------------------------------------------------------------------------------------------------------------
# 1. The tab flags move from just before the bar to above the head, which loads the script only where the bar is.
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''        // The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): each section that sat above the features as a
        // panel of its own (the Scenario Timeline, the Component Diagram, History, Report diagnostics) is a tab of the
        // bar, and the features, with the failure clusters and the background calls at their top (Q4), are the Features
        // tab. A page with nothing beside its features has no tabs. A panel configured visible is the tab the page opens
        // on; when both are, the timeline, which exists in both reports (Q2).
        var hasComponentTab = !string.IsNullOrEmpty(componentDiagramPlantUml);
        var hasHistoryTab = history is not null && showHistorySection;
        var hasDiagnosticsTab = includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 };
        var useTabs = hasDurations || hasComponentTab || hasHistoryTab || hasDiagnosticsTab;
        var showTimelinePanel = toggles.ScenarioTimelineVisible && hasDurations;
''', r'''        // The tab the page opens on (the tabs are worked out above the head, which loads the focus script only where the
        // bar is): a panel configured visible; when both are, the timeline, which exists in both reports (Q2).
        var showTimelinePanel = toggles.ScenarioTimelineVisible && hasDurations;
''')
edit(RG, r'''        var componentsScript = componentControls
            ? "<script id=\"kronikol-components\">" + LoadResource("report-components-script.js") + "</script>"
            : "";

''', r'''        var componentsScript = componentControls
            ? "<script id=\"kronikol-components\">" + LoadResource("report-components-script.js") + "</script>"
            : "";
        // The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): each section that sat above the features as a
        // panel of its own (the Scenario Timeline, the Component Diagram, History, Report diagnostics) is a tab of the
        // bar, and the features, with the failure clusters and the background calls at their top (Q4), are the Features
        // tab. A page with nothing beside its features has no tabs, and so no bar.
        var hasComponentTab = !string.IsNullOrEmpty(componentDiagramPlantUml);
        var hasHistoryTab = history is not null && showHistorySection;
        var hasDiagnosticsTab = includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 };
        var useTabs = hasDurations || hasComponentTab || hasHistoryTab || hasDiagnosticsTab;
        // focus the keyboard moves is never left under the pinned bar (5.0.0, plans/OPTION_C_PLAN.md S12, D48), in a page
        // that has the bar
        var focusScript = useTabs
            ? "<script id=\"kronikol-focus\">" + LoadResource("report-focus-script.js") + "</script>"
            : "";

''')

# ---------------------------------------------------------------------------------------------------------------------
# 2. The head: the focus script after the tabs script and the scripts that follow it.
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, '''                            {{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}
                        </head>
''', '''                            {{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}{{focusScript}}
                        </head>
''')

# ---------------------------------------------------------------------------------------------------------------------
# 3. The script itself.
# ---------------------------------------------------------------------------------------------------------------------
script = open(os.path.join(HERE, 'report-focus-script.js'), encoding='utf-8', newline='').read()
assert script.startswith('// Focus the keyboard moves') and '\r' not in script and chr(0xFEFF) not in script, \
    'report-focus-script.js beside this script is not the one this stage carries'
create(JS, script)

changed = sorted(p for p in files if files[p] != originals[p])
for path in changed:
    open(R + path, 'w', encoding='utf-8', newline='').write(files[path])
for path, content in created.items():
    open(R + path, 'w', encoding='utf-8', newline='').write(content)
print('ok:', ', '.join(changed + sorted(created)))
