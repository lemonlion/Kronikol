using System.Net;
using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md A5 and Appendix A: 5.16.0 indexes the root background block's calls, but recognised a
/// compressed <c>content</c> only under a scenario's call, so a background statement of 8,192 characters or more in a
/// version-2 file lost its statement head, and the card its operation.
/// </summary>
public class BackgroundBodyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-card-background").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_compressed_body_under_a_root_background_call_is_read_as_a_scenarios_is()
    {
        var statement = "SELECT id FROM outbox WHERE note = '" + new string('x', ReportPayloads.Threshold + 100) + "'";
        var (scenarioCall, backgroundCall) = (Guid.NewGuid(), Guid.NewGuid());
        var unknown = TestIdentityScope.UnknownTestId;
        RequestResponseLog[] logs =
        [
            new("Order pancakes", "t1", HttpMethod.Get, null, new Uri("http://localhost/orders/1"), [], "Breakfast Provider", "Caller", RequestResponseType.Request, Guid.NewGuid(), scenarioCall, false),
            new("Order pancakes", "t1", HttpMethod.Get, null, new Uri("http://localhost/orders/1"), [], "Breakfast Provider", "Caller", RequestResponseType.Response, Guid.NewGuid(), scenarioCall, false, HttpStatusCode.OK),
            new(unknown, unknown, "Query", statement, new Uri("sql://localhost/outbox"), [], "Outbox Db", "Breakfast Provider", RequestResponseType.Request, Guid.NewGuid(), backgroundCall, false, DependencyCategory: "SQL")
                { AttributionSource = AttributionSource.Expired },
            new(unknown, unknown, "Query", null, new Uri("sql://localhost/outbox"), [], "Outbox Db", "Breakfast Provider", RequestResponseType.Response, Guid.NewGuid(), backgroundCall, false, "1 row", DependencyCategory: "SQL")
                { AttributionSource = AttributionSource.Expired }
        ];
        Feature[] features = [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "t1", DisplayName = "Order pancakes", Result = ExecutionResult.Passed }] }];
        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Path.Combine(_dir, "TestRunReport.json"), DataFormat.Json,
            null, logs, null, fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null, compressPayloads: true);

        using (var written = JsonDocument.Parse(File.ReadAllText(path)))
            Assert.Equal(JsonValueKind.Object, written.RootElement.GetProperty("background").GetProperty("interactions")[0].GetProperty("content").ValueKind);

        using var index = ReportScanner.Scan(path);
        var request = index.BackgroundInteractions.Single(i => i.Type == "Request");
        Assert.True(request.Body.Exists);
        Assert.Equal(statement, PayloadReader.Read(index, request.Body));
        var target = ServiceCard.GraphOf(index).Operations.Single(o => o.To == "Outbox Db").Target;
        Assert.StartsWith("/outbox SELECT id FROM outbox WHERE note = ", target, StringComparison.Ordinal);
    }
}
