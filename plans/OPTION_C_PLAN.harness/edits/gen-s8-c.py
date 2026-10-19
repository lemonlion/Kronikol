# S8 part c: a diagram section's head, written once for a scenario and for a parameterized group (D29 to D34): the
# views' tabs on a bar of their own, then the scenario's settings behind a gear, for sequence diagrams only; on a phone
# the gear on the section's title opens them. A section with no sequence diagrams has no settings (the preview emitted
# its bar and always hid it). The span warning stays on the tabs bar, or on the title when there is one view.
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/ReportGenerator.cs'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:80])
    s = s.replace(old, new)

def cut(start, end, new):
    """replace s[start marker .. end marker) once, the end marker kept"""
    global s
    i = s.index(start)
    assert s.count(start) == 1, start[:80]
    j = s.index(end, i)
    s = s[:i] + new + s[j:]

COG = open('C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/4446b339-7f87-4b87-990f-495a136f5c68/scratchpad/v5parity/cog-path.txt', encoding='utf-8').read().strip()

# the helpers, after the toolbar builder
sub(r'''        sb.Append("</span>");
        return sb.ToString();
    }
''', r'''        sb.Append("</span>");
        return sb.ToString();
    }

    private const string DiagramTabsOpen = "<div class=\"diagram-toggle diagram-toggle-tabs\" role=\"tablist\" aria-label=\"Diagram views\">";

    /// <summary>The cog the settings gear draws: eight teeth around a ring, the hole cut by the even-odd rule.</summary>
    private const string ScenarioSettingsCog = "''' + COG + r'''";

    private const string ScenarioSettingsGear = "<button type=\"button\" class=\"scenario-settings-gear\" aria-label=\"This scenario's settings\" aria-expanded=\"false\" title=\"This scenario's payload and diagram settings, to set it apart from the report's\">"
        + "<svg viewBox=\"-12 -12 24 24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"" + ScenarioSettingsCog + "\" fill-rule=\"evenodd\"/></svg></button>";

    /// <summary>The same settings' gear on the section's title line, which a phone shows in place of the one above.</summary>
    private const string ScenarioSettingsSummaryGear = "<button type=\"button\" class=\"scenario-diagram-controls-toggle gear-in-summary\" aria-label=\"Diagram settings\" title=\"Diagram settings\" aria-expanded=\"false\">\u2699</button>";

    private static string BuildDiagramTab(string dtype, string label, bool active) =>
        $"<button class=\"diagram-toggle-btn{(active ? " diagram-toggle-active" : "")}\" role=\"tab\" aria-selected=\"{(active ? "true" : "false")}\" data-dtype=\"{dtype}\">{label}</button>";

    /// <summary>
    /// A diagram section's head (5.0.0, plans/OPTION_C_PLAN.md D29 to D34), for a scenario and for a parameterized group:
    /// the section's title; the tabs of its views on a bar of their own, when it has more than one; and the scenario's
    /// settings, the options bar behind a gear, which act on sequence diagrams only and so are written where the section
    /// has some and the page draws them (<paramref name="settingsControls"/> is empty otherwise). The bar starts with
    /// "This scenario:", so it reads apart from the report's own; on a phone the gear on the title opens it. A section
    /// that opens on another view starts with its settings out of sight (<c>diagrams-not-seq</c>, which
    /// toggle-script.js keeps as the tabs change). The warning goes on the tabs bar, or on the title when there is no bar.
    /// </summary>
    private static void AppendDiagramSectionHead(StringBuilder body, bool open, bool hasSeq, bool hasActivity, bool hasFlame,
        DiagramTabKind activeTab, string settingsControls, string warning)
    {
        var views = (hasSeq ? 1 : 0) + (hasActivity ? 1 : 0) + (hasFlame ? 1 : 0);
        var hasSettings = hasSeq && settingsControls.Length > 0;
        var notSeq = hasSettings && activeTab != DiagramTabKind.Sequence;
        body.Append($"<details class=\"example-diagrams{(notSeq ? " diagrams-not-seq" : "")}\"{(open ? " open" : "")}>");
        var title = views > 1 ? "Diagrams" : hasSeq ? "Sequence Diagrams" : hasActivity ? "Activity Diagrams" : "Flame Chart";
        body.Append("<summary class=\"h4\">").Append(title);
        if (hasSettings) body.Append(ScenarioSettingsSummaryGear);
        if (views == 1) body.Append(warning);
        body.Append("</summary>");
        if (views > 1)
        {
            body.Append(DiagramTabsOpen);
            if (hasSeq) body.Append(BuildDiagramTab("seq", "Sequence Diagrams", activeTab == DiagramTabKind.Sequence));
            if (hasActivity) body.Append(BuildDiagramTab("activity", "Activity Diagrams", activeTab == DiagramTabKind.Activity));
            if (hasFlame) body.Append(BuildDiagramTab("flame", "Flame Chart", activeTab == DiagramTabKind.FlameChart));
            body.Append(warning).Append("</div>");
        }
        if (hasSettings)
            body.Append("<div class=\"scenario-settings\"><div class=\"diagram-toggle diagram-options-bar\"><span class=\"scenario-settings-label\">This scenario:</span>")
                .Append(settingsControls).Append("</div>").Append(ScenarioSettingsGear).Append("</div>");
    }
''')

# the toolbar builder's comment: no spacer, three zones, one variant
sub(r'''    /// The scenario-level diagram toolbar controls (spacer, Details radio, filter toggles, note
    /// format dropdown). Built once per report — the five scenario toolbar emission sites all use
    /// this builder, so they cannot drift (the same contract as the note-format select).''',
    r'''    /// The scenario-level payload controls, in three zones (5.0.0, D30): the Payloads radio, the format and width
    /// dropdowns, the toggles. Built once per report and written by <see cref="AppendDiagramSectionHead"/> into every
    /// scenario's settings, so they cannot drift (the same contract as the note-format select).''')

# one variant only: a section with no sequence diagrams has no settings
sub(r'''        // The full scenario-level control run, built once (see BuildScenarioDiagramToolbar). The
        // no-filters variant serves the flow-only branch, which has no sequence content to filter.
        var scenarioToolbarControls = BuildScenarioDiagramToolbar(toggles,
            hasAssertionNotes, hasStepDelimiters, hasDatabaseParticipants, scenarioNoteFormatSelect,
            scenarioNoteWidthSelect);
        var scenarioToolbarControlsNoFilters = BuildScenarioDiagramToolbar(toggles,
            includeAssertions: false, includeSteps: false, includeDatabases: false, scenarioNoteFormatSelect,
            scenarioNoteWidthSelect);
''', r'''        // The scenario-level control run, built once (see BuildScenarioDiagramToolbar).
        var scenarioToolbarControls = BuildScenarioDiagramToolbar(toggles,
            hasAssertionNotes, hasStepDelimiters, hasDatabaseParticipants, scenarioNoteFormatSelect,
            scenarioNoteWidthSelect);
''')

# the plain scenario
cut(r'''                    body.Append($"<details class=\"example-diagrams\"{(toggles.DiagramsSectionOpen ? " open" : "")}>");

                    if (hasWholeTestFlow && hasSequenceDiagrams)''',
    r'''                    if (hasSequenceDiagrams)
                    {
                        var seqWrap = hasWholeTestFlow;''',
    r'''                    AppendDiagramSectionHead(body, toggles.DiagramsSectionOpen, hasSequenceDiagrams, hasActivityView, hasFlameView,
                        activeTab, isPlantUmlBrowser ? scenarioToolbarControls : "", spanWarning);

''')

# the parameterized group
cut(r'''            body.Append($"<details class=\"example-diagrams\"{(toggles.DiagramsSectionOpen ? " open" : "")}>");

            // Determine toggle buttons needed''',
    r'''            // Sequence diagrams
            if (hasAnySeqDiagrams)''',
    r'''            // The views the group has
            var showSeqToggle = hasAnySeqDiagrams;
            var showActivityToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.ActivityHtml));
            var showFlameToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.FlameHtml));
            var multipleTypes = (showSeqToggle ? 1 : 0) + (showActivityToggle ? 1 : 0) + (showFlameToggle ? 1 : 0) > 1;
            var activeTab = ResolveDiagramTab(toggles.DiagramTab, showSeqToggle, showActivityToggle, showFlameToggle);
            AppendDiagramSectionHead(body, toggles.DiagramsSectionOpen, showSeqToggle, showActivityToggle, showFlameToggle,
                activeTab, isPlantUmlBrowser ? scenarioToolbarControls : "", warning: "");

''')

open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
