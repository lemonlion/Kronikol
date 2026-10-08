namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The wait for two animation frames that tests use before they measure a layout. Playwright's evaluate has no timeout
/// of its own, and a busy machine can stall a headless renderer's frames: awaited bare, the two
/// <c>requestAnimationFrame</c> callbacks held a viewport sweep, and every test queued behind it in its collection, for
/// as long as the run was left (measured twice on 2026-10-08, with the test process idle and the browser alive).
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class AnimationFrameWaitTests : PlaywrightTestBase
{
    public AnimationFrameWaitTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_page_that_paints_returns_after_two_frames()
    {
        await Page.SetContentAsync("<html><body><p>painted</p></body></html>");

        await TwoAnimationFramesAsync(Page, timeoutMs: 15000);
    }

    [Fact]
    public async Task A_page_that_paints_no_frame_fails_the_wait_instead_of_holding_the_test()
    {
        await Page.SetContentAsync("<html><body><p>stalled</p></body></html>");
        // A renderer that produces no frame: no requestAnimationFrame callback ever runs.
        await Page.EvaluateAsync("() => { window.requestAnimationFrame = () => 0; }");

        var started = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<TimeoutException>(() => TwoAnimationFramesAsync(Page, timeoutMs: 1000));

        Assert.Equal("The page painted no animation frame within 1000 ms.", error.Message);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void No_test_awaits_animation_frames_without_the_bound()
    {
        // The four bare waits went through the helper; a new one would bring the hang back.
        var directory = Path.GetDirectoryName(SourcePath())!;
        var bare = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           Path.GetFileName(file) is not ("PlaywrightTestBase.cs" or "AnimationFrameWaitTests.cs"))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(l => l.line.Contains("requestAnimationFrame(() => requestAnimationFrame(r))", StringComparison.Ordinal))
            .Select(l => $"{Path.GetFileName(l.file)}:{l.index + 1}")
            .ToList();

        Assert.True(bare.Count == 0, "Use TwoAnimationFramesAsync: " + string.Join(", ", bare));
        Assert.Contains("requestAnimationFrame(() => requestAnimationFrame(r))",
            File.ReadAllText(Path.Combine(directory, "PlaywrightTestBase.cs")), StringComparison.Ordinal);
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
