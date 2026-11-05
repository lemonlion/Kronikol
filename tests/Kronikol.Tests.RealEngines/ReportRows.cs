using System.Text;
using System.Text.Json;

namespace Kronikol.Tests.RealEngines;

/// <summary>
/// A run's data file as these facts compare it, one line for a step and one for the calls: which steps a scenario lists, under
/// which address and with what outcome, and under which step each of its calls reads. The file itself is never printed.
/// </summary>
public sealed class ReportRows
{
    private readonly List<(string Name, JsonElement Scenario)> _scenarios = [];

    private ReportRows(JsonDocument document)
    {
        foreach (var feature in Items(document.RootElement, "features"))
            foreach (var scenario in Items(feature, "scenarios"))
                _scenarios.Add((scenario.GetProperty("name").GetString() ?? "", scenario));
    }

    public static string? DataFile(string directory) =>
        new[] { "TestRunReport.krkl", "TestRunReport.json" }.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    public static ReportRows Read(string path) => new(JsonDocument.Parse(Bytes(path)));

    public IReadOnlyList<string> ScenarioNames => _scenarios.Select(s => s.Name).ToArray();

    /// <summary>The steps and the calls of the scenario whose name starts with <paramref name="name"/>, as lines.</summary>
    /// <param name="name">How the scenario's name starts: an outline's rows carry their values after it.</param>
    /// <param name="occurrence">Which of the scenarios with that start, for an outline's rows.</param>
    /// <param name="withoutCalls">Calls left out for racing a delay against the next step, by path.</param>
    // These facts compare which of a scenario's rows a call belongs to. CallsOf reads the whole path.
    private static string? TopLevel(string? path) => path?.Split('.')[0];

    /// <summary>The scenario's calls, in order, each with the whole step path it reads: <c>0.0 /customers, 1.0 /orders</c>.</summary>
    public string CallsOf(string name)
    {
        var scenario = _scenarios.First(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Scenario;
        return string.Join(", ", Items(scenario, "httpInteractions")
            .Where(call => Text(call, "type") == "Request" && Text(call, "uri") is not null)
            .Select(call => $"{Text(call, "stepPath") ?? "-"} {new Uri(Text(call, "uri")!).AbsolutePath}"));
    }

    public string Of(string name, int occurrence = 0, params string[] withoutCalls)
    {
        var scenario = _scenarios.Where(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Select(s => s.Scenario).ElementAtOrDefault(occurrence);
        if (scenario.ValueKind == JsonValueKind.Undefined)
            return $"no scenario '{name}' in: {string.Join(" | ", ScenarioNames)}";

        var lines = new StringBuilder();
        Steps(Items(scenario, "backgroundSteps"), "b", lines, 0);
        Steps(Items(scenario, "steps"), "", lines, 0);
        var calls = Items(scenario, "httpInteractions")
            .Where(call => Text(call, "type") == "Request" && Text(call, "uri") is not null)
            .Select(call => (Step: TopLevel(Text(call, "stepPath")) ?? "-", Path: new Uri(Text(call, "uri")!).AbsolutePath))
            .Where(call => !withoutCalls.Contains(call.Path))
            .Select(call => $"{call.Step} {call.Path}");
        lines.Append("calls: ").Append(string.Join(", ", calls));
        return lines.ToString();
    }

    private JsonElement Scenario(string name) => _scenarios.First(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Scenario;

    /// <summary>What the report says of the scenario as a whole.</summary>
    public string? ResultOf(string name) => Text(Scenario(name), "result");

    public string? ErrorOf(string name) => Text(Scenario(name), "errorMessage");

    public TimeSpan? DurationOf(string name) => Duration(Scenario(name));

    /// <summary>How long a scenario's top-level row took, by its place in the list.</summary>
    public TimeSpan? StepDurationOf(string name, int step) => Duration(Items(Scenario(name), "steps").ElementAt(step));

    /// <summary>The reason a scenario's top-level row gives for not running, by its place in the list.</summary>
    public string? StepBypassReasonOf(string name, int step) => Text(Items(Scenario(name), "steps").ElementAt(step), "bypassReason");

    private static TimeSpan? Duration(JsonElement owner) =>
        owner.TryGetProperty("durationSeconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number ? TimeSpan.FromSeconds(seconds.GetDouble()) : null;

    /// <summary>The names of every file a scenario holds, on itself or on a row.</summary>
    public IReadOnlyList<string> AllFilesOf(string name)
    {
        var scenario = _scenarios.First(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Scenario;

        IEnumerable<string> Of(IEnumerable<JsonElement> steps) => steps.SelectMany(step =>
            Items(step, "attachments").Select(file => Text(file, "name") ?? "").Concat(Of(Items(step, "subSteps"))));

        return Items(scenario, "attachments").Select(file => Text(file, "name") ?? "")
            .Concat(Of(Items(scenario, "backgroundSteps"))).Concat(Of(Items(scenario, "steps"))).OrderBy(file => file, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The names of the files on the scenario itself, on no row.</summary>
    public IReadOnlyList<string> ScenarioFilesOf(string name) =>
        Items(_scenarios.First(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Scenario, "attachments").Select(file => Text(file, "name") ?? "").ToArray();

    /// <summary>The names an outline row's example values are held under, which say how they were grouped.</summary>
    public IReadOnlyList<string> ExampleValueNames(string name, int occurrence)
    {
        var scenario = _scenarios.Where(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Select(s => s.Scenario).ElementAt(occurrence);
        return scenario.TryGetProperty("exampleValues", out var values) ? values.EnumerateObject().Select(value => value.Name).ToArray() : [];
    }

    /// <summary>The names of the files on a scenario's top-level row, by its address (<c>b0</c>, <c>0</c>).</summary>
    public IReadOnlyList<string> FilesOn(string name, string address)
    {
        var scenario = _scenarios.First(s => s.Name.StartsWith(name, StringComparison.Ordinal)).Scenario;
        var (list, index) = address.StartsWith('b') ? ("backgroundSteps", int.Parse(address[1..])) : ("steps", int.Parse(address));
        return Items(Items(scenario, list).ElementAt(index), "attachments").Select(file => Text(file, "name") ?? "").ToArray();
    }

    private static void Steps(IEnumerable<JsonElement> steps, string prefix, StringBuilder lines, int depth)
    {
        var index = 0;
        foreach (var step in steps)
        {
            var address = $"{prefix}{index}";
            lines.Append(' ', depth * 2).Append(address).Append(' ');
            if (Text(step, "keyword") is { Length: > 0 } keyword)
                lines.Append(keyword).Append(' ');
            lines.Append(Text(step, "text") ?? "");
            if (Text(step, "status") is { } status && status != "Passed")
                lines.Append(" [").Append(status).Append(']');
            if (Text(step, "docString") is { } docString)
                lines.Append(" \"\"\"").Append(docString).Append("\"\"\"");
            if (step.TryGetProperty("parameters", out var parameters) && Strings(parameters).ToArray() is { Length: > 0 } values)
                lines.Append(" {").Append(string.Join(' ', values)).Append('}');
            lines.Append('\n');
            Steps(Items(step, "subSteps"), address + ".", lines, depth + 1);
            index++;
        }
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        JsonValueKind.Object => element.EnumerateObject().Where(property => property.Name is not ("kind" or "status" or "type")).SelectMany(property => Strings(property.Value)),
        _ => [],
    };

    private static IEnumerable<JsonElement> Items(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [];

    private static string? Text(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // A .krkl is the same JSON compressed whole, written without the keys that hold nothing, so an array may be absent.
    private static byte[] Bytes(string path)
    {
        if (!path.EndsWith(".krkl", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllBytes(path);

        using var file = File.OpenRead(path);
        using var content = Kronikol.Compression.ReportFileFormat.Open(file);
        using var decoded = new MemoryStream();
        content.Stream.CopyTo(decoded);
        return decoded.ToArray();
    }
}
