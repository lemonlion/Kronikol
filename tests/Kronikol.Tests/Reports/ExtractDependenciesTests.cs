using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ExtractDependenciesTests
{
    [Fact]
    public void Extracts_entity_participant()
    {
        var puml = """
            actor "Caller" as caller
            entity "OrderService" as orderService
            caller -> orderService: POST /orders
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("Caller", deps);
        Assert.Contains("OrderService", deps);
    }

    [Fact]
    public void Extracts_database_type()
    {
        var puml = """
            actor "Caller" as caller
            database "Cosmos DB" as cosmosDB
            caller -> cosmosDB: query
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("Cosmos DB", deps);
    }

    [Fact]
    public void Extracts_collections_type()
    {
        var puml = """
            actor "Caller" as caller
            collections "Redis Cache" as redisCache
            caller -> redisCache: GET key
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("Redis Cache", deps);
    }

    [Fact]
    public void Extracts_queue_type()
    {
        var puml = """
            actor "Caller" as caller
            queue "ServiceBus" as serviceBus
            caller -> serviceBus: send message
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("ServiceBus", deps);
    }

    [Fact]
    public void Extracts_participant_type()
    {
        var puml = """
            actor "Caller" as caller
            participant "Unknown Service" as unknownService
            caller -> unknownService: call
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("Unknown Service", deps);
    }

    [Fact]
    public void Extracts_boundary_type()
    {
        var puml = """
            actor "Caller" as caller
            boundary "API Gateway" as apiGateway
            caller -> apiGateway: request
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("API Gateway", deps);
    }

    [Fact]
    public void Extracts_control_type()
    {
        var puml = """
            actor "Caller" as caller
            control "Orchestrator" as orchestrator
            caller -> orchestrator: start
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("Orchestrator", deps);
    }

    [Fact]
    public void Extracts_actor_type()
    {
        var puml = """
            actor "Test Runner" as testRunner
            entity "OrderService" as orderService
            testRunner -> orderService: POST /orders
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Contains("Test Runner", deps);
    }

    [Fact]
    public void Extracts_all_types_from_mixed_diagram()
    {
        var puml = """
            actor "Caller" as caller
            entity "OrderService" as orderService
            database "Cosmos DB" as cosmosDB
            collections "Redis Cache" as redisCache
            queue "ServiceBus" as serviceBus
            boundary "API Gateway" as apiGateway
            control "Orchestrator" as orchestrator
            participant "Legacy" as legacy
            caller -> orderService: POST /orders
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Equal(8, deps.Count);
        Assert.Contains("Caller", deps);
        Assert.Contains("OrderService", deps);
        Assert.Contains("Cosmos DB", deps);
        Assert.Contains("Redis Cache", deps);
        Assert.Contains("ServiceBus", deps);
        Assert.Contains("API Gateway", deps);
        Assert.Contains("Orchestrator", deps);
        Assert.Contains("Legacy", deps);
    }

    [Fact]
    public void Returns_empty_for_null_input()
    {
        var deps = ReportGenerator.ExtractDependencies(null!, DiagramFormat.PlantUml);

        Assert.Empty(deps);
    }

    [Fact]
    public void Returns_empty_for_empty_string()
    {
        var deps = ReportGenerator.ExtractDependencies("", DiagramFormat.PlantUml);

        Assert.Empty(deps);
    }

    [Fact]
    public void Ignores_arrow_lines()
    {
        var puml = """
            actor "Caller" as caller
            entity "Svc" as svc
            caller -> svc: POST /orders
            svc --> caller: 200 OK
            """;

        var deps = ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml);

        Assert.Equal(2, deps.Count);
    }

    [Fact]
    public void The_participant_of_a_diagram_with_no_calls_is_no_dependency()
    {
        // A scenario that made no call is drawn with one participant, "(no interactions)", to hold its step markers. It
        // was a chip of the dependency filter until 5.0.0; the scenario has no dependency (plans/OPTION_C_PLAN.md S6).
        var puml = "@startuml\n" + global::Kronikol.PlantUml.PlantUmlCreator.MarkerOnlyParticipant + "\n@enduml";

        Assert.Empty(ReportGenerator.ExtractDependencies(puml, DiagramFormat.PlantUml));
    }
}
