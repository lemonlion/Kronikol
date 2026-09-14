using System.Text.Json;
using System.Xml.Linq;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Where a scenario is written (LLM_FRIENDLY_PLAN M2.6). A step has carried <c>file:line</c> since 3.0.47,
/// but the scenario itself never did, so an agent that had found the failing scenario still had to guess
/// which file to open. The Gherkin lanes parse the path and the line already and threw both away.
/// <para>Two different contracts share the name <c>sourceFile</c> deliberately: a step's is a bare file
/// name, because it comes from <c>[CallerFilePath]</c> on the build machine and an absolute path is not
/// something to put in a downloadable artifact; a scenario's is the project-relative path the Gherkin
/// document already states. The schema descriptions say which is which.</para>
/// </summary>
public class SourceLocationTests
{
    private static Feature[] Located() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            SourceFile = "Features/Checkout.feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Failed,
                    SourceFile = "Features/Checkout.feature", SourceLine = 42,
                    ErrorMessage = "declined",
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "it charges", Status = ExecutionResult.Failed,
                            FailureMessage = "card declined", SourceFile = "CheckoutSteps.cs", SourceLine = 118
                        }
                    ]
                }
            ]
        }
    ];

    private static string Write(DataFormat format)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        return File.ReadAllText(ReportGenerator.GenerateTestRunReportData(
            Located(), DateTime.UtcNow, DateTime.UtcNow,
            $"SourceLoc_{Guid.NewGuid():N}.{extension}", format));
    }

    [Fact]
    public void The_json_says_which_file_and_line_the_scenario_is_on()
    {
        using var document = JsonDocument.Parse(Write(DataFormat.Json));
        var feature = document.RootElement.GetProperty("features").EnumerateArray().Single();

        Assert.Equal("Features/Checkout.feature", feature.GetProperty("sourceFile").GetString());
        var scenario = feature.GetProperty("scenarios").EnumerateArray().Single();
        Assert.Equal("Features/Checkout.feature", scenario.GetProperty("sourceFile").GetString());
        Assert.Equal(42, scenario.GetProperty("sourceLine").GetInt32());

        // The step keeps its own, different contract: a bare name, findable with Glob **/<file>.
        var step = scenario.GetProperty("steps").EnumerateArray().Single();
        Assert.Equal("CheckoutSteps.cs", step.GetProperty("sourceFile").GetString());
    }

    [Fact]
    public void A_lane_that_has_no_source_writes_the_keys_as_null()
    {
        // The common case: the tests NDJSON and the unit-test adapters supply nothing, and every
        // consumer has to render that correctly rather than treating it as the edge case.
        var features = Located();
        features[0].SourceFile = null;
        features[0].Scenarios[0].SourceFile = null;
        features[0].Scenarios[0].SourceLine = null;

        var path = ReportGenerator.GenerateTestRunReportData(
            features, DateTime.UtcNow, DateTime.UtcNow, $"SourceLoc_{Guid.NewGuid():N}.json", DataFormat.Json);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var scenario = document.RootElement.GetProperty("features").EnumerateArray().Single()
            .GetProperty("scenarios").EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("sourceFile").ValueKind);
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("sourceLine").ValueKind);
    }

    [Fact]
    public void The_xml_and_yaml_carry_it_too()
    {
        var feature = XDocument.Parse(Write(DataFormat.Xml)).Root!
            .Element("Features")!.Element("Feature")!;
        Assert.Equal("Features/Checkout.feature", feature.Element("SourceFile")!.Value);
        var scenario = feature.Element("Scenarios")!.Element("Scenario")!;
        Assert.Equal("Features/Checkout.feature", scenario.Element("SourceFile")!.Value);
        Assert.Equal("42", scenario.Element("SourceLine")!.Value);

        var yaml = Write(DataFormat.Yaml);
        Assert.Contains("    SourceFile: Features/Checkout.feature", yaml);
        Assert.Contains("        SourceLine: 42", yaml);
    }

    [Fact]
    public void A_merged_report_keeps_the_scenario_location_and_the_step_detail()
    {
        // The ride-along bug this closes: ReadSteps never read failureMessage, sourceFile or
        // sourceLine, though the writer has always written all three - so merging a sharded run
        // silently threw away every step's failure message and every step's location. Nothing said so.
        var json = ReportGenerator.GenerateMergeableReportJson(
            Located(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null, componentRelationships: [], internalFlowSegmentData: null,
            wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

        var report = MergeableReportReader.Parse(json);
        var feature = report.Features.Single();
        var scenario = feature.Scenarios.Single();

        Assert.Equal("Features/Checkout.feature", feature.SourceFile);
        Assert.Equal("Features/Checkout.feature", scenario.SourceFile);
        Assert.Equal(42, scenario.SourceLine);

        var step = scenario.Steps!.Single();
        Assert.Equal("card declined", step.FailureMessage);
        Assert.Equal("CheckoutSteps.cs", step.SourceFile);
        Assert.Equal(118, step.SourceLine);
    }

    [Fact]
    public void Merging_two_shards_keeps_the_path_even_when_one_shard_lacks_it()
    {
        // A feature split across parallel runners: whichever shard knows the path, the merged
        // feature knows it. The second shard holds a DIFFERENT scenario of the feature - the same one
        // twice with the same result and timing is the same runner's output supplied twice, which the
        // merge counts once, second shard and all.
        var withPath = Located();
        var withoutPath = Located();
        withoutPath[0].SourceFile = null;
        withoutPath[0].Scenarios[0].Id = "t2";
        withoutPath[0].Scenarios[0].DisplayName = "Pay by voucher";
        withoutPath[0].Scenarios[0].SourceFile = null;
        withoutPath[0].Scenarios[0].SourceLine = null;

        MergeableReport Shard(Feature[] features) => MergeableReportReader.Parse(
            ReportGenerator.GenerateMergeableReportJson(
                features, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
                diagramLookup: null, componentRelationships: [], internalFlowSegmentData: null,
                wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null));

        var merged = MergeableReportMerger.Merge([Shard(withoutPath), Shard(withPath)]);

        Assert.Equal("Features/Checkout.feature", merged.Features.Single().SourceFile);
    }

    [Fact]
    public void Query_says_where_the_failing_scenario_is_written()
    {
        var directory = Directory.CreateTempSubdirectory("kronikol-srcloc").FullName;
        try
        {
            var report = Path.Combine(directory, "TestRunReport.json");
            File.Move(ReportGenerator.GenerateTestRunReportData(
                Located(), DateTime.UtcNow, DateTime.UtcNow,
                $"SourceLoc_{Guid.NewGuid():N}.json", DataFormat.Json), report, overwrite: true);

            var output = new StringWriter();
            Assert.Equal(0, Kronikol.Tool.QueryCommand.Run(["failures", report], output, new StringWriter()));

            // The line an agent needs before it can open anything: which file, which line.
            Assert.Contains("Features/Checkout.feature:42", output.ToString());
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_digest_says_it_too()
    {
        var digest = FailuresDigestGenerator.Generate(Located(), null, "TestRunReport", "3.1.0");

        Assert.Contains("Features/Checkout.feature:42", digest.Markdown);

        var line = Assert.Single(FailuresJsonl.Failures(digest.Jsonl));
        Assert.Equal("Features/Checkout.feature", line.GetProperty("sourceFile").GetString());
        Assert.Equal(42, line.GetProperty("sourceLine").GetInt32());
    }
}
