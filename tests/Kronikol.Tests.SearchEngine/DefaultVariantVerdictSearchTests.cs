using System.Reflection;
using Jint;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// A default report ships <c>advanced-search.js</c> without its fenced <c>$verdict</c> lines
/// (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 3.5): <c>$flaky</c> typed there is a status nobody has, so it
/// matches nothing, and never throws or falls back to the text search. The other Jint tests load the raw resource,
/// whose fences are comments, so they test the verdict variant.
/// </summary>
public class DefaultVariantVerdictSearchTests
{
    private static readonly Engine Js = Load();

    private static Engine Load()
    {
        // ReportGenerator.LoadScript is internal; this project is not a friend assembly of Kronikol.
        var loadScript = typeof(Reports.ReportGenerator).GetMethod("LoadScript", BindingFlags.NonPublic | BindingFlags.Static)
                         ?? throw new InvalidOperationException("ReportGenerator.LoadScript not found");
        var script = (string)loadScript.Invoke(null, ["advanced-search.js", false])!;
        Assert.DoesNotContain("verdicts", script);
        var engine = new Engine();
        engine.Execute(script);
        return engine;
    }

    private static bool? Match(string input, string status)
    {
        Js.Execute("var __tags = new Set();");
        var result = Js.Invoke("advancedSearchMatch", input, "pay by card", Js.GetValue("__tags"), status);
        return result.IsNull() ? null : result.AsBoolean();
    }

    [Fact]
    public void A_verdict_query_matches_nothing_and_is_not_an_error()
    {
        Assert.False(Match("$flaky", "Passed"));
        Assert.False(Match("$broke", "Failed"));
        Assert.False(Match("$failed && $flaky", "Failed"));
    }

    [Fact]
    public void Status_queries_work_as_they_did_before_history()
    {
        Assert.True(Match("$failed", "Failed"));
        Assert.False(Match("$passed", "Failed"));
        Assert.True(Match("$failed || $flaky", "Failed"));
    }
}
