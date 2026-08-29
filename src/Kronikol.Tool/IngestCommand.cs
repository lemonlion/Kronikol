using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol ingest</c>: replay NDJSON interaction captures (and an optional tests file)
/// into a full Kronikol report. The cross-language on-ramp — any capturer that writes
/// <see cref="InteractionRecord"/> lines can produce <c>TestRunReport.html</c>. Returns a process exit
/// code (0 = success, 1 = runtime failure, 2 = usage error).
/// </summary>
internal static class IngestCommand
{
    private static readonly string[] InteractionPatterns = ["*.ndjson", "*.jsonl"];

    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        var inputs = new List<string>();
        string? tests = null;
        string output = Path.Combine(Directory.GetCurrentDirectory(), "Reports");
        string? title = null;
        var render = PlantUmlRendering.BrowserJs;
        var collapse = true;
        var collapseThreshold = 2;
        int? maxArrows = null;
        int? browserRenderWorkers = null;
        var notePayloadFormat = Kronikol.Reports.NotePayloadFormat.Json;
        var componentDiagram = true;
        var redact = true;
        var redactHeaders = new List<string>();
        var featureName = "Ingested";
        var allowEmpty = false;
        var pairResponses = true;
        var mergeDuplicates = false;
        string? foldUnknown = null;
        var cucumberMessages = new List<string>();
        var includeHooks = false;
        var strictParsing = false;
        var capitalise = true;
        var attributeByWindow = false;
        string? windowFallbackId = null;
        var runWindow = false;
        DateTimeOffset? runStart = null;
        DateTimeOffset? runEnd = null;
        var phaseFromSteps = false;
        string? attachmentsBase = null;
        var cleanAttachments = false;
        var hostDiagnostics = new List<DiagnosticEntry>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--tests":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    tests = args[i];
                    break;
                case "-o" or "--output":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    output = args[i];
                    break;
                case "-t" or "--title":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    title = args[i];
                    break;
                case "--render":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    if (!TryParseRender(args[i], out render))
                    {
                        error.WriteLine($"Unknown render mode: {args[i]} (expected browserjs|nodejs|local|server)");
                        return 2;
                    }
                    break;
                case "--feature":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    featureName = args[i];
                    break;
                case "--collapse":
                    collapse = true;
                    break;
                case "--no-collapse":
                    collapse = false;
                    break;
                case "--collapse-threshold":
                    if (++i >= args.Count || !int.TryParse(args[i], out collapseThreshold) || collapseThreshold < 2)
                    {
                        error.WriteLine("--collapse-threshold needs an integer >= 2");
                        return 2;
                    }
                    break;
                case "--max-arrows":
                    if (++i >= args.Count || !int.TryParse(args[i], out var cap) || cap < 1)
                    {
                        error.WriteLine("--max-arrows needs a positive integer");
                        return 2;
                    }
                    maxArrows = cap;
                    break;
                case "--browser-render-workers":
                    if (++i >= args.Count || !int.TryParse(args[i], out var workerCount) || workerCount < 0)
                    {
                        error.WriteLine("--browser-render-workers needs an integer >= 0 (0 renders on the main thread)");
                        return 2;
                    }
                    browserRenderWorkers = workerCount;
                    break;
                case "--note-format":
                    if (++i >= args.Count ||
                        (args[i] != "json" && args[i] != "yaml"))
                    {
                        error.WriteLine("--note-format needs json or yaml");
                        return 2;
                    }
                    notePayloadFormat = args[i] == "yaml"
                        ? Kronikol.Reports.NotePayloadFormat.Yaml
                        : Kronikol.Reports.NotePayloadFormat.Json;
                    break;
                case "--no-component-diagram":
                    componentDiagram = false;
                    break;
                case "--no-redact":
                    redact = false;
                    break;
                case "--redact-header":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    redactHeaders.Add(args[i]);
                    break;
                case "--allow-empty":
                    allowEmpty = true;
                    break;
                case "--chronological":
                    pairResponses = false;
                    break;
                case "--merge-duplicates":
                    mergeDuplicates = true;
                    break;
                case "--cucumber-messages":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    cucumberMessages.Add(args[i]);
                    break;
                case "--include-hooks":
                    includeHooks = true;
                    break;
                case "--fold-unknown":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    foldUnknown = args[i];
                    break;
                case "--strict":
                    strictParsing = true;
                    break;
                case "--no-capitalise" or "--no-capitalize":
                    capitalise = false;
                    break;
                case "--run-window":
                    runWindow = true;
                    break;
                case "--run-start" or "--run-end":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    if (!DateTimeOffset.TryParse(args[i], System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var instant))
                    {
                        error.WriteLine($"{arg} expects an ISO-8601 instant, got '{args[i]}'");
                        return 2;
                    }
                    if (arg == "--run-start") runStart = instant; else runEnd = instant;
                    runWindow = true;
                    break;
                case "--attribute-by-window":
                    attributeByWindow = true;
                    // The optional value is the capturer's fallback test id ("session"); a following
                    // option or an input path is not one, so only a bare word is consumed.
                    if (i + 1 < args.Count && !args[i + 1].StartsWith('-') && !LooksLikeInput(args[i + 1]))
                        windowFallbackId = args[++i];
                    break;
                case "--phase-from-steps":
                    phaseFromSteps = true;
                    break;
                case "--attachments-base":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    attachmentsBase = args[i];
                    break;
                case "--clean-attachments":
                    cleanAttachments = true;
                    break;
                case "--diagnostic":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    if (!TryParseDiagnostic(args[i], out var diagnostic))
                    {
                        error.WriteLine("--diagnostic needs \"<kind>:<message>\" with a non-empty message (kind = a DiagnosticKind name; anything else counts as Other)");
                        return 2;
                    }
                    hostDiagnostics.Add(diagnostic);
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

        if (inputs.Count == 0 && cucumberMessages.Count == 0)
        {
            error.WriteLine("No inputs given. Specify one or more NDJSON files, directories, or glob patterns of interaction records.");
            PrintUsage(error);
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

        for (var i = 0; i < cucumberMessages.Count; i++)
        {
            var full = Path.GetFullPath(cucumberMessages[i]);
            files.Remove(full); // a messages file inside an input directory is not an interaction capture
            if (!File.Exists(full))
            {
                error.WriteLine($"Cucumber messages file not found: {cucumberMessages[i]}");
                return 1;
            }

            cucumberMessages[i] = full;
        }

        if (files.Count == 0 && cucumberMessages.Count == 0)
        {
            error.WriteLine("No matching capture files found (*.ndjson / *.jsonl).");
            return 1;
        }

        @out.WriteLine($"Ingesting {files.Count} capture file(s):");
        foreach (var f in files)
            @out.WriteLine("  " + f);
        if (tests is not null)
            @out.WriteLine($"Tests file: {tests}");
        foreach (var messages in cucumberMessages)
            @out.WriteLine($"Cucumber messages: {messages}");

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.GetFullPath(output);
        options.PlantUmlRendering = render;
        options.CollapseConsecutiveIdenticalCalls = collapse;
        options.CollapseThreshold = collapseThreshold;
        options.MaxArrowsPerDiagram = maxArrows;
        if (browserRenderWorkers is not null)
            options.BrowserRenderWorkers = browserRenderWorkers.Value;
        options.NotePayloadFormat = notePayloadFormat;
        options.GenerateComponentDiagram = componentDiagram;
        options.CapitaliseStepText = capitalise;
        options.CapitaliseTitles = capitalise;
        if (title is not null)
            options.TestRunReportTitle = title;

        if (attachmentsBase is not null && !Directory.Exists(attachmentsBase))
        {
            error.WriteLine($"Attachments base directory not found: {attachmentsBase}");
            return 1;
        }

        var previousRedaction = RequestResponseLogger.Redaction;
        RequestResponseLogger.Redaction = redact
            ? new CaptureRedaction(CaptureRedaction.DefaultSecretHeaders.Concat(redactHeaders))
            : null;

        try
        {
            var result = IngestPipeline.Run(new IngestRequest
            {
                InteractionFiles = files,
                TestsFile = tests,
                Options = options,
                DefaultFeatureName = featureName,
                AllowEmpty = allowEmpty,
                CallTreeOrdering = pairResponses,
                MergeDuplicateInteractions = mergeDuplicates,
                FoldUnknownTestsInto = foldUnknown is null ? null : new UnknownTestFold(foldUnknown),
                CucumberMessagesFiles = cucumberMessages,
                IncludeHooks = includeHooks,
                StrictParsing = strictParsing,
                AttributeByTestWindow = attributeByWindow,
                WindowAttributionFallbackId = windowFallbackId,
                DropOutsideRunWindow = runWindow,
                RunStartedAt = runStart,
                RunEndedAt = runEnd,
                PhaseFromSteps = phaseFromSteps,
                AttachmentsBase = attachmentsBase is null ? null : Path.GetFullPath(attachmentsBase),
                CleanAttachments = cleanAttachments,
                HostDiagnostics = hostDiagnostics,
            });

            if (!result.Generated)
            {
                error.WriteLine("Nothing to report: no interaction or test records were found in the inputs.");
                return 1;
            }

            @out.WriteLine($"Replayed {result.InteractionCount} interaction record(s) into {result.ScenarioCount} scenario(s).");
            PrintDiagnostics(result.Diagnostics, @out);
            @out.WriteLine($"Wrote reports to {result.ReportsDirectory}");
            @out.WriteLine($"  {result.TestRunReportHtml}");
            return 0;
        }
        catch (FormatException ex)
        {
            error.WriteLine("Failed to read a capture file: " + ex.Message);
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine(ex.Message + (ex.FileName is not null ? $" ({ex.FileName})" : ""));
            return 1;
        }
        finally
        {
            RequestResponseLogger.Redaction = previousRedaction;
        }
    }

    /// <summary>
    /// Prints what the ingest wants the operator to know: skipped torn lines, diagrams that could not be
    /// produced, outputs that failed, labels that still do not read as sentences. Silence means a clean run.
    /// </summary>
    private static void PrintDiagnostics(IReadOnlyList<DiagnosticEntry> diagnostics, TextWriter @out)
    {
        if (diagnostics.Count == 0)
            return;

        var malformed = diagnostics.Where(d => d.Kind == DiagnosticKind.MalformedLine).ToArray();
        if (malformed.Length > 0)
        {
            @out.WriteLine($"{malformed.Length} malformed line(s) skipped:");
            foreach (var entry in malformed.Take(5))
                @out.WriteLine("  " + entry.Message);
            if (malformed.Length > 5)
                @out.WriteLine($"  … and {malformed.Length - 5} more");
        }

        foreach (var entry in diagnostics.Where(d => d.Kind != DiagnosticKind.MalformedLine))
            @out.WriteLine("  " + entry);
    }

    /// <summary>
    /// Parses a <c>--diagnostic</c> value: <c>&lt;kind&gt;:&lt;message&gt;</c>, where the kind is a
    /// <see cref="DiagnosticKind"/> name (case-insensitive; anything else — or no colon at all — is
    /// <see cref="DiagnosticKind.Other"/>) and the message is free text. False when the message is empty.
    /// </summary>
    internal static bool TryParseDiagnostic(string value, out DiagnosticEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var colon = value.IndexOf(':');
        var kind = DiagnosticKind.Other;
        var message = value.Trim();
        if (colon > 0 && Enum.TryParse<DiagnosticKind>(value[..colon].Trim(), ignoreCase: true, out var parsed))
        {
            kind = parsed;
            message = value[(colon + 1)..].Trim();
        }

        if (message.Length == 0)
            return false;

        entry = new DiagnosticEntry(kind, message);
        return true;
    }

    /// <summary>Whether an argument looks like a capture input (a path) rather than an option's value.</summary>
    private static bool LooksLikeInput(string value) =>
        value.Contains('/') || value.Contains('\\') || File.Exists(value) || Directory.Exists(value);

    internal static bool TryParseRender(string value, out PlantUmlRendering rendering)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "browserjs" or "browser" or "js": rendering = PlantUmlRendering.BrowserJs; return true;
            case "nodejs" or "node": rendering = PlantUmlRendering.NodeJs; return true;
            case "local": rendering = PlantUmlRendering.Local; return true;
            case "server": rendering = PlantUmlRendering.Server; return true;
            default: rendering = PlantUmlRendering.BrowserJs; return false;
        }
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol ingest <inputs...> [--tests <file>] [-o <dir>] [--render <mode>] [-t <title>] [options]");
        w.WriteLine();
        w.WriteLine("  Replays NDJSON interaction captures (one JSON object per line, the httpInteraction shape");
        w.WriteLine("  plus testId/testName) into a full Kronikol report: TestRunReport.html, the data files and");
        w.WriteLine("  the component diagram. Any capturer in any language can produce the input.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <inputs...>              Files, directories (searched recursively for *.ndjson / *.jsonl), or globs.");
        w.WriteLine("Options:");
        w.WriteLine("  --tests <file>           Tests NDJSON (start/step/end records) supplying outcome, duration and steps.");
        w.WriteLine("  -o, --output <dir>       Output directory (default: ./Reports).");
        w.WriteLine("  --render <mode>          browserjs (default, needs internet at view time) | nodejs | local | server.");
        w.WriteLine("  -t, --title <text>       Report title.");
        w.WriteLine("  --feature <name>         Feature name for tests without one (default: Ingested).");
        w.WriteLine("  --collapse | --no-collapse   Collapse consecutive identical calls into a loop fragment (default: on).");
        w.WriteLine("  --collapse-threshold <n> Minimum run length to collapse (default: 2).");
        w.WriteLine("  --max-arrows <n>         Cap request/response pairs per diagram; the rest is summarised.");
        w.WriteLine("  --browser-render-workers <n>  Web Workers the browserjs report renders diagrams on (default: 4, capped by");
        w.WriteLine("                           the viewer's CPU count); 0 renders on the main thread as before 3.0.45.");
        w.WriteLine("  --note-format <json|yaml>  Initial display format for JSON note payloads in the browserjs report");
        w.WriteLine("                           (default: json); readers can still switch either way in the report.");
        w.WriteLine("  --no-component-diagram   Skip ComponentDiagram.html.");
        w.WriteLine("  --no-redact              Do not redact credential headers at ingest (default: redact).");
        w.WriteLine("  --redact-header <name>   Additional header to redact (repeatable).");
        w.WriteLine("  --allow-empty            Generate even when nothing was ingested.");
        w.WriteLine("  --chronological          Strict timestamp order (default: call-tree order — each response follows its");
        w.WriteLine("                           request, calls made while handling a request nest inside it).");
        w.WriteLine("  --merge-duplicates       Fold the wire and span views of the same call into one arrow (a proxy/TCP tap");
        w.WriteLine("                           and an OTLP span tap both saw it): span trace id, wire body and status.");
        w.WriteLine("  --cucumber-messages <f>  Cucumber Messages NDJSON (repeatable) — playwright-bdd's cucumberReporter('message'),");
        w.WriteLine("                           cucumber-js --format message, Cucumber-JVM --plugin message. Its Gherkin structure");
        w.WriteLine("                           wins over --tests for the scenarios it owns.");
        w.WriteLine("  --include-hooks          Keep Cucumber hook steps (BeforeEach hook and friends) in the step list.");
        w.WriteLine("  --fold-unknown <name>    Collect interactions of test ids absent from --tests into one scenario");
        w.WriteLine("                           with this name (e.g. \"Traffic outside any test\").");
        w.WriteLine("  --strict                 Fail on the first malformed capture line (default: skip and report them).");
        w.WriteLine("  --no-capitalise          Leave step/assertion labels and feature/rule/scenario titles exactly as");
        w.WriteLine("                           the producer wrote them (default: upper-case the first letter of");
        w.WriteLine("                           keyword-less labels and of every title).");
        w.WriteLine("  --attribute-by-window [id]  Attribute interactions that carry no testId to the test that was");
        w.WriteLine("                           running at their timestamp; the optional id is the capturer's");
        w.WriteLine("                           fallback marker (e.g. \"session\") which counts as \"no testId\".");
        w.WriteLine("  --run-window             Keep only this run's traffic: drop interactions whose request lies before");
        w.WriteLine("                           the earliest tests record (a testrun/started marker, if the host writes one)");
        w.WriteLine("                           or after the testrun end marker — the previous run's and the stack's start-up");
        w.WriteLine("                           traffic otherwise lands in --fold-unknown.");
        w.WriteLine("  --run-start <iso>        Explicit run window bounds (UTC); each implies --run-window.");
        w.WriteLine("  --run-end <iso>");
        w.WriteLine("  --phase-from-steps       Give interactions the phase of the Given/When/Then step they happened");
        w.WriteLine("                           during, so setup traffic can be separated or highlighted.");
        w.WriteLine("  --attachments-base <dir> Resolve relative attachment paths in --tests against this directory.");
        w.WriteLine("  --clean-attachments      Empty the report's attachments/ folder first, so it holds this run only.");
        w.WriteLine("  --diagnostic <kind>:<msg> Carry a host diagnostic into the report (repeatable) — e.g. a tap's capture");
        w.WriteLine("                           health: \"CaptureDegraded:tap-di-redis: decoding disabled on 1 connection\".");
        w.WriteLine("                           kind = a DiagnosticKind name (unknown → Other); it lands in IngestResult.Diagnostics,");
        w.WriteLine("                           the report's \"Report diagnostics\" section and TestRunReport.json's diagnostics array.");
        w.WriteLine("  -h, --help               Show this help.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol ingest ./captures --tests ./captures/tests.ndjson -o ./Reports -t \"E2E run\"");
    }
}

/// <summary>Shared file/dir/glob expansion for CLI verbs.</summary>
internal static class CliInputs
{
    /// <summary>
    /// Expands the inputs into a deduplicated, ordered list of files. A directory is searched recursively
    /// for each of <paramref name="patterns"/>; an existing file is taken as-is; anything else is treated
    /// as a glob (top directory only). Unmatched inputs are reported to <paramref name="error"/> and skipped.
    /// </summary>
    public static List<string> Resolve(IEnumerable<string> inputs, string[] patterns, TextWriter error)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                result.Add(full);
        }

        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                foreach (var pattern in patterns)
                    foreach (var f in Directory.EnumerateFiles(input, pattern, SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                        Add(f);
            }
            else if (File.Exists(input))
            {
                Add(input);
            }
            else
            {
                var dir = Path.GetDirectoryName(input);
                var pattern = Path.GetFileName(input);
                var baseDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
                if (string.IsNullOrEmpty(pattern) || !Directory.Exists(baseDir))
                {
                    error.WriteLine($"Input not found: {input}");
                    continue;
                }

                var matches = Directory.EnumerateFiles(baseDir, pattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (matches.Length == 0)
                    error.WriteLine($"No files matched: {input}");
                foreach (var f in matches)
                    Add(f);
            }
        }

        return result;
    }
}
