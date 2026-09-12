using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The shapes an outsider parses, and which therefore stop being free to change the moment anyone reads
/// them: the file's own version, the identity of a scenario, and the type of a status code.
///
/// <para>These are pinned here rather than spread through the format-specific suites because the property
/// that matters is that all three writers agree. A version key present in JSON and missing from YAML is
/// the same defect as no version key at all, for a consumer that reads YAML.</para>
/// </summary>
public class ReportContractShapeTests
{
    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                new Scenario { Id = "s2", DisplayName = "Cancel order", Result = ExecutionResult.Failed, ErrorMessage = "timeout", Duration = TimeSpan.FromSeconds(1) }
            ]
        }
    ];

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static JsonElement Json(string name)
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, name, DataFormat.Json);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    /// <summary>
    /// <c>formatVersion</c> has to be first, and it has to be there before any v1 file exists without it -
    /// otherwise every reader ours and the port's carries a "version 1 means absent" branch forever. The
    /// position is part of the contract: a streaming reader and a person running <c>head</c> both want the
    /// contract before the payload, which is the same reason <c>Failures.jsonl</c> and
    /// <c>query --json</c> already put it first.
    /// </summary>
    [Fact]
    public void The_json_declares_its_format_version_as_its_first_key()
    {
        var root = Json("Contract_version.json");

        Assert.Equal("formatVersion", root.EnumerateObject().First().Name);
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void The_xml_declares_its_format_version_as_its_first_element()
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Contract_version.xml", DataFormat.Xml);
        var root = XDocument.Load(path).Root!;

        Assert.Equal("FormatVersion", root.Elements().First().Name.LocalName);
        Assert.Equal("1", root.Element("FormatVersion")!.Value);
    }

    [Fact]
    public void The_yaml_declares_its_format_version_on_its_first_line()
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Contract_version.yml", DataFormat.Yaml);
        var first = File.ReadAllLines(path)[0];

        Assert.Equal("FormatVersion: 1", first);
    }

    /// <summary>
    /// A <c>stableId</c> that is not scoped to the suite that produced it collides the moment two suites
    /// are combined - by <c>merge</c>, by an ingest folding two runners, or by the cross-run ledger. The
    /// measured rate was 145 of 905 across suites and 0 within any single report, so this is a fix for
    /// combined reports and must not be sold as a live bug in an ordinary run.
    /// </summary>
    [Fact]
    public void A_stable_id_is_scoped_to_its_suite()
    {
        var unscoped = ScenarioStableId.Compute(null, "Orders", "Place order");
        var inAlpha = ScenarioStableId.Compute("Alpha.Tests", "Orders", "Place order");
        var inBeta = ScenarioStableId.Compute("Beta.Tests", "Orders", "Place order");

        Assert.NotEqual(inAlpha, inBeta);
        Assert.NotEqual(unscoped, inAlpha);

        // An absent suite has to reproduce the pre-suite id exactly, so a report whose suite cannot be
        // resolved - an ingest, a library caller - keeps the ids it has always had rather than silently
        // minting new ones.
        Assert.Equal(ScenarioStableId.Compute(null, "Orders", "Place order"),
                     ScenarioStableId.Compute("", "Orders", "Place order"));
    }

    [Fact]
    public void The_report_names_the_suite_its_ids_are_scoped_to()
    {
        var root = Json("Contract_suite.json");

        // The suite is a hash input, so the key has to be in the file: an id whose scope cannot be read
        // back is an id nobody can recompute or explain. The VALUE is deliberately allowed to be null —
        // RunSuite returns null rather than guessing when the output directory cannot answer — so
        // asserting a non-empty string here would be asserting a property of the machine running the
        // test rather than of the product, and would fail on any host whose layout differs.
        Assert.True(root.TryGetProperty("suite", out var suite), "the report must carry a suite key");
        Assert.True(suite.ValueKind is JsonValueKind.String or JsonValueKind.Null,
            $"suite must be a string or null, was {suite.ValueKind}");

        // What is unconditionally true, and is the thing that matters: whatever the report says its suite
        // is, that is the suite its ids were computed under.
        var scenario = root.GetProperty("features")[0].GetProperty("scenarios")[0];
        Assert.Equal(
            ScenarioStableId.Compute(suite.ValueKind == JsonValueKind.String ? suite.GetString() : null,
                "Orders", "Place order"),
            scenario.GetProperty("stableId").GetString());
    }

    /// <summary>
    /// <c>statusCode</c> was the enum NAME when .NET had one and a number when it did not, so
    /// <c>--status 5xx</c> found a 599 and missed a 500, and the only way to ask about a 400 was to know
    /// that .NET spells it <c>BadRequest</c>. The number is the interoperable value and belongs in the
    /// data; the name is a display property and belongs beside it.
    /// </summary>
    [Fact]
    public void An_http_status_is_a_number_with_its_name_beside_it()
    {
        RequestResponseLogger.Clear();
        var logs = new[]
        {
            new RequestResponseLog("Cancel order", "s2", HttpMethod.Post, null,
                new Uri("http://example.test/cake"), [], "Cake Service", "Tests",
                RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false,
                HttpStatusCode.BadRequest)
        };

        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Contract_status.json",
            DataFormat.Json, trackedLogs: logs);
        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        var interaction = root.GetProperty("features")[0].GetProperty("scenarios")[1]
            .GetProperty("httpInteractions")[0];

        Assert.Equal(JsonValueKind.Number, interaction.GetProperty("statusCode").ValueKind);
        Assert.Equal(400, interaction.GetProperty("statusCode").GetInt32());
        Assert.Equal("BadRequest", interaction.GetProperty("statusText").GetString());
    }

    /// <summary>
    /// A non-HTTP tap has no numeric code at all - a broker publish is <c>Sent</c>, a cache lookup is
    /// <c>Hit</c>. Those keep the text and carry no number, rather than being coerced into one.
    /// </summary>
    [Fact]
    public void A_non_numeric_status_carries_text_and_no_number()
    {
        RequestResponseLogger.Clear();
        var logs = new[]
        {
            new RequestResponseLog("Cancel order", "s2", "PUBLISH", null,
                new Uri("kafka:///orders"), [], "Event broker", "Tests",
                RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false,
                "Ack")
        };

        var path = ReportGenerator.GenerateTestRunReportData(Features(), Start, End, "Contract_status_text.json",
            DataFormat.Json, trackedLogs: logs);
        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        var interaction = root.GetProperty("features")[0].GetProperty("scenarios")[1]
            .GetProperty("httpInteractions")[0];

        Assert.Equal(JsonValueKind.Null, interaction.GetProperty("statusCode").ValueKind);
        Assert.Equal("Ack", interaction.GetProperty("statusText").GetString());
    }
}
