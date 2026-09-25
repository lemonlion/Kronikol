using System.Buffers;
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
    /// <summary>
    /// The built-in note wrap width — how wide a note body is drawn before PlantUML breaks it at a
    /// space. <c>ReportConfigurationOptions.DiagramNoteWrapWidth</c> overrides it per report; this is
    /// the value every report shipped with, and the one that keeps existing output byte-identical.
    /// </summary>
    internal const int DefaultNoteWrapWidth = DiagramWidth.DefaultWrapWidthPx;
    /// <summary>
    /// Slice size for a genuinely form-url-encoded body, which has no whitespace for the engine to
    /// wrap at. It must stay under the note wrap width at ~9px/char: a chunk the engine has to break
    /// itself is broken mid-line, which can cut one of Kronikol's own inline colour tags in half.
    /// <c>DiagramNoteWrapWidth</c> is floored at <see cref="DiagramWidth.MinNoteWrapWidthPx"/> so that
    /// relationship holds at every configurable width, which is what lets this stay a constant.
    /// </summary>
    private const int MaxNoteChunkChars = 80;

    /// <summary>Exposes <see cref="MaxNoteChunkChars"/> to the test that pins it against the width floor.</summary>
    internal static int MaxNoteChunkCharsForTests => MaxNoteChunkChars;
    private const string EventNoteClass = "eventNote";
    public const int DefaultMaxEncodedDiagramLength = 2000;
    private const int MaxResponseNoteChunkLength = 15_000;
    private const int MaxEstimatedDiagramHeight = 12_000;
    private const int EstimatedArrowHeight = 45;
    private const int EstimatedNoteLineHeight = 18;

    /// <summary>
    /// How wide the participant row may grow before a diagram is split for width. Measured on real
    /// PlantUML, a sequence diagram of short-named participants grows about 78px each - 50 services
    /// drew 4035px and 60 drew 4817px, in ONE diagram, because encoded length and estimated height
    /// were the only split guards and neither is reached by a test that is merely wide. The budget
    /// sits below <see cref="DiagramWidth.PlantUmlLimitSize"/> to leave room for the arrow labels and
    /// notes drawn between the lifelines.
    /// </summary>
    private const int MaxParticipantRowWidthPx = 3400;

    /// <summary>Per-character contribution of a participant's drawn name to the row width.</summary>
    private const int ParticipantPxPerChar = 7;

    /// <summary>Fixed per-participant cost: the box padding and the gap to the next lifeline.</summary>
    private const int ParticipantBoxPaddingPx = 20;

    /// <summary>No participant box is narrower than this however short its name is.</summary>
    private const int MinParticipantWidthPx = 80;

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
        int? maxArrowsPerDiagram = null,
        int diagramNoteWrapWidth = DefaultNoteWrapWidth)
    {
        excludedHeaders ??= DefaultExcludedHeaders;
        DiagramWidth.ValidateNoteWrapWidth(diagramNoteWrapWidth);

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
                maxArrowsPerDiagram,
                diagramNoteWrapWidth);
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
        int? maxArrowsPerDiagram = null,
        int diagramNoteWrapWidth = DefaultNoteWrapWidth)
    {
        // Collapse poll/retry bursts and apply the arrow cap before rendering (no-op when both are off).
        var collapsed = SequenceCollapser.Apply(tracesForTest, collapseConsecutiveIdenticalCalls, collapseThreshold, maxArrowsPerDiagram);
        tracesForTest = collapsed.Traces;
        var omittedPairs = collapsed.OmittedPairs;
        if (tracesForTest.Count == 0)
            return [];

        var builder = new DiagramBuilder(tracesForTest, plantUmlTheme, clientSideSplitting ? int.MaxValue : maxEncodedDiagramLength,
            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides,
            diagramNoteWrapWidth);
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

        // The last real (non-marker) trace before the StartAction marker. A narration marker
        // (step bar, assertion note, row band) past this point belongs to the action that is
        // about to begin and closes the setup partition; one at or before it is setup content
        // and renders inside the partition.
        var lastSetupTraceIndex = -1;
        for (var i = actionStartIndex - 1; i >= 0; i--)
        {
            var t = tracesForTest[i];
            if (!t.IsOverrideStart && !t.IsOverrideEnd && !t.IsActionStart)
            {
                lastSetupTraceIndex = i;
                break;
            }
        }

        var traceIndex = -1;
        foreach (var trace in tracesForTest)
        {
            traceIndex++;
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
                    // A Custom override marks the setup/action boundary and always closes the
                    // partition. A narration marker is scenario content: with real setup traces
                    // still to come it renders inside the partition (opening it if needed —
                    // BDD adapters emit the GIVEN step bar before the first setup call); past
                    // the last real setup trace it belongs to the imminent action and closes
                    // the partition like the boundary override does.
                    var isNarrationMarker = trace.MarkerKind
                        is DiagramMarkerKind.Step or DiagramMarkerKind.Assertion or DiagramMarkerKind.Row;
                    if (!isNarrationMarker || traceIndex > lastSetupTraceIndex)
                    {
                        builder.ClosePartition();
                        setupPartitionClosed = true;
                    }
                    else if (hasSetupTraces && !builder.HasOpenPartition)
                    {
                        builder.OpenPartition(partitionLine);
                    }
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
            builder.UseParticipants(callerShortName, trace.CallerName, serviceShortName, trace.ServiceName);
            var content = effectiveContent ?? string.Empty;

            switch (trace.Type)
            {
                case RequestResponseType.Request when trace.IsUserAction:
                {
                    // A user action: one arrow from the actor to the service, labelled with the action,
                    // no response arrow. Its detail (full title / locator) is the note.
                    // The label is the tracker's own words for the step, which can quote a locator or the text
                    // typed into a field: loader markup in it is escaped (EscapeLoaderMarkup), the rest is not.
                    var actionLabel = EscapeLoaderMarkup((effectiveMethod.Value?.ToString() ?? "action").Replace("\r", string.Empty).Replace("\n", "\\n"));
                    var actionCategory = trace.CallerDependencyCategory ?? Constants.DependencyCategories.User;
                    var actionColor = builder.GetArrowColor(trace.CallerName, actionCategory, trace.CallerName, actionCategory);
                    var actionPrefix = $"{callerShortName} -{actionColor}> {serviceShortName}: ";
                    // A long Playwright locator or action description is one message statement, and the
                    // engine abandons the whole diagram past 2000 characters. Wrapped before it is
                    // capped, not after: the `\n` escapes are two characters each and count against the
                    // cap. This is the one message label in the codebase that is neither chunked nor
                    // constant, and real Java PlantUML does not wrap arrow labels at any width, so the
                    // breaks have to be in the source (DiagramWidth).
                    actionLabel = DiagramWidth.Wrap(actionLabel, DiagramWidth.MaxLabelLineChars);
                    actionLabel = PlantUmlStatementLimits.TruncateLabel(
                        actionLabel, PlantUmlStatementLimits.MaxMessageStatementChars - actionPrefix.Length);
                    builder.AppendLine($"{actionPrefix}{actionLabel}");
                    builder.AddArrowHeight();

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        // The UI detail is a note body, but it never passed through FormatNoteContent,
                        // so it got neither the creole neutralisation every request/response note has
                        // nor the unbreakable-run bound — a locator with no whitespace in it drew the
                        // diagram thousands of pixels wide, and a `**` in a detail string styled the
                        // text instead of showing.
                        var actionNote = WrapUnbreakableRuns(EscapeCreoleMarkup(
                            TruncateNoteContent(content, truncateNotesAfterLines)));
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

            // The participant guard is deliberately NOT gated on clientSideSplitting, unlike the other
            // two. Encoded length and estimated height both change with note state, which is why
            // BrowserJs leaves them to the browser to re-derive after every toggle; the number of
            // participants a diagram draws does not, so splitting on it server-side is stable and is
            // the only thing that bounds the default renderer's width. Measured before it existed: a
            // test touching 60 services drew one 4 817px diagram, and 120 drew 6 382px — with names as
            // short as "Service37" and no split guard reached, because the diagram was neither long
            // nor tall, only wide.
            var splitForWidth = builder.ParticipantRowExceedsMaxWidth;
            var splitForSize = !clientSideSplitting
                && (builder.EncodedDiagramExceedsMaxLength || builder.EstimatedHeightExceedsMax);
            if ((splitForWidth || splitForSize) && !builder.HasOpenLoop && trace != lastTrace)
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
    internal static string AppendFullPathToNote(string noteContent, string pathAndQuery)
    {
        // Marked after escaping, so the marker stays unescaped and is provably Kronikol's own. One
        // URL chopped into 80-character display lines rejoins to the URL.
        var chunks = pathAndQuery.ChunksUpTo(MaxNoteChunkChars).Select(EscapeCreoleMarkup).ToArray();
        for (var i = 0; i < chunks.Length - 1; i++) chunks[i] += DiagramWidth.JoinMarker;
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
    /// <para>
    /// A <c>&lt;</c> opening an OpenIconic icon (<c>&lt;&amp;name&gt;</c>), an emoji (<c>&lt;:name:&gt;</c>)
    /// or a sprite (<c>&lt;$name&gt;</c>) is escaped too, see <see cref="EscapeLoaderMarkup"/>: the first two
    /// make the engine load a bundle the report's renderers cannot, and a sprite drops the text.
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

            if (c == '<' && i + 1 < line.Length
                && (IsCreoleTagStart(line[i + 1]) || (IsLoaderMarkupStart(line[i + 1]) && !IsTildeEscaped(line, i))))
                sb.Append('~');

            sb.Append(c);
        }
    }

    /// <summary>
    /// Puts a <c>~</c> before every <c>&lt;</c> that opens an OpenIconic icon (<c>&lt;&amp;name&gt;</c>), an
    /// emoji (<c>&lt;:name:&gt;</c>) or a sprite (<c>&lt;$name&gt;</c>), and before nothing else.
    /// <para>
    /// The engine loads OpenIconic and emoji by adding a script element to its page, which neither the
    /// report's render worker nor the Node renderer can answer: before 3.29.6 such a diagram was never
    /// drawn, and neither was anything rendered after it by the same engine. A sprite reference loads
    /// nothing but is dropped from the text. For text Kronikol copies in from a test (step names, test
    /// names, assertion messages, UI action descriptions, span names), which otherwise reaches PlantUML as
    /// written: its other markup keeps styling it, as it always has. Payloads are escaped in full by
    /// <see cref="EscapeCreoleMarkup"/>, which applies the same rule.
    /// </para>
    /// <para>
    /// A <c>&lt;</c> that the text already escapes itself is left alone. PlantUML reads a run of tildes in
    /// pairs, and a pair paints as two tildes and escapes nothing, so one more <c>~</c> in front of
    /// <c>~&lt;&amp;</c> would make the markup live again.
    /// </para>
    /// </summary>
    internal static string EscapeLoaderMarkup(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;

        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' && i + 1 < text.Length && IsLoaderMarkupStart(text[i + 1]) && !IsTildeEscaped(text, i))
            {
                sb ??= new StringBuilder(text.Length + 8).Append(text, 0, i);
                sb.Append('~');
            }
            sb?.Append(text[i]);
        }
        return sb?.ToString() ?? text;
    }

    /// <summary><c>&lt;&amp;</c> is an OpenIconic icon, <c>&lt;:</c> an emoji, <c>&lt;$</c> a sprite.</summary>
    private static bool IsLoaderMarkupStart(char c) => c is '&' or ':' or '$';

    /// <summary>Whether an odd run of tildes stands right before position <paramref name="at"/>, which escapes it.</summary>
    private static bool IsTildeEscaped(ReadOnlySpan<char> text, int at)
    {
        var tildes = 0;
        while (at - tildes - 1 >= 0 && text[at - tildes - 1] == '~')
            tildes++;
        return tildes % 2 == 1;
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
            // A thrown status ("!HttpRequestException", a failed send) is drawn as recorded: title-casing would
            // split the type name into words. See FailedSend.
            var rawStatus = trace.StatusCode?.Value?.ToString();
            var status = rawStatus is { Length: > 1 } && rawStatus[0] == '!' ? rawStatus : rawStatus?.Titleize();
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
        Dictionary<string, string>? serviceTypeOverrides = null,
        int noteWrapWidth = DefaultNoteWrapWidth)
    {
        var entitiesPlantUml = CreateEntitiesPlantUml(tracesForTest, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides);
        var themeDirective = !string.IsNullOrWhiteSpace(plantUmlTheme) ? $"!theme {plantUmlTheme}\n" : "";
        return $"""

                @startuml
                {themeDirective}!pragma teoz true
                {AddEventStyling(tracesForTest)}
                {AddMarkerNoteStyling(tracesForTest)}
                skinparam wrapWidth {noteWrapWidth}
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

    /// <summary>
    /// The styles for the injected marker notes, emitted only when a diagram carries one: the
    /// assertion note's shape, and the styled step-bar body (<c>&lt;&lt;stepBody&gt;&gt;</c>) whose
    /// black-bar/white-text colours live here rather than in inline tags — <c>&lt;color:white&gt;</c>
    /// styles only the first display line of a note, and inline colour tags put the statement under
    /// the coloured-bar crash cap (<see cref="PlantUmlStatementLimits.MaxColouredNoteBarChars"/>).
    /// </summary>
    private static string AddMarkerNoteStyling(List<RequestResponseLog> tracesForTest)
    {
        var parts = new List<string>(2);

        if (tracesForTest.Any(x => x.PlantUml is not null && x.PlantUml.Contains($"<<{AssertionNoteClass}>>")))
            parts.Add($$"""

                <style>
                 .{{AssertionNoteClass}} {
                     FontSize 11
                     RoundCorner 5
                 }
                </style>
                """.TrimStart());

        if (tracesForTest.Any(x => x.PlantUml is not null && x.PlantUml.Contains($"<<{StepBarPlantUml.BodyNoteClass}>>")))
            parts.Add($$"""

                <style>
                 .{{StepBarPlantUml.BodyNoteClass}} {
                     BackgroundColor black
                     FontColor white
                     LineColor white
                 }
                </style>
                """.TrimStart());

        return string.Join("\n", parts);
    }

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
                    .Append(WrapParticipantName(pureCaller))
                    .Append("\" as ")
                    .Append(pureCallerAlias)
                    .AppendLine(pureCallerColor);
            }
            else
            {
                sb.Append("actor \"")
                    .Append(WrapParticipantName(pureCaller))
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
                        .Append(WrapParticipantName(trace.CallerName))
                        .Append("\" as ")
                        .Append(callerShortName)
                        .AppendLine(callerColorSuffix);
                }
                else
                {
                    // Callers without a category: use actor (first) or entity (subsequent)
                    sb.Append(actorDefined ? "entity" : "actor")
                        .Append(" \"")
                        .Append(WrapParticipantName(trace.CallerName))
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
                    .Append(WrapParticipantName(trace.ServiceName))
                    .Append("\" as ")
                    .Append(serviceShortName)
                    .AppendLine(colorSuffix);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// A participant's display name, broken onto lines of at most
    /// <see cref="DiagramWidth.MaxNameLineChars"/> characters. Sequence participant boxes never wrap —
    /// not at whitespace, not at <c>skinparam wrapWidth</c> — so a long <c>ServiceName</c> or
    /// <c>CallerName</c> (a host, a fully-qualified type name, a connection descriptor) is drawn on one
    /// line however wide that makes the diagram: measured, an 800-character name drew 5 650 px, and the
    /// same name broken every 80 drew 610. Unchanged, byte for byte, for every name short enough to
    /// fit, which is all of them in practice.
    /// </summary>
    private static string WrapParticipantName(string name) =>
        DiagramWidth.Wrap(name, DiagramWidth.MaxNameLineChars);

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
            // Only a body that really is form-url-encoded gets the `&` dividers and the chunking
            // that go with them (see LooksLikeFormUrlEncoded). Everything else — SQL, XML, CSV,
            // plain text — reaches the note as captured, on the SAME path a response body takes,
            // so the two agree for identical bytes. Width is still bounded downstream:
            // WrapUnbreakableRuns breaks runs over MaxUnbrokenRunChars, and `skinparam wrapWidth`
            // wraps the rest at spaces when the note is drawn.
            if (type is RequestResponseType.Response || !LooksLikeFormUrlEncoded(content))
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
    /// A JSON body carrying a capture-cap footnote is not parseable as a whole, so
    /// <see cref="TryFormatAsJson"/> gives up and the note used to get the raw one-line payload.
    /// Two classes of footnote reach here:
    /// <list type="bullet">
    /// <item>a <em>cut</em> body — TcpTap/ProxyTap/<see cref="Tracking.RequestResponseLogger"/> append
    /// <c>…truncated (N chars total)</c>, the RESP decoder <c>…[bulk string truncated: …]</c>. What is
    /// left is only a prefix, so it is re-indented with a string-aware brace walker (no null stripping
    /// — there is no document to walk).</item>
    /// <item>a <em>row/document cap</em> — the SQL, Spanner and MongoDB readers append
    /// <c>... (N more rows not shown)</c> and friends after a COMPLETE document. That document is
    /// re-serialized through <see cref="TryFormatAsJson"/>, so a capped payload indents and drops
    /// nulls exactly like the uncapped one above it.</item>
    /// </list>
    /// Either way the marker keeps its own line; anything that is not a valid JSON <em>prefix</em>
    /// (a real non-JSON body that happens to start with a brace) is left to the plain-text path.
    /// </summary>
    internal static string? TryFormatTruncatedJson(string? content)
    {
        if (content is null || content.Length < 2 || (content[0] != '{' && content[0] != '['))
            return null;

        var (body, marker) = SplitTruncationMarker(content);
        if (body.Length < 2 || !IsJsonPrefix(body))
            return null;

        var indented = TryFormatAsJson(body) ?? ReindentJsonPrefix(body);
        return marker is null ? indented : indented + "\n" + marker;
    }

    [GeneratedRegex(@"(?:\r?\n\r?\n…truncated \(\d+ chars total\)|\s…\[bulk string truncated: [^\]]*\]|\r?\n\.\.\. \(\d+ more(?: (?:rows|documents) not shown)?\))\s*$")]
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
    /// the note wrap width; longer runs are broken, preferring a punctuation boundary and never
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
                    // Every cut here is mid-token, so the marker is the no-space kind: a reader
                    // copying this note back out gets the run in one piece again.
                    sb.Append(run[pos..cut]).Append(DiagramWidth.JoinMarker).Append('\n');
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

    private static readonly SearchValues<char> FormUrlEncodedDisqualifiers = SearchValues.Create(" \t\r\n");

    /// <summary>
    /// Whether a request body is form-url-encoded — one physical line of percent-encoded
    /// <c>k=v</c> pairs. A space in such a body is always encoded (<c>+</c> or <c>%20</c>), so any
    /// raw newline, space or tab means the body is text — SQL, XML, CSV, a GraphQL document — and
    /// must reach the note as captured. Getting this wrong in the permissive direction is what
    /// sliced multi-line SQL into 80-character pieces mid-identifier and wove grey <c>&amp;</c>
    /// dividers through bitwise operators and XML entities; getting it wrong in the other
    /// direction only costs a real form body its dividers.
    /// </summary>
    internal static bool LooksLikeFormUrlEncoded(string? content) =>
        !string.IsNullOrEmpty(content)
        && content.Contains('=')
        && content.AsSpan().IndexOfAny(FormUrlEncodedDisqualifiers) < 0;

    internal static string FormatFormUrlEncodedContent(string? content, bool escape = true)
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
                // Only the chunking INSIDE one field is marked. The `&` divider is a deliberate,
                // visible decomposition of the captured body into its fields — a reader is meant to
                // see those as separate lines — while an 80-character cut through a value is the
                // arbitrary break that corrupts what they copy.
                for (var i = 0; i < chunks.Length - 1; i++) chunks[i] += DiagramWidth.JoinMarker;
                chunks[^1] += divider;
                return chunks;
            })
            .StringJoin(Environment.NewLine)
            .TrimEnd(divider) ?? string.Empty;
    }

    internal static IEnumerable<string> BatchGray(string value)
    {
        // Escape after chunking so a `~` never ends up split from the character it protects, and prefix the
        // gray tag after escaping so Kronikol's own markup stays live.
        var chunks = value.ChunksUpTo(MaxNoteChunkChars).Select(x => "<color:gray>" + EscapeCreoleMarkup(x)).ToArray();
        // Each continuation carries the gray tag again so it still draws gray; a consumer strips those
        // per line BEFORE rejoining, which is why the rejoin itself needs no knowledge of them.
        for (var i = 0; i < chunks.Length - 1; i++) chunks[i] += DiagramWidth.JoinMarker;
        return chunks;
    }

    /// <summary>
    /// A participant declaration line of a diagram prefix: the shape keyword, the quoted display name
    /// (which may now carry <c>\n</c> breaks), and the alias the body refers to it by.
    /// </summary>
    [GeneratedRegex("""^(?:actor|entity|participant|database|collections|queue|control|boundary) "(?:[^"]*)" as (?<alias>\w+)""",
        RegexOptions.Multiline)]
    private static partial Regex ParticipantDeclarationRegex();

    /// <summary>
    /// The prefix with the participant declarations this fragment does not draw removed.
    /// <para>
    /// Splitting a diagram is no width bound while every fragment re-declares every participant the
    /// whole test touched, because <see cref="CreatePlantUmlPrefix"/> builds from the full trace list —
    /// each fragment comes out as wide as the unsplit diagram was. <paramref name="used"/> is what the
    /// generator actually drew arrows for; anything else is kept only if the body names it as a whole
    /// word, which covers the constructs the trace loop does not register — an explicit
    /// <c>note left of X</c>, an <c>activate</c>, and user-authored override PlantUML.
    /// </para>
    /// <para>
    /// Returns the prefix unchanged whenever nothing would be removed, which is the single-fragment
    /// case — that is, every ordinary diagram, byte for byte.
    /// </para>
    /// </summary>
    internal static string DeclareOnlyUsedParticipants(string prefix, string body, IReadOnlySet<string> used)
    {
        var declarations = ParticipantDeclarationRegex().Matches(prefix);
        if (declarations.Count == 0 || used.Count == 0)
            return prefix;

        var unusedCandidates = declarations
            .Where(m => !used.Contains(m.Groups["alias"].Value))
            .ToArray();
        if (unusedCandidates.Length == 0)
            return prefix;

        var bodyWords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in IdentifierRegex().EnumerateMatches(body))
            bodyWords.Add(body.Substring(word.Index, word.Length));

        var drop = unusedCandidates
            .Where(m => !bodyWords.Contains(m.Groups["alias"].Value))
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (drop.Count == 0)
            return prefix;

        var kept = new StringBuilder(prefix.Length);
        foreach (var line in prefix.Split('\n'))
        {
            if (drop.Contains(line.TrimEnd('\r'))) continue;
            if (kept.Length > 0) kept.Append('\n');
            kept.Append(line);
        }

        return kept.ToString();
    }

    [GeneratedRegex(@"\w+")]
    private static partial Regex IdentifierRegex();

    private sealed class DiagramBuilder(
        List<RequestResponseLog> tracesForTest,
        string? plantUmlTheme = null,
        int maxEncodedDiagramLength = DefaultMaxEncodedDiagramLength,
        bool sequenceDiagramArrowColors = true,
        bool sequenceDiagramParticipantColors = false,
        Dictionary<string, string>? dependencyColors = null,
        Dictionary<string, string>? serviceTypeOverrides = null,
        int noteWrapWidth = DefaultNoteWrapWidth)
    {
        private readonly List<PlantUmlResult> _results = [];
        // The prefix is kept alongside the buffer it opens so that FinishAndStartNewDiagram can tell
        // prefix from body and re-issue the participant declarations for just this fragment. An
        // instance field initialiser cannot read another field, so the (pure, deterministic) prefix
        // builder is simply called twice for the first diagram; every later one reuses the string.
        private string _currentPrefix = CreatePlantUmlPrefix(tracesForTest, 1, plantUmlTheme,
            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides, noteWrapWidth);
        private StringBuilder _currentDiagram = new(CreatePlantUmlPrefix(tracesForTest, 1, plantUmlTheme,
            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides, noteWrapWidth));
        private int _stepNumber = 1;
        private string? _openPartitionLine;
        private string? _cachedEncoded;
        private int _lengthAtLastEncode;
        private int _estimatedHeight;

        /// <summary>The participants the diagram currently being built actually draws an arrow for.</summary>
        private readonly HashSet<string> _currentParticipants = new(StringComparer.Ordinal);

        private int _estimatedParticipantRowWidth;

        /// <summary>
        /// Records that this diagram draws a participant — for the width guard, and so that the
        /// finished fragment declares only the participants it uses. Each new one widens the
        /// participant row by roughly its own box: measured, short names ("Service37") cost about
        /// 78 pixels each, and the estimate is deliberately a little pessimistic so the guard fires
        /// before the diagram reaches the limit rather than after.
        /// </summary>
        public void UseParticipant(string alias, string displayName)
        {
            if (string.IsNullOrEmpty(alias) || !_currentParticipants.Add(alias)) return;
            var drawnChars = Math.Min(displayName?.Length ?? 0, DiagramWidth.MaxNameLineChars);
            _estimatedParticipantRowWidth += Math.Max(MinParticipantWidthPx, drawnChars * ParticipantPxPerChar + ParticipantBoxPaddingPx);
        }

        public void UseParticipants(string callerAlias, string callerName, string serviceAlias, string serviceName)
        {
            UseParticipant(callerAlias, callerName);
            UseParticipant(serviceAlias, serviceName);
        }

        /// <summary>
        /// Whether this diagram has taken on more participants than it can draw inside
        /// <see cref="DiagramWidth.PlantUmlLimitSize"/>. Sequence-diagram width accumulates per
        /// participant and nothing else in the generator bounds it: a test that touches every service
        /// in a large system is neither long nor tall, only wide, so no existing guard ever fires.
        /// </summary>
        public bool ParticipantRowExceedsMaxWidth => _estimatedParticipantRowWidth > MaxParticipantRowWidthPx;

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
            // Splitting is no width bound on its own: the prefix is built from the whole test's traces,
            // so without this every fragment re-declares every participant the test ever touched and
            // each one is as wide as the unsplit diagram was. The body is left exactly as it was built
            // (so the encoded-length guard above measured the same bytes it always did) and only the
            // participant declarations in the prefix are narrowed to what this fragment draws.
            var body = _currentDiagram.ToString(_currentPrefix.Length, _currentDiagram.Length - _currentPrefix.Length);
            var plainText = DeclareOnlyUsedParticipants(_currentPrefix, body, _currentParticipants) + body;
            var encodedPlantUml = PlantUmlTextEncoder.Encode(plainText);
            _cachedEncoded = null;
            _lengthAtLastEncode = 0;
            _estimatedHeight = 0;
            _currentParticipants.Clear();
            _statementGuard.Reset();
            _results.Add(new PlantUmlResult(plainText, encodedPlantUml));
            _currentPrefix = CreatePlantUmlPrefix(tracesForTest, _stepNumber, plantUmlTheme,
                sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides, noteWrapWidth);
            _currentDiagram = new StringBuilder(_currentPrefix);

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