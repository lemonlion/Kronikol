using System.Text;
using System.Text.Json;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// Structural body diff — the most common debugging move, "this call succeeded in the passing scenario,
/// what was different in mine?", answered by printing only the paths that differ instead of two whole
/// payloads. Works inside one report (<c>diff s3/i47 s7/i47</c>, <c>diff b:a b:b</c>) and across two
/// runs (<c>diff old.json new.json --body s3/i47</c>, matched by stableId).
/// </summary>
internal static partial class QueryCommand
{
    private readonly record struct BodyRef(string Label, string Hash, int Length, string? Content);

    private static int BodyDiff(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count < 2
            || !Address.TryParse(options.Positional[0], out var first)
            || !Address.TryParse(options.Positional[1], out var second))
        {
            error.WriteLine("Body diff takes two addresses: kronikol query diff <report> s3/i47 s7/i47 (or b:hashes).");
            return 2;
        }

        if (first.Kind == AddressKind.Scenario || second.Kind == AddressKind.Scenario)
        {
            // An agent will type exactly this; the error must teach the right verb, not just refuse.
            error.WriteLine($"A scenario address names no body — 'compare {options.Positional[0]} {options.Positional[1]}' is the command for two scenarios.");
            error.WriteLine("diff takes two bodies: s3/i47 s7/i47, or b:hashes ('interactions' prints both on every row).");
            return 2;
        }

        if (!TryResolveBody(index, first, error, out var left) || !TryResolveBody(index, second, error, out var right))
            return 2;

        return EmitBodyDiff(writer, options, left, right);
    }

    private static bool TryResolveBody(ReportIndex index, Address address, TextWriter error, out BodyRef body)
    {
        body = default;
        switch (address.Kind)
        {
            case AddressKind.Body:
                if (!index.Bodies.TryGetValue(address.BodyHash!, out var entry))
                {
                    error.WriteLine($"No body {address.BodyHash} in this report.");
                    return false;
                }
                body = new BodyRef(address.BodyHash!, address.BodyHash!, entry.Length, PayloadReader.Read(index, entry.First));
                return true;

            case AddressKind.Interaction:
                if (index.Scenario(address.Scenario) is not { } scenario)
                {
                    error.WriteLine($"No scenario s{address.Scenario} — the report has {index.Scenarios.Count}.");
                    return false;
                }
                var interaction = scenario.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
                if (interaction is null)
                {
                    error.WriteLine($"No interaction i{address.Interaction} in {scenario.Address} — it has {scenario.Interactions.Count}.");
                    return false;
                }
                if (interaction.BodyHash is not { } hash)
                {
                    error.WriteLine($"{address} carries no body — 'interactions {scenario.Address}' shows which calls do.");
                    return false;
                }
                body = new BodyRef(address.ToString(), hash, interaction.BodyLength, PayloadReader.Read(index, interaction.Body));
                return true;

            default:
                error.WriteLine($"{address} does not name a body — use s3/i47 or b:hash.");
                return false;
        }
    }

    /// <summary>Cross-run: the address is resolved in the old report and matched into the new by stableId — ordinals shift between runs, stableId is the cross-run key the run diff already uses.</summary>
    private static int CrossRunBodyDiff(ReportIndex left, ReportIndex right, string addressText,
        QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!Address.TryParse(addressText, out var address) || address.Kind != AddressKind.Interaction)
        {
            error.WriteLine("--body takes an interaction address like s3/i47, resolved in the old report.");
            return 2;
        }

        if (left.Scenario(address.Scenario) is not { } oldScenario)
        {
            error.WriteLine($"No scenario s{address.Scenario} in {Path.GetFileName(left.Path)} — it has {left.Scenarios.Count}.");
            return 2;
        }

        var match = right.Scenarios.FirstOrDefault(s => s.StableId.Length > 0 && s.StableId == oldScenario.StableId);
        if (match is null)
        {
            error.WriteLine($"No scenario in {Path.GetFileName(right.Path)} with stableId {oldScenario.StableId} ({QueryWriter.OneLine(oldScenario.Name, 60)}).");
            return 2;
        }

        var oldInteraction = oldScenario.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
        if (oldInteraction is null)
        {
            error.WriteLine($"No interaction i{address.Interaction} in {oldScenario.Address} — it has {oldScenario.Interactions.Count}.");
            return 2;
        }
        var newInteraction = match.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
        if (newInteraction is null)
        {
            error.WriteLine($"i{address.Interaction} is out of range — {match.Address} in {Path.GetFileName(right.Path)} has {match.Interactions.Count} interactions.");
            return 2;
        }

        if (oldInteraction.BodyHash is not { } oldHash || newInteraction.BodyHash is not { } newHash)
        {
            error.WriteLine($"{(oldInteraction.BodyHash is null ? oldScenario.Address : match.Address)}/i{address.Interaction} carries no body.");
            return 2;
        }

        var leftRef = new BodyRef($"{Path.GetFileName(left.Path)} {oldScenario.Address}/i{address.Interaction}",
            oldHash, oldInteraction.BodyLength, PayloadReader.Read(left, oldInteraction.Body));
        var rightRef = new BodyRef($"{Path.GetFileName(right.Path)} {match.Address}/i{address.Interaction}",
            newHash, newInteraction.BodyLength, PayloadReader.Read(right, newInteraction.Body));
        return EmitBodyDiff(writer, options, leftRef, rightRef);
    }

    private static int EmitBodyDiff(QueryWriter writer, QueryOptions options, BodyRef left, BodyRef right)
    {
        if (left.Hash == right.Hash)
        {
            // The index already knows, without reading anything.
            if (options.Count)
            {
                writer.Line("0");
                return 0;
            }
            writer.Line($"- {left.Label}  {left.Hash}");
            writer.Line($"+ {right.Label}  {right.Hash}");
            writer.Line();
            writer.Line("byte-identical");
            writer.Footer("");
            return 0;
        }

        if (left.Content is null || right.Content is null)
        {
            writer.Line("A body could not be read back from the report.");
            return 1;
        }

        var rows = DiffBodies(left.Content, right.Content);

        if (options.Count)
        {
            writer.Line(rows.Count.ToString());
            return 0;
        }

        writer.Line($"- {left.Label}  {left.Hash}  {QueryWriter.Size(Encoding.UTF8.GetByteCount(left.Content))}");
        writer.Line($"+ {right.Label}  {right.Hash}  {QueryWriter.Size(Encoding.UTF8.GetByteCount(right.Content))}");
        if (left.Content.Contains("…truncated (", StringComparison.Ordinal) || right.Content.Contains("…truncated (", StringComparison.Ordinal))
            writer.Line("! a body was capped at capture time — the rest was never recorded, so this diff covers what was");
        writer.Line();
        writer.Page(rows, options.Offset, Math.Min(options.Limit, 200), "paths differ", row => writer.Line(row), "");
        return 0;
    }

    private static List<string> DiffBodies(string leftBody, string rightBody)
    {
        JsonDocument leftDocument;
        JsonDocument rightDocument;
        try
        {
            leftDocument = JsonDocument.Parse(leftBody);
        }
        catch (JsonException)
        {
            return DiffLines(leftBody, rightBody);
        }
        try
        {
            rightDocument = JsonDocument.Parse(rightBody);
        }
        catch (JsonException)
        {
            leftDocument.Dispose();
            return DiffLines(leftBody, rightBody);
        }

        using (leftDocument)
        using (rightDocument)
        {
            var rows = new List<string>();
            DiffElements(rows, leftDocument.RootElement, rightDocument.RootElement, "$");
            return rows;
        }
    }

    private static void DiffElements(List<string> rows, JsonElement a, JsonElement b, string path)
    {
        if (a.ValueKind != b.ValueKind)
        {
            if (a.ValueKind is JsonValueKind.True or JsonValueKind.False && b.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                rows.Add($"{path}: {RenderScalar(a)} → {RenderScalar(b)}");
                return;
            }
            rows.Add($"{path}: {DescribeTyped(a)} → {DescribeTyped(b)}");
            return;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in a.EnumerateObject())
                {
                    var childPath = PathEngine.Append(path, property.Name);
                    if (b.TryGetProperty(property.Name, out var other))
                        DiffElements(rows, property.Value, other, childPath);
                    else
                        rows.Add($"{childPath}: {RenderOrShape(property.Value)} → (absent)");
                }
                foreach (var property in b.EnumerateObject())
                    if (!a.TryGetProperty(property.Name, out _))
                        rows.Add($"{PathEngine.Append(path, property.Name)}: (absent) → {RenderOrShape(property.Value)}");
                break;

            case JsonValueKind.Array:
                DiffArrays(rows, a, b, path);
                break;

            default:
                if (!ScalarEquals(a, b))
                    rows.Add($"{path}: {RenderScalar(a)} → {RenderScalar(b)}");
                break;
        }
    }

    private static void DiffArrays(List<string> rows, JsonElement a, JsonElement b, string path)
    {
        var lengthA = a.GetArrayLength();
        var lengthB = b.GetArrayLength();
        var compared = Math.Min(lengthA, lengthB);

        // Index alignment makes one inserted element diff every subsequent index. When most compared
        // rows differ but the two element multisets are mostly shared, one honest row beats a page of
        // misleading ones. Proper LCS alignment is deliberately deferred.
        if (compared > 0)
        {
            var differing = 0;
            for (var i = 0; i < compared; i++)
                if (!JsonElement.DeepEquals(a[i], b[i]))
                    differing++;

            if (differing > 0.6 * compared)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var element in a.EnumerateArray())
                {
                    var key = element.GetRawText();
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
                var shared = 0;
                foreach (var element in b.EnumerateArray())
                {
                    var key = element.GetRawText();
                    if (counts.GetValueOrDefault(key) > 0)
                    {
                        shared++;
                        counts[key]--;
                    }
                }

                if (shared >= compared / 2.0)
                {
                    rows.Add($"{path}: elements shifted/reordered — {lengthA} vs {lengthB}, {shared} identical");
                    return;
                }
            }
        }

        if (lengthA != lengthB)
            rows.Add($"{path}: {lengthA} → {lengthB} elements");
        for (var i = 0; i < compared; i++)
            DiffElements(rows, a[i], b[i], $"{path}[{i}]");
        for (var i = compared; i < lengthA; i++)
            rows.Add($"{path}[{i}]: {RenderOrShape(a[i])} → (absent)");
        for (var i = compared; i < lengthB; i++)
            rows.Add($"{path}[{i}]: (absent) → {RenderOrShape(b[i])}");
    }

    /// <summary>First 20 differing lines of the pretty-printed texts — the fallback when either side is not JSON.</summary>
    private static List<string> DiffLines(string leftBody, string rightBody)
    {
        var left = PayloadReader.Pretty(leftBody).ReplaceLineEndings("\n").Split('\n');
        var right = PayloadReader.Pretty(rightBody).ReplaceLineEndings("\n").Split('\n');
        var rows = new List<string>();
        var shown = 0;

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : null;
            var b = i < right.Length ? right[i] : null;
            if (a == b)
                continue;
            if (shown == 20)
            {
                rows.Add("… more lines differ — --out both bodies and diff the files");
                break;
            }
            rows.Add($"line {i + 1}:  - {QueryWriter.OneLine(a ?? "(none)", 80)}");
            rows.Add($"          + {QueryWriter.OneLine(b ?? "(none)", 80)}");
            shown++;
        }

        return rows;
    }

    private static bool ScalarEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number)
            return a.TryGetDouble(out var x) && b.TryGetDouble(out var y) && x == y;
        if (a.ValueKind == JsonValueKind.String)
            return a.GetString() == b.GetString();
        return a.GetRawText() == b.GetRawText();
    }

    private static string RenderScalar(JsonElement element) =>
        QueryWriter.OneLine(element.GetRawText(), 60);

    private static string RenderOrShape(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => Shape(element),
        JsonValueKind.Array => $"[{element.GetArrayLength()} elements]",
        _ => RenderScalar(element)
    };

    /// <summary>An added or removed subtree is one row with a shape summary, never a dump.</summary>
    private static string Shape(JsonElement element)
    {
        var keys = element.EnumerateObject().Select(p => p.Name).ToList();
        var listed = string.Join(", ", keys.Take(6));
        return keys.Count > 6 ? $"{{{listed}, …}}" : $"{{{listed}}}";
    }

    private static string DescribeTyped(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => $"object {Shape(element)}",
        JsonValueKind.Array => $"array [{element.GetArrayLength()} elements]",
        JsonValueKind.String => $"string {RenderScalar(element)}",
        JsonValueKind.Number => $"number {RenderScalar(element)}",
        JsonValueKind.True or JsonValueKind.False => $"boolean {RenderScalar(element)}",
        _ => "null"
    };
}
