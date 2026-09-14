using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.History;

/// <summary>One logical call a scenario made, reduced to what is comparable across runs.</summary>
/// <param name="Caller">Who made it.</param>
/// <param name="Service">What was called.</param>
/// <param name="Method">The verb, as captured.</param>
/// <param name="Uri">The templated path and query — or, for a statement-shaped call, the templated statement head.</param>
/// <param name="Status">The response status code or text, or null when no response was captured.</param>
public sealed record ShapeCall(string Caller, string Service, string Method, string Uri, string? Status)
{
    /// <summary>The one-line form the fingerprints hash.</summary>
    public override string ToString() => $"{Caller}>{Service} {Method} {Uri} {Status ?? "-"}";
}

/// <summary>
/// The interaction fingerprint: what a scenario did, hashed so two runs that did the same thing hash
/// the same (plans/CROSS_RUN_HISTORY_PLAN.md §2.4). Two hashes per scenario: a set fingerprint over
/// the calls regardless of order — the primary signal, because parallel steps reorder calls run to run
/// without anything having changed — and an ordered one for the reader who asked for reorders.
///
/// <para>The templater is what makes the fingerprint stable: ids, timestamps and bare numbers in a path
/// change every run and mean nothing, so they are replaced by placeholders before hashing. There is no
/// base64 rule, deliberately — measured, it templated route words like <c>customers</c> (§5.6).</para>
/// </summary>
public static partial class InteractionShape
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>
    /// The version of the templating rules below. It rides on every run line, because a fingerprint is
    /// comparable only with one the same rule made: across a change of rule the analyzer reads no
    /// behaviour verdict, and says so, rather than calling every scenario changed once. Move it when a
    /// rule changes. 1: 3.9.0 - ids, timestamps and numbers, the statement head cut before it was
    /// templated. 2: 3.14.0 - the head templated before it is cut, and what a statement carried as data
    /// (the values of a document, the literals of a query) dropped. 3: 3.15.0 - the set fingerprint is
    /// the set of distinct calls, so a retry cannot move it; how many times is the call count, which the
    /// analyzer judges on the scenario's own record.
    /// </summary>
    public const int Version = 3;

    /// <summary>How much of a statement's first line is templated, and how much of the templated head is kept.</summary>
    private const int HeadRaw = 2000;
    private const int HeadLength = 120;

    // Ids: GUIDs with or without hyphens, hex runs of sixteen or more, ULIDs. Bounded by non-alphanumerics
    // so an id embedded in a segment (prefix_{id}_suffix) is found without eating the prefix.
    [GeneratedRegex(@"(?<![0-9A-Za-z])(?:[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}|[0-9A-Fa-f]{16,}|[0-9A-HJKMNP-TV-Z]{26})(?![0-9A-Za-z])", Options)]
    private static partial Regex IdPattern();

    // Timestamps: an ISO-8601 date, optionally with a time, fraction and zone.
    [GeneratedRegex(@"(?<![0-9A-Za-z])\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?(?![0-9A-Za-z])", Options)]
    private static partial Regex TimestampPattern();

    // Bare numbers: digit runs not touching a letter, so v2 and abc123 stay and 4711 and cust-4711 do not.
    [GeneratedRegex(@"(?<![0-9A-Za-z])\d+(?![0-9A-Za-z])", Options)]
    private static partial Regex NumberPattern();

    // A double-quoted string, escapes and all: a key or a value in a document, a name or a literal in a query.
    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""", Options)]
    private static partial Regex DoubleQuotedPattern();

    // A single-quoted literal, with the doubled-quote escape SQL uses.
    [GeneratedRegex(@"'(?:[^']|'')*'", Options)]
    private static partial Regex SingleQuotedPattern();

    // What sits on the left of a literal in a query: a comparison, LIKE, or the opening of an IN list.
    [GeneratedRegex(@"(?:=|<>|!=|<|>|\bLIKE|\bIN\s*\()\s*\z", Options | RegexOptions.IgnoreCase)]
    private static partial Regex ComparisonBeforePattern();

    /// <summary>The templated form of a path (with an optional query) or a statement head.</summary>
    public static string Template(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var query = text.IndexOf('?');
        var path = query < 0 ? text : text[..query];
        var templated = NumberPattern().Replace(TimestampPattern().Replace(IdPattern().Replace(path, "{id}"), "{ts}"), "{n}");

        if (query < 0)
            return templated;

        // Keys kept and sorted, values dropped: which parameters were sent is behaviour, what they held
        // is data.
        var keys = text[(query + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        return keys.Length == 0 ? templated : templated + "?" + string.Join("&", keys);
    }

    /// <summary>
    /// The calls in a log stream, in request order, each paired with its response by
    /// <see cref="RequestResponseLog.RequestResponseId"/>. Markers and ignored records are not calls.
    /// </summary>
    /// <summary>
    /// A statement head templated: ids, timestamps and numbers as in a path, and what the statement carried
    /// as data reduced to a marker - the values of a document (<c>{v}</c>), the literals of a query
    /// (<c>'{s}'</c>, or <c>"{v}"</c> on the right of a comparison, which is where Cosmos DB puts a string
    /// literal and a quoted identifier never sits alone). Which fields and parameters were sent is what
    /// the fingerprint compares, not what they held: the rule the query string already follows. A
    /// <c>?</c> here is a parameter, not the start of a query string.
    /// </summary>
    public static string TemplateStatement(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        string valued;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            // A document: every string that is not a key is a value.
            valued = DoubleQuotedPattern().Replace(text, match => IsKey(text, match) ? match.Value : "\"{v}\"");
        }
        else
        {
            // A query: single-quoted literals, and a double-quoted one compared against - not the quoted
            // identifiers PostgreSQL and EF Core write everywhere else.
            var statement = SingleQuotedPattern().Replace(text, "'{s}'");
            valued = DoubleQuotedPattern().Replace(statement, match => IsComparedLiteral(statement, match) ? "\"{v}\"" : match.Value);
        }

        return NumberPattern().Replace(TimestampPattern().Replace(IdPattern().Replace(valued, "{id}"), "{ts}"), "{n}");
    }

    private static bool IsKey(string text, Match match)
    {
        var i = match.Index + match.Length;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i < text.Length && text[i] == ':';
    }

    private static bool IsComparedLiteral(string text, Match match)
    {
        var after = match.Index + match.Length;
        if (after < text.Length && text[after] == '.')
            return false;   // "o"."Id": an identifier chain
        return ComparisonBeforePattern().IsMatch(text[..match.Index]);
    }

    public static IReadOnlyList<ShapeCall> Calls(IEnumerable<RequestResponseLog?> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        var responses = new Dictionary<Guid, RequestResponseLog>();
        var requests = new List<RequestResponseLog>();
        foreach (var log in logs)
        {
            if (log is null || log.TrackingIgnore || log.IsDiagramMarker)
                continue;
            if (log.Type == RequestResponseType.Response)
                responses.TryAdd(log.RequestResponseId, log);
            else
                requests.Add(log);
        }

        var calls = new List<ShapeCall>(requests.Count);
        foreach (var request in requests)
        {
            responses.TryGetValue(request.RequestResponseId, out var response);
            var (code, text) = InteractionStatus.Split(response?.StatusCode);
            var status = code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? text;
            calls.Add(new ShapeCall(request.CallerName, request.ServiceName, MethodOf(request), Target(request), status));
        }
        return calls;
    }

    private static string MethodOf(RequestResponseLog log) => log.Method.Value switch
    {
        HttpMethod method => method.Method,
        var other => other?.ToString() ?? ""
    };

    /// <summary>
    /// What identifies the call: the templated path and query, plus — for a statement-shaped dependency,
    /// whose URI is the same connection for every statement — the templated first line of the statement.
    /// </summary>
    private static string Target(RequestResponseLog log)
    {
        var path = log.Uri.IsAbsoluteUri ? log.Uri.PathAndQuery : log.Uri.OriginalString;
        var templated = Template(path);
        if (!DependencyCategories.IsStatementShaped(log.DependencyCategory) || string.IsNullOrWhiteSpace(log.Content))
            return templated;

        // Templated before it is cut: cut first, an id straddling the limit kept its first characters, and
        // a scenario writing a fresh id read as behaviour-changed on every run.
        var head = FailureText.Truncate(TemplateStatement(FailureText.Truncate(FailureText.FirstLine(log.Content), HeadRaw)), HeadLength);
        return templated + " " + head;
    }

    /// <summary>The two fingerprints and the call count for a scenario's calls.</summary>
    public static (string ShapeSet, string ShapeOrdered, int Calls) Fingerprint(IReadOnlyList<ShapeCall> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var lines = calls.Select(c => c.ToString()).ToArray();
        var ordered = string.Join("\n", lines);
        // Distinct: which calls were made. A retry against a throttled emulator, or a consumer's work
        // landing in whichever scenario is running, repeats a call without changing what the scenario does.
        var set = string.Join("\n", lines.Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal));
        return (Hash8(set), Hash8(ordered), calls.Count);
    }

    /// <summary>The distinct <c>caller&gt;service</c> pairs in a log stream, sorted.</summary>
    public static IReadOnlyList<string> Dependencies(IEnumerable<RequestResponseLog?> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        var pairs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var log in logs)
        {
            if (log is null || log.TrackingIgnore || log.IsDiagramMarker)
                continue;
            if (!string.IsNullOrEmpty(log.CallerName) && !string.IsNullOrEmpty(log.ServiceName))
                pairs.Add(log.CallerName + ">" + log.ServiceName);
        }
        return pairs.ToArray();
    }

    /// <summary>The first eight hex of SHA-256 over <paramref name="text"/> — the ledger's short hash.</summary>
    public static string Hash8(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
