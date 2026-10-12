using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// An outline's example columns are the consumer's names, not the report's: a column called <c>result</c>, <c>name</c> or
/// <c>durationSeconds</c> sits in the scenario's <c>exampleFlatValues</c>, and the query engine read it as the scenario's
/// own field until 4.16.3, so a passing row with a <c>result</c> of 42 was counted as neither passed nor failed.
/// </summary>
public class ExampleColumnsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-example-columns").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_column_named_like_a_scenario_field_does_not_replace_it()
    {
        var columns = new Dictionary<string, string> { ["result"] = "42", ["name"] = "Bob", ["durationSeconds"] = "7", ["id"] = "x" };
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "row1", DisplayName = "Place an order for Bob", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1.5),
                        OutlineId = "place", ExampleValues = columns, ExampleFlatValues = columns
                    },
                    new Scenario { Id = "other", DisplayName = "Browse", Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(0.5) }
                ]
            }
        ];
        var written = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1),
            $"ExampleColumns_{Guid.NewGuid():N}.json", DataFormat.Json, null, []);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);

        using var json = JsonDocument.Parse(Query("scenarios", path, "--json"));
        var row = json.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("address").GetString() == "s0");

        Assert.Equal("Place an order for Bob", row.GetProperty("scenario").GetString());
        Assert.Equal("Passed", row.GetProperty("result").GetString());
        Assert.Equal(1.5, row.GetProperty("durationSeconds").GetDouble(), 6);
        Assert.Contains("\nOrders  1 passed, 1 FAILED\n", Query("summary", path));
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString().ReplaceLineEndings("\n");
    }
}
