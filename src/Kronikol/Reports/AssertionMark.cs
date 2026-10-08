namespace Kronikol.Reports;

/// <summary>
/// The data file's assertion mark (#145): from 4.14.0 a tracked assertion's step is written with
/// <c>"assertion": true</c>, and a report written before it is read by the old inference, every step without a keyword.
/// Compiled for every target, so the merge reader can read a shard by it as <c>kronikol query</c> reads a report.
/// </summary>
internal static class AssertionMark
{
    /// <summary>The first version whose data file marks every tracked assertion.</summary>
    public static readonly Version Since = new(4, 14, 0);

    /// <summary>Whether a report from <paramref name="version"/> marks its assertions, so an unmarked step is a step.</summary>
    public static bool MarksAssertions(string? version) =>
        version is not null &&
        Version.TryParse(version.Split('-', '+')[0], out var parsed) &&
        parsed >= Since;
}
