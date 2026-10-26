using System.IO.Compression;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The weight the search adds to every page (plans/SEARCH_PLAN.md 4.12): its four scripts and its stylesheet, embedded
/// as written. At 5.11.0 they come to 199 KB as written and 51 KB compressed, past the plan's estimate of 60 to 100 KB
/// and its ceiling of 120 KB as written; a page has been compressed since 5.6.0, so what a report carries is the
/// compressed size. The ceilings hold both about where they are, so the search grows only when someone chooses it.
/// </summary>
public class SearchWeightTests
{
    private static readonly string[] Files =
    [
        "report-search-ui-script.js", "report-search-results-script.js", "report-search-hit-script.js",
        "report-search-marks-script.js", "search-styles.css",
    ];

    private static byte[] Resource(string file)
    {
        var assembly = typeof(ReportGenerator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void The_search_adds_at_most_210_KB_as_written_and_56_KB_compressed()
    {
        var all = Files.SelectMany(Resource).ToArray();
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(all);

        Assert.True(all.Length <= 210 * 1024, $"the search's files are {all.Length / 1024.0:F1} KB as written");
        Assert.True(packed.Length <= 56 * 1024, $"the search's files are {packed.Length / 1024.0:F1} KB compressed");
    }
}
