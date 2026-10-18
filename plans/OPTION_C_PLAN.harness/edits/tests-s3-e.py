# S3: the last tests of 4.6.0's defaults, for 5.0.0's: the History and Diagnostics tabs on, the scenarios' history off.
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

sub('tests/Kronikol.Tests/ReportConfigurationOptionsDefaultsTests.cs', '''    public void History_and_diagnostics_are_on_the_labs_page_by_default_and_out_of_the_report()
    {
        // plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md: the page is written by default, every view the report can carry
        // is an opt-in that defaults off, and history still reaches the HTML (the page) unless told not to.
        var options = new ReportConfigurationOptions();
        Assert.True(options.GenerateLabsReport);
        Assert.False(options.ShowScenarioHistory);
        Assert.False(options.ShowHistorySection);
        Assert.False(options.ShowReportDiagnosticsSection);
        Assert.True(options.EmbedHistoryInReport);
    }''', '''    public void History_and_diagnostics_are_tabs_of_the_report_by_default_and_out_of_its_features()
    {
        // plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md as 5.0.0 moves it (plans/OPTION_C_PLAN.md S3, the owner's call):
        // the page is written by default, the History and Diagnostics tabs are on (4.6.0 to 4.x: off), the scenarios'
        // own history stays an opt-in, and history still reaches the HTML unless told not to.
        var options = new ReportConfigurationOptions();
        Assert.True(options.GenerateLabsReport);
        Assert.False(options.ShowScenarioHistory);
        Assert.True(options.ShowHistorySection);
        Assert.True(options.ShowReportDiagnosticsSection);
        Assert.True(options.EmbedHistoryInReport);
    }''')

sub('tests/Kronikol.Tests/Reports/Merge/MergeCommandTests.cs', '''            // The merged report is a default report: no history in it (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md).
            // What --history read is on the labs page beside it, named after it.
            Assert.DoesNotContain("history-", File.ReadAllText(output));''', '''            // The merged report is a default report: since 5.0.0 what --history read is its History tab, and nothing of
            // it is in the features (plans/OPTION_C_PLAN.md S3). It is on the labs page beside it too, named after it.
            var merged = File.ReadAllText(output);
            Assert.Contains("<details id=''' + Q + '''history-section''' + Q + ''' class=''' + Q + '''history-section''' + Q + ''' open><summary class=''' + Q + '''h2''' + Q + ''' tabindex=''' + Q + '''-1''' + Q + '''>", merged);
            Assert.DoesNotContain("data-history-verdicts=''' + Q + '''", merged);
            Assert.DoesNotContain("<span class=''' + Q + '''history-sparkline''' + Q + '''", merged);''')

sub('tests/Kronikol.Tests/Reports/Merge/MergedLabsPageTests.cs', '''    public void The_shards_diagnostics_are_listed_on_the_page_beside_the_merged_report()
    {
        Render(Report(Dropped));

        var page = File.ReadAllText(Path.Combine(_dir, "Combined.labs.html"));
        Assert.Contains("<details class=''' + Q + '''report-diagnostics''' + Q + ''' open>", page);
        Assert.Contains("shard 2: a tap dropped 3 segments", page);
        Assert.DoesNotContain("report-diagnostic", File.ReadAllText(Path.Combine(_dir, "Combined.html")));
    }''', '''    public void The_shards_diagnostics_are_listed_on_the_page_beside_the_merged_report_and_in_its_diagnostics_tab()
    {
        Render(Report(Dropped));

        var page = File.ReadAllText(Path.Combine(_dir, "Combined.labs.html"));
        Assert.Contains("<details class=''' + Q + '''report-diagnostics''' + Q + ''' open>", page);
        Assert.Contains("shard 2: a tap dropped 3 segments", page);
        // since 5.0.0 the merged report has its Diagnostics tab by default (plans/OPTION_C_PLAN.md S3)
        var merged = File.ReadAllText(Path.Combine(_dir, "Combined.html"));
        Assert.Contains("id=''' + Q + '''tab-diagnostics''' + Q + '''", merged);
        Assert.Contains("shard 2: a tap dropped 3 segments", merged);
    }

    [Fact]
    public void A_merged_report_told_to_leave_the_diagnostics_out_carries_nothing_of_them()
    {
        Render(Report(Dropped), new ReportConfigurationOptions { ShowReportDiagnosticsSection = false });

        Assert.DoesNotContain("report-diagnostic", File.ReadAllText(Path.Combine(_dir, "Combined.html")));
    }''')

sub('tests/Kronikol.Tests/Reports/ReportGeneratorDiagnosticsScopeTests.cs', '''        // The HTML section is off by default (3.21.0), and since 4.6.0 its stylesheet goes with it, so the whole file
        // is checked. The diagnostic is on the labs page beside the report.
        Assert.DoesNotContain("report-diagnostic", File.ReadAllText(Path.Combine(_dir, "TestRunReport.html")));
        Assert.Contains("<details class=''' + Q + '''report-diagnostics''' + Q + ''' open>", File.ReadAllText(Path.Combine(_dir, "TestRunReport.labs.html")));''',
'''        // Since 5.0.0 the report lists it in its Diagnostics tab by default (plans/OPTION_C_PLAN.md S3; 3.21.0 to 4.x:
        // off). The diagnostic is on the labs page beside the report too.
        var html = File.ReadAllText(Path.Combine(_dir, "TestRunReport.html"));
        Assert.Contains("id=''' + Q + '''tab-diagnostics''' + Q + '''", html);
        Assert.Contains("<details class=''' + Q + '''report-diagnostics''' + Q + ''' open><summary tabindex=''' + Q + '''-1''' + Q + '''>", html);
        Assert.Contains("<details class=''' + Q + '''report-diagnostics''' + Q + ''' open>", File.ReadAllText(Path.Combine(_dir, "TestRunReport.labs.html")));''')
print('ok')
