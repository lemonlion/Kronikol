using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #139 on the painted page (plans/TITLES_AS_WRITTEN_PLAN.md 6.3): a name the test wrote, and a word a method's name
/// writes in capitals, are shown as written in a scenario's heading, in the value "Copy scenario name" copies, in a
/// group's heading and in a search's results; and the Search tab sorts names as the Features list orders them (B3).
/// The names are the ones the lanes give: a DisplayName as written, a method's name and a theory's by the rule.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class TitlesAsWrittenTests : PlaywrightTestBase
{
    private static readonly DateTime T0 = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    public TitlesAsWrittenTests(PlaywrightFixture fixture) : base(fixture) { }

    private static Scenario Run(string id, string name, bool written = false, string? outlineId = null, string? net = null) => new()
    {
        Id = id,
        DisplayName = name,
        NameAsWritten = written,
        OutlineId = outlineId,
        ExampleValues = net is null ? null : new() { ["net"] = net },
        Result = ExecutionResult.Passed,
        Duration = TimeSpan.FromMilliseconds(12),
        Steps = [new ScenarioStep { Keyword = "Given", Text = "the catalogue is open", Status = ExecutionResult.Passed }],
    };

    private static Feature Orders() => new()
    {
        DisplayName = "Orders",
        Scenarios =
        [
            Run("s-written", "Orders reach Kafka (v1.2)", written: true),
            Run("s-caps", ScenarioTitleResolver.FormatScenarioDisplayName("Ns.OrderTests.GetHTTPResponse")),
            Run("s-row1", ScenarioTitleResolver.FormatScenarioDisplayName("Ns.OrderTests.AddsVATForTheUK(net: 1)"), outlineId: "AddsVATForTheUK", net: "1"),
            Run("s-row2", ScenarioTitleResolver.FormatScenarioDisplayName("Ns.OrderTests.AddsVATForTheUK(net: 2)"), outlineId: "AddsVATForTheUK", net: "2"),
        ],
    };

    private async Task OpenAsync(string fileName, params Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport([], features, T0, T0.AddMinutes(1), null,
            Path.Combine(TempDir, fileName), "Titles", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });
    }

    private async Task SearchAsync(string query)
    {
        await FillSearchBar(query, toSearchTab: true);
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden && document.getElementById('search-count').textContent !== ''",
            null, new() { PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private Task<string[]> TextsAsync(string selector) => Page.EvaluateAsync<string[]>(
        "s => Array.from(document.querySelectorAll(s)).map(e => e.textContent.trim())", selector);

    [Fact]
    public async Task A_written_name_and_a_word_in_capitals_are_shown_as_written()
    {
        await OpenAsync("TitlesAsWritten.html", Orders());

        // 5.16.4 showed "Orders reach kafka [v1.2]", "Get http response" and a group "Adds vat for the uk"
        var headings = await TextsAsync("details.scenario > summary");
        Assert.Contains(headings, h => h.Contains("Orders reach Kafka (v1.2)"));
        Assert.Contains(headings, h => h.Contains("Get HTTP response"));
        await Expect(Page.Locator("details.scenario-parameterized > summary").First).ToContainTextAsync("Adds VAT for the UK");

        var copied = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('.copy-scenario-name')).map(b => b.getAttribute('data-scenario-name'))");
        Assert.Contains("Orders reach Kafka (v1.2)", copied);
        Assert.Contains("Get HTTP response", copied);
        Assert.Contains("Adds VAT for the UK", copied);

        await SearchAsync("catalogue");
        var results = await TextsAsync("#panel-search .search-result-title");
        Assert.Contains("Orders reach Kafka (v1.2)", results);
        Assert.Contains("Get HTTP response", results);
    }

    // B3
    [Fact]
    public async Task The_Search_tab_sorts_names_as_the_reports_order_features()
    {
        // The sort compared code units, so every capital came before every lower-case letter: "Get HTTP response",
        // "Get a list", "gRPC call". The data files, and since 5.16.5 a merge, order features by the culture's comparer.
        string[] names = ["gRPC call", "Get HTTP response", "Get a list"];
        await OpenAsync("TitlesSearchSort.html", names.Select(n => new Feature { DisplayName = n, Scenarios = [Run("s-" + n, n, written: true)] }).ToArray());

        await SearchAsync("catalogue");
        await Page.Locator("#panel-search .search-sort-select").SelectOptionAsync("name");
        await TwoAnimationFramesAsync(Page);

        var sorted = await TextsAsync("#panel-search .search-result-title");
        Assert.Equal("Get a list | Get HTTP response | gRPC call", string.Join(" | ", sorted));
        Assert.Equal(string.Join(" | ", names.Order(StringComparer.CurrentCulture)), string.Join(" | ", sorted));
    }

    // R2 (plans/TITLES_AS_WRITTEN_PLAN.md 4.5): a name a lane made, the feature's from its class and a scenario's from its
    // method, reads the suite's words; a name the test wrote stays as written
    private static Scenario Made(string id, string name, string? outlineId = null, string? net = null) =>
        Run(id, name, outlineId: outlineId, net: net) with { NameGenerated = true, OutlineTitle = outlineId, OutlineTitleGenerated = outlineId is not null };

    [Fact]
    public async Task A_listed_word_is_spelled_as_the_suite_spells_it_in_the_names_the_report_made()
    {
        var reportsDir = Path.Combine(TempDir, "TitleWords");
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Acme Pay Webhook Tests",
                    NameGenerated = true,
                    Scenarios =
                    [
                        Made("s-made", "Webhook lead created via api journey"),
                        Run("s-written", "Calls the acme pay api", written: true),
                        Made("s-row1", "Adds vat for the uk [1]", outlineId: "Adds vat for the uk", net: "1"),
                        Made("s-row2", "Adds vat for the uk [2]", outlineId: "Adds vat for the uk", net: "2"),
                    ],
                },
            ], T0, T0.AddMinutes(1), new ReportConfigurationOptions
            {
                ReportsFolderPath = reportsDir,
                TitleWords = ["AcmePay", "API", "VAT", "UK"],
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
            });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, "TitleWords.html"), true);
        await Page.GotoReportAsync(new Uri(report).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });

        await Expect(Page.Locator("details.feature > summary").First).ToContainTextAsync("AcmePay Webhook Tests");
        var headings = await TextsAsync("details.scenario > summary");
        Assert.Contains(headings, h => h.Contains("Webhook lead created via API journey"));
        Assert.Contains(headings, h => h.Contains("Calls the acme pay api"));
        await Expect(Page.Locator("details.scenario-parameterized > summary").First).ToContainTextAsync("Adds VAT for the UK");

        var copied = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('.copy-scenario-name')).map(b => b.getAttribute('data-scenario-name'))");
        Assert.Contains("Webhook lead created via API journey", copied);
        Assert.Contains("Calls the acme pay api", copied);
        Assert.Contains("Adds VAT for the UK", copied);

        await SearchAsync("catalogue");
        var results = await TextsAsync("#panel-search .search-result-title");
        Assert.Contains("Webhook lead created via API journey", results);
        Assert.Contains("Calls the acme pay api", results);
    }
}
