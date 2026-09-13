using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Kronikol.Reports;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A YAML run writes a schema beside its data file, and that schema has to describe the file it sits
/// next to. <see cref="ReportGenerator.GenerateTestRunReportSchema"/> hands YAML the JSON document —
/// which is right in kind, since JSON Schema is how YAML is described everywhere else — but the two
/// writers do not name their keys the same way, so what shipped described a file that was never written.
///
/// <para>JSON emits camelCase; the YAML writer emits PascalCase. Nothing matched, and because a JSON
/// Schema with no <c>additionalProperties</c> silently permits any key it has not heard of, the failure
/// did not read as "every property is wrong" — it read as four missing required keys, which looks like a
/// small problem.</para>
/// </summary>
public class YamlSchemaValidationTests
{
    [Fact]
    public void Yaml_report_validates_against_the_schema_generated_for_Yaml()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportPath = ReportGenerator.GenerateTestRunReportData(
            TestRunReportSchemaContractTests.RichFeatures(),
            TestRunReportSchemaContractTests.Start,
            TestRunReportSchemaContractTests.End,
            $"YamlSchemaValid_{suffix}.yml", DataFormat.Yaml,
            TestRunReportSchemaContractTests.Diagrams(),
            TestRunReportSchemaContractTests.Logs(),
            TestRunReportSchemaContractTests.Diagnostics());
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"YamlSchemaValid_{suffix}.schema.json", DataFormat.Yaml);

        var errors = ValidateYaml(schemaPath, reportPath);

        Assert.True(errors.Count == 0,
            $"{errors.Count} schema violations in a YAML report this build just wrote:\n  "
            + string.Join("\n  ", errors.Take(25))
            + (errors.Count > 25 ? $"\n  ... and {errors.Count - 25} more" : ""));
    }

    internal static List<string> ValidateYaml(string schemaPath, string reportPath)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        var instance = YamlToJson(File.ReadAllText(reportPath));

        using var document = JsonDocument.Parse(instance.ToJsonString());
        var result = schema.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = false
        });

        var errors = new List<string>();
        SchemaValidationTests.Collect(result, errors);
        return errors;
    }

    /// <summary>
    /// Reads YAML into the JSON node model, resolving scalars the way the YAML 1.2 core schema does.
    /// </summary>
    /// <remarks>
    /// <para>YamlDotNet's object deserializer hands back every scalar as a <c>string</c>, so validating
    /// its output against a schema would fail on <c>formatVersion: integer</c> and
    /// <c>isHappyPath: boolean</c> for reasons that belong to the parser rather than to Kronikol. The
    /// resolution has to be done here, and it has to be done on the rule that matters: only a
    /// <see cref="ScalarStyle.Plain"/> scalar is resolved. A quoted or block scalar is a string whatever
    /// it spells, which is the distinction that lets a report carry a feature literally named
    /// <c>123</c>.</para>
    ///
    /// <para>Kronikol's YAML writer emits every scalar plain, so nothing in a report on disk is currently
    /// protected by that distinction — but the converter must still honour it, or this test would pass
    /// for the wrong reason once the writer starts quoting.</para>
    /// </remarks>
    internal static JsonNode YamlToJson(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count == 0 ? JsonValue.Create((string?)null)! : Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode Convert(YamlNode node) => node switch
    {
        YamlMappingNode map => MapOf(map),
        YamlSequenceNode seq => new JsonArray(seq.Children.Select(Convert).ToArray()),
        YamlScalarNode scalar => Scalar(scalar)!,
        _ => throw new NotSupportedException($"Unsupported YAML node {node.GetType().Name}")
    };

    private static JsonObject MapOf(YamlMappingNode map)
    {
        var result = new JsonObject();
        foreach (var (key, value) in map.Children)
            result[((YamlScalarNode)key).Value ?? ""] = Convert(value);
        return result;
    }

    private static JsonNode? Scalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? "";

        // Quoted and block scalars are strings by construction, whatever they spell.
        if (scalar.Style != ScalarStyle.Plain)
            return JsonValue.Create(text);

        if (text.Length == 0 || text is "~" or "null" or "Null" or "NULL")
            return null;
        if (text is "true" or "True" or "TRUE")
            return JsonValue.Create(true);
        if (text is "false" or "False" or "FALSE")
            return JsonValue.Create(false);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);

        return JsonValue.Create(text);
    }
}
