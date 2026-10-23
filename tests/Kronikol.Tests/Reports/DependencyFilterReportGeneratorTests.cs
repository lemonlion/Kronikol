using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class DependencyFilterReportGeneratorTests
{
    private static Feature[] MakeFeatures(params (string id, string name)[] scenarios) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = scenarios.Select(s => new Scenario
            {
                Id = s.id,
                DisplayName = s.name,
                IsHappyPath = true,
                Result = ExecutionResult.Passed
            }).ToArray()
        }
    ];

    private static DefaultDiagramsFetcher.DiagramAsCode[] MakeDiagrams(params (string testId, string plantuml)[] diagrams) =>
        diagrams.Select(d => new DefaultDiagramsFetcher.DiagramAsCode(d.testId, "", d.plantuml)).ToArray();

    private string GenerateReport(Feature[] features, DefaultDiagramsFetcher.DiagramAsCode[] diagrams, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    [Fact]
    public void Report_contains_dependency_filter_container()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\ncaller -> orderService: POST /orders\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterContainer.html");

        Assert.Contains("dependency-filters", content);
    }

    [Fact]
    public void Report_contains_dependency_toggle_buttons_for_each_participant()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\nentity \"PaymentGateway\" as paymentGateway\ncaller -> orderService: POST /orders\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterButtons.html");

        Assert.Contains("data-dependency=\"OrderService\"", content);
        Assert.Contains("data-dependency=\"PaymentGateway\"", content);
    }

    [Fact]
    public void Report_includes_caller_actor_in_dependency_toggles()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\ncaller -> orderService: POST /orders\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterWithCaller.html");

        Assert.Contains("data-dependency=\"Caller\"", content);
        Assert.Contains("data-dependency=\"OrderService\"", content);
    }

    [Fact]
    public void Report_scenario_has_data_dependencies_attribute()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\nentity \"PaymentGateway\" as paymentGateway\ncaller -> orderService: POST /orders\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterDataAttr.html");

        Assert.Contains("data-dependencies=\"", content);
        Assert.Contains("OrderService", content);
        Assert.Contains("PaymentGateway", content);
    }

    [Fact]
    public void Report_deduplicates_dependencies_across_multiple_diagrams()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(
            ("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\ncaller -> orderService: POST\n@enduml"),
            ("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\nentity \"PaymentGateway\" as paymentGateway\ncaller -> orderService: GET\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterDedup.html");

        // OrderService should appear only once as a toggle
        var toggleCount = System.Text.RegularExpressions.Regex.Matches(content, "data-dependency=\"OrderService\"").Count;
        Assert.Equal(1, toggleCount);
    }

    [Fact]
    public void Report_collects_dependencies_across_multiple_scenarios()
    {
        var features = MakeFeatures(("t1", "Create order"), ("t2", "Check payment"));
        var diagrams = MakeDiagrams(
            ("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\ncaller -> orderService: POST\n@enduml"),
            ("t2", "@startuml\nactor \"Caller\" as caller\nentity \"PaymentGateway\" as paymentGateway\ncaller -> paymentGateway: GET\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterMultiScenario.html");

        Assert.Contains("data-dependency=\"OrderService\"", content);
        Assert.Contains("data-dependency=\"PaymentGateway\"", content);
    }

    [Fact]
    public void Report_no_diagrams_means_no_dependency_toggle_buttons()
    {
        var features = MakeFeatures(("t1", "Manual test"));
        var diagrams = Array.Empty<DefaultDiagramsFetcher.DiagramAsCode>();

        var content = GenerateReport(features, diagrams, "DepFilterNoDiagrams.html");

        Assert.DoesNotContain("<button class=\"dependency-toggle\"", content);
    }

    [Fact]
    public void Report_contains_dependency_filter_javascript_function()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\ncaller -> orderService: POST\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterJs.html");

        Assert.Contains("filter_dependencies", content);
    }

    [Fact]
    public void Report_contains_dep_hidden_css_class()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"OrderService\" as orderService\ncaller -> orderService: POST\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterCss.html");

        Assert.Contains("dep-hidden", content);
    }

    [Fact]
    public void Report_dependency_toggles_are_sorted_alphabetically()
    {
        var features = MakeFeatures(("t1", "Create order"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nentity \"Zebra\" as z\nentity \"Alpha\" as a\nentity \"Middle\" as m\ncaller -> z: Go\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterSorted.html");

        var alphaIdx = content.IndexOf("data-dependency=\"Alpha\"");
        var middleIdx = content.IndexOf("data-dependency=\"Middle\"");
        var zebraIdx = content.IndexOf("data-dependency=\"Zebra\"");

        Assert.True(alphaIdx < middleIdx, "Alpha should come before Middle");
        Assert.True(middleIdx < zebraIdx, "Middle should come before Zebra");
    }

    [Fact]
    public void Report_contains_database_dependency_toggle()
    {
        var features = MakeFeatures(("t1", "Query cosmos"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\ndatabase \"Cosmos DB\" as cosmosDB\ncaller -> cosmosDB: query\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterDatabase.html");

        Assert.Contains("data-dependency=\"Cosmos DB\"", content);
    }

    [Fact]
    public void Report_contains_queue_dependency_toggle()
    {
        var features = MakeFeatures(("t1", "Send message"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\nqueue \"ServiceBus\" as serviceBus\ncaller -> serviceBus: send\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterQueue.html");

        Assert.Contains("data-dependency=\"ServiceBus\"", content);
    }

    [Fact]
    public void Report_contains_collections_dependency_toggle()
    {
        var features = MakeFeatures(("t1", "Cache lookup"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"Caller\" as caller\ncollections \"Redis Cache\" as redisCache\ncaller -> redisCache: GET\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterCollections.html");

        Assert.Contains("data-dependency=\"Redis Cache\"", content);
    }

    [Fact]
    public void Report_contains_all_plantuml_participant_types_as_dependency_toggles()
    {
        var features = MakeFeatures(("t1", "Full test"));
        var diagrams = MakeDiagrams(("t1", "@startuml\nactor \"TestRunner\" as tr\nboundary \"Gateway\" as gw\ncontrol \"Orchestrator\" as orch\nentity \"OrderService\" as os\ndatabase \"SQL\" as sql\ncollections \"Cache\" as cache\nqueue \"Queue\" as q\nparticipant \"Legacy\" as leg\ntr -> os: call\n@enduml"));

        var content = GenerateReport(features, diagrams, "DepFilterAllTypes.html");

        Assert.Contains("data-dependency=\"TestRunner\"", content);
        Assert.Contains("data-dependency=\"Gateway\"", content);
        Assert.Contains("data-dependency=\"Orchestrator\"", content);
        Assert.Contains("data-dependency=\"OrderService\"", content);
        Assert.Contains("data-dependency=\"SQL\"", content);
        Assert.Contains("data-dependency=\"Cache\"", content);
        Assert.Contains("data-dependency=\"Queue\"", content);
        Assert.Contains("data-dependency=\"Legacy\"", content);
    }
}
