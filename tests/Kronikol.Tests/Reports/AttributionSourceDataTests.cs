using System.Net;
using System.Text.Json;
using System.Xml.Schema;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Every capture says how it got its scenario, and the data files carry it: a reader — or the report's
/// own background attribution — can tell a call the scenario made from one that inherited its context.
/// </summary>
[Collection("DiagramsFetcher")]
public class AttributionSourceDataTests
{
    [Fact]
    public void The_json_data_carries_the_attribution_source_and_the_scenario_a_call_expired_from()
    {
        var traceId = Guid.NewGuid();
        var reqId = Guid.NewGuid();
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }]
            }
        };
        var logs = new[]
        {
            new RequestResponseLog("Place order", "test-1", HttpMethod.Post, null, new Uri("https://api.example.com/orders"), [],
                "OrderService", "TestClient", RequestResponseType.Request, traceId, reqId, false)
            { Timestamp = DateTimeOffset.UtcNow, AttributionSource = AttributionSource.TestContext },
            new RequestResponseLog("Place order", "test-1", HttpMethod.Post, null, new Uri("https://api.example.com/orders"), [],
                "OrderService", "TestClient", RequestResponseType.Response, traceId, reqId, false, HttpStatusCode.Created)
            { Timestamp = DateTimeOffset.UtcNow, AttributionSource = AttributionSource.Expired, ExpiredFromTestId = "test-0" }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_attribution.json", DataFormat.Json, trackedLogs: logs);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var interactions = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions");
        Assert.Equal("TestContext", interactions[0].GetProperty("attributionSource").GetString());
        Assert.Equal(JsonValueKind.Null, interactions[0].GetProperty("expiredFrom").ValueKind);
        Assert.Equal("Expired", interactions[1].GetProperty("attributionSource").GetString());
        Assert.Equal("test-0", interactions[1].GetProperty("expiredFrom").GetString());
    }

    [Fact]
    public void A_capture_that_predates_the_mark_reads_null()
    {
        var features = new[] { new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "test-2", DisplayName = "Old", Result = ExecutionResult.Passed }] } };
        var logs = new[]
        {
            new RequestResponseLog("Old", "test-2", HttpMethod.Get, null, new Uri("https://api.example.com/orders"), [],
                "OrderService", "TestClient", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_attribution_null.json", DataFormat.Json, trackedLogs: logs);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var interaction = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions")[0];
        Assert.Equal(JsonValueKind.Null, interaction.GetProperty("attributionSource").ValueKind);
    }

    [Fact]
    public void The_xsd_admits_the_provenance_elements_the_xml_writes()
    {
        // The XML writer emitted AttributionSource and ExpiredFrom from the day the mark existed; the XSD
        // beside it did not declare them, so every XML report with a provenance failed its own schema.
        var features = new[] { new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "test-3", DisplayName = "Marked", Result = ExecutionResult.Passed }] } };
        var traceId = Guid.NewGuid();
        var reqId = Guid.NewGuid();
        var logs = new[]
        {
            new RequestResponseLog("Marked", "test-3", HttpMethod.Get, null, new Uri("https://api.example.com/orders"), [],
                "OrderService", "TestClient", RequestResponseType.Request, traceId, reqId, false)
            { Timestamp = DateTimeOffset.UtcNow, AttributionSource = AttributionSource.TestContext },
            new RequestResponseLog("Marked", "test-3", HttpMethod.Get, null, new Uri("https://api.example.com/orders"), [],
                "OrderService", "TestClient", RequestResponseType.Response, traceId, reqId, false, HttpStatusCode.OK)
            { Timestamp = DateTimeOffset.UtcNow, AttributionSource = AttributionSource.Expired, ExpiredFromTestId = "test-0" }
        };

        var dataPath = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, $"TestRunData_attribution_{Guid.NewGuid():N}.xml", DataFormat.Xml, trackedLogs: logs);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"TestRunData_attribution_{Guid.NewGuid():N}.xsd", DataFormat.Xml);

        var schemaSet = new System.Xml.Schema.XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(schemaPath))));
        var errors = new List<string>();
        System.Xml.Linq.XDocument.Load(dataPath).Validate(schemaSet, (_, e) => errors.Add(e.Message));

        Assert.Empty(errors);
    }
}
