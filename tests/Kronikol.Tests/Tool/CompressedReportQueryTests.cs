using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Reports;
using Kronikol.Constants;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Tool;

/// <summary>
/// A report written with <c>CompressTestRunReportPayloads</c> answers every question an uncompressed copy of the same
/// run answers, and the same way (#85, <c>plans/PAYLOAD_COMPRESSION_PLAN.md</c> §4): the two files differ only in
/// how a payload is stored, so every verb that reads one prints the same over both. The index takes a compressed
/// payload's address and length from the wrapper, and a payload is inflated only when a command reads it.
/// </summary>
public class CompressedReportQueryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kronikol-compressed-" + Guid.NewGuid().ToString("N"));
    private readonly string _plain;
    private readonly string _compressed;

    public CompressedReportQueryTests()
    {
        _plain = Write("plain", compress: false);
        // Compressed by the default option (on from 4.0.0), so the theory below is about a report written with no
        // options, and a default turned back off fails the fixture fact.
        _compressed = Write("compressed", compress: new ReportConfigurationOptions().CompressTestRunReportPayloads);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static TheoryData<string[]> Commands => new()
    {
        new[] { "http", "s0/i0" },
        new[] { "http", "s0/i0", "--body" },
        new[] { "http", "s0/i0", "--keys" },
        new[] { "http", "s0/i0", "--path", "$.order.id" },
        new[] { "http", "s0/i0", "--path", "$.order.items" },
        new[] { "http", "s0/i0", "--lines", "2-6" },
        new[] { "http", "s0/i1", "--body", "--out", "{out}" },
        new[] { "body", "{request}" },
        new[] { "body", "{request}", "--path", "$.order.note" },
        new[] { "values", "--path", "$.total" },
        new[] { "values", "--path", "$.order.id", "--request" },
        new[] { "interactions", "--where", "$.total > 3000" },
        new[] { "interactions", "--grep", "Widget" },
        new[] { "grep", "Widget" },
        new[] { "grep", "Widget", "--in", "bodies" },
        new[] { "grep", "Widget", "--values" },
        new[] { "grep", "Placed", "--in", "notes" },
        new[] { "grep", "3902", "--number" },
        new[] { "note", "s0/d0" },
        new[] { "note", "s0/d0/n0" },
        new[] { "diagram", "s0/d0", "--out", "{out}" },
        new[] { "diff", "s0/i0", "s0/i1" },
        new[] { "compare", "s0", "s1" },
        new[] { "failures" },
        new[] { "steps", "s1" },
        new[] { "flow", "s0" },
    };

    [Fact]
    public void The_fixture_holds_every_payload_the_verbs_read_compressed_in_one_copy_and_as_text_in_the_other()
    {
        using var compressed = JsonDocument.Parse(File.ReadAllText(_compressed));
        using var plain = JsonDocument.Parse(File.ReadAllText(_plain));

        Assert.Equal(2, compressed.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(1, plain.RootElement.GetProperty("formatVersion").GetInt32());
        foreach (var (document, kind) in new[] { (compressed, JsonValueKind.Object), (plain, JsonValueKind.String) })
        {
            var scenarios = document.RootElement.GetProperty("features")[0].GetProperty("scenarios");
            Assert.Equal(kind, scenarios[0].GetProperty("httpInteractions")[0].GetProperty("content").ValueKind);
            Assert.Equal(kind, scenarios[0].GetProperty("httpInteractions")[1].GetProperty("content").ValueKind);
            Assert.Equal(kind, scenarios[0].GetProperty("diagrams")[0].ValueKind);
            Assert.Equal(kind, scenarios[1].GetProperty("httpInteractions")[0].GetProperty("content").ValueKind);
        }
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void A_verb_answers_the_same_over_both_copies(string[] command)
    {
        var (plainOutput, plainFile) = Run(command, _plain);
        var (compressedOutput, compressedFile) = Run(command, _compressed);

        Assert.Equal(plainOutput, compressedOutput);
        Assert.Equal(plainFile, compressedFile);
        Assert.False(string.IsNullOrWhiteSpace(plainOutput + plainFile), "the command printed nothing, so the comparison proves nothing");
    }

    /// <summary>
    /// The <c>failures</c> row of the theory compares the SQL placeholder hint, which reads the statement's text: the
    /// statement here is compressed in one copy, and the hint has to fire on both, or the comparison proves nothing.
    /// </summary>
    [Fact]
    public void The_sql_hint_reads_a_compressed_statement()
    {
        foreach (var report in new[] { _plain, _compressed })
            Assert.Contains("captured with placeholders and no values", Query(["failures", report]), StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_copies_diff_as_one_run()
    {
        var output = Query(["diff", _plain, _compressed]);
        var bodies = Query(["diff", _plain, _compressed, "--body", "s0/i0"]);

        Assert.DoesNotContain("changed", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identical", bodies, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_body_keeps_its_address_when_it_is_compressed()
    {
        var address = PayloadCompressionTests.Address(RequestBody);

        Assert.Contains(address, Query(["http", _plain, "s0/i0"]), StringComparison.Ordinal);
        Assert.Contains(address, Query(["http", _compressed, "s0/i0"]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_this_build_does_not_know_is_refused()
    {
        var future = Path.Combine(_root, "future", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(future)!);
        File.WriteAllText(future, File.ReadAllText(_compressed).Replace("\"formatVersion\": 2", "\"formatVersion\": 3", StringComparison.Ordinal));

        var error = new StringWriter();
        var exit = QueryCommand.Run(["summary", future], new StringWriter(), error);

        Assert.Equal(1, exit);
        Assert.Contains("formatVersion 3", error.ToString(), StringComparison.Ordinal);
    }

    // ─── Running ───────────────────────────────────────────────

    private (string Output, string? File) Run(string[] command, string report)
    {
        var directory = Path.GetDirectoryName(report)!;
        var outFile = Path.Combine(directory, "out.txt");
        var args = new List<string> { command[0], report };
        args.AddRange(command[1..].Select(a => a switch
        {
            "{out}" => outFile,
            "{request}" => PayloadCompressionTests.Address(RequestBody),
            _ => a
        }));

        var output = Query(args).Replace(directory, "<dir>", StringComparison.Ordinal);
        return (output, File.Exists(outFile) ? File.ReadAllText(outFile) : null);
    }

    private static string Query(IReadOnlyList<string> args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    // ─── The run: an order placed and one saved, each call's body long enough to be compressed ───

    private static readonly string RequestBody = JsonSerializer.Serialize(new
    {
        order = new
        {
            id = 4173,
            customer = "Zoë",
            note = "a \"quoted\" <b>note</b> & more",
            items = Enumerable.Range(1, ReportPayloads.Threshold / 30).Select(i => new { sku = $"W-{i}", name = "Widget", qty = i % 3 + 1 }).ToArray()
        }
    });

    private static readonly string ResponseBody = JsonSerializer.Serialize(new
    {
        id = 4173,
        total = 3902,
        status = "Placed",
        lines = Enumerable.Range(1, ReportPayloads.Threshold / 30).Select(i => new { sku = $"W-{i}", price = 100 + i, currency = "GBP" }).ToArray()
    });

    // Exactly the threshold long, so the compressed copy holds it compressed and `failures` still reads it: the verb
    // reads a statement of up to 8,192 characters, and a file written before 4.0.2 compressed them from 512.
    private static readonly string Statement = PadTo(
        "INSERT INTO orders (id, customer, status, total, currency, created_at, updated_at, channel, region, warehouse) VALUES "
        + "(@id, @customer, @status, @total, @currency, @created_at, @updated_at, @channel, @region, @warehouse); "
        + string.Concat(Enumerable.Range(1, 12).Select(i => $"INSERT INTO order_lines (order_id, sku, qty) VALUES (@id, @sku{i}, @qty{i}); ")),
        ReportPayloads.Threshold);

    private static string PadTo(string sql, int length) => sql + "-- " + new string('x', length - sql.Length - 3);

    private static readonly string Diagram =
        "@startuml\nactor Test\nparticipant Orders\nTest -> Orders : POST /api/orders\nnote left\n" + RequestBody
        + "\nend note\nOrders --> Test : 201 Created\nnote right\nPlaced order 4173 for Zoë\n" + ResponseBody + "\nend note\n@enduml";

    /// <summary>
    /// 4.0.0 and 4.0.1 compressed a payload from 512 characters, 4.0.2 from 8,192, and a reader takes a wrapper at any
    /// length, so the files those releases wrote answer as they did. The writer no longer makes such a file, so it is
    /// made here from a plain copy whose payloads are shorter than 8,192, by wrapping each of 512 characters or more
    /// the way their writer did. <c>failures</c> is among the verbs compared: its SQL hint reads a statement of up to
    /// 8,192 characters, the one reader with a length bound, and the statement here is compressed.
    /// </summary>
    [Fact]
    public void A_file_that_compressed_payloads_from_512_characters_answers_as_its_plain_copy()
    {
        var plain = Write("short-plain", compress: false, requestBody: ShortRequestBody, statement: ShortStatement);
        var older = WrapFrom(plain, 512, Path.Combine(_root, "short-older"));

        using (var document = JsonDocument.Parse(File.ReadAllText(older)))
        {
            var scenarios = document.RootElement.GetProperty("features")[0].GetProperty("scenarios");
            Assert.Equal(JsonValueKind.Object, scenarios[0].GetProperty("httpInteractions")[0].GetProperty("content").ValueKind);
            Assert.Equal(JsonValueKind.Object, scenarios[1].GetProperty("httpInteractions")[0].GetProperty("content").ValueKind);
        }
        Assert.InRange(ShortRequestBody.Length, 512, ReportPayloads.Threshold - 1);
        Assert.InRange(ShortStatement.Length, 512, ReportPayloads.Threshold - 1);
        Assert.Contains("captured with placeholders and no values", Query(["failures", older]), StringComparison.Ordinal);
        foreach (var command in new[] { new[] { "http", "s0/i0", "--body" }, new[] { "failures" }, new[] { "grep", "Widget", "--in", "bodies" } })
            Assert.Equal(Run(command, plain).Output, Run(command, older).Output);
    }

    private static readonly string ShortRequestBody = JsonSerializer.Serialize(new
    {
        order = new { id = 4173, customer = "Zoë", items = Enumerable.Range(1, 12).Select(i => new { sku = $"W-{i}", name = "Widget", qty = i % 3 + 1 }).ToArray() }
    });

    private static readonly string ShortStatement =
        "INSERT INTO orders (id, customer, status, total) VALUES (@id, @customer, @status, @total); "
        + string.Concat(Enumerable.Range(1, 12).Select(i => $"INSERT INTO order_lines (order_id, sku, qty) VALUES (@id, @sku{i}, @qty{i}); "));

    /// <summary>A copy of a plain report with every body and diagram of <paramref name="from"/> characters or more wrapped.</summary>
    private static string WrapFrom(string plainReport, int from, string directory)
    {
        var report = JsonNode.Parse(File.ReadAllText(plainReport))!;
        foreach (var scenario in report["features"]!.AsArray().SelectMany(f => f!["scenarios"]!.AsArray()))
        {
            foreach (var interaction in scenario!["httpInteractions"]?.AsArray() ?? [])
            {
                if (interaction!["content"] is JsonValue content && content.TryGetValue<string>(out var text) && text.Length >= from)
                    interaction["content"] = Wrapper(text);
            }

            var diagrams = scenario["diagrams"]?.AsArray() ?? [];
            for (var i = 0; i < diagrams.Count; i++)
            {
                if (diagrams[i] is JsonValue diagram && diagram.TryGetValue<string>(out var source) && source.Length >= from)
                    diagrams[i] = Wrapper(source);
            }
        }

        report["formatVersion"] = 2;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return path;
    }

    private static JsonObject Wrapper(string text)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        return new JsonObject
        {
            ["$h"] = PayloadCompressionTests.Address(text),
            ["$n"] = text.Length,
            ["$z"] = Convert.ToBase64String(buffer.ToArray())
        };
    }

    private string Write(string name, bool compress, string? requestBody = null, string? statement = null)
    {
        var directory = Path.Combine(_root, name);
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
                    new Scenario { Id = "place", DisplayName = "Place an order", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "When", Text = "I place an order", Status = ExecutionResult.Passed }] },
                    new Scenario { Id = "save", DisplayName = "Save the order", Result = ExecutionResult.Failed,
                        ErrorMessage = "Expected 1 row but found 0",
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the order is saved", Status = ExecutionResult.Failed, FailureMessage = "Expected 1 row but found 0" }] }
                ]
            }
        ];

        RequestResponseLog[] logs =
        [
            new("Place an order", "place", HttpMethod.Post, requestBody ?? RequestBody, new Uri("http://orders/api/orders"), [("accept", "application/json")],
                "Orders", "Test", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c01"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c02"), false)
            { Timestamp = at },
            new("Place an order", "place", HttpMethod.Post, ResponseBody, new Uri("http://orders/api/orders"), [],
                "Orders", "Test", RequestResponseType.Response, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c01"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c02"), false, HttpStatusCode.Created)
            { Timestamp = at.AddMilliseconds(40) },
            new("Save the order", "save", "Execute", statement ?? Statement, new Uri("sql://orders-db/orders"), [],
                "Orders DB", "Orders", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c03"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c04"), false,
                DependencyCategory: DependencyCategories.SqlServer)
            { Timestamp = at.AddSeconds(1) },
            new("Save the order", "save", "Execute", "0 rows", new Uri("sql://orders-db/orders"), [],
                "Orders DB", "Orders", RequestResponseType.Response, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c03"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c04"), false,
                DependencyCategory: DependencyCategories.SqlServer)
            { Timestamp = at.AddSeconds(1).AddMilliseconds(5) }
        ];

        return ReportGenerator.GenerateTestRunReportData(features, start, start.AddMinutes(1),
            Path.Combine(directory, "TestRunReport.json"), DataFormat.Json, [new DiagramAsCode("place", "", Diagram)], logs,
            diagnostics: null, fullStepDetail: true, ciMetadata: null, suite: "compressed", environment: RunEnvironment.Unrecorded,
            attribution: null, compressPayloads: compress);
    }
}
