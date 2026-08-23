using System.Text;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// The commands that reach past the index into the file. A payload is the critical thing when debugging —
/// it is where a wrong number actually comes from — so none of this hides it. What it does is refuse to
/// print one that was not asked for by name, offer the cheap views first (keys, a path, a line range), and
/// hand the whole thing to a file when it is big enough that reading it would cost more than it is worth.
/// </summary>
internal static partial class QueryCommand
{
    private static int Interactions(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario))
            return 2;

        var matches = scenario.Interactions
            .Where(i => i.Type.Equals("Request", StringComparison.OrdinalIgnoreCase))
            .Where(i => Matches(scenario, i, options))
            .ToList();

        if (options.Count)
        {
            writer.Line(matches.Count.ToString());
            return 0;
        }

        if (matches.Count == 0)
        {
            writer.Line("nothing matched");
            writer.Footer($"{scenario.Interactions.Count(i => i.Type == "Request")} calls in {scenario.Address} · drop a filter, or try services");
            return 0;
        }

        if (options.Group)
        {
            var groups = Collapse(scenario, matches);
            writer.Page(groups, options.Offset, Math.Min(options.Limit, 200), "groups",
                group => writer.Line(group), options.RerunPrefix());
            return 0;
        }

        writer.Page(matches, options.Offset, Math.Min(options.Limit, 120), "calls", interaction =>
        {
            var response = FindResponse(scenario, interaction);
            var payload = interaction.BodyHash is { } hash
                ? $"  body {QueryWriter.Size(interaction.BodyLength)} {hash}"
                : "";
            var responsePayload = response?.BodyHash is { } responseHash
                ? $"  → {QueryWriter.Size(response.BodyLength)} {responseHash}"
                : "";
            writer.Line($"{interaction.Address(scenario),-9} {interaction.ServiceName,-16} {QueryWriter.OneLine(interaction.Summary(), 62),-62} "
                        + $"{response?.StatusCode ?? "",-6} {QueryWriter.Duration(interaction.DurationMs ?? response?.DurationMs),8}{payload}{responsePayload}");
        }, options.RerunPrefix());

        return 0;
    }

    private static bool Matches(ScenarioEntry scenario, InteractionEntry interaction, QueryOptions options)
    {
        if (options.Service is { } service && !interaction.ServiceName.Contains(service, StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.Method is { } method && !string.Equals(interaction.Method, method, StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.Step is { } step && interaction.StepPath != step)
            return false;
        if (options.Grep is { } grep && !interaction.Uri.Contains(grep, StringComparison.OrdinalIgnoreCase))
            return false;

        if (options.Status is { } status)
        {
            var actual = FindResponse(scenario, interaction)?.StatusCode ?? "";
            if (status.EndsWith("xx", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(actual, out var numeric) || numeric / 100 != status[0] - '0')
                    return false;
            }
            else if (!actual.Equals(status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Folds runs of identical calls into one row. A hundred and twenty calls to the same cache key are one
    /// fact, and printing them separately is the difference between an answer that fits and one that does not.
    /// </summary>
    private static List<string> Collapse(ScenarioEntry scenario, List<InteractionEntry> matches)
    {
        var rows = new List<string>();
        var i = 0;
        while (i < matches.Count)
        {
            var first = matches[i];
            var key = $"{first.ServiceName}|{first.Method}|{first.Uri}|{first.BodyHash}";
            var last = i;
            while (last + 1 < matches.Count
                   && $"{matches[last + 1].ServiceName}|{matches[last + 1].Method}|{matches[last + 1].Uri}|{matches[last + 1].BodyHash}" == key)
                last++;

            var count = last - i + 1;
            var address = count == 1 ? first.Address(scenario) : $"{scenario.Address}/i{first.Ordinal}-i{matches[last].Ordinal}";
            var body = first.BodyHash is { } hash ? $"  {hash} {QueryWriter.Size(first.BodyLength)}" : "";
            rows.Add($"{address,-14} {first.ServiceName,-16} {QueryWriter.OneLine(first.Summary(), 62),-62}"
                     + (count > 1 ? $"  ×{count}" : "") + body);
            i = last + 1;
        }

        return rows;
    }

    private static int Http(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0
            || !Address.TryParse(options.Positional[0], out var address)
            || address.Kind != AddressKind.Interaction)
        {
            error.WriteLine("Which interaction? Pass an address like s3/i47 — 'interactions s3' lists them.");
            return 2;
        }

        if (index.Scenario(address.Scenario) is not { } scenario)
        {
            error.WriteLine($"No scenario s{address.Scenario}.");
            return 2;
        }

        var interaction = scenario.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
        if (interaction is null)
        {
            error.WriteLine($"No interaction i{address.Interaction} in {scenario.Address} — it has {scenario.Interactions.Count}.");
            return 2;
        }

        writer.Line($"{address}  {interaction.Type}  {interaction.CallerName} → {interaction.ServiceName}");
        writer.Line($"{interaction.Method} {interaction.Uri}");
        if (interaction.StatusCode is { } status)
            writer.Line("status " + status);
        if (interaction.DurationMs is { } ms)
            writer.Line("took " + QueryWriter.Duration(ms));
        if (interaction.StepPath is { } step)
            writer.Line($"in step {scenario.Address}/{step}");
        if (interaction.ActivityTraceId is { } trace)
            writer.Line($"trace {trace}" + (interaction.ActivitySpanId is { } span ? $" span {span}" : "") + "   (W3C — matches your OTel traces and app logs)");
        foreach (var (name, value) in new[]
                 {
                     ("phase", interaction.Phase), ("kind", interaction.MetaType),
                     ("category", interaction.DependencyCategory), ("captured by", interaction.CapturedBy)
                 })
            if (value is { Length: > 0 })
                writer.Line($"{name} {value}");

        if (options.Headers)
        {
            var headers = PayloadReader.Headers(index, interaction);
            writer.Line();
            foreach (var (key, value) in headers)
                writer.Line($"  {key}: {QueryWriter.OneLine(value, 120)}");
            if (headers.Count == 0)
                writer.Line("  (no headers)");
        }
        else if (interaction.HeaderCount > 0)
        {
            writer.Line($"headers: {interaction.HeaderCount} · --headers");
        }

        if (interaction.BodyHash is null)
        {
            writer.Line("body: none");
            writer.Footer("");
            return 0;
        }

        if (!options.Body && !options.Keys && options.Path is null && options.LineRange is null && options.Out is null)
        {
            var others = index.Bodies.TryGetValue(interaction.BodyHash, out var entry) ? entry.Occurrences.Count : 1;
            writer.Line($"body: {QueryWriter.Size(interaction.BodyLength)} · {interaction.BodyHash}"
                        + (others > 1 ? $" (×{others} in this report)" : ""));
            writer.Footer($"--keys to see its shape · --path $.x to pull one value · --body for all of it · --out F to save it");
            return 0;
        }

        return EmitPayload(index, options, writer, error, PayloadReader.Read(index, interaction.Body), interaction.BodyHash);
    }

    private static int Body(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0 || !options.Positional[0].StartsWith("b:", StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine("Which body? Pass a content address like b:4bdea521 — listings print them beside each call.");
            return 2;
        }

        var hash = options.Positional[0].ToLowerInvariant();
        if (!index.Bodies.TryGetValue(hash, out var entry))
        {
            error.WriteLine($"No body {hash} in this report.");
            return 2;
        }

        writer.Line($"{hash}  {QueryWriter.Size(entry.Length)}  at {entry.Occurrences.Count} address(es)");
        foreach (var occurrence in entry.Occurrences.Take(12))
            writer.Line("  " + occurrence);
        if (entry.Occurrences.Count > 12)
            writer.Line($"  … {entry.Occurrences.Count - 12} more");
        writer.Line();

        return EmitPayload(index, options, writer, error, PayloadReader.Read(index, entry.First), hash);
    }

    /// <summary>
    /// The one place a payload is rendered, so every route to one offers the same four cheap views and the
    /// same escape to a file.
    /// </summary>
    private static int EmitPayload(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error, string? body, string hash)
    {
        if (body is null)
        {
            error.WriteLine("The body could not be read back from the report.");
            return 1;
        }

        if (body.Contains("…truncated (", StringComparison.Ordinal))
            writer.Line("! this body was capped at capture time — the rest was never recorded");

        if (options.Out is { } path)
        {
            var text = PayloadReader.Pretty(body);
            File.WriteAllText(path, text);
            writer.Line($"wrote {QueryWriter.Size(Encoding.UTF8.GetByteCount(text))} → {Path.GetFullPath(path)}");
            writer.Footer("grep the file — reading it back through here would cost the tokens this just saved");
            return 0;
        }

        if (options.Keys)
        {
            foreach (var line in PayloadReader.Keys(body))
                writer.Line(line);
            writer.Footer($"--path $.<one of these> for a value · --body for all {QueryWriter.Size(Encoding.UTF8.GetByteCount(body))}");
            return 0;
        }

        if (options.Path is { } jsonPath)
        {
            var value = PayloadReader.Path(body, jsonPath);
            if (value is null)
            {
                writer.Line($"{jsonPath} is not in this body");
                writer.Footer("--keys to see what is");
                return 0;
            }

            writer.Line(value);
            writer.Footer("");
            return 0;
        }

        var pretty = PayloadReader.Pretty(body);

        if (options.LineRange is { } range)
        {
            writer.Line(PayloadReader.Lines(pretty, range.From, range.To).TrimEnd('\n'));
            writer.Footer($"{pretty.ReplaceLineEndings("\n").Split('\n').Length} lines total");
            return 0;
        }

        var size = Encoding.UTF8.GetByteCount(pretty);
        if (options.MaxBytes > 0 && size > options.MaxBytes)
        {
            writer.Line($"body {hash} is {QueryWriter.Size(size)} — too big to print under the {options.MaxBytes}-byte budget.");
            writer.Line("Pick one:");
            writer.Line("  --keys              its shape, a line per field");
            writer.Line("  --path $.a.b        one value");
            writer.Line("  --lines 1-40        a window");
            writer.Line("  --out body.json     save it, then grep the file");
            writer.Footer("--max-bytes 0 prints it anyway");
            return 0;
        }

        writer.Line(pretty);
        writer.Footer("");
        return 0;
    }

    private static int Note(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0 || !Address.TryParse(options.Positional[0], out var address)
                                          || address.Kind is not (AddressKind.Diagram or AddressKind.Note))
        {
            error.WriteLine("Which note? Pass s3/d0 to list a diagram's notes, or s3/d0/n12 for one.");
            return 2;
        }

        if (!TryDiagram(index, address, error, out var scenario, out var diagram))
            return 2;

        var notes = PayloadReader.Notes(diagram);

        if (address.Kind == AddressKind.Diagram)
        {
            writer.Line($"{scenario.Address}/d{address.Diagram}  {notes.Count} notes");
            writer.Line();
            foreach (var (i, text) in notes)
                writer.Line($"n{i,-4} {QueryWriter.Size(Encoding.UTF8.GetByteCount(text)),8}  {QueryWriter.OneLine(text, 90)}");
            writer.Footer($"note {scenario.Address}/d{address.Diagram}/nN for one in full");
            return 0;
        }

        var note = notes.FirstOrDefault(n => n.Index == address.Note);
        if (note.Text is null)
        {
            error.WriteLine($"No note n{address.Note} — that diagram has {notes.Count}.");
            return 2;
        }

        if (options.Out is { } path)
        {
            File.WriteAllText(path, note.Text);
            writer.Line($"wrote {QueryWriter.Size(Encoding.UTF8.GetByteCount(note.Text))} → {Path.GetFullPath(path)}");
            writer.Footer("");
            return 0;
        }

        writer.Line(note.Text);
        writer.Footer("this is the rendered note, not the captured content — they differ under focus fields, phase variants and formatting processors");
        return 0;
    }

    private static int Diagram(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0 || !Address.TryParse(options.Positional[0], out var address)
                                          || address.Kind != AddressKind.Diagram)
        {
            error.WriteLine("Which diagram? Pass s3/d0.");
            return 2;
        }

        if (!TryDiagram(index, address, error, out var scenario, out var diagram))
            return 2;

        var size = Encoding.UTF8.GetByteCount(diagram);

        if (options.Out is null)
        {
            // A single diagram has been measured at 663 KB — around 166,000 tokens, more than most context
            // windows hold. It is available, but never by accident.
            error.WriteLine($"{scenario.Address}/d{address.Diagram} is {QueryWriter.Size(size)} of PlantUML — printing it would fill a context window.");
            error.WriteLine($"Pass --out FILE to save it, or use 'flow {scenario.Address}' for the same story in a couple of kilobytes.");
            return 2;
        }

        File.WriteAllText(options.Out, diagram);
        writer.Line($"wrote {QueryWriter.Size(size)} → {Path.GetFullPath(options.Out)}");
        writer.Footer($"flow {scenario.Address} says the same thing in a fraction of the bytes");
        return 0;
    }

    private static bool TryDiagram(ReportIndex index, Address address, TextWriter error, out ScenarioEntry scenario, out string diagram)
    {
        scenario = null!;
        diagram = "";

        if (index.Scenario(address.Scenario) is not { } found)
        {
            error.WriteLine($"No scenario s{address.Scenario}.");
            return false;
        }

        scenario = found;
        if (address.Diagram >= scenario.Diagrams.Count)
        {
            error.WriteLine($"No diagram d{address.Diagram} in {scenario.Address} — it has {scenario.Diagrams.Count}.");
            return false;
        }

        diagram = PayloadReader.Read(index, scenario.Diagrams[address.Diagram]) ?? "";
        return true;
    }
}
