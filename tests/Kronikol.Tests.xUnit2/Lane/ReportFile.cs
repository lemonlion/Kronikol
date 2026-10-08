using System.Globalization;
using System.Text.Json;

namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>A scenario of the report, with the paths of the requests filed under it.</summary>
public sealed record ReportScenario(
    string Id,
    string Feature,
    string Name,
    string Result,
    double DurationSeconds,
    DateTimeOffset? EndedAt,
    string? ErrorMessage,
    IReadOnlyList<string> Calls)
{
    public override string ToString() => $"{Feature}: {Name} ({Result}, {DurationSeconds:0.000} s, calls {string.Join(" ", Calls)})";
}

/// <summary>A call the report put in its background section, and the scenario whose end it outlived, if any.</summary>
public sealed record BackgroundCall(string Path, string? ExpiredFrom);

/// <summary>A report diagnostic.</summary>
public sealed record ReportDiagnostic(string Kind, string Message);

/// <summary>
/// The fields of a TestRunReport.json the lane compares with the runner's results, read as JSON one field at a
/// time. The file is never printed: failure messages describe it by these fields.
/// </summary>
public sealed class ReportFile
{
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public required IReadOnlyList<ReportScenario> Scenarios { get; init; }
    public required IReadOnlyList<BackgroundCall> Background { get; init; }
    public required IReadOnlyList<ReportDiagnostic> Diagnostics { get; init; }

    public static ReportFile Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        var scenarios = new List<ReportScenario>();
        foreach (var feature in root.GetProperty("features").EnumerateArray())
        {
            var featureName = feature.GetProperty("name").GetString() ?? "";
            foreach (var scenario in feature.GetProperty("scenarios").EnumerateArray())
            {
                scenarios.Add(new ReportScenario(
                    Id: scenario.GetProperty("id").GetString() ?? "",
                    Feature: featureName,
                    Name: scenario.GetProperty("name").GetString() ?? "",
                    Result: scenario.GetProperty("result").GetString() ?? "",
                    DurationSeconds: scenario.GetProperty("durationSeconds").GetDouble(),
                    EndedAt: Instant(scenario, "endedAt"),
                    ErrorMessage: String(scenario, "errorMessage"),
                    Calls: Requests(scenario, "httpInteractions").Select(r => r.Path).ToArray()));
            }
        }

        var background = root.TryGetProperty("background", out var section)
            ? Requests(section, "interactions").ToArray()
            : [];

        var diagnostics = root.TryGetProperty("diagnostics", out var entries)
            ? entries.EnumerateArray()
                .Select(d => new ReportDiagnostic(String(d, "kind") ?? "", String(d, "message") ?? ""))
                .ToArray()
            : [];

        return new ReportFile
        {
            Start = Instant(root, "startTime") ?? throw new FormatException("The report has no startTime."),
            End = Instant(root, "endTime") ?? throw new FormatException("The report has no endTime."),
            Scenarios = scenarios,
            Background = background,
            Diagnostics = diagnostics,
        };
    }

    public ReportScenario[] Named(string feature, string name) =>
        Scenarios.Where(s => s.Feature == feature && s.Name == name).ToArray();

    private static IEnumerable<BackgroundCall> Requests(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out var interactions) || interactions.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var interaction in interactions.EnumerateArray())
        {
            if (String(interaction, "type") != "Request" || String(interaction, "uri") is not { } uri)
                continue;
            yield return new BackgroundCall(new Uri(uri).AbsolutePath, String(interaction, "expiredFrom"));
        }
    }

    private static string? String(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Instant(JsonElement owner, string property) =>
        String(owner, property) is { } text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;
}
