using System.Net;
using Kronikol.History;
using Kronikol.Tracking;
using DependencyCategories = Kronikol.Constants.DependencyCategories;

namespace Kronikol.Tests.History;

/// <summary>
/// The guard's core half (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): one capture of every form a tracker records, as
/// each kept rule reads it, pinned. Every kept rule is a function of the raw call, and a bridge is only as good as that
/// function's agreement with the release that made the rule: a change to the templater, the operation key or a
/// statement's head that moves rule 6's or 7's lines would bridge to fingerprints no real run of theirs ever made. The
/// tracker half pins what each tracker records, in the tracker's own project (<see cref="HistoryGuard"/>).
/// </summary>
public class ShapeGuardTests
{
    private static IEnumerable<RequestResponseLog> Call(string test, OneOf<HttpMethod, string> method, string uri, string service, string? content = null,
        string? category = null, OneOf<HttpStatusCode, string>? status = null, Func<RequestResponseLog, RequestResponseLog>? request = null, string caller = "TestCaller")
    {
        var pair = Guid.NewGuid();
        var address = new Uri(uri, UriKind.RelativeOrAbsolute);
        var sent = new RequestResponseLog("t", test, method, content, address, [], service, caller, RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false,
            DependencyCategory: category);
        yield return request is null ? sent : request(sent);
        if (status is not null)
            yield return new RequestResponseLog("t", test, method, null, address, [], service, caller, RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false,
                StatusCode: status, DependencyCategory: category);
    }

    /// <summary>One capture of each form, in the order the pins list them.</summary>
    private static readonly (string Name, RequestResponseLog[] Logs)[] Corpus =
    [
        ("http", [.. Call("c", HttpMethod.Post, "http://target-service:5000/api/items/42?expand=1&b=", "TargetService", status: HttpStatusCode.OK)]),
        ("http named by its address", [.. Call("c", HttpMethod.Get, "http://orders-host:8080/orders/7", "orders-host:8080", status: HttpStatusCode.OK,
            request: r => r with { NamedByRemoteHostFallback = true })]),
        ("http relative", [.. Call("c", HttpMethod.Put, "/relative/7", "Api", status: HttpStatusCode.NoContent)]),
        ("http escapes", [.. Call("c", HttpMethod.Get, "http://files/a%2Fb/c%20d/caf%C3%A9/key%00tenant", "Files", status: HttpStatusCode.OK)]),
        ("graphql body", [.. Call("c", HttpMethod.Post, "http://api/graphql", "Api", """{"query":"query GetUser($id: ID!) { user(id: $id) { name } }"}""", status: HttpStatusCode.OK)]),
        ("graphql anonymous", [.. Call("c", HttpMethod.Post, "http://api/graphql", "Api", """{"query":"{ menu { items { name } } }"}""", status: HttpStatusCode.OK)]),
        ("graphql get", [.. Call("c", HttpMethod.Get, "http://api/graphql?query=query%20Menu%20%7B%20items%20%7D", "Api", status: HttpStatusCode.OK)]),
        ("graphql batch", [.. Call("c", HttpMethod.Post, "http://api/graphql", "Api", """[{"query":"{ a }"},{"query":"{ b }"}]""", status: HttpStatusCode.OK)]),
        ("sql set batch", [.. Call("c", "INSERT INTO Orders", "sql://localhost/app/Orders", "Database", "SET NOCOUNT ON; SET XACT_ABORT ON;\nINSERT INTO [Orders] ([Total]) VALUES (42)",
            DependencyCategories.SQL, "OK", r => r with { RecordedBefore8 = new RecordedBefore("Other", new Uri("sql://localhost/app")) })]),
        ("sql set batch not recorded before 8", [.. Call("c", "INSERT INTO Audit", "sql://localhost/app/Audit", "Database", "SET NOCOUNT ON; SET XACT_ABORT ON;\nINSERT INTO [Audit] VALUES (1)",
            DependencyCategories.SQL, "OK", r => r with { RecordedBefore8 = RecordedBefore.NotRecorded })]),
        ("ef summarised", [.. Call("c", "Select", "sql:///OrdersDb/Users", "Database", "SELECT [u].[Id] FROM [dbo].[Users] AS [u] WHERE [u].[Id] = 5",
            DependencyCategories.SQL, "OK", r => r with { RecordedBefore8 = new RecordedBefore("Select", new Uri("sql://OrdersDb/Users")) })]),
        ("dapper", [.. Call("c", "SELECT FROM Users", "sql:///localhost/TestDb/Users", "TestDB", "SELECT * FROM Users WHERE Id = 1", DependencyCategories.SQL, "OK")]),
        ("sqlclient", [.. Call("c", "SELECT FROM breakfasts", "sqlserver://localhost/analytics/breakfasts", "SQL Server", "SELECT id, name FROM breakfasts", DependencyCategories.SqlServer, "OK")]),
        ("npgsql", [.. Call("c", "SELECT FROM breakfasts", "postgresql://localhost/analytics/breakfasts", "PostgreSQL", "SELECT id, name FROM breakfasts WHERE name = 'x'", DependencyCategories.PostgreSQL, "OK")]),
        ("clickhouse", [.. Call("c", "INSERT INTO events", "clickhouse://ch-host:8123/analytics/events", "Analytics", "INSERT INTO events (name) VALUES ('x')", DependencyCategories.ClickHouse, "OK")]),
        ("redis", [.. Call("c", "Get", "redis://db0/user:123", "Redis", "GET user:123", DependencyCategories.Redis, "OK")]),
        ("redis raw endpoint", [.. Call("c", "Get", "redis://cache:6379/0/user:123", "Redis", "GET user:123", DependencyCategories.Redis, "OK",
            r => r with { RecordedBefore8 = new RecordedBefore("Get", new Uri("redis://Unspecified/cache:6379/0/user:123")) })]),
        ("mongodb", [.. Call("c", "Find ← testcollection", "mongodb:///testdb/testcollection", "MongoDB", """{"find":"testcollection","filter":{"_id":"abc","n":-3,"ok":true}}""",
            DependencyCategories.MongoDB, HttpStatusCode.OK)]),
        ("cosmos create", [.. Call("c", "Create", "https://account.documents.azure.com/dbs/mydb/colls/orders/docs", "CosmosDB", """{"id":"order-1","total":42.50}""",
            DependencyCategories.CosmosDB, HttpStatusCode.Created)]),
        ("cosmos query", [.. Call("c", "Query", "https://account.documents.azure.com/dbs/mydb/colls/orders/docs", "CosmosDB",
            """{"query":"SELECT * FROM c WHERE c.id = @id","parameters":[{"name":"@id","value":"order-1"}]}""", DependencyCategories.CosmosDB, HttpStatusCode.OK)]),
        ("bigquery", [.. Call("c", "Query", "bigquery:///myproject", "BigQuery", """{"query":"SELECT name FROM ds.t WHERE id = 7","useLegacySql":false}""", DependencyCategories.BigQuery, HttpStatusCode.OK)]),
        ("spanner", [.. Call("c", "SELECT FROM Users", "spanner:///Users", "Spanner", "SELECT * FROM Users WHERE Id = 3", DependencyCategories.Spanner)]),
        ("dynamodb", [.. Call("c", "PutItem", "dynamodb:///Users", "DynamoDB", """{"TableName":"Users","Item":{"id":{"S":"u-1"}}}""", DependencyCategories.DynamoDB, HttpStatusCode.OK)]),
        ("elasticsearch", [.. Call("c", "Search → orders", "elasticsearch:///orders", "Elasticsearch", """{"query":{"match":{"name":"pancake"}}}""", DependencyCategories.Elasticsearch, HttpStatusCode.OK)]),
        ("bigtable", [.. Call("c", "ReadRows ← my-table", "bigtable:///my-table", "Bigtable", category: DependencyCategories.Bigtable)]),
        ("atlas", [.. Call("c", "insertOne → orders", "atlas:///orders_db/orders", "AtlasDataApi", """{"document":{"name":"x"}}""", DependencyCategories.AtlasDataApi, HttpStatusCode.OK)]),
        ("s3", [.. Call("c", "GetObject", "s3://my-bucket/path/to/file.json", "S3", category: DependencyCategories.S3, status: HttpStatusCode.OK)]),
        ("blob", [.. Call("c", "Upload", "https://account.blob.core.windows.net/mycontainer/myblob.txt", "BlobStorage", "hello world", DependencyCategories.BlobStorage, HttpStatusCode.Created)]),
        ("gcs", [.. Call("c", "Download → my-bucket/file.txt", "gcs:///my-bucket/file.txt", "CloudStorage", status: HttpStatusCode.OK)]),
        ("servicebus", [.. Call("c", "Send → orders-queue", "servicebus://orders-queue/", "ServiceBus", "message body", DependencyCategories.ServiceBus)]),
        ("sqs", [.. Call("c", "SendMessage", "sqs:///orders-queue", "SQS", category: DependencyCategories.MessageQueue, status: HttpStatusCode.OK)]),
        ("sns", [.. Call("c", "Publish", "sns:///my-topic", "SNS", category: DependencyCategories.MessageQueue, status: HttpStatusCode.OK)]),
        ("eventbridge", [.. Call("c", "PutEvents [OrderCreated]", "eventbridge://default/", "EventBridge", category: DependencyCategories.MessageQueue, status: HttpStatusCode.OK)]),
        ("eventhubs", [.. Call("c", "Send → telemetry", "eventhubs:///telemetry", "EventHubs", "hello", DependencyCategories.MessageQueue)]),
        ("kafka", [.. Call("c", "Produce → orders-topic", "kafka:///orders-topic", "Kafka", "hello", DependencyCategories.MessageQueue)]),
        ("pubsub", [.. Call("c", "Publish → my-topic", "pubsub:///my-topic", "PubSub", "hello", DependencyCategories.MessageQueue)]),
        ("storagequeue", [.. Call("c", "Receive ← my-queue", "storagequeue:///my-queue", "StorageQueue", category: DependencyCategories.MessageQueue, status: HttpStatusCode.OK)]),
        ("masstransit", [.. Call("c", "Send OrderCreated", "masstransit:///orders", "MassTransit", category: DependencyCategories.MessageQueue)]),
        ("grpc", [.. Call("c", "SayHello", "grpc:///greet.Greeter/SayHello", "GrpcService", "Hello", "gRPC", HttpStatusCode.OK)]),
        ("mediatr", [.. Call("c", "Send", "mock://Application/IMediator/Send", "Application", status: HttpStatusCode.OK, caller: "Caller")]),
        ("otlp mongo", [.. Call("c", "Find ← Trial", "mongodb:///data-insights-Development/Trial", "mongo", """{"find":"Trial"}""", DependencyCategories.MongoDB, "OK",
            r => r with { RecordedBefore8 = new RecordedBefore("Find ← Trial", new Uri("http://unknown/")) }, caller: "data-insights")]),
        ("ingest", [.. Call("c", HttpMethod.Post, "http://localhost:8081/sidekick?x=1", "graphql", """{"query":"{ me }"}""", "AI", HttpStatusCode.Created, caller: "web")])
    ];

    /// <summary>
    /// The lines each kept rule makes of the corpus, one per capture, in corpus order. A change that moves one of the
    /// current rule's moves <see cref="InteractionShape.Version"/>, keeps the old line here under the old version, and
    /// pins the new; a change that moves an earlier rule's breaks the bridge to every run that rule made, and is a bug.
    /// </summary>
    private static readonly Dictionary<int, string[]> Pins = new()
    {
        [6] =
        [
            "TestCaller>TargetService POST /api/items/{n}?b&expand 200",
            "TestCaller>localhost:8080 GET /orders/{n} 200",
            "TestCaller>Api PUT /relative/{n} 204",
            "TestCaller>Files GET /a%2Fb/c%20d/caf%C3%A9/{bin} 200",
            "TestCaller>Api POST /graphql 200",
            "TestCaller>Api POST /graphql 200",
            "TestCaller>Api GET /graphql?query 200",
            "TestCaller>Api POST /graphql 200",
            "TestCaller>Database Other /app SET NOCOUNT ON; SET XACT_ABORT ON; OK",
            "",
            "TestCaller>Database Select /Users SELECT [u].[Id] FROM [dbo].[Users] AS [u] WHERE [u].[Id] = {n} OK",
            "TestCaller>TestDB SELECT FROM Users /localhost/TestDb/Users SELECT * FROM Users WHERE Id = {n} OK",
            "TestCaller>SQL Server SELECT FROM breakfasts /analytics/breakfasts SELECT id, name FROM breakfasts OK",
            "TestCaller>PostgreSQL SELECT FROM breakfasts /analytics/breakfasts SELECT id, name FROM breakfasts WHERE name = '{s}' OK",
            "TestCaller>Analytics INSERT INTO events /analytics/events INSERT INTO events (name) VALUES ('{s}') OK",
            "TestCaller>Redis Get /user:{n} GET user:{n} OK",
            "TestCaller>Redis Get /cache:{n}/{n}/user:{n} GET user:{n} OK",
            "TestCaller>MongoDB Find ← testcollection /testdb/testcollection {\"find\":\"{v}\",\"filter\":{\"_id\":\"{v}\",\"n\":{n},\"ok\":{b}}} 200",
            "TestCaller>CosmosDB Create /dbs/mydb/colls/orders/docs {\"id\":\"{v}\",\"total\":{n}.{n}} 201",
            "TestCaller>CosmosDB Query /dbs/mydb/colls/orders/docs {\"query\":\"{v}\",\"parameters\":[{\"name\":\"{v}\",\"value\":\"{v}\"}]} 200",
            "TestCaller>BigQuery Query /myproject {\"query\":\"{v}\",\"useLegacySql\":{b}} 200",
            "TestCaller>Spanner SELECT FROM Users /Users SELECT * FROM Users WHERE Id = {n} -",
            "TestCaller>DynamoDB PutItem /Users {\"TableName\":\"{v}\",\"Item\":{\"id\":{\"S\":\"{v}\"}}} 200",
            "TestCaller>Elasticsearch Search → orders /orders {\"query\":{\"match\":{\"name\":\"{v}\"}}} 200",
            "TestCaller>Bigtable ReadRows ← my-table /my-table -",
            "TestCaller>AtlasDataApi insertOne → orders /orders_db/orders {\"document\":{\"name\":\"{v}\"}} 200",
            "TestCaller>S3 GetObject /path/to/file.json 200",
            "TestCaller>BlobStorage Upload /mycontainer/myblob.txt 201",
            "TestCaller>CloudStorage Download → my-bucket/file.txt /my-bucket/file.txt 200",
            "TestCaller>ServiceBus Send → orders-queue / -",
            "TestCaller>SQS SendMessage /orders-queue 200",
            "TestCaller>SNS Publish /my-topic 200",
            "TestCaller>EventBridge PutEvents [OrderCreated] / 200",
            "TestCaller>EventHubs Send → telemetry /telemetry -",
            "TestCaller>Kafka Produce → orders-topic /orders-topic -",
            "TestCaller>PubSub Publish → my-topic /my-topic -",
            "TestCaller>StorageQueue Receive ← my-queue /my-queue 200",
            "TestCaller>MassTransit Send OrderCreated /orders -",
            "TestCaller>GrpcService SayHello /greet.Greeter/SayHello 200",
            "Caller>Application Send /IMediator/Send 200",
            "data-insights>mongo Find ← Trial / {\"find\":\"{v}\"} OK",
            "web>graphql POST /sidekick?x 201"
        ],
        [7] =
        [
            "TestCaller>TargetService POST /api/items/{n}?b&expand 200",
            "TestCaller>orders-host:8080 GET /orders/{n} 200",
            "TestCaller>Api PUT /relative/{n} 204",
            "TestCaller>Files GET /a%2Fb/c%20d/caf%C3%A9/{bin} 200",
            "TestCaller>Api POST /graphql 200",
            "TestCaller>Api POST /graphql 200",
            "TestCaller>Api GET /graphql?query 200",
            "TestCaller>Api POST /graphql 200",
            "TestCaller>Database Other /app SET NOCOUNT ON; SET XACT_ABORT ON; OK",
            "",
            "TestCaller>Database Select /Users SELECT [u].[Id] FROM [dbo].[Users] AS [u] WHERE [u].[Id] = {n} OK",
            "TestCaller>TestDB SELECT FROM Users /localhost/TestDb/Users SELECT * FROM Users WHERE Id = {n} OK",
            "TestCaller>SQL Server SELECT FROM breakfasts /analytics/breakfasts SELECT id, name FROM breakfasts OK",
            "TestCaller>PostgreSQL SELECT FROM breakfasts /analytics/breakfasts SELECT id, name FROM breakfasts WHERE name = '{s}' OK",
            "TestCaller>Analytics INSERT INTO events /analytics/events INSERT INTO events (name) VALUES ('{s}') OK",
            "TestCaller>Redis Get /user:{n} GET user:{n} OK",
            "TestCaller>Redis Get /cache:{n}/{n}/user:{n} GET user:{n} OK",
            "TestCaller>MongoDB Find ← testcollection /testdb/testcollection {\"find\":\"{v}\",\"filter\":{\"_id\":\"{v}\",\"n\":{n},\"ok\":{b}}} 200",
            "TestCaller>CosmosDB Create /dbs/mydb/colls/orders/docs {\"id\":\"{v}\",\"total\":{n}.{n}} 201",
            "TestCaller>CosmosDB Query /dbs/mydb/colls/orders/docs {\"query\":\"{v}\",\"parameters\":[{\"name\":\"{v}\",\"value\":\"{v}\"}]} 200",
            "TestCaller>BigQuery Query /myproject {\"query\":\"{v}\",\"useLegacySql\":{b}} 200",
            "TestCaller>Spanner SELECT FROM Users /Users SELECT * FROM Users WHERE Id = {n} -",
            "TestCaller>DynamoDB PutItem /Users {\"TableName\":\"{v}\",\"Item\":{\"id\":{\"S\":\"{v}\"}}} 200",
            "TestCaller>Elasticsearch Search → orders /orders {\"query\":{\"match\":{\"name\":\"{v}\"}}} 200",
            "TestCaller>Bigtable ReadRows ← my-table /my-table -",
            "TestCaller>AtlasDataApi insertOne → orders /orders_db/orders {\"document\":{\"name\":\"{v}\"}} 200",
            "TestCaller>S3 GetObject /path/to/file.json 200",
            "TestCaller>BlobStorage Upload /mycontainer/myblob.txt 201",
            "TestCaller>CloudStorage Download → my-bucket/file.txt /my-bucket/file.txt 200",
            "TestCaller>ServiceBus Send → orders-queue / -",
            "TestCaller>SQS SendMessage /orders-queue 200",
            "TestCaller>SNS Publish /my-topic 200",
            "TestCaller>EventBridge PutEvents [OrderCreated] / 200",
            "TestCaller>EventHubs Send → telemetry /telemetry -",
            "TestCaller>Kafka Produce → orders-topic /orders-topic -",
            "TestCaller>PubSub Publish → my-topic /my-topic -",
            "TestCaller>StorageQueue Receive ← my-queue /my-queue 200",
            "TestCaller>MassTransit Send OrderCreated /orders -",
            "TestCaller>GrpcService SayHello /greet.Greeter/SayHello 200",
            "Caller>Application Send /IMediator/Send 200",
            "data-insights>mongo Find ← Trial / {\"find\":\"{v}\"} OK",
            "web>graphql POST /sidekick?x 201"
        ],
        [8] =
        [
            "TestCaller>TargetService POST /api/items/{n}?b&expand 200",
            "TestCaller>orders-host:8080 GET /orders/{n} 200",
            "TestCaller>Api PUT /relative/{n} 204",
            "TestCaller>Files GET /a%2Fb/c d/café/{bin} 200",
            "TestCaller>Api POST /graphql query GetUser 200",
            "TestCaller>Api POST /graphql query { menu } 200",
            "TestCaller>Api GET /graphql?query query Menu 200",
            "TestCaller>Api POST /graphql batch 200",
            "TestCaller>Database INSERT INTO Orders /app/Orders INSERT INTO [Orders] ([Total]) VALUES ({n}) OK",
            "TestCaller>Database INSERT INTO Audit /app/Audit INSERT INTO [Audit] VALUES ({n}) OK",
            "TestCaller>Database Select /OrdersDb/Users SELECT [u].[Id] FROM [dbo].[Users] AS [u] WHERE [u].[Id] = {n} OK",
            "TestCaller>TestDB SELECT FROM Users /localhost/TestDb/Users SELECT * FROM Users WHERE Id = {n} OK",
            "TestCaller>SQL Server SELECT FROM breakfasts /analytics/breakfasts SELECT id, name FROM breakfasts OK",
            "TestCaller>PostgreSQL SELECT FROM breakfasts /analytics/breakfasts SELECT id, name FROM breakfasts WHERE name = '{s}' OK",
            "TestCaller>Analytics INSERT INTO events /analytics/events INSERT INTO events (name) VALUES ('{s}') OK",
            "TestCaller>Redis Get /db0/user:{n} GET user:{n} OK",
            "TestCaller>Redis Get /{n}/user:{n} GET user:{n} OK",
            "TestCaller>MongoDB Find ← testcollection /testdb/testcollection {\"find\":\"{v}\",\"filter\":{\"_id\":\"{v}\",\"n\":{n},\"ok\":{b}}} 200",
            "TestCaller>CosmosDB Create /dbs/mydb/colls/orders/docs {\"id\":\"{v}\",\"total\":{n}.{n}} 201",
            "TestCaller>CosmosDB Query /dbs/mydb/colls/orders/docs SELECT * FROM c WHERE c.id = @id 200",
            "TestCaller>BigQuery Query /myproject SELECT name FROM ds.t WHERE id = {n} 200",
            "TestCaller>Spanner SELECT FROM Users /Users SELECT * FROM Users WHERE Id = {n} -",
            "TestCaller>DynamoDB PutItem /Users {\"TableName\":\"{v}\",\"Item\":{\"id\":{\"S\":\"{v}\"}}} 200",
            "TestCaller>Elasticsearch Search → orders /orders {\"query\":{\"match\":{\"name\":\"{v}\"}}} 200",
            "TestCaller>Bigtable ReadRows ← my-table /my-table -",
            "TestCaller>AtlasDataApi insertOne → orders /orders_db/orders {\"document\":{\"name\":\"{v}\"}} 200",
            "TestCaller>S3 GetObject /my-bucket/path/to/file.json 200",
            "TestCaller>BlobStorage Upload /mycontainer/myblob.txt 201",
            "TestCaller>CloudStorage Download → my-bucket/file.txt /my-bucket/file.txt 200",
            "TestCaller>ServiceBus Send → orders-queue /orders-queue/ -",
            "TestCaller>SQS SendMessage /orders-queue 200",
            "TestCaller>SNS Publish /my-topic 200",
            "TestCaller>EventBridge PutEvents [OrderCreated] /default/ 200",
            "TestCaller>EventHubs Send → telemetry /telemetry -",
            "TestCaller>Kafka Produce → orders-topic /orders-topic -",
            "TestCaller>PubSub Publish → my-topic /my-topic -",
            "TestCaller>StorageQueue Receive ← my-queue /my-queue 200",
            "TestCaller>MassTransit Send OrderCreated /orders -",
            "TestCaller>GrpcService SayHello /greet.Greeter/SayHello 200",
            "Caller>Application Send /IMediator/Send 200",
            "data-insights>mongo Find ← Trial /data-insights-Development/Trial {\"find\":\"{v}\"} OK",
            "web>graphql POST /sidekick?x query { me } 201"
        ]
    };

    private static string[] LinesUnder(int version) =>
        Corpus.Select(c => string.Join(" | ", InteractionShape.Calls(c.Logs, null, version).Select(call => call.ToString()))).ToArray();

    [Fact]
    public void Every_kept_version_computes_its_lines_from_the_raw_call()
    {
        foreach (var version in InteractionShape.KeptVersions)
        {
            var actual = LinesUnder(version);
            Assert.True(Pins.TryGetValue(version, out var pinned) && actual.SequenceEqual(pinned, StringComparer.Ordinal),
                $"rule {version} reads the corpus otherwise; it reads:{Environment.NewLine}" +
                string.Join(Environment.NewLine, actual.Select(line => "            \"" + line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\",")));
        }
    }

    [Fact]
    public void Every_kept_version_is_pinned()
    {
        // A version moved without its lines pinned here, or a rule dropped from the kept ones without its pins.
        Assert.Equal(InteractionShape.KeptVersions.Order(), Pins.Keys.Order());
    }
}
