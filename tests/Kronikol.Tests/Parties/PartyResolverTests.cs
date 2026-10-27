using Kronikol.Parties;
using Kronikol.Reports;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1: who each party is. The resolver is a pure function of the run's calls, what the run and the parties file
/// declare, and what Kronikol observed of the test client; each field of a row is resolved on its own, declared over
/// observed over inferred (plans/PARTY_IDENTITY_PLAN.md §5.1-5.4).
/// </summary>
public class PartyResolverTests
{
    private static IEnumerable<PartyCall> Calls(params (string Caller, string Service, int Count, string? CallerCategory, string? Category, string? Source)[] edges) =>
        edges.SelectMany(e => Enumerable.Repeat(new PartyCall(e.Caller, e.Service, e.Category, e.CallerCategory, e.Source), e.Count));

    private static readonly PartyCall[] Lane = Calls(
        ("Caller", "Breakfast Provider", 100, null, null, "TestContext"),
        ("Breakfast Provider", "CosmosDB", 80, null, "CosmosDB", "RequestHeader"),
        ("Breakfast Provider", "Cow Service", 30, null, null, "RequestHeader")).ToArray();

    private static PartyTable Resolve(IEnumerable<PartyCall> calls, string? serviceId = null, PartyDescriptor[]? options = null, PartyDescriptor[]? file = null, PartyObservations? observations = null) =>
        PartyResolver.Resolve(calls, new PartyDeclarations(serviceId, options ?? [], file ?? [], []), observations ?? PartyObservations.None);

    private static Party Row(PartyTable table, string name) => table.Parties.Single(p => p.Name == name);

    [Fact]
    public void Nothing_declared_infers_the_test_and_the_subject_and_leaves_the_rest_unknown()
    {
        var table = Resolve(Lane);

        Assert.Equal(["Breakfast Provider", "Caller", "CosmosDB", "Cow Service"], table.Parties.Select(p => p.Name));
        Assert.Equal((PartyKind.Test, PartyKindSource.Inferred), (Row(table, "Caller").Kind, Row(table, "Caller").KindSource));
        Assert.Equal((PartyKind.UnderTest, PartyKindSource.Inferred), (Row(table, "Breakfast Provider").Kind, Row(table, "Breakfast Provider").KindSource));
        Assert.Equal((PartyKind.Unknown, (PartyKindSource?)null), (Row(table, "CosmosDB").Kind, Row(table, "CosmosDB").KindSource));
        Assert.False(table.Declared);
        Assert.Null(table.ServiceId);
        Assert.Empty(table.Diagnostics);
    }

    [Fact]
    public void A_declaration_in_the_options_beats_the_file_which_beats_an_observation_which_beats_the_inference()
    {
        var observed = new PartyObservations([new TestClientObservation("Caller", ["Breakfast Provider"], "BreakfastProvider.Api")]);

        var inferredOnly = Resolve(Lane);
        var observedOnly = Resolve(Lane, observations: observed);
        var fromFile = Resolve(Lane, file: [new PartyDescriptor("Caller") { Kind = PartyKind.Real }], observations: observed);
        var fromOptions = Resolve(Lane, options: [new PartyDescriptor("Caller") { Kind = PartyKind.Double }], file: [new PartyDescriptor("Caller") { Kind = PartyKind.Real }], observations: observed);

        Assert.Equal(PartyKindSource.Inferred, Row(inferredOnly, "Caller").KindSource);
        Assert.Equal((PartyKind.Test, PartyKindSource.Observed), (Row(observedOnly, "Caller").Kind, Row(observedOnly, "Caller").KindSource));
        Assert.Equal((PartyKind.Real, PartyKindSource.Declared), (Row(fromFile, "Caller").Kind, Row(fromFile, "Caller").KindSource));
        Assert.Equal((PartyKind.Double, PartyKindSource.Declared), (Row(fromOptions, "Caller").Kind, Row(fromOptions, "Caller").KindSource));
    }

    [Fact]
    public void The_file_and_the_options_merge_field_by_field()
    {
        // A lane declares only its kinds, and the shared file only its ids.
        var table = Resolve(Lane,
            options: [new PartyDescriptor("CosmosDB") { Kind = PartyKind.Double, Note = "in-memory fake" }],
            file: [new PartyDescriptor("CosmosDB") { Id = "orders-store" }, new PartyDescriptor("Cow Service") { Id = "cow-service", Kind = PartyKind.Real }]);

        Assert.Equal(new Party("CosmosDB", "orders-store", PartyKind.Double, PartyKindSource.Declared, "in-memory fake"), Row(table, "CosmosDB"));
        Assert.Equal(new Party("Cow Service", "cow-service", PartyKind.Real, PartyKindSource.Declared, null), Row(table, "Cow Service"));
        Assert.True(table.Declared);
    }

    // The other way round: an options row that says nothing of the kind leaves the file's kind standing (the mutation run
    // of R1 found an options row read as declaring Unknown unseen while the options always gave the kind).
    [Fact]
    public void An_options_row_without_a_kind_keeps_the_files_kind()
    {
        var table = Resolve(Lane,
            options: [new PartyDescriptor("CosmosDB") { Note = "in-memory fake" }],
            file: [new PartyDescriptor("CosmosDB") { Id = "orders-store", Kind = PartyKind.Double }]);

        Assert.Equal(new Party("CosmosDB", "orders-store", PartyKind.Double, PartyKindSource.Declared, "in-memory fake"), Row(table, "CosmosDB"));
    }

    // A name that differs only in case is as much a near miss as one with spaces around it (the mutation run of R1 found
    // a match that ignored case unseen while only the spaced name was tried).
    [Theory]
    [InlineData(" cosmosdb ")]
    [InlineData("cosmosdb")]
    [InlineData("COSMOSDB")]
    public void A_declared_name_matches_exactly_and_a_near_miss_is_named_but_never_matched(string declared)
    {
        var table = Resolve(Lane, options: [new PartyDescriptor(declared) { Kind = PartyKind.Double }]);

        Assert.Equal(PartyKind.Unknown, Row(table, "CosmosDB").Kind);
        Assert.DoesNotContain(table.Parties, p => p.Kind == PartyKind.Double);
        var nearMiss = Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyNameNearMiss);
        Assert.Contains("\"" + declared + "\"", nearMiss.Message);
        Assert.Contains("\"CosmosDB\"", nearMiss.Message);
        Assert.DoesNotContain(table.Diagnostics, d => d.Kind == DiagnosticKind.DeclaredPartyNotSeen);
    }

    [Fact]
    public void Names_the_options_declare_that_no_call_carries_are_listed_once_and_the_files_are_not()
    {
        var table = Resolve(Lane,
            options: [new PartyDescriptor("Payments") { Kind = PartyKind.Double }, new PartyDescriptor("Ledger") { Kind = PartyKind.Real }],
            file: [new PartyDescriptor("Inventory") { Id = "inventory" }]);

        var notSeen = Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.DeclaredPartyNotSeen);
        Assert.Contains("Ledger", notSeen.Message);
        Assert.Contains("Payments", notSeen.Message);
        Assert.Contains("only some of the tests", notSeen.Message);
        Assert.DoesNotContain("Inventory", notSeen.Message);
    }

    [Theory]
    [InlineData("declared")]
    [InlineData("observed")]
    [InlineData("inferred")]
    public void The_service_id_attaches_to_the_subject_however_it_was_found(string how)
    {
        var table = how switch
        {
            "declared" => Resolve(Lane, serviceId: "breakfast-provider", options: [new PartyDescriptor("Breakfast Provider") { Kind = PartyKind.UnderTest }]),
            "observed" => Resolve(Lane, serviceId: "breakfast-provider", observations: new PartyObservations([new TestClientObservation("Caller", ["Breakfast Provider"], "BreakfastProvider.Api")])),
            _ => Resolve(Lane, serviceId: "breakfast-provider")
        };

        Assert.Equal("breakfast-provider", Row(table, "Breakfast Provider").Id);
        Assert.Equal("breakfast-provider", table.ServiceId);
        Assert.True(table.Declared);
    }

    [Fact]
    public void An_observed_subject_is_noted_with_its_application_unless_a_note_is_declared()
    {
        var observed = new PartyObservations([new TestClientObservation("Caller", ["Breakfast Provider"], "BreakfastProvider.Api")]);

        Assert.Equal("BreakfastProvider.Api", Row(Resolve(Lane, observations: observed), "Breakfast Provider").Note);
        Assert.Equal("the API", Row(Resolve(Lane, file: [new PartyDescriptor("Breakfast Provider") { Note = "the API" }], observations: observed), "Breakfast Provider").Note);
    }

    [Fact]
    public void Two_declarations_of_one_field_at_one_level_conflict_and_the_later_wins()
    {
        var table = Resolve(Lane, options: [new PartyDescriptor("Cow Service") { Kind = PartyKind.Real }, new PartyDescriptor("Cow Service") { Kind = PartyKind.Double }]);

        Assert.Equal(PartyKind.Double, Row(table, "Cow Service").Kind);
        var conflict = Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationConflict);
        Assert.Contains("Cow Service", conflict.Message);
    }

    [Fact]
    public void Two_declarations_that_agree_at_one_level_in_the_options_are_one()
    {
        var table = Resolve(Lane, options: [new PartyDescriptor("Cow Service") { Kind = PartyKind.Double }, new PartyDescriptor("Cow Service") { Id = "cow" }]);

        Assert.Equal(("cow", PartyKind.Double), (Row(table, "Cow Service").Id, Row(table, "Cow Service").Kind));
        Assert.DoesNotContain(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationConflict);
    }

    [Fact]
    public void A_name_the_file_lists_twice_conflicts()
    {
        var table = Resolve(Lane, file: [new PartyDescriptor("Cow Service") { Id = "cow" }, new PartyDescriptor("Cow Service") { Id = "cow" }]);

        Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationConflict);
    }

    [Fact]
    public void A_service_id_unlike_the_subjects_declared_id_conflicts_and_the_service_id_wins()
    {
        var table = Resolve(Lane, serviceId: "breakfast-provider", file: [new PartyDescriptor("Breakfast Provider") { Id = "bp" }]);

        Assert.Equal("breakfast-provider", Row(table, "Breakfast Provider").Id);
        Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.PartyDeclarationConflict);
    }

    [Fact]
    public void The_in_process_subject_is_declared_and_the_table_then_has_no_test_row()
    {
        // The test calls the service's code directly: the test and the subject are one participant, which the calls
        // alone cannot tell from an external-service lane, so only a declaration settles it.
        var calls = Calls(("Test", "Pricing Engine", 30, null, null, null), ("Test", "Postgres", 50, null, "PostgreSQL", null));

        var table = Resolve(calls, options: [new PartyDescriptor("Test") { Kind = PartyKind.UnderTest }]);

        Assert.Equal((PartyKind.UnderTest, PartyKindSource.Declared), (Row(table, "Test").Kind, Row(table, "Test").KindSource));
        Assert.DoesNotContain(table.Parties, p => p.Kind == PartyKind.Test);
        Assert.Equal(PartyKind.Unknown, Row(table, "Pricing Engine").Kind);
    }

    [Fact]
    public void A_call_the_hosts_trackers_make_under_the_test_partys_name_is_counted()
    {
        // The service's trackers were left at the default "Caller", the test client's name: one name, two parties.
        var calls = Calls(
            ("Caller", "Breakfast Provider", 100, null, null, "TestContext"),
            ("Caller", "CosmosDB", 80, null, "CosmosDB", "RequestHeader"),
            ("Caller", "Cow Service", 30, null, null, "RequestHeader"));

        var table = Resolve(calls, serviceId: "breakfast-provider");

        var inside = Assert.Single(table.Diagnostics, d => d.Kind == DiagnosticKind.TestPartyNameInsideHost);
        Assert.Contains("110", inside.Message);
        Assert.Contains("CallerName", inside.Message);
        Assert.DoesNotContain(Resolve(Lane, serviceId: "breakfast-provider").Diagnostics, d => d.Kind == DiagnosticKind.TestPartyNameInsideHost);
    }

    [Fact]
    public void A_run_that_declares_nothing_carries_no_diagnostic_but_the_checks_are_still_made()
    {
        var calls = Calls(("Caller", "Breakfast Provider", 10, null, null, "TestContext"), ("Caller", "Cow Service", 5, null, null, "RequestHeader"));

        var table = Resolve(calls);

        Assert.False(table.Declared);
        Assert.Empty(table.Diagnostics);
        Assert.Single(table.Notes, d => d.Kind == DiagnosticKind.TestPartyNameInsideHost);
    }

    public static TheoryData<string, string[], string[], (string, string, int, string?, string?, string?)[]> Cases() => new()
    {
        // plans/PARTY_IDENTITY_PLAN.harness/bp-reports/cases.py, nine of ten right; the tenth is the in-process subject.
        { "Jest recipe: Fastify inbound, MSW outbound", ["Test"], ["Service"], [("Test", "Service", 40, null, null, null), ("Service", "api.psp.example", 25, null, null, null), ("Service", "risk.example", 12, null, null, null)] },
        { "an ingested UI test", ["User"], ["web"], [("User", "web", 20, "User", null, null), ("web", "Orders API", 35, null, null, null), ("Orders API", "Postgres", 60, null, "PostgreSQL", null)] },
        { "two services under test", ["Caller"], ["Gateway", "Orders API"], [("Caller", "Gateway", 60, null, null, "TestContext"), ("Caller", "Orders API", 40, null, null, "TestContext"), ("Gateway", "Orders API", 30, null, null, "RequestHeader"), ("Orders API", "Postgres", 80, null, "PostgreSQL", "RequestHeader")] },
        { "trackers left at the default caller name", ["Caller"], ["Breakfast Provider"], [("Caller", "Breakfast Provider", 100, null, null, "TestContext"), ("Caller", "CosmosDB", 80, null, "CosmosDB", "RequestHeader"), ("Caller", "Cow Service", 30, null, null, "RequestHeader")] },
        { "the test seeds a store through a client of its own", ["Caller"], ["Orders API"], [("Caller", "Orders API", 50, null, null, "TestContext"), ("Caller", "Postgres", 200, null, "PostgreSQL", "TestContext"), ("Orders API", "Postgres", 90, null, "PostgreSQL", "RequestHeader")] },
        { "the test calls only stores", ["Caller"], [], [("Caller", "Postgres", 20, null, "PostgreSQL", "TestContext")] },
        { "a consumer test: only a broker calls the service", [], [], [("Kafka Broker", "Orders Worker", 15, "MessageQueue", null, "Scope"), ("Orders Worker", "Postgres", 15, null, "PostgreSQL", "Scope")] },
        { "two test clients with different caller names", ["Admin Client", "Caller"], ["Orders API"], [("Caller", "Orders API", 70, null, null, "TestContext"), ("Admin Client", "Orders API", 20, null, null, "TestContext"), ("Orders API", "Postgres", 90, null, "PostgreSQL", "RequestHeader")] },
        { "a scheduler, not the test, also calls the service", ["Caller"], ["Orders API"], [("Caller", "Orders API", 70, null, null, "TestContext"), ("Scheduler", "Orders API", 30, null, null, "Scope"), ("Orders API", "Postgres", 90, null, "PostgreSQL", "RequestHeader")] },
        { "the in-process subject reads as an external-service lane", ["Test"], ["Pricing Engine"], [("Test", "Pricing Engine", 30, null, null, null), ("Test", "Postgres", 50, null, "PostgreSQL", null)] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Infers_each_shape_as_the_harness_measured(string shape, string[] tests, string[] subjects, (string, string, int, string?, string?, string?)[] edges)
    {
        var table = Resolve(Calls(edges));

        Assert.True(tests.SequenceEqual(table.Parties.Where(p => p.Kind == PartyKind.Test).Select(p => p.Name)), shape);
        Assert.True(subjects.SequenceEqual(table.Parties.Where(p => p.Kind == PartyKind.UnderTest).Select(p => p.Name)), shape);
        Assert.All(table.Parties.Where(p => p.Kind is not PartyKind.Unknown), p => Assert.Equal(PartyKindSource.Inferred, p.KindSource));
    }

    [Theory]
    [InlineData("CosmosDB")]
    [InlineData("redis")]
    [InlineData("AtlasDataApi")]
    [InlineData("S3")]
    public void A_store_is_never_the_subject(string category)
    {
        var table = Resolve(Calls(("Caller", "Store", 50, null, category, "TestContext"), ("Caller", "Orders API", 10, null, null, "TestContext")));

        Assert.Equal(PartyKind.UnderTest, Row(table, "Orders API").Kind);
        Assert.Equal(PartyKind.Unknown, Row(table, "Store").Kind);
    }
}
