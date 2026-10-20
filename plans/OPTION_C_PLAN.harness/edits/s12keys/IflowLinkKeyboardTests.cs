using Kronikol.InternalFlow;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The keyboard reaches an internal-flow link the browser engine draws (5.0.0, plans/OPTION_C_PLAN.md S12). That engine
/// writes no &lt;a&gt; for a link: it draws the label as text, which the render script binds to the popup, so Tab never
/// reached it and only a pointer opened the popup. A link with data is one Tab stop now, its first text, named from all
/// its texts; Enter or Space opens its popup; where links show only on hover, focus shows it; a link with no data, blue
/// text no link markup names, and a link the Java engine drew as an &lt;a&gt; (the anchor is its stop) take no stop. On the
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
}
