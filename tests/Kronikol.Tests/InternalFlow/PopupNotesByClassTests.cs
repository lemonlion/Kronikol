using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// The notes the generator writes into a popup are styled by class, on the theme's tokens in the popup's sheet (5.0.0,
/// plans/OPTION_C_PLAN.md S11), where each carried an inline colour: the left-out note, an empty popup's diagnostic
/// block (an in-process report's and an ingested one's), and the relationship table's "more tests" row.
/// </summary>
public class PopupNotesByClassTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_popup_that_left_spans_out_says_so_in_a_classed_paragraph()
    {
        var id = Guid.NewGuid();
        var segment = InternalFlowSegment.Of(id, RequestResponseType.Request, "test-a", T0, T0.AddMilliseconds(200), [], spansLeftOut: 2);

        var message = Message(InternalFlowHtmlGenerator.BuildSegmentData(new() { [Key(id)] = segment },
            InternalFlowDiagramStyle.ActivityDiagram), id);

        Assert.StartsWith("<p class=\"iflow-left-out\">2 spans that started during this call are left out", message);
        Assert.DoesNotContain("style=", message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_empty_popups_diagnostics_are_a_classed_block(bool ingested)
    {
        var id = Guid.NewGuid();
        var segment = InternalFlowSegment.Of(id, RequestResponseType.Request, "test-a", T0, T0.AddMilliseconds(200), []);

        var data = InternalFlowHtmlGenerator.BuildSegmentData(new() { [Key(id)] = segment },
            InternalFlowDiagramStyle.ActivityDiagram, showFlameChart: false,
            flameChartPosition: InternalFlowFlameChartPosition.BehindWithToggle,
            noDataBehavior: InternalFlowNoDataBehavior.ShowMessage,
            granularity: InternalFlowSpanGranularity.AutoInstrumentation, configuredActivitySources: null,
            startTab: InternalFlowTab.Activity, suppliedSpans: ingested ? 5 : null);
        var message = Message(data, id);

        Assert.Contains("<details class=\"iflow-diagnostics\"><summary>Diagnostic info</summary>", message);
        Assert.DoesNotContain("style=", message);
    }

    [Fact]
    public void A_relationships_table_past_twenty_tests_ends_in_a_classed_row()
    {
        var summaries = Enumerable.Range(1, 21).Select(i => new RelationshipTestSummary($"t{i}", $"Test {i}", 1, 10)).ToArray();
        var segment = InternalFlowSegment.Of(Guid.NewGuid(), RequestResponseType.Request, "", T0, T0.AddMilliseconds(200), []);

        var html = InternalFlowHtmlGenerator.GenerateRelationshipPopupContent(new RelationshipFlowData(segment, summaries),
            InternalFlowDiagramStyle.CallTree);

        Assert.Contains("<tr><td colspan=\"3\" class=\"iflow-more-tests\">...and 1 more tests</td></tr>", html);
        Assert.DoesNotContain("style=\"color", html);
    }

    private static string Key(Guid id) => $"iflow-{id}";

    private static string Message(Dictionary<string, object> data, Guid id) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(data))![Key(id)]
            .GetProperty("message").GetString()!;
}
