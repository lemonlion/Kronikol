namespace Kronikol.ReqNRoll;

/// <summary>
/// Normalization helpers for Gherkin description text (mirrors the Cucumber ingest path
/// so live Reqnroll runs and ingested message streams produce identical report content).
/// </summary>
internal static class GherkinText
{
    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Removes the common leading indentation Gherkin keeps on description blocks.</summary>
    internal static string? Dedent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var indent = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())).Trim('\n');
    }
}
