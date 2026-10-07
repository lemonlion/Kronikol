using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A bypassed step's reason is drawn in the step list (#105, CUCUMBER_BYPASS_PLAN.md T20's unit half). It reached the
/// data and the query verbs, from <c>SkipIf</c> and the tests file, and the page drew it nowhere; the Cucumber lane drew
/// it only because it misfiled it as a comment.
/// </summary>
public class StepBypassReasonReportTests
{
    private static string Generate(string fileName, params ScenarioStep[] steps)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Bypass Feature",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "A bypass", Result = ExecutionResult.Bypassed, Steps = steps }],
            },
        ];
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, DateTime.UtcNow, DateTime.UtcNow, null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void A_bypassed_step_draws_its_reason_where_a_comment_is_drawn()
    {
        var content = Generate("StepBypassReason.html",
            new ScenarioStep { Keyword = "Then", Text = "mock Gemini served the summary", Status = ExecutionResult.Bypassed, BypassReason = "not in the deployed path" });

        Assert.Contains("<div class=\"step-comment\">Bypassed: not in the deployed path</div>", content);
    }

    [Fact]
    public void The_reason_is_drawn_as_text()
    {
        var content = Generate("StepBypassReasonEncoded.html",
            new ScenarioStep { Keyword = "Then", Text = "a step", Status = ExecutionResult.Bypassed, BypassReason = "<img src=x onerror=alert(1)> & co" });

        Assert.Contains("<div class=\"step-comment\">Bypassed: &lt;img src=x onerror=alert(1)&gt; &amp; co</div>", content);
        Assert.DoesNotContain("<img src=x", content);
    }

    [Fact]
    public void Only_a_bypassed_step_with_a_reason_draws_one()
    {
        var content = Generate("StepBypassReasonNone.html",
            new ScenarioStep { Keyword = "Given", Text = "a bypass with no reason", Status = ExecutionResult.Bypassed },
            new ScenarioStep { Keyword = "When", Text = "a passed step carrying a stray reason", Status = ExecutionResult.Passed, BypassReason = "stray" });

        Assert.DoesNotContain("<div class=\"step-comment\">Bypassed:", content);
    }
}
