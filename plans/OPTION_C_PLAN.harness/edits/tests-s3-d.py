# S3: HistoryOutputsTests, for 5.0.0's default: the History tab on, the scenarios' own history off.
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

P = 'tests/Kronikol.Tests/History/HistoryOutputsTests.cs'
sub(P, '''    public void A_default_report_carries_no_history_and_each_opt_in_brings_back_its_own()
    {
        Run("h1", "test:1:1", Features(ExecutionResult.Passed), o => o.GenerateTestRunReport = true);
        Run("h2", "test:2:1", Features(ExecutionResult.Failed), o => o.GenerateTestRunReport = true);
        Run("h3", "test:3:1", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.ShowHistorySection = true; });
        Run("h4", "test:4:1", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.ShowScenarioHistory = true; });

        // A run that broke a scenario has as much to say as history ever has, and a default report says none of it
        // (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md): the whole file, since the stylesheet and the scripts no longer
        // name the classes either. It is on the labs page beside the report.
        var byDefault = File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.html"));
        Assert.DoesNotContain("history-", byDefault);
        Assert.Contains("<span class=''' + Q + '''history-verdict history-verdict-broke''' + Q + '''", File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.labs.html")));

        var section = File.ReadAllText(Path.Combine(Reports("h3"), "TestRunReport.html"));
        Assert.Contains("<details id=''' + Q + '''history-section''' + Q + '''", section);
        Assert.Contains("<summary class=''' + Q + '''h2''' + Q + '''>History ", section);
        Assert.DoesNotContain("data-history-verdicts=''' + Q + '''", section);
        Assert.DoesNotContain("<span class=''' + Q + '''history-sparkline''' + Q + '''", section);

        var scenario = File.ReadAllText(Path.Combine(Reports("h4"), "TestRunReport.html"));
        // The fourth run of P, F, F, F: the scenario is failing, no longer newly broken.
        Assert.Contains("data-history-verdicts=''' + Q + '''failing''' + Q + '''", scenario);
        Assert.Contains("<span class=''' + Q + '''history-sparkline''' + Q + '''", scenario);
        Assert.DoesNotContain("<details id=''' + Q + '''history-section''' + Q + '''", scenario);
    }''', '''    public void A_default_report_has_the_history_tab_and_nothing_of_history_in_its_features_and_each_option_brings_its_own()
    {
        Run("h1", "test:1:1", Features(ExecutionResult.Passed), o => o.GenerateTestRunReport = true);
        Run("h2", "test:2:1", Features(ExecutionResult.Failed), o => o.GenerateTestRunReport = true);
        Run("h3", "test:3:1", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.ShowHistorySection = false; });
        Run("h4", "test:4:1", Features(ExecutionResult.Failed), o => { o.GenerateTestRunReport = true; o.ShowHistorySection = false; o.ShowScenarioHistory = true; });

        // A run that broke a scenario has as much to say as history ever has. Since 5.0.0 a default report says it in
        // its History tab (plans/OPTION_C_PLAN.md S3), and nothing of it is in the features: no verdict attribute, no
        // sparkline (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md). The labs page beside the report carries both.
        var byDefault = File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.html"));
        Assert.Contains("data-panel=''' + Q + '''history''' + Q + ''' hidden><details id=''' + Q + '''history-section''' + Q + ''' class=''' + Q + '''history-section''' + Q + ''' open><summary class=''' + Q + '''h2''' + Q + ''' tabindex=''' + Q + '''-1''' + Q + '''>History ", byDefault);
        Assert.DoesNotContain("data-history-verdicts=''' + Q + '''", byDefault);
        Assert.DoesNotContain("<span class=''' + Q + '''history-sparkline''' + Q + '''", byDefault);
        Assert.Contains("<span class=''' + Q + '''history-verdict history-verdict-broke''' + Q + '''", File.ReadAllText(Path.Combine(Reports("h2"), "TestRunReport.labs.html")));

        // With the tab turned off, a report says none of it: the whole file, since the stylesheet and the scripts do
        // not name the classes either.
        Assert.DoesNotContain("history-", File.ReadAllText(Path.Combine(Reports("h3"), "TestRunReport.html")));

        var scenario = File.ReadAllText(Path.Combine(Reports("h4"), "TestRunReport.html"));
        // The fourth run of P, F, F, F: the scenario is failing, no longer newly broken.
        Assert.Contains("data-history-verdicts=''' + Q + '''failing''' + Q + '''", scenario);
        Assert.Contains("<span class=''' + Q + '''history-sparkline''' + Q + '''", scenario);
        Assert.DoesNotContain("<details id=''' + Q + '''history-section''' + Q + '''", scenario);
    }''')
print('ok')
