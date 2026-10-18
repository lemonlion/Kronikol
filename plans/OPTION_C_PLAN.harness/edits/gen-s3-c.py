# S3: the comments, docs and the ingest CLI follow the new defaults (History and Diagnostics tabs on).
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

G = 'src/Kronikol/Reports/ReportGenerator.cs'
sub(G, '''        // Off unless asked for (ReportConfigurationOptions.ShowReportDiagnosticsSection): on a healthy run
        // the section is a line of noise above the features, and nothing is lost by leaving it out — every
        // diagnostic is on the labs page beside the report, in TestRunReport.json's diagnostics array and in the
        // run's own result either way.
        // In its tab the section is always open: the tab is its disclosure, so DiagnosticsOpen has nothing left to open.''',
'''        // The Diagnostics tab (ReportConfigurationOptions.ShowReportDiagnosticsSection, on by default since 5.0.0): a
        // healthy run has nothing to list and no tab, and every diagnostic is also on the labs page beside the report,
        // in TestRunReport.json's diagnostics array and in the run's own result. In its tab the section is always
        // open: the tab is its disclosure, so DiagnosticsOpen has nothing left to open.''')
sub(G, '''        // Each part is off unless asked for (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md): the section by
        // ReportConfigurationOptions.ShowHistorySection, the sparklines, pills and verdict attributes by
        // ShowScenarioHistory. A default report carries neither; the labs page beside it carries both.''',
'''        // The section is the History tab (ReportConfigurationOptions.ShowHistorySection, on by default since 5.0.0);
        // the sparklines, pills and verdict attributes in the features stay off unless ShowScenarioHistory asks
        // (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md). The labs page beside the report carries both.''')
sub(G, '''    /// Drawn open on the labs page (<see cref="LabsReportGenerator"/>), and in <c>TestRunReport.html</c> when
    /// <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> asks for it, collapsed unless
    /// <see cref="ReportToggleDefaults.DiagnosticsOpen"/> says otherwise. Empty input renders nothing.''',
'''    /// Drawn open on the labs page (<see cref="LabsReportGenerator"/>), and in <c>TestRunReport.html</c> as its
    /// Diagnostics tab when <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> asks for it (the
    /// default since 5.0.0), open with its summary out of the tab order (<paramref name="inTab"/>). Empty input
    /// renders nothing.''')

T = 'src/Kronikol/Reports/ReportToggleDefaults.cs'
sub(T, '''    /// <summary>The report diagnostics disclosure starts open (test run report only). Default: <c>false</c>.
    /// Inert unless <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> renders the section,
    /// which it does not by default. The labs page's list is always open.</summary>''',
'''    /// <summary>The report diagnostics disclosure starts open (test run report only). Default: <c>false</c>.
    /// Since 5.0.0 the section is the report's Diagnostics tab, always open in it, so this has nothing to open there
    /// (plans/OPTION_C_PLAN.md S3). The labs page's list is always open.</summary>''')
sub(T, '''    /// <summary>The Scenario Timeline panel starts visible. Default: <c>false</c>. When both this and
    /// <see cref="ComponentDiagramVisible"/> are set, the timeline wins (the panels are mutually exclusive).</summary>''',
'''    /// <summary>The page opens on the Timeline tab, where the run has durations (since 5.0.0 the Scenario Timeline is a
    /// tab of the report bar; before, a panel that started visible). Default: <c>false</c>, the Features tab. When
    /// both this and <see cref="ComponentDiagramVisible"/> are set, the timeline wins.</summary>''')
sub(T, '''    /// <summary>The embedded Component Diagram panel starts visible (test run report only). Default: <c>false</c>.</summary>''',
'''    /// <summary>The page opens on the Components tab, where the report embeds a component diagram (test run report only;
    /// since 5.0.0 a tab of the report bar, before a panel that started visible). Default: <c>false</c>.</summary>''')

R = 'src/Kronikol/Reports/ReportToggleDefaultsResolver.cs'
sub(R, '''    /// <summary>Whether the report's Report diagnostics section starts open (<see cref="ReportToggleDefaults.DiagnosticsOpen"/>);
    /// the section is drawn only when <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> asks for it.</summary>''',
'''    /// <summary>Whether the report's Report diagnostics section starts open (<see cref="ReportToggleDefaults.DiagnosticsOpen"/>);
    /// since 5.0.0 the section is its Diagnostics tab, open in it, so the report has nothing to open with it.</summary>''')

sub(G, '''    /// and <c>data-history-verdicts</c> attribute, as they did before 4.6.0, and the History section with
    /// <paramref name="showHistorySection"/>; <paramref name="diagnostics"/> draw the Report diagnostics section with
    /// <paramref name="showReportDiagnostics"/>.''', '''    /// and <c>data-history-verdicts</c> attribute, as they did before 4.6.0, and the History tab with
    /// <paramref name="showHistorySection"/>; <paramref name="diagnostics"/> draw the Diagnostics tab with
    /// <paramref name="showReportDiagnostics"/>.''')

H = 'src/Kronikol/History/HistoryHtml.cs'
sub(H, '''/// and the History section, with the run trend and the lists of what changed. Since 4.6.0 the labs page beside
/// the report (<see cref="Reports.LabsReportGenerator"/>) draws them by default, and a report draws them only
/// where its opt-ins ask (<see cref="ReportConfigurationOptions.ShowScenarioHistory"/>,
/// <see cref="ReportConfigurationOptions.ShowHistorySection"/>), through these same renderers, so the two
/// cannot drift (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md).''', '''/// and the History section, with the run trend and the lists of what changed. The labs page beside the report
/// (<see cref="Reports.LabsReportGenerator"/>) draws them all; the report draws the section as its History tab
/// (<see cref="ReportConfigurationOptions.ShowHistorySection"/>, on by default since 5.0.0) and the scenarios'
/// own history only where <see cref="ReportConfigurationOptions.ShowScenarioHistory"/> asks, through these same
/// renderers, so the two cannot drift (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md).''')

I = 'src/Kronikol/Ingestion/IngestPipeline.cs'
sub(I, '''    /// <see cref="ReportConfigurationOptions.GenerateLabsReport"/> is off), and into the "Report diagnostics" section
    /// of <c>TestRunReport.html</c> when <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> asks
    /// for that section.
    /// Default empty — nothing changes when a host hands in nothing.
    /// <c>kronikol ingest --diagnostic "&lt;kind&gt;:&lt;message&gt;" --diagnostics-section</c> is the CLI form.''',
'''    /// <see cref="ReportConfigurationOptions.GenerateLabsReport"/> is off), and into the Diagnostics tab of
    /// <c>TestRunReport.html</c> unless <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> is off.
    /// Default empty: nothing changes when a host hands in nothing.
    /// <c>kronikol ingest --diagnostic "&lt;kind&gt;:&lt;message&gt;"</c> is the CLI form.''')

M = 'src/Kronikol/Reports/Merge/MergeableReportRenderer.cs'
sub(M, '''    /// and what the report and the labs page carry, as a run's options do: the history and diagnostics views are in the
    /// report only where <see cref="ReportConfigurationOptions.ShowScenarioHistory"/>,
    /// <see cref="ReportConfigurationOptions.ShowHistorySection"/> and
    /// <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> ask, and on the labs page,''',
'''    /// and what the report and the labs page carry, as a run's options do: the History and Diagnostics tabs where
    /// <see cref="ReportConfigurationOptions.ShowHistorySection"/> and
    /// <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> ask (both on by default since 5.0.0), the
    /// scenarios' own history where <see cref="ReportConfigurationOptions.ShowScenarioHistory"/> asks, and on the labs page,''')

C = 'src/Kronikol.Tool/IngestCommand.cs'
sub(C, '''        var diagnosticsSection = false;''', '''        var diagnosticsSection = true;''')
sub(C, '''                case "--diagnostics-section":
                    diagnosticsSection = true;
                    break;''', '''                case "--diagnostics-section":
                    // the default since 5.0.0, still taken so a command line written for 4.x runs unchanged
                    diagnosticsSection = true;
                    break;
                case "--no-diagnostics-section":
                    diagnosticsSection = false;
                    break;''')
sub(C, '''        w.WriteLine("                           (TestRunReport.labs.html), and in the HTML report's \\"Report diagnostics\\"");
        w.WriteLine("                           section when --diagnostics-section asks for it.");
        w.WriteLine("  --diagnostics-section    Also render the \\"Report diagnostics\\" section in the HTML report. Off by default:");
        w.WriteLine("                           every diagnostic is on the labs page, in TestRunReport.json and printed here.");''',
'''        w.WriteLine("                           (TestRunReport.labs.html), and in the HTML report's Diagnostics tab.");
        w.WriteLine("  --no-diagnostics-section Leave the Diagnostics tab out of the HTML report: every diagnostic is still on the");
        w.WriteLine("                           labs page, in TestRunReport.json and printed here. --diagnostics-section, the");
        w.WriteLine("                           default since 5.0.0, is still taken.");''')
print('ok')
