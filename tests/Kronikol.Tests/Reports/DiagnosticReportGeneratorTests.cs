using System.Net;
using System.Text.RegularExpressions;
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

    // ─── Where the page goes ───────────────────────────────────

    // Until 4.5.1 the page went to BaseDirectory joined with ReportsFolderPath, where every other output goes
    // to the run's directory: a blank folder put it one level above the run (outside Run.json, the rotation
    // and the upload), and a null one threw before the run could write its manifest.

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void The_page_goes_into_the_runs_directory_whatever_the_folder_option_holds(string? folder)
    {
        var runDirectory = Directory.CreateTempSubdirectory("kronikol-diagpage").FullName;
        try
        {
            using (ReportGenerator.ScopeReportsDirectory(runDirectory))
                DiagnosticReportGenerator.Generate([MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())], [],
                    new ReportConfigurationOptions { ReportsFolderPath = folder! });

            Assert.True(File.Exists(Path.Combine(runDirectory, "DiagnosticReport.html")));
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }

    [Fact]
    public void Outside_a_run_the_page_goes_where_the_options_name()
    {
        var folder = Directory.CreateTempSubdirectory("kronikol-diagpage-options").FullName;
        try
        {
            DiagnosticReportGenerator.Generate([MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())], [],
                new ReportConfigurationOptions { ReportsFolderPath = folder });

            Assert.True(File.Exists(Path.Combine(folder, "DiagnosticReport.html")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ─── What the page says ────────────────────────────────────

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

        Assert.Contains("<th>HttpContextAccessor</th>", html);
        Assert.Equal("<span class=\"info\">✓ 1 of 1</span>", AccessorCellOf(html, "Handler (CosmosDB)"));
    }

    [Fact]
    public void Tracking_components_table_warns_for_an_active_component_without_accessor()
    {
        TrackingComponentRegistry.Register(
            new StubComponentWithAccessor("Handler (CosmosDB)", wasInvoked: true, hasAccessor: false));

        var html = DiagnosticReportGenerator.BuildHtml(
            [MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())],
            [],
            new ReportConfigurationOptions());

        Assert.Equal("<span class=\"warn\">⚠ 0 of 1</span>", AccessorCellOf(html, "Handler (CosmosDB)"));
        Assert.DoesNotContain("null", AccessorCellOf(html, "Handler (CosmosDB)"));
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

        Assert.Equal("—", AccessorCellOf(html, "Handler (SomeQueue)"));
    }

    // ─── The accessor column counts instances (#134 R0, #137 section 2) ──
    // Until 4.9.1 the cell read one instance of an unordered bag (instances[0]) and printed "✓ configured" or a
    // literal "⚠ null", so a group of clients with and without an accessor read either way by registration order.

    public static readonly TheoryData<bool[], bool, string> AccessorCounts = new()
    {
        { new[] { true, true, false }, true, "2 of 3" },
        { new[] { false, true, true }, true, "2 of 3" },
        { new[] { true, true, true }, true, "<span class=\"info\">✓ 3 of 3</span>" },
        { new[] { true, true, true }, false, "<span class=\"info\">✓ 3 of 3</span>" },
        { new[] { false, false, false }, true, "<span class=\"warn\">⚠ 0 of 3</span>" },
        { new[] { false, false, false }, false, "—" },
        { new[] { true, false }, false, "1 of 2" },
    };

    [Theory]
    [MemberData(nameof(AccessorCounts))]
    public void The_accessor_cell_counts_the_instances_that_hold_one(bool[] accessors, bool invoked, string expected)
    {
        foreach (var hasAccessor in accessors)
            TrackingComponentRegistry.Register(new StubComponentWithAccessor("Client (Orders)", invoked, hasAccessor));

        var html = DiagnosticReportGenerator.BuildHtml([MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())], [], new ReportConfigurationOptions());

        Assert.Equal(expected, AccessorCellOf(html, "Client (Orders)"));
        Assert.DoesNotContain("⚠ null", html);
        Assert.DoesNotContain("✓ configured", html);
    }

    [Fact]
    public void The_same_instances_registered_in_either_order_read_the_same()
    {
        string CellFor(bool[] order)
        {
            TrackingComponentRegistry.Clear();
            foreach (var hasAccessor in order)
                TrackingComponentRegistry.Register(new StubComponentWithAccessor("Client (Orders)", true, hasAccessor));
            return AccessorCellOf(DiagnosticReportGenerator.BuildHtml([MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())], [], new ReportConfigurationOptions()), "Client (Orders)");
        }

        Assert.Equal("2 of 3", CellFor([true, true, false]));
        Assert.Equal("2 of 3", CellFor([false, true, true]));
    }

    [Fact]
    public void Each_instance_of_a_group_shows_whether_it_holds_an_accessor()
    {
        TrackingComponentRegistry.Register(new StubComponentWithAccessor("Client (Orders)", true, true));
        TrackingComponentRegistry.Register(new StubComponentWithAccessor("Client (Orders)", true, false));
        TrackingComponentRegistry.Register(new StubComponentWithAccessor("Client (Orders)", true, true));

        var html = DiagnosticReportGenerator.BuildHtml([MakeLog("t1", RequestResponseType.Request, Guid.NewGuid())], [], new ReportConfigurationOptions());

        var details = Regex.Match(html, @"<summary>Client \(Orders\) \(3 instances\)</summary>(?<rows>.*?)</details>", RegexOptions.Singleline);
        Assert.True(details.Success);
        Assert.Contains("<tr><th>#</th><th>Invocations</th><th>HttpContextAccessor</th></tr>", details.Groups["rows"].Value);
        Assert.Equal(2, Regex.Matches(details.Groups["rows"].Value, @"<td>1</td><td>✓</td></tr>").Count);
        Assert.Single(Regex.Matches(details.Groups["rows"].Value, @"<td>1</td><td>—</td></tr>"));
    }

    /// <summary>The HttpContextAccessor cell of a component group's row in the Tracking Components table.</summary>
    private static string AccessorCellOf(string html, string componentName)
    {
        var name = Regex.Escape(System.Net.WebUtility.HtmlEncode(componentName));
        var single = Regex.Match(html, $@"<tr><td>{name}</td><td>1</td><td>\d+</td><td>.*?</td><td>(?<cell>.*?)</td></tr>");
        if (single.Success)
            return single.Groups["cell"].Value;
        var group = Regex.Match(html, $@"<summary>{name} \(\d+ instances\)</summary>.*?</details></td>\s*<td>\d+</td><td>\d+</td><td>.*?</td><td>(?<cell>.*?)</td></tr>", RegexOptions.Singleline);
        Assert.True(group.Success, $"no row for {componentName}");
        return group.Groups["cell"].Value;
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
