# S4: the Features tools line (D21, D22, D35), the features table as the Summary view, the report bar's zones (D30).
# Run from the v5 worktree root. Every substitution must match exactly once.
P = 'src/Kronikol/Reports/ReportGenerator.cs'
s = open(P, 'rb').read().decode('utf-8')

def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:100])
    s = s.replace(old, new)

# 1. the features table is built aside, for the Features tab's Summary view
sub('''        if (includeTestRunData)
        {
            var numberOfFeatures = features.Length;''', '''        // The features table, the Features tab's Summary view (S4), and the column its duration group starts at.
        var featureTable = new StringBuilder();
        int? durationCol = null;
        if (includeTestRunData)
        {
            var numberOfFeatures = features.Length;''')

start = s.index('            // Feature summary table (collapsible, above execution summary)\n')
end_marker = '            body.Append("</details>");\n\n            // The run band'
end = s.index(end_marker, start)
region = s[start:end + len('            body.Append("</details>");\n')]
r = region.replace('body.Append(', 'featureTable.Append(')

def rsub(old, new):
    global r
    assert r.count(old) == 1, (r.count(old), old[:100])
    r = r.replace(old, new)

rsub('''            // Feature summary table (collapsible, above execution summary)
''', '''            // The features table (5.0.0, plans/OPTION_C_PLAN.md S4, D21): one row per feature with its counts and times,
            // written in the Features tab beside the feature cards (the Summary view) in place of the 4.x collapsible
            // Features Summary above the execution summary. Passed, Failed and Skipped repeat for scenarios and for steps,
            // so a row above the headers names each group and the group's first column reads Total. The rows are in the
            // cards' order, which report-features-tools-script.js relies on to pair a row with its card.
''')
rsub('''            featureTable.Append($"<details class=\\"features-summary-details\\"{(toggles.FeaturesSummaryOpen ? " open" : "")}><summary class=\\"h2\\">Features Summary</summary>");
            featureTable.Append("<div class=\\"features-summary-table-wrapper\\">");
            featureTable.Append("<table class=\\"feature-summary-table\\"><thead><tr>");
            featureTable.Append("<th onclick=\\"sort_table(0)\\">Feature</th>");
            featureTable.Append("<th onclick=\\"sort_table(1)\\">Scenarios</th>");''', '''            featureTable.Append("<div class=\\"features-summary-table-wrapper\\">");
            featureTable.Append("<table class=\\"feature-summary-table\\"><thead>");
            featureTable.Append("<tr class=\\"table-groups\\"><th></th><th colspan=\\"4\\">Scenarios</th>");
            if (hasAnySteps) featureTable.Append("<th class=\\"grp-start\\" colspan=\\"4\\">Steps</th>");
            if (hasAnyDurations) featureTable.Append("<th class=\\"grp-start\\" colspan=\\"3\\">Duration</th>");
            featureTable.Append("</tr><tr>");
            featureTable.Append("<th onclick=\\"sort_table(0)\\">Feature</th>");
            featureTable.Append("<th onclick=\\"sort_table(1)\\">Total</th>");''')
rsub('''                featureTable.Append($"<th onclick=\\"sort_table({nextCol++})\\">Steps</th>");''',
     '''                featureTable.Append($"<th class=\\"grp-start\\" onclick=\\"sort_table({nextCol++})\\">Total</th>");''')
rsub('''                featureTable.Append($"<th onclick=\\"sort_table({nextCol++})\\">Duration</th>");''',
     '''                durationCol = nextCol;
                featureTable.Append($"<th class=\\"grp-start\\" onclick=\\"sort_table({nextCol++})\\">Total</th>");''')
rsub('''                    featureTable.Append($"<td>{stepCount}</td>");''', '''                    featureTable.Append($"<td class=\\"grp-start\\">{stepCount}</td>");''')
rsub('''                    featureTable.Append(CultureInfo.InvariantCulture, $"<td data-sort=\\"{totalDuration.TotalMilliseconds:F0}\\">{FormatDuration(totalDuration)}</td>");''',
     '''                    featureTable.Append(CultureInfo.InvariantCulture, $"<td class=\\"grp-start\\" data-sort=\\"{totalDuration.TotalMilliseconds:F0}\\">{FormatDuration(totalDuration)}</td>");''')
rsub('''            featureTable.Append("</tbody></table>");
            featureTable.Append("</div>");
            featureTable.Append("</details>");
''', '''            featureTable.Append("</tbody></table>");
            featureTable.Append("</div>");
''')
s = s[:start] + r + s[end + len('            body.Append("</details>");\n'):]

# 2. the bar: the Summary view's class, the tools line, the zones
sub('''        var startTab = showTimelinePanel ? "timeline" : showComponentPanel ? "components" : "features";
''', '''        var startTab = showTimelinePanel ? "timeline" : showComponentPanel ? "components" : "features";
        // FeaturesSummaryOpen opens the Features tab on its Summary view (S4), where 4.x opened the summary's card.
        var hasFeatureTable = featureTable.Length > 0;
        var summaryStart = hasFeatureTable && toggles.FeaturesSummaryOpen;
''')
sub('''        body.Append(useTabs ? $"""<div class="toolbar-row report-nav{(startTab == "features" ? " on-features" : "")}">""" : """<div class="toolbar-row">""");''',
    '''        var tableMode = summaryStart ? " features-table-mode" : "";
        body.Append(useTabs ? $"""<div class="toolbar-row report-nav{(startTab == "features" ? " on-features" : "")}{tableMode}">""" : $"""<div class="toolbar-row{tableMode}">""");''')
sub('''        // The expand-all buttons flip by label, so a seeded-expanded start must seed the
        // matching flip label ("Collapse All …") or the first click would be a no-op.
        var featuresBtnLabel = toggles.FeaturesExpanded ? "Collapse All Features" : "Expand All Features";
        var scenariosBtnLabel = toggles.ScenariosExpanded ? "Collapse All Scenarios" : "Expand All Scenarios";
        // On a page with tabs the tools are the Features tab's, in a strip under the tabs that the other tabs hide.
        if (useTabs) body.Append("""<div class="features-tools">""");
        body.Append($"""<div class="toolbar-left{(useTabs ? " features-tools-line" : "")}"><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.feature', 'Expand All Features', 'Collapse All Features')">{featuresBtnLabel}</button><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.scenario', 'Expand All Scenarios', 'Collapse All Scenarios')">{scenariosBtnLabel}</button>""");
        body.Append("</div>");
        body.Append("""<div class="toolbar-right">""");
        if (isPlantUmlBrowser)
        {
            body.Append(BuildDetailsRadio(toggles, scenarioLevel: false));
            body.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: false));
            if (hasAssertionNotes)
                body.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: false));
            if (hasStepDelimiters)
                body.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: false));
            if (hasDatabaseParticipants)
                body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: false));
            body.Append(reportNoteFormatSelect);
            body.Append(reportNoteWidthSelect);
        }
        body.Append("</div>");''', '''        // The Features tools (5.0.0, plans/OPTION_C_PLAN.md S4, D21, D22, D35): the Full and Summary views as tabs when
        // the page has the features table; expand and collapse for features and for scenarios, each a pair of icon
        // buttons dimmed while it has nothing to do (the seeded start says which, report-features-tools-script.js keeps
        // it true); the sort; and on a phone the gear that shows the display settings. On a page with tabs they are the
        // Features tab's, in a strip under the tabs that the other tabs hide.
        if (useTabs) body.Append("""<div class="features-tools">""");
        body.Append($"""<div class="toolbar-left{(useTabs ? " features-tools-line" : "")}">""");
        if (hasFeatureTable) body.Append(FeaturesViewSwitch(summaryStart));
        body.Append(FoldGroup("features", features.Length, toggles.FeaturesExpanded));
        body.Append(FoldGroup("scenarios", features.Sum(f => f.Scenarios.Length), toggles.ScenariosExpanded));
        body.Append("""<span class="tools-spacer"></span>""");
        if (hasFeatureTable) body.Append(FeaturesSortGroup(durationCol));
        if (useTabs && isPlantUmlBrowser) body.Append(ToolsGear);
        body.Append("</div>");
        body.Append("""<div class="toolbar-right" id="display-settings">""");
        if (isPlantUmlBrowser)
        {
            // The display settings in zones (D30): what a note shows of its payload, then the payload's format and width,
            // then the toggles for what the diagrams draw.
            body.Append(BuildDetailsRadio(toggles, scenarioLevel: false));
            if (reportNoteFormatSelect.Length + reportNoteWidthSelect.Length > 0)
                body.Append("""<span class="diagram-format">""").Append(reportNoteFormatSelect).Append(reportNoteWidthSelect).Append("</span>");
            body.Append("""<span class="diagram-flags">""");
            body.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: false));
            if (hasAssertionNotes)
                body.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: false));
            if (hasStepDelimiters)
                body.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: false));
            if (hasDatabaseParticipants)
                body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: false));
            body.Append("</span>");
        }
        body.Append("</div>");''')

# 3. the Features tab's two views, the table and the cards, one shown at a time
sub('''        body.Append("<div id=\\"report-content\\">");''', '''        if (hasFeatureTable)
        {
            body.Append("<div class=\\"features-panel\\">");
            body.Append($"<div class=\\"features-table-view\\" id=\\"features-table-view\\" role=\\"tabpanel\\" aria-labelledby=\\"view-summary\\"{(summaryStart ? "" : " hidden")}>");
            body.Append(featureTable).Append("</div>");
            body.Append($"<div id=\\"report-content\\" role=\\"tabpanel\\" aria-labelledby=\\"view-full\\"{(summaryStart ? " hidden" : "")}>");
        }
        else body.Append("<div id=\\"report-content\\">");''')
sub('''        body.Append("</div>");
        body.Append("</div>"); // the Features tab's panel''', '''        body.Append("</div>");
        if (hasFeatureTable) body.Append("</div>"); // the features panel
        body.Append("</div>"); // the Features tab's panel''')

# 4. the script that drives them, in place of the expand-all function
sub('''        var collapseExpandAllFunction = LoadResource("report-collapse-expand-all-function.js");''',
    '''        var featuresToolsScript = LoadResource("report-features-tools-script.js");''')
sub('''                                {{collapseExpandAllFunction}}''', '''                                {{featuresToolsScript}}''')

# 5. the helpers, after ReportPanel
sub('''    /// <summary>A tab's panel: hidden unless its tab is the one selected.</summary>
    private static string ReportPanel(string key, string selected) =>
        $"<div class=\\"report-panel\\" id=\\"panel-{key}\\" role=\\"tabpanel\\" aria-labelledby=\\"tab-{key}\\" data-panel=\\"{key}\\"{(key == selected ? "" : " hidden")}>";
''', '''    /// <summary>A tab's panel: hidden unless its tab is the one selected.</summary>
    private static string ReportPanel(string key, string selected) =>
        $"<div class=\\"report-panel\\" id=\\"panel-{key}\\" role=\\"tabpanel\\" aria-labelledby=\\"tab-{key}\\" data-panel=\\"{key}\\"{(key == selected ? "" : " hidden")}>";

    /// <summary>The Features tab's views, Full (the cards) and Summary (the features table), as tabs (S4, D21).</summary>
    private static string FeaturesViewSwitch(bool summary) =>
        "<div class=\\"view-switch\\" role=\\"tablist\\" aria-label=\\"Features view\\">"
        + ViewTab("full", "Full", "report-content", "Each feature as a card with its scenarios", !summary)
        + ViewTab("summary", "Summary", "features-table-view", "One table row per feature, with its counts and times", summary)
        + "</div>";

    private static string ViewTab(string key, string label, string panel, string title, bool selected) =>
        $"<button type=\\"button\\" class=\\"view-btn\\" id=\\"view-{key}\\" role=\\"tab\\" aria-controls=\\"{panel}\\" aria-selected=\\"{(selected ? "true" : "false")}\\" tabindex=\\"{(selected ? "0" : "-1")}\\" data-view=\\"{key}\\" title=\\"{title}\\">{label}</button>";

    /// <summary>
    /// Expand and collapse all features, or all scenarios, as a label and two icon buttons (S4, D22, item R17.2). A button
    /// with nothing to do is dimmed, not disabled, so it keeps its place in the tab order: expand while every one starts
    /// open, collapse while none does, both when there are none.
    /// </summary>
    private static string FoldGroup(string noun, int count, bool expanded) =>
        $"<span class=\\"fold-group\\" role=\\"group\\" aria-label=\\"Expand or collapse all {noun}\\"><span class=\\"fold-label\\" aria-hidden=\\"true\\">{noun}</span>"
        + FoldButton("expand", noun, count == 0 || expanded, "<polyline points=\\"4,6 8,2 12,6\\"/><polyline points=\\"4,10 8,14 12,10\\"/>")
        + FoldButton("collapse", noun, count == 0 || !expanded, "<polyline points=\\"4,2 8,6 12,2\\"/><polyline points=\\"4,14 8,10 12,14\\"/>")
        + "</span>";

    private static string FoldButton(string action, string noun, bool idle, string chevrons)
    {
        var name = $"{(action == "expand" ? "Expand" : "Collapse")} all {noun}";
        return $"<button type=\\"button\\" class=\\"fold-btn fold-{action}\\" data-fold=\\"{noun}\\" title=\\"{name}\\" aria-label=\\"{name}\\" aria-disabled=\\"{(idle ? "true" : "false")}\\">"
            + "<svg viewBox=\\"0 0 16 16\\" fill=\\"none\\" stroke=\\"currentColor\\" stroke-width=\\"1.6\\" stroke-linecap=\\"round\\" stroke-linejoin=\\"round\\" aria-hidden=\\"true\\" focusable=\\"false\\">"
            + chevrons + "</svg></button>";
    }

    /// <summary>
    /// The sort for the feature cards and the table's rows alike (S4, D21). Report order is the order the page is written
    /// in; each other option names the table column it sorts on, so a report without step or duration columns offers
    /// only what it has (the preview sorted on fixed column numbers, and called the report's own order Feature A-Z).
    /// </summary>
    private static string FeaturesSortGroup(int? durationCol)
    {
        var options = new StringBuilder("<option value=\\"order\\">Report order</option><option value=\\"name\\" data-col=\\"0\\">Feature A–Z</option>"
            + "<option value=\\"failed\\" data-col=\\"3\\">Failures first</option><option value=\\"scenarios\\" data-col=\\"1\\">Most scenarios</option>");
        if (durationCol is { } d)
            options.Append(CultureInfo.InvariantCulture, $"<option value=\\"duration\\" data-col=\\"{d}\\">Slowest total</option><option value=\\"average\\" data-col=\\"{d + 1}\\">Slowest average</option><option value=\\"longest\\" data-col=\\"{d + 2}\\">Longest scenario</option>");
        return "<label class=\\"sort-group\\"><span class=\\"sort-label\\">Sort</span><select id=\\"features-sort\\" class=\\"sort-select\\" aria-label=\\"Sort features\\" title=\\"The order the features are listed in\\">"
            + options + "</select></label>";
    }

    /// <summary>On a phone, the gear that shows the report bar's display settings (S4, D35).</summary>
    private const string ToolsGear =
        "<button type=\\"button\\" class=\\"tools-gear\\" aria-label=\\"Display settings\\" title=\\"Display settings\\" aria-expanded=\\"false\\" aria-controls=\\"display-settings\\">\\u2699</button>";
''')

open(P, 'wb').write(s.encode('utf-8'))
print('ok')
