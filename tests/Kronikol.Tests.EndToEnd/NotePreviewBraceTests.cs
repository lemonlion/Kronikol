using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A collapsed note's one-line preview drops a JSON body's opening brace, and its closing one when the whole body fits
/// (plans/OPTION_C_PLAN.md S9, P1, the owner's round 5): the braces say nothing on one line. A cut body ends in "..." and
/// has no closing brace left to drop; "{}" stays as it is, and an array keeps its brackets.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NotePreviewBraceTests : PlaywrightTestBase
{
    public NotePreviewBraceTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync()
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nA -> B : call\nnote right\n{\"a\":1}\nend note\n@enduml")],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, "NotePreviewBraces.html"), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.WaitForFunctionAsync("() => typeof window._getNotePreview === 'function'", null, new() { PollingInterval = 200 });
    }

    private Task<string> PreviewAsync(params string[] lines) => Page.EvaluateAsync<string>("l => window._getNotePreview(l)", lines);

    [Theory]
    [InlineData("{\"item\":\"Widget\",\"qty\":2}", "\"item\":\"Widget\",\"qty\":2")]
    [InlineData("{ \"item\": \"Widget\" }", "\"item\": \"Widget\"")]
    [InlineData("{}", "{}")]
    [InlineData("[1, 2]", "[1, 2]")]
    [InlineData("1 row", "1 row")]
    [InlineData("{\"note\":\"a } b\"}", "\"note\":\"a } b\"")]
    // only one object's braces: two objects (one a line, as NDJSON writes them) and a template's {{name}} keep theirs,
    // which until 5.2.7 previewed as "a":1} {"b":2 and {template}
    [InlineData("{\"a\":1} {\"b\":2}", "{\"a\":1} {\"b\":2}")]
    [InlineData("{{template}}", "{{template}}")]
    public async Task A_short_body_previews_without_its_braces(string body, string preview)
    {
        await OpenAsync();
        Assert.Equal(preview, await PreviewAsync(body));
    }

    [Fact]
    public async Task A_body_over_the_preview_length_is_cut_after_its_opening_brace()
    {
        await OpenAsync();
        var preview = await PreviewAsync("{", "  \"description\": \"" + new string('x', 70) + "\"", "}");

        Assert.StartsWith("\"description\"", preview);
        Assert.EndsWith("...", preview);
    }

    // a body cut before its object closes is still that object's start
    [Fact]
    public async Task A_body_cut_before_its_object_closes_loses_its_opening_brace()
    {
        await OpenAsync();
        Assert.Equal("\"a\": 1, \"b\":", await PreviewAsync("{", "  \"a\": 1,", "  \"b\":"));
    }

    [Fact]
    public async Task A_pretty_printed_body_that_fits_loses_both_braces()
    {
        await OpenAsync();
        Assert.Equal("\"item\": \"Widget\", \"qty\": 2", await PreviewAsync("{", "  \"item\": \"Widget\",", "  \"qty\": 2", "}"));
    }
}
