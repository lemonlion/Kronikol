using System.Net;
using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>services</c> times every call once, whichever record carries its time (plans/WARM_UP_PLAN.md section 8). The data
/// file writes a call's <c>durationMs</c> on both of its records, and until 4.7.3 <c>services</c> read it from the
/// response alone, so a call recorded as one record (which the NDJSON ingest contract permits, with the capturer's own
/// measurement) took no time in the services table while <c>interactions --group-by service</c> counted it.
/// </summary>
public class ServicesDurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-services-duration").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_call_recorded_as_one_record_is_timed_once()
    {
        var report = Report();

        using var services = JsonDocument.Parse(Query("services", report, "--json"));
        var orders = services.RootElement.GetProperty("items").EnumerateArray().Single();
        using var grouped = JsonDocument.Parse(Query("interactions", report, "--group-by", "service", "--json"));
        var bucket = grouped.RootElement.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(2, orders.GetProperty("calls").GetInt32());
        Assert.Equal(260.0, orders.GetProperty("totalMs").GetDouble(), 3);
        Assert.Equal(250.0, orders.GetProperty("maxMs").GetDouble(), 3);
        // The two views of one service agree on its slowest call.
        Assert.Equal(bucket.GetProperty("maxMs").GetDouble(), orders.GetProperty("maxMs").GetDouble(), 3);
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private string Report()
    {
        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var (pairTrace, pairId) = (Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLog[] logs =
        [
            new("Place", "p1", HttpMethod.Post, "{}", new Uri("http://orders/orders"), [], "orders", "Test", RequestResponseType.Request, pairTrace, pairId, false)
                { Timestamp = at },
            new("Place", "p1", HttpMethod.Post, "{}", new Uri("http://orders/orders"), [], "orders", "Test", RequestResponseType.Response, pairTrace, pairId, false, HttpStatusCode.OK)
                { Timestamp = at.AddMilliseconds(10) },
            // One record, with the time its capturer measured.
            new("Place", "p1", HttpMethod.Get, null, new Uri("http://orders/orders/1"), [], "orders", "Test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
                { Timestamp = at.AddSeconds(1), DurationMs = 250 },
        ];
        Feature[] features = [new() { DisplayName = "Orders", Scenarios = [new Scenario { Id = "p1", DisplayName = "Place", Result = ExecutionResult.Passed }] }];

        var written = ReportGenerator.GenerateTestRunReportData(features, at.UtcDateTime, at.UtcDateTime.AddMinutes(1),
            $"Services_{Guid.NewGuid():N}.json", DataFormat.Json, null, logs);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }
}
