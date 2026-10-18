# S3: history and diagnostics back in the default report, in their tabs and not in the features (the owner, 2026-10-18:
# "in this version, history and diagnostics is to be brought back, in those tabs (not as part of the rest of the report
# though)"). The clean-report guards keep their strength for a run that read and recorded nothing.
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

D = 'tests/Kronikol.Tests/Reports/DefaultReportCleanTests.cs'
sub(D, '''/// <summary>
/// The guards of plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 7.3: with default options a report carries no
/// history and no report diagnostics at all (markup, stylesheet rules or verdict search code), so a run that read a
/// ledger and recorded diagnostics writes the same <c>TestRunReport.html</c> as one that did neither; and each opt-in
/// brings back its own pieces and no others. Each run names its ledger, because the test process has
/// <c>KRONIKOL_HISTORY=off</c>.
/// </summary>''', '''/// <summary>
/// The guards of plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 7.3, as 5.0.0 moves them (plans/OPTION_C_PLAN.md
/// S3, the owner's call): by default a run's history and its report diagnostics are the report's History and
/// Diagnostics tabs, and nothing of either is in the features; a run that read no history and recorded no diagnostic
/// carries neither anywhere in the file (markup, stylesheet rules or verdict search code); and each option brings or
/// keeps out its own pieces and no others. Each run names its ledger, because the test process has
/// <c>KRONIKOL_HISTORY=off</c>.
/// </summary>''')

sub(D, '''    [Fact]
    public void By_default_a_run_that_read_history_and_recorded_diagnostics_writes_the_same_reports_as_one_that_did_neither()
    {
        Run("plain", ledger: false);
        Seed();
        Run("full", ledger: true, diagnostics: 2);

        // The run did read history and record its diagnostics: they are on the labs page beside the report.
        Assert.Contains("<details id=''' + Q + '''history-section''' + Q + '''", Report("full", "TestRunReport.labs.html"));
        Assert.Contains("a tap dropped 4 segments", Report("full", "TestRunReport.labs.html"));

        Assert.Equal(Report("plain"), Report("full"));
        Assert.Equal(Report("plain", "Specifications.html"), Report("full", "Specifications.html"));
    }

    [Fact]
    public void A_default_report_names_no_history_or_diagnostics_anywhere_in_the_file()
    {
        Seed();
        Run("full", ledger: true, diagnostics: 2);

        // The whole file, scripts and styles included. Not `history`: the scripts call history.replaceState.
        var html = Report("full");
        Assert.DoesNotContain("history-", html);
        Assert.DoesNotContain("data-history-verdicts", html);
        Assert.DoesNotContain("report-diagnostic", html);
        Assert.DoesNotContain("verdicts.has(", html);
    }''', '''    [Fact]
    public void By_default_a_run_that_read_history_and_recorded_diagnostics_shows_them_in_their_tabs_and_not_in_its_features()
    {
        Run("plain", ledger: false);
        Seed();
        Run("full", ledger: true, diagnostics: 2);

        var html = Report("full");
        Assert.Contains("id=''' + Q + '''tab-history''' + Q + '''", html);
        Assert.Contains("<div class=''' + Q + '''report-panel''' + Q + ''' id=''' + Q + '''panel-history''' + Q + ''' role=''' + Q + '''tabpanel''' + Q + ''' aria-labelledby=''' + Q + '''tab-history''' + Q + ''' data-panel=''' + Q + '''history''' + Q + ''' hidden><details id=''' + Q + '''history-section''' + Q + '''", html);
        Assert.Contains("id=''' + Q + '''tab-diagnostics''' + Q + '''", html);
        Assert.Contains(">Report diagnostics (", html);
        Assert.Contains("a tap dropped 4 segments", html);
        // nothing of history is in the features: no sparkline, no verdict pill or attribute, no verdict search code
        Assert.DoesNotContain("<span class=''' + Q + '''history-sparkline''' + Q + '''", html);
        Assert.DoesNotContain("data-history-verdicts=''' + Q + '''", html);
        Assert.DoesNotContain("verdicts.has(", html);
        // the labs page beside the report still carries both
        Assert.Contains("<details id=''' + Q + '''history-section''' + Q + '''", Report("full", "TestRunReport.labs.html"));
        Assert.Contains("a tap dropped 4 segments", Report("full", "TestRunReport.labs.html"));
        // and the Specifications report, which has no run data, is the same either way
        Assert.Equal(Report("plain", "Specifications.html"), Report("full", "Specifications.html"));
    }

    [Fact]
    public void A_run_that_read_no_history_and_recorded_no_diagnostic_names_neither_anywhere_in_the_file()
    {
        Run("plain", ledger: false);

        // The whole file, scripts and styles included. Not `history`: the scripts call history.replaceState.
        var html = Report("plain");
        Assert.DoesNotContain("history-", html);
        Assert.DoesNotContain("data-history-verdicts", html);
        Assert.DoesNotContain("report-diagnostic", html);
        Assert.DoesNotContain("verdicts.has(", html);
    }

    [Fact]
    public void Turned_off_the_history_and_diagnostics_tabs_leave_no_trace_of_either()
    {
        Seed();
        Run("off", ledger: true, diagnostics: 2, configure: o =>
        {
            o.ShowHistorySection = false;
            o.ShowReportDiagnosticsSection = false;
        });

        var html = Report("off");
        Assert.DoesNotContain("history-", html);
        Assert.DoesNotContain("report-diagnostic", html);
        Assert.DoesNotContain("id=''' + Q + '''tab-history''' + Q + '''", html);
        Assert.DoesNotContain("id=''' + Q + '''tab-diagnostics''' + Q + '''", html);
    }''')

sub(D, '''        Run("scenario", ledger: true, diagnostics: 2, configure: o => o.ShowScenarioHistory = true);''',
    '''        Run("scenario", ledger: true, diagnostics: 2, configure: o =>
        {
            o.ShowScenarioHistory = true;
            // alone: since 5.0.0 the two tabs are on by default
            o.ShowHistorySection = false;
            o.ShowReportDiagnosticsSection = false;
        });''')
sub(D, '''        Run("diagnostics", ledger: true, diagnostics: 2, configure: o => o.ShowReportDiagnosticsSection = true);''',
    '''        Run("diagnostics", ledger: true, diagnostics: 2, configure: o =>
        {
            o.ShowReportDiagnosticsSection = true;
            o.ShowHistorySection = false; // on by default since 5.0.0
        });''')
print('ok')
