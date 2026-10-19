using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kronikol.Compression;

/// <summary>
/// A write-only stream that takes a report's JSON as the serializer writes it and passes it on compact, without any key
/// whose value is null, an empty array or an empty object, unless the schema requires that key where it stands
/// (plans/REPORT_FORMAT_PLAN.md 4.2). An object or array left empty by that is left out the same way. Array elements are
/// kept whatever they hold, so no index moves, and every string and number is written as it came, escapes and all.
/// </summary>
/// <remarks>
/// It works token by token on the writing thread, so the JSON is never held whole: a key and the start of its value wait
/// only until something worth writing appears inside, or the value ends empty. <see cref="Complete"/> ends the document.
/// </remarks>
internal sealed class ReportJsonPruner : Stream
{
    /// <summary>Deeper than any serializer writes by default, so the reader never refuses what the writer produced.</summary>
    private const int MaxDepth = 256;

    /// <summary>How much the window holds before it is read: tokens are passed on in batches, not on every write.</summary>
    private const int Batch = 32 * 1024;

    private readonly Utf8JsonWriter _writer;
    private readonly RequiredKeys.Node _root;
    private readonly List<Frame> _frames = [];
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
    private int _filled;
    private int _processAt = Batch;
    private JsonReaderState _state = new(new JsonReaderOptions { MaxDepth = MaxDepth });

    // Frames below this index are written; the rest wait for something worth writing inside them.
    private int _firstUnwritten;

    // The key just read, whose value has not shown yet: unescaped, with what the schema says of it.
    private byte[] _name = ArrayPool<byte>.Shared.Rent(256);
    private int _nameLength = -1;
    private bool _nameRequired;
    private RequiredKeys.Node? _nameSchema;

    private bool _completed;

    /// <summary>Writes the pruned JSON to <paramref name="output"/>, which it does not dispose.</summary>
    public ReportJsonPruner(Stream output, RequiredKeys required)
    {
        _writer = new Utf8JsonWriter(output, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = MaxDepth });
        _root = required.Root;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_completed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Passes on what is written so far; the document is not ended (<see cref="Complete"/> does that).</summary>
    public override void Flush() => _writer.Flush();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        while (buffer.Length > 0)
        {
            if (_filled == _buffer.Length)
            {
                // One token is larger than the window: double it, and read on.
                var bigger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                _buffer.AsSpan(0, _filled).CopyTo(bigger);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = bigger;
            }

            var taken = Math.Min(buffer.Length, _buffer.Length - _filled);
            buffer[..taken].CopyTo(_buffer.AsSpan(_filled));
            _filled += taken;
            buffer = buffer[taken..];
            if (_filled >= _processAt)
            {
                var before = _filled;
                Process(isFinalBlock: false);
                // Nothing whole in the window (one token larger than it): read twice as much before trying again, so a long
                // value is scanned a few times, not once per write.
                _processAt = _filled == before ? before * 2 : Batch;
            }
        }
    }

    /// <summary>Ends the document, and throws a <see cref="JsonException"/> when what was written is not one whole JSON value.</summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        // On the final block the reader refuses a document that holds no value, ends inside one, or goes on after it.
        Process(isFinalBlock: true);
        _writer.Flush();
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writer.Dispose();
            foreach (var frame in _frames)
            {
                if (frame.Name is not null)
                    ArrayPool<byte>.Shared.Return(frame.Name);
            }
            _frames.Clear();
            ArrayPool<byte>.Shared.Return(_buffer);
            ArrayPool<byte>.Shared.Return(_name);
            _buffer = [];
            _name = [];
            _completed = true;
        }
        base.Dispose(disposing);
    }

    private void Process(bool isFinalBlock)
    {
        var reader = new Utf8JsonReader(_buffer.AsSpan(0, _filled), isFinalBlock, _state);
        while (reader.Read())
            Handle(ref reader);

        _state = reader.CurrentState;
        var consumed = (int)reader.BytesConsumed;
        if (consumed > 0)
        {
            Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _filled - consumed);
            _filled -= consumed;
        }

        // Pass the output on as it is made: until it is flushed, the writer holds it in a buffer that grows to the size of
        // the whole document, and every growth is a new array.
        if (_writer.BytesPending >= Batch)
            _writer.Flush();
    }

    private void Handle(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                ReadName(ref reader);
                break;
            case JsonTokenType.StartObject:
                OpenFrame(isObject: true);
                break;
            case JsonTokenType.StartArray:
                OpenFrame(isObject: false);
                break;
            case JsonTokenType.EndObject:
            case JsonTokenType.EndArray:
                CloseFrame();
                break;
            case JsonTokenType.Null:
                if (_nameLength >= 0 && !_nameRequired)
                {
                    _nameLength = -1;
                    break;
                }
                BeforeValue();
                _writer.WriteNullValue();
                break;
            case JsonTokenType.String:
                BeforeValue();
                // As it came: the quotes and the escapes the serializer wrote.
                _writer.WriteRawValue(_buffer.AsSpan((int)reader.TokenStartIndex, reader.ValueSpan.Length + 2), skipInputValidation: true);
                break;
            case JsonTokenType.Number:
                BeforeValue();
                _writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                break;
            case JsonTokenType.True:
                BeforeValue();
                _writer.WriteBooleanValue(true);
                break;
            case JsonTokenType.False:
                BeforeValue();
                _writer.WriteBooleanValue(false);
                break;
        }
    }

    private void ReadName(ref Utf8JsonReader reader)
    {
        var raw = reader.ValueSpan;
        if (_name.Length < raw.Length)
        {
            ArrayPool<byte>.Shared.Return(_name);
            _name = ArrayPool<byte>.Shared.Rent(raw.Length);
        }

        if (reader.ValueIsEscaped)
            _nameLength = reader.CopyString(_name);
        else
        {
            raw.CopyTo(_name);
            _nameLength = raw.Length;
        }

        var schema = _frames[^1].Schema;
        var name = _name.AsSpan(0, _nameLength);
        _nameRequired = schema?.Requires(name) == true;
        _nameSchema = schema?.Property(name);
    }

    /// <summary>A value worth writing: every frame waiting above it is written first, then the key it stands under.</summary>
    private void BeforeValue()
    {
        WriteWaitingFrames();
        if (_nameLength >= 0)
        {
            _writer.WritePropertyName(_name.AsSpan(0, _nameLength));
            _nameLength = -1;
        }
    }

    private void OpenFrame(bool isObject)
    {
        RequiredKeys.Node? schema;
        byte[]? name = null;
        var nameLength = 0;
        bool keep;
        if (_frames.Count == 0)
        {
            schema = _root;
            keep = true;
        }
        else if (_frames[^1].IsObject)
        {
            // A key's value: written once something worth writing appears inside it, or at once when the key is required.
            schema = _nameSchema;
            name = ArrayPool<byte>.Shared.Rent(Math.Max(_nameLength, 1));
            _name.AsSpan(0, _nameLength).CopyTo(name);
            nameLength = _nameLength;
            keep = _nameRequired;
            _nameLength = -1;
        }
        else
        {
            // An array's element: always written, so that no index moves.
            schema = _frames[^1].Schema?.Items;
            keep = true;
        }

        _frames.Add(new Frame(isObject, schema, name, nameLength));
        if (keep)
            WriteWaitingFrames();
    }

    private void CloseFrame()
    {
        var index = _frames.Count - 1;
        var frame = _frames[index];
        _frames.RemoveAt(index);
        if (index < _firstUnwritten)
        {
            if (frame.IsObject)
                _writer.WriteEndObject();
            else
                _writer.WriteEndArray();
        }

        // A frame never written held nothing worth writing, and its key is not required: it is left out, key and all.
        _firstUnwritten = Math.Min(_firstUnwritten, _frames.Count);
        if (frame.Name is not null)
            ArrayPool<byte>.Shared.Return(frame.Name);
    }

    private void WriteWaitingFrames()
    {
        for (; _firstUnwritten < _frames.Count; _firstUnwritten++)
        {
            var frame = _frames[_firstUnwritten];
            if (frame.Name is not null)
                _writer.WritePropertyName(frame.Name.AsSpan(0, frame.NameLength));
            if (frame.IsObject)
                _writer.WriteStartObject();
            else
                _writer.WriteStartArray();
        }
    }

    /// <summary>An open object or array: the schema node it stands in, and the key it opened under (null for an element or the root).</summary>
    private readonly record struct Frame(bool IsObject, RequiredKeys.Node? Schema, byte[]? Name, int NameLength);
}
