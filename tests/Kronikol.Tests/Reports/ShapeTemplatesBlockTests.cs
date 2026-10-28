using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.History;
using Kronikol.Reports;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #192 R2 (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.5): the run's HistoryShapeTemplates in the data file, so a query
/// of the file templates a route as the run templated its history. Written only when there are rules, so every
/// report without them keeps its bytes.
/// </summary>
// A run of the whole generator draws through the process-wide diagram cache an ingest resets.
[Collection("DiagramsFetcher")]
public class ShapeTemplatesBlockTests
{
    private static readonly HistoryShapeTemplate[] Rules =
    [
        new(@"(?<=/cache/)[a-z]+-\d+", "{key}"),
        new(@"tenant-\d+: ""quoted""", "{tenant}")
    ];

    private static string Written(DataFormat format, IReadOnlyList<HistoryShapeTemplate>? templates)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "t0", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }]
            }
        ];
        var path = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Templates_" + Guid.NewGuid().ToString("N")[..8] + "." + format.ToString().ToLowerInvariant(), format, shapeTemplates: templates);
        try { return File.ReadAllText(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_templates_block_is_written_only_when_there_are_rules()
    {
        foreach (var none in new[] { null, Array.Empty<HistoryShapeTemplate>() })
        {
            Assert.DoesNotContain("shapeTemplates", Written(DataFormat.Json, none), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ShapeTemplates", Written(DataFormat.Yaml, none), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ShapeTemplates", Written(DataFormat.Xml, none), StringComparison.OrdinalIgnoreCase);
        }

        var block = JsonDocument.Parse(Written(DataFormat.Json, Rules)).RootElement.GetProperty("shapeTemplates");
        Assert.Equal(
            Rules.Select(r => (r.Pattern, r.Placeholder)),
            block.EnumerateArray().Select(t => (t.GetProperty("pattern").GetString()!, t.GetProperty("placeholder").GetString()!)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_run_writes_its_rules_into_its_data_file(bool mergeable)
    {
        // The run's own call sites, the standard file and the mergeable shard, not only the writer.
        var directory = Directory.CreateTempSubdirectory("kronikol-shape-templates").FullName;
        try
        {
            var options = new ReportConfigurationOptions
            {
                ReportsFolderPath = directory,
                GenerateMergeableData = mergeable,
                GenerateComponentDiagram = false,
                WriteRunSummaryToConsole = false,
                HistoryShapeTemplates = [.. Rules]
            };
            Feature[] features =
            [
                new Feature
                {
                    DisplayName = "Rules",
                    Scenarios = [new Scenario { Id = "rules-" + Guid.NewGuid().ToString("N"), DisplayName = "Rules", Result = ExecutionResult.Passed }]
                }
            ];

            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow, options,
                RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);

            var data = Directory.GetFiles(directory, "TestRunReport.*").Single(f => f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".krkl", StringComparison.Ordinal));
            var block = JsonDocument.Parse(ReportFile.Read(data)).RootElement.GetProperty("shapeTemplates");
            Assert.Equal(Rules.Select(r => r.Placeholder), block.EnumerateArray().Select(t => t.GetProperty("placeholder").GetString()!));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_engine_reads_the_block_into_its_index(bool compressedFile)
    {
        string Write(IReadOnlyList<HistoryShapeTemplate>? templates) => ReportGenerator.GenerateTestRunReportData(
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "t0", DisplayName = "Place an order", Result = ExecutionResult.Passed }] }],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            $"Templates_{Guid.NewGuid():N}.{(compressedFile ? "krkl" : "json")}", DataFormat.Json, null, null, null,
            fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null, compressedFile: compressedFile, shapeTemplates: templates);
        var with = Write(Rules);
        var without = Write(null);
        try
        {
            Assert.Equal(Rules, Kronikol.Query.ReportScanner.Scan(with).ShapeTemplates);
            Assert.Empty(Kronikol.Query.ReportScanner.Scan(without).ShapeTemplates);
        }
        finally
        {
            File.Delete(with);
            File.Delete(without);
        }
    }

    [Fact]
    public void The_xml_schema_admits_the_block()
    {
        var text = Written(DataFormat.Xml, Rules);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"Templates_{Guid.NewGuid():N}.xsd", DataFormat.Xml);
        var schemas = new System.Xml.Schema.XmlSchemaSet();
        try { schemas.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(schemaPath)))); }
        finally { File.Delete(schemaPath); }
        var errors = new List<string>();

        XDocument.Parse(text).Validate(schemas, (_, e) => errors.Add(e.Message));

        Assert.Empty(errors);
    }

    [Fact]
    public void A_mergeable_shard_carries_the_block_and_one_without_rules_does_not()
    {
        // kronikol merge reads the shards' rules (plan 4.5): a shard is the mergeable model.
        var model = ReportGenerator.BuildMergeableReportModel([], new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            null, null, null, null, WholeTestFlowVisualization.None, null, shapeTemplates: Rules);
        var none = ReportGenerator.BuildMergeableReportModel([], new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            null, null, null, null, WholeTestFlowVisualization.None, null);

        var block = JsonDocument.Parse(ReportGenerator.SerializeReportJson(model)).RootElement.GetProperty("shapeTemplates");
        Assert.Equal(Rules.Select(r => r.Pattern), block.EnumerateArray().Select(t => t.GetProperty("pattern").GetString()!));
        Assert.DoesNotContain("shapeTemplates", none.Keys);
    }

    [Fact]
    public void A_rule_given_no_placeholder_is_written_with_an_empty_one()
    {
        // The run's rules read a null placeholder as an empty one (HistoryShapeRules), so the file says what they did,
        // in every format, where the YAML writer threw on it.
        HistoryShapeTemplate[] rules = [new(@"\d+", null!)];

        var yaml = new YamlStream();
        yaml.Load(new StringReader(Written(DataFormat.Yaml, rules)));
        var entry = (YamlMappingNode)((YamlSequenceNode)((YamlMappingNode)yaml.Documents[0].RootNode).Children[new YamlScalarNode("ShapeTemplates")]).Children[0];
        Assert.Equal("", ((YamlScalarNode)entry.Children[new YamlScalarNode("Placeholder")]).Value);
        Assert.Equal("", JsonDocument.Parse(Written(DataFormat.Json, rules)).RootElement.GetProperty("shapeTemplates")[0].GetProperty("placeholder").GetString());
        Assert.Equal("", XDocument.Parse(Written(DataFormat.Xml, rules)).Root!.Element("ShapeTemplates")!.Element("ShapeTemplate")!.Element("Placeholder")!.Value);
    }

    [Fact]
    public void Rules_the_run_cannot_apply_are_left_out_and_the_file_is_still_written()
    {
        // The run's rules skip a null rule and an empty pattern (HistoryShapeRules.Create), so the file records only what
        // they apply; the writers threw on a null one and lost the file.
        HistoryShapeTemplate[] given = [null!, new("", "{x}"), new(@"\d+", "{n}")];
        HistoryShapeTemplate[] none = [null!, new("", "{x}")];

        var block = JsonDocument.Parse(Written(DataFormat.Json, given)).RootElement.GetProperty("shapeTemplates");

        Assert.Equal([@"\d+"], block.EnumerateArray().Select(t => t.GetProperty("pattern").GetString()!));
        Assert.DoesNotContain("shapeTemplates", Written(DataFormat.Json, none), StringComparison.Ordinal);
        Assert.DoesNotContain("ShapeTemplates", Written(DataFormat.Yaml, none), StringComparison.Ordinal);
        Assert.DoesNotContain("ShapeTemplates", Written(DataFormat.Xml, none), StringComparison.Ordinal);
        Assert.Single(XDocument.Parse(Written(DataFormat.Xml, given)).Root!.Element("ShapeTemplates")!.Elements("ShapeTemplate"));
    }

    [Fact]
    public void A_run_whose_rule_list_is_null_still_writes_its_data_file()
    {
        var directory = Directory.CreateTempSubdirectory("kronikol-shape-templates").FullName;
        try
        {
            var options = new ReportConfigurationOptions
            {
                ReportsFolderPath = directory,
                GenerateComponentDiagram = false,
                WriteRunSummaryToConsole = false,
                HistoryShapeTemplates = null!
            };
            Feature[] features =
            [
                new Feature
                {
                    DisplayName = "Rules",
                    Scenarios = [new Scenario { Id = "rules-" + Guid.NewGuid().ToString("N"), DisplayName = "Rules", Result = ExecutionResult.Passed }]
                }
            ];

            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow, options,
                RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);

            var data = Assert.Single(Directory.GetFiles(directory, "TestRunReport.*"), f => f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".krkl", StringComparison.Ordinal));
            Assert.DoesNotContain("shapeTemplates", ReportFile.Read(data), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void The_templates_block_is_in_every_data_format()
    {
        // The patterns as given, a quote and a colon included, read back from each format by a parser of that format.
        var yaml = new YamlStream();
        yaml.Load(new StringReader(Written(DataFormat.Yaml, Rules)));
        var yamlBlock = (YamlSequenceNode)((YamlMappingNode)yaml.Documents[0].RootNode).Children[new YamlScalarNode("ShapeTemplates")];
        Assert.Equal(
            Rules.Select(r => (r.Pattern, r.Placeholder)),
            yamlBlock.Children.Cast<YamlMappingNode>().Select(t =>
                (((YamlScalarNode)t.Children[new YamlScalarNode("Pattern")]).Value!, ((YamlScalarNode)t.Children[new YamlScalarNode("Placeholder")]).Value!)));

        var xmlBlock = XDocument.Parse(Written(DataFormat.Xml, Rules)).Root!.Element("ShapeTemplates")!;
        Assert.Equal(
            Rules.Select(r => (r.Pattern, r.Placeholder)),
            xmlBlock.Elements("ShapeTemplate").Select(t => (t.Element("Pattern")!.Value, t.Element("Placeholder")!.Value)));
    }
}
