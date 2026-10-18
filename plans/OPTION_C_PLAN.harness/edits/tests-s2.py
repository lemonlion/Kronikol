# S2: the tests that pinned the 4.x header row, rewritten for the run band (each says why). Run from the worktree root.
import sys

def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:80])
    open(path, 'wb').write(s.replace(old, new).encode())

C = 'tests/Kronikol.Tests/Reports/'
E = 'tests/Kronikol.Tests.EndToEnd/'

# CiMetadataReportTests: the CI zone's labels lost their colons and "#" moved to the value (5.0.0, the run band)
sub(C + 'CiMetadataReportTests.cs', '''        Assert.Contains("Build #:", content);
        Assert.Contains("42", content);
        Assert.Contains("Branch:", content);
        Assert.Contains("main", content);''', '''        // 5.0.0: the run band's CI zone (plans/OPTION_C_PLAN.md S2) in place of the 4.x table
        Assert.Contains("<div class=\\"run-stat\\"><dt>Build</dt><dd>#42</dd></div>", content);
        Assert.Contains("<div class=\\"run-stat\\"><dt>Branch</dt><dd>main</dd></div>", content);''')
sub(C + 'CiMetadataReportTests.cs', '''        Assert.Contains("<a href=\\"https://github.com/owner/repo/actions/runs/12345\\"", content);
        Assert.Contains("Pipeline", content);''', '''        Assert.Contains("<a class=\\"run-link\\" href=\\"https://github.com/owner/repo/actions/runs/12345\\"", content);
        Assert.Contains("<dt>Run</dt>", content);''')
sub(C + 'CiMetadataReportTests.cs', '''        Assert.DoesNotContain("Build #:", content);
        Assert.DoesNotContain("Pipeline", content);''', '''        Assert.DoesNotContain("run-ci", content);
        Assert.DoesNotContain("<dt>Build</dt>", content);''')

# CultureInvariantOutputTests: the run's start and end are written in the band's one line
sub(C + 'CultureInvariantOutputTests.cs', '''        Assert.Contains("<tr><td>Start Date:</td><td>2026-01-01 (UTC)</td>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>Start Time:</td><td>10:05:07 (UTC)</td>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>End Time:</td><td>10:07:07 (UTC)</td>", html, StringComparison.Ordinal);''',
'''        // 5.0.0: one line in the run band (plans/OPTION_C_PLAN.md S2) in place of the 4.x table's three rows
        Assert.Contains("<span class=\\"run-when\\">2026-01-01 \\u00b7 10:05:07\\u201310:07:07 UTC</span>", html, StringComparison.Ordinal);''')

# RunIdentityDataTests: a local run draws no CI zone
sub(C + 'RunIdentityDataTests.cs', '''        // Anchored on the emitted markup, not on the class name: the stylesheet declares
        // `.ci-metadata` in every report whether or not the table is drawn.
        var html = File.ReadAllText(path);
        Assert.DoesNotContain("<div class=\\"ci-metadata\\">", html);
        Assert.DoesNotContain("<div class=\\"ci-chart-group\\">", html);
        Assert.DoesNotContain("CI (None)", html);''', '''        // Anchored on the emitted markup, not on the class name: the stylesheet declares
        // `.run-ci` in every report whether or not the zone is drawn (5.0.0: the run band's CI zone).
        var html = File.ReadAllText(path);
        Assert.DoesNotContain("<div class=\\"run-zone run-ci\\">", html);
        Assert.DoesNotContain("<h2 class=\\"run-title\\">CI</h2>", html);
        Assert.DoesNotContain(">None</span>", html);''')

# PieChartReportTests: the ring sits in the band, coloured by classes the tokens fill
s = open(C + 'PieChartReportTests.cs', 'rb').read().decode()
s = s.replace('Assert.Contains("summary-chart", content);', 'Assert.Contains("<div class=\\"run-ring\\">", content);')
s = s.replace('content.IndexOf("<div class=\\"summary-chart\\"")', 'content.IndexOf("<div class=\\"run-ring\\"")')
s = s.replace('Assert.DoesNotContain("<div class=\\"summary-chart\\"", content);', 'Assert.DoesNotContain("<div class=\\"run-ring\\"", content);')
s = s.replace('Assert.Contains("#1daf26", svgContent); // green for passed', 'Assert.Contains("class=\\"ring-pass\\"", svgContent); // passed, coloured by the token (5.0.0)')
s = s.replace('Assert.DoesNotContain("#cc0000", svgContent); // no red for failed', 'Assert.DoesNotContain("class=\\"ring-fail\\"", svgContent); // no failed arc')
open(C + 'PieChartReportTests.cs', 'wb').write(s.encode())

# StylesheetRulesTests: the header row's and CI table's rules are gone with them
sub(C + 'StylesheetRulesTests.cs', '''    [Fact]
    public void Between_the_phone_layout_and_the_breakpoint_the_filtering_box_takes_its_own_row()
    {
        Assert.Equal("wrap", CssRules.Value(Base, ".header-row", "flex-wrap", Band));
        Assert.Equal("100%", CssRules.Value(Base, ".filtering-box", "flex-basis", Band));
    }

    [Fact]
    public void The_ci_box_is_capped_in_the_row_layout_and_breaks_its_values_not_its_labels()
    {
        Assert.Equal("100%", CssRules.Value(Base, ".ci-metadata table", "max-width"));
        Assert.Equal("nowrap", CssRules.Value(Base, ".ci-metadata td:first-child", "white-space"));
        Assert.Equal("anywhere", CssRules.Value(Base, ".ci-metadata td:last-child", "overflow-wrap"));
        Assert.Equal("20em", CssRules.Value(Base, ".ci-metadata", "max-width", RowLayout));
        Assert.Null(CssRules.Value(Base, ".ci-metadata", "max-width"));
    }''', '''    /// <summary>5.0.0 (plans/OPTION_C_PLAN.md S2): the filter box has a row of its own at every width, after the run
    /// band, so the 4.x header row and its band rules are gone.</summary>
    [Fact]
    public void The_header_row_is_gone_with_its_rules()
    {
        Assert.Equal(-1, CssRules.IndexOf(Base, ".header-row"));
        Assert.Null(CssRules.Value(Base, ".filtering-box", "flex-basis", Band));
    }

    /// <summary>The run band's values break where they must and its zones shrink, so a long branch or repository never
    /// widens the page (what the 4.x CI box's cap did); its labels are single words.</summary>
    [Fact]
    public void The_run_band_breaks_its_values_and_lets_its_zones_shrink()
    {
        Assert.Equal("anywhere", CssRules.Value(Base, ".run-stat dd", "overflow-wrap"));
        Assert.Equal("0", CssRules.Value(Base, ".run-zone", "min-width"));
        Assert.Equal("wrap", CssRules.Value(Base, ".run-stats", "flex-wrap"));
        Assert.Equal(-1, CssRules.IndexOf(Base, ".ci-metadata"));
    }''')
sub(C + 'StylesheetRulesTests.cs', '''    [InlineData(".test-execution-summary")]
''', '')

# DurationPaintTests: the ring's circles are in the band
s = open(E + 'DurationPaintTests.cs', 'rb').read().decode()
assert s.count('.summary-chart') == 3
s = s.replace('.summary-chart', '.run-ring')
open(E + 'DurationPaintTests.cs', 'wb').write(s.encode())

# ShowcaseReportTests: the summary scene scrolls to the band
sub(E + 'ShowcaseReportTests.cs', 'await ScrollTo(".test-execution-summary");', 'await ScrollTo(".run-band");')
print('ok')

# MobileResponsiveTests: the band in place of the header row
M = E + 'MobileResponsiveTests.cs'
sub(M, '''    // ── Header row stacks vertically ──

    [Fact]
    public async Task Header_row_stacks_vertically_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileHeaderStack.html"));
        var headerRow = Page.Locator(".header-row");
        await headerRow.WaitForAsync(new() { Timeout = 5000 });
        var flexDirection = await GetComputedStyle(headerRow, "flex-direction");
        Assert.Equal("column", flexDirection);
    }''', '''    // ── The run band fits a phone (5.0.0: the band in place of the 4.x header row, which stacked) ──

    [Fact]
    public async Task Run_band_fits_a_phone_with_a_smaller_ring()
    {
        await Page.GotoAsync(GenerateReport("MobileHeaderStack.html"));
        var band = Page.Locator(".run-band");
        await band.WaitForAsync(new() { Timeout = 5000 });
        var right = await band.EvaluateAsync<double>("e => e.getBoundingClientRect().right");
        Assert.True(right <= 375, $"the band ends at {right}, past the 375 px viewport");
        Assert.Equal(52, await Page.Locator(".run-ring").EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 1);
    }''')
sub(M, '''        await Page.GotoAsync(GenerateReport("MobileDesktopCheck.html"));
        await Page.Locator(".header-row").WaitForAsync(new() { Timeout = 5000 });

        // At 375px, header-row should be column
        var headerRow = Page.Locator(".header-row");
        Assert.Equal("column", await GetComputedStyle(headerRow, "flex-direction"));

        // Resize viewport to desktop
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.ReloadAsync();
        await Page.Locator(".header-row").WaitForAsync(new() { Timeout = 5000 });
        headerRow = Page.Locator(".header-row");
        Assert.Equal("row", await GetComputedStyle(headerRow, "flex-direction"));''', '''        await Page.GotoAsync(GenerateReport("MobileDesktopCheck.html"));
        await Page.Locator(".run-band").WaitForAsync(new() { Timeout = 5000 });

        // At 375px the ring is the phone's 52px (5.0.0: the run band in place of the 4.x header row)
        var ring = Page.Locator(".run-ring");
        Assert.Equal(52, await ring.EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 1);

        // Resize viewport to desktop
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.ReloadAsync();
        await Page.Locator(".run-band").WaitForAsync(new() { Timeout = 5000 });
        Assert.Equal(64, await Page.Locator(".run-ring").EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 1);''')
sub(M, '''    // ── Summary chart centred on mobile ──

    [Fact]
    public async Task Summary_chart_is_centred_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileChartCenter.html"));
        var chart = Page.Locator(".summary-chart");
        await chart.WaitForAsync(new() { Timeout = 5000 });
        var alignSelf = await GetComputedStyle(chart, "align-self");
        Assert.Equal("center", alignSelf);
    }''', '''    // ── The ring is centred on the summary beside it (5.0.0) ──

    [Fact]
    public async Task Run_ring_is_centred_on_the_summary_on_mobile()
    {
        await Page.GotoAsync(GenerateReport("MobileChartCenter.html"));
        var ring = Page.Locator(".run-ring");
        await ring.WaitForAsync(new() { Timeout = 5000 });
        var middles = await Page.EvaluateAsync<double[]>(
            "() => ['.run-ring', '.run-summary'].map(s => { const r = document.querySelector(s).getBoundingClientRect(); return r.top + r.height / 2; })");
        Assert.True(Math.Abs(middles[0] - middles[1]) < 1, $"ring middle {middles[0]}, summary middle {middles[1]}");
    }''')
s = open(M, 'rb').read().decode()
a = s.index('    // ── CI box full width on mobile ──')
b = s.index('    }\n', s.index('CI chart group width (')) + len('    }\n')
b = s.index('    }\n', b) + len('    }\n')
s = s[:a] + '''    // ── The CI zone under the summary on a phone (5.0.0: the run band; the 4.x test's report had no CI, so it measured nothing) ──
    // covered by RunBandLayoutTests.On_a_narrow_page_the_ci_zone_moves_under_the_summary_and_back
''' + s[b:]
open(M, 'wb').write(s.encode())
print('mobile ok')
