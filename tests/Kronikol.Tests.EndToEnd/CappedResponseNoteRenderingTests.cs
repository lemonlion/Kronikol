using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A database response capped at <c>MaxResponseRows</c> is a COMPLETE JSON document with a
/// <c>... (N more rows not shown)</c> footnote after it — the shape every SQL/ClickHouse,
/// Spanner and MongoDB reader emits. It used to fail the note formatter's JSON parse and paint
/// as one wrapped minified blob while the uncapped response beside it painted as indented rows.
/// Driven through the REAL capture pipeline (<see cref="RequestResponseLog"/> →
/// <see cref="PlantUmlCreator"/>) so the fix is pinned at the generator AND on the painted SVG.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class CappedResponseNoteRenderingTests : PlaywrightTestBase
{
    public CappedResponseNoteRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string CappedRows =
        """[{"location_id":"216149122232148","unique_customers":53,"total_takings":1016.15},"""
        + """{"location_id":"216149122232149","unique_customers":41,"total_takings":802.40}]"""
        + "\n... (90 more rows not shown)";

    private static string CappedNoteHtml()
    {
        var traceId = Guid.NewGuid();
        var pairId = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            new("Customer insights are read from ClickHouse", "capped-1", "SELECT",
                "SELECT location_id, unique_customers FROM insights",
                new Uri("clickhouse://insights/customer_insights"), [],
                "ClickHouse", "DataInsights", RequestResponseType.Request, traceId, pairId,
                TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SQL),
            new("Customer insights are read from ClickHouse", "capped-1", "SELECT", CappedRows,
                new Uri("clickhouse://insights/customer_insights"), [],
                "ClickHouse", "DataInsights", RequestResponseType.Response, traceId, pairId,
                TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SQL),
        ];

        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;
        var encoded = System.Net.WebUtility.HtmlEncode(source);

        return $$"""
            <!DOCTYPE html><html><head><title>capped rows</title>
            <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body><div class="scenario">
            <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </div></body></html>
            """;
    }

    [Fact]
    public async Task A_row_capped_response_paints_as_indented_rows_with_the_cap_note_on_its_own_line()
    {
        await Page.GotoAsync(ServePage(CappedNoteHtml()));

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120000, PollingInterval = 200 });

        Assert.Equal(0, await Page.Locator("[data-engine-failure]").CountAsync());

        // PlantUML paints one <text> per WORD, so the display lines have to be rebuilt from the
        // baselines the words share before anything can be asserted about them.
        var painted = await GetPaintedSvgLines("#puml-1 svg");

        // Each property is its own painted line, and the footnote survives on a line of its own.
        Assert.Contains("\"unique_customers\": 53,", painted);
        Assert.Contains("\"total_takings\": 802.40", painted);
        Assert.Contains("... (90 more rows not shown)", painted);

        // The minified one-line form — what the note used to paint — is gone.
        Assert.DoesNotContain(painted, line => line.Contains("""[{"location_id":"""));
    }
}
