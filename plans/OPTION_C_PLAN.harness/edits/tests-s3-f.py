# S3: the Playwright tests that clicked the Scenario Timeline and Component Diagram toggles open the tabs instead.
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

E = 'tests/Kronikol.Tests.EndToEnd/'
TAB = ' // 5.0.0: a tab of the report bar (plans/OPTION_C_PLAN.md S3)'
sub(E + 'ComponentDiagramDatabaseToggleTests.cs', '''        await Page.Locator("button[onclick*='toggle_component_diagram']").ClickAsync();''',
    '''        await OpenReportTabAsync("components");''' + TAB)
sub(E + 'DurationPaintTests.cs', '''        await Page.Locator(".timeline-toggle").ClickAsync();''', '''        await OpenReportTabAsync("timeline");''' + TAB)
sub(E + 'WarmUpMarkTests.cs', '''        await Page.Locator(".timeline-toggle").ClickAsync();''', '''        await OpenReportTabAsync("timeline");''' + TAB)
sub(E + 'ReportToolbarTests.cs', '''        await Page.Locator(".timeline-toggle").ClickAsync();''', '''        await OpenReportTabAsync("timeline");''' + TAB)
sub(E + 'OnDemandRenderingTests.cs', '''        await Page.Locator("button[onclick*='toggle_component_diagram']").First.ClickAsync();''',
    '''        await OpenReportTabAsync("components");''' + TAB)
sub(E + 'MergedReportTests.cs', '''        var button = Page.Locator("button[onclick*='toggle_component_diagram']");
        await button.WaitForAsync(new() { Timeout = 5000 });
        await button.ClickAsync();''', '''        await OpenReportTabAsync("components");''' + TAB)
sub(E + 'WikiGifTests.cs', '''        await JsClick(".timeline-toggle");''', '''        await JsClick("#tab-timeline");''' + TAB)

V = E + 'ViewportSweepTests.cs'
sub(V, '''            const lines = el => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };''',
    '''            // a line is a run of boxes that overlap vertically: a child in its own font (a tab's count) is a box of its own
            // on the same line, so counting boxes counted it as a line
            const lines = el => {
                const r = document.createRange(); r.selectNodeContents(el);
                const boxes = [...r.getClientRects()].filter(b => b.width > 0 && b.height > 0).sort((a, b) => a.top - b.top);
                let n = 0, bottom = -Infinity;
                for (const b of boxes) { if (b.top >= bottom - 1) { n++; bottom = b.bottom; } else bottom = Math.max(bottom, b.bottom); }
                return n;
            };''')
sub(V, '''            var timeline = page.Locator("button.timeline-toggle", new() { HasTextString = "Scenario Timeline" });
            if (await timeline.CountAsync() > 0) await timeline.First.ClickAsync();''', '''            // Since 5.0.0 the sections are tabs, one panel shown at a time (plans/OPTION_C_PLAN.md S3); the sweep shows
            // them all at once, as 4.x drew them, so every panel's content is measured at every width.
            await page.EvaluateAsync("() => document.querySelectorAll('.report-panel[hidden]').forEach(p => { p.hidden = false; })");''')
print('ok')
