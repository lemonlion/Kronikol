using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.PlantUml;

namespace Kronikol.History;

/// <summary>
/// What a call is, comparable across calls and across runs: one key behind the history fingerprint, the warm-up marks,
/// the component diagram's endpoints and the query engine's <c>route</c> (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.1).
/// It reads what the data file holds, the URI as the data file writes it (<c>Uri.ToString()</c>) and the request's
/// content, so a report read later keys a call as its run did.
/// </summary>
internal static partial class CallOperation
{
    /// <summary>
    /// The key: the path without its scheme, its authority and its fragment (an entity a tracker writes where a host
    /// would go kept as the first segment), the query's keys when <paramref name="queryKeys"/> asks for them, templated
    /// by <see cref="InteractionShape.Template(string, HistoryShapeRules?)"/>; then, for a statement-shaped call, the
    /// statement's templated head, and for a GraphQL request its operation (<c>/graphql query GetUser</c>), read from a
    /// POST's body or a GET's query string only, the two places the engine reads it from as well.
    /// </summary>
    public static string Of(string? method, string uri, string? dependencyCategory, string? requestContent, HistoryShapeRules? rules, bool queryKeys) =>
        Key(Untemplated(method, uri, dependencyCategory, requestContent, queryKeys), rules);

    /// <summary>
    /// What <see cref="Of"/> keys a call by before any rule is applied: the path, and the statement's first line or the
    /// GraphQL operation. A fragment carries these (<see cref="HistoryRawCall"/>), so a call is keyed later under rules
    /// the run did not know.
    /// </summary>
    internal static (string Path, string? Head, string? Operation) Untemplated(string? method, string uri, string? dependencyCategory, string? requestContent, bool queryKeys)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = PathOf(uri, dependencyCategory, queryKeys);
        if (DependencyCategories.IsStatementShaped(dependencyCategory) && !string.IsNullOrWhiteSpace(requestContent))
            return (path, InteractionShape.StatementLine(DocumentQuery(requestContent) ?? WithoutSetPrologue(requestContent)), null);

        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return (path, null, GraphQlOperationReader.FromQueryString(uri)?.Label);
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return (path, null, null);
        if (GraphQlOperationReader.FromBody(requestContent) is { } operation)
            return (path, null, operation.Label);
        return (path, null, GraphQlOperationReader.IsBatch(requestContent) ? "batch" : null);
    }

    /// <summary>
    /// <see cref="Of"/> in its two parts: the target, the templated path and, for a statement-shaped call, its templated
    /// head; and a GraphQL request's operation (<c>query GetUser</c>, or <c>batch</c>), which <see cref="Of"/> writes after
    /// the target and a space. For a reader that writes them apart, as the service card does (#191): a path the data file
    /// unescapes can hold a space itself, so the key is never cut to get them back.
    /// </summary>
    internal static (string Target, string? Operation) Parts(string? method, string uri, string? dependencyCategory, string? requestContent, HistoryShapeRules? rules, bool queryKeys) =>
        Parts(Untemplated(method, uri, dependencyCategory, requestContent, queryKeys), rules);

    /// <summary>The parts of an <see cref="Untemplated"/> call under <paramref name="rules"/>.</summary>
    internal static (string Target, string? Operation) Parts((string Path, string? Head, string? Operation) call, HistoryShapeRules? rules)
    {
        var path = InteractionShape.Template(call.Path, rules);
        return (call.Head is null ? path : path + " " + InteractionShape.StatementHead(call.Head, rules), call.Operation);
    }

    /// <summary>The key of an <see cref="Untemplated"/> call under <paramref name="rules"/>.</summary>
    internal static string Key((string Path, string? Head, string? Operation) call, HistoryShapeRules? rules)
    {
        var (target, operation) = Parts(call, rules);
        return operation is null ? target : target + " " + operation;
    }

    /// <summary>
    /// The path the key templates: an absolute URI loses its scheme and its authority, any URI its fragment, and its
    /// query too unless <paramref name="queryKeys"/>. The authority is kept, as the first segment and in lower case,
    /// where a tracker writes its entity there rather than a host: a Service Bus queue or topic, an S3 bucket, an
    /// EventBridge bus, a Redis database (<c>db3</c>). A positive list, because an entity and a host without a port look
    /// alike (<c>servicebus://orders/</c>, <c>mysql://mysql/app</c>): a scheme left off loses a distinction, never
    /// splits one call across runs.
    /// </summary>
    internal static string PathOf(string uri, string? dependencyCategory, bool queryKeys)
    {
        var rest = uri;
        string? entity = null;
        var match = SchemeAndAuthority().Match(uri);
        if (match.Success)
        {
            rest = match.Groups["rest"].Value;
            var authority = match.Groups["authority"].Value;
            if (KeepsAuthority(match.Groups["scheme"].Value, authority, dependencyCategory))
                entity = authority.ToLowerInvariant();
        }

        var cut = rest.IndexOfAny(queryKeys ? ['#'] : ['?', '#']);
        if (cut >= 0)
            rest = rest[..cut];
        if (entity is not null)
            rest = "/" + entity + (rest.StartsWith('/') || rest.Length == 0 ? rest : "/" + rest);
        if (rest.Length == 0 || rest[0] == '?')
            rest = "/" + rest;
        return rest;
    }

    private static bool KeepsAuthority(string scheme, string authority, string? dependencyCategory)
    {
        if (authority.Length == 0 || authority.Contains('@'))
            return false;
        return scheme.ToLowerInvariant() switch
        {
            "servicebus" => string.Equals(dependencyCategory, DependencyCategories.ServiceBus, StringComparison.OrdinalIgnoreCase),
            "s3" or "eventbridge" => true,
            "redis" => RedisDatabase().IsMatch(authority),
            _ => false
        };
    }

    // The statement a call's head is read from: past a prologue of SET statements, which EF Core opens every SQL Server
    // batch with, and, for a document that carries its statement as a string (Cosmos DB's and BigQuery's query bodies),
    // that string rather than the document, whose strings are data.
    private static string WithoutSetPrologue(string content)
    {
        var match = SetPrologue().Match(content);
        if (!match.Success)
            return content;
        var rest = content[match.Length..];
        // A statement of SET lines alone is what it is.
        return string.IsNullOrWhiteSpace(rest) ? content : rest;
    }

    private static string? DocumentQuery(string content)
    {
        var span = content.AsSpan().TrimStart();
        if (span.Length == 0 || span[0] != '{' || !content.Contains("\"query\"", StringComparison.Ordinal))
            return null;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(content), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read())
                    return null;
                if (name == "query")
                    return reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } query ? query : null;
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // A body cut at capture: no query read, the document rule applies.
        }
        catch (InvalidOperationException)
        {
        }
        return null;
    }

    [GeneratedRegex(@"^(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*)://(?<authority>[^/?#]*)(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex SchemeAndAuthority();

    [GeneratedRegex(@"^db\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex RedisDatabase();

    [GeneratedRegex(@"^(?:\s*SET\s[^;]*;)+\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SetPrologue();
}
