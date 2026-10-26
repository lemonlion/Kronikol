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

    // L10 (SEARCH_PLAN 4.4): the worker reads tag:, status: and a - before them as the page does, from the query the page
    // hands it after its own pass (an unknown status name arrives in quotes, as the text the page read it as), over the
    // same scenarios; its corpus here is the page's own text, so the two answers can only differ by the reading
    [Theory]
    [InlineData("tag:smoke", "S1,S3,S7,S11")]
    [InlineData("tag:\"two words\"", "S9")]
    [InlineData("-tag:slow", "S1,S2,S4,S5,S7,S8,S9,S10")]
    [InlineData("status:passed && tag:smoke", "S1,S3")]
    [InlineData("status:flaky", "S3,S7")]
    [InlineData("-status:passed", "S2,S4,S6,S7,S8,S11")]
    [InlineData("status:failed", "S2,S7,S11")]
    [InlineData("status:skipped", "S4,S6")]
    [InlineData("pancake -status:failed", "S9,S10")]
    [InlineData("status:400", "S9")]
    [InlineData("api status:400", "S9")]
    [InlineData("(tag:ui || tag:api) && !!tag:slow", "S1,S2,S4,S5,S7,S8,S10")]
    public void The_worker_reads_the_new_forms_as_the_page_does(string query, string ids)
    {
        using var page = SearchFixture.Page();
        var outcome = page.Search(query);
        Assert.Equal(SearchFixture.Named(ids), outcome.Kept);
        Assert.NotNull(outcome.DeepInput);
        var worker = SearchFixture.Scenarios().Where(p => DeepMatches(outcome.DeepInput!, p.Feature, p.Scenario)).Select(p => p.Scenario.Name);
        Assert.Equal(outcome.Kept, worker);
    }

    // what the worker is posted for a scenario (collectItemMeta): its tags in lower case, its status and its verdicts
    private bool DeepMatches(string input, PageFeature feature, PageScenario scenario)
    {
        _engine.SetValue("__in", input);
        _engine.SetValue("__text", SearchPage.SearchText(feature, scenario));
        _engine.SetValue("__tags", scenario.Tags.Select(t => t.ToLowerInvariant()).ToArray());
        _engine.SetValue("__status", scenario.Status);
        _engine.SetValue("__verdicts", scenario.Verdicts ?? []);
        return _engine.Evaluate("kronDeepMatchesItem(__in, kronNormalizeQueryText(__in), kronNormalizeForSearch(__text), new Set(__tags), __status, __verdicts.length ? new Set(__verdicts) : null)").AsBoolean();
    }

    // The worker holds only the functions buildWorker lists, each its own source text: built so here, from the shipped
    // scripts, with nothing else loaded, it reads the new forms, so nothing they are read with lives outside the roster
    [Fact]
    public void The_worker_built_from_its_roster_reads_the_new_forms()
    {
        var sources = string.Join("\n", new[] { "advanced-search.js", "report-search-function.js", "report-search-index.js" }.Select(LoadEmbedded));
        var index = LoadEmbedded("report-search-index.js");
        var list = index[(index.IndexOf("var fns = [", StringComparison.Ordinal) + "var fns = [".Length)..];
        list = list[..list.IndexOf("];", StringComparison.Ordinal)];
        var roster = Regex.Matches(list, @"\b\w+\b").Select(m => m.Value).ToArray();
        Assert.Contains("isAdvancedSearch", roster);
        Assert.Contains("advancedSearchTokenise", roster);
        Assert.Contains("kronPositiveTerms", roster);
        var worker = string.Join("\n\n", roster.Select(name =>
        {
            var function = Regex.Match(sources, @"^function " + name + @"\(.*?^\}", RegexOptions.Singleline | RegexOptions.Multiline);
            Assert.True(function.Success, $"{name} is not a function of the shipped scripts");
            return function.Value;
        }));
        using var engine = new Engine();
        engine.Execute(worker);
        bool Reads(string query, string tag, string status) =>
            engine.Evaluate($"kronDeepMatchesItem({Js(query)}, kronNormalizeQueryText({Js(query)}), 'pay by card', new Set([{Js(tag)}]), {Js(status)}, null)").AsBoolean();
        Assert.True(Reads("tag:smoke", "smoke", "Passed"));
        Assert.False(Reads("-tag:smoke", "smoke", "Passed"));
        Assert.True(Reads("card status:passed", "smoke", "Passed"));
        Assert.False(Reads("card -status:passed", "smoke", "Passed"));
        // a scenario skipped after a failure is skipped, as the Status buttons count it since 5.9.1 (E3, B4)
        Assert.True(Reads("status:skipped", "smoke", "SkippedAfterFailure"));
        Assert.False(Reads("status:failed", "smoke", "SkippedAfterFailure"));
        Assert.True(Reads("\"status:400\" || card", "smoke", "Passed"));
        // and the words a match hit are the query's text, never its tags and statuses (SEARCH_PLAN 4.6.2)
        string Terms(string query) =>
            engine.Evaluate($"JSON.stringify(kronPositiveTerms({Js(query)}, kronNormalizeQueryText({Js(query)})))").AsString();
        Assert.Equal("[\"card\"]", Terms("card -status:passed tag:smoke"));
        Assert.Equal("[\"card\"]", Terms("card && !! visa"));
        Assert.Equal("[\"pay by\",\"card\"]", Terms("\"pay by\" card @smoke"));
    }

    private static string Js(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    // The deep search plans a new form as it plans the old one it stands for: a tag or status alone is no text to look
    // for, a - makes the deep answer authoritative as !! does, and neither prunes the candidates
    [Theory]
    [InlineData("tag:smoke", "@smoke")]
    [InlineData("status:failed", "$failed")]
    [InlineData("-tag:slow && order", "!!@slow && order")]
    [InlineData("payment -status:passed", "payment && !!$passed")]
    [InlineData("tag:\"two words\" || pancake", "@\"two words\" || pancake")]
    public void The_deep_search_plans_the_new_forms_as_it_plans_their_old_ones(string query, string old)
    {
        foreach (var function in new[] { "kronIsDeepEligible", "kronQueryCanRemove" })
            Assert.True(_engine.Invoke(function, old).AsBoolean() == _engine.Invoke(function, query).AsBoolean(), $"{function}({query})");
        Assert.True(_engine.Invoke("isAdvancedSearch", query).AsBoolean(), $"{query} is not read by the advanced search");
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
