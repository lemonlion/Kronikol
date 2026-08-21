using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kronikol.Ingestion;

/// <summary>
/// One line of the companion <em>tests</em> NDJSON file that supplies scenario outcome, timing and steps to
/// <c>kronikol ingest</c> / <see cref="IngestPipeline"/>. Three event kinds:
/// <list type="bullet">
/// <item><c>start</c> — the test began (<c>testId</c>, <c>testName</c>, optional <c>feature</c>, <c>timestamp</c>).</item>
/// <item><c>step</c> — a named step inside the test (<c>text</c>, optional <c>keyword</c>, <c>status</c>, <c>durationMs</c>, <c>error</c>, <c>level</c>).
/// Top-level steps (<c>level</c> 0 or absent) also draw a step delimiter bar in the sequence diagram at their
/// <c>timestamp</c>, exactly like Kronikol's step tracking; nested steps (<c>level</c> &gt; 0) appear as sub-steps in the step list.</item>
/// <item><c>assertion</c> — an assertion the test made (<c>text</c>, <c>status</c>: passed | failed, optional <c>error</c>).
/// Draws a green ✓ / red ✗ assertion note in the sequence diagram at its <c>timestamp</c>, exactly like Kronikol's
/// assertion tracking, and appears as a sub-step of the enclosing step.</item>
/// <item><c>end</c> — the verdict (<c>status</c>: passed | failed | skipped | timedOut | interrupted | bypassed; <c>durationMs</c>; optional <c>error</c>).</item>
/// </list>
/// <c>testId</c> must equal the <c>testId</c> stamped on the interaction records for attribution to work.
/// </summary>
public sealed record TestRunRecord
{
    /// <summary><c>start</c>, <c>step</c> or <c>end</c>.</summary>
    [JsonPropertyName("event")] public required string Event { get; init; }

    /// <summary>The scenario id — the same value the interaction records carry as <c>testId</c>.</summary>
    [JsonPropertyName("testId")] public required string TestId { get; init; }

    /// <summary>Display name of the test.</summary>
    [JsonPropertyName("testName")] public string? TestName { get; init; }

    /// <summary>Grouping for the report (a spec file, a class, a suite). Scenarios with the same feature render under one heading.</summary>
    [JsonPropertyName("feature")] public string? Feature { get; init; }

    /// <summary>When the event happened (ISO-8601).</summary>
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Outcome for <c>end</c> (and optionally <c>step</c>) events.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>Duration in milliseconds for <c>end</c> (and optionally <c>step</c>) events.</summary>
    [JsonPropertyName("durationMs")] public double? DurationMs { get; init; }

    /// <summary>Failure message for a failed <c>end</c> event.</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>Step text for <c>step</c> events.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>Optional Gherkin-style keyword for <c>step</c> events (<c>Given</c>, <c>When</c>, …).</summary>
    [JsonPropertyName("keyword")] public string? Keyword { get; init; }

    /// <summary>Steps only: nesting depth. 0 / absent = top level (draws a delimiter); &gt; 0 = sub-step of the preceding top-level step.</summary>
    [JsonPropertyName("level")] public int? Level { get; init; }

    /// <summary><c>true</c> for <c>step</c> and <c>assertion</c> events that carry a timestamp — the ones that draw something in the diagram.</summary>
    [JsonIgnore] public bool IsDiagramMarker =>
        Timestamp is not null
        && (string.Equals(Event, "assertion", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(Event, "step", StringComparison.OrdinalIgnoreCase) && (Level ?? 0) == 0));

    /// <summary>Serialises this record as one NDJSON line.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, InteractionRecord.JsonOptions);

    /// <summary>Parses one NDJSON line.</summary>
    public static TestRunRecord FromJson(string json) =>
        JsonSerializer.Deserialize<TestRunRecord>(json, InteractionRecord.JsonOptions)
        ?? throw new JsonException("The line did not contain a test-run object.");
}

/// <summary>Reads <see cref="TestRunRecord"/> lines from NDJSON.</summary>
public static class NdjsonTestRunReader
{
    /// <summary>Reads every record in <paramref name="path"/>.</summary>
    public static List<TestRunRecord> ReadFile(string path)
    {
        // FileShare.ReadWrite: captures are tailed while a writer (a proxy tap, a fixture) still holds them open.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return Read(reader, path);
    }

    /// <summary>Reads every record from <paramref name="reader"/>.</summary>
    public static List<TestRunRecord> Read(TextReader reader, string? sourceName = null)
    {
        var result = new List<TestRunRecord>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                result.Add(TestRunRecord.FromJson(line));
            }
            catch (JsonException ex)
            {
                throw new FormatException($"{sourceName ?? "tests NDJSON"}:{lineNumber}: {ex.Message}", ex);
            }
        }

        return result;
    }
}
