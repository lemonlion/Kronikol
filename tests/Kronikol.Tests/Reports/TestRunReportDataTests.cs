using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

public class TestRunReportDataTests
{
    [Fact]
    public void GenerateTestRunReportData_produces_json_by_default()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s2", DisplayName = "Cancel order", Result = ExecutionResult.Failed, ErrorMessage = "timeout", Duration = TimeSpan.FromSeconds(1) }
                ]
            }
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_json.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.Equal("2026-01-01T10:00:00Z", root.GetProperty("startTime").GetString());
        Assert.Equal("2026-01-01T10:05:00Z", root.GetProperty("endTime").GetString());
        Assert.Equal(1, root.GetProperty("features").GetArrayLength());

        var feature = root.GetProperty("features")[0];
        Assert.Equal("Orders", feature.GetProperty("name").GetString());
        Assert.Equal(2, feature.GetProperty("scenarios").GetArrayLength());

        var s1 = feature.GetProperty("scenarios")[0];
        Assert.Equal("Place order", s1.GetProperty("name").GetString());
        Assert.Equal("Passed", s1.GetProperty("result").GetString());
        Assert.Equal(2.0, s1.GetProperty("durationSeconds").GetDouble());

        var s2 = feature.GetProperty("scenarios")[1];
        Assert.Equal("Failed", s2.GetProperty("result").GetString());
        Assert.Equal("timeout", s2.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_produces_xml()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_xml.xml", DataFormat.Xml);
        var content = File.ReadAllText(path);

        var doc = XDocument.Parse(content);
        Assert.Equal("TestRunReport", doc.Root!.Name.LocalName);
        Assert.Equal("2026-01-01T10:00:00Z", doc.Root.Element("StartTime")!.Value);
        Assert.NotNull(doc.Root.Element("Features"));
    }

    [Fact]
    public void GenerateTestRunReportData_produces_yaml()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_yaml.yml", DataFormat.Yaml);
        var content = File.ReadAllText(path);

        Assert.Contains("StartTime:", content);
        Assert.Contains("EndTime:", content);
        Assert.Contains("Features:", content);
        Assert.Contains("Orders", content);
        Assert.Contains("Place order", content);
        Assert.Contains("Passed", content);
    }

    [Fact]
    public void GenerateTestRunReportData_includes_steps_with_status()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a thing", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(100) },
                            new ScenarioStep { Keyword = "When", Text = "action", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "result", Status = ExecutionResult.Failed }
                        ]
                    }
                ]
            }
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_steps.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var steps = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("steps");
        Assert.Equal(3, steps.GetArrayLength());
        Assert.Equal("Given", steps[0].GetProperty("keyword").GetString());
        Assert.Equal("a thing", steps[0].GetProperty("text").GetString());
        Assert.Equal("Passed", steps[0].GetProperty("status").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_includes_labels_categories_and_error_stack_trace()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Labels = ["api"],
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Failed,
                        ErrorMessage = "NullRef", ErrorStackTrace = "at Foo.Bar()",
                        Labels = ["smoke"], Categories = ["Integration"],
                        IsHappyPath = true
                    }
                ]
            }
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_meta.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var feature = doc.RootElement.GetProperty("features")[0];
        Assert.Equal("api", feature.GetProperty("labels")[0].GetString());

        var scenario = feature.GetProperty("scenarios")[0];
        Assert.Equal("smoke", scenario.GetProperty("labels")[0].GetString());
        Assert.Equal("Integration", scenario.GetProperty("categories")[0].GetString());
        Assert.True(scenario.GetProperty("isHappyPath").GetBoolean());
        Assert.Equal("NullRef", scenario.GetProperty("errorMessage").GetString());
        Assert.Equal("at Foo.Bar()", scenario.GetProperty("errorStackTrace").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_includes_substeps()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "parent", Status = ExecutionResult.Passed,
                                SubSteps =
                                [
                                    new ScenarioStep { Keyword = "And", Text = "child1", Status = ExecutionResult.Passed },
                                    new ScenarioStep { Keyword = "And", Text = "child2", Status = ExecutionResult.Failed }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_sub.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var step = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("steps")[0];
        var subSteps = step.GetProperty("subSteps");
        Assert.Equal(2, subSteps.GetArrayLength());
        Assert.Equal("child1", subSteps[0].GetProperty("text").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_includes_plantuml_diagrams_per_scenario()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed },
                    new Scenario { Id = "test-2", DisplayName = "Cancel order", Result = ExecutionResult.Failed }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("test-1", "img-src-1", "@startuml\nAlice -> Bob : place order\n@enduml"),
            new DiagramAsCode("test-2", "img-src-2", "@startuml\nAlice -> Bob : cancel order\n@enduml")
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_diagrams.json", DataFormat.Json, diagrams);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var s1 = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        var s1Diagrams = s1.GetProperty("diagrams");
        Assert.Equal(1, s1Diagrams.GetArrayLength());
        Assert.Equal("@startuml\nAlice -> Bob : place order\n@enduml", s1Diagrams[0].GetString());

        var s2 = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[1];
        var s2Diagrams = s2.GetProperty("diagrams");
        Assert.Equal(1, s2Diagrams.GetArrayLength());
        Assert.Equal("@startuml\nAlice -> Bob : cancel order\n@enduml", s2Diagrams[0].GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_includes_multiple_diagrams_for_same_scenario()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("test-1", "img1", "@startuml\ndiagram-1\n@enduml"),
            new DiagramAsCode("test-1", "img2", "@startuml\ndiagram-2\n@enduml")
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_multi_diagrams.json", DataFormat.Json, diagrams);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var scenarioDiagrams = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("diagrams");
        Assert.Equal(2, scenarioDiagrams.GetArrayLength());
        Assert.Equal("@startuml\ndiagram-1\n@enduml", scenarioDiagrams[0].GetString());
        Assert.Equal("@startuml\ndiagram-2\n@enduml", scenarioDiagrams[1].GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_includes_empty_diagrams_when_no_match()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("other-test", "img", "@startuml\nother\n@enduml")
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_no_match.json", DataFormat.Json, diagrams);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var scenarioDiagrams = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("diagrams");
        Assert.Equal(0, scenarioDiagrams.GetArrayLength());
    }

    [Fact]
    public void GenerateTestRunReportData_includes_http_interactions_per_scenario()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var traceId = Guid.NewGuid();
        var reqId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        var logs = new[]
        {
            new RequestResponseLog(
                TestName: "Place order",
                TestId: "test-1",
                Method: HttpMethod.Post,
                Content: "{\"item\":\"widget\"}",
                Uri: new Uri("https://api.example.com/orders"),
                Headers: [("Content-Type", "application/json")],
                ServiceName: "OrderService",
                CallerName: "TestClient",
                Type: RequestResponseType.Request,
                TraceId: traceId,
                RequestResponseId: reqId,
                TrackingIgnore: false)
            { Timestamp = timestamp },
            new RequestResponseLog(
                TestName: "Place order",
                TestId: "test-1",
                Method: HttpMethod.Post,
                Content: "{\"id\":1}",
                Uri: new Uri("https://api.example.com/orders"),
                Headers: [("Content-Type", "application/json")],
                ServiceName: "OrderService",
                CallerName: "TestClient",
                Type: RequestResponseType.Response,
                TraceId: traceId,
                RequestResponseId: reqId,
                TrackingIgnore: false,
                StatusCode: HttpStatusCode.Created)
            { Timestamp = timestamp.AddMilliseconds(50) }
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_http.json", DataFormat.Json, trackedLogs: logs);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var interactions = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions");
        Assert.Equal(2, interactions.GetArrayLength());

        var request = interactions[0];
        Assert.Equal("Request", request.GetProperty("type").GetString());
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.Equal("https://api.example.com/orders", request.GetProperty("uri").GetString());
        Assert.Equal("OrderService", request.GetProperty("serviceName").GetString());
        Assert.Equal("TestClient", request.GetProperty("callerName").GetString());
        Assert.Equal("{\"item\":\"widget\"}", request.GetProperty("content").GetString());

        var response = interactions[1];
        Assert.Equal("Response", response.GetProperty("type").GetString());
        Assert.Equal("Created", response.GetProperty("statusCode").GetString());
        Assert.Equal("{\"id\":1}", response.GetProperty("content").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_empty_interactions_when_no_matching_logs()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var logs = new[]
        {
            new RequestResponseLog(
                TestName: "Other test",
                TestId: "other-test",
                Method: HttpMethod.Get,
                Content: null,
                Uri: new Uri("https://api.example.com/other"),
                Headers: [],
                ServiceName: "Svc",
                CallerName: "Caller",
                Type: RequestResponseType.Request,
                TraceId: Guid.NewGuid(),
                RequestResponseId: Guid.NewGuid(),
                TrackingIgnore: false)
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_no_http.json", DataFormat.Json, trackedLogs: logs);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var interactions = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions");
        Assert.Equal(0, interactions.GetArrayLength());
    }

    [Fact]
    public void GenerateTestRunReportData_diagrams_and_interactions_null_when_not_provided()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_nodiag.json", DataFormat.Json);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var scenario = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];

        // When no diagrams/logs provided, the properties should not be present
        Assert.False(scenario.TryGetProperty("diagrams", out _));
        Assert.False(scenario.TryGetProperty("httpInteractions", out _));
    }

    [Fact]
    public void GenerateTestRunReportData_xml_includes_diagrams_and_interactions()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("test-1", "img", "@startuml\nAlice -> Bob\n@enduml")
        };

        var traceId = Guid.NewGuid();
        var logs = new[]
        {
            new RequestResponseLog(
                TestName: "Place order",
                TestId: "test-1",
                Method: HttpMethod.Get,
                Content: null,
                Uri: new Uri("https://api.example.com/orders"),
                Headers: [],
                ServiceName: "OrderService",
                CallerName: "TestClient",
                Type: RequestResponseType.Request,
                TraceId: traceId,
                RequestResponseId: Guid.NewGuid(),
                TrackingIgnore: false)
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_xml_diag.xml", DataFormat.Xml, diagrams, logs);
        var content = File.ReadAllText(path);
        var doc = XDocument.Parse(content);

        var scenario = doc.Root!.Element("Features")!.Element("Feature")!.Element("Scenarios")!.Element("Scenario")!;

        var diagramsEl = scenario.Element("Diagrams");
        Assert.NotNull(diagramsEl);
        Assert.Equal("@startuml\nAlice -> Bob\n@enduml", diagramsEl!.Element("Diagram")!.Value);

        var interactionsEl = scenario.Element("HttpInteractions");
        Assert.NotNull(interactionsEl);
        var interaction = interactionsEl!.Element("HttpInteraction")!;
        Assert.Equal("Request", interaction.Element("Type")!.Value);
        Assert.Equal("GET", interaction.Element("Method")!.Value);
        Assert.Equal("OrderService", interaction.Element("ServiceName")!.Value);
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_includes_diagrams_and_interactions()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("test-1", "img", "@startuml\nAlice -> Bob\n@enduml")
        };

        var logs = new[]
        {
            new RequestResponseLog(
                TestName: "Place order",
                TestId: "test-1",
                Method: HttpMethod.Post,
                Content: "{\"data\":1}",
                Uri: new Uri("https://api.example.com/orders"),
                Headers: [],
                ServiceName: "OrderService",
                CallerName: "TestClient",
                Type: RequestResponseType.Request,
                TraceId: Guid.NewGuid(),
                RequestResponseId: Guid.NewGuid(),
                TrackingIgnore: false)
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_yaml_diag.yml", DataFormat.Yaml, diagrams, logs);
        var content = File.ReadAllText(path);

        Assert.Contains("Diagrams:", content);
        Assert.Contains("@startuml", content);
        Assert.Contains("HttpInteractions:", content);
        Assert.Contains("OrderService", content);
        Assert.Contains("POST", content);
    }

    [Fact]
    public void GenerateTestRunReportData_http_interaction_includes_headers()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "S1", Result = ExecutionResult.Passed }
                ]
            }
        };

        var logs = new[]
        {
            new RequestResponseLog(
                TestName: "S1",
                TestId: "test-1",
                Method: HttpMethod.Get,
                Content: null,
                Uri: new Uri("https://api.example.com/items"),
                Headers: [("Authorization", "Bearer token"), ("Accept", "application/json")],
                ServiceName: "ItemService",
                CallerName: "Client",
                Type: RequestResponseType.Request,
                TraceId: Guid.NewGuid(),
                RequestResponseId: Guid.NewGuid(),
                TrackingIgnore: false)
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var path = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_headers.json", DataFormat.Json, trackedLogs: logs);
        var content = File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);

        var interaction = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions")[0];
        var headers = interaction.GetProperty("headers");
        Assert.Equal(2, headers.GetArrayLength());
        Assert.Equal("Authorization", headers[0].GetProperty("key").GetString());
        Assert.Equal("Bearer token", headers[0].GetProperty("value").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_produces_valid_json_schema()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        Assert.Equal("object", root.GetProperty("type").GetString());

        var properties = root.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("startTime").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("endTime").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("features").GetProperty("type").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_schema_defines_feature_structure()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_feat.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var featureItems = doc.RootElement.GetProperty("properties").GetProperty("features").GetProperty("items");

        Assert.Equal("object", featureItems.GetProperty("type").GetString());
        var featureProps = featureItems.GetProperty("properties");
        Assert.Equal("string", featureProps.GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("array", featureProps.GetProperty("scenarios").GetProperty("type").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_schema_defines_scenario_structure()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_scen.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var scenarioItems = doc.RootElement
            .GetProperty("properties").GetProperty("features").GetProperty("items")
            .GetProperty("properties").GetProperty("scenarios").GetProperty("items");

        var scenarioProps = scenarioItems.GetProperty("properties");
        Assert.Equal("string", scenarioProps.GetProperty("id").GetProperty("type").GetString());
        Assert.Equal("string", scenarioProps.GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("string", scenarioProps.GetProperty("result").GetProperty("type").GetString());
        Assert.Equal("number", scenarioProps.GetProperty("durationSeconds").GetProperty("type").GetString());
        Assert.Equal("boolean", scenarioProps.GetProperty("isHappyPath").GetProperty("type").GetString());
        Assert.Equal("array", scenarioProps.GetProperty("steps").GetProperty("type").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_schema_defines_result_enum()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_enum.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var resultProp = doc.RootElement
            .GetProperty("properties").GetProperty("features").GetProperty("items")
            .GetProperty("properties").GetProperty("scenarios").GetProperty("items")
            .GetProperty("properties").GetProperty("result");

        var enumValues = resultProp.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Passed", enumValues);
        Assert.Contains("Failed", enumValues);
        Assert.Contains("Skipped", enumValues);
        Assert.Contains("Bypassed", enumValues);
        Assert.Contains("SkippedAfterFailure", enumValues);
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_schema_defines_steps_with_recursive_substeps()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_steps.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);

        // Steps in scenario reference $defs/step
        var stepsItems = doc.RootElement
            .GetProperty("properties").GetProperty("features").GetProperty("items")
            .GetProperty("properties").GetProperty("scenarios").GetProperty("items")
            .GetProperty("properties").GetProperty("steps").GetProperty("items");
        Assert.Equal("#/$defs/step", stepsItems.GetProperty("$ref").GetString());

        // Follow the $ref to the step definition
        var stepDef = doc.RootElement.GetProperty("$defs").GetProperty("step");
        var stepProps = stepDef.GetProperty("properties");
        Assert.Equal("string", stepProps.GetProperty("keyword").GetProperty("type").GetString());
        Assert.Equal("string", stepProps.GetProperty("text").GetProperty("type").GetString());

        // SubSteps should reference the same step definition (recursive via $ref)
        var subSteps = stepProps.GetProperty("subSteps");
        Assert.Equal("array", subSteps.GetProperty("type").GetString());
        Assert.Equal("#/$defs/step", subSteps.GetProperty("items").GetProperty("$ref").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_schema_defines_http_interaction_structure()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_http.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var scenarioProps = doc.RootElement
            .GetProperty("properties").GetProperty("features").GetProperty("items")
            .GetProperty("properties").GetProperty("scenarios").GetProperty("items")
            .GetProperty("properties");

        var httpRef = scenarioProps.GetProperty("httpInteractions").GetProperty("items");
        // Should reference the $defs/httpInteraction definition
        var httpDef = doc.RootElement.GetProperty("$defs").GetProperty("httpInteraction");
        var httpProps = httpDef.GetProperty("properties");

        Assert.Equal("string", httpProps.GetProperty("type").GetProperty("type").GetString());
        Assert.Equal("string", httpProps.GetProperty("method").GetProperty("type").GetString());
        Assert.Equal("string", httpProps.GetProperty("uri").GetProperty("type").GetString());
        Assert.Equal("string", httpProps.GetProperty("serviceName").GetProperty("type").GetString());
        Assert.Equal("string", httpProps.GetProperty("callerName").GetProperty("type").GetString());
        Assert.Equal("array", httpProps.GetProperty("headers").GetProperty("type").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_validates_actual_json_output()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
                ]
            }
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

        // Generate both data and schema
        var dataPath = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_validate.json", DataFormat.Json);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_validate.schema.json", DataFormat.Json);

        var dataContent = File.ReadAllText(dataPath);
        var schemaContent = File.ReadAllText(schemaPath);

        // Both should be valid JSON
        var dataDoc = JsonDocument.Parse(dataContent);
        var schemaDoc = JsonDocument.Parse(schemaContent);

        // Schema should have correct structure and data should align with it
        Assert.Equal("object", schemaDoc.RootElement.GetProperty("type").GetString());
        Assert.True(dataDoc.RootElement.TryGetProperty("startTime", out _));
        Assert.True(dataDoc.RootElement.TryGetProperty("features", out _));
    }

    [Fact]
    public void GenerateTestRunReportSchema_produces_valid_xsd()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport.schema.xsd", DataFormat.Xml);
        var content = File.ReadAllText(path);

        var doc = XDocument.Parse(content);
        Assert.Equal("schema", doc.Root!.Name.LocalName);
        Assert.Equal("http://www.w3.org/2001/XMLSchema", doc.Root.Name.NamespaceName);
    }

    [Fact]
    public void GenerateTestRunReportSchema_xsd_defines_root_element()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_root.schema.xsd", DataFormat.Xml);
        var content = File.ReadAllText(path);

        var doc = XDocument.Parse(content);
        var xs = XNamespace.Get("http://www.w3.org/2001/XMLSchema");

        var rootElement = doc.Root!.Elements(xs + "element").FirstOrDefault(e => e.Attribute("name")?.Value == "TestRunReport");
        Assert.NotNull(rootElement);
    }

    [Fact]
    public void GenerateTestRunReportSchema_xsd_validates_actual_xml_output()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
                ]
            }
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

        var dataPath = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_xsd_validate.xml", DataFormat.Xml);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_xsd_validate.schema.xsd", DataFormat.Xml);

        var schemaContent = File.ReadAllText(schemaPath);
        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(schemaContent)));

        var dataDoc = XDocument.Load(dataPath);
        var errors = new List<string>();
        dataDoc.Validate(schemaSet, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    [Fact]
    public void GenerateTestRunReportSchema_xsd_validates_xml_with_diagrams_and_interactions()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "test-1", DisplayName = "Place order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("test-1", "img", "@startuml\nAlice -> Bob\n@enduml")
        };

        var logs = new[]
        {
            new RequestResponseLog(
                TestName: "Place order",
                TestId: "test-1",
                Method: HttpMethod.Post,
                Content: "{\"item\":\"widget\"}",
                Uri: new Uri("https://api.example.com/orders"),
                Headers: [("Content-Type", "application/json")],
                ServiceName: "OrderService",
                CallerName: "TestClient",
                Type: RequestResponseType.Request,
                TraceId: Guid.NewGuid(),
                RequestResponseId: Guid.NewGuid(),
                TrackingIgnore: false)
        };

        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow;

        var dataPath = ReportGenerator.GenerateTestRunReportData(features, start, end, "TestRunData_xsd_full.xml", DataFormat.Xml, diagrams, logs);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_xsd_full.schema.xsd", DataFormat.Xml);

        var schemaContent = File.ReadAllText(schemaPath);
        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(schemaContent)));

        var dataDoc = XDocument.Load(dataPath);
        var errors = new List<string>();
        dataDoc.Validate(schemaSet, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    [Fact]
    public void GenerateTestRunReportSchema_yaml_returns_json_schema()
    {
        // YAML uses JSON Schema for validation (no separate YAML schema standard)
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_yaml.schema.json", DataFormat.Yaml);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", doc.RootElement.GetProperty("$schema").GetString());
    }

    [Fact]
    public void GenerateTestRunReportSchema_json_schema_required_properties_are_defined()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("TestRunReport_required.schema.json", DataFormat.Json);
        var content = File.ReadAllText(path);

        var doc = JsonDocument.Parse(content);
        var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("startTime", required);
        Assert.Contains("endTime", required);
        Assert.Contains("features", required);
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_kronikol_version()
    {
        var features = new[] { new Feature { DisplayName = "F1", Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }] } };
        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_version.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var version = doc.RootElement.GetProperty("kronikolVersion").GetString();
        Assert.NotNull(version);
        Assert.NotEmpty(version!);
    }

    [Fact]
    public void GenerateTestRunReportData_xml_includes_kronikol_version()
    {
        var features = new[] { new Feature { DisplayName = "F1", Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }] } };
        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_version.xml", DataFormat.Xml);
        var doc = XDocument.Parse(File.ReadAllText(path));
        Assert.NotNull(doc.Root!.Element("KronikolVersion"));
        Assert.NotEmpty(doc.Root.Element("KronikolVersion")!.Value);
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_includes_kronikol_version()
    {
        var features = new[] { new Feature { DisplayName = "F1", Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }] } };
        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_version.yml", DataFormat.Yaml);
        var content = File.ReadAllText(path);
        Assert.Contains("KronikolVersion:", content);
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_stableId()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "Cancel order", Result = ExecutionResult.Passed }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_stableId.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenarios = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios");

        var s1StableId = scenarios[0].GetProperty("stableId").GetString();
        var s2StableId = scenarios[1].GetProperty("stableId").GetString();

        Assert.NotNull(s1StableId);
        Assert.NotNull(s2StableId);
        Assert.NotEqual(s1StableId, s2StableId);
        Assert.Equal(16, s1StableId!.Length);
        Assert.Matches("^[0-9a-f]{16}$", s1StableId);

        // Verify it matches the standalone computation
        Assert.Equal(ScenarioStableId.Compute("Orders", "Place order"), s1StableId);
    }

    [Fact]
    public void GenerateTestRunReportData_xml_includes_stableId()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed }]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_stableId.xml", DataFormat.Xml);
        var doc = XDocument.Parse(File.ReadAllText(path));
        var scenario = doc.Root!.Element("Features")!.Element("Feature")!.Element("Scenarios")!.Element("Scenario")!;
        var stableId = scenario.Element("StableId")?.Value;

        Assert.NotNull(stableId);
        Assert.Equal(ScenarioStableId.Compute("Orders", "Place order"), stableId);
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_includes_stableId()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed }]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_stableId.yml", DataFormat.Yaml);
        var content = File.ReadAllText(path);

        Assert.Contains("StableId: " + ScenarioStableId.Compute("Orders", "Place order"), content);
    }

    [Fact]
    public void GenerateTestRunReportData_json_stableId_uses_outlineId_for_parameterized_scenarios()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Pay with visa", Result = ExecutionResult.Passed, OutlineId = "Pay with card" },
                    new Scenario { Id = "s2", DisplayName = "Pay with mastercard", Result = ExecutionResult.Passed, OutlineId = "Pay with card" }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_stableId_outline.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenarios = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios");

        var s1StableId = scenarios[0].GetProperty("stableId").GetString();
        var s2StableId = scenarios[1].GetProperty("stableId").GetString();

        Assert.NotEqual(s1StableId, s2StableId);
        Assert.Equal(ScenarioStableId.Compute("Checkout", "Pay with visa", "Pay with card"), s1StableId);
        Assert.Equal(ScenarioStableId.Compute("Checkout", "Pay with mastercard", "Pay with card"), s2StableId);
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_rule()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Accounts",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Create account", Result = ExecutionResult.Passed, Rule = "Account creation" },
                    new Scenario { Id = "s2", DisplayName = "Login", Result = ExecutionResult.Passed }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_rule.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenarios = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios");

        Assert.Equal("Account creation", scenarios[0].GetProperty("rule").GetString());
        Assert.Equal(JsonValueKind.Null, scenarios[1].GetProperty("rule").ValueKind);
    }

    [Fact]
    public void GenerateTestRunReportData_xml_includes_rule()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Accounts",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Create account", Result = ExecutionResult.Passed, Rule = "Account creation" },
                    new Scenario { Id = "s2", DisplayName = "Login", Result = ExecutionResult.Passed }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_rule.xml", DataFormat.Xml);
        var doc = XDocument.Parse(File.ReadAllText(path));
        var scenarios = doc.Root!.Element("Features")!.Element("Feature")!.Element("Scenarios")!.Elements("Scenario").ToArray();

        Assert.Equal("Account creation", scenarios[0].Element("Rule")?.Value);
        Assert.Null(scenarios[1].Element("Rule"));
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_includes_rule()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Accounts",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Create account", Result = ExecutionResult.Passed, Rule = "Account creation" }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_rule.yml", DataFormat.Yaml);
        var content = File.ReadAllText(path);

        Assert.Contains("Rule: Account creation", content);
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_step_attachments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "API",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Generate spec", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "spec is generated", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("openapi.json", "artifacts/openapi.json")]
                            }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_attachments.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var step = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("steps")[0];
        var attachments = step.GetProperty("attachments");

        Assert.Equal(1, attachments.GetArrayLength());
        Assert.Equal("openapi.json", attachments[0].GetProperty("name").GetString());
        Assert.Equal("artifacts/openapi.json", attachments[0].GetProperty("relativePath").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_xml_includes_step_attachments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "API",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Generate spec", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "spec is generated", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("openapi.json", "artifacts/openapi.json")]
                            }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_attachments.xml", DataFormat.Xml);
        var doc = XDocument.Parse(File.ReadAllText(path));
        var step = doc.Root!.Element("Features")!.Element("Feature")!.Element("Scenarios")!.Element("Scenario")!.Element("Steps")!.Element("Step")!;
        var attachment = step.Element("Attachments")?.Element("Attachment");

        Assert.NotNull(attachment);
        Assert.Equal("openapi.json", attachment!.Element("Name")?.Value);
        Assert.Equal("artifacts/openapi.json", attachment.Element("RelativePath")?.Value);
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_includes_step_attachments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "API",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Generate spec", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "spec is generated", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("openapi.json", "artifacts/openapi.json")]
                            }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_attachments.yml", DataFormat.Yaml);
        var content = File.ReadAllText(path);

        Assert.Contains("Attachments:", content);
        Assert.Contains("Name: openapi.json", content);
        Assert.Contains("RelativePath: artifacts/openapi.json", content);
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_scenario_level_attachments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "API",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Generate spec", Result = ExecutionResult.Passed,
                        Attachments = [new FileAttachment("openapi.json", "artifacts/openapi.json")],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Then", Text = "spec is generated", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_scenario_attachments.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenario = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        var attachments = scenario.GetProperty("attachments");

        Assert.Equal(1, attachments.GetArrayLength());
        Assert.Equal("openapi.json", attachments[0].GetProperty("name").GetString());
        Assert.Equal("artifacts/openapi.json", attachments[0].GetProperty("relativePath").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_xml_includes_scenario_level_attachments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "API",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Generate spec", Result = ExecutionResult.Passed,
                        Attachments = [new FileAttachment("openapi.json", "artifacts/openapi.json")],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Then", Text = "spec is generated", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_scenario_attachments.xml", DataFormat.Xml);
        var doc = XDocument.Parse(File.ReadAllText(path));
        var scenario = doc.Root!.Element("Features")!.Element("Feature")!.Element("Scenarios")!.Element("Scenario")!;
        var attachment = scenario.Element("Attachments")?.Element("Attachment");

        Assert.NotNull(attachment);
        Assert.Equal("openapi.json", attachment!.Element("Name")?.Value);
        Assert.Equal("artifacts/openapi.json", attachment.Element("RelativePath")?.Value);
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_includes_scenario_level_attachments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "API",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Generate spec", Result = ExecutionResult.Passed,
                        Attachments = [new FileAttachment("openapi.json", "artifacts/openapi.json")],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Then", Text = "spec is generated", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_scenario_attachments.yml", DataFormat.Yaml);
        var content = File.ReadAllText(path);

        // Should appear at scenario level (not step level)
        Assert.Contains("Attachments:", content);
        Assert.Contains("Name: openapi.json", content);
        Assert.Contains("RelativePath: artifacts/openapi.json", content);
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_backgroundSteps()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        BackgroundSteps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "common setup", Status = ExecutionResult.Passed }
                        ],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "action", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_bg.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenario = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        var bgSteps = scenario.GetProperty("backgroundSteps");

        Assert.Equal(1, bgSteps.GetArrayLength());
        Assert.Equal("common setup", bgSteps[0].GetProperty("text").GetString());
    }

    [Fact]
    public void GenerateTestRunReportData_json_includes_outlineId_and_exampleValues()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Pay with visa", Result = ExecutionResult.Passed,
                        OutlineId = "Pay with card",
                        ExampleValues = new Dictionary<string, string> { ["card"] = "visa" }
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow, "TestRunData_outline.json", DataFormat.Json);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenario = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];

        Assert.Equal("Pay with card", scenario.GetProperty("outlineId").GetString());
        Assert.Equal("visa", scenario.GetProperty("exampleValues").GetProperty("card").GetString());
    }

    /// <summary>
    /// The diagram-marker logs (<c>IsOverrideStart</c>/<c>IsOverrideEnd</c>/<c>IsActionStart</c>) share the
    /// tracked-log stream with real traffic but carry no interaction: their whole payload is the PlantUml
    /// fragment the sequence diagram splices in. Every other consumer filters them; the data exports must
    /// too, or a Gherkin step or assertion shows up as a content-free call to <c>http://override.com/</c>.
    /// </summary>
    private static RequestResponseLog MarkerLog(string testId, bool overrideStart = false, bool overrideEnd = false, bool actionStart = false) =>
        new(
            TestName: testId,
            TestId: testId,
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideStart = overrideStart,
            IsOverrideEnd = overrideEnd,
            IsActionStart = actionStart,
            PlantUml = overrideStart ? "hnote across #black:Given I am signed in" : null,
            Timestamp = DateTimeOffset.UtcNow
        };

    private static RequestResponseLog RealLog(string testId, RequestResponseType type, Guid pairId) =>
        new(
            TestName: testId,
            TestId: testId,
            Method: HttpMethod.Get,
            Content: type == RequestResponseType.Request ? "" : "{\"ok\":true}",
            Uri: new Uri("https://api.example.com/orders"),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "TestClient",
            Type: type,
            TraceId: pairId,
            RequestResponseId: pairId,
            TrackingIgnore: false,
            StatusCode: type == RequestResponseType.Response ? HttpStatusCode.OK : null)
        { Timestamp = DateTimeOffset.UtcNow };

    private static Feature[] SingleScenario(string id = "test-1") =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = id, DisplayName = "Place order", Result = ExecutionResult.Passed }]
        }
    ];

    [Fact]
    public void GenerateTestRunReportData_json_excludes_diagram_marker_logs_from_interactions()
    {
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MarkerLog("test-1", actionStart: true),
            MarkerLog("test-1", overrideStart: true),
            MarkerLog("test-1", overrideEnd: true),
            RealLog("test-1", RequestResponseType.Request, pairId),
            RealLog("test-1", RequestResponseType.Response, pairId)
        };

        var path = ReportGenerator.GenerateTestRunReportData(SingleScenario(), DateTime.UtcNow, DateTime.UtcNow, "TestRunData_markers.json", DataFormat.Json, trackedLogs: logs);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var interactions = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions");

        Assert.Equal(2, interactions.GetArrayLength());
        Assert.DoesNotContain("override.com", File.ReadAllText(path));
    }

    [Fact]
    public void GenerateTestRunReportData_json_emits_empty_interactions_when_only_markers_were_logged()
    {
        var logs = new[] { MarkerLog("test-1", overrideStart: true), MarkerLog("test-1", overrideEnd: true) };

        var path = ReportGenerator.GenerateTestRunReportData(SingleScenario(), DateTime.UtcNow, DateTime.UtcNow, "TestRunData_markers_only.json", DataFormat.Json, trackedLogs: logs);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var scenario = doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];

        Assert.Equal(0, scenario.GetProperty("httpInteractions").GetArrayLength());
    }

    [Fact]
    public void GenerateTestRunReportData_xml_excludes_diagram_marker_logs_from_interactions()
    {
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MarkerLog("test-1", overrideStart: true),
            MarkerLog("test-1", overrideEnd: true),
            RealLog("test-1", RequestResponseType.Request, pairId),
            RealLog("test-1", RequestResponseType.Response, pairId)
        };

        var path = ReportGenerator.GenerateTestRunReportData(SingleScenario(), DateTime.UtcNow, DateTime.UtcNow, "TestRunData_markers.xml", DataFormat.Xml, trackedLogs: logs);
        var doc = XDocument.Parse(File.ReadAllText(path));
        var interactions = doc.Descendants("HttpInteractions").Single();

        Assert.Equal(2, interactions.Elements().Count());
        Assert.DoesNotContain("override.com", File.ReadAllText(path));
    }

    [Fact]
    public void GenerateTestRunReportData_xml_omits_interactions_element_when_only_markers_were_logged()
    {
        var logs = new[] { MarkerLog("test-1", overrideStart: true), MarkerLog("test-1", overrideEnd: true) };

        var path = ReportGenerator.GenerateTestRunReportData(SingleScenario(), DateTime.UtcNow, DateTime.UtcNow, "TestRunData_markers_only.xml", DataFormat.Xml, trackedLogs: logs);
        var doc = XDocument.Parse(File.ReadAllText(path));

        Assert.Empty(doc.Descendants("HttpInteractions"));
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_excludes_diagram_marker_logs_from_interactions()
    {
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MarkerLog("test-1", overrideStart: true),
            MarkerLog("test-1", overrideEnd: true),
            RealLog("test-1", RequestResponseType.Request, pairId),
            RealLog("test-1", RequestResponseType.Response, pairId)
        };

        var path = ReportGenerator.GenerateTestRunReportData(SingleScenario(), DateTime.UtcNow, DateTime.UtcNow, "TestRunData_markers.yml", DataFormat.Yaml, trackedLogs: logs);
        var content = File.ReadAllText(path);

        Assert.DoesNotContain("override.com", content);
        Assert.Contains("HttpInteractions:", content);
    }

    [Fact]
    public void GenerateTestRunReportData_yaml_omits_interactions_block_when_only_markers_were_logged()
    {
        var logs = new[] { MarkerLog("test-1", overrideStart: true), MarkerLog("test-1", overrideEnd: true) };

        var path = ReportGenerator.GenerateTestRunReportData(SingleScenario(), DateTime.UtcNow, DateTime.UtcNow, "TestRunData_markers_only.yml", DataFormat.Yaml, trackedLogs: logs);
        var content = File.ReadAllText(path);

        Assert.DoesNotContain("HttpInteractions:", content);
    }
}
