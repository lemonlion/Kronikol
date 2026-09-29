using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>TestRunReport.json</c> is written with the relaxed encoder, in all three of its forms: the standard file, the
/// mergeable one and a merge's output. A quote inside a string is written <c>\"</c>, and <c>&lt;</c>, <c>&gt;</c>,
/// <c>&amp;</c>, <c>'</c>, <c>+</c> and non-ASCII text are written as they are. Until 3.34.2 the default encoder
/// wrote each of them as a six-byte escape, and on a report of JSON bodies and PlantUML source the escaped quotes
/// alone were a fifth of the file (<c>plans/V4_PLAN.md</c> F1). The JSON value is the same, so every reader, and
/// every <c>b:</c> address (a hash of the decoded text), is unchanged: the facts below hold both.
/// </summary>
public class ReportJsonEncodingTests : IDisposable
{
    /// <summary>
    /// A captured body holding every character the two encoders treat differently: quotes (inside a JSON string,
    /// so with the backslashes that escape them), markup characters, an apostrophe, a plus, a backslash, non-ASCII
    /// text in the Basic Multilingual Plane and an emoji outside it.
    /// </summary>
    internal const string Body = """{"note":"a \"quoted\" <b>tag</b> & 'apos' + plus","path":"C:\\tmp","name":"Zoë ✓ 😀"}""";

    /// <summary>
    /// The addresses <c>kronikol query http</c> printed on 3.34.1, the last release written with the default encoder,
    /// for <see cref="Body"/> and for the response's body. An address is a hash of the decoded body, so a change of
    /// encoding must not move it.
    /// </summary>
    private const string RequestAddressOn3341 = "b:2925d5f2";

    private const string ResponseAddressOn3341 = "b:ed60c37b";

    private static readonly string[] EscapesTheRelaxedEncoderDoesNotWrite = ["0022", "0026", "0027", "002B", "003C", "003E", "00EB", "2713"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kronikol-encoding-" + Guid.NewGuid().ToString("N"));

    public ReportJsonEncodingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void The_standard_file_writes_quotes_and_markup_characters_as_they_are()
    {
        var text = File.ReadAllText(WriteStandard());

        AssertRelaxed(text);
    }

    [Fact]
    public void The_mergeable_file_writes_them_the_same_way()
    {
        var text = WriteMergeable();

        AssertRelaxed(text);
    }

    [Fact]
    public void A_merged_file_writes_them_the_same_way()
    {
        File.WriteAllText(Path.Combine(_directory, "shard1.json"), WriteMergeable());

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], output, error);

        Assert.True(exit == 0, error.ToString());
        AssertRelaxed(File.ReadAllText(Path.Combine(_directory, "Combined.json")));
    }

    [Fact]
    public void A_body_keeps_its_address_and_its_value()
    {
        var report = WriteStandard();

        var request = Query("http", report, "s0/i0");
        Assert.True(request.Contains(RequestAddressOn3341, StringComparison.Ordinal), request);
        var response = Query("http", report, "s0/i1");
        Assert.True(response.Contains(ResponseAddressOn3341, StringComparison.Ordinal), response);

        var target = Path.Combine(_directory, "body.json");
        Query("http", report, "s0/i0", "--body", "--out", target);
        using var written = JsonDocument.Parse(File.ReadAllText(target, Encoding.UTF8));
        Assert.Equal("a \"quoted\" <b>tag</b> & 'apos' + plus", written.RootElement.GetProperty("note").GetString());
        Assert.Equal("C:" + (char)92 + "tmp", written.RootElement.GetProperty("path").GetString());
        Assert.Equal("Zoë ✓ 😀", written.RootElement.GetProperty("name").GetString());

        var byAddress = Query("body", report, RequestAddressOn3341);
        Assert.Contains("Zoë ✓", byAddress);
    }

    /// <summary>
    /// The tool prints a body with its characters as they were captured. Until 3.34.2 its printer used the default
    /// encoder, so a quote inside a string printed as a six-byte escape, and so did markup characters and non-ASCII
    /// text: output a search for the captured text missed. (An emoji is still printed escaped: every encoder the
    /// runtime offers escapes characters outside the Basic Multilingual Plane.)
    /// </summary>
    [Fact]
    public void The_tool_prints_a_body_with_its_characters_as_they_were_captured()
    {
        var report = WriteStandard();

        foreach (var printed in new[] { Query("http", report, "s0/i0", "--body"), Query("http", report, "s0/i0", "--path", "$") })
        {
            AssertNoEscapes(printed);
            Assert.Contains("<b>tag</b> & 'apos' + plus", printed);
            Assert.Contains("Zoë ✓", printed);
        }
    }

    /// <summary>
    /// The whole file, byte for byte, for a report that exercises every emitter, with the version normalised and line
    /// ends read as LF (the writer uses the platform's). It is the baseline #85's option must leave untouched when it
    /// is off (<c>plans/V4_PLAN.md</c> R2). A change to the file's shape changes the pin on purpose: the failure names
    /// the file it wrote the new bytes to, to compare and copy over <c>TestData/Reports/TestRunReport.pin.json</c>.
    /// </summary>
    [Fact]
    public void The_file_matches_its_pin()
    {
        var actual = Normalise(File.ReadAllText(WriteStandard()));
        var pin = Path.Combine(AppContext.BaseDirectory, "TestData", "Reports", "TestRunReport.pin.json");
        var expected = File.Exists(pin) ? Normalise(File.ReadAllText(pin)) : "";

        if (actual != expected)
        {
            var written = Path.Combine(Path.GetTempPath(), "TestRunReport.pin.actual.json");
            File.WriteAllText(written, actual);
            Assert.Fail($"TestRunReport.json no longer matches its pin; this build's bytes are in {written}");
        }
    }

    // ─── Writers ───────────────────────────────────────────────

    private string WriteStandard() =>
        ReportGenerator.GenerateTestRunReportData(TestRunReportSchemaContractTests.RichFeatures(),
            TestRunReportSchemaContractTests.Start, TestRunReportSchemaContractTests.End,
            Path.Combine(_directory, "TestRunReport.json"), DataFormat.Json, Diagrams(), Logs(),
            TestRunReportSchemaContractTests.Diagnostics(), suite: "encoding-pin", environment: RunEnvironment.Unrecorded);

    private static string WriteMergeable() =>
        ReportGenerator.GenerateMergeableReportJson(TestRunReportSchemaContractTests.RichFeatures(),
            TestRunReportSchemaContractTests.Start, TestRunReportSchemaContractTests.End,
            Diagrams().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [new ComponentRelationship("Test", "Orders <api>", "HTTP", new HashSet<string> { "POST /orders" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null,
            TestRunReportSchemaContractTests.Diagnostics(), Logs(), suite: "encoding-pin", environment: RunEnvironment.Unrecorded);

    private static void AssertNoEscapes(string text)
    {
        foreach (var hex in EscapesTheRelaxedEncoderDoesNotWrite)
        {
            var escape = "\\" + "u" + hex;
            Assert.False(text.Contains(escape, StringComparison.OrdinalIgnoreCase), $"it writes {escape}: {text}");
        }
    }

    private static void AssertRelaxed(string text)
    {
        AssertNoEscapes(text);

        // The body as a JSON string inside the file: its own backslashes doubled, its quotes written \".
        Assert.Contains(JsonSerializer.Serialize(Body, RelaxedStringOptions), text);
        Assert.Contains("Zoë ✓", text);
    }

    private static readonly JsonSerializerOptions RelaxedStringOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Normalise(string text) =>
        Regex.Replace(text.Replace("\r\n", "\n"), "\"kronikolVersion\": \"[^\"]*\"", "\"kronikolVersion\": \"(normalised)\"");

    private static string Query(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    // ─── The fixture: the schema contract's report, with fixed ids and the body above ───

    private static DiagramAsCode[] Diagrams() =>
    [
        new DiagramAsCode(TestRunReportSchemaContractTests.TestId, "",
            "@startuml\nA -> B : POST \"/api/orders\" <&lock>\nnote left\n" + Body + "\nend note\n@enduml")
    ];

    private static RequestResponseLog[] Logs()
    {
        const string testId = TestRunReportSchemaContractTests.TestId;
        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero);
        var pairId = new Guid("6a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0a01");
        var traceId = new Guid("6a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0a02");
        return
        [
            new RequestResponseLog(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
                RequestResponseType.Request, new Guid("6a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0a03"), new Guid("6a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0a04"), false)
            { IsOverrideStart = true, PlantUml = "note over A: row \"1\"", MarkerKind = DiagramMarkerKind.Row },
            new RequestResponseLog(testId, testId, HttpMethod.Post, Body, new Uri("http://orders/api/orders?q=a+b&r='c'"),
                [("accept", "application/json"), ("x-note", "say \"hi\" <now> & 'then' + more"), ("x-empty", null)], "orders", "test",
                RequestResponseType.Request, traceId, pairId, false,
                MetaType: RequestResponseMetaType.Default, DependencyCategory: "http", CallerDependencyCategory: "test")
            { Timestamp = at, ActivityTraceId = "4bf92f3577b34da6a3ce929d0e0e4736", ActivitySpanId = "00f067aa0ba902b7", CapturedBy = "wire", Phase = TestPhase.Action },
            new RequestResponseLog(testId, testId, HttpMethod.Post, "{\"total\":3902,\"note\":\"<ok> & 'fine' + \\\"done\\\"\"}", new Uri("http://orders/api/orders"), [],
                "orders", "test", RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.Created)
            { Timestamp = at.AddMilliseconds(35) },
            new RequestResponseLog(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
                RequestResponseType.Request, new Guid("6a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0a05"), new Guid("6a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0a06"), false)
            { IsOverrideStart = true, PlantUml = "note over A: custom <b>bold</b>", MarkerKind = DiagramMarkerKind.Custom }
        ];
    }
}
