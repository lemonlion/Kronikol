# S3: the report's tabs and panels in the generator (D20, Q2, Q4). Run from the worktree root.
P = 'src/Kronikol/Reports/ReportGenerator.cs'
s = open(P, 'rb').read().decode()
BS = chr(92)  # a backslash, so nothing here depends on how a shell or a string literal treats one
Q = BS + '"'  # \" as it is written in the C# source

def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:90])
    s = s.replace(old, new)

# K. the toggle functions go: the panels are tabs now
a = s.index('        // Toggle timeline\n        var deactivateComponentDiagramJs')
b = s.index('        // Jump to failure\n', a)
s = s[:a] + s[b:]
sub('                                {{toggleTimelineFunction}}\n                                {{toggleComponentDiagramFunction}}\n', '')

# L. the tabs script, after the run band's
sub('        var runBandScript = LoadResource("report-run-band-script.js");\n',
    '        var runBandScript = LoadResource("report-run-band-script.js");\n        var reportTabsScript = LoadResource("report-tabs-script.js");\n')
sub('                            <script id="kronikol-run-band">{{runBandScript}}</script>\n',
    '                            <script id="kronikol-run-band">{{runBandScript}}</script>\n                            <script id="kronikol-report-tabs">{{reportTabsScript}}</script>\n')

# A-C. which tabs the page has, the tab it opens on, and the bar
sub('''        // Toolbar row: expand buttons left, Details/Headers right
        body.Append("""<div class="toolbar-row">""");''', '''        // The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): each section that sat above the features as a
        // panel of its own (the Scenario Timeline, the Component Diagram, History, Report diagnostics) is a tab of the
        // bar, and the features, with the failure clusters and the background calls at their top (Q4), are the Features
        // tab. A page with nothing beside its features has no tabs. A panel configured visible is the tab the page opens
        // on; when both are, the timeline, which exists in both reports (Q2).
        var hasComponentTab = !string.IsNullOrEmpty(componentDiagramPlantUml);
        var hasHistoryTab = history is not null && showHistorySection;
        var hasDiagnosticsTab = includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 };
        var useTabs = hasDurations || hasComponentTab || hasHistoryTab || hasDiagnosticsTab;
        var showTimelinePanel = toggles.ScenarioTimelineVisible && hasDurations;
        var showComponentPanel = toggles.ComponentDiagramVisible && hasComponentTab && !showTimelinePanel;
        var startTab = showTimelinePanel ? "timeline" : showComponentPanel ? "components" : "features";

        // Toolbar row: the tabs, then expand buttons left, Details/Headers right
        body.Append(useTabs ? $"""<div class="toolbar-row report-nav{(startTab == "features" ? " on-features" : "")}">""" : """<div class="toolbar-row">""");
        if (useTabs)
        {
            body.Append("""<div class="report-tabs" role="tablist" aria-label="Report sections">""");
            body.Append(ReportTab("features", "Features", startTab, features.Length));
            if (hasDurations) body.Append(ReportTab("timeline", "Timeline", startTab));
            if (hasComponentTab) body.Append(ReportTab("components", "Components", startTab));
            if (hasHistoryTab) body.Append(ReportTab("history", "History", startTab));
            if (hasDiagnosticsTab) body.Append(ReportTab("diagnostics", "Diagnostics", startTab, diagnostics!.Count));
            body.Append("</div>");
        }''')

a = s.index('        // Seeded panel visibility: the two panels are mutually exclusive; when both are\n')
b = s.index('        body.Append("</div>");\n', a)
removed = s[a:b]
assert 'toggle_component_diagram(this)' in removed and 'toggle_timeline(this)' in removed, 'toggle buttons block'
s = s[:a] + s[b:]

# D. the panels open after the bar
sub('''        body.Append("</div>");
        body.Append("</div>");

        var plantUmlBrowserCounter = 0;''', '''        body.Append("</div>");
        body.Append("</div>");

        // The panels: the Features tab's first; the others are written after the features (below) from HTML built where
        // the sections were, so the diagrams keep their ids. Without tabs the features' panel has no tab to name it.
        body.Append("<div class=''' + Q + '''report-panels''' + Q + '''>");
        body.Append(useTabs ? ReportPanel("features", startTab) : """<div class="report-panel" id="panel-features" data-panel="features">""");

        var plantUmlBrowserCounter = 0;''')

# E. diagnostics into their panel
sub('''        if (includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 })
            body.Append(RenderReportDiagnostics(diagnostics, toggles.DiagnosticsOpen));''', '''        // In its tab the section is always open: the tab is its disclosure, so DiagnosticsOpen has nothing left to open.
        var diagnosticsHtml = hasDiagnosticsTab ? RenderReportDiagnostics(diagnostics!, open: true, inTab: true) : "";''')

# G. history into its panel
sub('''        if (history is not null && showHistorySection)
            body.Append(HistoryHtml.Section(history));''', '''        var historyHtml = hasHistoryTab ? HistoryHtml.InTab(history!) : "";''')

# H. the timeline into its panel
a = s.index('        // Scenario timeline / Gantt (hidden by default)\n')
b = s.index('        // Embedded component diagram\n', a)
block = s[a:b]
block = block.replace('        // Scenario timeline / Gantt (hidden by default)\n',
                      "        // Scenario timeline / Gantt, the Timeline tab's panel\n        var timelineHtml = new StringBuilder();\n", 1)
n = block.count('body.Append(')
block = block.replace('body.Append(', 'timelineHtml.Append(')
old_div = 'timelineHtml.Append($"<div id=' + Q + 'scenario-timeline' + Q + ' class=' + Q + 'scenario-timeline' + Q + '{(showTimelinePanel ? "" : " style=' + Q + 'display:none' + Q + '")}>");'
assert block.count(old_div) == 1, 'timeline div'
block = block.replace(old_div, 'timelineHtml.Append("<div id=' + Q + 'scenario-timeline' + Q + ' class=' + Q + 'scenario-timeline' + Q + '>");')
s = s[:a] + block + s[b:]
print('timeline appends moved:', n)

# I. the component diagram into its panel
sub('            body.Append($"""<div id="component-diagram" class="component-diagram-section"{(showComponentPanel ? "" : " style=' + Q + 'display:none' + Q + '")}>{componentDiagramHtml}</div>""");',
    '            componentHtml = $"""<div id="component-diagram" class="component-diagram-section">{componentDiagramHtml}</div>""";')
sub('''        // Embedded component diagram
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))''', '''        // Embedded component diagram, the Components tab's panel
        var componentHtml = "";
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))''')

# J. close the features panel, then the other panels, then the panels
sub('''            body.Append("</details>");
        }
        body.Append("</div>");

        // Jump-to-failure button (only when there are failures)''', '''            body.Append("</details>");
        }
        body.Append("</div>");
        body.Append("</div>"); // the Features tab's panel
        if (timelineHtml.Length > 0) body.Append(ReportPanel("timeline", startTab)).Append(timelineHtml).Append("</div>");
        if (componentHtml.Length > 0) body.Append(ReportPanel("components", startTab)).Append(componentHtml).Append("</div>");
        if (historyHtml.Length > 0) body.Append(ReportPanel("history", startTab)).Append(historyHtml).Append("</div>");
        if (diagnosticsHtml.Length > 0) body.Append(ReportPanel("diagnostics", startTab)).Append(diagnosticsHtml).Append("</div>");
        body.Append("</div>"); // the panels

        // Jump-to-failure button (only when there are failures)''')

# M. diagnostics open in a tab with their summary out of the tab order
sub('    internal static string RenderReportDiagnostics(IReadOnlyList<DiagnosticEntry> diagnostics, bool open = false)',
    '    internal static string RenderReportDiagnostics(IReadOnlyList<DiagnosticEntry> diagnostics, bool open = false, bool inTab = false)')
sub('        html.Append($"<details class=' + Q + 'report-diagnostics' + Q + '{(open ? " open" : "")}>");\n        html.Append($"<summary>{summary}</summary>");',
    '        html.Append($"<details class=' + Q + 'report-diagnostics' + Q + '{(open || inTab ? " open" : "")}>");\n'
    + '        // In its tab the summary is the section\'s heading and nothing to click: the tabs script keeps it open.\n'
    + '        html.Append($"<summary{(inTab ? " tabindex=' + Q + '-1' + Q + '" : "")}>{summary}</summary>");')

# the tab and panel markup
tab = ('    /// <summary>A tab of the report bar (5.0.0, S3): the selected one is in the tab order, the others reached by the arrow\n'
       '    /// keys (report-tabs-script.js).</summary>\n'
       '    private static string ReportTab(string key, string label, string selected, int? count = null) =>\n'
       '        $"<button class=' + Q + 'report-tab' + Q + ' id=' + Q + 'tab-{key}' + Q + ' role=' + Q + 'tab' + Q + ' aria-controls=' + Q + 'panel-{key}' + Q
       + ' aria-selected=' + Q + '{(key == selected ? "true" : "false")}' + Q + ' tabindex=' + Q + '{(key == selected ? "0" : "-1")}' + Q + ' data-tab=' + Q + '{key}' + Q + '>{label}"\n'
       '        + (count is { } n ? $"<span class=' + Q + 'tab-n' + Q + '>{n.ToString(CultureInfo.InvariantCulture)}</span>" : "") + "</button>";\n\n'
       '    /// <summary>A tab\'s panel: hidden unless its tab is the one selected.</summary>\n'
       '    private static string ReportPanel(string key, string selected) =>\n'
       '        $"<div class=' + Q + 'report-panel' + Q + ' id=' + Q + 'panel-{key}' + Q + ' role=' + Q + 'tabpanel' + Q + ' aria-labelledby=' + Q + 'tab-{key}' + Q
       + ' data-panel=' + Q + '{key}' + Q + '{(key == selected ? "" : " hidden")}>";\n\n')
sub('    private static void AppendRunBand(StringBuilder body,', tab + '    private static void AppendRunBand(StringBuilder body,')

open(P, 'wb').write(s.encode())
print('ok')
