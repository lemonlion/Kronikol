using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// Examples: block identity must survive the mergeable-JSON round trip so parallel shards
/// reassemble their blocks (and the merged report renders the separator bands).
/// </summary>
public class ExamplesBlockMergeTests
{
    private static Feature[] BlockFeatures() =>
    [
        new Feature
        {
            DisplayName = "Market share",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "Movement (OneWeek)", Result = ExecutionResult.Passed,
                    OutlineId = "Movement",
                    ExampleValues = new Dictionary<string, string> { ["Period"] = "OneWeek" },
                    ExamplesBlockName = "the merchant gained share",
                    ExamplesBlockDescription = "movement is positive",
                    ExamplesBlockIndex = 0
                },
                new Scenario
                {
                    Id = "s2", DisplayName = "Movement (FourWeeks)", Result = ExecutionResult.Failed, ErrorMessage = "wrong direction",
                    OutlineId = "Movement",
                    ExampleValues = new Dictionary<string, string> { ["Period"] = "FourWeeks" },
                    ExamplesBlockName = "the merchant lost share",
                    ExamplesBlockIndex = 1
                },
                new Scenario
                {
                    Id = "s3", DisplayName = "Plain", Result = ExecutionResult.Passed
                }
            ]
        }
    ];

    private static string Serialize(Feature[] features) =>
        ReportGenerator.GenerateMergeableReportJson(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null,
            wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

    [Fact]
    public void Block_fields_round_trip_through_the_mergeable_json()
    {
        var report = MergeableReportReader.Parse(Serialize(BlockFeatures()));
        var scenarios = report.Features.Single().Scenarios;

        var s1 = scenarios.Single(s => s.Id == "s1");
        Assert.Equal("the merchant gained share", s1.ExamplesBlockName);
        Assert.Equal("movement is positive", s1.ExamplesBlockDescription);
        Assert.Equal(0, s1.ExamplesBlockIndex);

        var s2 = scenarios.Single(s => s.Id == "s2");
        Assert.Equal("the merchant lost share", s2.ExamplesBlockName);
        Assert.Null(s2.ExamplesBlockDescription);
        Assert.Equal(1, s2.ExamplesBlockIndex);

        var s3 = scenarios.Single(s => s.Id == "s3");
        Assert.Null(s3.ExamplesBlockName);
        Assert.Null(s3.ExamplesBlockDescription);
        Assert.Null(s3.ExamplesBlockIndex);
    }

    [Fact]
    public void Reports_without_block_fields_still_parse()
    {
        Feature[] plain =
        [
            new Feature
            {
                DisplayName = "F",
                Scenarios = [new Scenario { Id = "a", DisplayName = "A", Result = ExecutionResult.Passed }]
            }
        ];

        var report = MergeableReportReader.Parse(Serialize(plain));
        var scenario = report.Features.Single().Scenarios.Single();
        Assert.Null(scenario.ExamplesBlockIndex);
    }
}
