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
    /// analyzer judges on the scenario's own record. 4: 3.22.2 - a GUID is a GUID whatever separates its
    /// groups (<c>job_09c68d49_adcf_47be_...</c>), a hex run of eight to fifteen holding a digit and a letter is
    /// an id, and bytes captured as text are <c>{bin}</c> (#75). 5: 4.20.0 - no rule changed, but what MongoDB records
    /// as a request did: the command without the driver's fields in place of the bare filter, so every MongoDB call's
    /// statement head moved (#168 R2). 6: 4.20.5 - a document's booleans (<c>{b}</c>), the sign of its numbers and how
    /// many scalars a list of them holds are data too, so a test that generates its data does not write a new shape
    /// whenever a flag flips (#168 audit). 7: #190 R0 - no rule changed, but what the HTTP handler records did: a call
    /// nothing maps is named by its remote host and port, where 6 recorded <c>localhost:{port}</c> for every host.
    /// 8: #192 R1 - one operation key behind history, the warm-up marks and the component diagram
    /// (<see cref="CallOperation"/>): the path as the data file writes it, unescaped, with an entity a tracker writes in
    /// the authority kept (a Service Bus queue, an S3 bucket); <c>{bin}</c> for bytes written raw; the consumer's rules
    /// applied to that string; a GraphQL request's operation; and a statement's head past a <c>SET</c> prologue, or a
    /// document's query string rather than the document.
    /// <para>From 6 on, a released rule is kept as a function of the raw call (<see cref="KeptVersions"/>): while the
    /// window holds runs fingerprinted under one, each run also fingerprints its own calls under it, its
    /// <see cref="HistoryRun.Bridge"/>, so a move costs no quiet run (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md §4.8, built
    /// for #190). A window holding runs under 5 or earlier compares from the next run, as before.</para>
    /// </summary>
    public const int Version = 8;

    /// <summary>
    /// The released rules a run can fingerprint its own calls under (<see cref="Calls(IEnumerable{RequestResponseLog}, HistoryShapeRules, int)"/>),
    /// the current one last. A rule is kept from 6 on: what each recorded is a function of what the current one records.
    /// </summary>
    internal static IReadOnlyList<int> KeptVersions { get; } = [6, 7, Version];

    /// <summary>How much of a statement's first line is templated, and how much of the templated head is kept.</summary>
    private const int HeadRaw = 2000;
    private const int HeadLength = 120;

    // Ids: GUIDs whose groups are joined by hyphens, by underscores or by nothing, hex runs of sixteen or
    // more, ULIDs, and short hex digests. Bounded by non-alphanumerics so an id embedded in a segment
    // (prefix_{id}_suffix) is found without eating the prefix. The order is the rule: the GUID is tried
    // before the short run, or its first group would match alone. The short run needs a digit AND a
    // letter - a plain number is already {n}, and a route word spelt in hex letters (facade, deadbeef)
    // has no digit. Measured over both repositories' sources, 628 distinct paths: it took nothing but
    // groups of whole GUIDs.
    [GeneratedRegex(@"(?<![0-9A-Za-z])(?:[0-9A-Fa-f]{8}(?<sep>[-_])[0-9A-Fa-f]{4}\k<sep>[0-9A-Fa-f]{4}\k<sep>[0-9A-Fa-f]{4}\k<sep>[0-9A-Fa-f]{12}|[0-9A-Fa-f]{16,}|[0-9A-HJKMNP-TV-Z]{26}|(?=[0-9A-Fa-f]*\d)(?=[0-9A-Fa-f]*[A-Fa-f])[0-9A-Fa-f]{8,15})(?![0-9A-Za-z])", Options)]
    private static partial Regex IdPattern();

    // A binary segment: bytes captured as text, which is what a hash in a cache key becomes. From the
    // delimiter before the first U+FFFD or C0 escape to the end of the path segment - walking back,
    // because a hash's leading bytes are printable about a third of the time and vary as the rest do.
    [GeneratedRegex(@"(?<=^|[/\-_:.])[^/\-_:.]*(?:%EF%BF%BD|%[01][0-9A-Fa-f])[^/]*", Options)]
    private static partial Regex BinaryPattern();

    // The same, for a path whose bytes are written as themselves: the data file's string unescapes a control character
    // and keeps U+FFFD as the character, where the escaped path had %0C and %EF%BF%BD.
    [GeneratedRegex(@"(?<=^|[/\-_:.])[^/\-_:.]*(?:%EF%BF%BD|%[01][0-9A-Fa-f]|[\u0000-\u001F\uFFFD])[^/]*", Options)]
    private static partial Regex RawBinaryPattern();

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
    public static string Template(string text) => Template(text, null);

    /// <summary>
    /// The templated form of a path, the consumer's rules applied to it first
    /// (<see cref="ReportConfigurationOptions.HistoryShapeTemplates"/>): the built-in rules then see what
    /// those left. The query string is not offered to them; its values are dropped whatever they hold.
    /// </summary>
    public static string Template(string text, HistoryShapeRules? rules) => Template(text, rules, rawBytes: true);

    /// <summary>
    /// <see cref="Template(string, HistoryShapeRules?)"/>, with <paramref name="rawBytes"/> saying whether bytes written
    /// as themselves are <c>{bin}</c> as well as their escapes: the data file's string unescapes them, the escaped path
    /// rules 6 and 7 read did not.
    /// </summary>
    internal static string Template(string text, HistoryShapeRules? rules, bool rawBytes)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var query = text.IndexOf('?');
        var path = query < 0 ? text : text[..query];
        if (rules is not null)
            path = rules.Apply(path);
        var binary = rawBytes ? RawBinaryPattern() : BinaryPattern();
        var templated = NumberPattern().Replace(TimestampPattern().Replace(IdPattern().Replace(binary.Replace(path, "{bin}"), "{id}"), "{ts}"), "{n}");

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
    /// A statement head templated: ids, timestamps and numbers as in a path, and what the statement carried
    /// as data reduced to a marker - the values of a document (<c>{v}</c>), the literals of a query
    /// (<c>'{s}'</c>, or <c>"{v}"</c> on the right of a comparison, which is where Cosmos DB puts a string
    /// literal and a quoted identifier never sits alone). Which fields and parameters were sent is what
    /// the fingerprint compares, not what they held: the rule the query string already follows. A
    /// <c>?</c> here is a parameter, not the start of a query string.
    /// </summary>
    public static string TemplateStatement(string text) => TemplateStatement(text, null);

    /// <summary><see cref="TemplateStatement(string)"/> with the consumer's rules applied to the head first.</summary>
    public static string TemplateStatement(string text, HistoryShapeRules? rules)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (rules is not null)
            text = rules.Apply(text);

        string valued;
        var trimmed = text.TrimStart();
        var isDocument = trimmed.StartsWith('{') || trimmed.StartsWith('[');
        if (isDocument)
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

        var templated = NumberPattern().Replace(TimestampPattern().Replace(IdPattern().Replace(valued, "{id}"), "{ts}"), "{n}");
        if (!isDocument)
            return templated;

        // A document's booleans, the signs of its numbers and the length of a list of scalars are what it carried, as its
        // strings and numbers are: a test that generates its data wrote a new shape whenever a flag flipped, a number went
        // negative or a list grew. A null stays, since a null filter matches what a valued one does not, and so does a
        // key: a flag the statement sets (upsert) is there or not.
        templated = BooleanPattern().Replace(templated, "{b}");
        templated = SignPattern().Replace(templated, "");
        return RepeatedScalarPattern().Replace(templated, "${scalar}");
    }

    /// <summary>A JSON boolean: every string of a document is <c>"{v}"</c> by now, so one left bare is a literal.</summary>
    [GeneratedRegex(@"(?<![0-9A-Za-z_$])(?:true|false)(?![0-9A-Za-z_])", Options)]
    private static partial Regex BooleanPattern();

    /// <summary>A number's minus sign, once the number is <c>{n}</c>.</summary>
    [GeneratedRegex(@"-(?=\{n\})", Options)]
    private static partial Regex SignPattern();

    /// <summary>A run of one templated scalar in a list (<c>[{n}, {n}, {n}]</c>), each a whole element.</summary>
    [GeneratedRegex(@"(?<=[\[,]\s*)(?<scalar>""\{v\}""|\{b\}|null|\{id\}|\{ts\}|\{n\}(?:\.\{n\})?)(?:\s*,\s*\k<scalar>(?=\s*[,\]]))+(?=\s*[,\]])", Options)]
    private static partial Regex RepeatedScalarPattern();

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

    /// <summary>
    /// The calls in a log stream, in request order, each paired with its response by
    /// <see cref="RequestResponseLog.RequestResponseId"/>. Markers and ignored records are not calls.
    /// </summary>
    public static IReadOnlyList<ShapeCall> Calls(IEnumerable<RequestResponseLog?> logs) => Calls(logs, null);

    /// <summary><see cref="Calls(IEnumerable{RequestResponseLog})"/> with the consumer's templating rules applied before the built-in ones.</summary>
    public static IReadOnlyList<ShapeCall> Calls(IEnumerable<RequestResponseLog?> logs, HistoryShapeRules? rules) => Calls(logs, rules, Version);

    /// <summary>
    /// The calls as a released rule recorded them (<see cref="KeptVersions"/>): under 6, a callee the HTTP handler named
    /// by its remote host (<see cref="RequestResponseLog.NamedByRemoteHostFallback"/>) is <c>localhost:{port}</c>, as it
    /// was then; under 6 and 7, a call reads as its tracker recorded it before 8 (<see cref="RequestResponseLog.RecordedBefore8"/>),
    /// or is no call, and its target is the escaped path rather than the operation key.
    /// </summary>
    internal static IReadOnlyList<ShapeCall> Calls(IEnumerable<RequestResponseLog?> logs, HistoryShapeRules? rules, int version) =>
        Calls(RawCalls(logs), rules, version);

    /// <summary>
    /// The calls of <see cref="Calls(IEnumerable{RequestResponseLog}, HistoryShapeRules, int)"/> as a rule takes them in,
    /// before any rule (<see cref="HistoryRawCall"/>): what a fragment carries, so <c>history record</c> fingerprints a
    /// run under rules the run did not know.
    /// </summary>
    internal static IReadOnlyList<ShapeCall> Calls(IReadOnlyList<HistoryRawCall> calls, HistoryShapeRules? rules, int version)
    {
        ArgumentNullException.ThrowIfNull(calls);
        if (!KeptVersions.Contains(version))
            throw new ArgumentOutOfRangeException(nameof(version), version, "only a kept rule can be applied to a call");

        var shaped = new List<ShapeCall>(calls.Count);
        foreach (var call in calls)
        {
            if (version >= 8)
            {
                shaped.Add(new ShapeCall(call.Caller, call.Service, call.Method, CallOperation.Key((call.Path, call.Head, call.Operation), rules), call.Status));
                continue;
            }

            // Under 6 and 7, the call as its tracker recorded it then, or none, and the escaped path and the statement's
            // own first line rather than the operation key.
            if (call.Absent7)
                continue;
            var target = Template(call.Path7 ?? call.Path, rules, rawBytes: false);
            if ((call.Head7 ?? call.Head) is { } head)
                target += " " + StatementHead(head, rules);
            shaped.Add(new ShapeCall(call.Caller, version < 7 ? call.Service6 ?? call.Service : call.Service, call.Method7 ?? call.Method, target, call.Status));
        }
        return shaped;
    }

    /// <summary>
    /// The calls in a log stream before any rule (<see cref="HistoryRawCall"/>), in request order, each paired with its
    /// response by <see cref="RequestResponseLog.RequestResponseId"/>. Markers and ignored records are not calls.
    /// </summary>
    internal static IReadOnlyList<HistoryRawCall> RawCalls(IEnumerable<RequestResponseLog?> logs)
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

        var calls = new List<HistoryRawCall>(requests.Count);
        foreach (var request in requests)
        {
            responses.TryGetValue(request.RequestResponseId, out var response);
            var (code, text) = InteractionStatus.Split(response?.StatusCode);
            var status = code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? text;
            var method = MethodOf(request);
            var (path, head, operation) = CallOperation.Untemplated(method, request.Uri.ToString(), request.DependencyCategory, request.Content, queryKeys: true);
            var call = new HistoryRawCall(request.CallerName, request.ServiceName, method, path, operation, head, status);

            // Rules 6 and 7 read a call as its tracker recorded it before 8 (RequestResponseLog.RecordedBefore8), or no
            // call, with its escaped path and its statement's own first line; 6 named a host by its address localhost.
            var service6 = ServiceNameUnder(request, 6);
            if (request.RecordedBefore8 is { Absent: true })
            {
                call = call with { Absent7 = true };
            }
            else
            {
                var uri = request.RecordedBefore8?.Uri ?? request.Uri;
                var path7 = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
                var method7 = request.RecordedBefore8 is { } before ? MethodOf(before.Method) : method;
                var head7 = head is null ? null : StatementLine(request.Content!);
                call = call with
                {
                    Method7 = method7 == method ? null : method7,
                    Path7 = path7 == path ? null : path7,
                    Head7 = head7 == head ? null : head7
                };
            }
            calls.Add(service6 == request.ServiceName ? call : call with { Service6 = service6 });
        }
        return calls;
    }

    /// <summary>The callee's name as <paramref name="version"/> recorded it.</summary>
    private static string ServiceNameUnder(RequestResponseLog log, int version) =>
        version < 7 && log.NamedByRemoteHostFallback && log.Uri.IsAbsoluteUri
            ? "localhost:" + log.Uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : log.ServiceName;

    private static string MethodOf(RequestResponseLog log) => MethodOf(log.Method);

    private static string MethodOf(OneOf<HttpMethod, string> method) => method.Value switch
    {
        HttpMethod http => http.Method,
        var other => other?.ToString() ?? ""
    };

    /// <summary>
    /// The templated first line of a statement, cut to a length a shape can carry: what tells one statement on a
    /// connection from another (the warm-up rule's shapes use it too, <see cref="Reports.WarmUpCalls"/>).
    /// </summary>
    internal static string StatementHead(string content, HistoryShapeRules? rules) =>
        // Templated before it is cut: cut first, an id straddling the limit kept its first characters, and
        // a scenario writing a fresh id read as behaviour-changed on every run.
        FailureText.Truncate(TemplateStatement(StatementLine(content), rules), HeadLength);

    /// <summary>
    /// The part of a statement <see cref="StatementHead"/> templates: its first line, cut. Read again it is unchanged,
    /// so a fragment carries this rather than the statement (<see cref="HistoryRawCall.Head"/>).
    /// </summary>
    internal static string StatementLine(string content) => FailureText.Truncate(FailureText.FirstLine(content), HeadRaw);

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

    /// <summary>
    /// The <c>caller&gt;service</c> pairs a released rule recorded differently from the current one: each pair as it is
    /// recorded now, mapped to the pair <paramref name="version"/> recorded. Empty when the rule renames nothing here.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> RenamedDependencies(IEnumerable<RequestResponseLog?> logs, int version)
    {
        ArgumentNullException.ThrowIfNull(logs);
        var renamed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var log in logs)
        {
            if (log is null || log.TrackingIgnore || log.IsDiagramMarker || string.IsNullOrEmpty(log.CallerName) || string.IsNullOrEmpty(log.ServiceName))
                continue;
            var then = ServiceNameUnder(log, version);
            if (!string.Equals(then, log.ServiceName, StringComparison.Ordinal))
                renamed[log.CallerName + ">" + log.ServiceName] = log.CallerName + ">" + then;
        }
        return renamed;
    }

    /// <summary>The first eight hex of SHA-256 over <paramref name="text"/> — the ledger's short hash.</summary>
    public static string Hash8(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
