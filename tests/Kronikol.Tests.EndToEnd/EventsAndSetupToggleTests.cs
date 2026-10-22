using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Events and Setup toggles (5.0.0, plans/OPTION_C_PLAN.md S9, D38). Events takes a sequence diagram's calls to and
/// from message brokers (the participants drawn as queues) out, with the broker; Setup takes the scenario's setup out:
/// its Setup frame where the report keeps setup apart, otherwise everything before its first When step, with the
/// participants only the setup called.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class EventsAndSetupToggleTests : PlaywrightTestBase
{
    public EventsAndSetupToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    // step bars as the emitter writes them (StepBarPlantUml.LegacyPrefix)
    private const string Given = "hnote across <<stepDelimiter>> #black:<color:white>Given stock exists";
    private const string When = "hnote across <<stepDelimiter>> #black:<color:white>When an order is placed";
    private const string Participants = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\n" +
        "queue \"Orders Topic\" as topic\nparticipant \"Seeder\" as seeder\n";
    private const string StepsSource = Participants + Given + "\nseeder -> stock : seed stock\n" + When +
        "\norders -> stock : reserve\norders -> topic : OrderPlaced\n@enduml";
    private const string FrameSource = Participants + "partition Setup\nseeder -> stock : seed stock\nend\n" +
        "orders -> stock : reserve\norders -> topic : OrderPlaced\n@enduml";

    // a note whose lines read like declarations, and a style block with a line that does
    private const string NoteLikeDeclarations = Participants + Given + "\nseeder -> stock : seed stock\n" + When +
        "\norders -> stock : reserve\nnote right\ndatabase connection opened\nqueue drained\nend note\norders -> topic : OrderPlaced\n@enduml";
    private static readonly string StyleLikeDeclaration = "@startuml\n<style>\nsequenceDiagram {\nparticipant {\nFontColor #C2185B\n}\n}\n</style>\n" +
        StepsSource.Substring("@startuml\n".Length);

    private async Task OpenAsync(string fileName, string source)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
    }

    private Task<string> DrawnAsync() => Page.EvaluateAsync<string>("() => document.querySelector(\"[data-diagram-type='plantuml'] svg\").textContent");

    // waits for the diagram to be drawn again without (or with) the text
    private Task WaitForAsync(string text, bool shown) => Page.WaitForFunctionAsync(
        "([t, on]) => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return s && s.textContent.includes(t) === on; }",
        new object[] { text, shown }, new() { Timeout = 20000, PollingInterval = 200 });

    [Fact]
    public async Task The_reports_Events_toggle_takes_the_brokers_calls_out_and_puts_them_back()
    {
        await OpenAsync("EventsToggleReport.html", StepsSource);
        Assert.Contains("OrderPlaced", await DrawnAsync());

        var toggle = Page.Locator("button[data-toggle='events']").First;
        await toggle.ClickAsync();
        await WaitForAsync("OrderPlaced", false);
        var drawn = await DrawnAsync();
        Assert.DoesNotContain("Orders Topic", drawn);
        Assert.Contains("reserve", drawn);
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-pressed"));

        await toggle.ClickAsync();
        await WaitForAsync("OrderPlaced", true);
        Assert.Contains("Orders Topic", await DrawnAsync());
    }

    [Fact]
    public async Task A_scenarios_Setup_toggle_takes_out_what_comes_before_its_When_step()
    {
        await OpenAsync("SetupToggleScenario.html", StepsSource);
        Assert.Contains("seed stock", await DrawnAsync());

        await OpenScenarioSettingsAsync();
        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='setup']").ClickAsync();
        await WaitForAsync("seed stock", false);
        var drawn = await DrawnAsync();
        Assert.DoesNotContain("Given stock exists", drawn);
        Assert.DoesNotContain("Seeder", drawn);
        Assert.Contains("When an order is placed", drawn);
        Assert.Contains("reserve", drawn);
    }

    // Once the setup was out, the declarations of the participants nothing called went too, read from every line: a
    // note's line that read like one ("database connection opened") went with them, and so did a style block's
    // participant { line, which left the block's braces unmatched (5.0.0 to 5.2.6)
    [Fact]
    public async Task A_notes_line_that_reads_like_a_declaration_stays_when_the_setup_goes()
    {
        await OpenAsync("SetupToggleNoteLines.html", NoteLikeDeclarations);
        Assert.Contains("database connection opened", await DrawnAsync());

        await OpenScenarioSettingsAsync();
        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='setup']").ClickAsync();
        await WaitForAsync("seed stock", false);
        var drawn = await DrawnAsync();
        Assert.Contains("database connection opened", drawn);
        Assert.Contains("queue drained", drawn);
        Assert.DoesNotContain("Seeder", drawn);
    }

    [Fact]
    public async Task A_style_blocks_lines_stay_when_the_setup_goes()
    {
        const string RedNames = "() => Array.from(document.querySelectorAll(\"[data-diagram-type='plantuml'] svg text\")).filter(t => /^#c2185b$/i.test(t.getAttribute('fill') || '')).map(t => t.textContent).join('|')";
        // the participants' names in the style's colour, and no call's: without its participant { line the colour was the
        // whole diagram's
        await OpenAsync("SetupToggleStyleLines.html", StyleLikeDeclaration);
        Assert.Contains("Orders", await Page.EvaluateAsync<string>(RedNames));
        Assert.DoesNotContain("reserve", await Page.EvaluateAsync<string>(RedNames));

        await OpenScenarioSettingsAsync();
        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='setup']").ClickAsync();
        await WaitForAsync("seed stock", false);
        Assert.Contains("reserve", await DrawnAsync());
        Assert.Contains("Orders", await Page.EvaluateAsync<string>(RedNames));
        Assert.DoesNotContain("reserve", await Page.EvaluateAsync<string>(RedNames));
    }

    // a style block on one line closes where it opens: the participants nothing calls still go after it
    [Fact]
    public async Task The_setup_goes_with_its_participants_after_a_style_block_on_one_line()
    {
        await OpenAsync("SetupToggleOneLineStyle.html", "@startuml\n<style> sequenceDiagram { arrow { FontColor #1565C0 } } </style>\n" + StepsSource.Substring("@startuml\n".Length));
        await OpenScenarioSettingsAsync();
        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='setup']").ClickAsync();
        await WaitForAsync("seed stock", false);
        var drawn = await DrawnAsync();
        Assert.DoesNotContain("Seeder", drawn);
        Assert.Contains("reserve", drawn);
    }

    [Fact]
    public async Task The_setup_goes_with_the_step_bars_hidden_too()
    {
        // the setup is found by the When step bar, which the steps filter takes out: the setup filter runs first
        await OpenAsync("SetupToggleStepsHidden.html", StepsSource);
        await OpenScenarioSettingsAsync();
        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='steps']").ClickAsync();
        await WaitForAsync("When an order is placed", false);

        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='setup']").ClickAsync();
        await WaitForAsync("seed stock", false);
        var drawn = await DrawnAsync();
        Assert.DoesNotContain("Seeder", drawn);
        Assert.Contains("reserve", drawn);
    }

    [Fact]
    public async Task The_Setup_toggle_takes_a_Setup_frame_out_whole()
    {
        await OpenAsync("SetupToggleFrame.html", FrameSource);
        Assert.Contains("seed stock", await DrawnAsync());

        await Page.Locator("button[data-toggle='setup']").First.ClickAsync();
        await WaitForAsync("seed stock", false);
        var drawn = await DrawnAsync();
        Assert.DoesNotContain("Seeder", drawn);
        Assert.Contains("reserve", drawn);
        Assert.Contains("OrderPlaced", drawn);
    }
}
