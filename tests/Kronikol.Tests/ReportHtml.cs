using System.Text;
using Kronikol.Reports;

namespace Kronikol.Tests;

/// <summary>
/// Reads a report page as a reader gets it (plans/HTML_COMPRESSION_PLAN.md 4.7, Q13), as <see cref="ReportFile"/> reads a
/// report file. <c>TestRunReport.html</c> and <c>Specifications.html</c> are written compressed, a loader page holding
/// the report as zstd frames; this returns the plain page <c>kronikol decode</c> writes from one, with every item in gzip
/// and base64, and any other file as it is. So a fact reads what ships, not a page no reader gets.
/// </summary>
internal static class ReportHtml
{
    public static string Read(string path) => HtmlReportFile.ToPlain(File.ReadAllText(path));

    public static string Read(string path, Encoding encoding) => HtmlReportFile.ToPlain(File.ReadAllText(path, encoding));

    public static async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        HtmlReportFile.ToPlain(await File.ReadAllTextAsync(path, cancellationToken));

    public static async Task<string> ReadAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) =>
        HtmlReportFile.ToPlain(await File.ReadAllTextAsync(path, encoding, cancellationToken));
}
