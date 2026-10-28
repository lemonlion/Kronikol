extern alias K;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Detector = K::Kronikol.PlantUml.GraphQlOperationDetector;

namespace RouteProbe;

/// <summary>One operation of a GraphQL document: its type, its name when it has one, and its root selections as the key
/// writes them (a field by its name, alias dropped; <c>...Name</c> for a fragment spread; <c>... on Type</c> for an inline
/// fragment). RootFields is null when the selection set could not be read.</summary>
sealed record GqlOperation(string Type, string? Name, IReadOnlyList<string>? RootFields);

/// <summary>What a GraphQL document holds, as far as an operation key needs it.</summary>
sealed class GqlDocument
{
    public List<GqlOperation> Operations { get; } = [];
    public List<string> Fragments { get; } = [];

    /// <summary>Why the document is not an executable GraphQL document, or null when it is one.</summary>
    public string? Error { get; set; }

    public static GqlDocument Parse(string text)
    {
        var document = new GqlDocument();
        new GqlScanner(text).Document(document);
        return document;
    }
}

/// <summary>
/// A scanner over GraphQL source text (already out of its JSON string, so JSON escapes are the JSON reader's job): the
/// lexical rules of the spec that matter for reading a document's definitions and root selections. Whitespace, commas,
/// the BOM and # comments are ignored; strings and block strings are skipped whole wherever a group is skipped, so a
/// brace or a parenthesis inside an argument value moves nothing; arguments, variable definitions, directives and nested
/// selection sets are skipped as balanced groups.
/// </summary>
sealed class GqlScanner(string s)
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

    private string Here => _i < s.Length ? s[_i].ToString() : "end";

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

    public void Document(GqlDocument d)
    {
        while (true)
        {
            SkipIgnored();
            if (_i >= s.Length)
                break;
            if (At('{'))
            {
                var shorthand = RootSelections();
                if (shorthand is null)
                {
                    d.Error = "unreadable selection set";
                    d.Operations.Add(new GqlOperation("query", null, null));
                    return;
                }
                d.Operations.Add(new GqlOperation("query", null, shorthand));
                continue;
            }
            var word = Name();
            if (word is null)
            {
                d.Error = $"unexpected '{Here}'";
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
                        d.Error = $"{word}{(name is null ? "" : " " + name)} without a selection set (at '{Here}')";
                        return;
                    }
                    var fields = RootSelections();
                    d.Operations.Add(new GqlOperation(word, name, fields));
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
                    d.Error = $"not an executable definition ('{Cut.To(word, 20)}')";
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

/// <summary>A request body's top level, read as JSON: what the operation needs and nothing else (variables are skipped).</summary>
sealed record TopLevel(bool IsObject, bool IsArray, bool Complete, bool HasQuery, JsonTokenType QueryToken, string? Query,
    bool HasOperationName, string? OperationName, bool HasPersistedQuery)
{
    public static readonly TopLevel None = new(false, false, false, false, JsonTokenType.None, null, false, null, false);

    public static TopLevel Read(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 512 });
        bool isObject = false, isArray = false, complete = false, hasQuery = false, hasOperationName = false, persisted = false;
        var queryToken = JsonTokenType.None;
        string? query = null, operationName = null;
        try
        {
            if (!reader.Read())
                return None;
            if (reader.TokenType == JsonTokenType.StartArray)
                return None with { IsArray = true };
            if (reader.TokenType != JsonTokenType.StartObject)
                return None;
            isObject = true;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    complete = true;
                    break;
                }
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
                        hasOperationName = true;
                        if (reader.TokenType == JsonTokenType.String)
                            operationName = reader.GetString();
                        else
                            reader.Skip();
                        break;
                    case "extensions" when reader.TokenType == JsonTokenType.StartObject:
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            var inner = reader.GetString();
                            reader.Read();
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
            // A body cut at capture, or not JSON past its first brace: what was read so far stands, marked incomplete.
        }
        return new TopLevel(isObject, isArray, complete, hasQuery, queryToken, query, hasOperationName, operationName, persisted);
    }
}

/// <summary>How a request's GraphQL operation was found, for the tallies.</summary>
sealed record GqlFinding(string? Operation, string How);

static class Gql
{
    /// <summary>
    /// The plan's rule (4.2), read literally: the detector's label when it names an operation; for an anonymous one (the
    /// label is the type alone) the type and the first operation's root selections; a persisted query by its name; else
    /// none. Steps 1 to 3 over a body; step 4 (a GET) goes through <see cref="FromQueryString"/>.
    /// </summary>
    public static GqlFinding Plan(string body)
    {
        var label = Detector.TryExtractLabel(body);
        if (label is not null && label.Contains(' '))
            return new(label, "named (detector label)");
        if (label is not null)
        {
            var top = TopLevel.Read(body);
            var document = top.Query is { } q ? GqlDocument.Parse(q) : null;
            var first = document is { Operations.Count: > 0 } ? document.Operations[0] : null;
            return first?.RootFields is { } fields
                ? new($"{label} {{ {string.Join(' ', fields)} }}", "anonymous: root fields")
                : new(label, "anonymous: root fields unreadable, type alone");
        }
        var t = TopLevel.Read(body);
        if (t.IsObject && (!t.HasQuery || t.QueryToken == JsonTokenType.Null) && t.HasPersistedQuery && t.OperationName is { Length: > 0 } persisted)
            return new(persisted, "persisted query by name");
        return new(null, t.IsArray ? "none: a batch" : "none");
    }

    /// <summary>
    /// The stricter reading compared with the plan's: the top level parsed as JSON (so a nested <c>query</c> key before the
    /// real one moves nothing), the query text parsed as a GraphQL document (so text that only starts like one is not
    /// one), and the operation selected the way a server selects it: by operationName, else the only one.
    /// </summary>
    public static GqlFinding Strict(string body)
    {
        var t = TopLevel.Read(body);
        if (!t.IsObject)
            return new(null, t.IsArray ? "none: a batch" : "none: not a JSON object");
        if (t.Query is { } q)
        {
            var document = GqlDocument.Parse(q);
            if (document.Operations.Count == 0)
                return new(null, "none: query is not GraphQL (" + (document.Error ?? "no operation") + ")");
            if (document.Error is not null && document.Operations.All(o => o.RootFields is null))
                return new(null, "none: query is not GraphQL (" + document.Error + ")");
            if (t.OperationName is { Length: > 0 } name)
            {
                var selected = document.Operations.FirstOrDefault(o => o.Name == name);
                return selected is not null
                    ? new($"{selected.Type} {name}", "named, selected by operationName")
                    : new($"{document.Operations[0].Type} {name}", "operationName names no operation in the document; first operation's type");
            }
            var operation = document.Operations[0];
            var how = document.Operations.Count > 1 ? "several operations and no operationName (a server rejects it); first taken" : null;
            if (operation.Name is not null)
                return new($"{operation.Type} {operation.Name}", how ?? "named");
            return operation.RootFields is { } fields
                ? new($"{operation.Type} {{ {string.Join(' ', fields)} }}", how ?? "anonymous: root fields")
                : new(operation.Type, "anonymous: root fields unreadable, type alone");
        }
        if ((!t.HasQuery || t.QueryToken == JsonTokenType.Null) && t.HasPersistedQuery && t.OperationName is { Length: > 0 } persisted)
            return new(persisted, "persisted query by name");
        return new(null, t.HasQuery ? $"none: query is a JSON {t.QueryToken}" : "none: no query key");
    }

    /// <summary>A GET's GraphQL parameters (query, operationName, extensions), decoded, as the body they stand for; null when it has neither query nor operationName.</summary>
    public static string? FromQueryString(string uri)
    {
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
        return Encoding.UTF8.GetString(stream.ToArray());

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
}

static class Cut
{
    /// <summary>One line, at most n characters: what any printed excerpt goes through.</summary>
    public static string To(string? text, int n)
    {
        if (text is null)
            return "(null)";
        var flat = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return flat.Length <= n ? flat : flat[..(n - 1)] + "…";
    }
}
