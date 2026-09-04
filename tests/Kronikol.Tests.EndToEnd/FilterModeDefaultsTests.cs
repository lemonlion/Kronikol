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
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        return url;
    }

    [Fact]
    public async Task Or_dependency_mode_default_seeds_button_and_hash_grammar()
    {
        await NavigateOrDefault("FilterMode_OrDefault.html");

        var modeBtn = Page.Locator(".dep-mode-toggle");
        Assert.Equal("OR", (await modeBtn.TextContentAsync())!.Trim());
        Assert.Equal("OR", await Page.EvaluateAsync<string>("() => _depMode"));

        // Selecting a chip writes the hash WITHOUT depmode — OR is this report's default now
        await Page.Locator(".dependency-toggle").First.ClickAsync();
        var hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("deps=", hash);
        Assert.DoesNotContain("depmode", hash);

        // Toggling to AND — the historically-omitted value — now lands in the hash
        await modeBtn.ClickAsync();
        Assert.Equal("AND", (await modeBtn.TextContentAsync())!.Trim());
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
        await Page.GotoAsync(url + "#depmode=AND");
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.WaitForFunctionAsync("() => _depMode === 'AND'",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        Assert.Equal("AND", (await Page.Locator(".dep-mode-toggle").TextContentAsync())!.Trim());
    }

    [Fact]
    public async Task Clear_all_resets_the_mode_to_the_configured_default()
    {
        await NavigateOrDefault("FilterMode_ClearAll.html");

        var modeBtn = Page.Locator(".dep-mode-toggle");
        await modeBtn.ClickAsync();
        Assert.Equal("AND", (await modeBtn.TextContentAsync())!.Trim());

        await Page.Locator("button.export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        Assert.Equal("OR", (await modeBtn.TextContentAsync())!.Trim());
        Assert.Equal("OR", await Page.EvaluateAsync<string>("() => _depMode"));
    }

    [Fact]
    public async Task And_category_mode_default_seeds_button_and_hash_grammar()
    {
        var url = ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "FilterMode_CatAnd.html",
            o => o.TestRunReportToggleDefaults.CategoryFilterMode = Kronikol.Reports.FilterCombinationMode.And);
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();

        var catBtn = Page.Locator(".cat-mode-toggle");
        Assert.Equal("AND", (await catBtn.TextContentAsync())!.Trim());
        Assert.Equal("AND", await Page.EvaluateAsync<string>("() => _catMode"));

        // Select a category so the hash is written; AND is the configured default → omitted
        await Page.Locator(".category-toggle[data-category='Smoke']").ClickAsync();
        var hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("cats=", hash);
        Assert.DoesNotContain("catmode", hash);

        // Toggling to OR — historically the silent default — now lands in the hash
        await catBtn.ClickAsync();
        hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.Contains("catmode=OR", hash);
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
        var content = await File.ReadAllTextAsync(path);
        // The export clones the head scripts, so the seeded default rides along
        Assert.Contains("var _depModeDefault = 'OR'", content);
    }
}
