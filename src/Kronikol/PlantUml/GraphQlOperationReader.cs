using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kronikol.PlantUml;

/// <summary>
/// The GraphQL operation a request names: its type, its name, and for an anonymous one its root fields, sorted and
/// de-duplicated (aliases, arguments, directives and nested selections dropped; <c>...Name</c> for a fragment spread,
/// <c>... on Type</c> for an inline fragment). A persisted query (<c>extensions.persistedQuery</c>) is named by its
/// <c>operationName</c> alone, whether the request carries the query text or only its hash, so the two forms of one
/// operation are one operation.
/// </summary>
internal sealed record GraphQlOperation(string? Type, string? Name, IReadOnlyList<string>? RootFields, bool Persisted)
{
    /// <summary>
    /// What a reader is shown and what keys the call: <c>query GetUser</c>, <c>query { orderSummaries }</c>, or a
    /// persisted query's name, <c>GetUser</c>.
    /// </summary>
    public string Label =>
        Persisted && Name is not null ? Name
        : Name is not null ? $"{Type} {Name}"
        : RootFields is { Count: > 0 } fields ? $"{Type} {{ {string.Join(' ', fields)} }}"
        : Type ?? "";

    /// <summary>
    /// <see cref="GraphQlOperationDetector.TryExtractLabel"/>'s documented form: the type and the name, the type alone for
    /// an anonymous operation, a persisted query's name when the request carries no query text.
    /// </summary>
    public string DetectorLabel =>
        Name is not null ? (Type is null ? Name : $"{Type} {Name}") : Type ?? "";
}

/// <summary>
/// Reads a request's GraphQL operation the way a server reads it (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.2): the
/// body's top level as JSON, so a key named <c>query</c> in the variables or in an input object is not the query; the
/// <c>query</c> string parsed as a GraphQL document, so a search API's <c>query</c> member is not an operation; and the
/// operation picked by <c>operationName</c>, else the document's first. One reader for the diagrams' arrow labels, the
/// key that collapses a repeated arrow, the note formatter and the operation key.
/// </summary>
internal static class GraphQlOperationReader
{
    /// <summary>The operation a POST body names, or null when the body is not a GraphQL request.</summary>
    public static GraphQlOperation? FromBody(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        var span = content.AsSpan().TrimStart();
        if (span.Length == 0 || span[0] != '{')
            return null;
        // Most JSON bodies are not GraphQL: one ordinal scan spares them the parse.
        if (!content.Contains("\"query\"", StringComparison.Ordinal) && !content.Contains("persistedQuery", StringComparison.Ordinal))
            return null;
        return FromTopLevel(TopLevel.Read(content));
    }

    /// <summary>
    /// The operation a GET names in its query string (<c>query</c>, <c>operationName</c>, <c>extensions</c>), read as the
    /// body they stand for; null when the parameters are not a GraphQL request, as a search API's <c>?query=</c> is not.
    /// </summary>
    public static GraphQlOperation? FromQueryString(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        var question = uri.IndexOf('?');
        if (question < 0)
            return null;
        var hash = uri.IndexOf('#', question);
        var text = hash < 0 ? uri[(question + 1)..] : uri[(question + 1)..hash];
        string? query = null, operationName = null, extensions = null;
        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Decode(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
            switch (key)
            {
                case "query": query ??= value; break;
                case "operationName": operationName ??= value; break;
                case "extensions": extensions ??= value; break;
            }
        }
        if (query is null && operationName is null)
            return null;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            if (query is not null)
                writer.WriteString("query", query);
            if (operationName is not null)
                writer.WriteString("operationName", operationName);
            if (extensions is not null)
            {
                writer.WritePropertyName("extensions");
                try
                {
                    using var parsed = JsonDocument.Parse(extensions);
                    parsed.RootElement.WriteTo(writer);
                }
                catch (JsonException)
                {
                    writer.WriteStringValue(extensions);
                }
            }
            writer.WriteEndObject();
        }
        return FromTopLevel(TopLevel.Read(Encoding.UTF8.GetString(stream.ToArray())));

        static string Decode(string part)
        {
            try
            {
                return Uri.UnescapeDataString(part.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return part;
            }
        }
    }

    /// <summary>
    /// Whether a body is a batch: a JSON array whose every element is a GraphQL request. One call that carries several
    /// operations; an array of anything else, a REST bulk endpoint's, is not one.
    /// </summary>
    public static bool IsBatch(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        var span = content.AsSpan().TrimStart();
        if (span.Length == 0 || span[0] != '[')
            return false;
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var elements = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object || FromTopLevel(TopLevel.Read(element.GetRawText())) is null)
                    return false;
                elements++;
            }
            return elements > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GraphQlOperation? FromTopLevel(TopLevel top)
    {
        if (!top.IsObject)
            return null;
        if (top.Query is { } text)
        {
            var document = GraphQlDocument.Parse(text);
            if (document.Operations.Count == 0)
                return null;
            if (document.Error is not null && document.Operations.All(o => o.RootFields is null))
                return null;
            if (top.OperationName is { Length: > 0 } name)
            {
                var selected = document.Operations.FirstOrDefault(o => o.Name == name) ?? document.Operations[0];
                return new GraphQlOperation(selected.Type, name, null, top.HasPersistedQuery);
            }
            var operation = document.Operations[0];
            return operation.Name is not null
                ? new GraphQlOperation(operation.Type, operation.Name, null, top.HasPersistedQuery)
                : new GraphQlOperation(operation.Type, null, Sorted(operation.RootFields), false);
        }
        // A persisted query sent by its hash alone: the name is all there is.
        if ((!top.HasQuery || top.QueryToken == JsonTokenType.Null) && top.HasPersistedQuery && top.OperationName is { Length: > 0 } persisted)
            return new GraphQlOperation(null, persisted, null, true);
        return null;
    }

    private static IReadOnlyList<string>? Sorted(IReadOnlyList<string>? fields) =>
        fields?.Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    /// <summary>A request body's top level, read as JSON: what the operation needs and nothing else (variables are skipped).</summary>
    private sealed record TopLevel(bool IsObject, bool HasQuery, JsonTokenType QueryToken, string? Query, string? OperationName, bool HasPersistedQuery)
    {
        private static readonly TopLevel None = new(false, false, JsonTokenType.None, null, null, false);

        public static TopLevel Read(string body)
        {
            var bytes = Encoding.UTF8.GetBytes(EscapeControlsInStrings(body));
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 512 });
            bool hasQuery = false, persisted = false;
            var queryToken = JsonTokenType.None;
            string? query = null, operationName = null;
            try
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return None;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    if (!reader.Read())
                        break;
                    switch (name)
                    {
                        case "query":
                            hasQuery = true;
                            queryToken = reader.TokenType;
                            if (queryToken == JsonTokenType.String)
                                query = reader.GetString();
                            else
                                reader.Skip();
                            break;
                        case "operationName":
                            if (reader.TokenType == JsonTokenType.String)
                                operationName = reader.GetString();
                            else
                                reader.Skip();
                            break;
                        case "extensions" when reader.TokenType == JsonTokenType.StartObject:
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                            {
                                var inner = reader.GetString();
                                if (!reader.Read())
                                    break;
                                if (inner == "persistedQuery")
                                    persisted = true;
                                reader.Skip();
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
            }
            catch (JsonException)
            {
                // A body cut at capture, or not JSON past its first brace: what was read so far stands.
            }
            catch (InvalidOperationException)
            {
                // Skip() over a value the buffer does not hold whole: the same.
            }
            return new TopLevel(true, hasQuery, queryToken, query, operationName, persisted);
        }

        /// <summary>
        /// A captured body may hold raw control characters inside its strings, which JSON does not allow and the reader
        /// rejects; escaped, they read as the value the application sent, and the label's own escaping deals with them.
        /// </summary>
        private static string EscapeControlsInStrings(string body)
        {
            StringBuilder? escaped = null;
            var inString = false;
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        escaped?.Append(c);
                        if (i + 1 < body.Length)
                            escaped?.Append(body[++i]);
                        continue;
                    }
                    if (c == '"')
                        inString = false;
                    else if (c < ' ')
                    {
                        escaped ??= new StringBuilder(body.Length + 16).Append(body, 0, i);
                        escaped.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        continue;
                    }
                }
                else if (c == '"')
                    inString = true;
                escaped?.Append(c);
            }
            return escaped?.ToString() ?? body;
        }
    }
}

/// <summary>One operation of a GraphQL document: its type, its name when it has one, its root selections (null when the selection set could not be read).</summary>
internal sealed record GraphQlDocumentOperation(string Type, string? Name, IReadOnlyList<string>? RootFields);

/// <summary>What a GraphQL document holds, as far as reading its operation needs.</summary>
internal sealed class GraphQlDocument
{
    public List<GraphQlDocumentOperation> Operations { get; } = [];
    public List<string> Fragments { get; } = [];

    /// <summary>Why the document is not an executable GraphQL document, or null when it is one.</summary>
    public string? Error { get; set; }

    public static GraphQlDocument Parse(string text)
    {
        var document = new GraphQlDocument();
        new Scanner(text).Document(document);
        return document;
    }

    /// <summary>
    /// A scanner over GraphQL source text (already out of its JSON string): the lexical rules of the specification that
    /// matter for reading a document's definitions and root selections. Whitespace, commas, the byte order mark and
    /// <c>#</c> comments are ignored; strings and block strings are skipped whole wherever a group is skipped, so a brace
    /// or a parenthesis inside an argument value moves nothing; arguments, variable definitions, directives and nested
    /// selection sets are skipped as balanced groups.
    /// </summary>
    private sealed class Scanner(string s)
    {
        private int _i;

        private void SkipIgnored()
        {
            while (_i < s.Length)
            {
                var c = s[_i];
                if (c is ' ' or '\t' or '\n' or '\r' or ',' or '﻿')
                {
                    _i++;
                    continue;
                }
                if (c == '#')
                {
                    while (_i < s.Length && s[_i] is not ('\n' or '\r'))
                        _i++;
                    continue;
                }
                break;
            }
        }

        private bool At(char c)
        {
            SkipIgnored();
            return _i < s.Length && s[_i] == c;
        }

        private bool AtSpread()
        {
            SkipIgnored();
            return _i + 2 < s.Length && s[_i] == '.' && s[_i + 1] == '.' && s[_i + 2] == '.';
        }

        private bool AtNameStart()
        {
            SkipIgnored();
            return _i < s.Length && (char.IsAsciiLetter(s[_i]) || s[_i] == '_');
        }

        private string? Name()
        {
            if (!AtNameStart())
                return null;
            var start = _i++;
            while (_i < s.Length && (char.IsAsciiLetterOrDigit(s[_i]) || s[_i] == '_'))
                _i++;
            return s[start.._i];
        }

        /// <summary>At an opening bracket: past its partner, over strings, block strings and comments. False when it does not close.</summary>
        private bool SkipGroup()
        {
            SkipIgnored();
            if (_i >= s.Length || s[_i] is not ('(' or '[' or '{'))
                return false;
            var stack = new Stack<char>();
            while (_i < s.Length)
            {
                var c = s[_i];
                switch (c)
                {
                    case '(': stack.Push(')'); _i++; break;
                    case '[': stack.Push(']'); _i++; break;
                    case '{': stack.Push('}'); _i++; break;
                    case ')' or ']' or '}':
                        if (stack.Count == 0 || stack.Pop() != c)
                            return false;
                        _i++;
                        if (stack.Count == 0)
                            return true;
                        break;
                    case '"':
                        if (!SkipString())
                            return false;
                        break;
                    case '#':
                        while (_i < s.Length && s[_i] is not ('\n' or '\r'))
                            _i++;
                        break;
                    default:
                        _i++;
                        break;
                }
            }
            return false;
        }

        /// <summary>At a quote: past the string or block string. False when it does not end.</summary>
        private bool SkipString()
        {
            if (_i + 2 < s.Length && s[_i + 1] == '"' && s[_i + 2] == '"')
            {
                _i += 3;
                while (_i < s.Length)
                {
                    if (s[_i] == '\\' && _i + 3 < s.Length && s[_i + 1] == '"' && s[_i + 2] == '"' && s[_i + 3] == '"')
                    {
                        _i += 4;
                        continue;
                    }
                    if (s[_i] == '"' && _i + 2 < s.Length && s[_i + 1] == '"' && s[_i + 2] == '"')
                    {
                        _i += 3;
                        return true;
                    }
                    _i++;
                }
                return false;
            }
            _i++;
            while (_i < s.Length)
            {
                var c = s[_i];
                if (c == '\\')
                {
                    _i += 2;
                    continue;
                }
                if (c == '"')
                {
                    _i++;
                    return true;
                }
                if (c is '\n' or '\r')
                    return false;
                _i++;
            }
            return false;
        }

        private bool Directives()
        {
            while (At('@'))
            {
                _i++;
                if (Name() is null)
                    return false;
                if (At('(') && !SkipGroup())
                    return false;
            }
            return true;
        }

        public void Document(GraphQlDocument d)
        {
            while (true)
            {
                SkipIgnored();
                if (_i >= s.Length)
                    break;
                if (At('{'))
                {
                    var shorthand = RootSelections();
                    d.Operations.Add(new GraphQlDocumentOperation("query", null, shorthand));
                    if (shorthand is null)
                    {
                        d.Error = "unreadable selection set";
                        return;
                    }
                    continue;
                }
                var word = Name();
                if (word is null)
                {
                    d.Error = "unexpected character";
                    return;
                }
                switch (word)
                {
                    case "query" or "mutation" or "subscription":
                    {
                        var name = AtNameStart() ? Name() : null;
                        if (At('(') && !SkipGroup())
                        {
                            d.Error = "unclosed variable definitions";
                            return;
                        }
                        if (!Directives())
                        {
                            d.Error = "unreadable directive";
                            return;
                        }
                        if (!At('{'))
                        {
                            d.Error = "an operation without a selection set";
                            return;
                        }
                        var fields = RootSelections();
                        d.Operations.Add(new GraphQlDocumentOperation(word, name, fields));
                        if (fields is null)
                        {
                            d.Error = "unreadable selection set";
                            return;
                        }
                        break;
                    }
                    case "fragment":
                    {
                        var fragment = Name();
                        if (fragment is null || Name() != "on" || Name() is null || !Directives() || !At('{') || !SkipGroup())
                        {
                            d.Error = "unreadable fragment definition";
                            return;
                        }
                        d.Fragments.Add(fragment);
                        break;
                    }
                    default:
                        d.Error = "not an executable definition";
                        return;
                }
            }
            if (d.Operations.Count == 0 && d.Error is null)
                d.Error = d.Fragments.Count > 0 ? "fragments only" : "empty document";
        }

        /// <summary>At a selection set's brace: its selections, nested ones skipped; null when it cannot be read.</summary>
        private List<string>? RootSelections()
        {
            SkipIgnored();
            _i++; // the brace
            var fields = new List<string>();
            while (true)
            {
                SkipIgnored();
                if (_i >= s.Length)
                    return null;
                if (s[_i] == '}')
                {
                    _i++;
                    return fields;
                }
                if (AtSpread())
                {
                    _i += 3;
                    var name = Name();
                    if (name == "on")
                    {
                        var type = Name();
                        if (type is null || !Directives() || !At('{') || !SkipGroup())
                            return null;
                        fields.Add("... on " + type);
                    }
                    else if (name is not null)
                    {
                        if (!Directives())
                            return null;
                        fields.Add("..." + name);
                    }
                    else
                    {
                        if (!Directives() || !At('{') || !SkipGroup())
                            return null;
                        fields.Add("...");
                    }
                    continue;
                }
                var first = Name();
                if (first is null)
                    return null;
                var field = first;
                if (At(':'))
                {
                    _i++;
                    field = Name();
                    if (field is null)
                        return null;
                }
                if (At('(') && !SkipGroup())
                    return null;
                if (!Directives())
                    return null;
                if (At('{') && !SkipGroup())
                    return null;
                fields.Add(field);
            }
        }
    }
}
