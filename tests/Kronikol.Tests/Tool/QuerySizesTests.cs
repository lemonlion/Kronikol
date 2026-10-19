using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query sizes</c> says what fills a report file (#119, <c>plans/REPORT_FORMAT_PLAN.md</c> section 4.9): its
/// bytes by part, stored and decoded, and the scenarios that hold the most. Every part is checked against an oracle that
/// reads the raw file on its own terms (the raw text of each value, with the whitespace outside strings taken out), and a
/// file whose payloads are compressed is checked against a plain copy of the same run: its decoded column is what the
/// plain copy stores.
/// </summary>
public class QuerySizesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-sizes").FullName;
    private readonly string _plain;
    private readonly string _compressed;

    public QuerySizesTests()
    {
        _plain = Write("plain", compress: false);
        _compressed = Write("compressed", compress: true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    // ─── The parts add up ──────────────────────────────────────

    [Fact]
    public void The_parts_of_a_plain_file_add_up_to_its_size_and_store_what_they_decode_to()
    {
        var sizes = Sizes(_plain);
        var length = new FileInfo(_plain).Length;

        Assert.Equal(length, sizes.Report.GetProperty("bytesOnDisk").GetInt64());
        Assert.Equal(length, sizes.Report.GetProperty("decodedBytes").GetInt64());
        Assert.Equal(0, sizes.Report.GetProperty("compressedPayloads").GetInt32());
        Assert.Equal(length, sizes.Parts.Values.Sum(p => p.Stored));
        Assert.Equal(length, sizes.Parts.Values.Sum(p => p.Decoded));
        Assert.All(sizes.Parts, part => Assert.Equal(part.Value.Stored, part.Value.Decoded));
    }

    [Fact]
    public void A_file_with_compressed_payloads_decodes_to_its_plain_copy_part_by_part()
    {
        var plain = Sizes(_plain);
        var compressed = Sizes(_compressed);

        // The fixture holds payloads past the threshold, so the two copies really differ on disk.
        Assert.True(new FileInfo(_compressed).Length < new FileInfo(_plain).Length);
        var wrappers = Encoding.UTF8.GetString(File.ReadAllBytes(_compressed)).Split("\"$z\"").Length - 1;
        Assert.True(wrappers >= 3, $"the fixture compressed {wrappers} payloads");
        Assert.Equal(wrappers, compressed.Report.GetProperty("compressedPayloads").GetInt32());

        Assert.Equal(new FileInfo(_compressed).Length, compressed.Report.GetProperty("bytesOnDisk").GetInt64());
        Assert.Equal(new FileInfo(_compressed).Length, compressed.Parts.Values.Sum(p => p.Stored));
        Assert.Equal(new FileInfo(_plain).Length, compressed.Report.GetProperty("decodedBytes").GetInt64());
        foreach (var (name, part) in plain.Parts)
            Assert.True(part.Stored == compressed.Parts[name].Decoded,
                $"{name}: the plain copy stores {part.Stored}, the compressed copy decodes to {compressed.Parts[name].Decoded}");

        Assert.True(compressed.Parts["bodies"].Stored < compressed.Parts["bodies"].Decoded);
        Assert.True(compressed.Parts["diagrams"].Stored < compressed.Parts["diagrams"].Decoded);
        foreach (var member in new[] { "count", "distinct", "distinctDecodedBytes" })
            Assert.Equal(plain.Bodies.GetProperty(member).GetInt64(), compressed.Bodies.GetProperty(member).GetInt64());
    }

    // ─── Each part, against an oracle ──────────────────────────

    [Fact]
    public void Each_part_holds_the_bytes_of_its_values_in_the_raw_file()
    {
        var raw = File.ReadAllBytes(_plain);
        var oracle = Oracle(raw);
        var sizes = Sizes(_plain);

        Assert.Equal(oracle.Whitespace, sizes.Parts["whitespace"].Stored);
        Assert.Equal(oracle.Steps, sizes.Parts["steps"].Stored);
        Assert.Equal(oracle.Calls, sizes.Parts["calls"].Stored);
        Assert.Equal(oracle.Bodies, sizes.Parts["bodies"].Stored);
        Assert.Equal(oracle.Diagrams, sizes.Parts["diagrams"].Stored);
        Assert.Equal(oracle.Annotations, sizes.Parts["annotations"].Stored);
        Assert.True(oracle.Steps > 0 && oracle.Calls > 0 && oracle.Bodies > 0 && oracle.Diagrams > 0 && oracle.Annotations > 0,
            "the fixture must give every part something to hold, or a part that is never counted passes");
    }

    [Fact]
    public void The_steps_say_how_many_of_their_bytes_are_keys_holding_null_or_nothing()
    {
        var oracle = Oracle(File.ReadAllBytes(_plain));
        var steps = Sizes(_plain).Items.Single(i => i.GetProperty("part").GetString() == "steps");

        Assert.True(oracle.EmptyStepKeys > 0);
        Assert.Equal(oracle.EmptyStepKeys, steps.GetProperty("emptyKeyBytes").GetInt64());
    }

    [Fact]
    public void Bodies_are_counted_in_all_and_once_each()
    {
        var contents = Contents(File.ReadAllBytes(_plain));
        var bodies = Sizes(_plain).Bodies;

        var distinct = contents.DistinctBy(c => c.Text).ToList();
        Assert.True(distinct.Count < contents.Count, "the fixture must repeat a body, or 'once each' is not tested");
        Assert.Equal(contents.Count, bodies.GetProperty("count").GetInt32());
        Assert.Equal(distinct.Count, bodies.GetProperty("distinct").GetInt32());
        Assert.Equal(distinct.Sum(c => c.Bytes), bodies.GetProperty("distinctDecodedBytes").GetInt64());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(4096)]
    public void Where_the_reads_fall_changes_nothing(int window)
    {
        foreach (var report in new[] { _plain, _compressed })
        {
            ReportSizes whole, small;
            using (var stream = File.OpenRead(report))
                whole = ReportSizes.Measure(stream);
            using (var stream = File.OpenRead(report))
                small = ReportSizes.Measure(stream, window);

            Assert.Equal(new FileInfo(report).Length, small.StoredTotal);
            for (var part = 0; part < ReportSizes.PartNames.Length; part++)
            {
                Assert.Equal(whole.Stored(part), small.Stored(part));
                Assert.Equal(whole.Decoded(part), small.Decoded(part));
            }
            Assert.Equal(whole.StepEmptyKeyBytes, small.StepEmptyKeyBytes);
            Assert.Equal((whole.BodyCount, whole.DistinctBodies, whole.DistinctBodyBytes), (small.BodyCount, small.DistinctBodies, small.DistinctBodyBytes));
            Assert.Equal(whole.CompressedPayloads, small.CompressedPayloads);
            Assert.Equal(whole.Scenarios, small.Scenarios);
        }
    }

    // ─── The largest scenarios ─────────────────────────────────

    [Fact]
    public void The_largest_scenarios_come_first_with_their_own_bytes()
    {
        var raw = File.ReadAllBytes(_plain);
        var expected = ScenarioObjects(raw)
            .Select((bytes, ordinal) => (Address: $"s{ordinal}", Bytes: bytes))
            .OrderByDescending(s => s.Bytes)
            .ToList();
        var largest = Sizes(_plain).Largest;

        Assert.Equal(expected.Select(s => s.Address), largest.Select(s => s.GetProperty("address").GetString()));
        Assert.Equal(expected.Select(s => s.Bytes), largest.Select(s => s.GetProperty("decodedBytes").GetInt64()));
        Assert.Equal("Place an order", largest[0].GetProperty("scenario").GetString());
        Assert.Equal("Orders", largest[0].GetProperty("feature").GetString());
    }

    [Fact]
    public void A_scenario_whose_payloads_are_compressed_is_ranked_by_what_it_decodes_to()
    {
        var plain = Sizes(_plain).Largest;
        var compressed = Sizes(_compressed).Largest;

        Assert.Equal(plain.Select(s => s.GetProperty("address").GetString()), compressed.Select(s => s.GetProperty("address").GetString()));
        Assert.Equal(plain.Select(s => s.GetProperty("decodedBytes").GetInt64()), compressed.Select(s => s.GetProperty("decodedBytes").GetInt64()));
        Assert.True(compressed[0].GetProperty("storedBytes").GetInt64() < compressed[0].GetProperty("decodedBytes").GetInt64());
        // The order's scenario is first decoded, and second by what it stores: the catalogue's steps outweigh it on disk.
        Assert.Equal("s0", compressed[0].GetProperty("address").GetString());
        Assert.Equal("s3", compressed[1].GetProperty("address").GetString());
        Assert.True(compressed[1].GetProperty("storedBytes").GetInt64() > compressed[0].GetProperty("storedBytes").GetInt64(),
            "the fixture must make the stored and decoded rankings differ");
    }

    [Fact]
    public void Only_the_ten_largest_scenarios_are_listed()
    {
        var directory = Path.Combine(_root, "many");
        Directory.CreateDirectory(directory);
        var scenarios = Enumerable.Range(0, 12).Select(i => new Scenario
        {
            Id = $"sc{i}", DisplayName = "Scenario " + new string('x', i * 40), Result = ExecutionResult.Passed
        }).ToArray();
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var report = ReportGenerator.GenerateTestRunReportData([new Feature { DisplayName = "Many", Scenarios = scenarios }],
            start, start.AddMinutes(1), Path.Combine(directory, "TestRunReport.json"), DataFormat.Json, null, []);

        var largest = Sizes(report).Largest;

        Assert.Equal(10, largest.Count);
        Assert.Equal("s11", largest[0].GetProperty("address").GetString());
        Assert.DoesNotContain(largest, s => s.GetProperty("address").GetString() is "s0" or "s1");
    }

    // ─── The mergeable file's own parts ────────────────────────

    [Fact]
    public void Internal_flow_segments_and_the_whole_test_flow_are_parts_of_their_own()
    {
        var node = JsonNode.Parse(File.ReadAllText(_plain))!.AsObject();
        node["internalFlowSegments"] = new JsonObject { ["seg-1"] = new string('f', 3000) };
        node["wholeTestFlow"] = new JsonObject { ["flow"] = new JsonObject { ["activityHtml"] = new string('a', 2000) } };
        var path = Path.Combine(_root, "mergeable", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        var sizes = Sizes(path);

        Assert.InRange(sizes.Parts["internal-flow segments"].Stored, 3000, 3100);
        Assert.InRange(sizes.Parts["whole-test flow"].Stored, 2000, 2100);
        Assert.Equal(new FileInfo(path).Length, sizes.Parts.Values.Sum(p => p.Stored));
    }

    // ─── Text, and what it refuses ─────────────────────────────

    [Fact]
    public void The_text_answer_prints_every_part_and_the_largest_scenarios()
    {
        var text = Query(["sizes", _compressed]);
        var sizes = Sizes(_compressed);

        Assert.Contains($"{N0(sizes.Report.GetProperty("bytesOnDisk").GetInt64())} bytes on disk", text, StringComparison.Ordinal);
        Assert.Contains($"{N0(sizes.Report.GetProperty("decodedBytes").GetInt64())} decoded", text, StringComparison.Ordinal);
        foreach (var (name, part) in sizes.Parts)
        {
            var row = text.Split('\n').SingleOrDefault(l => l.StartsWith(name + " ", StringComparison.Ordinal));
            Assert.True(row is not null, $"no row for {name} in:\n{text}");
            Assert.Contains(N0(part.Stored), row, StringComparison.Ordinal);
            Assert.Contains(N0(part.Decoded), row, StringComparison.Ordinal);
        }
        Assert.Contains("s0 ", text, StringComparison.Ordinal);
        Assert.Contains("Place an order", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_is_refused_because_the_sizes_are_the_file_s()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = QueryCommand.Run(["sizes", _plain, "s0"], output, error);

        Assert.Equal(2, exit);
        Assert.Contains("s0", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_changed_after_the_scan_is_refused_rather_than_measured()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var path = Path.Combine(_root, "changed", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(_plain, path);
        Assert.Contains("bytes on disk", Query(["sizes", path]), StringComparison.Ordinal);

        QueryCommand.AfterScan = changed => File.WriteAllText(changed, File.ReadAllText(_compressed));
        int exit;
        try
        {
            exit = QueryCommand.Run(["sizes", path], output, error);
        }
        finally
        {
            QueryCommand.AfterScan = null;
        }

        Assert.Equal(1, exit);
        Assert.Contains("changed while it was being read", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("bytes on disk", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_half_rewritten_after_the_scan_is_refused_rather_than_failing_to_parse()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var path = Path.Combine(_root, "half", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(_plain, path);

        // A run caught writing its new report: the first half of a file is not JSON.
        QueryCommand.AfterScan = changed => File.WriteAllBytes(changed, File.ReadAllBytes(_plain)[..(int)(new FileInfo(_plain).Length / 2)]);
        int exit;
        try
        {
            exit = QueryCommand.Run(["sizes", path], output, error);
        }
        finally
        {
            QueryCommand.AfterScan = null;
        }

        Assert.Equal(1, exit);
        Assert.Contains("changed while it was being read", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Whitespace_between_a_key_and_its_colon_is_whitespace()
    {
        // System.Text.Json writes "key": value; other writers put a space before the colon too, and the reader takes it as
        // part of the key's token.
        var path = Path.Combine(_root, "spaced", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "formatVersion" : 1,
              "startTime" : "2026-01-01T10:00:00Z",
              "endTime" : "2026-01-01T10:01:00Z",
              "features" : [
                {
                  "name" : "Orders",
                  "labels" : [ ],
                  "scenarios" : [
                    {
                      "id" : "place",
                      "stableId" : "abc",
                      "name" : "Place an order",
                      "result" : "Passed",
                      "durationSeconds" : 1.5,
                      "isHappyPath" : true,
                      "labels" : [ ],
                      "categories" : [ ],
                      "steps" : [ { "keyword" : "Given", "text" : "an order", "status" : "Passed", "comments" : null } ]
                    }
                  ]
                }
              ],
              "background" : { "calls" : [ ], "afterScenarioEnd" : [ ], "interactions" : [ ] }
            }
            """);

        var sizes = Sizes(path);
        var raw = File.ReadAllBytes(path);

        Assert.Equal(raw.Length, sizes.Parts.Values.Sum(p => p.Stored));
        Assert.Equal(WhitespaceOutsideStrings(raw), sizes.Parts["whitespace"].Stored);
        Assert.Equal(Oracle(raw).Steps, sizes.Parts["steps"].Stored);
        Assert.Equal(Oracle(raw).EmptyStepKeys, sizes.Items.Single(i => i.GetProperty("part").GetString() == "steps").GetProperty("emptyKeyBytes").GetInt64());
    }

    // ─── Bytes per bucket (#116) ───────────────────────────────

    [Fact]
    public void A_bucket_carries_the_bytes_services_counts_for_the_same_service()
    {
        using var services = JsonDocument.Parse(Query(["services", _compressed, "--json"]));
        using var grouped = JsonDocument.Parse(Query(["interactions", _compressed, "--group-by", "service", "--json"]));

        var byService = services.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("service").GetString()!, i => i.GetProperty("bytes").GetInt64());
        var byBucket = grouped.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(b => b.GetProperty("key").GetProperty("service").GetString()!, b => b.GetProperty("bytes").GetInt64());

        Assert.Equal(3, byBucket.Count);
        Assert.All(byBucket.Values, bytes => Assert.True(bytes > 0));
        Assert.Equal(byService.OrderBy(e => e.Key, StringComparer.Ordinal), byBucket.OrderBy(e => e.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void A_bucket_s_bytes_are_its_request_and_response_bodies_decoded()
    {
        using var grouped = JsonDocument.Parse(Query(["interactions", _compressed, "--group-by", "service,method", "--json"]));
        var orders = grouped.RootElement.GetProperty("items").EnumerateArray()
            .Single(b => b.GetProperty("key").GetProperty("service").GetString() == "Orders");

        // Counted as the index counts a body, in characters, whether the body was stored compressed or not.
        Assert.Equal(RequestBody.Length + ResponseBody.Length, orders.GetProperty("bytes").GetInt64());
    }

    [Fact]
    public void Sort_bytes_puts_the_heaviest_bucket_first_and_the_next_page_keeps_the_order()
    {
        var text = Query(["interactions", _plain, "--group-by", "service", "--sort", "bytes", "--limit", "1"]);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("bytes", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Orders ", lines[1], StringComparison.Ordinal);
        Assert.Contains(QueryWriter.Size(RequestBody.Length + ResponseBody.Length), lines[1], StringComparison.Ordinal);
        var next = lines.Single(l => l.Contains("next:", StringComparison.Ordinal));
        Assert.Contains("--sort bytes", next, StringComparison.Ordinal);
        Assert.Contains("--offset 1", next, StringComparison.Ordinal);

        var byCalls = Query(["interactions", _plain, "--group-by", "service", "--limit", "1"]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Stock ", byCalls[1], StringComparison.Ordinal);
    }

    // ─── Running ───────────────────────────────────────────────

    private sealed record Part(long Stored, long Decoded);

    private sealed record Measured(JsonElement Report, List<JsonElement> Items, Dictionary<string, Part> Parts, JsonElement Bodies, List<JsonElement> Largest);

    private static Measured Sizes(string report)
    {
        var root = JsonDocument.Parse(Query(["sizes", report, "--json"])).RootElement;
        var items = root.GetProperty("items").EnumerateArray().ToList();
        var parts = items.ToDictionary(i => i.GetProperty("part").GetString()!,
            i => new Part(i.GetProperty("storedBytes").GetInt64(), i.GetProperty("decodedBytes").GetInt64()));
        var bodies = items.Single(i => i.GetProperty("part").GetString() == "bodies");
        return new Measured(root.GetProperty("report"), items, parts, bodies, root.GetProperty("largestScenarios").EnumerateArray().ToList());
    }

    private static string Query(IReadOnlyList<string> args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    // ─── The oracle: the raw file read on its own terms ────────

    private sealed record OracleParts(long Whitespace, long Steps, long Calls, long Bodies, long Diagrams, long Annotations, long EmptyStepKeys);

    private static OracleParts Oracle(byte[] raw)
    {
        using var document = JsonDocument.Parse(raw);
        long steps = 0, calls = 0, bodies = 0, diagrams = 0, annotations = 0, empty = 0;

        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        foreach (var scenario in feature.GetProperty("scenarios").EnumerateArray())
        foreach (var property in scenario.EnumerateObject())
        {
            // Every property but the first carries the comma before it, and none of these is first.
            var cost = PropertyCost(property);
            switch (property.Name)
            {
                case "steps" or "backgroundSteps":
                    steps += cost;
                    empty += EmptyKeys(property.Value);
                    break;
                case "httpInteractions":
                    var content = property.Value.EnumerateArray().Sum(ContentCost);
                    calls += cost - content;
                    bodies += content;
                    break;
                case "diagrams":
                    diagrams += cost;
                    break;
                case "annotations":
                    annotations += cost;
                    break;
            }
        }

        var background = document.RootElement.EnumerateObject().Single(p => p.Name == "background");
        var backgroundContent = background.Value.EnumerateObject()
            .Where(p => p.Name == "interactions").Sum(p => p.Value.EnumerateArray().Sum(ContentCost));
        calls += PropertyCost(background) - backgroundContent;
        bodies += backgroundContent;

        return new OracleParts(WhitespaceOutsideStrings(raw), steps, calls, bodies, diagrams, annotations, empty);
    }

    /// <summary>A property's bytes with the whitespace outside strings taken out: the comma before it, its name, the colon and its value.</summary>
    private static long PropertyCost(JsonProperty property) =>
        1 + Utf8Length(JsonSerializer.Serialize(property.Name)) + 1 + WithoutWhitespace(property.Value.GetRawText());

    private static long ContentCost(JsonElement interaction)
    {
        var properties = interaction.EnumerateObject().ToList();
        var index = properties.FindIndex(p => p.Name == "content");
        if (index < 0)
            return 0;
        return PropertyCost(properties[index]) - (index == 0 ? 1 : 0);
    }

    /// <summary>Every body in the file: each interaction's <c>content</c> that holds text, with the bytes its value takes.</summary>
    private static List<(string Text, long Bytes)> Contents(byte[] raw)
    {
        using var document = JsonDocument.Parse(raw);
        var interactions = document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .SelectMany(s => s.TryGetProperty("httpInteractions", out var calls) ? calls.EnumerateArray() : Enumerable.Empty<JsonElement>())
            .Concat(document.RootElement.GetProperty("background").TryGetProperty("interactions", out var background)
                ? background.EnumerateArray() : Enumerable.Empty<JsonElement>());
        return interactions
            .Where(i => i.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            .Select(i => (i.GetProperty("content").GetString()!, (long)Utf8Length(i.GetProperty("content").GetRawText())))
            .ToList();
    }

    /// <summary>The bytes of every key in a step tree whose value is null, [] or {}, with the comma before it when it has one.</summary>
    private static long EmptyKeys(JsonElement element)
    {
        long total = 0;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                total += EmptyKeys(item);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            var first = true;
            foreach (var property in element.EnumerateObject())
            {
                var value = property.Value;
                var isEmpty = value.ValueKind == JsonValueKind.Null
                              || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0)
                              || (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any());
                if (isEmpty)
                    total += (first ? 0 : 1) + Utf8Length(JsonSerializer.Serialize(property.Name)) + 1 + WithoutWhitespace(value.GetRawText());
                else
                    total += EmptyKeys(value);
                first = false;
            }
        }
        return total;
    }

    private static List<long> ScenarioObjects(byte[] raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Select(s => (long)Utf8Length(s.GetRawText()))
            .ToList();
    }

    private static long WhitespaceOutsideStrings(byte[] raw)
    {
        long count = 0;
        bool inString = false, escaped = false;
        foreach (var b in raw)
        {
            if (inString)
            {
                if (escaped) escaped = false;
                else if (b == '\\') escaped = true;
                else if (b == '"') inString = false;
            }
            else if (b == '"') inString = true;
            else if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') count++;
        }
        return count;
    }

    private static long WithoutWhitespace(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return bytes.Length - WhitespaceOutsideStrings(bytes);
    }

    private static int Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);

    private static string N0(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    // ─── The run ───────────────────────────────────────────────

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
        lines = Enumerable.Range(1, ReportPayloads.Threshold / 20).Select(i => new { sku = $"W-{i}", price = 100 + i }).ToArray()
    });

    private const string StockBody = "{\"sku\":\"W-1\",\"inStock\":true}";

    private static readonly string Diagram =
        "@startuml\nactor Test\nparticipant Orders\nTest -> Orders : POST /api/orders\nnote left\n" + RequestBody
        + "\nend note\nOrders --> Test : 201 Created\n@enduml";

    private string Write(string name, bool compress)
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
                    new Scenario
                    {
                        Id = "place", DisplayName = "Place an order", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "I place an order", Status = ExecutionResult.Passed,
                                SubSteps =
                                [
                                    new ScenarioStep { Keyword = "And", Text = "I choose a widget", Status = ExecutionResult.Passed,
                                        SubSteps = [new ScenarioStep { Keyword = "And", Text = "I pay", Status = ExecutionResult.Passed }] }
                                ]
                            }
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
                    },
                    // Larger on disk than the order's scenario once that one's payloads are compressed, and smaller
                    // decoded: a ranking by stored bytes puts the two the other way round.
                    new Scenario
                    {
                        Id = "browse", DisplayName = "Browse the catalogue", Result = ExecutionResult.Passed,
                        Steps = Enumerable.Range(1, 25).Select(i => new ScenarioStep
                        {
                            Keyword = "And", Text = $"I open page {i} of the catalogue and read every widget listed there", Status = ExecutionResult.Passed
                        }).ToArray()
                    }
                ]
            }
        ];

        RequestResponseLog[] logs =
        [
            new("Place an order", "place", HttpMethod.Post, RequestBody, new Uri("http://orders/api/orders"), [("accept", "application/json")],
                "Orders", "Test", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c01"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c02"), false)
            { Timestamp = at },
            new("Place an order", "place", HttpMethod.Post, ResponseBody, new Uri("http://orders/api/orders"), [],
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
            new("Check stock", "stock", HttpMethod.Get, null, new Uri("http://stock/api/stock/W-1"), [],
                "Stock", "Test", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c07"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c08"), false)
            { Timestamp = at.AddSeconds(3) },
            new("Check stock", "stock", HttpMethod.Get, StockBody, new Uri("http://stock/api/stock/W-1"), [],
                "Stock", "Test", RequestResponseType.Response, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c07"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c08"), false, HttpStatusCode.OK)
            { Timestamp = at.AddSeconds(3).AddMilliseconds(5) },
            new("Check stock", "stock", HttpMethod.Get, "{\"sku\":\"W-2\",\"inStock\":false}", new Uri("http://stock/api/stock/W-2"), [],
                "Stock", "Test", RequestResponseType.Request, new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c09"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c0a"), false)
            { Timestamp = at.AddSeconds(4) },
            // A custom diagram marker: the data file keeps it as a scenario annotation.
            new("Place an order", "place", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request,
                new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c0b"), new Guid("8a1b3a6e-0d1c-4f55-9a53-5c8c3c1f0c0c"), false)
            { Timestamp = at.AddMilliseconds(50), IsOverrideStart = true, PlantUml = "note over Orders : Order placed", MarkerKind = DiagramMarkerKind.Custom }
        ];

        return ReportGenerator.GenerateTestRunReportData(features, start, start.AddMinutes(1),
            Path.Combine(directory, "TestRunReport.json"), DataFormat.Json,
            [new DiagramAsCode("place", "", Diagram), new DiagramAsCode("place", "", Diagram + "\n' second")], logs,
            diagnostics: null, fullStepDetail: true, ciMetadata: null, suite: "sizes", environment: RunEnvironment.Unrecorded,
            attribution: null, compressPayloads: compress);
    }
}
