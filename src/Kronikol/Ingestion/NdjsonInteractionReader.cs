using System.Text.Json;

namespace Kronikol.Ingestion;

/// <summary>Reads <see cref="InteractionRecord"/> lines from NDJSON files (blank lines skipped, malformed lines reported with their line number).</summary>
public static class NdjsonInteractionReader
{
    /// <summary>Reads every record in <paramref name="path"/>.</summary>
    public static List<InteractionRecord> ReadFile(string path)
    {
        // FileShare.ReadWrite: captures are tailed while a writer (a proxy tap, a fixture) still holds them open.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return Read(reader, path);
    }

    /// <summary>Reads every record from the given files, in file order then line order.</summary>
    public static List<InteractionRecord> ReadFiles(IEnumerable<string> paths)
    {
        var result = new List<InteractionRecord>();
        foreach (var path in paths)
            result.AddRange(ReadFile(path));
        return result;
    }

    /// <summary>Reads every record from <paramref name="reader"/>. <paramref name="sourceName"/> is used in error messages only.</summary>
    public static List<InteractionRecord> Read(TextReader reader, string? sourceName = null)
    {
        var result = new List<InteractionRecord>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                result.Add(InteractionRecord.FromJson(line));
            }
            catch (JsonException ex)
            {
                throw new FormatException($"{sourceName ?? "NDJSON"}:{lineNumber}: {ex.Message}", ex);
            }
        }

        return result;
    }
}
