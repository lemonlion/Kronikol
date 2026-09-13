using System.Text.Json.Nodes;
using System.Net;
using System.Text.Json;
using Json.Schema;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A JSON Schema that does not say <c>additionalProperties: false</c> permits every key it has not
/// heard of. Nothing in the generated schema said it — not one node in 5,600 lines — so the schema
/// could not detect the one thing it exists to detect: a writer emitting a field nobody declared.
///
/// <para>That is why <see cref="TestRunReportSchemaContractTests"/> exists at all. It re-implements, by
/// hand, the check the schema should be making, and it is load-bearing rather than belt-and-braces:
/// without it an undeclared key reached consumers with no signal anywhere. Closing the schema is what
/// makes the walker a second opinion instead of the only one — and it also hands the check to every
/// consumer downstream, who have a validator and do not have the walker.</para>
///
/// <para>Two nodes stay open, deliberately. <c>internalFlowSegments</c> is passed through a merge
/// verbatim from shard files that a different Kronikol version may have written
/// (<c>MergeableReportRenderer</c> re-serialises the parsed <c>JsonElement</c>), so pinning its values
/// would make <c>kronikol merge</c> produce a file that fails its own schema with no code change on
/// either side. <c>exampleValues</c> is a caller-supplied map of parameter names. Both are declared as
/// objects; what is inside them is not this schema's to fix.</para>
/// </summary>
public class SchemaClosedContractTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-closed-" + Guid.NewGuid().ToString("N"));

    public SchemaClosedContractTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The mergeable file is written under the same name as the standard one, with the same schema
    /// beside it, and carries five root keys the standard file does not. Closing the root without
    /// declaring them would make a merged report fail the schema shipped next to it.
    /// </summary>
    /// <remarks>
    /// Driven through <see cref="ReportGenerator.CreateStandardReportsWithDiagrams"/> rather than the
    /// writer beneath it, because that is the only path on which the internal-flow segments and the
    /// whole-test-flow fragments are actually built — and those are the two shapes this is riskiest at.
    /// </remarks>
    [Fact]
    public void A_mergeable_report_validates_against_the_schema_written_beside_it()
    {
        var testId = "closed-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Place order", testId, HttpMethod.Post,
            new Uri("http://orders/api/orders"), "OrdersApi", "Test", statusCode: HttpStatusCode.Created);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            OneFeature(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, MergeableOptions(_dir));

        var errors = SchemaValidationTests.Validate(
            Path.Combine(_dir, "TestRunReport.schema.json"),
            Path.Combine(_dir, "TestRunReport.json"));

        Assert.True(errors.Count == 0,
            $"{errors.Count} schema violations in a mergeable report this build just wrote:\n  "
            + string.Join("\n  ", errors.Take(25)));
    }

    /// <summary>
    /// The guard, proved rather than asserted: a key nobody declared has to be rejected, or closing the
    /// schema changed nothing. Both the root and a nested node, because <c>additionalProperties</c> is
    /// per-node and a root-only close would leave every scenario and step wide open.
    /// </summary>
    [Theory]
    [InlineData("$", "somethingNobodyDeclared")]
    [InlineData("$.features[0]", "featureExtra")]
    [InlineData("$.features[0].scenarios[0]", "scenarioExtra")]
    [InlineData("$.features[0].scenarios[0].steps[0]", "stepExtra")]
    [InlineData("$.ciMetadata", "ciExtra")]
    [InlineData("$.environment", "environmentExtra")]
    public void An_undeclared_key_is_a_violation(string where, string key)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportPath = ReportGenerator.GenerateTestRunReportData(
            TestRunReportSchemaContractTests.RichFeatures(),
            TestRunReportSchemaContractTests.Start,
            TestRunReportSchemaContractTests.End,
            $"Closed_{suffix}.json", DataFormat.Json,
            TestRunReportSchemaContractTests.Diagrams(),
            TestRunReportSchemaContractTests.Logs(),
            TestRunReportSchemaContractTests.Diagnostics());
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"Closed_{suffix}.schema.json", DataFormat.Json);

        // Unmodified it validates; that is the control, and without it a test that only checks the
        // mutation fails cannot tell "the key was rejected" from "the file was broken all along".
        Assert.Empty(SchemaValidationTests.Validate(schemaPath, reportPath));

        var mutated = Path.Combine(_dir, $"Mutated_{suffix}.json");
        File.WriteAllText(mutated, AddKey(File.ReadAllText(reportPath), where, key));

        Assert.NotEmpty(SchemaValidationTests.Validate(schemaPath, mutated));
    }

    /// <summary>
    /// The two nodes left open stay open. A merged report carries its shards' internal-flow payloads
    /// verbatim, so a shape this build has never seen has to pass.
    /// </summary>
    [Fact]
    public void A_shape_this_build_has_never_seen_still_passes_inside_internalFlowSegments()
    {
        var testId = "open-" + Guid.NewGuid().ToString("N");
        ReportGenerator.CreateStandardReportsWithDiagrams(
            OneFeature(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, MergeableOptions(_dir));

        var reportPath = Path.Combine(_dir, "TestRunReport.json");
        var root = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        root["internalFlowSegments"] = new JsonObject
        {
            ["seg-from-a-future-version"] = new JsonObject
            {
                ["shapeNobodyHere"] = "knows about",
                ["nested"] = new JsonArray(1, 2, 3)
            }
        };

        var mutated = Path.Combine(_dir, "FutureSegments.json");
        File.WriteAllText(mutated, root.ToJsonString());

        Assert.Empty(SchemaValidationTests.Validate(Path.Combine(_dir, "TestRunReport.schema.json"), mutated));
    }

    private static string AddKey(string json, string where, string key)
    {
        var root = JsonNode.Parse(json)!;
        var node = where.Split('.').Skip(1).Aggregate(root, (current, segment) =>
        {
            var name = segment;
            var index = -1;
            if (segment.EndsWith(']') && segment.IndexOf('[') is var open and > 0)
            {
                name = segment[..open];
                index = int.Parse(segment[(open + 1)..^1]);
            }
            var child = current![name]!;
            return index >= 0 ? child.AsArray()[index]! : child;
        });

        node!.AsObject()[key] = "an undeclared value";
        return root.ToJsonString();
    }

    private static ReportConfigurationOptions MergeableOptions(string dir) => new()
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = false,
        GenerateMergeableData = true,
        GenerateTestRunReportSchema = true,
    };

    private static Feature[] OneFeature(string testId) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = testId, DisplayName = "Place order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "When", Text = "the order is placed", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];
}
