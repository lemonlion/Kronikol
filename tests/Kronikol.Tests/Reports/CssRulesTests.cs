namespace Kronikol.Tests.Reports;

/// <summary>The stylesheet reader the report's CSS facts are built on.</summary>
public class CssRulesTests
{
    /// <summary>A selector list is split at its own commas: those inside a pseudo-class's argument, as in
    /// <c>:is(.plantuml-browser, .iflow-diagram)</c>, belong to the one selector, which a browser keeps whole.</summary>
    [Fact]
    public void A_selector_list_is_split_at_its_own_commas_only()
    {
        var rule = Assert.Single(CssRules.Parse(":is(.a, .b) svg rect, .c:not(.d, .e) { fill: red; }"));
        Assert.Equal([":is(.a, .b) svg rect", ".c:not(.d, .e)"], rule.Selectors);
    }
}
