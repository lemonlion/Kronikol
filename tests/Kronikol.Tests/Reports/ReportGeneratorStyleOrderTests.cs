using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The order of the stylesheets in a report's head: the base sheet, the component sheets, then the
/// custom sheet (the specifications theme, or a user's), then InternalFlowPopupCustomStyleSheet, then
/// CustomCss. A custom sheet that came before the component sheets lost to them at equal specificity,
/// which painted the default violet Specifications.html's Details radio Google blue
/// (plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md §2.4, §3.3, §3.7, §5 T3).
/// </summary>
[Collection("DiagramsFetcher")]
public class ReportGeneratorStyleOrderTests : IDisposable
{
    private const string Theme = "/*THEME*/";
    private const string PopupSheet = "/*POPUP*/";
    private const string Custom = "/*CUSTOM*/";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-style-order-" + Guid.NewGuid().ToString("N"));

    public ReportGeneratorStyleOrderTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static Feature[] Features(string id) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = id, DisplayName = "Create an order", IsHappyPath = true, Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromMilliseconds(100),
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private string Generate(string? stylesheet, bool internalFlowTracking, string? customCss = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("t1", "", "@startuml\nA -> B : hello\n@enduml")], Features("t1"),
            DateTime.UtcNow, DateTime.UtcNow,
            stylesheet, Path.Combine(_dir, $"StyleOrder-{Guid.NewGuid():N}.html"), "Style order", includeTestRunData: false,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: internalFlowTracking, customCss: customCss);
        return File.ReadAllText(path);
    }

    private static int EndOf(string html, string sheet)
    {
        var at = html.IndexOf(sheet, StringComparison.Ordinal);
        Assert.True(at >= 0, "the report does not carry the sheet verbatim");
        return at + sheet.Length;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_custom_sheet_follows_every_component_sheet_and_precedes_custom_css(bool internalFlowTracking)
    {
        var html = Generate(Theme, internalFlowTracking, $"body {{ }} {Custom}");
        var theme = html.IndexOf(Theme, StringComparison.Ordinal);

        Assert.True(theme > EndOf(html, Stylesheets.HtmlReportStyleSheet), "after the base sheet");
        Assert.True(theme > EndOf(html, DiagramContextMenu.GetStyles()), "after the context-menu sheet");
        Assert.True(theme > EndOf(html, DiagramContextMenu.GetInlineSvgStyles()), "after the inline-SVG sheet");
        Assert.True(theme > EndOf(html, DiagramContextMenu.GetCollapsibleNotesStyles()), "after the notes sheet (the Details radio)");
        if (internalFlowTracking)
            Assert.True(theme > EndOf(html, DiagramContextMenu.GetInternalFlowPopupStyles()), "after the internal-flow sheet (the tabs, the flow toggle)");
        Assert.True(theme < html.IndexOf(Custom, StringComparison.Ordinal), "before CustomCss");
        Assert.Equal(1, html.Split(Theme).Length - 1);
    }

    /// <summary>The custom sheet used to share a line with the base sheet, and the newline that line
    /// ended is kept, so a report without a custom sheet keeps its bytes (plan §3.3).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_custom_sheet_the_base_sheet_is_followed_by_the_newline_it_always_was(string? stylesheet)
    {
        var html = Generate(stylesheet, internalFlowTracking: true);
        var baseEnd = EndOf(html, Stylesheets.HtmlReportStyleSheet);
        var next = html.IndexOf(DiagramContextMenu.GetStyles(), baseEnd, StringComparison.Ordinal);
        Assert.Matches(@"^\n\r?\n *$", html[baseEnd..next]);
        Assert.DoesNotContain(Theme, html);
    }

    // ── UserStylesheets: the sheets a report carries after its built-in ones (S5) ──

    [Theory]
    [InlineData(null, null, true, null)]
    [InlineData("T", null, true, "T")]
    [InlineData(null, "P", true, "P")]
    [InlineData("T", "P", true, "T\nP")]
    [InlineData("T", "P", false, "T")]
    [InlineData(null, "P", false, null)]
    [InlineData("", "P", true, "P")]
    [InlineData("T", "", true, "T")]
    public void User_stylesheets_are_the_theme_then_the_popup_sheet_when_flow_tracking_is_on(
        string? theme, string? popup, bool flow, string? expected)
    {
        var options = new ReportConfigurationOptions { InternalFlowTracking = flow, InternalFlowPopupCustomStyleSheet = popup };
        Assert.Equal(expected, ReportGenerator.UserStylesheets(theme, options));
    }

    // ── Through the options, both reports ──

    private (string Specifications, string TestRun) Run(bool internalFlowTracking)
    {
        var dir = Path.Combine(_dir, internalFlowTracking ? "flow" : "noflow");
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = dir,
            InternalFlowTracking = internalFlowTracking,
            GenerateComponentDiagram = false,
            HtmlSpecificationsCustomStyleSheet = Theme,
            InternalFlowPopupCustomStyleSheet = $"{PopupSheet} .iflow-toggle-active {{ background: rgb(1, 2, 3); }}",
            CustomCss = Custom,
        };
        ReportGenerator.CreateStandardReportsWithDiagrams(Features("style-order-" + Guid.NewGuid().ToString("N")),
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        string Read(string name)
        {
            var path = Path.Combine(dir, name);
            Assert.True(File.Exists(path), $"{name} was not written");
            return File.ReadAllText(path);
        }
        return (Read("Specifications.html"), Read("TestRunReport.html"));
    }

    [Fact]
    public void With_flow_tracking_the_popup_sheet_follows_the_theme_and_precedes_custom_css_in_both_reports()
    {
        var (specifications, testRun) = Run(internalFlowTracking: true);

        int At(string html, string marker) => html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(At(specifications, Theme) >= 0 && At(specifications, Theme) < At(specifications, PopupSheet),
            "Specifications.html: the theme, then the popup sheet");
        Assert.True(At(specifications, PopupSheet) < At(specifications, Custom), "Specifications.html: the popup sheet before CustomCss");
        Assert.True(At(specifications, PopupSheet) > EndOf(specifications, DiagramContextMenu.GetInternalFlowPopupStyles()),
            "Specifications.html: the popup sheet after the built-in popup styles");

        Assert.Equal(-1, At(testRun, Theme));
        Assert.True(At(testRun, PopupSheet) > EndOf(testRun, DiagramContextMenu.GetInternalFlowPopupStyles()),
            "TestRunReport.html: the popup sheet after the built-in popup styles");
        Assert.True(At(testRun, PopupSheet) < At(testRun, Custom), "TestRunReport.html: the popup sheet before CustomCss");
    }

    [Fact]
    public void Without_flow_tracking_neither_report_carries_the_popup_sheet()
    {
        var (specifications, testRun) = Run(internalFlowTracking: false);

        Assert.DoesNotContain(PopupSheet, specifications);
        Assert.DoesNotContain(PopupSheet, testRun);
        Assert.Contains(Theme, specifications);
    }
}
