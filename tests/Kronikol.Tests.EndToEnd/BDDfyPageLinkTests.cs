namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Runs the BDDfy example once. Its test process writes the Kronikol report when its tests end, then BDDfy's own page,
/// <c>BDDfy.html</c>, with each scenario's diagram entry, when the process ends.
/// </summary>
public sealed class BDDfyExampleFixture : IAsyncLifetime
{
    private static readonly string ProjectDir = Path.Combine(
        FullPipelineFixture.RepoRoot, "examples", "Example.Api", "tests", "Example.Api.Tests.Component.BDDfy.xUnit3");

    public string PagePath => Path.Combine(ProjectDir, "bin", "Debug", "net10.0", "BDDfy.html");

    public ValueTask InitializeAsync()
    {
        FullPipelineFixture.RunDotnetTest(ProjectDir);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// #241 (plans/PARTICIPANT_ORDER_PLAN.md R4, F6): under the default renderer the Kronikol report draws each diagram in its
/// own page, so BDDfy's page has no picture to show. It wrote an <c>img</c> with no address; it now links each scenario
/// to its place in the report. In the FullPipeline collection so that the example never runs beside the others: they all
/// build Example.Api.
/// </summary>
[Collection("FullPipeline")]
public class BDDfyPageLinkTests : PlaywrightTestBase, IClassFixture<BDDfyExampleFixture>
{
    private const string LinkText = "View this sequence diagram in the Kronikol report";
    private readonly BDDfyExampleFixture _example;

    public BDDfyPageLinkTests(PlaywrightFixture fixture, BDDfyExampleFixture example) : base(fixture) => _example = example;

    [Fact]
    public async Task Each_diagram_on_BDDfys_page_links_to_its_scenario_in_the_report_where_it_is_drawn()
    {
        var page = _example.PagePath;
        Assert.True(File.Exists(page), $"BDDfy's page not found at: {page}");
        File.Copy(page, Path.Combine(OutputDir, "BDDfy_" + Guid.NewGuid().ToString("N")[..6] + ".html"), true);
        Assert.DoesNotContain("src=\"\"", await File.ReadAllTextAsync(page), StringComparison.Ordinal);

        await Page.GotoAsync(new Uri(page).AbsoluteUri);
        // BDDfy's script hides each scenario's steps, and the diagram entry with them, until they are shown.
        await Page.Locator(".expandAll").First.ClickAsync();
        var entries = await Page.Locator(".tracking-diagram details").CountAsync();
        var links = Page.Locator(".tracking-diagram details a", new() { HasText = LinkText });
        Assert.NotEqual(0, entries);
        Assert.Equal(entries, await links.CountAsync());
        var href = await links.First.GetAttributeAsync("href");
        Assert.StartsWith("Reports/TestRunReport.html#sid-", href, StringComparison.Ordinal);

        await links.First.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/Reports/TestRunReport.html#sid-", StringComparison.Ordinal));
        await Page.WaitForReportAsync();
        await Page.WaitForFunctionAsync("""
            ([id]) => {
                const scenario = document.querySelector(`details.scenario[data-stable-id="${id}"]`);
                return !!scenario && scenario.open && !!scenario.querySelector("[data-diagram-type='plantuml'] svg");
            }
            """, new object[] { href!["Reports/TestRunReport.html#sid-".Length..] },
            new() { Timeout = PopupFirstDrawTimeout, PollingInterval = 200 });
    }
}
