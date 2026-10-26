using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A closed composite step's sub-steps are drawn the first time it opens (5.0.0, <c>plans/REPORT_FORMAT_PLAN.md</c> 4.8
/// part b, Q8), read in the browser: the steps appear, a chain opens level by level from one entry, the step says how
/// many it holds before it is opened (D49), the search box finds a word only a held step holds, Export Filtered HTML
/// carries the entries its scenarios still need and no other, and a row of a parameterized group opens the steps that
/// were open in the row it replaced, drawn or not.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class HeldSubStepsTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private const string Drawn = "() => !document.querySelector('.sub-steps[data-steps]')";

    // Thirty sub-steps of 80-odd characters: past the 2 KB a held subtree needs.
    private static ScenarioStep Composite(string text, int count = 30) => new()
    {
        Keyword = "Given", Text = text, Status = ExecutionResult.Passed,
        SubSteps = Enumerable.Range(1, count).Select(i => new ScenarioStep
        {
            Keyword = "And", Text = $"{text} step {i} " + new string('x', 60), Status = ExecutionResult.Passed,
        }).ToArray(),
    };

    private static ScenarioStep Chain(string text, int depth)
    {
        var leaf = new ScenarioStep { Keyword = "And", Text = $"{text} leaf " + new string('y', 200), Status = ExecutionResult.Passed };
        for (var level = depth - 1; level >= 0; level--)
            leaf = new ScenarioStep { Keyword = "And", Text = $"{text} level {level}", Status = ExecutionResult.Passed, SubSteps = [leaf] };
        return leaf;
    }

    private static Scenario Holding(string id, string name, params ScenarioStep[] steps) =>
        new() { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Steps = steps };

    private string Generate(string fileName, bool testRunData, params Feature[] features) =>
        Generate(fileName, testRunData, separateBackground: false, features);

    private string Generate(string fileName, bool testRunData, bool separateBackground, Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Held", testRunData, diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: separateBackground);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private string Generate(string fileName, params Feature[] features) => Generate(fileName, true, features);

    private string Generate(string fileName, params Scenario[] scenarios) =>
        Generate(fileName, new Feature { DisplayName = "Held", Scenarios = scenarios });

    private async Task OpenScenarios() =>
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.scenario, details.scenario-parameterized, details.scenario-steps').forEach(d => d.open = true)");

    private const string StepText = "span[contains(concat(' ', @class, ' '), ' step-text ')]";
    private const string CompositeStep = "details[contains(concat(' ', @class, ' '), ' step-collapsible ')]";

    // A composite step's summary, by its own text exactly.
    private ILocator Summary(string step, string within = "") =>
        Page.Locator($"xpath={within}//{CompositeStep}/summary[{StepText}[normalize-space(.)='{step}']]").First;

    private ILocator Holder(string step) =>
        Page.Locator($"xpath=//{CompositeStep}[summary/{StepText}[normalize-space(.)='{step}']]/div[contains(concat(' ', @class, ' '), ' sub-steps ')]").First;

    [Fact]
    public async Task Opening_a_held_step_draws_its_steps()
    {
        await Page.GotoReportAsync(Generate("HeldDraw.html", Holding("a", "A basket", Composite("the basket"))));
        await OpenScenarios();
        var holder = Holder("the basket");
        Assert.Equal("30", await holder.GetAttributeAsync("data-count"));
        Assert.NotNull(await holder.GetAttributeAsync("data-steps"));
        Assert.Equal(0, await holder.Locator(":scope > .step").CountAsync());

        await Summary("the basket").ClickAsync();
        await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });

        Assert.Equal(30, await holder.Locator(":scope > .step").CountAsync());
        Assert.Null(await holder.GetAttributeAsync("data-steps"));
    }

    [Fact]
    public async Task The_specifications_page_draws_a_held_step_too()
    {
        await Page.GotoReportAsync(Generate("HeldSpecifications.html", false,
            new Feature { DisplayName = "Held", Scenarios = [Holding("p", "A basket", Composite("the basket"))] }));
        await OpenScenarios();
        var holder = Holder("the basket");
        Assert.NotNull(await holder.GetAttributeAsync("data-steps"));

        await Summary("the basket").ClickAsync();
        await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });

        Assert.Equal(30, await holder.Locator(":scope > .step").CountAsync());
    }

    [Fact]
    public async Task Where_the_browser_cannot_decompress_a_plain_page_s_opened_step_says_its_steps_could_not_be_drawn()
    {
        // Chrome before 80, Firefox before 113 and Safari before 16.4 have no DecompressionStream. A plain page (one
        // kronikol decode writes, or one written before 5.6.0) holds a held step's sub-steps in gzip.
        await Page.AddInitScriptAsync("delete window.DecompressionStream;");
        var path = new Uri(Generate("HeldNoDecompress.html", Holding("d", "A basket", Composite("the basket")))).LocalPath;
        var plain = Path.Combine(Path.GetDirectoryName(path)!, "HeldNoDecompress.plain.html");
        File.WriteAllText(plain, ReportFileText.Read(path));
        await Page.GotoReportAsync(new Uri(plain).AbsoluteUri);
        await OpenScenarios();

        await Summary("the basket").ClickAsync();

        await Expect(Holder("the basket")).ToHaveTextAsync("These steps could not be drawn: this browser cannot decompress them.");
        await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });
    }

    [Fact]
    public async Task Where_the_browser_cannot_decompress_a_compressed_page_still_draws_an_opened_step()
    {
        // The compressed page holds the sub-steps raw in its page frame, which the page's own decoder opens.
        await Page.AddInitScriptAsync("delete window.DecompressionStream;");
        await Page.GotoReportAsync(Generate("HeldNoDecompressCompressed.html", Holding("d", "A basket", Composite("the basket"))));
        await OpenScenarios();

        await Summary("the basket").ClickAsync();

        await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });
        await Expect(Page.Locator("xpath=//" + StepText + "[contains(., 'the basket step 30')]").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_chain_opens_level_by_level_from_one_entry()
    {
        await Page.GotoReportAsync(Generate("HeldChain.html", Holding("c", "A chain", Chain("deep", 12))));
        await OpenScenarios();
        Assert.Equal(1, await Page.Locator(".sub-steps[data-steps]").CountAsync());

        for (var level = 0; level < 12; level++)
        {
            await Summary($"deep level {level}").ClickAsync();
            await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });
        }

        await Expect(Page.Locator("span.step-text", new() { HasTextString = "deep leaf" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_held_step_says_how_many_steps_it_holds_before_it_is_opened()
    {
        await Page.GotoReportAsync(Generate("HeldTitle.html", Holding("t", "A basket", Composite("the basket"))));
        await OpenScenarios();
        var summary = Summary("the basket");

        await summary.HoverAsync();

        Assert.Equal("This step has 30 steps inside it: click to show or hide them", await summary.GetAttributeAsync("title"));
    }

    [Fact]
    public async Task The_search_box_finds_a_word_only_a_held_step_holds()
    {
        var needle = Composite("the basket");
        needle.SubSteps![12] = new ScenarioStep { Keyword = "And", Text = "a quokka in the basket", Status = ExecutionResult.Passed };
        await Page.GotoReportAsync(Generate("HeldSearch.html", Holding("n", "Has the word", needle), Holding("o", "Other", Composite("the shelf"))));
        await OpenScenarios();
        Assert.Equal(2, await Page.Locator(".sub-steps[data-steps]").CountAsync());

        await FillSearchBar("quokka");

        await Expect(Page.Locator("details.scenario", new() { HasTextString = "Has the word" }).First).ToBeVisibleAsync();
        await Expect(Page.Locator("details.scenario", new() { HasTextString = "Other" }).First).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Export_Filtered_HTML_carries_the_held_steps_its_scenarios_need_and_no_other()
    {
        await Page.GotoReportAsync(Generate("HeldExport.html",
            new Feature { DisplayName = "First", Scenarios = [Holding("f", "First basket", Composite("the first basket"))] },
            new Feature { DisplayName = "Second", Scenarios = [Holding("s", "Second basket", Composite("the second basket"))] }));
        await FillSearchBar("First basket");
        // The export takes the features the filters leave showing.
        await Expect(Page.Locator("details.feature", new() { HasTextString = "Second basket" }).First).ToBeHiddenAsync();

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await PressBoxExportAsync(Page, "Export Filtered HTML"));
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => Object.keys(JSON.parse(document.getElementById('step-data').textContent)).length"));
        await OpenScenarios();
        await Summary("the first basket").ClickAsync();
        await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });
        Assert.Equal(30, await Holder("the first basket").Locator(":scope > .step").CountAsync());
    }

    [Fact]
    public async Task An_export_taken_while_a_step_is_being_drawn_lets_the_exported_page_draw_it()
    {
        await Page.GotoReportAsync(Generate("HeldInFlight.html", Holding("i", "A basket", Composite("the basket"))));
        await OpenScenarios();
        // The decompression waits until the export is taken.
        await Page.EvaluateAsync(@"() => {
            const decode = window.decompressGzipBase64;
            window.__held = [];
            window.decompressGzipBase64 = b => new Promise(r => window.__held.push(() => r(decode(b))));
        }");
        await Summary("the basket").ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__held.length > 0", null, new() { PollingInterval = 200 });

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await PressBoxExportAsync(Page, "Export Filtered HTML"));
        await Page.EvaluateAsync("() => window.__held.forEach(release => release())");
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await OpenScenarios();

        // Closed and opened again, in case the copy's open step was not drawn as the page loaded.
        var summary = Summary("the basket");
        if (await Page.EvaluateAsync<bool>("() => document.querySelector('details.step-collapsible').open")) await summary.ClickAsync();
        await summary.ClickAsync();
        await Page.WaitForFunctionAsync(Drawn, null, new() { PollingInterval = 200 });
        Assert.Equal(30, await Holder("the basket").Locator(":scope > .step").CountAsync());
    }

    [Fact]
    public async Task A_row_of_a_parameterized_group_opens_the_steps_open_in_the_row_it_replaced()
    {
        Scenario Row(string id, string region) => new()
        {
            Id = id, DisplayName = $"Process(region: {region})", Result = ExecutionResult.Passed, OutlineId = "Process",
            ExampleValues = new Dictionary<string, string> { ["region"] = region },
            Steps = [Chain("deep", 12)],
        };
        await Page.GotoReportAsync(Generate("HeldRows.html", Row("r0", "UK"), Row("r1", "US")));
        await OpenScenarios();
        var group = Page.Locator("details.scenario-parameterized").First;
        var panel0 = "//*[contains(@id, '-detail-0')]";
        // Each row holds its chain below the first level, so row 1's levels are not in the page until it is drawn.
        Assert.Equal(1, await Page.Locator("[id$='-detail-0'] .sub-steps[data-steps]").CountAsync());
        Assert.Equal(1, await Page.Locator("[id$='-detail-1'] .sub-steps[data-steps]").CountAsync());
        for (var level = 0; level < 3; level++)
        {
            await Summary($"deep level {level}", panel0).ClickAsync();
            await Page.WaitForFunctionAsync(Drawn.Replace("document", "document.querySelector('[id$=\"-detail-0\"]')"), null, new() { PollingInterval = 200 });
        }

        await group.Locator("tbody tr").Nth(1).ClickAsync();

        // Row 1's chain was never drawn: it opens level by level as each level is drawn, to the three row 0 had open.
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[id$=\"-detail-1\"]').querySelectorAll('details.step-collapsible[open]').length === 3",
            null, new() { PollingInterval = 200 });
        var level3Open = await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelector('[id$=\"-detail-1\"]').querySelectorAll('details.step-collapsible')).some(d => d.open && d.querySelector(':scope > summary .step-text').textContent.trim() === 'deep level 3')");
        Assert.False(level3Open);
    }

    [Fact]
    public async Task A_row_keeps_a_background_step_apart_from_the_scenario_s_own()
    {
        // With the background in a section of its own, its first step and the scenario's first are each first in a list.
        Scenario Row(string id, string region) => new()
        {
            Id = id, DisplayName = $"Process(region: {region})", Result = ExecutionResult.Passed, OutlineId = "Process",
            ExampleValues = new Dictionary<string, string> { ["region"] = region },
            BackgroundSteps = [Composite("the shelf")],
            Steps = [Composite("the basket")],
        };
        await Page.GotoReportAsync(Generate("HeldRowsBackground.html", true, separateBackground: true,
            [new Feature { DisplayName = "Held", Scenarios = [Row("r0", "UK"), Row("r1", "US")] }]));
        await OpenScenarios();
        var group = Page.Locator("details.scenario-parameterized").First;
        Assert.Equal(2, await Page.Locator("[id$='-detail-0'] .sub-steps[data-steps]").CountAsync());

        await Summary("the basket", "//*[contains(@id, '-detail-0')]").ClickAsync();
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('[id$=\"-detail-0\"] .sub-steps[data-steps]').length === 1",
            null, new() { PollingInterval = 200 });
        await group.Locator("tbody tr").Nth(1).ClickAsync();

        // Row 1 draws and opens the basket, and leaves the shelf, closed in row 0, closed.
        const string States = "() => Array.from(document.querySelector('[id$=\"-detail-1\"]').querySelectorAll('details.step-collapsible')).map(d => d.querySelector(':scope > summary .step-text').textContent.trim() + '=' + d.open + (d.querySelector(':scope > .sub-steps[data-steps]') ? ' held' : ''))";
        await Page.WaitForFunctionAsync($"() => ({States})().includes('the basket=true')", null, new() { PollingInterval = 200 });
        Assert.Equal(["the shelf=false held", "the basket=true"], await Page.EvaluateAsync<string[]>(States));
    }
}
