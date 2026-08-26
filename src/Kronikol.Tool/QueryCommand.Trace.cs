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
            var prefix = argument.ToLowerInvariant();
            if (prefix.Length < 8 || !prefix.All(Uri.IsHexDigit))
            {
                error.WriteLine($"Not a trace id or address: {argument} — pass a W3C trace id (or an unambiguous prefix of at least 8 hex chars), or an interaction address like s3/i47.");
                return 2;
            }

            var candidates = distinctIds.Where(id => id!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
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

        // Chronology needs every timestamp; when one is absent or unparseable the whole trace falls back
        // to file order, with a line saying so — never a silent mix of two orderings.
        var timestamps = chain.Select(t => ParseTimestamp(t.Request.Timestamp)).ToList();
        var fileOrder = timestamps.Any(t => t is null);
        List<((ScenarioEntry Scenario, InteractionEntry Request, InteractionEntry? Response) Row, DateTimeOffset? At)> ordered =
            chain.Zip(timestamps, (row, at) => (row, at)).ToList();
        if (!fileOrder)
            ordered = ordered.OrderBy(pair => pair.At!.Value).ToList();

        var scenarios = chain.Select(t => t.Scenario).Distinct().ToList();
        writer.Line($"trace {traceId[..Math.Min(8, traceId.Length)]}… — {chain.Count} call{(chain.Count == 1 ? "" : "s")} across {scenarios.Count} scenario{(scenarios.Count == 1 ? "" : "s")}");
        if (fileOrder)
            writer.Line("! a timestamp was absent or unparseable — rows are in file order, not chronological");

        var first = ordered.Count > 0 ? ordered[0].At : null;
        foreach (var ((scenario, request, response), at) in ordered)
        {
            var offset = !fileOrder && at is { } stamp && first is { } start
                ? $"+{(stamp - start).TotalMilliseconds:0} ms"
                : "";
            var span = request.ActivitySpanId is { Length: > 0 } spanId
                ? $"   span {spanId[..Math.Min(8, spanId.Length)]}"
                : "";
            writer.Line($"  {offset,-9} {request.Address(scenario),-9} {request.ServiceName,-12} {QueryWriter.OneLine(request.Summary(), 50),-50} {response?.StatusCode ?? "",-6}{span}");
        }

        if (scenarios.Count > 1)
            writer.Line($"! spans {scenarios.Count} scenarios ({string.Join(", ", scenarios.Select(s => s.Address))}) — shared state or fixture leakage");

        writer.Footer("parent span ids are not captured — this is the chronology of the trace, not its tree");
        return 0;
    }

    private static DateTimeOffset? ParseTimestamp(string? timestamp) =>
        timestamp is { Length: > 0 }
        && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
