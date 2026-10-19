namespace Kronikol.Reports;

/// <summary>
/// The attachments a report does not hold, and those whose file changed after they were attached, said as one
/// <see cref="DiagnosticKind.AttachmentFailure"/> entry per report (#148, <c>plans/MISSING_ATTACHMENTS_PLAN.md</c> section
/// 4.2). One, because <c>kronikol query</c>'s <c>!</c> line shows only the first message of a kind, cut to 160
/// characters, with <c>(×N)</c> counting entries: an entry per file or per state would hide the count behind the first
/// one's text. So the message leads with the count and says what it is about in its own words, then the first
/// <see cref="PathsNamed"/> paths in report order and how many more there are.
/// </summary>
internal sealed class AttachmentAccount(string reportDirectory)
{
    /// <summary>How many paths a message names before it says how many more there are.</summary>
    internal const int PathsNamed = 5;

    private enum Why { NotFound, Gone, NotCopied, NotUsable, NotWritten }

    private readonly List<(Why Why, string Shown, bool InReportFolder)> _missing = [];
    private readonly List<string> _changed = [];
    private Exception? _firstCopyFailure;
    private Exception? _firstWriteFailure;

    /// <summary>Whether there is anything to say.</summary>
    internal bool IsEmpty => _missing.Count == 0 && _changed.Count == 0;

    /// <summary>An attachment with no file where its path points: <paramref name="wasThere"/> when there was one when it was attached.</summary>
    internal void NotFound(string shown, string path, bool wasThere) =>
        _missing.Add((wasThere ? Why.Gone : Why.NotFound, shown, AttachmentPlacement.IsInReportFolder(path, reportDirectory)));

    /// <summary>An attachment whose file is there and could not be copied into the report.</summary>
    internal void NotCopied(string shown, Exception error)
    {
        _firstCopyFailure ??= error;
        _missing.Add((Why.NotCopied, shown, false));
    }

    /// <summary>An attachment whose path is not a legal path on this machine.</summary>
    internal void NotUsable(string shown) => _missing.Add((Why.NotUsable, shown, false));

    /// <summary>An attachment a capture carried as a body that could not be written to a file, so it never reached the report.</summary>
    internal void NotWritten(string name, Exception error)
    {
        _firstWriteFailure ??= error;
        _missing.Add((Why.NotWritten, name, false));
    }

    /// <summary>An attachment the report holds, whose file changed after it was attached: the report holds what it found.</summary>
    internal void Changed(string shown) => _changed.Add(shown);

    /// <summary>Adds the attachments an ingest dropped before the report's model held them (<see cref="AttachmentContext"/>).</summary>
    internal void Include(AttachmentContext? context)
    {
        if (context is null)
            return;
        for (var i = 0; i < context.BlankPaths; i++)
            NotUsable(AttachmentPlacement.Printable(""));
        foreach (var (name, error) in context.Unwritten)
            NotWritten(name, error);
    }

    /// <summary>The entry's message, or null when there is nothing to say.</summary>
    internal string? Describe(AttachmentContext? context = null)
    {
        if (IsEmpty)
            return null;

        var sentences = new List<string>();
        if (_missing.Count > 0)
        {
            var parts = new List<string>();
            var notFound = Count(Why.NotFound);
            if (notFound > 0)
            {
                var files = Distinct(_missing.Where(m => m.Why == Why.NotFound).Select(m => m.Shown)).Count;
                parts.Add($"{notFound} not found when the report was drawn" + (files == notFound ? "" : $" ({files} file{(files == 1 ? "" : "s")})"));
            }

            var gone = Count(Why.Gone);
            if (gone > 0)
                parts.Add(gone == 1
                    ? "1 was there when it was attached and gone when the report was drawn"
                    : $"{gone} were there when they were attached and gone when the report was drawn");
            if (Count(Why.NotCopied) is var notCopied and > 0)
                parts.Add($"{notCopied} could not be copied ({Describe(_firstCopyFailure!)})");
            if (Count(Why.NotUsable) is var notUsable and > 0)
                parts.Add(notUsable == 1 ? "1 has a path that is not usable" : $"{notUsable} have a path that is not usable");
            if (Count(Why.NotWritten) is var notWritten and > 0)
                parts.Add($"{notWritten} could not be written from the Cucumber messages ({Describe(_firstWriteFailure!)})");

            sentences.Add($"{_missing.Count} attachment(s) are not in the report: {string.Join(", ", parts)}: {Named(_missing.Select(m => m.Shown))}.");
        }

        if (_changed.Count > 0)
            sentences.Add($"{_changed.Count} attachment(s) changed after {(_changed.Count == 1 ? "it was" : "they were")} attached, "
                          + $"and the report holds the version found when it was drawn: {Named(_changed)}.");

        if (context is not null)
        {
            var lookedFor = _missing.Any(m => m.Why is Why.NotFound);
            if (context.CleanedAttachments && _missing.Any(m => m.Why is Why.NotFound && m.InReportFolder))
                sentences.Add("--clean-attachments emptied the report's attachments/ folder before these were read.");
            if (lookedFor && context.RelativeBase is { } relativeBase)
                sentences.Add(context.BaseGiven
                    ? $"Relative paths were resolved against the attachments base, {relativeBase}."
                    : $"Relative paths were resolved against the working directory, {relativeBase}, since no attachments base was given (--attachments-base).");
        }

        return string.Join(" ", sentences);

        int Count(Why why) => _missing.Count(m => m.Why == why);
    }

    private static string Describe(Exception error) => $"{error.GetType().Name}: {error.Message}";

    /// <summary>The first <see cref="PathsNamed"/> distinct paths, and how many more there are.</summary>
    private static string Named(IEnumerable<string> shown)
    {
        var distinct = Distinct(shown);
        var named = string.Join(", ", distinct.Take(PathsNamed));
        return distinct.Count > PathsNamed ? $"{named}, and {distinct.Count - PathsNamed} more" : named;
    }

    private static List<string> Distinct(IEnumerable<string> shown) =>
        shown.Distinct(StringComparer.FromComparison(AttachmentPlacement.PathComparison)).ToList();
}

/// <summary>
/// What an ingest knows about its capture's attachments that the report's copier cannot see: the base its relative paths
/// were read against, whether <c>--clean-attachments</c> emptied the report's folder first, and the attachments that never
/// reached the model (a blank path; a Cucumber body that could not be written). The copier folds them into its one entry.
/// </summary>
internal sealed record AttachmentContext
{
    /// <summary>The folder relative paths in the capture were read against, when any path was relative; null otherwise.</summary>
    public string? RelativeBase { get; init; }

    /// <summary>Whether <see cref="RelativeBase"/> was given (<c>--attachments-base</c>) rather than the working directory.</summary>
    public bool BaseGiven { get; init; }

    /// <summary>Whether <c>--clean-attachments</c> emptied the report's <c>attachments/</c> folder before the copier ran.</summary>
    public bool CleanedAttachments { get; init; }

    /// <summary>How many attachment records had a blank path, and were dropped.</summary>
    public int BlankPaths { get; init; }

    /// <summary>The Cucumber attachment bodies that could not be written to a file, by name, with why.</summary>
    public IReadOnlyList<(string Name, Exception Error)> Unwritten { get; init; } = [];
}
