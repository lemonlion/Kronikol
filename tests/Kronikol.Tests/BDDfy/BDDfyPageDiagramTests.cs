using AngleSharp;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// #228 F6 (plans/PARTICIPANT_ORDER_PLAN.md R4): what BDDfy's own page shows for each scenario's diagram, by what the
/// renderer gave it. It wrote every diagram into an <c>img</c>, whose address the default renderer leaves empty, since it
/// draws in the report's own page: BDDfy's page showed no diagram, and no test read it.
/// </summary>
public class BDDfyPageDiagramTests
{
    private const string Source = "@startuml\nCaller -> App\n@enduml";
    private static readonly string PageDirectory = Path.Combine(Path.GetTempPath(), "bddfy-page-" + Guid.NewGuid().ToString("N")[..8]);

    private static DefaultDiagramsFetcher.DiagramAsCode Diagram(string imgSrc, string testId = "t1") => new(testId, imgSrc, Source);

    // The report in Reports/ beside the page, as the adapters write it by default.
    private static ReportLinks.Snapshot Report(string folder = "Reports") =>
        new(Path.Combine(PageDirectory, folder, "TestRunReport.html"), new Dictionary<string, string> { ["t1"] = "sid-1a2b" });

    private static string Html(DefaultDiagramsFetcher.DiagramAsCode diagram, ReportLinks.Snapshot? report, bool lazyLoad = true) =>
        ReportLinks.DiagramEntryHtml(diagram, lazyLoad, PageDirectory, report);

    [Fact]
    public void A_diagram_the_report_draws_itself_links_to_its_scenario_there()
    {
        var html = Html(Diagram(""), Report());

        Assert.Contains("<a href=\"Reports/TestRunReport.html#sid-1a2b\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("<pre>@startuml\nCaller -&gt; App\n@enduml</pre>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_report_written_the_entry_says_where_the_diagram_is()
    {
        var html = Html(Diagram(""), report: null);

        Assert.Contains("The sequence diagram is drawn in the Kronikol report.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_the_report_does_not_hold_says_where_the_diagram_is()
    {
        var html = Html(Diagram("", testId: "elsewhere"), Report());

        Assert.Contains("The sequence diagram is drawn in the Kronikol report.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_svg_is_drawn_as_it_is()
    {
        var html = Html(Diagram("<svg xmlns=\"http://www.w3.org/2000/svg\"><text>Caller</text></svg>"), Report());

        Assert.Contains("<summary><svg xmlns=\"http://www.w3.org/2000/svg\"><text>Caller</text></svg></summary>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://www.plantuml.com/plantuml/svg/SoWkIImgAStDuNBAJrBGjLDmpCbCJbMmKiX8pSd9vt98pKi1IW80", true)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=", false)]
    public void A_picture_address_is_drawn_as_before(string address, bool lazyLoad)
    {
        // The bytes 5.33.0 wrote, where its img had an address to load.
        var expected = $"<details><summary><img{(lazyLoad ? " loading=\"lazy\"" : "")} src=\"{address}\"></summary>"
                       + "<h4>Raw PlantUML</h4><pre>@startuml\nCaller -&gt; App\n@enduml</pre></details>";

        Assert.Equal(expected, Html(Diagram(address), Report(), lazyLoad));
    }

    [Fact]
    public void A_file_the_local_renderer_wrote_is_found_from_the_report_folder()
    {
        var html = Html(Diagram("diagrams/diagram_1.svg"), Report());

        Assert.Contains("src=\"Reports/diagrams/diagram_1.svg\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_escapes_each_part_of_its_path()
    {
        var html = Html(Diagram(""), Report("Test Reports"));

        Assert.Contains("<a href=\"Test%20Reports/TestRunReport.html#sid-1a2b\">", html, StringComparison.Ordinal);
    }
}

/// <summary>R4's pipeline half: the report says where each scenario sits in it, as its own page names them.</summary>
[Collection("DiagramsFetcher")]
public class BDDfyPageReportLinkTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-bddfy-links").FullName;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_report_records_each_scenarios_anchor_as_its_page_names_it()
    {
        var testId = "bddfy-link-" + _tag;
        RequestResponseLogger.LogPair("Run", testId, HttpMethod.Get, new Uri("http://app/run"), "App" + _tag, "Caller" + _tag);
        var reports = Path.Combine(_dir, "Reports");
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [new Feature { DisplayName = "BDDfy " + _tag, Scenarios = [new Scenario { Id = testId, DisplayName = "Runs", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            new ReportConfigurationOptions { ReportsFolderPath = reports, GenerateSpecificationsReport = false, GenerateSpecificationsData = false });

        var last = ReportLinks.Last;
        Assert.NotNull(last);
        Assert.Equal(Path.GetFullPath(Path.Combine(reports, "TestRunReport.html")), last.HtmlPath);
        var anchor = last.AnchorsByTestId[testId];
        var page = BrowsingContext.New(Configuration.Default)
            .OpenAsync(req => req.Content(ReportHtml.Read(last.HtmlPath))).GetAwaiter().GetResult();
        Assert.NotNull(page.QuerySelector($"[data-stable-id='{anchor["sid-".Length..]}']"));
    }
}
