extern alias K;
using System.Text.RegularExpressions;
using K::Kronikol.Constants;
using K::Kronikol.History;
using K::Kronikol.Reports;

namespace RouteProbe;

enum GqlRule { Plan, Strict }

/// <summary>What one key computation did, for the tallies.</summary>
struct KeyTrace
{
    public bool Head;
    public bool GqlBodyLooked;   // a POST body that is a JSON object was read for an operation
    public bool GqlGetLooked;    // a GET whose query string carries query or operationName
    public GqlFinding? Gql;
}

/// <summary>
/// CallOperation.Of as 4.1 states it, with queryKeys off (the route dimension of 4.4) and no consumer rules (none of these
/// runs sets HistoryShapeTemplates): the data file's URI string cut by PathOf, templated; a statement-shaped call with
/// content adds its statement head; a GraphQL request adds its operation.
/// </summary>
static partial class Key
{
    // WarmUpCalls.SchemeAndAuthority, copied.
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/?#]*(?<rest>.*)$")]
    private static partial Regex SchemeAndAuthority();

    /// <summary>WarmUpCalls.PathOf, reproduced: an absolute URI loses its scheme and authority, any URI its query and fragment.</summary>
    public static string PathOf(string uri)
    {
        var match = SchemeAndAuthority().Match(uri);
        var rest = match.Success ? match.Groups["rest"].Value : uri;
        var cut = rest.IndexOfAny(['?', '#']);
        var path = cut >= 0 ? rest[..cut] : rest;
        return path.Length == 0 ? "/" : path;
    }

    /// <summary>The internal original, for the cross-check.</summary>
    public static string PathOfInternal(string uri) => WarmUpCalls.PathOf(uri);

    /// <summary>InteractionShape.StatementHead (internal) reproduced from public members, rules null: the first line cut
    /// to 2,000 characters, templated as a statement, cut to 120. Exact, not an approximation: the same four calls.</summary>
    public static string Head(string content) =>
        FailureText.Truncate(InteractionShape.TemplateStatement(FailureText.Truncate(FailureText.FirstLine(content), 2000)), 120);

    public static string HeadInternal(string content) => InteractionShape.StatementHead(content, null);

    public static bool IsPost(string? method) => string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);

    public static bool IsGet(string? method) => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);

    public static char FirstNonWhite(string body)
    {
        foreach (var c in body)
            if (!char.IsWhiteSpace(c) && c != '﻿')
                return c;
        return '\0';
    }

    /// <summary>Whether the key needs this call's request body (4.4): a statement-shaped call, or a POST (only reading it tells
    /// whether it is a JSON object). A call with no content needs nothing.</summary>
    public static bool NeedsBody(string? method, string? category, bool hasContent) =>
        hasContent && (DependencyCategories.IsStatementShaped(category) || IsPost(method));

    public static string TemplatedPath(string uri) => InteractionShape.Template(PathOf(uri));

    public static string Route(string? method, string uri, string? category, string? body, GqlRule rule, out KeyTrace trace)
    {
        trace = default;
        var route = InteractionShape.Template(PathOf(uri));
        if (DependencyCategories.IsStatementShaped(category) && !string.IsNullOrWhiteSpace(body))
        {
            route += " " + Head(body);
            trace.Head = true;
        }

        GqlFinding? finding = null;
        if (IsPost(method) && body is not null && FirstNonWhite(body) == '{')
        {
            trace.GqlBodyLooked = true;
            finding = rule == GqlRule.Plan ? Gql.Plan(body) : Gql.Strict(body);
        }
        else if (IsGet(method) && Gql.FromQueryString(uri) is { } synthesized)
        {
            trace.GqlGetLooked = true;
            finding = rule == GqlRule.Plan ? Gql.Plan(synthesized) : Gql.Strict(synthesized);
        }
        trace.Gql = finding;
        if (finding?.Operation is { } operation)
            route += " " + operation;
        return route;
    }

    /// <summary>The route with only the parts before the GraphQL step: for the decomposition of the bucket counts.</summary>
    public static string PathAndHead(string uri, string? category, string? body)
    {
        var route = InteractionShape.Template(PathOf(uri));
        return DependencyCategories.IsStatementShaped(category) && !string.IsNullOrWhiteSpace(body) ? route + " " + Head(body) : route;
    }
}
