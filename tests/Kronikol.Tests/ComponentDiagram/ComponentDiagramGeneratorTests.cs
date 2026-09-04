using System.Net;
using Kronikol.ComponentDiagram;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.ComponentDiagram;

public class ComponentDiagramGeneratorTests
{
    // ─── Helpers ────────────────────────────────────────────────

    private static RequestResponseLog MakeRequest(
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "Caller",
        string method = "GET",
        string uri = "http://example.com/api/orders",
        bool trackingIgnore = false,
        RequestResponseMetaType metaType = RequestResponseMetaType.Default,
        string? dependencyCategory = null)
    {
        OneOf<HttpMethod, string> parsedMethod = metaType == RequestResponseMetaType.Event
            ? method
            : HttpMethod.Parse(method);

        return new RequestResponseLog(
            TestName: testName,
            TestId: testId,
            Method: parsedMethod,
            Content: null,
            Uri: new Uri(uri),
            Headers: [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: trackingIgnore,
            MetaType: metaType,
            DependencyCategory: dependencyCategory);
    }

    private static RequestResponseLog MakeResponse(
        string testId = "test-1",
        string testName = "My Test",
        string serviceName = "OrderService",
        string callerName = "Caller",
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
            StatusCode: statusCode);
    }

    private static RequestResponseLog MakeOverrideStart(string testId = "test-1")
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
            IsOverrideStart = true
        };
    }

    private static RequestResponseLog MakeOverrideEnd(string testId = "test-1")
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
            IsOverrideEnd = true
        };
    }

    private static RequestResponseLog MakeActionStart(string testId = "test-1")
    {
        return new RequestResponseLog(
            TestName: testId,
            TestId: testId,
            Method: "",
            Content: "",
            Uri: new Uri("http://action.com"),
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

    // ═══════════════════════════════════════════════════════════
    // ExtractRelationships Tests
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ExtractRelationships_EmptyLogs_ReturnsNoRelationships()
    {
        var result = ComponentDiagramGenerator.ExtractRelationships([]);
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractRelationships_SingleRequest_ExtractsOneRelationship()
    {
        var logs = new[] { MakeRequest() };
        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal("Caller", result[0].Caller);
        Assert.Equal("OrderService", result[0].Service);
        Assert.Equal("HTTP", result[0].Protocol);
        Assert.Contains("GET", result[0].Methods);
        Assert.Equal(1, result[0].CallCount);
        Assert.Equal(1, result[0].TestCount);
    }

    [Fact]
    public void ExtractRelationships_DuplicateCallerService_DeduplicatesRelationship()
    {
        var logs = new[]
        {
            MakeRequest(testId: "test-1"),
            MakeRequest(testId: "test-1")
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal(2, result[0].CallCount);
        Assert.Equal(1, result[0].TestCount);
    }

    [Fact]
    public void ExtractRelationships_SameRelationshipAcrossTests_AccumulatesCountsAndTests()
    {
        var logs = new[]
        {
            MakeRequest(testId: "test-1"),
            MakeRequest(testId: "test-2"),
            MakeRequest(testId: "test-2")
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal(3, result[0].CallCount);
        Assert.Equal(2, result[0].TestCount);
    }

    [Fact]
    public void ExtractRelationships_MultipleProtocols_TrackedSeparately()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "OrderService", serviceName: "PaymentService"),
            MakeRequest(callerName: "OrderService", serviceName: "Kafka", method: "Publish", metaType: RequestResponseMetaType.Event)
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Equal(2, result.Length);
        Assert.Contains(result, r => r.Protocol == "HTTP" && r.Service == "PaymentService");
        Assert.Contains(result, r => r.Protocol == "Publish" && r.Service == "Kafka");
    }

    [Fact]
    public void ExtractRelationships_IgnoredTracking_Excluded()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeRequest(trackingIgnore: true, serviceName: "IgnoredService")
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal("OrderService", result[0].Service);
    }

    [Fact]
    public void ExtractRelationships_OverrideMarkers_Excluded()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeOverrideStart(),
            MakeOverrideEnd()
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal("OrderService", result[0].Service);
    }

    [Fact]
    public void ExtractRelationships_ActionStartMarkers_Excluded()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeActionStart()
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
    }

    [Fact]
    public void ExtractRelationships_ResponseLogs_Excluded()
    {
        var logs = new[]
        {
            MakeRequest(),
            MakeResponse()
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal(1, result[0].CallCount);
    }

    [Fact]
    public void ExtractRelationships_MultipleMethods_AggregatedInMethods()
    {
        var logs = new[]
        {
            MakeRequest(method: "GET"),
            MakeRequest(method: "POST"),
            MakeRequest(method: "GET")
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(result);
        Assert.Equal(2, result[0].Methods.Count);
        Assert.Contains("GET", result[0].Methods);
        Assert.Contains("POST", result[0].Methods);
        Assert.Equal(3, result[0].CallCount);
    }

    [Fact]
    public void ExtractRelationships_ParticipantFilter_ExcludesMatchingParticipants()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "Caller", serviceName: "OrderService"),
            MakeRequest(callerName: "OrderService", serviceName: "InternalHelper")
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs,
            participantFilter: name => name != "InternalHelper");

        Assert.Single(result);
        Assert.Equal("OrderService", result[0].Service);
    }

    [Fact]
    public void ExtractRelationships_ParticipantFilter_ExcludesCallerMatch()
    {
        var logs = new[]
        {
            MakeRequest(callerName: "HiddenCaller", serviceName: "OrderService"),
            MakeRequest(callerName: "Caller", serviceName: "OrderService")
        };

        var result = ComponentDiagramGenerator.ExtractRelationships(logs,
            participantFilter: name => name != "HiddenCaller");

        Assert.Single(result);
        Assert.Equal("Caller", result[0].Caller);
    }

    // ═══════════════════════════════════════════════════════════
    // GeneratePlantUml Tests
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePlantUml_NoRelationships_GeneratesMinimalDiagram()
    {
        var result = ComponentDiagramGenerator.GeneratePlantUml([]);

        Assert.Contains("@startuml", result);
        Assert.Contains("@enduml", result);
        Assert.Contains("Component Diagram", result);
    }

    [Fact]
    public void GeneratePlantUml_SingleRelationship_GeneratesPersonAndSystem()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 5, 3)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("Person(", result);
        Assert.Contains("Caller", result);
        Assert.Contains("System(", result);
        Assert.Contains("OrderService", result);
        Assert.Contains("Rel(", result);
    }

    [Fact]
    public void GeneratePlantUml_MultipleRelationships_AllParticipantsAndRelsGenerated()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET", "POST"], 10, 5),
            new ComponentRelationship("OrderService", "PaymentService", "HTTP", ["POST"], 4, 2)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("Caller", result);
        Assert.Contains("OrderService", result);
        Assert.Contains("PaymentService", result);
        // Should have exactly 2 Rel lines
        var relCount = result.Split("Rel(").Length - 1;
        Assert.Equal(2, relCount);
    }

    [Fact]
    public void GeneratePlantUml_ServiceThatIsAlsoCaller_RenderedAsSystem()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1),
            new ComponentRelationship("OrderService", "PaymentService", "HTTP", ["POST"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        // Caller should be Person (only appears as caller, never as service)
        // OrderService appears as both caller and service — should be System
        var personCount = result.Split("Person(").Length - 1;
        Assert.Equal(1, personCount);

        // OrderService and PaymentService should be System
        var systemCount = result.Split("System(").Length - 1;
        Assert.Equal(2, systemCount);
    }

    [Fact]
    public void GeneratePlantUml_HttpProtocol_ShowsMethodsInLabel()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET", "POST"], 10, 5)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("GET", result);
        Assert.Contains("POST", result);
    }

    [Fact]
    public void GeneratePlantUml_EventProtocol_ShowsProtocolInLabel()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "Kafka", "Publish", ["Publish"], 3, 2)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("Publish", result);
    }

    [Fact]
    public void GeneratePlantUml_CustomTheme_IncludedInOutput()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var options = new ComponentDiagramOptions { PlantUmlTheme = "cerulean" };
        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options);

        Assert.Contains("!theme cerulean", result);
    }

    [Fact]
    public void GeneratePlantUml_NoTheme_NoThemeDirective()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.DoesNotContain("!theme", result);
    }

    [Fact]
    public void GeneratePlantUml_CustomTitle_IncludedInOutput()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var options = new ComponentDiagramOptions { Title = "My Architecture" };
        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options);

        Assert.Contains("My Architecture", result);
    }

    [Fact]
    public void GeneratePlantUml_RelationshipLabelFormatter_Applied()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 5, 3)
        };

        var options = new ComponentDiagramOptions
        {
            RelationshipLabelFormatter = rel => $"Custom: {rel.Protocol} ({rel.CallCount} calls)"
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options);

        Assert.Contains("Custom: HTTP (5 calls)", result);
    }

    [Fact]
    public void GeneratePlantUml_EdgeLabel_IsCappedAtTheStatementLimit()
    {
        // Component diagrams use a different parser from sequence diagrams and its limits are unmeasured,
        // so the cap here is defensive: real labels are two orders of magnitude below it, and an over-long
        // statement costs the whole diagram rather than the one edge.
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 5, 3)
        };

        var options = new ComponentDiagramOptions
        {
            RelationshipLabelFormatter = _ => new string('x', 6000)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options);

        var edge = result.Split('\n').Single(l => l.Contains("xxxx", StringComparison.Ordinal));
        Assert.True(edge.Trim().Length <= PlantUmlStatementLimits.MaxMessageStatementChars, $"{edge.Trim().Length} chars");
        Assert.Contains(PlantUmlStatementLimits.TruncationMarker, edge);
    }

    [Fact]
    public void GeneratePlantUml_CallCountAndTestCount_ShownInLabel()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 14, 8)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("14 calls", result);
        Assert.Contains("8 tests", result);
    }

    [Fact]
    public void GeneratePlantUml_ParticipantAliases_AreSanitized()
    {
        var relationships = new[]
        {
            new ComponentRelationship("My Caller App", "Order-Service.API", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        // Should contain display names in quotes
        Assert.Contains("\"My Caller App\"", result);
        Assert.Contains("\"Order-Service.API\"", result);
        // Should be valid PlantUML
        Assert.Contains("@startuml", result);
        Assert.Contains("@enduml", result);
    }

    [Fact]
    public void GeneratePlantUml_IncludesC4Directives()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("!include <C4/C4_Context>", result);
        Assert.DoesNotContain("C4_Component", result);
    }

    [Fact]
    public void GeneratePlantUml_DoesNotUseRemoteIncludes()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.DoesNotContain("!include http", result);
        Assert.Contains("!include <C4/", result);
    }

    [Fact]
    public void GeneratePlantUml_DefaultLabel_UsesHyphenNotEmDash()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 5, 3)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("HTTP: GET - 5 calls across 3 tests", result);
        Assert.DoesNotContain("\u2014", result); // no em-dash
    }

    [Fact]
    public void GeneratePlantUml_StartsWithStartuml()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.StartsWith("@startuml", result.TrimStart());
    }

    // ═══════════════════════════════════════════════════════════
    // End-to-end: ExtractRelationships → GeneratePlantUml
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void EndToEnd_MultiServiceArchitecture_GeneratesCompleteDiagram()
    {
        var logs = new[]
        {
            MakeRequest(testId: "t1", callerName: "WebApp", serviceName: "OrderService", method: "POST"),
            MakeResponse(testId: "t1", callerName: "WebApp", serviceName: "OrderService"),
            MakeRequest(testId: "t1", callerName: "OrderService", serviceName: "PaymentService", method: "POST"),
            MakeResponse(testId: "t1", callerName: "OrderService", serviceName: "PaymentService"),
            MakeRequest(testId: "t2", callerName: "WebApp", serviceName: "OrderService", method: "GET"),
            MakeResponse(testId: "t2", callerName: "WebApp", serviceName: "OrderService"),
            MakeRequest(testId: "t2", callerName: "OrderService", serviceName: "Kafka", method: "Publish",
                metaType: RequestResponseMetaType.Event),
        };

        var relationships = ComponentDiagramGenerator.ExtractRelationships(logs);
        var puml = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        // WebApp only appears as caller → Person
        Assert.Contains("Person(", puml);
        Assert.Contains("WebApp", puml);

        // OrderService, PaymentService, Kafka → System
        Assert.Contains("OrderService", puml);
        Assert.Contains("PaymentService", puml);
        Assert.Contains("Kafka", puml);

        // 3 distinct relationships
        var relCount = puml.Split("Rel(").Length - 1;
        Assert.Equal(3, relCount);

        // Valid PlantUML wrapper
        Assert.Contains("@startuml", puml);
        Assert.Contains("@enduml", puml);
    }

    // ═══════════════════════════════════════════════════════════
    // GeneratePlantUml with stats — labels, links, styling
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePlantUml_with_stats_includes_percentiles_in_label()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 10, 5)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-Caller-OrderService"] = new(10, 5, 50.0, 45.0, 120.0, 250.0, 5.0, 300.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, stats: stats);

        Assert.Contains("P50: 45ms", result);
        Assert.Contains("P95: 120ms", result);
        Assert.Contains("P99: 250ms", result);
    }

    [Fact]
    public void GeneratePlantUml_with_stats_includes_iflow_link()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 10, 5)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-Caller-OrderService"] = new(10, 5, 50.0, 45.0, 120.0, 250.0, 5.0, 300.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, stats: stats);

        Assert.Contains("[[#iflow-rel-Caller-OrderService", result);
    }

    [Fact]
    public void GeneratePlantUml_without_stats_uses_existing_format()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 5, 3)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("HTTP: GET - 5 calls across 3 tests", result);
        Assert.DoesNotContain("P50:", result);
        Assert.DoesNotContain("[[#iflow-rel-", result);
    }

    [Fact]
    public void GeneratePlantUml_with_stats_includes_error_rate_when_nonzero()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 10, 5)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-Caller-OrderService"] = new(10, 5, 50.0, 45.0, 120.0, 250.0, 5.0, 300.0,
                0.15, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, stats: stats);

        Assert.Contains("15%", result); // error rate shown
    }

    [Fact]
    public void GeneratePlantUml_hotspot_colors_arrows_by_p95()
    {
        var relationships = new[]
        {
            new ComponentRelationship("A", "FastService", "HTTP", ["GET"], 10, 5),
            new ComponentRelationship("A", "SlowService", "HTTP", ["GET"], 10, 5),
            new ComponentRelationship("A", "MediumService", "HTTP", ["GET"], 10, 5)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-A-FastService"] = new(10, 5, 10.0, 10.0, 30.0, 40.0, 5.0, 50.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0),
            ["iflow-rel-A-SlowService"] = new(10, 5, 300.0, 280.0, 500.0, 600.0, 100.0, 700.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0),
            ["iflow-rel-A-MediumService"] = new(10, 5, 100.0, 90.0, 150.0, 180.0, 50.0, 200.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0)
        };
        var options = new ComponentDiagramOptions { ArrowColorMode = ArrowColorMode.Performance };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options, stats);

        // Should contain skinparam or styling for different latency levels
        Assert.Contains("#Green", result);    // Fast (<50ms P95)
        Assert.Contains("#Red", result);      // Slow (>200ms P95)
        Assert.Contains("#Orange", result);   // Medium (50-200ms P95)
    }

    [Fact]
    public void GeneratePlantUml_low_coverage_uses_dashed_line()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "RareService", "HTTP", ["GET"], 1, 1)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-Caller-RareService"] = new(1, 1, 50.0, 50.0, 50.0, 50.0, 50.0, 50.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, true, 0, new Dictionary<string, int>(), null, 0)
        };
        var options = new ComponentDiagramOptions { ArrowColorMode = ArrowColorMode.Performance };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options, stats);

        // Low coverage should use dashed line or warning indicator
        Assert.Contains("..>", result); // dashed arrow in PlantUML
    }

    // ═══════════════════════════════════════════════════════════
    // useC4=false — browser-compatible plain PlantUML output
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePlantUml_UseC4False_DoesNotContainC4Include()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.DoesNotContain("!include", result);
        Assert.DoesNotContain("C4_Context", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_DoesNotContainC4Macros()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.DoesNotContain("Person(", result);
        Assert.DoesNotContain("System(", result);
        Assert.DoesNotContain("Rel(", result);
        Assert.DoesNotContain("$tags=", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_UsesRectangleWithStereotypes()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("<<person>>", result);
        Assert.Contains("<<system>>", result);
        Assert.Contains("rectangle", result);
        Assert.Contains("[Person]", result);
        Assert.Contains("[Software System]", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_IncludesC4SkinparamStyling()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("BackgroundColor #08427B", result);  // Person color
        Assert.Contains("BackgroundColor #438DD5", result);  // System color
        Assert.Contains("RoundCorner 25", result);
        Assert.Contains("skinparam arrow", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_UsesPlainArrowsWithColor()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-Caller-OrderService"] = new(1, 1, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0)
        };
        var options = new ComponentDiagramOptions { ArrowColorMode = ArrowColorMode.Performance };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options, stats, useC4: false);

        Assert.Contains("-[#Green]->", result);
        Assert.DoesNotContain("Rel(", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_PlainArrowWithoutStats()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        // Default ArrowColorMode.DependencyType colors arrows by target type
        Assert.Contains("-[#438DD5]->", result);
        Assert.DoesNotContain("Rel(", result);
    }

    // ═══════════════════════════════════════════════════════════
    // Left-to-right layout
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePlantUml_IncludesLeftToRightDirection()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("left to right direction", result);
    }

    [Fact]
    public void GeneratePlantUml_LeftToRight_AppearsAfterStartuml()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        var startumlIdx = result.IndexOf("@startuml");
        var ltrIdx = result.IndexOf("left to right direction");
        Assert.True(ltrIdx > startumlIdx, "left to right direction must come after @startuml");
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_IncludesLeftToRightDirection()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("left to right direction", result);
    }

    // ═══════════════════════════════════════════════════════════
    // Dependency-Type Shaped Participants
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePlantUml_Database_DependencyCategory_Uses_SystemDb_In_C4()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "CosmosStore", "CosmosDB", ["Upsert"], 5, 3, DependencyCategory: "CosmosDB")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("SystemDb(cosmosStore, \"CosmosStore\")", result);
        Assert.DoesNotContain("System(cosmosStore", result);
    }

    [Fact]
    public void GeneratePlantUml_MessageQueue_DependencyCategory_Uses_SystemQueue_In_C4()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "ServiceBus", "ServiceBus", ["Send"], 2, 1, DependencyCategory: "ServiceBus")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("SystemQueue(serviceBus, \"ServiceBus\")", result);
    }

    [Fact]
    public void GeneratePlantUml_Http_DependencyCategory_Uses_System_In_C4()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "PaymentApi", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("System(paymentApi, \"PaymentApi\")", result);
    }

    [Fact]
    public void GeneratePlantUml_Null_DependencyCategory_Defaults_To_HttpApi_System()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "SomeService", "HTTP", ["GET"], 1, 1, DependencyCategory: null)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("System(someService, \"SomeService\")", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_Database_Uses_Database_Shape()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "SqlDb", "SQL", ["Query"], 3, 2, DependencyCategory: "SQL")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("database \"SqlDb\" as sqlDb", result);
        Assert.DoesNotContain("as sqlDb <<system>>", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_Cache_Uses_Collections_Shape()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "RedisCache", "Redis", ["GET"], 5, 3, DependencyCategory: "Redis")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("collections \"RedisCache\" as redisCache", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_MessageQueue_Uses_Queue_Shape()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "EventBus", "ServiceBus", ["Send"], 2, 1, DependencyCategory: "ServiceBus")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("queue \"EventBus\" as eventBus", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_Http_Uses_Rectangle_System_Shape()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "OrderService", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("<<system>>", result);
        Assert.Contains("rectangle", result);
    }

    [Fact]
    public void GeneratePlantUml_UseC4False_Includes_Skinparam_For_Database_Collections_Queue()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "Db", "SQL", ["Query"], 1, 1, DependencyCategory: "SQL")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Contains("skinparam database {", result);
        Assert.Contains("skinparam collections {", result);
        Assert.Contains("skinparam queue {", result);
    }

    // ═══════════════════════════════════════════════════════════
    // Dependency-Type Arrow Coloring
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GeneratePlantUml_DependencyType_Mode_Colors_Arrow_By_Target_Type()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "CosmosDb", "CosmosDB", ["Read"], 1, 1, DependencyCategory: "CosmosDB")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        // Database color = #E74C3C
        Assert.Contains("$tags=\"#E74C3C\"", result);
    }

    [Fact]
    public void GeneratePlantUml_DependencyType_Mode_UseC4False_Colors_Arrow()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "Redis", "Redis", ["GET"], 1, 1, DependencyCategory: "Redis")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        // Cache color = #F39C12
        Assert.Contains("-[#F39C12]->", result);
    }

    [Fact]
    public void GeneratePlantUml_DependencyType_Default_HttpApi_Gets_Blue_Arrow()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "Api", "HTTP", ["GET"], 1, 1)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        // HttpApi color = #438DD5
        Assert.Contains("$tags=\"#438DD5\"", result);
    }

    [Fact]
    public void GeneratePlantUml_DependencyColors_Override_Applied()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "CosmosDb", "CosmosDB", ["Read"], 1, 1, DependencyCategory: "CosmosDB")
        };
        var options = new ComponentDiagramOptions
        {
            DependencyColors = new Dictionary<string, string> { ["CosmosDB"] = "#FF00FF" }
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, options);

        Assert.Contains("$tags=\"#FF00FF\"", result);
        Assert.DoesNotContain("#E74C3C", result);
    }

    [Fact]
    public void GeneratePlantUml_Mixed_Types_Each_Gets_Correct_Arrow_Color()
    {
        var relationships = new[]
        {
            new ComponentRelationship("App", "Api", "HTTP", ["GET"], 1, 1),
            new ComponentRelationship("App", "Db", "CosmosDB", ["Query"], 1, 1, DependencyCategory: "CosmosDB"),
            new ComponentRelationship("App", "Cache", "Redis", ["GET"], 1, 1, DependencyCategory: "Redis"),
            new ComponentRelationship("App", "Queue", "ServiceBus", ["Send"], 1, 1, DependencyCategory: "ServiceBus")
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships);

        Assert.Contains("#438DD5", result); // HttpApi
        Assert.Contains("#E74C3C", result); // Database
        Assert.Contains("#F39C12", result); // Cache
        Assert.Contains("#9B59B6", result); // MessageQueue
    }

    // ═══════════════════════════════════════════════════════════
    // GetProtocol — DependencyCategory preference
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ExtractRelationships_DependencyCategory_Used_As_Protocol()
    {
        var logs = new[]
        {
            MakeRequest(serviceName: "CosmosDb", callerName: "App", method: "POST",
                uri: "http://example.com/cosmos/items", dependencyCategory: "CosmosDB")
        };

        var rels = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(rels);
        Assert.Equal("CosmosDB", rels[0].Protocol);
        Assert.Equal("CosmosDB", rels[0].DependencyCategory);
    }

    [Fact]
    public void ExtractRelationships_Null_DependencyCategory_Falls_Back_To_HTTP()
    {
        var logs = new[]
        {
            MakeRequest(serviceName: "Service", callerName: "App")
        };

        var rels = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(rels);
        Assert.Equal("HTTP", rels[0].Protocol);
        Assert.Null(rels[0].DependencyCategory);
    }

    // ─── Spanner/SQL method extraction (component diagram arrow labels) ──

    [Fact]
    public void ExtractRelationships_Spanner_Uses_Short_Method_Names_Not_Full_Sql()
    {
        // Simulates what SpannerTracker produces at Raw verbosity after the fix:
        // Method is just the keyword, not the full SQL text.
        var logs = new[]
        {
            MakeRequest(serviceName: "Spanner", callerName: "My API", method: "Select", dependencyCategory: "Spanner"),
            MakeRequest(serviceName: "Spanner", callerName: "My API", method: "Select", dependencyCategory: "Spanner"),
            MakeRequest(serviceName: "Spanner", callerName: "My API", method: "Insert", dependencyCategory: "Spanner"),
            MakeRequest(serviceName: "Spanner", callerName: "My API", method: "InsertOrUpdate", dependencyCategory: "Spanner"),
        };

        var rels = ComponentDiagramGenerator.ExtractRelationships(logs);

        Assert.Single(rels);
        Assert.Equal(new HashSet<string> { "Select", "Insert", "InsertOrUpdate" }, rels[0].Methods);
        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(rels, useC4: false);
        Assert.Contains("Spanner: Insert, InsertOrUpdate, Select", plantUml);
        Assert.DoesNotContain("FROM", plantUml);
        Assert.DoesNotContain("WHERE", plantUml);
    }

    // ═══════════════════════════════════════════════════════════
    // Edge label wrapping — keeping the drawn diagram inside PlantUML's size limit
    // ═══════════════════════════════════════════════════════════

    /// <summary>A relationship whose method list is long enough to need wrapping.</summary>
    private static ComponentRelationship[] ManyOperations(int count = 30) =>
    [
        new("Data Insights API", "ClickHouse", "ClickHouse",
            [.. Enumerable.Range(0, count).Select(i => $"SELECT FROM location_performance_{i:D2}")],
            609, 199, "ClickHouse")
    ];

    /// <summary>The label of the one edge in <paramref name="plantUml"/>, without its quotes.</summary>
    private static string EdgeLabel(string plantUml)
    {
        var edge = plantUml.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Single(l => l.Contains("-> ", StringComparison.Ordinal) && l.Contains(" : \"", StringComparison.Ordinal));
        var open = edge.IndexOf(" : \"", StringComparison.Ordinal) + 4;
        return edge[open..^1];
    }

    /// <summary>The display lines a label draws as — it is one physical statement broken by <c>\n</c> escapes.</summary>
    private static string[] DisplayLines(string label) => label.Split("\\n");

    [Fact]
    public void GeneratePlantUml_LongEdgeLabel_IsBrokenIntoDisplayLines()
    {
        // Java PlantUML does not apply `skinparam wrapWidth` to arrow labels, so a label listing every
        // SQL operation drew one 6697-pixel-wide line and plantuml.com cropped the diagram at 4096,
        // losing every dependency node (user-reported). The generator has to break the line itself.
        var result = ComponentDiagramGenerator.GeneratePlantUml(ManyOperations(), useC4: false);

        var lines = DisplayLines(EdgeLabel(result));

        Assert.True(lines.Length > 1, "a thirty-operation label should not be drawn as one line");
        Assert.All(lines, line => Assert.True(
            line.Length <= ComponentDiagramGenerator.MaxLabelLineChars,
            $"display line of {line.Length} chars: {line}"));
    }

    [Fact]
    public void GeneratePlantUml_OrdinaryEdgeLabel_IsLeftExactlyAsItWas()
    {
        // Wrapping is for labels that would otherwise overflow the canvas. Everything shorter than the
        // budget keeps its bytes, so the overwhelming majority of diagrams are unchanged by this.
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP", ["GET", "POST"], 252, 241)
        };

        var result = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        Assert.Equal("HTTP: GET, POST - 252 calls across 241 tests", EdgeLabel(result));
    }

    [Fact]
    public void GeneratePlantUml_Wrapping_BreaksAtWhitespaceAndKeepsEveryOperationWhole()
    {
        var result = ComponentDiagramGenerator.GeneratePlantUml(ManyOperations(), useC4: false);

        var label = EdgeLabel(result);

        // Every operation must survive intact — a break inside a table name would be a silent
        // corruption of the architecture overview rather than a layout choice.
        for (var i = 0; i < 30; i++)
            Assert.Contains($"SELECT FROM location_performance_{i:D2}", label, StringComparison.Ordinal);
        // And the breaks replaced spaces, so no display line starts or ends with one.
        Assert.All(DisplayLines(label), line => Assert.Equal(line.Trim(), line));
    }

    [Fact]
    public void GeneratePlantUml_Wrapping_StillHonoursTheStatementLimit()
    {
        // The `\n` escapes are two characters each and count toward the statement the parser measures,
        // so the cap has to be applied to the wrapped label, not to the label it was built from.
        var options = new ComponentDiagramOptions { RelationshipLabelFormatter = _ => string.Join(" ", Enumerable.Repeat("operation", 900)) };

        var result = ComponentDiagramGenerator.GeneratePlantUml(ManyOperations(), options, useC4: false);

        var edge = result.Split('\n').Single(l => l.Contains("operation", StringComparison.Ordinal));
        Assert.True(edge.Trim().Length <= PlantUmlStatementLimits.MaxMessageStatementChars, $"{edge.Trim().Length} chars");
    }

    [Fact]
    public void GeneratePlantUml_Wrapping_HardBreaksAWordNoLineCouldHold()
    {
        // A single unbreakable run has no whitespace to break at, so wrapping alone would leave it as
        // wide as it is long — a formatter returning one 6000-character token drew 11687 pixels wide.
        var options = new ComponentDiagramOptions { RelationshipLabelFormatter = _ => new string('x', 6000) };

        var result = ComponentDiagramGenerator.GeneratePlantUml(ManyOperations(), options, useC4: false);

        Assert.All(DisplayLines(EdgeLabel(result)), line => Assert.True(
            line.Length <= ComponentDiagramGenerator.MaxLabelLineChars,
            $"display line of {line.Length} chars"));
    }

    [Fact]
    public void GeneratePlantUml_Wrapping_NeverSplitsAWordCarryingMarkup()
    {
        // Hard-breaking is a last resort and it is blind: splitting `[[#anchor` or `<size:10>` in half
        // would turn a link or a tag into literal text. Such a token is left long instead.
        var marked = "[[#iflow-rel-" + new string('a', 200) + "]]";
        var options = new ComponentDiagramOptions { RelationshipLabelFormatter = _ => marked };

        var result = ComponentDiagramGenerator.GeneratePlantUml(ManyOperations(), options, useC4: false);

        Assert.Contains(marked, EdgeLabel(result), StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratePlantUml_Wrapping_PreservesTheStatsLabelsOwnLineStructure()
    {
        // The stats label is already three display lines, the first of which is a creole link whose
        // text is the method list. Wrapping works within each of those, never across them.
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "OrderService", "HTTP",
                [.. Enumerable.Range(0, 30).Select(i => $"GET /api/orders/very/long/route/segment/{i:D2}")], 10, 5)
        };
        var stats = new Dictionary<string, RelationshipStats>
        {
            ["iflow-rel-Caller-OrderService"] = new(10, 5, 50.0, 45.0, 120.0, 250.0, 5.0, 300.0,
                0.0, new Dictionary<HttpStatusCode, int>(), [], null, null, false, 0, new Dictionary<string, int>(), null, 0)
        };

        var label = EdgeLabel(ComponentDiagramGenerator.GeneratePlantUml(relationships, stats: stats, useC4: false));
        var lines = DisplayLines(label);

        Assert.StartsWith("[[#iflow-rel-Caller-OrderService", label, StringComparison.Ordinal);
        Assert.Contains("]]", label, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("P50: 45ms", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("10 calls across 5 tests", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.True(
            line.Length <= ComponentDiagramGenerator.MaxLabelLineChars,
            $"display line of {line.Length} chars: {line}"));
    }
}
