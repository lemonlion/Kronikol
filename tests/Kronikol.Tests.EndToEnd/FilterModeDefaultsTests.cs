using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// E2E coverage for the configurable dependency/category filter-mode defaults (TOGGLE_DEFAULTS_PLAN
/// M6): seeded button text, the URL-hash grammar that now omits the mode when it equals the
/// CONFIGURED default (and accepts both values symmetrically on parse), Clear All resetting to the
/// configured defaults, and the Export Filtered HTML head carrying the seeded scripts.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class FilterModeDefaultsTests : DiagramNotePlaywrightBase
{
    public FilterModeDefaultsTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task<string> NavigateOrDefault(string fileName)
    {
        var url = ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, fileName,
            o => o.TestRunReportToggleDefaults.DependencyFilterMode = Kronikol.Reports.FilterCombinationMode.Or);
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();
        return url;
    }

    /// <summary>5.0.0: with nothing configured, a scenario with any of the chosen dependencies shows, as with tags
    /// (4.x showed only those with all of them).</summary>
    [Fact]
    public async Task Dependency_mode_is_or_when_nothing_is_configured()
    {
        var url = ReportTestHelper.GenerateToggleDefaultsReport(TempDir, OutputDir, "FilterMode_BuiltIn.html", _ => { });
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();

        Assert.Equal("ANY", (await Page.Locator(".dep-mode-toggle").TextContentAsync())!.Trim());
        Assert.Equal("OR", await Page.EvaluateAsync<string>("() => _depMode"));
    }

    [Fact]
    public async Task Or_dependency_mode_default_seeds_button_and_hash_grammar()
    {
        await NavigateOrDefault("FilterMode_OrDefault.html");

        var modeBtn = Page.Locator(".dep-mode-toggle");
        Assert.Equal("ANY", (await modeBtn.TextContentAsync())!.Trim());
        Assert.Equal("OR", await Page.EvaluateAsync<string>("() => _depMode"));

        // Selecting a chip writes the hash WITHOUT depmode — OR is this report's default now
        await Page.Locator(".dependency-toggle").First.ClickAsync();
        var hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("deps=", hash);
        Assert.DoesNotContain("depmode", hash);

        // Toggling to AND — the historically-omitted value — now lands in the hash
        await modeBtn.ClickAsync();
        Assert.Equal("ALL", (await modeBtn.TextContentAsync())!.Trim());
        hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("depmode=AND", hash);
    }

    [Fact]
    public async Task Hash_beats_the_configured_default_on_load()
    {
        // A deep link from an AND-default report must force AND on this OR-default report.
        var url = ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "FilterMode_HashBeats.html",
            o => o.TestRunReportToggleDefaults.DependencyFilterMode = Kronikol.Reports.FilterCombinationMode.Or);
        await Page.GotoReportAsync(url + "#depmode=AND");
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();

        await Page.WaitForFunctionAsync("() => _depMode === 'AND'",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        Assert.Equal("ALL", (await Page.Locator(".dep-mode-toggle").TextContentAsync())!.Trim());
    }

    [Fact]
    public async Task Clear_all_resets_the_mode_to_the_configured_default()
    {
        await NavigateOrDefault("FilterMode_ClearAll.html");

        var modeBtn = Page.Locator(".dep-mode-toggle");
        await modeBtn.ClickAsync();
        Assert.Equal("ALL", (await modeBtn.TextContentAsync())!.Trim());

        await Page.Locator("button.export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        Assert.Equal("ANY", (await modeBtn.TextContentAsync())!.Trim());
        Assert.Equal("OR", await Page.EvaluateAsync<string>("() => _depMode"));
    }

    [Fact]
    public async Task And_tag_mode_default_seeds_button_and_hash_grammar()
    {
        // TagFilterMode replaces CategoryFilterMode (plans/TAGS_PLAN.md §4.11), and the fixture's categories are tags
        var url = ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "FilterMode_TagAnd.html",
            o => o.TestRunReportToggleDefaults.TagFilterMode = Kronikol.Reports.FilterCombinationMode.And);
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();

        var tagBtn = Page.Locator(".tag-mode-toggle");
        Assert.Equal("ALL", (await tagBtn.TextContentAsync())!.Trim());
        Assert.Equal("AND", await Page.EvaluateAsync<string>("() => _tagMode"));

        // Select a tag so the hash is written; AND is the configured default → omitted
        await Page.Locator(".tag-toggle[data-tag='Smoke']").ClickAsync();
        var hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("tags=Smoke", hash);
        Assert.DoesNotContain("tagmode", hash);

        // Toggling to OR, the built-in default, now lands in the hash
        await tagBtn.ClickAsync();
        hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("tagmode=OR", hash);
        Assert.DoesNotContain("cat", hash);
    }

    [Fact]
    public async Task Export_filtered_html_carries_the_seeded_defaults()
    {
        await NavigateOrDefault("FilterMode_Export.html");

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync();
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        var content = ReportFileText.Read(path);
        // The export clones the head scripts, so the seeded default rides along
        Assert.Contains("var _depModeDefault = 'OR'", content);
    }
}
