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
        private readonly List<string> _path = [];
        private readonly List<StepEntry> _stepStack = [];

        private ScenarioEntry? _scenario;
        private InteractionEntry? _interaction;
        private AnnotationEntry? _annotation;
        private AttachmentEntry? _attachment;
        private DiagnosticEntry? _diagnostic;
        private string _featureName = "";
        private string[] _featureLabels = [];
        private readonly List<string> _pendingFeatureLabels = [];
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
                        SetProperty(reader.GetString() ?? "");
                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        _path.Add(CurrentKey());
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

        private void SetProperty(string name)
        {
            if (_containers.Count == 0)
                return;
            var top = _containers[^1];
            top.Property = name;
            _containers[^1] = top;
        }

        private string CurrentKey()
        {
            if (_containers.Count == 0)
                return "$";
            var top = _containers[^1];
            return top.IsArray ? top.Index.ToString() : top.Property ?? "";
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

        /// <summary>The path of the value currently being read, key included.</summary>
        private string Key => CurrentKey();

        private bool At(params string[] tail)
        {
            // Compares the last segments of the path, treating "#" as "any array index".
            if (_path.Count < tail.Length)
                return false;
            for (var i = 0; i < tail.Length; i++)
            {
                var segment = _path[_path.Count - tail.Length + i];
                if (tail[i] == "#")
                {
                    if (!int.TryParse(segment, out _))
                        return false;
                }
                else if (segment != tail[i])
                {
                    return false;
                }
            }
            return true;
        }

        // ─── Entity lifecycle ──────────────────────────────────

        private void Enter()
        {
            if (At("features", "#"))
            {
                _featureName = "";
                _featureLabels = [];
                _pendingFeatureLabels.Clear();
            }
            else if (At("scenarios", "#"))
            {
                _scenario = new ScenarioEntry { Ordinal = _scenarioOrdinal++, FeatureName = _featureName, FeatureLabels = _featureLabels };
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
                    case "kronikolVersion": index.KronikolVersion = reader.GetString(); return;
                    case "startTime": index.StartTime = reader.GetString(); return;
                    case "endTime": index.EndTime = reader.GetString(); return;
                    case "mergeableFormatVersion":
                        index.Mergeable = true;
                        index.MergeableFormatVersion = reader.TryGetInt32(out var version) ? version : null;
                        return;
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
                case "statusCode": interaction.StatusCode = reader.GetString(); break;
                case "timestamp": interaction.Timestamp = reader.GetString(); break;
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
                    if (reader.TokenType != JsonTokenType.String)
                        break;
                    var content = reader.GetString();
                    if (content is null)
                        break;
                    interaction.BodyLength = content.Length;
                    interaction.BodyHash = HashBody(content);
                    interaction.Body = TokenSlice(ref reader, windowStart);
                    break;
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
            }
        }

        private static string? Meaningful(string? value, string neutral) =>
            value is null || value == neutral ? null : value;

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
        private static string HashBody(string content)
        {
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(content));
            return "b:" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
        }
    }
}
