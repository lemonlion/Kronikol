using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #184 R1 (plans/QUERY_FOR_PROGRAMS_PLAN.md 5.1): a scenario named by the id its producer gave it, the scenario row made
/// whole, and one scenario resolver for every verb that takes a scenario. A host checking its own run knows its tests'
/// ids; it cannot know an ordinal in advance, and <c>sid:</c> is a hash of the name that two tests can share.
/// </summary>
public class QueryTestIdTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A JUnit unique id, as Kronikol4J writes one: slashes, colons, brackets, parentheses and a space.</summary>
    private const string JUnitId = "[engine:junit-jupiter]/[class:shop.OrdersTest]/[method:lists orders(java.lang.String)]";

    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-test-id").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // s0 t-1, s1 the JUnit id (the same name as s0, so the same stableId), s2 t-3, s3 t-9#1 (as a merge renames a
    // colliding id), s4 and s5 both "dup" once the file is written (a merged report that kept two runs of one scenario).
    private static Feature[] Features(bool cancelFails = false) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            SourceFile = "Orders.feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t-1", DisplayName = "Lists orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true, Rule = "Listing", SourceFile = "OrdersTests.cs", SourceLine = 12
                },
                new Scenario
                {
                    Id = JUnitId, DisplayName = "Lists orders", Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(2),
                    ErrorMessage = "boom", ErrorStackTrace = "   at Shop.OrdersTests.Lists_orders() in C:\\src\\OrdersTests.cs:line 40"
                },
                new Scenario
                {
                    Id = "t-3", DisplayName = "Cancels an order", Duration = TimeSpan.FromSeconds(1),
                    Result = cancelFails ? ExecutionResult.Failed : ExecutionResult.Passed, ErrorMessage = cancelFails ? "gone" : null
                },
                new Scenario { Id = "t-9#1", DisplayName = "Refunds an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                new Scenario { Id = "dup", DisplayName = "Ships an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                new Scenario { Id = "dup-again", DisplayName = "Ships an order again", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }
            ]
        }
    ];

    private static RequestResponseLog[] Call(string testName, string testId, string caller, HttpMethod method, string uri, int second, string? response)
    {
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var request = new RequestResponseLog(testName, testId, method, null, new Uri(uri), [], "orders-api", caller, RequestResponseType.Request,
            trace, pair, false) { Timestamp = Start.AddSeconds(second) };
        return response is null
            ? [request]
            : [request, new RequestResponseLog(testName, testId, method, response, new Uri(uri), [], "orders-api", caller, RequestResponseType.Response,
                trace, pair, false, HttpStatusCode.OK) { Timestamp = Start.AddSeconds(second).AddMilliseconds(20) }];
    }

    private static RequestResponseLog[] Logs() =>
    [
        // s0: one answered call and one with no response, so three records and two calls
        .. Call("Lists orders", "t-1", "orders-ui", HttpMethod.Get, "http://orders-api/orders", 1, """{"status":"open"}"""),
        .. Call("Lists orders", "t-1", "orders-ui", HttpMethod.Post, "http://orders-api/orders/audit", 2, null),
        .. Call("Lists orders", JUnitId, "admin-ui", HttpMethod.Get, "http://orders-api/orders?all", 3, """{"status":"all"}"""),
        .. Call("Cancels an order", "t-3", "admin-ui", HttpMethod.Delete, "http://orders-api/orders/1", 4, """{"status":"cancelled"}""")
    ];

    private string Write(string fileName = "TestRunReport.json", bool cancelFails = false)
    {
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams =
        [
            new("t-1", "first", "@startuml\nparticipant A\nA -> A : first\n@enduml"),
            new("t-1", "second", "@startuml\nparticipant B\nB -> B : second\n@enduml")
        ];
        var written = ReportGenerator.GenerateTestRunReportData(Features(cancelFails),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "TestId_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams, Logs());
        var path = Path.Combine(_directory, fileName);
        File.Move(written, path, overwrite: true);

        // A merged report keeps both runs of a scenario that ran in two shards under one id; no single run writes that.
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var scenario in root["features"]!.AsArray().SelectMany(f => f!["scenarios"]!.AsArray()))
            if ((string?)scenario!["id"] == "dup-again")
                scenario["id"] = "dup";
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    private static (string Output, string Error, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static JsonElement Json(params string[] args)
    {
        var (output, error, exit) = Run([.. args, "--json"]);
        Assert.True(exit == 0, error);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    private static JsonElement Item(JsonElement envelope, string address) =>
        envelope.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("address").GetString() == address);

    [Fact]
    public void Each_of_two_tests_that_share_a_name_is_reached_by_its_id_where_sid_is_refused()
    {
        var report = Write();
        var rows = Json("scenarios", report);
        var sid = Item(rows, "s0").GetProperty("stableId").GetString();
        Assert.Equal(sid, Item(rows, "s1").GetProperty("stableId").GetString());
        Assert.Equal(2, Item(rows, "s0").GetProperty("stableIdShared").GetInt32());

        var (_, sidError, sidExit) = Run("steps", report, "sid:" + sid);
        Assert.Equal(2, sidExit);
        Assert.Contains("share stableId", sidError, StringComparison.Ordinal);

        var (first, firstError, firstExit) = Run("steps", report, "id:t-1");
        Assert.True(firstExit == 0, firstError);
        Assert.StartsWith("s0 ", first, StringComparison.Ordinal);

        var (second, secondError, secondExit) = Run("steps", report, "id:" + JUnitId);
        Assert.True(secondExit == 0, secondError);
        Assert.StartsWith("s1 ", second, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_holding_slashes_colons_brackets_and_spaces_is_taken_whole()
    {
        // No child forms: id:a/i3 is the scenario whose id is a/i3, so a JUnit id is one address and not a path under one.
        var report = Write();

        var calls = Json("interactions", report, "id:" + JUnitId);

        var call = Assert.Single(calls.GetProperty("items").EnumerateArray());
        Assert.Equal("s1/i0", call.GetProperty("address").GetString());
        Assert.Equal("admin-ui", call.GetProperty("caller").GetString());
        Assert.True(Address.TryParse("id:" + JUnitId, out var address));
        Assert.Equal(AddressKind.TestId, address.Kind);
        Assert.Equal(JUnitId, address.TestId);
        Assert.Equal("id:" + JUnitId, address.ToString());
    }

    [Fact]
    public void An_id_is_matched_exactly()
    {
        var report = Write();

        Assert.Equal(2, Run("steps", report, "id:T-1").Exit);
        Assert.Equal(2, Run("steps", report, "id:t-").Exit);
        Assert.Equal(2, Run("steps", report, "id: t-1").Exit);
        Assert.False(Address.TryParse("id:", out _));
    }

    [Fact]
    public void An_id_no_scenario_has_is_refused_with_where_the_ids_are_listed()
    {
        var report = Write();

        var (output, error, exit) = Run("steps", report, "id:nope");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("No scenario with id nope in TestRunReport.json", error, StringComparison.Ordinal);
        Assert.Contains("scenarios --json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_a_merge_renamed_is_named_in_the_refusal()
    {
        // kronikol merge renames the later of two colliding ids to <id>#N; the producer's own id then matches nothing.
        var report = Write();

        var (_, error, exit) = Run("steps", report, "id:t-9");

        Assert.Equal(2, exit);
        Assert.Contains("No scenario with id t-9 in", error, StringComparison.Ordinal);
        Assert.Contains("id:t-9#1", error, StringComparison.Ordinal);
        Assert.Contains("s3", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_reports_renamed_id_is_its_own_address()
    {
        // NUnit's ids are a per-process counter, so two shards mint one id for different tests, and the merge renames the
        // later to <id>#1 (MergeableReportMerger.DisambiguateRuntimeIds). Written by the real merger and the real writer.
        static MergeableReport Shard(string name) => new()
        {
            KronikolVersion = "5.22.0",
            StartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
            Features = [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "0-1287", DisplayName = name, Result = ExecutionResult.Passed }] }]
        };
        var merged = MergeableReportMerger.Merge([Shard("Lists orders"), Shard("Cancels an order")]);
        var written = ReportGenerator.GenerateTestRunReportData(merged.Features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Merged_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, []);
        var report = Path.Combine(_directory, "Merged.json");
        File.Move(written, report, overwrite: true);

        var ids = Json("scenarios", report).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).Order(StringComparer.Ordinal);
        Assert.Equal(["0-1287", "0-1287#1"], ids);

        var (renamed, error, exit) = Run("steps", report, "id:0-1287#1");
        Assert.True(exit == 0, error);
        Assert.Contains("Cancels an order", renamed.Split('\n')[0], StringComparison.Ordinal);

        // The merge kept 0-1287 on the first shard's test, so the id as NUnit minted it now names two tests: refused, both
        // listed, where it answered for the first with nothing said.
        var (original, originalError, originalExit) = Run("steps", report, "id:0-1287");
        Assert.Equal(2, originalExit);
        Assert.Equal("", original);
        Assert.Contains("2 scenarios answer to id 0-1287", originalError, StringComparison.Ordinal);
        Assert.Contains("id:0-1287#1", originalError, StringComparison.Ordinal);
        Assert.Contains("Lists orders", originalError, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_two_scenarios_carry_is_refused_with_both_listed()
    {
        var report = Write();

        var (_, error, exit) = Run("steps", report, "id:dup");

        Assert.Equal(2, exit);
        Assert.Contains("2 scenarios answer to id dup", error, StringComparison.Ordinal);
        Assert.Contains("s4", error, StringComparison.Ordinal);
        Assert.Contains("s5", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_row_carries_the_id_and_every_fact_the_engine_holds_about_it()
    {
        var report = Write();

        var row = Item(Json("scenarios", report), "s0");

        Assert.Equal("t-1", row.GetProperty("id").GetString());
        Assert.Equal(3, row.GetProperty("interactions").GetInt32());
        Assert.Equal(2, row.GetProperty("calls").GetInt32());
        Assert.Equal(2, row.GetProperty("diagrams").GetInt32());
        Assert.Equal("OrdersTests.cs", row.GetProperty("sourceFile").GetString());
        Assert.Equal(12, row.GetProperty("sourceLine").GetInt32());
        Assert.Equal("Listing", row.GetProperty("rule").GetString());
        Assert.True(row.GetProperty("isHappyPath").GetBoolean());
        Assert.Equal("Orders.feature", row.GetProperty("featureSourceFile").GetString());

        var other = Item(Json("scenarios", report), "s2");
        Assert.Equal(1, other.GetProperty("calls").GetInt32());
        Assert.Equal(0, other.GetProperty("diagrams").GetInt32());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("sourceLine").ValueKind);
        Assert.False(other.GetProperty("isHappyPath").GetBoolean());
    }

    [Fact]
    public void The_run_counts_its_calls_beside_its_records()
    {
        var report = Write();

        var run = Json("summary", report).GetProperty("run");

        Assert.Equal(7, run.GetProperty("interactions").GetInt32());
        Assert.Equal(4, run.GetProperty("calls").GetInt32());
    }

    [Fact]
    public void Every_item_that_names_a_failed_scenario_carries_its_id()
    {
        var report = Write();

        var failed = Assert.Single(Json("summary", report).GetProperty("failed").EnumerateArray());
        Assert.Equal(JUnitId, failed.GetProperty("id").GetString());
        Assert.Equal(JUnitId, Item(Json("failures", report), "s1").GetProperty("id").GetString());
        Assert.Equal(JUnitId, Item(Json("repro", report), "s1").GetProperty("id").GetString());
    }

    [Fact]
    public void Failures_jsonl_carries_the_id_failures_json_carries()
    {
        var fromTheTool = Item(Json("failures", Write()), "s1").GetProperty("id").GetString();
        var digest = FailuresDigestGenerator.Generate(Features(), Logs(), "TestRunReport.html", "5.23.0");

        var failure = digest.Jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Single(line => line.GetProperty("kind").GetString() == "failure");
        Assert.Equal(JUnitId, failure.GetProperty("id").GetString());
        Assert.Equal(fromTheTool, failure.GetProperty("id").GetString());
        var names = failure.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(names.IndexOf("address") + 1, names.IndexOf("id"));
    }

    [Fact]
    public void A_diff_row_carries_no_id_because_it_names_two_runs()
    {
        // An id belongs to one run; diff matches the two on the cross-run stableId.
        var old = Write("old.json");
        var now = Write("new.json", cancelFails: true);

        var diff = Json("diff", old, now);

        var row = Assert.Single(diff.GetProperty("items").EnumerateArray(), r => r.GetProperty("stableId").GetString() is not null
            && r.TryGetProperty("scenario", out var name) && name.GetString() == "Cancels an order");
        Assert.False(row.TryGetProperty("id", out _));
    }

    [Theory]
    [InlineData("services")]
    [InlineData("parties")]
    public void Services_and_parties_take_a_scenario_by_sid_and_by_id(string verb)
    {
        var report = Write();
        var sid = Item(Json("scenarios", report), "s2").GetProperty("stableId").GetString();

        Assert.Equal(Json(verb, report, "s2").GetProperty("items").ToString(), Json(verb, report, "sid:" + sid).GetProperty("items").ToString());
        Assert.Equal(Json(verb, report, "s2").GetProperty("items").ToString(), Json(verb, report, "id:t-3").GetProperty("items").ToString());

        var (_, error, exit) = Run(verb, report, "s2/0");
        Assert.Equal(2, exit);
        Assert.Contains("Not a scenario address", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_takes_a_scenario_by_sid_and_by_id()
    {
        var report = Write();
        var sid = Item(Json("scenarios", report), "s2").GetProperty("stableId").GetString();

        var byOrdinal = Run("values", report, "s2", "--path", "$.status");
        var bySid = Run("values", report, "sid:" + sid, "--path", "$.status");
        var byId = Run("values", report, "id:t-3", "--path", "$.status");

        Assert.True(byOrdinal.Exit == 0, byOrdinal.Error);
        Assert.Contains("cancelled", byOrdinal.Output, StringComparison.Ordinal);
        Assert.Equal(byOrdinal, bySid);
        Assert.Equal(byOrdinal, byId);
        Assert.Equal(2, Run("values", report, "s2/0", "--path", "$.status").Exit);
    }

    [Fact]
    public void Compare_takes_a_scenario_by_sid_and_by_id()
    {
        var report = Write();
        var sid = Item(Json("scenarios", report), "s2").GetProperty("stableId").GetString();

        var (output, error, exit) = Run("compare", report, "sid:" + sid, "id:t-1");

        Assert.True(exit == 0, error);
        var lines = output.Split('\n');
        Assert.StartsWith("s2 Cancels an order", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("s0 Lists orders", lines[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("s0/i0", "http")]
    [InlineData("s0/d0", "diagram")]
    [InlineData("s0/d0/n0", "note")]
    [InlineData("b:00000000", "body")]
    [InlineData("s0/0", "steps")]
    public void Compare_refuses_an_address_that_is_not_a_scenario_and_names_the_verb_that_reads_it(string address, string verb)
    {
        // It read s0/i0 as s0 and answered for the scenario, and a body address as a scenario that was not there.
        var report = Write();

        var (output, error, exit) = Run("compare", report, address, "s2");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains($"`{verb} ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("not in this report", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("steps", "s0/i0", "http")]
    [InlineData("interactions", "s0/i0", "http")]
    [InlineData("flow", "s0/d0", "diagram")]
    [InlineData("scenarios", "s0/d0/n0", "note")]
    [InlineData("failures", "s1/i0", "http")]
    public void A_verb_that_takes_a_scenario_refuses_a_call_diagram_or_note_address_rather_than_widening_it(string verb, string address, string reader)
    {
        // These were read as their scenario: steps s0/i0 answered for all of s0, with nothing said.
        var report = Write();

        var (output, error, exit) = Run(verb, report, address);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains($"`{reader} ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_lists_the_id_form()
    {
        var (output, _, exit) = Run("--describe");

        Assert.Equal(0, exit);
        using var describe = JsonDocument.Parse(output);
        Assert.Contains(describe.RootElement.GetProperty("addresses").EnumerateArray(),
            a => a.GetProperty("form").GetString() == "id:<test id>" && a.GetProperty("example").GetString() == "id:0-1287");
    }

    [Theory]
    [InlineData("s0/i0", "`http <report> s0/i0`")]
    [InlineData("s0/d0", "`diagram <report> s0/d0 --out F`")]
    [InlineData("s0/d0/n0", "`note <report> s0/d0/n0`")]
    [InlineData("s0/1", "`steps s0/1`")]
    public void Grep_with_no_hits_for_an_address_names_the_verb_that_reads_it(string address, string reader)
    {
        // It said `steps s0/i0` opens a call address, which steps refuses since this release.
        var report = Write();

        var (output, error, exit) = Run("grep", report, address);

        Assert.Equal(0, exit);
        Assert.Contains(reader, output + error, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_with_no_hits_for_an_id_address_says_it_is_one()
    {
        var report = Write();

        var (output, error, exit) = Run("grep", report, "id:t-1");

        Assert.Equal(0, exit);
        Assert.Contains("id:t-1 is an address", output + error, StringComparison.Ordinal);
    }

    // The projection guard (plan 5.1): every fact the engine holds about a scenario reaches the scenarios row, or is
    // listed here with the reason it does not. A property added to ScenarioEntry fails this until it is decided.
    private static readonly Dictionary<string, string> ProjectedAs = new(StringComparer.Ordinal)
    {
        [nameof(ScenarioEntry.Address)] = "address",
        [nameof(ScenarioEntry.Id)] = "id",
        [nameof(ScenarioEntry.StableId)] = "stableId",
        [nameof(ScenarioEntry.FeatureName)] = "feature",
        [nameof(ScenarioEntry.FeatureSourceFile)] = "featureSourceFile",
        [nameof(ScenarioEntry.Name)] = "scenario",
        [nameof(ScenarioEntry.Rule)] = "rule",
        [nameof(ScenarioEntry.Result)] = "result",
        [nameof(ScenarioEntry.Failed)] = "failed",
        [nameof(ScenarioEntry.IsHappyPath)] = "isHappyPath",
        [nameof(ScenarioEntry.DurationSeconds)] = "durationSeconds",
        [nameof(ScenarioEntry.Interactions)] = "interactions",
        [nameof(ScenarioEntry.Diagrams)] = "diagrams",
        [nameof(ScenarioEntry.ErrorMessage)] = "errorMessage",
        [nameof(ScenarioEntry.Attempt)] = "attempt",
        [nameof(ScenarioEntry.SourceFile)] = "sourceFile",
        [nameof(ScenarioEntry.SourceLine)] = "sourceLine",
        [nameof(ScenarioEntry.EffectiveTags)] = "tags",
        [nameof(ScenarioEntry.ExampleValues)] = "exampleValues",
        // only when the run's first call warmed up, as notATest only on a scenario that is not a test
        [nameof(ScenarioEntry.WarmUpSeconds)] = "warmUpSeconds?",
        [nameof(ScenarioEntry.NotATest)] = "notATest?"
    };

    private static readonly Dictionary<string, string> NotOnTheRow = new(StringComparer.Ordinal)
    {
        [nameof(ScenarioEntry.Steps)] = "the step tree, which steps sN lists",
        [nameof(ScenarioEntry.BackgroundSteps)] = "the step tree, which steps sN lists",
        [nameof(ScenarioEntry.Annotations)] = "listed by annotations sN",
        [nameof(ScenarioEntry.Attachments)] = "listed by attachments sN",
        [nameof(ScenarioEntry.ErrorStackTrace)] = "kilobytes; failures and repro read it as thrownAt",
        [nameof(ScenarioEntry.TimeLeftSeconds)] = "durationSeconds less warmUpSeconds, both on the row",
        [nameof(ScenarioEntry.Tags)] = "the scenario's own tags, merged into tags with the feature's",
        [nameof(ScenarioEntry.FeatureTags)] = "the feature's tags, merged into tags with the scenario's own",
        [nameof(ScenarioEntry.RowTags)] = "tags plus Happy Path, which isHappyPath says",
        [nameof(ScenarioEntry.Ordinal)] = "the N of address",
        [nameof(ScenarioEntry.LegacyLabels)] = "labels as a report before 5.0.0 wrote them, merged into tags",
        [nameof(ScenarioEntry.LegacyCategories)] = "categories as a report before 5.0.0 wrote them, merged into tags"
    };

    internal static IReadOnlyList<string> Unaccounted(Type type, IReadOnlyDictionary<string, string> projected, IReadOnlyDictionary<string, string> excluded) =>
        // Internal ones too: a member a patch adds is made internal here, so it adds no public surface (NotATest).
        type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !projected.ContainsKey(name) && !excluded.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Every_fact_the_engine_holds_about_a_scenario_reaches_its_row_or_is_excluded_for_a_reason()
    {
        Assert.Empty(Unaccounted(typeof(ScenarioEntry), ProjectedAs, NotOnTheRow));
        Assert.Empty(ProjectedAs.Keys.Intersect(NotOnTheRow.Keys));

        var row = Item(Json("scenarios", Write()), "s0");
        foreach (var member in ProjectedAs.Values.Where(m => !m.EndsWith('?')))
            Assert.True(row.TryGetProperty(member, out _), $"the scenarios row has no {member}");
        foreach (var listed in ProjectedAs.Keys.Concat(NotOnTheRow.Keys))
            Assert.NotNull(typeof(ScenarioEntry).GetProperty(listed, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
    }

    private sealed class WithANewFact
    {
        public string Id { get; set; } = "";
        public string Owner { get; set; } = "";
        internal string? OutlineId { get; set; }
    }

    [Fact]
    public void The_projection_guard_names_a_fact_nobody_decided()
    {
        Assert.Equal(["OutlineId", "Owner"], Unaccounted(typeof(WithANewFact), new Dictionary<string, string> { ["Id"] = "id" }, new Dictionary<string, string>()));
    }
}
