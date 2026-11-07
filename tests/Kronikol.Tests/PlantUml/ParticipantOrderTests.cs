using System.Net;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// R1 of plans/PARTICIPANT_ORDER_PLAN.md (#228, 7): with the run's roles, a sequence diagram declares the test's
/// parties, then the parties under test, then the rest in the order of the first call; with none, today's order. Only
/// the declaration lines move, each as today's loop writes it.
/// </summary>
public partial class ParticipantOrderTests
{
    private const string TestId = "participant-order";

    private static RequestResponseLog Log(string caller, string service, RequestResponseType type, Guid id, Guid trace,
        string? category, string? callerCategory, string content) =>
        new(TestName: "Participant order", TestId: TestId, Method: HttpMethod.Post, Content: content,
            Uri: new Uri("http://example.com/call"), Headers: [], ServiceName: service, CallerName: caller, Type: type,
            TraceId: trace, RequestResponseId: id, TrackingIgnore: false,
            StatusCode: type == RequestResponseType.Response ? HttpStatusCode.OK : null,
            DependencyCategory: category, CallerDependencyCategory: callerCategory);

    // One call: its request and its response.
    private static IEnumerable<RequestResponseLog> Call(string caller, string service, string? category = null,
        string? callerCategory = null, string content = "{}")
    {
        var id = Guid.NewGuid();
        var trace = Guid.NewGuid();
        yield return Log(caller, service, RequestResponseType.Request, id, trace, category, callerCategory, content);
        yield return Log(caller, service, RequestResponseType.Response, id, trace, category, callerCategory, content);
    }

    private static RequestResponseLog ActionStart() =>
        new(TestName: TestId, TestId: TestId, Method: "", Content: "", Uri: new Uri("http://override.com"), Headers: [],
            ServiceName: "", CallerName: "", Type: RequestResponseType.Request, TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(), TrackingIgnore: false)
        {
            IsActionStart = true
        };

    // The issue's scenario (2.1): the test makes a folder and commits files, runs the app, and the app reads git and
    // keeps a file on the disk.
    private static List<RequestResponseLog> Repro(bool withBoundary = false) =>
    [
        .. Call("Caller", "disk", "Database"),
        .. Call("Caller", "git", "Database"),
        .. withBoundary ? [ActionStart()] : Array.Empty<RequestResponseLog>(),
        .. Call("Caller", "app"),
        .. Call("app", "git", "Database"),
        .. Call("app", "disk", "Database"),
    ];

    private static DiagramRoles Roles(string[]? test = null, string[]? underTest = null) =>
        new((test ?? []).ToHashSet(StringComparer.Ordinal), (underTest ?? []).ToHashSet(StringComparer.Ordinal));

    private static List<string> Fragments(List<RequestResponseLog> logs, DiagramRoles? roles, bool separateSetup = false,
        int maxEncodedDiagramLength = PlantUmlCreator.DefaultMaxEncodedDiagramLength) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestIdCore(logs, PlantUmlCreator.DefaultServerRendererUrl, null, null, null,
                null, null, null, null, PlantUmlCreator.DefaultMaxUrlLength, separateSetup, highlightSetup: true,
                setupHighlightColor: null, lazyLoadImages: true, FocusEmphasis.Bold, FocusDeEmphasis.LightGray,
                plantUmlTheme: null, internalFlowTracking: false, maxEncodedDiagramLength, truncateNotesAfterLines: 0,
                excludeAllHeaders: false, sequenceDiagramArrowColors: true, sequenceDiagramParticipantColors: false,
                dependencyColors: null, serviceTypeOverrides: null, GraphQlBodyFormat.FormattedWithMetadata,
                clientSideSplitting: false, collapseConsecutiveIdenticalCalls: false, collapseThreshold: 2,
                maxArrowsPerDiagram: null, PlantUmlCreator.DefaultNoteWrapWidth, systemUiFont: false, roles)
            .Single().PlantUmls.Select(p => p.PlainText).ToList();

    private static string Source(List<RequestResponseLog> logs, DiagramRoles? roles, bool separateSetup = false) =>
        Fragments(logs, roles, separateSetup).Single();

    [GeneratedRegex("""^\w+ "[^"]*" as (?<alias>\w+).*$""", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    private static string[] Aliases(string source) =>
        Declaration().Matches(source).Select(m => m.Groups["alias"].Value).ToArray();

    private static string[] DeclarationLines(string source) =>
        Declaration().Matches(source).Select(m => m.Value.TrimEnd('\r')).ToArray();

    private static string WithoutDeclarations(string source) => Declaration().Replace(source, "");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void T1_the_issues_scenario_with_app_under_test_draws_app_after_the_test(bool separateSetup)
    {
        var source = Source(Repro(withBoundary: separateSetup), Roles(underTest: ["app"]), separateSetup);

        Assert.Equal(["caller", "app", "disk", "git"], Aliases(source));
        Assert.Equal(separateSetup, SetupPartition().IsMatch(source));
    }

    // The Setup partition's own line: every sequence diagram's style block names "partition" too.
    [GeneratedRegex(@"^partition\b.*\bSetup\s*$", RegexOptions.Multiline)]
    private static partial Regex SetupPartition();

    [Fact]
    public void T2_no_roles_keeps_todays_order_and_every_byte()
    {
        var logs = Repro();

        var withNoRoles = Source(logs, roles: null);

        Assert.Equal(["caller", "disk", "git", "app"], Aliases(withNoRoles));
        Assert.Equal(PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Single().PlainText, withNoRoles);
        Assert.Equal(withNoRoles, Source(logs, Roles()));
    }

    [Fact]
    public void T3_a_subject_already_second_changes_no_byte()
    {
        List<RequestResponseLog> logs = [.. Call("Caller", "app"), .. Call("app", "disk", "Database"), .. Call("app", "git", "Database")];

        Assert.Equal(Source(logs, roles: null), Source(logs, Roles(test: ["Caller"], underTest: ["app"])));
    }

    [Fact]
    public void T4_a_test_whose_calls_carry_no_subject_changes_no_byte()
    {
        var logs = Repro();

        Assert.Equal(Source(logs, roles: null), Source(logs, Roles(test: ["Caller"], underTest: ["elsewhere"])));
    }

    [Fact]
    public void T5_two_parties_under_test_follow_the_test_in_the_order_of_their_first_call()
    {
        List<RequestResponseLog> logs =
        [
            .. Call("Caller", "disk", "Database"),
            .. Call("Caller", "worker"),
            .. Call("Caller", "api"),
            .. Call("api", "disk", "Database"),
        ];

        Assert.Equal(["caller", "worker", "api", "disk"], Aliases(Source(logs, Roles(underTest: ["api", "worker"]))));
    }

    [Fact]
    public void T6_every_fragment_of_a_split_diagram_keeps_the_whole_tests_order()
    {
        // Varied content, so that deflate cannot shrink the diagram under the limit (as PlantUmlCreatorTests' split facts).
        static string Payload() => $"{{{string.Join(", ", Enumerable.Range(0, 10).Select(n => $"\"{Guid.NewGuid():N}\": {n}"))}}}";
        List<RequestResponseLog> logs =
        [
            .. Call("Caller", "disk", "Database", content: Payload()),
            .. Call("Caller", "git", "Database", content: Payload()),
            .. Call("Caller", "app", content: Payload()),
            .. Call("app", "git", "Database", content: Payload()),
            .. Call("Caller", "disk", "Database", content: Payload()),
            .. Call("app", "disk", "Database", content: Payload()),
        ];
        string[] order = ["caller", "app", "disk", "git"];

        var fragments = Fragments(logs, Roles(underTest: ["app"]), maxEncodedDiagramLength: 1200);

        Assert.True(fragments.Count > 2, $"the premise: the diagram is drawn in parts ({fragments.Count})");
        foreach (var fragment in fragments)
        {
            var aliases = Aliases(fragment);
            Assert.Equal(order.Where(aliases.Contains), aliases);
        }
        // Not vacuous: some fragment draws the app beside a store, which today's order puts first.
        Assert.Contains(fragments, f => Aliases(f).Contains("app") && Aliases(f).Intersect(["disk", "git"]).Any());
    }

    [Theory]
    [InlineData("App")]
    [InlineData("app")]
    public void T7_a_role_given_under_either_raw_name_of_one_participant_moves_it(string underTest)
    {
        List<RequestResponseLog> logs =
        [
            .. Call("Caller", "disk", "Database"),
            .. Call("Caller", "App"),
            .. Call("app", "disk", "Database"),
        ];

        var source = Source(logs, Roles(underTest: [underTest]));

        Assert.Equal(["caller", "app", "disk"], Aliases(source));
        Assert.Contains("participant \"App\" as app", source, StringComparison.Ordinal);
    }

    [Fact]
    public void T8_a_subject_that_is_the_pure_caller_stays_first_drawn_as_a_service_not_the_actor()
    {
        // R2 (F2): the actor is the test's shape, so a declaring run draws its service under test as a box.
        List<RequestResponseLog> logs = [.. Call("app", "disk", "Database"), .. Call("app", "git", "Database")];

        var source = Source(logs, Roles(underTest: ["app"]));

        Assert.Equal(["app", "disk", "git"], Aliases(source));
        Assert.Equal("participant \"app\" as app", DeclarationLines(source)[0]);
        Assert.DoesNotContain(DeclarationLines(source), l => l.StartsWith("actor ", StringComparison.Ordinal));
        // a run that declares nothing still draws it as before
        Assert.Equal("actor \"app\" as app", DeclarationLines(Source(logs, roles: null))[0]);
    }

    [Fact]
    public void T8c_a_pure_caller_the_run_holds_as_neither_the_test_nor_under_test_keeps_its_actor()
    {
        // R2 takes the actor from a party under test only: a stub that starts the scenario, declared a double, keeps it.
        List<RequestResponseLog> logs = [.. Call("stub", "app"), .. Call("app", "disk", "Database")];

        Assert.Equal(["actor \"stub\" as stub", "participant \"app\" as app", "database \"disk\" as disk"],
            DeclarationLines(Source(logs, Roles(underTest: ["app"]))));
    }

    [Fact]
    public void T8b_a_pure_caller_with_a_category_keeps_its_categorys_shape_whatever_its_role()
    {
        List<RequestResponseLog> logs = [.. Call("broker", "app", callerCategory: "Kafka"), .. Call("app", "disk", "Database")];

        Assert.Equal(Source(logs, roles: null), Source(logs, Roles(underTest: ["app", "broker"])));
    }

    [Fact]
    public void T9_a_second_test_party_follows_the_pure_caller_and_precedes_the_subject()
    {
        List<RequestResponseLog> logs =
        [
            .. Call("Caller", "disk", "Database"),
            .. Call("Caller", "app"),
            .. Call("app", "callback"),
        ];

        var source = Source(logs, Roles(test: ["Caller", "callback"], underTest: ["app"]));

        Assert.Equal(["caller", "callback", "app", "disk"], Aliases(source));
        Assert.StartsWith("actor \"Caller\" as caller", DeclarationLines(source)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void T10_only_the_declaration_lines_move_each_as_today()
    {
        var logs = Repro();
        var today = Source(logs, roles: null);

        var moved = Source(logs, Roles(test: ["Caller"], underTest: ["app"]));

        Assert.NotEqual(today, moved);
        Assert.Equal(WithoutDeclarations(today), WithoutDeclarations(moved));
        Assert.Equal(DeclarationLines(today).Order(StringComparer.Ordinal), DeclarationLines(moved).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void T11_with_no_pure_caller_the_test_parties_then_the_subject_then_the_rest()
    {
        // Every caller is also called, so there is no pure caller.
        List<RequestResponseLog> logs =
        [
            .. Call("app", "disk", "Database"),
            .. Call("runner", "app"),
            .. Call("app", "runner"),
        ];
        var today = Source(logs, roles: null);

        var moved = Source(logs, Roles(test: ["runner"], underTest: ["app"]));

        Assert.Equal(["actor \"app\" as app", "database \"disk\" as disk", "participant \"runner\" as runner"], DeclarationLines(today));
        // R2: in a declaring run the actor is the test's party, and the subject a box
        Assert.Equal(["actor \"runner\" as runner", "participant \"app\" as app", "database \"disk\" as disk"], DeclarationLines(moved));
    }

    [Fact]
    public void T12_with_no_pure_caller_only_the_first_caller_without_a_category_is_an_actor()
    {
        // F7: the loop never marked its actor drawn, so every such caller met before it was called was an actor.
        List<RequestResponseLog> logs =
        [
            .. Call("worker", "disk", "Database"),
            .. Call("scheduler", "worker"),
            .. Call("worker", "scheduler"),
        ];

        Assert.Equal(["actor \"worker\" as worker", "database \"disk\" as disk", "participant \"scheduler\" as scheduler"],
            DeclarationLines(Source(logs, roles: null)));
    }

    [Fact]
    public void T14_in_a_declaring_run_with_no_pure_caller_the_actor_is_the_test_party()
    {
        List<RequestResponseLog> logs =
        [
            .. Call("worker", "disk", "Database"),
            .. Call("scheduler", "worker"),
            .. Call("worker", "scheduler"),
        ];

        Assert.Equal(["actor \"scheduler\" as scheduler", "participant \"worker\" as worker", "database \"disk\" as disk"],
            DeclarationLines(Source(logs, Roles(test: ["scheduler"], underTest: ["worker"]))));
    }

    [Fact]
    public void T13_an_undeclared_gateway_the_test_calls_on_its_way_to_the_subject_moves_right_of_it()
    {
        // The limit of 5.1, pinned so that a change to it is deliberate.
        List<RequestResponseLog> logs =
        [
            .. Call("Caller", "gateway"),
            .. Call("gateway", "app"),
            .. Call("app", "disk", "Database"),
        ];

        Assert.Equal(["caller", "app", "gateway", "disk"], Aliases(Source(logs, Roles(test: ["Caller"], underTest: ["app"]))));
    }
}
