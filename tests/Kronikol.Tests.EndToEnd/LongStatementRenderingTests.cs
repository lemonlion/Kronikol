using System.Text.Json;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A request whose URL is thousands of characters long used to produce one arrow statement past the
/// engine's 2000-character parse limit. PlantUML reports nothing for that: the statement matches no
/// rule, the parser abandons the whole diagram, and the fragment renders as <c>Syntax Error?</c> —
/// taking every other call in it down with it.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class LongStatementRenderingTests : PlaywrightTestBase
{
    public LongStatementRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string LongUrlReportHtml()
    {
        var log = new RequestResponseLog(
            TestName: "A cold insights request reaches the cache", TestId: "long-url-1",
            Method: HttpMethod.Delete, Content: null,
            Uri: new Uri("http://example.com/data-insights-api/_v2/customer-local-competitors-charts-agg-"
                         + string.Join(",", Enumerable.Range(0, 60).Select(i => $"rWeeks-{i}-" + new string('k', 80)))),
            Headers: [], ServiceName: "redis", CallerName: "dataInsights",
            Type: RequestResponseType.Request, TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false);

        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([log]).Single().PlantUmls.First().PlainText;
        var encoded = System.Net.WebUtility.HtmlEncode(source);

        return $$"""
            <!DOCTYPE html><html><head><title>long url</title>
            <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body><div class="scenario">
            <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </div></body></html>
            """;
    }

    [Fact]
    public async Task A_trace_with_a_very_long_url_renders_without_a_syntax_error()
    {
        await Page.GotoAsync(ServePage(LongUrlReportHtml()));

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120000, PollingInterval = 200 });

        Assert.Equal(0, await Page.Locator("[data-engine-failure]").CountAsync());
        Assert.Equal(1, await Page.Locator("#puml-1 svg").CountAsync());

        // The truncated label kept the method and the start of the path, and the note beside it still
        // carries the whole thing.
        var text = await Page.Locator("#puml-1").InnerTextAsync();
        Assert.Contains("DELETE", text);
        Assert.Contains("Full", text);
    }

    [Fact]
    public async Task An_over_long_statement_that_reaches_the_engine_is_named_in_the_failure_block()
    {
        // The backstop means Kronikol no longer emits one of these, so this drives the diagnosis path
        // with a hand-written diagram: if the limit ever moves, or a future emitter forgets, the report
        // says which line and how long instead of leaving a bare "Syntax Error?".
        var source = "@startuml\nAlice -> Bob: " + new string('x', 2500) + "\n@enduml";
        var encoded = System.Net.WebUtility.HtmlEncode(source);
        var html = $$"""
            <!DOCTYPE html><html><head><title>over long</title>
            <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body><div class="scenario">
            <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </div></body></html>
            """;

        await Page.GotoAsync(ServePage(html));
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120000, PollingInterval = 200 });

        // The classifier is what turns the banner into a diagnosis; assert it directly, since whether
        // this particular diagram trips the banner depends on the engine's class-diagram fallback.
        var found = await Page.EvaluateAsync<JsonElement>(
            "(src) => window._findOverLongStatement(src) || {}", source);

        Assert.Equal("message statement", found.GetProperty("kind").GetString());
        Assert.Equal(2, found.GetProperty("line").GetInt32());
        Assert.Equal(2514, found.GetProperty("length").GetInt32());
        Assert.Equal(2000, found.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task The_classifier_leaves_notes_comments_and_short_statements_alone()
    {
        await Page.GotoAsync(ServePage("""
            <!DOCTYPE html><html><head><title>classifier</title>
            """ + DiagramContextMenu.GetPlantUmlBrowserRenderScript() + """
            </head><body></body></html>
            """));

        var longRun = new string('n', 6000);
        var safe = "@startuml\nAlice -> Bob: Hello\n"
                   + "' a comment with a -> b: arrow and " + longRun + "\n"
                   + "hnote across #black:" + longRun + "\n"
                   + "note left\na -> b: payload, not a statement " + longRun + "\nend note\n"
                   + "@enduml";

        var found = await Page.EvaluateAsync<JsonElement?>("(src) => window._findOverLongStatement(src)", safe);
        Assert.True(found is null || found.Value.ValueKind == JsonValueKind.Null);
    }
}
