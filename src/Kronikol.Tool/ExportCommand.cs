using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol export</c>: push NDJSON interaction captures to an OTLP/HTTP collector as
/// OpenTelemetry spans, so the traffic only Kronikol saw (proxy/TCP-tap hops, handler-captured calls
/// with test attribution) appears in Tempo/Jaeger/any collector next to the app's real traces. Returns a
/// process exit code (0 = success, 1 = runtime failure, 2 = usage error) — the <see cref="IngestCommand"/>
/// conventions.
/// </summary>
/// <remarks>
/// Unlike the batch/streaming APIs, the NDJSON path is the one capture path where no redaction has run
/// yet (<c>RequestResponseLogger.Redaction</c> only applies on ingest-replay), so this verb applies
/// <see cref="CaptureRedaction"/> itself — default on, with <c>--no-redact</c>/<c>--redact-header</c>
/// mirroring <c>kronikol ingest</c> exactly.
/// </remarks>
internal static class ExportCommand
{
    private static readonly string[] InteractionPatterns = ["*.ndjson", "*.jsonl"];

    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        var inputs = new List<string>();
        Uri? endpoint = null;
        var headers = new List<(string Name, string Value)>();
        var includeBodies = false;
        int? bodyCap = null;
        var includeSpanSourced = false;
        var perPairTraces = false;
        var redact = true;
        var redactHeaders = new List<string>();
        var gzip = false;
        var dryRun = false;
        string? outFile = null;
        string? tests = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--tests":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    tests = args[i];
                    break;
                case "--otlp":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    if (!Uri.TryCreate(args[i], UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https"))
                    {
                        error.WriteLine($"--otlp expects an absolute http(s) endpoint such as http://localhost:4318/v1/traces, got '{args[i]}'");
                        return 2;
                    }
                    break;
                case "--header":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    var separator = args[i].IndexOf('=');
                    if (separator <= 0)
                    {
                        error.WriteLine($"--header expects name=value, got '{args[i]}'");
                        return 2;
                    }
                    headers.Add((args[i][..separator].Trim(), args[i][(separator + 1)..]));
                    break;
                case "--include-bodies":
                    includeBodies = true;
                    break;
                case "--body-cap":
                    if (++i >= args.Count || !int.TryParse(args[i], out var cap) || cap < 1)
                    {
                        error.WriteLine("--body-cap needs a positive integer (bytes)");
                        return 2;
                    }
                    bodyCap = cap;
                    break;
                case "--include-span-sourced":
                    includeSpanSourced = true;
                    break;
                case "--per-pair-traces":
                    perPairTraces = true;
                    break;
                case "--no-redact":
                    redact = false;
                    break;
                case "--redact-header":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    redactHeaders.Add(args[i]);
                    break;
                case "--gzip":
                    gzip = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--out":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    outFile = args[i];
                    break;
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error.WriteLine($"Unknown option: {arg}");
                        return 2;
                    }
                    inputs.Add(arg);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            error.WriteLine("No inputs given. Specify one or more NDJSON files, directories, or glob patterns of interaction records.");
            PrintUsage(error);
            return 2;
        }

        if (!dryRun && endpoint is null)
        {
            error.WriteLine("No destination: pass --otlp <endpoint> (or --dry-run to write the encoded JSON instead).");
            return 2;
        }

        if (outFile is not null && !dryRun)
        {
            error.WriteLine("--out only applies with --dry-run (a real export POSTs to --otlp).");
            return 2;
        }

        var files = CliInputs.Resolve(inputs, InteractionPatterns, error);
        if (tests is not null)
        {
            var testsFull = Path.GetFullPath(tests);
            files.Remove(testsFull); // a tests file inside an input directory is not an interaction file
            if (!File.Exists(testsFull))
            {
                error.WriteLine($"Tests file not found: {tests}");
                return 1;
            }
            tests = testsFull;
        }

        if (files.Count == 0)
        {
            error.WriteLine("No matching capture files found (*.ndjson / *.jsonl).");
            return 1;
        }

        var malformed = new List<MalformedLine>();
        List<InteractionRecord> records;
        try
        {
            records = NdjsonInteractionReader.ReadFiles(files, malformed);
        }
        catch (IOException ex)
        {
            error.WriteLine("Failed to read a capture file: " + ex.Message);
            return 1;
        }

        ReportMalformed(malformed, error);

        // The NDJSON path is the one capture path where nothing has redacted yet — apply it here,
        // before anything is mapped, exactly as ingest-replay would.
        var redaction = redact ? new CaptureRedaction(CaptureRedaction.DefaultSecretHeaders.Concat(redactHeaders)) : null;
        var logs = new List<RequestResponseLog>(records.Count);
        foreach (var record in records)
        {
            foreach (var log in record.ToLogs())
            {
                var entry = redaction is null ? log : redaction.Apply(log);
                if (entry is not null)
                    logs.Add(entry);
            }
        }

        var options = new OtlpExportOptions
        {
            Endpoint = endpoint,
            IncludeBodies = includeBodies,
            IncludeSpanSourced = includeSpanSourced,
            TraceIdStrategy = perPairTraces ? TraceIdStrategy.PerPair : TraceIdStrategy.PerTest,
            Gzip = gzip,
            Log = error.WriteLine,
        };
        if (bodyCap is not null)
            options.BodyAttributeCapBytes = bodyCap.Value;
        if (tests is not null && ReadVerdicts(tests, error) is { } verdicts)
            options.TestResult = id => verdicts.GetValueOrDefault(id);
        foreach (var (name, value) in headers)
            options.Headers[name] = value;

        if (dryRun)
        {
            var batch = OtlpSpanMapper.MapAll(logs, options, DateTimeOffset.UtcNow);
            var json = OtlpJsonEncoder.Encode(batch.Spans);
            var traces = batch.Spans.Select(s => s.TraceId).Distinct(StringComparer.Ordinal).Count();
            if (outFile is null)
            {
                // The document owns stdout so it can be piped; the counts go to stderr.
                @out.WriteLine(json);
                error.WriteLine(Summary("Encoded", batch.Spans.Count, traces, batch.SkippedRecords, batch.OrphanSpans) + " (dry run; nothing POSTed)");
            }
            else
            {
                if (!Kronikol.Tool.Query.QueryWriter.TryWriteFile(outFile, json, error, "--out",
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    return 1;

                @out.WriteLine(Summary("Wrote", batch.Spans.Count, traces, batch.SkippedRecords, batch.OrphanSpans)
                               + $" to {Path.GetFullPath(outFile)} (dry run; nothing POSTed)");
            }

            return 0;
        }

        using var exporter = new OtlpExporter(options);
        var result = exporter.ExportAsync(logs).GetAwaiter().GetResult();
        @out.WriteLine(Summary("Exported", result.SpansExported, result.TraceCount, result.SkippedRecords, result.OrphanSpans)
                       + $" to {endpoint}");
        if (result.Success)
            return 0;

        error.WriteLine($"Export failed: {result.BatchesFailed} batch(es) ({result.SpansFailed} span(s)) could not be delivered to {endpoint}.");
        return 1;
    }

    /// <summary>Names the lines that could not be parsed, capped so a broken file cannot flood the output.</summary>
    private static void ReportMalformed(List<MalformedLine> malformed, TextWriter error)
    {
        if (malformed.Count == 0)
            return;

        error.WriteLine($"{malformed.Count} malformed line(s) skipped:");
        foreach (var line in malformed.Take(5))
            error.WriteLine($"  {line.Source}:{line.LineNumber}: {line.Message}");
        if (malformed.Count > 5)
            error.WriteLine($"  … and {malformed.Count - 5} more");
    }

    /// <summary>
    /// testId -> verdict from the companion tests NDJSON, read from its <c>end</c> events. The status
    /// words a runner writes are mapped through the same <see cref="FeatureSynthesizer.MapStatus"/> the
    /// ingest uses, so a span attribute and the report it would have produced never disagree - which is
    /// also why an unrecognised word becomes <c>Failed</c> rather than being dropped. Unrecognised words
    /// are named on stderr, because a producer typo would otherwise paint a span red in silence.
    /// </summary>
    private static Dictionary<string, string>? ReadVerdicts(string path, TextWriter error)
    {
        var malformed = new List<MalformedLine>();
        List<TestRunRecord> records;
        try
        {
            records = NdjsonTestRunReader.ReadFile(path, malformed);
        }
        catch (IOException exception)
        {
            error.WriteLine("Failed to read the tests file: " + exception.Message);
            return null;
        }

        ReportMalformed(malformed, error);

        var verdicts = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (!string.Equals(record.Event, TestRunRecord.Events.End, StringComparison.OrdinalIgnoreCase))
                continue;
            if (record.Status is not { Length: > 0 } status)
                continue;

            if (!KnownStatuses.Contains(status.Trim()))
                unknown.Add(status.Trim());
            verdicts[record.TestId] = FeatureSynthesizer.MapStatus(status).ToString();
        }

        if (unknown.Count > 0)
            error.WriteLine($"{unknown.Count} unrecognised status word(s) exported as Failed: {string.Join(", ", unknown.Order())}");

        return verdicts;
    }

    /// <summary>Every word <see cref="FeatureSynthesizer.MapStatus"/> maps deliberately; anything else falls through to Failed.</summary>
    private static readonly HashSet<string> KnownStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "passed", "pass", "ok", "success", "succeeded",
        "failed", "fail", "error", "timedOut", "timed-out", "timeout", "interrupted", "broken",
        "skipped", "skip", "pending", "todo", "ignored", "bypassed", "skippedAfterFailure",
    };

    private static string Summary(string verb, int spans, int traces, int skipped, int orphans) =>
        $"{verb} {spans} span(s) in {traces} trace(s), {skipped} record(s) skipped, {orphans} orphan(s)";

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol export <inputs...> --otlp <endpoint> [options]");
        w.WriteLine();
        w.WriteLine("  Pushes NDJSON interaction captures to an OTLP/HTTP collector as OpenTelemetry spans, so the");
        w.WriteLine("  traffic only Kronikol saw (proxy/TCP-tap hops, handler-captured calls) appears in Tempo/Jaeger");
        w.WriteLine("  next to the app's real traces. One request/response pair becomes one CLIENT span; captured W3C");
        w.WriteLine("  trace ids are preserved, and pairs without one group into one trace per test.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <inputs...>              Files, directories (searched recursively for *.ndjson / *.jsonl), or globs.");
        w.WriteLine("Options:");
        w.WriteLine("  --otlp <endpoint>        The OTLP/HTTP traces endpoint, e.g. http://localhost:4318/v1/traces.");
        w.WriteLine("  --header <name=value>    Header added to every export request (repeatable) — auth tokens etc.");
        w.WriteLine("  --tests <file>           The run's companion tests NDJSON. Its end events give each span a");
        w.WriteLine("                           kronikol.test.result attribute; without it no span claims a verdict.");
        w.WriteLine("  --include-bodies         Export request/response bodies as kronikol.request.body /");
        w.WriteLine("                           kronikol.response.body span attributes (default: off).");
        w.WriteLine("  --body-cap <n>           Cap per body attribute when --include-bodies is on (default: 8192).");
        w.WriteLine("  --include-span-sourced   Also export records captured from the backend's own telemetry (an OTLP");
        w.WriteLine("                           tap); default off — re-exporting them duplicates spans the backend has.");
        w.WriteLine("  --per-pair-traces        Keep each pair's own trace id instead of grouping one test into one trace.");
        w.WriteLine("  --no-redact              Do not redact credential headers before mapping (default: redact).");
        w.WriteLine("  --redact-header <name>   Additional header to redact (repeatable).");
        w.WriteLine("  --gzip                   Gzip the POSTed payload (Content-Encoding: gzip).");
        w.WriteLine("  --dry-run                Write the encoded OTLP/JSON instead of POSTing (to --out, else stdout).");
        w.WriteLine("  --out <file>             Where --dry-run writes the document.");
        w.WriteLine("  -h, --help               Show this help.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol export ./captures --otlp http://localhost:4318/v1/traces --header \"authorization=Bearer t\"");
        w.WriteLine("  kronikol export ./captures --tests ./captures/tests.ndjson --otlp http://localhost:4318/v1/traces");
    }
}
