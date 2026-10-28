using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Kronikol.CallGraph;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tests.Reports;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 4.6 and 5.2.7: <c>schemas/service-card.v1.schema.json</c>, committed and closed as the data
/// file's schema is. A card holding every member the writer can write validates, which a closed schema allows only when
/// every one of them is declared; and a member it does not declare is a violation. The schema is read from the repository
/// and parsed, never compared by its bytes: a Windows checkout has CRLF.
/// </summary>
public class ServiceCardSchemaTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string SchemaPath = Path.Combine(RepoRoot, "schemas", "service-card.v1.schema.json");

    // Parsed once: JsonSchema.Net registers a schema by its $id, and a second parse of the same $id throws.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(File.ReadAllText(SchemaPath)));

    private static List<string> Errors(string card)
    {
        using var instance = JsonDocument.Parse(card);
        var result = Schema.Value.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical, RequireFormatValidation = false });
        var errors = new List<string>();
        SchemaValidationTests.Collect(result, errors);
        return errors;
    }

    /// <summary>A card with every member the writer can write: a subject, an environment, every degraded kind, sources with and without a line, a message with no direction and no topic, an empty target, a background-only operation.</summary>
    private static string Maximal()
    {
        var withLine = Records.Scenario(0, "Order pancakes", "Orders", "smoke") with { SourceFile = "Features/Orders.feature", SourceLine = 7 };
        var featureOnly = Records.Scenario(1, "Read the menu", "Menu") with { FeatureSourceFile = "Features/Menu.feature" };
        var r = new Records();
        var order = r.Request(withLine, "Caller", "Breakfast Provider", "POST", "http://localhost/orders/1", attribution: "TestContext", phase: "Action");
        r.Call(withLine, "Breakfast Provider", "Kafka", "Produce", "kafka:///", attribution: "RequestHeader", category: "MessageQueue");
        r.Call(withLine, "Breakfast Provider", "Kafka", "Teleport → orders", "kafka:///orders", attribution: "RequestHeader", category: "MessageQueue");
        r.Call(withLine, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/milk", attribution: "RequestHeader", testDouble: "WireMock", status: "503", isError: true);
        r.Answer(order, "201");
        r.Request(featureOnly, "Caller", "Breakfast Provider", "GET", "http://localhost/menu", attribution: "TestContext");
        r.Call(null, "Breakfast Provider", "Outbox", "POST", "http://localhost:5002/outbox", attribution: "Expired");
        var graph = CallGraphBuilder.Build([withLine, featureOnly], r.All, new PartiesInput(
        [
            new Party("Breakfast Provider", "breakfast-provider", PartyKind.UnderTest, PartyKindSource.Declared, "the service"),
            new Party("Caller", null, PartyKind.Test, PartyKindSource.Observed, null),
            new Party("Cow Service", null, PartyKind.Double, PartyKindSource.Declared, "WireMock stub")
        ], "breakfast-provider"));

        return ServiceCardTests.Card(graph, new ServiceCardRun
        {
            Id = "local:20261028T090030Z:00c4406a", At = "2026-10-28T09:00:30Z", Repository = "lemonlion/BreakfastProvider", Commit = "68a8760", Branch = "main",
            Suite = "BreakfastProvider.Tests", KronikolVersion = "5.17.0", ReportFile = "TestRunReport.krkl", ReportFormatVersion = 3,
            ReportKronikolVersion = "5.17.0+0123abcd", ReportSha256 = new string('a', 64), Os = "Microsoft Windows 10.0.26200", Runtime = ".NET 10.0.11",
            Scenarios = 2, Passed = 1, Failed = 1,
            Degraded = [.. ServiceCardWriter.DegradedKinds.Select(k => KeyValuePair.Create(k, 1))]
        });
    }

    [Fact]
    public void A_card_holding_every_member_the_writer_can_write_validates()
    {
        var card = Maximal();
        using (var document = JsonDocument.Parse(card))
        {
            var behaviour = document.RootElement.GetProperty("behaviour");
            Assert.Equal(JsonValueKind.Object, behaviour.GetProperty("subject").ValueKind);
            Assert.Contains(behaviour.GetProperty("messages").EnumerateArray(), m => m.GetProperty("direction").ValueKind == JsonValueKind.Null);
            Assert.Equal(7, document.RootElement.GetProperty("run").GetProperty("degraded").EnumerateObject().Count());
        }

        Assert.Empty(Errors(card));
    }

    [Fact]
    public void A_card_of_a_run_with_no_subject_no_environment_and_no_calls_validates()
    {
        Assert.Empty(Errors(ServiceCardTests.Card(CallGraphBuilder.Build([Records.Scenario(0, "Start")], [], PartiesInput.Inferred, callsRecorded: false))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("run")]
    [InlineData("run.report")]
    [InlineData("run.counts.0")]
    [InlineData("run.degraded")]
    [InlineData("behaviour")]
    [InlineData("behaviour.rules")]
    [InlineData("behaviour.subject")]
    [InlineData("behaviour.parties.0")]
    [InlineData("behaviour.edges.0.operations.0")]
    [InlineData("behaviour.scenarios.0.source")]
    [InlineData("behaviour.capture")]
    public void A_member_the_schema_does_not_declare_is_a_violation(string at)
    {
        var card = JsonNode.Parse(Maximal())!;
        var target = at.Length == 0 ? card : at.Split('.').Aggregate(card, (node, step) => int.TryParse(step, out var i) ? node[i]! : node[step]!);
        target.AsObject()["unknownMember"] = 1;

        Assert.NotEmpty(Errors(card.ToJsonString() + "\n"));
    }

    [Fact]
    public void The_schema_names_its_raw_url_on_main_and_the_card_carries_no_schema_member()
    {
        var schema = Schema.Value;
        using var document = JsonDocument.Parse(File.ReadAllText(SchemaPath));

        Assert.Equal("https://raw.githubusercontent.com/lemonlion/Kronikol/main/schemas/service-card.v1.schema.json", document.RootElement.GetProperty("$id").GetString());
        Assert.NotNull(schema);
        Assert.DoesNotContain("\"$schema\"", Maximal(), StringComparison.Ordinal);
    }
}
