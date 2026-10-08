using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.ComponentDiagram;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #145: <c>query assertions</c> took every step without a keyword for an assertion, since that was the only sign a
/// tracked assertion left in the data file, and a framework's own keyword-less steps (<c>[Step]</c>, an ingested step,
/// a bridged NScenario-style step) have none either. The data file now marks a tracked assertion with
/// <c>"assertion": true</c>, written only on one, and the query reads the mark (SHOULDLY_ASSERTIONS_PLAN section 3.10).
/// </summary>
public class AssertionMarkTests
{
    private static Feature[] WithBoth() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "it charges", Status = ExecutionResult.Passed,
                            SubSteps =
                            [
                                new ScenarioStep { IsAssertion = true, Text = "✓ Total should be 3", Status = ExecutionResult.Passed },
                                new ScenarioStep { Text = "a step its framework records without a keyword", Status = ExecutionResult.Passed },
                            ]
                        }
                    ]
                }
            ]
        }
    ];

    private static Feature[] WithoutAssertions() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "When", Text = "it charges", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static string Write(Feature[] features, DataFormat format)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        return File.ReadAllText(ReportGenerator.GenerateTestRunReportData(
            features, DateTime.UtcNow, DateTime.UtcNow, $"AssertionMark_{Guid.NewGuid():N}.{extension}", format));
    }

    [Fact]
    public void An_assertion_sub_step_is_marked_in_the_data_file()
    {
        using var json = JsonDocument.Parse(Write(WithBoth(), DataFormat.Json));
        var subSteps = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0]
            .GetProperty("steps")[0].GetProperty("subSteps");

        Assert.True(subSteps[0].GetProperty("assertion").GetBoolean());
        Assert.False(subSteps[1].TryGetProperty("assertion", out _));

        var xml = XDocument.Parse(Write(WithBoth(), DataFormat.Xml));
        var xmlSubSteps = xml.Descendants("SubSteps").Single().Elements("Step").ToArray();
        Assert.Equal("true", xmlSubSteps[0].Element("Assertion")?.Value);
        Assert.Null(xmlSubSteps[1].Element("Assertion"));

        var yaml = Write(WithBoth(), DataFormat.Yaml).ReplaceLineEndings("\n");
        var lines = yaml.Split('\n');
        var mark = Assert.Single(lines.Select((line, i) => (line, i)).Where(l => l.line.TrimStart() == "Assertion: true"));
        Assert.Equal("- Keyword: \"\"", lines[mark.i - 1].TrimStart());
        Assert.Contains("Total should be 3", lines[mark.i + 1]);
    }

    [Fact]
    public void A_merged_report_keeps_the_mark()
    {
        var json = ReportGenerator.GenerateMergeableReportJson(
            WithBoth(), DateTime.UtcNow, DateTime.UtcNow,
            diagramLookup: null, componentRelationships: [], internalFlowSegmentData: null,
            wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

        var subSteps = MergeableReportReader.Parse(json).Features.Single().Scenarios.Single().Steps!.Single().SubSteps!;

        Assert.True(subSteps[0].IsAssertion);
        Assert.False(subSteps[1].IsAssertion);
    }

    [Fact]
    public void A_tracked_assertion_is_marked_in_process()
    {
        var testId = "assertion-mark-" + Guid.NewGuid();
        StepCollector.StartStep(testId, "Then", "the total is right", null, null);
        StepCollector.AddAssertionSubStep(testId, "total.ShouldBe(3)", passed: true);
        StepCollector.CompleteStep(testId, true);

        var step = Assert.Single(StepCollector.GetSteps(testId));
        StepCollector.ClearSteps(testId);

        Assert.False(step.IsAssertion);
        Assert.True(Assert.Single(step.SubSteps!).IsAssertion);
    }

    [Theory]
    [InlineData("4.14.0", 1)]
    [InlineData("4.14.0-local.141", 1)]
    [InlineData("4.13.1", 2)]
    [InlineData("4.12.0", 2)]
    public void A_step_without_a_keyword_is_not_an_assertion(string writtenBy, int assertions)
    {
        // A report that marks its assertions lists the marked one only; one written before the mark keeps the
        // inference, every keyword-less step.
        var directory = Directory.CreateTempSubdirectory("kronikol-assertion-mark").FullName;
        try
        {
            var report = Path.Combine(directory, "TestRunReport.json");
            var text = Regex.Replace(Write(WithBoth(), DataFormat.Json),
                "\"kronikolVersion\":\\s*\"[^\"]*\"", $"\"kronikolVersion\": \"{writtenBy}\"");
            File.WriteAllText(report, text);

            var index = ReportScanner.Scan(report);
            var marked = index.Scenarios.Single().AllSteps().Count(row => row.Step.IsAssertion);

            Assert.Equal(assertions, marked);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_report_without_assertions_is_written_as_before()
    {
        var json = Write(WithoutAssertions(), DataFormat.Json);
        var xml = Write(WithoutAssertions(), DataFormat.Xml);
        var yaml = Write(WithoutAssertions(), DataFormat.Yaml);

        Assert.DoesNotContain("assertion", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Assertion", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Assertion", yaml, StringComparison.Ordinal);
    }
}
