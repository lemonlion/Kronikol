using System.Net;

namespace Kronikol.Reports;

/// <summary>
/// Where the last run report of this process put each scenario (#228 F6): the report's HTML file and each test's anchor
/// in it, for a page written after the report that points at it. BDDfy's page is one: its batch processor runs when the
/// process ends, and under the default renderer the report draws every diagram itself, so that page links to it.
/// </summary>
internal static class ReportLinks
{
    private static Snapshot? _last;

    /// <summary>The report's HTML file, as a full path, and each test's anchor in it (<c>sid-…</c>), by test id.</summary>
    internal sealed record Snapshot(string HtmlPath, IReadOnlyDictionary<string, string> AnchorsByTestId);

    /// <summary>The last report written, or null when this process wrote none.</summary>
    internal static Snapshot? Last => Volatile.Read(ref _last);

    internal static void Record(string htmlPath, IReadOnlyDictionary<string, string> anchorsByTestId) =>
        Volatile.Write(ref _last, new Snapshot(Path.GetFullPath(htmlPath), anchorsByTestId));

    /// <summary>Forgets the last report. Only Kronikol's own tests call it.</summary>
    internal static void Clear() => Volatile.Write(ref _last, null);

    /// <summary>
    /// One diagram's entry on a page outside the report, BDDfy's, by what the renderer gave it, with the diagram's source
    /// below it (#241). A picture's address (the PlantUML server's, or a data URI) is drawn as before. Markup, an inline
    /// SVG, is drawn as it is, where an <c>img</c> could load none. A file the local renderer wrote is addressed from the
    /// page's folder, as its address starts at the report's. No address at all is the default renderer's, which draws in
    /// the report's own page: the entry links to the scenario there.
    /// </summary>
    internal static string DiagramEntryHtml(DefaultDiagramsFetcher.DiagramAsCode diagram, bool lazyLoad, string pageDirectory,
        Snapshot? report)
    {
        var src = diagram.ImgSrc;
        string summary;
        if (src.Length == 0)
        {
            var anchor = report?.AnchorsByTestId.GetValueOrDefault(diagram.TestRuntimeId);
            summary = report is null || anchor is null
                ? "The sequence diagram is drawn in the Kronikol report."
                : $"<a href=\"{WebUtility.HtmlEncode(Href(pageDirectory, report.HtmlPath))}#{anchor}\">View this sequence diagram in the Kronikol report</a>";
        }
        else if (src.StartsWith('<'))
            summary = src;
        else
        {
            var url = IsAddress(src) || report is null
                ? src
                : WebUtility.HtmlEncode(Href(pageDirectory, Path.Combine(Path.GetDirectoryName(report.HtmlPath)!, src)));
            summary = $"<img{(lazyLoad ? " loading=\"lazy\"" : "")} src=\"{url}\">";
        }
        return $"<details><summary>{summary}</summary><h4>Raw PlantUML</h4><pre>{WebUtility.HtmlEncode(diagram.CodeBehind)}</pre></details>";
    }

    private static bool IsAddress(string src) =>
        src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    // A relative URL from the page's folder to a file, each part escaped; a file on another drive keeps its full URL.
    private static string Href(string pageDirectory, string target)
    {
        var relative = Path.GetRelativePath(pageDirectory, target);
        return Path.IsPathRooted(relative)
            ? new Uri(Path.GetFullPath(target)).AbsoluteUri
            : string.Join('/', relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString));
    }
}
