# S3: the tests of the ingest CLI, the in-tab summaries, the timeline and component tabs and the merge render.
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

I = 'tests/Kronikol.Tests/Tool/IngestCommandTests.cs'
sub(I, '''        // The HTML section is off unless the run asks for it; the data file carries the diagnostic either way, and the
        // labs page beside the report lists it, named by the pointer after the report.
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.DoesNotContain("<summary>Report diagnostics (", html);
        Assert.DoesNotContain("tap-di-redis: decoding disabled on 1 connection(s)", html);''', '''        // Since 5.0.0 the HTML report lists it in its Diagnostics tab (plans/OPTION_C_PLAN.md S3); the data file carries
        // the diagnostic either way, and the labs page beside the report lists it, named by the pointer after the report.
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.Contains("id=''' + Q + '''tab-diagnostics''' + Q + '''", html);
        Assert.Contains(">Report diagnostics (", html);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", html);''')
sub(I, '''        var shown = Path.Combine(_dir, "shown");
        Assert.Equal(0, IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", shown, "--diagnostics-section",
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
        ], new StringWriter(), new StringWriter()));
        var shownHtml = File.ReadAllText(Path.Combine(shown, "TestRunReport.html"));
        Assert.Contains("<summary>Report diagnostics (", shownHtml);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", shownHtml);

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--diagnostic <kind>:<msg>", usage.ToString());
        Assert.Contains("--diagnostics-section", usage.ToString());''', '''        // --no-diagnostics-section leaves the tab out, and nothing of it is in the file
        var hidden = Path.Combine(_dir, "hidden");
        Assert.Equal(0, IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", hidden, "--no-diagnostics-section",
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
        ], new StringWriter(), new StringWriter()));
        var hiddenHtml = File.ReadAllText(Path.Combine(hidden, "TestRunReport.html"));
        Assert.DoesNotContain("report-diagnostic", hiddenHtml);
        Assert.DoesNotContain("tap-di-redis: decoding disabled on 1 connection(s)", hiddenHtml);

        // a 4.x command line that asked for the section runs unchanged and has it
        var shown = Path.Combine(_dir, "shown");
        Assert.Equal(0, IngestCommand.Run(
        [
            captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", shown, "--diagnostics-section",
            "--diagnostic", "CaptureDegraded:tap-di-redis: decoding disabled on 1 connection(s)",
        ], new StringWriter(), new StringWriter()));
        var shownHtml = File.ReadAllText(Path.Combine(shown, "TestRunReport.html"));
        Assert.Contains(">Report diagnostics (", shownHtml);
        Assert.Contains("tap-di-redis: decoding disabled on 1 connection(s)", shownHtml);

        var usage = new StringWriter();
        IngestCommand.PrintUsage(usage);
        Assert.Contains("--diagnostic <kind>:<msg>", usage.ToString());
        Assert.Contains("--no-diagnostics-section", usage.ToString());''')

H = 'tests/Kronikol.Tests/Ingestion/IngestHostDiagnosticsTests.cs'
sub(H, '''        Assert.Contains("<summary>Report diagnostics (", html);''', '''        Assert.Contains(">Report diagnostics (", html); // the summary in its tab (5.0.0) has tabindex="-1"''')
sub(H, '''        Assert.DoesNotContain("<summary>Report diagnostics (", html);''', '''        Assert.DoesNotContain(">Report diagnostics (", html);''')

O = 'tests/Kronikol.Tests/Reports/OnDemandRenderingReportTests.cs'
sub(O, '''    /// <summary>The component panel's markup, up to the report's content.</summary>
    private static string Panel(string html)
    {
        var start = html.IndexOf("<div id=''' + Q + '''component-diagram''' + Q + '''", StringComparison.Ordinal);
        Assert.True(start >= 0, "the report has no component panel");
        var end = html.IndexOf("<div id=''' + Q + '''report-content''' + Q + '''>", start, StringComparison.Ordinal);
        return html[start..end];
    }''', '''    /// <summary>The component panel's markup, up to the next tab's panel or, when it is the last, the page's last button
    /// (since 5.0.0 the Components tab's panel follows the features, plans/OPTION_C_PLAN.md S3).</summary>
    private static string Panel(string html)
    {
        var start = html.IndexOf("<div id=''' + Q + '''component-diagram''' + Q + '''", StringComparison.Ordinal);
        Assert.True(start >= 0, "the report has no component panel");
        var next = html.IndexOf("<div class=''' + Q + '''report-panel''' + Q + '''", start, StringComparison.Ordinal);
        var end = next >= 0 ? next : html.IndexOf("class=''' + Q + '''back-to-top''' + Q + '''", start, StringComparison.Ordinal);
        Assert.True(end > start, "nothing follows the component panel");
        return html[start..end];
    }''')

T = 'tests/Kronikol.Tests/Reports/ScenarioTimelineReportTests.cs'
sub(T, '''    public void Report_with_durations_contains_timeline_toggle_button()''', '''    public void Report_with_durations_has_a_timeline_tab()''')
sub(T, '''        var content = GenerateReport(features, "TimelineToggle.html");
        Assert.Contains("timeline-toggle", content);''', '''        var content = GenerateReport(features, "TimelineToggle.html");
        // 5.0.0 (plans/OPTION_C_PLAN.md S3): a tab of the report bar in place of the toolbar's toggle
        Assert.Contains("<button class=''' + Q + '''report-tab''' + Q + ''' id=''' + Q + '''tab-timeline''' + Q + '''", content);''')
sub(T, '''        Assert.DoesNotContain("onclick=''' + Q + '''toggle_timeline", content);''', '''        Assert.DoesNotContain("onclick=''' + Q + '''toggle_timeline", content);
        Assert.DoesNotContain("id=''' + Q + '''tab-timeline''' + Q + '''", content);''')
sub(T, '''    public void Report_timeline_toggle_function_exists()''', '''    public void Report_timeline_is_its_tabs_panel_shown_by_the_tabs_script()''')
sub(T, '''        var content = GenerateReport(features, "TimelineToggleJs.html");
        Assert.Contains("toggle_timeline", content);''', '''        var content = GenerateReport(features, "TimelineToggleJs.html");
        Assert.Contains("data-panel=''' + Q + '''timeline''' + Q + ''' hidden><div id=''' + Q + '''scenario-timeline''' + Q + '''", content);
        Assert.Contains("<script id=''' + Q + '''kronikol-report-tabs''' + Q + '''>", content);''')

M = 'tests/Kronikol.Tests/Reports/Merge/MergeableReportTests.cs'
sub(M, '''        // Component diagram embedded (toggle button rendered when component PlantUML is supplied).
        Assert.Contains("toggle_component_diagram", html);''', '''        // Component diagram embedded (since 5.0.0 its tab, rendered when component PlantUML is supplied).
        Assert.Contains("<button class=''' + Q + '''report-tab''' + Q + ''' id=''' + Q + '''tab-components''' + Q + '''", html);''')
print('ok')
