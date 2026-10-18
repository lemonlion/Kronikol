# S3: the Playwright history tests, for the History tab (5.0.0, R18.1).
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

E = 'tests/Kronikol.Tests.EndToEnd/'
H = E + 'HistorySectionTests.cs'
sub(H, '''        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
    }''', '''        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        // since 5.0.0 the section is the History tab's panel (plans/OPTION_C_PLAN.md S3)
        await OpenReportTabAsync("history");
    }''')
sub(H, '''            (i, run) => i == 3 ? run with { Durations = [.. run.Durations!.Select(d => d * 3)] } : run, wide: true));
        var section = Page.Locator("#history-section");
        await section.WaitForAsync();''', '''            (i, run) => i == 3 ? run with { Durations = [.. run.Durations!.Select(d => d * 3)] } : run, wide: true));
        await OpenReportTabAsync("history");
        var section = Page.Locator("#history-section");
        await section.WaitForAsync();''')

X = E + 'HistoryExportTests.cs'
sub(X, '''        await Page.Locator("details.feature").First.WaitForAsync();
        await Expect(Page.Locator("#history-section")).ToBeVisibleAsync();
''', '''        await Page.Locator("details.feature").First.WaitForAsync();
        // the section is the History tab's panel since 5.0.0; the export copies what the Features tab shows
        await OpenReportTabAsync("history");
        await Expect(Page.Locator("#history-section")).ToBeVisibleAsync();
        await OpenReportTabAsync("features");
''')

D = E + 'DefaultReportWithoutHistoryTests.cs'
sub(D, '''/// <summary>
/// A default report from a run that read a ledger (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md): nothing about history
/// is painted, a verdict query typed in the search box matches nothing without an error, and Export Filtered HTML
/// carries no history. The history is on the labs page beside it (<see cref="LabsPageTests"/>).
/// </summary>''', '''/// <summary>
/// A default report from a run that read a ledger (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md, as 5.0.0 moves it:
/// plans/OPTION_C_PLAN.md S3, R18.1): its history is the History tab and nothing of it is painted in the features,
/// a verdict query typed in the search box matches nothing without an error, and Export Filtered HTML carries no
/// history. The history is on the labs page beside it too (<see cref="LabsPageTests"/>).
/// </summary>''')
sub(D, '''        await Expect(Page.Locator("details.scenario")).ToHaveCountAsync(4);
        await Expect(Page.Locator(".history-sparkline")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".history-verdict")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-history-verdicts]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#history-section")).ToHaveCountAsync(0);''', '''        await Expect(Page.Locator("details.scenario")).ToHaveCountAsync(4);
        // the features: the History tab's own lists carry verdict pills, the scenarios none
        await Expect(Page.Locator("#report-content .history-sparkline")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#report-content .history-verdict")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-history-verdicts]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#panel-history > #history-section")).ToHaveCountAsync(1);''')
sub(D, '''        Assert.Contains("data-stable-id=''' + Q + '''", exported);
        Assert.DoesNotContain("history-", exported);
        Assert.DoesNotContain("data-history-verdicts", exported);''', '''        Assert.Contains("data-stable-id=''' + Q + '''", exported);
        // the features alone: no History section, no sparkline, no verdict. The head it copies is the report's, whose
        // stylesheet names the History tab's classes since 5.0.0, so the markup is what is checked.
        Assert.DoesNotContain("<details id=''' + Q + '''history-section''' + Q + '''", exported);
        Assert.DoesNotContain("<span class=''' + Q + '''history-sparkline''' + Q + '''", exported);
        Assert.DoesNotContain("<span class=''' + Q + '''history-verdict", exported);
        Assert.DoesNotContain("data-history-verdicts", exported);''')

W = E + 'ReportSectionWidthTests.cs'
sub(W, '''        var el = Page.Locator(selector, new() { HasTextString = text }).First;
        await el.ScrollIntoViewIfNeededAsync();

        // The right edge of its text, the content edge of the box holding it, and the lines it takes.''', '''        var el = Page.Locator(selector, new() { HasTextString = text }).First;
        // since 5.0.0 a section beside the features is a tab: open the one that holds it (plans/OPTION_C_PLAN.md S3)
        var tab = await el.EvaluateAsync<string?>("e => { const p = e.closest('.report-panel'); return p && p.hidden ? p.dataset.panel : null; }");
        if (tab is not null) await OpenReportTabAsync(tab);
        await el.ScrollIntoViewIfNeededAsync();

        // The right edge of its text, the content edge of the box holding it, and the lines it takes.''')
print('ok')
