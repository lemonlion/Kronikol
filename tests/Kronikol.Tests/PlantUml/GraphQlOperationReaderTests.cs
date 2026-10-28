using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The GraphQL operation a request names, read the way a server reads it: the body's top level as JSON, its query text
/// as a GraphQL document, the operation picked by operationName (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.2). The bodies
/// are the ones real clients send; each case the regular expressions misread is a probe of the plan's harness
/// (results/route/corpus.txt).
/// </summary>
public class GraphQlOperationReaderTests
{
    private static string? Label(string body) => GraphQlOperationReader.FromBody(body)?.Label;

    [Fact]
    public void A_named_operation_is_its_type_and_name()
    {
        Assert.Equal("query GetUser", Label("""{"query":"query GetUser { user { name } }"}"""));
        Assert.Equal("mutation CreateOrder", Label("""{"query":"mutation CreateOrder($o: OrderInput!) { createOrder(input: $o) { id } }","variables":{"o":{}}}"""));
        Assert.Equal("subscription OnMessage", Label("""{"query":"subscription OnMessage { messageAdded { text } }"}"""));
    }

    [Fact]
    public void Apollos_key_order_reads_the_same()
    {
        Assert.Equal("query GetUser", Label("""{"operationName":"GetUser","variables":{"id":"1"},"query":"query GetUser($id: ID!) { user(id: $id) { name } }"}"""));
    }

    [Fact]
    public void An_anonymous_operation_is_named_by_its_root_fields()
    {
        Assert.Equal("query { orderSummaries }", Label("""{"query":"{ orderSummaries { orderId status } }"}"""));
        Assert.Equal("query { recipeReports }", Label("""{"query":"{ recipeReports { recipeType } }"}"""));
        Assert.Equal("mutation { cancelOrder }", Label("""{"query":"mutation { cancelOrder(id: 1) { ok } }"}"""));
        Assert.Equal("query { user }", Label("""{"query":"query ($id: ID!) { user(id: $id) { name } }"}"""));
    }

    [Fact]
    public void Root_fields_are_sorted_and_de_duplicated()
    {
        Assert.Equal("query { a b }", Label("""{"query":"{ b a }"}"""));
        Assert.Equal(Label("""{"query":"{ a b }"}"""), Label("""{"query":"{ b a }"}"""));
        Assert.Equal("mutation { createOrder }", Label("""{"query":"mutation { first: createOrder(n: 1) { id } second: createOrder(n: 2) { id } }"}"""));
    }

    [Fact]
    public void Aliases_arguments_directives_and_nested_selections_are_not_root_fields()
    {
        Assert.Equal("query { orderSummaries user }",
            Label("""{"query":"{ mine: orderSummaries(first: 10) @include(if: true) { orderId items { sku } } user { name } }"}"""));
    }

    [Fact]
    public void Fragments_at_the_root_are_kept_by_their_form()
    {
        Assert.Equal("query { ...Fields }", Label("""{"query":"{ ...Fields } fragment Fields on Query { a }"}"""));
        Assert.Equal("query { ... on Query }", Label("""{"query":"{ ... on Query { a } }"}"""));
        Assert.Equal("query { orderSummaries }", Label("""{"query":"{ orderSummaries { ...OrderFields } } fragment OrderFields on Order { orderId }"}"""));
    }

    [Fact]
    public void Braces_inside_strings_and_block_strings_do_not_end_a_selection()
    {
        Assert.Equal("query { a b }", Label("""{"query":"{ a(s: \"}\") b }"}"""));
        Assert.Equal("query { a b }", Label("""{"query":"{ a(s: \"\"\"block } ) \\\"\"\" still the string\"\"\") b }"}"""));
    }

    [Fact]
    public void A_variable_named_query_is_not_the_operation()
    {
        Assert.Equal("query Search", Label("""{"variables":{"query":"pancakes"},"query":"query Search($query: String!) { search(q: $query) { id } }"}"""));
        Assert.Equal("query Search", Label("""{"operationName":"Search","variables":{"query":"pancakes"},"query":"query Search($query: String!) { search(q: $query) { id } }"}"""));
    }

    [Fact]
    public void A_key_named_query_in_an_input_object_is_not_the_operation()
    {
        Assert.Equal("mutation Save", Label("""{"variables":{"input":{"query":"saved search"}},"query":"mutation Save($input: SaveInput!) { save(input: $input) { id } }"}"""));
    }

    [Fact]
    public void A_document_that_opens_with_a_fragment_or_a_comment_names_its_operation()
    {
        Assert.Equal("query Q", Label("""{"query":"fragment F on Query { a } query Q { ...F }"}"""));
        Assert.Equal("query Q", Label("""{"query":"# fetch the user\nquery Q { user { name } }"}"""));
    }

    [Fact]
    public void A_name_on_the_line_after_its_keyword_is_the_name()
    {
        Assert.Equal("query GetUser", Label("""{"query":"query\nGetUser { user { name } }"}"""));
    }

    [Fact]
    public void OperationName_picks_the_operation_and_its_type()
    {
        Assert.Equal("mutation B", Label("""{"operationName":"B","query":"query A { a } mutation B { b }"}"""));
        Assert.Equal("query A", Label("""{"operationName":"A","query":"query A { a } mutation B { b }"}"""));
    }

    [Fact]
    public void A_persisted_query_is_one_operation_in_both_forms()
    {
        var hashOnly = Label("""{"operationName":"GetUser","variables":{},"extensions":{"persistedQuery":{"version":1,"sha256Hash":"ecf4edb46db40b5132295c0291d62fb65d6759a9eedfa4d5d612dd5ec54a6b38"}}}""");
        var registering = Label("""{"operationName":"GetUser","query":"query GetUser { user { name } }","variables":{},"extensions":{"persistedQuery":{"version":1,"sha256Hash":"ecf4edb46db40b5132295c0291d62fb65d6759a9eedfa4d5d612dd5ec54a6b38"}}}""");
        Assert.Equal("GetUser", hashOnly);
        Assert.Equal(hashOnly, registering);
    }

    [Fact]
    public void A_raw_control_character_in_a_string_is_read_as_sent()
    {
        // Not JSON as written (a string may not hold a raw line break), but a captured body can: the value is read
        // as the application sent it, and the label's escaping is the drawing's job.
        Assert.Equal("query x\r\ny", Label("{\"query\":\"query Q { a }\",\"operationName\":\"x\r\ny\"}"));
        Assert.Equal("query Q", Label("{\"query\":\"query\r\nQ { a }\"}"));
        Assert.Equal("query Q", Label("{\n  \"query\": \"query Q { a }\"\n}"));
    }

    [Fact]
    public void A_search_apis_query_member_is_not_an_operation()
    {
        Assert.Null(Label("""{"query":"{\"match\":{\"title\":\"x\"}}"}"""));
        Assert.Null(Label("""{"query":{"match":{"title":"x"}}}"""));
        Assert.Null(Label("""{"query":"SELECT * FROM c WHERE c.id = @id","parameters":[]}"""));
        Assert.Null(Label("""{"query":"pancakes"}"""));
    }

    [Fact]
    public void A_get_is_named_from_its_query_string()
    {
        Assert.Equal("query { user }", GraphQlOperationReader.FromQueryString("http://api/graphql?query=%7B%20user%20%7B%20name%20%7D%20%7D")?.Label);
        Assert.Equal("query GetUser", GraphQlOperationReader.FromQueryString("/graphql?operationName=GetUser&query=query%20GetUser%20%7B%20user%20%7B%20name%20%7D%20%7D")?.Label);
        Assert.Equal("GetUser", GraphQlOperationReader.FromQueryString("/graphql?operationName=GetUser&variables=%7B%7D&extensions=%7B%22persistedQuery%22%3A%7B%22version%22%3A1%7D%7D")?.Label);
    }

    [Fact]
    public void A_search_apis_query_parameter_is_not_an_operation()
    {
        Assert.Null(GraphQlOperationReader.FromQueryString("/search?query=mutation%20testing"));
        Assert.Null(GraphQlOperationReader.FromQueryString("/search?query=pancakes&page=2"));
        Assert.Null(GraphQlOperationReader.FromQueryString("/orders?page=1"));
        Assert.Null(GraphQlOperationReader.FromQueryString("/orders"));
    }

    [Fact]
    public void A_batch_is_marked_only_when_every_element_is_an_operation()
    {
        Assert.True(GraphQlOperationReader.IsBatch("""[{"query":"query A { a }"},{"query":"{ b }"}]"""));
        Assert.False(GraphQlOperationReader.IsBatch("""[{"sku":"A1","quantity":2},{"sku":"B2","quantity":1}]"""));
        Assert.False(GraphQlOperationReader.IsBatch("""[{"query":"query A { a }"},{"sku":"B2"}]"""));
        Assert.False(GraphQlOperationReader.IsBatch("""[]"""));
        Assert.False(GraphQlOperationReader.IsBatch("""{"query":"{ a }"}"""));
        Assert.Null(Label("""[{"query":"query A { a }"},{"query":"{ b }"}]"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("name=Alice&age=30")]
    [InlineData("<query>query GetUser { user }</query>")]
    [InlineData("""{"orderId":42,"items":[{"sku":"A1"}]}""")]
    [InlineData("""{"query":""")]
    [InlineData("""{"query":"query GetUser { user { name }""")]
    public void A_body_that_is_not_a_graphql_request_has_no_operation(string? body)
    {
        Assert.Null(GraphQlOperationReader.FromBody(body));
    }

    [Fact]
    public void The_detectors_label_keeps_its_documented_form()
    {
        // TryExtractLabel is public: "query" for an anonymous operation, as its summary says, with the misreads fixed.
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel("""{"query":"{ orderSummaries { orderId } }"}"""));
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel("""{"query":"query GetUser { user { name } }"}"""));
        Assert.Equal("query Search", GraphQlOperationDetector.TryExtractLabel("""{"variables":{"query":"pancakes"},"query":"query Search($query: String!) { search(q: $query) { id } }"}"""));
        Assert.Equal("mutation B", GraphQlOperationDetector.TryExtractLabel("""{"operationName":"B","query":"query A { a } mutation B { b }"}"""));
        Assert.Equal("GetUser", GraphQlOperationDetector.TryExtractLabel("""{"operationName":"GetUser","extensions":{"persistedQuery":{"version":1,"sha256Hash":"x"}}}"""));
        Assert.Null(GraphQlOperationDetector.TryExtractLabel("""{"query":"{\"match\":{\"title\":\"x\"}}"}"""));
    }
}
