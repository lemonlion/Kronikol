using Microsoft.Playwright;

namespace Kronikol.Playwright;

/// <summary>
/// Applies a <see cref="TestTrackingIdentity"/> to Playwright objects so every request the browser makes
/// carries the Kronikol test-tracking headers. Apply at the <see cref="IBrowserContext"/> level (or when
/// creating it) so the headers survive navigation and reach every page of the context.
/// </summary>
/// <remarks>
/// Playwright's <c>SetExtraHTTPHeadersAsync</c> <em>replaces</em> the context's extra headers — pass any
/// headers you already set via <paramref name="additionalHeaders"/> so they are merged, or use
/// <see cref="NewTrackedContextAsync"/> which merges with <c>BrowserNewContextOptions.ExtraHTTPHeaders</c>.
/// The sink is downstream, not the fixture: a Kronikol-instrumented backend records the calls via
/// <c>TestTrackingContextMiddleware</c>; an uninstrumented one is observed by <c>Kronikol.Extensions.ProxyTap</c>.
/// </remarks>
public static class PlaywrightTestTrackingExtensions
{
    /// <summary>Stamps the identity's headers on the context (merged with <paramref name="additionalHeaders"/>).</summary>
    public static async Task<IBrowserContext> UseTestTrackingAsync(this IBrowserContext context, TestTrackingIdentity identity, IDictionary<string, string>? additionalHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(identity);
        await context.SetExtraHTTPHeadersAsync(Merge(identity, additionalHeaders)).ConfigureAwait(false);
        return context;
    }

    /// <summary>Stamps the identity's headers on a single page (merged with <paramref name="additionalHeaders"/>).</summary>
    public static async Task<IPage> UseTestTrackingAsync(this IPage page, TestTrackingIdentity identity, IDictionary<string, string>? additionalHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(identity);
        await page.SetExtraHTTPHeadersAsync(Merge(identity, additionalHeaders)).ConfigureAwait(false);
        return page;
    }

    /// <summary>
    /// Creates a new browser context whose <c>ExtraHTTPHeaders</c> carry the identity (merged with any
    /// headers already on <paramref name="options"/>). The usual per-test entry point: one context per test,
    /// one identity per context.
    /// </summary>
    public static Task<IBrowserContext> NewTrackedContextAsync(this IBrowser browser, TestTrackingIdentity identity, BrowserNewContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(identity);
        options = options is null ? new BrowserNewContextOptions() : new BrowserNewContextOptions(options);
        options.ExtraHTTPHeaders = Merge(identity, options.ExtraHTTPHeaders is null ? null : new Dictionary<string, string>(options.ExtraHTTPHeaders));
        return browser.NewContextAsync(options);
    }

    /// <summary>Returns the identity's headers merged over <paramref name="additionalHeaders"/> (identity wins on conflict).</summary>
    public static Dictionary<string, string> Merge(TestTrackingIdentity identity, IDictionary<string, string>? additionalHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (additionalHeaders is not null)
            foreach (var (key, value) in additionalHeaders)
                merged[key] = value;
        foreach (var (key, value) in identity.ToHeaders())
            merged[key] = value;
        return merged;
    }
}
