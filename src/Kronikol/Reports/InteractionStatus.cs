using System.Globalization;
using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// Splits a captured status into the two things a consumer actually wants: the number, and the label.
///
/// <para><b>Why it was one field and why that failed.</b> <c>RequestResponseLog.StatusCode</c> is a
/// <c>OneOf&lt;HttpStatusCode, string&gt;</c>, and every writer emitted
/// <c>StatusCode?.Value?.ToString()</c> — which is the enum <i>name</i> when .NET has one
/// (<c>"OK"</c>, <c>"BadRequest"</c>) and the raw text when it does not (<c>"Ack"</c>, <c>"Responded"</c>,
/// a bare <c>"599"</c> from a tap that only had a number). So the field's type depended on whether .NET
/// happened to name the code: <c>--status 5xx</c> matched a 599 and missed a 500, <c>--status 400</c>
/// matched nothing at all, and the only way to ask about a bad request was to know that .NET spells it
/// <c>BadRequest</c>. Measured before the change: <c>--status 4xx</c> and <c>--status 400</c> both
/// returned "nothing matched" on a report containing a <c>BadRequest</c>.</para>
///
/// <para><b>The shape now.</b> <c>statusCode</c> is the number or null; <c>statusText</c> is the label or
/// null. An HTTP response carries both (<c>400</c> + <c>"BadRequest"</c>). A broker acknowledgement
/// carries text only (<c>null</c> + <c>"Ack"</c>). A numeric-only tap carries both, with the text being
/// the .NET name when one exists and the digits otherwise — so grepping for a name still works and
/// filtering by range finally does.</para>
/// </summary>
public static class InteractionStatus
{
    /// <summary>The numeric code and the display label for a captured status. Both may be null.</summary>
    public static (int? Code, string? Text) Split(OneOf<HttpStatusCode, string>? status)
    {
        switch (status?.Value)
        {
            case null:
                return (null, null);

            case HttpStatusCode code:
                return ((int)code, code.ToString());

            case var value:
            {
                var text = value.ToString();
                if (string.IsNullOrEmpty(text)) return (null, null);

                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                    return (null, text);

                // A tap that captured only a number still gets the name when .NET has one, so a reader
                // grepping for `NotFound` finds a 404 whichever side it was captured from.
                var named = Enum.IsDefined(typeof(HttpStatusCode), numeric)
                    ? ((HttpStatusCode)numeric).ToString()
                    : text;
                return (numeric, named);
            }
        }
    }

    /// <summary>
    /// Reads a status back out of a written report, accepting both the current shape and the pre-3.1.0
    /// one where <c>statusCode</c> held the name. A reader that only understood the new shape would
    /// answer "no status" for every report ever written before this release.
    /// </summary>
    public static (int? Code, string? Text) Read(string? statusCode, string? statusText)
    {
        if (statusText is not null || statusCode is null)
        {
            return (int.TryParse(statusCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : null, statusText);
        }

        // Pre-3.1.0: one field, holding a name or a number.
        return int.TryParse(statusCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacy)
            ? (legacy, Enum.IsDefined(typeof(HttpStatusCode), legacy) ? ((HttpStatusCode)legacy).ToString() : statusCode)
            : (Enum.TryParse<HttpStatusCode>(statusCode, out var parsed) ? (int)parsed : null, statusCode);
    }

    /// <summary>
    /// The statuses that are not numbers and are not failures. The HTTP non-200 successes, and then the
    /// labels Kronikol itself stamps on calls that have no status code: a broker publish is <c>Sent</c>,
    /// a consume <c>Ack</c>, a reply <c>Responded</c> (<c>MessageTracker</c>'s own defaults), a cache
    /// lookup <c>Hit</c> or <c>Miss</c> - a miss is an outcome, not a failure - and a Spanner
    /// transaction <c>Committed</c>. A refusal (<c>Nack</c>, <c>Fault</c>) is deliberately absent.
    /// </summary>
    private static readonly HashSet<string> NonErrorStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Created", "Accepted", "NoContent",
        "Sent", "Ack", "Responded", "Hit", "Miss", "Committed",
    };

    /// <summary>
    /// Treats anything that is not a success as an error, including the non-numeric statuses the non-HTTP
    /// taps use (a database driver reports <c>ERROR</c>, not 500) - while knowing the successes that are
    /// not spelled <c>OK</c> by name (<see cref="NonErrorStatuses"/>).
    ///
    /// <para>It lives here rather than in the tool because the failures digest asks the same question when
    /// it decides which of a scenario's calls to show first, and a digest that called a <c>Miss</c> an
    /// error while <c>query services</c> did not would be two surfaces of one report disagreeing about one
    /// call. One classifier behind <c>services</c>, <c>flow --errors-only</c>, <c>--group-by</c> and the
    /// digest.</para>
    /// </summary>
    public static bool IsError(string? statusCode, string? statusText = null)
    {
        var (code, text) = Read(statusCode, statusText);

        // A number settles it on its own: from 3.1.0 every HTTP call has one, so the name list above is
        // only ever consulted for the taps that genuinely have no code.
        if (code is { } numeric) return numeric >= 400;

        return text is { Length: > 0 }
               && !text.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
               && !NonErrorStatuses.Contains(text);
    }
}
