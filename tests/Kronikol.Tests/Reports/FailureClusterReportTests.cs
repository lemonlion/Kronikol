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
        return ReportHtml.Read(path);
    }

    [Fact]
    public void Report_with_clustered_failures_shows_failure_clusters_section()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"),
            ("t3", "Test 3", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "FailureClusters.html");
        // The element, not the class name alone: the stylesheet names .failure-clusters in every report.
        Assert.Contains("<details class=\"failure-clusters\"", content);
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
        Assert.Contains("<details class=\"failure-clusters\"", content);
        Assert.Contains("<span class=\"failure-cluster-count\">2 scenarios</span>", content);
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
    public void Report_cluster_link_to_a_parameterized_row_is_plain_and_names_a_row_that_exists()
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

        // Since 5.2.0 the link is plain and the report's navigation script takes it to reveal_url_anchor, the one way to a
        // scenario, which opens every enclosing section, clicks the example row and picks the copy that shows
        // (plans/BACK_BUTTON_NAVIGATION_PLAN.md S6, S16; ParameterizedGroupRenderTests pins the row resolution, and
        // FailureClusterLinkTests and BackForwardRestoreTests.R9 what the browser does): each link names a row that exists
        var links = Regex.Matches(content, @"<a class=""failure-cluster-scenario-link"" href=""#([^""]+)""[^>]*>");
        Assert.True(links.Count >= 2, $"Expected at least 2 cluster links, found {links.Count}");
        foreach (Match m in links)
        {
            Assert.DoesNotContain("onclick", m.Value);
            Assert.Matches(new Regex($@"<tr[^>]+id=""{Regex.Escape(m.Groups[1].Value)}"""), content);
        }
    }

    [Fact]
    public void Report_cluster_links_are_plain_links_the_navigation_script_takes()
    {
        // Since 5.2.0 (plans/BACK_BUTTON_NAVIGATION_PLAN.md S2, S16, S20): the link's own script replaced the address and
        // cancelled every click, so Back left the report and Ctrl+click could not open a new tab. The navigation script
        // takes a same-page link to a scenario, leaves a click with a modifier key to the browser, and reaches the scenario
        // through reveal_url_anchor and jump_into_view, never smoothly (a smooth scroll ended past a scenario far down a
        // long report, plans/DOORSTEP_PLAN.md F28; DeepLinkReportTests pins the jump)
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Connection refused"),
            ("t2", "Test 2", ExecutionResult.Failed, "Connection refused"));
        var content = GenerateReport(features, "ClusterLinkPlain.html");

        var links = Regex.Matches(content, @"<a class=""failure-cluster-scenario-link""[^>]*>");
        Assert.Equal(2, links.Count);
        Assert.All(links, m => Assert.DoesNotContain("onclick", m.Value));
        var script = ReportGenerator.LoadResource("report-navigation-script.js");
        Assert.Contains("closest('a[href^=\"#scenario-\"], a[href^=\"#sid-\"]')", script);
        Assert.Contains("e.ctrlKey || e.metaKey || e.shiftKey || e.altKey", script);
    }
}
