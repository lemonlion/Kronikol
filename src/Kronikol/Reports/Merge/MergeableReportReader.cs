using System.Globalization;
using System.Net;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Reports.Merge;

/// <summary>
/// Parses an enriched "mergeable" test-run report JSON file (produced with
/// <see cref="ReportConfigurationOptions.GenerateMergeableData"/>) back into a <see cref="MergeableReport"/>.
/// </summary>
public static class MergeableReportReader
{
    /// <summary>
    /// The mergeable superset's own shape version — the one <c>GenerateMergeableReportJson</c> stamps.
    /// A constant rather than a literal in two places, because the writer and the reader disagreeing
    /// about it is precisely the defect the version exists to catch.
    /// </summary>
    public const int MergeableFormatVersion = 1;

    /// <summary>Reads and parses a mergeable report from a file path.</summary>
    public static MergeableReport ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses a mergeable report from a JSON string.</summary>
    public static MergeableReport Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("features", out _))
            throw new FormatException("Not a recognised Kronikol test-run report (missing 'features').");

        if (!root.TryGetProperty("mergeableFormatVersion", out var mergeableVersion) && !root.TryGetProperty("componentRelationships", out _))
            throw new FormatException(
                "This report was not produced with GenerateMergeableData enabled, so it lacks the data needed to " +
                "reconstruct a full combined report. Re-run the tests with ReportConfigurationOptions.GenerateMergeableData = true.");

        // The same gate `kronikol query` applies, and it was missing here — so a shard declaring a version
        // this build does not understand was read anyway, and the merge then re-stamped its output as
        // version 1, laundering the unknown format past the gate for every reader downstream.
        if (mergeableVersion.ValueKind is not JsonValueKind.Undefined)
        {
            if (mergeableVersion.ValueKind != JsonValueKind.Number || !mergeableVersion.TryGetInt32(out var declared))
                throw new FormatException(
                    "This report declares a mergeableFormatVersion that is not a number, so it is not a mergeable " +
                    "report this build can read.");

            if (declared != MergeableFormatVersion)
                throw new FormatException(
                    $"This report declares mergeableFormatVersion {declared}; this build of Kronikol writes and reads " +
                    $"{MergeableFormatVersion}. Upgrade Kronikol to merge it.");
        }

        // The report's own shape version, gated for the same reason.
        if (root.TryGetProperty("formatVersion", out var formatVersion))
        {
            if (formatVersion.ValueKind != JsonValueKind.Number || !formatVersion.TryGetInt32(out var shape))
                throw new FormatException("This report declares a formatVersion that is not a number.");

            if (shape != ReportGenerator.ReportFormatVersion)
                throw new FormatException(
                    $"This report declares formatVersion {shape}; this build of Kronikol understands " +
                    $"{ReportGenerator.ReportFormatVersion}. Upgrade Kronikol to merge it.");
        }

        var features = new List<Feature>();
        var diagrams = new List<DiagramAsCode>();
        var interactions = new List<RequestResponseLog>();
        var stepPaths = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        var annotations = new Dictionary<string, List<ReportGenerator.ScenarioAnnotation>>(StringComparer.Ordinal);
        var defaultedResults = new List<string>();
        var notes = new ParseNotes();

        foreach (var fe in EnumerateArray(root, "features"))
        {
            var scenarios = new List<Scenario>();
            foreach (var se in EnumerateArray(fe, "scenarios"))
            {
                var scenario = ReadScenario(se, notes);
                scenarios.Add(scenario);

                if (!DeclaresAResult(se))
                    defaultedResults.Add(scenario.DisplayName ?? scenario.Id);

                if (se.TryGetProperty("diagrams", out var diags) && diags.ValueKind == JsonValueKind.Array)
                    foreach (var d in diags.EnumerateArray())
                        diagrams.Add(new DiagramAsCode(scenario.Id, "", d.GetString() ?? ""));

                ReadInteractions(se, scenario, interactions, stepPaths, notes);
                ReadAnnotations(se, scenario.Id, annotations, notes);
            }

            features.Add(new Feature
            {
                DisplayName = GetString(fe, "name") ?? "",
                Endpoint = GetString(fe, "endpoint"),
                Description = GetString(fe, "description"),
                SourceFile = GetString(fe, "sourceFile"),
                Labels = ReadStringArray(fe, "labels"),
                Scenarios = scenarios.ToArray()
            });
        }

        return new MergeableReport
        {
            KronikolVersion = GetString(root, "kronikolVersion") ?? "",
            Suite = GetString(root, "suite"),
            StartTime = ReadDate(root, "startTime"),
            EndTime = ReadDate(root, "endTime"),
            Features = features.ToArray(),
            Diagrams = diagrams.ToArray(),
            ComponentRelationships = ReadRelationships(root),
            InternalFlowSegments = ReadObjectMap(root, "internalFlowSegments"),
            WholeTestFlow = ReadWholeTestFlow(root),
            WholeTestVisualization = ReadEnum(GetString(root, "wholeTestVisualization"), WholeTestFlowVisualization.None),
            CiMetadata = ReadCiMetadata(root),
            Environment = ReadEnvironment(root),
            Interactions = interactions.ToArray(),
            StepPaths = stepPaths,
            Annotations = annotations,
            Diagnostics = [.. ReadDiagnostics(root), .. DefaultedResultDiagnostics(defaultedResults), .. notes.Diagnostics()]
        };
    }

    /// <summary>
    /// The values a shard carried that this build could not read and had to replace with a default,
    /// gathered so that each kind is reported once with the values that were seen.
    /// </summary>
    /// <remarks>
    /// The scenario's <c>result</c> was the first of these to be said; a step's <c>status</c>, an
    /// annotation's <c>kind</c> and an interaction's <c>type</c> degraded the same way in the same silence
    /// - a step marked with a status a newer Kronikol writes showed as passed, an annotation of a kind this
    /// build does not know became <see cref="DiagramMarkerKind.Custom"/>, and an interaction whose type
    /// could not be read was counted as a request. The defaults stay, because a shard is not refused over
    /// one field; what changes is that the merged report now carries a diagnostic for each, so a reader
    /// can tell "this build's guess" from "what the run recorded".
    /// </remarks>
    private sealed class ParseNotes
    {
        public List<string> StepStatuses { get; } = [];
        public List<string> AnnotationKinds { get; } = [];
        public List<string> InteractionTypes { get; } = [];

        public IEnumerable<DiagnosticEntry> Diagnostics()
        {
            if (StepStatuses.Count > 0)
                yield return new DiagnosticEntry(
                    DiagnosticKind.ResultDefaulted,
                    $"{StepStatuses.Count} step(s) in a merged shard recorded a status this build does not understand and are shown "
                    + $"without one. Seen: {Seen(StepStatuses)}.");

            if (AnnotationKinds.Count > 0)
                yield return new DiagnosticEntry(
                    DiagnosticKind.Other,
                    $"{AnnotationKinds.Count} annotation(s) in a merged shard carry a kind this build does not understand and were "
                    + $"read as {DiagramMarkerKind.Custom}. Seen: {Seen(AnnotationKinds)}.");

            if (InteractionTypes.Count > 0)
                yield return new DiagnosticEntry(
                    DiagnosticKind.Other,
                    $"{InteractionTypes.Count} interaction(s) in a merged shard recorded a type this build does not understand and "
                    + $"were read as {RequestResponseType.Request}. Seen: {Seen(InteractionTypes)}.");
        }

        private static string Seen(List<string> values) =>
            string.Join(", ", values.Distinct(StringComparer.Ordinal).Take(3).Select(v => $"\"{v}\""));
    }

    /// <summary>
    /// Whether the scenario states a result this build understands.
    /// </summary>
    /// <remarks>
    /// A missing or unparseable <c>result</c> is read as <see cref="ExecutionResult.Passed"/>, which is
    /// the compatible default and the right one - but it was applied in silence, so a shard that said
    /// nothing about how a scenario ended, or said something this build does not recognise, merged as a
    /// pass indistinguishable from a real one. That is the same trap
    /// <see cref="DiagnosticKind.ResultDefaulted"/> exists to close on the ingestion side, and the merge
    /// reaches it by a different door: a third-party writer, a hand-edited file, or version skew between
    /// the Kronikol that wrote the shard and the one merging it.
    /// </remarks>
    private static bool DeclaresAResult(JsonElement se) =>
        GetString(se, "result") is { } text && Enum.TryParse<ExecutionResult>(text, ignoreCase: true, out _);

    private static IEnumerable<DiagnosticEntry> DefaultedResultDiagnostics(List<string> scenarios)
    {
        if (scenarios.Count == 0)
            yield break;

        yield return new DiagnosticEntry(
            DiagnosticKind.ResultDefaulted,
            $"{scenarios.Count} scenario(s) in a merged shard recorded no result this build understands and were "
            + $"reported as {ExecutionResult.Passed}. First: {string.Join(", ", scenarios.Take(3))}.");
    }

    private static Scenario ReadScenario(JsonElement se, ParseNotes notes) => new()
    {
        Id = GetString(se, "id") ?? "",
        DisplayName = GetString(se, "name") ?? "",
        Description = GetString(se, "description"),
        Result = ReadEnum(GetString(se, "result"), ExecutionResult.Passed),
        Duration = se.TryGetProperty("durationSeconds", out var d) && d.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(d.GetDouble())
            : null,
        IsHappyPath = se.TryGetProperty("isHappyPath", out var hp) && hp.ValueKind == JsonValueKind.True,
        ErrorMessage = GetString(se, "errorMessage"),
        ErrorStackTrace = GetString(se, "errorStackTrace"),
        FailureCause = GetString(se, "failureCause"),
        Labels = ReadStringArray(se, "labels"),
        Categories = ReadStringArray(se, "categories"),
        Rule = GetString(se, "rule"),
        Attempt = ReadInt(se, "attempt"),
        SourceFile = GetString(se, "sourceFile"),
        SourceLine = ReadInt(se, "sourceLine"),
        OutlineId = GetString(se, "outlineId"),
        ExamplesBlockName = GetString(se, "examplesBlockName"),
        ExamplesBlockDescription = GetString(se, "examplesBlockDescription"),
        ExamplesBlockIndex = ReadInt(se, "examplesBlockIndex"),
        ExampleValues = ReadStringDictionary(se, "exampleValues"),
        ExampleFlatValues = ReadStringDictionary(se, "exampleFlatValues") ?? ReadStringDictionary(se, "exampleValues"),
        ExampleDisplayName = GetString(se, "exampleDisplayName"),
        Attachments = ReadAttachments(se, "attachments"),
        BackgroundSteps = ReadSteps(se, "backgroundSteps", notes),
        Steps = ReadSteps(se, "steps", notes)
    };

    private static ExecutionResult? ReadStepStatus(JsonElement step, ParseNotes notes)
    {
        if (!step.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            return null;

        var text = status.GetString();
        if (text is not null && Enum.TryParse<ExecutionResult>(text, ignoreCase: true, out var parsed))
            return parsed;

        notes.StepStatuses.Add(text ?? "");
        return null;
    }

    private static ScenarioStep[]? ReadSteps(JsonElement parent, string name, ParseNotes notes)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var steps = arr.EnumerateArray().Select(s => new ScenarioStep
        {
            Keyword = GetString(s, "keyword"),
            Text = GetString(s, "text") ?? "",
            // Null, not Passed, when the status cannot be read: a step has an honest "not recorded"
            // value where a scenario does not, and a status this build does not know is not a pass.
            Status = ReadStepStatus(s, notes),
            Duration = s.TryGetProperty("durationSeconds", out var sd) && sd.ValueKind == JsonValueKind.Number
                ? TimeSpan.FromSeconds(sd.GetDouble())
                : null,
            // Written by both step mappers since 3.0.47 and read by neither until 3.1.0: merging a
            // sharded run silently threw away every step's failure message and every step's location,
            // which is most of what makes a merged report worth reading.
            FailureMessage = GetString(s, "failureMessage"),
            SourceFile = GetString(s, "sourceFile"),
            SourceLine = ReadInt(s, "sourceLine"),
            BypassReason = GetString(s, "bypassReason"),
            DocString = GetString(s, "docString"),
            DocStringMediaType = GetString(s, "docStringMediaType"),
            Comments = ReadStringArray(s, "comments"),
            SubSteps = ReadSteps(s, "subSteps", notes),
            Attachments = ReadAttachments(s, "attachments"),
            Parameters = ReadParameters(s),
            TextSegments = ReadTextSegments(s)
        }).ToArray();

        return steps.Length > 0 ? steps : null;
    }

    private static StepParameter[]? ReadParameters(JsonElement step)
    {
        if (!step.TryGetProperty("parameters", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = arr.EnumerateArray().Select(p => new StepParameter
        {
            Name = GetString(p, "name") ?? "",
            Kind = ReadEnum(GetString(p, "kind"), StepParameterKind.Inline),
            InlineValue = ReadInlineValue(p, "inlineValue"),
            TabularValue = ReadTabular(p),
            TreeValue = ReadTree(p)
        }).ToArray();

        return list.Length > 0 ? list : null;
    }

    private static InlineParameterValue? ReadInlineValue(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object)
            return null;
        return new InlineParameterValue(
            GetString(v, "value") ?? "",
            GetString(v, "expectation"),
            ReadEnum(GetString(v, "status"), VerificationStatus.NotApplicable));
    }

    private static TabularParameterValue? ReadTabular(JsonElement parent)
    {
        if (!parent.TryGetProperty("tabularValue", out var t) || t.ValueKind != JsonValueKind.Object)
            return null;

        var columns = EnumerateArray(t, "columns")
            .Select(c => new TabularColumn(GetString(c, "name") ?? "", c.TryGetProperty("isKey", out var k) && k.ValueKind == JsonValueKind.True))
            .ToArray();

        var rows = EnumerateArray(t, "rows")
            .Select(r => new TabularRow(
                ReadEnum(GetString(r, "type"), TableRowType.Matching),
                EnumerateArray(r, "values").Select(ReadCell).ToArray()))
            .ToArray();

        var isLinkedOutput = t.TryGetProperty("isLinkedOutput", out var lo) && lo.ValueKind == JsonValueKind.True;
        return new TabularParameterValue(columns, rows, isLinkedOutput);
    }

    private static TabularCell ReadCell(JsonElement c) => new(
        GetString(c, "value") ?? "",
        GetString(c, "expectation"),
        ReadEnum(GetString(c, "status"), VerificationStatus.NotApplicable));

    private static TreeParameterValue? ReadTree(JsonElement parent)
    {
        if (!parent.TryGetProperty("treeValue", out var t) || t.ValueKind != JsonValueKind.Object
            || !t.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.Object)
            return null;
        return new TreeParameterValue(ReadTreeNode(root));
    }

    private static TreeNode ReadTreeNode(JsonElement n)
    {
        var children = n.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array
            ? ch.EnumerateArray().Select(ReadTreeNode).ToArray()
            : null;
        return new TreeNode(
            GetString(n, "path") ?? "",
            GetString(n, "node") ?? "",
            GetString(n, "value") ?? "",
            GetString(n, "expectation"),
            ReadEnum(GetString(n, "status"), VerificationStatus.NotApplicable),
            children);
    }

    private static StepTextSegment[]? ReadTextSegments(JsonElement step)
    {
        if (!step.TryGetProperty("textSegments", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = arr.EnumerateArray().Select(s => new StepTextSegment
        {
            Text = GetString(s, "text"),
            ParameterName = GetString(s, "parameterName"),
            Parameter = ReadInlineValue(s, "parameter"),
            TableReference = GetString(s, "tableReference"),
            TableReferenceFormattedValue = GetString(s, "tableReferenceFormattedValue")
        }).ToArray();

        return list.Length > 0 ? list : null;
    }

    private static FileAttachment[]? ReadAttachments(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = arr.EnumerateArray()
            .Select(a => new FileAttachment(GetString(a, "name") ?? "", GetString(a, "relativePath") ?? "", GetString(a, "mediaType")))
            .ToArray();
        return list.Length > 0 ? list : null;
    }

    private static ComponentRelationship[] ReadRelationships(JsonElement root)
    {
        if (!root.TryGetProperty("componentRelationships", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray().Select(r => new ComponentRelationship(
            Caller: GetString(r, "caller") ?? "",
            Service: GetString(r, "service") ?? "",
            Protocol: GetString(r, "protocol") ?? "",
            Methods: new HashSet<string>(ReadStringArray(r, "methods") ?? []),
            CallCount: r.TryGetProperty("callCount", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0,
            TestCount: r.TryGetProperty("testCount", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0,
            DependencyCategory: GetString(r, "dependencyCategory"))).ToArray();
    }

    private static Dictionary<string, WholeTestFlowFragment> ReadWholeTestFlow(JsonElement root)
    {
        var result = new Dictionary<string, WholeTestFlowFragment>();
        if (!root.TryGetProperty("wholeTestFlow", out var obj) || obj.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in obj.EnumerateObject())
        {
            result[prop.Name] = new WholeTestFlowFragment(
                GetString(prop.Value, "activityHtml") ?? "",
                GetString(prop.Value, "flameHtml") ?? "",
                prop.Value.TryGetProperty("spanCount", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0);
        }
        return result;
    }

    private static Dictionary<string, JsonElement> ReadObjectMap(JsonElement root, string name)
    {
        var result = new Dictionary<string, JsonElement>();
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var prop in obj.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();
        return result;
    }

    /// <summary>
    /// The <c>method</c> field, back in the shape the run wrote it: an <see cref="HttpMethod"/> when it
    /// is one, and the label itself when it is not.
    /// </summary>
    /// <remarks>
    /// <para>This read <c>MetaType == Event ? label : HttpMethod.Parse(...)</c>, on the premise that only
    /// an event carries a free-text label. That premise is wrong, and <see cref="DiagramMethod"/>'s own
    /// documentation says so: the slot is "either an HttpMethod (for standard HTTP operations) or a
    /// string (for custom labels like \"Blob Upload\", \"Cache Get (Hit)\")". Every non-HTTP tracker in
    /// the repo writes one at the default <c>MetaType</c> - <c>SELECT FROM CUSTOMERS</c> from SQL,
    /// <c>GET (Hit)</c> from Redis, <c>Orders/Place [unary]</c> from gRPC - and a SQL response carries the
    /// empty string. <see cref="HttpMethod.Parse"/> is total only over RFC-7230 tokens, so a spaced label
    /// threw <see cref="FormatException"/> and an empty one threw <see cref="ArgumentException"/>, which
    /// no catch clause in the tool covered: an unhandled exception and a stack trace, from merging a
    /// shard that had captured a database call.</para>
    ///
    /// <para>It is <b>not</b> <c>InteractionRecord.ParseMethod</c>, which substitutes <c>"CALL"</c> for an
    /// empty method. That is right when synthesising a record from a capture that named none, and wrong
    /// here: this is reading back a file that already holds the answer, and a merge must round-trip what
    /// it was given rather than invent a label the run never wrote.</para>
    /// </remarks>
    private static OneOf<HttpMethod, string> ReadMethod(string method, RequestResponseMetaType metaType)
    {
        if (metaType == RequestResponseMetaType.Event)
            return method;

        // The NINE standard verbs, not "anything HttpMethod.Parse will take". Parse accepts any RFC-7230
        // token, so `Publish` - which a message-queue tracker writes as a LABEL - came back as an
        // HttpMethod, and the writer then upper-cased it to `PUBLISH`. Matching the known set is what
        // makes the round trip exact, and it is the rule InteractionRecord.ParseMethod already uses on
        // the ingestion side.
        return method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            "TRACE" => HttpMethod.Trace,
            "CONNECT" => HttpMethod.Connect,
            _ => method
        };
    }

    /// <summary>
    /// Rebuilds one scenario's captured traffic. Only the real interactions are in the file - the
    /// diagram markers are dropped at write time - so <c>stepPath</c> is read back rather than
    /// re-derived: the derivation walks the markers, which are gone.
    /// </summary>
    private static void ReadInteractions(JsonElement se, Scenario scenario, List<RequestResponseLog> into,
        Dictionary<string, List<string?>> stepPaths, ParseNotes notes)
    {
        if (!se.TryGetProperty("httpInteractions", out var array) || array.ValueKind != JsonValueKind.Array)
            return;

        var paths = new List<string?>();
        foreach (var element in array.EnumerateArray())
        {
            var metaType = ReadEnum(GetString(element, "metaType"), RequestResponseMetaType.Default);
            var method = GetString(element, "method") ?? "";
            OneOf<HttpMethod, string> parsedMethod = ReadMethod(method, metaType);

            // Both shapes: 3.1.0 writes a number plus a label, older shards wrote the name alone. The
            // enum is preferred when the number names one, so a round trip through a merge produces the
            // same two fields the run wrote rather than degrading them to text.
            OneOf<HttpStatusCode, string>? status = null;
            var (statusNumber, statusLabel) = InteractionStatus.Read(
                GetNumberOrString(element, "statusCode"), GetString(element, "statusText"));
            if (statusNumber is { } numeric && Enum.IsDefined(typeof(HttpStatusCode), numeric))
                status = (HttpStatusCode)numeric;
            else if (statusLabel is { } label)
                status = label;
            else if (statusNumber is { } bare)
                status = bare.ToString(CultureInfo.InvariantCulture);

            var log = new RequestResponseLog(
                TestName: scenario.DisplayName,
                TestId: scenario.Id,
                Method: parsedMethod,
                Content: GetString(element, "content"),
                Uri: Uri.TryCreate(GetString(element, "uri"), UriKind.RelativeOrAbsolute, out var uri) ? uri : new Uri("about:blank"),
                Headers: ReadHeaders(element),
                ServiceName: GetString(element, "serviceName") ?? "",
                CallerName: GetString(element, "callerName") ?? "",
                Type: ReadInteractionType(element, notes),
                TraceId: ReadGuid(element, "traceId"),
                RequestResponseId: ReadGuid(element, "requestResponseId"),
                TrackingIgnore: false,
                StatusCode: status,
                MetaType: metaType,
                DependencyCategory: GetString(element, "dependencyCategory"),
                CallerDependencyCategory: GetString(element, "callerDependencyCategory"))
            {
                Timestamp = ReadTimestamp(element),
                Phase = ReadEnum(GetString(element, "phase"), default(TestPhase)),
                IsUserAction = element.TryGetProperty("isUserAction", out var ua) && ua.ValueKind == JsonValueKind.True,
                ActivityTraceId = GetString(element, "activityTraceId"),
                ActivitySpanId = GetString(element, "activitySpanId"),
                CapturedBy = GetString(element, "capturedBy"),
                DurationMs = ReadDouble(element, "durationMs")
            };

            into.Add(log);
            paths.Add(GetString(element, "stepPath"));
        }

        if (paths.Count > 0)
            stepPaths[scenario.Id] = paths;
    }

    private static RequestResponseType ReadInteractionType(JsonElement element, ParseNotes notes)
    {
        var text = GetString(element, "type");
        if (text is not null && Enum.TryParse<RequestResponseType>(text, ignoreCase: true, out var parsed))
            return parsed;

        notes.InteractionTypes.Add(text ?? "");
        return RequestResponseType.Request;
    }

    private static DiagramMarkerKind ReadAnnotationKind(JsonElement annotation, ParseNotes notes)
    {
        var text = GetString(annotation, "kind");
        if (text is not null && Enum.TryParse<DiagramMarkerKind>(text, ignoreCase: true, out var parsed))
            return parsed;

        notes.AnnotationKinds.Add(text ?? "");
        return DiagramMarkerKind.Custom;
    }

    private static void ReadAnnotations(JsonElement se, string scenarioId,
        Dictionary<string, List<ReportGenerator.ScenarioAnnotation>> into, ParseNotes notes)
    {
        if (!se.TryGetProperty("annotations", out var array) || array.ValueKind != JsonValueKind.Array)
            return;

        var found = array.EnumerateArray()
            .Select(a => new ReportGenerator.ScenarioAnnotation(
                ReadInt(a, "index") ?? 0,
                ReadAnnotationKind(a, notes),
                GetString(a, "text") ?? ""))
            .ToList();

        if (found.Count > 0)
            into[scenarioId] = found;
    }

    private static (string Key, string? Value)[] ReadHeaders(JsonElement element) =>
        element.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array
            ? headers.EnumerateArray().Select(h => (GetString(h, "key") ?? "", GetString(h, "value"))).ToArray()
            : [];

    private static IReadOnlyList<DiagnosticEntry> ReadDiagnostics(JsonElement root) =>
        EnumerateArray(root, "diagnostics")
            .Select(d => new DiagnosticEntry(
                ReadEnum(GetString(d, "kind"), DiagnosticKind.Other),
                GetString(d, "message") ?? "",
                GetString(d, "scenarioId")))
            .ToArray();

    private static Guid ReadGuid(JsonElement parent, string name) =>
        Guid.TryParse(GetString(parent, name), out var value) ? value : Guid.Empty;

    private static double? ReadDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement parent) =>
        DateTimeOffset.TryParse(GetString(parent, "timestamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    /// <summary>
    /// The shard's <c>environment</c>, or null when it has none - which is every shard written before
    /// the merge carried it, and any merged shard whose own inputs disagreed.
    /// </summary>
    private static RunEnvironment? ReadEnvironment(JsonElement root)
    {
        if (!root.TryGetProperty("environment", out var e) || e.ValueKind != JsonValueKind.Object)
            return null;

        var os = GetString(e, "os");
        var runtime = GetString(e, "runtime");
        return os is null && runtime is null ? null : new RunEnvironment(os ?? "", runtime ?? "");
    }

    private static CiMetadata? ReadCiMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("ciMetadata", out var m) || m.ValueKind != JsonValueKind.Object)
            return null;
        // Since 3.1.0 the object is always written, with provider None off CI. Null is still what
        // "there was no CI" means to every reader downstream - the merged report's summary gates its
        // CI table on it - so collapse it back here rather than letting a None record travel.
        var provider = ReadEnum(GetString(m, "provider"), CiEnvironment.None);
        if (provider == CiEnvironment.None)
            return null;
        return new CiMetadata(
            Provider: provider,
            BuildNumber: GetString(m, "buildNumber"),
            Branch: GetString(m, "branch"),
            CommitSha: GetString(m, "commitSha"),
            PipelineUrl: GetString(m, "pipelineUrl"),
            Repository: GetString(m, "repository"),
            RunId: GetString(m, "runId"),
            RunAttempt: GetString(m, "runAttempt"));
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : [];

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// For a field whose JSON type changed across releases. <c>statusCode</c> is a number from 3.1.0 and
    /// was a string before it, so a reader that insists on one kind silently drops the other - which on
    /// this path means every merged shard losing its statuses rather than failing loudly.
    /// </summary>
    private static string? GetNumberOrString(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var v)
            ? null
            : v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            };

    private static string[]? ReadStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray();
        return list.Length > 0 ? list : null;
    }

    private static Dictionary<string, string>? ReadStringDictionary(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;
        var dict = new Dictionary<string, string>();
        foreach (var prop in obj.EnumerateObject())
            dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
        return dict.Count > 0 ? dict : null;
    }

    private static DateTime ReadDate(JsonElement root, string name)
    {
        var s = GetString(root, name);
        return s is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : default;
    }

    private static TEnum ReadEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct =>
        value is not null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
