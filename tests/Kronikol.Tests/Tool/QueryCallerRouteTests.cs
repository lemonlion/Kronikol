using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kronikol.History;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #192 R2 (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.4): who made a call and what it was, as dimensions of
/// <c>interactions --group-by</c>, the caller on <c>--json</c> rows, and <c>--phase</c>. The issue asked to tell the
/// test's own seeding from the subject's calls, and one endpoint's operations apart.
/// </summary>
public class QueryCallerRouteTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-caller-route").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = "t0", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }]
        }
    ];

    private static RequestResponseLog[] Call(string caller, string service, OneOf<HttpMethod, string> method, string uri, string? body, int second,
        TestPhase phase = TestPhase.Unknown, string? category = null)
    {
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        return
        [
            new RequestResponseLog("t0", "t0", method, body, new Uri(uri), [], service, caller, RequestResponseType.Request, trace, pair, false,
                DependencyCategory: category) { Timestamp = Start.AddSeconds(second), Phase = phase },
            new RequestResponseLog("t0", "t0", method, "{}", new Uri(uri), [], service, caller, RequestResponseType.Response, trace, pair, false,
                HttpStatusCode.OK, DependencyCategory: category) { Timestamp = Start.AddSeconds(second).AddMilliseconds(20), Phase = phase }
        ];
    }

    private string Write(RequestResponseLog[] logs, string fileName = "TestRunReport.json", IReadOnlyList<HistoryShapeTemplate>? templates = null,
        string? kronikolVersion = null)
    {
        var written = ReportGenerator.GenerateTestRunReportData(Features(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Route_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs, shapeTemplates: templates);
        var path = Path.Combine(_directory, fileName);
        File.Move(written, path, overwrite: true);
        if (kronikolVersion is not null)
        {
            // A report as an earlier release wrote it: the version is what says whether the file could record its rules.
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            root["kronikolVersion"] = kronikolVersion;
            File.WriteAllText(path, root.ToJsonString());
        }
        return path;
    }

    private static RequestResponseLog[] Get(string uri, int second) => Call("orders-api", "Cache", HttpMethod.Get, uri, null, second);

    private static (string Output, string Error, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Group_by_caller_separates_the_seeding_from_the_subjects_calls()
    {
        // The issue's first clause: the test seeds the database itself, then the subject reads it. By service alone the
        // two are one bucket; by caller the seeding is the test's.
        var report = Write([
            .. Call("Test", "OrdersDb", "INSERT", "sql:///orders/Orders", "INSERT INTO Orders VALUES (1)", 1, category: "SQL"),
            .. Call("Test", "OrdersDb", "INSERT", "sql:///orders/Orders", "INSERT INTO Orders VALUES (2)", 2, category: "SQL"),
            .. Call("orders-api", "OrdersDb", "SELECT", "sql:///orders/Orders", "SELECT * FROM Orders", 3, category: "SQL")]);

        var (output, error, exit) = Run("interactions", report, "--group-by", "caller,service");

        Assert.True(exit == 0, error);
        Assert.Matches(@"^Test\s+OrdersDb\s+2\s", Assert.Single(Lines(output), l => l.StartsWith("Test ", StringComparison.Ordinal)));
        Assert.Matches(@"^orders-api\s+OrdersDb\s+1\s", Assert.Single(Lines(output), l => l.StartsWith("orders-api ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_caller_is_one_caller_whatever_case_it_is_named_in()
    {
        // Plan 3.7 (Q14): a caller value folds as a service does, named by the spelling the first call gives it.
        var report = Write([
            .. Call("orders-api", "Payments", HttpMethod.Post, "http://payments/charges", "{}", 1),
            .. Call("Orders-Api", "Payments", HttpMethod.Post, "http://payments/charges", "{}", 2)]);

        var (output, error, exit) = Run("interactions", report, "--group-by", "caller");

        Assert.True(exit == 0, error);
        Assert.Matches(@"^orders-api\s+2\s", Assert.Single(Lines(output), l => l.StartsWith("orders-api", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Interactions_json_rows_name_the_caller()
    {
        var report = Write([.. Call("orders-api", "Payments", HttpMethod.Post, "http://payments/charges", "{}", 1)]);

        var (output, error, exit) = Run("interactions", report, "--json");

        Assert.True(exit == 0, error);
        var item = Assert.Single(JsonDocument.Parse(output).RootElement.GetProperty("items").EnumerateArray());
        var names = item.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal("orders-api", item.GetProperty("caller").GetString());
        Assert.Equal(names.IndexOf("service") + 1, names.IndexOf("caller"));
    }

    [Fact]
    public void Phase_filters_setup_action_and_unknown()
    {
        var report = Write([
            .. Call("Test", "OrdersDb", HttpMethod.Post, "http://orders-db/seed", "{\"id\":1}", 1, TestPhase.Setup),
            .. Call("Test", "Orders", HttpMethod.Post, "http://orders/orders", "{}", 2, TestPhase.Action),
            .. Call("orders-api", "Payments", HttpMethod.Post, "http://payments/charges", "{}", 3)]);

        string[] Services(string phase)
        {
            var (output, error, exit) = Run("interactions", report, "--phase", phase, "--json");
            Assert.True(exit == 0, error);
            return JsonDocument.Parse(output).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("service").GetString()!).ToArray();
        }

        Assert.Equal(["OrdersDb"], Services("setup"));
        Assert.Equal(["Orders"], Services("Action"));
        Assert.Equal(["Payments"], Services("unknown"));
        var refused = Run("interactions", report, "--phase", "teardown");
        Assert.Equal(2, refused.Exit);
        Assert.Contains("setup, action or unknown", refused.Error, StringComparison.Ordinal);
        // values reads the same filter (Q10), and refuses what interactions refuses.
        var (values, valuesError, valuesExit) = Run("values", report, "--path", "$", "--phase", "setup", "--request");
        Assert.True(valuesExit == 0, valuesError);
        Assert.Contains("$ across 1 request bodies", values, StringComparison.Ordinal);
        Assert.Equal(2, Run("values", report, "--path", "$", "--phase", "teardown").Exit);
    }

    [Fact]
    public void The_next_page_keeps_the_phase()
    {
        // An offset counted over the setup calls means nothing over every call.
        var report = Write([
            .. Call("Test", "OrdersDb", "INSERT", "sql:///orders/Orders", "INSERT INTO Orders VALUES (1)", 1, TestPhase.Setup, "SQL"),
            .. Call("Test", "OrdersDb", "INSERT", "sql:///orders/Orders", "INSERT INTO Orders VALUES (2)", 2, TestPhase.Setup, "SQL"),
            .. Call("orders-api", "Payments", HttpMethod.Post, "http://payments/charges", "{}", 3)]);

        var (output, error, exit) = Run("interactions", report, "--phase", "setup", "--limit", "1");

        Assert.True(exit == 0, error);
        Assert.Matches(@"next: .*--phase setup.*--offset 1", output);
    }

    [Fact]
    public void Group_by_route_folds_ids_and_splits_graphql_operations()
    {
        // The query is no part of a route (plan 4.4): the two reads differ in what they expand, and are one operation.
        var report = Write([
            .. Call("Test", "Orders", HttpMethod.Get, "http://orders/orders/1?expand=items", null, 1),
            .. Call("Test", "Orders", HttpMethod.Get, "http://orders/orders/2?expand=lines&page=2", null, 2),
            .. Call("orders-api", "Catalogue", HttpMethod.Post, "http://catalogue/graphql", "{\"query\":\"query GetUser { user(id: 1) { name } }\"}", 3),
            .. Call("orders-api", "Catalogue", HttpMethod.Post, "http://catalogue/graphql", "{\"query\":\"mutation PlaceOrder { placeOrder { id } }\"}", 4)]);

        var (output, error, exit) = Run("interactions", report, "--group-by", "route");

        Assert.True(exit == 0, error);
        Assert.Matches(@"^/orders/\{n\}\s+2\s", Assert.Single(Lines(output), l => l.StartsWith("/orders/", StringComparison.Ordinal)));
        Assert.Matches(@"^/graphql query GetUser\s+1\s", Assert.Single(Lines(output), l => l.Contains("GetUser", StringComparison.Ordinal)));
        Assert.Matches(@"^/graphql mutation PlaceOrder\s+1\s", Assert.Single(Lines(output), l => l.Contains("PlaceOrder", StringComparison.Ordinal)));
    }

    [Fact]
    public void Route_applies_the_runs_recorded_templates()
    {
        // The issue's buckets were "mostly cache keys and ids": the consumer's rule folds them, as it did the run's history.
        RequestResponseLog[] calls = [.. Get("http://cache/cache/abc-1", 1), .. Get("http://cache/cache/def-2", 2)];
        var with = Write(calls, "WithRules.json", templates: [new(@"(?<=/cache/)[a-z]+-\d+", "{key}")]);
        var without = Write(calls, "WithoutRules.json");

        var (output, error, exit) = Run("interactions", with, "--group-by", "route");
        var (plain, _, _) = Run("interactions", without, "--group-by", "route");

        Assert.True(exit == 0, error);
        Assert.Matches(@"^/cache/\{key\}\s+2\s", Assert.Single(Lines(output), l => l.StartsWith("/cache/", StringComparison.Ordinal)));
        Assert.Equal(2, Lines(plain).Count(l => l.StartsWith("/cache/", StringComparison.Ordinal)));
    }

    [Fact]
    public void Route_on_a_report_from_before_the_block_says_so_once()
    {
        RequestResponseLog[] calls = [.. Get("http://cache/cache/abc-1", 1), .. Get("http://cache/cache/def-2", 2), .. Get("http://cache/cache/ghi-3", 3)];
        var old = Write(calls, "Old.json", kronikolVersion: "5.17.0");

        var (output, error, exit) = Run("interactions", old, "--group-by", "route");
        var (byService, serviceError, _) = Run("interactions", old, "--group-by", "service");

        Assert.True(exit == 0, error);
        Assert.Equal(1, Regex.Count(output + error, "route: built-in templating only; this report cannot say whether its run had HistoryShapeTemplates"));
        Assert.DoesNotContain("predates", byService + serviceError, StringComparison.Ordinal);
    }

    [Fact]
    public void Route_on_a_report_without_rules_says_nothing()
    {
        // From the first release that writes the block, a report carries it exactly when its run had rules, so its absence is
        // an answer. Named rather than taken from this build, whose version is the one before the release until it is cut.
        var report = Write([.. Get("http://cache/cache/abc-1", 1)], kronikolVersion: QueryCommand.FirstVersionWithShapeTemplates.ToString(3));

        var (output, error, exit) = Run("interactions", report, "--group-by", "route");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("route:", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_pattern_that_times_out_is_dropped_for_the_rest_of_the_command()
    {
        // A run keeps a timed-out pattern for its next text; a query over thousands of paths cannot afford to. The second
        // call would read /{q} under a pattern still in force.
        var report = Write([.. Get("http://cache/x/" + new string('q', 40) + "!", 1), .. Get("http://cache/cache-7", 2)],
            templates: [new(@"(q+)+$|(?<=/)cache-\d+", "{q}")]);

        var (output, error, exit) = Run("interactions", report, "--group-by", "route");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain(Lines(output), l => l.StartsWith("/{q}", StringComparison.Ordinal));
        Assert.Single(Lines(output), l => l.StartsWith("/cache-", StringComparison.Ordinal));
        Assert.Equal(1, Regex.Count(output + error, "timed out"));
        Assert.Contains("(q+)+$", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_pattern_that_does_not_compile_is_named_and_the_rest_apply()
    {
        var report = Write([.. Get("http://cache/cache/abc-1", 1)], templates: [new("([unclosed", "{x}"), new(@"(?<=/cache/)[a-z]+-\d+", "{key}")]);

        var (output, error, exit) = Run("interactions", report, "--group-by", "route");

        Assert.True(exit == 0, error);
        Assert.Contains("([unclosed", output + error, StringComparison.Ordinal);
        Assert.Single(Lines(output), l => l.StartsWith("/cache/{key}", StringComparison.Ordinal));
    }

    [Fact]
    public void Route_reads_only_statement_and_post_bodies_once_each()
    {
        // A GET is read from its URI and every other call stays index-only; a POST may be GraphQL whatever its endpoint
        // is called, and a statement's head is in its body. Two POSTs with one body are one read.
        var report = Write([
            .. Call("orders-api", "Api", HttpMethod.Post, "http://api/api", "{\"query\":\"query A { a }\"}", 1),
            .. Call("orders-api", "Api", HttpMethod.Post, "http://api/api", "{\"query\":\"query A { a }\"}", 2),
            .. Call("orders-api", "Api", HttpMethod.Post, "http://api/api", "{\"query\":\"query B { b }\"}", 3),
            .. Call("orders-api", "OrdersDb", "SELECT", "sql:///orders/Orders", "SELECT * FROM Orders", 4, category: "SQL"),
            .. Call("orders-api", "Api", HttpMethod.Put, "http://api/api", "{\"query\":\"query C { c }\"}", 5),
            .. Call("orders-api", "Api", HttpMethod.Get, "http://api/items", "{\"query\":\"query D { d }\"}", 6)]);

        int BodyReads(params string[] args)
        {
            var index = ReportScanner.Scan(report);
            var error = new StringWriter();
            var options = QueryOptions.Parse([index.Path, .. args], error)!;
            var writer = new QueryWriter(new StringWriter(), options.MaxBytes);
            Assert.Equal(0, QueryCommand.Interactions(index, options, writer, error));
            return index.BodyReads;
        }

        Assert.Equal(3, BodyReads("--group-by", "route"));
        Assert.Equal(0, BodyReads("--group-by", "service,caller"));
        var (output, _, _) = Run("interactions", report, "--group-by", "route");
        Assert.DoesNotContain("query C", output, StringComparison.Ordinal);
        Assert.Contains(Lines(output), l => l.StartsWith("/orders/Orders SELECT", StringComparison.Ordinal));
    }

    [Fact]
    public void Wide_routes_are_cut_in_text_output()
    {
        var segment = new string('w', 150);
        var report = Write([.. Get("http://cache/" + segment, 1)]);

        var (text, _, _) = Run("interactions", report, "--group-by", "route");
        var (json, error, exit) = Run("interactions", report, "--group-by", "route", "--json");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain(segment, text, StringComparison.Ordinal);
        Assert.Contains(Lines(text), l => l.StartsWith("/www", StringComparison.Ordinal));
        var key = Assert.Single(JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray()).GetProperty("key");
        Assert.Equal("/" + segment, key.GetProperty("route").GetString());
    }

    [Fact]
    public void Route_under_count_is_one_token_with_its_notes_on_stderr()
    {
        // --count prints one token: a caveat goes to stderr under it, as every other note does.
        var report = Write([.. Get("http://cache/cache/abc-1", 1)], templates: [new("([unclosed", "{x}")], kronikolVersion: "5.17.0");

        var (output, error, exit) = Run("interactions", report, "--group-by", "route", "--count");

        Assert.True(exit == 0, error);
        Assert.Equal("1", output.Trim());
        Assert.Contains("([unclosed", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_that_carries_rules_is_never_said_to_be_templated_by_the_built_in_rules_alone()
    {
        // The note is for a report that holds no block and cannot say why; one that holds the block, whatever version wrote
        // it (a build between releases, a port), has its rules applied.
        var report = Write([.. Get("http://cache/cache/abc-1", 1)], templates: [new(@"(?<=/cache/)[a-z]+-\d+", "{key}")], kronikolVersion: "0.1.30");

        var (output, error, exit) = Run("interactions", report, "--group-by", "route");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("built-in templating only", output + error, StringComparison.Ordinal);
        Assert.Single(Lines(output), l => l.StartsWith("/cache/{key}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("service,service")]
    [InlineData("route,caller,Route")]
    public void A_dimension_named_twice_is_refused(string dimensions)
    {
        // A bucket's JSON key is an object of its dimensions, which cannot hold one twice: it threw.
        var report = Write([.. Get("http://cache/cache/abc-1", 1)]);

        var text = Run("interactions", report, "--group-by", dimensions);
        var json = Run("interactions", report, "--group-by", dimensions, "--json");

        Assert.Equal((2, 2), (text.Exit, json.Exit));
        Assert.Contains("named twice", text.Error, StringComparison.Ordinal);
        Assert.Contains("named twice", json.Output + json.Error, StringComparison.Ordinal);
    }
}
