namespace Kronikol.Tool.Query;

/// <summary>
/// The checks that stand between opening a file and answering questions about it: is this a Kronikol
/// report at all, and is it a shape this build understands.
/// </summary>
/// <remarks>
/// <para>These used to live inline in <c>query</c>, which meant the other two commands that reach
/// <see cref="ReportScanner"/> did not have them. <c>ctrf</c> had neither version gate, so any valid
/// JSON became a CTRF document with zero tests and exit 0. <c>diff</c> reached the scanner by a third
/// path with no gates and no <c>ResolveReport</c>, so an arbitrary file became "the new run" and every
/// scenario in the real one was reported as removed.</para>
///
/// <para>The identity check keys on a root <c>features</c> array rather than on <c>formatVersion</c>,
/// because absent-formatVersion is legitimate — it is absent on every report written before 3.1.0, six
/// hand-written fixtures and eight of the fifteen example reports on disk included. <c>features</c> is
/// the marker every writer has emitted on every version. It must be the array and not merely the key, so
/// that this agrees with <c>MergeableReportReader</c> about the malformed inputs it exists to catch.</para>
///
/// <para>Refusal is exit 1, matching the version gates it replaces. Exit 2 is this tool's usage error,
/// and a file that is not a report is not a usage error — the command was well-formed.</para>
/// </remarks>
internal static class ReportGate
{
    /// <summary>
    /// Writes the reason and returns <c>1</c> when <paramref name="index"/> must not be answered from;
    /// returns <c>null</c> when it is a report this build can read.
    /// </summary>
    internal static int? Refuse(ReportIndex index, string path, TextWriter error)
    {
        if (!index.HasFeatures)
        {
            error.WriteLine(
                $"{path} is not a Kronikol report: it has no `features` array at the root. "
                + "Point at a TestRunReport.json, or the directory holding one.");
            return 1;
        }

        // Absent means "written before the shape was versioned", which is a real answer. A version this
        // build does not know is refused, because half-reading a shape whose keys have changed meaning
        // produces a confident wrong answer rather than an error, which is what the field exists to stop.
        if (index.FormatVersion is { } format && format != Kronikol.Reports.ReportGenerator.ReportFormatVersion)
        {
            error.WriteLine(format == ReportScanner.UnreadableVersion
                ? $"{path} declares a formatVersion that is not a number. It is not a Kronikol report this tool can read."
                : $"{path} declares formatVersion {format}; this tool understands {Kronikol.Reports.ReportGenerator.ReportFormatVersion}. Upgrade Kronikol.Tool.");
            return 1;
        }

        if (index.MergeableFormatVersion is { } mergeable and not Kronikol.Reports.Merge.MergeableReportReader.MergeableFormatVersion)
        {
            error.WriteLine(mergeable == ReportScanner.UnreadableVersion
                ? $"{path} declares a mergeableFormatVersion that is not a number. It is not a mergeable report this tool can read."
                : $"{path} declares mergeableFormatVersion {mergeable}, which this tool does not understand. Upgrade Kronikol.Tool.");
            return 1;
        }

        return null;
    }
}
