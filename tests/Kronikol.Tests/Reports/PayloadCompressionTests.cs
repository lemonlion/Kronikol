using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>ReportConfigurationOptions.CompressTestRunReportPayloads</c> (#85, <c>plans/PAYLOAD_COMPRESSION_PLAN.md</c>): a
/// captured body or a diagram's PlantUML source of 512 characters or more is written as
/// <c>{"$h": address, "$n": length, "$z": base64 of gzip}</c> in place of its string, unless that would not make it
/// smaller, and a file holding one declares <c>formatVersion</c> 2. These facts hold the writer to that rule; the
/// readers' facts are in <c>CompressedReportReadingTests</c>.
/// </summary>
public class PayloadCompressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kronikol-payloads-" + Guid.NewGuid().ToString("N"));

    public PayloadCompressionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void The_option_is_off_by_default() => Assert.False(new ReportConfigurationOptions().CompressTestRunReportPayloads);

    [Fact]
    public void A_payload_of_512_characters_is_written_compressed_and_one_of_511_is_not()
    {
        using var report = Write(compress: true, Json(511), Json(512));

        var (short_, long_) = Contents(report.RootElement);
        Assert.Equal(JsonValueKind.String, short_.ValueKind);
        Assert.Equal(JsonValueKind.Object, long_.ValueKind);
        Assert.Equal(2, report.RootElement.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void Nothing_is_compressed_with_the_option_off()
    {
        using var report = Write(compress: false, Json(4000), Json(512));

        var (request, response) = Contents(report.RootElement);
        Assert.Equal(JsonValueKind.String, request.ValueKind);
        Assert.Equal(JsonValueKind.String, response.ValueKind);
        Assert.Equal(1, report.RootElement.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void A_payload_compression_would_not_shrink_stays_a_string_and_the_file_stays_version_1()
    {
        var bytes = new byte[900];
        new Random(7).NextBytes(bytes);
        var noise = Convert.ToBase64String(bytes);

        using var report = Write(compress: true, noise, Json(100));

        var (request, _) = Contents(report.RootElement);
        Assert.Equal(JsonValueKind.String, request.ValueKind);
        Assert.Equal(noise, request.GetString());
        Assert.Equal(1, report.RootElement.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void The_wrapper_carries_the_address_and_the_length_of_the_text_and_inflates_to_it()
    {
        var text = Json(3000) + " Zoë ✓";

        using var report = Write(compress: true, text, Json(20));

        var (wrapper, _) = Contents(report.RootElement);
        Assert.Equal(["$h", "$n", "$z"], wrapper.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(Address(text), wrapper.GetProperty("$h").GetString());
        Assert.Equal(text.Length, wrapper.GetProperty("$n").GetInt32());
        Assert.Equal(text, Inflate(wrapper.GetProperty("$z").GetString()!));
    }

    [Fact]
    public void A_diagram_is_compressed_by_the_same_rule()
    {
        var big = "@startuml\n" + string.Concat(Enumerable.Range(0, 80).Select(i => $"A -> B : call {i}\n")) + "@enduml";

        using var report = Write(compress: true, Json(20), Json(20), diagrams: [big, "@startuml\nA -> B\n@enduml"]);

        var diagrams = Scenario(report.RootElement).GetProperty("diagrams");
        Assert.Equal(JsonValueKind.Object, diagrams[0].ValueKind);
        Assert.Equal(big, Inflate(diagrams[0].GetProperty("$z").GetString()!));
        Assert.Equal(JsonValueKind.String, diagrams[1].ValueKind);
        Assert.Equal(2, report.RootElement.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void The_mergeable_file_compresses_by_the_same_rule()
    {
        var json = ReportGenerator.GenerateMergeableReportJson(Features(), Start, End,
            Diagrams(["@startuml\nA -> B\n@enduml"]).ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [new ComponentRelationship("Test", "Orders", "HTTP", new HashSet<string> { "POST /orders" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null,
            diagnostics: null, trackedLogs: Logs(Json(2000), Json(20)), suite: "payloads", environment: RunEnvironment.Unrecorded,
            compressPayloads: true);

        using var report = JsonDocument.Parse(json);
        var (request, response) = Contents(report.RootElement);
        Assert.Equal(JsonValueKind.Object, request.ValueKind);
        Assert.Equal(JsonValueKind.String, response.ValueKind);
        Assert.Equal(2, report.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("mergeableFormatVersion").GetInt32());
    }

    [Fact]
    public void The_run_writes_compressed_payloads_when_the_option_is_on()
    {
        // The logger is process-wide, so this test's calls carry an id of their own.
        var testId = "payload-run-" + Guid.NewGuid().ToString("N");
        foreach (var log in Logs(Json(2000), Json(20), testId))
            RequestResponseLogger.Log(log);

        ReportGenerator.CreateStandardReportsWithDiagrams(Features(testId), Start, End, new ReportConfigurationOptions
        {
            ReportsFolderPath = _directory,
            CompressTestRunReportPayloads = true,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        });

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        var scenario = report.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == testId);
        Assert.Equal(JsonValueKind.Object, scenario.GetProperty("httpInteractions")[0].GetProperty("content").ValueKind);
        Assert.Equal(2, report.RootElement.GetProperty("formatVersion").GetInt32());
    }

    // ─── Fixture ───────────────────────────────────────────────

    internal static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);
    internal const string TestId = "payload-t1";

    /// <summary>A JSON body of exactly <paramref name="length"/> characters that compresses well.</summary>
    internal static string Json(int length)
    {
        const string head = "{\"note\":\"";
        const string tail = "\"}";
        var filler = string.Concat(Enumerable.Range(0, length).Select(i => (char)('a' + i % 7)));
        return head + filler[..(length - head.Length - tail.Length)] + tail;
    }

    internal static Feature[] Features(string testId = TestId) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = testId, DisplayName = "Place an order", Result = ExecutionResult.Passed }]
        }
    ];

    internal static DiagramAsCode[] Diagrams(string[] sources) => sources.Select(s => new DiagramAsCode(TestId, "", s)).ToArray();

    internal static RequestResponseLog[] Logs(string requestBody, string responseBody, string testId = TestId)
    {
        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero);
        var pairId = new Guid("7a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0b01");
        var traceId = new Guid("7a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0b02");
        return
        [
            new RequestResponseLog("Place an order", testId, HttpMethod.Post, requestBody, new Uri("http://orders/api/orders"), [],
                "orders", "test", RequestResponseType.Request, traceId, pairId, false)
            { Timestamp = at },
            new RequestResponseLog("Place an order", testId, HttpMethod.Post, responseBody, new Uri("http://orders/api/orders"), [],
                "orders", "test", RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.Created)
            { Timestamp = at.AddMilliseconds(35) }
        ];
    }

    private JsonDocument Write(bool compress, string requestBody, string responseBody, string[]? diagrams = null)
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End,
            Path.Combine(_directory, "TestRunReport.json"), DataFormat.Json, Diagrams(diagrams ?? ["@startuml\nA -> B\n@enduml"]),
            Logs(requestBody, responseBody), diagnostics: null, fullStepDetail: true, ciMetadata: null, suite: "payloads",
            environment: RunEnvironment.Unrecorded, attribution: null, compressPayloads: compress);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonElement Scenario(JsonElement root) => root.GetProperty("features")[0].GetProperty("scenarios")[0];

    private static (JsonElement Request, JsonElement Response) Contents(JsonElement root)
    {
        var interactions = Scenario(root).GetProperty("httpInteractions");
        return (interactions[0].GetProperty("content"), interactions[1].GetProperty("content"));
    }

    internal static string Address(string text) =>
        "b:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();

    internal static string Inflate(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
