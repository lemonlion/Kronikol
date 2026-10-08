using System.Diagnostics;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Shared Playwright browser instance for all test classes via xUnit assembly fixture.
/// Each test class gets its own <see cref="IBrowserContext"/> via <see cref="OpenPageAsync"/>,
/// so tests are fully isolated and can run in parallel.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public IBrowser Browser => _browser;

    /// <summary>The operating system's id for the browser's main process.</summary>
    public int BrowserProcessId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        await LaunchAsync();
    }

    private async Task LaunchAsync()
    {
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        BrowserProcessId = await PlaywrightTestBase.WithinAsync(BrowserProcessIdAsync(_browser),
            PlaywrightTestBase.PageOpenTimeout, "The browser named no process of its own.");
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    /// <summary>
    /// A new context and page on the browser. When the browser opens neither within <paramref name="timeoutMs"/>, the
    /// fixture kills it, launches another and fails the test: the tests after it get the new browser, where every test
    /// of the collection waited on the silent one (<see cref="BrowserStallTests"/>).
    /// </summary>
    public async Task<(IBrowserContext Context, IPage Page)> OpenPageAsync(
        int width = 1920, int height = 1080, int timeoutMs = PlaywrightTestBase.PageOpenTimeout)
    {
        try
        {
            var context = await PlaywrightTestBase.OpenContextAsync(_browser, new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height }
            }, timeoutMs);
            return (context, await PlaywrightTestBase.OpenPageAsync(context, timeoutMs));
        }
        catch (TimeoutException)
        {
            KillBrowser();
            await LaunchAsync();
            throw new TimeoutException($"The browser opened no page within {timeoutMs} ms. The fixture killed it and " +
                                       "launched another for the tests after this one.");
        }
    }

    private void KillBrowser()
    {
        try
        {
            using var process = Process.GetProcessById(BrowserProcessId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10000);
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { /* already gone */ }
    }

    private static async Task<int> BrowserProcessIdAsync(IBrowser browser)
    {
        var session = await browser.NewBrowserCDPSessionAsync();
        try
        {
            var info = await session.SendAsync("SystemInfo.getProcessInfo");
            foreach (var process in info!.Value.GetProperty("processInfo").EnumerateArray())
            {
                if (process.GetProperty("type").GetString() == "browser")
                    return process.GetProperty("id").GetInt32();
            }
            throw new InvalidOperationException("The browser listed no process of type browser.");
        }
        finally
        {
            await session.DetachAsync();
        }
    }
}
