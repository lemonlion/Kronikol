using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class FailureClusterReportTests
{
    private static Feature[] MakeFeatures(params (string id, string name, ExecutionResult result, string? errorMessage)[] scenarios) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = scenarios.Select(s => new Scenario
            {
                Id = s.id,
                DisplayName = s.name,
                IsHappyPath = false,
                Result = s.result,
                ErrorMessage = s.errorMessage
            }).ToArray()
        }
    ];

    private static Feature[] MakeMultiFeature(params Feature[] features) => features;

    private static string GenerateReport(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_with_clustered_failures_shows_failure_clusters_section()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"),
            ("t3", "Test 3", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "FailureClusters.html");
        Assert.Contains("failure-clusters", content);
    }

    [Fact]
    public void Report_without_clusters_does_not_show_failure_clusters_section()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, null),
            ("t2", "Test 2", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "NoClusters.html");
        Assert.DoesNotContain("class=\"failure-clusters\"", content);
    }

    [Fact]
    public void Report_cluster_shows_count_in_summary()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"),
            ("t3", "Test 3", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterCount.html");
        Assert.Contains("3 scenarios", content);
    }

    [Fact]
    public void Report_cluster_contains_cluster_key_text()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterKey.html");
        Assert.Contains("Connection refused", content);
    }

    [Fact]
    public void Report_cluster_lists_affected_scenario_names()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterScenarios.html");
        Assert.Contains("Test 1", content);
        Assert.Contains("Test 2", content);
    }

    [Fact]
    public void Report_cluster_across_features_groups_together()
    {
        var features = MakeMultiFeature(
            new Feature
            {
                DisplayName = "Feature A",
                Scenarios = [new Scenario { Id = "t1", DisplayName = "Test 1", Result = ExecutionResult.Failed, ErrorMessage = "Timeout" }]
            },
            new Feature
            {
                DisplayName = "Feature B",
                Scenarios = [new Scenario { Id = "t2", DisplayName = "Test 2", Result = ExecutionResult.Failed, ErrorMessage = "Timeout" }]
            });
        var content = GenerateReport(features, "ClusterAcrossFeatures.html");
        Assert.Contains("failure-clusters", content);
        Assert.Contains("2 scenarios", content);
    }

    [Fact]
    public void Report_cluster_link_href_matches_scenario_element_id()
    {
        var features = MakeFeatures(
            ("t1", "Caller Uses Authorise Client Endpoint", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Caller Submits Payment Request", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterLinkHref.html");

        // Extract all cluster link hrefs
        var hrefMatches = Regex.Matches(content, @"class=""failure-cluster-scenario-link""\s+href=""#([^""]+)""");
        Assert.True(hrefMatches.Count >= 2, $"Expected at least 2 cluster links, found {hrefMatches.Count}");

        // Extract all scenario element ids
        var idMatches = Regex.Matches(content, @"<details\s+class=""scenario[^""]*""[^>]*\sid=""([^""]+)""");
        var elementIds = idMatches.Select(m => m.Groups[1].Value).ToHashSet();

        // Every cluster link href must target an existing element id
        foreach (Match href in hrefMatches)
        {
            var targetId = href.Groups[1].Value;
            Assert.True(elementIds.Contains(targetId),
                $"Cluster link targets '#{targetId}' but no scenario element has id='{targetId}'. Available ids: {string.Join(", ", elementIds)}");
        }
    }

    [Fact]
    public void Report_cluster_link_onclick_uses_correct_anchor_id()
    {
        var features = MakeFeatures(
            ("t1", "Caller Uses Authorise Client Endpoint", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Caller Submits Payment Request", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterLinkOnclick.html");

        // Extract the getElementById argument from the onclick handler
        var onclickMatch = Regex.Match(content, @"onclick=""[^""]*document\.getElementById\('([^']+)'\)");
        Assert.True(onclickMatch.Success, "Could not find onclick getElementById in cluster link");
        var onclickId = onclickMatch.Groups[1].Value;

        // Extract all scenario element ids
        var idMatches = Regex.Matches(content, @"<details\s+class=""scenario[^""]*""[^>]*\sid=""([^""]+)""");
        var elementIds = idMatches.Select(m => m.Groups[1].Value).ToHashSet();

        Assert.True(elementIds.Contains(onclickId),
            $"onclick targets '{onclickId}' but no scenario element has that id. Available ids: {string.Join(", ", elementIds)}");
    }

    [Fact]
    public void Report_cluster_link_href_matches_element_id_for_parameterized_scenarios()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Test Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Process(region: UK)", Result = ExecutionResult.Failed,
                        ErrorMessage = "Connection refused", OutlineId = "Process",
                        ExampleValues = new Dictionary<string, string> { ["region"] = "UK" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Process(region: US)", Result = ExecutionResult.Failed,
                        ErrorMessage = "Connection refused", OutlineId = "Process",
                        ExampleValues = new Dictionary<string, string> { ["region"] = "US" }
                    }
                ]
            }
        };
        var content = GenerateReport(features, "ClusterLinkParamHref.html");

        // Extract all cluster link hrefs
        var hrefMatches = Regex.Matches(content, @"class=""failure-cluster-scenario-link""\s+href=""#([^""]+)""");
        Assert.True(hrefMatches.Count >= 2, $"Expected at least 2 cluster links, found {hrefMatches.Count}");

        // Extract all element ids (both <details> and <tr> elements) — exclude data-scenario-id
        var allIdMatches = Regex.Matches(content, @"(?<!\w-)id=""([^""]+)""");
        var elementIds = allIdMatches.Select(m => m.Groups[1].Value).ToHashSet();

        // Every cluster link href must target an existing element id
        foreach (Match href in hrefMatches)
        {
            var targetId = href.Groups[1].Value;
            Assert.True(elementIds.Contains(targetId),
                $"Cluster link targets '#{targetId}' but no element has id='{targetId}'.");
        }
    }

    [Fact]
    public void Report_cluster_link_onclick_opens_ancestor_details_for_parameterized_row()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Test Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Process(region: UK)", Result = ExecutionResult.Failed,
                        ErrorMessage = "Connection refused", OutlineId = "Process",
                        ExampleValues = new Dictionary<string, string> { ["region"] = "UK" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Process(region: US)", Result = ExecutionResult.Failed,
                        ErrorMessage = "Connection refused", OutlineId = "Process",
                        ExampleValues = new Dictionary<string, string> { ["region"] = "US" }
                    }
                ]
            }
        };
        var content = GenerateReport(features, "ClusterLinkParamOnclick.html");

        // The onclick handler must call selectRow for parameterized row navigation
        var onclickMatches = Regex.Matches(content, @"class=""failure-cluster-scenario-link""[^>]*onclick=""([^""]+)""");
        Assert.True(onclickMatches.Count >= 2, $"Expected at least 2 onclick handlers, found {onclickMatches.Count}");

        foreach (Match m in onclickMatches)
        {
            var onclick = m.Groups[1].Value;
            // Must open all ancestor details AND trigger click for parameterized row navigation
            Assert.Contains("el.click()", onclick);
        }
    }

    [Fact]
    public void Report_cluster_link_onclick_calls_scrollIntoView()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterLinkScroll.html");

        var onclickMatches = Regex.Matches(content, @"class=""failure-cluster-scenario-link""[^>]*onclick=""([^""]+)""");
        Assert.True(onclickMatches.Count >= 2, $"Expected at least 2 onclick handlers, found {onclickMatches.Count}");

        foreach (Match m in onclickMatches)
        {
            var onclick = m.Groups[1].Value;
            Assert.Contains("scrollIntoView", onclick);
        }
    }

    [Fact]
    public void Report_cluster_link_onclick_prevents_default_for_reliable_scrolling()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterLinkPreventDefault.html");

        var onclickMatches = Regex.Matches(content, @"class=""failure-cluster-scenario-link""[^>]*onclick=""([^""]+)""");
        Assert.True(onclickMatches.Count >= 2);

        foreach (Match m in onclickMatches)
        {
            var onclick = m.Groups[1].Value;
            // Must prevent default to avoid native anchor behavior racing with scrollIntoView
            Assert.Contains("event.preventDefault()", onclick);
        }
    }
}
