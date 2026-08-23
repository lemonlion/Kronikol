namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class LoadingMessageTests : PlaywrightTestBase
{
    public LoadingMessageTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Body_has_plantuml_ready_class_after_page_load()
    {
        await Page.GotoAsync(GenerateReport("BodyPlantumlReady.html"));
        await Page.WaitForFunctionAsync(
            "() => document.body.classList.contains('plantuml-ready')");
        var classes = await Page.Locator("body").GetAttributeAsync("class");
        Assert.Contains("plantuml-ready", classes!);
    }

    [Fact]
    public async Task Unrendered_diagram_shows_rendering_message_not_waiting()
    {
        await Page.GotoAsync(GenerateReport("LoadingMsgRendering.html"));
        await Page.WaitForFunctionAsync(
            "() => document.body.classList.contains('plantuml-ready')");
        await ExpandFirstScenarioWithDiagram();

        var message = await Page.EvaluateAsync<string?>("""
            () => {
                var diagrams = document.querySelectorAll('.plantuml-browser:not([data-rendered])');
                for (var i = 0; i < diagrams.length; i++) {
                    var before = window.getComputedStyle(diagrams[i], '::before').getPropertyValue('content');
                    if (before && before !== 'none' && before !== 'normal') return before;
                }
                return null;
            }
        """);

        Assert.NotNull(message);
        Assert.DoesNotContain("Waiting for page load", message);
        Assert.Contains("Rendering diagram", message);
    }
    [Fact]
    public async Task Unrendered_fragment_of_a_split_diagram_shows_the_rendering_message()
    {
        // A split diagram's container is marked rendered as soon as its fragment divs exist, so the
        // placeholder has to live on each fragment until that fragment's SVG arrives — otherwise the
        // reader sees a blank box for the seconds the engine needs for the first fragment.
        await Page.GotoAsync(GenerateReport("LoadingMsgFragment.html"));
        await Page.WaitForFunctionAsync(
            "() => document.body.classList.contains('plantuml-ready')");

        var message = await Page.EvaluateAsync<string?>("""
            () => {
                var container = document.querySelector('.plantuml-browser');
                if (!container) return 'NO_CONTAINER';
                container.dataset.rendered = '1';
                var frag = document.createElement('div');
                frag.className = 'puml-fragment';
                container.appendChild(frag);
                var before = window.getComputedStyle(frag, '::before').getPropertyValue('content');
                var height = frag.getBoundingClientRect().height;
                frag.dataset.rendered = '1';
                var after = window.getComputedStyle(frag, '::before').getPropertyValue('content');
                return JSON.stringify({ before: before, height: height, after: after });
            }
        """);

        Assert.NotNull(message);
        var parsed = System.Text.Json.JsonDocument.Parse(message!).RootElement;
        Assert.Contains("Rendering diagram", parsed.GetProperty("before").GetString());
        Assert.True(parsed.GetProperty("height").GetDouble() >= 48, "the unrendered fragment must reserve space for the placeholder");
        var after = parsed.GetProperty("after").GetString();
        Assert.True(after is null or "none" or "normal", $"placeholder must go once the fragment is rendered, got {after}");
    }
}
