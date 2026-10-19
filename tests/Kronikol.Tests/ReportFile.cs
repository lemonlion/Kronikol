using System.Text;
using Kronikol.Compression;

namespace Kronikol.Tests;

/// <summary>
/// The JSON a report data file holds, in every form the readers take: <c>TestRunReport.json</c>, and the compressed
/// <c>TestRunReport.krkl</c> decoded (plans/REPORT_FORMAT_PLAN.md section 5, the test helper). A test that reads a run's
/// data file reads it here, so that what it checks is what the file says, not the bytes it is stored in.
/// </summary>
internal static class ReportFile
{
    public static string Read(string path)
    {
        using var file = File.OpenRead(path);
        using var content = ReportFileFormat.Open(file);
        using var reader = new StreamReader(content.Stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }
}
