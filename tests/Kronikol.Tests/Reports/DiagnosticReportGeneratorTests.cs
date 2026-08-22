using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

[Collection("DiagramsFetcher")]
public class DiagnosticReportGeneratorTests : IDisposable
{
    public DiagnosticReportGeneratorTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    [Fact]
    public void Unused_component_hint_mentions_ResolveDbContextOptions_not_PostConfigure()
    {
        TrackingComponentRegistry.Register(
            new StubComponent("SqlTrackingInterceptor (DB)", wasInvoked: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("ResolveDbContextOptions", html);
        Assert.DoesNotContain("Fix: use <code>PostConfigure</code> on the framework", html);
    }

    [Fact]
    public void Unused_component_hint_warns_PostConfigure_does_not_work_with_Duende()
    {
        TrackingComponentRegistry.Register(
            new StubComponent("SqlTrackingInterceptor (DB)", wasInvoked: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("PostConfigure", html);
        Assert.Contains("does not work with Duende IdentityServer", html);
    }

    [Fact]
    public void No_unused_component_hints_when_all_components_are_active()
    {
        TrackingComponentRegistry.Register(
            new StubComponent("SqlTrackingInterceptor (DB)", wasInvoked: true));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.DoesNotContain("Never Invoked", html);
        Assert.DoesNotContain("ResolveDbContextOptions", html);
    }

    [Fact]
    public void No_tracking_component_section_when_none_registered()
    {
        TrackingComponentRegistry.Clear();

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.DoesNotContain("Tracking Components", html);
    }

    // ─── Unknown entries breakdown ─────────────────────────────

    [Fact]
    public void Unknown_entries_breakdown_shown_when_unknown_logs_exist()
    {
        var rrId1 = Guid.NewGuid();
        var rrId2 = Guid.NewGuid();

        var html = DiagnosticReportGenerator.BuildHtml(
            [
                MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Request, rrId1, serviceName: "CosmosDB", method: "GET containers/events/docs"),
                MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Response, rrId1, serviceName: "CosmosDB", method: "GET containers/events/docs"),
                MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Request, rrId2, serviceName: "Service Bus", method: "Publish"),
                MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Response, rrId2, serviceName: "Service Bus", method: "Publish"),
            ],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("Unknown Entries Breakdown", html);
        Assert.Contains("CosmosDB", html);
        Assert.Contains("Service Bus", html);
    }

    [Fact]
    public void Unknown_entries_breakdown_not_shown_when_no_unknown_logs()
    {
        var rrId = Guid.NewGuid();

        var html = DiagnosticReportGenerator.BuildHtml(
            [
                MakeLog("real-test-id", RequestResponseType.Request, rrId),
                MakeLog("real-test-id", RequestResponseType.Response, rrId),
            ],
            [],
            new ReportConfigurationOptions());

        Assert.DoesNotContain("Unknown Entries Breakdown", html);
    }

    [Fact]
    public void Unknown_entries_breakdown_groups_by_service_and_method()
    {
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 10; i++)
        {
            var rrId = Guid.NewGuid();
            logs.Add(MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Request, rrId, serviceName: "CosmosDB", method: "GET"));
            logs.Add(MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Response, rrId, serviceName: "CosmosDB", method: "GET"));
        }
        for (var i = 0; i < 4; i++)
        {
            var rrId = Guid.NewGuid();
            logs.Add(MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Request, rrId, serviceName: "CosmosDB", method: "POST"));
            logs.Add(MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Response, rrId, serviceName: "CosmosDB", method: "POST"));
        }

        var html = DiagnosticReportGenerator.BuildHtml(
            [.. logs],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("CosmosDB", html);
        Assert.Contains("GET", html);
        Assert.Contains("POST", html);
    }

    [Fact]
    public void Unknown_entries_breakdown_shows_entry_count()
    {
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 5; i++)
        {
            var rrId = Guid.NewGuid();
            logs.Add(MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Request, rrId, serviceName: "Redis", method: "GET"));
            logs.Add(MakeLog(TestIdentityScope.UnknownTestId, RequestResponseType.Response, rrId, serviceName: "Redis", method: "GET"));
        }

        var html = DiagnosticReportGenerator.BuildHtml(
            [.. logs],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("10", html); // 5 requests + 5 responses = 10 entries
    }

    // ─── HasHttpContextAccessor in diagnostic table (#09) ─────

    [Fact]
    public void Tracking_components_table_shows_HttpContextAccessor_column()
    {
        TrackingComponentRegistry.Register(
            new StubComponentWithAccessor("Handler (CosmosDB)", wasInvoked: true, hasAccessor: true));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("HttpContextAccessor", html);
        Assert.Contains("✓ configured", html);
    }

    [Fact]
    public void Tracking_components_table_shows_null_warning_for_active_component_without_accessor()
    {
        TrackingComponentRegistry.Register(
            new StubComponentWithAccessor("Handler (CosmosDB)", wasInvoked: true, hasAccessor: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("⚠ null", html);
    }

    [Fact]
    public void Tracking_components_table_shows_dash_for_inactive_component_without_accessor()
    {
        TrackingComponentRegistry.Register(
            new StubComponentWithAccessor("Handler (SomeQueue)", wasInvoked: false, hasAccessor: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.DoesNotContain("⚠ null", html);
    }

    // ─── Unmatched client names (#10) ──────────────────────────

    [Fact]
    public void Unmatched_client_names_section_shown_when_mismatches_exist()
    {
        UnmatchedClientNameRegistry.Clear();
        UnmatchedClientNameRegistry.Record("TenantHierarchyHttpClient");
        UnmatchedClientNameRegistry.Record("TenantHierarchyHttpClient");
        UnmatchedClientNameRegistry.Record("TenantHierarchyHttpClient");

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("Unmatched HTTP Client Names", html);
        Assert.Contains("TenantHierarchyHttpClient", html);
        Assert.Contains("3", html);
        UnmatchedClientNameRegistry.Clear();
    }

    [Fact]
    public void Unmatched_client_names_section_not_shown_when_no_mismatches()
    {
        UnmatchedClientNameRegistry.Clear();

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.DoesNotContain("Unmatched HTTP Client Names", html);
    }

    // ─── Component grouping (#11) ──────────────────────────────

    [Fact]
    public void Components_grouped_by_name_with_instance_count()
    {
        TrackingComponentRegistry.Register(new StubComponent("MessageTracker (Bus)", wasInvoked: true));
        TrackingComponentRegistry.Register(new StubComponent("MessageTracker (Bus)", wasInvoked: false));
        TrackingComponentRegistry.Register(new StubComponent("MessageTracker (Bus)", wasInvoked: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Contains("<summary>", html); // expandable detail
        Assert.Contains("3 instances", html);
    }

    [Fact]
    public void Never_invoked_warning_distinguishes_all_inactive_vs_some_inactive()
    {
        // Type with ALL instances inactive
        TrackingComponentRegistry.Register(new StubComponent("MessageTracker (Bus)", wasInvoked: false));
        TrackingComponentRegistry.Register(new StubComponent("MessageTracker (Bus)", wasInvoked: false));
        // Type with SOME instances active
        TrackingComponentRegistry.Register(new StubComponent("Handler (CosmosDB)", wasInvoked: true));
        TrackingComponentRegistry.Register(new StubComponent("Handler (CosmosDB)", wasInvoked: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        // Should identify the fully-inactive type as the real problem
        Assert.Contains("MessageTracker (Bus)", html);
        Assert.Contains("0 of 2", html); // 0 of 2 active
        Assert.Contains("1 of 2", html); // 1 of 2 active (some inactive is expected)
    }

    [Fact]
    public void No_never_invoked_warning_when_all_instances_active()
    {
        TrackingComponentRegistry.Register(new StubComponent("Handler (Cosmos)", wasInvoked: true));
        TrackingComponentRegistry.Register(new StubComponent("Handler (Cosmos)", wasInvoked: true));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.DoesNotContain("Never Invoked", html);
    }

    // ─── Helpers ───────────────────────────────────────────────

    private class StubComponent(string name, bool wasInvoked) : ITrackingComponent
    {
        public string ComponentName => name;
        public bool WasInvoked => wasInvoked;
        public int InvocationCount => wasInvoked ? 1 : 0;
    }

    private class StubComponentWithAccessor(string name, bool wasInvoked, bool hasAccessor) : ITrackingComponent
    {
        public string ComponentName => name;
        public bool WasInvoked => wasInvoked;
        public int InvocationCount => wasInvoked ? 1 : 0;
        public bool HasHttpContextAccessor => hasAccessor;
    }

    private static RequestResponseLog MakeLog(
        string testId,
        RequestResponseType type,
        Guid requestResponseId,
        string serviceName = "Svc",
        string method = "GET") =>
        new("Test", testId, (OneOf<HttpMethod, string>)method, null, new Uri("http://svc/api"),
            [], serviceName, "Caller", type, Guid.NewGuid(), requestResponseId, false)
        {
            Timestamp = DateTimeOffset.UtcNow
        };
}
