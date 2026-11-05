using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A step's id comes from one counter per process, so a report that wrote it would differ on every run, and it would be surface
/// for readers of the file. <c>RequestResponseLogRoundTripTests</c> reads public members only, so it cannot pin an internal one.
/// </summary>
public class StepIdentityIsNeverWrittenTests
{
    private const int TheId = 7654321;

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "F",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1",
                    DisplayName = "S",
                    Result = ExecutionResult.Passed,
                    BackgroundSteps = [new ScenarioStep { CollectedId = TheId, Keyword = "Given", Text = "shared", Status = ExecutionResult.Passed }],
                    Steps =
                    [
                        new ScenarioStep { CollectedId = TheId, OutsideFrameworkStep = true, Text = "a hook's", Status = ExecutionResult.Passed, SubSteps = [new ScenarioStep { CollectedId = TheId, Text = "a sub-step" }] },
                        new ScenarioStep { DidNotRun = true, Keyword = "Then", Text = "never ran", Status = ExecutionResult.Skipped },
                    ],
                }
            ],
        }
    ];

    private static RequestResponseLog[] Logs() =>
    [
        new("Test", "t1", HttpMethod.Get, null, new Uri("http://svc/x"), [], "svc", "api", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { OpenStep = TheId },
    ];

    private static void HoldsNoneOfIt(string text)
    {
        Assert.Contains("a hook's", text);
        Assert.DoesNotContain(TheId.ToString(), text);
        foreach (var name in new[] { nameof(ScenarioStep.CollectedId), nameof(ScenarioStep.OutsideFrameworkStep), nameof(ScenarioStep.DidNotRun), nameof(RequestResponseLog.OpenStep) })
            Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DataFormat.Json, "json")]
    [InlineData(DataFormat.Xml, "xml")]
    [InlineData(DataFormat.Yaml, "yml")]
    public void No_data_file_holds_a_steps_id_or_its_marks(DataFormat format, string extension)
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), DateTime.UtcNow, DateTime.UtcNow, $"StepIdentity_{Guid.NewGuid():N}.{extension}", format, trackedLogs: Logs());

        HoldsNoneOfIt(ReportHtml.Read(path));
    }

    [Fact]
    public void Nor_does_the_compressed_file()
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), DateTime.UtcNow, DateTime.UtcNow, $"StepIdentity_{Guid.NewGuid():N}.krkl", DataFormat.Json, null, Logs(), null, true, null, null, null, null, compressedFile: true);

        HoldsNoneOfIt(ReportFile.Read(path));
    }

    [Fact]
    public void Nor_does_a_record_on_the_ingest_feed()
    {
        var line = InteractionRecord.FromLog(Logs()[0]).ToJson();

        Assert.DoesNotContain(TheId.ToString(), line);
        Assert.DoesNotContain(nameof(RequestResponseLog.OpenStep), line, StringComparison.OrdinalIgnoreCase);
    }
}
