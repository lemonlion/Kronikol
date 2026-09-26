using System.Globalization;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// <c>trace</c> — follows a W3C trace id (exported on every interaction since 3.0.47) across the whole
/// run: the chain in chronological order with offsets, and the one smell nothing else in the tool can
/// see — a trace id that leaks across scenarios, the classic flaky-test signature.
/// </summary>
internal static partial class QueryCommand
{
    private static int Trace(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0)
        {
            error.WriteLine("Which trace? kronikol query trace <report> <trace-id | prefix ≥8 hex | s3/i47>");
            return 2;
        }

        // Under --count stdout is the one token --count documents, so what is said around the rows goes to
        // stderr, as history's notes do.
        Action<string> note = options.Count && !options.Json ? error.WriteLine : writer.Note;

        var all = AllInteractions(index).ToList();
        var distinctIds = all.Select(t => t.Request.ActivityTraceId)
            .Where(id => id is { Length: > 0 })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string traceId;
        var argument = options.Positional[0];

        if (Address.TryParse(argument, out var address) && address.Kind == AddressKind.Interaction)
        {
            if (index.Scenario(address.Scenario) is not { } scenario)
            {
                error.WriteLine($"No scenario s{address.Scenario} — the report has {index.Scenarios.Count}.");
                return 2;
            }
            var interaction = scenario.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
            if (interaction is null)
            {
                error.WriteLine($"No interaction i{address.Interaction} in {scenario.Address} — it has {scenario.Interactions.Count}.");
                return 2;
            }
            if (interaction.ActivityTraceId is not { Length: > 0 } id)
            {
                error.WriteLine($"{address} carries no W3C trace id — the report predates trace export, or the call was not traced. Re-run the suite on a current Kronikol to get trace ids.");
                return 2;
            }
            traceId = id;
        }
        else
        {
            // `trace` heads its own answer `trace c90a1912… — 2 calls across 1 scenario`, and that header
            // pasted back was refused as "not a trace id": the ellipsis is the tool's punctuation, not
            // part of the id. Three dots too, because a terminal that cannot render U+2026 shows those.
            var prefix = argument.TrimEnd('…', '.', ' ').ToLowerInvariant();
            if (prefix.Length < 8 || !prefix.All(Uri.IsHexDigit))
            {
                error.WriteLine($"Not a trace id or address: {argument} — pass a W3C trace id (or an unambiguous prefix of at least 8 hex chars), a span id, or an interaction address like s3/i47.");
                return 2;
            }

            var candidates = distinctIds.Where(id => id!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            // A span id is the second identifier the tool prints - `http` puts it on the same line as the
            // trace id - and until now nothing in the tool accepted one. It is sixteen hex, so it passes
            // the shape test above and then matches no trace prefix: the refusal was printed one line
            // below the place the id had come from. Resolving it to its trace is the only route from a
            // span to anything at all, and the note says that is what happened.
            if (candidates.Count == 0 && prefix.Length == 16
                && all.FirstOrDefault(t => string.Equals(t.Request.ActivitySpanId, prefix, StringComparison.OrdinalIgnoreCase))
                    is { Request.ActivityTraceId: { Length: > 0 } spanTrace })
            {
                note($"! {argument} is a span id, not a trace id — showing the trace that span belongs to");
                candidates = [spanTrace];
            }

            switch (candidates.Count)
            {
                case 0:
                    error.WriteLine($"No trace {argument} — the report holds {distinctIds.Count} distinct trace id{(distinctIds.Count == 1 ? "" : "s")}."
                                    + (distinctIds.Count == 0 ? " Re-run the suite on a current Kronikol to get trace ids." : ""));
                    return 2;
                case 1:
                    traceId = candidates[0]!;
                    break;
                default:
                    error.WriteLine($"{argument} is ambiguous — candidates:");
                    foreach (var candidate in candidates)
                        error.WriteLine("  " + candidate);
                    return 2;
            }
        }

        var chain = all
            .Where(t => string.Equals(t.Request.ActivityTraceId, traceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var scenarios = chain.Select(t => t.Scenario).Distinct().ToList();
        var leak = scenarios.Count > 1
            ? $"! spans {scenarios.Count} scenarios ({string.Join(", ", scenarios.Select(s => s.Address))}) — shared state or fixture leakage"
            : null;

        // `--count` was declared for trace and never read, so the whole trace was printed where one number
        // was documented: the calls on the trace, the number its header states.
        if (options.Count)
        {
            writer.Count(chain.Count);
            if (leak is not null)
                note(leak);
            return 0;
        }

        // Chronology needs every timestamp; when one is absent or unparseable the whole trace falls back
        // to file order, with a line saying so — never a silent mix of two orderings.
        var timestamps = chain.Select(t => ParseTimestamp(t.Request.Timestamp)).ToList();
        var fileOrder = timestamps.Any(t => t is null);
        List<((ScenarioEntry Scenario, InteractionEntry Request, InteractionEntry? Response) Row, DateTimeOffset? At)> ordered =
            chain.Zip(timestamps, (row, at) => (row, at)).ToList();
        if (!fileOrder)
            ordered = ordered.OrderBy(pair => pair.At!.Value).ToList();

        writer.Line($"trace {traceId[..Math.Min(8, traceId.Length)]}… — {chain.Count} call{(chain.Count == 1 ? "" : "s")} across {scenarios.Count} scenario{(scenarios.Count == 1 ? "" : "s")}");
        if (fileOrder)
            writer.Note("! a timestamp was absent or unparseable — rows are in file order, not chronological");

        var first = ordered.Count > 0 ? ordered[0].At : null;
        foreach (var ((scenario, request, response), at) in ordered)
        {
            var offset = !fileOrder && at is { } stamp && first is { } start
                ? $"+{(stamp - start).TotalMilliseconds:0} ms"
                : "";
            var span = request.ActivitySpanId is { Length: > 0 } spanId
                ? $"   span {spanId[..Math.Min(8, spanId.Length)]}"
                : "";
            writer.Line($"  {offset,-9} {request.Address(scenario),-9} {request.ServiceName,-12} {QueryWriter.OneLine(request.Summary(), 50),-50} {StatusOf(response).Text ?? "",-6}{span}");
        }

        if (leak is not null)
            writer.Note(leak);

        writer.Footer("parent span ids are not captured — this is the chronology of the trace, not its tree");
        return 0;
    }

    private static DateTimeOffset? ParseTimestamp(string? timestamp) =>
        timestamp is { Length: > 0 }
        && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
