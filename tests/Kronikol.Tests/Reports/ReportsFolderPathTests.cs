using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>K1 (honoured output directory), K2 (resettable diagram cache) and K9 (no-interactions marker).</summary>
[Collection("DiagramsFetcher")]
public class ReportsFolderPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-out-" + Guid.NewGuid().ToString("N"));

    public ReportsFolderPathTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static ReportConfigurationOptions Options(string dir) => new()
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = false,
        GenerateComponentDiagram = true,
        GenerateSpecificationsReport = true,
        GenerateSpecificationsData = true,
        WriteCiSummary = true,
    };

    private static Feature[] OneFeature(string testId, string name = "Place order") =>
        [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = testId, DisplayName = name, Result = ExecutionResult.Passed }] }];

    [Fact]
    public void ResolveReportsDirectory_defaults_to_Reports_under_base_directory()
    {
        var resolved = ReportGenerator.ResolveReportsDirectory(new ReportConfigurationOptions());
        Assert.Equal(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports")), resolved);
        Assert.Equal(resolved, ReportGenerator.ResolveReportsDirectory(null));
    }

    [Fact]
    public void ResolveReportsDirectory_honours_relative_and_absolute_paths()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "out", "sub")),
            ReportGenerator.ResolveReportsDirectory(new ReportConfigurationOptions { ReportsFolderPath = "out/sub" }));
        Assert.Equal(Path.GetFullPath(_dir),
            ReportGenerator.ResolveReportsDirectory(new ReportConfigurationOptions { ReportsFolderPath = _dir }));
    }

    [Fact]
    public void Every_standard_output_lands_in_the_configured_directory()
    {
        var testId = "k1-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Place order", testId, HttpMethod.Post, new Uri("http://orders/api/orders"), "OrdersApi", "Test", statusCode: HttpStatusCode.Created);

        ReportGenerator.CreateStandardReportsWithDiagrams(OneFeature(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(_dir));

        foreach (var file in new[] { "TestRunReport.html", "TestRunReport.json", "TestRunReport.schema.json", "Specifications.html", "Specifications.yml", "ComponentDiagram.html", "CiSummary.md" })
            Assert.True(File.Exists(Path.Combine(_dir, file)), $"expected {file} in {_dir}");

        Assert.Contains("Place order", File.ReadAllText(Path.Combine(_dir, "TestRunReport.html")));
    }

    [Fact]
    public void Reset_lets_two_generations_in_one_process_render_distinct_diagrams()
    {
        var firstId = "k2a-" + Guid.NewGuid().ToString("N");
        var secondId = "k2b-" + Guid.NewGuid().ToString("N");
        var firstDir = Path.Combine(_dir, "first");
        var secondDir = Path.Combine(_dir, "second");

        RequestResponseLogger.LogPair("First", firstId, HttpMethod.Get, new Uri("http://svc/first-path"), "Svc", "Test");
        ReportGenerator.CreateStandardReportsWithDiagrams(OneFeature(firstId, "First"), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(firstDir));
        Assert.True(DefaultDiagramsFetcher.HasCachedDiagrams);

        DefaultDiagramsFetcher.Reset();
        Assert.False(DefaultDiagramsFetcher.HasCachedDiagrams);

        RequestResponseLogger.LogPair("Second", secondId, HttpMethod.Get, new Uri("http://svc/second-path"), "Svc", "Test");
        ReportGenerator.CreateStandardReportsWithDiagrams(OneFeature(secondId, "Second"), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(secondDir));

        var secondHtml = File.ReadAllText(Path.Combine(secondDir, "TestRunReport.html"));
        Assert.Contains("second-path", secondHtml);
    }

    [Fact]
    public void Scenario_without_interactions_renders_an_explicit_marker_by_default()
    {
        var testId = "k9-" + Guid.NewGuid().ToString("N");

        ReportGenerator.CreateStandardReportsWithDiagrams(OneFeature(testId, "Pure UI test"), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(_dir));

        var html = File.ReadAllText(Path.Combine(_dir, "TestRunReport.html"));
        Assert.Contains("No interactions captured for this scenario.", html);
        Assert.Contains("data-no-interactions=\"true\"", html);
    }

    [Fact]
    public void No_interactions_marker_can_be_disabled()
    {
        var testId = "k9off-" + Guid.NewGuid().ToString("N");
        var options = Options(_dir);
        options.ShowNoInteractionsMarker = false;

        ReportGenerator.CreateStandardReportsWithDiagrams(OneFeature(testId, "Pure UI test"), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.DoesNotContain("data-no-interactions", File.ReadAllText(Path.Combine(_dir, "TestRunReport.html")));
    }

    [Fact]
    public void Options_defaults_for_new_knobs()
    {
        var options = new ReportConfigurationOptions();
        Assert.False(options.CollapseConsecutiveIdenticalCalls);
        Assert.Equal(2, options.CollapseThreshold);
        Assert.Null(options.MaxArrowsPerDiagram);
        Assert.True(options.ShowNoInteractionsMarker);
    }
}
