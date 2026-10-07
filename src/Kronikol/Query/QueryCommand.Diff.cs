using System.Text;
using System.Text.Json;

namespace Kronikol.Query;

/// <summary>
/// Structural body diff — the most common debugging move, "this call succeeded in the passing scenario,
/// what was different in mine?", answered by printing only the paths that differ instead of two whole
/// payloads. Works inside one report (<c>diff s3/i47 s7/i47</c>, <c>diff b:a b:b</c>) and across two
/// runs (<c>diff old.json new.json --body s3/i47</c>: the scenario matched by stableId, the call by its
/// service, method and URI, see <see cref="CallPairing"/>).
/// </summary>
public static partial class QueryCommand
{
    private readonly record struct BodyRef(string Label, string Hash, int Length, string? Content);

    /// <summary>
    /// Where a note goes: into the answer, or to stderr under <c>--count</c>, whose stdout is one token by
    /// contract - the rule <see cref="WriteProvenance"/> applies for every other verb.
    /// </summary>
    private static Action<string> NoteSink(QueryOptions options, QueryWriter writer, TextWriter error) =>
        options.Count && !options.Json ? error.WriteLine : writer.Note;

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

        // The header every other verb gets from WriteProvenance, which skips `diff` so that the two-report
        // forms can name the side a note is about. This form holds one report, so it is unprefixed; it used
        // to print none, so a body diff on a report that predates step attribution said nothing about it.
        var note = NoteSink(options, writer, error);
        foreach (var line in ProvenanceNotes(index))
            note("! " + line);

        return EmitBodyDiff(writer, options, error, left, right);
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

    /// <summary>
    /// Cross-run: the address is resolved in the old report; the scenario is matched into the new by
    /// stableId, as the run diff matches it, and the call by what it is - its service, method and URI
    /// (<see cref="CallPairing"/>) - because ordinals shift between runs for calls as they do for scenarios.
    /// A call nothing pairs with is refused with exit 2 and the calls the new scenario does make, never
    /// diffed against whatever sits at its ordinal.
    /// </summary>
    private static int CrossRunBodyDiff(ReportIndex left, ReportIndex right, string addressText,
        QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!Address.TryParse(addressText, out var address) || address.Kind != AddressKind.Interaction)
        {
            error.WriteLine("--body takes an interaction address like s3/i47, resolved in the old report.");
            return 2;
        }

        // Under --baseline both files are usually called TestRunReport.json, and a bare file name labelled
        // the two sides of the body identically.
        var (leftLabel, rightLabel) = DiffLabels(left.Path, right.Path);

        if (left.Scenario(address.Scenario) is not { } oldScenario)
        {
            error.WriteLine($"No scenario s{address.Scenario} in {leftLabel} — it has {left.Scenarios.Count}.");
            return 2;
        }

        // The run diff's refusals first: a pair of runs whose scenarios cannot be matched is refused with its
        // reason here too, rather than with a stableId the other report happens not to hold.
        var before = GroupByStableId(left);
        var after = GroupByStableId(right);
        if (RefuseUnmatchable(left, right, before, after, error) is { } refusal)
            return refusal;

        // Repeated rows and retries share a stableId; the n-th holder in the old report is matched to the
        // n-th in the new. Reports without stableIds share the empty one, which makes this the match by
        // position the run diff makes for them.
        var holders = before[oldScenario.StableId];
        var position = holders.IndexOf(oldScenario);
        var candidates = after.GetValueOrDefault(oldScenario.StableId) ?? [];
        var unidentified = oldScenario.StableId.Length == 0;
        string? scenarioNote = null;
        ScenarioEntry match;
        if (position < candidates.Count)
        {
            match = candidates[position];
        }
        else if (candidates.Count == 0)
        {
            error.WriteLine($"No scenario in {rightLabel} with stableId sid:{oldScenario.StableId} ({QueryWriter.OneLine(oldScenario.Name, 60)}).");
            return 2;
        }
        else if (unidentified)
        {
            // By position there is no first-holder fallback: the first scenario of the other report is
            // another test, not this one run once more.
            error.WriteLine($"No scenario in {rightLabel} at {oldScenario.Address}'s position: neither report has stableIds, so scenarios are matched by position, and {rightLabel} has {candidates.Count}.");
            return 2;
        }
        else
        {
            // The same test, which the new run held fewer times: compared, and said, where it used to be silent.
            match = candidates[0];
            scenarioNote = $"! {oldScenario.Address} is the {Nth(position + 1)} of {holders.Count} scenarios with sid:{oldScenario.StableId} in {leftLabel}; {rightLabel} has {candidates.Count}, compared with its first";
        }

        var oldInteraction = oldScenario.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
        if (oldInteraction is null)
        {
            error.WriteLine($"No interaction i{address.Interaction} in {leftLabel} {oldScenario.Address} — it has {oldScenario.Interactions.Count}.");
            return 2;
        }

        var oldAddress = oldInteraction.Address(oldScenario);
        if (oldInteraction.BodyHash is not { } oldHash)
        {
            error.WriteLine($"{leftLabel} {oldAddress} carries no body — 'interactions {oldScenario.Address}' shows which calls do.");
            return 2;
        }

        var pairing = CallPairing.Pair(oldScenario, oldInteraction, match);
        if (pairing.Partner is not { } newInteraction)
            return RefuseUnpaired(right, leftLabel, rightLabel, oldScenario, oldInteraction, match, pairing, error);

        var newAddress = newInteraction.Address(match);
        if (newInteraction.BodyHash is not { } newHash)
        {
            error.WriteLine($"{rightLabel} {newAddress} carries no body, so there is nothing to compare {leftLabel} {oldAddress} with.");
            return 2;
        }

        // The notes, once the answer is certain: which report each side is, how the scenario and the call
        // were matched where that was not the plain case.
        var note = NoteSink(options, writer, error);
        WriteSideProvenance(left, "old", note);
        WriteSideProvenance(right, "new", note);
        if (unidentified)
            note("! this report has no stableIds (written before 3.0.47) — scenarios matched by position");
        if (scenarioNote is not null)
            note(scenarioNote);
        var keyText = CallPairing.Describe(oldInteraction, pairing.OnShape ? pairing.Shape : pairing.Target);
        if (pairing.OnShape)
            note($"! no call in {rightLabel} {match.Address} has that URI; matched on its shape {pairing.Shape} (the URIs differ in what looks like an id)");
        if (pairing.OldCount > 1 || pairing.NewCount > 1)
            note($"! {keyText} is made {pairing.OldCount}× in {leftLabel} {oldScenario.Address} and {pairing.NewCount}× in {rightLabel} {match.Address}; this is the {Nth(pairing.Position)}, matched in order");

        writer.Data("left", CallSide(left, oldScenario, oldInteraction));
        writer.Data("right", CallSide(right, match, newInteraction));
        writer.Data("pairing", pairing.OnShape
            ? new { on = "shape", position = pairing.Position, old = pairing.OldCount, @new = pairing.NewCount, shape = pairing.Shape }
            : new { on = "uri", position = pairing.Position, old = pairing.OldCount, @new = pairing.NewCount });

        // What call this is, so a reader can check the pair: the URI both sides share, or on the shape both.
        var half = oldInteraction.Type.ToLowerInvariant();
        var call = pairing.OnShape
            ? $"call: {CallPairing.Describe(oldInteraction, pairing.Target)} → {CallPairing.Target(pairing.PartnerCall!)}  ({half})"
            : $"call: {CallPairing.Describe(oldInteraction, pairing.Target)}  ({half})";

        var leftRef = new BodyRef($"{leftLabel} {oldAddress}", oldHash, oldInteraction.BodyLength, PayloadReader.Read(left, oldInteraction.Body));
        var rightRef = new BodyRef($"{rightLabel} {newAddress}", newHash, newInteraction.BodyLength, PayloadReader.Read(right, newInteraction.Body));
        return EmitBodyDiff(writer, options, error, leftRef, rightRef, call);
    }

    /// <summary>One side of a call pairing, as <c>--json</c> names it.</summary>
    private static object CallSide(ReportIndex index, ScenarioEntry scenario, InteractionEntry entry) => new
    {
        report = index.Path,
        address = entry.Address(scenario),
        half = entry.Type.ToLowerInvariant(),
        service = entry.ServiceName,
        method = entry.Method,
        uri = entry.Uri,
        bodyHash = entry.BodyHash,
        bodyLength = entry.BodyLength
    };

    /// <summary>
    /// The refusal of a call nothing in the new scenario pairs with, on stderr (the <c>--json</c> envelope's
    /// <c>message</c> is its first line, its <c>hint</c> the rest): what was looked for, the calls the new
    /// scenario makes to the same service, and the way to both bodies.
    /// </summary>
    private static int RefuseUnpaired(ReportIndex right, string leftLabel, string rightLabel, ScenarioEntry oldScenario,
        InteractionEntry oldInteraction, ScenarioEntry match, CallPairing.Result pairing, TextWriter error)
    {
        var oldAddress = oldInteraction.Address(oldScenario);
        var call = CallPairing.Describe(oldInteraction, pairing.Target);
        var head = $"No call in {rightLabel} {match.Address} matches {leftLabel} {oldAddress}, {call}: ";
        var calls = CallPairing.Describe(oldInteraction, "");
        error.WriteLine(head + pairing.Reason switch
        {
            CallPairing.Refusal.NoResponse =>
                $"the call it pairs with, {pairing.PartnerCall!.Address(match)}, has no response recorded.",
            CallPairing.Refusal.Fewer when pairing.OnShape =>
                $"{match.Address} makes {pairing.NewCount} {calls} calls shaped {pairing.Shape} once those with the same URI are paired; {leftLabel} {oldAddress} is the {Nth(pairing.Position)} of {pairing.OldCount}.",
            CallPairing.Refusal.Fewer =>
                $"{match.Address} makes {pairing.NewCount} {call} calls; {leftLabel} {oldAddress} is the {Nth(pairing.Position)} of {pairing.OldCount}.",
            _ =>
                $"{match.Address} makes no {calls} call to that URI or to one shaped like it."
        });

        // The calls it does make: the same service and method, else the same service, else none.
        var requests = match.Interactions.Where(i => i.Type.Equals("Request", StringComparison.OrdinalIgnoreCase)).ToList();
        var sameMethod = requests.Where(i => i.ServiceName == oldInteraction.ServiceName
            && string.Equals(i.Method ?? "", oldInteraction.Method ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
        var sameService = requests.Where(i => i.ServiceName == oldInteraction.ServiceName).ToList();
        var (listed, noun) = sameMethod.Count > 0 ? (sameMethod, calls) : (sameService, oldInteraction.ServiceName);
        if (listed.Count == 0)
        {
            error.WriteLine($"{match.Address} makes no {oldInteraction.ServiceName} calls.");
        }
        else
        {
            const int shown = 6;
            var rows = string.Join(", ", listed.Take(shown).Select(i => $"{i.Address(match)} {QueryWriter.OneLine(CallPairing.Target(i), 80)}"));
            error.WriteLine($"{match.Address}'s {noun} calls: {rows}" + (listed.Count > shown ? $" ({shown} of {listed.Count})" : ""));
        }

        error.WriteLine($"`{QueryOptions.Shell(["kronikol", "query", "interactions", right.Path, match.Address, "--service", oldInteraction.ServiceName])}` lists them; "
            + "`http <report> <address> --body --out FILE` saves either body");
        return 2;
    }

    /// <summary>1st, 2nd, 3rd, 4th, 11th, 21st.</summary>
    private static string Nth(int n) =>
        n.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private static int EmitBodyDiff(QueryWriter writer, QueryOptions options, TextWriter error,
        BodyRef left, BodyRef right, string? call = null)
    {
        if (left.Hash == right.Hash)
        {
            // The index already knows, without reading anything.
            if (options.Count)
            {
                writer.Count(0);
                return 0;
            }
            writer.Line($"- {left.Label}  {left.Hash}");
            writer.Line($"+ {right.Label}  {right.Hash}");
            if (call is not null)
                writer.Line(call);
            writer.Line();
            writer.Line("byte-identical");
            // No rows to page, so `items` stays empty and this is what says the empty array means
            // "identical" rather than "nothing was compared".
            writer.Data("byteIdentical", true);
            writer.Footer("");
            return 0;
        }

        if (left.Content is null || right.Content is null)
        {
            // On stderr, not through the writer: nothing is flushed on a non-zero exit, so this message
            // was written into a buffer that was then thrown away - the failure reported nothing at all.
            error.WriteLine("A body could not be read back from the report.");
            return 1;
        }

        var rows = DiffBodies(left.Content, right.Content);

        if (options.Count)
        {
            writer.Count(rows.Count);
            return 0;
        }

        writer.Line($"- {left.Label}  {left.Hash}  {QueryWriter.Size(Encoding.UTF8.GetByteCount(left.Content))}");
        writer.Line($"+ {right.Label}  {right.Hash}  {QueryWriter.Size(Encoding.UTF8.GetByteCount(right.Content))}");
        if (call is not null)
            writer.Line(call);
        if (left.Content.Contains("…truncated (", StringComparison.Ordinal) || right.Content.Contains("…truncated (", StringComparison.Ordinal))
            writer.Note("! a body was capped at capture time — the rest was never recorded, so this diff covers what was");
        writer.Line();
        // No flags to repeat: a body diff is addressed positionally, so the text footer's `next` is
        // appended to the original command, addresses and all. The JSON argv rebuilds them from the
        // envelope, which is why it does not need them here.
        writer.Page(rows, options.Offset, options.PageSize(200, writer, "paths"), "paths differ",
            row => { foreach (var line in row.Text.Split('\n')) writer.Line(line); }, [],
            row => new { kind = row.Kind, path = row.Path, before = row.Before, after = row.After });
        return 0;
    }

    /// <summary>
    /// One difference between two bodies. A record rather than the pre-rendered string these used to
    /// be: a string is the one thing an <c>items</c> array cannot make structure out of.
    /// <paramref name="Text"/> carries the rendered form - one line for a JSON path, two for the
    /// line-by-line fallback - so the text output stays exactly what it was.
    /// </summary>
    private sealed record DiffRow(string Kind, string Path, string? Before, string? After, string Text);

    private static List<DiffRow> DiffBodies(string leftBody, string rightBody)
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
            var rows = new List<DiffRow>();
            DiffElements(rows, leftDocument.RootElement, rightDocument.RootElement, "$");
            return rows;
        }
    }

    private static void DiffElements(List<DiffRow> rows, JsonElement a, JsonElement b, string path)
    {
        if (a.ValueKind != b.ValueKind)
        {
            if (a.ValueKind is JsonValueKind.True or JsonValueKind.False && b.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                rows.Add(Changed(path, RenderScalar(a), RenderScalar(b)));
                return;
            }
            rows.Add(new DiffRow("retyped", path, DescribeTyped(a), DescribeTyped(b),
                $"{path}: {DescribeTyped(a)} → {DescribeTyped(b)}"));
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
                        rows.Add(Removed(childPath, RenderOrShape(property.Value)));
                }
                foreach (var property in b.EnumerateObject())
                    if (!a.TryGetProperty(property.Name, out _))
                        rows.Add(Added(PathEngine.Append(path, property.Name), RenderOrShape(property.Value)));
                break;

            case JsonValueKind.Array:
                DiffArrays(rows, a, b, path);
                break;

            default:
                if (!ScalarEquals(a, b))
                    rows.Add(Changed(path, RenderScalar(a), RenderScalar(b)));
                break;
        }
    }

    private static void DiffArrays(List<DiffRow> rows, JsonElement a, JsonElement b, string path)
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
                    rows.Add(new DiffRow("reordered", path, $"{lengthA} elements", $"{lengthB} elements",
                        $"{path}: elements shifted/reordered — {lengthA} vs {lengthB}, {shared} identical"));
                    return;
                }
            }
        }

        if (lengthA != lengthB)
            rows.Add(new DiffRow("length", path, $"{lengthA} elements", $"{lengthB} elements",
                $"{path}: {lengthA} → {lengthB} elements"));
        for (var i = 0; i < compared; i++)
            DiffElements(rows, a[i], b[i], $"{path}[{i}]");
        for (var i = compared; i < lengthA; i++)
            rows.Add(Removed($"{path}[{i}]", RenderOrShape(a[i])));
        for (var i = compared; i < lengthB; i++)
            rows.Add(Added($"{path}[{i}]", RenderOrShape(b[i])));
    }

    /// <summary>First 20 differing lines of the pretty-printed texts — the fallback when either side is not JSON.</summary>
    private static List<DiffRow> DiffLines(string leftBody, string rightBody)
    {
        var left = PayloadReader.Pretty(leftBody).ReplaceLineEndings("\n").Split('\n');
        var right = PayloadReader.Pretty(rightBody).ReplaceLineEndings("\n").Split('\n');
        var rows = new List<DiffRow>();
        var shown = 0;

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : null;
            var b = i < right.Length ? right[i] : null;
            if (a == b)
                continue;
            if (shown == 20)
            {
                rows.Add(new DiffRow("elided", "", null, null,
                    "… more lines differ — --out both bodies and diff the files"));
                break;
            }
            // ONE record, two rendered lines. As two rows the count double-counted every difference,
            // so the footer said "40 of 40" about twenty of them.
            rows.Add(new DiffRow("line", $"line {i + 1}", a, b,
                $"line {i + 1}:  - {QueryWriter.OneLine(a ?? "(none)", 80)}\n"
                + $"          + {QueryWriter.OneLine(b ?? "(none)", 80)}"));
            shown++;
        }

        return rows;
    }

    private static DiffRow Changed(string path, string before, string after) =>
        new("changed", path, before, after, $"{path}: {before} → {after}");

    private static DiffRow Removed(string path, string before) =>
        new("removed", path, before, null, $"{path}: {before} → (absent)");

    private static DiffRow Added(string path, string after) =>
        new("added", path, null, after, $"{path}: (absent) → {after}");

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
