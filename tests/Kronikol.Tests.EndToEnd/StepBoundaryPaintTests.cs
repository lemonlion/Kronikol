using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// R2 of plans/STEP_PHASE_PLAN.md (5.6) on the page: a report from real <see cref="StepCollector"/> steps and a real
/// <see cref="MessageTracker"/>, so the boundary is the one a step draws. A suite of step attributes had none, a Given that only
/// sends a message had no partition, and a first act under a later boundary would be hidden by the page's Setup filter.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class StepBoundaryPaintTests : DiagramNotePlaywrightBase
{
    public StepBoundaryPaintTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string SourceOf(params (string? Keyword, string Message)[] steps)
    {
        var id = $"step-boundary-paint-{Guid.NewGuid():N}";
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Test",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = () => ("Collect milk", id),
        });

        foreach (var (keyword, message) in steps)
        {
            // No keyword: the test's own body, between its steps.
            if (keyword is not null)
                StepCollector.StartStep(id, keyword, message, null, null);
            tracker.TrackSendEvent("bus", message, new Uri($"bus://dairy/{message}"));
            if (keyword is not null)
                StepCollector.CompleteStep(id, passed: true);
        }

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == id).ToList();
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true, systemUiFont: true).Single().PlantUmls.Single().PlainText;
    }

    private async Task OpenAsync(string fileName, string source)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Dairy",
                Scenarios = [new() { Id = "s1", DisplayName = "Collect milk", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Dairy", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
    }

    private Task<string> PaintedAsync() => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string>("el => el.textContent");

    private Task WaitForPaintedAsync(string text, bool shown) => Page.WaitForFunctionAsync(
        "([t, on]) => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && s.textContent.includes(t) === on; }",
        new object[] { text, shown }, new() { Timeout = 30000, PollingInterval = 200 });

    private async Task ClickToggleAsync(string toggle)
    {
        await OpenScenarioSettingsAsync();
        await Page.Locator($".scenario-settings .toggle-btn[data-toggle='{toggle}']").First.ClickAsync();
    }

    // Whether the picture draws the Setup box, by its title.
    private Task<bool> PaintsSetupAsync() => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<bool>(
        "el => Array.from(el.querySelectorAll('text')).some(t => t.textContent === 'Setup')");

    [Fact]
    public async Task The_Setup_frame_goes_round_the_Givens_message_and_not_the_Whens_and_the_filter_takes_it_out()
    {
        var source = SourceOf(("Given", "seeded"), ("When", "acted"));
        var open = Regex.Match(source, @"(?m)^partition\b.*\bSetup\s*$").Index;
        Assert.True(open > 0, source);
        var seeded = source.IndexOf("bus: /seeded", StringComparison.Ordinal);
        var end = Regex.Match(source[open..], @"(?m)^end\s*$").Index + open;
        var acted = source.IndexOf("bus: /acted", StringComparison.Ordinal);
        Assert.True(open < seeded && seeded < end && end < acted, source);

        await OpenAsync("StepBoundaryFrame.html", source);

        Assert.True(await PaintsSetupAsync());
        Assert.Contains("/seeded", await PaintedAsync());

        await ClickToggleAsync("setup");
        await WaitForPaintedAsync("/seeded", false);

        var painted = await PaintedAsync();
        Assert.DoesNotContain("seeded", painted);
        Assert.Contains("/acted", painted);
        Assert.False(await PaintsSetupAsync());
    }

    // review2/A01: an act in the test's body, between a [GivenStep] and a [ThenStep]: a partition up to the Then would hold it, and the page's Setup filter
    // would hide it. With no frame and no When there is no setup to take out, so the page does not offer the filter.
    [Fact]
    public async Task A_Given_an_act_in_the_body_and_a_Then_paint_no_frame_and_offer_no_Setup_filter()
    {
        var source = SourceOf(("Given", "arranged"), (null, "the-act"), ("Then", "checked"));
        Assert.DoesNotMatch(@"(?m)^partition\b.*\bSetup\s*$", source);

        await OpenAsync("StepBoundaryActInBody.html", source);

        Assert.False(await PaintsSetupAsync());
        Assert.Contains("/the-act", await PaintedAsync());

        await OpenScenarioSettingsAsync();
        Assert.Equal(0, await Page.Locator(".scenario-settings .toggle-btn[data-toggle='setup']").CountAsync());
    }

    // review/E05: a Setup partition over the first act, and the page's Setup filter would hide that act. The filter leaves this source as it
    // was, so nothing is drawn again for it: the Steps filter's redraw is the one to wait for, as it applies them all.
    [Fact]
    public async Task When_Then_Given_When_paints_no_frame_and_the_filter_leaves_the_first_act()
    {
        var source = SourceOf(("When", "first-act"), ("Then", "checked"), ("Given", "seeded-late"), ("When", "second-act"));
        Assert.DoesNotMatch(@"(?m)^partition\b.*\bSetup\s*$", source);

        await OpenAsync("StepBoundaryNoFrame.html", source);

        Assert.False(await PaintsSetupAsync());
        Assert.Contains("When first-act", await PaintedAsync());

        await ClickToggleAsync("setup");
        await ClickToggleAsync("steps");
        await WaitForPaintedAsync("When first-act", false);

        Assert.Contains("/first-act", await PaintedAsync());
    }
}
