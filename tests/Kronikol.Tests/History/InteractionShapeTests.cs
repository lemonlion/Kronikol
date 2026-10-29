using System.Net;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// The interaction fingerprint (plans/CROSS_RUN_HISTORY_PLAN.md §2.4, §5.6): what a scenario did,
/// reduced to the calls it made with their variable parts templated away, so the same behaviour on two
/// runs hashes the same and a new call, a dropped call or a changed status does not. Measured on the
/// real corpus: 68 percent of scenarios have interactions at all, and of those the raw URI was stable
/// across two runs in fewer than half - ids and timestamps in paths are why the templater exists.
/// </summary>
public class InteractionShapeTests
{
    [Theory]
    [InlineData("/orders/550e8400-e29b-41d4-a716-446655440000", "/orders/{id}")]
    [InlineData("/orders/550E8400E29B41D4A716446655440000/lines", "/orders/{id}/lines")]
    [InlineData("/sessions/a1b2c3d4e5f60718", "/sessions/{id}")]
    [InlineData("/events/01ARZ3NDEKTSV4RRFFQ69G5FAV", "/events/{id}")]
    [InlineData("/orders/123", "/orders/{n}")]
    [InlineData("/orders/123/items/4567", "/orders/{n}/items/{n}")]
    [InlineData("/v2/orders", "/v2/orders")]
    [InlineData("/reports/2026-09-12", "/reports/{ts}")]
    [InlineData("/snapshots/2026-09-12T10:04:11Z", "/snapshots/{ts}")]
    [InlineData("/snapshots/2026-09-12T10:04:11.123+01:00", "/snapshots/{ts}")]
    [InlineData("/customers/cust-4711/orders", "/customers/cust-{n}/orders")]
    [InlineData("/customers/abc123", "/customers/abc123")]
    [InlineData("/orders?page=3&sort=desc", "/orders?page&sort")]
    [InlineData("/orders?sort=desc&page=3", "/orders?page&sort")]
    [InlineData("/orders?id=550e8400-e29b-41d4-a716-446655440000", "/orders?id")]
    [InlineData("/prefix_550e8400-e29b-41d4-a716-446655440000_suffix", "/prefix_{id}_suffix")]
    public void Variable_path_parts_are_templated(string path, string expected)
    {
        Assert.Equal(expected, InteractionShape.Template(path));
    }

    // #75 §1a-c: what a real parallel suite wrote on every run and the rule missed. A GUID whose
    // separators are underscores (Google.Cloud.BigQuery names every job so), a twelve-hex digest
    // (Convert.ToHexString(hash, 0, 6)), and hash bytes captured as text in a cache key.
    [Theory]
    [InlineData("/projects/p/queries/job_09c68d49_adcf_47be_bf9e_fc041cdc4a8e", "/projects/p/queries/job_{id}")]
    [InlineData("/projects/p/queries/job_0094e738_b6a9_4411_ba74_9379763e3183", "/projects/p/queries/job_{id}")]
    [InlineData("/app:_v2:charts-agg-100000000000001-D692278CF6C9-Weekly", "/app:_v2:charts-agg-{n}-{id}-Weekly")]
    [InlineData("/app:_customerDates_100000000000001-%1D(%EF%BF%BD4%EF%BF%BD%EF%BF%BD:", "/app:_customerDates_{n}-{bin}")]
    [InlineData("/commits/3787de7c2f2e", "/commits/{id}")]
    [InlineData("/containers/0a1b2c3d4e5f/logs", "/containers/{id}/logs")]
    [InlineData("/assets/app.3f9a1c2b.js", "/assets/app.{id}.js")]
    [InlineData("/cache/%1D(%EF%BF%BD4/next", "/cache/{bin}/next")]
    [InlineData("/cache/key-ab%0Ccd/next", "/cache/key-{bin}/next")]
    public void Ids_the_templater_missed(string path, string expected)
    {
        Assert.Equal(expected, InteractionShape.Template(path));
    }

    // The short-hex rule is the nearest thing to the base64 rule that was measured and thrown out, so
    // what it must leave alone is pinned: a route word made of hex letters has no digit, a version
    // segment and a colour are under eight characters, and a number was never an id.
    [Theory]
    [InlineData("/customers/abc123")]
    [InlineData("/v2/orders")]
    [InlineData("/colours/ff00aa")]
    [InlineData("/facade/deadbeef/decade/feedface")]
    [InlineData("/api/v1/accede1/b2b")]
    [InlineData("/files/report%20final.pdf")]
    [InlineData("/files/caf%C3%A9.pdf")]
    [InlineData("/customers/download")]
    [InlineData("/files/SGVsbG8gV29ybGQ")]
    [InlineData("/orders/accessed/effaced/defaced")]
    public void What_is_not_an_id_stays(string path)
    {
        Assert.Equal(path, InteractionShape.Template(path));
    }

    [Fact]
    public void A_number_is_still_a_number_and_a_mixed_separator_guid_is_not_a_guid()
    {
        Assert.Equal("/builds/{n}/{n}", InteractionShape.Template("/builds/20260918/12345678"));
        // Nothing writes these; its first group falls to the short rule, its all-digit last to the number rule.
        Assert.Equal("/x/{id}-e29b_41d4-a716-{n}", InteractionShape.Template("/x/550e8400-e29b_41d4-a716-446655440000"));
    }

    // Statement heads share the id rule. What it may take is data (a bare hex value compared against,
    // the hash suffix of a table name); what it must not take is an identifier, a parameter or a keyword.
    [Theory]
    [InlineData("SELECT [o].[Id], [o].[CustomerId] FROM [Orders] AS [o] WHERE [o].[Id] = @__id_0")]
    [InlineData("SELECT \"o\".\"Id\" FROM \"Orders\" AS \"o\" WHERE \"o\".\"Id\" = @p0")]
    [InlineData("SELECT e0.added1d, c.name FROM events AS e0 JOIN customers AS c ON c.id = e0.customer_id")]
    [InlineData("SELECT * FROM c WHERE c.partitionKey = @pk AND c.added1d = @p1")]
    [InlineData("{ \"find\" : \"Trial\", \"filter\" : { \"CustomerId\" : \"cust-111\" }, \"limit\" : 1 }")]
    [InlineData("SELECT count() FROM events WHERE ts > now() SETTINGS max_threads = 4")]
    [InlineData("SELECT * FROM t WHERE flags = 0xDEADBEEF12")]
    public void The_short_hex_rule_takes_nothing_from_a_statement_head(string head)
    {
        Assert.DoesNotContain("{id}", InteractionShape.TemplateStatement(head));
    }

    [Fact]
    public void In_a_statement_head_the_short_hex_rule_takes_data_and_a_hash_suffix()
    {
        Assert.Equal("UPDATE leases SET lease = {id} WHERE owner = {id}", InteractionShape.TemplateStatement("UPDATE leases SET lease = abcdef12 WHERE owner = a1b2c3d4e5"));
        Assert.Equal("SELECT * FROM sales_{id}", InteractionShape.TemplateStatement("SELECT * FROM sales_a1b2c3d4e5"));
        // The limit, pinned as one: a name of eight or more characters spelt wholly in hex, with a digit,
        // cannot be told from a digest. Consistently templated, so it never reads as a change.
        Assert.Equal("SELECT c.{id} FROM c", InteractionShape.TemplateStatement("SELECT c.deadbeef1 FROM c"));
    }

    [Fact]
    public void There_is_no_base64_rule_because_it_templated_real_words()
    {
        // §5.6, reversed by measurement: a base64 detector matched ordinary path words like "customers"
        // and "download", templating the very segments that distinguish two endpoints. A payload id in a
        // path is rare enough to live with; a rule that erases route names is not.
        Assert.Equal("/files/SGVsbG8gV29ybGQ", InteractionShape.Template("/files/SGVsbG8gV29ybGQ"));
        Assert.Equal("/customers/download", InteractionShape.Template("/customers/download"));
    }

    private static RequestResponseLog Request(string testId, string method, string uri, string service, string caller, Guid pair, string? category = null, string? content = null) =>
        new("Scenario", testId, new HttpMethod(method), content, new Uri("http://" + service + uri), [], service, caller,
            RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false, DependencyCategory: category);

    private static RequestResponseLog Response(string testId, string service, string caller, Guid pair, HttpStatusCode status) =>
        new("Scenario", testId, HttpMethod.Get, null, new Uri("http://" + service + "/"), [], service, caller,
            RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: status);

    private static RequestResponseLog[] Pair(string testId, string method, string uri, HttpStatusCode status, string service = "orders", string caller = "Test")
    {
        var id = Guid.NewGuid();
        return [Request(testId, method, uri, service, caller, id), Response(testId, service, caller, id, status)];
    }

    [Fact]
    public void Calls_pair_a_request_with_its_response_by_id_and_keep_request_order()
    {
        var logs = new List<RequestResponseLog>();
        logs.AddRange(Pair("t1", "POST", "/orders", HttpStatusCode.Created));
        logs.AddRange(Pair("t1", "GET", "/orders/123", HttpStatusCode.OK));

        var calls = InteractionShape.Calls(logs);

        Assert.Equal(2, calls.Count);
        Assert.Equal(new ShapeCall("Test", "orders", "POST", "/orders", "201"), calls[0]);
        Assert.Equal(new ShapeCall("Test", "orders", "GET", "/orders/{n}", "200"), calls[1]);
    }

    [Fact]
    public void A_request_without_a_response_is_a_call_with_no_status()
    {
        var calls = InteractionShape.Calls([Request("t1", "GET", "/health", "orders", "Test", Guid.NewGuid())]);

        Assert.Equal(new ShapeCall("Test", "orders", "GET", "/health", null), Assert.Single(calls));
    }

    [Fact]
    public void A_call_that_threw_carries_the_exception_type_where_its_status_would_be()
    {
        // A failed send logs a response whose status is the exception's type behind a bang, so the
        // fingerprint tells "the call threw" from "the call never answered" and the evidence can name it.
        var pair = Guid.NewGuid();
        var request = Request("t1", "GET", "/asyncapi/v1.json", "orders", "Test", pair);
        var threw = new RequestResponseLog("Scenario", "t1", HttpMethod.Get, null, new Uri("http://orders/"), [], "orders", "Test",
            RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: "!HttpRequestException") { Error = "Error while copying content to a stream." };

        var call = Assert.Single(InteractionShape.Calls([request, threw]));

        Assert.Equal(new ShapeCall("Test", "orders", "GET", "/asyncapi/v1.json", "!HttpRequestException"), call);
        Assert.EndsWith(" GET /asyncapi/v1.json !HttpRequestException", call.ToString());
    }

    [Fact]
    public void Diagram_markers_and_ignored_records_are_not_calls()
    {
        var marker = Request("t1", "GET", "/", "orders", "Test", Guid.NewGuid()) with { IsOverrideStart = true };
        var ignored = Request("t1", "GET", "/", "orders", "Test", Guid.NewGuid()) with { TrackingIgnore = true };

        Assert.Empty(InteractionShape.Calls([marker, ignored]));
    }

    [Fact]
    public void A_statement_shaped_call_keeps_the_templated_statement_head()
    {
        // Every SQL call of a scenario shares one URI - the connection - so without the statement the
        // fingerprint could not tell "SELECT customers" from "DELETE customers". The head is templated
        // like a path, so a literal id in a WHERE clause does not make every run different.
        var call = Request("t1", "QUERY", "/", "db", "Api", Guid.NewGuid(), category: "database",
            content: "SELECT * FROM orders WHERE id = 4711\nORDER BY created");

        var shaped = Assert.Single(InteractionShape.Calls([call]));

        Assert.Contains("SELECT * FROM orders WHERE id = {n}", shaped.Uri);
        Assert.DoesNotContain("ORDER BY", shaped.Uri);
    }

    [Fact]
    public void The_set_fingerprint_ignores_order_and_the_ordered_one_does_not()
    {
        var a = new[] { new ShapeCall("Test", "orders", "POST", "/orders", "201"), new ShapeCall("Test", "orders", "GET", "/orders/{n}", "200") };
        var b = new[] { a[1], a[0] };

        var (setA, orderedA, callsA) = InteractionShape.Fingerprint(a);
        var (setB, orderedB, _) = InteractionShape.Fingerprint(b);

        Assert.Equal(setA, setB);
        Assert.NotEqual(orderedA, orderedB);
        Assert.Equal(2, callsA);
        Assert.Matches("^[0-9a-f]{8}$", setA);
    }

    [Fact]
    public void A_changed_status_a_new_call_or_a_dropped_call_changes_the_set_fingerprint()
    {
        var baseline = new[] { new ShapeCall("Test", "orders", "GET", "/orders/{n}", "200") };
        var status = new[] { new ShapeCall("Test", "orders", "GET", "/orders/{n}", "500") };
        var extra = new[] { baseline[0], new ShapeCall("Test", "audit", "POST", "/events", "202") };

        Assert.NotEqual(InteractionShape.Fingerprint(baseline).ShapeSet, InteractionShape.Fingerprint(status).ShapeSet);
        Assert.NotEqual(InteractionShape.Fingerprint(baseline).ShapeSet, InteractionShape.Fingerprint(extra).ShapeSet);
        Assert.NotEqual(InteractionShape.Fingerprint(extra).ShapeSet, InteractionShape.Fingerprint([]).ShapeSet);
    }

    [Fact]
    public void The_same_calls_with_different_ids_fingerprint_the_same()
    {
        var run1 = InteractionShape.Calls(Pair("t1", "GET", "/orders/550e8400-e29b-41d4-a716-446655440000", HttpStatusCode.OK));
        var run2 = InteractionShape.Calls(Pair("t1", "GET", "/orders/6ba7b810-9dad-11d1-80b4-00c04fd430c8", HttpStatusCode.OK));

        Assert.Equal(InteractionShape.Fingerprint(run1).ShapeSet, InteractionShape.Fingerprint(run2).ShapeSet);
    }

    [Fact]
    public void Dependencies_are_the_distinct_sorted_caller_service_pairs()
    {
        var logs = new List<RequestResponseLog>();
        logs.AddRange(Pair("t1", "GET", "/a", HttpStatusCode.OK, service: "orders", caller: "Test"));
        logs.AddRange(Pair("t1", "GET", "/b", HttpStatusCode.OK, service: "audit", caller: "orders"));
        logs.AddRange(Pair("t2", "GET", "/c", HttpStatusCode.OK, service: "orders", caller: "Test"));

        Assert.Equal(["Test>orders", "orders>audit"], InteractionShape.Dependencies(logs));
    }

    [Fact]
    public void An_id_cut_by_the_head_limit_is_still_templated()
    {
        // The head is templated before it is cut to length. Cut first, a GUID straddling the limit left its
        // first characters in the shape, and every run of a scenario that wrote a fresh id read as a change
        // (BreakfastProvider, 2026-09-14: 1 to 24 scenarios per lane, run after run, nothing having changed).
        var prefix = "SELECT * FROM orders WHERE " + new string('x', 84) + " = ";   // the id begins at column 114
        var run1 = InteractionShape.Calls([Request("t1", "QUERY", "/", "db", "Api", Guid.NewGuid(), category: "database", content: prefix + "550e8400-e29b-41d4-a716-446655440000")]);
        var run2 = InteractionShape.Calls([Request("t1", "QUERY", "/", "db", "Api", Guid.NewGuid(), category: "database", content: prefix + "6ba7b810-9dad-11d1-80b4-00c04fd430c8")]);

        Assert.Contains("= {id}", run1[0].Uri);
        Assert.Equal(InteractionShape.Fingerprint(run1).ShapeSet, InteractionShape.Fingerprint(run2).ShapeSet);
    }

    [Fact]
    public void Values_in_a_statement_head_are_data_and_keys_are_behaviour()
    {
        // A document written to Cosmos DB, a row inserted into BigQuery, a literal in a WHERE clause: which
        // fields and parameters were sent is behaviour, what they held is data - the rule the query string
        // already follows.
        static string Shape(string content) =>
            Assert.Single(InteractionShape.Calls([Request("t1", "CREATE", "/orders", "db", "Api", Guid.NewGuid(), category: "database", content: content)])).Uri;

        Assert.Equal(Shape(@"{""id"":""a1"",""Status"":""Pending"",""Note"":""Alice ordered""}"), Shape(@"{""id"":""b2"",""Status"":""Shipped"",""Note"":""Bob ordered""}"));
        Assert.NotEqual(Shape(@"{""id"":""a1"",""Status"":""Pending""}"), Shape(@"{""id"":""a1"",""State"":""Pending""}"));
        Assert.Contains(@"""Status"":""{v}""", Shape(@"{""id"":""a1"",""Status"":""Pending""}"));
        Assert.Equal(Shape("INSERT INTO orders (name) VALUES ('Alice')"), Shape("INSERT INTO orders (name) VALUES ('Bob')"));
        Assert.Contains("'{s}'", Shape("INSERT INTO orders (name) VALUES ('Alice')"));
        Assert.NotEqual(Shape("SELECT * FROM orders WHERE name = 'x'"), Shape("SELECT * FROM customers WHERE name = 'x'"));
    }

    [Fact]
    public void A_bracketed_identifier_is_not_a_value()
    {
        // Cosmos DB names a property as root["Name"]: the name is what the query does, the literal beside it
        // is data.
        static string Shape(string content) =>
            Assert.Single(InteractionShape.Calls([Request("t1", "QUERY", "/orders", "db", "Api", Guid.NewGuid(), category: "database", content: content)])).Uri;

        Assert.Equal(Shape(@"SELECT VALUE root FROM root WHERE root[""EntityId""] = ""abc"""), Shape(@"SELECT VALUE root FROM root WHERE root[""EntityId""] = ""xyz"""));
        Assert.NotEqual(Shape(@"SELECT VALUE root FROM root WHERE root[""EntityId""] = ""abc"""), Shape(@"SELECT VALUE root FROM root WHERE root[""Status""] = ""abc"""));
    }

    [Fact]
    public void A_placeholder_in_a_statement_is_not_a_query_string()
    {
        // A path's ? starts its query string; a statement's ? is a parameter, and the head keeps what follows.
        var shaped = Assert.Single(InteractionShape.Calls([Request("t1", "QUERY", "/", "db", "Api", Guid.NewGuid(), category: "database", content: "SELECT * FROM t WHERE a = ? AND b = ?")]));

        Assert.Contains("AND b = ?", shaped.Uri);
    }

    [Fact]
    public void The_shape_rule_has_a_version_a_run_records()
    {
        // Fingerprints are only comparable when the same rule made them; the version rides on the run line so
        // the analyzer can tell, and it moves when the rule does. 5: a MongoDB request note became the command without
        // the driver's fields (#168 R2), so every MongoDB call's statement head moved. 6: a document's booleans, signs
        // and list lengths are data too (the #168 audit). 7: a remote host the HTTP handler names by its fallback keeps
        // its name, where 6 recorded localhost (#190 R0); 6 is kept, so a run bridges it. 8: the operation key (#192 R1):
        // the path as the data file writes it, an entity in the authority, a GraphQL operation, a statement past its SET
        // prologue; 6 and 7 are kept. 9: what the BigQuery and Cloud Storage handlers name (#125 R1). 10: where an Oracle
        // call is drawn, the server and the service its data source names (CLIENT_FLOORS_PLAN.md F26); 6 to 9 are kept.
        Assert.Equal(10, InteractionShape.Version);
        Assert.Equal([6, 7, 8, 9, 10], InteractionShape.KeptVersions);
    }

    [Fact]
    public void Rule_6_reads_a_host_the_fallback_named_as_localhost_and_nothing_else()
    {
        RequestResponseLog Log(string service, bool fallback, RequestResponseType type, Guid id) =>
            new("t", "t1", HttpMethod.Get, null, new Uri("https://pricing.example.com:8443/v1/prices"), [], service, "Caller", type, Guid.NewGuid(), id, TrackingIgnore: false)
                { NamedByRemoteHostFallback = fallback };
        var named = Guid.NewGuid();
        var mapped = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            Log("pricing.example.com:8443", true, RequestResponseType.Request, named), Log("pricing.example.com:8443", true, RequestResponseType.Response, named),
            Log("Pricing", false, RequestResponseType.Request, mapped), Log("Pricing", false, RequestResponseType.Response, mapped)
        ];

        Assert.Equal(["localhost:8443", "Pricing"], InteractionShape.Calls(logs, null, 6).Select(c => c.Service));
        Assert.Equal(["pricing.example.com:8443", "Pricing"], InteractionShape.Calls(logs, null, 7).Select(c => c.Service));
        Assert.Equal(new Dictionary<string, string> { ["Caller>pricing.example.com:8443"] = "Caller>localhost:8443" }, InteractionShape.RenamedDependencies(logs, 6));
        Assert.Empty(InteractionShape.RenamedDependencies(logs, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionShape.Calls(logs, null, 5));
    }

    [Fact]
    public void Rules_6_and_7_read_a_call_as_its_tracker_recorded_it_before_8()
    {
        // #192 R1 classifies a SQL Server batch past both of its SET statements; 5.16.1 read it as Other with no table,
        // and at Summarised did not record it. The tracker keeps the old record on the call, so rules 6 and 7 still
        // read it as it was, and only rule 8 reads the statement.
        const string batch = "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nINSERT INTO [Orders] ([Status]) VALUES (@p0);";
        RequestResponseLog Log(RequestResponseType type, Guid id, RecordedBefore? before) =>
            new("t", "t1", "INSERT INTO Orders", type == RequestResponseType.Request ? batch : null, new Uri("sqlserver://db/app/Orders"), [], "Database", "Caller", type,
                Guid.NewGuid(), id, TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SqlServer) { RecordedBefore8 = before };
        var (detailed, summarised) = (Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLog[] logs =
        [
            Log(RequestResponseType.Request, detailed, new RecordedBefore("Other", new Uri("sqlserver://db/app"))), Log(RequestResponseType.Response, detailed, null),
            Log(RequestResponseType.Request, summarised, RecordedBefore.NotRecorded), Log(RequestResponseType.Response, summarised, null)
        ];

        Assert.Equal(["Caller>Database Other /app SET IMPLICIT_TRANSACTIONS OFF; -"], InteractionShape.Calls(logs, null, 7).Select(c => c.ToString()));
        Assert.Equal(InteractionShape.Calls(logs, null, 7), InteractionShape.Calls(logs, null, 6));
        Assert.Equal(
            ["Caller>Database INSERT INTO Orders /app/Orders INSERT INTO [Orders] ([Status]) VALUES (@p0); -", "Caller>Database INSERT INTO Orders /app/Orders INSERT INTO [Orders] ([Status]) VALUES (@p0); -"],
            InteractionShape.Calls(logs, null, 8).Select(c => c.ToString()));
    }

    [Fact]
    public void A_documents_booleans_signs_and_list_lengths_are_data()
    {
        // Since 4.20.0 a MongoDB write's documents are its statement head, and a test that generates its data wrote a
        // new shape whenever a flag flipped, a number went negative or a list grew.
        Assert.Equal(
            InteractionShape.TemplateStatement("{ \"documents\" : [{ \"approved\" : true, \"delta\" : -5, \"tags\" : [\"a\", \"b\"], \"rates\" : [1.5, -2.0] }] }"),
            InteractionShape.TemplateStatement("{ \"documents\" : [{ \"approved\" : false, \"delta\" : 7, \"tags\" : [\"c\"], \"rates\" : [3.25] }] }"));
        Assert.Equal(
            InteractionShape.TemplateStatement("{ \"filter\" : { \"_id\" : { \"$in\" : [1, 2, 3] } } }"),
            InteractionShape.TemplateStatement("{ \"filter\" : { \"_id\" : { \"$in\" : [4] } } }"));
    }

    [Fact]
    public void A_documents_keys_nulls_and_flags_still_tell_shapes_apart()
    {
        // A null filter matches what a valued one does not, a key is the statement's own, and so is a flag that is there.
        Assert.NotEqual(
            InteractionShape.TemplateStatement("{ \"filter\" : { \"deletedAt\" : null } }"),
            InteractionShape.TemplateStatement("{ \"filter\" : { \"deletedAt\" : \"x\" } }"));
        Assert.NotEqual(
            InteractionShape.TemplateStatement("{ \"updates\" : [{ \"q\" : { \"_id\" : 1 }, \"u\" : { \"$set\" : { \"a\" : 1 } }, \"upsert\" : true }] }"),
            InteractionShape.TemplateStatement("{ \"updates\" : [{ \"q\" : { \"_id\" : 1 }, \"u\" : { \"$set\" : { \"a\" : 1 } } }] }"));
        Assert.NotEqual(
            InteractionShape.TemplateStatement("{ \"filter\" : { \"tags\" : [\"a\", 1] } }"),
            InteractionShape.TemplateStatement("{ \"filter\" : { \"tags\" : [\"a\"] } }"));
        // A run folds whole elements only: an integer and a fraction are two elements, not one and a half.
        Assert.NotEqual(
            InteractionShape.TemplateStatement("{ \"filter\" : { \"rates\" : [1, 2.5] } }"),
            InteractionShape.TemplateStatement("{ \"filter\" : { \"rates\" : [2.5] } }"));
    }

    [Fact]
    public void Two_finds_with_different_filters_have_different_shapes()
    {
        Assert.NotEqual(
            InteractionShape.TemplateStatement("{ \"filter\" : { \"status\" : \"Ready\" } }"),
            InteractionShape.TemplateStatement("{ \"filter\" : { \"loanNumber\" : \"L-1\" } }"));
    }

    [Fact]
    public void A_querys_booleans_and_signs_are_left_as_they_were()
    {
        // The rule changed for documents only: a query's `- 1` is arithmetic, and its keywords are its own.
        Assert.Equal("SELECT * FROM t WHERE a - {n} > {n} AND b = true", InteractionShape.TemplateStatement("SELECT * FROM t WHERE a - 1 > 0 AND b = true"));
    }

    [Fact]
    public void A_repeated_call_does_not_change_the_set_fingerprint()
    {
        // A retry against a throttled emulator is the same call again. The set says which calls were made;
        // how many times is the call count, which the analyzer judges on the scenario's own record.
        var once = new[] { new ShapeCall("Api", "CosmosDB", "Create", "/orders", "200") };
        var thrice = new[] { once[0], once[0], once[0] };

        Assert.Equal(InteractionShape.Fingerprint(once).ShapeSet, InteractionShape.Fingerprint(thrice).ShapeSet);
        Assert.NotEqual(InteractionShape.Fingerprint(once).ShapeOrdered, InteractionShape.Fingerprint(thrice).ShapeOrdered);
        Assert.Equal(3, InteractionShape.Fingerprint(thrice).Calls);
    }
}
