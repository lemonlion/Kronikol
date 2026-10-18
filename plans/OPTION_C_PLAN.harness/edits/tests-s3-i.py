# S3: ToolbarStylingTests without the timeline toggle (5.0.0: the Timeline tab).
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

P = 'tests/Kronikol.Tests.EndToEnd/ToolbarStylingTests.cs'
sub(P, '''        { true, Tint, TintBorder, ".timeline-toggle" },
''', '')
sub(P, '''        { false, BlueTint, BlueTintBorder, ".timeline-toggle" },
''', '''        // 5.0.0: the Scenario Timeline toggle is a tab (plans/OPTION_C_PLAN.md S3); a tab not selected takes the hover
        // tint and has no border. The violet theme's tabs are S13's.
        { false, Hover, null, ".report-tab[aria-selected=''' + Q + '''false''' + Q + ''']" },
''')
sub(P, '''    [Theory]
    [InlineData(true, Violet, Violet, VioletDark)]
    [InlineData(false, Blue, Blue, "rgb(50, 110, 220)")]
    public async Task Active_percentile_and_timeline_buttons_take_the_themes_colours(bool violet, string active, string activeHovered, string timelineActiveHovered)
    {''', '''    /// <summary>The percentile buttons' active state. The timeline toggle this covered too is a tab since 5.0.0, whose
    /// selected look ReportTabsTests covers (plans/OPTION_C_PLAN.md S3).</summary>
    [Theory]
    [InlineData(true, Violet, Violet)]
    [InlineData(false, Blue, Blue)]
    public async Task Active_percentile_buttons_take_the_themes_colours(bool violet, string active, string activeHovered)
    {''')
sub(P, '''        await Paints(percentile, active, active);

        var timeline = Page.Locator(".timeline-toggle", new() { HasTextString = "Scenario Timeline" });
        await timeline.ClickAsync();
        await Expect(timeline).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("timeline-toggle-active"));
        await Paints(timeline, timelineActiveHovered);
        await Page.Mouse.MoveAsync(0, 0);
        await Paints(timeline, active, active);
    }''', '''        await Paints(percentile, active, active);
    }''')
print('ok')
