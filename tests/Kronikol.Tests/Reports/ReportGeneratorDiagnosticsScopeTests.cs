using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The structured diagnostics channel must work on every path, not only under <c>kronikol ingest</c>. An
/// adapter-driven run (xUnit, NUnit, ReqNRoll…) calls <see cref="ReportGenerator.CreateStandardReportsWithDiagrams"/>
/// with no collector scoped; before this, every <c>ReportDiagnosticsScope.Record</c> on that path was a silent
/// no-op and the JSON's <c>diagnostics</c> array was always empty.
/// </summary>
[Collection("DiagramsFetcher")]
public class ReportGeneratorDiagnosticsScopeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-diagscope-" + Guid.NewGuid().ToString("N"));

    public ReportGeneratorDiagnosticsScopeTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static ReportConfigurationOptions Options(string dir) => new()
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
        PlantUmlRendering = PlantUmlRendering.Local,
        PlantUmlImageFormat = PlantUmlImageFormat.Base64Svg,
        LocalDiagramRenderer = (_, _) => throw new TimeoutException("the render process did not answer"),
    };

    private static Feature[] OneScenario(string testId) =>
        [new Feature { DisplayName = "Renders", Scenarios = [new Scenario { Id = testId, DisplayName = "Poisoned diagram", Result = ExecutionResult.Passed }] }];

    private static JsonElement[] DiagnosticsIn(string dir)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "TestRunReport.json")));
        return json.RootElement.GetProperty("diagnostics").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    [Fact]
    public void A_normal_run_records_its_diagnostics_in_the_json()
    {
        var testId = "diag-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Renders", testId, HttpMethod.Get, new Uri("http://svc/poisoned"), "Svc", "Test");

        ReportGenerator.CreateStandardReportsWithDiagrams(OneScenario(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(_dir));

        var diagnostics = DiagnosticsIn(_dir);
        Assert.Contains(diagnostics, d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.RenderFailure));
        Assert.Contains("Report diagnostics", File.ReadAllText(Path.Combine(_dir, "TestRunReport.html")));
    }

    [Fact]
    public void A_scope_opened_by_the_host_is_reused_not_replaced()
    {
        var testId = "diag-host-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Renders", testId, HttpMethod.Get, new Uri("http://svc/poisoned"), "Svc", "Test");
        var collector = new ReportDiagnosticsCollector();
        collector.Add(DiagnosticKind.CaptureDegraded, "a tap dropped 3 segments");

        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagrams(OneScenario(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(_dir));

        // The host's own entry and the render failure both reach the file, and the host still sees the
        // render failure in the collector it owns.
        var diagnostics = DiagnosticsIn(_dir);
        Assert.Contains(diagnostics, d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.CaptureDegraded));
        Assert.Contains(diagnostics, d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.RenderFailure));
        Assert.True(collector.CountOf(DiagnosticKind.RenderFailure) > 0);
    }

    [Fact]
    public void Two_generations_do_not_share_a_collector()
    {
        var first = Path.Combine(_dir, "first");
        var second = Path.Combine(_dir, "second");
        var testId = "diag-two-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Renders", testId, HttpMethod.Get, new Uri("http://svc/poisoned"), "Svc", "Test");

        ReportGenerator.CreateStandardReportsWithDiagrams(OneScenario(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(first));
        DefaultDiagramsFetcher.Reset();
        var healthy = Options(second);
        healthy.LocalDiagramRenderer = (source, _) => System.Text.Encoding.UTF8.GetBytes("<svg>" + source.Length + "</svg>");
        ReportGenerator.CreateStandardReportsWithDiagrams(OneScenario(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, healthy);

        Assert.Contains(DiagnosticsIn(first), d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.RenderFailure));
        Assert.DoesNotContain(DiagnosticsIn(second), d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.RenderFailure));
    }
}
