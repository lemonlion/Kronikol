using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

[Collection("DiagramsFetcher")]
public class ReportDiagnosticsTests : IDisposable
{
    public ReportDiagnosticsTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    [Fact]
    public void Warns_when_unpaired_requests_exist()
    {
        var testId = Guid.NewGuid().ToString();
        var logs = new[]
        {
            MakeLog(testId, RequestResponseType.Request, Guid.NewGuid()),
            MakeLog(testId, RequestResponseType.Request, Guid.NewGuid())
        };

        var warnings = ReportDiagnostics.Analyse(logs, []);
        Assert.Contains(warnings, w => w.Contains("unpaired") && w.Contains("2"));
    }

    [Fact]
    public void No_warning_when_all_requests_are_paired()
    {
        var testId = Guid.NewGuid().ToString();
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MakeLog(testId, RequestResponseType.Request, pairId),
            MakeLog(testId, RequestResponseType.Response, pairId)
        };

        var warnings = ReportDiagnostics.Analyse(logs, []);
        Assert.DoesNotContain(warnings, w => w.Contains("unpaired"));
    }

    [Fact]
    public void Warns_when_log_test_ids_dont_match_any_feature()
    {
        var orphanTestId = Guid.NewGuid().ToString();
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MakeLog(orphanTestId, RequestResponseType.Request, pairId),
            MakeLog(orphanTestId, RequestResponseType.Response, pairId)
        };
        var features = new[] { MakeFeature("different-test-id") };

        var warnings = ReportDiagnostics.Analyse(logs, features);
        Assert.Contains(warnings, w => w.Contains("orphaned") && w.Contains("1"));
    }

    [Fact]
    public void No_orphaned_warning_when_test_ids_match()
    {
        var testId = Guid.NewGuid().ToString();
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MakeLog(testId, RequestResponseType.Request, pairId),
            MakeLog(testId, RequestResponseType.Response, pairId)
        };
        var features = new[] { MakeFeature(testId) };

        var warnings = ReportDiagnostics.Analyse(logs, features);
        Assert.DoesNotContain(warnings, w => w.Contains("orphaned"));
    }

    [Fact]
    public void Returns_summary_with_total_counts()
    {
        var testId = Guid.NewGuid().ToString();
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MakeLog(testId, RequestResponseType.Request, pairId),
            MakeLog(testId, RequestResponseType.Response, pairId)
        };
        var features = new[] { MakeFeature(testId) };

        var warnings = ReportDiagnostics.Analyse(logs, features);
        Assert.Contains(warnings, w => w.Contains("2 log entries") && w.Contains("1 test"));
    }

    [Fact]
    public void Returns_empty_for_no_logs_and_no_features()
    {
        var warnings = ReportDiagnostics.Analyse([], []);
        Assert.Empty(warnings);
    }

    // ─── Empty features with logs warnings ─────────────────────

    [Fact]
    public void Warns_when_logs_exist_but_no_features()
    {
        var logs = new[]
        {
            MakeLog("t1", RequestResponseType.Request, Guid.NewGuid()),
            MakeLog("t1", RequestResponseType.Response, Guid.NewGuid())
        };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.Contains(warnings, w =>
            w.Contains("no test contexts were provided") &&
            w.Contains("TestContexts.Enqueue"));
    }

    [Fact]
    public void No_empty_features_warning_when_features_exist()
    {
        var testId = Guid.NewGuid().ToString();
        var pairId = Guid.NewGuid();
        var logs = new[]
        {
            MakeLog(testId, RequestResponseType.Request, pairId),
            MakeLog(testId, RequestResponseType.Response, pairId)
        };
        var features = new[] { MakeFeature(testId) };

        var warnings = ReportDiagnostics.Analyse(logs, features);

        Assert.DoesNotContain(warnings, w => w.Contains("no test contexts were provided"));
    }

    // ─── Unused tracking component warnings ────────────────────

    [Fact]
    public void Warns_when_tracking_component_never_invoked()
    {
        TrackingComponentRegistry.Register(new StubComponent("SqlTrackingInterceptor (DB)", wasInvoked: false));
        var logs = new[] { MakeLog("t1", RequestResponseType.Request, Guid.NewGuid()) };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.Contains(warnings, w => w.Contains("never invoked") && w.Contains("SqlTrackingInterceptor (DB)"));
    }

    [Fact]
    public void No_unused_component_warning_when_all_invoked()
    {
        TrackingComponentRegistry.Register(new StubComponent("Handler", wasInvoked: true));
        var logs = new[] { MakeLog("t1", RequestResponseType.Request, Guid.NewGuid()) };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.DoesNotContain(warnings, w => w.Contains("never invoked"));
    }

    [Fact]
    public void No_unused_component_warning_when_no_components_registered()
    {
        var logs = new[] { MakeLog("t1", RequestResponseType.Request, Guid.NewGuid()) };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.DoesNotContain(warnings, w => w.Contains("never invoked"));
    }

    [Fact]
    public void Unused_component_warning_lists_count()
    {
        TrackingComponentRegistry.Clear();
        TrackingComponentRegistry.Register(new StubComponent("A", wasInvoked: false));
        TrackingComponentRegistry.Register(new StubComponent("B", wasInvoked: false));
        TrackingComponentRegistry.Register(new StubComponent("C", wasInvoked: true));
        var logs = new[] { MakeLog("t1", RequestResponseType.Request, Guid.NewGuid()) };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.Contains(warnings, w => w.Contains("2 tracking component(s)") && w.Contains("A") && w.Contains("B"));
    }

    [Fact]
    public void Unused_component_warning_does_not_throw()
    {
        TrackingComponentRegistry.Register(new StubComponent("DB", wasInvoked: false));
        var logs = new[] { MakeLog("t1", RequestResponseType.Request, Guid.NewGuid()) };

        // This should return warnings, NOT throw
        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.NotEmpty(warnings);
    }

    // ─── Helpers ───────────────────────────────────────────────

    private class StubComponent(string name, bool wasInvoked) : ITrackingComponent
    {
        public string ComponentName => name;
        public bool WasInvoked => wasInvoked;
        public int InvocationCount => wasInvoked ? 1 : 0;
    }

    private static RequestResponseLog MakeLog(string testId, RequestResponseType type, Guid requestResponseId) =>
        new("Test", testId, HttpMethod.Get, null, new Uri("http://svc/api"),
            [], "Svc", "Caller", type, Guid.NewGuid(), requestResponseId, false)
        {
            Timestamp = DateTimeOffset.UtcNow
        };

    private static Feature MakeFeature(string testId) =>
        new()
        {
            DisplayName = "Feature",
            Scenarios =
            [
                new Scenario { Id = testId, DisplayName = "Scenario" }
            ]
        };
    [Fact]
    public void Markers_and_user_actions_are_not_unpaired_requests()
    {
        static RequestResponseLog Log(RequestResponseType type, Guid id) =>
            new("t", "id", HttpMethod.Get, null, new Uri("http://svc/x"), [], "svc", "caller", type, Guid.NewGuid(), id, false);

        var paired = Guid.NewGuid();
        var logs = new[]
        {
            Log(RequestResponseType.Request, paired),
            Log(RequestResponseType.Response, paired),
            Log(RequestResponseType.Request, Guid.NewGuid()), // genuinely unpaired
            new RequestResponseLog("t", "id", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsOverrideStart = true },
            new RequestResponseLog("t", "id", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsOverrideEnd = true },
            new RequestResponseLog("t", "id", "Click", null, new Uri("http://web/"), [], "web", "User", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsUserAction = true },
        };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        var unpaired = Assert.Single(warnings, w => w.Contains("unpaired request", StringComparison.Ordinal));
        Assert.Contains("1 unpaired", unpaired, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_action_boundary_marker_is_not_an_unpaired_request()
    {
        // StartAction() logs one marker per test with a fresh RequestResponseId and no response;
        // counting it warned about "unpaired requests" on every phase-separated run.
        var logs = new[]
        {
            new RequestResponseLog("t", "id", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true }
        };

        var warnings = ReportDiagnostics.Analyse(logs, []);

        Assert.DoesNotContain(warnings, w => w.Contains("unpaired request", StringComparison.Ordinal));
    }
}
