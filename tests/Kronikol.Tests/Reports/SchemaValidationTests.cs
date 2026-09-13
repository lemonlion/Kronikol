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
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = false
        });

        var errors = new List<string>();
        Collect(result, errors);
        return errors;
    }

    internal static void Collect(EvaluationResults results, List<string> errors)
    {
        if (results is { IsValid: false, Errors: { } found })
            foreach (var (keyword, message) in found)
                errors.Add($"{results.InstanceLocation} [{keyword}] {message}");

        if (results.Details is { } details)
            foreach (var detail in details)
                Collect(detail, errors);
    }
}
