# 5.0.0's diagram defaults (the owner, 2026-10-19: "for v5 I want all the report level diagram toggles except Headers to
# be on by default, and the default should be Truncate 10 Lines"): assertions start shown, payloads truncate at 10
# lines. Steps, Databases, Events and Setup already start shown; Headers stays hidden. usage: python defaults-v5.py
# [--tests | --src]   (tests first: --tests applies only the test edits, --src only the product's)
import sys

ROOT = 'C:/Code/Kronikol-v5/'
which = sys.argv[1] if len(sys.argv) > 1 else '--all'


def edit(path, pairs):
    p = ROOT + path
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    for old, new in pairs:
        if crlf:
            old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
        assert s.count(old) == 1, (path, old[:70], s.count(old))
        s = s.replace(old, new)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', path)


if which in ('--tests', '--all'):
    edit('tests/Kronikol.Tests/Reports/ReportToggleDefaultsResolverTests.cs', [
        ("""        Assert.Equal(TruncateLineCount.Lines40, b.TruncateLines);
        // 4.0.0 (plans/V4_PLAN.md R8): headers start hidden and notes start as YAML; through 3.x
        // headers started shown and notes as JSON.
        Assert.False(b.HeadersShown);
        Assert.False(b.AssertionsShown);""",
         """        // 5.0.0 (the owner, 2026-10-19): every diagram toggle but Headers starts on, and payloads truncate at 10
        // lines; through 4.x assertions started hidden and the truncation was 40 lines.
        Assert.Equal(TruncateLineCount.Lines10, b.TruncateLines);
        // 4.0.0 (plans/V4_PLAN.md R8): headers start hidden and notes start as YAML; through 3.x
        // headers started shown and notes as JSON.
        Assert.False(b.HeadersShown);
        Assert.True(b.AssertionsShown);
        Assert.True(b.EventsShown);
        Assert.True(b.SetupShown);"""),
    ])
    edit('tests/Kronikol.Tests/Reports/DiagramContextMenuTests.cs', [
        ("""    public void Globals_truncateLines_defaults_to_40()""", """    public void Globals_truncateLines_defaults_to_10()"""),
        ("""        Assert.Contains("window._truncateLinesDefault = 40", _notesScript);""",
         """        Assert.Contains("window._truncateLinesDefault = 10", _notesScript);"""),
    ])
    edit('tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs', [
        ("""        // 4.0.0 (plans/V4_PLAN.md R8): headers start hidden and notes as YAML.
        Assert.Contains("window._headersHidden = true", script);
        Assert.Contains("window._truncateLinesDefault = 40", script);""",
         """        // 4.0.0 (plans/V4_PLAN.md R8): headers start hidden and notes as YAML. 5.0.0 (the owner): assertions start
        // shown and payloads truncate at 10 lines.
        Assert.Contains("window._headersHidden = true", script);
        Assert.Contains("window._truncateLinesDefault = 10", script);"""),
        ("""        Assert.Contains("window._assertionsVisible = false", script);
        Assert.Contains("window._stepsVisible = true", script);
        Assert.Contains("window._databasesVisible = true", script);
        Assert.Contains("window._noteFormatDefault = 'yaml'", script);
        foreach (var token in ScriptTokens)
            Assert.DoesNotContain(token, script);
    }

    [Fact]
    public void Script_tokens_substitute_to_the_configured_globals()""",
         """        Assert.Contains("window._assertionsVisible = true", script);
        Assert.Contains("window._stepsVisible = true", script);
        Assert.Contains("window._databasesVisible = true", script);
        Assert.Contains("window._noteFormatDefault = 'yaml'", script);
        foreach (var token in ScriptTokens)
            Assert.DoesNotContain(token, script);
    }

    [Fact]
    public void Script_tokens_substitute_to_the_configured_globals()"""),
    ])
    edit('tests/Kronikol.Tests.EndToEnd/ToggleDefaultsTests.cs', [
        ("""        await NavigateLongNote("Toggle_TruncateLines10.html", t => t.TruncateLines = Kronikol.Reports.TruncateLineCount.Lines10);""",
         """        // a preset other than the built-in 10 lines (5.0.0)
        await NavigateLongNote("Toggle_TruncateLines20.html", t => t.TruncateLines = Kronikol.Reports.TruncateLineCount.Lines20);"""),
        ("""        Assert.True(lines is > 5 and < 15, $"the 45-line note should truncate near the seeded 10 lines (got {lines})");

        // Both dropdowns read the seeded value
        Assert.Equal("10", await Page.Locator(".toolbar-right .truncate-lines-select").InputValueAsync());
        await OpenScenarioSettingsAsync();
        Assert.Equal("10", await Page.Locator("details.scenario .truncate-lines-select").First.InputValueAsync());
        Assert.Equal(10, await Page.EvaluateAsync<int>("() => window._truncateLines"));""",
         """        Assert.True(lines is > 15 and < 25, $"the 45-line note should truncate near the seeded 20 lines (got {lines})");

        // Both dropdowns read the seeded value
        Assert.Equal("20", await Page.Locator(".toolbar-right .truncate-lines-select").InputValueAsync());
        await OpenScenarioSettingsAsync();
        Assert.Equal("20", await Page.Locator("details.scenario .truncate-lines-select").First.InputValueAsync());
        Assert.Equal(20, await Page.EvaluateAsync<int>("() => window._truncateLines"));"""),
        ("""    public async Task Assertions_shown_default_renders_assertions_with_zero_clicks()
    {
        await NavigateWide("Toggle_AssertionsShown.html", t => t.AssertionsShown = true);

        var text = await GetNormalizedSvgText();
        Assert.Contains("✓ Put steps response message status code should be OK", text);

        var btn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions']");
        Assert.Equal("Assertions", (await btn.TextContentAsync())!.Trim());
        Assert.Equal("true", await btn.GetAttributeAsync("aria-pressed"));

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await OpenScenarioSettingsAsync();
        await Page.Locator("details.scenario .toggle-btn[data-toggle='assertions'][data-shown='true']").ClickAsync();
        await WaitForRenderCountIncrease(renderCount);
        Assert.DoesNotContain("✓ Put steps response message status code should be OK", await GetNormalizedSvgText());
    }""",
         """    public async Task Assertions_hidden_default_strips_assertions_with_zero_clicks()
    {
        // assertions start shown since 5.0.0, so the configured state here is the other one
        await NavigateWide("Toggle_AssertionsHidden.html", t => t.AssertionsShown = false);

        Assert.DoesNotContain("✓ Put steps response message status code should be OK", await GetNormalizedSvgText());

        var btn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions']");
        Assert.Equal("Assertions", (await btn.TextContentAsync())!.Trim());
        Assert.Equal("false", await btn.GetAttributeAsync("aria-pressed"));

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await OpenScenarioSettingsAsync();
        await Page.Locator("details.scenario .toggle-btn[data-toggle='assertions'][data-shown='false']").ClickAsync();
        await WaitForRenderCountIncrease(renderCount);
        Assert.Contains("✓ Put steps response message status code should be OK", await GetNormalizedSvgText());
    }

    [Fact]
    public async Task With_nothing_configured_assertion_notes_are_drawn_and_payloads_truncate_at_10_lines()
    {
        // 5.0.0 (the owner, 2026-10-19): every diagram toggle but Headers starts on; payloads truncate at 10 lines
        await NavigateWide("Toggle_V5Assertions.html", _ => { });
        Assert.Contains("✓ Put steps response message status code should be OK", await GetNormalizedSvgText());
        Assert.Equal("true", await Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions']").GetAttributeAsync("aria-pressed"));

        await NavigateLongNote("Toggle_V5Truncation.html", _ => { });
        var lines = await CountNoteLinesInSource();
        Assert.True(lines is > 5 and < 15, $"the 45-line note should truncate near 10 lines (got {lines})");
        Assert.Equal("10", await Page.Locator(".toolbar-right .truncate-lines-select").InputValueAsync());
    }"""),
    ])

if which in ('--src', '--all'):
    edit('src/Kronikol/Reports/ReportToggleDefaultsResolver.cs', [
        ("""    public TruncateLineCount TruncateLines { get; init; } = TruncateLineCount.Lines40;""",
         """    public TruncateLineCount TruncateLines { get; init; } = TruncateLineCount.Lines10;"""),
        ("""    public bool AssertionsShown { get; init; }
""", """    public bool AssertionsShown { get; init; } = true;
"""),
    ])
    edit('src/Kronikol/Reports/ReportToggleDefaults.cs', [
        ("""Default: <see cref="TruncateLineCount.Lines40"/>.</summary>""",
         """Default: <see cref="TruncateLineCount.Lines10"/> from 5.0.0; <see cref="TruncateLineCount.Lines40"/> in 4.x.</summary>"""),
        ("""    /// <summary>Assertion notes shown in diagrams. Default: <c>false</c>.</summary>""",
         """    /// <summary>Assertion notes shown in diagrams. Default: <c>true</c> from 5.0.0; <c>false</c> in 4.x.</summary>"""),
    ])
    edit('src/Kronikol/Reports/TruncateLineCount.cs', [
        ("""/// resolver: <see cref="Lines40"/>.""", """/// resolver: <see cref="Lines10"/> (5.0.0; <see cref="Lines40"/> in 4.x)."""),
    ])
