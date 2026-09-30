using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// TEMPORARY probe for plans/INGEST_FIDELITY_PLAN.md (kept in its harness, deleted after the run): reads the
/// OTLP/JSON line the Node spike wrote (@opentelemetry/otlp-transformer's JsonTraceSerializer) with the
/// reader Kronikol already ships, and says what it keeps and what the tail mapper would draw from it.
/// </summary>
public class IngestFidelitySpanProbeTests
{
    [Fact]
    public void P4_the_node_spike_spans_through_the_existing_reader()
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "plans"))) root = Path.GetDirectoryName(root)!;
        var file = Path.Combine(root, "plans", "INGEST_FIDELITY_PLAN.harness", "otel-jest", "results", "v2-spans.otlp.jsonl");
        var sb = new StringBuilder();
        var spans = File.ReadLines(file).Where(l => l.Length > 0)
            .SelectMany(l => OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(l))).ToList();
        sb.AppendLine($"P4 spans read: {spans.Count} from {Path.GetFileName(file)}");
        foreach (var s in spans.OrderBy(s => s.StartTimeUnixNano))
            sb.AppendLine($"  {s.TraceId[..8]} {s.SpanId} parent={(s.ParentSpanId ?? "-")} kind={s.Kind} scope={s.ScopeName} name=\"{(s.Name.Length > 60 ? s.Name[..60] + "…" : s.Name)}\" ms={s.DurationMs:0.###} test={(s.Attributes.TryGetValue("kronikol.test.id", out var t) ? t : "-")} service={s.ServiceName}");
        var mapped = SpanToInteractionMapper.MapAll(spans, new OtlpTapOptions());
        sb.AppendLine($"P4 the tail mapper would draw {mapped.Count} arrow(s) from them: " + string.Join("; ", mapped.Select(m => $"{m.Method} {m.Uri} ({m.CallerName} -> {m.ServiceName})")));
        var output = Environment.GetEnvironmentVariable("KRONIKOL_PROBE_OUT") ?? Path.Combine(Path.GetTempPath(), "fidelity-probe.txt");
        File.AppendAllText(output, sb.ToString());
    }
}
