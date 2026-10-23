using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Reqnroll;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #101, facts K5 and K6: filled names do not reorder an outline's rows or move their anchors. The rows keep the order
/// the adapter gave them when every row bore the outline's title, in the data file, on the page and in every
/// Specifications file, and each keeps its in-page slug (plans/OUTLINE_ROW_NAMES_PLAN.md 3.3, Q3).
/// </summary>
[Collection("DiagramsFetcher")]
public class OutlineRowOrderTests : IDisposable
{
    private const string Title = "<b> weighs <a>";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-outline-row-order-" + Guid.NewGuid().ToString("N"));

    public OutlineRowOrderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static ReqNRollScenarioInfo Info(string id, int block, bool filled, string a, string b) => new()
    {
        ScenarioId = id,
        ScenarioTitle = Title,
        RowName = filled ? $"{b} weighs {a}" : null,
        FeatureTitle = "Weights",
        ScenarioTags = [],
        CombinedTags = [],
        ExecutionStatus = ScenarioExecutionStatus.OK,
        OutlineId = Title,
        ExampleValues = new() { ["a"] = a, ["b"] = b },
        ExamplesBlockIndex = block,
        Duration = TimeSpan.FromMilliseconds(5),
    };

    /// <summary>
    /// Two blocks. The adapter orders the rows by block, then by their values: zebra, apple (block 0), mango (block 1).
    /// By their filled names they would be apple, mango, zebra: across the blocks and within the first.
    /// </summary>
    private static Feature[] Weights(bool filled = true) => new[]
        {
            Info("s2", 1, filled, "3", "mango"),
            Info("s1", 0, filled, "2", "apple"),
            Info("s0", 0, filled, "1", "zebra"),
        }
        .ToFeatures();

    private static readonly string[] AdapterOrder = ["zebra weighs 1", "apple weighs 2", "mango weighs 3"];

    private string Html(Feature[] features)
    {
        var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DiagramAsCode(s.Id, "", "@startuml\nA->B\n@enduml")).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(diagrams, features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, null,
            Path.Combine(_dir, $"Weights_{Guid.NewGuid():N}.html"), "Weights", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, suite: "Suite");
        return ReportHtml.Read(path);
    }

    private string Specifications(DataFormat format, string extension)
    {
        using var _ = ReportGenerator.ScopeReportsDirectory(_dir);
        return ReportHtml.Read(ReportGenerator.GenerateSpecificationsData(Weights(), $"Specifications_{Guid.NewGuid():N}.{extension}", "Weights", format));
    }

    [Fact]
    public void The_data_file_keeps_the_adapter_s_order()
    {
        var json = ReportGenerator.GenerateMergeableReportJson(Weights(), DateTime.UtcNow, DateTime.UtcNow,
            diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(AdapterOrder, document.RootElement.GetProperty("features")[0].GetProperty("scenarios").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public void The_page_keeps_the_rows_order_and_every_anchor()
    {
        static string[] RowIds(string html) =>
            Regex.Matches(html, "<tr [^>]*data-stable-id=\"([0-9a-f]{16})\"").Select(m => m.Groups[1].Value).ToArray();
        static string[] Anchors(string html) =>
            Regex.Matches(html, " id=\"(scenario-[^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

        var before = Html(Weights(filled: false));
        var after = Html(Weights());

        Assert.Equal(3, RowIds(after).Length);
        Assert.Equal(RowIds(before), RowIds(after));
        // K6: the rows keep their slugs, the outline's title's, and the group the one after them
        Assert.NotEmpty(Anchors(after));
        Assert.Equal(Anchors(before), Anchors(after));
    }

    [Fact]
    public void Specifications_yml_keeps_the_adapter_s_order()
    {
        var yaml = Specifications(DataFormat.Yaml, "yml");

        Assert.Equal(AdapterOrder, Regex.Matches(yaml, "- Scenario: (.*)").Select(m => m.Groups[1].Value.Trim().Trim('"')));
    }

    [Fact]
    public void Specifications_json_keeps_the_adapter_s_order()
    {
        using var json = JsonDocument.Parse(Specifications(DataFormat.Json, "json"));

        Assert.Equal(AdapterOrder, json.RootElement.GetProperty("features")[0].GetProperty("scenarios").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public void Specifications_xml_keeps_the_adapter_s_order()
    {
        var xml = XDocument.Parse(Specifications(DataFormat.Xml, "xml"));

        Assert.Equal(AdapterOrder, xml.Descendants("Scenario").Select(s => s.Element("Name")!.Value));
    }

    [Fact]
    public void Specifications_md_keeps_the_adapter_s_order()
    {
        var markdown = SpecificationsMarkdownGenerator.Generate(Weights(), "Weights");

        Assert.Equal(AdapterOrder, Regex.Matches(markdown, "^### (.*)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value.Trim()));
    }
}
