using System.Diagnostics;

namespace Kronikol.InternalFlow;

/// <summary>
/// The name a span's swimlane, the granularity filters and the discovered sources read: its <see cref="ActivitySource"/>'s
/// name, or, for an activity made without one, a name taken from its operation (#138).
/// </summary>
/// <remarks>
/// An activity made with <c>new Activity(name)</c> belongs to .NET's unnamed default source, whose name is empty. Code
/// still makes activities that way: HttpClient whenever nothing hears <c>System.Net.Http</c>, the Azure SDK unless its
/// clients use their sources, Application Insights' own. Such a span was drawn in an "Unknown" lane and matched no filter.
/// One rule here keeps the lane, both filters and the diagnostics report in agreement.
/// </remarks>
internal static class SpanSource
{
    /// <summary>HttpClient's source, whose span <c>DiagnosticsHandler</c> starts for each call it sends.</summary>
    internal const string HttpClient = "System.Net.Http";

    /// <summary>
    /// The activities whose source is known, so that each lands in the same lane whichever way it was made. The second and
    /// third are a safeguard: the span listener hears their sources, so their legacy forms appear only in a process where
    /// it has not started yet.
    /// </summary>
    private static readonly Dictionary<string, string> KnownOperations = new(StringComparer.Ordinal)
    {
        ["System.Net.Http.HttpRequestOut"] = HttpClient,
        ["Microsoft.AspNetCore.Hosting.HttpRequestIn"] = "Microsoft.AspNetCore",
        ["Grpc.Net.Client.GrpcOut"] = "Grpc.Net.Client",
    };

    internal static string Of(Activity activity) => Of(activity.Source.Name, activity.OperationName);

    /// <summary>
    /// The source's name when there is one. Otherwise the source a known operation comes from, else the operation's name up
    /// to its last dot (<c>Microsoft.ApplicationInsights.Init</c> is <c>Microsoft.ApplicationInsights</c>'s), else the
    /// operation's name itself.
    /// </summary>
    internal static string Of(string? sourceName, string? operationName)
    {
        if (!string.IsNullOrEmpty(sourceName))
            return sourceName;
        if (string.IsNullOrEmpty(operationName))
            return "";
        if (KnownOperations.TryGetValue(operationName, out var known))
            return known;
        var lastDot = operationName.LastIndexOf('.');
        return lastDot > 0 ? operationName[..lastDot] : operationName;
    }
}
