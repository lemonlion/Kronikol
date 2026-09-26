using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The colours the internal-flow link binding paints (DIAGRAM_COLOURS_PLAN S2), on a hand-written SVG in a light
/// ink on a dark fill, so the rest colour cannot be black by accident. The binding used to black every
/// <c>#0000FF</c> text before it knew which were links, and to rest a link in <c>#000000</c> after a hover: under
/// a non-black ink a link went black for good, and blue text that was no link (a focus field painted by
/// <c>FocusEmphasis.Colored</c>, a hyperlink in a payload) lost its colour on load.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class IflowLinkColourTests : PlaywrightTestBase
{
    public IflowLinkColourTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string BodyInk = "#E6EBE9";

    private async Task OpenAndBind(InternalFlowHasDataBehavior behavior)
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowLinkColourPage(behavior)));
        await Page.WaitForFunctionAsync("() => typeof window._iflowBindLinks === 'function'", null,
            new() { Timeout = 10_000, PollingInterval = 200 });
        await Page.EvaluateAsync("src => window._iflowBindLinks(document.getElementById('diagram'), src)",
            TestPageGenerator.IflowLinkColourSource);
    }

    /// <summary>The fill and text-decoration of one text element, as <c>fill|decoration</c>.</summary>
    private Task<string> PaintOf(string id) => Page.EvaluateAsync<string>(
        "id => { const t = document.getElementById(id); return (t.getAttribute('fill') || '').toUpperCase() + '|' + (t.getAttribute('text-decoration') || ''); }", id);

    private Task Dispatch(string id, string type) => Page.EvaluateAsync(
        "([id, type]) => document.getElementById(id).dispatchEvent(new MouseEvent(type, { bubbles: true }))", new[] { id, type });

    [Fact]
    public async Task A_link_rests_in_the_ink_around_it_and_highlights_in_the_fill_the_engine_gave_it()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLinkOnHover);

        Assert.Equal(BodyInk + "|", await PaintOf("t-link-1"));
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-2"));

        await Dispatch("t-link-1", "mouseenter");
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-1"));
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-2"));

        await Dispatch("t-link-1", "mouseleave");
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-1"));
        Assert.Equal(BodyInk + "|", await PaintOf("t-link-2"));
    }

    [Fact]
    public async Task Blue_text_no_link_markup_names_keeps_its_colour()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLinkOnHover);

        Assert.Equal("#0000FF|", await PaintOf("t-focus"));
        Assert.Equal("#0000FF|underline", await PaintOf("t-href"));
    }

    [Fact]
    public async Task A_link_whose_segment_has_no_data_rests_in_the_ink_around_it_and_does_not_light_up()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLinkOnHover);

        Assert.Equal(BodyInk + "|", await PaintOf("t-nodata"));
        await Dispatch("t-nodata", "mouseenter");
        Assert.Equal(BodyInk + "|", await PaintOf("t-nodata"));
    }

    /// <summary>
    /// The real emitter and the real engine: a request whose label is an internal-flow link with data, and whose
    /// body has a focused field painted blue by <c>FocusEmphasis.Colored</c>.
    /// </summary>
    private async Task OpenRealDiagram(string path = "/api/orders")
    {
        var pairId = Guid.NewGuid();
        Kronikol.Tracking.RequestResponseLog Log(Kronikol.Tracking.RequestResponseType type, string? body) =>
            new("t1", "t1", "POST", body, new Uri("http://localhost" + path), [], "OrderService", "Caller", type,
                Guid.NewGuid(), pairId, TrackingIgnore: false,
                StatusCode: type == Kronikol.Tracking.RequestResponseType.Response ? System.Net.HttpStatusCode.OK : null)
            { FocusFields = ["name"] };
        var source = Kronikol.PlantUml.PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
                [Log(Kronikol.Tracking.RequestResponseType.Request, """{"name":"Alice","age":30}"""),
                 Log(Kronikol.Tracking.RequestResponseType.Response, """{"ok":true}""")],
                focusEmphasis: FocusEmphasis.Colored, internalFlowTracking: true)
            .Single().PlantUmls.First().PlainText;
        Assert.Contains($"[[#iflow-{pairId} ", source);

        var scripts = Kronikol.Reports.DiagramContextMenu.GetInternalFlowConfigScript(InternalFlowHasDataBehavior.ShowLinkOnHover)
            + $"<script>window.__iflowSegments = {{ 'iflow-{pairId}': {{ title: 'POST', content: '<p>flow</p>' }} }};</script>";
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage(scripts, ("d1", source))));
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync("() => { const el = document.getElementById('d1'); return el && el.dataset.rendered === '1' && el.querySelector('svg'); }",
            null, new() { Timeout = 60_000, PollingInterval = 200 });
    }

    /// <summary>Fill and decoration of every painted word containing <paramref name="word"/>, as <c>fill|decoration</c>.</summary>
    private Task<string[]> PaintsOf(string word) => Page.EvaluateAsync<string[]>(
        "w => Array.from(document.querySelectorAll('#d1 svg text')).filter(t => t.textContent.includes(w)).map(t => (t.getAttribute('fill') || '').toUpperCase() + '|' + (t.getAttribute('text-decoration') || ''))",
        word);

    [Fact]
    public async Task On_the_default_theme_a_real_link_rests_black_and_lights_up_blue()
    {
        await OpenRealDiagram();

        Assert.All(await PaintsOf("/api/orders"), p => Assert.Equal("#000000|", p));

        await Page.EvaluateAsync("() => Array.from(document.querySelectorAll('#d1 svg text')).find(t => t.textContent.includes('/api/orders')).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))");
        Assert.All(await PaintsOf("/api/orders"), p => Assert.Equal("#0000FF|underline", p));

        await Page.EvaluateAsync("() => Array.from(document.querySelectorAll('#d1 svg text')).find(t => t.textContent.includes('/api/orders')).dispatchEvent(new MouseEvent('mouseleave', { bubbles: true }))");
        Assert.All(await PaintsOf("/api/orders"), p => Assert.Equal("#000000|", p));
    }

    [Fact]
    public async Task A_focus_field_painted_blue_keeps_its_colour_in_a_diagram_with_a_link()
    {
        // Live on the default theme before 3.30.0 whenever FocusEmphasis.Colored met internal-flow tracking.
        await OpenRealDiagram();

        var alice = await PaintsOf("Alice");
        Assert.NotEmpty(alice);
        Assert.All(alice, p => Assert.StartsWith("#0000FF|", p));
    }

    [Fact]
    public async Task A_link_whose_path_holds_a_bracket_and_a_tilde_is_drawn_as_captured_and_still_binds()
    {
        // A JSON:API query (`page[size]`) ended the page's reading of the link markup at its first `]`, so the link was
        // drawn and never bound; the engine ate the `~` before `.`. The label writes both as code points (3.30.2).
        await OpenRealDiagram("/api/articles?page[size]=10&q=a~.b");

        var painted = await Page.EvaluateAsync<string>("() => Array.from(document.querySelectorAll('#d1 svg text')).map(t => t.textContent).join('')");
        Assert.Contains("/api/articles?page[size]=10&q=a~.b", painted);
        Assert.All(await PaintsOf("articles"), p => Assert.Equal("#000000|", p));

        await Page.EvaluateAsync("() => Array.from(document.querySelectorAll('#d1 svg text')).find(t => t.textContent.includes('articles')).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))");
        Assert.All(await PaintsOf("articles"), p => Assert.Equal("#0000FF|underline", p));
    }

    [Fact]
    public async Task A_component_diagrams_links_rest_in_their_labels_ink_light_up_blue_and_open_their_popups()
    {
        // The component diagram's relationship labels take the same binding (DIAGRAM_COLOURS_PLAN F12). The source is the
        // generator's own, with the plain palette the JS engines draw: its component names are white, and a link resting
        // in the ink of the text before it rested the first edge's link white on white (§12.5). The hand-written source
        // this fact used before had no palette, so it passed either way.
        ComponentRelationship Rel(string caller, string service, string protocol, string method) =>
            new(caller, service, protocol, [method], 3, 2, protocol);
        var rels = new[] { Rel("Caller", "Orders API", "HTTP", "GET"), Rel("Orders API", "Orders DB", "SQL", "SELECT"), Rel("Orders API", "Events", "ServiceBus", "Send") };
        string Key(ComponentRelationship r) => $"iflow-rel-{r.Caller.Replace(" ", "_")}-{r.Service.Replace(" ", "_")}";
        var stats = rels.ToDictionary(Key, _ => new RelationshipStats(3, 2, 10, 12, 30, 45, 1, 50, 0, [], [], null, null, false, 0.1, [], null, 5));
        var source = ComponentDiagramGenerator.GeneratePlantUml(rels, new ComponentDiagramOptions(), stats, useC4: false);
        var segments = string.Join(", ", rels.Select(r => $"'{Key(r)}': {{ title: '{r.Service}', content: '<p>flow</p>' }}"));
        var scripts = Kronikol.Reports.DiagramContextMenu.GetInternalFlowConfigScript(InternalFlowHasDataBehavior.ShowLinkOnHover)
            + "<script>window.__iflowSegments = { " + segments + " };</script>"
            + $"<style>{Kronikol.Reports.DiagramContextMenu.GetInternalFlowPopupStyles()}</style>"
            + Kronikol.Reports.DiagramContextMenu.GetInternalFlowPopupScript();
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage(scripts, ("d1", source))));
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync("() => { const el = document.getElementById('d1'); return el && el.dataset.rendered === '1' && el.querySelector('svg'); }",
            null, new() { Timeout = 60_000, PollingInterval = 200 });

        // Each link rests in the ink of its own label's stats lines, the arrow ink the generator's palette sets.
        foreach (var word in new[] { "GET", "SELECT", "Send", "P50" })
            Assert.All(await PaintsOf(word), p => Assert.Equal("#666666|", p));

        await Page.EvaluateAsync("() => Array.from(document.querySelectorAll('#d1 svg text')).find(t => t.textContent.includes('GET')).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))");
        Assert.All(await PaintsOf("GET"), p => Assert.Equal("#0000FF|underline", p));

        await Page.EvaluateAsync("() => Array.from(document.querySelectorAll('#d1 svg text')).find(t => t.textContent.includes('GET')).dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        await Page.Locator(".iflow-popup").First.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    [Fact]
    public async Task Under_show_link_a_link_is_painted_as_a_link_at_rest_and_the_rest_is_untouched()
    {
        await OpenAndBind(InternalFlowHasDataBehavior.ShowLink);

        Assert.Equal("#0000FF|underline", await PaintOf("t-link-1"));
        Assert.Equal("#0000FF|underline", await PaintOf("t-link-2"));
        Assert.Equal("#0000FF|", await PaintOf("t-focus"));
        Assert.Equal(BodyInk + "|", await PaintOf("t-nodata"));
    }
}
