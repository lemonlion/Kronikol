using Kronikol.Parties;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #192 R3: the party table of a report already written, one function the query engine and the service card (#191) both
/// call, so they agree on who each party is; with the subject the report is about, and each call's origin and cause.
/// </summary>
public class PartyTableTests
{
    private static readonly PartyCall[] Calls =
    [
        new("Caller", "orders-api", AttributionSource: "TestContext"),
        new("Caller", "orders-api", AttributionSource: "TestContext"),
        new("Caller", "billing-api", AttributionSource: "TestContext"),
        new("orders-api", "Payments", AttributionSource: "RequestHeader"),
        new("billing-api", "Ledger", AttributionSource: "RequestHeader")
    ];

    [Fact]
    public void A_report_with_its_block_is_read_as_written_and_one_without_is_inferred()
    {
        Party[] written = [new("Caller", null, PartyKind.Test, PartyKindSource.Declared, null), new("orders-api", "orders", PartyKind.UnderTest, PartyKindSource.Declared, null)];

        var (asWritten, _) = PartyTable.OfReport(written, "orders", Calls);
        var (inferred, _) = PartyTable.OfReport(null, null, Calls);

        Assert.Equal(written, asWritten.Parties);
        Assert.True(asWritten.Declared);
        Assert.False(inferred.Declared);
        Assert.Equal(PartyKind.Test, inferred.Parties.Single(p => p.Name == "Caller").Kind);
    }

    [Fact]
    public void The_subject_is_the_party_the_service_id_names_else_the_most_called_subject_else_the_first_by_name()
    {
        Party[] two = [new("billing-api", "billing", PartyKind.UnderTest, PartyKindSource.Declared, null), new("orders-api", null, PartyKind.UnderTest, PartyKindSource.Declared, null)];
        Party[] even = [new("b-api", null, PartyKind.UnderTest, PartyKindSource.Declared, null), new("a-api", null, PartyKind.UnderTest, PartyKindSource.Declared, null)];

        Assert.Equal("billing-api", PartyTable.OfReport(two, "billing", Calls).Subject);
        Assert.Equal("orders-api", PartyTable.OfReport(two, null, Calls).Subject);
        Assert.Equal("a-api", PartyTable.OfReport(even, null, [new("Caller", "a-api"), new("Caller", "b-api")]).Subject);
        Assert.Null(PartyTable.OfReport([new("Caller", null, PartyKind.Test, PartyKindSource.Declared, null)], null, Calls).Subject);
    }

    [Fact]
    public void A_report_without_its_block_is_inferred_from_its_calls_alone_whatever_its_service_id()
    {
        // As the parties verb read such a report before the function was shared, and as the card (#191) reads it: a root
        // service id with no block, which no Kronikol writer makes, is no declaration, so no row takes it as its id.
        var (table, subject) = PartyTable.OfReport(null, "orders", Calls);

        Assert.False(table.Declared);
        Assert.All(table.Parties, p => Assert.Null(p.Id));
        Assert.Equal("orders-api", subject);
    }

    [Fact]
    public void The_service_id_names_the_subject_only_among_the_subjects()
    {
        // A merge whose shards disagreed on a row's kind writes it unknown, with the id they agreed on.
        Party[] merged = [new("billing-api", null, PartyKind.UnderTest, PartyKindSource.Declared, null), new("orders-api", "orders", PartyKind.Unknown, null, null)];

        Assert.Equal("billing-api", PartyTable.OfReport(merged, "orders", Calls).Subject);
    }

    [Fact]
    public void Only_a_test_party_and_a_subject_have_an_origin_of_their_own()
    {
        Party[] rows =
        [
            new("Caller", null, PartyKind.Test, PartyKindSource.Declared, null), new("orders-api", null, PartyKind.UnderTest, PartyKindSource.Declared, null),
            new("Payments", null, PartyKind.Real, PartyKindSource.Declared, null), new("Stub", null, PartyKind.Double, PartyKindSource.Declared, null),
            new("Mystery", null, PartyKind.Unknown, null, null)
        ];
        var (table, _) = PartyTable.OfReport(rows, null, []);

        Assert.Equal(["test", "subject", "other", "other", "other"], rows.Select(r => table.OriginOf(r.Name)).ToArray());
    }

    [Fact]
    public void The_tests_context_is_the_tests_only_on_a_subjects_call_when_no_party_is_the_test()
    {
        // The in-process case: the test runs the subject's code, so the subject's calls on its context are the test's. A
        // broker's delivery or another party's call on that context is the background work the context reached.
        var (table, _) = PartyTable.OfReport([new("orders-handler", null, PartyKind.UnderTest, PartyKindSource.Declared, null)], null, []);

        Assert.Equal("test", table.CauseOf("orders-handler", "TestContext", inBackgroundBlock: false, insideReceivedCall: false));
        Assert.Equal("background", table.CauseOf("Kafka Broker", "TestContext", inBackgroundBlock: false, insideReceivedCall: false));
    }

    [Fact]
    public void A_call_outside_every_scenario_is_background_unless_a_test_party_made_it()
    {
        var (table, _) = PartyTable.OfReport(null, null, Calls);

        Assert.Equal("background", table.CauseOf("orders-api", "RequestHeader", inBackgroundBlock: true, insideReceivedCall: true));
        Assert.Equal("test", table.CauseOf("Caller", null, inBackgroundBlock: true, insideReceivedCall: false));
        Assert.Equal("other", table.OriginOf("nobody"));
    }
}
