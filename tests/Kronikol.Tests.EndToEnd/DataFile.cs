using System.Text;
using Kronikol.Compression;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The data file a run wrote beside its report, in either form: <c>TestRunReport.krkl</c>, the default from 5.0.0
/// (<c>CompressTestRunReport</c>), or <c>TestRunReport.json</c>. A test that reads what the run recorded reads it here.
/// </summary>
internal static class DataFile
{
    /// <summary>The path of the data file in <paramref name="directory"/>: the compressed one where the run wrote it.</summary>
    public static string In(string directory)
    {
        var compressed = Path.Combine(directory, "TestRunReport.krkl");
        return File.Exists(compressed) ? compressed : Path.Combine(directory, "TestRunReport.json");
    }

    /// <summary>The JSON the data file in <paramref name="directory"/> holds, decoded where it is compressed.</summary>
    public static string Read(string directory)
    {
        using var file = File.OpenRead(In(directory));
        using var content = ReportFileFormat.Open(file);
        using var reader = new StreamReader(content.Stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }
}
