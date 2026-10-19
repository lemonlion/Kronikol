# S9a, step 2 (C#): the Events and Setup toggles (D38, P5, P11, F11). Gates written report-wide like the other filter
# toggles' (a report with a broker gets Events, beside a Databases toggle or not), the buttons after Databases in the
# report's bar and every scenario's settings, and the script's two start states. usage: python gen-s9-a.py
ROOT = 'C:/Code/Kronikol-v5/'
RG = ROOT + 'src/Kronikol/Reports/ReportGenerator.cs'
DCM = ROOT + 'src/Kronikol/Reports/DiagramContextMenu.cs'


def edit(path, pairs):
    s = open(path, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    for old, new in pairs:
        if crlf:
            old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
        assert s.count(old) == 1, (path, old[:70], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)
    print('edited', path)


edit(RG, [
    ("""    private static string BuildScenarioDiagramToolbar(ResolvedToggleDefaults toggles,
        bool includeAssertions, bool includeSteps, bool includeDatabases, string scenarioNoteFormatSelect,
        string scenarioNoteWidthSelect = "")
    {""",
     """    private static string BuildScenarioDiagramToolbar(ResolvedToggleDefaults toggles,
        bool includeAssertions, bool includeSteps, bool includeDatabases, bool includeEvents, bool includeSetup,
        string scenarioNoteFormatSelect, string scenarioNoteWidthSelect = "")
    {"""),
    ("""        if (includeDatabases)
            sb.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
        sb.Append("</span>");""",
     """        if (includeDatabases)
            sb.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
        if (includeEvents)
            sb.Append(BuildFilterToggleButton("events", toggles.EventsShown, scenarioLevel: true));
        if (includeSetup)
            sb.Append(BuildFilterToggleButton("setup", toggles.SetupShown, scenarioLevel: true));
        sb.Append("</span>");"""),
    ("""        var scenarioToolbarControls = BuildScenarioDiagramToolbar(toggles,
            hasAssertionNotes, hasStepDelimiters, hasDatabaseParticipants, scenarioNoteFormatSelect,
            scenarioNoteWidthSelect);""",
     """        var scenarioToolbarControls = BuildScenarioDiagramToolbar(toggles,
            hasAssertionNotes, hasStepDelimiters, hasDatabaseParticipants, hasEventParticipants, hasSetup,
            scenarioNoteFormatSelect, scenarioNoteWidthSelect);"""),
    ("""            if (hasDatabaseParticipants)
                body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: false));
            body.Append("</span>");""",
     """            if (hasDatabaseParticipants)
                body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: false));
            if (hasEventParticipants)
                body.Append(BuildFilterToggleButton("events", toggles.EventsShown, scenarioLevel: false));
            if (hasSetup)
                body.Append(BuildFilterToggleButton("setup", toggles.SetupShown, scenarioLevel: false));
            body.Append("</span>");"""),
    ("""            diagrams.Any(d => d.CodeBehind.Contains("\\ndatabase \\"") || d.CodeBehind.Contains("\\ncollections \\"")));""",
     """            diagrams.Any(d => d.CodeBehind.Contains("\\ndatabase \\"") || d.CodeBehind.Contains("\\ncollections \\"")));
        // The Events and Setup toggles (5.0.0, D38): a message broker is a participant drawn as a queue; a setup is one the
        // page's stripSetup finds, a Setup frame or a When step bar with the setup before it.
        var hasEventParticipants = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("\\nqueue \\""))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\\nqueue \\"")));
        var hasSetup = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && SetupMarker.IsMatch(l.PlantUml))) ||
            diagrams.Any(d => SetupMarker.IsMatch(d.CodeBehind)));"""),
    ("""    private const string DiagramTabsOpen =""",
     """    /// <summary>
    /// What the page's Setup filter (stripSetup in collapsible-notes-script.js) finds a setup by: the Setup frame the
    /// SeparateSetup option writes, or a When step bar in either of the forms <see cref="StepBarPlantUml"/> writes.
    /// </summary>
    private static readonly Regex SetupMarker = new(
        @"^[ \\t]*(?:partition\\b.*\\bSetup\\r?$|hnote across <<stepDelimiter>>(?: #black:<color:white>|<<stepBody>>: )(?:<b>)?when\\b)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string DiagramTabsOpen ="""),
])

edit(DCM, [
    (""".Replace("__DATABASES_VISIBLE_DEFAULT__", toggleDefaults.DatabasesShown ? "true" : "false")""",
     """.Replace("__DATABASES_VISIBLE_DEFAULT__", toggleDefaults.DatabasesShown ? "true" : "false")
            .Replace("__EVENTS_VISIBLE_DEFAULT__", toggleDefaults.EventsShown ? "true" : "false")
            .Replace("__SETUP_VISIBLE_DEFAULT__", toggleDefaults.SetupShown ? "true" : "false")"""),
])
