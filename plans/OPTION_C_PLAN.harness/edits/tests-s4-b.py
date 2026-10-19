# S4: the Playwright tests that read the 4.x Expand All buttons' flipping labels (D22).
def region(path, start, end, new):
    s = open(path, 'rb').read().decode('utf-8')
    i = s.index(start)
    j = s.index(end, i)
    s = s[:i] + new + s[j:]
    open(path, 'wb').write(s.encode('utf-8'))

def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode('utf-8')
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode('utf-8'))

E = 'tests/Kronikol.Tests.EndToEnd/'
region(E + 'ReportToolbarTests.cs', '    // ── Expand / Collapse All buttons ──', '    // ── Details radio buttons', '''    // ── Expand and collapse all: since 5.0.0 an icon pair per level (plans/OPTION_C_PLAN.md S4, D22) ──

    private ILocator Fold(string action, string noun) => Page.Locator($"button.fold-{action}[data-fold='{noun}']");

    [Fact]
    public async Task Expand_all_features_opens_all_feature_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarExpandFeatures.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Fold("expand", "features").ClickAsync();

        var features = Page.Locator("details.feature");
        var count = await features.CountAsync();
        Assert.True(count >= 2, "Should have at least 2 features");
        for (var i = 0; i < count; i++)
            Assert.NotNull(await features.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Collapse_all_features_closes_all_feature_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarCollapseFeatures.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Fold("expand", "features").ClickAsync();
        await Fold("collapse", "features").ClickAsync();

        var features = Page.Locator("details.feature");
        var count = await features.CountAsync();
        for (var i = 0; i < count; i++)
            Assert.Null(await features.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Expand_all_scenarios_opens_all_scenario_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarExpandScenarios.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Fold("expand", "scenarios").ClickAsync();

        var scenarios = Page.Locator("details.scenario");
        var count = await scenarios.CountAsync();
        Assert.True(count >= 3, "Should have scenarios");
        for (var i = 0; i < count; i++)
            Assert.NotNull(await scenarios.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Collapse_all_scenarios_closes_all_scenario_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarCollapseScenarios.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Fold("expand", "scenarios").ClickAsync();
        await Fold("collapse", "scenarios").ClickAsync();

        var scenarios = Page.Locator("details.scenario");
        var count = await scenarios.CountAsync();
        for (var i = 0; i < count; i++)
            Assert.Null(await scenarios.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task A_fold_button_is_dimmed_while_it_has_nothing_to_do()
    {
        await Page.GotoAsync(GenerateReport("ToolbarFoldIdle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // nothing is open: collapsing has nothing to do
        await Expect(Fold("expand", "features")).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(Fold("collapse", "features")).ToHaveAttributeAsync("aria-disabled", "true");

        await Fold("expand", "features").ClickAsync();
        await Expect(Fold("expand", "features")).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(Fold("collapse", "features")).ToHaveAttributeAsync("aria-disabled", "false");

        // one closed by hand: both have something to do (item R17.2, no need to open everything before closing it)
        await Page.Locator("details.feature > summary").First.ClickAsync();
        await Expect(Fold("expand", "features")).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(Fold("collapse", "features")).ToHaveAttributeAsync("aria-disabled", "false");
    }

''')

T = E + 'ToggleDefaultsTests.cs'
s = open(T, 'rb').read().decode('utf-8')
i = s.index('        var featuresBtn = Page.Locator("button.collapse-expand-all").Nth(0);')
print(s[i - 900:i + 700])
