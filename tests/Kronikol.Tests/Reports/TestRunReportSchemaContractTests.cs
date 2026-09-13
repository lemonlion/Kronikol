using System.Net;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>TestRunReport.schema.json</c> is the field-level contract of <c>TestRunReport.json</c>: a reader — or
/// an agent that reads the schema instead of the 10 MB file — must never meet a key the contract does not
/// mention. The walker below generates a report that exercises every emitter and checks each emitted key
/// against the schema, so every field added later has to be declared (and described) to get in.
/// </summary>
public class TestRunReportSchemaContractTests
{
    internal static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);
    internal const string TestId = "schema-t1";

    [Fact]
    public void Every_key_the_json_writer_emits_is_declared_in_the_schema()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportPath = ReportGenerator.GenerateTestRunReportData(RichFeatures(), Start, End,
            $"SchemaContract_{suffix}.json", DataFormat.Json, Diagrams(), Logs(), Diagnostics());
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"SchemaContract_{suffix}.schema.json", DataFormat.Json);

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var undeclared = new SortedSet<string>(StringComparer.Ordinal);
        Walk(report.RootElement, schema.RootElement, schema.RootElement, "$", undeclared);

        Assert.True(undeclared.Count == 0, "Emitted by the writer but not declared in the schema:\n  " + string.Join("\n  ", undeclared));
    }

    [Fact]
    public void Every_schema_property_has_a_description()
    {
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"SchemaDescribed_{Guid.NewGuid():N}.schema.json", DataFormat.Json);
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var undescribed = new SortedSet<string>(StringComparer.Ordinal);
        CollectUndescribed(schema.RootElement, "$", undescribed);

        Assert.True(undescribed.Count == 0, "Schema properties without a description:\n  " + string.Join("\n  ", undescribed));
    }

    [Fact]
    public void Schema_names_the_query_tool_and_the_size_trap()
    {
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"SchemaComment_{Guid.NewGuid():N}.schema.json", DataFormat.Json);
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var comment = schema.RootElement.GetProperty("$comment").GetString();
        Assert.Contains("kronikol query", comment);
        Assert.Contains("megabytes", comment);
    }

    [Fact]
    public void Address_bearing_fields_carry_examples()
    {
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"SchemaExamples_{Guid.NewGuid():N}.schema.json", DataFormat.Json);
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = schema.RootElement;

        var scenario = root.GetProperty("properties").GetProperty("features").GetProperty("items")
            .GetProperty("properties").GetProperty("scenarios").GetProperty("items").GetProperty("properties");
        var interaction = root.GetProperty("$defs").GetProperty("httpInteraction").GetProperty("properties");

        Assert.True(scenario.GetProperty("stableId").TryGetProperty("examples", out _), "stableId has no examples");
        Assert.True(interaction.GetProperty("stepPath").TryGetProperty("examples", out _), "stepPath has no examples");
        Assert.True(interaction.GetProperty("activityTraceId").TryGetProperty("examples", out _), "activityTraceId has no examples");
    }

    // ─── The walker ─────────────────────────────────────────────

    private static void Walk(JsonElement value, JsonElement node, JsonElement root, string path, SortedSet<string> undeclared)
    {
        node = Resolve(node, root);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var hasProperties = node.TryGetProperty("properties", out var properties);
                var hasAdditional = node.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object;
                if (!hasProperties && !hasAdditional)
                    return; // an open object (a step parameter, a text segment) declared as {type: object}
                foreach (var child in value.EnumerateObject())
                {
                    if (hasProperties && properties.TryGetProperty(child.Name, out var childSchema))
                        Walk(child.Value, childSchema, root, $"{path}.{child.Name}", undeclared);
                    else if (hasAdditional)
                        Walk(child.Value, additional, root, $"{path}.{child.Name}", undeclared);
                    else
                        undeclared.Add($"{path}.{child.Name}");
                }
                break;

            case JsonValueKind.Array:
                if (!node.TryGetProperty("items", out var items))
                    return;
                foreach (var element in value.EnumerateArray())
                    Walk(element, items, root, $"{path}[]", undeclared);
                break;
        }
    }

    private static JsonElement Resolve(JsonElement node, JsonElement root)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("$ref", out var reference)
            || reference.GetString() is not { } r || !r.StartsWith("#/", StringComparison.Ordinal))
            return node;

        var current = root;
        foreach (var segment in r[2..].Split('/'))
            current = current.GetProperty(segment);
        return current;
    }

    private static void CollectUndescribed(JsonElement node, string path, SortedSet<string> undescribed)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (node.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                var childPath = $"{path}.{property.Name}";
                if (!property.Value.TryGetProperty("$ref", out _) && !property.Value.TryGetProperty("description", out _))
                    undescribed.Add(childPath);
                CollectUndescribed(property.Value, childPath, undescribed);
            }
        }

        if (node.TryGetProperty("items", out var items))
            CollectUndescribed(items, path + "[]", undescribed);

        if (node.TryGetProperty("$defs", out var defs))
            foreach (var def in defs.EnumerateObject())
                CollectUndescribed(def.Value, $"$defs.{def.Name}", undescribed);
    }

    // ─── A fixture that touches every emitter ───────────────────

    internal static Feature[] RichFeatures() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Endpoint = "/api/orders",
            Description = "Everything about placing an order.",
            Labels = ["orders", "smoke"],
            Scenarios =
            [
                new Scenario
                {
                    Id = TestId,
                    DisplayName = "Place an order for <item>",
                    Description = "The happy path.",
                    IsHappyPath = true,
                    Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(2.5),
                    ErrorMessage = "Expected: 4173\nActual: 3902",
                    ErrorStackTrace = "   at OrderTests.Place() in OrderTests.cs:line 42",
                    Labels = ["checkout"],
                    Categories = ["API"],
                    Rule = "Totals are exact",
                    OutlineId = "Place an order for <item>",
                    ExamplesBlockName = "Standard items",
                    ExamplesBlockDescription = "Items every shop stocks",
                    ExamplesBlockIndex = 0,
                    ExampleValues = new Dictionary<string, string> { ["item"] = "Widget" },
                    ExampleFlatValues = new Dictionary<string, string> { ["item"] = "Widget" },
                    ExampleDisplayName = "Place an order for Widget",
                    Attachments = [new FileAttachment("receipt.png", "attachments/receipt.png", "image/png")],
                    BackgroundSteps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "the shop is open", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(5) }
                    ],
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "When",
                            Text = "I order a Widget",
                            Status = ExecutionResult.Passed,
                            Duration = TimeSpan.FromMilliseconds(120),
                            DocString = "{ \"item\": \"Widget\" }",
                            DocStringMediaType = "application/json",
                            Comments = ["# the order body"],
                            Attachments = [new FileAttachment("request.json", "attachments/request.json", "application/json")],
                            TextSegments =
                            [
                                StepTextSegment.Literal("I order a "),
                                StepTextSegment.Param("item", new InlineParameterValue("Widget", null, VerificationStatus.NotApplicable)),
                                StepTextSegment.TableRef("lines", "2 rows")
                            ],
                            Parameters =
                            [
                                new StepParameter { Name = "item", Kind = StepParameterKind.Inline, InlineValue = new InlineParameterValue("Widget", "Widget", VerificationStatus.Success) },
                                new StepParameter
                                {
                                    Name = "lines",
                                    Kind = StepParameterKind.Tabular,
                                    TabularValue = new TabularParameterValue(
                                        [new TabularColumn("sku", true), new TabularColumn("qty", false)],
                                        [new TabularRow(TableRowType.Matching, [new TabularCell("W-1", "W-1", VerificationStatus.Success), new TabularCell("2", null, VerificationStatus.NotApplicable)])],
                                        IsLinkedOutput: true)
                                },
                                new StepParameter
                                {
                                    Name = "receipt",
                                    Kind = StepParameterKind.Tree,
                                    TreeValue = new TreeParameterValue(new TreeNode("$", "receipt", "", null, VerificationStatus.NotApplicable,
                                        [new TreeNode("$.total", "total", "4173", "4173", VerificationStatus.Success, null)]))
                                }
                            ]
                        },
                        new ScenarioStep
                        {
                            Keyword = "Then",
                            Text = "the total is right",
                            Status = ExecutionResult.Failed,
                            Duration = TimeSpan.FromMilliseconds(3),
                            FailureMessage = "Expected 4173 but found 3902",
                            SourceFile = "OrderTests.cs",
                            SourceLine = 42,
                            SubSteps =
                            [
                                new ScenarioStep { Text = "total == 4173", Status = ExecutionResult.Failed, FailureMessage = "Expected 4173 but found 3902", SourceFile = "OrderTests.cs", SourceLine = 42 }
                            ]
                        },
                        new ScenarioStep { Keyword = "And", Text = "an email is sent", Status = ExecutionResult.Bypassed, BypassReason = "no SMTP in this environment" }
                    ]
                }
            ]
        }
    ];

    internal static DiagramAsCode[] Diagrams() => [new DiagramAsCode(TestId, "", "@startuml\nA -> B\n@enduml")];

    internal static IReadOnlyList<DiagnosticEntry> Diagnostics() =>
    [
        new DiagnosticEntry(DiagnosticKind.RenderFailure, "Building the diagram failed: TimeoutException: no answer", TestId),
        new DiagnosticEntry(DiagnosticKind.Other, "host says hello")
    ];

    internal static RequestResponseLog[] Logs()
    {
        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero);
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(TestId, TestId, "", "", new Uri("http://override.com"), [], "", "",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
            { IsOverrideStart = true, PlantUml = "note over A: row 1", MarkerKind = DiagramMarkerKind.Row },
            new RequestResponseLog(TestId, TestId, HttpMethod.Post, "{\"item\":\"Widget\"}", new Uri("http://orders/api/orders"),
                [("accept", "application/json"), ("x-empty", null)], "orders", "test", RequestResponseType.Request, traceId, pairId, false,
                MetaType: RequestResponseMetaType.Default, DependencyCategory: "http", CallerDependencyCategory: "test")
            { Timestamp = at, ActivityTraceId = "4bf92f3577b34da6a3ce929d0e0e4736", ActivitySpanId = "00f067aa0ba902b7", CapturedBy = "wire", Phase = TestPhase.Action },
            new RequestResponseLog(TestId, TestId, HttpMethod.Post, "{\"total\":3902}", new Uri("http://orders/api/orders"), [],
                "orders", "test", RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.Created)
            { Timestamp = at.AddMilliseconds(35) },
            new RequestResponseLog(TestId, TestId, "", "", new Uri("http://override.com"), [], "", "",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
            { IsOverrideStart = true, PlantUml = "note over A: custom", MarkerKind = DiagramMarkerKind.Custom }
        ];
    }
}
