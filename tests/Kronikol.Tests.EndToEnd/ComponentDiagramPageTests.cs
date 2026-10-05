using Microsoft.Playwright;
using Kronikol.ComponentDiagram;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// ComponentDiagram.html, the page beside the report. Until 4.5.1 it had no DOCTYPE, so a browser drew it in
/// quirks mode, and no <c>&lt;title&gt;</c>; its heading wrote the configured title as markup. In standards mode
/// the diagram draws at the size it drew in quirks mode, which a copy of the page without its DOCTYPE shows.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ComponentDiagramPageTests : PlaywrightTestBase
{
    public ComponentDiagramPageTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Title = "Checkout <components> & more";

    private static RequestResponseLog Log(string caller, string service, RequestResponseType type, Guid pair) =>
        new(TestName: "Places an order", TestId: "cd-page-test", Method: HttpMethod.Post, Content: null,
            Uri: new Uri($"http://{service.ToLowerInvariant()}/api"), Headers: [], ServiceName: service, CallerName: caller,
            Type: type, TraceId: Guid.NewGuid(), RequestResponseId: pair, TrackingIgnore: false);

    private string Generate(string folder)
    {
        var orders = Guid.NewGuid();
        var payments = Guid.NewGuid();
        var logs = new[]
        {
            Log("Web", "Orders", RequestResponseType.Request, orders),
            Log("Orders", "Payments", RequestResponseType.Request, payments),
            Log("Orders", "Payments", RequestResponseType.Response, payments),
            Log("Web", "Orders", RequestResponseType.Response, orders),
        };
        var result = ComponentDiagramReportGenerator.GenerateComponentDiagramReport(logs, new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(TempDir, folder),
            ComponentDiagramOptions = new ComponentDiagramOptions { Title = Title },
        });
        File.Copy(result.HtmlFilePath, Path.Combine(OutputDir, $"{folder}-ComponentDiagram.html"), true);
        return result.HtmlFilePath;
    }

    [Fact]
    public async Task The_page_is_drawn_in_standards_mode_and_names_itself()
    {
        await Page.GotoAsync(new Uri(Generate("component-page-standards")).AbsoluteUri);

        Assert.Equal("CSS1Compat", await Page.EvaluateAsync<string>("() => document.compatMode"));
        Assert.Equal(Title, await Page.TitleAsync());
        await Expect(Page.Locator("h1")).ToHaveTextAsync(Title);
    }

    [Fact]
    public async Task The_diagram_draws_at_the_size_it_drew_in_quirks_mode()
    {
        var page = Generate("component-page-size");
        var html = await File.ReadAllTextAsync(page);
        Assert.StartsWith("<!DOCTYPE html>", html);
        var quirks = Path.Combine(Path.GetDirectoryName(page)!, "ComponentDiagram.quirks.html");
        await File.WriteAllTextAsync(quirks, html["<!DOCTYPE html>".Length..]);

        var standards = await Drawn(page);
        var control = await Drawn(quirks);

        Assert.Equal("CSS1Compat", standards.Mode);
        Assert.Equal("BackCompat", control.Mode);
        Assert.Contains("Payments", standards.Text);
        Assert.InRange(standards.Width, control.Width - 1, control.Width + 1);
        Assert.InRange(standards.Height, control.Height - 1, control.Height + 1);
    }

    private async Task<(string Mode, float Width, float Height, string Text)> Drawn(string path)
    {
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        var svg = await WaitForDiagramSvg(60000);
        var box = await svg.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Width > 0 && box.Height > 0, $"the diagram painted {box.Width} x {box.Height}");
        return (await Page.EvaluateAsync<string>("() => document.compatMode"), box.Width, box.Height, await svg.TextContentAsync() ?? "");
    }
}
