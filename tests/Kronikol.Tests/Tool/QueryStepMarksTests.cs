using Kronikol.Query;

namespace Kronikol.Tests.Tool;

/// <summary>What a row's mark in <c>query steps</c> says about how it ended: a step that did not run is not a step that passed (plans/STEP_IDENTITY_PLAN.md, R2).</summary>
public class QueryStepMarksTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-marks-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Report(string status, string interactions = "[]")
    {
        var path = Path.Combine(_directory, $"TestRunReport-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "5.28.3",
              "startTime": "2026-11-04T10:00:00Z",
              "endTime": "2026-11-04T10:05:00Z",
              "features": [
                { "name": "Orders", "labels": [], "scenarios": [
                  { "id": "t0", "stableId": "0000000000000000", "name": "A scenario", "result": "Passed", "labels": [], "categories": [],
                    "steps": [ { "keyword": "Given", "text": "a basket", "status": "{{status}}" } ],
                    "httpInteractions": {{interactions}}, "attachments": [] } ] }
              ]
            }
            """);
        return path;
    }

    private static string Row(string report)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["steps", report, "s0"], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.Contains("Given a basket", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Passed", " ")]
    [InlineData("Failed", "✗")]
    [InlineData("Bypassed", "~")]
    [InlineData("Skipped", "-")]
    [InlineData("SkippedAfterFailure", "-")]
    public void A_rows_mark_says_how_it_ended(string status, string mark) =>
        Assert.StartsWith(mark + " 0 ", Row(Report(status)));

    [Fact]
    public void A_step_that_ran_and_then_skipped_itself_reads_as_skipped_with_its_call_under_it()
    {
        // A LightBDD step can make a call and then ignore itself: the mark is what its status says, and the call stays listed.
        const string call = """[ { "type": "Request", "method": "GET", "uri": "http://shop/orders", "stepPath": "0", "traceId": "t", "serviceName": "Shop" } ]""";

        var row = Row(Report("Skipped", call));

        Assert.StartsWith("- 0 ", row);
        Assert.Contains("[i0]", row);
    }
}
