using System.Net;
using Kronikol.ComponentDiagram;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// One rule answers "which call is this" for every site that asks (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.1): history's
/// fingerprint, the warm-up marks, the component diagram's endpoints and the background calls table. Each site is read
/// through its own entry point, so a site that keeps a copy of the rule fails here (the 3.30.1 lesson: two
/// implementations of one rule get a parity fact).
/// </summary>
public class OperationKeyParityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The corpus: what escapes, what a tracker writes in the authority, and the calls a body names.</summary>
    public static TheoryData<string, string, string?, string?, string, string> Corpus() => new()
    {
        // method, uri, category, request content, the key without the query, history's key
        { "GET", "http://localhost/files/report%202024.pdf?version=3", null, null, "/files/report {n}.pdf", "/files/report {n}.pdf?version" },
        { "GET", "http://localhost/caf%C3%A9/menu/42", null, null, "/café/menu/{n}", "/café/menu/{n}" },
        { "Get", "redis://db3/cache:user:42", DependencyCategories.Redis, null, "/db3/cache:user:{n}", "/db3/cache:user:{n}" },
        { "Send", "servicebus://orders/", DependencyCategories.ServiceBus, null, "/orders/", "/orders/" },
        { "POST", "http://localhost/graphql", null, """{"query":"query GetMenu { menu { items } }"}""", "/graphql query GetMenu", "/graphql query GetMenu" },
        { "POST", "http://localhost/graphql", null, """{"query":"{ orderSummaries { orderId } }"}""", "/graphql query { orderSummaries }", "/graphql query { orderSummaries }" },
        { "Insert", "sql:///app/Orders", DependencyCategories.SqlServer, "SET NOCOUNT ON;\r\nINSERT INTO [Orders] ([Status]) VALUES (@p0);",
            "/app/Orders INSERT INTO [Orders] ([Status]) VALUES (@p0);", "/app/Orders INSERT INTO [Orders] ([Status]) VALUES (@p0);" },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void One_operation_rule_answers_for_every_site(string method, string uri, string? category, string? content, string key, string historyKey)
    {
        Assert.Equal(key, CallOperation.Of(method, new Uri(uri).ToString(), category, content, null, queryKeys: false));
        Assert.Equal(historyKey, CallOperation.Of(method, new Uri(uri).ToString(), category, content, null, queryKeys: true));

        // History: the fingerprint's call line.
        var logs = Calls(method, uri, category, content);
        Assert.Equal(historyKey, InteractionShape.Calls(logs).First().Uri);

        // The warm-up marks: a slow first call judged against a quick one of the same shape.
        var feature = new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "s1", DisplayName = "s1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(10) }] };
        var mark = Assert.Single(WarmUpCalls.Find([feature], logs).Calls);
        Assert.Equal(method + " " + key, mark.Value.Shape);

        // The component diagram's endpoints.
        var relationships = ComponentDiagramGenerator.ExtractRelationships(logs);
        var endpoint = Assert.Single(ComponentFlowSegmentBuilder.ComputeRelationshipStats(relationships, logs).Values.Single().EndpointBreakdown);
        Assert.Equal(key, endpoint.Path);

        // The background calls table.
        var html = ReportGenerator.RenderBackgroundCalls(new BackgroundCalls(2, [], logs));
        Assert.Contains($"<td>{WebUtility.HtmlEncode(key)}</td><td>2</td>", html, StringComparison.Ordinal);
    }

    /// <summary>Two calls of one request by the test: the first slow, the second quick and after it.</summary>
    private static RequestResponseLog[] Calls(string method, string uri, string? category, string? content)
    {
        OneOf<HttpMethod, string> verb = method is "GET" or "POST" ? new HttpMethod(method) : method;
        var logs = new List<RequestResponseLog>();
        foreach (var (start, ms) in new[] { (0.0, 600.0), (700.0, 5.0) })
        {
            var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
            logs.Add(new RequestResponseLog("s1", "s1", verb, content, new Uri(uri), [], "Orders", "Test", RequestResponseType.Request, trace, id, false,
                DependencyCategory: category) { Timestamp = T0.AddMilliseconds(start) });
            logs.Add(new RequestResponseLog("s1", "s1", verb, "[]", new Uri(uri), [], "Orders", "Test", RequestResponseType.Response, trace, id, false,
                HttpStatusCode.OK, DependencyCategory: category) { Timestamp = T0.AddMilliseconds(start + ms) });
        }
        return logs.ToArray();
    }
}
