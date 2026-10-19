using System.Text;
using System.Text.Json;
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

    /// <summary>
    /// <paramref name="path"/>, a data file's <c>.json</c> name, or the compressed <c>.krkl</c> beside it where the run
    /// wrote that one instead, as it does by default from 5.0.0 (<c>CompressTestRunReport</c>). A test that names the
    /// JSON reads whichever form the run wrote; one about the JSON itself turns the option off.
    /// </summary>
    public static string Resolve(string path)
    {
        if (File.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return path;
        var compressed = Path.ChangeExtension(path, ".krkl");
        return File.Exists(compressed) ? compressed : path;
    }

    /// <summary>The JSON of the data file <paramref name="path"/> names, in the form the run wrote (<see cref="Resolve"/>).</summary>
    public static string ReadAny(string path) => Read(Resolve(path));

    /// <summary>
    /// The items of the array <paramref name="name"/> on <paramref name="parent"/>, or none where the key is absent: a
    /// <c>.krkl</c> leaves out an array with nothing in it unless the schema requires it there.
    /// </summary>
    public static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var items) ? items.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
