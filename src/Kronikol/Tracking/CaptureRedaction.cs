using System.Text.RegularExpressions;

namespace Kronikol.Tracking;

/// <summary>
/// Capture-time redaction applied by <see cref="RequestResponseLogger.Log"/> before a
/// <see cref="RequestResponseLog"/> is stored. Unlike <c>ReportConfigurationOptions.ExcludedHeaders</c>
/// and the content processors — which only affect what the PlantUML diagram <em>shows</em> — this
/// runs before anything is enqueued, so a redacted value never reaches the in-memory store, the
/// <c>TestRunReport.json</c> data file, the mergeable JSON, or an NDJSON capture file.
/// </summary>
/// <remarks>
/// Assign an instance to <see cref="RequestResponseLogger.Redaction"/> (e.g. <c>CaptureRedaction.Secrets()</c>)
/// to enable it process-wide. The default instance redacts the well-known credential-bearing headers
/// (<see cref="DefaultSecretHeaders"/>) and leaves content untouched; add <see cref="ContentPatterns"/> for
/// token/connection-string patterns inside bodies, or set <see cref="Custom"/> for anything bespoke.
/// Header matching is case-insensitive.
/// </remarks>
public sealed class CaptureRedaction
{
    /// <summary>Headers that carry credentials and must never be written to disk.</summary>
    public static readonly string[] DefaultSecretHeaders =
    [
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
        "x-api-key",
        "x-auth-token",
        "api-key",
        "x-amz-security-token",
        "x-goog-api-key",
    ];

    /// <summary>Creates a redaction policy for the given headers (defaults to <see cref="DefaultSecretHeaders"/>).</summary>
    public CaptureRedaction(IEnumerable<string>? headers = null)
    {
        Headers = new HashSet<string>(headers ?? DefaultSecretHeaders, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The secure preset: <see cref="DefaultSecretHeaders"/> replaced with <see cref="Replacement"/>.</summary>
    public static CaptureRedaction Secrets() => new();

    /// <summary>Header names (case-insensitive) whose values are redacted or dropped.</summary>
    public HashSet<string> Headers { get; }

    /// <summary>The value written in place of a redacted header value or matched content. Default <c>[REDACTED]</c>.</summary>
    public string Replacement { get; set; } = "[REDACTED]";

    /// <summary>
    /// When <c>true</c>, matching headers are removed entirely instead of having their value replaced.
    /// Default <c>false</c> (the header name stays visible so the diagram still shows the call was
    /// authenticated, without the credential).
    /// </summary>
    public bool DropHeaders { get; set; }

    /// <summary>
    /// Regular expressions applied to request/response <see cref="RequestResponseLog.Content"/>; every
    /// match is replaced with <see cref="Replacement"/> (or the pattern's own replacement when given).
    /// Use this for bearer tokens, connection strings or API keys that appear inside bodies.
    /// </summary>
    public List<(Regex Pattern, string? Replacement)> ContentPatterns { get; } = [];

    /// <summary>
    /// Optional last-stage hook. Receives the already header/content-redacted log and returns the log to
    /// store, or <c>null</c> to drop the entry entirely.
    /// </summary>
    public Func<RequestResponseLog, RequestResponseLog?>? Custom { get; set; }

    /// <summary>Adds a content pattern (see <see cref="ContentPatterns"/>). Returns this instance for chaining.</summary>
    public CaptureRedaction RedactContent(Regex pattern, string? replacement = null)
    {
        ContentPatterns.Add((pattern, replacement));
        return this;
    }

    /// <summary>Adds a content pattern from a regular-expression string (case-insensitive). Returns this instance for chaining.</summary>
    public CaptureRedaction RedactContent(string pattern, string? replacement = null) =>
        RedactContent(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), replacement);

    /// <summary>Applies the policy to a log entry. Returns the redacted entry, or <c>null</c> when the entry should be dropped.</summary>
    public RequestResponseLog? Apply(RequestResponseLog log)
    {
        var result = log;

        var headers = RedactHeaders(log.Headers);
        var content = RedactContentText(log.Content);
        if (!ReferenceEquals(headers, log.Headers) || !ReferenceEquals(content, log.Content))
            result = result with { Headers = headers, Content = content };

        if (log.SetupVariant is { } setup)
        {
            var variant = RedactVariant(setup);
            if (!ReferenceEquals(variant, setup))
                result = result with { SetupVariant = variant };
        }

        if (log.ActionVariant is { } action)
        {
            var variant = RedactVariant(action);
            if (!ReferenceEquals(variant, action))
                result = result with { ActionVariant = variant };
        }

        return Custom is null ? result : Custom(result);
    }

    /// <summary>Redacts a header collection according to this policy (exposed for capturers that build headers themselves).</summary>
    public (string Key, string? Value)[] RedactHeaders((string Key, string? Value)[] headers)
    {
        if (headers.Length == 0 || Headers.Count == 0)
            return headers;

        var touched = false;
        var result = new List<(string Key, string? Value)>(headers.Length);
        foreach (var header in headers)
        {
            if (Headers.Contains(header.Key))
            {
                touched = true;
                if (!DropHeaders)
                    result.Add((header.Key, Replacement));
            }
            else
            {
                result.Add(header);
            }
        }

        return touched ? result.ToArray() : headers;
    }

    /// <summary>Whether a header name is in the denylist.</summary>
    public bool IsSecretHeader(string name) => Headers.Contains(name);

    private string? RedactContentText(string? content)
    {
        if (string.IsNullOrEmpty(content) || ContentPatterns.Count == 0)
            return content;

        var result = content;
        foreach (var (pattern, replacement) in ContentPatterns)
            result = pattern.Replace(result, replacement ?? Replacement);
        return result;
    }

    private PhaseVariant RedactVariant(PhaseVariant variant)
    {
        var headers = RedactHeaders(variant.Headers);
        var content = RedactContentText(variant.Content);
        return ReferenceEquals(headers, variant.Headers) && ReferenceEquals(content, variant.Content)
            ? variant
            : variant with { Headers = headers, Content = content };
    }
}
