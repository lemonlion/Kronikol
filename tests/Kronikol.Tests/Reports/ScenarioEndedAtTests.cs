using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports;

/// <summary>
/// When a scenario ended (BACKGROUND_ATTRIBUTION_PLAN, option D). A dependency call that inherited a
/// scenario's identity after this instant is the host's background work, not the scenario's, and the
/// boundary has to travel in every format for a reader to say so. Millisecond precision, UTC, the same
/// spelling as an interaction's <c>timestamp</c>, so the two compare as strings.
/// </summary>
public class ScenarioEndedAtTests
{
    private static readonly DateTimeOffset Ended = new(2026, 1, 1, 10, 0, 3, 250, TimeSpan.Zero);

    private static Feature[] Timed(DateTimeOffset? endedAt = null) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(3.25), EndedAt = endedAt
                }
            ]
        }
    ];

    private static string Write(Feature[] features, DataFormat format)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        return ReportGenerator.GenerateTestRunReportData(
            features, DateTime.UtcNow, DateTime.UtcNow, $"EndedAt_{Guid.NewGuid():N}.{extension}", format);
    }

    private static JsonElement JsonScenario(Feature[] features)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Write(features, DataFormat.Json)));
        return document.RootElement.GetProperty("features").EnumerateArray().Single()
            .GetProperty("scenarios").EnumerateArray().Single().Clone();
    }

    private static XElement XmlScenario(Feature[] features) =>
        XDocument.Load(Write(features, DataFormat.Xml)).Root!
            .Element("Features")!.Element("Feature")!.Element("Scenarios")!.Element("Scenario")!;

    [Fact]
    public void The_json_says_when_the_scenario_ended_to_the_millisecond()
    {
        var scenario = JsonScenario(Timed(Ended));

        Assert.Equal("2026-01-01T10:00:03.250Z", scenario.GetProperty("endedAt").GetString());
    }

    [Fact]
    public void A_lane_that_did_not_record_an_end_writes_null()
    {
        var scenario = JsonScenario(Timed());

        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("endedAt").ValueKind);
    }

    [Fact]
    public void The_end_is_written_in_utc_whatever_offset_the_framework_recorded()
    {
        var scenario = JsonScenario(Timed(new DateTimeOffset(2026, 1, 1, 12, 0, 3, 250, TimeSpan.FromHours(2))));

        Assert.Equal("2026-01-01T10:00:03.250Z", scenario.GetProperty("endedAt").GetString());
    }

    [Fact]
    public void The_xml_carries_it_and_the_xsd_admits_it()
    {
        var xmlPath = Write(Timed(Ended), DataFormat.Xml);
        var scenario = XDocument.Load(xmlPath).Root!
            .Element("Features")!.Element("Feature")!.Element("Scenarios")!.Element("Scenario")!;
        Assert.Equal("2026-01-01T10:00:03.250Z", scenario.Element("EndedAt")!.Value);

        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(
            ReportGenerator.GenerateTestRunReportSchema($"EndedAt_{Guid.NewGuid():N}.xsd", DataFormat.Xml)))));
        var errors = new List<string>();
        XDocument.Load(xmlPath).Validate(schemaSet, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    [Fact]
    public void The_yaml_carries_it_at_the_scenario_indent()
    {
        var yaml = File.ReadAllText(Write(Timed(Ended), DataFormat.Yaml));

        var line = Assert.Single(yaml.Split((char)10), l => l.TrimStart().StartsWith("EndedAt:", StringComparison.Ordinal));
        Assert.StartsWith("        EndedAt: ", line, StringComparison.Ordinal);
        Assert.Contains("2026-01-01T10:00:03.250Z", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lane_that_did_not_record_an_end_omits_the_element_and_the_line()
    {
        Assert.Null(XmlScenario(Timed()).Element("EndedAt"));
        Assert.DoesNotContain("EndedAt:", File.ReadAllText(Write(Timed(), DataFormat.Yaml)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_declares_it_as_a_nullable_date_time()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(
            ReportGenerator.GenerateTestRunReportSchema($"EndedAt_{Guid.NewGuid():N}.schema.json", DataFormat.Json)));

        var endedAt = schema.RootElement.GetProperty("properties").GetProperty("features").GetProperty("items")
            .GetProperty("properties").GetProperty("scenarios").GetProperty("items")
            .GetProperty("properties").GetProperty("endedAt");

        Assert.Equal(["string", "null"], endedAt.GetProperty("type").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("date-time", endedAt.GetProperty("format").GetString());
    }

    [Fact]
    public void A_merged_report_keeps_the_end()
    {
        var json = ReportGenerator.GenerateMergeableReportJson(
            Timed(Ended),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null, componentRelationships: [], internalFlowSegmentData: null,
            wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

        var scenario = MergeableReportReader.Parse(json).Features.Single().Scenarios.Single();

        Assert.Equal(Ended, scenario.EndedAt);
    }
}
