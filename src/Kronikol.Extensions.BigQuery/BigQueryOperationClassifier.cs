namespace Kronikol.Extensions.BigQuery;

/// <summary>
/// Classifies Google BigQuery HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
/// <remarks>
/// The handler is not given the client's base URI, which an emulator, a gateway or a proxy replaces, so the API's own
/// path is found in the URL rather than assumed at the production base: every BigQuery method's path begins
/// <c>projects</c>, followed by a project and one of the project's collections, or ends there (<c>projects.list</c>).
/// Whatever precedes that is the client's base (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 5.1).
/// </remarks>
public static class BigQueryOperationClassifier
{
    // What a project holds, as the API's methods name it: the API path starts at a `projects` segment followed by a
    // project and one of these, which keeps a dataset or a base path segment named `projects` from being taken for it.
    private static readonly HashSet<string> ProjectCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "datasets", "jobs", "queries", "serviceAccount"
    };

    public static BigQueryOperationInfo Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var method = request.Method.Method.ToUpperInvariant();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var start = ApiPathStart(segments);
        if (start < 0)
            return Unnamed(null);

        var api = segments[start..];

        // A custom method (`:getIamPolicy`, `:undelete`, `:batchDelete`) is split off before the segments are read,
        // so a resource name never carries it.
        string? custom = null;
        var colon = api[^1].IndexOf(':');
        if (colon > 0)
        {
            custom = api[^1][(colon + 1)..];
            api[^1] = api[^1][..colon];
        }

        if (api.Length == 1)
            return method == "GET" && custom is null
                ? new BigQueryOperationInfo(BigQueryOperation.List, "project", null, null, null)
                : Unnamed(null);

        return ClassifyFromSegments(method, api[2..], api[1], custom, request.RequestUri?.Query ?? "");
    }

    /// <summary>
    /// The API's own path in <paramref name="uri"/>, from its <c>projects</c> segment, without the client's base or the
    /// query: what a call no rule names is drawn with, so it reads the same under any base. A collection the rule does not
    /// know yet (a method a later API version adds) starts at the leftmost <c>projects</c> segment with a project after
    /// it; a path with none is drawn as sent.
    /// </summary>
    internal static string ApiPathOf(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var start = ApiPathStart(segments);
        if (start < 0)
            start = Array.FindIndex(segments, 0, Math.Max(0, segments.Length - 1), s => s.Equals("projects", StringComparison.OrdinalIgnoreCase));
        return start < 0 ? uri.AbsolutePath : "/" + string.Join("/", segments[start..]);
    }

    public static string? GetDiagramLabel(BigQueryOperationInfo op, BigQueryTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            BigQueryTrackingVerbosity.Summarised or BigQueryTrackingVerbosity.Detailed => op.Operation.ToString(),
            _ => null
        };
    }

    // The index of the segment the API path starts at, or -1: the leftmost `projects` followed by a project and a
    // collection, or ending the path.
    private static int ApiPathStart(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            if (!segments[i].Equals("projects", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i == segments.Length - 1)
                return i;
            if (i + 2 < segments.Length && ProjectCollections.Contains(CollectionName(segments[i + 2])))
                return i;
        }
        return -1;
    }

    private static string CollectionName(string segment)
    {
        var colon = segment.IndexOf(':');
        return colon > 0 ? segment[..colon] : segment;
    }

    private static BigQueryOperationInfo Unnamed(string? project) =>
        new(BigQueryOperation.Other, null, null, project, null);

    private static BigQueryOperationInfo ClassifyFromSegments(string method, string[] segments, string project, string? custom, string query)
    {
        return segments[0].ToLowerInvariant() switch
        {
            "queries" => ClassifyQuery(segments, project),
            "datasets" => ClassifyDataset(method, segments, project, custom),
            "jobs" => ClassifyJob(method, segments, project, query),
            "serviceaccount" when segments.Length == 1 => new BigQueryOperationInfo(BigQueryOperation.Read, "serviceAccount", null, project, null),
            _ => Unnamed(project)
        };
    }

    private static BigQueryOperationInfo ClassifyQuery(string[] segments, string project)
    {
        // POST /queries → run query
        // GET  /queries/{jobId} → get query results
        var resourceName = segments.Length > 1 ? segments[1] : null;
        return new BigQueryOperationInfo(BigQueryOperation.Query, "query", resourceName, project, null);
    }

    private static BigQueryOperationInfo ClassifyDataset(string method, string[] segments, string project, string? custom)
    {
        // /datasets → list or create
        if (segments.Length == 1)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, "dataset", null, project, null);
        }

        var datasetId = segments[1];

        // /datasets/{id} → CRUD on dataset itself; :undelete brings it back
        if (segments.Length == 2)
        {
            if (custom is not null)
                return custom.Equals("undelete", StringComparison.OrdinalIgnoreCase)
                    ? new BigQueryOperationInfo(BigQueryOperation.Create, "dataset", datasetId, project, datasetId)
                    : Unnamed(project);

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
            "tables" => ClassifyTable(method, segments, project, datasetId, custom),
            "models" => ClassifySubResource(method, segments, project, datasetId, "model", custom),
            "routines" => ClassifySubResource(method, segments, project, datasetId, "routine", custom),
            _ => Unnamed(project)
        };
    }

    private static BigQueryOperationInfo ClassifyTable(string method, string[] segments, string project, string datasetId, string? custom)
    {
        // /datasets/{ds}/tables → list or create
        if (segments.Length == 3)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, "table", null, project, datasetId);
        }

        var tableId = segments[3];

        if (segments.Length == 4 && custom is not null)
            return IamPolicy(custom, project, datasetId, $"table/{tableId}");

        // /datasets/{ds}/tables/{tbl}/insertAll
        if (segments.Length > 4 && segments[4].Equals("insertAll", StringComparison.OrdinalIgnoreCase))
            return new BigQueryOperationInfo(BigQueryOperation.Insert, "table", tableId, project, datasetId);

        // /datasets/{ds}/tables/{tbl}/data
        if (segments.Length > 4 && segments[4].Equals("data", StringComparison.OrdinalIgnoreCase))
            return new BigQueryOperationInfo(BigQueryOperation.List, "tabledata", tableId, project, datasetId);

        // /datasets/{ds}/tables/{tbl}/rowAccessPolicies[/{policy}]
        if (segments.Length > 4 && segments[4].Equals("rowAccessPolicies", StringComparison.OrdinalIgnoreCase))
            return ClassifyRowAccessPolicy(method, segments, project, datasetId, tableId, custom);

        if (segments.Length > 4)
            return Unnamed(project);

        // /datasets/{ds}/tables/{tbl} → CRUD
        var tableOp = method switch
        {
            "DELETE" => BigQueryOperation.Delete,
            "PUT" or "PATCH" => BigQueryOperation.Update,
            _ => BigQueryOperation.Read
        };
        return new BigQueryOperationInfo(tableOp, "table", tableId, project, datasetId);
    }

    private static BigQueryOperationInfo ClassifyRowAccessPolicy(
        string method, string[] segments, string project, string datasetId, string tableId, string? custom)
    {
        var table = $"table/{tableId}";

        // .../rowAccessPolicies → list, insert, or :batchDelete
        if (segments.Length == 5)
        {
            if (custom is not null)
                return custom.Equals("batchDelete", StringComparison.OrdinalIgnoreCase)
                    ? new BigQueryOperationInfo(BigQueryOperation.Delete, "rowAccessPolicy", null, project, datasetId) { Parent = table }
                    : Unnamed(project);
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, "rowAccessPolicy", null, project, datasetId) { Parent = table };
        }

        var policyId = segments[5];
        if (segments.Length > 6)
            return Unnamed(project);
        if (custom is not null)
            return IamPolicy(custom, project, datasetId, $"{table}/rowAccessPolicy/{policyId}");

        var policyOp = method switch
        {
            "DELETE" => BigQueryOperation.Delete,
            "PUT" or "PATCH" => BigQueryOperation.Update,
            _ => BigQueryOperation.Read
        };
        return new BigQueryOperationInfo(policyOp, "rowAccessPolicy", policyId, project, datasetId) { Parent = table };
    }

    private static BigQueryOperationInfo ClassifySubResource(
        string method, string[] segments, string project, string datasetId,
        string resourceType, string? custom)
    {
        // /datasets/{ds}/{type} → list
        if (segments.Length <= 3)
        {
            var op = method == "POST" ? BigQueryOperation.Create : BigQueryOperation.List;
            return new BigQueryOperationInfo(op, resourceType, null, project, datasetId);
        }

        var resourceName = segments[3];
        if (segments.Length > 4)
            return Unnamed(project);
        if (custom is not null)
            return IamPolicy(custom, project, datasetId, $"{resourceType}/{resourceName}");

        var subOp = method switch
        {
            "DELETE" => BigQueryOperation.Delete,
            "PUT" or "PATCH" => BigQueryOperation.Update,
            "POST" => BigQueryOperation.Create,
            _ => BigQueryOperation.Read
        };
        return new BigQueryOperationInfo(subOp, resourceType, resourceName, project, datasetId);
    }

    // {resource}:getIamPolicy and :testIamPermissions read the policy on a table, a routine or a row access policy, and
    // :setIamPolicy replaces it.
    private static BigQueryOperationInfo IamPolicy(string custom, string project, string datasetId, string parent)
    {
        var op = custom.ToLowerInvariant() switch
        {
            "getiampolicy" or "testiampermissions" => BigQueryOperation.Read,
            "setiampolicy" => BigQueryOperation.Update,
            _ => BigQueryOperation.Other
        };
        return op == BigQueryOperation.Other
            ? Unnamed(project)
            : new BigQueryOperationInfo(op, "iamPolicy", null, project, datasetId) { Parent = parent };
    }

    private static BigQueryOperationInfo ClassifyJob(string method, string[] segments, string project, string query)
    {
        // /jobs → list or create; a load job's data goes up by PUT to the session the create opened
        if (segments.Length == 1)
        {
            var op = method switch
            {
                "POST" => BigQueryOperation.Create,
                "PUT" when query.Contains("upload_id=", StringComparison.Ordinal) => BigQueryOperation.Insert,
                _ => BigQueryOperation.List
            };
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
}
