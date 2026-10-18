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
        await Page.GotoAsync(GenerateReport(fileName));
        await Switch.WaitForAsync();
    }

    private Task<string?> ThemeAsync() =>
        Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-theme')");

    private Task WaitForThemeAsync(string theme) =>
        Page.WaitForFunctionAsync($"() => document.documentElement.getAttribute('data-theme') === '{theme}'", null,
            new() { PollingInterval = 200, Timeout = 10000 });

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
        await OpenAsync("Theme_Switch.html", ColorScheme.Light);
        var lightGround = (await GroundAndDarkTokenAsync())[0];

        await Switch.ClickAsync();
        Assert.Equal("dark", await ThemeAsync());
        Assert.Equal("true", await Switch.GetAttributeAsync("aria-pressed"));
        Assert.NotEqual(lightGround, (await GroundAndDarkTokenAsync())[0]);

        await Page.ReloadAsync();
        await Switch.WaitForAsync();
        Assert.Equal("dark", await ThemeAsync());

        // a stored pick is not overruled by the system's setting changing
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
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
}
