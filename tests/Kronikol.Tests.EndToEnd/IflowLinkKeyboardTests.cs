using Kronikol.InternalFlow;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The keyboard reaches an internal-flow link however the engine draws it (5.0.0, plans/OPTION_C_PLAN.md S12). The 4.x
/// browser engine writes no &lt;a&gt; for a link: it draws the label as text, which the render script binds to the popup,
/// so Tab never reached it and only a pointer opened the popup. Such a link with data is one Tab stop now, its first text,
/// named from all its texts; Enter or Space opens its popup; where links show only on hover, focus shows it; a link with
/// no data, blue text no link markup names, and a link drawn as an &lt;a&gt; (the anchor is its stop) take no stop. 5.0.0's
/// engine draws an &lt;a&gt; round each line of a label, titled with the segment's id, which a browser shows as a tooltip
/// and reads as the link's name: the first anchor is the one stop, named from all the texts, and no title is left. On the
/// hand-written diagram of <see cref="TestPageGenerator.GenerateIflowLinkColourPage"/>: "Get order" is one link of two
/// texts with data, "Missing" one without.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class IflowLinkKeyboardTests : PlaywrightTestBase
{
    public IflowLinkKeyboardTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string BodyInk = "#E6EBE9";

    // the page, its popup opener recorded (what it was asked to open, in order), then the diagram bound
    private async Task OpenAndBind(InternalFlowHasDataBehavior behavior, string? beforeBinding = null)
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowLinkColourPage(behavior)));
        await Page.WaitForFunctionAsync("() => typeof window._iflowBindLinks === 'function'", null,
            new() { Timeout = 10_000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => { window.__opened = []; window._iflowShowPopup = id => window.__opened.push(id); }");
        if (beforeBinding is not null) await Page.EvaluateAsync(beforeBinding);
        await Page.EvaluateAsync("src => window._iflowBindLinks(document.getElementById('diagram'), src)",
            TestPageGenerator.IflowLinkColourSource);
    }

    // tabindex, role and name of one text, as tabindex|role|aria-label
    private Task<string> KeysOf(string id) => Page.EvaluateAsync<string>("""
        id => { const t = document.getElementById(id); return ['tabindex', 'role', 'aria-label'].map(a => t.getAttribute(a) || '').join('|'); }
        """, id);

    private Task<string> PaintOf(string id) => Page.EvaluateAsync<string>(
        "id => { const t = document.getElementById(id); return (t.getAttribute('fill') || '').toUpperCase() + '|' + (t.getAttribute('text-decoration') || ''); }", id);

    private Task<string[]> Opened() => Page.EvaluateAsync<string[]>("() => window.__opened");

    private Task<string> Focused() => Page.EvaluateAsync<string>("() => document.activeElement ? (document.activeElement.id || document.activeElement.tagName) : ''");

    [Theory]
    [InlineData(InternalFlowHasDataBehavior.ShowLink)]
    [InlineData(InternalFlowHasDataBehavior.ShowLinkOnHover)]
    public async Task A_link_with_data_is_one_stop_for_the_keyboard_named_from_all_its_texts(InternalFlowHasDataBehavior behavior)
    {
        await OpenAndBind(behavior);

        Assert.Equal("0|link|Get order", await KeysOf("t-link-1"));
        Assert.Equal("||", await KeysOf("t-link-2"));
        Assert.Equal("||", await KeysOf("t-nodata"));
        Assert.Equal("||", await KeysOf("t-focus"));
        Assert.Equal("||", await KeysOf("t-href"));
    }

    [Fact]
    public async Task Tab_lands_on_the_link_once_and_then_leaves_the_diagram()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLink);

        await Page.Keyboard.PressAsync("Tab");
        Assert.Equal("t-link-1", await Focused());
        await Page.Keyboard.PressAsync("Tab");
        Assert.NotEqual("t-link-2", await Focused());
        Assert.NotEqual("t-nodata", await Focused());
    }

    [Fact]
    public async Task Enter_or_Space_on_the_link_opens_its_popup()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLink);
        await Page.Locator("#t-link-1").FocusAsync();

        await Page.Keyboard.PressAsync("Enter");
        Assert.Equal(new[] { "iflow-seg-1" }, await Opened());
        await Page.Keyboard.PressAsync("Space");
        Assert.Equal(new[] { "iflow-seg-1", "iflow-seg-1" }, await Opened());
        // any other key opens nothing
        await Page.Keyboard.PressAsync("a");
        Assert.Equal(2, (await Opened()).Length);
    }

    [Fact]
    public async Task Where_links_show_only_on_hover_focus_shows_the_link_and_leaving_puts_it_at_rest()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLinkOnHover);
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-1"));

        await Page.Locator("#t-link-1").FocusAsync();
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-1"));
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-2"));

        await Page.EvaluateAsync("() => document.getElementById('t-link-1').blur()");
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-1"));
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-2"));
    }

    [Fact]
    public async Task A_link_the_Java_engine_drew_as_an_anchor_keeps_the_anchor_as_its_stop()
    {
        // the two texts of "Get order" in an <a>, as the Java engine (Server or Local rendering) writes a link
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLink, """
            () => {
                const ns = 'http://www.w3.org/2000/svg', xlink = 'http://www.w3.org/1999/xlink';
                const first = document.getElementById('t-link-1'), a = document.createElementNS(ns, 'a');
                a.setAttribute('href', '#iflow-seg-1');
                a.setAttributeNS(xlink, 'xlink:href', '#iflow-seg-1');
                first.parentNode.insertBefore(a, first);
                a.appendChild(first);
                a.appendChild(document.getElementById('t-link-2'));
            }
            """);

        Assert.Equal("||", await KeysOf("t-link-1"));
        Assert.Equal("||", await KeysOf("t-link-2"));
    }

    // "Get order" as 5.0.0's browser engine draws a link of two lines: an <a> round each line's text, each naming the
    // segment in its href and in a title, which a browser shows as the tooltip and reads as the link's name. The first
    // holds xlink:title as a page parses it, in the xlink namespace; the second as a plain attribute of that name.
    private const string EngineAnchors = """
        () => {
            const ns = 'http://www.w3.org/2000/svg', xlink = 'http://www.w3.org/1999/xlink';
            ['t-link-1', 't-link-2'].forEach((id, i) => {
                const t = document.getElementById(id), a = document.createElementNS(ns, 'a');
                a.id = 'a-link-' + (i + 1);
                a.setAttribute('target', '_top');
                a.setAttribute('href', '#iflow-seg-1');
                a.setAttributeNS(xlink, 'xlink:href', '#iflow-seg-1');
                a.setAttribute('title', '#iflow-seg-1');
                if (i === 0) a.setAttributeNS(xlink, 'xlink:title', '#iflow-seg-1');
                else a.setAttribute('xlink:title', '#iflow-seg-1');
                t.parentNode.insertBefore(a, t);
                a.appendChild(t);
            });
        }
        """;

    // tabindex, aria-hidden, aria-label and both titles of one anchor, as tabindex|aria-hidden|aria-label|title|xlink:title,
    // the last in any of its forms
    private Task<string> AnchorOf(string id) => Page.EvaluateAsync<string>("""
        id => {
            const a = document.getElementById(id);
            return [a.getAttribute('tabindex'), a.getAttribute('aria-hidden'), a.getAttribute('aria-label'), a.getAttribute('title'),
                a.getAttributeNS('http://www.w3.org/1999/xlink', 'title') || a.getAttribute('xlink:title')].map(v => v || '').join('|');
        }
        """, id);

    [Fact]
    public async Task A_link_the_engine_drew_as_one_anchor_a_line_is_one_stop_named_from_all_its_texts()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLink, EngineAnchors);

        Assert.Equal("||Get order||", await AnchorOf("a-link-1"));
        Assert.Equal("-1|true|||", await AnchorOf("a-link-2"));
        Assert.Equal("||", await KeysOf("t-link-1"));

        await Page.Keyboard.PressAsync("Tab");
        Assert.Equal("a-link-1", await Focused());
        await Page.Keyboard.PressAsync("Tab");
        Assert.NotEqual("a-link-2", await Focused());
    }

    [Fact]
    public async Task An_anchor_bound_twice_and_a_dead_one_are_left_without_a_title()
    {
        // "Missing", a link with no data, drawn by the engine as an <a> too, its xlink:title written under another prefix
        const string deadAnchor = """
            () => {
                const ns = 'http://www.w3.org/2000/svg', xlink = 'http://www.w3.org/1999/xlink';
                const t = document.getElementById('t-nodata'), a = document.createElementNS(ns, 'a');
                a.id = 'a-nodata';
                a.setAttribute('href', '#iflow-seg-nodata');
                a.setAttributeNS(xlink, 'xlink:href', '#iflow-seg-nodata');
                a.setAttribute('title', '#iflow-seg-nodata');
                a.setAttributeNS(xlink, 'x:title', '#iflow-seg-nodata');
                t.parentNode.insertBefore(a, t);
                a.appendChild(t);
            }
            """;
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLink, "() => { (" + EngineAnchors + ")(); (" + deadAnchor + ")(); }");
        await Page.EvaluateAsync("src => window._iflowBindLinks(document.getElementById('diagram'), src)",
            TestPageGenerator.IflowLinkColourSource);

        Assert.Equal("||Get order||", await AnchorOf("a-link-1"));
        Assert.Equal("-1|true|||", await AnchorOf("a-link-2"));
        Assert.Equal("||||", await AnchorOf("a-nodata"));
        Assert.False(await Page.EvaluateAsync<bool>("() => document.getElementById('a-nodata').hasAttribute('href')"));
    }

    [Fact]
    public async Task Where_links_show_only_on_hover_focus_on_the_engines_anchor_shows_the_link()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLinkOnHover, EngineAnchors);
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-1"));

        await Page.Locator("#a-link-1").FocusAsync();
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-1"));
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-2"));

        await Page.EvaluateAsync("() => document.getElementById('a-link-1').blur()");
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-1"));
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-2"));
    }
}
