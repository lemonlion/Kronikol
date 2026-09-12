using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// What <c>--json</c> has to state before the first row: which file was read, by which Kronikol, to
/// answer which question. Null everywhere else, which is the default — text is what an agent reading a
/// terminal should ask for, because the same answer as JSON costs it roughly twice the tokens.
/// </summary>
/// <summary>
/// What the envelope knows about the call that produced it. <paramref name="Addresses"/> is every
/// positional after the report - a scenario, an interaction, the two ends of a diff - because a resume
/// pointer that drops them silently widens the question it was resuming.
/// </summary>
internal sealed record QueryEnvelope(string Command, string Report, string? KronikolVersion,
    IReadOnlyList<string> Addresses);

/// <summary>
/// Writes a command's answer under a byte budget, as text or as one JSON envelope.
///
/// <para>The budget is the whole point of the tool, so it is enforced here rather than trusted to each
/// command. Two rules follow from it. Truncation always announces itself, with the exact flags that
/// resume — an agent that cannot tell whether it saw everything has to go back to reading the file, which
/// is the failure this exists to prevent. And nothing is ever silently dropped: where a payload would go,
/// a pointer goes instead, saying how big it is and what to ask for.</para>
///
/// <para>The format lives here for the same reason. Every verb writes through this one class and it is
/// constructed in exactly one place, so <c>--json</c> costs each verb a projector lambda rather than a
/// second rendering path — and no verb can page, truncate or warn in one format and not the other.</para>
/// </summary>
internal sealed class QueryWriter
{
    /// <summary>
    /// Bumped when a consumer would have to change, and written as the first key of the object so it can
    /// be checked before anything else is read — the contract <c>Failures.jsonl</c> already uses.
    /// </summary>
    public const int JsonFormatVersion = 1;

    /// <summary>
    /// camelCase and the default escaping, matching <c>FailuresDigestGenerator.BuildJsonl</c>: one product
    /// should not ship two JSON dialects for the same concepts.
    /// </summary>
    private static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TextWriter _output;
    private readonly QueryEnvelope? _envelope;
    private readonly string? _outPath;
    private readonly int _maxBytes;

    private readonly StringBuilder _buffer = new();
    private readonly List<string> _notes = [];
    private readonly List<string> _items = [];
    private readonly List<KeyValuePair<string, string>> _data = [];

    private int _bytes;
    private bool _overBudget;
    private string? _footer;
    private int? _count;
    private int? _total;
    private IReadOnlyList<string>? _next;
    private List<string>? _capture;

    /// <summary>
    /// Builds the writer every command answers through.
    /// <paramref name="output"/> is where the answer goes when <paramref name="outPath"/> is null;
    /// <paramref name="maxBytes"/> is the budget, or 0 for none, and is ignored when writing to a file;
    /// <paramref name="envelope"/> is non-null for <c>--json</c> and null for the text path;
    /// <paramref name="outPath"/> names a file to write the answer to instead of the terminal.
    /// </summary>
    public QueryWriter(TextWriter output, int maxBytes, QueryEnvelope? envelope = null, string? outPath = null)
    {
        _output = output;
        _envelope = envelope;
        _outPath = outPath;
        // A file is not a context window. `--out` exists to escape the budget - which is what it has
        // always meant on the payload verbs - so honouring the budget as well would defeat it.
        _maxBytes = outPath is null ? maxBytes : 0;
    }

    /// <summary>True when the answer is one JSON object rather than lines of text.</summary>
    public bool Json => _envelope is not null;

    public bool Truncated => _overBudget;

    /// <summary>
    /// One line of the text answer. A no-op under <c>--json</c>: column headers, indentation and prose
    /// are the text rendering, not the answer, and a script that had to strip them out of an array would
    /// be parsing a terminal again.
    /// </summary>
    public void Line(string text = "")
    {
        if (_capture is not null)
        {
            _capture.Add(text);
            return;
        }

        if (Json || _overBudget)
            return;

        var cost = Encoding.UTF8.GetByteCount(text) + 1;
        if (_maxBytes > 0 && _bytes + cost > _maxBytes)
        {
            _overBudget = true;
            return;
        }

        _buffer.Append(text).Append('\n');
        _bytes += cost;
    }

    /// <summary>
    /// A line that is not part of the answer: a <c>!</c> warning, or the sentence that stands in for an
    /// empty result. Text prints it where it stands; JSON collects it into <c>notes</c>, because the
    /// absence of services is the whole answer to <c>services</c> and must not arrive as an empty array
    /// with nothing said.
    /// </summary>
    public void Note(string text)
    {
        if (Json)
            _notes.Add(text);
        else
            Line(text);
    }

    /// <summary>
    /// The answer to <c>--count</c>. One place, so the thirteen verbs that support it cannot fork: text
    /// prints the bare number and nothing else, JSON gives an envelope with <c>count</c> and no
    /// <c>items</c>.
    /// </summary>
    public void Count(int value)
    {
        if (Json)
            _count = value;
        else
            Line(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A member of the envelope beside <c>items</c>. <c>summary</c> is five heterogeneous sections and no
    /// list at all, so flattening it into one array would be a lie; this is how a verb says what shape it
    /// actually has. Ignored on the text path, which renders those sections itself.
    /// </summary>
    public void Data(string name, object? value)
    {
        if (Json)
            _data.Add(new KeyValuePair<string, string>(name, JsonSerializer.Serialize(value, Compact)));
    }

    /// <summary>
    /// One element of <c>items</c>, costed whole. Half a serialized object is not JSON, so a row that
    /// does not fit is dropped entirely and the envelope says it was truncated — unlike the text path,
    /// where a row can be cut mid-way and the reader can still see what it was.
    /// Returns false when the row did not fit, which is also when the budget has just run out.
    /// </summary>
    public bool Item(object value)
    {
        // Silently inert on the text path, so a verb can project every row unconditionally rather than
        // wrapping each call in a format check - and so a stray projection can never spend the text budget.
        if (!Json)
            return !_overBudget;

        if (_overBudget)
            return false;

        var fragment = JsonSerializer.Serialize(value, Compact);
        var cost = Encoding.UTF8.GetByteCount(fragment) + 1;
        if (_maxBytes > 0 && _bytes + cost > _maxBytes)
        {
            _overBudget = true;
            return false;
        }

        _items.Add(fragment);
        _bytes += cost;
        return true;
    }

    /// <summary>
    /// The line printed after the body of the answer whatever happens — how much was shown, and the exact
    /// re-run that shows the next part. A no-op under <c>--json</c>, where <c>total</c> and <c>next</c>
    /// carry the same facts in a form a script can act on.
    /// </summary>
    public void Footer(string text)
    {
        if (!Json)
            _footer = text;
    }

    /// <summary>
    /// Writes the assembled answer, to <c>--out</c> when one was given and to the terminal otherwise.
    /// False, with the reason already on <paramref name="error"/>, when the file could not be written.
    /// </summary>
    public bool Flush(TextWriter error)
    {
        var text = Json ? BuildEnvelope() : BuildText();

        if (_outPath is null)
        {
            _output.Write(text);
            return true;
        }

        try
        {
            File.WriteAllText(_outPath, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not write {_outPath}: {exception.Message}");
            return false;
        }

        _output.Write($"wrote {Size(Encoding.UTF8.GetByteCount(text))} → {Path.GetFullPath(_outPath)}\n");
        return true;
    }

    /// <summary>
    /// Renders a paged listing and its footer in one place, so no command can page without saying so.
    ///
    /// <para><paramref name="json"/> projects one row into the object <c>items</c> holds; without it a
    /// row's rendered text is wrapped instead, which keeps a verb honest rather than answering with an
    /// empty array. <paramref name="whenComplete"/> replaces the exhausted-listing footer for the verbs
    /// whose closing line says something a row count cannot — that a service missing from the table was
    /// never called, or where to go after the failures.</para>
    /// </summary>
    public void Page<T>(IReadOnlyList<T> all, int offset, int limit, string noun, Action<T> render,
        IReadOnlyList<string>? rerunArgs = null, Func<T, object>? json = null, string? whenComplete = null)
    {
        var shown = 0;
        for (var i = offset; i < all.Count && shown < limit; i++)
        {
            if (!Render(all[i], render, json))
                break;
            shown++;
        }

        var last = offset + shown;
        // Three endings, and only the first used to be told apart from the others. An offset past the end
        // and a first row too big for the budget both showed nothing and both answered
        // `next: --offset {offset}` - the offset already asked for. A reader shrugs and raises the budget;
        // a script that follows `next` re-runs the identical command forever.
        var past = offset >= all.Count;
        var stuck = !past && shown == 0;
        var complete = last >= all.Count;

        if (Json)
        {
            _total = all.Count;
            // An argument vector, not a string: the report path and every filter value reach the consumer
            // as their own element, so a service called "Dessert Provider" cannot arrive as two arguments.
            // It carries the page size too - a pointer that resumes at a different width has not resumed.
            _next = complete || stuck
                ? null
                : ["query", _envelope!.Command, _envelope.Report, .. _envelope.Addresses, .. rerunArgs ?? [],
                   "--limit", limit.ToString(CultureInfo.InvariantCulture),
                   "--offset", last.ToString(CultureInfo.InvariantCulture)];
            if (stuck)
                _notes.Add($"row {offset + 1} is larger than the {_maxBytes} byte budget — raise --max-bytes");
            return;
        }

        Footer(past
            ? $"{noun}: nothing at --offset {offset} · {all.Count} {noun} in total"
            : stuck
                ? $"{noun}: nothing fits — row {offset + 1} is larger than {_maxBytes} bytes · raise with --max-bytes"
                : complete
                    ? whenComplete ?? (offset == 0 ? $"{all.Count} {noun}" : $"{noun}: {offset + 1}-{last} of {all.Count}")
                    : $"{noun}: {offset + 1}-{last} of {all.Count} · next: {Prefix(rerunArgs)}--offset {last}");
    }

    /// <summary>Rerun tokens as a pasteable prefix, with the trailing space a footer appends its offset to.</summary>
    private static string Prefix(IReadOnlyList<string>? args) =>
        args is null || args.Count == 0 ? "" : QueryOptions.Shell(args) + " ";

    /// <summary>One row, in whichever format is in force. False when it did not fit.</summary>
    private bool Render<T>(T row, Action<T> render, Func<T, object>? json)
    {
        if (!Json)
        {
            render(row);
            return !_overBudget;
        }

        if (json is not null)
            return Item(json(row));

        var captured = _capture = [];
        try
        {
            render(row);
        }
        finally
        {
            _capture = null;
        }

        return Item(new { text = string.Join("\n", captured) });
    }

    private string BuildText()
    {
        var text = new StringBuilder(_buffer.ToString());

        if (_overBudget)
            text.Append("… output truncated at ").Append(_maxBytes.ToString(CultureInfo.InvariantCulture))
                .Append(" bytes · raise with --max-bytes, or filter harder\n");

        if (_footer is not null)
            text.Append(_footer).Append('\n');

        return text.ToString();
    }

    /// <summary>
    /// The envelope, assembled by hand rather than by serializing one object: each item was already
    /// serialized and costed on its own, and re-serializing them together would either double the work or
    /// lose the guarantee that what is inside <c>items</c> is exactly what the budget accounted for.
    /// </summary>
    private string BuildEnvelope()
    {
        var envelope = new StringBuilder("{");

        void Member(string name, string value)
        {
            if (envelope.Length > 1)
                envelope.Append(',');
            envelope.Append(JsonSerializer.Serialize(name, Compact)).Append(':').Append(value);
        }

        Member("formatVersion", JsonFormatVersion.ToString(CultureInfo.InvariantCulture));
        Member("command", JsonSerializer.Serialize(_envelope!.Command, Compact));
        Member("report", JsonSerializer.Serialize(_envelope.Report, Compact));
        Member("kronikolVersion", JsonSerializer.Serialize(_envelope.KronikolVersion, Compact));
        Member("notes", JsonSerializer.Serialize(_notes, Compact));

        foreach (var (name, value) in _data)
            Member(name, value);

        // `--count` answers one number, so an empty `items` beside it would invite a script to iterate it.
        if (_count is { } count)
        {
            Member("count", count.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Member("items", "[" + string.Join(",", _items) + "]");
            if (_total is { } total)
                Member("total", total.ToString(CultureInfo.InvariantCulture));
        }

        Member("truncated", _overBudget ? "true" : "false");
        Member("next", _next is null ? "null" : JsonSerializer.Serialize(_next, Compact));

        return envelope.Append("}\n").ToString();
    }

    /// <summary>
    /// Formats a size the way the elision markers do. Bytes below a kilobyte, kilobytes above — precision
    /// past that is noise in a decision about whether to fetch something.
    /// </summary>
    public static string Size(int bytes) =>
        bytes < 1024 ? bytes + " B" : (bytes / 1024.0).ToString("0.#") + " KB";

    public static string Duration(double? ms) =>
        ms is null ? "" : ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.##} s";

    /// <summary>One line of text with its newlines flattened, so a listing stays one row per item.</summary>
    public static string OneLine(string? text, int max = 160)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var flat = text.ReplaceLineEndings(" ").Trim();
        while (flat.Contains("  ", StringComparison.Ordinal))
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
