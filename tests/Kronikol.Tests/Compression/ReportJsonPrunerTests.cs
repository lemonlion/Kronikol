using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Compression;

namespace Kronikol.Tests.Compression;

/// <summary>
/// The JSON a compressed report holds (plans/REPORT_FORMAT_PLAN.md 4.2): compact, and without a key whose value is null,
/// an empty array or an empty object, unless the report's schema requires that key where it stands. Array elements are
/// kept whatever they hold, so no index moves, and every value that is written is written as the serializer wrote it.
/// </summary>
public class ReportJsonPrunerTests
{
    /// <summary>A schema that describes nothing and requires nothing: every empty key goes.</summary>
    private const string NoSchema = "{}";

    /// <summary>A schema in the shape the report's generator writes: definitions, references, a recursive step, a map.</summary>
    private const string Schema = """
        {
          "type": "object",
          "required": ["formatVersion", "features", "background"],
          "properties": {
            "formatVersion": { "type": "integer" },
            "features": { "type": "array", "items": { "$ref": "#/definitions/feature" } },
            "background": { "type": ["object", "null"], "required": ["calls"], "properties": { "calls": { "type": "array" } } },
            "statuses": { "type": "object", "additionalProperties": { "$ref": "#/definitions/status" } }
          },
          "definitions": {
            "feature": {
              "type": "object",
              "required": ["name", "labels", "scenarios"],
              "properties": {
                "name": { "type": ["string", "null"] },
                "labels": { "type": "array" },
                "scenarios": { "type": "array", "items": { "$ref": "#/definitions/scenario" } }
              }
            },
            "scenario": {
              "type": "object",
              "required": ["id", "steps"],
              "properties": {
                "id": { "type": "string" },
                "labels": { "type": "array" },
                "steps": { "type": "array", "items": { "$ref": "#/definitions/step" } }
              }
            },
            "step": {
              "type": "object",
              "required": ["text", "parameters"],
              "properties": {
                "text": { "type": ["string", "null"] },
                "parameters": { "type": "array" },
                "subSteps": { "type": "array", "items": { "$ref": "#/definitions/step" } }
              }
            },
            "status": { "type": "object", "required": ["count"], "properties": { "count": { "type": "integer" } } }
          }
        }
        """;

    [Fact]
    public void Keys_holding_null_or_an_empty_array_or_object_are_left_out_at_every_depth()
    {
        const string json = """
            {
              "a": null, "b": [], "c": {}, "d": 1,
              "e": { "f": null, "g": [ ], "h": { }, "i": "kept" },
              "j": [ { "k": null, "l": 2 } ]
            }
            """;

        Assert.Equal("""{"d":1,"e":{"i":"kept"},"j":[{"l":2}]}""", Prune(json, NoSchema));
    }

    [Fact]
    public void Values_that_are_not_empty_are_written_as_the_serializer_wrote_them()
    {
        const string json = """{"zero":0,"no":false,"blank":"","price":2.50,"big":1e3,"quote":"a\"b","é":"é\u0001","line":"a\nb","array":[1,2.50,true,false,null,"x"]}""";

        Assert.Equal(json, Prune(json, NoSchema));
    }

    [Fact]
    public void A_string_the_reader_cannot_turn_into_text_passes_through_as_it_came()
    {
        // A lone surrogate, which a captured body can hold: valid as JSON, not as .NET text.
        const string json = """{"body":"\uD800 half","empty":null}""";

        Assert.Equal("""{"body":"\uD800 half"}""", Prune(json, NoSchema));
    }

    [Fact]
    public void An_escaped_key_is_matched_unescaped_and_written_as_the_writer_writes_it()
    {
        const string json = """{"formatVersion":3,"features":[{"name":"f","lab\u0065ls":[],"scenarios":[]}],"background":null,"a\"b":1,"c\u0001":2,"gone\u0021":null}""";

        Assert.Equal("""{"formatVersion":3,"features":[{"name":"f","labels":[],"scenarios":[]}],"background":null,"a\"b":1,"c\u0001":2}""", Prune(json, Schema));
    }

    [Fact]
    public void A_part_the_schema_leaves_open_is_kept_as_written()
    {
        // As internalFlowSegments: rendering data a merge carries through unchanged, deliberately not described.
        const string schema = """{"type":"object","properties":{"open":{"type":"object","description":"not described"},"closed":{"type":"object","properties":{"x":{"type":"string"}}},"loose":{"type":"object","properties":{"x":{"type":"string"}},"additionalProperties":true}}}""";
        const string json = """{"open":{"a":{"x":null,"y":[],"z":{}},"b":{},"c":[null,{}]},"closed":{"x":null,"y":[]},"loose":{"x":null,"other":{"y":null}},"gone":{"open":{}}}""";

        Assert.Equal("""{"open":{"a":{"x":null,"y":[],"z":{}},"b":{},"c":[null,{}]},"loose":{"other":{"y":null}}}""", Prune(json, schema));
    }

    [Fact]
    public void An_open_part_that_holds_nothing_is_left_out_like_any_other_key()
    {
        Assert.Equal("{}", Prune("""{"open":{}}""", """{"type":"object","properties":{"open":{"type":"object"}}}"""));
    }

    [Fact]
    public void An_object_or_array_that_pruning_leaves_empty_is_left_out_too()
    {
        const string json = """{"a":{"b":null,"c":[],"d":{"e":{}}},"f":[],"g":{"h":{"i":null}},"keep":{"j":{"k":[{}]}}}""";

        Assert.Equal("""{"keep":{"j":{"k":[{}]}}}""", Prune(json, NoSchema));
    }

    [Fact]
    public void Array_elements_are_kept_whatever_they_hold()
    {
        const string json = """{"list":[null,[],{},{"x":null},[null],[[]]],"top":[[],{}]}""";

        Assert.Equal("""{"list":[null,[],{},{},[null],[[]]],"top":[[],{}]}""", Prune(json, NoSchema));
    }

    [Fact]
    public void A_root_that_is_not_an_object_passes_through()
    {
        Assert.Equal("[null,{},[]]", Prune("[ null, { }, [ ] ]", NoSchema));
        Assert.Equal("{}", Prune("""{"a":null}""", NoSchema));
    }

    [Fact]
    public void A_key_the_schema_requires_where_it_stands_is_written_even_when_empty()
    {
        const string json = """
            {
              "formatVersion": 3, "features": [], "background": null, "statuses": {}, "other": null
            }
            """;

        Assert.Equal("""{"formatVersion":3,"features":[],"background":null}""", Prune(json, Schema));
    }

    [Fact]
    public void Requirements_follow_references_arrays_maps_and_recursion()
    {
        const string json = """
            {
              "formatVersion": 3,
              "features": [
                {
                  "name": null, "labels": [], "description": null,
                  "scenarios": [
                    {
                      "id": "s", "labels": [], "notes": [],
                      "steps": [
                        { "text": null, "parameters": [], "subSteps": [
                          { "text": "inner", "parameters": [], "subSteps": [], "comment": null }
                        ] }
                      ]
                    }
                  ]
                }
              ],
              "background": { "calls": [], "extra": [] },
              "statuses": { "Passed": { "count": 1, "names": [] }, "Failed": { "count": null } }
            }
            """;

        Assert.Equal(
            """{"formatVersion":3,"features":[{"name":null,"labels":[],"scenarios":[{"id":"s","steps":[{"text":null,"parameters":[],"subSteps":[{"text":"inner","parameters":[]}]}]}]}],"background":{"calls":[]},"statuses":{"Passed":{"count":1},"Failed":{"count":null}}}""",
            Prune(json, Schema));
    }

    [Fact]
    public void A_key_required_in_one_object_is_left_out_of_another_that_does_not_require_it()
    {
        // labels: required on a feature, optional on a scenario.
        const string json = """{"formatVersion":3,"features":[{"name":"f","labels":[],"scenarios":[{"id":"s","labels":[],"steps":[]}]}],"background":null}""";

        Assert.Equal(
            """{"formatVersion":3,"features":[{"name":"f","labels":[],"scenarios":[{"id":"s","steps":[]}]}],"background":null}""",
            Prune(json, Schema));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(4096)]
    public void Any_split_of_the_input_gives_the_same_output(int chunk)
    {
        var json = Encoding.UTF8.GetBytes(Document(seed: 3, scenarios: 40));
        var whole = Prune(json, Schema, json.Length);

        Assert.Equal(whole, Prune(json, Schema, chunk));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void The_output_is_a_bottom_up_pruning_of_the_parsed_document(int seed)
    {
        var json = Document(seed, scenarios: 60);

        var expected = Reference(JsonNode.Parse(json)!, JsonDocument.Parse(Schema).RootElement);

        Assert.Equal(expected, Prune(json, Schema));
    }

    [Fact]
    public void A_value_larger_than_the_window_is_carried_whole()
    {
        var body = new string('x', 3 << 20) + "é\"";
        var json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["empty"] = null, ["body"] = body },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var pruned = Prune(json, NoSchema);

        Assert.Equal(body, JsonNode.Parse(pruned)!["body"]!.GetValue<string>());
        Assert.DoesNotContain("empty", pruned, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"a":1""")]
    [InlineData("""{"a":[1,2""")]
    [InlineData("""{"a":""")]
    [InlineData("""{"a":1}}""")]
    public void JSON_that_is_not_whole_is_an_error(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Prune(json, NoSchema));
    }

    [Fact]
    public void The_schema_s_own_required_lists_are_read_with_definitions_or_defs()
    {
        var defs = Schema.Replace("\"definitions\"", "\"$defs\"", StringComparison.Ordinal).Replace("#/definitions/", "#/$defs/", StringComparison.Ordinal);
        const string json = """{"formatVersion":3,"features":[{"name":null,"labels":[],"scenarios":[]}],"background":null}""";

        Assert.Equal(Prune(json, Schema), Prune(json, defs));
        Assert.Equal(json, Prune(json, defs));
    }

    private static string Prune(string json, string schema) => Prune(Encoding.UTF8.GetBytes(json), schema, int.MaxValue);

    private static string Prune(byte[] json, string schema, int chunk)
    {
        using var output = new MemoryStream();
        using (var pruner = new ReportJsonPruner(output, RequiredKeys.FromSchema(JsonDocument.Parse(schema).RootElement)))
        {
            for (var at = 0; at < json.Length; at += chunk)
                pruner.Write(json, at, Math.Min(chunk, json.Length - at));
            pruner.Complete();
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>A report-shaped document with every kind of empty value in every place, the same for one seed.</summary>
    private static string Document(int seed, int scenarios)
    {
        var random = new Random(seed);
        object? Maybe(object? value) => random.Next(4) switch { 0 => null, 1 => new List<object?>(), 2 => new Dictionary<string, object?>(), _ => value };
        Dictionary<string, object?> Step(int depth) => new()
        {
            ["text"] = random.Next(3) == 0 ? null : $"step {random.Next(1000)}",
            ["parameters"] = random.Next(2) == 0 ? new List<object?>() : new List<object?> { new Dictionary<string, object?> { ["name"] = Maybe("p"), ["value"] = Maybe(1) } },
            ["subSteps"] = depth < 3 && random.Next(2) == 0 ? Enumerable.Range(0, random.Next(3)).Select(_ => (object?)Step(depth + 1)).ToList() : Maybe(null),
            ["comment"] = Maybe("c"),
        };
        var root = new Dictionary<string, object?>
        {
            ["formatVersion"] = 3,
            ["features"] = Enumerable.Range(0, 3).Select(f => (object?)new Dictionary<string, object?>
            {
                ["name"] = Maybe($"feature {f}"),
                ["labels"] = Maybe(new[] { "a" }),
                ["scenarios"] = Enumerable.Range(0, scenarios / 3).Select(s => (object?)new Dictionary<string, object?>
                {
                    ["id"] = $"s{f}.{s}",
                    ["labels"] = Maybe(new[] { "b" }),
                    ["steps"] = Enumerable.Range(0, random.Next(4)).Select(_ => (object?)Step(0)).ToList(),
                    ["notes"] = new object?[] { null, new Dictionary<string, object?>(), Maybe("n") },
                    ["body"] = Maybe("{\"price\":2.50,\"name\":\"é\"}"),
                }).ToList(),
            }).ToList(),
            ["background"] = Maybe(new Dictionary<string, object?> { ["calls"] = Maybe(new[] { 1 }) }),
            ["statuses"] = Maybe(new Dictionary<string, object?> { ["Passed"] = new Dictionary<string, object?> { ["count"] = Maybe(2) } }),
        };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = random.Next(2) == 0, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>
    /// The rule on a parsed document, written apart from the product: bottom up, a key whose value is null, or an array or
    /// object empty once its own keys are pruned, goes, unless the schema node it stands in requires it.
    /// </summary>
    private static string Reference(JsonNode root, JsonElement schema)
    {
        var definitions = schema.TryGetProperty("definitions", out var d) ? d : schema.TryGetProperty("$defs", out var defs) ? defs : default;

        JsonElement? Resolve(JsonElement? node)
        {
            if (node is { } n && n.ValueKind == JsonValueKind.Object && n.TryGetProperty("$ref", out var reference))
                return definitions.GetProperty(reference.GetString()!.Split('/')[^1]);
            return node;
        }

        JsonElement? Child(JsonElement? node, string key)
        {
            if (Resolve(node) is not { ValueKind: JsonValueKind.Object } n)
                return null;
            if (n.TryGetProperty("properties", out var properties) && properties.TryGetProperty(key, out var p))
                return Resolve(p);
            return n.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object ? Resolve(additional) : null;
        }

        JsonElement? Items(JsonElement? node) =>
            Resolve(node) is { ValueKind: JsonValueKind.Object } n && n.TryGetProperty("items", out var items) ? Resolve(items) : null;

        bool Requires(JsonElement? node, string key) =>
            Resolve(node) is { ValueKind: JsonValueKind.Object } n && n.TryGetProperty("required", out var required)
            && required.EnumerateArray().Any(r => r.GetString() == key);

        void Walk(JsonNode? node, JsonElement? schemaNode)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj.ToList())
                    {
                        Walk(value, Child(schemaNode, key));
                        if (!Requires(schemaNode, key) && value is null or JsonArray { Count: 0 } or JsonObject { Count: 0 })
                            obj.Remove(key);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                        Walk(item, Items(schemaNode));
                    break;
            }
        }

        Walk(root, schema);
        return root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
