using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Parties;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R2 (plans/PARTY_IDENTITY_PLAN.md §5.10): a call a capturer saw a test double answer carries what answered as
/// <c>testDouble</c>, a string. The data file writes it last on the call and only when set; a reader reads a call's own
/// members only, so an object under the key cannot rename the call; and the resolver reads a party all of whose calls
/// were so answered as a double, observed.
/// </summary>
[Collection("DiagramsFetcher")]
public class TestDoubleTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-test-double").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static RequestResponseLog[] Pair(string caller, string service, string url, string? testDouble)
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        return
        [
            new RequestResponseLog("Order pancakes", "t1", HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, pair, false)
                { Timestamp = DateTimeOffset.UtcNow, TestDouble = testDouble },
            new RequestResponseLog("Order pancakes", "t1", HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK)
                { Timestamp = DateTimeOffset.UtcNow, TestDouble = testDouble }
        ];
    }

    private static RequestResponseLog[] Logs() =>
    [
        .. Pair("Caller", "Breakfast Provider", "http://localhost/pancakes", null),
        .. Pair("Breakfast Provider", "Cow Service", "http://localhost:5001/milk", "WireMock")
    ];

    private string Write(DataFormat format, RequestResponseLog[] logs)
    {
        var extension = format switch { DataFormat.Xml => "xml", DataFormat.Yaml => "yml", _ => "json" };
        return ReportGenerator.GenerateTestRunReportData(PartiesBlockTests.Features(), Start, Start.AddMinutes(1), Path.Combine(_dir, $"Doubles_{Guid.NewGuid():N}.{extension}"),
            format, null, logs, null, fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null);
    }

    private static JsonElement[] Calls(string json)
    {
        using var report = JsonDocument.Parse(json);
        return report.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions").EnumerateArray().Select(c => c.Clone()).ToArray();
    }

    [Fact]
    public void The_json_writes_the_mark_last_on_a_marked_call_and_nowhere_else()
    {
        var calls = Calls(ReportFile.Read(Write(DataFormat.Json, Logs())));

        var marked = calls.Where(c => c.GetProperty("serviceName").GetString() == "Cow Service").ToArray();
        Assert.Equal(2, marked.Length);
        Assert.All(marked, c => Assert.Equal(("testDouble", "WireMock"), (c.EnumerateObject().Last().Name, c.GetProperty("testDouble").GetString())));
        Assert.All(calls.Where(c => c.GetProperty("serviceName").GetString() == "Breakfast Provider"), c => Assert.False(c.TryGetProperty("testDouble", out _)));
    }

    [Fact]
    public void The_xml_and_the_yaml_write_it_in_their_own_names()
    {
        var xml = XDocument.Load(Write(DataFormat.Xml, Logs()));
        var xmlCalls = xml.Descendants().Where(e => e.Element("ServiceName")?.Value == "Cow Service").ToArray();
        Assert.NotEmpty(xmlCalls);
        Assert.All(xmlCalls, c => Assert.Equal(("TestDouble", "WireMock"), (c.Elements().Last().Name.LocalName, c.Elements().Last().Value)));
        Assert.DoesNotContain(xml.Descendants(), e => e.Name.LocalName == "TestDouble" && e.Parent?.Element("ServiceName")?.Value != "Cow Service");
        var schemas = new System.Xml.Schema.XmlSchemaSet();
        schemas.Add("", System.Xml.XmlReader.Create(new StringReader(ReportHtml.Read(ReportGenerator.GenerateTestRunReportSchema($"Doubles_{Guid.NewGuid():N}.xsd", DataFormat.Xml)))));
        var errors = new List<string>();
        xml.Validate(schemas, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);

        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Write(DataFormat.Yaml, Logs()))));
        var yamlCalls = yaml.Documents[0].AllNodes.OfType<YamlMappingNode>()
            .Where(m => m.Children.TryGetValue(new YamlScalarNode("ServiceName"), out var name) && ((YamlScalarNode)name).Value == "Cow Service").ToArray();
        Assert.NotEmpty(yamlCalls);
        Assert.All(yamlCalls, c => Assert.Equal("WireMock", ((YamlScalarNode)c[new YamlScalarNode("TestDouble")]).Value));
    }

    // kronikol merge reads each shard's calls back through MergeableReportReader, so a mark the reader dropped would be
    // missing from every merged report.
    [Fact]
    public void A_mergeable_file_reads_the_mark_back()
    {
        var shard = MergeableReportReader.Parse(ReportGenerator.GenerateMergeableReportJson(PartiesBlockTests.Features(), Start, Start.AddMinutes(1), null, null, null, null,
            WholeTestFlowVisualization.None, null, trackedLogs: Logs()));

        Assert.Contains(shard.Interactions, l => l.ServiceName == "Cow Service");
        Assert.All(shard.Interactions.Where(l => l.ServiceName == "Cow Service"), l => Assert.Equal("WireMock", l.TestDouble));
        Assert.All(shard.Interactions.Where(l => l.ServiceName == "Breakfast Provider"), l => Assert.Null(l.TestDouble));
    }

    [Fact]
    public void A_report_without_a_mark_writes_no_key()
    {
        RequestResponseLog[] plain = [.. Pair("Caller", "Breakfast Provider", "http://localhost/pancakes", null)];

        Assert.DoesNotContain("testDouble", ReportFile.Read(Write(DataFormat.Json, plain)));
        Assert.DoesNotContain("TestDouble", File.ReadAllText(Write(DataFormat.Xml, plain)));
        Assert.DoesNotContain("TestDouble", File.ReadAllText(Write(DataFormat.Yaml, plain)));
    }

    // Every released scanner read any scalar under a call as the call's own member, so an object under testDouble that
    // holds a serviceName renamed the call. A call now reads its own members only, as a scenario has since 4.16.3.
    [Fact]
    public void An_object_under_the_key_does_not_rename_the_call()
    {
        var path = Path.Combine(_dir, "handmade.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "5.15.0", "formatVersion": 1, "suite": null,
              "startTime": "2026-10-27T09:00:00Z", "endTime": "2026-10-27T09:01:00Z",
              "features": [ { "name": "Breakfast", "labels": [], "scenarios": [
                { "id": "t1", "stableId": "aaaabbbbccccdddd", "name": "Order pancakes", "result": "Passed", "durationSeconds": 1, "labels": [], "categories": [], "steps": [],
                  "httpInteractions": [
                    { "type": "Request", "method": "GET", "uri": "http://localhost:5001/milk", "serviceName": "Cow Service", "callerName": "Breakfast Provider",
                      "requestResponseId": "11111111-1111-1111-1111-111111111111", "testDouble": { "serviceName": "Impostor", "statusCode": 500, "kind": "mock" } },
                    { "type": "Response", "method": "GET", "uri": "http://localhost:5001/milk", "serviceName": "Cow Service", "callerName": "Breakfast Provider",
                      "statusCode": 200, "statusText": "OK", "requestResponseId": "11111111-1111-1111-1111-111111111111", "testDouble": { "serviceName": "Impostor" } }
                  ] } ] } ]
            }
            """);

        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["services", path], output, new StringWriter()));

        Assert.Contains("Cow Service", output.ToString());
        Assert.DoesNotContain("Impostor", output.ToString());
        var call = Assert.Single(ReportScanner.Scan(path).Scenarios.Single().Interactions, i => i.Type == "Request");
        Assert.Equal(("Cow Service", (string?)null), (call.ServiceName, call.TestDouble));
    }

    [Fact]
    public void A_party_all_of_whose_calls_a_double_answered_is_a_double_observed()
    {
        var table = PartyResolver.Resolve(PartiesReport.CallsOf(Logs()), PartyDeclarations.None, PartyObservations.None);

        Assert.Equal(new Party("Cow Service", null, PartyKind.Double, PartyKindSource.Observed, null), table.Parties.Single(p => p.Name == "Cow Service"));
    }

    [Fact]
    public void A_party_only_some_of_whose_calls_a_double_answered_keeps_its_kind()
    {
        RequestResponseLog[] logs = [.. Logs(), .. Pair("Breakfast Provider", "Cow Service", "http://localhost:5001/cream", null)];

        var undeclared = PartyResolver.Resolve(PartiesReport.CallsOf(logs), PartyDeclarations.None, PartyObservations.None);
        Assert.Equal((PartyKind.Unknown, (PartyKindSource?)null), (undeclared.Parties.Single(p => p.Name == "Cow Service").Kind, undeclared.Parties.Single(p => p.Name == "Cow Service").KindSource));

        var declared = PartyResolver.Resolve(PartiesReport.CallsOf(logs), new PartyDeclarations(null, [new PartyDescriptor("Cow Service") { Kind = PartyKind.Real }], [], []), PartyObservations.None);
        Assert.Equal((PartyKind.Real, (PartyKindSource?)PartyKindSource.Declared), (declared.Parties.Single(p => p.Name == "Cow Service").Kind, declared.Parties.Single(p => p.Name == "Cow Service").KindSource));
    }

    // A party the capturers saw a double answer is no candidate for the service under test. Before it was kept out, a
    // test that called a stub more often than the service made the stub the subject, and the service stayed unknown.
    [Fact]
    public void A_double_the_test_calls_more_often_than_the_service_is_not_the_service_under_test()
    {
        RequestResponseLog[] logs =
        [
            .. Pair("Caller", "Breakfast Provider", "http://localhost/pancakes", null),
            .. Pair("Caller", "Cow Service", "http://localhost:5001/milk", "WireMock"),
            .. Pair("Caller", "Cow Service", "http://localhost:5001/cream", "WireMock")
        ];

        var table = PartyResolver.Resolve(PartiesReport.CallsOf(logs), PartyDeclarations.None, PartyObservations.None);

        var (provider, cow) = (table.Parties.Single(p => p.Name == "Breakfast Provider"), table.Parties.Single(p => p.Name == "Cow Service"));
        Assert.Equal((PartyKind.UnderTest, (PartyKindSource?)PartyKindSource.Inferred), (provider.Kind, provider.KindSource));
        Assert.Equal((PartyKind.Double, (PartyKindSource?)PartyKindSource.Observed), (cow.Kind, cow.KindSource));
    }

    [Fact]
    public void Marked_calls_on_a_party_declared_real_are_a_conflict()
    {
        var table = PartyResolver.Resolve(PartiesReport.CallsOf(Logs()), new PartyDeclarations(null, [new PartyDescriptor("Cow Service") { Kind = PartyKind.Real }], [], []), PartyObservations.None);

        Assert.Equal(PartyKind.Real, table.Parties.Single(p => p.Name == "Cow Service").Kind);
        var conflict = Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationConflict);
        Assert.Contains("\"Cow Service\" is declared real", conflict.Message);
        Assert.Contains("1 of its 1 calls", conflict.Message);
        Assert.Contains("WireMock", conflict.Message);
    }

    [Fact]
    public void The_parties_verb_counts_the_calls_a_double_answered()
    {
        RequestResponseLog[] logs = [.. Logs(), .. Pair("Breakfast Provider", "Cow Service", "http://localhost:5001/cream", null)];
        var path = Write(DataFormat.Json, logs);

        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["parties", path, "--json"], output, new StringWriter()));

        using var json = JsonDocument.Parse(output.ToString());
        var cow = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("party").GetString() == "Cow Service");
        Assert.Equal((2, 1), (cow.GetProperty("callsReceived").GetInt32(), cow.GetProperty("doubleCalls").GetInt32()));
        var provider = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("party").GetString() == "Breakfast Provider");
        Assert.Equal(0, provider.GetProperty("doubleCalls").GetInt32());
    }
}
