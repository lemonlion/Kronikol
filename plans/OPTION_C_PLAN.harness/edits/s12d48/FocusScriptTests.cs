using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The script that keeps focus the keyboard moves clear of the pinned report bar (5.0.0, plans/OPTION_C_PLAN.md S12, D48)
/// is in the head of every page that has the bar, after the tabs script, as report-focus-script.js holds it, and in no
/// page without the bar. What it does in a browser is held by Kronikol.Tests.EndToEnd's FocusClearOfTheBarTests.
/// </summary>
public class FocusScriptTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 19, 9, 30, 0, DateTimeKind.Utc);

    private const string ComponentSource = "@startuml\nrectangle \"Caller\" as caller\nrectangle \"OrderService\" as svc\ncaller --> svc\n@enduml";

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-focus-script").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Features(bool durations) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = "a", DisplayName = "Create order", Result = ExecutionResult.Passed, Duration = durations ? TimeSpan.FromMilliseconds(120) : null },
                new Scenario { Id = "b", DisplayName = "Reject order", Result = ExecutionResult.Passed, Duration = durations ? TimeSpan.FromMilliseconds(80) : null }
            ]
        }
    ];

    private IDocument Generate(string fileName, bool durations, string? component, bool includeTestRunData = true)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], Features(durations), Start, Start.AddSeconds(5), null, Path.Combine(_dir, fileName), "Orders", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, componentDiagramPlantUml: component);
        return BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(File.ReadAllText(path))).GetAwaiter().GetResult();
    }

    private static string Resource(string name)
    {
        var assembly = typeof(ReportGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData("a timeline", true, null)]
    [InlineData("a component diagram", false, ComponentSource)]
    public void A_page_with_the_report_bar_has_the_focus_script_in_its_head_after_the_tabs_script(string beside, bool durations, string? component)
    {
        var page = Generate($"Focus_Script_{(durations ? "Timeline" : "Components")}.html", durations, component);

        Assert.NotNull(page.QuerySelector("div.toolbar-row.report-nav"));
        var scripts = page.Head!.QuerySelectorAll("script#kronikol-focus");
        Assert.True(scripts.Length == 1, $"a page with {beside} has the focus script once in its head, not {scripts.Length} times");
        var tabs = page.Head!.QuerySelector("script#kronikol-report-tabs")!;
        Assert.True(tabs.CompareDocumentPosition(scripts[0]).HasFlag(DocumentPositions.Following), "after the tabs script");
        Assert.Equal(Resource("report-focus-script.js"), scripts[0].TextContent);
        Assert.Contains("e.key !== 'Tab'", scripts[0].TextContent);
        Assert.Contains("'.toolbar-row.report-nav'", scripts[0].TextContent);
        Assert.Contains("'.scenario-settings.is-open'", scripts[0].TextContent);
    }

    [Theory]
    [InlineData("nothing beside its features", true)]
    [InlineData("the specifications report", false)]
    public void A_page_without_the_report_bar_has_no_focus_script(string which, bool includeTestRunData)
    {
        var page = Generate($"Focus_Script_None_{includeTestRunData}.html", durations: false, component: null, includeTestRunData);

        Assert.Null(page.QuerySelector("div.toolbar-row.report-nav"));
        Assert.True(page.QuerySelector("script#kronikol-focus") is null, $"{which} has no focus script");
        Assert.DoesNotContain(page.QuerySelectorAll("script"), s => s.TextContent.Contains("e.key !== 'Tab'", StringComparison.Ordinal));
    }
}
