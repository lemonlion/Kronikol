using System.Net;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// What the diagrams draw for a GraphQL call, read by the one reader (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.2): the
/// arrow's label, the key that collapses a repeated arrow, and the note's formatting. The owner chose, on 2026-10-28,
/// that an anonymous operation's label shows its root fields (Q13).
/// </summary>
public class GraphQlArrowLabelTests
{
    private static string Drawn(RequestResponseLog request) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId([request, Response(request)]).Single().PlantUmls.Single().PlainText;

    [Fact]
    public void An_anonymous_operations_arrow_shows_its_root_fields()
    {
        var text = Drawn(Request("POST", "http://example.com/graphql", """{"query":"{ orderSummaries { orderId } }"}"""));
        Assert.Contains("(query { orderSummaries })", text);
    }

    [Fact]
    public void A_named_operations_arrow_is_unchanged()
    {
        var text = Drawn(Request("POST", "http://example.com/graphql", """{"query":"query GetUser { user { name } }"}"""));
        Assert.Contains("(query GetUser)", text);
    }

    [Fact]
    public void A_variable_named_query_is_not_the_operation_on_the_arrow()
    {
        var text = Drawn(Request("POST", "http://example.com/graphql", """{"variables":{"query":"pancakes"},"query":"query Search($query: String!) { search(q: $query) { id } }"}"""));
        Assert.Contains("(query Search)", text);
    }

    [Fact]
    public void A_persisted_query_is_named_by_its_operationName_on_the_arrow()
    {
        var text = Drawn(Request("POST", "http://example.com/graphql", """{"operationName":"GetUser","extensions":{"persistedQuery":{"version":1,"sha256Hash":"ab12"}}}"""));
        Assert.Contains("(GetUser)", text);
    }

    [Fact]
    public void A_get_is_named_from_its_query_string_on_the_arrow()
    {
        var text = Drawn(Request("GET", "http://example.com/graphql?query=%7B%20user%20%7B%20name%20%7D%20%7D", null));
        Assert.Contains("(query { user })", text);
    }

    [Fact]
    public void A_search_apis_get_is_not_labelled_an_operation()
    {
        var text = Drawn(Request("GET", "http://example.com/search?query=mutation%20testing", null));
        Assert.DoesNotContain("(mutation", text);
    }

    [Fact]
    public void Two_anonymous_queries_in_a_row_are_two_arrows()
    {
        var first = Request("POST", "http://example.com/graphql", """{"query":"{ orderSummaries { orderId } }"}""");
        var second = Request("POST", "http://example.com/graphql", """{"query":"{ recipeReports { recipeType } }"}""");
        var firstKey = SequenceCollapser.KeyFor(first, Response(first));
        Assert.False(string.Equals(firstKey, SequenceCollapser.KeyFor(second, Response(second)), StringComparison.Ordinal), $"one key for two operations: {firstKey}");

        var same = Request("POST", "http://example.com/graphql", """{"query":"{ orderSummaries { status } }"}""");
        Assert.Equal(SequenceCollapser.KeyFor(first, Response(first)), SequenceCollapser.KeyFor(same, Response(same)));
    }

    [Fact]
    public void The_collapse_key_keeps_its_parts_apart()
    {
        var one = Request("GET", "http://example.com/x", null) with { CallerName = "AB", ServiceName = "C" };
        var other = Request("GET", "http://example.com/x", null) with { CallerName = "A", ServiceName = "BC" };
        var first = SequenceCollapser.KeyFor(one, null);
        var second = SequenceCollapser.KeyFor(other, null);
        // A pin, not a fix: KeyFor's separator is a U+001F written as a raw character inside its quotes, which an editor
        // shows as "", so the key reads as if its parts ran together. They do not.
        Assert.False(string.Equals(first, second, StringComparison.Ordinal), $"one key for two calls: {first}");
    }

    [Fact]
    public void A_body_the_reader_names_is_formatted_as_graphql()
    {
        // The variables come first, as some clients send them: the reader still finds the operation, so the note
        // formats the query instead of showing the body as JSON.
        var body = """{"variables":{"query":"pancakes"},"query":"query Search($query: String!) { search(q: $query) { id } }"}""";
        var formatted = GraphQlBodyFormatter.TryFormat(body, GraphQlBodyFormat.FormattedQueryOnly);
        Assert.NotNull(formatted);
        Assert.Contains("query Search", formatted);
    }

    private static RequestResponseLog Request(string method, string uri, string? content) => new(
        TestName: "GraphQL", TestId: "graphql-1", Method: HttpMethod.Parse(method), Content: content, Uri: new Uri(uri),
        Headers: [], ServiceName: "Orders API", CallerName: "Caller", Type: RequestResponseType.Request,
        TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(), TrackingIgnore: false);

    private static RequestResponseLog Response(RequestResponseLog? request = null) => new(
        TestName: "GraphQL", TestId: "graphql-1", Method: HttpMethod.Post, Content: "{}", Uri: new Uri("http://example.com/graphql"),
        Headers: [("Content-Type", "application/json")], ServiceName: "Orders API", CallerName: "Caller", Type: RequestResponseType.Response,
        TraceId: request?.TraceId ?? Guid.NewGuid(), RequestResponseId: request?.RequestResponseId ?? Guid.NewGuid(), TrackingIgnore: false,
        StatusCode: HttpStatusCode.OK);
}
