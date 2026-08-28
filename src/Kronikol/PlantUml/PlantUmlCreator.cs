using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kronikol.Extensions;
using Kronikol.Tracking;

namespace Kronikol.PlantUml;

/// <summary>
/// Generates PlantUML sequence diagram source from <see cref="RequestResponseLog"/> entries.
/// Groups log entries by test ID and produces one or more PlantUML diagram fragments per test.
/// </summary>
public static partial class PlantUmlCreator
{
    private const int MaxLineWidth = 800;
    private const int MaxNoteChunkChars = 80; // Must stay under MaxLineWidth at ~9px/char to avoid PlantUML wrapWidth overflow losing color tags
    private const string EventNoteClass = "eventNote";
    public const int DefaultMaxEncodedDiagramLength = 2000;
    private const int MaxResponseNoteChunkLength = 15_000;
    private const int MaxEstimatedDiagramHeight = 12_000;
    private const int EstimatedArrowHeight = 45;
    private const int EstimatedNoteLineHeight = 18;

    public static string[] DefaultExcludedHeaders => ["Cache-Control", "Pragma"];

    private static readonly ConcurrentDictionary<string, string> AliasCache = new();

    public static IEnumerable<PlantUmlForTest> GetPlantUmlImageTagsPerTestId(
        IEnumerable<RequestResponseLog>? requestResponses,
        string plantUmlServerRendererUrl = "https://www.plantuml.com/plantuml/png",
        Func<string, string>? requestPreFormattingProcessor = null,
        Func<string, string>? requestPostFormattingProcessor = null,
        Func<string, string>? responsePreFormattingProcessor = null,
        Func<string, string>? responsePostFormattingProcessor = null,
        Func<string, string>? requestMidFormattingProcessor = null,
        Func<string, string>? responseMidFormattingProcessor = null,
        string[]? excludedHeaders = null,
        int maxUrlLength = 100,
        bool separateSetup = false,
        bool highlightSetup = true,
        string? setupHighlightColor = null,
        bool lazyLoadImages = true,
        FocusEmphasis focusEmphasis = FocusEmphasis.Bold,
        FocusDeEmphasis focusDeEmphasis = FocusDeEmphasis.LightGray,
        string? plantUmlTheme = null,
        bool internalFlowTracking = false,
        int maxEncodedDiagramLength = DefaultMaxEncodedDiagramLength,
        int truncateNotesAfterLines = 0,
        bool excludeAllHeaders = false,
        bool sequenceDiagramArrowColors = true,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null,
        GraphQlBodyFormat graphQlBodyFormat = GraphQlBodyFormat.FormattedWithMetadata,
        bool clientSideSplitting = false,
        bool collapseConsecutiveIdenticalCalls = false,
        int collapseThreshold = 2,
        int? maxArrowsPerDiagram = null)
    {
        excludedHeaders ??= DefaultExcludedHeaders;

        var requestsResponseByTraceIdAndTest = requestResponses?.GroupBy(x => x.TestId);

        var plantUmlPerTestName = requestsResponseByTraceIdAndTest?
            .AsParallel()
            .AsOrdered()
            .Select(testTraces =>
        {
            var traces = testTraces.ToList();
            var testName = testTraces.First().TestName;
            var results = CreatePlantUml(
                traces, 
                requestPreFormattingProcessor,
                requestPostFormattingProcessor,
                responsePreFormattingProcessor,
                responsePostFormattingProcessor,
                requestMidFormattingProcessor,
                responseMidFormattingProcessor,
                excludedHeaders, 
                maxUrlLength,
                separateSetup,
                highlightSetup,
                setupHighlightColor,
                focusEmphasis,
                focusDeEmphasis,
                plantUmlTheme,
                internalFlowTracking,
                maxEncodedDiagramLength,
                truncateNotesAfterLines,
                excludeAllHeaders,
                sequenceDiagramArrowColors,
                sequenceDiagramParticipantColors,
                dependencyColors,
                serviceTypeOverrides,
                graphQlBodyFormat,
                clientSideSplitting,
                collapseConsecutiveIdenticalCalls,
                collapseThreshold,
                maxArrowsPerDiagram);
            var imageTags = results.Select(x => x.GetPlantUmlImageTag(plantUmlServerRendererUrl, lazyLoadImages)).ToArray();
            return new PlantUmlForTest(testTraces.Key, testName, results.Select(result => (result.PlantUml, result.PlantUmlEncoded)), testTraces.ToList(), imageTags);
        });

        return plantUmlPerTestName?.AsEnumerable() ?? [];
    }

    private static PlantUmlResult[] CreatePlantUml(
        List<RequestResponseLog> tracesForTest,
        Func<string, string>? requestPreFormattingProcessor,
        Func<string, string>? requestPostFormattingProcessor,
        Func<string, string>? responsePreFormattingProcessor,
        Func<string, string>? responsePostFormattingProcessor,
        Func<string, string>? requestMidFormattingProcessor,
        Func<string, string>? responseMidFormattingProcessor,
        string[] excludedHeaders,
        int maxUrlLength,
        bool separateSetup,
        bool highlightSetup,
        string? setupHighlightColor,
        FocusEmphasis focusEmphasis,
        FocusDeEmphasis focusDeEmphasis,
        string? plantUmlTheme,
        bool internalFlowTracking,
        int maxEncodedDiagramLength,
        int truncateNotesAfterLines = 0,
        bool excludeAllHeaders = false,
        bool sequenceDiagramArrowColors = true,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null,
        GraphQlBodyFormat graphQlBodyFormat = GraphQlBodyFormat.FormattedWithMetadata,
        bool clientSideSplitting = false,
        bool collapseConsecutiveIdenticalCalls = false,
        int collapseThreshold = 2,
        int? maxArrowsPerDiagram = null)
    {
        // Collapse poll/retry bursts and apply the arrow cap before rendering (no-op when both are off).
        var collapsed = SequenceCollapser.Apply(tracesForTest, collapseConsecutiveIdenticalCalls, collapseThreshold, maxArrowsPerDiagram);
        tracesForTest = collapsed.Traces;
        var omittedPairs = collapsed.OmittedPairs;
        if (tracesForTest.Count == 0)
            return [];

        var builder = new DiagramBuilder(tracesForTest, plantUmlTheme, clientSideSplitting ? int.MaxValue : maxEncodedDiagramLength,
            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides);
        var lastTrace = tracesForTest[^1];

        var currentlyOverriding = false;
        var hasActionStart = separateSetup && tracesForTest.Any(t => t.IsActionStart);
        var actionStartIndex = tracesForTest.FindIndex(t => t.IsActionStart);
        var hasSetupTraces = hasActionStart && tracesForTest
            .Take(actionStartIndex)
            .Any(t => !t.IsOverrideStart && !t.IsOverrideEnd && !t.IsActionStart);
        var setupPartitionClosed = false;
        var effectiveColor = setupHighlightColor ?? "#F6F6F6";
        var partitionLine = highlightSetup ? $"partition {effectiveColor} Setup" : "partition Setup";
        var isInActionPhase = actionStartIndex < 0; // no IsActionStart marker → everything is action

        foreach (var trace in tracesForTest)
        {
            if (trace.IsActionStart)
            {
                builder.ClosePartition();
                setupPartitionClosed = true;
                isInActionPhase = true;
                continue;
            }

            if (trace.IsOverrideStart && currentlyOverriding)
            {
                Debug.Write("Ignoring an override as you're already overriding");
                continue;
            }

            if (trace.IsOverrideEnd)
            {
                currentlyOverriding = false;
                builder.Append(trace.PlantUml ?? "");
                continue;
            }

            if (trace.IsOverrideStart)
            {
                if (hasActionStart && !setupPartitionClosed)
                {
                    builder.ClosePartition();
                    setupPartitionClosed = true;
                }
                currentlyOverriding = true;
                builder.Append(trace.PlantUml ?? "");
                continue;
            }

            if (currentlyOverriding)
                continue;

            if (hasSetupTraces && !builder.HasOpenPartition && !setupPartitionClosed)
                builder.OpenPartition(partitionLine);

            // Resolve phase variant: pick Setup or Action variant based on position relative to IsActionStart
            var activeVariant = isInActionPhase ? trace.ActionVariant : trace.SetupVariant;
            if (activeVariant is { Skip: true })
                continue;

            var effectiveMethod = activeVariant?.Method ?? trace.Method;
            var effectiveUri = activeVariant?.Uri ?? trace.Uri;
            var effectiveContent = activeVariant is not null ? activeVariant.Content : trace.Content;
            var effectiveHeaders = activeVariant?.Headers ?? trace.Headers;

            var serviceShortName = SanitizePlantUmlAlias(trace.ServiceName);
            var callerShortName = SanitizePlantUmlAlias(trace.CallerName);
            var content = effectiveContent ?? string.Empty;

            switch (trace.Type)
            {
                case RequestResponseType.Request when trace.IsUserAction:
                {
                    // A user action: one arrow from the actor to the service, labelled with the action,
                    // no response arrow. Its detail (full title / locator) is the note.
                    var actionLabel = (effectiveMethod.Value?.ToString() ?? "action").Replace("\r", string.Empty).Replace("\n", "\\n");
                    var actionCategory = trace.CallerDependencyCategory ?? Constants.DependencyCategories.User;
                    var actionColor = builder.GetArrowColor(trace.CallerName, actionCategory, trace.CallerName, actionCategory);
                    var actionPrefix = $"{callerShortName} -{actionColor}> {serviceShortName}: ";
                    // A long Playwright locator or action description is one message statement, and the
                    // engine abandons the whole diagram past 2000 characters.
                    actionLabel = PlantUmlStatementLimits.TruncateLabel(
                        actionLabel, PlantUmlStatementLimits.MaxMessageStatementChars - actionPrefix.Length);
                    builder.AppendLine($"{actionPrefix}{actionLabel}");
                    builder.AddArrowHeight();

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var actionNote = TruncateNoteContent(content, truncateNotesAfterLines);
                        builder.AppendLine($"note left");
                        builder.AppendLine(actionNote);
                        builder.AppendLine("end note");
                        builder.AddNoteHeight(actionNote);
                    }

                    break;
                }
                case RequestResponseType.Request:
                {
                    if (requestPreFormattingProcessor is not null)
                        content = requestPreFormattingProcessor(content);

                    var noteContent = FormatNoteContent(excludeAllHeaders ? [] : effectiveHeaders, content, excludedHeaders, RequestResponseType.Request, requestMidFormattingProcessor, trace.FocusFields, focusEmphasis, focusDeEmphasis, graphQlBodyFormat);

                    if (requestPostFormattingProcessor is not null)
                        noteContent = requestPostFormattingProcessor(noteContent);

                    var fullPathAndQuery = effectiveUri.PathAndQuery;
                    var pathAndQuery = fullPathAndQuery;
                    if (pathAndQuery.Length > maxUrlLength)
                        pathAndQuery = string.Join("\\n        ", pathAndQuery.ChunksUpTo(maxUrlLength));

                    var requestLabel = $"{effectiveMethod.Value}: {pathAndQuery}";

                    var graphQlLabel = GraphQlOperationDetector.TryExtractLabel(effectiveContent);
                    if (graphQlLabel is not null)
                        requestLabel = $"{requestLabel}\\n({graphQlLabel})";

                    var arrowColor = builder.GetArrowColor(trace.ServiceName, trace.DependencyCategory, trace.CallerName, trace.CallerDependencyCategory);
                    var requestPrefix = $"{callerShortName} -{arrowColor}> {serviceShortName}: ";

                    // `maxUrlLength` decides where the label *wraps for display*, not how long it may get:
                    // a 5,300-character Redis DELETE path became 53 display chunks joined by a literal
                    // `\n        ` and one 5,410-character statement, which the engine refuses outright.
                    // Cap against the real statement, counting the prefix and the internal-flow link that
                    // wraps the label — cutting inside `[[…]]` would leave the link unclosed.
                    var linkWrapperLength = internalFlowTracking ? $"[[#iflow-{trace.RequestResponseId} ]]".Length : 0;
                    var labelBudget = PlantUmlStatementLimits.MaxMessageStatementChars - requestPrefix.Length - linkWrapperLength;
                    var cappedLabel = PlantUmlStatementLimits.TruncateLabel(requestLabel, labelBudget);
                    var labelWasTruncated = cappedLabel.Length != requestLabel.Length;
                    requestLabel = cappedLabel;

                    if (internalFlowTracking)
                        requestLabel = $"[[#iflow-{trace.RequestResponseId} {requestLabel}]]";

                    if (trace.CollapsedCount > 1)
                    {
                        var loopLabel = trace.CollapsedSummary is { Length: > 0 } summary
                            ? $"loop ×{trace.CollapsedCount} · {summary}"
                            : $"loop ×{trace.CollapsedCount}";
                        builder.OpenLoop(PlantUmlStatementLimits.TruncateStatement(loopLabel, PlantUmlStatementLimits.MaxBlockLabelChars),
                            trace.RequestResponseId);
                    }

                    builder.AppendLine($"{requestPrefix}{requestLabel}");
                    builder.AddArrowHeight();

                    // For a DELETE with no body the path *is* the payload, so a truncated label must not be
                    // the only record of what was called. Note bodies are uncapped and already chunked for
                    // wrapWidth, so the whole path stays visible, searchable and copyable there.
                    if (labelWasTruncated)
                        noteContent = AppendFullPathToNote(noteContent, fullPathAndQuery);

                    if (!string.IsNullOrEmpty(noteContent))
                    {
                        var truncatedContent = TruncateNoteContent(noteContent, truncateNotesAfterLines);
                        var noteSide = trace.NoteOnRight ? "right" : "left";
                        builder.AppendLine($"note{GetNoteClass(trace.MetaType)} {noteSide}");
                        builder.AppendLine(truncatedContent);
                        builder.AppendLine("end note");
                        builder.AddNoteHeight(truncatedContent);
                    }

                    break;
                }
                case RequestResponseType.Response:
                {
                    if (responsePreFormattingProcessor is not null)
                        content = responsePreFormattingProcessor(content);

                    var noteContent = FormatNoteContent(excludeAllHeaders ? [] : effectiveHeaders, content, excludedHeaders, RequestResponseType.Response, responseMidFormattingProcessor, trace.FocusFields, focusEmphasis, focusDeEmphasis);

                    if (responsePostFormattingProcessor is not null)
                        noteContent = responsePostFormattingProcessor(noteContent);

                    AppendResponseNoteContent(builder, noteContent, trace, serviceShortName, callerShortName, internalFlowTracking, truncateNotesAfterLines, clientSideSplitting);
                    if (builder.OpenLoopRequestResponseId == trace.RequestResponseId)
                        builder.CloseLoop();
                    break;
                }
            }

            builder.IncrementStep();

            if (!clientSideSplitting && !builder.HasOpenLoop && (builder.EncodedDiagramExceedsMaxLength || builder.EstimatedHeightExceedsMax) && trace != lastTrace)
                builder.FinishAndStartNewDiagram();
        }

        builder.CloseLoop();
        if (omittedPairs > 0)
        {
            builder.AppendLine($"...+{omittedPairs} more call{(omittedPairs == 1 ? "" : "s")} omitted (MaxArrowsPerDiagram)...");
            builder.AddArrowHeight();
        }

        builder.FinishAndStartNewDiagram();
        return builder.GetResults();
    }

    /// <summary>
    /// Adds the untruncated request path to the note beside the arrow, chunked the way every other note
    /// value is so <c>skinparam wrapWidth</c> can break it. Kronikol's own <c>&lt;color:gray&gt;</c> label
    /// is added after escaping, like the header tags, so it stays live markup rather than printed text.
    /// </summary>
    private static string AppendFullPathToNote(string noteContent, string pathAndQuery)
    {
        var chunks = pathAndQuery.ChunksUpTo(MaxNoteChunkChars).Select(EscapeCreoleMarkup);
        var block = "<color:gray>[Full path]" + Environment.NewLine + string.Join(Environment.NewLine, chunks);
        return string.IsNullOrEmpty(noteContent)
            ? block
            : noteContent + Environment.NewLine + Environment.NewLine + block;
    }

    private static string GetNoteClass(RequestResponseMetaType metaType) =>
        metaType == RequestResponseMetaType.Event ? $"<<{EventNoteClass}>>" : "";

    // Note bodies carry the payload's backslash bytes verbatim. PlantUML block
    // notes render backslash sequences literally (probed against plantuml.js
    // 1.2026.6 and the IKVM jar, with and without teoz): the ONLY consumed
    // sequence is \t, rendered as a real tab — and no escaping can prevent
    // that, since the final \t pair of any backslash run is consumed. The
    // pre-3.0.62 blanket backslash doubling therefore displayed \\n for a
    // wire \n while still losing tabs, and was removed.

    /// <summary>
    /// Neutralises PlantUML creole markup a captured payload happens to contain, so a note shows the bytes
    /// that went over the wire rather than PlantUML's reading of them. Creole consumes its own markers:
    /// a line carrying two <c>--</c> (SQL comments in a one-line BigQuery job body), two <c>//</c> (two URLs),
    /// two <c>**</c>, <c>__</c> or <c>""</c> loses both markers and gets the span between them restyled, and a
    /// tag PlantUML knows — <c>&lt;b&gt;</c>, <c>&lt;color:red&gt;</c> — is swallowed wherever it appears.
    /// A <c>~</c> in front of a marker character makes PlantUML print it instead.
    /// <para>
    /// Only what PlantUML would actually consume is escaped: a marker needs a partner on the same line to
    /// style anything, so a lone <c>https://</c> is left exactly as captured. Kronikol's own markup — the
    /// gray header tags, the binary placeholder, focus emphasis — is added after this runs and is never escaped.
    /// </para>
    /// </summary>
    internal static string EscapeCreoleMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length + text.Length / 16);
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? text.Length : newline;
            EscapeCreoleLine(text.AsSpan(lineStart, lineEnd - lineStart), sb);
            if (newline < 0) break;
            sb.Append('\n');
            lineStart = newline + 1;
        }
        return sb.ToString();
    }

    /// <summary>Doubled characters creole reads as a span delimiter, plus <c>[[</c> for a link.</summary>
    private const string CreolePairChars = "/*_-\"[";

    private static void EscapeCreoleLine(ReadOnlySpan<char> line, StringBuilder sb)
    {
        // A marker only styles anything when the line gives it a partner, so decide per line which of them
        // are live. Escaping the rest would only add invisible `~` noise to the .puml a reader may open.
        Span<bool> live = stackalloc bool[CreolePairChars.Length];
        for (var k = 0; k < CreolePairChars.Length; k++)
        {
            // `[[…]]` needs its closing half; every other marker pairs with a second copy of itself.
            live[k] = CreolePairChars[k] == '['
                ? Occurrences(line, '[') >= 1 && line.IndexOf("]]".AsSpan()) >= 0
                : Occurrences(line, CreolePairChars[k]) >= 2;
        }

        var contentStarted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var pairIndex = CreolePairChars.IndexOf(c);
            var isPair = pairIndex >= 0 && live[pairIndex] && i + 1 < line.Length && line[i + 1] == c;

            if (!contentStarted && c != ' ' && c != '\t')
            {
                contentStarted = true;
                // A line opening with one of these is a bullet, a numbered item or a heading: creole eats
                // the marker and restyles the line. `--`/`__` separators are already covered as pairs.
                if (!isPair && c is '*' or '#' or '=') sb.Append('~');
            }

            if (isPair)
            {
                sb.Append('~').Append(c).Append('~').Append(c);
                i++;
                continue;
            }

            if (c == '<' && i + 1 < line.Length && IsCreoleTagStart(line[i + 1]))
                sb.Append('~');

            sb.Append(c);
        }
    }

    private static int Occurrences(ReadOnlySpan<char> line, char c)
    {
        var count = 0;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] != c || line[i + 1] != c) continue;
            count++;
            i++;
        }
        return count;
    }

    private static bool IsCreoleTagStart(char c) => c == '/' || c == '#' || char.IsAsciiLetter(c);

    private static string TruncateNoteContent(string noteContent, int maxLines)
    {
        if (maxLines <= 0) return noteContent;
        var lines = noteContent.Split('\n');
        if (lines.Length <= maxLines) return noteContent;
        return string.Join("\n", lines.Take(maxLines)) + "\n...";
    }

    private static void AppendResponseNoteContent(
        DiagramBuilder builder,
        string noteContent,
        RequestResponseLog trace,
        string serviceShortName,
        string callerShortName,
        bool internalFlowTracking = false,
        int truncateNotesAfterLines = 0,
        bool clientSideSplitting = false)
    {
        var prefix = "..Continued From Previous Diagram.." + Environment.NewLine;
        var suffix = Environment.NewLine + "..Continued On Next Diagram..";
        var maxResponseLength = MaxResponseNoteChunkLength + suffix.Length + prefix.Length;

        if (!clientSideSplitting && noteContent.Length > maxResponseLength)
        {
            var chunks = noteContent.ChunksUpTo(MaxResponseNoteChunkLength).ToArray();
            for (var i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                var isFirst = i == 0;
                var isLast = i == chunks.Length - 1;

                if (!isFirst) chunk = prefix + chunk;
                if (!isLast) chunk += suffix;

                AppendResponseNoteContent(builder, chunk, trace, serviceShortName, callerShortName, internalFlowTracking, truncateNotesAfterLines);

                if (!isLast)
                    builder.FinishAndStartNewDiagram();
            }
        }
        else
        {
            var status = trace.StatusCode?.Value?.ToString()?.Titleize();
            if (trace?.StatusCode?.Value as HttpStatusCode? == (HttpStatusCode)302)
                status += " (Redirect)"; // The name of 302 'Found' is a bit ambiguous, so we make it clearer for the reader

            var responseLabel = status ?? "";

            var arrowColor = builder.GetArrowColor(trace!.ServiceName, trace.DependencyCategory, trace.CallerName, trace.CallerDependencyCategory);
            var responsePrefix = $"{serviceShortName} -{arrowColor}-> {callerShortName}: ";
            responseLabel = PlantUmlStatementLimits.TruncateLabel(
                responseLabel, PlantUmlStatementLimits.MaxMessageStatementChars - responsePrefix.Length);
            builder.AppendLine($"{responsePrefix}{responseLabel}");
            builder.AddArrowHeight();

            if (!string.IsNullOrEmpty(noteContent))
            {
                var truncatedContent = TruncateNoteContent(noteContent, truncateNotesAfterLines);
                builder.AppendLine($"note{GetNoteClass(trace!.MetaType)} right");
                builder.AppendLine(truncatedContent);
                builder.AppendLine("end note");
                builder.AddNoteHeight(truncatedContent);
            }
        }
    }

    private static string CreatePlantUmlPrefix(
        List<RequestResponseLog> tracesForTest,
        int stepNumber,
        string? plantUmlTheme = null,
        bool sequenceDiagramArrowColors = true,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null)
    {
        var entitiesPlantUml = CreateEntitiesPlantUml(tracesForTest, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides);
        var themeDirective = !string.IsNullOrWhiteSpace(plantUmlTheme) ? $"!theme {plantUmlTheme}\n" : "";
        return $"""

                @startuml
                {themeDirective}!pragma teoz true
                {AddEventStyling(tracesForTest)}
                {AddAssertionStyling(tracesForTest)}
                skinparam wrapWidth {MaxLineWidth}
                autonumber {stepNumber}

                {entitiesPlantUml}

                """.TrimStart();
    }

    private const string AssertionNoteClass = "assertionNote";

    /// <summary>
    /// The single participant declared for a diagram that consists only of injected markers (step bars /
    /// assertion notes) so that <c>hnote across</c> has a lifeline to span. The browser render script
    /// recognises this exact line and does not count it as a drawable body.
    /// </summary>
    internal const string MarkerOnlyParticipant = "participant \"(no interactions)\" as noInteractions";

    private static string AddAssertionStyling(List<RequestResponseLog> tracesForTest) =>
        tracesForTest.Any(x => x.PlantUml is not null && x.PlantUml.Contains($"<<{AssertionNoteClass}>>"))
            ? $$"""

                <style>
                 .{{AssertionNoteClass}} {
                     FontSize 11
                     RoundCorner 5
                 }
                </style>
                """.TrimStart()
            : "";

    private static string AddEventStyling(List<RequestResponseLog> tracesForTest) =>
        tracesForTest.Any(x => x.MetaType == RequestResponseMetaType.Event)
            ? $$"""

                <style>
                 .{{EventNoteClass}} {
                     BackgroundColor #cfecf7
                     FontSize 11
                     RoundCorner 10
                 }
                </style>
                """.TrimStart()
            : "";

    private static string CreateEntitiesPlantUml(
        List<RequestResponseLog> tracesForTest,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null)
    {
        var sb = new StringBuilder();
        var actorDefined = false;
        var currentPlayers = new HashSet<string>();

        var relevantTraces = tracesForTest
            .Where(x => x is { IsOverrideStart: false, IsOverrideEnd: false, IsActionStart: false })
            .ToList();

        // A diagram made only of injected markers (step bars / assertion notes — a test that asserted
        // but never touched a tracked dependency) has no participant at all, and `hnote across` with
        // nothing to span is a PlantUML syntax error in every real engine (server, IKVM, plantuml.js).
        // Give the notes one lifeline to hang on; the browser guard treats this line as non-drawable so
        // the "Nothing to draw with the current filters…" affordance still applies while they are hidden.
        if (relevantTraces.Count == 0)
        {
            if (tracesForTest.Any(t => (t.IsOverrideStart || t.IsOverrideEnd) && !string.IsNullOrWhiteSpace(t.PlantUml)))
                sb.AppendLine(MarkerOnlyParticipant);
            return sb.ToString();
        }

        // Find the pure caller (appears as CallerName but never as ServiceName) and declare it first
        var allServiceNames = new HashSet<string>(relevantTraces.Select(t => t.ServiceName));
        var pureCaller = relevantTraces
            .Select(t => t.CallerName)
            .FirstOrDefault(c => !allServiceNames.Contains(c));

        // Build a lookup: callerName → CallerDependencyCategory (for caller participant shapes)
        var callerCategories = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var trace in relevantTraces)
        {
            if (callerCategories.ContainsKey(trace.CallerName)) continue;
            if (serviceTypeOverrides?.TryGetValue(trace.CallerName, out var callerOv) == true)
            {
                callerCategories[trace.CallerName] = callerOv;
                continue;
            }
            callerCategories[trace.CallerName] = relevantTraces
                .Where(t => t.CallerName == trace.CallerName && t.CallerDependencyCategory is not null)
                .Select(t => t.CallerDependencyCategory)
                .FirstOrDefault();
        }

        if (pureCaller != null)
        {
            var pureCallerAlias = SanitizePlantUmlAlias(pureCaller);
            currentPlayers.Add(pureCallerAlias);
            var pureCallerCategory = callerCategories.TryGetValue(pureCaller, out var pcc) ? pcc : null;
            if (pureCallerCategory is not null)
            {
                var pureCallerType = DependencyPalette.Resolve(pureCallerCategory);
                var pureCallerShape = DependencyPalette.GetSequenceShape(pureCallerType);
                var pureCallerColor = "";
                if (sequenceDiagramParticipantColors)
                    pureCallerColor = " " + DependencyPalette.GetColor(pureCallerCategory, dependencyColors);

                sb.Append(pureCallerShape)
                    .Append(" \"")
                    .Append(pureCaller)
                    .Append("\" as ")
                    .Append(pureCallerAlias)
                    .AppendLine(pureCallerColor);
            }
            else
            {
                sb.Append("actor \"")
                    .Append(pureCaller)
                    .Append("\" as ")
                    .AppendLine(pureCallerAlias);
            }
            actorDefined = true;
        }

        // Build a lookup: serviceName → category (user overrides then auto-detect)
        var serviceCategories = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var trace in relevantTraces)
        {
            if (serviceCategories.ContainsKey(trace.ServiceName)) continue;
            if (serviceTypeOverrides?.TryGetValue(trace.ServiceName, out var ov) == true)
            {
                serviceCategories[trace.ServiceName] = ov;
                continue;
            }
            serviceCategories[trace.ServiceName] = relevantTraces
                .Where(t => t.ServiceName == trace.ServiceName && t.DependencyCategory is not null)
                .Select(t => t.DependencyCategory)
                .FirstOrDefault();
        }

        foreach (var trace in relevantTraces)
        {
            var serviceShortName = SanitizePlantUmlAlias(trace.ServiceName);
            var callerShortName = SanitizePlantUmlAlias(trace.CallerName);

            if (currentPlayers.Add(callerShortName))
            {
                var callerCategory = callerCategories.TryGetValue(trace.CallerName, out var cc) ? cc : null;
                if (callerCategory is not null)
                {
                    // Caller has an explicit category — use DependencyPalette for shape
                    var callerType = DependencyPalette.Resolve(callerCategory);
                    var callerShape = DependencyPalette.GetSequenceShape(callerType);
                    var callerColorSuffix = "";
                    if (sequenceDiagramParticipantColors)
                        callerColorSuffix = " " + DependencyPalette.GetColor(callerCategory, dependencyColors);

                    sb.Append(callerShape)
                        .Append(" \"")
                        .Append(trace.CallerName)
                        .Append("\" as ")
                        .Append(callerShortName)
                        .AppendLine(callerColorSuffix);
                }
                else
                {
                    // Callers without a category: use actor (first) or entity (subsequent)
                    sb.Append(actorDefined ? "entity" : "actor")
                        .Append(" \"")
                        .Append(trace.CallerName)
                        .Append("\" as ")
                        .AppendLine(callerShortName);
                }
            }

            if (currentPlayers.Add(serviceShortName))
            {
                var category = serviceCategories.TryGetValue(trace.ServiceName, out var cat) ? cat : null;
                var depType = DependencyPalette.Resolve(category);
                var shape = DependencyPalette.GetSequenceShape(depType);
                var colorSuffix = "";
                if (sequenceDiagramParticipantColors && category is not null)
                    colorSuffix = " " + DependencyPalette.GetColor(category, dependencyColors);

                sb.Append(shape)
                    .Append(" \"")
                    .Append(trace.ServiceName)
                    .Append("\" as ")
                    .Append(serviceShortName)
                    .AppendLine(colorSuffix);
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitizeAliasRegex();

    private static string SanitizePlantUmlAlias(string name)
    {
        return AliasCache.GetOrAdd(name, n => SanitizeAliasRegex().Replace(n.Camelize(), "_"));
    }

    internal static bool IsBinaryContent(string? content)
    {
        if (content is null || content.Length == 0) return false;
        var checkLength = Math.Min(content.Length, 512);
        var controlCount = 0;
        for (var i = 0; i < checkLength; i++)
        {
            var c = content[i];
            if (c != '\t' && c != '\n' && c != '\r' && c < ' ')
                controlCount++;
        }
        return controlCount > checkLength * 0.1;
    }

    private static string FormatNoteContent(
        IEnumerable<(string Key, string? Value)> headers,
        string? content,
        string[] excludedHeaders,
        RequestResponseType type,
        Func<string, string>? midFormattingProcessor = null,
        string[]? focusFields = null,
        FocusEmphasis focusEmphasis = FocusEmphasis.Bold,
        FocusDeEmphasis focusDeEmphasis = FocusDeEmphasis.LightGray,
        GraphQlBodyFormat graphQlBodyFormat = GraphQlBodyFormat.FormattedWithMetadata)
    {
        // Detect binary/compressed content and replace with placeholder. The placeholder is Kronikol's own
        // markup rather than captured bytes, so it is the one body that must not be creole-escaped.
        var escapePayload = !IsBinaryContent(content);
        if (!escapePayload)
            content = "<i>[binary content]</i>";

        // For requests, try GraphQL formatting first (unless FocusFields are in use, which need JSON)
        string? parsedContent = null;
        var suppressHeaders = false;

        if (type is RequestResponseType.Request && graphQlBodyFormat != GraphQlBodyFormat.Json && focusFields is not { Length: > 0 })
        {
            parsedContent = GraphQlBodyFormatter.TryFormat(content, graphQlBodyFormat);
            if (parsedContent is not null && graphQlBodyFormat == GraphQlBodyFormat.FormattedQueryOnly)
                suppressHeaders = true;
        }

        parsedContent ??= TryFormatAsJson(content);
        parsedContent ??= TryFormatTruncatedJson(content);

        var payloadIsPreEscaped = false;
        if (parsedContent is null)
        {
            if (type is RequestResponseType.Response)
                parsedContent = content ?? string.Empty;
            else
            {
                // Escapes each piece itself: the `&` divider it weaves in is Kronikol markup.
                parsedContent = FormatFormUrlEncodedContent(content, escapePayload);
                payloadIsPreEscaped = true;
            }
        }

        var formattedContent = parsedContent!;

        // Before the processors: a payload rewrite sees the bytes as captured, and markup a processor
        // deliberately injects still reaches PlantUML.
        if (escapePayload && !payloadIsPreEscaped)
            formattedContent = EscapeCreoleMarkup(formattedContent);

        if (midFormattingProcessor is not null)
            formattedContent = midFormattingProcessor(formattedContent);

        // Whatever the formatter produced, no line may carry a whitespace-free run PlantUML cannot wrap:
        // `skinparam wrapWidth` breaks at spaces only, so a 65 KB minified payload on one line is a
        // 400,000 px wide note and plantuml.js refuses the diagram ("Diagram too large for browser
        // rendering"). Seen live with capture-capped Redis/BigQuery bodies (tap-resilience plan).
        formattedContent = WrapUnbreakableRuns(formattedContent);

        if (focusFields is { Length: > 0 })
        {
            formattedContent = JsonFocusFormatter.FormatWithFocus(formattedContent, focusFields, focusEmphasis, focusDeEmphasis);
        }

        var headersOnTop = suppressHeaders ? "" : string.Join(Environment.NewLine, headers
            .Where(y => !excludedHeaders.Contains(y.Key))
            .OrderBy(y => y.Key)
            .SelectMany(y => BatchGray($"[{y.Key}={y.Value}]")));

        return ((headersOnTop + Environment.NewLine + Environment.NewLine).TrimStart() + formattedContent.Trim()).TrimEnd();
    }

    private static readonly JsonWriterOptions IndentedWriterOptions = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string? TryFormatAsJson(string? content)
    {
        if (content is null || (!content.StartsWith('{') && !content.StartsWith('[')))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, IndentedWriterOptions))
            {
                WriteElementWithoutNulls(writer, doc.RootElement);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// A JSON body cut by a capture cap — TcpTap/ProxyTap/<see cref="Tracking.RequestResponseLogger"/>
    /// append <c>…truncated (N chars total)</c>, the RESP decoder <c>…[bulk string truncated: …]</c> —
    /// is not parseable, so <see cref="TryFormatAsJson"/> gives up and the note used to get the raw
    /// one-line payload. This re-indents the valid prefix with a string-aware brace walker (no null
    /// stripping — there is no document to walk) and keeps the marker on its own line; anything that
    /// is not a valid JSON <em>prefix</em> (a real non-JSON body that happens to start with a brace)
    /// is left to the plain-text path.
    /// </summary>
    internal static string? TryFormatTruncatedJson(string? content)
    {
        if (content is null || content.Length < 2 || (content[0] != '{' && content[0] != '['))
            return null;

        var (body, marker) = SplitTruncationMarker(content);
        if (body.Length < 2 || !IsJsonPrefix(body))
            return null;

        var indented = ReindentJsonPrefix(body);
        return marker is null ? indented : indented + "\n" + marker;
    }

    [GeneratedRegex(@"(?:\r?\n\r?\n…truncated \(\d+ chars total\)|\s…\[bulk string truncated: [^\]]*\])\s*$")]
    private static partial Regex TruncationMarkerRegex();

    private static (string Body, string? Marker) SplitTruncationMarker(string content)
    {
        var match = TruncationMarkerRegex().Match(content);
        return match.Success
            ? (content[..match.Index], match.Value.Trim())
            : (content, null);
    }

    /// <summary>True when <paramref name="text"/> is a valid JSON document or a valid prefix of one (cut anywhere).</summary>
    internal static bool IsJsonPrefix(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
        try
        {
            while (reader.Read()) { }
            return reader.BytesConsumed > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Pretty-prints a (possibly truncated) JSON text by structure alone: two-space indent, one property per line.</summary>
    internal static string ReindentJsonPrefix(string json)
    {
        var sb = new StringBuilder(json.Length + json.Length / 4);
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                sb.Append(c);
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    break;
                case '{':
                case '[':
                {
                    var closer = c == '{' ? '}' : ']';
                    var j = i + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && json[j] == closer)
                    {
                        sb.Append(c).Append(closer);
                        i = j;
                        break;
                    }
                    sb.Append(c);
                    depth++;
                    sb.Append('\n').Append(' ', depth * 2);
                    break;
                }
                case '}':
                case ']':
                    depth = Math.Max(0, depth - 1);
                    sb.Append('\n').Append(' ', depth * 2).Append(c);
                    break;
                case ',':
                    sb.Append(c).Append('\n').Append(' ', depth * 2);
                    break;
                case ':':
                    sb.Append(": ");
                    break;
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Longest whitespace-free run a note line may carry. PlantUML wraps at spaces only, so this bounds
    /// the width of a note holding a minified payload, a base64 blob or a long URL to roughly
    /// <see cref="MaxLineWidth"/>; longer runs are broken, preferring a punctuation boundary and never
    /// inside a <c>&lt;tag&gt;</c>.
    /// </summary>
    internal const int MaxUnbrokenRunChars = 120;

    internal static string WrapUnbreakableRuns(string text)
    {
        if (text.Length <= MaxUnbrokenRunChars || !HasUnbreakableRun(text))
            return text;

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (HasUnbreakableRun(lines[i]))
                lines[i] = WrapLine(lines[i]);
        }
        return string.Join('\n', lines);
    }

    private static bool HasUnbreakableRun(string text)
    {
        var run = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) run = 0;
            else if (++run > MaxUnbrokenRunChars) return true;
        }
        return false;
    }

    private static string WrapLine(string line)
    {
        var sb = new StringBuilder(line.Length + line.Length / MaxUnbrokenRunChars * 2);
        var runStart = 0;
        for (var i = 0; i <= line.Length; i++)
        {
            if (i < line.Length && !char.IsWhiteSpace(line[i]))
                continue;
            var run = line.AsSpan(runStart, i - runStart);
            if (run.Length <= MaxUnbrokenRunChars)
            {
                sb.Append(run);
            }
            else
            {
                var pos = 0;
                while (run.Length - pos > MaxUnbrokenRunChars)
                {
                    var cut = ChooseCut(run, pos);
                    sb.Append(run[pos..cut]).Append('\n');
                    pos = cut;
                }
                sb.Append(run[pos..]);
            }
            if (i < line.Length) sb.Append(line[i]);
            runStart = i + 1;
        }
        return sb.ToString();
    }

    private static int ChooseCut(ReadOnlySpan<char> run, int pos)
    {
        var hard = pos + MaxUnbrokenRunChars;
        var cut = hard;
        // Prefer a punctuation boundary in the tail of the chunk so JSON/URL pieces stay readable.
        for (var k = hard; k > hard - 24 && k > pos + 1; k--)
        {
            if (",;:}]\"&=)/".Contains(run[k - 1]))
            {
                cut = k;
                break;
            }
        }
        // Never cut inside a <tag>: back up to before an unclosed '<'.
        var open = run[pos..cut].LastIndexOf('<');
        if (open > 0 && run[(pos + open)..cut].IndexOf('>') < 0)
            cut = pos + open;
        // Never strand a creole escape from the character it protects.
        while (cut > pos + 1 && run[cut - 1] == '~') cut--;
        return cut;
    }

    private static void WriteElementWithoutNulls(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    writer.WritePropertyName(property.Name);
                    WriteElementWithoutNulls(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElementWithoutNulls(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string FormatFormUrlEncodedContent(string? content, bool escape = true)
    {
        const string divider = "<font color=\"lightgray\">&";
        return content?
            .Split("&")
            .SelectMany(x =>
            {
                // Escape per chunk, after the split: a `~` and the character it protects must not land
                // either side of a chunk boundary.
                var chunks = x.ChunksUpTo(MaxNoteChunkChars).Select(c => escape ? EscapeCreoleMarkup(c) : c).ToArray();
                if (chunks.Length == 0)
                    return chunks;
                chunks[^1] += divider;
                return chunks;
            })
            .StringJoin(Environment.NewLine)
            .TrimEnd(divider) ?? string.Empty;
    }

    private static IEnumerable<string> BatchGray(string value)
    {
        // Escape after chunking so a `~` never ends up split from the character it protects, and prefix the
        // gray tag after escaping so Kronikol's own markup stays live.
        return value.ChunksUpTo(MaxNoteChunkChars).Select(x => "<color:gray>" + EscapeCreoleMarkup(x));
    }

    private sealed class DiagramBuilder(
        List<RequestResponseLog> tracesForTest,
        string? plantUmlTheme = null,
        int maxEncodedDiagramLength = DefaultMaxEncodedDiagramLength,
        bool sequenceDiagramArrowColors = true,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null)
    {
        private readonly List<PlantUmlResult> _results = [];
        private StringBuilder _currentDiagram = new(CreatePlantUmlPrefix(tracesForTest, 1, plantUmlTheme,
            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides));
        private int _stepNumber = 1;
        private string? _openPartitionLine;
        private string? _cachedEncoded;
        private int _lengthAtLastEncode;
        private int _estimatedHeight;

        // Build a lookup from ServiceName → resolved DependencyCategory
        private readonly Dictionary<string, string?> _serviceCategoryCache = BuildServiceCategoryCache(tracesForTest, serviceTypeOverrides);

        private static Dictionary<string, string?> BuildServiceCategoryCache(
            List<RequestResponseLog> traces,
            Dictionary<string, string>? overrides)
        {
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var trace in traces)
            {
                if (!cache.ContainsKey(trace.ServiceName))
                {
                    // User override takes priority
                    if (overrides?.TryGetValue(trace.ServiceName, out var overrideCategory) == true)
                    {
                        cache[trace.ServiceName] = overrideCategory;
                    }
                    else
                    {
                        // Auto-detect from DependencyCategory on first request targeting this service
                        cache[trace.ServiceName] = traces
                            .Where(t => t.ServiceName == trace.ServiceName && t.DependencyCategory is not null)
                            .Select(t => t.DependencyCategory)
                            .FirstOrDefault();
                    }
                }

                // Also add CallerDependencyCategory entries for caller participants
                if (!cache.ContainsKey(trace.CallerName))
                {
                    if (overrides?.TryGetValue(trace.CallerName, out var callerOverride) == true)
                    {
                        cache[trace.CallerName] = callerOverride;
                    }
                    else
                    {
                        var callerCat = traces
                            .Where(t => t.CallerName == trace.CallerName && t.CallerDependencyCategory is not null)
                            .Select(t => t.CallerDependencyCategory)
                            .FirstOrDefault();
                        if (callerCat is not null)
                            cache[trace.CallerName] = callerCat;
                    }
                }
            }
            return cache;
        }

        /// <summary>Returns the arrow color syntax (e.g. <c>[#E74C3C]</c>) for a given service, or empty if coloring is off.</summary>
        public string GetArrowColor(string serviceName, string? dependencyCategory, string? callerName = null, string? callerDependencyCategory = null)
        {
            if (!sequenceDiagramArrowColors) return "";

            // Use cached category for the service (accounts for overrides and auto-detection)
            var category = _serviceCategoryCache.TryGetValue(serviceName, out var cached) ? cached : dependencyCategory;

            // Fall back to caller's category when the service has no category (e.g. consume events)
            if (string.IsNullOrEmpty(category) && callerName is not null)
                category = _serviceCategoryCache.TryGetValue(callerName, out var callerCached) ? callerCached : callerDependencyCategory;

            var color = DependencyPalette.GetColor(category, dependencyColors);
            return $"[{color}]";
        }

        private readonly PlantUmlStatementGuard _statementGuard = new();

        public void Append(string text) => _currentDiagram.Append(_statementGuard.Apply(text, terminated: false));
        public void AppendLine(string text) => _currentDiagram.AppendLine(_statementGuard.Apply(text, terminated: true));
        public void IncrementStep() => _stepNumber++;
        public bool HasOpenPartition => _openPartitionLine != null;

        public void AddArrowHeight() => _estimatedHeight += EstimatedArrowHeight;

        public void AddNoteHeight(string noteContent)
        {
            if (string.IsNullOrEmpty(noteContent)) return;
            var lineCount = noteContent.Split('\n').Length;
            _estimatedHeight += (lineCount * EstimatedNoteLineHeight) + EstimatedArrowHeight;
        }

        public bool EstimatedHeightExceedsMax => _estimatedHeight > MaxEstimatedDiagramHeight;

        public void OpenPartition(string partitionLine)
        {
            AppendLine(partitionLine);
            _openPartitionLine = partitionLine;
        }

        public void ClosePartition()
        {
            if (_openPartitionLine != null)
            {
                AppendLine("end");
                _openPartitionLine = null;
            }
        }

        private string? _openLoopLine;
        private Guid? _openLoopRequestResponseId;

        /// <summary>A <c>loop</c> fragment is open (collapsed run in progress) — see <see cref="SequenceCollapser"/>.</summary>
        public bool HasOpenLoop => _openLoopLine != null;

        /// <summary>The request/response id whose response closes the open loop, if any.</summary>
        public Guid? OpenLoopRequestResponseId => _openLoopRequestResponseId;

        public void OpenLoop(string loopLine, Guid requestResponseId)
        {
            CloseLoop();
            AppendLine(loopLine);
            AddArrowHeight();
            _openLoopLine = loopLine;
            _openLoopRequestResponseId = requestResponseId;
        }

        public void CloseLoop()
        {
            if (_openLoopLine != null)
            {
                AppendLine("end");
                _openLoopLine = null;
                _openLoopRequestResponseId = null;
            }
        }

        public bool EncodedDiagramExceedsMaxLength
        {
            get
            {
                if (_currentDiagram.Length <= maxEncodedDiagramLength)
                    return false;

                // Only re-encode when the diagram has grown meaningfully since the last check
                if (_cachedEncoded is not null && _currentDiagram.Length - _lengthAtLastEncode < 200)
                    return _cachedEncoded.Length > maxEncodedDiagramLength;

                _cachedEncoded = PlantUmlTextEncoder.Encode(_currentDiagram.ToString());
                _lengthAtLastEncode = _currentDiagram.Length;
                return _cachedEncoded.Length > maxEncodedDiagramLength;
            }
        }

        public void FinishAndStartNewDiagram()
        {
            var partitionToReopen = _openPartitionLine;
            var loopToReopen = _openLoopLine;
            if (_openLoopLine != null)
                AppendLine("end");
            if (_openPartitionLine != null)
                AppendLine("end");

            AppendLine("@enduml");
            var plainText = _currentDiagram.ToString();
            var encodedPlantUml = PlantUmlTextEncoder.Encode(plainText);
            _cachedEncoded = null;
            _lengthAtLastEncode = 0;
            _estimatedHeight = 0;
            _statementGuard.Reset();
            _results.Add(new PlantUmlResult(plainText, encodedPlantUml));
            _currentDiagram = new StringBuilder(CreatePlantUmlPrefix(tracesForTest, _stepNumber, plantUmlTheme,
                sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides));

            if (partitionToReopen != null)
            {
                AppendLine(partitionToReopen);
                _openPartitionLine = partitionToReopen;
            }

            if (loopToReopen != null)
            {
                AppendLine(loopToReopen);
                _openLoopLine = loopToReopen;
            }
        }

        public PlantUmlResult[] GetResults() => [.. _results];
    }

    private record PlantUmlResult(string PlantUml, string PlantUmlEncoded)
    {
        public string GetPlantUmlImageTag(string plantUmlServerRendererUrl, bool lazyLoad = true) =>
            $"<img{(lazyLoad ? " loading=\"lazy\"" : "")} src=\"{plantUmlServerRendererUrl.TrimEnd('/')}/{PlantUmlEncoded}\">";
    }

    /// <summary>
    /// Contains the generated PlantUML source text for a specific test execution.
    /// </summary>
    public record PlantUmlForTest(
        string TestId,
        string TestName,
        IEnumerable<(string PlainText, string PlantUmlEncoded)> PlantUmls,
        IEnumerable<RequestResponseLog> Traces,
        string[] ImageTags);
}