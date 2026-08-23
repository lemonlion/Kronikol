using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The data files have to carry everything the diagram knows. These cover the fields the JSON/XML/YAML
/// writers used to drop on the floor: the interaction metadata (phase, dependency category, OTel ids,
/// capture path, derived duration) and the full step detail (parameters, doc strings, comments).
/// </summary>
public class ReportEnrichmentTests
{
    // ─── §2.1 Interaction fields ───────────────────────────────

    [Fact]
    public void Json_interaction_carries_the_metadata_the_diagram_uses()
    {
        var pair = Guid.NewGuid();
        var logs = new[]
        {
            new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://svc/api"), [],
                "cache", "api", RequestResponseType.Request, Guid.NewGuid(), pair, false,
                null, RequestResponseMetaType.Event, "redis", "service")
            {
                Phase = TestPhase.Action,
                ActivityTraceId = "0af7651916cd43dd8448eb211c80319c",
                ActivitySpanId = "b7ad6b7169203331",
                CapturedBy = "wire",
                IsUserAction = false,
                Timestamp = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)
            },
            new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://svc/api"), [],
                "cache", "api", RequestResponseType.Response, Guid.NewGuid(), pair, false,
                HttpStatusCode.OK)
            {
                Timestamp = new DateTimeOffset(2026, 1, 1, 10, 0, 0, 250, TimeSpan.Zero)
            }
        };

        var interaction = FirstInteractionJson(logs, "Enrich_fields.json");

        Assert.Equal("Event", interaction.GetProperty("metaType").GetString());
        Assert.Equal("redis", interaction.GetProperty("dependencyCategory").GetString());
        Assert.Equal("service", interaction.GetProperty("callerDependencyCategory").GetString());
        Assert.Equal("Action", interaction.GetProperty("phase").GetString());
        Assert.False(interaction.GetProperty("isUserAction").GetBoolean());
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", interaction.GetProperty("activityTraceId").GetString());
        Assert.Equal("b7ad6b7169203331", interaction.GetProperty("activitySpanId").GetString());
        Assert.Equal("wire", interaction.GetProperty("capturedBy").GetString());
    }

    [Fact]
    public void Json_interaction_duration_is_derived_from_the_request_response_pair()
    {
        var pair = Guid.NewGuid();
        var logs = new[]
        {
            Paired(RequestResponseType.Request, pair, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            Paired(RequestResponseType.Response, pair, new DateTimeOffset(2026, 1, 1, 10, 0, 0, 250, TimeSpan.Zero))
        };

        var interaction = FirstInteractionJson(logs, "Enrich_duration.json");

        Assert.Equal(250.0, interaction.GetProperty("durationMs").GetDouble(), 3);
    }

    [Fact]
    public void Json_interaction_duration_is_null_when_the_request_never_got_a_response()
    {
        var logs = new[] { Paired(RequestResponseType.Request, Guid.NewGuid(), DateTimeOffset.UtcNow) };

        var interaction = FirstInteractionJson(logs, "Enrich_duration_unpaired.json");

        Assert.Equal(JsonValueKind.Null, interaction.GetProperty("durationMs").ValueKind);
    }

    [Fact]
    public void Json_response_repeats_the_duration_so_either_half_answers_how_long()
    {
        var pair = Guid.NewGuid();
        var logs = new[]
        {
            Paired(RequestResponseType.Request, pair, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            Paired(RequestResponseType.Response, pair, new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero))
        };

        var interactions = InteractionsJson(logs, "Enrich_duration_both.json");

        Assert.Equal(1000.0, interactions[0].GetProperty("durationMs").GetDouble(), 3);
        Assert.Equal(1000.0, interactions[1].GetProperty("durationMs").GetDouble(), 3);
    }

    [Fact]
    public void A_capturer_that_measured_the_call_itself_is_believed_over_the_timestamps()
    {
        // The NDJSON ingest contract lets a capturer send one record for a whole call, with durationMs
        // instead of a second timestamp. Before this the field reached the flow calculations and then
        // vanished, so the round trip through a report lost it.
        var logs = new[]
        {
            new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://svc/api"), [], "svc", "api",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
            { Timestamp = DateTimeOffset.UtcNow, DurationMs = 42.5 }
        };

        var interaction = FirstInteractionJson(logs, "Enrich_duration_measured.json");

        Assert.Equal(42.5, interaction.GetProperty("durationMs").GetDouble(), 3);
    }

    [Fact]
    public void Xml_interaction_carries_the_same_metadata()
    {
        var logs = new[]
        {
            new RequestResponseLog("Test", "t1", HttpMethod.Post, null, new Uri("http://svc/api"), [],
                "db", "api", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false,
                null, RequestResponseMetaType.Default, "sql", null)
            {
                Phase = TestPhase.Setup,
                ActivityTraceId = "trace-1",
                ActivitySpanId = "span-1",
                CapturedBy = "span",
                IsUserAction = true
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Enrich_fields.xml", DataFormat.Xml, trackedLogs: logs);
        var interaction = XDocument.Load(path).Descendants("HttpInteraction").Single();

        Assert.Equal("sql", interaction.Element("DependencyCategory")?.Value);
        Assert.Equal("Setup", interaction.Element("Phase")?.Value);
        Assert.Equal("trace-1", interaction.Element("ActivityTraceId")?.Value);
        Assert.Equal("span-1", interaction.Element("ActivitySpanId")?.Value);
        Assert.Equal("span", interaction.Element("CapturedBy")?.Value);
        Assert.Equal("true", interaction.Element("IsUserAction")?.Value);
    }

    [Fact]
    public void Xml_omits_the_metadata_elements_that_carry_nothing()
    {
        var logs = new[] { Paired(RequestResponseType.Request, Guid.NewGuid(), null) };

        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Enrich_fields_sparse.xml", DataFormat.Xml, trackedLogs: logs);
        var interaction = XDocument.Load(path).Descendants("HttpInteraction").Single();

        Assert.Null(interaction.Element("DependencyCategory"));
        Assert.Null(interaction.Element("ActivityTraceId"));
        Assert.Null(interaction.Element("CapturedBy"));
        Assert.Null(interaction.Element("IsUserAction"));
        Assert.Null(interaction.Element("MetaType"));
    }

    [Fact]
    public void Yaml_interaction_carries_the_same_metadata()
    {
        var logs = new[]
        {
            new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://svc/api"), [],
                "queue", "api", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false,
                null, RequestResponseMetaType.Event, "messaging", null)
            {
                Phase = TestPhase.Action,
                ActivityTraceId = "trace-2",
                CapturedBy = "wire"
            }
        };

        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Enrich_fields.yml", DataFormat.Yaml, trackedLogs: logs);
        var yaml = File.ReadAllText(path);

        Assert.Contains("MetaType: Event", yaml);
        Assert.Contains("DependencyCategory: messaging", yaml);
        Assert.Contains("Phase: Action", yaml);
        Assert.Contains("ActivityTraceId: trace-2", yaml);
        Assert.Contains("CapturedBy: wire", yaml);
    }

    [Fact]
    public void Json_schema_describes_the_new_interaction_fields()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("Enrich_schema.json", DataFormat.Json);
        var props = JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("$defs").GetProperty("httpInteraction").GetProperty("properties");

        foreach (var field in new[] { "metaType", "dependencyCategory", "callerDependencyCategory", "phase",
                     "isUserAction", "activityTraceId", "activitySpanId", "capturedBy", "durationMs" })
            Assert.True(props.TryGetProperty(field, out _), $"schema is missing httpInteraction.{field}");
    }

    [Fact]
    public void Xml_schema_describes_the_new_interaction_fields()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("Enrich_schema.xsd", DataFormat.Xml);
        XNamespace xs = "http://www.w3.org/2001/XMLSchema";
        var type = XDocument.Load(path).Root!.Elements(xs + "complexType")
            .Single(e => (string?)e.Attribute("name") == "HttpInteractionType");
        var names = type.Descendants(xs + "element").Select(e => (string?)e.Attribute("name")).ToArray();

        foreach (var field in new[] { "MetaType", "DependencyCategory", "CallerDependencyCategory", "Phase",
                     "IsUserAction", "ActivityTraceId", "ActivitySpanId", "CapturedBy", "DurationMs" })
            Assert.Contains(field, names);
    }

    // ─── §2.3 Step detail ──────────────────────────────────────

    [Fact]
    public void Standard_json_carries_step_parameters()
    {
        var path = ReportGenerator.GenerateTestRunReportData(FeaturesWithRichStep(), Start, End, "Enrich_params.json", DataFormat.Json);
        var step = FirstStep(path);

        var parameters = step.GetProperty("parameters");
        Assert.Equal(1, parameters.GetArrayLength());
        Assert.Equal("recipe", parameters[0].GetProperty("name").GetString());
        Assert.Equal("Tabular", parameters[0].GetProperty("kind").GetString());
        Assert.Equal("flour", parameters[0].GetProperty("tabularValue").GetProperty("columns")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Standard_json_carries_doc_string_comments_and_bypass_reason()
    {
        var path = ReportGenerator.GenerateTestRunReportData(FeaturesWithRichStep(), Start, End, "Enrich_stepdetail.json", DataFormat.Json);
        var step = FirstStep(path);

        Assert.Equal("{ \"a\": 1 }", step.GetProperty("docString").GetString());
        Assert.Equal("application/json", step.GetProperty("docStringMediaType").GetString());
        Assert.Equal("no oven", step.GetProperty("bypassReason").GetString());
        Assert.Equal("mind the gap", step.GetProperty("comments")[0].GetString());
    }

    [Fact]
    public void Full_step_detail_can_be_turned_off()
    {
        var path = ReportGenerator.GenerateTestRunReportData(FeaturesWithRichStep(), Start, End, "Enrich_stepdetail_off.json",
            DataFormat.Json, fullStepDetail: false);
        var step = FirstStep(path);

        Assert.False(step.TryGetProperty("parameters", out _));
        Assert.False(step.TryGetProperty("docString", out _));
    }

    [Fact]
    public void Json_schema_describes_the_new_step_fields()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("Enrich_schema_steps.json", DataFormat.Json);
        var props = JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("$defs").GetProperty("step").GetProperty("properties");

        foreach (var field in new[] { "bypassReason", "docString", "docStringMediaType", "comments", "parameters", "textSegments" })
            Assert.True(props.TryGetProperty(field, out _), $"schema is missing step.{field}");
    }

    // ─── Helpers ───────────────────────────────────────────────

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static RequestResponseLog Paired(RequestResponseType type, Guid pair, DateTimeOffset? timestamp) =>
        new("Test", "t1", HttpMethod.Get, null, new Uri("http://svc/api"), [], "svc", "api",
            type, Guid.NewGuid(), pair, false)
        { Timestamp = timestamp };

    private static Feature[] Features() =>
    [
        new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "t1", DisplayName = "S" }] }
    ];

    private static Feature[] FeaturesWithRichStep() =>
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
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Given",
                            Text = "a recipe",
                            Status = ExecutionResult.Passed,
                            BypassReason = "no oven",
                            DocString = "{ \"a\": 1 }",
                            DocStringMediaType = "application/json",
                            Comments = ["mind the gap"],
                            Parameters =
                            [
                                new StepParameter
                                {
                                    Name = "recipe",
                                    Kind = StepParameterKind.Tabular,
                                    TabularValue = new TabularParameterValue(
                                        [new TabularColumn("flour", true)],
                                        [new TabularRow(TableRowType.Matching, [new TabularCell("200g", null, VerificationStatus.NotApplicable)])],
                                        false)
                                }
                            ]
                        }
                    ]
                }
            ]
        }
    ];

    private static JsonElement FirstStep(string path) =>
        JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("steps")[0];

    private static JsonElement[] InteractionsJson(RequestResponseLog[] logs, string fileName)
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, fileName, DataFormat.Json, trackedLogs: logs);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions")
            .EnumerateArray().ToArray();
    }

    private static JsonElement FirstInteractionJson(RequestResponseLog[] logs, string fileName) =>
        InteractionsJson(logs, fileName)[0];
}
