using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A closed composite step's sub-steps are drawn the first time it opens (5.0.0, <c>plans/REPORT_FORMAT_PLAN.md</c> 4.8
/// part b, Q8). Their HTML, when it is 2 KB or more, is held gzipped and base64-encoded in the page's <c>#step-data</c>
/// map, keyed from an empty <c>div.sub-steps</c> that says how many steps it holds. A failed path stays drawn and open,
/// a closed composite inside a held subtree goes in the same entry, and <c>RenderCollapsedSubStepsOnExpand = false</c>
/// draws every step as 4.x did. Kronikol's search reads <c>data-search</c>, which still holds every step's text.
/// </summary>
[Collection("DiagramsFetcher")]
public class SubStepsOnExpandTests
{
    private static readonly DateTime At = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static string Report(Scenario scenario, string name, bool onExpand = true, bool numbered = false) =>
        File.ReadAllText(ReportGenerator.GenerateHtmlReport([], [new Feature { DisplayName = "F", Scenarios = [scenario] }],
            At, At, null, name, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, showStepNumbers: numbered,
            renderCollapsedSubStepsOnExpand: onExpand));

    private static string WithoutScripts(string html) =>
        Regex.Replace(html, "<script[^>]*>.*?</script>", "", RegexOptions.Singleline);

    // A composite of `count` sub-steps, each long enough that 30 of them are past 2 KB; the last one has `last`.
    private static ScenarioStep Composite(string text, int count, ExecutionResult last = ExecutionResult.Passed) => new()
    {
        Keyword = "Given",
        Text = text,
        Status = last == ExecutionResult.Failed ? ExecutionResult.Failed : ExecutionResult.Passed,
        SubSteps = Enumerable.Range(1, count).Select(i => new ScenarioStep
        {
            Keyword = "And",
            Text = $"{text} sub-step {i} " + new string('x', 60),
            Status = i == count ? last : ExecutionResult.Passed,
        }).ToArray(),
    };

    private static Scenario Holding(params ScenarioStep[] steps) => new() { Id = "s", DisplayName = "S", Steps = steps };

    private static Dictionary<string, string> StepData(string html)
    {
        var match = Regex.Match(html, "<script id=\"step-data\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline);
        return match.Success ? JsonSerializer.Deserialize<Dictionary<string, string>>(match.Groups[1].Value)! : [];
    }

    private static string Decode(string base64)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static int DrawnLeafSteps(string html) => Regex.Matches(html, "<div class=\"step[ \"]").Count;

    [Fact]
    public void A_closed_composite_of_2_KB_or_more_is_written_empty_and_its_steps_go_in_the_map()
    {
        var html = Report(Holding(Composite("the basket", 30)), "OnExpandClosed.html");

        var holder = Regex.Match(html, "<div class=\"sub-steps\" data-steps=\"([^\"]+)\" data-count=\"30\"></div>");
        Assert.True(holder.Success, "no empty sub-steps holder carrying its key and its count");
        Assert.Equal(0, DrawnLeafSteps(html));
        var drawn = Decode(StepData(html)[holder.Groups[1].Value]);
        Assert.Equal(30, DrawnLeafSteps(drawn));
        Assert.Contains("the basket sub-step 30 ", drawn, StringComparison.Ordinal);
        // The search's own text still names every step.
        Assert.Matches("data-search=\"[^\"]*the basket sub-step 30 ", html);
    }

    [Fact]
    public void A_failed_path_stays_drawn_and_open()
    {
        var html = Report(Holding(Composite("the payment", 30, ExecutionResult.Failed)), "OnExpandFailed.html");

        Assert.Contains("<details class=\"step step-collapsible\" open>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
        Assert.Empty(StepData(html));
        Assert.Equal(30, DrawnLeafSteps(html));
    }

    [Fact]
    public void A_composite_under_2_KB_stays_drawn()
    {
        var html = Report(Holding(new ScenarioStep
        {
            Keyword = "Given", Text = "a small one", Status = ExecutionResult.Passed,
            SubSteps = [new ScenarioStep { Keyword = "And", Text = "one", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "two", Status = ExecutionResult.Passed }],
        }), "OnExpandSmall.html");

        Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
        Assert.Equal(2, DrawnLeafSteps(html));
    }

    [Fact]
    public void A_chain_of_closed_composites_is_one_entry_drawn_by_one_decode()
    {
        var leaf = new ScenarioStep { Keyword = "And", Text = "the leaf " + new string('y', 200), Status = ExecutionResult.Passed };
        for (var depth = 19; depth >= 0; depth--)
            leaf = new ScenarioStep { Keyword = "And", Text = $"level {depth} " + new string('z', 80), Status = ExecutionResult.Passed, SubSteps = [leaf] };

        var html = Report(Holding(leaf), "OnExpandChain.html");

        var holders = Regex.Matches(html, "<div class=\"sub-steps\" data-steps=\"([^\"]+)\" data-count=\"1\"></div>");
        Assert.Single(holders);
        var data = StepData(html);
        Assert.Single(data);
        var drawn = Decode(data[holders[0].Groups[1].Value]);
        Assert.Equal(19, Regex.Matches(drawn, "<details class=\"step step-collapsible\"").Count);
        Assert.Equal(1, DrawnLeafSteps(drawn));
        Assert.DoesNotContain("data-steps=", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void Drawing_every_entry_gives_the_page_the_option_turned_off_writes()
    {
        // Numbered, so a held step's number is checked too: two composites, a chain inside the second, a step after them.
        var chain = new ScenarioStep { Keyword = "And", Text = "the leaf " + new string('y', 200), Status = ExecutionResult.Passed };
        for (var depth = 5; depth >= 0; depth--)
            chain = new ScenarioStep { Keyword = "And", Text = $"level {depth} " + new string('z', 80), Status = ExecutionResult.Passed, SubSteps = [chain] };
        var second = Composite("the second basket", 30);
        second.SubSteps![4] = chain;
        // What else a step draws: a doc string, comments, a bypass and its reason, an assertion, a duration.
        second.SubSteps[1].DocString = "{\"basket\": 1}";
        second.SubSteps[1].DocStringMediaType = "application/json";
        second.SubSteps[2].Comments = ["# filled by hand"];
        second.SubSteps[3].Status = ExecutionResult.Bypassed;
        second.SubSteps[3].BypassReason = "not on a Sunday";
        second.SubSteps[5].IsAssertion = true;
        second.SubSteps[6].Duration = TimeSpan.FromMilliseconds(1250);
        var scenario = Holding(Composite("the first basket", 30), second,
            new ScenarioStep { Keyword = "Then", Text = "the baskets are full", Status = ExecutionResult.Passed });

        var held = Report(scenario, "OnExpandSameHeld.html", numbered: true);
        var eager = Report(scenario, "OnExpandSameEager.html", onExpand: false, numbered: true);

        var data = StepData(held);
        Assert.Equal(2, data.Count);
        var drawn = Regex.Replace(held, "<div class=\"sub-steps\" data-steps=\"([^\"]+)\" data-count=\"\\d+\"></div>",
            m => "<div class=\"sub-steps\">" + Decode(data[m.Groups[1].Value]) + "</div>");
        Assert.Equal(WithoutScripts(eager), WithoutScripts(drawn));
    }

    [Fact]
    public void Two_closed_composites_are_two_entries()
    {
        var html = Report(Holding(Composite("the first basket", 30), Composite("the second basket", 30)), "OnExpandTwo.html");

        Assert.Equal(2, Regex.Matches(html, "<div class=\"sub-steps\" data-steps=\"[^\"]+\" data-count=\"30\"></div>").Count);
        Assert.Equal(2, StepData(html).Count);
        Assert.Equal(0, DrawnLeafSteps(html));
    }

    [Fact]
    public void A_failed_composite_draws_its_closed_children_on_expand()
    {
        // The failed path is drawn open; a passed composite beside it, past 2 KB, is held.
        var failed = new ScenarioStep
        {
            Keyword = "When", Text = "the order is paid", Status = ExecutionResult.Failed,
            SubSteps = [Composite("the card", 30), new ScenarioStep { Keyword = "And", Text = "the charge", Status = ExecutionResult.Failed }],
        };

        var html = Report(Holding(failed), "OnExpandMixed.html");

        Assert.Contains("<details class=\"step step-collapsible\" open>", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "data-steps=\"[^\"]+\" data-count=\"30\""));
        Assert.Contains("the charge", Regex.Replace(html, "data-search=\"[^\"]*\"", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Turned_off_every_step_is_drawn_as_before()
    {
        var html = Report(Holding(Composite("the basket", 30)), "OnExpandOff.html", onExpand: false);

        Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"step-data\"", html, StringComparison.Ordinal);
        Assert.Equal(30, DrawnLeafSteps(html));
    }

    [Fact]
    public void The_option_is_on_by_default() => Assert.True(new ReportConfigurationOptions().RenderCollapsedSubStepsOnExpand);

    [Fact]
    public void A_run_hands_both_its_pages_the_option()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-held-" + Guid.NewGuid().ToString("N"));
        try
        {
            ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "F", Scenarios = [Holding(Composite("the basket", 30))] }],
                DateTime.UtcNow, DateTime.UtcNow, new ReportConfigurationOptions
                {
                    ReportsFolderPath = directory,
                    RenderCollapsedSubStepsOnExpand = false,
                    InternalFlowTracking = false,
                    GenerateComponentDiagram = false,
                    GenerateSpecificationsReport = true,
                    GenerateSpecificationsData = false,
                });

            // Both of the run's pages: the test run report and the specifications.
            foreach (var page in new[] { "TestRunReport.html", "Specifications.html" })
            {
                var html = File.ReadAllText(Path.Combine(directory, page));
                Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
                Assert.Equal(30, DrawnLeafSteps(html));
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
