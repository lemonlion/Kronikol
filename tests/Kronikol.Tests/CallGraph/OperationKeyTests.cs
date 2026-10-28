using Kronikol.CallGraph;
using Kronikol.History;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.3 and 5.2.4: an operation is #192's key, <see cref="CallOperation"/> with the query's
/// keys left out as the query engine's <c>route</c> reads it, so the card keys a call as every other reader does.
/// </summary>
public class OperationKeyTests
{
    private static readonly CallScenario Pancakes = Records.Scenario(0, "Order pancakes");

    private static Graph Build(Records records) => CallGraphBuilder.Build([Pancakes], records.All, PartiesInput.Inferred);

    // 5.1.3: one key, not two. History keeps the query's keys, so the parity is with route's setting, not with history's.
    [Theory]
    [InlineData("GET", "http://localhost/orders/123?expand=items", null, null)]
    [InlineData("POST", "http://localhost/graphql", null, "{\"query\":\"query Menu { items { name } }\"}")]
    [InlineData("GET", "http://localhost/graphql?query=query Menu { items { name } }", null, null)]
    [InlineData("POST", "http://localhost/graphql", null, "[{\"query\":\"query A { a }\"},{\"query\":\"query B { b }\"}]")]
    [InlineData("SELECT", "sql://localhost/orders", "SQL", "SELECT * FROM orders WHERE id = 7")]
    [InlineData("Send", "servicebus://orders/", "ServiceBus", null)]
    public void An_operations_target_and_GraphQL_operation_are_the_key_route_reads(string method, string uri, string? category, string? content)
    {
        var r = new Records();
        r.Call(Pancakes, "Caller", "Breakfast Provider", method, uri, attribution: "TestContext", category: category, content: content);

        var operation = Assert.Single(Build(r).Operations);

        var written = operation.GraphqlOperation is null ? operation.Target : operation.Target + " " + operation.GraphqlOperation;
        Assert.Equal(CallOperation.Of(method, uri, category, content, rules: null, queryKeys: false), written);
    }

    // 5.2.4: the issue's GraphQL POSTs, one endpoint and two operations, are two operations, each named.
    [Fact]
    public void Two_GraphQL_operations_on_one_endpoint_are_two_operations()
    {
        var r = new Records();
        const string menu = "{\"query\":\"query Menu { items { name } }\"}";
        r.Call(Pancakes, "Caller", "Breakfast Provider", "POST", "http://localhost/graphql", attribution: "TestContext", content: menu);
        r.Call(Pancakes, "Caller", "Breakfast Provider", "POST", "http://localhost/graphql", attribution: "TestContext",
            content: "{\"query\":\"mutation PlaceOrder($dish: String!) { placeOrder(dish: $dish) { id } }\",\"variables\":{\"dish\":\"pancakes\"}}");
        r.Call(Pancakes, "Caller", "Breakfast Provider", "POST", "http://localhost/graphql", attribution: "TestContext", content: menu);

        var graph = Build(r);

        Assert.Equal([("/graphql", "mutation PlaceOrder"), ("/graphql", "query Menu")],
            graph.Operations.Select(o => (o.Target, o.GraphqlOperation)).OrderBy(o => o.GraphqlOperation, StringComparer.Ordinal));
        Assert.Equal(2, graph.Operations.Select(o => o.Op).Distinct().Count());
    }
}
