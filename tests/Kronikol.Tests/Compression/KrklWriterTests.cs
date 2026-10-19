using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Compression;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Compression;

/// <summary>
/// The writer of the compressed report (plans/REPORT_FORMAT_PLAN.md 4.1 to 4.5; section 5, R4 items 1, 2, 10 and 12):
/// <see cref="ReportConfigurationOptions.CompressTestRunReport"/> writes <c>TestRunReport.krkl</c> in place of the JSON,
/// Kronikol's marker then one zstd frame with its checksum, around the JSON writer's model with no key that holds nothing
/// unless the schema requires it where it stands, no wrapper and <c>formatVersion</c> 3; and the run's manifest names it.
/// </summary>
[Collection("DiagramsFetcher")]
public class KrklWriterTests : IDisposable
{
    private const string LargeBody = "LARGE";
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-krkl-writer").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_option_writes_TestRunReport_krkl_and_no_json()
    {
        var directory = Run("krkl", compress: true);

        Assert.True(File.Exists(Path.Combine(directory, "TestRunReport.krkl")));
        Assert.False(File.Exists(Path.Combine(directory, "TestRunReport.json")));
        Assert.True(File.Exists(Path.Combine(directory, "TestRunReport.html")));
    }

    [Fact]
    public void Without_the_option_the_run_writes_its_JSON_as_before()
    {
        var directory = Run("json", compress: false);

        Assert.True(File.Exists(Path.Combine(directory, "TestRunReport.json")));
        Assert.False(File.Exists(Path.Combine(directory, "TestRunReport.krkl")));
    }

    [Fact]
    public void The_file_is_Kronikol_s_marker_then_one_zstd_frame_with_its_checksum()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Run("container", compress: true), "TestRunReport.krkl"));
        var decoded = Decode(bytes);

        Assert.Equal(0x184D2A50u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("KRKL"u8.ToArray(), bytes[8..12]);
        Assert.Equal(1, bytes[12]);
        Assert.Equal(decoded.Length, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(0xFD2FB528u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)));
        // The frame header descriptor's content-checksum flag.
        Assert.True((bytes[28] & 0x04) != 0, "the frame carries no checksum");
        // One frame: a second would follow the first frame's end, which only a decoder finds; the reader takes one
        // container and refuses trailing bytes as damage, so a whole decode of the container is the proof.
        Assert.Equal('{', (char)decoded[0]);
    }

    [Fact]
    public void The_JSON_inside_is_the_JSON_writer_s_model_without_what_holds_nothing_or_whitespace()
    {
        using var _ = RequestResponseLogger.IsolateForTests();
        var testId = LogCalls();
        var json = Run("same-json", compress: false, testId: testId, compressPayloads: false, isolate: false);
        var krkl = Run("same-krkl", compress: true, testId: testId, compressPayloads: false, isolate: false);

        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(json, "TestRunReport.schema.json"))).RootElement;
        var expected = SchemaWalk.Prune(JsonNode.Parse(File.ReadAllText(Path.Combine(json, "TestRunReport.json")))!, schema);
        expected["formatVersion"] = 3;
        var text = Encoding.UTF8.GetString(Decode(File.ReadAllBytes(Path.Combine(krkl, "TestRunReport.krkl"))));

        Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
        var actual = JsonNode.Parse(text);
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "the .krkl's JSON differs from the pruned JSON file at " + SchemaWalk.FirstDifference(expected, actual, "$"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_key_the_schema_requires_is_written_where_it_stands_and_no_other_holds_nothing(bool mergeable)
    {
        var directory = Run(mergeable ? "required-mergeable" : "required", compress: true, mergeable: mergeable);
        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "TestRunReport.schema.json"))).RootElement;
        var root = JsonNode.Parse(Decode(File.ReadAllBytes(Path.Combine(directory, "TestRunReport.krkl"))))!;

        var (missing, empty) = SchemaWalk.Check(root, schema);

        Assert.True(missing.Count == 0, "required keys missing: " + string.Join(", ", missing.Take(20)));
        Assert.True(empty.Count == 0, "keys holding nothing: " + string.Join(", ", empty.Take(20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payloads_are_text_and_formatVersion_is_3_whatever_CompressTestRunReportPayloads_says(bool mergeable)
    {
        // The mergeable file too, which a run builds by a model of its own (plans/REPORT_FORMAT_PLAN.md 4.2, F28).
        var directory = Run(mergeable ? "payloads-mergeable" : "payloads", compress: true, mergeable: mergeable, compressPayloads: true);
        var text = Encoding.UTF8.GetString(Decode(File.ReadAllBytes(Path.Combine(directory, "TestRunReport.krkl"))));
        var root = JsonNode.Parse(text)!;

        Assert.Equal(3, root["formatVersion"]!.GetValue<int>());
        Assert.DoesNotContain("\"$z\"", text, StringComparison.Ordinal);
        Assert.Contains(LargeBody, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_s_manifest_names_the_krkl_and_no_temporary_file_is_left()
    {
        var directory = Run("manifest", compress: true);

        var manifest = RunManifest.TryRead(Path.Combine(directory, RunManifest.FileName));

        Assert.NotNull(manifest);
        Assert.Contains("TestRunReport.krkl", manifest.Files);
        Assert.DoesNotContain("TestRunReport.json", manifest.Files);
        Assert.DoesNotContain(manifest.Files, file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Directory.EnumerateFiles(directory), file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_krkl_a_reader_holds_open_is_replaced_whole_or_this_run_s_copy_goes_beside_it()
    {
        var directory = Path.Combine(_root, "held");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "TestRunReport.krkl");
        var fallback = Path.Combine(directory, "TestRunReport2.krkl");
        File.WriteAllText(target, "an earlier run");

        Exception? thrown;
        var original = Console.Out;
        var captured = new StringWriter();
        // A viewer reading the earlier run's file.
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Console.SetOut(captured);
            try
            {
                thrown = Record.Exception(() => Run("held", compress: true));
            }
            finally
            {
                Console.SetOut(original);
            }
        }

        // Each output is isolated: one that fails is a diagnostic and a warning, and the run goes on.
        Assert.Null(thrown);
        if (OperatingSystem.IsWindows())
        {
            // Windows renames over no file another handle holds: this run's copy goes beside it, as a JSON file held the same
            // way does.
            Assert.Equal('{', (char)Decode(File.ReadAllBytes(fallback))[0]);
            Assert.Equal("an earlier run", File.ReadAllText(target));
            var manifest = RunManifest.TryRead(Path.Combine(directory, RunManifest.FileName));
            Assert.True(manifest is null || manifest.Files.Contains("TestRunReport2.krkl") && !manifest.Files.Contains("TestRunReport.krkl"),
                "the manifest names the earlier run's file");
            // The warning names the folder, as a JSON file's does; a failed rename's own message names no path.
            Assert.Contains("could not write TestRunReport.krkl", Reports.ConsoleLines.About(captured.ToString(), directory), StringComparison.Ordinal);
        }
        else
        {
            // Elsewhere the rename replaces the name, and the reader keeps reading the file it opened.
            Assert.False(File.Exists(fallback));
            Assert.Equal('{', (char)Decode(File.ReadAllBytes(target))[0]);
        }
        Assert.DoesNotContain(Directory.EnumerateFiles(directory), file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_frame_is_the_JSON_it_holds_compressed_in_one_pass_at_level_9_with_its_checksum()
    {
        // JSON of several hundred kilobytes, so the filter passes its output on many times before the end. A flush that
        // reached the compressor would end a zstd block each time, and a different level changes every byte.
        var model = new Dictionary<string, object?>
        {
            ["formatVersion"] = 1,
            ["features"] = Enumerable.Range(0, 300).Select(i => (object?)new Dictionary<string, object?>
            {
                ["name"] = $"feature {i}",
                ["body"] = string.Concat(Enumerable.Range(0, 40).Select(j => $"line {j} of feature {i}; ")),
                ["empty"] = null,
            }).ToList(),
        };
        using var file = new MemoryStream();
        var length = ReportGenerator.WriteCompressedReport(model, file);
        var bytes = file.ToArray();
        var json = Decode(bytes);

        using var expected = new MemoryStream();
        using (var zstd = new Kronikol.Compression.Zstd.CompressionStream(expected, 9, leaveOpen: true))
        {
            zstd.SetParameter(Kronikol.Compression.Zstd.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            zstd.Write(json);
        }

        Assert.True(length > 200_000, $"the JSON is {length:N0} bytes");
        Assert.Equal(length, json.Length);
        Assert.Equal(expected.ToArray(), bytes[ReportFileFormat.MarkerLength..]);
    }

    [Fact]
    public void Writing_a_krkl_allocates_a_fraction_of_its_JSON_where_building_the_string_takes_twice_it()
    {
        // A model whose JSON runs to megabytes, built before anything is measured, as a run's is.
        var model = new Dictionary<string, object?>
        {
            ["formatVersion"] = 1,
            ["features"] = Enumerable.Range(0, 4_000).Select(i => (object?)new Dictionary<string, object?>
            {
                ["name"] = $"feature {i}",
                ["body"] = new string((char)('a' + i % 26), 1_000),
                ["empty"] = null,
                ["labels"] = Array.Empty<string>(),
            }).ToList(),
            ["background"] = null,
        };
        // The first write pays for type metadata and the schema's requirements, once per process.
        using (var warm = File.Create(Path.Combine(_root, "warm.krkl")))
            ReportGenerator.WriteCompressedReport(new Dictionary<string, object?>(model), warm);

        long json, allocated;
        using (var file = File.Create(Path.Combine(_root, "measured.krkl")))
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            json = ReportGenerator.WriteCompressedReport(model, file);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        // The path it replaces, as the control: the JSON built as one string first.
        var beforeString = GC.GetAllocatedBytesForCurrentThread();
        var text = JsonSerializer.Serialize(model);
        var stringAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeString;

        Assert.True(json > 4_000_000, $"the model's JSON is {json:N0} bytes");
        Assert.True(allocated < json / 4, $"{allocated:N0} bytes allocated writing {json:N0} bytes of JSON");
        Assert.True(stringAllocated > json * 2, $"the control allocated {stringAllocated:N0} bytes for {text.Length:N0} characters");
    }

    private string Run(string name, bool compress, bool mergeable = false, bool compressPayloads = true, string? testId = null, bool isolate = true)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var scope = isolate ? RequestResponseLogger.IsolateForTests() : null;
        try
        {
            var id = testId ?? LogCalls();
            ReportGenerator.CreateStandardReportsWithDiagrams(Features(id), new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc), new ReportConfigurationOptions
                {
                    ReportsFolderPath = directory,
                    InternalFlowTracking = false,
                    GenerateMergeableData = mergeable,
                    GenerateTestRunReportSchema = true,
                    CompressTestRunReportPayloads = compressPayloads,
                    CompressTestRunReport = compress,
                });
        }
        finally
        {
            scope?.Dispose();
        }
        return directory;
    }

    /// <summary>Calls for the scenario, one of them with a body large enough to be stored compressed in the JSON file.</summary>
    private static string LogCalls()
    {
        var testId = "krkl-" + Guid.NewGuid().ToString("N");
        var large = LargeBody + string.Concat(Enumerable.Range(0, 600).Select(i => $"{{\"line\":{i},\"text\":\"a captured line\"}}"));
        RequestResponseLogger.LogPair("Place order", testId, HttpMethod.Post, new Uri("http://orders/api/orders"), "OrdersApi", "Test",
            requestContent: "{\"item\":\"widget\",\"note\":null}", responseContent: large, statusCode: HttpStatusCode.Created);
        RequestResponseLogger.LogPair("Place order", testId, HttpMethod.Get, new Uri("http://stock/api/stock/W-1"), "StockApi", "OrdersApi",
            responseContent: "{\"inStock\":true}", statusCode: HttpStatusCode.OK);
        return testId;
    }

    private static Feature[] Features(string testId) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = testId, DisplayName = "Place order", Result = ExecutionResult.Failed, ErrorMessage = "Expected 1 row but found 0",
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "the order is placed", Status = ExecutionResult.Passed,
                            SubSteps = [new ScenarioStep { Keyword = "And", Text = "a widget is chosen", Status = ExecutionResult.Passed }]
                        },
                        new ScenarioStep { Keyword = "Then", Text = "the order is saved", Status = ExecutionResult.Failed, FailureMessage = "Expected 1 row but found 0" }
                    ]
                },
                new Scenario { Id = testId + "-other", DisplayName = "Nothing called", Result = ExecutionResult.Passed }
            ]
        }
    ];

    internal static byte[] Decode(byte[] file)
    {
        using var content = ReportFileFormat.Open(new MemoryStream(file));
        using var decoded = new MemoryStream();
        content.Stream.CopyTo(decoded);
        return decoded.ToArray();
    }
}

/// <summary>
/// The rule of section 4.2 on a parsed document, written apart from the product's streaming filter: a key the schema
/// requires where it stands is kept, an object the schema leaves undescribed is kept as written, and every other key whose
/// value is null, or an array or object empty once its own keys are pruned, goes.
/// </summary>
internal static class SchemaWalk
{
    /// <summary>Where two documents first differ, with both values, for a failure message that names the place.</summary>
    public static string FirstDifference(JsonNode? expected, JsonNode? actual, string path)
    {
        switch (expected, actual)
        {
            case (JsonObject e, JsonObject a):
                foreach (var (key, value) in e)
                {
                    if (!a.ContainsKey(key))
                        return $"{path}.{key}: expected {Show(value)}, missing in the .krkl";
                    if (!JsonNode.DeepEquals(value, a[key]))
                        return FirstDifference(value, a[key], $"{path}.{key}");
                }
                foreach (var (key, value) in a)
                {
                    if (!e.ContainsKey(key))
                        return $"{path}.{key}: not expected, the .krkl holds {Show(value)}";
                }
                return $"{path}: key order";
            case (JsonArray e, JsonArray a):
                for (var i = 0; i < Math.Min(e.Count, a.Count); i++)
                {
                    if (!JsonNode.DeepEquals(e[i], a[i]))
                        return FirstDifference(e[i], a[i], $"{path}[{i}]");
                }
                return $"{path}: {e.Count} elements expected, {a.Count} in the .krkl";
            default:
                return $"{path}: expected {Show(expected)}, the .krkl holds {Show(actual)}";
        }

        static string Show(JsonNode? node)
        {
            var text = node?.ToJsonString() ?? "null";
            return text.Length > 160 ? text[..160] + "..." : text;
        }
    }

    public static JsonNode Prune(JsonNode root, JsonElement schema)
    {
        var defs = Definitions(schema);
        Walk(root, schema, defs, prune: true, path: "$", null, null);
        return root;
    }

    public static (List<string> Missing, List<string> Empty) Check(JsonNode root, JsonElement schema)
    {
        var missing = new List<string>();
        var empty = new List<string>();
        Walk(root, schema, Definitions(schema), prune: false, path: "$", missing, empty);
        return (missing, empty);
    }

    private static JsonElement Definitions(JsonElement schema) =>
        schema.TryGetProperty("$defs", out var defs) ? defs : schema.TryGetProperty("definitions", out var definitions) ? definitions : default;

    private static void Walk(JsonNode? node, JsonElement? schemaNode, JsonElement defs, bool prune, string path, List<string>? missing, List<string>? empty)
    {
        var resolved = Resolve(schemaNode, defs);
        if (resolved is { } open && IsOpen(open))
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var key in Required(resolved, defs))
                {
                    if (!obj.ContainsKey(key))
                        missing?.Add($"{path}.{key}");
                }
                foreach (var (key, value) in obj.ToList())
                {
                    Walk(value, Child(resolved, key, defs), defs, prune, $"{path}.{key}", missing, empty);
                    var nothing = value is null or JsonArray { Count: 0 } or JsonObject { Count: 0 };
                    if (!nothing || Required(resolved, defs).Contains(key))
                        continue;
                    if (prune)
                        obj.Remove(key);
                    else
                        empty?.Add($"{path}.{key}");
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Walk(array[i], Items(resolved, defs), defs, prune, $"{path}[{i}]", missing, empty);
                break;
        }
    }

    private static JsonElement? Resolve(JsonElement? node, JsonElement defs)
    {
        if (node is { ValueKind: JsonValueKind.Object } n && n.TryGetProperty("$ref", out var reference))
            return Resolve(defs.GetProperty(reference.GetString()!.Split('/')[^1]), defs);
        return node;
    }

    private static bool IsOpen(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty("type", out var type)
        && (type.ValueKind == JsonValueKind.String ? type.GetString() == "object" : type.EnumerateArray().Any(t => t.GetString() == "object"))
        && !new[] { "properties", "additionalProperties", "patternProperties", "items", "$ref", "oneOf", "anyOf", "allOf" }.Any(k => node.TryGetProperty(k, out _));

    private static IEnumerable<JsonElement> Branches(JsonElement? node, JsonElement defs)
    {
        if (Resolve(node, defs) is not { ValueKind: JsonValueKind.Object } n)
            yield break;
        yield return n;
        foreach (var combinator in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (n.TryGetProperty(combinator, out var branches))
            {
                foreach (var branch in branches.EnumerateArray())
                {
                    foreach (var inner in Branches(branch, defs))
                        yield return inner;
                }
            }
        }
    }

    private static HashSet<string> Required(JsonElement? node, JsonElement defs) =>
        [.. Branches(node, defs).Where(b => b.TryGetProperty("required", out _)).SelectMany(b => b.GetProperty("required").EnumerateArray().Select(r => r.GetString()!))];

    private static JsonElement? Child(JsonElement? node, string key, JsonElement defs)
    {
        foreach (var branch in Branches(node, defs))
        {
            if (branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty(key, out var property))
                return property;
        }
        foreach (var branch in Branches(node, defs))
        {
            if (branch.TryGetProperty("additionalProperties", out var additional))
                return additional.ValueKind == JsonValueKind.Object ? additional
                    : additional.ValueKind == JsonValueKind.True ? JsonDocument.Parse("""{"type":"object"}""").RootElement : null;
        }
        return null;
    }

    private static JsonElement? Items(JsonElement? node, JsonElement defs)
    {
        foreach (var branch in Branches(node, defs))
        {
            if (branch.TryGetProperty("items", out var items))
                return items;
        }
        return null;
    }
}
