using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A report file's text as a fact reads or changes it (plans/HTML_COMPRESSION_PLAN.md 4.7). A compressed page is read as
/// its plain page, and an edit is made to the plain page and written back compressed, so the page the fact then opens is
/// still the one a reader gets; a plain page is read and edited as it is.
/// </summary>
public static class ReportFileText
{
    public static string Read(string path) => HtmlReportFile.ReadPlain(path);

    public static void Edit(string path, Func<string, string> edit) => EditInto(path, path, edit);

    public static void EditInto(string path, string destination, Func<string, string> edit)
    {
        var text = File.ReadAllText(path);
        File.WriteAllText(destination, CompressedPage.IsCompressed(text) ? CompressedPage.EditPlain(text, edit) : edit(text));
    }
}

/// <summary>
/// Opening a report and waiting for it (plans/HTML_COMPRESSION_PLAN.md 4.7). A compressed report is a loader page
/// whose own <c>load</c> fires before its loader has put the report in place (decoded and written with
/// <c>document.open</c>), so <see cref="IPage.GotoAsync"/> returns on the loader page. Every navigation in these suites
/// that can land on a report goes through these, which wait until the report is up: the loader sets
/// <c>window.__kronikolReady</c> at the rebuilt page's <c>DOMContentLoaded</c>, and a plain page has no loader to wait for.
/// </summary>
public static class ReportPageExtensions
{
    /// <summary>True on a plain page, and on a compressed one once the report is in place.</summary>
    public const string ReportIsUp =
        "() => window.__kronikolReady === true || !document.querySelector('meta[name=\"kronikol-page\"]')";

    public static async Task<IResponse?> GotoReportAsync(this IPage page, string url, PageGotoOptions? options = null)
    {
        var response = await page.GotoAsync(url, options);
        await page.WaitForReportAsync(options?.Timeout);
        return response;
    }

    public static async Task<IResponse?> ReloadReportAsync(this IPage page, PageReloadOptions? options = null)
    {
        var response = await page.ReloadAsync(options);
        await page.WaitForReportAsync(options?.Timeout);
        return response;
    }

    public static async Task<IResponse?> GoBackReportAsync(this IPage page, PageGoBackOptions? options = null)
    {
        var response = await page.GoBackAsync(options);
        await page.WaitForReportAsync(options?.Timeout);
        return response;
    }

    public static async Task<IResponse?> GoForwardReportAsync(this IPage page, PageGoForwardOptions? options = null)
    {
        var response = await page.GoForwardAsync(options);
        await page.WaitForReportAsync(options?.Timeout);
        return response;
    }

    public static async Task WaitForURLReportAsync(this IPage page, string url, PageWaitForURLOptions? options = null)
    {
        await page.WaitForURLAsync(url, options);
        await page.WaitForReportAsync(options?.Timeout);
    }

    public static async Task WaitForURLReportAsync(this IPage page, System.Text.RegularExpressions.Regex url, PageWaitForURLOptions? options = null)
    {
        await page.WaitForURLAsync(url, options);
        await page.WaitForReportAsync(options?.Timeout);
    }

    public static async Task WaitForURLReportAsync(this IPage page, Func<string, bool> url, PageWaitForURLOptions? options = null)
    {
        await page.WaitForURLAsync(url, options);
        await page.WaitForReportAsync(options?.Timeout);
    }

    /// <summary>Waits until the page is a plain page or a compressed report that is in place.</summary>
    public static Task WaitForReportAsync(this IPage page, float? timeout = null) =>
        page.WaitForFunctionAsync(ReportIsUp, null, new() { PollingInterval = 200, Timeout = timeout });

    /// <summary>
    /// An init script that runs <paramref name="body"/> at the report's <c>DOMContentLoaded</c>, before the report's own
    /// listeners, as a <c>document.addEventListener('DOMContentLoaded', …)</c> in an init script did on a plain page.
    /// <c>document.open</c> erases every listener added before it, and init scripts run once per document, so on a
    /// compressed page the listener is added after the loader's <c>document.open</c>; on the loader page itself the body
    /// does not run.
    /// </summary>
    public static Task AddReportInitScriptAsync(this IPage page, string body) => page.AddInitScriptAsync(ReportInitScript(body));

    /// <inheritdoc cref="AddReportInitScriptAsync(IPage, string)"/>
    public static Task AddReportInitScriptAsync(this IBrowserContext context, string body) => context.AddInitScriptAsync(ReportInitScript(body));

    public static string ReportInitScript(string body) => $$"""
        (function () {
            var hook = function () {
        {{body}}
            };
            var open = Document.prototype.open;
            Document.prototype.open = function () {
                var r = open.apply(this, arguments);
                this.addEventListener('DOMContentLoaded', hook);
                return r;
            };
            document.addEventListener('DOMContentLoaded', function () {
                if (!document.querySelector('meta[name="kronikol-page"]')) hook();
            });
        })();
        """;
}
