using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A run's diagnostics carry what a tracking component recorded while it was set up, before any report generation was
/// scoped (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.1: Kafka build interception that tiered compilation can
/// end). Every report the process writes carries each entry once.
/// </summary>
// It generates reports, which draw through the process-wide diagram cache an ingest resets, and every report generated
// while its entry is held would carry it: the DiagramsFetcher collection holds every class that generates one.
[Collection("DiagramsFetcher")]
public class ProcessDiagnosticsReportTests : IDisposable
{
    private const string Key = "Kronikol.Tests.ProcessDiagnosticsReportTests";
    private const string Message = "a component set up before the run cannot keep tracking";

    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-process-diagnostics").FullName;

    public void Dispose()
    {
        ProcessDiagnostics.Remove(Key);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = "process-diagnostics", DisplayName = "Place an order", Result = ExecutionResult.Passed }]
        }
    ];

    private JsonElement[] Diagnostics(string run, ReportDiagnosticsCollector? hostCollector = null)
    {
        var folder = Path.Combine(_dir, run);
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = folder,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
            WriteRunSummaryToConsole = false,
        };
        using (RequestResponseLogger.IsolateForTests())
        using (hostCollector is null ? null : ReportDiagnosticsScope.Begin(hostCollector))
            ReportGenerator.CreateStandardReportsWithDiagrams(Features(), Start, Start.AddMinutes(1), options);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "TestRunReport.json")));
        return document.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Where(d => d.GetProperty("message").GetString() == Message)
            .Select(d => d.Clone())
            .ToArray();
    }

    [Fact]
    public void An_entry_recorded_at_setup_is_in_every_report_the_process_writes_once()
    {
        Assert.True(ProcessDiagnostics.RecordOnce(Key, DiagnosticKind.CaptureDegraded, Message));
        Assert.False(ProcessDiagnostics.RecordOnce(Key, DiagnosticKind.CaptureDegraded, Message));

        var first = Assert.Single(Diagnostics("first"));
        var second = Assert.Single(Diagnostics("second"));

        Assert.Equal("CaptureDegraded", first.GetProperty("kind").GetString());
        Assert.Equal("CaptureDegraded", second.GetProperty("kind").GetString());
    }

    [Fact]
    public void A_host_collector_that_already_holds_the_entry_reports_it_once()
    {
        ProcessDiagnostics.RecordOnce(Key, DiagnosticKind.CaptureDegraded, Message);
        var host = new ReportDiagnosticsCollector();
        host.Add(DiagnosticKind.CaptureDegraded, Message);

        Assert.Single(Diagnostics("hosted", host));
    }

    [Fact]
    public void Nothing_recorded_adds_nothing()
    {
        Assert.Empty(Diagnostics("none"));
    }
}
