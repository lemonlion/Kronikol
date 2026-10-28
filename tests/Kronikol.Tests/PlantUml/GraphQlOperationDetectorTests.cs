using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

public class GraphQlOperationDetectorTests
{
    // ════════════════════════════════════════════════════════════
    //  NON-GRAPHQL / NULL / EMPTY → null
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Null_empty_or_whitespace_returns_null(string? content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("name=Alice&age=30")]
    [InlineData("key=value")]
    [InlineData("plain text content")]
    [InlineData("Hello World")]
    public void Non_json_body_returns_null(string content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("<request><query>select * from users</query></request>")]
    [InlineData("<xml><query>query GetUser { user }</query></xml>")]
    [InlineData("<?xml version=\"1.0\"?><root/>")]
    public void Xml_body_returns_null(string content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{"name":"Alice","age":30}""")]
    [InlineData("""{"data":{"orderSummaries":[{"orderId":"abc"}]}}""")]
    [InlineData("""{"errors":[{"message":"something failed"}]}""")]
    [InlineData("""{"id":1,"type":"request"}""")]
    [InlineData("""{"method":"POST","url":"/api"}""")]
    [InlineData("""{"status":200,"body":"ok"}""")]
    public void Json_without_query_key_returns_null(string content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{"query":42}""")]
    [InlineData("""{"query":true}""")]
    [InlineData("""{"query":false}""")]
    [InlineData("""{"query":null}""")]
    [InlineData("""{"query":[1,2,3]}""")]
    [InlineData("""{"query":{"nested":"value"}}""")]
    [InlineData("""{"query":3.14}""")]
    public void Query_key_with_non_string_value_returns_null(string content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{"query":"select * from users"}""")]
    [InlineData("""{"query":"SELECT query FROM table"}""")]
    [InlineData("""{"query":"INSERT INTO orders VALUES (1)"}""")]
    [InlineData("""{"query":"UPDATE users SET name='Alice'"}""")]
    [InlineData("""{"query":"DELETE FROM orders WHERE id=1"}""")]
    [InlineData("""{"query":"hello world"}""")]
    [InlineData("""{"query":""}""")]
    [InlineData("""{"query":"   "}""")]
    [InlineData("""{"query":"some random text"}""")]
    [InlineData("""{"query":"GET /api/users"}""")]
    [InlineData("""{"query":"POST /api/orders"}""")]
    [InlineData("""{"query":"https://example.com/api"}""")]
    public void Query_key_with_non_graphql_string_value_returns_null(string content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void Nested_query_key_not_at_top_level_returns_null()
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(
            """{"data":{"query":"query GetUser { user { name } }"}}"""));
    }

    [Fact]
    public void Deeply_nested_query_key_returns_null()
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(
            """{"outer":{"inner":{"query":"query GetUser { user }"}}}"""));
    }

    [Fact]
    public void Query_key_inside_array_returns_null()
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(
            """[{"query":"query GetUser { user }"}]"""));
    }

    [Fact]
    public void Graphql_response_body_not_confused_as_request()
    {
        var responseBody = """{"data":{"orderSummaries":[{"orderId":"abc-123","customerName":"Alice"}]}}""";
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(responseBody));
    }

    [Fact]
    public void Graphql_error_response_not_confused_as_request()
    {
        var responseBody = """{"errors":[{"message":"Unexpected Execution Error","locations":[{"line":1,"column":3}],"path":["orderSummaries"]}]}""";
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(responseBody));
    }

    [Fact]
    public void Very_long_non_graphql_content_returns_quickly()
    {
        var body = """{"data":""" + "\"" + new string('x', 100_000) + "\"" + "}";
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Theory]
    [InlineData("""{"Query":"query GetUser { user { name } }"}""")]
    [InlineData("""{"QUERY":"query GetUser { user { name } }"}""")]
    public void Query_key_is_case_sensitive(string content)
    {
        // JSON keys are case-sensitive; "Query" ≠ "query"
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    // ════════════════════════════════════════════════════════════
    //  ANONYMOUS / SHORTHAND QUERIES → "query"
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"{ user { name } }"}""")]
    [InlineData("""{"query":"{ orderSummaries { orderId customerName itemCount tableNumber status createdAt } }"}""")]
    [InlineData("""{"query":"{ recipeReports { recipeType ingredients } }"}""")]
    [InlineData("""{"query":"{ orderSummaries { orderId status } recipeReports { recipeType ingredients } }"}""")]
    public void Anonymous_shorthand_query_returns_query(string content)
    {
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void Shorthand_query_with_filtering_and_sorting()
    {
        var body = """{"query":"{ orderSummaries(where: { status: { eq: \"Completed\" } }, order: { createdAt: DESC }) { orderId customerName itemCount status createdAt } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Shorthand_query_with_aliases()
    {
        var body = """{"query":"{ completed: orderSummaries(where: { status: { eq: \"Completed\" } }) { orderId } pending: orderSummaries(where: { status: { eq: \"Created\" } }) { orderId } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Shorthand_query_with_fragments()
    {
        var body = """{"query":"{ orderSummaries { ...OrderFields } } fragment OrderFields on OrderSummary { orderId customerName itemCount tableNumber status createdAt }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Shorthand_query_with_inline_fragment()
    {
        var body = """{"query":"{ orderSummaries { orderId ... on OrderSummary { customerName status } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Shorthand_introspection_schema()
    {
        var body = """{"query":"{ __schema { queryType { name } mutationType { name } subscriptionType { name } types { name kind } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Shorthand_introspection_type()
    {
        var body = """{"query":"{ __type(name: \"OrderSummary\") { name fields { name type { name kind } } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Shorthand_introspection_available_queries()
    {
        var body = """{"query":"{ __schema { queryType { fields { name args { name type { name } } type { name kind ofType { name } } } } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  UNNAMED OPERATIONS (keyword but no name) → type only
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"query { user { name } }"}""", "query")]
    [InlineData("""{"query":"mutation { createUser { id } }"}""", "mutation")]
    [InlineData("""{"query":"subscription { onMessage { text } }"}""", "subscription")]
    public void Unnamed_operation_returns_type_only(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void Unnamed_mutation_with_arguments()
    {
        var body = """{"query":"mutation { createOrder(input: { customerName: \"Alice\", items: 3, tableNumber: 5 }) { orderId status } }"}""";
        Assert.Equal("mutation", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Unnamed_mutation_with_multiple_operations()
    {
        var body = """{"query":"mutation { updateOrderStatus(orderId: \"abc-123\", status: \"Preparing\") { orderId status } cancelOrder(orderId: \"def-456\") { orderId status } }"}""";
        Assert.Equal("mutation", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Unnamed_subscription()
    {
        var body = """{"query":"subscription { orderStatusChanged { orderId status updatedAt } }"}""";
        Assert.Equal("subscription", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  NAMED OPERATIONS → "type Name"
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"query GetUser { user { name } }"}""", "query GetUser")]
    [InlineData("""{"query":"query GetOrderSummaries { orderSummaries { orderId customerName } }"}""", "query GetOrderSummaries")]
    [InlineData("""{"query":"mutation CreateOrder { createOrder { id } }"}""", "mutation CreateOrder")]
    [InlineData("""{"query":"subscription OnMessage { messageAdded { text } }"}""", "subscription OnMessage")]
    public void Named_operation_extracts_type_and_name(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{"query":"query GetUser($id: ID!) { user(id: $id) { name } }","variables":{"id":"123"}}""", "query GetUser")]
    [InlineData("""{"query":"query GetOrdersByStatus($status: String!) { orderSummaries(where: { status: { eq: $status } }) { orderId customerName status } }","variables":{"status":"Completed"}}""", "query GetOrdersByStatus")]
    public void Named_query_with_variables_extracts_name(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void Named_mutation_with_complex_variables()
    {
        var body = """{"query":"mutation CreateOrder($input: CreateOrderInput!) { createOrder(input: $input) { orderId status createdAt } }","variables":{"input":{"customerName":"Alice","items":3,"tableNumber":5}}}""";
        Assert.Equal("mutation CreateOrder", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Named_subscription_with_variables()
    {
        var body = """{"query":"subscription OnOrderUpdate($orderId: String!) { orderStatusChanged(orderId: $orderId) { orderId status updatedAt } }","variables":{"orderId":"abc-123"}}""";
        Assert.Equal("subscription OnOrderUpdate", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Named_query_with_directives_include()
    {
        var body = """{"query":"query GetOrders($includeCustomer: Boolean!) { orderSummaries { orderId status customerName @include(if: $includeCustomer) } }","variables":{"includeCustomer":false}}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Named_query_with_directives_skip()
    {
        var body = """{"query":"query GetOrders($hideTable: Boolean!) { orderSummaries { orderId status tableNumber @skip(if: $hideTable) } }","variables":{"hideTable":true}}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Named_query_with_fragments()
    {
        var body = """{"query":"query GetUser { ...UserFields } fragment UserFields on User { name }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Named_mutation_with_multiple_variables()
    {
        var body = """{"query":"mutation UpdateUser($id: ID!, $name: String!, $email: String) { updateUser(id: $id, name: $name, email: $email) { id } }"}""";
        Assert.Equal("mutation UpdateUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Named_introspection_query()
    {
        var body = """{"query":"query IntrospectionQuery { __schema { types { name } } }"}""";
        Assert.Equal("query IntrospectionQuery", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Theory]
    [InlineData("""{"query":"query Get_User_2 { user { name } }"}""", "query Get_User_2")]
    [InlineData("""{"query":"query A { x }"}""", "query A")]
    [InlineData("""{"query":"query ALLCAPS { x }"}""", "query ALLCAPS")]
    [InlineData("""{"query":"query camelCase { x }"}""", "query camelCase")]
    [InlineData("""{"query":"query PascalCase { x }"}""", "query PascalCase")]
    [InlineData("""{"query":"query _leading { x }"}""", "query _leading")]
    [InlineData("""{"query":"query x123 { x }"}""", "query x123")]
    public void Operation_name_various_identifiers(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    // ════════════════════════════════════════════════════════════
    //  OPERATION NAME FIELD
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"query GetUser { user { name } }","operationName":"GetUser"}""", "query GetUser")]
    [InlineData("""{"operationName":"GetUser","query":"query GetUser { user { name } }"}""", "query GetUser")]
    public void OperationName_field_used_regardless_of_json_key_order(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void OperationName_overrides_inline_name()
    {
        var body = """{"query":"query Q1 { user { name } }","operationName":"GetUser"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Multiple_named_operations_with_operationName()
    {
        var body = """{"query":"query GetOrders { orderSummaries { orderId status } } query GetRecipes { recipeReports { recipeType ingredients } }","operationName":"GetOrders"}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Theory]
    [InlineData("""{"query":"query { user { name } }","operationName":null}""", "query")]
    [InlineData("""{"query":"query { user { name } }","operationName":""}""", "query")]
    public void OperationName_null_or_empty_is_ignored(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void OperationName_with_unnamed_query_provides_name()
    {
        var body = """{"query":"query { user { name } }","operationName":"GetUser"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void OperationName_with_anonymous_shorthand_provides_name()
    {
        var body = """{"query":"{ user { name } }","operationName":"GetUser"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  WHITESPACE & FORMATTING VARIATIONS
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Minified_body()
    {
        Assert.Equal("query GetUser",
            GraphQlOperationDetector.TryExtractLabel("""{"query":"query GetUser{user{name}}"}"""));
    }

    [Fact]
    public void Pretty_printed_body_with_real_newlines()
    {
        var body = "{\n  \"query\": \"query GetUser { user { name } }\"\n}";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Pretty_printed_body_with_crlf()
    {
        var body = "{\r\n  \"query\": \"query GetUser { user { name } }\"\r\n}";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Pretty_printed_body_with_tabs()
    {
        var body = "{\n\t\"query\": \"query GetUser { user { name } }\"\n}";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Theory]
    [InlineData("""{"query":"\n  query GetUser {\n    user { name }\n  }"}""", "query GetUser")]
    [InlineData("""{"query":"\r\n\tquery GetUser { user { name } }"}""", "query GetUser")]
    [InlineData("""{"query":"   query GetUser { user { name } }"}""", "query GetUser")]
    [InlineData("""{"query":"\n\n\n  query GetUser { user { name } }"}""", "query GetUser")]
    [InlineData("""{"query":"\t\t  query GetUser { user { name } }"}""", "query GetUser")]
    public void Leading_whitespace_in_query_value(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{"query":"\n  { user { name } }"}""", "query")]
    [InlineData("""{"query":"\r\n  { user { name } }"}""", "query")]
    [InlineData("""{"query":"  { user { name } }"}""", "query")]
    public void Leading_whitespace_in_anonymous_query(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{ "query" : "query GetUser { user { name } }" }""")]
    [InlineData("""{  "query"  :  "query GetUser { user { name } }"  }""")]
    [InlineData("""{"query" :"query GetUser { user { name } }"}""")]
    public void Whitespace_around_json_keys_and_colons(string content)
    {
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void Pretty_printed_with_variables_before_query()
    {
        var body = """
{
  "variables": {
    "input": {
      "customerName": "Alice",
      "items": 3,
      "tableNumber": 5
    }
  },
  "query": "mutation CreateOrder($input: CreateOrderInput!) { createOrder(input: $input) { orderId status createdAt } }"
}
""";
        Assert.Equal("mutation CreateOrder", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Pretty_printed_with_all_fields()
    {
        var body = """
{
  "operationName": "GetOrders",
  "variables": {
    "status": "Completed"
  },
  "query": "query GetOrders($status: String!) { orderSummaries(where: { status: { eq: $status } }) { orderId customerName status } }"
}
""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Pretty_printed_shorthand_query()
    {
        var body = """
{
  "query": "{ orderSummaries { orderId customerName itemCount tableNumber status createdAt } }"
}
""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  CASE SENSITIVITY
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"Query GetUser { user { name } }"}""")]
    [InlineData("""{"query":"QUERY GetUser { user { name } }"}""")]
    [InlineData("""{"query":"Mutation CreateUser { createUser { id } }"}""")]
    [InlineData("""{"query":"MUTATION CreateUser { createUser { id } }"}""")]
    [InlineData("""{"query":"Subscription OnMsg { msg { text } }"}""")]
    [InlineData("""{"query":"SUBSCRIPTION OnMsg { msg { text } }"}""")]
    public void Operation_type_must_be_lowercase_per_graphql_spec(string content)
    {
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Theory]
    [InlineData("""{"query":"query GetUserProfile { user { name } }"}""", "query GetUserProfile")]
    [InlineData("""{"query":"query getUserProfile { user { name } }"}""", "query getUserProfile")]
    [InlineData("""{"query":"query GETUSERPROFILE { user { name } }"}""", "query GETUSERPROFILE")]
    public void Operation_name_preserves_original_casing(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    // ════════════════════════════════════════════════════════════
    //  VARIABLES WITH NESTED OBJECTS (correctness of depth tracking)
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Variables_with_simple_object_before_query()
    {
        var body = """{"variables":{"status":"Completed"},"query":"query GetOrders { orders { id } }"}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Variables_with_nested_object_before_query()
    {
        var body = """{"variables":{"input":{"name":"Alice","address":{"city":"London"}}},"query":"mutation CreateUser($input: UserInput!) { createUser(input: $input) { id } }"}""";
        Assert.Equal("mutation CreateUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Variables_with_deeply_nested_object_before_query()
    {
        var body = """{"variables":{"a":{"b":{"c":{"d":{"e":"deep"}}}}},"query":"query Deep { deep { value } }"}""";
        Assert.Equal("query Deep", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Variables_with_array_before_query()
    {
        var body = """{"variables":{"ids":["a","b","c"]},"query":"query GetUsers($ids: [ID!]!) { users(ids: $ids) { name } }"}""";
        Assert.Equal("query GetUsers", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Variables_with_array_of_objects_before_query()
    {
        var body = """{"variables":{"items":[{"name":"item1"},{"name":"item2"}]},"query":"query GetItems { items { name } }"}""";
        Assert.Equal("query GetItems", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Extensions_with_persisted_query_before_query()
    {
        var body = """{"extensions":{"persistedQuery":{"version":1,"sha256Hash":"abc123"}},"query":"query GetUser { user { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Variables_and_extensions_both_before_query()
    {
        var body = """{"variables":{"id":"1"},"extensions":{"persistedQuery":{"version":1}},"query":"query GetUser($id: ID!) { user(id: $id) { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void All_fields_before_query()
    {
        var body = """{"operationName":"GetUser","variables":{"id":"1"},"extensions":{},"query":"query GetUser($id: ID!) { user(id: $id) { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  ESCAPED CHARACTERS IN QUERY VALUE
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Escaped_quotes_in_filter_arguments()
    {
        var body = """{"query":"{ orderSummaries(where: { status: { eq: \"Completed\" } }) { orderId } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Escaped_quotes_in_named_query_filter()
    {
        var body = """{"query":"query GetCompleted { orderSummaries(where: { status: { eq: \"Completed\" } }) { orderId } }"}""";
        Assert.Equal("query GetCompleted", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Multiple_escaped_quotes_in_aliases()
    {
        var body = """{"query":"{ completed: orderSummaries(where: { status: { eq: \"Completed\" } }) { orderId } pending: orderSummaries(where: { status: { eq: \"Created\" } }) { orderId } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Escaped_backslash_in_query()
    {
        var body = """{"query":"query GetPath { file(path: \"C:\\\\Users\") { name } }"}""";
        Assert.Equal("query GetPath", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Escaped_newlines_and_tabs_in_query()
    {
        var body = """{"query":"query GetFormatted {\n  user {\n    name\n  }\n}"}""";
        Assert.Equal("query GetFormatted", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  ALL EXAMPLES FROM graphqlexamples.md — MINIFIED
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Example_shorthand_no_variables()
    {
        var body = """{"query":"{ orderSummaries { orderId customerName itemCount tableNumber status createdAt } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_named_query_no_variables()
    {
        var body = """{"query":"query GetOrderSummaries { orderSummaries { orderId customerName itemCount tableNumber status createdAt } }"}""";
        Assert.Equal("query GetOrderSummaries", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_query_with_variables()
    {
        var body = """{"query":"query GetOrdersByStatus($status: String!) { orderSummaries(where: { status: { eq: $status } }) { orderId customerName status } }","variables":{"status":"Completed"}}""";
        Assert.Equal("query GetOrdersByStatus", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_filtering_and_sorting()
    {
        var body = """{"query":"{ orderSummaries(where: { status: { eq: \"Completed\" } }, order: { createdAt: DESC }) { orderId customerName itemCount status createdAt } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_projection()
    {
        var body = """{"query":"{ recipeReports { recipeType ingredients } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_multiple_root_fields()
    {
        var body = """{"query":"{ orderSummaries { orderId status } recipeReports { recipeType ingredients } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_multiple_named_operations_with_operationName()
    {
        var body = """{"query":"query GetOrders { orderSummaries { orderId status } } query GetRecipes { recipeReports { recipeType ingredients } }","operationName":"GetOrders"}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_named_query_fragments()
    {
        var body = """{"query":"{ orderSummaries { ...OrderFields } } fragment OrderFields on OrderSummary { orderId customerName itemCount tableNumber status createdAt }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_inline_fragment()
    {
        var body = """{"query":"{ orderSummaries { orderId ... on OrderSummary { customerName status } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_aliases()
    {
        var body = """{"query":"{ completed: orderSummaries(where: { status: { eq: \"Completed\" } }) { orderId } pending: orderSummaries(where: { status: { eq: \"Created\" } }) { orderId } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_mutation_no_variables()
    {
        var body = """{"query":"mutation { createOrder(input: { customerName: \"Alice\", items: 3, tableNumber: 5 }) { orderId status } }"}""";
        Assert.Equal("mutation", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_named_mutation_with_variables()
    {
        var body = """{"query":"mutation CreateOrder($input: CreateOrderInput!) { createOrder(input: $input) { orderId status createdAt } }","variables":{"input":{"customerName":"Alice","items":3,"tableNumber":5}}}""";
        Assert.Equal("mutation CreateOrder", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_mutation_multiple_operations()
    {
        var body = """{"query":"mutation { updateOrderStatus(orderId: \"abc-123\", status: \"Preparing\") { orderId status } cancelOrder(orderId: \"def-456\") { orderId status } }"}""";
        Assert.Equal("mutation", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_subscription()
    {
        var body = """{"query":"subscription { orderStatusChanged { orderId status updatedAt } }"}""";
        Assert.Equal("subscription", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_named_subscription_with_variables()
    {
        var body = """{"query":"subscription OnOrderUpdate($orderId: String!) { orderStatusChanged(orderId: $orderId) { orderId status updatedAt } }","variables":{"orderId":"abc-123"}}""";
        Assert.Equal("subscription OnOrderUpdate", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_introspection_full_schema()
    {
        var body = """{"query":"{ __schema { queryType { name } mutationType { name } subscriptionType { name } types { name kind } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_introspection_single_type()
    {
        var body = """{"query":"{ __type(name: \"OrderSummary\") { name fields { name type { name kind } } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_introspection_available_queries()
    {
        var body = """{"query":"{ __schema { queryType { fields { name args { name type { name } } type { name kind ofType { name } } } } } }"}""";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_directive_include()
    {
        var body = """{"query":"query GetOrders($includeCustomer: Boolean!) { orderSummaries { orderId status customerName @include(if: $includeCustomer) } }","variables":{"includeCustomer":false}}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Example_directive_skip()
    {
        var body = """{"query":"query GetOrders($hideTable: Boolean!) { orderSummaries { orderId status tableNumber @skip(if: $hideTable) } }","variables":{"hideTable":true}}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  ALL EXAMPLES FROM graphqlexamples.md — PRETTY-PRINTED
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void PrettyPrinted_shorthand_no_variables()
    {
        var body = "{\n  \"query\": \"{ orderSummaries { orderId customerName itemCount tableNumber status createdAt } }\"\n}";
        Assert.Equal("query", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_named_query_no_variables()
    {
        var body = "{\n  \"query\": \"query GetOrderSummaries { orderSummaries { orderId customerName itemCount tableNumber status createdAt } }\"\n}";
        Assert.Equal("query GetOrderSummaries", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_query_with_variables()
    {
        var body = "{\n  \"query\": \"query GetOrdersByStatus($status: String!) { orderSummaries(where: { status: { eq: $status } }) { orderId customerName status } }\",\n  \"variables\": {\n    \"status\": \"Completed\"\n  }\n}";
        Assert.Equal("query GetOrdersByStatus", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_named_mutation_with_variables()
    {
        var body = "{\n  \"query\": \"mutation CreateOrder($input: CreateOrderInput!) { createOrder(input: $input) { orderId status createdAt } }\",\n  \"variables\": {\n    \"input\": {\n      \"customerName\": \"Alice\",\n      \"items\": 3,\n      \"tableNumber\": 5\n    }\n  }\n}";
        Assert.Equal("mutation CreateOrder", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_multiple_operations_with_operationName()
    {
        var body = "{\n  \"query\": \"query GetOrders { orderSummaries { orderId status } } query GetRecipes { recipeReports { recipeType ingredients } }\",\n  \"operationName\": \"GetOrders\"\n}";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_subscription_with_variables()
    {
        var body = "{\n  \"query\": \"subscription OnOrderUpdate($orderId: String!) { orderStatusChanged(orderId: $orderId) { orderId status updatedAt } }\",\n  \"variables\": {\n    \"orderId\": \"abc-123\"\n  }\n}";
        Assert.Equal("subscription OnOrderUpdate", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_variables_before_query()
    {
        var body = "{\n  \"variables\": {\n    \"status\": \"Completed\"\n  },\n  \"query\": \"query GetOrdersByStatus($status: String!) { orderSummaries(where: { status: { eq: $status } }) { orderId } }\"\n}";
        Assert.Equal("query GetOrdersByStatus", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void PrettyPrinted_deeply_nested_variables_before_query()
    {
        var body = "{\n  \"variables\": {\n    \"input\": {\n      \"customerName\": \"Alice\",\n      \"items\": 3,\n      \"address\": {\n        \"city\": \"London\",\n        \"postcode\": \"SW1A 1AA\"\n      }\n    }\n  },\n  \"query\": \"mutation CreateOrder($input: CreateOrderInput!) { createOrder(input: $input) { orderId } }\"\n}";
        Assert.Equal("mutation CreateOrder", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  REALISTIC END-TO-END GRAPHQL BODIES (complex)
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void HotChocolate_filtering_sorting_paging()
    {
        var body = """{"query":"query GetPaged($first: Int, $after: String) { orderSummaries(first: $first, after: $after, where: { status: { eq: \"Completed\" } }, order: { createdAt: DESC }) { nodes { orderId customerName } pageInfo { hasNextPage endCursor } } }","variables":{"first":10,"after":null}}""";
        Assert.Equal("query GetPaged", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Apollo_persisted_query_with_extensions()
    {
        var body = """{"query":"query GetUser { user { name } }","extensions":{"persistedQuery":{"version":1,"sha256Hash":"ecf4edb46db40b5132295c0291d62fb65d6759a9eedfa4d5d612dd5ec54a6b38"}}}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Mutation_with_file_upload_style_variables()
    {
        var body = """{"query":"mutation UploadFile($file: Upload!) { uploadFile(file: $file) { url } }","variables":{"file":null}}""";
        Assert.Equal("mutation UploadFile", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_with_enum_arguments()
    {
        var body = """{"query":"query GetOrders($status: OrderStatus!) { orders(status: $status) { id } }","variables":{"status":"COMPLETED"}}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Mutation_with_boolean_and_int_variables()
    {
        var body = """{"query":"mutation UpdateSettings($notify: Boolean!, $limit: Int!) { updateSettings(notify: $notify, limit: $limit) { ok } }","variables":{"notify":true,"limit":50}}""";
        Assert.Equal("mutation UpdateSettings", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_with_default_variable_values()
    {
        var body = """{"query":"query GetOrders($status: String = \"Created\") { orders(status: $status) { id } }"}""";
        Assert.Equal("query GetOrders", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_with_union_type_inline_fragments()
    {
        var body = """{"query":"query Search($term: String!) { search(term: $term) { ... on User { name } ... on Order { orderId } } }","variables":{"term":"alice"}}""";
        Assert.Equal("query Search", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Mutation_with_array_input()
    {
        var body = """{"query":"mutation CreateBatch($orders: [OrderInput!]!) { createBatch(orders: $orders) { count } }","variables":{"orders":[{"name":"Order1"},{"name":"Order2"}]}}""";
        Assert.Equal("mutation CreateBatch", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  EDGE CASES & CORNER CASES
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"query A { x }","extra":"value"}""", "query A")]
    [InlineData("""{"query":"query A { x }","variables":null}""", "query A")]
    [InlineData("""{"query":"query A { x }","variables":{}}""", "query A")]
    [InlineData("""{"query":"query A { x }","extensions":null}""", "query A")]
    public void Extra_or_null_fields_do_not_interfere(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }

    [Fact]
    public void Query_value_containing_braces_in_strings_inside_variables()
    {
        // Variables contain strings with { } characters — depth tracking must handle them
        var body = """{"variables":{"template":"Hello {name}!"},"query":"query GetTemplate { template { content } }"}""";
        Assert.Equal("query GetTemplate", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_value_containing_escaped_quotes_in_variable_strings()
    {
        var body = """{"variables":{"name":"She said \"hello\""},"query":"query GetGreeting { greeting { message } }"}""";
        Assert.Equal("query GetGreeting", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_after_boolean_variable()
    {
        var body = """{"variables":{"includeDetails":true},"query":"query GetUser { user { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_after_null_variables()
    {
        var body = """{"variables":null,"query":"query GetUser { user { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_after_empty_variables()
    {
        var body = """{"variables":{},"query":"query GetUser { user { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Single_character_operation_name()
    {
        var body = """{"query":"query Q { user { name } }"}""";
        Assert.Equal("query Q", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Very_long_operation_name()
    {
        var name = new string('A', 200);
        var body = $$$"""{"query":"query {{{name}}} { user { name } }"}""";
        Assert.Equal($"query {name}", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Very_long_query_string()
    {
        var fields = string.Join(" ", Enumerable.Range(0, 1000).Select(i => $"field{i}"));
        var body = $$"""{"query":"query GetAll { entity { {{fields}} } }"}""";
        Assert.Equal("query GetAll", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Operation_name_that_starts_with_underscore()
    {
        var body = """{"query":"query _internal { user { name } }"}""";
        Assert.Equal("query _internal", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Operation_name_with_digits()
    {
        var body = """{"query":"query GetV2Users { users { name } }"}""";
        Assert.Equal("query GetV2Users", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Query_with_comment_like_content_in_string()
    {
        // GraphQL doesn't have comments in JSON transport, but strings might contain # characters
        var body = """{"query":"query GetUser { user(filter: \"#active\") { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Fact]
    public void Content_that_starts_with_whitespace_then_json()
    {
        var body = "   \n  " + """{"query":"query GetUser { user { name } }"}""";
        Assert.Equal("query GetUser", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  BATCH / ARRAY GRAPHQL REQUESTS
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void Array_batch_request_returns_null()
    {
        // GraphQL batch requests are JSON arrays, not objects
        var body = """[{"query":"query A { a }"},{"query":"query B { b }"}]""";
        Assert.Null(GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  PARAMETERIZED THEORY: MANY NAMED OPERATIONS
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("query", "GetUser", "query GetUser")]
    [InlineData("query", "ListOrders", "query ListOrders")]
    [InlineData("query", "SearchProducts", "query SearchProducts")]
    [InlineData("query", "FetchCustomer", "query FetchCustomer")]
    [InlineData("query", "LoadDashboard", "query LoadDashboard")]
    [InlineData("query", "ReadProfile", "query ReadProfile")]
    [InlineData("query", "FindInventory", "query FindInventory")]
    [InlineData("query", "GetOrderHistory", "query GetOrderHistory")]
    [InlineData("query", "CheckBalance", "query CheckBalance")]
    [InlineData("query", "VerifyEmail", "query VerifyEmail")]
    [InlineData("mutation", "CreateUser", "mutation CreateUser")]
    [InlineData("mutation", "UpdateOrder", "mutation UpdateOrder")]
    [InlineData("mutation", "DeleteProduct", "mutation DeleteProduct")]
    [InlineData("mutation", "CancelSubscription", "mutation CancelSubscription")]
    [InlineData("mutation", "SubmitPayment", "mutation SubmitPayment")]
    [InlineData("mutation", "ResetPassword", "mutation ResetPassword")]
    [InlineData("mutation", "ToggleFeature", "mutation ToggleFeature")]
    [InlineData("mutation", "ArchiveOrder", "mutation ArchiveOrder")]
    [InlineData("mutation", "RefundPayment", "mutation RefundPayment")]
    [InlineData("mutation", "AssignRole", "mutation AssignRole")]
    [InlineData("subscription", "OnOrderCreated", "subscription OnOrderCreated")]
    [InlineData("subscription", "OnUserLoggedIn", "subscription OnUserLoggedIn")]
    [InlineData("subscription", "OnPaymentReceived", "subscription OnPaymentReceived")]
    [InlineData("subscription", "OnInventoryChanged", "subscription OnInventoryChanged")]
    [InlineData("subscription", "OnNotification", "subscription OnNotification")]
    public void Named_operations_parametrized(string type, string name, string expected)
    {
        var body = $$"""{"query":"{{type}} {{name}} { result { id } }"}""";
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(body));
    }

    [Theory]
    [InlineData("query", "GetUser", "$id: ID!")]
    [InlineData("query", "ListOrders", "$status: String!, $limit: Int")]
    [InlineData("mutation", "CreateUser", "$input: CreateUserInput!")]
    [InlineData("mutation", "UpdateOrder", "$id: ID!, $status: OrderStatus!")]
    [InlineData("subscription", "OnOrder", "$orderId: String!")]
    public void Named_operations_with_variable_signatures(string type, string name, string vars)
    {
        var body = $$"""{"query":"{{type}} {{name}}({{vars}}) { result { id } }"}""";
        Assert.Equal($"{type} {name}", GraphQlOperationDetector.TryExtractLabel(body));
    }

    // ════════════════════════════════════════════════════════════
    //  PARAMETERIZED THEORY: MANY JSON KEY ORDERINGS
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"query A { x }"}""")]
    [InlineData("""{"query":"query A { x }","variables":{}}""")]
    [InlineData("""{"query":"query A { x }","operationName":"A"}""")]
    [InlineData("""{"query":"query A { x }","variables":{},"operationName":"A"}""")]
    [InlineData("""{"query":"query A { x }","operationName":"A","variables":{}}""")]
    [InlineData("""{"variables":{},"query":"query A { x }"}""")]
    [InlineData("""{"variables":{},"query":"query A { x }","operationName":"A"}""")]
    [InlineData("""{"operationName":"A","query":"query A { x }"}""")]
    [InlineData("""{"operationName":"A","query":"query A { x }","variables":{}}""")]
    [InlineData("""{"operationName":"A","variables":{},"query":"query A { x }"}""")]
    [InlineData("""{"variables":{},"operationName":"A","query":"query A { x }"}""")]
    [InlineData("""{"extensions":{},"query":"query A { x }"}""")]
    [InlineData("""{"extensions":{},"variables":{},"query":"query A { x }"}""")]
    [InlineData("""{"variables":{},"extensions":{},"query":"query A { x }"}""")]
    [InlineData("""{"operationName":"A","extensions":{},"variables":{},"query":"query A { x }"}""")]
    public void All_key_orderings_produce_same_result(string content)
    {
        Assert.Equal("query A", GraphQlOperationDetector.TryExtractLabel(content));
    }

    // ════════════════════════════════════════════════════════════
    //  PARAMETERIZED THEORY: SHORTHAND VS EXPLICIT query KEYWORD
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"query":"{ user { name } }"}""", "query")]
    [InlineData("""{"query":"query { user { name } }"}""", "query")]
    [InlineData("""{"query":"query GetUser { user { name } }"}""", "query GetUser")]
    public void Shorthand_vs_explicit_vs_named(string content, string expected)
    {
        Assert.Equal(expected, GraphQlOperationDetector.TryExtractLabel(content));
    }
}
