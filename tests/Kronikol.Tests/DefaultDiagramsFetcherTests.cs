using System.Reflection;
using Kronikol.Tracking;

namespace Kronikol.Tests;

[Collection("DiagramsFetcher")]
public class DefaultDiagramsFetcherTests : IDisposable
{
    private static readonly FieldInfo DiagramsField =
        typeof(DefaultDiagramsFetcher).GetField("_diagrams", BindingFlags.Static | BindingFlags.NonPublic)!;

    public DefaultDiagramsFetcherTests()
    {
        DiagramsField.SetValue(null, null);
    }

    public void Dispose()
    {
        DiagramsField.SetValue(null, null);
    }

    private static string SeedLog()
    {
        var testId = Guid.NewGuid().ToString();
        RequestResponseLogger.Log(new RequestResponseLog(
            TestName: "Test",
            TestId: testId,
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri("http://example.com/api/orders"),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "WebApp",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false));
        return testId;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_nodejs_image_whose_path_carries_an_ampersand_decodes_to_well_formed_xml()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // Without internal-flow tracking a NodeJs report embeds each SVG as a data: image. A `&` the
        // renderer left bare made the browser refuse to parse it, and the whole diagram showed as a
        // broken image (DIAGRAM_COLOURS_PLAN F20).
        var testId = Guid.NewGuid().ToString();
        var pairId = Guid.NewGuid();
        RequestResponseLog Log(RequestResponseType type, string? content) => new(
            TestName: "Amp", TestId: testId, Method: HttpMethod.Get, Content: content,
            Uri: new Uri("http://example.com/api/items?page=1&size=10"), Headers: [], ServiceName: "Items",
            CallerName: "WebApp", Type: type, TraceId: Guid.NewGuid(), RequestResponseId: pairId, TrackingIgnore: false,
            StatusCode: type == RequestResponseType.Response ? System.Net.HttpStatusCode.OK : null);

        var fetcher = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions
        {
            PlantUmlRendering = PlantUmlRendering.NodeJs,
            PlantUmlImageFormat = PlantUmlImageFormat.Svg,
            InlineSvgRendering = false,
            Logs = [Log(RequestResponseType.Request, null), Log(RequestResponseType.Response, """{"dish":"fish & chips"}""")]
        });
        var diagram = fetcher().Single(d => d.TestRuntimeId == testId);

        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, diagram.ImgSrc);
        var svg = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(diagram.ImgSrc[prefix.Length..]));
        // The engine draws each space between words as its own <text>.
        var text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", System.Xml.Linq.XDocument.Parse(svg).Descendants()
            .Where(e => e.Name.LocalName == "text").Select(e => e.Value)), @"\s+", " ");
        Assert.Contains("?page=1&size=10", text);
        Assert.Contains("fish & chips", text);
    }

    [Fact]
    public void Default_options_produce_png_url()
    {
        var testId = SeedLog();
        var fetcher = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions
        {
            PlantUmlRendering = PlantUmlRendering.Server
        });
        var diagrams = fetcher();
        var diagram = diagrams.Single(d => d.TestRuntimeId == testId);

        Assert.Contains("/png/", diagram.ImgSrc);
    }

    [Fact]
    public void Svg_format_produces_svg_url()
    {
        var testId = SeedLog();
        var fetcher = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions
        {
            PlantUmlImageFormat = PlantUmlImageFormat.Svg,
            PlantUmlRendering = PlantUmlRendering.Server
        });
        var diagrams = fetcher();
        var diagram = diagrams.Single(d => d.TestRuntimeId == testId);

        Assert.Contains("/svg/", diagram.ImgSrc);
        Assert.DoesNotContain("/png/", diagram.ImgSrc);
    }

    [Fact]
    public void Png_format_produces_png_url()
    {
        var testId = SeedLog();
        var fetcher = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions
        {
            PlantUmlImageFormat = PlantUmlImageFormat.Png,
            PlantUmlRendering = PlantUmlRendering.Server
        });
        var diagrams = fetcher();
        var diagram = diagrams.Single(d => d.TestRuntimeId == testId);

        Assert.Contains("/png/", diagram.ImgSrc);
    }
}
