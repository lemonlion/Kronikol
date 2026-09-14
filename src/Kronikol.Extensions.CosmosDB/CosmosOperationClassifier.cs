using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kronikol.Extensions.CosmosDB;

/// <summary>
/// Classifies Azure Cosmos DB HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
public static partial class CosmosOperationClassifier
{
    // Matches both named paths and _rid-encoded paths.
    // Named:  /dbs/mydb/colls/mycoll/docs/doc1
    // _rid:   /dbs/Sl8fAA==/colls/Sl8fALN4sw4=/docs
    // The resource types are: dbs, colls, docs, sprocs, triggers, udfs, pkranges, offers
    [GeneratedRegex(
        @"^/(?:dbs/(?<db>[^/]+))?" +
        @"(?:/colls/(?<coll>[^/]+))?" +
        @"(?:/(?<resourceType>docs|sprocs|triggers|udfs|pkranges)(?:/(?<resourceId>[^/]+))?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CosmosPathRegex();

    public static CosmosOperationInfo Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var method = request.Method.Method.ToUpperInvariant();

        var match = CosmosPathRegex().Match(path);

        var db = match.Groups["db"].Value;
        var coll = match.Groups["coll"].Value;
        var resourceType = match.Groups["resourceType"].Value.ToLowerInvariant();
        var resourceId = match.Groups["resourceId"].Value;

        var hasResourceId = !string.IsNullOrEmpty(resourceId);

        var isQuery = HasHeader(request, "x-ms-documentdb-isquery", "True");
        var isUpsert = HasHeader(request, "x-ms-documentdb-is-upsert", "true");
        // Before running a query the SDK fetches its plan: a POST to the documents resource that carries
        // the query text and this header, not the query header. Without the rule it read as a document
        // create, so every query in a report was preceded by a Create that never happened.
        var isQueryPlan = HasHeader(request, "x-ms-cosmos-is-query-plan-request", "True");

        var operation = (method, resourceType, hasResourceId, isQuery, isUpsert) switch
        {
            _ when isQueryPlan => CosmosOperation.Other,
            ("POST", "docs", false, false, false) => CosmosOperation.Create,
            ("POST", "docs", false, false, true) => CosmosOperation.Upsert,
            ("POST", "docs", false, true, _) => CosmosOperation.Query,
            ("GET", "docs", true, _, _) => CosmosOperation.Read,
            ("GET", "docs", false, _, _) => CosmosOperation.List,
            ("PUT", "docs", true, _, _) => CosmosOperation.Replace,
            ("PATCH", "docs", true, _, _) => CosmosOperation.Patch,
            ("DELETE", "docs", true, _, _) => CosmosOperation.Delete,
            ("POST", "sprocs", true, _, _) => CosmosOperation.ExecStoredProc,
            ("POST", "docs", true, _, _) => CosmosOperation.Batch,
            _ => CosmosOperation.Other
        };

        string? queryText = null;
        if (operation == CosmosOperation.Query)
            queryText = ExtractQueryText(request);

        return new CosmosOperationInfo(
            operation,
            NullIfEmpty(db),
            NullIfEmpty(coll),
            hasResourceId ? resourceId : null,
            queryText);
    }

    public static string GetDiagramLabel(CosmosOperationInfo op, CosmosTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            CosmosTrackingVerbosity.Summarised or CosmosTrackingVerbosity.Detailed => op.Operation.ToString(),

            // Raw: fall through to the standard method + path used by the base handler
            _ => null!
        };
    }

    private static bool HasHeader(HttpRequestMessage request, string name, string expectedValue)
    {
        return request.Headers.TryGetValues(name, out var values)
               && values.Any(v => v.Equals(expectedValue, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractQueryText(HttpRequestMessage request)
    {
        if (request.Content is null)
            return null;

        // The content may already have been read, so we read it synchronously from the buffered copy
        var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(content))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("query", out var q) ? q.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}