using System.Net;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

public class PlantUmlCreatorTests
{
    // ─── Helpers ────────────────────────────────────────────────

    private static readonly string Nl = Environment.NewLine;

    private static RequestResponseLog MakeRequest(
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "WebApp",
        string method = "GET",
        string uri = "http://example.com/api/orders",
        string? content = null,
        (string Key, string? Value)[]? headers = null,
        RequestResponseMetaType metaType = RequestResponseMetaType.Default,
        string[]? focusFields = null)
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: HttpMethod.Parse(method),
            Content: content,
            Uri: new Uri(uri),
            Headers: headers ?? [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            MetaType: metaType)
        {
            FocusFields = focusFields
        };
    }

    private static RequestResponseLog MakeResponse(
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "WebApp",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? content = null,
        (string Key, string? Value)[]? headers = null,
        RequestResponseMetaType metaType = RequestResponseMetaType.Default,
        string[]? focusFields = null)
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: HttpMethod.Get,
            Content: content,
            Uri: new Uri("http://example.com/api/orders"),
            Headers: headers ?? [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: statusCode,
            MetaType: metaType)
        {
            FocusFields = focusFields
        };
    }

    private static RequestResponseLog MakeOverrideStart(
        string testId = "test-1",
        string? plantUml = null)
    {
        return new RequestResponseLog(
            TestName: testId,
            TestId: testId,
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideStart = true,
            PlantUml = plantUml
        };
    }

    private static RequestResponseLog MakeOverrideEnd(
        string testId = "test-1",
        string? plantUml = null)
    {
        return new RequestResponseLog(
            TestName: testId,
            TestId: testId,
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideEnd = true,
            PlantUml = plantUml
        };
    }

    private static string GetPlantUml(IEnumerable<RequestResponseLog> logs, bool separateSetup = false, bool highlightSetup = true, string? setupHighlightColor = null)
    {
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: separateSetup, highlightSetup: highlightSetup, setupHighlightColor: setupHighlightColor).ToList();
        return results.Single().PlantUmls.First().PlainText;
    }

    // ─── Null & empty input ─────────────────────────────────────

    [Fact]
    public void Returns_empty_when_input_is_null()
    {
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(null).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void Returns_empty_when_input_is_empty_collection()
    {
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([]).ToList();

        Assert.Empty(results);
    }

    // ─── Default excluded headers ───────────────────────────────

    [Fact]
    public void Default_excluded_headers_contain_cache_control_and_pragma()
    {
        var headers = PlantUmlCreator.DefaultExcludedHeaders;

        Assert.Contains("Cache-Control", headers);
        Assert.Contains("Pragma", headers);
    }

    // ─── Grouping by TestId ─────────────────────────────────────

    [Fact]
    public void Groups_traces_by_test_id_returning_one_result_per_test()
    {
        var logs = new[]
        {
            MakeRequest(testId: "test-A", testName: "Test A"),
            MakeResponse(testId: "test-A", testName: "Test A"),
            MakeRequest(testId: "test-B", testName: "Test B"),
            MakeResponse(testId: "test-B", testName: "Test B"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.TestId == "test-A" && r.TestName == "Test A");
        Assert.Contains(results, r => r.TestId == "test-B" && r.TestName == "Test B");
    }

    // ─── Basic request arrow ────────────────────────────────────

    [Fact]
    public void Request_produces_arrow_with_method_and_path()
    {
        var logs = new[] { MakeRequest(uri: "http://example.com/api/orders?id=1") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains($"webApp -[#438DD5]> orderService: GET: /api/orders?id=1{Nl}", plantUml);
    }

    [Fact]
    public void First_caller_is_actor_and_service_is_entity()
    {
        var logs = new[] { MakeRequest(callerName: "User", serviceName: "Api") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("actor \"User\" as user", plantUml);
        Assert.Contains("entity \"Api\" as api", plantUml);
    }

    // ─── GraphQL label enrichment ───────────────────────────────

    [Fact]
    public void GraphQL_named_query_appends_operation_label_to_arrow()
    {
        var graphqlContent = """{"query":"query GetUser { user { name } }"}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains($"POST: /graphql\\n(query GetUser){Nl}", plantUml);
    }

    [Fact]
    public void GraphQL_named_mutation_appends_operation_label_to_arrow()
    {
        var graphqlContent = """{"query":"mutation CreateOrder { createOrder { id } }"}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/api/data", content: graphqlContent),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains($"POST: /api/data\\n(mutation CreateOrder){Nl}", plantUml);
    }

    [Fact]
    public void GraphQL_anonymous_query_appends_query_label_to_arrow()
    {
        var graphqlContent = """{"query":"{ user { name } }"}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains($"POST: /graphql\\n(query){Nl}", plantUml);
    }

    [Fact]
    public void Non_GraphQL_POST_has_no_extra_label()
    {
        var jsonContent = """{"name":"Alice","age":30}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/api/users", content: jsonContent),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains($"POST: /api/users{Nl}", plantUml);
        Assert.DoesNotContain("query", plantUml);
        Assert.DoesNotContain("mutation", plantUml);
    }

    // ─── GraphQL body formatting ────────────────────────────────

    [Fact]
    public void GraphQL_FormattedWithMetadata_formats_query_in_note()
    {
        var graphqlContent = """{"query":"query GetUser($id: ID!) { user(id: $id) { name } }","variables":{"id":"123"}}""";
        var logs = new[] { MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, graphQlBodyFormat: GraphQlBodyFormat.FormattedWithMetadata).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("query GetUser($id: ID!) {", plantUml);
        Assert.Contains("  user(id: $id) {", plantUml);
        Assert.Contains("    name", plantUml);
        Assert.Contains("variables:", plantUml);
        Assert.Contains("\"id\": \"123\"", plantUml);
    }

    [Fact]
    public void GraphQL_FormattedQueryOnly_shows_only_formatted_query_without_headers()
    {
        var graphqlContent = """{"query":"{ user { name } }","variables":{"id":"123"}}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent,
                headers: [("Content-Type", "application/json"), ("Authorization", "Bearer tok")]),
        };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, graphQlBodyFormat: GraphQlBodyFormat.FormattedQueryOnly).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("user {", plantUml);
        Assert.Contains("  name", plantUml);
        Assert.DoesNotContain("Content-Type", plantUml);
        Assert.DoesNotContain("Authorization", plantUml);
        Assert.DoesNotContain("variables", plantUml);
    }

    [Fact]
    public void GraphQL_Formatted_shows_formatted_query_with_headers()
    {
        var graphqlContent = """{"query":"{ user { name } }"}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent,
                headers: [("Content-Type", "application/json")]),
        };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, graphQlBodyFormat: GraphQlBodyFormat.Formatted).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("user {", plantUml);
        Assert.Contains("  name", plantUml);
        Assert.Contains("Content-Type", plantUml);
    }

    [Fact]
    public void GraphQL_Json_mode_shows_json_pretty_printed_body()
    {
        var graphqlContent = """{"query":"{ user { name } }"}""";
        var logs = new[] { MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, graphQlBodyFormat: GraphQlBodyFormat.Json).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        // In Json mode, the body is JSON pretty-printed — query value stays as single-line string
        Assert.Contains("\"query\": \"{ user { name } }\"", plantUml);
    }

    [Fact]
    public void GraphQL_with_FocusFields_falls_back_to_json_mode()
    {
        var graphqlContent = """{"query":"query GetUser { user { name age } }"}""";
        var logs = new[]
        {
            MakeRequest(method: "POST", uri: "http://example.com/graphql", content: graphqlContent,
                focusFields: ["query"]),
        };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs,
            graphQlBodyFormat: GraphQlBodyFormat.FormattedWithMetadata).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        // FocusFields forces Json mode — field highlighting needs JSON structure
        Assert.Contains("\"query\":", plantUml);
    }

    [Fact]
    public void Non_GraphQL_json_is_unaffected_by_GraphQL_format_setting()
    {
        var jsonContent = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(method: "POST", uri: "http://example.com/api/users", content: jsonContent) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, graphQlBodyFormat: GraphQlBodyFormat.FormattedWithMetadata).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        // Regular JSON gets pretty-printed as before
        Assert.Contains("\"name\": \"Alice\"", plantUml);
        Assert.Contains("\"age\": 30", plantUml);
    }

    // ─── Basic response arrow ───────────────────────────────────

    [Fact]
    public void Response_produces_return_arrow_with_status_text()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(statusCode: HttpStatusCode.OK),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains($"orderService -[#438DD5]-> webApp: OK{Nl}", plantUml);
    }

    [Fact]
    public void Http_302_response_appends_redirect_to_status()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(statusCode: HttpStatusCode.Found),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("orderService -[#438DD5]-> webApp: Found (Redirect)", plantUml);
    }

    [Fact]
    public void Http_404_response_shows_not_found()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(statusCode: HttpStatusCode.NotFound),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("orderService -[#438DD5]-> webApp: Not Found", plantUml);
    }

    [Fact]
    public void Http_500_response_shows_internal_server_error()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(statusCode: HttpStatusCode.InternalServerError),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("orderService -[#438DD5]-> webApp: Internal Server Error", plantUml);
    }

    // ─── PlantUML structure ─────────────────────────────────────

    [Fact]
    public void Output_begins_with_startuml_and_ends_with_enduml()
    {
        var logs = new[] { MakeRequest() };
        var plantUml = GetPlantUml(logs);

        Assert.StartsWith("@startuml", plantUml);
        Assert.EndsWith($"@enduml{Nl}", plantUml);
    }

    [Fact]
    public void Output_contains_autonumber_directive()
    {
        var logs = new[] { MakeRequest() };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("autonumber 1", plantUml);
    }

    [Fact]
    public void Output_contains_wrap_width_setting()
    {
        var logs = new[] { MakeRequest() };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("skinparam wrapWidth 800", plantUml);
    }

    // ─── Headers ────────────────────────────────────────────────

    [Fact]
    public void Request_headers_appear_in_note_left()
    {
        var logs = new[]
        {
            MakeRequest(headers: [("Authorization", "Bearer token123")]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note left", plantUml);
        Assert.Contains("[Authorization=Bearer token123]", plantUml);
    }

    [Fact]
    public void Response_headers_appear_in_note_right()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(headers: [("X-Correlation-Id", "abc-123")]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note right", plantUml);
        Assert.Contains("[X-Correlation-Id=abc-123]", plantUml);
    }

    [Fact]
    public void Default_excluded_headers_are_omitted_from_notes()
    {
        var logs = new[]
        {
            MakeRequest(headers:
            [
                ("Cache-Control", "no-cache"),
                ("Pragma", "no-cache"),
                ("Accept", "application/json")
            ]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("Cache-Control", plantUml);
        Assert.DoesNotContain("Pragma", plantUml);
        Assert.Contains("[Accept=application/json]", plantUml);
    }

    [Fact]
    public void Custom_excluded_headers_are_respected()
    {
        var logs = new[]
        {
            MakeRequest(headers:
            [
                ("Accept", "application/json"),
                ("Authorization", "Bearer xyz")
            ]),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            excludedHeaders: ["Authorization"]).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("Authorization", plantUml);
        Assert.Contains("[Accept=application/json]", plantUml);
    }

    [Fact]
    public void Headers_are_sorted_alphabetically()
    {
        var logs = new[]
        {
            MakeRequest(headers:
            [
                ("Zebra", "z"),
                ("Alpha", "a"),
                ("Middle", "m")
            ]),
        };
        var plantUml = GetPlantUml(logs);

        var alphaPos = plantUml.IndexOf("[Alpha=a]", StringComparison.Ordinal);
        var middlePos = plantUml.IndexOf("[Middle=m]", StringComparison.Ordinal);
        var zebraPos = plantUml.IndexOf("[Zebra=z]", StringComparison.Ordinal);

        Assert.True(alphaPos < middlePos, "Alpha should appear before Middle");
        Assert.True(middlePos < zebraPos, "Middle should appear before Zebra");
    }

    // ─── JSON content formatting ────────────────────────────────

    [Fact]
    public void Json_request_body_is_pretty_printed_in_note()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json) };
        var plantUml = GetPlantUml(logs);

        // Pretty-printed JSON should contain line breaks
        Assert.Contains("\"name\": \"Alice\"", plantUml);
        Assert.Contains("\"age\": 30", plantUml);
    }

    [Fact]
    public void Json_array_body_is_pretty_printed()
    {
        var json = """[{"id":1},{"id":2}]""";
        var logs = new[] { MakeRequest(content: json) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"id\": 1", plantUml);
        Assert.Contains("\"id\": 2", plantUml);
    }

    [Fact]
    public void Json_response_body_is_pretty_printed_in_note()
    {
        var json = """{"result":"success"}""";
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: json),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note right", plantUml);
        Assert.Contains("\"result\": \"success\"", plantUml);
    }

    [Fact]
    public void Json_null_properties_are_stripped_from_formatted_output()
    {
        var json = """{"name":"Alice","age":null,"address":null,"city":"London"}""";
        var logs = new[] { MakeRequest(content: json) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"name\": \"Alice\"", plantUml);
        Assert.Contains("\"city\": \"London\"", plantUml);
        Assert.DoesNotContain("\"age\"", plantUml);
        Assert.DoesNotContain("\"address\"", plantUml);
    }

    [Fact]
    public void Json_nested_null_properties_are_stripped_from_formatted_output()
    {
        var json = """{"config":{"query":"SELECT 1","timeout":null,"options":null},"status":"done","error":null}""";
        var logs = new[] { MakeRequest(content: json) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"query\": \"SELECT 1\"", plantUml);
        Assert.Contains("\"status\": \"done\"", plantUml);
        Assert.DoesNotContain("\"timeout\"", plantUml);
        Assert.DoesNotContain("\"options\"", plantUml);
        Assert.DoesNotContain("\"error\"", plantUml);
    }

    [Fact]
    public void Json_array_with_null_properties_in_elements_are_stripped()
    {
        var json = """[{"name":"flour","type":null},{"name":"sugar","type":null}]""";
        var logs = new[] { MakeRequest(content: json) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"name\": \"flour\"", plantUml);
        Assert.Contains("\"name\": \"sugar\"", plantUml);
        Assert.DoesNotContain("\"type\"", plantUml);
    }

    // ─── Non-JSON content formatting ────────────────────────────

    [Fact]
    public void Plain_text_response_body_appears_as_is()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: "plain text body"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("plain text body", plantUml);
    }

    [Fact]
    public void Form_url_encoded_request_body_is_split_on_ampersands()
    {
        var body = "grant_type=client_credentials&client_id=myapp&scope=openid";
        var logs = new[] { MakeRequest(content: body) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("grant_type=client_credentials", plantUml);
        Assert.Contains("client_id=myapp", plantUml);
        Assert.Contains("scope=openid", plantUml);
    }

    [Fact]
    public void Empty_request_body_does_not_produce_note()
    {
        var logs = new[] { MakeRequest(content: null, headers: []) };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("note left", plantUml);
    }

    [Fact]
    public void Empty_response_body_and_no_headers_does_not_produce_note()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: null, headers: []),
        };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("note right", plantUml);
    }

    // ─── Long URLs ──────────────────────────────────────────────

    [Fact]
    public void Long_url_is_broken_into_chunks_at_max_url_length()
    {
        var longPath = "/api/" + new string('x', 150);
        var logs = new[] { MakeRequest(uri: "http://example.com" + longPath) };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxUrlLength: 50).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        // The path should be split using \\n (line continuation in PlantUML)
        Assert.Contains("\\n", plantUml);
    }

    [Fact]
    public void Short_url_is_not_broken_into_chunks()
    {
        var logs = new[] { MakeRequest(uri: "http://example.com/api/orders") };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxUrlLength: 200).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("\\n", plantUml);
    }

    // ─── Multiple entities ──────────────────────────────────────

    [Fact]
    public void Second_caller_is_entity_not_actor()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Gateway"),
            MakeResponse(callerName: "User", serviceName: "Gateway"),
            MakeRequest(callerName: "Gateway", serviceName: "Backend"),
            MakeResponse(callerName: "Gateway", serviceName: "Backend"),
        };
        var plantUml = GetPlantUml(logs);

        // "User" is the first caller, so it's actor
        Assert.Contains("actor \"User\" as user", plantUml);
        // "Gateway" first appears as a service, then as a caller — entity both times
        Assert.Contains("entity \"Gateway\" as gateway", plantUml);
        Assert.Contains("entity \"Backend\" as backend", plantUml);
        // "Gateway" should only appear once in the entity declarations
        var gatewayEntityCount = plantUml.Split("entity \"Gateway\" as gateway").Length - 1;
        Assert.Equal(1, gatewayEntityCount);
    }

    [Fact]
    public void Each_unique_participant_is_declared_only_once()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "Client", serviceName: "Api"),
            MakeResponse(callerName: "Client", serviceName: "Api"),
            MakeRequest(callerName: "Client", serviceName: "Api"),
            MakeResponse(callerName: "Client", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        var actorCount = plantUml.Split("actor \"Client\"").Length - 1;
        var entityCount = plantUml.Split("entity \"Api\"").Length - 1;
        Assert.Equal(1, actorCount);
        Assert.Equal(1, entityCount);
    }

    // ─── Overrides ──────────────────────────────────────────────

    [Fact]
    public void Override_suppresses_interaction_arrows_between_start_and_end()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeOverrideStart(),
            MakeRequest(callerName: "User", serviceName: "HiddenService"),
            MakeResponse(callerName: "User", serviceName: "HiddenService"),
            MakeOverrideEnd(),
            MakeRequest(callerName: "User", serviceName: "VisibleService"),
            MakeResponse(callerName: "User", serviceName: "VisibleService"),
        };
        var plantUml = GetPlantUml(logs);

        // The arrows for the overridden traces should not appear
        Assert.DoesNotContain("user -[#438DD5]> hiddenService:", plantUml);
        Assert.DoesNotContain("hiddenService -[#438DD5]-> user:", plantUml);
        // But visible service interactions should exist
        Assert.Contains("user -[#438DD5]> visibleService:", plantUml);
        Assert.Contains("visibleService -[#438DD5]-> user:", plantUml);
    }

    [Fact]
    public void Override_with_custom_plantuml_inserts_the_markup()
    {
        var customMarkup = "hnote across: Custom Note";
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeOverrideStart(plantUml: customMarkup),
            MakeOverrideEnd(),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("Custom Note", plantUml);
    }

    [Fact]
    public void Nested_override_start_is_ignored()
    {
        var logs = new[]
        {
            MakeOverrideStart(plantUml: "first-start"),
            MakeOverrideStart(plantUml: "nested-start"),
            MakeOverrideEnd(plantUml: "end-override"),
            MakeRequest(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("first-start", plantUml);
        Assert.DoesNotContain("nested-start", plantUml);
        Assert.Contains("end-override", plantUml);
    }

    [Fact]
    public void Override_end_without_start_still_emits_plantuml()
    {
        var logs = new[]
        {
            MakeOverrideEnd(plantUml: "end-markup"),
            MakeRequest(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("end-markup", plantUml);
    }

    // ─── Event meta type styling ────────────────────────────────

    [Fact]
    public void Event_meta_type_adds_style_block_and_note_class()
    {
        var logs = new[]
        {
            MakeRequest(content: "event payload", metaType: RequestResponseMetaType.Event),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("<<eventNote>>", plantUml);
        Assert.Contains(".eventNote", plantUml);
        Assert.Contains("BackgroundColor #cfecf7", plantUml);
    }

    [Fact]
    public void Default_meta_type_does_not_add_event_style_block()
    {
        var logs = new[]
        {
            MakeRequest(metaType: RequestResponseMetaType.Default),
        };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("<<eventNote>>", plantUml);
        Assert.DoesNotContain(".eventNote", plantUml);
    }

    [Fact]
    public void Event_note_class_appears_on_response_notes_too()
    {
        var logs = new[]
        {
            MakeRequest(content: "event request", metaType: RequestResponseMetaType.Event),
            MakeResponse(content: """{"event":true}""", metaType: RequestResponseMetaType.Event),
        };
        var plantUml = GetPlantUml(logs);

        var eventNoteOccurrences = plantUml.Split("<<eventNote>>").Length - 1;
        Assert.Equal(2, eventNoteOccurrences);
    }

    // ─── Pre/post formatting processors ─────────────────────────

    [Fact]
    public void Request_pre_formatting_processor_transforms_content_before_formatting()
    {
        var logs = new[]
        {
            MakeRequest(content: "SENSITIVE_DATA"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            requestPreFormattingProcessor: c => c.Replace("SENSITIVE_DATA", "REDACTED")).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("REDACTED", plantUml);
        Assert.DoesNotContain("SENSITIVE_DATA", plantUml);
    }

    [Fact]
    public void Request_post_formatting_processor_transforms_content_after_formatting()
    {
        var json = """{"key":"value"}""";
        var logs = new[]
        {
            MakeRequest(content: json),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            requestPostFormattingProcessor: c => c + "\n-- END --").ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("-- END --", plantUml);
    }

    [Fact]
    public void Response_pre_formatting_processor_transforms_content_before_formatting()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: "SECRET"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            responsePreFormattingProcessor: c => c.Replace("SECRET", "[HIDDEN]")).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("[HIDDEN]", plantUml);
        Assert.DoesNotContain("SECRET", plantUml);
    }

    [Fact]
    public void Response_post_formatting_processor_transforms_content_after_formatting()
    {
        var json = """{"data":"test"}""";
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: json),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            responsePostFormattingProcessor: c => c + "\n-- FOOTER --").ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("-- FOOTER --", plantUml);
    }

    // ─── Image tags ─────────────────────────────────────────────

    [Fact]
    public void Image_tags_use_default_plantuml_server_url()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.StartsWith("<img loading=\"lazy\" src=\"https://www.plantuml.com/plantuml/png/", imageTag);
        Assert.EndsWith("\">", imageTag);
    }

    [Fact]
    public void Image_tags_use_custom_plantuml_server_url()
    {
        var logs = new[] { MakeRequest() };
        var customUrl = "http://my-plantuml-server/svg";
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlServerRendererUrl: customUrl).ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.StartsWith("<img loading=\"lazy\" src=\"http://my-plantuml-server/svg/", imageTag);
    }

    [Fact]
    public void Trailing_slash_on_server_url_is_not_doubled()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs, plantUmlServerRendererUrl: "http://server/png/").ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.DoesNotContain("png//", imageTag);
    }

    [Fact]
    public void Image_tags_include_lazy_loading_by_default()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.StartsWith("<img loading=\"lazy\" src=\"", imageTag);
    }

    [Fact]
    public void Image_tags_omit_lazy_loading_when_disabled()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, lazyLoadImages: false).ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.StartsWith("<img src=\"", imageTag);
        Assert.DoesNotContain("loading", imageTag);
    }

    [Fact]
    public void Image_tags_include_lazy_loading_with_custom_server_url()
    {
        var logs = new[] { MakeRequest() };
        var customUrl = "http://my-plantuml-server/svg";
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlServerRendererUrl: customUrl, lazyLoadImages: true).ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.StartsWith("<img loading=\"lazy\" src=\"http://my-plantuml-server/svg/", imageTag);
    }

    // ─── Encoded PlantUML ───────────────────────────────────────

    [Fact]
    public void PlantUml_encoded_value_is_nonblank()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var encoded = results.Single().PlantUmls.First().PlantUmlEncoded;

        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);
    }

    [Fact]
    public void Same_input_produces_same_encoded_output()
    {
        var logs = new[] { MakeRequest(testId: "stable", testName: "Stable") };

        var run1 = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlantUmlEncoded;
        var run2 = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlantUmlEncoded;

        Assert.Equal(run1, run2);
    }

    // ─── PlantUmlForTest record ─────────────────────────────────

    [Fact]
    public void Result_contains_test_id_test_name_and_original_traces()
    {
        var request = MakeRequest(testId: "my-test", testName: "My Test Name");
        var response = MakeResponse(testId: "my-test", testName: "My Test Name");
        var logs = new[] { request, response };

        var result = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single();

        Assert.Equal("my-test", result.TestId);
        Assert.Equal("My Test Name", result.TestName);
        Assert.Equal(2, result.Traces.Count());
    }

    // ─── Large response splitting ───────────────────────────────

    [Fact]
    public void Very_large_response_body_is_split_across_multiple_diagrams()
    {
        // 15_000 chars per chunk; need >~15_000 to trigger splitting
        var largeBody = new string('A', 20_000);
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var diagramCount = results.Single().PlantUmls.Count();

        Assert.True(diagramCount >= 2, $"Expected at least 2 diagrams but got {diagramCount}");
    }

    [Fact]
    public void Split_diagrams_contain_continuation_markers()
    {
        var largeBody = new string('B', 20_000);
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        var firstDiagram = diagrams.First().PlainText;
        Assert.Contains("Continued On Next Diagram", firstDiagram);

        if (diagrams.Count > 1)
        {
            var secondDiagram = diagrams[1].PlainText;
            Assert.Contains("Continued From Previous Diagram", secondDiagram);
        }
    }

    [Fact]
    public void Each_split_diagram_has_startuml_and_enduml()
    {
        var largeBody = new string('C', 20_000);
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        foreach (var diagram in diagrams)
        {
            Assert.Contains("@startuml", diagram.PlainText);
            Assert.Contains("@enduml", diagram.PlainText);
        }
    }

    // ─── Diagram splitting on encoded length ────────────────────

    [Fact]
    public void Long_trace_sequence_produces_multiple_diagrams_when_encoded_exceeds_limit()
    {
        // Generate enough request/response pairs with large payloads to exceed the
        // 2000-char encoded PlantUML limit that triggers diagram splitting
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 200; i++)
        {
            logs.Add(MakeRequest(
                content: $"{{\"payload_{i}\": \"{new string('X', 300)}\"}}",
                headers: [("X-Trace", $"trace-{i}-{new string('H', 100)}")]));
            logs.Add(MakeResponse(
                content: $"{{\"result_{i}\": \"{new string('Y', 300)}\"}}",
                headers: [("X-Response", $"resp-{i}-{new string('R', 100)}")]));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var diagramCount = results.Single().PlantUmls.Count();

        Assert.True(diagramCount > 1, $"Expected multiple diagrams from long sequence but got {diagramCount}");
    }

    [Fact]
    public void Continuation_diagrams_have_correct_autonumber_sequence()
    {
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 50; i++)
        {
            logs.Add(MakeRequest(content: $"{{\"i\": \"{new string('X', 100)}\"}}"));
            logs.Add(MakeResponse(content: $"{{\"o\": \"{new string('Y', 100)}\"}}"));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        // First diagram should start at 1
        Assert.Contains("autonumber 1", diagrams.First().PlainText);

        // Subsequent diagrams should start at a higher number
        if (diagrams.Count > 1)
        {
            var secondDiagram = diagrams[1].PlainText;
            Assert.DoesNotContain("autonumber 1\n", secondDiagram);
        }
    }

    [Fact]
    public void Higher_maxEncodedDiagramLength_produces_fewer_diagrams()
    {
        // Generate enough traces to split at default 2000 but not at 8000.
        // Use varied (non-repetitive) content so PlantUML's deflate compression
        // doesn't eliminate the size difference between 2000 and 8000 char limits.
        // Keep pair count low (20 pairs ≈ 5,000px) to stay well under the
        // 12,000px height-split threshold.
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 20; i++)
        {
            var payload = string.Join(", ", Enumerable.Range(i * 10, 10).Select(n => $"\"{Guid.NewGuid():N}\": {n}"));
            var result = string.Join(", ", Enumerable.Range(i * 10, 10).Select(n => $"\"{Guid.NewGuid():N}\": {n}"));
            logs.Add(MakeRequest(content: $"{{{payload}}}"));
            logs.Add(MakeResponse(content: $"{{{result}}}"));
        }

        var defaultResults = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var defaultCount = defaultResults.Single().PlantUmls.Count();

        var largerResults = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000).ToList();
        var largerCount = largerResults.Single().PlantUmls.Count();

        Assert.True(defaultCount > 1, "Default limit should produce multiple diagrams");
        Assert.True(largerCount < defaultCount, $"8000 limit ({largerCount} diagrams) should produce fewer diagrams than default ({defaultCount})");
    }

    // ─── Height-based diagram splitting ─────────────────────────

    [Fact]
    public void Many_medium_sized_notes_split_on_estimated_height()
    {
        // Create many request/response pairs with medium-sized bodies (not large
        // enough to trigger the 15KB response chunk split or exceed 2000 encoded
        // chars on their own, but collectively very tall).
        var logs = new List<RequestResponseLog>();
        // Each pair: ~50 lines of note content × 18px ≈ 900px per request + response
        // 15 pairs ≈ 13,500px estimated, exceeding MaxEstimatedDiagramHeight (12,000)
        for (var i = 0; i < 15; i++)
        {
            var body = string.Join("\n", Enumerable.Range(0, 25).Select(n => $"\"field_{n}\": \"value_{n}\""));
            logs.Add(MakeRequest(content: $"{{{body}}}"));
            logs.Add(MakeResponse(content: $"{{{body}}}"));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000).ToList();
        var diagramCount = results.Single().PlantUmls.Count();

        Assert.True(diagramCount >= 2, $"Expected height-based split to produce at least 2 diagrams but got {diagramCount}");
    }

    [Fact]
    public void Height_split_diagrams_each_have_valid_plantuml_structure()
    {
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 15; i++)
        {
            var body = string.Join("\n", Enumerable.Range(0, 25).Select(n => $"\"f_{n}\": \"v_{n}\""));
            logs.Add(MakeRequest(content: $"{{{body}}}"));
            logs.Add(MakeResponse(content: $"{{{body}}}"));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        foreach (var diagram in diagrams)
        {
            Assert.Contains("@startuml", diagram.PlainText);
            Assert.Contains("@enduml", diagram.PlainText);
            Assert.Contains("autonumber", diagram.PlainText);
        }
    }

    [Fact]
    public void Small_diagram_does_not_split_on_height()
    {
        // A single request/response pair should never trigger height splitting
        var logs = new[]
        {
            MakeRequest(content: "{\"small\": \"value\"}"),
            MakeResponse(content: "{\"result\": \"ok\"}"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000).ToList();
        var diagramCount = results.Single().PlantUmls.Count();

        Assert.Equal(1, diagramCount);
    }

    [Fact]
    public void Height_split_does_not_split_on_last_trace()
    {
        // Even if height exceeds max on the last trace, it should not create an
        // empty trailing diagram — same behaviour as encoded length splitting
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 20; i++)
        {
            var body = string.Join("\n", Enumerable.Range(0, 30).Select(n => $"\"f_{n}\": \"v_{n}\""));
            logs.Add(MakeRequest(content: $"{{{body}}}"));
            logs.Add(MakeResponse(content: $"{{{body}}}"));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        // The last diagram should have actual content, not just prefix + @enduml
        var lastDiagram = diagrams.Last().PlainText;
        Assert.Contains("->", lastDiagram);
    }

    // ─── Camelized names ────────────────────────────────────────

    [Fact]
    public void Multi_word_service_names_are_camelized_for_aliases()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "web app", serviceName: "order service"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("webApp -[#438DD5]> orderService", plantUml);
    }

    [Fact]
    public void Service_display_names_remain_original_in_entity_declarations()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "Web App", serviceName: "Order Service"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"Web App\"", plantUml);
        Assert.Contains("\"Order Service\"", plantUml);
    }

    [Fact]
    public void Service_names_with_colons_produce_valid_aliases()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "localhost:80", serviceName: "localhost:5001"),
            MakeResponse(callerName: "localhost:80", serviceName: "localhost:5001"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"localhost:80\" as localhost_80", plantUml);
        Assert.Contains("\"localhost:5001\" as localhost_5001", plantUml);
        Assert.Contains("localhost_80 -[#438DD5]> localhost_5001", plantUml);
        Assert.Contains("localhost_5001 -[#438DD5]-> localhost_80", plantUml);
        Assert.DoesNotContain("as localhost:80", plantUml);
        Assert.DoesNotContain("as localhost:5001", plantUml);
    }

    // ─── Full request-response flow ─────────────────────────────

    [Fact]
    public void Complete_request_response_pair_produces_valid_sequence_diagram()
    {
        var logs = new[]
        {
            MakeRequest(
                callerName: "Browser",
                serviceName: "Api",
                method: "POST",
                uri: "http://api.example.com/users",
                content: """{"name":"Alice"}""",
                headers: [("Content-Type", "application/json")]),
            MakeResponse(
                callerName: "Browser",
                serviceName: "Api",
                statusCode: HttpStatusCode.Created,
                content: """{"id":1,"name":"Alice"}""",
                headers: [("Location", "/users/1")]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("@startuml", plantUml);
        Assert.Contains("actor \"Browser\" as browser", plantUml);
        Assert.Contains("entity \"Api\" as api", plantUml);
        Assert.Contains("browser -[#438DD5]> api: POST: /users", plantUml);
        Assert.Contains("note left", plantUml);
        Assert.Contains("\"name\": \"Alice\"", plantUml);
        Assert.Contains("api -[#438DD5]-> browser: Created", plantUml);
        Assert.Contains("note right", plantUml);
        Assert.Contains("\"id\": 1", plantUml);
        Assert.Contains("@enduml", plantUml);
    }

    // ─── Multiple services chain ────────────────────────────────

    [Fact]
    public void Multi_service_chain_shows_all_participants_and_interactions()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "Client", serviceName: "Gateway", method: "GET", uri: "http://gateway/api"),
            MakeRequest(callerName: "Gateway", serviceName: "Backend", method: "GET", uri: "http://backend/internal"),
            MakeResponse(callerName: "Gateway", serviceName: "Backend", statusCode: HttpStatusCode.OK),
            MakeResponse(callerName: "Client", serviceName: "Gateway", statusCode: HttpStatusCode.OK),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("client -[#438DD5]> gateway", plantUml);
        Assert.Contains("gateway -[#438DD5]> backend", plantUml);
        Assert.Contains("backend -[#438DD5]-> gateway", plantUml);
        Assert.Contains("gateway -[#438DD5]-> client", plantUml);
    }

    // ─── Color function ─────────────────────────────────────────

    [Fact]
    public void Headers_use_gray_color_tag()
    {
        var logs = new[]
        {
            MakeRequest(headers: [("Accept", "text/html")]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("<color:gray>", plantUml);
    }

    // ─── Invalid JSON is treated as plain text ──────────────────

    [Fact]
    public void Malformed_json_starting_with_brace_is_treated_as_plain_text()
    {
        var badJson = "{not valid json at all";
        var logs = new[] { MakeRequest(content: badJson) };
        var plantUml = GetPlantUml(logs);

        // Should still appear in the output without crashing
        Assert.Contains("{not valid json at all", plantUml);
    }

    [Fact]
    public void Malformed_json_starting_with_bracket_is_treated_as_plain_text()
    {
        var badJson = "[not valid json at all";
        var logs = new[] { MakeRequest(content: badJson) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("[not valid json at all", plantUml);
    }

    // ─── Long header values ─────────────────────────────────────

    [Fact]
    public void Long_header_value_is_chunked_across_multiple_lines()
    {
        var longValue = new string('V', 200);
        var logs = new[]
        {
            MakeRequest(headers: [("X-Long", longValue)]),
        };
        var plantUml = GetPlantUml(logs);

        // Each chunk gets its own <color:gray> prefix, so >80 chars means multiple lines
        var colorGrayCount = plantUml.Split("<color:gray>").Length - 1;
        Assert.True(colorGrayCount >= 2, $"Expected at least 2 <color:gray> lines for long header, got {colorGrayCount}");
    }

    [Fact]
    public void Header_chunks_do_not_exceed_safe_width_for_wrap()
    {
        // A header value that is 200 chars — well beyond a single chunk
        var longValue = new string('A', 200);
        var logs = new[]
        {
            MakeRequest(headers: [("Authorization", longValue)]),
        };
        var plantUml = GetPlantUml(logs);

        // Each <color:gray> line visible content must be ≤80 chars to avoid PlantUML wrapWidth overflow.
        // wrapWidth is 800px; at ~9px/char worst case, 80 chars = 720px which is safely under.
        var grayLines = plantUml.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("<color:gray>"))
            .Select(l => l["<color:gray>".Length..])
            .ToList();

        Assert.NotEmpty(grayLines);
        foreach (var content in grayLines)
        {
            Assert.True(content.Length <= 80,
                $"Header chunk exceeds safe width (80 chars): '{content}' ({content.Length} chars)");
        }
    }

    // ─── Null header value ──────────────────────────────────────

    [Fact]
    public void Null_header_value_renders_without_crashing()
    {
        var logs = new[]
        {
            MakeRequest(headers: [("X-NullVal", null)]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("[X-NullVal=]", plantUml);
    }

    // ─── Null StatusCode ────────────────────────────────────────

    [Fact]
    public void Response_with_null_status_code_does_not_crash()
    {
        var response = new RequestResponseLog(
            TestName: "My Test",
            TestId: "test-1",
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri("http://example.com/api"),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "WebApp",
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: null);

        var logs = new[] { MakeRequest(), response };
        var plantUml = GetPlantUml(logs);

        // Should produce an arrow even though status is null
        Assert.Contains("orderService -[#438DD5]-> webApp:", plantUml);
    }

    // ─── String status code via OneOf ───────────────────────────

    [Fact]
    public void Response_with_string_status_code_renders_the_string()
    {
        var response = new RequestResponseLog(
            TestName: "My Test",
            TestId: "test-1",
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri("http://example.com/api"),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "WebApp",
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: "CustomStatus");

        var logs = new[] { MakeRequest(), response };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("orderService -[#438DD5]-> webApp: Custom Status", plantUml);
    }

    // ─── Override with null PlantUml ────────────────────────────

    [Fact]
    public void Override_start_with_null_plantuml_emits_nothing_extra()
    {
        var logs = new[]
        {
            MakeOverrideStart(plantUml: null),
            MakeOverrideEnd(plantUml: null),
            MakeRequest(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        // Should produce a valid diagram with just the final request
        Assert.Contains("user -[#438DD5]> api:", plantUml);
        Assert.Contains("@startuml", plantUml);
        Assert.Contains("@enduml", plantUml);
    }

    // ─── Long form-URL-encoded segment ──────────────────────────

    [Fact]
    public void Long_form_url_encoded_segment_is_chunked()
    {
        var longParam = "key=" + new string('v', 200);
        var logs = new[] { MakeRequest(content: longParam) };
        var plantUml = GetPlantUml(logs);

        // The value exceeds 80 chars, so ChunksUpTo(80) should split it
        // Each chunk appears on its own line, which means multiple lines in the note
        Assert.Contains("key=", plantUml);
        Assert.Contains(new string('v', 80), plantUml);
    }

    // ─── ImageTags count matches PlantUmls count ────────────────

    [Fact]
    public void Image_tags_count_matches_plant_umls_count()
    {
        var largeBody = new string('Z', 20_000);
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody),
        };

        var result = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single();

        Assert.Equal(result.PlantUmls.Count(), result.ImageTags.Length);
        Assert.True(result.ImageTags.Length >= 2, "Should have multiple diagrams and matching image tags");
    }

    // ─── Empty string content vs null content ───────────────────

    [Fact]
    public void Empty_string_request_body_does_not_produce_note()
    {
        var logs = new[] { MakeRequest(content: "", headers: []) };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("note left", plantUml);
    }

    [Fact]
    public void Empty_string_response_body_does_not_produce_note()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: "", headers: []),
        };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("note right", plantUml);
    }

    // ─── Plain text request content (non-JSON, non-form) ────────

    [Fact]
    public void Plain_text_request_body_without_ampersands_appears_in_note()
    {
        var logs = new[] { MakeRequest(content: "plain request text") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note left", plantUml);
        Assert.Contains("plain request text", plantUml);
    }

    [Fact]
    public void Xml_request_body_is_treated_as_plain_text_with_ampersand_splitting()
    {
        var xml = "<root><item>value</item></root>";
        var logs = new[] { MakeRequest(content: xml) };
        var plantUml = GetPlantUml(logs);

        // It's not JSON, so it goes through the form-url-encoded path which splits on &
        // Since there's no &, the whole thing appears as-is
        Assert.Contains("<root><item>value</item></root>", plantUml);
    }

    // ─── actorDefined flag behavior ─────────────────────────────

    [Fact]
    public void Two_distinct_callers_that_first_appear_as_callers_are_both_rendered()
    {
        // First caller "Alice" calls "Api", then "Bob" calls "Api"
        // Both are new callers that weren't previously seen as services
        var logs = new[]
        {
            MakeRequest(callerName: "Alice", serviceName: "Api"),
            MakeResponse(callerName: "Alice", serviceName: "Api"),
            MakeRequest(callerName: "Bob", serviceName: "Api"),
            MakeResponse(callerName: "Bob", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        // "Alice" is the first caller seen, so it gets "actor"
        Assert.Contains("actor \"Alice\" as alice", plantUml);
        // "Bob" appears later as a fresh caller not previously seen as a service.
        // The code never sets actorDefined=true, so Bob also gets "actor" —
        // this documents the actual behavior.
        Assert.Contains("\"Bob\" as bob", plantUml);
    }

    // ─── HTTP method variations ─────────────────────────────────

    [Fact]
    public void Post_request_shows_POST_method_on_arrow()
    {
        var logs = new[] { MakeRequest(method: "POST", uri: "http://example.com/api/items") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("POST: /api/items", plantUml);
    }

    [Fact]
    public void Put_request_shows_PUT_method_on_arrow()
    {
        var logs = new[] { MakeRequest(method: "PUT", uri: "http://example.com/api/items/1") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("PUT: /api/items/1", plantUml);
    }

    [Fact]
    public void Delete_request_shows_DELETE_method_on_arrow()
    {
        var logs = new[] { MakeRequest(method: "DELETE", uri: "http://example.com/api/items/1") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("DELETE: /api/items/1", plantUml);
    }

    [Fact]
    public void Patch_request_shows_PATCH_method_on_arrow()
    {
        var logs = new[] { MakeRequest(method: "PATCH", uri: "http://example.com/api/items/1") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("PATCH: /api/items/1", plantUml);
    }

    // ─── Setup partition ────────────────────────────────────────

    [Fact]
    public void Setup_partition_wraps_traces_before_StartAction_marker_when_enabled()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.Contains("partition #F6F6F6 Setup", plantUml);
        var endMarker = Environment.NewLine + "end" + Environment.NewLine;
        var partitionOpen = plantUml.IndexOf("partition #F6F6F6 Setup");
        var setupCall = plantUml.IndexOf("/api/setup");
        var partitionEnd = plantUml.IndexOf(endMarker, partitionOpen);
        var actionCall = plantUml.IndexOf("/api/action");
        Assert.True(partitionOpen < setupCall, "Partition should open before setup call");
        Assert.True(setupCall < partitionEnd, "Setup call should be inside partition");
        Assert.True(partitionEnd < actionCall, "Action call should be after partition closes");
    }

    [Fact]
    public void No_partition_when_separateSetup_is_false_even_with_StartAction_marker()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("partition", plantUml);
    }

    [Fact]
    public void No_partition_when_no_StartAction_marker_even_when_separateSetup_is_true()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.DoesNotContain("partition", plantUml);
    }

    [Fact]
    public void Setup_partition_has_background_color()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.Matches("partition #[a-fA-F0-9]+ Setup", plantUml);
    }

    [Fact]
    public void Setup_partition_has_no_background_color_when_highlightSetup_is_false()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true, highlightSetup: false);

        Assert.Contains("partition Setup", plantUml);
        Assert.DoesNotMatch("partition #[a-fA-F0-9]+ Setup", plantUml);
    }

    [Fact]
    public void HighlightSetup_has_no_effect_when_separateSetup_is_false()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: false, highlightSetup: true);

        Assert.DoesNotContain("partition", plantUml);
    }

    [Fact]
    public void Setup_partition_uses_default_color_F6F6F6()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.Contains("partition #F6F6F6 Setup", plantUml);
    }

    [Fact]
    public void Setup_partition_uses_custom_color_when_configured()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true, setupHighlightColor: "#AABBCC");

        Assert.Contains("partition #AABBCC Setup", plantUml);
    }

    [Fact]
    public void StartAction_marker_is_not_rendered_as_entity()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.DoesNotContain("entity \"\" as", plantUml);
        Assert.DoesNotContain("actor \"\" as", plantUml);
    }

    [Fact]
    public void StartAction_marker_does_not_produce_arrow_or_note()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.DoesNotContain("override.com", plantUml);
    }

    [Fact]
    public void Setup_partition_works_with_multiple_setup_request_response_pairs()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup1"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup2"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        var endMarker = Environment.NewLine + "end" + Environment.NewLine;
        var partitionOpen = plantUml.IndexOf("partition #F6F6F6 Setup");
        var setup1 = plantUml.IndexOf("/api/setup1");
        var setup2 = plantUml.IndexOf("/api/setup2");
        var partitionEnd = plantUml.IndexOf(endMarker, partitionOpen);
        var actionCall = plantUml.IndexOf("/api/action");
        Assert.True(partitionOpen < setup1);
        Assert.True(setup1 < setup2);
        Assert.True(setup2 < partitionEnd);
        Assert.True(partitionEnd < actionCall);
    }

    [Fact]
    public void StartAction_at_beginning_produces_no_partition_since_setup_is_empty()
    {
        var logs = new[]
        {
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.DoesNotContain("partition", plantUml);
    }

    private static RequestResponseLog MakeActionStart(string testId = "test-1")
    {
        return new RequestResponseLog(
            TestName: testId,
            TestId: testId,
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsActionStart = true
        };
    }

    // ─── Partition + override interaction ────────────────────────

    [Fact]
    public void Override_between_setup_and_action_is_rendered_outside_partition()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeOverrideStart(plantUml: "\nhnote across #black:<color:white>Test 1\n"),
            MakeOverrideEnd(),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        var endMarker = Environment.NewLine + "end" + Environment.NewLine;
        var partitionEnd = plantUml.IndexOf(endMarker, plantUml.IndexOf("partition"));
        var hnote = plantUml.IndexOf("hnote across");
        var actionCall = plantUml.IndexOf("/api/action");
        Assert.True(partitionEnd < hnote, "Partition should close before the override hnote");
        Assert.True(hnote < actionCall, "Override hnote should appear before the action call");
    }

    [Fact]
    public void Multiple_overrides_between_setup_and_action_are_all_outside_partition()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeOverrideStart(plantUml: "\nhnote across #black:<color:white>Test 1\n"),
            MakeOverrideEnd(),
            MakeOverrideStart(plantUml: "\nhnote across #blue:<color:white>Marker\n"),
            MakeOverrideEnd(),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        var endMarker = Environment.NewLine + "end" + Environment.NewLine;
        var partitionEnd = plantUml.IndexOf(endMarker, plantUml.IndexOf("partition"));
        var hnote1 = plantUml.IndexOf("Test 1");
        var hnote2 = plantUml.IndexOf("Marker");
        Assert.True(partitionEnd < hnote1, "Partition should close before first override");
        Assert.True(partitionEnd < hnote2, "Partition should close before second override");
    }

    // ─── PlantUML structural validity ────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlantUml_has_balanced_partitions(bool separateSetup)
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: separateSetup);

        AssertBalancedPartitions(plantUml);
    }

    [Fact]
    public void PlantUml_has_balanced_partitions_with_override_between_setup_and_action()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeOverrideStart(plantUml: "\nhnote across #black:<color:white>Test 1\n"),
            MakeOverrideEnd(),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        AssertBalancedPartitions(plantUml);
    }

    [Fact]
    public void PlantUml_has_balanced_note_blocks()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup", content: "body"),
            MakeResponse(callerName: "User", serviceName: "Api", content: "response"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action", content: "action-body"),
            MakeResponse(callerName: "User", serviceName: "Api", content: "action-response"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        AssertBalancedNotes(plantUml);
    }

    [Fact]
    public void PlantUml_starts_with_startuml_and_ends_with_enduml()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.StartsWith("@startuml", plantUml.TrimStart());
        Assert.EndsWith("@enduml", plantUml.TrimEnd());
    }

    [Fact]
    public void PlantUml_contains_teoz_pragma_when_partition_is_used()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        Assert.Contains("!pragma teoz true", plantUml);
        var pragmaIndex = plantUml.IndexOf("!pragma teoz true");
        var partitionIndex = plantUml.IndexOf("partition");
        Assert.True(pragmaIndex < partitionIndex, "Teoz pragma must appear before partition");
    }

    [Fact]
    public void PlantUml_partition_block_is_well_formed()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };
        var plantUml = GetPlantUml(logs, separateSetup: true);

        // partition line uses teoz syntax (no braces)
        var partitionLine = plantUml.Split('\n').First(l => l.Contains("partition"));
        Assert.Equal("partition #F6F6F6 Setup", partitionLine.TrimEnd());

        // "end" line comes before action content
        var endMarker = Environment.NewLine + "end" + Environment.NewLine;
        var partitionOpen = plantUml.IndexOf("partition");
        var endLine = plantUml.IndexOf(endMarker, partitionOpen);
        Assert.True(endLine > partitionOpen, "Partition must have an end keyword");
        Assert.True(endLine < plantUml.IndexOf("/api/action"), "Partition must close before action");
    }

    private static void AssertBalancedPartitions(string plantUml)
    {
        // Count partition opens and their matching "end" lines (teoz syntax)
        var openCount = 0;
        var closeCount = 0;
        var inNote = false;
        foreach (var line in plantUml.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("note ")) inNote = true;
            if (trimmed == "end note") { inNote = false; continue; }
            if (inNote) continue;

            if (trimmed.StartsWith("partition ")) openCount++;
            else if (trimmed == "end") closeCount++;
        }
        Assert.Equal(openCount, closeCount);
    }

    [Fact]
    public void Setup_partition_is_balanced_when_large_response_triggers_chunking_split()
    {
        // Large response body during setup triggers AppendResponseNoteContent chunking,
        // which calls FinishAndStartNewDiagram directly. This reproduced the original
        // bug where a bare "end" appeared in a later diagram without a matching "partition".
        var largeBody = new string('X', 20_000);
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api", content: largeBody),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        Assert.True(diagrams.Count > 1, "Should produce multiple diagrams from large setup response");

        foreach (var diagram in diagrams)
            AssertBalancedPartitions(diagram.PlainText);
    }

    [Fact]
    public void Setup_partition_continues_across_split_diagrams_when_still_in_setup()
    {
        // When setup traces span multiple diagrams, each diagram should contain
        // a balanced partition open/close, and continuation diagrams should re-open it.
        var largeBody = new string('Y', 20_000);
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup-1"),
            MakeResponse(callerName: "User", serviceName: "Api", content: largeBody),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup-2"),
            MakeResponse(callerName: "User", serviceName: "Api"),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        Assert.True(diagrams.Count > 1, "Should produce multiple diagrams");

        // First diagram should open the partition
        Assert.Contains("partition", diagrams.First().PlainText);

        // All diagrams must have balanced partitions
        foreach (var diagram in diagrams)
            AssertBalancedPartitions(diagram.PlainText);

        // The continuation diagram that still has setup-2 should also contain a partition
        var setup2Diagram = diagrams.First(d => d.PlainText.Contains("/api/setup-2"));
        Assert.Contains("partition", setup2Diagram.PlainText);
    }

    [Fact]
    public void Action_traces_after_split_have_balanced_partitions_and_no_bare_end()
    {
        // After ActionStart, diagrams should not have unbalanced partition state.
        // The action diagram may contain a partition close if it also has the
        // final setup content, but it must be balanced.
        var largeBody = new string('Z', 20_000);
        var logs = new[]
        {
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup"),
            MakeResponse(callerName: "User", serviceName: "Api", content: largeBody),
            MakeActionStart(),
            MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action"),
            MakeResponse(callerName: "User", serviceName: "Api"),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        // Every diagram must have balanced partitions
        foreach (var diagram in diagrams)
            AssertBalancedPartitions(diagram.PlainText);

        // The last diagram (with action content) must not have a bare "end" outside notes
        var lastDiagram = diagrams.Last().PlainText;
        Assert.Contains("/api/action", lastDiagram);
        AssertBalancedPartitions(lastDiagram);
    }

    [Fact]
    public void Setup_partition_is_balanced_when_encoded_length_triggers_split()
    {
        // Triggers the per-trace encoded-length split path (as opposed to response chunking).
        var logs = new List<RequestResponseLog>();

        for (var i = 0; i < 200; i++)
        {
            logs.Add(MakeRequest(callerName: "User", serviceName: "Api",
                uri: "http://example.com/api/setup",
                content: $"{{\"payload_{i}\": \"{new string('X', 300)}\"}}",
                headers: [("X-Trace", $"trace-{i}-{new string('H', 100)}")]));
            logs.Add(MakeResponse(callerName: "User", serviceName: "Api",
                content: $"{{\"result_{i}\": \"{new string('Y', 300)}\"}}",
                headers: [("X-Response", $"resp-{i}-{new string('R', 100)}")]));
        }

        logs.Add(MakeActionStart());
        logs.Add(MakeRequest(callerName: "User", serviceName: "Api",
            uri: "http://example.com/api/action"));
        logs.Add(MakeResponse(callerName: "User", serviceName: "Api"));

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        Assert.True(diagrams.Count > 1, "Should produce multiple diagrams");

        foreach (var diagram in diagrams)
            AssertBalancedPartitions(diagram.PlainText);
    }

    private static void AssertBalancedNotes(string plantUml)
    {
        var noteOpens = plantUml.Split('\n').Count(l => l.Trim().StartsWith("note "));
        var noteCloses = plantUml.Split('\n').Count(l => l.Trim() == "end note");
        Assert.Equal(noteOpens, noteCloses);
    }

    // ─── String method (non-HTTP) on arrow ──────────────────────

    private static RequestResponseLog MakeStringMethodRequest(
        string protocol = "Send (Event Protocol)",
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "Event broker",
        string callerName = "MyApi",
        string uri = "event://event-broker/cake_events",
        string? content = null,
        (string Key, string? Value)[]? headers = null,
        RequestResponseMetaType metaType = RequestResponseMetaType.Event)
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: protocol,
            Content: content,
            Uri: new Uri(uri),
            Headers: headers ?? [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            MetaType: metaType);
    }

    private static RequestResponseLog MakeStringMethodResponse(
        string protocol = "Send (Event Protocol)",
        string statusCode = "Responded",
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "Event broker",
        string callerName = "MyApi",
        string uri = "event://event-broker/cake_events",
        string? content = null,
        (string Key, string? Value)[]? headers = null,
        RequestResponseMetaType metaType = RequestResponseMetaType.Event)
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: protocol,
            Content: content,
            Uri: new Uri(uri),
            Headers: headers ?? [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: statusCode,
            MetaType: metaType);
    }

    [Fact]
    public void String_method_renders_on_request_arrow()
    {
        var logs = new[] { MakeStringMethodRequest(protocol: "Send (Event Protocol)", uri: "event://event-broker/cake_events") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("myApi -[#438DD5]> eventBroker: Send (Event Protocol): /cake_events", plantUml);
    }

    [Fact]
    public void Event_response_with_string_status_code_renders_on_return_arrow()
    {
        var logs = new[]
        {
            MakeStringMethodRequest(),
            MakeStringMethodResponse(statusCode: "Responded"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("eventBroker -[#438DD5]-> myApi: Responded", plantUml);
    }

    // ─── Complete event round-trip (MessageTracker shape) ────────

    [Fact]
    public void Complete_event_round_trip_produces_valid_sequence_diagram()
    {
        var logs = new[]
        {
            MakeStringMethodRequest(
                protocol: "Send (Event Protocol)",
                callerName: "Cake Api",
                serviceName: "Event broker",
                uri: "event://event-broker/cake_events",
                content: """{"batchId":"abc-123","ingredients":["flour","sugar"]}"""),
            MakeStringMethodResponse(
                protocol: "Send (Event Protocol)",
                statusCode: "Responded",
                callerName: "Cake Api",
                serviceName: "Event broker",
                uri: "event://event-broker/cake_events",
                content: ""),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("@startuml", plantUml);
        Assert.Contains("\"Cake Api\" as cakeApi", plantUml);
        Assert.Contains("\"Event broker\" as eventBroker", plantUml);
        Assert.Contains("cakeApi -[#438DD5]> eventBroker: Send (Event Protocol): /cake_events", plantUml);
        Assert.Contains("eventBroker -[#438DD5]-> cakeApi: Responded", plantUml);
        Assert.Contains("<<eventNote>>", plantUml);
        Assert.Contains(".eventNote", plantUml);
        Assert.Contains("\"batchId\": \"abc-123\"", plantUml);
        Assert.Contains("@enduml", plantUml);
    }

    // ─── Mixed Event and Default traces ─────────────────────────

    [Fact]
    public void Mixed_event_and_default_traces_add_style_block_but_only_event_notes_get_class()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "Client", serviceName: "Api", content: "http body"),
            MakeResponse(callerName: "Client", serviceName: "Api", content: """{"ok":true}"""),
            MakeStringMethodRequest(callerName: "Api", serviceName: "Event broker", content: """{"event":true}"""),
            MakeStringMethodResponse(callerName: "Api", serviceName: "Event broker"),
        };
        var plantUml = GetPlantUml(logs);

        // Style block should be present because at least one event trace exists
        Assert.Contains(".eventNote", plantUml);

        // Count <<eventNote>> occurrences — should be exactly 2 (event request + event response notes)
        // but event response has no content/headers so no note → only 1
        var eventNoteCount = plantUml.Split("<<eventNote>>").Length - 1;
        Assert.Equal(1, eventNoteCount);

        // Default-typed notes should NOT have the class
        // The "http body" request note should be a plain "note left" without <<eventNote>>
        var lines = plantUml.Split(Nl);
        var httpNoteIndex = Array.FindIndex(lines, l => l.Contains("http body"));
        Assert.True(httpNoteIndex > 0);
        // The "note left" line immediately before should not have <<eventNote>>
        var noteLeftLine = lines.Take(httpNoteIndex).Last(l => l.TrimStart().StartsWith("note"));
        Assert.DoesNotContain("<<eventNote>>", noteLeftLine);
    }

    // ─── Headers-only notes (no content) ────────────────────────

    [Fact]
    public void Request_with_headers_but_empty_content_still_produces_note()
    {
        var logs = new[]
        {
            MakeRequest(content: null, headers: [("Authorization", "Bearer abc123")]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note left", plantUml);
        Assert.Contains("[Authorization=Bearer abc123]", plantUml);
    }

    [Fact]
    public void Response_with_headers_but_empty_content_still_produces_note()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: null, headers: [("X-Request-Id", "req-42")]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note right", plantUml);
        Assert.Contains("[X-Request-Id=req-42]", plantUml);
    }

    // ─── Focus field formatting ─────────────────────────────────

    [Fact]
    public void Focused_request_fields_are_bold_in_note_by_default()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json, focusFields: ["name"]) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note left", plantUml);
        Assert.Contains("<b>\"name\": \"Alice\"", plantUml);
    }

    [Fact]
    public void Non_focused_request_fields_are_lightgray_by_default()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json, focusFields: ["name"]) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("<color:lightgray>\"age\": 30</color>", plantUml);
    }

    [Fact]
    public void Focused_response_fields_are_bold_in_note_by_default()
    {
        var json = """{"id":"123","status":"ok"}""";
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: json, focusFields: ["status"]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note right", plantUml);
        Assert.Contains("<b>\"status\": \"ok\"</b>", plantUml);
    }

    [Fact]
    public void Non_focused_response_fields_are_lightgray_by_default()
    {
        var json = """{"id":"123","status":"ok"}""";
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: json, focusFields: ["status"]),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("<color:lightgray>\"id\": \"123\"", plantUml);
    }

    [Fact]
    public void Custom_focus_emphasis_is_respected()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json, focusFields: ["name"]) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            focusEmphasis: FocusEmphasis.Colored,
            focusDeEmphasis: FocusDeEmphasis.None).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("<color:blue>\"name\": \"Alice\"", plantUml);
        Assert.DoesNotContain("<b>", plantUml);
    }

    [Fact]
    public void Hidden_focus_deemphasis_replaces_non_focused_with_ellipsis()
    {
        var json = """{"a":"1","name":"Alice","b":"2"}""";
        var logs = new[] { MakeRequest(content: json, focusFields: ["name"]) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            focusDeEmphasis: FocusDeEmphasis.Hidden).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("\"name\": \"Alice\"", plantUml);
        Assert.DoesNotContain("\"a\"", plantUml);
        Assert.DoesNotContain("\"b\"", plantUml);
        Assert.Contains("...", plantUml);
    }

    [Fact]
    public void No_focus_fields_renders_json_normally()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json) };
        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain("<b>", plantUml);
        Assert.DoesNotContain("<color:lightgray>", plantUml);
        Assert.Contains("\"name\": \"Alice\"", plantUml);
    }

    [Fact]
    public void Focus_fields_with_non_json_content_are_ignored()
    {
        var logs = new[] { MakeRequest(content: "plain text body", focusFields: ["name"]) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("plain text body", plantUml);
        Assert.DoesNotContain("<b>", plantUml);
    }

    [Fact]
    public void Focus_does_not_affect_headers_formatting()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json, headers: [("Authorization", "Bearer xyz")], focusFields: ["name"]) };
        var plantUml = GetPlantUml(logs);

        // Headers still use the gray color tag
        Assert.Contains("<color:gray>[Authorization=Bearer xyz]", plantUml);
        // But JSON fields use focus formatting
        Assert.Contains("<b>\"name\": \"Alice\"", plantUml);
    }

    [Fact]
    public void Focus_composes_with_post_processor()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json, focusFields: ["name"]) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            requestPostFormattingProcessor: s => s.Replace("Alice", "***")).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        // Post-processor runs AFTER focus formatting
        Assert.Contains("***", plantUml);
    }

    [Fact]
    public void Focus_emphasis_none_and_deemphasis_none_renders_normally()
    {
        var json = """{"name":"Alice","age":30}""";
        var logs = new[] { MakeRequest(content: json, focusFields: ["name"]) };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            focusEmphasis: FocusEmphasis.None,
            focusDeEmphasis: FocusDeEmphasis.None).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("<b>", plantUml);
        Assert.DoesNotContain("<color:lightgray>", plantUml);
        Assert.DoesNotContain("<color:blue>", plantUml);
        Assert.Contains("\"name\": \"Alice\"", plantUml);
    }

    // ─── PlantUML theme ─────────────────────────────────────────

    [Fact]
    public void Theme_directive_is_included_when_theme_is_specified()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlTheme: "spacelab").ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("!theme spacelab", plantUml);
    }

    [Fact]
    public void Theme_directive_appears_after_startuml_and_before_pragma()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlTheme: "cerulean").ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        var themeIndex = plantUml.IndexOf("!theme cerulean");
        var startUmlIndex = plantUml.IndexOf("@startuml");
        var pragmaIndex = plantUml.IndexOf("!pragma teoz true");

        Assert.True(themeIndex > startUmlIndex, "Theme should appear after @startuml");
        Assert.True(themeIndex < pragmaIndex, "Theme should appear before !pragma");
    }

    [Fact]
    public void No_theme_directive_when_theme_is_null()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlTheme: null).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("!theme", plantUml);
    }

    [Fact]
    public void No_theme_directive_when_theme_is_empty_string()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlTheme: "").ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("!theme", plantUml);
    }

    [Fact]
    public void No_theme_directive_when_theme_is_whitespace()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlTheme: "   ").ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("!theme", plantUml);
    }

    [Fact]
    public void Theme_is_included_in_all_diagrams_when_response_is_split()
    {
        var largeBody = new string('A', 20_000);
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody)
        };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, plantUmlTheme: "materia").ToList();
        var plantUmls = results.Single().PlantUmls.ToList();

        Assert.True(plantUmls.Count > 1, "Should have multiple diagrams");
        foreach (var diagram in plantUmls)
            Assert.Contains("!theme materia", diagram.PlainText);
    }

    // ─── PlantUML image format ──────────────────────────────────

    [Fact]
    public void Default_image_format_is_png()
    {
        Assert.Equal(PlantUmlImageFormat.Png, new ReportConfigurationOptions().PlantUmlImageFormat);
        Assert.Equal(PlantUmlImageFormat.Png, new DiagramsFetcherOptions().PlantUmlImageFormat);
    }

    [Fact]
    public void Image_format_enum_has_png_and_svg()
    {
        Assert.True(Enum.IsDefined(typeof(PlantUmlImageFormat), PlantUmlImageFormat.Png));
        Assert.True(Enum.IsDefined(typeof(PlantUmlImageFormat), PlantUmlImageFormat.Svg));
    }

    [Fact]
    public void Image_tags_use_svg_url_when_server_url_contains_svg()
    {
        var logs = new[] { MakeRequest() };
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs, plantUmlServerRendererUrl: "https://plantuml.com/plantuml/svg").ToList();
        var imageTag = results.Single().ImageTags.First();

        Assert.Contains("/svg/", imageTag);
        Assert.DoesNotContain("/png/", imageTag);
    }

    // ─── Binary content detection ───────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("{\"key\": \"value\"}", false)]
    [InlineData("plain text content", false)]
    [InlineData("line1\nline2\nline3", false)]
    [InlineData("tabs\tand\tnewlines\n", false)]
    public void IsBinaryContent_returns_false_for_text_content(string? input, bool expected)
    {
        Assert.Equal(expected, PlantUmlCreator.IsBinaryContent(input));
    }

    [Fact]
    public void IsBinaryContent_returns_true_for_binary_content()
    {
        // Simulate garbled gzip content read as string (lots of control chars)
        var binary = "\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008" +
                     "\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015" +
                     "some text mixed in but mostly binary" +
                     "\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f";
        Assert.True(PlantUmlCreator.IsBinaryContent(binary));
    }

    [Fact]
    public void Binary_request_body_shows_placeholder_in_plantuml_note()
    {
        // Simulate binary/compressed content that would come from gzip
        var binaryContent = new string('\u0001', 100);
        var logs = new[]
        {
            MakeRequest(content: binaryContent),
            MakeResponse(),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("[binary content]", plantUml);
        // Verify the note section contains the placeholder, not raw binary
        var noteStart = plantUml.IndexOf("note left");
        Assert.True(noteStart >= 0, "Expected 'note left' in PlantUML output");
        var noteEnd = plantUml.IndexOf("end note", noteStart);
        var noteBody = plantUml.Substring(noteStart, noteEnd - noteStart);
        Assert.Contains("<i>[binary content]</i>", noteBody);
    }

    // ─── Backslash escaping in notes ────────────────────────────

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("no escapes", "no escapes")]
    [InlineData("{\"key\":\"\\u0022value\\u0022\"}", "{\"key\":\"\\\\u0022value\\\\u0022\"}")]
    [InlineData("line1\\nline2", "line1\\\\nline2")]
    [InlineData("already\\\\escaped", "already\\\\\\\\escaped")]
    public void EscapeForPlantUmlNote_escapes_backslashes(string input, string expected)
    {
        Assert.Equal(expected, PlantUmlCreator.EscapeForPlantUmlNote(input));
    }

    [Fact]
    public void Request_note_with_backslash_content_is_escaped_in_plantuml()
    {
        var json = """{"data":"\\u0022value\\u0022"}""";
        var logs = new[]
        {
            MakeRequest(content: json),
            MakeResponse(),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("\\\\u0022", plantUml);
        Assert.DoesNotContain("\\u0022", plantUml.Replace("\\\\u0022", ""));
    }

    [Fact]
    public void Json_with_backtick_renders_literal_backtick_not_unicode_escape()
    {
        var json = """{"query":"SELECT * FROM `my_table`"}""";
        var logs = new[]
        {
            MakeRequest(content: json),
            MakeResponse(),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).ToList();
        var plantUml = results.Single().PlantUmls.First().PlainText;

        Assert.Contains("`my_table`", plantUml);
        Assert.DoesNotContain("\\u0060", plantUml);
    }

    // ─── Dependency coloring ────────────────────────────────────

    private static RequestResponseLog MakeRequestWithCategory(
        string dependencyCategory,
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "WebApp",
        string method = "GET",
        string uri = "http://example.com/api/orders")
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: HttpMethod.Parse(method),
            Content: null,
            Uri: new Uri(uri),
            Headers: [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            DependencyCategory: dependencyCategory);
    }

    private static RequestResponseLog MakeResponseWithCategory(
        string dependencyCategory,
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "WebApp",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri("http://example.com/api/orders"),
            Headers: [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: statusCode,
            DependencyCategory: dependencyCategory);
    }

    private static string GetPlantUmlWithOptions(
        IEnumerable<RequestResponseLog> logs,
        bool sequenceDiagramArrowColors = true,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null)
    {
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs,
            sequenceDiagramArrowColors: sequenceDiagramArrowColors,
            sequenceDiagramParticipantColors: sequenceDiagramParticipantColors,
            dependencyColors: dependencyColors,
            serviceTypeOverrides: serviceTypeOverrides).ToList();
        return results.Single().PlantUmls.First().PlainText;
    }

    [Fact]
    public void CosmosDB_service_uses_database_shape()
    {
        var logs = new[] { MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("database \"Cosmos DB\" as cosmosDB", plantUml);
    }

    [Fact]
    public void Redis_service_uses_collections_shape()
    {
        var logs = new[] { MakeRequestWithCategory("Redis", serviceName: "Redis Cache") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("collections \"Redis Cache\" as redisCache", plantUml);
    }

    [Fact]
    public void ServiceBus_service_uses_queue_shape()
    {
        var logs = new[] { MakeRequestWithCategory("ServiceBus", serviceName: "Service Bus") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("queue \"Service Bus\" as serviceBus", plantUml);
    }

    [Fact]
    public void BlobStorage_service_uses_database_shape()
    {
        var logs = new[] { MakeRequestWithCategory("BlobStorage", serviceName: "Blob Storage") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("database \"Blob Storage\" as blobStorage", plantUml);
    }

    [Fact]
    public void Spanner_service_uses_database_shape()
    {
        var logs = new[] { MakeRequestWithCategory("Spanner", serviceName: "Cloud Spanner") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("database \"Cloud Spanner\" as cloudSpanner", plantUml);
    }

    [Fact]
    public void Bigtable_service_uses_database_shape()
    {
        var logs = new[] { MakeRequestWithCategory("Bigtable", serviceName: "Cloud Bigtable") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("database \"Cloud Bigtable\" as cloudBigtable", plantUml);
    }

    [Fact]
    public void HTTP_service_uses_entity_shape()
    {
        var logs = new[] { MakeRequestWithCategory("HTTP", serviceName: "Payment API") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("entity \"Payment API\" as paymentAPI", plantUml);
    }

    [Fact]
    public void Null_category_defaults_to_entity_shape()
    {
        var logs = new[] { MakeRequest(serviceName: "Unknown Service") };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("entity \"Unknown Service\" as unknownService", plantUml);
    }

    [Fact]
    public void Database_arrows_use_red_color()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB"),
            MakeResponseWithCategory("CosmosDB", serviceName: "Cosmos DB"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("webApp -[#E74C3C]> cosmosDB:", plantUml);
        Assert.Contains("cosmosDB -[#E74C3C]-> webApp:", plantUml);
    }

    [Fact]
    public void Cache_arrows_use_orange_color()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("Redis", serviceName: "Redis Cache"),
            MakeResponseWithCategory("Redis", serviceName: "Redis Cache"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("-[#F39C12]>", plantUml);
        Assert.Contains("-[#F39C12]->", plantUml);
    }

    [Fact]
    public void Queue_arrows_use_purple_color()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("ServiceBus", serviceName: "Service Bus"),
            MakeResponseWithCategory("ServiceBus", serviceName: "Service Bus"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("-[#9B59B6]>", plantUml);
        Assert.Contains("-[#9B59B6]->", plantUml);
    }

    [Fact]
    public void Storage_arrows_use_green_color()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("BlobStorage", serviceName: "Blob Storage"),
            MakeResponseWithCategory("BlobStorage", serviceName: "Blob Storage"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("-[#2ECC71]>", plantUml);
        Assert.Contains("-[#2ECC71]->", plantUml);
    }

    [Fact]
    public void Arrow_colors_disabled_produces_plain_arrows()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB"),
            MakeResponseWithCategory("CosmosDB", serviceName: "Cosmos DB"),
        };
        var plantUml = GetPlantUmlWithOptions(logs, sequenceDiagramArrowColors: false);

        Assert.Contains("webApp -> cosmosDB:", plantUml);
        Assert.Contains("cosmosDB --> webApp:", plantUml);
        Assert.DoesNotContain("[#", plantUml);
    }

    [Fact]
    public void Participant_colors_enabled_adds_color_to_entity_declaration()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB"),
        };
        var plantUml = GetPlantUmlWithOptions(logs, sequenceDiagramParticipantColors: true);

        Assert.Contains("database \"Cosmos DB\" as cosmosDB #E74C3C", plantUml);
    }

    [Fact]
    public void Participant_colors_disabled_by_default_no_color_on_entity()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("database \"Cosmos DB\" as cosmosDB", plantUml);
        Assert.DoesNotContain("cosmosDB #", plantUml);
    }

    [Fact]
    public void Both_options_off_produces_original_behavior()
    {
        var logs = new[]
        {
            MakeRequest(serviceName: "OrderService"),
            MakeResponse(serviceName: "OrderService"),
        };
        var plantUml = GetPlantUmlWithOptions(logs,
            sequenceDiagramArrowColors: false,
            sequenceDiagramParticipantColors: false);

        Assert.Contains("entity \"OrderService\" as orderService", plantUml);
        Assert.Contains("webApp -> orderService:", plantUml);
        Assert.Contains("orderService --> webApp:", plantUml);
        Assert.DoesNotContain("[#", plantUml);
    }

    [Fact]
    public void Custom_dependency_color_override_is_used()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB"),
        };
        var plantUml = GetPlantUmlWithOptions(logs,
            dependencyColors: new Dictionary<string, string> { ["CosmosDB"] = "#FF0000" });

        Assert.Contains("-[#FF0000]>", plantUml);
        Assert.DoesNotContain("#E74C3C", plantUml);
    }

    [Fact]
    public void Service_type_override_changes_shape_and_color()
    {
        var logs = new[]
        {
            MakeRequest(serviceName: "My Gateway"),
            MakeResponse(serviceName: "My Gateway"),
        };
        var plantUml = GetPlantUmlWithOptions(logs,
            serviceTypeOverrides: new Dictionary<string, string> { ["My Gateway"] = "Redis" });

        Assert.Contains("collections \"My Gateway\" as myGateway", plantUml);
        Assert.Contains("-[#F39C12]>", plantUml);
    }

    [Fact]
    public void Mixed_dependency_types_use_correct_shapes_and_colors()
    {
        var logs = new[]
        {
            MakeRequestWithCategory("CosmosDB", serviceName: "Cosmos DB"),
            MakeResponseWithCategory("CosmosDB", serviceName: "Cosmos DB"),
            MakeRequestWithCategory("Redis", serviceName: "Redis Cache"),
            MakeResponseWithCategory("Redis", serviceName: "Redis Cache"),
            MakeRequestWithCategory("ServiceBus", serviceName: "Service Bus"),
            MakeResponseWithCategory("ServiceBus", serviceName: "Service Bus"),
            MakeRequest(serviceName: "Payment API"),
            MakeResponse(serviceName: "Payment API"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("actor \"WebApp\" as webApp", plantUml);
        Assert.Contains("database \"Cosmos DB\" as cosmosDB", plantUml);
        Assert.Contains("collections \"Redis Cache\" as redisCache", plantUml);
        Assert.Contains("queue \"Service Bus\" as serviceBus", plantUml);
        Assert.Contains("entity \"Payment API\" as paymentAPI", plantUml);

        Assert.Contains("-[#E74C3C]>", plantUml); // Cosmos
        Assert.Contains("-[#F39C12]>", plantUml); // Redis
        Assert.Contains("-[#9B59B6]>", plantUml); // ServiceBus
        Assert.Contains("-[#438DD5]>", plantUml); // HTTP
    }

    // ─── PhaseVariant rendering ─────────────────────────────────

    private static PhaseVariant MakeVariant(
        string method = "Op",
        string uri = "http://example.com/api",
        string? content = null,
        bool skip = false)
        => new(method, new Uri(uri), content, [], skip);

    private static PhaseVariant MakeHttpVariant(
        HttpMethod? method = null,
        string uri = "http://example.com/api",
        string? content = null,
        bool skip = false)
        => new(method ?? HttpMethod.Get, new Uri(uri), content, [], skip);

    [Fact]
    public void ActionVariant_used_when_no_IsActionStart_present()
    {
        var request = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/default");
        request.ActionVariant = MakeVariant("ActionOp", "http://example.com/api/action-variant", "action content");
        var response = MakeResponse(callerName: "User", serviceName: "Api");
        response.ActionVariant = MakeHttpVariant(uri: "http://example.com/api/action-variant", content: "action response");

        var plantUml = GetPlantUml([request, response]);

        Assert.Contains("/api/action-variant", plantUml);
        Assert.DoesNotContain("/api/default", plantUml);
    }

    [Fact]
    public void SetupVariant_used_before_IsActionStart_and_ActionVariant_after()
    {
        var setupRequest = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/default");
        setupRequest.SetupVariant = MakeVariant("SetupOp", "http://example.com/api/setup-variant");
        setupRequest.ActionVariant = MakeVariant("ActionOp", "http://example.com/api/action-variant");
        var setupResponse = MakeResponse(callerName: "User", serviceName: "Api");
        setupResponse.SetupVariant = MakeHttpVariant(uri: "http://example.com/api/setup-variant");
        setupResponse.ActionVariant = MakeHttpVariant(uri: "http://example.com/api/action-variant");

        var actionStart = MakeActionStart();

        var actionRequest = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/default");
        actionRequest.SetupVariant = MakeVariant("SetupOp", "http://example.com/api/setup-variant");
        actionRequest.ActionVariant = MakeVariant("ActionOp", "http://example.com/api/action-variant");
        var actionResponse = MakeResponse(callerName: "User", serviceName: "Api");
        actionResponse.SetupVariant = MakeHttpVariant(uri: "http://example.com/api/setup-variant");
        actionResponse.ActionVariant = MakeHttpVariant(uri: "http://example.com/api/action-variant");

        var plantUml = GetPlantUml([setupRequest, setupResponse, actionStart, actionRequest, actionResponse], separateSetup: true);

        Assert.Contains("/api/setup-variant", plantUml);
        Assert.Contains("/api/action-variant", plantUml);
        Assert.DoesNotContain("/api/default", plantUml);
    }

    [Fact]
    public void Skip_flag_on_ActionVariant_omits_entry_when_no_IsActionStart()
    {
        var request = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/default");
        request.ActionVariant = MakeVariant(uri: "http://example.com/api/skipped", skip: true);
        var response = MakeResponse(callerName: "User", serviceName: "Api");
        response.ActionVariant = MakeHttpVariant(uri: "http://example.com/api/skipped", skip: true);

        var plantUml = GetPlantUml([request, response]);

        Assert.DoesNotContain("/api/skipped", plantUml);
        Assert.DoesNotContain("/api/default", plantUml);
    }

    [Fact]
    public void Skip_flag_on_SetupVariant_omits_entry_before_IsActionStart()
    {
        var skippedRequest = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/setup-default");
        skippedRequest.SetupVariant = MakeVariant(uri: "http://example.com/api/setup", skip: true);
        skippedRequest.ActionVariant = MakeVariant(uri: "http://example.com/api/action");
        var skippedResponse = MakeResponse(callerName: "User", serviceName: "Api");
        skippedResponse.SetupVariant = MakeHttpVariant(uri: "http://example.com/api/setup", skip: true);
        skippedResponse.ActionVariant = MakeHttpVariant(uri: "http://example.com/api/action");

        var actionStart = MakeActionStart();

        var actionRequest = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/action-default");
        actionRequest.SetupVariant = MakeVariant(uri: "http://example.com/api/setup", skip: true);
        actionRequest.ActionVariant = MakeVariant(uri: "http://example.com/api/action");
        var actionResponse = MakeResponse(callerName: "User", serviceName: "Api");
        actionResponse.SetupVariant = MakeHttpVariant(uri: "http://example.com/api/setup", skip: true);
        actionResponse.ActionVariant = MakeHttpVariant(uri: "http://example.com/api/action");

        var plantUml = GetPlantUml([skippedRequest, skippedResponse, actionStart, actionRequest, actionResponse], separateSetup: true);

        Assert.DoesNotContain("/api/setup", plantUml);
        Assert.Contains("/api/action", plantUml);
    }

    [Fact]
    public void No_variants_uses_primary_fields_unchanged()
    {
        var request = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/primary");
        var response = MakeResponse(callerName: "User", serviceName: "Api");

        var plantUml = GetPlantUml([request, response]);

        Assert.Contains("/api/primary", plantUml);
    }

    [Fact]
    public void ActionVariant_content_shown_in_note()
    {
        var request = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/default", content: "default body");
        request.ActionVariant = MakeVariant("ActionOp", "http://example.com/api/action", "action body content");
        var response = MakeResponse(callerName: "User", serviceName: "Api");

        var plantUml = GetPlantUml([request, response]);

        Assert.Contains("action body content", plantUml);
        Assert.DoesNotContain("default body", plantUml);
    }

    [Fact]
    public void Variant_method_used_in_request_label()
    {
        var request = MakeRequest(callerName: "User", serviceName: "Api", uri: "http://example.com/api/default", method: "GET");
        request.ActionVariant = MakeVariant("Select", "http://example.com/api/action-uri");
        var response = MakeResponse(callerName: "User", serviceName: "Api");

        var plantUml = GetPlantUml([request, response]);

        Assert.Contains("Select: /api/action-uri", plantUml);
        Assert.DoesNotContain("GET: /api/default", plantUml);
    }

    // ─── CallerDependencyCategory rendering ─────────────────────

    private static RequestResponseLog MakeRequestWithCallerCategory(
        string callerDependencyCategory,
        string? dependencyCategory = null,
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "Kafka Broker")
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: "Consume (Kafka)",
            Content: null,
            Uri: new Uri("kafka:///orders"),
            Headers: [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            DependencyCategory: dependencyCategory,
            CallerDependencyCategory: callerDependencyCategory);
    }

    private static RequestResponseLog MakeResponseWithCallerCategory(
        string callerDependencyCategory,
        string? dependencyCategory = null,
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "Kafka Broker",
        string statusCode = "Ack")
    {
        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: "Consume (Kafka)",
            Content: null,
            Uri: new Uri("kafka:///orders"),
            Headers: [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: statusCode,
            DependencyCategory: dependencyCategory,
            CallerDependencyCategory: callerDependencyCategory);
    }

    [Fact]
    public void Caller_with_CallerDependencyCategory_MessageQueue_renders_as_queue()
    {
        var logs = new[]
        {
            MakeRequestWithCallerCategory("MessageQueue", callerName: "Kafka Broker", serviceName: "Breakfast Provider"),
            MakeResponseWithCallerCategory("MessageQueue", callerName: "Kafka Broker", serviceName: "Breakfast Provider"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("queue \"Kafka Broker\" as kafkaBroker", plantUml);
        Assert.Contains("entity \"Breakfast Provider\" as breakfastProvider", plantUml);
    }

    [Fact]
    public void Caller_without_CallerDependencyCategory_renders_as_actor_or_entity()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "WebApp", serviceName: "Kafka Broker"),
            MakeResponse(callerName: "WebApp", serviceName: "Kafka Broker"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("actor \"WebApp\" as webApp", plantUml);
    }

    [Fact]
    public void CallerDependencyCategory_does_not_affect_ServiceName_shape()
    {
        var logs = new[]
        {
            MakeRequestWithCallerCategory("MessageQueue",
                dependencyCategory: null,
                callerName: "Kafka Broker",
                serviceName: "Breakfast Provider"),
            MakeResponseWithCallerCategory("MessageQueue",
                dependencyCategory: null,
                callerName: "Kafka Broker",
                serviceName: "Breakfast Provider"),
        };
        var plantUml = GetPlantUml(logs);

        // ServiceName with null DependencyCategory should remain entity
        Assert.Contains("entity \"Breakfast Provider\" as breakfastProvider", plantUml);
        // Caller should be queue
        Assert.Contains("queue \"Kafka Broker\" as kafkaBroker", plantUml);
    }

    [Fact]
    public void CallerDependencyCategory_with_participant_colors_adds_color()
    {
        var logs = new[]
        {
            MakeRequestWithCallerCategory("MessageQueue", callerName: "Kafka Broker", serviceName: "Consumer Svc"),
            MakeResponseWithCallerCategory("MessageQueue", callerName: "Kafka Broker", serviceName: "Consumer Svc"),
        };
        var plantUml = GetPlantUmlWithOptions(logs, sequenceDiagramParticipantColors: true);

        Assert.Contains("queue \"Kafka Broker\" as kafkaBroker #9B59B6", plantUml);
    }

    [Fact]
    public void CallerDependencyCategory_affects_arrow_color()
    {
        var logs = new[]
        {
            MakeRequestWithCallerCategory("MessageQueue", callerName: "Kafka Broker", serviceName: "Consumer Svc"),
            MakeResponseWithCallerCategory("MessageQueue", callerName: "Kafka Broker", serviceName: "Consumer Svc"),
        };
        var plantUml = GetPlantUmlWithOptions(logs, sequenceDiagramArrowColors: true);

        Assert.Contains("-[#9B59B6]>", plantUml);
    }

    [Fact]
    public void Consume_and_produce_pattern_renders_correct_shapes()
    {
        // Simulate: consume from Kafka (broker→SUT) + produce to Kafka (SUT→broker) + DB insert
        var consumeId = Guid.NewGuid();
        var produceId = Guid.NewGuid();
        var dbId = Guid.NewGuid();
        var logs = new[]
        {
            // Consume: Kafka Broker → Breakfast Provider (no DependencyCategory on SUT, CallerDependencyCategory on broker)
            new RequestResponseLog("T", "t1", "Consume (Kafka)", "{}", new Uri("kafka:///recipe_logs"), [],
                "Breakfast Provider", "Kafka Broker", RequestResponseType.Request, Guid.NewGuid(), consumeId, false,
                CallerDependencyCategory: "MessageQueue"),
            new RequestResponseLog("T", "t1", "Consume (Kafka)", "", new Uri("kafka:///recipe_logs"), [],
                "Breakfast Provider", "Kafka Broker", RequestResponseType.Response, Guid.NewGuid(), consumeId, false,
                StatusCode: "Ack", CallerDependencyCategory: "MessageQueue"),
            // DB insert: Breakfast Provider → Reporting DB
            new RequestResponseLog("T", "t1", "Insert", "{}", new Uri("http://db/RecipeReports"), [],
                "Reporting DB", "Breakfast Provider", RequestResponseType.Request, Guid.NewGuid(), dbId, false,
                DependencyCategory: "Database"),
            new RequestResponseLog("T", "t1", "Insert", "", new Uri("http://db/RecipeReports"), [],
                "Reporting DB", "Breakfast Provider", RequestResponseType.Response, Guid.NewGuid(), dbId, false,
                StatusCode: HttpStatusCode.OK, DependencyCategory: "Database"),
            // Produce: Breakfast Provider → Kafka Broker
            new RequestResponseLog("T", "t1", "Produce", "{}", new Uri("kafka:///recipe_logs"), [],
                "Kafka Broker", "Breakfast Provider", RequestResponseType.Request, Guid.NewGuid(), produceId, false,
                DependencyCategory: "MessageQueue"),
            new RequestResponseLog("T", "t1", "Produce", "", new Uri("kafka:///recipe_logs"), [],
                "Kafka Broker", "Breakfast Provider", RequestResponseType.Response, Guid.NewGuid(), produceId, false,
                StatusCode: "Responded", DependencyCategory: "MessageQueue"),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("queue \"Kafka Broker\" as kafkaBroker", plantUml);
        Assert.Contains("entity \"Breakfast Provider\" as breakfastProvider", plantUml);
        Assert.Contains("database \"Reporting DB\" as reportingDB", plantUml);
    }

    [Fact]
    public void Consume_event_note_renders_on_right_side()
    {
        var consumeId = Guid.NewGuid();
        var logs = new[]
        {
            new RequestResponseLog("T", "t1", "Consume (Kafka)", "{\"OrderId\":\"123\"}", new Uri("kafka:///orders"), [],
                "Consumer Svc", "Kafka Broker", RequestResponseType.Request, Guid.NewGuid(), consumeId, false,
                CallerDependencyCategory: "MessageQueue", MetaType: RequestResponseMetaType.Event)
            { NoteOnRight = true },
            new RequestResponseLog("T", "t1", "Consume (Kafka)", "", new Uri("kafka:///orders"), [],
                "Consumer Svc", "Kafka Broker", RequestResponseType.Response, Guid.NewGuid(), consumeId, false,
                StatusCode: "Ack", CallerDependencyCategory: "MessageQueue", MetaType: RequestResponseMetaType.Event),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note<<eventNote>> right", plantUml);
        Assert.DoesNotContain("note<<eventNote>> left", plantUml);
    }

    [Fact]
    public void Send_event_note_renders_on_left_side()
    {
        var sendId = Guid.NewGuid();
        var logs = new[]
        {
            new RequestResponseLog("T", "t1", "Produce", "{\"OrderId\":\"123\"}", new Uri("kafka:///orders"), [],
                "Kafka Broker", "My Svc", RequestResponseType.Request, Guid.NewGuid(), sendId, false,
                DependencyCategory: "MessageQueue", MetaType: RequestResponseMetaType.Event),
            new RequestResponseLog("T", "t1", "Produce", "", new Uri("kafka:///orders"), [],
                "Kafka Broker", "My Svc", RequestResponseType.Response, Guid.NewGuid(), sendId, false,
                StatusCode: "Responded", DependencyCategory: "MessageQueue", MetaType: RequestResponseMetaType.Event),
        };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("note<<eventNote>> left", plantUml);
        Assert.DoesNotContain("note<<eventNote>> right", plantUml);
    }

    // ─── Client-side splitting ────────────────────────────────────────

    [Fact]
    public void ClientSideSplitting_produces_single_diagram_for_tall_content()
    {
        // Same scenario as Many_medium_sized_notes_split_on_estimated_height
        // but with clientSideSplitting=true — should NOT split server-side
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 15; i++)
        {
            var body = string.Join("\n", Enumerable.Range(0, 25).Select(n => $"\"field_{n}\": \"value_{n}\""));
            logs.Add(MakeRequest(content: $"{{{body}}}"));
            logs.Add(MakeResponse(content: $"{{{body}}}"));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000, clientSideSplitting: true).ToList();
        var diagramCount = results.Single().PlantUmls.Count();

        Assert.Equal(1, diagramCount);
    }

    [Fact]
    public void ClientSideSplitting_does_not_chunk_large_response_bodies()
    {
        // Create a response with >15000 chars — should NOT be chunked when clientSideSplitting=true
        var largeBody = string.Join("\n", Enumerable.Range(0, 500).Select(n => $"\"field_{n}\": \"{new string('x', 30)}\""));
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000, clientSideSplitting: true).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        // Should be exactly 1 diagram (no chunking split)
        Assert.Single(diagrams);
        // Should NOT contain continuation markers
        Assert.DoesNotContain("Continued On Next Diagram", diagrams[0].PlainText);
        Assert.DoesNotContain("Continued From Previous Diagram", diagrams[0].PlainText);
    }

    [Fact]
    public void ClientSideSplitting_still_produces_valid_plantuml()
    {
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 15; i++)
        {
            var body = string.Join("\n", Enumerable.Range(0, 25).Select(n => $"\"f_{n}\": \"v_{n}\""));
            logs.Add(MakeRequest(content: $"{{{body}}}"));
            logs.Add(MakeResponse(content: $"{{{body}}}"));
        }

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000, clientSideSplitting: true).ToList();
        var diagram = results.Single().PlantUmls.Single();

        Assert.Contains("@startuml", diagram.PlainText);
        Assert.Contains("@enduml", diagram.PlainText);
        Assert.Contains("autonumber", diagram.PlainText);
    }

    [Fact]
    public void Without_clientSideSplitting_large_response_bodies_are_chunked()
    {
        // Verify that the normal path still chunks (regression guard)
        var largeBody = string.Join("\n", Enumerable.Range(0, 500).Select(n => $"\"field_{n}\": \"{new string('x', 30)}\""));
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse(content: largeBody),
        };

        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000, clientSideSplitting: false).ToList();
        var diagrams = results.Single().PlantUmls.ToList();

        Assert.True(diagrams.Count >= 2, $"Expected chunking to produce multiple diagrams but got {diagrams.Count}");
        Assert.Contains("Continued On Next Diagram", diagrams[0].PlainText);
    }

    // ─── Markers-only diagrams ───────────────────────────────────

    [Fact]
    public void Markers_only_diagram_declares_a_placeholder_participant_so_notes_have_a_lifeline_to_span()
    {
        // A test that asserted but never touched a tracked dependency: nothing but injected notes.
        // `hnote across` with no participant is a PlantUML syntax error in every real engine.
        var logs = new[]
        {
            MakeOverrideStart(plantUml: "hnote across <<assertionNote>> #d4edda\n✓ the mock answers 200\nend note"),
            MakeOverrideEnd(),
        };

        var plantUml = GetPlantUml(logs);

        Assert.Contains(PlantUmlCreator.MarkerOnlyParticipant, plantUml);
        Assert.Contains("✓ the mock answers 200", plantUml);
        // The participant comes before the note it anchors.
        Assert.True(plantUml.IndexOf(PlantUmlCreator.MarkerOnlyParticipant, StringComparison.Ordinal)
                    < plantUml.IndexOf("hnote across", StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholder_participant_is_not_declared_when_the_diagram_has_real_interactions()
    {
        var logs = new[]
        {
            MakeOverrideStart(plantUml: "hnote across <<assertionNote>> #d4edda\n✓ ok\nend note"),
            MakeOverrideEnd(),
            MakeRequest(),
            MakeResponse(),
        };

        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain(PlantUmlCreator.MarkerOnlyParticipant, plantUml);
        Assert.Contains("actor \"WebApp\"", plantUml);
    }

    [Fact]
    public void Placeholder_participant_is_not_declared_when_there_are_no_markers_either()
    {
        var logs = new[] { MakeOverrideStart(plantUml: null), MakeOverrideEnd(plantUml: null) };

        var plantUml = GetPlantUml(logs);

        Assert.DoesNotContain(PlantUmlCreator.MarkerOnlyParticipant, plantUml);
    }
    // ─── Truncated JSON bodies and unbreakable lines ────────────
    // A body cut by a capture cap is not parseable JSON, and a 65 KB minified payload on one note line
    // is a 400,000 px wide diagram plantuml.js refuses ("Diagram too large for browser rendering").

    [Fact]
    public void Truncated_json_response_with_resp_marker_is_reindented_and_keeps_the_marker_on_its_own_line()
    {
        var truncated = """{"Instance":[{"ReportDate":"2026-08-17T00:00:00","UniqueCustomers":501,"Nested":{"a":1,"b":[1,2""" +
                        " …[bulk string truncated: 486,330 bytes on the wire, 65,408 kept]";
        var logs = new[] { MakeRequest(), MakeResponse(content: truncated) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"UniqueCustomers\": 501," + Nl, plantUml.Replace("\r\n", "\n").Replace("\n", Nl));
        Assert.Contains("…[bulk string truncated: 486,330 bytes on the wire, 65,408 kept]", plantUml);
        var lines = plantUml.Split('\n');
        Assert.Contains(lines, l => l.Trim() == "…[bulk string truncated: 486,330 bytes on the wire, 65,408 kept]");
        Assert.All(lines, l => Assert.True(l.Length <= 200, $"line too long: {l.Length}"));
    }

    [Fact]
    public void Truncated_json_response_with_http_cap_marker_is_reindented()
    {
        var truncated = """{"jobReference":{"jobId":"job_1","projectId":"p"},"rows":[{"f":[{"v":"1"},{"v":"2""" +
                        "\n\n…truncated (90000 chars total)";
        var logs = new[] { MakeRequest(), MakeResponse(content: truncated) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains("\"jobId\": \"job_1\"", plantUml);
        Assert.Contains("…truncated (90000 chars total)", plantUml);
        Assert.DoesNotContain("\"jobReference\":{", plantUml); // the one-line raw form is gone
    }

    [Fact]
    public void Non_json_body_starting_with_a_brace_still_takes_the_plain_text_path()
    {
        var logs = new[] { MakeRequest(), MakeResponse(content: "{not valid json at all") };
        Assert.Contains("{not valid json at all", GetPlantUml(logs));
    }

    [Fact]
    public void Long_unbreakable_body_line_is_wrapped_below_the_run_limit()
    {
        var blob = new string('x', 5000); // no spaces anywhere: PlantUML's wrapWidth could never break it
        var logs = new[] { MakeRequest(), MakeResponse(content: blob) };
        var plantUml = GetPlantUml(logs);

        Assert.Contains(blob[..PlantUmlCreator.MaxUnbrokenRunChars], plantUml);
        var noteLines = plantUml.Split('\n').Where(l => l.Trim().All(c => c == 'x') && l.Trim().Length > 0).ToList();
        Assert.True(noteLines.Count >= 5000 / PlantUmlCreator.MaxUnbrokenRunChars);
        Assert.All(noteLines, l => Assert.True(l.Trim().Length <= PlantUmlCreator.MaxUnbrokenRunChars, $"run of {l.Trim().Length}"));
        Assert.Equal(5000, noteLines.Sum(l => l.Trim().Length));
    }

    [Fact]
    public void ReindentJsonPrefix_indents_by_structure_and_leaves_strings_alone()
    {
        var json = """{"a":"x{y}[z],:","b":{},"c":[],"d":[1,{"e":"q\"u"}""";
        var expected = "{\n  \"a\": \"x{y}[z],:\",\n  \"b\": {},\n  \"c\": [],\n  \"d\": [\n    1,\n    {\n      \"e\": \"q\\\"u\"\n    }";
        Assert.Equal(expected, PlantUmlCreator.ReindentJsonPrefix(json));
    }

    [Theory]
    [InlineData("""{"a":1,"b":[1,2""", true)]
    [InlineData("""{"a":1}""", true)]
    [InlineData("""[{"a":"unterminated str""", true)]
    [InlineData("{not valid json at all", false)]
    [InlineData("[not valid json at all", false)]
    public void IsJsonPrefix_accepts_any_cut_of_valid_json_and_rejects_invalid_tokens(string text, bool expected)
    {
        Assert.Equal(expected, PlantUmlCreator.IsJsonPrefix(text));
    }

    [Fact]
    public void WrapUnbreakableRuns_prefers_punctuation_and_never_cuts_inside_a_tag()
    {
        var run = string.Concat(Enumerable.Range(0, 40).Select(i => $"k{i:00}:v{i:00},")); // 360 chars, no spaces, commas every 9
        var wrapped = PlantUmlCreator.WrapUnbreakableRuns(run);
        var pieces = wrapped.Split('\n');
        Assert.True(pieces.Length >= 3);
        Assert.All(pieces, piece => Assert.True(piece.Length <= PlantUmlCreator.MaxUnbrokenRunChars));
        Assert.All(pieces[..^1], piece => Assert.EndsWith(",", piece)); // cut at a comma, not mid-token
        Assert.Equal(run, wrapped.Replace("\n", ""));

        var tagged = new string('a', 110) + "<color:gray>" + new string('b', 110);
        var wrappedTag = PlantUmlCreator.WrapUnbreakableRuns(tagged);
        Assert.Contains("<color:gray>", wrappedTag); // the tag survived intact
        Assert.DoesNotContain("<color:\ngray>", wrappedTag);
        Assert.Equal(tagged, wrappedTag.Replace("\n", ""));

        var shortText = "fits on one line";
        Assert.Same(shortText, PlantUmlCreator.WrapUnbreakableRuns(shortText));
    }
}
