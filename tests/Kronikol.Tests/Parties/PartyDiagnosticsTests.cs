using System.Net;
using System.Text.Json;
using Kronikol.Parties;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1 (plans/PARTY_IDENTITY_PLAN.md §5.2): what the party checks find is written only by a run that declares
/// something, from the one resolution made before the outputs are written. A run that declares nothing keeps its files
/// as they were, gains no labs page for it, and <c>kronikol query parties</c> prints the same checks as notes.
/// </summary>
[Collection("DiagramsFetcher")]
public class PartyDiagnosticsTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-party-diagnostics").FullName;

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static void Pair(string testId, string caller, string service, string url, AttributionSource source)
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLogger.Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, pair, false)
            { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source });
        RequestResponseLogger.Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK)
            { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source });
    }

    /// <summary>
    /// A run whose service left its tracker at the test's name: its call to Cow Service, made while serving the test's
    /// request, says the test made it, which is <see cref="DiagnosticKind.TestPartyNameInsideHost"/>.
    /// </summary>
    private string Write(string? serviceId)
    {
        var reports = Path.Combine(_dir, serviceId ?? "plain");
        var testId = "party-diagnostics-" + Guid.NewGuid().ToString("N");
        Pair(testId, "Caller", "Breakfast Provider", "http://localhost/pancakes", AttributionSource.TestContext);
        Pair(testId, "Caller", "Cow Service", "http://localhost:5001/milk", AttributionSource.RequestHeader);
        Feature[] features = [new Feature { DisplayName = "Breakfast", Scenarios = [new Scenario { Id = testId, DisplayName = "Order pancakes", Result = ExecutionResult.Passed }] }];

        ReportGenerator.CreateStandardReportsWithDiagrams(features, Start, Start.AddSeconds(30), new ReportConfigurationOptions
        {
            ReportsFolderPath = reports,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
            CompressTestRunReport = false,
            ServiceId = serviceId
        });
        return reports;
    }

    private static string[] KindsIn(string reports)
    {
        using var report = JsonDocument.Parse(ReportFile.Read(Path.Combine(reports, "TestRunReport.json")));
        return report.RootElement.TryGetProperty("diagnostics", out var diagnostics)
            ? diagnostics.EnumerateArray().Select(d => d.GetProperty("kind").GetString()!).ToArray()
            : [];
    }

    private static string Labs(string reports) => Path.Combine(reports, LabsReportGenerator.FileName("TestRunReport"));

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        QueryCommand.Run(args, output, new StringWriter());
        return output.ToString();
    }

    [Fact]
    public void A_run_that_declares_nothing_records_no_party_check_and_the_parties_verb_prints_it_as_a_note()
    {
        var reports = Write(serviceId: null);

        Assert.DoesNotContain(nameof(DiagnosticKind.TestPartyNameInsideHost), KindsIn(reports));
        // Other facts' calls in this process are orphans here, which the run records as UnattributedInteractions, so a labs
        // page may be written for them; it never carries a party check.
        Assert.False(File.Exists(Labs(reports)) && ReportHtml.Read(Labs(reports)).Contains(nameof(DiagnosticKind.TestPartyNameInsideHost), StringComparison.Ordinal));
        Assert.Contains("carry the test's name, \"Caller\", as their caller", Query("parties", reports));
    }

    [Fact]
    public void A_run_that_declares_its_service_records_the_check_in_its_data_file_its_labs_page_and_its_summary()
    {
        var reports = Write(serviceId: "breakfast-provider");

        Assert.Contains(nameof(DiagnosticKind.TestPartyNameInsideHost), KindsIn(reports));
        Assert.Contains("as their caller", ReportHtml.Read(Labs(reports)));
        Assert.Contains(nameof(DiagnosticKind.TestPartyNameInsideHost), Query("summary", reports));
    }
}
