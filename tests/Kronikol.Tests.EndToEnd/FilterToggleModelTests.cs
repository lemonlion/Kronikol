using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// One model for every diagram filter (plans/OPTION_C_PLAN.md S9, F5): a scenario's own choice holds for the diagrams it
/// draws later, and the report's choice holds everywhere, over a scenario's own. A diagram is given its source only when
/// it is drawn, so a scenario's toggle reaches the diagrams already drawn; until 5.0.0 a diagram drawn after it took the
/// report's state and ignored the scenario's.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FilterToggleModelTests : PlaywrightTestBase
{
    public FilterToggleModelTests(PlaywrightFixture fixture) : base(fixture) { }

    // a database, a broker, a setup before the When step (step bars as the emitter writes them) and an assertion
    private static string Source(string region) => "@startuml\nparticipant \"Service\" as svc\ndatabase \"Order Db\" as db\n" +
        "queue \"Orders Topic\" as topic\n" +
        "hnote across <<stepDelimiter>> #black:<color:white>Given a clean store\nsvc -> svc : clear cache\n" +
        "hnote across <<stepDelimiter>> #black:<color:white>When the order is stored\n" +
        $"svc -> db : INSERT {region}\nsvc -> topic : OrderStored\n" +
        "hnote across <<assertionNote>> #E8F5E9\n✓ Stored in the database\nend note\n@enduml";

    private async Task<Microsoft.Playwright.ILocator> OpenGroupAsync(string fileName)
    {
        Scenario Example(string id, string region) => new()
        {
            Id = id, DisplayName = $"Store(region: {region})", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
            OutlineId = "Store", ExampleValues = new() { ["region"] = region }
        };
        Scenario[] scenarios = [Example("p1", "UK"), Example("p2", "US")];
        var path = ReportGenerator.GenerateHtmlReport(
            scenarios.Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", Source(s.ExampleValues!["region"]))).ToArray(),
            [new Feature { DisplayName = "Orders", Scenarios = scenarios }], DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Bakery", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs, groupParameterizedTests: true);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await ExpandFirstScenarioWithDiagram();
        var group = Page.Locator("details.scenario-parameterized");
        await group.ScrollIntoViewIfNeededAsync();
        await WaitForDrawnAsync("[id$='-diagram-0']");
        return group;
    }

    private Task WaitForDrawnAsync(string diagram) => Page.WaitForFunctionAsync(
        "d => { const el = document.querySelector(d); return el && el.offsetParent !== null && el.querySelector('svg'); }",
        diagram, new() { Timeout = 20000, PollingInterval = 200 });

    // whether the diagram, once drawn again, shows the marker as asked
    private Task WaitForMarkerAsync(string diagram, string marker, bool shown) => Page.WaitForFunctionAsync(
        "([d, m, on]) => { const s = document.querySelector(d + ' svg'); return s && s.textContent.includes(m) === on; }",
        new object[] { diagram, marker, shown }, new() { Timeout = 20000, PollingInterval = 200 });

    private Task<bool> ShowsAsync(string diagram, string marker) => Page.EvaluateAsync<bool>(
        "([d, m]) => document.querySelector(d + ' svg').textContent.includes(m)", new object[] { diagram, marker });

    // a diagram the scenario draws now, given its source only now, as every diagram is when it is drawn
    private async Task DrawLaterDiagramAsync()
    {
        await Page.EvaluateAsync("""
            src => {
                const host = document.querySelector("[id$='-diagram-1']").parentElement;
                const el = document.createElement('div');
                el.className = 'plantuml-browser';
                el.id = 'later-drawn';
                el.setAttribute('data-diagram-type', 'plantuml');
                el.setAttribute('data-plantuml', src);
                host.appendChild(el);
                window._renderDiagramsInContainer(host);
            }
            """, Source("DE"));
        await Page.WaitForFunctionAsync("() => document.querySelector('#later-drawn svg')", null, new() { Timeout = 20000, PollingInterval = 200 });
    }

    [Theory]
    [InlineData("databases", "Order Db", false)]
    [InlineData("steps", "When the order is stored", false)]
    [InlineData("assertions", "Stored in the database", true)]
    [InlineData("events", "OrderStored", false)]
    [InlineData("setup", "clear cache", false)]
    public async Task A_scenarios_choice_holds_for_the_diagrams_it_draws_after_it(string toggle, string marker, bool shownAfter)
    {
        var group = await OpenGroupAsync($"FilterToggleModel-{toggle}.html");
        Assert.Equal(!shownAfter, await ShowsAsync("[id$='-diagram-0']", marker));

        await OpenScenarioSettingsAsync(group);
        await group.Locator($".scenario-settings .toggle-btn[data-toggle='{toggle}']").ClickAsync();
        await WaitForMarkerAsync("[id$='-diagram-0']", marker, shownAfter);

        // the second example, drawn with the first, was drawn again with the scenario's choice
        await group.Locator("tbody tr").Nth(1).ClickAsync();
        await WaitForDrawnAsync("[id$='-diagram-1']");
        Assert.Equal(shownAfter, await ShowsAsync("[id$='-diagram-1']", marker));

        await DrawLaterDiagramAsync();
        Assert.Equal(shownAfter, await ShowsAsync("#later-drawn", marker));
    }

    [Fact]
    public async Task The_reports_choice_holds_over_a_scenarios_own()
    {
        var group = await OpenGroupAsync("FilterToggleModel-report.html");
        await OpenScenarioSettingsAsync(group);
        var scenarioToggle = group.Locator(".scenario-settings .toggle-btn[data-toggle='databases']");
        await scenarioToggle.ClickAsync();
        await WaitForMarkerAsync("[id$='-diagram-0']", "Order Db", false);

        // the report's Databases off and on again: every scenario shows its databases, the one that hid them too
        var reportToggle = Page.Locator("button[data-toggle='databases']").First;
        await reportToggle.ClickAsync();
        await WaitForMarkerAsync("[id$='-diagram-0']", "Order Db", false);
        await reportToggle.ClickAsync();
        await WaitForMarkerAsync("[id$='-diagram-0']", "Order Db", true);
        Assert.Equal("true", await scenarioToggle.GetAttributeAsync("aria-pressed"));

        await DrawLaterDiagramAsync();
        Assert.True(await ShowsAsync("#later-drawn", "Order Db"));
    }
}
