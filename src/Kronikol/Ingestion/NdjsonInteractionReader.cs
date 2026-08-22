using System.Text.Json;

namespace Kronikol.Ingestion;

/// <summary>
/// Reads <see cref="InteractionRecord"/> lines from NDJSON files (blank lines skipped).
/// </summary>
/// <remarks>
/// Two modes, chosen by whether the caller passes a <c>malformed</c> collector:
/// <list type="bullet">
/// <item>without one (the historical signatures) a malformed line throws
/// <see cref="FormatException"/> with its line number — strict, and what
/// <see cref="IngestRequest.StrictParsing"/> selects;</item>
/// <item>with one, malformed lines are skipped and recorded as <see cref="MalformedLine"/> so a capture
/// truncated by a killed process still yields a report from every complete line — the default for
/// <see cref="IngestPipeline"/> and <c>kronikol ingest</c>.</item>
/// </list>
/// </remarks>
public static class NdjsonInteractionReader
{
    /// <summary>Reads every record in <paramref name="path"/>, throwing on the first malformed line.</summary>
    public static List<InteractionRecord> ReadFile(string path) => ReadFile(path, malformed: null);

    /// <summary>
    /// Reads every record in <paramref name="path"/>. When <paramref name="malformed"/> is given,
    /// unparsable lines are skipped and recorded there instead of throwing.
    /// </summary>
    public static List<InteractionRecord> ReadFile(string path, ICollection<MalformedLine>? malformed)
    {
        // FileShare.ReadWrite: captures are tailed while a writer (a proxy tap, a fixture) still holds them open.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return Read(reader, path, malformed);
    }

    /// <summary>Reads every record from the given files, in file order then line order.</summary>
    public static List<InteractionRecord> ReadFiles(IEnumerable<string> paths) => ReadFiles(paths, malformed: null);

    /// <summary>
    /// Reads every record from the given files, in file order then line order. When
    /// <paramref name="malformed"/> is given, unparsable lines are skipped and recorded there.
    /// </summary>
    public static List<InteractionRecord> ReadFiles(IEnumerable<string> paths, ICollection<MalformedLine>? malformed)
    {
        var result = new List<InteractionRecord>();
        foreach (var path in paths)
            result.AddRange(ReadFile(path, malformed));
        return result;
    }

    /// <summary>
    /// Reads every record from <paramref name="reader"/>. <paramref name="sourceName"/> is used in error
    /// messages only. When <paramref name="malformed"/> is given, unparsable lines are skipped and
    /// recorded there instead of throwing.
    /// </summary>
    public static List<InteractionRecord> Read(TextReader reader, string? sourceName = null, ICollection<MalformedLine>? malformed = null) =>
        NdjsonLineReader.Read(reader, sourceName ?? "NDJSON", InteractionRecord.FromJson, malformed);
}

/// <summary>The shared NDJSON line loop behind the interaction and test-run readers.</summary>
internal static class NdjsonLineReader
{
    public static List<T> Read<T>(TextReader reader, string sourceName, Func<string, T> parse, ICollection<MalformedLine>? malformed)
    {
        var result = new List<T>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                result.Add(parse(line));
            }
            catch (JsonException ex)
            {
                if (malformed is null)
                    throw new FormatException($"{sourceName}:{lineNumber}: {ex.Message}", ex);
                malformed.Add(new MalformedLine(sourceName, lineNumber, MalformedLine.MakeExcerpt(line), ex.Message));
            }
        }

        return result;
    }
}
