namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that verify the "Databases Shown/Hidden" toggle removes database
/// participant declarations and all arrows to/from database aliases.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class DatabaseToggleTests : DiagramNotePlaywrightBase
{
    public DatabaseToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Databases_toggle_button_is_rendered_when_database_participant_exists()
    {
        await Page.GotoAsync(GenerateDatabaseParticipantReport("DbToggleBtn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var btn = Page.Locator("button[data-toggle='databases']").First;
        await btn.WaitForAsync(new() { Timeout = 5000 });
        var text = await btn.TextContentAsync();
        Assert.Equal("Databases Shown", text);
        Assert.True(await btn.EvaluateAsync<bool>("el => el.classList.contains('details-active')"));
    }

    [Fact]
    public async Task Databases_toggle_hides_database_participant_and_arrows()
    {
        await Page.GotoAsync(GenerateDatabaseParticipantReport("DbToggleHide.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Verify database is present initially
        var sourceBefore = await GetDataPlantuml();
        Assert.Contains("database", sourceBefore);
        Assert.Contains("cosmosdb", sourceBefore);

        // Click "Databases Shown" to hide
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();

        // Wait for re-render
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('cosmosdb');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // Verify database declaration and arrows are gone
        var sourceAfter = await GetDataPlantuml();
        Assert.DoesNotContain("database", sourceAfter);
        Assert.DoesNotContain("cosmosdb", sourceAfter);
        // Non-database arrows should remain
        Assert.Contains("caller", sourceAfter);
        Assert.Contains("svc", sourceAfter);
    }

    [Fact]
    public async Task Databases_toggle_shows_database_after_hiding()
    {
        await Page.GotoAsync(GenerateDatabaseParticipantReport("DbToggleShow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Hide databases
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('cosmosdb');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // Show databases again
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && source.includes('cosmosdb');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        var sourceAfterShow = await GetDataPlantuml();
        Assert.Contains("database", sourceAfterShow);
        Assert.Contains("cosmosdb", sourceAfterShow);
    }

    [Fact]
    public async Task Databases_toggle_button_text_changes_on_click()
    {
        await Page.GotoAsync(GenerateDatabaseParticipantReport("DbToggleText.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var btn = Page.Locator("button[data-toggle='databases']").First;
        await btn.WaitForAsync(new() { Timeout = 5000 });

        Assert.Equal("Databases Shown", await btn.TextContentAsync());
        await btn.ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var btn = document.querySelector("button[data-toggle='databases']");
                return btn && btn.textContent === 'Databases Hidden';
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        Assert.Equal("Databases Hidden", await btn.TextContentAsync());
        Assert.False(await btn.EvaluateAsync<bool>("el => el.classList.contains('details-active')"));
    }

    [Fact]
    public async Task Databases_toggle_does_not_clip_diagram_left_edge()
    {
        await Page.GotoAsync(GenerateDatabaseParticipantReport("DbToggleClip.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Hide databases
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();

        // Wait for re-render without databases
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('cosmosdb');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // The diagram container's scrollLeft should be 0 — no horizontal clipping
        var scrollLeft = await Page.EvaluateAsync<double>("""
            () => document.querySelector('[data-diagram-type="plantuml"]').scrollLeft
        """);
        Assert.Equal(0, scrollLeft);

        // The SVG's left edge should be at or past the container's left edge
        var svgIsVisible = await Page.EvaluateAsync<bool>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = container.querySelector('svg');
                if (!svg) return false;
                var cRect = container.getBoundingClientRect();
                var sRect = svg.getBoundingClientRect();
                return sRect.left >= cRect.left - 1;
            }
        """);
        Assert.True(svgIsVisible, "SVG left edge should not be clipped off-screen after hiding databases");
    }

    [Fact]
    public async Task Databases_toggle_resets_zoom_so_diagram_is_not_clipped()
    {
        await Page.GotoAsync(GenerateWideDatabaseParticipantReport("DbToggleZoomClip.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Capture before-toggle layout
        var beforeInfo = await Page.EvaluateAsync<string>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = container ? container.querySelector('svg') : null;
                if (!svg) return 'no svg';
                return JSON.stringify({
                    containerW: Math.round(container.getBoundingClientRect().width),
                    svgW: Math.round(svg.getBoundingClientRect().width),
                    svgNatW: svg.getAttribute('width'),
                    viewBox: svg.getAttribute('viewBox'),
                    scrollLeft: container.scrollLeft,
                    maxWidth: svg.style.maxWidth,
                    cssMaxWidth: getComputedStyle(svg).maxWidth
                });
            }
        """);

        // Now hide databases
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();

        // Wait for re-render without databases
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('spanner');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // Wait for the addZoomButton callback to fire
        await TwoAnimationFramesAsync(Page);

        // Capture after-toggle layout
        var afterInfo = await Page.EvaluateAsync<string>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = container ? container.querySelector('svg') : null;
                if (!svg) return 'no svg';
                var cRect = container.getBoundingClientRect();
                var sRect = svg.getBoundingClientRect();
                return JSON.stringify({
                    containerW: Math.round(cRect.width),
                    svgW: Math.round(sRect.width),
                    svgNatW: svg.getAttribute('width'),
                    viewBox: svg.getAttribute('viewBox'),
                    scrollLeft: container.scrollLeft,
                    maxWidth: svg.style.maxWidth,
                    cssMaxWidth: getComputedStyle(svg).maxWidth,
                    svgLeft: Math.round(sRect.left),
                    containerLeft: Math.round(cRect.left),
                    naturalSize: container.classList.contains('diagram-natural-size'),
                    overflowX: container.style.overflowX || getComputedStyle(container).overflowX
                });
            }
        """);

        // The SVG's left edge should be visible
        var svgIsVisible = await Page.EvaluateAsync<bool>("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = container.querySelector('svg');
                if (!svg) return false;
                var cRect = container.getBoundingClientRect();
                var sRect = svg.getBoundingClientRect();
                return sRect.left >= cRect.left - 1;
            }
        """);

        Assert.True(svgIsVisible, $"SVG left edge should not be clipped. Before: {beforeInfo}. After: {afterInfo}");
    }

    [Fact]
    public async Task Databases_toggle_removes_notes_following_database_arrows()
    {
        await Page.GotoAsync(GenerateWideDatabaseParticipantReport("DbToggleOrphanedNotes.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Verify the database note is in the original source
        var sourceBefore = await GetDataPlantuml();
        Assert.Contains("UPSERT CustomerPreferences", sourceBefore);
        Assert.Contains("spanner", sourceBefore);

        // Hide databases
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();

        // Wait for re-render without database
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('spanner');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // The note that followed the database arrow should also be removed
        var sourceAfter = await GetDataPlantuml();
        Assert.DoesNotContain("UPSERT CustomerPreferences", sourceAfter);
        // Non-database notes should remain
        Assert.Contains("customerId", sourceAfter);
    }

    [Fact]
    public async Task Databases_toggle_button_is_rendered_when_collections_participant_exists()
    {
        await Page.GotoAsync(GenerateCollectionsParticipantReport("DbToggleCollectionsBtn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var btn = Page.Locator("button[data-toggle='databases']").First;
        await btn.WaitForAsync(new() { Timeout = 5000 });
        var text = await btn.TextContentAsync();
        Assert.Contains("Shown", text);
    }

    [Fact]
    public async Task Databases_toggle_hides_collections_participant_and_arrows()
    {
        await Page.GotoAsync(GenerateCollectionsParticipantReport("DbToggleCollectionsHide.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var sourceBefore = await GetDataPlantuml();
        Assert.Contains("collections", sourceBefore);
        Assert.Contains("redis", sourceBefore);

        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('redis');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        var sourceAfter = await GetDataPlantuml();
        Assert.DoesNotContain("collections", sourceAfter);
        Assert.DoesNotContain("redis", sourceAfter);
        Assert.Contains("caller", sourceAfter);
        Assert.Contains("svc", sourceAfter);
    }

    [Fact]
    public async Task Databases_toggle_hides_both_database_and_collections_participants()
    {
        await Page.GotoAsync(GenerateMixedDatabaseCollectionsReport("DbToggleMixedHide.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var sourceBefore = await GetDataPlantuml();
        Assert.Contains("database", sourceBefore);
        Assert.Contains("cosmosdb", sourceBefore);
        Assert.Contains("collections", sourceBefore);
        Assert.Contains("redis", sourceBefore);

        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('redis') && !source.includes('cosmosdb');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        var sourceAfter = await GetDataPlantuml();
        Assert.DoesNotContain("database", sourceAfter);
        Assert.DoesNotContain("cosmosdb", sourceAfter);
        Assert.DoesNotContain("collections", sourceAfter);
        Assert.DoesNotContain("redis", sourceAfter);
        Assert.Contains("caller", sourceAfter);
        Assert.Contains("svc", sourceAfter);
    }

    private async Task<string> GetDataPlantuml()
    {
        return await Page.EvaluateAsync<string>("""
            () => document.querySelector('[data-diagram-type="plantuml"]').getAttribute('data-plantuml') || ''
        """);
    }
}
