using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The light and dark themes (5.0.0, plans/OPTION_C_PLAN.md S1, D1): a report follows the system's setting until the
/// reader picks a theme with the switch on the title line, and then keeps the reader's pick, over the system's too.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ThemeTests : PlaywrightTestBase
{
    public ThemeTests(PlaywrightFixture fixture) : base(fixture) { }

    private ILocator Switch => Page.Locator("button.theme-toggle");

    private async Task OpenAsync(string fileName, ColorScheme scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = scheme });
        await Page.GotoReportAsync(GenerateReport(fileName));
        await Switch.WaitForAsync();
    }

    private Task<string?> ThemeAsync() =>
        Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-theme')");

    private Task WaitForThemeAsync(string theme) =>
        Page.WaitForFunctionAsync($"() => document.documentElement.getAttribute('data-theme') === '{theme}'", null,
            new() { PollingInterval = 200, Timeout = 10000 });

    // Counts the changes of the system's setting the page is told of. Two emulations in a row can land in one
    // evaluation of the page's media queries, where the setting is back where it was and no change event fires, so a
    // test waits for each change before the next. This listener comes before the page's, in the same dispatch.
    private const string CountSystemChanges =
        "window.__systemChanges = 0; matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => window.__systemChanges++);";

    private async Task SystemChangesToAsync(ColorScheme scheme, int changesSeen)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = scheme });
        await Page.WaitForFunctionAsync($"() => window.__systemChanges === {changesSeen}", null,
            new() { PollingInterval = 200, Timeout = 10000 });
    }

    // The colour the page paints behind everything, and the dark token it should be: a probe element resolves the
    // token to the same rgb() form the computed style uses.
    private Task<string[]> GroundAndDarkTokenAsync() =>
        Page.EvaluateAsync<string[]>(
            """
            () => {
                const probe = document.createElement('div');
                probe.style.background = 'var(--kron-ground)';
                document.body.appendChild(probe);
                const token = getComputedStyle(probe).backgroundColor;
                probe.remove();
                return [getComputedStyle(document.body).backgroundColor, token];
            }
            """);

    [Fact]
    public async Task A_dark_system_opens_the_report_dark_and_paints_the_dark_ground()
    {
        await OpenAsync("Theme_SystemDark.html", ColorScheme.Dark);

        Assert.Equal("dark", await ThemeAsync());
        Assert.Equal("true", await Switch.GetAttributeAsync("aria-pressed"));
        Assert.Equal("Switch to the light theme", await Switch.GetAttributeAsync("title"));
        var ground = await GroundAndDarkTokenAsync();
        Assert.Equal(ground[1], ground[0]);
        Assert.Equal("rgb(14, 19, 26)", ground[0]);
    }

    [Fact]
    public async Task Until_the_reader_picks_a_change_of_the_system_setting_is_followed()
    {
        await OpenAsync("Theme_FollowSystem.html", ColorScheme.Light);
        Assert.Equal("light", await ThemeAsync());

        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await WaitForThemeAsync("dark");
        Assert.Equal("true", await Switch.GetAttributeAsync("aria-pressed"));

        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await WaitForThemeAsync("light");
        Assert.Equal("false", await Switch.GetAttributeAsync("aria-pressed"));
    }

    [Fact]
    public async Task The_switch_flips_the_theme_and_the_pick_is_kept_over_a_reload_and_the_system()
    {
        await Context.AddInitScriptAsync(CountSystemChanges);
        await OpenAsync("Theme_Switch.html", ColorScheme.Light);
        var lightGround = (await GroundAndDarkTokenAsync())[0];

        await Switch.ClickAsync();
        Assert.Equal("dark", await ThemeAsync());
        Assert.Equal("true", await Switch.GetAttributeAsync("aria-pressed"));
        Assert.NotEqual(lightGround, (await GroundAndDarkTokenAsync())[0]);

        await Page.ReloadReportAsync();
        await Switch.WaitForAsync();
        Assert.Equal("dark", await ThemeAsync());

        // a stored pick is not overruled by the system's setting changing (the counter starts again on the reload)
        await SystemChangesToAsync(ColorScheme.Dark, 1);
        await SystemChangesToAsync(ColorScheme.Light, 2);
        Assert.Equal("dark", await ThemeAsync());

        await Switch.ClickAsync();
        Assert.Equal("light", await ThemeAsync());
        Assert.Equal("Switch to the dark theme", await Switch.GetAttributeAsync("title"));
    }

    [Fact]
    public async Task A_stored_pick_wins_over_the_system_from_the_start()
    {
        await Context.AddInitScriptAsync("try { localStorage.setItem('kronikol.theme', 'light'); } catch (e) { }");
        // recorded as the body starts, before any script in it has run
        await Context.AddInitScriptAsync(
            "document.addEventListener('readystatechange', () => { if (!window.__themeAtParse) window.__themeAtParse = document.documentElement.getAttribute('data-theme'); }, { once: true });");

        await OpenAsync("Theme_StoredWins.html", ColorScheme.Dark);

        Assert.Equal("light", await ThemeAsync());
        Assert.Equal("light", await Page.EvaluateAsync<string?>("() => window.__themeAtParse"));
        Assert.Equal("false", await Switch.GetAttributeAsync("aria-pressed"));
    }

    // A browser that refuses the page its storage: every access throws (all cookies blocked, a sandboxed frame), or
    // only a write does (a full quota, an older Safari's private window).
    [Theory]
    [InlineData("Object.defineProperty(window, 'localStorage', { configurable: true, get() { throw new DOMException('The operation is insecure.', 'SecurityError'); } });")]
    [InlineData("Storage.prototype.setItem = function () { throw new DOMException('The quota has been exceeded.', 'QuotaExceededError'); };")]
    public async Task Where_storage_is_refused_the_pick_holds_on_the_open_page_and_the_next_load_follows_the_system(string refuse)
    {
        await Context.AddInitScriptAsync(refuse);
        await Context.AddInitScriptAsync(CountSystemChanges);
        await OpenAsync("Theme_StorageRefused.html", ColorScheme.Light);
        Assert.Equal("light", await ThemeAsync());

        await Switch.ClickAsync();
        Assert.Equal("dark", await ThemeAsync());

        // the system's setting changing does not overrule the pick on the open page
        await SystemChangesToAsync(ColorScheme.Dark, 1);
        await SystemChangesToAsync(ColorScheme.Light, 2);
        Assert.Equal("dark", await ThemeAsync());
        Assert.Equal("true", await Switch.GetAttributeAsync("aria-pressed"));

        // nothing was kept, so the next load follows the system's setting again
        await Page.ReloadReportAsync();
        await Switch.WaitForAsync();
        Assert.Equal("light", await ThemeAsync());
    }
}
