using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// Search and comparison. <c>grep</c> answers the question a passing test suite still leaves open — "the
/// number on screen is wrong, where did it come from" — by returning addresses rather than content.
/// <c>compare</c> uses a passing neighbour as an oracle for a failing scenario, and <c>diff</c> does the
/// same across two runs.
/// </summary>
internal static partial class QueryCommand
{
    private static int Grep(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0)
        {
            error.WriteLine("What are you looking for? kronikol query grep <report> \"4173\"");
            return 2;
        }

        var needle = options.Positional[0];
        var targets = (options.In ?? "bodies,uris,steps,assertions").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var wantsBodies = targets.Contains("bodies");
        var hits = new List<string>();

        foreach (var scenario in index.Scenarios)
        {
            if (targets.Contains("steps") || targets.Contains("assertions"))
            {
                foreach (var (path, _, step) in scenario.AllSteps())
                {
                    if (step.IsAssertion && !targets.Contains("assertions"))
                        continue;
                    if (!step.IsAssertion && !targets.Contains("steps"))
                        continue;

                    if (step.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || (step.FailureMessage?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
                        hits.Add($"{scenario.Address}/{path,-6} {(step.IsAssertion ? "assertion" : "step")}  "
                                 + QueryWriter.OneLine(step.FailureMessage ?? step.Text, 110));
                }
            }

            if (targets.Contains("uris"))
                foreach (var interaction in scenario.Interactions.Where(i => i.Uri.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                    hits.Add($"{interaction.Address(scenario),-9} uri        {QueryWriter.OneLine(interaction.Uri, 110)}");

            if (targets.Contains("headers"))
                foreach (var interaction in scenario.Interactions.Where(i => i.HeaderCount > 0))
                    foreach (var (key, value) in PayloadReader.Headers(index, interaction))
                        if (key.Contains(needle, StringComparison.OrdinalIgnoreCase) || (value?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
                            hits.Add($"{interaction.Address(scenario),-9} header     {key}: {QueryWriter.OneLine(value, 90)}");
        }

        // Bodies are searched once per distinct content, not once per occurrence: the same body appears
        // dozens of times in a real report and reading it dozens of times would be the slow half of this.
        if (wantsBodies)
        {
            foreach (var body in index.Bodies.Values)
            {
                var content = PayloadReader.Read(index, body.First);
                if (content is null || !content.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                var where = body.Occurrences.Count == 1
                    ? body.Occurrences[0]
                    : $"{body.Occurrences[0]} ×{body.Occurrences.Count}";

                if (options.Values)
                    foreach (var path in PathsContaining(content, needle).Take(4))
                        hits.Add($"{where,-16} body       {path}");
                else
                    hits.Add($"{where,-16} body       {body.Hash} {QueryWriter.Size(body.Length)}  {Excerpt(content, needle)}");
            }
        }

        if (targets.Contains("notes"))
        {
            foreach (var scenario in index.Scenarios)
                for (var d = 0; d < scenario.Diagrams.Count; d++)
                {
                    var diagram = PayloadReader.Read(index, scenario.Diagrams[d]);
                    if (diagram is null)
                        continue;
                    foreach (var (i, text) in PayloadReader.Notes(diagram))
                        if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                            hits.Add($"{scenario.Address}/d{d}/n{i,-4} note       {Excerpt(text, needle)}");
                }
        }

        if (options.Count)
        {
            writer.Line(hits.Count.ToString());
            return 0;
        }

        if (hits.Count == 0)
        {
            writer.Line($"\"{needle}\" is not in {string.Join(", ", targets)}");
            writer.Footer("--in bodies,headers,uris,steps,assertions,notes widens the search · notes are searched last because they are the expensive one");
            return 0;
        }

        writer.Page(hits, options.Offset, Math.Min(options.Limit, 200), "hits", hit => writer.Line(hit),
            $"grep \"{needle}\" ");
        return 0;
    }

    /// <summary>The JSON paths whose value contains the needle — what <c>--values</c> is for.</summary>
    private static IEnumerable<string> PathsContaining(string body, string needle)
    {
        List<string> found = [];
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            Walk(document.RootElement, "$");
        }
        catch (System.Text.Json.JsonException)
        {
            return [Excerpt(body, needle)];
        }

        return found;

        void Walk(System.Text.Json.JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Walk(property.Value, path == "$" ? "$." + property.Name : $"{path}.{property.Name}");
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in element.EnumerateArray())
                        Walk(item, $"{path}[{i++}]");
                    break;
                default:
                    var text = element.ToString();
                    if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        found.Add($"{path} = {QueryWriter.OneLine(text, 60)}");
                    break;
            }
        }
    }

    private static string Excerpt(string text, string needle)
    {
        var at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return "";
        var from = Math.Max(0, at - 30);
        var to = Math.Min(text.Length, at + needle.Length + 30);
        return (from > 0 ? "…" : "") + QueryWriter.OneLine(text[from..to], 80) + (to < text.Length ? "…" : "");
    }

    private static int Compare(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count < 2
            || !Address.TryParse(options.Positional[0], out var leftAddress)
            || !Address.TryParse(options.Positional[1], out var rightAddress))
        {
            error.WriteLine("Which two? kronikol query compare <report> s3 s7");
            return 2;
        }

        if (index.Scenario(leftAddress.Scenario) is not { } left || index.Scenario(rightAddress.Scenario) is not { } right)
        {
            error.WriteLine("One of those scenarios is not in this report.");
            return 2;
        }

        writer.Line($"{left.Address} {QueryWriter.OneLine(left.Name, 60)}  [{left.Result}]  {left.DurationSeconds:0.##}s");
        writer.Line($"{right.Address} {QueryWriter.OneLine(right.Name, 60)}  [{right.Result}]  {right.DurationSeconds:0.##}s");
        writer.Line();

        if (left.ExampleValues.Count > 0 || right.ExampleValues.Count > 0)
        {
            foreach (var key in left.ExampleValues.Keys.Union(right.ExampleValues.Keys).Order())
            {
                var a = left.ExampleValues.GetValueOrDefault(key, "—");
                var b = right.ExampleValues.GetValueOrDefault(key, "—");
                if (a != b)
                    writer.Line($"example {key}: {a} → {b}");
            }
            writer.Line();
        }

        CompareSequences(writer, "steps",
            left.AllSteps().Select(s => $"{s.Step.Display} [{s.Step.Status}]").ToList(),
            right.AllSteps().Select(s => $"{s.Step.Display} [{s.Step.Status}]").ToList());

        CompareSequences(writer, "calls",
            left.Interactions.Where(i => i.Type == "Request").Select(i => $"{i.ServiceName} {i.Summary()}").ToList(),
            right.Interactions.Where(i => i.Type == "Request").Select(i => $"{i.ServiceName} {i.Summary()}").ToList());

        var leftBodies = left.Interactions.Select(i => i.BodyHash).Where(h => h is not null).ToHashSet();
        var rightBodies = right.Interactions.Select(i => i.BodyHash).Where(h => h is not null).ToHashSet();
        var shared = leftBodies.Intersect(rightBodies).Count();
        writer.Line();
        writer.Line($"bodies: {leftBodies.Count} vs {rightBodies.Count}, {shared} byte-identical");

        writer.Footer("a passing neighbour is the best oracle for a failing scenario — the first differing call is usually the answer");
        return 0;
    }

    private static void CompareSequences(QueryWriter writer, string noun, List<string> left, List<string> right)
    {
        writer.Line($"{noun}: {left.Count} vs {right.Count}");
        var shown = 0;
        for (var i = 0; i < Math.Max(left.Count, right.Count) && shown < 20; i++)
        {
            var a = i < left.Count ? left[i] : null;
            var b = i < right.Count ? right[i] : null;
            if (a == b)
                continue;

            writer.Line($"  {i,3}  - {QueryWriter.OneLine(a ?? "(none)", 70)}");
            writer.Line($"       + {QueryWriter.OneLine(b ?? "(none)", 70)}");
            shown++;
        }

        if (shown == 0)
            writer.Line("  identical");
    }

    private static int Diff(ReportIndex left, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0)
        {
            error.WriteLine("Diff needs two reports: kronikol query diff <old.json> <new.json>");
            return 2;
        }

        ReportIndex right;
        try
        {
            right = ReportScanner.Scan(options.Positional[0]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error.WriteLine($"Could not read {options.Positional[0]}: {exception.Message}");
            return 1;
        }

        writer.Line($"- {Path.GetFileName(left.Path)}  {left.StartTime}  {left.Scenarios.Count} scenarios, {left.Scenarios.Count(s => s.Failed)} failed");
        writer.Line($"+ {Path.GetFileName(right.Path)}  {right.StartTime}  {right.Scenarios.Count} scenarios, {right.Scenarios.Count(s => s.Failed)} failed");
        writer.Line();

        // stableId is the cross-run key: it survives a re-run, and since example values went into the hash
        // it tells one row of a scenario outline from another, which is where per-row matching matters.
        var before = left.Scenarios.ToDictionary(s => s.StableId, s => s, StringComparer.Ordinal);
        var after = right.Scenarios.ToDictionary(s => s.StableId, s => s, StringComparer.Ordinal);

        var broke = new List<string>();
        var fixedUp = new List<string>();
        var slower = new List<string>();

        foreach (var (id, now) in after)
        {
            if (!before.TryGetValue(id, out var then))
            {
                broke.Add($"  new   {now.Address} {QueryWriter.OneLine(now.Name, 70)} [{now.Result}]");
                continue;
            }

            if (then.Failed && !now.Failed)
                fixedUp.Add($"  fixed {now.Address} {QueryWriter.OneLine(now.Name, 70)}");
            else if (!then.Failed && now.Failed)
                broke.Add($"  BROKE {now.Address} {QueryWriter.OneLine(now.Name, 70)}"
                          + (now.ErrorMessage is { } e ? $"\n          {QueryWriter.OneLine(e, 100)}" : ""));

            if (then.DurationSeconds > 0.1 && now.DurationSeconds > then.DurationSeconds * 1.5)
                slower.Add($"  {now.Address} {then.DurationSeconds:0.##}s → {now.DurationSeconds:0.##}s  {QueryWriter.OneLine(now.Name, 60)}");
        }

        var gone = before.Keys.Except(after.Keys).ToArray();

        Section("Broken", broke);
        Section("Fixed", fixedUp);
        Section("Slower", slower);
        if (gone.Length > 0)
        {
            writer.Line($"Gone ({gone.Length}):");
            foreach (var id in gone.Take(10))
                writer.Line($"  {QueryWriter.OneLine(before[id].Name, 80)}");
        }

        if (broke.Count == 0 && fixedUp.Count == 0 && slower.Count == 0 && gone.Length == 0)
            writer.Line("no change in results or timings");

        writer.Footer("matched on stableId · compare s3 s7 for two scenarios in one run");
        return 0;

        void Section(string title, List<string> rows)
        {
            if (rows.Count == 0)
                return;
            writer.Line($"{title} ({rows.Count}):");
            foreach (var row in rows.Take(15))
                writer.Line(row);
            if (rows.Count > 15)
                writer.Line($"  … {rows.Count - 15} more");
            writer.Line();
        }
    }
}
