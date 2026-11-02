// A merged run against the run it came from, position by position. Two modes:
//   run <case> <folder> <ledger> <run number> [shard]   one run of a case: one process, one report, one fragment
//   read <ledger> <Kronikol.Tool.dll> <folder>...       the folders' fragments folded, as `history record` folds them, and
//                                                       the folders' reports merged, as `kronikol merge --history` reads
//                                                       them, through the tool's own MergeCommand by reflection
// Published 5.27.13. For the prototype, the same build runs with the prototype's Kronikol.dll beside it, and is handed the
// prototype's tool. merge-round-trip-cases.py drives it. Report content here is made up.
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Net;
using System.Reflection;
using System.Text;
using Kronikol;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

if (args[0] == "read")
    return Read(args[1], args[2], args[3..]);

var (caseName, folder, ledger, runNumber) = (args[1], args[2], args[3], int.Parse(args[4]));
var shard = args.Length > 5 ? args[5] : "";
var last = int.TryParse(Environment.GetEnvironmentVariable("MRT_LAST"), out var lastRun) ? lastRun : 4;
var rows = new List<Feature>();
var options = new ReportConfigurationOptions
{
    ReportsFolderPath = folder,
    PlantUmlRendering = PlantUmlRendering.BrowserJs,
    GenerateMergeableData = true,
    GenerateComponentDiagram = false,
    GenerateSpecificationsReport = false,
    GenerateSpecificationsData = false,
    GenerateTestRunReport = false,
    WriteRunSummaryToConsole = true,
    // A run that sees the ledger reads it, and records what it read: shards in a job that restored it do.
    HistoryFilePath = Environment.GetEnvironmentVariable("MRT_NO_LEDGER") is "1" ? null : ledger,
    WriteHistoryLedger = false,
    SuiteName = "fixture",
};
var at = DateTimeOffset.UtcNow;
const string Sql = DependencyCategories.SQL;

// One call of a scenario.
void Call(string id, string name, OneOf<HttpMethod, string> method, string uri, OneOf<HttpStatusCode, string>? status = null, string? content = null,
    string service = "Svc", string caller = "Caller", string? category = null, RequestResponseMetaType kind = RequestResponseMetaType.Default, UriKind uriKind = UriKind.RelativeOrAbsolute,
    bool ignored = false, bool answer = true)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var parsed = new Uri(uri, uriKind);
    at = at.AddMilliseconds(3);
    RequestResponseLogger.Log(new RequestResponseLog(name, id, method, content, parsed, [], service, caller,
        RequestResponseType.Request, trace, request, ignored, MetaType: kind, DependencyCategory: category) { Timestamp = at });
    if (answer)
        RequestResponseLogger.Log(new RequestResponseLog(name, id, method, null, parsed, [], service, caller,
            RequestResponseType.Response, trace, request, ignored, status ?? HttpStatusCode.OK, DependencyCategory: category) { Timestamp = at.AddMilliseconds(1) });
}

var seconds = 1.0;
Scenario Sc(string id, string name, ExecutionResult result = ExecutionResult.Passed)
{
    seconds += 0.137;
    return new Scenario { Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromSeconds(seconds) };
}

// A scenario holding one call.
Scenario One(string id, string name, OneOf<HttpMethod, string> method, string uri, OneOf<HttpStatusCode, string>? status = null, string? content = null,
    string? category = null, RequestResponseMetaType kind = RequestResponseMetaType.Default, UriKind uriKind = UriKind.RelativeOrAbsolute, string service = "Svc")
{
    Call(id, name, method, uri, status, content, service: service, category: category, kind: kind, uriKind: uriKind);
    return Sc(id, name);
}

Feature F(string name, params Scenario[] scenarios) => new() { DisplayName = name, Scenarios = scenarios };
var control = "http://h/control";

switch (caseName)
{
    case "uri":
    {
        var uris = new (string Label, string Uri, UriKind Kind)[]
        {
            ("control", control, UriKind.Absolute),
            ("space mid path", "http://h/a%20b", UriKind.Absolute),
            ("space end of path", "http://h/search/foo%20", UriKind.Absolute),
            ("space end of path, custom scheme", "spanner://p/foo%20", UriKind.Absolute),
            ("space end of segment, more after", "http://h/foo%20/bar", UriKind.Absolute),
            ("space end of query value", "http://h/q?a=foo%20", UriKind.Absolute),
            ("raw unicode", "http://h/caf" + char.ConvertFromUtf32(0xE9), UriKind.Absolute),
            ("escaped unicode", "http://h/caf%C3%A9", UriKind.Absolute),
            ("invalid utf-8", "http://h/%FF%FE", UriKind.Absolute),
            ("query and fragment", "http://h/a?b=1&c=2#frag", UriKind.Absolute),
            ("trailing slash", "http://h/a/", UriKind.Absolute),
            ("no path", "http://h", UriKind.Absolute),
            ("upper case scheme and host", "HTTP://H/A", UriKind.Absolute),
            ("default port", "http://h:80/a", UriKind.Absolute),
            ("ipv6", "http://[::1]:5000/x", UriKind.Absolute),
            ("ipv6 zone", "http://[fe80::1%25eth0]:5000/x", UriKind.Absolute),
            ("reserved escapes", "http://h/%2F%3F%23%25", UriKind.Absolute),
            ("double escape", "http://h/a%252Fb", UriKind.Absolute),
            ("escaped backslash", "http://h/a%5Cb", UriKind.Absolute),
            ("control escapes", "http://h/a%0Cb%00c", UriKind.Absolute),
            ("replacement char escaped", "http://h/k-%EF%BF%BD-x", UriKind.Absolute),
            ("dots", "http://h/a/../b/./c", UriKind.Absolute),
            ("escaped dots", "http://h/a/%2E%2E/b", UriKind.Absolute),
            ("braces", "http://h/{id}/%7Bx%7D", UriKind.Absolute),
            ("about:blank", "about:blank", UriKind.Absolute),
            ("urn", "urn:a:b", UriKind.Absolute),
            ("mailto", "mailto:a@b", UriKind.Absolute),
            ("spanner", "spanner://projects/p/instances/i/databases/d", UriKind.Absolute),
            ("servicebus queue, mixed case", "servicebus://Orders-Queue/", UriKind.Absolute),
            ("s3 bucket", "s3://Bucket.Name/key%2Fa", UriKind.Absolute),
            ("redis db", "redis://DB3/user:123:profile", UriKind.Absolute),
            ("file", "file:///tmp/x%20y", UriKind.Absolute),
            ("unc", "file://server/share/x", UriKind.Absolute),
        };
        var i = 0;
        rows.Add(F("Uri", uris.Select(u => One("u" + i++, u.Label, HttpMethod.Get, u.Uri, uriKind: u.Kind)).ToArray()));
        break;
    }
    case "uri-relative":
    {
        // A relative URI costs the run its data file while internal flow is on (it reads PathAndQuery), so it is off here.
        options.InternalFlowTracking = false;
        var uris = new (string Label, string Uri, UriKind Kind)[]
        {
            ("control", control, UriKind.Absolute),
            ("relative", "Orders/Place", UriKind.Relative),
            ("rooted relative", "/Orders/Place", UriKind.Relative),
            ("rooted relative with query", "/Orders/Place?x=1", UriKind.Relative),
            ("rooted relative with space", "/Orders/Pl ace", UriKind.Relative),
            ("double-rooted relative", "//host/Orders", UriKind.Relative),
            ("empty relative", "", UriKind.Relative),
            ("query only relative", "?x=1", UriKind.Relative),
            ("relative space end", "Orders ", UriKind.Relative),
            ("relative escape", "a%20b", UriKind.Relative),
        };
        var i = 0;
        rows.Add(F("Uri", uris.Select(u => One("u" + i++, u.Label, HttpMethod.Get, u.Uri, uriKind: u.Kind)).ToArray()));
        break;
    }
    case "method":
        rows.Add(F("Method",
            One("m0", "verb GET", HttpMethod.Get, control),
            One("m1", "custom HttpMethod Patch", new HttpMethod("Patch"), control),
            One("m2", "custom HttpMethod patch", new HttpMethod("patch"), control),
            One("m3", "custom HttpMethod PURGE", new HttpMethod("PURGE"), control),
            One("m4", "custom HttpMethod Purge", new HttpMethod("Purge"), control),
            One("m5", "label Delete", "Delete", control),
            One("m6", "label delete", "delete", control),
            One("m7", "label DELETE", "DELETE", control),
            One("m8", "label DELETE at Event", "DELETE", control, kind: RequestResponseMetaType.Event),
            One("m9", "label Delete at Event", "Delete", control, kind: RequestResponseMetaType.Event),
            One("m10", "label Get (Hit)", "Get (Hit)", control),
            One("m11", "label Publish", "Publish", control),
            One("m12", "label empty", "", control),
            One("m13", "label space", " ", control),
            One("m14", "label GET and a space", "GET ", control)));
        break;
    case "status":
    {
        var statuses = new (string Label, OneOf<HttpStatusCode, string>? Status)[]
        {
            ("200", HttpStatusCode.OK), ("404", HttpStatusCode.NotFound), ("undefined number 299", (HttpStatusCode)299), ("undefined number 599", (HttpStatusCode)599),
            ("zero", (HttpStatusCode)0), ("text Ok", "Ok"), ("text OK", "OK"), ("text NotFound", "NotFound"), ("text 200", "200"), ("text 0200", "0200"), ("text 200 and a space", "200 "),
            ("text 599", "599"), ("text -1", "-1"), ("text Ack", "Ack"), ("text empty", ""), ("text space", " "), ("error with text", "ERROR: connection refused"),
            ("thrown", "!HttpRequestException"), ("text 1e3", "1e3"), ("past int", "2147483648"),
        };
        var i = 0;
        rows.Add(F("Status", statuses.Select(s => One("s" + i++, "status " + s.Label, HttpMethod.Get, control + i, s.Status)).ToArray()));
        var noAnswer = "s-none";
        Call(noAnswer, "no response", HttpMethod.Get, control, answer: false);
        rows[0] = rows[0] with { Scenarios = [.. rows[0].Scenarios, Sc(noAnswer, "no response")] };
        break;
    }
    case "content":
    case "content-json":
    {
        if (caseName == "content-json") options.CompressTestRunReport = false;
        var nl = ((char)10).ToString();
        var cr = ((char)13).ToString();
        var longLine = "SELECT " + new string('x', 20000) + " FROM t";
        var bodies = new (string Label, string? Content, string? Category, OneOf<HttpMethod, string> Method)[]
        {
            ("select", "SELECT 1 FROM t", Sql, "Query"),
            ("multi-line", "SELECT *" + nl + "FROM t", Sql, "Query"),
            ("cr only newline", "SELECT *" + cr + "FROM t", Sql, "Query"),
            ("whitespace only", "   ", Sql, "Query"),
            ("empty", "", Sql, "Query"),
            ("null", null, Sql, "Query"),
            ("set prologue", "SET NOCOUNT ON; SET XACT_ABORT ON;" + nl + "SELECT 1", Sql, "Query"),
            ("document query", "{ " + (char)34 + "query" + (char)34 + ": " + (char)34 + "SELECT @a" + (char)34 + " }", DependencyCategories.BigQuery, "Query"),
            ("cut document", "{ " + (char)34 + "query" + (char)34 + ": " + (char)34 + "SELECT", DependencyCategories.BigQuery, "Query"),
            ("long first line", longLine, Sql, "Query"),
            ("long second line", "SELECT 2" + nl + new string('y', 20000), Sql, "Query"),
            ("nul", "SELECT " + (char)0 + " x", Sql, "Query"),
            ("line separator", "SELECT 1" + char.ConvertFromUtf32(0x2028) + "FROM t", Sql, "Query"),
            ("bom", char.ConvertFromUtf32(0xFEFF) + "SELECT 3", Sql, "Query"),
            ("lone surrogate", "SELECT '" + (char)0xD83D + "' FROM t", Sql, "Query"),
            ("replacement char", "SELECT '" + char.ConvertFromUtf32(0xFFFD) + "' FROM t", Sql, "Query"),
            ("graphql post", "{" + (char)34 + "query" + (char)34 + ":" + (char)34 + "query GetUser { id }" + (char)34 + "}", null, HttpMethod.Post),
            ("graphql post, label Post", "{" + (char)34 + "query" + (char)34 + ":" + (char)34 + "query GetUser { id }" + (char)34 + "}", null, "Post"),
        };
        var i = 0;
        rows.Add(F("Content", bodies.Select(b => One("c" + i++, "content " + b.Label, b.Method, control + "/" + i, content: b.Content, category: b.Category)).ToArray()));
        break;
    }
    case "ignored":
    {
        // A call the tracker said to ignore, a marker, a response with no request, a user action, two responses.
        Call("i1", "ignored call beside a real one", HttpMethod.Get, control);
        Call("i1", "ignored call beside a real one", HttpMethod.Post, control + "/ignored", ignored: true);
        Call("i2", "marker beside a real one", HttpMethod.Get, control);
        var marker = new RequestResponseLog("marker beside a real one", "i2", "", null, new Uri("http://override.com/"), [], "Svc", "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsOverrideStart = true, Timestamp = at, PlantUml = "note over Svc: x" };
        RequestResponseLogger.Log(marker);
        var orphanResponse = new RequestResponseLog("response with no request", "i3", "GET", null, new Uri(control), [], "Svc", "Caller", RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false, HttpStatusCode.OK) { Timestamp = at };
        RequestResponseLogger.Log(orphanResponse);
        Call("i3", "response with no request", HttpMethod.Get, control + "/real");
        var action = new RequestResponseLog("user action", "i4", "Click", "detail", new Uri("ui://page"), [], "Page", "User", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsUserAction = true, Timestamp = at };
        RequestResponseLogger.Log(action);
        var twice = Guid.NewGuid();
        var twoReq = new RequestResponseLog("two responses", "i5", HttpMethod.Get, null, new Uri(control), [], "Svc", "Caller", RequestResponseType.Request, twice, twice, false) { Timestamp = at };
        RequestResponseLogger.Log(twoReq);
        RequestResponseLogger.Log(twoReq with { Type = RequestResponseType.Response, StatusCode = HttpStatusCode.OK });
        RequestResponseLogger.Log(twoReq with { Type = RequestResponseType.Response, StatusCode = HttpStatusCode.Accepted });
        rows.Add(F("Ignored", Sc("i1", "ignored call beside a real one"), Sc("i2", "marker beside a real one"), Sc("i3", "response with no request"), Sc("i4", "user action"), Sc("i5", "two responses")));
        break;
    }
    case "retry-one-id":
    {
        // Two attempts of one scenario under one runtime id, a retry under a framework whose id is the test's own. The second passes.
        Call("0-1002", "retried", HttpMethod.Get, control + "/a");
        Call("0-1002", "retried", HttpMethod.Get, control + "/b");
        var first = Sc("0-1002", "retried", ExecutionResult.Failed) with { Attempt = 1, ErrorMessage = "boom" };
        var second = Sc("0-1002", "retried") with { Attempt = 2 };
        Call("ctl", "control", HttpMethod.Get, control);
        rows.Add(F("Retry", first, second, Sc("ctl", "control")));
        break;
    }
    case "shared-id-two-tests":
    {
        // Two different tests under one runtime id, a framework whose id is not unique, with no attempts.
        Call("same", "first test", HttpMethod.Get, control + "/a");
        Call("same", "second test", HttpMethod.Get, control + "/b");
        Call("ctl", "control", HttpMethod.Get, control);
        rows.Add(F("Shared", Sc("same", "first test"), Sc("same", "second test"), Sc("ctl", "control")));
        break;
    }
    case "repeat":
    {
        // One stable id twice in one run: repeated data, or the same row in two Examples blocks. Differing runtime ids.
        Call("r1", "untimed row", HttpMethod.Get, control + "/r1");
        Call("r2", "untimed row", HttpMethod.Get, control + "/r2");
        Call("r3", "timed alike", HttpMethod.Get, control + "/r3");
        Call("r4", "timed alike", HttpMethod.Get, control + "/r4");
        Call("r5", "timed apart", HttpMethod.Get, control + "/r5");
        Call("r6", "timed apart", HttpMethod.Get, control + "/r6");
        Call("r7", "skipped row", HttpMethod.Get, control + "/r7");
        rows.Add(F("Repeat",
            Sc("r1", "untimed row") with { Duration = null },
            Sc("r2", "untimed row") with { Duration = null },
            Sc("r3", "timed alike") with { Duration = TimeSpan.FromSeconds(1.5) },
            Sc("r4", "timed alike") with { Duration = TimeSpan.FromSeconds(1.5) },
            Sc("r5", "timed apart"),
            Sc("r6", "timed apart"),
            Sc("r7", "skipped row", ExecutionResult.Skipped) with { Duration = null },
            Sc("r8", "skipped row", ExecutionResult.Skipped) with { Duration = null }));
        break;
    }
    case "defaulted":
    {
        // A result the run never saw: an ingest's scenario with no end. It failed until now.
        Call("d1", "defaulted after failing", HttpMethod.Get, control);
        Call("d2", "defaulted after passing", HttpMethod.Get, control);
        Call("ctl", "control", HttpMethod.Get, control);
        rows.Add(F("Defaulted",
            runNumber < last ? Sc("d1", "defaulted after failing", ExecutionResult.Failed) with { ErrorMessage = "boom" } : Sc("d1", "defaulted after failing") with { ResultDefaulted = true },
            runNumber < last ? Sc("d2", "defaulted after passing") : Sc("d2", "defaulted after passing") with { ResultDefaulted = true },
            Sc("ctl", "control")));
        break;
    }
    case "examples":
    case "examples-json":
    {
        if (caseName == "examples-json") options.CompressTestRunReport = false;
        Call("e1", "row with an empty value", HttpMethod.Get, control);
        Call("e2", "row with a null value", HttpMethod.Get, control);
        Call("e3", "row with an empty outline", HttpMethod.Get, control);
        Call("e4", "row keyed by outline", HttpMethod.Get, control);
        Call("e5", "row with a stable key", HttpMethod.Get, control);
        rows.Add(F("Examples",
            Sc("e1", "row with an empty value") with { OutlineId = "o1", ExampleValues = new Dictionary<string, string> { ["a"] = "" } },
            Sc("e2", "row with a null value") with { OutlineId = "o2", ExampleValues = new Dictionary<string, string> { ["a"] = null! } },
            Sc("e3", "row with an empty outline") with { OutlineId = "" },
            Set(Sc("e4", "row keyed by outline") with { OutlineId = "o4", ExampleValues = new Dictionary<string, string> { ["b"] = "1" } }, "KeyedByOutline", true)));
        break;
    }
    case "partial":
    {
        // The host says this run is partial, and it lacks one of twenty, under the heuristic's bar.
        var all = Enumerable.Range(0, runNumber < last ? 20 : 19).Select(i => One("p" + i, "scenario " + i, HttpMethod.Get, control + "/" + i)).ToArray();
        rows.Add(F("Partial", all));
        if (runNumber == last) options.HistoryPartialRun = true;
        break;
    }
    case "partial-false":
    {
        // The host says this run is whole, and it lacks half: the heuristic would call it partial.
        var all = Enumerable.Range(0, runNumber < last ? 20 : 10).Select(i => One("p" + i, "scenario " + i, HttpMethod.Get, control + "/" + i)).ToArray();
        rows.Add(F("Partial", all));
        if (runNumber == last) options.HistoryPartialRun = false;
        break;
    }
    case "reorder":
    {
        options.HistoryReordered = true;
        Call("o1", "two calls", HttpMethod.Get, control + (runNumber < last ? "/a" : "/b"));
        Call("o1", "two calls", HttpMethod.Get, control + (runNumber < last ? "/b" : "/a"));
        Call("ctl", "control", HttpMethod.Get, control);
        rows.Add(F("Reorder", Sc("o1", "two calls"), Sc("ctl", "control")));
        break;
    }
    case "count":
    {
        options.HistoryCountRuns = 1;
        Call("o1", "a call made again", HttpMethod.Get, control);
        if (runNumber == last) Call("o1", "a call made again", HttpMethod.Get, control);
        Call("ctl", "control", HttpMethod.Get, control);
        rows.Add(F("Count", Sc("o1", "a call made again"), Sc("ctl", "control")));
        break;
    }
    case "window":
    {
        // The host reads one run back: its report says this scenario's change of three runs ago is behind the window.
        options.HistoryWindow = 1;
        Call("o1", "changed long ago", HttpMethod.Get, control + (runNumber == 1 ? "/old" : "/new"));
        Call("ctl", "control", HttpMethod.Get, control);
        rows.Add(F("Window", Sc("o1", "changed long ago"), Sc("ctl", "control")));
        break;
    }
    case "rules":
    {
        options.HistoryShapeTemplates = [new HistoryShapeTemplate("k-[a-z]+", "{key}")];
        var key = "k-" + (char)('a' + runNumber) + "x";
        rows.Add(F("Rules", One("t1", "templated key", HttpMethod.Get, "http://h/cache/" + key), One("t2", "templated statement", "Query", "http://h/db", content: "SELECT " + key, category: Sql), One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "rules-two-shards-same":
    case "rules-two-shards-differ":
    {
        options.HistoryShapeTemplates = caseName.EndsWith("differ") && shard == "b"
            ? [new HistoryShapeTemplate("v-[a-z]+", "{value}")]
            : [new HistoryShapeTemplate("k-[a-z]+", "{key}")];
        var key = "k-" + (char)('a' + runNumber) + "x";
        rows.Add(F("Rules " + shard, One("t" + shard, "templated " + shard, HttpMethod.Get, "http://h/cache/" + key), One("c" + shard, "control " + shard, HttpMethod.Get, control)));
        break;
    }
    case "error-keys":
    {
        options.HistoryErrorKeys = false;
        Call("e1", "failing", HttpMethod.Get, control);
        rows.Add(F("Errors", Sc("e1", "failing", ExecutionResult.Failed) with { ErrorMessage = "the same message each run" }, One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "expired":
    {
        // A call under a scenario's identity after it ended (background), and a call under an id no scenario has (orphaned).
        var ended = at.AddSeconds(-1);
        Call("x1", "with a late call", HttpMethod.Get, control);
        Call("x1", "with a late call", HttpMethod.Post, control + "/late", service: "Late");
        Call("zzz", "no such scenario", HttpMethod.Post, control + "/orphan", service: "Orphan");
        Call(TestIdentityScope.UnknownTestId, TestIdentityScope.UnknownTestName, HttpMethod.Post, control + "/background", service: "Background");
        rows.Add(F("Expired", Sc("x1", "with a late call") with { EndedAt = at.AddMilliseconds(-4) }, One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "features-one-name":
    {
        // Two features of one name whose ids key on two names, as the lanes that record a frozen name can write.
        rows.Add(Set(new Feature { DisplayName = "Same", Scenarios = [One("f1", "in A", HttpMethod.Get, control)] }, "StableIdName", "Frozen A"));
        rows.Add(Set(new Feature { DisplayName = "Same", Scenarios = [One("f2", "in B", HttpMethod.Get, control)] }, "StableIdName", "Frozen B"));
        rows.Add(F("Other", One("f3", "control", HttpMethod.Get, control)));
        break;
    }
    case "two-shards":
    case "two-suites":
    {
        if (caseName == "two-suites") options.SuiteName = "suite-" + shard;
        rows.Add(F("Common feature",
            One(shard + "1", "row 1 of " + shard, "Delete", "cosmos://db/" + shard, category: DependencyCategories.CosmosDB),
            One(shard + "2", "row 2 of " + shard, HttpMethod.Get, "http://h/" + shard + "/42")));
        rows.Add(F("Feature of " + shard,
            One(shard + "3", "row 3 of " + shard, "Query", "sql://db", content: "SELECT * FROM t" + shard + " WHERE a = 1", category: Sql),
            One(shard + "4", "row 4 of " + shard, "Publish", "kafka://topic", kind: RequestResponseMetaType.Event)));
        Call("zzz" + shard, "no such scenario", HttpMethod.Post, control + "/orphan", service: "Orphan" + shard);
        Call(TestIdentityScope.UnknownTestId, TestIdentityScope.UnknownTestName, HttpMethod.Post, control + "/background", service: "Background" + shard);
        break;
    }
    case "overlap":
    {
        // One scenario ran in both shards, a re-run beside its first attempt: it fails in a and passes in b.
        Call("ov" + shard, "both shards", HttpMethod.Get, control);
        rows.Add(F("Overlap", Sc("ov" + shard, "both shards", shard == "a" ? ExecutionResult.Failed : ExecutionResult.Passed) with { ErrorMessage = shard == "a" ? "boom" : null, Duration = TimeSpan.FromSeconds(shard == "a" ? 1 : 2) },
            One("only" + shard, "only in " + shard, HttpMethod.Get, control)));
        break;
    }
    case "two-shards-change":
    {
        // A real change at run 2, and nothing since: from run 3 on nothing has changed.
        rows.Add(F("Common feature",
            One(shard + "1", "row 1 of " + shard, HttpMethod.Get, "http://h/" + shard + (runNumber == 1 ? "/old" : "/new")),
            One(shard + "2", "row 2 of " + shard, HttpMethod.Get, "http://h/" + shard + "/stays")));
        break;
    }
    case "flaky-min-runs":
    {
        // The host reads flakiness over two verdicts. It flips every run.
        options.HistoryMinRuns = 2;
        Call("o1", "flips", HttpMethod.Get, control);
        rows.Add(F("Flips", Sc("o1", "flips", runNumber % 2 == 0 ? ExecutionResult.Passed : ExecutionResult.Failed) with { ErrorMessage = runNumber % 2 == 0 ? null : "boom" }, One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "run-id-plain":
    {
        rows.Add(F("RunId", One("n1", "a scenario that breaks in the last run", HttpMethod.Get, control)));
        if (runNumber == last) rows[0].Scenarios[0].Result = ExecutionResult.Failed;
        break;
    }
    case "run-id":
    {
        options.HistoryRunId = "custom-" + runNumber;
        rows.Add(F("RunId", One("n1", "named run", HttpMethod.Get, control)));
        if (runNumber == last) rows[0].Scenarios[0].Result = ExecutionResult.Failed;
        break;
    }
    case "real-change":
    {
        // The other side: a real change must still be named.
        rows.Add(F("Change", One("g1", "changes in the last run", "Delete", control + (runNumber == last ? "/new" : "")), One("g2", "stays", "Delete", control)));
        break;
    }
    case "rerun-failed-job":
    {
        // GitHub's "re-run failed jobs": shard b fails in attempt 1 and is run again under attempt 2, shard a stays as attempt 1 wrote it.
        rows.Add(F("Feature of " + shard, One(shard + "1", "row 1 of " + shard, "Delete", "http://h/" + shard), One(shard + "2", "row 2 of " + shard, HttpMethod.Get, "http://h/" + shard + "/two")));
        break;
    }
    case "two-shards-one-empty":
    {
        // A matrix shard whose filter matched nothing.
        if (shard == "a")
            rows.Add(F("Feature of a", One("a1", "row 1", "Delete", "http://h/a"), One("a2", "row 2", HttpMethod.Get, "http://h/a/two")));
        break;
    }
    case "rules-edit":
    {
        // The consumer edits a pattern in the last run; the window holds the earlier one.
        options.HistoryShapeTemplates = runNumber == last
            ? [new HistoryShapeTemplate("k-[a-z]+", "{renamed}")]
            : [new HistoryShapeTemplate("k-[a-z]+", "{key}")];
        var key = "k-" + (char)('a' + runNumber) + "x";
        rows.Add(F("Rules", One("t1", "templated key", HttpMethod.Get, "http://h/cache/" + key), One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "rules-last-shard-diverges":
    {
        // One shard takes an edit the other has not: every earlier run agreed.
        options.HistoryShapeTemplates = shard == "b" && runNumber == last
            ? [new HistoryShapeTemplate("v-[a-z]+", "{value}")]
            : [new HistoryShapeTemplate("k-[a-z]+", "{key}")];
        var key = "k-" + (char)('a' + runNumber) + "x";
        rows.Add(F("Rules " + shard, One("t" + shard, "templated " + shard, HttpMethod.Get, "http://h/cache/" + key), One("c" + shard, "control " + shard, HttpMethod.Get, control)));
        break;
    }
    case "pr-branch":
    case "history-branch":
    case "history-compare-branch":
    {
        // A run on another branch reading against main: by the pull request's target, by the host's option.
        if (caseName == "history-branch" && runNumber == last) options.HistoryBranch = "main";
        if (caseName == "history-compare-branch" && runNumber == last) options.HistoryCompareBranch = "main";
        rows.Add(F("Branch", One("b1", "changes in the last run", HttpMethod.Get, control + (runNumber == last ? "/new" : "")), One("b2", "stays", HttpMethod.Get, control)));
        break;
    }
    case "two-shards-same-ids":
    {
        // NUnit's runtime ids are a counter per process: two shards give the same ids to different tests.
        rows.Add(F("Feature of " + shard,
            One("0-1001", "first of " + shard, HttpMethod.Get, "http://h/" + shard + "/one"),
            One("0-1002", "second of " + shard, "Delete", "http://h/" + shard + "/two")));
        break;
    }
    case "error-text":
    {
        // What the error key is cut from, as the file holds it.
        var esc = ((char)27).ToString();
        string?[] messages =
        [
            "plain message",
            "Expected 1 but found 2" + (char)13 + (char)10 + "second line" + (char)10 + "   at Some.Stack.Frame() in file.cs:line 12",
            esc + "[31mred" + esc + "[0m message",
            new string('x', 5000),
            new string('y', 70000),
            "id 3f2504e0-4f89-11d3-9a0c-0305e82c3301 at 2026-10-01T10:00:00Z took 123 ms",
            "",
            "   surrounded   ",
            "nul" + (char)0 + "inside",
            char.ConvertFromUtf32(0x1F600) + " and a lone " + (char)0xD800,
            null,
        ];
        var scenarios = new List<Scenario>();
        for (var i = 0; i < messages.Length; i++)
        {
            Call("e" + i, "message " + i, HttpMethod.Get, control);
            scenarios.Add(Sc("e" + i, "message " + i, ExecutionResult.Failed) with { ErrorMessage = messages[i], ErrorStackTrace = "   at Frame() in f.cs:line " + i });
        }
        scenarios.Add(One("ctl", "control", HttpMethod.Get, control));
        rows.Add(F("Errors", scenarios.ToArray()));
        break;
    }
    case "names":
    {
        // A caller or service that is empty, null, padded, or holds what a line is split on.
        Scenario Named(string id, string name, string caller, string service)
        {
            Call(id, name, HttpMethod.Get, control, caller: caller, service: service);
            return Sc(id, name);
        }
        rows.Add(F("Names",
            Named("n1", "empty caller", "", "Svc"),
            Named("n2", "empty service", "Caller", ""),
            Named("n3", "null caller", null!, "Svc"),
            Named("n4", "null service", "Caller", null!),
            Named("n5", "padded", " Caller ", " Svc "),
            Named("n6", "a greater-than and a line break", "A>B", "line" + (char)10 + "break"),
            Named("n7", "unicode", "Caller" + (char)0xE9, "Svc" + (char)0x4E2D),
            One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "method-real-handler":
    {
        // The real tracker: an HttpMessageHandler chain, the label as a test would build it.
        var current = ("", "");
        using var invoker = new HttpMessageInvoker(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => current,
        }) { InnerHandler = new Answers() });
        var scenarios = new List<Scenario>();
        foreach (var (label, verb) in new (string, HttpMethod)[]
        {
            ("Patch as written", new HttpMethod("Patch")),
            ("patch as written", new HttpMethod("patch")),
            ("Parse of patch", HttpMethod.Parse("patch")),
            ("PATCH as written", new HttpMethod("PATCH")),
            ("Purge as written", new HttpMethod("Purge")),
            ("PURGE as written", new HttpMethod("PURGE")),
            ("HttpMethod.Patch", HttpMethod.Patch),
        })
        {
            var id = "h" + scenarios.Count;
            current = (label, id);
            using var request = new HttpRequestMessage(verb, "http://h/real-handler/" + id);
            using var answer = await invoker.SendAsync(request, CancellationToken.None);
            scenarios.Add(Sc(id, label));
        }
        rows.Add(F("Real handler", scenarios.ToArray()));
        break;
    }
    case "attempts":
    {
        rows.Add(F("Attempts",
            One("a0", "no attempt recorded", HttpMethod.Get, control),
            One("a1", "first", HttpMethod.Get, control) with { Attempt = 1 },
            One("a2", "second", HttpMethod.Get, control) with { Attempt = 2 },
            One("a9", "ninth", HttpMethod.Get, control) with { Attempt = 9 },
            One("a12", "twelfth", HttpMethod.Get, control) with { Attempt = 12 }));
        break;
    }
    case "not-a-test":
    {
        rows.Add(F("Not a test",
            One("na", "a row that is not a test", HttpMethod.Get, control) with { NotATest = true },
            One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "retry-process":
    case "retry-process-failed-only":
    {
        // A second process under one run id in one folder, a retained first attempt under runs/.
        options.KeepRuns = 5;
        var attempt = int.TryParse(Environment.GetEnvironmentVariable("MRT_ATTEMPT"), out var a) ? a : 1;
        var failing = runNumber == last && attempt == 1;
        Call("r1", "flaky across processes", HttpMethod.Get, control);
        var retried = Sc("r1", "flaky across processes", failing ? ExecutionResult.Failed : ExecutionResult.Passed) with { ErrorMessage = failing ? "boom" : null };
        if (caseName.EndsWith("failed-only") && attempt == 2)
            rows.Add(F("Retry", retried));
        else
            rows.Add(F("Retry", retried, One("ctl", "control", HttpMethod.Get, control)));
        break;
    }
    case "two-shards-late-change":
    {
        // Nothing changes until the last run, and then something does: the merge has to name it.
        rows.Add(F("Common feature",
            One(shard + "1", "row 1 of " + shard, HttpMethod.Get, "http://h/" + shard + (runNumber == last ? "/new" : "/old")),
            One(shard + "2", "row 2 of " + shard, HttpMethod.Get, "http://h/" + shard + "/stays")));
        break;
    }
    case "history-shapes-off":
    {
        options.HistoryShapes = false;
        rows.Add(F("No shapes", One("s1", "stays", "Delete", control), One("s2", "stays too", HttpMethod.Get, control)));
        break;
    }
    case "history-durations-off":
    {
        options.HistoryDurations = false;
        rows.Add(F("No durations", One("s1", "stays", HttpMethod.Get, control), One("s2", "stays too", HttpMethod.Get, control)));
        break;
    }
    case "slower":
    {
        // The run's reading of duration against the merge's, with the host's own bar.
        options.HistorySlowerBy = 1.1;
        options.HistorySlowerMinMs = 10;
        options.HistoryMinRuns = 2;
        Call("d1", "a little slower in the last run", HttpMethod.Get, control);
        rows.Add(F("Slower", Sc("d1", "a little slower in the last run") with { Duration = TimeSpan.FromMilliseconds(runNumber == last ? 1300 : 1000) }, One("ctl", "control", HttpMethod.Get, control) with { Duration = TimeSpan.FromMilliseconds(1000) }));
        break;
    }
    default:
        throw new ArgumentException("no such case: " + caseName);
}

var featuresOut = rows.ToArray();
var end = DateTime.UtcNow;
ReportGenerator.CreateStandardReportsWithDiagrams(featuresOut, end.AddSeconds(-5), end, options);
return 0;

// ----------------------------------------------------------------------------------------------------------------

static int Read(string ledgerPath, string toolDll, string[] folders)
{
    var reports = folders.Select(f => Directory.GetFiles(f).First(p => Path.GetFileName(p) is "TestRunReport.krkl" or "TestRunReport.json")).ToList();
    var merged = MergeableReportRenderer.MergeFiles(reports);

    // The tool's own code path.
    var tool = Assembly.LoadFrom(toolDll);
    var command = tool.GetType("Kronikol.Tool.MergeCommand", throwOnError: true)!;
    const BindingFlags Any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    var readHistory = command.GetMethod("ReadHistory", Any)!;
    var runOf = command.GetMethod("HistoryRunOf", Any)!;
    object?[] call = [ledgerPath, merged, Console.Error, (Func<string, string?>)Environment.GetEnvironmentVariable, 0];
    var mergedVerdicts = (HistoryVerdicts?)readHistory.Invoke(null, call) ?? throw new InvalidOperationException("no verdicts");

    var read = HistoryLedgerReader.Read(Path.GetFullPath(ledgerPath), 51);
    var ledger = read.Ledger!;
    var tuple = runOf.Invoke(null, [merged, ledger, new string?[] { null }, 50])!;
    var mergedRoster = (HistoryRoster)tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
    var mergedRun = (HistoryRun)tuple.GetType().GetField("Item2")!.GetValue(tuple)!;

    // The run's own: its fragments folded, as history record folds them, read as `query history` reads a fragment.
    var fragments = folders.Select(f =>
    {
        var top = HistoryFragment.Parse(File.ReadAllText(Path.Combine(f, HistoryFormat.FragmentFileName)));
        var attempts = Directory.EnumerateFiles(f, HistoryFormat.FragmentFileName, SearchOption.AllDirectories)
            .Select(p => HistoryFragment.Parse(File.ReadAllText(p)))
            .Where(x => x.Run.Id == top.Run.Id && x.Run.Suite == top.Run.Suite)
            .OrderBy(x => x.Run.At)
            .ToList();
        return HistoryFold.Attempts(attempts);
    }).ToList();
    var folded = HistoryFold.Fold(fragments);
    var (ownRoster, ownRun, ownShapes) = folded.Count == 1
        ? (folded[0].Roster, folded[0].Run, folded[0].Shapes)
        : (folded[0].Roster, folded[0].Run, folded[0].Shapes);
    if (folded.Count > 1)
        Console.WriteLine($"  ! the fragments fold into {folded.Count} lines (suites {string.Join(", ", folded.Select(x => x.Run.Suite ?? "(none)"))}): the first is read as the run's own");
    // What the run itself read with: its options, which a merge has no way to be told.
    var ownOptions = new HistoryAnalysisOptions
    {
        ReportReordered = Environment.GetEnvironmentVariable("MRT_OWN_REORDERED") is "1",
        CountRuns = int.TryParse(Environment.GetEnvironmentVariable("MRT_OWN_COUNT_RUNS"), out var countRuns) ? countRuns : 2,
        MinRuns = int.TryParse(Environment.GetEnvironmentVariable("MRT_OWN_MIN_RUNS"), out var minRuns) ? minRuns : 5,
        Branch = Environment.GetEnvironmentVariable("MRT_OWN_BRANCH"),
        SlowerBy = double.TryParse(Environment.GetEnvironmentVariable("MRT_OWN_SLOWER_BY"), out var slowerBy) ? slowerBy : 1.5,
        SlowerMinMs = int.TryParse(Environment.GetEnvironmentVariable("MRT_OWN_SLOWER_MIN_MS"), out var slowerMin) ? slowerMin : 100,
    };
    var ownVerdicts = HistoryAnalyzer.Analyse(ledger, ownRoster, ownRun, ownOptions, shapes: ownShapes);

    // The lines a position's calls name.
    IReadOnlyList<string> Lines(HistoryRun run, HistoryShapes? shapes, int at) =>
        run.CallSetAt(at) is { } set && shapes is not null ? set.Select(shapes.At).OfType<string>().ToArray() : [];
    var mergedShapes = BuildShapes(merged);

    Console.WriteLine($"  own    : {HistorySummary.Line(ownVerdicts)}; id {ownRun.Id}, suite {ownRun.Suite ?? "(none)"}, stream {ownRun.Stream}, rule {HistoryBridge.RuleOf(ownRun)}, partial {ownVerdicts.Partial}, absent {ownVerdicts.Absent.Count}, new deps {ownVerdicts.NewDependencies.Count}, deps {ownRun.Deps?.Count}, positions {ownRoster.Count}");
    Console.WriteLine($"  merged : {HistorySummary.Line(mergedVerdicts)}; id {mergedRun.Id}, suite {mergedRun.Suite ?? "(none)"}, stream {mergedRun.Stream}, rule {HistoryBridge.RuleOf(mergedRun)}, partial {mergedVerdicts.Partial}, absent {mergedVerdicts.Absent.Count}, new deps {mergedVerdicts.NewDependencies.Count}, deps {mergedRun.Deps?.Count}, positions {mergedRoster.Count}");
    foreach (var a in mergedVerdicts.Absent)
        Console.WriteLine($"    merged says absent: {a.Name}");
    foreach (var d in mergedVerdicts.NewDependencies)
        Console.WriteLine($"    merged says new dependency: {d}");
    foreach (var d in ownVerdicts.NewDependencies)
        Console.WriteLine($"    own says new dependency: {d}");
    var ownDeps = new HashSet<string>(ownRun.Deps ?? []);
    var mergedDeps = new HashSet<string>(mergedRun.Deps ?? []);
    foreach (var d in ownDeps.Except(mergedDeps))
        Console.WriteLine($"    a dep only the run's own line has: {d}");
    foreach (var d in mergedDeps.Except(ownDeps))
        Console.WriteLine($"    a dep only the merged line has: {d}");

    var ownAt = new Dictionary<(string, int), int>();
    for (var i = 0; i < ownRoster.Count; i++) ownAt[(ownRoster.Ids[i], ownRoster.Slots[i])] = i;
    var mergedAt = new Dictionary<(string, int), int>();
    for (var i = 0; i < mergedRoster.Count; i++) mergedAt[(mergedRoster.Ids[i], mergedRoster.Slots[i])] = i;

    foreach (var key in ownAt.Keys.Except(mergedAt.Keys))
        Console.WriteLine($"    only in the run's own roster: {ownRoster.Names[ownAt[key]]} (slot {key.Item2}, {key.Item1})");
    foreach (var key in mergedAt.Keys.Except(ownAt.Keys))
        Console.WriteLine($"    only in the merged roster: {mergedRoster.Names[mergedAt[key]]} (slot {key.Item2}, {key.Item1})");

    var same = 0;
    for (var m = 0; m < mergedRoster.Count; m++)
    {
        var key = (mergedRoster.Ids[m], mergedRoster.Slots[m]);
        var mv = mergedVerdicts.At(m, mergedRoster.Ids[m]);
        if (!ownAt.TryGetValue(key, out var o))
        {
            Console.WriteLine($"  {mergedRoster.Names[m],-44} merged only: {Names(mv)}");
            continue;
        }
        var ov = ownVerdicts.At(o, ownRoster.Ids[o]);
        var differences = new List<string>();
        if (Names(ov) != Names(mv)) differences.Add($"verdict {Names(ov)} -> {Names(mv)}");
        if (ownRun.ResultAt(o) != mergedRun.ResultAt(m)) differences.Add($"result {ownRun.ResultAt(o)} -> {mergedRun.ResultAt(m)}");
        if (ownRun.AttemptAt(o) != mergedRun.AttemptAt(m)) differences.Add($"attempt {ownRun.AttemptAt(o)} -> {mergedRun.AttemptAt(m)}");
        if (ownRun.CallsAt(o) != mergedRun.CallsAt(m)) differences.Add($"calls {ownRun.CallsAt(o)} -> {mergedRun.CallsAt(m)}");
        if (ownRun.ShapeSetAt(o) != mergedRun.ShapeSetAt(m)) differences.Add("set fingerprint moves");
        else if (ownRun.ShapeOrderedAt(o) != mergedRun.ShapeOrderedAt(m)) differences.Add("ordered fingerprint moves");
        if (ownRun.DurationAt(o) != mergedRun.DurationAt(m)) differences.Add($"duration {ownRun.DurationAt(o)} -> {mergedRun.DurationAt(m)}");
        if (ownRun.ErrorAt(o) != mergedRun.ErrorAt(m)) differences.Add($"error key [{ownRun.ErrorAt(o)}] -> [{mergedRun.ErrorAt(m)}]");
        var ownLines = Lines(ownRun, ownShapes, o);
        var mergedLines = Lines(mergedRun, mergedShapes, m);
        if (differences.Count == 0)
        {
            same++;
            continue;
        }
        Console.WriteLine($"  {mergedRoster.Names[m],-44} {string.Join("; ", differences)}");
        foreach (var line in ownLines.Except(mergedLines))
            Console.WriteLine($"      run's line : {Show(line)}");
        foreach (var line in mergedLines.Except(ownLines))
            Console.WriteLine($"      merged line: {Show(line)}");
        if (mv?.Evidence is { Length: > 0 } evidence && Names(ov) != Names(mv))
            Console.WriteLine($"      evidence   : {Show(evidence)}");
    }
    Console.WriteLine($"  {same} of {mergedRoster.Count} positions read the same on every field");
    var notes = ownVerdicts.Scenarios.Where(s => s.Evidence.Contains("earlier rule", StringComparison.Ordinal)).Count();
    var mergedNotes = mergedVerdicts.Scenarios.Where(s => s.Evidence.Contains("earlier rule", StringComparison.Ordinal)).Count();
    if (notes + mergedNotes > 0)
        Console.WriteLine($"  'earlier rule' evidence: own {notes}, merged {mergedNotes}");
    return 0;
}

// An internal member of a report model, as a lane sets it.
static T Set<T>(T target, string member, object? value)
{
    typeof(T).GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    return target;
}

static string Names(ScenarioHistory? h) => h is null ? "(none)" : string.Join("+", h.Verdicts.Select(v => v.ToString()).OrderBy(v => v, StringComparer.Ordinal));

// The merged run's shapes, as the tool's own build makes them.
static HistoryShapes? BuildShapes(MergeableReport merged) =>
    HistoryRunBuilder.Build(merged.Features, merged.Interactions, merged.Suite, merged.CiMetadata, new DateTimeOffset(merged.EndTime.ToUniversalTime()),
        new HistoryBuildOptions { ServiceId = merged.ServiceId, ShapeRules = HistoryShapeRules.Create(merged.ShapeTemplates) }).Shapes;

static string Show(string s)
{
    var b = new StringBuilder();
    foreach (var c in s)
    {
        if (c < 0x20 || c == 0x7f || c == 0x5C || (c >= 0x80 && !char.IsLetterOrDigit(c) && c != 0x2014 && c != 0x2026))
            b.Append("<U+").Append(((int)c).ToString("X4")).Append('>');
        else
            b.Append(c);
    }
    var text = b.ToString();
    return text.Length > 220 ? text[..220] + " ..." : text;
}

sealed class Answers : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
}
