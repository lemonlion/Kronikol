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

    private static string SourceOfScript(Action<string, MessageTracker> script)
    {
        var id = $"step-boundary-paint-{Guid.NewGuid():N}";
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Test",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = () => ("Collect milk", id),
        });

        script(id, tracker);

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == id).ToList();
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true, systemUiFont: true).Single().PlantUmls.Single().PlainText;
    }

    private static void Sends(MessageTracker tracker, string message) => tracker.TrackSendEvent("bus", message, new Uri($"bus://dairy/{message}"));

    private async Task FilterTakesOutAsync(string fileName, string source, string[] gone, string[] kept)
    {
        await OpenAsync(fileName, source);
        Assert.True(await PaintsSetupAsync());

        await ClickToggleAsync("setup");
        await WaitForPaintedAsync($"/{gone[0]}", false);

        var painted = await PaintedAsync();
        foreach (var message in gone)
            Assert.DoesNotContain($"/{message}", painted);
        foreach (var message in kept)
            Assert.Contains($"/{message}", painted);
    }

    // Q5: an explicit StartAction() inside the step that drew the boundary moves it, and the filter goes by where it moved to.
    [Fact]
    public async Task A_StartAction_inside_the_When_moves_the_frame_past_what_came_before_it()
    {
        var source = SourceOfScript((id, tracker) =>
        {
            StepCollector.StartStep(id, "Given", "seeded", null, null);
            Sends(tracker, "seeded");
            StepCollector.CompleteStep(id, passed: true);
            StepCollector.StartStep(id, "When", "acted", null, null);
            Sends(tracker, "before-explicit");
            DefaultTrackingDiagramOverride.StartAction(id);
            Sends(tracker, "after-explicit");
            StepCollector.CompleteStep(id, passed: true);
        });

        await FilterTakesOutAsync("StepBoundaryExplicit.html", source, ["seeded", "before-explicit"], ["after-explicit"]);
    }

    // Q9: a bypassed When moves the phase, so it draws the boundary, and a Given after it stays out of the frame, as a When after that
    // does not draw it again.
    [Fact]
    public async Task A_bypassed_When_closes_the_frame_and_a_Given_after_it_stays_on_the_page()
    {
        var source = SourceOfScript((id, tracker) =>
        {
            StepCollector.StartStep(id, "Given", "first-given", null, null);
            Sends(tracker, "first-given");
            StepCollector.CompleteStep(id, passed: true);
            StepCollector.StartStep(id, "When", "bypassed", null, null);
            StepCollector.BypassStep(id, "not today");
            StepCollector.StartStep(id, "Given", "later-given", null, null);
            Sends(tracker, "later-given");
            StepCollector.CompleteStep(id, passed: true);
            StepCollector.StartStep(id, "When", "real-act", null, null);
            Sends(tracker, "real-act");
            StepCollector.CompleteStep(id, passed: true);
        });

        await FilterTakesOutAsync("StepBoundaryBypassed.html", source, ["first-given"], ["later-given", "real-act"]);
    }

    // Q13: a When in tear-down, on a plain suite whose body has no When, draws the boundary, so the body sits in the frame.
    [Fact]
    public async Task A_When_in_tear_down_after_a_body_with_no_When_closes_the_frame_after_the_body()
    {
        var source = SourceOf(("Given", "arranged"), (null, "the-body"), ("When", "torn-down"));

        await FilterTakesOutAsync("StepBoundaryTearDown.html", source, ["arranged", "the-body"], ["torn-down"]);
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
