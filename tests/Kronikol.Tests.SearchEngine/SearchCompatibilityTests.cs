namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// Every query the wiki's Search-Syntax page writes, run through the page's own search over <see cref="SearchFixture"/>,
/// keeps what it kept before SEARCH_PLAN 4.4 added <c>tag:</c>, <c>status:</c>, <c>feature:</c> and a <c>-</c> before a
/// qualifier (L8, a guard): the table was captured on 5.9.0's scripts (c10ed743) and pinned. One change is intended and
/// left out of the comparison: 5.9.1 made <c>$skipped</c> find a scenario skipped after a failure (S4), as the Status
/// buttons and the summary count it (plans/REPORT_UI_FIXES_PLAN.md E3, which B4 follows), so a query that names
/// <c>$skipped</c> is compared without S4; <c>SearchQueryFormsTests.Status_skipped_finds_a_scenario_skipped_after_a_failure</c>
/// holds that change. A report drawn
/// with history verdicts and a default one (whose scripts ship without their verdict lines) are both read; they differ
/// only where a query names a verdict.
/// </summary>
public class SearchCompatibilityTests
{
    private static readonly SearchPage WithVerdicts = SearchFixture.Page(verdicts: true);
    private static readonly SearchPage Default = SearchFixture.Page(verdicts: false);

    // query, kept with verdicts, kept in a default report when it differs ("=" when the same)
    private static readonly (string Query, string WithVerdicts, string InDefault)[] Table =
    [
        // Basic text search, quoted phrases
        ("order", "S1,S2,S3,S4,S11", "="),
        ("Pancakes Creation", "S9,S10,S11", "="),
        ("order create", "S1", "="),
        ("\"delete order\"", "S2", "="),
        ("\"create order\"", "S1", "="),
        // Advanced operators, precedence, implicit AND
        ("order && !!delete", "S1,S3,S4,S11", "="),
        ("payment || order", "S1,S2,S3,S4,S5,S6,S7,S8,S11", "="),
        ("(payment || order) && !!delete", "S1,S3,S4,S5,S6,S7,S8,S11", "="),
        ("payment || order && create", "S1,S4,S5,S6,S7,S8", "="),
        ("payment || (order && create)", "S1,S4,S5,S6,S7,S8", "="),
        ("(payment || order) && create", "S1", "="),
        ("(a || b) && c", "S1,S2,S3,S4,S5,S7,S8,S9,S10,S11", "="),
        ("@smoke $failed \"order\"", "S11", "="),
        ("@smoke && $failed && \"order\"", "S11", "="),
        // Tags, legacy tag expressions
        ("@smoke", "S1,S3,S7,S11", "="),
        ("@api && @smoke", "S1,S3,S7", "="),
        ("@\"Happy Path\"", "S1,S3,S5", "="),
        ("@\"two words\"", "S9", "="),
        ("@smoke and @api", "S1,S3,S7", "="),
        ("@smoke or @regression", "S1,S2,S3,S4,S7,S8,S11", "="),
        ("not @slow", "S1,S2,S4,S5,S7,S8,S9,S10", "="),
        ("(@ui or @api) and not @slow", "S1,S2,S4,S5,S7,S8,S10", "="),
        ("@smoke and not @slow", "S1,S7", "="),
        ("@issue(12)", "S9", "="),
        ("@a and @b or @c", "", "="),
        // Statuses
        ("$failed", "S2,S7,S11", "="),
        ("$passed", "S1,S3,S5,S9,S10", "="),
        ("$skipped", "S6", "="),
        ("$failed && order", "S2,S11", "="),
        ("$passed || $skipped", "S1,S3,S5,S6,S9,S10", "="),
        ("@api && $failed", "S2,S7", "="),
        ("$passed && @smoke", "S1,S3", "="),
        ("(order || payment) && $passed", "S1,S3,S5", "="),
        ("@api && $failed && !!timeout", "", "="),
        ("@smoke && \"expected result\" && !!@slow", "S7", "="),
        // History verdicts: in a default report a verdict is a status nobody has
        ("$flaky", "S3,S7", ""),
        ("$broke", "S2,S7", ""),
        ("$failed && !!$flaky", "S2,S11", "S2,S7,S11"),
        ("$passed && $flaky", "S3", ""),
        ("$stable", "S1,S5", ""),
        // Qualifiers
        ("dep:payment", "S4,S5,S6,S7", "="),
        ("dep:orderservice", "S1,S2,S3,S4,S11", "="),
        ("dep:\"order service\"", "S1,S2,S3,S4,S11", "="),
        ("dep:payment,order", "S1,S2,S3,S4,S5,S6,S7,S11", "="),
        ("dep:ödeme", "S6", "="),
        ("dep:支付", "S9", "="),
        ("duration:>2s", "S2,S11", "="),
        ("duration:<500ms", "S4,S6,S8", "="),
        ("duration:2s", "S1,S2,S11", "="),
        ("duration:>=2s", "S1,S2,S11", "="),
        ("duration:1s..5s", "S1,S2,S3,S7,S9,S11", "="),
        ("duration:>1.5s", "S1,S2,S11", "="),
        ("duration:>=p90", "S2", "="),
        ("!!duration:>2s", "S1,S3,S4,S5,S6,S7,S8,S9,S10", "="),
        ("is:happy", "S1,S3,S5", "="),
        ("is:happy-path", "S1,S3,S5", "="),
        ("is:happypath", "S1,S3,S5", "="),
        ("is:happy-paths", "S1,S3,S5", "="),
        ("!!is:happy", "S2,S4,S6,S7,S8,S9,S10,S11", "="),
        ("!!dep:payment", "S1,S2,S3,S8,S9,S10,S11", "="),
        ("not is:happy", "S2,S4,S6,S7,S8,S9,S10,S11", "="),
        ("not dep:payment", "S1,S2,S3,S8,S9,S10,S11", "="),
        ("order || dep:payment", "S4", "="),
        ("@smoke and dep:payment", "S7", "="),
        ("$failed dep:payment duration:>1s", "S7", "="),
        ("$failed duration:>2s", "S2,S11", "="),
        // Text that is no qualifier, a qualifier in quotes or in a negated group, and qualifiers that cannot be read
        ("status:400", "S9", "="),
        ("milk:", "S9", "="),
        ("\"order dep:payment\"", "", "="),
        ("!!(dep:payment)", "S1,S2,S3,S4,S5,S6,S7,S8,S9,S10,S11", "="),
        ("duration:fast", "", "="),
        ("duration:\"\"", "", "="),
        ("is:sad", "", "="),
        // A query the advanced grammar cannot parse falls back to the legacy search
        ("(order && create", "", "="),
    ];

    public static TheoryData<string, string, string> Documented()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var row in Table)
            data.Add(row.Query, row.WithVerdicts, row.InDefault);
        return data;
    }

    /// <summary>The queries of the table, for other facts to read.</summary>
    public static IEnumerable<string> Queries => Table.Select(row => row.Query);

    [Theory]
    [MemberData(nameof(Documented))]
    public void Every_documented_query_finds_what_it_found_before(string query, string withVerdicts, string inDefault)
    {
        Assert.Equal(Comparable(query, SearchFixture.Named(withVerdicts)), Comparable(query, WithVerdicts.Kept(query)));
        var expectedDefault = inDefault == "=" ? withVerdicts : inDefault;
        Assert.Equal(Comparable(query, SearchFixture.Named(expectedDefault)), Comparable(query, Default.Kept(query)));
    }

    // E3 (5.9.1), which B4 (Q11) follows, is the one change of meaning: a query that names $skipped is compared without the
    // scenario skipped after a failure
    private static string[] Comparable(string query, string[] kept) =>
        query.Contains("$skipped", StringComparison.Ordinal) ? kept.Where(k => k != SearchFixture.SkippedAfterFailure).ToArray() : kept;
}
