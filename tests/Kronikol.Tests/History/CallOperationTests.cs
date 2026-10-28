using Kronikol.Constants;
using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// The operation key: what a call is, comparable across calls and runs, one function behind history, the warm-up marks,
/// the component diagram and the engine (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.1). It reads what the data file
/// holds, the URI as the data file writes it and the request's content, so a report read later keys a call as its run
/// did.
/// </summary>
public class CallOperationTests
{
    private static string Key(string method, string uri, string? category = null, string? content = null, HistoryShapeRules? rules = null, bool queryKeys = false) =>
        CallOperation.Of(method, uri, category, content, rules, queryKeys);

    [Fact]
    public void The_path_is_templated_without_its_authority_query_or_fragment()
    {
        Assert.Equal("/orders/{n}", Key("GET", "http://localhost:54321/orders/123?page=2#top"));
        Assert.Equal("/orders/{n}", Key("GET", "https://orders.example.com/orders/456"));
    }

    [Fact]
    public void An_entity_a_tracker_writes_in_the_authority_is_kept()
    {
        // Service Bus at Summarised, S3, EventBridge and Redis at Detailed and Summarised name their entity where a host
        // would go: dropped, two queues were one operation.
        Assert.Equal("/orders/", Key("Send", "servicebus://orders/", DependencyCategories.ServiceBus));
        Assert.Equal("/payments/", Key("Send", "servicebus://payments/", DependencyCategories.ServiceBus));
        Assert.Equal("/orders-topic/Subscriptions/audit/", Key("Receive", "servicebus://orders-topic/Subscriptions/audit/", DependencyCategories.ServiceBus));
        Assert.Equal("/receipts/{n}/{n}.pdf", Key("PutObject", "s3://receipts/2026/0042.pdf", DependencyCategories.S3));
        Assert.Equal("/orders-bus/", Key("PutEvents", "eventbridge://orders-bus/"));
        Assert.Equal("/db3/user:{n}", Key("Get", "redis://db3/user:42", DependencyCategories.Redis));
        // Written by something other than System.Uri, the entity keeps the case it was given; the key does not.
        Assert.Equal("/orders/", Key("Send", "servicebus://Orders/", DependencyCategories.ServiceBus));
        // A per-run entity is templated like any segment.
        Assert.Equal("/orders-{id}/", Key("Send", "servicebus://orders-550e8400-e29b-41d4-a716-446655440000/", DependencyCategories.ServiceBus));
    }

    [Fact]
    public void A_host_in_the_authority_is_dropped()
    {
        Assert.Equal("/{n}/user:{n}", Key("Get", "redis://localhost:6379/0/user:42", DependencyCategories.Redis));
        Assert.Equal("/orders", Key("Send", "servicebus://ns.servicebus.windows.net/orders", DependencyCategories.MessageQueue));
        Assert.Equal("/main", Key("Select", "sqlite://localhost/main"));
        Assert.Equal("/q", Key("Send", "amqp://guest:secret@localhost:5672/q"));
        Assert.Equal("/IOrders/Get", Key("Get", "mock://Orders/IOrders/Get"));
    }

    [Fact]
    public void Query_keys_follow_sorted_when_asked_for()
    {
        Assert.Equal("/orders/{n}?page&sort", Key("GET", "http://localhost/orders/123?sort=desc&page=2", queryKeys: true));
        Assert.Equal("/orders/{n}", Key("GET", "http://localhost/orders/123?sort=desc&page=2"));
    }

    [Fact]
    public void An_escaped_path_reads_as_the_data_file_writes_it()
    {
        // The data file writes Uri.ToString(), which unescapes: history used to template the escaped form, where an
        // escape's digits read as a number of their own (%20 became %{n}).
        var uri = new Uri("http://localhost/files/report%202024.pdf").ToString();
        Assert.Equal("/files/report {n}.pdf", Key("GET", uri));
    }

    [Fact]
    public void Bytes_written_raw_are_bin()
    {
        // InteractionShapeTests pins the escaped form; the data file's string carries the control character itself.
        Assert.Equal("/{n}/cache/key-{bin}/next", Key("Get", "redis:///0/cache/key-ab\u000ccd/next"));
        Assert.Equal("/{n}/app:_customerDates_{n}-{bin}", Key("Get", "redis:///0/app:_customerDates_100000000000001-\u001d(�4��:"));
    }

    [Fact]
    public void Consumer_rules_see_the_unescaped_path()
    {
        // Q17: the warm-up marks always applied the rules to the data file's string; history now does too.
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate(@"user\|\d+", "{user}")]);
        Assert.Equal("/users/{user}", Key("GET", new Uri("http://localhost/users/user%7C12345").ToString(), rules: rules));
    }

    [Fact]
    public void A_long_opaque_segment_is_kept()
    {
        Assert.Equal("/topics/breakfast-equipment-alerts/PancakeBatchCompletedEvent", Key("POST", "http://localhost/topics/breakfast-equipment-alerts/PancakeBatchCompletedEvent"));
        Assert.Equal("/files/SGVsbG8gV29ybGQ", Key("GET", "http://localhost/files/SGVsbG8gV29ybGQ"));
    }

    [Fact]
    public void A_statement_adds_its_templated_head()
    {
        Assert.Equal("/main/Orders SELECT * FROM \"Orders\" WHERE \"Id\" = {n}",
            Key("Select", "sql:///main/Orders", DependencyCategories.SQL, "SELECT * FROM \"Orders\" WHERE \"Id\" = 42"));
    }

    [Fact]
    public void A_set_prologue_is_not_the_statement_head()
    {
        var batch = "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nINSERT INTO [Orders] ([Status]) OUTPUT INSERTED.[Id] VALUES (@p0);";
        var update = "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nUPDATE [Orders] SET [Status] = @p0 WHERE [Id] = @p1;";
        var insertKey = Key("Insert", "sql:///BreakfastProvider/Orders", DependencyCategories.SqlServer, batch);
        Assert.StartsWith("/BreakfastProvider/Orders INSERT INTO [Orders]", insertKey);
        Assert.NotEqual(insertKey, Key("Insert", "sql:///BreakfastProvider/Orders", DependencyCategories.SqlServer, update));
        Assert.Equal("/BreakfastProvider/Orders INSERT INTO [Orders] ([Status]) VALUES (@p0);",
            Key("Insert", "sql:///BreakfastProvider/Orders", DependencyCategories.SqlServer, "SET NOCOUNT ON; INSERT INTO [Orders] ([Status]) VALUES (@p0);"));
    }

    [Fact]
    public void A_statement_of_set_lines_alone_keeps_them_as_its_head()
    {
        Assert.Equal("/BreakfastProvider SET NOCOUNT ON;", Key("Other", "sql:///BreakfastProvider", DependencyCategories.SqlServer, "SET NOCOUNT ON;"));
    }

    [Fact]
    public void A_query_carried_in_a_document_is_templated_as_a_statement()
    {
        var poller = """{"query":"SELECT VALUE root FROM root WHERE true","parameters":[]}""";
        var filtered = """{"query":"SELECT * FROM c WHERE c.status = @status","parameters":[{"name":"@status","value":"Pending"}]}""";
        var pollerKey = Key("Query", "https://localhost:8081/dbs/db/colls/orders", DependencyCategories.CosmosDB, poller);
        Assert.Equal("/dbs/db/colls/orders SELECT VALUE root FROM root WHERE true", pollerKey);
        Assert.NotEqual(pollerKey, Key("Query", "https://localhost:8081/dbs/db/colls/orders", DependencyCategories.CosmosDB, filtered));
        // A document that is the data, not a query, keeps the document rule: its values are data.
        Assert.Equal("/dbs/db/colls/orders {\"id\":\"{v}\",\"status\":\"{v}\"}",
            Key("Create", "https://localhost:8081/dbs/db/colls/orders", DependencyCategories.CosmosDB, """{"id":"a1","status":"Pending"}"""));
    }

    [Fact]
    public void Two_graphql_operations_on_one_endpoint_are_two_keys()
    {
        Assert.Equal("/graphql query GetUser", Key("POST", "http://localhost/graphql", content: """{"query":"query GetUser { user { name } }"}"""));
        Assert.Equal("/graphql mutation CreateOrder", Key("POST", "http://localhost/graphql", content: """{"query":"mutation CreateOrder { createOrder { id } }"}"""));
    }

    [Fact]
    public void Two_anonymous_operations_with_different_root_fields_are_two_keys()
    {
        Assert.Equal("/graphql query { orderSummaries }", Key("POST", "http://localhost/graphql", content: """{"query":"{ orderSummaries { orderId } }"}"""));
        Assert.Equal("/graphql query { recipeReports }", Key("POST", "http://localhost/graphql", content: """{"query":"{ recipeReports { recipeType } }"}"""));
    }

    [Fact]
    public void A_get_is_keyed_by_the_operation_in_its_query_string()
    {
        Assert.Equal("/graphql query { user }", Key("GET", "http://localhost/graphql?query={ user { name } }"));
        Assert.Equal("/search", Key("GET", "http://localhost/search?query=mutation testing"));
    }

    [Fact]
    public void A_batch_of_operations_is_marked()
    {
        Assert.Equal("/graphql batch", Key("POST", "http://localhost/graphql", content: """[{"query":"query A { a }"},{"query":"{ b }"}]"""));
        Assert.Equal("/orders/bulk", Key("POST", "http://localhost/orders/bulk", content: """[{"sku":"A1"},{"sku":"B2"}]"""));
    }

    [Fact]
    public void A_rest_post_has_no_operation()
    {
        Assert.Equal("/orders", Key("POST", "http://localhost/orders", content: """{"orderId":1,"query":"pancakes"}"""));
    }

    [Fact]
    public void Only_a_post_body_or_a_get_query_names_an_operation()
    {
        // The engine reads a POST's body and a GET's URI and nothing else (plan 4.4), so the run keys no other call by
        // its body either, and a report read later keys every call as its run did.
        Assert.Equal("/graphql", Key("PUT", "http://localhost/graphql", content: """{"query":"query GetUser { user { name } }"}"""));
        Assert.Equal("/graphql/", Key("Send", "servicebus://graphql/", DependencyCategories.ServiceBus, """{"query":"query GetUser { user { name } }"}"""));
        Assert.Equal("/graphql", Key("POST", "http://localhost/graphql?query={ user { name } }"));
        Assert.Equal("/graphql query GetUser", Key("post", "http://localhost/graphql", content: """{"query":"query GetUser { user { name } }"}"""));
    }

    [Fact]
    public void The_parts_are_the_target_and_the_operation_the_key_joins()
    {
        // The service card writes them apart (#191). A path the data file unescapes can hold a space, so the key is not cut.
        Assert.Equal(("/a b/graphql", "query GetUser"),
            CallOperation.Parts("POST", new Uri("http://localhost/a%20b/graphql").ToString(), null, """{"query":"query GetUser { user { name } }"}""", null, queryKeys: false));
        Assert.Equal(("/graphql", "batch"),
            CallOperation.Parts("POST", "http://localhost/graphql", null, """[{"query":"{ a }"},{"query":"{ b }"}]""", null, queryKeys: false));
        Assert.Equal(("/OrdersDb/Orders SELECT [o].[Id] FROM [Orders] AS [o] WHERE [o].[Id] = @p0", (string?)null),
            CallOperation.Parts("SELECT", "sql:///OrdersDb/Orders", DependencyCategories.SqlServer, "SELECT [o].[Id] FROM [Orders] AS [o] WHERE [o].[Id] = @p0", null, queryKeys: false));
        Assert.Equal(("/orders/{n}", (string?)null), CallOperation.Parts("GET", "http://localhost/orders/7", null, null, null, queryKeys: false));

        foreach (var (method, uri, category, content) in new (string, string, string?, string?)[]
                 {
                     ("POST", new Uri("http://localhost/a%20b/graphql").ToString(), null, """{"query":"query GetUser { user { name } }"}"""),
                     ("SELECT", "sql:///OrdersDb/Orders", DependencyCategories.SqlServer, "SELECT 1"),
                     ("GET", "http://localhost/orders/7", null, null)
                 })
        {
            var (target, operation) = CallOperation.Parts(method, uri, category, content, null, queryKeys: true);
            Assert.Equal(Key(method, uri, category, content, queryKeys: true), operation is null ? target : target + " " + operation);
        }
    }
}
