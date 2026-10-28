using System.Net;
using System.Text.Json;
using Kronikol.Parties;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1: <c>kronikol query parties</c>, and what <c>services</c> and <c>summary</c> say of a report that declares its
/// parties (plans/PARTY_IDENTITY_PLAN.md §5.7). A report that declares nothing reads as it did.
/// </summary>
[Collection("DiagramsFetcher")]
public class PartiesQueryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-parties-query").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Report(PartyTable? parties) => Report(parties, PartiesBlockTests.Logs());

    private string Report(PartyTable? parties, RequestResponseLog[] logs)
    {
        var path = Path.Combine(_dir, $"Parties_{Guid.NewGuid():N}.json");
        return ReportGenerator.GenerateTestRunReportData(PartiesBlockTests.Features(), PartiesBlockTests.Start, PartiesBlockTests.Start.AddMinutes(1), path, DataFormat.Json,
            null, logs, null, fullStepDetail: true, ciMetadata: null, suite: null, environment: null, attribution: null, parties: parties);
    }

    /// <summary>The fixture's calls, and one Breakfast Provider made to Outbox Store outside every scenario: a background call.</summary>
    private static RequestResponseLog[] WithBackgroundCall()
    {
        var (trace, pair, id) = (Guid.NewGuid(), Guid.NewGuid(), TestIdentityScope.UnknownTestId);
        var uri = new Uri("http://localhost:5002/outbox");
        return
        [
            .. PartiesBlockTests.Logs(),
            new RequestResponseLog(id, id, HttpMethod.Post, null, uri, [], "Outbox Store", "Breakfast Provider", RequestResponseType.Request, trace, pair, false) { Timestamp = DateTimeOffset.UtcNow },
            new RequestResponseLog(id, id, HttpMethod.Post, null, uri, [], "Outbox Store", "Breakfast Provider", RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = DateTimeOffset.UtcNow }
        ];
    }

    // The run resolves its parties over the background's calls too, so its block names a party only the background called;
    // the verb read the scenarios' calls only and left that party out (until 5.16.0: kronikol-ed's audit of 5.14.0).
    [Fact]
    public void A_party_called_only_in_the_background_is_listed_from_the_block_with_its_calls()
    {
        var logs = WithBackgroundCall();
        var declared = PartyResolver.Resolve(PartiesReport.CallsOf(logs),
            new PartyDeclarations(null, [new PartyDescriptor("Outbox Store") { Id = "outbox-db", Kind = PartyKind.Real }], [], []), PartyObservations.None);
        var path = Report(declared, logs);

        var (text, _, exit) = Query("parties", path, "--kind", "real");
        Assert.Equal(0, exit);
        Assert.Contains(Lines(text), l => l.StartsWith("Outbox Store") && l.Contains("real") && l.Contains("outbox-db"));

        var items = Items(Query("parties", path, "--json").Out);
        var outbox = items.Single(i => i.GetProperty("party").GetString() == "Outbox Store");
        Assert.Equal((0, 1), (outbox.GetProperty("callsMade").GetInt32(), outbox.GetProperty("callsReceived").GetInt32()));
        Assert.Equal(2, items.Single(i => i.GetProperty("party").GetString() == "Breakfast Provider").GetProperty("callsMade").GetInt32());
    }

    [Fact]
    public void A_report_that_declares_nothing_infers_over_the_background_calls_too()
    {
        var path = Report(null, WithBackgroundCall());

        var items = Items(Query("parties", path, "--json").Out);

        var outbox = items.Single(i => i.GetProperty("party").GetString() == "Outbox Store");
        Assert.Equal(1, outbox.GetProperty("callsReceived").GetInt32());
    }

    // An address scopes the verb to one scenario's calls, which the background's are not.
    [Fact]
    public void A_scenario_address_leaves_the_background_calls_out()
    {
        var path = Report(null, WithBackgroundCall());

        var items = Items(Query("parties", path, "s0", "--json").Out);

        Assert.DoesNotContain(items, i => i.GetProperty("party").GetString() == "Outbox Store");
        Assert.Equal(1, items.Single(i => i.GetProperty("party").GetString() == "Breakfast Provider").GetProperty("callsMade").GetInt32());
    }

    // Every other verb still reads the scenarios' calls only: services counts what the scenarios called.
    [Fact]
    public void Services_still_counts_the_scenarios_calls_only()
    {
        var path = Report(null, WithBackgroundCall());

        Assert.DoesNotContain("Outbox Store", Query("services", path).Out);
    }

    private static (string Out, string Err, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static string[] Lines(string text) => text.Replace("\r", "").Split('\n');

    private static JsonElement[] Items(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    [Fact]
    public void Lists_every_party_of_a_declaring_report_as_written_with_its_calls()
    {
        var (output, _, exit) = Query("parties", Report(PartiesBlockTests.Declared()));

        Assert.Equal(0, exit);
        var lines = Lines(output);
        Assert.Contains(lines, l => l.StartsWith("Breakfast Provider", StringComparison.Ordinal) && l.Contains("underTest") && l.Contains("inferred") && l.Contains("breakfast-provider"));
        Assert.Contains(lines, l => l.StartsWith("Caller", StringComparison.Ordinal) && l.Contains(" test ") && l.Contains("inferred"));
        Assert.Contains(lines, l => l.StartsWith("Cow Service", StringComparison.Ordinal) && l.Contains("double") && l.Contains("declared") && l.Contains("WireMock stub"));
        Assert.DoesNotContain("declares nothing", output);
        // Caller has no id and no note, and its line ends at its count
        Assert.DoesNotContain(lines, l => l.EndsWith(' '));
    }

    [Fact]
    public void The_json_gives_each_party_its_kind_its_source_and_its_calls()
    {
        var items = Items(Query("parties", Report(PartiesBlockTests.Declared()), "--json").Out);

        var provider = items.Single(i => i.GetProperty("party").GetString() == "Breakfast Provider");
        Assert.Equal(("underTest", "inferred", "breakfast-provider"), (provider.GetProperty("kind").GetString(), provider.GetProperty("kindSource").GetString(), provider.GetProperty("id").GetString()));
        Assert.Equal((1, 1), (provider.GetProperty("callsMade").GetInt32(), provider.GetProperty("callsReceived").GetInt32()));
        var caller = items.Single(i => i.GetProperty("party").GetString() == "Caller");
        Assert.Equal((1, 0), (caller.GetProperty("callsMade").GetInt32(), caller.GetProperty("callsReceived").GetInt32()));
    }

    [Fact]
    public void Kind_keeps_the_parties_of_that_kind_and_refuses_one_that_is_none_of_the_five()
    {
        var report = Report(PartiesBlockTests.Declared());

        var items = Items(Query("parties", report, "--kind", "double", "--json").Out);
        Assert.Equal(["Cow Service"], items.Select(i => i.GetProperty("party").GetString()));

        var (_, error, exit) = Query("parties", report, "--kind", "stub");
        Assert.Equal(2, exit);
        Assert.Contains("Not a party kind: stub", error);
    }

    [Fact]
    public void A_report_that_declares_nothing_gets_the_inferred_table_and_a_footer_saying_so()
    {
        var (output, _, exit) = Query("parties", Report(null));

        Assert.Equal(0, exit);
        Assert.Contains(Lines(output), l => l.StartsWith("Breakfast Provider", StringComparison.Ordinal) && l.Contains("underTest") && l.Contains("inferred"));
        Assert.Contains("1 parties of unknown kind: declare them in Parties or .kronikol/parties.json", output);
        Assert.Contains("the report declares nothing, so this table is inferred from the calls", output);
    }

    [Fact]
    public void Services_reads_as_it_did_on_a_report_that_declares_nothing_and_gains_id_and_kind_on_one_that_does()
    {
        var plain = Lines(Query("services", Report(null)).Out);
        Assert.Equal("service                  calls errors     bytes      p50      max  statuses", plain[0]);
        Assert.DoesNotContain(Items(Query("services", Report(null), "--json").Out), i => i.TryGetProperty("kind", out _));

        var declared = Report(PartiesBlockTests.Declared());
        var header = Lines(Query("services", declared).Out)[0];
        Assert.True(header.IndexOf(" id ", StringComparison.Ordinal) < header.IndexOf(" kind ", StringComparison.Ordinal));
        Assert.True(header.IndexOf(" kind ", StringComparison.Ordinal) < header.IndexOf("statuses", StringComparison.Ordinal));
        var cow = Items(Query("services", declared, "--json").Out).Single(i => i.GetProperty("service").GetString() == "Cow Service");
        Assert.Equal("double", cow.GetProperty("kind").GetString());
    }

    [Fact]
    public void Summary_says_the_service_only_on_a_report_that_declares_its_parties()
    {
        Assert.DoesNotContain(Lines(Query("summary", Report(null)).Out), l => l.StartsWith("service:", StringComparison.Ordinal));
        var declared = Report(PartiesBlockTests.Declared());
        Assert.Contains(Lines(Query("summary", declared).Out), l => l.StartsWith("service: breakfast-provider  under test: Breakfast Provider", StringComparison.Ordinal));

        using var plain = JsonDocument.Parse(Query("summary", Report(null), "--json").Out);
        Assert.False(plain.RootElement.GetProperty("run").TryGetProperty("serviceId", out _));
        using var json = JsonDocument.Parse(Query("summary", declared, "--json").Out);
        Assert.Equal("breakfast-provider", json.RootElement.GetProperty("run").GetProperty("serviceId").GetString());
    }
}
