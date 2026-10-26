using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

public class SearchDataAttributeTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchDataAttr.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    private static string[] ExtractDataSearchValues(string html)
    {
        return Regex.Matches(html, @"data-search=""([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }

    [Fact]
    public void Data_search_includes_step_text()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a valid order request with zuplotzky widget", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I submit the order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the order is confirmed", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        // The data-search attribute for scenario s1 should contain step text
        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("create order"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("zuplotzky widget", scenarioSearch);
        Assert.Contains("submit the order", scenarioSearch);
        Assert.Contains("order is confirmed", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_substep_text()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Nested steps", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a parent step", Status = ExecutionResult.Passed,
                                SubSteps =
                                [
                                    new ScenarioStep { Keyword = "And", Text = "a deeply nested frobnicator step", Status = ExecutionResult.Passed }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("nested steps"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("deeply nested frobnicator", scenarioSearch);
    }

    [Fact]
    public void Data_search_for_outline_includes_step_text()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$200" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the xylophonic account has funds", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$500" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the xylophonic account has funds", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var outlineSearch = searchValues.FirstOrDefault(v => v.Contains("withdraw"));
        Assert.NotNull(outlineSearch);
        Assert.Contains("xylophonic account", outlineSearch);
    }

    [Fact]
    public void Data_search_includes_feature_display_name()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Pancakes Creation",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Create a basic pancake", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "some batter", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("create a basic pancake"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("pancakes creation", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_feature_description()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Description = "This feature covers zorblatt integration",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test scenario", Result = ExecutionResult.Passed
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("test scenario"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("zorblatt integration", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_feature_labels()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Tags = ["xylophage", "frobnicate"],
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test scenario", Result = ExecutionResult.Passed
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("test scenario"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("xylophage", scenarioSearch);
        Assert.Contains("frobnicate", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_scenario_categories()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test scenario", Result = ExecutionResult.Passed,
                        Tags = ["zygomorphic", "plumbus"]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("test scenario"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("zygomorphic", scenarioSearch);
        Assert.Contains("plumbus", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_scenario_labels()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test scenario", Result = ExecutionResult.Passed,
                        Tags = ["quixotic", "barnacle"]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("test scenario"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("quixotic", scenarioSearch);
        Assert.Contains("barnacle", scenarioSearch);
    }

    [Fact]
    public void Data_search_for_parameterized_group_includes_feature_name()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Waffles Preparation",
                Description = "Covers all waffle scenarios",
                Tags = ["breakfast"],
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Make waffles with sugar", Result = ExecutionResult.Passed,
                        OutlineId = "make-waffles",
                        ExampleValues = new Dictionary<string, string> { ["Topping"] = "sugar" },
                        Tags = ["sweet"],
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "waffle iron is hot", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Make waffles with syrup", Result = ExecutionResult.Passed,
                        OutlineId = "make-waffles",
                        ExampleValues = new Dictionary<string, string> { ["Topping"] = "syrup" },
                        Tags = ["sweet"],
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "waffle iron is hot", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        // The parameterized group data-search should contain the feature name, description, labels, and categories
        var groupSearch = searchValues.FirstOrDefault(v => v.Contains("make waffles"));
        Assert.NotNull(groupSearch);
        Assert.Contains("waffles preparation", groupSearch);
        Assert.Contains("covers all waffle scenarios", groupSearch);
        Assert.Contains("breakfast", groupSearch);
        Assert.Contains("sweet", groupSearch);
    }

    [Fact]
    public void Data_search_does_not_contain_raw_plantuml_source()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nOrderService -> PaymentGateway : POST /payments\n@enduml")
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchNoPlantUml.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("create order"));
        Assert.NotNull(scenarioSearch);
        Assert.DoesNotContain("@startuml", scenarioSearch);
        // Raw aliases (without quoted participant declarations) should NOT appear
        Assert.DoesNotContain("paymentgateway", scenarioSearch);
        Assert.DoesNotContain("orderservice", scenarioSearch);
    }

    [Fact]
    public void Data_search_for_parameterized_group_does_not_contain_raw_plantuml_source()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$200" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$500" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nAccountService -> Ledger : debit\n@enduml"),
            new DefaultDiagramsFetcher.DiagramAsCode("s2", "", "@startuml\nAccountService -> Ledger : debit\n@enduml")
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchNoPlantUmlParam.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);

        var searchValues = ExtractDataSearchValues(content);
        foreach (var sv in searchValues)
        {
            Assert.DoesNotContain("@startuml", sv);
            Assert.DoesNotContain("accountservice", sv);
            Assert.DoesNotContain("ledger", sv);
        }

        var rowSearchValues = Regex.Matches(content, @"data-row-search=""([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();
        foreach (var rv in rowSearchValues)
        {
            Assert.DoesNotContain("@startuml", rv);
            Assert.DoesNotContain("accountservice", rv);
            Assert.DoesNotContain("ledger", rv);
        }
    }

    [Fact]
    public void Search_bar_is_immediately_usable_without_loading_state()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "something", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nparticipant \"Order Service\" as OS\nOS -> DB\n@enduml")
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchNoLoading.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);

        // Search bar should exist and NOT be disabled
        Assert.Contains("id=\"searchbar\"", content);
        Assert.DoesNotContain("search-loading-overlay", content);
        Assert.DoesNotContain("enrichSearchData", content);
    }

    [Fact]
    public void Data_search_includes_diagram_participant_names()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nparticipant \"Order Service\" as OS\ndatabase \"PostgreSQL\" as DB\nOS -> DB : save\n@enduml")
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchParticipants.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("create order"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("order service", scenarioSearch);
        Assert.Contains("postgresql", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_diagram_request_urls()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Process payment", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid card", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nparticipant \"API Gateway\" as GW\nGW -> PaySvc : POST: /api/payments/charge\nPaySvc -> GW : 200 OK\n@enduml")
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchUrls.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("process payment"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("/api/payments/charge", scenarioSearch);
        Assert.Contains("api gateway", scenarioSearch);
    }

    [Fact]
    public void Data_search_for_parameterized_group_includes_diagram_terms()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$200" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$500" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nparticipant \"Banking API\" as BA\nBA -> Ledger : GET: /accounts/balance\n@enduml"),
            new DefaultDiagramsFetcher.DiagramAsCode("s2", "", "@startuml\nparticipant \"Banking API\" as BA\nBA -> Ledger : GET: /accounts/balance\n@enduml")
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchParamDiagram.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);
        var searchValues = ExtractDataSearchValues(content);

        var groupSearch = searchValues.FirstOrDefault(v => v.Contains("withdraw"));
        Assert.NotNull(groupSearch);
        Assert.Contains("banking api", groupSearch);
        Assert.Contains("/accounts/balance", groupSearch);
    }

    [Fact]
    public void ExtractDiagramSearchTerms_extracts_participant_display_names()
    {
        var source = """
            @startuml
            participant "Order Service" as OS
            actor "Customer" as C
            database "PostgreSQL" as DB
            boundary "API Gateway" as GW
            control "Scheduler" as SCH
            entity "Invoice" as INV
            collections "Events" as EV
            queue "Message Bus" as MB
            OS -> DB : save
            @enduml
            """;

        var terms = ReportGenerator.ExtractDiagramSearchTerms(source);

        Assert.Contains("Order Service", terms);
        Assert.Contains("Customer", terms);
        Assert.Contains("PostgreSQL", terms);
        Assert.Contains("API Gateway", terms);
        Assert.Contains("Scheduler", terms);
        Assert.Contains("Invoice", terms);
        Assert.Contains("Events", terms);
        Assert.Contains("Message Bus", terms);
    }

    [Fact]
    public void ExtractDiagramSearchTerms_extracts_request_urls()
    {
        var source = """
            @startuml
            participant "API" as A
            A -> B : GET: /api/orders
            B -> C : POST: /api/payments/charge
            C -> D : DELETE: /api/sessions/abc123
            @enduml
            """;

        var terms = ReportGenerator.ExtractDiagramSearchTerms(source);

        Assert.Contains("/api/orders", terms);
        Assert.Contains("/api/payments/charge", terms);
        Assert.Contains("/api/sessions/abc123", terms);
    }

    // SEARCH_PLAN B11: with internal-flow tracking on, the default, a call's label is written inside its link,
    // [[#iflow-ID GET: /path]], and a long path is cut into chunks joined by a PlantUML break and spaces for display.
    // Until then the instant search read neither: the link put the method out of UrlRegex's reach, and a chunked path
    // was read to its first chunk with the break on its end.
    [Fact]
    public void A_linked_call_s_path_is_in_data_search()
    {
        var pairId = Guid.NewGuid();
        var request = new RequestResponseLog("Search orders", "linked-1", HttpMethod.Get, null, new Uri("http://orders.internal/api/orders/search"), [],
            "OrderService", "Api", RequestResponseType.Request, Guid.NewGuid(), pairId, TrackingIgnore: false);
        var response = request with { Type = RequestResponseType.Response, Content = "{ \"orders\": [] }", StatusCode = System.Net.HttpStatusCode.OK };
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([request, response], internalFlowTracking: true)
            .Single().PlantUmls.First().PlainText;
        Assert.Contains($"[[#iflow-{pairId} GET: /api/orders/search]]", source);

        var terms = ReportGenerator.ExtractDiagramSearchTerms(source);

        Assert.Contains("/api/orders/search", terms);
        Assert.DoesNotContain(terms, t => t.Contains("]]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_chunked_path_is_read_whole(bool linked)
    {
        var url = "http://orders.internal/api/orders/search?" + string.Join("&", Enumerable.Range(0, 7).Select(i => $"filter{i}=value{i}"));
        var request = new RequestResponseLog("Search orders", "chunked-1", HttpMethod.Get, null, new Uri(url), [],
            "OrderService", "Api", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), TrackingIgnore: false);
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([request], internalFlowTracking: linked)
            .Single().PlantUmls.First().PlainText;
        Assert.Contains("\\n        ", source);

        var terms = ReportGenerator.ExtractDiagramSearchTerms(source);

        Assert.Contains(new Uri(url).PathAndQuery, terms);
    }

    [Fact]
    public void ExtractDiagramSearchTerms_returns_empty_for_no_matching_lines()
    {
        var source = """
            @startuml
            A -> B : hello
            B -> A : world
            @enduml
            """;

        var terms = ReportGenerator.ExtractDiagramSearchTerms(source);

        Assert.Empty(terms);
    }

    [Fact]
    public void ExtractDiagramSearchTerms_handles_empty_and_null_input()
    {
        Assert.Empty(ReportGenerator.ExtractDiagramSearchTerms(""));
        Assert.Empty(ReportGenerator.ExtractDiagramSearchTerms(null!));
    }

    [Fact]
    public void Data_search_includes_rule_name()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Scenario with rule", Result = ExecutionResult.Passed,
                        Rule = "Business Rule Alpha",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "some context", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("scenario with rule"));
        Assert.NotNull(scenarioSearch);
        Assert.Contains("business rule alpha", scenarioSearch);
    }

    [Fact]
    public void Data_search_includes_rule_name_in_parameterized_group()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Param scenario", Result = ExecutionResult.Passed,
                        Rule = "Parameterized Rule Bravo",
                        OutlineId = "param-grp",
                        ExampleValues = new Dictionary<string, string> { ["Val"] = "one" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a value", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Param scenario", Result = ExecutionResult.Passed,
                        Rule = "Parameterized Rule Bravo",
                        OutlineId = "param-grp",
                        ExampleValues = new Dictionary<string, string> { ["Val"] = "two" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "another value", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var groupSearch = searchValues.FirstOrDefault(v => v.Contains("param scenario"));
        Assert.NotNull(groupSearch);
        Assert.Contains("parameterized rule bravo", groupSearch);
    }

    [Fact]
    public void Data_search_excludes_rule_when_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "No rule scenario", Result = ExecutionResult.Passed,
                        Rule = null,
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "some context", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        var searchValues = ExtractDataSearchValues(content);

        var scenarioSearch = searchValues.FirstOrDefault(v => v.Contains("no rule scenario"));
        Assert.NotNull(scenarioSearch);
        // Should only contain feature name, scenario name, and step text - no "rule" word injected
        Assert.Equal("f1 no rule scenario some context", scenarioSearch);
    }
}
