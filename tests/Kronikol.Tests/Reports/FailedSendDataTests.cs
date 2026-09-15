using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A call that threw is a call with an answer: the exception's type where the status would be, and the
/// message in <c>error</c>, in every data format (plans/ALTERNATING_AND_FAILED_SENDS_PLAN.md §3).
/// </summary>
[Collection("DiagramsFetcher")]
public class FailedSendDataTests
{
    private static Feature[] Features(string testId) =>
    [
        new Feature { DisplayName = "Specifications", Scenarios = [new Scenario { Id = testId, DisplayName = "AsyncAPI", Result = ExecutionResult.Passed }] }
    ];

    private static RequestResponseLog[] Threw(string testId)
    {
        var trace = Guid.NewGuid();
        var pair = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        return
        [
            new RequestResponseLog("AsyncAPI", testId, HttpMethod.Get, null, new Uri("http://localhost/asyncapi/v1.json"), [], "Breakfast Provider", "Caller",
                RequestResponseType.Request, trace, pair, false) { Timestamp = at, AttributionSource = AttributionSource.TestContext },
            new RequestResponseLog("AsyncAPI", testId, HttpMethod.Get, null, new Uri("http://localhost/asyncapi/v1.json"), [], "Breakfast Provider", "Caller",
                RequestResponseType.Response, trace, pair, false, "!HttpRequestException")
            { Timestamp = at.AddMilliseconds(12), AttributionSource = AttributionSource.TestContext, Error = "Error while copying content to a stream. Caused by: The pipe was advanced too far." }
        ];
    }

    private static string Write(string testId, DataFormat format)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        return ReportGenerator.GenerateTestRunReportData(Features(testId), DateTime.UtcNow, DateTime.UtcNow,
            $"FailedSend_{Guid.NewGuid():N}.{extension}", format, null, Threw(testId));
    }

    [Fact]
    public void The_json_carries_the_type_as_the_status_text_and_the_message_as_the_error()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Write("failed-1", DataFormat.Json)));

        var interactions = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions");
        var request = interactions[0];
        var response = interactions[1];
        Assert.Equal(JsonValueKind.Null, request.GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("statusCode").ValueKind);
        Assert.Equal("!HttpRequestException", response.GetProperty("statusText").GetString());
        Assert.Equal("Error while copying content to a stream. Caused by: The pipe was advanced too far.", response.GetProperty("error").GetString());
    }

    [Fact]
    public void The_json_schema_describes_the_error()
    {
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"FailedSend_{Guid.NewGuid():N}.schema.json", DataFormat.Json);
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var interaction = schema.RootElement.GetProperty("$defs").GetProperty("httpInteraction").GetProperty("properties");
        Assert.True(interaction.TryGetProperty("error", out var error));
        Assert.Contains("threw", error.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_xml_validates_against_its_schema_with_the_error_present()
    {
        var dataPath = Write("failed-2", DataFormat.Xml);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"FailedSend_{Guid.NewGuid():N}.xsd", DataFormat.Xml);

        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(schemaPath))));
        var errors = new List<string>();
        var document = XDocument.Load(dataPath);
        document.Validate(schemaSet, (_, e) => errors.Add(e.Message));

        Assert.Empty(errors);
        var error = Assert.Single(document.Descendants("Error"));
        Assert.Equal("Error while copying content to a stream. Caused by: The pipe was advanced too far.", error.Value);
    }

    [Fact]
    public void The_yaml_carries_the_error_and_validates()
    {
        var yamlPath = Write("failed-3", DataFormat.Yaml);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"FailedSend_{Guid.NewGuid():N}.schema.json", DataFormat.Yaml);

        Assert.Contains("Error: \"Error while copying content to a stream. Caused by: The pipe was advanced too far.\"", File.ReadAllText(yamlPath), StringComparison.Ordinal);
        Assert.Empty(YamlSchemaValidationTests.ValidateYaml(schemaPath, yamlPath));
    }
}
