using System.Globalization;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The parts of a report that kept colours of their own after the report took its tokens, in a browser, light and dark
/// (5.0.0, plans/OPTION_C_PLAN.md S13b): the diagram's right-click menu, an attachment the report does not hold, a
/// parameterized table's flatten toggle, the line that says a scenario captured no interactions, and a scenario's history
/// sparkline. Each takes its colours from the theme's tokens, which the facts read as the page resolves them, through a
/// probe element, and its text reads at 4.5:1 or more on what it is painted on. The menu was white and had no ink of its
/// own, so the dark theme drew its light ink on white; the other parts kept 4.x's greys and reds in every theme, and the
/// sparkline painted its runs in hex colours.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class DarkThemeColoursTests : PlaywrightTestBase
{
    public DarkThemeColoursTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string NotFound = "not found when the report was drawn";

    /// <summary>
    /// The Refund scenario's six earlier runs, oldest first: a failure, a skip, a run it was absent from, an unknown result,
    /// a position that was not a test and a bypass. It passes in this run, so its sparkline holds a run of every kind.
    /// </summary>
    private const string RefundRuns = "FS.?NB";

    private static ColorScheme Scheme(string scheme) => scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;

    private Task<string?> ThemeAsync() =>
        Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-theme')");

    // A token as the page resolves it: a probe element turns it into the rgb() form getComputedStyle gives.
    private const string Token =
        "const tok = n => { const p = document.createElement('div'); p.style.color = 'var(' + n + ')'; document.body.appendChild(p); "
        + "const c = getComputedStyle(p).color; p.remove(); return c; };";

    // What an element's text is painted on: the first background from the element up that is not transparent; one only
    // partly opaque is laid over the ones under it, down to the first opaque one (white when there is none).
    private const string Ground = """
        const ground = el => {
            const layers = [];
            for (let e = el; e; e = e.parentElement) {
                const bg = getComputedStyle(e).backgroundColor;
                if (!bg.startsWith('rgb')) throw new Error('a background the walk cannot read: ' + bg);
                const [r, g, b, a = 1] = bg.match(/[0-9.]+/g).map(Number);
                if (a === 0) continue;
                layers.unshift([r, g, b, a]);
                if (a === 1) break;
            }
            let c = [255, 255, 255];
            for (const [r, g, b, a] of layers) c = [r, g, b].map((v, i) => v * a + c[i] * (1 - a));
            return 'rgb(' + c.map(Math.round).join(', ') + ')';
        };
        """;

    /// <summary>The diagram's right-click menu on the card under the page's ink, where it was white in every theme and had no
    /// ink of its own, so the dark theme drew its light ink on white.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_diagram_menu_is_drawn_on_the_card_in_the_pages_ink(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await Page.GotoReportAsync(GenerateReport($"DarkColoursMenu-{scheme}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.Equal(scheme, await ThemeAsync());
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();

        // the pointer away from where the menu opens, so no item reads hovered
        await Page.Mouse.MoveAsync(0, 0);
        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu").First;
        await menu.WaitForAsync(new() { Timeout = 5000 });

        var r = await menu.EvaluateAsync<string[]>($$"""
            menu => {
                {{Token}}
                {{Ground}}
                const item = menu.querySelector(':scope > div');
                return [getComputedStyle(menu).backgroundColor, tok('--kron-card'),
                        getComputedStyle(item).color, tok('--kron-ink'), ground(item)];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.True(Contrast(r[2], r[4]) >= 4.5, $"the menu's first item {r[2]} on {r[4]}: {Contrast(r[2], r[4]):F2}:1");
    }

    /// <summary>An attachment the report does not hold names itself in the fail colour, where its red was one value in every
    /// theme and too dark to read on the dark card.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task An_attachment_the_report_does_not_hold_is_named_in_the_fail_colour(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await OpenMissingAttachmentsReport($"DarkColoursMissing-{scheme}.html");
        Assert.Equal(scheme, await ThemeAsync());

        // the scenario's own, which the report writes as its name and why
        var missing = Page.Locator("span.attachment-missing", new() { HasTextString = $"lost.png ({NotFound})" }).First;
        await Expect(missing).ToBeVisibleAsync();
        var r = await missing.EvaluateAsync<string[]>($$"""
            el => {
                {{Token}}
                {{Ground}}
                return [getComputedStyle(el).color, tok('--kron-fail'), ground(el)];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.True(Contrast(r[0], r[2]) >= 4.5, $"the attachment's name {r[0]} on {r[2]}: {Contrast(r[0], r[2]):F2}:1");
    }

    /// <summary>A parameterized table's flatten toggle in the muted ink inside a control's border, where both were 4.x's greys
    /// and its sign too dark to read on the dark header.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_flatten_toggle_takes_the_muted_ink_inside_a_controls_border(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await Page.GotoReportAsync(GenerateFlatReport($"DarkColoursFlatten-{scheme}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.Equal(scheme, await ThemeAsync());
        await ExpandAllFeaturesAsync();
        var group = Page.Locator("details.scenario-parameterized").First;
        if (await group.GetAttributeAsync("open") == null)
            await group.Locator("summary").First.ClickAsync();

        // the flat table's, shown by default
        var toggle = group.Locator("button.flatten-toggle").First;
        await Expect(toggle).ToBeVisibleAsync();
        // the pointer off the toggle, whose hover has colours of its own
        await Page.Mouse.MoveAsync(0, 0);
        var r = await toggle.EvaluateAsync<string[]>($$"""
            el => {
                {{Token}}
                {{Ground}}
                const cs = getComputedStyle(el);
                return [cs.color, tok('--kron-muted'), cs.borderTopColor, tok('--kron-control-border'), ground(el)];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.True(Contrast(r[0], r[4]) >= 4.5, $"the toggle's sign {r[0]} on {r[4]}: {Contrast(r[0], r[4]):F2}:1");
    }

    /// <summary>The line that says a scenario captured no interactions in the faint ink, where its grey was one value in every
    /// theme and too light to read on the light card.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_line_that_says_no_interactions_were_captured_takes_the_faint_ink(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        await Page.GotoReportAsync(GenerateReportWithoutInteractions($"DarkColoursNoInteractions-{scheme}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.Equal(scheme, await ThemeAsync());
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var line = Page.Locator(".no-interactions[data-no-interactions]").First;
        await Expect(line).ToBeVisibleAsync();
        await Expect(line).ToHaveTextAsync("No interactions captured for this scenario.");
        var r = await line.EvaluateAsync<string[]>($$"""
            el => {
                {{Token}}
                {{Ground}}
                return [getComputedStyle(el).color, tok('--kron-faint'), ground(el)];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.True(Contrast(r[0], r[2]) >= 4.5, $"the line {r[0]} on {r[2]}: {Contrast(r[0], r[2]):F2}:1");
    }

    /// <summary>A scenario's sparkline paints each run in its kind's token, where its runs were hex colours, an absent run
    /// bright on the dark card. The gradient the page writes is the whole paint: the inline <c>background</c> shorthand
    /// leaves no ground of the sheet's under it.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_sparkline_paints_each_run_in_its_kinds_token(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = Scheme(scheme) });
        // the Refund scenario is the roster's second position (HistoryReportHelper.Seed)
        await Page.GotoReportAsync(HistoryReportHelper.Generate(TempDir, OutputDir, $"DarkColoursSparkline-{scheme}.html",
            (i, run) => run with { Results = run.Results[..1] + RefundRuns[i] + run.Results[2..] }));
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.Equal(scheme, await ThemeAsync());
        // features start collapsed, and a collapsed feature hides its scenario headers
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");

        var sparkline = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.RefundId}'] summary .history-sparkline").First;
        await Expect(sparkline).ToBeVisibleAsync();
        Assert.Contains("oldest first: " + RefundRuns + "P", await sparkline.GetAttributeAsync("title") ?? "");

        var r = await sparkline.EvaluateAsync<string[]>($$"""
            el => {
                {{Token}}
                return [getComputedStyle(el).backgroundImage, tok('--kron-track'), tok('--kron-pass-ring'), tok('--kron-fail-solid'),
                        tok('--kron-skip'), tok('--kron-hairline'), tok('--kron-hairline-soft'), tok('--kron-warn')];
            }
            """);
        var image = r[0];
        string track = r[1], pass = r[2], fail = r[3], skip = r[4], unknown = r[5], notATest = r[6], other = r[7];

        var stops = Regex.Matches(image, @"rgba?\([^)]*\)").Select(m => m.Value).ToArray();
        Assert.NotEmpty(stops);
        string[] tokens = [pass, fail, skip, track, unknown, notATest, other];
        Assert.All(stops, stop => Assert.True(tokens.Contains(stop),
            $"a run painted {stop}, none of the run tokens ({string.Join(", ", tokens)}): {image}"));
        // a band a run, a hard stop at each end, oldest first and this run's pass last
        var bands = stops.Where((stop, i) => i == 0 || stop != stops[i - 1]).ToArray();
        Assert.Equal([fail, skip, track, unknown, notATest, other, pass], bands);
    }

    private async Task OpenMissingAttachmentsReport(string fileName)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithMissingAttachments(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
    }

    /// <summary>
    /// A report whose one scenario captured nothing to draw, written with the line that says so
    /// (<see cref="ReportConfigurationOptions.ShowNoInteractionsMarker"/>, on in a run; the generator called directly
    /// leaves it off unless asked).
    /// </summary>
    private string GenerateReportWithoutInteractions(string fileName)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Storefront",
                Scenarios =
                [
                    new()
                    {
                        Id = "quiet1", DisplayName = "The basket shows its total", Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(30),
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the basket shows 12.50", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        ];

        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "No Interactions Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            showNoInteractionsMarker: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    // FlattenToggleTests' fixture: one outline of three examples, grouped, its flat table shown by default.
    private string GenerateFlatReport(string fileName)
    {
        var scenarios = new[]
        {
            new Scenario
            {
                Id = "flat1", DisplayName = "Bake(recipe)", IsHappyPath = true,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = "Bake",
                ExampleValues = new Dictionary<string, string> { ["Recipe"] = "{ Flour = Plain, Eggs = 2 }" },
                ExampleRawValues = new Dictionary<string, object?> { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Plain", ["Eggs"] = 2 } },
                ExampleFlatValues = new Dictionary<string, string> { ["RecipeName"] = "Classic", ["Flour"] = "Plain", ["Eggs"] = "2" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a classic recipe", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "baking", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is bread", Status = ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = "flat2", DisplayName = "Bake(recipe)", IsHappyPath = false,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                OutlineId = "Bake",
                ExampleValues = new Dictionary<string, string> { ["Recipe"] = "{ Flour = Whole, Eggs = 3 }" },
                ExampleRawValues = new Dictionary<string, object?> { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Whole", ["Eggs"] = 3 } },
                ExampleFlatValues = new Dictionary<string, string> { ["RecipeName"] = "Rustic", ["Flour"] = "Whole", ["Eggs"] = "3" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a rustic recipe", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "baking", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is bread", Status = ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = "flat3", DisplayName = "Bake(recipe)", IsHappyPath = false,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = "Bake",
                ExampleValues = new Dictionary<string, string> { ["Recipe"] = "{ Flour = Rye, Eggs = 1 }" },
                ExampleRawValues = new Dictionary<string, object?> { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Rye", ["Eggs"] = 1 } },
                ExampleFlatValues = new Dictionary<string, string> { ["RecipeName"] = "Nordic", ["Flour"] = "Rye", ["Eggs"] = "1" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a nordic recipe", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "baking", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is bread", Status = ExecutionResult.Passed }
                ]
            }
        };

        var features = new[]
        {
            new Feature { DisplayName = "Baking Recipes", Scenarios = scenarios }
        };

        var diagrams = scenarios.Select(s => new DiagramAsCode(s.Id, "",
            $"@startuml\nActor -> Oven : {s.ExampleFlatValues!["RecipeName"]}\n@enduml")).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Flatten Toggle Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>WCAG's contrast ratio of two opaque colours written as getComputedStyle writes them.</summary>
    private static double Contrast(string a, string b)
    {
        static double Luminance(string rgb)
        {
            var c = Regex.Matches(rgb, @"\d+(?:\.\d+)?").Take(3)
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture) / 255)
                .Select(v => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4))
                .ToArray();
            return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
        }
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
