using System.Globalization;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// Builds a <see cref="ReportIndex"/> in one forward pass over the file, without ever holding the whole
/// document in memory.
///
/// <para>The reader works on a window of the file that is refilled as it advances, so all parsing state
/// lives out here rather than on the stack — hence the explicit container stack instead of the recursive
/// descent this shape invites. The window grows when a single token will not fit: a report's diagrams are
/// one JSON string each and run to megabytes, which is exactly the case a fixed buffer gets wrong.</para>
/// </summary>
internal static class ReportScanner
{
    /// <summary>
    /// A version field that is present and is not an integer. Distinct from null, which means the key was
    /// absent - a file written before the shape was versioned. A file that declares a version this tool
    /// cannot read is refused; a file that declares none is read on its merits.
    /// </summary>
    internal const int UnreadableVersion = -1;

    private const int InitialWindow = 128 * 1024;

    public static ReportIndex Scan(string path)
    {
        using var stream = File.OpenRead(path);
        var index = new ReportIndex { Path = path, FileLength = stream.Length };
        var walker = new Walker(index);

        var buffer = ArrayPool<byte>.Shared.Rent(InitialWindow);
        try
        {
            var state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            var filled = 0;
            long windowStart = 0;
            var eof = false;

            while (true)
            {
                if (!eof)
                {
                    var read = stream.Read(buffer, filled, buffer.Length - filled);
                    filled += read;
                    if (read == 0)
                        eof = true;
                }

                var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), eof, state);
                walker.Consume(ref reader, windowStart);
                state = reader.CurrentState;
                var consumed = (int)reader.BytesConsumed;

                if (eof && consumed >= filled)
                    break;

                if (consumed == 0 && filled == buffer.Length)
                {
                    // One token is larger than the whole window — a diagram, almost always. Double and retry.
                    var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, filled).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                    continue;
                }

                if (eof && consumed == 0)
                    break;

                Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
                filled -= consumed;
                windowStart += consumed;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        walker.Finish();
        return index;
    }

    /// <summary>
    /// Turns the token stream into entities. Position is tracked as a list of path segments — property
    /// names and array indices — so a value can be dispatched on where it sits rather than on how it was
    /// reached, which is what lets the walk survive the window being refilled underneath it.
    /// </summary>
    private sealed class Walker(ReportIndex index)
    {
        private readonly List<Container> _containers = [];
        private readonly List<string?> _path = [];
        private readonly List<StepEntry> _stepStack = [];

        private ScenarioEntry? _scenario;
        private InteractionEntry? _interaction;
        private AnnotationEntry? _annotation;
        private AttachmentEntry? _attachment;
        private DiagnosticEntry? _diagnostic;
        private string _featureName = "";
        private string[] _featureLabels = [];
        private readonly List<string> _pendingFeatureLabels = [];
        private string? _featureSourceFile;
        private int _scenarioOrdinal;
        private int _interactionOrdinal;

        private struct Container
        {
            public bool IsArray;
            public int Index;
            public string? Property;
        }

        public void Consume(ref Utf8JsonReader reader, long windowStart)
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        SetProperty(ref reader);
                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        _path.Add(CurrentSegment());
                        _containers.Add(new Container { IsArray = reader.TokenType == JsonTokenType.StartArray });
                        Enter();
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        Leave();
                        _containers.RemoveAt(_containers.Count - 1);
                        _path.RemoveAt(_path.Count - 1);
                        Advance();
                        break;

                    default:
                        Value(ref reader, windowStart);
                        Advance();
                        break;
                }
            }
        }

        public void Finish()
        {
            // A report with no failure detail and no attribution predates the enrichment; say so rather
            // than letting every command silently answer less than it was asked.
            if (index.Scenarios.Count == 0)
                index.Enriched = true;
        }

        // ─── Position tracking ─────────────────────────────────

        // The scanner's hottest bookkeeping (QUERY_PERF_PLAN.md §3.4): it runs per token over a 100 MB+
        // file, so property names are interned against the closed set the walker dispatches on (no
        // per-name string), array positions travel as a null segment (no Index.ToString() per element),
        // and At is fixed-arity (no params array per call).

        private static readonly Dictionary<string, string> KnownNames = BuildKnownNames();

        private static readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> KnownNamesBySpan =
            KnownNames.GetAlternateLookup<ReadOnlySpan<char>>();

        private static Dictionary<string, string> BuildKnownNames()
        {
            // Every name Enter/Leave/Value dispatch on, plus the report's other high-frequency keys.
            string[] names =
            [
                "kronikolVersion", "startTime", "endTime", "mergeableFormatVersion", "formatVersion", "suite", "kind", "message",
                "scenarioId", "name", "relativePath", "mediaType", "index", "text", "key", "value", "type",
                "method", "uri", "serviceName", "callerName", "statusCode", "statusText", "timestamp", "requestResponseId",
                "traceId", "stepPath", "phase", "metaType", "dependencyCategory", "activityTraceId",
                "activitySpanId", "capturedBy", "isUserAction", "durationMs", "content", "keyword", "status",
                "durationSeconds", "failureMessage", "sourceFile", "sourceLine", "bypassReason", "docString",
                "id", "stableId", "result", "isHappyPath", "errorMessage", "errorStackTrace", "rule",
                "features", "scenarios", "httpInteractions", "annotations", "attachments", "diagnostics",
                "steps", "backgroundSteps", "subSteps", "headers", "labels", "categories", "exampleValues",
                "diagrams", "comments", "parameters", "attempt",
                // Run identity (3.1.0). Interned for the same reason as the rest: the scanner
                // sees these on every report and an uninterned name allocates per document.
                "ciMetadata", "environment", "provider", "buildNumber", "branch", "commitSha",
                "pipelineUrl", "repository", "runId", "runAttempt", "os", "runtime",
            ];
            var known = new Dictionary<string, string>(names.Length, StringComparer.Ordinal);
            foreach (var name in names)
                known[name] = name;
            return known;
        }

        private void SetProperty(ref Utf8JsonReader reader)
        {
            if (_containers.Count == 0)
                return;
            var top = _containers[^1];
            top.Property = PropertyName(ref reader);
            _containers[^1] = top;
        }

        private static string PropertyName(ref Utf8JsonReader reader)
        {
            // Unescaped chars never outnumber raw UTF-8 bytes, so a short raw value fits the buffer.
            var rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
            if (rawLength <= 32)
            {
                Span<char> buffer = stackalloc char[32];
                var written = reader.CopyString(buffer);
                if (KnownNamesBySpan.TryGetValue(buffer[..written], out var known))
                    return known;
                return new string(buffer[..written]);
            }
            return reader.GetString() ?? "";
        }

        /// <summary>The path segment the current position contributes: a name, or null for an array index.</summary>
        private string? CurrentSegment()
        {
            if (_containers.Count == 0)
                return "$";
            var top = _containers[^1];
            return top.IsArray ? null : top.Property ?? "";
        }

        private void Advance()
        {
            if (_containers.Count == 0)
                return;
            var top = _containers[^1];
            if (top.IsArray)
            {
                top.Index++;
                _containers[^1] = top;
            }
        }

        /// <summary>The key of the value currently being read — its property name, or "" under an array.</summary>
        private string Key
        {
            get
            {
                if (_containers.Count == 0)
                    return "";
                var top = _containers[^1];
                return top.IsArray ? "" : top.Property ?? "";
            }
        }

        // Compares the last segments of the path; "#" means "any array index" (a null segment).
        private static bool Matches(string? segment, string tail) =>
            tail == "#" ? segment is null : segment == tail;

        private bool At(string a) =>
            _path.Count >= 1 && Matches(_path[^1], a);

        private bool At(string a, string b) =>
            _path.Count >= 2 && Matches(_path[^2], a) && Matches(_path[^1], b);

        private bool At(string a, string b, string c) =>
            _path.Count >= 3 && Matches(_path[^3], a) && Matches(_path[^2], b) && Matches(_path[^1], c);

        // ─── Entity lifecycle ──────────────────────────────────

        private void Enter()
        {
            if (At("features", "#"))
            {
                _featureName = "";
                _featureSourceFile = null;
                _featureLabels = [];
                _pendingFeatureLabels.Clear();
            }
            else if (At("scenarios", "#"))
            {
                _scenario = new ScenarioEntry { Ordinal = _scenarioOrdinal++, FeatureName = _featureName, FeatureLabels = _featureLabels, FeatureSourceFile = _featureSourceFile };
                _interactionOrdinal = 0;
                _stepStack.Clear();
            }
            else if (At("httpInteractions", "#"))
            {
                _interaction = new InteractionEntry { Ordinal = _interactionOrdinal++ };
            }
            else if (At("annotations", "#"))
            {
                _annotation = new AnnotationEntry();
                index.Enriched = true;
            }
            else if (At("attachments", "#"))
            {
                _attachment = new AttachmentEntry();
            }
            else if (At("diagnostics", "#"))
            {
                _diagnostic = new DiagnosticEntry();
            }
            else if (InStepArray())
            {
                var step = new StepEntry();
                if (_stepStack.Count > 0)
                    _stepStack[^1].SubSteps.Add(step);
                else if (At("backgroundSteps", "#"))
                    _scenario?.BackgroundSteps.Add(step);
                else
                    _scenario?.Steps.Add(step);
                _stepStack.Add(step);
            }
        }

        private void Leave()
        {
            if (At("features", "#"))
            {
                _featureLabels = [];
            }
            else if (At("scenarios", "#"))
            {
                if (_scenario is not null)
                    index.Scenarios.Add(_scenario);
                _scenario = null;
            }
            else if (At("httpInteractions", "#"))
            {
                if (_interaction is not null && _scenario is not null)
                {
                    _scenario.Interactions.Add(_interaction);
                    RecordBody(_interaction, _scenario);
                }
                _interaction = null;
            }
            else if (At("annotations", "#"))
            {
                if (_annotation is not null)
                    _scenario?.Annotations.Add(_annotation);
                _annotation = null;
            }
            else if (At("attachments", "#"))
            {
                if (_attachment is not null)
                {
                    if (_stepStack.Count > 0)
                        _stepStack[^1].Attachments.Add(_attachment);
                    else
                        _scenario?.Attachments.Add(_attachment);
                }
                _attachment = null;
            }
            else if (At("diagnostics", "#"))
            {
                if (_diagnostic is not null)
                    index.Diagnostics.Add(_diagnostic);
                _diagnostic = null;
            }
            else if (InStepArray() && _stepStack.Count > 0)
            {
                _stepStack.RemoveAt(_stepStack.Count - 1);
            }
        }

        private bool InStepArray() =>
            At("steps", "#") || At("backgroundSteps", "#") || At("subSteps", "#");

        private void RecordBody(InteractionEntry interaction, ScenarioEntry scenario)
        {
            if (interaction.BodyHash is null)
                return;

            if (!index.Bodies.TryGetValue(interaction.BodyHash, out var body))
            {
                body = new BodyEntry { Hash = interaction.BodyHash, Length = interaction.BodyLength, First = interaction.Body };
                index.Bodies[interaction.BodyHash] = body;
            }

            body.Occurrences.Add(interaction.Address(scenario));
        }

        // ─── Values ────────────────────────────────────────────

        private void Value(ref Utf8JsonReader reader, long windowStart)
        {
            var key = Key;

            if (_path.Count == 1)
            {
                switch (key)
                {
                    case "formatVersion":
                        // Absent means "written before the shape was versioned", which is a real answer;
                        // present-but-not-an-integer means the file is not what it claims, and must not
                        // read the same as absent.
                        index.FormatVersion = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var format)
                            ? format
                            : UnreadableVersion;
                        return;
                    case "kronikolVersion": index.KronikolVersion = reader.GetString(); return;
                    case "suite": index.Suite = reader.TokenType == JsonTokenType.String ? reader.GetString() : null; return;
                    case "startTime": index.StartTime = reader.GetString(); return;
                    case "endTime": index.EndTime = reader.GetString(); return;
                    case "mergeableFormatVersion":
                        index.Mergeable = true;
                        // Same rule. This used to be `reader.TryGetInt32(out var v) ? v : null`, so a
                        // string, a float or a null landed as null - indistinguishable from a file with
                        // no version at all - and the gate one layer up accepted it silently.
                        index.MergeableFormatVersion = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version)
                            ? version
                            : UnreadableVersion;
                        return;
                }
            }

            // Run identity sits one level down, and before `features` - so it is in the index by the
            // time any scenario is, whatever the file's size.
            if (_path.Count == 2 && _path[^1] == "ciMetadata")
            {
                switch (key)
                {
                    case "provider": index.CiProvider = reader.GetString(); return;
                    case "branch": index.CiBranch = reader.GetString(); return;
                    case "commitSha": index.CiCommitSha = reader.GetString(); return;
                    case "buildNumber": index.CiBuildNumber = reader.GetString(); return;
                    case "runId": index.CiRunId = reader.GetString(); return;
                    case "runAttempt": index.CiRunAttempt = reader.GetString(); return;
                    case "repository": index.CiRepository = reader.GetString(); return;
                    case "pipelineUrl": index.CiPipelineUrl = reader.GetString(); return;
                }
            }
            else if (_path.Count == 2 && _path[^1] == "environment")
            {
                switch (key)
                {
                    case "os": index.EnvironmentOs = reader.GetString(); return;
                    case "runtime": index.EnvironmentRuntime = reader.GetString(); return;
                }
            }

            if (_diagnostic is not null)
            {
                switch (key)
                {
                    case "kind": _diagnostic.Kind = reader.GetString() ?? ""; return;
                    case "message": _diagnostic.Message = reader.GetString() ?? ""; return;
                    case "scenarioId": _diagnostic.ScenarioId = reader.GetString(); return;
                }
                return;
            }

            if (_attachment is not null)
            {
                switch (key)
                {
                    case "name": _attachment.Name = reader.GetString() ?? ""; return;
                    case "relativePath": _attachment.RelativePath = reader.GetString() ?? ""; return;
                    case "mediaType": _attachment.MediaType = reader.GetString(); return;
                }
                return;
            }

            if (_annotation is not null)
            {
                switch (key)
                {
                    case "index": _annotation.Index = reader.TryGetInt32(out var i) ? i : 0; return;
                    case "kind": _annotation.Kind = reader.GetString() ?? ""; return;
                    case "text": _annotation.Text = reader.GetString() ?? ""; return;
                }
                return;
            }

            if (_interaction is not null)
            {
                Interaction(_interaction, key, ref reader, windowStart);
                return;
            }

            if (_stepStack.Count > 0)
            {
                Step(_stepStack[^1], key, ref reader);
                return;
            }

            if (_scenario is not null)
            {
                Scenario(_scenario, key, ref reader, windowStart);
                return;
            }

            if (At("features", "#") && key == "name")
            {
                _featureName = reader.GetString() ?? "";
                return;
            }

            // Read before the scenarios array, so every scenario of the feature is built with it.
            if (At("features", "#") && key == "sourceFile")
            {
                _featureSourceFile = reader.GetString();
                return;
            }

            if (At("features", "#", "labels"))
            {
                _pendingFeatureLabels.Add(reader.GetString() ?? "");
                _featureLabels = _pendingFeatureLabels.ToArray();
            }
        }

        private void Interaction(InteractionEntry interaction, string key, ref Utf8JsonReader reader, long windowStart)
        {
            if (At("headers", "#") && key == "key")
            {
                interaction.HeaderCount++;
                if (!interaction.Headers.Exists)
                    interaction.Headers = TokenSlice(ref reader, windowStart);
                return;
            }

            switch (key)
            {
                case "type": interaction.Type = reader.GetString() ?? ""; break;
                case "method": interaction.Method = reader.GetString(); break;
                case "uri": interaction.Uri = reader.GetString() ?? ""; break;
                case "serviceName": interaction.ServiceName = reader.GetString() ?? ""; break;
                case "callerName": interaction.CallerName = reader.GetString() ?? ""; break;
                // Both shapes. From 3.1.0 this is a Number and the label lives in statusText; before
                // that it was the enum NAME or a bare numeric string. GetString() throws
                // InvalidOperationException on a Number token, and the dispatch catch list does not
                // include it - so reading only the new shape would crash the tool on every old report
                // and reading only the old one would crash it on every new one.
                case "statusCode":
                    interaction.StatusCode = reader.TokenType == JsonTokenType.Number
                        ? (reader.TryGetInt32(out var code) ? code.ToString(CultureInfo.InvariantCulture) : null)
                        : reader.GetString();
                    break;
                case "statusText": interaction.StatusText = reader.GetString(); break;
                case "timestamp": interaction.Timestamp = reader.GetString(); break;
                case "requestResponseId": interaction.RequestResponseId = NonEmptyId(reader.GetString()); break;
                case "traceId": interaction.TraceId = NonEmptyId(reader.GetString()); break;
                case "stepPath":
                    // Presence of the key, not of a value: a current report writes stepPath on every
                    // interaction and null is a legitimate answer (before the first step, or attribution
                    // that could not be trusted). An older report has no such key at all.
                    interaction.StepPath = reader.GetString();
                    index.Enriched = true;
                    break;
                case "phase": interaction.Phase = Meaningful(reader.GetString(), "Unknown"); break;
                case "metaType": interaction.MetaType = Meaningful(reader.GetString(), "Default"); break;
                case "dependencyCategory": interaction.DependencyCategory = reader.GetString(); break;
                case "activityTraceId": interaction.ActivityTraceId = reader.GetString(); break;
                case "activitySpanId": interaction.ActivitySpanId = reader.GetString(); break;
                case "capturedBy": interaction.CapturedBy = reader.GetString(); break;
                case "isUserAction": interaction.IsUserAction = reader.TokenType == JsonTokenType.True; break;
                case "durationMs":
                    interaction.DurationMs = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var ms) ? ms : null;
                    break;
                case "content":
                {
                    if (reader.TokenType != JsonTokenType.String)
                        break;

                    // Every payload in the file passes through here, and all this needs from one is two
                    // numbers. GetString() allocated a UTF-16 copy of the body and HashBody then allocated
                    // a UTF-8 copy of that, so indexing a 142 MB report produced over 300 MB of garbage
                    // before a single question had been answered - the text dropped again immediately,
                    // since what the index keeps is the byte range. Neither copy is needed: the hash is
                    // over exactly the bytes the reader is already sitting on, and the length is a count.
                    if (!reader.ValueIsEscaped && !reader.HasValueSequence)
                    {
                        interaction.BodyLength = Encoding.UTF8.GetCharCount(reader.ValueSpan);
                        interaction.BodyHash = HashBody(reader.ValueSpan);
                    }
                    else
                    {
                        // Escaped, or split across the read window. Decode once, into pooled buffers -
                        // the hash is over the same characters either way, so no `b:` address moves.
                        var raw = (int)(reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length);
                        var chars = ArrayPool<char>.Shared.Rent(raw);
                        try
                        {
                            var written = reader.CopyString(chars);
                            interaction.BodyLength = written;

                            var utf8 = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(written));
                            try
                            {
                                var encoded = Encoding.UTF8.GetBytes(chars.AsSpan(0, written), utf8);
                                interaction.BodyHash = HashBody(utf8.AsSpan(0, encoded));
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(utf8);
                            }
                        }
                        finally
                        {
                            ArrayPool<char>.Shared.Return(chars);
                        }
                    }

                    interaction.Body = TokenSlice(ref reader, windowStart);
                    break;
                }
            }
        }

        private void Step(StepEntry step, string key, ref Utf8JsonReader reader)
        {
            if (At("comments"))
            {
                step.Comments.Add(reader.GetString() ?? "");
                return;
            }

            // Parameters are read as flat lines: their structure is deep and every consumer prints them.
            if (_path.Contains("parameters"))
            {
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.Number && key is "name" or "value")
                {
                    var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : reader.GetDouble().ToString();
                    if (!string.IsNullOrEmpty(text))
                        step.Parameters.Add(text);
                }
                return;
            }

            switch (key)
            {
                case "keyword": step.Keyword = reader.GetString(); break;
                case "text": step.Text = reader.GetString() ?? ""; break;
                case "status": step.Status = reader.GetString(); break;
                case "durationSeconds":
                    step.DurationSeconds = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var d) ? d : null;
                    break;
                case "failureMessage":
                    step.FailureMessage = reader.GetString();
                    index.Enriched = true;
                    break;
                case "sourceFile": step.SourceFile = reader.GetString(); break;
                case "sourceLine": step.SourceLine = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var line) ? line : null; break;
                case "bypassReason": step.BypassReason = reader.GetString(); break;
                case "docString": step.DocString = reader.GetString(); break;
            }
        }

        private void Scenario(ScenarioEntry scenario, string key, ref Utf8JsonReader reader, long windowStart)
        {
            if (At("labels")) { scenario.Labels.Add(reader.GetString() ?? ""); return; }
            if (At("categories")) { scenario.Categories.Add(reader.GetString() ?? ""); return; }
            if (At("exampleValues"))
            {
                if (reader.TokenType == JsonTokenType.String)
                    scenario.ExampleValues[key] = reader.GetString() ?? "";
                return;
            }
            if (At("diagrams"))
            {
                if (reader.TokenType == JsonTokenType.String)
                    scenario.Diagrams.Add(TokenSlice(ref reader, windowStart));
                return;
            }

            switch (key)
            {
                case "id": scenario.Id = reader.GetString() ?? ""; break;
                case "stableId": scenario.StableId = reader.GetString() ?? ""; break;
                case "name": scenario.Name = reader.GetString() ?? ""; break;
                case "result": scenario.Result = reader.GetString() ?? ""; break;
                case "durationSeconds": scenario.DurationSeconds = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var d) ? d : 0; break;
                case "isHappyPath": scenario.IsHappyPath = reader.TokenType == JsonTokenType.True; break;
                case "errorMessage": scenario.ErrorMessage = reader.GetString(); break;
                case "errorStackTrace": scenario.ErrorStackTrace = reader.GetString(); break;
                case "rule": scenario.Rule = reader.GetString(); break;
                case "attempt": scenario.Attempt = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var attempt) ? attempt : null; break;
                case "sourceFile": scenario.SourceFile = reader.GetString(); break;
                case "sourceLine": scenario.SourceLine = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var scenarioLine) ? scenarioLine : null; break;
            }
        }

        private static string? Meaningful(string? value, string neutral) =>
            value is null || value == neutral ? null : value;

        /// <summary>
        /// An id worth matching on. Markers and user actions travel with the empty Guid
        /// (<c>InteractionMerger</c> shows both), which would pair everything with everything.
        /// </summary>
        private static string? NonEmptyId(string? id) =>
            string.IsNullOrEmpty(id) || id == "00000000-0000-0000-0000-000000000000" ? null : id;

        /// <summary>
        /// Where the token just read sits in the file. <c>BytesConsumed</c> lands immediately past a token,
        /// so the difference from its start is its exact raw length, quotes and escapes included.
        /// </summary>
        private static Slice TokenSlice(ref Utf8JsonReader reader, long windowStart) =>
            new(windowStart + reader.TokenStartIndex, (int)(reader.BytesConsumed - reader.TokenStartIndex));

        /// <summary>
        /// A body's identity is its content. Eight hex characters of SHA-1 name it in five tokens and
        /// collide at a rate no report reaches; the point is that two identical bodies get one address, so
        /// an agent that has read one has read all of them.
        /// </summary>
        /// <summary>
        /// The <c>b:</c> address of a body: the first four bytes of the SHA-1 of its UTF-8 form. Takes the
        /// bytes rather than a string because the caller usually has them already — but they are the same
        /// bytes the old string overload encoded, so every address a shipped report contains still resolves.
        /// </summary>
        private static string HashBody(ReadOnlySpan<byte> utf8)
        {
            Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
            SHA1.HashData(utf8, hash);
            return "b:" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
        }
    }
}
