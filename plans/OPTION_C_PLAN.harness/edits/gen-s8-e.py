# S8 part e: the diagram tabs take a roving tabindex, as the page's other tabs do (the arrow keys move between them),
# and a page whose diagrams the page draws carries the scenario settings' script.
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/ReportGenerator.cs'
s = open(p, encoding='utf-8', newline='').read()
def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:80])
    s = s.replace(old, new)
sub('''role=\\"tab\\" aria-selected=\\"{(active ? "true" : "false")}\\" data-dtype=\\"{dtype}\\">{label}</button>";''',
    '''role=\\"tab\\" aria-selected=\\"{(active ? "true" : "false")}\\" tabindex=\\"{(active ? "0" : "-1")}\\" data-dtype=\\"{dtype}\\">{label}</button>";''')
sub('''        var diagramToggleLayoutScript = DiagramContextMenu.GetDiagramToggleLayoutScript();
''', '''        var diagramToggleLayoutScript = DiagramContextMenu.GetDiagramToggleLayoutScript();
        // each scenario's settings and their gears exist only where the page draws the diagrams (AppendDiagramSectionHead)
        var scenarioSettingsScript = isPlantUmlBrowser
            ? "<script id=\\"kronikol-scenario-settings\\">" + LoadResource("report-scenario-settings-script.js") + "</script>"
            : "";
''')
sub('''                            <script id="kronikol-report-tabs">{{reportTabsScript}}</script>
''', '''                            <script id="kronikol-report-tabs">{{reportTabsScript}}</script>
                            {{scenarioSettingsScript}}
''')
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
