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

        var head = FailureText.Truncate(FailureText.FirstLine(log.Content), 120);
        return templated + " " + Template(head);
    }

    /// <summary>The two fingerprints and the call count for a scenario's calls.</summary>
    public static (string ShapeSet, string ShapeOrdered, int Calls) Fingerprint(IReadOnlyList<ShapeCall> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var lines = calls.Select(c => c.ToString()).ToArray();
        var ordered = string.Join("\n", lines);
        var set = string.Join("\n", lines.OrderBy(l => l, StringComparer.Ordinal));
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
