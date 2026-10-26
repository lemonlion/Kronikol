using System.Text.Json;
using Jint;
using Kronikol.Reports;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// The pure part of the page's diagram API (plans/SEARCH_PLAN.md 4.6.5): how a message statement's label reads once the
/// engine has drawn it, <c>kronikol_diagrams.labelOf</c>, which <c>findMessage</c> compares each drawn message's text
/// with and counts a statement's place among the source's statements of the same label by. The engine draws a link's
/// label without its markup, a code point as its character, the display breaks of a long path as breaks, and each word
/// and space as a text of its own, so a label reads as its characters without its white space. The notes script runs
/// here as the page loads it, on a window with nothing drawn.
/// </summary>
public sealed class DiagramLabelTests : IDisposable
{
    private readonly Engine _js = new();

    public DiagramLabelTests()
    {
        var script = DiagramContextMenu.GetCollapsibleNotesScript().Replace("<script>", "").Replace("</script>", "");
        _js.Execute("var window = this; window.addEventListener = function() {}; var document = { addEventListener: function() {} };");
        _js.Execute(script);
    }

    private string? LabelOf(string statement)
    {
        var v = _js.Evaluate("window.kronikol_diagrams.labelOf(" + JsonSerializer.Serialize(statement) + ")");
        return v.IsNull() || v.IsUndefined() ? null : v.AsString();
    }

    [Theory]
    [InlineData("caller -[#438DD5]> ordersApi: [[#iflow-67a2a680 PUT: /customer-preferences/d37d5aba]]", "PUT:/customer-preferences/d37d5aba")]
    [InlineData("ordersApi -[#438DD5]-> caller: OK", "OK")]
    [InlineData("caller -> svc : POST /api/orders", "POST/api/orders")]
    [InlineData("a->b:hi there", "hithere")]
    [InlineData("a <-- b : done", "done")]
    [InlineData("a -> a : to itself", "toitself")]
    [InlineData("a -> b ++ : opens b", "opensb")]
    [InlineData("caller -[#438DD5]> api: [[#iflow-1 GET: /api/items?page=4&q=<U+005B>x<U+005D>]]", "GET:/api/items?page=4&q=[x]")]
    [InlineData("caller -[#438DD5]> api: [[#iflow-2 GET: /api/aaaa\\n        bbbb]]", "GET:/api/aaaabbbb")]
    [InlineData("caller -> api : GET: /a<U+002F>/b", "GET:/a//b")]
    [InlineData("user -[#000000]> page: first<U+200B><U+200B>\\nsecond", "firstsecond")]
    [InlineData("a -> b : 50<U+0025> off ~**not bold~**", "50%off**notbold**")]
    [InlineData("a -> b : <color:red>Failed</color> call", "Failedcall")]
    [InlineData("a -> b : [[http://example.com/x]]", "http://example.com/x")]
    [InlineData("\"A service\" -> \"B service\" : quoted", "quoted")]
    [InlineData("spanner -[#E74C3C]-> breakfastProvider: ", "")]
    [InlineData("a -> b", "")]
    public void A_message_statement_s_label_reads_as_the_engine_draws_it(string statement, string reads) =>
        Assert.Equal(reads, LabelOf(statement));

    /// <summary>The arrows and forms a source can hold besides the generator's own: a message sent in parallel (teoz's
    /// &amp;), the hidden spacer row the page adds to parts lined up (a label of space, drawn as nothing), a link with a
    /// tooltip, an escaped link, the other arrow heads and a message from the diagram's edge.</summary>
    [Theory]
    [InlineData("& a -> b : in parallel", "inparallel")]
    [InlineData("a -[hidden]> b : <space:40>", "")]
    [InlineData("caller -> api : [[#iflow-3{the tooltip} GET: /x]]", "GET:/x")]
    [InlineData("a -> b : ~[[not a link]]", "[[notalink]]")]
    [InlineData("a ->> b : async", "async")]
    [InlineData("a -\\ b : half", "half")]
    [InlineData("a <<-- b : back", "back")]
    [InlineData("[-> a : from the edge", "fromtheedge")]
    [InlineData("a -> \"Orders DB\" ++ : opens", "opens")]
    public void A_message_in_any_form_reads_as_its_label(string statement, string reads) =>
        Assert.Equal(reads, LabelOf(statement));

    [Theory]
    [InlineData("note left")]
    [InlineData("end note")]
    [InlineData("participant \"Caller\" as caller")]
    [InlineData("database \"Spanner\" as spanner")]
    [InlineData("hnote across <<stepDelimiter>> #black:<color:white>Given a request")]
    [InlineData("skinparam wrapWidth 800")]
    [InlineData("autonumber 1")]
    [InlineData("@startuml")]
    [InlineData("' caller -> api : a comment")]
    [InlineData("partition \"Setup\" #F7F7F7")]
    [InlineData("loop ×3 · GET: /api/items")]
    [InlineData("== Setup ==")]
    [InlineData("\"flour\": \"200 -> 300 : grams\",")]
    [InlineData("")]
    [InlineData("activate ordersApi")]
    [InlineData("||45||")]
    [InlineData("... 5 minutes later ...")]
    [InlineData("ref over a, b : the setup")]
    [InlineData("note over a, b : a -> b")]
    public void A_line_that_is_no_message_has_no_label(string line) => Assert.Null(LabelOf(line));

    public void Dispose() => _js.Dispose();
}
