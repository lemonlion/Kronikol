using System.Text;

namespace Kronikol.Reports;

/// <summary>
/// What an attachment's path names, decided once for both writers that place attachments beside a report: the run's
/// copier (<see cref="ReportGenerator.CopyAttachmentsToReportsFolder"/>, every in-process adapter and <c>kronikol
/// ingest</c>) and <c>kronikol merge</c>'s carrier (<see cref="Merge.MergeableReportRenderer"/>). Each still names the copy
/// it makes its own way (a run overwrites and numbers a clash <c>name_2.ext</c>; a merge never overwrites, reuses a file
/// with the same bytes and numbers a clash <c>name-1.ext</c>), since changing either would rename files in every report
/// with a clash. What a path names, and so the state an attachment ends in, is decided here, and a parity fact holds the
/// two writers to it (#148, <c>plans/MISSING_ATTACHMENTS_PLAN.md</c> section 4.1).
/// </summary>
internal static class AttachmentPlacement
{
    /// <summary>The folder a report's own copies are in, beside the report.</summary>
    internal const string FolderName = RunFileCollector.AttachmentsFolderName;

    /// <summary>How the file system compares paths here.</summary>
    internal static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>What an attachment's path names on this machine, before anything is copied.</summary>
    internal enum SourceKind
    {
        /// <summary>A URL: not a file, never copied, never a problem.</summary>
        Link,

        /// <summary>A file already in the report's own <c>attachments/</c> folder: linked where it is.</summary>
        InReport,

        /// <summary>A file elsewhere, to be copied in.</summary>
        File,

        /// <summary>No file at the path.</summary>
        Missing,

        /// <summary>Not a legal path on this machine.</summary>
        Unusable,
    }

    /// <summary>
    /// One attachment's path, read. <paramref name="Path"/> is the file's full path (for <see cref="SourceKind.File"/>,
    /// <see cref="SourceKind.InReport"/>, and <see cref="SourceKind.Missing"/>, where it is where the writer looked first) or
    /// the path as given (<see cref="SourceKind.Link"/>, <see cref="SourceKind.Unusable"/>). <paramref name="Link"/> is
    /// what the report links for a file it holds; <paramref name="Shown"/> is what a diagnostic prints for it.
    /// </summary>
    internal readonly record struct Source(SourceKind Kind, string Path, string Link, string Shown, Exception? Error = null);

    /// <summary>
    /// Reads <paramref name="attachment"/>'s path for a report written into <paramref name="reportDirectory"/>. A
    /// relative path starting <c>attachments/</c> is the report's own copy and is looked for there only; any other
    /// relative path is read against each of <paramref name="relativeBases"/> in turn, the first holding a file winning.
    /// A file directly in the report's <c>attachments/</c> folder is linked where it is, as copying it onto itself fails.
    /// </summary>
    internal static Source Classify(FileAttachment attachment, string reportDirectory, IReadOnlyList<string> relativeBases)
    {
        var given = attachment.RelativePath;
        if (attachment.IsUrl)
            return new Source(SourceKind.Link, given, given, given);

        try
        {
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(reportDirectory, FolderName));
            if (IsReportRelative(given))
            {
                var own = System.IO.Path.GetFullPath(System.IO.Path.Combine(reportDirectory, given));
                return File.Exists(own)
                    ? new Source(SourceKind.InReport, own, given.Replace('\\', '/'), own)
                    : new Source(SourceKind.Missing, own, given, own);
            }

            string[] candidates = System.IO.Path.IsPathRooted(given)
                ? [System.IO.Path.GetFullPath(given)]
                : relativeBases.Select(b => System.IO.Path.GetFullPath(given, b)).Distinct(StringComparer.FromComparison(PathComparison)).ToArray();
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate))
                    continue;
                return string.Equals(System.IO.Path.GetDirectoryName(candidate), folder, PathComparison)
                    ? new Source(SourceKind.InReport, candidate, FolderName + "/" + System.IO.Path.GetFileName(candidate), candidate)
                    : new Source(SourceKind.File, candidate, candidate, candidate);
            }

            return new Source(SourceKind.Missing, candidates[0], given, string.Join(" or ", candidates));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Source(SourceKind.Unusable, given, given, Printable(given), ex);
        }
    }

    /// <summary>Whether <paramref name="path"/> names the report's own <c>attachments/</c> folder, relative to the report.</summary>
    internal static bool IsReportRelative(string path) =>
        path.StartsWith(FolderName + "/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(FolderName + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is a file directly in <paramref name="reportDirectory"/>'s <c>attachments/</c> folder.</summary>
    internal static bool IsInReportFolder(string path, string reportDirectory)
    {
        try
        {
            return string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(reportDirectory, FolderName)), PathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the file at <paramref name="path"/> is not the one <paramref name="attachment"/> saw when it was attached:
    /// its length or last write differs. False when nothing was noted then.
    /// </summary>
    internal static bool ChangedSinceAttached(FileAttachment attachment, string path) =>
        attachment.WhenAttached is { } then && AttachedFile.Of(path) is { } now && now != then;

    /// <summary>A path as a diagnostic can print it: a blank one named, a control character written as its escape.</summary>
    internal static string Printable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "(blank path)";
        if (!path.Any(char.IsControl))
            return path;
        var builder = new StringBuilder(path.Length + 8);
        foreach (var c in path)
            builder.Append(char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString());
        return builder.ToString();
    }

    /// <summary>
    /// Replaces each attachment list of <paramref name="scenarios"/>, in report order: the scenario's own, then its
    /// background steps' and its steps', each step before its sub-steps.
    /// </summary>
    internal static void ForEachList(IEnumerable<Scenario> scenarios, Func<FileAttachment[], FileAttachment[]> replace)
    {
        foreach (var scenario in scenarios)
        {
            if (scenario.Attachments is { Length: > 0 } own)
                scenario.Attachments = replace(own);
            Steps(scenario.BackgroundSteps);
            Steps(scenario.Steps);
        }

        void Steps(ScenarioStep[]? steps)
        {
            foreach (var step in steps ?? [])
            {
                if (step.Attachments is { Length: > 0 } attachments)
                    step.Attachments = replace(attachments);
                Steps(step.SubSteps);
            }
        }
    }
}
