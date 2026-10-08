using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A browser that stops answering. Playwright's NewContextAsync and NewPageAsync have no timeout of their own: in a full
/// run on 2026-10-08 every browser of the run went silent at one moment (a browser launched beside them later worked),
/// and two tests in two collections waited on NewPageAsync, with every test queued behind them, until the run was
/// killed (a dump of the test process). Opening a context or a page is bounded, and the shared fixture replaces a
/// browser that opens no page in time, so one test fails and the rest of its collection runs.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BrowserStallTests
{
    [Fact]
    public async Task A_browser_that_opens_no_page_fails_its_test_and_the_next_test_gets_a_new_browser()
    {
        var fixture = new PlaywrightFixture();
        await fixture.InitializeAsync();
        var frozen = fixture.BrowserProcessId;
        try
        {
            var (first, _) = await fixture.OpenPageAsync(800, 600);
            await first.DisposeAsync();

            var stalled = fixture.Browser;
            Freeze(frozen);

            var started = Stopwatch.StartNew();
            var attempt = fixture.OpenPageAsync(800, 600, timeoutMs: 2000);
            Assert.True(await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(90))) == attempt,
                "Opening a page on a browser that stopped answering never returned.");
            var error = await Assert.ThrowsAsync<TimeoutException>(() => attempt);

            Assert.Equal("The browser opened no page within 2000 ms. The fixture killed it and launched another for " +
                         "the tests after this one.", error.Message);
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(60), $"The failure took {started.Elapsed}.");
            Assert.NotSame(stalled, fixture.Browser);
            Assert.NotEqual(frozen, fixture.BrowserProcessId);
            Assert.True(Exited(frozen), "The stalled browser was left running.");

            var (context, page) = await fixture.OpenPageAsync(800, 600);
            await page.SetContentAsync("<p>painted</p>");
            Assert.Equal("painted", await page.TextContentAsync("p"));
            await context.DisposeAsync();
        }
        finally
        {
            Kill(frozen);
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public void No_test_opens_a_context_or_a_page_without_the_bound()
    {
        var directory = Path.GetDirectoryName(SourcePath())!;
        string[] calls = [".NewPageAsync(", ".NewContextAsync("];
        var bare = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           Path.GetFileName(file) is not ("PlaywrightTestBase.cs" or "BrowserStallTests.cs"))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(l => calls.Any(call => l.line.Contains(call, StringComparison.Ordinal)))
            .Select(l => $"{Path.GetFileName(l.file)}:{l.index + 1}")
            .ToList();

        Assert.True(bare.Count == 0, "Use OpenPageAsync or OpenContextAsync: " + string.Join(", ", bare));
    }

    /// <summary>Stops every thread of the process, as a browser that has stopped answering.</summary>
    private static void Freeze(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.GetProcessById(pid);
            Assert.Equal(0, NtSuspendProcess(process.Handle));
        }
        else
        {
            using var stop = Process.Start("kill", ["-STOP", pid.ToString()])!;
            stop.WaitForExit();
            Assert.Equal(0, stop.ExitCode);
        }
    }

    private static bool Exited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(10000);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { /* already gone */ }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
