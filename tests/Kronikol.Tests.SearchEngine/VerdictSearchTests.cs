using System.Text.RegularExpressions;
using Jint;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// The cross-run history verdicts in the search DSL (plans/CROSS_RUN_HISTORY_PLAN.md §8.1): <c>$flaky</c>,
/// <c>$broke</c>, <c>$new</c>... resolve against a scenario's verdict set, carried as its own argument
/// because a scenario is routinely both <c>$failed</c> and <c>$flaky</c> and a single status string
/// cannot say so. An execution-result name always means the status, never a verdict.
/// </summary>
public class VerdictSearchTests : JintTestBase
{
    [Fact]
    public void A_status_query_still_matches_the_execution_result()
    {
        Assert.True(CallMatch("$failed", "pay by card", [], "Failed"));
        Assert.True(CallMatch("$failed", "pay by card", [], "Failed", ["flaky"]));
        Assert.False(CallMatch("$passed", "pay by card", [], "Failed", ["flaky"]));
    }

    [Fact]
    public void A_verdict_query_matches_the_scenarios_that_carry_it()
    {
        Assert.True(CallMatch("$flaky", "pay by card", [], "Passed", ["flaky", "unknown"]));
        Assert.True(CallMatch("$broke", "pay by card", [], "Failed", ["broke"]));
        Assert.False(CallMatch("$broke", "pay by card", [], "Failed", ["flaky"]));
        Assert.True(CallMatch("$always-failing", "pay by card", [], "Failed", ["always-failing"]));
    }

    [Fact]
    public void Without_a_verdict_set_a_verdict_query_is_false_and_never_an_error()
    {
        // A report generated with no ledger has no verdicts; `$flaky` must evaluate, not throw or fall
        // back to the legacy text search.
        Assert.False(CallMatch("$flaky", "pay by card", [], "Passed"));
        Assert.False(CallMatch("$flaky", "pay by card", [], "Passed", []));
    }

    [Fact]
    public void Verdicts_and_statuses_combine_with_the_operators()
    {
        Assert.True(CallMatch("$failed && $flaky", "pay by card", [], "Failed", ["flaky"]));
        Assert.False(CallMatch("$failed && !!$flaky", "pay by card", [], "Failed", ["flaky"]));
        Assert.True(CallMatch("$failed && !!$flaky", "pay by card", [], "Failed", ["broke"]));
        Assert.True(CallMatch("$flaky || @slow", "pay by card", ["slow"], "Passed"));
        Assert.True(CallMatch("card && $broke", "pay by card", [], "Failed", ["broke"]));
    }

    [Fact]
    public void An_execution_result_name_is_never_looked_up_in_the_verdict_set()
    {
        // The verdict vocabulary is closed and holds no status name, but the rule is pinned here so a
        // future verdict cannot shadow a status: the status decides, and the set is not consulted.
        Assert.False(CallMatch("$passed", "pay by card", [], "Failed", ["passed"]));
        Assert.False(CallMatch("$skipped", "pay by card", [], "Failed", ["skipped"]));
    }

    [Fact]
    public void The_evaluator_takes_the_verdict_set_on_the_parsed_tree_as_well()
    {
        var ast = CallParse("$flaky && $passed");
        Assert.NotNull(ast);
        Assert.True(CallEvaluate(ast!, "pay by card", [], "Passed", ["flaky"]));
        Assert.False(CallEvaluate(ast!, "pay by card", [], "Passed"));
    }
}

/// <summary>
/// The deep-search worker path evaluates the same query over the full corpus with the verdicts it was
/// posted, so <c>$flaky</c> reads the same on both paths of the search box.
/// </summary>
public class VerdictDeepSearchTests
{
    private readonly Engine _engine = new();

    public VerdictDeepSearchTests()
    {
        foreach (var resource in new[] { "advanced-search.js", "report-search-function.js", "report-search-index.js" })
            _engine.Execute(LoadEmbedded(resource));
    }

    private static string LoadEmbedded(string name)
    {
        var assembly = typeof(Kronikol.Reports.ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource {name} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_deep_matcher_reads_the_verdicts_it_is_handed()
    {
        _engine.Execute("var __with = kronDeepMatchesItem('$flaky', '$flaky', 'pay by card\\nsome body', new Set(), 'Passed', new Set(['flaky']));");
        _engine.Execute("var __without = kronDeepMatchesItem('$flaky', '$flaky', 'pay by card\\nsome body', new Set(), 'Passed', null);");
        _engine.Execute("var __both = kronDeepMatchesItem('body && $broke', 'body && $broke', 'pay by card\\nsome body', new Set(), 'Failed', new Set(['broke']));");

        Assert.True(_engine.GetValue("__with").AsBoolean());
        Assert.False(_engine.GetValue("__without").AsBoolean());
        Assert.True(_engine.GetValue("__both").AsBoolean());
    }

    [Fact]
    public void The_worker_source_carries_the_verdict_aware_matcher()
    {
        // The worker is built from the functions' own source text (Function.prototype.toString in the
        // browser; Jint does not keep it, so the shipped script is read), so a matcher whose verdicts
        // arrived through a variable outside its own body would silently run without them off the main
        // thread - the roster trap that once timed out every deep-search test.
        var index = LoadEmbedded("report-search-index.js");
        var matcher = Regex.Match(index, @"function kronDeepMatchesItem\([^)]*\)\s*\{.*?\n\}", RegexOptions.Singleline);
        Assert.True(matcher.Success);
        Assert.Contains("verdicts", matcher.Value);
        Assert.Contains("item.verdicts", index); // the worker's call site unpacks the posted field
        Assert.Contains("verdicts: c.items[i].verdicts", index); // and the page posts it
    }
}
