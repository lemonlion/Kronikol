using System.Text;
using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// Fetches the parts of the file the index deliberately left behind — bodies, header blocks, diagrams —
/// by seeking to the byte range recorded for them. Nothing here is called unless a command was explicitly
/// asked for a payload.
/// </summary>
internal static class PayloadReader
{
    public static string? Read(ReportIndex index, Slice slice)
    {
        if (!slice.Exists)
            return null;

        using var stream = File.OpenRead(index.Path);
        stream.Seek(slice.Offset, SeekOrigin.Begin);
        var raw = new byte[slice.Length];
        stream.ReadExactly(raw);

        try
        {
            return JsonSerializer.Deserialize<string>(raw);
        }
        catch (JsonException)
        {
            return Encoding.UTF8.GetString(raw);
        }
    }

    /// <summary>The header block of one interaction, as key/value pairs.</summary>
    public static List<(string Key, string? Value)> Headers(ReportIndex index, InteractionEntry interaction)
    {
        var found = new List<(string, string?)>();
        if (interaction.HeaderCount == 0 || !interaction.Headers.Exists)
            return found;

        // The recorded slice is the first key inside the array; step back to the array itself and read it.
        using var stream = File.OpenRead(index.Path);
        var start = FindArrayStart(stream, interaction.Headers.Offset);
        if (start < 0)
            return found;

        stream.Seek(start, SeekOrigin.Begin);
        var length = (int)Math.Min(index.FileLength - start, 1 << 20);
        var raw = new byte[length];
        var got = stream.Read(raw, 0, length);

        var reader = new Utf8JsonReader(raw.AsSpan(0, got), isFinalBlock: false, state: default);
        string? key = null;
        var depth = 0;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray or JsonTokenType.StartObject:
                    depth++;
                    break;
                case JsonTokenType.EndObject:
                    depth--;
                    break;
                case JsonTokenType.EndArray:
                    if (--depth == 0)
                        return found;
                    break;
                case JsonTokenType.PropertyName:
                    key = reader.GetString();
                    break;
                default:
                    if (key == "key")
                        found.Add((reader.GetString() ?? "", null));
                    else if (key == "value" && found.Count > 0)
                        found[^1] = (found[^1].Item1, reader.TokenType == JsonTokenType.Null ? null : reader.GetString());
                    break;
            }
        }

        return found;
    }

    private static long FindArrayStart(FileStream stream, long fromKeyToken)
    {
        // Walk back over `{"key":` and the `[` that opens the header array. A short bounded scan: the
        // preamble is a handful of bytes and a malformed file simply yields no headers.
        var window = (int)Math.Min(fromKeyToken, 64);
        stream.Seek(fromKeyToken - window, SeekOrigin.Begin);
        var raw = new byte[window];
        stream.ReadExactly(raw);

        for (var i = raw.Length - 1; i >= 0; i--)
            if (raw[i] == (byte)'[')
                return fromKeyToken - window + i;

        return -1;
    }

    /// <summary>
    /// Resolves a path (<c>$.data.customers[2].total</c>) against a JSON body: the first match, or null
    /// when the path selects nothing — which is itself an answer worth printing. The thin wrapper over
    /// <see cref="PathEngine"/> for call sites that want one value, not a fan-out.
    /// </summary>
    public static string? Path(string body, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!PathEngine.TryParse(path, out var segments, out _))
                return null;
            foreach (var (_, value) in PathEngine.SelectAll(document.RootElement, segments))
                return value.Text();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A line range of a payload, one-based and inclusive, the way an editor addresses lines.</summary>
    public static string Lines(string body, int from, int to)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        from = Math.Max(1, from);
        to = Math.Min(lines.Length, to);
        if (from > lines.Length)
            return "";

        var builder = new StringBuilder();
        for (var i = from; i <= to; i++)
            builder.Append(i).Append('\t').Append(lines[i - 1]).Append('\n');
        return builder.ToString();
    }

    /// <summary>
    /// Pretty-prints a JSON body, leaving anything else as it was. Report bodies are usually captured
    /// minified, and a line range is only addressable once there are lines.
    /// </summary>
    public static string Pretty(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return body;
        }
    }

    /// <summary>
    /// Splits a PlantUML diagram into its note blocks. The escape hatch behind <c>query note</c>: a note is
    /// a rendering of a payload rather than a copy of it, so when what the user saw in the HTML cannot be
    /// found in the captured content, this is where it is.
    /// </summary>
    public static List<(int Index, string Text)> Notes(string diagram)
    {
        var notes = new List<(int, string)>();
        var lines = diagram.ReplaceLineEndings("\n").Split('\n');
        var current = new StringBuilder();
        var inNote = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (!inNote && (trimmed.StartsWith("note ", StringComparison.Ordinal) || trimmed.StartsWith("hnote ", StringComparison.Ordinal)
                                                                                 || trimmed.StartsWith("rnote ", StringComparison.Ordinal)))
            {
                // A one-line note carries its text after a colon; a block note runs to `end note`.
                var colon = trimmed.IndexOf(" : ", StringComparison.Ordinal);
                if (colon >= 0)
                {
                    notes.Add((notes.Count, trimmed[(colon + 3)..].Trim()));
                    continue;
                }

                inNote = true;
                current.Clear();
                continue;
            }

            if (inNote)
            {
                if (trimmed.StartsWith("end note", StringComparison.Ordinal))
                {
                    notes.Add((notes.Count, current.ToString().TrimEnd()));
                    inNote = false;
                    continue;
                }
                current.Append(line).Append('\n');
            }
        }

        return notes;
    }
}
