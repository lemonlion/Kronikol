namespace Kronikol.Ingestion;

/// <summary>
/// One NDJSON line that could not be parsed and was skipped.
/// </summary>
/// <remarks>
/// The common cause is not a bug in the producer but a killed process: a capturer that is terminated
/// mid-write leaves a truncated last line, and a whole run's report used to be lost to it. The readers
/// therefore skip and count malformed lines by default and surface them as
/// <see cref="Kronikol.Reports.DiagnosticKind.MalformedLine"/> diagnostics;
/// <see cref="IngestRequest.StrictParsing"/> restores the throw for CI.
/// </remarks>
/// <param name="Source">The file the line came from (or a caller-supplied label for a non-file reader).</param>
/// <param name="LineNumber">1-based line number within <paramref name="Source"/>.</param>
/// <param name="Excerpt">The first 80 characters of the offending line, for identification.</param>
/// <param name="Message">The parser's message.</param>
public sealed record MalformedLine(string Source, int LineNumber, string Excerpt, string Message)
{
    /// <summary>How much of the offending line <see cref="Excerpt"/> keeps.</summary>
    public const int ExcerptLength = 80;

    /// <summary>Trims a raw line to <see cref="ExcerptLength"/> characters, marking the cut with an ellipsis.</summary>
    public static string MakeExcerpt(string line) =>
        line.Length <= ExcerptLength ? line : line[..ExcerptLength] + "…";

    /// <inheritdoc />
    public override string ToString() => $"{Source}:{LineNumber}: {Message} — {Excerpt}";
}
