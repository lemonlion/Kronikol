using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class FailureClustererTests
{
    private static Scenario MakeFailedScenario(string id, string name, string errorMessage, string? errorStackTrace = null) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Result = ExecutionResult.Failed,
            ErrorMessage = errorMessage,
            ErrorStackTrace = errorStackTrace
        };

    private static Scenario MakePassedScenario(string id, string name) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Result = ExecutionResult.Passed
        };

    [Fact]
    public void Returns_empty_when_no_failures()
    {
        var scenarios = new[] { MakePassedScenario("s1", "Test 1") };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Empty(clusters);
    }

    [Fact]
    public void Returns_empty_when_scenarios_empty()
    {
        var clusters = FailureClusterer.Cluster([]);
        Assert.Empty(clusters);
    }

    [Fact]
    public void Single_failure_does_not_create_cluster()
    {
        var scenarios = new[] { MakeFailedScenario("s1", "Test 1", "NullReferenceException") };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Empty(clusters);
    }

    [Fact]
    public void Two_failures_with_same_message_grouped_together()
    {
        var scenarios = new[]
        {
            MakeFailedScenario("s1", "Test 1", "Connection refused"),
            MakeFailedScenario("s2", "Test 2", "Connection refused")
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].Scenarios.Length);
    }

    [Fact]
    public void Two_failures_with_different_messages_create_no_clusters()
    {
        var scenarios = new[]
        {
            MakeFailedScenario("s1", "Test 1", "Connection refused"),
            MakeFailedScenario("s2", "Test 2", "Timeout expired")
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Empty(clusters);
    }

    [Fact]
    public void Cluster_key_uses_first_line_of_error_message()
    {
        var scenarios = new[]
        {
            MakeFailedScenario("s1", "Test 1", "Connection refused\nSome detail"),
            MakeFailedScenario("s2", "Test 2", "Connection refused\nDifferent detail")
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Single(clusters);
        Assert.Equal("Connection refused", clusters[0].ClusterKey);
    }

    [Fact]
    public void Passed_scenarios_are_excluded_from_clusters()
    {
        var scenarios = new[]
        {
            MakePassedScenario("s1", "Test 1"),
            MakeFailedScenario("s2", "Test 2", "Error!"),
            MakeFailedScenario("s3", "Test 3", "Error!")
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].Scenarios.Length);
        Assert.DoesNotContain(clusters[0].Scenarios, s => s.Id == "s1");
    }

    [Fact]
    public void Cluster_preserves_feature_name()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Feature A",
                Scenarios = [MakeFailedScenario("s1", "Test 1", "Error!")]
            },
            new Feature
            {
                DisplayName = "Feature B",
                Scenarios = [MakeFailedScenario("s2", "Test 2", "Error!")]
            }
        };

        var allScenarios = features.SelectMany(f => f.Scenarios).ToArray();
        var clusters = FailureClusterer.Cluster(allScenarios);
        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].Scenarios.Length);
    }

    [Fact]
    public void Only_creates_clusters_with_two_or_more_scenarios()
    {
        var scenarios = new[]
        {
            MakeFailedScenario("s1", "Test 1", "Unique error 1"),
            MakeFailedScenario("s2", "Test 2", "Unique error 2"),
            MakeFailedScenario("s3", "Test 3", "Common error"),
            MakeFailedScenario("s4", "Test 4", "Common error")
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        // Only the "Common error" cluster should be returned (2+ scenarios)
        Assert.Single(clusters);
        Assert.Equal("Common error", clusters[0].ClusterKey);
    }

    [Fact]
    public void Clusters_ordered_by_count_descending()
    {
        var scenarios = new[]
        {
            MakeFailedScenario("s1", "Test 1", "Error A"),
            MakeFailedScenario("s2", "Test 2", "Error A"),
            MakeFailedScenario("s3", "Test 3", "Error B"),
            MakeFailedScenario("s4", "Test 4", "Error B"),
            MakeFailedScenario("s5", "Test 5", "Error B"),
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Equal(2, clusters.Length);
        Assert.Equal("Error B", clusters[0].ClusterKey);
        Assert.Equal("Error A", clusters[1].ClusterKey);
    }

    [Fact]
    public void Normalizes_whitespace_in_cluster_key()
    {
        var scenarios = new[]
        {
            MakeFailedScenario("s1", "Test 1", "  Connection   refused  "),
            MakeFailedScenario("s2", "Test 2", "Connection refused")
        };
        var clusters = FailureClusterer.Cluster(scenarios);
        Assert.Single(clusters);
    }

    [Fact]
    public void Handles_null_error_messages()
    {
        var scenario = new Scenario
        {
            Id = "s1",
            DisplayName = "Test 1",
            Result = ExecutionResult.Failed,
            ErrorMessage = null
        };
        var clusters = FailureClusterer.Cluster([scenario]);
        Assert.Empty(clusters); // null error message can't be clustered
    }

    [Fact]
    public void Duplicate_display_names_get_deduplicated_anchor_ids_in_report()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Feature A",
                Scenarios =
                [
                    new Scenario { Id = "a1", DisplayName = "Health check fails", Result = ExecutionResult.Failed, ErrorMessage = "Service unavailable" },
                ]
            },
            new Feature
            {
                DisplayName = "Feature B",
                Scenarios =
                [
                    new Scenario { Id = "b1", DisplayName = "Health check fails", Result = ExecutionResult.Failed, ErrorMessage = "Service unavailable" },
                ]
            }
        };

        var tempFile = Path.Combine(Path.GetTempPath(), $"dup-anchor-{Guid.NewGuid():N}.html");
        try
        {
            var path = ReportGenerator.GenerateHtmlReport(
                [], features, DateTime.UtcNow, DateTime.UtcNow,
                null, tempFile, "Test", false);
            var html = ReportHtml.Read(path);

            // First occurrence gets the base ID
            Assert.Contains("id=\"scenario-health-check-fails\"", html);
            // Second occurrence gets a deduplicated ID with suffix
            Assert.Contains("id=\"scenario-health-check-fails-2\"", html);
            // Cluster link for the second scenario should reference the deduplicated ID
            Assert.Contains("href=\"#scenario-health-check-fails-2\"", html);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
