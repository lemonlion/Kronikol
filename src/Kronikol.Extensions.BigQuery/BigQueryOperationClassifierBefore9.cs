using System.Text.RegularExpressions;

namespace Kronikol.Extensions.BigQuery;

/// <summary>
/// The classifier, label and clean URI as every release before #125's fix recorded a call, kept unchanged so
/// history's earlier rules can read a call as it was recorded then (<see cref="Tracking.RequestResponseLog.RecordedBefore9"/>,
/// plans/EMULATOR_URL_SHAPES_PLAN.md section 7). It is frozen: an edit here changes what those rules read, and every
/// scenario whose calls it touches would read as changed. <c>RecordedBefore9Tests</c> pins it to what the
/// last package published before the fix recorded.
/// </summary>
internal static partial class BigQueryOperationClassifierBefore9
{
    // Matches BigQuery REST API paths, including the optional /upload/ prefix.
    // /bigquery/v2/projects/{project}/{resource-path}
    // /upload/bigquery/v2/projects/{project}/{resource-path}
    [GeneratedRegex(
        @"^/(?:upload/)?bigquery/v2/projects/(?<project>[^/]+)/(?<rest>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BigQueryPathRegex();

    internal static BigQueryOperationInfo Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var method = request.Method.Method.ToUpperInvariant();

        var match = BigQueryPathRegex().Match(path);
        if (!match.Success)
            return new BigQueryOperationInfo(BigQueryOperation.Other, null, null, null, null);

        var project = match.Groups["project"].Value;
        var rest = match.Groups["rest"].Value;

        // Strip trailing query string remnants and split into segments
        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return ClassifyFromSegments(method, segments, project);
    }

    internal static string? GetDiagramLabel(BigQueryOperationInfo op, BigQueryTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            BigQueryTrackingVerbosity.Summarised or BigQueryTrackingVerbosity.Detailed => op.Operation.ToString(),
            _ => null
        };
    }

    private static BigQueryOperationInfo ClassifyFromSegments(string method, string[] segments, string project)
    {
        if (segments.Length == 0)
            return new BigQueryOperationInfo(BigQueryOperation.Other, null, null, project, null);

        return segments[0].ToLowerInvariant() switch
        {
            "queries" => ClassifyQuery(method, segments, project),
            "datasets" => ClassifyDataset(method, segments, project),
            "jobs" => ClassifyJob(method, segments, project),
            _ => new BigQueryOperationInfo(BigQueryOperation.Other, null, null, project, null)
        };
    }

    private static BigQueryOperationInfo ClassifyQuery(string method, string[] segments, string project)
    {
        // POST /queries → run query
        // GET  /queries/{jobId} → get query results
        var resourceName = segments.Length > 1 ? segments[1] : null;
        return new BigQueryOperationInfo(BigQueryOperation.Query, "query", resourceName, project, null);
    }

    private static BigQueryOperationInfo ClassifyDataset(string method, string[] segments, string project)
    {
        // /datasets → list or create
        if (segments.Length == 1)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, "dataset", null, project, null);
        }

        var datasetId = segments[1];

        // /datasets/{id} → CRUD on dataset itself
        if (segments.Length == 2)
        {
            var op = method switch
            {
                "DELETE" => BigQueryOperation.Delete,
                "PUT" or "PATCH" => BigQueryOperation.Update,
                _ => BigQueryOperation.Read
            };
            return new BigQueryOperationInfo(op, "dataset", datasetId, project, datasetId);
        }

        // /datasets/{id}/{sub-resource}/...
        return segments[2].ToLowerInvariant() switch
        {
            "tables" => ClassifyTable(method, segments, project, datasetId),
            "models" => ClassifySubResource(method, segments, project, datasetId, "model", 3),
            "routines" => ClassifySubResource(method, segments, project, datasetId, "routine", 3),
            _ => new BigQueryOperationInfo(BigQueryOperation.Other, null, null, project, datasetId)
        };
    }

    private static BigQueryOperationInfo ClassifyTable(string method, string[] segments, string project, string datasetId)
    {
        // /datasets/{ds}/tables → list or create
        if (segments.Length == 3)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, "table", null, project, datasetId);
        }

        var tableId = segments[3];

        // /datasets/{ds}/tables/{tbl}/insertAll
        if (segments.Length > 4 && segments[4].Equals("insertAll", StringComparison.OrdinalIgnoreCase))
            return new BigQueryOperationInfo(BigQueryOperation.Insert, "table", tableId, project, datasetId);

        // /datasets/{ds}/tables/{tbl}/data
        if (segments.Length > 4 && segments[4].Equals("data", StringComparison.OrdinalIgnoreCase))
            return new BigQueryOperationInfo(BigQueryOperation.List, "tabledata", tableId, project, datasetId);

        // /datasets/{ds}/tables/{tbl} → CRUD
        var tableOp = method switch
        {
            "DELETE" => BigQueryOperation.Delete,
            "PUT" or "PATCH" => BigQueryOperation.Update,
            _ => BigQueryOperation.Read
        };
        return new BigQueryOperationInfo(tableOp, "table", tableId, project, datasetId);
    }

    private static BigQueryOperationInfo ClassifySubResource(
        string method, string[] segments, string project, string datasetId,
        string resourceType, int resourceIndex)
    {
        // /datasets/{ds}/{type} → list
        if (segments.Length <= resourceIndex)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, resourceType, null, project, datasetId);
        }

        var resourceName = segments[resourceIndex];

        var subOp = method switch
        {
            "DELETE" => BigQueryOperation.Delete,
            "PUT" or "PATCH" => BigQueryOperation.Update,
            "POST" => BigQueryOperation.Create,
            _ => BigQueryOperation.Read
        };
        return new BigQueryOperationInfo(subOp, resourceType, resourceName, project, datasetId);
    }

    private static BigQueryOperationInfo ClassifyJob(string method, string[] segments, string project)
    {
        // /jobs → list or create
        if (segments.Length == 1)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, "job", null, project, null);
        }

        var jobId = segments[1];

        // /jobs/{id}/cancel
        if (segments.Length > 2 && segments[2].Equals("cancel", StringComparison.OrdinalIgnoreCase))
            return new BigQueryOperationInfo(BigQueryOperation.Cancel, "job", jobId, project, null);

        // /jobs/{id}/delete
        if (segments.Length > 2 && segments[2].Equals("delete", StringComparison.OrdinalIgnoreCase))
            return new BigQueryOperationInfo(BigQueryOperation.Delete, "job", jobId, project, null);

        // /jobs/{id} → read or delete
        var jobOp = method switch
        {
            "DELETE" => BigQueryOperation.Delete,
            _ => BigQueryOperation.Read
        };
        return new BigQueryOperationInfo(jobOp, "job", jobId, project, null);
    }

    internal static Uri BuildCleanUri(Uri originalUri, BigQueryOperationInfo op, BigQueryTrackingVerbosity verbosity)
    {
        if (op.ResourceType is null)
            return originalUri;

        if (verbosity == BigQueryTrackingVerbosity.Summarised)
        {
            var path = op.ResourceName is not null
                ? $"/{op.ResourceType}/{op.ResourceName}"
                : $"/{op.ResourceType}";
            return new UriBuilder(originalUri) { Path = path, Query = "" }.Uri;
        }

        // Detailed: include dataset context if available
        var parts = new List<string>();
        if (op.DatasetId is not null)
            parts.Add(op.DatasetId);
        if (op.ResourceType != "dataset" || op.ResourceName is null)
            parts.Add(op.ResourceType);
        if (op.ResourceName is not null)
            parts.Add(op.ResourceName);

        var cleanPath = "/" + string.Join("/", parts);
        return new UriBuilder(originalUri) { Path = cleanPath, Query = "" }.Uri;
    }
}
