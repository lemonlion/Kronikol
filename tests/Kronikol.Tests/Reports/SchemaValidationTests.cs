using System.Text.Json;
using Json.Schema;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The schema beside the report has to be the contract the report actually satisfies. Its sibling suite,
/// <see cref="TestRunReportSchemaContractTests"/>, walks emitted keys against declared ones by hand; this
/// one hands both files to a conformant draft 2020-12 validator and asserts it finds nothing, which is the
/// only check that covers the keywords a hand-rolled walker never evaluates.
///
/// <para>The defect it was written against: the generator expressed "may be null" with OpenAPI's
/// <c>nullable: true</c> inside a document declaring <c>$schema: .../draft/2020-12/schema</c>. JSON Schema
/// has no <c>nullable</c> keyword — 2020-12 tolerates unknown keywords as annotations, so it is not an
/// error, it is silently inert, and the <c>"type": "string"</c> beside it then rejects every null the
/// writer emits. Measured across the 17 report/schema pairs on disk: 17 fail, 5,464 errors, every one of
/// them keyword <c>type</c> against instance <c>null</c>.</para>
/// </summary>
public class SchemaValidationTests
{
    [Fact]
    public void A_generated_report_validates_against_its_own_schema()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportPath = ReportGenerator.GenerateTestRunReportData(
            TestRunReportSchemaContractTests.RichFeatures(),
            TestRunReportSchemaContractTests.Start,
            TestRunReportSchemaContractTests.End,
            $"SchemaValid_{suffix}.json", DataFormat.Json,
            TestRunReportSchemaContractTests.Diagrams(),
            TestRunReportSchemaContractTests.Logs(),
            TestRunReportSchemaContractTests.Diagnostics());
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"SchemaValid_{suffix}.schema.json", DataFormat.Json);

        var errors = Validate(schemaPath, reportPath);

        Assert.True(errors.Count == 0,
            $"{errors.Count} schema violations in a report this build just wrote:\n  "
            + string.Join("\n  ", errors.Take(25))
            + (errors.Count > 25 ? $"\n  ... and {errors.Count - 25} more" : ""));
    }

    /// <summary>
    /// A report written with <c>CompressTestRunReportPayloads</c> holds both forms of a payload, a string below
    /// <see cref="ReportPayloads.Threshold"/> characters and a <c>compressedPayload</c> object from there on (#85), and
    /// validates as it stands.
    /// </summary>
    [Fact]
    public void A_report_with_compressed_payloads_validates_against_its_own_schema()
    {
        var (reportPath, schemaPath) = WriteCompressed();

        var errors = Validate(schemaPath, reportPath);

        Assert.Contains("\"$z\"", File.ReadAllText(reportPath), StringComparison.Ordinal);
        Assert.True(errors.Count == 0, $"{errors.Count} schema violations:\n  " + string.Join("\n  ", errors.Take(25)));
    }

    /// <summary>A wrapper that lost its text is not a payload, and the schema says so.</summary>
    [Fact]
    public void A_compressed_payload_without_its_text_fails_the_schema()
    {
        var (reportPath, schemaPath) = WriteCompressed();
        var report = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(reportPath))!;
        var content = report["features"]![0]!["scenarios"]![0]!["httpInteractions"]![0]!["content"]!.AsObject();
        content.Remove("$z");
        File.WriteAllText(reportPath, report.ToJsonString());

        Assert.NotEmpty(Validate(schemaPath, reportPath));
    }

    private static (string Report, string Schema) WriteCompressed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var big = "@startuml\n" + string.Concat(Enumerable.Range(0, ReportPayloads.Threshold / 10).Select(i => $"A -> B : call {i}\n")) + "@enduml";
        var reportPath = ReportGenerator.GenerateTestRunReportData(
            PayloadCompressionTests.Features(), PayloadCompressionTests.Start, PayloadCompressionTests.End,
            $"SchemaCompressed_{suffix}.json", DataFormat.Json, PayloadCompressionTests.Diagrams([big, "@startuml\nA -> B\n@enduml"]),
            PayloadCompressionTests.Logs(PayloadCompressionTests.Json(ReportPayloads.Threshold + 2000), PayloadCompressionTests.Json(40)),
            diagnostics: null, fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null,
            compressPayloads: true);
        return (reportPath, ReportGenerator.GenerateTestRunReportSchema($"SchemaCompressed_{suffix}.schema.json", DataFormat.Json));
    }

    /// <summary>
    /// The mechanism, pinned on its own so a regression says which of the two halves broke. `nullable` is
    /// not a 2020-12 keyword; a validator ignores it and enforces the `type` beside it.
    /// </summary>
    [Fact]
    public void OpenApi_nullable_does_not_admit_a_null_under_2020_12()
    {
        var openApi = JsonSchema.FromText("""
            { "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": { "name": { "type": "string", "nullable": true } } }
            """);
        var union = JsonSchema.FromText("""
            { "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": { "name": { "type": ["string", "null"] } } }
            """);

        using var instance = JsonDocument.Parse("""{ "name": null }""");

        Assert.False(openApi.Evaluate(instance.RootElement).IsValid, "`nullable` was honoured — it is not a 2020-12 keyword and must not be");
        Assert.True(union.Evaluate(instance.RootElement).IsValid, "the 2020-12 type-union form was rejected");
    }

    /// <summary>
    /// `enum` is the one assertion that is type-blind, so widening `type` alone does not admit a null: the
    /// enum array itself has to carry it. This is the single node in the schema that needs more than the
    /// mechanical rewrite, and the test exists so that stays true rather than being rediscovered.
    /// </summary>
    [Fact]
    public void A_nullable_enum_needs_null_in_the_enum_not_just_in_the_type()
    {
        using var instance = JsonDocument.Parse("null");

        var typeOnly = JsonSchema.FromText("""
            { "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": ["string", "null"], "enum": ["Passed", "Failed"] }
            """);
        var enumToo = JsonSchema.FromText("""
            { "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": ["string", "null"], "enum": ["Passed", "Failed", null] }
            """);

        Assert.False(typeOnly.Evaluate(instance.RootElement).IsValid, "a type-union alone admitted a null past an enum");
        Assert.True(enumToo.Evaluate(instance.RootElement).IsValid);
    }

    internal static List<string> Validate(string schemaPath, string reportPath)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        using var instance = JsonDocument.Parse(File.ReadAllText(reportPath));

        var result = schema.Evaluate(instance.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Hierarchical,
            RequireFormatValidation = false
        });

        var errors = new List<string>();
        Collect(result, errors);
        return errors;
    }

    /// <summary>
    /// The errors of a hierarchical evaluation, read only below the nodes that failed. A node that passed is
    /// skipped whole: a <c>oneOf</c> the instance satisfies keeps the branch it did not take as a failed child, and
    /// reading that child as an error reported every payload under the compressed-payload union (#85) as a
    /// violation. (The flat list format loses which failures sit under a passing node, so it cannot be used.)
    /// </summary>
    internal static void Collect(EvaluationResults results, List<string> errors)
    {
        if (results.IsValid)
            return;

        if (results.Errors is { } found)
            foreach (var (keyword, message) in found)
                errors.Add($"{results.InstanceLocation} [{keyword}] {message}");

        if (results.Details is { } details)
            foreach (var detail in details)
                Collect(detail, errors);
    }
}
