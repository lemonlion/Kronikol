using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Query;

/// <summary>
/// What fills a report file, part by part (#119, <c>plans/REPORT_FORMAT_PLAN.md</c> section 4.9): one forward pass over
/// the JSON that charges every byte to exactly one part, so the parts add up to the file. Whitespace outside strings is a
/// part of its own. A payload stored compressed (<c>{"$h","$n","$z"}</c>, #85) is charged its stored bytes, and its
/// decoded bytes are what it would take written as a plain string, so the decoded column adds up to the file the same run
/// writes with <see cref="ReportConfigurationOptions.CompressTestRunReportPayloads"/> off.
/// </summary>
/// <remarks>
/// A part is decided by where a value sits, never by what it holds: a step tree is <c>steps</c> and
/// <c>backgroundSteps</c> under a scenario at any depth; a call is an element of a scenario's <c>httpInteractions</c> or
/// of the background's <c>interactions</c>, whose <c>content</c> is its body and everything else its metadata, as is the
/// rest of <c>background</c>; a diagram is an element of a scenario's <c>diagrams</c>. A property's name, and the comma
/// and colon around it, are charged to the part of its value.
/// </remarks>
internal sealed class ReportSizes
{
    /// <summary>The parts, in the order the verb prints them.</summary>
    internal static readonly string[] PartNames =
        ["steps", "calls", "bodies", "diagrams", "annotations", "internal-flow segments", "whole-test flow", "whitespace", "other"];

    private const int Steps = 0, Calls = 1, Bodies = 2, Diagrams = 3, Annotations = 4, InternalFlow = 5, WholeTestFlow = 6, Whitespace = 7, Other = 8;

    private readonly long[] _stored = new long[PartNames.Length];
    private readonly long[] _wrapperStored = new long[PartNames.Length];
    private readonly long[] _wrapperDecoded = new long[PartNames.Length];
    private readonly HashSet<string> _bodyHashes = new(StringComparer.Ordinal);
    private readonly List<(long Stored, long Decoded)> _scenarios = [];

    private ReportSizes()
    {
    }

    /// <summary>A part's bytes as the file stores them.</summary>
    public long Stored(int part) => _stored[part];

    /// <summary>A part's bytes with every compressed payload in it written as its text.</summary>
    public long Decoded(int part) => _stored[part] - _wrapperStored[part] + _wrapperDecoded[part];

    public long StoredTotal => _stored.Sum();

    public long DecodedTotal => Enumerable.Range(0, PartNames.Length).Sum(Decoded);

    /// <summary>The bytes of the step trees' keys whose value is null, an empty array or an empty object: what writing the steps without them would save.</summary>
    public long StepEmptyKeyBytes { get; private set; }

    /// <summary>How many calls carry a body, a call's request and response counted apart.</summary>
    public int BodyCount { get; private set; }

    public int DistinctBodies => _bodyHashes.Count;

    /// <summary>The decoded bytes of each distinct body, counted once.</summary>
    public long DistinctBodyBytes { get; private set; }

    /// <summary>How many payloads the file stores compressed.</summary>
    public int CompressedPayloads { get; private set; }

    /// <summary>The file's <c>formatVersion</c>, when it declares one.</summary>
    public int? FormatVersion { get; private set; }

    /// <summary>Each scenario's object, stored and decoded, in file order: the position is the scenario's ordinal.</summary>
    public IReadOnlyList<(long Stored, long Decoded)> Scenarios => _scenarios;

    private const int InitialWindow = 128 * 1024;
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Measures the JSON <paramref name="source"/> holds, reading it forward once from where it stands.</summary>
    public static ReportSizes Measure(Stream source) => Measure(source, InitialWindow);

    /// <summary>The same, reading <paramref name="window"/> bytes at a time: a test sets it small, so that reads end everywhere.</summary>
    internal static ReportSizes Measure(Stream source, int window)
    {
        var sizes = new ReportSizes();
        var pass = new Pass(sizes);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(window, 4));
        try
        {
            var filled = 0;
            var eof = false;
            long windowStart = 0;
            var state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            while (true)
            {
                if (!eof)
                {
                    var read = source.Read(buffer, filled, buffer.Length - filled);
                    filled += read;
                    if (read == 0)
                        eof = true;
                }

                var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), eof, state);
                pass.Consume(ref reader, buffer, windowStart);
                state = reader.CurrentState;
                var consumed = (int)reader.BytesConsumed;
                // The reader can step past whitespace, and a separator, after its last token before it finds the window
                // ends; those bytes leave the buffer with the shift below, so they are counted now.
                pass.Skipped(buffer, windowStart, windowStart + consumed);

                if (eof && consumed >= filled)
                    break;

                if (consumed == 0 && filled == buffer.Length)
                {
                    // One token is larger than the whole window, a payload almost always. Double and retry.
                    var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, filled).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                    continue;
                }

                if (eof)
                {
                    // Whatever follows the root value: a line end, or nothing.
                    pass.Trailing(buffer.AsSpan(consumed, filled - consumed));
                    break;
                }

                Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
                filled -= consumed;
                windowStart += consumed;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return sizes;
    }

    private enum Role
    {
        Plain,
        Root,
        Features,
        Feature,
        Scenarios,
        Scenario,
        Background,
        Interactions,
        Interaction,
        Diagrams,
        Payload
    }

    private sealed class Frame
    {
        public Role Role;
        public int Part;
        public bool IsObject;
        public bool InSteps;
        public long Start;
        // A scenario's: its ordinal, and what its compressed payloads add when decoded.
        public int Ordinal = -1;
        public long DecodedAdjust;
        // A payload object's: whether it is a wrapper, the whitespace inside it, and what it decodes to.
        public long Whitespace;
        public string? Hash;
        public long DecodedBytes = -1;
    }

    /// <summary>One pass's position and stack. Its own type, because a ref struct reader cannot live in a lambda.</summary>
    private sealed class Pass(ReportSizes sizes)
    {
        private readonly List<Frame> _stack = [];
        private long _end;
        private int _scenarioOrdinal;

        // The property whose value comes next: its part, its role, and for a step key what dropping it would save so far.
        private bool _pendingProperty;
        private int _pendingPart;
        private Role _pendingRole;
        private bool _pendingInSteps;
        private string? _pendingName;
        private long _pendingKeyCost;
        // An empty-key candidate: a step key whose value opened as [ or {, at this depth, costing this much so far.
        private int _emptyDepth = -1;
        private long _emptyCost;

        // Separators the reader stepped past before a window ended: charged to the next token, as its own gap would be.
        private long _carried;

        public void Skipped(byte[] buffer, long windowStart, long upTo)
        {
            if (upTo <= _end)
                return;
            _carried += Gap(buffer, _end - windowStart, upTo - windowStart);
            _end = upTo;
        }

        public void Consume(ref Utf8JsonReader reader, byte[] buffer, long windowStart)
        {
            while (reader.Read())
            {
                var start = windowStart + reader.TokenStartIndex;
                var end = windowStart + reader.BytesConsumed;
                var gap = Gap(buffer, _end - windowStart, start - windowStart);
                var token = end - start;
                var tokenWhitespace = reader.TokenType == JsonTokenType.PropertyName
                    ? CountWhitespace(buffer.AsSpan((int)(reader.TokenStartIndex + reader.ValueSpan.Length + 2), (int)(token - reader.ValueSpan.Length - 2)))
                    : 0;
                AddWhitespace(tokenWhitespace);
                var bytes = _carried + gap + token - tokenWhitespace;
                _carried = 0;
                _end = end;

                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        Property(ref reader, bytes);
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        Open(reader.TokenType == JsonTokenType.StartObject, start, bytes);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        Close(end, bytes);
                        break;
                    default:
                        Value(ref reader, bytes);
                        break;
                }
            }
        }

        public void Trailing(ReadOnlySpan<byte> rest)
        {
            var whitespace = CountWhitespace(rest);
            sizes._stored[Whitespace] += whitespace;
            sizes._stored[Other] += rest.Length - whitespace + _carried;
            _carried = 0;
        }

        private Frame? Top => _stack.Count == 0 ? null : _stack[^1];

        /// <summary>The separators and whitespace between the last token and this one: whitespace is charged now, the rest returned.</summary>
        private long Gap(byte[] buffer, long from, long to)
        {
            var span = buffer.AsSpan((int)from, (int)(to - from));
            var whitespace = CountWhitespace(span);
            AddWhitespace(whitespace);
            return span.Length - whitespace;
        }

        private void AddWhitespace(long whitespace)
        {
            if (whitespace == 0)
                return;
            sizes._stored[Whitespace] += whitespace;
            // Whitespace inside a wrapper is gone once the wrapper is a string.
            for (var i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i].Role == Role.Payload && _stack[i].IsObject)
                {
                    _stack[i].Whitespace += whitespace;
                    break;
                }
        }

        private void Property(ref Utf8JsonReader reader, long bytes)
        {
            var frame = Top!;
            var name = reader.GetString()!;
            var (part, role) = frame.Role switch
            {
                Role.Root => name switch
                {
                    "features" => (Other, Role.Features),
                    "background" => (Calls, Role.Background),
                    "internalFlowSegments" => (InternalFlow, Role.Plain),
                    "wholeTestFlow" => (WholeTestFlow, Role.Plain),
                    _ => (Other, Role.Plain)
                },
                Role.Feature => name == "scenarios" ? (Other, Role.Scenarios) : (Other, Role.Plain),
                Role.Scenario => name switch
                {
                    "steps" or "backgroundSteps" => (Steps, Role.Plain),
                    "httpInteractions" => (Calls, Role.Interactions),
                    "diagrams" => (Diagrams, Role.Diagrams),
                    "annotations" => (Annotations, Role.Plain),
                    _ => (Other, Role.Plain)
                },
                Role.Background => name == "interactions" ? (Calls, Role.Interactions) : (Calls, Role.Plain),
                Role.Interaction => name == "content" ? (Bodies, Role.Payload) : (Calls, Role.Plain),
                _ => (frame.Part, Role.Plain)
            };

            if (frame.Role == Role.Root && name == "formatVersion")
                _pendingName = name;
            else
                _pendingName = frame.Role == Role.Payload ? name : null;

            _pendingProperty = true;
            _pendingPart = part;
            _pendingRole = role;
            _pendingInSteps = frame.InSteps || (frame.Role == Role.Scenario && part == Steps);
            // The comma before the key is in its bytes already, charged from the gap.
            _pendingKeyCost = frame.InSteps ? bytes : 0;
            sizes._stored[part] += bytes;
            _emptyDepth = -1;
        }

        private (int Part, Role Role, bool InSteps) TakeValueContext()
        {
            if (_pendingProperty)
            {
                _pendingProperty = false;
                return (_pendingPart, _pendingRole, _pendingInSteps);
            }

            var frame = Top;
            if (frame is null)
                return (Other, Role.Root, false);
            return frame.Role switch
            {
                Role.Features => (Other, Role.Feature, false),
                Role.Scenarios => (Other, Role.Scenario, false),
                Role.Interactions => (Calls, Role.Interaction, false),
                Role.Diagrams => (Diagrams, Role.Payload, false),
                _ => (frame.Part, Role.Plain, frame.InSteps)
            };
        }

        private void Open(bool isObject, long start, long bytes)
        {
            var keyCost = _pendingKeyCost;
            var parentInSteps = Top?.InSteps ?? false;
            var (part, role, inSteps) = TakeValueContext();
            sizes._stored[part] += bytes;

            var frame = new Frame { Role = role, Part = part, IsObject = isObject, InSteps = inSteps, Start = start };
            if (role == Role.Scenario)
                frame.Ordinal = _scenarioOrdinal++;
            _stack.Add(frame);

            // A step key whose value opens a container is empty if the next token closes it.
            if (parentInSteps && keyCost > 0)
            {
                _emptyDepth = _stack.Count;
                _emptyCost = keyCost + bytes;
            }
            else
                _emptyDepth = -1;
            _pendingKeyCost = 0;
        }

        private void Close(long end, long bytes)
        {
            var frame = Top!;
            sizes._stored[frame.Part] += bytes;
            if (_emptyDepth == _stack.Count)
                sizes.StepEmptyKeyBytes += _emptyCost + bytes;
            _emptyDepth = -1;
            _stack.RemoveAt(_stack.Count - 1);

            if (frame.Role == Role.Scenario)
            {
                var stored = end - frame.Start;
                while (sizes._scenarios.Count <= frame.Ordinal)
                    sizes._scenarios.Add((0, 0));
                sizes._scenarios[frame.Ordinal] = (stored, stored + frame.DecodedAdjust);
            }
            else if (frame.Role == Role.Payload && frame.IsObject && frame.DecodedBytes >= 0)
            {
                // A wrapper: what it stores, and the string it stands for.
                var extent = end - frame.Start;
                sizes._wrapperStored[frame.Part] += extent - frame.Whitespace;
                sizes._wrapperStored[Whitespace] += frame.Whitespace;
                sizes._wrapperDecoded[frame.Part] += frame.DecodedBytes;
                sizes.CompressedPayloads++;
                for (var i = _stack.Count - 1; i >= 0; i--)
                    if (_stack[i].Role == Role.Scenario)
                    {
                        _stack[i].DecodedAdjust += frame.DecodedBytes - extent;
                        break;
                    }
                if (frame.Part == Bodies)
                    Body(frame.Hash ?? "", frame.DecodedBytes);
            }
        }

        private void Value(ref Utf8JsonReader reader, long bytes)
        {
            var keyCost = _pendingKeyCost;
            var parentInSteps = Top?.InSteps ?? false;
            var name = _pendingName;
            _pendingName = null;
            var (part, role, _) = TakeValueContext();
            sizes._stored[part] += bytes;
            _pendingKeyCost = 0;
            _emptyDepth = -1;

            if (parentInSteps && keyCost > 0 && reader.TokenType == JsonTokenType.Null)
                sizes.StepEmptyKeyBytes += keyCost + bytes;

            var frame = Top;
            if (name == "formatVersion" && frame?.Role == Role.Root && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version))
                sizes.FormatVersion = version;

            if (frame is { Role: Role.Payload, IsObject: true })
            {
                // Inside a wrapper.
                if (name == "$h" && reader.TokenType == JsonTokenType.String)
                    frame.Hash = reader.GetString();
                else if (name == "$z" && reader.TokenType == JsonTokenType.String)
                {
                    var text = ReportPayloads.Inflate(reader.GetString()!);
                    frame.DecodedBytes = JsonEncodedText.Encode(text, Encoder).EncodedUtf8Bytes.Length + 2;
                }
                return;
            }

            if (role == Role.Payload && part == Bodies && reader.TokenType == JsonTokenType.String)
            {
                // A body's own bytes are its token, quotes included; the comma and colon before it are its key's.
                var utf8 = reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan.ToArray();
                Body(ReportPayloads.Address(utf8), reader.BytesConsumed - reader.TokenStartIndex);
            }
        }

        private void Body(string hash, long decoded)
        {
            sizes.BodyCount++;
            if (sizes._bodyHashes.Add(hash))
                sizes.DistinctBodyBytes += decoded;
        }
    }

    private static long CountWhitespace(ReadOnlySpan<byte> bytes)
    {
        long count = 0;
        foreach (var b in bytes)
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                count++;
        return count;
    }
}
