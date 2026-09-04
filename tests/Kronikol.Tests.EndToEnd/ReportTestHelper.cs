using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Shared helper for generating test reports with diagrams for Playwright tests.
/// </summary>
public static class ReportTestHelper
{
    private const string PlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : POST /api/orders
        note left
        Content-Type: application/json
        {"item":"Widget","qty":2}
        end note

        svc -> db : INSERT INTO Orders
        note left
        INSERT INTO Orders (Item, Qty)
        VALUES ('Widget', 2)
        end note
        db --> svc : OK
        svc --> caller : 201 Created
        note left
        {"id":"abc-123","status":"created"}
        end note
        @enduml
        """;

    /// <summary>
    /// A wide PlantUML diagram with many participants that exceeds typical container widths.
    /// Used by zoom tests that need a diagram wider than the viewport.
    /// </summary>
    private const string WidePlantUmlSource = """
        @startuml
        participant "AuthenticationService" as a1
        participant "AuthorizationEngine" as a2
        participant "UserProfileManager" as a3
        participant "OrderProcessingUnit" as a4
        participant "InventoryTracker" as a5
        participant "PaymentGateway" as a6
        participant "NotificationHub" as a7
        participant "AuditLogService" as a8
        participant "CacheManager" as a9
        participant "ExternalApiClient" as a10
        participant "ReportingEngine" as a11
        participant "DataWarehouse" as a12
        participant "EventStreamProcessor" as a13
        participant "ConfigurationStore" as a14

        a1 -> a2 : validatePermissions
        a2 -> a3 : getUserProfile
        a3 -> a4 : processOrder
        a4 -> a5 : checkInventory
        a5 -> a6 : processPayment
        a6 -> a7 : sendNotification
        a7 -> a8 : logActivity
        a8 -> a9 : updateCache
        a9 -> a10 : callExternalApi
        a10 -> a11 : generateReport
        a11 -> a12 : storeResults
        a12 -> a13 : processEventStream
        a13 -> a14 : getConfiguration
        a14 --> a1 : complete
        @enduml
        """;

    public static (Feature[] Features, DiagramAsCode[] Diagrams) CreateTestData()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Create order successfully", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                        Categories = ["Smoke", "API"],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the order is created", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "t2", DisplayName = "Delete order fails gracefully", IsHappyPath = false,
                        Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(5),
                        Categories = ["API"],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I delete a non-existent order", Status = ExecutionResult.Failed },
                            new ScenarioStep { Keyword = "Then", Text = "an error is returned", Status = ExecutionResult.Skipped }
                        ]
                    },
                    new Scenario
                    {
                        Id = "t3", DisplayName = "List orders returns paginated results", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Categories = ["Smoke"]
                    }
                ]
            },
            new Feature
            {
                DisplayName = "Payment Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t4", DisplayName = "Process payment", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500)
                    },
                    new Scenario
                    {
                        Id = "t5", DisplayName = "Refund payment", IsHappyPath = false,
                        Result = ExecutionResult.Skipped, Duration = TimeSpan.FromMilliseconds(100)
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", PlantUmlSource),
            new DiagramAsCode("t2", "", PlantUmlSource)
        };

        return (features, diagrams);
    }

    public static string GenerateReport(string tempDir, string outputDir, string fileName)
    {
        var (features, diagrams) = CreateTestData();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithWideDiagram(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", WidePlantUmlSource),
            new DiagramAsCode("t2", "", WidePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// A wide PlantUML diagram that also contains notes (for testing zoom + note interaction).
    /// </summary>
    private const string WideWithNotesPlantUmlSource = """
        @startuml
        participant "AuthenticationService" as a1
        participant "AuthorizationEngine" as a2
        participant "UserProfileManager" as a3
        participant "OrderProcessingUnit" as a4
        participant "InventoryTracker" as a5
        participant "PaymentGateway" as a6
        participant "NotificationHub" as a7
        participant "AuditLogService" as a8
        participant "CacheManager" as a9
        participant "ExternalApiClient" as a10
        participant "ReportingEngine" as a11
        participant "DataWarehouse" as a12
        participant "EventStreamProcessor" as a13
        participant "ConfigurationStore" as a14

        a1 -> a2 : validatePermissions
        note left
        Authorization request
        {"user":"admin","action":"create"}
        end note
        a2 -> a3 : getUserProfile
        a3 -> a4 : processOrder
        note left
        Order payload
        {"item":"Widget","qty":2}
        end note
        a4 -> a5 : checkInventory
        a5 -> a6 : processPayment
        a6 -> a7 : sendNotification
        a7 -> a8 : logActivity
        a8 -> a9 : updateCache
        a9 -> a10 : callExternalApi
        a10 -> a11 : generateReport
        a11 -> a12 : storeResults
        a12 -> a13 : processEventStream
        a13 -> a14 : getConfiguration
        a14 --> a1 : complete
        @enduml
        """;

    public static string GenerateReportWithWideNoteDiagram(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", WideWithNotesPlantUmlSource),
            new DiagramAsCode("t2", "", WideWithNotesPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with an embedded component diagram for testing
    /// the dependency-type coloring and embedded component diagram section.
    /// </summary>
    public static string GenerateReportWithEmbeddedComponentDiagram(string tempDir, string outputDir, string fileName)
    {
        var (features, diagrams) = CreateTestData();

        // PlantUML for a component diagram with typed shapes
        const string componentPlantUml = """
            @startuml
            left to right direction
            skinparam defaultTextAlignment center
            skinparam wrapWidth 200
            skinparam shadowing false
            skinparam rectangle<<person>> {
              BackgroundColor #08427B
              FontColor #FFFFFF
              BorderColor #073B6F
              RoundCorner 25
            }
            skinparam rectangle<<system>> {
              BackgroundColor #438DD5
              FontColor #FFFFFF
              BorderColor #3C7FC0
              RoundCorner 25
            }
            skinparam database {
              BackgroundColor #E74C3C
              FontColor #FFFFFF
              BorderColor #C0392B
            }
            skinparam queue {
              BackgroundColor #9B59B6
              FontColor #FFFFFF
              BorderColor #7D3C98
            }
            skinparam arrow {
              Color #666666
              FontColor #666666
              FontSize 11
            }

            title Component Diagram

            rectangle "**Client**\n<size:10>[Person]</size>" as client <<person>>
            rectangle "**API**\n<size:10>[Software System]</size>" as api <<system>>
            database "CosmosDB" as cosmosDB
            queue "ServiceBus" as serviceBus

            client -[#438DD5]-> api : "HTTP: GET - 10 calls across 5 tests"
            api -[#E74C3C]-> cosmosDB : "CosmosDB: Query - 8 calls across 4 tests"
            api -[#9B59B6]-> serviceBus : "ServiceBus: Send - 3 calls across 2 tests"
            @enduml
            """;

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            componentDiagramPlantUml: componentPlantUml);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a combined report by simulating two parallel CI runners: each emits a mergeable
    /// TestRunReport.json (disjoint features, diagrams and component relationships), then the two are
    /// merged and rendered into a single HTML report. Exercises the full merge pipeline end-to-end.
    /// </summary>
    public static string GenerateMergedReport(string tempDir, string outputDir, string fileName)
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // Runner 1: the "Order Feature".
        var runner1 = new[]
        {
            new Feature
            {
                DisplayName = "Order Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "m1", DisplayName = "Create order successfully", IsHappyPath = true, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2), Categories = ["Smoke"],
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a muffin recipe", Status = ExecutionResult.Passed,
                                TextSegments = [StepTextSegment.Literal("a muffin "), StepTextSegment.TableRef("recipe")],
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "recipe", Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Flour", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Classic", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("Plain Flour", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "Then", Text = "the order is created", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario { Id = "m2", DisplayName = "Delete order fails gracefully", Result = ExecutionResult.Failed, ErrorMessage = "404", Duration = TimeSpan.FromSeconds(1) }
                ]
            }
        };
        var json1 = ReportGenerator.GenerateMergeableReportJson(
            runner1, start, start.AddMinutes(3),
            new[] { new DiagramAsCode("m1", "", PlantUmlSource), new DiagramAsCode("m2", "", PlantUmlSource) }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [ new ComponentRelationship("Caller", "OrderService", "HTTP", new HashSet<string> { "POST /api/orders" }, 2, 1, "http"),
              new ComponentRelationship("OrderService", "Database", "SQL", new HashSet<string> { "INSERT" }, 2, 1, "sql") ],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

        // Runner 2: the "Payment Feature".
        var runner2 = new[]
        {
            new Feature
            {
                DisplayName = "Payment Feature",
                Scenarios =
                [
                    new Scenario { Id = "m3", DisplayName = "Process payment", IsHappyPath = true, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500) }
                ]
            }
        };
        var json2 = ReportGenerator.GenerateMergeableReportJson(
            runner2, start.AddMinutes(1), start.AddMinutes(4),
            new[] { new DiagramAsCode("m3", "", PlantUmlSource) }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [ new ComponentRelationship("Caller", "PaymentService", "HTTP", new HashSet<string> { "POST /api/pay" }, 1, 1, "http") ],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

        var file1 = Path.Combine(tempDir, "runner1.json");
        var file2 = Path.Combine(tempDir, "runner2.json");
        File.WriteAllText(file1, json1);
        File.WriteAllText(file2, json2);

        var outputPath = Path.Combine(tempDir, fileName);
        MergeableReportRenderer.MergeFilesToHtml([file1, file2], outputPath, "Combined Test Run Report");

        File.Copy(outputPath, Path.Combine(outputDir, fileName), true);
        return new Uri(outputPath).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source with one long note (more lines than the default truncation of 40)
    /// and one short note (2 lines). Used by tests that verify the 3-state note cycle
    /// for long notes vs the 2-state cycle for short notes.
    /// </summary>
    private const string LongNotePlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : POST /api/orders
        note left
        Line 1
        Line 2
        Line 3
        Line 4
        Line 5
        Line 6
        Line 7
        Line 8
        Line 9
        Line 10
        Line 11
        Line 12
        Line 13
        Line 14
        Line 15
        Line 16
        Line 17
        Line 18
        Line 19
        Line 20
        Line 21
        Line 22
        Line 23
        Line 24
        Line 25
        Line 26
        Line 27
        Line 28
        Line 29
        Line 30
        Line 31
        Line 32
        Line 33
        Line 34
        Line 35
        Line 36
        Line 37
        Line 38
        Line 39
        Line 40
        Line 41
        Line 42
        Line 43
        Line 44
        Line 45
        end note
        svc -> db : INSERT INTO Orders
        note left
        Short note line 1
        Short note line 2
        Short note line 3
        Short note line 4
        end note
        db --> svc : OK
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithLongNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        // Only one diagram to avoid ambiguity in Playwright selectors
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", LongNotePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string PartitionPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "SetupService" as setup
        participant "OrderService" as svc
        participant "Database" as db

        partition #F6F6F6 Setup
          caller -> setup : POST /api/setup
          note left
          Content-Type: application/json
          {"env":"test"}
          end note
          setup --> caller : 200 OK
        end

        caller -> svc : POST /api/orders
        note left
        Content-Type: application/json
        {"item":"Widget","qty":2}
        end note
        svc -> db : INSERT INTO Orders
        note left
        INSERT INTO Orders (Item, Qty)
        VALUES ('Widget', 2)
        end note
        db --> svc : OK
        svc --> caller : 201 Created
        @enduml
        """;

    private static string PartitionLongNotePlantUmlSource
    {
        get
        {
            // Build PlantUML source with long notes (> 40 lines) to trigger truncation
            var longContent = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"Line {i}: some content here"));
            return $"""
                @startuml
                actor "Caller" as caller
                participant "SetupService" as setup
                participant "OrderService" as svc
                participant "Database" as db

                partition #F6F6F6 Setup
                  caller -> setup : POST /api/setup
                  note left
                {longContent}
                  end note
                  setup --> caller : 200 OK
                end

                caller -> svc : POST /api/orders
                note left
                {longContent}
                end note
                svc -> db : INSERT INTO Orders
                note left
                {longContent}
                end note
                db --> svc : OK
                svc --> caller : 201 Created
                @enduml
                """;
        }
    }

    public static string GenerateReportWithPartitionLongNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", PartitionLongNotePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithPartitionDiagram(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", PartitionPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source with long notes (10+ lines) for testing truncation across
    /// multiple diagrams. Each scenario gets its own diagram with notes that exceed
    /// a truncation limit of 5 lines.
    /// </summary>
    private static string TwoScenarioLongNotePlantUmlSource(int scenarioIndex)
    {
        var longContent = string.Join("\n", Enumerable.Range(1, 15).Select(i => $"Scenario {scenarioIndex} - Line {i}"));
        return $"""
            @startuml
            actor "Caller" as caller
            participant "Service{scenarioIndex}" as svc
            participant "Database" as db

            caller -> svc : POST /api/items
            note left
            {longContent}
            end note
            svc -> db : INSERT INTO Items
            db --> svc : OK
            svc --> caller : 201 Created
            note right
            {longContent}
            end note
            @enduml
            """;
    }

    public static string GenerateReportWithTwoLongNoteDiagrams(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", TwoScenarioLongNotePlantUmlSource(1)),
            new DiagramAsCode("t2", "", TwoScenarioLongNotePlantUmlSource(2))
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report where ONE scenario has TWO diagram containers (simulating a
    /// split diagram). Each diagram has long notes that exceed a truncation limit of 5.
    /// Used to test that hover buttons appear on ALL diagrams within a single scenario
    /// after a truncation dropdown change.
    /// </summary>
    public static string GenerateReportWithSplitDiagramLongNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        // Generate long note content (50+ lines) to simulate real-world split diagrams
        var longContent1 = string.Join("\n",
            Enumerable.Range(1, 50).Select(i => $"  \"field{i}\": \"value {i}\","));
        var longContent2 = string.Join("\n",
            new[] { "..Continued From Previous Diagram.." }.Concat(
                Enumerable.Range(1, 50).Select(i => $"  \"continued_{i}\": \"data {i}\",")));

        var source1 = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 1
            actor "Caller" as caller
            entity "Service" as svc
            caller -> svc : GET /api/spec
            note left
            <color:gray>[traceparent=00-abc-123-00]
            end note
            svc --> caller : OK
            note right
            <color:gray>[X-Correlation-Id=test-123]

            {
            {{longContent1}}
            ..Continued On Next Diagram..
            end note
            @enduml
            """;

        var source2 = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 2
            actor "Caller" as caller
            entity "Service" as svc
            svc --> caller : OK
            note right
            {{longContent2}}
            }
            end note
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source1),
            new DiagramAsCode("t1", "", source2)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with THREE diagram containers for ONE scenario, matching a real-world
    /// split diagram with a very large response body (like an AsyncAPI spec).
    /// Structure: diagram 1 has no notes, diagram 2 has 2 notes (short header + long body),
    /// diagram 3 has 1 note (continuation with "..Continued From Previous Diagram..").
    /// </summary>
    public static string GenerateReportWithThreeDiagramSplit(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        // Diagram 1: simple request/response with NO notes (like puml-0 in the real report)
        const string source1 = """
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 1
            actor "Caller" as caller
            entity "Service" as svc
            caller -[#438DD5]> svc : GET /api/spec
            @enduml
            """;

        // Diagram 2: response with 2 notes — short header + VERY long JSON body (200+ lines)
        var longJsonContent = string.Join("\n",
            Enumerable.Range(1, 200).Select(i =>
                $"    \"field_{i}\": {{\"type\": \"string\", \"description\": \"Field {i} description\"}},"
            ));
        var source2 = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 1
            actor "Caller" as caller
            entity "Service" as svc
            caller -[#438DD5]> svc : GET /api/spec
            note left
            <color:gray>[traceparent=00-abc-def-00]
            end note
            svc -[#438DD5]-> caller: OK
            note right
            <color:gray>[X-Correlation-Id=test-456]

            {
              "asyncapi": "3.0.0",
              "info": {
                "title": "Breakfast Provider",
                "version": "1.0.0"
              },
              "components": {
                "schemas": {
            {{longJsonContent}}
              ..Continued On Next Diagram..
            end note
            @enduml
            """;

        // Diagram 3: continuation note with "..Continued From Previous Diagram.."
        var continuedContent = string.Join("\n",
            Enumerable.Range(201, 100).Select(i =>
                $"    \"continued_{i}\": {{\"type\": \"integer\", \"description\": \"Continued field {i}\"}},"
            ));
        var source3 = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 2
            actor "Caller" as caller
            entity "Service" as svc
            svc -[#438DD5]-> caller: OK
            note right
            ..Continued From Previous Diagram..
            {{continuedContent}}
                }
              }
            }
            end note
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source1),
            new DiagramAsCode("t1", "", source2),
            new DiagramAsCode("t1", "", source3)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source where the note has 5 gray header lines + 1 blank + 38 body lines = 44 total.
    /// Total lines exceed 40 (so it's "long" when headers visible), but non-gray effective lines = 38
    /// (so it's NOT "long" when headers are hidden). Used by tests that verify the expand arrow
    /// does NOT appear when all visible content already fits within the truncation limit.
    /// </summary>
    private const string BarelyOverLimitWithHeadersPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : POST /api/orders
        note left
        <color:gray>[Content-Type=application/json]
        <color:gray>[Authorization=Bearer token123]
        <color:gray>[Accept=application/json]
        <color:gray>[X-Request-Id=req-001]
        <color:gray>[X-Correlation-Id=corr-abc]

        Line 1
        Line 2
        Line 3
        Line 4
        Line 5
        Line 6
        Line 7
        Line 8
        Line 9
        Line 10
        Line 11
        Line 12
        Line 13
        Line 14
        Line 15
        Line 16
        Line 17
        Line 18
        Line 19
        Line 20
        Line 21
        Line 22
        Line 23
        Line 24
        Line 25
        Line 26
        Line 27
        Line 28
        Line 29
        Line 30
        Line 31
        Line 32
        Line 33
        Line 34
        Line 35
        Line 36
        Line 37
        Line 38
        end note
        svc -> db : INSERT INTO Orders
        db --> svc : OK
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithBarelyOverLimitHeaders(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", BarelyOverLimitWithHeadersPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source with one long note (45+ body lines, exceeds default truncation of 40)
    /// AND &lt;color:gray&gt; header lines, plus one short note (4 body lines + headers).
    /// Used by tests that verify note hover button behavior after hiding headers.
    /// </summary>
    private const string LongNoteWithHeadersPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : POST /api/orders
        note left
        <color:gray>Content-Type: application/json
        <color:gray>Authorization: Bearer token123

        Line 1
        Line 2
        Line 3
        Line 4
        Line 5
        Line 6
        Line 7
        Line 8
        Line 9
        Line 10
        Line 11
        Line 12
        Line 13
        Line 14
        Line 15
        Line 16
        Line 17
        Line 18
        Line 19
        Line 20
        Line 21
        Line 22
        Line 23
        Line 24
        Line 25
        Line 26
        Line 27
        Line 28
        Line 29
        Line 30
        Line 31
        Line 32
        Line 33
        Line 34
        Line 35
        Line 36
        Line 37
        Line 38
        Line 39
        Line 40
        Line 41
        Line 42
        Line 43
        Line 44
        Line 45
        end note
        svc -> db : INSERT INTO Orders
        note left
        <color:gray>Content-Type: text/plain
        <color:gray>X-Request-Id: abc-123

        Short note line 1
        Short note line 2
        Short note line 3
        Short note line 4
        end note
        db --> svc : OK
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithLongNotesAndHeaders(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        // Only one diagram to avoid ambiguity in Playwright selectors
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", LongNoteWithHeadersPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source where note 1 is ALL gray headers (no body content) and note 2
    /// has actual body content. When headers are hidden, note 1 becomes empty, testing
    /// the index alignment between SVG groups and source note blocks.
    /// </summary>
    private const string HeaderOnlyNotePlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : GET /api/orders
        note left
        <color:gray>Authorization: Bearer token123
        <color:gray>Accept: application/json
        <color:gray>X-Request-Id: req-001
        end note
        svc -> db : SELECT * FROM Orders
        note left
        <color:gray>Content-Type: text/plain

        SELECT Id, Name, Status
        FROM Orders
        WHERE Active = 1
        end note
        db --> svc : OK
        note right
        <color:gray>Content-Type: application/json

        [{"id":1,"name":"Order A"},{"id":2,"name":"Order B"}]
        end note
        svc --> caller : 200 OK
        @enduml
        """;

    /// <summary>
    /// PlantUML source with multiple header-only notes interspersed with content notes.
    /// Notes 1 and 3 are all-headers; notes 2 and 4 have body content.
    /// </summary>
    private const string MultipleHeaderOnlyNotesPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "PaymentService" as pay
        participant "Database" as db

        caller -> svc : GET /api/orders
        note left
        <color:gray>Authorization: Bearer token123
        <color:gray>Accept: application/json
        end note
        svc -> db : SELECT * FROM Orders
        note left
        <color:gray>X-DB-Hint: readonly

        SELECT Id, Name FROM Orders
        end note
        db --> svc : OK
        svc -> pay : POST /api/charge
        note left
        <color:gray>Content-Type: application/json
        <color:gray>X-Idempotency-Key: abc-123
        end note
        pay --> svc : 200 OK
        note right
        <color:gray>Content-Type: application/json

        {"chargeId":"ch_001","status":"succeeded"}
        end note
        svc --> caller : 200 OK
        @enduml
        """;

    public static string GenerateReportWithHeaderOnlyNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", HeaderOnlyNotePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithMultipleHeaderOnlyNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", MultipleHeaderOnlyNotesPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with TWO scenarios, each having assertion notes (hnote across assertionNote)
    /// plus regular notes. Used to test that Show/Hide assertions on one scenario
    /// does not affect the other scenario.
    /// </summary>
    public static string GenerateReportWithAssertionNotes(string tempDir, string outputDir, string fileName)
    {
        // Use a minimal feature set with exactly 2 happy-path scenarios that sort adjacently,
        // ensuring they appear as details.scenario:nth(0) and nth(1) in the DOM.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Assertion Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "a1", DisplayName = "Alpha scenario", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "alpha precondition", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "alpha result", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "a2", DisplayName = "Beta scenario", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "beta precondition", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "beta result", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("a1", "", AssertionNotePlantUmlSource("Scenario1")),
            new DiagramAsCode("a2", "", AssertionNotePlantUmlSource("Scenario2"))
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static string AssertionNotePlantUmlSource(string label) => $$"""
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : POST /api/orders
        note left
        Content-Type: application/json
        {"item":"Widget","qty":2}
        end note
        svc -> db : INSERT INTO Orders
        db --> svc : OK

        hnote across <<assertionNote>> #d4edda
        ✓ {{label}} status code should be created
        end note
        '__^*__:OrderTests.cs:L42

        svc --> caller : 201 Created

        hnote across <<assertionNote>> #d4edda
        ✓ {{label}} response id should not be empty
        end note
        '__^*__:OrderTests.cs:L45

        @enduml
        """;

    /// <summary>
    /// PlantUML source with a regular note containing multiple lines that will render as
    /// separate SVG text elements. Used to test that "Copy Highlighted Text" normalizes
    /// text against the original note source (removing artificial newlines from word-wrap).
    /// </summary>
    private const string LongLineNotePlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        caller -> svc : POST /api/orders
        note left
        <color:gray>[Content-Type=application/json]
        <color:gray>[Authorization=Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyMSJ9.abc123]

        {"orderId":"ord-12345","customerName":"John Smith","items":[{"sku":"WIDGET-001","quantity":2,"price":29.99}],"shippingAddress":"123 Main Street, Springfield, IL 62701"}
        end note
        svc -> db : INSERT INTO Orders
        db --> svc : OK
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithLongLineNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", LongLineNotePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source with entity, queue, and database participant types that generate
    /// SVG path+text groups similar to notes. Tests that findNoteGroups correctly
    /// distinguishes notes from participant shapes using fold-triangle detection.
    /// Mirrors the BreakfastProvider report structure where clicking minus on one note
    /// was collapsing a different note due to index misalignment.
    /// </summary>
    private const string MixedParticipantNotesPlantUmlSource = """
        @startuml
        !pragma teoz true
        <style>
         .eventNote {
             BackgroundColor #cfecf7
             FontSize 11
             RoundCorner 10
         }
        </style>
        skinparam wrapWidth 800
        autonumber 1

        actor "Caller" as caller
        entity "Service A" as svcA
        entity "Service B" as svcB
        queue "Message Broker" as broker
        database "Database" as db

        caller -[#438DD5]> svcA: GET /api/items
        note left
        <color:gray>[traceparent=00-abc-def-00]
        <color:gray>[X-Request-Id=req-001]
        end note
        svcA -[#438DD5]> svcB: GET /api/data
        note left
        <color:gray>[X-Request-Id=req-001]
        <color:gray>[X-Correlation-Id=cor-001]
        end note
        svcB -[#438DD5]-> svcA: OK
        note right
        <color:gray>[Content-Type=application/json]

        {
          "data": "value1"
        }
        end note
        broker -[#9B59B6]> svcA: Consume: /events
        note<<eventNote>> right
        {
          "eventId": "evt-001",
          "type": "ItemCreated",
          "payload": {
            "id": "item-001",
            "name": "Test Item"
          }
        }
        end note
        svcA -[#9B59B6]-> broker: Ack
        svcA -[#E74C3C]> db: Insert: /Items
        db -[#E74C3C]-> svcA: OK
        svcA -[#438DD5]-> caller: Created
        note right
        <color:gray>[X-Correlation-Id=cor-001]

        {
          "id": "item-001",
          "status": "created"
        }
        end note
        @enduml
        """;

    public static string GenerateReportWithMixedParticipantNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", MixedParticipantNotesPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with a step that has a TableRef toggle button and a tabular parameter table.
    /// Used for testing the ▴ toggle button click functionality.
    /// </summary>
    public static string GenerateReportWithStepTableToggle(string tempDir, string outputDir, string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Step Toggle Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "st1", DisplayName = "Step with table toggle", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given",
                                Text = "a muffin recipe",
                                Status = ExecutionResult.Passed,
                                TextSegments =
                                [
                                    StepTextSegment.Literal("a muffin "),
                                    StepTextSegment.TableRef("recipe")
                                ],
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "recipe",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Flour", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Classic", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("Plain Flour", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "I bake the muffin", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "it should be delicious", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("st1", "", PlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string StepDelimiterWithNotesPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Database" as db

        hnote across <<stepDelimiter>> #black:<color:white>Given the system is running
        caller -> svc : POST /api/orders
        note left
        Content-Type: application/json
        {"item":"Widget","qty":2}
        end note
        hnote across <<stepDelimiter>> #black:<color:white>When I create an order
        svc -> db : INSERT INTO Orders
        note left
        INSERT INTO Orders (Item, Qty)
        VALUES ('Widget', 2)
        end note
        db --> svc : OK
        svc --> caller : 201 Created
        note left
        {"id":"abc-123","status":"created"}
        end note
        @enduml
        """;

    /// <summary>
    /// A diagram whose step bars carry a Gherkin data table and a doc string, built with the real
    /// emitter (<see cref="Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml"/>) so the
    /// fixture cannot drift from what step tracking draws, plus the <c>.stepBody</c> style
    /// <c>PlantUmlCreator</c> injects for it, a legacy bar, and a payload note for the notes machinery.
    /// </summary>
    public static string GenerateReportWithStepTableBars(string tempDir, string outputDir, string fileName)
    {
        var tableBar = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(
            "Given", "the following muffins exist",
            table: [["name", "price"], ["Blueberry", "3.50"], ["Double Chocolate", "4.00"]]);
        var docStringBar = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(
            "When", "the order payload is submitted",
            docString: "{ \"muffin\": \"Blueberry\",\n  \"qty\": 2 }");

        var source = $$"""
            @startuml
            <style>
             .stepBody {
                 BackgroundColor black
                 FontColor white
                 LineColor white
             }
            </style>
            actor "Caller" as caller
            participant "OrderService" as svc

            {{tableBar}}
            caller -> svc : POST /api/orders
            note left
            Content-Type: application/json
            {"item":"Blueberry","qty":2}
            end note
            {{docStringBar}}
            hnote across <<stepDelimiter>> #black:<color:white>Then the order is confirmed
            svc --> caller : 201 Created
            @enduml
            """;

        var (features, _) = CreateTestData();
        var diagrams = new[] { new DiagramAsCode("t1", "", source) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithStepDelimitersAndNotes(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", StepDelimiterWithNotesPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with Gherkin Rule grouping:
    /// - Feature 1: 1 scenario outside any rule, then 2 rules with 2 scenarios each
    /// - Feature 2: 1 rule with 2 scenarios (no scenarios outside rules)
    /// </summary>
    public static string GenerateReportWithRules(string tempDir, string outputDir, string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Management Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "r1", DisplayName = "Health check returns OK", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the service is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I call the health endpoint", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "I receive 200 OK", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "r2", DisplayName = "Create order with valid data", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                        Rule = "Valid Order Creation",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have a valid order payload", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I submit the order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the order is created", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "r3", DisplayName = "Create order with express shipping", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(3),
                        Rule = "Valid Order Creation",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have an express order payload", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I submit the order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the order is created with express flag", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "r4", DisplayName = "Missing required field returns 400", IsHappyPath = false,
                        Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(1),
                        Rule = "Invalid Order Handling",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have an order missing the name field", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I submit the order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "I receive a 400 Bad Request", Status = ExecutionResult.Failed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "r5", DisplayName = "Invalid quantity returns 400", IsHappyPath = false,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Rule = "Invalid Order Handling",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have an order with negative quantity", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I submit the order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "I receive a 400 Bad Request", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            },
            new Feature
            {
                DisplayName = "Payment Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "r6", DisplayName = "Charge card successfully", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                        Rule = "Card Payments",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have a valid card", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I charge the card", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the payment succeeds", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "r7", DisplayName = "Declined card returns error", IsHappyPath = false,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Rule = "Card Payments",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have a declined card", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I charge the card", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the payment is declined", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = Array.Empty<DiagramAsCode>();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithBackground(string tempDir, string outputDir, string fileName, bool separateBackgroundSteps = false)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "User Registration Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "bg1", DisplayName = "Register with valid email", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                        BackgroundSteps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the registration service is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "And", Text = "the database is available", Status = ExecutionResult.Passed }
                        ],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "I register with a valid email", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "my account is created", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "bg2", DisplayName = "Register with duplicate email", IsHappyPath = false,
                        Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(3),
                        BackgroundSteps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the registration service is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "And", Text = "the database is available", Status = ExecutionResult.Passed }
                        ],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "I register with a duplicate email", Status = ExecutionResult.Failed },
                            new ScenarioStep { Keyword = "Then", Text = "I receive a conflict error", Status = ExecutionResult.Skipped }
                        ]
                    },
                    new Scenario
                    {
                        Id = "bg3", DisplayName = "View profile without background", IsHappyPath = false,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I am logged in", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I view my profile", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "my details are shown", Status = ExecutionResult.Passed }
                        ]
                    },
                    // The Given/Given seam: a background ending on Given followed by a scenario opening on
                    // Given is what keyword collapsing exists for, and the three above never exercise it.
                    new Scenario
                    {
                        Id = "bg4", DisplayName = "Withdraw a pending registration", IsHappyPath = false,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                        BackgroundSteps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the registration service is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "And", Text = "the database is available", Status = ExecutionResult.Passed }
                        ],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a pending registration exists", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I withdraw it", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the registration is gone", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = Array.Empty<DiagramAsCode>();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: separateBackgroundSteps);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>The same fixture with the opt-in <c>Background Steps</c> disclosure instead of one combined list.</summary>
    public static string GenerateReportWithSeparatedBackground(string tempDir, string outputDir, string fileName) =>
        GenerateReportWithBackground(tempDir, outputDir, fileName, separateBackgroundSteps: true);

    public static string GenerateReportWithAttachments(string tempDir, string outputDir, string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Upload Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "att1", DisplayName = "Upload with screenshot", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "I upload a file", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("screenshot.png", "files/screenshot.png")]
                            },
                            new ScenarioStep { Keyword = "Then", Text = "the file is stored", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "att2", DisplayName = "Upload with multiple attachments", IsHappyPath = false,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(3),
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "I upload multiple files", Status = ExecutionResult.Passed,
                                Attachments =
                                [
                                    new FileAttachment("log.txt", "files/log.txt"),
                                    new FileAttachment("trace.json", "files/trace.json")
                                ]
                            },
                            new ScenarioStep { Keyword = "Then", Text = "all files are stored", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "att3", DisplayName = "Step without attachments", IsHappyPath = false,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "I do nothing special", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "no attachments exist", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = Array.Empty<DiagramAsCode>();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithCopiedAttachment(string tempDir, string outputDir, string fileName)
    {
        // Create a real source file with an absolute path
        var sourceFile = Path.Combine(tempDir, "openapi.json");
        File.WriteAllText(sourceFile, "{\"openapi\":\"3.0.0\"}");

        var features = new[]
        {
            new Feature
            {
                DisplayName = "API Spec Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "copy1", DisplayName = "Spec is written to disk", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "the openapi spec is written to disk",
                                Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("OpenAPI Spec", sourceFile)]
                            }
                        ]
                    }
                ]
            }
        };

        // Run the copy logic (rewrites RelativePath to attachments/openapi.json)
        var reportsDir = Path.Combine(tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);
        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        var diagrams = Array.Empty<DiagramAsCode>();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    public static string GenerateReportWithComplexInlineParams(string tempDir, string outputDir, string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Complex Param Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "cp1", DisplayName = "Small complex param renders inline", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given",
                                Text = "a small recipe",
                                Status = ExecutionResult.Passed,
                                TextSegments =
                                [
                                    StepTextSegment.Literal("a small "),
                                    StepTextSegment.TableRef("recipe")
                                ],
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "recipe",
                                        Kind = StepParameterKind.Inline,
                                        InlineValue = new InlineParameterValue(
                                            "MuffinRecipeTestData { Name = Classic, Flour = Plain Flour }",
                                            null, VerificationStatus.NotApplicable)
                                    }
                                ]
                            },
                            new ScenarioStep
                            {
                                Keyword = "When",
                                Text = "I apply a large config",
                                Status = ExecutionResult.Passed,
                                TextSegments =
                                [
                                    StepTextSegment.Literal("I apply a large "),
                                    StepTextSegment.TableRef("config")
                                ],
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "config",
                                        Kind = StepParameterKind.Inline,
                                        InlineValue = new InlineParameterValue(
                                            "AppConfig { Host = localhost, Port = 8080, Timeout = 30, RetryCount = 3, Debug = True }",
                                            null, VerificationStatus.NotApplicable)
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "Then", Text = "it should succeed", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("cp1", "", PlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string DatabaseParticipantPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        database "CosmosDB" as cosmosdb #E74C3C

        caller -> svc : POST /api/orders
        note left
        Content-Type: application/json
        {"item":"Widget","qty":2}
        end note
        svc -[#E74C3C]> cosmosdb: CreateItemAsync
        note left
        {"id":"abc","item":"Widget","qty":2}
        end note
        cosmosdb -[#E74C3C]-> svc: 201 Created
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithDatabaseParticipant(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", DatabaseParticipantPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string WideDatabaseParticipantPlantUmlSource = """
        @startuml
        !pragma teoz true
        <style>
         .eventNote {
             BackgroundColor #cfecf7
             FontSize 11
             RoundCorner 10
         }
        </style>
        <style>
         .assertionNote {
             FontSize 11
             RoundCorner 5
         }
        </style>
        skinparam wrapWidth 800
        autonumber 1
        
        actor "Caller" as caller
        entity "Breakfast Provider" as breakfastProvider
        database "Spanner" as spanner
        
        
        hnote across <<stepDelimiter>> #black:<color:white>Given a valid customer preference request
        
        
        hnote across <<stepDelimiter>> #black:<color:white>When the customer preferences are saved
        
        caller -[#438DD5]> breakfastProvider: [[#iflow-67a2a680-5cb1-4b1e-a145-e5046cd095af PUT: /customer-preferences/d37d5aba2a244807b7fe008d01f6ba0f]]
        note left
        <color:gray>[traceparent=00-22c760ca8f8c3943bc8a2430baf4bb99-1ce90250d46976e6-00]
        
        {
          "customerId": "d37d5aba2a244807b7fe008d01f6ba0f",
          "customerName": "Customer-5cb476e1634b4b6e885875b3ee037a3e",
          "preferredMilkType": "Oat",
          "likesExtraToppings": true,
          "favouriteItem": "Blueberry Pancakes"
        }
        end note
        breakfastProvider -[#E74C3C]> spanner: [[#iflow-81f4874d-c8e9-4174-9f48-9f50155ac238 InsertOrUpdate: /breakfast-db/CustomerPreferences]]
        note<<eventNote>> left
        UPSERT CustomerPreferences
        end note
        spanner -[#E74C3C]-> breakfastProvider: 
        breakfastProvider -[#438DD5]-> caller: OK
        note right
        {
          "customerId": "d37d5aba2a244807b7fe008d01f6ba0f",
          "customerName": "Customer-5cb476e1634b4b6e885875b3ee037a3e",
          "preferredMilkType": "Oat",
          "likesExtraToppings": true,
          "favouriteItem": "Blueberry Pancakes",
          "updatedAt": "2026-05-14T14:38:15.9062722Z"
        }
        end note
        
        hnote across <<stepDelimiter>> #black:<color:white>Then the preference response should contain the saved preferences
        
        
        hnote across <<assertionNote>> #d4edda
        ✓ Put steps response message status code should be OK
        end note
        
        
        hnote across <<assertionNote>> #d4edda
        ✓ Response content is valid json should be true
        end note
        
        
        hnote across <<assertionNote>> #d4edda
        ✓ Put steps response preferred milk type should be "Oat"
        end note
        
        
        hnote across <<assertionNote>> #d4edda
        ✓ Put steps response favourite item should be "Blueberry Pancakes"
        end note
        
        @enduml
        """;

    /// <summary>
    /// The wide-database fixture (JSON note + gray header, step bars, assertion notes, database
    /// participant — every toolbar gate ON) generated with configured toggle defaults resolved
    /// through the real options record. The workhorse fixture for ToggleDefaultsTests.
    /// </summary>
    public static string GenerateToggleDefaultsReport(string tempDir, string outputDir, string fileName,
        Action<ReportConfigurationOptions> configure, string? plantUmlSource = null)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", plantUmlSource ?? WideDatabaseParticipantPlantUmlSource)
        };

        var options = new ReportConfigurationOptions();
        configure(options);

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>The 45-line long-note fixture with configured toggle defaults — for the
    /// Details radio and truncate-lines zero-click facts.</summary>
    public static string GenerateLongNoteToggleDefaultsReport(string tempDir, string outputDir, string fileName,
        Action<ReportConfigurationOptions> configure) =>
        GenerateToggleDefaultsReport(tempDir, outputDir, fileName, configure, LongNotePlantUmlSource);

    /// <summary>
    /// A sequence diagram plus whole-test-flow activity and flame views for the same scenario
    /// (three diagram-type tabs), with configured toggle defaults — for the DiagramTab facts.
    /// </summary>
    public static string GenerateWholeTestFlowToggleDefaultsReport(string tempDir, string outputDir, string fileName,
        Action<ReportConfigurationOptions> configure)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", WideDatabaseParticipantPlantUmlSource)
        };

        using var activitySource = new System.Diagnostics.ActivitySource("Kronikol.Tests.ToggleDefaults.E2E");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        System.Diagnostics.Activity.Current = null;
        var baseTime = DateTime.UtcNow;

        var root = activitySource.StartActivity("HTTP PUT /customer-preferences", System.Diagnostics.ActivityKind.Server)!;
        root.SetStartTime(baseTime);
        root.SetEndTime(baseTime.AddMilliseconds(500));
        var rootCtx = new System.Diagnostics.ActivityContext(root.TraceId, root.SpanId, System.Diagnostics.ActivityTraceFlags.Recorded);
        var child = activitySource.StartActivity("Spanner: InsertOrUpdate", System.Diagnostics.ActivityKind.Internal, rootCtx)!;
        child.SetStartTime(baseTime.AddMilliseconds(20));
        child.SetEndTime(baseTime.AddMilliseconds(400));

        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-t1"] = new(
                Guid.Empty, RequestResponseType.Request, "t1",
                baseTime, baseTime.AddMilliseconds(500), [root, child])
        };

        var options = new ReportConfigurationOptions();
        configure(options);

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));

        child.Dispose();
        root.Dispose();

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string ToggleDefaultsComponentDiagramSource = """
        @startuml
        rectangle "Caller" as caller
        rectangle "OrderService" as svc
        caller --> svc
        @enduml
        """;

    /// <summary>
    /// A report with both toolbar panels available — scenario durations (timeline) and an embedded
    /// component diagram — with configured toggle defaults, for the panel-visibility facts.
    /// </summary>
    public static string GenerateComponentTimelineToggleDefaultsReport(string tempDir, string outputDir, string fileName,
        Action<ReportConfigurationOptions> configure)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", WideDatabaseParticipantPlantUmlSource)
        };

        var options = new ReportConfigurationOptions();
        configure(options);

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            componentDiagramPlantUml: ToggleDefaultsComponentDiagramSource,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Both report shapes (Specifications + TestRunReport) generated from ONE options record,
    /// mirroring the two real GenerateReports call sites — for the inheritance facts. Uses
    /// all-passing features so the Specifications shape (generateBlankOnFailedTests) has content.
    /// </summary>
    public static (string SpecificationsUri, string TestRunUri) GenerateBothReportsWithToggleDefaults(
        string tempDir, string outputDir, string baseName, Action<ReportConfigurationOptions> configure)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Inheritance Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Inherited scenario", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a valid customer preference request", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the preferences are saved", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };
        var diagrams = new[] { new DiagramAsCode("t1", "", WideDatabaseParticipantPlantUmlSource) };

        var options = new ReportConfigurationOptions();
        configure(options);

        var specPath = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            Kronikol.Stylesheets.VioletThemeStyleSheet, Path.Combine(tempDir, $"{baseName}_Specifications.html"),
            "Service Specifications", false,
            generateBlankOnFailedTests: true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: true));

        var testRunPath = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, $"{baseName}_TestRunReport.html"), "Test Run Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));

        File.Copy(specPath, Path.Combine(outputDir, $"{baseName}_Specifications.html"), true);
        File.Copy(testRunPath, Path.Combine(outputDir, $"{baseName}_TestRunReport.html"), true);
        return (new Uri(specPath).AbsoluteUri, new Uri(testRunPath).AbsoluteUri);
    }

    public static string GenerateReportWithWideDatabaseParticipant(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", WideDatabaseParticipantPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string CollectionsParticipantPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        collections "Redis" as redis #F39C12

        caller -> svc : POST /api/orders
        note left
        Content-Type: application/json
        {"item":"Widget","qty":2}
        end note
        svc -[#F39C12]> redis: GET cache:orders
        note left
        key=orders:widget
        end note
        redis -[#F39C12]-> svc: (nil)
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithCollectionsParticipant(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", CollectionsParticipantPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private const string MixedDatabaseCollectionsPlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        database "CosmosDB" as cosmosdb #E74C3C
        collections "Redis" as redis #F39C12

        caller -> svc : POST /api/orders
        svc -[#F39C12]> redis: GET cache:orders
        redis -[#F39C12]-> svc: (nil)
        svc -[#E74C3C]> cosmosdb: CreateItemAsync
        cosmosdb -[#E74C3C]-> svc: 201 Created
        svc -[#F39C12]> redis: SET cache:orders
        redis -[#F39C12]-> svc: OK
        svc --> caller : 201 Created
        @enduml
        """;

    public static string GenerateReportWithMixedDatabaseCollections(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", MixedDatabaseCollectionsPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with a single diagram that exceeds the height threshold (12000px estimated)
    /// by having many arrow pairs, triggering client-side fragment splitting via splitDiagramSource.
    /// </summary>
    public static string GenerateReportWithFragmentedDiagram(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        // Generate many request-response pairs to exceed _maxDiagramHeight (12000px)
        // Each arrow ≈ 45px, so 300 arrows = 13500px > 12000px threshold
        var arrows = string.Join("\n", Enumerable.Range(1, 150).Select(i => $"""
            caller -> svc : GET /api/item/{i}
            svc --> caller : OK
            """));

        var source = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 1
            actor "Caller" as caller
            entity "Service" as svc
            caller -> svc : POST /api/data
            note left
            <color:gray>[traceparent=00-abc-123-00]

            {
              "action": "create",
              "item": "widget"
            }
            end note
            svc --> caller : OK
            note right
            {
              "status": "success",
              "id": "test-123"
            }
            end note
            {{arrows}}
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source modelled on the user-reported bug: database participant with
    /// step delimiters, assertion notes, and a database response note (n=1) that gets
    /// stripped when databases are hidden — causing note-index mismatch.
    /// Original notes: 0=request (left), 1=db-response (right "n=1"), 2=api-response (right JSON).
    /// After hiding databases: 0=request, 1=api-response → index shift.
    /// </summary>
    private const string DatabaseStepNoteCollapseSource = """
        @startuml
        !pragma teoz true
        <style>
         .assertionNote {
             FontSize 11
             RoundCorner 5
         }
        </style>
        skinparam wrapWidth 800
        autonumber 1

        actor "Caller" as caller
        entity "Breakfast Provider" as breakfastProvider
        database "MongoDB" as mongoDB

        hnote across <<stepDelimiter>> #black:<color:white>GIVEN A valid chef note request

        hnote across <<stepDelimiter>> #black:<color:white>WHEN The note is submitted

        caller -[#438DD5]> breakfastProvider: POST /chef-notes
        note left
        {
          "recipeName": "Recipe-abc",
          "chefName": "Chef-xyz",
          "noteText": "Remember to fold the batter gently.",
          "category": "Technique",
          "requestPriority": "urgent"
        }
        end note
        breakfastProvider -[#E74C3C]> mongoDB: Insert chef_notes
        mongoDB -[#E74C3C]-> breakfastProvider: OK
        note right
        n=1
        end note
        breakfastProvider -[#438DD5]-> caller: Created
        note right
        {
          "noteId": "abc-123",
          "recipeName": "Recipe-abc",
          "chefName": "Chef-xyz",
          "noteText": "Remember to fold the batter gently.",
          "category": "Technique",
          "createdAt": "2026-05-17T16:00:50Z"
        }
        end note

        hnote across <<stepDelimiter>> #black:<color:white>THEN The response should contain the created note

        hnote across <<assertionNote>> #d4edda
        ✓ Status code should be Created
        end note

        hnote across <<assertionNote>> #d4edda
        ✓ Response recipe name should be correct
        end note

        @enduml
        """;

    public static string GenerateReportWithDatabaseStepNoteCollapse(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", DatabaseStepNoteCollapseSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// PlantUML source using colored arrow syntax (-[#color]> and -[#color]->) with
    /// step delimiters and assertion notes, modelled on real-world ReqNRoll test output.
    /// Generates enough interactions to exceed _maxDiagramHeight (12000px) and trigger
    /// client-side fragment splitting via splitDiagramSource.
    /// </summary>
    private static string ColoredArrowPlantUmlSource
    {
        get
        {
            // Build 100 colored request-response pairs (200 arrows ≈ 9000px)
            // plus step delimiters and assertion notes to push past 12000px
            var interactions = new System.Text.StringBuilder();
            for (int i = 1; i <= 100; i++)
            {
                interactions.AppendLine(
                    $"\nhnote across <<stepDelimiter>> #black:<color:white>Step {i}\n" +
                    $"\ncaller -[#438DD5]> svc : [[#iflow-{i} GET: /api/item/{i}]]\n" +
                    $"note left\n<color:gray>[traceparent=00-abc-{i.ToString("D3")}-00]\nend note\n" +
                    $"svc -[#438DD5]-> caller : OK\n" +
                    "note right\n" +
                    "{\n" +
                    $"  \"id\": {i},\n" +
                    $"  \"name\": \"Item {i}\"\n" +
                    "}\nend note\n" +
                    "\nhnote across <<assertionNote>> #d4edda\n" +
                    $"✓ Response status should be OK for item {i}\nend note\n");
            }

            return """
                @startuml
                !pragma teoz true
                <style>
                 .eventNote {
                     BackgroundColor #cfecf7
                     FontSize 11
                     RoundCorner 10
                 }
                </style>
                <style>
                 .assertionNote {
                     FontSize 11
                     RoundCorner 5
                 }
                </style>
                skinparam wrapWidth 800
                autonumber 1

                actor "Caller" as caller
                entity "Service" as svc
                database "Database" as db

                """ + interactions + "\n@enduml\n";
        }
    }

    public static string GenerateReportWithColoredArrows(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", ColoredArrowPlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static string LargeNotePlantUmlSource
    {
        get
        {
            // Build 50 colored request-response pairs with small notes (~7650px estimated height)
            // then one pair with a very large note (>15000 chars) that triggers chunkLargeNotes.
            // Combined, Part 0 of the chunked split exceeds 12000px and gets height-split.
            // This reproduces the bug where Part 0 (no @enduml) has its last 'end note'
            // excluded by parseDiagramStructure, leaving an unclosed note in the fragment.
            var interactions = new System.Text.StringBuilder();
            for (int i = 1; i <= 50; i++)
            {
                interactions.AppendLine(
                    $"caller -[#438DD5]> svc : [[#iflow-{i} GET: /api/item/{i}]]\n" +
                    "note left\n" +
                    $"<color:gray>[traceparent=00-abc-{i:D3}-00]\n" +
                    "end note\n" +
                    "svc -[#438DD5]-> caller : OK\n" +
                    "note right\n" +
                    $"{{\"id\":{i},\"name\":\"Item {i}\"}}\n" +
                    "end note");
            }

            // Add an arrow pair with a very large note (>15000 chars)
            interactions.AppendLine("svc -[#E74C3C]> db : Query /data");
            interactions.AppendLine("note left\nSELECT * FROM items\nend note");
            interactions.AppendLine("db -[#E74C3C]-> svc : OK");
            interactions.AppendLine("note right");
            interactions.AppendLine("{");
            for (int j = 0; j < 500; j++)
            {
                interactions.AppendLine($"  \"item_{j:D4}\": \"value_{j:D4}_xxxxxxxxxxxx\",");
            }
            interactions.AppendLine("}");
            interactions.AppendLine("end note");

            // One more arrow after the large note
            interactions.AppendLine("svc -[#438DD5]-> caller : OK");

            return """
                @startuml
                !pragma teoz true
                <style>
                 .eventNote {
                     BackgroundColor #cfecf7
                     FontSize 11
                     RoundCorner 10
                 }
                </style>
                <style>
                 .assertionNote {
                     FontSize 11
                     RoundCorner 5
                 }
                </style>
                skinparam wrapWidth 800
                autonumber 1

                actor "Caller" as caller
                entity "Service" as svc
                database "Database" as db

                """ + interactions + "\n@enduml\n";
        }
    }

    public static string GenerateReportWithLargeNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", LargeNotePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with a continuation diagram that includes database and collections
    /// participant types. Reproduces a real-world scenario where the continuation diagram has
    /// actor + entity + collections (Redis) + database (BigQuery) participants, which can cause
    /// findNoteGroups to misidentify participant shapes (especially database cylinders) as notes.
    /// </summary>
    public static string GenerateReportWithDatabaseContinuationSplit(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        var continuedContent = string.Join("\n",
            Enumerable.Range(1, 80).Select(i =>
                $"    \"field_{i}\": {{\"type\": \"string\", \"description\": \"Field {i} description\"}},"
            ));

        // Diagram 1: request with a large response note ending in "..Continued On Next Diagram.."
        var source1 = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 1
            actor "Caller" as caller
            entity "Data Insights API" as api
            collections "Redis" as redis
            database "BigQuery" as bq

            caller -[#438DD5]> api : POST /api/query
            note left
            <color:gray>[traceparent=00-abc-def-00]
            end note
            api -[#E74C3C]> bq : Query /data
            note left
            SELECT * FROM dataset.table
            end note
            bq -[#E74C3C]-> api : OK
            note right
            <color:gray>[X-Correlation-Id=test-456]

            {
              "configuration": {
                "jobType": "QUERY",
                "query": {
                  "query": "SELECT * FROM dataset.table WHERE date > '2026-01-01'",
                  "queryParameters": [
            {{continuedContent}}
              ..Continued On Next Diagram..
            end note
            @enduml
            """;

        // Diagram 2: continuation note with database + collections participants
        var source2 = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 5
            actor "Caller" as caller
            entity "Data Insights API" as api
            collections "Redis" as redis
            database "BigQuery" as bq

            bq -[#E74C3C]-> api : OK
            note right
            ..Continued From Previous Diagram..
            {{continuedContent}}
                }
              }
            }
            end note
            api -[#438DD5]-> caller : OK
            note right
            {
              "configuration": {
                "jobType": "QUERY",
                "status": "DONE"
              }
            }
            end note
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source1),
            new DiagramAsCode("t1", "", source2)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a single-container report where a large note (>15k chars) triggers
    /// client-side chunkLargeNotes splitting, with step delimiters and database/collections
    /// participants matching the real-world Data Insights API scenario.
    /// </summary>
    public static string GenerateReportWithChunkedDatabaseNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        var largeQuery = string.Concat(Enumerable.Repeat(
            "        SAFE_DIVIDE((t.total_cards - t.total_cards_lag), t.total_cards_lag) AS unique_customers_change,\n", 200));

        var queryParams = string.Join("\n",
            new[] { "LocationId", "reportDate", "comparisonPeriod", "cadence", "ChartWeeksCount" }
                .Select(name =>
                    "    {\n" +
                    $"      \"name\": \"{name}\",\n" +
                    "      \"parameterType\": {\n" +
                    "        \"type\": \"STRING\"\n" +
                    "      },\n" +
                    "      \"parameterValue\": {\n" +
                    "        \"value\": \"test-value\"\n" +
                    "      }\n" +
                    "    },"));

        var source = $$"""
            @startuml
            !pragma teoz true
            <style>
             .assertionNote {
                 FontSize 11
                 RoundCorner 5
             }
            </style>
            skinparam wrapWidth 800
            autonumber 1

            actor "Caller" as caller
            entity "Data Insights API" as dataInsightsAPI
            collections "Redis" as redis
            database "BigQuery" as bigQuery


            hnote across <<stepDelimiter>> #black:<color:white>Given I use a location id "756152205962546"


            hnote across <<stepDelimiter>> #black:<color:white>When I call the insights data endpoint

            caller -[#438DD5]> dataInsightsAPI: POST /api/data-products/insights
            note left
            <color:gray>[traceparent=00-abc-def-00]

            {
              "context": {
                "reportDate": "2025-11-10"
              }
            }
            end note
            dataInsightsAPI -[#F39C12]> redis: Get cache
            redis -[#F39C12]-> dataInsightsAPI: OK
            dataInsightsAPI -[#E74C3C]> bigQuery: Query /data
            note left
            {
              "configuration": {
                "query": {
                  "parameterMode": "named",
                  "query": "SELECT
            {{largeQuery}}
                  FROM `data.transactions` t;",
                  "queryParameters": [
            {{queryParams}}
                  ],
                  "useLegacySql": false
                }
              },
              "jobReference": {
                "jobId": "job_33daedff",
                "projectId": "data-prod"
              }
            }
            end note
            bigQuery -[#E74C3C]-> dataInsightsAPI: OK
            note right
            <color:gray>[Date=Fri, 05 Jun 2026 15:23:43 GMT]

            {
              "status": {
                "state": "DONE"
              }
            }
            end note
            dataInsightsAPI -[#438DD5]-> caller: OK
            note right
            {
              "metrics": {},
              "charts": {}
            }
            end note

            hnote across <<stepDelimiter>> #black:<color:white>Then the response should be successful

            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Generates a report with a note large enough to span 3+ client-side fragments,
    /// plus additional notes after it. This exercises the noteIndexOffset continuation
    /// overcounting bug: when fragment 1 has a continuation, its note count inflates
    /// the offset for fragment 2, causing out-of-bounds access.
    /// </summary>
    public static string GenerateReportWithThreeFragmentContinuation(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        // A very large query that forces the note to span 3+ fragments
        var hugeQuery = string.Concat(Enumerable.Repeat(
            "        SAFE_DIVIDE((t.total_cards - t.total_cards_lag), t.total_cards_lag) AS metric_change,\n", 500));

        var source = $$"""
            @startuml
            !pragma teoz true
            skinparam wrapWidth 800
            autonumber 1

            actor "Caller" as caller
            entity "API" as api
            database "BigQuery" as bq

            caller -[#438DD5]> api: POST /api/data
            note left
            {
              "context": { "reportDate": "2025-11-10" }
            }
            end note
            api -[#E74C3C]> bq: Query /data
            note left
            {
              "configuration": {
                "query": {
                  "query": "SELECT
            {{hugeQuery}}
                  FROM `data.transactions` t;"
                }
              }
            }
            end note
            bq -[#E74C3C]-> api: OK
            note right
            {
              "status": { "state": "DONE" },
              "rows": [
                { "f": [{ "v": "42" }] }
              ]
            }
            end note
            api -[#438DD5]-> caller: OK
            note right
            {
              "metrics": { "total": 42 },
              "charts": { "count": 1 }
            }
            end note

            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// A diagram whose first note carries a JSON payload exactly as the .NET
    /// generation pipeline emits it (gray header line, wire-verbatim \n
    /// escapes — 3.0.62 removed the backslash doubling — and an int64 beyond
    /// 2^53) and whose second note is plain text — the gold vector for the
    /// JSON ⇄ YAML note format toggle. Includes a step delimiter so the steps
    /// filter can be toggled against it.
    /// </summary>
    private const string JsonYamlNotePlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        hnote across <<stepDelimiter>> #black:<color:white>Given an order request
        caller -> svc : POST /api/orders
        note left
        <color:gray>[content-type=application/json]</color>

        {
          "id": 9007199254740993,
          "query": "SELECT o.id,\n       o.total\nFROM orders o"
        }
        end note
        svc --> caller : 200 OK
        note left
        plain text response body
        not json at all
        end note
        @enduml
        """;

    public static string GenerateReportWithJsonYamlNotes(string tempDir, string outputDir, string fileName,
        NotePayloadFormat notePayloadFormat = NotePayloadFormat.Json)
    {
        var (features, _) = CreateTestData();
        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", JsonYamlNotePlantUmlSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            notePayloadFormat: notePayloadFormat);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Two adjacent scenarios, each with one diagram holding a distinguishable
    /// YAML-eligible JSON note (alphaField / betaField) — for the bulk-format
    /// dropdown tests that need scenario isolation and report-wide sync.
    /// </summary>
    public static string GenerateReportWithJsonNotesInTwoScenarios(string tempDir, string outputDir, string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Format Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "f1", DisplayName = "Alpha scenario", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "alpha precondition", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "alpha result", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "f2", DisplayName = "Beta scenario", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "beta precondition", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "beta result", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("f1", "", JsonNotePlantUmlSource("alphaField")),
            new DiagramAsCode("f2", "", JsonNotePlantUmlSource("betaField"))
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static string JsonNotePlantUmlSource(string fieldName) => $$"""
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        caller -> svc : POST /api/orders
        note left
        <color:gray>[content-type=application/json]</color>

        {
          "{{fieldName}}": "SELECT o.id,\nFROM orders o"
        }
        end note
        svc --> caller : 200 OK
        @enduml
        """;

    /// <summary>
    /// A YAML-eligible JSON note whose YAML view produces creole escapes in
    /// the render splice (the URL's <c>//</c> becomes <c>~/~/</c>) AND unfolds
    /// past the 40-line truncation limit (45-column SQL) — the bug-exposing
    /// fixture for the copy-text paths on YAML notes. The shipped gold vector
    /// produces zero escapes, which is why the leak went unnoticed.
    /// </summary>
    public static string GenerateReportWithEscapingYamlNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        var sqlColumns = string.Join(@"\n", Enumerable.Range(1, 45).Select(i => $"  col_{i},"));
        var source = $$"""
            @startuml
            actor "Caller" as caller
            participant "OrderService" as svc

            caller -> svc : POST /api/orders
            note left
            <color:gray>[content-type=application/json]

            {
              "url": "https:~/~/example.com/orders",
              "query": "SELECT\n{{sqlColumns}}\n  o.total\nFROM orders o"
            }
            end note
            svc --> caller : 200 OK
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// A JSON note whose string value uses CRLF (\r\n) line breaks, carried
    /// verbatim in the note source — the shape every Windows-captured payload
    /// has. The YAML view must still unfold it into a block scalar.
    /// </summary>
    public static string GenerateReportWithCrlfJsonNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        const string source = """
            @startuml
            actor "Caller" as caller
            participant "OrderService" as svc

            caller -> svc : POST /api/orders
            note left
            <color:gray>[content-type=application/json]</color>

            {
              "query": "SELECT o.id,\r\n       o.total\r\nFROM orders o",
              "id": 42
            }
            end note
            svc --> caller : 200 OK
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// The user-reported BigQuery job shape (3.0.79): a JSON note whose query
    /// string was authored as "\n" + a C# raw string — a bare-LF body with a
    /// trailing space on the SELECT line and the raw string's closing
    /// indentation as an all-space tail. Either offender alone used to force
    /// the one-line quoted fallback in YAML view; both must now be stripped
    /// from the display so the block scalar unfolds.
    /// </summary>
    public static string GenerateReportWithBigQueryTrailingWhitespaceNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        const string source = """
            @startuml
            actor "Caller" as caller
            participant "BigQuery" as bq

            caller -> bq : POST /jobs/query
            note left
            <color:gray>[content-type=application/json]</color>

            {
              "query": "\n            -- daily revenue per location\n            SELECT \n                daily.location_id,\n                SUM(daily.total) AS revenue\n            FROM daily\n            GROUP BY daily.location_id\n            ",
              "useLegacySql": false
            }
            end note
            bq --> caller : 200 OK
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// A JSON note that is short in JSON view (one string value holding a
    /// 45-line SQL query as \n escapes) but unfolds past the 40-line truncation
    /// limit in YAML view — exercises "truncation and isLongNote operate on the
    /// active format's line count".
    /// </summary>
    public static string GenerateReportWithLongSqlJsonNote(string tempDir, string outputDir, string fileName)
    {
        var (features, _) = CreateTestData();

        var sqlColumns = string.Join(@"\n", Enumerable.Range(1, 45).Select(i => $"  col_{i},"));
        var source = $$"""
            @startuml
            actor "Caller" as caller
            participant "Db" as db

            caller -> db : query
            note left
            {
              "query": "SELECT\n{{sqlColumns}}\n  1\nFROM t"
            }
            end note
            db --> caller : OK
            @enduml
            """;

        var diagrams = new[]
        {
            new DiagramAsCode("t1", "", source)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
