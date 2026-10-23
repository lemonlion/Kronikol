namespace Kronikol.Reports;

/// <summary>
/// Reads the HTML reports Kronikol writes, for code that reads a page's markup itself (an HTML parser in a test, a
/// script). <c>TestRunReport.html</c> and <c>Specifications.html</c> are written compressed: a small page that holds the
/// report as zstd frames and opens it in the browser. These give the plain page instead, every item embedded in it in
/// gzip and base64 as a page written before the compressed page holds it, which a browser opens as it is.
/// <c>kronikol decode</c> does the same from the command line.
/// </summary>
public static class HtmlReportFile
{
    /// <summary>True when <paramref name="html"/> is a compressed report page.</summary>
    public static bool IsCompressed(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return CompressedPage.IsCompressed(html);
    }

    /// <summary>The plain page of <paramref name="html"/>: a compressed report decoded, any other page as it is.</summary>
    /// <exception cref="InvalidDataException">The page is a compressed report that is damaged.</exception>
    public static string ToPlain(string html) => IsCompressed(html) ? CompressedPage.ToPlain(html) : html;

    /// <summary>The plain page of the report at <paramref name="path"/> (see <see cref="ToPlain"/>).</summary>
    /// <exception cref="InvalidDataException">The file is a compressed report that is damaged.</exception>
    public static string ReadPlain(string path) => ToPlain(File.ReadAllText(path));
}
