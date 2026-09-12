using System.Text.Json;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Reads <c>Failures.jsonl</c> the way a consumer has to: one header line, then one line per failure.
///
/// <para>Shared rather than repeated because the header is exactly the kind of change that a test which
/// splits on newlines and parses every line absorbs silently - it would read the header as a failure with
/// no address and either throw somewhere unhelpful or, worse, count it.</para>
/// </summary>
internal static class FailuresJsonl
{
    /// <summary>The header line: the contract version, the run, and the failure count.</summary>
    public static JsonElement Header(string jsonl) =>
        JsonDocument.Parse(jsonl.TrimEnd('\n').Split('\n')[0]).RootElement.Clone();

    /// <summary>Every failure record, with the header skipped.</summary>
    public static IReadOnlyList<JsonElement> Failures(string jsonl) =>
        [.. jsonl.TrimEnd('\n').Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];
}
