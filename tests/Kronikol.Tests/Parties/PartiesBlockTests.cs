using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tracking;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1: <c>serviceId</c> and <c>parties</c> in every form of the data file, only when the run declared something
/// (plans/PARTY_IDENTITY_PLAN.md §5.5): before <c>features</c> in the JSON and the <c>.krkl</c>, after <c>environment</c>
/// in the mergeable file, and in XML and YAML in their own idiom.
/// </summary>
[Collection("DiagramsFetcher")]
public class PartiesBlockTests
{
    internal static readonly DateTime Start = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    internal static Feature[] Features() =>
        [new Feature { DisplayName = "Breakfast", Scenarios = [new Scenario { Id = "t1", DisplayName = "Order pancakes", Result = ExecutionResult.Passed }] }];

    private static RequestResponseLog[] Pair(string caller, string service, string url, AttributionSource source)
    {
        var trace = Guid.NewGuid();
        var pair = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Order pancakes", "t1", HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, pair, false)
                { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source },
            new RequestResponseLog("Order pancakes", "t1", HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK)
                { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source }
        ];
    }

    internal static RequestResponseLog[] Logs() =>
    [
        .. Pair("Caller", "Breakfast Provider", "http://localhost/pancakes", AttributionSource.TestContext),
        .. Pair("Breakfast Provider", "Cow Service", "http://localhost:5001/milk", AttributionSource.RequestHeader)
    ];

    internal static PartyTable Declared() => PartyResolver.Resolve(PartiesReport.CallsOf(Logs()),
        new PartyDeclarations("breakfast-provider", [new PartyDescriptor("Cow Service") { Kind = PartyKind.Double, Note = "WireMock stub" }], [], []), PartyObservations.None);

    internal static PartyTable Undeclared() => PartyResolver.Resolve(PartiesReport.CallsOf(Logs()), PartyDeclarations.None, PartyObservations.None);

    private static string Write(DataFormat format, PartyTable? parties, bool compressed = false)
    {
        var extension = compressed ? "krkl" : format switch { DataFormat.Xml => "xml", DataFormat.Yaml => "yml", _ => "json" };
        return ReportGenerator.GenerateTestRunReportData(Features(), Start, Start.AddMinutes(1), $"Parties_{Guid.NewGuid():N}.{extension}", format, null, Logs(), null,
            fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null, compressedFile: compressed, parties: parties);
    }

    public static TheoryData<string> Forms() => ["json", "krkl", "xml", "yaml"];

    private static string WriteForm(string form, PartyTable? parties) => form switch
    {
        "krkl" => Write(DataFormat.Json, parties, compressed: true),
        "xml" => Write(DataFormat.Xml, parties),
        "yaml" => Write(DataFormat.Yaml, parties),
        _ => Write(DataFormat.Json, parties)
    };

    private static string Text(string form, string path) => form is "xml" or "yaml" ? File.ReadAllText(path) : ReportFile.Read(path);

    private static bool HasBlock(string form, string text) => form switch
    {
        "xml" => XDocument.Parse(text).Root!.Elements().Any(e => e.Name.LocalName is "ServiceId" or "Parties"),
        "yaml" => text.Split('\n').Any(l => l.StartsWith("ServiceId:", StringComparison.Ordinal) || l.StartsWith("Parties:", StringComparison.Ordinal)),
        _ => Has(JsonDocument.Parse(text).RootElement)
    };

    private static bool Has(JsonElement root) => root.TryGetProperty("serviceId", out _) || root.TryGetProperty("parties", out _);

    [Theory]
    [MemberData(nameof(Forms))]
    public void A_run_that_declares_nothing_writes_no_key(string form)
    {
        Assert.False(HasBlock(form, Text(form, WriteForm(form, null))));
        Assert.False(HasBlock(form, Text(form, WriteForm(form, Undeclared()))));
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void A_declaration_writes_the_block(string form) => Assert.True(HasBlock(form, Text(form, WriteForm(form, Declared()))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_json_holds_the_block_before_the_features_with_its_rows_sorted_and_empty_members_left_out(bool compressed)
    {
        using var report = JsonDocument.Parse(ReportFile.Read(Write(DataFormat.Json, Declared(), compressed)));

        var keys = report.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(keys.IndexOf("serviceId") + 1, keys.IndexOf("parties"));
        Assert.Equal(keys.IndexOf("parties") + 1, keys.IndexOf("features"));
        Assert.True(keys.IndexOf("ciMetadata") < keys.IndexOf("serviceId"));
        Assert.Equal("breakfast-provider", report.RootElement.GetProperty("serviceId").GetString());
        Assert.Equal(
            """[{"name":"Breakfast Provider","id":"breakfast-provider","kind":"underTest","kindSource":"inferred"},{"name":"Caller","kind":"test","kindSource":"inferred"},{"name":"Cow Service","kind":"double","kindSource":"declared","note":"WireMock stub"}]""",
            JsonSerializer.Serialize(report.RootElement.GetProperty("parties")));
    }

    // A call whose test id names none of the run's scenarios is in neither a scenario nor the background, so the report
    // leaves it out (an UnattributedInteractions diagnostic says so); its names are no parties of the report either.
    [Fact]
    public void The_table_names_the_parties_of_the_calls_the_report_carries_and_no_others()
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLog Call(string testId, string caller, string service) =>
            new("x", testId, HttpMethod.Get, null, new Uri("http://localhost:5002/x"), [], service, caller, RequestResponseType.Request, trace, pair, false);
        RequestResponseLog[] logs = [.. Logs(), Call("not-a-scenario", "Ghost Caller", "Ghost Service"), Call(Kronikol.Tracking.TestIdentityScope.UnknownTestId, "Breakfast Provider", "Config Server")];

        var table = PartiesReport.Resolve(new ReportConfigurationOptions { ServiceId = "breakfast-provider" }, Features(), logs, Directory.CreateTempSubdirectory("kronikol-parties-scope").FullName);

        Assert.Equal(["Breakfast Provider", "Caller", "Config Server", "Cow Service"], table.Parties.Select(p => p.Name));
    }

    [Fact]
    public void A_row_whose_kind_is_unknown_carries_no_kind_source()
    {
        var calls = PartiesReport.CallsOf(Logs()).Append(new PartyCall("Breakfast Provider", "Kafka Broker", "Kafka"));
        var table = PartyResolver.Resolve(calls, new PartyDeclarations("breakfast-provider", [], [], []), PartyObservations.None);

        Assert.Equal("""{"name":"Kafka Broker","kind":"unknown"}""", JsonSerializer.Serialize(PartiesReport.Json(table).Single(r => (string)r["name"]! == "Kafka Broker")));
    }

    [Fact]
    public void The_xml_holds_the_block_after_the_environment_and_its_schema_admits_it()
    {
        var dataPath = Write(DataFormat.Xml, Declared());
        var root = XDocument.Load(dataPath).Root!;

        var names = root.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(names.IndexOf("ServiceId") + 1, names.IndexOf("Parties"));
        Assert.Equal(names.IndexOf("Parties") + 1, names.IndexOf("Features"));
        var cow = root.Element("Parties")!.Elements("Party").Single(p => p.Element("Name")!.Value == "Cow Service");
        Assert.Equal(["Name", "Kind", "KindSource", "Note"], cow.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("double", cow.Element("Kind")!.Value);

        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"Parties_{Guid.NewGuid():N}.xsd", DataFormat.Xml);
        var schemas = new System.Xml.Schema.XmlSchemaSet();
        schemas.Add("", System.Xml.XmlReader.Create(new StringReader(ReportHtml.Read(schemaPath))));
        var errors = new List<string>();
        XDocument.Load(dataPath).Validate(schemas, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    [Fact]
    public void The_yaml_holds_the_block_in_its_own_names()
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Write(DataFormat.Yaml, Declared()))));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        Assert.Equal("breakfast-provider", ((YamlScalarNode)root["ServiceId"]).Value);
        var cow = ((YamlSequenceNode)root["Parties"]).Cast<YamlMappingNode>().Single(p => ((YamlScalarNode)p["Name"]).Value == "Cow Service");
        Assert.Equal(["Name", "Kind", "KindSource", "Note"], cow.Children.Keys.Select(k => ((YamlScalarNode)k).Value));
        Assert.Equal("WireMock stub", ((YamlScalarNode)cow["Note"]).Value);
    }

    [Fact]
    public void The_mergeable_file_holds_the_block_last()
    {
        var model = ReportGenerator.BuildMergeableReportModel(Features(), Start, Start.AddMinutes(1), null, null, null, null, WholeTestFlowVisualization.None, null,
            trackedLogs: Logs(), parties: Declared());

        Assert.Equal(["serviceId", "parties"], model.Keys.TakeLast(2));
        Assert.Equal("breakfast-provider", model["serviceId"]);
    }

    [Fact]
    public void The_mergeable_file_of_a_run_that_declares_nothing_has_no_block()
    {
        var model = ReportGenerator.BuildMergeableReportModel(Features(), Start, Start.AddMinutes(1), null, null, null, null, WholeTestFlowVisualization.None, null,
            trackedLogs: Logs(), parties: Undeclared());

        Assert.DoesNotContain("parties", model.Keys);
        Assert.DoesNotContain("serviceId", model.Keys);
    }
}
