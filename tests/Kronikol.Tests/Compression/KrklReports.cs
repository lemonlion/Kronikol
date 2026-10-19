using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Compression;

/// <summary>
/// Reports for the compressed file's facts: one run written as JSON by the real writer, and the same JSON in the forms the
/// reader takes (plans/REPORT_FORMAT_PLAN.md section 5, R3). Until the writer of the compressed file exists (R4), its
/// facts make it here, with the copy, exactly as section 4.1 lays it out.
/// </summary>
internal static class KrklReports
{
    private const string RequestBody = "{\"item\":\"widget\",\"qty\":2,\"note\":\"leave it at the door\"}";
    private const string ResponseBody = "{\"id\":\"ord-1\",\"status\":\"placed\",\"lines\":[{\"item\":\"widget\",\"qty\":2}]}";
    private const string StockBody = "{\"sku\":\"W-1\",\"inStock\":true,\"count\":12}";
    private const string Diagram = "@startuml\nparticipant Test\nparticipant Orders\nTest -> Orders: POST /api/orders\nnote left\n" + RequestBody + "\nend note\nOrders --> Test: 201 Created\n@enduml";

    /// <summary>Writes the run as <c>TestRunReport.json</c> in <paramref name="directory"/>, and returns its path.</summary>
    public static string WriteJson(string directory, bool compressPayloads = false)
    {
        Directory.CreateDirectory(directory);
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var at = new DateTimeOffset(start).AddSeconds(1);

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "place", DisplayName = "Place an order", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "I place an order", Status = ExecutionResult.Passed,
                                SubSteps = [new ScenarioStep { Keyword = "And", Text = "I choose a widget", Status = ExecutionResult.Passed }]
                            },
                            new ScenarioStep { Keyword = "Then", Text = "the order is placed", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "save", DisplayName = "Save the order", Result = ExecutionResult.Failed, ErrorMessage = "Expected 1 row but found 0",
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the order is saved", Status = ExecutionResult.Failed, FailureMessage = "Expected 1 row but found 0" }]
                    },
                    new Scenario
                    {
                        Id = "stock", DisplayName = "Check stock", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a widget", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        ];

        RequestResponseLog[] logs =
        [
            new("Place an order", "place", HttpMethod.Post, RequestBody, new Uri("http://orders/api/orders"), [("accept", "application/json"), ("x-correlation-id", "c-1")],
                "Orders", "Test", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c01"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c02"), false)
            { Timestamp = at },
            new("Place an order", "place", HttpMethod.Post, ResponseBody, new Uri("http://orders/api/orders"), [("content-type", "application/json")],
                "Orders", "Test", RequestResponseType.Response, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c01"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c02"), false, HttpStatusCode.Created)
            { Timestamp = at.AddMilliseconds(40) },
            new("Save the order", "save", "Execute", "INSERT INTO orders (id) VALUES (@id)", new Uri("sql://orders-db/orders"), [],
                "Orders DB", "Orders", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c03"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c04"), false)
            { Timestamp = at.AddSeconds(1) },
            new("Save the order", "save", "Execute", "0 rows", new Uri("sql://orders-db/orders"), [],
                "Orders DB", "Orders", RequestResponseType.Response, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c03"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c04"), false)
            { Timestamp = at.AddSeconds(1).AddMilliseconds(5) },
            new("Check stock", "stock", HttpMethod.Get, null, new Uri("http://stock/api/stock/W-1"), [],
                "Stock", "Test", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c05"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c06"), false)
            { Timestamp = at.AddSeconds(2) },
            new("Check stock", "stock", HttpMethod.Get, StockBody, new Uri("http://stock/api/stock/W-1"), [],
                "Stock", "Test", RequestResponseType.Response, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c05"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c06"), false, HttpStatusCode.OK)
            { Timestamp = at.AddSeconds(2).AddMilliseconds(5) },
            new("Place an order", "place", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request,
                new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c0b"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c0c"), false)
            { Timestamp = at.AddMilliseconds(50), IsOverrideStart = true, PlantUml = "note over Orders : Order placed", MarkerKind = DiagramMarkerKind.Custom }
        ];

        return ReportGenerator.GenerateTestRunReportData(features, start, start.AddMinutes(1),
            Path.Combine(directory, "TestRunReport.json"), DataFormat.Json,
            [new DiagramAsCode("place", "", Diagram)], logs,
            diagnostics: null, fullStepDetail: true, ciMetadata: null, suite: "krkl", environment: RunEnvironment.Unrecorded,
            attribution: null, compressPayloads: compressPayloads);
    }

    /// <summary>Writes <paramref name="json"/>'s bytes as <c>TestRunReport.krkl</c> in <paramref name="directory"/>, and returns its path.</summary>
    public static string WriteKrkl(string json, string directory, string name = "TestRunReport.krkl")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, ReportFileFormatTests.Krkl(File.ReadAllBytes(json)));
        return path;
    }

    /// <summary>
    /// The JSON as <c>formatVersion</c> 3 writes it (4.2): every optional key holding null, an empty array or an empty
    /// object left out, and the version given. <paramref name="kronikolVersion"/> replaces the writer's own when given.
    /// </summary>
    public static byte[] AsVersion3(string json, int formatVersion = 3, string? kronikolVersion = null, params string[] withoutKeys)
    {
        var root = JsonNode.Parse(File.ReadAllText(json))!.AsObject();
        Strip(root, withoutKeys);
        Prune(root);
        root["formatVersion"] = formatVersion;
        if (kronikolVersion is not null)
            root["kronikolVersion"] = kronikolVersion;
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static readonly HashSet<string> Required = ["features", "formatVersion"];

    /// <summary>Removes every key named in <paramref name="keys"/>, at any depth.</summary>
    private static void Strip(JsonNode? node, string[] keys)
    {
        if (keys.Length == 0)
            return;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (keys.Contains(key, StringComparer.Ordinal))
                        obj.Remove(key);
                    else
                        Strip(value, keys);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    Strip(item, keys);
                break;
        }
    }

    private static void Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    Prune(value);
                    if (!Required.Contains(key) && value is null or JsonArray { Count: 0 } or JsonObject { Count: 0 })
                        obj.Remove(key);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    Prune(item);
                break;
        }
    }
}
